using NSCheatManager.Windows.Models;
using NSCheatManager.Windows.Services;
using NSCheatManager.Windows.Persistence;
using NSCheatManager.Windows.Protocol;
using Microsoft.Data.Sqlite;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

var host = args.FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.Ordinal)) ?? IPAddress.Loopback.ToString();
var localOnly = args.Contains("--local", StringComparer.OrdinalIgnoreCase);
var ftpOnly = args.Contains("--ftp-only", StringComparer.OrdinalIgnoreCase);
var databaseLive = args.Contains("--database-live", StringComparer.OrdinalIgnoreCase);
var session = new DeviceSession();
await session.InitializeAsync();
try
{
    var failedSession = new DeviceSession();
    await failedSession.InitializeAsync();
    try
    {
        var unusedPort = ReserveUnusedLoopbackPort();
        var failedAsExpected = false;
        try
        {
            await failedSession.ConnectAsync(new DeviceProfile
            {
                Name = "Closed loopback endpoint",
                Host = IPAddress.Loopback.ToString(),
                SysBotbasePort = unusedPort,
                FtpPort = unusedPort,
                AutoDetachDmnt = false
            }, CancellationToken.None);
        }
        catch { failedAsExpected = true; }
        if (!failedAsExpected) throw new Exception("Closed loopback sys-botbase endpoint unexpectedly connected");
        if (failedSession.Device is not null || failedSession.Identity is not null)
            throw new Exception("Failed sys-botbase connection retained a stale device or identity");
        Console.WriteLine("SYSBOT_FAILURE_STATE_ROLLBACK_OK");
    }
    finally { await failedSession.DisposeAsync(); }

    if (!localOnly && !ftpOnly) try
    {
        var identity = await session.ConnectAsync(new DeviceProfile { Name = "Smoke Test", Host = host, AutoDetachDmnt = false }, CancellationToken.None);
        Console.WriteLine($"TID={identity.TitleId}"); Console.WriteLine($"BID={identity.BuildId}"); Console.WriteLine($"MAIN={identity.MainBase}"); Console.WriteLine($"HEAP={identity.HeapBase}");
        if (databaseLive)
        {
            const string liveRemark = "只读真机数据库功能测试";
            var result = await session.SaveCurrentGameToDatabaseAsync(liveRemark, CancellationToken.None);
            var saved = (await session.ListHistoryAsync()).Where(x => x.TitleId == identity.TitleId && x.BuildId == identity.BuildId && x.Remark == liveRemark).ToList();
            if (saved.Count == 0 || result.Added + result.Unchanged == 0) throw new Exception("Live database snapshot was not saved");
            Console.WriteLine($"DATABASE_LIVE_SAVE_OK={result.Added}|{result.Unchanged}|notes={result.NotesFound}");
        }
    }
    catch (Exception error) { Console.WriteLine($"SYSBOT_ERROR={error.Message}"); }
    if (!localOnly) try
    {
        var ftpDevice = new DeviceProfile { Name = "FTP-only Smoke Test", Host = host, FtpPort = 21, AutoDetachDmnt = false };
        var entries = await session.ListRemoteAsync(ftpDevice, "/switch/breeze/cheats", CancellationToken.None);
        Console.WriteLine($"FTP_ENTRY_COUNT={entries.Count}"); foreach (var entry in entries) Console.WriteLine($"FTP_ENTRY={entry.Name}|{entry.Size}");
        if (ftpOnly && (session.Device is not null || session.Identity is not null)) throw new Exception("Standalone FTP unexpectedly created a sys-botbase session");
        if (ftpOnly) Console.WriteLine("FTP_SESSION_INDEPENDENT=OK");
    }
    catch (Exception error)
    {
        Console.WriteLine($"FTP_ERROR={error.Message}");
        if (ftpOnly) throw;
    }
    if (args.Contains("--noexs", StringComparer.OrdinalIgnoreCase)) try
    {
        await NSCheatManager.Windows.Protocol.NoexsClient.DetachDmntAsync(host, 7331, CancellationToken.None);
        Console.WriteLine("NOEXS_DETACH=OK");
    }
    catch (Exception error) { Console.WriteLine($"NOEXS_ERROR={error.Message}"); }
    var tid = "0100000000000000"; var bid = "0011223344556677";
    Directory.CreateDirectory(NSCheatManager.Windows.Core.AppPaths.CheatDirectory(tid));
    await File.WriteAllTextAsync(NSCheatManager.Windows.Core.AppPaths.CheatFile(tid, bid), "[Smoke]\n04000000 00000000 00000000\n");
    await File.WriteAllTextAsync(NSCheatManager.Windows.Core.AppPaths.NotesFile(tid), "smoke notes");
    var zip = await NSCheatManager.Windows.Services.CheatZipService.ExportAsync(tid, bid, "smoke.zip", CancellationToken.None);
    if (!File.Exists(zip.SharePath) || !File.Exists(zip.BackupPath)) throw new Exception("ZIP share or backup file is missing");
    if (!Path.GetDirectoryName(zip.SharePath)!.Equals(NSCheatManager.Windows.Core.AppPaths.Exports, StringComparison.OrdinalIgnoreCase)) throw new Exception("Share ZIP was not isolated in the exports directory");
    if (!Path.GetDirectoryName(zip.BackupPath)!.Equals(NSCheatManager.Windows.Core.AppPaths.Archives, StringComparison.OrdinalIgnoreCase)) throw new Exception("Backup ZIP was not isolated in the archives directory");
    using (var archive = ZipFile.OpenRead(zip.SharePath))
    {
        var names = archive.Entries.Select(entry => entry.FullName).OrderBy(x => x).ToArray();
        var expected = new[] { $"atmosphere/contents/{tid}/cheats/{bid}.txt", $"atmosphere/contents/{tid}/cheats/notes.txt" }.OrderBy(x => x).ToArray();
        if (!names.SequenceEqual(expected)) throw new Exception("ZIP entries do not match the expected Atmosphere layout");
    }
    Console.WriteLine($"ZIP_SHARE_OK={zip.SharePath}");
    Console.WriteLine($"ZIP_BACKUP_OK={zip.BackupPath}");
    if (NSCheatManager.Windows.Protocol.SwitchFtpClient.Normalize("switch/breeze") != "/switch/breeze") throw new Exception("FTP normalize failed");
    if (NSCheatManager.Windows.Protocol.SwitchFtpClient.Parent("/switch/breeze/cheats") != "/switch/breeze") throw new Exception("FTP parent failed");
    try { _ = NSCheatManager.Windows.Protocol.SwitchFtpClient.Normalize("/switch/../Nintendo"); throw new Exception("FTP traversal was accepted"); } catch (ArgumentException) { }
    if (SwitchFtpClient.NormalizeConfiguredDirectory("/switch\\breeze/cheats/") != "/switch/breeze/cheats") throw new Exception("Configured FTP directory normalization failed");
    try { _ = SwitchFtpClient.NormalizeConfiguredDirectory("switch/breeze"); throw new Exception("Relative configured FTP directory was accepted"); } catch (ArgumentException) { }
    if (SwitchFtpClient.NormalizeTextFileName("new-cheat") != "new-cheat.txt" ||
        SwitchFtpClient.NormalizeTextFileName("already.TXT") != "already.TXT" ||
        SwitchFtpClient.NormalizeTextFileName("draft.md") != "draft.md.txt")
        throw new Exception("FTP text file suffix normalization failed");
    try { _ = SwitchFtpClient.NormalizeTextFileName("../unsafe"); throw new Exception("Unsafe FTP text file name was accepted"); } catch (ArgumentException) { }
    Console.WriteLine("FTP_PATH_SAFETY=OK");
    Console.WriteLine("FTP_DEFAULT_DIRECTORY_AND_TEXT_NAME_OK");
    const string scopeTid = "0100A3D008C5C000";
    const string scopeBid = "421C5411B487EB4D";
    const string otherScopeTid = "0100000000000001";
    var scopeDirectory = $"/switch/breeze/cheats/{scopeTid}";
    var currentCheatCandidates = new[]
    {
        new NSCheatManager.Windows.Protocol.RemoteEntry(scopeBid.ToLowerInvariant() + ".TXT", $"{scopeDirectory}/{scopeBid.ToLowerInvariant()}.TXT", false, false, 10, DateTime.UtcNow),
        new NSCheatManager.Windows.Protocol.RemoteEntry("NoTeS.TxT", $"{scopeDirectory}/NoTeS.TxT", false, false, 20, DateTime.UtcNow),
        new NSCheatManager.Windows.Protocol.RemoteEntry("0011223344556677.txt", $"{scopeDirectory}/0011223344556677.txt", false, false, 30, DateTime.UtcNow),
        new NSCheatManager.Windows.Protocol.RemoteEntry("readme.txt", $"{scopeDirectory}/readme.txt", false, false, 40, DateTime.UtcNow),
        new NSCheatManager.Windows.Protocol.RemoteEntry(scopeBid + ".txt", $"{scopeDirectory}/{scopeBid}.txt", true, false, 0, DateTime.UtcNow),
        new NSCheatManager.Windows.Protocol.RemoteEntry("notes.txt", $"{scopeDirectory}/notes.txt", false, true, 50, DateTime.UtcNow),
        new NSCheatManager.Windows.Protocol.RemoteEntry(scopeBid + ".txt", $"/switch/breeze/cheats/{otherScopeTid}/{scopeBid}.txt", false, false, 60, DateTime.UtcNow)
    };
    var currentCheatFiles = currentCheatCandidates
        .Where(entry => NSCheatManager.Windows.Protocol.SwitchFtpClient.IsCurrentCheatFile(entry, scopeTid, scopeBid))
        .ToArray();
    var expectedCurrentNames = new HashSet<string>([scopeBid + ".txt", "notes.txt"], StringComparer.OrdinalIgnoreCase);
    if (currentCheatFiles.Length != 2 ||
        currentCheatFiles.Select(entry => entry.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != currentCheatFiles.Length ||
        currentCheatFiles.Any(entry => !expectedCurrentNames.Contains(entry.Name) || entry.IsDirectory || entry.IsLink ||
                                               !SwitchFtpClient.Parent(entry.FullPath).Equals(scopeDirectory, StringComparison.OrdinalIgnoreCase)))
        throw new Exception("Current-game cheat scope admitted a foreign TID/BID, an unrelated TXT, a directory, or a link");
    Console.WriteLine("CURRENT_CHEAT_SCOPE_FILTER_OK");
    var databaseProbeRoot = Path.Combine(Path.GetTempPath(), "nscheatmanager-database-smoke", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(databaseProbeRoot);
    try
    {
        var legacyPath = Path.Combine(databaseProbeRoot, "legacy.db");
        var legacyContent = System.Text.Encoding.UTF8.GetBytes("legacy history");
        await using (var legacyConnection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = legacyPath, Pooling = false }.ToString()))
        {
            await legacyConnection.OpenAsync();
            var legacyCommand = legacyConnection.CreateCommand();
            legacyCommand.CommandText = """
                CREATE TABLE file_history(
                  id INTEGER PRIMARY KEY AUTOINCREMENT,title_id TEXT NOT NULL,build_id TEXT NOT NULL,
                  relative_path TEXT NOT NULL,sha256 TEXT NOT NULL,content BLOB NOT NULL,created_utc TEXT NOT NULL,
                  parent_id INTEGER NULL,source TEXT NOT NULL DEFAULT 'sync'
                );
                INSERT INTO file_history(title_id,build_id,relative_path,sha256,content,created_utc,source)
                VALUES($t,$b,$p,$h,$c,$d,'sync');
                """;
            legacyCommand.Parameters.AddWithValue("$t", "0100000000000002");
            legacyCommand.Parameters.AddWithValue("$b", "0011223344556677");
            legacyCommand.Parameters.AddWithValue("$p", "0011223344556677.txt");
            legacyCommand.Parameters.AddWithValue("$h", Hashing.Sha256(legacyContent));
            legacyCommand.Parameters.AddWithValue("$c", legacyContent);
            legacyCommand.Parameters.AddWithValue("$d", DateTimeOffset.UtcNow.ToString("O"));
            await legacyCommand.ExecuteNonQueryAsync();
        }
        var migratedDatabase = new LocalDatabase(legacyPath);
        await migratedDatabase.InitializeAsync();
        var migratedHistory = await migratedDatabase.ListHistoryAsync();
        if (migratedHistory.Count != 1 || migratedHistory[0].Remark.Length != 0 || !(await migratedDatabase.ReadHistoryContentAsync(migratedHistory[0].Id)).SequenceEqual(legacyContent))
            throw new Exception("Legacy database migration did not preserve history content");

        var database = new LocalDatabase(Path.Combine(databaseProbeRoot, "database.db"));
        await database.InitializeAsync();
        const string databaseTid = "0100A3D008C5C000";
        const string databaseBid = "421C5411B487EB4D";
        var contentA = System.Text.Encoding.UTF8.GetBytes("[Database A]\n04000000 00000000 00000001\n");
        var contentB = System.Text.Encoding.UTF8.GetBytes("[Database B]\n04000000 00000000 00000002\n");
        var first = await database.AddHistoryBatchAsync([new HistorySnapshot(databaseTid, databaseBid, databaseBid + ".txt", contentA, Hashing.Sha256(contentA), "manual-db-save", "首次备份")]);
        var duplicate = await database.AddHistoryBatchAsync([new HistorySnapshot(databaseTid, databaseBid, databaseBid + ".txt", contentA, Hashing.Sha256(contentA), "manual-db-save", "首次备份")]);
        var annotated = await database.AddHistoryBatchAsync([new HistorySnapshot(databaseTid, databaseBid, databaseBid + ".txt", contentA, Hashing.Sha256(contentA), "manual-db-save", "确认可用")]);
        var changed = await database.AddHistoryBatchAsync([new HistorySnapshot(databaseTid, databaseBid, databaseBid + ".txt", contentB, Hashing.Sha256(contentB), "manual-db-save", "修改后")]);
        var returned = await database.AddHistoryBatchAsync([new HistorySnapshot(databaseTid, databaseBid, databaseBid + ".txt", contentA, Hashing.Sha256(contentA), "manual-db-save", "恢复旧版")]);
        if (first != 1 || duplicate != 0 || annotated != 1 || changed != 1 || returned != 1) throw new Exception("Database snapshot deduplication/timeline behavior is invalid");
        try
        {
            await database.AddHistoryBatchAsync([new HistorySnapshot(databaseTid, databaseBid, "notes.txt", contentA, Hashing.Sha256(contentA), "manual-db-save", "")]);
            throw new Exception("Database accepted a manual snapshot without a remark");
        }
        catch (ArgumentException error) when (error.Message.Contains("备注", StringComparison.Ordinal)) { }

        await database.SaveGameMetadataAsync(databaseTid.ToLowerInvariant(), databaseBid.ToLowerInvariant(), "宝可梦朱紫", "版本 v4.0.0");
        var metadata = await database.GetGameMetadataAsync(databaseTid, databaseBid);
        var history = await database.ListHistoryAsync(databaseTid);
        var contentC = System.Text.Encoding.UTF8.GetBytes("[Database C]\n04000000 00000000 00000003\n");
        var edited = await database.AddEditedHistoryAsync(history[0].Id, contentC, Hashing.Sha256(contentC), "离线编辑测试");
        var duplicateEdit = await database.AddEditedHistoryAsync(history[0].Id, contentC, Hashing.Sha256(contentC), "离线编辑测试");
        history = await database.ListHistoryAsync(databaseTid);
        if (!edited || duplicateEdit || history[0].Source != "offline-edit" || !history[0].Remark.Contains("离线编辑", StringComparison.Ordinal))
            throw new Exception("Offline database edit was not deduplicated or annotated");
        if (metadata.GameName != "宝可梦朱紫" || metadata.VersionName != "版本 v4.0.0" || history.Count != 5 || history.Any(x => x.GameName != metadata.GameName || x.VersionName != metadata.VersionName || string.IsNullOrWhiteSpace(x.Remark)))
            throw new Exception("Database labels or remarks were not persisted");

        const string secondTid = "0100000000000001";
        await database.SaveGameMetadataAsync(secondTid, databaseBid, "其他游戏", "另一个版本");
        var secondMetadata = await database.GetGameMetadataAsync(secondTid, databaseBid);
        if (secondMetadata.GameName != "其他游戏" || secondMetadata.VersionName != "另一个版本" || (await database.GetGameMetadataAsync(databaseTid, databaseBid)).GameName != "宝可梦朱紫")
            throw new Exception("Database labels leaked across TIDs");
        await database.SaveGameMetadataAsync(databaseTid, databaseBid, "", "");
        var clearedMetadata = await database.GetGameMetadataAsync(databaseTid, databaseBid);
        if (clearedMetadata.GameName.Length != 0 || clearedMetadata.VersionName.Length != 0) throw new Exception("Database labels could not be cleared");
        Console.WriteLine("DATABASE_SNAPSHOT_REMARK_DEDUPE_OK");
        Console.WriteLine("DATABASE_TID_BID_LABELS_OK");
        Console.WriteLine("DATABASE_LEGACY_MIGRATION_OK");
    }
    finally
    {
        var expectedParent = Path.Combine(Path.GetTempPath(), "nscheatmanager-database-smoke");
        if (Path.GetFullPath(databaseProbeRoot).StartsWith(Path.GetFullPath(expectedParent) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            Directory.Delete(databaseProbeRoot, true);
    }
    var pendingId = Guid.NewGuid();
    var legacyPendingJson = JsonSerializer.Serialize(new { DeviceId = pendingId, RelativePath = "notes.txt", ContentHash = "abc", QueuedAt = DateTimeOffset.UtcNow, LocalPath = "notes.txt", RemotePath = "/switch/breeze/cheats/0100000000000000/notes.txt" });
    var legacyPending = JsonSerializer.Deserialize<PendingUpload>(legacyPendingJson) ?? throw new Exception("Legacy pending JSON did not deserialize");
    if (legacyPending.DeviceHost is not null || legacyPending.DeviceFtpPort is not null) throw new Exception("Legacy pending endpoint defaults are invalid");
    var boundPending = legacyPending with { DeviceHost = "192.0.2.41", DeviceFtpPort = 21 };
    var pendingRoundTrip = JsonSerializer.Deserialize<PendingUpload>(JsonSerializer.Serialize(boundPending)) ?? throw new Exception("Pending endpoint round trip failed");
    if (pendingRoundTrip.DeviceHost != "192.0.2.41" || pendingRoundTrip.DeviceFtpPort != 21) throw new Exception("Pending endpoint snapshot was not preserved");
    Console.WriteLine("PENDING_ENDPOINT_SCHEMA_OK");
    var tempDeviceId = Guid.NewGuid();
    var endpointTempA = NSCheatManager.Windows.Core.AppPaths.DeviceTemp(tempDeviceId, "192.0.2.41", 21);
    var endpointTempB = NSCheatManager.Windows.Core.AppPaths.DeviceTemp(tempDeviceId, "192.168.1.42", 21);
    var endpointTempC = NSCheatManager.Windows.Core.AppPaths.DeviceTemp(tempDeviceId, "192.0.2.41", 2121);
    if (endpointTempA == endpointTempB || endpointTempA == endpointTempC || !endpointTempA.Equals(NSCheatManager.Windows.Core.AppPaths.DeviceTemp(tempDeviceId, "192.0.2.41", 21), StringComparison.OrdinalIgnoreCase)) throw new Exception("Temporary editor paths are not isolated by FTP endpoint");
    Console.WriteLine("TEMP_EDITOR_ENDPOINT_ISOLATION_OK");
    var snapshotProbe = new DeviceProfile { Name = "Snapshot Probe", Host = "192.0.2.10", FtpPort = 21, SysBotbasePort = 6000 };
    session.UseManualIdentity(snapshotProbe, tid, bid);
    snapshotProbe.Host = "192.0.2.99";
    snapshotProbe.FtpPort = 2121;
    if (session.Device?.Host != "192.0.2.10" || session.Device.FtpPort != 21) throw new Exception("Current-game session retained a mutable device profile");
    await session.DisconnectAsync();
    Console.WriteLine("GAME_SESSION_ENDPOINT_SNAPSHOT=OK");
}
finally
{
    await session.DisposeAsync();
}

static int ReserveUnusedLoopbackPort()
{
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var port = ((IPEndPoint)listener.LocalEndpoint).Port;
    listener.Stop();
    return port;
}
