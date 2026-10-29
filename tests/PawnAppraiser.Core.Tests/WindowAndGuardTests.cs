using System.Text.Json;
using PawnAppraiser.Core;
using Xunit;

namespace PawnAppraiser.Core.Tests;

public class WindowAndGuardTests
{
    [Fact]
    public void VisibilityCollapseAndScreenRecoveryAreIndependent()
    {
        var state = new OverlayState(); state.ToggleExpanded(); state.Hide(); state.ToggleExpanded();
        Assert.False(state.Visible); Assert.False(state.Expanded); state.Show(); Assert.False(state.Expanded);
        var work = new WindowBounds(0, 0, 2560, 1400);
        var restored = WindowLayout.Constrain(new(-9999, 9999, 9999, 9999), work, 320, 430, 640);
        Assert.Equal(new WindowBounds(0, 0, 640, 1400), restored);
        Assert.Equal(0, WindowLayout.Snap(new(5, 5, 440, 800), work, 12).X);
        Assert.Equal(2120, WindowLayout.Snap(new(2114, 598, 440, 800), work, 12).X);
        var small = WindowLayout.Constrain(new(180, 90, 440, 800), new(0, 0, 280, 400), 320, 430, 640);
        Assert.Equal(new WindowBounds(0, 0, 280, 400), small);
    }
    [Fact]
    public void SettingsRoundTripContainsNoValuationAndCorruptionUsesDefaults()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "settings-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SettingsStore(path); Assert.True(store.Load(out var warning).Topmost); Assert.Null(warning);
            var settings = new AppSettings { WindowPosition = new(700, 100), Opacity = 0.94, CollapseHotkey = new(0x75) };
            store.Save(settings); Assert.Equal(settings, store.Load(out warning)); Assert.Null(warning);
            var json = File.ReadAllText(store.Path); Assert.DoesNotContain("selectedOptions", json); Assert.DoesNotContain("finalPrice", json);
            File.WriteAllText(store.Path, "{bad"); Assert.True(store.Load(out warning).IsValid); Assert.NotNull(warning);
            File.WriteAllText(store.Path, "{\"schemaVersion\":999}"); Assert.Equal(new AppSettings(), store.Load(out warning)); Assert.NotNull(warning);
            Assert.Throws<ArgumentException>(() => store.Save(new() { Opacity = double.NaN }));
            Assert.False(new AppSettings { WindowPosition = null! }.IsValid);
            Assert.False(new AppSettings { VisibilityHotkey = new(0x6A) }.IsValid);
        }
        finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
    }
    [Fact]
    public void DefensiveDefinitionBranchesRejectMalformedData()
    {
        var hand = ValuationTests.Hand;
        var c = new Condition("selected", "material", "gold");
        var bad = new[]
        {
            hand with { Pricing = new(1_000_000_001, 0) },
            hand with { Groups = [hand.Groups[0] with { Options = [null!] }] },
            hand with { Rules = [new("bad", "bad", "bonus", 1, null!)] },
            hand with { Rules = [new("bad", "bad", "bonus", 1, c with { Children = [] })] },
            hand with { Rules = [new("bad", "bad", "bonus", 1, c with { Op = "unknown" })] },
            hand with { Rules = [new("bad", "bad", "bonus", 1, new("not", Children: [c, c]))] },
            hand with { Groups = Enumerable.Repeat(hand.Groups[0], 65).ToList() }
        };
        foreach (var item in bad) Assert.Throws<DefinitionException>(() => DefinitionValidator.Validate(item, ["generic"]));
        for (int i = 0; i < 18; i++) c = new("not", Children: [c]);
        Assert.Throws<DefinitionException>(() => DefinitionValidator.Validate(hand with { Rules = [new("deep", "deep", "bonus", 1, c)] }, ["generic"]));
    }
    [Theory]
    [InlineData(-1L, 100L)][InlineData(100L, null)]
    public void InvalidCustomStrategyContractNeverProducesCopyablePrice(long final, long? raw)
    {
        var engine = new ValuationEngine(); engine.Register("bad", new BadResult(final, raw));
        var r = engine.Evaluate(ValuationTests.Hand with { Strategy = "bad" }, ValuationTests.HandState("aluminum", "intact", "emg"));
        Assert.Equal(ValuationStatus.Error, r.Status); Assert.False(r.CanCopy); Assert.Null(r.FinalPrice);
    }
    private class BadResult(long final, long? raw) : IValuationStrategy
    { public ValuationResult Evaluate(ItemDefinition item, SelectionState state) => new(ValuationStatus.Complete, final, raw, [], []); }
}
