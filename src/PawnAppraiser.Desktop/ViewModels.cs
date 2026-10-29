using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;
using PawnAppraiser.Core;

namespace PawnAppraiser.Desktop;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
public sealed class ActionCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) => execute();
    public event EventHandler? CanExecuteChanged;
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
public sealed class OptionViewModel : Observable
{
    private readonly AppraiserSession session;
    public string GroupId { get; }
    public OptionDefinition Definition { get; }
    public string Label => Definition.Label;
    public string Delta => Definition.PriceDelta.ToString("+0;-0;±0", CultureInfo.InvariantCulture);
    public string? Description => Definition.OptionalDescription;
    public bool IsSelected => session.CurrentState?.Snapshot.Has(GroupId, Definition.Id) == true;
    public ICommand ToggleCommand { get; }
    public OptionViewModel(AppraiserSession session, string group, OptionDefinition option)
    {
        this.session = session; GroupId = group; Definition = option;
        ToggleCommand = new ActionCommand(() => session.Toggle(group, option.Id));
    }
    public void Refresh() => Raise(nameof(IsSelected));
}
public sealed record GroupViewModel(string Label, string Hint, IReadOnlyList<OptionViewModel> Options);
public sealed class MainViewModel : Observable
{
    private readonly AppraiserSession session;
    private readonly IClipboardService clipboard;
    private readonly DispatcherTimer feedbackTimer;
    private bool copying;
    private string feedback = "";
    public IReadOnlyList<ItemDefinition> Items => session.Items;
    public ItemDefinition? SelectedItem
    {
        get => session.CurrentItem;
        set { if (value is not null && value.Id != session.CurrentItem?.Id) { session.SwitchItem(value.Id); BuildGroups(); Raise(); } }
    }
    public IReadOnlyList<GroupViewModel> Groups { get; private set; } = [];
    public string Price => session.Result.CanCopy ? session.Result.FinalPrice!.Value.ToString(CultureInfo.InvariantCulture) : "—";
    public string Status => session.Result.Status switch
    {
        ValuationStatus.Incomplete => $"待选：{string.Join("、", session.Result.ValidationErrors)}",
        ValuationStatus.Complete when session.Result.IsEarlyComplete => "已确定固定价 · 无需补选其余项目",
        ValuationStatus.Complete => session.Result.IsMinimumApplied ? "鉴定完成 · 最低价值保护" : "鉴定完成",
        _ => string.Join("；", session.Result.ValidationErrors)
    };
    public string Bonuses => string.Join(" · ", session.Result.AppliedBonuses.Select(x => $"{x.Label} +{x.Value}"));
    public string Details => session.Result.CanCopy ? string.Join(Environment.NewLine, session.Result.Lines.Select(x =>
        $"{x.Label}：{(x.Kind is "modifier" or "bonus" or "bound" ? x.Value.ToString("+0;-0;0", CultureInfo.InvariantCulture) : x.Value.ToString(CultureInfo.InvariantCulture))}")) +
        (session.Result.IsEarlyComplete ? "\n已确定固定价，无需补选其余项目" : $"\n原始总价：{session.Result.RawPrice}") +
        $"\n最终价格：{session.Result.FinalPrice}" : "选齐必选项或确定固定价后显示明细";
    public string Feedback { get => feedback; private set { feedback = value; Raise(); } }
    public ActionCommand CopyCommand { get; }
    public ActionCommand ResetCommand { get; }
    public MainViewModel(AppraiserSession session, IClipboardService clipboard)
    {
        this.session = session; this.clipboard = clipboard;
        CopyCommand = new(() => _ = CopyAsync(), () => session.Result.CanCopy && !copying);
        ResetCommand = new(() => { session.ResetCurrent(); Feedback = "已准备下一件"; }, () => session.CurrentItem is not null);
        feedbackTimer = new(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) => { Feedback = ""; feedbackTimer!.Stop(); }, Dispatcher.CurrentDispatcher);
        feedbackTimer.Stop(); session.Changed += (_, _) => Refresh(); BuildGroups();
    }
    private void BuildGroups()
    {
        Groups = session.CurrentItem?.Groups.OrderBy(g => g.DisplayOrder).Select(g => new GroupViewModel(g.Label,
            (g.Required ? "必选" : "可选") + (g.SelectionType == "single" ? " · 单选" : " · 多选"),
            g.Options.OrderBy(o => o.DisplayOrder).Select(o => new OptionViewModel(session, g.Id, o)).ToArray())).ToArray() ?? [];
        Raise(nameof(Groups)); Refresh();
    }
    private void Refresh()
    {
        foreach (var g in Groups) foreach (var o in g.Options) o.Refresh();
        foreach (var name in new[] { nameof(Price), nameof(Status), nameof(Bonuses), nameof(Details) }) Raise(name);
        Feedback = ""; CopyCommand.Refresh(); ResetCommand.Refresh();
    }
    public async Task CopyAsync()
    {
        if (!CopyCommand.CanExecute(null)) return;
        copying = true; CopyCommand.Refresh();
        try { if (await session.CopyAsync(clipboard)) Feedback = "已复制纯数字"; }
        catch (Exception ex) { Feedback = $"复制失败：{ex.Message}"; }
        finally { copying = false; CopyCommand.Refresh(); feedbackTimer.Start(); }
    }
}
