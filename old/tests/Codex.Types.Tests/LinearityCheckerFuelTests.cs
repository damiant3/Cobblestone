using Codex.Ast;
using Codex.Core;
using Xunit;

namespace Codex.Types.Tests;

public class LinearityCheckerFuelTests
{
    static SourceSpan Span() => SourceSpan.Single(0, 1, 1, "test");

    [Fact]
    public void LinearityChecker_deep_apply_trips_fuel()
    {
        // Hand-built 400-nested ApplyExpr in opening's body.
        Expr body = new NameExpr(new Name("opening"), Span());
        for (int i = 0; i < 400; i++)
        {
            body = new ApplyExpr(
                body,
                new LiteralExpr(0L, LiteralKind.Integer, Span()),
                Span());
        }
        Definition def = new(
            new Name("opening"), [], null, body, Span());
        Chapter chapter = new(
            QualifiedName.Simple("T"),
            [def], [], [], [], Span());

        DiagnosticBag bag = new();
        LinearityChecker checker = new(bag, Map<string, CodexType>.s_empty);
        checker.CheckChapter(chapter);

        Diagnostic? fuelDiag = bag.ToImmutable()
            .FirstOrDefault(d => d.Code == CdxCodes.ResourceExhausted);
        Assert.NotNull(fuelDiag);
        Assert.Contains("linearity-checker", fuelDiag.Message);
        Assert.Contains("budget 256", fuelDiag.Message);
    }

    [Fact]
    public void LinearityChecker_moderate_depth_succeeds()
    {
        Expr body = new NameExpr(new Name("opening"), Span());
        for (int i = 0; i < 64; i++)
        {
            body = new ApplyExpr(
                body,
                new LiteralExpr(0L, LiteralKind.Integer, Span()),
                Span());
        }
        Definition def = new(
            new Name("opening"), [], null, body, Span());
        Chapter chapter = new(
            QualifiedName.Simple("T"),
            [def], [], [], [], Span());

        DiagnosticBag bag = new();
        LinearityChecker checker = new(bag, Map<string, CodexType>.s_empty);
        checker.CheckChapter(chapter);

        Assert.DoesNotContain(bag.ToImmutable(), d => d.Code == CdxCodes.ResourceExhausted);
    }
}
