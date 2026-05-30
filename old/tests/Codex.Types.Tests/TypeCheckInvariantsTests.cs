using System.Collections.Immutable;
using Codex.Ast;
using Codex.Core;
using Codex.Semantics;
using Codex.Syntax;
using Xunit;

namespace Codex.Types.Tests;

public class TypeCheckInvariantsTests
{
    static (ResolvedChapter Resolved, Map<string, CodexType> Types) CheckClean(string source)
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
        return (resolved, types);
    }

    static SourceSpan SyntheticSpan() =>
        new(new SourcePosition(0, 1, 1), new SourcePosition(0, 1, 1), "synthetic.codex");

    [Fact]
    public void Verify_passes_for_well_typed_program()
    {
        (ResolvedChapter resolved, Map<string, CodexType> types) = CheckClean("f (x) = x + 1\ng = f 41");
        TypeCheckInvariants.Verify(resolved.Chapter, types);
    }

    [Fact]
    public void Verify_throws_when_definition_missing_from_type_map()
    {
        SourceSpan span = SyntheticSpan();
        Definition d = new(
            new Name("missing"), [], null,
            new LiteralExpr(0L, LiteralKind.Integer, span), span);
        Chapter chapter = new(
            QualifiedName.Simple("S"), [d], [], [], [], span);
        ResolvedChapter resolved = new(
            chapter, Set<string>.Of("missing"), Set<string>.s_empty, Set<string>.s_empty);

        InvariantViolationException ex = Assert.Throws<InvariantViolationException>(
            () => TypeCheckInvariants.Verify(resolved.Chapter, Map<string, CodexType>.s_empty));
        Assert.Equal("type-check", ex.Phase);
        Assert.Contains("missing", ex.Detail);
        Assert.Contains("type-map entry", ex.Invariant);
    }

    [Fact]
    public void Verify_throws_when_top_level_type_is_ErrorType()
    {
        SourceSpan span = SyntheticSpan();
        Definition d = new(
            new Name("broken"), [], null,
            new LiteralExpr(0L, LiteralKind.Integer, span), span);
        Chapter chapter = new(
            QualifiedName.Simple("S"), [d], [], [], [], span);
        ResolvedChapter resolved = new(
            chapter, Set<string>.Of("broken"), Set<string>.s_empty, Set<string>.s_empty);
        Map<string, CodexType> types = Map<string, CodexType>.s_empty
            .Set("broken", ErrorType.s_instance);

        InvariantViolationException ex = Assert.Throws<InvariantViolationException>(
            () => TypeCheckInvariants.Verify(resolved.Chapter, types));
        Assert.Equal("type-check", ex.Phase);
        Assert.Contains("ErrorType", ex.Detail);
    }

    [Fact]
    public void Verify_throws_when_ErrorType_nested_inside_function_type()
    {
        SourceSpan span = SyntheticSpan();
        Definition d = new(
            new Name("fn"), [], null,
            new LiteralExpr(0L, LiteralKind.Integer, span), span);
        Chapter chapter = new(
            QualifiedName.Simple("S"), [d], [], [], [], span);
        ResolvedChapter resolved = new(
            chapter, Set<string>.Of("fn"), Set<string>.s_empty, Set<string>.s_empty);
        // fn : Integer -> <error>
        CodexType leaky = new FunctionType(IntegerType.s_instance, ErrorType.s_instance);
        Map<string, CodexType> types = Map<string, CodexType>.s_empty.Set("fn", leaky);

        InvariantViolationException ex = Assert.Throws<InvariantViolationException>(
            () => TypeCheckInvariants.Verify(resolved.Chapter, types));
        Assert.Equal("type-check", ex.Phase);
        Assert.Contains("fn", ex.Detail);
    }

    [Fact]
    public void Verify_throws_when_ErrorType_nested_inside_record_field()
    {
        SourceSpan span = SyntheticSpan();
        Definition d = new(
            new Name("r"), [], null,
            new LiteralExpr(0L, LiteralKind.Integer, span), span);
        Chapter chapter = new(
            QualifiedName.Simple("S"), [d], [], [], [], span);
        ResolvedChapter resolved = new(
            chapter, Set<string>.Of("r"), Set<string>.s_empty, Set<string>.s_empty);
        RecordType record = new(
            new Name("Rec"), [],
            [new RecordFieldType(new Name("bad"), ErrorType.s_instance)]);
        Map<string, CodexType> types = Map<string, CodexType>.s_empty.Set("r", record);

        Assert.Throws<InvariantViolationException>(
            () => TypeCheckInvariants.Verify(resolved.Chapter, types));
    }
}
