namespace PromptClipboard.Application.Tests;

using PromptClipboard.Application.Services;
using PromptClipboard.Domain.Models;

public class SearchQueryParserTests
{
    [Fact]
    public void Parse_EmptyString_ReturnsDefault()
    {
        var result = SearchQueryParser.Parse("");
        Assert.Empty(result.FreeTextTerms);
        Assert.Empty(result.IncludeTags);
        Assert.Empty(result.ExcludeTags);
        Assert.Empty(result.ExcludeWords);
        Assert.Null(result.FolderFilter);
        Assert.Null(result.LangFilter);
        Assert.Null(result.PinnedFilter);
        Assert.Null(result.HasTemplate);
        Assert.Null(result.RecentLimit);
        Assert.Equal(SortMode.Relevance, result.Sort);
        Assert.False(result.IsTruncated);
    }

    [Fact]
    public void Parse_FreeText_ExtractsTerms()
    {
        var result = SearchQueryParser.Parse("hello world");
        Assert.Equal(new[] { "hello", "world" }, result.FreeTextTerms);
    }

    [Fact]
    public void Parse_IncludeTag_ExtractsTag()
    {
        var result = SearchQueryParser.Parse("#email");
        Assert.Contains("email", result.IncludeTags);
        Assert.Empty(result.FreeTextTerms);
    }

    [Fact]
    public void Parse_MultipleIncludeTags_ExtractsAll()
    {
        var result = SearchQueryParser.Parse("#email #important");
        Assert.Equal(2, result.IncludeTags.Count);
        Assert.Contains("email", result.IncludeTags);
        Assert.Contains("important", result.IncludeTags);
    }

    [Fact]
    public void Parse_ExcludeTag_ExtractsNegativeTag()
    {
        var result = SearchQueryParser.Parse("-#old");
        Assert.Contains("old", result.ExcludeTags);
        Assert.Empty(result.IncludeTags);
    }

    [Fact]
    public void Parse_ExcludeWord_ExtractsNegativeWord()
    {
        var result = SearchQueryParser.Parse("-obsolete");
        Assert.Contains("obsolete", result.ExcludeWords);
        Assert.Empty(result.FreeTextTerms);
    }

    [Fact]
    public void Parse_LangFilter_ExtractsLang()
    {
        var result = SearchQueryParser.Parse("lang:en");
        Assert.Equal("en", result.LangFilter);
    }

    [Fact]
    public void Parse_FolderFilter_ExtractsFolder()
    {
        var result = SearchQueryParser.Parse("folder:work");
        Assert.Equal("work", result.FolderFilter);
    }

    [Fact]
    public void Parse_IsPinned_SetsFilter()
    {
        var result = SearchQueryParser.Parse("is:pinned");
        Assert.True(result.PinnedFilter);
    }

    [Fact]
    public void Parse_IsTemplate_SetsFilter()
    {
        var result = SearchQueryParser.Parse("is:template");
        Assert.True(result.HasTemplate);
    }

    [Fact]
    public void Parse_QuotedPhrase_PreservedAsOneToken()
    {
        var result = SearchQueryParser.Parse("\"exact phrase\" hello");
        Assert.Equal(2, result.FreeTextTerms.Count);
        Assert.Equal("exact phrase", result.FreeTextTerms[0]);
        Assert.Equal("hello", result.FreeTextTerms[1]);
    }

    [Fact]
    public void Parse_ComplexQuery_ExtractsAll()
    {
        var result = SearchQueryParser.Parse("#email #important lang:en -old -#deprecated hello world");
        Assert.Equal(new[] { "hello", "world" }, result.FreeTextTerms);
        Assert.Contains("email", result.IncludeTags);
        Assert.Contains("important", result.IncludeTags);
        Assert.Contains("deprecated", result.ExcludeTags);
        Assert.Contains("old", result.ExcludeWords);
        Assert.Equal("en", result.LangFilter);
    }

    [Fact]
    public void Parse_UnclosedQuote_TreatedAsLiterals()
    {
        // Unclosed quote: skip the quote char, parse rest as normal token
        var result = SearchQueryParser.Parse("\"hello world");
        // "hello is treated as literal after skipping the quote
        Assert.True(result.FreeTextTerms.Count >= 1);
        Assert.Contains("world", result.FreeTextTerms);
    }

    [Fact]
    public void Parse_MaxTokens_SetsIsTruncated()
    {
        // Generate 25 tokens (exceeds MaxTokens=20)
        var tokens = string.Join(" ", Enumerable.Range(1, 25).Select(i => $"word{i}"));
        var result = SearchQueryParser.Parse(tokens);
        Assert.True(result.IsTruncated);
        Assert.Equal(20, result.FreeTextTerms.Count);
    }

    [Fact]
    public void Parse_MaxTokens_MixedTokens_Deterministic()
    {
        // Mix of tags, free text, filters — truncation is left-to-right
        var tokens = string.Join(" ", Enumerable.Range(1, 25).Select(i => i % 3 == 0 ? $"#tag{i}" : $"word{i}"));
        var result = SearchQueryParser.Parse(tokens);
        Assert.True(result.IsTruncated);
        Assert.True(result.FreeTextTerms.Count + result.IncludeTags.Count == 20);
    }

    [Fact]
    public void Parse_CaseInsensitive_Filters()
    {
        var result = SearchQueryParser.Parse("LANG:EN IS:PINNED IS:TEMPLATE FOLDER:WORK");
        Assert.Equal("EN", result.LangFilter);
        Assert.True(result.PinnedFilter);
        Assert.True(result.HasTemplate);
        Assert.Equal("WORK", result.FolderFilter);
    }

    [Fact]
    public void Parse_DashAlone_IsNotExcludeWord()
    {
        var result = SearchQueryParser.Parse("-");
        Assert.Empty(result.ExcludeWords);
        // Single dash becomes a free text token (not an exclude word)
        Assert.Single(result.FreeTextTerms, "-");
    }

    [Fact]
    public void Parse_HasPositiveTerms_WhenFreeText()
    {
        var result = SearchQueryParser.Parse("hello");
        Assert.True(result.HasPositiveTerms);
        Assert.False(result.HasNegativeTerms);
        Assert.False(result.IsNegativeOnly);
    }

    [Fact]
    public void Parse_IsNegativeOnly_WhenOnlyExcludes()
    {
        var result = SearchQueryParser.Parse("-word -#tag");
        Assert.False(result.HasPositiveTerms);
        Assert.True(result.HasNegativeTerms);
        Assert.True(result.IsNegativeOnly);
    }

    [Fact]
    public void Parse_FirstLangFilterWins()
    {
        var result = SearchQueryParser.Parse("lang:en lang:ru");
        Assert.Equal("en", result.LangFilter);
    }

    [Fact]
    public void Parse_FirstFolderFilterWins()
    {
        var result = SearchQueryParser.Parse("folder:work folder:personal");
        Assert.Equal("work", result.FolderFilter);
    }
}
