using System.Diagnostics;
using System.Text;

namespace FilePilot.Core.Documents;

public sealed class LibreOfficePdfEngine(string executablePath, TimeSpan? timeout = null) : IDocumentPdfEngine
{
    private readonly TimeSpan timeout = timeout ?? TimeSpan.FromSeconds(90);
    public static string ValidateExecutable(string path)
    {
        var file = new FileActions().Inspect(path);
        if (!file.Name.Equals("soffice.com", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose soffice.com in a trusted LibreOffice installation's program folder.");
        return file.FullPath;
    }

    internal static ProcessStartInfo BuildStartInfo(string executable, string input, string output, string profile)
    {
        executable = ValidateExecutable(executable);
        input = new FileActions().Inspect(input).FullPath;
        output = LocalPathPolicy.Validate(output);
        profile = LocalPathPolicy.Validate(profile);
        if (!Directory.Exists(output) || !Directory.Exists(profile)) throw new ArgumentException("Engine output/profile must be existing folders.");
        var start = new ProcessStartInfo(executable)
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = profile };
        var profileUri = new Uri(Path.TrimEndingDirectorySeparator(profile) + Path.DirectorySeparatorChar).AbsoluteUri;
        foreach (var argument in new[] { "-env:UserInstallation=" + profileUri, "--headless", "--nologo", "--nodefault", "--norestore",
            "--convert-to", "pdf:writer_pdf_Export", "--outdir", output, input }) start.ArgumentList.Add(argument);
        return start;
    }

    public async Task ExportAsync(string inputPath, string outputFolder, string profileFolder, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var start = BuildStartInfo(executablePath, inputPath, outputFolder, profileFolder);
        // Fresh, per-job preferences. Do not read or modify the user's LibreOffice profile.
        File.WriteAllText(Path.Combine(profileFolder, "registrymodifications.xcu"), """
            <?xml version="1.0" encoding="UTF-8"?>
            <oor:items xmlns:oor="http://openoffice.org/2001/registry">
              <item oor:path="/org.openoffice.Office.Common/Security/Scripting">
                <prop oor:name="MacroSecurityLevel" oor:op="fuse"><value>3</value></prop>
                <prop oor:name="DisableMacrosExecution" oor:op="fuse"><value>true</value></prop>
                <prop oor:name="DisableActiveContent" oor:op="fuse"><value>true</value></prop>
                <prop oor:name="DisableOLEAutomation" oor:op="fuse"><value>true</value></prop>
              </item>
            </oor:items>
            """);
        using var deadline = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        using var process = new Process { StartInfo = start };
        try { if (!process.Start()) throw new DocumentEngineException("LibreOffice did not start."); }
        catch (System.ComponentModel.Win32Exception ex) { throw new DocumentEngineException("LibreOffice could not start. Check the configured installation.", inner: ex); }
        var stdout = DrainAsync(process.StandardOutput, linked.Token);
        var stderr = DrainAsync(process.StandardError, linked.Token);
        try
        {
            await Task.WhenAll(process.WaitForExitAsync(linked.Token), stdout, stderr);
            if (process.ExitCode != 0) throw new DocumentEngineException($"LibreOffice export failed (exit {process.ExitCode}). {await stderr}");
        }
        catch (Exception failure)
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or TimeoutException)
            { throw new DocumentEngineException("The document engine could not be stopped safely.", mayStillBeRunning: true, inner: ex); }
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
            if (deadline.IsCancellationRequested) throw new DocumentEngineException($"LibreOffice export exceeded its {timeout.TotalSeconds:N1}-second time limit.");
            if (failure is DocumentEngineException) throw;
            throw new DocumentEngineException("LibreOffice export failed while reading process output.", inner: failure);
        }
    }

    private static async Task<string> DrainAsync(StreamReader reader, CancellationToken token)
    {
        var saved = new StringBuilder();
        var buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer, token)) != 0)
        {
            var keep = Math.Min(count, 4096 - saved.Length);
            if (keep > 0) saved.Append(buffer, 0, keep);
        }
        return saved.ToString().Trim();
    }
}
