namespace PromptClipboard.Application.Tests;

using PromptClipboard.Application.Tests.Fakes;
using PromptClipboard.Application.UseCases;
using PromptClipboard.Domain.Entities;

public sealed class ImportExportUseCaseTests
{
    private readonly FakePromptRepository _repo = new();
    private readonly ImportExportUseCase _sut;

    public ImportExportUseCaseTests()
    {
        _sut = new ImportExportUseCase(_repo);
    }

    [Fact]
    public async Task ExportAsync_EmptyRepo_ReturnValidJson()
    {
        var json = await _sut.ExportAsync();

        Assert.Contains("\"schemaVersion\"", json);
        Assert.Contains("\"prompts\"", json);
        Assert.Contains("[]", json);
    }

    [Fact]
    public async Task ExportAsync_IncludesAllFields()
    {
        var prompt = new Prompt
        {
            Title = "Test Title",
            Body = "Test Body",
            Folder = "work",
            IsPinned = true,
            Lang = "en",
            ModelHint = "gpt-4"
        };
        prompt.SetTags(["tag1", "tag2"]);
        _repo.Prompts.Add(prompt);

        var json = await _sut.ExportAsync();

        Assert.Contains("Test Title", json);
        Assert.Contains("Test Body", json);
        Assert.Contains("work", json);
        Assert.Contains("tag1", json);
        Assert.Contains("tag2", json);
        Assert.Contains("\"isPinned\": true", json);
        Assert.Contains("en", json);
        Assert.Contains("gpt-4", json);
    }

    [Fact]
    public async Task ImportAsync_ValidJson_CreatesPrompts()
    {
        var json = """
        {
            "schemaVersion": "1.0",
            "exportedAt": "2024-01-01T00:00:00Z",
            "prompts": [
                {
                    "title": "Imported 1",
                    "body": "Body 1",
                    "tags": ["a"],
                    "folder": "",
                    "isPinned": false,
                    "lang": "",
                    "modelHint": ""
                },
                {
                    "title": "Imported 2",
                    "body": "Body 2",
                    "tags": [],
                    "folder": "dev",
                    "isPinned": true,
                    "lang": "ru",
                    "modelHint": ""
                }
            ]
        }
        """;

        var count = await _sut.ImportAsync(json);

        Assert.Equal(2, count);
        Assert.Equal(2, _repo.Prompts.Count);
        Assert.Equal("Imported 1", _repo.Prompts[0].Title);
        Assert.Equal("Imported 2", _repo.Prompts[1].Title);
        Assert.True(_repo.Prompts[1].IsPinned);
    }

    [Fact]
    public async Task ImportAsync_EmptyPrompts_ReturnsZero()
    {
        var json = """
        {
            "schemaVersion": "1.0",
            "exportedAt": "2024-01-01T00:00:00Z",
            "prompts": []
        }
        """;

        var count = await _sut.ImportAsync(json);

        Assert.Equal(0, count);
        Assert.Empty(_repo.Prompts);
    }

    [Fact]
    public async Task ImportAsync_InvalidJson_Throws()
    {
        await Assert.ThrowsAsync<System.Text.Json.JsonException>(
            () => _sut.ImportAsync("not valid json"));
    }

    [Fact]
    public async Task RoundTrip_ExportThenImport_PreservesData()
    {
        var original = new Prompt
        {
            Title = "Round Trip",
            Body = "Body content",
            Folder = "test",
            IsPinned = true,
            Lang = "en",
            ModelHint = "claude"
        };
        original.SetTags(["alpha", "beta"]);
        _repo.Prompts.Add(original);

        var json = await _sut.ExportAsync();

        // Clear and reimport
        _repo.Prompts.Clear();
        var count = await _sut.ImportAsync(json);

        Assert.Equal(1, count);
        var imported = _repo.Prompts[0];
        Assert.Equal("Round Trip", imported.Title);
        Assert.Equal("Body content", imported.Body);
        Assert.Equal("test", imported.Folder);
        Assert.True(imported.IsPinned);
        Assert.Equal("en", imported.Lang);
        Assert.Equal("claude", imported.ModelHint);
        Assert.Contains("alpha", imported.GetTags());
        Assert.Contains("beta", imported.GetTags());
    }
}
