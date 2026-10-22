using System.IO;
using FilePilot.App;
using FilePilot.Core.Documents;
using Xunit;

namespace FilePilot.Tests;

public sealed class DocumentViewModelTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "FilePilot-doc-ui-" + Guid.NewGuid().ToString("N"));
    private readonly string source;
    public DocumentViewModelTests() { Directory.CreateDirectory(root); source = Path.Combine(root, "fixture.docx"); File.WriteAllText(source, "Injected converter does not parse this fixture."); }
    [Fact]
    public async Task DocumentBusyStateBlocksDuplicateActionsAndKeepsOriginalSelected()
    {
        var fake = new Controlled(); var model = new MainViewModel(_ => { }, documentConverter: fake);
        model.LoadFile(source); Assert.True(model.IsDocumentSelected); Assert.True(model.CanConvertDocument);
        var task = model.ConvertDocumentAsync();
        Assert.True(model.IsBusy); Assert.False(model.CanChooseFile); Assert.False(model.CanMove); Assert.False(model.CopyPathCommand.CanExecute(null));
        model.LoadFile("missing"); await model.ConvertDocumentAsync(); Assert.Equal(1, fake.Calls);
        var destination = Path.Combine(root, "fixture.pdf"); fake.Completion.SetResult(new(destination)); await task;
        Assert.Equal(source, model.FullPath); Assert.Equal(destination, model.LastOutputPath); Assert.True(model.CanOpenOutputFolder); Assert.True(model.CanConvertDocument); Assert.False(model.IsBusy);
    }
    [Fact]
    public async Task CancellationAndFailuresRestoreDocumentControls()
    {
        var fake = new Controlled(); var model = new MainViewModel(_ => { }, documentConverter: fake); model.LoadFile(source);
        var task = model.ConvertDocumentAsync(); model.CancelConversion(); Assert.True(fake.Token.IsCancellationRequested); Assert.True(model.IsBusy);
        fake.Completion.SetCanceled(fake.Token); await task; Assert.False(model.IsBusy); Assert.Contains("cancelled", model.Status); Assert.True(model.CanConvertDocument);
        fake = new Controlled(); model = new MainViewModel(_ => { }, documentConverter: fake); model.LoadFile(source); task = model.ConvertDocumentAsync();
        fake.Completion.SetException(new DocumentConversionException("Unsupported document")); await task;
        Assert.Equal("Unsupported document", model.Status); Assert.True(model.CanConvertDocument); Assert.Null(model.LastOutputPath);
    }
    [Fact]
    public void MissingEngineAndBatchDocumentSelectionsGiveSafeFallback()
    {
        var model = new MainViewModel(_ => { }); model.LoadFile(source);
        Assert.True(model.IsDocumentSelected); Assert.False(model.CanConvertDocument); Assert.Contains("Settings", model.DocumentEngineStatus);
        model.ConfigureDocumentEngine(Path.Combine(root, "missing", "soffice.com")); Assert.False(model.CanConvertDocument); Assert.Contains("unavailable", model.DocumentEngineStatus);
        var second = Path.Combine(root, "another.docx"); File.WriteAllText(second, "fixture"); model.LoadFiles([source, second]);
        Assert.False(model.IsDocumentSelected); Assert.False(model.CanConvertDocument); Assert.Contains("single selection", model.Status);
    }
    [Fact]
    public void OptionalEnginePreferenceRoundTripsWithoutResettingOtherPreferences()
    {
        var store = new SettingsStore(Path.Combine(root, "settings.json"));
        var path = Path.Combine(root, "missing installed folder", "soffice.com");
        store.Save(new("Alt+Shift+G", [root], true, path));
        var loaded = store.Load(); Assert.Equal(path, loaded.LibreOfficePath); Assert.Equal("Alt+Shift+G", loaded.Shortcut); Assert.True(loaded.DarkTheme);
        Assert.Throws<ArgumentException>(() => store.Save(loaded with { LibreOfficePath = "relative/soffice.com" }));
        Assert.Throws<ArgumentException>(() => store.Save(loaded with { LibreOfficePath = Path.Combine(root, "other.exe") }));
    }
    private sealed class Controlled : IDocumentConverter
    {
        public TaskCompletionSource<DocumentConversionResult> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken Token { get; private set; }
        public int Calls { get; private set; }
        public Task<DocumentConversionResult> ConvertAsync(DocumentConversionRequest request, CancellationToken cancellationToken = default)
        { Calls++; Token = cancellationToken; return Completion.Task; }
    }
    public void Dispose() => Directory.Delete(root, true);
}
