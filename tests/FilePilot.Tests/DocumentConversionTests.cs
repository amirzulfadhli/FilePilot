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
    [Fact]
    public async Task RemovesPropertiesOnlyFromTheOwnedCopy()
    {
        var source = LibreOfficeAcceptanceTests.CreateDocument(root);
        var before = File.ReadAllBytes(source);
        var engine = new FakeEngine(async (input, folder, profile, token) =>
        {
            using (var zip = ZipFile.OpenRead(input))
            {
                Assert.DoesNotContain(zip.Entries, entry => entry.FullName.StartsWith("docProps/"));
                foreach (var name in new[] { "[Content_Types].xml", "_rels/.rels" })
                { using var reader = new StreamReader(zip.GetEntry(name)!.Open()); Assert.DoesNotContain("docProps/", reader.ReadToEnd()); }
                Assert.NotNull(zip.GetEntry("word/document.xml")); Assert.NotNull(zip.GetEntry("word/media/image.png"));
            }
            await GoodEngine().ExportAsync(input, folder, profile, token);
        });
        await Converter(engine).ConvertAsync(new(source, output));
        Assert.Equal(before, File.ReadAllBytes(source)); AssertClean();
    }
    private DocxPdfConverter Converter(IDocumentPdfEngine engine, DocumentConversionLimits? limits = null) => new(engine, limits) { WorkingDirectoryRoot = output };
    private void AssertClean() { Assert.Empty(Directory.GetDirectories(output, ".filepilot-doc-*")); Assert.Empty(Directory.GetFiles(output, ".filepilot-*.tmp")); }
    [Fact]
    public async Task FinalizesEnginePdfPreservingOriginalAndSpacedUnicodeNames()
    {
        var source = Docx(); var bytes = File.ReadAllBytes(source); var modified = File.GetLastWriteTimeUtc(source);
        var result = await Converter(GoodEngine()).ConvertAsync(new(source, output));
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
        await Assert.ThrowsAsync<DocumentConversionException>(() => Converter(engine).ConvertAsync(new(source, output)));
        Assert.Equal(0, engine.Calls); Assert.True(File.Exists(source)); AssertClean();
    }
    [Fact]
    public async Task RejectsExternalRelationshipsAndLinkedFields()
    {
        var source = Docx(extraName: "word/_rels/document.xml.rels", extraText: "<Relationships><Relationship TargetMode=\"External\" Target=\"https://example.invalid/image\" /></Relationships>");
        var engine = GoodEngine();
        await Assert.ThrowsAsync<DocumentConversionException>(() => Converter(engine).ConvertAsync(new(source, output)));
        File.Delete(source);
        source = Docx("<w:p><w:r><w:instrText>PAGE</w:instrText><w:instrText>INC</w:instrText><w:instrText>LUDEPICTURE</w:instrText></w:r></w:p>");
        await Assert.ThrowsAsync<DocumentConversionException>(() => Converter(engine).ConvertAsync(new(source, output)));
        Assert.Equal(0, engine.Calls); AssertClean();
    }
    [Fact]
    public async Task RejectsDtdCorruptedAndMismatchedDocuments()
    {
        var path = Path.Combine(root, "broken.docx"); File.WriteAllText(path, "not a zip"); var engine = GoodEngine();
        await Assert.ThrowsAsync<DocumentConversionException>(() => Converter(engine).ConvertAsync(new(path, output)));
        var source = Docx(extraName: "evil.xml", extraText: "<!DOCTYPE x SYSTEM 'file:///C:/secret'><x/>");
        await Assert.ThrowsAsync<DocumentConversionException>(() => Converter(engine).ConvertAsync(new(source, output)));
        var text = Path.Combine(root, "text.txt"); File.WriteAllText(text, "text");
        await Assert.ThrowsAsync<DocumentConversionException>(() => Converter(engine).ConvertAsync(new(text, output)));
        Assert.Equal(0, engine.Calls); AssertClean();
    }
    [Fact]
    public async Task RefusesExistingAndLateCollisions()
    {
        var source = Docx(); var destination = Path.Combine(output, "source 文 name.pdf"); File.WriteAllText(destination, "keep");
        var engine = GoodEngine(); await Assert.ThrowsAsync<FileCollisionException>(() => Converter(engine).ConvertAsync(new(source, output)));
        Assert.Equal(0, engine.Calls); Assert.Equal("keep", File.ReadAllText(destination));
        File.Delete(destination);
        engine = new(async (_, folder, _, token) => { await File.WriteAllTextAsync(Path.Combine(folder, "input.pdf"), "%PDF-1.7\nfixture\n%%EOF", token); File.WriteAllText(destination, "racer"); });
        await Assert.ThrowsAsync<FileCollisionException>(() => Converter(engine).ConvertAsync(new(source, output)));
        Assert.Equal("racer", File.ReadAllText(destination)); AssertClean();
    }
    [Fact]
    public async Task FailureAndCancellationCleanOwnedWork()
    {
        var source = Docx();
        var engine = new FakeEngine((_, folder, _, _) => { File.WriteAllText(Path.Combine(folder, "partial.tmp"), "partial"); throw new DocumentEngineException("Synthetic failure"); });
        await Assert.ThrowsAsync<DocumentEngineException>(() => Converter(engine).ConvertAsync(new(source, output))); AssertClean();
        using var cancel = new CancellationTokenSource();
        engine = new((_, folder, _, token) => { File.WriteAllText(Path.Combine(folder, "partial.tmp"), "partial"); cancel.Cancel(); token.ThrowIfCancellationRequested(); return Task.CompletedTask; });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Converter(engine).ConvertAsync(new(source, output), cancel.Token)); AssertClean();
        Assert.Empty(Directory.GetFiles(output, "*.pdf")); Assert.True(File.Exists(source));
    }
    [Theory]
    [InlineData("")][InlineData("not a pdf")][InlineData("%PDF-1.7\nincomplete")]
    public async Task RefusesEmptyInvalidAndIncompleteEngineOutput(string data)
    {
        var source = Docx(); var engine = new FakeEngine(async (_, folder, _, token) => await File.WriteAllTextAsync(Path.Combine(folder, "input.pdf"), data, token));
        await Assert.ThrowsAsync<DocumentConversionException>(() => Converter(engine).ConvertAsync(new(source, output))); AssertClean();
    }
    [Fact]
    public async Task ResourceLimitsRejectBeforeEngineAndUnsafeDestinationsFail()
    {
        var source = Docx(); var engine = GoodEngine();
        await Assert.ThrowsAsync<DocumentConversionException>(() => Converter(engine, new(MaxInputBytes: 1)).ConvertAsync(new(source, output)));
        await Assert.ThrowsAsync<DocumentConversionException>(() => Converter(engine, new(MaxExpandedBytes: 1)).ConvertAsync(new(source, output)));
        await Assert.ThrowsAsync<DocumentConversionException>(() => Converter(engine, new(MaxEntries: 1)).ConvertAsync(new(source, output)));
        await Assert.ThrowsAsync<ArgumentException>(() => Converter(engine).ConvertAsync(new(source, Path.Combine(output, ".."))));
        Assert.Equal(0, engine.Calls); AssertClean();
    }
    private string FixtureData => Path.Combine(root, "engine data with spaces 文");
    private IDocumentPdfEngine FixtureEngine(string mode, TimeSpan? timeout = null)
    {
        Directory.CreateDirectory(FixtureData);
        var executable = Path.Combine(AppContext.BaseDirectory, "EngineFixture", "engine with spaces 文", "soffice.com");
        return new FakeEngine(async (input, destination, profile, token) =>
        {
            File.WriteAllText(Path.Combine(profile, "fixture-control.json"), JsonSerializer.Serialize(new { Root = FixtureData, Mode = mode }));
            try { await new LibreOfficePdfEngine(executable, timeout).ExportAsync(input, destination, profile, token); }
            finally
            {
                var marker = Path.Combine(FixtureData, "started.txt");
                if (File.Exists(marker))
                {
                    var pid = int.Parse(File.ReadAllText(marker), System.Globalization.CultureInfo.InvariantCulture);
                    System.Diagnostics.Process? process = null;
                    try { process = System.Diagnostics.Process.GetProcessById(pid); }
                    catch (ArgumentException) { } // The owned process has already left the process table.
                    using (process) Assert.True(process is null || process.HasExited, $"Owned fixture process {pid} is still running after {mode}.");
                }
            }
        });
    }
    [Fact]
    public async Task NativeInitializationFailureExplainsRuntimeRepairAndCleansWork()
    {
        var source = Docx();
        var error = await Assert.ThrowsAsync<DocumentEngineException>(() => Converter(FixtureEngine("missing-runtime")).ConvertAsync(new(source, output)));
        Assert.Contains("0xC0000135", error.Message); Assert.Contains("Visual C++ runtime", error.Message); AssertClean();
    }
    [Theory]
    [InlineData("success")][InlineData("noisy")]
    public async Task RealSubprocessUsesQuotedArgumentsAndDrainsBothPipes(string mode)
    {
        var source = Docx();
        var result = await Converter(FixtureEngine(mode, TimeSpan.FromSeconds(15))).ConvertAsync(new(source, output));
        Assert.True(File.Exists(result.OutputPath));
        var args = JsonSerializer.Deserialize<string[]>(File.ReadAllText(Path.Combine(FixtureData, "arguments.json")))!;
        Assert.Contains("pdf:writer_pdf_Export", args); Assert.Contains("--headless", args);
        Assert.StartsWith("-env:UserInstallation=file:///", args[0]); Assert.Contains("%20", args[0]);
        var profile = File.ReadAllText(Path.Combine(FixtureData, "requested-profile.xcu"));
        Assert.Contains("DisableMacrosExecution", profile); Assert.Contains("DisableActiveContent", profile); Assert.Contains("<value>true</value>", profile); AssertClean();
    }
    [Fact]
    public async Task RealSubprocessFailureIsBoundedAndCleansWork()
    {
        var source = Docx();
        var error = await Assert.ThrowsAsync<DocumentEngineException>(() => Converter(FixtureEngine("fail")).ConvertAsync(new(source, output)));
        Assert.Contains("exit 7", error.Message); Assert.True(error.Message.Length < 5000); AssertClean();
    }
    [Fact]
    public async Task RealSubprocessTimeoutAndCancellationStopOwnedEngine()
    {
        var source = Docx();
        var error = await Assert.ThrowsAsync<DocumentEngineException>(() => Converter(FixtureEngine("hang", TimeSpan.FromSeconds(2))).ConvertAsync(new(source, output)));
        Assert.Contains("time limit", error.Message); Assert.False(error.MayStillBeRunning); AssertClean();
        File.Delete(Path.Combine(FixtureData, "started.txt"));
        using var cancel = new CancellationTokenSource();
        var task = Converter(FixtureEngine("hang")).ConvertAsync(new(source, output), cancel.Token);
        var start = System.Diagnostics.Stopwatch.StartNew();
        while (!File.Exists(Path.Combine(FixtureData, "started.txt")) && start.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(20);
        Assert.True(File.Exists(Path.Combine(FixtureData, "started.txt")));
        cancel.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task); AssertClean();
    }
    [Fact]
    public async Task MissingOutputAndOutputLimitFailuresAreExplicit()
    {
        var source = Docx();
        var none = new FakeEngine((_, _, _, _) => Task.CompletedTask);
        var error = await Assert.ThrowsAsync<DocumentConversionException>(() => Converter(none).ConvertAsync(new(source, output)));
        Assert.Contains("without creating a PDF", error.Message); AssertClean();
        await Assert.ThrowsAsync<DocumentConversionException>(() => Converter(GoodEngine(), new(MaxOutputBytes: 12)).ConvertAsync(new(source, output)));
        AssertClean();
    }
    [Fact]
    public async Task FolderCollisionPreCancellationAndLockedSourceCannotStartEngine()
    {
        var source = Docx(); var engine = GoodEngine();
        using (File.Open(source, FileMode.Open, FileAccess.Read, FileShare.None))
            await Assert.ThrowsAsync<IOException>(() => Converter(engine).ConvertAsync(new(source, output)));
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Converter(engine).ConvertAsync(new(source, output), cancel.Token));
        Directory.CreateDirectory(Path.Combine(output, "source 文 name.pdf"));
        await Assert.ThrowsAsync<FileCollisionException>(() => Converter(engine).ConvertAsync(new(source, output)));
        Assert.Equal(0, engine.Calls); AssertClean();
    }
    [Fact]
    public async Task PotentiallyRunningEngineRetainsWorkInsteadOfRacingDeletion()
    {
        var source = Docx();
        var engine = new FakeEngine((_, _, _, _) => throw new DocumentEngineException("Cannot stop", mayStillBeRunning: true));
        var error = await Assert.ThrowsAsync<DocumentConversionException>(() => Converter(engine).ConvertAsync(new(source, output)));
        Assert.Contains("retained", error.Message); Assert.Single(Directory.GetDirectories(output, ".filepilot-doc-*"));
        Assert.Empty(Directory.GetFiles(output, "*.pdf")); Assert.True(File.Exists(source));
    }
    [Fact]
    public async Task DuplicatePackagePathsAreRefusedBeforeEngineStart()
    {
        var source = Docx(extraName: "WORD/DOCUMENT.XML", extraText: "<x/>"); var engine = GoodEngine();
        await Assert.ThrowsAsync<DocumentConversionException>(() => Converter(engine).ConvertAsync(new(source, output)));
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
        await Assert.ThrowsAsync<DocumentConversionException>(() => Converter(engine).ConvertAsync(new(source, output)));
        File.Delete(source);
        source = Docx(extraName: "word/_rels/document.xml.rels", extraText: "<Relationships><Relationship Target=\"../../../outside.xml\" /></Relationships>");
        await Assert.ThrowsAsync<DocumentConversionException>(() => Converter(engine).ConvertAsync(new(source, output)));
        File.Delete(source);
        source = Docx(extraName: "word/_rels/document.xml.rels", extraText: "<Relationships><Relationship TargetMode=\"Internal\" Target=\"https://example.invalid/image\" /></Relationships>");
        await Assert.ThrowsAsync<DocumentConversionException>(() => Converter(engine).ConvertAsync(new(source, output)));
        File.Delete(source);
        source = Docx(extraName: "word/_rels/document.xml.rels", extraText: "<Relationships><Relationship Target=\"//example.invalid/share.xml\" /></Relationships>");
        await Assert.ThrowsAsync<DocumentConversionException>(() => Converter(engine).ConvertAsync(new(source, output)));
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
        await Converter(engine).ConvertAsync(new(source, output));
        Assert.Equal(1, engine.Calls); AssertClean();
    }
    private sealed class FakeEngine(Func<string, string, string, CancellationToken, Task> run) : IDocumentPdfEngine
    {
        public int Calls { get; private set; }
        public Task ExportAsync(string inputPath, string outputFolder, string profileFolder, CancellationToken cancellationToken) { Calls++; return run(inputPath, outputFolder, profileFolder, cancellationToken); }
    }
    public void Dispose() => Directory.Delete(root, true);
}
