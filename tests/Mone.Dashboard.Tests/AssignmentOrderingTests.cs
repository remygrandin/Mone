using Mone.Dashboard.Helpers;
using Xunit;

namespace Mone.Dashboard.Tests;

// Proves the HostDetail Assignment-tab list ordering (S08): items sort alphabetically by their
// resolved display name, case-insensitively, via the AssignmentOrdering.ByName chokepoint the
// markup's @foreach loops call.
public class AssignmentOrderingTests
{
    private sealed record Item(string Name);

    [Fact]
    public void ByName_SortsAlphabetically()
    {
        var input = new[] { new Item("charlie"), new Item("alpha"), new Item("bravo") };

        var ordered = AssignmentOrdering.ByName(input, i => i.Name);

        Assert.Equal(new[] { "alpha", "bravo", "charlie" }, ordered.Select(i => i.Name));
    }

    [Fact]
    public void ByName_IsCaseInsensitive()
    {
        var input = new[] { new Item("Zebra"), new Item("apple"), new Item("Banana") };

        var ordered = AssignmentOrdering.ByName(input, i => i.Name);

        Assert.Equal(new[] { "apple", "Banana", "Zebra" }, ordered.Select(i => i.Name));
    }

    [Fact]
    public void ByName_IsStableForEqualKeys()
    {
        var first = new Item("same");
        var second = new Item("same");

        var ordered = AssignmentOrdering.ByName(new[] { first, second }, i => i.Name);

        Assert.Same(first, ordered[0]);
        Assert.Same(second, ordered[1]);
    }

    [Fact]
    public void ByName_HandlesEmptyInput()
    {
        Assert.Empty(AssignmentOrdering.ByName(Array.Empty<Item>(), i => i.Name));
    }

    private sealed record Labeled(string Label, string Name);

    [Fact]
    public void ByName_UsesProvidedSelector_NotToString()
    {
        // Label order is the reverse of Name order, and the record's ToString leads with Label,
        // so a ToString-based sort would invert the result. Asserting Name order proves the
        // nameSelector drives the ordering.
        var input = new[]
        {
            new Labeled(Label: "3", Name: "apple"),
            new Labeled(Label: "2", Name: "banana"),
            new Labeled(Label: "1", Name: "cherry"),
        };

        var ordered = AssignmentOrdering.ByName(input, i => i.Name);

        Assert.Equal(new[] { "apple", "banana", "cherry" }, ordered.Select(i => i.Name));
    }

    private sealed record EnItem(string Name, bool Enabled);

    [Fact]
    public void ByNameEnabledFirst_SortsDisabledLast_AlphabeticalWithinGroups()
    {
        var input = new[]
        {
            new EnItem("charlie", Enabled: true),
            new EnItem("delta", Enabled: false),
            new EnItem("alpha", Enabled: true),
            new EnItem("bravo", Enabled: false),
        };

        var ordered = AssignmentOrdering.ByNameEnabledFirst(input, i => i.Name, i => i.Enabled);

        Assert.Equal(new[] { "alpha", "charlie", "bravo", "delta" }, ordered.Select(i => i.Name));
    }

    [Fact]
    public void ByNameEnabledFirst_GroupsAllEnabledBeforeAllDisabled()
    {
        var input = new[]
        {
            new EnItem("zulu", Enabled: false),
            new EnItem("alpha", Enabled: true),
            new EnItem("mike", Enabled: false),
            new EnItem("yankee", Enabled: true),
        };

        var ordered = AssignmentOrdering.ByNameEnabledFirst(input, i => i.Name, i => i.Enabled);

        var enabledFlags = ordered.Select(i => i.Enabled).ToList();
        var firstDisabled = enabledFlags.IndexOf(false);
        Assert.DoesNotContain(true, enabledFlags.Skip(firstDisabled));
    }

    [Fact]
    public void ByNameEnabledFirst_IsStableForEqualKeys()
    {
        var first = new EnItem("same", Enabled: true);
        var second = new EnItem("same", Enabled: true);

        var ordered = AssignmentOrdering.ByNameEnabledFirst(
            new[] { first, second }, i => i.Name, i => i.Enabled);

        Assert.Same(first, ordered[0]);
        Assert.Same(second, ordered[1]);
    }

    [Fact]
    public void WithDisabledSuffix_LeavesEnabledNameUnchanged()
    {
        Assert.Equal("web-probe", AssignmentOrdering.WithDisabledSuffix("web-probe", enabled: true));
    }

    [Fact]
    public void WithDisabledSuffix_AppendsSuffixWhenDisabled()
    {
        Assert.Equal("web-probe (disabled)", AssignmentOrdering.WithDisabledSuffix("web-probe", enabled: false));
    }
}
