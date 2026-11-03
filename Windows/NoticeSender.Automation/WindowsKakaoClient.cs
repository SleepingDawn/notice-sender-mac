using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Windows.Automation;

namespace NoticeSender.Automation;

/// <summary>Targeted UIA/Win32 adapter. Composer focus is explicit; it never types into the currently focused window or uses coordinates, clipboard, or global keyboard input.</summary>
public sealed class WindowsKakaoClient : IChatClient, IDryRunChatClient
{
    internal sealed record NativeFileModal(nint Window, nint FileView, nint HiddenImageView);
    internal static NativeFileModal InspectNativeFileModal(ChatTarget target, nint modal, IReadOnlySet<nint> baseline)
    {
        bool Owned(nint window) => Native.IsWindow(window) && Native.Pid(window) == target.ProcessId;
        if (!Owned(target.WindowHandle) || Native.ClassName(target.WindowHandle) != "EVA_Window_Dblclk" || Native.Title(target.WindowHandle) != target.RoomName ||
            !Native.IsWindowVisible(target.WindowHandle) || Native.IsWindowEnabled(target.WindowHandle) || !Owned(target.ComposerHandle) || !Native.IsChild(target.WindowHandle, target.ComposerHandle) ||
            Native.ClassName(target.ComposerHandle) != "RICHEDIT50W" || Native.ControlId(target.ComposerHandle) != 1006)
            throw new InvalidOperationException("합성 파일 확인 대상 채팅방/입력란 상태가 변경되었습니다.");
        if (!Owned(modal) || baseline.Contains(modal) || Native.Owner(modal) != target.WindowHandle || Native.ClassName(modal) != "EVA_Window_Dblclk" || !Native.IsWindowVisible(modal) || !Native.IsWindowEnabled(modal))
            throw new InvalidOperationException("합성 파일 확인의 새 모달 창을 확인할 수 없습니다.");
        var active = OwnedWindowHandles(target.WindowHandle, target.ProcessId).Where(Native.IsWindowEnabled).ToArray();
        if (active.Length != 1 || active[0] != modal) throw new InvalidOperationException("합성 파일 확인 모달 창이 고유하지 않습니다.");
        var children = Native.Children(modal);
        var file = children.Where(w => Native.Parent(w) == modal && Native.ControlId(w) == 1001).ToArray();
        var image = children.Where(w => Native.Parent(w) == modal && Native.ControlId(w) == 1002).ToArray();
        if (!children.All(Owned) || file.Length != 1 || image.Length != 1 || Native.ClassName(file[0]) != "EVA_ChildWindow" || !Native.IsWindowVisible(file[0]) || !Native.IsWindowEnabled(file[0]) ||
            Native.ClassName(image[0]) != "EVA_ChildWindow_Dblclk" || Native.IsWindowVisible(image[0]) || !Native.IsWindowEnabled(image[0]))
            throw new InvalidOperationException("검증한 일반 파일 전송 보기와 다릅니다. 다른 이미지 전송 보기는 지원하지 않습니다.");
        return new(modal, file[0], image[0]);
    }
    private readonly string? reviewedBotCueRoom;
    private readonly SyntheticFileConfirmationPolicy? nativeFileConfirmation;
    /// <param name="reviewedBotCueRoom">Explicit permission to replace the exact observed gray bot cue in this one approved bot room. Null preserves every nonempty draft.</param>
    public WindowsKakaoClient(string? reviewedBotCueRoom = null, SyntheticFileConfirmationPolicy? nativeFileConfirmation = null)
    {
        if (reviewedBotCueRoom != null && reviewedBotCueRoom is not ("카카오톡 선물하기" or "지모" or "KREAM"))
            throw new ArgumentException("안내 문구 교체 허용은 지정된 합성 테스트 봇 방으로 제한됩니다.");
        this.reviewedBotCueRoom = reviewedBotCueRoom;
        if (nativeFileConfirmation != null && reviewedBotCueRoom != null && nativeFileConfirmation.ApprovedBotRoom != reviewedBotCueRoom)
            throw new ArgumentException("합성 파일 시험과 안내 문구 교체의 봇 방이 다릅니다.");
        this.nativeFileConfirmation = nativeFileConfirmation;
    }
    internal sealed record NativeRoomSearch(int ProcessId, nint MainWindow, nint OnlineView, nint ChatView, nint SearchEdit, nint Results);

    /// <summary>Opens and verifies a room without focusing, changing, or sending its composer. Native results with no accessible names are permitted only on this dry-run path.</summary>
    public Task<RoomOpenDryRunResult> OpenRoomDryRunAsync(string exactRoomName, CancellationToken cancellationToken) => OnSta(() =>
    {
        using var lease = AcquireRoomOpeningLease(cancellationToken);
        if (string.IsNullOrWhiteSpace(exactRoomName)) throw new InvalidOperationException("정확한 채팅방 이름이 필요합니다.");
        var rooms = RoomWindows(exactRoomName);
        if (rooms.Length > 1) throw new InvalidOperationException("동일한 이름의 채팅방이 여러 개 열려 있습니다.");
        bool wasAlreadyOpen = rooms.Length == 1;
        if (!wasAlreadyOpen)
        {
            var search = FindNativeRoomSearch(KakaoProcessIds());
            CheckKakaoProcess(search.ProcessId);
            SearchNativeRoom(search, exactRoomName, cancellationToken);
            WaitForNativeRoom(search, exactRoomName, cancellationToken);
            rooms = RoomWindows(exactRoomName);
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (rooms.Length != 1) throw new InvalidOperationException("채팅방 이름이 고유하지 않습니다.");
        var target = InspectOpenedRoom(rooms[0].Current.NativeWindowHandle, exactRoomName);
        ValidateRoomRoot(target);
        if (target.ComposerHandle != 0) Validate(target);
        return new RoomOpenDryRunResult(exactRoomName, wasAlreadyOpen, target);
    }, cancellationToken);

    internal static ChatTarget InspectOpenedRoom(nint windowHandle, string exactRoomName)
    {
        var room = AutomationElement.FromHandle(windowHandle);
        if (room.Current.NativeWindowHandle != windowHandle || room.Current.ProcessId != Native.Pid(windowHandle) || Native.Title(windowHandle) != exactRoomName)
            throw new InvalidOperationException("열린 채팅방 창의 이름 또는 소유권이 변경되었습니다.");
        var composers = ComposerCandidates(room);
        if (composers.Length > 1) throw new InvalidOperationException($"고유한 쓰기 가능 채팅 입력란이 없습니다 (후보 {composers.Length}개).");
        // Notification-only channels can be opened and identified, but may never be treated as send-capable.
        return new ChatTarget(room.Current.ProcessId, windowHandle, exactRoomName, composers.Length == 0 ? 0 : composers[0].Current.NativeWindowHandle);
    }

    internal static NativeRoomSearch FindNativeRoomSearch(IReadOnlySet<int> processIds)
    {
        var searches = new List<NativeRoomSearch>();
        foreach (var main in DiscoverProcessWindows(processIds).Where(w => Native.Title(w) == "카카오톡" && Native.ClassName(w) == "EVA_Window_Dblclk" && Native.IsWindowVisible(w) && Native.IsWindowEnabled(w)))
        {
            int pid = Native.Pid(main);
            var children = Native.Children(main);
            bool Owned(nint child) => Native.Pid(child) == pid && Native.IsChild(main, child);
            if (children.Any(w => Owned(w) && Native.IsWindowVisible(w) && (Native.Title(w).StartsWith("LockModeView_", StringComparison.Ordinal) || Native.Title(w).StartsWith("MoreView_", StringComparison.Ordinal)))) continue;
            foreach (var online in children.Where(w => Owned(w) && Native.Parent(w) == main && Native.ControlId(w) == 132 && Native.ClassName(w) == "EVA_ChildWindow" && Native.Title(w).StartsWith("OnlineMainView_", StringComparison.Ordinal) && Native.IsWindowVisible(w) && Native.IsWindowEnabled(w)))
            foreach (var view in children.Where(w => Owned(w) && Native.Parent(w) == online && Native.ClassName(w) == "EVA_Window" && Native.IsWindowVisible(w) && Native.IsWindowEnabled(w) &&
                ((Native.ControlId(w) == 1150 && Native.Title(w).StartsWith("ChatRoomListView_", StringComparison.Ordinal)) ||
                 (Native.ControlId(w) == 1149 && Native.Title(w).StartsWith("ContactListView_", StringComparison.Ordinal)))))
            {
                // Both observed native views expose their own search. Never query a hidden view or its regular contact/chat list.
                int resultId = Native.ControlId(view) == 1149 ? 101 : 1003;
                var edits = children.Where(w => Owned(w) && Native.Parent(w) == view && Native.ControlId(w) == 100 && Native.ClassName(w) == "Edit").ToArray();
                var results = children.Where(w => Owned(w) && Native.Parent(w) == view && Native.ControlId(w) == resultId && Native.ClassName(w) == "EVA_VH_ListControl_Dblclk" && Native.Title(w).StartsWith("SearchListCtrl_", StringComparison.Ordinal)).ToArray();
                if (edits.Length == 1 && results.Length == 1) searches.Add(new(pid, main, online, view, edits[0], results[0]));
            }
        }
        if (searches.Count != 1) throw new InvalidOperationException("카카오톡 메인 창에 속한 고유한 채팅방 검색 입력란·결과 목록을 확인할 수 없습니다.");
        return searches[0];
    }

    private static void ValidateNativeSearch(NativeRoomSearch search)
    {
        if (FindNativeRoomSearch(new HashSet<int> { search.ProcessId }) != search)
            throw new InvalidOperationException("채팅방 검색 컨트롤 소유권이 변경되었습니다.");
    }

    internal static void SearchNativeRoom(NativeRoomSearch search, string roomName, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ValidateNativeSearch(search);
        // Kakao keeps this exact owned Edit hidden until EN_CHANGE activates search. Do not enable arbitrary edits or touch the composer.
        Native.SetText(search.SearchEdit, roomName);
        ValidateNativeSearch(search);
        if (Native.Text(search.SearchEdit) != roomName) throw new InvalidOperationException("검색어 확인에 실패했습니다.");
        WaitUntil(() => { ValidateNativeSearch(search); return Native.IsWindowVisible(search.Results); }, token, "채팅방 검색 결과 목록이 활성화되지 않았습니다.");
        ConfirmNativeRoomSearch(search, roomName, token);
    }

    internal static void WaitForNativeRoom(NativeRoomSearch search, string roomName, CancellationToken token)
    {
        // List visibility does not mean that a changed query has finished updating. Only the exact opened native title establishes success.
        var elapsed = Stopwatch.StartNew();
        var nextConfirmation = TimeSpan.FromMilliseconds(750);
        int remainingConfirmations = 2;
        var processIds = new HashSet<int> { search.ProcessId };
        while (elapsed.Elapsed < TimeSpan.FromSeconds(5))
        {
            token.ThrowIfCancellationRequested();
            int matches = DiscoverProcessWindows(processIds).Count(w => Native.Title(w) == roomName && Native.ClassName(w) == "EVA_Window_Dblclk");
            if (matches > 1) throw new InvalidOperationException("동일한 이름의 채팅방이 여러 개 열려 있습니다.");
            if (matches == 1) return;
            ValidateNativeSearch(search);
            if (Native.Text(search.SearchEdit) != roomName) throw new InvalidOperationException("검색어가 변경되어 채팅방 열기를 중단했습니다.");
            if (remainingConfirmations > 0 && elapsed.Elapsed >= nextConfirmation)
            {
                // Retry result confirmation only; never repeat message or file delivery. Every attempt revalidates the same owned query/list.
                ConfirmNativeRoomSearch(search, roomName, token);
                remainingConfirmations--;
                nextConfirmation = elapsed.Elapsed + TimeSpan.FromMilliseconds(750);
            }
            Thread.Sleep(50);
        }
        throw new InvalidOperationException("검색 결과에서 정확한 이름의 채팅방이 열리지 않았습니다. 메시지·파일을 전송하지 않았습니다.");
    }

    private static void ConfirmNativeRoomSearch(NativeRoomSearch search, string roomName, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ValidateNativeSearch(search);
        if (Native.Text(search.SearchEdit) != roomName) throw new InvalidOperationException("검색어가 변경되어 채팅방을 열지 않았습니다.");
        if (!Native.IsWindowVisible(search.Results) || !Native.IsWindowEnabled(search.Results))
            throw new InvalidOperationException("채팅방 검색 결과 목록이 비활성화되어 채팅방을 열지 않았습니다.");
        // The search Edit stays disabled in Kakao's native search mode. Queue confirmation only to its visible result list.
        Native.PostSearchEnter(search.Results);
    }
    internal static nint[] DiscoverProcessWindows(IReadOnlySet<int> processIds) => Native.TopLevelWindows().Where(w => processIds.Contains(Native.Pid(w))).ToArray();
    internal static void RequireFocusedEmptyComposer(Action requestFocus, Func<bool> verifiedFocus, Func<string> read, CancellationToken token)
        => RequireFocusedComposer(requestFocus, verifiedFocus, () => read().Length == 0, token);

    private static void RequireFocusedComposer(Action requestFocus, Func<bool> verifiedFocus, Func<bool> acceptable, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        requestFocus();
        WaitUntil(verifiedFocus, token, "채팅 입력란 포커스를 확인할 수 없습니다. 창을 활성화한 후 다시 시도해 주세요.");
        var elapsed = Stopwatch.StartNew();
        do
        {
            token.ThrowIfCancellationRequested();
            if (!verifiedFocus()) throw new InvalidOperationException("채팅 입력란 포커스가 변경되어 전송하지 않았습니다.");
            if (acceptable()) return;
            // Wait for a verified empty/cue state. Inspection never erases a document.
            Thread.Sleep(50);
        } while (elapsed.Elapsed < TimeSpan.FromSeconds(1));
        throw new InvalidOperationException("채팅 입력란에 기존 내용이 있습니다. 내용을 확인해 주세요.");
    }

    internal static void WithExpectedComposerText(Action finalChecks, Func<string> read, string expected, Action mutation, CancellationToken token, string? changedMessage = null)
    {
        token.ThrowIfCancellationRequested();
        finalChecks();
        token.ThrowIfCancellationRequested();
        // Keep this direct native read adjacent to the already-resolved write/invoke. Do not perform UIA lookups after it.
        if (Normalize(read()) != Normalize(expected))
            throw new InvalidOperationException(changedMessage ?? (expected.Length == 0 ? "채팅 입력란에 기존 내용이 있습니다. 내용을 확인해 주세요." : "전송 직전에 입력 내용이 변경되었습니다."));
        token.ThrowIfCancellationRequested();
        mutation();
    }

    private ComposerReplacementPermit? PrepareComposerForSend(ChatTarget target, CancellationToken token)
    {
        ComposerReplacementPermit? permit = null;
        string before = ComposerFocusEvidence(target);
        try
        {
            RequireFocusedComposer(() =>
            {
                Validate(target); token.ThrowIfCancellationRequested();
                if (OwnedDialogs(target).Length != 0) throw new InvalidOperationException("채팅방에 기존 대화상자가 열려 있습니다.");
                if (Native.IsIconic(target.WindowHandle) && !Native.ShowWindowAsync(target.WindowHandle, 9))
                    throw new InvalidOperationException("최소화된 채팅방 창을 복원할 수 없습니다.");
                WaitUntil(() => { Validate(target); return Native.IsWindowVisible(target.WindowHandle) && Native.IsWindowEnabled(target.WindowHandle) && !Native.IsIconic(target.WindowHandle); }, token,
                    "채팅방 창이 표시·활성화되어 있지 않습니다.");
                Validate(target); token.ThrowIfCancellationRequested();
                Native.SetForegroundWindow(target.WindowHandle);
                WaitUntil(() => { Validate(target); return Native.GetForegroundWindow() == target.WindowHandle; }, token,
                    "채팅방 창을 전면으로 가져올 수 없습니다. 창을 활성화한 후 다시 시도해 주세요.");
                token.ThrowIfCancellationRequested();
                UniqueComposer(Validate(target)).SetFocus();
            }, () => HasVerifiedComposerFocus(target), () =>
            {
                var composer = UniqueComposer(Validate(target));
                if (ReadComposer(composer).Length == 0) { permit = null; return true; }
                string? approved = target.RoomName == reviewedBotCueRoom ? CaptureReviewedBotCue(composer) : null;
                permit = approved == null ? null : new ComposerReplacementPermit(target, approved);
                return permit != null;
            }, token);
            return permit;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            throw new InvalidOperationException(ex.Message + Environment.NewLine + "포커스 진단 (내용 제외): 이전 [" + before + "]; 현재 [" + ComposerFocusEvidence(target) + "]", ex);
        }
    }

    private void WithReadyComposer(ChatTarget target, ComposerReplacementPermit? permit, Action additionalChecks, Action mutation, CancellationToken token, bool selectApprovedDocument = false)
    {
        void Checks()
        {
            RequireComposerFocus(target);
            additionalChecks();
            if (selectApprovedDocument) Native.SelectAll(target.ComposerHandle);
            if (permit != null && (permit.Target != target || target.RoomName != reviewedBotCueRoom ||
                CaptureReviewedBotCue(UniqueComposer(Validate(target))) != permit.ExpectedText))
                throw new InvalidOperationException("교체를 허용한 봇 안내 문구의 창 또는 내용/서식이 변경되었습니다.");
            if (selectApprovedDocument) Native.RequireFullSelection(target.ComposerHandle, permit?.ExpectedText.Length ?? 0);
        }
        if (permit == null) WithExpectedComposerText(Checks, () => Native.Text(target.ComposerHandle), "", mutation, token);
        else permit.Use(Checks, () => Native.Text(target.ComposerHandle), mutation, token);
    }

    private bool IsAllowedIdleComposer(ChatTarget target)
    {
        var composer = UniqueComposer(Validate(target));
        return ReadComposer(composer).Length == 0 || target.RoomName == reviewedBotCueRoom && CaptureReviewedBotCue(composer) != null;
    }

    internal sealed class ComposerReplacementPermit(ChatTarget target, string expectedText)
    {
        internal ChatTarget Target { get; } = target;
        internal string ExpectedText { get; } = expectedText;
        private int consumed;
        internal void Use(Action finalChecks, Func<string> read, Action mutation, CancellationToken token)
        {
            if (Volatile.Read(ref consumed) != 0) throw new InvalidOperationException("안내 문구 교체 허용을 다시 사용할 수 없습니다.");
            WithExpectedComposerText(finalChecks, read, ExpectedText, () =>
            {
                if (Interlocked.Exchange(ref consumed, 1) != 0) throw new InvalidOperationException("안내 문구 교체 허용이 이미 사용되었습니다.");
                mutation();
            }, token);
        }
    }

    private static bool HasVerifiedComposerFocus(ChatTarget target)
    {
        var composer = UniqueComposer(Validate(target));
        return Native.HasComposerFocus(target.WindowHandle, target.ComposerHandle) && composer.Current.HasKeyboardFocus && !composer.Current.IsOffscreen;
    }

    private static void RequireComposerFocus(ChatTarget target)
    {
        if (!HasVerifiedComposerFocus(target)) throw new InvalidOperationException("채팅 입력란 포커스가 변경되어 전송하지 않았습니다. " + ComposerFocusEvidence(target));
    }

    private static string ComposerFocusEvidence(ChatTarget target)
    {
        try
        {
            string root = $"room HWND {target.WindowHandle}, foreground HWND {Native.GetForegroundWindow()}, visible {Native.IsWindowVisible(target.WindowHandle)}, enabled {Native.IsWindowEnabled(target.WindowHandle)}, iconic {Native.IsIconic(target.WindowHandle)}";
            if (!Native.IsWindow(target.ComposerHandle) || Native.Pid(target.WindowHandle) != target.ProcessId || Native.Pid(target.ComposerHandle) != target.ProcessId || !Native.IsChild(target.WindowHandle, target.ComposerHandle))
                return root + ", composer ownership unverified";
            var composer = AutomationElement.FromHandle(target.ComposerHandle).Current;
            return root + $", {Native.FocusState(target.ComposerHandle)}, UIA hasKeyboardFocus {composer.HasKeyboardFocus}, keyboardFocusable {composer.IsKeyboardFocusable}, offscreen {composer.IsOffscreen}, WM_GETTEXT length {Native.Text(target.ComposerHandle).Length}, {Native.EditState(target.ComposerHandle)}";
        }
        catch (Exception ex) { return "Focus diagnostic unavailable: " + ex.GetType().Name; }
    }
    public Task<ChatTarget> ResolveAsync(string exactRoomName, CancellationToken cancellationToken) => OnSta(() =>
    {
        using var lease = AcquireRoomOpeningLease(cancellationToken);
        if (string.IsNullOrWhiteSpace(exactRoomName)) throw new InvalidOperationException("정확한 채팅방 이름이 필요합니다.");
        var rooms = RoomWindows(exactRoomName);
        if (rooms.Length > 1) throw new InvalidOperationException("동일한 이름의 채팅방이 여러 개 열려 있습니다.");
        if (rooms.Length == 0)
        {
            // Only search edits and exact invokable results are used; the message composer is never searched into.
            var searches = KakaoWindows().SelectMany(w => Elements(w, ControlType.Edit)
                .Where(e => IsSearchName(e.Current.Name) && Writable(e)).Select(e => (Root: w, Element: e))).ToArray();
            if (searches.Length != 1) throw new InvalidOperationException("채팅방을 먼저 열어 주세요. 고유한 접근성 검색 입력란을 찾을 수 없습니다.");
            var search = searches[0].Element;
            var searchRoot = searches[0].Root;
            int searchPid = searchRoot.Current.ProcessId;
            CheckKakaoProcess(searchPid);
            var searchValue = (ValuePattern)search.GetCurrentPattern(ValuePattern.Pattern);
            RequireOwnedElement(search, searchRoot.Current.NativeWindowHandle, searchPid);
            cancellationToken.ThrowIfCancellationRequested();
            searchValue.SetValue(exactRoomName);
            if (ReadValue(search) != exactRoomName) throw new InvalidOperationException("검색어 확인에 실패했습니다.");
            WaitUntil(() => KakaoWindows().SelectMany(w => Elements(w, ControlType.ListItem))
                .Any(e => e.Current.Name == exactRoomName), cancellationToken, "정확한 이름의 검색 결과가 없습니다.");
            var results = KakaoWindows().SelectMany(w => Elements(w, ControlType.ListItem)
                .Where(e => e.Current.Name == exactRoomName).Select(e => (Root: w, Element: e))).ToArray();
            if (results.Length != 1) throw new InvalidOperationException("정확히 일치하는 검색 결과가 여러 개입니다.");
            var result = results[0].Element;
            var resultRoot = results[0].Root;
            int resultPid = resultRoot.Current.ProcessId;
            CheckKakaoProcess(resultPid);
            var openResult = OwnedInvoker(result, resultRoot.Current.NativeWindowHandle, resultPid);
            RequireOwnedElement(result, resultRoot.Current.NativeWindowHandle, resultPid);
            cancellationToken.ThrowIfCancellationRequested();
            openResult();
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
        var permit = PrepareComposerForSend(target, cancellationToken);
        bool nativeRichEdit = Native.WritableRichEdit(target.ComposerHandle);
        Action write = nativeRichEdit ? () => Native.ReplaceSelection(target.ComposerHandle, text)
            : composer.TryGetCurrentPattern(ValuePattern.Pattern, out var value) && !((ValuePattern)value).Current.IsReadOnly
                ? () => ((ValuePattern)value).SetValue(text) : () => Native.SetText(target.ComposerHandle, text);
        WithReadyComposer(target, permit, () => { }, write, cancellationToken, selectApprovedDocument: nativeRichEdit);
        // Reading the targeted control back is mandatory before invoking send.
        if (Normalize(ReadComposer(UniqueComposer(Validate(target)))) != Normalize(text))
            throw new InvalidOperationException("입력 내용이 원문과 다릅니다. 전송하지 않았습니다.");
        cancellationToken.ThrowIfCancellationRequested();
        room = Validate(target);
        var sendButtons = Elements(room, ControlType.Button).Where(e => e.Current.IsEnabled && e.Current.Name is "전송" or "보내기" or "Send").ToArray();
        if (sendButtons.Length > 1) throw new InvalidOperationException("전송 버튼이 고유하지 않습니다.");
        Action confirm = sendButtons.Length == 1 ? OwnedInvoker(sendButtons[0], target.WindowHandle, target.ProcessId) : () =>
        {
            Native.Key(target.ComposerHandle, 0x100, 0x0D);
            // Keyup is scoped to the same validated HWND, never the focused window.
            Validate(target);
            Native.Key(target.ComposerHandle, 0x101, 0x0D);
        };
        WithExpectedComposerText(() =>
        {
            RequireComposerFocus(target);
            if (sendButtons.Length == 1) RequireOwnedElement(sendButtons[0], target.WindowHandle, target.ProcessId);
        }, () => Native.Text(target.ComposerHandle), text, confirm, cancellationToken);
        WaitUntil(() => { Validate(target); return Native.Text(target.ComposerHandle) != text && IsAllowedIdleComposer(target); }, cancellationToken,
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
        using var fileExperiment = nativeFileConfirmation?.Acquire(target, files);
        bool ownDropIssued = false;
        var room = Validate(target);
        cancellationToken.ThrowIfCancellationRequested();
        var permit = PrepareComposerForSend(target, cancellationToken);
        var ownedBaseline = OwnedWindowHandles(target.WindowHandle, target.ProcessId).ToHashSet();
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
            WithReadyComposer(target, permit, () => fileExperiment?.RequireUnchanged(), () => { ownDropIssued = true; Native.DropFiles(drops[0], files); }, cancellationToken);
        }
        else
        {
        var attach = OwnedInvoker(controls[0], target.WindowHandle, target.ProcessId);
        WithReadyComposer(target, permit, () =>
        {
            RequireComposerFocus(target);
            RequireOwnedElement(controls[0], target.WindowHandle, target.ProcessId);
        }, attach, cancellationToken);
        WaitUntil(() =>
        {
            var current = AttachmentDialogs(target, ownedBaseline);
            if (current.Length > 1) throw new InvalidOperationException("새 첨부 대화상자가 여러 개여서 중단했습니다.");
            return current.Length == 1;
        }, cancellationToken, "파일 선택 대화상자를 확인할 수 없습니다.");
        var dialog = AttachmentDialogs(target, ownedBaseline).Single();
        var edits = Elements(dialog, ControlType.Edit).Where(e =>
            e.Current.AutomationId is "1148" or "FileNameControlHost" || e.Current.Name is "파일 이름:" or "File name:").ToArray();
        if (edits.Length != 1 || !Writable(edits[0])) throw new NotSupportedException("파일 이름 입력란이 고유한 접근성 컨트롤이 아닙니다.");
        var filenames = string.Join(" ", files.Select(p => "\"" + p + "\""));
        var filenameValue = (ValuePattern)edits[0].GetCurrentPattern(ValuePattern.Pattern);
        cancellationToken.ThrowIfCancellationRequested();
        Validate(target); ValidateOwned(target, dialog);
        RequireOwnedElement(edits[0], dialog.Current.NativeWindowHandle, target.ProcessId);
        cancellationToken.ThrowIfCancellationRequested();
        filenameValue.SetValue(filenames);
        if (ReadValue(edits[0]) != filenames) throw new InvalidOperationException("선택한 첨부 경로가 원문과 다릅니다.");
        var open = Elements(dialog, ControlType.Button).Where(e => e.Current.AutomationId == "1" && e.Current.IsEnabled).ToArray();
        if (open.Length != 1) throw new InvalidOperationException("파일 열기 버튼을 확인할 수 없습니다.");
        var chooseFiles = OwnedInvoker(open[0], dialog.Current.NativeWindowHandle, target.ProcessId);
        nint filenameWindow = edits[0].Current.NativeWindowHandle;
        WithExpectedComposerText(() =>
        {
            Validate(target); ValidateOwned(target, dialog);
            RequireOwnedElement(edits[0], dialog.Current.NativeWindowHandle, target.ProcessId);
            if (edits[0].Current.NativeWindowHandle != filenameWindow) throw new InvalidOperationException("파일 이름 입력란 식별자가 변경되었습니다.");
            RequireOwnedElement(open[0], dialog.Current.NativeWindowHandle, target.ProcessId);
        }, () => Native.Text(filenameWindow), filenames, chooseFiles, cancellationToken,
            "선택한 첨부 경로가 변경되어 파일을 열지 않았습니다.");
        }
        // Some versions upload directly; others expose an owned confirmation dialog. Never press an unnamed default button.
        var end = DateTime.UtcNow.AddSeconds(20);
        bool confirmed = false;
        while (DateTime.UtcNow < end)
        {
            cancellationToken.ThrowIfCancellationRequested();
            room = Validate(target);
            var dialogs = AttachmentDialogs(target, ownedBaseline);
            if (dialogs.Length > 1) throw new InvalidOperationException("첨부 대화상자가 여러 개여서 중단했습니다.");
            if (dialogs.Length == 1)
            {
                var confirmation = dialogs[0];
                if (fileExperiment != null && ownDropIssued && !confirmed)
                {
                    nint modal = confirmation.Current.NativeWindowHandle;
                    var pin = InspectNativeFileModal(target, modal, ownedBaseline);
                    confirmed = true; // One attempt, including callback/dispatch failures. Never discover a replacement modal.
                    fileExperiment.Confirm(modal, () =>
                    {
                        ValidateRoomRoot(target);
                        if (InspectNativeFileModal(target, modal, ownedBaseline) != pin || !Native.HasFileModalFocus(modal))
                            throw new InvalidOperationException("합성 파일 확인 창/보기/포커스가 변경되었습니다.");
                    }, () =>
                    {
                        Native.Key(modal, 0x100, 0x0D);
                        if (Native.IsWindow(modal) && Native.Pid(modal) == target.ProcessId && Native.Owner(modal) == target.WindowHandle)
                            Native.Key(modal, 0x101, 0x0D);
                    }, cancellationToken);
                    continue; // Dispatch/modal disappearance never bypasses transcript completion checks below.
                }
                var buttons = Elements(confirmation, ControlType.Button).Where(e => e.Current.IsEnabled && e.Current.Name is "전송" or "보내기" or "Send").ToArray();
                var evidence = AllNames(confirmation);
                if (!confirmed && buttons.Length == 1 && files.All(f => evidence.Contains(Path.GetFileName(f), StringComparison.Ordinal)))
                {
                    var confirmFiles = OwnedInvoker(buttons[0], confirmation.Current.NativeWindowHandle, target.ProcessId);
                    cancellationToken.ThrowIfCancellationRequested();
                    Validate(target); ValidateOwned(target, confirmation);
                    RequireOwnedElement(buttons[0], confirmation.Current.NativeWindowHandle, target.ProcessId);
                    cancellationToken.ThrowIfCancellationRequested(); confirmFiles(); confirmed = true;
                }
            }
            else if (FileEvidence(room, files).Where((count, i) => count > before[i]).Count() == files.Length &&
                     Elements(room, ControlType.ProgressBar).Length == 0)
            {
                // Completion requires newly visible transcript evidence for every filename, plus no modal/progress UI.
                Thread.Sleep(300);
                Validate(target);
                if (AttachmentDialogs(target, ownedBaseline).Length == 0 && FileEvidence(room, files).Where((count, i) => count > before[i]).Count() == files.Length)
                    return true;
            }
            Thread.Sleep(150);
        }
        throw new InvalidOperationException("첨부 전송 완료를 접근성 정보로 확인할 수 없습니다. 채팅방을 열어 두었습니다. 확인 필요 상태를 유지해 주세요.");
    }, cancellationToken);

    public Task CompleteAsync(ChatTarget target, CancellationToken cancellationToken) => OnSta(() =>
    {
        var room = Validate(target);
        if (OwnedDialogs(target).Length > 0 || Elements(room, ControlType.ProgressBar).Length > 0 || !IsAllowedIdleComposer(target))
            throw new InvalidOperationException("미완료 전송 또는 대화상자가 남아 있습니다.");
        return true;
    }, cancellationToken);

    public Task<string> DiagnoseAsync(CancellationToken cancellationToken) => OnSta(() =>
    {
        var pids = KakaoProcessIds();
        var allWindows = Native.TopLevelWindows();
        var windows = allWindows.Where(w => pids.Contains(Native.Pid(w))).ToArray();
        var approvedBotRoots = windows.Where(w => Native.Title(w) is "카카오톡 선물하기" or "지모" or "KREAM").ToArray();
        var lines = new List<string> { $"Current PID {Environment.ProcessId}, session {Process.GetCurrentProcess().SessionId}; foreground HWND {Native.GetForegroundWindow()}; KakaoTalk PIDs [{string.Join(',', pids)}]; native total {allWindows.Length}, Kakao windows {windows.Length}. Read-only inspection; no text entry/file paste/Enter." };
        foreach (var pid in pids)
        {
            using var process = Process.GetProcessById(pid);
            lines.Add($"Kakao PID {pid}, session {process.SessionId}, process MainWindowHandle {process.MainWindowHandle}");
        }
        foreach (var hwnd in windows)
        {
            bool botScope = approvedBotRoots.Any(root => hwnd == root || Native.OwnerChainIncludes(hwnd, root));
            cancellationToken.ThrowIfCancellationRequested();
            lines.Add($"HWND {hwnd}, PID {Native.Pid(hwnd)}, owner {Native.Owner(hwnd)}, title [{Native.Title(hwnd)}], native class {Native.ClassName(hwnd)}, visible {Native.IsWindowVisible(hwnd)}, enabled {Native.IsWindowEnabled(hwnd)}, iconic {Native.IsIconic(hwnd)}, {Native.Styles(hwnd)}, drop targets [{string.Join(',', Native.DropTargets(hwnd))}]");
            foreach (var child in Native.Children(hwnd))
            {
                cancellationToken.ThrowIfCancellationRequested();
                lines.Add($"  native child HWND {child}, PID {Native.Pid(child)}, parent {Native.Parent(child)}, id {Native.ControlId(child)}, class {Native.ClassName(child)}, visible {Native.IsWindowVisible(child)}, enabled {Native.IsWindowEnabled(child)}");
                if (Native.Pid(child) == Native.Pid(hwnd) && Native.ClassName(child).StartsWith("RichEdit", StringComparison.OrdinalIgnoreCase))
                    lines.Add($"    {Native.FocusState(child)}, WM_GETTEXT length {Native.Text(child).Length}, {Native.EditState(child)}");
            }
            try
            {
                if (botScope && approvedBotRoots.Contains(hwnd) && Native.Title(hwnd) is "카카오톡 선물하기" or "지모" or "KREAM")
                {
                    nint transcript = FindNativeTranscript(hwnd, Native.Pid(hwnd));
                    string content = ReadNativeTranscript(hwnd, Native.Pid(hwnd));
                    lines.Add($"  bot native transcript HWND {transcript}, WM_GETTEXT length {content.Length}, content {System.Text.Json.JsonSerializer.Serialize(content)}");
                }
                var window = AutomationElement.FromHandle(hwnd);
                lines.Add($"  UIA role {window.Current.ControlType.ProgrammaticName}, HWND {window.Current.NativeWindowHandle}, name matches native {window.Current.Name == Native.Title(hwnd)}");
                lines.Add(window.TryGetCurrentPattern(WindowPattern.Pattern, out var windowPattern)
                    ? $"  UIA WindowPattern modal {((WindowPattern)windowPattern).Current.IsModal}, interaction {((WindowPattern)windowPattern).Current.WindowInteractionState}"
                    : "  UIA WindowPattern unavailable");
                foreach (var element in window.FindAll(TreeScope.Descendants, Condition.TrueCondition).Cast<AutomationElement>())
                {
                    var current = element.Current;
                    if (botScope)
                        lines.Add($"  bot UIA HWND {current.NativeWindowHandle}, role {current.ControlType.ProgrammaticName}, id {current.AutomationId}, class {current.ClassName}, enabled {current.IsEnabled}, name [{current.Name}], patterns [{string.Join(',', element.GetSupportedPatterns().Select(p => p.ProgrammaticName))}]");
                    if (Native.Title(hwnd) == "카카오톡")
                        lines.Add($"  main UIA HWND {current.NativeWindowHandle}, role {current.ControlType.ProgrammaticName}, id {current.AutomationId}, class {current.ClassName}, enabled {current.IsEnabled}, name [{current.Name}], patterns [{string.Join(',', element.GetSupportedPatterns().Select(p => p.ProgrammaticName))}]");
                    if (current.ControlType != ControlType.Edit && current.ControlType != ControlType.Document && current.AutomationId != "202") continue;
                    int? length = current.NativeWindowHandle != 0 && current.ProcessId == Native.Pid(hwnd) && Native.IsChild(hwnd, current.NativeWindowHandle)
                        ? Native.Text(current.NativeWindowHandle).Length : null;
                    lines.Add($"  control HWND {current.NativeWindowHandle}, role {current.ControlType.ProgrammaticName}, id {current.AutomationId}, class {current.ClassName}, enabled {current.IsEnabled}, hasKeyboardFocus {current.HasKeyboardFocus}, keyboardFocusable {current.IsKeyboardFocusable}, offscreen {current.IsOffscreen}, writableValue {Writable(element)}, writableRichEdit {Native.WritableRichEdit(current.NativeWindowHandle)}, WM_GETTEXT length {length}");
                    if (current.NativeWindowHandle != 0 && current.ClassName.StartsWith("RichEdit", StringComparison.OrdinalIgnoreCase))
                    {
                        lines.Add($"    {Native.EditState(current.NativeWindowHandle)}");
                        if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var richValue))
                            lines.Add($"    UIA ValuePattern length {((ValuePattern)richValue).Current.Value.Length}, readonly {((ValuePattern)richValue).Current.IsReadOnly}");
                        if (element.TryGetCurrentPattern(TextPattern.Pattern, out var richText))
                        {
                            var range = ((TextPattern)richText).DocumentRange;
                            lines.Add($"    UIA TextPattern length {range.GetText(-1).Length}, readonly {range.GetAttributeValue(TextPattern.IsReadOnlyAttribute)}, foreground {range.GetAttributeValue(TextPattern.ForegroundColorAttribute)}, hidden {range.GetAttributeValue(TextPattern.IsHiddenAttribute)}");
                        }
                    }
                }
            }
            catch (Exception ex) { lines.Add($"  UIA/read error {ex.GetType().Name}: {ex.Message}"); }
            if (botScope)
            {
                try { lines.AddRange(MsaaInspection.Read(hwnd, Native.Pid(hwnd))); }
                catch (Exception ex) { lines.Add("  MSAA inspection failed: " + ex.Message); }
            }
        }
        lines.Add("파일 첨부는 접근성 파일 버튼, 소유 대화상자, 각 파일명의 새 대화 기록 및 진행 표시 종료가 모두 확인될 때만 완료됩니다.");
        return string.Join(Environment.NewLine, lines);
    }, cancellationToken);

    private static AutomationElement Validate(ChatTarget target)
    {
        var room = ValidateRoomRoot(target);
        if (!Native.IsWindow(target.ComposerHandle) || Native.Pid(target.ComposerHandle) != target.ProcessId || !Native.IsChild(target.WindowHandle, target.ComposerHandle))
            throw new InvalidOperationException("채팅 입력란이 사라졌거나 다른 창에 속합니다.");
        var composer = UniqueComposer(room);
        if (composer.Current.NativeWindowHandle != target.ComposerHandle) throw new InvalidOperationException("채팅 입력란 식별자가 변경되었습니다.");
        return room;
    }

    private static AutomationElement ValidateRoomRoot(ChatTarget target)
    {
        CheckKakaoProcess(target.ProcessId);
        if (!Native.IsWindow(target.WindowHandle) || Native.Pid(target.WindowHandle) != target.ProcessId || Native.Title(target.WindowHandle) != target.RoomName)
            throw new InvalidOperationException("채팅방 창의 이름 또는 소유 프로세스가 변경되었습니다.");
        if (RoomWindows(target.RoomName).Length != 1) throw new InvalidOperationException("채팅방 이름이 고유하지 않습니다.");
        return AutomationElement.FromHandle(target.WindowHandle);
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
    private static AutomationElement[] RoomWindows(string name) => NamedRoomWindows(DiscoverProcessWindows(KakaoProcessIds()), name);
    // Unrelated popups may close after native enumeration. Resolve UIA only for still-live exact-title candidates;
    // target destruction and duplicate matching rooms retain their existing failure/ambiguity behavior.
    internal static AutomationElement[] NamedRoomWindows(IEnumerable<nint> snapshot, string name) => snapshot
        .Where(w => Native.IsWindow(w) && Native.Title(w) == name).Select(AutomationElement.FromHandle).ToArray();
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
    internal static string ReadComposer(AutomationElement composer) => Native.Text(composer.Current.NativeWindowHandle);

    internal static string? CaptureReviewedBotCue(AutomationElement composer)
    {
        var current = composer.Current;
        nint window = current.NativeWindowHandle;
        string native = Native.Text(window);
        // Kakao 26.9's bot cue is an actual RichEdit document, not an OS cue banner.
        // Shipped IDS_PLUS_FRIEND_100034 and B40 supply these texts and this uniform color.
        // Text alone is never evidence: normal, undoable, mixed-format and unrelated drafts stay protected.
        if (native is not ("챗봇에게 메시지 입력" or "Enter a message to the chatbot" or "チャットボットにメッセージを入力") ||
            current.ClassName != "RICHEDIT50W" || Native.ControlId(window) != 1006 ||
            !composer.TryGetCurrentPattern(TextPattern.Pattern, out var pattern)) return null;
        var range = ((TextPattern)pattern).DocumentRange;
        bool cue = range.GetText(-1) == native &&
            range.GetAttributeValue(TextPattern.ForegroundColorAttribute) is int color && color == 0x949494 &&
            range.GetAttributeValue(TextPattern.IsReadOnlyAttribute) is false && !Native.CanUndo(window);
        // This is an observed content fingerprint, not proof that a document is empty or application-owned.
        // Only an explicit room-scoped replacement policy may turn it into a one-operation permit.
        string fresh = Native.Text(window);
        return cue && fresh == native ? native : null;
    }
    private static string Normalize(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');
    internal static void RequireNativeControlScope(nint expectedRoot, int expectedPid, nint control, int uiaPid)
    {
        if (expectedRoot == 0 || control == 0 || uiaPid != expectedPid || !Native.IsWindow(expectedRoot) || !Native.IsWindow(control) ||
            Native.Pid(expectedRoot) != expectedPid || Native.Pid(control) != expectedPid || !Native.IsChild(expectedRoot, control))
            throw new InvalidOperationException("접근성 컨트롤의 네이티브 창·프로세스·하위 소유권을 확인할 수 없습니다.");
    }

    private static void RequireOwnedElement(AutomationElement element, nint expectedRoot, int expectedPid)
    {
        var current = element.Current;
        RequireNativeControlScope(expectedRoot, expectedPid, current.NativeWindowHandle, current.ProcessId);
        if (!current.IsEnabled || !Native.IsWindowEnabled(current.NativeWindowHandle) || !Native.IsWindowVisible(current.NativeWindowHandle))
            throw new InvalidOperationException("접근성 컨트롤이 표시·활성화되어 있지 않습니다.");
    }

    private static Action OwnedInvoker(AutomationElement element, nint expectedRoot, int expectedPid)
    {
        if (!element.Current.IsEnabled || !element.TryGetCurrentPattern(InvokePattern.Pattern, out var pattern))
            throw new NotSupportedException("접근성 Invoke 기능을 제공하지 않는 컨트롤입니다.");
        // Resolve the semantic action first, then verify its concrete native scope before any caller's final text guard.
        RequireOwnedElement(element, expectedRoot, expectedPid);
        return ((InvokePattern)pattern).Invoke;
    }
    internal static nint[] OwnedWindowHandles(nint owner, int processId) => DiscoverProcessWindows(new HashSet<int> { processId })
        .Where(w => Native.IsWindowVisible(w) && Native.OwnerChainIncludes(w, owner)).ToArray();

    internal static nint[] BlockingOwnedWindowHandles(nint owner, int processId) => OwnedWindowHandles(owner, processId).Where(w =>
        !Native.IsWindowEnabled(owner) || Native.ClassName(w) == "#32770" ||
        (AutomationElement.FromHandle(w).TryGetCurrentPattern(WindowPattern.Pattern, out var modal) && ((WindowPattern)modal).Current.IsModal)).ToArray();

    internal static nint[] AttachmentWindowHandles(nint owner, int processId, IReadOnlySet<nint> visibleBaseline) => OwnedWindowHandles(owner, processId)
        .Where(w => !visibleBaseline.Contains(w)).ToArray();

    private static AutomationElement[] OwnedDialogs(ChatTarget target) => BlockingOwnedWindowHandles(target.WindowHandle, target.ProcessId)
        .Select(AutomationElement.FromHandle).ToArray();

    private static AutomationElement[] AttachmentDialogs(ChatTarget target, IReadOnlySet<nint> baseline) => AttachmentWindowHandles(target.WindowHandle, target.ProcessId, baseline)
        .Select(AutomationElement.FromHandle).ToArray();
    private static void ValidateOwned(ChatTarget target, AutomationElement dialog)
    {
        var hwnd = (nint)dialog.Current.NativeWindowHandle;
        if (dialog.Current.ProcessId != target.ProcessId || !Native.IsWindow(hwnd) || Native.Pid(hwnd) != target.ProcessId || !Native.IsWindowVisible(hwnd) || !Native.OwnerChainIncludes(hwnd, target.WindowHandle))
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
        names.Add(ReadNativeTranscript(room.Current.NativeWindowHandle, room.Current.ProcessId));
        return files.Select(f => names.Sum(n => CountOccurrences(n, Path.GetFileName(f)))).ToArray();
    }
    internal static nint FindNativeTranscript(nint root, int pid)
    {
        if (root == 0 || pid <= 0 || !Native.IsWindow(root) || Native.Pid(root) != pid)
            throw new InvalidOperationException("대화 기록 루트의 네이티브 소유권이 변경되었습니다.");
        var candidates = Native.Children(root).Where(w => Native.Pid(w) == pid && Native.Parent(w) == root &&
            Native.ControlId(w) == 100 && Native.ClassName(w) == "EVA_VH_ListControl_Dblclk").ToArray();
        if (candidates.Length > 1) throw new InvalidOperationException("네이티브 대화 기록 컨트롤이 고유하지 않습니다.");
        return candidates.Length == 0 ? 0 : candidates[0];
    }
    internal static string ReadNativeTranscript(nint root, int pid)
    {
        nint transcript = FindNativeTranscript(root, pid);
        if (transcript == 0) return "";
        RequireNativeControlScope(root, pid, transcript, pid);
        if (Native.Parent(transcript) != root || Native.ControlId(transcript) != 100 || Native.ClassName(transcript) != "EVA_VH_ListControl_Dblclk")
            throw new InvalidOperationException("네이티브 대화 기록 컨트롤의 범위/종류가 변경되었습니다.");
        return Native.Text(transcript);
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

    private static IDisposable AcquireRoomOpeningLease(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        // The native search box is shared by every app/diagnostic window and process in this user's desktop session.
        using var identity = WindowsIdentity.GetCurrent();
        var mutex = new Mutex(false, @"Local\NoticeSender.Kakao.RoomOpening." + (identity.User?.Value ?? throw new InvalidOperationException("현재 사용자 소유권을 확인할 수 없습니다.")));
        try
        {
            if (!mutex.WaitOne(0)) throw new InvalidOperationException("다른 채팅방 열기 작업이 실행 중입니다. 완료 후 다시 시도해 주세요.");
            return new RoomOpeningLease(mutex);
        }
        catch (AbandonedMutexException)
        {
            mutex.ReleaseMutex(); mutex.Dispose();
            throw new InvalidOperationException("이전 채팅방 열기 작업이 중단되었습니다. 카카오톡 상태를 확인한 후 다시 시도해 주세요.");
        }
        catch { mutex.Dispose(); throw; }
    }

    private sealed class RoomOpeningLease(Mutex mutex) : IDisposable
    {
        private Mutex? held = mutex;
        public void Dispose()
        {
            var lease = Interlocked.Exchange(ref held, null);
            if (lease is null) return;
            try { lease.ReleaseMutex(); } finally { lease.Dispose(); }
        }
    }

    private static class Native
    {
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool IsWindow(nint hWnd);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool IsWindowVisible(nint hWnd);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool IsWindowEnabled(nint hWnd);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool IsIconic(nint hWnd);
        [DllImport("user32.dll")] public static extern nint GetForegroundWindow();
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool SetForegroundWindow(nint window);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool ShowWindowAsync(nint window, int show);
        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo info);
        [DllImport("user32.dll", EntryPoint = "GetParent")] public static extern nint Parent(nint hWnd);
        [DllImport("user32.dll", EntryPoint = "GetDlgCtrlID")] public static extern int ControlId(nint hWnd);
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
        [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct GuiThreadInfo { public int Size; public uint Flags; public nint Active; public nint Focus; public nint Capture; public nint MenuOwner; public nint MoveSize; public nint Caret; public NativeRect CaretRect; }
        public static string FocusState(nint window)
        {
            uint thread = GetWindowThreadProcessId(window, out _);
            var info = new GuiThreadInfo { Size = Marshal.SizeOf<GuiThreadInfo>() };
            return GetGUIThreadInfo(thread, ref info)
                ? $"owner thread {thread}, active HWND {info.Active}, focus HWND {info.Focus}, caret HWND {info.Caret}, foreground HWND {GetForegroundWindow()}"
                : $"owner thread {thread}, GetGUIThreadInfo failed Win32 {Marshal.GetLastWin32Error()}";
        }
        public static bool HasComposerFocus(nint room, nint composer)
        {
            if (!IsWindow(room) || !IsWindow(composer) || Pid(room) != Pid(composer) || !IsChild(room, composer) || GetForegroundWindow() != room ||
                !IsWindowVisible(room) || !IsWindowEnabled(room) || IsIconic(room) || !IsWindowVisible(composer) || !IsWindowEnabled(composer)) return false;
            uint thread = GetWindowThreadProcessId(composer, out _);
            var info = new GuiThreadInfo { Size = Marshal.SizeOf<GuiThreadInfo>() };
            return GetGUIThreadInfo(thread, ref info) && info.Active == room && info.Focus == composer;
        }
        public static bool HasFileModalFocus(nint modal)
        {
            if (!IsWindow(modal) || !IsWindowVisible(modal) || !IsWindowEnabled(modal) || GetForegroundWindow() != modal) return false;
            uint thread = GetWindowThreadProcessId(modal, out _);
            var info = new GuiThreadInfo { Size = Marshal.SizeOf<GuiThreadInfo>() };
            return GetGUIThreadInfo(thread, ref info) && info.Active == modal && (info.Focus == modal || IsChild(modal, info.Focus));
        }
        public static string Title(nint window) { var b = new StringBuilder(4096); GetWindowText(window, b, b.Capacity); return b.ToString(); }
        public static string ClassName(nint window) { var b = new StringBuilder(256); GetClassName(window, b, b.Capacity); return b.ToString(); }
        public static nint Owner(nint window) => GetWindow(window, 4);
        public static string Styles(nint window) => $"style 0x{GetWindowLong(window, -16):X8}, exStyle 0x{GetWindowLong(window, -20):X8}";
        public static string EditState(nint window) => $"EM_GETMODIFY {Message(window, 0xB8, 0, 0) != 0}, EM_GETSEL packed {Message(window, 0xB0, 0, 0)}, EM_CANUNDO {Message(window, 0xC6, 0, 0) != 0}, EM_GETEVENTMASK 0x{Message(window, 0x43B, 0, 0):X}, WM_GETDLGCODE 0x{Message(window, 0x87, 0, 0):X}, async modifiers Shift {GetAsyncKeyState(0x10) < 0}, Ctrl {GetAsyncKeyState(0x11) < 0}, Alt {GetAsyncKeyState(0x12) < 0}";
        public static bool CanUndo(nint window) => Message(window, 0xC6, 0, 0) != 0;
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
        public static nint[] Children(nint root)
        {
            var windows = new List<nint>();
            EnumChildWindows(root, (window, _) => { windows.Add(window); return true; }, 0);
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
            var memory = Marshal.StringToHGlobalUni(text);
            try { Message(window, 0xC, 0, memory); }
            finally { Marshal.FreeHGlobal(memory); }
        }
        public static void ReplaceSelection(nint window, string text)
        {
            // The caller already resolved this exact native operation before its final raw read.
            var memory = Marshal.StringToHGlobalUni(text);
            try { Message(window, 0xC2, 1, memory); }
            finally { Marshal.FreeHGlobal(memory); }
        }
        public static void SelectAll(nint window) => Message(window, 0xB1, 0, -1);
        public static void RequireFullSelection(nint window, int approvedLength)
        {
            // Nonempty approval is restricted to the short single-line bot cue; other drafts are rejected.
            // RICHEDIT50W's Select All includes the mandatory final paragraph marker omitted by WM_GETTEXT.
            if (approvedLength >= ushort.MaxValue || Message(window, 0xB0, 0, 0) != (nuint)(approvedLength + 1) << 16)
                throw new InvalidOperationException("교체할 입력 범위가 승인한 문서 전체와 다릅니다.");
        }
        public static string Text(nint window)
        {
            int length = checked((int)Message(window, 0xE, 0, 0));
            if (length > 1_000_000) throw new InvalidOperationException("입력 내용이 너무 깁니다.");
            int capacity = length + 2; // One sentinel character detects growth after WM_GETTEXTLENGTH.
            var memory = Marshal.AllocHGlobal(capacity * 2);
            try
            {
                int copied = checked((int)Message(window, 0xD, (nuint)capacity, memory));
                if (copied >= capacity - 1) throw new InvalidOperationException("입력 내용을 읽는 동안 길이가 증가했습니다. 초안을 변경하지 않았습니다.");
                return Marshal.PtrToStringUni(memory, copied) ?? "";
            }
            finally { Marshal.FreeHGlobal(memory); }
        }
        public static void Key(nint window, uint message, int key)
        {
            // Kakao's preprocessing runs on queued messages; SendMessage bypasses that path.
            nint flags = key == 0x0D ? 0x001C0001 : 1;
            if (message == 0x101) flags |= unchecked((nint)0xC0000000);
            if (!PostMessage(window, message, (nuint)key, flags))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "검증한 채팅 입력란에 키 요청을 전달하지 못했습니다.");
        }
        public static void PostSearchEnter(nint window)
        {
            if (!PostMessage(window, 0x100, 0xD, 0x001C0001) || !PostMessage(window, 0x101, 0xD, unchecked((nint)0xC01C0001)))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "채팅방 검색 확인 요청에 실패했습니다.");
        }
    }
}
