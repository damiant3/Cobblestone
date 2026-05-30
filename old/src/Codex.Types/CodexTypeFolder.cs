namespace Codex.Types;

// Structural fold over CodexType trees. The default Fold recurses into every
// variant that has children, combining the results with Combine. Subclasses
// override Fold with a switch that handles the interesting variants and
// delegates the rest to base.Fold.
//
// Used for queries like OccursIn and ContainsEffectRowVar that ask a
// yes/no / accumulate-a-set question over the tree. The rewriter counterpart
// is CodexTypeRewriter; the split exists because the fold return type is not
// usually CodexType.
public abstract class CodexTypeFolder<T>
{
    protected abstract T Zero { get; }
    protected abstract T Combine(T a, T b);

    public virtual T Fold(CodexType type) => type switch
    {
        FunctionType f => Combine(Fold(f.Parameter), Fold(f.Return)),
        ListType l => Fold(l.Element),
        LinkedListType l => Fold(l.Element),
        ConstructedType c => FoldMany(c.Arguments),
        ForAllType fa => Fold(fa.Body),
        SumType s => Combine(
            FoldMany(s.Constructors.SelectMany(ctor => (IEnumerable<CodexType>)ctor.Fields)),
            FoldMany(s.TypeArguments)),
        RecordType r => Combine(
            FoldMany(r.Fields.Select(f => f.Type)),
            FoldMany(r.TypeArguments)),
        EffectfulType eft => Fold(eft.Return),
        LinearType lin => Fold(lin.Inner),
        DependentFunctionType dep => Combine(Fold(dep.ParamType), Fold(dep.Body)),
        TypeLevelBinary bin => Combine(Fold(bin.Left), Fold(bin.Right)),
        ProofType proof => Fold(proof.Claim),
        LessThanClaim lt => Combine(Fold(lt.Left), Fold(lt.Right)),
        _ => Zero
    };

    protected T FoldMany(IEnumerable<CodexType> types)
    {
        T acc = Zero;
        foreach (CodexType t in types)
            acc = Combine(acc, Fold(t));
        return acc;
    }
}
