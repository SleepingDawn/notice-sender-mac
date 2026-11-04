using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;
using NoticeSender.Automation;
using NoticeSender.Core;

internal static class ManualAttachmentCompletionTests
{
    private static Type? fakeType;
    private static Type Type(string name) => typeof(IChatClient).Assembly.GetType("NoticeSender.Automation." + name)
        ?? throw new Exception(name + " does not exist: manual completion cannot be distinguished from automatic proof.");
    private static object Unwrap(Func<object> work)
    { try { return work(); } catch (TargetInvocationException ex) when (ex.InnerException != null) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; } }
    private static object? Get(object value, string name) => value.GetType().GetProperty(name)!.GetValue(value);
    private static object Outcome(BatchItem item, ChatTarget? target = null, IReadOnlyList<string>? paths = null, Guid? batchId = null,
        Guid? attemptId = null, DateTimeOffset? at = null, string audit = "immutable-manual-audit.json") =>
        Unwrap(() => Activator.CreateInstance(Type("AttachmentCompletionOutcome"), attemptId ?? Guid.NewGuid(), batchId ?? item.Id,
            target ?? ManualOutcomeFakeClient.Target(item.ChatRoomName), paths ?? item.AttachmentPaths, at ?? DateTimeOffset.UtcNow, audit)!);
    private static ManualOutcomeFakeClient Fake()
    {
        if (fakeType == null)
        {
            Type source = Type("IAttachmentCompletionOutcomeSource"), outcome = Type("AttachmentCompletionOutcome");
            var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("NoticeSenderManualOutcomeFixtures"), AssemblyBuilderAccess.Run);
            var builder = assembly.DefineDynamicModule("fixtures").DefineType("OutcomeFake", TypeAttributes.Public, typeof(ManualOutcomeFakeClient), new[] { source });
            var getter = builder.DefineMethod("get_LastAttachmentCompletion", MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.SpecialName | MethodAttributes.HideBySig, outcome, System.Type.EmptyTypes);
            var body = getter.GetILGenerator(); body.Emit(OpCodes.Ldarg_0); body.Emit(OpCodes.Ldfld, typeof(ManualOutcomeFakeClient).GetField(nameof(ManualOutcomeFakeClient.Outcome))!); body.Emit(OpCodes.Castclass, outcome); body.Emit(OpCodes.Ret);
            var property = builder.DefineProperty("LastAttachmentCompletion", PropertyAttributes.None, outcome, null); property.SetGetMethod(getter);
            builder.DefineMethodOverride(getter, source.GetProperty("LastAttachmentCompletion")!.GetMethod!); fakeType = builder.CreateType()!;
        }
        return (ManualOutcomeFakeClient)Activator.CreateInstance(fakeType)!;
    }
    private static BatchItem Item(string room) => new() { ChatRoomName = room, Message = "reviewed text", AttachmentPaths = [Path.GetFullPath(room + "-first.txt"), Path.GetFullPath(room + "-second.csv")] };
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Reject(Action work)
    { try { work(); } catch (Exception ex) when (ex is InvalidOperationException or ArgumentException) { return; } throw new Exception("Uncorrelated manual completion was accepted."); }

    public static async Task AcceptedIsExplicitAndAppliedOnlyAfterComplete()
    {
        foreach (bool wrapped in new[] { false, true })
        {
            BatchItem first = Item("manual-first"), next = Item("automatic-next"); next.AttachmentPaths.Clear(); var db = new AppDatabase(); var fake = Fake();
            fake.Review = (_, _, _) => Outcome(first);
            fake.BeforeComplete = target => { if (target.RoomName == first.ChatRoomName) Check(Get(first, "AttachmentCompletionKind") == null && Get(db.Logs.Single(), "AttachmentCompletionKind") == null, "Manual metadata was committed before final completion checks."); };
            IChatClient client = wrapped ? new OpeningVerifiedChatClient(fake) : fake;
            await new SendCoordinator(client).RunAsync(db, new[] { first, next }, false, _ => { }, () => { }, default);
            var log = db.Logs[0];
            Check(first.Status == "전송 요청 완료" && next.Status == "전송 요청 완료", "Audited manual acceptance did not allow the next row to proceed.");
            Check((string?)Get(log, "AttachmentCompletionKind") == "user-confirmed" && (Guid?)Get(log, "AttachmentCompletionBatchId") == first.Id && Get(log, "AttachmentCompletionConfirmedAt") != null && Get(log, "AttachmentCompletionAttemptId") != null && (string?)Get(log, "AttachmentCompletionAuditReference") == "immutable-manual-audit.json", "Manual completion lost required audit correlation.");
            Check((string?)Get(first, "AttachmentCompletionDisplay") == "사용자 확인" && (string?)Get(log, "AttachmentCompletionDisplay") == "사용자 확인" && (string?)Get(next, "AttachmentCompletionDisplay") == "앱 확인", "Manual completion masqueraded as automatic in display.");
            string json = JsonSerializer.Serialize(db); Check(!json.Contains("AttachmentCompletionDisplay", StringComparison.OrdinalIgnoreCase), "Display-only labels leaked into shared database JSON.");
            var recovered = JsonSerializer.Deserialize<AppDatabase>(json)!;
            Check(recovered.Logs[0].Result == "전송 요청 완료" && (string?)Get(recovered.Logs[0], "AttachmentCompletionKind") == "user-confirmed", "Manual metadata or compatible raw status did not survive reload.");
            int before = fake.Trace.Count; first.Status = "준비";
            await new SendCoordinator(client).RunAsync(recovered, new[] { first }, false, _ => { }, () => { }, default);
            Check(fake.Trace.Count == before, "Accepted manual delivery was automatically resent after reload.");
        }
    }
    public static async Task DeclineCancellationAndAuditFailureStayUncertain()
    {
        foreach (string mode in new[] { "decline", "cancel", "audit-failure" })
        {
            BatchItem first = Item("manual-first"), next = Item("next"); var db = new AppDatabase(); var fake = Fake(); using var cancel = new CancellationTokenSource();
            fake.Review = (_, _, _) => { if (mode == "cancel") { cancel.Cancel(); throw new OperationCanceledException(cancel.Token); } throw new InvalidOperationException(mode); };
            await new SendCoordinator(fake).RunAsync(db, new[] { first, next }, false, _ => { }, () => { }, cancel.Token);
            Check(first.Status == "확인 필요" && next.Status == "준비" && db.Logs.Count == 1 && Get(first, "AttachmentCompletionKind") == null && !fake.Trace.Contains("complete:" + first.ChatRoomName), "Decline/cancellation/audit failure was accepted or continued the batch.");
            int before = fake.Trace.Count; first.Status = "준비";
            await new SendCoordinator(fake).RunAsync(db, new[] { first }, false, _ => { }, () => { }, default);
            Check(fake.Trace.Count == before, "Uncertain manual review was automatically resent.");
        }
    }
    public static async Task RejectMismatchedOutcomeBeforeCompletion()
    {
        foreach (string field in new[] { "batch", "pid", "root", "composer", "room", "paths", "order", "audit" })
        {
            var item = Item("manual-first"); var next = Item("next"); var fake = Fake(); var target = ManualOutcomeFakeClient.Target(item.ChatRoomName);
            fake.Review = (_, _, _) => Outcome(item,
                target: field switch { "pid" => target with { ProcessId = 18 }, "root" => target with { WindowHandle = 31 }, "composer" => target with { ComposerHandle = 32 }, "room" => target with { RoomName = "other room" }, _ => target },
                paths: field == "paths" ? [Path.GetFullPath("other.txt")] : field == "order" ? item.AttachmentPaths.AsEnumerable().Reverse().ToArray() : null,
                batchId: field == "batch" ? Guid.NewGuid() : null, audit: field == "audit" ? "" : "audit.json");
            await new SendCoordinator(fake).RunAsync(new(), new[] { item, next }, false, _ => { }, () => { }, default);
            Check(item.Status == "확인 필요" && next.Status == "준비" && Get(item, "AttachmentCompletionKind") == null && !fake.Trace.Contains("complete:" + item.ChatRoomName), "Mismatched manual " + field + " was accepted.");
        }
    }
    public static async Task FinalCheckFailureDoesNotCommitManualCompletion()
    {
        BatchItem item = Item("manual-first"), next = Item("next"); var db = new AppDatabase(); var fake = Fake(); fake.Review = (_, _, _) => Outcome(item);
        fake.BeforeComplete = _ => throw new InvalidOperationException("progress/modal appeared after user review");
        await new SendCoordinator(fake).RunAsync(db, new[] { item, next }, false, _ => { }, () => { }, default);
        Check(item.Status == "확인 필요" && next.Status == "준비" && Get(item, "AttachmentCompletionKind") == null && Get(db.Logs.Single(), "AttachmentCompletionKind") == null, "Failed final checks committed manual completion metadata.");
    }
    public static async Task StalePreviousRowCannotAuthorizeNext()
    {
        BatchItem first = Item("manual-first"), next = Item("manual-next"), last = Item("last"); var db = new AppDatabase(); var fake = Fake(); fake.PreserveOutcome = true;
        fake.Review = (target, _, _) => target.RoomName == first.ChatRoomName ? Outcome(first) : fake.Outcome;
        await new SendCoordinator(fake).RunAsync(db, new[] { first, next, last }, false, _ => { }, () => { }, default);
        Check(first.Status == "전송 요청 완료" && next.Status == "확인 필요" && last.Status == "준비" && Get(db.Logs[1], "AttachmentCompletionKind") == null, "Stale manual outcome authorized the next recipient.");
    }
    public static Task RequestIsImmutableAndRequiresExactAuditedAttempt()
    {
        var item = Item("manual-first"); var paths = item.AttachmentPaths.ToList(); var attempt = Guid.NewGuid(); var submitted = DateTimeOffset.UtcNow.AddSeconds(-1); var target = ManualOutcomeFakeClient.Target(item.ChatRoomName);
        object request = Activator.CreateInstance(Type("ManualAttachmentRequest"), attempt, target, paths, submitted)!; paths.Clear();
        Check(((System.Collections.IEnumerable)Get(request, "Paths")!).Cast<string>().SequenceEqual(item.AttachmentPaths), "Manual request did not freeze reviewed paths.");
        void Match(object value) => Unwrap(() => { request.GetType().GetMethod("RequireMatch")!.Invoke(request, new[] { value }); return true; });
        Match(Outcome(item, attemptId: attempt));
        Reject(() => Match(Outcome(item, attemptId: Guid.NewGuid())));
        Reject(() => Match(Outcome(item, target: target with { WindowHandle = 999 }, attemptId: attempt)));
        Reject(() => Match(Outcome(item, paths: item.AttachmentPaths.AsEnumerable().Reverse().ToArray(), attemptId: attempt)));
        Reject(() => Match(Outcome(item, at: submitted.AddSeconds(-1), attemptId: attempt)));
        Reject(() => Outcome(item, audit: ""));
        Check(Type("IManualAttachmentReview").GetMethod("Review")!.ReturnType == Type("AttachmentCompletionOutcome"), "Manual callback contract changed.");
        return Task.CompletedTask;
    }
}

// Public so the runtime-generated interface fixture can derive from it before the new contract exists.
public class ManualOutcomeFakeClient : IChatClient, IDryRunChatClient
{
    public object? Outcome;
    public bool PreserveOutcome;
    public readonly List<string> Trace = new();
    public Func<ChatTarget, IReadOnlyList<string>, CancellationToken, object?>? Review;
    public Action<ChatTarget>? BeforeComplete;
    public static ChatTarget Target(string room) => new(17, 21, room, 22);
    public Task<RoomOpenDryRunResult> OpenRoomDryRunAsync(string room, CancellationToken token) => Task.FromResult(new RoomOpenDryRunResult(room, false, Target(room)));
    public Task<ChatTarget> ResolveAsync(string room, CancellationToken token) { Trace.Add("resolve:" + room); return Task.FromResult(Target(room)); }
    public Task SendTextAsync(ChatTarget target, string text, CancellationToken token) { Trace.Add("text:" + target.RoomName); return Task.CompletedTask; }
    public Task SendFilesAsync(ChatTarget target, IReadOnlyList<string> paths, CancellationToken token)
    { Trace.Add("files:" + target.RoomName); if (!PreserveOutcome) Outcome = null; if (Review != null) Outcome = Review(target, paths, token); return Task.CompletedTask; }
    public Task CompleteAsync(ChatTarget target, CancellationToken token) { Trace.Add("complete:" + target.RoomName); BeforeComplete?.Invoke(target); return Task.CompletedTask; }
}
