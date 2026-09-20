using ContextPin.Service.Data;
using ContextPin.Service.Domain;

namespace ContextPin.Service.Tests.Data;

[Collection("Postgres")]
public class FindingRepositoryTests(PostgresFixture fixture)
{
    private readonly IFindingRepository _findings = new FindingRepository(fixture.DataSource);

    [Fact]
    public async Task AddAsync_persists_the_finding_and_it_is_retrievable_by_owner_and_repo()
    {
        var owner = $"owner-{Guid.NewGuid():N}";
        var finding = new Finding(
            Guid.NewGuid(), owner, "context-pin", "abc123def", "P4", "1.0.0",
            "warning", "Schema is EnsureCreated, not migrated.", DateTime.UtcNow);

        await _findings.AddAsync(finding);

        var stored = await _findings.GetByOwnerAndRepoAsync(owner, "context-pin");

        stored.Should().ContainSingle();
        stored[0].RuleId.Should().Be("P4");
        stored[0].CommitSha.Should().Be("abc123def");
        stored[0].Message.Should().Be("Schema is EnsureCreated, not migrated.");
    }

    [Fact]
    public async Task GetByOwnerAndRepoAsync_does_not_return_another_repos_findings()
    {
        var owner = $"owner-{Guid.NewGuid():N}";
        await _findings.AddAsync(new Finding(
            Guid.NewGuid(), owner, "repo-a", "sha1", "P1", "1.0.0", "info", "in repo-a", DateTime.UtcNow));
        await _findings.AddAsync(new Finding(
            Guid.NewGuid(), owner, "repo-b", "sha2", "P2", "1.0.0", "info", "in repo-b", DateTime.UtcNow));

        var forRepoA = await _findings.GetByOwnerAndRepoAsync(owner, "repo-a");

        forRepoA.Should().ContainSingle(f => f.Message == "in repo-a");
    }

    [Fact]
    public async Task GetByOwnerAndRepoAsync_returns_empty_for_a_repo_with_no_findings()
    {
        var result = await _findings.GetByOwnerAndRepoAsync($"owner-{Guid.NewGuid():N}", "nothing-here");

        result.Should().BeEmpty();
    }
}
