using System.Runtime.InteropServices;
using System.Text;
using NoticeSender.Automation;

internal static class NativeTranscriptTests
{
    public static Task ExactTranscript()
    {
        using var f = new Fixture();
        string text = WindowsKakaoClient.ReadNativeTranscript(f.Root, Environment.ProcessId);
        if (text != "receipt-synthetic-D-only.txt") throw new Exception("The exact native transcript filename was not read; actual length " + text.Length);
        if (f.EditText != "keep existing draft") throw new Exception("Reading receipt evidence changed the same-ID edit bait.");
        return Task.CompletedTask;
    }
    public static Task EditAndNestedBaitIgnored()
    {
        using var f = new Fixture(createTranscript: false);
        if (WindowsKakaoClient.ReadNativeTranscript(f.Root, Environment.ProcessId) != "" || f.EditText != "keep existing draft")
            throw new Exception("An Edit or nested control with ID100 was accepted as the room transcript.");
        return Task.CompletedTask;
    }
    public static Task AmbiguousTranscriptRejected()
    {
        using var f = new Fixture(); f.AddTranscript(); bool rejected = false;
        try { WindowsKakaoClient.ReadNativeTranscript(f.Root, Environment.ProcessId); }
        catch (InvalidOperationException) { rejected = true; }
        if (!rejected) throw new Exception("Multiple native transcripts were accepted or their contents were summed.");
        return Task.CompletedTask;
    }
    private sealed class Fixture : IDisposable
    {
        private const string ListClass = "EVA_VH_ListControl_Dblclk";
        private readonly WndProc procedure;
        private readonly List<nint> handles = new();
        private readonly Dictionary<nint, string> texts = new();
        public nint Root { get; }
        private readonly nint edit;
        public string EditText { get { var b = new StringBuilder(256); GetWindowText(edit, b, b.Capacity); return b.ToString(); } }
        public Fixture(bool createTranscript = true)
        {
            procedure = Procedure;
            var c = new WindowClass { Procedure = Marshal.GetFunctionPointerForDelegate(procedure), ClassName = ListClass };
            if (RegisterClass(ref c) == 0) throw new Exception("Cannot register synthetic transcript class.");
            Root = Create("STATIC", "synthetic transcript root", 0, 0);
            edit = Create("Edit", "keep existing draft", Root, 100);
            nint panel = Create("STATIC", "nested decoy parent", Root, 55);
            nint decoy = Create(ListClass, "", panel, 100); texts[decoy] = "nested-decoy-file.txt";
            if (createTranscript) AddTranscript();
        }
        public void AddTranscript() { nint list = Create(ListClass, "", Root, 100); texts[list] = "receipt-synthetic-D-only.txt"; }
        private nint Create(string cls, string title, nint parent, int id)
        {
            nint window = CreateWindowEx(0, cls, title, parent == 0 ? 0u : 0x50000000, 0, 0, 2, 2, parent, id, 0, 0);
            if (window == 0) throw new Exception("Cannot create synthetic native transcript control."); handles.Add(window); return window;
        }
        private nint Procedure(nint window, uint message, nuint wParam, nint lParam)
        {
            if (texts.TryGetValue(window, out string? value))
            {
                if (message == 0xE) return value.Length;
                if (message == 0xD)
                {
                    int copied = Math.Min(value.Length, Math.Max(0, checked((int)wParam) - 1));
                    if (wParam > 0) { Marshal.Copy(value.ToCharArray(), 0, lParam, copied); Marshal.WriteInt16(lParam, copied * 2, 0); }
                    return copied;
                }
            }
            return DefWindowProc(window, message, wParam, lParam);
        }
        public void Dispose() { foreach (var h in handles.AsEnumerable().Reverse()) DestroyWindow(h); UnregisterClass(ListClass, 0); GC.KeepAlive(procedure); }
    }
    private delegate nint WndProc(nint window, uint message, nuint wParam, nint lParam);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct WindowClass { public uint Style; public nint Procedure; public int ClassExtra, WindowExtra; public nint Instance, Icon, Cursor, Background; public string? Menu; public string ClassName; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClass(ref WindowClass windowClass);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool UnregisterClass(string name, nint instance);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint CreateWindowEx(uint ex, string cls, string title, uint style, int x, int y, int width, int height, nint parent, nint id, nint instance, nint parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint DefWindowProc(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint window, StringBuilder text, int count);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
}
