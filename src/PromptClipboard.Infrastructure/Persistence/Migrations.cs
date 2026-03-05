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
