namespace PromptClipboard.Application.Tests;

using PromptClipboard.Application.Tests.Fakes;
using PromptClipboard.Application.UseCases;
using PromptClipboard.Domain.Entities;
using Serilog;

public sealed class PastePromptUseCaseTests
{
    private readonly FakePromptRepository _repo = new();
    private readonly FakeClipboardService _clipboard = new();
    private readonly FakeInputSimulator _input = new();
    private readonly FakeFocusTracker _focus = new();
    private readonly FakeFocusRestoreService _focusRestore = new();
    private readonly PastePromptUseCase _sut;

    private string? _failReason;
    private bool _succeeded;

    public PastePromptUseCaseTests()
    {
        var log = new LoggerConfiguration().CreateLogger();
        _sut = new PastePromptUseCase(_repo, _clipboard, _input, _focus, _focusRestore, log)
        {
            FocusSettleDelayMs = 0,
            PrePasteDelayMs = 0,
            PostPasteDelayMs = 0
        };
        _sut.PasteFailed += reason => _failReason = reason;
        _sut.PasteSucceeded += () => _succeeded = true;

        _repo.Prompts.Add(new Prompt { Id = 1, Title = "Test", Body = "body" });
    }

    private Task Execute() => _sut.ExecuteAsync(
        promptId: 1,
        resolvedText: "resolved",
        hidePalette: () => { },
        getForegroundWindow: () => _focus.CurrentForeground,
        isWindow: hwnd => _focus.WindowValid,
        getIntegrityLevel: _ => 0x2000, // MEDIUM
        currentProcessIL: 0x2000);

    [Fact]
    public async Task Execute_NoTargetWindow_AbortsPaste()
    {
        _focus.SavedHwnd = IntPtr.Zero;

        await Execute();

        Assert.Equal("No target window", _failReason);
        Assert.False(_succeeded);
    }

    [Fact]
    public async Task Execute_TargetWindowClosed_AbortsPaste()
    {
        _focus.WindowValid = false;

        await Execute();

        Assert.Equal("Target window closed", _failReason);
        Assert.False(_succeeded);
    }

    [Fact]
    public async Task Execute_ElevatedTarget_AbortsPaste()
    {
        await _sut.ExecuteAsync(
            promptId: 1,
            resolvedText: "resolved",
            hidePalette: () => { },
            getForegroundWindow: () => _focus.CurrentForeground,
            isWindow: _ => true,
            getIntegrityLevel: _ => 0x3000, // HIGH
            currentProcessIL: 0x2000);

        Assert.Equal("Target window runs with elevated privileges", _failReason);
        Assert.False(_succeeded);
    }

    [Fact]
    public async Task Execute_IntegrityCheckFails_AbortsPaste()
    {
        await _sut.ExecuteAsync(
            promptId: 1,
            resolvedText: "resolved",
            hidePalette: () => { },
            getForegroundWindow: () => _focus.CurrentForeground,
            isWindow: _ => true,
            getIntegrityLevel: _ => null,
            currentProcessIL: 0x2000);

        Assert.Equal("Failed to check target window privileges", _failReason);
        Assert.False(_succeeded);
    }

    [Fact]
    public async Task Execute_FocusRestoreFails_AbortsPaste()
    {
        _focusRestore.RestoreFocusResult = false;

        await Execute();

        Assert.Equal("Failed to restore focus", _failReason);
        Assert.False(_succeeded);
    }

    [Fact]
    public async Task Execute_FocusLostBeforePaste_AbortsPaste()
    {
        // After restoring focus, the foreground window changes to something else
        var callCount = 0;
        await _sut.ExecuteAsync(
            promptId: 1,
            resolvedText: "resolved",
            hidePalette: () => { },
            getForegroundWindow: () =>
            {
                callCount++;
                return callCount == 1 ? new IntPtr(999) : _focus.SavedHwnd;
            },
            isWindow: _ => true,
            getIntegrityLevel: _ => 0x2000,
            currentProcessIL: 0x2000);

        Assert.Equal("Focus lost before paste", _failReason);
        Assert.False(_succeeded);
    }

    [Fact]
    public async Task Execute_SendInputFails_AbortsPaste()
    {
        _input.SimulateCtrlVResult = 2;

        await Execute();

        Assert.Contains("SendInput sent 2/4 events", _failReason);
        Assert.False(_succeeded);
    }

    [Fact]
    public async Task Execute_Success_MarksUsedAndRaisesEvent()
    {
        await Execute();

        Assert.Null(_failReason);
        Assert.True(_succeeded);
        Assert.Equal(1L, _repo.LastMarkedUsedId);
        Assert.Equal("resolved", _clipboard.LastText);
    }
}
