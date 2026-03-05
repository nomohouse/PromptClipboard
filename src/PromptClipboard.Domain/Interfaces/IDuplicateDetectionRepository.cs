namespace PromptClipboard.Domain.Interfaces;

using PromptClipboard.Domain.Entities;

public interface IDuplicateDetectionRepository
{
    Task<List<Prompt>> FindCandidatesAsync(string title, string body, int limit = 10, CancellationToken ct = default);
}
