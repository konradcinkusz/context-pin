using ContextPin.Service.Domain;
using Dapper;
using Npgsql;

namespace ContextPin.Service.Data;

public interface IRuleSetRepository
{
    /// <summary>Creates a new RuleSet with its rules, in one transaction.</summary>
    Task<RuleSet> CreateAsync(
        string version,
        string contentHash,
        string status,
        IReadOnlyList<NewRule> rules,
        CancellationToken cancellationToken = default);

    Task<RuleSet?> GetByVersionAsync(string version, CancellationToken cancellationToken = default);

    /// <summary>The most recently created RuleSet, or null if none exist yet.</summary>
    Task<RuleSet?> GetLatestAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Rule>> GetRulesAsync(Guid ruleSetId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Dapper over raw SQL, matching the rest of this service (see MigrationRunner
/// for why there is no ORM here).
/// </summary>
/// <remarks>
/// Column aliases below are written in PascalCase deliberately. Postgres folds an
/// unquoted alias to lowercase on the wire, and Dapper matches a result column to
/// a property or record constructor parameter case-insensitively — so
/// "content_hash AS ContentHash" arrives as the column "contenthash" and still
/// binds correctly to RuleSet.ContentHash. Without the alias, the raw column name
/// "content_hash" would NOT bind: Dapper's default matching is case-insensitive,
/// not underscore-aware, so it does not treat "content_hash" as equivalent to
/// "ContentHash" on its own.
/// </remarks>
public sealed class RuleSetRepository(NpgsqlDataSource dataSource) : IRuleSetRepository
{
    private const string SelectRuleSetColumns =
        "id AS Id, version AS Version, content_hash AS ContentHash, status AS Status, created_at AS CreatedAt";

    private const string SelectRuleColumns =
        "id AS Id, rule_set_id AS RuleSetId, rule_id AS RuleId, title AS Title, " +
        "content AS Content, severity AS Severity, sort_order AS SortOrder";

    public async Task<RuleSet> CreateAsync(
        string version,
        string contentHash,
        string status,
        IReadOnlyList<NewRule> rules,
        CancellationToken cancellationToken = default)
    {
        var ruleSetId = Guid.NewGuid();
        var createdAt = DateTime.UtcNow;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO rule_sets (id, version, content_hash, status, created_at)
            VALUES (@Id, @Version, @ContentHash, @Status, @CreatedAt);
            """,
            new { Id = ruleSetId, Version = version, ContentHash = contentHash, Status = status, CreatedAt = createdAt },
            transaction,
            cancellationToken: cancellationToken));

        foreach (var rule in rules)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO rules (id, rule_set_id, rule_id, title, content, severity, sort_order)
                VALUES (@Id, @RuleSetId, @RuleId, @Title, @Content, @Severity, @SortOrder);
                """,
                new
                {
                    Id = Guid.NewGuid(),
                    RuleSetId = ruleSetId,
                    rule.RuleId,
                    rule.Title,
                    rule.Content,
                    rule.Severity,
                    rule.SortOrder,
                },
                transaction,
                cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);

        return new RuleSet(ruleSetId, version, contentHash, status, createdAt);
    }

    public async Task<RuleSet?> GetByVersionAsync(string version, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<RuleSet>(new CommandDefinition(
            $"SELECT {SelectRuleSetColumns} FROM rule_sets WHERE version = @Version;",
            new { Version = version },
            cancellationToken: cancellationToken));
    }

    public async Task<RuleSet?> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<RuleSet>(new CommandDefinition(
            $"SELECT {SelectRuleSetColumns} FROM rule_sets ORDER BY created_at DESC LIMIT 1;",
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<Rule>> GetRulesAsync(Guid ruleSetId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<Rule>(new CommandDefinition(
            $"""
            SELECT {SelectRuleColumns}
            FROM rules
            WHERE rule_set_id = @RuleSetId
            ORDER BY sort_order, rule_id;
            """,
            new { RuleSetId = ruleSetId },
            cancellationToken: cancellationToken));
        return rows.ToList();
    }
}
