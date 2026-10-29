using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PawnAppraiser.Core;
using PawnAppraiser.Desktop;
using Xunit;

namespace PawnAppraiser.Desktop.Tests;

public class DesktopIntegrationTests
{
    [Fact]
    public void CompiledWpfAndWindowsServicesIntegration()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { RunScenario(); } catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(45)), "STA场景超时");
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
    private static void Settle()
    {
        var frame = new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
    }
    private static void RunScenario()
    {
        var testRoot = Path.Combine(AppContext.BaseDirectory, "desktop-evidence"); Directory.CreateDirectory(testRoot);
        var app = new App(); app.InitializeComponent();
        var engine = new ValuationEngine(); var catalog = ItemCatalog.Load(Path.Combine(AppContext.BaseDirectory, "items"), engine);
        Assert.Empty(catalog.Issues); var session = new AppraiserSession(catalog.Items, engine);
        var clipboard = new FakeClipboard(); var vm = new MainViewModel(session, clipboard);
        var window = new MainWindow(vm, new(), new(testRoot), new(testRoot), []); app.MainWindow = window;
        try
        {
            window.Show(); Settle();
            Assert.True(window.Topmost); Assert.Equal("—", vm.Price); Assert.False(vm.CopyCommand.CanExecute(null));
            void ClickOption(string group, string id)
            {
                var button = Descendants<ToggleButton>(window).Single(b => b.DataContext is OptionViewModel o && o.GroupId == group && o.Definition.Id == id);
                typeof(ButtonBase).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(button, null);
                Settle();
            }
            ClickOption("material", "carbon"); ClickOption("condition", "intact"); ClickOption("interface", "emg_plus"); ClickOption("components", "armor"); ClickOption("components", "blade");
            Assert.Equal("425", vm.Price); Assert.Contains("战术型", vm.Bonuses);
            ClickOption("material", "carbon"); Assert.Equal("425", vm.Price);
            Assert.True(Descendants<ToggleButton>(window).Single(b => b.DataContext is OptionViewModel o && o.GroupId == "material" && o.Definition.Id == "carbon").IsChecked);
            vm.CopyAsync().GetAwaiter().GetResult(); Assert.Equal("425", clipboard.Text);
            var source = (HwndSource)PresentationSource.FromVisual(window)!;
            using (var conflict = new HotkeyService(source.Handle, new(testRoot)))
            {
                Assert.Equal(2, conflict.Bind(new(0x6A), new(0x6F)).Length);
            }
            foreach (double width in new[] { 320.0, 440.0, 640.0 })
            {
                window.Width = width; window.Height = 800; Settle();
                Assert.True(window.ActualWidth >= window.MinWidth);
                SaveRender(window, Path.Combine(testRoot, $"layout-{width}.png"));
            }
            window.Width = 320; window.Height = 430; Settle();
            ((Expander)window.FindName("DetailsExpander")).IsExpanded = true; Settle();
            AssertVisible(window, (FrameworkElement)window.FindName("PriceButton"));
            AssertVisible(window, Descendants<Button>(window).Single(b => Equals(b.Content, "下一件")));
            SaveRender(window, Path.Combine(testRoot, "minimum-details.png"));
            ((Expander)window.FindName("DetailsExpander")).IsExpanded = false; window.Height = 800; Settle();
            window.ToggleExpanded(); Assert.False(window.IsExpanded); Assert.Equal(46, window.Height);
            window.ToggleVisibility(); Assert.False(window.IsVisible);
            window.ToggleExpanded(); Assert.False(window.IsVisible); Assert.False(window.IsExpanded);
            window.ToggleVisibility(); Assert.True(window.IsVisible); Assert.False(window.IsExpanded);
            Assert.Equal("425", vm.Price); window.ToggleExpanded(); Assert.Equal(800, window.Height);
            PostMessage(source.Handle, 0x312, 1, 0); Settle(); Assert.False(window.IsExpanded);
            PostMessage(source.Handle, 0x312, 1, 0); Settle(); Assert.True(window.IsExpanded);
            // WPF Closing is the same path used by the title X and Alt+F4.
            window.Close(); Settle(); Assert.False(window.IsVisible); Assert.Equal("425", vm.Price); window.RestoreExisting();
            vm.ResetCommand.Execute(null); Assert.Equal("—", vm.Price); Assert.Equal("425", clipboard.Text);
            Assert.Contains("selectedOptions", System.Text.Json.JsonSerializer.Serialize(session.CurrentState!.Snapshot, ItemJson.Options));
            Assert.DoesNotContain("selectedOptions", File.ReadAllText(new SettingsStore(testRoot).Path));
            Assert.Equal(new[] { "bionic_hand", "plasma_fuel_cell", "ribwich" }, vm.Items.Select(i => i.Id));
            var navigation = Descendants<ComboBox>(window).Single();
            navigation.SelectedItem = vm.Items.Single(i => i.Id == "ribwich"); Settle();
            Assert.Equal("—", vm.Price); Assert.Equal(7, vm.Groups.Count);
            ClickOption("packaging", "opened");
            Assert.Equal("40", vm.Price); Assert.True(vm.CopyCommand.CanExecute(null));
            Assert.Contains("无需补选", vm.Status); Assert.DoesNotContain("原始总价", vm.Details);
            vm.CopyAsync().GetAwaiter().GetResult(); Assert.Equal("40", clipboard.Text);
            SaveRender(window, Path.Combine(testRoot, "early-fixed-40.png"));
            ClickOption("packaging", "intact"); Assert.Equal("—", vm.Price);
            Assert.False(vm.CopyCommand.CanExecute(null)); Assert.Equal("40", clipboard.Text);
            vm.ResetCommand.Execute(null);
            ClickOption("ribwich", "correct"); Assert.Equal("—", vm.Price);
            vm.ResetCommand.Execute(null);
            ClickOption("packaging", "intact"); ClickOption("date", "first_month");
            ClickOption("factory", "high"); ClickOption("edition", "deluxe"); ClickOption("patties", "double");
            ClickOption("brand", "correct");
            Assert.False(vm.CopyCommand.CanExecute(null));
            ClickOption("ribwich", "missing"); Assert.Equal("1430", vm.Price);
            vm.CopyAsync().GetAwaiter().GetResult(); Assert.Equal("1430", clipboard.Text);
            foreach (double width in new[] { 320.0, 440.0, 640.0 })
            {
                window.Width = width; window.Height = 800; Settle();
                AssertVisible(window, (FrameworkElement)window.FindName("PriceButton"));
                SaveRender(window, Path.Combine(testRoot, $"ribwich-{width}.png"));
            }
            ClickOption("ribwich", "correct"); Assert.Equal("40", vm.Price);
            Assert.DoesNotContain("正品豪华版", vm.Bonuses);
            ClickOption("date", "second_other"); Assert.Equal("1300", vm.Price);
            navigation.SelectedItem = vm.Items.Single(i => i.Id == "bionic_hand"); Settle();
            Assert.Equal("—", vm.Price);
            navigation.SelectedItem = vm.Items.Single(i => i.Id == "ribwich"); Settle();
            Assert.Equal("1300", vm.Price);
            window.Width = 320; window.Height = 430;
            ((Expander)window.FindName("DetailsExpander")).IsExpanded = true; Settle();
            AssertVisible(window, (FrameworkElement)window.FindName("PriceButton"));
            AssertVisible(window, Descendants<Button>(window).Single(b => Equals(b.Content, "下一件")));
            var optionScroller = Descendants<ScrollViewer>(window).First(s => s.Content is ItemsControl);
            optionScroller.ScrollToBottom(); Settle();
            var lastOption = Descendants<ToggleButton>(window).Single(b => b.DataContext is OptionViewModel o && o.GroupId == "ribwich" && o.Definition.Id == "wrong");
            Assert.True(lastOption.IsVisible); lastOption.BringIntoView(); Settle();
            Assert.True(lastOption.TransformToAncestor(optionScroller).TransformBounds(new Rect(lastOption.RenderSize)).IntersectsWith(new Rect(optionScroller.RenderSize)));
            SaveRender(window, Path.Combine(testRoot, "ribwich-minimum-details.png"));
            vm.ResetCommand.Execute(null); Assert.Equal("—", vm.Price); Assert.Equal("1430", clipboard.Text);
            navigation.SelectedItem = vm.Items.Single(i => i.Id == "plasma_fuel_cell"); Settle();
            Assert.Equal("—", vm.Price); Assert.Equal(5, vm.Groups.Count);
            Assert.False(vm.CopyCommand.CanExecute(null));
            ClickOption("wear", "standard"); ClickOption("color", "standard");
            ClickOption("manufacturer", "rnr");
            Assert.False(vm.CopyCommand.CanExecute(null));
            ClickOption("security_mark", "present"); Assert.Equal("420", vm.Price);
            ClickOption("features", "reinforced"); Assert.Equal("480", vm.Price);
            ClickOption("features", "cracked"); Assert.Equal("210", vm.Price); Assert.Empty(vm.Bonuses);
            ClickOption("features", "cracked"); Assert.Equal("480", vm.Price);
            ClickOption("features", "exposed_screws"); Assert.Equal("450", vm.Price);
            ClickOption("wear", "light"); ClickOption("wear", "heavy"); ClickOption("wear", "heavy");
            Assert.Equal("360", vm.Price);
            Assert.Single(Descendants<ToggleButton>(window), b => b.DataContext is OptionViewModel o &&
                o.GroupId == "wear" && b.IsChecked == true);
            Assert.Equal(2, Descendants<ToggleButton>(window).Count(b => b.DataContext is OptionViewModel o &&
                o.GroupId == "features" && b.IsChecked == true));
            ClickOption("color", "low"); Assert.Equal("90", vm.Price);
            Assert.DoesNotContain("最低价值保护", vm.Status);
            ClickOption("features", "reinforced"); Assert.Equal("90", vm.Price);
            Assert.Contains("最低价值保护", vm.Status);
            Assert.Contains("原始总价：30", vm.Details);
            vm.CopyAsync().GetAwaiter().GetResult(); Assert.Equal("90", clipboard.Text);
            foreach (double width in new[] { 320.0, 440.0, 640.0 })
            {
                window.Width = width; window.Height = 800; Settle();
                AssertVisible(window, (FrameworkElement)window.FindName("PriceButton"));
                SaveRender(window, Path.Combine(testRoot, $"plasma-fuel-cell-{width}.png"));
            }
            navigation.SelectedItem = vm.Items.Single(i => i.Id == "ribwich"); Settle(); Assert.Equal("—", vm.Price);
            navigation.SelectedItem = vm.Items.Single(i => i.Id == "plasma_fuel_cell"); Settle(); Assert.Equal("90", vm.Price);
            vm.ResetCommand.Execute(null); Assert.False(vm.CopyCommand.CanExecute(null)); Assert.Equal("90", clipboard.Text);
            var longItem = catalog.Items[0] with { Id = "long_labels", Rules = catalog.Items[0].Rules.Select(r => r with { Label = new string('长', 500) }).ToList() };
            var longSession = new AppraiserSession([longItem], engine);
            var longVm = new MainViewModel(longSession, clipboard); window.DataContext = longVm;
            longSession.Toggle("material", "gold"); longSession.Toggle("condition", "intact"); longSession.Toggle("interface", "neural");
            window.Width = 320; window.Height = 430; Settle();
            ((Expander)window.FindName("DetailsExpander")).IsExpanded = true; Settle();
            AssertVisible(window, (FrameworkElement)window.FindName("PriceButton"));
            AssertVisible(window, Descendants<Button>(window).Single(b => Equals(b.Content, "下一件")));
            SaveRender(window, Path.Combine(testRoot, "long-labels-minimum.png"));
            // The second window uses a small logical monitor and forces the normal taskbar fallback.
            var fallback = new MainWindow(vm, new(), new(testRoot), new(testRoot), [])
                { WorkAreaOverride = () => new(0, 0, 280, 400), TrayAvailabilityOverride = false };
            fallback.Show(); Settle(); Assert.True(fallback.ActualHeight <= 400); Assert.True(fallback.ActualWidth <= 280);
            fallback.ToggleVisibility(); Assert.Equal(WindowState.Minimized, fallback.WindowState);
            fallback.ToggleVisibility(); Settle(); Assert.Equal(WindowState.Normal, fallback.WindowState);
            fallback.ToggleVisibility(); fallback.WindowState = WindowState.Normal; Settle();
            fallback.ToggleVisibility(); Assert.Equal(WindowState.Minimized, fallback.WindowState);
            typeof(MainWindow).GetField("exiting", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(fallback, true);
            fallback.Close();
            File.WriteAllText(Path.Combine(testRoot, "integration-result.txt"), "PASS: compiled XAML; 320/440/640 render; dynamic MVVM; copy format; native key conflict; WM_HOTKEY dispatch; close-to-tray; collapse/visibility independence; settings isolation; release re-registration.\nPhysical key/game/DPI acceptance remains manual.");
        }
        finally { window.ExitApplication(); }
        using var checkWindow = new HwndSource(new HwndSourceParameters("ReleaseCheck") { Width = 1, Height = 1, WindowStyle = 0 });
        using var keys = new HotkeyService(checkWindow.Handle, new(testRoot)); Assert.Empty(keys.Bind(new(0x6A), new(0x6F)));
    }
    private static void SaveRender(Window window, string path)
    {
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path); encoder.Save(output);
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); if (child is T t) yield return t; foreach (var next in Descendants<T>(child)) yield return next; }
    }
    private static void AssertVisible(Window window, FrameworkElement element)
    {
        var bounds = element.TransformToAncestor(window).TransformBounds(new Rect(element.RenderSize));
        Assert.True(bounds.Bottom <= window.ActualHeight && bounds.Top >= 0 && bounds.Right <= window.ActualWidth, $"控件越界：{bounds} / {window.RenderSize}");
    }
    private class FakeClipboard : IClipboardService
    { public string? Text; public Task WriteAsync(string digits) { Text = digits; return Task.CompletedTask; } }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostMessage(nint hwnd, uint msg, nint w, nint l);
}
