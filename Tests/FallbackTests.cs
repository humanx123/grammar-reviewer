using GrammarBubble.Core;
using Xunit;

namespace GrammarBubble.Tests;

public class FallbackTests
{
    private sealed class FakeProvider(string name, string? answer, string? error = null) : ILlmProvider
    {
        public string Name => name;
        public Task<string> CompleteJsonAsync(string system, string user, CancellationToken ct = default) =>
            error is null ? Task.FromResult(answer!) : throw new GroqException(error);
        public Task<string> ChatAsync(string system, IReadOnlyList<ChatTurn> turns, CancellationToken ct = default) =>
            error is null ? Task.FromResult(answer!) : throw new GroqException(error);
        public void Dispose() { }
    }

    [Fact]
    public async Task UsesNextModelWhenFirstFails()
    {
        var provider = new ModelFallbackProvider("Test", new List<Func<ILlmProvider>>
        {
            () => new FakeProvider("m1", null, "modelo no disponible"),
            () => new FakeProvider("m2", "{\"corrected\":\"ok\"}"),
        });

        var raw = await provider.CompleteJsonAsync("sys", "user");
        Assert.Equal("{\"corrected\":\"ok\"}", raw);
    }

    [Fact]
    public async Task ThrowsWhenEveryModelFails()
    {
        var provider = new ModelFallbackProvider("Test", new List<Func<ILlmProvider>>
        {
            () => new FakeProvider("m1", null, "uno"),
            () => new FakeProvider("m2", null, "dos"),
        });

        var ex = await Assert.ThrowsAsync<GroqException>(() => provider.CompleteJsonAsync("sys", "user"));
        Assert.Equal("dos", ex.Message);
    }
}
