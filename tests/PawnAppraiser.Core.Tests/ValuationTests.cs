using PawnAppraiser.Core;
using Xunit;

namespace PawnAppraiser.Core.Tests;

public class ValuationTests
{
    internal static ItemDefinition Hand => ItemJson.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/bionic_hand.json")));
    internal static ItemDefinition Relic => ItemJson.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/test_relic.json")));
    internal static SelectionState State(params (string Group, string[] Options)[] values) =>
        new(values.ToDictionary(v => v.Group, v => (IReadOnlySet<string>)v.Options.ToHashSet()));
    internal static SelectionState HandState(string material, string condition, string control, params string[] components) =>
        State(("material", [material]), ("condition", [condition]), ("interface", [control]), ("components", components));

    [Theory]
    [InlineData("aluminum", "intact", "emg", "", 100)]
    [InlineData("carbon", "worn", "emg_plus", "", 160)]
    [InlineData("aluminum", "damaged", "unknown", "", 50)]
    [InlineData("carbon", "intact", "emg_plus", "armor,blade", 425)]
    [InlineData("gold", "intact", "neural", "", 620)]
    [InlineData("gold", "intact", "neural", "armor,blade", 750)]
    [InlineData("carbon", "intact", "emg_plus", "armor", 245)]
    [InlineData("carbon", "intact", "neural", "armor,blade", 500)]
    [InlineData("gold", "intact", "emg_plus", "armor,blade", 475)]
    [InlineData("gold", "worn", "neural", "", 385)]
    public void StandardT01ToT10(string material, string condition, string control, string extras, long expected)
    {
        var result = new ValuationEngine().Evaluate(Hand, HandState(material, condition, control, extras.Split(',', StringSplitOptions.RemoveEmptyEntries)));
        Assert.Equal(ValuationStatus.Complete, result.Status);
        Assert.Equal(expected, result.FinalPrice);
        if (material == "aluminum" && condition == "damaged")
        { Assert.Equal(-45, result.RawPrice); Assert.True(result.IsMinimumApplied); }
    }
    [Fact]
    public void Exhaustive144CombinationsMatchIndependentFormula()
    {
        var engine = new ValuationEngine(); var item = Hand; int count = 0;
        foreach (var (material, md) in new[] { ("aluminum", 0), ("carbon", 50), ("gold", 200) })
        foreach (var (condition, cd) in new[] { ("intact", 0), ("worn", -35), ("damaged", -85) })
        foreach (var (control, id) in new[] { ("emg", 0), ("emg_plus", 45), ("neural", 120), ("unknown", -60) })
        for (int mask = 0; mask < 4; mask++)
        {
            var extras = new List<string>(); if ((mask & 1) != 0) extras.Add("armor"); if ((mask & 2) != 0) extras.Add("blade");
            bool tactical = material == "carbon" && condition == "intact" && control is "emg_plus" or "neural" && mask == 3;
            bool luxury = material == "gold" && condition == "intact" && control == "neural";
            long raw = 100 + md + cd + id + ((mask & 1) != 0 ? 50 : 0) + ((mask & 2) != 0 ? 80 : 0) + (tactical ? 100 : 0) + (luxury ? 200 : 0);
            var result = engine.Evaluate(item, HandState(material, condition, control, extras.ToArray()));
            Assert.Equal(raw, result.RawPrice); Assert.Equal(Math.Max(50, raw), result.FinalPrice);
            Assert.Equal((tactical ? 1 : 0) + (luxury ? 1 : 0), result.AppliedBonuses.Count); count++;
        }
        Assert.Equal(144, count);
    }
    [Theory]
    [InlineData("material")][InlineData("condition")][InlineData("interface")]
    public void MissingAnyRequiredGroupBlocksPrice(string group)
    {
        var values = HandState("gold", "intact", "neural").SelectedOptions.Where(p => p.Key != group).ToDictionary();
        var result = new ValuationEngine().Evaluate(Hand, new(values));
        Assert.Equal(ValuationStatus.Incomplete, result.Status); Assert.Null(result.FinalPrice); Assert.False(result.CanCopy);
    }
    [Fact]
    public void InvalidStateDoesNotLeakPrice()
    {
        var engine = new ValuationEngine();
        foreach (var state in new[] { State(("material", ["aluminum", "gold"])), State(("material", ["typo"])), State(("typo", ["gold"])) })
        { var result = engine.Evaluate(Hand, state); Assert.Equal(ValuationStatus.Invalid, result.Status); Assert.Null(result.FinalPrice); }
    }
    [Fact]
    public void ExtensionFixtureSupportsBonusesAndAbsoluteOverrideBelowMinimum()
    {
        var engine = new ValuationEngine(); var item = Relic;
        Assert.Equal(420, engine.Evaluate(item, State(("authenticity", ["real"]), ("extras", ["seal", "case"]))).FinalPrice);
        var result = engine.Evaluate(item, State(("authenticity", ["fake"]), ("extras", ["seal", "case"])));
        Assert.Equal(40, result.FinalPrice); Assert.Equal(320, result.RawPrice); Assert.False(result.IsMinimumApplied);
        var bounded = item with { Rules = item.Rules.Select(r => r.Kind == "absoluteOverride" ? r with { ApplyBounds = true } : r).ToList() };
        Assert.Equal(90, engine.Evaluate(bounded, State(("authenticity", ["fake"]))).FinalPrice);
    }
    [Fact]
    public void BaseOverridePriorityMaximumAndNot()
    {
        var item = Relic with { Rules = [new("base_low", "替换基础", "baseOverride", 700, new("not", Children: [new("selected", "authenticity", "fake")]), 1),
            new("base_high", "优先基础", "baseOverride", 790, new("selected", "authenticity", "real"), 2)] };
        var r = new ValuationEngine().Evaluate(item, State(("authenticity", ["real"]), ("extras", ["seal", "case"])));
        Assert.Equal(840, r.RawPrice); Assert.Equal(800, r.FinalPrice); Assert.Single(r.AppliedOverrides);
    }
    [Fact]
    public void ExplicitStrategyRegistrationAndFailureAreIsolated()
    {
        var engine = new ValuationEngine(); engine.Register("double", new DoubleStrategy()); engine.Register("broken", new BrokenStrategy());
        var item = Relic with { Strategy = "double" };
        Assert.Equal(600, engine.Evaluate(item, State(("authenticity", ["real"]))).FinalPrice);
        var result = engine.Evaluate(item with { Strategy = "broken" }, State(("authenticity", ["real"])));
        Assert.Equal(ValuationStatus.Error, result.Status); Assert.False(result.CanCopy);
        Assert.Equal(ValuationStatus.Incomplete, engine.Evaluate(item, SelectionState.Empty).Status);
        Assert.Throws<ArgumentException>(() => engine.Register("double", new DoubleStrategy()));
    }
    private class DoubleStrategy : IValuationStrategy
    { public ValuationResult Evaluate(ItemDefinition item, SelectionState state) => new(ValuationStatus.Complete, item.Pricing.BasePrice * 2, item.Pricing.BasePrice * 2, [], []); }
    private class BrokenStrategy : IValuationStrategy
    { public ValuationResult Evaluate(ItemDefinition item, SelectionState state) => throw new OverflowException("test"); }
}
