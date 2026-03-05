namespace PromptClipboard.Application.Services;

using PromptClipboard.Domain.Models;
using System.Text.RegularExpressions;

public static partial class SearchQueryParser
{
    private const int MaxTokens = 20;

    public static SearchQuery Parse(string rawQuery)
    {
        if (string.IsNullOrWhiteSpace(rawQuery))
            return new SearchQuery();

        var tokens = Tokenize(rawQuery);
        var isTruncated = tokens.Count > MaxTokens;
        if (isTruncated)
            tokens = tokens.Take(MaxTokens).ToList();

        var freeText = new List<string>();
        var includeTags = new List<string>();
        var excludeTags = new List<string>();
        var excludeWords = new List<string>();
        string? folderFilter = null;
        string? langFilter = null;
        bool? pinnedFilter = null;
        bool? hasTemplate = null;
        int? recentLimit = null;
        var sort = SortMode.Relevance;

        foreach (var token in tokens)
        {
            if (token.StartsWith("-#", StringComparison.Ordinal))
            {
                var tag = token[2..];
                if (tag.Length > 0) excludeTags.Add(tag.ToLowerInvariant());
            }
            else if (token.StartsWith('#'))
            {
                var tag = token[1..];
                if (tag.Length > 0) includeTags.Add(tag.ToLowerInvariant());
            }
            else if (token.StartsWith('-') && token.Length > 1)
            {
                excludeWords.Add(token[1..]);
            }
            else if (token.StartsWith("lang:", StringComparison.OrdinalIgnoreCase) && token.Length > 5)
            {
                langFilter ??= token[5..];
            }
            else if (token.StartsWith("folder:", StringComparison.OrdinalIgnoreCase) && token.Length > 7)
            {
                folderFilter ??= token[7..];
            }
            else if (token.Equals("is:pinned", StringComparison.OrdinalIgnoreCase))
            {
                pinnedFilter = true;
            }
            else if (token.Equals("is:template", StringComparison.OrdinalIgnoreCase))
            {
                hasTemplate = true;
            }
            else
            {
                freeText.Add(token);
            }
        }

        return new SearchQuery
        {
            FreeTextTerms = freeText,
            IncludeTags = includeTags,
            ExcludeTags = excludeTags,
            ExcludeWords = excludeWords,
            FolderFilter = folderFilter,
            LangFilter = langFilter,
            PinnedFilter = pinnedFilter,
            HasTemplate = hasTemplate,
            RecentLimit = recentLimit,
            Sort = sort,
            IsTruncated = isTruncated
        };
    }

    private static List<string> Tokenize(string input)
    {
        var tokens = new List<string>();
        var i = 0;
        while (i < input.Length)
        {
            // Skip whitespace
            if (char.IsWhiteSpace(input[i]))
            {
                i++;
                continue;
            }

            // Quoted phrase
            if (input[i] == '"')
            {
                var closeQuote = input.IndexOf('"', i + 1);
                if (closeQuote > i + 1)
                {
                    tokens.Add(input[(i + 1)..closeQuote]);
                    i = closeQuote + 1;
                    continue;
                }
                // Unclosed quote: treat '"' as literal part of token
                // Fall through to regular token parsing (skip the quote char)
                i++;
                if (i >= input.Length) break;
            }

            // Regular token (until whitespace)
            var start = i;
            while (i < input.Length && !char.IsWhiteSpace(input[i]))
                i++;
            var tok = input[start..i];
            if (tok.Length > 0)
                tokens.Add(tok);
        }

        return tokens;
    }
}
