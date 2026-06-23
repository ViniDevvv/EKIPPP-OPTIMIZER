using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using EkipppOptimizer.Services;

namespace EkipppOptimizer;

public partial class App : Application
{
    private static readonly string LogDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     "EKIPPP-OPTIMIZER");

    private WinForms.NotifyIcon? _notifyIcon;
    private bool _trayBalloonShown = false;

    private async void App_Startup(object sender, StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
            Dump("Fatal", ex.ExceptionObject as Exception);

        DispatcherUnhandledException += (_, ex) =>
        {
            Dump("UI", ex.Exception);
            ex.Handled = true;
        };

        TaskScheduler.UnobservedTaskException += (_, ex) =>
        {
            Dump("Task", ex.Exception);
            ex.SetObserved();
        };

        bool termsAccepted = CheckTermsAccepted();

        var splash = new SplashWindow(!termsAccepted);
        splash.Show();

        bool proceed = await splash.CompletionTask;

        if (!proceed)
        {
            Shutdown();
            return;
        }

        if (!termsAccepted)
            SaveTermsAccepted();

        // ── Vérification licence ─────────────────────────────────────────────
        var license = new LicenseService();

        if (!license.IsActivatedLocally())
        {
            var licWin = new LicenseWindow(license);
            licWin.ShowDialog();
            if (!license.IsActivatedLocally())
            {
                Shutdown();
                return;
            }
        }
        else
        {
            // Validation silencieuse en arrière-plan (révocation, expiration)
            _ = license.ValidateStoredAsync().ContinueWith(t =>
            {
                if (!t.Result)
                {
                    Dispatcher.Invoke(() =>
                    {
                        var licWin = new LicenseWindow(license);
                        licWin.ShowDialog();
                        if (!license.IsActivatedLocally()) ExplicitShutdown();
                    });
                }
            });
        }
        // ────────────────────────────────────────────────────────────────────

        InitTray();

        var main = new MainWindow();
        MainWindow = main;
        main.Show();
    }

    private static bool CheckTermsAccepted()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\EKIPPP-OPTIMIZER\App");
            return key?.GetValue("TermsAccepted") is int v && v == 1;
        }
        catch { return false; }
    }

    private static void SaveTermsAccepted()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\EKIPPP-OPTIMIZER\App");
            key.SetValue("TermsAccepted", 1);
        }
        catch { }
    }

    private void InitTray()
    {
        try
        {
            var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
            var icon    = System.Drawing.Icon.ExtractAssociatedIcon(exePath);

            var menu = new WinForms.ContextMenuStrip();
            menu.Items.Add("Ouvrir EKIPPP Optimizer", null, (_, _) => ShowMainWindow());
            menu.Items.Add(new WinForms.ToolStripSeparator());
            menu.Items.Add("Quitter", null, (_, _) => ExplicitShutdown());

            _notifyIcon = new WinForms.NotifyIcon
            {
                Icon             = icon,
                Text             = "EKIPPP Optimizer",
                Visible          = true,
                ContextMenuStrip = menu,
            };
            _notifyIcon.DoubleClick += (_, _) => ShowMainWindow();
        }
        catch { }
    }

    public void ShowTrayBalloon()
    {
        if (_trayBalloonShown || _notifyIcon == null) return;
        _trayBalloonShown = true;
        try
        {
            _notifyIcon.BalloonTipTitle = "EKIPPP Optimizer";
            _notifyIcon.BalloonTipText  = "L'app tourne en arrière-plan. Double-cliquez sur l'icône pour rouvrir.";
            _notifyIcon.ShowBalloonTip(4000);
        }
        catch { }
    }

    private void ShowMainWindow()
    {
        Dispatcher.Invoke(() =>
        {
            if (MainWindow == null) return;
            MainWindow.Show();
            MainWindow.WindowState = WindowState.Normal;
            MainWindow.Activate();
        });
    }

    internal void ExplicitShutdown()
    {
        _notifyIcon?.Dispose();
        _notifyIcon = null;
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _notifyIcon?.Dispose();
        base.OnExit(e);
    }

    private static void Dump(string kind, Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(LogDir);
            var path = Path.Combine(LogDir, "crash.log");
            var msg  = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{kind}]\n{BuildMsg(ex)}STACK:\n{ex?.StackTrace}\n\n";
            File.AppendAllText(path, msg);
        }
        catch { }
    }

    private static string BuildMsg(Exception? ex)
    {
        var sb = new System.Text.StringBuilder();
        int depth = 0;
        while (ex != null && depth++ < 6)
        {
            sb.AppendLine($"  [{ex.GetType().Name}] {ex.Message}");
            ex = ex.InnerException;
        }
        return sb.ToString();
    }
}
