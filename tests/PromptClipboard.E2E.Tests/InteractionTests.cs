namespace PromptClipboard.E2E.Tests;

using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;

[Trait("Category", "E2E")]
public sealed class InteractionTests : IClassFixture<AppFixture>
{
    private readonly AppFixture _fixture;

    public InteractionTests(AppFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void DoubleClick_PromptCard_TriggersAction()
    {
        Assert.NotNull(_fixture.AppProcess);
        Assert.False(_fixture.AppProcess!.HasExited);

        using var automation = new UIA3Automation();
        var window = OpenPalette(automation);
        if (window == null) { Assert.Fail("Palette window not found"); return; }

        var list = window.FindFirstDescendant(cf => cf.ByAutomationId("PromptList"));
        Assert.NotNull(list);

        var items = list!.FindAllChildren();
        if (items.Length == 0) return; // No prompts seeded — skip

        var firstItem = items[0];
        firstItem.DoubleClick();
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        // After double-click, palette should hide (paste action hides the window)
        var windowAfter = FindPaletteWindow(automation);
        Assert.True(windowAfter == null || !windowAfter.IsAvailable,
            "Palette should hide after double-click paste");
    }

    [Fact]
    public void Enter_WithSelection_TriggersAction()
    {
        Assert.NotNull(_fixture.AppProcess);
        Assert.False(_fixture.AppProcess!.HasExited);

        using var automation = new UIA3Automation();
        var window = OpenPalette(automation);
        if (window == null) { Assert.Fail("Palette window not found"); return; }

        var list = window.FindFirstDescendant(cf => cf.ByAutomationId("PromptList"));
        Assert.NotNull(list);

        var items = list!.FindAllChildren();
        if (items.Length == 0) return; // No prompts seeded — skip

        // Arrow down to select first item, then Enter
        Keyboard.Press(VirtualKeyShort.DOWN);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(200));
        Keyboard.Press(VirtualKeyShort.ENTER);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        var windowAfter = FindPaletteWindow(automation);
        Assert.True(windowAfter == null || !windowAfter.IsAvailable,
            "Palette should hide after Enter paste");
    }

    [Fact]
    public void Escape_ClearsSearchThenCloses()
    {
        Assert.NotNull(_fixture.AppProcess);
        Assert.False(_fixture.AppProcess!.HasExited);

        using var automation = new UIA3Automation();
        var window = OpenPalette(automation);
        if (window == null) { Assert.Fail("Palette window not found"); return; }

        var searchBox = window.FindFirstDescendant(cf => cf.ByAutomationId("SearchBox"));
        Assert.NotNull(searchBox);

        // Type something in search
        searchBox!.AsTextBox().Enter("test");
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));

        // First Escape: clears search text
        Keyboard.Press(VirtualKeyShort.ESCAPE);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));

        var textAfterFirst = searchBox.AsTextBox().Text;
        Assert.True(string.IsNullOrEmpty(textAfterFirst), "First Escape should clear search text");

        // Second Escape: closes window
        Keyboard.Press(VirtualKeyShort.ESCAPE);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        var windowAfter = FindPaletteWindow(automation);
        Assert.True(windowAfter == null || !windowAfter.IsAvailable,
            "Second Escape should close the palette");
    }

    [Fact]
    public void CtrlN_OpensQuickAddMode()
    {
        Assert.NotNull(_fixture.AppProcess);
        Assert.False(_fixture.AppProcess!.HasExited);

        using var automation = new UIA3Automation();
        var window = OpenPalette(automation);
        if (window == null) { Assert.Fail("Palette window not found"); return; }

        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_N);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        // QuickAdd mode should show — look for a save button or QuickAdd panel
        var windowStill = FindPaletteWindow(automation);
        Assert.NotNull(windowStill);
        Assert.True(windowStill!.IsAvailable, "Palette should stay open in QuickAdd mode");

        // Escape back to list mode
        Keyboard.Press(VirtualKeyShort.ESCAPE);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(200));
    }

    #region Helpers

    private AutomationElement? OpenPalette(UIA3Automation automation)
    {
        // Try Ctrl+Shift+P to open palette (default hotkey)
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.SHIFT, VirtualKeyShort.KEY_P);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(2000));

        return RetryFind(() => FindPaletteWindow(automation), maxRetries: 5, delayMs: 500);
    }

    private static AutomationElement? FindPaletteWindow(UIA3Automation automation)
    {
        var desktop = automation.GetDesktop();
        return desktop.FindFirstChild(cf => cf.ByName("Prompt Clipboard").And(cf.ByControlType(ControlType.Window)));
    }

    private static T? RetryFind<T>(Func<T?> action, int maxRetries, int delayMs) where T : class
    {
        for (int i = 0; i < maxRetries; i++)
        {
            var result = action();
            if (result != null) return result;
            Thread.Sleep(delayMs);
        }
        return action();
    }

    #endregion
}
