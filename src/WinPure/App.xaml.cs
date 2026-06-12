using System.Windows;
using WinPure.Services;

namespace WinPure;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        LogService.Log($"WinPure started (v{typeof(App).Assembly.GetName().Version})");

        DispatcherUnhandledException += (_, args) =>
        {
            LogService.Log($"UNHANDLED: {args.Exception}");
            MessageBox.Show($"Unexpected error:\n{args.Exception.Message}\n\nDetails were written to %AppData%\\WinPure\\Logs.",
                "WinPure", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
    }
}
