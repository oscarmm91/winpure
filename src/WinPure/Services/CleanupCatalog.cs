namespace WinPure.Services;

/// <summary>
/// The junk WinPure offers to delete on the Cleanup page. Every target here is regenerable (a cache Windows
/// rebuilds) or clearly labelled as the user's own data (the Recycle Bin), and nothing does a blind
/// whole-drive sweep by extension — the mined cleaners' dangerous ".tmp/.cache across all of C:" and
/// registry-cleaning targets were deliberately left out. Vetted 2026-09-13 against three cleaner repos.
/// </summary>
public static class CleanupCatalog
{
    private static string Glyph(int code) => ((char)code).ToString();

    public static IReadOnlyList<CleanupTarget> Build() => new[]
    {
        new CleanupTarget
        {
            Id = "clean-temp",
            Name = "Temporary files",
            Description = "Your temp folder and the Windows temp folder. Windows recreates these as needed.",
            Icon = Glyph(0xE74D),
            Roots = new[] { @"%TEMP%", @"%SystemRoot%\Temp" },
        },
        new CleanupTarget
        {
            Id = "clean-wu-download",
            Name = "Windows Update cache",
            Description = "Update files Windows already installed or can download again (SoftwareDistribution\\Download).",
            Icon = Glyph(0xE896),
            Roots = new[] { @"%SystemRoot%\SoftwareDistribution\Download" },
        },
        new CleanupTarget
        {
            Id = "clean-delivery-optimization",
            Name = "Delivery Optimization cache",
            Description = "Cached update pieces shared with other PCs on your network. Regenerated on demand.",
            Icon = Glyph(0xE968),
            Roots = new[] { @"%SystemRoot%\SoftwareDistribution\DeliveryOptimization" },
        },
        new CleanupTarget
        {
            Id = "clean-browser-cache",
            Name = "Browser caches",
            Description = "The web cache of every Chrome, Edge, Brave and Firefox profile. Not your history, cookies or passwords.",
            Icon = Glyph(0xE774),
            Roots = new[]
            {
                @"%LOCALAPPDATA%\Google\Chrome\User Data\*\Cache",
                @"%LOCALAPPDATA%\Google\Chrome\User Data\*\Code Cache",
                @"%LOCALAPPDATA%\Google\Chrome\User Data\*\GPUCache",
                @"%LOCALAPPDATA%\Google\Chrome\User Data\*\Service Worker\CacheStorage",
                @"%LOCALAPPDATA%\Microsoft\Edge\User Data\*\Cache",
                @"%LOCALAPPDATA%\Microsoft\Edge\User Data\*\Code Cache",
                @"%LOCALAPPDATA%\Microsoft\Edge\User Data\*\GPUCache",
                @"%LOCALAPPDATA%\Microsoft\Edge\User Data\*\Service Worker\CacheStorage",
                @"%LOCALAPPDATA%\BraveSoftware\Brave-Browser\User Data\*\Cache",
                @"%LOCALAPPDATA%\BraveSoftware\Brave-Browser\User Data\*\Code Cache",
                @"%LOCALAPPDATA%\BraveSoftware\Brave-Browser\User Data\*\GPUCache",
                @"%LOCALAPPDATA%\Mozilla\Firefox\Profiles\*\cache2",
            },
        },
        new CleanupTarget
        {
            Id = "clean-thumbnails",
            Name = "Thumbnail and icon cache",
            Description = "Explorer's cached thumbnails and icons. Rebuilt automatically when you browse folders.",
            Icon = Glyph(0xE91B),
            Roots = new[] { @"%LOCALAPPDATA%\Microsoft\Windows\Explorer" },
            Globs = new[] { "thumbcache_*.db", "iconcache_*.db" },
        },
        new CleanupTarget
        {
            Id = "clean-inetcache",
            Name = "Internet cache (legacy)",
            Description = "The old WinINet/Internet Explorer temporary-files cache still used by some apps and the Store.",
            Icon = Glyph(0xE774),
            Roots = new[] { @"%LOCALAPPDATA%\Microsoft\Windows\INetCache" },
        },
        new CleanupTarget
        {
            Id = "clean-crash-dumps",
            Name = "Crash dumps and error reports",
            Description = "Queued crash dumps and Windows Error Reporting files. Only local diagnostics — safe to clear.",
            Icon = Glyph(0xE7BA),
            Roots = new[]
            {
                @"%LOCALAPPDATA%\CrashDumps",
                @"%SystemRoot%\Minidump",
                @"%LOCALAPPDATA%\Microsoft\Windows\WER\ReportQueue",
                @"%LOCALAPPDATA%\Microsoft\Windows\WER\ReportArchive",
                @"%LOCALAPPDATA%\Microsoft\Windows\WER\Temp",
                @"%ProgramData%\Microsoft\Windows\WER\ReportQueue",
                @"%ProgramData%\Microsoft\Windows\WER\ReportArchive",
                @"%ProgramData%\Microsoft\Windows\WER\Temp",
            },
        },
        new CleanupTarget
        {
            Id = "clean-shader-cache",
            Name = "GPU shader caches",
            Description = "Compiled shader caches (DirectX, NVIDIA, AMD, Intel). Some games may stutter once while they rebuild.",
            Icon = Glyph(0xE7FC),
            Roots = new[]
            {
                @"%LOCALAPPDATA%\Microsoft\DirectX Shader Cache",
                @"%LOCALAPPDATA%\NVIDIA\DXCache",
                @"%LOCALAPPDATA%\NVIDIA\GLCache",
                @"%LOCALAPPDATA%\AMD\DxCache",
                @"%LOCALAPPDATA%\Intel\ShaderCache",
            },
        },
        new CleanupTarget
        {
            Id = "clean-defender-history",
            Name = "Defender scan history",
            Description = "Microsoft Defender's scan and detection history. Not its signatures — protection is unaffected.",
            Icon = Glyph(0xEA18),
            Roots = new[] { @"%ProgramData%\Microsoft\Windows Defender\Scans\History" },
        },
        new CleanupTarget
        {
            Id = "clean-recycle-bin",
            Name = "Recycle Bin",
            Description = "Permanently delete everything in the Recycle Bin. These are your own deleted files — this cannot be undone.",
            Icon = Glyph(0xE74D),
            Kind = CleanupKind.RecycleBin,
            NeedsConfirm = true,
        },
    };
}
