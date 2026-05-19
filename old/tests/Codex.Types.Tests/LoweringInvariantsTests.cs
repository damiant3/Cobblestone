using System.Collections.Immutable;
using Codex.Ast;
using Codex.Core;
using Codex.IR;
using Codex.Semantics;
using Codex.Syntax;
using Xunit;

namespace Codex.Types.Tests;

public class LoweringInvariantsTests
{
    static IRChapter LowerClean(string source)
    {
        SourceText src = new("test.codex", source);
        DiagnosticBag bag = new();
        Lexer lexer = new(src, bag);
        IReadOnlyList<Token> tokens = lexer.TokenizeAll();
        Parser parser = new(tokens, bag);
        DocumentNode doc = parser.ParseDocument();
        Desugarer desugarer = new(bag);
        Chapter chapter = desugarer.Desugar(doc, "Test");
        NameResolver resolver = new(bag);
        ResolvedChapter resolved = resolver.Resolve(chapter);
        TypeChecker checker = new(bag);
        Map<string, CodexType> types = checker.CheckChapter(resolved.Chapter);
        Assert.False(bag.HasErrors);
        Lowering lowering = new(types, checker.ConstructorMap, checker.TypeDefMap, bag);
        return lowering.Lower(resolved);
    }

    [Fact]
    public void Verify_passes_for_well_lowered_program()
    {
        IRChapter ir = LowerClean("f (x) = x + 1\ng = f 41");
        LoweringInvariants.Verify(ir);
    }

    [Fact]
    public void Verify_passes_for_record_and_match_program()
    {
        string source = """
            Shape =
             | Circle (Integer)
             | Square (Integer)

            Point = record { x : Integer, y : Integer }

            area (s) = when s
             is Circle (r) -> r * r * 3
             is Square (w) -> w * w

            origin = Point { x = 0, y = 0 }
            """;
        IRChapter ir = LowerClean(source);
        LoweringInvariants.Verify(ir);
    }

    [Fact]
    public void Verify_throws_on_unbound_IRName()
    {
        // Synthetic IRChapter with a definition whose body references a name
        // not in topLevels/ctors/builtins/params.
        IRDefinition def = new(
            Name: "opening",
            Parameters: [],
            Type: IntegerType.s_instance,
            Body: new IRName("ghost", IntegerType.s_instance));
        IRChapter chapter = new(
            QualifiedName.Simple("S"), [def], Map<string, CodexType>.s_empty);

        InvariantViolationException ex = Assert.Throws<InvariantViolationException>(
            () => LoweringInvariants.Verify(chapter));
        Assert.Equal("lowering", ex.Phase);
        Assert.Contains("ghost", ex.Detail);
    }

    [Fact]
    public void Verify_throws_on_record_field_count_mismatch()
    {
        RecordType declared = new(
            new Name("R"), [],
            [
                new RecordFieldType(new Name("a"), IntegerType.s_instance),
                new RecordFieldType(new Name("b"), IntegerType.s_instance),
            ]);
        Map<string, CodexType> typeDefs = Map<string, CodexType>.s_empty.Set("R", declared);

        ImmutableArray<(string, IRExpr)> recFields =
            [("a", new IRIntegerLit(1))];  // missing b
        IRDefinition def = new(
            Name: "mk",
            Parameters: [],
            Type: declared,
            Body: new IRRecord("R", recFields, declared));
        IRChapter chapter = new(
            QualifiedName.Simple("S"), [def], typeDefs);

        InvariantViolationException ex = Assert.Throws<InvariantViolationException>(
            () => LoweringInvariants.Verify(chapter));
        Assert.Contains("field count", ex.Invariant);
    }

    [Fact]
    public void Verify_throws_when_record_type_name_unknown()
    {
        IRDefinition def = new(
            Name: "mk",
            Parameters: [],
            Type: IntegerType.s_instance,
            Body: new IRRecord("Phantom", [], IntegerType.s_instance));
        IRChapter chapter = new(
            QualifiedName.Simple("S"), [def], Map<string, CodexType>.s_empty);

        InvariantViolationException ex = Assert.Throws<InvariantViolationException>(
            () => LoweringInvariants.Verify(chapter));
        Assert.Contains("Phantom", ex.Detail);
    }

    [Fact]
    public void Verify_accepts_SumType_constructor_reference()
    {
        // A top-level def referencing a ctor by bare name should verify,
        // since ctor names are part of the effective scope.
        SumType declared = new(
            new Name("C"), [],
            [new SumConstructorType(new Name("Nil"), [])]);
        Map<string, CodexType> typeDefs = Map<string, CodexType>.s_empty.Set("C", declared);

        IRDefinition def = new(
            Name: "x",
            Parameters: [],
            Type: declared,
            Body: new IRName("Nil", declared));
        IRChapter chapter = new(
            QualifiedName.Simple("S"), [def], typeDefs);

        LoweringInvariants.Verify(chapter);
    }
}
