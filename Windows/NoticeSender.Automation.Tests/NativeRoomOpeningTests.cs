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
        private readonly nint main;
        private readonly nint view;
        public nint Results { get; }
        public bool TamperSearch { get; set; }
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
            var room = Create("EVA_Window_Dblclk", "unrelated room", 0, 0);
            Composer = Create("Edit", "keep this draft", room, 100);
        }
        private nint Create(string name, string title, nint parent, int id)
        {
            var window = CreateWindowEx(0, name, title, parent == 0 ? 0x10000000 : Child, 0, 0, 1, 1, parent, id, 0, 0);
            if (window == 0) throw new Exception("CreateWindowEx failed: " + Marshal.GetLastWin32Error());
            windows.Add(window); return window;
        }
        private nint WindowProcedure(nint window, uint message, nuint wParam, nint lParam)
        {
            if (message == 0x100 && wParam == 0xD) { Entered = window; EnterCount++; }
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
        public void RenameChatView() => SetWindowText(view, "FriendListView_synthetic");
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
    [DllImport("user32.dll")] private static extern bool PeekMessage(out Message message, nint window, uint min, uint max, uint remove);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint DispatchMessage(ref Message message);
}
