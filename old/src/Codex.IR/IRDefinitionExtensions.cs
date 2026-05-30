using Codex.Types;

namespace Codex.IR;

// Definition-level predicates shared across every emit backend. Each backend
// historically re-implemented these inline — the pattern is identical, only
// the target-language emission differs.
public static class IRDefinitionExtensions
{
    // Walk the declared type past Parameters.Length argument positions,
    // skipping ProofType-parameter arrows and following DependentFunctionType
    // bodies. The result is the "body-position" type — what the function's
    // body expression must inhabit. For an effectful function this is an
    // EffectfulType; for a pure one it's the raw return type.
    public static CodexType FinalReturnType(this IRDefinition def)
    {
        CodexType type = def.Type;
        for (int i = 0; i < def.Parameters.Length; i++)
        {
            while (type is FunctionType pft && pft.Parameter is ProofType)
                type = pft.Return;

            if (type is FunctionType ft)
                type = ft.Return;
            else if (type is DependentFunctionType dep)
                type = dep.Body;
            else
                break;
        }

        while (type is FunctionType pft && pft.Parameter is ProofType)
            type = pft.Return;
        return type;
    }

    public static bool IsEffectfulDefinition(this IRDefinition def)
        => def.FinalReturnType() is EffectfulType;

    // FinalReturnType with an EffectfulType wrapper peeled off the outside.
    // Every text-emit backend wants this shape at emit time: "what target-
    // language return type do I name?" The EffectfulType annotation is an
    // effect-system tag; at runtime the body produces its wrapped value, so
    // the emitted signature names the inner.
    public static CodexType PureReturnType(this IRDefinition def)
    {
        CodexType type = def.FinalReturnType();
        return type is EffectfulType eft ? eft.Return : type;
    }
}
