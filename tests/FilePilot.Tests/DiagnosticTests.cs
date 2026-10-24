using System.IO;
using System.Text.Json;
using FilePilot.App.Diagnostics;
using FilePilot.Core;
using Xunit;
using Xunit.Abstractions;

namespace FilePilot.Tests;

public sealed class DiagnosticTests : IDisposable
{
    private readonly ITestOutputHelper output;
    private readonly string root = Path.Combine(Path.GetTempPath(), "FilePilot-diagnostic-tests-" + Guid.NewGuid().ToString("N"));
    public DiagnosticTests(ITestOutputHelper output) { this.output = output; Directory.CreateDirectory(root); }
    [Theory]
    [InlineData("popup", false)]
    [InlineData("widget", true)]
    public void DiagnosticArgumentsRequireAnExplicitNewReportAndMode(string mode, bool widget)
    {
        var path = Path.Combine(root, "report 文.json");
        var options = DiagnosticOptions.Parse(["--diagnostics", path, mode]);
        Assert.Equal(path, options.ReportPath);
        Assert.Equal(widget, options.Widget);
        Assert.False(options.Extended);
        Assert.False(File.Exists(path));
    }
    [Theory]
    [InlineData("popup")]
    [InlineData("widget")]
    public void ExtendedWorkloadRequiresExplicitFlag(string mode)
    {
        var path = Path.Combine(root, "extended.json");
        Assert.True(DiagnosticOptions.Parse(["--diagnostics", path, mode, "--extended"]).Extended);
        Assert.Throws<ArgumentException>(() => DiagnosticOptions.Parse(["--diagnostics", path, mode, "--unknown"]));
        Assert.Throws<ArgumentException>(() => DiagnosticOptions.Parse(["--diagnostics", path, mode, "--extended", "extra"]));
        Assert.False(File.Exists(path));
    }
    [Fact]
    public void RejectsMalformedUnsafeAndOccupiedReports()
    {
        Assert.Throws<ArgumentException>(() => DiagnosticOptions.Parse(["--diagnostics"]));
        Assert.Throws<ArgumentException>(() => DiagnosticOptions.Parse(["--diagnostics", Path.Combine(root, "report.json"), "unknown"]));
        Assert.Throws<ArgumentException>(() => DiagnosticOptions.Parse(["--diagnostics", "relative.json", "popup"]));
        Assert.Throws<ArgumentException>(() => DiagnosticOptions.Parse(["--diagnostics", Path.Combine(root, "report.txt"), "popup"]));
        var existing = Path.Combine(root, "report.json");
        File.WriteAllText(existing, "keep me");
        Assert.Throws<FileCollisionException>(() => DiagnosticOptions.WriteReport(existing, new { Succeeded = true }));
        Assert.Equal("keep me", File.ReadAllText(existing));
    }
    [Fact]
    public void ReportFinalizationLeavesNoTemporaryFileAndIsReadable()
    {
        var path = Path.Combine(root, "report.json");
        DiagnosticOptions.WriteReport(path, new { Succeeded = true, Value = 42 });
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        Assert.True(json.RootElement.GetProperty("Succeeded").GetBoolean());
        Assert.Equal(42, json.RootElement.GetProperty("Value").GetInt32());
        Assert.Empty(Directory.GetFiles(root, "*.tmp"));
    }
    [Fact]
    public void FailedReportSerializationCleansUpOwnedTemporaryOutput()
    {
        var path = Path.Combine(root, "report.json");
        Assert.Throws<InvalidOperationException>(() => DiagnosticOptions.WriteReport(path, new InvalidReport()));
        Assert.False(File.Exists(path));
        Assert.Empty(Directory.GetFiles(root, "*.tmp"));
    }
    [Fact]
    public void MemorySnapshotReportsCurrentProcessAndRequiredWindowsApisArePresent()
    {
        var snapshot = MemorySnapshot.Capture();
        output.WriteLine(snapshot.ToString());
        output.WriteLine($"GC server={System.Runtime.GCSettings.IsServerGC}, collections={GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}, heapAtLastGC={GC.GetGCMemoryInfo().HeapSizeBytes}, currentEstimate={GC.GetTotalMemory(false)}");
        Assert.True(snapshot.PrivateBytes > 0, "Private memory: " + snapshot);
        Assert.True(snapshot.WorkingSetBytes > 0, "Working set: " + snapshot);
        Assert.True(snapshot.ManagedBytes > 0, "Managed estimate: " + snapshot);
        Assert.True(snapshot.PeakWorkingSetBytes >= snapshot.WorkingSetBytes, "Peak working set: " + snapshot);
        Assert.True(DiagnosticRunner.CheckNativeApis());
    }
    [Fact]
    public async Task MemorySnapshotsRemainConsistentDuringAllocation()
    {
        using var stop = new CancellationTokenSource();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allocating = Task.Run(() =>
        {
            // Retain an 8 MiB ring, touching pages; cap total allocation at 256 MiB.
            var blocks = new byte[8][];
            for (int allocation = 0; allocation < 256 && !stop.IsCancellationRequested; allocation++)
            {
                var block = new byte[1024 * 1024];
                for (int page = 0; page < block.Length; page += 4096) block[page] = 1;
                blocks[allocation % blocks.Length] = block;
                if (allocation == blocks.Length - 1) ready.TrySetResult();
            }
            GC.KeepAlive(blocks);
        });
        try
        {
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
            for (int sample = 0; sample < 200; sample++)
            {
                var snapshot = MemorySnapshot.Capture();
                Assert.True(snapshot.PrivateBytes > 0, $"Sample {sample}: {snapshot}");
                Assert.True(snapshot.WorkingSetBytes > 0, $"Sample {sample}: {snapshot}");
                Assert.True(snapshot.ManagedBytes > 0, $"Sample {sample}: {snapshot}");
                Assert.True(snapshot.PeakWorkingSetBytes >= snapshot.WorkingSetBytes, $"Sample {sample}: {snapshot}");
            }
        }
        finally { stop.Cancel(); await allocating; }
    }
    [Fact]
    public void OneShotFallbackCannotKeepResidentMarkerAlive()
    {
        var name = @"Local\FilePilot.Tests." + Guid.NewGuid().ToString("N");
        var first = FilePilot.App.Widget.ResidentInstance.TryStart(name);
        Assert.NotNull(first);
        try { Assert.Null(FilePilot.App.Widget.ResidentInstance.TryStart(name)); }
        finally { first.Dispose(); }
        using var restarted = FilePilot.App.Widget.ResidentInstance.TryStart(name);
        Assert.NotNull(restarted);
    }
    private sealed class InvalidReport { public string Value => throw new InvalidOperationException("Synthetic serialization failure"); }
    public void Dispose() => Directory.Delete(root, true);
}
