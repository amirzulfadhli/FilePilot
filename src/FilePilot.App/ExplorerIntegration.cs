using System.IO;
using System.Runtime.InteropServices;
using FilePilot.Core;
using Microsoft.Win32;

namespace FilePilot.App;

public static class ExplorerIntegration
{
    private const string Owner = "FilePilot.Phase2.4cf15962";
    internal static readonly string[] Extensions = [".png", ".jpg", ".jpeg", ".webp", ".docx"];
    private static string VerbPath(string extension) => $@"SystemFileAssociations\{extension}\shell\FilePilot";

    public static void Register(string executablePath)
    {
        using var classes = Registry.CurrentUser.CreateSubKey(@"Software\Classes", true);
        Register(classes, executablePath);
        NotifyShell();
    }

    public static void Unregister()
    {
        using var classes = Registry.CurrentUser.OpenSubKey(@"Software\Classes", true);
        if (classes is not null) Unregister(classes, Environment.ProcessPath ?? throw new InvalidOperationException("Cannot locate FilePilot.exe."));
        NotifyShell();
    }

    internal static string BuildCommand(string executablePath)
    {
        if (!Path.IsPathFullyQualified(executablePath) || executablePath.Contains('"') ||
            executablePath.Contains('%') || executablePath.Any(char.IsControl) ||
            !string.Equals(Path.GetExtension(executablePath), ".exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Use a fully qualified executable path without quotes, percent signs, or control characters.");
        return $"\"{executablePath}\" \"%1\"";
    }

    // An isolated registry root lets tests exercise real registry behavior without installing a menu.
    internal static void Register(RegistryKey classes, string executablePath)
    {
        var executable = new FileActions().Inspect(executablePath).FullPath;
        var command = BuildCommand(executable);
        CheckOwnership(classes);
        foreach (var extension in Extensions)
        {
            using var verb = classes.CreateSubKey(VerbPath(extension), true);
            verb.SetValue("FilePilotOwner", Owner);
            verb.SetValue("", "FilePilot");
            verb.SetValue("Icon", $"\"{executable}\",0");
            verb.SetValue("MultiSelectModel", "Single");
            using var commandKey = verb.CreateSubKey("command", true);
            commandKey.SetValue("", command, RegistryValueKind.String);
        }
    }

    internal static void Unregister(RegistryKey classes, string? expectedExecutable = null)
    {
        CheckOwnership(classes);
        foreach (var extension in Extensions)
        {
            if (expectedExecutable is not null)
            {
                using var command = classes.OpenSubKey(VerbPath(extension) + @"\command");
                if (!string.Equals(command?.GetValue("") as string, BuildCommand(expectedExecutable), StringComparison.OrdinalIgnoreCase)) continue;
            }
            classes.DeleteSubKeyTree(VerbPath(extension), throwOnMissingSubKey: false);
        }
    }

    private static void CheckOwnership(RegistryKey classes)
    {
        foreach (var extension in Extensions)
        {
            using var verb = classes.OpenSubKey(VerbPath(extension));
            if (verb is not null && !Equals(verb.GetValue("FilePilotOwner"), Owner))
                throw new IOException("An unrelated FilePilot menu entry already exists. No entries were changed. Resolve it before registering or unregistering.");
        }
    }

    private static void NotifyShell() => SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);
}
