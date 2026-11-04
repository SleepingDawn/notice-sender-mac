using System.Collections;
using System.Reflection;
using NoticeSender.Automation;
using NoticeSender.Core;

internal static class ReviewedAttachmentBatchTests
{
    // Reflection keeps the RED artifact runnable before the production API exists.
    private static Type BatchType => typeof(IChatClient).Assembly.GetType("NoticeSender.Automation.ReviewedAttachmentBatch")
        ?? throw new Exception("ReviewedAttachmentBatch does not exist: reviewed file bytes cannot yet be approved.");
    private static object Capture(params BatchItem[] items) => Invoke(BatchType.GetMethod("Capture")!, null, new object[] { items });
    private static object Invoke(MethodInfo method, object? instance, params object[] arguments)
    {
        try { return method.Invoke(instance, arguments)!; }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }
    private static object Open(object batch) => Invoke(BatchType.GetMethod("OpenVerifiedFiles")!, batch);
    private static object Match(object lease, ChatTarget target, params string[] paths) => Invoke(lease.GetType().GetMethod("RequireMatch")!, lease, target, paths);
    private static object Property(object value, string name) => value.GetType().GetProperty(name)!.GetValue(value)!;
    private static object[] Values(object value) => ((IEnumerable)value).Cast<object>().ToArray();
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or ObjectDisposedException or ArgumentException or NotSupportedException) { return; }
        throw new Exception("Unreviewed, changed or unavailable attachments were accepted.");
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }

    public static Task ImmutableManifest()
    {
        using var files = new Files(); var item = files.Item(); var captured = Capture(item);
        item.ChatRoomName = "changed room"; item.AttachmentPaths.Reverse(); item.AttachmentPaths.Clear();
        object manifest = Values(Property(captured, "Items")).Single();
        Check((Guid)Property(manifest, "BatchId") == item.Id && (string)Property(manifest, "RoomName") == "KREAM", "Reviewed target identity changed.");
        var attachments = Values(Property(manifest, "Files"));
        Check(attachments.Length == 2 && (string)Property(attachments[0], "Path") == files.First && (string)Property(attachments[0], "Name") == "first.txt", "Reviewed file ordering changed.");
        Check((long)Property(attachments[0], "Length") == 4 && (string)Property(attachments[0], "Sha256") == Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(new byte[] { 1, 2, 3, 4 })), "Manifest did not bind reviewed bytes/length.");
        foreach (var value in new[] { Property(captured, "Items"), Property(manifest, "Files") })
            if (value is IList list) Reject(() => list.Clear());
        return Task.CompletedTask;
    }

    public static Task ChangedBytesAndWholeBatchPreflight()
    {
        using var files = new Files(); var captured = Capture(files.Item());
        File.WriteAllBytes(files.Second, new byte[] { 9, 8, 7, 6 }); // Same length, so timestamps/size alone cannot pass.
        Reject(() => ((IDisposable)Open(captured)).Dispose());
        File.Delete(files.Second); Reject(() => ((IDisposable)Open(captured)).Dispose());
        // Any failed acquisition must close earlier files in that same batch.
        using var writer = new FileStream(files.First, FileMode.Open, FileAccess.Write, FileShare.None);
        return Task.CompletedTask;
    }

    public static Task ExactScopeAndLifetime()
    {
        using var files = new Files(); var captured = Capture(files.Item()); using var lease = (IDisposable)Open(captured);
        var target = new ChatTarget(1, 2, "KREAM", 3);
        var exact = Match(lease, target, files.First, files.Second);
        Check((string)Property(exact, "RoomName") == "KREAM", "Wrong approved item returned.");
        Match(lease, target, files.First, files.Second); // Matching alone must not consume confirmation permission.
        Reject(() => Match(lease, target with { RoomName = "human" }, files.First, files.Second));
        Reject(() => Match(lease, target, files.Second, files.First));
        Reject(() => Match(lease, target, files.First));
        Reject(() => Match(lease, target, files.First, files.Second, files.First));
        Reject(() => { using var writer = new FileStream(files.First, FileMode.Open, FileAccess.Write, FileShare.ReadWrite); });
        Reject(() => File.Delete(files.First));
        lease.Dispose(); Reject(() => Match(lease, target, files.First, files.Second));
        using var released = new FileStream(files.First, FileMode.Open, FileAccess.Write, FileShare.None);
        return Task.CompletedTask;
    }

    public static Task UnsafeManifestAndWriter()
    {
        using var files = new Files(); var item = files.Item();
        Reject(() => Capture(item, files.Item()));
        item.AttachmentPaths = [files.First, files.First]; Reject(() => Capture(item));
        string nested = System.IO.Path.Combine(files.Directory, "nested"); System.IO.Directory.CreateDirectory(nested);
        string sameName = System.IO.Path.Combine(nested, "first.txt"); File.Copy(files.First, sameName);
        item.AttachmentPaths = [files.First, sameName]; Reject(() => Capture(item));
        item.AttachmentPaths = ["relative.txt"]; Reject(() => Capture(item));
        item.AttachmentPaths = [files.First];
        using var writer = new FileStream(files.First, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        Reject(() => Capture(item));
        return Task.CompletedTask;
    }

    public static Task ReparseAncestor()
    {
        using var files = new Files(); string link = System.IO.Path.Combine(files.Directory, "linked");
        // A directory junction requires no symbolic-link privilege and exercises ancestor traversal.
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe")
        { Arguments = "/c mklink /J \"" + link + "\" \"" + files.Directory + "\"", CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true })!;
        process.WaitForExit(); if (process.ExitCode != 0) throw new Exception("Cannot create reparse fixture: " + process.StandardError.ReadToEnd());
        try { var item = files.Item(); item.AttachmentPaths = [System.IO.Path.Combine(link, "first.txt")]; Reject(() => Capture(item)); }
        finally { System.IO.Directory.Delete(link); }
        return Task.CompletedTask;
    }

    private sealed class Files : IDisposable
    {
        public string Directory { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "NoticeSender-reviewed-files-" + Guid.NewGuid().ToString("N"));
        public string First => System.IO.Path.Combine(Directory, "first.txt");
        public string Second => System.IO.Path.Combine(Directory, "second.csv");
        public Files() { System.IO.Directory.CreateDirectory(Directory); File.WriteAllBytes(First, [1, 2, 3, 4]); File.WriteAllBytes(Second, [5, 6, 7, 8]); }
        public BatchItem Item() => new() { Id = Guid.NewGuid(), ChatRoomName = "KREAM", AttachmentPaths = [First, Second] };
        public void Dispose() { System.IO.Directory.Delete(Directory, true); }
    }
}
