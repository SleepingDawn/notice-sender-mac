using System.Runtime.InteropServices;
using NoticeSender.Automation;

internal static class NativeFileConfirmationTests
{
    public static Task SelfChatAuthorizationGuards()
    {
        using var f = new SyntheticFiles();
        Reject(() => new SyntheticFileConfirmationPolicy("임세용", (_, _, _) => { }));
        Reject(() => SyntheticFileConfirmationPolicy.ForSelfChat("임세용", false, (_, _, _) => { }));
        foreach (string room in new[] { "임세용 ", "임세용(나에게 보내기)", "KREAM", "다른 사람" })
            Reject(() => SyntheticFileConfirmationPolicy.ForSelfChat(room, true, (_, _, _) => { }));
        var policy = SyntheticFileConfirmationPolicy.ForSelfChat("임세용", true, (_, _, _) => { });
        if (!policy.IsSelfChat || !policy.IsRoomApproved("임세용") || policy.IsRoomApproved("KREAM")) throw new Exception("Self-chat authorization leaked into bot scope.");
        using (var lease = policy.Freeze(f.Target with { RoomName = "임세용" }, new[] { f.File }, () => 7u)) lease.RequireUnchanged();
        Reject(() => policy.Freeze(f.Target, new[] { f.File }, () => 7u));
        File.WriteAllText(f.File, "ordinary private content");
        Reject(() => policy.Freeze(f.Target with { RoomName = "임세용" }, new[] { f.File }, () => 7u));
        return Task.CompletedTask;
    }
    public static Task SyntheticManifestGuards()
    {
        using var f = new SyntheticFiles();
        var policy = new SyntheticFileConfirmationPolicy("KREAM", (_, _, _) => { });
        using (var lease = policy.Freeze(f.Target, new[] { f.File }, () => 7u)) lease.RequireUnchanged();
        Reject(() => policy.Freeze(f.Target with { RoomName = "human room" }, new[] { f.File }, () => 7u));
        Reject(() => policy.Acquire(f.Target, new[] { f.File })); // This fixture process is not the pinned Kakao binary.
        File.WriteAllText(f.File, "private content must never be accepted");
        Reject(() => policy.Freeze(f.Target, new[] { f.File }, () => 7u));
        using var korean = new SyntheticFiles("E-한글자료.txt");
        using (var lease = policy.Freeze(korean.Target, new[] { korean.File }, () => 7u))
        {
            lease.RequireUnchanged();
            bool writeBlocked = false;
            try { using var writer = new FileStream(korean.File, FileMode.Open, FileAccess.Write, FileShare.Read); }
            catch (IOException) { writeBlocked = true; }
            if (!writeBlocked) throw new Exception("Frozen Korean synthetic bytes were not protected from writers.");
        }
        Reject(() => policy.Freeze(korean.Target with { RoomName = "human room" }, new[] { korean.File }, () => 7u));
        Reject(() => policy.Freeze(korean.Target, new[] { korean.File, korean.File }, () => 7u));
        string mixed = korean.Sibling("B-single.txt");
        Reject(() => policy.Freeze(korean.Target, new[] { korean.File, mixed }, () => 7u));
        foreach (string invalid in new[] { "E-한글자료2.txt", "E-한글자료.TXT" })
        {
            using var changedName = new SyntheticFiles(invalid);
            Reject(() => policy.Freeze(changedName.Target, new[] { changedName.File }, () => 7u));
        }
        File.WriteAllText(korean.File, "modified Korean attachment");
        Reject(() => policy.Freeze(korean.Target, new[] { korean.File }, () => 7u));
        return Task.CompletedTask;
    }
    public static Task OnceCaptureInputCancellation()
    {
        foreach (string suffix in new[] { "D-only.txt", "E-한글자료.txt" })
        {
            using var f = new SyntheticFiles(suffix);
            foreach (string mode in new[] { "normal", "capture-fails", "input-during-capture", "scope-during-capture", "cancel-during-capture" })
            {
                uint input = 7; int captured = 0, dispatches = 0; bool changed = false; using var cancel = new CancellationTokenSource();
                var policy = new SyntheticFileConfirmationPolicy("KREAM", (target, modal, files) =>
                {
                    captured++;
                    if (target != f.Target || modal != 999 || files.Count != 1 || files[0] != f.File) throw new Exception("Capture did not receive the exact frozen manifest/target.");
                    if (mode == "capture-fails") throw new InvalidOperationException("required capture failed");
                    if (mode == "input-during-capture") input++;
                    if (mode == "scope-during-capture") changed = true;
                    if (mode == "cancel-during-capture") cancel.Cancel();
                });
                using var lease = policy.Freeze(f.Target, new[] { f.File }, () => input);
                bool rejected = false;
                try { lease.Confirm(999, () => { if (changed) throw new InvalidOperationException("scope changed"); }, () => dispatches++, cancel.Token); }
                catch (Exception ex) when (ex is InvalidOperationException or OperationCanceledException) { rejected = true; }
                if (captured != 1 || dispatches != (mode == "normal" ? 1 : 0) || rejected != (mode != "normal")) throw new Exception("Unsafe file confirmation after " + mode);
                Reject(() => lease.Confirm(999, () => { }, () => dispatches++, default));
                if (dispatches != (mode == "normal" ? 1 : 0)) throw new Exception("Consumed confirmation was retried.");
            }
        }
        return Task.CompletedTask;
    }
    private static void Reject(Action action) { try { action(); } catch (InvalidOperationException) { return; } throw new Exception("Unsafe synthetic file policy was accepted."); }
    private sealed class SyntheticFiles : IDisposable
    {
        private readonly string directory;
        private readonly string stamp;
        private readonly List<string> files = new();
        public string File { get; }
        public ChatTarget Target { get; } = new(Environment.ProcessId, 123, "KREAM", 456);
        public SyntheticFiles(string suffix = "D-only.txt")
        {
            stamp = "20261104-075500-123-" + Guid.NewGuid().ToString("N")[..8];
            directory = Path.Combine(Path.GetTempPath(), "NoticeSender-BotSendTest-" + stamp); Directory.CreateDirectory(directory);
            File = Sibling(suffix);
        }
        public string Sibling(string suffix)
        {
            string path = Path.Combine(directory, "NoticeSender-SYNTHETIC-" + stamp + "-" + suffix);
            System.IO.File.WriteAllText(path, "NoticeSender synthetic bot-only attachment test. No student data. " + stamp + " " + Path.GetFileName(path), System.Text.Encoding.UTF8);
            files.Add(path); return path;
        }
        public void Dispose() { foreach (string path in files) System.IO.File.Delete(path); Directory.Delete(directory); }
    }
    public static Task RegularFileProfile()
    {
        using var f = new Fixture();
        var pin = WindowsKakaoClient.InspectNativeFileModal(f.Target, f.Modal, new HashSet<nint>());
        if (pin.Window != f.Modal || pin.FileView != f.FileView || pin.HiddenImageView != f.ImageView) throw new Exception("Exact regular-file modal children were not pinned.");
        return Task.CompletedTask;
    }
    public static Task RejectChangedProfiles()
    {
        foreach (string change in new[] { "image", "duplicate", "owner", "enabled-root", "other-modal", "old-modal", "reparent" })
        {
            using var f = new Fixture(); var baseline = new HashSet<nint>();
            if (change == "image") ShowWindow(f.ImageView, 5);
            if (change == "duplicate") f.AddDuplicate();
            if (change == "owner") SetWindowLongPtr(f.Modal, -8, 0);
            if (change == "enabled-root") EnableWindow(f.Root, true);
            if (change == "other-modal") f.AddOtherModal();
            if (change == "old-modal") baseline.Add(f.Modal);
            if (change == "reparent") SetParent(f.FileView, f.Root);
            bool rejected = false;
            try { WindowsKakaoClient.InspectNativeFileModal(f.Target, f.Modal, baseline); }
            catch (InvalidOperationException) { rejected = true; }
            if (!rejected) throw new Exception("Unsafe native confirmation profile accepted: " + change);
        }
        return Task.CompletedTask;
    }
    private sealed class Fixture : IDisposable
    {
        private readonly List<nint> handles = new();
        private readonly WndProc procedure = DefWindowProc;
        private readonly string[] classes = { "EVA_Window_Dblclk", "EVA_ChildWindow", "EVA_ChildWindow_Dblclk" };
        private readonly nint library;
        public nint Root { get; }
        public nint Modal { get; }
        public nint FileView { get; }
        public nint ImageView { get; }
        public ChatTarget Target { get; }
        public Fixture()
        {
            foreach (string name in classes)
            {
                var c = new WindowClass { Procedure = Marshal.GetFunctionPointerForDelegate(procedure), ClassName = name };
                if (RegisterClass(ref c) == 0) throw new Exception("Cannot register synthetic file-modal class.");
            }
            library = LoadLibrary("msftedit.dll");
            Root = Create("EVA_Window_Dblclk", "KREAM", 0x10000000, 0, 0);
            nint composer = Create("RICHEDIT50W", "", 0x50000004, Root, 1006);
            Modal = Create("EVA_Window_Dblclk", "FileTransferWnd_synthetic", 0x90000000, Root, 0);
            FileView = Create("EVA_ChildWindow", "", 0x50000000, Modal, 1001);
            ImageView = Create("EVA_ChildWindow_Dblclk", "", 0x40000000, Modal, 1002);
            EnableWindow(Root, false);
            Target = new(Environment.ProcessId, Root, "KREAM", composer);
        }
        private nint Create(string cls, string title, uint style, nint parent, int id)
        {
            nint w = CreateWindowEx(0, cls, title, style, 0, 0, 4, 4, parent, id, 0, 0);
            if (w == 0) throw new Exception("Cannot create synthetic file-modal control."); handles.Add(w); return w;
        }
        public void AddDuplicate() => Create("EVA_ChildWindow", "", 0x50000000, Modal, 1001);
        public void AddOtherModal() => Create("EVA_Window_Dblclk", "another synthetic popup", 0x90000000, Root, 0);
        public void Dispose()
        {
            foreach (nint h in handles.AsEnumerable().Reverse()) DestroyWindow(h);
            if (library != 0) FreeLibrary(library);
            foreach (string cls in classes) UnregisterClass(cls, 0);
            GC.KeepAlive(procedure);
        }
    }
    private delegate nint WndProc(nint window, uint message, nuint wParam, nint lParam);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct WindowClass { public uint Style; public nint Procedure; public int ClassExtra, WindowExtra; public nint Instance, Icon, Cursor, Background; public string? Menu; public string ClassName; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint LoadLibrary(string path);
    [DllImport("kernel32.dll")] private static extern bool FreeLibrary(nint handle);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClass(ref WindowClass windowClass);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool UnregisterClass(string name, nint instance);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint CreateWindowEx(uint ex, string cls, string title, uint style, int x, int y, int width, int height, nint parent, nint id, nint instance, nint parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint DefWindowProc(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint window, int mode);
    [DllImport("user32.dll")] private static extern bool EnableWindow(nint window, bool enabled);
    [DllImport("user32.dll")] private static extern nint SetParent(nint window, nint parent);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint window, int index, nint value);
}
