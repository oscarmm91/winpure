using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
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
    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (Set(ref _isRunning, value)) OnPropertyChanged(nameof(CanCancel));
        }
    }

    /// <summary>A Cancel button only appears for tools that are safe to kill mid-run.</summary>
    public bool CanCancel => IsRunning && Tool.Cancellable;

    private CancellationTokenSource? _cts;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Stopwatch _clock = new();

    public RepairToolViewModel()
    {
        // A long repair with a frozen "Running…" is indistinguishable from a hung app.
        // The ticking elapsed time is what tells the user it is still alive.
        _timer.Tick += (_, _) => StatusText = $"Running… {Format(_clock.Elapsed)}";
    }

    internal CancellationToken BeginRun()
    {
        _cts = new CancellationTokenSource();
        IsRunning = true;
        _clock.Restart();
        StatusText = "Running… 0:00";
        _timer.Start();
        return _cts.Token;
    }

    internal void EndRun()
    {
        _timer.Stop();
        _clock.Stop();
        IsRunning = false;
        _cts?.Dispose();
        _cts = null;
    }

    internal TimeSpan Elapsed => _clock.Elapsed;

    internal void Cancel()
    {
        if (!Tool.Cancellable) return;
        _cts?.Cancel();
        StatusText = "Cancelling…";
    }

    internal static string Format(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
}

public sealed class RepairViewModel : PageViewModel
{
    public required MainViewModel Main { get; init; }
    public ObservableCollection<RepairToolViewModel> Tools { get; } = new();
    public RelayCommand RunCommand { get; }
    public RelayCommand CancelCommand { get; }

    public RepairViewModel()
    {
        foreach (var tool in RepairCatalog.Build())
            Tools.Add(new RepairToolViewModel { Tool = tool });
        RunCommand = new RelayCommand(p => _ = RunAsync((RepairToolViewModel)p!), _ => Main is { IsBusy: false });
        CancelCommand = new RelayCommand(p => ((RepairToolViewModel)p!).Cancel(), p => p is RepairToolViewModel { CanCancel: true });
    }

    private async Task RunAsync(RepairToolViewModel vm)
    {
        var tool = vm.Tool;
        if (!Main.ConfirmDespiteGuards($"run \"{tool.Name}\"", SystemGuards.ForRepair)) return;
        if (tool.ConfirmText is not null)
        {
            var answer = MessageBox.Show(tool.ConfirmText, $"WinPure — {tool.Name}",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;
        }

        Main.IsBusy = true;
        var token = vm.BeginRun();
        Main.StatusText = $"Running: {tool.Name}";
        LogService.Log($"Repair started: {tool.Name}");
        try
        {
            // A tool that is safe to cancel may also die with the app. One that is not — SFC/DISM —
            // keeps running if WinPure is closed: killing DISM midway is the harm Cancellable exists
            // to prevent, and closing the window must not do it through the back door.
            var result = await Task.Run(() => PowerShellRunner.Run(tool.Script, tool.TimeoutMs, token, dieWithApp: tool.Cancellable));
            string elapsed = RepairToolViewModel.Format(vm.Elapsed);
            string lastLine = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .LastOrDefault()?.Trim() ?? "";
            if (result.Success)
            {
                vm.StatusText = (string.IsNullOrEmpty(lastLine) ? "Done." : lastLine) + $" ({elapsed})";
                Main.StatusText = $"{tool.Name}: done in {elapsed}.";
                LogService.Log($"Repair finished in {elapsed}: {tool.Name} — {lastLine}");
                if (tool.RequiresRestart)
                    MessageBox.Show("Restart your PC for the changes to take full effect.", "WinPure",
                        MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else if (result.Cancelled)
            {
                vm.StatusText = $"Cancelled after {elapsed}.";
                Main.StatusText = $"{tool.Name}: cancelled.";
                LogService.Log($"Repair cancelled by the user after {elapsed}: {tool.Name}");
            }
            else if (result.TimedOut)
            {
                vm.StatusText = $"Gave up after {elapsed} — it was still running and was stopped.";
                Main.StatusText = $"{tool.Name}: timed out.";
                LogService.Log($"Repair timed out after {elapsed}: {tool.Name}");
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
            vm.EndRun();
            Main.IsBusy = false;
        }
    }
}
