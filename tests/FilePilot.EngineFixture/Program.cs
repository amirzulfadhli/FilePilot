using System.Text.Json;

// A deterministic test process; it is never shipped with FilePilot or used for real rendering.
using var control = JsonDocument.Parse(File.ReadAllText(Path.Combine(Environment.CurrentDirectory, "fixture-control.json")));
var root = control.RootElement.GetProperty("Root").GetString()!;
var mode = control.RootElement.GetProperty("Mode").GetString()!;
File.WriteAllText(Path.Combine(root, "arguments.json"), JsonSerializer.Serialize(args));
var profileArgument = args.FirstOrDefault(value => value.StartsWith("-env:UserInstallation=", StringComparison.Ordinal));
if (profileArgument is not null)
    File.Copy(Path.Combine(new Uri(profileArgument["-env:UserInstallation=".Length..]).LocalPath, "user", "registrymodifications.xcu"), Path.Combine(root, "requested-profile.xcu"), overwrite: true);
// Publish the PID only after the complete marker is readable, before entering the selected workload.
var marker = Path.Combine(root, "started-" + Guid.NewGuid().ToString("N") + ".tmp");
File.WriteAllText(marker, Environment.ProcessId.ToString());
File.Move(marker, Path.Combine(root, "started.txt"), overwrite: true);
if (mode == "missing-runtime") { Environment.ExitCode = unchecked((int)0xC0000135); return; }
if (mode == "hang") { await Task.Delay(TimeSpan.FromMinutes(10)); return; }
if (mode is "fail" or "noisy")
{
    await Console.Out.WriteAsync(new string('o', 100000));
    await Console.Error.WriteAsync(new string('e', 100000));
    if (mode == "fail") { Environment.ExitCode = 7; return; }
}
if (mode == "no-output") return;
var index = Array.IndexOf(args, "--outdir");
if (index < 0 || index + 1 >= args.Length) { Environment.ExitCode = 9; return; }
File.WriteAllText(Path.Combine(args[index + 1], "input.pdf"), "%PDF-1.7\nSynthetic adapter fixture, not a rendered document.\n%%EOF\n");
