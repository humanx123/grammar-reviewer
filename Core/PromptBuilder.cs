namespace GrammarBubble.Core;

public static class PromptBuilder
{
    private const string ScientificRegister =
        "Target register: peer-reviewed scientific article in English. " +
        "Use formal, precise, concise prose. Prefer active voice when it is clear; use past tense for methods and results, " +
        "present tense for established facts. Hedge claims the text does not support (e.g. 'suggests', 'may', 'is associated with') " +
        "without strengthening or weakening what the authors state. Avoid colloquialisms, contractions, filler and vague intensifiers. " +
        "Keep terminology consistent. Use 'significant' or 'significantly' only when a statistical test is reported; otherwise use 'marked', 'substantial' or 'considerably'. Never alter " +
        "numbers, units, statistics, p-values, confidence intervals, equations, " +
        "gene, virus or species names, proper nouns, abbreviations' definitions, or citations; never invent references. " +
        "If something is ambiguous, keep the meaning and mention it in a reason instead of guessing. ";

    public static string System(CorrectionMode mode, DocLanguage lang) => mode switch
    {
        CorrectionMode.Correct =>
            "Fix grammar, spelling, punctuation and clarity. Keep the author's meaning, tone and language. " +
            "Make the smallest changes that make the text correct. " + LanguageLine(lang),
        CorrectionMode.Rewrite =>
            "Rewrite the text to read naturally and clearly while keeping the same meaning and language. " + LanguageLine(lang),
        CorrectionMode.Formal =>
            "Rewrite the text in a formal, professional register while keeping the same meaning and language. " + LanguageLine(lang),
        CorrectionMode.Casual =>
            "Rewrite the text in a relaxed, friendly register while keeping the same meaning and language. " + LanguageLine(lang),
        CorrectionMode.Concise =>
            "Shorten the text without losing meaning, keeping the same language. " + LanguageLine(lang),
        CorrectionMode.Scientific =>
            "Correct grammar, spelling and punctuation, and improve the scientific style. " + ScientificRegister +
            "Make only the edits needed for clarity, precision and academic register; do not restructure the argument. " +
            LanguageLine(lang),
        CorrectionMode.Translate => TranslateInstruction(lang),
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };

    public const string Contract =
        "Respond only with a JSON object of this exact shape: " +
        "{\"corrected\": string, \"changes\": [{\"original\": string, \"replacement\": string, \"reason\": string}]}. " +
        "\"corrected\" is the full resulting text. \"changes\" lists each edit with a short reason in the same language as the text. " +
        "If the text needs no change, return it unchanged with an empty changes array. Do not add commentary.";

    public static string Reviewer(CorrectionMode mode, DocLanguage lang) =>
        "You are a senior editor checking a proposed correction. You receive the ORIGINAL text and a PROPOSED result as JSON. " +
        "Verify every proposed change: keep only changes that are correct and preserve the author's meaning; " +
        "revert changes that alter facts, numbers, names, citations or meaning; fix any error the proposal introduced; " +
        "and add any grammar, spelling, punctuation or style issue the proposal missed. " +
        "Goal: " + GoalFor(mode, lang) + " " + Contract;

    public static string Consensus(CorrectionMode mode, DocLanguage lang) =>
        "You are the final editor. You receive the ORIGINAL text and several independent PROPOSALS from different models. " +
        "Build one best result: keep a change when at least two proposals make it or when it is clearly correct; " +
        "where proposals conflict, choose the option that best preserves the author's meaning, numbers, names and citations; " +
        "discard any change that alters facts or introduces errors; add any clear issue all proposals missed. " +
        "Goal: " + GoalFor(mode, lang) + " " + Contract;

    private static string GoalFor(CorrectionMode mode, DocLanguage lang) => mode == CorrectionMode.Scientific
        ? "scientific English that is grammatically correct and in a precise academic register. " + ScientificRegister
        : "the result must match this instruction: " + System(mode, lang);

    private static string LanguageLine(DocLanguage lang) => lang == DocLanguage.English
        ? "The source text is in English; keep the output in English."
        : "El texto de origen está en español; mantén la salida en español.";

    private static string TranslateInstruction(DocLanguage lang)
    {
        var (source, target) = lang == DocLanguage.English ? ("English", "Spanish") : ("Spanish", "English");
        return
            "Produce an interpretive translation: convey the meaning, intent and tone of the text, not a word-for-word rendering. " +
            "Rephrase idioms, jokes and cultural references with equivalents a native reader would naturally use. " +
            "Keep names, numbers, units, dates and technical terms exact. " +
            $"The source text is in {source}. Translate it into {target}. " +
            $"The result must read as if it had been written originally in {target}. " +
            "In \"corrected\" put only the translation. In \"changes\" list the notable renderings: original is the source phrase, " +
            $"replacement is the rendering, reason is a short note explaining the interpretation (in {target}).";
    }
}
