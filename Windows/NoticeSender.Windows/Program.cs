using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows;
using NoticeSender.Automation;

namespace NoticeSender.Windows;

internal static class Program
{
    internal static bool ModesAreCompatible(string[] args)
    {
        if (args.Contains("--bot-send-test"))
            return !args.Any(arg => arg is "--demo" or "--smoke-test" or "--diagnose-kakao" or "--dry-run-rooms");
        return !args.Contains("--dry-run-rooms") || !args.Any(arg => arg is "--demo" or "--smoke-test" or "--diagnose-kakao");
    }

    [STAThread]
    public static int Main(string[] args)
    {
        if (!ModesAreCompatible(args)) return 1;
        if (args.Contains("--bot-send-test")) return RunBotSendTest(args);
        if (args.Contains("--dry-run-rooms")) return RunRoomDryRun(args);
        bool smoke = args.Contains("--smoke-test");
        bool demo = smoke || args.Contains("--demo");
        if (args.Contains("--diagnose-kakao"))
        {
            try
            {
                var diagnostic = new WindowsKakaoClient().DiagnoseAsync(CancellationToken.None).GetAwaiter().GetResult();
                string path = Path.Combine(Path.GetTempPath(), "NoticeSender-kakao-diagnostics.txt");
                File.WriteAllText(path, diagnostic, Encoding.UTF8);
                if (!smoke) MessageBox.Show(diagnostic + "\n\n저장 위치: " + path, "카카오톡 읽기 전용 진단");
                return 0;
            }
            catch (Exception ex) { if (!smoke) MessageBox.Show(ex.Message, "진단 실패"); return 1; }
        }
        if (smoke) File.Delete(Path.Combine(Path.GetTempPath(), "NoticeSender-smoke-error.txt"));
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        Ui.InstallStyles(app);
        try
        {
            using var instance = demo ? null : new SingleInstanceGuard(new NoticeSender.Core.DatabaseStore().Path);
            if (instance != null && !instance.Acquired)
            {
                MessageBox.Show("이미 실행 중인 공지발송 창이 있습니다. 같은 데이터베이스를 두 창에서 동시에 열 수 없습니다.", "공지발송 실행 중");
                return 0;
            }
            var window = new MainWindow(demo);
            app.MainWindow = window;
            if (smoke)
            {
                window.Loaded += (_, _) => window.Dispatcher.BeginInvoke(new Action(async () =>
                {
                    try { window.VerifySmokeTest(); await RoomDryRunSmokeTests.RunAsync(); await BotSendTestSmokeTests.RunAsync(); app.Shutdown(0); }
                    catch (Exception ex) { File.WriteAllText(Path.Combine(Path.GetTempPath(), "NoticeSender-smoke-error.txt"), ex.ToString()); app.Shutdown(2); }
                }));
            }
            return app.Run(window);
        }
        catch (Exception ex)
        {
            if (!smoke) MessageBox.Show("데이터를 열지 못했습니다. 기존 파일은 교체하지 않았습니다.\n\n" + ex.Message, "공지발송 시작 실패");
            return 1;
        }
    }

    private static int RunBotSendTest(string[] args)
    {
        try
        {
            var options = BotSendTestOptions.Parse(args);
            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            Ui.InstallStyles(app);
            // This mode never constructs the main window, database store, or real database mutex.
            return app.Run(new BotSendTestWindow(options.Room, options.ReportPath, acknowledgeBotOnly: true, autoStart: true, exitAfterTest: options.ExitAfterTest, captureEvidence: options.CaptureEvidence));
        }
        catch (Exception) { return 1; } // Command-line test failures must not block on an error dialog.
    }

    private static int RunRoomDryRun(string[] args)
    {
        try
        {
            string? Value(string name, bool required)
            {
                int index = Array.IndexOf(args, name);
                if (index < 0)
                {
                    if (required) throw new ArgumentException(name + " 뒤에 파일 경로가 필요합니다.");
                    return null;
                }
                if (args.Count(a => a == name) != 1 || index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
                    throw new ArgumentException(name + " 뒤에 파일 경로 하나를 지정하세요.");
                return args[index + 1];
            }
            using var json = JsonDocument.Parse(File.ReadAllText(Value("--dry-run-rooms", true)!));
            if (json.RootElement.ValueKind != JsonValueKind.Array || json.RootElement.EnumerateArray().Any(e => e.ValueKind != JsonValueKind.String))
                throw new ArgumentException("채팅방 목록 파일은 정확한 방 이름으로 이루어진 JSON 배열이어야 합니다.");
            var rooms = RoomDryRunRunner.ValidateRooms(json.RootElement.EnumerateArray().Select(e => e.GetString()!));
            string? reportPath = Value("--report", false);
            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            Ui.InstallStyles(app);
            // This path intentionally constructs no main window, store, or database mutex.
            return app.Run(new RoomDryRunWindow(rooms, reportPath, autoStart: true, exitAfterFinish: args.Contains("--exit-after-dry-run")));
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "방 열기 드라이런 시작 실패");
            return 1;
        }
    }
}
