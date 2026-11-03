using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace NoticeSender.Automation;

/// <summary>Opt-in experiment for generated bot-test attachments only. Confirmation dispatch never establishes delivery.</summary>
public sealed class SyntheticFileConfirmationPolicy
{
    private const string SupportedSha256 = "BFB4206321786D0F5C8A9E0FF2A1959E0E901A18BCEA40A652B49229B8B86BF1";
    public string ApprovedBotRoom { get; }
    private readonly Action<ChatTarget, nint, IReadOnlyList<string>> captureBeforeConfirmation;
    public SyntheticFileConfirmationPolicy(string approvedBotRoom, Action<ChatTarget, nint, IReadOnlyList<string>> captureBeforeConfirmation)
    {
        Require(approvedBotRoom is "카카오톡 선물하기" or "지모" or "KREAM", "합성 파일 확인 시험은 허용된 봇 방에서만 가능합니다.");
        ApprovedBotRoom = approvedBotRoom;
        this.captureBeforeConfirmation = captureBeforeConfirmation ?? throw new ArgumentNullException(nameof(captureBeforeConfirmation));
    }
    internal Lease Acquire(ChatTarget target, IReadOnlyList<string> files)
    {
        Require(target.RoomName == ApprovedBotRoom, "파일 확인 시험 대상 봇이 다릅니다.");
        using var process = Process.GetProcessById(target.ProcessId);
        string executable = process.MainModule?.FileName ?? throw new InvalidOperationException("카카오톡 실행 파일을 확인할 수 없습니다.");
        var version = FileVersionInfo.GetVersionInfo(executable);
        using var image = new FileStream(executable, FileMode.Open, FileAccess.Read, FileShare.Read);
        Require(process.ProcessName.Equals("KakaoTalk", StringComparison.OrdinalIgnoreCase) && version.FileMajorPart == 26 && version.FileMinorPart == 9 && version.FileBuildPart == 2 && version.FilePrivatePart == 5342 &&
            Convert.ToHexString(SHA256.HashData(image)) == SupportedSha256, "파일 확인 시험에 검증된 카카오톡 버전/해시가 아닙니다.");
        return Freeze(target, files, LastInputStamp);
    }
    internal Lease Freeze(ChatTarget target, IReadOnlyList<string> files, Func<uint> readInput)
    {
        Require(target.RoomName == ApprovedBotRoom, "파일 확인 시험 대상 봇이 다릅니다.");
        Require(files.Count is 1 or 2, "지원하는 합성 첨부 묶음이 아닙니다.");
        string[] paths = files.Select(Path.GetFullPath).ToArray();
        var matches = paths.Select(p => Regex.Match(Path.GetFileName(p), @"^NoticeSender-SYNTHETIC-(\d{8}-\d{6}-\d{3}-[0-9a-f]{8})-(B-single\.txt|C-first\.txt|C-second\.csv|D-only\.txt|E-document\.pdf|E-image\.png)$")).ToArray();
        Require(matches.All(m => m.Success) && matches.Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal).Count() == 1, "생성한 합성 파일 이름이 아닙니다.");
        string stamp = matches[0].Groups[1].Value;
        string[] suffixes = matches.Select(m => m.Groups[2].Value).ToArray();
        Require(paths.Length == 1 ? suffixes[0] is "B-single.txt" or "D-only.txt" or "E-document.pdf" or "E-image.png" : suffixes.SequenceEqual(new[] { "C-first.txt", "C-second.csv" }), "검토한 합성 파일 순서/묶음과 다릅니다.");
        string directory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "NoticeSender-BotSendTest-" + stamp));
        var streams = new List<FileStream>();
        try
        {
            foreach (string path in paths)
            {
                Require(string.Equals(Path.GetDirectoryName(path), directory, StringComparison.OrdinalIgnoreCase), "합성 시험 폴더 밖의 파일입니다.");
                for (string? item = path; item != null; item = Path.GetDirectoryName(item)) Require((File.GetAttributes(item) & FileAttributes.ReparsePoint) == 0, "합성 시험 파일에 재분석 지점이 있습니다.");
                var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read); streams.Add(stream);
                Require(ReadAll(stream).AsSpan().SequenceEqual(ExpectedBytes(stamp, Path.GetFileName(path))), "합성 시험 파일의 원본 바이트와 다릅니다.");
            }
            return new(target, Array.AsReadOnly(paths), streams, readInput, captureBeforeConfirmation);
        }
        catch { foreach (var stream in streams) stream.Dispose(); throw; }
    }
    internal sealed class Lease : IDisposable
    {
        private readonly ChatTarget target;
        internal IReadOnlyList<string> Files { get; }
        private readonly List<FileStream> streams;
        private readonly byte[][] hashes;
        private readonly Func<uint> readInput;
        private readonly uint inputStamp;
        private readonly Action<ChatTarget, nint, IReadOnlyList<string>> capture;
        private int attempted;
        internal Lease(ChatTarget target, IReadOnlyList<string> paths, List<FileStream> streams, Func<uint> readInput, Action<ChatTarget, nint, IReadOnlyList<string>> capture)
        {
            this.target = target; Files = paths; this.streams = streams; this.readInput = readInput; this.capture = capture;
            hashes = streams.Select(s => SHA256.HashData(ReadAll(s))).ToArray(); inputStamp = readInput();
        }
        internal void RequireUnchanged()
        {
            RequireInputUnchanged();
            for (int i = 0; i < streams.Count; i++) Require(SHA256.HashData(ReadAll(streams[i])).AsSpan().SequenceEqual(hashes[i]), "고정한 합성 파일이 변경되었습니다.");
            RequireInputUnchanged();
        }
        private void RequireInputUnchanged() => Require(readInput() == inputStamp, "시험 중 사용자 입력을 감지하여 파일 확인을 중단했습니다.");
        internal void Confirm(nint modal, Action finalChecks, Action dispatch, CancellationToken token)
        {
            if (Interlocked.Exchange(ref attempted, 1) != 0) throw new InvalidOperationException("같은 파일 확인을 재시도할 수 없습니다.");
            token.ThrowIfCancellationRequested(); RequireUnchanged(); finalChecks();
            capture(target, modal, Files); // Required caller audit/capture must throw on failure.
            token.ThrowIfCancellationRequested(); RequireUnchanged(); finalChecks(); RequireInputUnchanged(); token.ThrowIfCancellationRequested(); dispatch();
        }
        public void Dispose() { foreach (var stream in streams) stream.Dispose(); }
    }
    private static byte[] ReadAll(FileStream stream) { stream.Position = 0; using var memory = new MemoryStream(); stream.CopyTo(memory); return memory.ToArray(); }
    private static void Require(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }
    [StructLayout(LayoutKind.Sequential)] private struct LastInput { public uint Size, Time; }
    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LastInput value);
    private static uint LastInputStamp() { var value = new LastInput { Size = (uint)Marshal.SizeOf<LastInput>() }; Require(GetLastInputInfo(ref value), "마지막 사용자 입력을 확인할 수 없습니다."); return value.Time; }

    internal static byte[] ExpectedBytes(string stamp, string name)
    {
        string suffix = name[("NoticeSender-SYNTHETIC-" + stamp + "-").Length..];
        if (suffix is "B-single.txt" or "C-first.txt" or "D-only.txt") return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("NoticeSender synthetic bot-only attachment test. No student data. " + stamp + " " + name)).ToArray();
        if (suffix == "C-second.csv") return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("test,value\nsynthetic,1\n")).ToArray();
        if (suffix == "E-image.png")
        {
            using var bitmap = new System.Drawing.Bitmap(320, 160, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
            using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
            using (var ink = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(24, 43, 65)))
            using (var accent = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(26, 101, 130)))
            using (var font = new System.Drawing.Font(System.Drawing.FontFamily.GenericSansSerif, 11))
            {
                graphics.Clear(System.Drawing.Color.AliceBlue); graphics.FillRectangle(accent, 0, 0, bitmap.Width, 18);
                graphics.DrawString("NoticeSender synthetic bot test\n\n" + stamp + "\n\nNo student data", font, ink, new System.Drawing.RectangleF(12, 30, 296, 120));
            }
            using var png = new MemoryStream(); bitmap.Save(png, System.Drawing.Imaging.ImageFormat.Png); return png.ToArray();
        }
        Require(suffix == "E-document.pdf", "지원하지 않는 합성 파일입니다.");
        string content = "BT /F1 12 Tf 15 50 Td (NoticeSender synthetic bot test " + stamp + ") Tj ET\n";
        var objects = new[] { "<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [3 0 R] /Count 1 >>", "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 600 100] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>",
            "<< /Length " + Encoding.ASCII.GetByteCount(content) + " >>\nstream\n" + content + "endstream", "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>" };
        var text = new StringBuilder("%PDF-1.4\n"); var offsets = new List<int>();
        for (int i = 0; i < objects.Length; i++) { offsets.Add(Encoding.ASCII.GetByteCount(text.ToString())); text.Append(i + 1).Append(" 0 obj\n").Append(objects[i]).Append("\nendobj\n"); }
        int xref = Encoding.ASCII.GetByteCount(text.ToString()); text.Append("xref\n0 6\n0000000000 65535 f \n");
        foreach (int offset in offsets) text.Append(offset.ToString("D10", System.Globalization.CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        text.Append("trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF\n"); return Encoding.ASCII.GetBytes(text.ToString());
    }
}
