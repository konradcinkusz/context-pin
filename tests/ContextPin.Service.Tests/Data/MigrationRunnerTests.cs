using ContextPin.Service.Data;
using Microsoft.Extensions.Logging.Abstractions;

namespace ContextPin.Service.Tests.Data;

[Collection("Postgres")]
public class MigrationRunnerTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Applying_migrations_a_second_time_is_a_no_op()
    {
        // The fixture already applied every migration once during InitializeAsync.
        // Re-running must not throw — in particular, it must not attempt
        // 001_init.sql's CREATE TABLE statements again just because they carry
        // IF NOT EXISTS; it must skip the file entirely because
        // schema_migrations already names it. That's the actual thing this
        // class exists to protect: the tracking table deciding what runs, not
        // the SQL's own defensiveness.
        var migrationsDirectory = Path.Combine(
            RepositoryPaths.Root, "src", "ContextPin.Service", "Migrations");

        var act = () => MigrationRunner.ApplyPendingAsync(
            fixture.DataSource, migrationsDirectory, NullLogger.Instance);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task An_empty_migrations_directory_is_a_no_op_not_an_error()
    {
        var emptyDirectory = Path.Combine(Path.GetTempPath(), $"contextpin-empty-{Guid.NewGuid():N}");
        Directory.CreateDirectory(emptyDirectory);
        try
        {
            var act = () => MigrationRunner.ApplyPendingAsync(
                fixture.DataSource, emptyDirectory, NullLogger.Instance);

            await act.Should().NotThrowAsync();
        }
        finally
        {
            Directory.Delete(emptyDirectory);
        }
    }

    [Fact]
    public async Task A_missing_migrations_directory_is_a_no_op_not_an_error()
    {
        var missingDirectory = Path.Combine(Path.GetTempPath(), $"contextpin-missing-{Guid.NewGuid():N}");

        var act = () => MigrationRunner.ApplyPendingAsync(
            fixture.DataSource, missingDirectory, NullLogger.Instance);

        await act.Should().NotThrowAsync();
    }
}
