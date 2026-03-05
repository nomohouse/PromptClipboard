namespace PromptClipboard.Domain.Interfaces;

using PromptClipboard.Domain.Entities;
using PromptClipboard.Domain.Models;

public interface IAdvancedSearchRepository
{
    Task<List<Prompt>> SearchAsync(SearchQuery query, CancellationToken ct = default);
}
