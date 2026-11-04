using System.Reflection;
using NoticeSender.Automation;

internal static class AttachmentCompletionGateTests
{
    private const string Stamp = "20261104-180000-001-1234abcd";
    private static readonly string First = "NoticeSender-SYNTHETIC-" + Stamp + "-C-first.txt";
    private static readonly string Second = "NoticeSender-SYNTHETIC-" + Stamp + "-C-second.csv";
    private static readonly DateTimeOffset Start = new(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);
    private static Type Type(string name) => typeof(IChatClient).Assembly.GetType("NoticeSender.Automation." + name)
        ?? throw new Exception(name + " does not exist: fresh correlated attachment completion is not gated.");
    private static object Scope(int process = 17, nint root = 21, nint composer = 22, nint transcript = 23, string batch = "reviewed-bytes-signature") =>
        Activator.CreateInstance(Type("AttachmentCompletionScope"), process, root, composer, transcript, batch)!;
    private static Array Expected(params (string Name, long Length)[] files)
    {
        var type = Type("AttachmentExpectedFile"); var result = Array.CreateInstance(type, files.Length);
        for (int i = 0; i < files.Length; i++) result.SetValue(Activator.CreateInstance(type, files[i].Name, files[i].Length), i);
        return result;
    }
    private static object Observation(object scope, long sequence, int milliseconds, params (string Name, long Length, int Count)[] counts)
    {
        var type = Type("AttachmentReceiptCount"); var receipts = Array.CreateInstance(type, counts.Length);
        for (int i = 0; i < counts.Length; i++) receipts.SetValue(Activator.CreateInstance(type, counts[i].Name, counts[i].Length, counts[i].Count), i);
        return Activator.CreateInstance(Type("AttachmentCompletionObservation"), scope, sequence, Start.AddMilliseconds(milliseconds), receipts)!;
    }
    private static object Gate(object? scope = null, Array? expected = null, object? baseline = null)
    {
        scope ??= Scope(); expected ??= Expected((First, 4), (Second, 5)); baseline ??= Observation(scope, 0, 0);
        return Unwrap(() => Activator.CreateInstance(Type("AttachmentCompletionGate"), scope, expected, baseline)!);
    }
    private static object Unwrap(Func<object> work)
    {
        try { return work(); }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }
    private static bool Observe(object gate, object observation, CancellationToken token = default) => (bool)Unwrap(() => gate.GetType().GetMethod("Observe")!.Invoke(gate, new[] { observation, (object)token })!);
    private static object Complete(object scope, long sequence, int milliseconds) => Observation(scope, sequence, milliseconds, (First, 4, 1), (Second, 5, 1));
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception ex) when (ex is InvalidOperationException or OperationCanceledException or ArgumentException) { return; }
        throw new Exception("Unsafe attachment completion proof was accepted.");
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }

    public static Task StableFreshEveryFile()
    {
        var scope = Scope(); var gate = Gate(scope);
        Check(!Observe(gate, Observation(scope, 1, 10, (First, 4, 1))), "Partial attachment set completed.");
        Check(!Observe(gate, Complete(scope, 2, 100)), "One complete capture established completion.");
        Check(!Observe(gate, Complete(scope, 3, 399)), "Completion did not require 300ms stability.");
        Check(Observe(gate, Complete(scope, 4, 400)), "Two fresh complete observations 300ms apart did not complete.");
        string korean = "NoticeSender-SYNTHETIC-" + Stamp + "-E-한글자료.txt";
        var koreanGate = Gate(scope, Expected((korean, 123)));
        Check(!Observe(koreanGate, Observation(scope, 1, 10, (korean.Normalize(System.Text.NormalizationForm.FormD), 123, 1))), "A non-exact decomposed Korean filename counted as a receipt.");
        Check(!Observe(koreanGate, Observation(scope, 2, 100, (korean, 123, 1))), "One Korean filename capture completed.");
        Check(Observe(koreanGate, Observation(scope, 3, 400, (korean, 123, 1))), "Exact stable Korean filename receipts did not complete.");
        var wrongSize = Gate(scope, Expected((korean, 123)));
        Reject(() => Observe(wrongSize, Observation(scope, 1, 100, (korean, 122, 1))));
        return Task.CompletedTask;
    }
    public static Task RejectScopeChangesAndStaleCaptures()
    {
        var scope = Scope();
        foreach (var changed in new[] { Scope(process: 18), Scope(root: 31), Scope(composer: 32), Scope(transcript: 33), Scope(batch: "other bytes") })
        {
            var gate = Gate(scope); Reject(() => Observe(gate, Complete(changed, 1, 100)));
            Reject(() => Observe(gate, Complete(scope, 2, 500))); // An invalid observation cannot be repaired by a later good frame.
        }
        foreach (var next in new[] { (Sequence: 1L, At: 500), (Sequence: 2L, At: 100), (Sequence: 2L, At: 99) })
        {
            var gate = Gate(scope); Observe(gate, Complete(scope, 1, 100));
            Reject(() => Observe(gate, Complete(scope, next.Sequence, next.At)));
        }
        return Task.CompletedTask;
    }
    public static Task RejectDuplicateDropAndWrongSize()
    {
        var scope = Scope();
        foreach (var bad in new[] {
            Observation(scope, 2, 500, (First, 4, 1), (First, 4, 1), (Second, 5, 1)),
            Observation(scope, 2, 500, (First, 4, 2), (Second, 5, 1)),
            Observation(scope, 2, 500, (First, 4, 0), (Second, 5, 1)),
            Observation(scope, 2, 500, (First, 99, 1), (Second, 5, 1)),
            Observation(scope, 2, 500, (First, 4, -1), (Second, 5, 1)) })
        {
            var gate = Gate(scope); Observe(gate, Complete(scope, 1, 100));
            Reject(() => Observe(gate, bad)); Reject(() => Observe(gate, Complete(scope, 3, 900)));
        }
        return Task.CompletedTask;
    }
    public static Task BaselineAndSyntheticIdentityGuards()
    {
        var scope = Scope();
        Reject(() => Gate(scope, baseline: Observation(scope, 0, 0, (First, 4, 1))));
        Reject(() => Gate(scope, baseline: Observation(Scope(root: 999), 0, 0)));
        Reject(() => Gate(scope, Expected(("ordinary-file.pdf", 4))));
        Reject(() => Gate(scope, Expected((First, 4), (First, 5))));
        Reject(() => Gate(scope, Expected((First, 4), (Second.Replace("1234abcd", "abcdef01", StringComparison.Ordinal), 5))));
        Reject(() => Gate(scope, Expected((First.Replace("C-first.txt", "unexpected.exe", StringComparison.Ordinal), 4))));
        Reject(() => Gate(scope, Expected((First, -1))));
        Reject(() => Gate(scope, Expected(("NoticeSender-SYNTHETIC-" + Stamp + "-E-한글자료2.txt", 4))));
        Reject(() => Gate(scope, Expected(("NoticeSender-SYNTHETIC-" + Stamp + "-E-한글자료.TXT", 4))));
        return Task.CompletedTask;
    }
    public static Task CancellationAndInputCollectionOwnership()
    {
        var scope = Scope(); var files = Expected((First, 4), (Second, 5)); var gate = Gate(scope, files);
        files.SetValue(Activator.CreateInstance(Type("AttachmentExpectedFile"), "mutated.txt", 4L), 0);
        Check(!Observe(gate, Complete(scope, 1, 100)) && Observe(gate, Complete(scope, 2, 400)), "External collection edits changed the frozen expected file set.");
        var cancelled = Gate(scope); using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Reject(() => Observe(cancelled, Complete(scope, 1, 100), cancel.Token));
        Reject(() => Observe(cancelled, Complete(scope, 2, 500)));
        var observer = Type("IAttachmentCompletionObserver"); var session = Type("IAttachmentCompletionSession");
        Check(observer.IsInterface && session.IsInterface && typeof(IDisposable).IsAssignableFrom(session), "Optional observer/session contracts are absent.");
        Check(observer.GetMethod("Begin")?.ReturnType == session && session.GetMethod("Observe")?.ReturnType == typeof(bool), "Completion observer contract changed.");
        return Task.CompletedTask;
    }
}
