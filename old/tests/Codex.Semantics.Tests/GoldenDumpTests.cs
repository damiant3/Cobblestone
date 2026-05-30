using System.Runtime.CompilerServices;
using System.Text;
using Codex.Ast;
using Codex.Cli;
using Codex.Core;
using Codex.Emit.Codex;
using Codex.IR;
using Codex.Semantics;
using Codex.Syntax;
using Codex.Types;
using Xunit;

namespace Codex.Semantics.Tests;

// Golden-dump roundtrip (section H/1). For each fixture in goldens/<name>/
// the test runs source.codex through parse/desugar/resolve/typecheck/lower,
// serializes each phase via the same formatters the CLI's --dump uses, and
// diffs against a checked-in golden. CI catches silent changes to any dump
// format; intentional changes regenerate via `UPDATE_GOLDENS=1 dotnet test`.
public class GoldenDumpTests
{
    static string GoldensRoot([CallerFilePath] string? thisFile = null)
    {
        // thisFile = .../tests/Codex.Semantics.Tests/GoldenDumpTests.cs
        string dir = Path.GetDirectoryName(thisFile)!;
        return Path.Combine(dir, "goldens");
    }

    static bool Updating => Environment.GetEnvironmentVariable("UPDATE_GOLDENS") == "1";

    [Theory]
    [InlineData("arith")]
    [InlineData("shapes")]
    public void Goldens_match(string fixture)
    {
        string fixtureDir = Path.Combine(GoldensRoot(), fixture);
        string sourcePath = Path.Combine(fixtureDir, "source.codex");
        Assert.True(File.Exists(sourcePath),
            $"fixture source missing: {sourcePath}");

        Dumps dumps = GenerateDumps(File.ReadAllText(sourcePath), fixture);

        AssertOrUpdate(Path.Combine(fixtureDir, "parsed.txt"), dumps.Parsed);
        AssertOrUpdate(Path.Combine(fixtureDir, "desugared.txt"), dumps.Desugared);
        AssertOrUpdate(Path.Combine(fixtureDir, "resolved.txt"), dumps.Resolved);
        AssertOrUpdate(Path.Combine(fixtureDir, "typed.txt"), dumps.Typed);
        AssertOrUpdate(Path.Combine(fixtureDir, "ir.codex"), dumps.Ir);
    }

    static void AssertOrUpdate(string goldenPath, string actual)
    {
        if (Updating)
        {
            File.WriteAllText(goldenPath, actual);
            return;
        }
        Assert.True(File.Exists(goldenPath),
            $"golden missing: {goldenPath}  (rerun with UPDATE_GOLDENS=1 to create)");
        string expected = File.ReadAllText(goldenPath);
        // Normalize line endings: git can convert them on checkout. Goldens
        // are canonically LF so the assertion compares LF-form on both sides.
        Assert.Equal(expected.Replace("\r\n", "\n"), actual.Replace("\r\n", "\n"));
    }

    sealed record Dumps(string Parsed, string Desugared, string Resolved, string Typed, string Ir);

    static Dumps GenerateDumps(string source, string chapterName)
    {
        SourceText src = new($"{chapterName}.codex", source);
        DiagnosticBag bag = new();
        // Use DocumentParser: fixtures use the Chapter:/Section: prose
        // format, which requires ProseParser. Going straight to Parser
        // misreads any file starting with `Chapter:`.
        DocumentNode document = DocumentParser.Parse(src, bag);

        StringWriter parsedSw = new();
        parsedSw.WriteLine($"=== file: {chapterName}.codex ===");
        ParsedDumpFormatter.Write(parsedSw, document);

        Desugarer desugarer = new(bag);
        Chapter chapter = desugarer.Desugar(document, chapterName);
        Assert.False(bag.HasErrors, $"parse/desugar errors: {string.Join(", ", bag.ToImmutable())}");

        StringWriter desugaredSw = new();
        DesugaredDumpFormatter.Write(desugaredSw, chapter);

        NameResolver resolver = new(bag);
        ResolvedChapter resolved = resolver.Resolve(chapter);
        Assert.False(bag.HasErrors, $"resolve errors: {string.Join(", ", bag.ToImmutable())}");

        StringWriter resolvedSw = new();
        ResolvedDumpFormatter.Write(resolvedSw, resolved);

        TypeChecker checker = new(bag);
        Map<string, CodexType> types = checker.CheckChapter(resolved.Chapter);
        Assert.False(bag.HasErrors, $"typecheck errors: {string.Join(", ", bag.ToImmutable())}");

        StringWriter typedSw = new();
        TypedDumpFormatter.Write(typedSw, resolved, types, checker.ExprTypes);

        Lowering lowering = new(types, checker.ConstructorMap, checker.TypeDefMap, bag);
        IRChapter ir = lowering.Lower(resolved);
        Assert.False(bag.HasErrors, $"lower errors: {string.Join(", ", bag.ToImmutable())}");

        string irText = new CodexEmitter().Emit(ir);

        return new Dumps(
            Parsed:    parsedSw.ToString(),
            Desugared: desugaredSw.ToString(),
            Resolved:  resolvedSw.ToString(),
            Typed:     typedSw.ToString(),
            Ir:        irText);
    }
}
