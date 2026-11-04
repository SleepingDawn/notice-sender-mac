namespace NoticeSender.Automation;

/// <summary>One native confirmation attempt per room in the explicitly approved, externally owned attachment batch.</summary>
public sealed class ReviewedFileConfirmationPolicy : NativeFileConfirmationPolicy
{
    private readonly ApprovedAttachmentBatch batch;
    private readonly Action<ChatTarget, nint, IReadOnlyList<string>> capture;
    private readonly HashSet<string> attemptedRooms = new(StringComparer.Ordinal);
    private readonly object gate = new();

    public ReviewedFileConfirmationPolicy(ApprovedAttachmentBatch batch, Action<ChatTarget, nint, IReadOnlyList<string>> requiredCapture)
    { this.batch = batch ?? throw new ArgumentNullException(nameof(batch)); capture = requiredCapture ?? throw new ArgumentNullException(nameof(requiredCapture)); }

    public override bool IsRoomApproved(string roomName) => batch.Items.Any(item => item.RoomName == roomName && item.Files.Count > 0);

    internal override Lease Acquire(ChatTarget target, IReadOnlyList<string> files) => Create(target, files, LastInputStamp, verifyExecutable: true);

    // Fixture seam: production always calls Acquire and verifies the pinned executable.
    internal Lease Freeze(ChatTarget target, IReadOnlyList<string> files, Func<uint> readInput) => Create(target, files, readInput, verifyExecutable: false);

    private Lease Create(ChatTarget target, IReadOnlyList<string> files, Func<uint> readInput, bool verifyExecutable)
    {
        ArgumentNullException.ThrowIfNull(target);
        lock (gate)
        {
            Require(IsRoomApproved(target.RoomName), "최종 검토에서 승인한 첨부 채팅방이 아닙니다.");
            Require(attemptedRooms.Add(target.RoomName), "이 채팅방의 승인한 파일 확인을 재시도할 수 없습니다.");
        }
        // An attempt is consumed even when manifest, binary, input or later audit checks fail.
        var item = batch.RequireMatch(target, files);
        if (verifyExecutable) VerifyExecutable(target);
        string[] paths = item.Files.Select(file => file.Path).ToArray();
        return new(target, paths, readInput, capture, () => batch.RequireMatch(target, paths));
    }
}
