using System.IO;
using System.Text.Json;
using FilePilot.Core;

namespace FilePilot.App.Diagnostics;

internal sealed record DiagnosticOptions(string ReportPath, bool Widget, bool Extended = false)
{
    internal static DiagnosticOptions Parse(string[] args)
    {
        if (args.Length is not (3 or 4) || args[0] != "--diagnostics" || args[2] is not ("popup" or "widget") ||
            (args.Length == 4 && args[3] != "--extended"))
            throw new ArgumentException("Usage: FilePilot --diagnostics <new absolute report.json> popup|widget [--extended]");
        return new(ValidateReportPath(args[1]), args[2] == "widget", args.Length == 4);
    }

    internal static string ValidateReportPath(string path)
    {
        if (!Path.IsPathFullyQualified(path) || !string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose a new absolute .json report path in an existing local folder.");
        var parent = LocalPathPolicy.Validate(Path.GetDirectoryName(path)!);
        var name = Path.GetFileName(path);
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.EndsWith(' '))
            throw new ArgumentException("Invalid report filename.");
        var fullPath = Path.Combine(parent, name);
        if (File.Exists(fullPath) || Directory.Exists(fullPath)) throw new FileCollisionException(fullPath);
        return fullPath;
    }

    internal static void WriteReport(string path, object report)
    {
        path = ValidateReportPath(path);
        var temp = Path.Combine(Path.GetDirectoryName(path)!, $".filepilot-diagnostic-{Guid.NewGuid():N}.tmp");
        var owned = false;
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                owned = true;
                JsonSerializer.Serialize(stream, report, new JsonSerializerOptions { WriteIndented = true });
                stream.Flush(flushToDisk: true);
            }
            LocalPathPolicy.Validate(Path.GetDirectoryName(path)!);
            File.Move(temp, path, overwrite: false);
            owned = false;
        }
        finally { if (owned) File.Delete(temp); }
    }
}
