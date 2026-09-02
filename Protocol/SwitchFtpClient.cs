using FluentFTP;
using FluentFTP.Exceptions;
using NSCheatManager.Windows.Services;
using System.IO;

namespace NSCheatManager.Windows.Protocol;

public sealed class SwitchFtpClient(string host, int port)
{
    public const long MaxTextBytes = 1024 * 1024;
    public const long MaxGeneralBytes = 2L * 1024 * 1024 * 1024;
    private const long FullHashLimit = 16L * 1024 * 1024;

    private AsyncFtpClient CreateClient()
    {
        var client = new AsyncFtpClient(host, "anonymous", "anonymous@", port);
        client.Config.ConnectTimeout = 5000; client.Config.ReadTimeout = 10000;
        client.Config.DataConnectionConnectTimeout = 5000; client.Config.DataConnectionReadTimeout = 10000;
        client.Config.DataConnectionType = FtpDataConnectionType.AutoPassive;
        client.Config.InternetProtocolVersions = FtpIpVersion.IPv4;
        return client;
    }

    public async Task<string> DetectSdRootAsync(CancellationToken ct)
    { await using var client = CreateClient(); await client.Connect(ct); return await DetectRootAsync(client, ct); }

    public async Task<IReadOnlyList<RemoteEntry>> ListDirectoryAsync(string path, CancellationToken ct)
    {
        await using var client = CreateClient(); await client.Connect(ct); var root = await DetectRootAsync(client, ct); var resolved = Resolve(root, path);
        if (!await client.DirectoryExists(resolved, ct)) throw new DirectoryNotFoundException($"远端目录不存在：{path}");
        await client.SetWorkingDirectory(resolved, ct);
        var listing = await client.GetListing(".", ct);
        return listing.Where(x => x.Name is not "." and not ".." && !x.Name.StartsWith(".nscheatmanager-", StringComparison.OrdinalIgnoreCase))
            .Select(x => new RemoteEntry(x.Name, Combine(path, x.Name), x.Type == FtpObjectType.Directory, x.Type == FtpObjectType.Link, x.Size, x.Modified))
            .OrderByDescending(x => x.IsDirectory).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<IReadOnlyList<RemoteTextFile>> ListTextFilesAsync(string titleId, CancellationToken ct)
    {
        try { return (await ListDirectoryAsync(CheatDirectory(titleId), ct)).Where(x => !x.IsDirectory && !x.IsLink && IsSafeTextName(x.Name)).Select(x => new RemoteTextFile(x.Name, x.Size, x.Modified)).ToList(); }
        catch (DirectoryNotFoundException) { return []; }
    }

    public async Task<byte[]?> DownloadAsync(string titleId, string fileName, CancellationToken ct)
    {
        ValidateTextName(fileName); await using var client = CreateClient(); await client.Connect(ct);
        var target = Resolve(await DetectRootAsync(client, ct), Combine(CheatDirectory(titleId), fileName));
        if (!await client.FileExists(target, ct)) return null;
        if (await client.GetFileSize(target, -1, ct) > MaxTextBytes) throw new InvalidDataException("远端 TXT 超过 1 MiB 限制。");
        return await client.DownloadBytes(target, ct) ?? throw new IOException("FTP 下载失败。");
    }

    public async Task DownloadFileAsync(string remotePath, string localPath, IProgress<FtpProgress>? progress, CancellationToken ct)
    {
        remotePath = Normalize(remotePath); await using var client = CreateClient(); await client.Connect(ct);
        var target = Resolve(await DetectRootAsync(client, ct), remotePath); var size = await client.GetFileSize(target, -1, ct);
        if (size < 0 || size > MaxGeneralBytes) throw new InvalidDataException("远端文件大小无效或超过 2 GiB 限制。");
        Directory.CreateDirectory(Path.GetDirectoryName(localPath)!); var partial = localPath + ".partial";
        try
        {
            var status = await client.DownloadFile(partial, target, FtpLocalExists.Overwrite, FtpVerify.None, progress, ct);
            if (status != FtpStatus.Success || new FileInfo(partial).Length != size) throw new IOException("FTP 下载或长度校验失败。");
            File.Move(partial, localPath, true);
        }
        finally { if (File.Exists(partial)) File.Delete(partial); }
    }

    public Task UploadSafelyAsync(string titleId, string fileName, byte[] content, CancellationToken ct)
    {
        ValidateTextName(fileName); if (content.LongLength > MaxTextBytes) throw new InvalidDataException("TXT 超过 1 MiB 限制。");
        return UploadBytesAsync(Combine(CheatDirectory(titleId), fileName), content, ct);
    }

    private async Task UploadBytesAsync(string remotePath, byte[] content, CancellationToken ct)
    {
        var local = Path.Combine(Path.GetTempPath(), $"nscheatmanager-{Guid.NewGuid():N}.tmp");
        try { await File.WriteAllBytesAsync(local, content, ct); await UploadFileSafelyAsync(local, remotePath, null, ct); }
        finally { if (File.Exists(local)) File.Delete(local); }
    }

    public async Task UploadFileSafelyAsync(string localPath, string remotePath, IProgress<FtpProgress>? progress, CancellationToken ct)
    {
        remotePath = Normalize(remotePath); var info = new FileInfo(localPath); if (!info.Exists) throw new FileNotFoundException("本地文件不存在。", localPath);
        var limit = info.Extension.Equals(".txt", StringComparison.OrdinalIgnoreCase) ? MaxTextBytes : MaxGeneralBytes;
        if (info.Length > limit) throw new InvalidDataException("文件超过允许大小。");
        await using var client = CreateClient(); await client.Connect(ct); var target = Resolve(await DetectRootAsync(client, ct), remotePath);
        var dir = target[..target.LastIndexOf('/')]; await client.CreateDirectory(dir, ct);
        var id = Guid.NewGuid().ToString("N"); var temp = $"{dir}/.nscheatmanager-upload-{id}.tmp"; var backup = $"{dir}/.nscheatmanager-backup-{id}.bak";
        var hadTarget = await client.FileExists(target, ct); var backedUp = false; var committed = false; var renamePhase = false;
        try
        {
            var status = await client.UploadFile(localPath, temp, FtpRemoteExists.Overwrite, true, FtpVerify.None, progress, ct);
            if (status != FtpStatus.Success || await client.GetFileSize(temp, -1, ct) != info.Length) throw new IOException("FTP 暂存上传校验失败。");
            if (info.Length <= FullHashLimit)
            {
                var bytes = await client.DownloadBytes(temp, ct) ?? throw new IOException("FTP 暂存校验下载失败。");
                if (!Hashing.Sha256(bytes).Equals(await Hashing.Sha256FileAsync(localPath, ct), StringComparison.Ordinal)) throw new IOException("FTP 暂存哈希不匹配。");
            }
            renamePhase = true;
            if (hadTarget) { await client.MoveFile(target, backup, FtpRemoteExists.NoCheck, ct); backedUp = true; }
            await client.MoveFile(temp, target, FtpRemoteExists.NoCheck, ct); committed = true;
            if (await client.GetFileSize(target, -1, ct) != info.Length) throw new IOException("FTP 提交大小校验失败。");
            if (backedUp && await client.FileExists(backup, ct)) await client.DeleteFile(backup, ct);
        }
        catch (Exception error)
        {
            try
            {
                if (committed && await client.FileExists(target, ct)) await client.DeleteFile(target, ct);
                if (backedUp && await client.FileExists(backup, ct)) await client.MoveFile(backup, target, FtpRemoteExists.NoCheck, ct);
                if (await client.FileExists(temp, ct)) await client.DeleteFile(temp, ct);
            }
            catch (Exception rollback) { throw new AggregateException("FTP 上传失败且回滚不完整。", error, rollback); }
            if (renamePhase && error is FtpCommandException) throw new FtpAtomicRenameUnavailableException("FTP 服务不支持安全重命名。", error);
            throw;
        }
    }

    public async Task UploadDirectWithRollbackAsync(string titleId, string fileName, byte[] content, CancellationToken ct)
    {
        ValidateTextName(fileName); var local = Path.Combine(Path.GetTempPath(), $"nscheatmanager-{Guid.NewGuid():N}.tmp");
        try { await File.WriteAllBytesAsync(local, content, ct); await UploadDirectFileWithRollbackAsync(local, Combine(CheatDirectory(titleId), fileName), ct); }
        finally { if (File.Exists(local)) File.Delete(local); }
    }

    public async Task UploadDirectFileWithRollbackAsync(string localPath, string remotePath, CancellationToken ct)
    {
        remotePath = Normalize(remotePath); await using var client = CreateClient(); await client.Connect(ct); var target = Resolve(await DetectRootAsync(client, ct), remotePath); byte[]? original = null;
        if (await client.FileExists(target, ct)) original = await client.DownloadBytes(target, ct);
        try
        {
            var status = await client.UploadFile(localPath, target, FtpRemoteExists.Overwrite, true, FtpVerify.None, null, ct);
            if (status != FtpStatus.Success || await client.GetFileSize(target, -1, ct) != new FileInfo(localPath).Length) throw new IOException("FTP 直接覆盖校验失败。");
        }
        catch (Exception upload)
        {
            if (original is null) throw;
            try { await client.UploadBytes(original, target, FtpRemoteExists.Overwrite, true, null, ct); }
            catch (Exception rollback) { throw new AggregateException("直接覆盖失败且旧内容恢复失败。", upload, rollback); }
            throw;
        }
    }

    public async Task CreateDirectoryAsync(string path, CancellationToken ct)
    { await using var client = CreateClient(); await client.Connect(ct); await client.CreateDirectory(Resolve(await DetectRootAsync(client, ct), Normalize(path)), ct); }

    public async Task DeleteFileAsync(string path, CancellationToken ct)
    { await using var client = CreateClient(); await client.Connect(ct); var p = Resolve(await DetectRootAsync(client, ct), Normalize(path)); if (await client.FileExists(p, ct)) await client.DeleteFile(p, ct); }

    public Task DeleteAsync(string titleId, string fileName, string protectedBuildId, CancellationToken ct)
    {
        ValidateTextName(fileName);
        if (fileName.Equals(protectedBuildId + ".txt", StringComparison.OrdinalIgnoreCase) || fileName.Equals("notes.txt", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("专用文件不能通过普通文件管理器删除。");
        return DeleteFileAsync(Combine(CheatDirectory(titleId), fileName), ct);
    }

    public async Task RenameFileAsync(string path, string newName, CancellationToken ct)
    {
        ValidateName(newName); await using var client = CreateClient(); await client.Connect(ct); var root = await DetectRootAsync(client, ct);
        var source = Resolve(root, Normalize(path)); var target = source[..(source.LastIndexOf('/') + 1)] + newName;
        if (await client.FileExists(target, ct) || await client.DirectoryExists(target, ct)) throw new IOException("目标名称已存在。");
        await client.MoveFile(source, target, FtpRemoteExists.NoCheck, ct);
    }

    private static async Task<string> DetectRootAsync(AsyncFtpClient client, CancellationToken ct)
    {
        if (await client.DirectoryExists("/switch", ct)) return "/";
        if (await client.DirectoryExists("/sdmc:/switch", ct) || await client.DirectoryExists("/sdmc:", ct)) return "/sdmc:";
        return "/";
    }

    public static string Normalize(string path)
    {
        path = path.Replace('\\', '/'); if (!path.StartsWith('/')) path = "/" + path;
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries); if (parts.Any(x => x is "." or "..")) throw new ArgumentException("远端路径包含非法片段。");
        return "/" + string.Join('/', parts);
    }
    public static string Parent(string path) { var p = Normalize(path); if (p == "/") return "/"; var i = p.LastIndexOf('/'); return i <= 0 ? "/" : p[..i]; }
    public static string Combine(string dir, string name) { ValidateName(name); return Normalize(dir).TrimEnd('/') + "/" + name; }
    public static bool IsCurrentCheatFile(RemoteEntry entry, string titleId, string buildId)
    {
        var directory = CheatDirectory(titleId);
        buildId = ValidateBuildId(buildId);
        return !entry.IsDirectory
               && !entry.IsLink
               && Parent(entry.FullPath).Equals(directory, StringComparison.OrdinalIgnoreCase)
               && (entry.Name.Equals(buildId + ".txt", StringComparison.OrdinalIgnoreCase)
                   || entry.Name.Equals("notes.txt", StringComparison.OrdinalIgnoreCase));
    }
    private static string Resolve(string root, string path) => root == "/" ? Normalize(path) : root.TrimEnd('/') + Normalize(path);
    private static string CheatDirectory(string titleId) => $"/switch/breeze/cheats/{ValidateTitleId(titleId)}";
    public static bool IsSafeTextName(string name) => IsSafeName(name) && name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);
    public static bool IsSafeName(string name) => name.Length is >= 1 and <= 255 && name is not "." and not ".." && name.All(c => !char.IsControl(c) && c is not '/' and not '\\' and not ':');
    private static void ValidateName(string name) { if (!IsSafeName(name)) throw new ArgumentException("名称不安全。", nameof(name)); }
    private static void ValidateTextName(string name) { if (!IsSafeTextName(name)) throw new ArgumentException("TXT 文件名不安全。", nameof(name)); }
    private static string ValidateTitleId(string id) => id.Length == 16 && id.All(Uri.IsHexDigit) ? id.ToUpperInvariant() : throw new ArgumentException("Title ID 无效。");
    private static string ValidateBuildId(string id) => id.Length is >= 8 and <= 64 && id.All(Uri.IsHexDigit) ? id.ToUpperInvariant() : throw new ArgumentException("Build ID 无效。");
}

public sealed record RemoteEntry(string Name, string FullPath, bool IsDirectory, bool IsLink, long Size, DateTime Modified)
{
    public string TypeText => IsLink ? "链接" : IsDirectory ? "文件夹" : "文件";
    public string SizeText => IsDirectory ? "—" : Size < 1024 ? $"{Size} B" : Size < 1024 * 1024 ? $"{Size / 1024d:F1} KiB" : $"{Size / 1024d / 1024d:F1} MiB";
}
public sealed record RemoteTextFile(string Name, long Size, DateTime Modified = default);
public sealed class FtpAtomicRenameUnavailableException(string message, Exception innerException) : IOException(message, innerException);
