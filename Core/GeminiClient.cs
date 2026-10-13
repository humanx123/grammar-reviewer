using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GrammarBubble.Core;

public sealed class GeminiClient : ILlmProvider
{
    public const string DefaultModel = "gemini-3-flash-preview";
    private const int MaxAttempts = 3;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly string _endpoint;

    public GeminiClient(string apiKey, string model = DefaultModel, HttpMessageHandler? handler = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new GroqException("Falta la clave de Gemini.");

        _apiKey = apiKey.Trim();
        _model = model;
        _endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent";
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromSeconds(90);
    }

    public string Name => "Gemini/" + _model;

    public async Task<string> CompleteJsonAsync(string system, string user, CancellationToken ct = default)
    {
        var body = new GenerateRequest(
            new SystemInstruction(new[] { new Part(system) }),
            new[] { new Content("user", new[] { new Part(user) }) },
            new GenerationConfig(0.2, "application/json"));
        return await PostAsync(body, ct);
    }

    public async Task<string> ChatAsync(string system, IReadOnlyList<ChatTurn> turns, CancellationToken ct = default)
    {
        var contents = turns.Select(t => new Content(t.Role == "assistant" ? "model" : "user", new[] { new Part(t.Content) })).ToList();
        var body = new GenerateRequest(
            new SystemInstruction(new[] { new Part(system) }),
            contents,
            new GenerationConfig(0.5, "text/plain"));
        return await PostAsync(body, ct);
    }

    private async Task<string> PostAsync(GenerateRequest body, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(body);

        for (int attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("x-goog-api-key", _apiKey);

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, ct);
            }
            catch (HttpRequestException ex)
            {
                throw new GroqException("Sin conexión con Gemini. Revisa tu red.", ex);
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                if (attempt < MaxAttempts) continue;
                throw new GroqException("Gemini tardó demasiado en responder.", ex);
            }

            using (response)
            {
                if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < MaxAttempts)
                {
                    await Task.Delay(TimeSpan.FromSeconds(attempt * 2), ct);
                    continue;
                }

                var payload = await response.Content.ReadAsStringAsync(ct);

                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    throw new GroqException("La clave de Gemini no es válida o no tiene acceso.");
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    throw new GroqException("Límite gratuito de Gemini alcanzado. Espera un minuto.");
                if (!response.IsSuccessStatusCode)
                    throw new GroqException($"Gemini respondió {(int)response.StatusCode}.");

                var parsed = JsonSerializer.Deserialize<GenerateResponse>(payload, JsonOptions);
                var content = parsed?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;
                if (string.IsNullOrWhiteSpace(content))
                    throw new GroqException("Respuesta vacía de Gemini.");
                return content;
            }
        }
    }

    public void Dispose() => _http.Dispose();

    private sealed record GenerateRequest(
        [property: JsonPropertyName("systemInstruction")] SystemInstruction SystemInstruction,
        [property: JsonPropertyName("contents")] IReadOnlyList<Content> Contents,
        [property: JsonPropertyName("generationConfig")] GenerationConfig GenerationConfig);

    private sealed record SystemInstruction([property: JsonPropertyName("parts")] IReadOnlyList<Part> Parts);

    private sealed record Content(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("parts")] IReadOnlyList<Part> Parts);

    private sealed record Part([property: JsonPropertyName("text")] string Text);

    private sealed record GenerationConfig(
        [property: JsonPropertyName("temperature")] double Temperature,
        [property: JsonPropertyName("responseMimeType")] string ResponseMimeType);

    private sealed record GenerateResponse([property: JsonPropertyName("candidates")] List<Candidate>? Candidates);

    private sealed record Candidate([property: JsonPropertyName("content")] ResponseContent? Content);

    private sealed record ResponseContent([property: JsonPropertyName("parts")] List<ResponsePart>? Parts);

    private sealed record ResponsePart([property: JsonPropertyName("text")] string? Text);
}
