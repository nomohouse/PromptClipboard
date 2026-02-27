namespace PromptClipboard.App.Tests;

using PromptClipboard.App.Tests.Fakes;
using PromptClipboard.App.ViewModels;
using PromptClipboard.Domain.Entities;

public sealed class EditorViewModelTests
{
    private readonly FakePromptRepository _repo = new();
    private readonly EditorViewModel _sut;
    private bool? _closeResult;

    public EditorViewModelTests()
    {
        _sut = new EditorViewModel(_repo);
        _sut.RequestClose += result => _closeResult = result;
    }

    [Fact]
    public void LoadForCreate_SetsDefaultState()
    {
        _sut.LoadForCreate();

        Assert.Equal("New prompt", _sut.WindowTitle);
        Assert.Equal(string.Empty, _sut.Title);
        Assert.Equal(string.Empty, _sut.Body);
        Assert.Equal(string.Empty, _sut.TagsInput);
        Assert.Equal(string.Empty, _sut.Folder);
        Assert.Equal(string.Empty, _sut.Lang);
        Assert.False(_sut.IsPinned);
    }

    [Fact]
    public void LoadForEdit_PopulatesFromPrompt()
    {
        var prompt = new Prompt
        {
            Id = 42,
            Title = "My Prompt",
            Body = "Some body",
            Folder = "work",
            Lang = "en",
            IsPinned = true
        };
        prompt.SetTags(["tag1", "tag2"]);

        _sut.LoadForEdit(prompt);

        Assert.Equal("Edit prompt", _sut.WindowTitle);
        Assert.Equal("My Prompt", _sut.Title);
        Assert.Equal("Some body", _sut.Body);
        Assert.Contains("tag1", _sut.TagsInput);
        Assert.Contains("tag2", _sut.TagsInput);
        Assert.Equal("work", _sut.Folder);
        Assert.Equal("en", _sut.Lang);
        Assert.True(_sut.IsPinned);
    }

    [Fact]
    public async Task SaveAsync_EmptyTitle_DoesNotCreatePrompt()
    {
        _sut.LoadForCreate();
        _sut.Title = "";
        _sut.Body = "has body";

        await _sut.SaveCommand.ExecuteAsync(null);

        Assert.Empty(_repo.Prompts);
        Assert.Null(_closeResult);
    }

    [Fact]
    public async Task SaveAsync_EmptyBody_DoesNotCreatePrompt()
    {
        _sut.LoadForCreate();
        _sut.Title = "has title";
        _sut.Body = "";

        await _sut.SaveCommand.ExecuteAsync(null);

        Assert.Empty(_repo.Prompts);
        Assert.Null(_closeResult);
    }

    [Fact]
    public async Task SaveAsync_NewPrompt_CreatesInRepo()
    {
        _sut.LoadForCreate();
        _sut.Title = "New Title";
        _sut.Body = "New Body";
        _sut.TagsInput = "a, b";
        _sut.Folder = "dev";

        await _sut.SaveCommand.ExecuteAsync(null);

        Assert.Single(_repo.Prompts);
        Assert.Equal("New Title", _repo.Prompts[0].Title);
        Assert.Equal("New Body", _repo.Prompts[0].Body);
        Assert.Equal("dev", _repo.Prompts[0].Folder);
        Assert.True(_closeResult);
    }

    [Fact]
    public async Task SaveAsync_EditPrompt_UpdatesInRepo()
    {
        var prompt = new Prompt { Id = 1, Title = "Old", Body = "Old body" };
        _repo.Prompts.Add(prompt);
        _sut.LoadForEdit(prompt);
        _sut.Title = "Updated";
        _sut.Body = "Updated body";

        await _sut.SaveCommand.ExecuteAsync(null);

        Assert.Equal("Updated", prompt.Title);
        Assert.Equal("Updated body", prompt.Body);
        Assert.True(_closeResult);
    }

    [Fact]
    public async Task DeleteAsync_ExistingPrompt_RemovesFromRepo()
    {
        var prompt = new Prompt { Id = 5, Title = "To delete", Body = "body" };
        _repo.Prompts.Add(prompt);
        _sut.LoadForEdit(prompt);

        await _sut.DeleteCommand.ExecuteAsync(null);

        Assert.Empty(_repo.Prompts);
        Assert.True(_closeResult);
    }

    [Fact]
    public void Cancel_RaisesRequestCloseWithFalse()
    {
        _sut.CancelCommand.Execute(null);

        Assert.False(_closeResult);
    }

    [Fact]
    public async Task SaveAsync_TagsParsing_TrimsWhitespace()
    {
        _sut.LoadForCreate();
        _sut.Title = "Tagged";
        _sut.Body = "Body";
        _sut.TagsInput = " tag1 , tag2 , ";

        await _sut.SaveCommand.ExecuteAsync(null);

        var tags = _repo.Prompts[0].GetTags();
        Assert.Contains("tag1", tags);
        Assert.Contains("tag2", tags);
        Assert.DoesNotContain("", tags);
    }
}
