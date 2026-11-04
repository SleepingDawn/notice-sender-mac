using System.IO;

namespace NoticeSender.Automation;

/// <summary>Explicit human review after an owned attachment submission. Null means the user did not confirm.</summary>
public interface IManualAttachmentReview
{
    AttachmentCompletionOutcome? Review(ManualAttachmentRequest request, CancellationToken cancellationToken);
}

public interface IAttachmentCompletionOutcomeSource
{
    AttachmentCompletionOutcome? LastAttachmentCompletion { get; }
}

public sealed class ManualAttachmentRequest
{
    public Guid AttemptId { get; }
    public ChatTarget Target { get; }
    public IReadOnlyList<string> Paths { get; }
    public DateTimeOffset SubmittedAt { get; }

    public ManualAttachmentRequest(Guid attemptId, ChatTarget target, IReadOnlyList<string> paths, DateTimeOffset submittedAt)
    {
        if (attemptId == Guid.Empty || submittedAt == default) throw new ArgumentException("첨부 전송 시도와 시각이 필요합니다.");
        ManualAttachmentScope.RequireTarget(target);
        AttemptId = attemptId; Target = target; Paths = ManualAttachmentScope.FreezePaths(paths); SubmittedAt = submittedAt;
    }

    public void RequireMatch(AttachmentCompletionOutcome outcome)
    {
        if (outcome == null || outcome.AttemptId != AttemptId || outcome.ConfirmedAt < SubmittedAt)
            throw new InvalidOperationException("사용자 확인이 현재 첨부 전송 시도와 다릅니다.");
        ManualAttachmentScope.RequireMatch(Target, Paths, outcome.Target, outcome.Paths);
    }
}

/// <summary>A durable user statement, distinct from an automatic receipt or server acknowledgement.</summary>
public sealed class AttachmentCompletionOutcome
{
    public string Kind => "user-confirmed";
    public Guid AttemptId { get; }
    public Guid BatchId { get; }
    public ChatTarget Target { get; }
    public IReadOnlyList<string> Paths { get; }
    public DateTimeOffset ConfirmedAt { get; }
    public string AuditReference { get; }

    public AttachmentCompletionOutcome(Guid attemptId, Guid batchId, ChatTarget target, IReadOnlyList<string> paths,
        DateTimeOffset confirmedAt, string auditReference)
    {
        if (attemptId == Guid.Empty || batchId == Guid.Empty || confirmedAt == default || string.IsNullOrWhiteSpace(auditReference))
            throw new ArgumentException("사용자 첨부 확인에는 전송 시도, 발송 항목, 시각과 저장한 확인 기록이 필요합니다.");
        ManualAttachmentScope.RequireTarget(target);
        AttemptId = attemptId; BatchId = batchId; Target = target; Paths = ManualAttachmentScope.FreezePaths(paths);
        ConfirmedAt = confirmedAt; AuditReference = auditReference;
    }

    internal void RequireMatch(Guid batchId, ChatTarget target, IReadOnlyList<string> paths)
    {
        if (BatchId != batchId) throw new InvalidOperationException("사용자가 확인한 첨부 발송 항목이 다릅니다.");
        ManualAttachmentScope.RequireMatch(target, paths, Target, Paths);
    }
}

internal static class ManualAttachmentScope
{
    internal static void RequireTarget(ChatTarget? target)
    {
        if (target == null || target.ProcessId <= 0 || target.WindowHandle == 0 || target.ComposerHandle == 0 ||
            target.WindowHandle == target.ComposerHandle || string.IsNullOrWhiteSpace(target.RoomName))
            throw new ArgumentException("정확한 첨부 수신 채팅방과 입력란이 필요합니다.");
    }
    internal static IReadOnlyList<string> FreezePaths(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (paths.Count == 0 || paths.Any(path => string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)))
            throw new ArgumentException("사용자 확인의 첨부 전체 경로가 필요합니다.");
        string[] result = paths.Select(Path.GetFullPath).ToArray();
        if (result.Distinct(StringComparer.OrdinalIgnoreCase).Count() != result.Length) throw new ArgumentException("사용자 확인의 첨부 경로가 중복됩니다.");
        return Array.AsReadOnly(result);
    }
    internal static void RequireMatch(ChatTarget expectedTarget, IReadOnlyList<string> expectedPaths, ChatTarget actualTarget, IReadOnlyList<string> actualPaths)
    {
        RequireTarget(expectedTarget); RequireTarget(actualTarget);
        if (expectedTarget.ProcessId != actualTarget.ProcessId || expectedTarget.WindowHandle != actualTarget.WindowHandle ||
            expectedTarget.ComposerHandle != actualTarget.ComposerHandle || expectedTarget.RoomName != actualTarget.RoomName ||
            !FreezePaths(expectedPaths).SequenceEqual(FreezePaths(actualPaths), StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("사용자가 확인한 채팅방이나 첨부 경로와 순서가 변경되었습니다.");
    }
}
