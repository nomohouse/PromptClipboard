namespace PromptClipboard.Application.Tests.Fakes;

using PromptClipboard.Domain.Interfaces;

internal sealed class FakeFocusRestoreService : IFocusRestoreService
{
    public bool RestoreFocusResult { get; set; } = true;

    public bool RestoreFocus(IntPtr targetHwnd) => RestoreFocusResult;
}
