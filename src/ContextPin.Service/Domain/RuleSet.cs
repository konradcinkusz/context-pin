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
    // DateTime (UTC), not DateTimeOffset: Postgres's timestamptz has no per-value
    // offset to preserve — it is always UTC internally — and Npgsql reads it back
    // as DateTime with Kind=Utc. Dapper's constructor-based materialization for
    // records requires the parameter type to match what the reader reports
    // exactly; a DateTimeOffset parameter here throws
    // "no constructor matching (..., DateTime createdat)" at read time rather than
    // converting. Confirmed by CI, not assumed.
    DateTime CreatedAt);
