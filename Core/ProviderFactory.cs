namespace GrammarBubble.Core;

public static class ProviderFactory
{
    public const string Llm7BaseUrl = "https://api.llm7.io/v1";
    public const string Llm7Model = "gpt-oss:20b";
    public const string NvidiaBaseUrl = "https://integrate.api.nvidia.com/v1";
    public const string NvidiaModel = "meta/llama-3.3-70b-instruct";
    public const string KiloBaseUrl = "https://api.kilo.ai/api/gateway";
    public const string KiloModel = "kilo-auto/free";

    /// Modelos en orden de preferencia. Si uno deja de existir o se agota, se usa el siguiente.
    public static readonly string[] GroqModels = ["openai/gpt-oss-120b", "qwen/qwen3.8-27b", "llama-3.3-70b-versatile"];
    public static readonly string[] GeminiModels = ["gemini-3-flash-preview", "gemini-2.5-flash-lite", "gemini-flash-latest"];

    private static ILlmProvider GroqWithFallback(string key, string first) =>
        new ModelFallbackProvider("Groq", GroqModels.Prepend(first).Distinct()
            .Select(m => (Func<ILlmProvider>)(() => new GroqClient(key, m))).ToList());

    private static ILlmProvider GeminiWithFallback(string key) =>
        new ModelFallbackProvider("Gemini", GeminiModels
            .Select(m => (Func<ILlmProvider>)(() => new GeminiClient(key, m))).ToList());

    public static ChatService CreateChatService(ApiKeys keys)
    {
        var providers = new List<ILlmProvider>();
        if (!string.IsNullOrWhiteSpace(keys.Groq))
            providers.Add(GroqWithFallback(keys.Groq, GroqModels[0]));
        if (!string.IsNullOrWhiteSpace(keys.Gemini))
            providers.Add(GeminiWithFallback(keys.Gemini));
        if (!string.IsNullOrWhiteSpace(keys.Nvidia))
            providers.Add(new OpenAiCompatibleClient("NVIDIA", NvidiaBaseUrl, NvidiaModel, keys.Nvidia));
        providers.Add(new OpenAiCompatibleClient("Kilo", KiloBaseUrl, KiloModel, keys.Kilo));
        providers.Add(new OpenAiCompatibleClient("LLM7", Llm7BaseUrl, Llm7Model));
        return new ChatService(providers);
    }

    public static CorrectionService? CreateService(ApiKeys keys)
    {
        var drafters = new List<ILlmProvider>();
        if (!string.IsNullOrWhiteSpace(keys.Gemini))
            drafters.Add(GeminiWithFallback(keys.Gemini));
        if (!string.IsNullOrWhiteSpace(keys.Groq))
        {
            drafters.Add(GroqWithFallback(keys.Groq, GroqModels[0]));
            drafters.Add(GroqWithFallback(keys.Groq, GroqModels[1]));
        }

        var reviewers = new List<ILlmProvider>();
        if (!string.IsNullOrWhiteSpace(keys.Groq))
            reviewers.Add(GroqWithFallback(keys.Groq, GroqModels[0]));
        if (!string.IsNullOrWhiteSpace(keys.Gemini))
            reviewers.Add(GeminiWithFallback(keys.Gemini));

        var fallbacks = new List<ILlmProvider>
        {
            new OpenAiCompatibleClient("Kilo", KiloBaseUrl, KiloModel, keys.Kilo),
            new OpenAiCompatibleClient("LLM7", Llm7BaseUrl, Llm7Model)
        };
        if (!string.IsNullOrWhiteSpace(keys.Nvidia))
            fallbacks.Insert(0, new OpenAiCompatibleClient("NVIDIA", NvidiaBaseUrl, NvidiaModel, keys.Nvidia));

        return new CorrectionService(drafters, reviewers, fallbacks);
    }
}
