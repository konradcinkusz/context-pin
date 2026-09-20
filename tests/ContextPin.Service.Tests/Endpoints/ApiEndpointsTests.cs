using System.Net;
using System.Net.Http.Json;
using ContextPin.Service.Contracts;
using ContextPin.Service.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace ContextPin.Service.Tests.Endpoints;

/// <summary>
/// End-to-end HTTP tests against the real pipeline, including the admin-key
/// filter.
/// </summary>
/// <remarks>
/// The factory pins AdminApiKey to a known value via ConfigureAppConfiguration
/// rather than relying on whatever ambient environment variables happen to
/// supply. CI sets one value (see ci.yml); a local developer's shell might set
/// another, or none. This class's job is to verify the filter's behaviour
/// against a value it controls, not to guess the ambient configuration —
/// ConfigureAppConfiguration callbacks run after the host's own default
/// sources (appsettings, environment variables), so this override wins
/// regardless of what else is configured.
/// </remarks>
public class ApiEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string KnownAdminKey = "test-known-admin-key";

    private readonly HttpClient _client;

    public ApiEndpointsTests(WebApplicationFactory<Program> factory)
    {
        var pinned = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AdminApiKey"] = KnownAdminKey,
                })));

        _client = pinned.CreateClient();
    }

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    private static HttpRequestMessage AdminPost<T>(string url, T body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-Admin-Key", KnownAdminKey);
        return request;
    }

    private async Task<string> CreateRuleSetAsync(string version, string status = "released", string ruleId = "P1")
    {
        var response = await _client.SendAsync(AdminPost("/api/rulesets", new CreateRuleSetRequest(
            version, status, [new CreateRuleSetRuleDto(ruleId, "Title", "Content", "info", 1)])));
        response.EnsureSuccessStatusCode();
        return version;
    }

    private async Task PinAsync(string owner, string repo, string channel, string version)
    {
        var response = await _client.SendAsync(
            AdminPost($"/api/repos/{owner}/{repo}/pin", new PinRequest(channel, version)));
        response.EnsureSuccessStatusCode();
    }

    // ── POST /api/rulesets ──────────────────────────────────────────────────

    [Fact]
    public async Task CreateRuleSet_without_an_admin_key_is_unauthorized()
    {
        var response = await _client.PostAsJsonAsync("/api/rulesets", new CreateRuleSetRequest(
            Unique("v"), "draft", [new CreateRuleSetRuleDto("P1", "T", "C", "info", 1)]));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateRuleSet_with_the_wrong_admin_key_is_unauthorized()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/rulesets")
        {
            Content = JsonContent.Create(new CreateRuleSetRequest(
                Unique("v"), "draft", [new CreateRuleSetRuleDto("P1", "T", "C", "info", 1)])),
        };
        request.Headers.Add("X-Admin-Key", "definitely-not-the-key");

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateRuleSet_with_the_correct_admin_key_creates_it()
    {
        var version = Unique("v");

        var response = await _client.SendAsync(AdminPost("/api/rulesets", new CreateRuleSetRequest(
            version, "draft", [new CreateRuleSetRuleDto("P1", "Title", "Content", "info", 1)])));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<CreateRuleSetResponse>();
        body.Should().NotBeNull();
        body!.Version.Should().Be(version);
        body.ContentHash.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task CreateRuleSet_with_a_version_that_already_exists_returns_conflict()
    {
        var version = await CreateRuleSetAsync(Unique("v"));

        var response = await _client.SendAsync(AdminPost("/api/rulesets", new CreateRuleSetRequest(
            version, "draft", [new CreateRuleSetRuleDto("P2", "T", "C", "info", 1)])));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task CreateRuleSet_without_any_rules_is_rejected()
    {
        var response = await _client.SendAsync(AdminPost("/api/rulesets", new CreateRuleSetRequest(
            Unique("v"), "draft", [])));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── GET /api/rulesets/{version} ─────────────────────────────────────────

    [Fact]
    public async Task GetRuleSet_returns_the_published_content_anonymously()
    {
        var version = await CreateRuleSetAsync(Unique("v"), ruleId: "P1");

        var response = await _client.GetAsync($"/api/rulesets/{version}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<RuleSetResponse>();
        body!.Rules.Should().ContainSingle(r => r.RuleId == "P1");
    }

    [Fact]
    public async Task GetRuleSet_returns_not_found_for_an_unknown_version()
    {
        var response = await _client.GetAsync($"/api/rulesets/{Unique("missing")}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── POST /api/repos/{owner}/{repo}/pin ──────────────────────────────────

    [Fact]
    public async Task Pinning_a_repo_to_an_unknown_version_returns_not_found()
    {
        var response = await _client.SendAsync(AdminPost(
            $"/api/repos/{Unique("owner")}/repo/pin",
            new PinRequest("stable", Unique("missing-version"))));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Pin_without_an_admin_key_is_unauthorized()
    {
        var response = await _client.PostAsJsonAsync(
            $"/api/repos/{Unique("owner")}/repo/pin", new PinRequest("stable", Unique("v")));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── GET /api/repos/{owner}/{repo}/manifest ──────────────────────────────

    [Fact]
    public async Task Manifest_for_an_unpinned_repo_returns_not_found()
    {
        var response = await _client.GetAsync($"/api/repos/{Unique("owner")}/some-repo/manifest");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Pin_then_manifest_resolves_the_pinned_rule_set()
    {
        var owner = Unique("owner");
        const string repo = "context-pin";
        const string channel = "stable";
        var version = await CreateRuleSetAsync(Unique("v"), ruleId: "P7");
        await PinAsync(owner, repo, channel, version);

        var response = await _client.GetAsync($"/api/repos/{owner}/{repo}/manifest?channel={channel}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var manifest = await response.Content.ReadFromJsonAsync<ManifestResponse>();
        manifest.Should().NotBeNull();
        manifest!.Version.Should().Be(version);
        manifest.Channel.Should().Be(channel);
        manifest.Rules.Should().ContainSingle(r => r.RuleId == "P7");
        response.Headers.ETag.Should().NotBeNull();
    }

    [Fact]
    public async Task Manifest_defaults_to_the_stable_channel_when_none_is_given()
    {
        var owner = Unique("owner");
        const string repo = "context-pin";
        var version = await CreateRuleSetAsync(Unique("v"));
        await PinAsync(owner, repo, "stable", version);

        var response = await _client.GetAsync($"/api/repos/{owner}/{repo}/manifest");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var manifest = await response.Content.ReadFromJsonAsync<ManifestResponse>();
        manifest!.Channel.Should().Be("stable");
    }

    [Fact]
    public async Task Manifest_returns_not_modified_when_If_None_Match_matches_the_current_hash()
    {
        var owner = Unique("owner");
        const string repo = "context-pin";
        const string channel = "stable";
        var version = await CreateRuleSetAsync(Unique("v"));
        await PinAsync(owner, repo, channel, version);

        var first = await _client.GetAsync($"/api/repos/{owner}/{repo}/manifest?channel={channel}");
        var etag = first.Headers.ETag!.ToString();

        var second = new HttpRequestMessage(HttpMethod.Get, $"/api/repos/{owner}/{repo}/manifest?channel={channel}");
        second.Headers.Add("If-None-Match", etag);
        var secondResponse = await _client.SendAsync(second);

        secondResponse.StatusCode.Should().Be(HttpStatusCode.NotModified);
    }

    [Fact]
    public async Task Manifest_returns_a_fresh_body_when_If_None_Match_does_not_match()
    {
        var owner = Unique("owner");
        const string repo = "context-pin";
        const string channel = "stable";
        var version = await CreateRuleSetAsync(Unique("v"));
        await PinAsync(owner, repo, channel, version);

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/repos/{owner}/{repo}/manifest?channel={channel}");
        request.Headers.Add("If-None-Match", "\"some-other-hash-entirely\"");

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Different_channels_on_the_same_repo_can_resolve_different_versions()
    {
        var owner = Unique("owner");
        const string repo = "context-pin";
        var stableVersion = await CreateRuleSetAsync(Unique("v"), ruleId: "P-stable");
        var canaryVersion = await CreateRuleSetAsync(Unique("v"), ruleId: "P-canary");
        await PinAsync(owner, repo, "stable", stableVersion);
        await PinAsync(owner, repo, "canary", canaryVersion);

        var stableResponse = await _client.GetAsync($"/api/repos/{owner}/{repo}/manifest?channel=stable");
        var canaryResponse = await _client.GetAsync($"/api/repos/{owner}/{repo}/manifest?channel=canary");

        (await stableResponse.Content.ReadFromJsonAsync<ManifestResponse>())!.Version.Should().Be(stableVersion);
        (await canaryResponse.Content.ReadFromJsonAsync<ManifestResponse>())!.Version.Should().Be(canaryVersion);
    }

    // ── Findings ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReportFinding_then_GetFindings_round_trips_it()
    {
        var owner = Unique("owner");
        const string repo = "context-pin";

        var reportResponse = await _client.SendAsync(AdminPost(
            $"/api/repos/{owner}/{repo}/findings",
            new ReportFindingRequest("abc123", "P4", "1.0.0", "warning", "Schema is EnsureCreated, not migrated.")));

        reportResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var listResponse = await _client.GetAsync($"/api/repos/{owner}/{repo}/findings");

        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var findings = await listResponse.Content.ReadFromJsonAsync<List<Finding>>();
        findings.Should().ContainSingle(f => f.RuleId == "P4" && f.CommitSha == "abc123");
    }

    [Fact]
    public async Task ReportFinding_without_an_admin_key_is_unauthorized()
    {
        var response = await _client.PostAsJsonAsync(
            $"/api/repos/{Unique("owner")}/repo/findings",
            new ReportFindingRequest("sha", "P1", "1.0.0", "info", "msg"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetFindings_for_a_repo_with_none_returns_an_empty_list()
    {
        var response = await _client.GetAsync($"/api/repos/{Unique("owner")}/repo/findings");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var findings = await response.Content.ReadFromJsonAsync<List<Finding>>();
        findings.Should().BeEmpty();
    }
}
