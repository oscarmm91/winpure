using System.IO;
using Microsoft.Win32;

namespace WinPure.Services;

/// <summary>One labelled hardware fact.</summary>
public sealed record HardwareRow(string Label, string Value);

/// <summary>A named group of hardware facts (Processor, Memory, Graphics, Storage, Motherboard).</summary>
public sealed record HardwareSection(string Title, IReadOnlyList<HardwareRow> Rows);

/// <summary>
/// Read-only hardware facts for the Hardware page. Everything is read from the registry, <see cref="Environment"/>,
/// GlobalMemoryStatusEx (via <see cref="MemoryService"/>) and <see cref="DriveInfo"/> — deliberately NOT via WMI,
/// so there is no <c>System.Management</c> NuGet dependency and no kernel driver (this delivers the read-only
/// hardware panel Oscar asked for without the dependency a live-sensor panel would need). Never throws: an
/// unreadable field is skipped and an empty section is dropped, so the worst case is a shorter list.
/// The Title and the fixed Row.Label are stable English keys the view model maps to translated text.
/// </summary>
public static class HardwareService
{
    // Integrated GPUs report this exact capped value (0x7FFFF000, ~2 GB) as their "memory" — it is shared system
    // RAM, not real VRAM, so showing it is misleading. Only the reliable qwMemorySize (discrete cards) is trusted
    // for a size; this capped MemorySize is dropped.
    private const ulong IntegratedMemoryCap = 0x7FFFF000;

    public static List<HardwareSection> Info()
    {
        var sections = new List<HardwareSection>();

        // Processor — registry + Environment, all invariant.
        {
            var rows = new List<HardwareRow>();
            try
            {
                using var cpu = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                Add(rows, "Model", (cpu?.GetValue("ProcessorNameString") as string)?.Trim());
                if (cpu?.GetValue("~MHz") is int mhz && mhz > 0)
                    Add(rows, "Base speed", $"{mhz / 1000.0:0.0} GHz");
            }
            catch { }
            try { Add(rows, "Logical processors", Environment.ProcessorCount.ToString()); } catch { }
            AddSection(sections, "Processor", rows);
        }

        // Memory — GlobalMemoryStatusEx, never touches real memory beyond reading counters.
        {
            var rows = new List<HardwareRow>();
            try
            {
                var mem = MemoryService.Query();
                if (mem.TotalBytes > 0)
                {
                    Add(rows, "Total", FormatGB(mem.TotalBytes));
                    Add(rows, "Available", FormatGB(mem.AvailableBytes));
                }
            }
            catch { }
            AddSection(sections, "Memory", rows);
        }

        // Graphics — the display-adapter class key. DriverDesc is the device name; qwMemorySize is real VRAM.
        {
            var rows = new List<HardwareRow>();
            try
            {
                using var cls = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // a DisplayLink dock lists the same adapter twice
                if (cls is not null)
                    foreach (var sub in cls.GetSubKeyNames())
                    {
                        if (sub.Length != 4 || !int.TryParse(sub, out _)) continue; // 0000, 0001, ... — skip Properties/Configuration
                        using var g = cls.OpenSubKey(sub);
                        var desc = (g?.GetValue("DriverDesc") as string)?.Trim();
                        if (string.IsNullOrWhiteSpace(desc) || !seen.Add(desc)) continue;
                        var vram = ReadAdapterMemory(g);
                        Add(rows, "Adapter", vram is null ? desc : $"{desc} ({FormatGB(vram.Value)})");
                    }
            }
            catch { }
            AddSection(sections, "Graphics", rows);
        }

        // Storage — fixed drives only. DriveInfo, no registry.
        {
            var rows = new List<HardwareRow>();
            try
            {
                foreach (var d in DriveInfo.GetDrives())
                {
                    try
                    {
                        if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                        Add(rows, d.Name.TrimEnd('\\'),
                            Loc.F("{0}, {1} free · {2}", FormatGB((ulong)d.TotalSize), FormatGB((ulong)d.AvailableFreeSpace), d.DriveFormat));
                    }
                    catch { }
                }
            }
            catch { }
            AddSection(sections, "Storage", rows);
        }

        // Motherboard + firmware — SMBIOS values mirrored into the registry, all invariant.
        {
            var rows = new List<HardwareRow>();
            try
            {
                using var bios = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
                var boardMfr = CleanOem(bios?.GetValue("BaseBoardManufacturer") as string);
                var boardProd = CleanOem(bios?.GetValue("BaseBoardProduct") as string);
                var board = string.Join(" ", new[] { boardMfr, boardProd }.Where(s => s is not null));
                if (!string.IsNullOrWhiteSpace(board)) Add(rows, "Motherboard", board);

                var biosVendor = CleanOem(bios?.GetValue("BIOSVendor") as string);
                var biosVer = (bios?.GetValue("BIOSVersion") as string)?.Trim();
                var biosDate = (bios?.GetValue("BIOSReleaseDate") as string)?.Trim();
                var biosText = string.Join(" ", new[] { biosVendor, biosVer }.Where(s => !string.IsNullOrWhiteSpace(s)));
                if (!string.IsNullOrWhiteSpace(biosDate)) biosText = $"{biosText} ({biosDate})".Trim();
                if (!string.IsNullOrWhiteSpace(biosText)) Add(rows, "BIOS", biosText);
            }
            catch { }
            AddSection(sections, "Motherboard", rows);
        }

        return sections;
    }

    private static void Add(List<HardwareRow> rows, string label, string? value)
    { if (!string.IsNullOrWhiteSpace(value)) rows.Add(new HardwareRow(label, value!)); }

    private static void AddSection(List<HardwareSection> sections, string title, List<HardwareRow> rows)
    { if (rows.Count > 0) sections.Add(new HardwareSection(title, rows)); }

    // OEM string fields are frequently unfilled placeholders; treat those as "not known".
    private static string? CleanOem(string? s)
    {
        s = s?.Trim();
        if (string.IsNullOrWhiteSpace(s)) return null;
        string[] junk = { "System Product Name", "System manufacturer", "To Be Filled By O.E.M.",
                          "Default string", "None", "O.E.M.", "Not Applicable", "Not Specified" };
        return junk.Any(j => string.Equals(j, s, StringComparison.OrdinalIgnoreCase)) ? null : s;
    }

    private static ulong? ReadAdapterMemory(RegistryKey? g)
    {
        if (g is null) return null;
        // qwMemorySize is a REG_QWORD on modern drivers and is real VRAM. HardwareInformation.MemorySize is an
        // older 4/8-byte REG_BINARY that integrated GPUs report as a capped shared value — dropped below.
        try { if (g.GetValue("HardwareInformation.qwMemorySize") is long q && q > 0) return (ulong)q; } catch { }
        try
        {
            if (g.GetValue("HardwareInformation.MemorySize") is byte[] b && (b.Length == 4 || b.Length == 8))
            {
                ulong v = 0;
                for (int i = 0; i < b.Length; i++) v |= (ulong)b[i] << (8 * i);
                if (v > 0 && v != IntegratedMemoryCap) return v;
            }
        }
        catch { }
        return null;
    }

    private static string FormatGB(ulong bytes) => $"{bytes / 1024.0 / 1024 / 1024:0.0} GB";
}
