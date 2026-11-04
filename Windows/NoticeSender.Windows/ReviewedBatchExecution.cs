using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NoticeSender.Automation;
using NoticeSender.Core;

namespace NoticeSender.Windows;

/// <summary>Connects one reviewed snapshot to transport only after whole-batch attachment verification.</summary>
internal static class ReviewedBatchExecution
{
    internal static async Task RunAsync(AppDatabase db, IReadOnlyList<BatchItem> items, bool dryRun, ReviewedAttachmentBatch? reviewed,
        Func<ApprovedAttachmentBatch?, IChatClient> factory, Action<BatchItem> progress, Action persist, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        // Preserve the approved values while file verification awaits a worker thread.
        var originals = items.ToArray();
        var work = originals.Select(item => new BatchItem
        {
            Id = item.Id, StudentId = item.StudentId, StudentName = item.StudentName, Nickname = item.Nickname,
            ChatRoomName = item.ChatRoomName, ChatId = item.ChatId, Message = item.Message, Status = item.Status,
            PreserveMessageWhitespace = item.PreserveMessageWhitespace, CommonMessages = [.. item.CommonMessages], AttachmentPaths = [.. item.AttachmentPaths],
            AttachmentCompletionKind = item.AttachmentCompletionKind, AttachmentCompletionConfirmedAt = item.AttachmentCompletionConfirmedAt,
            AttachmentCompletionAuditReference = item.AttachmentCompletionAuditReference, AttachmentCompletionBatchId = item.AttachmentCompletionBatchId,
            AttachmentCompletionAttemptId = item.AttachmentCompletionAttemptId
        }).ToArray();
        if (work.Select(item => item.Id).Distinct().Count() != work.Length)
            throw new InvalidOperationException("검토한 발송 항목이 중복됩니다.");
        bool hasAttachments = work.Any(item => item.AttachmentPaths.Count > 0);
        if (!dryRun)
        {
            if (hasAttachments && reviewed == null)
                throw new InvalidOperationException("실제 첨부 발송에는 최종 검토한 파일 정보가 필요합니다.");
            if (reviewed != null) RequireReviewedScope(work, reviewed);
        }
        token.ThrowIfCancellationRequested();
        // Dry runs never open file handles, even if a prior manifest is supplied or its paths no longer exist.
        using ApprovedAttachmentBatch? approved = !dryRun && hasAttachments
            ? await Task.Run(() => reviewed!.OpenVerifiedFiles(), token)
            : null;
        token.ThrowIfCancellationRequested();
        var client = factory(approved) ?? throw new InvalidOperationException("발송 연결을 준비하지 못했습니다.");
        token.ThrowIfCancellationRequested();
        var originalById = originals.Select((item, index) => (Id: work[index].Id, Item: item)).ToDictionary(pair => pair.Id, pair => pair.Item);
        await new SendCoordinator(client).RunAsync(db, work, dryRun, changed =>
        {
            var original = originalById[changed.Id]; original.Status = changed.Status;
            original.AttachmentCompletionKind = changed.AttachmentCompletionKind;
            original.AttachmentCompletionConfirmedAt = changed.AttachmentCompletionConfirmedAt;
            original.AttachmentCompletionAuditReference = changed.AttachmentCompletionAuditReference;
            original.AttachmentCompletionBatchId = changed.AttachmentCompletionBatchId;
            original.AttachmentCompletionAttemptId = changed.AttachmentCompletionAttemptId;
            progress(original);
        }, persist, token);
    }

    private static void RequireReviewedScope(IReadOnlyList<BatchItem> items, ReviewedAttachmentBatch reviewed)
    {
        if (items.Count != reviewed.Items.Count) throw new InvalidOperationException("최종 검토한 수신자 목록이 변경되었습니다.");
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i]; var expected = reviewed.Items[i];
            if (item.Id != expected.BatchId || item.ChatRoomName != expected.RoomName || item.AttachmentPaths.Count != expected.Files.Count)
                throw new InvalidOperationException("최종 검토한 수신자, 채팅방 또는 첨부 묶음이 변경되었습니다.");
            for (int j = 0; j < item.AttachmentPaths.Count; j++)
                if (!string.Equals(Path.GetFullPath(item.AttachmentPaths[j]), expected.Files[j].Path, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("최종 검토한 첨부파일 경로나 순서가 변경되었습니다.");
        }
    }
}
