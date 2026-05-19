using Codex.Ast;
using Codex.Core;
using Codex.Semantics;
using Codex.Syntax;
using Xunit;

namespace Codex.Types.Tests;

public class ElaboratedAstTests
{
    static (ResolvedChapter Resolved, TypeChecker Checker, Map<string, CodexType> Types) CheckClean(string source)
    {
        SourceText src = new("test.codex", source);
        DiagnosticBag bag = new();
        Lexer lexer = new(src, bag);
        IReadOnlyList<Token> tokens = lexer.TokenizeAll();
        Parser parser = new(tokens, bag);
        DocumentNode doc = parser.ParseDocument();
        Desugarer desugarer = new(bag);
        Chapter chapter = Helpers.WithAllBuiltinCites(desugarer.Desugar(doc, "Test"));
        NameResolver resolver = new(bag);
        ResolvedChapter resolved = resolver.Resolve(chapter);
        TypeChecker checker = new(bag);
        Map<string, CodexType> types = checker.CheckChapter(resolved.Chapter);
        Assert.False(bag.HasErrors);
        return (resolved, checker, types);
    }

    [Fact]
    public void Every_literal_in_body_has_an_entry_in_ExprTypes()
    {
        (ResolvedChapter resolved, TypeChecker checker, _) =
            CheckClean("f = 1 + 2");
        // f's body: BinaryExpr(LiteralExpr(1), LiteralExpr(2))
        // All three should be in ExprTypes.
        Definition f = resolved.Chapter.Definitions[0];
        BinaryExpr plus = Assert.IsType<BinaryExpr>(f.Body);
        Assert.True(checker.ExprTypes.ContainsKey(plus));
        Assert.True(checker.ExprTypes.ContainsKey(plus.Left));
        Assert.True(checker.ExprTypes.ContainsKey(plus.Right));
        Assert.Equal(IntegerType.s_instance, checker.ExprTypes[plus]);
        Assert.Equal(IntegerType.s_instance, checker.ExprTypes[plus.Left]);
        Assert.Equal(IntegerType.s_instance, checker.ExprTypes[plus.Right]);
    }

    [Fact]
    public void Char_literal_gets_CharType_not_ErrorType()
    {
        // Regression for the bug the D/1 elaboration verifier caught: the
        // LiteralExpr case in InferExpr's switch was missing LiteralKind.Char
        // so every char literal got ErrorType out of the default arm.
        (ResolvedChapter resolved, TypeChecker checker, _) =
            CheckClean("f = char-code 'E'");
        // Find the char literal in the body.
        Definition f = resolved.Chapter.Definitions[0];
        ApplyExpr app = Assert.IsType<ApplyExpr>(f.Body);
        LiteralExpr lit = Assert.IsType<LiteralExpr>(app.Argument);
        Assert.Equal(LiteralKind.Char, lit.Kind);
        Assert.Equal(CharType.s_instance, checker.ExprTypes[lit]);
    }

    [Fact]
    public void VerifyElaboration_throws_when_entry_missing()
    {
        // Synthetic scenario: Chapter has an expression that was never
        // recorded in ExprTypes (simulates a compiler bug in TypeChecker).
        SourceSpan span = SourceSpan.Single(0, 1, 1, "t");
        LiteralExpr stray = new(0L, LiteralKind.Integer, span);
        Definition def = new(new Name("f"), [], null, stray, span);
        Chapter chapter = new(
            QualifiedName.Simple("T"), [def], [], [], [], span);
        ResolvedChapter resolved = new(
            chapter, Set<string>.Of("f"), Set<string>.s_empty, Set<string>.s_empty);
        Dictionary<Expr, CodexType> empty = new(ReferenceEqualityComparer.Instance);

        InvariantViolationException ex = Assert.Throws<InvariantViolationException>(
            () => TypeCheckInvariants.VerifyElaboration(resolved.Chapter, empty));
        Assert.Contains("ExprTypes entry", ex.Detail);
    }
}
