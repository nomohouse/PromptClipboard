namespace PromptClipboard.E2E.Tests;

using System.Diagnostics;

public sealed class AppFixture : IDisposable
{
    public Process? AppProcess { get; private set; }

    public AppFixture()
    {
        // Kill any running instance first (single-instance mutex blocks second launch)
        foreach (var proc in Process.GetProcessesByName("PromptClipboard.App"))
        {
            try { proc.Kill(); } catch { /* ignore */ }
        }
        Thread.Sleep(1000);

        var exePath = FindAppExe();
        if (exePath != null && File.Exists(exePath))
        {
            AppProcess = Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = false
            });
            Thread.Sleep(3000); // Wait for app to initialize
        }
    }

    private static string? FindAppExe()
    {
        // Look for the published exe relative to test output
        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "..", "..", "..", "..", "..", "src", "PromptClipboard.App", "bin", "Debug", "net8.0-windows", "PromptClipboard.App.exe"),
            Path.Combine(baseDir, "..", "..", "..", "..", "..", "src", "PromptClipboard.App", "bin", "Release", "net8.0-windows", "PromptClipboard.App.exe"),
            Path.Combine(baseDir, "..", "..", "..", "..", "..", "artifacts", "publish", "PromptClipboard.App.exe")
        };

        return candidates.Select(Path.GetFullPath).FirstOrDefault(File.Exists);
    }

    public void Dispose()
    {
        if (AppProcess is { HasExited: false })
        {
            AppProcess.CloseMainWindow();
            if (!AppProcess.WaitForExit(5000))
                AppProcess.Kill();
        }
        AppProcess?.Dispose();
    }
}
