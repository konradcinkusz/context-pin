using ContextPin.Service.Data;
using ContextPin.Service.Domain;

namespace ContextPin.Service.Tests.Data;

[Collection("Postgres")]
public class RuleSetRepositoryTests(PostgresFixture fixture)
{
    private readonly IRuleSetRepository _repository = new RuleSetRepository(fixture.DataSource);

    private static string UniqueVersion() => $"test-{Guid.NewGuid():N}";

    [Fact]
    public async Task CreateAsync_then_GetByVersionAsync_round_trips_the_rule_set_and_its_rules()
    {
        var version = UniqueVersion();
        var rules = new[]
        {
            new NewRule("P1", "Composition root", "The AppHost is the composition root.", "info", 1),
            new NewRule("P2", "Shared kernel", "Shared code is a kernel, not a domain.", "info", 2),
        };

        var created = await _repository.CreateAsync(version, contentHash: "abc123", status: "released", rules);

        created.Version.Should().Be(version);

        var fetched = await _repository.GetByVersionAsync(version);
        fetched.Should().NotBeNull();
        fetched!.Id.Should().Be(created.Id);
        fetched.ContentHash.Should().Be("abc123");
        fetched.Status.Should().Be("released");

        var fetchedRules = await _repository.GetRulesAsync(created.Id);
        fetchedRules.Should().HaveCount(2);
        fetchedRules.Select(r => r.RuleId).Should().Equal("P1", "P2");
        fetchedRules[0].Title.Should().Be("Composition root");
    }

    [Fact]
    public async Task GetByVersionAsync_returns_null_for_an_unknown_version()
    {
        var result = await _repository.GetByVersionAsync(UniqueVersion());

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetLatestAsync_returns_the_most_recently_created_rule_set()
    {
        await _repository.CreateAsync(UniqueVersion(), "hash-1", "released", []);
        // created_at has sub-millisecond precision in practice, but this margin
        // makes the ordering assertion below robust rather than merely likely.
        await Task.Delay(50);
        var second = await _repository.CreateAsync(UniqueVersion(), "hash-2", "released", []);

        var latest = await _repository.GetLatestAsync();

        latest.Should().NotBeNull();
        latest!.Id.Should().Be(second.Id);
    }

    [Fact]
    public async Task GetRulesAsync_returns_an_empty_list_for_a_rule_set_with_no_rules()
    {
        var created = await _repository.CreateAsync(UniqueVersion(), "hash", "draft", []);

        var rules = await _repository.GetRulesAsync(created.Id);

        rules.Should().BeEmpty();
    }
}
