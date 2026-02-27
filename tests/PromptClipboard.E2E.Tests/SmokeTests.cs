namespace PromptClipboard.E2E.Tests;

using System.Runtime.InteropServices;
using System.Text;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;

[Trait("Category", "E2E")]
public sealed class SmokeTests : IClassFixture<AppFixture>
{
    private const string TrayIconName = "Prompt Clipboard";
    private readonly AppFixture _fixture;

    public SmokeTests(AppFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void App_Launches_WithoutCrashing()
    {
        Assert.NotNull(_fixture.AppProcess);
        Assert.False(_fixture.AppProcess!.HasExited, "App should still be running after 3s");
    }

    [Fact]
    public void App_TrayIcon_Exists()
    {
        Assert.NotNull(_fixture.AppProcess);
        Assert.False(_fixture.AppProcess!.HasExited);

        using var automation = new UIA3Automation();
        var desktop = automation.GetDesktop();

        // Phase 1: UIA — search visible tray icons by Name (Win10 + Win11 visible)
        var found = RetryFind(() => FindVisibleTrayIcon(desktop), maxRetries: 3, delayMs: 500);

        if (found == null)
        {
            // Phase 2: UIA — open overflow and search by Name (Win10 overflow + some Win11)
            found = FindInOverflow(desktop);
        }

        if (found != null) return; // UIA found the icon — test passes

        // Phase 3: Win32 fallback — On Win11, overflow icons from classic Shell_NotifyIcon
        // have empty UIA Name/HelpText. Verify via hidden tray window instead.
        // Hardcodet.NotifyIcon.Wpf creates a message window with class "WPFTaskbarIcon_<guid>".
        Assert.True(
            HasTrayIconWindow(_fixture.AppProcess!.Id),
            "Tray icon not found via UIA (Win11 overflow Name is empty for classic icons). " +
            "Win32 fallback also failed: no WPFTaskbarIcon_* window in process.");
    }

    #region UIA tray search

    private static AutomationElement? FindVisibleTrayIcon(AutomationElement desktop)
    {
        var taskbar = desktop.FindFirstDescendant(cf => cf.ByClassName("Shell_TrayWnd"));
        if (taskbar == null) return null;

        return taskbar.FindAllDescendants()
            .FirstOrDefault(e => SafeName(e)?.Contains(TrayIconName) == true);
    }

    private static AutomationElement? FindInOverflow(AutomationElement desktop)
    {
        if (!OpenOverflow(desktop))
            return null;

        Thread.Sleep(1000);

        try
        {
            return RetryFind(() =>
            {
                // Win11
                var overflow = desktop.FindFirstDescendant(cf =>
                    cf.ByClassName("TopLevelWindowForOverflowXamlIsland"));
                // Win10
                overflow ??= desktop.FindFirstDescendant(cf =>
                    cf.ByClassName("NotifyIconOverflowWindow"));

                if (overflow == null) return null;

                return overflow.FindAllDescendants()
                    .FirstOrDefault(e => SafeName(e)?.Contains(TrayIconName) == true);
            }, maxRetries: 3, delayMs: 500);
        }
        finally
        {
            Keyboard.Press(VirtualKeyShort.ESCAPE);
            Thread.Sleep(300);
        }
    }

    private static bool OpenOverflow(AutomationElement desktop)
    {
        var taskbar = desktop.FindFirstDescendant(cf => cf.ByClassName("Shell_TrayWnd"));
        if (taskbar == null) return false;

        // Win11: "Show Hidden Icons"
        var showHidden = taskbar.FindAllDescendants()
            .FirstOrDefault(e => SafeName(e) == "Show Hidden Icons");
        if (showHidden != null)
        {
            showHidden.Click();
            return true;
        }

        // Win10: Button inside TrayNotifyWnd
        var trayNotify = taskbar.FindFirstDescendant(cf => cf.ByClassName("TrayNotifyWnd"));
        var chevron = trayNotify?.FindFirstDescendant(cf => cf.ByClassName("Button"));
        if (chevron != null)
        {
            chevron.Click();
            return true;
        }

        return false;
    }

    private static string? SafeName(AutomationElement e)
    {
        try { return e.Name; }
        catch { return null; }
    }

    #endregion

    #region Win32 fallback

    /// <summary>
    /// Checks if the process owns a hidden window with class "WPFTaskbarIcon_*"
    /// created by Hardcodet.NotifyIcon.Wpf when TaskbarIcon is instantiated.
    /// This proves the tray icon was created even when UIA can't expose it.
    /// </summary>
    private static bool HasTrayIconWindow(int processId)
    {
        bool found = false;
        EnumWindows((hWnd, _) =>
        {
            GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid != (uint)processId) return true;

            var sb = new StringBuilder(256);
            GetClassName(hWnd, sb, 256);
            if (sb.ToString().StartsWith("WPFTaskbarIcon_", StringComparison.Ordinal))
            {
                found = true;
                return false; // stop enumeration
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder sb, int nMaxCount);

    #endregion

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
}
