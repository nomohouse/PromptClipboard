namespace PromptClipboard.Application.Tests.Fakes;

using PromptClipboard.Domain.Interfaces;

internal sealed class FakeInputSimulator : IInputSimulator
{
    public uint SimulateCtrlVResult { get; set; } = 4;

    public uint SimulateCtrlV() => SimulateCtrlVResult;
}
