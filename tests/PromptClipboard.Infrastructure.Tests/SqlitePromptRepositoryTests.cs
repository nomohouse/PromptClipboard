namespace PromptClipboard.Infrastructure.Tests;

using Microsoft.Data.Sqlite;
using PromptClipboard.Domain;
using PromptClipboard.Domain.Entities;
using PromptClipboard.Domain.Models;
using PromptClipboard.Infrastructure.Persistence;
using Serilog;

public class SqlitePromptRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SqlitePromptRepository _repo;

    public SqlitePromptRepositoryTests()
    {
        // Use a named shared-cache in-memory database so that multiple connections
        // (created by the factory) all see the same data. This sentinel connection
        // keeps the database alive for the lifetime of the test.
        var dbName = $"TestDb_{Guid.NewGuid():N}";
        _connection = new SqliteConnection($"Data Source={dbName};Mode=Memory;Cache=Shared");
        _connection.Open();

        // Enable foreign keys
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys=ON;";
        cmd.ExecuteNonQuery();

        var factory = new SqliteConnectionFactory(_connection);
        var log = new LoggerConfiguration().CreateLogger();
        var runner = new MigrationRunner(factory, log);
        runner.RunAll();

        _repo = new SqlitePromptRepository(factory);
    }

    [Fact]
    public async Task CreateAndGetById_Works()
    {
        var prompt = new Prompt { Title = "Test", Body = "Body text" };
        prompt.SetTags(new[] { "tag1", "tag2" });

        var id = await _repo.CreateAsync(prompt);
        Assert.True(id > 0);

        var loaded = await _repo.GetByIdAsync(id);
        Assert.NotNull(loaded);
        Assert.Equal("Test", loaded.Title);
        Assert.Equal("Body text", loaded.Body);
        Assert.Contains("tag1", loaded.GetTags());
    }

    [Fact]
    public async Task Search_FTS5Works()
    {
        var p1 = new Prompt { Title = "Email template", Body = "Professional email" };
        p1.SetTags(new[] { "email" });
        await _repo.CreateAsync(p1);

        var p2 = new Prompt { Title = "Jira ticket", Body = "Bug report" };
        p2.SetTags(new[] { "jira" });
        await _repo.CreateAsync(p2);

        var results = await _repo.SearchAsync("email");
        Assert.Single(results);
        Assert.Equal("Email template", results[0].Title);
    }

    [Fact]
    public async Task Search_UsesCanonicalMaxResultsPlusOneLimit()
    {
        for (var i = 0; i < SearchDefaults.MaxResults + 5; i++)
        {
            var prompt = new Prompt { Title = $"Email template {i}", Body = "email body" };
            prompt.SetTags(["email"]);
            await _repo.CreateAsync(prompt);
        }

        var results = await _repo.SearchAsync("email");

        // Repo returns at most MaxResults+1 for HasMore detection by caller
        Assert.True(results.Count <= SearchDefaults.MaxResults + 1);
        Assert.True(results.Count > 0);
    }

    [Fact]
    public async Task Search_TagFilterWorks()
    {
        var p1 = new Prompt { Title = "Test 1", Body = "Body 1" };
        p1.SetTags(new[] { "email", "work" });
        await _repo.CreateAsync(p1);

        var p2 = new Prompt { Title = "Test 2", Body = "Body 2" };
        p2.SetTags(new[] { "jira" });
        await _repo.CreateAsync(p2);

        var results = await _repo.SearchAsync("", tagFilter: "email");
        Assert.Single(results);
        Assert.Equal("Test 1", results[0].Title);
    }

    [Fact]
    public async Task Search_TagFilterIsCaseInsensitive()
    {
        var p1 = new Prompt { Title = "Test", Body = "Body" };
        p1.SetTags(new[] { "email" }); // stored as lowercase
        await _repo.CreateAsync(p1);

        var results = await _repo.SearchAsync("", tagFilter: "Email");
        Assert.Single(results);
    }

    [Fact]
    public async Task GetPinned_ReturnsPinnedOnly()
    {
        var p1 = new Prompt { Title = "Pinned", Body = "Body", IsPinned = true };
        await _repo.CreateAsync(p1);

        var p2 = new Prompt { Title = "Not pinned", Body = "Body", IsPinned = false };
        await _repo.CreateAsync(p2);

        var results = await _repo.GetPinnedAsync();
        Assert.Single(results);
        Assert.Equal("Pinned", results[0].Title);
    }

    [Fact]
    public async Task MarkUsed_IncrementsCountAndSetsDate()
    {
        var p = new Prompt { Title = "Test", Body = "Body" };
        var id = await _repo.CreateAsync(p);

        var now = DateTime.UtcNow;
        await _repo.MarkUsedAsync(id, now);

        var loaded = await _repo.GetByIdAsync(id);
        Assert.NotNull(loaded);
        Assert.Equal(1, loaded.UseCount);
        Assert.NotNull(loaded.LastUsedAt);
    }

    [Fact]
    public async Task Update_ModifiesPrompt()
    {
        var p = new Prompt { Title = "Original", Body = "Body" };
        var id = await _repo.CreateAsync(p);

        var loaded = await _repo.GetByIdAsync(id);
        Assert.NotNull(loaded);
        loaded.Title = "Updated";
        loaded.UpdatedAt = DateTime.UtcNow;
        await _repo.UpdateAsync(loaded);

        var updated = await _repo.GetByIdAsync(id);
        Assert.NotNull(updated);
        Assert.Equal("Updated", updated.Title);
    }

    [Fact]
    public async Task Delete_RemovesPrompt()
    {
        var p = new Prompt { Title = "ToDelete", Body = "Body" };
        var id = await _repo.CreateAsync(p);

        await _repo.DeleteAsync(id);

        var loaded = await _repo.GetByIdAsync(id);
        Assert.Null(loaded);
    }

    [Fact]
    public async Task GetCount_ReturnsCorrectCount()
    {
        Assert.Equal(0, await _repo.GetCountAsync());

        await _repo.CreateAsync(new Prompt { Title = "A", Body = "B" });
        await _repo.CreateAsync(new Prompt { Title = "C", Body = "D" });

        Assert.Equal(2, await _repo.GetCountAsync());
    }

    [Fact]
    public async Task GetRecent_ReturnsRecentlyUsed()
    {
        var p1 = new Prompt { Title = "Old", Body = "B" };
        var id1 = await _repo.CreateAsync(p1);
        await _repo.MarkUsedAsync(id1, DateTime.UtcNow.AddDays(-2));

        var p2 = new Prompt { Title = "New", Body = "B" };
        var id2 = await _repo.CreateAsync(p2);
        await _repo.MarkUsedAsync(id2, DateTime.UtcNow);

        // Also create a never-used prompt — should NOT appear in strict recent
        var p3 = new Prompt { Title = "NeverUsed", Body = "B" };
        await _repo.CreateAsync(p3);

        var results = await _repo.GetRecentAsync(SearchDefaults.RecentSliceLimit);
        Assert.Equal(2, results.Count); // Only used prompts
        Assert.Equal("New", results[0].Title); // Most recent first
        Assert.DoesNotContain(results, r => r.Title == "NeverUsed");
    }

    [Fact]
    public async Task GetRecentAsync_ExcludesNullLastUsed()
    {
        // Create prompt without marking used (LastUsedAt stays null)
        var p1 = new Prompt { Title = "NeverUsed", Body = "B" };
        await _repo.CreateAsync(p1);

        var p2 = new Prompt { Title = "Used", Body = "B" };
        var id2 = await _repo.CreateAsync(p2);
        await _repo.MarkUsedAsync(id2, DateTime.UtcNow);

        var results = await _repo.GetRecentAsync();
        Assert.Single(results);
        Assert.Equal("Used", results[0].Title);
    }

    [Fact]
    public async Task GetPinnedAsync_WithLimit_ReturnsAtMostLimitPlusOne()
    {
        // Create more than limit pinned prompts
        for (int i = 0; i < 5; i++)
        {
            var p = new Prompt { Title = $"Pinned{i}", Body = "B", IsPinned = true };
            await _repo.CreateAsync(p);
        }

        var result = await _repo.GetPinnedAsync(3);
        Assert.True(result.Count <= 4); // limit+1 = 4
    }

    [Fact]
    public async Task GetPinnedAsync_WithLimit_OnlyPinned()
    {
        await _repo.CreateAsync(new Prompt { Title = "Pinned", Body = "B", IsPinned = true });
        await _repo.CreateAsync(new Prompt { Title = "NotPinned", Body = "B", IsPinned = false });

        var result = await _repo.GetPinnedAsync(10);
        Assert.Single(result);
        Assert.Equal("Pinned", result[0].Title);
    }

    // ── Advanced SearchQuery tests ─────────────────────────────────────

    [Fact]
    public async Task AdvancedSearch_MultiTagAnd_FiltersCorrectly()
    {
        var p1 = new Prompt { Title = "Both tags", Body = "B" };
        p1.SetTags(["email", "important"]);
        await _repo.CreateAsync(p1);

        var p2 = new Prompt { Title = "Only email", Body = "B" };
        p2.SetTags(["email"]);
        await _repo.CreateAsync(p2);

        var query = new SearchQuery { IncludeTags = ["email", "important"] };
        var results = await _repo.SearchAsync(query);
        Assert.Single(results);
        Assert.Equal("Both tags", results[0].Title);
    }

    [Fact]
    public async Task AdvancedSearch_ExcludeTag_FiltersOut()
    {
        var p1 = new Prompt { Title = "Keep", Body = "B" };
        p1.SetTags(["email"]);
        await _repo.CreateAsync(p1);

        var p2 = new Prompt { Title = "Remove", Body = "B" };
        p2.SetTags(["deprecated"]);
        await _repo.CreateAsync(p2);

        var query = new SearchQuery { ExcludeTags = ["deprecated"] };
        var results = await _repo.SearchAsync(query);
        Assert.DoesNotContain(results, r => r.Title == "Remove");
        Assert.Contains(results, r => r.Title == "Keep");
    }

    [Fact]
    public async Task AdvancedSearch_FolderFilter_FiltersCorrectly()
    {
        await _repo.CreateAsync(new Prompt { Title = "Work", Body = "B", Folder = "work" });
        await _repo.CreateAsync(new Prompt { Title = "Personal", Body = "B", Folder = "personal" });

        var query = new SearchQuery { FolderFilter = "work" };
        var results = await _repo.SearchAsync(query);
        Assert.Single(results);
        Assert.Equal("Work", results[0].Title);
    }

    [Fact]
    public async Task AdvancedSearch_HasTemplateFilter()
    {
        await _repo.CreateAsync(new Prompt { Title = "Template", Body = "Hello {{name}}" });
        await _repo.CreateAsync(new Prompt { Title = "Plain", Body = "Hello world" });

        var query = new SearchQuery { HasTemplate = true };
        var results = await _repo.SearchAsync(query);
        Assert.Single(results);
        Assert.Equal("Template", results[0].Title);
    }

    [Fact]
    public async Task AdvancedSearch_RecentLimit_ExcludesNullLastUsed()
    {
        var p1 = new Prompt { Title = "Used", Body = "B" };
        var id1 = await _repo.CreateAsync(p1);
        await _repo.MarkUsedAsync(id1, DateTime.UtcNow);

        await _repo.CreateAsync(new Prompt { Title = "NeverUsed", Body = "B" });

        var query = new SearchQuery { RecentLimit = 10, Sort = SortMode.Recent };
        var results = await _repo.SearchAsync(query);
        Assert.Single(results);
        Assert.Equal("Used", results[0].Title);
    }

    [Fact]
    public async Task AdvancedSearch_NegativeOnly_FallbackWithoutFts()
    {
        await _repo.CreateAsync(new Prompt { Title = "Hello world", Body = "Good content" });
        await _repo.CreateAsync(new Prompt { Title = "Bad stuff", Body = "Bad content" });

        var query = new SearchQuery { ExcludeWords = ["Bad"] };
        var results = await _repo.SearchAsync(query);
        Assert.Single(results);
        Assert.Equal("Hello world", results[0].Title);
    }

    [Fact]
    public async Task AdvancedSearch_NegativeOnly_LikeEscapes_WildcardsLiteral()
    {
        await _repo.CreateAsync(new Prompt { Title = "100% done", Body = "B" });
        await _repo.CreateAsync(new Prompt { Title = "foo_bar", Body = "B" });
        await _repo.CreateAsync(new Prompt { Title = "Normal", Body = "B" });

        // Exclude "100%" — should treat % as literal
        var query1 = new SearchQuery { ExcludeWords = ["100%"] };
        var results1 = await _repo.SearchAsync(query1);
        Assert.DoesNotContain(results1, r => r.Title == "100% done");
        Assert.Contains(results1, r => r.Title == "Normal");

        // Exclude "foo_bar" — should treat _ as literal
        var query2 = new SearchQuery { ExcludeWords = ["foo_bar"] };
        var results2 = await _repo.SearchAsync(query2);
        Assert.DoesNotContain(results2, r => r.Title == "foo_bar");
    }

    [Fact]
    public async Task AdvancedSearch_SortModes()
    {
        var p1 = new Prompt { Title = "Old", Body = "B", IsPinned = false };
        var id1 = await _repo.CreateAsync(p1);
        await _repo.MarkUsedAsync(id1, DateTime.UtcNow.AddDays(-2));

        var p2 = new Prompt { Title = "New", Body = "B", IsPinned = true };
        var id2 = await _repo.CreateAsync(p2);
        await _repo.MarkUsedAsync(id2, DateTime.UtcNow);
        // Mark used again for higher use_count
        await _repo.MarkUsedAsync(id2, DateTime.UtcNow);

        // Recent sort
        var recentQuery = new SearchQuery { Sort = SortMode.Recent };
        var recentResults = await _repo.SearchAsync(recentQuery);
        Assert.Equal("New", recentResults[0].Title);

        // MostUsed sort
        var usedQuery = new SearchQuery { Sort = SortMode.MostUsed };
        var usedResults = await _repo.SearchAsync(usedQuery);
        Assert.Equal("New", usedResults[0].Title); // 2 uses vs 1

        // PinnedFirst sort
        var pinnedQuery = new SearchQuery { Sort = SortMode.PinnedFirst };
        var pinnedResults = await _repo.SearchAsync(pinnedQuery);
        Assert.Equal("New", pinnedResults[0].Title); // is_pinned = true
    }

    [Fact]
    public async Task AdvancedSearch_IncludeTags_DeduplicatesInputTokens()
    {
        var p1 = new Prompt { Title = "Tagged", Body = "B" };
        p1.SetTags(["email"]);
        await _repo.CreateAsync(p1);

        // Duplicate tag should not break COUNT(DISTINCT ...) = @includeTagCount
        var query = new SearchQuery { IncludeTags = ["email", "email"] };
        var results = await _repo.SearchAsync(query);
        Assert.Single(results);
    }

    [Fact]
    public async Task AdvancedSearch_PinnedFilter()
    {
        await _repo.CreateAsync(new Prompt { Title = "Pinned", Body = "B", IsPinned = true });
        await _repo.CreateAsync(new Prompt { Title = "NotPinned", Body = "B", IsPinned = false });

        var query = new SearchQuery { PinnedFilter = true };
        var results = await _repo.SearchAsync(query);
        Assert.Single(results);
        Assert.Equal("Pinned", results[0].Title);
    }

    [Fact]
    public async Task AdvancedSearch_FreeTextWithFts()
    {
        var p1 = new Prompt { Title = "Email template", Body = "Professional email" };
        p1.SetTags(["email"]);
        await _repo.CreateAsync(p1);

        await _repo.CreateAsync(new Prompt { Title = "Code review", Body = "Review code" });

        var query = new SearchQuery { FreeTextTerms = ["email"] };
        var results = await _repo.SearchAsync(query);
        Assert.Single(results);
        Assert.Equal("Email template", results[0].Title);
    }

    [Fact]
    public async Task AdvancedSearch_LangFilter()
    {
        await _repo.CreateAsync(new Prompt { Title = "English", Body = "B", Lang = "en" });
        await _repo.CreateAsync(new Prompt { Title = "Russian", Body = "B", Lang = "ru" });

        var query = new SearchQuery { LangFilter = "en" };
        var results = await _repo.SearchAsync(query);
        Assert.Single(results);
        Assert.Equal("English", results[0].Title);
    }

    // ── P2: V003b body_hash tests ─────────────────────────────────────

    [Fact]
    public void V003b_BodyHash_ColumnExists()
    {
        // V003b migration runs in setup — verify column exists
        using var conn = new SqliteConnection(_connection.ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('prompts') WHERE name='body_hash'";
        var count = (long)cmd.ExecuteScalar()!;
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task V003b_CreateAsync_StoresBodyHash()
    {
        var id = await _repo.CreateAsync(new Prompt { Title = "Test", Body = "Hello world" });
        var prompt = await _repo.GetByIdAsync(id);
        Assert.NotNull(prompt);
        Assert.NotNull(prompt!.BodyHash);
        Assert.Equal(64, prompt.BodyHash!.Length); // SHA256 hex
    }

    [Fact]
    public async Task V003b_UpdateAsync_UpdatesBodyHash()
    {
        var id = await _repo.CreateAsync(new Prompt { Title = "Test", Body = "Original" });
        var prompt = await _repo.GetByIdAsync(id);
        var originalHash = prompt!.BodyHash;

        prompt.Body = "Modified";
        await _repo.UpdateAsync(prompt);

        var updated = await _repo.GetByIdAsync(id);
        Assert.NotEqual(originalHash, updated!.BodyHash);
    }

    [Fact]
    public async Task V003b_SameBody_SameHash()
    {
        var id1 = await _repo.CreateAsync(new Prompt { Title = "A", Body = "Same body" });
        var id2 = await _repo.CreateAsync(new Prompt { Title = "B", Body = "Same body" });

        var p1 = await _repo.GetByIdAsync(id1);
        var p2 = await _repo.GetByIdAsync(id2);

        Assert.Equal(p1!.BodyHash, p2!.BodyHash);
    }

    // ── P2: Tag suggestion tests ────────────────────────────────────

    [Fact]
    public async Task GetAllTags_ReturnsDistinct()
    {
        var p1 = new Prompt { Title = "A", Body = "B" };
        p1.SetTags(["email", "work"]);
        await _repo.CreateAsync(p1);

        var p2 = new Prompt { Title = "C", Body = "D" };
        p2.SetTags(["email", "personal"]);
        await _repo.CreateAsync(p2);

        var tags = await _repo.GetAllTagsAsync();
        Assert.Equal(3, tags.Count);
        Assert.Contains("email", tags);
        Assert.Contains("work", tags);
        Assert.Contains("personal", tags);
    }

    // ── P2: Duplicate detection tests ───────────────────────────────

    [Fact]
    public async Task FindCandidates_ExactBodyHash_ReturnsMatch()
    {
        await _repo.CreateAsync(new Prompt { Title = "Original", Body = "Unique content here" });

        var candidates = await _repo.FindCandidatesAsync("Different title", "Unique content here");
        Assert.Single(candidates);
        Assert.Equal("Original", candidates[0].Title);
    }

    [Fact]
    public async Task FindCandidates_NoMatch_ReturnsEmpty()
    {
        await _repo.CreateAsync(new Prompt { Title = "A", Body = "Completely different" });

        var candidates = await _repo.FindCandidatesAsync("Test", "xyzzy");
        Assert.Empty(candidates);
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}
