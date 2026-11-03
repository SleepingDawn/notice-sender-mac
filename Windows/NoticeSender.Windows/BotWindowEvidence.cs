using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using NoticeSender.Automation;

namespace NoticeSender.Windows;

internal interface IBotEvidenceCapture
{
    BotEvidenceCaptureResult Capture(ChatTarget target, string stage, string artifactDirectory);
}

internal sealed record BotEvidenceCaptureResult(DateTimeOffset At, string Stage, long RootWindowHandle,
    IReadOnlyList<BotEvidenceImage> Images, string? Error = null);

internal sealed record BotEvidenceImage(string Role, long WindowHandle, int Width, int Height,
    string? Path, string Method, string? Error);

/// <summary>Optional visual diagnostics of an exact verified bot root and its same-process owned windows. Ownership does not establish modality. Never authorizes or completes delivery.</summary>
internal sealed class BotWindowEvidence : IBotEvidenceCapture
{
    private int sequence;
    public BotEvidenceCaptureResult Capture(ChatTarget target, string stage, string artifactDirectory)
    {
        DateTimeOffset at = DateTimeOffset.Now;
        var images = new List<BotEvidenceImage>();
        try
        {
            string? guard = MatchTarget(target);
            if (guard != null) return new(at, stage, target.WindowHandle.ToInt64(), images, guard);
            var windows = new List<(nint Handle, string Role)> { (target.WindowHandle, "bot-root") };
            Native.EnumWindows((window, _) =>
            {
                if (windows.Count < 9 && window != target.WindowHandle && Native.IsWindowVisible(window) && OwnedByTarget(window, target))
                    windows.Add((window, "owned-window"));
                return true;
            }, 0);
            foreach (var (window, role) in windows)
            {
                if (MatchTarget(target) is string changed) return new(at, stage, target.WindowHandle.ToInt64(), images, changed);
                images.Add(CaptureWindow(target, window, role, stage, artifactDirectory));
            }
            return new(at, stage, target.WindowHandle.ToInt64(), images, images.FirstOrDefault()?.Error);
        }
        catch (Exception ex) { return new(at, stage, target.WindowHandle.ToInt64(), images, "증거 캡처 진단: " + ex.Message); }
    }

    private static string? MatchTarget(ChatTarget target)
    {
        try { BotSendTestOptions.ValidateDestination(target.RoomName, true); }
        catch (ArgumentException) { return "허용 봇 이외의 창은 캡처하지 않습니다."; }
        if (target.ProcessId <= 0 || target.WindowHandle == 0) return "유효한 검증 프로세스 / 창 식별자가 없습니다.";
        if (!Native.IsWindow(target.WindowHandle)) return "검증한 봇 창이 더 이상 존재하지 않습니다.";
        if (Pid(target.WindowHandle) != target.ProcessId) return "검증한 봇 창의 프로세스가 변경되었습니다.";
        if (!string.Equals(Title(target.WindowHandle), target.RoomName, StringComparison.Ordinal)) return "검증한 봇 방의 창 제목과 다릅니다.";
        if (Native.GetAncestor(target.WindowHandle, 2) != target.WindowHandle) return "검증한 봇 루트 창이 아닙니다.";
        return null;
    }

    private static int Pid(nint window) { Native.GetWindowThreadProcessId(window, out uint pid); return unchecked((int)pid); }
    private static string? Title(nint window)
    {
        int length = Native.GetWindowTextLength(window);
        if (length < 1 || length > 512) return null;
        var value = new StringBuilder(length + 1);
        Native.GetWindowText(window, value, value.Capacity);
        return value.ToString();
    }
    private static bool OwnedByTarget(nint window, ChatTarget target)
    {
        if (!Native.IsWindow(window) || Pid(window) != target.ProcessId) return false;
        var seen = new HashSet<nint>();
        for (int depth = 0; depth < 32 && window != 0 && seen.Add(window); depth++)
        {
            window = Native.GetWindow(window, 4);
            if (window == target.WindowHandle) return true;
            if (window != 0 && Pid(window) != target.ProcessId) return false;
        }
        return false;
    }

    private BotEvidenceImage CaptureWindow(ChatTarget target, nint window, string role, string stage, string folder)
    {
        int width = 0, height = 0;
        string method = "PrintWindow/PW_RENDERFULLCONTENT";
        BotEvidenceImage Error(string error) => new(role, window.ToInt64(), width, height, null, method, error);
        try
        {
            if (MatchTarget(target) is string guard) return Error(guard);
            if (window != target.WindowHandle && !OwnedByTarget(window, target)) return Error("모달 창의 소유 관계가 변경되었습니다.");
            if (!Native.GetWindowRect(window, out var rect)) return Error("창 크기를 읽지 못했습니다.");
            long w = (long)rect.Right - rect.Left, h = (long)rect.Bottom - rect.Top;
            if (w < 1 || h < 1 || w > 4096 || h > 4096 || w * h > 16_777_216) return Error("창 크기가 캡처 한도를 벗어납니다.");
            width = (int)w; height = (int)h;
            using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppRgb);
            bool painted;
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Black);
                nint destination = graphics.GetHdc();
                try { painted = Native.PrintWindow(window, destination, 2); }
                finally { graphics.ReleaseHdc(destination); }
            }
            if (!painted || Blank(bitmap))
            {
                // Screen-based fallback is limited to the exact bot root while that root is foreground.
                // Owned modals never use this fallback, and no other application/window is captured.
                if (window != target.WindowHandle || Native.GetForegroundWindow() != target.WindowHandle || MatchTarget(target) != null)
                    return Error(painted ? "PrintWindow 결과가 비어 있습니다. 봇 루트가 전경이 아니므로 대체 캡처를 건너뛰었습니다." : "PrintWindow가 실패했습니다. 안전한 전경 대체 캡처 조건이 아닙니다.");
                method = "GetWindowDC/BitBlt (exact foreground bot root)";
                nint source = Native.GetWindowDC(window);
                if (source == 0) return Error("봇 창의 표시 DC를 가져오지 못했습니다.");
                try
                {
                    using var graphics = Graphics.FromImage(bitmap);
                    nint destination = graphics.GetHdc();
                    try
                    {
                        if (Native.GetForegroundWindow() != target.WindowHandle || MatchTarget(target) != null) return Error("대체 캡처 직전 봇 전경 / 소유권이 변경되었습니다.");
                        painted = Native.BitBlt(destination, 0, 0, width, height, source, 0, 0, 0x00CC0020);
                    }
                    finally { graphics.ReleaseHdc(destination); }
                }
                finally { Native.ReleaseDC(window, source); }
                if (!painted || Blank(bitmap)) return Error("안전한 봇 전경 대체 캡처도 실패했거나 비어 있습니다.");
            }
            if (MatchTarget(target) is string after) return Error(after);
            if (window != target.WindowHandle && !OwnedByTarget(window, target)) return Error("캡처 중 모달 창의 소유 관계가 변경되었습니다.");
            if (method.StartsWith("GetWindowDC", StringComparison.Ordinal) && Native.GetForegroundWindow() != target.WindowHandle)
                return Error("대체 캡처 중 전경이 변경되어 이미지를 저장하지 않았습니다.");
            Directory.CreateDirectory(folder);
            string safeStage = new(stage.Take(100).Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray());
            string path = System.IO.Path.Combine(folder, $"bot-evidence-{Interlocked.Increment(ref sequence):D3}-{safeStage}-{role}-{window.ToInt64():X}.png");
            bitmap.Save(path, ImageFormat.Png);
            return new(role, window.ToInt64(), width, height, path, method, null);
        }
        catch (Exception ex) { return Error("이미지 캡처 진단: " + ex.Message); }
    }

    private static bool Blank(Bitmap bitmap)
    {
        var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
        try
        {
            int x0 = bitmap.Width / 10, y0 = bitmap.Height / 10;
            int reference = Marshal.ReadInt32(data.Scan0, y0 * data.Stride + x0 * 4) & 0x00FFFFFF;
            for (int y = y0; y < Math.Max(y0 + 1, bitmap.Height * 9 / 10); y += Math.Max(1, bitmap.Height / 100))
                for (int x = x0; x < Math.Max(x0 + 1, bitmap.Width * 9 / 10); x += Math.Max(1, bitmap.Width / 100))
                    if ((Marshal.ReadInt32(data.Scan0, y * data.Stride + x * 4) & 0x00FFFFFF) != reference) return false;
            return true;
        }
        finally { bitmap.UnlockBits(data); }
    }

    private static class Native
    {
        internal delegate bool EnumWindowsCallback(nint window, nint parameter);
        [StructLayout(LayoutKind.Sequential)] internal struct Rect { internal int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsWindow(nint window);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsWindowVisible(nint window);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint window, out uint processId);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextLengthW")] internal static extern int GetWindowTextLength(nint window);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextW")] internal static extern int GetWindowText(nint window, StringBuilder text, int maximum);
        [DllImport("user32.dll")] internal static extern nint GetAncestor(nint window, uint flags);
        [DllImport("user32.dll")] internal static extern nint GetWindow(nint window, uint command);
        [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumWindowsCallback callback, nint parameter);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetWindowRect(nint window, out Rect rectangle);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool PrintWindow(nint window, nint destination, uint flags);
        [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern nint GetWindowDC(nint window);
        [DllImport("user32.dll")] internal static extern int ReleaseDC(nint window, nint dc);
        [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool BitBlt(nint destination, int x, int y, int width, int height, nint source, int sourceX, int sourceY, uint operation);
    }
}
