using System.IO;
using System.Text.Json;
using FilePilot.App.Diagnostics;
using FilePilot.Core;
using Xunit;

namespace FilePilot.Tests;

public sealed class DiagnosticTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "FilePilot-diagnostic-tests-" + Guid.NewGuid().ToString("N"));
    public DiagnosticTests() => Directory.CreateDirectory(root);
    [Theory]
    [InlineData("popup", false)]
    [InlineData("widget", true)]
    public void DiagnosticArgumentsRequireAnExplicitNewReportAndMode(string mode, bool widget)
    {
        var path = Path.Combine(root, "report 文.json");
        var options = DiagnosticOptions.Parse(["--diagnostics", path, mode]);
        Assert.Equal(path, options.ReportPath);
        Assert.Equal(widget, options.Widget);
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
        Assert.True(snapshot.PrivateBytes > 0);
        Assert.True(snapshot.WorkingSetBytes > 0);
        Assert.True(snapshot.ManagedBytes > 0);
        Assert.True(snapshot.PeakWorkingSetBytes >= snapshot.WorkingSetBytes);
        Assert.True(DiagnosticRunner.CheckNativeApis());
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
