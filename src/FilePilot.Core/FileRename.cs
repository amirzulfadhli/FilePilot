using System.Text.RegularExpressions;

namespace FilePilot.Core;

public static partial class FileRename
{
    public static string Rename(string sourcePath, string enteredName, bool changeExtension = false)
    {
        var source = new FileActions().Inspect(sourcePath);
        ValidateName(enteredName);
        var name = changeExtension ? enteredName : enteredName + source.Extension;
        ValidateName(name);
        var parent = LocalPathPolicy.Validate(Path.GetDirectoryName(source.FullPath)!);
        var destination = Path.Combine(parent, name);
        if (string.Equals(source.FullPath, destination, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose a different name. Case-only renaming is not supported yet.");
        if (File.Exists(destination) || Directory.Exists(destination)) throw new FileCollisionException(destination);
        try { File.Move(source.FullPath, destination, overwrite: false); }
        catch (IOException ex) when (File.Exists(destination) || Directory.Exists(destination))
        { throw new FileCollisionException(destination, ex); }
        return destination;
    }

    internal static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 255 || name is "." or ".." ||
            name.EndsWith('.') || name.EndsWith(' ') || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            Reserved().IsMatch(name.Split('.')[0].TrimEnd(' ')))
            throw new ArgumentException("Enter a valid Windows filename, without a path, reserved name, or trailing dot/space.");
    }

    [GeneratedRegex(@"^(CON|PRN|AUX|NUL|CONIN\$|CONOUT\$|COM[1-9¹²³]|LPT[1-9¹²³])$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Reserved();
}
