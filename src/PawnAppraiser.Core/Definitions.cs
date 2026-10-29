using System.Text.Json;
using System.Text.Json.Serialization;

namespace PawnAppraiser.Core;

public sealed record Pricing(long BasePrice, long Minimum, long? Maximum = null);
public sealed record OptionDefinition(string Id, string Label, long PriceDelta, int DisplayOrder = 0,
    string? OptionalIcon = null, string? OptionalDescription = null);
public sealed record GroupDefinition(string Id, string Label, string SelectionType, bool Required,
    int DisplayOrder, List<OptionDefinition> Options);
// References are qualified by group, allowing future items to reuse an option ID in another group.
public sealed record Condition(string Op, string? Group = null, string? Option = null, List<Condition>? Children = null);
public sealed record RuleDefinition(string Id, string Label, string Kind, long Value, Condition When,
    int Priority = 0, bool ApplyBounds = false, bool CompleteEarly = false);
public sealed record ItemDefinition(int SchemaVersion, string Id, string DisplayName, Pricing Pricing,
    List<GroupDefinition> Groups, List<RuleDefinition> Rules, string Strategy = "generic",
    string? Description = null, string? Icon = null);

public static class ItemJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
        RespectRequiredConstructorParameters = true,
        MaxDepth = 48
    };
    public static ItemDefinition Parse(string json) =>
        JsonSerializer.Deserialize<ItemDefinition>(json, Options) ?? throw new JsonException("物品配置为空");
}

public sealed class DefinitionException(string message) : Exception(message);

public static class DefinitionValidator
{
    private const long Limit = 1_000_000_000;
    public static void Validate(ItemDefinition item, IEnumerable<string> strategies)
    {
        static void Require(bool ok, string message) { if (!ok) throw new DefinitionException(message); }
        static bool Id(string? s) => !string.IsNullOrWhiteSpace(s) && s.Length <= 80 &&
            s.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');
        static bool Label(string? s) => !string.IsNullOrWhiteSpace(s) && s.Length <= 500;
        Require(item.SchemaVersion == 1, "不支持的 schemaVersion");
        Require(Id(item.Id) && Label(item.DisplayName), "物品 ID 或名称不合法");
        Require(strategies.Contains(item.Strategy, StringComparer.Ordinal), $"未注册策略：{item.Strategy}");
        Require(item.Pricing is not null, "缺少 pricing");
        var pricing = item.Pricing!;
        Require(pricing.BasePrice >= 0 && pricing.BasePrice <= Limit && pricing.Minimum >= 0 && pricing.Minimum <= Limit,
            "基础价/最低价超出 0..1000000000");
        Require(pricing.Maximum is null || pricing.Maximum >= pricing.Minimum && pricing.Maximum <= Limit, "最高价不合法");
        Require(item.Groups is { Count: > 0 and <= 64 } && item.Rules is { Count: <= 128 }, "groups/rules 缺失或超出数量限制");
        var groups = item.Groups!;
        var groupIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var g in groups)
        {
            Require(g is not null, "选项组不能为 null");
            Require(Id(g!.Id) && groupIds.Add(g.Id) && Label(g.Label), "选项组 ID 重复或名称不合法");
            Require(g.SelectionType is "single" or "multiple", $"未知选择类型：{g.Id}");
            Require(g.Options is { Count: > 0 and <= 128 }, $"选项组 {g.Id} 为空或过大");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var o in g.Options!)
            {
                Require(o is not null, "选项不能为 null");
                Require(Id(o!.Id) && ids.Add(o.Id) && Label(o.Label), $"选项 ID 重复或名称不合法：{g.Id}");
                Require(o.PriceDelta >= -Limit && o.PriceDelta <= Limit, "价格修正超出范围");
            }
        }
        var ruleIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in item.Rules!)
        {
            Require(r is not null, "规则不能为 null");
            Require(Id(r!.Id) && ruleIds.Add(r.Id) && Label(r.Label), "规则 ID 重复或名称不合法");
            Require(r.Kind is "bonus" or "absoluteOverride" or "baseOverride", $"未知规则类型：{r.Kind}");
            Require(!r.CompleteEarly || r.Kind == "absoluteOverride" && item.Strategy == "generic",
                "completeEarly 仅用于 generic 策略的 absoluteOverride");
            Require(r.Value >= -Limit && r.Value <= Limit && (r.Kind == "bonus" || r.Value >= 0), "规则价格超出范围");
            ValidateCondition(r.When, groups, 0);
        }
        // Two potentially competing overrides with different semantics require explicit ordering.
        foreach (var set in item.Rules!.Where(r => r.Kind != "bonus").GroupBy(r => (r.Kind, r.Priority)))
            Require(set.Select(r => (r.Value, r.ApplyBounds)).Distinct().Count() <= 1,
                "同类型覆盖规则优先级相同但结果不同；请指定不同优先级");
    }

    private static void ValidateCondition(Condition? c, List<GroupDefinition> groups, int depth)
    {
        if (c is null || depth > 16) throw new DefinitionException("条件为空或嵌套超过16层");
        if (c.Op == "selected")
        {
            if (c.Children is not null || !groups.Any(g => g.Id == c.Group && g.Options.Any(o => o.Id == c.Option)))
                throw new DefinitionException("selected 条件引用不存在或混有 children");
        }
        else if (c.Op is "all" or "any" or "not")
        {
            if (c.Group is not null || c.Option is not null || c.Children is not { Count: > 0 and <= 128 } ||
                c.Op == "not" && c.Children.Count != 1) throw new DefinitionException("布尔条件结构错误");
            foreach (var child in c.Children) ValidateCondition(child, groups, depth + 1);
        }
        else throw new DefinitionException($"未知条件操作：{c.Op}");
    }
}
