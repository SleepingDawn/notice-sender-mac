using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NoticeSender.Automation;
using NoticeSender.Core;

namespace NoticeSender.Windows;

internal sealed class ReviewWindow : Window
{
    public ReviewWindow(IReadOnlyList<BatchItem> snapshot, bool dryRun, bool execute, bool demo, ReviewedAttachmentBatch? reviewed = null)
    {
        Title = execute ? dryRun ? "드라이런 최종 검토" : "실제 발송 최종 검토" : "발송 미리보기";
        Width = 1160; Height = 800; MinWidth = 850; MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new FontFamily("Malgun Gothic"); FontSize = 14; Background = Brushes.White;
        var root = new DockPanel { Margin = new Thickness(25) };
        string heading = $"수신자 {snapshot.Count}명 · 메시지 {snapshot.Sum(b => b.AllMessages.Count)}개 · 첨부파일 {snapshot.Sum(b => b.AttachmentPaths.Count)}개";
        var top = Ui.Stack(Ui.Text(Title, 25), Ui.Text(heading, 18), Ui.Text(demo ? "데모 데이터입니다. 실제 카카오톡에 접근하지 않습니다." : dryRun ? "드라이런은 채팅방과 입력창만 확인합니다. 글 입력·붙여넣기·파일 전송을 실행하지 않습니다." : "아래 수신자·문구·공통 메시지 순서와 첨부파일 경로를 확인하세요. 이 창에 표시된 사본만 발송하며 이전 미리보기 확인은 재사용하지 않습니다."));
        var missingNicknames = snapshot.Where(b => !string.IsNullOrWhiteSpace(b.Message) && !string.IsNullOrWhiteSpace(b.Nickname) && !b.Message.Contains(b.Nickname, StringComparison.Ordinal)).Select(b => b.StudentName).ToList();
        if (missingNicknames.Count > 0)
        {
            var warning = Ui.Text("호칭이 포함되지 않은 개별 문구: " + string.Join(", ", missingNicknames) + ". 표의 행과 수신자가 맞는지 확인하세요.", 13);
            warning.Foreground = Brushes.DarkOrange; top.Children.Add(warning);
        }
        DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        var footer = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        var check = new CheckBox { Content = dryRun ? "수신 대상과 검사 내용을 확인했습니다." : "모든 수신자, 최종 메시지와 첨부파일을 확인했으며 이 내용의 실제 발송에 동의합니다.", Margin = new Thickness(0, 4, 0, 14), Visibility = execute ? Visibility.Visible : Visibility.Collapsed };
        footer.Children.Add(check);
        var proceed = Ui.Button(dryRun ? "확인한 내용으로 드라이런 시작" : "확인한 내용 실제 발송", () => DialogResult = true, true); proceed.IsEnabled = false; proceed.Visibility = execute ? Visibility.Visible : Visibility.Collapsed;
        check.Checked += (_, _) => proceed.IsEnabled = true; check.Unchecked += (_, _) => proceed.IsEnabled = false;
        footer.Children.Add(Ui.Actions(Ui.Button("돌아가기", () => DialogResult = false), Ui.Button("전체 미리보기 복사", () => Clipboard.SetText(string.Join("\n\n════════════════════\n\n", snapshot.Select(batch => Describe(batch, reviewed))))), proceed));
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var left = new ListBox { ItemsSource = snapshot, DisplayMemberPath = "StudentName", MinWidth = 200, Margin = new Thickness(0, 0, 18, 0) };
        var detail = Ui.Box("", true); detail.IsReadOnly = true; detail.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        left.SelectionChanged += (_, _) => { if (left.SelectedItem is BatchItem batch) detail.Text = Describe(batch, reviewed); };
        var content = new Grid(); content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) }); content.ColumnDefinitions.Add(new ColumnDefinition()); content.Children.Add(left); Grid.SetColumn(detail, 1); content.Children.Add(detail); root.Children.Add(content);
        Content = root; if (snapshot.Count > 0) left.SelectedIndex = 0;
    }
    private static string Describe(BatchItem batch, ReviewedAttachmentBatch? reviewed)
    {
        var text = new StringBuilder();
        text.AppendLine("학생: " + batch.StudentName); text.AppendLine("호칭: " + batch.Nickname); text.AppendLine("정확한 채팅방: " + batch.ChatRoomName); text.AppendLine("학생 ID: " + batch.StudentId); text.AppendLine();
        int index = 0; foreach (string message in batch.AllMessages) { text.AppendLine($"── 메시지 {++index} ({message.Length}자) ──"); text.AppendLine(message); text.AppendLine(); }
        if (reviewed != null)
        {
            var captured = reviewed.Items.SingleOrDefault(item => item.BatchId == batch.Id);
            if (captured == null || captured.RoomName != batch.ChatRoomName) throw new InvalidOperationException("첨부 검토 정보의 발송 대상이 다릅니다.");
            text.AppendLine("── 첨부파일 " + captured.Files.Count + "개 ──");
            if (captured.Files.Count == 0) text.AppendLine("없음");
            foreach (var file in captured.Files)
            {
                text.AppendLine(file.Name + " · " + file.Length.ToString("N0") + "바이트");
                text.AppendLine(file.Path);
            }
        }
        else
        {
            text.AppendLine("── 첨부파일 " + batch.AttachmentPaths.Count + "개 ──");
            if (batch.AttachmentPaths.Count == 0) text.AppendLine("없음"); else foreach (string file in batch.AttachmentPaths) text.AppendLine(file);
        }
        return text.ToString();
    }
}
