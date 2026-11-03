using System.Runtime.InteropServices;
using NoticeSender.Automation;

internal static class NativeEnumerationTests
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowEx(uint exStyle, string className, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
    public static Task CustomTopLevel()
    {
        // A hidden standard static control is our own window; no Kakao or desktop control is touched.
        var window = CreateWindowEx(0, "STATIC", "NoticeSender synthetic native enumeration test", 0, 0, 0, 1, 1, 0, 0, 0, 0);
        if (window == 0) throw new Exception("Synthetic native window creation failed.");
        try
        {
            var matches = WindowsKakaoClient.DiscoverProcessWindows(new HashSet<int> { Environment.ProcessId });
            if (!matches.Contains(window)) throw new Exception("Native top-level static control was excluded by its UIA role.");
        }
        finally { DestroyWindow(window); }
        return Task.CompletedTask;
    }
}
