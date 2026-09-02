using System.ComponentModel;
using System.Runtime.CompilerServices;
namespace NSCheatManager.Windows.Models;
public sealed class DeviceProfile : INotifyPropertyChanged
{
    private string _name = "My Switch"; private string _host = "192.168.1.100"; private int _ftpPort = 21; private bool _autoDetachDmnt = true; private int _sysBotbasePort = 6000; private int _noexsPort = 7331; private string _textEditorPath = "";
    public Guid Id { get; set; } = Guid.NewGuid();
    public int SysBotbasePort { get => _sysBotbasePort; set => Set(ref _sysBotbasePort, value); }
    public int NoexsPort { get => _noexsPort; set => Set(ref _noexsPort, value); }
    public string Name { get => _name; set => Set(ref _name, value); }
    public string Host { get => _host; set { if (Set(ref _host, value)) PropertyChanged?.Invoke(this, new(nameof(EndpointSummary))); } }
    public int FtpPort { get => _ftpPort; set { if (Set(ref _ftpPort, value)) PropertyChanged?.Invoke(this, new(nameof(EndpointSummary))); } }
    public bool AutoDetachDmnt { get => _autoDetachDmnt; set => Set(ref _autoDetachDmnt, value); }
    public string TextEditorPath { get => _textEditorPath; set => Set(ref _textEditorPath, value); }
    public bool FtpRiskAccepted { get; set; }
    public string EndpointSummary => Host;
    public event PropertyChangedEventHandler? PropertyChanged;
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null) { if (EqualityComparer<T>.Default.Equals(field, value)) return false; field = value; PropertyChanged?.Invoke(this, new(name)); return true; }
}
