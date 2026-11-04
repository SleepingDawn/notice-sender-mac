using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using NoticeSender.Automation;

namespace NoticeSender.Windows;

internal static class ExactTranscriptCaptureSmokeTests
{
    internal static void Run()
    {
        var failures = new List<Exception>();
        foreach (Action check in new Action[] { RejectNewDuplicate, ValidateCraftedBitmapContent, CaptureFixtureDiagnostic })
            try { check(); } catch (Exception ex) { failures.Add(ex); }
        if (failures.Count > 0) throw new AggregateException("Exact transcript capture guards failed.", failures);
    }
    private static void RejectNewDuplicate()
    {
        using var f = new Fixture(); var capture = new ExactTranscriptCapture(f.Target); capture.RequireCurrent(); f.AddTranscript();
        try { capture.RequireCurrent(); }
        catch (InvalidOperationException) { return; }
        throw new Exception("A second exact transcript added after pinning was accepted.");
    }
    private static void CaptureFixtureDiagnostic()
    {
        // PW_RENDERFULLCONTENT does not honor this fake child's WM_PRINT handler consistently on the VM.
        // Record its actual pixels rather than assuming a native fake window must produce a blank bitmap.
        using var f = new Fixture(); var capture = new ExactTranscriptCapture(f.Target);
        string output = Path.Combine(Path.GetTempPath(), "NoticeSender-capture-unexpected.png");
        string report = Path.Combine(Path.GetTempPath(), "NoticeSender-capture-fixture.json"); File.Delete(output);
        byte[] bytes;
        try { bytes = ExactTranscriptCapture.Capture(f.Transcript, capture.RequireCurrent); }
        catch (InvalidOperationException ex)
        {
            File.WriteAllText(report, JsonSerializer.Serialize(new { accepted = false, reason = ex.Message })); return;
        }
        File.WriteAllBytes(output, bytes);
        using var stream = new MemoryStream(bytes); using var bitmap = new Bitmap(stream);
        var colors = new Dictionary<int, int>();
        for (int y = 0; y < bitmap.Height; y++) for (int x = 0; x < bitmap.Width; x++)
        { int argb = bitmap.GetPixel(x, y).ToArgb(); colors[argb] = colors.GetValueOrDefault(argb) + 1; }
        File.WriteAllText(report, JsonSerializer.Serialize(new {
            accepted = true, bitmap.Width, bitmap.Height, colors = colors.Count, image = output,
            histogram = colors.OrderByDescending(pair => pair.Value).Select(pair => new { argb = pair.Key.ToString("X8"), pixels = pair.Value }).ToArray()
        }));
    }
    private static void ValidateCraftedBitmapContent()
    {
        var validate = typeof(ExactTranscriptCapture).GetMethod("RequirePaintedBitmap", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new Exception("RequirePaintedBitmap does not exist: capture content is not validated.");
        void Check(Bitmap bitmap)
        {
            try { validate.Invoke(null, new object[] { bitmap }); }
            catch (TargetInvocationException ex) when (ex.InnerException != null) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
        }
        foreach (Color color in new[] { Color.Magenta, Color.Black, Color.White })
        {
            using var bitmap = new Bitmap(80, 60, PixelFormat.Format32bppRgb);
            using (var graphics = Graphics.FromImage(bitmap)) graphics.Clear(color);
            bool rejected = false; try { Check(bitmap); } catch (InvalidOperationException) { rejected = true; }
            if (!rejected) throw new Exception("Uniform " + color.Name + " capture pixels were accepted.");
        }
        // Known painted content is tested independently of this VM's fake-child PrintWindow behavior.
        using var painted = new Bitmap(80, 60, PixelFormat.Format32bppRgb);
        using (var graphics = Graphics.FromImage(painted)) { graphics.Clear(Color.White); graphics.FillRectangle(Brushes.Black, 10, 10, 60, 40); }
        Check(painted);
    }
    private sealed class Fixture : IDisposable
    {
        private readonly List<nint> windows = new();
        private readonly Procedure procedure;
        private readonly string[] classes = { "EVA_Window_Dblclk", "EVA_VH_ListControl_Dblclk" };
        private readonly nint library;
        internal nint Root { get; }
        internal nint Transcript { get; }
        internal ChatTarget Target { get; }
        internal Fixture()
        {
            procedure = DefWindowProc;
            foreach (string name in classes)
            {
                var type = new WindowClass { Procedure = Marshal.GetFunctionPointerForDelegate(procedure), ClassName = name };
                if (RegisterClass(ref type) == 0) throw new Exception("Cannot register exact transcript capture fixture class.");
            }
            library = LoadLibrary("msftedit.dll");
            if (library == 0) throw new Exception("Cannot load fixture RichEdit.");
            Root = Create(classes[0], "NoticeSender exact capture fixture", 0x90000000, 0, 0, 120, 100);
            nint composer = Create("RICHEDIT50W", "", 0x50000004, Root, 1006, 80, 20);
            Transcript = AddTranscript(); Target = new(Environment.ProcessId, Root, "NoticeSender exact capture fixture", composer);
        }
        internal nint AddTranscript() => Create(classes[1], "", 0x50000000, Root, 100, 80, 60);
        private nint Create(string cls, string title, uint style, nint parent, int id, int width, int height)
        {
            nint window = CreateWindowEx(0, cls, title, style, 0, 0, width, height, parent, id, 0, 0);
            if (window == 0) throw new Exception("Cannot create exact transcript capture fixture window."); windows.Add(window); return window;
        }
        public void Dispose()
        {
            for (int i = windows.Count - 1; i >= 0; i--) DestroyWindow(windows[i]);
            if (library != 0) FreeLibrary(library);
            foreach (string name in classes) UnregisterClass(name, 0);
            GC.KeepAlive(procedure);
        }
    }
    private delegate nint Procedure(nint window, uint message, nint wParam, nint lParam);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass { public uint Style; public nint Procedure; public int ClassExtra, WindowExtra; public nint Instance, Icon, Cursor, Background; public string? Menu; public string ClassName; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClass(ref WindowClass type);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool UnregisterClass(string name, nint instance);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint CreateWindowEx(uint extra, string cls, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint DefWindowProc(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint LoadLibrary(string name);
    [DllImport("kernel32.dll")] private static extern bool FreeLibrary(nint library);
}
