namespace GrammarBubble.Core;

/// Veredicto corto del texto revisado, calculado a partir del número de ajustes propuestos.
public static class Verdict
{
    public static string For(IReadOnlyCollection<Change> changes, DocLanguage lang)
    {
        var n = changes.Count;
        var en = lang == DocLanguage.English;

        if (n == 0)
            return en ? "Verdict: no issues found." : "Veredicto: sin errores detectados.";
        if (n <= 3)
            return en ? $"Verdict: good text, {n} minor adjustment(s)." : $"Veredicto: texto bueno, {n} ajuste(s) menor(es).";
        if (n <= 10)
            return en ? $"Verdict: review recommended, {n} adjustments." : $"Veredicto: revisión recomendada, {n} ajustes.";
        return en ? $"Verdict: needs thorough revision, {n} adjustments." : $"Veredicto: requiere revisión a fondo, {n} ajustes.";
    }
}
