using Codex.Core;
using Codex.Syntax;
using Codex.Ast;
using Codex.Semantics;
using Codex.Types;
using Codex.IR;
using Codex.Emit.CSharp;

namespace Codex.Cli;

public static partial class Program
{
    sealed record CompilationResult(
        string CSharpSource,
        Map<string, CodexType> Types);

    sealed record IRCompilationResult(
        IRModule Module,
        Map<string, CodexType> Types,
        CapabilityReport? Capabilities = null);

    static CompilationResult? CompileFile(string filePath)
    {
        IRCompilationResult? irResult = CompileToIR(filePath);
        if (irResult is null) return null;

        CSharpEmitter emitter = new();
        string csharpSource = emitter.Emit(irResult.Module);
        return new CompilationResult(csharpSource, irResult.Types);
    }

    static IRCompilationResult? CompileToIR(string filePath, Set<string>? grantedCapabilities = null)
    {
        if (!File.Exists(filePath))
        {
            Console.Error.WriteLine($"File not found: {filePath}");
            return null;
        }

        string content = File.ReadAllText(filePath);
        SourceText source = new(filePath, content);
        DiagnosticBag diagnostics = new();

        DocumentNode document = ParseSourceFile(source, content, diagnostics);

        Desugarer desugarer = new(diagnostics);
        string moduleName = Path.GetFileNameWithoutExtension(filePath);
        Module module = desugarer.Desugar(document, moduleName);

        if (diagnostics.HasErrors) { PrintDiagnostics(diagnostics); return null; }

        NameResolver resolver = CreateResolver(diagnostics,
            Path.GetDirectoryName(Path.GetFullPath(filePath)));
        ResolvedModule resolved = resolver.Resolve(module);

        if (diagnostics.HasErrors) { PrintDiagnostics(diagnostics); return null; }

        TypeChecker checker = new(diagnostics);

        // Import types from dependency modules before checking main module
        foreach (ResolvedModule imported in resolved.ImportedModules)
            checker.ImportModule(imported.Module, imported.ExportedNames);

        Map<string, CodexType> types = checker.CheckModule(resolved.Module);

        if (diagnostics.HasErrors) { PrintDiagnostics(diagnostics); return null; }

        LinearityChecker linearityChecker = new(diagnostics, types);
        linearityChecker.CheckModule(resolved.Module);

        if (diagnostics.HasErrors) { PrintDiagnostics(diagnostics); return null; }

        Codex.Proofs.ProofChecker proofChecker = new(diagnostics);
        proofChecker.CheckModule(resolved.Module, types);

        if (diagnostics.HasErrors) { PrintDiagnostics(diagnostics); return null; }

        Lowering lowering = new(types, checker.ConstructorMap, checker.TypeDefMap, diagnostics);
        IRModule irModule = lowering.Lower(resolved.Module);

        if (diagnostics.HasErrors) { PrintDiagnostics(diagnostics); return null; }

        CapabilityChecker capChecker = new(diagnostics, types);
        CapabilityReport capReport = capChecker.CheckModule(resolved.Module, grantedCapabilities);

        return new IRCompilationResult(irModule, types, capReport);
    }

    static IRCompilationResult? CompileMultipleToIR(
        string[] filePaths, string moduleName, IReadOnlyList<IModuleLoader>? extraLoaders = null,
        Set<string>? grantedCapabilities = null)
    {
        DiagnosticBag diagnostics = new();
        Desugarer desugarer = new(diagnostics);
        List<Module> perFileModules = [];

        foreach (string filePath in filePaths)
        {
            if (!File.Exists(filePath))
            {
                Console.Error.WriteLine($"File not found: {filePath}");
                return null;
            }

            string content = File.ReadAllText(filePath);
            SourceText source = new(filePath, content);
            DocumentNode document = ParseSourceFile(source, content, diagnostics);
            string fileModule = Path.GetFileNameWithoutExtension(filePath);
            Module module = desugarer.Desugar(document, fileModule);
            perFileModules.Add(module);
        }

        if (diagnostics.HasErrors) { PrintDiagnostics(diagnostics); return null; }

        ModuleScoper scoper = new(diagnostics);
        Module combined = scoper.Scope(perFileModules, moduleName);

        string? baseDir = filePaths.Length > 0
            ? Path.GetDirectoryName(Path.GetFullPath(filePaths[0]))
            : null;
        NameResolver resolver = CreateResolver(diagnostics, baseDir, extraLoaders);
        ResolvedModule resolved = resolver.Resolve(combined);

        if (diagnostics.HasErrors) { PrintDiagnostics(diagnostics); return null; }

        TypeChecker checker = new(diagnostics);

        foreach (ResolvedModule imported in resolved.ImportedModules)
            checker.ImportModule(imported.Module, imported.ExportedNames);

        Map<string, CodexType> types = checker.CheckModule(resolved.Module);

        foreach (ResolvedModule imported in resolved.ImportedModules)
        {
            TypeChecker importChecker = new(diagnostics);
            importChecker.CheckModule(imported.Module);
        }

        if (diagnostics.HasErrors) { PrintDiagnostics(diagnostics); return null; }

        LinearityChecker linearityChecker = new(diagnostics, types);
        linearityChecker.CheckModule(resolved.Module);

        if (diagnostics.HasErrors) { PrintDiagnostics(diagnostics); return null; }

        Codex.Proofs.ProofChecker proofChecker = new(diagnostics);
        proofChecker.CheckModule(resolved.Module, types);

        if (diagnostics.HasErrors) { PrintDiagnostics(diagnostics); return null; }

        Lowering lowering = new(types, checker.ConstructorMap, checker.TypeDefMap, diagnostics);
        IRModule irModule = lowering.Lower(resolved.Module);

        if (diagnostics.HasErrors) { PrintDiagnostics(diagnostics); return null; }

        CapabilityChecker capChecker = new(diagnostics, types);
        CapabilityReport capReport = capChecker.CheckModule(resolved.Module, grantedCapabilities);

        return new IRCompilationResult(irModule, types, capReport);
    }

    static void PrintDiagnostics(DiagnosticBag diagnostics)
    {
        foreach (Diagnostic diag in diagnostics.ToImmutable())
        {
            string severity = diag.Severity switch
            {
                DiagnosticSeverity.Error => "error",
                DiagnosticSeverity.Warning => "warning",
                DiagnosticSeverity.Info => "info",
                DiagnosticSeverity.Hint => "hint",
                _ => "?"
            };
            Console.Error.WriteLine($"{severity} {diag.Code}: {diag.Message} {diag.Span}");
            foreach (SourceSpan related in diag.RelatedSpans)
            {
                Console.Error.WriteLine($"  note: see {related}");
            }
        }
    }

    static NameResolver CreateResolver(
        DiagnosticBag diagnostics,
        string? baseDirectory = null,
        IReadOnlyList<IModuleLoader>? extraLoaders = null)
    {
        string dir = baseDirectory ?? Directory.GetCurrentDirectory();
        List<IModuleLoader> loaders = [new FileModuleLoader(dir, diagnostics)];

        if (extraLoaders is not null)
        {
            foreach (IModuleLoader loader in extraLoaders)
                loaders.Add(loader);
        }

        PreludeModuleLoader? prelude = PreludeModuleLoader.TryCreate(diagnostics);
        if (prelude is not null)
            loaders.Add(prelude);

        Codex.Repository.FactStore? store =
            Codex.Repository.FactStore.Open(Directory.GetCurrentDirectory());
        if (store is not null)
            loaders.Add(new RepositoryModuleLoader(store, diagnostics));

        return new NameResolver(diagnostics, new CompositeModuleLoader([.. loaders]));
    }

    static IRCompilationResult? CompileViewToIR(
        Codex.Repository.FactStore store, string viewName, string moduleName,
        Set<string>? grantedCapabilities = null)
    {
        ValueMap<string, ContentHash> view = store.GetNamedView(viewName);
        if (view.Count == 0)
        {
            Console.Error.WriteLine($"View '{viewName}' is empty — nothing to compile.");
            return null;
        }

        DiagnosticBag diagnostics = new();
        Desugarer desugarer = new(diagnostics);
        List<Definition> allDefinitions = [];
        List<TypeDef> allTypeDefinitions = [];
        List<ClaimDef> allClaims = [];
        List<ProofDef> allProofs = [];
        List<ImportDecl> allImports = [];
        List<ExportDecl> allExports = [];
        List<EffectDef> allEffectDefs = [];

        foreach (KeyValuePair<string, ContentHash> kv in view)
        {
            Codex.Repository.Fact? fact = store.Load(kv.Value);
            if (fact is null)
            {
                Console.Error.WriteLine(
                    $"error: definition '{kv.Key}' references missing fact {kv.Value.ToHex()}");
                return null;
            }
            if (fact.Kind != Codex.Repository.FactKind.Definition)
            {
                Console.Error.WriteLine(
                    $"error: view entry '{kv.Key}' is a {fact.Kind}, expected Definition");
                return null;
            }

            SourceText source = new(kv.Key + ".codex", fact.Content);
            DocumentNode document = ParseSourceFile(source, fact.Content, diagnostics);
            Module module = desugarer.Desugar(document, kv.Key);

            allDefinitions.AddRange(module.Definitions);
            allTypeDefinitions.AddRange(module.TypeDefinitions);
            allClaims.AddRange(module.Claims);
            allProofs.AddRange(module.Proofs);
            allImports.AddRange(module.Imports);
            allExports.AddRange(module.Exports);
            allEffectDefs.AddRange(module.EffectDefs);
        }

        if (diagnostics.HasErrors) { PrintDiagnostics(diagnostics); return null; }

        SourceSpan combinedSpan = allDefinitions.Count > 0
            ? allDefinitions[0].Span
            : SourceSpan.Single(0, 1, 1, "<view>");
        Module combined = new(
            QualifiedName.Simple(moduleName),
            allDefinitions,
            allTypeDefinitions,
            allClaims,
            allProofs,
            combinedSpan)
        {
            Imports = allImports,
            Exports = allExports,
            EffectDefs = allEffectDefs
        };

        NameResolver resolver = CreateResolver(diagnostics);
        ResolvedModule resolved = resolver.Resolve(combined);

        if (diagnostics.HasErrors) { PrintDiagnostics(diagnostics); return null; }

        TypeChecker checker = new(diagnostics);

        foreach (ResolvedModule imported in resolved.ImportedModules)
            checker.ImportModule(imported.Module, imported.ExportedNames);

        Map<string, CodexType> types = checker.CheckModule(resolved.Module);

        if (diagnostics.HasErrors) { PrintDiagnostics(diagnostics); return null; }

        LinearityChecker linearityChecker = new(diagnostics, types);
        linearityChecker.CheckModule(resolved.Module);

        if (diagnostics.HasErrors) { PrintDiagnostics(diagnostics); return null; }

        Codex.Proofs.ProofChecker proofChecker = new(diagnostics);
        proofChecker.CheckModule(resolved.Module, types);

        if (diagnostics.HasErrors) { PrintDiagnostics(diagnostics); return null; }

        Lowering lowering = new(types, checker.ConstructorMap, checker.TypeDefMap, diagnostics);
        IRModule irModule = lowering.Lower(resolved.Module);

        if (diagnostics.HasErrors) { PrintDiagnostics(diagnostics); return null; }

        CapabilityChecker capChecker = new(diagnostics, types);
        CapabilityReport capReport = capChecker.CheckModule(resolved.Module, grantedCapabilities);

        return new IRCompilationResult(irModule, types, capReport);
    }
}
