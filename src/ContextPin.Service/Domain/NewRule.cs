namespace ContextPin.Service.Domain;

/// <summary>
/// A rule as given to <see cref="Data.IRuleSetRepository.CreateAsync"/>, before it
/// has an Id or a RuleSetId — both are assigned by the repository at insert time.
/// Kept separate from <see cref="Rule"/> (the read model, always fully populated
/// from storage) rather than reusing it with two fields the caller has to fill
/// with a placeholder it knows will be discarded.
/// </summary>
public sealed record NewRule(
    string RuleId,
    string Title,
    string Content,
    string Severity,
    int SortOrder);
