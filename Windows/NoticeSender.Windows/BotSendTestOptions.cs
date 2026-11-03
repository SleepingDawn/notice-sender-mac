using System;
using System.IO;
using System.Linq;

namespace NoticeSender.Windows;

internal sealed record BotSendTestOptions(string Room, string ReportPath, bool ExitAfterTest, bool CaptureEvidence = false, bool ReplaceReviewedBotCue = false, string? OnlyStep = null, bool ConfirmNativeBotFiles = false)
{
    internal static void ValidateStep(string? step)
    {
        if (step != null && !new[] { "A", "B", "C", "D", "E-PDF", "E-PNG", "F" }.Contains(step, StringComparer.Ordinal))
            throw new ArgumentException("알 수 없는 합성 테스트 단계입니다.");
    }
    internal static void ValidateDestination(string room, bool acknowledged)
    {
        if (!acknowledged || !new[] { "카카오톡 선물하기", "지모", "KREAM" }.Contains(room, StringComparer.Ordinal))
            throw new ArgumentException("실제 테스트는 명시 확인한 카카오톡 선물하기 / 지모 / KREAM 봇 방에서만 실행할 수 있습니다.");
    }

    internal static BotSendTestOptions Parse(string[] args)
    {
        if (!Program.ModesAreCompatible(args)) throw new ArgumentException("봇 전송 테스트는 다른 실행 모드와 함께 사용할 수 없습니다.");
        string? room = null, report = null, onlyStep = null;
        bool mode = false, acknowledged = false, exit = false, captureEvidence = false, replaceReviewedBotCue = false, confirmNativeBotFiles = false;
        for (int index = 0; index < args.Length; index++)
        {
            string Value()
            {
                if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
                    throw new ArgumentException(args[index] + " 뒤에 값을 지정하세요.");
                return args[++index];
            }
            switch (args[index])
            {
                case "--bot-send-test" when !mode: mode = true; break;
                case "--acknowledge-bot-only" when !acknowledged: acknowledged = true; break;
                case "--exit-after-test" when !exit: exit = true; break;
                case "--capture-bot-evidence" when !captureEvidence: captureEvidence = true; break;
                case "--replace-reviewed-bot-cue" when !replaceReviewedBotCue: replaceReviewedBotCue = true; break;
                case "--confirm-native-bot-files" when !confirmNativeBotFiles: confirmNativeBotFiles = true; break;
                case "--room" when room == null: room = Value(); break;
                case "--report" when report == null: report = Value(); break;
                case "--step" when onlyStep == null: onlyStep = Value(); break;
                default: throw new ArgumentException("알 수 없거나 중복된 봇 테스트 인수입니다.");
            }
        }
        if (!mode || room == null || string.IsNullOrWhiteSpace(report)) throw new ArgumentException("--bot-send-test, --room, --acknowledge-bot-only, --report가 필요합니다.");
        ValidateDestination(room, acknowledged);
        ValidateStep(onlyStep);
        if (replaceReviewedBotCue && !captureEvidence) throw new ArgumentException("안내 문구 교체 시험에는 봇 창 증거 캡처가 필요합니다.");
        ValidateNativeFileTest(confirmNativeBotFiles, captureEvidence, onlyStep);
        return new(room, Path.GetFullPath(report), exit, captureEvidence, replaceReviewedBotCue, onlyStep, confirmNativeBotFiles);
    }

    internal static void ValidateNativeFileTest(bool enabled, bool capture, string? step)
    {
        if (enabled && (!capture || step is not ("B" or "C" or "D" or "E-PDF" or "E-PNG")))
            throw new ArgumentException("네이티브 파일 확인 시험에는 증거 캡처와 하나의 첨부 시험 단계가 필요합니다.");
    }
}
