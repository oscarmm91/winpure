using System.Collections.ObjectModel;
using System.Windows;
using WinPure.Services;

namespace WinPure.ViewModels;

/// <summary>One resolver on the DNS page.</summary>
public sealed class DnsPresetViewModel : ObservableObject
{
    public required DnsPreset Preset { get; init; }

    public string Name => Loc.T(Preset.Name);
    public string Description => Loc.T(Preset.Description);
    public string Icon => Preset.Icon;
}

public sealed class DnsViewModel : PageViewModel
{
    public required MainViewModel Main { get; init; }
    public ObservableCollection<DnsPresetViewModel> Presets { get; } = new();
    public RelayCommand ApplyCommand { get; }

    private readonly TweakEngine _engine;

    private string _summary = "";
    public string Summary { get => _summary; set => Set(ref _summary, value); }

    public bool HasLoaded { get; private set; }

    public DnsViewModel(TweakEngine engine)
    {
        _engine = engine;
        foreach (var preset in DnsService.Presets)
            Presets.Add(new DnsPresetViewModel { Preset = preset });
        ApplyCommand = new RelayCommand(p => _ = ApplyAsync((DnsPresetViewModel)p!), _ => Main is { IsBusy: false });
    }

    /// <summary>Reads the current DNS of every adapter when the page opens, and shows it.</summary>
    public async Task LoadAsync()
    {
        Summary = Loc.T("Reading your current DNS…");
        var adapters = await Task.Run(() => DnsService.Backend.ReadAdapters());
        Summary = adapters.Count == 0
            ? Loc.T("No active network adapter was found.")
            : Loc.F("Now: {0}", string.Join("   ·   ", adapters.Select(a =>
                a.Dhcp || a.Servers.Length == 0 ? Loc.F("{0}: automatic", a.Name) : $"{a.Name}: {string.Join(", ", a.Servers)}")));
        HasLoaded = true;
    }

    private async Task ApplyAsync(DnsPresetViewModel vm)
    {
        var preset = vm.Preset;
        if (!Main.ConfirmDespiteGuards(Loc.F("switch DNS to {0}", vm.Name), SystemGuards.ForRepair)) return;
        var answer = MessageBox.Show(
            Loc.F("Set every network adapter's DNS to {0}? WinPure saves your current DNS first, so Restore can put it back.", vm.Name),
            Loc.T("WinPure — DNS servers"), MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes);
        if (answer != MessageBoxResult.Yes) return;

        Main.IsBusy = true;
        Main.StatusText = Loc.F("Switching DNS to {0}…", vm.Name);
        try
        {
            var (changed, total) = await Task.Run(() => _engine.ApplyDns(preset));
            Main.RefreshBackupCount();
            Main.StatusText = total == 0
                ? Loc.T("No active network adapter was found.")
                : Loc.F("DNS set to {0} on {1} of {2} adapter(s).", vm.Name, changed, total);
        }
        finally
        {
            Main.IsBusy = false;
        }
        await LoadAsync();
    }
}
