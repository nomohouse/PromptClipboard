namespace PromptClipboard.Application.Tests;

using PromptClipboard.Application.Services;
using PromptClipboard.Application.Tests.Fakes;
using PromptClipboard.Domain;
using PromptClipboard.Domain.Entities;

public class SearchRankingServiceTests
{
    [Theory]
    [InlineData("#email rest of query", "rest of query", "email", null)]
    [InlineData("#Jira", "", "jira", null)]
    [InlineData("lang:ru some query", "some query", null, "ru")]
    [InlineData("#email lang:en query", "query", "email", "en")]
    [InlineData("plain query", "plain query", null, null)]
    [InlineData("", "", null, null)]
    public void ParseQuery_ExtractsFilters(string input, string expectedQuery, string? expectedTag, string? expectedLang)
    {
        var (query, tag, lang) = SearchRankingService.ParseQuery(input);
        Assert.Equal(expectedQuery, query);
        Assert.Equal(expectedTag, tag);
        Assert.Equal(expectedLang, lang);
    }

    [Fact]
    public async Task EmptyQuery_UsesRecentSliceLimit()
    {
        var repo = new FakePromptRepository();
        var service = new SearchRankingService(repo);

        await service.SearchAsync(string.Empty);

        Assert.Equal(SearchDefaults.RecentSliceLimit, repo.LastRecentLimitRequested);
    }

    [Fact]
    public async Task DefaultPath_MergeOrder_PinnedFirst()
    {
        var repo = new FakePromptRepository();
        repo.Prompts.Add(new Prompt { Id = 1, Title = "Recent", Body = "B", IsPinned = false, LastUsedAt = DateTime.UtcNow });
        repo.Prompts.Add(new Prompt { Id = 2, Title = "Pinned", Body = "B", IsPinned = true, CreatedAt = DateTime.UtcNow });
        var service = new SearchRankingService(repo);

        var result = await service.SearchAsync("");

        Assert.True(result.Items.Count >= 2);
        Assert.True(result.Items[0].IsPinned); // Pinned first
    }

    [Fact]
    public async Task DefaultPath_Dedup_FirstSeenWins()
    {
        var repo = new FakePromptRepository();
        // Same prompt is both pinned and recently used
        repo.Prompts.Add(new Prompt { Id = 1, Title = "Both", Body = "B", IsPinned = true, LastUsedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow });
        var service = new SearchRankingService(repo);

        var result = await service.SearchAsync("");

        // Should appear exactly once
        Assert.Single(result.Items, p => p.Id == 1);
    }

    [Fact]
    public async Task GetRecentAsync_ExcludesNullLastUsed()
    {
        var repo = new FakePromptRepository();
        repo.Prompts.Add(new Prompt { Id = 1, Title = "NeverUsed", Body = "B", LastUsedAt = null });
        repo.Prompts.Add(new Prompt { Id = 2, Title = "Used", Body = "B", LastUsedAt = DateTime.UtcNow });

        var recent = await repo.GetRecentAsync();

        Assert.Single(recent);
        Assert.Equal("Used", recent[0].Title);
    }

    [Fact]
    public async Task SearchResult_HasMore_WhenOverLimit()
    {
        var repo = new FakePromptRepository();
        for (int i = 1; i <= SearchDefaults.MaxResults + 5; i++)
            repo.Prompts.Add(new Prompt { Id = i, Title = $"Prompt {i}", Body = $"Body {i}" });
        var service = new SearchRankingService(repo);

        var result = await service.SearchAsync("Prompt");

        Assert.True(result.HasMore);
        Assert.Equal(SearchDefaults.MaxResults, result.Items.Count);
    }

    [Fact]
    public async Task SearchResult_NoMore_WhenUnderLimit()
    {
        var repo = new FakePromptRepository();
        repo.Prompts.Add(new Prompt { Id = 1, Title = "A", Body = "a" });
        var service = new SearchRankingService(repo);

        var result = await service.SearchAsync("A");

        Assert.False(result.HasMore);
        Assert.False(result.IsTruncated);
    }
}
