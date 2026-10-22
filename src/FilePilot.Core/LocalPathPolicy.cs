namespace FilePilot.Core;

/// <summary>Conservative policy for ordinary files on local Windows drives.</summary>
public static class LocalPathPolicy
{
    public static string Validate(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Choose a file or folder first.");

        if (!Path.IsPathFullyQualified(path) || path.Length < 3 ||
            !char.IsAsciiLetter(path[0]) || path[1] != ':' ||
            (path[2] != '\\' && path[2] != '/'))
            throw new ArgumentException("Use an absolute local path, such as C:\\Files\\photo.png. Network and device paths are not supported.");

        var segments = path[3..].Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            if (segment is "." or ".." || segment.EndsWith('.') || segment.EndsWith(' ') ||
                segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || IsReservedName(segment))
                throw new ArgumentException("The path contains an unsupported Windows name or traversal segment.");
        }

        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath)!;
        var drive = new DriveInfo(root);
        if (drive.DriveType is not (DriveType.Fixed or DriveType.Removable or DriveType.Ram))
            throw new ArgumentException("Choose a writable local drive. Network drives are not supported.");

        // Check every existing component, including the selected file and drive root.
        // This blocks ordinary junction/symlink traversal, not malicious concurrent swaps.
        var current = root;
        CheckAttributes(current);
        foreach (var segment in segments)
        {
            current = Path.Combine(current, segment);
            CheckAttributes(current);
        }

        return Path.TrimEndingDirectorySeparator(fullPath);
    }

    private static void CheckAttributes(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
            throw new ArgumentException("Symbolic links, junctions, and cloud placeholders are not supported. Choose a regular local file or folder.");
        if ((attributes & FileAttributes.Offline) != 0)
            throw new ArgumentException("This file is offline. Choose a file already stored on this computer.");
    }

    private static bool IsReservedName(string segment)
    {
        var stem = segment.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
        return stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$" ||
               (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) &&
                "123456789¹²³".Contains(stem[3]));
    }
}
