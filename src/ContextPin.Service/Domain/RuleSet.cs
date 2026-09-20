namespace ContextPin.Service.Domain;

/// <summary>
/// A versioned, content-hashed bundle of rules. Immutable once created: a change
/// to a rule's content always produces a new RuleSet with a new version and a new
/// hash, never an update to an existing one — a RepoPin or Finding that cites a
/// RuleSet by id keeps meaning the same thing forever.
/// </summary>
public sealed record RuleSet(
    Guid Id,
    string Version,
    string ContentHash,
    string Status,
    DateTimeOffset CreatedAt);
