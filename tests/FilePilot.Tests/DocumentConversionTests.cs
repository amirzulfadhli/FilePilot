using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using FilePilot.Core;
using FilePilot.Core.Documents;
using Xunit;

namespace FilePilot.Tests;

public sealed class DocumentConversionTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "FilePilot-doc-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string output;
    public DocumentConversionTests() { Directory.CreateDirectory(root); output = Directory.CreateDirectory(Path.Combine(root, "output 文 folder")).FullName; }
    private string Docx(string? body = null, string? extraName = null, string? extraText = null)
    {
        var path = Path.Combine(root, "source 文 name.docx");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        void Entry(string name, string text) { using var writer = new StreamWriter(zip.CreateEntry(name).Open()); writer.Write(text); }
        Entry("[Content_Types].xml", "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\" /></Types>");
        Entry("_rels/.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\" /></Relationships>");
        Entry("word/document.xml", "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>" + (body ?? "<w:p><w:r><w:t>Hello 文</w:t></w:r></w:p>") + "</w:body></w:document>");
        if (extraName is not null) Entry(extraName, extraText ?? "content");
        return path;
    }
    private static FakeEngine GoodEngine() => new(async (_, folder, _, token) => await File.WriteAllTextAsync(Path.Combine(folder, "input.pdf"), "%PDF-1.7\nSynthetic PDF fixture\n%%EOF\n", token));
    private void AssertClean() { Assert.Empty(Directory.GetDirectories(output, ".filepilot-doc-*")); Assert.Empty(Directory.GetFiles(output, ".filepilot-*.tmp")); }
    [Fact]
    public async Task FinalizesEnginePdfPreservingOriginalAndSpacedUnicodeNames()
    {
        var source = Docx(); var bytes = File.ReadAllBytes(source); var modified = File.GetLastWriteTimeUtc(source);
        var result = await new DocxPdfConverter(GoodEngine()).ConvertAsync(new(source, output));
        Assert.Equal(Path.Combine(output, "source 文 name.pdf"), result.OutputPath);
        Assert.StartsWith("%PDF-", File.ReadAllText(result.OutputPath));
        Assert.Equal(bytes, File.ReadAllBytes(source)); Assert.Equal(modified, File.GetLastWriteTimeUtc(source)); AssertClean();
    }
    [Theory]
    [InlineData("word/media/disguised.png")][InlineData("word/vbaProject.bin")][InlineData("word/embeddings/ole.bin")][InlineData("word/media/image.svg")]
    [InlineData("../outside.xml")][InlineData("/absolute.xml")][InlineData("word/%2e%2e/bad.xml")]
    public async Task RejectsUnsafeOrActivePackageContentBeforeStartingEngine(string name)
    {
        var source = Docx(extraName: name, extraText: "<x/>"); var engine = GoodEngine();
        await Assert.ThrowsAsync<DocumentConversionException>(() => new DocxPdfConverter(engine).ConvertAsync(new(source, output)));
        Assert.Equal(0, engine.Calls); Assert.True(File.Exists(source)); AssertClean();
    }
    [Fact]
    public async Task RejectsExternalRelationshipsAndLinkedFields()
    {
        var source = Docx(extraName: "word/_rels/document.xml.rels", extraText: "<Relationships><Relationship TargetMode=\"External\" Target=\"https://example.invalid/image\" /></Relationships>");
        var engine = GoodEngine();
        await Assert.ThrowsAsync<DocumentConversionException>(() => new DocxPdfConverter(engine).ConvertAsync(new(source, output)));
        File.Delete(source);
        source = Docx("<w:p><w:r><w:instrText>PAGE</w:instrText><w:instrText>INC</w:instrText><w:instrText>LUDEPICTURE</w:instrText></w:r></w:p>");
        await Assert.ThrowsAsync<DocumentConversionException>(() => new DocxPdfConverter(engine).ConvertAsync(new(source, output)));
        Assert.Equal(0, engine.Calls); AssertClean();
    }
    [Fact]
    public async Task RejectsDtdCorruptedAndMismatchedDocuments()
    {
        var path = Path.Combine(root, "broken.docx"); File.WriteAllText(path, "not a zip"); var engine = GoodEngine();
        await Assert.ThrowsAsync<DocumentConversionException>(() => new DocxPdfConverter(engine).ConvertAsync(new(path, output)));
        var source = Docx(extraName: "evil.xml", extraText: "<!DOCTYPE x SYSTEM 'file:///C:/secret'><x/>");
        await Assert.ThrowsAsync<DocumentConversionException>(() => new DocxPdfConverter(engine).ConvertAsync(new(source, output)));
        var text = Path.Combine(root, "text.txt"); File.WriteAllText(text, "text");
        await Assert.ThrowsAsync<DocumentConversionException>(() => new DocxPdfConverter(engine).ConvertAsync(new(text, output)));
        Assert.Equal(0, engine.Calls); AssertClean();
    }
    [Fact]
    public async Task RefusesExistingAndLateCollisions()
    {
        var source = Docx(); var destination = Path.Combine(output, "source 文 name.pdf"); File.WriteAllText(destination, "keep");
        var engine = GoodEngine(); await Assert.ThrowsAsync<FileCollisionException>(() => new DocxPdfConverter(engine).ConvertAsync(new(source, output)));
        Assert.Equal(0, engine.Calls); Assert.Equal("keep", File.ReadAllText(destination));
        File.Delete(destination);
        engine = new(async (_, folder, _, token) => { await File.WriteAllTextAsync(Path.Combine(folder, "input.pdf"), "%PDF-1.7\nfixture\n%%EOF", token); File.WriteAllText(destination, "racer"); });
        await Assert.ThrowsAsync<FileCollisionException>(() => new DocxPdfConverter(engine).ConvertAsync(new(source, output)));
        Assert.Equal("racer", File.ReadAllText(destination)); AssertClean();
    }
    [Fact]
    public async Task FailureAndCancellationCleanOwnedWork()
    {
        var source = Docx();
        var engine = new FakeEngine((_, folder, _, _) => { File.WriteAllText(Path.Combine(folder, "partial.tmp"), "partial"); throw new DocumentEngineException("Synthetic failure"); });
        await Assert.ThrowsAsync<DocumentEngineException>(() => new DocxPdfConverter(engine).ConvertAsync(new(source, output))); AssertClean();
        using var cancel = new CancellationTokenSource();
        engine = new((_, folder, _, token) => { File.WriteAllText(Path.Combine(folder, "partial.tmp"), "partial"); cancel.Cancel(); token.ThrowIfCancellationRequested(); return Task.CompletedTask; });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new DocxPdfConverter(engine).ConvertAsync(new(source, output), cancel.Token)); AssertClean();
        Assert.Empty(Directory.GetFiles(output, "*.pdf")); Assert.True(File.Exists(source));
    }
    [Theory]
    [InlineData("")][InlineData("not a pdf")][InlineData("%PDF-1.7\nincomplete")]
    public async Task RefusesEmptyInvalidAndIncompleteEngineOutput(string data)
    {
        var source = Docx(); var engine = new FakeEngine(async (_, folder, _, token) => await File.WriteAllTextAsync(Path.Combine(folder, "input.pdf"), data, token));
        await Assert.ThrowsAsync<DocumentConversionException>(() => new DocxPdfConverter(engine).ConvertAsync(new(source, output))); AssertClean();
    }
    [Fact]
    public async Task ResourceLimitsRejectBeforeEngineAndUnsafeDestinationsFail()
    {
        var source = Docx(); var engine = GoodEngine();
        await Assert.ThrowsAsync<DocumentConversionException>(() => new DocxPdfConverter(engine, new(MaxInputBytes: 1)).ConvertAsync(new(source, output)));
        await Assert.ThrowsAsync<DocumentConversionException>(() => new DocxPdfConverter(engine, new(MaxExpandedBytes: 1)).ConvertAsync(new(source, output)));
        await Assert.ThrowsAsync<DocumentConversionException>(() => new DocxPdfConverter(engine, new(MaxEntries: 1)).ConvertAsync(new(source, output)));
        await Assert.ThrowsAsync<ArgumentException>(() => new DocxPdfConverter(engine).ConvertAsync(new(source, Path.Combine(output, ".."))));
        Assert.Equal(0, engine.Calls); AssertClean();
    }
    private string FixtureEngine(string mode)
    {
        var folder = Directory.CreateDirectory(Path.Combine(root, "engine with spaces 文")).FullName;
        foreach (var source in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "EngineFixture"))) File.Copy(source, Path.Combine(folder, Path.GetFileName(source)));
        File.Copy(Path.Combine(folder, "FilePilot.EngineFixture.exe"), Path.Combine(folder, "soffice.com"));
        File.WriteAllText(Path.Combine(folder, "mode.txt"), mode);
        return Path.Combine(folder, "soffice.com");
    }
    [Theory]
    [InlineData("success")][InlineData("noisy")]
    public async Task RealSubprocessUsesQuotedArgumentsAndDrainsBothPipes(string mode)
    {
        var source = Docx(); var executable = FixtureEngine(mode);
        var result = await new DocxPdfConverter(new LibreOfficePdfEngine(executable, TimeSpan.FromSeconds(15))).ConvertAsync(new(source, output));
        Assert.True(File.Exists(result.OutputPath));
        var args = JsonSerializer.Deserialize<string[]>(File.ReadAllText(Path.Combine(Path.GetDirectoryName(executable)!, "arguments.json")))!;
        Assert.Contains("pdf:writer_pdf_Export", args); Assert.Contains("--headless", args);
        Assert.StartsWith("-env:UserInstallation=file:///", args[0]); Assert.Contains("%20", args[0]);
        var profile = File.ReadAllText(Path.Combine(Path.GetDirectoryName(executable)!, "requested-profile.xcu"));
        Assert.Contains("DisableMacrosExecution", profile); Assert.Contains("DisableActiveContent", profile); Assert.Contains("<value>true</value>", profile); AssertClean();
    }
    [Fact]
    public async Task RealSubprocessFailureIsBoundedAndCleansWork()
    {
        var source = Docx(); var executable = FixtureEngine("fail");
        var error = await Assert.ThrowsAsync<DocumentEngineException>(() => new DocxPdfConverter(new LibreOfficePdfEngine(executable)).ConvertAsync(new(source, output)));
        Assert.Contains("exit 7", error.Message); Assert.True(error.Message.Length < 5000); AssertClean();
    }
    [Fact]
    public async Task RealSubprocessTimeoutAndCancellationStopOwnedEngine()
    {
        var source = Docx(); var executable = FixtureEngine("hang");
        var error = await Assert.ThrowsAsync<DocumentEngineException>(() => new DocxPdfConverter(new LibreOfficePdfEngine(executable, TimeSpan.FromSeconds(2))).ConvertAsync(new(source, output)));
        Assert.Contains("time limit", error.Message); Assert.False(error.MayStillBeRunning); AssertClean();
        File.Delete(Path.Combine(Path.GetDirectoryName(executable)!, "started.txt"));
        using var cancel = new CancellationTokenSource();
        var task = new DocxPdfConverter(new LibreOfficePdfEngine(executable)).ConvertAsync(new(source, output), cancel.Token);
        var start = System.Diagnostics.Stopwatch.StartNew();
        while (!File.Exists(Path.Combine(Path.GetDirectoryName(executable)!, "started.txt")) && start.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(20);
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(executable)!, "started.txt")));
        cancel.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task); AssertClean();
    }
    [Fact]
    public async Task MissingOutputAndOutputLimitFailuresAreExplicit()
    {
        var source = Docx();
        var none = new FakeEngine((_, _, _, _) => Task.CompletedTask);
        var error = await Assert.ThrowsAsync<DocumentConversionException>(() => new DocxPdfConverter(none).ConvertAsync(new(source, output)));
        Assert.Contains("without creating a PDF", error.Message); AssertClean();
        await Assert.ThrowsAsync<DocumentConversionException>(() => new DocxPdfConverter(GoodEngine(), new(MaxOutputBytes: 12)).ConvertAsync(new(source, output)));
        AssertClean();
    }
    [Fact]
    public async Task FolderCollisionPreCancellationAndLockedSourceCannotStartEngine()
    {
        var source = Docx(); var engine = GoodEngine();
        using (File.Open(source, FileMode.Open, FileAccess.Read, FileShare.None))
            await Assert.ThrowsAsync<IOException>(() => new DocxPdfConverter(engine).ConvertAsync(new(source, output)));
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new DocxPdfConverter(engine).ConvertAsync(new(source, output), cancel.Token));
        Directory.CreateDirectory(Path.Combine(output, "source 文 name.pdf"));
        await Assert.ThrowsAsync<FileCollisionException>(() => new DocxPdfConverter(engine).ConvertAsync(new(source, output)));
        Assert.Equal(0, engine.Calls); AssertClean();
    }
    [Fact]
    public async Task PotentiallyRunningEngineRetainsWorkInsteadOfRacingDeletion()
    {
        var source = Docx();
        var engine = new FakeEngine((_, _, _, _) => throw new DocumentEngineException("Cannot stop", mayStillBeRunning: true));
        var error = await Assert.ThrowsAsync<DocumentConversionException>(() => new DocxPdfConverter(engine).ConvertAsync(new(source, output)));
        Assert.Contains("retained", error.Message); Assert.Single(Directory.GetDirectories(output, ".filepilot-doc-*"));
        Assert.Empty(Directory.GetFiles(output, "*.pdf")); Assert.True(File.Exists(source));
    }
    [Fact]
    public async Task DuplicatePackagePathsAreRefusedBeforeEngineStart()
    {
        var source = Docx(extraName: "WORD/DOCUMENT.XML", extraText: "<x/>"); var engine = GoodEngine();
        await Assert.ThrowsAsync<DocumentConversionException>(() => new DocxPdfConverter(engine).ConvertAsync(new(source, output)));
        Assert.Equal(0, engine.Calls); AssertClean();
    }
    [Fact]
    public void EngineSelectionRequiresAnExplicitExistingSofficeConsolePath()
    {
        var path = Path.Combine(root, "arbitrary.exe"); File.WriteAllText(path, "not executable");
        Assert.Throws<ArgumentException>(() => LibreOfficePdfEngine.ValidateExecutable(path));
        Assert.Throws<ArgumentException>(() => LibreOfficePdfEngine.ValidateExecutable("relative/soffice.com"));
        Assert.Throws<FileNotFoundException>(() => LibreOfficePdfEngine.ValidateExecutable(Path.Combine(root, "soffice.com")));
    }
    [Fact]
    public async Task RejectsInlineLinksAndRelationshipEscapesBeforeStartingEngine()
    {
        var engine = GoodEngine();
        var source = Docx("<w:p><w:r><w:pict xmlns:v=\"urn:schemas-microsoft-com:vml\"><v:imagedata src=\"https://example.invalid/image\" /></w:pict></w:r></w:p>");
        await Assert.ThrowsAsync<DocumentConversionException>(() => new DocxPdfConverter(engine).ConvertAsync(new(source, output)));
        File.Delete(source);
        source = Docx(extraName: "word/_rels/document.xml.rels", extraText: "<Relationships><Relationship Target=\"../../../outside.xml\" /></Relationships>");
        await Assert.ThrowsAsync<DocumentConversionException>(() => new DocxPdfConverter(engine).ConvertAsync(new(source, output)));
        File.Delete(source);
        source = Docx(extraName: "word/_rels/document.xml.rels", extraText: "<Relationships><Relationship TargetMode=\"Internal\" Target=\"https://example.invalid/image\" /></Relationships>");
        await Assert.ThrowsAsync<DocumentConversionException>(() => new DocxPdfConverter(engine).ConvertAsync(new(source, output)));
        File.Delete(source);
        source = Docx(extraName: "word/_rels/document.xml.rels", extraText: "<Relationships><Relationship Target=\"//example.invalid/share.xml\" /></Relationships>");
        await Assert.ThrowsAsync<DocumentConversionException>(() => new DocxPdfConverter(engine).ConvertAsync(new(source, output)));
        Assert.Equal(0, engine.Calls); AssertClean();
    }
    [Fact]
    public async Task OrdinaryEmbeddedRasterPartPassesPreflight()
    {
        var source = Docx();
        using (var zip = ZipFile.Open(source, ZipArchiveMode.Update))
        {
            using var bitmap = new SkiaSharp.SKBitmap(12, 8);
            bitmap.Erase(SkiaSharp.SKColors.White);
            using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
            using var bytes = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
            using var entry = zip.CreateEntry("word/media/image.png").Open();
            bytes.SaveTo(entry);
        }
        var engine = GoodEngine();
        await new DocxPdfConverter(engine).ConvertAsync(new(source, output));
        Assert.Equal(1, engine.Calls); AssertClean();
    }
    private sealed class FakeEngine(Func<string, string, string, CancellationToken, Task> run) : IDocumentPdfEngine
    {
        public int Calls { get; private set; }
        public Task ExportAsync(string inputPath, string outputFolder, string profileFolder, CancellationToken cancellationToken) { Calls++; return run(inputPath, outputFolder, profileFolder, cancellationToken); }
    }
    public void Dispose() => Directory.Delete(root, true);
}
