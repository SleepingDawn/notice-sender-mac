using NoticeSender.Core;

namespace NoticeSender.Automation;

public record ChatTarget(int ProcessId, nint WindowHandle, string RoomName, nint ComposerHandle);
public sealed record RoomOpenDryRunResult(string RoomName, bool WasAlreadyOpen, ChatTarget Target)
{
    public bool CanCompose => Target.ComposerHandle != 0;
}

public interface IDryRunChatClient
{
    Task<RoomOpenDryRunResult> OpenRoomDryRunAsync(string exactRoomName, CancellationToken cancellationToken);
}

public interface IChatClient
{
    Task<ChatTarget> ResolveAsync(string exactRoomName, CancellationToken cancellationToken);
    Task SendTextAsync(ChatTarget target, string text, CancellationToken cancellationToken);
    Task SendFilesAsync(ChatTarget target, IReadOnlyList<string> paths, CancellationToken cancellationToken);
    Task CompleteAsync(ChatTarget target, CancellationToken cancellationToken);
}
