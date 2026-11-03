using System;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using NoticeSender.Automation;

namespace NoticeSender.Windows;

internal static class BotSendEvidenceSmokeTests
{
    internal static void RunGuards()
    {
        string folder = Path.Combine(Path.GetTempPath(), "NoticeSender-evidence-must-not-create-" + Guid.NewGuid().ToString("N"));
        var capture = new BotWindowEvidence();
        var invalid = capture.Capture(new ChatTarget(-1, 1, "KREAM", 0), "invalid-pid", folder);
        Require(invalid.Error != null && invalid.Images.Count == 0 && !Directory.Exists(folder), "잘못된 PID에서 GDI / 증거 저장 진입");

        // These handles belong solely to a hidden synthetic WPF window, never to KakaoTalk.
        var window = new Window { Title = "합성 증거 가드 테스트", Width = 200, Height = 100, ShowInTaskbar = false };
        try
        {
            nint handle = new WindowInteropHelper(window).EnsureHandle();
            var wrongTitle = capture.Capture(new ChatTarget(Environment.ProcessId, handle, "KREAM", 0), "wrong-title", folder);
            Require(wrongTitle.Error?.Contains("제목", StringComparison.Ordinal) == true && wrongTitle.Images.Count == 0 && !Directory.Exists(folder), "제목 불일치 창 캡처 허용");
            var wrongPid = capture.Capture(new ChatTarget(Environment.ProcessId + 1, handle, "KREAM", 0), "wrong-pid", folder);
            Require(wrongPid.Error?.Contains("프로세스", StringComparison.Ordinal) == true && wrongPid.Images.Count == 0 && !Directory.Exists(folder), "다른 프로세스 창 캡처 허용");
            var wrongRoom = capture.Capture(new ChatTarget(Environment.ProcessId, handle, "개인 방", 0), "wrong-room", folder);
            Require(wrongRoom.Error != null && wrongRoom.Images.Count == 0 && !Directory.Exists(folder), "허용 봇 이외의 방 캡처 허용");
        }
        finally { window.Close(); }
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
