using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NoticeSender.Automation;

namespace NoticeSender.Windows;

internal static class RoomDryRunSmokeTests
{
    internal static async Task RunAsync()
    {
        foreach (string mode in new[] { "--demo", "--smoke-test", "--diagnose-kakao" })
        {
            string[] conflicting = { mode, "--dry-run-rooms", "this-file-must-never-be-read.json" };
            Require(!Program.ModesAreCompatible(conflicting), "실제 방 열기와 격리 / 진단 모드의 동시 실행 허용");
            Require(Program.Main(conflicting) == 1, "충돌 모드에서 앱을 시작했습니다.");
        }
        Require(Program.ModesAreCompatible(new[] { "--dry-run-rooms", "rooms.json", "--exit-after-dry-run" }), "정상 방 열기 실행 차단");
        Require(Program.ModesAreCompatible(new[] { "--smoke-test", "--demo" }), "기존 격리 테스트 실행 차단");
        string folder = Path.Combine(Path.GetTempPath(), "NoticeSender-room-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            // A reorder, skipped room, or unverified success must change the report and fail this test.
            var client = new FakeDryRunClient();
            var snapshots = new List<string>();
            string path = Path.Combine(folder, "ordered.json");
            var result = await new RoomDryRunRunner(client).RunAsync(new[] { "가상 방 B", "가상 방 A" }, path,
                report => snapshots.Add(File.ReadAllText(path)), CancellationToken.None);
            Require(client.Rooms.SequenceEqual(new[] { "가상 방 B", "가상 방 A" }), "입력 순서대로 방을 열지 않았습니다.");
            Require(result.Completed && result.Success && result.SuccessCount == 2 && result.ExitCode == 0, "성공 집계 실패");
            Require(result.Rooms[0].Status == "already-open" && result.Rooms[1].Status == "newly-opened", "기존 / 새 창 상태 구분 실패");
            Require(result.Rooms[0].ProcessId == 42 && result.Rooms[0].WindowHandle == 100 && result.Rooms[0].ComposerHandle == 200, "검증된 대상 식별자 누락");
            using (var json = JsonDocument.Parse(snapshots.Last()))
                Require(json.RootElement.GetProperty("mode").GetString() == "room-open-only" && json.RootElement.GetProperty("completed").GetBoolean(), "최종 JSON 보고서 누락");
            Require(snapshots.Any(s => { using var d = JsonDocument.Parse(s); return d.RootElement.GetProperty("successCount").GetInt32() == 1; }), "방별 중간 보고서 누락");

            client = new FakeDryRunClient { ReadOnlyRoom = true };
            result = await new RoomDryRunRunner(client).RunAsync(new[] { "가상 읽기 전용 방" }, Path.Combine(folder, "read-only.json"), null, CancellationToken.None);
            Require(result.Success && result.SuccessCount == 1 && result.Rooms[0].CanCompose == false && result.Rooms[0].ComposerHandle == 0
                && result.Rooms[0].DisplayStatus.Contains("발송 입력란 없음", StringComparison.Ordinal), "입력란 없는 방의 열기 완료 / 기능 구분 실패");
            client = new FakeDryRunClient { InvalidWindow = true };
            result = await new RoomDryRunRunner(client).RunAsync(new[] { "가상 잘못된 창" }, Path.Combine(folder, "invalid-target.json"), null, CancellationToken.None);
            Require(!result.Success && result.FailedCount == 1, "검증되지 않은 루트를 방 열기 성공으로 집계");

            // Continuing after the first failure would reach a third room and fail these assertions.
            client = new FakeDryRunClient { FailRoom = "없는 가상 방" };
            result = await new RoomDryRunRunner(client).RunAsync(new[] { "가상 방 B", "없는 가상 방", "접근하면 안 되는 방" }, Path.Combine(folder, "failure.json"), null, CancellationToken.None);
            Require(client.Rooms.Count == 2 && result.SuccessCount == 1 && result.FailedCount == 1 && result.ExitCode == 1 && result.Completed, "첫 실패 후 중단 실패");
            Require(result.Rooms[2].Status == "pending", "실패 뒤 미시도 방 상태 손실");

            using var cancel = new CancellationTokenSource();
            client = new FakeDryRunClient { AfterOpen = () => cancel.Cancel() };
            result = await new RoomDryRunRunner(client).RunAsync(new[] { "가상 방 B", "미시도 방" }, Path.Combine(folder, "cancel.json"), null, cancel.Token);
            Require(client.Rooms.Count == 1 && result.Cancelled && result.ExitCode == 2 && result.Completed && result.SuccessCount == 1, "취소 뒤 다음 방 접근 / 결과 집계 실패");

            foreach (var invalid in new[] { Array.Empty<string>(), new[] { " " }, new[] { "중복", "중복" }, Enumerable.Repeat("방", 51).ToArray() })
            {
                client = new FakeDryRunClient();
                bool rejected = false;
                try { await new RoomDryRunRunner(client).RunAsync(invalid, Path.Combine(folder, "invalid.json"), null, CancellationToken.None); }
                catch (ArgumentException) { rejected = true; }
                Require(rejected && client.Rooms.Count == 0, "잘못된 방 목록에서 카카오톡 접근");
            }

            Require(typeof(IDryRunChatClient).GetMethods().Length == 1 && typeof(IDryRunChatClient).GetMethods()[0].Name == "OpenRoomDryRunAsync", "드라이런 인터페이스에 전송 기능이 있습니다.");
            client = new FakeDryRunClient();
            var window = new RoomDryRunWindow(client, new[] { " 가상 방 B ", "가상\n방 A" }, Path.Combine(folder, "window.json"));
            Require(window.Title.Contains("방 열기"), "독립 방 열기 창 초기화 실패");
            await window.StartAsync();
            Require(client.Rooms.SequenceEqual(new[] { " 가상 방 B ", "가상\n방 A" }), "JSON에서 받은 정확한 방 이름을 창에서 변경했습니다.");
            window.Close();
        }
        finally
        {
            var directory = new DirectoryInfo(folder);
            if (directory.Parent?.FullName == new DirectoryInfo(Path.GetTempPath()).FullName && directory.Name.StartsWith("NoticeSender-room-smoke-", StringComparison.Ordinal))
                directory.Delete(recursive: true);
        }
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class FakeDryRunClient : IDryRunChatClient
    {
        internal List<string> Rooms { get; } = new();
        internal string? FailRoom { get; init; }
        internal Action? AfterOpen { get; init; }
        internal bool ReadOnlyRoom { get; init; }
        internal bool InvalidWindow { get; init; }
        public Task<RoomOpenDryRunResult> OpenRoomDryRunAsync(string exactRoomName, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Rooms.Add(exactRoomName);
            if (exactRoomName == FailRoom) throw new InvalidOperationException("가상 방을 찾을 수 없습니다.");
            var result = new RoomOpenDryRunResult(exactRoomName, Rooms.Count == 1, new ChatTarget(42, InvalidWindow ? 0 : 100, exactRoomName, ReadOnlyRoom ? 0 : 200));
            AfterOpen?.Invoke();
            return Task.FromResult(result);
        }
    }
}
