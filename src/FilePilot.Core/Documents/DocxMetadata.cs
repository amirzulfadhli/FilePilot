using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace FilePilot.Core.Documents;

internal static class DocxMetadata
{
    // Called only on our preflighted, owned copy. Never rewrite the selected document.
    internal static void RemoveFromCopy(string path, CancellationToken token)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Update);
        foreach (var entry in archive.Entries.ToArray())
        {
            token.ThrowIfCancellationRequested();
            if (entry.FullName.StartsWith("docProps/", StringComparison.OrdinalIgnoreCase))
            {
                entry.Delete();
                continue;
            }
            if (entry.FullName is not ("[Content_Types].xml" or "_rels/.rels")) continue;
            XDocument xml;
            using (var stream = entry.Open())
            using (var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 8 * 1024 * 1024 }))
                xml = XDocument.Load(reader);
            var references = xml.Root!.Elements().Where(element =>
            {
                var target = (string?)element.Attribute("PartName") ?? (string?)element.Attribute("Target");
                return target?.TrimStart('/').StartsWith("docProps/", StringComparison.OrdinalIgnoreCase) == true;
            }).ToArray();
            if (references.Length == 0) continue;
            foreach (var reference in references) reference.Remove();
            using var output = entry.Open();
            output.SetLength(0);
            xml.Save(output);
        }
        token.ThrowIfCancellationRequested();
    }
}
