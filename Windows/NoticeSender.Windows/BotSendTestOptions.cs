using System;
using System.IO;
using System.Linq;

namespace NoticeSender.Windows;

internal sealed record BotSendTestOptions(string Room, string ReportPath, bool ExitAfterTest, bool CaptureEvidence = false)
{
    internal static void ValidateDestination(string room, bool acknowledged)
    {
        if (!acknowledged || !new[] { "카카오톡 선물하기", "지모", "KREAM" }.Contains(room, StringComparer.Ordinal))
            throw new ArgumentException("실제 테스트는 명시 확인한 카카오톡 선물하기 / 지모 / KREAM 봇 방에서만 실행할 수 있습니다.");
    }

    internal static BotSendTestOptions Parse(string[] args)
    {
        if (!Program.ModesAreCompatible(args)) throw new ArgumentException("봇 전송 테스트는 다른 실행 모드와 함께 사용할 수 없습니다.");
        string? room = null, report = null;
        bool mode = false, acknowledged = false, exit = false, captureEvidence = false;
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
                case "--room" when room == null: room = Value(); break;
                case "--report" when report == null: report = Value(); break;
                default: throw new ArgumentException("알 수 없거나 중복된 봇 테스트 인수입니다.");
            }
        }
        if (!mode || room == null || string.IsNullOrWhiteSpace(report)) throw new ArgumentException("--bot-send-test, --room, --acknowledge-bot-only, --report가 필요합니다.");
        ValidateDestination(room, acknowledged);
        return new(room, Path.GetFullPath(report), exit, captureEvidence);
    }
}
