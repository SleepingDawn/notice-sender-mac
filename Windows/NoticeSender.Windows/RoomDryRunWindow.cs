using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NoticeSender.Automation;

namespace NoticeSender.Windows;

public sealed class RoomDryRunWindow : Window
{
    private readonly IDryRunChatClient client;
    private readonly string reportPath;
    private readonly bool exitAfterFinish;
    private readonly string[]? suppliedRooms;
    private readonly string suppliedText;
    private readonly TextBox roomsBox = Ui.Box("", true);
    private readonly TextBlock status = Ui.Text("정확한 채팅방 이름을 한 줄에 하나씩 입력하세요.");
    private readonly DataGrid results = Ui.Table();
    private readonly Button start;
    private readonly Button cancel;
    private CancellationTokenSource? cancellation;
    private bool running;
    private bool closeRequested;

    public RoomDryRunWindow(IReadOnlyList<string>? rooms = null, string? reportPath = null, bool autoStart = false, bool exitAfterFinish = false)
        : this(new WindowsKakaoClient(), rooms, reportPath, autoStart, exitAfterFinish) { }

    internal RoomDryRunWindow(IDryRunChatClient client, IReadOnlyList<string>? rooms = null, string? reportPath = null, bool autoStart = false, bool exitAfterFinish = false)
    {
        this.client = client;
        this.reportPath = System.IO.Path.GetFullPath(reportPath ?? RoomDryRunRunner.DefaultReportPath());
        this.exitAfterFinish = exitAfterFinish;
        Title = "공지발송 · 방 열기 드라이런";
        Width = 930; Height = 720; MinWidth = 700; MinHeight = 540;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Malgun Gothic"); FontSize = 14;
        Background = new SolidColorBrush(Color.FromRgb(243, 247, 250));
        suppliedRooms = rooms?.ToArray();
        roomsBox.Text = suppliedRooms == null ? "" : string.Join(Environment.NewLine, suppliedRooms);
        suppliedText = roomsBox.Text;
        roomsBox.Height = 150;
        start = Ui.Button("방 열기 시작", () => _ = StartAsync(), true);
        cancel = Ui.Button("중지", () => { cancellation?.Cancel(); status.Text = "중지 요청 중… 현재 작업이 끝나면 결과를 저장합니다."; });
        cancel.IsEnabled = false;
        results.IsReadOnly = true; results.MinHeight = 180;
        Ui.Col(results, "정확한 방 이름", nameof(RoomDryRunStep.RoomName), 300, true);
        Ui.Col(results, "상태", nameof(RoomDryRunStep.DisplayStatus), 190, true);
        Ui.Col(results, "실패 사유", nameof(RoomDryRunStep.Error), 300, true);
        var root = new DockPanel { Margin = new Thickness(24) };
        var heading = Ui.Stack(Ui.Text("방 열기 드라이런", 26),
            Ui.Text("앱이 입력한 순서대로 방을 열고 창과 입력란을 확인합니다. 메시지나 파일을 보내지 않으며 학생 DB를 사용하지 않습니다."),
            Ui.Text("정확한 채팅방 이름 · 한 줄에 하나 · 최대 50개", 12), roomsBox, Ui.Actions(start, cancel));
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        var footer = Ui.Stack(status, Ui.Text("결과 저장 위치: " + this.reportPath, 11));
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        root.Children.Add(results); Content = root;
        Closing += OnClosing;
        if (autoStart) Loaded += async (_, _) => await StartAsync();
    }

    internal async Task StartAsync()
    {
        if (running) return;
        int exitCode = 1;
        try
        {
            var rooms = RoomDryRunRunner.ValidateRooms(suppliedRooms != null && roomsBox.Text == suppliedText
                ? suppliedRooms : roomsBox.Text.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries));
            cancellation = new CancellationTokenSource();
            running = true; roomsBox.IsReadOnly = true; start.IsEnabled = false; cancel.IsEnabled = true;
            var report = await new RoomDryRunRunner(client).RunAsync(rooms, reportPath, current =>
            {
                results.ItemsSource = current.Rooms;
                results.Items.Refresh();
                status.Text = current.Completed
                    ? current.Cancelled ? $"중지했습니다. 확인 완료 {current.SuccessCount}/{current.RequestedCount}개"
                    : current.Success ? $"모든 방을 확인했습니다. {current.SuccessCount}/{current.RequestedCount}개"
                    : $"실패한 방에서 중단했습니다. 확인 완료 {current.SuccessCount}/{current.RequestedCount}개"
                    : $"방 확인 중… 완료 {current.SuccessCount}/{current.RequestedCount}개";
            }, cancellation.Token);
            exitCode = report.ExitCode;
        }
        catch (Exception ex) { status.Text = "드라이런을 완료하지 못했습니다: " + ex.Message; }
        finally
        {
            running = false; roomsBox.IsReadOnly = false; start.IsEnabled = true; cancel.IsEnabled = false;
            cancellation?.Dispose(); cancellation = null;
        }
        if (exitAfterFinish) Application.Current.Shutdown(exitCode);
        else if (closeRequested) Close();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!running) return;
        e.Cancel = true;
        closeRequested = true;
        cancellation?.Cancel();
        status.Text = "중지 후 결과를 저장하고 창을 닫습니다…";
    }
}
