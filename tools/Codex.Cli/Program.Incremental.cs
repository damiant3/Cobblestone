using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Codex.Core;
using Codex.Syntax;
using Codex.Ast;
using Codex.Types;

namespace Codex.Cli;

public static partial class Program
{
    sealed class BuildManifest
    {
        public Dictionary<string, FileEntry> Files { get; set; } = [];
        public string? OutputHash { get; set; }
        public long LastBuildMs { get; set; }
    }

    sealed class FileEntry
    {
        public string Hash { get; set; } = "";
        public long LastModifiedTicks { get; set; }
    }

    sealed class PerFileFrontEndResult
    {
        public required string FilePath { get; init; }
        public required List<Definition> Definitions { get; init; }
        public required List<TypeDef> TypeDefinitions { get; init; }
        public required List<ClaimDef> Claims { get; init; }
        public required List<ProofDef> Proofs { get; init; }
        public required List<CitesDecl> Citations { get; init; }
        public required List<ExportDecl> Exports { get; init; }
        public required List<EffectDef> EffectDefs { get; init; }
    }

    static string ManifestPath(string directory) =>
        Path.Combine(directory, ".codex-build", "manifest.json");

    static BuildManifest LoadManifest(string directory)
    {
        string path = ManifestPath(directory);
        if (!File.Exists(path)) return new BuildManifest();
        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<BuildManifest>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new BuildManifest();
    }

    static void SaveManifest(string directory, BuildManifest manifest)
    {
        string buildDir = Path.Combine(directory, ".codex-build");
        if (!Directory.Exists(buildDir))
            Directory.CreateDirectory(buildDir);
        string json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(ManifestPath(directory), json);
    }

    static string ComputeFileHash(string filePath)
    {
        byte[] bytes = File.ReadAllBytes(filePath);
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    static bool IsFileChanged(string filePath, BuildManifest manifest)
    {
        string relativePath = filePath;
        if (!manifest.Files.TryGetValue(relativePath, out FileEntry? entry))
            return true;

        long ticks = File.GetLastWriteTimeUtc(filePath).Ticks;
        if (ticks != entry.LastModifiedTicks)
            return true;

        string hash = ComputeFileHash(filePath);
        return hash != entry.Hash;
    }

    static int RunIncrementalBuild(
        string directory, string target, string[] allFiles, string outputPath,
        IReadOnlyList<Codex.Semantics.IChapterLoader>? extraLoaders = null)
    {
        Stopwatch sw = Stopwatch.StartNew();
        BuildManifest manifest = LoadManifest(directory);

        List<string> changedFiles = [];
        List<string> unchangedFiles = [];
        foreach (string file in allFiles)
        {
            if (IsFileChanged(file, manifest))
                changedFiles.Add(file);
            else
                unchangedFiles.Add(file);
        }

        if (changedFiles.Count == 0 && File.Exists(outputPath))
        {
            sw.Stop();
            Console.WriteLine($"✓ Up to date ({allFiles.Length} file(s), {sw.ElapsedMilliseconds}ms)");
            return 0;
        }

        int changed = changedFiles.Count;
        int total = allFiles.Length;
        Console.WriteLine($"  Changed: {changed}/{total} file(s)");

        DiagnosticBag diagnostics = new();
        ConcurrentBag<PerFileFrontEndResult> results = [];

        Parallel.ForEach(allFiles, filePath =>
        {
            PerFileFrontEndResult? result = RunFrontEnd(filePath, diagnostics);
            if (result is not null)
                results.Add(result);
        });

        if (diagnostics.HasErrors)
        {
            PrintDiagnostics(diagnostics);
            return 1;
        }

        List<Definition> allDefinitions = [];
        List<TypeDef> allTypeDefinitions = [];
        List<ClaimDef> allClaims = [];
        List<ProofDef> allProofs = [];
        List<CitesDecl> allCitations = [];
        List<ExportDecl> allExports = [];
        List<EffectDef> allEffectDefs = [];

        List<PerFileFrontEndResult> orderedResults = results
            .OrderBy(r => r.FilePath, StringComparer.Ordinal)
            .ToList();

        foreach (PerFileFrontEndResult r in orderedResults)
        {
            allDefinitions.AddRange(r.Definitions);
            allTypeDefinitions.AddRange(r.TypeDefinitions);
            allClaims.AddRange(r.Claims);
            allProofs.AddRange(r.Proofs);
            allCitations.AddRange(r.Citations);
            allExports.AddRange(r.Exports);
            allEffectDefs.AddRange(r.EffectDefs);
        }

        string chapterName = Path.GetFileNameWithoutExtension(
            Path.GetFileName(outputPath).Replace(Path.GetExtension(outputPath), ""));

        SourceSpan combinedSpan = allDefinitions.Count > 0
            ? allDefinitions[0].Span
            : SourceSpan.Single(0, 1, 1, "<combined>");
        Chapter combined = new(
            QualifiedName.Simple(chapterName),
            allDefinitions,
            allTypeDefinitions,
            allClaims,
            allProofs,
            combinedSpan)
        {
            Citations = allCitations,
            Exports = allExports,
            EffectDefs = allEffectDefs
        };

        IRCompilationResult? irResult = RunBackEnd(combined, diagnostics, extraLoaders);
        if (irResult is null) return 1;

        Codex.Emit.ICodeEmitter emitter = CreateEmitter(target);
        string output = emitter.Emit(irResult.Chapter);
        File.WriteAllText(outputPath, output);

        BuildManifest newManifest = new()
        {
            OutputHash = Convert.ToHexString(SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(output))),
            LastBuildMs = sw.ElapsedMilliseconds
        };
        foreach (string file in allFiles)
        {
            newManifest.Files[file] = new FileEntry
            {
                Hash = ComputeFileHash(file),
                LastModifiedTicks = File.GetLastWriteTimeUtc(file).Ticks
            };
        }
        SaveManifest(directory, newManifest);

        sw.Stop();
        Console.WriteLine($"✓ Compiled to {outputPath} ({target}, {sw.ElapsedMilliseconds}ms)");
        foreach (KeyValuePair<string, CodexType> kv in irResult.Types)
            Console.WriteLine($"  {kv.Key} : {kv.Value}");
        return 0;
    }

    static int RunParallelMultiTargetBuild(
        string directory, string[] targets, string[] allFiles, string outputDir, string chapterName,
        IReadOnlyList<Codex.Semantics.IChapterLoader>? extraLoaders = null)
    {
        Stopwatch sw = Stopwatch.StartNew();

        DiagnosticBag diagnostics = new();
        ConcurrentBag<PerFileFrontEndResult> results = [];

        Parallel.ForEach(allFiles, filePath =>
        {
            PerFileFrontEndResult? result = RunFrontEnd(filePath, diagnostics);
            if (result is not null)
                results.Add(result);
        });

        if (diagnostics.HasErrors)
        {
            PrintDiagnostics(diagnostics);
            return 1;
        }

        List<PerFileFrontEndResult> orderedResults = results
            .OrderBy(r => r.FilePath, StringComparer.Ordinal)
            .ToList();

        List<Definition> allDefinitions = [];
        List<TypeDef> allTypeDefinitions = [];
        List<ClaimDef> allClaims = [];
        List<ProofDef> allProofs = [];
        List<CitesDecl> allCitations = [];
        List<ExportDecl> allExports = [];
        List<EffectDef> allEffectDefs = [];
        foreach (PerFileFrontEndResult r in orderedResults)
        {
            allDefinitions.AddRange(r.Definitions);
            allTypeDefinitions.AddRange(r.TypeDefinitions);
            allClaims.AddRange(r.Claims);
            allProofs.AddRange(r.Proofs);
            allCitations.AddRange(r.Citations);
            allExports.AddRange(r.Exports);
            allEffectDefs.AddRange(r.EffectDefs);
        }

        SourceSpan combinedSpan = allDefinitions.Count > 0
            ? allDefinitions[0].Span
            : SourceSpan.Single(0, 1, 1, "<combined>");
        Chapter combined = new(
            QualifiedName.Simple(chapterName),
            allDefinitions,
            allTypeDefinitions,
            allClaims,
            allProofs,
            combinedSpan);

        IRCompilationResult? irResult = RunBackEnd(combined, diagnostics, extraLoaders);
        if (irResult is null) return 1;

        if (!Directory.Exists(outputDir))
            Directory.CreateDirectory(outputDir);

        ConcurrentBag<string> emitResults = [];

        Parallel.ForEach(targets, target =>
        {
            Codex.Emit.ICodeEmitter emitter = CreateEmitter(target);
            string output = emitter.Emit(irResult.Chapter);
            string outputPath = Path.Combine(outputDir, chapterName + emitter.FileExtension);
            File.WriteAllText(outputPath, output);
            emitResults.Add($"  ✓ {outputPath} ({target})");
        });

        sw.Stop();
        Console.WriteLine($"✓ Built {targets.Length} target(s) in {sw.ElapsedMilliseconds}ms");
        foreach (string line in emitResults.OrderBy(s => s))
            Console.WriteLine(line);
        foreach (KeyValuePair<string, CodexType> kv in irResult.Types)
            Console.WriteLine($"  {kv.Key} : {kv.Value}");
        return 0;
    }

    static PerFileFrontEndResult? RunFrontEnd(string filePath, DiagnosticBag diagnostics)
    {
        string content = File.ReadAllText(filePath);
        SourceText source = new(filePath, content);

        DiagnosticBag localDiag = new();
        DocumentNode document = ParseSourceFile(source, content, localDiag);

        Desugarer desugarer = new(localDiag);
        string fileModule = Path.GetFileNameWithoutExtension(filePath);
        Chapter chapter = desugarer.Desugar(document, fileModule);

        foreach (Diagnostic d in localDiag.ToImmutable())
            diagnostics.Add(d);

        if (localDiag.HasErrors) return null;

        return new PerFileFrontEndResult
        {
            FilePath = filePath,
            Definitions = chapter.Definitions.ToList(),
            TypeDefinitions = chapter.TypeDefinitions.ToList(),
            Claims = chapter.Claims.ToList(),
            Proofs = chapter.Proofs.ToList(),
            Citations = chapter.Citations.ToList(),
            Exports = chapter.Exports.ToList(),
            EffectDefs = chapter.EffectDefs.ToList()
        };
    }

    static IRCompilationResult? RunBackEnd(
        Chapter combined, DiagnosticBag diagnostics, IReadOnlyList<Codex.Semantics.IChapterLoader>? extraLoaders = null)
    {
        List<Codex.Semantics.IChapterLoader> loaders = [];
        if (extraLoaders is not null)
        {
            foreach (Codex.Semantics.IChapterLoader loader in extraLoaders)
                loaders.Add(loader);
        }
        ForewordChapterLoader? foreword = ForewordChapterLoader.TryCreate(diagnostics);
        if (foreword is not null)
            loaders.Add(foreword);
        Codex.Repository.FactStore? store =
            Codex.Repository.FactStore.Open(Directory.GetCurrentDirectory());
        if (store is not null)
            loaders.Add(new RepositoryChapterLoader(store, diagnostics));

        Codex.Semantics.IChapterLoader? compositeLoader = loaders.Count > 0
            ? new CompositeChapterLoader([.. loaders])
            : null;
        Codex.Semantics.NameResolver resolver = compositeLoader is not null
            ? new(diagnostics, compositeLoader)
            : new(diagnostics);
        Codex.Semantics.ResolvedChapter resolved = resolver.Resolve(combined);
        if (diagnostics.HasErrors) { PrintDiagnostics(diagnostics); return null; }

        Codex.Types.TypeChecker checker = new(diagnostics);

        foreach (Codex.Semantics.ResolvedChapter imported in resolved.CitedChapters)
            checker.CiteChapter(imported.Chapter, imported.ExportedNames);

        Map<string, CodexType> types = checker.CheckChapter(resolved.Chapter);
        if (diagnostics.HasErrors) { PrintDiagnostics(diagnostics); return null; }

        Codex.Types.LinearityChecker linearityChecker = new(diagnostics, types);
        linearityChecker.CheckChapter(resolved.Chapter);
        if (diagnostics.HasErrors) { PrintDiagnostics(diagnostics); return null; }

        Codex.Proofs.ProofChecker proofChecker = new(diagnostics);
        proofChecker.CheckChapter(resolved.Chapter, types);
        if (diagnostics.HasErrors) { PrintDiagnostics(diagnostics); return null; }

        Codex.IR.Lowering lowering = new(types, checker.ConstructorMap, checker.TypeDefMap, diagnostics);
        Codex.IR.IRChapter irModule = lowering.Lower(resolved.Chapter);
        if (diagnostics.HasErrors) { PrintDiagnostics(diagnostics); return null; }

        return new IRCompilationResult(irModule, types);
    }
}
