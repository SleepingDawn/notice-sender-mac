using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NoticeSender.Automation;

namespace NoticeSender.Windows;

public sealed class BotSendTestWindow : Window
{
    private readonly IChatClient client;
    private readonly IDryRunChatClient opener;
    private readonly string room;
    private readonly string reportPath;
    private readonly bool exitAfterTest;
    private readonly IBotEvidenceCapture? evidenceCapture;
    private readonly string? onlyStep;
    private readonly TextBlock status = Ui.Text("합성 봇 테스트 준비");
    private readonly DataGrid results = Ui.Table();
    private readonly Button start;
    private readonly Button cancel;
    private CancellationTokenSource? cancellation;
    private bool running;
    private bool started;
    private bool closeRequested;

    public BotSendTestWindow(string room, string reportPath, bool acknowledgeBotOnly, bool autoStart = false, bool exitAfterTest = false, bool captureEvidence = false, bool replaceReviewedBotCue = false, string? onlyStep = null, bool confirmNativeBotFiles = false)
        : this(CreateClient(room, acknowledgeBotOnly, replaceReviewedBotCue, captureEvidence, onlyStep, reportPath, confirmNativeBotFiles), room, reportPath, acknowledgeBotOnly, autoStart, exitAfterTest, captureEvidence ? new BotWindowEvidence() : null, onlyStep)
    {
        if (replaceReviewedBotCue)
            status.Text = "명시 허용: 이 봇의 확인한 회색 안내 문구와 동일한 내용/서식만 작업마다 한 번 교체합니다. 일반 초안은 차단합니다.";
    }

    private static WindowsKakaoClient CreateClient(string room, bool acknowledged, bool replaceReviewedBotCue, bool captureEvidence, string? onlyStep, string reportPath, bool confirmNativeBotFiles)
    {
        BotSendTestOptions.ValidateDestination(room, acknowledged);
        BotSendTestOptions.ValidateNativeFileTest(confirmNativeBotFiles, captureEvidence, onlyStep);
        var evidence = confirmNativeBotFiles ? new BotFileConfirmationEvidence(reportPath, new BotWindowEvidence()) : null;
        return new WindowsKakaoClient(replaceReviewedBotCue ? room : null,
            evidence == null ? null : new SyntheticFileConfirmationPolicy(room, evidence.Capture));
    }

    internal BotSendTestWindow(IChatClient client, string room, string reportPath, bool acknowledgeBotOnly, bool autoStart = false, bool exitAfterTest = false, IBotEvidenceCapture? evidenceCapture = null, string? onlyStep = null)
    {
        BotSendTestOptions.ValidateDestination(room, acknowledgeBotOnly);
        BotSendTestOptions.ValidateStep(onlyStep);
        this.client = client;
        opener = client as IDryRunChatClient ?? throw new ArgumentException("방 열기 검증 기능이 필요합니다.");
        this.room = room;
        this.reportPath = Path.GetFullPath(reportPath);
        this.exitAfterTest = exitAfterTest;
        this.evidenceCapture = evidenceCapture;
        this.onlyStep = onlyStep;
        Title = "공지발송 · 합성 봇 전송 테스트";
        Width = 1030; Height = 700; MinWidth = 780; MinHeight = 540;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Malgun Gothic"); FontSize = 14;
        Background = new SolidColorBrush(Color.FromRgb(243, 247, 250));
        start = Ui.Button("합성 봇 테스트 시작", () => _ = StartAsync(), true);
        cancel = Ui.Button("중지", () => { cancellation?.Cancel(); status.Text = "중지 요청 중… 현재 결과를 저장합니다."; });
        cancel.IsEnabled = false;
        results.IsReadOnly = true;
        Ui.Col(results, "단계", nameof(BotSendTestStep.Id), 70, true);
        Ui.Col(results, "합성 테스트", nameof(BotSendTestStep.Title), 340, true);
        Ui.Col(results, "상태", nameof(BotSendTestStep.DisplayStatus), 230, true);
        Ui.Col(results, "사유", nameof(BotSendTestStep.Error), 300, true);
        var root = new DockPanel { Margin = new Thickness(24) };
        var heading = Ui.Stack(Ui.Text("합성 봇 전송 테스트", 26), Ui.Text("대상 봇: " + room, 19),
            Ui.Text("이 봇 방에 실제 합성 메시지와 TXT / CSV / PDF / PNG 파일을 보냅니다. 학생 DB를 열지 않고 새 메모리 데이터만 사용합니다."),
            Ui.Text(onlyStep == null ? "메시지 6개, 단일 / 다중 첨부, 첨부 전용, PDF / PNG 단독, 첫 메시지 후 중단과 재시도 차단을 순서대로 확인합니다. 예상 밖 실패는 즉시 중단합니다." : "선택 단계 " + onlyStep + "만 시험합니다. 다른 단계는 보내지 않으며 전체 7개 통과로 기록하지 않습니다.", 12),
            Ui.Actions(start, cancel));
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        var footer = Ui.Stack(status, Ui.Text("전송 요청 / 앱 내 완료 확인은 서버 수신 확인이 아닙니다.", 12), Ui.Text("합성 테스트 결과: " + this.reportPath, 11));
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        root.Children.Add(results); Content = root;
        Closing += OnClosing;
        if (autoStart) ContentRendered += async (_, _) => { Activate(); await StartAsync(); };
    }

    internal async Task StartAsync()
    {
        if (started) return;
        started = true;
        running = true; start.IsEnabled = false; cancel.IsEnabled = true;
        cancellation = new CancellationTokenSource();
        int exitCode = 1;
        try
        {
            var report = await new BotSendTestRunner(client, opener, evidenceCapture).RunAsync(room, true, reportPath, current =>
            {
                results.ItemsSource = current.Steps; results.Items.Refresh();
                status.Text = current.Completed
                    ? current.Cancelled ? $"사용자 중지 · 통과 {current.SuccessCount}/{current.RequestedCount}개"
                    : current.Success ? $"{(current.OnlyStep == null ? "합성 테스트 모두 통과" : "선택 테스트 통과")} · {current.SuccessCount}/{current.RequestedCount}개 (제어 중단은 정상)"
                    : $"실패하여 중단 · 통과 {current.SuccessCount}/{current.RequestedCount}개 · {current.Failure}"
                    : current.Preparation.Status != "verified" ? "봇 방 열기 / 발송 입력란 검증 중…"
                    : $"합성 테스트 진행 중… 통과 {current.SuccessCount}/{current.RequestedCount}개";
            }, cancellation.Token, onlyStep);
            exitCode = report.ExitCode;
        }
        catch (Exception ex) { status.Text = "테스트를 완료하지 못했습니다: " + WindowsTrustFailure.Describe(ex); }
        finally { running = false; cancel.IsEnabled = false; cancellation.Dispose(); cancellation = null; }
        if (exitAfterTest) Application.Current.Shutdown(exitCode);
        else if (closeRequested) Close();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!running) return;
        e.Cancel = true; closeRequested = true; cancellation?.Cancel();
        status.Text = "중지 후 결과를 저장하고 창을 닫습니다…";
    }
}
