namespace PromptClipboard.Infrastructure.Tests;

using Microsoft.Data.Sqlite;
using PromptClipboard.Infrastructure.Persistence;
using Serilog;

public sealed class MigrationRunnerTests : IDisposable
{
    private readonly SqliteConnection _sentinel;
    private readonly SqliteConnectionFactory _factory;
    private readonly MigrationRunner _sut;

    public MigrationRunnerTests()
    {
        var dbName = $"file:migtest_{Guid.NewGuid():N}?mode=memory&cache=shared";
        _sentinel = new SqliteConnection($"Data Source={dbName}");
        _sentinel.Open();
        _factory = new SqliteConnectionFactory(_sentinel);
        var log = new LoggerConfiguration().CreateLogger();
        _sut = new MigrationRunner(_factory, log);
    }

    public void Dispose()
    {
        _sentinel.Dispose();
    }

    [Fact]
    public void RunAll_Idempotent_SecondRunNoOp()
    {
        _sut.RunAll();
        var countAfterFirst = GetMigrationCount();

        _sut.RunAll();
        var countAfterSecond = GetMigrationCount();

        Assert.True(countAfterFirst >= 1, "At least one migration should have run");
        Assert.Equal(countAfterFirst, countAfterSecond);
    }

    private long GetMigrationCount()
    {
        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM schema_migrations";
        return (long)cmd.ExecuteScalar()!;
    }

    [Fact]
    public void RunAll_CreatesExpectedTables()
    {
        _sut.RunAll();

        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='prompts'";
        var result = cmd.ExecuteScalar();

        Assert.NotNull(result);
        Assert.Equal("prompts", result);
    }
}
