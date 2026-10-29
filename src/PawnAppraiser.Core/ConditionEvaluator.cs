namespace PawnAppraiser.Core;

// null means that a required group has not yet been checked. In particular, NOT unknown is unknown.
internal static class ConditionEvaluator
{
    internal static bool? Match(Condition condition, ItemDefinition item, SelectionState state)
    {
        if (condition.Op == "selected")
        {
            var group = item.Groups.Single(g => g.Id == condition.Group);
            if (group.Required && (!state.SelectedOptions.TryGetValue(group.Id, out var selected) || selected.Count == 0))
                return null;
            return state.Has(group.Id, condition.Option!);
        }
        if (condition.Op == "not") return !Match(condition.Children![0], item, state);
        var values = condition.Children!.Select(c => Match(c, item, state)).ToArray();
        return condition.Op switch
        {
            "all" => values.Contains(false) ? false : values.Contains(null) ? null : true,
            "any" => values.Contains(true) ? true : values.Contains(null) ? null : false,
            _ => throw new DefinitionException("未知条件")
        };
    }
}
