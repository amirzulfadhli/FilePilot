using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows.Interop;
using System.Windows.Threading;
using FilePilot.App.Widget;
using FilePilot.Core.Conversion;
using SkiaSharp;

namespace FilePilot.App.Diagnostics;

internal sealed record MemorySnapshot(long WorkingSetBytes, long PrivateBytes, long ManagedBytes, long PeakWorkingSetBytes)
{
    internal static MemorySnapshot Capture()
    {
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        return new(process.WorkingSet64, process.PrivateMemorySize64, GC.GetTotalMemory(forceFullCollection: false), process.PeakWorkingSet64);
    }
}

internal sealed record IdleMemorySample(double ElapsedMilliseconds, MemorySnapshot Memory);

/// <summary>Explicit, local-only diagnostic workload. Normal launches never instantiate this runner.</summary>
internal static class DiagnosticRunner
{
    internal static async Task RunAsync(MainWindow window, DiagnosticOptions options, Task rendered, bool? hotkeyRegistered)
    {
        await rendered.WaitAsync(TimeSpan.FromSeconds(10));
        using var process = Process.GetCurrentProcess();
        var startupMs = (DateTime.UtcNow - process.StartTime.ToUniversalTime()).TotalMilliseconds;
        var firstFrame = MemorySnapshot.Capture();
        var nativeApis = CheckNativeApis();
        if (!nativeApis) throw new InvalidOperationException("A required Windows desktop API is unavailable.");
        if (hotkeyRegistered == false) throw new InvalidOperationException("The diagnostic shortcut is unavailable. Close other FilePilot instances or conflicting shortcut owners and retry.");
        await Task.Delay(2000); // Allow initial WPF/JIT work to settle; no forced GC or working-set trimming.
        window.Hide();
        var idleClock = Stopwatch.StartNew();
        var cpuStart = process.TotalProcessorTime;
        var idleSamples = new List<IdleMemorySample>();
        for (int waited = 0; waited < options.RequestedIdleSeconds;)
        {
            int interval = Math.Min(10, options.RequestedIdleSeconds - waited);
            await Task.Delay(TimeSpan.FromSeconds(interval));
            waited += interval;
            idleSamples.Add(new(idleClock.Elapsed.TotalMilliseconds, MemorySnapshot.Capture()));
        }
        process.Refresh();
        var idleCpuMs = (process.TotalProcessorTime - cpuStart).TotalMilliseconds;
        var idleWallMs = idleClock.Elapsed.TotalMilliseconds;
        var hidden = idleSamples[^1].Memory;
        int cycles = options.Extended ? 100 : 20;
        for (int i = 0; i < cycles; i++)
        {
            window.ShowPopup();
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            window.Hide();
        }
        int settingsCycles = options.Extended ? 10 : 0;
        for (int i = 0; i < settingsCycles; i++)
        {
            // Synthetic settings only: never save preferences or register a new shortcut.
            var settings = new SettingsWindow(new(), _ => { }) { Owner = window, IsEnabled = false };
            try
            {
                settings.Show();
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            }
            finally { settings.Close(); }
        }
        await Task.Delay(2000);
        var afterCycles = MemorySnapshot.Capture();
        // Run codecs after idle sampling so native codec initialization is reported separately.
        var smoke = await CodecSmokeAsync(Path.GetDirectoryName(options.ReportPath)!);
        var afterCodec = MemorySnapshot.Capture();
        var hwnd = new WindowInteropHelper(window).Handle;
        DiagnosticOptions.WriteReport(options.ReportPath, new
        {
            SchemaVersion = 1,
            Succeeded = true,
            Mode = options.Widget ? "widget" : "popup",
            OsDescription = RuntimeInformation.OSDescription,
            WindowsBuild = Environment.OSVersion.Version.Build,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            Framework = RuntimeInformation.FrameworkDescription,
            StopwatchFrequency = Stopwatch.Frequency,
            ProcessStartToContentRenderedMilliseconds = startupMs,
            FirstFrame = firstFrame,
            HiddenIdle = hidden,
            IdleCpuMilliseconds = idleCpuMs,
            IdleWallMilliseconds = idleWallMs,
            RequestedIdleSeconds = options.RequestedIdleSeconds,
            IdleSamples = idleSamples,
            PopupCycles = cycles,
            SettingsCycles = settingsCycles,
            Extended = options.Extended,
            AfterPopupCycles = afterCycles,
            AfterCodec = afterCodec,
            NativeApisAvailable = nativeApis,
            WindowDpi = NativeMethods.GetDpiForWindow(hwnd),
            HotkeyRegistered = hotkeyRegistered,
            CodecSmoke = smoke,
            ForcedGarbageCollection = false,
            CreatedUtc = DateTime.UtcNow
        });
    }

    internal static bool CheckNativeApis()
    {
        var module = NativeLibrary.Load("user32.dll");
        try
        {
            return new[] { "GetDpiForWindow", "RegisterHotKey", "UnregisterHotKey", "GetMonitorInfoW", "MonitorFromPoint", "SetWindowPos" }
                .All(name => NativeLibrary.TryGetExport(module, name, out _));
        }
        finally { NativeLibrary.Free(module); }
    }

    private static async Task<object> CodecSmokeAsync(string folder)
    {
        // Keep these synthetic fixtures beside the report, never read or modify a user's document/image.
        var scratch = Path.Combine(folder, "codec-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        var input = Path.Combine(scratch, "diagnostic 文 image.png");
        using (var bitmap = new SKBitmap(12, 8))
        {
            bitmap.Erase(SKColors.CornflowerBlue);
            using var image = SKImage.FromBitmap(bitmap);
            using var bytes = image.Encode(SKEncodedImageFormat.Png, 100);
            using var output = new FileStream(input, FileMode.CreateNew);
            bytes.SaveTo(output);
        }
        var hash = SHA256.HashData(File.ReadAllBytes(input));
        var converter = new ImageFileConverter();
        var jpeg = await converter.ConvertAsync(new(input, scratch, ImageFileFormat.Jpg));
        var webp = await converter.ConvertAsync(new(input, scratch, ImageFileFormat.WebP));
        using var decoded = SKBitmap.Decode(webp.OutputPath);
        if (jpeg.Width != 12 || jpeg.Height != 8 || decoded is null || decoded.Width != 12 || decoded.Height != 8 ||
            !hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(input))))
            throw new InvalidOperationException("Extracted-package native codec smoke check failed.");
        try
        {
            await converter.ConvertAsync(new(input, scratch, ImageFileFormat.Jpg));
            throw new InvalidOperationException("Extracted-package collision protection failed.");
        }
        catch (FilePilot.Core.FileCollisionException) { }
        return new { PngToJpeg = true, PngToWebP = true, DimensionsPreserved = true, OriginalPreserved = true, CollisionRefused = true };
    }
}
