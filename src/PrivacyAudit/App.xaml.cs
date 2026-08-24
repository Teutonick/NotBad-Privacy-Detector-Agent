using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using PrivacyAudit.Core;

namespace PrivacyAudit;

public partial class App : System.Windows.Application
{
    static Mutex? _singleInstanceMutex;
    ProcessStartInfo? _restartStartInfo;
    public App()
    {
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(true, @"Local\NotBadPrivacyDetectorAgent", out var isFirstInstance);
        if (!isFirstInstance)
        {
            System.Windows.MessageBox.Show(
                LocalizationService.Get("SingleInstanceMessage"),
                LocalizationService.Get("AppTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown(2);
            return;
        }

        var window = new MainWindow(e.Args.Contains("--smoke-test", StringComparer.OrdinalIgnoreCase));
        MainWindow = window;
        if (e.Args.Contains("--smoke-test", StringComparer.OrdinalIgnoreCase))
        {
            window.Show();
            var smokeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            smokeTimer.Tick += (_, _) => { smokeTimer.Stop(); window.Close(); };
            smokeTimer.Start();
            return;
        }
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _singleInstanceMutex?.ReleaseMutex(); } catch (ApplicationException) { }
        _singleInstanceMutex?.Dispose();
        _singleInstanceMutex = null;
        if (_restartStartInfo is not null)
        {
            try { Process.Start(_restartStartInfo); } catch { }
            _restartStartInfo = null;
        }
        base.OnExit(e);
    }

    public void RequestRestart()
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable)) return;

        var startInfo = new ProcessStartInfo(executable) { UseShellExecute = true };
        foreach (var argument in Environment.GetCommandLineArgs().Skip(1)) startInfo.ArgumentList.Add(argument);
        _restartStartInfo = startInfo;
        Shutdown();
    }

    void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        CrashLogger.ShowErrorDialog(e.Exception, "UI Dispatcher", MainWindow);
        e.Handled = true;
    }

    void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            CrashLogger.LogException(ex, $"AppDomainUnhandledException (IsTerminating={e.IsTerminating})");
        }
    }

    void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        CrashLogger.LogException(e.Exception, "TaskSchedulerUnobservedTaskException");
        e.SetObserved();
    }
}
