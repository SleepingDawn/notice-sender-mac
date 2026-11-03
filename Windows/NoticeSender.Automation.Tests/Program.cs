using NoticeSender.Automation;
using NoticeSender.Core;

if (args.Any(a => a.StartsWith("--live-", StringComparison.Ordinal))) return await LiveSmoke.RunAsync(args);
if (args.Contains("--hold-room-opening-lease")) return NativeRoomOpeningTests.HoldRoomOpeningLease();

var tests = new (string, Func<Task>)[] {
    ("dry run resolves only, persists a log, and never enters text/files/Enter", DryRun),
    ("success records each transition and sends reviewed messages in order", Success),
    ("ambiguous room stops the entire batch before any delivery", Ambiguity),
    ("cancellation during first send becomes uncertain and never retries", PartialCancellation),
    ("exception after possible delivery becomes uncertain and stops", PartialFailure),
    ("cancellation before delivery leaves a safe retryable item", BeforeDeliveryCancellation),
    ("sent, uncertain and excluded items never resolve again", SkipProtected),
    ("persistence failure prevents all delivery", PersistenceFailure),
    ("duplicate destinations stop before resolution", DuplicateDestinations),
    ("empty dry run resolves without message entry", EmptyDryRun),
    ("cancellation during resolution fails safely", ResolveCancellation),
    ("reviewed values remain frozen across awaits", Snapshot),
    ("durable sent logs block automatic retry", DurableRetry),
    ("file send failure becomes uncertain after text and stops batch", FileFailure),
    ("single and multiple attachments follow all reviewed messages", Files),
    ("cancelled native operations never inspect or modify desktop", NativeCancelled),
    ("native adapter rejects impossible target before text entry", NativeInvalidTarget),
    ("native adapter rejects missing attachment before desktop access", NativeMissingFile),
    ("direct message preserves exact reviewed whitespace", Whitespace),
    ("dry run can inspect a sent item while retaining sent status", DryRunSent),
    ("attachment-only batch sends files without empty text", AttachmentOnly),
    ("native discovery includes top-level controls without a Window UIA role", NativeEnumerationTests.CustomTopLevel),
    ("interrupted dry run never creates a protected delivery log", InterruptedDryRun),
    ("native room opening queries the owned search and queues Enter only to its visible result list", NativeRoomOpeningTests.OwnedUnnamedSearch)
    ,("coordinator dry run uses the app opener without entering delivery", DryRunOpener)
    ,("coordinator actual delivery retains strict resolution", ActualStrictResolver)
    ,("native room search rejects a locked main window", NativeRoomOpeningTests.LockedMain)
    ,("native room search rejects an inactive chat view", NativeRoomOpeningTests.InactiveChatView)
    ,("native room search rejects duplicate search edits", NativeRoomOpeningTests.DuplicateEdits)
    ,("native room search rejects changed view ownership before entry", NativeRoomOpeningTests.ChangedOwnership)
    ,("native room search cancellation happens before any query or Enter", NativeRoomOpeningTests.CancelledSearch)
    ,("native room search excludes windows from another process", NativeRoomOpeningTests.ForeignProcess)
    ,("native room search rejects a query changed before Enter", NativeRoomOpeningTests.ChangedQuery)
    ,("native room search can activate the owned hidden edit through its query notification", NativeRoomOpeningTests.HiddenSearch)
    ,("room opening is serialized across processes before any desktop operation", NativeRoomOpeningTests.CrossProcessLease)
    ,("native room search rejects a visible conflicting main view", NativeRoomOpeningTests.VisibleOtherView)
    ,("delayed native search results are reconfirmed until the exact room opens", NativeRoomOpeningTests.DelayedResults)
    ,("native result confirmations stop at the bounded retry limit", NativeRoomOpeningTests.CappedConfirmations)
    ,("native result readiness cancellation prevents another confirmation", NativeRoomOpeningTests.CancelDuringReadiness)
    ,("native query changes while waiting prevent another confirmation", NativeRoomOpeningTests.QueryChangesDuringReadiness)
    ,("native ownership changes while waiting prevent another confirmation", NativeRoomOpeningTests.OwnershipChangesDuringReadiness)
    ,("opening-only inspection accepts an exact read-only room without send capability", NativeRoomOpeningTests.ReadOnlyOpeningCapability)
    ,("opening-only inspection rejects multiple composer candidates", NativeRoomOpeningTests.AmbiguousOpeningCapability)
    ,("lesson dry run rejects a room that opened without a send input", ReadOnlyLessonDryRun)
    ,("composer preflight verifies focus before reading a draft", ComposerPreflightTests.VerifyBeforeRead)
    ,("composer preflight rejects denied focus before any draft read", ComposerPreflightTests.DeniedFocus)
    ,("composer preflight waits for the application to clear its own cue", ComposerPreflightTests.DelayedCueClear)
    ,("composer preflight preserves arbitrary nonempty drafts", ComposerPreflightTests.PreserveDraft)
    ,("composer preflight stops immediately when verified focus is lost", ComposerPreflightTests.FocusLoss)
    ,("composer preflight cancellation prevents draft reads", ComposerPreflightTests.Cancellation)
    ,("a fresh draft after preflight blocks outgoing text mutation", ComposerPreflightTests.FreshDraftBeforeWrite)
    ,("an edit after preview blocks send confirmation", ComposerPreflightTests.EditedTextBeforeSend)
    ,("a fresh draft during attachment preparation blocks attachment invocation", ComposerPreflightTests.FreshDraftBeforeAttachment)
    ,("cancellation during final checks blocks native-text read and mutation", ComposerPreflightTests.CancelDuringFinalChecks)
    ,("visible modeless channel menu does not block composer preflight", NativeRoomOpeningTests.ModelessOwnedMenu)
    ,("owned modal classification blocks disabled-owner popups and ignores hidden popups", NativeRoomOpeningTests.DisabledOwnerModal)
    ,("standard visible owned dialogs still block preflight", NativeRoomOpeningTests.StandardOwnedDialog)
    ,("attachment discovery excludes the persistent modeless-window baseline", NativeRoomOpeningTests.NewAttachmentWindow)
    ,("native semantic controls accept only a concrete owned child", NativeRoomOpeningTests.OwnedNativeControl)
    ,("virtual semantic controls without an HWND fail closed", NativeRoomOpeningTests.RejectVirtualSemanticControl)
    ,("semantic controls reject foreign processes, other rooms and owned popups", NativeRoomOpeningTests.RejectForeignSemanticControl)
    ,("semantic controls reject reparenting after pattern resolution", NativeRoomOpeningTests.RejectReparentedSemanticControl)
    ,("file-name edits must belong to the exact file dialog", NativeRoomOpeningTests.FilenameEditScope)
    ,("cancellation during a native-text read prevents subsequent mutation", ComposerPreflightTests.CancelDuringNativeRead)
    ,("changed filenames after final ownership checks prevent file-open invocation", ComposerPreflightTests.FilenameChangesBeforeOpen)
};
int failures = 0;
foreach (var (name, test) in tests) { try { await test(); Console.WriteLine("PASS " + name); } catch (Exception e) { failures++; Console.WriteLine("FAIL " + name + ": " + e.Message); } }
Console.WriteLine($"{tests.Length - failures}/{tests.Length} passed");
return failures == 0 ? 0 : 1;

static BatchItem Item(string room = "테스트 전용 방") => new() { Id = Guid.NewGuid(), StudentId = Guid.NewGuid(), StudentName = "가상 학생", ChatRoomName = room, Message = "첫 공지", CommonMessages = ["추가 공지"], AttachmentPaths = [], Status = "준비" };
static void Check(bool ok, string detail) { if (!ok) throw new Exception(detail); }
static async Task DryRun() {
    var fake = new FakeClient(); var db = new AppDatabase(); var item = Item(); item.AttachmentPaths.Add("never-open-this-file"); int saved = 0;
    await new SendCoordinator(fake).RunAsync(db, [item], true, _ => { }, () => saved++, default);
    Check(fake.Resolves == 1 && fake.Effects.Count == 0, "dry run caused a side effect or skipped resolution");
    Check(item.Status == "드라이런 확인 완료" && db.Logs.Count == 1 && saved >= 2, "dry run result not persisted");
}
static async Task DryRunOpener() {
    var client = new OpeningClient(); var item = Item(); item.AttachmentPaths.Add("never-read-this.txt");
    await new SendCoordinator(client).RunAsync(new(), [item], true, _ => { }, () => { }, default);
    Check(item.Status == "드라이런 확인 완료" && client.Opened == 1 && client.Delivered == 0, "dry run failed to open and verify the room safely");
}
static async Task ActualStrictResolver() {
    var client = new OpeningClient(); var item = Item();
    await new SendCoordinator(client).RunAsync(new(), [item], false, _ => { }, () => { }, default);
    Check(item.Status == "실패" && client.Opened == 0 && client.Delivered == 0, "native dry-run opener authorized actual delivery");
}
static async Task ReadOnlyLessonDryRun() {
    var client = new OpeningClient(false); var item = Item(); var database = new AppDatabase();
    await new SendCoordinator(client).RunAsync(database, [item], true, _ => { }, () => { }, default);
    Check(item.Status == "실패" && database.Logs.Single().Detail?.Contains("채팅방은 열렸으나 발송 입력란이 없습니다", StringComparison.Ordinal) == true && client.Delivered == 0,
        "lesson preflight treated a read-only room as send-capable");
}
static async Task Success() {
    var fake = new FakeClient(); var db = new AppDatabase(); var item = Item(); var states = new List<string>();
    await new SendCoordinator(fake).RunAsync(db, [item], false, i => states.Add(i.Status), () => { }, default);
    Check(fake.Effects.SequenceEqual(new[] { "text:첫 공지", "text:추가 공지", "complete" }), "wrong side effect sequence");
    Check(states.SequenceEqual(new[] { "전송 중", "전송 요청 완료" }) && db.Logs.Count == 1 && db.Logs[0].MessageSHA256.Length == 64, "missing durable result");
}
static async Task Ambiguity() {
    var fake = new FakeClient { ResolveError = new InvalidOperationException("동일 이름 방이 두 개") }; var db = new AppDatabase(); var a = Item(); var b = Item("다른 방");
    await new SendCoordinator(fake).RunAsync(db, [a, b], false, _ => { }, () => { }, default);
    Check(fake.Resolves == 1 && fake.Effects.Count == 0 && a.Status == "실패" && b.Status == "준비" && db.Logs.Count == 1, "ambiguity did not stop safely");
}
static async Task PartialCancellation() {
    using var cts = new CancellationTokenSource(); var fake = new FakeClient { AfterText = () => { cts.Cancel(); throw new OperationCanceledException(cts.Token); } }; var db = new AppDatabase(); var a = Item(); var b = Item("다른 방");
    await new SendCoordinator(fake).RunAsync(db, [a, b], false, _ => { }, () => { }, cts.Token);
    Check(a.Status == "확인 필요" && b.Status == "준비" && db.Logs.Single().Result == "확인 필요", "partial cancellation not durable uncertain");
    await new SendCoordinator(fake).RunAsync(db, [a], false, _ => { }, () => { }, default);
    Check(fake.Effects.Count == 1 && fake.Resolves == 1, "uncertain item retried");
}
static async Task PartialFailure() {
    var fake = new FakeClient { AfterText = () => throw new IOException("delivery response lost") }; var db = new AppDatabase(); var a = Item(); var b = Item("다른 방");
    await new SendCoordinator(fake).RunAsync(db, [a,b], false, _ => { }, () => { }, default);
    Check(a.Status == "확인 필요" && b.Status == "준비" && fake.Resolves == 1 && db.Logs.Count == 1, "failure after send was retryable");
}
static async Task BeforeDeliveryCancellation() {
    using var cts = new CancellationTokenSource(); cts.Cancel(); var fake = new FakeClient(); var item = Item();
    await new SendCoordinator(fake).RunAsync(new(), [item], false, _ => { }, () => { }, cts.Token);
    Check(fake.Resolves == 0 && fake.Effects.Count == 0 && item.Status == "준비", "pre-cancelled batch mutated");
}
static async Task SkipProtected() {
    var fake = new FakeClient(); var a = Item(); a.Status = "전송 요청 완료"; var b = Item(); b.Status = "확인 필요"; var c = Item(); c.Status = "제외";
    await new SendCoordinator(fake).RunAsync(new(), [a,b,c], false, _ => { }, () => { }, default);
    Check(fake.Resolves == 0 && fake.Effects.Count == 0, "protected status retried");
}
static async Task PersistenceFailure() {
    var fake = new FakeClient(); try { await new SendCoordinator(fake).RunAsync(new(), [Item()], false, _ => { }, () => throw new IOException("disk full"), default); } catch (IOException) { }
    Check(fake.Effects.Count == 0, "sent without durable pre-send state");
}
static async Task DuplicateDestinations() {
    var fake = new FakeClient(); bool failed = false;
    try { await new SendCoordinator(fake).RunAsync(new(), [Item(), Item()], false, _ => { }, () => { }, default); } catch (InvalidOperationException) { failed = true; }
    Check(failed && fake.Resolves == 0, "duplicate endpoint was not blocked");
}
static async Task EmptyDryRun() {
    var fake = new FakeClient(); var item = Item(); item.Message = ""; item.CommonMessages.Clear();
    await new SendCoordinator(fake).RunAsync(new(), [item], true, _ => { }, () => { }, default);
    Check(item.Status == "드라이런 확인 완료" && fake.Resolves == 1 && fake.Effects.Count == 0, "empty dry run failed");
}
static async Task ResolveCancellation() {
    var fake = new FakeClient { ResolveError = new OperationCanceledException() }; var item = Item(); var db = new AppDatabase();
    await new SendCoordinator(fake).RunAsync(db, [item], false, _ => { }, () => { }, default);
    Check(item.Status == "실패" && fake.Effects.Count == 0 && db.Logs.Single().Result == "실패", "resolve cancellation unsafe");
}
static async Task Snapshot() {
    var item = Item(); var fake = new FakeClient { OnResolve = () => { item.Message = "unreviewed"; item.CommonMessages.Clear(); } };
    await new SendCoordinator(fake).RunAsync(new(), [item], false, _ => { }, () => { }, default);
    Check(fake.Effects.SequenceEqual(new[] { "text:첫 공지", "text:추가 공지", "complete" }), "live edits changed reviewed payload");
}
static async Task DurableRetry() {
    var item = Item(); var fake = new FakeClient(); var db = new AppDatabase();
    db.Logs.Add(new SendLog { BatchId = item.Id, Result = "전송 중" });
    await new SendCoordinator(fake).RunAsync(db, [item], false, _ => { }, () => { }, default);
    Check(fake.Resolves == 0, "interrupted durable log retried");
}
static async Task FileFailure() {
    var fake = new FakeClient { AfterFiles = () => throw new IOException("unknown upload state") }; var item = Item(); item.AttachmentPaths = ["fake-only.txt"]; var next = Item("다음 방"); var db = new AppDatabase();
    await new SendCoordinator(fake).RunAsync(db, [item, next], false, _ => { }, () => { }, default);
    Check(item.Status == "확인 필요" && next.Status == "준비" && fake.Effects.SequenceEqual(new[] { "text:첫 공지", "text:추가 공지", "files:fake-only.txt" }), "file failure continued sending");
}
static async Task Files() {
    foreach (var paths in new[] { new[] { "가상1.txt" }, new[] { "가상1.txt", "가상2.png" } }) {
        var fake = new FakeClient(); var item = Item(); item.CommonMessages.Clear(); item.AttachmentPaths = [.. paths];
        await new SendCoordinator(fake).RunAsync(new(), [item], false, _ => { }, () => { }, default);
        Check(fake.Effects.SequenceEqual(new[] { "text:첫 공지", "files:" + string.Join("|", paths), "complete" }) && item.Status == "전송 요청 완료", "attachment order/payload changed");
    }
}
static async Task Whitespace() {
    var fake = new FakeClient(); var item = Item(); item.Message = "  줄1\n줄2  "; item.PreserveMessageWhitespace = true; item.CommonMessages.Clear();
    await new SendCoordinator(fake).RunAsync(new(), [item], false, _ => { }, () => { }, default);
    Check(fake.Effects[0] == "text:  줄1\n줄2  ", "reviewed whitespace lost");
}
static async Task DryRunSent() {
    foreach (var error in new Exception?[] { null, new InvalidOperationException("ambiguous") }) {
        var fake = new FakeClient { ResolveError = error }; var item = Item(); item.Status = "전송 요청 완료"; var db = new AppDatabase(); db.Logs.Add(new SendLog { BatchId = item.Id, Result = "전송 요청 완료" });
        await new SendCoordinator(fake).RunAsync(db, [item], true, _ => { }, () => { }, default);
        Check(fake.Resolves == 1 && fake.Effects.Count == 0 && item.Status == "전송 요청 완료", "dry-run erased sent state or skipped inspection");
    }
}
static async Task AttachmentOnly() {
    var item = Item(); item.Message = ""; item.CommonMessages.Clear(); item.AttachmentPaths = ["synthetic.txt"]; var fake = new FakeClient();
    await new SendCoordinator(fake).RunAsync(new(), [item], false, _ => { }, () => { }, default);
    Check(fake.Effects.SequenceEqual(new[] { "files:synthetic.txt", "complete" }) && item.Status == "전송 요청 완료", "attachment-only batch refused");
}
static async Task InterruptedDryRun() {
    var db = new AppDatabase(); var item = Item(); var fake = new FakeClient(); string snapshot = "";
    try { await new SendCoordinator(fake).RunAsync(db, [item], true, _ => { }, () => { snapshot = System.Text.Json.JsonSerializer.Serialize(db); throw new IOException("simulated process interruption after save"); }, default); } catch (IOException) { }
    var recovered = System.Text.Json.JsonSerializer.Deserialize<AppDatabase>(snapshot)!; item.Status = "준비";
    Check(recovered.Logs.Single().Result == "준비" && recovered.Logs.Single().Detail == "드라이런 검사 시작: 메시지·파일 전송 없음", "provisional dry-run log is not a supported retryable Mac status");
    await new SendCoordinator(fake).RunAsync(recovered, [item], false, _ => { }, () => { }, default);
    Check(item.Status == "전송 요청 완료" && fake.Resolves == 1, "interrupted dry-run incorrectly blocked a real send");
}
static async Task NativeCancelled() {
    using var cts = new CancellationTokenSource(); cts.Cancel(); bool cancelled = false;
    try { await new WindowsKakaoClient().ResolveAsync("not-a-real-room", cts.Token); } catch (OperationCanceledException) { cancelled = true; }
    Check(cancelled, "cancelled adapter began resolution");
}
static async Task NativeInvalidTarget() {
    bool rejected = false;
    try { await new WindowsKakaoClient().SendTextAsync(new(-1, 1, "not-a-real-room", 2), "never deliver", default); } catch (InvalidOperationException) { rejected = true; }
    Check(rejected, "invalid process ownership accepted");
}
static async Task NativeMissingFile() {
    bool rejected = false;
    try { await new WindowsKakaoClient().SendFilesAsync(new(-1, 1, "not-a-real-room", 2), [System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid() + ".missing")], default); } catch (InvalidOperationException) { rejected = true; }
    Check(rejected, "missing file accepted");
}

sealed class FakeClient : IChatClient {
    public int Resolves; public List<string> Effects = []; public Exception? ResolveError; public Action? AfterText; public Action? OnResolve; public Action? AfterFiles;
    public Task<ChatTarget> ResolveAsync(string room, CancellationToken ct) { Resolves++; OnResolve?.Invoke(); if (ResolveError is not null) throw ResolveError; return Task.FromResult(new ChatTarget(42, 1, room, 2)); }
    public Task SendTextAsync(ChatTarget target, string text, CancellationToken ct) { Effects.Add("text:" + text); AfterText?.Invoke(); return Task.CompletedTask; }
    public Task SendFilesAsync(ChatTarget target, IReadOnlyList<string> paths, CancellationToken ct) { Effects.Add("files:" + string.Join("|", paths)); AfterFiles?.Invoke(); return Task.CompletedTask; }
    public Task CompleteAsync(ChatTarget target, CancellationToken ct) { Effects.Add("complete"); return Task.CompletedTask; }
}

sealed class OpeningClient(bool canCompose = true) : IChatClient, IDryRunChatClient {
    public int Opened; public int Delivered;
    public Task<RoomOpenDryRunResult> OpenRoomDryRunAsync(string room, CancellationToken ct) { Opened++; return Task.FromResult(new RoomOpenDryRunResult(room, false, new(42, 1, room, canCompose ? 2 : 0))); }
    public Task<ChatTarget> ResolveAsync(string room, CancellationToken ct) => throw new InvalidOperationException("strict resolution rejected an inaccessible result list");
    public Task SendTextAsync(ChatTarget target, string text, CancellationToken ct) { Delivered++; return Task.CompletedTask; }
    public Task SendFilesAsync(ChatTarget target, IReadOnlyList<string> paths, CancellationToken ct) { Delivered++; return Task.CompletedTask; }
    public Task CompleteAsync(ChatTarget target, CancellationToken ct) { Delivered++; return Task.CompletedTask; }
}
