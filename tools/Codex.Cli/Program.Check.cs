using Codex.Core;
using Codex.Syntax;
using Codex.Ast;
using Codex.Semantics;
using Codex.Types;

namespace Codex.Cli;

public static partial class Program
{
    static int RunCheck(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: codex check <file.codex> [--capabilities c1,c2]");
            return 1;
        }

        string filePath = args[0];
        Set<string>? grantedCapabilities = null;
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "--capabilities" && i + 1 < args.Length)
            {
                string[] capNames = args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                grantedCapabilities = Set<string>.s_empty;
                foreach (string cap in capNames)
                {
                    grantedCapabilities = grantedCapabilities.Add(cap);
                }
            }
        }
        if (!File.Exists(filePath))
        {
            Console.Error.WriteLine($"File not found: {filePath}");
            return 1;
        }

        string content = File.ReadAllText(filePath);
        SourceText source = new(filePath, content);
        DiagnosticBag diagnostics = new();

        DocumentNode document = ParseSourceFile(source, content, diagnostics);

        Desugarer desugarer = new(diagnostics);
        string chapterName = Path.GetFileNameWithoutExtension(filePath);
        Chapter chapter = desugarer.Desugar(document, chapterName);

        if (diagnostics.HasErrors)
        {
            PrintDiagnostics(diagnostics);
            return 1;
        }

        NameResolver resolver = new(diagnostics);
        ResolvedChapter resolved = resolver.Resolve(chapter);

        if (diagnostics.HasErrors)
        {
            PrintDiagnostics(diagnostics);
            return 1;
        }

        TypeChecker checker = new(diagnostics);
        Map<string, CodexType> types = checker.CheckChapter(resolved.Chapter);

        if (diagnostics.HasErrors) { PrintDiagnostics(diagnostics); return 1; }

        LinearityChecker linearityChecker = new(diagnostics, types);
        linearityChecker.CheckChapter(resolved.Chapter);

        if (diagnostics.HasErrors) { PrintDiagnostics(diagnostics); return 1; }

        CapabilityChecker capChecker = new(diagnostics, types);
        CapabilityReport capReport = capChecker.CheckChapter(resolved.Chapter, grantedCapabilities);

        if (diagnostics.HasErrors) { PrintDiagnostics(diagnostics); return 1; }

        Codex.Proofs.ProofChecker proofChecker = new(diagnostics);
        proofChecker.CheckChapter(resolved.Chapter, types);

        if (diagnostics.HasErrors) { PrintDiagnostics(diagnostics); return 1; }

        if (!diagnostics.HasErrors)
        {
            int claimCount = resolved.Chapter.Claims.Count;
            int proofCount = resolved.Chapter.Proofs.Count;
            string proofInfo = claimCount > 0 ? $", {claimCount} claim(s), {proofCount} proof(s)" : "";
            Console.WriteLine($"✓ {chapter.Name}: {chapter.Definitions.Count} definition(s){proofInfo}, no errors.");
            foreach (KeyValuePair<string, CodexType> kv in types)
            {
                Console.WriteLine($"  {kv.Key} : {kv.Value}");
            }
            if (capReport.MainRequiresEffects)
            {
                List<string> names = [];
                foreach (string c in capReport.MainEffects)
                {
                    names.Add(c);
                }

                Console.WriteLine($"  Capabilities: [{string.Join(", ", names)}]");
            }
        }

        PrintDiagnostics(diagnostics);
        return diagnostics.HasErrors ? 1 : 0;
    }
}
