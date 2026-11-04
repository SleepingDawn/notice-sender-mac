using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace NoticeSender.Automation;

/// <summary>Explicit authority for the pinned native regular-file modal. Dispatch is never a delivery receipt.</summary>
public abstract class NativeFileConfirmationPolicy
{
    private const string SupportedSha256 = "BFB4206321786D0F5C8A9E0FF2A1959E0E901A18BCEA40A652B49229B8B86BF1";
    public abstract bool IsRoomApproved(string roomName);
    internal abstract Lease Acquire(ChatTarget target, IReadOnlyList<string> files);

    protected static void VerifyExecutable(ChatTarget target)
    {
        using var process = Process.GetProcessById(target.ProcessId);
        string executable = process.MainModule?.FileName ?? throw new InvalidOperationException("카카오톡 실행 파일을 확인할 수 없습니다.");
        var version = FileVersionInfo.GetVersionInfo(executable);
        using var image = new FileStream(executable, FileMode.Open, FileAccess.Read, FileShare.Read);
        Require(process.ProcessName.Equals("KakaoTalk", StringComparison.OrdinalIgnoreCase) && version.FileMajorPart == 26 && version.FileMinorPart == 9 && version.FileBuildPart == 2 && version.FilePrivatePart == 5342 &&
            Convert.ToHexString(SHA256.HashData(image)) == SupportedSha256, "파일 확인에 검증된 카카오톡 버전/해시가 아닙니다.");
    }

    internal sealed class Lease : IDisposable
    {
        private readonly object gate = new();
        private readonly ChatTarget target;
        internal IReadOnlyList<string> Files { get; }
        private readonly Func<uint> readInput;
        private readonly uint inputStamp;
        private readonly Action<ChatTarget, nint, IReadOnlyList<string>> capture;
        private readonly Action requireFilesUnchanged;
        private readonly Action? release;
        private bool attempted, disposed;

        internal Lease(ChatTarget target, IReadOnlyList<string> paths, Func<uint> readInput,
            Action<ChatTarget, nint, IReadOnlyList<string>> capture, Action requireFilesUnchanged, Action? release = null)
        {
            this.target = target; Files = Array.AsReadOnly(paths.ToArray());
            this.readInput = readInput ?? throw new ArgumentNullException(nameof(readInput));
            this.capture = capture ?? throw new ArgumentNullException(nameof(capture));
            this.requireFilesUnchanged = requireFilesUnchanged ?? throw new ArgumentNullException(nameof(requireFilesUnchanged));
            this.release = release; inputStamp = readInput();
        }

        internal void RequireUnchanged()
        {
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                RequireInputUnchanged(); requireFilesUnchanged(); RequireInputUnchanged();
            }
        }

        private void RequireInputUnchanged() => Require(readInput() == inputStamp, "사용자 입력을 감지하여 파일 확인을 중단했습니다.");

        internal void Confirm(nint modal, Action finalChecks, Action dispatch, CancellationToken token)
        {
            lock (gate)
            {
                if (attempted) throw new InvalidOperationException("같은 파일 확인을 재시도할 수 없습니다.");
                attempted = true; // Including audit/check/cancellation/dispatch failures.
                token.ThrowIfCancellationRequested(); RequireUnchanged(); finalChecks();
                capture(target, modal, Files); // Required audit must throw on failure.
                token.ThrowIfCancellationRequested(); RequireUnchanged(); finalChecks(); RequireInputUnchanged(); token.ThrowIfCancellationRequested(); dispatch();
            }
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (disposed) return;
                disposed = true; release?.Invoke();
            }
        }
    }

    protected static void Require(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }
    [StructLayout(LayoutKind.Sequential)] private struct LastInput { public uint Size, Time; }
    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LastInput value);
    protected static uint LastInputStamp()
    {
        var value = new LastInput { Size = (uint)Marshal.SizeOf<LastInput>() };
        Require(GetLastInputInfo(ref value), "마지막 사용자 입력을 확인할 수 없습니다."); return value.Time;
    }
}
