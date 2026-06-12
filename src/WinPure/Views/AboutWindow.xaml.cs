using System.Diagnostics;
using System.Windows;

namespace WinPure.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        var version = typeof(AboutWindow).Assembly.GetName().Version;
        if (version is not null)
            VersionText.Text = $"v{version.ToString(3)}";
    }

    private void OpenLink(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string url })
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
}
