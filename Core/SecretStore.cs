using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GrammarBubble.Core;

public sealed record ApiKeys(string? Groq, string? Gemini, string? Nvidia = null, string? Kilo = null);

public static class SecretStore
{
    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GrammarBubble");

    private static readonly string FilePath = Path.Combine(Folder, "keys.bin");

    public static ApiKeys Load()
    {
        var stored = LoadStored();
        return new ApiKeys(
            stored.Groq ?? Environment.GetEnvironmentVariable("GROQ_API_KEY"),
            stored.Gemini ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY"),
            stored.Nvidia ?? Environment.GetEnvironmentVariable("NVIDIA_API_KEY"),
            stored.Kilo ?? Environment.GetEnvironmentVariable("KILO_API_KEY"));
    }

    private static ApiKeys LoadStored()
    {
        try
        {
            if (!File.Exists(FilePath)) return new ApiKeys(null, null);
            var json = OperatingSystem.IsWindows() ? UnprotectWindows(File.ReadAllBytes(FilePath)) : File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<ApiKeys>(json) ?? new ApiKeys(null, null);
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or IOException)
        {
            return new ApiKeys(null, null);
        }
    }

    public static void Save(ApiKeys keys)
    {
        Directory.CreateDirectory(Folder);
        var json = JsonSerializer.Serialize(keys);
        if (OperatingSystem.IsWindows())
        {
            File.WriteAllBytes(FilePath, ProtectWindows(json));
            return;
        }

        File.WriteAllText(FilePath, json);
        File.SetUnixFileMode(FilePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    [SupportedOSPlatform("windows")]
    private static byte[] ProtectWindows(string json) =>
        ProtectedData.Protect(Encoding.UTF8.GetBytes(json), null, DataProtectionScope.CurrentUser);

    [SupportedOSPlatform("windows")]
    private static string UnprotectWindows(byte[] bytes) =>
        Encoding.UTF8.GetString(ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser));
}
