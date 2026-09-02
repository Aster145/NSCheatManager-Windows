using System.Security.Cryptography;
using System.IO;

namespace NSCheatManager.Windows.Services;

public static class Hashing
{
    public static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    public static async Task<string> Sha256FileAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }
}
