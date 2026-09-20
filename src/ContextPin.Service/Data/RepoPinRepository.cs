using ContextPin.Service.Domain;
using Dapper;
using Npgsql;

namespace ContextPin.Service.Data;

public interface IRepoPinRepository
{
    /// <summary>
    /// Points (owner, repo, channel) at a RuleSet, creating the pin if it doesn't
    /// exist or moving it if it does. Never creates a second row for the same
    /// (owner, repo, channel).
    /// </summary>
    Task UpsertAsync(
        string owner, string repo, string channel, Guid ruleSetId,
        CancellationToken cancellationToken = default);

    Task<RepoPin?> GetAsync(
        string owner, string repo, string channel,
        CancellationToken cancellationToken = default);
}

public sealed class RepoPinRepository(NpgsqlDataSource dataSource) : IRepoPinRepository
{
    public async Task UpsertAsync(
        string owner, string repo, string channel, Guid ruleSetId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO repo_pins (id, owner, repo, channel, rule_set_id, updated_at)
            VALUES (@Id, @Owner, @Repo, @Channel, @RuleSetId, @UpdatedAt)
            ON CONFLICT (owner, repo, channel)
            DO UPDATE SET rule_set_id = EXCLUDED.rule_set_id, updated_at = EXCLUDED.updated_at;
            """,
            new
            {
                Id = Guid.NewGuid(),
                Owner = owner,
                Repo = repo,
                Channel = channel,
                RuleSetId = ruleSetId,
                UpdatedAt = DateTime.UtcNow,
            },
            cancellationToken: cancellationToken));
    }

    public async Task<RepoPin?> GetAsync(
        string owner, string repo, string channel,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<RepoPin>(new CommandDefinition(
            """
            SELECT id AS Id, owner AS Owner, repo AS Repo, channel AS Channel,
                   rule_set_id AS RuleSetId, updated_at AS UpdatedAt
            FROM repo_pins
            WHERE owner = @Owner AND repo = @Repo AND channel = @Channel;
            """,
            new { Owner = owner, Repo = repo, Channel = channel },
            cancellationToken: cancellationToken));
    }
}
