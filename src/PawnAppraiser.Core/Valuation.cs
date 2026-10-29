using System.Collections.Frozen;

namespace PawnAppraiser.Core;

public enum ValuationStatus { Incomplete, Complete, Invalid, Error }
public sealed record SelectionState(IReadOnlyDictionary<string, IReadOnlySet<string>> SelectedOptions)
{
    public bool Has(string group, string option) => SelectedOptions.TryGetValue(group, out var values) && values.Contains(option);
    public static SelectionState Empty { get; } = new(FrozenDictionary<string, IReadOnlySet<string>>.Empty);
}
public sealed record PriceLine(string Id, string Label, long Value, string Kind);
public sealed record ValuationResult(ValuationStatus Status, long? FinalPrice, long? RawPrice,
    IReadOnlyList<PriceLine> Lines, IReadOnlyList<string> ValidationErrors, bool IsMinimumApplied = false,
    bool IsEarlyComplete = false)
{
    public bool CanCopy => Status == ValuationStatus.Complete && FinalPrice is >= 0;
    public IReadOnlyList<PriceLine> AppliedModifiers => Lines.Where(x => x.Kind == "modifier").ToArray();
    public IReadOnlyList<PriceLine> AppliedBonuses => Lines.Where(x => x.Kind == "bonus").ToArray();
    public IReadOnlyList<PriceLine> AppliedOverrides => Lines.Where(x => x.Kind.EndsWith("Override", StringComparison.Ordinal)).ToArray();
    public static ValuationResult Failure(ValuationStatus status, params string[] errors) => new(status, null, null, [], errors);
}
public interface IValuationStrategy
{
    ValuationResult Evaluate(ItemDefinition item, SelectionState state);
}

public sealed class ValuationEngine
{
    private readonly Dictionary<string, IValuationStrategy> strategies = new(StringComparer.Ordinal)
        { ["generic"] = new GenericValuationStrategy() };
    public IEnumerable<string> StrategyIds => strategies.Keys;
    public void Register(string id, IValuationStrategy strategy)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        if (string.IsNullOrWhiteSpace(id) || !strategies.TryAdd(id, strategy)) throw new ArgumentException("策略 ID 为空或已注册");
    }
    public ValuationResult Evaluate(ItemDefinition item, SelectionState state)
    {
        try
        {
            DefinitionValidator.Validate(item, StrategyIds);
            var errors = ValidateState(item, state);
            if (errors.Count > 0) return ValuationResult.Failure(ValuationStatus.Invalid, errors.ToArray());
            var missing = item.Groups.Where(g => g.Required &&
                (!state.SelectedOptions.TryGetValue(g.Id, out var v) || v.Count == 0)).Select(g => g.Label).ToArray();
            if (missing.Length > 0)
            {
                // Only a proven, explicitly enabled fixed result may bypass required groups.
                if (strategies[item.Strategy] is GenericValuationStrategy generic &&
                    generic.TryEvaluateEarly(item, state) is { } early) return early;
                return ValuationResult.Failure(ValuationStatus.Incomplete, missing);
            }
            var result = strategies[item.Strategy].Evaluate(item, state);
            if (result.Status == ValuationStatus.Complete && (result.FinalPrice is null or < 0 || result.RawPrice is null))
                return ValuationResult.Failure(ValuationStatus.Error, "计算策略返回了无效价格");
            return result;
        }
        catch (DefinitionException ex) { return ValuationResult.Failure(ValuationStatus.Invalid, ex.Message); }
        catch (Exception ex) { return ValuationResult.Failure(ValuationStatus.Error, $"计算失败：{ex.GetType().Name}：{ex.Message}"); }
    }
    private static List<string> ValidateState(ItemDefinition item, SelectionState state)
    {
        var errors = new List<string>();
        foreach (var (id, selected) in state.SelectedOptions)
        {
            var group = item.Groups.SingleOrDefault(g => g.Id == id);
            if (group is null || selected is null) { errors.Add($"未知选项组或空状态：{id}"); continue; }
            if (group.SelectionType == "single" && selected.Count > 1) errors.Add($"单选组 {id} 出现多个值");
            if (selected.Any(o => !group.Options.Any(def => def.Id == o))) errors.Add($"选项组 {id} 包含未知值");
        }
        return errors;
    }
}

public sealed class GenericValuationStrategy : IValuationStrategy
{
    internal ValuationResult? TryEvaluateEarly(ItemDefinition item, SelectionState state)
    {
        var overrides = item.Rules.Where(r => r.Kind == "absoluteOverride")
            .Select(r => (Rule: r, Match: ConditionEvaluator.Match(r.When, item, state))).ToArray();
        var winner = overrides.Where(x => x.Rule.CompleteEarly && x.Match == true)
            .OrderByDescending(x => x.Rule.Priority).Select(x => x.Rule).FirstOrDefault();
        if (winner is null) return null;
        // A still-possible higher-priority outcome must not be concealed by an early result.
        if (overrides.Any(x => x.Rule.Priority > winner.Priority && x.Match != false &&
            (x.Rule.Value != winner.Value || x.Rule.ApplyBounds != winner.ApplyBounds))) return null;
        var lines = new List<PriceLine> { new(winner.Id, winner.Label, winner.Value, winner.Kind) };
        long final = winner.Value;
        bool minimumApplied = false;
        if (winner.ApplyBounds)
        {
            if (final < item.Pricing.Minimum)
            {
                lines.Add(new("minimum", "最低价值保护", item.Pricing.Minimum - final, "bound"));
                final = item.Pricing.Minimum; minimumApplied = true;
            }
            if (item.Pricing.Maximum is long max && final > max)
            { lines.Add(new("maximum", "最高价值限制", max - final, "bound")); final = max; }
        }
        // Unselected modifiers have no raw total; report only the proven fixed result.
        return new(ValuationStatus.Complete, final, null, lines, [], minimumApplied, IsEarlyComplete: true);
    }

    public ValuationResult Evaluate(ItemDefinition item, SelectionState state)
    {
        bool Matches(Condition c) => c.Op switch
        {
            "selected" => state.Has(c.Group!, c.Option!),
            "all" => c.Children!.All(Matches), "any" => c.Children!.Any(Matches),
            "not" => !Matches(c.Children![0]), _ => throw new DefinitionException("未知条件")
        };
        var matching = item.Rules.Where(r => Matches(r.When)).ToArray();
        RuleDefinition? Winner(string kind) => matching.Where(r => r.Kind == kind).OrderByDescending(r => r.Priority).FirstOrDefault();
        var lines = new List<PriceLine> { new("base", "基础价格", item.Pricing.BasePrice, "base") };
        long raw = item.Pricing.BasePrice;
        checked
        {
            var baseRule = Winner("baseOverride");
            if (baseRule is not null)
            {
                raw = baseRule.Value;
                lines.Add(new(baseRule.Id, baseRule.Label, baseRule.Value, baseRule.Kind));
            }
            foreach (var g in item.Groups.OrderBy(g => g.DisplayOrder))
                foreach (var o in g.Options.OrderBy(o => o.DisplayOrder).Where(o => state.Has(g.Id, o.Id)))
                { raw += o.PriceDelta; lines.Add(new($"{g.Id}.{o.Id}", o.Label, o.PriceDelta, "modifier")); }
            foreach (var r in matching.Where(r => r.Kind == "bonus"))
            { raw += r.Value; lines.Add(new(r.Id, r.Label, r.Value, "bonus")); }
            var absolute = Winner("absoluteOverride");
            long final = absolute?.Value ?? raw;
            if (absolute is not null) lines.Add(new(absolute.Id, absolute.Label, absolute.Value, absolute.Kind));
            var minimumApplied = false;
            if (absolute is null || absolute.ApplyBounds)
            {
                if (final < item.Pricing.Minimum)
                { lines.Add(new("minimum", "最低价值保护", item.Pricing.Minimum - final, "bound")); final = item.Pricing.Minimum; minimumApplied = true; }
                if (item.Pricing.Maximum is long max && final > max)
                { lines.Add(new("maximum", "最高价值限制", max - final, "bound")); final = max; }
            }
            return new(ValuationStatus.Complete, final, raw, lines, [], minimumApplied);
        }
    }
}
