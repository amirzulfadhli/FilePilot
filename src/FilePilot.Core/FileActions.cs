namespace FilePilot.Core;

public sealed record MoveResult(string DestinationPath, bool SourceMayRemain);

public sealed class FileCollisionException(string destination, Exception? inner = null)
    : IOException($"A file or folder named '{Path.GetFileName(destination)}' already exists there. Nothing was overwritten. Choose another folder.", inner);

public sealed class FileActions
{
    public SelectedFile Inspect(string path)
    {
        var fullPath = LocalPathPolicy.Validate(path);
        var info = new FileInfo(fullPath);
        if ((info.Attributes & FileAttributes.Directory) != 0)
            throw new ArgumentException("Select a file, not a folder.");
        return new SelectedFile(info.FullName, info.Name, info.Extension, info.Length, info.LastWriteTime);
    }

    public MoveResult Move(string sourcePath, string destinationFolder)
    {
        var source = Inspect(sourcePath);
        var folder = LocalPathPolicy.Validate(destinationFolder);
        if ((File.GetAttributes(folder) & FileAttributes.Directory) == 0)
            throw new ArgumentException("The destination must be an existing folder.");

        // Only a validated leaf name can enter the selected destination folder.
        var destination = Path.Combine(folder, source.Name);
        if (string.Equals(source.FullPath, destination, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The file is already in that folder. Choose another folder.");
        if (File.Exists(destination) || Directory.Exists(destination))
            throw new FileCollisionException(destination);

        try
        {
            // Windows handles same-volume rename and cross-volume copy/delete.
            // No replacement is allowed, including collisions after the preflight check.
            File.Move(source.FullPath, destination, overwrite: false);
        }
        catch (IOException ex) when (File.Exists(destination) || Directory.Exists(destination))
        {
            // The destination may be a racing file or a partial cross-volume copy.
            // Do not delete it: ownership of that path cannot safely be assumed.
            throw new IOException($"Move did not complete. The destination now exists at '{destination}'. Inspect both locations before retrying; no existing file was overwritten.", ex);
        }

        return new MoveResult(destination, SourceMayStillExist(source.FullPath));
    }

    private static bool SourceMayStillExist(string path)
    {
        try { File.GetAttributes(path); return true; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
        catch (IOException) { return true; }
        catch (UnauthorizedAccessException) { return true; }
        catch (System.Security.SecurityException) { return true; }
    }
}
