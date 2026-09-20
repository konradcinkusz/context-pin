using ContextPin.Service.Data;

namespace ContextPin.Service.Tests.Data;

[Collection("Postgres")]
public class RepoPinRepositoryTests(PostgresFixture fixture)
{
    private readonly IRuleSetRepository _ruleSets = new RuleSetRepository(fixture.DataSource);
    private readonly IRepoPinRepository _pins = new RepoPinRepository(fixture.DataSource);

    private static string UniqueOwner() => $"owner-{Guid.NewGuid():N}";

    [Fact]
    public async Task UpsertAsync_then_GetAsync_round_trips_the_pin()
    {
        var ruleSet = await _ruleSets.CreateAsync($"test-{Guid.NewGuid():N}", "hash", "released", []);
        var owner = UniqueOwner();

        await _pins.UpsertAsync(owner, "context-pin", "stable", ruleSet.Id);

        var pin = await _pins.GetAsync(owner, "context-pin", "stable");

        pin.Should().NotBeNull();
        pin!.RuleSetId.Should().Be(ruleSet.Id);
        pin.Owner.Should().Be(owner);
        pin.Channel.Should().Be("stable");
    }

    [Fact]
    public async Task UpsertAsync_moves_an_existing_pin_rather_than_duplicating_it()
    {
        var first = await _ruleSets.CreateAsync($"test-{Guid.NewGuid():N}", "hash-1", "released", []);
        var second = await _ruleSets.CreateAsync($"test-{Guid.NewGuid():N}", "hash-2", "released", []);
        var owner = UniqueOwner();

        await _pins.UpsertAsync(owner, "context-pin", "stable", first.Id);
        await _pins.UpsertAsync(owner, "context-pin", "stable", second.Id);

        var pin = await _pins.GetAsync(owner, "context-pin", "stable");

        pin.Should().NotBeNull();
        pin!.RuleSetId.Should().Be(second.Id, "the second upsert should move the pin, not create a second row");
    }

    [Fact]
    public async Task Channels_for_the_same_repo_are_independent()
    {
        var stable = await _ruleSets.CreateAsync($"test-{Guid.NewGuid():N}", "hash-stable", "released", []);
        var canary = await _ruleSets.CreateAsync($"test-{Guid.NewGuid():N}", "hash-canary", "released", []);
        var owner = UniqueOwner();

        await _pins.UpsertAsync(owner, "context-pin", "stable", stable.Id);
        await _pins.UpsertAsync(owner, "context-pin", "canary", canary.Id);

        (await _pins.GetAsync(owner, "context-pin", "stable"))!.RuleSetId.Should().Be(stable.Id);
        (await _pins.GetAsync(owner, "context-pin", "canary"))!.RuleSetId.Should().Be(canary.Id);
    }

    [Fact]
    public async Task GetAsync_returns_null_when_no_pin_exists()
    {
        var pin = await _pins.GetAsync(UniqueOwner(), "repo", "stable");

        pin.Should().BeNull();
    }
}
