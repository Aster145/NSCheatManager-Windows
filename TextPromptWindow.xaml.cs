using System.Windows;

namespace NSCheatManager.Windows;

public partial class TextPromptWindow : Window
{
    private TextPromptWindow(string prompt, string caption, string defaultValue, bool english)
    {
        InitializeComponent();
        Title = caption;
        PromptText.Text = prompt;
        ValueTextBox.Text = defaultValue;
        CancelButton.Content = english ? "Cancel" : "取消";
        AcceptButton.Content = english ? "OK" : "确定";
        Loaded += (_, _) =>
        {
            ValueTextBox.Focus();
            ValueTextBox.SelectAll();
        };
    }

    public static bool TryShow(string prompt, string caption, string defaultValue, out string value)
        => TryShow(prompt, caption, defaultValue, false, out value);

    public static bool TryShow(string prompt, string caption, string defaultValue, bool english, out string value)
    {
        var dialog = new TextPromptWindow(prompt, caption, defaultValue, english);
        if (Application.Current?.MainWindow is { IsVisible: true } owner) dialog.Owner = owner;
        var accepted = dialog.ShowDialog() == true;
        value = accepted ? dialog.ValueTextBox.Text : string.Empty;
        return accepted;
    }

    private void Accept_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
