namespace PromptClipboard.Infrastructure.Tests;

using PromptClipboard.Domain.Interfaces;
using PromptClipboard.Infrastructure.Platform;
using Serilog;
using Serilog.Sinks.InMemory;

public sealed class SettingsServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly InMemorySink _sink;
    private readonly SettingsService _sut;

    public SettingsServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"PCTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _sink = new InMemorySink();
        var log = new LoggerConfiguration().WriteTo.Sink(_sink).CreateLogger();
        _sut = new SettingsService(_tempDir, log);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var settings = _sut.Load();

        Assert.Equal("Ctrl+Shift+Q", settings.Hotkey);
        Assert.Equal(50, settings.PasteDelayMs);
        Assert.Equal(150, settings.RestoreDelayMs);
        Assert.False(settings.AutoStart);
    }

    [Fact]
    public void Load_CorruptJson_ReturnsDefaultsAndLogsWarning()
    {
        File.WriteAllText(Path.Combine(_tempDir, "settings.json"), "not valid json!!!");

        var settings = _sut.Load();

        Assert.Equal("Ctrl+Shift+Q", settings.Hotkey);
        Assert.Contains(_sink.LogEvents, e => e.Level == Serilog.Events.LogEventLevel.Warning);
    }

    [Fact]
    public void Save_CreatesDirectoryAndFile()
    {
        var nestedDir = Path.Combine(_tempDir, "nested", "sub");
        var log = new LoggerConfiguration().CreateLogger();
        var sut = new SettingsService(nestedDir, log);

        sut.Save(new AppSettings { Hotkey = "Ctrl+Q" });

        Assert.True(File.Exists(Path.Combine(nestedDir, "settings.json")));
    }

    [Fact]
    public void RoundTrip_SaveThenLoad_PreservesValues()
    {
        var original = new AppSettings
        {
            Hotkey = "Alt+P",
            PasteDelayMs = 100,
            RestoreDelayMs = 200,
            AutoStart = true
        };

        _sut.Save(original);
        var loaded = _sut.Load();

        Assert.Equal("Alt+P", loaded.Hotkey);
        Assert.Equal(100, loaded.PasteDelayMs);
        Assert.Equal(200, loaded.RestoreDelayMs);
        Assert.True(loaded.AutoStart);
    }
}
