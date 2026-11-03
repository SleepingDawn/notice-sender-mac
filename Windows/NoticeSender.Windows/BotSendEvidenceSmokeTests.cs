using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using NoticeSender.Automation;

namespace NoticeSender.Windows;

internal static class BotSendEvidenceSmokeTests
{
    internal static void RunGuards()
    {
        CheckRequiredFileEvidence();
        using (var release = new ManualResetEventSlim())
        using (var completed = new ManualResetEventSlim())
        {
            bool timedOut = false;
            try { BotWindowEvidence.WithDeadline(() => { try { release.Wait(TimeSpan.FromSeconds(2)); return 1; } finally { completed.Set(); } }, TimeSpan.FromMilliseconds(20)); }
            catch (TimeoutException) { timedOut = true; }
            finally { release.Set(); Require(completed.Wait(TimeSpan.FromSeconds(2)), "합성 캡처 작업이 종료되지 않았습니다."); }
            Require(timedOut, "응답 없는 증거 캡처가 제한 시간 이후에도 호출자를 붙잡았습니다.");
        }
        Require(BotWindowEvidence.WithDeadline(() => 7, TimeSpan.FromSeconds(1)) == 7, "정상 증거 캡처 결과가 변경되었습니다.");
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

    private static void CheckRequiredFileEvidence()
    {
        string image = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(image, new byte[] { 1 });
            var root = new BotEvidenceImage("bot-root", 101, 200, 100, image, "synthetic", null);
            var modal = new BotEvidenceImage("owned-window", 102, 200, 100, image, "synthetic", null);
            var valid = new BotEvidenceCaptureResult(DateTimeOffset.Now, "synthetic", 101, new[] { root, modal });
            BotFileConfirmationEvidence.RequireImages(valid, 101, 102);
            foreach (var invalid in new[] {
                valid with { Error = "capture failed" }, valid with { RootWindowHandle = 999 },
                valid with { Images = new[] { root } }, valid with { Images = new[] { root, modal, modal } },
                valid with { Images = new[] { root, modal with { WindowHandle = 999 } } },
                valid with { Images = new[] { root, modal with { Error = "blank" } } },
                valid with { Images = new[] { root, modal with { Path = image + ".missing" } } },
                valid with { Images = new[] { root, modal with { Width = 0 } } }
            })
            {
                bool rejected = false;
                try { BotFileConfirmationEvidence.RequireImages(invalid, 101, 102); }
                catch (InvalidOperationException) { rejected = true; }
                Require(rejected, "유효하지 않은 첨부 확인 캡처를 허용했습니다.");
            }
        }
        finally { File.Delete(image); }
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
