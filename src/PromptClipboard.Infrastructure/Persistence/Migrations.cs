namespace PromptClipboard.Infrastructure.Persistence;

public static class Migrations
{
    public const string V001_InitialSchema = """
        CREATE TABLE prompts (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            title TEXT NOT NULL,
            body TEXT NOT NULL,
            tags_json TEXT NOT NULL DEFAULT '[]',
            tags_text TEXT NOT NULL DEFAULT '',
            folder TEXT NOT NULL DEFAULT '',
            created_at TEXT NOT NULL DEFAULT (datetime('now')),
            updated_at TEXT NOT NULL DEFAULT (datetime('now')),
            last_used_at TEXT,
            use_count INTEGER NOT NULL DEFAULT 0,
            is_pinned INTEGER NOT NULL DEFAULT 0,
            lang TEXT NOT NULL DEFAULT '',
            model_hint TEXT NOT NULL DEFAULT '',
            version_parent_id INTEGER REFERENCES prompts(id)
        );

        CREATE TABLE prompt_versions (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            prompt_id INTEGER NOT NULL REFERENCES prompts(id) ON DELETE CASCADE,
            body TEXT NOT NULL,
            created_at TEXT NOT NULL DEFAULT (datetime('now')),
            note TEXT NOT NULL DEFAULT ''
        );
    """;

    public const string V002_AddFts5Index = """
        CREATE VIRTUAL TABLE prompts_fts USING fts5(
            title,
            body,
            tags_text,
            content='prompts',
            content_rowid='id'
        );

        -- Populate FTS from existing data
        INSERT INTO prompts_fts(rowid, title, body, tags_text)
            SELECT id, title, body, tags_text FROM prompts;

        -- Sync triggers
        CREATE TRIGGER prompts_ai AFTER INSERT ON prompts BEGIN
            INSERT INTO prompts_fts(rowid, title, body, tags_text)
                VALUES (new.id, new.title, new.body, new.tags_text);
        END;

        CREATE TRIGGER prompts_ad AFTER DELETE ON prompts BEGIN
            INSERT INTO prompts_fts(prompts_fts, rowid, title, body, tags_text)
                VALUES ('delete', old.id, old.title, old.body, old.tags_text);
        END;

        CREATE TRIGGER prompts_au AFTER UPDATE ON prompts BEGIN
            INSERT INTO prompts_fts(prompts_fts, rowid, title, body, tags_text)
                VALUES ('delete', old.id, old.title, old.body, old.tags_text);
            INSERT INTO prompts_fts(rowid, title, body, tags_text)
                VALUES (new.id, new.title, new.body, new.tags_text);
        END;
    """;

    public const string V003_AddIndexes = """
        CREATE INDEX IF NOT EXISTS idx_prompts_pinned ON prompts(is_pinned);
        CREATE INDEX IF NOT EXISTS idx_prompts_last_used ON prompts(last_used_at DESC);
        CREATE INDEX IF NOT EXISTS idx_prompts_use_count ON prompts(use_count DESC);
        CREATE INDEX IF NOT EXISTS idx_prompts_folder ON prompts(folder) WHERE folder != '';
    """;

    /// <summary>
    /// V003b code migration: adds body_hash column, index, and backfills existing rows.
    /// Called from MigrationRunner via MigrationEntry.FromCode().
    /// </summary>
    public static void V003b_AddBodyHash(Microsoft.Data.Sqlite.SqliteConnection conn, Microsoft.Data.Sqlite.SqliteTransaction tx)
    {
        // DDL — idempotency guard: check if column already exists (crash-recovery safe)
        using var checkCmd = conn.CreateCommand();
        checkCmd.Transaction = tx;
        checkCmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('prompts') WHERE name='body_hash'";
        var exists = (long)checkCmd.ExecuteScalar()! > 0;

        if (!exists)
        {
            using var ddl = conn.CreateCommand();
            ddl.Transaction = tx;
            ddl.CommandText = "ALTER TABLE prompts ADD COLUMN body_hash TEXT";
            ddl.ExecuteNonQuery();
        }

        // Index — IF NOT EXISTS already idempotent
        using var idxCmd = conn.CreateCommand();
        idxCmd.Transaction = tx;
        idxCmd.CommandText = "CREATE INDEX IF NOT EXISTS idx_prompts_body_hash ON prompts(body_hash) WHERE body_hash IS NOT NULL";
        idxCmd.ExecuteNonQuery();

        // Backfill — batched, idempotent (WHERE body_hash IS NULL)
        V003b_BackfillBatched(conn, tx, batchSize: 200);
    }

    public const string V004_NormalizeTagsDdl = """
        CREATE TABLE IF NOT EXISTS prompt_tags (
            prompt_id INTEGER NOT NULL REFERENCES prompts(id) ON DELETE CASCADE,
            tag TEXT NOT NULL,
            PRIMARY KEY (prompt_id, tag)
        );
        CREATE INDEX IF NOT EXISTS idx_prompt_tags_tag ON prompt_tags(tag);
    """;

    /// <summary>
    /// V004 code migration: creates prompt_tags table, backfills from json_each(tags_json).
    /// Called from MigrationRunner via MigrationEntry.FromCode().
    /// </summary>
    public static void V004_NormalizeTags(Microsoft.Data.Sqlite.SqliteConnection conn, Microsoft.Data.Sqlite.SqliteTransaction tx)
    {
        // DDL — idempotent (IF NOT EXISTS)
        using var ddlCmd = conn.CreateCommand();
        ddlCmd.Transaction = tx;
        ddlCmd.CommandText = V004_NormalizeTagsDdl;
        ddlCmd.ExecuteNonQuery();

        // Log warning count for invalid JSON
        using var warnCmd = conn.CreateCommand();
        warnCmd.Transaction = tx;
        warnCmd.CommandText = "SELECT COUNT(*) FROM prompts WHERE NOT json_valid(tags_json)";
        var invalidCount = (long)warnCmd.ExecuteScalar()!;
        // Warning is logged by MigrationRunner if needed (we pass it via return/side-effect)
        // For now, rows with invalid JSON are simply skipped by the WHERE clause below.

        // Backfill — idempotent (INSERT OR IGNORE)
        using var backfillCmd = conn.CreateCommand();
        backfillCmd.Transaction = tx;
        backfillCmd.CommandText = """
            INSERT OR IGNORE INTO prompt_tags (prompt_id, tag)
                SELECT p.id, LOWER(TRIM(j.value))
                FROM prompts p, json_each(p.tags_json) j
                WHERE json_valid(p.tags_json) AND TRIM(j.value) != ''
        """;
        backfillCmd.ExecuteNonQuery();
    }

    public const string V005_CreateSavedViewsSql = """
        CREATE TABLE IF NOT EXISTS saved_views (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            name TEXT NOT NULL,
            query_json TEXT NOT NULL,
            query_schema_version INTEGER NOT NULL DEFAULT 1,
            original_schema_version INTEGER,
            created_at TEXT NOT NULL DEFAULT (datetime('now')),
            sort_order INTEGER NOT NULL DEFAULT 0
        )
    """;

    /// <summary>
    /// V005 code migration: creates saved_views table with idempotent column guards.
    /// </summary>
    public static void V005_CreateSavedViews(Microsoft.Data.Sqlite.SqliteConnection conn, Microsoft.Data.Sqlite.SqliteTransaction tx)
    {
        // CREATE TABLE IF NOT EXISTS — safe for first run
        using var createCmd = conn.CreateCommand();
        createCmd.Transaction = tx;
        createCmd.CommandText = V005_CreateSavedViewsSql;
        createCmd.ExecuteNonQuery();

        // Fail-fast for incompatible legacy schemas
        if (!HasColumn(conn, tx, "saved_views", "name") || !HasColumn(conn, tx, "saved_views", "query_json"))
            throw new IncompatibleSchemaException("saved_views schema is incompatible; manual migration required");

        // Guard: ensure additive required columns exist on pre-existing table
        EnsureColumnExists(conn, tx, "saved_views", "original_schema_version", "INTEGER");
        EnsureColumnExists(conn, tx, "saved_views", "query_schema_version", "INTEGER NOT NULL DEFAULT 1");
        EnsureColumnExists(conn, tx, "saved_views", "created_at", "TEXT");
        EnsureColumnExists(conn, tx, "saved_views", "sort_order", "INTEGER NOT NULL DEFAULT 0");

        // Backfill nullable created_at introduced by ALTER path
        using var fixCreatedAt = conn.CreateCommand();
        fixCreatedAt.Transaction = tx;
        fixCreatedAt.CommandText = "UPDATE saved_views SET created_at = datetime('now') WHERE created_at IS NULL";
        fixCreatedAt.ExecuteNonQuery();
    }

    internal static bool HasColumn(Microsoft.Data.Sqlite.SqliteConnection conn, Microsoft.Data.Sqlite.SqliteTransaction tx, string table, string column)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name='{column}'";
        return (long)cmd.ExecuteScalar()! > 0;
    }

    internal static void EnsureColumnExists(Microsoft.Data.Sqlite.SqliteConnection conn, Microsoft.Data.Sqlite.SqliteTransaction tx, string table, string column, string ddlType)
    {
        if (HasColumn(conn, tx, table, column)) return;

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {ddlType}";
        cmd.ExecuteNonQuery();
    }

    internal static void V003b_BackfillBatched(Microsoft.Data.Sqlite.SqliteConnection conn, Microsoft.Data.Sqlite.SqliteTransaction tx, int batchSize)
    {
        while (true)
        {
            // Read a batch of rows without hash
            using var selectCmd = conn.CreateCommand();
            selectCmd.Transaction = tx;
            selectCmd.CommandText = "SELECT id, body FROM prompts WHERE body_hash IS NULL LIMIT @batchSize";
            selectCmd.Parameters.AddWithValue("@batchSize", batchSize);

            var batch = new List<(long Id, string Body)>();
            using (var reader = selectCmd.ExecuteReader())
            {
                while (reader.Read())
                    batch.Add((reader.GetInt64(0), reader.GetString(1)));
            }

            if (batch.Count == 0) break;

            foreach (var (id, body) in batch)
            {
                var hash = BodyHasher.ComputeHash(body);
                using var updateCmd = conn.CreateCommand();
                updateCmd.Transaction = tx;
                updateCmd.CommandText = "UPDATE prompts SET body_hash = @hash WHERE id = @id";
                updateCmd.Parameters.AddWithValue("@hash", hash);
                updateCmd.Parameters.AddWithValue("@id", id);
                updateCmd.ExecuteNonQuery();
            }
        }
    }
}
