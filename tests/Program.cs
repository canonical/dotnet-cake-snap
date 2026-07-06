using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

// ─────────────────────────────────────────────────────────────────────────────
// cake-test — diagnostic app
//
// This app is NOT a "Hello World": its purpose is to exercise the runtime inside
// the snap in order to detect breaking points when Cake compiles and runs it.
//
// Usage:
//   cake-test                 → prints diagnostics and exits with code 0
//   cake-test --fail          → exits with code 42 (tests exit code handling)
//   cake-test --echo <text>   → prints <text> (tests argument passing)
//   cake-test --fs            → tests write/read on the file system
// ─────────────────────────────────────────────────────────────────────────────

Console.OutputEncoding = Encoding.UTF8;

var arguments = args.ToList();

// --- Handle arguments that force specific behaviors ---

if (arguments.Contains("--fail"))
{
    Console.Error.WriteLine("[cake-test] Exiting intentionally with code 42.");
    return 42;
}

if (arguments.Contains("--echo"))
{
    var index = arguments.IndexOf("--echo");
    var message = index + 1 < arguments.Count ? arguments[index + 1] : "(no message)";
    Console.WriteLine($"[echo] {message}");
    return 0;
}

// --- Environment diagnostics (always printed) ---

Console.WriteLine("╔══════════════════════════════════════════════════╗");
Console.WriteLine("║  cake-test — runtime diagnostics inside the snap ║");
Console.WriteLine("╚══════════════════════════════════════════════════╝");

Console.WriteLine($".NET version      : {Environment.Version}");
Console.WriteLine($"Framework         : {RuntimeInformation.FrameworkDescription}");
Console.WriteLine($"OS                : {RuntimeInformation.OSDescription}");
Console.WriteLine($"Architecture      : {RuntimeInformation.OSArchitecture} / process {RuntimeInformation.ProcessArchitecture}");
Console.WriteLine($"Machine name      : {Environment.MachineName}");
Console.WriteLine($"User name         : {Environment.UserName}");
Console.WriteLine($"Processor count   : {Environment.ProcessorCount}");
Console.WriteLine($"Culture           : {CultureInfo.CurrentCulture.Name} / UI {CultureInfo.CurrentUICulture.Name}");
Console.WriteLine($"Working directory : {Environment.CurrentDirectory}");
Console.WriteLine($"Base directory    : {AppContext.BaseDirectory}");
Console.WriteLine($"64-bit process    : {Environment.Is64BitProcess}");

// --- Environment variables relevant to snap confinement ---

Console.WriteLine();
Console.WriteLine("── Environment variables (relevant to snap) ──");
foreach (var name in new[] { "HOME", "USER", "PATH", "TMPDIR", "TMP",
                             "SNAP", "SNAP_NAME", "SNAP_USER_DATA", "SNAP_USER_COMMON",
                             "DOTNET_ROOT", "DOTNET_CLI_HOME", "LANG", "LC_ALL" })
{
    var value = Environment.GetEnvironmentVariable(name);
    if (value is null)
    {
        Console.WriteLine($"  {name,-18}: (not set)");
    }
    else
    {
        // Truncate PATH so it does not flood the output
        var display = name == "PATH" && value.Length > 80
            ? value.Substring(0, 80) + $"... ({value.Length} chars)"
            : value;
        Console.WriteLine($"  {name,-18}: {display}");
    }
}

// --- Optional file system test ---

if (arguments.Contains("--fs"))
{
    Console.WriteLine();
    Console.WriteLine("── File system test ──");
    try
    {
        var tempDir = Path.GetTempPath();
        var probe = Path.Combine(tempDir, $"cake-test-probe-{Guid.NewGuid():N}.txt");
        var content = $"probe written at {DateTime.UtcNow:o}";

        File.WriteAllText(probe, content, Encoding.UTF8);
        var readBack = File.ReadAllText(probe);
        var ok = readBack == content;

        Console.WriteLine($"  TempPath        : {tempDir}");
        Console.WriteLine($"  File written    : {probe}");
        Console.WriteLine($"  Read OK         : {ok}");

        File.Delete(probe);
        Console.WriteLine($"  File deleted    : {!File.Exists(probe)}");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"  FS FAILURE inside the snap!: {ex.GetType().Name}: {ex.Message}");
        return 13;
    }
}

Console.WriteLine();
Console.WriteLine("[cake-test] Diagnostics completed successfully.");
return 0;
