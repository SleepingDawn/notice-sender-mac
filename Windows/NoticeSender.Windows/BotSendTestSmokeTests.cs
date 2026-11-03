using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using NoticeSender.Automation;

namespace NoticeSender.Windows;

internal static class BotSendTestSmokeTests
{
    internal static async Task RunAsync()
    {
        BotSendEvidenceSmokeTests.RunGuards();
        foreach (string mode in new[] { "--demo", "--smoke-test", "--diagnose-kakao", "--dry-run-rooms" })
        {
            string[] conflicting = { mode, "--bot-send-test", "--room", "KREAM", "--acknowledge-bot-only", "--report", "must-not-write.json" };
            Require(!Program.ModesAreCompatible(conflicting), "봇 실전 테스트의 혼합 실행 허용");
            Require(Program.Main(conflicting) == 1, "혼합 실행에서 앱 시작");
        }
        foreach (string room in new[] { "카카오톡 선물하기", "지모", "KREAM" })
            Require(BotSendTestOptions.Parse(new[] { "--bot-send-test", "--room", room, "--acknowledge-bot-only", "--report", "report.json" }).Room == room, "허용 봇 이름 변경");
        Require(BotSendTestOptions.Parse(new[] { "--bot-send-test", "--room", "KREAM", "--acknowledge-bot-only", "--report", "report.json", "--capture-bot-evidence" }).CaptureEvidence, "선택적 봇 증거 캡처 인수 무시");
        Require(!BotSendTestOptions.Parse(new[] { "--bot-send-test", "--room", "KREAM", "--acknowledge-bot-only", "--report", "report.json" }).ReplaceReviewedBotCue, "일반 봇 확인만으로 기존 문구 교체 허용");
        Require(BotSendTestOptions.Parse(new[] { "--bot-send-test", "--room", "KREAM", "--acknowledge-bot-only", "--report", "report.json", "--capture-bot-evidence", "--replace-reviewed-bot-cue" }).ReplaceReviewedBotCue, "명시 안내 문구 교체 허용 무시");
        Reject(new[] { "--bot-send-test", "--room", "KREAM", "--acknowledge-bot-only", "--report", "report.json", "--replace-reviewed-bot-cue" });
        Require(BotSendTestOptions.Parse(new[] { "--bot-send-test", "--room", "카카오톡 선물하기", "--acknowledge-bot-only", "--report", "report.json", "--capture-bot-evidence", "--confirm-native-bot-files", "--step", "D" }).Room == "카카오톡 선물하기", "명시 합성 파일 확인 시험 인수 무시");
        Require(BotSendTestOptions.Parse(new[] { "--bot-send-test", "--room", "카카오톡 선물하기", "--acknowledge-bot-only", "--report", "report.json", "--capture-bot-evidence", "--confirm-native-bot-files", "--step", "D" }).ConfirmNativeBotFiles, "네이티브 파일 확인 허용 옵션 누락");
        Require(!BotSendTestOptions.Parse(new[] { "--bot-send-test", "--room", "카카오톡 선물하기", "--acknowledge-bot-only", "--report", "report.json", "--capture-bot-evidence", "--step", "D" }).ConfirmNativeBotFiles, "명시 옵션 없이 네이티브 파일 확인 허용");
        Reject(new[] { "--bot-send-test", "--room", "KREAM", "--acknowledge-bot-only", "--report", "report.json", "--confirm-native-bot-files", "--step", "D" });
        Reject(new[] { "--bot-send-test", "--room", "KREAM", "--acknowledge-bot-only", "--report", "report.json", "--capture-bot-evidence", "--confirm-native-bot-files" });
        Reject(new[] { "--bot-send-test", "--room", "KREAM", "--acknowledge-bot-only", "--report", "report.json", "--capture-bot-evidence", "--confirm-native-bot-files", "--step", "A" });
        Require(BotSendTestOptions.Parse(new[] { "--bot-send-test", "--room", "KREAM", "--acknowledge-bot-only", "--report", "report.json", "--step", "B" }).OnlyStep == "B", "선택 테스트 단계 무시");
        Reject(new[] { "--bot-send-test", "--room", "KREAM", "--acknowledge-bot-only", "--report", "report.json", "--step", "G" });
        foreach (string room in new[] { "", "KREAM ", "kream", "개인 학생 방" })
            Reject(new[] { "--bot-send-test", "--room", room, "--acknowledge-bot-only", "--report", "report.json" });
        Reject(new[] { "--bot-send-test", "--room", "KREAM", "--report", "report.json" });
        Reject(new[] { "--bot-send-test", "--room", "KREAM", "--acknowledge-bot-only" });
        Reject(new[] { "--bot-send-test", "--room", "KREAM", "--room", "지모", "--acknowledge-bot-only", "--report", "report.json" });

        string folder = Path.Combine(Path.GetTempPath(), "NoticeSender-bot-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var artifactFolders = new List<string>();
        try
        {
            // Reordered messages/files, a missing step, or an accidental F retry changes these observable calls.
            var fake = new FakeBotClient();
            string path = Path.Combine(folder, "success.json");
            var snapshots = new List<string>();
            var result = await new BotSendTestRunner(fake, fake).RunAsync("KREAM", true, path,
                _ => snapshots.Add(File.ReadAllText(path)), CancellationToken.None);
            artifactFolders.Add(result.ArtifactDirectory!);
            Require(result.Completed && result.Success && result.ExitCode == 0 && result.SuccessCount == 7, "봇 테스트 전체 성공 집계 실패");
            Require(result.Steps.Select(s => s.Id).SequenceEqual(new[] { "A", "B", "C", "D", "E-PDF", "E-PNG", "F" }) && result.Steps.All(s => s.Status == "passed"), "봇 단계 순서 / 완료 누락");
            string[] expected = { "open", "resolve", "resolve", "text", "text", "text", "text", "text", "text", "complete",
                "resolve", "text", "files", "complete", "resolve", "text", "files", "complete", "resolve", "files", "complete",
                "resolve", "text", "files", "complete", "resolve", "text", "files", "complete", "resolve", "text" };
            Require(fake.Calls.Select(c => c.Operation).SequenceEqual(expected), "봇 테스트 전송 / 완료 / 재시도 순서 변경");
            Require(fake.Calls.Where(c => c.Operation == "text").Select(c => c.Text!).Take(6).SequenceEqual(result.Steps[0].Messages), "개별 + 공통 5개 순서 변경");
            Require(result.Steps[0].Messages.Count == 6 && result.Steps[3].Messages.Count == 0, "6개 메시지 / 첨부 전용 구성 실패");
            Require(result.Steps.SelectMany(s => s.Messages).All(m => m.Contains(result.TestStamp, StringComparison.Ordinal)), "실행 식별자가 없는 합성 문구가 있습니다.");
            var files = fake.Calls.Where(c => c.Operation == "files").Select(c => c.Files!).ToArray();
            Require(files[0].Count == 1 && Path.GetExtension(files[0][0]) == ".txt" && files[1].Select(Path.GetExtension).SequenceEqual(new[] { ".txt", ".csv" })
                && files[2].Count == 1 && files[3].Select(Path.GetExtension).SequenceEqual(new[] { ".pdf" }) && files[4].Select(Path.GetExtension).SequenceEqual(new[] { ".png" }), "합성 파일 종류 / 순서 변경");
            Require(files.SelectMany(p => p).Distinct(StringComparer.Ordinal).Count() == 6, "단계 사이에 파일 이름을 재사용하여 이전 완료 증거와 혼동할 수 있습니다.");
            Require(result.Steps[6].ExpectedCancellation && result.Steps[6].BatchStatus == "확인 필요" && result.Steps[6].RetryTransportCalls == 0 && result.Steps[6].RetryTraceEntries == 0 && result.Logs.Last().Result == "확인 필요",
                "제어 중단 뒤 보호된 재시도 검증 실패");
            Require(result.Trace.Count(t => t.Step == "F" && t.Operation == "send-text" && t.Phase == "returned") == 1, "F 단계에서 두 번째 메시지 전송");
            Require(result.SyntheticFiles.All(p => File.Exists(p) && Path.GetFileName(p).Contains(result.TestStamp, StringComparison.Ordinal)), "합성 파일 이름 고유성 / 생성 실패");
            using (var stream = File.OpenRead(result.SyntheticFiles.Single(p => p.EndsWith(".png", StringComparison.Ordinal))))
            {
                var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
                Require(frame.PixelWidth >= 128 && frame.PixelHeight >= 96, "합성 PNG가 이미지 전송 테스트에 비해 너무 작습니다.");
            }
            string pdf = Encoding.ASCII.GetString(File.ReadAllBytes(result.SyntheticFiles.Single(p => p.EndsWith(".pdf", StringComparison.Ordinal))));
            Require(pdf.StartsWith("%PDF-1.4", StringComparison.Ordinal) && pdf.EndsWith("%%EOF\n", StringComparison.Ordinal), "합성 PDF가 유효하지 않습니다.");
            using (var json = JsonDocument.Parse(File.ReadAllText(path)))
                Require(json.RootElement.GetProperty("mode").GetString() == "bot-send-test" && json.RootElement.GetProperty("completed").GetBoolean(), "최종 봇 JSON 누락");
            Require(snapshots.Count > 6 && snapshots.Any(s => { using var d = JsonDocument.Parse(s); return d.RootElement.GetProperty("successCount").GetInt32() == 1; }), "단계별 봇 JSON 누락");
            Require(result.FullSuitePassed, "전체 테스트와 선택 테스트 구분 누락");

            fake = new FakeBotClient();
            result = await new BotSendTestRunner(fake, fake).RunAsync("KREAM", true, Path.Combine(folder, "only-B.json"), null, CancellationToken.None, onlyStep: "B");
            artifactFolders.Add(result.ArtifactDirectory!);
            Require(result.Success && !result.FullSuitePassed && result.OnlyStep == "B" && result.RequestedCount == 1 && result.Steps.Single().Id == "B"
                && fake.Calls.Select(c => c.Operation).SequenceEqual(new[] { "open", "resolve", "resolve", "text", "files", "complete" }), "선택 단계 이외 메시지 전송 / 전체 통과 오판");

            fake = new FakeBotClient { FailFirstFiles = true };
            result = await new BotSendTestRunner(fake, fake).RunAsync("지모", true, Path.Combine(folder, "failure.json"), null, CancellationToken.None);
            artifactFolders.Add(result.ArtifactDirectory!);
            Require(result.Completed && !result.Success && result.ExitCode == 1 && result.SuccessCount == 1 && result.Steps[1].Status == "failed"
                && result.Steps.Skip(2).All(s => s.Status == "pending"), "예상 밖 실패 후 후속 테스트 실행");
            Require(fake.Calls.Count(c => c.Operation == "files") == 1 && fake.Calls.Count(c => c.Operation == "text") == 7, "실패 뒤 추가 전송");

            using var cancel = new CancellationTokenSource();
            fake = new FakeBotClient { AfterText = () => cancel.Cancel() };
            result = await new BotSendTestRunner(fake, fake).RunAsync("KREAM", true, Path.Combine(folder, "cancel.json"), null, cancel.Token);
            artifactFolders.Add(result.ArtifactDirectory!);
            Require(result.Completed && result.Cancelled && result.ExitCode == 2 && !result.Success && fake.Calls.Count(c => c.Operation == "text") == 1, "사용자 취소를 통과로 집계 / 추가 전송");

            fake = new FakeBotClient();
            bool rejected = false;
            try { await new BotSendTestRunner(fake, fake).RunAsync("개인 학생 방", true, Path.Combine(folder, "invalid.json"), null, CancellationToken.None); }
            catch (ArgumentException) { rejected = true; }
            Require(rejected && fake.Calls.Count == 0, "허용 목록 밖 방에서 전송 경로 실행");

            fake = new FakeBotClient { ReadOnlyRoom = true };
            result = await new BotSendTestRunner(fake, fake).RunAsync("KREAM", true, Path.Combine(folder, "read-only.json"), null, CancellationToken.None);
            artifactFolders.Add(result.ArtifactDirectory!);
            Require(result.Completed && !result.Success && result.SuccessCount == 0 && fake.Calls.Select(c => c.Operation).SequenceEqual(new[] { "open" }), "입력란 없는 방에서 실제 전송 진입");

            // Optional diagnostic failures must preserve both successful sends and the original file-send failure.
            fake = new FakeBotClient { FailFirstFiles = true };
            result = await new BotSendTestRunner(fake, fake, new ThrowingEvidenceCapture()).RunAsync("KREAM", true, Path.Combine(folder, "evidence-errors.json"), null, CancellationToken.None);
            artifactFolders.Add(result.ArtifactDirectory!);
            Require(result.Steps[0].Status == "passed" && result.Steps[1].Status == "failed" && result.Failure == "합성 파일 테스트 실패", "증거 캡처 오류가 원래 전송 결과를 변경했습니다.");
            Require(result.Evidence.Count == 9 && result.Evidence.All(e => e.Error == "합성 증거 캡처 실패"), "사전 확인 / 완료된 문구 / 최초 실패의 증거 진단 누락");

            // Room text alone must never authorize rebinding to a different PID/root/composer tuple.
            foreach (string changedPart in new[] { "process", "window", "composer" })
            {
                fake = new FakeBotClient { ChangeAtResolve = 1, ChangedPart = changedPart };
                result = await new BotSendTestRunner(fake, fake).RunAsync("KREAM", true, Path.Combine(folder, "initial-identity-" + changedPart + ".json"), null, CancellationToken.None);
                artifactFolders.Add(result.ArtifactDirectory!);
                Require(result.Completed && !result.Success && result.SuccessCount == 0 && fake.Calls.Select(c => c.Operation).SequenceEqual(new[] { "open", "resolve" }),
                    "방 열기와 첫 엄격 확인의 식별자 불일치에서 실제 전송 진입");

                fake = new FakeBotClient { ChangeAtResolve = 3, ChangedPart = changedPart };
                result = await new BotSendTestRunner(fake, fake).RunAsync("KREAM", true, Path.Combine(folder, "later-identity-" + changedPart + ".json"), null, CancellationToken.None);
                artifactFolders.Add(result.ArtifactDirectory!);
                Require(result.Completed && !result.Success && result.SuccessCount == 1 && result.Steps[1].Status == "failed"
                    && fake.Calls.Count(c => c.Operation == "text") == 6 && fake.Calls.Count(c => c.Operation == "complete") == 1 && fake.Calls.All(c => c.Operation != "files"),
                    "후속 확인의 식별자 변경 후 전송 / 자동 재연결 발생");
            }
        }
        finally
        {
            foreach (string path in artifactFolders) DeletePrivateTemporary(path, "NoticeSender-BotSendTest-");
            DeletePrivateTemporary(folder, "NoticeSender-bot-smoke-");
        }
    }

    private static void Reject(string[] args)
    {
        bool rejected = false;
        try { BotSendTestOptions.Parse(args); } catch (ArgumentException) { rejected = true; }
        Require(rejected, "명시 확인 / 방 이름 / 인수 검증 실패");
    }
    private static void DeletePrivateTemporary(string path, string prefix)
    {
        var directory = new DirectoryInfo(path);
        if (directory.Exists && directory.Parent?.FullName == new DirectoryInfo(Path.GetTempPath()).FullName && directory.Name.StartsWith(prefix, StringComparison.Ordinal))
            directory.Delete(recursive: true);
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed record FakeCall(string Operation, string? Text = null, IReadOnlyList<string>? Files = null);
    private sealed class ThrowingEvidenceCapture : IBotEvidenceCapture
    {
        public BotEvidenceCaptureResult Capture(ChatTarget target, string stage, string artifactDirectory) => throw new InvalidOperationException("합성 증거 캡처 실패");
    }
    private sealed class FakeBotClient : IChatClient, IDryRunChatClient
    {
        internal List<FakeCall> Calls { get; } = new();
        internal bool FailFirstFiles { get; init; }
        internal Action? AfterText { get; init; }
        internal bool ReadOnlyRoom { get; init; }
        internal int ChangeAtResolve { get; init; }
        internal string? ChangedPart { get; init; }
        private int resolves;
        public Task<RoomOpenDryRunResult> OpenRoomDryRunAsync(string room, CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); Calls.Add(new("open")); return Task.FromResult(new RoomOpenDryRunResult(room, false, new(42, 100, room, ReadOnlyRoom ? 0 : 200))); }
        public Task<ChatTarget> ResolveAsync(string room, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); Calls.Add(new("resolve"));
            bool change = ++resolves == ChangeAtResolve;
            return Task.FromResult(new ChatTarget(change && ChangedPart == "process" ? 43 : 42,
                change && ChangedPart == "window" ? 101 : 100, room, change && ChangedPart == "composer" ? 201 : 200));
        }
        public Task SendTextAsync(ChatTarget target, string text, CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); Calls.Add(new("text", text)); AfterText?.Invoke(); return Task.CompletedTask; }
        public Task SendFilesAsync(ChatTarget target, IReadOnlyList<string> files, CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); Calls.Add(new("files", Files: files.ToArray())); if (FailFirstFiles) throw new InvalidOperationException("합성 파일 테스트 실패"); return Task.CompletedTask; }
        public Task CompleteAsync(ChatTarget target, CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); Calls.Add(new("complete")); return Task.CompletedTask; }
    }
}
