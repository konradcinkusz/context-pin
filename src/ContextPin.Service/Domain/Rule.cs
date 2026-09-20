namespace ContextPin.Service.Domain;

/// <summary>
/// One rule within a RuleSet. RuleId is the stable, human-meaningful identifier
/// (e.g. "P7"); Id is the row's own surrogate key, scoped to the RuleSet it
/// belongs to.
/// </summary>
public sealed record Rule(
    Guid Id,
    Guid RuleSetId,
    string RuleId,
    string Title,
    string Content,
    string Severity,
    int SortOrder);
