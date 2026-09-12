namespace WinPure.Services;

/// <summary>Reads and switches Windows optional features. Swappable so tests never run DISM.</summary>
public interface IFeatureBackend
{
    /// <summary>"Enabled", "Disabled" or "Missing" (not part of this Windows build). Throws when it cannot be read.</summary>
    string Read(string featureName);
    /// <summary>Turns a feature on or off. Throws when Windows refuses.</summary>
    void Write(string featureName, bool enable);
}

/// <summary>
/// The one way a feature state reaches the system — including a state read back from a backup, which
/// is checked first for the same reason as <see cref="SystemState.Restore"/>: backups are plain files
/// in the user's profile while WinPure runs elevated.
/// </summary>
public static class OptionalFeatures
{
    public const string Enabled = "Enabled";
    public const string Disabled = "Disabled";
    public const string Missing = "Missing";

    internal static IFeatureBackend Backend { get; set; } = new DismFeatureBackend();

    /// <summary>DISM feature names: letters, digits, dash, underscore and dot — nothing that could end a quoted string.</summary>
    public static bool IsValidName(string? name) =>
        !string.IsNullOrEmpty(name) && name.Length <= 128 &&
        name.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');

    /// <summary>
    /// Win32_OptionalFeature.InstallState: 1 enabled, 2 disabled, 3 absent (disabled with its files
    /// removed, which Windows can still turn back on). Anything else is unknown.
    /// </summary>
    public static string? FromInstallState(int installState) => installState switch
    {
        1 => Enabled,
        2 or 3 => Disabled,
        _ => null,
    };

    /// <summary>Writes back a feature state recorded in a backup, after checking both the name and the state.</summary>
    public static void Restore(string? featureName, string? state)
    {
        if (!IsValidName(featureName))
            throw new InvalidOperationException($"The backup names an invalid Windows feature '{Clip(featureName)}'; refused.");
        if (state is not (Enabled or Disabled))
            throw new InvalidOperationException($"The backup holds an invalid state '{Clip(state)}' for {featureName}; refused.");
        Backend.Write(featureName!, state == Enabled);
    }

    private static string Clip(string? s) => s is null ? "(none)" : s.Length <= 60 ? s : s[..60] + "...";
}

internal sealed class DismFeatureBackend : IFeatureBackend
{
    public string Read(string featureName)
    {
        if (!OptionalFeatures.IsValidName(featureName))
            throw new ArgumentException($"Invalid Windows feature name '{featureName}'.");

        // Win32_OptionalFeature answers without elevation, where DISM's own cmdlets refuse; about 0.6 s
        // for one feature on 25H2. An unknown name returns nothing rather than an error.
        var result = PowerShellRunner.Run($$"""
            $f = @(Get-CimInstance Win32_OptionalFeature -Filter "Name='{{featureName}}'" -ErrorAction Stop)
            if ($f.Count -eq 0) { 'missing' } else { [string]$f[0].InstallState }
            """, 30_000, dieWithApp: true);

        string output = result.Output.Trim();
        if (result.Success && output == "missing") return OptionalFeatures.Missing;
        if (result.Success && int.TryParse(output, out int installState) &&
            OptionalFeatures.FromInstallState(installState) is { } state)
            return state;
        throw new InvalidOperationException(
            $"Could not read the state of the Windows feature {featureName}: {(result.TimedOut ? "timed out" : result.Error)}");
    }

    public void Write(string featureName, bool enable)
    {
        if (!OptionalFeatures.IsValidName(featureName))
            throw new ArgumentException($"Invalid Windows feature name '{featureName}'.");

        // No -All: it would switch parent features on too, and undoing this one feature could not
        // switch them off again. A tweak that needs a parent lists it as its own action.
        // -NoRestart: WinPure says a restart is needed instead of rebooting on its own.
        // dieWithApp stays false: DISM cut off halfway can leave a feature half-installed.
        string verb = enable ? "Enable-WindowsOptionalFeature" : "Disable-WindowsOptionalFeature";
        PowerShellRunner.RunOrThrow(
            $"{verb} -Online -FeatureName '{featureName}' -NoRestart -ErrorAction Stop | Out-Null",
            $"{(enable ? "Turning on" : "Turning off")} {featureName}", 1_800_000);
    }
}
