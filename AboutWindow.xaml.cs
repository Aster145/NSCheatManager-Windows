using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;

namespace NSCheatManager.Windows;

public partial class AboutWindow : Window
{
    private const string AndroidProjectUrl = "https://github.com/Aster145/NSCheatManager";
    private const string WindowsProjectUrl = "https://github.com/Aster145/NSCheatManager-Windows";
    private const string QqGroupNumber = "457965140";
    private readonly bool _english;

    public AboutWindow(bool english)
    {
        InitializeComponent();
        _english = english;
        VersionText.Text = $"v{MainWindow.GetVersion()} · win-x64 · portable/self-contained";
        ApplyLanguage();
    }

    private void ApplyLanguage()
    {
        if (_english)
        {
            Title = "About";
            SummaryText.Text = "A portable Windows client for an owned or authorized CFW Nintendo Switch, using sys-botbase, FTP and Noexs.";
            CreditsTitle.Text = "Open-source projects and licenses";
            SysBotCredit.Text = "device session and game recognition protocol";
            AtmosphereCredit.Text = "CFW and cheat format";
            NoexsCredit.Text = "dmnt detach protocol reference";
            LicenseSummary.Text = "Licensed under GNU GPL v3 only. Third-party components retain their respective licenses.";
            AndroidProjectTitle.Text = "Android project on GitHub";
            WindowsProjectTitle.Text = "Windows repository";
            RiskTitle.Text = "Usage and risk";
            RiskText.Text = "Unofficial community software. Remote file and memory operations can crash software or damage data. Use only on devices you own or are authorized to manage, keep backups, and never expose unauthenticated Switch services to the Internet.";
            QqTitle.Text = "QQ group";
            QqNumber.Text = $"Group: {QqGroupNumber}";
            CopyButton.Content = "Copy version info";
            CloseButton.Content = "Close";
            AndroidProjectButton.ToolTip = "Open the Android project in your browser";
            WindowsProjectButton.ToolTip = "Open the Windows repository in your browser";
            QqButton.ToolTip = "Copy the QQ group number";
        }
        else
        {
            Title = "关于";
            SummaryText.Text = "面向自有或获授权 CFW Nintendo Switch 的便携 Windows 客户端，使用 sys-botbase、FTP 与 Noexs。";
            CreditsTitle.Text = "开源项目与许可";
            SysBotCredit.Text = "设备会话与游戏识别协议";
            AtmosphereCredit.Text = "CFW 与金手指格式";
            NoexsCredit.Text = "dmnt 分离协议参考";
            LicenseSummary.Text = "本程序采用 GNU GPL v3 only；第三方组件保留各自许可证。";
            AndroidProjectTitle.Text = "安卓版项目 GitHub";
            WindowsProjectTitle.Text = "Windows 仓库";
            RiskTitle.Text = "使用说明与风险";
            RiskText.Text = "这是非官方社区软件。远端文件及内存操作可能导致软件崩溃或数据损坏。仅在自有或获授权设备上使用，提前备份重要数据，不要把未认证的 Switch 服务暴露到互联网。";
            QqTitle.Text = "QQ 群";
            QqNumber.Text = $"群号：{QqGroupNumber}";
            CopyButton.Content = "复制版本信息";
            CloseButton.Content = "关闭";
            AndroidProjectButton.ToolTip = "在浏览器中打开安卓版项目";
            WindowsProjectButton.ToolTip = "在浏览器中打开 Windows 版仓库";
            QqButton.ToolTip = "复制 QQ 群号";
        }
    }

    private void AndroidProject_Click(object sender, RoutedEventArgs e) => OpenExternal(AndroidProjectUrl);
    private void WindowsProject_Click(object sender, RoutedEventArgs e) => OpenExternal(WindowsProjectUrl);
    private void Qq_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(QqGroupNumber);
        MessageBox.Show(
            _english ? $"QQ group number {QqGroupNumber} has been copied." : $"QQ群号 {QqGroupNumber} 已复制。",
            _english ? "QQ group" : "QQ群",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void OpenExternal(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception error)
        {
            MessageBox.Show(
                _english ? $"Windows could not open the link.\n\n{error.Message}" : $"Windows 无法打开链接。\n\n{error.Message}",
                _english ? "Open link" : "打开链接",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void Copy_Click(object sender, RoutedEventArgs e) => Clipboard.SetText(
        $"NS Cheat Manager v{MainWindow.GetVersion()}\n" +
        $"Windows {Environment.OSVersion.Version}\n" +
        $"{RuntimeInformation.ProcessArchitecture}\n" +
        "Portable self-contained\n" +
        $"Android project: {AndroidProjectUrl}\n" +
        $"Windows repository: {WindowsProjectUrl}\n" +
        $"QQ group: {QqGroupNumber}");

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
