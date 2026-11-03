using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using NoticeSender.Automation;

namespace NoticeSender.Windows;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
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
                window.Loaded += (_, _) => window.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try { window.VerifySmokeTest(); app.Shutdown(0); }
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
}
