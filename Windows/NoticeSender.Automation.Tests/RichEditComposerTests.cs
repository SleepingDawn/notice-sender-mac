using System.Runtime.InteropServices;
using System.Reflection;
using System.Windows.Automation;
using NoticeSender.Automation;

internal static class RichEditComposerTests
{
    private const string Cue = "챗봇에게 메시지 입력";

    public static Task ApplicationCue() => Check(Cue, 0x949494, false, false, 1006, Cue, Cue);
    public static Task IdenticalUserDraft() => Check(Cue, 0x000000, false, false, 1006, Cue);
    public static Task ArbitraryGrayDraft() => Check("keep my existing draft", 0x949494, false, false, 1006, "keep my existing draft");
    public static Task UndoableCueDraft() => Check(Cue, 0x949494, true, false, 1006, Cue);
    public static Task MixedCueDraft() => Check(Cue, 0x949494, false, true, 1006, Cue);
    public static Task DifferentControl() => Check(Cue, 0x949494, false, false, 1007, Cue);
    public static Task StandardCueRequiresReviewedRoom()
    {
        const string standard = "메시지 입력";
        using var fixture = new RichEditFixture(standard, 0x949494, false, false, 1006);
        var composer = AutomationElement.FromHandle(fixture.Edit);
        nint root = (nint)NativeMethod("Parent").Invoke(null, new object[] { fixture.Edit })!;
        var target = new ChatTarget(Environment.ProcessId, root, "임세용", fixture.Edit);
        string[] approvedRooms = ["임세용"];
        var client = new WindowsKakaoClient(reviewedStandardCueRooms: approvedRooms); approvedRooms[0] = "changed room";
        if (WindowsKakaoClient.CaptureStandardComposerCue(composer) != standard ||
            client.CaptureAllowedComposerCue(target, composer) != standard ||
            new WindowsKakaoClient().CaptureAllowedComposerCue(target, composer) != null ||
            client.CaptureAllowedComposerCue(target with { RoomName = "other room" }, composer) != null ||
            client.CaptureAllowedComposerCue(target with { ComposerHandle = 0 }, composer) != null || fixture.Text != standard)
            throw new Exception("Standard cue permission was not exact, copied, room-scoped and nonmutating.");
        return Task.CompletedTask;
    }
    public static Task StandardCueDraftsRemainProtected()
    {
        foreach (var state in new[] { (Color: 0, Undo: false, Mixed: false), (Color: 0x949494, Undo: true, Mixed: false), (Color: 0x949494, Undo: false, Mixed: true) })
        {
            using var fixture = new RichEditFixture("메시지 입력", state.Color, state.Undo, state.Mixed, 1006);
            if (WindowsKakaoClient.CaptureStandardComposerCue(AutomationElement.FromHandle(fixture.Edit)) != null || fixture.Text != "메시지 입력")
                throw new Exception("A normal, undoable or mixed-format user draft was treated as a standard cue.");
        }
        using var bot = new RichEditFixture(Cue, 0x949494, false, false, 1006);
        var element = AutomationElement.FromHandle(bot.Edit); nint root = (nint)NativeMethod("Parent").Invoke(null, new object[] { bot.Edit })!;
        if (new WindowsKakaoClient(reviewedStandardCueRooms: new[] { "KREAM" }).CaptureAllowedComposerCue(new(Environment.ProcessId, root, "KREAM", bot.Edit), element) != null)
            throw new Exception("Standard cue permission implicitly enabled bot-cue replacement.");
        return Task.CompletedTask;
    }

    private static Task Check(string text, int color, bool undoable, bool mixed, int id, string expected, string? approvedCue = null)
    {
        using var fixture = new RichEditFixture(text, color, undoable, mixed, id);
        string actual = WindowsKakaoClient.ReadComposer(AutomationElement.FromHandle(fixture.Edit));
        if (actual != expected) throw new Exception($"Composer content was not classified safely (actual length {actual.Length}, expected {expected.Length}).");
        if (WindowsKakaoClient.CaptureReviewedBotCue(AutomationElement.FromHandle(fixture.Edit)) != approvedCue)
            throw new Exception("Reviewed cue fingerprint did not match the explicitly accepted document state.");
        if (fixture.Text != text) throw new Exception("Inspecting a composer changed its native document.");
        return Task.CompletedTask;
    }

    public static Task PermitGuards()
    {
        var target = new ChatTarget(123, 456, "KREAM", 789);
        int writes = 0;
        var permit = new WindowsKakaoClient.ComposerReplacementPermit(target, Cue);
        permit.Use(() => { }, () => Cue, () => writes++, default);
        bool reuseRejected = false;
        try { permit.Use(() => { }, () => Cue, () => writes++, default); }
        catch (InvalidOperationException) { reuseRejected = true; }
        if (writes != 1 || !reuseRejected) throw new Exception("One-operation replacement permission was reused.");
        var changed = new WindowsKakaoClient.ComposerReplacementPermit(target, Cue);
        bool changeRejected = false;
        try { changed.Use(() => { }, () => "fresh user draft", () => writes++, default); }
        catch (InvalidOperationException) { changeRejected = true; }
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        bool cancellationObserved = false;
        try { changed.Use(() => { }, () => Cue, () => writes++, cancelled.Token); }
        catch (OperationCanceledException) { cancellationObserved = true; }
        if (!changeRejected || !cancellationObserved || writes != 1) throw new Exception("Changed/cancelled replacement mutated content.");
        return Task.CompletedTask;
    }

    public static Task GrowingNativeDraft()
    {
        foreach (string initial in new[] { "", Cue })
        {
            using var fixture = new RichEditFixture(initial, 0, false, false, 1006);
            string fresh = initial + " appended user draft";
            fixture.ReplaceAfterLengthRead = fresh;
            bool rejected = false;
            try { NativeMethod("Text").Invoke(null, new object[] { fixture.Edit }); }
            catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException) { rejected = true; }
            if (!rejected || fixture.Text != fresh) throw new Exception("A growing native draft was silently truncated or changed.");
        }
        return Task.CompletedTask;
    }

    public static async Task QueuedEnter()
    {
        using var fixture = new RichEditFixture("synthetic message", 0, false, false, 1006);
        NativeMethod("Key").Invoke(null, new object[] { fixture.Edit, (uint)0x100, 0x0D });
        NativeMethod("Key").Invoke(null, new object[] { fixture.Edit, (uint)0x101, 0x0D });
        for (int n = 0; n < 100 && fixture.QueuedEnters == 0; n++) await Task.Delay(10);
        if (fixture.QueuedEnters != 1) throw new Exception("Enter bypassed the destination thread's normal message-queue preprocessing.");
    }
    public static Task RichEditChangeNotification()
    {
        using var fixture = new RichEditFixture("", 0, false, false, 1006);
        NativeMethod("ReplaceSelection").Invoke(null, new object[] { fixture.Edit, "new synthetic message" });
        if (fixture.Text != "new synthetic message" || fixture.ChangeNotifications < 1 || fixture.ChangeSource != fixture.Edit ||
            !fixture.Modified || !fixture.CanUndo || fixture.Caret != 21)
            throw new Exception("RichEdit replacement did not establish genuine edited content, undo, caret and automatic host notification.");
        return Task.CompletedTask;
    }
    public static Task FullApprovedSelection()
    {
        foreach (string text in new[] { "", Cue })
        {
            using var fixture = new RichEditFixture(text, 0, false, false, 1006);
            NativeMethod("SelectAll").Invoke(null, new object[] { fixture.Edit });
            try { NativeMethod("RequireFullSelection").Invoke(null, new object[] { fixture.Edit, text.Length }); }
            catch (TargetInvocationException ex) { throw new Exception("Full native selection was rejected at end " + fixture.Caret + " for content length " + text.Length, ex); }
            fixture.CollapseSelection();
            bool rejected = false;
            try { NativeMethod("RequireFullSelection").Invoke(null, new object[] { fixture.Edit, text.Length }); }
            catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException) { rejected = true; }
            if (!rejected) throw new Exception("A collapsed selection could append instead of replacing the approved document.");
        }
        return Task.CompletedTask;
    }
    private static MethodInfo NativeMethod(string name) => typeof(WindowsKakaoClient).GetNestedType("Native", BindingFlags.NonPublic)!.GetMethod(name)!;

    // Genuine RichEdit + Windows UIA provider, hosted on a pumping thread. No Kakao process is touched.
    private sealed class RichEditFixture : IDisposable
    {
        private readonly ManualResetEventSlim stop = new();
        private readonly Thread thread;
        private readonly WndProc procedure;
        private readonly WndProc parentProcedure;
        private nint originalProcedure;
        private nint originalParentProcedure;
        public string? ReplaceAfterLengthRead;
        public int QueuedEnters;
        public int ChangeNotifications;
        public nint ChangeSource;
        public bool Modified => SendMessage(Edit, 0xB8, 0, 0) != 0;
        public bool CanUndo => SendMessage(Edit, 0xC6, 0, 0) != 0;
        public int Caret => (int)((nuint)SendMessage(Edit, 0xB0, 0, 0) >> 16);
        public void CollapseSelection() => SendMessage(Edit, 0xB1, 0, 0);
        public nint Edit { get; }
        public string Text
        {
            get
            {
                nint buffer = Marshal.AllocHGlobal(1024);
                try { SendMessage(Edit, 0xD, 512, buffer); return Marshal.PtrToStringUni(buffer) ?? ""; }
                finally { Marshal.FreeHGlobal(buffer); }
            }
        }

        public RichEditFixture(string text, int color, bool undoable, bool mixed, int id)
        {
            parentProcedure = (window, message, wParam, lParam) =>
            {
                if (message == 0x111 && (wParam >> 16) == 0x300 && (wParam & 0xFFFF) == (nuint)id)
                { ChangeSource = lParam; Interlocked.Increment(ref ChangeNotifications); }
                return CallWindowProc(originalParentProcedure, window, message, wParam, lParam);
            };
            procedure = (window, message, wParam, lParam) =>
            {
                nint result = CallWindowProc(originalProcedure, window, message, wParam, lParam);
                if (message == 0xE && Interlocked.Exchange(ref ReplaceAfterLengthRead, null) is string replacement)
                    SetText(window, replacement);
                return result;
            };
            var ready = new TaskCompletionSource<nint>(TaskCreationOptions.RunContinuationsAsynchronously);
            thread = new Thread(() =>
            {
                nint library = 0, parent = 0, edit = 0;
                try
                {
                    library = LoadLibrary("msftedit.dll");
                    if (library == 0) throw new Exception("Cannot load the Windows RichEdit provider.");
                    parent = CreateWindowEx(0, "STATIC", "Synthetic composer fixture", 0x10000000, 0, 0, 1, 1, 0, 0, 0, 0);
                    edit = CreateWindowEx(0, "RICHEDIT50W", "", 0x50000004, 0, 0, 1, 1, parent, id, 0, 0);
                    if (parent == 0 || edit == 0) throw new Exception("Cannot create the synthetic native composer.");
                    SetText(edit, text);
                    Format(edit, color, 4);
                    if (mixed)
                    {
                        SendMessage(edit, 0xB1, 0, 1);
                        Format(edit, 0, 1);
                        SendMessage(edit, 0xB1, 0, 0);
                    }
                    SendMessage(edit, 0xCD, 0, 0);
                    if (undoable)
                    {
                        SendMessage(edit, 0xB1, 0, -1);
                        WithText(text, pointer => SendMessage(edit, 0xC2, 1, pointer));
                    }
                    if ((SendMessage(edit, 0xC6, 0, 0) != 0) != undoable) throw new Exception("Synthetic undo state was not established.");
                    originalProcedure = SetWindowLongPtr(edit, -4, Marshal.GetFunctionPointerForDelegate(procedure));
                    originalParentProcedure = SetWindowLongPtr(parent, -4, Marshal.GetFunctionPointerForDelegate(parentProcedure));
                    SendMessage(edit, 0x445, 0, 1); // Observe actual native EN_CHANGE, not a manually forged notification.
                    ready.SetResult(edit);
                    while (!stop.IsSet)
                    {
                        while (PeekMessage(out var message, 0, 0, 0, 1))
                        {
                            if (message.Window == edit && message.Id == 0x100 && message.WParam == 0x0D) Interlocked.Increment(ref QueuedEnters);
                            TranslateMessage(ref message); DispatchMessage(ref message);
                        }
                        Thread.Sleep(5);
                    }
                }
                catch (Exception ex) { ready.TrySetException(ex); }
                finally
                {
                    if (edit != 0) { if (originalProcedure != 0) SetWindowLongPtr(edit, -4, originalProcedure); DestroyWindow(edit); }
                    if (parent != 0) { if (originalParentProcedure != 0) SetWindowLongPtr(parent, -4, originalParentProcedure); DestroyWindow(parent); }
                    if (library != 0) FreeLibrary(library);
                }
            }) { IsBackground = true };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Edit = ready.Task.GetAwaiter().GetResult();
        }
        private static void SetText(nint edit, string text) => WithText(text, pointer => SendMessage(edit, 0xC, 0, pointer));
        private static void WithText(string text, Action<nint> action)
        {
            var pointer = Marshal.StringToHGlobalUni(text);
            try { action(pointer); } finally { Marshal.FreeHGlobal(pointer); }
        }
        private static void Format(nint edit, int color, nuint scope)
        {
            var format = new CharFormat { Size = (uint)Marshal.SizeOf<CharFormat>(), Mask = 0x40000000, TextColor = color, FaceName = "" };
            var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<CharFormat>());
            try { Marshal.StructureToPtr(format, pointer, false); SendMessage(edit, 0x444, scope, pointer); }
            finally { Marshal.FreeHGlobal(pointer); }
        }
        public void Dispose() { stop.Set(); if (!thread.Join(3000)) throw new Exception("Synthetic composer did not stop."); stop.Dispose(); }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct CharFormat
    {
        public uint Size, Mask, Effects; public int Height, Offset, TextColor; public byte CharacterSet, PitchAndFamily;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string FaceName;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Message { public nint Window; public uint Id; public nuint WParam; public nint LParam; public uint Time; public int X, Y; public uint Private; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint LoadLibrary(string name);
    [DllImport("kernel32.dll")] private static extern bool FreeLibrary(nint library);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint CreateWindowEx(uint exStyle, string className, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint SendMessage(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool PeekMessage(out Message message, nint window, uint min, uint max, uint remove);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint DispatchMessage(ref Message message);
    private delegate nint WndProc(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint CallWindowProc(nint procedure, nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint window, int index, nint value);
}
