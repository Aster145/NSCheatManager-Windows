namespace NSCheatManager.Windows.Models;

public sealed class AppSettings
{
    public Guid? LastSelectedDeviceId { get; set; }
    public string FtpDefaultDirectory { get; set; } = "/";
}
