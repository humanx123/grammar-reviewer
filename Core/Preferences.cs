using System.Text.Json;

namespace GrammarBubble.Core;

public enum DocLanguage
{
    Spanish,
    English
}

public static class Preferences
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GrammarBubble", "prefs.json");

    private sealed record Stored(string? Language);

    public static DocLanguage LoadLanguage()
    {
        try
        {
            if (!File.Exists(FilePath)) return DocLanguage.Spanish;
            var stored = JsonSerializer.Deserialize<Stored>(File.ReadAllText(FilePath));
            return stored?.Language == "en" ? DocLanguage.English : DocLanguage.Spanish;
        }
        catch (JsonException)
        {
            return DocLanguage.Spanish;
        }
    }

    public static void SaveLanguage(DocLanguage lang)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(new Stored(lang == DocLanguage.English ? "en" : "es")));
    }
}
