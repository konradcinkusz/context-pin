namespace ContextPin.Service.Domain;

/// <summary>
/// Which RuleSet a given (Owner, Repo, Channel) resolves to right now. This is a
/// pointer, updated in place — the RuleSet it points at is what's immutable, not
/// this row.
/// </summary>
public sealed record RepoPin(
    Guid Id,
    string Owner,
    string Repo,
    string Channel,
    Guid RuleSetId,
    DateTimeOffset UpdatedAt);
