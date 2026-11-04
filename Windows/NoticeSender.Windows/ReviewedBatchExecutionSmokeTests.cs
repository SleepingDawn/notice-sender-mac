using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NoticeSender.Automation;
using NoticeSender.Core;

namespace NoticeSender.Windows;

internal static class ReviewedBatchExecutionSmokeTests
{
    internal static async Task RunAsync()
    {
        await RejectChangedMissingAndUnreviewed();
        await RejectChangedScope();
        await HoldLocksThroughTransport();
        await CancelBeforeFactory();
        await FactoryFailureOrCancellationReleasesLocks();
        await DryRunSkipsAttachmentPreflight();
        await ProtectedManualMetadataSurvivesDryRun();
        ReviewUsesCapturedFileMetadata();
    }

    private static Task Execute(AppDatabase db, IReadOnlyList<BatchItem> items, bool dryRun, ReviewedAttachmentBatch? reviewed,
        Func<ApprovedAttachmentBatch?, IChatClient> factory, Action persist, CancellationToken token = default)
    {
        Type type = typeof(ReviewWindow).Assembly.GetType("NoticeSender.Windows.ReviewedBatchExecution")
            ?? throw new Exception("ReviewedBatchExecution does not exist: approved file preflight is absent from the production route.");
        try { return (Task)type.GetMethod("RunAsync", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object?[] { db, items, dryRun, reviewed, factory, (Action<BatchItem>)(_ => { }), persist, token })!; }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }
    private static async Task Reject(Func<Task> work)
    {
        try { await work(); }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or OperationCanceledException or ObjectDisposedException) { return; }
        throw new Exception("Unreviewed or changed production attachment batch was accepted.");
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }

    private static async Task RejectChangedMissingAndUnreviewed()
    {
        foreach (string mode in new[] { "changed-same-size", "missing", "no-manifest" })
        {
            using var f = new Fixture(); var item = f.Item(); var reviewed = ReviewedAttachmentBatch.Capture(new[] { item });
            if (mode == "changed-same-size") File.WriteAllBytes(f.First, new byte[] { 9, 8, 7, 6 });
            if (mode == "missing") File.Delete(f.First);
            int factories = 0, persisted = 0; var client = new FakeClient();
            await Reject(() => Execute(new(), new[] { item }, false, mode == "no-manifest" ? null : reviewed,
                _ => { factories++; return client; }, () => persisted++));
            Check(factories == 0 && persisted == 0 && client.Calls == 0, "Attachment preflight failure touched transport or persistence: " + mode);
        }
    }
    private static async Task RejectChangedScope()
    {
        foreach (string mode in new[] { "id", "room", "path", "order", "removed-row", "extra-row" })
        {
            using var f = new Fixture(); var item = f.Item(); var reviewed = ReviewedAttachmentBatch.Capture(new[] { item }); var items = new List<BatchItem> { item };
            if (mode == "id") item.Id = Guid.NewGuid();
            if (mode == "room") item.ChatRoomName = "unreviewed room";
            if (mode == "path") item.AttachmentPaths = [f.First];
            if (mode == "order") item.AttachmentPaths.Reverse();
            if (mode == "removed-row") items.Clear();
            if (mode == "extra-row") items.Add(new BatchItem { ChatRoomName = "extra room", Message = "unreviewed text" });
            int factories = 0, persisted = 0;
            await Reject(() => Execute(new(), items, false, reviewed, _ => { factories++; return new FakeClient(); }, () => persisted++));
            Check(factories == 0 && persisted == 0, "Changed reviewed scope reached factory/persistence: " + mode);
        }
    }
    private static async Task HoldLocksThroughTransport()
    {
        using var f = new Fixture(); var item = f.Item(); var reviewed = ReviewedAttachmentBatch.Capture(new[] { item });
        ApprovedAttachmentBatch? held = null; int inspected = 0;
        var client = new FakeClient { DuringText = () =>
        {
            held!.RequireMatch(new ChatTarget(1, 2, item.ChatRoomName, 3), item.AttachmentPaths);
            bool locked = false;
            try { using var writer = new FileStream(f.First, FileMode.Open, FileAccess.Write, FileShare.ReadWrite); }
            catch (IOException) { locked = true; }
            Check(locked, "Production transport ran without reviewed file locks."); inspected++;
        } };
        await Execute(new(), new[] { item }, false, reviewed, approved => { held = approved; Check(held != null, "Production factory did not receive approved files."); return client; }, () => { });
        Check(inspected == 1 && item.Status == "전송 요청 완료", "Valid reviewed batch did not finish ordered transport.");
        bool disposed = false; try { held!.RequireMatch(new ChatTarget(1, 2, item.ChatRoomName, 3), item.AttachmentPaths); } catch (ObjectDisposedException) { disposed = true; }
        Check(disposed, "Production batch file lease leaked after completion.");
        using var released = new FileStream(f.First, FileMode.Open, FileAccess.Write, FileShare.None);
    }
    private static async Task CancelBeforeFactory()
    {
        using var f = new Fixture(); var item = f.Item(); var reviewed = ReviewedAttachmentBatch.Capture(new[] { item }); using var cancel = new CancellationTokenSource(); cancel.Cancel();
        int factories = 0, persisted = 0;
        await Reject(() => Execute(new(), new[] { item }, false, reviewed, _ => { factories++; return new FakeClient(); }, () => persisted++, cancel.Token));
        Check(factories == 0 && persisted == 0, "Pre-cancelled reviewed batch reached client factory/persistence.");
        using var released = new FileStream(f.First, FileMode.Open, FileAccess.Write, FileShare.None);
    }
    private static async Task FactoryFailureOrCancellationReleasesLocks()
    {
        foreach (bool cancelInFactory in new[] { false, true })
        {
            using var f = new Fixture(); var item = f.Item(); var reviewed = ReviewedAttachmentBatch.Capture(new[] { item }); using var cancel = new CancellationTokenSource();
            int persisted = 0; var client = new FakeClient();
            await Reject(() => Execute(new(), new[] { item }, false, reviewed, approved =>
            {
                Check(approved != null, "Factory ran before the attachment lease was ready.");
                if (cancelInFactory) { cancel.Cancel(); return client; }
                throw new InvalidOperationException("required client setup failed");
            }, () => persisted++, cancel.Token));
            Check(persisted == 0 && client.Calls == 0, "Factory failure/cancellation entered transport or persistence.");
            using var released = new FileStream(f.First, FileMode.Open, FileAccess.Write, FileShare.None);
        }
    }
    private static async Task DryRunSkipsAttachmentPreflight()
    {
        using var f = new Fixture(); var item = f.Item(); var reviewed = ReviewedAttachmentBatch.Capture(new[] { item });
        File.Delete(f.First); File.Delete(f.Second); var client = new FakeClient(); int factories = 0;
        await Execute(new(), new[] { item }, true, reviewed, approved => { Check(approved == null, "Dry run acquired file delivery authority."); factories++; return client; }, () => { });
        Check(factories == 1 && client.Calls == 1 && item.Status == "드라이런 확인 완료", "Dry run read missing attachment files or entered delivery.");
    }
    private static async Task ProtectedManualMetadataSurvivesDryRun()
    {
        using var f = new Fixture(); var item = f.Item(); var at = DateTimeOffset.UtcNow; var attempt = Guid.NewGuid();
        item.Status = "전송 요청 완료"; item.AttachmentCompletionKind = "user-confirmed";
        item.AttachmentCompletionConfirmedAt = at; item.AttachmentCompletionAuditReference = "original-user-audit.json";
        item.AttachmentCompletionBatchId = item.Id; item.AttachmentCompletionAttemptId = attempt;
        var protectedLog = new SendLog { BatchId = item.Id, Result = item.Status, AttachmentCompletionKind = item.AttachmentCompletionKind,
            AttachmentCompletionConfirmedAt = at, AttachmentCompletionAuditReference = item.AttachmentCompletionAuditReference,
            AttachmentCompletionBatchId = item.Id, AttachmentCompletionAttemptId = attempt };
        var db = new AppDatabase { Logs = [protectedLog] }; var client = new FakeClient();
        await Execute(db, new[] { item }, true, null, approved => { Check(approved == null, "Protected dry run acquired file authority."); return client; }, () => { });
        Check(item.Status == "전송 요청 완료" && item.AttachmentCompletionDisplay == "사용자 확인" &&
            item.AttachmentCompletionKind == "user-confirmed" && item.AttachmentCompletionConfirmedAt == at &&
            item.AttachmentCompletionAuditReference == "original-user-audit.json" && item.AttachmentCompletionBatchId == item.Id &&
            item.AttachmentCompletionAttemptId == attempt, "Execution clone/dry-run progress erased protected manual evidence or relabeled it automatic.");
        Check(db.Logs.Count == 2 && protectedLog.AttachmentCompletionDisplay == "사용자 확인" && protectedLog.AttachmentCompletionAttemptId == attempt &&
            db.Logs[1].Result == "드라이런 확인 완료" && db.Logs[1].AttachmentCompletionKind == null && client.Calls == 1,
            "Dry run changed original manual log evidence or claimed it as new delivery evidence.");
    }
    private static void ReviewUsesCapturedFileMetadata()
    {
        using var f = new Fixture(); var item = f.Item(); var reviewed = ReviewedAttachmentBatch.Capture(new[] { item });
        item.AttachmentPaths.Clear(); File.Delete(f.First);
        var describe = typeof(ReviewWindow).GetMethod("Describe", BindingFlags.Static | BindingFlags.NonPublic)!;
        Check(describe.GetParameters().Length == 2, "Review does not accept captured attachment metadata.");
        string description = (string)describe.Invoke(null, new object[] { item, reviewed })!;
        Check(description.Contains("ordinary.txt", StringComparison.Ordinal) && description.Contains(f.First, StringComparison.Ordinal) && description.Contains("4바이트", StringComparison.Ordinal), "Review did not show captured file name/size/path independently of current files.");
        Check(!description.Contains(reviewed.Items[0].Files[0].Sha256, StringComparison.Ordinal), "Technical file hash leaked into the normal review text.");
    }
    private sealed class FakeClient : IChatClient
    {
        public int Calls;
        public Action? DuringText;
        public Task<ChatTarget> ResolveAsync(string room, CancellationToken token) { Calls++; return Task.FromResult(new ChatTarget(1, 2, room, 3)); }
        public Task SendTextAsync(ChatTarget target, string text, CancellationToken token) { Calls++; DuringText?.Invoke(); return Task.CompletedTask; }
        public Task SendFilesAsync(ChatTarget target, IReadOnlyList<string> paths, CancellationToken token) { Calls++; return Task.CompletedTask; }
        public Task CompleteAsync(ChatTarget target, CancellationToken token) { Calls++; return Task.CompletedTask; }
    }
    private sealed class Fixture : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "NoticeSender-reviewed-execution-" + Guid.NewGuid().ToString("N"));
        public string First => Path.Combine(directory, "ordinary.txt");
        public string Second => Path.Combine(directory, "second.csv");
        public Fixture() { Directory.CreateDirectory(directory); File.WriteAllBytes(First, [1, 2, 3, 4]); File.WriteAllBytes(Second, [5, 6, 7, 8]); }
        public BatchItem Item() => new() { ChatRoomName = "ordinary reviewed fixture room", Message = "reviewed message", AttachmentPaths = [First, Second] };
        public void Dispose() { File.Delete(First); File.Delete(Second); Directory.Delete(directory); }
    }
}
