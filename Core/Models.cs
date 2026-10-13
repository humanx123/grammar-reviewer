using System.Text.Json.Serialization;

namespace GrammarBubble.Core;

public enum CorrectionMode
{
    Correct,
    Rewrite,
    Formal,
    Casual,
    Concise,
    Scientific,
    Translate
}

public sealed record Change(
    [property: JsonPropertyName("original")] string Original,
    [property: JsonPropertyName("replacement")] string Replacement,
    [property: JsonPropertyName("reason")] string Reason);

public sealed record CorrectionResult(
    [property: JsonPropertyName("corrected")] string Corrected,
    [property: JsonPropertyName("changes")] IReadOnlyList<Change> Changes);

public enum DiffKind
{
    Equal,
    Delete,
    Insert
}

public sealed record DiffSegment(DiffKind Kind, string Text);
