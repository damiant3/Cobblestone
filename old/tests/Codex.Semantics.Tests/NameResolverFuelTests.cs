using Codex.Ast;
using Codex.Core;
using Xunit;

namespace Codex.Semantics.Tests;

public class NameResolverFuelTests
{
    static SourceSpan Span() => SourceSpan.Single(0, 1, 1, "test");

    static Chapter ChapterWithBody(Expr body, string defName = "opening")
    {
        Definition def = new(
            new Name(defName), [], null, body, Span());
        return new Chapter(
            QualifiedName.Simple("T"),
            [def], [], [], [], Span());
    }

    [Fact]
    public void ResolveExpr_moderate_depth_succeeds()
    {
        // 64-deep nested apply of a self-reference — resolver should bind
        // 'opening' and terminate cleanly.
        Expr body = new NameExpr(new Name("opening"), Span());
        for (int i = 0; i < 64; i++)
        {
            body = new ApplyExpr(body, new LiteralExpr(1L, LiteralKind.Integer, Span()), Span());
        }

        DiagnosticBag bag = new();
        NameResolver r = new(bag);
        r.Resolve(ChapterWithBody(body));

        Assert.DoesNotContain(bag.ToImmutable(), d => d.Code == CdxCodes.ResourceExhausted);
    }

    [Fact]
    public void ResolveExpr_excessive_depth_emits_CDX9001()
    {
        // 300 nested applies exceed the 256 fuel budget. Verifier emits
        // CDX9001 and returns, no stack overflow.
        Expr body = new NameExpr(new Name("opening"), Span());
        for (int i = 0; i < 300; i++)
        {
            body = new ApplyExpr(body, new LiteralExpr(1L, LiteralKind.Integer, Span()), Span());
        }

        DiagnosticBag bag = new();
        NameResolver r = new(bag);
        r.Resolve(ChapterWithBody(body));

        Assert.Contains(bag.ToImmutable(), d => d.Code == CdxCodes.ResourceExhausted);
    }

    [Fact]
    public void CollectPatternBindings_excessive_depth_emits_CDX9001()
    {
        // Register a constructor 'K' as part of a variant type, then wrap
        // it 300 deep: K (K (K ...)).
        VariantCtorDef ctor = new(
            new Name("K"),
            [new VariantFieldDef(null, new NamedTypeExpr(new Name("Color"), Span()), Span())],
            Span());
        VariantTypeDef color = new(new Name("Color"), [], [ctor], Span());

        Pattern inner = new WildcardPattern(Span());
        for (int i = 0; i < 300; i++)
        {
            inner = new CtorPattern(new Name("K"), [inner], Span());
        }

        // opening = match Nothing is <deep pattern> -> 0
        Expr body = new MatchExpr(
            new NameExpr(new Name("Nothing"), Span()),
            [new MatchBranch(inner, new LiteralExpr(0L, LiteralKind.Integer, Span()), Span())],
            Span());

        Definition def = new(
            new Name("opening"), [], null, body, Span());
        Chapter chapter = new(
            QualifiedName.Simple("T"),
            [def], [color], [], [], Span());

        DiagnosticBag bag = new();
        NameResolver r = new(bag);
        r.Resolve(chapter);

        Assert.Contains(bag.ToImmutable(), d => d.Code == CdxCodes.ResourceExhausted);
    }

    [Fact]
    public void ResourceExhausted_detail_names_the_phase()
    {
        Expr body = new NameExpr(new Name("opening"), Span());
        for (int i = 0; i < 300; i++)
        {
            body = new ApplyExpr(body, new LiteralExpr(1L, LiteralKind.Integer, Span()), Span());
        }

        DiagnosticBag bag = new();
        NameResolver r = new(bag);
        r.Resolve(ChapterWithBody(body));

        Diagnostic? fuelDiag = bag.ToImmutable()
            .FirstOrDefault(d => d.Code == CdxCodes.ResourceExhausted);
        Assert.NotNull(fuelDiag);
        Assert.Contains("name-resolver", fuelDiag.Message);
        Assert.Contains("ResolveExpr", fuelDiag.Message);
    }
}
