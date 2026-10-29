using System.Windows;
using PawnAppraiser.Core;

namespace PawnAppraiser.Desktop;

public partial class SettingsWindow : Window
{
    public sealed record Choice(string Name, Hotkey Key);
    public AppSettings Result { get; private set; }
    public SettingsWindow(AppSettings settings, string path)
    {
        InitializeComponent(); Result = settings; TopmostBox.IsChecked = settings.Topmost; OpacitySlider.Value = settings.Opacity;
        var choices = new List<Choice> { new("小键盘 *", new(0x6A)), new("小键盘 /", new(0x6F)) };
        for (uint key = 0x75; key <= 0x7B; key++) { choices.Add(new($"F{key - 0x6F}", new(key))); choices.Add(new($"Ctrl + Alt + F{key - 0x6F}", new(key, 3))); }
        foreach (var key in new[] { settings.CollapseHotkey, settings.VisibilityHotkey }) if (!choices.Any(c => c.Key == key)) choices.Add(new(key.Display, key));
        CollapseKeys.ItemsSource = choices; VisibilityKeys.ItemsSource = choices;
        CollapseKeys.SelectedItem = choices.First(c => c.Key == settings.CollapseHotkey);
        VisibilityKeys.SelectedItem = choices.First(c => c.Key == settings.VisibilityHotkey);
        SavePath.Text = "设置文件：" + path + "\n主题：深色。鉴定选择只在本次运行中保留。";
    }
    private void Save(object sender, RoutedEventArgs e)
    {
        var collapse = ((Choice)CollapseKeys.SelectedItem).Key; var visibility = ((Choice)VisibilityKeys.SelectedItem).Key;
        if (collapse == visibility) { ErrorText.Text = "两项快捷键不能相同。"; return; }
        Result = Result with { Topmost = TopmostBox.IsChecked == true, Opacity = OpacitySlider.Value, CollapseHotkey = collapse, VisibilityHotkey = visibility };
        DialogResult = true;
    }
}
