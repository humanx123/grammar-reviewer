using GrammarBubble.Core;

if (args.Length > 1 && args[0] == "--chat")
{
    using var chat = ProviderFactory.CreateChatService(SecretStore.Load());
    try
    {
        var reply = await chat.AskAsync([new ChatTurn("user", string.Join(' ', args.Skip(1)))]);
        Console.WriteLine($"[{reply.Provider}] {reply.Text}");
        return 0;
    }
    catch (GroqException ex)
    {
        Console.Error.WriteLine("Error: " + ex.Message);
        return 2;
    }
}

var updateCheck = UpdateChecker.CheckAsync("cli");
var english = args.Contains("--en");
args = args.Where(a => a != "--en").ToArray();
var fallbackOnly = args.Length > 0 && args[0] == "--fallback-only";
if (fallbackOnly) args = args.Skip(1).ToArray();

var hasMode = args.Length > 1 && Enum.TryParse<CorrectionMode>(args[0], true, out _);
var mode = hasMode ? Enum.Parse<CorrectionMode>(args[0], true) : CorrectionMode.Correct;
var text = string.Join(' ', hasMode ? args.Skip(1) : args);

if (string.IsNullOrWhiteSpace(text))
{
    Console.Error.WriteLine("Uso: grammar-bubble-cli [Correct|Rewrite|Formal|Casual|Concise|Scientific|Translate] <texto>");
    return 1;
}

var keys = fallbackOnly ? new ApiKeys(null, null, null) : SecretStore.Load();
using var service = ProviderFactory.CreateService(keys)!;

try
{
    Console.WriteLine($"Modo: {mode}  |  Pipeline: {service.Pipeline}");
    Console.WriteLine();

    var lang = english ? DocLanguage.English : DocLanguage.Spanish;
    var result = await service.CorrectAsync(text, mode, lang);
    Console.WriteLine("Resultado: " + result.Corrected);
    Console.WriteLine();
    foreach (var c in result.Changes)
        Console.WriteLine($"  {c.Original} -> {c.Replacement}  ({c.Reason})");

    if (updateCheck.IsCompletedSuccessfully && updateCheck.Result is { } update)
        Console.Error.WriteLine($"Hay una nueva versión ({update.Version}): {update.Url}");

    if (result.Changes.Count > 0)
    {
        var selected = Enumerable.Repeat(true, result.Changes.Count).ToArray();
        selected[0] = false;
        var (partial, _) = ChangeApplier.Apply(text, result.Changes, selected);
        Console.WriteLine();
        Console.WriteLine("Con el primer cambio rechazado: " + partial);
    }
    return 0;
}
catch (GroqException ex)
{
    Console.Error.WriteLine("Error: " + ex.Message);
    return 2;
}
