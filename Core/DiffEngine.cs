using System.Text.RegularExpressions;

namespace GrammarBubble.Core;

public static class DiffEngine
{
    private static readonly Regex Tokens = new(@"\s+|[^\s]+", RegexOptions.Compiled);

    public static IReadOnlyList<DiffSegment> Diff(string before, string after)
    {
        var a = Tokenize(before);
        var b = Tokenize(after);
        int n = a.Count, m = b.Count;

        var lcs = new int[n + 1, m + 1];
        for (int i = n - 1; i >= 0; i--)
            for (int j = m - 1; j >= 0; j--)
                lcs[i, j] = a[i] == b[j]
                    ? lcs[i + 1, j + 1] + 1
                    : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);

        var raw = new List<DiffSegment>();
        int x = 0, y = 0;
        while (x < n && y < m)
        {
            if (a[x] == b[y])
            {
                raw.Add(new(DiffKind.Equal, a[x]));
                x++; y++;
            }
            else if (lcs[x + 1, y] >= lcs[x, y + 1])
            {
                raw.Add(new(DiffKind.Delete, a[x]));
                x++;
            }
            else
            {
                raw.Add(new(DiffKind.Insert, b[y]));
                y++;
            }
        }
        while (x < n) raw.Add(new(DiffKind.Delete, a[x++]));
        while (y < m) raw.Add(new(DiffKind.Insert, b[y++]));

        return Merge(raw);
    }

    private static List<string> Tokenize(string text) =>
        Tokens.Matches(text).Select(m => m.Value).ToList();

    private static List<DiffSegment> Merge(List<DiffSegment> raw)
    {
        var merged = new List<DiffSegment>();
        foreach (var seg in raw)
        {
            if (merged.Count > 0 && merged[^1].Kind == seg.Kind)
                merged[^1] = merged[^1] with { Text = merged[^1].Text + seg.Text };
            else
                merged.Add(seg);
        }
        return merged;
    }
}
