using Microsoft.Data.Sqlite;

namespace NSCheatManager.Windows.Persistence;

public sealed class LocalDatabase(string path)
{
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private string ConnectionString => new SqliteConnectionStringBuilder
    {
        DataSource = path,
        DefaultTimeout = 5,
        Pooling = false
    }.ToString();

    public async Task InitializeAsync()
    {
        await _writeGate.WaitAsync();
        try
        {
            await using var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText = """
                PRAGMA busy_timeout=5000;
                CREATE TABLE IF NOT EXISTS file_history(
                  id INTEGER PRIMARY KEY AUTOINCREMENT,
                  title_id TEXT NOT NULL,
                  build_id TEXT NOT NULL,
                  relative_path TEXT NOT NULL,
                  sha256 TEXT NOT NULL,
                  content BLOB NOT NULL,
                  created_utc TEXT NOT NULL,
                  parent_id INTEGER NULL,
                  source TEXT NOT NULL DEFAULT 'sync',
                  remark TEXT NOT NULL DEFAULT ''
                );
                CREATE INDEX IF NOT EXISTS ix_history_game ON file_history(title_id, build_id, relative_path, created_utc DESC);
                CREATE TABLE IF NOT EXISTS sync_state(
                  device_id TEXT NOT NULL, title_id TEXT NOT NULL, build_id TEXT NOT NULL,
                  relative_path TEXT NOT NULL, sha256 TEXT NOT NULL, synced_utc TEXT NOT NULL,
                  PRIMARY KEY(device_id,title_id,build_id,relative_path)
                );
                CREATE TABLE IF NOT EXISTS game_titles(
                  title_id TEXT PRIMARY KEY COLLATE NOCASE,
                  game_name TEXT NOT NULL,
                  updated_utc TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS game_builds(
                  title_id TEXT NOT NULL COLLATE NOCASE,
                  build_id TEXT NOT NULL COLLATE NOCASE,
                  version_name TEXT NOT NULL,
                  updated_utc TEXT NOT NULL,
                  PRIMARY KEY(title_id,build_id)
                );
                PRAGMA user_version=3;
                """;
            await command.ExecuteNonQueryAsync();

            foreach (var sql in new[]
                     {
                         "ALTER TABLE file_history ADD COLUMN parent_id INTEGER NULL",
                         "ALTER TABLE file_history ADD COLUMN source TEXT NOT NULL DEFAULT 'sync'",
                         "ALTER TABLE file_history ADD COLUMN remark TEXT NOT NULL DEFAULT ''"
                     })
            {
                try
                {
                    var migration = connection.CreateCommand();
                    migration.CommandText = sql;
                    await migration.ExecuteNonQueryAsync();
                }
                catch (SqliteException error) when (
                    error.SqliteErrorCode == 1 &&
                    error.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase))
                {
                    // Already upgraded.
                }
            }
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task<bool> AddHistoryAsync(
        string titleId,
        string buildId,
        string relativePath,
        byte[] content,
        string hash,
        string source,
        string remark,
        long? parentId = null)
    {
        var added = await AddHistoryBatchAsync(
        [
            new HistorySnapshot(titleId, buildId, relativePath, content, hash, source, remark, parentId)
        ]);
        return added == 1;
    }

    public async Task<int> AddHistoryBatchAsync(IReadOnlyList<HistorySnapshot> snapshots)
    {
        if (snapshots.Count == 0) return 0;
        var normalized = snapshots.Select(NormalizeSnapshot).ToArray();

        await _writeGate.WaitAsync();
        try
        {
            await using var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync();
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
            var added = 0;

            foreach (var snapshot in normalized)
            {
                var latest = connection.CreateCommand();
                latest.Transaction = transaction;
                latest.CommandText = "SELECT sha256,remark FROM file_history WHERE title_id=$t COLLATE NOCASE AND build_id=$b COLLATE NOCASE AND relative_path=$p COLLATE NOCASE ORDER BY id DESC LIMIT 1";
                latest.Parameters.AddWithValue("$t", snapshot.TitleId);
                latest.Parameters.AddWithValue("$b", snapshot.BuildId);
                latest.Parameters.AddWithValue("$p", snapshot.RelativePath);
                string? latestHash = null;
                string latestRemark = string.Empty;
                await using (var latestReader = await latest.ExecuteReaderAsync())
                {
                    if (await latestReader.ReadAsync())
                    {
                        latestHash = latestReader.GetString(0);
                        latestRemark = latestReader.GetString(1);
                    }
                }
                var duplicateContent = string.Equals(latestHash, snapshot.Sha256, StringComparison.OrdinalIgnoreCase);
                var duplicateRemark = string.Equals(latestRemark, snapshot.Remark, StringComparison.Ordinal);
                if (duplicateContent && duplicateRemark) continue;

                var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = "INSERT INTO file_history(title_id,build_id,relative_path,sha256,content,created_utc,parent_id,source,remark) VALUES($t,$b,$p,$h,$c,$d,$parent,$source,$remark)";
                insert.Parameters.AddWithValue("$t", snapshot.TitleId);
                insert.Parameters.AddWithValue("$b", snapshot.BuildId);
                insert.Parameters.AddWithValue("$p", snapshot.RelativePath);
                insert.Parameters.AddWithValue("$h", snapshot.Sha256);
                insert.Parameters.AddWithValue("$c", snapshot.Content);
                insert.Parameters.AddWithValue("$d", DateTimeOffset.UtcNow.ToString("O"));
                insert.Parameters.AddWithValue("$parent", snapshot.ParentId is null ? DBNull.Value : snapshot.ParentId.Value);
                insert.Parameters.AddWithValue("$source", snapshot.Source);
                insert.Parameters.AddWithValue("$remark", snapshot.Remark);
                await insert.ExecuteNonQueryAsync();
                added++;

                var trim = connection.CreateCommand();
                trim.Transaction = transaction;
                trim.CommandText = "DELETE FROM file_history WHERE id IN (SELECT id FROM file_history WHERE title_id=$t COLLATE NOCASE AND build_id=$b COLLATE NOCASE AND relative_path=$p COLLATE NOCASE ORDER BY id DESC LIMIT -1 OFFSET 50)";
                trim.Parameters.AddWithValue("$t", snapshot.TitleId);
                trim.Parameters.AddWithValue("$b", snapshot.BuildId);
                trim.Parameters.AddWithValue("$p", snapshot.RelativePath);
                await trim.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
            return added;
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task<bool> AddEditedHistoryAsync(long parentId, byte[] content, string hash, string remark)
    {
        string titleId;
        string buildId;
        string relativePath;
        await using (var connection = new SqliteConnection(ConnectionString))
        {
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText = "SELECT title_id,build_id,relative_path FROM file_history WHERE id=$id";
            command.Parameters.AddWithValue("$id", parentId);
            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) throw new KeyNotFoundException("历史记录不存在。");
            titleId = reader.GetString(0);
            buildId = reader.GetString(1);
            relativePath = reader.GetString(2);
        }
        return await AddHistoryAsync(
            titleId,
            buildId,
            relativePath,
            content,
            hash,
            "offline-edit",
            remark,
            parentId);
    }

    public async Task SaveGameMetadataAsync(string titleId, string buildId, string? gameName, string? versionName)
    {
        (titleId, buildId) = NormalizeIdentity(titleId, buildId);
        gameName = NormalizeLabel(gameName, "游戏名");
        versionName = NormalizeLabel(versionName, "版本名");

        await _writeGate.WaitAsync();
        try
        {
            await using var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync();
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
            await UpsertTitleLabelAsync(connection, transaction, titleId, gameName);
            await UpsertBuildLabelAsync(connection, transaction, titleId, buildId, versionName);
            await transaction.CommitAsync();
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task<GameMetadata> GetGameMetadataAsync(string titleId, string buildId)
    {
        (titleId, buildId) = NormalizeIdentity(titleId, buildId);
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COALESCE(t.game_name,''),COALESCE(b.version_name,'')
            FROM (SELECT $t AS title_id, $b AS build_id) q
            LEFT JOIN game_titles t ON t.title_id=q.title_id COLLATE NOCASE
            LEFT JOIN game_builds b ON b.title_id=q.title_id COLLATE NOCASE AND b.build_id=q.build_id COLLATE NOCASE
            """;
        command.Parameters.AddWithValue("$t", titleId);
        command.Parameters.AddWithValue("$b", buildId);
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return new GameMetadata(titleId, buildId, reader.GetString(0), reader.GetString(1));
    }

    public async Task<IReadOnlyList<HistoryEntry>> ListHistoryAsync(string? titleId = null)
    {
        var result = new List<HistoryEntry>();
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT h.id,h.title_id,h.build_id,h.relative_path,h.sha256,h.created_utc,length(h.content),
                   COALESCE(t.game_name,''),COALESCE(b.version_name,''),h.source,h.remark
            FROM file_history h
            LEFT JOIN game_titles t ON t.title_id=h.title_id COLLATE NOCASE
            LEFT JOIN game_builds b ON b.title_id=h.title_id COLLATE NOCASE AND b.build_id=h.build_id COLLATE NOCASE
            """ + (titleId is null ? " ORDER BY h.created_utc DESC,h.id DESC" : " WHERE h.title_id=$t COLLATE NOCASE ORDER BY h.created_utc DESC,h.id DESC");
        if (titleId is not null) command.Parameters.AddWithValue("$t", titleId.Trim().ToUpperInvariant());
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new HistoryEntry(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                DateTimeOffset.Parse(reader.GetString(5)),
                reader.GetInt64(6),
                reader.GetString(7),
                reader.GetString(8),
                reader.GetString(9),
                reader.GetString(10)));
        }
        return result;
    }

    public async Task<byte[]> ReadHistoryContentAsync(long id)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT content FROM file_history WHERE id=$id";
        command.Parameters.AddWithValue("$id", id);
        return (byte[]?)await command.ExecuteScalarAsync() ?? throw new KeyNotFoundException("历史记录不存在。");
    }

    public async Task<string?> GetBaselineAsync(Guid deviceId, string titleId, string buildId, string relativePath)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT sha256 FROM sync_state WHERE device_id=$d AND title_id=$t AND build_id=$b AND relative_path=$p";
        command.Parameters.AddWithValue("$d", deviceId.ToString("D"));
        command.Parameters.AddWithValue("$t", titleId);
        command.Parameters.AddWithValue("$b", buildId);
        command.Parameters.AddWithValue("$p", relativePath);
        return (string?)await command.ExecuteScalarAsync();
    }

    public async Task SetBaselineAsync(Guid deviceId, string titleId, string buildId, string relativePath, string hash)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO sync_state(device_id,title_id,build_id,relative_path,sha256,synced_utc) VALUES($d,$t,$b,$p,$h,$u) ON CONFLICT(device_id,title_id,build_id,relative_path) DO UPDATE SET sha256=excluded.sha256,synced_utc=excluded.synced_utc";
        command.Parameters.AddWithValue("$d", deviceId.ToString("D"));
        command.Parameters.AddWithValue("$t", titleId);
        command.Parameters.AddWithValue("$b", buildId);
        command.Parameters.AddWithValue("$p", relativePath);
        command.Parameters.AddWithValue("$h", hash);
        command.Parameters.AddWithValue("$u", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task UpsertTitleLabelAsync(SqliteConnection connection, SqliteTransaction transaction, string titleId, string gameName)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = gameName.Length == 0
            ? "DELETE FROM game_titles WHERE title_id=$t COLLATE NOCASE"
            : "INSERT INTO game_titles(title_id,game_name,updated_utc) VALUES($t,$value,$updated) ON CONFLICT(title_id) DO UPDATE SET game_name=excluded.game_name,updated_utc=excluded.updated_utc";
        command.Parameters.AddWithValue("$t", titleId);
        if (gameName.Length > 0)
        {
            command.Parameters.AddWithValue("$value", gameName);
            command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
        }
        await command.ExecuteNonQueryAsync();
    }

    private static async Task UpsertBuildLabelAsync(SqliteConnection connection, SqliteTransaction transaction, string titleId, string buildId, string versionName)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = versionName.Length == 0
            ? "DELETE FROM game_builds WHERE title_id=$t COLLATE NOCASE AND build_id=$b COLLATE NOCASE"
            : "INSERT INTO game_builds(title_id,build_id,version_name,updated_utc) VALUES($t,$b,$value,$updated) ON CONFLICT(title_id,build_id) DO UPDATE SET version_name=excluded.version_name,updated_utc=excluded.updated_utc";
        command.Parameters.AddWithValue("$t", titleId);
        command.Parameters.AddWithValue("$b", buildId);
        if (versionName.Length > 0)
        {
            command.Parameters.AddWithValue("$value", versionName);
            command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
        }
        await command.ExecuteNonQueryAsync();
    }

    private static HistorySnapshot NormalizeSnapshot(HistorySnapshot snapshot)
    {
        var (titleId, buildId) = NormalizeIdentity(snapshot.TitleId, snapshot.BuildId);
        var relativePath = NormalizeRelativePath(snapshot.RelativePath);
        var source = snapshot.Source.Trim();
        var remark = snapshot.Remark.Trim();
        if (snapshot.Content is null) throw new ArgumentNullException(nameof(snapshot.Content));
        if (string.IsNullOrWhiteSpace(snapshot.Sha256)) throw new ArgumentException("历史内容哈希不能为空。", nameof(snapshot.Sha256));
        if (source.Length == 0 || source.Length > 64) throw new ArgumentException("历史来源无效。", nameof(snapshot.Source));
        if (remark.Length == 0) throw new ArgumentException("写入数据库时必须填写备注。", nameof(snapshot.Remark));
        if (remark.Length > 500 || remark.Any(char.IsControl)) throw new ArgumentException("备注不能超过 500 个字符，也不能包含控制字符。", nameof(snapshot.Remark));
        return snapshot with { TitleId = titleId, BuildId = buildId, RelativePath = relativePath, Source = source, Remark = remark };
    }

    private static (string TitleId, string BuildId) NormalizeIdentity(string titleId, string buildId)
    {
        titleId = titleId.Trim().ToUpperInvariant();
        buildId = buildId.Trim().ToUpperInvariant();
        if (titleId.Length != 16 || !titleId.All(Uri.IsHexDigit)) throw new ArgumentException("TID 必须是 16 位十六进制。", nameof(titleId));
        if (buildId.Length is < 8 or > 64 || !buildId.All(Uri.IsHexDigit)) throw new ArgumentException("BID 必须是 8 到 64 位十六进制。", nameof(buildId));
        return (titleId, buildId);
    }

    private static string NormalizeRelativePath(string relativePath)
    {
        relativePath = relativePath.Trim();
        if (relativePath.Length == 0 || relativePath.IndexOfAny(['/', '\\']) >= 0) throw new ArgumentException("历史文件名无效。", nameof(relativePath));
        return relativePath;
    }

    private static string NormalizeLabel(string? value, string label)
    {
        value = value?.Trim() ?? string.Empty;
        if (value.Length > 100) throw new ArgumentException($"{label}不能超过 100 个字符。");
        if (value.Any(char.IsControl)) throw new ArgumentException($"{label}不能包含控制字符。");
        return value;
    }
}

public sealed record HistorySnapshot(
    string TitleId,
    string BuildId,
    string RelativePath,
    byte[] Content,
    string Sha256,
    string Source,
    string Remark,
    long? ParentId = null);

public sealed record GameMetadata(string TitleId, string BuildId, string GameName, string VersionName);

public sealed record HistoryEntry(
    long Id,
    string TitleId,
    string BuildId,
    string RelativePath,
    string Sha256,
    DateTimeOffset CreatedAt,
    long Size,
    string GameName,
    string VersionName,
    string Source,
    string Remark)
{
    public string TitleDisplay => string.IsNullOrWhiteSpace(GameName) ? TitleId : $"{GameName}\n{TitleId}";
    public string BuildDisplay => string.IsNullOrWhiteSpace(VersionName) ? BuildId : $"{VersionName}\n{BuildId}";
    public string FileDisplay => RelativePath.Equals(BuildId + ".txt", StringComparison.OrdinalIgnoreCase) ? "BID.txt" : RelativePath;
}
