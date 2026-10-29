using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PawnAppraiser.Core;

namespace PawnAppraiser.Desktop;

// Explicit developer command only: --smoke-test <new-report.json>. Does not access the game or clipboard.
internal static class StartupSmoke
{
    internal static async Task RunAsync(MainWindow window, MainViewModel vm, string output)
    {
        var passed = false; var error = ""; var price = "—";
        var itemPrices = new Dictionary<string, string>();
        string? earlyFixedPrice = null;
        bool correctedIncomplete = false;
        try
        {
            if (!Path.IsPathFullyQualified(output) || !string.Equals(Path.GetExtension(output), ".json", StringComparison.OrdinalIgnoreCase) ||
                File.Exists(output) || File.Exists(Path.ChangeExtension(output, ".png")))
                throw new IOException("冒烟报告和图片需使用新的绝对文件路径");
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            await Task.Delay(150);
            if (!window.IsVisible || !window.Topmost || vm.SelectedItem is null || vm.CopyCommand.CanExecute(null))
                throw new InvalidOperationException("启动状态不正确");
            foreach (var group in vm.SelectedItem.Groups.Where(g => g.Required))
                vm.Groups.SelectMany(g => g.Options).First(o => o.GroupId == group.Id).ToggleCommand.Execute(null);
            if (!vm.CopyCommand.CanExecute(null)) throw new InvalidOperationException("选齐必选项后未生成价格");
            price = vm.Price;
            // Exercise the actual published catalog and generic UI for every shipped definition.
            foreach (var item in vm.Items)
            {
                vm.SelectedItem = item;
                foreach (var group in item.Groups.Where(g => g.Required))
                    vm.Groups.SelectMany(g => g.Options).First(o => o.GroupId == group.Id).ToggleCommand.Execute(null);
                if (!vm.CopyCommand.CanExecute(null)) throw new InvalidOperationException($"物品未生成价格：{item.Id}");
                itemPrices[item.Id] = vm.Price;
            }
            // Explicit developer smoke scenario for the reported partial-selection workflow.
            if (vm.Items.SingleOrDefault(i => i.Id == "ribwich") is { } ribwich)
            {
                vm.SelectedItem = ribwich; vm.ResetCommand.Execute(null);
                var options = vm.Groups.SelectMany(g => g.Options).ToArray();
                options.Single(o => o.GroupId == "packaging" && o.Definition.Id == "opened").ToggleCommand.Execute(null);
                earlyFixedPrice = vm.Price;
                if (earlyFixedPrice != "40" || !vm.CopyCommand.CanExecute(null) || !vm.Status.Contains("无需补选"))
                    throw new InvalidOperationException("只选已撕开包装未提前完成");
                options.Single(o => o.GroupId == "packaging" && o.Definition.Id == "intact").ToggleCommand.Execute(null);
                correctedIncomplete = vm.Price == "—" && !vm.CopyCommand.CanExecute(null);
                if (!correctedIncomplete) throw new InvalidOperationException("纠正包装后未恢复未完成状态");
                vm.ResetCommand.Execute(null);
                options.Single(o => o.GroupId == "packaging" && o.Definition.Id == "opened").ToggleCommand.Execute(null);
            }
            var currentPrice = vm.Price;
            window.ToggleExpanded(); window.ToggleVisibility(); window.ToggleExpanded();
            if (window.IsVisible) throw new InvalidOperationException("隐藏状态错误");
            window.ToggleVisibility();
            if (window.IsExpanded) throw new InvalidOperationException("展开状态未保留");
            window.ToggleExpanded(); window.UpdateLayout();
            if (vm.Price != currentPrice) throw new InvalidOperationException("显隐或折叠改变了价格");
            var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = new FileStream(Path.ChangeExtension(output, ".png"), FileMode.CreateNew, FileAccess.Write)) encoder.Save(stream);
            passed = true;
        }
        catch (Exception ex) { error = ex.ToString(); }
        finally
        {
            try
            {
                // CreateNew deliberately refuses overwriting a previous verification result.
                using var file = new FileStream(output, FileMode.CreateNew, FileAccess.Write);
                JsonSerializer.Serialize(file, new { passed, error, price, itemPrices, earlyFixedPrice, correctedIncomplete, runtime = Environment.Version.ToString(), x64 = Environment.Is64BitProcess,
                    baseDirectory = AppContext.BaseDirectory, width = window.ActualWidth, height = window.ActualHeight,
                    topmost = window.Topmost, utc = DateTimeOffset.UtcNow, clipboard = "not touched", game = "not touched" }, ItemJson.Options);
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
            window.ExitApplication();
        }
    }
}
