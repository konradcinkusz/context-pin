using ContextPin.Service.Contracts;
using ContextPin.Service.Data;
using ContextPin.Service.Domain;
using ContextPin.Service.Security;

namespace ContextPin.Service.Endpoints;

public static class ApiEndpoints
{
    private const string DefaultChannel = "stable";

    public static WebApplication MapApiEndpoints(this WebApplication app)
    {
        // ── Rule sets ────────────────────────────────────────────────────────

        // Publishes a new, immutable rule-set version. Admin-key gated: this is
        // how content enters the system at all, so it carries the same write
        // protection as pinning and reporting findings.
        app.MapPost("/api/rulesets", async (
            CreateRuleSetRequest request,
            IRuleSetRepository ruleSets,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Version))
                return Results.BadRequest(new { error = "version is required" });
            if (request.Rules.Count == 0)
                return Results.BadRequest(new { error = "at least one rule is required" });

            // Check-then-insert: the common case (a genuine duplicate publish
            // attempt) gets a clean 409. A concurrent publish of the exact same
            // version — not expected at this service's current scale of one
            // admin-key holder — would still be caught by the database's own
            // UNIQUE constraint on rule_sets.version, just as a 500 rather than
            // a 409. Not worth a retry/translate layer yet.
            var existing = await ruleSets.GetByVersionAsync(request.Version, cancellationToken);
            if (existing is not null)
                return Results.Conflict(new { error = $"rule set version '{request.Version}' already exists" });

            var newRules = request.Rules
                .Select(r => new NewRule(r.RuleId, r.Title, r.Content, r.Severity, r.SortOrder))
                .ToList();

            var created = await ruleSets.CreateAsync(request.Version, request.Status, newRules, cancellationToken);

            return Results.Created(
                $"/api/rulesets/{created.Version}",
                new CreateRuleSetResponse(created.Version, created.ContentHash, created.Status));
        })
        .AddEndpointFilter<RequireAdminKey>();

        // Anonymous: reading a published rule set's content is not a secret —
        // it is the thing every consumer exists to fetch.
        app.MapGet("/api/rulesets/{version}", async (
            string version,
            IRuleSetRepository ruleSets,
            CancellationToken cancellationToken) =>
        {
            var ruleSet = await ruleSets.GetByVersionAsync(version, cancellationToken);
            if (ruleSet is null)
                return Results.NotFound();

            var rules = await ruleSets.GetRulesAsync(ruleSet.Id, cancellationToken);

            return Results.Ok(new RuleSetResponse(
                ruleSet.Version,
                ruleSet.ContentHash,
                ruleSet.Status,
                rules.Select(ToDto).ToList()));
        });

        // ── Manifest ─────────────────────────────────────────────────────────

        app.MapGet("/api/repos/{owner}/{repo}/manifest", async (
            string owner,
            string repo,
            string? channel,
            IRepoPinRepository pins,
            IRuleSetRepository ruleSets,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var resolvedChannel = string.IsNullOrWhiteSpace(channel) ? DefaultChannel : channel;

            var pin = await pins.GetAsync(owner, repo, resolvedChannel, cancellationToken);
            if (pin is null)
            {
                return Results.NotFound(new
                {
                    error = $"No rule set pinned for {owner}/{repo}@{resolvedChannel}.",
                });
            }

            var ruleSet = await ruleSets.GetByIdAsync(pin.RuleSetId, cancellationToken);
            if (ruleSet is null)
            {
                // A pin pointing at a rule set that no longer resolves is a data
                // integrity problem, not a routine "not found" — rule_sets rows
                // are never deleted, so this should be unreachable. 500 rather
                // than 404 so it doesn't read as an ordinary missing pin.
                return Results.Problem(
                    $"Pin for {owner}/{repo}@{resolvedChannel} points at a rule set that no longer exists.",
                    statusCode: StatusCodes.Status500InternalServerError);
            }

            var etag = $"\"{ruleSet.ContentHash}\"";
            var ifNoneMatch = httpContext.Request.Headers["If-None-Match"].ToString();
            if (!string.IsNullOrEmpty(ifNoneMatch) && ifNoneMatch.Contains(etag, StringComparison.Ordinal))
            {
                return Results.StatusCode(StatusCodes.Status304NotModified);
            }

            httpContext.Response.Headers["ETag"] = etag;

            var rules = await ruleSets.GetRulesAsync(ruleSet.Id, cancellationToken);

            return Results.Ok(new ManifestResponse(
                resolvedChannel,
                ruleSet.Version,
                ruleSet.ContentHash,
                rules.Select(ToDto).ToList()));
        });

        // ── Pinning ──────────────────────────────────────────────────────────

        app.MapPost("/api/repos/{owner}/{repo}/pin", async (
            string owner,
            string repo,
            PinRequest request,
            IRuleSetRepository ruleSets,
            IRepoPinRepository pins,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Channel))
                return Results.BadRequest(new { error = "channel is required" });
            if (string.IsNullOrWhiteSpace(request.Version))
                return Results.BadRequest(new { error = "version is required" });

            var ruleSet = await ruleSets.GetByVersionAsync(request.Version, cancellationToken);
            if (ruleSet is null)
                return Results.NotFound(new { error = $"rule set version '{request.Version}' does not exist" });

            await pins.UpsertAsync(owner, repo, request.Channel, ruleSet.Id, cancellationToken);

            return Results.Ok(new PinResponse(owner, repo, request.Channel, request.Version));
        })
        .AddEndpointFilter<RequireAdminKey>();

        // ── Findings ─────────────────────────────────────────────────────────

        app.MapPost("/api/repos/{owner}/{repo}/findings", async (
            string owner,
            string repo,
            ReportFindingRequest request,
            IFindingRepository findings,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.RuleId) || string.IsNullOrWhiteSpace(request.CommitSha))
                return Results.BadRequest(new { error = "ruleId and commitSha are required" });

            var finding = new Finding(
                Guid.NewGuid(), owner, repo, request.CommitSha, request.RuleId,
                request.RuleSetVersion, request.Severity, request.Message, DateTime.UtcNow);

            await findings.AddAsync(finding, cancellationToken);

            return Results.Created($"/api/repos/{owner}/{repo}/findings/{finding.Id}", finding);
        })
        .AddEndpointFilter<RequireAdminKey>();

        // Anonymous for now, matching the manifest read: a repo's own findings
        // are not a secret from that repo's own CI. Revisit alongside real
        // per-repo service tokens.
        app.MapGet("/api/repos/{owner}/{repo}/findings", async (
            string owner,
            string repo,
            IFindingRepository findings,
            CancellationToken cancellationToken) =>
        {
            var results = await findings.GetByOwnerAndRepoAsync(owner, repo, cancellationToken);
            return Results.Ok(results);
        });

        return app;
    }

    private static ManifestRuleDto ToDto(Rule rule) =>
        new(rule.RuleId, rule.Title, rule.Content, rule.Severity);
}
