namespace PromptClipboard.App.Tests;

using PromptClipboard.App.Tests.Fakes;
using PromptClipboard.App.ViewModels;
using PromptClipboard.Domain.Entities;
using Serilog;

public class QuickAddViewModelTests
{
    private readonly FakePromptRepository _repo = new();
    private readonly ILogger _log = new LoggerConfiguration().CreateLogger();

    private QuickAddViewModel CreateVm() => new(_repo, _repo, _repo, _log);

    [Fact]
    public async Task SaveCreatesPrompt()
    {
        var vm = CreateVm();
        vm.Show();
        vm.Title = "Test prompt";
        vm.Body = "Test body";

        long? createdId = null;
        vm.PromptCreated += id => createdId = id;

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.NotNull(createdId);
        Assert.Single(_repo.Prompts);
        Assert.Equal("Test prompt", _repo.Prompts[0].Title);
    }

    [Fact]
    public void Cancel_RaisesCancelledEvent()
    {
        var vm = CreateVm();
        vm.Show();
        var cancelled = false;
        vm.Cancelled += () => cancelled = true;

        vm.Cancel();

        Assert.True(cancelled);
    }

    [Fact]
    public async Task PrefillFromSearch()
    {
        var vm = CreateVm();
        vm.Show(prefillTitle: "hello", prefillTags: "email", prefillLang: "en");

        Assert.Equal("hello", vm.Title);
        Assert.Equal("email", vm.TagsInput);
        Assert.Equal("en", vm.Lang);

        vm.Body = "body";
        long? id = null;
        vm.PromptCreated += i => id = i;
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.NotNull(id);
        var created = _repo.Prompts[0];
        Assert.Equal("hello", created.Title);
        Assert.Contains("email", created.GetTags());
        Assert.Equal("en", created.Lang);
    }

    [Fact]
    public async Task DuplicateWarning_HardThreshold()
    {
        // Add existing prompt
        _repo.Prompts.Add(new Prompt { Id = 1, Title = "Send email to user", Body = "Send email to users" });

        var vm = CreateVm();
        vm.Show();
        vm.Title = "Send email to user";
        vm.Body = "Send email to users";

        // First save triggers duplicate check
        await vm.SaveCommand.ExecuteAsync(null);

        // Should detect duplicate and show warning without saving
        Assert.NotNull(vm.DuplicateWarning);
        Assert.Single(_repo.Prompts); // not created yet
    }

    [Fact]
    public async Task SaveAnyway_CreatesPromptDespiteDuplicate()
    {
        _repo.Prompts.Add(new Prompt { Id = 1, Title = "Test", Body = "Test body" });

        var vm = CreateVm();
        vm.Show();
        vm.Title = "Test";
        vm.Body = "Test body";

        // Trigger duplicate detection
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.NotNull(vm.DuplicateWarning);

        long? id = null;
        vm.PromptCreated += i => id = i;
        await vm.SaveAnywayCommand.ExecuteAsync(null);

        Assert.NotNull(id);
        Assert.Equal(2, _repo.Prompts.Count);
    }

    [Fact]
    public void OpenDuplicate_SwitchesToList()
    {
        var vm = CreateVm();
        vm.Show();
        vm.DuplicateId = 42;
        vm.DuplicateWarning = "Similar prompt";

        var cancelled = false;
        vm.Cancelled += () => cancelled = true;

        vm.OpenDuplicateCommand.Execute(null);

        Assert.True(cancelled);
        Assert.Null(vm.DuplicateWarning);
    }

    [Fact]
    public async Task EmptyTitleAndBody_DoesNotSave()
    {
        var vm = CreateVm();
        vm.Show();
        vm.Title = "";
        vm.Body = "";

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Empty(_repo.Prompts);
    }

    [Fact]
    public void CancelDuplicate_ClearsWarning()
    {
        var vm = CreateVm();
        vm.DuplicateWarning = "Similar";
        vm.DuplicateId = 1;
        vm.DuplicateSeverity = DuplicateSeverity.Hard;

        vm.CancelDuplicateCommand.Execute(null);

        Assert.Null(vm.DuplicateWarning);
        Assert.Null(vm.DuplicateId);
        Assert.Equal(DuplicateSeverity.None, vm.DuplicateSeverity);
    }

    [Fact]
    public async Task TagsInput_ParsedAndSaved()
    {
        var vm = CreateVm();
        vm.Show();
        vm.Title = "Test";
        vm.Body = "Body";
        vm.TagsInput = "email, work, personal";

        long? id = null;
        vm.PromptCreated += i => id = i;
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.NotNull(id);
        var tags = _repo.Prompts[0].GetTags();
        Assert.Contains("email", tags);
        Assert.Contains("work", tags);
        Assert.Contains("personal", tags);
    }
}
