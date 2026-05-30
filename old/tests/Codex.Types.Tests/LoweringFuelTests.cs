using System.Collections.Immutable;
using Codex.Ast;
using Codex.Core;
using Codex.IR;
using Codex.Semantics;
using Codex.Syntax;
using Xunit;

namespace Codex.Types.Tests;

public class LoweringFuelTests
{
    static SourceSpan Span() => SourceSpan.Single(0, 1, 1, "test");

    static (DiagnosticBag Bag, IRChapter? Ir) LowerHandBuilt(Expr body)
    {
        Definition def = new(
            new Name("opening"), [], null, body, Span());
        Chapter chapter = new(
            QualifiedName.Simple("T"),
            [def], [], [], [], Span());

        // Bypass NameResolver + TypeChecker so only the lowering pass fires,
        // and any CDX9001 in the bag is attributable to lowering.
        DiagnosticBag bag = new();
        Lowering lowering = new(
            Map<string, CodexType>.s_empty,
            Map<string, CtorInfo>.s_empty,
            Map<string, CodexType>.s_empty,
            bag);
        IRChapter ir = lowering.Lower(chapter);
        return (bag, ir);
    }

    [Fact]
    public void Lowering_moderate_depth_succeeds()
    {
        // 64 nested Apply — under the 256 budget.
        Expr body = new NameExpr(new Name("opening"), Span());
        for (int i = 0; i < 64; i++)
        {
            body = new ApplyExpr(
                body,
                new LiteralExpr(0L, LiteralKind.Integer, Span()),
                Span());
        }

        (DiagnosticBag bag, _) = LowerHandBuilt(body);
        Assert.DoesNotContain(bag.ToImmutable(), d => d.Code == CdxCodes.ResourceExhausted);
    }

    [Fact]
    public void Lowering_deep_apply_trips_fuel()
    {
        Expr body = new NameExpr(new Name("opening"), Span());
        for (int i = 0; i < 400; i++)
        {
            body = new ApplyExpr(
                body,
                new LiteralExpr(0L, LiteralKind.Integer, Span()),
                Span());
        }

        (DiagnosticBag bag, _) = LowerHandBuilt(body);
        Assert.Contains(bag.ToImmutable(), d => d.Code == CdxCodes.ResourceExhausted);
    }

    [Fact]
    public void ResourceExhausted_detail_names_lowering()
    {
        Expr body = new NameExpr(new Name("opening"), Span());
        for (int i = 0; i < 400; i++)
        {
            body = new ApplyExpr(
                body,
                new LiteralExpr(0L, LiteralKind.Integer, Span()),
                Span());
        }

        (DiagnosticBag bag, _) = LowerHandBuilt(body);
        Diagnostic? fuelDiag = bag.ToImmutable()
            .FirstOrDefault(d => d.Code == CdxCodes.ResourceExhausted);
        Assert.NotNull(fuelDiag);
        Assert.Contains("lowering", fuelDiag.Message);
        Assert.Contains("budget 256", fuelDiag.Message);
    }
}
