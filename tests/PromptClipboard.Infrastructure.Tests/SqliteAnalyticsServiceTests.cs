namespace PromptClipboard.Infrastructure.Tests;

using System.IO;
using PromptClipboard.Infrastructure.Persistence;
using Serilog;

public class SqliteAnalyticsServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SqliteAnalyticsService _service;

    public SqliteAnalyticsServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"analytics_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        var log = new LoggerConfiguration().CreateLogger();
        _service = new SqliteAnalyticsService(_tempDir, log);
    }

    [Fact]
    public async Task RecordEvent_CreatesEntry()
    {
        await _service.RecordEventAsync("paste", promptId: 42);

        // Verify by reading directly
        var dbPath = Path.Combine(_tempDir, "analytics.db");
        Assert.True(File.Exists(dbPath));

        using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM usage_stats WHERE event_type = 'paste'";
        var count = (long)cmd.ExecuteScalar()!;
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task RecordEvent_WithMetadata()
    {
        await _service.RecordEventAsync("search", metadataJson: "{\"query\":\"hello\"}");

        var dbPath = Path.Combine(_tempDir, "analytics.db");
        using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT metadata_json FROM usage_stats WHERE event_type = 'search'";
        var metadata = cmd.ExecuteScalar() as string;
        Assert.Equal("{\"query\":\"hello\"}", metadata);
    }

    [Fact]
    public async Task ClearAll_RemovesAllEntries()
    {
        await _service.RecordEventAsync("paste");
        await _service.RecordEventAsync("search");
        await _service.RecordEventAsync("create");

        await _service.ClearAllAsync();

        var dbPath = Path.Combine(_tempDir, "analytics.db");
        using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM usage_stats";
        var count = (long)cmd.ExecuteScalar()!;
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task Cleanup_RowCap_RemovesOldest()
    {
        // Insert 5 events
        for (int i = 0; i < 5; i++)
            await _service.RecordEventAsync($"event_{i}");

        // Cleanup with maxRows=3
        await _service.CleanupAsync(retentionDays: 365, maxRows: 3);

        var dbPath = Path.Combine(_tempDir, "analytics.db");
        using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM usage_stats";
        var count = (long)cmd.ExecuteScalar()!;
        Assert.Equal(3, count);

        // Verify the newest 3 remain
        using var selectCmd = conn.CreateCommand();
        selectCmd.CommandText = "SELECT event_type FROM usage_stats ORDER BY rowid ASC";
        var remaining = new List<string>();
        using var reader = selectCmd.ExecuteReader();
        while (reader.Read()) remaining.Add(reader.GetString(0));

        Assert.Contains("event_2", remaining);
        Assert.Contains("event_3", remaining);
        Assert.Contains("event_4", remaining);
    }

    [Fact]
    public async Task Cleanup_BelowCap_NoOp()
    {
        await _service.RecordEventAsync("paste");
        await _service.RecordEventAsync("search");

        await _service.CleanupAsync(retentionDays: 365, maxRows: 100);

        var dbPath = Path.Combine(_tempDir, "analytics.db");
        using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM usage_stats";
        var count = (long)cmd.ExecuteScalar()!;
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task RecordEvent_NullPromptId_StoredAsNull()
    {
        await _service.RecordEventAsync("search");

        var dbPath = Path.Combine(_tempDir, "analytics.db");
        using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT prompt_id FROM usage_stats LIMIT 1";
        var result = cmd.ExecuteScalar();
        Assert.Equal(DBNull.Value, result);
    }

    public void Dispose()
    {
        _service.Dispose();
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* cleanup best-effort */ }
    }
}
