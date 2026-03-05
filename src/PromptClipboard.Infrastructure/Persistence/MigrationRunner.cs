namespace PromptClipboard.Infrastructure.Persistence;

using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Serilog;

public sealed class MigrationRunner
{
    private readonly SqliteConnectionFactory _factory;
    private readonly ILogger _log;
    private readonly List<(string Version, MigrationEntry Entry)> _migrations = [];

    public MigrationRunner(SqliteConnectionFactory factory, ILogger log)
    {
        _factory = factory;
        _log = log;
        RegisterMigrations();
    }

    private void RegisterMigrations()
    {
        _migrations.Add(("V001", MigrationEntry.FromSql(Migrations.V001_InitialSchema)));
        _migrations.Add(("V002", MigrationEntry.FromSql(Migrations.V002_AddFts5Index)));
        _migrations.Add(("V003", MigrationEntry.FromSql(Migrations.V003_AddIndexes)));
        _migrations.Add(("V003b", MigrationEntry.FromCode(Migrations.V003b_AddBodyHash, requiresBackup: true)));
        _migrations.Add(("V004", MigrationEntry.FromCode(Migrations.V004_NormalizeTags, requiresBackup: true)));
        _migrations.Add(("V005", MigrationEntry.FromCode(Migrations.V005_CreateSavedViews, requiresBackup: true)));
    }

    public void RunAll()
    {
        using var conn = _factory.CreateConnection();

        using var createCmd = conn.CreateCommand();
        createCmd.CommandText = """
            CREATE TABLE IF NOT EXISTS schema_migrations (
                version TEXT PRIMARY KEY,
                applied_at TEXT NOT NULL DEFAULT (datetime('now'))
            );
        """;
        createCmd.ExecuteNonQuery();

        // Collect pending migrations
        var pending = new List<(string Version, MigrationEntry Entry)>();
        foreach (var (version, entry) in _migrations)
        {
            using var checkCmd = conn.CreateCommand();
            checkCmd.CommandText = "SELECT COUNT(1) FROM schema_migrations WHERE version = @v";
            checkCmd.Parameters.AddWithValue("@v", version);
            var applied = (long)(checkCmd.ExecuteScalar() ?? 0) > 0;

            if (applied)
            {
                _log.Debug("Migration {Version} already applied", version);
                continue;
            }

            pending.Add((version, entry));
        }

        if (pending.Count == 0) return;

        // Pre-migration backup if any pending migration requires it
        if (pending.Any(m => m.Entry.RequiresBackup))
        {
            EnsureBackup(conn);
        }

        // Apply pending migrations
        foreach (var (version, entry) in pending)
        {
            _log.Information("Applying migration {Version}...", version);
            using var tx = conn.BeginTransaction();
            try
            {
                if (entry.Sql != null)
                {
                    using var migCmd = conn.CreateCommand();
                    migCmd.Transaction = tx;
                    migCmd.CommandText = entry.Sql;
                    migCmd.ExecuteNonQuery();
                }
                else if (entry.Code != null)
                {
                    entry.Code(conn, tx);
                }

                using var insertCmd = conn.CreateCommand();
                insertCmd.Transaction = tx;
                insertCmd.CommandText = "INSERT INTO schema_migrations (version) VALUES (@v)";
                insertCmd.Parameters.AddWithValue("@v", version);
                insertCmd.ExecuteNonQuery();

                tx.Commit();
                _log.Information("Migration {Version} applied successfully", version);
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }
    }

    private void EnsureBackup(SqliteConnection conn)
    {
        if (_factory.IsSharedMemory || _factory.DbPath == null)
        {
            _log.Debug("Skipping backup for in-memory/shared database");
            return;
        }

#if DEBUG
        if (Environment.GetEnvironmentVariable("PROMPTCLIPBOARD_SKIP_BACKUP") == "1")
        {
            _log.Warning("Backup skipped via PROMPTCLIPBOARD_SKIP_BACKUP (DEBUG build)");
            return;
        }
#endif

        var dbPath = _factory.DbPath;

        try
        {
            // WAL checkpoint
            WalCheckpoint(conn);

            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
            var dbFileName = Path.GetFileName(dbPath);
            var dir = Path.GetDirectoryName(dbPath)!;

            // Copy main DB file
            var backupPath = Path.Combine(dir, $"{dbFileName}.bak.{timestamp}");
            File.Copy(dbPath, backupPath, overwrite: false);

            // Copy WAL file if it exists
            var walPath = dbPath + "-wal";
            if (File.Exists(walPath))
            {
                var walBackupPath = Path.Combine(dir, $"{dbFileName}-wal.bak.{timestamp}");
                File.Copy(walPath, walBackupPath, overwrite: false);
            }

            _log.Information("Pre-migration backup created: {BackupPath}", backupPath);

            // Rotation: keep only 3 most recent backup sets
            RotateBackups(dir, dbFileName, keepCount: 3);
        }
        catch (Exception ex)
        {
            throw new BackupFailedException(ex);
        }
    }

    private void WalCheckpoint(SqliteConnection conn)
    {
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)";
            cmd.ExecuteNonQuery();
        }
        catch (SqliteException)
        {
            // Retry once after 500ms
            _log.Debug("WAL checkpoint busy, retrying in 500ms");
            Thread.Sleep(500);
            using var retryCmd = conn.CreateCommand();
            retryCmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)";
            retryCmd.ExecuteNonQuery();
        }
    }

    private void RotateBackups(string dir, string dbFileName, int keepCount)
    {
        try
        {
            var pattern = new Regex($@"\.bak\.(\d{{8}}_\d{{6}}_\d{{3}})$", RegexOptions.Compiled);
            var files = Directory.GetFiles(dir, $"{dbFileName}.bak.*");

            var timestamps = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var file in files)
            {
                var match = pattern.Match(file);
                if (!match.Success) continue;
                var ts = match.Groups[1].Value;
                if (DateTime.TryParseExact(ts, "yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                    timestamps.Add(ts);
            }

            if (timestamps.Count <= keepCount) return;

            var toDelete = timestamps.Take(timestamps.Count - keepCount).ToList();
            foreach (var ts in toDelete)
            {
                var mainBackup = Path.Combine(dir, $"{dbFileName}.bak.{ts}");
                var walBackup = Path.Combine(dir, $"{dbFileName}-wal.bak.{ts}");
                if (File.Exists(mainBackup)) File.Delete(mainBackup);
                if (File.Exists(walBackup)) File.Delete(walBackup);
                _log.Debug("Rotated old backup: {Timestamp}", ts);
            }
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Backup rotation failed (non-fatal)");
        }
    }
}

public sealed record MigrationEntry(
    string? Sql,
    Action<SqliteConnection, SqliteTransaction>? Code,
    bool RequiresBackup = false)
{
    public static MigrationEntry FromSql(string sql, bool requiresBackup = false)
        => new(sql, null, requiresBackup);

    public static MigrationEntry FromCode(Action<SqliteConnection, SqliteTransaction> code, bool requiresBackup = false)
        => new(null, code, requiresBackup);
}
