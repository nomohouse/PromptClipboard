namespace PromptClipboard.Application.Services;

using PromptClipboard.Application.Models;
using PromptClipboard.Domain;
using PromptClipboard.Domain.Entities;
using PromptClipboard.Domain.Interfaces;
using PromptClipboard.Domain.Models;
public sealed class SearchRankingService
{
    private readonly IPromptRepository _repository;
    private readonly IAdvancedSearchRepository? _advancedRepo;

    public SearchRankingService(IPromptRepository repository, IAdvancedSearchRepository? advancedRepo = null)
    {
        _repository = repository;
        _advancedRepo = advancedRepo;
    }

    public Task<SearchResult> SearchAsync(string rawQuery, CancellationToken ct = default)
    {
        if (_advancedRepo != null)
        {
            var parsed = SearchQueryParser.Parse(rawQuery);
            return SearchAsync(parsed, ct);
        }
        return SearchLegacyAsync(rawQuery, ct);
    }

    public Task<SearchResult> SearchAsync(SearchQuery query, CancellationToken ct = default)
    {
        if (IsDefaultListQuery(query))
            return GetDefaultListAsync(ct);
        return SearchAdvancedAsync(query, ct);
    }

    private static bool IsDefaultListQuery(SearchQuery q)
        => q.FreeTextTerms.Count == 0
           && q.IncludeTags.Count == 0
           && q.ExcludeTags.Count == 0
           && q.ExcludeWords.Count == 0
           && string.IsNullOrWhiteSpace(q.FolderFilter)
           && string.IsNullOrWhiteSpace(q.LangFilter)
           && q.PinnedFilter is not true
           && q.HasTemplate is not true
           && q.RecentLimit is null
           && q.Sort == SortMode.Relevance;

    private async Task<SearchResult> SearchAdvancedAsync(SearchQuery query, CancellationToken ct)
    {
        var results = await _advancedRepo!.SearchAsync(query, ct);

        var hasMore = results.Count > SearchDefaults.MaxResults;
        if (hasMore)
            results = results.Take(SearchDefaults.MaxResults).ToList();

        return new SearchResult(results, hasMore, query.IsTruncated);
    }

    private async Task<SearchResult> SearchLegacyAsync(string rawQuery, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawQuery))
        {
            return await GetDefaultListAsync(ct);
        }

        var parsed = SearchQueryParser.Parse(rawQuery);
        var query = string.Join(" ", parsed.FreeTextTerms);
        var tagFilter = parsed.IncludeTags.Count > 0 ? parsed.IncludeTags[0] : null;
        var langFilter = parsed.LangFilter;

        List<Prompt> results;
        if (string.IsNullOrWhiteSpace(query) && tagFilter != null)
        {
            results = await _repository.SearchAsync("", tagFilter, langFilter, ct);
        }
        else
        {
            results = await _repository.SearchAsync(query, tagFilter, langFilter, ct);
        }

        // LIMIT+1 strategy: repo returns up to MaxResults+1, we detect HasMore and trim
        var hasMore = results.Count > SearchDefaults.MaxResults;
        if (hasMore)
            results = results.Take(SearchDefaults.MaxResults).ToList();

        return new SearchResult(results, hasMore, parsed.IsTruncated);
    }

    /// <summary>
    /// Canonical default empty-query pipeline:
    /// 1. GetPinnedAsync(MaxResults) — newest-pinned first, with overflow detection
    /// 2. GetRecentAsync(RecentSliceLimit) — strict last_used_at DESC, NULL excluded
    /// 3. SearchAsync("") — fallback sort
    /// 4. Merge: pinned > recent > fallback (dedup by Id, first-seen wins)
    /// 5. Final sort: is_pinned DESC, COALESCE(last_used_at, created_at) DESC, id DESC
    /// 6. HasMore = pinnedOverflow || fallbackOverflow || merged.Count > MaxResults
    /// 7. Trim to MaxResults
    /// </summary>
    private async Task<SearchResult> GetDefaultListAsync(CancellationToken ct)
    {
        var pinnedTask = _repository.GetPinnedAsync(SearchDefaults.MaxResults, ct);
        var recentTask = _repository.GetRecentAsync(SearchDefaults.RecentSliceLimit, ct);
        var fallbackTask = _repository.SearchAsync("", null, null, ct);
        await Task.WhenAll(pinnedTask, recentTask, fallbackTask);

        var pinned = pinnedTask.Result;
        var recent = recentTask.Result;
        var fallback = fallbackTask.Result;

        // Overflow detection
        var pinnedOverflow = pinned.Count > SearchDefaults.MaxResults;
        var fallbackOverflow = fallback.Count > SearchDefaults.MaxResults;

        // Trim sentinel items
        if (pinnedOverflow)
            pinned = pinned.Take(SearchDefaults.MaxResults).ToList();
        if (fallbackOverflow)
            fallback = fallback.Take(SearchDefaults.MaxResults).ToList();

        // Merge: pinned > recent > fallback (first-seen wins)
        var merged = new List<Prompt>(pinned.Count + recent.Count + fallback.Count);
        var seenIds = new HashSet<long>();

        foreach (var p in pinned)
            if (seenIds.Add(p.Id)) merged.Add(p);
        foreach (var p in recent)
            if (seenIds.Add(p.Id)) merged.Add(p);
        foreach (var p in fallback)
            if (seenIds.Add(p.Id)) merged.Add(p);

        // Final sort: is_pinned DESC, COALESCE(last_used_at, created_at) DESC, id DESC
        merged.Sort((a, b) =>
        {
            var pinCmp = b.IsPinned.CompareTo(a.IsPinned);
            if (pinCmp != 0) return pinCmp;
            var aDate = a.LastUsedAt ?? a.CreatedAt;
            var bDate = b.LastUsedAt ?? b.CreatedAt;
            var dateCmp = bDate.CompareTo(aDate);
            if (dateCmp != 0) return dateCmp;
            return b.Id.CompareTo(a.Id);
        });

        // HasMore computation
        var hasMore = pinnedOverflow || fallbackOverflow || merged.Count > SearchDefaults.MaxResults;

        // Trim
        if (merged.Count > SearchDefaults.MaxResults)
            merged = merged.Take(SearchDefaults.MaxResults).ToList();

        return new SearchResult(merged, hasMore, IsTruncated: false);
    }

}
