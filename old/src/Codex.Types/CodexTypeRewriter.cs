namespace Codex.Types;

// Structural rewriter base for CodexType trees. The default Rewrite recurses
// into every variant that has children, rebuilding with each child rewritten.
// Subclasses override Rewrite with a switch that handles the interesting
// variants and delegates the rest to base.Rewrite.
//
// Adding a new CodexType variant that contains nested types means adding one
// case here; every subclass inherits the new recursion automatically. This
// replaces the hand-rolled parallel walkers that used to live in Unifier and
// TypeChecker.Substitution.
public abstract class CodexTypeRewriter
{
    public virtual CodexType Rewrite(CodexType type) => type switch
    {
        FunctionType f => new FunctionType(Rewrite(f.Parameter), Rewrite(f.Return)),
        ListType l => new ListType(Rewrite(l.Element)),
        LinkedListType l => new LinkedListType(Rewrite(l.Element)),
        ConstructedType c => c with
        {
            Arguments = [.. c.Arguments.Select(Rewrite)]
        },
        ForAllType fa => fa with { Body = Rewrite(fa.Body) },
        SumType s => s with
        {
            Constructors = [.. s.Constructors.Select(ctor => ctor with
            {
                Fields = [.. ctor.Fields.Select(Rewrite)]
            })],
            TypeArguments = [.. s.TypeArguments.Select(Rewrite)]
        },
        RecordType r => r with
        {
            Fields = [.. r.Fields.Select(f => f with { Type = Rewrite(f.Type) })],
            TypeArguments = [.. r.TypeArguments.Select(Rewrite)]
        },
        EffectfulType eft => eft with { Return = Rewrite(eft.Return) },
        LinearType lin => new LinearType(Rewrite(lin.Inner)),
        DependentFunctionType dep => new DependentFunctionType(
            dep.ParamName, Rewrite(dep.ParamType), Rewrite(dep.Body)),
        TypeLevelBinary bin => new TypeLevelBinary(
            bin.Op, Rewrite(bin.Left), Rewrite(bin.Right)),
        ProofType proof => new ProofType(Rewrite(proof.Claim)),
        LessThanClaim lt => new LessThanClaim(Rewrite(lt.Left), Rewrite(lt.Right)),
        _ => type
    };
}
