namespace FilePilot.Core.Documents;

internal static class DocumentWorkCleanup
{
    internal static async Task DeleteAsync(string root, string job)
    {
        const string prefix = ".filepilot-doc-";
        root = LocalPathPolicy.Validate(root);
        job = Path.GetFullPath(job);
        var name = Path.GetFileName(job);
        if (!string.Equals(Path.GetDirectoryName(job), root, StringComparison.OrdinalIgnoreCase) ||
            !name.StartsWith(prefix, StringComparison.Ordinal) || !Guid.TryParseExact(name[prefix.Length..], "N", out _))
            throw new ArgumentException("Cleanup is restricted to the owned document workspace.");

        // Windows may briefly retain a sharing handle after an engine exits. This is
        // separate from its execution deadline, and cancellation must not skip cleanup.
        for (int attempt = 0; ; attempt++)
        {
            LocalPathPolicy.Validate(job);
            try { Directory.Delete(job, recursive: true); return; }
            catch (IOException ex) when (((ex.HResult & 0xFFFF) is 32 or 33) && attempt < 20)
            { await Task.Delay(50, CancellationToken.None).ConfigureAwait(false); }
        }
    }
}
