using PawnAppraiser.Core;
using Xunit;

namespace PawnAppraiser.Core.Tests;

public class EarlyCompletionTests
{
    [Theory]
    [InlineData("packaging", "opened")][InlineData("packaging", "missing")]
    [InlineData("date", "missing")][InlineData("date", "invalid")]
    [InlineData("factory", "missing")][InlineData("factory", "invalid")]
    [InlineData("edition", "sesame_only")][InlineData("edition", "pickles_only")]
    [InlineData("brand", "missing")][InlineData("brand", "wrong")]
    [InlineData("ribwich", "wrong")]
    public void AnySingleProvenDefectImmediatelyCompletes(string group, string option)
    {
        var r = new ValuationEngine().Evaluate(RibwichTests.Item, ValuationTests.State((group, [option])));
        Assert.Equal(ValuationStatus.Complete, r.Status); Assert.Equal(40, r.FinalPrice);
        Assert.True(r.CanCopy); Assert.True(r.IsEarlyComplete); Assert.Null(r.RawPrice);
        Assert.Single(r.AppliedOverrides); Assert.Empty(r.AppliedBonuses);
    }

    [Fact]
    public void All40320PartialAndCompleteSelectionsMatchIndependentDecisionTable()
    {
        var choices = new[]
        {
            new[] { "packaging", "", "intact", "opened", "missing" },
            new[] { "date", "", "first_month", "first_other", "second_other", "last_month", "missing", "invalid" },
            new[] { "factory", "", "high", "normal", "low", "missing", "invalid" },
            new[] { "edition", "", "regular", "deluxe", "sesame_only", "pickles_only" },
            new[] { "patties", "", "single", "double" },
            new[] { "brand", "", "correct", "missing", "wrong" },
            new[] { "ribwich", "", "missing", "correct", "wrong" }
        };
        var item = RibwichTests.Item; var engine = new ValuationEngine(); int count = 0;
        foreach(var packaging in choices[0].Skip(1))
        foreach(var date in choices[1].Skip(1))
        foreach(var factory in choices[2].Skip(1))
        foreach(var edition in choices[3].Skip(1))
        foreach(var patties in choices[4].Skip(1))
        foreach(var brand in choices[5].Skip(1))
        foreach(var sticker in choices[6].Skip(1))
        {
            string[] values = [packaging, date, factory, edition, patties, brand, sticker];
            var selected = values.Select((v, i) => (Group: choices[i][0], Value: v))
                .Where(x => x.Value.Length > 0).ToDictionary(x => x.Group, x => (IReadOnlySet<string>)new HashSet<string> { x.Value });
            bool first = date is "first_month" or "first_other";
            bool second = date is "second_other" or "last_month";
            // Manual truth table, independently written without interpreting JSON rules.
            bool defect = packaging is "opened" or "missing" || date is "missing" or "invalid" ||
                factory is "missing" or "invalid" || edition is "sesame_only" or "pickles_only" ||
                brand is "missing" or "wrong" || sticker == "wrong" ||
                first && sticker == "correct" || second && sticker == "missing";
            bool complete = values.All(v => v.Length > 0);
            var result = engine.Evaluate(item, new(selected));
            if (defect)
            {
                Assert.Equal(40, result.FinalPrice); Assert.True(result.CanCopy);
                Assert.Equal(!complete, result.IsEarlyComplete);
                Assert.Equal(!complete, result.RawPrice is null);
            }
            else if (!complete)
            {
                Assert.Equal(ValuationStatus.Incomplete, result.Status); Assert.False(result.CanCopy);
                Assert.False(result.IsEarlyComplete); Assert.Null(result.FinalPrice);
            }
            else
            {
                long expected = 150 + (first ? 80 : 0) + (date is "first_month" or "last_month" ? 50 : 0) +
                    (factory == "high" ? 50 : factory == "low" ? -50 : 0) +
                    (edition == "deluxe" ? 300 : 0) + (patties == "double" ? 800 : 0);
                Assert.Equal(expected, result.FinalPrice); Assert.True(result.CanCopy); Assert.False(result.IsEarlyComplete);
            }
            count++;
        }
        Assert.Equal(40320, count);
    }

    [Fact]
    public async Task CorrectionResetAndItemSwitchNeverKeepAStaleFixedPrice()
    {
        var s = new AppraiserSession([ValuationTests.Hand, RibwichTests.Item], new()); var clipboard = new MemoryClipboard();
        s.SwitchItem("ribwich"); Assert.False(s.Result.CanCopy);
        s.Toggle("packaging", "opened"); Assert.Equal(40, s.Result.FinalPrice);
        Assert.True(await s.CopyAsync(clipboard)); Assert.Equal("40", clipboard.Text);
        s.SwitchItem("bionic_hand"); Assert.False(s.Result.CanCopy);
        s.SwitchItem("ribwich"); Assert.Equal(40, s.Result.FinalPrice);
        s.Toggle("packaging", "intact"); Assert.False(s.Result.CanCopy);
        Assert.False(await s.CopyAsync(clipboard)); Assert.Equal("40", clipboard.Text);
        foreach(var (group, values) in RibwichTests.State().SelectedOptions) s.Toggle(group, values.Single());
        Assert.Equal(150, s.Result.FinalPrice); Assert.False(s.Result.IsEarlyComplete);
        s.ResetCurrent(); Assert.False(s.Result.CanCopy);
        s.Toggle("brand", "wrong"); Assert.Equal(40, s.Result.FinalPrice);
        s.ResetCurrent(); Assert.False(s.Result.CanCopy); Assert.Empty(s.CurrentState!.Snapshot.SelectedOptions);
    }

    private static ItemDefinition Generic(bool early = true, bool bounds = false, params RuleDefinition[] more)
    {
        var hand = ValuationTests.Hand;
        return hand with { Rules = [new("fixed", "固定赝品价", "absoluteOverride", 40,
            new("selected", "material", "gold"), 10, bounds, early), .. more] };
    }
    private static SelectionState Gold => ValuationTests.State(("material", ["gold"]));

    [Fact]
    public void EarlyCompletionIsOptInAndOldDefinitionsRemainIncomplete()
    {
        var engine = new ValuationEngine();
        Assert.Equal(ValuationStatus.Incomplete, engine.Evaluate(Generic(false), Gold).Status);
        Assert.Equal(ValuationStatus.Incomplete, engine.Evaluate(ValuationTests.Hand, Gold).Status);
        Assert.Equal(ValuationStatus.Incomplete, engine.Evaluate(ValuationTests.Relic, SelectionState.Empty).Status);
        Assert.Equal(40, engine.Evaluate(Generic(), Gold).FinalPrice);
        Assert.Equal(50, engine.Evaluate(Generic(bounds: true), Gold).FinalPrice);
        var capped = Generic() with { Pricing = new(100, 10, 30),
            Rules = [Generic(bounds: true).Rules[0]] };
        Assert.Equal(30, engine.Evaluate(capped, Gold).FinalPrice);
    }

    [Theory]
    [InlineData("all", false)][InlineData("any", true)][InlineData("not", false)]
    public void UnknownRequiredGroupIsNotTheSameAsFalse(string op, bool expected)
    {
        var gold = new Condition("selected", "material", "gold");
        var unknown = new Condition("selected", "condition", "intact");
        var condition = op == "not" ? new Condition("not", Children: [unknown]) : new Condition(op, Children: [gold, unknown]);
        var item = Generic(); item = item with { Rules = [item.Rules[0] with { When = condition }] };
        var r = new ValuationEngine().Evaluate(item, Gold);
        Assert.Equal(expected, r.CanCopy);
        var emptyGroup = Gold.SelectedOptions.ToDictionary(); emptyGroup["condition"] = new HashSet<string>();
        Assert.Equal(expected, new ValuationEngine().Evaluate(item, new(emptyGroup)).CanCopy);
        Assert.False(new ValuationEngine().Evaluate(RibwichTests.Item, SelectionState.Empty).CanCopy);
    }

    [Fact]
    public void PotentialHigherPriorityDifferentOutcomeBlocksEarlyCompletion()
    {
        var competitor = new RuleDefinition("higher", "高优先级覆盖", "absoluteOverride", 90,
            new("selected", "condition", "intact"), 20);
        var engine = new ValuationEngine(); var item = Generic(more: [competitor]);
        Assert.False(engine.Evaluate(item, Gold).CanCopy); // unknown competitor may win later
        Assert.False(engine.Evaluate(item, ValuationTests.State(("material", ["gold"]), ("condition", ["intact"]))).CanCopy);
        var ruledOut = engine.Evaluate(item, ValuationTests.State(("material", ["gold"]), ("condition", ["worn"])));
        Assert.Equal(40, ruledOut.FinalPrice);
        var samePrice = item with { Rules = [item.Rules[0], competitor with { Value = 40 }] };
        Assert.Equal(40, engine.Evaluate(samePrice, Gold).FinalPrice);
        var sameValueDifferentBounds = item with { Rules = [item.Rules[0], competitor with { Value = 40, ApplyBounds = true }] };
        Assert.False(engine.Evaluate(sameValueDifferentBounds, Gold).CanCopy);
    }

    [Fact]
    public void InvalidStateAndInvalidEarlyRuleCannotBypassValidation()
    {
        var engine = new ValuationEngine(); var item = Generic();
        var invalid = ValuationTests.State(("material", ["gold"]), ("condition", ["typo"]));
        Assert.Equal(ValuationStatus.Invalid, engine.Evaluate(item, invalid).Status);
        Assert.Equal(ValuationStatus.Invalid, engine.Evaluate(item, ValuationTests.State(("material", ["gold", "aluminum"]))).Status);
        foreach(var kind in new[] { "bonus", "baseOverride" })
            Assert.Throws<DefinitionException>(() => DefinitionValidator.Validate(item with { Rules = [item.Rules[0] with { Kind = kind }] }, ["generic"]));
        Assert.Throws<DefinitionException>(() => DefinitionValidator.Validate(item with { Strategy = "custom" }, ["generic", "custom"]));
    }

    private sealed class MemoryClipboard : IClipboardService
    { public string? Text; public Task WriteAsync(string digits) { Text = digits; return Task.CompletedTask; } }
}

