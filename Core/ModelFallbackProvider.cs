namespace GrammarBubble.Core;

/// Prueba varios modelos del mismo proveedor en orden. Si uno desaparece o alcanza su límite, usa el siguiente.
public sealed class ModelFallbackProvider(string name, IReadOnlyList<Func<ILlmProvider>> candidates) : ILlmProvider
{
    private readonly List<ILlmProvider> _created = new();

    public string Name => name;

    public async Task<string> CompleteJsonAsync(string system, string user, CancellationToken ct = default)
    {
        GroqException? last = null;
        foreach (var make in candidates)
        {
            var provider = Track(make);
            try
            {
                return await provider.CompleteJsonAsync(system, user, ct);
            }
            catch (GroqException ex)
            {
                last = ex;
            }
        }
        throw last ?? new GroqException($"{name}: sin modelos disponibles.");
    }

    public async Task<string> ChatAsync(string system, IReadOnlyList<ChatTurn> turns, CancellationToken ct = default)
    {
        GroqException? last = null;
        foreach (var make in candidates)
        {
            var provider = Track(make);
            try
            {
                return await provider.ChatAsync(system, turns, ct);
            }
            catch (GroqException ex)
            {
                last = ex;
            }
        }
        throw last ?? new GroqException($"{name}: sin modelos disponibles.");
    }

    private ILlmProvider Track(Func<ILlmProvider> make)
    {
        var provider = make();
        _created.Add(provider);
        return provider;
    }

    public void Dispose()
    {
        foreach (var p in _created) p.Dispose();
    }
}
