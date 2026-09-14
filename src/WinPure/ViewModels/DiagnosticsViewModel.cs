using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using Microsoft.Win32;
using WinPure.Services;

namespace WinPure.ViewModels;

/// <summary>One labelled fact on the Diagnostics page.</summary>
public sealed class InfoRow
{
    public required string Label { get; init; }
    public required string Value { get; init; }
}

public sealed class DiagnosticsViewModel : PageViewModel
{
    public required MainViewModel Main { get; init; }
    public ObservableCollection<InfoRow> Rows { get; } = new();
    public RelayCommand CopyCommand { get; }
    public RelayCommand ExportCommand { get; }

    private string _status = "";
    public string Status { get => _status; set => Set(ref _status, value); }

    public bool HasLoaded { get; private set; }

    public DiagnosticsViewModel()
    {
        CopyCommand = new RelayCommand(_ => Copy());
        ExportCommand = new RelayCommand(_ => Export());
    }

    /// <summary>Reads the read-only system facts when the page opens.</summary>
    public void Load()
    {
        Rows.Clear();
        foreach (var (key, value) in DiagnosticsService.Info())
            Rows.Add(new InfoRow { Label = LabelText(key), Value = value });
        HasLoaded = true;
    }

    // The service returns stable English keys; each maps to a translated label here so the strings register
    // with the translation test (Loc.T needs a literal argument).
    private static string LabelText(string key) => key switch
    {
        "Windows" => Loc.T("Windows"),
        "Version" => Loc.T("Version"),
        "Install date" => Loc.T("Install date"),
        "Processor" => Loc.T("Processor"),
        "Memory" => Loc.T("Memory"),
        "Uptime" => Loc.T("Uptime"),
        "Device" => Loc.T("Device"),
        "WinPure" => Loc.T("WinPure"),
        _ => key,
    };

    private void Copy()
    {
        var sb = new StringBuilder();
        foreach (var r in Rows) sb.AppendLine($"{r.Label}: {r.Value}");
        try { Clipboard.SetText(sb.ToString()); Status = Loc.T("Copied the system info to the clipboard."); }
        catch { Status = Loc.T("Could not copy to the clipboard."); }
    }

    private void Export()
    {
        var dialog = new SaveFileDialog
        {
            Title = Loc.T("Export diagnostics"),
            FileName = "winpure-diagnostics.zip",
            Filter = "Zip (*.zip)|*.zip",
        };
        if (dialog.ShowDialog() != true) return;

        var (ok, error) = DiagnosticsService.Export(dialog.FileName);
        Status = ok
            ? Loc.F("Saved the diagnostics bundle to {0}", dialog.FileName)
            : Loc.F("Could not save the bundle: {0}", error ?? "");
    }
}
