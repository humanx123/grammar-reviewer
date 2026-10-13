using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GrammarBubble.Core;

public sealed class GroqException : Exception
{
    public GroqException(string message, Exception? inner = null) : base(message, inner) { }
}

public sealed class GroqClient : ILlmProvider
{
    public const string DefaultModel = "openai/gpt-oss-120b";
    private const string Endpoint = "https://api.groq.com/openai/v1/chat/completions";
    private const int MaxAttempts = 3;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _model;

    public GroqClient(string apiKey, string model = DefaultModel, HttpMessageHandler? handler = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new GroqException("Falta la clave de Groq.");

        _apiKey = apiKey.Trim();
        _model = model;
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromSeconds(45);
    }

    public string Name => "Groq/" + _model;

    public async Task<string> CompleteJsonAsync(string system, string user, CancellationToken ct = default)
    {
        var body = new ChatRequest(
            _model,
            new[] { new ChatMessage("system", system), new ChatMessage("user", user) },
            0.2,
            new ResponseFormat("json_object"));
        return await PostAsync(body, ct);
    }

    public async Task<string> ChatAsync(string system, IReadOnlyList<ChatTurn> turns, CancellationToken ct = default)
    {
        var messages = new List<ChatMessage> { new("system", system) };
        messages.AddRange(turns.Select(t => new ChatMessage(t.Role, t.Content)));
        return await PostAsync(new ChatRequest(_model, messages, 0.5, null), ct);
    }

    private async Task<string> PostAsync(ChatRequest body, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(body);

        for (int attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, ct);
            }
            catch (HttpRequestException ex)
            {
                throw new GroqException("Sin conexión con Groq. Revisa tu red.", ex);
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                throw new GroqException("Groq tardó demasiado en responder.", ex);
            }

            using (response)
            {
                if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < MaxAttempts)
                {
                    await Task.Delay(RetryDelay(response, attempt), ct);
                    continue;
                }

                var payload = await response.Content.ReadAsStringAsync(ct);

                if (response.StatusCode == HttpStatusCode.Unauthorized)
                    throw new GroqException("La clave de Groq no es válida.");
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    throw new GroqException("Límite gratuito de Groq alcanzado. Espera un minuto.");
                if (!response.IsSuccessStatusCode)
                    throw new GroqException($"Groq respondió {(int)response.StatusCode}.");

                var chat = JsonSerializer.Deserialize<ChatResponse>(payload, JsonOptions);
                var content = chat?.Choices?.FirstOrDefault()?.Message?.Content;
                if (string.IsNullOrWhiteSpace(content))
                    throw new GroqException("Respuesta vacía de Groq.");
                return content;
            }
        }
    }

    private static TimeSpan RetryDelay(HttpResponseMessage response, int attempt)
    {
        if (response.Headers.RetryAfter?.Delta is TimeSpan delta && delta <= TimeSpan.FromSeconds(10))
            return delta;
        return TimeSpan.FromSeconds(attempt * 2);
    }

    public void Dispose() => _http.Dispose();

    private sealed record ChatRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyList<ChatMessage> Messages,
        [property: JsonPropertyName("temperature")] double Temperature,
        [property: JsonPropertyName("response_format")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ResponseFormat? ResponseFormat);

    private sealed record ChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record ResponseFormat([property: JsonPropertyName("type")] string Type);

    private sealed record ChatResponse([property: JsonPropertyName("choices")] List<Choice>? Choices);

    private sealed record Choice([property: JsonPropertyName("message")] ChatMessage? Message);
}
