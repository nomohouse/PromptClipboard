namespace PromptClipboard.App.Tests.Fakes;

using PromptClipboard.Domain;
using PromptClipboard.Domain.Entities;
using PromptClipboard.Domain.Interfaces;
using PromptClipboard.Domain.Models;

internal class FakePromptRepository : IPromptRepository, IAdvancedSearchRepository, ITagSuggestionRepository, IDuplicateDetectionRepository
{
    public List<Prompt> Prompts { get; set; } = [];
    public bool ThrowOnSearch { get; set; }

    public Task<List<Prompt>> SearchAsync(string query, string? tagFilter = null, string? langFilter = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (ThrowOnSearch) throw new InvalidOperationException("Simulated FTS5 error");
        var results = Prompts.Where(p => string.IsNullOrEmpty(query) || p.Title.Contains(query, StringComparison.OrdinalIgnoreCase) || p.Body.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        if (!string.IsNullOrEmpty(tagFilter))
            results = results.Where(p => p.TagsText.Contains(tagFilter, StringComparison.OrdinalIgnoreCase)).ToList();
        return Task.FromResult(results.Take(SearchDefaults.MaxResults + 1).ToList());
    }

    public Task<List<Prompt>> GetPinnedAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (ThrowOnSearch) throw new InvalidOperationException("Simulated error");
        return Task.FromResult(Prompts.Where(p => p.IsPinned).ToList());
    }

    public Task<List<Prompt>> GetPinnedAsync(int limit, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (ThrowOnSearch) throw new InvalidOperationException("Simulated error");
        return Task.FromResult(Prompts
            .Where(p => p.IsPinned)
            .OrderByDescending(p => p.LastUsedAt ?? p.CreatedAt)
            .ThenByDescending(p => p.Id)
            .Take(limit + 1)
            .ToList());
    }

    public Task<List<Prompt>> GetRecentAsync(int limit = SearchDefaults.RecentSliceLimit, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (ThrowOnSearch) throw new InvalidOperationException("Simulated error");
        return Task.FromResult(Prompts
            .Where(p => p.LastUsedAt.HasValue)
            .OrderByDescending(p => p.LastUsedAt)
            .Take(limit)
            .ToList());
    }

    public Task<Prompt?> GetByIdAsync(long id, CancellationToken ct = default) =>
        Task.FromResult(Prompts.FirstOrDefault(p => p.Id == id));

    public Task<long> CreateAsync(Prompt prompt, CancellationToken ct = default)
    {
        prompt.Id = Prompts.Count + 1;
        Prompts.Add(prompt);
        return Task.FromResult(prompt.Id);
    }

    public Task UpdateAsync(Prompt prompt, CancellationToken ct = default) => Task.CompletedTask;
    public Task DeleteAsync(long id, CancellationToken ct = default)
    {
        Prompts.RemoveAll(p => p.Id == id);
        return Task.CompletedTask;
    }

    public Task MarkUsedAsync(long id, DateTime usedAt, CancellationToken ct = default) => Task.CompletedTask;
    public Task<List<Prompt>> GetAllAsync(CancellationToken ct = default) => Task.FromResult(Prompts.ToList());
    public Task<int> GetCountAsync(CancellationToken ct = default) => Task.FromResult(Prompts.Count);

    public Task<List<string>> GetAllTagsAsync(CancellationToken ct = default)
    {
        var tags = Prompts.SelectMany(p => p.GetTags()).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t).ToList();
        return Task.FromResult(tags);
    }

    public Task<List<Prompt>> FindCandidatesAsync(string title, string body, int limit = 10, CancellationToken ct = default)
    {
        return Task.FromResult(Prompts.Take(limit).ToList());
    }

    public Task<List<Prompt>> SearchAsync(SearchQuery query, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (ThrowOnSearch) throw new InvalidOperationException("Simulated error");

        var results = Prompts.AsEnumerable();

        // Free text filter
        if (query.FreeTextTerms.Count > 0)
        {
            var terms = query.FreeTextTerms;
            results = results.Where(p => terms.Any(t =>
                p.Title.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                p.Body.Contains(t, StringComparison.OrdinalIgnoreCase)));
        }

        // Exclude words
        foreach (var word in query.ExcludeWords)
        {
            var w = word;
            results = results.Where(p =>
                !p.Title.Contains(w, StringComparison.OrdinalIgnoreCase) &&
                !p.Body.Contains(w, StringComparison.OrdinalIgnoreCase));
        }

        // Include tags (AND)
        foreach (var tag in query.IncludeTags)
        {
            var t = tag;
            results = results.Where(p => p.GetTags().Any(pt =>
                pt.Equals(t, StringComparison.OrdinalIgnoreCase)));
        }

        // Exclude tags
        foreach (var tag in query.ExcludeTags)
        {
            var t = tag;
            results = results.Where(p => !p.GetTags().Any(pt =>
                pt.Equals(t, StringComparison.OrdinalIgnoreCase)));
        }

        // Folder filter
        if (!string.IsNullOrWhiteSpace(query.FolderFilter))
            results = results.Where(p => p.Folder == query.FolderFilter);

        // Lang filter
        if (!string.IsNullOrWhiteSpace(query.LangFilter))
            results = results.Where(p => p.Lang.Equals(query.LangFilter, StringComparison.OrdinalIgnoreCase));

        // Pinned filter
        if (query.PinnedFilter == true)
            results = results.Where(p => p.IsPinned);

        // Template filter
        if (query.HasTemplate == true)
            results = results.Where(p => p.Body.Contains("{{"));

        // Recent filter
        if (query.RecentLimit.HasValue)
            results = results.Where(p => p.LastUsedAt.HasValue);

        // Sort
        results = query.Sort switch
        {
            SortMode.Recent => results.OrderByDescending(p => p.LastUsedAt).ThenByDescending(p => p.Id),
            SortMode.MostUsed => results.OrderByDescending(p => p.UseCount).ThenByDescending(p => p.LastUsedAt).ThenByDescending(p => p.Id),
            SortMode.PinnedFirst => results.OrderByDescending(p => p.IsPinned).ThenByDescending(p => p.LastUsedAt ?? p.CreatedAt).ThenByDescending(p => p.Id),
            _ => results.OrderByDescending(p => p.IsPinned).ThenByDescending(p => p.LastUsedAt ?? p.CreatedAt).ThenByDescending(p => p.UseCount).ThenByDescending(p => p.Id),
        };

        // Limit
        var limit = query.RecentLimit ?? (SearchDefaults.MaxResults + 1);
        return Task.FromResult(results.Take(limit).ToList());
    }
}
