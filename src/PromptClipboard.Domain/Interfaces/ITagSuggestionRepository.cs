namespace PromptClipboard.Domain.Interfaces;

public interface ITagSuggestionRepository
{
    Task<List<string>> GetAllTagsAsync(CancellationToken ct = default);
}
