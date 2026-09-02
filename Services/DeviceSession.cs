using System.Diagnostics;
using System.IO;
using System.Text;
using NSCheatManager.Windows.Core;
using NSCheatManager.Windows.Persistence;
using NSCheatManager.Windows.Models;
using NSCheatManager.Windows.Protocol;

namespace NSCheatManager.Windows.Services;

public sealed class DeviceSession : IAsyncDisposable
{
    private SysBotbaseClient? _sysBot;
    private FileSystemWatcher? _watcher;
    private readonly Dictionary<string, FileSystemWatcher> _temporaryWatchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<long, string> _historyEditFiles = [];
    private readonly LocalDatabase _database = new(AppPaths.Database);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _suppressWatcher;
    public DeviceProfile? Device { get; private set; }
    public GameIdentity? Identity { get; private set; }
    public string DmntStatus { get; private set; } = "未请求";
    public event Action<string>? LocalFileChanged;
    public event Action<Guid, string, int, string, string>? TemporaryFileChanged;

    public async Task InitializeAsync()
    {
        AppPaths.Ensure();
        await AppLog.InitializeAsync();
        await _database.InitializeAsync();
        CleanupTemporaryFiles();
        await AppLog.InfoAsync($"本地数据初始化完成 | root={AppPaths.Root}");
    }

    public async Task<GameIdentity> ConnectAsync(DeviceProfile device, CancellationToken cancellationToken)
    {
        var sessionDevice = SnapshotDevice(device);
        await AppLog.InfoAsync($"sys-botbase 连接开始 | device={sessionDevice.Name} | host={sessionDevice.Host} | port={sessionDevice.SysBotbasePort}");
        await DisconnectAsync();
        DmntStatus = "未请求";
        if (sessionDevice.AutoDetachDmnt) { try { await NoexsClient.DetachDmntAsync(sessionDevice.Host, sessionDevice.NoexsPort, cancellationToken); DmntStatus = "已分离"; await AppLog.InfoAsync($"Noexs 自动分离成功 | host={sessionDevice.Host} | port={sessionDevice.NoexsPort}"); } catch (Exception error) { DmntStatus = "分离失败：" + error.Message; await AppLog.WarningAsync($"Noexs 自动分离失败，继续连接 sys-botbase | host={sessionDevice.Host} | port={sessionDevice.NoexsPort}", error); } }
        GameIdentity? identity = null;
        for (var attempt = 0; attempt < 2 && identity is null; attempt++)
        {
            var candidate = new SysBotbaseClient();
            try
            {
                await candidate.ConnectAsync(sessionDevice.Host, sessionDevice.SysBotbasePort, cancellationToken);
                identity = await candidate.RecognizeAsync(cancellationToken);
                _sysBot = candidate;
            }
            catch (IOException) when (attempt == 0)
            {
                await candidate.DisposeAsync();
            }
            catch
            {
                await candidate.DisposeAsync();
                _sysBot = null;
                Device = null;
                Identity = null;
                throw;
            }
        }
        if (_sysBot is null || identity is null) throw new IOException("sys-botbase connection failed.");
        Device = sessionDevice;
        Identity = identity;
        StartWatcher();
        await AppLog.InfoAsync($"sys-botbase 识别成功 | host={sessionDevice.Host} | tid={Identity.TitleId} | bid={Identity.BuildId} | main={Identity.MainBase} | heap={Identity.HeapBase}");
        return Identity;
    }

    public GameIdentity UseManualIdentity(DeviceProfile device, string titleId, string buildId)
    {
        titleId = titleId.Trim().ToUpperInvariant(); buildId = buildId.Trim().ToUpperInvariant();
        if (titleId.Length != 16 || !titleId.All(Uri.IsHexDigit)) throw new InvalidDataException("Title ID 必须是 16 位十六进制。 ");
        if (buildId.Length is < 8 or > 64 || !buildId.All(Uri.IsHexDigit)) throw new InvalidDataException("Build ID 必须是 8 到 64 位十六进制。 ");
        Device = SnapshotDevice(device);
        Identity = new GameIdentity(titleId, buildId, "手动模式", "手动模式");
        StartWatcher();
        return Identity;
    }

    public async Task DisconnectAsync()
    {
        var endpoint = Device is null ? null : $"{Device.Host}:{Device.SysBotbasePort}";
        _watcher?.Dispose(); _watcher = null;
        if (_sysBot is not null) await _sysBot.DisposeAsync();
        _sysBot = null; Identity = null; Device = null; DmntStatus = "未请求";
        if (endpoint is not null) await AppLog.InfoAsync($"设备会话已断开 | endpoint={endpoint}");
    }

    public async Task<IReadOnlyList<RemoteTextFile>> ListAsync(CancellationToken cancellationToken)
    {
        var (device, identity) = RequireSession();
        return await new SwitchFtpClient(device.Host, device.FtpPort).ListTextFilesAsync(identity.TitleId, cancellationToken);
    }

    public async Task<IReadOnlyList<RemoteEntry>> ListRemoteAsync(DeviceProfile device, string path, CancellationToken cancellationToken)
    {
        await AppLog.InfoAsync($"FTP 列表开始 | host={device.Host}:{device.FtpPort} | path={path}");
        var entries = await new SwitchFtpClient(device.Host, device.FtpPort).ListDirectoryAsync(path, cancellationToken);
        await AppLog.InfoAsync($"FTP 列表完成 | path={path} | count={entries.Count}");
        return entries;
    }

    public async Task DownloadRemoteAsync(DeviceProfile device, string remotePath, string localPath, IProgress<FluentFTP.FtpProgress>? progress, CancellationToken cancellationToken)
    {
        await AppLog.InfoAsync($"FTP 下载开始 | remote={remotePath} | local={localPath}");
        await new SwitchFtpClient(device.Host, device.FtpPort).DownloadFileAsync(remotePath, localPath, progress, cancellationToken);
        await AppLog.InfoAsync($"FTP 下载完成 | remote={remotePath} | bytes={new FileInfo(localPath).Length}");
    }

    public async Task<string> OpenRemoteAsync(DeviceProfile device, string remotePath, CancellationToken cancellationToken)
    {
        var relative = SwitchFtpClient.Normalize(remotePath).TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var local = Path.Combine(AppPaths.DeviceTemp(device.Id, device.Host, device.FtpPort), relative);
        await DownloadRemoteAsync(device, remotePath, local, null, cancellationToken);
        WatchTemporary(device, local, remotePath);
        OpenWithConfiguredProgram(local, Path.GetExtension(local).Equals(".txt", StringComparison.OrdinalIgnoreCase), device);
        return local;
    }

    public async Task UploadRemoteAsync(DeviceProfile device, string localPath, string remotePath, IProgress<FluentFTP.FtpProgress>? progress, CancellationToken cancellationToken)
    {
        await AppLog.InfoAsync($"FTP 安全上传开始 | local={localPath} | remote={remotePath} | bytes={new FileInfo(localPath).Length}");
        await new SwitchFtpClient(device.Host, device.FtpPort).UploadFileSafelyAsync(localPath, remotePath, progress, cancellationToken);
        await AppLog.InfoAsync($"FTP 安全上传完成 | remote={remotePath}");
    }

    public async Task UploadRemoteDirectAsync(DeviceProfile device, string localPath, string remotePath, CancellationToken cancellationToken)
    {
        await AppLog.WarningAsync($"FTP 直接覆盖开始 | local={localPath} | remote={remotePath} | bytes={new FileInfo(localPath).Length}");
        await new SwitchFtpClient(device.Host, device.FtpPort).UploadDirectFileWithRollbackAsync(localPath, remotePath, cancellationToken);
        await AppLog.InfoAsync($"FTP 直接覆盖完成 | remote={remotePath}");
    }

    public async Task CreateRemoteDirectoryAsync(DeviceProfile device, string path, CancellationToken cancellationToken)
    { await new SwitchFtpClient(device.Host, device.FtpPort).CreateDirectoryAsync(path, cancellationToken); }
    public async Task DeleteRemoteFileByPathAsync(DeviceProfile device, string path, CancellationToken cancellationToken)
    { await new SwitchFtpClient(device.Host, device.FtpPort).DeleteFileAsync(path, cancellationToken); }
    public async Task RenameRemoteFileAsync(DeviceProfile device, string path, string newName, CancellationToken cancellationToken)
    { await new SwitchFtpClient(device.Host, device.FtpPort).RenameFileAsync(path, newName, cancellationToken); }

    public async Task DownloadCurrentAsync(CancellationToken cancellationToken)
    {
        var (device, identity) = RequireSession();
        var ftp = new SwitchFtpClient(device.Host, device.FtpPort);
        await AppLog.InfoAsync($"下载当前金手指开始 | tid={identity.TitleId} | bid={identity.BuildId}");
        Directory.CreateDirectory(AppPaths.CheatDirectory(identity.TitleId));
        _suppressWatcher = true;
        try
        {
            await DownloadOneAsync(ftp, identity, identity.BuildId + ".txt", AppPaths.CheatFile(identity.TitleId, identity.BuildId), true, cancellationToken);
            await DownloadOneAsync(ftp, identity, "notes.txt", AppPaths.NotesFile(identity.TitleId), false, cancellationToken);
        }
        finally { _suppressWatcher = false; }
        await AppLog.InfoAsync($"下载当前金手指完成 | tid={identity.TitleId} | bid={identity.BuildId}");
    }

    public async Task DownloadFileAsync(string fileName, CancellationToken cancellationToken)
    {
        var (device, identity) = RequireSession();
        var ftp = new SwitchFtpClient(device.Host, device.FtpPort);
        var target = fileName.Equals("notes.txt", StringComparison.OrdinalIgnoreCase) ? AppPaths.NotesFile(identity.TitleId) : Path.Combine(AppPaths.CheatDirectory(identity.TitleId), fileName);
        _suppressWatcher = true;
        try { await DownloadOneAsync(ftp, identity, fileName, target, true, cancellationToken); }
        finally { _suppressWatcher = false; }
    }

    public void OpenLocal(string fileName)
    {
        var (_, identity) = RequireSession();
        if (!SwitchFtpClient.IsSafeTextName(fileName)) throw new ArgumentException("Unsafe file name.");
        var path = fileName.Equals("notes.txt", StringComparison.OrdinalIgnoreCase) ? AppPaths.NotesFile(identity.TitleId) : Path.Combine(AppPaths.CheatDirectory(identity.TitleId), fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (!File.Exists(path)) File.WriteAllText(path, string.Empty, new UTF8Encoding(false));
        OpenWithConfiguredProgram(path, true);
    }

    public string CreateLocalText(string fileName)
    {
        var (_, identity) = RequireSession();
        if (!SwitchFtpClient.IsSafeTextName(fileName)) throw new ArgumentException("文件名必须是安全的顶层 .txt 名称。", nameof(fileName));
        if (fileName.Equals(identity.BuildId + ".txt", StringComparison.OrdinalIgnoreCase) || fileName.Equals("notes.txt", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("专用文件必须通过专用入口管理。");
        var path = Path.Combine(AppPaths.CheatDirectory(identity.TitleId), fileName);
        if (File.Exists(path)) throw new IOException("本地文件已存在。");
        File.WriteAllText(path, string.Empty, new UTF8Encoding(false)); return path;
    }

    public async Task DeleteRemoteAsync(string fileName, CancellationToken cancellationToken)
    {
        var (device, identity) = RequireSession();
        await new SwitchFtpClient(device.Host, device.FtpPort).DeleteAsync(identity.TitleId, fileName, identity.BuildId, cancellationToken);
        var local = Path.Combine(AppPaths.CheatDirectory(identity.TitleId), fileName); if (File.Exists(local)) File.Delete(local);
    }

    public async Task UploadAsync(string fileName, CancellationToken cancellationToken)
    {
        var (device, identity) = RequireSession();
        var path = fileName.Equals("notes.txt", StringComparison.OrdinalIgnoreCase) ? AppPaths.NotesFile(identity.TitleId) : Path.Combine(AppPaths.CheatDirectory(identity.TitleId), fileName);
        var content = await File.ReadAllBytesAsync(path, cancellationToken);
        await new SwitchFtpClient(device.Host, device.FtpPort).UploadSafelyAsync(identity.TitleId, fileName, content, cancellationToken);
        await _database.SetBaselineAsync(device.Id, identity.TitleId, identity.BuildId, fileName, Hashing.Sha256(content));
    }

    public async Task UploadDirectAsync(string fileName, CancellationToken cancellationToken)
    {
        var (device, identity) = RequireSession();
        var path = fileName.Equals("notes.txt", StringComparison.OrdinalIgnoreCase) ? AppPaths.NotesFile(identity.TitleId) : Path.Combine(AppPaths.CheatDirectory(identity.TitleId), fileName);
        var content = await File.ReadAllBytesAsync(path, cancellationToken);
        await new SwitchFtpClient(device.Host, device.FtpPort).UploadDirectWithRollbackAsync(identity.TitleId, fileName, content, cancellationToken);
        await _database.SetBaselineAsync(device.Id, identity.TitleId, identity.BuildId, fileName, Hashing.Sha256(content));
    }

    public async Task<bool> IsChangedFromBaselineAsync(string fileName, CancellationToken cancellationToken)
    {
        var (device, identity) = RequireSession();
        var path = fileName.Equals("notes.txt", StringComparison.OrdinalIgnoreCase) ? AppPaths.NotesFile(identity.TitleId) : Path.Combine(AppPaths.CheatDirectory(identity.TitleId), fileName);
        if (!File.Exists(path)) return false;
        var current = await Hashing.Sha256FileAsync(path, cancellationToken);
        var baseline = await _database.GetBaselineAsync(device.Id, identity.TitleId, identity.BuildId, fileName);
        return baseline is null || !baseline.Equals(current, StringComparison.Ordinal);
    }

    public Task<IReadOnlyList<HistoryEntry>> ListHistoryAsync() => _database.ListHistoryAsync();

    public Task<GameMetadata> GetGameMetadataAsync(string titleId, string buildId) => _database.GetGameMetadataAsync(titleId, buildId);

    public async Task SaveGameMetadataAsync(string titleId, string buildId, string? gameName, string? versionName)
    {
        await _database.SaveGameMetadataAsync(titleId, buildId, gameName, versionName);
    }

    public async Task<DatabaseArchiveResult> SaveCurrentGameToDatabaseAsync(string remark, CancellationToken cancellationToken)
    {
        var (device, identity) = RequireSession();
        var ftp = new SwitchFtpClient(device.Host, device.FtpPort);
        await AppLog.InfoAsync($"保存当前游戏到数据库开始 | host={device.Host}:{device.FtpPort} | tid={identity.TitleId} | bid={identity.BuildId}");

        var cheatName = identity.BuildId + ".txt";
        var cheat = await ftp.DownloadAsync(identity.TitleId, cheatName, cancellationToken)
                    ?? throw new FileNotFoundException($"Switch 上未找到当前 BID 文件：{cheatName}");
        var notes = await ftp.DownloadAsync(identity.TitleId, "notes.txt", cancellationToken);
        var snapshots = new List<HistorySnapshot>
        {
            new(identity.TitleId, identity.BuildId, cheatName, cheat, Hashing.Sha256(cheat), "manual-db-save", remark)
        };
        if (notes is not null)
            snapshots.Add(new HistorySnapshot(identity.TitleId, identity.BuildId, "notes.txt", notes, Hashing.Sha256(notes), "manual-db-save", remark));

        var added = await _database.AddHistoryBatchAsync(snapshots);
        await AppLog.InfoAsync($"保存当前游戏到数据库完成 | tid={identity.TitleId} | bid={identity.BuildId} | added={added} | unchanged={snapshots.Count - added} | notes={notes is not null}");
        return new DatabaseArchiveResult(added, snapshots.Count - added, notes is not null);
    }

    public async Task RestoreAndUploadAsync(long historyId, CancellationToken cancellationToken)
    {
        var (_, identity) = RequireSession();
        if (identity.MainBase.Equals("手动模式", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("恢复并上传必须使用 sys-botbase 自动识别的游戏，不能使用手动 TID/BID。");
        var entry = (await _database.ListHistoryAsync(identity.TitleId)).FirstOrDefault(x => x.Id == historyId) ?? throw new KeyNotFoundException("History entry not found for current title.");
        if (!entry.BuildId.Equals(identity.BuildId, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("History BID does not match the current validated game.");
        if (!SwitchFtpClient.IsSafeTextName(entry.RelativePath)) throw new InvalidDataException("历史记录中的文件名不安全，已拒绝恢复。");
        var content = await _database.ReadHistoryContentAsync(historyId);
        var path = entry.RelativePath.Equals("notes.txt", StringComparison.OrdinalIgnoreCase) ? AppPaths.NotesFile(identity.TitleId) : Path.Combine(AppPaths.CheatDirectory(identity.TitleId), entry.RelativePath);
        _suppressWatcher = true; try { await File.WriteAllBytesAsync(path, content, cancellationToken); } finally { _suppressWatcher = false; }
        await UploadAsync(entry.RelativePath, cancellationToken);
    }

    public async Task<string> ScreenshotAsync(string? requestedName, CancellationToken cancellationToken)
    {
        if (_sysBot is null) throw new InvalidOperationException("Not connected.");
        var bytes = await _sysBot.CaptureScreenshotAsync(cancellationToken);
        var baseName = string.IsNullOrWhiteSpace(requestedName) ? DateTime.Now.ToString("yyyyMMdd-HHmmss") : Path.GetFileNameWithoutExtension(requestedName.Trim());
        if (string.IsNullOrWhiteSpace(baseName) || baseName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new ArgumentException("截图文件名无效。");
        var path = Path.Combine(AppPaths.Screenshots, baseName + ".jpg"); var suffix = 2;
        while (File.Exists(path)) path = Path.Combine(AppPaths.Screenshots, $"{baseName}-{suffix++}.jpg");
        await File.WriteAllBytesAsync(path, bytes, cancellationToken);
        await AppLog.InfoAsync($"截图保存完成 | path={path} | bytes={bytes.Length}");
        return path;
    }

    public async Task<string> OpenHistoryForOfflineEditAsync(long historyId, CancellationToken cancellationToken)
    {
        var entry = (await _database.ListHistoryAsync()).FirstOrDefault(x => x.Id == historyId) ?? throw new KeyNotFoundException("历史记录不存在。");
        if (!SwitchFtpClient.IsSafeTextName(entry.RelativePath)) throw new InvalidDataException("历史记录中的文件名不安全，已拒绝打开。");
        var content = await _database.ReadHistoryContentAsync(historyId);
        var directory = Path.Combine(AppPaths.Temporary, "history", historyId.ToString());
        Directory.CreateDirectory(directory);
        var local = Path.Combine(directory, entry.RelativePath);
        await File.WriteAllBytesAsync(local, content, cancellationToken);
        _historyEditFiles[historyId] = local;
        OpenWithConfiguredProgram(local, true);
        return local;
    }

    public async Task<bool> SaveHistoryEditToDatabaseAsync(long historyId, string remark, CancellationToken cancellationToken)
    {
        var entry = (await _database.ListHistoryAsync()).FirstOrDefault(x => x.Id == historyId) ?? throw new KeyNotFoundException("历史记录不存在。");
        if (!_historyEditFiles.TryGetValue(historyId, out var local) || !File.Exists(local)) throw new InvalidOperationException("请先点击“离线编辑”，在编辑器中保存文件后再写入数据库。");
        var content = await File.ReadAllBytesAsync(local, cancellationToken);
        var hash = Hashing.Sha256(content);
        if (hash.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("离线副本没有变化，无需写入数据库。");
        var added = await _database.AddEditedHistoryAsync(historyId, content, hash, remark);
        await AppLog.InfoAsync($"离线编辑手动写入数据库 | parent={historyId} | added={added} | tid={entry.TitleId} | bid={entry.BuildId} | file={entry.RelativePath}");
        return added;
    }

    public Task SetScreenAsync(bool enabled, CancellationToken cancellationToken) => _sysBot?.SetScreenEnabledAsync(enabled, cancellationToken) ?? throw new InvalidOperationException("Not connected.");
    public async Task DetachDmntAsync(CancellationToken cancellationToken) { var device = Device ?? throw new InvalidOperationException("请先选择设备。"); await AppLog.InfoAsync($"Noexs 分离开始 | host={device.Host} | port={device.NoexsPort}"); try { await NoexsClient.DetachDmntAsync(device.Host, device.NoexsPort, cancellationToken); DmntStatus = "已分离"; await AppLog.InfoAsync("Noexs 分离成功"); } catch (Exception error) { DmntStatus = "分离失败：" + error.Message; await AppLog.ErrorAsync("Noexs 分离失败", error); throw; } }

    private async Task DownloadOneAsync(SwitchFtpClient ftp, GameIdentity identity, string name, string target, bool required, CancellationToken cancellationToken)
    {
        var bytes = await ftp.DownloadAsync(identity.TitleId, name, cancellationToken);
        if (bytes is null) { if (required) throw new FileNotFoundException($"Remote {name} was not found."); return; }
        var temp = target + ".tmp"; await File.WriteAllBytesAsync(temp, bytes, cancellationToken); File.Move(temp, target, true);
        if (Device is not null) await _database.SetBaselineAsync(Device.Id, identity.TitleId, identity.BuildId, name, Hashing.Sha256(bytes));
    }

    private (DeviceProfile, GameIdentity) RequireSession() => (Device ?? throw new InvalidOperationException("No device session."), Identity ?? throw new InvalidOperationException("No validated game identity."));
    private static DeviceProfile SnapshotDevice(DeviceProfile device) => new()
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
    private void OpenWithConfiguredProgram(string path, bool text, DeviceProfile? device = null)
    {
        device ??= Device;
        if (text && device is { TextEditorPath.Length: > 0 } && File.Exists(device.TextEditorPath)) Process.Start(new ProcessStartInfo { FileName = device.TextEditorPath, Arguments = $"\"{path}\"", UseShellExecute = true });
        else Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
    }
    private void WatchTemporary(DeviceProfile device, string localPath, string remotePath)
    {
        if (_temporaryWatchers.ContainsKey(localPath)) return;
        var watcher = new FileSystemWatcher(Path.GetDirectoryName(localPath)!, Path.GetFileName(localPath)) { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size, EnableRaisingEvents = true };
        watcher.Changed += (_, _) => TemporaryFileChanged?.Invoke(device.Id, device.Host, device.FtpPort, localPath, remotePath); _temporaryWatchers[localPath] = watcher;
    }
    private static void CleanupTemporaryFiles()
    {
        if (!Directory.Exists(AppPaths.Temporary)) return;
        foreach (var file in Directory.EnumerateFiles(AppPaths.Temporary, "*", SearchOption.AllDirectories))
            if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-7)) try { File.Delete(file); } catch { }
    }
    private void StartWatcher()
    {
        if (Identity is null) return;
        var directory = AppPaths.CheatDirectory(Identity.TitleId); Directory.CreateDirectory(directory);
        _watcher = new FileSystemWatcher(directory, "*.txt") { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName, EnableRaisingEvents = true };
        _watcher.Changed += (_, e) => { if (!_suppressWatcher) LocalFileChanged?.Invoke(Path.GetFileName(e.FullPath)); };
        _watcher.Created += (_, e) => { if (!_suppressWatcher) LocalFileChanged?.Invoke(Path.GetFileName(e.FullPath)); };
        _watcher.Renamed += (_, e) => { if (!_suppressWatcher) LocalFileChanged?.Invoke(Path.GetFileName(e.FullPath)); };
    }
    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        foreach (var watcher in _temporaryWatchers.Values) watcher.Dispose();
        _temporaryWatchers.Clear();
        _historyEditFiles.Clear();
        _gate.Dispose();
    }
}

public sealed record DatabaseArchiveResult(int Added, int Unchanged, bool NotesFound);
