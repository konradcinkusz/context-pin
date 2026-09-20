namespace ContextPin.Service.Contracts;

// Request/response shapes for the HTTP API. Kept together in one file while the
// surface is this small; split out per-resource if it grows.

public sealed record ManifestRuleDto(string RuleId, string Title, string Content, string Severity);

public sealed record ManifestResponse(
    string Channel,
    string Version,
    string ContentHash,
    IReadOnlyList<ManifestRuleDto> Rules);

/// <summary>
/// A rule set's content with no pin/channel involved — what GET
/// /api/rulesets/{version} returns. Kept distinct from ManifestResponse rather
/// than reusing it with a placeholder Channel: that endpoint isn't resolving a
/// pin, so it has no channel to report.
/// </summary>
public sealed record RuleSetResponse(
    string Version,
    string ContentHash,
    string Status,
    IReadOnlyList<ManifestRuleDto> Rules);

public sealed record CreateRuleSetRuleDto(string RuleId, string Title, string Content, string Severity, int SortOrder);

public sealed record CreateRuleSetRequest(string Version, string Status, IReadOnlyList<CreateRuleSetRuleDto> Rules);

public sealed record CreateRuleSetResponse(string Version, string ContentHash, string Status);

public sealed record PinRequest(string Channel, string Version);

public sealed record PinResponse(string Owner, string Repo, string Channel, string Version);

public sealed record ReportFindingRequest(
    string CommitSha,
    string RuleId,
    string RuleSetVersion,
    string Severity,
    string Message);
