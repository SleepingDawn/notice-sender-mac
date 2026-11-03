using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using NoticeSender.Automation;

internal static class NativeRoomOpeningTests
{
    private static string LeaseName => @"Local\NoticeSender.Kakao.RoomOpening." + WindowsIdentity.GetCurrent().User!.Value;
    public static int HoldRoomOpeningLease()
    {
        using var mutex = new Mutex(false, LeaseName);
        if (!mutex.WaitOne(0)) return 2;
        try { Console.WriteLine("held"); Console.Out.Flush(); Console.ReadLine(); return 0; }
        finally { mutex.ReleaseMutex(); }
    }
    public static async Task CrossProcessLease()
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        if (System.IO.Path.GetFileNameWithoutExtension(start.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(typeof(NativeRoomOpeningTests).Assembly.Location);
        start.ArgumentList.Add("--hold-room-opening-lease");
        using var process = Process.Start(start) ?? throw new Exception("Lease child process did not start.");
        try
        {
            if (await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)) != "held") throw new Exception("Lease child process failed to hold its mutex.");
            // Empty names guarantee the red run cannot reach or change the real desktop before the guard exists.
            foreach (var operation in new Func<Task>[] { () => new WindowsKakaoClient().OpenRoomDryRunAsync("", default), () => new WindowsKakaoClient().ResolveAsync("", default) })
            {
                try { await operation(); } catch (InvalidOperationException ex) when (ex.Message.Contains("다른 채팅방 열기 작업", StringComparison.Ordinal)) { continue; }
                catch (Exception ex) { throw new Exception("Cross-process room opening was not blocked before desktop access: " + ex.Message); }
                throw new Exception("Cross-process room opening was not blocked.");
            }
        }
        finally
        {
            process.StandardInput.WriteLine("release");
            if (!process.WaitForExit(5000)) process.Kill();
        }
        if (process.ExitCode != 0) throw new Exception("Lease child did not release cleanly.");
        try { await new WindowsKakaoClient().OpenRoomDryRunAsync("", default); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("정확한 채팅방 이름", StringComparison.Ordinal)) { return; }
        throw new Exception("Room-opening lease was not released.");
    }
    public static Task OwnedUnnamedSearch()
    {
        using var windows = new OwnedSearchWindows();
        var target = WindowsKakaoClient.FindNativeRoomSearch(new HashSet<int> { Environment.ProcessId });
        WindowsKakaoClient.SearchNativeRoom(target, "정확한 가상 채팅방", CancellationToken.None);
        windows.Pump();
        if (windows.Text(windows.Search) != "정확한 가상 채팅방") throw new Exception("The owned unnamed chat search was not queried.");
        if (windows.Text(windows.Composer) != "keep this draft" || windows.Text(windows.OtherEdit) != "keep other search")
            throw new Exception("An unrelated edit or composer was changed.");
        if (windows.Entered != windows.Results || windows.EnterCount != 1) throw new Exception("Enter was not queued only to the visible owned search result list.");
        return Task.CompletedTask;
    }

    public static Task LockedMain()
    {
        using var windows = new OwnedSearchWindows(); windows.AddLockView();
        Reject(() => WindowsKakaoClient.FindNativeRoomSearch(new HashSet<int> { Environment.ProcessId }));
        windows.AssertUntouched(); return Task.CompletedTask;
    }
    public static Task InactiveChatView()
    {
        using var windows = new OwnedSearchWindows(); windows.HideChatView();
        Reject(() => WindowsKakaoClient.FindNativeRoomSearch(new HashSet<int> { Environment.ProcessId }));
        windows.AssertUntouched(); return Task.CompletedTask;
    }
    public static Task VisibleOtherView()
    {
        using var windows = new OwnedSearchWindows(); windows.AddMoreView();
        Reject(() => WindowsKakaoClient.FindNativeRoomSearch(new HashSet<int> { Environment.ProcessId }));
        windows.AssertUntouched(); return Task.CompletedTask;
    }
    public static Task DuplicateEdits()
    {
        using var windows = new OwnedSearchWindows(); windows.DuplicateSearch();
        Reject(() => WindowsKakaoClient.FindNativeRoomSearch(new HashSet<int> { Environment.ProcessId }));
        windows.AssertUntouched(); return Task.CompletedTask;
    }
    public static Task ChangedOwnership()
    {
        using var windows = new OwnedSearchWindows();
        var target = WindowsKakaoClient.FindNativeRoomSearch(new HashSet<int> { Environment.ProcessId });
        windows.RenameChatView();
        Reject(() => WindowsKakaoClient.SearchNativeRoom(target, "never-enter", default));
        windows.AssertUntouched(); return Task.CompletedTask;
    }
    public static Task CancelledSearch()
    {
        using var windows = new OwnedSearchWindows();
        var target = WindowsKakaoClient.FindNativeRoomSearch(new HashSet<int> { Environment.ProcessId });
        using var cts = new CancellationTokenSource(); cts.Cancel(); bool rejected = false;
        try { WindowsKakaoClient.SearchNativeRoom(target, "never-enter", cts.Token); } catch (OperationCanceledException) { rejected = true; }
        if (!rejected) throw new Exception("Cancelled native search was accepted.");
        windows.AssertUntouched(); return Task.CompletedTask;
    }
    public static Task ForeignProcess()
    {
        using var windows = new OwnedSearchWindows();
        Reject(() => WindowsKakaoClient.FindNativeRoomSearch(new HashSet<int> { -1 }));
        windows.AssertUntouched(); return Task.CompletedTask;
    }
    public static Task ChangedQuery()
    {
        using var windows = new OwnedSearchWindows(); windows.TamperSearch = true;
        var target = WindowsKakaoClient.FindNativeRoomSearch(new HashSet<int> { Environment.ProcessId });
        Reject(() => WindowsKakaoClient.SearchNativeRoom(target, "intended room", default));
        windows.Pump();
        if (windows.EnterCount != 0 || windows.Text(windows.Composer) != "keep this draft") throw new Exception("Changed query reached Enter or changed a composer.");
        return Task.CompletedTask;
    }
    public static Task HiddenSearch()
    {
        using var windows = new OwnedSearchWindows(); windows.HideSearchUntilQuery();
        var target = WindowsKakaoClient.FindNativeRoomSearch(new HashSet<int> { Environment.ProcessId });
        WindowsKakaoClient.SearchNativeRoom(target, "owned hidden search", default);
        windows.Pump();
        if (windows.Text(windows.Search) != "owned hidden search" || windows.Entered != windows.Results || windows.Text(windows.Composer) != "keep this draft")
            throw new Exception("The native query did not activate the owned hidden search safely.");
        return Task.CompletedTask;
    }
    public static Task DelayedResults()
    {
        using var host = new SearchHost(w => w.DelayResultAfterFirstConfirmation = true);
        var windows = host.Windows;
        var target = WindowsKakaoClient.FindNativeRoomSearch(new HashSet<int> { Environment.ProcessId });
        WindowsKakaoClient.SearchNativeRoom(target, "delayed exact room", default);
        AwaitRoom(target, "delayed exact room", default);
        if (windows.EnterCount != 2 || windows.Entered != windows.Results || windows.Text(windows.Composer) != "keep this draft")
            throw new Exception("Delayed results were not reconfirmed exactly once on their owned list.");
        return Task.CompletedTask;
    }
    public static Task CappedConfirmations()
    {
        using var host = new SearchHost(_ => { });
        var target = WindowsKakaoClient.FindNativeRoomSearch(new HashSet<int> { Environment.ProcessId });
        WindowsKakaoClient.SearchNativeRoom(target, "never-ready exact room", default);
        Reject(() => AwaitRoom(target, "never-ready exact room", default));
        if (host.Windows.EnterCount != 3 || host.Windows.Text(host.Windows.Composer) != "keep this draft")
            throw new Exception("Room-opening confirmation retries were not capped at three without composer changes.");
        return Task.CompletedTask;
    }
    public static Task CancelDuringReadiness()
    {
        using var cts = new CancellationTokenSource();
        using var host = new SearchHost(w => w.AfterFirstConfirmation = cts.Cancel);
        var target = WindowsKakaoClient.FindNativeRoomSearch(new HashSet<int> { Environment.ProcessId });
        WindowsKakaoClient.SearchNativeRoom(target, "cancelled readiness room", cts.Token);
        bool cancelled = false;
        try { AwaitRoom(target, "cancelled readiness room", cts.Token); } catch (OperationCanceledException) { cancelled = true; }
        if (!cancelled || host.Windows.EnterCount != 1) throw new Exception("Cancellation allowed another result confirmation.");
        return Task.CompletedTask;
    }
    public static Task QueryChangesDuringReadiness()
    {
        using var host = new SearchHost(w => w.AfterFirstConfirmation = () => w.ChangeQuery("different query"));
        var target = WindowsKakaoClient.FindNativeRoomSearch(new HashSet<int> { Environment.ProcessId });
        WindowsKakaoClient.SearchNativeRoom(target, "query changed while waiting", default);
        Reject(() => AwaitRoom(target, "query changed while waiting", default));
        if (host.Windows.EnterCount != 1) throw new Exception("Changed query allowed another result confirmation.");
        return Task.CompletedTask;
    }
    public static Task OwnershipChangesDuringReadiness()
    {
        using var host = new SearchHost(w => w.AfterFirstConfirmation = w.RenameChatView);
        var target = WindowsKakaoClient.FindNativeRoomSearch(new HashSet<int> { Environment.ProcessId });
        WindowsKakaoClient.SearchNativeRoom(target, "ownership changed while waiting", default);
        Reject(() => AwaitRoom(target, "ownership changed while waiting", default));
        if (host.Windows.EnterCount != 1) throw new Exception("Changed ownership allowed another result confirmation.");
        return Task.CompletedTask;
    }
    public static Task ReadOnlyOpeningCapability()
    {
        using var windows = new OwnedSearchWindows(); windows.MakeRoomReadOnly();
        var target = InspectOpenedRoom(windows.Room, "unrelated room");
        if (target.WindowHandle != windows.Room || target.RoomName != "unrelated room" || target.ComposerHandle != 0)
            throw new Exception("A verified read-only room did not preserve its root identity with no composer capability.");
        if (new RoomOpenDryRunResult(target.RoomName, true, target).CanCompose) throw new Exception("A read-only opening was reported as send-capable.");
        return Task.CompletedTask;
    }
    public static Task AmbiguousOpeningCapability()
    {
        using var windows = new OwnedSearchWindows(); windows.AddAnotherComposer();
        Reject(() => InspectOpenedRoom(windows.Room, "unrelated room"));
        if (windows.Text(windows.Composer) != "keep this draft") throw new Exception("Ambiguous composer inspection changed a draft.");
        return Task.CompletedTask;
    }
    public static Task ModelessOwnedMenu()
    {
        using var windows = new OwnedSearchWindows(); windows.CreateOwnedPopup("EVA_Window_Dblclk");
        if (Blocking(windows.Room).Length != 0) throw new Exception("A visible modeless channel menu blocked composer preflight.");
        if (windows.Text(windows.Composer) != "keep this draft") throw new Exception("Popup classification modified the composer.");
        return Task.CompletedTask;
    }
    public static Task DisabledOwnerModal()
    {
        using var windows = new OwnedSearchWindows(); var popup = windows.CreateOwnedPopup("EVA_Window_Dblclk"); windows.DisableRoom();
        if (!Blocking(windows.Room).SequenceEqual(new[] { popup })) throw new Exception("A visible owned modal with a disabled room was not blocked.");
        windows.HidePopup(popup);
        if (Blocking(windows.Room).Length != 0) throw new Exception("A hidden old owned popup was treated as an active modal.");
        return Task.CompletedTask;
    }
    public static Task StandardOwnedDialog()
    {
        using var windows = new OwnedSearchWindows(); var dialog = windows.CreateOwnedPopup("#32770");
        if (!Blocking(windows.Room).SequenceEqual(new[] { dialog })) throw new Exception("A standard owned dialog was not blocked.");
        return Task.CompletedTask;
    }
    public static Task NewAttachmentWindow()
    {
        using var windows = new OwnedSearchWindows(); var menu = windows.CreateOwnedPopup("EVA_Window_Dblclk");
        var picker = windows.CreateOwnedPopup("EVA_Window_Dblclk");
        windows.DisableRoom(); // A new modal may disable the owner; its old modeless channel menu must remain excluded.
        var result = WindowsKakaoClient.AttachmentWindowHandles(windows.Room, Environment.ProcessId, new HashSet<nint> { menu });
        if (!result.SequenceEqual(new[] { picker })) throw new Exception("Persistent channel menu contaminated new attachment-window discovery.");
        return Task.CompletedTask;
    }
    // Added while Windows application control blocks execution. Compile-only until an authorized execution environment is available.
    public static Task OwnedNativeControl()
    {
        using var windows = new OwnedSearchWindows();
        WindowsKakaoClient.RequireNativeControlScope(windows.Room, Environment.ProcessId, windows.Composer, Environment.ProcessId);
        return Task.CompletedTask;
    }
    public static Task RejectVirtualSemanticControl()
    {
        using var windows = new OwnedSearchWindows();
        Reject(() => WindowsKakaoClient.RequireNativeControlScope(windows.Room, Environment.ProcessId, 0, Environment.ProcessId));
        return Task.CompletedTask;
    }
    public static Task RejectForeignSemanticControl()
    {
        using var windows = new OwnedSearchWindows();
        Reject(() => WindowsKakaoClient.RequireNativeControlScope(windows.Room, Environment.ProcessId, windows.Search, Environment.ProcessId));
        Reject(() => WindowsKakaoClient.RequireNativeControlScope(windows.Room, Environment.ProcessId, windows.Composer, -1));
        var menu = windows.CreateOwnedPopup("EVA_Window_Dblclk");
        Reject(() => WindowsKakaoClient.RequireNativeControlScope(windows.Room, Environment.ProcessId, menu, Environment.ProcessId));
        return Task.CompletedTask;
    }
    public static Task RejectReparentedSemanticControl()
    {
        using var windows = new OwnedSearchWindows();
        WindowsKakaoClient.RequireNativeControlScope(windows.Room, Environment.ProcessId, windows.Composer, Environment.ProcessId);
        windows.ReparentComposer(); // Simulates a scope change after the semantic pattern/action was resolved.
        Reject(() => WindowsKakaoClient.RequireNativeControlScope(windows.Room, Environment.ProcessId, windows.Composer, Environment.ProcessId));
        if (windows.Text(windows.Composer) != "keep this draft") throw new Exception("Scope validation changed the draft.");
        return Task.CompletedTask;
    }
    public static Task FilenameEditScope()
    {
        using var windows = new OwnedSearchWindows(); var dialog = windows.CreateOwnedPopup("#32770"); var filename = windows.CreateFilenameEdit(dialog);
        WindowsKakaoClient.RequireNativeControlScope(dialog, Environment.ProcessId, filename, Environment.ProcessId);
        Reject(() => WindowsKakaoClient.RequireNativeControlScope(windows.Room, Environment.ProcessId, filename, Environment.ProcessId));
        return Task.CompletedTask;
    }
    private static nint[] Blocking(nint room)
        => WindowsKakaoClient.BlockingOwnedWindowHandles(room, Environment.ProcessId);
    private static ChatTarget InspectOpenedRoom(nint root, string name)
        => WindowsKakaoClient.InspectOpenedRoom(root, name);
    private static void AwaitRoom(WindowsKakaoClient.NativeRoomSearch target, string room, CancellationToken token)
        => WindowsKakaoClient.WaitForNativeRoom(target, room, token);
    private static void Reject(Action action)
    {
        try { action(); } catch (InvalidOperationException) { return; }
        throw new Exception("An unsafe native room-search target was accepted.");
    }

    private sealed class OwnedSearchWindows : IDisposable
    {
        private const uint Child = 0x50000000;
        private readonly WndProc procedure;
        private readonly List<nint> windows = [];
        private readonly List<string> classes = [];
        public nint Search { get; }
        public nint OtherEdit { get; }
        public nint Composer { get; }
        public nint Room { get; }
        private readonly nint main;
        private readonly nint view;
        public nint Results { get; }
        public bool TamperSearch { get; set; }
        public bool DelayResultAfterFirstConfirmation { get; set; }
        public Action? AfterFirstConfirmation { get; set; }
        private long resultReadyAt = long.MaxValue;
        private bool createdDelayedRoom;
        private bool activateResults;
        public nint Entered { get; private set; }
        public int EnterCount { get; private set; }
        private nint originalEditProc;
        public OwnedSearchWindows()
        {
            procedure = WindowProcedure;
            foreach (var name in new[] { "EVA_Window_Dblclk", "EVA_ChildWindow", "EVA_Window", "EVA_VH_ListControl_Dblclk" })
            {
                var registration = new WindowClass { Procedure = Marshal.GetFunctionPointerForDelegate(procedure), ClassName = name };
                if (RegisterClass(ref registration) == 0) throw new Exception("RegisterClass failed: " + Marshal.GetLastWin32Error());
                classes.Add(name);
            }
            main = Create("EVA_Window_Dblclk", "카카오톡", 0, 0);
            var online = Create("EVA_ChildWindow", "OnlineMainView_synthetic", main, 132);
            view = Create("EVA_Window", "ChatRoomListView_synthetic", online, 1150);
            Search = Create("Edit", "", view, 100);
            originalEditProc = SetWindowLongPtr(Search, -4, Marshal.GetFunctionPointerForDelegate(procedure));
            Results = Create("EVA_VH_ListControl_Dblclk", "SearchListCtrl_synthetic", view, 1003);
            var friendView = Create("EVA_Window", "FriendListView_synthetic", online, 1149);
            OtherEdit = Create("Edit", "keep other search", friendView, 100);
            Room = Create("EVA_Window_Dblclk", "unrelated room", 0, 0);
            Composer = Create("Edit", "keep this draft", Room, 100);
        }
        private nint Create(string name, string title, nint parent, int id)
        {
            var window = CreateWindowEx(0, name, title, parent == 0 ? 0x10000000 : Child, 0, 0, 1, 1, parent, id, 0, 0);
            if (window == 0) throw new Exception("CreateWindowEx failed: " + Marshal.GetLastWin32Error());
            windows.Add(window); return window;
        }
        private nint WindowProcedure(nint window, uint message, nuint wParam, nint lParam)
        {
            if (message == 0x100 && wParam == 0xD)
            {
                Entered = window; EnterCount++;
                if (EnterCount == 1)
                {
                    if (DelayResultAfterFirstConfirmation) resultReadyAt = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 3 / 10;
                    AfterFirstConfirmation?.Invoke();
                }
                else if (window == Results && DelayResultAfterFirstConfirmation && Stopwatch.GetTimestamp() >= resultReadyAt && !createdDelayedRoom)
                {
                    createdDelayedRoom = true;
                    Create("EVA_Window_Dblclk", "delayed exact room", 0, 0);
                }
            }
            if (message == 0x111 && (wParam >> 16) == 0x300 && lParam == Search)
            {
                if (activateResults) { activateResults = false; ShowWindow(Results, 5); }
                if (TamperSearch) { TamperSearch = false; SetWindowText(Search, "changed by user"); }
            }
            return window == Search && originalEditProc != 0 ? CallWindowProc(originalEditProc, window, message, wParam, lParam) : DefWindowProc(window, message, wParam, lParam);
        }
        public string Text(nint window) { var text = new StringBuilder(256); GetWindowText(window, text, text.Capacity); return text.ToString(); }
        public void AddLockView() => Create("EVA_ChildWindow", "LockModeView_synthetic", main, 183);
        public void AddMoreView() => Create("EVA_Window", "MoreView_synthetic", main, 1075);
        public void HideChatView() => ShowWindow(view, 0);
        public void DuplicateSearch() => Create("Edit", "", view, 100);
        public void MakeRoomReadOnly() => DestroyWindow(Composer);
        public void AddAnotherComposer() => Create("Edit", "keep second draft", Room, 101);
        public nint CreateOwnedPopup(string className)
        {
            var popup = CreateWindowEx(0, className, "", 0x90000000, 0, 0, 1, 1, Room, 0, 0, 0);
            if (popup == 0) throw new Exception("Synthetic owned popup creation failed: " + Marshal.GetLastWin32Error());
            windows.Add(popup); return popup;
        }
        public void DisableRoom() => EnableWindow(Room, false);
        public void ReparentComposer() => SetParent(Composer, main);
        public nint CreateFilenameEdit(nint dialog) => Create("Edit", "existing filename", dialog, 1148);
        public void HidePopup(nint popup) => ShowWindow(popup, 0);
        public void RenameChatView() => SetWindowText(view, "FriendListView_synthetic");
        public void ChangeQuery(string value) => SetWindowText(Search, value);
        public void HideSearchUntilQuery() { ShowWindow(Search, 0); EnableWindow(Search, false); ShowWindow(Results, 0); activateResults = true; }
        public void AssertUntouched()
        {
            Pump();
            if (Text(Search) != "" || Text(Composer) != "keep this draft" || Text(OtherEdit) != "keep other search" || EnterCount != 0)
                throw new Exception("A rejected native search changed text or queued Enter.");
        }
        public void Pump() { while (PeekMessage(out var message, 0, 0, 0, 1)) { TranslateMessage(ref message); DispatchMessage(ref message); } }
        public void Dispose()
        {
            if (originalEditProc != 0) SetWindowLongPtr(Search, -4, originalEditProc);
            foreach (var window in windows.AsEnumerable().Reverse()) DestroyWindow(window);
            foreach (var name in classes) UnregisterClass(name, 0);
            GC.KeepAlive(procedure);
        }
    }

    private sealed class SearchHost : IDisposable
    {
        private readonly ManualResetEventSlim stop = new();
        private readonly Thread thread;
        public OwnedSearchWindows Windows { get; }
        public SearchHost(Action<OwnedSearchWindows> configure)
        {
            var ready = new TaskCompletionSource<OwnedSearchWindows>(TaskCreationOptions.RunContinuationsAsynchronously);
            thread = new Thread(() =>
            {
                try
                {
                    using var windows = new OwnedSearchWindows();
                    configure(windows); ready.SetResult(windows);
                    while (!stop.IsSet) { windows.Pump(); Thread.Sleep(5); }
                }
                catch (Exception ex) { ready.TrySetException(ex); }
            }) { IsBackground = true };
            thread.Start(); Windows = ready.Task.GetAwaiter().GetResult();
        }
        public void Dispose() { stop.Set(); if (!thread.Join(3000)) throw new Exception("Synthetic search window thread did not stop."); stop.Dispose(); }
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate nint WndProc(nint window, uint message, nuint wParam, nint lParam);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct WindowClass { public uint Style; public nint Procedure; public int ClassExtra; public int WindowExtra; public nint Instance; public nint Icon; public nint Cursor; public nint Background; public string? Menu; public string ClassName; }
    [StructLayout(LayoutKind.Sequential)] private struct Message { public nint Window; public uint Id; public nuint WParam; public nint LParam; public uint Time; public int X; public int Y; public uint Private; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClass(ref WindowClass windowClass);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool UnregisterClass(string name, nint instance);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateWindowEx(uint exStyle, string name, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint DefWindowProc(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint CallWindowProc(nint procedure, nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint window, int index, nint value);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint window, StringBuilder text, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool SetWindowText(nint window, string text);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint window, int show);
    [DllImport("user32.dll")] private static extern bool EnableWindow(nint window, bool enabled);
    [DllImport("user32.dll")] private static extern nint SetParent(nint child, nint parent);
    [DllImport("user32.dll")] private static extern bool PeekMessage(out Message message, nint window, uint min, uint max, uint remove);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint DispatchMessage(ref Message message);
}
