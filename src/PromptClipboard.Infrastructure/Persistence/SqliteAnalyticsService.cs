namespace PromptClipboard.Infrastructure.Persistence;

using System.IO;
using Microsoft.Data.Sqlite;
using PromptClipboard.Domain.Interfaces;
using Serilog;

public sealed class SqliteAnalyticsService : IAnalyticsService, IDisposable
{
    private readonly string _dbPath;
    private readonly ILogger _log;
    private bool _initialized;

    public SqliteAnalyticsService(string appDataPath, ILogger log)
    {
        _dbPath = Path.Combine(appDataPath, "analytics.db");
        _log = log;
    }

    private SqliteConnection CreateConnection()
    {
        var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        using var walCmd = conn.CreateCommand();
        walCmd.CommandText = "PRAGMA journal_mode=WAL;";
        walCmd.ExecuteNonQuery();
        return conn;
    }

    private void EnsureInitialized()
    {
        if (_initialized) return;

        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);

        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS usage_stats (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                event_type TEXT NOT NULL,
                prompt_id INTEGER,
                metadata_json TEXT,
                created_at TEXT NOT NULL DEFAULT (datetime('now'))
            );
            CREATE INDEX IF NOT EXISTS idx_stats_created ON usage_stats(created_at);
            CREATE INDEX IF NOT EXISTS idx_stats_event ON usage_stats(event_type);
        """;
        cmd.ExecuteNonQuery();
        _initialized = true;
    }

    public async Task RecordEventAsync(string eventType, long? promptId = null, string? metadataJson = null, CancellationToken ct = default)
    {
        EnsureInitialized();
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO usage_stats (event_type, prompt_id, metadata_json)
            VALUES (@type, @promptId, @metadata)
        """;
        cmd.Parameters.AddWithValue("@type", eventType);
        cmd.Parameters.AddWithValue("@promptId", promptId ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@metadata", metadataJson ?? (object)DBNull.Value);
        await Task.Run(() => cmd.ExecuteNonQuery(), ct);
    }

    public async Task CleanupAsync(int retentionDays, int maxRows, CancellationToken ct = default)
    {
        EnsureInitialized();
        using var conn = CreateConnection();

        // 1. TTL cleanup
        using var ttlCmd = conn.CreateCommand();
        ttlCmd.CommandText = "DELETE FROM usage_stats WHERE created_at < datetime('now', '-' || @days || ' days')";
        ttlCmd.Parameters.AddWithValue("@days", retentionDays);
        await Task.Run(() => ttlCmd.ExecuteNonQuery(), ct);

        // 2. Row cap cleanup
        using var countCmd = conn.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(*) FROM usage_stats";
        var count = (long)await Task.Run(() => countCmd.ExecuteScalar()!, ct);

        if (count > maxRows)
        {
            using var capCmd = conn.CreateCommand();
            capCmd.CommandText = """
                DELETE FROM usage_stats
                WHERE rowid IN (
                    SELECT rowid FROM usage_stats
                    ORDER BY created_at ASC, rowid ASC
                    LIMIT (SELECT COUNT(*) - @max FROM usage_stats)
                )
            """;
            capCmd.Parameters.AddWithValue("@max", maxRows);
            await Task.Run(() => capCmd.ExecuteNonQuery(), ct);
        }

        _log.Debug("Analytics cleanup: retention={Days}d, maxRows={Max}, rowsBefore={Count}", retentionDays, maxRows, count);
    }

    public async Task ClearAllAsync(CancellationToken ct = default)
    {
        EnsureInitialized();
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM usage_stats";
        await Task.Run(() => cmd.ExecuteNonQuery(), ct);
        _log.Information("Analytics data cleared");
    }

    public void Dispose()
    {
        // No persistent connection to dispose
    }
}
