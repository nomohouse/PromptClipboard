namespace PromptClipboard.Domain.Interfaces;

using PromptClipboard.Domain;
using PromptClipboard.Domain.Entities;

public interface IPromptRepository
{
    Task<List<Prompt>> SearchAsync(string query, string? tagFilter = null, string? langFilter = null, CancellationToken ct = default);
    Task<List<Prompt>> GetPinnedAsync(CancellationToken ct = default);
    /// <summary>
    /// Returns pinned prompts sorted by COALESCE(last_used_at, created_at) DESC, id DESC.
    /// Internally uses LIMIT (limit+1) so caller can detect overflow via count > limit.
    /// </summary>
    Task<List<Prompt>> GetPinnedAsync(int limit, CancellationToken ct = default);
    Task<List<Prompt>> GetRecentAsync(int limit = SearchDefaults.RecentSliceLimit, CancellationToken ct = default);
    Task<Prompt?> GetByIdAsync(long id, CancellationToken ct = default);
    Task<long> CreateAsync(Prompt prompt, CancellationToken ct = default);
    Task UpdateAsync(Prompt prompt, CancellationToken ct = default);
    Task DeleteAsync(long id, CancellationToken ct = default);
    Task MarkUsedAsync(long id, DateTime usedAt, CancellationToken ct = default);
    Task<List<Prompt>> GetAllAsync(CancellationToken ct = default);
    Task<int> GetCountAsync(CancellationToken ct = default);
}
