using System.Collections.Frozen;
using System.Globalization;

namespace PawnAppraiser.Core;

public interface IClipboardService { Task WriteAsync(string digits); }
public sealed class ItemSessionState(string itemId)
{
    public string ItemId { get; } = itemId;
    internal Dictionary<string, HashSet<string>> Values { get; } = new(StringComparer.Ordinal);
    public SelectionState Snapshot => new(Values.ToFrozenDictionary(k => k.Key, v => (IReadOnlySet<string>)v.Value.ToFrozenSet()));
    public ValuationResult LastValuationResult { get; internal set; } = ValuationResult.Failure(ValuationStatus.Incomplete);
    public bool IsDirty { get; internal set; }
}
public sealed class AppraiserSession
{
    private readonly ValuationEngine engine;
    private readonly Dictionary<string, ItemSessionState> states = new(StringComparer.Ordinal);
    public IReadOnlyList<ItemDefinition> Items { get; }
    public ItemDefinition? CurrentItem { get; private set; }
    public ItemSessionState? CurrentState => CurrentItem is null ? null : states[CurrentItem.Id];
    public ValuationResult Result => CurrentState?.LastValuationResult ?? ValuationResult.Failure(ValuationStatus.Invalid, "没有有效物品");
    public event EventHandler? Changed;
    public AppraiserSession(IEnumerable<ItemDefinition> items, ValuationEngine engine)
    {
        this.engine = engine;
        Items = items.ToArray();
        foreach (var item in Items)
        {
            DefinitionValidator.Validate(item, engine.StrategyIds);
            states.Add(item.Id, new(item.Id));
            states[item.Id].LastValuationResult = engine.Evaluate(item, SelectionState.Empty);
        }
        CurrentItem = Items.FirstOrDefault();
    }
    public void SwitchItem(string id)
    {
        CurrentItem = Items.Single(i => i.Id == id);
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public void Toggle(string groupId, string optionId)
    {
        if (CurrentItem is null) return;
        var group = CurrentItem.Groups.Single(g => g.Id == groupId);
        if (!group.Options.Any(o => o.Id == optionId)) throw new ArgumentException("未知选项");
        var state = CurrentState!;
        if (!state.Values.TryGetValue(groupId, out var selected)) state.Values[groupId] = selected = [];
        if (group.SelectionType == "single") { selected.Clear(); selected.Add(optionId); }
        else if (!selected.Remove(optionId)) selected.Add(optionId);
        state.IsDirty = true;
        state.LastValuationResult = engine.Evaluate(CurrentItem, state.Snapshot);
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public void ResetCurrent()
    {
        if (CurrentItem is null) return;
        CurrentState!.Values.Clear(); CurrentState.IsDirty = false;
        CurrentState.LastValuationResult = engine.Evaluate(CurrentItem, CurrentState.Snapshot);
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public async Task<bool> CopyAsync(IClipboardService clipboard)
    {
        var result = Result;
        if (!result.CanCopy) return false;
        await clipboard.WriteAsync(result.FinalPrice!.Value.ToString(CultureInfo.InvariantCulture));
        return true;
    }
}
