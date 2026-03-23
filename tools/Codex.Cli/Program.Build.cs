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
            return 1;
        }

        string filePath = args[0];
        string? targetOverride = null;
        string[]? multiTargets = null;
        string? viewName = null;
        string[]? capNames = null;
        bool incremental = false;
        bool verbose = false;
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "--target" && i + 1 < args.Length)
                targetOverride = args[++i].ToLowerInvariant();
            else if (args[i] == "--targets" && i + 1 < args.Length)
                multiTargets = args[++i].ToLowerInvariant().Split(',', StringSplitOptions.RemoveEmptyEntries);
            else if (args[i] == "--view" && i + 1 < args.Length)
                viewName = args[++i];
            else if (args[i] == "--capabilities" && i + 1 < args.Length)
                capNames = args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            else if (args[i] == "--incremental" || args[i] == "-i")
                incremental = true;
            else if (args[i] == "--verbose" || args[i] == "-v")
                verbose = true;
        }
        s_verbose = verbose;

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
                    targetOverride = args[++i].ToLowerInvariant();
            }
        }

        if (viewName is not null)
            return RunBuildView(viewName, targetOverride ?? "cs", grantedCapabilities);

        if (filePath == "." || filePath == "./" || filePath == ".\\" 
            || (Directory.Exists(filePath) && File.Exists(Path.Combine(filePath, "codex.project.json"))))
        {
            return RunBuildProject(filePath, targetOverride, incremental, multiTargets);
        }

        if (Directory.Exists(filePath))
        {
            return RunBuildDirectory(filePath, targetOverride ?? "cs");
        }

        return RunBuildFile(filePath, targetOverride ?? "cs", grantedCapabilities);
    }

    static int RunBuildProject(string directory, string? targetOverride, bool incremental, string[]? multiTargets)
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
        List<Codex.Semantics.IModuleLoader> depLoaders = packageResolver.ResolveAll(project);
        IReadOnlyList<Codex.Semantics.IModuleLoader>? extraLoaders =
            depLoaders.Count > 0 ? depLoaders : null;

        if (depLoaders.Count > 0)
            Console.WriteLine($"  Dependencies: {depLoaders.Count} loader(s)");

        string outputDir = Path.GetFullPath(Path.Combine(directory, project.Output));
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

        IRCompilationResult? irResult = CompileMultipleToIR(files, project.Name, extraLoaders);
        if (irResult is null) return 1;

        if (IsAssemblyTarget(target))
        {
            return EmitAssembly(irResult, outputDir, project.Name, target);
        }

        Codex.Emit.ICodeEmitter emitter = CreateEmitter(target);
        string output = emitter.Emit(irResult.Module);
        string fullOutputPath = Path.Combine(outputDir, project.Name + emitter.FileExtension);
        File.WriteAllText(fullOutputPath, output);

        Console.WriteLine($"✓ Compiled to {fullOutputPath} ({target})");
        PrintTypes(irResult);
        return 0;
    }
    static int RunBuildDirectory(string directory, string target)
    {
        string[] files = Directory.GetFiles(directory, "*.codex", SearchOption.AllDirectories);
        if (files.Length == 0)
        {
            Console.Error.WriteLine($"No .codex files found in {directory}");
            return 1;
        }
        Array.Sort(files, StringComparer.Ordinal);
        string moduleName = Path.GetFileName(Path.GetFullPath(directory));
        IRCompilationResult? irResult = CompileMultipleToIR(files, moduleName);
        if (irResult is null) return 1;

        if (IsAssemblyTarget(target))
        {
            return EmitAssembly(irResult, directory, moduleName, target);
        }

        Codex.Emit.ICodeEmitter emitter = CreateEmitter(target);
        string output = emitter.Emit(irResult.Module);
        string outputPath = Path.Combine(directory, "output" + emitter.FileExtension);
        File.WriteAllText(outputPath, output);

        Console.WriteLine($"✓ Compiled to {outputPath} ({target})");
        PrintTypes(irResult);
        return 0;
    }

    static int RunBuildFile(string filePath, string target, Set<string>? grantedCapabilities = null)
    {
        IRCompilationResult? irResult = CompileToIR(filePath, grantedCapabilities);
        if (irResult is null) return 1;

        if (IsAssemblyTarget(target))
        {
            string outputDir = Path.GetDirectoryName(Path.GetFullPath(filePath)) ?? ".";
            string moduleName = Path.GetFileNameWithoutExtension(filePath);
            return EmitAssembly(irResult, outputDir, moduleName, target);
        }

        Codex.Emit.ICodeEmitter emitter = CreateEmitter(target);
        string output = emitter.Emit(irResult.Module);
        string outputPath = Path.ChangeExtension(filePath, emitter.FileExtension);
        File.WriteAllText(outputPath, output);

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
        string output = emitter.Emit(irResult.Module);
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

    static void PrintTypes(IRCompilationResult irResult)
    {
        if (!s_verbose) return;
        foreach (KeyValuePair<string, CodexType> kv in irResult.Types)
            Console.WriteLine($"  {kv.Key} : {kv.Value}");
    }

    static bool IsAssemblyTarget(string target) => target is "il" or "exe" or "riscv" or "riscv-bare" or "wasm" or "arm64";

    static int EmitAssembly(IRCompilationResult irResult, string outputDir, string moduleName, string target)
    {
        if (target is "riscv" or "riscv-bare")
        {
            Emit.RiscV.RiscVTarget rvTarget = target == "riscv-bare"
                ? Emit.RiscV.RiscVTarget.BareMetal : Emit.RiscV.RiscVTarget.LinuxUser;
            Emit.RiscV.RiscVEmitter rvEmitter = new(rvTarget);
            byte[] elf = rvEmitter.EmitAssembly(irResult.Module, moduleName);
            string ext = target == "riscv-bare" ? ".bin" : "";
            string outputPath = Path.Combine(outputDir, moduleName + ext);
            File.WriteAllBytes(outputPath, elf);
            Console.WriteLine($"✓ Compiled to {outputPath} ({target}, {elf.Length:N0} bytes)");
            PrintTypes(irResult);
            return 0;
        }

        if (target == "wasm")
        {
            Emit.Wasm.WasmEmitter wasmEmitter = new();
            byte[] wasm = wasmEmitter.EmitAssembly(irResult.Module, moduleName);
            string outputPath = Path.Combine(outputDir, moduleName + ".wasm");
            File.WriteAllBytes(outputPath, wasm);
            Console.WriteLine($"✓ Compiled to {outputPath} ({target}, {wasm.Length:N0} bytes)");
            PrintTypes(irResult);
            return 0;
        }

        if (target == "arm64")
        {
            Emit.Arm64.Arm64Emitter arm64Emitter = new();
            byte[] elf = arm64Emitter.EmitAssembly(irResult.Module, moduleName);
            string outputPath = Path.Combine(outputDir, moduleName);
            File.WriteAllBytes(outputPath, elf);
            Console.WriteLine($"✓ Compiled to {outputPath} ({target}, {elf.Length:N0} bytes)");
            PrintTypes(irResult);
            return 0;
        }

        Emit.IL.ILEmitter emitter = new();
        byte[] assembly = emitter.EmitAssembly(irResult.Module, moduleName);
        string ilOutputPath = Path.Combine(outputDir, moduleName + ".exe");
        File.WriteAllBytes(ilOutputPath, assembly);

        string runtimeConfigPath = Path.Combine(outputDir, moduleName + ".runtimeconfig.json");
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

        Console.WriteLine($"✓ Compiled to {ilOutputPath} ({target})");
        PrintTypes(irResult);
        return 0;
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
        _ => new CSharpEmitter()
    };
}
