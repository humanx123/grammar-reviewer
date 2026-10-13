namespace GrammarBubble.Core;

public sealed record ChatReply(string Text, string Provider);

public sealed class ChatService(IReadOnlyList<ILlmProvider> providers) : IDisposable
{
    public const string SystemPrompt =
        "You are Grammar Reviewer, a bilingual (Spanish and English) writing assistant. " +
        "You help with grammar, spelling, punctuation, style, clarity, interpretive translation and explaining language. " +
        "Answer in the same language the user writes in, unless they ask for another. " +
        "When you correct a text, show the corrected version first, then a short list of the main changes with brief reasons. " +
        "When you translate, prefer natural, interpretive renderings over literal ones and note any idiom you adapted. " +
        "Keep answers concise and practical. If you are unsure about a fact, say so.";

    public async Task<ChatReply> AskAsync(IReadOnlyList<ChatTurn> turns, CancellationToken ct = default)
    {
        foreach (var provider in providers)
        {
            try
            {
                var text = await provider.ChatAsync(SystemPrompt, turns, ct);
                return new ChatReply(text.Trim(), provider.Name);
            }
            catch (GroqException)
            {
            }
        }
        throw new GroqException("Ningún modelo respondió en este momento. Inténtalo de nuevo en un minuto.");
    }

    public void Dispose()
    {
        foreach (var p in providers) p.Dispose();
    }
}
