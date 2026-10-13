namespace GrammarBubble.Core;

public sealed record ChatTurn(string Role, string Content);

public interface ILlmProvider : IDisposable
{
    string Name { get; }

    Task<string> CompleteJsonAsync(string system, string user, CancellationToken ct = default);

    Task<string> ChatAsync(string system, IReadOnlyList<ChatTurn> turns, CancellationToken ct = default);
}
