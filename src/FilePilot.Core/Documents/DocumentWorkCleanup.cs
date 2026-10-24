namespace FilePilot.Core.Documents;

internal static class DocumentWorkCleanup
{
    internal static Task DeleteAsync(string root, string job) =>
        DeleteAsync(root, job, TimeProvider.System, static interval => Task.Delay(interval, CancellationToken.None));

    internal static async Task DeleteAsync(string root, string job, TimeProvider clock, Func<TimeSpan, Task> delay)
    {
        const string prefix = ".filepilot-doc-";
        root = LocalPathPolicy.Validate(root);
        job = Path.GetFullPath(job);
        var name = Path.GetFileName(job);
        if (!string.Equals(Path.GetDirectoryName(job), root, StringComparison.OrdinalIgnoreCase) ||
            !name.StartsWith(prefix, StringComparison.Ordinal) || !Guid.TryParseExact(name[prefix.Length..], "N", out _))
            throw new ArgumentException("Cleanup is restricted to the owned document workspace.");

        // Windows may briefly retain a sharing handle after an engine exits. Do not
        // schedule a whole sequence of late retries when continuations are delayed.
        // This budget is separate from engine execution; cancellation must not skip cleanup.
        var started = clock.GetTimestamp();
        for (int attempt = 0; ; attempt++)
        {
            LocalPathPolicy.Validate(job);
            try { Directory.Delete(job, recursive: true); return; }
            catch (IOException ex) when (((ex.HResult & 0xFFFF) is 32 or 33) && attempt < 20 &&
                clock.GetElapsedTime(started) < TimeSpan.FromSeconds(1))
            { await delay(TimeSpan.FromMilliseconds(50)).ConfigureAwait(false); }
        }
    }
}
