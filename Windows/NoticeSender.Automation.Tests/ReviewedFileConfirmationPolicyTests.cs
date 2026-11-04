using System.Reflection;
using NoticeSender.Automation;
using NoticeSender.Core;

internal static class ReviewedFileConfirmationPolicyTests
{
    private static Type PolicyType => typeof(IChatClient).Assembly.GetType("NoticeSender.Automation.ReviewedFileConfirmationPolicy")
        ?? throw new Exception("ReviewedFileConfirmationPolicy does not exist: ordinary reviewed attachments cannot use native confirmation.");
    private static object Policy(ApprovedAttachmentBatch batch, Action<ChatTarget, nint, IReadOnlyList<string>> capture) => Activator.CreateInstance(PolicyType, batch, capture)!;
    private static object Call(object value, string name, params object[] args)
    {
        try { return value.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(value, args)!; }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException or OperationCanceledException) { return; }
        throw new Exception("Unsafe reviewed native confirmation was accepted.");
    }
    public static Task ReviewedScopeAndOneAcquisition()
    {
        using var f = new Fixture(); var policy = Policy(f.Batch, (_, _, _) => { });
        if (!(bool)Call(policy, "IsRoomApproved", f.Target.RoomName) || (bool)Call(policy, "IsRoomApproved", "unreviewed room")) throw new Exception("Wrong reviewed room approval scope.");
        using var lease = (IDisposable)Call(policy, "Freeze", f.Target, new[] { f.Path }, (Func<uint>)(() => 7));
        Call(lease, "RequireUnchanged");
        Reject(() => Call(policy, "Freeze", f.Target, new[] { f.Path }, (Func<uint>)(() => 7)));
        var other = Policy(f.Batch, (_, _, _) => { });
        Reject(() => Call(other, "Freeze", f.Target with { RoomName = "unreviewed room" }, new[] { f.Path }, (Func<uint>)(() => 7)));
        var wrongFiles = Policy(f.Batch, (_, _, _) => { });
        Reject(() => Call(wrongFiles, "Freeze", f.Target, Array.Empty<string>(), (Func<uint>)(() => 7)));
        Reject(() => Call(wrongFiles, "Freeze", f.Target, new[] { f.Path }, (Func<uint>)(() => 7)));
        return Task.CompletedTask;
    }
    public static Task CaptureInputScopeCancellationGuards()
    {
        foreach (string mode in new[] { "normal", "capture-fails", "input", "scope", "disposed", "cancel" })
        {
            using var f = new Fixture(); uint input = 7; int captured = 0, dispatches = 0; bool changed = false; using var cancel = new CancellationTokenSource();
            var policy = Policy(f.Batch, (target, modal, paths) =>
            {
                captured++;
                if (target != f.Target || modal != 999 || !paths.SequenceEqual(new[] { f.Path })) throw new Exception("Capture used a different reviewed target/manifest.");
                if (mode == "capture-fails") throw new InvalidOperationException("required audit unavailable");
                if (mode == "input") input++;
                if (mode == "scope") changed = true;
                if (mode == "disposed") f.Batch.Dispose();
                if (mode == "cancel") cancel.Cancel();
            });
            using var lease = (IDisposable)Call(policy, "Freeze", f.Target, new[] { f.Path }, (Func<uint>)(() => input));
            Action confirm = () => Call(lease, "Confirm", (nint)999, (Action)(() => { if (changed) throw new InvalidOperationException("modal changed"); }), (Action)(() => dispatches++), cancel.Token);
            if (mode == "normal") confirm(); else Reject(confirm);
            Reject(confirm);
            Reject(() => Call(policy, "Freeze", f.Target, new[] { f.Path }, (Func<uint>)(() => input)));
            if (captured != 1 || dispatches != (mode == "normal" ? 1 : 0)) throw new Exception("Unsafe dispatch/capture count after " + mode);
        }
        return Task.CompletedTask;
    }
    public static Task PinnedBinaryAndExternalLifetime()
    {
        using var f = new Fixture(); var policy = Policy(f.Batch, (_, _, _) => { });
        Reject(() => Call(policy, "Acquire", f.Target, new[] { f.Path })); // Fixture process cannot be the pinned Kakao image.
        Reject(() => Call(policy, "Freeze", f.Target, new[] { f.Path }, (Func<uint>)(() => 7)));
        var another = Policy(f.Batch, (_, _, _) => { });
        var lease = (IDisposable)Call(another, "Freeze", f.Target, new[] { f.Path }, (Func<uint>)(() => 7));
        lease.Dispose();
        f.Batch.RequireMatch(f.Target, new[] { f.Path }); // Policy lease does not own/dispose the batch read locks.
        Reject(() => Call(lease, "Confirm", (nint)999, (Action)(() => { }), (Action)(() => throw new Exception("Disposed lease dispatched")), CancellationToken.None));
        Type? baseType = typeof(IChatClient).Assembly.GetType("NoticeSender.Automation.NativeFileConfirmationPolicy");
        if (baseType == null || !baseType.IsAssignableFrom(PolicyType) || !baseType.IsAssignableFrom(typeof(SyntheticFileConfirmationPolicy)) ||
            !typeof(WindowsKakaoClient).GetConstructors().SelectMany(c => c.GetParameters()).Any(p => p.Name == "nativeFileConfirmation" && p.ParameterType == baseType))
            throw new Exception("Native adapter is not wired to the explicit common policy.");
        return Task.CompletedTask;
    }
    private sealed class Fixture : IDisposable
    {
        private readonly string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "NoticeSender-general-confirm-" + Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(directory, "ordinary-reviewed-private-file.txt");
        public ChatTarget Target { get; } = new(Environment.ProcessId, 123, "ordinary reviewed unit-test room", 456);
        public ApprovedAttachmentBatch Batch { get; }
        public Fixture() { Directory.CreateDirectory(directory); File.WriteAllText(Path, "normal reviewed bytes"); Batch = ReviewedAttachmentBatch.Capture(new[] { new BatchItem { ChatRoomName = Target.RoomName, AttachmentPaths = [Path] } }).OpenVerifiedFiles(); }
        public void Dispose() { Batch.Dispose(); File.Delete(Path); Directory.Delete(directory); }
    }
}
