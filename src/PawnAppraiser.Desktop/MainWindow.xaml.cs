using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using PawnAppraiser.Core;

namespace PawnAppraiser.Desktop;

public partial class MainWindow : Window
{
    private AppSettings settings;
    private readonly SettingsStore store;
    private readonly DiagnosticLog log;
    private readonly List<string> initialWarnings;
    private readonly OverlayState overlay = new();
    private HotkeyService? hotkeys;
    private TrayService? tray;
    private SettingsWindow? settingsWindow;
    private nint handle;
    private double expandedWidth, expandedHeight;
    private bool exiting, adjusting, platformDisposed;
    private double scale = 1;
    public bool IsExpanded => overlay.Expanded;
    internal Func<WindowBounds>? WorkAreaOverride { get; init; }
    internal bool? TrayAvailabilityOverride { get; init; }
    private bool TrayAvailable => TrayAvailabilityOverride ?? tray?.Available == true;
    public MainWindow(MainViewModel viewModel, AppSettings settings, SettingsStore store, DiagnosticLog log, IEnumerable<string> warnings)
    {
        InitializeComponent(); DataContext = viewModel;
        this.settings = settings; this.store = store; this.log = log; initialWarnings = warnings.ToList();
        SourceInitialized += InitializePlatform; Closing += OnClosing;
        StateChanged += (_, _) => { if (WindowState == WindowState.Normal && IsVisible) overlay.Show(); };
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(() => { UpdateMetrics(); Constrain(); });
        Closed += (_, _) => DisposePlatform();
    }
    private void InitializePlatform(object? sender, EventArgs e)
    {
        handle = new WindowInteropHelper(this).Handle; UpdateMetrics();
        Width = settings.WindowSize.Width / scale; Height = settings.WindowSize.Height / scale;
        Left = settings.WindowPosition.X / scale; Top = settings.WindowPosition.Y / scale;
        Topmost = settings.Topmost; SetBackground(); Constrain();
        expandedWidth = Width; expandedHeight = Height;
        hotkeys = new(handle, log); hotkeys.Collapse += ToggleExpanded; hotkeys.Visibility += ToggleVisibility;
        tray = new(handle, TrayAction, log); tray.AvailabilityChanged += available =>
        { if (!available) { ShowInTaskbar = true; RestoreExisting(); ShowWarnings(["托盘不可用，已保留任务栏入口"]); } };
        ShowWarnings(hotkeys.Bind(settings.CollapseHotkey, settings.VisibilityHotkey));
        if (!tray.Available) { ShowInTaskbar = true; ShowWarnings(["托盘不可用，已保留任务栏入口"]); }
        log.Write($"WINDOW ready dpi={96 * scale} bounds={Left},{Top},{Width},{Height}");
    }
    private void ShowWarnings(IEnumerable<string> extra) => DiagnosticBanner.Text = string.Join("\n", initialWarnings.Concat(extra));
    private void SetBackground() => Shell.Background = new SolidColorBrush(Color.FromArgb((byte)Math.Round(settings.Opacity * 255), 15, 23, 36));
    private void UpdateMetrics()
    {
        scale = handle == 0 ? 1 : Math.Max(96, NativeMethods.GetDpiForWindow(handle)) / 96.0;
        var work = WorkArea();
        MinWidth = Math.Min(Math.Max(300, 320 / scale), work.Width);
        MaxWidth = Math.Min(Math.Max(MinWidth, 640 / scale), work.Width);
        MinHeight = Math.Min(overlay.Expanded ? 430 : 46, work.Height);
        MaxHeight = work.Height;
    }
    private WindowBounds WorkArea()
    {
        if (WorkAreaOverride is not null) return WorkAreaOverride();
        var info = new NativeMethods.MonitorInfo { Size = (uint)Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (handle != 0 && NativeMethods.GetMonitorInfo(NativeMethods.MonitorFromWindow(handle, 2), ref info))
            return new(info.Work.Left / scale, info.Work.Top / scale, (info.Work.Right - info.Work.Left) / scale, (info.Work.Bottom - info.Work.Top) / scale);
        var area = SystemParameters.WorkArea; return new(area.Left, area.Top, area.Width, area.Height);
    }
    private void Constrain(bool snap = false)
    {
        if (adjusting) return;
        adjusting = true;
        try
        {
            var work = WorkArea(); var bounds = new WindowBounds(Left, Top, Width, Height);
            bounds = WindowLayout.Constrain(bounds, work, MinWidth, MinHeight, MaxWidth);
            if (snap) bounds = WindowLayout.Snap(bounds, work, 12 / scale);
            Width = bounds.Width; Height = bounds.Height; Left = bounds.X; Top = bounds.Y;
        }
        finally { adjusting = false; }
    }
    private void DragTitle(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.OriginalSource is DependencyObject d && HasButtonAncestor(d)) return;
        try { DragMove(); Constrain(true); } catch (InvalidOperationException) { }
    }
    private static bool HasButtonAncestor(DependencyObject current)
    {
        for (var p = current; p is not null; p = VisualTreeHelper.GetParent(p)) if (p is ButtonBase) return true;
        return false;
    }
    private void ResizeDrag(object sender, DragDeltaEventArgs e)
    {
        if (!overlay.Expanded) return;
        Width = Math.Clamp(Width + e.HorizontalChange, MinWidth, MaxWidth);
        Height = Math.Clamp(Height + e.VerticalChange, MinHeight, MaxHeight); Constrain();
    }
    private void ToggleCollapse(object sender, RoutedEventArgs e) => ToggleExpanded();
    public void ToggleExpanded()
    {
        if (!overlay.Visible) return;
        if (overlay.Expanded) { expandedWidth = Width; expandedHeight = Height; }
        overlay.ToggleExpanded();
        ExpandedContent.Visibility = overlay.Expanded ? Visibility.Visible : Visibility.Collapsed;
        ResizeGrip.Visibility = ExpandedContent.Visibility;
        UpdateMetrics();
        Height = overlay.Expanded ? expandedHeight : 46; Width = expandedWidth;
        CollapseButton.Content = overlay.Expanded ? "−" : "+"; Constrain();
        log.Write($"WINDOW expanded={overlay.Expanded}");
    }
    private void HideClicked(object sender, RoutedEventArgs e) => HideToTray();
    private void HideToTray()
    {
        if (settingsWindow?.IsVisible == true) settingsWindow.Close();
        SaveSettings();
        if (!TrayAvailable) { overlay.Hide(); ShowInTaskbar = true; WindowState = WindowState.Minimized; return; }
        overlay.Hide(); Hide(); log.Write("WINDOW hidden");
    }
    public void ToggleVisibility() { if (overlay.Visible) HideToTray(); else RestoreExisting(); }
    public void RestoreExisting()
    {
        if (exiting) return;
        overlay.Show(); Show(); WindowState = WindowState.Normal; UpdateMetrics(); Constrain(); Activate(); log.Write("WINDOW shown");
    }
    private void TrayAction(int command)
    {
        switch (command) { case 1: ToggleVisibility(); break; case 2: ToggleExpanded(); break; case 3: OpenSettings(this, new()); break; case 4: ExitApplication(); break; }
    }
    private void OpenSettings(object sender, RoutedEventArgs e)
    {
        RestoreExisting();
        if (settingsWindow is not null) { settingsWindow.Activate(); return; }
        settingsWindow = new(settings, store.Path) { Owner = this };
        try
        {
            if (settingsWindow.ShowDialog() == true)
            {
                settings = settingsWindow.Result; Topmost = settings.Topmost; SetBackground();
                ShowWarnings(hotkeys!.Bind(settings.CollapseHotkey, settings.VisibilityHotkey)); SaveSettings();
            }
        }
        finally { settingsWindow = null; }
    }
    private void SaveSettings()
    {
        try
        {
            if (overlay.Expanded) { expandedWidth = Width; expandedHeight = Height; }
            settings = settings with { WindowPosition = new(Left * scale, Top * scale), WindowSize = new(expandedWidth * scale, expandedHeight * scale) };
            store.Save(settings);
        }
        catch (Exception ex) { log.Write($"SETTINGS save failed: {ex.Message}"); ShowWarnings([$"设置保存失败：{ex.Message}"]); }
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (exiting) { DisposePlatform(); return; }
        e.Cancel = true; HideToTray();
    }
    private void DisposePlatform()
    {
        if (platformDisposed) return;
        platformDisposed = true; hotkeys?.Dispose(); tray?.Dispose();
    }
    private void ContentSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (DetailsScroller is not null)
            DetailsScroller.MaxHeight = Math.Clamp(ExpandedContent.ActualHeight - 380 - DiagnosticBanner.ActualHeight, 20, 145);
    }
    public void ExitApplication()
    {
        if (exiting) return;
        exiting = true; settingsWindow?.Close(); SaveSettings(); Close(); Application.Current.Shutdown();
    }
}
