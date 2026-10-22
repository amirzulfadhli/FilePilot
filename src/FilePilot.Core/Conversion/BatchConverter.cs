namespace FilePilot.Core.Conversion;

public sealed record BatchItemResult(string SourcePath, string? OutputPath, string Message);
public sealed record BatchResult(IReadOnlyList<BatchItemResult> Items, bool Cancelled);

/// <summary>Sequential conversion bounds native codec memory to one image at a time.</summary>
public sealed class BatchConverter(IFileConverter converter)
{
    public const int MaxFiles = 100;
    public async Task<BatchResult> ConvertAsync(IReadOnlyList<string> sources, string folder, ImageFileFormat format,
        string background, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        if (sources.Count is < 1 or > MaxFiles) throw new ArgumentException($"Choose between 1 and {MaxFiles} files.");
        var results = new List<BatchItemResult>();
        foreach (var source in sources.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (cancellationToken.IsCancellationRequested) return new(results, true);
            progress?.Report($"Converting {results.Count + 1}/{sources.Count}: {Path.GetFileName(source)}");
            try
            {
                if (!converter.GetOutputFormats(source).Contains(format))
                {
                    results.Add(new(source, null, "Skipped: unsupported input or already in this format."));
                    continue;
                }
                var result = await converter.ConvertAsync(new(source, folder, format, background), cancellationToken);
                results.Add(new(source, result.OutputPath, "Saved; original unchanged."));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return new(results, true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
            { results.Add(new(source, null, ex.Message)); }
        }
        return new(results, cancellationToken.IsCancellationRequested);
    }
}
