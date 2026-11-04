using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using NoticeSender.Automation;

namespace NoticeSender.Windows;

internal sealed class ManualAttachmentReviewWindow : Window
{
    internal CheckBox Acknowledgement { get; } = new()
    {
        Content = new TextBlock { Text = "이 채팅방에서 위 파일이 모두 전송된 것을 확인했습니다.", TextWrapping = TextWrapping.Wrap },
        HorizontalContentAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 18, 0, 12)
    };
    internal Button ConfirmButton { get; }
    internal Button StopButton { get; }
    internal ManualAttachmentReviewWindow(string room, ReviewedAttachmentItem item)
    {
        Title = "첨부 전송 확인"; Width = 700; Height = 480; MinWidth = 520; MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var content = Ui.Box(string.Join("\n\n", item.Files.Select(file => $"{file.Name} · {file.Length:N0} bytes\n{file.Path}")), true);
        content.IsReadOnly = true; content.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        ConfirmButton = Ui.Button("확인하고 다음으로", () => { if (Acknowledgement.IsChecked == true) DialogResult = true; }, true);
        ConfirmButton.IsEnabled = false; ConfirmButton.IsDefault = false;
        StopButton = Ui.Button("확인하지 못했어요 · 중단", () => DialogResult = false);
        StopButton.IsCancel = true;
        Acknowledgement.Checked += (_, _) => ConfirmButton.IsEnabled = true;
        Acknowledgement.Unchecked += (_, _) => ConfirmButton.IsEnabled = false;
        var layout = new DockPanel();
        var footer = Ui.Stack(Acknowledgement, Ui.Actions(StopButton, ConfirmButton));
        DockPanel.SetDock(footer, Dock.Bottom); layout.Children.Add(footer);
        layout.Children.Add(Ui.Scroll(Ui.Stack(
            Ui.Text("첨부파일을 확인해 주세요", 24), Ui.Text("채팅방: " + room, 18),
            Ui.Text("앱이 전송 완료 여부를 자동으로 읽지 못했습니다. 카카오톡에서 아래 파일이 모두 전송됐는지 확인해 주세요. 이 창에서는 파일을 다시 보내지 않습니다."),
            content)));
        Content = new Border { Padding = new Thickness(24), Child = layout };
    }
}

// Supplied only by the ordinary reviewed workflow. Unattended bot tests never construct this reviewer.
internal sealed class ManualAttachmentReview(Window owner, ApprovedAttachmentBatch approved, string folder) : IManualAttachmentReview
{
    public AttachmentCompletionOutcome? Review(ManualAttachmentRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var item = approved.RequireMatch(request.Target, request.Paths);
        ExactTranscriptCapture.RequireRoot(request.Target, true);
        bool accepted = owner.Dispatcher.Invoke(() =>
        {
            token.ThrowIfCancellationRequested();
            var dialog = new ManualAttachmentReviewWindow(request.Target.RoomName, item) { Owner = owner };
            using var registration = token.Register(() => owner.Dispatcher.BeginInvoke(new Action(() => { if (dialog.IsVisible) dialog.DialogResult = false; })));
            bool result = dialog.ShowDialog() == true;
            token.ThrowIfCancellationRequested(); return result;
        });
        if (!accepted) return null;
        token.ThrowIfCancellationRequested();
        item = approved.RequireMatch(request.Target, request.Paths);
        ExactTranscriptCapture.RequireRoot(request.Target, true);
        DateTimeOffset at = DateTimeOffset.UtcNow;
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, request.AttemptId.ToString("N") + "-user-confirmation.json");
        using (var audit = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
        {
            JsonSerializer.Serialize(audit, new {
                kind = "user-confirmed", automaticReceipt = false, request.AttemptId, batchId = item.BatchId,
                request.SubmittedAt, confirmedAt = at, request.Target.RoomName, request.Target.ProcessId,
                root = request.Target.WindowHandle.ToInt64(), composer = request.Target.ComposerHandle.ToInt64(),
                files = item.Files.Select(file => new { file.Path, file.Length, file.Sha256 }).ToArray()
            });
            audit.Flush(true);
        }
        token.ThrowIfCancellationRequested(); approved.RequireMatch(request.Target, request.Paths);
        ExactTranscriptCapture.RequireRoot(request.Target, true);
        return new(request.AttemptId, item.BatchId, request.Target, request.Paths, at, path);
    }
}
