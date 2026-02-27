namespace PromptClipboard.TestContracts;

using PromptClipboard.Domain.Entities;
using PromptClipboard.Domain.Interfaces;

public abstract class PromptRepositoryContractTests
{
    protected abstract IPromptRepository CreateRepository();

    private Prompt MakePrompt(string title = "Test", string body = "Body", bool isPinned = false)
    {
        var p = new Prompt
        {
            Title = title,
            Body = body,
            IsPinned = isPinned,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        return p;
    }

    [Fact]
    public async Task Create_AssignsPositiveId()
    {
        var repo = CreateRepository();
        var prompt = MakePrompt();

        var id = await repo.CreateAsync(prompt);

        Assert.True(id > 0);
    }

    [Fact]
    public async Task GetById_AfterCreate_ReturnsPrompt()
    {
        var repo = CreateRepository();
        var prompt = MakePrompt("FindMe");
        var id = await repo.CreateAsync(prompt);

        var found = await repo.GetByIdAsync(id);

        Assert.NotNull(found);
        Assert.Equal("FindMe", found.Title);
    }

    [Fact]
    public async Task GetById_NonExistent_ReturnsNull()
    {
        var repo = CreateRepository();

        var found = await repo.GetByIdAsync(99999);

        Assert.Null(found);
    }

    [Fact]
    public async Task Delete_RemovesPrompt()
    {
        var repo = CreateRepository();
        var id = await repo.CreateAsync(MakePrompt());

        await repo.DeleteAsync(id);

        var found = await repo.GetByIdAsync(id);
        Assert.Null(found);
    }

    [Fact]
    public async Task GetCount_ReflectsCreateAndDelete()
    {
        var repo = CreateRepository();

        Assert.Equal(0, await repo.GetCountAsync());

        var id1 = await repo.CreateAsync(MakePrompt("A"));
        await repo.CreateAsync(MakePrompt("B"));
        Assert.Equal(2, await repo.GetCountAsync());

        await repo.DeleteAsync(id1);
        Assert.Equal(1, await repo.GetCountAsync());
    }

    [Fact]
    public async Task GetPinned_ReturnsPinnedOnly()
    {
        var repo = CreateRepository();
        await repo.CreateAsync(MakePrompt("Pinned", isPinned: true));
        await repo.CreateAsync(MakePrompt("NotPinned", isPinned: false));

        var pinned = await repo.GetPinnedAsync();

        Assert.Single(pinned);
        Assert.Equal("Pinned", pinned[0].Title);
    }

    [Fact]
    public async Task GetAll_ReturnsAll()
    {
        var repo = CreateRepository();
        await repo.CreateAsync(MakePrompt("One"));
        await repo.CreateAsync(MakePrompt("Two"));
        await repo.CreateAsync(MakePrompt("Three"));

        var all = await repo.GetAllAsync();

        Assert.Equal(3, all.Count);
    }
}
