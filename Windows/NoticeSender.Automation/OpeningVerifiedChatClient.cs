namespace NoticeSender.Automation;

/// <summary>Opens a reviewed room, then independently resolves and compares its exact native target before transport.</summary>
public sealed class OpeningVerifiedChatClient : IChatClient, IDryRunChatClient, IAttachmentCompletionOutcomeSource
{
    private readonly IChatClient client;
    private readonly IDryRunChatClient opener;
    public AttachmentCompletionOutcome? LastAttachmentCompletion => (client as IAttachmentCompletionOutcomeSource)?.LastAttachmentCompletion;

    public OpeningVerifiedChatClient(IChatClient client)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
        opener = client as IDryRunChatClient ?? throw new ArgumentException("채팅방 열기 확인 기능이 필요합니다.", nameof(client));
    }

    public async Task<ChatTarget> ResolveAsync(string exactRoomName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(exactRoomName)) throw new ArgumentException("정확한 채팅방 이름이 필요합니다.", nameof(exactRoomName));
        var opened = await opener.OpenRoomDryRunAsync(exactRoomName, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (opened == null || opened.RoomName != exactRoomName || !ValidTarget(opened.Target, exactRoomName))
            throw new InvalidOperationException("열린 채팅방의 정확한 이름과 발송 입력란을 확인할 수 없습니다.");
        var resolved = await client.ResolveAsync(exactRoomName, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!ValidTarget(resolved, exactRoomName) || resolved.ProcessId != opened.Target.ProcessId ||
            resolved.WindowHandle != opened.Target.WindowHandle || resolved.ComposerHandle != opened.Target.ComposerHandle)
            throw new InvalidOperationException("채팅방을 연 뒤 정확한 대상이나 입력란이 변경되어 발송을 중단했습니다.");
        return resolved;
    }

    private static bool ValidTarget(ChatTarget? target, string room) => target != null && target.RoomName == room &&
        target.ProcessId > 0 && target.WindowHandle != 0 && target.ComposerHandle != 0 && target.WindowHandle != target.ComposerHandle;

    public Task<RoomOpenDryRunResult> OpenRoomDryRunAsync(string exactRoomName, CancellationToken cancellationToken) =>
        opener.OpenRoomDryRunAsync(exactRoomName, cancellationToken);

    public Task SendTextAsync(ChatTarget target, string text, CancellationToken cancellationToken) =>
        client.SendTextAsync(target, text, cancellationToken);

    public Task SendFilesAsync(ChatTarget target, IReadOnlyList<string> paths, CancellationToken cancellationToken) =>
        client.SendFilesAsync(target, paths, cancellationToken);

    public Task CompleteAsync(ChatTarget target, CancellationToken cancellationToken) => client.CompleteAsync(target, cancellationToken);
}
