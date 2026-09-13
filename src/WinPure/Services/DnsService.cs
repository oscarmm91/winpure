using System.Net;
using System.Text.Json;
using WinPure.Models;

namespace WinPure.Services;

/// <summary>A DNS resolver the DNS page can switch to. Empty <see cref="Servers"/> means "back to automatic (DHCP)".</summary>
public sealed record DnsPreset(string Id, string Name, string Description, string Icon, string[] Servers);

/// <summary>One network adapter and the DNS servers it currently uses.</summary>
public sealed record DnsAdapter(string Name, string[] Servers, bool Dhcp);

/// <summary>Reads and sets the DNS servers of the machine's adapters. Swappable so tests never touch the network.</summary>
public interface IDnsBackend
{
    /// <summary>The "up" adapters and their current IPv4 DNS servers.</summary>
    IReadOnlyList<DnsAdapter> ReadAdapters();
    /// <summary>Sets an adapter's DNS servers; an empty list resets it to automatic (DHCP).</summary>
    void SetServers(string adapterName, string[] servers);
    /// <summary>Clears the resolver cache.</summary>
    void FlushCache();
}

/// <summary>
/// The DNS page's engine. Switching resolver is a real, reversible change: the current servers of every
/// adapter are captured into the backup session BEFORE anything changes (the same before-the-change
/// invariant as the tweak engine), so Restore puts back exactly what each adapter had — a custom DNS, or
/// automatic. Only the measured state is stored, never a command, and a state read from a backup is
/// validated (every entry an IP, or "DHCP") before it reaches the system.
/// </summary>
public static class DnsService
{
    public const string EntryType = "dns";

    internal static IDnsBackend Backend { get; set; } = new DnsCli();

    /// <summary>Tests swap in a fake so nothing runs against the real network. Returns the backend it replaced.</summary>
    internal static IDnsBackend Swap(IDnsBackend backend)
    {
        var previous = Backend;
        Backend = backend;
        return previous;
    }

    public static IReadOnlyList<DnsPreset> Presets { get; } = new[]
    {
        new DnsPreset("dns-automatic", "Automatic (DHCP)", "Let each network decide — the default. Undoes a manual DNS choice.", Glyph(0xE774), Array.Empty<string>()),
        new DnsPreset("dns-cloudflare", "Cloudflare", "1.1.1.1 — fast and privacy-focused.", Glyph(0xE753), new[] { "1.1.1.1", "1.0.0.1" }),
        new DnsPreset("dns-cloudflare-malware", "Cloudflare (blocks malware)", "1.1.1.2 — Cloudflare with malware-domain filtering.", Glyph(0xEA18), new[] { "1.1.1.2", "1.0.0.2" }),
        new DnsPreset("dns-google", "Google Public DNS", "8.8.8.8 — a reliable, widely used resolver.", Glyph(0xE774), new[] { "8.8.8.8", "8.8.4.4" }),
        new DnsPreset("dns-quad9", "Quad9", "9.9.9.9 — a non-profit resolver that blocks known-malicious domains.", Glyph(0xEA18), new[] { "9.9.9.9", "149.112.112.112" }),
        new DnsPreset("dns-opendns", "OpenDNS", "208.67.222.222 — a long-standing public resolver.", Glyph(0xE774), new[] { "208.67.222.222", "208.67.220.220" }),
        new DnsPreset("dns-adguard", "AdGuard DNS", "94.140.14.14 — blocks ads and trackers at the network level.", Glyph(0xE7BA), new[] { "94.140.14.14", "94.140.15.15" }),
    };

    private static string Glyph(int code) => ((char)code).ToString();

    /// <summary>
    /// Applies a preset to every up adapter, after capturing each adapter's current servers into the
    /// backup session and flushing that snapshot to disk. Logs failures per adapter rather than throwing,
    /// so one stubborn NIC never stops the rest. Returns how many adapters changed and how many there were.
    /// </summary>
    public static (int changed, int total) Apply(DnsPreset preset, BackupSession session, Action flushBeforeChanging)
    {
        var adapters = Backend.ReadAdapters();
        if (adapters.Count == 0) return (0, 0);

        foreach (var adapter in adapters)
            session.Entries.Add(new BackupEntry
            {
                Type = EntryType,
                TweakId = EntryType,
                TweakName = $"DNS: {adapter.Name}",
                ValueName = adapter.Name,
                Existed = true,
                Value = adapter.Dhcp || adapter.Servers.Length == 0 ? "DHCP" : string.Join(",", adapter.Servers),
            });

        flushBeforeChanging();

        int changed = 0;
        foreach (var adapter in adapters)
        {
            try { Backend.SetServers(adapter.Name, preset.Servers); changed++; }
            catch (Exception ex) { LogService.Log($"DNS: could not change {adapter.Name}: {ex.Message}"); }
        }
        try { Backend.FlushCache(); } catch { }
        LogService.Log($"DNS: applied {preset.Id} to {changed} of {adapters.Count} adapter(s).");
        return (changed, adapters.Count);
    }

    /// <summary>Puts one adapter's DNS back to what a backup recorded — a validated server list, or automatic.</summary>
    public static void RestoreEntry(BackupEntry entry)
    {
        if (entry.ValueName is null || entry.Value is null) return;
        string[] servers = entry.Value.Equals("DHCP", StringComparison.OrdinalIgnoreCase)
            ? Array.Empty<string>()
            : entry.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        // A backup is untrusted input, and this runs elevated: only IP addresses may reach the command.
        foreach (var server in servers)
            if (!IPAddress.TryParse(server, out _))
                throw new InvalidOperationException($"The backup holds an invalid DNS server '{Clip(server)}'; refused.");
        Backend.SetServers(entry.ValueName, servers);
    }

    /// <summary>True when a value is a comma-separated list of IPs, or "DHCP" — what the policy allows for a dns entry.</summary>
    public static bool IsValidServerList(string? value)
    {
        if (value is null) return false;
        if (value.Equals("DHCP", StringComparison.OrdinalIgnoreCase)) return true;
        var parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length > 0 && parts.All(p => IPAddress.TryParse(p, out _));
    }

    private static string Clip(string s) => s.Length <= 40 ? s : s[..40] + "...";
}

/// <summary>Talks to Windows' DNS client through PowerShell. Never exercised by tests — they swap in a fake.</summary>
internal sealed class DnsCli : IDnsBackend
{
    public IReadOnlyList<DnsAdapter> ReadAdapters()
    {
        // One read-only pass, emitting JSON so no localized text is parsed.
        var result = PowerShellRunner.Run(
            "Get-NetAdapter -Physical -ErrorAction SilentlyContinue | Where-Object Status -eq 'Up' | ForEach-Object { " +
            "$dns = (Get-DnsClientServerAddress -InterfaceIndex $_.ifIndex -AddressFamily IPv4 -ErrorAction SilentlyContinue).ServerAddresses; " +
            "$src = (Get-DnsClient -InterfaceIndex $_.ifIndex -ErrorAction SilentlyContinue).ConnectionSpecificSuffix; " +
            "[pscustomobject]@{ Name = $_.Name; Servers = @($dns); Dhcp = -not ($dns) } } | ConvertTo-Json -Depth 3 -Compress",
            60_000, dieWithApp: true);
        if (!result.Success || string.IsNullOrWhiteSpace(result.Output)) return Array.Empty<DnsAdapter>();
        try
        {
            var adapters = new List<DnsAdapter>();
            using var doc = JsonDocument.Parse(result.Output.Trim().StartsWith("[") ? result.Output : "[" + result.Output + "]");
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                string name = el.GetProperty("Name").GetString() ?? "";
                var servers = el.TryGetProperty("Servers", out var s) && s.ValueKind == JsonValueKind.Array
                    ? s.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToArray()
                    : Array.Empty<string>();
                if (name.Length > 0) adapters.Add(new DnsAdapter(name, servers, servers.Length == 0));
            }
            return adapters;
        }
        catch (Exception ex)
        {
            LogService.Log($"DNS: could not read adapters: {ex.Message}");
            return Array.Empty<DnsAdapter>();
        }
    }

    public void SetServers(string adapterName, string[] servers)
    {
        string quotedName = PowerShellRunner.Quote(adapterName);
        string command = servers.Length == 0
            ? $"Set-DnsClientServerAddress -InterfaceAlias {quotedName} -ResetServerAddresses -ErrorAction Stop"
            : $"Set-DnsClientServerAddress -InterfaceAlias {quotedName} -ServerAddresses {string.Join(",", servers.Select(PowerShellRunner.Quote))} -ErrorAction Stop";
        PowerShellRunner.RunOrThrow(command, "Setting DNS servers");
    }

    public void FlushCache() => PowerShellRunner.Run("Clear-DnsClientCache -ErrorAction SilentlyContinue", 15_000);
}
