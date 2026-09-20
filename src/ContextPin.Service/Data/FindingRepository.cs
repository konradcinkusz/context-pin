using ContextPin.Service.Domain;
using Dapper;
using Npgsql;

namespace ContextPin.Service.Data;

public interface IFindingRepository
{
    Task AddAsync(Finding finding, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Finding>> GetByOwnerAndRepoAsync(
        string owner, string repo, CancellationToken cancellationToken = default);
}

public sealed class FindingRepository(NpgsqlDataSource dataSource) : IFindingRepository
{
    private const string SelectColumns =
        "id AS Id, owner AS Owner, repo AS Repo, commit_sha AS CommitSha, rule_id AS RuleId, " +
        "rule_set_version AS RuleSetVersion, severity AS Severity, message AS Message, " +
        "reported_at AS ReportedAt";

    public async Task AddAsync(Finding finding, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO findings
                (id, owner, repo, commit_sha, rule_id, rule_set_version, severity, message, reported_at)
            VALUES
                (@Id, @Owner, @Repo, @CommitSha, @RuleId, @RuleSetVersion, @Severity, @Message, @ReportedAt);
            """,
            finding,
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<Finding>> GetByOwnerAndRepoAsync(
        string owner, string repo, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<Finding>(new CommandDefinition(
            $"SELECT {SelectColumns} FROM findings WHERE owner = @Owner AND repo = @Repo ORDER BY reported_at;",
            new { Owner = owner, Repo = repo },
            cancellationToken: cancellationToken));
        return rows.ToList();
    }
}
