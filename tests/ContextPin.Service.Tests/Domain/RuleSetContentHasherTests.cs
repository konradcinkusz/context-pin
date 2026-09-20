using ContextPin.Service.Domain;

namespace ContextPin.Service.Tests.Domain;

/// <summary>
/// Pure unit tests — no database — for the hash every RuleSet's identity as
/// "content changed or didn't" depends on.
/// </summary>
public class RuleSetContentHasherTests
{
    private static NewRule Rule(string ruleId = "P1", string title = "Title", string content = "Content",
        string severity = "info", int sortOrder = 1) => new(ruleId, title, content, severity, sortOrder);

    [Fact]
    public void Same_input_produces_the_same_hash()
    {
        var rules = new[] { Rule("P1"), Rule("P2") };

        var first = RuleSetContentHasher.ComputeContentHash(rules);
        var second = RuleSetContentHasher.ComputeContentHash(rules);

        first.Should().Be(second);
    }

    [Fact]
    public void Hash_is_independent_of_the_order_rules_are_given_in()
    {
        var inOneOrder = new[] { Rule("P1"), Rule("P2"), Rule("P3") };
        var inAnotherOrder = new[] { Rule("P3"), Rule("P1"), Rule("P2") };

        RuleSetContentHasher.ComputeContentHash(inOneOrder)
            .Should().Be(RuleSetContentHasher.ComputeContentHash(inAnotherOrder));
    }

    [Fact]
    public void Hash_changes_when_a_rules_content_changes()
    {
        var before = new[] { Rule("P1", content: "old text") };
        var after = new[] { Rule("P1", content: "new text") };

        RuleSetContentHasher.ComputeContentHash(before)
            .Should().NotBe(RuleSetContentHasher.ComputeContentHash(after));
    }

    [Fact]
    public void Hash_changes_when_sort_order_changes_even_though_the_rule_set_is_content_identical_otherwise()
    {
        // Reordering rules is itself a content change worth invalidating a cached
        // manifest over — a consumer's rendered rule list would differ.
        var before = new[] { Rule("P1", sortOrder: 1), Rule("P2", sortOrder: 2) };
        var after = new[] { Rule("P1", sortOrder: 2), Rule("P2", sortOrder: 1) };

        RuleSetContentHasher.ComputeContentHash(before)
            .Should().NotBe(RuleSetContentHasher.ComputeContentHash(after));
    }

    [Fact]
    public void Adding_a_rule_changes_the_hash()
    {
        var before = new[] { Rule("P1") };
        var after = new[] { Rule("P1"), Rule("P2") };

        RuleSetContentHasher.ComputeContentHash(before)
            .Should().NotBe(RuleSetContentHasher.ComputeContentHash(after));
    }

    [Fact]
    public void An_empty_rule_list_hashes_stably_rather_than_throwing()
    {
        var act = () => RuleSetContentHasher.ComputeContentHash([]);

        act.Should().NotThrow();
        RuleSetContentHasher.ComputeContentHash([]).Should().Be(RuleSetContentHasher.ComputeContentHash([]));
    }

    [Fact]
    public void Moving_text_from_one_field_to_another_changes_the_hash()
    {
        // A boundary-confusion bug would make these collide: same characters,
        // different field. "ab" in Title + "" in Content must not hash the same
        // as "" in Title + "ab" in Content.
        var textInTitle = new[] { Rule("P1", title: "ab", content: "") };
        var textInContent = new[] { Rule("P1", title: "", content: "ab") };

        RuleSetContentHasher.ComputeContentHash(textInTitle)
            .Should().NotBe(RuleSetContentHasher.ComputeContentHash(textInContent));
    }
}
