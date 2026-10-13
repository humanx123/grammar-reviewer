using System.Text.Json;

namespace GrammarBubble.Core;

internal static class CorrectionParser
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static CorrectionResult Parse(string content)
    {
        try
        {
            var result = JsonSerializer.Deserialize<CorrectionResult>(content, Options);
            if (result is null || result.Corrected is null)
                throw new GroqException("El modelo devolvió un formato inesperado.");
            return result with { Changes = result.Changes ?? Array.Empty<Change>() };
        }
        catch (JsonException ex)
        {
            throw new GroqException("El modelo devolvió JSON inválido.", ex);
        }
    }
}
