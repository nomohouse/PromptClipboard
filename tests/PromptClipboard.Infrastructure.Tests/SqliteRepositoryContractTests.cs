namespace PromptClipboard.Infrastructure.Tests;

using Microsoft.Data.Sqlite;
using PromptClipboard.Domain.Interfaces;
using PromptClipboard.Infrastructure.Persistence;
using PromptClipboard.TestContracts;
using Serilog;

public sealed class SqliteRepositoryContractTests : PromptRepositoryContractTests, IDisposable
{
    private readonly SqliteConnection _sentinel;
    private readonly SqliteConnectionFactory _factory;

    public SqliteRepositoryContractTests()
    {
        var dbName = $"file:contract_{Guid.NewGuid():N}?mode=memory&cache=shared";
        _sentinel = new SqliteConnection($"Data Source={dbName}");
        _sentinel.Open();
        _factory = new SqliteConnectionFactory(_sentinel);

        var log = new LoggerConfiguration().CreateLogger();
        var runner = new MigrationRunner(_factory, log);
        runner.RunAll();
    }

    protected override IPromptRepository CreateRepository() =>
        new SqlitePromptRepository(_factory);

    public void Dispose() => _sentinel.Dispose();
}
