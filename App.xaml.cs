using System.Threading;
using System.Windows;
using System.Windows.Threading;
using NSCheatManager.Windows.Core;
using NSCheatManager.Windows.Services;

namespace NSCheatManager.Windows;

public partial class App : Application
{
    private Mutex? _singleInstanceMutex;
    private bool _ownsMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        AppPaths.Ensure();
        AppLog.InitializeAsync().GetAwaiter().GetResult();
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        _singleInstanceMutex = new Mutex(true, "Local\\NSCheatManager.Windows.SingleInstance", out var createdNew);
        _ownsMutex = createdNew;
        if (!createdNew)
        {
            AppLog.WarningAsync("检测到第二个程序实例，已拒绝启动。").GetAwaiter().GetResult();
            Shutdown();
            return;
        }
        try
        {
            AppLog.InfoAsync("WPF 启动阶段 | 调用 base.OnStartup").GetAwaiter().GetResult();
            base.OnStartup(e);
            AppLog.InfoAsync("WPF 启动阶段 | 构造主窗口").GetAwaiter().GetResult();
            MainWindow = new MainWindow();
            AppLog.InfoAsync("WPF 启动阶段 | 显示主窗口").GetAwaiter().GetResult();
            MainWindow.Show();
            AppLog.InfoAsync("WPF 启动阶段 | 主窗口已显示").GetAwaiter().GetResult();
        }
        catch (Exception error)
        {
            AppLog.ErrorAsync("主窗口启动失败", error).GetAwaiter().GetResult();
            throw;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppLog.InfoAsync($"应用退出 | code={e.ApplicationExitCode}").GetAwaiter().GetResult();
        if (_ownsMutex) _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e) =>
        AppLog.ErrorAsync("UI 线程未处理异常", e.Exception).GetAwaiter().GetResult();

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e) =>
        AppLog.ErrorAsync($"进程未处理异常 | terminating={e.IsTerminating}", e.ExceptionObject as Exception).GetAwaiter().GetResult();

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        AppLog.ErrorAsync("未观察到的后台任务异常", e.Exception).GetAwaiter().GetResult();
        e.SetObserved();
    }
}
