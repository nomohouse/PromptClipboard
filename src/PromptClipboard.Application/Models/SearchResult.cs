namespace PromptClipboard.Application.Models;

using PromptClipboard.Domain.Entities;

public sealed record SearchResult(
    List<Prompt> Items,
    bool HasMore,
    bool IsTruncated
);
