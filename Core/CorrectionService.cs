using System.Text;
using System.Text.Json;

namespace GrammarBubble.Core;

public sealed class CorrectionService(
    IReadOnlyList<ILlmProvider> drafters,
    IReadOnlyList<ILlmProvider> reviewers,
    IReadOnlyList<ILlmProvider> fallbacks) : IDisposable
{
    private const int MaxChunkChars = 6000;

    public string Pipeline
    {
        get
        {
            var names = string.Join(" + ", drafters.Select(d => d.Name));
            var review = reviewers.Count > 0 ? $" -> {reviewers[0].Name}" : "";
            return drafters.Count == 0 ? "respaldo: " + string.Join(" + ", fallbacks.Select(f => f.Name)) : $"[{names}]{review}";
        }
    }

    public async Task<CorrectionResult> CorrectAsync(
        string text,
        CorrectionMode mode,
        DocLanguage lang,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new GroqException("No hay texto para revisar.");

        var corrected = new List<string>();
        var changes = new List<Change>();

        foreach (var chunk in Chunk(text))
        {
            var drafts = (await Task.WhenAll(drafters.Select(d => DraftAsync(d, chunk, mode, lang, ct))))
                .Where(d => d is not null).Select(d => d!).ToList();

            if (drafts.Count == 0)
            {
                foreach (var fallback in fallbacks)
                {
                    var backup = await DraftAsync(fallback, chunk, mode, lang, ct);
                    if (backup is not null)
                    {
                        drafts.Add(backup);
                        break;
                    }
                }
            }

            if (drafts.Count == 0)
                throw new GroqException("Ningún modelo disponible respondió. Revisa tus claves o inténtalo en un minuto.");

            var final = drafts.Count == 1 ? drafts[0] : await ReviewAsync(chunk, drafts, mode, lang, ct);

            corrected.Add(final.Corrected);
            changes.AddRange(final.Changes);
        }

        return new CorrectionResult(string.Concat(corrected), changes);
    }

    private static async Task<CorrectionResult?> DraftAsync(ILlmProvider provider, string chunk, CorrectionMode mode, DocLanguage lang, CancellationToken ct)
    {
        try
        {
            var raw = await provider.CompleteJsonAsync(PromptBuilder.System(mode, lang) + " " + PromptBuilder.Contract, chunk, ct);
            return CorrectionParser.Parse(raw);
        }
        catch (GroqException)
        {
            return null;
        }
    }

    private async Task<CorrectionResult> ReviewAsync(string chunk, IReadOnlyList<CorrectionResult> drafts, CorrectionMode mode, DocLanguage lang, CancellationToken ct)
    {
        var proposals = new StringBuilder();
        for (int i = 0; i < drafts.Count; i++)
            proposals.Append($"PROPOSAL {i + 1}:\n{JsonSerializer.Serialize(drafts[i])}\n\n");
        var user = $"ORIGINAL:\n{chunk}\n\n{proposals}";

        foreach (var reviewer in reviewers)
        {
            try
            {
                var raw = await reviewer.CompleteJsonAsync(PromptBuilder.Consensus(mode, lang), user, ct);
                return CorrectionParser.Parse(raw);
            }
            catch (GroqException)
            {
            }
        }
        return drafts[0];
    }

    private static List<string> Chunk(string text)
    {
        if (text.Length <= MaxChunkChars)
            return [text];

        var chunks = new List<string>();
        var current = new StringBuilder();
        foreach (var paragraph in text.Split('\n').Select(p => p + "\n"))
        {
            if (current.Length + paragraph.Length > MaxChunkChars && current.Length > 0)
            {
                chunks.Add(current.ToString());
                current.Clear();
            }
            current.Append(paragraph);
        }
        if (current.Length > 0)
            chunks.Add(current.ToString());
        return chunks;
    }

    public void Dispose()
    {
        foreach (var p in drafters.Concat(reviewers).Concat(fallbacks)) p.Dispose();
    }
}
