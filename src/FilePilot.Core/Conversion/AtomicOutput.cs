namespace FilePilot.Core.Conversion;

internal static class AtomicOutput
{
    internal static async Task WriteAsync(string destination, Func<Stream, CancellationToken, Task> write,
        CancellationToken cancellationToken)
    {
        var folder = LocalPathPolicy.Validate(Path.GetDirectoryName(destination)!);
        if (File.Exists(destination) || Directory.Exists(destination)) throw new FileCollisionException(destination);
        var temporary = Path.Combine(folder, $".filepilot-{Guid.NewGuid():N}.tmp");
        var ownsTemporary = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                ownsTemporary = true;
                await write(stream, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            LocalPathPolicy.Validate(folder);
            try { File.Move(temporary, destination, overwrite: false); }
            catch (IOException ex) when (File.Exists(destination) || Directory.Exists(destination))
            { throw new FileCollisionException(destination, ex); }
            ownsTemporary = false;
        }
        catch (Exception original)
        {
            if (ownsTemporary)
            {
                try { File.Delete(temporary); }
                catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
                {
                    throw new IOException($"The operation failed and temporary output could not be removed: {temporary}",
                        new AggregateException(original, cleanup));
                }
            }
            throw;
        }
    }
}
