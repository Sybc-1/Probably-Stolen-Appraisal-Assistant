namespace PawnAppraiser.Core;

public sealed record CatalogIssue(string File, string Message);
public sealed record CatalogResult(IReadOnlyList<ItemDefinition> Items, IReadOnlyList<CatalogIssue> Issues);
public static class ItemCatalog
{
    public static CatalogResult Load(string directory, ValuationEngine engine)
    {
        var candidates = new List<(string File, ItemDefinition Item)>();
        var issues = new List<CatalogIssue>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*.json").Order(StringComparer.Ordinal))
            {
                try
                {
                    if (new FileInfo(file).Length > 2_000_000) throw new DefinitionException("配置大于2MB");
                    var item = ItemJson.Parse(File.ReadAllText(file));
                    DefinitionValidator.Validate(item, engine.StrategyIds);
                    candidates.Add((file, item));
                }
                catch (Exception ex) when (ex is System.Text.Json.JsonException or DefinitionException or IOException or UnauthorizedAccessException)
                { issues.Add(new(Path.GetFileName(file), ex.Message)); }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { issues.Add(new(directory, ex.Message)); }
        var duplicates = candidates.GroupBy(c => c.Item.Id).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        foreach (var entry in candidates.Where(c => duplicates.Contains(c.Item.Id)))
            issues.Add(new(Path.GetFileName(entry.File), $"重复物品 ID：{entry.Item.Id}；相关定义全部禁用"));
        return new(candidates.Where(c => !duplicates.Contains(c.Item.Id)).Select(c => c.Item).ToArray(), issues);
    }
}
