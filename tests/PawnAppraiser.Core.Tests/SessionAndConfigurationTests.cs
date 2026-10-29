using System.Text.Json;
using System.Text.Json.Nodes;
using PawnAppraiser.Core;
using Xunit;

namespace PawnAppraiser.Core.Tests;

public class SessionAndConfigurationTests
{
    private static AppraiserSession Session() => new([ValuationTests.Hand, ValuationTests.Relic], new());
    [Fact]
    public async Task RuntimeStateCopyResetAndRestartAreIndependent()
    {
        var s = Session(); var clipboard = new Clipboard();
        Assert.False(await s.CopyAsync(clipboard)); Assert.Empty(clipboard.Writes);
        s.Toggle("material", "gold"); s.Toggle("condition", "intact"); s.Toggle("interface", "neural");
        Assert.True(await s.CopyAsync(clipboard)); Assert.Equal("620", clipboard.Writes.Single());
        s.Toggle("material", "carbon"); s.Toggle("components", "armor"); s.Toggle("components", "blade");
        Assert.Equal(500, s.Result.FinalPrice); Assert.Single(clipboard.Writes);
        s.Toggle("components", "blade"); Assert.Equal(320, s.Result.FinalPrice); Assert.Empty(s.Result.AppliedBonuses);
        s.Toggle("material", "carbon"); Assert.Equal(320, s.Result.FinalPrice); // single re-click stays selected
        Assert.True(await s.CopyAsync(clipboard)); Assert.Equal("320", clipboard.Writes.Last());
        s.SwitchItem("test_relic"); s.Toggle("authenticity", "real"); Assert.Equal(300, s.Result.FinalPrice);
        s.SwitchItem("bionic_hand"); Assert.Equal(320, s.Result.FinalPrice);
        s.ResetCurrent(); Assert.False(s.Result.CanCopy); Assert.Empty(s.CurrentState!.Snapshot.SelectedOptions);
        Assert.Equal("320", clipboard.Writes.Last()); s.SwitchItem("test_relic"); Assert.Equal(300, s.Result.FinalPrice);
        Assert.False(Session().Result.CanCopy);
    }
    [Fact]
    public async Task ClipboardFailurePreservesSelection()
    {
        var s = Session(); s.Toggle("material", "aluminum"); s.Toggle("condition", "intact"); s.Toggle("interface", "emg");
        await Assert.ThrowsAsync<IOException>(() => s.CopyAsync(new FailingClipboard()));
        Assert.Equal(100, s.Result.FinalPrice);
    }
    [Fact]
    public void InvalidConfigurationsAreRejected()
    {
        var item = ValuationTests.Hand;
        var bad = new List<ItemDefinition>
        {
            item with { SchemaVersion = 2 }, item with { Id = "" }, item with { Strategy = "missing" },
            item with { Pricing = new(100, 50, 40) }, item with { Pricing = null! },
            item with { Groups = [] }, item with { Groups = [item.Groups[0], item.Groups[0]] },
            item with { Groups = [item.Groups[0] with { Options = [] }] },
            item with { Groups = [item.Groups[0] with { Options = [item.Groups[0].Options[0], item.Groups[0].Options[0]] }] },
            item with { Groups = [item.Groups[0] with { SelectionType = "unknown" }] },
            item with { Rules = [new("bad", "坏引用", "bonus", 1, new("selected", "material", "missing"))] },
            item with { Rules = [new("bad", "坏布尔", "bonus", 1, new("not", Children: []))] },
            item with { Rules = [new("bad", "未知", "unknown", 1, new("selected", "material", "gold"))] },
            item with { Rules = [new("a", "A", "absoluteOverride", 10, new("selected", "material", "gold")), new("b", "B", "absoluteOverride", 20, new("selected", "material", "gold"))] },
            item with { Groups = [null!] }, item with { Rules = [null!] }
        };
        foreach (var definition in bad) Assert.Throws<DefinitionException>(() => DefinitionValidator.Validate(definition, ["generic"]));
        Assert.Throws<JsonException>(() => ItemJson.Parse("{"));
        Assert.Throws<JsonException>(() => ItemJson.Parse(JsonSerializer.Serialize(item, ItemJson.Options).Replace("\"schemaVersion\": 1", "\"schemaVersion\": 1, \"typo\": 1")));
    }
    [Fact]
    public void CatalogDisablesAllDuplicateIdsAndIsolatesBadFiles()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "catalog-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path);
        try
        {
            File.WriteAllText(Path.Combine(path, "hand.json"), JsonSerializer.Serialize(ValuationTests.Hand, ItemJson.Options));
            File.WriteAllText(Path.Combine(path, "copy.json"), JsonSerializer.Serialize(ValuationTests.Hand, ItemJson.Options));
            File.WriteAllText(Path.Combine(path, "relic.json"), JsonSerializer.Serialize(ValuationTests.Relic, ItemJson.Options));
            File.WriteAllText(Path.Combine(path, "broken.json"), "{broken");
            var result = ItemCatalog.Load(path, new()); Assert.Equal("test_relic", Assert.Single(result.Items).Id); Assert.Equal(3, result.Issues.Count);
            Assert.Single(ItemCatalog.Load(Path.Combine(path, "missing"), new()).Issues);
        }
        finally { Directory.Delete(path, true); }
    }
    [Theory]
    [InlineData("basePrice")][InlineData("minimum")][InlineData("priceDelta")][InlineData("required")]
    public void MissingRequiredScalarIsNotSilentlyDefaulted(string field)
    {
        var node = JsonNode.Parse(JsonSerializer.Serialize(ValuationTests.Hand, ItemJson.Options))!;
        JsonObject parent = field switch
        {
            "basePrice" or "minimum" => node["pricing"]!.AsObject(),
            "required" => node["groups"]![0]!.AsObject(),
            _ => node["groups"]![0]!["options"]![0]!.AsObject()
        };
        parent.Remove(field);
        Assert.Throws<JsonException>(() => ItemJson.Parse(node.ToJsonString()));
    }
    private class Clipboard : IClipboardService
    { public List<string> Writes { get; } = []; public Task WriteAsync(string digits) { Writes.Add(digits); return Task.CompletedTask; } }
    private class FailingClipboard : IClipboardService
    { public Task WriteAsync(string digits) => throw new IOException("occupied"); }
}
