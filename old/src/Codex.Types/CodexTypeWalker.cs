namespace Codex.Types;

// Structural walker over CodexType trees (void-returning). The default Visit
// recurses into every variant that has children. Subclasses override Visit
// with a switch that handles interesting variants and delegates the rest to
// base.Visit.
//
// Counterpart to CodexTypeRewriter (which returns CodexType) and
// CodexTypeFolder (which aggregates into T). Use this when the traversal
// needs stateful side-effects that don't fit a pure fold — e.g.
// accumulation into a pre-allocated HashSet with scope-bound removal on
// ForAll.
public abstract class CodexTypeWalker
{
    public virtual void Visit(CodexType type)
    {
        switch (type)
        {
            case FunctionType f:
                Visit(f.Parameter);
                Visit(f.Return);
                break;
            case ListType l:
                Visit(l.Element);
                break;
            case LinkedListType l:
                Visit(l.Element);
                break;
            case ConstructedType c:
                foreach (CodexType a in c.Arguments) Visit(a);
                break;
            case ForAllType fa:
                Visit(fa.Body);
                break;
            case SumType s:
                foreach (SumConstructorType ctor in s.Constructors)
                    foreach (CodexType f in ctor.Fields) Visit(f);
                foreach (CodexType a in s.TypeArguments) Visit(a);
                break;
            case RecordType r:
                foreach (RecordFieldType f in r.Fields) Visit(f.Type);
                foreach (CodexType a in r.TypeArguments) Visit(a);
                break;
            case EffectfulType eft:
                Visit(eft.Return);
                break;
            case LinearType lin:
                Visit(lin.Inner);
                break;
            case DependentFunctionType dep:
                Visit(dep.ParamType);
                Visit(dep.Body);
                break;
            case TypeLevelBinary bin:
                Visit(bin.Left);
                Visit(bin.Right);
                break;
            case ProofType proof:
                Visit(proof.Claim);
                break;
            case LessThanClaim lt:
                Visit(lt.Left);
                Visit(lt.Right);
                break;
        }
    }
}
