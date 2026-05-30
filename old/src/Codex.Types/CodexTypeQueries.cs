namespace Codex.Types;

// Query-style helpers over CodexType built on top of the CodexTypeFolder
// infrastructure from M4. Kept separate from CodexTypeHelpers so queries
// (return bool / project into new types) don't mix with the
// transformations and normalizations that live there.
public static class CodexTypeQueries
{
    // True if the type contains an ErrorType anywhere in its structure.
    // Used by invariant checkers to assert that the user-facing type-error
    // path halted the pipeline before ErrorType could leak into lowering.
    public static bool ContainsErrorType(CodexType type)
        => s_errorTypeFinder.Fold(type);

    static readonly ErrorTypeFinder s_errorTypeFinder = new();

    sealed class ErrorTypeFinder : CodexTypeFolder<bool>
    {
        protected override bool Zero => false;
        protected override bool Combine(bool a, bool b) => a || b;

        public override bool Fold(CodexType type) => type switch
        {
            ErrorType => true,
            _ => base.Fold(type)
        };
    }

    // Strip ForAllType / EffectfulType / LinearType wrappers to reach the
    // underlying "shape" type. Used by consumers that care about the
    // structural kind of a value's type (e.g. "is the scrutinee a SumType?")
    // without needing to understand the polymorphism / effect / linearity
    // layers those wrappers add.
    public static CodexType UnwrapType(CodexType type) => type switch
    {
        ForAllType fa => UnwrapType(fa.Body),
        EffectfulType eft => UnwrapType(eft.Return),
        LinearType lin => UnwrapType(lin.Inner),
        _ => type
    };
}
