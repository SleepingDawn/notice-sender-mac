using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using NoticeSender.Automation;

namespace NoticeSender.Windows;

// Captures an identified native control; pixels never select a window or dispatch input.
internal sealed class ExactTranscriptCapture
{
    private readonly ChatTarget target;
    internal nint Transcript { get; }
    internal ExactTranscriptCapture(ChatTarget target)
    {
        this.target = target;
        RequireRoot(target, true);
        var matches = TranscriptCandidates();
        if (matches.Length != 1) throw new InvalidOperationException("대화 내용 창을 고유하게 확인할 수 없습니다.");
        Transcript = matches.Single(); RequireCurrent();
    }
    internal void RequireCurrent()
    {
        RequireRoot(target, true);
        var matches = TranscriptCandidates();
        if (matches.Length != 1 || matches[0] != Transcript)
            throw new InvalidOperationException("고유하게 검증한 대화 내용 창이 변경되었습니다.");
        if (!Native.IsWindow(Transcript) || Pid(Transcript) != target.ProcessId || Native.GetParent(Transcript) != target.WindowHandle ||
            Native.GetDlgCtrlID(Transcript) != 100 || Class(Transcript) != "EVA_VH_ListControl_Dblclk" || !Native.IsWindowVisible(Transcript) || !Native.IsWindowEnabled(Transcript))
            throw new InvalidOperationException("검증한 대화 내용 창이 변경되었습니다.");
    }
    private nint[] TranscriptCandidates()
    {
        var matches = new List<nint>();
        Native.EnumChildWindows(target.WindowHandle, (w, _) =>
        {
            if (Native.GetParent(w) == target.WindowHandle && Native.GetDlgCtrlID(w) == 100 && Class(w) == "EVA_VH_ListControl_Dblclk") matches.Add(w);
            return true;
        }, 0);
        return matches.ToArray();
    }
    internal byte[] Read(CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); RequireCurrent();
        byte[] result = BotWindowEvidence.WithDeadline(() => Capture(Transcript, RequireCurrent), TimeSpan.FromSeconds(8));
        token.ThrowIfCancellationRequested(); RequireCurrent(); return result;
    }
    internal static void RequireRoot(ChatTarget target, bool enabled)
    {
        if (target.ProcessId <= 0 || !Native.IsWindow(target.WindowHandle) || Pid(target.WindowHandle) != target.ProcessId ||
            Native.GetAncestor(target.WindowHandle, 2) != target.WindowHandle || Title(target.WindowHandle) != target.RoomName ||
            Class(target.WindowHandle) != "EVA_Window_Dblclk" || !Native.IsWindowVisible(target.WindowHandle) || Native.IsWindowEnabled(target.WindowHandle) != enabled ||
            !Native.IsWindow(target.ComposerHandle) || Pid(target.ComposerHandle) != target.ProcessId || !Native.IsChild(target.WindowHandle, target.ComposerHandle) ||
            Native.GetDlgCtrlID(target.ComposerHandle) != 1006 || Class(target.ComposerHandle) != "RICHEDIT50W")
            throw new InvalidOperationException("검증한 채팅방과 입력란의 식별 정보가 변경되었습니다.");
    }
    internal static byte[] Capture(nint window, Action validate)
    {
        validate();
        if (!Native.GetWindowRect(window, out var before)) throw new InvalidOperationException("창 크기를 읽을 수 없습니다.");
        int width = before.Right - before.Left, height = before.Bottom - before.Top;
        if (width < 1 || height < 1 || width > 4096 || height > 4096) throw new InvalidOperationException("캡처 크기가 지원 범위를 벗어납니다.");
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppRgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Magenta);
            nint dc = graphics.GetHdc();
            try { if (!Native.PrintWindow(window, dc, 2)) throw new InvalidOperationException("대상 창 이미지를 읽지 못했습니다."); }
            finally { graphics.ReleaseHdc(dc); }
        }
        validate();
        if (!Native.GetWindowRect(window, out var after) || !before.Equals(after)) throw new InvalidOperationException("캡처 중 창 크기 또는 위치가 변경되었습니다.");
        RequirePaintedBitmap(bitmap);
        using var output = new MemoryStream(); bitmap.Save(output, ImageFormat.Png); return output.ToArray();
    }
    internal static void RequirePaintedBitmap(Bitmap bitmap)
    {
        // Inspect the interior so a painted nonclient border cannot make an empty body acceptable.
        var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
        try
        {
            int x0 = bitmap.Width / 10, y0 = bitmap.Height / 10;
            int reference = Marshal.ReadInt32(data.Scan0, y0 * data.Stride + x0 * 4) & 0x00FFFFFF;
            for (int y = y0; y < Math.Max(y0 + 1, bitmap.Height * 9 / 10); y += Math.Max(1, bitmap.Height / 100))
                for (int x = x0; x < Math.Max(x0 + 1, bitmap.Width * 9 / 10); x += Math.Max(1, bitmap.Width / 100))
                    if ((Marshal.ReadInt32(data.Scan0, y * data.Stride + x * 4) & 0x00FFFFFF) != reference) return;
            throw new InvalidOperationException("대상 창의 캡처 내용이 비어 있거나 그려지지 않았습니다.");
        }
        finally { bitmap.UnlockBits(data); }
    }
    internal static int Pid(nint w) { Native.GetWindowThreadProcessId(w, out uint p); return (int)p; }
    internal static string Title(nint w) { var s = new StringBuilder(1024); Native.GetWindowText(w, s, s.Capacity); return s.ToString(); }
    internal static string Class(nint w) { var s = new StringBuilder(256); Native.GetClassName(w, s, s.Capacity); return s.ToString(); }
    internal static bool IsOwnedModal(nint w, ChatTarget t) => Native.IsWindow(w) && Pid(w) == t.ProcessId && Native.GetWindow(w, 4) == t.WindowHandle && Native.IsWindowVisible(w) && Native.IsWindowEnabled(w);
    private static class Native
    {
        internal delegate bool Callback(nint w, nint p);
        [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] internal static extern bool EnumChildWindows(nint w, Callback c, nint p);
        [DllImport("user32.dll")] internal static extern bool IsWindow(nint w);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint w);
        [DllImport("user32.dll")] internal static extern bool IsWindowEnabled(nint w);
        [DllImport("user32.dll")] internal static extern bool IsChild(nint p,nint w);
        [DllImport("user32.dll")] internal static extern nint GetParent(nint w);
        [DllImport("user32.dll")] internal static extern nint GetWindow(nint w,uint cmd);
        [DllImport("user32.dll")] internal static extern nint GetAncestor(nint w,uint flags);
        [DllImport("user32.dll")] internal static extern int GetDlgCtrlID(nint w);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint w,out uint p);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] internal static extern int GetWindowText(nint w,StringBuilder s,int n);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] internal static extern int GetClassName(nint w,StringBuilder s,int n);
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(nint w,out Rect r);
        [DllImport("user32.dll")] internal static extern bool PrintWindow(nint w,nint dc,uint flags);
    }
}
