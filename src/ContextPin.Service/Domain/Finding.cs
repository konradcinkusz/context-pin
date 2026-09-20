namespace ContextPin.Service.Domain;

/// <summary>
/// A reported violation of a rule in a consuming repo at a given commit.
/// Append-only: nothing here is ever updated or deleted, only queried.
/// </summary>
public sealed record Finding(
    Guid Id,
    string Owner,
    string Repo,
    string CommitSha,
    string RuleId,
    string RuleSetVersion,
    string Severity,
    string Message,
    DateTimeOffset ReportedAt);
