using PawnAppraiser.Core;
using Xunit;

namespace PawnAppraiser.Core.Tests;

public class PlasmaFuelCellTests
{
    internal static ItemDefinition Item => ItemJson.Parse(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Fixtures/plasma_fuel_cell.json")));
    internal static SelectionState State(string wear = "standard", string color = "standard",
        string manufacturer = "unknown", string mark = "absent", params string[] features) =>
        ValuationTests.State(("wear", [wear]), ("features", features), ("color", [color]),
            ("manufacturer", [manufacturer]), ("security_mark", [mark]));

    [Theory]
    [InlineData("standard", "standard", "unknown", "absent", "", 300, 300)]
    [InlineData("light", "standard", "unknown", "absent", "", 255, 255)]
    [InlineData("heavy", "standard", "unknown", "absent", "", 210, 210)]
    [InlineData("standard", "standard", "unknown", "absent", "cracked", 150, 150)]
    [InlineData("standard", "standard", "unknown", "absent", "exposed_screws", 270, 270)]
    [InlineData("standard", "standard", "unknown", "absent", "reinforced", 360, 360)]
    [InlineData("standard", "low", "unknown", "absent", "", 150, 150)]
    [InlineData("standard", "high", "unknown", "absent", "", 450, 450)]
    [InlineData("standard", "standard", "rnr", "present", "cracked,reinforced", 210, 210)]
    [InlineData("heavy", "low", "unknown", "absent", "exposed_screws", 30, 90)]
    [InlineData("heavy", "low", "unknown", "absent", "cracked,exposed_screws", -120, 90)]
    [InlineData("heavy", "low", "unknown", "absent", "cracked,exposed_screws,reinforced", -60, 90)]
    [InlineData("standard", "standard", "wld", "present", "", 360, 360)]
    [InlineData("heavy", "low", "wld", "present", "cracked,exposed_screws,reinforced", 0, 90)]
    [InlineData("standard", "high", "wld", "present", "", 450, 450)]
    [InlineData("standard", "standard", "wld", "absent", "", 300, 300)]
    [InlineData("heavy", "standard", "rnr", "present", "exposed_screws,reinforced", 360, 360)]
    [InlineData("standard", "low", "rnr", "present", "", 150, 150)]
    [InlineData("standard", "standard", "zhs", "present", "", 420, 420)]
    [InlineData("standard", "standard", "zhs", "present", "reinforced", 480, 480)]
    [InlineData("light", "standard", "zhs", "present", "", 255, 255)]
    [InlineData("heavy", "standard", "zhs", "present", "reinforced", 270, 270)]
    [InlineData("standard", "standard", "zhs", "present", "cracked", 150, 150)]
    [InlineData("standard", "standard", "zhs", "present", "exposed_screws", 270, 270)]
    [InlineData("standard", "high", "ign", "present", "", 1200, 1200)]
    [InlineData("light", "high", "ign", "present", "", 405, 405)]
    [InlineData("standard", "high", "ign", "present", "reinforced", 510, 510)]
    [InlineData("standard", "high", "ign", "present", "cracked", 300, 300)]
    [InlineData("standard", "high", "ign", "present", "exposed_screws", 420, 420)]
    [InlineData("standard", "standard", "ign", "present", "", 300, 300)]
    [InlineData("standard", "high", "ign", "absent", "", 450, 450)]
    [InlineData("standard", "low", "unknown", "absent", "cracked,reinforced", 60, 90)]
    [InlineData("standard", "low", "unknown", "absent", "cracked", 0, 90)]
    public void ManualExamplesModifiersAndKnownManufacturerConditions(string wear, string color,
        string manufacturer, string mark, string features, long raw, long final)
    {
        var result = new ValuationEngine().Evaluate(Item, State(wear, color, manufacturer, mark,
            features.Split(',', StringSplitOptions.RemoveEmptyEntries)));
        Assert.Equal(ValuationStatus.Complete, result.Status);
        Assert.Equal(raw, result.RawPrice); Assert.Equal(final, result.FinalPrice);
        Assert.Equal(raw < 90, result.IsMinimumApplied); Assert.True(result.CanCopy);
        Assert.Empty(result.AppliedOverrides); Assert.False(result.IsEarlyComplete);
        Assert.Equal(final, result.Lines.Sum(l => l.Value));
    }

    [Theory]
    [InlineData("wear")][InlineData("color")][InlineData("manufacturer")][InlineData("security_mark")]
    public void RequiredChecksCannotBeBypassedByMissingMarkOrSevereDamage(string missing)
    {
        var values = State("heavy", "low", "ign", "absent", "cracked", "exposed_screws")
            .SelectedOptions.Where(p => p.Key != missing).ToDictionary();
        var engine = new ValuationEngine();
        var result = engine.Evaluate(Item, new(values));
        Assert.Equal(ValuationStatus.Incomplete, result.Status);
        Assert.Null(result.FinalPrice); Assert.False(result.CanCopy);
        values[missing] = new HashSet<string>();
        Assert.False(engine.Evaluate(Item, new(values)).CanCopy);
    }

    [Fact]
    public void OptionalFeaturesCanBeOmittedAndInvalidSelectionsAreRejected()
    {
        var engine = new ValuationEngine(); var item = Item;
        DefinitionValidator.Validate(item, engine.StrategyIds); Assert.Equal("generic", item.Strategy);
        var values = State().SelectedOptions.Where(p => p.Key != "features").ToDictionary();
        Assert.Equal(300, engine.Evaluate(item, new(values)).FinalPrice);
        foreach (var invalid in new[] { ValuationTests.State(("wear", ["light", "heavy"])),
            ValuationTests.State(("features", ["typo"])), ValuationTests.State(("typo", ["standard"])) })
        {
            var result = engine.Evaluate(item, invalid);
            Assert.Equal(ValuationStatus.Invalid, result.Status); Assert.False(result.CanCopy);
        }
        var malformed = item with { Rules = [new("bad", "坏引用", "bonus", 1,
            new("selected", "features", "unknown"))] };
        Assert.Equal(ValuationStatus.Invalid, engine.Evaluate(malformed, State()).Status);
    }

    [Fact]
    public void All720CompleteCombinationsMatchIndependentManualFormula()
    {
        var engine = new ValuationEngine(); var item = Item; int count = 0;
        var reversed = item with { Rules = item.Rules.AsEnumerable().Reverse().ToList() };
        foreach (var wear in new[] { "standard", "light", "heavy" })
        foreach (var color in new[] { "standard", "low", "high" })
        foreach (var manufacturer in new[] { "wld", "zhs", "rnr", "ign", "unknown" })
        foreach (var mark in new[] { "present", "absent" })
        for (int mask = 0; mask < 8; mask++)
        {
            // Independent manual decision table; no inspection of JSON predicates or engine lines.
            bool cracked = (mask & 1) != 0, screws = (mask & 2) != 0, reinforced = (mask & 4) != 0;
            long bonus = mark == "absent" ? 0 : manufacturer switch
            {
                "wld" when color is "standard" or "low" => 60,
                "zhs" when color == "standard" && wear == "standard" && !cracked && !screws => 120,
                "rnr" when color == "standard" && !cracked => 120,
                "ign" when color == "high" && wear == "standard" && mask == 0 => 750,
                _ => 0
            };
            long raw = 300 + (wear == "light" ? -45 : wear == "heavy" ? -90 : 0) +
                (color == "low" ? -150 : color == "high" ? 150 : 0) +
                (cracked ? -150 : 0) + (screws ? -30 : 0) + (reinforced ? 60 : 0) + bonus;
            var features = new List<string>();
            if (cracked) features.Add("cracked"); if (screws) features.Add("exposed_screws");
            if (reinforced) features.Add("reinforced");
            var state = State(wear, color, manufacturer, mark, features.ToArray());
            var result = engine.Evaluate(item, state);
            Assert.True(result.RawPrice == raw && result.FinalPrice == Math.Max(90, raw),
                $"{wear}/{color}/{manufacturer}/{mark}/{mask}: {result.RawPrice}/{result.FinalPrice} expected {raw}/{Math.Max(90, raw)}");
            Assert.Equal(ValuationStatus.Complete, result.Status); Assert.True(result.CanCopy);
            Assert.Equal(raw < 90, result.IsMinimumApplied); Assert.False(result.IsEarlyComplete);
            Assert.Equal(bonus == 0 ? 0 : 1, result.AppliedBonuses.Count);
            Assert.Equal(bonus, result.AppliedBonuses.Sum(b => b.Value));
            if (bonus > 0) Assert.Equal(manufacturer + "_bonus", Assert.Single(result.AppliedBonuses).Id);
            Assert.Empty(result.AppliedOverrides);
            Assert.Equal(result.FinalPrice, engine.Evaluate(reversed, state).FinalPrice);
            count++;
        }
        Assert.Equal(720, count);
    }

    [Fact]
    public void All2304PartialAndCompleteSelectionsWaitForAllRequiredChecks()
    {
        var item = Item; var engine = new ValuationEngine(); int total = 0, complete = 0;
        foreach (var wear in new[] { "", "standard", "light", "heavy" })
        foreach (var color in new[] { "", "standard", "low", "high" })
        foreach (var manufacturer in new[] { "", "wld", "zhs", "rnr", "ign", "unknown" })
        foreach (var mark in new[] { "", "present", "absent" })
        for (int mask = 0; mask < 8; mask++)
        {
            var features = new List<string>();
            if ((mask & 1) != 0) features.Add("cracked"); if ((mask & 2) != 0) features.Add("exposed_screws");
            if ((mask & 4) != 0) features.Add("reinforced");
            var values = State(wear, color, manufacturer, mark, features.ToArray()).SelectedOptions
                .Where(p => !p.Value.Contains("")).ToDictionary();
            bool isComplete = wear != "" && color != "" && manufacturer != "" && mark != "";
            var result = engine.Evaluate(item, new(values));
            Assert.Equal(isComplete ? ValuationStatus.Complete : ValuationStatus.Incomplete, result.Status);
            Assert.Equal(isComplete, result.CanCopy); Assert.False(result.IsEarlyComplete);
            if (isComplete) complete++; else Assert.Null(result.FinalPrice);
            total++;
        }
        Assert.Equal(2304, total); Assert.Equal(720, complete);
    }

    [Fact]
    public async Task ThreeItemsKeepIndependentStateAndOnlyExplicitCopyChangesClipboard()
    {
        var session = new AppraiserSession([ValuationTests.Hand, RibwichTests.Item, Item], new());
        var clipboard = new MemoryClipboard();
        session.Toggle("material", "gold"); session.Toggle("condition", "intact"); session.Toggle("interface", "neural");
        Assert.Equal(620, session.Result.FinalPrice); Assert.True(await session.CopyAsync(clipboard));
        session.SwitchItem("ribwich"); session.Toggle("packaging", "opened");
        Assert.Equal(40, session.Result.FinalPrice);
        session.SwitchItem("plasma_fuel_cell"); Assert.False(await session.CopyAsync(clipboard));
        foreach (var (group, values) in State(manufacturer: "rnr", mark: "present", features: ["reinforced"]).SelectedOptions)
            foreach (var value in values) session.Toggle(group, value);
        Assert.Equal(480, session.Result.FinalPrice); Assert.Equal("620", clipboard.Text);
        Assert.True(await session.CopyAsync(clipboard)); Assert.Equal("480", clipboard.Text);
        session.Toggle("features", "cracked"); Assert.Equal(210, session.Result.FinalPrice);
        Assert.Empty(session.Result.AppliedBonuses); session.Toggle("features", "cracked");
        Assert.Equal(480, session.Result.FinalPrice);
        session.Toggle("wear", "light"); session.Toggle("wear", "heavy"); session.Toggle("wear", "heavy");
        Assert.Equal("heavy", Assert.Single(session.CurrentState!.Snapshot.SelectedOptions["wear"]));
        Assert.Equal(390, session.Result.FinalPrice);
        session.SwitchItem("bionic_hand"); Assert.Equal(620, session.Result.FinalPrice);
        session.SwitchItem("ribwich"); Assert.Equal(40, session.Result.FinalPrice);
        session.SwitchItem("plasma_fuel_cell"); Assert.Equal(390, session.Result.FinalPrice);
        session.ResetCurrent(); Assert.False(await session.CopyAsync(clipboard)); Assert.Equal("480", clipboard.Text);
        Assert.Empty(session.CurrentState!.Snapshot.SelectedOptions);
        session.SwitchItem("ribwich"); Assert.Equal(40, session.Result.FinalPrice);
        var restarted = new AppraiserSession([ValuationTests.Hand, RibwichTests.Item, Item], new());
        foreach (var item in restarted.Items) { restarted.SwitchItem(item.Id); Assert.False(restarted.Result.CanCopy); }
    }

    private sealed class MemoryClipboard : IClipboardService
    { public string? Text; public Task WriteAsync(string digits) { Text = digits; return Task.CompletedTask; } }
}
