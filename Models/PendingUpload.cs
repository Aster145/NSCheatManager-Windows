using System.IO;
namespace NSCheatManager.Windows.Models;
public sealed record PendingUpload(
    Guid DeviceId,
    string RelativePath,
    string ContentHash,
    DateTimeOffset QueuedAt,
    string? LocalPath = null,
    string? RemotePath = null,
    string? DeviceHost = null,
    int? DeviceFtpPort = null)
{
    public string EffectiveRemotePath => RemotePath ?? RelativePath;
    public string DisplayName => Path.GetFileName(LocalPath ?? RelativePath);
}
