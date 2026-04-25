using System.Text;
using Codex.Ast;
using Codex.Core;
using Codex.Syntax;
using Xunit;

namespace Codex.Ast.Tests;

public class DesugarerFuelTests
{
    static DiagnosticBag DesugarBag(string source)
    {
        SourceText src = new("test.codex", source);
        DiagnosticBag bag = new();
        Lexer lexer = new(src, bag);
        IReadOnlyList<Token> tokens = lexer.TokenizeAll();
        Parser parser = new(tokens, bag);
        DocumentNode doc = parser.ParseDocument();
        // Discard parser fuel diagnostics so the assertion targets desugarer.
        DiagnosticBag desugarerBag = new();
        Desugarer desugarer = new(desugarerBag);
        desugarer.Desugar(doc, "Test");
        return desugarerBag;
    }

    static DocumentNode ParseClean(string source, out DiagnosticBag parseBag)
    {
        SourceText src = new("test.codex", source);
        parseBag = new();
        Lexer lexer = new(src, parseBag);
        IReadOnlyList<Token> tokens = lexer.TokenizeAll();
        Parser parser = new(tokens, parseBag);
        return parser.ParseDocument();
    }

    [Fact]
    public void Desugarer_moderate_depth_succeeds()
    {
        // 64 nested applies is well under the 256 budget and also under
        // the parser's own budget so parse-clean reaches desugarer.
        StringBuilder body = new("x");
        for (int i = 0; i < 64; i++) { body.Append(" x"); }
        string source = $"opening : Integer\nopening = let x = 1 in {body}\n";

        DiagnosticBag bag = DesugarBag(source);
        Assert.DoesNotContain(bag.ToImmutable(), d => d.Code == CdxCodes.ResourceExhausted);
    }

    [Fact]
    public void Desugarer_deep_let_binding_trips_fuel()
    {
        // Build a DocumentNode by hand: 400-deep let-in chain (each let has
        // one binding and its body is another let). Parser's own fuel can't
        // see us here because we skip parser entirely.
        SourceSpan span = SourceSpan.Single(0, 1, 1, "t");
        Token intTok = new(TokenKind.IntegerLiteral, "1", span) { LiteralValue = 1L };
        ExpressionNode inner = new LiteralExpressionNode(intTok);
        Token nameTok = new(TokenKind.Identifier, "x", span);
        for (int i = 0; i < 400; i++)
        {
            inner = new LetExpressionNode(
                [new Syntax.LetBinding(nameTok, new LiteralExpressionNode(intTok))],
                inner,
                span);
        }

        Token mainTok = new(TokenKind.Identifier, "opening", span);
        DefinitionNode def = new(mainTok, [], null, inner, span);
        DocumentNode doc = new([def], span);

        DiagnosticBag bag = new();
        Desugarer desugarer = new(bag);
        desugarer.Desugar(doc, "Test");

        Assert.Contains(bag.ToImmutable(), d => d.Code == CdxCodes.ResourceExhausted);
    }

    [Fact]
    public void Desugarer_deep_type_trips_fuel()
    {
        // Hand-built FunctionTypeNode chain, 400 deep. Bypasses parser.
        SourceSpan span = SourceSpan.Single(0, 1, 1, "t");
        Token intName = new(TokenKind.TypeIdentifier, "Integer", span);
        TypeNode t = new NamedTypeNode(intName);
        for (int i = 0; i < 400; i++)
        {
            t = new FunctionTypeNode(new NamedTypeNode(intName), t, span);
        }

        Token mainTok = new(TokenKind.Identifier, "opening", span);
        Token intTok = new(TokenKind.IntegerLiteral, "0", span) { LiteralValue = 0L };
        DefinitionNode def = new(
            mainTok, [], new TypeAnnotationNode(mainTok, t, span),
            new LiteralExpressionNode(intTok), span);
        DocumentNode doc = new([def], span);

        DiagnosticBag bag = new();
        Desugarer desugarer = new(bag);
        desugarer.Desugar(doc, "Test");

        Assert.Contains(bag.ToImmutable(), d => d.Code == CdxCodes.ResourceExhausted);
    }

    [Fact]
    public void ResourceExhausted_detail_names_desugarer()
    {
        SourceSpan span = SourceSpan.Single(0, 1, 1, "t");
        Token intTok = new(TokenKind.IntegerLiteral, "1", span) { LiteralValue = 1L };
        Token nameTok = new(TokenKind.Identifier, "x", span);
        ExpressionNode inner = new LiteralExpressionNode(intTok);
        for (int i = 0; i < 400; i++)
        {
            inner = new LetExpressionNode(
                [new Syntax.LetBinding(nameTok, new LiteralExpressionNode(intTok))],
                inner, span);
        }
        Token mainTok = new(TokenKind.Identifier, "opening", span);
        DocumentNode doc = new([new DefinitionNode(mainTok, [], null, inner, span)], span);

        DiagnosticBag bag = new();
        new Desugarer(bag).Desugar(doc, "T");

        Diagnostic? fuelDiag = bag.ToImmutable()
            .FirstOrDefault(d => d.Code == CdxCodes.ResourceExhausted);
        Assert.NotNull(fuelDiag);
        Assert.Contains("desugarer", fuelDiag.Message);
        Assert.Contains("budget 256", fuelDiag.Message);
    }
}
