using System.Net;
using System.Net.Http.Headers;

namespace GrammarBubble.Core;

public enum KeyState { Ok, Invalid, Limit, Offline, Missing }

public sealed record ProviderHealth(string Name, KeyState State, string Detail);

public static class KeyChecker
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };

    public static async Task<IReadOnlyList<ProviderHealth>> CheckAllAsync(ApiKeys keys, CancellationToken ct = default)
    {
        var results = await Task.WhenAll(
            CheckGroqAsync(keys.Groq, ct),
            CheckGeminiAsync(keys.Gemini, ct),
            CheckNvidiaAsync(keys.Nvidia, ct),
            CheckKeylessAsync("Kilo", "https://api.kilo.ai/api/gateway/models", keys.Kilo, ct),
            CheckKeylessAsync("LLM7", "https://api.llm7.io/v1/models", null, ct));
        return results;
    }

    public static string Overall(IReadOnlyList<ProviderHealth> results)
    {
        if (results.Any(r => r.State == KeyState.Ok && r.Name is "Groq" or "Gemini" or "NVIDIA")) return "green";
        if (results.Any(r => r.State == KeyState.Ok)) return "yellow";
        return "red";
    }

    public static string Mask(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return "";
        var k = key.Trim();
        return k.Length <= 8 ? "••••" : $"{k[..4]}…{k[^4..]}";
    }

    private static Task<ProviderHealth> CheckGroqAsync(string? key, CancellationToken ct) =>
        CheckBearerAsync("Groq", "https://api.groq.com/openai/v1/models", key, ct);

    private static Task<ProviderHealth> CheckNvidiaAsync(string? key, CancellationToken ct) =>
        CheckBearerAsync("NVIDIA", "https://integrate.api.nvidia.com/v1/models", key, ct);

    private static async Task<ProviderHealth> CheckBearerAsync(string name, string url, string? key, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(key)) return new(name, KeyState.Missing, "Sin clave");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key.Trim());
        return await SendAsync(name, request, ct);
    }

    private static async Task<ProviderHealth> CheckGeminiAsync(string? key, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(key)) return new("Gemini", KeyState.Missing, "Sin clave");
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://generativelanguage.googleapis.com/v1beta/models");
        request.Headers.Add("x-goog-api-key", key.Trim());
        return await SendAsync("Gemini", request, ct);
    }

    private static async Task<ProviderHealth> CheckKeylessAsync(string name, string url, string? key, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrWhiteSpace(key))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key.Trim());
        var result = await SendAsync(name, request, ct);
        return string.IsNullOrWhiteSpace(key) && result.State == KeyState.Invalid
            ? new(name, KeyState.Missing, "Requiere clave")
            : result;
    }

    private static async Task<ProviderHealth> SendAsync(string name, HttpRequestMessage request, CancellationToken ct)
    {
        try
        {
            using var response = await Http.SendAsync(request, ct);
            return response.StatusCode switch
            {
                HttpStatusCode.OK => new(name, KeyState.Ok, "Funcionando"),
                HttpStatusCode.TooManyRequests => new(name, KeyState.Limit, "Límite alcanzado, reintenta más tarde"),
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.BadRequest => new(name, KeyState.Invalid, "Clave no válida"),
                _ => new(name, KeyState.Offline, $"Respondió {(int)response.StatusCode}")
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new(name, KeyState.Offline, "Sin conexión");
        }
    }
}
