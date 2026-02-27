namespace PromptClipboard.Application.Tests.Fakes;

using PromptClipboard.Domain.Interfaces;

internal sealed class FakeClipboardService : IClipboardService
{
    public string? LastText { get; private set; }
    public Guid? LastMarkerId { get; private set; }
    public bool HasMarkerResult { get; set; } = true;

    public IClipboardSnapshot Save() => new FakeSnapshot();

    public void SetTextWithMarker(string text, Guid operationId)
    {
        LastText = text;
        LastMarkerId = operationId;
    }

    public bool HasMarker(Guid operationId) => HasMarkerResult;

    public void Restore(IClipboardSnapshot snapshot) { }

    private sealed class FakeSnapshot : IClipboardSnapshot
    {
        public bool IsEmpty => true;
        public void Dispose() { }
    }
}
