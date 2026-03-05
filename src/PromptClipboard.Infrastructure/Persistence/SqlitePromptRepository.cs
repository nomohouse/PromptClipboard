namespace PromptClipboard.Infrastructure.Persistence;

using Microsoft.Data.Sqlite;
using PromptClipboard.Domain;
using PromptClipboard.Domain.Entities;
using PromptClipboard.Domain.Interfaces;
using PromptClipboard.Domain.Models;

public sealed class SqlitePromptRepository : IPromptRepository, IAdvancedSearchRepository, ITagSuggestionRepository, IDuplicateDetectionRepository
{
    private readonly SqliteConnectionFactory _factory;

    public SqlitePromptRepository(SqliteConnectionFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// Returns up to SearchDefaults.MaxResults + 1 items; caller uses extra item to detect HasMore.
    /// </summary>
    public async Task<List<Prompt>> SearchAsync(string query, string? tagFilter = null, string? langFilter = null, CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();

        var hasQuery = !string.IsNullOrWhiteSpace(query);
        var hasTag = !string.IsNullOrWhiteSpace(tagFilter);
        var hasLang = !string.IsNullOrWhiteSpace(langFilter);

        string sql;
        if (hasQuery)
        {
            sql = """
                SELECT p.* FROM prompts p
                INNER JOIN prompts_fts ON prompts_fts.rowid = p.id
                WHERE prompts_fts MATCH @query
            """;
            if (hasTag)
                sql += " AND EXISTS (SELECT 1 FROM json_each(p.tags_json) WHERE LOWER(json_each.value) = LOWER(@tag))";
            if (hasLang)
                sql += " AND p.lang = @lang";
            sql += " ORDER BY bm25(prompts_fts, 10.0, 5.0, 1.0), p.is_pinned DESC, p.use_count DESC LIMIT @limit";
        }
        else
        {
            sql = "SELECT p.* FROM prompts p WHERE 1=1";
            if (hasTag)
                sql += " AND EXISTS (SELECT 1 FROM json_each(p.tags_json) WHERE LOWER(json_each.value) = LOWER(@tag))";
            if (hasLang)
                sql += " AND p.lang = @lang";
            sql += " ORDER BY p.is_pinned DESC, COALESCE(p.last_used_at, p.created_at) DESC, p.use_count DESC LIMIT @limit";
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@limit", SearchDefaults.MaxResults + 1);
        if (hasQuery) cmd.Parameters.AddWithValue("@query", SanitizeFtsQuery(query));
        if (hasTag) cmd.Parameters.AddWithValue("@tag", tagFilter!.ToLowerInvariant());
        if (hasLang) cmd.Parameters.AddWithValue("@lang", langFilter!);

        return await ReadPromptsAsync(cmd, ct);
    }

    public async Task<List<Prompt>> GetPinnedAsync(CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM prompts WHERE is_pinned = 1 ORDER BY title";
        return await ReadPromptsAsync(cmd, ct);
    }

    /// <summary>
    /// Returns pinned prompts sorted by newest-first, with internal LIMIT (limit+1) sentinel for overflow detection.
    /// </summary>
    public async Task<List<Prompt>> GetPinnedAsync(int limit, CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM prompts WHERE is_pinned = 1 ORDER BY COALESCE(last_used_at, created_at) DESC, id DESC LIMIT @limit";
        cmd.Parameters.AddWithValue("@limit", limit + 1);
        return await ReadPromptsAsync(cmd, ct);
    }

    /// <summary>
    /// Strict recent: only prompts with non-null last_used_at, ordered by last_used_at DESC.
    /// </summary>
    public async Task<List<Prompt>> GetRecentAsync(int limit = SearchDefaults.RecentSliceLimit, CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM prompts WHERE last_used_at IS NOT NULL ORDER BY last_used_at DESC LIMIT @limit";
        cmd.Parameters.AddWithValue("@limit", limit);
        return await ReadPromptsAsync(cmd, ct);
    }

    public async Task<Prompt?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM prompts WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        var list = await ReadPromptsAsync(cmd, ct);
        return list.FirstOrDefault();
    }

    public async Task<long> CreateAsync(Prompt prompt, CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO prompts (title, body, tags_json, tags_text, folder, created_at, updated_at, last_used_at, use_count, is_pinned, lang, model_hint, version_parent_id, body_hash)
            VALUES (@title, @body, @tags_json, @tags_text, @folder, @created_at, @updated_at, @last_used_at, @use_count, @is_pinned, @lang, @model_hint, @version_parent_id, @body_hash);
            SELECT last_insert_rowid();
        """;
        AddPromptParams(cmd, prompt);
        var result = await Task.Run(() => cmd.ExecuteScalar(), ct);
        return (long)(result ?? 0);
    }

    public async Task UpdateAsync(Prompt prompt, CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE prompts SET
                title = @title, body = @body, tags_json = @tags_json, tags_text = @tags_text,
                folder = @folder, updated_at = @updated_at, last_used_at = @last_used_at,
                use_count = @use_count, is_pinned = @is_pinned, lang = @lang,
                model_hint = @model_hint, version_parent_id = @version_parent_id,
                body_hash = @body_hash
            WHERE id = @id
        """;
        cmd.Parameters.AddWithValue("@id", prompt.Id);
        AddPromptParams(cmd, prompt);
        await Task.Run(() => cmd.ExecuteNonQuery(), ct);
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM prompts WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        await Task.Run(() => cmd.ExecuteNonQuery(), ct);
    }

    public async Task MarkUsedAsync(long id, DateTime usedAt, CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE prompts SET
                last_used_at = @used_at,
                use_count = use_count + 1,
                updated_at = @used_at
            WHERE id = @id
        """;
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@used_at", usedAt.ToString("yyyy-MM-dd HH:mm:ss"));
        await Task.Run(() => cmd.ExecuteNonQuery(), ct);
    }

    public async Task<List<Prompt>> GetAllAsync(CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM prompts ORDER BY title";
        return await ReadPromptsAsync(cmd, ct);
    }

    public async Task<int> GetCountAsync(CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM prompts";
        var result = await Task.Run(() => cmd.ExecuteScalar(), ct);
        return Convert.ToInt32(result);
    }

    private static void AddPromptParams(SqliteCommand cmd, Prompt p)
    {
        cmd.Parameters.AddWithValue("@title", p.Title);
        cmd.Parameters.AddWithValue("@body", p.Body);
        cmd.Parameters.AddWithValue("@tags_json", p.TagsJson);
        cmd.Parameters.AddWithValue("@tags_text", p.TagsText);
        cmd.Parameters.AddWithValue("@folder", p.Folder);
        cmd.Parameters.AddWithValue("@created_at", p.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.Parameters.AddWithValue("@updated_at", p.UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.Parameters.AddWithValue("@last_used_at", p.LastUsedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@use_count", p.UseCount);
        cmd.Parameters.AddWithValue("@is_pinned", p.IsPinned ? 1 : 0);
        cmd.Parameters.AddWithValue("@lang", p.Lang);
        cmd.Parameters.AddWithValue("@model_hint", p.ModelHint);
        cmd.Parameters.AddWithValue("@version_parent_id", p.VersionParentId ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@body_hash", (object?)BodyHasher.ComputeHash(p.Body) ?? DBNull.Value);
    }

    private static async Task<List<Prompt>> ReadPromptsAsync(SqliteCommand cmd, CancellationToken ct)
    {
        var prompts = new List<Prompt>();
        await Task.Run(() =>
        {
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                prompts.Add(MapPrompt(reader));
            }
        }, ct);
        return prompts;
    }

    /// <summary>
    /// Advanced search using SearchQuery AST. Returns up to MaxResults+1 for HasMore detection.
    /// </summary>
    public async Task<List<Prompt>> SearchAsync(SearchQuery query, CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();

        var ftsMatch = FtsQueryBuilder.Build(query);
        var useFts = ftsMatch != null;

        var sql = new System.Text.StringBuilder();
        var parameters = new List<(string Name, object Value)>();

        if (useFts)
        {
            sql.Append("SELECT p.* FROM prompts p INNER JOIN prompts_fts ON prompts_fts.rowid = p.id WHERE prompts_fts MATCH @ftsQuery");
            parameters.Add(("@ftsQuery", ftsMatch!));
        }
        else
        {
            sql.Append("SELECT p.* FROM prompts p WHERE 1=1");
        }

        // Negative-only fallback: NOT LIKE predicates
        if (!useFts && query.ExcludeWords.Count > 0)
        {
            for (var i = 0; i < query.ExcludeWords.Count; i++)
            {
                var paramName = $"@excWord{i}";
                sql.Append($" AND LOWER(p.title) NOT LIKE '%' || LOWER({paramName}) || '%' ESCAPE '\\'");
                sql.Append($" AND LOWER(p.body) NOT LIKE '%' || LOWER({paramName}) || '%' ESCAPE '\\'");
                parameters.Add((paramName, EscapeLike(query.ExcludeWords[i])));
            }
        }

        // Include tags (multi-tag AND)
        var includeTags = query.IncludeTags
            .Select(t => t.Trim().ToLowerInvariant())
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (includeTags.Count > 0)
        {
            sql.Append(" AND (SELECT COUNT(DISTINCT LOWER(TRIM(j.value))) FROM json_each(p.tags_json) j");
            sql.Append(" WHERE json_valid(p.tags_json) AND TRIM(j.value) != '' AND LOWER(TRIM(j.value)) IN (");
            var incParams = includeTags.Select((tag, i) => (Name: $"@incTag{i}", Value: (object)tag)).ToList();
            sql.Append(string.Join(", ", incParams.Select(x => x.Name)));
            sql.Append(")) = @includeTagCount");
            parameters.Add(("@includeTagCount", includeTags.Count));
            foreach (var p in incParams) parameters.Add(p);
        }

        // Exclude tags
        var excludeTags = query.ExcludeTags
            .Select(t => t.Trim().ToLowerInvariant())
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (excludeTags.Count > 0)
        {
            sql.Append(" AND NOT EXISTS (SELECT 1 FROM json_each(p.tags_json) j");
            sql.Append(" WHERE json_valid(p.tags_json) AND TRIM(j.value) != '' AND LOWER(TRIM(j.value)) IN (");
            var excParams = excludeTags.Select((tag, i) => (Name: $"@excTag{i}", Value: (object)tag)).ToList();
            sql.Append(string.Join(", ", excParams.Select(x => x.Name)));
            sql.Append("))");
            foreach (var p in excParams) parameters.Add(p);
        }

        // Folder filter
        if (!string.IsNullOrWhiteSpace(query.FolderFilter))
        {
            sql.Append(" AND p.folder = @folder");
            parameters.Add(("@folder", query.FolderFilter));
        }

        // Lang filter
        if (!string.IsNullOrWhiteSpace(query.LangFilter))
        {
            sql.Append(" AND p.lang = @lang");
            parameters.Add(("@lang", query.LangFilter));
        }

        // Pinned filter
        if (query.PinnedFilter == true)
        {
            sql.Append(" AND p.is_pinned = 1");
        }

        // Template filter
        if (query.HasTemplate == true)
        {
            sql.Append(" AND p.body LIKE '%{{%'");
        }

        // Recent filter
        if (query.RecentLimit.HasValue)
        {
            sql.Append(" AND p.last_used_at IS NOT NULL");
        }

        // Sort
        sql.Append(" ORDER BY ");
        switch (query.Sort)
        {
            case SortMode.Recent:
                sql.Append("p.last_used_at DESC, p.id DESC");
                break;
            case SortMode.MostUsed:
                sql.Append("p.use_count DESC, p.last_used_at DESC, p.id DESC");
                break;
            case SortMode.PinnedFirst:
                sql.Append("p.is_pinned DESC, COALESCE(p.last_used_at, p.created_at) DESC, p.id DESC");
                break;
            default: // Relevance
                if (useFts)
                    sql.Append("bm25(prompts_fts, 10.0, 5.0, 1.0) ASC, p.is_pinned DESC, p.use_count DESC, p.id DESC");
                else
                    sql.Append("p.is_pinned DESC, COALESCE(p.last_used_at, p.created_at) DESC, p.use_count DESC, p.id DESC");
                break;
        }

        // Limit
        var limit = query.RecentLimit ?? (SearchDefaults.MaxResults + 1);
        sql.Append(" LIMIT @limit");
        parameters.Add(("@limit", limit));

        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql.ToString();
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);

        return await ReadPromptsAsync(cmd, ct);
    }

    public async Task<List<string>> GetAllTagsAsync(CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT DISTINCT LOWER(TRIM(j.value))
            FROM prompts p, json_each(p.tags_json) j
            WHERE json_valid(p.tags_json) AND TRIM(j.value) != ''
            ORDER BY 1
        """;

        var tags = new List<string>();
        await Task.Run(() =>
        {
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                tags.Add(reader.GetString(0));
        }, ct);
        return tags;
    }

    public async Task<List<Prompt>> FindCandidatesAsync(string title, string body, int limit = 10, CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();

        // First check exact body_hash match
        var bodyHash = BodyHasher.ComputeHash(body);
        using var hashCmd = conn.CreateCommand();
        hashCmd.CommandText = "SELECT * FROM prompts WHERE body_hash = @hash LIMIT @limit";
        hashCmd.Parameters.AddWithValue("@hash", bodyHash);
        hashCmd.Parameters.AddWithValue("@limit", limit);
        var exactMatches = await ReadPromptsAsync(hashCmd, ct);
        if (exactMatches.Count > 0)
            return exactMatches;

        // FTS candidates from title + body
        var searchTerms = (title + " " + body).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (searchTerms.Length == 0)
            return [];

        var ftsQuery = string.Join(" OR ",
            searchTerms.Take(5).Select(t => "\"" + t.Replace("\"", "\"\"") + "\""));

        using var ftsCmd = conn.CreateCommand();
        ftsCmd.CommandText = """
            SELECT p.* FROM prompts p
            INNER JOIN prompts_fts ON prompts_fts.rowid = p.id
            WHERE prompts_fts MATCH @query
            ORDER BY bm25(prompts_fts, 10.0, 5.0, 1.0)
            LIMIT @limit
        """;
        ftsCmd.Parameters.AddWithValue("@query", ftsQuery);
        ftsCmd.Parameters.AddWithValue("@limit", limit);
        return await ReadPromptsAsync(ftsCmd, ct);
    }

    private static string EscapeLike(string word)
        => word.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private static string SanitizeFtsQuery(string query)
    {
        var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", tokens.Select(t => "\"" + t.Replace("\"", "\"\"") + "\""));
    }

    private static Prompt MapPrompt(SqliteDataReader r)
    {
        var prompt = new Prompt
        {
            Id = r.GetInt64(r.GetOrdinal("id")),
            Title = r.GetString(r.GetOrdinal("title")),
            Body = r.GetString(r.GetOrdinal("body")),
            TagsJson = r.GetString(r.GetOrdinal("tags_json")),
            TagsText = r.GetString(r.GetOrdinal("tags_text")),
            Folder = r.GetString(r.GetOrdinal("folder")),
            CreatedAt = DateTime.Parse(r.GetString(r.GetOrdinal("created_at"))),
            UpdatedAt = DateTime.Parse(r.GetString(r.GetOrdinal("updated_at"))),
            LastUsedAt = r.IsDBNull(r.GetOrdinal("last_used_at")) ? null : DateTime.Parse(r.GetString(r.GetOrdinal("last_used_at"))),
            UseCount = r.GetInt32(r.GetOrdinal("use_count")),
            IsPinned = r.GetInt32(r.GetOrdinal("is_pinned")) == 1,
            Lang = r.GetString(r.GetOrdinal("lang")),
            ModelHint = r.GetString(r.GetOrdinal("model_hint")),
            VersionParentId = r.IsDBNull(r.GetOrdinal("version_parent_id")) ? null : r.GetInt64(r.GetOrdinal("version_parent_id"))
        };

        // body_hash column may not exist on pre-V003b databases
        try
        {
            var ordinal = r.GetOrdinal("body_hash");
            prompt.BodyHash = r.IsDBNull(ordinal) ? null : r.GetString(ordinal);
        }
        catch (ArgumentOutOfRangeException) { }

        return prompt;
    }
}
