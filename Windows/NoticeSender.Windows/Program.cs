using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows;

namespace NoticeSender.Windows;

internal static class Program
{
    internal static bool ModesAreCompatible(string[] args)
    {
        if (args.Contains("--bot-send-test") || args.Contains("--self-chat-send-test"))
            return !(args.Contains("--bot-send-test") && args.Contains("--self-chat-send-test")) && !args.Any(arg => arg is "--demo" or "--smoke-test" or "--diagnose-kakao" or "--dry-run-rooms");
        return !args.Contains("--dry-run-rooms") || !args.Any(arg => arg is "--demo" or "--smoke-test" or "--diagnose-kakao");
    }

    [STAThread]
    public static int Main(string[] args)
    {
        if (!ModesAreCompatible(args)) return 1;
        if (args.Contains("--bot-send-test") || args.Contains("--self-chat-send-test")) return RunBotSendTest(args);
        if (args.Contains("--dry-run-rooms")) return RunRoomDryRun(args);
        bool smoke = args.Contains("--smoke-test");
        bool demo = smoke || args.Contains("--demo");
        if (args.Contains("--diagnose-kakao")) return RunKakaoDiagnosis(smoke);
        if (smoke)
        {
            File.Delete(Path.Combine(Path.GetTempPath(), "NoticeSender-smoke-error.txt"));
            File.Delete(Path.Combine(Path.GetTempPath(), "NoticeSender-smoke-success.json"));
        }
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
                    try
                    {
                        string? renderFixtures = Environment.GetEnvironmentVariable("NOTICE_SENDER_VISUAL_FIXTURES");
                        if (!string.IsNullOrEmpty(renderFixtures)) AttachmentVisualSmokeTests.RunExpectedRendering(renderFixtures, Path.Combine(AppContext.BaseDirectory, "ocr", "tesseract.exe"), Path.Combine(AppContext.BaseDirectory, "ocr", "tessdata"));
                        if (!string.IsNullOrEmpty(renderFixtures)) AttachmentVisualSmokeTests.RunFreshRegressionFixtures(renderFixtures, Path.Combine(AppContext.BaseDirectory, "ocr", "tesseract.exe"), Path.Combine(AppContext.BaseDirectory, "ocr", "tessdata"));
                        if (!string.IsNullOrEmpty(renderFixtures)) AttachmentVisualSmokeTests.RunPngStampRegressionFixtures(renderFixtures, Path.Combine(AppContext.BaseDirectory, "ocr", "tesseract.exe"), Path.Combine(AppContext.BaseDirectory, "ocr", "tessdata"));
                        WindowsTrustFailureSmokeTests.Run(); window.VerifySmokeTest(); await RoomDryRunSmokeTests.RunAsync(); await BotSendTestSmokeTests.RunAsync(); await ReviewedBatchExecutionSmokeTests.RunAsync(); ExactTranscriptCaptureSmokeTests.Run(); ManualAttachmentReviewSmokeTests.Run();
                        string? visualFixtures = Environment.GetEnvironmentVariable("NOTICE_SENDER_VISUAL_FIXTURES");
                        if (!string.IsNullOrEmpty(visualFixtures))
                        {
                            AttachmentVisualSmokeTests.Run(visualFixtures, Path.Combine(AppContext.BaseDirectory, "ocr", "tesseract.exe"), Path.Combine(AppContext.BaseDirectory, "ocr", "tessdata"));
                            AttachmentVisualSmokeTests.RunSyntheticImage(Path.Combine(visualFixtures, "transcript-native-20261104.direct.png"), Path.Combine(visualFixtures, "synthetic-image.png"), Path.Combine(AppContext.BaseDirectory, "ocr", "tesseract.exe"), Path.Combine(AppContext.BaseDirectory, "ocr", "tessdata"));
                        }
                        File.WriteAllText(Path.Combine(Path.GetTempPath(), "NoticeSender-smoke-success.json"), JsonSerializer.Serialize(new { completed = true, exitCode = 0, syntheticOnly = true, at = DateTimeOffset.Now }));
                        app.Shutdown(0);
                    }
                    catch (Exception ex) { File.WriteAllText(Path.Combine(Path.GetTempPath(), "NoticeSender-smoke-error.txt"), ex.ToString()); app.Shutdown(2); }
                }));
            }
            return app.Run(window);
        }
        catch (Exception ex)
        {
            if (!smoke) MessageBox.Show(WindowsTrustFailure.Classify(ex)?.Message ?? "데이터를 열지 못했습니다. 기존 파일은 교체하지 않았습니다.\n\n" + ex.Message, "공지발송 시작 실패");
            return 1;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int RunKakaoDiagnosis(bool smoke)
    {
        try
        {
            string diagnostic = NativeKakaoDiagnostics.RunAsync(CancellationToken.None).GetAwaiter().GetResult();
            string path = Path.Combine(Path.GetTempPath(), "NoticeSender-kakao-diagnostics.txt");
            File.WriteAllText(path, diagnostic, Encoding.UTF8);
            if (!smoke) MessageBox.Show(diagnostic + "\n\n저장 위치: " + path, "카카오톡 읽기 전용 진단");
            return 0;
        }
        catch (Exception ex) { if (!smoke) MessageBox.Show(WindowsTrustFailure.Describe(ex), "진단 실패"); return 1; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int RunBotSendTest(string[] args)
    {
        try
        {
            var options = BotSendTestOptions.Parse(args);
            return RunBotSendTestWindow(options);
        }
        catch (Exception ex) { WriteBotStartupFailureReport(args, ex); return 1; }
    }

    // Keep optional Automation-typed window construction outside the caller's catch/JIT boundary.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int RunBotSendTestWindow(BotSendTestOptions options)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        Ui.InstallStyles(app);
        return app.Run(new BotSendTestWindow(options.Room, options.ReportPath, acknowledgeBotOnly: !options.SelfChat, autoStart: true, exitAfterTest: options.ExitAfterTest, captureEvidence: options.CaptureEvidence, replaceReviewedBotCue: options.ReplaceReviewedBotCue, onlyStep: options.OnlyStep, confirmNativeBotFiles: options.ConfirmNativeBotFiles, acknowledgeSelfChat: options.SelfChat));
    }

    internal static void WriteBotStartupFailureReport(string[] args, Exception exception)
    {
        try
        {
            int index = Array.IndexOf(args, "--report");
            if (index < 0 || index + 1 >= args.Length || args.Count(a => a == "--report") != 1 || args[index + 1].StartsWith("--", StringComparison.Ordinal)) return;
            string path = Path.GetFullPath(args[index + 1]);
            var failure = WindowsTrustFailure.Classify(exception);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string Sidecar() => path + ".unhandled-failure-" + Guid.NewGuid().ToString("N") + ".json";
            // app.Run may escape after delivery has started: never replace its trace, logs, evidence, or another existing file.
            string output = File.Exists(path) ? Sidecar() : path;
            string temporary = output + ".tmp-" + Guid.NewGuid().ToString("N");
            File.WriteAllText(temporary, JsonSerializer.Serialize(new { mode = args.Contains("--self-chat-send-test") ? "self-chat-send-test" : "bot-send-test", completed = true, success = false, cancelled = false, exitCode = 1,
                failure = WindowsTrustFailure.Describe(exception), failureKind = failure?.Kind.ToString(), errorCode = failure?.ErrorCode, helpUrl = failure?.HelpUrl,
                originalReportPath = path }, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
            try { File.Move(temporary, output, overwrite: false); }
            catch (IOException) when (File.Exists(output)) { File.Move(temporary, Sidecar(), overwrite: false); }
        }
        catch (Exception) { } // Startup failures must not block on a dialog or replace their original exit result.
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
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
            var rooms = json.RootElement.EnumerateArray().Select(e => e.GetString()!).ToArray();
            string? reportPath = Value("--report", false);
            return RunRoomDryRunWindow(rooms, reportPath, args.Contains("--exit-after-dry-run"));
        }
        catch (Exception ex)
        {
            MessageBox.Show(WindowsTrustFailure.Describe(ex), "방 열기 드라이런 시작 실패");
            return 1;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int RunRoomDryRunWindow(string[] rooms, string? reportPath, bool exitAfterFinish)
    {
        RoomDryRunRunner.ValidateRooms(rooms);
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        Ui.InstallStyles(app);
        return app.Run(new RoomDryRunWindow(rooms, reportPath, autoStart: true, exitAfterFinish: exitAfterFinish));
    }
}
