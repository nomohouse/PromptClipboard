namespace PromptClipboard.Domain.Models;

public sealed record SearchQuery
{
    public IReadOnlyList<string> FreeTextTerms { get; init; } = [];
    public IReadOnlyList<string> IncludeTags { get; init; } = [];
    public IReadOnlyList<string> ExcludeTags { get; init; } = [];
    public IReadOnlyList<string> ExcludeWords { get; init; } = [];
    public string? FolderFilter { get; init; }
    public string? LangFilter { get; init; }
    public bool? PinnedFilter { get; init; }
    public bool? HasTemplate { get; init; }
    public int? RecentLimit { get; init; }
    public SortMode Sort { get; init; } = SortMode.Relevance;
    public bool IsTruncated { get; init; }

    public bool HasPositiveTerms => FreeTextTerms.Count > 0;
    public bool HasNegativeTerms => ExcludeWords.Count > 0 || ExcludeTags.Count > 0;
    public bool IsNegativeOnly => !HasPositiveTerms && HasNegativeTerms;
}

public enum SortMode { Relevance, Recent, MostUsed, PinnedFirst }
