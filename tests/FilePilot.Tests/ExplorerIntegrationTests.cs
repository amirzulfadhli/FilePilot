using System.IO;
using FilePilot.App;
using Microsoft.Win32;
using Xunit;

namespace FilePilot.Tests;

public sealed class ExplorerIntegrationTests
{
    [Fact]
    public void CommandQuotesExecutableAndOneFileArgument()
    {
        Assert.Equal("\"C:\\工具\\File Pilot\\FilePilot.exe\" \"%1\"",
            ExplorerIntegration.BuildCommand(@"C:\工具\File Pilot\FilePilot.exe"));
    }

    [Theory]
    [InlineData("relative.exe")]
    [InlineData("C:\\unsafe\"name.exe")]
    [InlineData("C:\\%1\\FilePilot.exe")]
    [InlineData("C:\\FilePilot.cmd")]
    public void RejectsUnsafeExecutableCommands(string path) =>
        Assert.Throws<ArgumentException>(() => ExplorerIntegration.BuildCommand(path));

    [Fact]
    public void RegistrationIsIdempotentSingleSelectionAndUnregistrationPreservesOtherVerbs()
    {
        WithIsolatedRoot((root, executable) =>
        {
            using (var other = root.CreateSubKey(@"SystemFileAssociations\.png\shell\OtherApp")) other.SetValue("", "Keep");
            ExplorerIntegration.Register(root, executable);
            ExplorerIntegration.Register(root, executable);
            foreach (var extension in ExplorerIntegration.Extensions)
            {
                using var verb = root.OpenSubKey($@"SystemFileAssociations\{extension}\shell\FilePilot")!;
                Assert.Equal("Single", verb.GetValue("MultiSelectModel"));
                using var command = verb.OpenSubKey("command")!;
                Assert.Equal(ExplorerIntegration.BuildCommand(executable), command.GetValue(""));
                Assert.Equal(RegistryValueKind.String, command.GetValueKind(""));
            }
            ExplorerIntegration.Unregister(root);
            ExplorerIntegration.Unregister(root);
            using var preserved = root.OpenSubKey(@"SystemFileAssociations\.png\shell\OtherApp");
            Assert.NotNull(preserved);
            foreach (var extension in ExplorerIntegration.Extensions)
                Assert.Null(root.OpenSubKey($@"SystemFileAssociations\{extension}\shell\FilePilot"));
        });
    }

    [Fact]
    public void OlderVersionCannotUnregisterNewerVersionsMenu()
    {
        WithIsolatedRoot((root, executable) =>
        {
            var newer = Path.Combine(Path.GetDirectoryName(executable)!, "New FilePilot.exe");
            File.WriteAllText(newer, "Never executed");
            ExplorerIntegration.Register(root, executable);
            ExplorerIntegration.Register(root, newer);
            ExplorerIntegration.Unregister(root, executable);
            foreach (var extension in ExplorerIntegration.Extensions)
            {
                using var command = root.OpenSubKey($@"SystemFileAssociations\{extension}\shell\FilePilot\command");
                Assert.Equal(ExplorerIntegration.BuildCommand(newer), command?.GetValue(""));
            }
            ExplorerIntegration.Unregister(root, newer);
            Assert.Null(root.OpenSubKey(@"SystemFileAssociations\.docx\shell\FilePilot"));
        });
    }

    [Fact]
    public void RefusesToChangeAnUnownedEntry()
    {
        WithIsolatedRoot((root, executable) =>
        {
            using (var other = root.CreateSubKey(@"SystemFileAssociations\.webp\shell\FilePilot")) other.SetValue("", "Other program");
            Assert.Throws<IOException>(() => ExplorerIntegration.Register(root, executable));
            Assert.Throws<IOException>(() => ExplorerIntegration.Unregister(root));
            Assert.Null(root.OpenSubKey(@"SystemFileAssociations\.png\shell\FilePilot"));
        });
    }

    private static void WithIsolatedRoot(Action<RegistryKey, string> test)
    {
        var id = Guid.NewGuid().ToString("N");
        var registryPath = @"Software\FilePilot.Tests\" + id;
        var folder = Path.Combine(Path.GetTempPath(), "FilePilot-registry-" + id);
        Directory.CreateDirectory(folder);
        var executable = Path.Combine(folder, "File Pilot 文.exe");
        File.WriteAllText(executable, "Test fixture; never executed.");
        try
        {
            using var root = Registry.CurrentUser.CreateSubKey(registryPath, true);
            test(root, executable);
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(registryPath, false);
            Directory.Delete(folder, true);
        }
    }
}
