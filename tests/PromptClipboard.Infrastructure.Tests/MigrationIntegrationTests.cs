namespace PromptClipboard.Infrastructure.Tests;

using Microsoft.Data.Sqlite;
using PromptClipboard.Domain.Entities;
using PromptClipboard.Infrastructure.Persistence;
using Serilog;

public class MigrationIntegrationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SqliteConnectionFactory _factory;
    private readonly ILogger _log = new LoggerConfiguration().CreateLogger();

    public MigrationIntegrationTests()
    {
        var dbName = $"MigTest_{Guid.NewGuid():N}";
        _connection = new SqliteConnection($"Data Source={dbName};Mode=Memory;Cache=Shared");
        _connection.Open();

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys=ON;";
        cmd.ExecuteNonQuery();

        _factory = new SqliteConnectionFactory(_connection);
    }

    private MigrationRunner CreateRunner()
    {
        return new MigrationRunner(_factory, _log);
    }

    // ── V004 tests ───────────────────────────────────────────────────

    [Fact]
    public void V004_Idempotent_SecondRunNoOp()
    {
        var runner = CreateRunner();
        runner.RunAll();

        // Seed some data
        using var conn = _factory.CreateConnection();
        using var insertCmd = conn.CreateCommand();
        insertCmd.CommandText = """
            INSERT INTO prompts (title, body, tags_json, tags_text) VALUES ('Test', 'Body', '["email","work"]', 'email work');
        """;
        insertCmd.ExecuteNonQuery();

        // Manually insert into prompt_tags to simulate V004
        using var tagCmd = conn.CreateCommand();
        tagCmd.CommandText = """
            INSERT OR IGNORE INTO prompt_tags (prompt_id, tag) VALUES (1, 'email');
            INSERT OR IGNORE INTO prompt_tags (prompt_id, tag) VALUES (1, 'work');
        """;
        tagCmd.ExecuteNonQuery();

        // Second run should not fail or duplicate
        var runner2 = CreateRunner();
        runner2.RunAll();

        using var countCmd = conn.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(*) FROM prompt_tags WHERE prompt_id = 1";
        var count = (long)countCmd.ExecuteScalar()!;
        Assert.Equal(2, count);
    }

    [Fact]
    public void V004_BackfillCorrectness()
    {
        var runner = CreateRunner();
        runner.RunAll();

        // prompt_tags should have tags from initial seeding through V004
        using var conn = _factory.CreateConnection();

        // Insert a prompt with tags
        using var insertCmd = conn.CreateCommand();
        insertCmd.CommandText = """
            INSERT INTO prompts (title, body, tags_json, tags_text) VALUES ('Test', 'Body', '["Email","Work","personal"]', 'Email Work personal');
        """;
        insertCmd.ExecuteNonQuery();

        // V004 backfill runs during RunAll for prompts that existed before V004
        // But this prompt was added after V004 ran. Let's verify the prompt_tags table exists
        // and manually run the backfill SQL to test it:
        using var backfillCmd = conn.CreateCommand();
        backfillCmd.CommandText = """
            INSERT OR IGNORE INTO prompt_tags (prompt_id, tag)
                SELECT p.id, LOWER(TRIM(j.value))
                FROM prompts p, json_each(p.tags_json) j
                WHERE json_valid(p.tags_json) AND TRIM(j.value) != ''
                  AND p.id NOT IN (SELECT DISTINCT prompt_id FROM prompt_tags)
        """;
        backfillCmd.ExecuteNonQuery();

        using var selectCmd = conn.CreateCommand();
        selectCmd.CommandText = "SELECT tag FROM prompt_tags WHERE prompt_id = 1 ORDER BY tag";
        var tags = new List<string>();
        using var reader = selectCmd.ExecuteReader();
        while (reader.Read()) tags.Add(reader.GetString(0));

        Assert.Equal(3, tags.Count);
        Assert.Contains("email", tags); // lowercased
        Assert.Contains("work", tags);
        Assert.Contains("personal", tags);
    }

    [Fact]
    public void V004_PromptTags_TableExists()
    {
        var runner = CreateRunner();
        runner.RunAll();

        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='prompt_tags'";
        var exists = (long)cmd.ExecuteScalar()! > 0;
        Assert.True(exists);
    }

    [Fact]
    public void V004_PromptTags_IndexExists()
    {
        var runner = CreateRunner();
        runner.RunAll();

        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='idx_prompt_tags_tag'";
        var exists = (long)cmd.ExecuteScalar()! > 0;
        Assert.True(exists);
    }

    // ── V005 tests ───────────────────────────────────────────────────

    [Fact]
    public void V005_CreatesSavedViews()
    {
        var runner = CreateRunner();
        runner.RunAll();

        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='saved_views'";
        var exists = (long)cmd.ExecuteScalar()! > 0;
        Assert.True(exists);
    }

    [Fact]
    public void V005_SavedViews_HasExpectedColumns()
    {
        var runner = CreateRunner();
        runner.RunAll();

        using var conn = _factory.CreateConnection();
        var columns = new List<string>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM pragma_table_info('saved_views')";
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) columns.Add(reader.GetString(0));

        Assert.Contains("id", columns);
        Assert.Contains("name", columns);
        Assert.Contains("query_json", columns);
        Assert.Contains("query_schema_version", columns);
        Assert.Contains("original_schema_version", columns);
        Assert.Contains("created_at", columns);
        Assert.Contains("sort_order", columns);
    }

    [Fact]
    public void V005_Idempotent_SecondRunNoOp()
    {
        var runner = CreateRunner();
        runner.RunAll();

        // Insert a saved view
        using var conn = _factory.CreateConnection();
        using var insertCmd = conn.CreateCommand();
        insertCmd.CommandText = "INSERT INTO saved_views (name, query_json) VALUES ('Test', '{\"FreeTextTerms\":[]}')";
        insertCmd.ExecuteNonQuery();

        // Second run should not fail
        var runner2 = CreateRunner();
        runner2.RunAll();

        using var countCmd = conn.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(*) FROM saved_views";
        var count = (long)countCmd.ExecuteScalar()!;
        Assert.Equal(1, count);
    }

    [Fact]
    public void V005_SavedViews_InsertWithoutCreatedAt_UsesDefault()
    {
        var runner = CreateRunner();
        runner.RunAll();

        using var conn = _factory.CreateConnection();
        using var insertCmd = conn.CreateCommand();
        insertCmd.CommandText = "INSERT INTO saved_views (name, query_json) VALUES ('Test', '{}')";
        insertCmd.ExecuteNonQuery();

        using var selectCmd = conn.CreateCommand();
        selectCmd.CommandText = "SELECT created_at FROM saved_views WHERE name = 'Test'";
        var createdAt = selectCmd.ExecuteScalar() as string;
        Assert.NotNull(createdAt);
        Assert.NotEmpty(createdAt!);
    }

    [Fact]
    public void V005_SavedViews_CrudRoundTrip()
    {
        var runner = CreateRunner();
        runner.RunAll();

        using var conn = _factory.CreateConnection();

        // Insert
        using var insertCmd = conn.CreateCommand();
        insertCmd.CommandText = """
            INSERT INTO saved_views (name, query_json, query_schema_version, original_schema_version, sort_order)
            VALUES ('My filter', '{"FreeTextTerms":["hello"]}', 1, NULL, 5);
        """;
        insertCmd.ExecuteNonQuery();

        // Read back
        using var selectCmd = conn.CreateCommand();
        selectCmd.CommandText = "SELECT name, query_json, query_schema_version, original_schema_version, sort_order FROM saved_views WHERE id = 1";
        using var reader = selectCmd.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal("My filter", reader.GetString(0));
        Assert.Equal("{\"FreeTextTerms\":[\"hello\"]}", reader.GetString(1));
        Assert.Equal(1, reader.GetInt32(2));
        Assert.True(reader.IsDBNull(3)); // original_schema_version is null
        Assert.Equal(5, reader.GetInt32(4));

        // Update
        using var updateCmd = conn.CreateCommand();
        updateCmd.CommandText = "UPDATE saved_views SET name = 'Updated' WHERE id = 1";
        updateCmd.ExecuteNonQuery();

        // Delete
        using var deleteCmd = conn.CreateCommand();
        deleteCmd.CommandText = "DELETE FROM saved_views WHERE id = 1";
        deleteCmd.ExecuteNonQuery();

        using var countCmd = conn.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(*) FROM saved_views";
        Assert.Equal(0L, (long)countCmd.ExecuteScalar()!);
    }

    // ── Migration registry tests ─────────────────────────────────────

    [Fact]
    public void MigrationRegistry_SchemaChangingEntries_RequireBackupFlag()
    {
        var runner = CreateRunner();
        // Access migrations via reflection to verify backup flags
        var field = typeof(MigrationRunner).GetField("_migrations",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(field);

        var migrations = (List<(string Version, MigrationEntry Entry)>)field!.GetValue(runner)!;

        // V003b, V004, V005 should all require backup
        var v003b = migrations.First(m => m.Version == "V003b");
        Assert.True(v003b.Entry.RequiresBackup);

        var v004 = migrations.First(m => m.Version == "V004");
        Assert.True(v004.Entry.RequiresBackup);

        var v005 = migrations.First(m => m.Version == "V005");
        Assert.True(v005.Entry.RequiresBackup);

        // V003 (indexes only) should NOT require backup
        var v003 = migrations.First(m => m.Version == "V003");
        Assert.False(v003.Entry.RequiresBackup);
    }

    [Fact]
    public void TryBackup_SkipsForInMemoryDb()
    {
        // In-memory factory — backup should be skipped (no exception)
        var runner = CreateRunner();
        Assert.True(_factory.IsSharedMemory);

        // RunAll should succeed without trying to backup
        runner.RunAll();
    }

    // ── Prompt tag sync tests ───────────────────────────────────────

    [Fact]
    public async Task CreateAsync_SyncsPromptTags()
    {
        var runner = CreateRunner();
        runner.RunAll();

        var repo = new SqlitePromptRepository(_factory);
        var prompt = new Prompt { Title = "Test", Body = "Body" };
        prompt.SetTags(["email", "Work", "personal"]);
        var id = await repo.CreateAsync(prompt);

        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT tag FROM prompt_tags WHERE prompt_id = @id ORDER BY tag";
        cmd.Parameters.AddWithValue("@id", id);
        var tags = new List<string>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) tags.Add(reader.GetString(0));

        Assert.Equal(3, tags.Count);
        Assert.Contains("email", tags);
        Assert.Contains("work", tags); // lowercased
        Assert.Contains("personal", tags);
    }

    [Fact]
    public async Task UpdateAsync_SyncsPromptTags()
    {
        var runner = CreateRunner();
        runner.RunAll();

        var repo = new SqlitePromptRepository(_factory);
        var prompt = new Prompt { Title = "Test", Body = "Body" };
        prompt.SetTags(["email", "work"]);
        var id = await repo.CreateAsync(prompt);

        var loaded = await repo.GetByIdAsync(id);
        Assert.NotNull(loaded);
        loaded!.SetTags(["code", "review"]);
        await repo.UpdateAsync(loaded);

        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT tag FROM prompt_tags WHERE prompt_id = @id ORDER BY tag";
        cmd.Parameters.AddWithValue("@id", id);
        var tags = new List<string>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) tags.Add(reader.GetString(0));

        Assert.Equal(2, tags.Count);
        Assert.Contains("code", tags);
        Assert.Contains("review", tags);
        Assert.DoesNotContain("email", tags); // old tags removed
    }

    [Fact]
    public async Task AdvancedSearch_UsesPromptTagsJoin()
    {
        var runner = CreateRunner();
        runner.RunAll();

        var repo = new SqlitePromptRepository(_factory);
        var p1 = new Prompt { Title = "Both tags", Body = "B" };
        p1.SetTags(["email", "important"]);
        await repo.CreateAsync(p1);

        var p2 = new Prompt { Title = "Only email", Body = "B" };
        p2.SetTags(["email"]);
        await repo.CreateAsync(p2);

        var query = new Domain.Models.SearchQuery { IncludeTags = ["email", "important"] };
        var results = await repo.SearchAsync(query);
        Assert.Single(results);
        Assert.Equal("Both tags", results[0].Title);
    }

    [Fact]
    public async Task GetAllTags_UsesPromptTagsTable()
    {
        var runner = CreateRunner();
        runner.RunAll();

        var repo = new SqlitePromptRepository(_factory);
        var p1 = new Prompt { Title = "A", Body = "B" };
        p1.SetTags(["email", "Work"]);
        await repo.CreateAsync(p1);

        var p2 = new Prompt { Title = "C", Body = "D" };
        p2.SetTags(["email", "Personal"]);
        await repo.CreateAsync(p2);

        var tags = await repo.GetAllTagsAsync();
        Assert.Equal(3, tags.Count);
        Assert.Contains("email", tags);
        Assert.Contains("work", tags); // lowercased
        Assert.Contains("personal", tags); // lowercased
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}
