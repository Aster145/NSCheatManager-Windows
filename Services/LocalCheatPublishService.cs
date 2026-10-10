using System.Security.Cryptography;
using System.IO;
using NSCheatManager.Windows.Core;

namespace NSCheatManager.Windows.Services;

public static class LocalCheatPublishService
{
    private const int MaximumCheatBytes = 1024 * 1024;

    public static string ValidateRepositoryRoot(string repositoryRoot, bool verifyWritable)
    {
        if (string.IsNullOrWhiteSpace(repositoryRoot))
            throw new InvalidOperationException("请先在设置中选择本地发布目录。");

        var root = Path.GetFullPath(repositoryRoot.Trim());
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"本地发布目录不存在：{root}");

        var gitMarker = Path.Combine(root, ".git");
        if (!Directory.Exists(gitMarker) && !File.Exists(gitMarker))
            throw new InvalidOperationException($"所选目录不是现有 Git 仓库（缺少 .git）：{root}");

        if (verifyWritable)
        {
            var probe = Path.Combine(root, $".nscheatmanager-write-test-{Guid.NewGuid():N}.tmp");
            try
            {
                using var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
                stream.WriteByte(0);
            }
            catch (Exception error) when (error is UnauthorizedAccessException or IOException)
            {
                throw new IOException($"本地发布目录不可写：{root}", error);
            }
            finally
            {
                if (File.Exists(probe)) try { File.Delete(probe); } catch { }
            }
        }

        return root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public static LocalPublishPreview Inspect(string repositoryRoot, string titleId, string buildId, byte[] content)
    {
        var root = ValidateRepositoryRoot(repositoryRoot, verifyWritable: true);
        ValidateIdentity(titleId, nameof(titleId));
        ValidateIdentity(buildId, nameof(buildId));
        if (content.Length == 0) throw new InvalidDataException("当前 BID 的金手指文件为空，已取消发布。");
        if (content.Length > MaximumCheatBytes) throw new InvalidDataException("当前 BID 的金手指文件超过 1 MiB 限制。");

        titleId = titleId.ToUpperInvariant();
        buildId = buildId.ToUpperInvariant();
        var targetPath = Path.GetFullPath(Path.Combine(root, "atmosphere", "contents", titleId, "cheats", buildId + ".txt"));
        EnsureContained(root, targetPath);

        if (!File.Exists(targetPath))
            return new LocalPublishPreview(root, titleId, buildId, targetPath, LocalPublishChange.New, null, 0, content.LongLength);

        var existing = File.ReadAllBytes(targetPath);
        var existingHash = Convert.ToHexString(SHA256.HashData(existing));
        var state = existing.AsSpan().SequenceEqual(content) ? LocalPublishChange.Unchanged : LocalPublishChange.Changed;
        return new LocalPublishPreview(root, titleId, buildId, targetPath, state, existingHash, existing.LongLength, content.LongLength);
    }

    public static async Task<LocalPublishResult> PublishAsync(LocalPublishPreview preview, byte[] content, CancellationToken cancellationToken, string? backupRoot = null)
    {
        var current = Inspect(preview.RepositoryRoot, preview.TitleId, preview.BuildId, content);
        if (!string.Equals(current.TargetPath, preview.TargetPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("发布目标在确认后发生变化，已取消写入。");
        if (current.Change != preview.Change || !string.Equals(current.ExistingHash, preview.ExistingHash, StringComparison.Ordinal))
            throw new IOException("本地目标文件在确认后发生变化，请重新发布并再次确认。");
        if (current.Change == LocalPublishChange.Unchanged)
            return new LocalPublishResult(current.TargetPath, null, LocalPublishChange.Unchanged);

        Directory.CreateDirectory(Path.GetDirectoryName(current.TargetPath)!);
        string? backupPath = null;
        if (current.Change == LocalPublishChange.Changed)
        {
            var backupDirectory = Path.Combine(backupRoot ?? AppPaths.PublishBackups, current.TitleId, current.BuildId, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
            Directory.CreateDirectory(backupDirectory);
            backupPath = Path.Combine(backupDirectory, current.BuildId + ".txt");
            File.Copy(current.TargetPath, backupPath, false);
            var backup = await File.ReadAllBytesAsync(backupPath, cancellationToken);
            if (current.ExistingHash != Convert.ToHexString(SHA256.HashData(backup)))
                throw new IOException("覆盖前备份校验失败，原文件未被修改。");
        }

        var temporary = Path.Combine(Path.GetDirectoryName(current.TargetPath)!, $".nscheatmanager-{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(temporary, content, cancellationToken);
            File.Move(temporary, current.TargetPath, true);
        }
        finally
        {
            if (File.Exists(temporary)) try { File.Delete(temporary); } catch { }
        }

        await AppLog.InfoAsync($"发布到本地目录成功 | path={current.TargetPath} | change={current.Change} | backup={backupPath ?? "none"}");
        return new LocalPublishResult(current.TargetPath, backupPath, current.Change);
    }

    private static void ValidateIdentity(string value, string parameterName)
    {
        if (value.Length != 16 || value.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("TID 和 BID 必须分别是 16 位十六进制字符。", parameterName);
    }

    private static void EnsureContained(string root, string target)
    {
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("发布路径超出所选 Git 仓库，已拒绝写入。");
    }
}

public enum LocalPublishChange { New, Unchanged, Changed }

public sealed record LocalPublishPreview(
    string RepositoryRoot,
    string TitleId,
    string BuildId,
    string TargetPath,
    LocalPublishChange Change,
    string? ExistingHash,
    long ExistingBytes,
    long NewBytes);

public sealed record LocalPublishResult(string TargetPath, string? BackupPath, LocalPublishChange Change);
