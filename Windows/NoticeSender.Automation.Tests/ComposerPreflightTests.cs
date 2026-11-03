using NoticeSender.Automation;

internal static class ComposerPreflightTests
{
    public static Task VerifyBeforeRead()
    {
        bool requested = false; int checks = 0; int reads = 0;
        Prepare(() => requested = true, () => requested && ++checks >= 2, () =>
        {
            if (!requested || checks < 2) throw new Exception("Draft read happened before focus was verified.");
            reads++; return "";
        });
        if (reads != 1) throw new Exception("Verified empty composer was not read exactly once.");
        return Task.CompletedTask;
    }
    public static Task DeniedFocus()
    {
        int reads = 0; bool rejected = false;
        try { Prepare(() => { }, () => false, () => { reads++; return ""; }); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("포커스", StringComparison.Ordinal)) { rejected = true; }
        if (!rejected || reads != 0) throw new Exception("Denied focus allowed a draft read or was not reported clearly.");
        return Task.CompletedTask;
    }
    public static Task DelayedCueClear()
    {
        int reads = 0;
        Prepare(() => { }, () => true, () => ++reads < 3 ? "application-owned cue still clearing" : "");
        if (reads != 3) throw new Exception("Preflight did not wait for the application's own empty state.");
        return Task.CompletedTask;
    }
    public static Task PreserveDraft()
    {
        const string draft = "arbitrary existing user draft"; bool rejected = false; int reads = 0;
        try { Prepare(() => { }, () => true, () => { reads++; return draft; }); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("기존 내용", StringComparison.Ordinal)) { rejected = true; }
        if (!rejected || reads == 0) throw new Exception("A remaining draft was accepted.");
        return Task.CompletedTask;
    }
    public static Task FocusLoss()
    {
        int reads = 0; bool rejected = false;
        try { Prepare(() => { }, () => reads == 0, () => { reads++; return "still nonempty"; }); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("포커스", StringComparison.Ordinal)) { rejected = true; }
        if (!rejected || reads != 1) throw new Exception("Focus loss allowed another draft read.");
        return Task.CompletedTask;
    }
    public static Task Cancellation()
    {
        using var cts = new CancellationTokenSource(); int reads = 0; bool cancelled = false;
        try { Prepare(() => { }, () => { cts.Cancel(); return false; }, () => { reads++; return ""; }, cts.Token); }
        catch (OperationCanceledException) { cancelled = true; }
        if (!cancelled || reads != 0) throw new Exception("Cancelled focus verification read the composer.");
        return Task.CompletedTask;
    }
    public static Task FreshDraftBeforeWrite()
    {
        string composer = ""; bool mutated = false;
        Prepare(() => { }, () => true, () => composer);
        bool rejected = false;
        try { Guard(() => composer = "fresh user draft", () => composer, "", () => { mutated = true; composer = "outgoing"; }); }
        catch (InvalidOperationException) { rejected = true; }
        if (!rejected || mutated || composer != "fresh user draft") throw new Exception("A fresh draft arriving after preflight was overwritten.");
        return Task.CompletedTask;
    }
    public static Task EditedTextBeforeSend()
    {
        string composer = "reviewed outgoing text"; bool sent = false; bool rejected = false;
        try { Guard(() => composer = "edited by user after preview", () => composer, "reviewed outgoing text", () => sent = true); }
        catch (InvalidOperationException) { rejected = true; }
        if (!rejected || sent || composer != "edited by user after preview") throw new Exception("An edit arriving after preview was sent.");
        return Task.CompletedTask;
    }
    public static Task FreshDraftBeforeAttachment()
    {
        string composer = ""; bool attached = false; bool rejected = false;
        try { Guard(() => composer = "draft during attachment enumeration", () => composer, "", () => attached = true); }
        catch (InvalidOperationException) { rejected = true; }
        if (!rejected || attached || composer != "draft during attachment enumeration") throw new Exception("A fresh draft did not block attachment invocation/drop.");
        return Task.CompletedTask;
    }
    public static Task CancelDuringFinalChecks()
    {
        using var cts = new CancellationTokenSource(); int reads = 0; bool mutated = false; bool cancelled = false;
        try { Guard(cts.Cancel, () => { reads++; return ""; }, "", () => mutated = true, cts.Token); }
        catch (OperationCanceledException) { cancelled = true; }
        if (!cancelled || reads != 0 || mutated) throw new Exception("Cancellation during final checks permitted read/mutation.");
        return Task.CompletedTask;
    }
    // Compile-only locally while Smart App Control blocks these assemblies; execute in authorized Windows CI.
    public static Task CancelDuringNativeRead()
    {
        using var cts = new CancellationTokenSource(); bool mutated = false; bool cancelled = false;
        try { Guard(() => { }, () => { cts.Cancel(); return "reviewed"; }, "reviewed", () => mutated = true, cts.Token); }
        catch (OperationCanceledException) { cancelled = true; }
        if (!cancelled || mutated) throw new Exception("Cancellation during a successful native read still allowed mutation.");
        return Task.CompletedTask;
    }
    public static Task FilenameChangesBeforeOpen()
    {
        string filename = "\"C:\\reviewed.txt\""; bool opened = false; bool rejected = false;
        try
        {
            WindowsKakaoClient.WithExpectedComposerText(() => filename = "\"C:\\unreviewed.txt\"", () => filename,
                "\"C:\\reviewed.txt\"", () => opened = true, default, "선택한 첨부 경로가 변경되어 파일을 열지 않았습니다.");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("첨부 경로", StringComparison.Ordinal)) { rejected = true; }
        if (!rejected || opened || filename != "\"C:\\unreviewed.txt\"") throw new Exception("A changed filename was opened or its existing text was changed.");
        return Task.CompletedTask;
    }
    private static void Guard(Action finalChecks, Func<string> read, string expected, Action mutation, CancellationToken token = default)
        => WindowsKakaoClient.WithExpectedComposerText(finalChecks, read, expected, mutation, token);
    private static void Prepare(Action requestFocus, Func<bool> verifiedFocus, Func<string> read, CancellationToken token = default)
        => WindowsKakaoClient.RequireFocusedEmptyComposer(requestFocus, verifiedFocus, read, token);
}
