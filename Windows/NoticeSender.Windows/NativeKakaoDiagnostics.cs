using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using NoticeSender.Automation;

namespace NoticeSender.Windows;

internal static class NativeKakaoDiagnostics
{
    // Callers can catch a blocked optional module before this method's body is JIT-compiled.
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static Task<string> RunAsync(CancellationToken cancellationToken) => new WindowsKakaoClient().DiagnoseAsync(cancellationToken);
}
