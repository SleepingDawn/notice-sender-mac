using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

namespace NoticeSender.Automation;

/// <summary>Targeted UIA/Win32 adapter. Composer focus is explicit; it never types into the currently focused window or uses coordinates, clipboard, or global keyboard input.</summary>
public sealed class WindowsKakaoClient : IChatClient
{
    internal static nint[] DiscoverProcessWindows(IReadOnlySet<int> processIds) => Native.TopLevelWindows().Where(w => processIds.Contains(Native.Pid(w))).ToArray();
    internal static void RequireEmptyAfterFocus(Action focus, Func<string> read)
    {
        // Let the application remove its own cue on focus. Never erase or special-case arbitrary text.
        focus();
        if (read().Length != 0) throw new InvalidOperationException("채팅 입력란에 기존 내용이 있습니다. 내용을 확인해 주세요.");
    }
    public Task<ChatTarget> ResolveAsync(string exactRoomName, CancellationToken cancellationToken) => OnSta(() =>
    {
        if (string.IsNullOrWhiteSpace(exactRoomName)) throw new InvalidOperationException("정확한 채팅방 이름이 필요합니다.");
        var rooms = RoomWindows(exactRoomName);
        if (rooms.Length > 1) throw new InvalidOperationException("동일한 이름의 채팅방이 여러 개 열려 있습니다.");
        if (rooms.Length == 0)
        {
            // Only search edits and exact invokable results are used; the message composer is never searched into.
            var searches = KakaoWindows().SelectMany(w => Elements(w, ControlType.Edit)
                .Where(e => IsSearchName(e.Current.Name) && Writable(e))).ToArray();
            if (searches.Length != 1) throw new InvalidOperationException("채팅방을 먼저 열어 주세요. 고유한 접근성 검색 입력란을 찾을 수 없습니다.");
            var search = searches[0];
            CheckKakaoProcess(search.Current.ProcessId);
            cancellationToken.ThrowIfCancellationRequested();
            ((ValuePattern)search.GetCurrentPattern(ValuePattern.Pattern)).SetValue(exactRoomName);
            if (ReadValue(search) != exactRoomName) throw new InvalidOperationException("검색어 확인에 실패했습니다.");
            WaitUntil(() => KakaoWindows().SelectMany(w => Elements(w, ControlType.ListItem))
                .Any(e => e.Current.Name == exactRoomName), cancellationToken, "정확한 이름의 검색 결과가 없습니다.");
            var results = KakaoWindows().SelectMany(w => Elements(w, ControlType.ListItem))
                .Where(e => e.Current.Name == exactRoomName).ToArray();
            if (results.Length != 1) throw new InvalidOperationException("정확히 일치하는 검색 결과가 여러 개입니다.");
            CheckKakaoProcess(results[0].Current.ProcessId);
            cancellationToken.ThrowIfCancellationRequested();
            Invoke(results[0]);
            WaitUntil(() => RoomWindows(exactRoomName).Length > 0, cancellationToken, "검색 결과에서 채팅방이 열리지 않았습니다.");
            rooms = RoomWindows(exactRoomName);
        }
        if (rooms.Length != 1) throw new InvalidOperationException("채팅방 이름이 고유하지 않습니다.");
        var room = rooms[0];
        var composer = UniqueComposer(room);
        var target = new ChatTarget(room.Current.ProcessId, room.Current.NativeWindowHandle, exactRoomName, composer.Current.NativeWindowHandle);
        Validate(target);
        return target;
    }, cancellationToken);

    public Task SendTextAsync(ChatTarget target, string text, CancellationToken cancellationToken) => OnSta(() =>
    {
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("빈 메시지는 전송할 수 없습니다.");
        var room = Validate(target);
        var composer = UniqueComposer(room);
        cancellationToken.ThrowIfCancellationRequested();
        RequireEmptyAfterFocus(() => { Validate(target); cancellationToken.ThrowIfCancellationRequested(); composer.SetFocus(); },
            () => ReadComposer(UniqueComposer(Validate(target))));
        cancellationToken.ThrowIfCancellationRequested();
        Validate(target);
        cancellationToken.ThrowIfCancellationRequested();
        if (composer.TryGetCurrentPattern(ValuePattern.Pattern, out var value) && !((ValuePattern)value).Current.IsReadOnly)
            ((ValuePattern)value).SetValue(text);
        else Native.SetText(target.ComposerHandle, text);
        // Reading the targeted control back is mandatory before invoking send.
        if (Normalize(ReadComposer(UniqueComposer(Validate(target)))) != Normalize(text))
            throw new InvalidOperationException("입력 내용이 원문과 다릅니다. 전송하지 않았습니다.");
        cancellationToken.ThrowIfCancellationRequested();
        room = Validate(target);
        if (Normalize(ReadComposer(UniqueComposer(room))) != Normalize(text))
            throw new InvalidOperationException("전송 직전에 입력 내용이 변경되었습니다.");
        cancellationToken.ThrowIfCancellationRequested();
        var sendButtons = Elements(room, ControlType.Button).Where(e => e.Current.IsEnabled && e.Current.Name is "전송" or "보내기" or "Send").ToArray();
        if (sendButtons.Length > 1) throw new InvalidOperationException("전송 버튼이 고유하지 않습니다.");
        if (sendButtons.Length == 1) Invoke(sendButtons[0]);
        else
        {
            Native.Key(target.ComposerHandle, 0x100, 0x0D);
            // Keyup is scoped to the same validated HWND, never the focused window.
            Validate(target);
            Native.Key(target.ComposerHandle, 0x101, 0x0D);
        }
        WaitUntil(() => ReadComposer(UniqueComposer(Validate(target))).Length == 0, cancellationToken,
            "메시지 전송 후 입력란이 비워지지 않았습니다. 중복 전송하지 말고 채팅방을 확인해 주세요.");
        return true;
    }, cancellationToken);

    public Task SendFilesAsync(ChatTarget target, IReadOnlyList<string> paths, CancellationToken cancellationToken) => OnSta(() =>
    {
        if (paths.Count == 0) return true;
        var files = paths.Select(Path.GetFullPath).ToArray();
        if (files.Any(p => !File.Exists(p) || p.Contains('"'))) throw new InvalidOperationException("첨부 파일이 없거나 파일 경로가 올바르지 않습니다.");
        if (files.Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Length) throw new InvalidOperationException("중복 첨부 파일입니다.");
        if (files.Select(Path.GetFileName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Length)
            throw new InvalidOperationException("동일한 파일 이름은 전송 완료를 구별할 수 없습니다.");
        var room = Validate(target);
        cancellationToken.ThrowIfCancellationRequested();
        RequireEmptyAfterFocus(() => { Validate(target); cancellationToken.ThrowIfCancellationRequested(); UniqueComposer(room).SetFocus(); },
            () => ReadComposer(UniqueComposer(Validate(target))));
        var before = FileEvidence(room, files);
        var controls = Elements(room, ControlType.Button).Where(e => e.Current.IsEnabled &&
            e.Current.Name is "파일 보내기" or "파일 첨부" or "파일" or "Send file" or "Attach file").ToArray();
        if (controls.Length > 1) throw new InvalidOperationException("파일 첨부 버튼이 여러 개입니다.");
        if (OwnedDialogs(target).Length != 0) throw new InvalidOperationException("기존 대화상자가 열려 있습니다.");
        cancellationToken.ThrowIfCancellationRequested();
        Validate(target);
        if (controls.Length == 0)
        {
            // A registered, uniquely owned native drop target avoids any shared clipboard state.
            var drops = Native.DropTargets(target.WindowHandle).ToArray();
            if (drops.Length != 1) throw new NotSupportedException("접근성 파일 버튼 또는 고유한 등록 파일 드롭 대상이 없습니다. 파일을 보내지 않았습니다.");
            if (Native.Pid(drops[0]) != target.ProcessId || (drops[0] != target.WindowHandle && !Native.IsChild(target.WindowHandle, drops[0])))
                throw new InvalidOperationException("파일 드롭 대상의 소유권이 다릅니다.");
            cancellationToken.ThrowIfCancellationRequested();
            Validate(target); Native.DropFiles(drops[0], files);
        }
        else
        {
        Invoke(controls[0]);
        WaitUntil(() => OwnedDialogs(target).Length == 1, cancellationToken, "파일 선택 대화상자를 확인할 수 없습니다.");
        var dialog = OwnedDialogs(target).Single();
        var edits = Elements(dialog, ControlType.Edit).Where(e =>
            e.Current.AutomationId is "1148" or "FileNameControlHost" || e.Current.Name is "파일 이름:" or "File name:").ToArray();
        if (edits.Length != 1 || !Writable(edits[0])) throw new NotSupportedException("파일 이름 입력란이 고유한 접근성 컨트롤이 아닙니다.");
        var filenames = string.Join(" ", files.Select(p => "\"" + p + "\""));
        cancellationToken.ThrowIfCancellationRequested();
        Validate(target); ValidateOwned(target, dialog);
        ((ValuePattern)edits[0].GetCurrentPattern(ValuePattern.Pattern)).SetValue(filenames);
        if (ReadValue(edits[0]) != filenames) throw new InvalidOperationException("선택한 첨부 경로가 원문과 다릅니다.");
        var open = Elements(dialog, ControlType.Button).Where(e => e.Current.AutomationId == "1" && e.Current.IsEnabled).ToArray();
        if (open.Length != 1) throw new InvalidOperationException("파일 열기 버튼을 확인할 수 없습니다.");
        cancellationToken.ThrowIfCancellationRequested();
        Validate(target); ValidateOwned(target, dialog); Invoke(open[0]);
        }
        // Some versions upload directly; others expose an owned confirmation dialog. Never press an unnamed default button.
        var end = DateTime.UtcNow.AddSeconds(20);
        bool confirmed = false;
        while (DateTime.UtcNow < end)
        {
            cancellationToken.ThrowIfCancellationRequested();
            room = Validate(target);
            var dialogs = OwnedDialogs(target);
            if (dialogs.Length > 1) throw new InvalidOperationException("첨부 대화상자가 여러 개여서 중단했습니다.");
            if (dialogs.Length == 1)
            {
                var confirmation = dialogs[0];
                var buttons = Elements(confirmation, ControlType.Button).Where(e => e.Current.IsEnabled && e.Current.Name is "전송" or "보내기" or "Send").ToArray();
                var evidence = AllNames(confirmation);
                if (!confirmed && buttons.Length == 1 && files.All(f => evidence.Contains(Path.GetFileName(f), StringComparison.Ordinal)))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Validate(target); ValidateOwned(target, confirmation); Invoke(buttons[0]); confirmed = true;
                }
            }
            else if (FileEvidence(room, files).Where((count, i) => count > before[i]).Count() == files.Length &&
                     Elements(room, ControlType.ProgressBar).Length == 0)
            {
                // Completion requires newly visible transcript evidence for every filename, plus no modal/progress UI.
                Thread.Sleep(300);
                Validate(target);
                if (OwnedDialogs(target).Length == 0 && FileEvidence(room, files).Where((count, i) => count > before[i]).Count() == files.Length)
                    return true;
            }
            Thread.Sleep(150);
        }
        throw new InvalidOperationException("첨부 전송 완료를 접근성 정보로 확인할 수 없습니다. 채팅방을 열어 두었습니다. 확인 필요 상태를 유지해 주세요.");
    }, cancellationToken);

    public Task CompleteAsync(ChatTarget target, CancellationToken cancellationToken) => OnSta(() =>
    {
        var room = Validate(target);
        if (OwnedDialogs(target).Length > 0 || Elements(room, ControlType.ProgressBar).Length > 0 || ReadComposer(UniqueComposer(room)).Length > 0)
            throw new InvalidOperationException("미완료 전송 또는 대화상자가 남아 있습니다.");
        return true;
    }, cancellationToken);

    public Task<string> DiagnoseAsync(CancellationToken cancellationToken) => OnSta(() =>
    {
        var pids = KakaoProcessIds();
        var allWindows = Native.TopLevelWindows();
        var windows = allWindows.Where(w => pids.Contains(Native.Pid(w))).ToArray();
        var lines = new List<string> { $"Current PID {Environment.ProcessId}, session {Process.GetCurrentProcess().SessionId}; KakaoTalk PIDs [{string.Join(',', pids)}]; native total {allWindows.Length}, Kakao windows {windows.Length}. Read-only inspection; no text entry/file paste/Enter." };
        foreach (var pid in pids)
        {
            using var process = Process.GetProcessById(pid);
            lines.Add($"Kakao PID {pid}, session {process.SessionId}, process MainWindowHandle {process.MainWindowHandle}");
        }
        foreach (var hwnd in windows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lines.Add($"HWND {hwnd}, PID {Native.Pid(hwnd)}, title [{Native.Title(hwnd)}], native class {Native.ClassName(hwnd)}, visible {Native.IsWindowVisible(hwnd)}, drop targets [{string.Join(',', Native.DropTargets(hwnd))}]");
            try
            {
                var window = AutomationElement.FromHandle(hwnd);
                lines.Add($"  UIA role {window.Current.ControlType.ProgrammaticName}, HWND {window.Current.NativeWindowHandle}, name matches native {window.Current.Name == Native.Title(hwnd)}");
                foreach (var element in window.FindAll(TreeScope.Descendants, Condition.TrueCondition).Cast<AutomationElement>())
                {
                    var current = element.Current;
                    if (current.ControlType != ControlType.Edit && current.ControlType != ControlType.Document && current.AutomationId != "202") continue;
                    int? length = current.NativeWindowHandle != 0 && current.ProcessId == Native.Pid(hwnd) && Native.IsChild(hwnd, current.NativeWindowHandle)
                        ? Native.Text(current.NativeWindowHandle).Length : null;
                    lines.Add($"  control HWND {current.NativeWindowHandle}, role {current.ControlType.ProgrammaticName}, id {current.AutomationId}, class {current.ClassName}, enabled {current.IsEnabled}, writableValue {Writable(element)}, writableRichEdit {Native.WritableRichEdit(current.NativeWindowHandle)}, WM_GETTEXT length {length}");
                    if (current.NativeWindowHandle != 0 && current.ClassName.StartsWith("RichEdit", StringComparison.OrdinalIgnoreCase))
                        lines.Add($"    {Native.EditState(current.NativeWindowHandle)}");
                }
            }
            catch (Exception ex) { lines.Add($"  UIA/read error {ex.GetType().Name}: {ex.Message}"); }
        }
        lines.Add("파일 첨부는 접근성 파일 버튼, 소유 대화상자, 각 파일명의 새 대화 기록 및 진행 표시 종료가 모두 확인될 때만 완료됩니다.");
        return string.Join(Environment.NewLine, lines);
    }, cancellationToken);

    private static AutomationElement Validate(ChatTarget target)
    {
        CheckKakaoProcess(target.ProcessId);
        if (!Native.IsWindow(target.WindowHandle) || Native.Pid(target.WindowHandle) != target.ProcessId || Native.Title(target.WindowHandle) != target.RoomName)
            throw new InvalidOperationException("채팅방 창의 이름 또는 소유 프로세스가 변경되었습니다.");
        if (RoomWindows(target.RoomName).Length != 1) throw new InvalidOperationException("채팅방 이름이 고유하지 않습니다.");
        if (!Native.IsWindow(target.ComposerHandle) || Native.Pid(target.ComposerHandle) != target.ProcessId || !Native.IsChild(target.WindowHandle, target.ComposerHandle))
            throw new InvalidOperationException("채팅 입력란이 사라졌거나 다른 창에 속합니다.");
        var room = AutomationElement.FromHandle(target.WindowHandle);
        var composer = UniqueComposer(room);
        if (composer.Current.NativeWindowHandle != target.ComposerHandle) throw new InvalidOperationException("채팅 입력란 식별자가 변경되었습니다.");
        return room;
    }

    private static HashSet<int> KakaoProcessIds()
    {
        var ids = new HashSet<int>();
        foreach (var process in Process.GetProcessesByName("KakaoTalk"))
            using (process) { try { ids.Add(process.Id); } catch (InvalidOperationException) { } }
        return ids;
    }
    private static AutomationElement[] KakaoWindows() => DiscoverProcessWindows(KakaoProcessIds())
        .Select(AutomationElement.FromHandle).ToArray();
    private static AutomationElement[] RoomWindows(string name) => KakaoWindows().Where(e =>
        Native.Title(e.Current.NativeWindowHandle) == name).ToArray();
    private static bool IsKakao(int id) { try { using var p = Process.GetProcessById(id); return p.ProcessName.Equals("KakaoTalk", StringComparison.OrdinalIgnoreCase); } catch { return false; } }
    private static void CheckKakaoProcess(int id) { if (!IsKakao(id)) throw new InvalidOperationException("카카오톡 프로세스 소유권을 확인할 수 없습니다."); }
    private static bool IsSearchName(string name) => name is "검색" or "Search" or "친구, 채팅방 검색" or "채팅방 검색";
    private static AutomationElement[] Elements(AutomationElement root, ControlType type) => root.FindAll(TreeScope.Descendants,
        new PropertyCondition(AutomationElement.ControlTypeProperty, type)).Cast<AutomationElement>().ToArray();
    private static bool Writable(AutomationElement element) => element.Current.IsEnabled &&
        element.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern) && !((ValuePattern)pattern).Current.IsReadOnly;
    private static AutomationElement[] ComposerCandidates(AutomationElement room) => room.FindAll(TreeScope.Descendants,
        new OrCondition(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document))).Cast<AutomationElement>()
        .Where(e => e.Current.ProcessId == room.Current.ProcessId && e.Current.NativeWindowHandle != 0 && e.Current.IsEnabled && !IsSearchName(e.Current.Name) &&
            (Writable(e) || Native.WritableRichEdit(e.Current.NativeWindowHandle))).ToArray();
    private static AutomationElement UniqueComposer(AutomationElement room)
    {
        var candidates = ComposerCandidates(room);
        if (candidates.Length != 1) throw new InvalidOperationException($"고유한 쓰기 가능 채팅 입력란이 없습니다 (후보 {candidates.Length}개).");
        return candidates[0];
    }
    private static string ReadValue(AutomationElement element) => ((ValuePattern)element.GetCurrentPattern(ValuePattern.Pattern)).Current.Value;
    private static string ReadComposer(AutomationElement composer) => Native.Text(composer.Current.NativeWindowHandle);
    private static string Normalize(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');
    private static void Invoke(AutomationElement element)
    {
        if (!element.Current.IsEnabled || !element.TryGetCurrentPattern(InvokePattern.Pattern, out var pattern))
            throw new NotSupportedException("접근성 Invoke 기능을 제공하지 않는 컨트롤입니다.");
        ((InvokePattern)pattern).Invoke();
    }
    private static AutomationElement[] OwnedDialogs(ChatTarget target) => KakaoWindows().Where(e =>
        e.Current.ProcessId == target.ProcessId && Native.OwnerChainIncludes(e.Current.NativeWindowHandle, target.WindowHandle)).ToArray();
    private static void ValidateOwned(ChatTarget target, AutomationElement dialog)
    {
        if (dialog.Current.ProcessId != target.ProcessId || !Native.OwnerChainIncludes(dialog.Current.NativeWindowHandle, target.WindowHandle))
            throw new InvalidOperationException("파일 대화상자의 소유권이 변경되었습니다.");
    }
    private static string AllNames(AutomationElement root) => string.Join("\n", root.FindAll(TreeScope.Descendants, Condition.TrueCondition)
        .Cast<AutomationElement>().Where(e => e.Current.ControlType != ControlType.Edit).Select(e => e.Current.Name));
    private static int[] FileEvidence(AutomationElement room, string[] files)
    {
        // Restrict evidence to transcript containers, excluding composer and arbitrary preview labels.
        var transcripts = room.FindAll(TreeScope.Descendants,
            new OrCondition(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.List),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document)))
            .Cast<AutomationElement>().Where(e => !Writable(e) &&
                (e.Current.NativeWindowHandle == 0 || !Native.WritableRichEdit(e.Current.NativeWindowHandle))).ToArray();
        var names = transcripts.SelectMany(t => t.FindAll(TreeScope.Descendants, Condition.TrueCondition).Cast<AutomationElement>())
            .Where(e => e.Current.ControlType is var t && (t == ControlType.Text || t == ControlType.ListItem || t == ControlType.Hyperlink))
            .Select(e => e.Current.Name).ToList();
        foreach (var transcript in transcripts)
            if (transcript.TryGetCurrentPattern(TextPattern.Pattern, out var text)) names.Add(((TextPattern)text).DocumentRange.GetText(-1));
        // KakaoTalk's owned transcript custom window may expose text through WM_GETTEXT even without UIA children.
        foreach (var transcript in room.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "202")).Cast<AutomationElement>())
        {
            var hwnd = (nint)transcript.Current.NativeWindowHandle;
            if (hwnd != 0 && transcript.Current.ProcessId == room.Current.ProcessId && Native.IsChild(room.Current.NativeWindowHandle, hwnd))
                names.Add(Native.Text(hwnd));
        }
        return files.Select(f => names.Sum(n => CountOccurrences(n, Path.GetFileName(f)))).ToArray();
    }
    private static int CountOccurrences(string text, string value)
    {
        int count = 0, start = 0;
        while ((start = text.IndexOf(value, start, StringComparison.Ordinal)) >= 0) { count++; start += value.Length; }
        return count;
    }
    private static void WaitUntil(Func<bool> predicate, CancellationToken token, string failure)
    {
        var end = DateTime.UtcNow.AddSeconds(5);
        do { token.ThrowIfCancellationRequested(); if (predicate()) return; Thread.Sleep(100); } while (DateTime.UtcNow < end);
        throw new InvalidOperationException(failure);
    }
    private static Task<T> OnSta<T>(Func<T> operation, CancellationToken token)
    {
        var source = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => { try { token.ThrowIfCancellationRequested(); source.SetResult(operation()); } catch (OperationCanceledException ex) { source.SetCanceled(ex.CancellationToken); } catch (Exception ex) { source.SetException(ex); } }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return source.Task;
    }

    private static class Native
    {
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool IsWindow(nint hWnd);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool IsWindowVisible(nint hWnd);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool IsChild(nint parent, nint child);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out int process);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint window, StringBuilder text, int count);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, StringBuilder text, int count);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(nint window, int index);
        [DllImport("user32.dll")] private static extern nint GetWindow(nint window, uint command);
        private delegate bool EnumWindowCallback(nint window, nint parameter);
        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumWindows(EnumWindowCallback callback, nint parameter);
        [DllImport("user32.dll")] private static extern bool EnumChildWindows(nint parent, EnumWindowCallback callback, nint parameter);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool PostMessage(nint window, uint message, nuint wParam, nint lParam);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern nint GlobalAlloc(uint flags, nuint bytes);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern nint GlobalLock(nint memory);
        [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(nint memory);
        [DllImport("kernel32.dll")] private static extern nint GlobalFree(nint memory);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint SendMessageTimeout(nint window, uint message, nuint wParam, nint lParam, uint flags, uint timeout, out nuint result);
        public static int Pid(nint window) { GetWindowThreadProcessId(window, out int id); return id; }
        public static string Title(nint window) { var b = new StringBuilder(4096); GetWindowText(window, b, b.Capacity); return b.ToString(); }
        public static string ClassName(nint window) { var b = new StringBuilder(256); GetClassName(window, b, b.Capacity); return b.ToString(); }
        public static string EditState(nint window) => $"EM_GETMODIFY {Message(window, 0xB8, 0, 0) != 0}, EM_GETSEL packed {Message(window, 0xB0, 0, 0)}";
        public static nint[] TopLevelWindows()
        {
            var windows = new List<nint>();
            Marshal.SetLastPInvokeError(0);
            if (!EnumWindows((window, _) => { windows.Add(window); return true; }, 0))
            {
                int error = Marshal.GetLastPInvokeError();
                throw new Win32Exception(error, $"EnumWindows failed (Win32 {error}, collected {windows.Count} windows, PID {Environment.ProcessId}, session {Process.GetCurrentProcess().SessionId}).");
            }
            return windows.ToArray();
        }
        public static bool WritableRichEdit(nint window)
        {
            var b = new StringBuilder(256); GetClassName(window, b, b.Capacity);
            int style = GetWindowLong(window, -16);
            return b.ToString().StartsWith("RichEdit", StringComparison.OrdinalIgnoreCase) && (style & 0x800) == 0 && (style & 4) != 0;
        }
        public static bool OwnerChainIncludes(nint window, nint owner)
        {
            for (int i = 0; i < 16 && window != 0; i++) { window = GetWindow(window, 4); if (window == owner) return true; }
            return false;
        }
        public static IEnumerable<nint> DropTargets(nint root)
        {
            var windows = new List<nint> { root };
            EnumChildWindows(root, (window, _) => { windows.Add(window); return true; }, 0);
            return windows.Where(w => (GetWindowLong(w, -20) & 0x10) != 0).ToArray();
        }
        public static void DropFiles(nint window, string[] files)
        {
            var payload = Encoding.Unicode.GetBytes(string.Join('\0', files) + "\0\0");
            var memory = GlobalAlloc(0x42, (nuint)(20 + payload.Length));
            if (memory == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            bool transferred = false;
            try
            {
                var pointer = GlobalLock(memory);
                if (pointer == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
                try { Marshal.WriteInt32(pointer, 0, 20); Marshal.WriteInt32(pointer, 16, 1); Marshal.Copy(payload, 0, pointer + 20, payload.Length); }
                finally { GlobalUnlock(memory); }
                if (!PostMessage(window, 0x233, (nuint)memory, 0)) throw new Win32Exception(Marshal.GetLastWin32Error(), "파일 드롭 요청에 실패했습니다.");
                transferred = true; // WM_DROPFILES receiver owns HDROP and releases it with DragFinish.
            }
            finally { if (!transferred) GlobalFree(memory); }
        }
        private static nuint Message(nint window, uint message, nuint wParam, nint lParam)
        {
            if (SendMessageTimeout(window, message, wParam, lParam, 2, 3000, out var result) == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "대상 컨트롤 응답 시간 초과");
            return result;
        }
        public static void SetText(nint window, string text)
        {
            var memory = Marshal.StringToHGlobalUni(text); try { Message(window, 0xC, 0, memory); } finally { Marshal.FreeHGlobal(memory); }
        }
        public static string Text(nint window)
        {
            int length = checked((int)Message(window, 0xE, 0, 0));
            if (length > 1_000_000) throw new InvalidOperationException("입력 내용이 너무 깁니다.");
            var memory = Marshal.AllocHGlobal((length + 1) * 2);
            try { Message(window, 0xD, (nuint)(length + 1), memory); return Marshal.PtrToStringUni(memory) ?? ""; }
            finally { Marshal.FreeHGlobal(memory); }
        }
        public static void Key(nint window, uint message, int key) => Message(window, message, (nuint)key, message == 0x101 ? unchecked((nint)0xC0000001) : 1);
    }
}
