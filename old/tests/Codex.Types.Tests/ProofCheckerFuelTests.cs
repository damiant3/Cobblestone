using Codex.Ast;
using Codex.Core;
using Codex.Proofs;
using Xunit;

namespace Codex.Types.Tests;

public class ProofCheckerFuelTests
{
    static SourceSpan Span() => SourceSpan.Single(0, 1, 1, "test");

    [Fact]
    public void ProofChecker_deep_sym_chain_trips_fuel()
    {
        // Construct a proof-expression tree with 400 nested SymProofExprs.
        // Each sym flips the equality; double-sym returns the original goal.
        // The exact semantics don't matter here — we only care that the
        // recursive walk hits the fuel guard before overflowing the stack.
        ProofExpr inner = new ReflProofExpr(Span());
        for (int i = 0; i < 400; i++)
        {
            inner = new SymProofExpr(inner, Span());
        }
        ClaimDef claim = new(
            new Name("triv"), [],
            new NamedTypeExpr(new Name("Integer"), Span()),
            new NamedTypeExpr(new Name("Integer"), Span()),
            Span());
        ProofDef proof = new(
            new Name("triv"), [], inner, Span());  // name must match claim
        Chapter chapter = new(
            QualifiedName.Simple("T"), [], [], [claim], [proof], Span());

        DiagnosticBag bag = new();
        ProofChecker checker = new(bag);
        checker.CheckChapter(chapter, Map<string, CodexType>.s_empty);

        Diagnostic? fuelDiag = bag.ToImmutable()
            .FirstOrDefault(d => d.Code == CdxCodes.ResourceExhausted);
        Assert.NotNull(fuelDiag);
        Assert.Contains("proof-checker", fuelDiag.Message);
    }

    [Fact]
    public void ProofChecker_moderate_depth_succeeds()
    {
        // 64-deep sym chain — well under the 256 fuel budget. Verifies the
        // guard doesn't trip on legitimate proofs.
        ProofExpr inner = new ReflProofExpr(Span());
        for (int i = 0; i < 64; i++)
        {
            inner = new SymProofExpr(inner, Span());
        }
        ClaimDef claim = new(
            new Name("triv"), [],
            new NamedTypeExpr(new Name("Integer"), Span()),
            new NamedTypeExpr(new Name("Integer"), Span()),
            Span());
        ProofDef proof = new(
            new Name("triv"), [], inner, Span());
        Chapter chapter = new(
            QualifiedName.Simple("T"), [], [], [claim], [proof], Span());

        DiagnosticBag bag = new();
        ProofChecker checker = new(bag);
        checker.CheckChapter(chapter, Map<string, CodexType>.s_empty);

        Assert.DoesNotContain(bag.ToImmutable(), d => d.Code == CdxCodes.ResourceExhausted);
    }
}
