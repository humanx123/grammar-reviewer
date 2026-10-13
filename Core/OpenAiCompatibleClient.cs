using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GrammarBubble.Core;

public sealed class OpenAiCompatibleClient : ILlmProvider
{
    private const int MaxAttempts = 2;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;
    private readonly string _endpoint;
    private readonly string _model;
    private readonly string? _apiKey;
    private readonly string _label;

    public OpenAiCompatibleClient(string label, string baseUrl, string model, string? apiKey = null, HttpMessageHandler? handler = null)
    {
        _label = label;
        _model = model;
        _apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
        _endpoint = baseUrl.TrimEnd('/') + "/chat/completions";
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromSeconds(60);
    }

    public string Name => $"{_label}/{_model}";

    public async Task<string> CompleteJsonAsync(string system, string user, CancellationToken ct = default)
    {
        try
        {
            return await SendAsync(system, user, useJsonMode: true, ct);
        }
        catch (GroqException ex) when (ex.Message.StartsWith("BadRequest"))
        {
            return await SendAsync(system, user, useJsonMode: false, ct);
        }
    }

    public async Task<string> ChatAsync(string system, IReadOnlyList<ChatTurn> turns, CancellationToken ct = default)
    {
        var messages = new List<ChatMessage> { new("system", system) };
        messages.AddRange(turns.Select(t => new ChatMessage(t.Role, t.Content)));
        return await PostAsync(new ChatRequest(_model, messages, 0.5, null), ct);
    }

    private Task<string> SendAsync(string system, string user, bool useJsonMode, CancellationToken ct) =>
        PostAsync(new ChatRequest(
            _model,
            new[] { new ChatMessage("system", system), new ChatMessage("user", user) },
            0.2,
            useJsonMode ? new ResponseFormat("json_object") : null), ct);

    private async Task<string> PostAsync(ChatRequest body, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(body, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });

        for (int attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            if (_apiKey is not null)
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, ct);
            }
            catch (HttpRequestException ex)
            {
                throw new GroqException($"{_label}: sin conexión.", ex);
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                throw new GroqException($"{_label}: tiempo de espera agotado.", ex);
            }

            using (response)
            {
                if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < MaxAttempts)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), ct);
                    continue;
                }

                var payload = await response.Content.ReadAsStringAsync(ct);

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    throw new GroqException($"{_label}: límite gratuito alcanzado.");
                if (response.StatusCode == HttpStatusCode.BadRequest)
                    throw new GroqException("BadRequest: " + _label);
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    throw new GroqException($"{_label}: clave no válida o sin acceso.");
                if (!response.IsSuccessStatusCode)
                    throw new GroqException($"{_label} respondió {(int)response.StatusCode}.");

                var chat = JsonSerializer.Deserialize<ChatResponse>(payload, JsonOptions);
                var content = chat?.Choices?.FirstOrDefault()?.Message?.Content;
                if (string.IsNullOrWhiteSpace(content))
                    throw new GroqException($"{_label}: respuesta vacía.");
                return content;
            }
        }
    }

    public void Dispose() => _http.Dispose();

    private sealed record ChatRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyList<ChatMessage> Messages,
        [property: JsonPropertyName("temperature")] double Temperature,
        [property: JsonPropertyName("response_format")] ResponseFormat? ResponseFormat);

    private sealed record ChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record ResponseFormat([property: JsonPropertyName("type")] string Type);

    private sealed record ChatResponse([property: JsonPropertyName("choices")] List<Choice>? Choices);

    private sealed record Choice([property: JsonPropertyName("message")] ChatMessage? Message);
}
