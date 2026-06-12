using System.Collections.ObjectModel;
using System.Windows;
using WinPure.Services;

namespace WinPure.ViewModels;

public sealed class RepairToolViewModel : ObservableObject
{
    public required RepairTool Tool { get; init; }

    public string Name => Tool.Name;
    public string Description => Tool.Description;
    public string Icon => Tool.Icon;

    private string _statusText = "";
    public string StatusText { get => _statusText; set => Set(ref _statusText, value); }

    private bool _isRunning;
    public bool IsRunning { get => _isRunning; set => Set(ref _isRunning, value); }
}

public sealed class RepairViewModel : PageViewModel
{
    public required MainViewModel Main { get; init; }
    public ObservableCollection<RepairToolViewModel> Tools { get; } = new();
    public RelayCommand RunCommand { get; }

    public RepairViewModel()
    {
        foreach (var tool in RepairCatalog.Build())
            Tools.Add(new RepairToolViewModel { Tool = tool });
        RunCommand = new RelayCommand(p => _ = RunAsync((RepairToolViewModel)p!), _ => Main is { IsBusy: false });
    }

    private async Task RunAsync(RepairToolViewModel vm)
    {
        var tool = vm.Tool;
        if (tool.ConfirmText is not null)
        {
            var answer = MessageBox.Show(tool.ConfirmText, $"WinPure — {tool.Name}",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;
        }

        Main.IsBusy = true;
        vm.IsRunning = true;
        vm.StatusText = "Running…";
        Main.StatusText = $"Running: {tool.Name}";
        LogService.Log($"Repair started: {tool.Name}");
        try
        {
            var result = await Task.Run(() => PowerShellRunner.Run(tool.Script, tool.TimeoutMs));
            string lastLine = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .LastOrDefault()?.Trim() ?? "";
            if (result.Success)
            {
                vm.StatusText = string.IsNullOrEmpty(lastLine) ? "Done." : lastLine;
                Main.StatusText = $"{tool.Name}: done.";
                LogService.Log($"Repair finished: {tool.Name} — {lastLine}");
                if (tool.RequiresRestart)
                    MessageBox.Show("Restart your PC for the changes to take full effect.", "WinPure",
                        MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                vm.StatusText = $"Failed: {(string.IsNullOrWhiteSpace(result.Error) ? "see log" : result.Error)}";
                Main.StatusText = $"{tool.Name}: failed (see log).";
                LogService.Log($"Repair FAILED: {tool.Name} — {result.Error}");
            }
        }
        finally
        {
            vm.IsRunning = false;
            Main.IsBusy = false;
        }
    }
}
