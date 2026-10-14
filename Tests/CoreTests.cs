using GrammarBubble.Core;
using Xunit;

namespace GrammarBubble.Tests;

public class DiffEngineTests
{
    [Fact]
    public void IdenticalTextsProduceOnlyEqualSegments()
    {
        var segments = DiffEngine.Diff("hello world", "hello world");
        Assert.All(segments, s => Assert.Equal(DiffKind.Equal, s.Kind));
        Assert.Equal("hello world", string.Concat(segments.Select(s => s.Text)));
    }

    [Fact]
    public void ReplacementProducesDeleteAndInsert()
    {
        var segments = DiffEngine.Diff("he goes home", "he went home");
        Assert.Contains(segments, s => s.Kind == DiffKind.Delete && s.Text.Contains("goes"));
        Assert.Contains(segments, s => s.Kind == DiffKind.Insert && s.Text.Contains("went"));
    }
}

public class ChangeApplierTests
{
    private static readonly Change Subject = new("Me and him", "He and I", "pronoun");
    private static readonly Change Verb = new("goes", "went", "tense");

    [Fact]
    public void AppliesAllSelectedChanges()
    {
        var (text, _) = ChangeApplier.Apply("Me and him goes home.", [Subject, Verb], [true, true]);
        Assert.Equal("He and I went home.", text);
    }

    [Fact]
    public void KeepsOriginalWhenChangeIsRejected()
    {
        var (text, _) = ChangeApplier.Apply("Me and him goes home.", [Subject, Verb], [false, true]);
        Assert.Equal("Me and him went home.", text);
    }

    [Fact]
    public void SkipsChangesWhoseOriginalIsNotFound()
    {
        var missing = new Change("nowhere", "x", "none");
        var (text, applied) = ChangeApplier.Apply("Some text.", [missing], [true]);
        Assert.Equal("Some text.", text);
        Assert.False(applied[0].Applicable);
    }

    [Fact]
    public void ReportsTheSentenceContainingEachChange()
    {
        var (_, applied) = ChangeApplier.Apply("First one. Me and him goes home.", [Subject], [true]);
        Assert.Equal("Me and him goes home.", applied[0].Sentence);
    }
}

public class CorrectionParserTests
{
    [Fact]
    public void ParsesCorrectedTextAndChanges()
    {
        var json = """{"corrected":"He went.","changes":[{"original":"He goes.","replacement":"He went.","reason":"tense"}]}""";
        var result = CorrectionParser.Parse(json);
        Assert.Equal("He went.", result.Corrected);
        Assert.Single(result.Changes);
    }

    [Fact]
    public void MissingChangesBecomesEmptyList()
    {
        var result = CorrectionParser.Parse("""{"corrected":"Fine."}""");
        Assert.Empty(result.Changes);
    }

    [Fact]
    public void InvalidJsonThrowsGroqException()
    {
        Assert.Throws<GroqException>(() => CorrectionParser.Parse("not json at all"));
    }
}

public class PromptBuilderTests
{
    [Fact]
    public void SpanishSourceTranslatesToEnglish()
    {
        var prompt = PromptBuilder.System(CorrectionMode.Translate, DocLanguage.Spanish);
        Assert.Contains("source text is in Spanish", prompt);
        Assert.Contains("Translate it into English", prompt);
    }

    [Fact]
    public void EnglishSourceTranslatesToSpanish()
    {
        var prompt = PromptBuilder.System(CorrectionMode.Translate, DocLanguage.English);
        Assert.Contains("source text is in English", prompt);
        Assert.Contains("Translate it into Spanish", prompt);
    }

    [Fact]
    public void ScientificModeForbidsUnsupportedSignificance()
    {
        var prompt = PromptBuilder.System(CorrectionMode.Scientific, DocLanguage.English);
        Assert.Contains("statistical test", prompt);
    }
}

public class UpdateCheckerTests
{
    [Theory]
    [InlineData("1.2.1", "1.2.0", true)]
    [InlineData("1.10.0", "1.9.0", true)]
    [InlineData("1.2.0", "1.2.0", false)]
    [InlineData("1.1.9", "1.2.0", false)]
    public void ComparesVersionsNumerically(string candidate, string current, bool expected)
    {
        Assert.Equal(expected, UpdateChecker.IsNewer(candidate, current));
    }
}

public class KeyCheckerTests
{
    [Fact]
    public void MaskKeepsOnlyEdgesOfTheKey()
    {
        Assert.Equal("gsk_…ZGyf", KeyChecker.Mask("gsk_abcdefghijZGyf"));
        Assert.Equal("", KeyChecker.Mask(null));
    }

    [Fact]
    public void OverallIsGreenWhenAPrimaryProviderWorks()
    {
        var results = new[]
        {
            new ProviderHealth("Groq", KeyState.Ok, "Funcionando"),
            new ProviderHealth("Kilo", KeyState.Missing, "Sin clave"),
        };
        Assert.Equal("green", KeyChecker.Overall(results));
    }

    [Fact]
    public void OverallIsRedWhenNothingWorks()
    {
        var results = new[] { new ProviderHealth("Groq", KeyState.Offline, "Sin conexión") };
        Assert.Equal("red", KeyChecker.Overall(results));
    }
}
