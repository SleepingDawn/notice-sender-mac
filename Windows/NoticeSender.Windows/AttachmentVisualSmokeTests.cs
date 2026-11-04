using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NoticeSender.Automation;
using System.Threading;

namespace NoticeSender.Windows;

internal static class AttachmentVisualSmokeTests
{
    // Static saved synthetic fixtures only. This test never opens or controls KakaoTalk.
    internal static void Run(string fixtures, string tesseractExe, string tessdataDirectory)
    {
        Require(AttachmentVisualRecognizer.SupportsFileName("notice_20261104.txt"), "Supported ASCII name rejected.");
        Require(!AttachmentVisualRecognizer.SupportsFileName("합성-첨부.txt") && !AttachmentVisualRecognizer.SupportsFileName("notice report.txt"), "Unproved filename form accepted.");
        var recognizer = new AttachmentVisualRecognizer(tesseractExe, tessdataDirectory);
        foreach (var item in new[] {
            (Image: "bot-app-send-15-native-D.settled-root.png", Names: new[] { "NoticeSender-SYNTHETIC-20261104-072252-893-58c9a6fb-D-only.txt" }, Sizes: new long[] { 160 }),
            (Image: "bot-app-send-16-native-B.settled-root.png", Names: new[] { "NoticeSender-SYNTHETIC-20261104-072536-946-d5ecbff5-B-single.txt" }, Sizes: new long[] { 162 }),
            (Image: "bot-app-send-17-native-C.after-root.png", Names: new[] { "NoticeSender-SYNTHETIC-20261104-073051-245-74a13bc2-C-first.txt", "NoticeSender-SYNTHETIC-20261104-073051-245-74a13bc2-C-second.csv" }, Sizes: new long[] { 161, 26 }),
            (Image: "bot-app-send-18-native-PDF.after-root.png", Names: new[] { "NoticeSender-SYNTHETIC-20261104-073319-522-24c33178-E-document.pdf" }, Sizes: new long[] { 634 })
        })
        {
            var receipts = recognizer.ReadFileReceipts(TranscriptFixture(Path.Combine(fixtures, item.Image)), CancellationToken.None);
            for (int i = 0; i < item.Names.Length; i++)
                Require(receipts.Count(r => r.Name == item.Names[i] && r.Length == item.Sizes[i]) == 1, "Exact visual receipt missing: " + item.Names[i] + "\n" + string.Join("\n", recognizer.LastDiagnostics));
            Require(receipts.Count == item.Names.Length, "Unexpected visual receipt: " + item.Image);
            Require(receipts.All(r => !r.Name.Contains("dSecbff5", StringComparison.Ordinal)), "Confusable identifier accepted.");
        }
        Require(recognizer.ReadFileReceipts(TranscriptFixture(Path.Combine(fixtures, "bot-app-send-15-native-D.after-root.png")), CancellationToken.None).Count == 0, "Upload progress accepted as completed receipt.");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        bool stopped = false;
        try { recognizer.ReadFileReceipts(TranscriptFixture(Path.Combine(fixtures, "bot-app-send-15-native-D.settled-root.png")), cancelled.Token); }
        catch (OperationCanceledException) { stopped = true; }
        Require(stopped, "OCR ignored cancellation.");
    }

    internal static void RunSyntheticImage(string transcriptFixture, string sourcePng, string tesseractExe, string tessdataDirectory)
    {
        var recognizer = new AttachmentVisualRecognizer(tesseractExe, tessdataDirectory);
        byte[] transcript = File.ReadAllBytes(transcriptFixture), source = File.ReadAllBytes(sourcePng);
        const string stamp = "20261104-073747-618-9dde2351";
        Require(recognizer.CountSyntheticImageReceipts(transcript, source, stamp, CancellationToken.None) == 1, "Known synthetic image receipt missing.\n" + string.Join("\n", recognizer.LastDiagnostics));
        Require(recognizer.CountSyntheticImageReceipts(transcript, source, stamp.Replace("9dde2351", "9dde2352"), CancellationToken.None) == 0, "Wrong source stamp accepted.");
        using (var redStream = new MemoryStream(transcript))
        using (var redImage = new Bitmap(redStream))
        {
            // Static fixture: failed-send marker immediately outside the known image bubble.
            using (var drawing = Graphics.FromImage(redImage)) drawing.FillEllipse(Brushes.Red, 123, 326, 12, 12);
            using var redBytes = new MemoryStream(); redImage.Save(redBytes, ImageFormat.Png);
            Require(recognizer.CountSyntheticImageReceipts(redBytes.ToArray(), source, stamp, CancellationToken.None) == 0, "Failed-send marker outside image accepted.");
        }
        using var stream = new MemoryStream(transcript);
        using var image = new Bitmap(stream);
        using (var drawing = Graphics.FromImage(image)) drawing.FillEllipse(Brushes.Gray, 142 + 175, 247 + 73, 20, 20);
        using var altered = new MemoryStream(); image.Save(altered, ImageFormat.Png);
        Require(recognizer.CountSyntheticImageReceipts(altered.ToArray(), source, stamp, CancellationToken.None) == 0, "Image status overlay accepted.");
    }

    internal static void RunExpectedRendering(string fixtures, string tesseractExe, string tessdataDirectory)
    {
        Require(AttachmentVisualRecognizer.SupportsRenderedFileName("합성-테스트-첨부파일.txt"), "Unambiguous Korean manifest name rejected.");
        Require(!AttachmentVisualRecognizer.SupportsRenderedFileName("합성 파일.txt") && !AttachmentVisualRecognizer.SupportsRenderedFileName("옫.txt") && !AttachmentVisualRecognizer.SupportsRenderedFileName("합성\u200B파일.txt"), "Ambiguous rendered filename accepted.");
        var recognizer = new AttachmentVisualRecognizer(tesseractExe, tessdataDirectory);
        foreach (var item in new[] {
            (Image: "bot-app-send-15-native-D.settled-root.png", Receipts: new[] { new VisualAttachmentReceipt("NoticeSender-SYNTHETIC-20261104-072252-893-58c9a6fb-D-only.txt", 160) }),
            (Image: "bot-app-send-16-native-B.settled-root.png", Receipts: new[] { new VisualAttachmentReceipt("NoticeSender-SYNTHETIC-20261104-072536-946-d5ecbff5-B-single.txt", 162) }),
            (Image: "bot-app-send-17-native-C.after-root.png", Receipts: new[] { new VisualAttachmentReceipt("NoticeSender-SYNTHETIC-20261104-073051-245-74a13bc2-C-first.txt", 161), new VisualAttachmentReceipt("NoticeSender-SYNTHETIC-20261104-073051-245-74a13bc2-C-second.csv", 26) }),
            (Image: "bot-app-send-18-native-PDF.after-root.png", Receipts: new[] { new VisualAttachmentReceipt("NoticeSender-SYNTHETIC-20261104-073319-522-24c33178-E-document.pdf", 634) })
        })
        {
            byte[] image = TranscriptFixture(Path.Combine(fixtures, item.Image));
            var actual = recognizer.ReadExpectedFileReceipts(image, item.Receipts, CancellationToken.None);
            Require(actual.SequenceEqual(item.Receipts), "Exact rendered visual receipt missing: " + item.Image + "\n" + string.Join("\n", recognizer.LastDiagnostics));
            foreach (var wrong in item.Receipts)
            {
                Require(recognizer.ReadExpectedFileReceipts(image, new[] { wrong with { Name = wrong.Name.Replace("SYNTHETIC", "SYNTHET1C") } }, CancellationToken.None).Count == 0, "Wrong rendered ASCII filename accepted.");
                Require(recognizer.ReadExpectedFileReceipts(image, new[] { wrong with { Name = "합성-틀린파일.txt" } }, CancellationToken.None).Count == 0, "Wrong rendered Unicode filename accepted.");
                Require(recognizer.ReadExpectedFileReceipts(image, new[] { wrong with { Length = wrong.Length + 1 } }, CancellationToken.None).Count == 0, "Wrong rendered file size accepted.");
            }
        }
    }

    internal static void RunFreshRegressionFixtures(string fixtures, string tesseractExe, string tessdataDirectory)
    {
        var recognizer = new AttachmentVisualRecognizer(tesseractExe, tessdataDirectory);
        byte[] thinPdf = File.ReadAllBytes(Path.Combine(fixtures, "bot21-PDF-transcript.png"));
        var thinPdfReceipt = new VisualAttachmentReceipt("NoticeSender-SYNTHETIC-20261104-084115-875-e6459ca3-E-document.pdf", 634);
        Require(recognizer.ReadExpectedFileReceipts(thinPdf, new[] { thinPdfReceipt }, CancellationToken.None).SequenceEqual(new[] { thinPdfReceipt }), "Thin trailing PDF glyph receipt missing.\n" + string.Join("\n", recognizer.LastDiagnostics));
        Require(recognizer.ReadExpectedFileReceipts(thinPdf, new[] { thinPdfReceipt with { Name = thinPdfReceipt.Name[..^1] } }, CancellationToken.None).Count == 0, "Truncated PDF filename accepted.");
        byte[] korean = File.ReadAllBytes(Path.Combine(fixtures, "bot22-Korean-transcript.png"));
        var koreanReceipt = new VisualAttachmentReceipt("NoticeSender-SYNTHETIC-20261104-084453-208-68b567a0-E-한글자료.txt", 168);
        Require(recognizer.ReadExpectedFileReceipts(korean, new[] { koreanReceipt }, CancellationToken.None).SequenceEqual(new[] { koreanReceipt }), "Actual Korean receipt missing.\n" + string.Join("\n", recognizer.LastDiagnostics));
        Require(recognizer.ReadExpectedFileReceipts(korean, new[] { koreanReceipt with { Name = koreanReceipt.Name.Replace("자료", "자려") } }, CancellationToken.None).Count == 0, "Wrong Hangul filename accepted.");
        Require(recognizer.ReadExpectedFileReceipts(korean, new[] { koreanReceipt with { Length = 169 } }, CancellationToken.None).Count == 0, "Wrong Korean file size accepted.");
    }

    internal static void RunPngStampRegressionFixtures(string fixtures, string tesseractExe, string tessdataDirectory)
    {
        var recognizer = new AttachmentVisualRecognizer(tesseractExe, tessdataDirectory);
        byte[] transcript = File.ReadAllBytes(Path.Combine(fixtures, "bot24-PNG-transcript.png"));
        byte[] source = File.ReadAllBytes(Path.Combine(fixtures, "bot24-PNG-source.png"));
        const string stamp = "20261104-091158-741-e8e15b17";
        Require(SyntheticImageSource(stamp).SequenceEqual(source), "Stamp regression generator differs from actual source bytes.");
        Require(recognizer.CountSyntheticImageReceipts(transcript, source, stamp, CancellationToken.None) == 1, "Valid hex PNG stamp receipt missing.\n" + string.Join("\n", recognizer.LastDiagnostics));
        foreach (string confusable in new[] { "20261104-091158-741-e8e55b17", "20261104-091158-741-e8e15617", "20261104-091158-741-e8e15017" })
            Require(recognizer.CountSyntheticImageReceipts(transcript, SyntheticImageSource(confusable), confusable, CancellationToken.None) == 0, "Confusable valid hex source stamp accepted: " + confusable);
        RunSyntheticGridFixtures(recognizer, fixtures);
    }

    private static void RunSyntheticGridFixtures(AttachmentVisualRecognizer recognizer, string fixtures)
    {
        const string original = "20261104-095425-392-d37d212e";
        byte[] originalSource = File.ReadAllBytes(Path.Combine(fixtures, "self26-PNG-source.png"));
        byte[] originalFrame = File.ReadAllBytes(Path.Combine(fixtures, "self26-PNG-transcript.png"));
        Require(originalSource.SequenceEqual(SyntheticBotImage.Create(original)), "Actual self PNG source differs from shared generator.");
        Require(recognizer.CountSyntheticImageReceipts(originalFrame, originalSource, original, CancellationToken.None) == 1, "Actual self PNG receipt missing.\n" + string.Join("\n", recognizer.LastDiagnostics));
        byte[] self27Source = File.ReadAllBytes(Path.Combine(fixtures, "self27-PNG-source.png"));
        byte[] self27Frame = File.ReadAllBytes(Path.Combine(fixtures, "self27-PNG-transcript.png"));
        Require(recognizer.CountSyntheticImageReceipts(self27Frame, self27Source, "20261104-100703-936-f86b0551", CancellationToken.None) == 1, "Actual self PNG blocked by earlier PDF icon.\n" + string.Join("\n", recognizer.LastDiagnostics));
        foreach (string wrong in new[] { "20261104-095425-392-d37d252e", "20261104-095425-392-d37d212b" })
            Require(recognizer.CountSyntheticImageReceipts(originalFrame, SyntheticBotImage.Create(wrong), wrong, CancellationToken.None) == 0, "Wrong valid grid stamp accepted.");
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(original));
        using (var stream = new MemoryStream(originalFrame))
        using (var frame = new Bitmap(stream))
        {
            // Synthetic fixture coordinates only, never application input coordinates.
            int x = 142 + (int)((SyntheticBotImage.GridX + SyntheticBotImage.GridCellSize / 2.0) * 210 / SyntheticBotImage.Width);
            int y = 192 + (int)((SyntheticBotImage.GridY + SyntheticBotImage.GridCellSize / 2.0) * 210 / SyntheticBotImage.Width);
            bool black = (digest[0] & 0x80) != 0;
            using (var graphics = Graphics.FromImage(frame)) graphics.FillRectangle(black ? Brushes.White : Brushes.Black, x - 1, y - 1, 3, 3);
            Require(recognizer.CountSyntheticImageReceipts(PngBytes(frame), originalSource, original, CancellationToken.None) == 0, "One wrong grid bit accepted.");
            using (var graphics = Graphics.FromImage(frame)) graphics.FillRectangle(Brushes.Gray, x - 1, y - 1, 3, 3);
            Require(recognizer.CountSyntheticImageReceipts(PngBytes(frame), originalSource, original, CancellationToken.None) == 0, "Low-confidence grid overlay accepted.");
        }
        using (var stream = new MemoryStream(originalFrame))
        using (var frame = new Bitmap(stream))
        {
            using (var graphics = Graphics.FromImage(frame)) graphics.FillEllipse(Brushes.Red, 123, 271, 12, 12);
            Require(recognizer.CountSyntheticImageReceipts(PngBytes(frame), originalSource, original, CancellationToken.None) == 0, "Grid external failed-send marker accepted.");
        }
        using (var stream = new MemoryStream(originalFrame))
        using (var frame = new Bitmap(stream))
        {
            // Center/right upload overlay leaves every grid sample untouched.
            using (var graphics = Graphics.FromImage(frame)) graphics.FillEllipse(Brushes.Gray, 142 + 105 - 7, 192 + 53 - 7, 14, 14);
            Require(recognizer.CountSyntheticImageReceipts(PngBytes(frame), originalSource, original, CancellationToken.None) == 0, "Upload overlay beside intact grid accepted.");
        }
        using (var stream = new MemoryStream(originalSource))
        using (var altered = new Bitmap(stream))
        {
            altered.SetPixel(SyntheticBotImage.GridX, SyntheticBotImage.GridY, Color.Gray);
            Require(recognizer.CountSyntheticImageReceipts(originalFrame, PngBytes(altered), original, CancellationToken.None) == 0, "Altered generated grid source accepted.");
        }
    }

    private static byte[] PngBytes(Bitmap image) { using var output = new MemoryStream(); image.Save(output, ImageFormat.Png); return output.ToArray(); }

    private static byte[] SyntheticImageSource(string stamp)
    {
        using var bitmap = new Bitmap(320, 160, PixelFormat.Format32bppRgb);
        using (var drawing = Graphics.FromImage(bitmap))
        using (var ink = new SolidBrush(Color.FromArgb(24, 43, 65)))
        using (var accent = new SolidBrush(Color.FromArgb(26, 101, 130)))
        using (var font = new Font(FontFamily.GenericSansSerif, 11))
        {
            drawing.Clear(Color.AliceBlue); drawing.FillRectangle(accent, 0, 0, bitmap.Width, 18);
            drawing.DrawString("NoticeSender synthetic bot test\n\n" + stamp + "\n\nNo student data", font, ink, new RectangleF(12, 30, 296, 120));
        }
        using var stream = new MemoryStream(); bitmap.Save(stream, ImageFormat.Png); return stream.ToArray();
    }

    private static byte[] TranscriptFixture(string path)
    {
        // Bounds are fixture metadata from transcript-native-20261104.json; never app-control coordinates.
        using var root = new Bitmap(path);
        using var crop = root.Clone(new Rectangle(1, 89, 378, 433), PixelFormat.Format24bppRgb);
        using var stream = new MemoryStream(); crop.Save(stream, ImageFormat.Png); return stream.ToArray();
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
