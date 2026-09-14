using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using WinPure.Services;

namespace WinPure.ViewModels;

public sealed class RepairToolViewModel : ObservableObject
{
    public required RepairTool Tool { get; init; }

    public string Name => Loc.T(Tool.Name);
    public string Description => Loc.T(Tool.Description);
    public string Icon => Tool.Icon;

    private string _statusText = "";
    public string StatusText { get => _statusText; set => Set(ref _statusText, value); }

    /// <summary>The latest line the running tool printed, shown live so the user can see it is working.</summary>
    private string _liveOutput = "";
    public string LiveOutput { get => _liveOutput; set => Set(ref _liveOutput, value); }

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
        _timer.Tick += (_, _) => StatusText = Loc.F("Running… {0}", Format(_clock.Elapsed));
    }

    internal CancellationToken BeginRun()
    {
        _cts = new CancellationTokenSource();
        IsRunning = true;
        LiveOutput = "";
        _clock.Restart();
        StatusText = Loc.F("Running… {0}", "0:00");
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
        StatusText = Loc.T("Cancelling…");
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
        if (!Main.ConfirmDespiteGuards(Loc.F("run \"{0}\"", vm.Name), SystemGuards.ForRepair)) return;
        if (tool.ConfirmText is not null)
        {
            var answer = MessageBox.Show(Loc.T(tool.ConfirmText), $"WinPure — {vm.Name}",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;
        }

        Main.IsBusy = true;
        var token = vm.BeginRun();
        Main.StatusText = Loc.F("Running: {0}", vm.Name);
        LogService.Log($"Repair started: {tool.Name}");
        try
        {
            // A tool that is safe to cancel may also die with the app. One that is not — SFC/DISM —
            // keeps running if WinPure is closed: killing DISM midway is the harm Cancellable exists
            // to prevent, and closing the window must not do it through the back door.
            // Stream each output line to the tool's live view. The lines arrive on a background thread, so hop to
            // the UI thread to update the bound property.
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            void OnLine(string line)
            {
                var t = line.Trim();
                if (t.Length == 0) return;
                if (dispatcher is not null) dispatcher.BeginInvoke(() => vm.LiveOutput = t);
                else vm.LiveOutput = t;
            }
            var result = await Task.Run(() => PowerShellRunner.Run(tool.Script, tool.TimeoutMs, token, dieWithApp: tool.Cancellable, onOutputLine: OnLine));
            string elapsed = RepairToolViewModel.Format(vm.Elapsed);
            string lastLine = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .LastOrDefault()?.Trim() ?? "";
            if (result.Success)
            {
                // The script's own words are English; a tool that has a DoneText says it in the user's
                // language. Output from Windows itself (sfc, netsh) is already in the user's language.
                string done = tool.DoneText is { } doneText ? Loc.F(doneText, lastLine)
                    : string.IsNullOrEmpty(lastLine) ? Loc.T("Done.")
                    : lastLine;
                vm.StatusText = done + $" ({elapsed})";
                Main.StatusText = Loc.F("{0}: done in {1}.", vm.Name, elapsed);
                LogService.Log($"Repair finished in {elapsed}: {tool.Name} — {lastLine}");
                if (tool.RequiresRestart)
                    MessageBox.Show(Loc.T("Restart your PC for the changes to take full effect."), "WinPure",
                        MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else if (result.Cancelled)
            {
                vm.StatusText = Loc.F("Cancelled after {0}.", elapsed);
                Main.StatusText = Loc.F("{0}: cancelled.", vm.Name);
                LogService.Log($"Repair cancelled by the user after {elapsed}: {tool.Name}");
            }
            else if (result.TimedOut)
            {
                vm.StatusText = Loc.F("Gave up after {0} — it was still running and was stopped.", elapsed);
                Main.StatusText = Loc.F("{0}: timed out.", vm.Name);
                LogService.Log($"Repair timed out after {elapsed}: {tool.Name}");
            }
            else
            {
                vm.StatusText = string.IsNullOrWhiteSpace(result.Error)
                    ? Loc.T("Failed: see log")
                    : Loc.F("Failed: {0}", result.Error);
                Main.StatusText = Loc.F("{0}: failed (see log).", vm.Name);
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
