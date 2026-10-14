using System.Net.Http;
using System.Text.Json;

namespace GrammarBubble.Core;

public sealed record UpdateInfo(string Version, string Url, string Notes);

/** Versión de este build del motor, escritorio y CLI. */
public static class AppInfo
{
    public const string Version = "1.3.0";
}

public static class UpdateChecker
{
    private const string ManifestUrl = "https://humanx123.github.io/grammar-reviewer/version.json";

    /// <param name="platform">"desktop" o "cli".</param>
    public static async Task<UpdateInfo?> CheckAsync(string platform, CancellationToken ct = default)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            var body = await http.GetStringAsync(ManifestUrl, ct);
            using var doc = JsonDocument.Parse(body);
            var node = doc.RootElement.GetProperty(platform);
            var latest = node.GetProperty("version").GetString() ?? "";
            var url = node.GetProperty("url").GetString() ?? "";
            var notes = doc.RootElement.TryGetProperty("notes", out var n) ? n.GetString() ?? "" : "";
            return IsNewer(latest, AppInfo.Version) ? new UpdateInfo(latest, url, notes) : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    public static bool IsNewer(string candidate, string current)
    {
        var a = Parse(candidate);
        var b = Parse(current);
        for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            int x = i < a.Length ? a[i] : 0;
            int y = i < b.Length ? b[i] : 0;
            if (x != y) return x > y;
        }
        return false;
    }

    private static int[] Parse(string v) =>
        v.Split('.').Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();
}
