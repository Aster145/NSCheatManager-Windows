using System.IO.Compression;
using System.IO;
using NSCheatManager.Windows.Core;

namespace NSCheatManager.Windows.Services;

public static class CheatZipService
{
    public static async Task<ZipExportResult> ExportAsync(string titleId, string buildId, string requestedName, CancellationToken cancellationToken)
    {
        var cheat = AppPaths.CheatFile(titleId, buildId);
        if (!File.Exists(cheat)) throw new FileNotFoundException("本地不存在当前 BID 的金手指文件。请先从 Switch 下载。", cheat);
        var cheatContent = await File.ReadAllBytesAsync(cheat, cancellationToken);
        var notes = AppPaths.NotesFile(titleId);
        var notesContent = File.Exists(notes) ? await File.ReadAllBytesAsync(notes, cancellationToken) : null;
        return await ExportAsync(titleId, buildId, requestedName, cheatContent, notesContent, cancellationToken);
    }

    public static async Task<ZipExportResult> ExportAsync(string titleId, string buildId, string requestedName, byte[] cheatContent, byte[]? notesContent, CancellationToken cancellationToken)
    {
        AppPaths.Ensure();
        if (cheatContent.Length == 0) throw new InvalidDataException("当前 BID 的金手指文件为空，已取消打包。");
        if (cheatContent.LongLength > 1024 * 1024 || notesContent?.LongLength > 1024 * 1024) throw new InvalidDataException("待打包 TXT 超过 1 MiB 限制。");
        var sharePath = EnsureShareTargetWritable(requestedName);
        var safe = Path.GetFileNameWithoutExtension(sharePath);
        var backupPath = UniquePath(Path.Combine(AppPaths.Archives, DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip"));
        var temporary = Path.Combine(AppPaths.Exports, $".nscheatmanager-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, true))
            {
                using var archive = new ZipArchive(stream, ZipArchiveMode.Create, true);
                await AddAsync(archive, cheatContent, $"atmosphere/contents/{titleId}/cheats/{buildId}.txt", cancellationToken);
                if (notesContent is not null) await AddAsync(archive, notesContent, $"atmosphere/contents/{titleId}/cheats/notes.txt", cancellationToken);
            }

            ValidateExport(temporary, titleId, buildId);
            try { File.Move(temporary, sharePath, true); }
            catch (UnauthorizedAccessException error) { throw CreateShareTargetException(sharePath, error); }
            catch (IOException error) when (File.Exists(sharePath) || Directory.Exists(sharePath)) { throw CreateShareTargetException(sharePath, error); }
            File.Copy(sharePath, backupPath, false);
            await AppLog.InfoAsync($"ZIP 打包成功 | share={sharePath} | backup={backupPath} | tid={titleId} | bid={buildId}");
            return new ZipExportResult(sharePath, backupPath);
        }
        catch (Exception error)
        {
            await AppLog.ErrorAsync($"ZIP 打包失败 | name={safe} | tid={titleId} | bid={buildId}", error);
            throw;
        }
        finally
        {
            if (File.Exists(temporary)) try { File.Delete(temporary); } catch { }
        }
    }

    public static string EnsureShareTargetWritable(string requestedName)
    {
        AppPaths.Ensure();
        var rawName = string.IsNullOrWhiteSpace(requestedName) ? "cheats" : requestedName.Trim();
        if (rawName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) rawName = rawName[..^4];
        var safe = string.Concat(rawName.Where(c => !Path.GetInvalidFileNameChars().Contains(c))).Trim().TrimEnd('.');
        if (safe.Length == 0) safe = "cheats";
        var sharePath = Path.Combine(AppPaths.Exports, safe + ".zip");
        if (Directory.Exists(sharePath)) throw new IOException($"无法创建 ZIP，因为目标路径是同名文件夹：{sharePath}");
        if (!File.Exists(sharePath)) return sharePath;
        if ((File.GetAttributes(sharePath) & FileAttributes.ReadOnly) != 0) throw new IOException($"无法覆盖只读 ZIP，请取消只读属性或使用其他名称：{sharePath}");
        try
        {
            using var stream = new FileStream(sharePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        catch (UnauthorizedAccessException error) { throw CreateShareTargetException(sharePath, error); }
        catch (IOException error) { throw CreateShareTargetException(sharePath, error); }
        return sharePath;
    }

    private static IOException CreateShareTargetException(string sharePath, Exception innerException) =>
        new($"无法覆盖目标 ZIP，它正被资源管理器、预览窗格或解压软件占用。请关闭该 ZIP 后重试，或输入其他名称：{sharePath}", innerException);

    public static async Task ImportAsync(string zipPath, string expectedTitleId, string expectedBuildId, CancellationToken cancellationToken)
    {
        var info = new FileInfo(zipPath);
        if (!info.Exists || info.Length > 8 * 1024 * 1024) throw new InvalidDataException("ZIP is missing or exceeds 8 MiB.");
        using var archive = ZipFile.OpenRead(zipPath);
        if (archive.Entries.Count > 100) throw new InvalidDataException("ZIP has too many entries.");
        var prefix = $"atmosphere/contents/{expectedTitleId}/cheats/";
        var accepted = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            if (name.StartsWith('/') || name.Contains("../", StringComparison.Ordinal) || !name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("ZIP contains an unsafe or mismatched path.");
            var leaf = name[prefix.Length..];
            if (leaf.Contains('/') || (leaf != expectedBuildId + ".txt" && !leaf.Equals("notes.txt", StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("ZIP contains an unexpected file.");
            if (entry.Length > 2 * 1024 * 1024 || (total += entry.Length) > 4 * 1024 * 1024) throw new InvalidDataException("ZIP expanded content is too large.");
            if (!accepted.TryAdd(leaf, entry)) throw new InvalidDataException("ZIP contains duplicate files.");
        }
        if (!accepted.TryGetValue(expectedBuildId + ".txt", out var cheatEntry)) throw new InvalidDataException("ZIP does not contain the expected cheat file.");
        Directory.CreateDirectory(AppPaths.CheatDirectory(expectedTitleId));
        await ExtractAtomicAsync(cheatEntry, AppPaths.CheatFile(expectedTitleId, expectedBuildId), cancellationToken);
        if (accepted.TryGetValue("notes.txt", out var notesEntry)) await ExtractAtomicAsync(notesEntry, AppPaths.NotesFile(expectedTitleId), cancellationToken);
    }

    private static async Task AddAsync(ZipArchive archive, byte[] content, string name, CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        await using var output = entry.Open();
        await output.WriteAsync(content, cancellationToken);
    }

    private static async Task ExtractAtomicAsync(ZipArchiveEntry entry, string target, CancellationToken cancellationToken)
    {
        var temp = target + ".tmp"; await using (var input = entry.Open()) await using (var output = File.Create(temp)) await input.CopyToAsync(output, cancellationToken); File.Move(temp, target, true);
    }

    private static string UniquePath(string requested)
    {
        if (!File.Exists(requested)) return requested;
        var directory = Path.GetDirectoryName(requested)!;
        var stem = Path.GetFileNameWithoutExtension(requested);
        var extension = Path.GetExtension(requested);
        for (var suffix = 2; ; suffix++)
        {
            var candidate = Path.Combine(directory, $"{stem}-{suffix}{extension}");
            if (!File.Exists(candidate)) return candidate;
        }
    }

    private static void ValidateExport(string zipPath, string titleId, string buildId)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var expected = $"atmosphere/contents/{titleId}/cheats/{buildId}.txt";
        var cheat = archive.GetEntry(expected) ?? throw new InvalidDataException("生成的 ZIP 缺少当前 BID 金手指文件。");
        if (cheat.Length <= 0) throw new InvalidDataException("生成的 ZIP 中金手指文件为空。");
        if (archive.Entries.Any(entry => entry.FullName.Contains("..", StringComparison.Ordinal))) throw new InvalidDataException("生成的 ZIP 路径校验失败。");
    }
}

public sealed record ZipExportResult(string SharePath, string BackupPath);
