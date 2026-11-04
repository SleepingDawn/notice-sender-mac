using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace NoticeSender.Automation;

/// <summary>Opt-in experiment for generated bot-test attachments only. Confirmation dispatch never establishes delivery.</summary>
public sealed class SyntheticFileConfirmationPolicy : NativeFileConfirmationPolicy
{
    public string ApprovedRoom { get; }
    public bool IsSelfChat { get; }
    public string ApprovedBotRoom => !IsSelfChat ? ApprovedRoom : throw new InvalidOperationException("나에게 보내기는 봇 시험 대상이 아닙니다.");
    private readonly Action<ChatTarget, nint, IReadOnlyList<string>> captureBeforeConfirmation;
    public SyntheticFileConfirmationPolicy(string approvedBotRoom, Action<ChatTarget, nint, IReadOnlyList<string>> captureBeforeConfirmation)
        : this(approvedBotRoom, captureBeforeConfirmation, false) { }
    private SyntheticFileConfirmationPolicy(string room, Action<ChatTarget, nint, IReadOnlyList<string>> captureBeforeConfirmation, bool selfChat)
    {
        Require(selfChat ? room == "임세용" : room is "카카오톡 선물하기" or "지모" or "KREAM", "합성 파일 확인 시험의 명시 대상이 아닙니다.");
        ApprovedRoom = room; IsSelfChat = selfChat;
        this.captureBeforeConfirmation = captureBeforeConfirmation ?? throw new ArgumentNullException(nameof(captureBeforeConfirmation));
    }
    public static SyntheticFileConfirmationPolicy ForSelfChat(string exactRoom, bool acknowledged, Action<ChatTarget, nint, IReadOnlyList<string>> capture)
    {
        Require(acknowledged && exactRoom == "임세용", "나에게 보내기 합성 파일 시험은 정확한 ‘임세용’ 대상의 별도 확인이 필요합니다.");
        return new(exactRoom, capture, true);
    }
    public override bool IsRoomApproved(string roomName) => roomName == ApprovedRoom;
    internal override Lease Acquire(ChatTarget target, IReadOnlyList<string> files)
    {
        Require(target.RoomName == ApprovedRoom, "파일 확인 시험 대상 채팅방이 다릅니다.");
        VerifyExecutable(target);
        return Freeze(target, files, LastInputStamp);
    }
    internal Lease Freeze(ChatTarget target, IReadOnlyList<string> files, Func<uint> readInput)
    {
        Require(target.RoomName == ApprovedRoom, "파일 확인 시험 대상 채팅방이 다릅니다.");
        Require(files.Count is 1 or 2, "지원하는 합성 첨부 묶음이 아닙니다.");
        string[] paths = files.Select(Path.GetFullPath).ToArray();
        var matches = paths.Select(p => Regex.Match(Path.GetFileName(p), @"^NoticeSender-SYNTHETIC-(\d{8}-\d{6}-\d{3}-[0-9a-f]{8})-(B-single\.txt|C-first\.txt|C-second\.csv|D-only\.txt|E-document\.pdf|E-image\.png|E-한글자료\.txt)$")).ToArray();
        Require(matches.All(m => m.Success) && matches.Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal).Count() == 1, "생성한 합성 파일 이름이 아닙니다.");
        string stamp = matches[0].Groups[1].Value;
        string[] suffixes = matches.Select(m => m.Groups[2].Value).ToArray();
        Require(paths.Length == 1 ? suffixes[0] is "B-single.txt" or "D-only.txt" or "E-document.pdf" or "E-image.png" or "E-한글자료.txt" : suffixes.SequenceEqual(new[] { "C-first.txt", "C-second.csv" }), "검토한 합성 파일 순서/묶음과 다릅니다.");
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
            var hashes = streams.Select(s => SHA256.HashData(ReadAll(s))).ToArray();
            return new(target, paths, readInput, captureBeforeConfirmation, () =>
            {
                for (int i = 0; i < streams.Count; i++) Require(SHA256.HashData(ReadAll(streams[i])).AsSpan().SequenceEqual(hashes[i]), "고정한 합성 파일이 변경되었습니다.");
            }, () => { foreach (var stream in streams) stream.Dispose(); });
        }
        catch { foreach (var stream in streams) stream.Dispose(); throw; }
    }
    private static byte[] ReadAll(FileStream stream) { stream.Position = 0; using var memory = new MemoryStream(); stream.CopyTo(memory); return memory.ToArray(); }
    internal static byte[] ExpectedBytes(string stamp, string name)
    {
        string suffix = name[("NoticeSender-SYNTHETIC-" + stamp + "-").Length..];
        if (suffix is "B-single.txt" or "C-first.txt" or "D-only.txt" or "E-한글자료.txt") return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("NoticeSender synthetic bot-only attachment test. No student data. " + stamp + " " + name)).ToArray();
        if (suffix == "C-second.csv") return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("test,value\nsynthetic,1\n")).ToArray();
        if (suffix == "E-image.png")
        {
            return SyntheticBotImage.Create(stamp);
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
