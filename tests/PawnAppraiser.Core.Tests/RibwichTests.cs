using PawnAppraiser.Core;
using Xunit;

namespace PawnAppraiser.Core.Tests;

public class RibwichTests
{
    internal static ItemDefinition Item => ItemJson.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/ribwich.json")));
    internal static SelectionState State(string date = "second_other", string factory = "normal",
        string edition = "regular", string patties = "single", string packaging = "intact",
        string brand = "correct", string ribwich = "correct") => ValuationTests.State(
            ("packaging", [packaging]), ("date", [date]), ("factory", [factory]), ("edition", [edition]),
            ("patties", [patties]), ("brand", [brand]), ("ribwich", [ribwich]));

    [Theory]
    [InlineData("second_other", "normal", "regular", "single", "correct", 150)]
    [InlineData("second_other", "high", "regular", "single", "correct", 200)]
    [InlineData("second_other", "low", "regular", "single", "correct", 100)]
    [InlineData("first_other", "normal", "regular", "single", "missing", 230)]
    [InlineData("first_month", "normal", "regular", "single", "missing", 280)]
    [InlineData("last_month", "normal", "regular", "single", "correct", 200)]
    [InlineData("first_month", "high", "deluxe", "double", "missing", 1430)]
    [InlineData("first_other", "low", "deluxe", "double", "missing", 1280)]
    [InlineData("last_month", "low", "deluxe", "double", "correct", 1250)]
    [InlineData("second_other", "normal", "deluxe", "single", "correct", 450)]
    [InlineData("second_other", "normal", "regular", "double", "correct", 950)]
    [InlineData("second_other", "normal", "deluxe", "double", "correct", 1250)]
    [InlineData("first_month", "high", "deluxe", "double", "correct", 40)]
    [InlineData("first_other", "normal", "regular", "single", "wrong", 40)]
    [InlineData("second_other", "normal", "regular", "single", "missing", 40)]
    [InlineData("last_month", "normal", "regular", "single", "wrong", 40)]
    [InlineData("missing", "high", "deluxe", "double", "correct", 40)]
    [InlineData("invalid", "high", "deluxe", "double", "correct", 40)]
    public void ManualExamplesAndConfirmedStickerRule(string date, string factory, string edition,
        string patties, string sticker, long expected)
    {
        var result = new ValuationEngine().Evaluate(Item, State(date, factory, edition, patties, ribwich: sticker));
        Assert.Equal(ValuationStatus.Complete, result.Status);
        Assert.Equal(expected, result.FinalPrice);
        Assert.True(result.CanCopy);
    }

    [Fact]
    public void All6480CompleteSelectionsMatchIndependentManualFormula()
    {
        var item = Item; var engine = new ValuationEngine(); int count = 0, genuineCount = 0;
        foreach (var packaging in new[] { "intact", "opened", "missing" })
        foreach (var date in new[] { "first_month", "first_other", "second_other", "last_month", "missing", "invalid" })
        foreach (var factory in new[] { "high", "normal", "low", "missing", "invalid" })
        foreach (var edition in new[] { "regular", "deluxe", "sesame_only", "pickles_only" })
        foreach (var patties in new[] { "single", "double" })
        foreach (var brand in new[] { "correct", "missing", "wrong" })
        foreach (var sticker in new[] { "missing", "correct", "wrong" })
        {
            // This oracle uses the manual's decision table, not JSON conditions or engine intermediates.
            bool first = date is "first_month" or "first_other";
            bool second = date is "second_other" or "last_month";
            bool genuine = packaging == "intact" && (first || second) &&
                factory is "high" or "normal" or "low" && edition is "regular" or "deluxe" &&
                brand == "correct" && (first ? sticker == "missing" : sticker == "correct");
            long expected = genuine
                ? 150 + (first ? 80 : 0) + (date is "first_month" or "last_month" ? 50 : 0) +
                  (factory == "high" ? 50 : factory == "low" ? -50 : 0) +
                  (edition == "deluxe" ? 300 : 0) + (patties == "double" ? 800 : 0)
                : 40;
            var result = engine.Evaluate(item, State(date, factory, edition, patties, packaging, brand, sticker));
            Assert.True(result.FinalPrice == expected,
                $"包装={packaging},日期={date},工厂={factory},版本={edition},肉饼={patties},标志={brand},贴纸={sticker}: {result.FinalPrice} != {expected}");
            Assert.Equal(ValuationStatus.Complete, result.Status);
            Assert.Equal(!genuine, result.AppliedOverrides.Count > 0);
            Assert.Equal(genuine && edition == "deluxe", result.AppliedBonuses.Any(b => b.Id == "deluxe"));
            Assert.Equal(genuine && patties == "double", result.AppliedBonuses.Any(b => b.Id == "double_patty"));
            Assert.True(result.FinalPrice >= 40);
            if (genuine) genuineCount++;
            count++;
        }
        Assert.Equal(6480, count); Assert.Equal(48, genuineCount);
    }

    [Theory]
    [InlineData("packaging")][InlineData("date")][InlineData("factory")][InlineData("edition")]
    [InlineData("patties")][InlineData("brand")][InlineData("ribwich")]
    public void RequiredGroupsAreNeededUntilAFixedPriceIsKnown(string missing)
    {
        var state = State().SelectedOptions.Where(p => p.Key != missing).ToDictionary();
        var result = new ValuationEngine().Evaluate(Item, new(state));
        Assert.Equal(ValuationStatus.Incomplete, result.Status); Assert.Null(result.FinalPrice); Assert.False(result.CanCopy);
        state[missing] = new HashSet<string>();
        Assert.Equal(ValuationStatus.Incomplete, new ValuationEngine().Evaluate(Item, new(state)).Status);
    }

    [Theory]
    [InlineData("packaging", "opened")][InlineData("packaging", "missing")]
    [InlineData("factory", "missing")][InlineData("factory", "invalid")]
    [InlineData("edition", "sesame_only")][InlineData("edition", "pickles_only")]
    [InlineData("brand", "missing")][InlineData("brand", "wrong")]
    public void AnyDefectOverridesOtherBonusesAndRecoveryRecalculates(string group, string defect)
    {
        var session = new AppraiserSession([Item], new());
        foreach (var (id, values) in State("first_month", "high", "deluxe", "double", ribwich: "missing").SelectedOptions)
            session.Toggle(id, values.Single());
        Assert.Equal(1430, session.Result.FinalPrice);
        var original = session.CurrentState!.Snapshot.SelectedOptions[group].Single();
        session.Toggle(group, defect);
        Assert.Equal(40, session.Result.FinalPrice);
        Assert.Single(session.Result.AppliedOverrides);
        Assert.DoesNotContain(session.Result.AppliedBonuses, b => b.Id is "deluxe" or "double_patty");
        session.Toggle(group, original);
        Assert.Equal(1430, session.Result.FinalPrice);
    }

    [Fact]
    public void MultipleDefectsAndRuleOrderDoNotChangeFixed40()
    {
        var item = Item; var state = State("invalid", "invalid", "sesame_only", "double", "opened", "wrong", "wrong");
        var engine = new ValuationEngine();
        Assert.Equal(40, engine.Evaluate(item, state).FinalPrice);
        Assert.Equal(40, engine.Evaluate(item with { Rules = item.Rules.AsEnumerable().Reverse().ToList() }, state).FinalPrice);
    }

    [Fact]
    public void ConfigurationAndInvalidSelectionsFailWithoutPrice()
    {
        var item = Item; var engine = new ValuationEngine();
        Assert.Equal("generic", item.Strategy); DefinitionValidator.Validate(item, engine.StrategyIds);
        var invalid = State().SelectedOptions.ToDictionary();
        invalid["date"] = new HashSet<string> { "first_month", "last_month" };
        Assert.Equal(ValuationStatus.Invalid, engine.Evaluate(item, new(invalid)).Status);
        invalid["date"] = new HashSet<string> { "typo" };
        Assert.False(engine.Evaluate(item, new(invalid)).CanCopy);
        var bad = item with { Rules = [new("bad", "bad", "bonus", 1, new("selected", "date", "unknown"))] };
        Assert.Equal(ValuationStatus.Invalid, engine.Evaluate(bad, State()).Status);
    }

    [Fact]
    public async Task BothRealItemsKeepIndependentStateResetAndClipboard()
    {
        var session = new AppraiserSession([ValuationTests.Hand, Item], new()); var clipboard = new MemoryClipboard();
        session.Toggle("material", "carbon"); session.Toggle("condition", "intact");
        session.Toggle("interface", "emg_plus"); session.Toggle("components", "armor"); session.Toggle("components", "blade");
        Assert.Equal(425, session.Result.FinalPrice); await session.CopyAsync(clipboard);
        session.SwitchItem("ribwich"); Assert.False(session.Result.CanCopy);
        foreach (var (group, options) in State(edition: "deluxe", patties: "double").SelectedOptions)
            session.Toggle(group, options.Single());
        Assert.Equal(1250, session.Result.FinalPrice); Assert.Equal("425", clipboard.Text);
        await session.CopyAsync(clipboard); Assert.Equal("1250", clipboard.Text);
        session.SwitchItem("bionic_hand"); Assert.Equal(425, session.Result.FinalPrice);
        session.ResetCurrent(); Assert.False(session.Result.CanCopy);
        session.SwitchItem("ribwich"); Assert.Equal(1250, session.Result.FinalPrice);
        session.ResetCurrent(); Assert.False(await session.CopyAsync(clipboard)); Assert.Equal("1250", clipboard.Text);
        Assert.False(new AppraiserSession([ValuationTests.Hand, Item], new()).Result.CanCopy);
    }
    private sealed class MemoryClipboard : IClipboardService
    { public string? Text; public Task WriteAsync(string digits) { Text = digits; return Task.CompletedTask; } }
}

