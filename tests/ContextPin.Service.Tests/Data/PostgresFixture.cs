using ContextPin.Service.Data;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace ContextPin.Service.Tests.Data;

/// <summary>
/// One NpgsqlDataSource, migrated once, shared by every test class in the
/// "Postgres" collection.
/// </summary>
/// <remarks>
/// Tests isolate themselves with random version/owner/repo values rather than
/// resetting the schema between tests — cheaper, and every assertion is scoped
/// to rows a given test itself created. xunit runs tests within one collection
/// sequentially by default, which is what makes GetLatestAsync's test
/// (whichever row has the newest timestamp) safe to assert on without a shared
/// database reset.
/// </remarks>
public sealed class PostgresFixture : IAsyncLifetime
{
    public NpgsqlDataSource DataSource { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("CONTEXTPIN_TEST_CONNECTION_STRING")
            ?? throw new InvalidOperationException(
                "CONTEXTPIN_TEST_CONNECTION_STRING is not set. These tests need a real " +
                "Postgres instance — run `docker compose up -d`, or see the postgres " +
                "service container in .github/workflows/ci.yml.");

        DataSource = NpgsqlDataSource.Create(connectionString);

        var migrationsDirectory = Path.Combine(
            RepositoryPaths.Root, "src", "ContextPin.Service", "Migrations");
        await MigrationRunner.ApplyPendingAsync(DataSource, migrationsDirectory, NullLogger.Instance);
    }

    public async Task DisposeAsync()
    {
        await DataSource.DisposeAsync();
    }
}

[CollectionDefinition("Postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;
