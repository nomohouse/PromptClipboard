namespace PromptClipboard.Application.Tests;

using PromptClipboard.Application.Services;

public class SavedViewQueryMigratorTests
{
    [Fact]
    public void CurrentVersion_Noop()
    {
        var json = "{\"FreeTextTerms\":[\"hello\"]}";
        var result = SavedViewQueryMigrator.Migrate(json, SavedViewQueryMigrator.CurrentVersion);

        Assert.NotNull(result);
        Assert.Equal(json, result!.Value.Json);
        Assert.Equal(SavedViewQueryMigrator.CurrentVersion, result.Value.Version);
    }

    [Fact]
    public void UnknownFutureVersion_ReturnsNull()
    {
        var result = SavedViewQueryMigrator.Migrate("{}", SavedViewQueryMigrator.CurrentVersion + 1);
        Assert.Null(result);
    }

    // TechDebtIssue[saved-view-v1-v2]: deferred — test created when schema v2 is defined
}
