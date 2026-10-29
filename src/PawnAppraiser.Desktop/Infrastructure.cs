using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using PawnAppraiser.Core;

namespace PawnAppraiser.Desktop;

public static class Storage
{
    public static string SelectRoot(string preferred, out string? warning)
    {
        warning = null;
        try
        {
            var config = Path.Combine(preferred, "config"); Directory.CreateDirectory(config);
            var probe = Path.Combine(config, ".write-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(probe, ""); File.Delete(probe); return preferred;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PawnAppraiser");
            warning = $"程序目录不可写；设置和日志保存于：{fallback}"; return fallback;
        }
    }
}
public sealed class DiagnosticLog(string root)
{
    private readonly object gate = new();
    public void Write(string message)
    {
        try { lock (gate) { Directory.CreateDirectory(Path.Combine(root, "logs")); File.AppendAllText(Path.Combine(root, "logs", "app.log"), $"{DateTimeOffset.UtcNow:O} {message}{Environment.NewLine}"); } }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { System.Diagnostics.Debug.WriteLine(ex); }
    }
}
public sealed class WindowsClipboardService : IClipboardService
{
    public async Task WriteAsync(string digits)
    {
        // WPF continuation returns to its STA dispatcher; never uses a worker thread for clipboard access.
        for (int attempt = 0; ; attempt++)
        {
            try { Clipboard.SetText(digits, TextDataFormat.UnicodeText); return; }
            catch (COMException) when (attempt < 3) { await Task.Delay(50); }
        }
    }
}
