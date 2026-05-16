using System.Collections.Immutable;

namespace Codex.Types;

// Shared one-liners over CodexType. Grows as duplicate patterns are collapsed
// out of TypeChecker, LinearityChecker, CapabilityChecker, and emit backends.
public static class CodexTypeHelpers
{
    // Unwrap a chain of FunctionType wrappers whose parameter is a ProofType.
    // Proof parameters are erased at the value level — they do not take a
    // runtime argument — so parameter-binding and arity-counting passes skip
    // past them to the first "real" arrow.
    public static CodexType SkipProofParams(CodexType type)
    {
        while (type is FunctionType ft && ft.Parameter is ProofType)
            type = ft.Return;
        return type;
    }

    // Walk past any FunctionType + DependentFunctionType wrappers to reach
    // the declaration's tail type. Returns the EffectfulType if the tail is
    // one, otherwise null. Callers decide whether to project to names, keep
    // the row variable, etc.
    public static EffectfulType? ExtractEffectfulType(CodexType type)
    {
        CodexType current = type;
        while (current is FunctionType ft)
            current = ft.Return;

        while (current is DependentFunctionType dep)
            current = dep.Body;

        return current as EffectfulType;
    }

    // Constant-fold TypeLevelBinary where both operands resolve to
    // TypeLevelValue, recursively, including inside ConstructedType arguments.
    // Idempotent: already-normalized input returns structurally equal output.
    public static CodexType NormalizeTypeLevelExpr(CodexType type)
    {
        if (type is TypeLevelBinary bin)
        {
            CodexType left = NormalizeTypeLevelExpr(bin.Left);
            CodexType right = NormalizeTypeLevelExpr(bin.Right);

            if (left is TypeLevelValue lv && right is TypeLevelValue rv)
            {
                long result = bin.Op switch
                {
                    TypeLevelOp.Add => lv.Value + rv.Value,
                    TypeLevelOp.Sub => lv.Value - rv.Value,
                    TypeLevelOp.Mul => lv.Value * rv.Value,
                    _ => 0
                };
                return new TypeLevelValue(result);
            }

            return new TypeLevelBinary(bin.Op, left, right);
        }

        if (type is ConstructedType ct)
        {
            ImmutableArray<CodexType> normArgs =
                [.. ct.Arguments.Select(NormalizeTypeLevelExpr)];
            return ct with { Arguments = normArgs };
        }

        return type;
    }
}
