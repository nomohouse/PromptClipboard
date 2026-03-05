namespace PromptClipboard.App.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PromptClipboard.Application.Services;
using PromptClipboard.Domain;
using PromptClipboard.Domain.Entities;
using PromptClipboard.Domain.Interfaces;
using Serilog;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

public partial class PaletteViewModel : ObservableObject
{
    private readonly SearchRankingService _searchService;
    private readonly IPromptRepository _repository;
    private readonly ILogger _log;
    private readonly int _debounceMs;
    private CancellationTokenSource? _searchCts;

    // P0.3: search result metadata
    private bool _hasMoreResults;
    private bool _isTruncated;

    // P0.6: selection restore
    private string? _lastLoadedQueryFingerprint;

    // P0.4: total count for empty states
    private int _cachedTotalCount;

    // P0.4: transient load error
    private string? _transientLoadError;

    // P0 scaffold fields (used in later phases, P1 chips + P2 QuickAdd)
    private long? _pendingNewPromptId;
#pragma warning disable CS0649 // assigned in P1.6 chips and P2.5 RevealNewPrompt
    private bool _suppressRequery;
#pragma warning restore CS0649
    private PromptItemViewModel? _revealedPrompt;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private PromptItemViewModel? _selectedPrompt;

    [ObservableProperty]
    private int _selectedIndex;

    [ObservableProperty]
    private bool _isPasting;

    [ObservableProperty]
    private bool _hasTarget;

    public ObservableCollection<PromptItemViewModel> Prompts { get; } = [];

    public int ResultCount => Prompts.Count;

    public string ResultCountText
    {
        get
        {
            if (Prompts.Count == 0)
                return _isTruncated ? "Query truncated to 20 tokens" : string.Empty;
            var text = _hasMoreResults
                ? $"Showing {SearchDefaults.MaxResults}+"
                : $"Showing {Prompts.Count}";
            if (_isTruncated) text += " (query truncated)";
            return text;
        }
    }

    public bool ShowEmptyDatabase => Prompts.Count == 0 && _cachedTotalCount == 0;
    public bool ShowNoResults => Prompts.Count == 0 && _cachedTotalCount > 0;
    public bool ShowNewPromptBanner => _pendingNewPromptId.HasValue;

    public string? TransientLoadError => _transientLoadError;
    public bool ShowLoadError => _transientLoadError != null;

    public PromptItemViewModel? RevealedPrompt
    {
        get => _revealedPrompt;
        private set
        {
            _revealedPrompt = value;
            OnPropertyChanged(nameof(RevealedPrompt));
            OnPropertyChanged(nameof(ShowRevealedPrompt));
        }
    }

    public bool ShowRevealedPrompt => RevealedPrompt != null;

    public event Action<Prompt>? PasteRequested;
    public event Action<Prompt>? PasteAsTextRequested;
    public event Action<Prompt>? EditRequested;
    public event Action? CreateRequested;
    public event Action<string, string?, string?>? CreateWithTitleRequested;
    public event Action? CloseRequested;
    public event Action<Prompt>? CopyRequested;
    public event Action<Prompt>? PinToggleRequested;
    public event Action<Prompt>? DeleteRequested;

    public PaletteViewModel(SearchRankingService searchService, IPromptRepository repository, ILogger log, int debounceMs = 150)
    {
        _searchService = searchService;
        _repository = repository;
        _log = log;
        _debounceMs = debounceMs;
        Prompts.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(ResultCount));
            OnPropertyChanged(nameof(ResultCountText));
            OnPropertyChanged(nameof(ShowEmptyDatabase));
            OnPropertyChanged(nameof(ShowNoResults));
        };
    }

    public void OnPaletteHidden()
    {
        // SearchText is NOT cleared (persist across show/hide)
        // Cleanup CTA banner
        _pendingNewPromptId = null;
        OnPropertyChanged(nameof(ShowNewPromptBanner));
        // Cleanup reveal-card transient state
        if (RevealedPrompt != null)
            RevealedPrompt = null;
    }

    public void SetTransientLoadError(string message)
    {
        _transientLoadError = message;
        OnPropertyChanged(nameof(TransientLoadError));
        OnPropertyChanged(nameof(ShowLoadError));
    }

    public async Task RefreshTotalCountAsync(CancellationToken ct = default)
    {
        _cachedTotalCount = await _repository.GetCountAsync(ct);
        OnPropertyChanged(nameof(ShowEmptyDatabase));
        OnPropertyChanged(nameof(ShowNoResults));
    }

    partial void OnSearchTextChanged(string value)
    {
        // CTA cleanup: user started new query
        if (_pendingNewPromptId.HasValue)
        {
            _pendingNewPromptId = null;
            OnPropertyChanged(nameof(ShowNewPromptBanner));
        }
        if (_suppressRequery) return;
        _ = DebounceSearchAsync(value);
    }

    private async Task DebounceSearchAsync(string query)
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        try
        {
            await Task.Delay(_debounceMs, token);
            await LoadPromptsAsync(query, token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _log.Error(ex, "Search failed for query '{Query}'", query);
            Prompts.Clear();
            SelectedIndex = -1;
            SelectedPrompt = null;
        }
    }

    public async Task LoadPromptsAsync(string? query = null, CancellationToken ct = default)
    {
        // Clear any transient load error on successful start
        if (_transientLoadError != null)
        {
            _transientLoadError = null;
            OnPropertyChanged(nameof(TransientLoadError));
            OnPropertyChanged(nameof(ShowLoadError));
        }

        var rawQuery = (query ?? SearchText).Trim();
        var compareKey = BuildSelectionCompareKey(rawQuery);
        var previousId = SelectedPrompt?.Prompt.Id;
        var queryChanged = compareKey != _lastLoadedQueryFingerprint;
        _lastLoadedQueryFingerprint = compareKey;

        var searchResult = await _searchService.SearchAsync(rawQuery, ct);
        _hasMoreResults = searchResult.HasMore;
        _isTruncated = searchResult.IsTruncated;

        Prompts.Clear();
        foreach (var p in searchResult.Items)
            Prompts.Add(new PromptItemViewModel(p));

        // Clear reveal-card on successful load
        if (RevealedPrompt != null)
            RevealedPrompt = null;

        // Restore selection by Id if query unchanged, otherwise select first
        PromptItemViewModel? restored = null;
        if (!queryChanged && previousId.HasValue)
            restored = Prompts.FirstOrDefault(p => p.Prompt.Id == previousId.Value);

        if (restored != null)
        {
            SelectedIndex = Prompts.IndexOf(restored);
            SelectedPrompt = restored;
        }
        else
        {
            SelectedIndex = Prompts.Count > 0 ? 0 : -1;
            SelectedPrompt = Prompts.Count > 0 ? Prompts[0] : null;
        }
    }

    private static string BuildSelectionCompareKey(string rawQuery)
        => NormalizeForCompare(rawQuery);

    private static string NormalizeForCompare(string q)
        => Regex.Replace(q.ToLowerInvariant(), @"\s+", " ");

    [RelayCommand]
    private void MoveUp()
    {
        if (Prompts.Count == 0) return;
        SelectedIndex = SelectedIndex <= 0 ? Prompts.Count - 1 : SelectedIndex - 1;
        SelectedPrompt = Prompts[SelectedIndex];
    }

    [RelayCommand]
    private void MoveDown()
    {
        if (Prompts.Count == 0) return;
        SelectedIndex = SelectedIndex >= Prompts.Count - 1 ? 0 : SelectedIndex + 1;
        SelectedPrompt = Prompts[SelectedIndex];
    }

    [RelayCommand]
    private void Paste()
    {
        if (SelectedPrompt != null && !IsPasting)
            PasteRequested?.Invoke(SelectedPrompt.Prompt);
    }

    [RelayCommand]
    private void PasteAsText()
    {
        if (SelectedPrompt != null && !IsPasting)
            PasteAsTextRequested?.Invoke(SelectedPrompt.Prompt);
    }

    [RelayCommand]
    private void OpenEditor()
    {
        if (SelectedPrompt != null)
            EditRequested?.Invoke(SelectedPrompt.Prompt);
    }

    [RelayCommand]
    private void Create()
    {
        CreateRequested?.Invoke();
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchText = string.Empty;
    }

    [RelayCommand]
    private async Task RetryLoad()
    {
        try
        {
            await RefreshTotalCountAsync();
            await LoadPromptsAsync();
        }
        catch (Exception ex)
        {
            _log.Error(ex, "RetryLoad failed");
            SetTransientLoadError("Retry failed. Check database file/permissions and try again.");
        }
    }

    [RelayCommand]
    private void DismissError()
    {
        _transientLoadError = null;
        OnPropertyChanged(nameof(TransientLoadError));
        OnPropertyChanged(nameof(ShowLoadError));
    }

    [RelayCommand]
    private void CreateWithTitle()
    {
        var (freeText, tagFilter, langFilter) = SearchRankingService.ParseQuery(SearchText);
        CreateWithTitleRequested?.Invoke(freeText.Trim(), tagFilter, langFilter);
    }

    [RelayCommand]
    private void HandleEscape()
    {
        if (!string.IsNullOrEmpty(SearchText))
            SearchText = string.Empty;
        else
            CloseRequested?.Invoke();
    }

    [RelayCommand]
    private void CopyPrompt()
    {
        if (SelectedPrompt != null)
            CopyRequested?.Invoke(SelectedPrompt.Prompt);
    }

    [RelayCommand]
    private void TogglePin(PromptItemViewModel? item)
    {
        if (item != null)
            PinToggleRequested?.Invoke(item.Prompt);
    }

    [RelayCommand]
    private void DeletePrompt(PromptItemViewModel? item)
    {
        if (item != null)
            DeleteRequested?.Invoke(item.Prompt);
    }

    public void RaiseEditRequested(Prompt prompt) => EditRequested?.Invoke(prompt);
    public void RaiseCopyRequested(Prompt prompt) => CopyRequested?.Invoke(prompt);
}
