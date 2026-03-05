namespace PromptClipboard.Application.Tests;

using PromptClipboard.Application.Services;
using PromptClipboard.Application.Tests.Fakes;
using PromptClipboard.Domain;
using PromptClipboard.Domain.Entities;
using PromptClipboard.Domain.Models;

public class SearchRankingServiceTests
{
    [Fact]
    public async Task SearchAsync_AdvancedRepoNull_UsesLegacyPath()
    {
        var repo = new FakePromptRepository();
        repo.Prompts.Add(new Prompt { Id = 1, Title = "Test email", Body = "body" });
        var service = new SearchRankingService(repo); // no advancedRepo

        var result = await service.SearchAsync("#email test");

        // Legacy path uses single-tag extraction
        Assert.False(result.HasMore);
    }

    [Fact]
    public async Task SearchAsync_WithAdvancedRepo_UsesAdvancedPath()
    {
        var repo = new FakePromptRepository();
        repo.Prompts.Add(new Prompt { Id = 1, Title = "Test", Body = "body" });
        repo.Prompts[0].SetTags(["email"]);
        var service = new SearchRankingService(repo, repo); // repo as both

        var result = await service.SearchAsync("#email test");

        // Advanced path routes through SearchQueryParser
        Assert.NotNull(result);
    }

    [Fact]
    public async Task SearchQuery_DefaultList_UsesMixedPipeline()
    {
        var repo = new FakePromptRepository();
        repo.Prompts.Add(new Prompt { Id = 1, Title = "Pinned", Body = "B", IsPinned = true, CreatedAt = DateTime.UtcNow });
        repo.Prompts.Add(new Prompt { Id = 2, Title = "Recent", Body = "B", IsPinned = false, LastUsedAt = DateTime.UtcNow });
        var service = new SearchRankingService(repo, repo);

        // Empty SearchQuery triggers default pipeline
        var result = await service.SearchAsync(new SearchQuery());

        Assert.True(result.Items.Count >= 2);
        Assert.True(result.Items[0].IsPinned); // Pinned first in default
    }

    [Fact]
    public async Task SearchQuery_NonDefault_UsesAdvancedRepoPath()
    {
        var repo = new FakePromptRepository();
        repo.Prompts.Add(new Prompt { Id = 1, Title = "Keep", Body = "B" });
        repo.Prompts[0].SetTags(["email"]);
        repo.Prompts.Add(new Prompt { Id = 2, Title = "Exclude", Body = "B" });
        repo.Prompts[1].SetTags(["old"]);
        var service = new SearchRankingService(repo, repo);

        var query = new SearchQuery { IncludeTags = ["email"] };
        var result = await service.SearchAsync(query);

        Assert.Single(result.Items);
        Assert.Equal("Keep", result.Items[0].Title);
    }

    [Fact]
    public async Task IsDefaultListQuery_WithPinnedFilter_NotDefault()
    {
        var repo = new FakePromptRepository();
        repo.Prompts.Add(new Prompt { Id = 1, Title = "Pinned", Body = "B", IsPinned = true });
        repo.Prompts.Add(new Prompt { Id = 2, Title = "Not", Body = "B", IsPinned = false });
        var service = new SearchRankingService(repo, repo);

        var query = new SearchQuery { PinnedFilter = true };
        var result = await service.SearchAsync(query);

        Assert.Single(result.Items); // Only pinned via advanced path
    }

    [Fact]
    public async Task StringOverload_RoutesThroughSearchAsyncQuery()
    {
        var repo = new FakePromptRepository();
        repo.Prompts.Add(new Prompt { Id = 1, Title = "Email template", Body = "body" });
        repo.Prompts[0].SetTags(["email"]);
        repo.Prompts.Add(new Prompt { Id = 2, Title = "Code review", Body = "body" });
        var service = new SearchRankingService(repo, repo);

        var result = await service.SearchAsync("#email");

        Assert.Single(result.Items);
        Assert.Equal("Email template", result.Items[0].Title);
    }

    [Fact]
    public async Task DefaultPath_Parity_StringVsQueryOverloads()
    {
        var repo = new FakePromptRepository();
        repo.Prompts.Add(new Prompt { Id = 1, Title = "A", Body = "B", IsPinned = true, CreatedAt = DateTime.UtcNow });
        repo.Prompts.Add(new Prompt { Id = 2, Title = "B", Body = "B", LastUsedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow });
        var service = new SearchRankingService(repo, repo);

        var stringResult = await service.SearchAsync("");
        var queryResult = await service.SearchAsync(new SearchQuery());

        Assert.Equal(stringResult.Items.Count, queryResult.Items.Count);
        for (int i = 0; i < stringResult.Items.Count; i++)
            Assert.Equal(stringResult.Items[i].Id, queryResult.Items[i].Id);
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
