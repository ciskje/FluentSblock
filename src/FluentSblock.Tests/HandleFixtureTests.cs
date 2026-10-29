using System.Diagnostics;
using FluentSblock.Services;
using Xunit;
using Xunit.Abstractions;

namespace FluentSblock.Tests;

/// <summary>
/// Golden test: a file locked by this process must be reported both by
/// HandleEnumerator and by Sysinternals handle64.exe (-v CSV).
/// handle64 path: HANDLE64_PATH env, else C:\Temp\opencode\handle64.exe.
/// </summary>
public sealed class HandleFixtureTests
{
    private readonly ITestOutputHelper _out;

    public HandleFixtureTests(ITestOutputHelper output) => _out = output;

    [Fact(Timeout = 300000)]
    public async Task LockedFileAppearsInBothEnumerators()
    {
        string fixture = Path.Combine(Path.GetTempPath(), $"fluentSblock-golden-{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(fixture, "golden");
        using var locked = new FileStream(fixture, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(4));
            var ours = await HandleEnumerator.GetHoldersForPathAsync(fixture, null, cts.Token);
            _out.WriteLine($"enumerator rows for fixture: {ours.Count}");
            Assert.Contains(ours, h => h.FilePath.Equals(fixture, StringComparison.OrdinalIgnoreCase));

            string handle64 = Environment.GetEnvironmentVariable("HANDLE64_PATH")
                ?? @"C:\Temp\opencode\handle64.exe";
            if (!File.Exists(handle64))
            {
                _out.WriteLine("handle64 missing, cross-check skipped");
                return;
            }
            int pid = Environment.ProcessId;
            var psi = new ProcessStartInfo(handle64, $"-p {pid} -v -nobanner")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi)!;
            string csv = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync(cts.Token);
            _out.WriteLine($"handle64 exit={proc.ExitCode}");
            bool found = csv.Split('\n').Any(line =>
                line.Split(',').LastOrDefault()?.Trim().Trim('"')
                    .Equals(fixture, StringComparison.OrdinalIgnoreCase) == true);
            Assert.True(found, "handle64.exe did not list the locked fixture file");
        }
        finally
        {
            locked.Dispose();
            File.Delete(fixture);
        }
    }
}
