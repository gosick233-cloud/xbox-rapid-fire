using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Windows;

namespace XboxRapidFire;

internal static class Loc
{
    private sealed record Text(string Zh, string En);
    private static readonly Dictionary<string, Text> strings = JsonSerializer.Deserialize<Dictionary<string, Text>>(
        Assembly.GetExecutingAssembly().GetManifestResourceStream("XboxRapidFire.Strings")!,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    private static readonly Dictionary<string, string> english = strings.Values.ToDictionary(t => t.Zh, t => t.En);
    public static ResourceDictionary Resources { get; } = new();
    public static string Language { get; private set; } = "zh";

    public static void SetLanguage(string language)
    {
        Language = language == "en" ? "en" : "zh";
        foreach (var (key, value) in strings) Resources[key] = Language == "en" ? value.En : value.Zh;
    }
    public static string T(string text)
    {
        if (Language != "en") return text;
        if (english.TryGetValue(text, out var translated)) return translated;
        // Native error details may follow an application-owned message prefix.
        foreach (var (prefix, value) in english)
            if (prefix.Length > 5 && text.StartsWith(prefix, StringComparison.Ordinal))
                return value + T(text[prefix.Length..]).Replace("。", ".");
        return text;
    }
    public static string F(string text, params object[] args) => string.Format(CultureInfo.CurrentCulture, T(text), args);
}
