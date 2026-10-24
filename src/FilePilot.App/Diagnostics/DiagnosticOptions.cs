using System.IO;
using System.Globalization;
using System.Text.Json;
using FilePilot.Core;

namespace FilePilot.App.Diagnostics;

internal sealed record DiagnosticOptions(string ReportPath, bool Widget, bool Extended = false, int? IdleSeconds = null)
{
    internal int RequestedIdleSeconds => IdleSeconds ?? (Extended ? 30 : 3);
    internal static DiagnosticOptions Parse(string[] args)
    {
        if (args.Length is not (3 or 4 or 6) || args[0] != "--diagnostics" || args[2] is not ("popup" or "widget") ||
            (args.Length >= 4 && args[3] != "--extended"))
            throw new ArgumentException("Usage: FilePilot --diagnostics <new absolute report.json> popup|widget [--extended [--idle-seconds 30..3600]]");
        int? idleSeconds = null;
        if (args.Length == 6)
        {
            if (args[4] != "--idle-seconds" || !int.TryParse(args[5], NumberStyles.None, CultureInfo.InvariantCulture, out int seconds) || seconds is < 30 or > 3600)
                throw new ArgumentException("Extended diagnostic idle duration must be an integer from 30 to 3600 seconds.");
            idleSeconds = seconds;
        }
        return new(ValidateReportPath(args[1]), args[2] == "widget", args.Length >= 4, idleSeconds);
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
