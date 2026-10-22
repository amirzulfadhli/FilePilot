using System.IO.Compression;
using System.Text;
using System.Xml;
using SkiaSharp;

namespace FilePilot.Core.Documents;

internal static class DocxPreflight
{
    internal static async Task ValidateAsync(Stream source, DocumentConversionLimits limits, CancellationToken token)
    {
        if (source.Length > limits.MaxInputBytes) throw new DocumentConversionException("DOCX exceeds the 32 MiB input limit.");
        try
        {
            using var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
            if (archive.Entries.Count > limits.MaxEntries) throw new DocumentConversionException("DOCX contains too many package entries.");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var hasDocument = false;
            var hasContentType = false;
            long total = 0;
            long imagePixels = 0;
            foreach (var entry in archive.Entries)
            {
                token.ThrowIfCancellationRequested();
                var name = entry.FullName;
                if (name.Contains('\\') || name.Contains(':') || name.Contains('%') || name.StartsWith('/') ||
                    name.TrimEnd('/').Split('/').Any(part => part is "" or "." or ".." || part.Length > 255 || part.EndsWith('.') || part.EndsWith(' ') || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) || !names.Add(name) ||
                    ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                    throw new DocumentConversionException("DOCX contains unsafe or duplicate package paths.");
                if (name.EndsWith('/') && entry.Length == 0) continue;
                var extension = Path.GetExtension(name).ToLowerInvariant();
                if (extension is not (".xml" or ".rels" or ".png" or ".jpg" or ".jpeg"))
                    throw new DocumentConversionException("This first version supports DOCX XML and embedded PNG/JPEG images only; macros, embedded objects and other media are refused.");
                var xml = extension is ".xml" or ".rels";
                long maximum = xml ? 8 * 1024 * 1024 : limits.MaxInputBytes;
                if (entry.Length > maximum || entry.Length > limits.MaxExpandedBytes - total)
                    throw new DocumentConversionException("DOCX expanded content exceeds resource limits.");
                await using var content = entry.Open();
                using var xmlBytes = new MemoryStream();
                var buffer = new byte[65536];
                long count = 0;
                int read;
                while ((read = await content.ReadAsync(buffer, token)) != 0)
                {
                    count += read;
                    total += read;
                    if (count > maximum || total > limits.MaxExpandedBytes) throw new DocumentConversionException("DOCX expanded content exceeds resource limits.");
                    await xmlBytes.WriteAsync(buffer.AsMemory(0, read), token);
                }
                if (count != entry.Length) throw new DocumentConversionException("DOCX contains inconsistent package lengths.");
                xmlBytes.Position = 0;
                if (!xml)
                {
                    using var managed = new SKManagedStream(xmlBytes, disposeManagedStream: false);
                    using var codec = SKCodec.Create(managed);
                    var expectedFormat = extension == ".png" ? SKEncodedImageFormat.Png : SKEncodedImageFormat.Jpeg;
                    if (codec is null || codec.EncodedFormat != expectedFormat) throw new DocumentConversionException("DOCX image content does not match its PNG/JPEG extension.");
                    var info = codec.Info;
                    long pixels = (long)info.Width * info.Height;
                    imagePixels += pixels;
                    if (info.Width <= 0 || info.Height <= 0 || info.Width > 16384 || info.Height > 16384 || pixels > 16_000_000 || imagePixels > 64_000_000 || codec.FrameCount > 1)
                        throw new DocumentConversionException("DOCX embedded images exceed dimension/pixel limits or contain animation.");
                    continue;
                }
                using var reader = XmlReader.Create(xmlBytes, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 8 * 1024 * 1024 });
                var instructions = new StringBuilder();
                int instructionDepth = -1;
                while (reader.Read())
                {
                    token.ThrowIfCancellationRequested();
                    if (instructionDepth >= 0 && reader.NodeType is XmlNodeType.Text or XmlNodeType.CDATA) instructions.Append(reader.Value);
                    if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == instructionDepth) instructionDepth = -1;
                    if (reader.NodeType != XmlNodeType.Element) continue;
                    if (reader.HasAttributes)
                    {
                        while (reader.MoveToNextAttribute())
                            if (reader.LocalName is "src" or "href" or "link" && !string.IsNullOrWhiteSpace(reader.Value))
                                throw new DocumentConversionException("DOCX inline linked resources are not supported in offline conversion.");
                        reader.MoveToElement();
                    }
                    if (reader.LocalName == "Relationship") ValidateRelationshipTarget(name, reader.GetAttribute("Target"));
                    if (reader.LocalName == "Relationship" && string.Equals(reader.GetAttribute("TargetMode"), "External", StringComparison.OrdinalIgnoreCase))
                        throw new DocumentConversionException("DOCX external links, templates and linked resources are not supported in offline conversion.");
                    if (reader.LocalName is "altChunk" or "OLEObject" or "object" or "control" or "svg")
                        throw new DocumentConversionException("DOCX active/embedded content is not supported.");
                    if (reader.LocalName == "instrText" && !reader.IsEmptyElement) instructionDepth = reader.Depth;
                    if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "fldSimple") instructions.Append(reader.GetAttribute("instr", reader.NamespaceURI));
                    if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "Override" && name.Equals("[Content_Types].xml", StringComparison.OrdinalIgnoreCase))
                    {
                        var type = reader.GetAttribute("ContentType") ?? "";
                        if (type.Contains("macro", StringComparison.OrdinalIgnoreCase) || type.Contains("vba", StringComparison.OrdinalIgnoreCase))
                            throw new DocumentConversionException("Macro-enabled documents are not supported.");
                        if (reader.GetAttribute("PartName") == "/word/document.xml" && type == "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml") hasContentType = true;
                    }
                    if (name.Equals("word/document.xml", StringComparison.OrdinalIgnoreCase) && reader.Depth == 0 && reader.LocalName == "document" &&
                        reader.NamespaceURI is "http://schemas.openxmlformats.org/wordprocessingml/2006/main" or "http://purl.oclc.org/ooxml/wordprocessingml/main") hasDocument = true;
                }
                var fieldText = string.Concat(instructions.ToString().Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
                if (new[] { "DDE", "INCLUDEPICTURE", "INCLUDETEXT", "DATABASE", "LINK" }.Any(fieldText.Contains))
                    throw new DocumentConversionException("DOCX contains a field that can load external or active content; conversion was refused.");
            }
            if (!hasDocument || !hasContentType) throw new DocumentConversionException("The file is not a supported, ordinary DOCX document.");
        }
        catch (Exception ex) when (ex is InvalidDataException or XmlException)
        { throw new DocumentConversionException("DOCX is corrupted, encrypted, or contains unsupported XML.", ex); }
    }
    private static void ValidateRelationshipTarget(string entry, string? target)
    {
        if (string.IsNullOrWhiteSpace(target)) return;
        var decoded = Uri.UnescapeDataString(target).Split('#')[0].Trim();
        if (decoded.Contains(':') || decoded.Contains('\\') || decoded.Contains('%') || decoded.StartsWith("//", StringComparison.Ordinal)) throw new DocumentConversionException("DOCX contains an external or unsafe relationship target.");
        // OPC root-relative part references are allowed; traversal beyond the package root is not.
        var relationFolder = Path.GetDirectoryName(entry.Replace('/', Path.DirectorySeparatorChar)) ?? "";
        var baseFolder = Path.GetDirectoryName(relationFolder) ?? "";
        int depth = decoded.StartsWith('/') ? 0 : baseFolder.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries).Length;
        foreach (var part in decoded.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".") continue;
            if (part == "..") { if (depth == 0) throw new DocumentConversionException("DOCX relationship escapes the package root."); depth--; }
            else depth++;
        }
    }
}