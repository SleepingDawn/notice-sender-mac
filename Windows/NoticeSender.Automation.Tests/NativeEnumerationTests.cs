using System.Runtime.InteropServices;
using NoticeSender.Automation;

internal static class NativeEnumerationTests
{
    public static Task NamedRoomSnapshot()
    {
        const string name = "NoticeSender exact synthetic room";
        nint target = CreateWindowEx(0, "STATIC", name, 0, 0, 0, 1, 1, 0, 0, 0, 0);
        nint unrelated = CreateWindowEx(0, "STATIC", "unrelated synthetic modal", 0, 0, 0, 1, 1, 0, 0, 0, 0);
        nint stale = CreateWindowEx(0, "STATIC", "disappearing synthetic modal", 0, 0, 0, 1, 1, 0, 0, 0, 0);
        if (target == 0 || unrelated == 0 || stale == 0) throw new Exception("Cannot create native snapshot fixture.");
        DestroyWindow(stale);
        try
        {
            var selected = WindowsKakaoClient.NamedRoomWindows(new[] { stale, unrelated, target }, name);
            if (selected.Length != 1 || selected[0].Current.NativeWindowHandle != target) throw new Exception("Named-room selection converted a stale/unrelated window or lost its exact room.");
        }
        finally { DestroyWindow(unrelated); DestroyWindow(target); }
        return Task.CompletedTask;
    }
    public static Task NamedRoomDuplicates()
    {
        const string name = "NoticeSender duplicate synthetic room";
        nint first = CreateWindowEx(0, "STATIC", name, 0, 0, 0, 1, 1, 0, 0, 0, 0);
        nint second = CreateWindowEx(0, "STATIC", name, 0, 0, 0, 1, 1, 0, 0, 0, 0);
        if (first == 0 || second == 0) throw new Exception("Cannot create duplicate-room fixture.");
        try
        {
            var selected = WindowsKakaoClient.NamedRoomWindows(new[] { first, second }, name);
            if (selected.Length != 2 || !selected.Select(e => (nint)e.Current.NativeWindowHandle).ToHashSet().SetEquals(new[] { first, second }))
                throw new Exception("Named-room selection hid an ambiguous duplicate room.");
        }
        finally { DestroyWindow(second); DestroyWindow(first); }
        return Task.CompletedTask;
    }
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
