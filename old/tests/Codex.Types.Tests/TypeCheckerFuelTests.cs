using Codex.Ast;
using Codex.Core;
using Codex.Semantics;
using Xunit;

namespace Codex.Types.Tests;

public class TypeCheckerFuelTests
{
    static SourceSpan Span() => SourceSpan.Single(0, 1, 1, "test");

    [Fact]
    public void TypeChecker_deep_apply_trips_fuel()
    {
        // Hand-built 400-nested ApplyExpr. InferExpr recurses on each
        // Function/Argument pair.
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
        // Run resolver only to register names; its fuel may also trip —
        // we filter the bag for the inference-specific diagnostic.
        NameResolver resolver = new(bag);
        resolver.Resolve(chapter);
        TypeChecker checker = new(bag);
        checker.CheckChapter(chapter);

        Diagnostic? fuelDiag = bag.ToImmutable()
            .FirstOrDefault(d => d.Code == CdxCodes.ResourceExhausted
                && d.Message.Contains("type-checker"));
        Assert.NotNull(fuelDiag);
        Assert.Contains("InferExpr", fuelDiag.Message);
        Assert.Contains("budget 256", fuelDiag.Message);
    }
}
