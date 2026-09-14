using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using WinPure.Services;

namespace WinPure.ViewModels;

/// <summary>One labelled hardware fact on the Hardware page.</summary>
public sealed class HardwareInfoRow
{
    public required string Label { get; init; }
    public required string Value { get; init; }
}

/// <summary>A titled group of hardware facts.</summary>
public sealed class HardwareSectionViewModel
{
    public required string Title { get; init; }
    public ObservableCollection<HardwareInfoRow> Rows { get; } = new();
}

public sealed class HardwareViewModel : PageViewModel
{
    public required MainViewModel Main { get; init; }
    public ObservableCollection<HardwareSectionViewModel> Sections { get; } = new();
    public RelayCommand CopyCommand { get; }

    private string _status = "";
    public string Status { get => _status; set => Set(ref _status, value); }

    public bool HasLoaded { get; private set; }

    public HardwareViewModel() => CopyCommand = new RelayCommand(_ => Copy());

    /// <summary>Reads the read-only hardware facts when the page opens.</summary>
    public void Load()
    {
        Sections.Clear();
        foreach (var section in HardwareService.Info())
        {
            var vm = new HardwareSectionViewModel { Title = SectionText(section.Title) };
            foreach (var row in section.Rows)
                vm.Rows.Add(new HardwareInfoRow { Label = LabelText(row.Label), Value = row.Value });
            Sections.Add(vm);
        }
        HasLoaded = true;
    }

    // The service returns stable English keys; map each to a translated string here so the strings register
    // with the translation test (Loc.T needs a literal argument). Unknown labels (drive letters) pass through.
    private static string SectionText(string key) => key switch
    {
        "Processor" => Loc.T("Processor"),
        "Memory" => Loc.T("Memory"),
        "Graphics" => Loc.T("Graphics"),
        "Storage" => Loc.T("Storage"),
        "Motherboard" => Loc.T("Motherboard"),
        _ => key,
    };

    private static string LabelText(string key) => key switch
    {
        "Model" => Loc.T("Model"),
        "Base speed" => Loc.T("Base speed"),
        "Logical processors" => Loc.T("Logical processors"),
        "Total" => Loc.T("Total"),
        "Available" => Loc.T("Available"),
        "Adapter" => Loc.T("Adapter"),
        "Motherboard" => Loc.T("Motherboard"),
        "BIOS" => Loc.T("BIOS"),
        _ => key,
    };

    private void Copy()
    {
        var sb = new StringBuilder();
        foreach (var section in Sections)
        {
            sb.AppendLine(section.Title);
            foreach (var row in section.Rows) sb.AppendLine($"  {row.Label}: {row.Value}");
            sb.AppendLine();
        }
        try { Clipboard.SetText(sb.ToString()); Status = Loc.T("Copied the system info to the clipboard."); }
        catch { Status = Loc.T("Could not copy to the clipboard."); }
    }
}
