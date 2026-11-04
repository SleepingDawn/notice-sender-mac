using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;
using NoticeSender.Core;

namespace NoticeSender.Automation;

/// <summary>Metadata for the exact attachment bytes shown in one review. This object grants no permission to send.</summary>
public sealed class ReviewedAttachmentFile
{
    public string Path { get; }
    public string Name => System.IO.Path.GetFileName(Path);
    public long Length { get; }
    public string Sha256 { get; }
    internal ReviewedAttachmentFile(string path, long length, string sha256) { Path = path; Length = length; Sha256 = sha256; }
}

public sealed class ReviewedAttachmentItem
{
    public Guid BatchId { get; }
    public string RoomName { get; }
    public IReadOnlyList<ReviewedAttachmentFile> Files { get; }
    internal ReviewedAttachmentItem(Guid batchId, string roomName, IEnumerable<ReviewedAttachmentFile> files)
    { BatchId = batchId; RoomName = roomName; Files = Array.AsReadOnly(files.ToArray()); }
}

/// <summary>Capture before review; open verified files only after that same review is approved, before any delivery.</summary>
public sealed class ReviewedAttachmentBatch
{
    public IReadOnlyList<ReviewedAttachmentItem> Items { get; }
    private ReviewedAttachmentBatch(IEnumerable<ReviewedAttachmentItem> items) => Items = Array.AsReadOnly(items.ToArray());

    public static ReviewedAttachmentBatch Capture(IReadOnlyList<BatchItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var result = new List<ReviewedAttachmentItem>();
        var rooms = new HashSet<string>(StringComparer.Ordinal);
        var ids = new HashSet<Guid>();
        foreach (var item in items)
        {
            if (item == null || item.Id == Guid.Empty || !ids.Add(item.Id) || string.IsNullOrWhiteSpace(item.ChatRoomName) || !rooms.Add(item.ChatRoomName))
                throw new InvalidOperationException("첨부 검토의 발송 항목과 정확한 채팅방은 고유해야 합니다.");
            string room = item.ChatRoomName;
            Guid id = item.Id;
            var paths = item.AttachmentPaths.ToArray();
            var files = new List<ReviewedAttachmentFile>();
            var uniquePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var uniqueNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in paths)
            {
                string full = ReviewedAttachmentFiles.CanonicalPath(path);
                if (!uniquePaths.Add(full) || !uniqueNames.Add(System.IO.Path.GetFileName(full)))
                    throw new InvalidOperationException("첨부 검토에 중복 경로나 같은 파일 이름이 있습니다.");
                using var stream = ReviewedAttachmentFiles.Open(full);
                files.Add(ReviewedAttachmentFiles.Read(stream, full));
            }
            result.Add(new(id, room, files));
        }
        return new(result);
    }

    public ApprovedAttachmentBatch OpenVerifiedFiles() => new(Items);
}

/// <summary>Owns read locks for the approved batch. Matching does not confirm, consume permission, or establish delivery.</summary>
public sealed class ApprovedAttachmentBatch : IDisposable
{
    private readonly object gate = new();
    private readonly Dictionary<string, (ReviewedAttachmentItem Item, FileStream[] Streams)> rooms = new(StringComparer.Ordinal);
    private bool disposed;
    public IReadOnlyList<ReviewedAttachmentItem> Items { get; }

    internal ApprovedAttachmentBatch(IReadOnlyList<ReviewedAttachmentItem> items)
    {
        Items = Array.AsReadOnly(items.ToArray());
        var opened = new List<FileStream>();
        try
        {
            foreach (var item in Items)
            {
                var streams = new List<FileStream>();
                foreach (var file in item.Files)
                {
                    var stream = ReviewedAttachmentFiles.Open(file.Path);
                    opened.Add(stream); streams.Add(stream);
                    ReviewedAttachmentFiles.RequireUnchanged(stream, file);
                }
                rooms.Add(item.RoomName, (item, streams.ToArray()));
            }
        }
        catch { foreach (var stream in opened) stream.Dispose(); throw; }
    }

    public ReviewedAttachmentItem RequireMatch(ChatTarget target, IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(paths);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!rooms.TryGetValue(target.RoomName, out var entry) || paths.Count != entry.Item.Files.Count)
                throw new InvalidOperationException("검토하고 승인한 채팅방과 첨부 묶음이 아닙니다.");
            for (int i = 0; i < paths.Count; i++)
            {
                string path = ReviewedAttachmentFiles.CanonicalPath(paths[i]);
                var expected = entry.Item.Files[i];
                if (!string.Equals(path, expected.Path, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("승인한 첨부 경로나 순서가 변경되었습니다.");
                ReviewedAttachmentFiles.RequireUnchanged(entry.Streams[i], expected);
                // Verify the pathname still resolves to the held, reviewed file, including directory moves/rebinding.
                using var current = ReviewedAttachmentFiles.Open(path);
                ReviewedAttachmentFiles.RequireSameFile(entry.Streams[i], current);
            }
            return entry.Item;
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            foreach (var entry in rooms.Values) foreach (var stream in entry.Streams) stream.Dispose();
        }
    }
}

internal static class ReviewedAttachmentFiles
{
    internal static string CanonicalPath(string path)
    {
        // The verified profile accepts ordinary local DOS paths. Network, device and alternate stream paths need separate support.
        if (string.IsNullOrWhiteSpace(path) || !System.IO.Path.IsPathFullyQualified(path) || path.Length < 3 ||
            !char.IsAsciiLetter(path[0]) || path[1] != ':' || (path[2] != '\\' && path[2] != '/') || path[2..].Contains(':') || path.Contains('"'))
            throw new InvalidOperationException("첨부파일은 로컬 드라이브의 일반 전체 경로여야 합니다.");
        return System.IO.Path.GetFullPath(path);
    }

    private static void RequireOrdinaryPath(string path)
    {
        string full = CanonicalPath(path);
        for (string? current = full; current != null; current = System.IO.Path.GetDirectoryName(current))
        {
            var attributes = File.GetAttributes(current);
            if ((attributes & FileAttributes.ReparsePoint) != 0 || (current == full && (attributes & FileAttributes.Directory) != 0))
                throw new InvalidOperationException("첨부파일과 상위 폴더에 재분석 지점이 없어야 하며 일반 파일이어야 합니다.");
        }
    }

    internal static FileStream Open(string path)
    {
        RequireOrdinaryPath(path);
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try { RequireHandlePath(stream, path); RequireOrdinaryPath(path); return stream; }
        catch { stream.Dispose(); throw; }
    }

    internal static ReviewedAttachmentFile Read(FileStream stream, string path)
    {
        RequireOrdinaryPath(path); RequireHandlePath(stream, path);
        long length = stream.Length; stream.Position = 0;
        string sha = Convert.ToHexString(SHA256.HashData(stream));
        if (stream.Length != length) throw new InvalidOperationException("검토하는 동안 첨부파일 크기가 변경되었습니다.");
        RequireHandlePath(stream, path); RequireOrdinaryPath(path);
        return new(path, length, sha);
    }

    internal static void RequireUnchanged(FileStream stream, ReviewedAttachmentFile expected)
    {
        var actual = Read(stream, expected.Path);
        if (actual.Length != expected.Length || actual.Sha256 != expected.Sha256)
            throw new InvalidOperationException("최종 검토 후 첨부파일 내용이 변경되었습니다. 다시 검토하세요.");
    }

    private static void RequireHandlePath(FileStream stream, string path)
    {
        var buffer = new StringBuilder(32768);
        uint count = GetFinalPathNameByHandle(stream.SafeFileHandle, buffer, (uint)buffer.Capacity, 0);
        if (count == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "고정한 첨부파일 경로를 확인할 수 없습니다.");
        if (count >= buffer.Capacity) throw new InvalidOperationException("첨부파일의 실제 경로가 너무 깁니다.");
        string actual = buffer.ToString();
        if (actual.StartsWith(@"\\?\", StringComparison.Ordinal)) actual = actual[4..];
        if (!string.Equals(actual, path, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("고정한 첨부파일의 실제 경로가 변경되었습니다.");
    }

    internal static void RequireSameFile(FileStream held, FileStream current)
    {
        if (!GetFileInformationByHandle(held.SafeFileHandle, out var left) || !GetFileInformationByHandle(current.SafeFileHandle, out var right))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "첨부파일의 실제 식별자를 확인할 수 없습니다.");
        if (left.VolumeSerial != right.VolumeSerial || left.FileIndexHigh != right.FileIndexHigh || left.FileIndexLow != right.FileIndexLow)
            throw new InvalidOperationException("승인한 첨부파일 경로가 다른 파일로 변경되었습니다.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME Creation, Access, Write;
        public uint VolumeSerial, SizeHigh, SizeLow, Links, FileIndexHigh, FileIndexLow;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle file, StringBuilder path, uint capacity, uint flags);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileInformation information);
}
