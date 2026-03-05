namespace PromptClipboard.Infrastructure.Tests;

using PromptClipboard.Domain.Models;
using PromptClipboard.Infrastructure.Persistence;

public class FtsQueryBuilderTests
{
    [Fact]
    public void Build_PositiveTerms_QuotedOutput()
    {
        var query = new SearchQuery { FreeTextTerms = ["hello", "world"] };
        var result = FtsQueryBuilder.Build(query);
        Assert.NotNull(result);
        Assert.Equal("\"hello\" \"world\"", result);
    }

    [Fact]
    public void Build_QuotedPhrase_PreservedAsOneToken()
    {
        var query = new SearchQuery { FreeTextTerms = ["exact phrase"] };
        var result = FtsQueryBuilder.Build(query);
        Assert.Equal("\"exact phrase\"", result);
    }

    [Fact]
    public void Build_NegativeOnly_ReturnsNull()
    {
        var query = new SearchQuery { ExcludeWords = ["old"] };
        var result = FtsQueryBuilder.Build(query);
        Assert.Null(result);
    }

    [Fact]
    public void Build_MixedPositiveNegative_IncludesNot()
    {
        var query = new SearchQuery
        {
            FreeTextTerms = ["hello"],
            ExcludeWords = ["old"]
        };
        var result = FtsQueryBuilder.Build(query);
        Assert.NotNull(result);
        Assert.Equal("\"hello\" NOT \"old\"", result);
    }

    [Fact]
    public void Build_QuotesInTerm_Escaped()
    {
        var query = new SearchQuery { FreeTextTerms = ["say \"hello\""] };
        var result = FtsQueryBuilder.Build(query);
        Assert.NotNull(result);
        Assert.Equal("\"say \"\"hello\"\"\"", result);
    }

    [Fact]
    public void Build_NoTerms_ReturnsNull()
    {
        var query = new SearchQuery();
        var result = FtsQueryBuilder.Build(query);
        Assert.Null(result);
    }
}
