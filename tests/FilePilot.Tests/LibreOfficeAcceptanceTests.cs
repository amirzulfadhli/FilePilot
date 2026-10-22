using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using FilePilot.Core;
using FilePilot.Core.Documents;
using SkiaSharp;
using Xunit;

namespace FilePilot.Tests;

// Opt in explicitly; a missing engine must never be represented as a successful render.
public sealed class LibreOfficeFactAttribute : FactAttribute
{
    public LibreOfficeFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FILEPILOT_LIBREOFFICE")))
            Skip = "Set FILEPILOT_LIBREOFFICE to a real, trusted program/soffice.com to run renderer acceptance.";
    }
}

public sealed class LibreOfficeAcceptanceTests
{
    private static string Engine => LibreOfficePdfEngine.ValidateExecutable(Environment.GetEnvironmentVariable("FILEPILOT_LIBREOFFICE")!);
    private static string NewRoot() => Directory.CreateDirectory(Path.Combine(
        Environment.GetEnvironmentVariable("FILEPILOT_DOCUMENT_TEST_OUTPUT") ?? Path.GetTempPath(), "FilePilot-real-" + Guid.NewGuid().ToString("N"))).FullName;

    [LibreOfficeFact]
    public async Task RendersTwoPagesPreservingSourceAndRefusingCollision()
    {
        var root = NewRoot();
        var source = CreateDocument(root);
        var before = SHA256.HashData(File.ReadAllBytes(source));
        var modified = File.GetLastWriteTimeUtc(source);
        var captured = new CaptureInput(new LibreOfficePdfEngine(Engine), root);
        var converter = new DocxPdfConverter(captured);
        var result = await converter.ConvertAsync(new(source, root));
        Assert.True(new FileInfo(result.OutputPath).Length > 1000);
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(source)));
        Assert.Equal(modified, File.GetLastWriteTimeUtc(source));
        var pdfBefore = SHA256.HashData(File.ReadAllBytes(result.OutputPath));
        await Assert.ThrowsAsync<FileCollisionException>(() => converter.ConvertAsync(new(source, root)));
        Assert.Equal(pdfBefore, SHA256.HashData(File.ReadAllBytes(result.OutputPath)));
        Assert.Empty(Directory.GetDirectories(root, ".filepilot-doc-*"));
        Assert.False(Directory.Exists(captured.Job));
        File.WriteAllText(Path.Combine(root, "render-result.txt"), result.OutputPath);
    }

    [LibreOfficeFact]
    public async Task CancelsAfterRealEngineLaunchAndCleansOwnedWork()
    {
        var root = NewRoot(); var source = CreateDocument(root);
        using var cancellation = new CancellationTokenSource();
        var engine = new CancelAfterLaunch(new LibreOfficePdfEngine(Engine), cancellation);
        var converter = new DocxPdfConverter(engine);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => converter.ConvertAsync(new(source, root), cancellation.Token));
        Assert.Empty(Directory.GetFiles(root, "*.pdf"));
        Assert.Empty(Directory.GetDirectories(root, ".filepilot-doc-*"));
        Assert.True(File.Exists(source));
        Assert.False(Directory.Exists(engine.Job));
    }

    [LibreOfficeFact]
    public async Task RealEngineDeadlineAndUnreadableDocumentFailWithoutFinalOutput()
    {
        var root = NewRoot(); var source = CreateDocument(root);
        var error = await Assert.ThrowsAsync<DocumentEngineException>(() => new DocxPdfConverter(
            new LibreOfficePdfEngine(Engine, TimeSpan.FromMilliseconds(1))).ConvertAsync(new(source, root)));
        Assert.Contains("time limit", error.Message); Assert.False(error.MayStillBeRunning);
        Assert.Empty(Directory.GetDirectories(root, ".filepilot-doc-*"));
        // Exercise LibreOffice's own failure path, independently of preflight rejection.
        var broken = Path.Combine(root, "broken.docx"); File.WriteAllText(broken, "PK\u0003\u0004invalid zip bytes");
        var output = Directory.CreateDirectory(Path.Combine(root, "engine-output")).FullName;
        var profile = Directory.CreateDirectory(Path.Combine(root, "engine-profile")).FullName;
        var invalid = await Assert.ThrowsAsync<DocumentEngineException>(() => new LibreOfficePdfEngine(Engine).ExportAsync(broken, output, profile, default));
        Assert.False(invalid.MayStillBeRunning);
        Assert.Empty(Directory.GetFiles(output, "*.pdf"));
        Assert.Empty(Directory.GetFiles(root, "*.pdf"));
    }

    internal static string CreateDocument(string root)
    {
        var path = Path.Combine(root, "Fidelity 文 spaced.docx");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        void Xml(string name, string value) { using var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false)); writer.Write(value); }
        Xml("[Content_Types].xml", """
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
            <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="png" ContentType="image/png"/>
            <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
            <Override PartName="/docProps/core.xml" ContentType="application/vnd.openxmlformats-package.core-properties+xml"/>
            <Override PartName="/docProps/custom.xml" ContentType="application/vnd.openxmlformats-officedocument.custom-properties+xml"/></Types>
            """);
        Xml("_rels/.rels", """
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
            <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
            <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties" Target="docProps/core.xml"/>
            <Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/custom-properties" Target="docProps/custom.xml"/></Relationships>
            """);
        Xml("docProps/core.xml", """
            <cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties" xmlns:dc="http://purl.org/dc/elements/1.1/">
            <dc:creator>PRIVATE_AUTHOR_SENTINEL</dc:creator><dc:title>PRIVATE_TITLE_SENTINEL</dc:title><dc:subject>PRIVATE_SUBJECT_SENTINEL</dc:subject><cp:keywords>PRIVATE_KEYWORDS_SENTINEL</cp:keywords><cp:lastModifiedBy>PRIVATE_EDITOR_SENTINEL</cp:lastModifiedBy></cp:coreProperties>
            """);
        Xml("docProps/custom.xml", """
            <Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/custom-properties" xmlns:vt="http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes"><property fmtid="{D5CDD505-2E9C-101B-9397-08002B2CF9AE}" pid="2" name="Private"><vt:lpwstr>PRIVATE_CUSTOM_SENTINEL</vt:lpwstr></property></Properties>
            """);
        Xml("word/_rels/document.xml.rels", """
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="image1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="media/image.png"/></Relationships>
            """);
        Xml("word/document.xml", """
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:pic="http://schemas.openxmlformats.org/drawingml/2006/picture"><w:body>
            <w:p><w:r><w:rPr><w:rFonts w:ascii="Arial" w:hAnsi="Arial"/><w:b/><w:sz w:val="36"/></w:rPr><w:t>FilePilot fidelity acceptance</w:t></w:r></w:p>
            <w:p><w:r><w:rPr><w:i/></w:rPr><w:t>Styled text, table, raster image and explicit page break.</w:t></w:r></w:p>
            <w:tbl><w:tblPr><w:tblBorders><w:top w:val="single" w:sz="8"/><w:left w:val="single" w:sz="8"/><w:bottom w:val="single" w:sz="8"/><w:right w:val="single" w:sz="8"/><w:insideH w:val="single" w:sz="8"/><w:insideV w:val="single" w:sz="8"/></w:tblBorders></w:tblPr><w:tblGrid><w:gridCol w:w="3000"/><w:gridCol w:w="3000"/></w:tblGrid><w:tr><w:tc><w:p><w:r><w:t>Format</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>Result</w:t></w:r></w:p></w:tc></w:tr><w:tr><w:tc><w:p><w:r><w:t>DOCX</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>PDF offline</w:t></w:r></w:p></w:tc></w:tr></w:tbl>
            <w:p><w:r><w:drawing><wp:inline><wp:extent cx="1828800" cy="914400"/><wp:docPr id="1" name="Color blocks"/><a:graphic><a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/picture"><pic:pic><pic:nvPicPr><pic:cNvPr id="1" name="image.png"/><pic:cNvPicPr/></pic:nvPicPr><pic:blipFill><a:blip r:embed="image1"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill><pic:spPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="1828800" cy="914400"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom></pic:spPr></pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p>
            <w:p><w:r><w:br w:type="page"/></w:r></w:p>
            <w:p><w:r><w:t>Second page: preserved layout and Unicode </w:t></w:r><w:r><w:rPr><w:rFonts w:eastAsia="Microsoft YaHei"/></w:rPr><w:t>文件转换</w:t></w:r></w:p>
            <w:sectPr><w:pgSz w:w="11906" w:h="16838"/><w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440"/></w:sectPr>
            </w:body></w:document>
            """);
        using var bitmap = new SKBitmap(200, 100);
        using (var canvas = new SKCanvas(bitmap)) { canvas.Clear(SKColors.CornflowerBlue); using var paint = new SKPaint { Color = SKColors.Orange }; canvas.DrawRect(100, 0, 100, 100, paint); }
        using var image = SKImage.FromBitmap(bitmap); using var bytes = image.Encode(SKEncodedImageFormat.Png, 100);
        using var entry = archive.CreateEntry("word/media/image.png").Open(); bytes.SaveTo(entry);
        return path;
    }

    private sealed class CancelAfterLaunch(IDocumentPdfEngine engine, CancellationTokenSource cancellation) : IDocumentPdfEngine
    {
        public string? Job { get; private set; }
        public async Task ExportAsync(string inputPath, string outputFolder, string profileFolder, CancellationToken token)
        {
            Job = Path.GetDirectoryName(outputFolder);
            var existing = System.Diagnostics.Process.GetProcessesByName("soffice.bin").Select(process => { using (process) return process.Id; }).ToHashSet();
            var running = engine.ExportAsync(inputPath, outputFolder, profileFolder, token);
            var observed = new List<System.Diagnostics.Process>();
            var deadline = System.Diagnostics.Stopwatch.StartNew();
            while (!running.IsCompleted && deadline.Elapsed < TimeSpan.FromSeconds(10))
            {
                foreach (var process in System.Diagnostics.Process.GetProcessesByName("soffice.bin"))
                {
                    if (!existing.Contains(process.Id) && process.MainModule?.FileName?.Equals(Path.Combine(Path.GetDirectoryName(Engine)!, "soffice.bin"), StringComparison.OrdinalIgnoreCase) == true) observed.Add(process);
                    else process.Dispose();
                }
                if (observed.Count != 0) break;
                await Task.Delay(10, token);
            }
            cancellation.Cancel();
            try
            {
                Assert.NotEmpty(observed); // Require the native renderer, not only its console launcher.
                await running;
            }
            finally
            {
                foreach (var process in observed)
                {
                    using (process)
                    {
                        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                        Assert.True(process.HasExited);
                    }
                }
            }
        }
    }
    private sealed class CaptureInput(IDocumentPdfEngine engine, string root) : IDocumentPdfEngine
    {
        public string? Job { get; private set; }
        public async Task ExportAsync(string inputPath, string outputFolder, string profileFolder, CancellationToken token)
        {
            Job = Path.GetDirectoryName(outputFolder);
            File.Copy(inputPath, Path.Combine(root, "prepared-copy.docx"));
            try { await engine.ExportAsync(inputPath, outputFolder, profileFolder, token); }
            finally
            {
                var profile = Path.Combine(profileFolder, "user", "registrymodifications.xcu");
                if (File.Exists(profile)) File.Copy(profile, Path.Combine(root, "engine-profile.xcu"));
            }
        }
    }
}
