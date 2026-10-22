using System.Text.Json;

// A deterministic test process; it is never shipped with FilePilot or used for real rendering.
var root = AppContext.BaseDirectory;
File.WriteAllText(Path.Combine(root, "started.txt"), Environment.ProcessId.ToString());
File.WriteAllText(Path.Combine(root, "arguments.json"), JsonSerializer.Serialize(args));
var profileArgument = args.FirstOrDefault(value => value.StartsWith("-env:UserInstallation=", StringComparison.Ordinal));
if (profileArgument is not null)
    File.Copy(Path.Combine(new Uri(profileArgument["-env:UserInstallation=".Length..]).LocalPath, "registrymodifications.xcu"), Path.Combine(root, "requested-profile.xcu"), overwrite: true);
var mode = File.ReadAllText(Path.Combine(root, "mode.txt")).Trim();
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
