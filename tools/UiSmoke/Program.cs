using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;
using NSCheatManager.Windows;
using NSCheatManager.Windows.Core;
using NSCheatManager.Windows.Protocol;
using NSCheatManager.Windows.ViewModels;

namespace UiSmoke;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var liveFtp = args.Contains("--ftp-live", StringComparer.OrdinalIgnoreCase);
        var liveDatabase = args.Contains("--database-live", StringComparer.OrdinalIgnoreCase);
        var deviceHost = args.FirstOrDefault(value => value.StartsWith("--device-host=", StringComparison.OrdinalIgnoreCase))?.Split('=', 2)[1];
        if ((liveFtp || liveDatabase) && string.IsNullOrWhiteSpace(deviceHost))
            throw new InvalidOperationException("Live UI tests require --device-host=<authorized Switch IPv4>.");
        var outputArgument = args.FirstOrDefault(value => !value.StartsWith("--", StringComparison.Ordinal));
        var output = outputArgument is null ? AppContext.BaseDirectory : Path.GetFullPath(outputArgument);
        Directory.CreateDirectory(output);
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var window = new MainWindow { ShowInTaskbar = false, Left = -10000, Top = -10000 };
        window.Show();
        Pump(TimeSpan.FromSeconds(1));
        if (window.FindName("VersionButton") is not Button { Content: string mainVersion } || mainVersion != "v1.0.2  ⓘ")
            throw new InvalidOperationException("Main-window version is not the final v1.0.2 label.");
        Render(window, Path.Combine(output, "main-window.png"));
        window.Close();

        var settingsWindow = new MainWindow { ShowInTaskbar = false, Left = -10000, Top = -10000 };
        if (settingsWindow.DataContext is MainViewModel viewModel)
        {
            viewModel.NavigateSettingsCommand.Execute(null);
        }
        settingsWindow.Show();
        Pump(TimeSpan.FromSeconds(1));
        if (settingsWindow.DataContext is not MainViewModel settingsViewModel || !settingsViewModel.FtpDefaultDirectory.StartsWith('/'))
            throw new InvalidOperationException("Global FTP default directory did not initialize as an absolute path.");
        if (settingsWindow.FindName("NewTextButton") is not Button)
            throw new InvalidOperationException("FTP new-text button is missing.");
        if (!FindVisualChildren<TextBlock>(settingsWindow).Any(text => text.Text == "TXT 文本编辑器路径（留空使用 Windows 默认程序）"))
            throw new InvalidOperationException("Settings do not explain that the TXT editor override is optional.");
        Render(settingsWindow, Path.Combine(output, "settings-page.png"));
        settingsWindow.Close();

        RenderAndAssertAboutWindow(output, english: false, "about-page.png");
        RenderAndAssertAboutWindow(output, english: true, "about-page-en.png");
        Console.WriteLine("ABOUT_WINDOW_BILINGUAL_CONTENT_OK");

        var databaseWindow = new MainWindow { ShowInTaskbar = false, Left = -10000, Top = -10000 };
        databaseWindow.Show();
        Pump(TimeSpan.FromSeconds(1));
        if (databaseWindow.DataContext is not MainViewModel databaseViewModel) throw new InvalidOperationException("Database view model is missing.");
        databaseViewModel.NavigateLibraryCommand.Execute(null);
        PumpUntil(() => databaseViewModel.IsLibraryPageVisible == Visibility.Visible, TimeSpan.FromSeconds(3));
        Pump(TimeSpan.FromMilliseconds(500));
        if (databaseWindow.FindName("SaveToDatabaseButton") is null || databaseWindow.FindName("SaveDatabaseLabelsButton") is null || databaseWindow.FindName("SaveSelectedHistoryEditButton") is null)
            throw new InvalidOperationException("Database save or label controls are missing.");
        Render(databaseWindow, Path.Combine(output, "database-page.png"));
        if (liveDatabase)
        {
            (databaseViewModel.SelectedDevice ?? throw new InvalidOperationException("No selected device for the current-game live test.")).Host = deviceHost!;
            if (databaseViewModel.ConnectionStatus != "未连接" || databaseViewModel.FtpStatus != "FTP 未连接" ||
                databaseViewModel.ConnectionButtonText != "连接" || databaseViewModel.TitleId != "—" || databaseViewModel.BuildId != "—")
                throw new InvalidOperationException("The current-game live test did not start with both sys-botbase and FTP disconnected.");

            var historyCountBeforeDownload = databaseViewModel.HistoryEntries.Count;
            var databaseRowsBeforeDownload = ReadHistoryRowCount();
            databaseViewModel.NavigateCheatsCommand.Execute(null);

            // This is deliberately the only connection-related click in this flow. It must
            // establish sys-botbase, recognize the game, connect FTP and list the scoped files.
            databaseViewModel.DownloadCommand.Execute(null);
            PumpUntil(
                () => databaseViewModel.StatusMessage.Contains("已自动识别并列出当前 TID", StringComparison.Ordinal) ||
                      databaseViewModel.ConnectionStatus == "连接失败" ||
                      databaseViewModel.CheatFtpStatus.Contains("失败", StringComparison.Ordinal),
                TimeSpan.FromSeconds(25));
            if (databaseViewModel.ConnectionStatus != "已连接 · 已识别" || databaseViewModel.CheatFtpStatus.Contains("失败", StringComparison.Ordinal))
                throw new InvalidOperationException($"One-click current-game connection/listing failed: {databaseViewModel.StatusMessage}");
            Pump(TimeSpan.FromMilliseconds(250));

            var recognizedTid = databaseViewModel.TitleId;
            var recognizedBid = databaseViewModel.BuildId;
            var expectedCheatPath = SwitchFtpClient.Combine("/switch/breeze/cheats", recognizedTid);
            if (recognizedTid == "—" || recognizedBid == "—" || databaseViewModel.MainBase is "—" or "手动模式" || databaseViewModel.HeapBase is "—" or "手动模式")
                throw new InvalidOperationException("One-click current-game flow did not retain an automatically recognized sys-botbase identity.");
            if (!databaseViewModel.FtpStatus.StartsWith("FTP 已连接", StringComparison.Ordinal) || databaseViewModel.ConnectionButtonText != "断开")
                throw new InvalidOperationException("One-click current-game flow did not establish the logical FTP connection.");
            if (!databaseViewModel.CheatRemotePath.Equals(expectedCheatPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Current-game cheat listing used the wrong TID path: {databaseViewModel.CheatRemotePath}");
            if (databaseViewModel.CheatEntries.Any(entry => !SwitchFtpClient.IsCurrentCheatFile(entry, recognizedTid, recognizedBid)))
                throw new InvalidOperationException("Current-game cheat listing exposed another BID, an unrelated TXT, a directory, a link, or another TID.");
            var allowedNames = new HashSet<string>([recognizedBid + ".txt", "notes.txt"], StringComparer.OrdinalIgnoreCase);
            if (databaseViewModel.CheatEntries.Count > 2 ||
                databaseViewModel.CheatEntries.Select(entry => entry.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != databaseViewModel.CheatEntries.Count ||
                databaseViewModel.CheatEntries.Any(entry => !allowedNames.Contains(entry.Name) || entry.IsDirectory || entry.IsLink ||
                                                         !SwitchFtpClient.Parent(entry.FullPath).Equals(expectedCheatPath, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Current-game cheat listing was not the exact BID.txt/notes.txt subset.");
            Render(databaseWindow, Path.Combine(output, "cheats-page-current-game-live.png"));
            Console.WriteLine("CURRENT_CHEAT_ONE_CLICK_SYSBOT_FTP_OK");
            Console.WriteLine("CURRENT_CHEAT_UI_SCOPE_OK");
            if (ReadHistoryRowCount() != databaseRowsBeforeDownload)
                throw new InvalidOperationException("Browsing/downloading from Switch implicitly wrote file_history.");
            databaseViewModel.NavigateLibraryCommand.Execute(null);
            Pump(TimeSpan.FromMilliseconds(500));
            if (databaseViewModel.HistoryEntries.Count != historyCountBeforeDownload)
                throw new InvalidOperationException("Browsing/downloading from Switch implicitly wrote database history.");
            Console.WriteLine("CURRENT_CHEAT_DOWNLOAD_NO_HISTORY_OK");
            var databaseRemark = "界面真机只读测试-" + DateTime.Now.ToString("HHmmssfff");
            var databasePromptTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(150) };
            databasePromptTimer.Tick += (_, _) =>
            {
                var prompt = Application.Current.Windows.OfType<TextPromptWindow>().SingleOrDefault();
                if (prompt is null) return;
                databasePromptTimer.Stop();
                if (prompt.FindName("ValueTextBox") is TextBox textBox) textBox.Text = databaseRemark;
                prompt.DialogResult = true;
            };
            databasePromptTimer.Start();
            databaseViewModel.SaveToDatabaseCommand.Execute(null);
            PumpUntil(() => databaseViewModel.StatusMessage.Contains("已写入数据库", StringComparison.Ordinal), TimeSpan.FromSeconds(12));
            if (databaseViewModel.HistoryEntries.Count <= historyCountBeforeDownload || !databaseViewModel.HistoryEntries.Any(x => x.TitleId == databaseViewModel.TitleId && x.BuildId == databaseViewModel.BuildId && x.Remark == databaseRemark))
                throw new InvalidOperationException("Database UI did not persist the required remark.");
            databaseViewModel.DatabaseGameName = "宝可梦朱紫";
            databaseViewModel.DatabaseVersionName = "版本 v4.0.0";
            databaseViewModel.SaveDatabaseLabelsCommand.Execute(null);
            PumpUntil(() => databaseViewModel.StatusMessage.Contains("游戏名与版本名已保存", StringComparison.Ordinal), TimeSpan.FromSeconds(3));
            if (!databaseViewModel.HistoryEntries.Where(x => x.TitleId == databaseViewModel.TitleId && x.BuildId == databaseViewModel.BuildId).All(x => x.GameName == "宝可梦朱紫" && x.VersionName == "版本 v4.0.0"))
                throw new InvalidOperationException("Database UI did not apply TID/BID labels.");
            Render(databaseWindow, Path.Combine(output, "database-page-live.png"));
            Console.WriteLine("DATABASE_UI_LIVE_SAVE_OK");
        }
        databaseWindow.Close();
        Console.WriteLine("DATABASE_UI_CONTROLS_OK");

        var failureWindow = new MainWindow { ShowInTaskbar = false, Left = -10000, Top = -10000 };
        failureWindow.Show();
        Pump(TimeSpan.FromSeconds(1));
        if (failureWindow.DataContext is not MainViewModel failureViewModel) throw new InvalidOperationException("Failure-path view model is missing.");
        var failureDevice = failureViewModel.SelectedDevice ?? throw new InvalidOperationException("No device is available for the failure-path test.");
        var unusedPort = ReserveUnusedLoopbackPort();
        failureDevice.AutoDetachDmnt = false;
        failureDevice.Host = IPAddress.Loopback.ToString();
        failureDevice.SysBotbasePort = unusedPort;
        failureDevice.FtpPort = unusedPort;
        failureViewModel.NavigateCheatsCommand.Execute(null);
        var staleEntry = new RemoteEntry(
            "0011223344556677.txt",
            "/switch/breeze/cheats/0100000000000000/0011223344556677.txt",
            false,
            false,
            123,
            DateTime.UtcNow);
        failureViewModel.CheatEntries.Add(staleEntry);
        failureViewModel.SelectedCheatEntry = staleEntry;
        var failureRowsBefore = ReadHistoryRowCount();
        var unexpectedManualDialog = false;
        var dialogGuard = new DispatcherTimer(DispatcherPriority.Send) { Interval = TimeSpan.FromMilliseconds(50) };
        dialogGuard.Tick += (_, _) =>
        {
            foreach (var prompt in Application.Current.Windows.OfType<TextPromptWindow>().ToArray())
            {
                unexpectedManualDialog = true;
                prompt.Close();
            }
            if (CloseNativeDialogs()) unexpectedManualDialog = true;
        };
        dialogGuard.Start();
        try
        {
            failureViewModel.DownloadCommand.Execute(null);
            PumpUntil(() => failureViewModel.ConnectionStatus == "连接失败", TimeSpan.FromSeconds(12));
            Pump(TimeSpan.FromMilliseconds(300));
        }
        finally { dialogGuard.Stop(); }
        if (unexpectedManualDialog || Application.Current.Windows.OfType<TextPromptWindow>().Any())
            throw new InvalidOperationException("Automatic current-game failure displayed a manual identity prompt.");
        if (failureViewModel.CheatEntries.Count != 0 || failureViewModel.SelectedCheatEntry is not null ||
            failureViewModel.CheatRemotePath != "/switch/breeze/cheats" || failureViewModel.TitleId != "—" ||
            failureViewModel.BuildId != "—" || failureViewModel.MainBase != "—" || failureViewModel.HeapBase != "—")
            throw new InvalidOperationException("Automatic current-game failure retained a stale identity or cheat entry.");
        if (failureViewModel.ConnectionButtonText != "连接" || failureViewModel.FtpStatus.StartsWith("FTP 已连接", StringComparison.Ordinal))
            throw new InvalidOperationException("Automatic current-game failure left FTP marked as connected.");
        if (!failureViewModel.StatusMessage.Contains("sys-botbase 自动连接或识别失败", StringComparison.Ordinal))
            throw new InvalidOperationException($"Automatic current-game failure did not report the expected error: {failureViewModel.StatusMessage}");
        if (ReadHistoryRowCount() != failureRowsBefore)
            throw new InvalidOperationException("Automatic current-game failure changed database history.");
        Render(failureWindow, Path.Combine(output, "cheats-page-auto-connect-failure.png"));
        failureWindow.Close();
        Console.WriteLine("CURRENT_CHEAT_FAILURE_NO_MANUAL_PROMPT_OK");
        Console.WriteLine("CURRENT_CHEAT_FAILURE_CLEARS_STALE_OK");
        Console.WriteLine("CURRENT_CHEAT_FAILURE_NO_HISTORY_OK");

        // Standalone FTP has its own window/session so the one-click current-game flow above
        // cannot accidentally satisfy or contaminate its independence assertions.
        var ftpWindow = new MainWindow { ShowInTaskbar = false, Left = -10000, Top = -10000 };
        ftpWindow.Show();
        Pump(TimeSpan.FromSeconds(1));
        if (ftpWindow.DataContext is not MainViewModel ftpViewModel) throw new InvalidOperationException("FTP view model is missing.");
        ftpViewModel.NavigateFilesCommand.Execute(null);
        if (ftpViewModel.ConnectionButtonText != "连接" || ftpViewModel.FtpStatus != "FTP 未连接")
            throw new InvalidOperationException("FTP connected without an explicit user action.");
        ftpViewModel.FtpDefaultDirectory = "/switch/breeze";
        SetPrivateField(ftpViewModel, "_ftpBrowserLocationInitialized", false);
        if (InvokePrivateMethod<string>(ftpViewModel, "ResolveFtpListAllPath") != "/switch/breeze")
            throw new InvalidOperationException("The initial generic FTP browse path ignored the configured default directory.");
        Console.WriteLine("FTP_DEFAULT_DIRECTORY_AFTER_CHEAT_SESSION_OK");
        Console.WriteLine("FTP_STARTUP_DISCONNECTED_OK");
        Render(ftpWindow, Path.Combine(output, "ftp-page-disconnected.png"));
        if (liveFtp)
        {
            var device = ftpViewModel.SelectedDevice ?? throw new InvalidOperationException("No selected FTP device.");
            device.Host = deviceHost!;
            device.FtpRiskAccepted = true;
            ftpViewModel.NavigateFilesCommand.Execute(null);
            ftpViewModel.ListAllFilesCommand.Execute(null);
            PumpUntil(() => ftpViewModel.FtpStatus.StartsWith("FTP 已连接", StringComparison.Ordinal) || ftpViewModel.FtpStatus.Contains("失败", StringComparison.Ordinal), TimeSpan.FromSeconds(10));
            if (ftpViewModel.RemotePath != "/switch/breeze" || !ftpViewModel.FtpStatus.StartsWith("FTP 已连接", StringComparison.Ordinal))
                throw new InvalidOperationException($"Standalone FTP list-all failed: {ftpViewModel.FtpStatus}");
            if (ftpViewModel.ConnectionStatus != "未连接" || ftpViewModel.TitleId != "—")
                throw new InvalidOperationException("Standalone FTP unexpectedly created a sys-botbase game session.");
            Console.WriteLine("FTP_SESSION_INDEPENDENT_UI_OK");

            device.FtpRiskAccepted = false;
            try
            {
                ftpViewModel.QueueDroppedFilesAsync([], "/").GetAwaiter().GetResult();
                throw new InvalidOperationException("FTP scope allowed an unapproved full-SD upload target.");
            }
            catch (InvalidOperationException error) when (error.Message.Contains("完整 SD 写入尚未授权", StringComparison.Ordinal)) { }
            var queueProbe = Path.Combine(output, "pending-endpoint-probe.txt");
            File.WriteAllText(queueProbe, "endpoint probe");
            try
            {
                var queueTask = ftpViewModel.QueueDroppedFilesAsync([queueProbe], "/switch/breeze/cheats/0100000000000000");
                PumpUntil(() => queueTask.IsCompleted, TimeSpan.FromSeconds(3));
                queueTask.GetAwaiter().GetResult();
                var queued = ftpViewModel.PendingUploads.Single(item => string.Equals(item.LocalPath, queueProbe, StringComparison.OrdinalIgnoreCase));
                if (queued.DeviceHost != device.Host || queued.DeviceFtpPort != device.FtpPort)
                    throw new InvalidOperationException("Pending upload did not capture its FTP endpoint.");
                ftpViewModel.RemovePendingCommand.Execute(queued);
                PumpUntil(() => !ftpViewModel.PendingUploads.Contains(queued), TimeSpan.FromSeconds(3));
                Pump(TimeSpan.FromMilliseconds(300));
            }
            finally { File.Delete(queueProbe); }
            Render(ftpWindow, Path.Combine(output, "ftp-list-all-auto-connected.png"));
            Console.WriteLine("FTP_CHEAT_SCOPE_GUARD_OK");
            Console.WriteLine("PENDING_ENDPOINT_CAPTURE_OK");
            Console.WriteLine("FTP_LIST_ALL_AUTO_CONNECT_OK");

            var mismatched = new NSCheatManager.Windows.Models.PendingUpload(device.Id, "endpoint-mismatch.txt", "probe", DateTimeOffset.UtcNow, "endpoint-mismatch.txt", "/switch/breeze/cheats/0100000000000000/endpoint-mismatch.txt", "192.0.2.1", 21);
            ftpViewModel.PendingUploads.Add(mismatched);
            ftpViewModel.UploadAllCommand.Execute(null);
            PumpUntil(() => ftpViewModel.StatusMessage.Contains("待上传文件属于", StringComparison.Ordinal), TimeSpan.FromSeconds(3));
            if (!ftpViewModel.PendingUploads.Contains(mismatched))
                throw new InvalidOperationException("Mismatched pending upload was removed instead of remaining pending.");
            ftpViewModel.RemovePendingCommand.Execute(mismatched);
            PumpUntil(() => !ftpViewModel.PendingUploads.Contains(mismatched), TimeSpan.FromSeconds(3));
            Pump(TimeSpan.FromMilliseconds(300));
            Console.WriteLine("PENDING_ENDPOINT_MISMATCH_BLOCKED_OK");

            var originalFtpPort = device.FtpPort;
            device.FtpRiskAccepted = true;
            device.FtpPort = originalFtpPort + 1;
            if (device.FtpRiskAccepted || ftpViewModel.ConnectionButtonText != "连接")
                throw new InvalidOperationException("Full-SD consent remained active after the FTP endpoint changed.");
            device.FtpPort = originalFtpPort;
            Console.WriteLine("FTP_CONSENT_ENDPOINT_BOUND_OK");
        }
        else
        {
            Render(ftpWindow, Path.Combine(output, "cheats-page-disconnected.png"));
            Console.WriteLine("FTP_EXPLICIT_ACTION_REQUIRED_OK");
        }
        ftpWindow.Close();

        var englishWindow = new MainWindow { ShowInTaskbar = false, Left = -10000, Top = -10000 };
        englishWindow.Show();
        Pump(TimeSpan.FromSeconds(1));
        if (englishWindow.FindName("LanguageButton") is not Button languageButton || englishWindow.DataContext is not MainViewModel englishViewModel)
            throw new InvalidOperationException("English UI controls are missing.");
        languageButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if ((englishWindow.FindName("NewTextButton") as Button)?.Content?.ToString() != "New text file")
            throw new InvalidOperationException("FTP new-text button did not switch to English.");
        if (!FindVisualChildren<TextBlock>(englishWindow).Any(text => text.Text == "TXT editor path (empty uses the Windows default app)"))
            throw new InvalidOperationException("English settings do not explain that the TXT editor override is optional.");
        englishViewModel.NavigateLibraryCommand.Execute(null);
        Pump(TimeSpan.FromMilliseconds(500));
        if ((englishWindow.FindName("SaveToDatabaseButton") as Button)?.Content?.ToString() != "Save to database" ||
            (englishWindow.FindName("SaveSelectedHistoryEditButton") as Button)?.Content?.ToString() != "Save edit")
            throw new InvalidOperationException("Database controls did not switch to English.");
        Render(englishWindow, Path.Combine(output, "database-page-en.png"));
        englishViewModel.NavigateCheatsCommand.Execute(null);
        Pump(TimeSpan.FromMilliseconds(300));
        Render(englishWindow, Path.Combine(output, "cheats-page-en.png"));
        englishWindow.Close();
        Console.WriteLine("DATABASE_ENGLISH_UI_OK");

        var promptTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(150) };
        promptTimer.Tick += (_, _) =>
        {
            var prompt = Application.Current.Windows.OfType<TextPromptWindow>().SingleOrDefault();
            if (prompt is null) return;
            promptTimer.Stop();
            prompt.DialogResult = true;
        };
        promptTimer.Start();
        if (!TextPromptWindow.TryShow("ZIP 名称", "打包为 ZIP", "smoke", out var promptValue) || promptValue != "smoke")
            throw new InvalidOperationException("Native WPF text prompt smoke test failed.");
        Console.WriteLine("WPF_PROMPT_OK");
        var englishPromptButtonsOk = false;
        var englishPromptTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(150) };
        englishPromptTimer.Tick += (_, _) =>
        {
            var prompt = Application.Current.Windows.OfType<TextPromptWindow>().SingleOrDefault();
            if (prompt is null) return;
            englishPromptTimer.Stop();
            englishPromptButtonsOk = (prompt.FindName("CancelButton") as Button)?.Content?.ToString() == "Cancel"
                                     && (prompt.FindName("AcceptButton") as Button)?.Content?.ToString() == "OK";
            prompt.DialogResult = true;
        };
        englishPromptTimer.Start();
        if (!TextPromptWindow.TryShow("Remark", "Save", "english", true, out var englishPromptValue) || englishPromptValue != "english" || !englishPromptButtonsOk)
            throw new InvalidOperationException("English WPF text prompt smoke test failed.");
        Console.WriteLine("WPF_PROMPT_ENGLISH_OK");

        var persistenceViewModel = new MainViewModel();
        var persistenceInit = persistenceViewModel.InitializeAsync();
        PumpUntil(() => persistenceInit.IsCompleted, TimeSpan.FromSeconds(5));
        persistenceInit.GetAwaiter().GetResult();
        persistenceViewModel.AddDeviceCommand.Execute(null);
        var remembered = persistenceViewModel.SelectedDevice ?? throw new InvalidOperationException("Remembered-device probe could not create a device.");
        remembered.Name = "Remembered device probe";
        persistenceViewModel.FtpDefaultDirectory = "/switch/breeze/cheats";
        persistenceViewModel.SaveDeviceCommand.Execute(null);
        PumpUntil(() => persistenceViewModel.StatusMessage.Contains("设备设置已保存", StringComparison.Ordinal), TimeSpan.FromSeconds(3));
        var persistenceShutdown = persistenceViewModel.ShutdownAsync();
        PumpUntil(() => persistenceShutdown.IsCompleted, TimeSpan.FromSeconds(5));
        persistenceShutdown.GetAwaiter().GetResult();
        var restoredViewModel = new MainViewModel();
        var restoredInit = restoredViewModel.InitializeAsync();
        PumpUntil(() => restoredInit.IsCompleted, TimeSpan.FromSeconds(5));
        restoredInit.GetAwaiter().GetResult();
        if (restoredViewModel.SelectedDevice?.Id != remembered.Id || restoredViewModel.FtpDefaultDirectory != "/switch/breeze/cheats" || restoredViewModel.ConnectionStatus != "未连接" || restoredViewModel.FtpStatus != "FTP 未连接")
            throw new InvalidOperationException("Last-selected device or global FTP default directory was not restored without connecting.");
        var restoredShutdown = restoredViewModel.ShutdownAsync();
        PumpUntil(() => restoredShutdown.IsCompleted, TimeSpan.FromSeconds(5));
        restoredShutdown.GetAwaiter().GetResult();
        Console.WriteLine("LAST_DEVICE_AND_FTP_DEFAULT_PERSISTENCE_OK");
        application.Shutdown();
    }

    private static void RenderAndAssertAboutWindow(string output, bool english, string fileName)
    {
        var about = new AboutWindow(english)
        {
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -10000,
            Top = -10000
        };
        about.Show();
        Pump(TimeSpan.FromMilliseconds(500));
        about.UpdateLayout();

        if (about.FindName("AndroidProjectButton") is not Button androidButton ||
            about.FindName("QqButton") is not Button qqButton ||
            about.FindName("WindowsProjectButton") is not Button windowsButton ||
            about.FindName("WindowsProjectUrlText") is not TextBlock windowsUrl ||
            about.FindName("VersionText") is not TextBlock aboutVersion ||
            about.FindName("QqNumber") is not TextBlock qqNumber ||
            !androidButton.IsVisible || !windowsButton.IsVisible || !qqButton.IsVisible || !windowsUrl.IsVisible || !qqNumber.IsVisible)
            throw new InvalidOperationException("About window project/community controls are missing or hidden.");

        var visibleText = FindVisualChildren<TextBlock>(about)
            .Where(text => text.IsVisible && text.ActualWidth > 0 && text.ActualHeight > 0)
            .Select(text => text.Text)
            .ToHashSet(StringComparer.Ordinal);
        var expectedGroup = english ? "Group: 457965140" : "群号：457965140";
        if (!visibleText.Contains("https://github.com/Aster145/NSCheatManager") ||
            !visibleText.Contains("NSCheatManager-Windows") ||
            !visibleText.Contains("https://github.com/Aster145/NSCheatManager-Windows") ||
            !visibleText.Contains(expectedGroup))
            throw new InvalidOperationException($"About window {(english ? "English" : "Chinese")} project/community details are incomplete.");
        if (windowsUrl.Text != "https://github.com/Aster145/NSCheatManager-Windows" ||
            aboutVersion.Text != "v1.0.2 · win-x64 · portable/self-contained" || aboutVersion.Text.Contains("preview", StringComparison.OrdinalIgnoreCase) ||
            qqNumber.Text != expectedGroup ||
            about.Title != (english ? "About" : "关于"))
            throw new InvalidOperationException($"About window {(english ? "English" : "Chinese")} localization is incorrect.");

        Render(about, Path.Combine(output, fileName));
        about.Close();
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var descendant in FindVisualChildren<T>(child)) yield return descendant;
        }
    }

    private static int ReserveUnusedLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static void SetPrivateField<T>(object target, string name, T value)
    {
        var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingFieldException(target.GetType().FullName, name);
        field.SetValue(target, value);
    }

    private static T InvokePrivateMethod<T>(object target, string name)
    {
        var method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingMethodException(target.GetType().FullName, name);
        return (T)(method.Invoke(target, null) ?? throw new InvalidOperationException($"{name} returned null."));
    }

    private static bool CloseNativeDialogs()
    {
        var found = false;
        EnumThreadWindows(GetCurrentThreadId(), (window, parameter) =>
        {
            var className = new StringBuilder(64);
            _ = GetClassName(window, className, className.Capacity);
            if (!className.ToString().Equals("#32770", StringComparison.Ordinal)) return true;
            found = true;
            _ = PostMessage(window, WmClose, IntPtr.Zero, IntPtr.Zero);
            return true;
        }, IntPtr.Zero);
        return found;
    }

    private static void Pump(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = duration };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static void PumpUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline) Pump(TimeSpan.FromMilliseconds(100));
        if (!condition()) throw new TimeoutException("Timed out waiting for the UI operation.");
    }

    private static long ReadHistoryRowCount()
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = AppPaths.Database,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
            DefaultTimeout = 5
        }.ToString();
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM file_history";
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static void Render(Window window, string path)
    {
        window.UpdateLayout();
        var width = Math.Max(1, (int)Math.Ceiling(window.ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(window.ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
        Console.WriteLine(path);
    }

    private const uint WmClose = 0x0010;
    private delegate bool EnumThreadWindowsCallback(IntPtr window, IntPtr parameter);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumThreadWindows(uint threadId, EnumThreadWindowsCallback callback, IntPtr parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder className, int maximumCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
