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
            MessageBox.Show(Loc.F("Unexpected error:\n{0}\n\nDetails were written to %AppData%\\WinPure\\Logs.", args.Exception.Message),
                "WinPure", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
    }
}
