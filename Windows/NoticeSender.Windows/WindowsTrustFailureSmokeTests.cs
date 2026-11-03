using System;
using System.IO;
using System.Text.Json;

namespace NoticeSender.Windows;

internal static class WindowsTrustFailureSmokeTests
{
    internal static void Run()
    {
        var blocked = new TestFileLoadException(unchecked((int)0x800711C7));
        var nested = new AggregateException(new InvalidOperationException("일반 오류"), new Exception("래퍼", blocked));
        var result = WindowsTrustFailure.Classify(nested);
        Require(result?.Kind == WindowsTrustFailureKind.ExecutionTrustPolicyBlocked && result.HResult == unchecked((int)0x800711C7), "중첩 실행 신뢰 차단 분류 실패");
        Require(result!.Message.Contains("0x800711C7", StringComparison.Ordinal) && result.Message.Contains(WindowsTrustFailure.OfficialFaqUrl, StringComparison.Ordinal), "차단 오류 코드 / 공식 안내 누락");
        foreach (var ordinary in new Exception[] { new InvalidOperationException("일반 오류"), new UnauthorizedAccessException("접근 거부"),
            new FileLoadException("Smart App Control이라는 텍스트만 있는 일반 로드 오류"), new TestFileLoadException(unchecked((int)0x80070005)) })
            Require(WindowsTrustFailure.Classify(ordinary) == null && WindowsTrustFailure.Describe(ordinary) == ordinary.Message, "일반 오류를 실행 신뢰 차단으로 오분류");

        string folder = Path.Combine(Path.GetTempPath(), "NoticeSender-trust-smoke-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(folder, "startup.json");
        try
        {
            Program.WriteBotStartupFailureReport(new[] { "--bot-send-test", "--report", path }, nested);
            using (var json = JsonDocument.Parse(File.ReadAllText(path)))
            {
                var root = json.RootElement;
                Require(root.GetProperty("completed").GetBoolean() && !root.GetProperty("success").GetBoolean() && root.GetProperty("exitCode").GetInt32() == 1, "봇 시작 실패 최종 결과 누락");
                Require(root.GetProperty("failureKind").GetString() == "ExecutionTrustPolicyBlocked" && root.GetProperty("errorCode").GetString() == "0x800711C7"
                    && root.GetProperty("failure").GetString() == result.Message && root.GetProperty("helpUrl").GetString() == WindowsTrustFailure.OfficialFaqUrl, "봇 시작 실패의 친절한 신뢰 정책 안내 누락");
            }
            string ordinaryPath = Path.Combine(folder, "ordinary.json");
            Program.WriteBotStartupFailureReport(new[] { "--bot-send-test", "--report", ordinaryPath }, new InvalidOperationException("원래 일반 오류"));
            using (var json = JsonDocument.Parse(File.ReadAllText(ordinaryPath)))
                Require(json.RootElement.GetProperty("failure").GetString() == "원래 일반 오류" && json.RootElement.GetProperty("failureKind").ValueKind == JsonValueKind.Null, "일반 봇 시작 오류 메시지 변경");

            string existingPath = Path.Combine(folder, "existing.json");
            File.WriteAllText(existingPath, "{\"mode\":\"bot-send-test\",\"trace\":[{\"operation\":\"send-text\",\"phase\":\"returned\"}],\"steps\":[{\"id\":\"A\",\"status\":\"passed\"}],\"logs\":[{\"result\":\"전송 요청 완료\"}],\"evidence\":[{\"path\":\"synthetic.png\"}]}");
            byte[] before = File.ReadAllBytes(existingPath);
            Program.WriteBotStartupFailureReport(new[] { "--bot-send-test", "--report", existingPath }, nested);
            Require(File.ReadAllBytes(existingPath).AsSpan().SequenceEqual(before), "시작 / 실행 실패가 기존 전송 기록과 증거를 덮어썼습니다.");
            var sidecars = Directory.GetFiles(folder, "existing.json.unhandled-failure-*.json");
            Require(sidecars.Length == 1, "기존 보고서를 보존한 별도 실패 보고서 누락");
            using (var json = JsonDocument.Parse(File.ReadAllText(sidecars[0])))
                Require(!json.RootElement.GetProperty("success").GetBoolean() && json.RootElement.GetProperty("exitCode").GetInt32() == 1
                    && json.RootElement.GetProperty("failure").GetString() == result.Message && json.RootElement.GetProperty("originalReportPath").GetString() == existingPath,
                    "별도 실패 보고서의 원본 연결 / 안내 누락");

            string unrelatedPath = Path.Combine(folder, "unrelated.json");
            File.WriteAllText(unrelatedPath, "unrelated existing content");
            Program.WriteBotStartupFailureReport(new[] { "--bot-send-test", "--report", unrelatedPath }, new InvalidOperationException("일반 실행 오류"));
            Require(File.ReadAllText(unrelatedPath) == "unrelated existing content" && Directory.GetFiles(folder, "unrelated.json.unhandled-failure-*.json").Length == 1,
                "관련 없는 기존 파일을 실패 보고서로 교체했습니다.");
        }
        finally
        {
            var directory = new DirectoryInfo(folder);
            if (directory.Exists && directory.Parent?.FullName == new DirectoryInfo(Path.GetTempPath()).FullName && directory.Name.StartsWith("NoticeSender-trust-smoke-", StringComparison.Ordinal))
                directory.Delete(recursive: true);
        }
    }
    private sealed class TestFileLoadException : FileLoadException
    {
        internal TestFileLoadException(int errorCode) : base("합성 구성요소 로드 오류") { HResult = errorCode; }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
