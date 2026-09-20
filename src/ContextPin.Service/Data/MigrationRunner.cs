using Npgsql;

namespace ContextPin.Service.Data;

/// <summary>
/// Applies pending numbered .sql files in filename order, tracking what has
/// already run in a schema_migrations table it creates itself.
/// </summary>
/// <remarks>
/// Chosen over EF Core migrations: generating those requires `dotnet ef
/// migrations add`, which needs the SDK's design-time tooling — not available
/// when this was written. Plain numbered SQL files plus a tracking table give
/// the same guarantee (schema is migrated, never "ensured") without that
/// dependency, at the cost of writing SQL by hand instead of a fluent builder.
/// </remarks>
public static class MigrationRunner
{
    public static async Task ApplyPendingAsync(
        NpgsqlDataSource dataSource,
        string migrationsDirectory,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        await using (var createTrackingTable = connection.CreateCommand())
        {
            createTrackingTable.CommandText = """
                CREATE TABLE IF NOT EXISTS schema_migrations (
                    filename   TEXT PRIMARY KEY,
                    applied_at TIMESTAMPTZ NOT NULL DEFAULT now()
                );
                """;
            await createTrackingTable.ExecuteNonQueryAsync(cancellationToken);
        }

        var applied = new HashSet<string>(StringComparer.Ordinal);
        await using (var selectApplied = connection.CreateCommand())
        {
            selectApplied.CommandText = "SELECT filename FROM schema_migrations;";
            await using var reader = await selectApplied.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                applied.Add(reader.GetString(0));
            }
        }

        if (!Directory.Exists(migrationsDirectory))
        {
            logger.LogWarning(
                "Migrations directory {Directory} does not exist; nothing to apply.",
                migrationsDirectory);
            return;
        }

        var pending = Directory.GetFiles(migrationsDirectory, "*.sql")
            .Select(Path.GetFileName)
            .Where(name => name is not null && !applied.Contains(name))
            .Select(name => name!)
            // Ordinal string sort puts "001_" before "002_" before "010_" — filenames
            // are zero-padded specifically so this holds past nine migrations.
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        foreach (var filename in pending)
        {
            var sql = await File.ReadAllTextAsync(
                Path.Combine(migrationsDirectory, filename), cancellationToken);

            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            try
            {
                await using (var applyMigration = connection.CreateCommand())
                {
                    applyMigration.Transaction = transaction;
                    applyMigration.CommandText = sql;
                    await applyMigration.ExecuteNonQueryAsync(cancellationToken);
                }

                await using (var recordMigration = connection.CreateCommand())
                {
                    recordMigration.Transaction = transaction;
                    recordMigration.CommandText =
                        "INSERT INTO schema_migrations (filename) VALUES (@filename);";
                    recordMigration.Parameters.AddWithValue("filename", filename);
                    await recordMigration.ExecuteNonQueryAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
                logger.LogInformation("Applied migration {Filename}.", filename);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }
    }
}
