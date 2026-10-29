using System.Diagnostics;
using System.IO;
using Xunit;

namespace PawnAppraiser.Desktop.Tests;

public class SingleInstanceTests
{
    [Fact]
    public async Task SecondProcessSignalsOwnerAndReleasesAfterExit()
    {
        Process Start() => Process.Start(new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "probe", "SingleInstanceProbe.exe"))
        { RedirectStandardInput = true, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true })!;
        using var first = Start();
        try
        {
            Assert.Equal("OWNER", await first.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(8)));
            using var second = Start();
            try
            {
                Assert.Equal("SECONDARY", await second.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(8)));
                Assert.Equal("SHOW", await first.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(8)));
                await second.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8));
            }
            finally { if (!second.HasExited) second.Kill(); }
            await first.StandardInput.WriteLineAsync(); await first.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8));
            using var third = Start();
            try
            {
                Assert.Equal("OWNER", await third.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(8)));
                await third.StandardInput.WriteLineAsync(); await third.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8));
            }
            finally { if (!third.HasExited) third.Kill(); }
        }
        finally { if (!first.HasExited) first.Kill(); }
    }
}
