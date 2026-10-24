using FilePilot.Core.Conversion;

namespace FilePilot.Core.Documents;

public sealed class DocxPdfConverter(IDocumentPdfEngine engine, DocumentConversionLimits? limits = null) : IDocumentConverter
{
    private readonly DocumentConversionLimits limits = limits ?? new();
    internal string? WorkingDirectoryRoot { get; init; }
    public Task<DocumentConversionResult> ConvertAsync(DocumentConversionRequest request, CancellationToken cancellationToken = default) =>
        Task.Run(() => ConvertCoreAsync(request, cancellationToken), cancellationToken);

    private async Task<DocumentConversionResult> ConvertCoreAsync(DocumentConversionRequest request, CancellationToken token)
    {
        var file = new FileActions().Inspect(request.SourcePath);
        if (!file.Extension.Equals(".docx", StringComparison.OrdinalIgnoreCase)) throw new DocumentConversionException("Choose an ordinary .docx document.");
        var folder = LocalPathPolicy.Validate(request.DestinationFolder);
        if (!Directory.Exists(folder)) throw new ArgumentException("Choose an existing destination folder.");
        var destination = Path.Combine(folder, Path.GetFileNameWithoutExtension(file.Name) + ".pdf");
        if (File.Exists(destination) || Directory.Exists(destination)) throw new FileCollisionException(destination);
        await using var source = new FileStream(file.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
        await DocxPreflight.ValidateAsync(source, limits, token);
        token.ThrowIfCancellationRequested();
        // LibreOffice's native profile/extension files can exceed MAX_PATH when nested
        // under a long destination. Keep engine work short; AtomicOutput still finalizes
        // through a temporary file on the destination volume.
        var workRoot = LocalPathPolicy.Validate(WorkingDirectoryRoot ?? Path.GetTempPath());
        if (!Directory.Exists(workRoot)) throw new DocumentConversionException("The local temporary folder is unavailable.");
        if (workRoot.Length > 120) throw new DocumentConversionException("The temporary folder path is too long for the document engine. Configure a shorter local TEMP folder and restart FilePilot.");
        var job = Path.Combine(workRoot, ".filepilot-doc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(job);
        var keepJob = false;
        try
        {
            var input = Path.Combine(job, "input.docx");
            source.Position = 0;
            await using (var copy = new FileStream(input, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
                await source.CopyToAsync(copy, token);
            DocxMetadata.RemoveFromCopy(input, token);
            var output = Directory.CreateDirectory(Path.Combine(job, "output")).FullName;
            var profile = Directory.CreateDirectory(Path.Combine(job, "profile")).FullName;
            await engine.ExportAsync(input, output, profile, token);
            token.ThrowIfCancellationRequested();
            var expectedPdf = Path.Combine(output, "input.pdf");
            if (!File.Exists(expectedPdf)) throw new DocumentConversionException("The document engine returned without creating a PDF. The DOCX may contain unsupported content.");
            var pdf = LocalPathPolicy.Validate(expectedPdf);
            await using var completed = new FileStream(pdf, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
            if (completed.Length < 12 || completed.Length > limits.MaxOutputBytes) throw new DocumentConversionException("The document engine produced an empty or oversized PDF.");
            var header = new byte[5];
            await completed.ReadExactlyAsync(header, token);
            if (!header.SequenceEqual("%PDF-"u8.ToArray())) throw new DocumentConversionException("The document engine did not produce a PDF.");
            completed.Position = Math.Max(0, completed.Length - 2048);
            var tail = new byte[completed.Length - completed.Position];
            await completed.ReadExactlyAsync(tail, token);
            if (!System.Text.Encoding.ASCII.GetString(tail).Contains("%%EOF", StringComparison.Ordinal)) throw new DocumentConversionException("The document engine produced an incomplete PDF.");
            completed.Position = 0;
            await AtomicOutput.WriteAsync(destination, (stream, ct) => completed.CopyToAsync(stream, ct), token);
            return new(destination);
        }
        catch (DocumentEngineException ex) when (ex.MayStillBeRunning)
        {
            keepJob = true;
            throw new DocumentConversionException($"The document engine could not be stopped. Temporary work is retained at {job}. No completed output was finalized.", ex);
        }
        finally
        {
            if (!keepJob)
            {
                // Delete only the freshly created, validated child; never a source or chosen destination folder.
                LocalPathPolicy.Validate(job);
                try { await DocumentWorkCleanup.DeleteAsync(workRoot, job); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { throw new DocumentConversionException($"Temporary document work could not be removed: {job}. Check the output folder before retrying.", ex); }
            }
        }
    }
}
