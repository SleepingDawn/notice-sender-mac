using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Json.Nodes;
using System.Windows.Automation;
using NoticeSender.Automation;

// Explicit test-only housekeeping. It never sends, retries, or alters the previous uncertain report.
internal static class SyntheticDraftCleanup
{
    internal static Task GuardTests()
    {
        const string text = "[NoticeSender 합성 봇 테스트 20261104-024000-123-abcdef12 / A] synthetic only";
        string source = JsonSerializer.Serialize(new { mode = "bot-send-test", room = "KREAM", completed = true, failure = "send remained uncertain",
            testStamp = "20261104-024000-123-abcdef12", preparation = new { status = "verified", processId = 123, windowHandle = 456, composerHandle = 789 },
            steps = new[] { new { messages = new[] { text } } }, trace = new[] {
                new { operation = "send-text", phase = "started", syntheticText = (string?)text }, new { operation = "send-text", phase = "failed", syntheticText = (string?)null } } });
        var valid = ReadRequest(source, "KREAM");
        if (valid.Text != text || valid.Target != new ChatTarget(123, 456, "KREAM", 789)) throw new Exception("Synthetic cleanup request lost its exact target/content.");
        foreach (Action<JsonNode> mutate in new Action<JsonNode>[] {
            n => n["room"] = "human room", n => n["completed"] = false, n => n["failure"] = null,
            n => n["testStamp"] = "untrusted", n => n["preparation"]!["status"] = "failed",
            n => n["preparation"]!["composerHandle"] = 0, n => n["steps"]![0]!["messages"]![0] = "unrelated draft",
            n => n["trace"]![1]!["phase"] = "returned" })
        {
            var node = JsonNode.Parse(source)!; mutate(node);
            bool rejected = false;
            try { ReadRequest(node.ToJsonString(), "KREAM"); } catch (InvalidOperationException) { rejected = true; }
            if (!rejected) throw new Exception("Unverified cleanup request was accepted.");
        }
        return Task.CompletedTask;
    }
    internal sealed record Request(ChatTarget Target, string Text);
    internal static Request ReadRequest(string json, string room)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (room is not ("카카오톡 선물하기" or "지모" or "KREAM") || root.GetProperty("room").GetString() != room ||
            root.GetProperty("mode").GetString() != "bot-send-test" || !root.GetProperty("completed").GetBoolean() ||
            root.GetProperty("failure").ValueKind != JsonValueKind.String)
            throw new InvalidOperationException("Only a completed failed synthetic bot report can authorize draft cleanup.");
        string stamp = root.GetProperty("testStamp").GetString() ?? "";
        if (!Regex.IsMatch(stamp, @"^\d{8}-\d{6}-\d{3}-[0-9a-f]{8}$")) throw new InvalidOperationException("Invalid synthetic stamp.");
        var trace = root.GetProperty("trace").EnumerateArray().ToArray();
        var attempt = trace.Last(t => t.GetProperty("operation").GetString() == "send-text" && t.GetProperty("phase").GetString() == "started");
        if (trace.Last().GetProperty("phase").GetString() != "failed" || trace.Last().GetProperty("operation").GetString() != "send-text")
            throw new InvalidOperationException("Last text attempt was not failed.");
        string text = attempt.GetProperty("syntheticText").GetString() ?? "";
        string prefix = "[NoticeSender 합성 봇 테스트 " + stamp + " / ";
        if (!text.StartsWith(prefix, StringComparison.Ordinal) ||
            !root.GetProperty("steps").EnumerateArray().SelectMany(s => s.GetProperty("messages").EnumerateArray()).Any(m => m.GetString() == text))
            throw new InvalidOperationException("The exact draft must be a generated message recorded in the previous suite.");
        var prep = root.GetProperty("preparation");
        if (prep.GetProperty("status").GetString() != "verified") throw new InvalidOperationException("Previous target was not verified.");
        var target = new ChatTarget(prep.GetProperty("processId").GetInt32(), (nint)prep.GetProperty("windowHandle").GetInt64(), room,
            (nint)prep.GetProperty("composerHandle").GetInt64());
        if (target.ProcessId <= 0 || target.WindowHandle == 0 || target.ComposerHandle == 0) throw new InvalidOperationException("Missing pinned target.");
        return new(target, text);
    }

    internal static async Task<int> RunAsync(string[] args)
    {
        try
        {
            string? room = null, previous = null, audit = null;
            bool acknowledged = false, mode = false;
            for (int i = 0; i < args.Length; i++)
            {
                string Value() => i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[++i] : throw new ArgumentException("Missing value.");
                switch (args[i])
                {
                    case "--live-discard-synthetic-draft" when !mode: mode = true; break;
                    case "--acknowledge-bot-only" when !acknowledged: acknowledged = true; break;
                    case "--room" when room == null: room = Value(); break;
                    case "--previous-report" when previous == null: previous = Value(); break;
                    case "--audit-report" when audit == null: audit = Value(); break;
                    default: throw new ArgumentException("Unknown or duplicate cleanup option.");
                }
            }
            if (!mode || !acknowledged || room == null || previous == null || audit == null) throw new ArgumentException("Explicit bot cleanup options are required.");
            var request = ReadRequest(await File.ReadAllTextAsync(previous), room);
            string? verifiedSyntheticDraft = null;
            using var auditFile = new FileStream(Path.GetFullPath(audit), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            async Task Audit(string status)
            {
                auditFile.Position = 0; auditFile.SetLength(0);
                await JsonSerializer.SerializeAsync(auditFile, new { mode = "discard-own-synthetic-draft", previousReport = Path.GetFullPath(previous),
                    target = new { request.Target.ProcessId, request.Target.RoomName, windowHandle = request.Target.WindowHandle.ToInt64(), composerHandle = request.Target.ComposerHandle.ToInt64() },
                    syntheticText = request.Text, verifiedSyntheticDraft, status, noTransmissionRequested = true, at = DateTimeOffset.Now });
                await auditFile.FlushAsync();
            }
            await Audit("prepared");
            var client = new WindowsKakaoClient();
            var target = await client.ResolveAsync(room, default);
            if (target != request.Target) throw new InvalidOperationException("The previous exact process/room/composer is no longer present.");
            var composer = AutomationElement.FromHandle(target.ComposerHandle);
            var rootWindow = AutomationElement.FromHandle(target.WindowHandle);
            var value = (ValuePattern)composer.GetCurrentPattern(ValuePattern.Pattern);
            if (value.Current.IsReadOnly) throw new InvalidOperationException("Composer became read-only.");
            string observed = WindowsKakaoClient.ReadComposer(composer);
            Console.WriteLine(JsonSerializer.Serialize(new { observedLength = observed.Length, expectedLength = request.Text.Length,
                exact = observed == request.Text, equalAfterTerminalNull = observed.TrimEnd('\0') == request.Text,
                equalAfterTerminalNewline = observed.TrimEnd('\r', '\n') == request.Text,
                exactLeadingEnterNewline = observed == "\r\n" + request.Text,
                approvedCue = WindowsKakaoClient.CaptureReviewedBotCue(composer) != null }));
            // The old synchronous Enter path inserted one CRLF at the initial caret instead of sending.
            // Only that exact, fully synthetic variant is accepted; arbitrary edits still fail closed.
            if (observed != request.Text && observed != "\r\n" + request.Text)
                throw new InvalidOperationException("Existing draft differs from the exact recorded synthetic content.");
            verifiedSyntheticDraft = observed;
            await Audit("verified-exact-own-synthetic-draft");
            WindowsKakaoClient.WithExpectedComposerText(() =>
            {
                if (rootWindow.Current.Name != room) throw new InvalidOperationException("Bot room title changed before cleanup.");
                WindowsKakaoClient.RequireNativeControlScope(target.WindowHandle, target.ProcessId, target.ComposerHandle, composer.Current.ProcessId);
            }, () => WindowsKakaoClient.ReadComposer(composer), verifiedSyntheticDraft, () => value.SetValue(""), default);
            if (WindowsKakaoClient.ReadComposer(composer).Length != 0 && WindowsKakaoClient.CaptureReviewedBotCue(composer) == null)
                throw new InvalidOperationException("Synthetic draft was not cleared.");
            await Audit("cleared-exact-own-synthetic-draft");
            Console.WriteLine("Cleared only the exact recorded synthetic draft; no transmission. Previous uncertain report retained.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
