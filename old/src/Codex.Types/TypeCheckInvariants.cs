using Codex.Core;
using Codex.Ast;

namespace Codex.Types;

// Post-type-check invariant pass. Companion to Codex.Semantics.InvariantVerifier
// — lives in Codex.Types because it depends on CodexType. Violations throw
// InvariantViolationException (phase "type-check") and are compiler bugs, not
// user errors: by the time this runs the type-checker has already reported
// every user-facing type error via the diagnostic bag and the pipeline halted
// if any fired.
public static class TypeCheckInvariants
{
    public static void Verify(Chapter chapter, Map<string, CodexType> types)
    {
        foreach (Definition def in chapter.Definitions)
        {
            if (!types.ContainsKey(def.Name.Value))
            {
                throw new InvariantViolationException(
                    phase: "type-check",
                    invariant: "every top-level definition has a type-map entry",
                    detail: $"definition '{def.Name.Value}' is missing from the type-check result",
                    location: def.Span);
            }
            CodexType? t = types[def.Name.Value];
            if (t is null)
            {
                throw new InvariantViolationException(
                    phase: "type-check",
                    invariant: "every type-map entry is non-null",
                    detail: $"definition '{def.Name.Value}' has a null type",
                    location: def.Span);
            }
            CheckNoErrorType(t, def.Name.Value, def.Span);
        }
    }

    // Elaborated-AST check (section D/1). Walks every Definition body and
    // asserts each Expr has a non-ErrorType entry in the per-expression
    // types table produced by TypeChecker. Completes the deferred half of
    // section B's post-type-check bullet ("every AST/IR node has a non-null
    // type assignment").
    public static void VerifyElaboration(
        Chapter chapter,
        IReadOnlyDictionary<Expr, CodexType> exprTypes)
    {
        foreach (Definition def in chapter.Definitions)
            new ElaborationChecker(def.Name.Value, exprTypes).Visit(def.Body);
    }

    sealed class ElaborationChecker(
        string enclosingDef,
        IReadOnlyDictionary<Expr, CodexType> exprTypes) : ExprWalker
    {
        protected override void OnEnter(Expr expr)
        {
            if (!exprTypes.TryGetValue(expr, out CodexType? t))
            {
                throw new InvariantViolationException(
                    phase: "type-check",
                    invariant: "every AST expression node has a type in the elaborated-AST table",
                    detail: $"{expr.GetType().Name} in definition '{enclosingDef}' has no ExprTypes entry",
                    location: expr.Span);
            }
            if (t is ErrorType)
            {
                throw new InvariantViolationException(
                    phase: "type-check",
                    invariant: "no ErrorType in elaborated-AST expression types",
                    detail: $"{expr.GetType().Name} in definition '{enclosingDef}' has ErrorType",
                    location: expr.Span);
            }
        }

        protected override void OnUnknownExpr(Expr expr)
        {
            throw new InvariantViolationException(
                phase: "verifier",
                invariant: "exhaustive AST expression coverage",
                detail: $"unknown Expr kind {expr.GetType().Name} in '{enclosingDef}' — elaboration verifier needs a new case",
                location: expr.Span);
        }

        protected override void OnUnknownActStatement(ActStatement s)
        {
            throw new InvariantViolationException(
                phase: "verifier",
                invariant: "exhaustive AST act-statement coverage",
                detail: $"unknown ActStatement kind {s.GetType().Name} in '{enclosingDef}' — elaboration verifier needs a new case",
                location: s.Span);
        }
    }

    static void CheckNoErrorType(CodexType t, string defName, SourceSpan defSpan)
    {
        if (CodexTypeQueries.ContainsErrorType(t))
        {
            throw new InvariantViolationException(
                phase: "type-check",
                invariant: "no ErrorType reaches later phases",
                detail: $"ErrorType survived into the type-map for definition '{defName}'",
                location: defSpan);
        }
    }
}
