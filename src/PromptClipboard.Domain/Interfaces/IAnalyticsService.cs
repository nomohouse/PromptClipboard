namespace PromptClipboard.Domain.Interfaces;

public interface IAnalyticsService
{
    Task RecordEventAsync(string eventType, long? promptId = null, string? metadataJson = null, CancellationToken ct = default);
    Task CleanupAsync(int retentionDays, int maxRows, CancellationToken ct = default);
    Task ClearAllAsync(CancellationToken ct = default);
}
