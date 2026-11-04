using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using NoticeSender.Automation;

namespace NoticeSender.Windows;

internal sealed record VisualAttachmentReceipt(string Name, long Length);

internal sealed class AttachmentVisualRecognizer
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_.";
    private readonly string executable;
    private readonly string tessdata;
    internal List<string> LastDiagnostics { get; } = new();
    internal AttachmentVisualRecognizer(string tesseractExe, string tessdataDirectory)
    {
        executable = Path.GetFullPath(tesseractExe);
        tessdata = Path.GetFullPath(tessdataDirectory);
        RequireHash(executable, "BABB405F4366B480D02CD8FF2BAC8D497170F6C1711CE6F3D5D8BF0FB7FA6ED9");
        RequireHash(Path.Combine(tessdata, "eng.traineddata"), "7D4322BD2A7749724879683FC3912CB542F19906C83BCC1A52132556427170B2");
        RequireHash(Path.Combine(tessdata, "kor.traineddata"), "6B85E11D9BBF07863B97B3523B1B112844C43E713DF8B66418A081FD1060B3B2");
    }

    // Input must be a fresh capture of the identity-validated native transcript HWND.
    // This parser has no window lookup, input dispatch, or application-control capability.
    internal IReadOnlyList<VisualAttachmentReceipt> ReadFileReceipts(byte[] exactTranscriptPng, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = new MemoryStream(exactTranscriptPng, false);
        using var bitmap = new Bitmap(stream);
        var image = Raster.FromBitmap(bitmap);
        LastDiagnostics.Clear();
        LastDiagnostics.Add($"Image {image.Width}x{image.Height}; pixelhash {PixelHash(image)}");
        var receipts = new List<VisualAttachmentReceipt>();
        foreach (var region in Components(image, color => color == 0xFFFFFF, cancellationToken))
        {
            var bounds = region.Bounds;
            if (bounds.Width < 140 || bounds.Height < 55 || region.Count / (double)(bounds.Width * bounds.Height) <= .7) continue;
            var card = image.Crop(bounds);
            LastDiagnostics.Add($"Card {bounds}; count {region.Count}; pixelhash {PixelHash(card)}");
            Rectangle? titleBounds = TitleBounds(card, cancellationToken);
            LastDiagnostics.Add($"TitleBounds {titleBounds}");
            if (titleBounds is null) continue;
            // A single fixed identifier pipeline prevents accepting both 5 and S from different variants.
            var title = Preprocess(card.Crop(titleBounds.Value), 180);
            LastDiagnostics.Add($"TitleRaster {title.Width}x{title.Height}; pixelhash {PixelHash(title)}");
            string name = Compact(Ocr(title, "eng", true, cancellationToken));
            LastDiagnostics.Add("TitleOCR " + JsonSerializer.Serialize(name));
            if (!Regex.IsMatch(name, @"\A[A-Za-z0-9_.-]+\.[A-Za-z0-9]{1,10}\z", RegexOptions.CultureInvariant)) continue;
            var labels = new[] {
                Compact(Ocr(Preprocess(card, null), "eng+kor", false, cancellationToken)),
                Compact(Ocr(Preprocess(card, 180), "eng+kor", false, cancellationToken))
            };
            LastDiagnostics.Add("Labels " + JsonSerializer.Serialize(labels));
            if (labels.Any(label => new[] { "업로드중", "전송중", "실패", "오류", "취소" }.Any(label.Contains))) continue;
            if (!labels.Any(label => label.Contains("유효기간:", StringComparison.Ordinal) && label.Contains("열기", StringComparison.Ordinal) && label.Contains("폴더열기", StringComparison.Ordinal))) continue;
            var sizes = labels.SelectMany(label => Regex.Matches(label, @"용량:(\d+)bytes", RegexOptions.CultureInvariant).Select(match => match.Groups[1].Value))
                .Distinct(StringComparer.Ordinal).ToArray();
            LastDiagnostics.Add("Sizes " + JsonSerializer.Serialize(sizes));
            if (sizes.Length != 1 || !long.TryParse(sizes[0], NumberStyles.None, CultureInfo.InvariantCulture, out long length)) continue;
            receipts.Add(new VisualAttachmentReceipt(name, length));
        }
        return receipts;
    }
    private static string PixelHash(Raster image)
    {
        var bytes = new byte[image.Pixels.Length * 4];
        Buffer.BlockCopy(image.Pixels, 0, bytes, 0, bytes.Length);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    internal static bool SupportsFileName(string name) => Regex.IsMatch(name, @"\A[A-Za-z0-9_.-]+\.[A-Za-z0-9]{1,10}\z", RegexOptions.CultureInvariant);

    private static readonly Lazy<HashSet<char>> AmbiguousGlyphs = new(BuildAmbiguousGlyphs);

    internal static bool SupportsRenderedFileName(string name)
    {
        if (name.Length is < 1 or > 255 || name is "." or ".." || name.EndsWith(".", StringComparison.Ordinal)) return false;
        if (name.Any(c => !Alphabet.Contains(c) && (c < '\uAC00' || c > '\uD7A3'))) return false;
        return !name.Any(AmbiguousGlyphs.Value.Contains);
    }

    // Compare only the supplied manifest names against the complete rendered title pixels.
    // There is no text correction, partial-name acceptance, or routing based on image content.
    internal IReadOnlyList<VisualAttachmentReceipt> ReadExpectedFileReceipts(byte[] exactTranscriptPng, IReadOnlyList<VisualAttachmentReceipt> expected, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        LastDiagnostics.Clear();
        if (expected.Any(file => file.Length < 0 || !SupportsRenderedFileName(file.Name)) || expected.Select(file => file.Name).Distinct(StringComparer.Ordinal).Count() != expected.Count)
            throw new InvalidOperationException("정확한 시각 확인을 지원하지 않는 첨부 파일명입니다.");
        using var stream = new MemoryStream(exactTranscriptPng, false);
        using var bitmap = new Bitmap(stream);
        var image = Raster.FromBitmap(bitmap);
        var result = new List<VisualAttachmentReceipt>();
        foreach (var region in Components(image, color => color == 0xFFFFFF, token))
        {
            Rectangle bounds = region.Bounds;
            if (bounds.Width < 140 || bounds.Height < 55 || region.Count / (double)(bounds.Width * bounds.Height) <= .7) continue;
            var card = image.Crop(bounds);
            Rectangle? titleBounds = TitleBounds(card, token);
            if (titleBounds is null) continue;
            Raster? actualTitle = Tight(card.Crop(titleBounds.Value));
            if (actualTitle is null) continue;
            using var renderer = new MemoryFontRenderer(titleBounds.Value.Right, actualTitle.Height + 32);
            var matches = new List<VisualAttachmentReceipt>();
            foreach (var file in expected)
            {
                token.ThrowIfCancellationRequested();
                bool exact = false;
                for (int width = actualTitle.Width; width <= titleBounds.Value.Right; width++)
                {
                    token.ThrowIfCancellationRequested();
                    Raster? rendered = renderer.DrawWrapped(file.Name, width);
                    if (rendered is null) continue;
                    rendered = Tight(rendered);
                    if (rendered is not null && rendered.Width == actualTitle.Width && rendered.Height == actualTitle.Height && rendered.Pixels.AsSpan().SequenceEqual(actualTitle.Pixels)) { exact = true; break; }
                }
                if (exact) matches.Add(file);
            }
            LastDiagnostics.Add($"RenderedCard {bounds}; title {actualTitle.Width}x{actualTitle.Height}; exact manifest matches {matches.Count}");
            if (matches.Count != 1) continue;
            var labels = new[] { Compact(Ocr(Preprocess(card, null), "eng+kor", false, token)), Compact(Ocr(Preprocess(card, 180), "eng+kor", false, token)) };
            if (labels.Any(label => new[] { "업로드중", "전송중", "실패", "오류", "취소" }.Any(label.Contains))) continue;
            if (!labels.Any(label => label.Contains("유효기간:", StringComparison.Ordinal) && label.Contains("열기", StringComparison.Ordinal) && label.Contains("폴더열기", StringComparison.Ordinal))) continue;
            var sizes = labels.SelectMany(label => Regex.Matches(label, @"용량:(\d+)bytes", RegexOptions.CultureInvariant).Select(match => match.Groups[1].Value)).Distinct(StringComparer.Ordinal).ToArray();
            if (sizes.Length == 1 && long.TryParse(sizes[0], NumberStyles.None, CultureInfo.InvariantCulture, out long length) && length == matches[0].Length) result.Add(matches[0]);
        }
        return result;
    }

    private static Raster? Tight(Raster image)
    {
        int left = image.Width, top = image.Height, right = -1, bottom = -1;
        for (int y = 0; y < image.Height; y++)
        for (int x = 0; x < image.Width; x++)
            if (Max(image[x, y]) < 200) { left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y); }
        return right < 0 ? null : image.Crop(Rectangle.FromLTRB(left, top, right + 1, bottom + 1));
    }

    private static HashSet<char> BuildAmbiguousGlyphs()
    {
        // Recompute for the installed font rather than assuming a particular font-file version.
        using var renderer = new MemoryFontRenderer(32, 24);
        var seen = new Dictionary<string, char>(StringComparer.Ordinal);
        var ambiguous = new HashSet<char>();
        foreach (char c in Alphabet.Concat(Enumerable.Range(0xAC00, 0xD7A4 - 0xAC00).Select(value => (char)value)))
        {
            string hash = PixelHash(renderer.DrawCharacter(c));
            if (seen.TryGetValue(hash, out char prior)) { ambiguous.Add(c); ambiguous.Add(prior); }
            else seen.Add(hash, c);
        }
        return ambiguous;
    }

    private sealed class MemoryFontRenderer : IDisposable
    {
        private readonly int width, height;
        private readonly nint dc, bitmap, font, oldBitmap, oldFont, bits;
        private readonly int[] white, pixels;
        internal MemoryFontRenderer(int width, int height)
        {
            this.width = width; this.height = height;
            dc = Gdi.CreateCompatibleDC(0);
            if (dc == 0) throw new InvalidOperationException("파일명 렌더링 메모리 DC를 만들 수 없습니다.");
            var info = new Gdi.BitmapInfo { Header = new Gdi.BitmapInfoHeader { Size = (uint)Marshal.SizeOf<Gdi.BitmapInfoHeader>(), Width = width, Height = -height, Planes = 1, BitCount = 32, SizeImage = (uint)(width * height * 4) } };
            bitmap = Gdi.CreateDIBSection(dc, ref info, 0, out bits, 0, 0);
            if (bitmap == 0 || bits == 0) { Gdi.DeleteDC(dc); throw new InvalidOperationException("파일명 렌더링 메모리 비트맵을 만들 수 없습니다."); }
            oldBitmap = Gdi.SelectObject(dc, bitmap);
            font = Gdi.CreateFontW(-12, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 6, 0, "맑은 고딕");
            if (font == 0) { Gdi.SelectObject(dc, oldBitmap); Gdi.DeleteObject(bitmap); Gdi.DeleteDC(dc); throw new InvalidOperationException("파일명 렌더링 글꼴을 만들 수 없습니다."); }
            oldFont = Gdi.SelectObject(dc, font);
            Gdi.SetTextColor(dc, 0); Gdi.SetBkColor(dc, 0xFFFFFF); Gdi.SetBkMode(dc, 1);
            white = new int[width * height]; Array.Fill(white, -1); pixels = new int[white.Length];
        }
        private Raster Snapshot()
        {
            if (!Gdi.GdiFlush()) throw new InvalidOperationException("파일명 렌더링 결과를 완료할 수 없습니다.");
            Marshal.Copy(bits, pixels, 0, pixels.Length);
            var result = new Raster(width, height);
            for (int i = 0; i < pixels.Length; i++) result.Pixels[i] = pixels[i] & 0xFFFFFF;
            return result;
        }
        internal Raster DrawCharacter(char c)
        {
            Marshal.Copy(white, 0, bits, white.Length);
            if (!Gdi.TextOutW(dc, 0, 0, c.ToString(), 1)) throw new InvalidOperationException("파일명 글리프 렌더링에 실패했습니다.");
            return Snapshot();
        }
        internal Raster? DrawWrapped(string text, int wrapWidth)
        {
            const uint format = 0x10 | 0x2000 | 0x800; // WORDBREAK, EDITCONTROL, NOPREFIX.
            var measured = new Gdi.Rect { Right = wrapWidth, Bottom = height };
            if (Gdi.DrawTextW(dc, text, text.Length, ref measured, format | 0x400) == 0 || measured.Bottom > height || measured.Right > width) return null;
            Marshal.Copy(white, 0, bits, white.Length);
            var bounds = new Gdi.Rect { Right = wrapWidth, Bottom = height };
            if (Gdi.DrawTextW(dc, text, text.Length, ref bounds, format) == 0) return null;
            return Snapshot();
        }
        public void Dispose()
        {
            Gdi.SelectObject(dc, oldFont); Gdi.SelectObject(dc, oldBitmap); Gdi.DeleteObject(font); Gdi.DeleteObject(bitmap); Gdi.DeleteDC(dc);
        }
    }

    private static class Gdi
    {
        [StructLayout(LayoutKind.Sequential)] internal struct Rect { internal int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] internal struct BitmapInfoHeader { internal uint Size; internal int Width, Height; internal ushort Planes, BitCount; internal uint Compression, SizeImage; internal int XPelsPerMeter, YPelsPerMeter; internal uint ClrUsed, ClrImportant; }
        [StructLayout(LayoutKind.Sequential)] internal struct BitmapInfo { internal BitmapInfoHeader Header; internal uint Color1, Color2, Color3; }
        [DllImport("gdi32.dll")] internal static extern nint CreateCompatibleDC(nint dc);
        [DllImport("gdi32.dll")] internal static extern nint CreateDIBSection(nint dc, ref BitmapInfo info, uint usage, out nint bits, nint section, uint offset);
        [DllImport("gdi32.dll")] internal static extern nint SelectObject(nint dc, nint value);
        [DllImport("gdi32.dll")] internal static extern bool DeleteObject(nint value);
        [DllImport("gdi32.dll")] internal static extern bool DeleteDC(nint dc);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] internal static extern nint CreateFontW(int height, int width, int escapement, int orientation, int weight, uint italic, uint underline, uint strikeout, uint charset, uint outputPrecision, uint clipPrecision, uint quality, uint pitchAndFamily, string face);
        [DllImport("gdi32.dll")] internal static extern uint SetTextColor(nint dc, uint color);
        [DllImport("gdi32.dll")] internal static extern uint SetBkColor(nint dc, uint color);
        [DllImport("gdi32.dll")] internal static extern int SetBkMode(nint dc, int mode);
        [DllImport("gdi32.dll")] internal static extern bool GdiFlush();
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] internal static extern bool TextOutW(nint dc, int x, int y, string text, int count);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] internal static extern int DrawTextW(nint dc, string text, int count, ref Rect rectangle, uint format);
    }

    // Deliberately limited to the uniquely stamped synthetic bot payload used by the validation suite.
    // Arbitrary photographs do not provide the exact source text required by this verifier.
    internal int CountSyntheticImageReceipts(byte[] exactTranscriptPng, byte[] sourcePng, string stamp, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        LastDiagnostics.Clear();
        if (!Regex.IsMatch(stamp, @"\A\d{8}-\d{6}-\d{3}-[0-9a-f]{8}\z", RegexOptions.CultureInvariant)) return 0;
        bool gridFormat = sourcePng.AsSpan().SequenceEqual(SyntheticBotImage.Create(stamp));
        if (!gridFormat && !sourcePng.AsSpan().SequenceEqual(SyntheticBotImage.CreateLegacy(stamp))) return 0;
        using var sourceStream = new MemoryStream(sourcePng, false);
        using var sourceBitmap = new Bitmap(sourceStream);
        if (sourceBitmap.Width != 320 || sourceBitmap.Height != 160) return 0;
        var source = Raster.FromBitmap(sourceBitmap);
        static bool HasLabels(string text) => text.Contains("NoticeSendersyntheticbottest", StringComparison.Ordinal) && text.Contains("Nostudentdata", StringComparison.Ordinal);
        int background = source.Pixels.GroupBy(color => color).OrderByDescending(group => group.Count()).First().Key;
        bool Background(int color) => Difference(color, background) <= 2;
        var sourceRegions = Components(source, Background, token).Where(region => region.Count >= 100 && region.Bounds.Width * region.Bounds.Height >= 500).ToArray();
        if (sourceRegions.Length == 0) return 0;
        Rectangle sourceRegion = sourceRegions.OrderByDescending(region => region.Bounds.Width * region.Bounds.Height).First().Bounds;
        Rectangle? sourceStampBounds = gridFormat ? null : SyntheticStampBounds(source, sourceRegion);
        if (!gridFormat && sourceStampBounds is null) return 0;
        using var captureStream = new MemoryStream(exactTranscriptPng, false);
        using var captureBitmap = new Bitmap(captureStream);
        var capture = Raster.FromBitmap(captureBitmap);
        // Collect red shapes once, then scope them to each candidate image and its status gutter.
        // Older PDF icons elsewhere in the transcript do not describe this image's send status.
        bool RedStatus(int color)
        {
            int red = color >> 16 & 255, green = color >> 8 & 255, blue = color & 255;
            return red >= 150 && green <= 110 && blue <= 110 && red - Math.Max(green, blue) >= 80 && Math.Abs(green - blue) <= 25;
        }
        var redStatuses = Components(capture, RedStatus, token).Where(region => region.Count >= 3).ToArray();
        int count = 0;
        foreach (var region in Components(capture, Background, token).Where(region => region.Count >= 100 && region.Bounds.Width * region.Bounds.Height >= 500))
        {
            Rectangle bounds = region.Bounds;
            double scale = bounds.Width / (double)sourceRegion.Width;
            if (Math.Abs(scale - bounds.Height / (double)sourceRegion.Height) > .02) continue;
            int x = bounds.Left - (int)Math.Round(sourceRegion.Left * scale), y = bounds.Bottom - (int)Math.Round(sourceRegion.Bottom * scale);
            int width = (int)Math.Round(source.Width * scale), height = (int)Math.Round(source.Height * scale);
            if (width < 100 || height < 50 || x < 0 || y < 0 || x + width > capture.Width || y + height > capture.Height) continue;
            int gutter = Math.Max(16, width / 4), verticalMargin = Math.Max(4, height / 12);
            var statusRegion = Rectangle.FromLTRB(Math.Max(0, x - gutter), Math.Max(0, y - verticalMargin), Math.Min(capture.Width, x + width + gutter), Math.Min(capture.Height, y + height + verticalMargin));
            if (redStatuses.Any(status => statusRegion.IntersectsWith(status.Bounds)))
            {
                LastDiagnostics.Add("Potential red failed-send status adjacent to candidate image.");
                continue;
            }
            var patch = capture.Crop(new Rectangle(x, y, width, height));
            var reference = ResizeBicubic(source, width, height);
            long absoluteError = 0; int uniform = 0, uniformBad = 0, gridMaxError = 0;
            for (int row = 0; row < height; row++)
            {
                token.ThrowIfCancellationRequested();
                for (int column = 0; column < width; column++)
                {
                    int a = reference[column, row], b = patch[column, row];
                    if (gridFormat && column >= SyntheticBotImage.GridX * scale && column < (SyntheticBotImage.GridX + SyntheticBotImage.GridSide * SyntheticBotImage.GridCellSize) * scale && row >= SyntheticBotImage.GridY * scale && row < (SyntheticBotImage.GridY + SyntheticBotImage.GridSide * SyntheticBotImage.GridCellSize) * scale)
                        gridMaxError = Math.Max(gridMaxError, Difference(a, b));
                    absoluteError += Math.Abs((a >> 16 & 255) - (b >> 16 & 255)) + Math.Abs((a >> 8 & 255) - (b >> 8 & 255)) + Math.Abs((a & 255) - (b & 255));
                    if (row < 3 || row >= height - 3 || column < 3 || column >= width - 3) continue;
                    bool flat = true;
                    for (int dy = -2; dy <= 2 && flat; dy++)
                    for (int dx = -2; dx <= 2; dx++)
                        if (Difference(a, reference[column + dx, row + dy]) > 2) { flat = false; break; }
                    if (flat)
                    {
                        uniform++;
                        if (Difference(a, b) > 12)
                        {
                            uniformBad++;
                            if (uniformBad <= 10) LastDiagnostics.Add($"UniformResidual at {column},{row} reference #{a:X6} actual #{b:X6}");
                        }
                    }
                }
            }
            double mae = absoluteError / (3.0 * width * height);
            LastDiagnostics.Add($"SyntheticImage {x},{y},{width},{height} MAE {mae.ToString(CultureInfo.InvariantCulture)} uniform {uniform} bad {uniformBad} grid maximum residual {gridMaxError}");
            // Dense grid edges differ slightly under Kakao's observed thumbnail sampling.
            // Its identifier still requires every confidently decoded SHA bit to match.
            if (mae >= (gridFormat ? 6 : 4) || uniform <= 1000 || uniformBad != 0) continue;
            if (gridFormat)
            {
                if (HasNeutralStatusOutsideGrid(patch, scale, token))
                {
                    LastDiagnostics.Add("Possible neutral upload status outside grid.");
                    continue;
                }
                bool exactGrid = DecodeSyntheticGrid(patch, stamp);
                LastDiagnostics.Add("SyntheticGrid " + (exactGrid ? "all 256 bits exact" : "mismatch or insufficient contrast"));
                if (exactGrid) count++;
                continue; // A v2 grid failure never falls back to legacy OCR.
            }
            string text = Compact(Ocr(Preprocess(patch, null), "eng", false, token));
            LastDiagnostics.Add("SyntheticImageOCR " + JsonSerializer.Serialize(text));
            if (sourceStampBounds is not Rectangle sb) continue;
            var stampBounds = Rectangle.FromLTRB(Math.Max(0, (int)Math.Round(sb.Left * scale) - 2), Math.Max(0, (int)Math.Round(sb.Top * scale) - 2), Math.Min(width, (int)Math.Round(sb.Right * scale) + 2), Math.Min(height, (int)Math.Round(sb.Bottom * scale) + 2));
            if (stampBounds.Width <= 0 || stampBounds.Height <= 0) continue;
            string candidateStamp = Compact(Ocr(PreprocessStamp(patch.Crop(stampBounds)), "eng", false, token, 7, "0123456789abcdef-", true));
            LastDiagnostics.Add("SyntheticStampOCR " + JsonSerializer.Serialize(candidateStamp));
            if (HasLabels(text) && candidateStamp.Equals(stamp, StringComparison.Ordinal)) count++;
        }
        return count;
    }

    private static bool HasNeutralStatusOutsideGrid(Raster patch, double scale, CancellationToken token)
    {
        // The generated source has no medium neutral shapes outside its black/white grid.
        // Keep a two-pixel margin for the grid's resampled edges.
        var grid = Rectangle.FromLTRB(Math.Max(0, (int)Math.Floor(SyntheticBotImage.GridX * scale) - 2), Math.Max(0, (int)Math.Floor(SyntheticBotImage.GridY * scale) - 2),
            Math.Min(patch.Width, (int)Math.Ceiling((SyntheticBotImage.GridX + SyntheticBotImage.GridSide * SyntheticBotImage.GridCellSize) * scale) + 2),
            Math.Min(patch.Height, (int)Math.Ceiling((SyntheticBotImage.GridY + SyntheticBotImage.GridSide * SyntheticBotImage.GridCellSize) * scale) + 2));
        var mask = new Raster(patch.Width, patch.Height);
        for (int y = 0; y < patch.Height; y++)
        for (int x = 0; x < patch.Width; x++)
        {
            if (grid.Contains(x, y)) continue;
            int color = patch[x, y], high = Max(color);
            if (high >= 72 && high <= 192 && high - Min(color) <= 8) mask[x, y] = 1;
        }
        return Components(mask, color => color == 1, token).Any(region => region.Count >= 8 && region.Bounds.Width >= 3 && region.Bounds.Height >= 3);
    }

    private static bool DecodeSyntheticGrid(Raster patch, string stamp)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(stamp));
        double scale = patch.Width / (double)SyntheticBotImage.Width;
        for (int index = 0; index < SyntheticBotImage.GridSide * SyntheticBotImage.GridSide; index++)
        {
            int x = (int)((SyntheticBotImage.GridX + (index % SyntheticBotImage.GridSide + .5) * SyntheticBotImage.GridCellSize) * scale);
            int y = (int)((SyntheticBotImage.GridY + (index / SyntheticBotImage.GridSide + .5) * SyntheticBotImage.GridCellSize) * scale);
            if (x < 1 || y < 1 || x + 1 >= patch.Width || y + 1 >= patch.Height) return false;
            bool black = true, white = true;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int color = patch[x + dx, y + dy];
                black &= Max(color) <= 80;
                white &= Min(color) >= 175;
            }
            if ((!black && !white) || black != ((digest[index / 8] & (1 << (7 - index % 8))) != 0)) return false;
        }
        return true;
    }

    private static Rectangle? SyntheticStampBounds(Raster source, Rectangle background)
    {
        var bands = new List<(int Top, int Bottom)>();
        for (int y = background.Top; y < background.Bottom; y++)
        {
            bool ink = false;
            for (int x = 0; x < source.Width; x++) if (Max(source[x, y]) < 180) { ink = true; break; }
            if (!ink) continue;
            if (bands.Count == 0 || y > bands[^1].Bottom + 2) bands.Add((y, y + 1));
            else bands[^1] = (bands[^1].Top, y + 1);
        }
        if (bands.Count != 3) return null;
        var band = bands[1]; int left = source.Width, right = -1;
        for (int y = band.Top; y < band.Bottom; y++)
        for (int x = 0; x < source.Width; x++) if (Max(source[x, y]) < 180) { left = Math.Min(left, x); right = Math.Max(right, x); }
        return right < 0 ? null : Rectangle.FromLTRB(Math.Max(0, left - 3), Math.Max(0, band.Top - 3), Math.Min(source.Width, right + 4), Math.Min(source.Height, band.Bottom + 3));
    }

    private static Raster PreprocessStamp(Raster source)
    {
        var result = new Raster(source.Width * 2 + 24, source.Height * 2 + 24, 0xFFFFFF);
        for (int y = 0; y < source.Height * 2; y++)
        for (int x = 0; x < source.Width * 2; x++)
        {
            int color = source[x / 2, y / 2];
            int gray = ((color >> 16 & 255) * 19595 + (color >> 8 & 255) * 38470 + (color & 255) * 7471 + 32768) >> 16;
            result[x + 12, y + 12] = gray * 0x010101;
        }
        return result;
    }

    private static int Difference(int a, int b) => Math.Max(Math.Abs((a >> 16 & 255) - (b >> 16 & 255)), Math.Max(Math.Abs((a >> 8 & 255) - (b >> 8 & 255)), Math.Abs((a & 255) - (b & 255))));

    private static Raster ResizeBicubic(Raster source, int width, int height)
    {
        static Weights[] Build(int input, int output)
        {
            static double Cubic(double value)
            {
                value = Math.Abs(value);
                if (value < 1) return ((1.5 * value - 2.5) * value) * value + 1;
                return value < 2 ? (((-.5 * value + 2.5) * value - 4) * value + 2) : 0;
            }
            double scale = input / (double)output, filterScale = Math.Max(1, scale), support = 2 * filterScale;
            var result = new Weights[output];
            for (int x = 0; x < output; x++)
            {
                double center = (x + .5) * scale;
                int left = Math.Max(0, (int)(center - support + .5)), right = Math.Min(input, (int)(center + support + .5));
                var raw = new double[right - left]; double total = 0;
                for (int k = 0; k < raw.Length; k++) { raw[k] = Cubic((left + k - center + .5) / filterScale); total += raw[k]; }
                result[x] = new Weights(left, raw.Select(value => (int)(value / total * (1 << 22) + (value >= 0 ? .5 : -.5))).ToArray());
            }
            return result;
        }
        var xWeights = Build(source.Width, width); var yWeights = Build(source.Height, height);
        var horizontal = new Raster(width, source.Height); var result = new Raster(width, height);
        for (int y = 0; y < source.Height; y++)
        for (int x = 0; x < width; x++)
        {
            var weights = xWeights[x]; int color = 0;
            for (int channel = 0; channel < 3; channel++)
            {
                long sum = 1 << 21;
                for (int k = 0; k < weights.Coefficients.Length; k++) sum += (long)(source[weights.Start + k, y] >> (channel * 8) & 255) * weights.Coefficients[k];
                color |= Math.Clamp((int)(sum >> 22), 0, 255) << (channel * 8);
            }
            horizontal[x, y] = color;
        }
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            var weights = yWeights[y]; int color = 0;
            for (int channel = 0; channel < 3; channel++)
            {
                long sum = 1 << 21;
                for (int k = 0; k < weights.Coefficients.Length; k++) sum += (long)(horizontal[x, weights.Start + k] >> (channel * 8) & 255) * weights.Coefficients[k];
                color |= Math.Clamp((int)(sum >> 22), 0, 255) << (channel * 8);
            }
            result[x, y] = color;
        }
        return result;
    }

    private static Rectangle? TitleBounds(Raster card, CancellationToken token)
    {
        var icons = Components(card, color => Max(color) - Min(color) > 90, token)
            .Where(region => region.Bounds.Width > 10 && region.Bounds.Height > 10 && region.Count >= 20).ToArray();
        if (icons.Length != 1) return null;
        int right = icons[0].Bounds.Left, minX = card.Width, minY = card.Height, maxY = -1;
        for (int y = 0; y < card.Height; y++)
        for (int x = 0; x < right; x++)
            // ClearType's thin black stems may retain a light red channel. Use their
            // dark channel, while excluding gray metadata and bright blue action links.
            if (Min(card[x, y]) < 115 && Max(card[x, y]) < 200) { minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y); }
        if (maxY < 0 || minX >= right) return null;
        return Rectangle.FromLTRB(Math.Max(0, minX - 2), Math.Max(0, minY - 2), right, Math.Min(card.Height, maxY + 3));
    }

    private string Ocr(Raster image, string language, bool ascii, CancellationToken token, int pageSegmentation = 6, string? alphabet = null, bool disableDictionaries = false)
    {
        token.ThrowIfCancellationRequested();
        string directory = Path.Combine(Path.GetTempPath(), "NoticeSender-ReceiptOCR-" + Guid.NewGuid().ToString("N"));
        string input = Path.Combine(directory, "transcript-card.png");
        Directory.CreateDirectory(directory);
        try
        {
            using (var bitmap = image.ToBitmap()) bitmap.Save(input, ImageFormat.Png);
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, WorkingDirectory = Path.GetDirectoryName(executable)! };
            foreach (string arg in new[] { input, "stdout", "--psm", pageSegmentation.ToString(CultureInfo.InvariantCulture), "-l", language, "--tessdata-dir", tessdata }) start.ArgumentList.Add(arg);
            if (ascii || alphabet is not null) { start.ArgumentList.Add("-c"); start.ArgumentList.Add("tessedit_char_whitelist=" + (alphabet ?? Alphabet)); }
            if (disableDictionaries) foreach (string arg in new[] { "-c", "load_system_dawg=0", "-c", "load_freq_dawg=0" }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("OCR 프로세스를 시작할 수 없습니다.");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            var deadline = Stopwatch.StartNew();
            try
            {
                while (!process.WaitForExit(50))
                {
                    token.ThrowIfCancellationRequested();
                    if (deadline.Elapsed > TimeSpan.FromSeconds(15)) throw new TimeoutException("첨부 확인 OCR 제한 시간이 초과되었습니다.");
                }
                token.ThrowIfCancellationRequested();
                if (process.ExitCode != 0) throw new InvalidOperationException("첨부 확인 OCR에 실패했습니다: " + stderr.GetAwaiter().GetResult());
                return stdout.GetAwaiter().GetResult();
            }
            finally { if (!process.HasExited) { process.Kill(true); process.WaitForExit(2000); } }
        }
        finally { File.Delete(input); Directory.Delete(directory, false); }
    }

    private static void RequireHash(string path, string expected)
    {
        using var stream = File.OpenRead(path);
        if (!Convert.ToHexString(SHA256.HashData(stream)).Equals(expected, StringComparison.Ordinal))
            throw new InvalidOperationException("검증되지 않은 첨부 확인 OCR 구성요소입니다: " + Path.GetFileName(path));
    }
    private static string Compact(string text) => Regex.Replace(text, @"\s+", "", RegexOptions.CultureInvariant);
    private static int Max(int c) => Math.Max(c >> 16 & 255, Math.Max(c >> 8 & 255, c & 255));
    private static int Min(int c) => Math.Min(c >> 16 & 255, Math.Min(c >> 8 & 255, c & 255));

    private sealed record Region(Rectangle Bounds, int Count);
    private static List<Region> Components(Raster image, Func<int, bool> predicate, CancellationToken token)
    {
        var seen = new bool[image.Pixels.Length];
        var queue = new int[image.Pixels.Length];
        var result = new List<Region>();
        for (int start = 0; start < image.Pixels.Length; start++)
        {
            if (start % image.Width == 0) token.ThrowIfCancellationRequested();
            if (seen[start] || !predicate(image.Pixels[start])) continue;
            int head = 0, tail = 1, minX = start % image.Width, maxX = minX, minY = start / image.Width, maxY = minY;
            seen[start] = true; queue[0] = start;
            while (head < tail)
            {
                int current = queue[head++], x = current % image.Width, y = current / image.Width;
                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                void Visit(int index) { if (!seen[index] && predicate(image.Pixels[index])) { seen[index] = true; queue[tail++] = index; } }
                if (x > 0) Visit(current - 1); if (x + 1 < image.Width) Visit(current + 1);
                if (y > 0) Visit(current - image.Width); if (y + 1 < image.Height) Visit(current + image.Width);
            }
            result.Add(new Region(Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1), tail));
        }
        return result;
    }

    private static Raster Preprocess(Raster source, int? threshold)
    {
        var gray = new int[source.Pixels.Length];
        for (int i = 0; i < gray.Length; i++)
        {
            int c = source.Pixels[i];
            gray[i] = ((c >> 16 & 255) * 19595 + (c >> 8 & 255) * 38470 + (c & 255) * 7471 + 32768) >> 16;
        }
        int width = source.Width * 2, height = source.Height * 2;
        var horizontal = new int[width * source.Height];
        var output = new Raster(width + 24, height + 24, 0xFFFFFF);
        var xWeights = LanczosWeights(source.Width); var yWeights = LanczosWeights(source.Height);
        for (int y = 0; y < source.Height; y++)
        for (int x = 0; x < width; x++)
        {
            var weights = xWeights[x]; long sum = 1 << 21;
            for (int k = 0; k < weights.Coefficients.Length; k++) sum += (long)gray[y * source.Width + weights.Start + k] * weights.Coefficients[k];
            horizontal[y * width + x] = Math.Clamp((int)(sum >> 22), 0, 255);
        }
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            var weights = yWeights[y]; long sum = 1 << 21;
            for (int k = 0; k < weights.Coefficients.Length; k++) sum += (long)horizontal[(weights.Start + k) * width + x] * weights.Coefficients[k];
            int value = Math.Clamp((int)(sum >> 22), 0, 255);
            if (threshold.HasValue) value = value >= threshold.Value ? 255 : 0;
            output[x + 12, y + 12] = value * 0x010101;
        }
        return output;
    }
    private sealed record Weights(int Start, int[] Coefficients);
    private static Weights[] LanczosWeights(int length)
    {
        var result = new Weights[length * 2];
        static double Sinc(double value) => value == 0 ? 1 : Math.Sin(Math.PI * value) / (Math.PI * value);
        for (int x = 0; x < result.Length; x++)
        {
            double center = (x + .5) / 2;
            int left = Math.Max(0, (int)(center - 3 + .5)), right = Math.Min(length, (int)(center + 3 + .5));
            var raw = new double[right - left]; double total = 0;
            for (int k = 0; k < raw.Length; k++) { double distance = left + k - center + .5; raw[k] = distance >= -3 && distance < 3 ? Sinc(distance) * Sinc(distance / 3) : 0; total += raw[k]; }
            result[x] = new Weights(left, raw.Select(v => (int)(v / total * (1 << 22) + (v >= 0 ? .5 : -.5))).ToArray());
        }
        return result;
    }

    private sealed class Raster
    {
        internal int Width { get; }
        internal int Height { get; }
        internal int[] Pixels { get; }
        internal Raster(int width, int height, int fill = 0)
        {
            if (width <= 0 || height <= 0 || (long)width * height > 4_000_000) throw new InvalidOperationException("지원하지 않는 첨부 확인 캡처 크기입니다.");
            Width = width; Height = height; Pixels = new int[width * height]; if (fill != 0) Array.Fill(Pixels, fill);
        }
        internal int this[int x, int y] { get => Pixels[y * Width + x]; set => Pixels[y * Width + x] = value; }
        internal static Raster FromBitmap(Bitmap bitmap)
        {
            var result = new Raster(bitmap.Width, bitmap.Height);
            for (int y = 0; y < bitmap.Height; y++) for (int x = 0; x < bitmap.Width; x++) result[x, y] = bitmap.GetPixel(x, y).ToArgb() & 0xFFFFFF;
            return result;
        }
        internal Raster Crop(Rectangle bounds)
        {
            var result = new Raster(bounds.Width, bounds.Height);
            for (int y = 0; y < bounds.Height; y++) Array.Copy(Pixels, (y + bounds.Y) * Width + bounds.X, result.Pixels, y * bounds.Width, bounds.Width);
            return result;
        }
        internal Bitmap ToBitmap()
        {
            var result = new Bitmap(Width, Height, PixelFormat.Format24bppRgb);
            for (int y = 0; y < Height; y++) for (int x = 0; x < Width; x++) result.SetPixel(x, y, Color.FromArgb(unchecked((int)0xFF000000) | this[x, y]));
            return result;
        }
    }
}
