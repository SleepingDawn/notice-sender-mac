using System;
using System.IO;
using System.Linq;

namespace NoticeSender.Windows;

internal sealed record BotSendTestOptions(string Room, string ReportPath, bool ExitAfterTest, bool CaptureEvidence = false, bool ReplaceReviewedBotCue = false, string? OnlyStep = null, bool ConfirmNativeBotFiles = false, bool SelfChat = false)
{
    internal static void ValidateStep(string? step)
    {
        if (step != null && !new[] { "A", "B", "C", "D", "E-PDF", "E-PNG", "F", "E-KOREAN" }.Contains(step, StringComparer.Ordinal))
            throw new ArgumentException("알 수 없는 합성 테스트 단계입니다.");
    }
    internal static void ValidateDestination(string room, bool acknowledged)
    {
        if (!acknowledged || !new[] { "카카오톡 선물하기", "지모", "KREAM" }.Contains(room, StringComparer.Ordinal))
            throw new ArgumentException("실제 테스트는 명시 확인한 카카오톡 선물하기 / 지모 / KREAM 봇 방에서만 실행할 수 있습니다.");
    }
    internal static void ValidateDestination(string room, bool acknowledged, bool acknowledgeSelfChat)
    {
        if (!acknowledgeSelfChat) { ValidateDestination(room, acknowledged); return; }
        if (acknowledged || room != "임세용") throw new ArgumentException("나에게 보내기 시험은 별도 명시 확인한 정확한 ‘임세용’ 채팅방에서만 가능합니다.");
    }

    internal static BotSendTestOptions Parse(string[] args)
    {
        if (!Program.ModesAreCompatible(args)) throw new ArgumentException("봇 전송 테스트는 다른 실행 모드와 함께 사용할 수 없습니다.");
        string? room = null, report = null, onlyStep = null;
        bool mode = false, selfMode = false, acknowledged = false, acknowledgeSelfChat = false, exit = false, captureEvidence = false, replaceReviewedBotCue = false, confirmNativeBotFiles = false;
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
                case "--bot-send-test" when !mode && !selfMode: mode = true; break;
                case "--self-chat-send-test" when !mode && !selfMode: selfMode = true; break;
                case "--acknowledge-bot-only" when !acknowledged: acknowledged = true; break;
                case "--acknowledge-self-chat" when !acknowledgeSelfChat: acknowledgeSelfChat = true; break;
                case "--exit-after-test" when !exit: exit = true; break;
                case "--capture-bot-evidence" when !captureEvidence: captureEvidence = true; break;
                case "--capture-test-evidence" when !captureEvidence: captureEvidence = true; break;
                case "--replace-reviewed-bot-cue" when !replaceReviewedBotCue: replaceReviewedBotCue = true; break;
                case "--confirm-native-bot-files" when !confirmNativeBotFiles: confirmNativeBotFiles = true; break;
                case "--confirm-native-test-files" when !confirmNativeBotFiles: confirmNativeBotFiles = true; break;
                case "--room" when room == null: room = Value(); break;
                case "--report" when report == null: report = Value(); break;
                case "--step" when onlyStep == null: onlyStep = Value(); break;
                default: throw new ArgumentException("알 수 없거나 중복된 봇 테스트 인수입니다.");
            }
        }
        if ((!mode && !selfMode) || room == null || string.IsNullOrWhiteSpace(report)) throw new ArgumentException("시험 실행 모드, --room, 대상 확인 옵션과 --report가 필요합니다.");
        if (selfMode != acknowledgeSelfChat || (selfMode && replaceReviewedBotCue)) throw new ArgumentException("나에게 보내기 시험에는 별도 대상 확인이 필요하며 봇 안내 문구 교체를 사용할 수 없습니다.");
        ValidateDestination(room, acknowledged, acknowledgeSelfChat);
        ValidateStep(onlyStep);
        if (replaceReviewedBotCue && !captureEvidence) throw new ArgumentException("안내 문구 교체 시험에는 봇 창 증거 캡처가 필요합니다.");
        ValidateNativeFileTest(confirmNativeBotFiles, captureEvidence, onlyStep);
        return new(room, Path.GetFullPath(report), exit, captureEvidence, replaceReviewedBotCue, onlyStep, confirmNativeBotFiles, selfMode);
    }

    internal static void ValidateNativeFileTest(bool enabled, bool capture, string? step)
    {
        if (enabled && (!capture || step is not (null or "B" or "C" or "D" or "E-PDF" or "E-PNG" or "E-KOREAN")))
            throw new ArgumentException("네이티브 파일 확인 시험에는 증거 캡처와 전체 또는 첨부 시험 단계가 필요합니다.");
    }
}
