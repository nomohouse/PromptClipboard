namespace PromptClipboard.App.Tests.Snapshots;

using PromptClipboard.App.Tests.Fakes;
using PromptClipboard.App.ViewModels;
using PromptClipboard.Application.Services;
using PromptClipboard.Domain.Entities;
using Serilog;
using VerifyXunit;

[Trait("Category", "Snapshot")]
public sealed class PaletteViewModelSnapshotTests
{
    [Fact]
    public async Task LoadPrompts_SnapshotMatchesExpected()
    {
        var repo = new FakePromptRepository();
        var p1 = new Prompt { Title = "Greeting", Body = "Hello {{name}}", Folder = "general", Lang = "en", IsPinned = true };
        p1.SetTags(["greeting", "template"]);
        repo.Prompts.Add(p1);
        p1.Id = 1;

        var p2 = new Prompt { Title = "Code Review", Body = "Please review this code:\n```\n{{code}}\n```", Folder = "dev", Lang = "en" };
        p2.SetTags(["dev", "review"]);
        repo.Prompts.Add(p2);
        p2.Id = 2;

        var log = new LoggerConfiguration().CreateLogger();
        var search = new SearchRankingService(repo);
        var vm = new PaletteViewModel(search, repo, log, debounceMs: 0);
        await vm.LoadPromptsAsync();

        var snapshot = vm.Prompts.Select(p => new
        {
            p.Prompt.Title,
            p.Prompt.Body,
            Tags = p.Prompt.GetTags(),
            p.Prompt.Folder,
            p.Prompt.IsPinned,
            p.IsExpanded,
            p.IsLongBody
        });

        await Verifier.Verify(snapshot);
    }
}
