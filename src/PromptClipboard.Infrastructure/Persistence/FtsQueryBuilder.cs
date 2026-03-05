namespace PromptClipboard.Infrastructure.Persistence;

using PromptClipboard.Domain.Models;

public static class FtsQueryBuilder
{
    /// <summary>
    /// Builds FTS5 MATCH expression from SearchQuery AST.
    /// Each FreeTextTerm is quoted as-is (already tokenized by parser).
    /// Returns null if no positive terms (negative-only query).
    /// </summary>
    public static string? Build(SearchQuery query)
    {
        if (!query.HasPositiveTerms)
            return null;

        var parts = new List<string>();

        foreach (var term in query.FreeTextTerms)
            parts.Add("\"" + term.Replace("\"", "\"\"") + "\"");

        foreach (var word in query.ExcludeWords)
            parts.Add("NOT \"" + word.Replace("\"", "\"\"") + "\"");

        return string.Join(" ", parts);
    }
}
