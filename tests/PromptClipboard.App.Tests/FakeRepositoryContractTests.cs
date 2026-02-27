namespace PromptClipboard.App.Tests;

using PromptClipboard.App.Tests.Fakes;
using PromptClipboard.Domain.Interfaces;
using PromptClipboard.TestContracts;

public sealed class FakeRepositoryContractTests : PromptRepositoryContractTests
{
    protected override IPromptRepository CreateRepository() => new FakePromptRepository();
}
