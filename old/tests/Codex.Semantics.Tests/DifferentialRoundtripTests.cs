using System.Runtime.CompilerServices;
using Codex.Ast;
using Codex.Core;
using Codex.Emit.Codex;
using Codex.IR;
using Codex.Semantics;
using Codex.Syntax;
using Codex.Types;
using Xunit;

namespace Codex.Semantics.Tests;

// Differential roundtrip harness (section H/3). The reference compiler's
// CodexEmitter emits IR as Codex source text. Running that source back
// through the pipeline should produce IR that re-emits byte-identical to
// the first round's text. This is pingpong's sem-equiv test shrunk to
// unit scale: pingpong runs the self-host compiler through one full
// iteration; this test runs the reference compiler through one full
// iteration and asserts fixed point.
//
// A divergence here indicates: CodexEmitter drops information, a parser/
// desugarer/typechecker/lowerer phase is non-idempotent against its own
// output, or some subtle difference like name-mangling drift. Pingpong
// would catch the same bug at bare-metal scale — this harness surfaces
// it in ~50ms at unit scale.
public class DifferentialRoundtripTests
{
    static string FixturesRoot([CallerFilePath] string? thisFile = null)
    {
        string dir = Path.GetDirectoryName(thisFile)!;
        return Path.Combine(dir, "goldens");
    }

    [Theory]
    [InlineData("arith")]
    [InlineData("shapes")]
    public void IR_text_roundtrip_is_fixed_point(string fixture)
    {
        string sourcePath = Path.Combine(FixturesRoot(), fixture, "source.codex");
        string source = File.ReadAllText(sourcePath);

        string round1 = CompileAndEmit(source, fixture);
        string round2 = CompileAndEmit(round1, fixture);

        Assert.Equal(round1, round2);
    }

    static string CompileAndEmit(string source, string chapterName)
    {
        SourceText src = new($"{chapterName}.codex", source);
        DiagnosticBag bag = new();
        DocumentNode doc = DocumentParser.Parse(src, bag);
        Assert.False(bag.HasErrors, $"parse errors: {string.Join(", ", bag.ToImmutable())}");

        Desugarer desugarer = new(bag);
        Chapter chapter = desugarer.Desugar(doc, chapterName);
        Assert.False(bag.HasErrors, $"desugar errors: {string.Join(", ", bag.ToImmutable())}");

        NameResolver resolver = new(bag);
        ResolvedChapter resolved = resolver.Resolve(chapter);
        Assert.False(bag.HasErrors, $"resolve errors: {string.Join(", ", bag.ToImmutable())}");

        TypeChecker checker = new(bag);
        Map<string, CodexType> types = checker.CheckChapter(resolved.Chapter);
        Assert.False(bag.HasErrors, $"typecheck errors: {string.Join(", ", bag.ToImmutable())}");

        Lowering lowering = new(types, checker.ConstructorMap, checker.TypeDefMap, bag);
        IRChapter ir = lowering.Lower(resolved);
        Assert.False(bag.HasErrors, $"lower errors: {string.Join(", ", bag.ToImmutable())}");

        return new CodexEmitter().Emit(ir);
    }
}
