using System.Reflection;
using NoticeSender.Automation;
using NoticeSender.Core;

internal static class OpeningVerifiedChatClientTests
{
    private const string Room = "precisely reviewed room";
    private static readonly ChatTarget Target = new(17, 21, Room, 22);
    private static IChatClient Wrap(IChatClient client)
    {
        Type type = typeof(IChatClient).Assembly.GetType("NoticeSender.Automation.OpeningVerifiedChatClient")
            ?? throw new Exception("OpeningVerifiedChatClient does not exist: a closed reviewed room cannot be independently opened and resolved.");
        try { return (IChatClient)Activator.CreateInstance(type, client)!; }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static async Task Reject(Func<Task> work)
    {
        try { await work(); }
        catch (Exception ex) when (ex is InvalidOperationException or OperationCanceledException or ArgumentException) { return; }
        throw new Exception("An unverified opened room was accepted.");
    }
    public static async Task OpensThenStrictlyResolves()
    {
        foreach (bool alreadyOpen in new[] { false, true })
        {
            var fake = new FakeClient { Closed = !alreadyOpen, WasAlreadyOpen = alreadyOpen }; var client = Wrap(fake);
            var result = await client.ResolveAsync(Room, default);
            Check(result == Target && fake.Trace.SequenceEqual(new[] { "open:" + Room, "resolve:" + Room }), "Opening did not precede independent exact resolution.");
        }
        var transport = new FakeClient(); var batch = new BatchItem { ChatRoomName = Room, Message = "reviewed text", AttachmentPaths = ["reviewed.txt"] };
        await new SendCoordinator(Wrap(transport)).RunAsync(new(), new[] { batch }, false, _ => { }, () => { }, default);
        Check(batch.Status == "전송 요청 완료" && transport.Trace.SequenceEqual(new[] { "open:" + Room, "resolve:" + Room, "text:reviewed text", "files:reviewed.txt", "complete" }), "Verified opening changed transport ordering.");
    }
    public static async Task InvalidOpeningStopsBeforeStrictResolution()
    {
        foreach (var wrong in new[] { Target with { ProcessId = 0 }, Target with { WindowHandle = 0 }, Target with { ComposerHandle = 0 }, Target with { RoomName = "wrong room" }, Target with { ComposerHandle = Target.WindowHandle } })
        {
            var fake = new FakeClient { OpenTarget = wrong }; var client = Wrap(fake);
            await Reject(() => client.ResolveAsync(Room, default));
            Check(fake.Trace.SequenceEqual(new[] { "open:" + Room }), "Invalid opening reached strict resolver or transport.");
        }
        var wrongResult = new FakeClient { OpenRoomName = "wrong result room" };
        await Reject(() => Wrap(wrongResult).ResolveAsync(Room, default));
        Check(wrongResult.Trace.Count == 1, "Opening result title mismatch reached strict resolver.");
        var empty = new FakeClient(); await Reject(() => Wrap(empty).ResolveAsync(" ", default)); Check(empty.Trace.Count == 0, "Empty destination entered room opening.");
    }
    public static async Task StrictIdentityChangesStopBeforeSend()
    {
        foreach (var wrong in new[] { Target with { ProcessId = 18 }, Target with { WindowHandle = 31 }, Target with { ComposerHandle = 32 }, Target with { RoomName = "wrong room" }, Target with { ComposerHandle = 0 } })
        {
            var fake = new FakeClient { StrictTarget = wrong }; var batch = new BatchItem { ChatRoomName = Room, Message = "must never send" };
            await new SendCoordinator(Wrap(fake)).RunAsync(new(), new[] { batch }, false, _ => { }, () => { }, default);
            Check(batch.Status == "실패" && fake.Trace.SequenceEqual(new[] { "open:" + Room, "resolve:" + Room }), "Changed opened target entered transport or automatic retry.");
        }
    }
    public static async Task CancellationAndOpeningFailuresStop()
    {
        foreach (string phase in new[] { "before", "open", "resolve", "open-error", "resolve-error" })
        {
            using var cancel = new CancellationTokenSource(); var fake = new FakeClient();
            if (phase == "before") cancel.Cancel();
            if (phase == "open") fake.OnOpen = cancel.Cancel;
            if (phase == "resolve") fake.OnResolve = cancel.Cancel;
            if (phase == "open-error") fake.OnOpen = () => throw new InvalidOperationException("opener failed");
            if (phase == "resolve-error") fake.OnResolve = () => throw new InvalidOperationException("strict resolution failed");
            await Reject(() => Wrap(fake).ResolveAsync(Room, cancel.Token));
            int expected = phase == "before" ? 0 : phase is "open" or "open-error" ? 1 : 2;
            Check(fake.Trace.Count == expected && !fake.Trace.Any(value => value.StartsWith("text:", StringComparison.Ordinal)), "Cancellation or opening failure performed another operation: " + phase);
        }
    }
    public static async Task DryRunAndTransportAreDelegated()
    {
        var fake = new FakeClient(); var client = Wrap(fake); var dry = (IDryRunChatClient)client;
        var opened = await dry.OpenRoomDryRunAsync(Room, default);
        Check(opened.Target == Target && fake.Trace.SequenceEqual(new[] { "open:" + Room }), "Dry run unexpectedly entered strict resolution or sending.");
        fake.Trace.Clear();
        await client.SendTextAsync(Target, "exact text", default); await client.SendFilesAsync(Target, new[] { "exact path" }, default); await client.CompleteAsync(Target, default);
        Check(fake.Trace.SequenceEqual(new[] { "text:exact text", "files:exact path", "complete" }), "Decorator changed payloads or introduced extra opening/resolution.");
    }
    private sealed class FakeClient : IChatClient, IDryRunChatClient
    {
        public readonly List<string> Trace = new();
        public bool Closed = true, WasAlreadyOpen;
        public string OpenRoomName = Room;
        public ChatTarget OpenTarget = Target, StrictTarget = Target;
        public Action? OnOpen, OnResolve;
        public Task<RoomOpenDryRunResult> OpenRoomDryRunAsync(string room, CancellationToken token)
        { Trace.Add("open:" + room); OnOpen?.Invoke(); Closed = false; return Task.FromResult(new RoomOpenDryRunResult(OpenRoomName, WasAlreadyOpen, OpenTarget)); }
        public Task<ChatTarget> ResolveAsync(string room, CancellationToken token)
        { Trace.Add("resolve:" + room); OnResolve?.Invoke(); if (Closed) throw new InvalidOperationException("Room is still closed."); return Task.FromResult(StrictTarget); }
        public Task SendTextAsync(ChatTarget target, string text, CancellationToken token) { Trace.Add("text:" + text); return Task.CompletedTask; }
        public Task SendFilesAsync(ChatTarget target, IReadOnlyList<string> paths, CancellationToken token) { Trace.Add("files:" + string.Join("|", paths)); return Task.CompletedTask; }
        public Task CompleteAsync(ChatTarget target, CancellationToken token) { Trace.Add("complete"); return Task.CompletedTask; }
    }
}
