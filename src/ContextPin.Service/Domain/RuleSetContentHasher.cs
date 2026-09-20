using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ContextPin.Service.Domain;

/// <summary>
/// Computes a RuleSet's content hash from its rules — never accepted as caller
/// input, so it cannot drift from what the rules actually say. Mirrors the
/// content-digest approach documented in konradcinkusz/architecture-standards
/// (catalog/versions.lock.json + build-marketplace.mjs): a hash over the content
/// alone, independent of what version string was chosen, so a version bump with
/// no content change and a content change with no version bump are both visible
/// by comparing this value across two RuleSets.
/// </summary>
public static class RuleSetContentHasher
{
    public static string ComputeContentHash(IReadOnlyList<NewRule> rules)
    {
        // Sorted by RuleId so the hash depends only on the set of rules and their
        // fields, never on the order the caller happened to list them in. Each
        // rule's fields are joined with a unit separator (0x1F) — a character that
        // cannot appear in ordinary text — so a value containing the field
        // delimiter itself cannot shift a boundary and collide with a different
        // rule. Rules are then joined with newline.
        var canonical = string.Join(
            '\n',
            rules
                .OrderBy(r => r.RuleId, StringComparer.Ordinal)
                .Select(r => string.Join(
                    '\u001f',
                    r.RuleId,
                    r.Title,
                    r.Content,
                    r.Severity,
                    r.SortOrder.ToString(CultureInfo.InvariantCulture))));

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(digest).ToLowerInvariant();
    }
}
