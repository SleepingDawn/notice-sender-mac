namespace NoticeSender.Automation;

/// <summary>Optional independent receipt observation. Implementations must never send or confirm attachments.</summary>
public interface IAttachmentCompletionObserver
{
    IAttachmentCompletionSession Begin(ChatTarget target, IReadOnlyList<string> paths, CancellationToken cancellationToken);
}

public interface IAttachmentCompletionSession : IDisposable
{
    bool Observe(CancellationToken cancellationToken);
}
