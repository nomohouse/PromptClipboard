namespace PromptClipboard.Application.Services;

using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Sørensen–Dice coefficient on character trigrams for language-independent similarity.
/// </summary>
public static class SimilarityScorer
{
    public static double Score(string title1, string body1, string title2, string body2)
    {
        var titleDice = Dice(Normalize(title1), Normalize(title2));
        var bodyDice = Dice(Normalize(body1), Normalize(body2));
        return titleDice * 0.7 + bodyDice * 0.3;
    }

    internal static double Dice(string a, string b)
    {
        if (a.Length < 3 && b.Length < 3)
            return a == b ? 1.0 : 0.0;

        if (a.Length < 3 || b.Length < 3)
        {
            if (a.Length == 0 || b.Length == 0)
                return 0.0;
            return a == b ? 1.0 : 0.0;
        }

        var trigramsA = GetTrigrams(a);
        var trigramsB = GetTrigrams(b);

        if (trigramsA.Count == 0 && trigramsB.Count == 0)
            return 1.0;

        var intersection = 0;
        var countB = new Dictionary<string, int>(trigramsB.Count);
        foreach (var t in trigramsB)
            countB[t] = countB.GetValueOrDefault(t) + 1;

        foreach (var t in trigramsA)
        {
            if (countB.TryGetValue(t, out var c) && c > 0)
            {
                intersection++;
                countB[t] = c - 1;
            }
        }

        return 2.0 * intersection / (trigramsA.Count + trigramsB.Count);
    }

    private static List<string> GetTrigrams(string s)
    {
        if (s.Length < 3) return [];
        var result = new List<string>(s.Length - 2);
        for (var i = 0; i <= s.Length - 3; i++)
            result.Add(s.Substring(i, 3));
        return result;
    }

    internal static string Normalize(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var result = text.Normalize(NormalizationForm.FormKC);
        result = result.ToLowerInvariant();
        result = result.Replace("\r\n", "\n").Replace("\r", "\n");
        result = result.Trim();
        result = Regex.Replace(result, @"\s+", " ");
        return result;
    }
}
