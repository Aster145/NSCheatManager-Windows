using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace NSCheatManager.Windows.Core;

public static class AppPaths
{
    public static string Root { get; } = Path.Combine(AppContext.BaseDirectory, "data");
    public static string Devices => Path.Combine(Root, "devices.json");
    public static string Settings => Path.Combine(Root, "settings.json");
    public static string Pending => Path.Combine(Root, "pending.json");
    public static string PendingQuarantine => Path.Combine(Root, "pending-quarantine.json");
    public static string Database => Path.Combine(Root, "nscheatmanager.db");
    public static string Mirror => Path.Combine(Root, "mirror");
    public static string Archives => Path.Combine(Root, "archives");
    public static string Exports => Path.Combine(Root, "exports");
    public static string Screenshots => Path.Combine(Root, "screenshots");
    public static string Temporary => Path.Combine(Root, "temp");
    public static string Logs => Path.Combine(Root, "logs");
    public static string Downloads => Path.Combine(Root, "downloads");

    public static void Ensure()
    {
        foreach (var path in new[] { Root, Mirror, Archives, Exports, Screenshots, Temporary, Logs, Downloads }) Directory.CreateDirectory(path);
    }

    public static string CheatDirectory(string titleId) => Path.Combine(Mirror, "atmosphere", "contents", titleId, "cheats");
    public static string CheatFile(string titleId, string buildId) => Path.Combine(CheatDirectory(titleId), buildId + ".txt");
    public static string NotesFile(string titleId) => Path.Combine(CheatDirectory(titleId), "notes.txt");
    public static string DeviceTemp(Guid deviceId, string host, int ftpPort)
    {
        var endpoint = $"{host.Trim().ToLowerInvariant()}:{ftpPort}";
        var endpointKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(endpoint)))[..16];
        return Path.Combine(Temporary, deviceId.ToString("N"), endpointKey);
    }
}
