using Microsoft.Win32;
using NSCheatManager.Windows.Protocol;
using NSCheatManager.Windows.Services;
using NSCheatManager.Windows.ViewModels;
using System.Reflection;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace NSCheatManager.Windows;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();
    private bool _english;
    public MainWindow()
    {
        InitializeComponent(); DataContext = _viewModel;
        VersionButton.Content = $"v{GetVersion()}  ⓘ";
        Loaded += async (_, _) => await _viewModel.InitializeAsync();
        Closed += async (_, _) => await _viewModel.ShutdownAsync();
    }

    private async void RemotePanel_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var files = ((string[])e.Data.GetData(DataFormats.FileDrop)).Where(File.Exists).ToArray();
        try { await _viewModel.QueueDroppedFilesAsync(files); }
        catch (Exception error) { await AppLog.ErrorAsync("拖放文件加入待上传区失败", error); MessageBox.Show(error.Message, "FTP", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
    private void RemotePanel_DragOver(object sender, DragEventArgs e)
    { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; }
    private void RemoteGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    { if (_viewModel.SelectedRemoteEntry is not null) _viewModel.OpenEntryCommand.Execute(_viewModel.SelectedRemoteEntry); }
    private void CheatGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    { if (sender is DataGrid { SelectedItem: RemoteEntry entry }) _viewModel.OpenCheatEntryCommand.Execute(entry); }
    private void RemoteGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var element = e.OriginalSource as DependencyObject;
        while (element is not null && element is not DataGridRow) element = VisualTreeHelper.GetParent(element);
        if (element is DataGridRow row && sender is DataGrid grid) { row.IsSelected = true; grid.SelectedItem = row.Item; }
    }
    private async void BrowseEditor_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Programs (*.exe)|*.exe" };
        if (dialog.ShowDialog() == true && _viewModel.SelectedDevice is not null) { _viewModel.SelectedDevice.TextEditorPath = dialog.FileName; await AppLog.InfoAsync($"已选择 TXT 编辑器 | path={dialog.FileName}"); }
    }
    private async void VersionButton_Click(object sender, RoutedEventArgs e) { await AppLog.InfoAsync("打开版本信息窗口"); new AboutWindow(_english) { Owner = this }.ShowDialog(); }
    private async void PinButton_Click(object sender, RoutedEventArgs e)
    {
        Topmost = !Topmost; UpdatePinText(); await AppLog.InfoAsync($"窗口置顶切换 | enabled={Topmost}");
    }
    private async void LanguageButton_Click(object sender, RoutedEventArgs e)
    {
        _english = !_english; _viewModel.SetLanguage(_english); ApplyLanguage(); await AppLog.InfoAsync($"界面语言切换 | language={(_english ? "en" : "zh-CN")}");
    }
    private void ApplyLanguage()
    {
        var map = _english ? new Dictionary<FrameworkElement, string>
        {
            [DeviceLabel]="Device",[GameNavLabel]="Game Information",[CheatsNavLabel]="Cheats",[FtpNavLabel]="FTP",[DatabaseNavLabel]="Local Database",[ScreenNavLabel]="Screenshots & Screen",[SettingsNavLabel]="Settings",
            [GameTitle]="Game Information",[GameSubtitle]="The default device session uses sys-botbase",[ConnectButton]="Connect",[DisconnectButton]="Disconnect",[SessionOps]="Session actions",[DetachButton]="Detach dmnt",[GoFtpButton]="Open FTP",
            [CheatsTitle]="Cheats",[CheatsSubtitle]="Auto-identify and connect; current BID.txt and notes.txt only",[FtpTitle]="FTP",[ListAllButton]="List all files",[DownloadCheatButton]="Download from Switch",[SaveCheatToDatabaseButton]="Save to DB",[ImportZipButton]="Import ZIP",[ExportZipButton]="Package ZIP",[NewFolderButton]="New folder",[BackButton]="← Parent",[CheatBackButton]="← Parent",
            [DropTitle]="Drop one or more files here",[DropHelp]="Adds to confirmation queue; never auto-uploads; folders are rejected",[ConfirmUploadButton]="Confirm all uploads",
            [DatabaseTitle]="Local Database",[DatabaseSubtitle]="Manually save, edit offline and restore versions",[SaveToDatabaseButton]="Save to database",[SaveDatabaseLabelsButton]="Save names",[EditSelectedHistoryButton]="Edit offline",[SaveSelectedHistoryEditButton]="Save edit",[RestoreSelectedHistoryButton]="Restore & upload",[OpenDataButton]="Open data folder",
            [ScreensTitle]="Screenshots & Screen",[ScreensSubtitle]="Leave the name empty to use a timestamp",[OpenScreensButton]="Open screenshots folder",[CaptureTitle]="Capture screenshot",[CaptureButton]="Capture",[ScreenOnButton]="Screen on",[ScreenOffButton]="Screen off",
            [SettingsTitle]="Settings",[SettingsSubtitle]="Each device stores its own name, IPv4 address and protocol ports",[SaveSettingsButton]="Save settings",[OpenLogsButton]="Open logs folder"
        } : new Dictionary<FrameworkElement, string>
        {
            [DeviceLabel]="要连接的设备",[GameNavLabel]="游戏信息",[CheatsNavLabel]="金手指",[FtpNavLabel]="FTP 功能",[DatabaseNavLabel]="本地数据库",[ScreenNavLabel]="截图与屏幕",[SettingsNavLabel]="设置",
            [GameTitle]="游戏信息",[GameSubtitle]="默认通过 sys-botbase 建立设备会话",[ConnectButton]="连接",[DisconnectButton]="断开",[SessionOps]="会话操作",[DetachButton]="分离 dmnt",[GoFtpButton]="打开 FTP 功能",
            [CheatsTitle]="金手指",[CheatsSubtitle]="点击后自动识别并连接，仅显示当前 BID.txt 与 notes.txt",[FtpTitle]="FTP 功能",[ListAllButton]="列出全部文件",[DownloadCheatButton]="从 Switch 下载金手指",[SaveCheatToDatabaseButton]="写入数据库",[ImportZipButton]="导入 ZIP",[ExportZipButton]="打包为 ZIP",[NewFolderButton]="新建文件夹",[BackButton]="← 上级",[CheatBackButton]="← 上级",
            [DropTitle]="拖放一个或多个文件到这里",[DropHelp]="只加入待确认区，不会自动上传；不接受文件夹",[ConfirmUploadButton]="确认上传全部",
            [DatabaseTitle]="本地数据库",[DatabaseSubtitle]="手动写入、离线编辑和恢复历史版本",[SaveToDatabaseButton]="写入数据库",[SaveDatabaseLabelsButton]="保存名称",[EditSelectedHistoryButton]="离线编辑",[SaveSelectedHistoryEditButton]="保存编辑",[RestoreSelectedHistoryButton]="恢复并上传",[OpenDataButton]="打开 data 目录",
            [ScreensTitle]="截图与屏幕",[ScreensSubtitle]="文件名留空时使用当前时间戳",[OpenScreensButton]="打开截图目录",[CaptureTitle]="截取屏幕",[CaptureButton]="截图",[ScreenOnButton]="亮屏",[ScreenOffButton]="熄屏",
            [SettingsTitle]="设置",[SettingsSubtitle]="每台设备分别保存名称、IPv4 和协议端口",[SaveSettingsButton]="保存设置",[OpenLogsButton]="打开日志文件夹"
        };
        foreach (var item in map) if (item.Key is TextBlock text) text.Text = item.Value; else if (item.Key is Button button) button.Content = item.Value;
        TranslateRemaining(_english);
        DownloadMenuItem.Header = _english ? "Download" : "下载";
        CheatDownloadMenuItem.Header = _english ? "Download" : "下载";
        RenameMenuItem.Header = _english ? "Rename" : "重命名";
        DeleteMenuItem.Header = _english ? "Delete" : "删除";
        DownloadCheatButton.ToolTip = _english
            ? "Automatically connect to sys-botbase and FTP, then list only the current BID.txt and notes.txt."
            : "自动连接 sys-botbase 与 FTP，并只列出当前 BID.txt 和 notes.txt";
        SaveToDatabaseButton.ToolTip = _english
            ? "Read BID.txt and notes.txt for the identified game and save them only to the local database; nothing is uploaded to Switch."
            : "读取当前识别游戏的 BID.txt 与 notes.txt，仅保存到本地数据库，不会上传到 Switch";
        SaveCheatToDatabaseButton.ToolTip = _english
            ? "Connect and identify the game first. Data is written locally only after you click and enter a remark."
            : "必须先连接并识别游戏；点击后填写备注，才会写入本地数据库";
        UpdatePinText();
        LanguageButton.Content = _english ? "EN / 中文" : "中文 / EN";
    }
    private void UpdatePinText()
    {
        PinButton.Content = _english ? (Topmost ? "Unpin" : "Always on top") : (Topmost ? "取消置顶" : "置顶");
        PinButton.Background = Topmost ? new SolidColorBrush(Color.FromRgb(39, 100, 216)) : Brushes.White;
        PinButton.Foreground = Topmost ? Brushes.White : new SolidColorBrush(Color.FromRgb(30, 38, 55));
        PinButton.BorderBrush = Topmost ? new SolidColorBrush(Color.FromRgb(39, 100, 216)) : new SolidColorBrush(Color.FromRgb(199, 206, 220));
    }
    private void TranslateRemaining(bool toEnglish)
    {
        var zhToEn = new Dictionary<string, string>
        {
            ["设备名称"]="Device name",["设备 IPv4"]="Device IPv4",["sys-botbase 端口"]="sys-botbase port",["FTP 端口"]="FTP port",["Noexs 端口"]="Noexs port",
            ["连接前自动分离 dmnt"]="Detach dmnt before connecting",["TXT 文本编辑器路径（留空使用系统关联）"]="TXT editor path (empty uses Windows association)",["浏览…"]="Browse…",["新增设备"]="Add device",["删除此设备"]="Delete device",
            ["名称"]="Name",["类型"]="Type",["大小"]="Size",["修改时间"]="Modified",["操作"]="Actions",["打开"]="Open",["下载"]="Download",["改名"]="Rename",["删除"]="Delete",
            ["时间"]="Time",["文件"]="File",["离线编辑副本"]="Edit offline copy",["恢复并上传"]="Restore and upload",["刷新"]="Refresh",["移除"]="Remove",
            ["游戏名称设置"]="Game labels",["游戏名"]="Game name",["版本名"]="Version name",["游戏 / TID"]="Game / TID",["版本 / BID"]="Version / BID",["备注"]="Remark"
            ,["请输入要保存的文件名"]="Enter the file name to save"
        };
        var dictionary = toEnglish ? zhToEn : zhToEn.ToDictionary(x => x.Value, x => x.Key);
        TranslateElement(this, dictionary);
        foreach (var grid in FindVisualChildren<DataGrid>(this)) foreach (var column in grid.Columns) if (column.Header is string value && dictionary.TryGetValue(value, out var translated)) column.Header = translated;
    }
    private static void TranslateElement(DependencyObject root, IReadOnlyDictionary<string, string> map)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is TextBlock text && map.TryGetValue(text.Text, out var translatedText)) text.Text = translatedText;
            else if (child is Button button && button.Content is string buttonText && map.TryGetValue(buttonText, out var translatedButton)) button.Content = translatedButton;
            else if (child is CheckBox check && check.Content is string checkText && map.TryGetValue(checkText, out var translatedCheck)) check.Content = translatedCheck;
            TranslateElement(child, map);
        }
    }
    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); if (child is T match) yield return match; foreach (var nested in FindVisualChildren<T>(child)) yield return nested; }
    }
    internal static string GetVersion() => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
}
