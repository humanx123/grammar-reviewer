namespace GrammarBubble.Core;

public static class ProviderFactory
{
    public const string Llm7BaseUrl = "https://api.llm7.io/v1";
    public const string Llm7Model = "gpt-oss:20b";
    public const string NvidiaBaseUrl = "https://integrate.api.nvidia.com/v1";
    public const string NvidiaModel = "meta/llama-3.3-70b-instruct";
    public const string KiloBaseUrl = "https://api.kilo.ai/api/gateway";
    public const string KiloModel = "kilo-auto/free";

    public static ChatService CreateChatService(ApiKeys keys)
    {
        var providers = new List<ILlmProvider>();
        if (!string.IsNullOrWhiteSpace(keys.Groq))
            providers.Add(new GroqClient(keys.Groq));
        if (!string.IsNullOrWhiteSpace(keys.Gemini))
            providers.Add(new GeminiClient(keys.Gemini));
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
            drafters.Add(new GeminiClient(keys.Gemini));
        if (!string.IsNullOrWhiteSpace(keys.Groq))
        {
            drafters.Add(new GroqClient(keys.Groq));
            drafters.Add(new GroqClient(keys.Groq, "qwen/qwen3.8-27b"));
        }

        var reviewers = new List<ILlmProvider>();
        if (!string.IsNullOrWhiteSpace(keys.Groq))
            reviewers.Add(new GroqClient(keys.Groq));
        if (!string.IsNullOrWhiteSpace(keys.Gemini))
            reviewers.Add(new GeminiClient(keys.Gemini));

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
