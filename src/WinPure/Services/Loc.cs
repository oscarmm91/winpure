using System.Globalization;
using System.IO;
using System.Text.Json;

namespace WinPure.Services;

/// <summary>
/// WinPure's text in the user's language. English stays in the code, where it always was, and is the key: a
/// translation is looked up by that exact English text in Resources/Strings.es.json. Text with no translation
/// shows in English — never as an empty control.
///
/// Keyed by the English text rather than by an id, on purpose: editing an English sentence orphans its old
/// translation and the tests fail, instead of the Spanish silently going on saying what the English no longer says.
///
/// Where a key comes from matters to those tests: they collect every literal passed to T, F and N and every
/// {l:Tr '...'} in XAML, plus the catalogs at run time. So a key must be ONE plain string literal — never
/// interpolated, never concatenated — or pass through a catalog.
/// </summary>
public static class Loc
{
    private const string SpanishResource = "WinPure.Strings.es.json";

    private static IReadOnlyDictionary<string, string> _table = new Dictionary<string, string>();

    /// <summary>"es" or "en".</summary>
    public static string Language { get; private set; } = "en";

    static Loc() => Use(DetectLanguage());

    /// <summary>
    /// Windows' display language decides; WINPURE_LANG=en or es overrides it. Read once at startup: views
    /// translate their text as they load, so switching later would leave half the window in each language.
    /// </summary>
    internal static string DetectLanguage()
    {
        string? forced = Environment.GetEnvironmentVariable("WINPURE_LANG");
        if (forced is "en" or "es") return forced;
        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "es" ? "es" : "en";
    }

    public static void Use(string language)
    {
        Language = language == "es" ? "es" : "en";
        _table = Language == "es" ? LoadSpanish() : new Dictionary<string, string>();
    }

    /// <summary>Tests only: a made-up table, to see what a broken translation does.</summary>
    internal static void UseTable(string language, IReadOnlyDictionary<string, string> table)
    {
        Language = language;
        _table = table;
    }

    /// <summary>The text in the user's language, or the English itself when there is no translation.</summary>
    public static string T(string english) =>
        _table.TryGetValue(english, out var text) && text.Length > 0 ? text : english;

    /// <summary>
    /// string.Format over the translated format. A translation with broken placeholders would throw in the middle
    /// of an apply or a scan; it shows the English instead and says so in the log. The tests check placeholders too.
    /// </summary>
    public static string F(string englishFormat, params object?[] args)
    {
        string format = T(englishFormat);
        try
        {
            return string.Format(CultureInfo.CurrentCulture, format, args);
        }
        catch (FormatException) when (!ReferenceEquals(format, englishFormat))
        {
            LogService.Log($"The translation of \"{englishFormat}\" has broken placeholders; showing English.");
            return string.Format(CultureInfo.CurrentCulture, englishFormat, args);
        }
    }

    /// <summary>
    /// Marks text as translatable without translating it: for values kept in English because they end up in ids
    /// or backups. Translate them with T where they are shown.
    /// </summary>
    public static string N(string english) => english;

    internal static Dictionary<string, string> LoadSpanish()
    {
        try
        {
            using var stream = typeof(Loc).Assembly.GetManifestResourceStream(SpanishResource);
            if (stream is null)
            {
                LogService.Log($"Spanish text is missing from the build ({SpanishResource}); showing English.");
                return new Dictionary<string, string>();
            }
            return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? new Dictionary<string, string>();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            LogService.Log($"Spanish text could not be read, showing English: {ex.Message}");
            return new Dictionary<string, string>();
        }
    }
}
