using Codex.Core;
using Codex.Types;
using Codex.Emit.CSharp;

namespace Codex.Cli;

public static partial class Program
{
    static int RunBuild(string[] args)
    {
        if (args.Length == 0)
        {
            if (File.Exists("codex.project.json"))
            {
                return RunBuildProject(".", null, false, null);
            }
            Console.Error.WriteLine("Usage: codex build <file|dir>  Compile a Codex file or project");
            Console.WriteLine("                    --target <t>        Target backend (cs|js|rust|py|cpp|go|java|ada|fortran|cobol|babbage)");
            Console.WriteLine("                    --targets <t1,t2>   Emit to multiple backends in parallel");
            Console.WriteLine("                    --incremental, -i   Skip unchanged files (uses .codex-build/manifest.json)");
            Console.WriteLine("                    --view <name>       Compile from a repository view");
            Console.WriteLine("                    --verbose, -v       Show resolved type signatures");
            Console.WriteLine("                    --dump-frames       Dump per-function stack frame sizes (x86-64 only)");
            Console.WriteLine("                    --diagnostic        Emit serial diagnostics (allocation, TCO, function trace)");
            Console.WriteLine("                    --dump=<phases>     Comma-separated phases to dump: parsed,desugared,resolved,ir,codegen,all");
            Console.WriteLine("                    --dump-dir <path>   Directory for phase dumps (default: ./dumps)");
            Console.WriteLine("                    --verify-invariants Run compiler-internal invariant checks between phases (also: CODEX_VERIFY_INVARIANTS=1)");
            Console.WriteLine("                    --emit-phase-timings Write 'PHASE <name> <ms>' lines to stderr per pipeline phase");
            Console.WriteLine("                    --output-file <p>   Single-file build only: write output to exactly <p> (overrides --output-dir)");
            return 1;
        }

        string filePath = args[0];
        string? targetOverride = null;
        string[]? multiTargets = null;
        string? viewName = null;
        string[]? capNames = null;
        string? outputDirOverride = null;
        string? outputFileOverride = null;
        string? dumpPhases = null;
        string? dumpDir = null;
        bool incremental = false;
        bool verbose = false;
        bool dumpFrames = false;
        bool diagnostic = false;
        bool verifyInvariants = Environment.GetEnvironmentVariable("CODEX_VERIFY_INVARIANTS") == "1";
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "--target" && i + 1 < args.Length)
            {
                targetOverride = args[++i].ToLowerInvariant();
            }
            else if (args[i] == "--targets" && i + 1 < args.Length)
            {
                multiTargets = args[++i].ToLowerInvariant().Split(',', StringSplitOptions.RemoveEmptyEntries);
            }
            else if (args[i] == "--view" && i + 1 < args.Length)
            {
                viewName = args[++i];
            }
            else if (args[i] == "--capabilities" && i + 1 < args.Length)
            {
                capNames = args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }
            else if (args[i] == "--output-dir" && i + 1 < args.Length)
            {
                outputDirOverride = args[++i];
            }
            else if (args[i] == "--output-file" && i + 1 < args.Length)
            {
                outputFileOverride = args[++i];
            }
            else if (args[i] == "--incremental" || args[i] == "-i")
            {
                incremental = true;
            }
            else if (args[i] == "--verbose" || args[i] == "-v")
            {
                verbose = true;
            }
            else if (args[i] == "--dump-frames")
            {
                dumpFrames = true;
            }
            else if (args[i] == "--diagnostic")
            {
                diagnostic = true;
            }
            else if (args[i].StartsWith("--dump=", StringComparison.Ordinal))
            {
                dumpPhases = args[i]["--dump=".Length..];
            }
            else if (args[i] == "--dump" && i + 1 < args.Length)
            {
                dumpPhases = args[++i];
            }
            else if (args[i].StartsWith("--dump-dir=", StringComparison.Ordinal))
            {
                dumpDir = args[i]["--dump-dir=".Length..];
            }
            else if (args[i] == "--dump-dir" && i + 1 < args.Length)
            {
                dumpDir = args[++i];
            }
            else if (args[i] == "--verify-invariants")
            {
                verifyInvariants = true;
            }
            else if (args[i] == "--emit-phase-timings")
            {
                s_emitPhaseTimings = true;
            }
            else if (args[i] == "--exit-mode" && i + 1 < args.Length)
            {
                string mode = args[++i].ToLowerInvariant();
                switch (mode)
                {
                    case "repl": s_exitMode = Emit.X86_64.X86_64ExitMode.Repl; break;
                    case "qemu-exit": s_exitMode = Emit.X86_64.X86_64ExitMode.QemuExit; break;
                    case "acpi-s5":
                        Console.Error.WriteLine("error: --exit-mode=acpi-s5 not implemented (ACPI S5 shutdown requires a DSDT walker; tracked for post-MM4)");
                        return 1;
                    default:
                        Console.Error.WriteLine($"error: unknown --exit-mode value '{mode}' (expected: repl | qemu-exit | acpi-s5)");
                        return 1;
                }
            }
            else if (args[i].StartsWith("--exit-mode=", StringComparison.Ordinal))
            {
                string mode = args[i]["--exit-mode=".Length..].ToLowerInvariant();
                switch (mode)
                {
                    case "repl": s_exitMode = Emit.X86_64.X86_64ExitMode.Repl; break;
                    case "qemu-exit": s_exitMode = Emit.X86_64.X86_64ExitMode.QemuExit; break;
                    case "acpi-s5":
                        Console.Error.WriteLine("error: --exit-mode=acpi-s5 not implemented (ACPI S5 shutdown requires a DSDT walker; tracked for post-MM4)");
                        return 1;
                    default:
                        Console.Error.WriteLine($"error: unknown --exit-mode value '{mode}' (expected: repl | qemu-exit | acpi-s5)");
                        return 1;
                }
            }
            else if (args[i] == "--watchdog" && i + 1 < args.Length)
            {
                string mode = args[++i].ToLowerInvariant();
                switch (mode)
                {
                    case "progress": s_watchdogMode = Emit.X86_64.X86_64WatchdogMode.Progress; break;
                    case "pet": s_watchdogMode = Emit.X86_64.X86_64WatchdogMode.Pet; break;
                    default:
                        Console.Error.WriteLine($"error: unknown --watchdog value '{mode}' (expected: progress | pet)");
                        return 1;
                }
            }
            else if (args[i].StartsWith("--watchdog=", StringComparison.Ordinal))
            {
                string mode = args[i]["--watchdog=".Length..].ToLowerInvariant();
                switch (mode)
                {
                    case "progress": s_watchdogMode = Emit.X86_64.X86_64WatchdogMode.Progress; break;
                    case "pet": s_watchdogMode = Emit.X86_64.X86_64WatchdogMode.Pet; break;
                    default:
                        Console.Error.WriteLine($"error: unknown --watchdog value '{mode}' (expected: progress | pet)");
                        return 1;
                }
            }
        }
        s_verbose = verbose;
        s_dumpFrames = dumpFrames;
        s_diagnostic = diagnostic;
        s_verifyInvariants = verifyInvariants;

        PhaseDumpSink? dumpSink = PhaseDumpSink.TryCreate(dumpPhases, dumpDir, out string? dumpError);
        if (dumpError is not null)
        {
            Console.Error.WriteLine($"error: {dumpError}");
            return 1;
        }
        s_dumpSink = dumpSink;

        Set<string>? grantedCapabilities = null;
        if (capNames is not null)
        {
            grantedCapabilities = Set<string>.s_empty;
            foreach (string cap in capNames)
                grantedCapabilities = grantedCapabilities.Add(cap);
        }

        // Also support: codex build --view <name> (without a file arg)
        if (filePath == "--view" && args.Length >= 2)
        {
            viewName = args[1];
            // Re-parse remaining args for --target
            for (int i = 2; i < args.Length; i++)
            {
                if (args[i] == "--target" && i + 1 < args.Length)
                {
                    targetOverride = args[++i].ToLowerInvariant();
                }
            }
        }

        try
        {
            if (viewName is not null)
            {
                return RunBuildView(viewName, targetOverride ?? "cs", grantedCapabilities);
            }

            if (filePath == "." || filePath == "./" || filePath == ".\\"
                || (Directory.Exists(filePath) && File.Exists(Path.Combine(filePath, "codex.project.json"))))
            {
                return RunBuildProject(filePath, targetOverride, incremental, multiTargets, outputDirOverride);
            }

            if (Directory.Exists(filePath))
            {
                return RunBuildDirectory(filePath, targetOverride ?? "cs");
            }

            return RunBuildFile(filePath, targetOverride ?? "cs", grantedCapabilities, outputDirOverride, outputFileOverride);
        }
        catch (InvariantViolationException ex)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine();
            Console.Error.WriteLine("This is a compiler bug. Please report it.");
            return 2;
        }
    }

    static int RunBuildProject(string directory, string? targetOverride, bool incremental, string[]? multiTargets, string? outputDirOverride = null)
    {
        CodexProject? project = LoadProjectFile(directory);
        if (project is null)
        {
            Console.Error.WriteLine($"No codex.project.json found in {Path.GetFullPath(directory)}");
            return 1;
        }

        Console.WriteLine($"Building project: {project.Name} v{project.Version}");

        string[] files = ResolveProjectSources(directory, project);
        if (files.Length == 0)
        {
            Console.Error.WriteLine("No .codex source files matched by project sources.");
            return 1;
        }

        // Resolve project dependencies (packages + path deps + prelude)
        string fullDir = Path.GetFullPath(directory);
        DiagnosticBag depDiag = new();
        PackageResolver packageResolver = new(fullDir, depDiag);
        List<Codex.Semantics.IChapterLoader> depLoaders = packageResolver.ResolveAll(project);
        IReadOnlyList<Codex.Semantics.IChapterLoader>? extraLoaders =
            depLoaders.Count > 0 ? depLoaders : null;

        if (depLoaders.Count > 0)
            Console.WriteLine($"  Dependencies: {depLoaders.Count} loader(s)");

        string outputDir = outputDirOverride is not null
            ? Path.GetFullPath(outputDirOverride)
            : Path.GetFullPath(Path.Combine(directory, project.Output));
        if (!Directory.Exists(outputDir))
            Directory.CreateDirectory(outputDir);

        if (multiTargets is not null && multiTargets.Length > 1)
        {
            Console.WriteLine($"  Sources: {files.Length} file(s)");
            Console.WriteLine($"  Targets: {string.Join(", ", multiTargets)}");
            return RunParallelMultiTargetBuild(directory, multiTargets, files, outputDir, project.Name, extraLoaders);
        }

        string[]? projectTargets = project.Targets.Length > 1 ? project.Targets : null;
        if (projectTargets is not null && targetOverride is null)
        {
            Console.WriteLine($"  Sources: {files.Length} file(s)");
            Console.WriteLine($"  Targets: {string.Join(", ", projectTargets)}");
            return RunParallelMultiTargetBuild(directory, projectTargets, files, outputDir, project.Name, extraLoaders);
        }

        string target = targetOverride ?? project.Target;
        Console.WriteLine($"  Sources: {files.Length} file(s)");
        Console.WriteLine($"  Target:  {target}");

        if (incremental && !IsAssemblyTarget(target))
        {
            string outputPath = Path.Combine(outputDir, project.Name + CreateEmitter(target).FileExtension);
            return RunIncrementalBuild(directory, target, files, outputPath, extraLoaders);
        }

        PhaseTimer.Reset();
        IRCompilationResult? irResult = CompileMultipleToIR(files, project.Name, extraLoaders, codexRoot: fullDir, dumpSink: s_dumpSink, liftLambdas: TargetNeedsLambdaLifting(target));
        if (irResult is null) return 1;

        if (IsAssemblyTarget(target))
        {
            int rc = EmitAssembly(irResult, outputDir, project.Name, target);
            PhaseTimer.Flush();
            return rc;
        }

        Codex.Emit.ICodeEmitter emitter = CreateEmitter(target);
        string output = PhaseTimer.Time("emit", () => emitter.Emit(irResult.Chapter));
        if (CheckEmitFuel(emitter))
        {
            return 1;
        }
        string fullOutputPath = Path.Combine(outputDir, project.Name + emitter.FileExtension);
        File.WriteAllText(fullOutputPath, output);
        s_dumpSink?.DumpCodegen(project.Name, emitter.FileExtension, output);

        Console.WriteLine($"✓ Compiled to {fullOutputPath} ({target})");
        PrintTypes(irResult);
        PhaseTimer.Flush();
        return 0;
    }
    static int RunBuildDirectory(string directory, string target)
    {
        // Quire semantics: walk root + one level of subdirectory. Nested
        // subdirectories below depth 1 are not part of the codex.
        List<string> fileList = [];
        fileList.AddRange(Directory.GetFiles(directory, "*.codex", SearchOption.TopDirectoryOnly));
        foreach (string subDir in Directory.GetDirectories(directory))
            fileList.AddRange(Directory.GetFiles(subDir, "*.codex", SearchOption.TopDirectoryOnly));
        string[] files = fileList.ToArray();
        if (files.Length == 0)
        {
            Console.Error.WriteLine($"No .codex files found in {directory}");
            return 1;
        }
        Array.Sort(files, StringComparer.Ordinal);
        string chapterName = Path.GetFileName(Path.GetFullPath(directory));

        PhaseTimer.Reset();
        IRCompilationResult? irResult = CompileMultipleToIR(files, chapterName, codexRoot: Path.GetFullPath(directory), dumpSink: s_dumpSink, liftLambdas: TargetNeedsLambdaLifting(target));
        if (irResult is null) return 1;

        if (IsAssemblyTarget(target))
        {
            int rc = EmitAssembly(irResult, directory, chapterName, target);
            PhaseTimer.Flush();
            return rc;
        }

        Codex.Emit.ICodeEmitter emitter = CreateEmitter(target);
        string output = PhaseTimer.Time("emit", () => emitter.Emit(irResult.Chapter));
        if (CheckEmitFuel(emitter))
        {
            return 1;
        }
        string outputPath = Path.Combine(directory, "output" + emitter.FileExtension);
        File.WriteAllText(outputPath, output);
        s_dumpSink?.DumpCodegen(chapterName, emitter.FileExtension, output);

        Console.WriteLine($"✓ Compiled to {outputPath} ({target})");
        PrintTypes(irResult);
        PhaseTimer.Flush();
        return 0;
    }

    static int RunBuildFile(string filePath, string target, Set<string>? grantedCapabilities = null, string? outputDirOverride = null, string? outputFileOverride = null)
    {
        IRCompilationResult? irResult = CompileToIR(filePath, grantedCapabilities, s_dumpSink, liftLambdas: TargetNeedsLambdaLifting(target));
        if (irResult is null) return 1;

        if (IsAssemblyTarget(target))
        {
            // --output-file routes the assembly emit through a temp dir + rename
            // so the existing EmitAssembly contract (which writes <dir>/<name>.<ext>)
            // can land at an arbitrary path. Lets ref-sweep ask for foo.elf
            // explicitly instead of relying on the default <name>-with-no-extension
            // x86-64 file layout.
            if (outputFileOverride is not null)
            {
                string tempDir = Path.Combine(Path.GetTempPath(),
                    "codex_emit_" + Guid.NewGuid().ToString("N")[..8]);
                Directory.CreateDirectory(tempDir);
                try
                {
                    string chapterName = Path.GetFileNameWithoutExtension(filePath);
                    int rc = EmitAssembly(irResult, tempDir, chapterName, target);
                    if (rc != 0) return rc;
                    string[] produced = Directory.GetFiles(tempDir);
                    if (produced.Length == 0)
                    {
                        Console.Error.WriteLine("error: --output-file: assembly emit produced no files");
                        return 1;
                    }
                    string? outDir = Path.GetDirectoryName(Path.GetFullPath(outputFileOverride));
                    if (!string.IsNullOrEmpty(outDir)) Directory.CreateDirectory(outDir);
                    File.Copy(produced[0], outputFileOverride, overwrite: true);
                    Console.WriteLine($"✓ Compiled to {outputFileOverride} ({target}, {new FileInfo(outputFileOverride).Length:N0} bytes)");
                    return 0;
                }
                finally
                {
                    try { Directory.Delete(tempDir, true); } catch { /* best effort */ }
                }
            }

            string outputDir = outputDirOverride is not null
                ? Path.GetFullPath(outputDirOverride)
                : Path.GetDirectoryName(Path.GetFullPath(filePath)) ?? ".";
            string chapterName2 = Path.GetFileNameWithoutExtension(filePath);
            return EmitAssembly(irResult, outputDir, chapterName2, target);
        }

        Codex.Emit.ICodeEmitter emitter = CreateEmitter(target);
        string output = emitter.Emit(irResult.Chapter);
        if (CheckEmitFuel(emitter))
        {
            return 1;
        }
        string outputPath = outputFileOverride is not null
            ? Path.GetFullPath(outputFileOverride)
            : outputDirOverride is not null
                ? Path.Combine(Path.GetFullPath(outputDirOverride), Path.GetFileNameWithoutExtension(filePath) + emitter.FileExtension)
                : Path.ChangeExtension(filePath, emitter.FileExtension);
        string? outputPathDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputPathDir)) Directory.CreateDirectory(outputPathDir);
        File.WriteAllText(outputPath, output);
        s_dumpSink?.DumpCodegen(Path.GetFileNameWithoutExtension(filePath), emitter.FileExtension, output);

        Console.WriteLine($"✓ Compiled to {outputPath} ({target})");
        PrintTypes(irResult);
        PrintCapabilityReport(irResult.Capabilities);
        return 0;
    }

    static int RunBuildView(string viewName, string target, Set<string>? grantedCapabilities = null)
    {
        Repository.FactStore? store = Repository.FactStore.Open(Directory.GetCurrentDirectory());
        if (store is null)
        {
            Console.Error.WriteLine("No .codex repository found in current directory.");
            return 1;
        }

        if (!store.ViewExists(viewName))
        {
            Console.Error.WriteLine($"View '{viewName}' does not exist.");
            return 1;
        }

        Console.WriteLine($"Building from view: {viewName}");

        IRCompilationResult? irResult = CompileViewToIR(store, viewName, viewName, grantedCapabilities);
        if (irResult is null) return 1;

        string outputDir = Directory.GetCurrentDirectory();

        if (IsAssemblyTarget(target))
            return EmitAssembly(irResult, outputDir, viewName, target);

        Codex.Emit.ICodeEmitter emitter = CreateEmitter(target);
        string output = emitter.Emit(irResult.Chapter);
        if (CheckEmitFuel(emitter))
        {
            return 1;
        }
        string outputPath = Path.Combine(outputDir, viewName + emitter.FileExtension);
        File.WriteAllText(outputPath, output);

        Console.WriteLine($"✓ Compiled to {outputPath} ({target})");
        PrintTypes(irResult);
        PrintCapabilityReport(irResult.Capabilities);
        return 0;
    }

    static void PrintCapabilityReport(CapabilityReport? report)
    {
        if (report is null || !report.MainRequiresEffects)
            return;
        Set<string> caps = report.RequiredCapabilities;
        List<string> names = [];
        foreach (string c in report.MainEffects)
            names.Add(c);
        Console.WriteLine($"  Capabilities: [{string.Join(", ", names)}]");
    }

    static bool s_verbose;
    static bool s_dumpFrames;
    static bool s_diagnostic;
    static Emit.X86_64.X86_64ExitMode s_exitMode = Emit.X86_64.X86_64ExitMode.Repl;
    static Emit.X86_64.X86_64WatchdogMode s_watchdogMode = Emit.X86_64.X86_64WatchdogMode.Progress;
    internal static bool s_verifyInvariants;
    internal static bool s_emitPhaseTimings;
    static PhaseDumpSink? s_dumpSink;

    static void PrintTypes(IRCompilationResult irResult)
    {
        if (!s_verbose) return;
        foreach (KeyValuePair<string, CodexType> kv in irResult.Types)
            Console.WriteLine($"  {kv.Key} : {kv.Value}");
    }

    static bool IsAssemblyTarget(string target) => target is "il" or "exe" or "riscv" or "riscv-bare" or "wasm" or "arm64" or "x86-64" or "x86-64-bare";

    static bool TargetNeedsLambdaLifting(string target) => target is "x86-64" or "x86-64-bare" or "il";

    // Check an emitter's post-Emit fuel flag and, if tripped, print a CDX9001
    // error to stderr. Returns true if fuel was exhausted (caller should
    // propagate exit code 1).
    static bool CheckEmitFuel(Codex.Emit.ICodeEmitter emitter)
    {
        if (!emitter.EmitFuelExhausted)
        {
            return false;
        }
        Console.Error.WriteLine($"error CDX9001: compiler resource exhausted in {emitter.TargetName}-emit.EmitExpr (budget {emitter.MaxEmitDepth})");
        return true;
    }

    static bool CheckEmitFuel(Codex.Emit.IAssemblyEmitter emitter)
    {
        if (!emitter.EmitFuelExhausted)
        {
            return false;
        }
        Console.Error.WriteLine($"error CDX9001: compiler resource exhausted in {emitter.TargetName}-emit.EmitExpr (budget {emitter.MaxEmitDepth})");
        return true;
    }

    static int EmitAssembly(IRCompilationResult irResult, string outputDir, string chapterName, string target)
    {
        if (target is "riscv" or "riscv-bare")
        {
            Emit.RiscV.RiscVTarget rvTarget = target == "riscv-bare"
                ? Emit.RiscV.RiscVTarget.BareMetal : Emit.RiscV.RiscVTarget.LinuxUser;
            Emit.RiscV.RiscVEmitter rvEmitter = new(rvTarget);
            byte[] elf = PhaseTimer.Time("emit", () => rvEmitter.EmitAssembly(irResult.Chapter, chapterName));
            string ext = target == "riscv-bare" ? ".bin" : "";
            string outputPath = Path.Combine(outputDir, chapterName + ext);
            File.WriteAllBytes(outputPath, elf);
            s_dumpSink?.DumpCodegen(chapterName, ext, elf);
            Console.WriteLine($"✓ Compiled to {outputPath} ({target}, {elf.Length:N0} bytes)");
            PrintTypes(irResult);
            return 0;
        }

        if (target == "wasm")
        {
            Emit.Wasm.WasmEmitter wasmEmitter = new();
            byte[] wasm = PhaseTimer.Time("emit", () => wasmEmitter.EmitAssembly(irResult.Chapter, chapterName));
            string outputPath = Path.Combine(outputDir, chapterName + ".wasm");
            File.WriteAllBytes(outputPath, wasm);
            s_dumpSink?.DumpCodegen(chapterName, ".wasm", wasm);
            Console.WriteLine($"✓ Compiled to {outputPath} ({target}, {wasm.Length:N0} bytes)");
            PrintTypes(irResult);
            return 0;
        }

        if (target == "arm64")
        {
            Emit.Arm64.Arm64Emitter arm64Emitter = new();
            byte[] elf = PhaseTimer.Time("emit", () => arm64Emitter.EmitAssembly(irResult.Chapter, chapterName));
            string outputPath = Path.Combine(outputDir, chapterName);
            File.WriteAllBytes(outputPath, elf);
            s_dumpSink?.DumpCodegen(chapterName, ".elf", elf);
            Console.WriteLine($"✓ Compiled to {outputPath} ({target}, {elf.Length:N0} bytes)");
            PrintTypes(irResult);
            return 0;
        }

        if (target is "x86-64" or "x86-64-bare")
        {
            Emit.X86_64.X86_64Target x64Target = target == "x86-64-bare"
                ? Emit.X86_64.X86_64Target.BareMetal : Emit.X86_64.X86_64Target.LinuxUser;
            Emit.X86_64.X86_64Emitter x64Emitter = new(x64Target, s_diagnostic, s_exitMode, s_watchdogMode);
            byte[] elf = PhaseTimer.Time("emit", () => x64Emitter.EmitAssembly(irResult.Chapter, chapterName));
            if (x64Emitter.EmitFuelExhausted)
            {
                Console.Error.WriteLine($"error CDX9001: compiler resource exhausted in x86-64-emit.EmitExpr (budget {x64Emitter.MaxEmitDepth})");
                return 1;
            }
            if (s_verifyInvariants)
            {
                Emit.X86_64.X86_64CodegenInvariants.Verify(x64Emitter);
            }
            string ext = target == "x86-64-bare" ? ".elf" : "";
            string outputPath = Path.Combine(outputDir, chapterName + ext);
            File.WriteAllBytes(outputPath, elf);
            s_dumpSink?.DumpCodegen(chapterName, string.IsNullOrEmpty(ext) ? ".elf" : ext, elf);
            Console.WriteLine($"✓ Compiled to {outputPath} ({target}, {elf.Length:N0} bytes)");

            if (Environment.GetEnvironmentVariable("CODEX_DUMP_FUNC_OFFSETS") is not null)
            {
                Dictionary<string, int>? offs = x64Emitter.GetFunctionOffsets();
                if (offs is not null)
                {
                    string offsPath = Path.Combine(outputDir, chapterName + ".funcoffsets.txt");
                    using StreamWriter sw = new(offsPath);
                    foreach (KeyValuePair<string, int> kv in offs.OrderBy(o => o.Value))
                        sw.WriteLine($"{kv.Value:X8} {kv.Key}");
                    Console.WriteLine($"  func offsets: {offsPath} ({offs.Count} entries)");
                }
            }
            PrintTypes(irResult);

            if (s_dumpFrames)
            {
                Dictionary<string, int>? frames = x64Emitter.GetFunctionFrameSizes();
                if (frames is { Count: > 0 })
                {
                    Console.Error.WriteLine($"\n--- Stack Frame Sizes ({frames.Count} functions) ---");
                    int totalStack = 0;
                    foreach (KeyValuePair<string, int> kv in frames.OrderByDescending(kv => kv.Value))
                    {
                        Console.Error.WriteLine($"  {kv.Value,6} B  {kv.Key}");
                        totalStack += kv.Value;
                    }
                    Console.Error.WriteLine($"\n  Total (non-recursive sum): {totalStack:N0} B");
                    Console.Error.WriteLine($"  STACK:{frames.Values.Max()} (largest single frame)");
                }
            }

            return 0;
        }

        Emit.IL.ILEmitter emitter = new();
        byte[] assembly = PhaseTimer.Time("emit", () => emitter.EmitAssembly(irResult.Chapter, chapterName));
        if (CheckEmitFuel(emitter))
        {
            return 1;
        }

        // Write managed assembly as .dll — the native apphost .exe will load it
        string dllPath = Path.Combine(outputDir, chapterName + ".dll");
        File.WriteAllBytes(dllPath, assembly);
        s_dumpSink?.DumpCodegen(chapterName, ".dll", assembly);

        // Emitted IL references Codex.Core.CceTable for CCE ↔ Unicode conversion
        // at I/O boundaries (print-line, read-file, get-env, etc.). Copy the dll
        // next to the output so `dotnet <file>.dll` resolves it via default probing.
        string codexCoreDll = typeof(Codex.Core.CceTable).Assembly.Location;
        if (!string.IsNullOrEmpty(codexCoreDll))
            File.Copy(codexCoreDll, Path.Combine(outputDir, "Codex.Core.dll"), overwrite: true);

        string runtimeConfigPath = Path.Combine(outputDir, chapterName + ".runtimeconfig.json");
        File.WriteAllText(runtimeConfigPath, """
            {
              "runtimeOptions": {
                "tfm": "net8.0",
                "framework": {
                  "name": "Microsoft.NETCore.App",
                  "version": "8.0.0"
                }
              }
            }
            """);

        // Generate native apphost .exe stub that bootstraps the .NET runtime
        string exePath = Path.Combine(outputDir, chapterName + ".exe");
        bool appHostCreated = TryCreateAppHost(dllPath, exePath, chapterName + ".dll");

        if (appHostCreated)
            Console.WriteLine($"✓ Compiled to {exePath} ({target}, apphost + {chapterName}.dll)");
        else
            Console.WriteLine($"✓ Compiled to {dllPath} ({target}, run with: dotnet {dllPath})");

        PrintTypes(irResult);
        return 0;
    }

    static bool TryCreateAppHost(string dllPath, string exePath, string dllFileName)
    {
        // The .NET SDK ships a native apphost template that we copy and patch.
        // The template contains a SHA-256 placeholder ("c3ab8ff1...") that we
        // replace with the actual DLL filename (null-padded to 1024 bytes).
        try
        {
            // Find the .NET SDK root — try multiple strategies
            string? dotnetRoot = null;
            string[] candidates = new[]
            {
                Environment.GetEnvironmentVariable("DOTNET_ROOT") ?? "",
                Path.GetDirectoryName(Environment.ProcessPath ?? "") ?? "",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet"),
            };
            foreach (string candidate in candidates)
            {
                string? dir = candidate;
                // Walk up from candidate until we find a dir containing "sdk/"
                while (dir != null && !Directory.Exists(Path.Combine(dir, "sdk")))
                    dir = Path.GetDirectoryName(dir);
                if (dir != null) { dotnetRoot = dir; break; }
            }

            if (dotnetRoot == null) return false;

            // Find the latest SDK's AppHostTemplate
            string sdkDir = Path.Combine(dotnetRoot, "sdk");
            string? latestSdk = Directory.GetDirectories(sdkDir)
                .Where(d => File.Exists(Path.Combine(d, "AppHostTemplate", "apphost.exe")))
                .OrderByDescending(d => d)
                .FirstOrDefault();

            if (latestSdk == null) return false;

            string templatePath = Path.Combine(latestSdk, "AppHostTemplate", "apphost.exe");
            byte[] appHost = File.ReadAllBytes(templatePath);

            // The placeholder is the SHA-256 of "foobar", null-padded to 1024 bytes
            byte[] placeholder = System.Text.Encoding.UTF8.GetBytes(
                "c3ab8ff13720e8ad9047dd39466b3c8974e592c2fa383d4a3960714caef0c4f2");

            int offset = FindBytes(appHost, placeholder);
            if (offset < 0) return false;

            // Write the DLL filename (UTF-8, null-terminated, padded to 1024)
            byte[] dllNameBytes = System.Text.Encoding.UTF8.GetBytes(dllFileName);
            if (dllNameBytes.Length >= 1024) return false;

            // Clear the 1024-byte slot and write the filename
            Array.Clear(appHost, offset, 1024);
            Array.Copy(dllNameBytes, 0, appHost, offset, dllNameBytes.Length);

            File.WriteAllBytes(exePath, appHost);
            return true;
        }
        catch
        {
            return false;
        }
    }

    static int FindBytes(byte[] haystack, byte[] needle)
    {
        for (int i = 0; i <= haystack.Length - needle.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j]) { match = false; break; }
            }
            if (match) return i;
        }
        return -1;
    }

    static Emit.ICodeEmitter CreateEmitter(string target) => target switch
    {
#if LEGACY_EMITTERS
        "js" or "javascript" => new Emit.JavaScript.JavaScriptEmitter(),
        "python" or "py" => new Emit.Python.PythonEmitter(),
        "rust" or "rs" => new Emit.Rust.RustEmitter(),
        "cpp" or "c++" => new Emit.Cpp.CppEmitter(),
        "go" => new Emit.Go.GoEmitter(),
        "java" => new Emit.Java.JavaEmitter(),
        "ada" => new Emit.Ada.AdaEmitter(),
        "babbage" or "ae" => new Emit.Babbage.BabbageEmitter(),
        "fortran" or "f90" => new Emit.Fortran.FortranEmitter(),
        "cobol" or "cob" => new Emit.Cobol.CobolEmitter(),
#endif
        "codex" => new Emit.Codex.CodexEmitter(),
        _ => new CSharpEmitter()
    };
}
