using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Data;
using Microsoft.Win32;
using NSCheatManager.Windows.Persistence;
using NSCheatManager.Windows.Core;
using NSCheatManager.Windows.Models;
using NSCheatManager.Windows.Protocol;
using NSCheatManager.Windows.Services;

namespace NSCheatManager.Windows.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private const string CheatRootPath = "/switch/breeze/cheats";
    private readonly DeviceSession _session = new();
    private readonly SemaphoreSlim _gameSessionGate = new(1, 1);
    private readonly SemaphoreSlim _historyRefreshGate = new(1, 1);
    private DeviceProfile? _selectedDevice;
    private string _page = "Game";
    private string _connectionStatus = "未连接";
    private Brush _connectionStatusBrush = Brushes.Gray;
    private string _statusMessage = "就绪。";
    private string _titleId = "—", _buildId = "—", _mainBase = "—", _heapBase = "—";
    private string _remotePath = "/"; private string _ftpStatus = "FTP 未连接"; private string _screenshotName = ""; private string _transferStatus = "";
    private string _cheatRemotePath = CheatRootPath; private string _cheatFtpStatus = "尚未列出";
    private string _dmntStatus = "未请求";
    private Guid? _ftpConnectedDeviceId;
    private string? _ftpConnectedHost;
    private int? _ftpConnectedPort;
    private int _ftpOperationBusy;
    private bool _english;
    private Brush _statusMessageBrush = new SolidColorBrush(Color.FromRgb(45, 55, 75));
    private HistoryEntry? _selectedHistoryEntry;
    private string _databaseTitleId = "—";
    private string _databaseBuildId = "—";
    private string _databaseGameName = "";
    private string _databaseVersionName = "";
    public ObservableCollection<DeviceProfile> Devices { get; } = [];
    public ObservableCollection<PendingUpload> PendingUploads { get; } = [];
    public ObservableCollection<RemoteTextFile> RemoteFiles { get; } = [];
    public ObservableCollection<HistoryEntry> HistoryEntries { get; } = [];
    public ObservableCollection<RemoteEntry> RemoteEntries { get; } = [];
    public ObservableCollection<RemoteEntry> CheatEntries { get; } = [];
    public RemoteTextFile? SelectedRemoteFile { get; set; }
    public HistoryEntry? SelectedHistoryEntry
    {
        get => _selectedHistoryEntry;
        set
        {
            if (!Set(ref _selectedHistoryEntry, value)) return;
            Raise(nameof(SelectedHistorySummary));
            if (value is null) return;
            DatabaseTitleId = value.TitleId;
            DatabaseBuildId = value.BuildId;
            DatabaseGameName = value.GameName;
            DatabaseVersionName = value.VersionName;
        }
    }
    public RemoteEntry? SelectedRemoteEntry { get; set; }
    public RemoteEntry? SelectedCheatEntry { get; set; }
    public string RemotePath { get => _remotePath; set => Set(ref _remotePath, value); }
    public string CheatRemotePath { get => _cheatRemotePath; private set => Set(ref _cheatRemotePath, value); }
    public string FtpStatus { get => _ftpStatus; private set => Set(ref _ftpStatus, value); }
    public string CheatFtpStatus { get => _cheatFtpStatus; private set => Set(ref _cheatFtpStatus, value); }
    public string ScreenshotName { get => _screenshotName; set => Set(ref _screenshotName, value); }
    public string TransferStatus { get => _transferStatus; private set => Set(ref _transferStatus, value); }
    public string DmntStatus { get => _dmntStatus; private set => Set(ref _dmntStatus, value); }
    public Brush StatusMessageBrush { get => _statusMessageBrush; private set => Set(ref _statusMessageBrush, value); }
    public Brush GameNavBackground => NavBackground("Game");
    public Brush CheatsNavBackground => NavBackground("Cheats");
    public Brush FtpNavBackground => NavBackground("Files");
    public Brush LibraryNavBackground => NavBackground("Library");
    public Brush ScreensNavBackground => NavBackground("Screens");
    public Brush SettingsNavBackground => NavBackground("Settings");
    public Brush GameNavForeground => NavForeground("Game");
    public Brush CheatsNavForeground => NavForeground("Cheats");
    public Brush FtpNavForeground => NavForeground("Files");
    public Brush LibraryNavForeground => NavForeground("Library");
    public Brush ScreensNavForeground => NavForeground("Screens");
    public Brush SettingsNavForeground => NavForeground("Settings");
    public DeviceProfile? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            var previous = _selectedDevice;
            var changed = Set(ref _selectedDevice, value);
            if (changed)
            {
                if (previous is not null) previous.PropertyChanged -= SelectedDeviceOnPropertyChanged;
                if (value is not null) value.PropertyChanged += SelectedDeviceOnPropertyChanged;
                if (_session.Device is not null && _session.Device.Id != value?.Id) _ = RunAsync(DisconnectAsync);
                if (_ftpConnectedDeviceId != value?.Id) ResetFtpState();
            }
            RefreshPendingFilter();
            Raise(nameof(VisiblePendingUploads));
        }
    }
    public IEnumerable<PendingUpload> VisiblePendingUploads => SelectedDevice is null ? [] : PendingUploads.Where(x => x.DeviceId == SelectedDevice.Id);
    public string ConnectionStatus { get => _connectionStatus; private set => Set(ref _connectionStatus, value); }
    public Brush ConnectionStatusBrush { get => _connectionStatusBrush; private set => Set(ref _connectionStatusBrush, value); }
    public string StatusMessage { get => _statusMessage; private set => Set(ref _statusMessage, value); }
    public string TitleId { get => _titleId; private set => Set(ref _titleId, value); }
    public string BuildId { get => _buildId; private set => Set(ref _buildId, value); }
    public string MainBase { get => _mainBase; private set => Set(ref _mainBase, value); }
    public string HeapBase { get => _heapBase; private set => Set(ref _heapBase, value); }
    public string DatabaseTitleId { get => _databaseTitleId; private set => Set(ref _databaseTitleId, value); }
    public string DatabaseBuildId { get => _databaseBuildId; private set => Set(ref _databaseBuildId, value); }
    public string DatabaseGameName { get => _databaseGameName; set => Set(ref _databaseGameName, value); }
    public string DatabaseVersionName { get => _databaseVersionName; set => Set(ref _databaseVersionName, value); }
    public string SelectedHistorySummary => SelectedHistoryEntry is null
        ? (_english ? "Select a history record" : "请选择一条历史记录")
        : $"{SelectedHistoryEntry.FileDisplay} · {SelectedHistoryEntry.CreatedAt.LocalDateTime:g} · {SelectedHistoryEntry.Size} B";
    public Visibility IsGamePageVisible => Page == "Game" ? Visibility.Visible : Visibility.Collapsed;
    public Visibility IsCheatsPageVisible => Page == "Cheats" ? Visibility.Visible : Visibility.Collapsed;
    public Visibility IsFilesPageVisible => Page == "Files" ? Visibility.Visible : Visibility.Collapsed;
    public Visibility IsLibraryPageVisible => Page == "Library" ? Visibility.Visible : Visibility.Collapsed;
    public Visibility IsScreensPageVisible => Page == "Screens" ? Visibility.Visible : Visibility.Collapsed;
    public Visibility IsSettingsPageVisible => Page == "Settings" ? Visibility.Visible : Visibility.Collapsed;
    private string Page { get => _page; set { if (Set(ref _page, value)) RaisePages(); } }

    public DelegateCommand NavigateGameCommand { get; }
    public DelegateCommand NavigateCheatsCommand { get; }
    public DelegateCommand NavigateFilesCommand { get; }
    public DelegateCommand NavigateLibraryCommand { get; }
    public DelegateCommand NavigateScreensCommand { get; }
    public DelegateCommand NavigateSettingsCommand { get; }
    public DelegateCommand ConnectCommand { get; }
    public DelegateCommand ToggleConnectionCommand { get; }
    public DelegateCommand DisconnectCommand { get; }
    public DelegateCommand SaveDeviceCommand { get; }
    public DelegateCommand AddDeviceCommand { get; }
    public DelegateCommand DeleteDeviceCommand { get; }
    public DelegateCommand OpenDataFolderCommand { get; }
    public DelegateCommand OpenLogsFolderCommand { get; }
    public DelegateCommand UploadAllCommand { get; }
    public DelegateCommand UploadOneCommand { get; }
    public DelegateCommand DownloadCommand { get; }
    public DelegateCommand DetachDmntCommand { get; }
    public DelegateCommand ScreenshotCommand { get; }
    public DelegateCommand ScreenOnCommand { get; }
    public DelegateCommand ScreenOffCommand { get; }
    public DelegateCommand ExportZipCommand { get; }
    public DelegateCommand ImportZipCommand { get; }
    public DelegateCommand OpenCheatCommand { get; }
    public DelegateCommand OpenNotesCommand { get; }
    public DelegateCommand RefreshFilesCommand { get; }
    public DelegateCommand CreateTextCommand { get; }
    public DelegateCommand OpenRemoteFileCommand { get; }
    public DelegateCommand DeleteRemoteFileCommand { get; }
    public DelegateCommand RemovePendingCommand { get; }
    public DelegateCommand RefreshHistoryCommand { get; }
    public DelegateCommand RestoreHistoryCommand { get; }
    public DelegateCommand SaveToDatabaseCommand { get; }
    public DelegateCommand SaveDatabaseLabelsCommand { get; }
    public DelegateCommand ListAllFilesCommand { get; }
    public DelegateCommand GoParentCommand { get; }
    public DelegateCommand OpenEntryCommand { get; }
    public DelegateCommand DownloadEntryCommand { get; }
    public DelegateCommand DeleteEntryCommand { get; }
    public DelegateCommand RenameEntryCommand { get; }
    public DelegateCommand CreateFolderCommand { get; }
    public DelegateCommand GoCheatParentCommand { get; }
    public DelegateCommand OpenCheatEntryCommand { get; }
    public DelegateCommand DownloadCheatEntryCommand { get; }
    public DelegateCommand OpenScreenshotsFolderCommand { get; }
    public DelegateCommand EditHistoryCommand { get; }
    public DelegateCommand SaveHistoryEditCommand { get; }
    public DelegateCommand ImportZipCommand2 => ImportZipCommand;

    public MainViewModel()
    {
        NavigateGameCommand = new(_ => NavigateTo("Game")); NavigateCheatsCommand = new(_ => NavigateTo("Cheats")); NavigateFilesCommand = new(_ => NavigateTo("Files")); NavigateLibraryCommand = new(async _ => await RunAsync(NavigateLibraryAsync)); NavigateScreensCommand = new(_ => NavigateTo("Screens")); NavigateSettingsCommand = new(_ => NavigateTo("Settings"));
        ConnectCommand = new(async _ => await RunAsync(ConnectAsync)); DisconnectCommand = new(async _ => await RunAsync(DisconnectAsync)); ToggleConnectionCommand = new(async _ => await RunFtpOperationAsync(ToggleFtpAsync));
        SaveDeviceCommand = new(async _ => await RunAsync(SaveDevicesAsync)); AddDeviceCommand = new(_ => AddDevice()); DeleteDeviceCommand = new(_ => DeleteDevice());
        OpenDataFolderCommand = new(async _ => await RunAsync(() => OpenFolderAsync(AppPaths.Root))); OpenLogsFolderCommand = new(async _ => await RunAsync(() => OpenFolderAsync(AppPaths.Logs))); UploadAllCommand = new(async _ => await RunFtpOperationAsync(UploadAllAsync)); UploadOneCommand = new(async value => await RunFtpOperationAsync(() => UploadOneAsync(value as PendingUpload)));
        DownloadCommand = new(async _ => await RunFtpOperationAsync(DownloadAsync)); DetachDmntCommand = new(async _ => await RunAsync(DetachDmntAsync)); ScreenshotCommand = new(async _ => await RunAsync(ScreenshotAsync)); ScreenOnCommand = new(async _ => await RunAsync(() => ScreenAsync(true))); ScreenOffCommand = new(async _ => await RunAsync(() => ScreenAsync(false)));
        ExportZipCommand = new(async _ => await RunFtpOperationAsync(ExportZipAsync)); ImportZipCommand = new(async _ => await RunAsync(ImportZipAsync)); OpenCheatCommand = new(_ => OpenCheat()); OpenNotesCommand = new(_ => OpenNotes());
        RefreshFilesCommand = new(async _ => await RunFtpOperationAsync(RefreshFilesAsync));
        CreateTextCommand = new(async _ => await RunAsync(CreateTextAsync));
        OpenRemoteFileCommand = new(async value => await RunFtpOperationAsync(() => OpenRemoteFileAsync(value as RemoteTextFile ?? SelectedRemoteFile)));
        DeleteRemoteFileCommand = new(async value => await RunFtpOperationAsync(() => DeleteRemoteFileAsync(value as RemoteTextFile ?? SelectedRemoteFile)));
        RemovePendingCommand = new(async value => await RunAsync(() => RemovePendingAsync(value as PendingUpload)));
        RefreshHistoryCommand = new(async _ => await RunAsync(RefreshHistoryAsync));
        RestoreHistoryCommand = new(async value => await RunFtpOperationAsync(() => RestoreHistoryAsync(value as HistoryEntry ?? SelectedHistoryEntry)));
        SaveToDatabaseCommand = new(async _ => await RunFtpOperationAsync(SaveCurrentGameToDatabaseAsync));
        SaveDatabaseLabelsCommand = new(async _ => await RunAsync(SaveDatabaseLabelsAsync));
        ListAllFilesCommand = new(async _ => await RunFtpOperationAsync(() => ListRemoteAsync(RemotePath, true)));
        GoParentCommand = new(async _ => await RunFtpOperationAsync(() => ListRemoteAsync(SwitchFtpClient.Parent(RemotePath))));
        OpenEntryCommand = new(async value => await RunFtpOperationAsync(() => OpenEntryAsync(value as RemoteEntry ?? SelectedRemoteEntry)));
        DownloadEntryCommand = new(async value => await RunFtpOperationAsync(() => DownloadEntryAsync(value as RemoteEntry ?? SelectedRemoteEntry)));
        DeleteEntryCommand = new(async value => await RunFtpOperationAsync(() => DeleteEntryAsync(value as RemoteEntry ?? SelectedRemoteEntry)));
        RenameEntryCommand = new(async value => await RunFtpOperationAsync(() => RenameEntryAsync(value as RemoteEntry ?? SelectedRemoteEntry)));
        CreateFolderCommand = new(async _ => await RunFtpOperationAsync(CreateFolderAsync));
        GoCheatParentCommand = new(async _ => await RunFtpOperationAsync(GoCheatParentAsync));
        OpenCheatEntryCommand = new(async value => await RunFtpOperationAsync(() => OpenCheatEntryAsync(value as RemoteEntry ?? SelectedCheatEntry)));
        DownloadCheatEntryCommand = new(async value => await RunFtpOperationAsync(() => DownloadCheatEntryAsync(value as RemoteEntry ?? SelectedCheatEntry)));
        OpenScreenshotsFolderCommand = new(async _ => await RunAsync(() => OpenFolderAsync(AppPaths.Screenshots)));
        EditHistoryCommand = new(async value => await RunAsync(() => EditHistoryAsync(value as HistoryEntry ?? SelectedHistoryEntry)));
        SaveHistoryEditCommand = new(async value => await RunAsync(() => SaveHistoryEditAsync(value as HistoryEntry ?? SelectedHistoryEntry)));
        _session.LocalFileChanged += file => Application.Current.Dispatcher.InvokeAsync(async () => await QueueChangedAsync(file));
        _session.TemporaryFileChanged += (deviceId, host, ftpPort, local, remote) => Application.Current.Dispatcher.InvokeAsync(async () => await QueueTemporaryAsync(deviceId, host, ftpPort, local, remote));
    }

    public async Task InitializeAsync()
    {
        AppPaths.Ensure(); await _session.InitializeAsync();
        var seenDeviceIds = new HashSet<Guid>();
        var ambiguousDeviceIds = new HashSet<Guid>();
        foreach (var device in await LoadAsync(AppPaths.Devices, new List<DeviceProfile>()))
        {
            var originalId = device.Id;
            if (originalId == Guid.Empty || !seenDeviceIds.Add(originalId))
            {
                ambiguousDeviceIds.Add(originalId);
                do { device.Id = Guid.NewGuid(); } while (!seenDeviceIds.Add(device.Id));
                await AppLog.WarningAsync($"设备档案 ID 缺失或重复，已重新分配 | device={device.Name}");
            }
            Devices.Add(device);
        }
        if (Devices.Count == 0) Devices.Add(new DeviceProfile());
        SelectedDevice = Devices[0];
        await SaveAsync(AppPaths.Devices, Devices.ToList());
        var savedPending = await LoadAsync(AppPaths.Pending, new List<PendingUpload>());
        var newlyQuarantined = savedPending.Where(x => ambiguousDeviceIds.Contains(x.DeviceId) || string.IsNullOrWhiteSpace(x.DeviceHost) || x.DeviceFtpPort is not > 0).Distinct().ToList();
        if (newlyQuarantined.Count > 0)
        {
            var existingQuarantine = await LoadAsync(AppPaths.PendingQuarantine, new List<PendingUpload>());
            await SaveAsync(AppPaths.PendingQuarantine, existingQuarantine.Concat(newlyQuarantined).Distinct().ToList());
            savedPending.RemoveAll(newlyQuarantined.Contains);
            await SaveAsync(AppPaths.Pending, savedPending);
            await AppLog.WarningAsync($"设备 ID 歧义或缺少端点快照的旧待上传项已隔离，禁止自动上传 | count={newlyQuarantined.Count} | path={AppPaths.PendingQuarantine}");
        }
        foreach (var item in savedPending) PendingUploads.Add(item);
        RefreshPendingFilter();
        StatusMessage = newlyQuarantined.Count == 0
            ? $"已加载 {Devices.Count} 个设备档案。"
            : $"已加载 {Devices.Count} 个设备档案；{newlyQuarantined.Count} 个归属不明的待上传项已安全隔离。";
        await AppLog.InfoAsync($"主界面初始化完成 | devices={Devices.Count}");
    }

    private async Task ConnectAsync()
    {
        await _gameSessionGate.WaitAsync();
        try { await ConnectCoreAsync(allowManualFallback: true); }
        finally { _gameSessionGate.Release(); }
    }

    private async Task ConnectCoreAsync(bool allowManualFallback)
    {
        var targetDevice = SnapshotFtpDevice(RequireSelectedDevice());
        ConnectionStatus = "正在连接…"; ConnectionStatusBrush = Brushes.DarkOrange;
        try
        {
            GameIdentity identity;
            try { identity = await _session.ConnectAsync(targetDevice, CancellationToken.None); }
            catch (Exception automaticError)
            {
                if (!allowManualFallback)
                    throw new InvalidOperationException(_english
                        ? $"Automatic sys-botbase recognition failed: {automaticError.Message}"
                        : $"sys-botbase 自动连接或识别失败：{automaticError.Message}", automaticError);
                if (MessageBox.Show($"自动识别失败：{automaticError.Message}\n\n是否使用手动 TID/BID 模式？手动模式不会启用截图和屏幕控制。", "自动识别失败", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) throw;
                await _session.DisconnectAsync();
                if (!TextPromptWindow.TryShow(_english ? "Enter the current game's 16-digit Title ID:" : "输入当前游戏的 16 位 Title ID：", _english ? "Manual identity" : "手动识别", "", _english, out var titleInput)) throw;
                var titleId = titleInput.Trim();
                if (titleId.Length == 0) throw;
                if (!TextPromptWindow.TryShow(_english ? "Enter the current game's Build ID:" : "输入当前游戏的 Build ID：", _english ? "Manual identity" : "手动识别", "", _english, out var buildInput)) throw;
                var buildId = buildInput.Trim();
                if (buildId.Length == 0) throw;
                identity = _session.UseManualIdentity(targetDevice, titleId, buildId);
            }
            if (!SelectedDeviceMatches(targetDevice))
                throw new InvalidOperationException(_english ? "The selected device or its ports changed during connection. Please try again." : "连接期间所选设备、IP 或端口已改变；本次识别结果已丢弃，请重试。");
            TitleId = identity.TitleId; BuildId = identity.BuildId; MainBase = identity.MainBase; HeapBase = identity.HeapBase;
            ConnectionStatus = "已连接 · 已识别"; ConnectionStatusBrush = Brushes.ForestGreen;
            StatusMessage = $"已识别 TID {TitleId}，BID {BuildId}。";
            DmntStatus = _session.DmntStatus;
            CheatEntries.Clear();
            SelectedCheatEntry = null;
            CheatRemotePath = SwitchFtpClient.Combine(CheatRootPath, TitleId);
            CheatFtpStatus = identity.MainBase.Equals("手动模式", StringComparison.OrdinalIgnoreCase) ? "手动身份不能列出当前金手指" : "尚未列出当前游戏";
            SelectedHistoryEntry = null;
            await RefreshHistoryAsync();
            await LoadDatabaseMetadataAsync(TitleId, BuildId);
        }
        catch
        {
            await _session.DisconnectAsync();
            ConnectionStatus = "连接失败";
            ConnectionStatusBrush = Brushes.Firebrick;
            TitleId = BuildId = MainBase = HeapBase = "—";
            ClearCurrentCheatView("请先连接并识别游戏");
            throw;
        }
    }

    private async Task DisconnectAsync()
    {
        await _gameSessionGate.WaitAsync();
        try
        {
            await _session.DisconnectAsync();
            ConnectionStatus = "未连接";
            ConnectionStatusBrush = Brushes.Gray;
            TitleId = BuildId = MainBase = HeapBase = "—";
            DmntStatus = "未请求";
            ClearCurrentCheatView("尚未连接");
            StatusMessage = "sys-botbase 已断开。";
        }
        finally { _gameSessionGate.Release(); }
    }
    private bool IsFtpConnected => SelectedDevice is not null
        && _ftpConnectedDeviceId == SelectedDevice.Id
        && string.Equals(_ftpConnectedHost, SelectedDevice.Host, StringComparison.OrdinalIgnoreCase)
        && _ftpConnectedPort == SelectedDevice.FtpPort;
    public string ConnectionButtonText => _english ? (IsFtpConnected ? "Disconnect" : "Connect") : (IsFtpConnected ? "断开" : "连接");
    public void SetLanguage(bool english) { _english = english; Raise(nameof(ConnectionButtonText)); Raise(nameof(SelectedHistorySummary)); }

    private DeviceProfile RequireSelectedDevice()
    {
        var device = SelectedDevice ?? throw new InvalidOperationException("请先选择设备。");
        if (Devices.Count(x => x.Id == device.Id) != 1) throw new InvalidOperationException("设备档案 ID 重复；请保存设置以修复后再连接。");
        return device;
    }

    private static DeviceProfile SnapshotFtpDevice(DeviceProfile device) => new()
    {
        Id = device.Id,
        Name = device.Name,
        Host = device.Host,
        FtpPort = device.FtpPort,
        SysBotbasePort = device.SysBotbasePort,
        NoexsPort = device.NoexsPort,
        AutoDetachDmnt = device.AutoDetachDmnt,
        TextEditorPath = device.TextEditorPath,
        FtpRiskAccepted = device.FtpRiskAccepted
    };

    private bool SelectedDeviceMatches(DeviceProfile snapshot) => SelectedDevice is not null
        && SelectedDevice.Id == snapshot.Id
        && string.Equals(SelectedDevice.Host, snapshot.Host, StringComparison.OrdinalIgnoreCase)
        && SelectedDevice.SysBotbasePort == snapshot.SysBotbasePort
        && SelectedDevice.FtpPort == snapshot.FtpPort
        && SelectedDevice.NoexsPort == snapshot.NoexsPort;

    private bool IsCurrentFtpSnapshot(DeviceProfile snapshot) => SelectedDevice is not null
        && _ftpConnectedDeviceId == snapshot.Id
        && string.Equals(_ftpConnectedHost, snapshot.Host, StringComparison.OrdinalIgnoreCase)
        && _ftpConnectedPort == snapshot.FtpPort
        && SelectedDevice.Id == snapshot.Id
        && string.Equals(SelectedDevice.Host, snapshot.Host, StringComparison.OrdinalIgnoreCase)
        && SelectedDevice.FtpPort == snapshot.FtpPort;
    private void SelectedDeviceOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not DeviceProfile device || device != SelectedDevice) return;
        var ftpEndpointChanged = e.PropertyName is nameof(DeviceProfile.Host) or nameof(DeviceProfile.FtpPort);
        var gameEndpointChanged = e.PropertyName is nameof(DeviceProfile.Host) or nameof(DeviceProfile.SysBotbasePort);
        if (ftpEndpointChanged)
        {
            device.FtpRiskAccepted = false;
            if (_ftpConnectedDeviceId == device.Id) ResetFtpState("FTP 设置已改变，请重新连接");
            StatusMessage = "设备 IP 或 FTP 端口已修改；完整 SD 授权已清除，FTP 需重新连接。";
        }
        if (gameEndpointChanged && _session.Device?.Id == device.Id && _session.Identity is not null)
        {
            var current = HasCurrentGameIdentityFor(device);
            ConnectionStatus = current ? "已连接 · 已识别" : "设置已改变 · 会话暂停";
            ConnectionStatusBrush = current ? Brushes.ForestGreen : Brushes.DarkOrange;
            if (!current)
            {
                ClearCurrentCheatView("游戏会话已失效");
                StatusMessage = "设备 IP 或 sys-botbase 端口已修改；请保存设置并重新识别游戏。";
            }
        }
    }

    private DeviceProfile RequireFtpDevice()
    {
        var device = RequireSelectedDevice();
        if (!IsFtpConnected)
        {
            ResetFtpState(_ftpConnectedDeviceId == device.Id ? "FTP 设置已改变，请重新连接" : "FTP 未连接");
            throw new InvalidOperationException("请先在 FTP 功能页点击“连接”。");
        }
        return SnapshotFtpDevice(device);
    }

    private async Task ToggleFtpAsync()
    {
        if (IsFtpConnected)
        {
            var endpoint = $"{SelectedDevice!.Host}:{SelectedDevice.FtpPort}";
            ResetFtpState();
            StatusMessage = "FTP 已断开；sys-botbase 游戏会话未改变。";
            await AppLog.InfoAsync($"FTP 逻辑会话已断开 | endpoint={endpoint}");
            return;
        }

        var device = RequireSelectedDevice();
        await ListRemoteAsync(RemotePath, true);
        if (IsFtpConnected) StatusMessage = $"FTP 已连接到 {device.Name}；未调用 sys-botbase。";
    }

    private async Task<IReadOnlyList<RemoteEntry>?> FetchFtpDirectoryAsync(string path, bool autoConnect)
    {
        path = SwitchFtpClient.Normalize(path);
        var establishingConnection = !IsFtpConnected;
        DeviceProfile snapshot;
        if (establishingConnection)
        {
            if (!autoConnect) throw new InvalidOperationException("请先在 FTP 功能页点击“连接”。");
            var selected = RequireSelectedDevice();
            if (!await ConfirmFullFtpRiskAsync(selected, path)) return null;
            snapshot = SnapshotFtpDevice(selected);
        }
        else
        {
            snapshot = RequireFtpDevice();
            if (!await ConfirmFullFtpRiskAsync(snapshot, path)) return null;
        }

        try
        {
            var entries = await _session.ListRemoteAsync(snapshot, path, CancellationToken.None);
            if (establishingConnection)
            {
                if (SelectedDevice is null || SelectedDevice.Id != snapshot.Id || !string.Equals(SelectedDevice.Host, snapshot.Host, StringComparison.OrdinalIgnoreCase) || SelectedDevice.FtpPort != snapshot.FtpPort)
                {
                    ResetFtpState("FTP 设置已改变，请重新连接");
                    StatusMessage = "连接期间设备 IP 或 FTP 端口发生变化；已丢弃本次连接结果。";
                    return null;
                }
                _ftpConnectedDeviceId = snapshot.Id;
                _ftpConnectedHost = snapshot.Host;
                _ftpConnectedPort = snapshot.FtpPort;
            }
            else if (!IsCurrentFtpSnapshot(snapshot)) return null;

            Raise(nameof(ConnectionButtonText));
            return entries;
        }
        catch
        {
            if (establishingConnection) ResetFtpState("FTP 连接失败");
            throw;
        }
    }

    private async Task<bool> ConfirmFullFtpRiskAsync(DeviceProfile device, string path)
    {
        if (path != "/" || device.FtpRiskAccepted) return true;
        var result = MessageBox.Show("完整 SD 文件管理可以覆盖、改名或删除远端文件，错误操作可能导致 CFW、游戏、存档或 Homebrew 无法使用。程序不会自动备份所有大文件。\n\n确认仅在自有或获授权设备上继续？", "完整 FTP 功能风险", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
        {
            StatusMessage = "已取消连接 FTP。";
            return false;
        }
        var storedDevice = Devices.SingleOrDefault(x => x.Id == device.Id) ?? throw new InvalidOperationException("设备档案不存在。");
        storedDevice.FtpRiskAccepted = true;
        await SaveAsync(AppPaths.Devices, Devices.ToList());
        return true;
    }

    private void ResetFtpState(string status = "FTP 未连接")
    {
        _ftpConnectedDeviceId = null;
        _ftpConnectedHost = null;
        _ftpConnectedPort = null;
        RemoteEntries.Clear();
        RemotePath = "/";
        ClearCurrentCheatView(status);
        FtpStatus = status;
        TransferStatus = "";
        Raise(nameof(ConnectionButtonText));
    }

    private async Task DownloadAsync()
    {
        await _gameSessionGate.WaitAsync();
        try
        {
            var selected = RequireSelectedDevice();
            if (!HasAutomaticallyIdentifiedGameIdentityFor(selected, requireSameFtpPort: true))
                await ConnectCoreAsync(allowManualFallback: false);

            RequireAutomaticallyIdentifiedGameSession(requireSameFtpPort: true);
            var expectedTitleId = _session.Identity!.TitleId;
            var expectedBuildId = _session.Identity.BuildId;
            var path = SwitchFtpClient.Combine(CheatRootPath, expectedTitleId);
            if (!await ListCheatDirectoryAsync(path, true, expectedTitleId, expectedBuildId)) return;
            StatusMessage = $"已自动识别并列出当前 TID {expectedTitleId}、BID {expectedBuildId} 对应的金手指与 notes.txt；未写入本地数据库。";
        }
        finally { _gameSessionGate.Release(); }
    }

    private async Task<bool> ListCheatDirectoryAsync(string path, bool autoConnect, string expectedTitleId, string expectedBuildId)
    {
        RequireAutomaticallyIdentifiedGameSession(requireSameFtpPort: true);
        var currentDirectory = SwitchFtpClient.Combine(CheatRootPath, expectedTitleId);
        path = SwitchFtpClient.Normalize(path);
        if (!path.Equals(currentDirectory, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("金手指页只允许查看当前已识别 TID/BID 对应的文件；完整目录请使用 FTP 功能页。");
        CheatEntries.Clear();
        SelectedCheatEntry = null;
        CheatRemotePath = currentDirectory;
        var wasConnected = IsFtpConnected;
        CheatFtpStatus = IsFtpConnected ? "正在列出金手指…" : "正在自动连接 FTP…";
        IReadOnlyList<RemoteEntry>? entries;
        try { entries = await FetchFtpDirectoryAsync(path, autoConnect); }
        catch
        {
            CheatFtpStatus = IsFtpConnected ? "FTP 已连接 · 列出失败" : "FTP 连接失败";
            throw;
        }
        if (entries is null) return false;

        RequireAutomaticallyIdentifiedGameSession(requireSameFtpPort: true);
        if (!_session.Identity!.TitleId.Equals(expectedTitleId, StringComparison.OrdinalIgnoreCase)
            || !_session.Identity.BuildId.Equals(expectedBuildId, StringComparison.OrdinalIgnoreCase))
        {
            ClearCurrentCheatView("游戏会话已改变，请重新列出");
            throw new InvalidOperationException("FTP 列表期间当前游戏发生变化；旧结果已丢弃。");
        }

        var visibleEntries = entries.Where(x => SwitchFtpClient.IsCurrentCheatFile(x, expectedTitleId, expectedBuildId));
        foreach (var entry in visibleEntries) CheatEntries.Add(entry);
        CheatFtpStatus = $"FTP 已连接 · 当前 TID/BID · {CheatEntries.Count} 项";
        if (!wasConnected)
            FtpStatus = RequireSelectedDevice().FtpRiskAccepted ? "FTP 已连接 · 尚未列出当前目录" : "FTP 已连接 · 仅限金手指";
        return true;
    }

    private async Task GoCheatParentAsync()
    {
        await DownloadAsync();
    }

    private async Task OpenCheatEntryAsync(RemoteEntry? entry)
    {
        var currentEntry = RequireCurrentCheatEntry(entry);
        await _session.OpenRemoteAsync(RequireFtpDevice(), currentEntry.FullPath, CancellationToken.None);
        StatusMessage = $"已在临时目录打开 {currentEntry.Name}；保存后会加入待上传区。";
    }

    private async Task DownloadCheatEntryAsync(RemoteEntry? entry)
    {
        await DownloadEntryAsync(RequireCurrentCheatEntry(entry));
    }

    private RemoteEntry RequireCurrentCheatEntry(RemoteEntry? entry)
    {
        RequireAutomaticallyIdentifiedGameSession(requireSameFtpPort: true);
        if (entry is null || !SwitchFtpClient.IsCurrentCheatFile(entry, TitleId, BuildId))
            throw new InvalidOperationException("只能操作当前已识别 BID.txt 或 notes.txt。");
        return entry;
    }
    private async Task DetachDmntAsync()
    {
        await _gameSessionGate.WaitAsync();
        try { RequireCurrentGameSession(requireSameNoexsPort: true); try { await _session.DetachDmntAsync(CancellationToken.None); } finally { DmntStatus = _session.DmntStatus; } StatusMessage = "dmnt 已分离。"; }
        finally { _gameSessionGate.Release(); }
    }
    private async Task ScreenshotAsync()
    {
        await _gameSessionGate.WaitAsync();
        try { RequireCurrentGameSession(); var path = await _session.ScreenshotAsync(ScreenshotName, CancellationToken.None); ScreenshotName = ""; StatusMessage = $"截图已保存：{path}"; }
        finally { _gameSessionGate.Release(); }
    }
    private async Task ScreenAsync(bool enabled)
    {
        await _gameSessionGate.WaitAsync();
        try { RequireCurrentGameSession(); await _session.SetScreenAsync(enabled, CancellationToken.None); StatusMessage = enabled ? "亮屏命令已发送。" : "熄屏命令已发送。"; }
        finally { _gameSessionGate.Release(); }
    }
    private void OpenCheat() { RequireCurrentGameSession(); _session.OpenLocal(BuildId + ".txt"); }
    private void OpenNotes() { RequireCurrentGameSession(); _session.OpenLocal("notes.txt"); }

    private async Task QueueChangedAsync(string file)
    {
        await Task.Delay(350);
        var device = _session.Device; var identity = _session.Identity;
        if (device is null || identity is null || !SwitchFtpClient.IsSafeTextName(file)) return;
        var path = Path.Combine(AppPaths.CheatDirectory(identity.TitleId), file); if (!File.Exists(path) || !await _session.IsChangedFromBaselineAsync(file, CancellationToken.None)) return;
        var remote = SwitchFtpClient.Combine($"/switch/breeze/cheats/{identity.TitleId}", file);
        var hash = await Hashing.Sha256FileAsync(path); var existing = PendingUploads.FirstOrDefault(x => x.DeviceId == device.Id && EndpointMatches(x, device.Host, device.FtpPort) && x.EffectiveRemotePath.Equals(remote, StringComparison.OrdinalIgnoreCase)); if (existing is not null) PendingUploads.Remove(existing);
        PendingUploads.Add(new PendingUpload(device.Id, file, hash, DateTimeOffset.Now, path, remote, device.Host, device.FtpPort)); Raise(nameof(VisiblePendingUploads)); await SavePendingAsync(); StatusMessage = $"{file} 已加入待上传区。";
    }

    private async Task QueueTemporaryAsync(Guid deviceId, string host, int ftpPort, string local, string remote)
    {
        await Task.Delay(350); if (!Devices.Any(x => x.Id == deviceId) || !File.Exists(local)) return;
        var hash = await Hashing.Sha256FileAsync(local); var existing = PendingUploads.FirstOrDefault(x => x.DeviceId == deviceId && EndpointMatches(x, host, ftpPort) && x.EffectiveRemotePath.Equals(remote, StringComparison.OrdinalIgnoreCase)); if (existing is not null) PendingUploads.Remove(existing);
        PendingUploads.Add(new PendingUpload(deviceId, remote, hash, DateTimeOffset.Now, local, remote, host, ftpPort)); Raise(nameof(VisiblePendingUploads)); await SavePendingAsync(); StatusMessage = $"{Path.GetFileName(local)} 已加入原设备的待确认上传区。";
    }

    public async Task QueueDroppedFilesAsync(IEnumerable<string> files, string? targetDirectory = null)
    {
        var device = RequireFtpDevice();
        var directory = targetDirectory ?? RemotePath;
        EnsureRemoteWriteAuthorized(device, directory);
        foreach (var local in files.Where(File.Exists))
        {
            var info = new FileInfo(local); var limit = info.Extension.Equals(".txt", StringComparison.OrdinalIgnoreCase) ? SwitchFtpClient.MaxTextBytes : SwitchFtpClient.MaxGeneralBytes;
            if (info.Length > limit) throw new InvalidDataException($"{info.Name} 超过允许大小。");
            await QueueTemporaryAsync(device.Id, device.Host, device.FtpPort, local, SwitchFtpClient.Combine(directory, info.Name));
        }
    }

    private async Task UploadAllAsync()
    {
        var device = RequireFtpDevice();
        var items = PendingUploads.Where(item => device.Id == item.DeviceId).ToList();
        foreach (var item in items)
        {
            EnsurePendingEndpointMatches(item, device);
            if (item.RemotePath is { Length: > 0 } remote) EnsureRemoteWriteAuthorized(device, remote);
        }
        foreach (var item in items) { try { await UploadOneAsync(item); } catch { throw; } }
        StatusMessage = "待上传文件已全部上传。";
    }
    private async Task UploadOneAsync(PendingUpload? item)
    {
        if (item is null) throw new InvalidOperationException("未选择待上传文件。");
        var device = RequireFtpDevice();
        if (item.DeviceId != device.Id) throw new InvalidOperationException("请先选择该文件对应的设备并连接 FTP。");
        EnsurePendingEndpointMatches(item, device);
        var isStandaloneFtpItem = item.LocalPath is { Length: > 0 } && item.RemotePath is { Length: > 0 };
        if (item.RemotePath is { Length: > 0 } remotePath) EnsureRemoteWriteAuthorized(device, remotePath);
        if (!isStandaloneFtpItem && !HasCurrentGameIdentityFor(device, requireSameFtpPort: true)) throw new InvalidOperationException("旧版待上传项需要先使用相同端点连接并识别对应游戏；可重新打开并保存文件以转换为独立 FTP 项。");
        if (!isStandaloneFtpItem && _session.Identity?.MainBase == "手动模式" && MessageBox.Show($"当前是未验证手动模式。确认上传到手动输入的 TID/BID？\nTID: {TitleId}\nBID: {BuildId}", "未验证手动模式", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            if (item.LocalPath is { Length: > 0 } local && item.RemotePath is { Length: > 0 } remote)
            {
                var progress = new Progress<FluentFTP.FtpProgress>(p => TransferStatus = $"上传 {item.DisplayName}  {p.Progress:F0}%  {p.TransferSpeedToString()}");
                await _session.UploadRemoteAsync(device, local, remote, progress, CancellationToken.None);
            }
            else
            {
                await _session.UploadAsync(item.RelativePath, CancellationToken.None);
            }
        }
        catch (FtpAtomicRenameUnavailableException)
        {
            if (MessageBox.Show("该 FTP 服务器不支持安全重命名。是否改用直接覆盖？程序会先保存远端旧内容，并在失败时尝试恢复。", "需要直接覆盖", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) throw;
            if (item.LocalPath is { Length: > 0 } local && item.RemotePath is { Length: > 0 } remote) await _session.UploadRemoteDirectAsync(device, local, remote, CancellationToken.None);
            else
            {
                if (!HasCurrentGameIdentityFor(device, requireSameFtpPort: true)) throw new InvalidOperationException("旧版待上传项需要先使用相同端点连接并识别对应游戏。");
                await _session.UploadDirectAsync(item.RelativePath, CancellationToken.None);
            }
        }
        PendingUploads.Remove(item); Raise(nameof(VisiblePendingUploads)); await SavePendingAsync(); await RefreshHistoryAsync(); StatusMessage = $"{item.RelativePath} 上传成功。";
    }

    private async Task ExportZipAsync()
    {
        RequireCurrentGameSession(requireSameFtpPort: true);
        if (!File.Exists(AppPaths.CheatFile(TitleId, BuildId)))
        {
            StatusMessage = "本地没有当前 BID 金手指，正在从 Switch 下载…";
            await _session.DownloadCurrentAsync(CancellationToken.None); await RefreshHistoryAsync();
        }
        if (!TextPromptWindow.TryShow(_english ? "Enter a ZIP name (empty cancels; a timestamped backup is also created):" : "填写 ZIP 名称（留空则取消；程序会另外生成一份时间戳备份）：", _english ? "Package as ZIP" : "打包为 ZIP", "cheats", _english, out var name)) return;
        if (string.IsNullOrWhiteSpace(name)) return;
        var result = await CheatZipService.ExportAsync(TitleId, BuildId, name, CancellationToken.None);
        OpenFolder(Path.GetDirectoryName(result.SharePath)!);
        StatusMessage = $"分享 ZIP 已生成：{result.SharePath}；时间戳备份已保存到 archives。";
    }
    private async Task ImportZipAsync() { RequireCurrentGameSession(); var dialog = new OpenFileDialog { Filter = "ZIP archive (*.zip)|*.zip" }; if (dialog.ShowDialog() != true) return; await CheatZipService.ImportAsync(dialog.FileName, TitleId, BuildId, CancellationToken.None); StatusMessage = "ZIP 已导入本地镜像；文件将进入待上传区。"; }
    private async Task SaveDevicesAsync()
    {
        ValidateDevices(); var reconnect = _session.Device is not null; var ftpWasConnected = IsFtpConnected; await SaveAsync(AppPaths.Devices, Devices.ToList());
        if (ftpWasConnected) ResetFtpState();
        if (reconnect) { await DisconnectAsync(); await ConnectAsync(); }
        StatusMessage = reconnect ? "设置已保存，并已使用新参数重新连接 sys-botbase；FTP 请重新连接。" : ftpWasConnected ? "设备设置已保存；FTP 请使用新参数重新连接。" : "设备设置已保存。";
    }

    private async Task RefreshFilesAsync()
    {
        var selectedDevice = RequireSelectedDevice();
        var path = HasCurrentGameIdentityFor(selectedDevice) ? $"/switch/breeze/cheats/{TitleId}" : RemotePath;
        FtpStatus = "正在列出文本文件…";
        IReadOnlyList<RemoteEntry>? entries;
        try { entries = await FetchFtpDirectoryAsync(path, false); }
        catch
        {
            FtpStatus = IsFtpConnected ? "FTP 已连接 · 列出失败" : "FTP 连接失败";
            throw;
        }
        if (entries is null) return;
        RemoteEntries.Clear();
        foreach (var entry in entries.Where(x => !x.IsDirectory && !x.IsLink && x.Name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))) RemoteEntries.Add(entry);
        RemotePath = SwitchFtpClient.Normalize(path); FtpStatus = $"FTP 已连接 · {RemoteEntries.Count} 个 TXT"; StatusMessage = $"已读取 {RemoteEntries.Count} 个远端 TXT 文件。";
    }

    private async Task ListRemoteAsync(string path, bool autoConnect = false)
    {
        path = SwitchFtpClient.Normalize(path);
        FtpStatus = IsFtpConnected ? "正在列出…" : "FTP 正在自动连接…";
        IReadOnlyList<RemoteEntry>? entries;
        try { entries = await FetchFtpDirectoryAsync(path, autoConnect); }
        catch
        {
            FtpStatus = IsFtpConnected ? "FTP 已连接 · 列出失败" : "FTP 连接失败";
            throw;
        }
        if (entries is null)
        {
            FtpStatus = IsFtpConnected ? "FTP 已连接 · 仅限金手指" : "FTP 未连接";
            return;
        }
        RemoteEntries.Clear();
        foreach (var entry in entries) RemoteEntries.Add(entry);
        RemotePath = path; FtpStatus = $"FTP 已连接 · {RemoteEntries.Count} 项";
        StatusMessage = $"已通过独立 FTP 列出 {RemoteEntries.Count} 项；未调用 sys-botbase。";
    }

    private async Task OpenEntryAsync(RemoteEntry? entry)
    {
        if (entry is null) throw new InvalidOperationException("请选择文件或文件夹。"); if (entry.IsLink) throw new InvalidOperationException("禁止操作链接或未知节点。");
        if (entry.IsDirectory) { await ListRemoteAsync(entry.FullPath); return; }
        await _session.OpenRemoteAsync(RequireFtpDevice(), entry.FullPath, CancellationToken.None); StatusMessage = $"已在临时目录打开 {entry.Name}。";
    }

    private async Task DownloadEntryAsync(RemoteEntry? entry)
    {
        if (entry is null || entry.IsDirectory || entry.IsLink) throw new InvalidOperationException("请选择普通文件。");
        var dialog = new SaveFileDialog { FileName = entry.Name, Filter = "All files (*.*)|*.*" }; if (dialog.ShowDialog() != true) return;
        var progress = new Progress<FluentFTP.FtpProgress>(p => TransferStatus = $"下载 {entry.Name}  {p.Progress:F0}%  {p.TransferSpeedToString()}");
        await _session.DownloadRemoteAsync(RequireFtpDevice(), entry.FullPath, dialog.FileName, progress, CancellationToken.None); StatusMessage = $"已下载：{dialog.FileName}";
    }

    private async Task DeleteEntryAsync(RemoteEntry? entry)
    {
        if (entry is null || entry.IsLink) throw new InvalidOperationException("请选择普通文件。"); if (entry.IsDirectory) throw new InvalidOperationException("禁止删除远端文件夹。");
        if (IsProtectedGameFile(entry)) throw new InvalidOperationException("当前 BID 金手指与 notes.txt 在所有视图中都禁止删除。");
        EnsureRemoteWriteAuthorized(RequireFtpDevice(), entry.FullPath);
        if (MessageBox.Show($"永久删除远端文件？\n{entry.FullPath}\n{entry.SizeText}", "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        await _session.DeleteRemoteFileByPathAsync(RequireFtpDevice(), entry.FullPath, CancellationToken.None); await ListRemoteAsync(RemotePath);
    }

    private async Task RenameEntryAsync(RemoteEntry? entry)
    {
        if (entry is null || entry.IsLink) throw new InvalidOperationException("请选择普通文件。"); if (entry.IsDirectory) throw new InvalidOperationException("禁止重命名远端文件夹。");
        if (IsProtectedGameFile(entry)) throw new InvalidOperationException("当前 BID 金手指与 notes.txt 在所有视图中都禁止重命名。");
        EnsureRemoteWriteAuthorized(RequireFtpDevice(), entry.FullPath);
        if (!TextPromptWindow.TryShow(_english ? "Enter the new file name:" : "输入新文件名：", _english ? "Rename remote file" : "重命名远端文件", entry.Name, _english, out var input)) return;
        var name = input.Trim(); if (name.Length == 0 || name == entry.Name) return;
        await _session.RenameRemoteFileAsync(RequireFtpDevice(), entry.FullPath, name, CancellationToken.None); await ListRemoteAsync(RemotePath);
    }

    private async Task CreateFolderAsync()
    {
        if (!TextPromptWindow.TryShow(_english ? "Enter the new folder name:" : "输入新文件夹名称：", _english ? "Create remote folder" : "新建远端文件夹", "New Folder", _english, out var input)) return;
        var name = input.Trim(); if (name.Length == 0) return;
        var device = RequireFtpDevice();
        var target = SwitchFtpClient.Combine(RemotePath, name);
        EnsureRemoteWriteAuthorized(device, target);
        await _session.CreateRemoteDirectoryAsync(device, target, CancellationToken.None); await ListRemoteAsync(RemotePath);
    }

    private static void EnsureRemoteWriteAuthorized(DeviceProfile device, string remotePath)
    {
        var path = SwitchFtpClient.Normalize(remotePath);
        var isCheatPath = path.Equals(CheatRootPath, StringComparison.OrdinalIgnoreCase) || path.StartsWith(CheatRootPath + "/", StringComparison.OrdinalIgnoreCase);
        if (!isCheatPath && !device.FtpRiskAccepted)
            throw new InvalidOperationException("完整 SD 写入尚未授权。请先在 FTP 功能页点击“列出全部文件”并确认风险。");
    }

    private static bool EndpointMatches(PendingUpload pending, string host, int ftpPort) =>
        !string.IsNullOrWhiteSpace(pending.DeviceHost)
        && pending.DeviceFtpPort == ftpPort
        && string.Equals(pending.DeviceHost, host, StringComparison.OrdinalIgnoreCase);

    private static void EnsurePendingEndpointMatches(PendingUpload pending, DeviceProfile device)
    {
        if (EndpointMatches(pending, device.Host, device.FtpPort)) return;
        var origin = string.IsNullOrWhiteSpace(pending.DeviceHost) || pending.DeviceFtpPort is not > 0
            ? "未知旧端点"
            : $"{pending.DeviceHost}:{pending.DeviceFtpPort}";
        throw new InvalidOperationException($"待上传文件属于 {origin}，当前 FTP 是 {device.Host}:{device.FtpPort}。为防止写错设备，已停止整个批次；请切回原端点或移除并重新加入该文件。");
    }

    private bool HasCurrentGameIdentityFor(DeviceProfile device, bool requireSameFtpPort = false, bool requireSameNoexsPort = false)
    {
        var sessionDevice = _session.Device;
        return _session.Identity is not null
            && sessionDevice?.Id == device.Id
            && string.Equals(sessionDevice.Host, device.Host, StringComparison.OrdinalIgnoreCase)
            && sessionDevice.SysBotbasePort == device.SysBotbasePort
            && (!requireSameFtpPort || sessionDevice.FtpPort == device.FtpPort)
            && (!requireSameNoexsPort || sessionDevice.NoexsPort == device.NoexsPort);
    }

    private bool HasAutomaticallyIdentifiedGameIdentityFor(DeviceProfile device, bool requireSameFtpPort = false) =>
        HasCurrentGameIdentityFor(device, requireSameFtpPort)
        && _session.Identity?.MainBase.Equals("手动模式", StringComparison.OrdinalIgnoreCase) == false;

    private DeviceProfile RequireCurrentGameSession(bool requireSameFtpPort = false, bool requireSameNoexsPort = false)
    {
        var device = RequireSelectedDevice();
        if (_session.Identity is null) throw new InvalidOperationException("请先连接并识别游戏。");
        if (!HasCurrentGameIdentityFor(device, requireSameFtpPort, requireSameNoexsPort))
            throw new InvalidOperationException("当前游戏会话使用的是旧设备端点。请先保存设置并重新连接识别，程序不会把旧会话用于新端点。");
        return device;
    }
    private DeviceProfile RequireAutomaticallyIdentifiedGameSession(bool requireSameFtpPort = false)
    {
        var device = RequireCurrentGameSession(requireSameFtpPort);
        if (!HasAutomaticallyIdentifiedGameIdentityFor(device, requireSameFtpPort))
            throw new InvalidOperationException(_english
                ? "Download from Switch requires a game identified by sys-botbase; manual TID/BID mode is not accepted."
                : "从 Switch 下载金手指需要先由 sys-botbase 自动识别当前游戏；不接受手动 TID/BID 模式。");
        return device;
    }
    private void ClearCurrentCheatView(string status)
    {
        CheatEntries.Clear();
        SelectedCheatEntry = null;
        CheatRemotePath = CheatRootPath;
        CheatFtpStatus = status;
    }
    private bool IsProtectedGameFile(RemoteEntry entry)
    {
        if (_session.Identity is null) return false;
        var directory = $"/switch/breeze/cheats/{TitleId}";
        return SwitchFtpClient.Parent(entry.FullPath).Equals(directory, StringComparison.OrdinalIgnoreCase) && (entry.Name.Equals(BuildId + ".txt", StringComparison.OrdinalIgnoreCase) || entry.Name.Equals("notes.txt", StringComparison.OrdinalIgnoreCase));
    }

    private async Task CreateTextAsync()
    {
        RequireCurrentGameSession();
        if (!TextPromptWindow.TryShow(_english ? "Enter a new file name (must end in .txt):" : "输入新文件名（必须以 .txt 结尾）：", _english ? "Create text file" : "新建文本", "new-cheat.txt", _english, out var input)) return;
        var name = input.Trim();
        if (name.Length == 0) return;
        _session.CreateLocalText(name);
        _session.OpenLocal(name);
        await QueueChangedAsync(name);
    }

    private async Task OpenRemoteFileAsync(RemoteTextFile? file)
    {
        RequireCurrentGameSession(requireSameFtpPort: true);
        if (file is null) throw new InvalidOperationException("请先选择文件。");
        await _session.DownloadFileAsync(file.Name, CancellationToken.None);
        _session.OpenLocal(file.Name);
        await RefreshHistoryAsync();
    }

    private async Task DeleteRemoteFileAsync(RemoteTextFile? file)
    {
        RequireCurrentGameSession(requireSameFtpPort: true);
        if (file is null) throw new InvalidOperationException("请先选择文件。");
        if (MessageBox.Show($"确定删除 Switch 与本地镜像中的 {file.Name}？专用文件不允许在此删除。", "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        await _session.DeleteRemoteAsync(file.Name, CancellationToken.None);
        await RefreshFilesAsync();
    }

    private async Task RemovePendingAsync(PendingUpload? item)
    {
        if (item is null) throw new InvalidOperationException("请先选择待上传文件。");
        PendingUploads.Remove(item); Raise(nameof(VisiblePendingUploads)); await SavePendingAsync(); StatusMessage = $"已从队列移除 {item.RelativePath}，本地文件未删除。";
    }

    private async Task RefreshHistoryAsync()
    {
        await _historyRefreshGate.WaitAsync();
        try
        {
            var selectedId = SelectedHistoryEntry?.Id;
            var entries = await _session.ListHistoryAsync();
            HistoryEntries.Clear();
            foreach (var entry in entries) HistoryEntries.Add(entry);
            SelectedHistoryEntry = selectedId is null ? null : HistoryEntries.FirstOrDefault(x => x.Id == selectedId.Value);
            if (SelectedHistoryEntry is not null) return;
            if (_session.Identity is not null)
            {
                SelectedHistoryEntry = HistoryEntries.FirstOrDefault(x =>
                    x.TitleId.Equals(_session.Identity.TitleId, StringComparison.OrdinalIgnoreCase) &&
                    x.BuildId.Equals(_session.Identity.BuildId, StringComparison.OrdinalIgnoreCase));
                if (SelectedHistoryEntry is null) await LoadDatabaseMetadataAsync(_session.Identity.TitleId, _session.Identity.BuildId);
                return;
            }
            if (HistoryEntries.Count > 0) SelectedHistoryEntry = HistoryEntries[0];
        }
        finally
        {
            _historyRefreshGate.Release();
        }
    }

    private async Task SaveCurrentGameToDatabaseAsync()
    {
        RequireCurrentGameSession(requireSameFtpPort: true);
        var currentCheatDirectory = SwitchFtpClient.Combine(CheatRootPath, TitleId);
        if (Page == "Cheats" && CheatRemotePath != CheatRootPath && !CheatRemotePath.Equals(currentCheatDirectory, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(_english
                ? $"The browser is showing {CheatRemotePath}, but the identified game is {TitleId}. Return to the identified TID before saving."
                : $"当前浏览的是 {CheatRemotePath}，但已识别游戏是 {TitleId}。请回到已识别 TID 后再写入数据库。");
        var prompt = _english
            ? $"Target TID: {TitleId}\nTarget BID: {BuildId}\n\nEnter a required remark:"
            : $"目标 TID：{TitleId}\n目标 BID：{BuildId}\n\n请输入本次快照备注（必填）：";
        var title = _english ? "Save to database" : "写入数据库";
        if (!TextPromptWindow.TryShow(prompt, title, "", _english, out var input)) return;
        var remark = input.Trim();
        if (remark.Length == 0) throw new InvalidOperationException(_english ? "A remark is required." : "写入数据库时必须填写备注。");
        if (remark.Length > 500 || remark.Any(char.IsControl)) throw new InvalidOperationException(_english ? "The remark must be 500 characters or fewer and contain no control characters." : "备注不能超过 500 个字符，也不能包含控制字符。");
        var result = await _session.SaveCurrentGameToDatabaseAsync(remark, CancellationToken.None);
        await RefreshHistoryAsync();
        SelectedHistoryEntry = HistoryEntries.FirstOrDefault(x =>
            x.TitleId.Equals(TitleId, StringComparison.OrdinalIgnoreCase) &&
            x.BuildId.Equals(BuildId, StringComparison.OrdinalIgnoreCase) &&
            x.Remark.Equals(remark, StringComparison.Ordinal));
        await LoadDatabaseMetadataAsync(TitleId, BuildId);
        if (_english)
            StatusMessage = $"Saved to database: {result.Added} added, {result.Unchanged} unchanged; {(result.NotesFound ? "notes.txt included" : "notes.txt not found on Switch")}.";
        else
            StatusMessage = $"已写入数据库：新增 {result.Added} 条，未变化 {result.Unchanged} 条；{(result.NotesFound ? "包含 notes.txt" : "Switch 上没有 notes.txt")}。";
    }

    private async Task SaveDatabaseLabelsAsync()
    {
        if (DatabaseTitleId == "—" || DatabaseBuildId == "—") throw new InvalidOperationException(_english ? "Connect and identify a game, or select a database record first." : "请先连接识别游戏，或在数据库列表中选择一条记录。");
        var selectedId = SelectedHistoryEntry?.Id;
        await _session.SaveGameMetadataAsync(DatabaseTitleId, DatabaseBuildId, DatabaseGameName, DatabaseVersionName);
        await RefreshHistoryAsync();
        if (selectedId is not null) SelectedHistoryEntry = HistoryEntries.FirstOrDefault(x => x.Id == selectedId.Value);
        await LoadDatabaseMetadataAsync(DatabaseTitleId, DatabaseBuildId);
        StatusMessage = _english ? "Game and version names saved; raw TID/BID values were not changed." : "游戏名与版本名已保存；TID/BID 原值未修改。";
    }

    private async Task LoadDatabaseMetadataAsync(string titleId, string buildId)
    {
        var metadata = await _session.GetGameMetadataAsync(titleId, buildId);
        DatabaseTitleId = metadata.TitleId;
        DatabaseBuildId = metadata.BuildId;
        DatabaseGameName = metadata.GameName;
        DatabaseVersionName = metadata.VersionName;
    }

    private async Task RestoreHistoryAsync(HistoryEntry? entry)
    {
        RequireCurrentGameSession(requireSameFtpPort: true);
        if (entry is null) throw new InvalidOperationException(_english ? "Select a history version." : "请选择历史版本。");
        var message = _english
            ? $"Restore {entry.FileDisplay} ({entry.CreatedAt.LocalDateTime:g}) and upload it to Switch immediately?"
            : $"恢复 {entry.FileDisplay}（{entry.CreatedAt.LocalDateTime:g}）并立即上传到 Switch？";
        if (MessageBox.Show(message, _english ? "Confirm restore" : "确认恢复", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        await _session.RestoreAndUploadAsync(entry.Id, CancellationToken.None);
        await RefreshHistoryAsync();
        StatusMessage = _english ? $"Restored and uploaded {entry.FileDisplay}." : $"已恢复并上传 {entry.FileDisplay}。";
    }
    private async Task EditHistoryAsync(HistoryEntry? entry)
    {
        if (entry is null) throw new InvalidOperationException(_english ? "Select a history version." : "请选择历史版本。");
        await _session.OpenHistoryForOfflineEditAsync(entry.Id, CancellationToken.None);
        StatusMessage = _english
            ? "Offline copy opened. Editor saves do not change the database; click Save edit to database when finished."
            : "已打开离线副本；编辑器保存不会改数据库，完成后请点击“保存编辑到数据库”。";
    }

    private async Task SaveHistoryEditAsync(HistoryEntry? entry)
    {
        if (entry is null) throw new InvalidOperationException(_english ? "Select a history version." : "请选择历史版本。");
        var prompt = _english
            ? $"File: {entry.FileDisplay}\nTID: {entry.TitleId}\nBID: {entry.BuildId}\n\nEnter a required remark for the edited copy:"
            : $"文件：{entry.FileDisplay}\nTID：{entry.TitleId}\nBID：{entry.BuildId}\n\n请输入编辑副本的备注（必填）：";
        if (!TextPromptWindow.TryShow(prompt, _english ? "Save edit to database" : "保存编辑到数据库", "", _english, out var input)) return;
        var remark = input.Trim();
        if (remark.Length == 0) throw new InvalidOperationException(_english ? "A remark is required." : "写入数据库时必须填写备注。");
        if (remark.Length > 500 || remark.Any(char.IsControl)) throw new InvalidOperationException(_english ? "The remark must be 500 characters or fewer and contain no control characters." : "备注不能超过 500 个字符，也不能包含控制字符。");
        var added = await _session.SaveHistoryEditToDatabaseAsync(entry.Id, remark, CancellationToken.None);
        await RefreshHistoryAsync();
        SelectedHistoryEntry = HistoryEntries.FirstOrDefault(x =>
            x.TitleId.Equals(entry.TitleId, StringComparison.OrdinalIgnoreCase) &&
            x.BuildId.Equals(entry.BuildId, StringComparison.OrdinalIgnoreCase) &&
            x.RelativePath.Equals(entry.RelativePath, StringComparison.OrdinalIgnoreCase) &&
            x.Remark.Equals(remark, StringComparison.Ordinal));
        StatusMessage = added
            ? (_english ? "Edited copy saved as a new database version." : "编辑副本已作为新的数据库版本写入。")
            : (_english ? "The same edited content and remark already exist; no duplicate was added." : "相同的编辑内容和备注已存在，未重复写入。");
    }
    private void AddDevice() { var item = new DeviceProfile { Name = "新 Switch", Host = "192.168.1.100" }; Devices.Add(item); SelectedDevice = item; }
    private void DeleteDevice() { if (SelectedDevice is null || Devices.Count <= 1) throw new InvalidOperationException("至少保留一个设备档案。"); Devices.Remove(SelectedDevice); SelectedDevice = Devices[0]; }
    private void ValidateDevices()
    {
        if (Devices.Any(x => x.Id == Guid.Empty) || Devices.Select(x => x.Id).Distinct().Count() != Devices.Count) throw new InvalidOperationException("设备档案 ID 缺失或重复。");
        foreach (var d in Devices)
            if (string.IsNullOrWhiteSpace(d.Name) || !System.Net.IPAddress.TryParse(d.Host, out _) || d.SysBotbasePort is < 1 or > 65535 || d.FtpPort is < 1 or > 65535 || d.NoexsPort is < 1 or > 65535)
                throw new InvalidOperationException("设备名称、IPv4 或端口无效。");
    }

    private async Task RunFtpOperationAsync(Func<Task> action, [CallerArgumentExpression(nameof(action))] string operation = "")
    {
        if (Interlocked.CompareExchange(ref _ftpOperationBusy, 1, 0) != 0)
        {
            StatusMessage = "已有 FTP 操作正在进行，请等待完成。";
            StatusMessageBrush = Brushes.DarkOrange;
            await AppLog.WarningAsync($"忽略并发 FTP 操作 | {operation}");
            return;
        }

        try
        {
            await RunAsync(async () =>
            {
                try { await action(); }
                catch (Exception error)
                {
                    if (IsFtpConnectionFailure(error)) ResetFtpState("FTP 连接已失效");
                    throw;
                }
            }, operation);
        }
        finally { Interlocked.Exchange(ref _ftpOperationBusy, 0); }
    }

    private static bool IsFtpConnectionFailure(Exception error)
    {
        if (error is AggregateException aggregate) return aggregate.InnerExceptions.Any(IsFtpConnectionFailure);
        if (error is TimeoutException or System.Net.Sockets.SocketException) return true;
        if (error is FluentFTP.Exceptions.FtpException and not FluentFTP.Exceptions.FtpCommandException) return true;
        if (error is IOException and not DirectoryNotFoundException and not FileNotFoundException and not FtpAtomicRenameUnavailableException) return true;
        return error.InnerException is not null && IsFtpConnectionFailure(error.InnerException);
    }

    private async Task RunAsync(Func<Task> action, [CallerArgumentExpression(nameof(action))] string operation = "")
    {
        var timer = Stopwatch.StartNew();
        await AppLog.InfoAsync($"操作开始 | {operation}");
        try
        {
            StatusMessageBrush = new SolidColorBrush(Color.FromRgb(45, 55, 75));
            await action();
            await AppLog.InfoAsync($"操作成功 | {operation} | elapsedMs={timer.ElapsedMilliseconds}");
        }
        catch (Exception error)
        {
            StatusMessage = error.Message;
            StatusMessageBrush = Brushes.Firebrick;
            await AppLog.ErrorAsync($"操作失败 | {operation} | elapsedMs={timer.ElapsedMilliseconds}", error);
        }
        finally { Raise(nameof(ConnectionButtonText)); }
    }
    private static void OpenFolder(string path) => Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
    private static Task OpenFolderAsync(string path) { OpenFolder(path); return Task.CompletedTask; }
    private async Task NavigateLibraryAsync() { Page = "Library"; await RefreshHistoryAsync(); }
    private void NavigateTo(string page) { Page = page; _ = AppLog.InfoAsync($"页面切换 | page={page}"); }
    private static async Task<T> LoadAsync<T>(string path, T fallback) { if (!File.Exists(path)) return fallback; await using var stream = File.OpenRead(path); return await JsonSerializer.DeserializeAsync<T>(stream) ?? fallback; }
    private static async Task SaveAsync<T>(string path, T value) { var temp = path + ".tmp"; await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true })); File.Move(temp, path, true); }
    private Task SavePendingAsync() => SaveAsync(AppPaths.Pending, PendingUploads.ToList());
    private void RefreshPendingFilter()
    {
        var view = CollectionViewSource.GetDefaultView(PendingUploads); view.Filter = item => item is PendingUpload pending && SelectedDevice?.Id == pending.DeviceId; view.Refresh();
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null) { if (EqualityComparer<T>.Default.Equals(field, value)) return false; field = value; PropertyChanged?.Invoke(this, new(name)); return true; }
    private void Raise(string name) => PropertyChanged?.Invoke(this, new(name));
    private void RaisePages() { foreach (var name in new[] { nameof(IsGamePageVisible), nameof(IsCheatsPageVisible), nameof(IsFilesPageVisible), nameof(IsLibraryPageVisible), nameof(IsScreensPageVisible), nameof(IsSettingsPageVisible), nameof(GameNavBackground), nameof(CheatsNavBackground), nameof(FtpNavBackground), nameof(LibraryNavBackground), nameof(ScreensNavBackground), nameof(SettingsNavBackground), nameof(GameNavForeground), nameof(CheatsNavForeground), nameof(FtpNavForeground), nameof(LibraryNavForeground), nameof(ScreensNavForeground), nameof(SettingsNavForeground) }) PropertyChanged?.Invoke(this, new(name)); }
    private Brush NavBackground(string page) => Page == page ? new SolidColorBrush(Color.FromRgb(214, 228, 255)) : Brushes.Transparent;
    private Brush NavForeground(string page) => Page == page ? new SolidColorBrush(Color.FromRgb(22, 70, 150)) : new SolidColorBrush(Color.FromRgb(30, 38, 55));
    public async Task ShutdownAsync() => await _session.DisposeAsync();
}
