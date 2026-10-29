using System.Text.Json;

namespace PawnAppraiser.Core;

public sealed record WindowPosition(double X = 180, double Y = 90);
public sealed record WindowSize(double Width = 440, double Height = 800);
public sealed record Hotkey(uint VirtualKey, uint Modifiers = 0)
{
    public string Display => $"{(Modifiers == 0 ? "" : $"修饰键0x{Modifiers:X}+")}0x{VirtualKey:X2}";
}
public sealed record AppSettings
{
    public int SchemaVersion { get; init; } = 1;
    public WindowPosition WindowPosition { get; init; } = new();
    public WindowSize WindowSize { get; init; } = new();
    public bool Topmost { get; init; } = true;
    public string Theme { get; init; } = "dark";
    public double Opacity { get; init; } = 0.93;
    public Hotkey CollapseHotkey { get; init; } = new(0x6A);
    public Hotkey VisibilityHotkey { get; init; } = new(0x6F);
    public bool IsValid => SchemaVersion == 1 && Theme == "dark" && WindowPosition is not null && WindowSize is not null &&
        double.IsFinite(WindowPosition.X) && double.IsFinite(WindowPosition.Y) &&
        double.IsFinite(WindowSize.Width) && double.IsFinite(WindowSize.Height) && WindowSize.Width >= 100 && WindowSize.Height >= 100 &&
        double.IsFinite(Opacity) && Opacity is >= 0.85 and <= 1 && ValidHotkey(CollapseHotkey) && ValidHotkey(VisibilityHotkey) && CollapseHotkey != VisibilityHotkey;
    public static bool ValidHotkey(Hotkey? h) => h is not null && h.VirtualKey is >= 0x08 and <= 0xFE && (h.Modifiers & ~0x0Fu) == 0;
}
public readonly record struct WindowBounds(double X, double Y, double Width, double Height);
public static class WindowLayout
{
    public static WindowBounds Constrain(WindowBounds value, WindowBounds work, double minWidth, double minHeight, double maxWidth)
    {
        var width = Math.Clamp(value.Width, Math.Min(minWidth, work.Width), Math.Min(maxWidth, work.Width));
        var height = Math.Clamp(value.Height, Math.Min(minHeight, work.Height), work.Height);
        return new(Math.Clamp(value.X, work.X, work.X + work.Width - width),
            Math.Clamp(value.Y, work.Y, work.Y + work.Height - height), width, height);
    }
    public static WindowBounds Snap(WindowBounds value, WindowBounds work, double distance)
    {
        double x = value.X, y = value.Y;
        if (Math.Abs(x - work.X) <= distance) x = work.X;
        else if (Math.Abs(x + value.Width - work.X - work.Width) <= distance) x = work.X + work.Width - value.Width;
        if (Math.Abs(y - work.Y) <= distance) y = work.Y;
        else if (Math.Abs(y + value.Height - work.Y - work.Height) <= distance) y = work.Y + work.Height - value.Height;
        return value with { X = x, Y = y };
    }
}
public sealed class OverlayState
{
    public bool Visible { get; private set; } = true;
    public bool Expanded { get; private set; } = true;
    public void ToggleVisibility() => Visible = !Visible;
    public void Show() => Visible = true;
    public void Hide() => Visible = false;
    public void ToggleExpanded() { if (Visible) Expanded = !Expanded; }
}

public sealed class SettingsStore(string root)
{
    public string Path { get; } = System.IO.Path.Combine(root, "config", "settings.json");
    public AppSettings Load(out string? warning)
    {
        warning = null;
        try
        {
            if (!File.Exists(Path)) return new();
            var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path), ItemJson.Options);
            if (s?.IsValid == true) return s;
            warning = "窗口设置无效，已恢复安全默认值";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { warning = $"无法读取窗口设置，使用默认值：{ex.Message}"; }
        return new();
    }
    public void Save(AppSettings settings)
    {
        if (!settings.IsValid) throw new ArgumentException("拒绝保存无效设置");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var temp = Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(settings, ItemJson.Options)); File.Move(temp, Path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
