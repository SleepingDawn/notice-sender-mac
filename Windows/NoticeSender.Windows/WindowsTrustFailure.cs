using System;
using System.Collections.Generic;
using System.IO;

namespace NoticeSender.Windows;

internal enum WindowsTrustFailureKind { ExecutionTrustPolicyBlocked }
internal sealed record WindowsTrustFailureDetails(WindowsTrustFailureKind Kind, int HResult, string Message, string HelpUrl)
{
    public string ErrorCode => "0x" + HResult.ToString("X8", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>Pure BCL classification; does not inspect or change security policy, request elevation, or load automation modules.</summary>
internal static class WindowsTrustFailure
{
    internal const string OfficialFaqUrl = "https://support.microsoft.com/en-us/windows/security/threat-malware-protection/smart-app-control-frequently-asked-questions";
    internal const string PermissionNote = "Windows 버전에는 macOS 손쉬운 사용과 같은 별도 권한 승인 과정이 없습니다. 앱과 카카오톡을 같은 Windows 사용자와 권한 수준으로 실행하세요. Windows의 앱 실행 신뢰 차단은 별개입니다.";
    internal const string StartupLimitNote = "앱 실행 파일 자체가 차단되면 앱 내부의 안내도 표시할 수 없습니다.";

    internal static WindowsTrustFailureDetails? Classify(Exception exception)
    {
        var pending = new Stack<Exception>();
        var visited = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        pending.Push(exception);
        while (pending.TryPop(out var error))
        {
            if (!visited.Add(error)) continue;
            if (error is FileLoadException && error.HResult == unchecked((int)0x800711C7))
            {
                string message = "Windows의 앱 실행 신뢰 정책이 구성요소를 차단했습니다 (오류 0x800711C7). Smart App Control 등이 이 차단의 원인일 수 있습니다.\n\n"
                    + "이는 카카오톡 접근 권한 승인이 아닙니다. Smart App Control에는 앱별 허용 예외가 없으며, 관리자 실행으로 해제되지 않습니다.\n\n"
                    + "Microsoft 공식 안내: " + OfficialFaqUrl + "\n\n" + StartupLimitNote;
                return new(WindowsTrustFailureKind.ExecutionTrustPolicyBlocked, error.HResult, message, OfficialFaqUrl);
            }
            if (error.InnerException != null) pending.Push(error.InnerException);
            if (error is AggregateException aggregate)
                foreach (var inner in aggregate.InnerExceptions) pending.Push(inner);
        }
        return null;
    }
    internal static string Describe(Exception exception) => Classify(exception)?.Message ?? exception.Message;
}
