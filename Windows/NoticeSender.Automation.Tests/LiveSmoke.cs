using System.Text.Json;
using NoticeSender.Automation;
using NoticeSender.Core;

internal static class LiveSmoke
{
    // These modes are never invoked by the default safety test run.
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Contains("--live-inspect"))
        {
            if (args.Contains("--live-bot-smoke") || args.Contains("--live-dry-run")) { Console.Error.WriteLine("Inspect must be used alone."); return 2; }
            string diagnosis; int inspectResult = 0;
            try { diagnosis = await new WindowsKakaoClient().DiagnoseAsync(default); }
            catch (Exception ex) { diagnosis = "Read-only native inspection failed: " + ex; inspectResult = 1; }
            var inspectPath = System.IO.Path.GetFullPath(System.IO.Path.Combine("outputs", "windows-live-inspect-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt"));
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(inspectPath)!);
            await System.IO.File.WriteAllTextAsync(inspectPath, diagnosis);
            Console.WriteLine(diagnosis); Console.WriteLine("Read-only report: " + inspectPath); return inspectResult;
        }
        bool dry = args.Contains("--live-dry-run"), actual = args.Contains("--live-bot-smoke");
        int roomIndex = Array.IndexOf(args, "--room");
        string room = roomIndex >= 0 && roomIndex + 1 < args.Length ? args[roomIndex + 1] : "";
        if (dry == actual || string.IsNullOrWhiteSpace(room)) { Console.Error.WriteLine("Use exactly one live mode and --room EXACT."); return 2; }
        if (actual && (!args.Contains("--acknowledge-bot-only") || !new[] { "카카오톡 선물하기", "지모", "KREAM" }.Contains(room, StringComparer.Ordinal)))
        { Console.Error.WriteLine("Actual delivery requires --acknowledge-bot-only and an exact allowlisted bot room."); return 2; }
        var stamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
        var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "notice-sender-test-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(folder);
        var first = System.IO.Path.Combine(folder, "notice-sender-test-single.txt");
        var second = System.IO.Path.Combine(folder, "notice-sender-test-multiple.csv");
        await System.IO.File.WriteAllTextAsync(first, "Synthetic NoticeSender Windows attachment test. No student data. " + stamp);
        await System.IO.File.WriteAllTextAsync(second, "test,value\nsynthetic,1\n");
        var client = new TraceClient(new WindowsKakaoClient());
        var coordinator = new SendCoordinator(client); var db = new AppDatabase();
        var batches = new List<BatchItem> {
            new() { Id = Guid.NewGuid(), StudentId = Guid.NewGuid(), StudentName = "가상 테스트", ChatRoomName = room,
                Message = $"[NoticeSender Windows 테스트 {stamp}] 개별 메시지", CommonMessages = Enumerable.Range(1,5).Select(i => $"[NoticeSender 테스트] 공통 메시지 {i}/5").ToList() },
            new() { Id = Guid.NewGuid(), StudentId = Guid.NewGuid(), StudentName = "가상 테스트", ChatRoomName = room,
                Message = $"[NoticeSender Windows 테스트 {stamp}] 단일 파일", AttachmentPaths = [first] },
            new() { Id = Guid.NewGuid(), StudentId = Guid.NewGuid(), StudentName = "가상 테스트", ChatRoomName = room,
                Message = $"[NoticeSender Windows 테스트 {stamp}] 다중 파일", AttachmentPaths = [first, second] }
        };
        var output = System.IO.Path.GetFullPath(System.IO.Path.Combine("outputs", "windows-live-" + stamp + ".json"));
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(output)!);
        string? failure = null; int persists = 0;
        void Save() {
            persists++;
            System.IO.File.WriteAllText(output, JsonSerializer.Serialize(new { mode = dry ? "dry-run" : "bot-smoke", room, startedAt = stamp,
                syntheticFiles = new[] { first, second }, persists, failure, batches, logs = db.Logs, trace = client.Events,
                note = "A send request is not server receipt. Any incomplete upload remains uncertain and is never retried." }, new JsonSerializerOptions { WriteIndented = true }));
        }
        try
        {
            foreach (var batch in batches)
            {
                await coordinator.RunAsync(db, [batch], dry, i => Console.WriteLine(i.Status), Save, default);
                if (batch.Status != (dry ? "드라이런 확인 완료" : "전송 요청 완료")) { failure = "Stopped after incomplete item: " + batch.Status; break; }
                if (!dry)
                {
                    int before = client.Events.Count;
                    await coordinator.RunAsync(db, [batch], false, _ => { }, Save, default);
                    if (client.Events.Count != before) throw new InvalidOperationException("Protected item unexpectedly retried.");
                }
            }
            if (dry && client.Events.Any(e => !e.StartsWith("resolve:", StringComparison.Ordinal) && !e.StartsWith("open-dry-run:", StringComparison.Ordinal))) throw new InvalidOperationException("Dry run entered a send operation.");
        }
        catch (Exception ex) { failure = ex.ToString(); }
        Save(); Console.WriteLine("Live report: " + output);
        return failure is null ? 0 : 1;
    }

    private sealed class TraceClient(IChatClient inner) : IChatClient, IDryRunChatClient
    {
        public List<string> Events { get; } = [];
        public async Task<RoomOpenDryRunResult> OpenRoomDryRunAsync(string room, CancellationToken ct)
        {
            Events.Add("open-dry-run:start:" + room);
            var result = inner is IDryRunChatClient opener ? await opener.OpenRoomDryRunAsync(room, ct) : new RoomOpenDryRunResult(room, true, await inner.ResolveAsync(room, ct));
            Events.Add("open-dry-run:" + (result.WasAlreadyOpen ? "existing:" : "newly-opened:") + room);
            return result;
        }
        public async Task<ChatTarget> ResolveAsync(string room, CancellationToken ct) { Events.Add("resolve:" + room); return await inner.ResolveAsync(room, ct); }
        public async Task SendTextAsync(ChatTarget target, string text, CancellationToken ct) { Events.Add("text:" + text); await inner.SendTextAsync(target, text, ct); }
        public async Task SendFilesAsync(ChatTarget target, IReadOnlyList<string> files, CancellationToken ct) { Events.Add("files:" + string.Join("|", files)); await inner.SendFilesAsync(target, files, ct); }
        public async Task CompleteAsync(ChatTarget target, CancellationToken ct) { Events.Add("complete"); await inner.CompleteAsync(target, ct); }
    }
}
