using System.IO.Compression;
using System.IO;
using NSCheatManager.Windows.Core;

namespace NSCheatManager.Windows.Services;

public static class CheatZipService
{
    public static async Task<ZipExportResult> ExportAsync(string titleId, string buildId, string requestedName, CancellationToken cancellationToken)
    {
        AppPaths.Ensure();
        var cheat = AppPaths.CheatFile(titleId, buildId);
        if (!File.Exists(cheat)) throw new FileNotFoundException("本地不存在当前 BID 的金手指文件。请先从 Switch 下载。", cheat);
        var rawName = string.IsNullOrWhiteSpace(requestedName) ? "cheats" : Path.GetFileNameWithoutExtension(requestedName.Trim());
        var safe = string.Concat(rawName.Where(c => !Path.GetInvalidFileNameChars().Contains(c))).Trim();
        if (safe.Length == 0) safe = "cheats";
        var sharePath = UniquePath(Path.Combine(AppPaths.Exports, safe + ".zip"));
        var backupPath = UniquePath(Path.Combine(AppPaths.Archives, DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip"));
        var temporary = Path.Combine(AppPaths.Exports, $".nscheatmanager-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, true))
            {
                using var archive = new ZipArchive(stream, ZipArchiveMode.Create, true);
                await AddAsync(archive, cheat, $"atmosphere/contents/{titleId}/cheats/{buildId}.txt", cancellationToken);
                var notes = AppPaths.NotesFile(titleId);
                if (File.Exists(notes)) await AddAsync(archive, notes, $"atmosphere/contents/{titleId}/cheats/notes.txt", cancellationToken);
            }

            ValidateExport(temporary, titleId, buildId);
            File.Move(temporary, sharePath);
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

    private static async Task AddAsync(ZipArchive archive, string source, string name, CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        await using var input = File.OpenRead(source); await using var output = entry.Open(); await input.CopyToAsync(output, cancellationToken);
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
