namespace PromptClipboard.Application.Tests.Fakes;

using PromptClipboard.Domain.Interfaces;

internal sealed class FakeFocusTracker : IFocusTracker
{
    public IntPtr SavedHwnd { get; set; } = new(1);
    public IntPtr CurrentForeground { get; set; } = new(1);
    public bool WindowValid { get; set; } = true;

    public void CaptureForegroundWindow() { }
    public void ClearSavedHwnd() => SavedHwnd = IntPtr.Zero;
    public IntPtr GetCurrentForegroundWindow() => CurrentForeground;
    public bool IsWindowValid(IntPtr hwnd) => WindowValid;
}
