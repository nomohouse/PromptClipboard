namespace PromptClipboard.App.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PromptClipboard.Application.Services;
using PromptClipboard.Domain.Entities;
using PromptClipboard.Domain.Interfaces;
using Serilog;
using System.Collections.ObjectModel;

public enum DuplicateSeverity { None, Soft, Hard }

public partial class QuickAddViewModel : ObservableObject
{
    private readonly IPromptRepository _repository;
    private readonly ITagSuggestionRepository _tagRepo;
    private readonly IDuplicateDetectionRepository _dupRepo;
    private readonly ILogger _log;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _body = string.Empty;

    [ObservableProperty]
    private string _tagsInput = string.Empty;

    [ObservableProperty]
    private string _lang = string.Empty;

    [ObservableProperty]
    private bool _isPinned;

    [ObservableProperty]
    private string? _duplicateWarning;

    [ObservableProperty]
    private long? _duplicateId;

    [ObservableProperty]
    private DuplicateSeverity _duplicateSeverity = DuplicateSeverity.None;

    public ObservableCollection<string> TagSuggestions { get; } = [];

    public event Action<long>? PromptCreated;
    public event Action? Cancelled;

    private List<string> _allTags = [];

    public QuickAddViewModel(IPromptRepository repository, ITagSuggestionRepository tagRepo, IDuplicateDetectionRepository dupRepo, ILogger log)
    {
        _repository = repository;
        _tagRepo = tagRepo;
        _dupRepo = dupRepo;
        _log = log;
    }

    public void Show(string? prefillTitle = null, string? prefillTags = null, string? prefillLang = null)
    {
        Title = prefillTitle ?? string.Empty;
        Body = string.Empty;
        TagsInput = prefillTags ?? string.Empty;
        Lang = prefillLang ?? string.Empty;
        IsPinned = false;
        DuplicateWarning = null;
        DuplicateId = null;
        DuplicateSeverity = DuplicateSeverity.None;
        _ = LoadTagsAsync();
    }

    public void Cancel()
    {
        Cancelled?.Invoke();
    }

    private async Task LoadTagsAsync()
    {
        try
        {
            _allTags = await _tagRepo.GetAllTagsAsync();
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to load tags for autocomplete");
            _allTags = [];
        }
    }

    partial void OnTagsInputChanged(string value)
    {
        UpdateTagSuggestions(value);
    }

    private void UpdateTagSuggestions(string input)
    {
        TagSuggestions.Clear();
        if (string.IsNullOrWhiteSpace(input) || _allTags.Count == 0) return;

        var lastTag = input.Split(',').Last().Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(lastTag)) return;

        var existingTags = input.Split(',')
            .Select(t => t.Trim().ToLowerInvariant())
            .Where(t => !string.IsNullOrEmpty(t))
            .ToHashSet();

        foreach (var tag in _allTags)
        {
            if (tag.Contains(lastTag, StringComparison.OrdinalIgnoreCase) && !existingTags.Contains(tag))
                TagSuggestions.Add(tag);
            if (TagSuggestions.Count >= 5) break;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(Title) && string.IsNullOrWhiteSpace(Body))
            return;

        // Check duplicates before saving
        if (DuplicateSeverity == DuplicateSeverity.None)
        {
            var found = await CheckDuplicatesAsync();
            if (found) return; // Show warning, don't save yet
        }

        await CreatePromptAsync();
    }

    [RelayCommand]
    private async Task SaveAnywayAsync()
    {
        await CreatePromptAsync();
    }

    [RelayCommand]
    private void OpenDuplicate()
    {
        // Signal to VM to switch to list and select the duplicate
        if (DuplicateId.HasValue)
        {
            DuplicateWarning = null;
            DuplicateSeverity = DuplicateSeverity.None;
            Cancelled?.Invoke();
        }
    }

    [RelayCommand]
    private void CancelDuplicate()
    {
        DuplicateWarning = null;
        DuplicateId = null;
        DuplicateSeverity = DuplicateSeverity.None;
    }

    private async Task<bool> CheckDuplicatesAsync()
    {
        try
        {
            var candidates = await _dupRepo.FindCandidatesAsync(Title, Body, limit: 10);
            if (candidates.Count == 0) return false;

            var bestScore = 0.0;
            Prompt? bestMatch = null;

            foreach (var candidate in candidates)
            {
                var score = SimilarityScorer.Score(Title, Body, candidate.Title, candidate.Body);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestMatch = candidate;
                }
            }

            if (bestMatch == null) return false;

            if (bestScore >= 0.85)
            {
                DuplicateSeverity = DuplicateSeverity.Hard;
                DuplicateWarning = $"Very similar to \"{bestMatch.Title}\" ({bestScore:P0} match)";
                DuplicateId = bestMatch.Id;
                return true;
            }

            if (bestScore >= 0.70)
            {
                DuplicateSeverity = DuplicateSeverity.Soft;
                DuplicateWarning = $"Similar to \"{bestMatch.Title}\" ({bestScore:P0} match)";
                DuplicateId = bestMatch.Id;
                return true;
            }
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Duplicate check failed");
        }

        return false;
    }

    private async Task CreatePromptAsync()
    {
        var prompt = new Prompt
        {
            Title = Title.Trim(),
            Body = Body,
            Lang = Lang.Trim(),
            IsPinned = IsPinned,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var tags = TagsInput.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim())
            .Where(t => !string.IsNullOrEmpty(t))
            .ToList();
        prompt.SetTags(tags);

        try
        {
            var id = await _repository.CreateAsync(prompt);
            _log.Information("QuickAdd created prompt {Id}: '{Title}'", id, prompt.Title);

            DuplicateWarning = null;
            DuplicateId = null;
            DuplicateSeverity = DuplicateSeverity.None;

            PromptCreated?.Invoke(id);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "QuickAdd save failed");
        }
    }
}
