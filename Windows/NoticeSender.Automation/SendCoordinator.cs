using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NoticeSender.Core;

namespace NoticeSender.Automation;

public sealed class SendCoordinator(IChatClient client)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private static bool Protected(string status) => status is "전송 요청 완료" or "확인 필요" or "전송 중" or "제외";

    public async Task RunAsync(AppDatabase database, IReadOnlyList<BatchItem> items, bool dryRun,
        Action<BatchItem> progress, Action persist, CancellationToken cancellationToken)
    {
        if (!await gate.WaitAsync(0, CancellationToken.None)) throw new InvalidOperationException("이미 전송이 실행 중입니다.");
        try
        {
            // Capture the reviewed values before awaiting external UI operations.
            var work = items.Where(i => (dryRun && i.Status != "제외") || (!Protected(i.Status) && !database.Logs.Any(l => l.BatchId == i.Id && Protected(l.Result))))
                .Select(i => (Original: i, Snapshot: new BatchItem { Id = i.Id, StudentId = i.StudentId,
                    StudentName = i.StudentName, Nickname = i.Nickname, ChatRoomName = i.ChatRoomName, Message = i.Message, PreserveMessageWhitespace = i.PreserveMessageWhitespace,
                    CommonMessages = [.. i.CommonMessages], AttachmentPaths = [.. i.AttachmentPaths], ChatId = i.ChatId })).ToArray();
            if (work.GroupBy(w => w.Snapshot.ChatRoomName, StringComparer.Ordinal).Any(g => g.Count() > 1))
                throw new InvalidOperationException("동일한 채팅방이 두 번 포함되어 있습니다.");
            foreach (var (original, item) in work)
            {
                if (cancellationToken.IsCancellationRequested) break;
                if (string.IsNullOrWhiteSpace(item.ChatRoomName)) throw new InvalidOperationException("정확한 채팅방 이름이 필요합니다.");
                var messages = item.AllMessages.ToArray();
                if (!dryRun && ((messages.Length == 0 && item.AttachmentPaths.Count == 0) || messages.Length > 6 || messages.Any(string.IsNullOrWhiteSpace)))
                    throw new InvalidOperationException("전송할 메시지는 빈 내용 없이 1~6개여야 합니다.");
                bool possibleDelivery = false;
                var priorStatus = original.Status;
                bool preserveStatus = dryRun && Protected(priorStatus);
                var log = new SendLog { Id = Guid.NewGuid(), BatchId = item.Id, StudentId = item.StudentId,
                    StudentName = item.StudentName, ChatRoomName = item.ChatRoomName, SentAt = DateTimeOffset.UtcNow,
                    Messages = [.. messages], MessageSHA256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(messages)))) };
                log.Result = dryRun ? "준비" : "전송 중";
                if (dryRun) log.Detail = "드라이런 검사 시작: 메시지·파일 전송 없음";
                if (!dryRun)
                {
                    original.Status = log.Result;
                    original.AttachmentCompletionKind = null; original.AttachmentCompletionConfirmedAt = null;
                    original.AttachmentCompletionAuditReference = null; original.AttachmentCompletionBatchId = null; original.AttachmentCompletionAttemptId = null;
                }
                database.Logs.Add(log);
                // If persistence fails here, absolutely no transport operation is allowed.
                persist(); progress(original);
                try
                {
                    ChatTarget target;
                    if (dryRun && client is IDryRunChatClient opener)
                    {
                        var opened = await opener.OpenRoomDryRunAsync(item.ChatRoomName, cancellationToken);
                        if (!opened.CanCompose) throw new InvalidOperationException("채팅방은 열렸으나 발송 입력란이 없습니다. 알림 전용 채널에는 발송할 수 없습니다.");
                        target = opened.Target;
                    }
                    else target = await client.ResolveAsync(item.ChatRoomName, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!dryRun)
                    {
                        AttachmentCompletionOutcome? attachmentOutcome = null;
                        foreach (var message in messages)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            possibleDelivery = true; // An exception cannot establish that delivery did not occur.
                            await client.SendTextAsync(target, message, cancellationToken);
                        }
                        if (item.AttachmentPaths.Count != 0)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            possibleDelivery = true;
                            await client.SendFilesAsync(target, item.AttachmentPaths, cancellationToken);
                            attachmentOutcome = (client as IAttachmentCompletionOutcomeSource)?.LastAttachmentCompletion;
                            attachmentOutcome?.RequireMatch(item.Id, target, item.AttachmentPaths);
                        }
                        cancellationToken.ThrowIfCancellationRequested();
                        await client.CompleteAsync(target, cancellationToken);
                        cancellationToken.ThrowIfCancellationRequested();
                        // User confirmation is committed only after the native final-state checks also succeed.
                        if (attachmentOutcome != null)
                        {
                            original.AttachmentCompletionKind = log.AttachmentCompletionKind = attachmentOutcome.Kind;
                            original.AttachmentCompletionConfirmedAt = log.AttachmentCompletionConfirmedAt = attachmentOutcome.ConfirmedAt;
                            original.AttachmentCompletionAuditReference = log.AttachmentCompletionAuditReference = attachmentOutcome.AuditReference;
                            original.AttachmentCompletionBatchId = log.AttachmentCompletionBatchId = attachmentOutcome.BatchId;
                            original.AttachmentCompletionAttemptId = log.AttachmentCompletionAttemptId = attachmentOutcome.AttemptId;
                            log.Detail = "첨부 완료는 사용자가 카카오톡에서 확인했습니다. 자동 완료 확인이 아닙니다.";
                        }
                    }
                    log.Result = dryRun ? "드라이런 확인 완료" : "전송 요청 완료";
                    original.Status = preserveStatus ? priorStatus : log.Result;
                }
                catch (Exception ex)
                {
                    log.Result = possibleDelivery ? "확인 필요" : "실패";
                    original.Status = preserveStatus ? priorStatus : log.Result;
                    log.Detail = ex is OperationCanceledException ? "사용자 중단: " + ex.Message : ex.Message;
                    log.SentAt = DateTimeOffset.UtcNow;
                    persist(); progress(original);
                    break; // Ambiguity, cancellation, and failures all stop the batch.
                }
                log.SentAt = DateTimeOffset.UtcNow;
                persist(); progress(original);
            }
        }
        finally { gate.Release(); }
    }
}
