using NoticeSender.Automation;
using NoticeSender.Core;

internal static class LegacyAttachmentEvidenceTests
{
    public static Task ManualReviewNeverReadsOrAcceptsFilenameMentions()
    {
        string path = Path.Combine(Path.GetTempPath(), "NoticeSender-legacy-evidence-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(path, "Synthetic unit-test bytes; no sends.");
        try
        {
            using var approved = ReviewedAttachmentBatch.Capture(new[] { new BatchItem { ChatRoomName = "ordinary reviewed room", AttachmentPaths = [path] } }).OpenVerifiedFiles();
            var reviewer = new NeverReviewer();
            var client = new WindowsKakaoClient(nativeFileConfirmation: new ReviewedFileConfirmationPolicy(approved, (_, _, _) => { }), manualAttachmentReview: reviewer);
            RequireBlocked(client, "manual review");
            if (reviewer.Calls != 0) throw new Exception("An evidence decision invoked user review without a submitted attachment.");
        }
        finally { File.Delete(path); }
        return Task.CompletedTask;
    }
    public static Task StrictObserverCannotFallBackToFilenameMentions()
    {
        RequireBlocked(new WindowsKakaoClient(attachmentCompletion: new StrictObserver()), "strict observer");
        return Task.CompletedTask;
    }
    private static void RequireBlocked(WindowsKakaoClient client, string mode)
    {
        int baselineReads = 0, receiptReads = 0;
        var baseline = client.ReadLegacyAttachmentBaseline(() => { baselineReads++; return new[] { 0 }; });
        // Mere transcript text can increase a filename count twice without containing any attachment card.
        bool first = client.AcceptsLegacyAttachmentEvidence(() => { receiptReads++; return true; });
        bool second = client.AcceptsLegacyAttachmentEvidence(() => { receiptReads++; return true; });
        if (baselineReads != 0 || receiptReads != 0 || baseline.Length != 0 || first || second)
            throw new Exception(mode + " read or accepted filename-only evidence instead of its required completion method.");
    }
    public static Task ExistingLegacyModeRetainsItsEvidenceResult()
    {
        var client = new WindowsKakaoClient(); int reads = 0;
        int[] baseline = client.ReadLegacyAttachmentBaseline(() => { reads++; return new[] { 7 }; });
        bool notComplete = client.AcceptsLegacyAttachmentEvidence(() => { reads++; return false; });
        bool complete = client.AcceptsLegacyAttachmentEvidence(() => { reads++; return true; });
        if (reads != 3 || baseline.Single() != 7 || notComplete || !complete) throw new Exception("Unconfigured legacy mode changed its evidence behavior.");
        return Task.CompletedTask;
    }
    private sealed class NeverReviewer : IManualAttachmentReview
    {
        internal int Calls;
        public AttachmentCompletionOutcome? Review(ManualAttachmentRequest request, CancellationToken token) { Calls++; throw new Exception("No submission occurred in this fixture."); }
    }
    private sealed class StrictObserver : IAttachmentCompletionObserver
    {
        public IAttachmentCompletionSession Begin(ChatTarget target, IReadOnlyList<string> files, CancellationToken token) => throw new Exception("This fixture inspects configuration only.");
    }
}
