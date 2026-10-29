using System.Windows;
using PawnAppraiser.Core;

namespace PawnAppraiser.Desktop;

public partial class App : Application
{
    private SingleInstance? instance;
    private DiagnosticLog? log;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            instance = new();
            if (!instance.IsOwner) { Shutdown(); return; }
            var root = Storage.SelectRoot(AppContext.BaseDirectory, out var storageWarning);
            log = new(root); log.Write($"START runtime={Environment.Version} x64={Environment.Is64BitProcess} base={AppContext.BaseDirectory}");
            var engine = new ValuationEngine();
            // New trusted C# strategies are explicitly registered here; never load arbitrary external DLLs.
            var catalog = ItemCatalog.Load(System.IO.Path.Combine(AppContext.BaseDirectory, "items"), engine);
            var store = new SettingsStore(root); var settings = store.Load(out var settingsWarning);
            var warnings = catalog.Issues.Select(i => $"{i.File}：{i.Message}").ToList();
            if (!catalog.Items.Any(i => i.Id == "bionic_hand")) warnings.Insert(0, "仿生机械手配置无效或缺失，机械手鉴定不可用");
            if (storageWarning is not null) warnings.Add(storageWarning);
            if (settingsWarning is not null) warnings.Add(settingsWarning);
            foreach (var warning in warnings) log.Write(warning);
            var window = new MainWindow(new(new(catalog.Items, engine), new WindowsClipboardService()), settings, store, log, warnings);
            MainWindow = window;
            instance.Listen(() => Dispatcher.BeginInvoke(() => window.RestoreExisting()));
            if (e.Args.Length == 2 && e.Args[0] == "--smoke-test")
            {
                var started = false;
                window.ContentRendered += (_, _) =>
                {
                    if (started) return;
                    started = true; _ = StartupSmoke.RunAsync(window, (MainViewModel)window.DataContext, e.Args[1]);
                };
            }
            window.Show();
            DispatcherUnhandledException += (_, args) =>
            {
                log.Write($"UNHANDLED {args.Exception}");
                MessageBox.Show("发生意外错误，程序将退出以避免错误鉴定。诊断日志已尽力保存。\n" + args.Exception.Message, "深空当铺鉴定助手");
                args.Handled = true; window.ExitApplication();
            };
        }
        catch (Exception ex)
        {
            log?.Write($"START FAILED {ex}");
            MessageBox.Show($"程序无法启动：{ex.Message}", "深空当铺鉴定助手"); Shutdown(1);
        }
    }
    protected override void OnExit(ExitEventArgs e) { instance?.Dispose(); log?.Write("EXIT clean"); base.OnExit(e); }
}
