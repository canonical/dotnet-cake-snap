///////////////////////////////////////////////////////////////////////////////
// build.cake — Complex script to test breaking points of a Cake snap
//
// Goal: exercise as many Cake aliases, tools and features as possible in order
// to detect what might be missing in the packaged environment (snap):
//   - Arguments and environment variables
//   - Setup / Teardown (global and per-task)
//   - IO: create/copy/move/delete files and directories
//   - Globbing (file patterns)
//   - External processes (StartProcess) and tools (dotnet, git, etc.)
//   - Full .NET CLI (restore, build, test, publish, pack)
//   - Compression (Zip / Unzip) and hashing
//   - Conditional criteria, error handling, retries
//   - Task chaining and dependencies
//   - File read/write, JSON, text transformations
//   - NuGet addin download/resolution (validates the snap behaves like a
//     normally installed Cake)
///////////////////////////////////////////////////////////////////////////////

// NOTE: This addin is kept ON PURPOSE to validate that the snap can download
// and resolve NuGet addins exactly like a normally installed Cake. It emits a
// cosmetic "referencing an older version of Cake.Core" warning because no
// published version of Cake.FileHelpers targets Cake.Core 6.x yet; that warning
// is harmless and does not affect functionality.
#addin nuget:?package=Cake.FileHelpers&version=6.1.1

using System.Linq;
using System.Text;
using System.Collections.Generic;

///////////////////////////////////////////////////////////////////////////////
// ARGUMENTS
///////////////////////////////////////////////////////////////////////////////

var target        = Argument("target", "Default");
var configuration = Argument("configuration", "Release");
var runtime       = Argument("runtime", (string)null); // e.g. linux-x64
var verbosityArg  = Argument("verbosity", "Normal");
var skipTests     = Argument("skipTests", false);

///////////////////////////////////////////////////////////////////////////////
// GLOBAL VARIABLES
///////////////////////////////////////////////////////////////////////////////

var projectFile     = File("./cake-test.csproj");
var solutionRoot    = Directory("./");
var artifactsDir    = Directory("./artifacts");
var publishDir      = artifactsDir + Directory("publish");
var packagesDir     = artifactsDir + Directory("packages");
var reportsDir      = artifactsDir + Directory("reports");
var scratchDir      = Directory("./_scratch");
var outputBinDir    = Directory($"./bin/{configuration}/net10.0");
var zipFile         = artifactsDir + File("cake-test-artifact.zip");

DirectoryPath[] cleanupDirs = { artifactsDir, scratchDir };

///////////////////////////////////////////////////////////////////////////////
// SETUP / TEARDOWN
///////////////////////////////////////////////////////////////////////////////

Setup(context =>
{
    Information("─────────────────────────────────────────────");
    Information("  Cake execution started");
    Information("─────────────────────────────────────────────");
    Information("Target        : {0}", target);
    Information("Configuration : {0}", configuration);
    Information("Runtime       : {0}", runtime ?? "(portable)");
    Information("SkipTests     : {0}", skipTests);
    Information("Working dir   : {0}", context.Environment.WorkingDirectory);
    Information("Platform      : {0} ({1})",
        context.Environment.Platform.Family,
        context.Environment.Platform.Is64Bit ? "x64" : "x86");
    Information("Runtime CLR   : {0}", context.Environment.Runtime.IsCoreClr ? "CoreCLR" : "Other");
    Information(".NET Version  : {0}", context.Environment.Runtime.BuiltFramework);

    // Environment variables relevant to a snap
    var home    = EnvironmentVariable("HOME") ?? "(no HOME)";
    var pathVar = EnvironmentVariable("PATH") ?? "(no PATH)";
    var tmp     = EnvironmentVariable("TMPDIR") ?? EnvironmentVariable("TMP") ?? "/tmp";
    Information("HOME          : {0}", home);
    Information("TMPDIR        : {0}", tmp);
    Information("PATH length   : {0} chars", pathVar.Length);
});

Teardown(context =>
{
    Information("─────────────────────────────────────────────");
    Information("  Execution finished. Cleaning scratch...");
    Information("─────────────────────────────────────────────");
    if (DirectoryExists(scratchDir))
    {
        DeleteDirectory(scratchDir, new DeleteDirectorySettings {
            Recursive = true, Force = true
        });
    }
});

TaskSetup(info =>
{
    Information(">>> Starting task: {0}", info.Task.Name);
});

TaskTeardown(info =>
{
    Information("<<< Task completed: {0} ({1} ms)",
        info.Task.Name, info.Duration.TotalMilliseconds);
});

///////////////////////////////////////////////////////////////////////////////
// TASKS
///////////////////////////////////////////////////////////////////////////////

Task("Clean")
    .Does(() =>
{
    Information("Cleaning output directories...");
    foreach (var dir in cleanupDirs)
    {
        if (DirectoryExists(dir))
        {
            Information("  Deleting {0}", dir);
            DeleteDirectory(dir, new DeleteDirectorySettings {
                Recursive = true, Force = true
            });
        }
    }

    // Cleanup via globbing (bin/obj)
    var binObj = GetDirectories("./**/bin") + GetDirectories("./**/obj");
    Information("  Found {0} bin/obj directories", binObj.Count);
    foreach (var dir in binObj)
    {
        Information("  Deleting {0}", dir);
        DeleteDirectory(dir, new DeleteDirectorySettings {
            Recursive = true, Force = true
        });
    }

    // Recreate structure
    EnsureDirectoryExists(artifactsDir);
    EnsureDirectoryExists(publishDir);
    EnsureDirectoryExists(packagesDir);
    EnsureDirectoryExists(reportsDir);
    EnsureDirectoryExists(scratchDir);
});

Task("Show-Environment")
    .Does(() =>
{
    // Run external processes: availability check inside the snap
    void TryTool(string tool, string args)
    {
        try
        {
            var exit = StartProcess(tool, new ProcessSettings {
                Arguments = args,
                RedirectStandardOutput = true
            }, out var output);
            var lines = output.ToList();
            Information("[{0} {1}] exit={2} :: {3}",
                tool, args, exit,
                lines.Count > 0 ? lines.First() : "(no output)");
        }
        catch (Exception ex)
        {
            Warning("[{0}] NOT available in the snap: {1}", tool, ex.Message);
        }
    }

    TryTool("dotnet", "--version");
    TryTool("dotnet", "--list-sdks");
    TryTool("dotnet", "--list-runtimes");
    TryTool("git", "--version");
    TryTool("uname", "-a");
    TryTool("bash", "--version");
    TryTool("ls", "-la");
});

Task("Restore")
    .IsDependentOn("Clean")
    .Does(() =>
{
    DotNetRestore(projectFile, new DotNetRestoreSettings {
        Verbosity = DotNetVerbosity.Minimal
    });
});

Task("Build")
    .IsDependentOn("Restore")
    .Does(() =>
{
    var settings = new DotNetBuildSettings {
        Configuration    = configuration,
        NoRestore        = true,
        Verbosity        = DotNetVerbosity.Minimal,
        MSBuildSettings  = new DotNetMSBuildSettings()
            .WithProperty("ContinuousIntegrationBuild", "true")
            .SetVersion("1.0.0")
    };
    if (!string.IsNullOrEmpty(runtime))
    {
        settings.Runtime = runtime;
    }
    DotNetBuild(projectFile, settings);
});

Task("Test")
    .IsDependentOn("Build")
    .WithCriteria(() => !skipTests, "Tests skipped via --skipTests")
    .Does(() =>
{
    // Even if there is no test project, we check that the alias works.
    // The failure is ignored so it does not break the snap test pipeline.
    try
    {
        DotNetTest(projectFile, new DotNetTestSettings {
            Configuration = configuration,
            NoBuild       = true,
            Loggers       = new[] { "trx" },
            ResultsDirectory = reportsDir
        });
    }
    catch (Exception ex)
    {
        Warning("DotNetTest failed (expected if there are no tests): {0}", ex.Message);
    }
});

Task("Publish")
    .IsDependentOn("Build")
    .Does(() =>
{
    var settings = new DotNetPublishSettings {
        Configuration = configuration,
        OutputDirectory = publishDir,
        NoRestore = true,
        Verbosity = DotNetVerbosity.Minimal
    };
    if (!string.IsNullOrEmpty(runtime))
    {
        settings.Runtime = runtime;
        settings.SelfContained = true;
    }
    DotNetPublish(projectFile, settings);

    var published = GetFiles(publishDir.Path + "/**/*");
    Information("Published files: {0}", published.Count);
    foreach (var f in published.Take(20))
    {
        Information("  - {0}", f.GetFilename());
    }
});

Task("Generate-Files")
    .IsDependentOn("Clean")
    .Does(() =>
{
    // File write and read (native .NET IO)
    var textFile = scratchDir + File("notes.txt");
    var jsonFile = scratchDir + File("metadata.json");

    var sb = new StringBuilder();
    for (int i = 1; i <= 10; i++)
    {
        sb.AppendLine($"Line {i}: test content with accents áéíóú ñ €");
    }
    System.IO.File.WriteAllText(textFile.Path.FullPath, sb.ToString(), Encoding.UTF8);
    Information("Text file created: {0}", textFile);

    // Read the lines back
    var readLines = System.IO.File.ReadAllLines(textFile.Path.FullPath);
    Information("Lines read: {0}", readLines.Length);

    // Text replacement using native .NET IO
    var replaced = System.IO.File.ReadAllText(textFile.Path.FullPath).Replace("test", "PROD");
    System.IO.File.WriteAllText(textFile.Path.FullPath, replaced, Encoding.UTF8);
    Information("Contains 'PROD': {0}", replaced.Contains("PROD"));

    // Hand-crafted JSON
    var json = "{\n  \"name\": \"cake-test\",\n  \"generated\": \""
        + DateTime.UtcNow.ToString("o") + "\",\n  \"lines\": "
        + readLines.Length + "\n}";
    System.IO.File.WriteAllText(jsonFile.Path.FullPath, json);
    Information("JSON created: {0}", jsonFile);
});

Task("Verify-Addin")
    .IsDependentOn("Generate-Files")
    .Does(() =>
{
    // Purpose: prove that the NuGet addin (Cake.FileHelpers) was actually
    // downloaded, resolved and loaded — i.e. the snap behaves like a normally
    // installed Cake. This task USES the addin's aliases directly; if the addin
    // did not resolve, the script would fail to compile before reaching here.
    var addinFile = scratchDir + File("addin-check.txt");

    FileWriteText(addinFile.Path.FullPath, "hello addin world");
    ReplaceTextInFiles(addinFile.Path.FullPath, "world", "SNAP");

    var content = FileReadText(addinFile.Path.FullPath);
    Information("Addin FileReadText result : '{0}'", content);

    var containsSnap = FileReadLines(addinFile.Path.FullPath)
        .Any(line => line.Contains("SNAP"));

    if (containsSnap)
    {
        Information("Cake.FileHelpers addin resolved and executed correctly ✔");
    }
    else
    {
        throw new Exception("Addin loaded but produced an unexpected result.");
    }
});

Task("File-Operations")
    .IsDependentOn("Generate-Files")
    .Does(() =>
{
    var srcDir  = scratchDir + Directory("src");
    var copyDir = scratchDir + Directory("copy");
    EnsureDirectoryExists(srcDir);
    EnsureDirectoryExists(copyDir);

    // Create several files for globbing
    for (int i = 0; i < 5; i++)
    {
        var f = srcDir + File($"file_{i}.dat");
        System.IO.File.WriteAllText(f.Path.FullPath, $"data-{i}");
    }

    // Globbing: find and copy
    var datFiles = GetFiles(srcDir.Path + "/*.dat");
    Information(".dat files found: {0}", datFiles.Count);
    CopyFiles(datFiles, copyDir);

    // Copy an entire directory
    var mirrorDir = scratchDir + Directory("mirror");
    CopyDirectory(srcDir, mirrorDir);
    Information("Directory mirrored to {0}", mirrorDir);

    // Move a file
    var toMove = copyDir + File("file_0.dat");
    var moved  = copyDir + File("file_0.moved");
    MoveFile(toMove, moved);
    Information("File moved: {0} exists={1}", moved, FileExists(moved));

    // Delete by pattern
    DeleteFiles(mirrorDir.Path + "/*.dat");
    Information("Mirror files deleted.");
});

Task("Hash-And-Compress")
    .IsDependentOn("File-Operations")
    .Does(() =>
{
    // Compress the build output directory if it exists
    if (DirectoryExists(outputBinDir))
    {
        Zip(outputBinDir, zipFile);
        Information("Zip created: {0}", zipFile);

        // Unzip into scratch to verify
        var unzipDir = scratchDir + Directory("unzipped");
        Unzip(zipFile, unzipDir);
        var extracted = GetFiles(unzipDir.Path + "/**/*");
        Information("Files extracted from zip: {0}", extracted.Count);
    }
    else
    {
        Warning("{0} does not exist; skipping compression.", outputBinDir);
    }

    // SHA256 hashing of a file using System.Security.Cryptography
    var hashTarget = scratchDir + File("src/file_1.dat");
    if (FileExists(hashTarget))
    {
        using (var sha = System.Security.Cryptography.SHA256.Create())
        using (var stream = System.IO.File.OpenRead(hashTarget.Path.FullPath))
        {
            var hash = sha.ComputeHash(stream);
            var hex = string.Concat(hash.Select(b => b.ToString("x2")));
            Information("SHA256({0}) = {1}", hashTarget.Path.GetFilename(), hex);
        }
    }
});

Task("Run-App")
    .IsDependentOn("Build")
    .Does(() =>
{
    var exePath = outputBinDir + File("cake-test");

    // Local helper to run the binary and capture output + exit code
    int RunApp(string args)
    {
        var exit = StartProcess(exePath.Path.FullPath, new ProcessSettings {
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError  = true
        }, out var stdout, out var stderr);

        foreach (var line in stdout) { Information("  [out] {0}", line); }
        foreach (var line in stderr) { Warning("  [err] {0}", line); }
        return exit;
    }

    if (!FileExists(exePath))
    {
        throw new Exception($"Expected binary not found at {exePath}. " +
                            "Build may have failed or produced output elsewhere.");
    }

    // 1) Normal run: runtime diagnostics inside the snap
    Information("── Running app diagnostics ──");
    var exitDiag = RunApp(null);
    if (exitDiag != 0)
    {
        throw new Exception($"The diagnostic app failed with exit code {exitDiag}");
    }

    // 2) Argument passing test (--echo)
    Information("── Running argument test (--echo) ──");
    RunApp("--echo \"Hello from Cake inside the snap\"");

    // 3) File system test within confinement (--fs)
    Information("── Running file system test (--fs) ──");
    var exitFs = RunApp("--fs");
    if (exitFs != 0)
    {
        Warning("The FS test returned exit code {0} (possible snap confinement).", exitFs);
    }

    // 4) Verify that error exit codes propagate correctly (--fail => 42)
    Information("── Verifying exit code propagation (--fail) ──");
    var exitFail = RunApp("--fail");
    if (exitFail == 42)
    {
        Information("Exit code propagated correctly: {0} ✔", exitFail);
    }
    else
    {
        throw new Exception($"Expected exit code 42 but got {exitFail}");
    }
});

Task("Error-Handling")
    .Does(() =>
{
    // Error handling with DeferOnError
    Information("Testing error handling...");
})
.OnError(exception =>
{
    Warning("An error was caught in the task: {0}", exception.Message);
})
.DeferOnError();

Task("Retry-Logic")
    .Does(() =>
{
    int attempts = 0;
    // Simple manual retry
    for (int i = 0; i < 3; i++)
    {
        attempts++;
        try
        {
            if (attempts < 2)
                throw new Exception("Simulated failure, retrying...");
            Information("Success after {0} attempt(s)", attempts);
            break;
        }
        catch (Exception ex)
        {
            Warning("Attempt {0} failed: {1}", attempts, ex.Message);
        }
    }
});

// Tasks to test dependencies and chained execution
Task("Parallel-A").Does(() => { Information("Task A running"); System.Threading.Thread.Sleep(100); });
Task("Parallel-B").Does(() => { Information("Task B running"); System.Threading.Thread.Sleep(100); });
Task("Parallel-C").Does(() => { Information("Task C running"); System.Threading.Thread.Sleep(100); });

Task("Fan-Out")
    .IsDependentOn("Parallel-A")
    .IsDependentOn("Parallel-B")
    .IsDependentOn("Parallel-C")
    .Does(() =>
{
    Information("All fan-out tasks completed.");
});

///////////////////////////////////////////////////////////////////////////////
// AGGREGATOR TASKS
///////////////////////////////////////////////////////////////////////////////

Task("CreateArtifact")
    .IsDependentOn("Publish")
    .IsDependentOn("Hash-And-Compress")
    .Does(() =>
{
    Information("Artifact created in {0}", artifactsDir);
    var allArtifacts = GetFiles(artifactsDir.Path + "/**/*");
    Information("Total files in artifacts: {0}", allArtifacts.Count);
});

Task("Default")
    .IsDependentOn("Show-Environment")
    .IsDependentOn("Build")
    .IsDependentOn("Test")
    .IsDependentOn("Verify-Addin")
    .IsDependentOn("File-Operations")
    .IsDependentOn("Hash-And-Compress")
    .IsDependentOn("Run-App")
    .IsDependentOn("Error-Handling")
    .IsDependentOn("Retry-Logic")
    .IsDependentOn("Fan-Out")
    .IsDependentOn("CreateArtifact")
    .Does(() =>
{
    Information("╔═══════════════════════════════════════════╗");
    Information("║  FULL pipeline executed successfully       ║");
    Information("╚═══════════════════════════════════════════╝");
});

///////////////////////////////////////////////////////////////////////////////
// EXECUTION
///////////////////////////////////////////////////////////////////////////////

RunTarget(target);