using System.Text.RegularExpressions;

namespace GrammarBubble.Core;

public sealed record AppliedChange(Change Change, bool Applicable, string Sentence);

public static class ChangeApplier
{
    private static readonly Regex SentenceBreak = new(@"(?<=[.!?])\s+|\n+", RegexOptions.Compiled);

    public static (string Text, IReadOnlyList<AppliedChange> Changes) Apply(
        string original,
        IReadOnlyList<Change> changes,
        IReadOnlyList<bool> selected)
    {
        var positions = new int[changes.Count];
        int cursor = 0;
        for (int i = 0; i < changes.Count; i++)
        {
            var needle = changes[i].Original;
            int pos = string.IsNullOrEmpty(needle) ? -1 : original.IndexOf(needle, cursor, StringComparison.Ordinal);
            positions[i] = pos;
            if (pos >= 0) cursor = pos + needle.Length;
        }

        var output = new System.Text.StringBuilder();
        int last = 0;
        for (int i = 0; i < changes.Count; i++)
        {
            if (positions[i] < 0 || !selected[i]) continue;
            output.Append(original, last, positions[i] - last);
            output.Append(changes[i].Replacement);
            last = positions[i] + changes[i].Original.Length;
        }
        output.Append(original, last, original.Length - last);

        var applied = changes.Select((c, i) => new AppliedChange(
            c,
            positions[i] >= 0,
            positions[i] >= 0 ? SentenceAt(original, positions[i]) : c.Original)).ToList();

        return (output.ToString(), applied);
    }

    private static string SentenceAt(string text, int position)
    {
        int start = 0;
        int end = text.Length;
        foreach (Match m in SentenceBreak.Matches(text))
        {
            if (m.Index + m.Length <= position) start = m.Index + m.Length;
            else { end = m.Index; break; }
        }
        return text.Substring(start, end - start).Trim();
    }
}
