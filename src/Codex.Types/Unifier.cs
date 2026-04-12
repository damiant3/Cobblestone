using System.Collections.Immutable;
using Codex.Core;

namespace Codex.Types;

public sealed class Unifier(DiagnosticBag diagnostics)
{
    Map<int, CodexType> m_substitutions = Map<int, CodexType>.s_empty;
    readonly DiagnosticBag m_diagnostics = diagnostics;
    int m_nextId;

    public SourceSpan? ContextSpan { get; set; }

    public TypeVariable FreshVar() => new(m_nextId++);

    public EffectRowVariable FreshEffectVar() => new(m_nextId++);

    public bool Unify(CodexType a, CodexType b, SourceSpan span)
    {
        a = Resolve(a);
        b = Resolve(b);

        if (a.Equals(b)) return true;

        // Suppress cascading errors: if either side is already an error, unification "succeeds"
        if (a is ErrorType || b is ErrorType) return true;

        if (a is TypeVariable va)
        {
            if (OccursIn(va.Id, b))
            {
                m_diagnostics.Error(CdxCodes.InfiniteType, $"Infinite type: {va} occurs in {b}", span);
                return false;
            }
            m_substitutions = m_substitutions.Set(va.Id, b);
            return true;
        }

        if (b is TypeVariable vb)
        {
            if (OccursIn(vb.Id, a))
            {
                m_diagnostics.Error(CdxCodes.InfiniteType, $"Infinite type: {vb} occurs in {a}", span);
                return false;
            }
            m_substitutions = m_substitutions.Set(vb.Id, a);
            return true;
        }

        if (a is FunctionType fa && b is FunctionType fb)
        {
            return Unify(fa.Parameter, fb.Parameter, span)
                && Unify(fa.Return, fb.Return, span);
        }

        if (a is ListType la && b is ListType lb)
            return Unify(la.Element, lb.Element, span);

        if (a is LinkedListType lla && b is LinkedListType llb)
            return Unify(lla.Element, llb.Element, span);

        if (a is SumType sa && b is SumType sb && sa.TypeName == sb.TypeName)
        {
            if (sa.Constructors.Length != sb.Constructors.Length) return true;
            for (int i = 0; i < sa.Constructors.Length; i++)
            {
                SumConstructorType sca = sa.Constructors[i];
                SumConstructorType scb = sb.Constructors[i];
                int fieldCount = Math.Min(sca.Fields.Length, scb.Fields.Length);
                for (int j = 0; j < fieldCount; j++)
                    Unify(sca.Fields[j], scb.Fields[j], span);
            }
            return true;
        }

        if (a is RecordType ra && b is RecordType rb && ra.TypeName == rb.TypeName)
        {
            int fieldCount = Math.Min(ra.Fields.Length, rb.Fields.Length);
            for (int i = 0; i < fieldCount; i++)
                Unify(ra.Fields[i].Type, rb.Fields[i].Type, span);
            return true;
        }

        if (a is LinearType la2 && b is LinearType lb2)
            return Unify(la2.Inner, lb2.Inner, span);

        if (a is EffectfulType ea && b is EffectfulType eb)
        {
            UnifyEffectRows(ea, eb, span);
            return Unify(ea.Return, eb.Return, span);
        }

        if (a is EffectfulType ea2)
            return Unify(ea2.Return, b, span);

        if (b is EffectfulType eb2)
            return Unify(a, eb2.Return, span);

        if (a is ConstructedType ca && b is ConstructedType cb)
        {
            if (ca.Constructor != cb.Constructor || ca.Arguments.Length != cb.Arguments.Length)
            {
                ReportMismatch(a, b, span);
                return false;
            }
            for (int i = 0; i < ca.Arguments.Length; i++)
            {
                if (!Unify(ca.Arguments[i], cb.Arguments[i], span))
                    return false;
            }
            return true;
        }

        if (a is ConstructedType ca2 && b is SumType sb2 && ca2.Constructor == sb2.TypeName
            && ca2.Arguments.Length == 0)
            return true;

        if (a is SumType sa3 && b is ConstructedType cb3 && sa3.TypeName == cb3.Constructor
            && cb3.Arguments.Length == 0)
            return true;

        if (a is ConstructedType ca3 && b is RecordType rb3 && ca3.Constructor == rb3.TypeName
            && ca3.Arguments.Length == 0)
            return true;

        if (a is RecordType ra3 && b is ConstructedType cb4 && ra3.TypeName == cb4.Constructor
            && cb4.Arguments.Length == 0)
            return true;

        if (a is ListType la3 && b is ConstructedType cv && cv.Constructor.Value == "Vector"
            && cv.Arguments.Length == 2)
            return Unify(la3.Element, cv.Arguments[1], span);

        if (a is ConstructedType cv2 && cv2.Constructor.Value == "Vector"
            && cv2.Arguments.Length == 2 && b is ListType lb3)
            return Unify(cv2.Arguments[1], lb3.Element, span);

        if (a is DependentFunctionType da && b is DependentFunctionType db)
        {
            return Unify(da.ParamType, db.ParamType, span)
                && Unify(da.Body, db.Body, span);
        }

        if (a is DependentFunctionType da2 && b is FunctionType fb2)
        {
            return Unify(da2.ParamType, fb2.Parameter, span)
                && Unify(da2.Body, fb2.Return, span);
        }

        if (a is FunctionType fa2 && b is DependentFunctionType db2)
        {
            return Unify(fa2.Parameter, db2.ParamType, span)
                && Unify(fa2.Return, db2.Body, span);
        }

        if (a is TypeLevelValue lva && b is TypeLevelValue lvb)
        {
            if (lva.Value != lvb.Value)
            {
                ReportMismatch(a, b, span);
                return false;
            }
            return true;
        }

        if (a is TypeLevelBinary || b is TypeLevelBinary)
        {
            CodexType normA = NormalizeTypeLevelExpr(a);
            CodexType normB = NormalizeTypeLevelExpr(b);
            if (!normA.Equals(a) || !normB.Equals(b))
                return Unify(normA, normB, span);
        }

        if (a is TypeLevelVar tva && b is TypeLevelVar tvb)
        {
            if (tva.Name == tvb.Name) return true;
            ReportMismatch(a, b, span);
            return false;
        }

        if (a is ProofType pa && b is ProofType pb)
            return Unify(pa.Claim, pb.Claim, span);

        if (a is LessThanClaim lta && b is LessThanClaim ltb)
            return Unify(lta.Left, ltb.Left, span) && Unify(lta.Right, ltb.Right, span);

        if (a is ErrorType || b is ErrorType) return true;

        ReportMismatch(a, b, span);
        return false;
    }

    public CodexType Resolve(CodexType type)
    {
        while (type is TypeVariable tv && m_substitutions.TryGet(tv.Id, out CodexType? resolved))
        {
            type = resolved;
        }
        while (type is EffectRowVariable erv && m_substitutions.TryGet(erv.Id, out CodexType? resolved2))
        {
            type = resolved2;
        }
        return type;
    }

    public ImmutableArray<EffectType> ResolveEffectRow(EffectRowVariable? rowVar)
    {
        if (rowVar is null) return [];
        CodexType resolved = Resolve(rowVar);
        if (resolved is EffectfulType eft)
            return eft.Effects;
        return [];
    }

    public CodexType DeepResolve(CodexType type)
    {
        type = Resolve(type);
        return type switch
        {
            FunctionType f => new FunctionType(DeepResolve(f.Parameter), DeepResolve(f.Return)),
            ListType l => new ListType(DeepResolve(l.Element)),
            LinkedListType l => new LinkedListType(DeepResolve(l.Element)),
            ConstructedType c => c with
            {
                Arguments = [.. c.Arguments.Select(DeepResolve)]
            },
            ForAllType fa => fa with { Body = DeepResolve(fa.Body) },
            SumType s => s with
            {
                Constructors = [.. s.Constructors.Select(c => c with
                {
                    Fields = [.. c.Fields.Select(DeepResolve)]
                })]
            },
            RecordType r => r with
            {
                Fields = [.. r.Fields.Select(f => f with
                {
                    Type = DeepResolve(f.Type)
                })]
            },
            EffectfulType eft => DeepResolveEffectful(eft),
            LinearType lin => new LinearType(DeepResolve(lin.Inner)),
            DependentFunctionType dep => new DependentFunctionType(
                dep.ParamName, DeepResolve(dep.ParamType), DeepResolve(dep.Body)),
            TypeLevelBinary bin => NormalizeTypeLevelExpr(
                new TypeLevelBinary(bin.Op, DeepResolve(bin.Left), DeepResolve(bin.Right))),
            ProofType proof => new ProofType(DeepResolve(proof.Claim)),
            LessThanClaim lt => new LessThanClaim(DeepResolve(lt.Left), DeepResolve(lt.Right)),
            _ => type
        };
    }

    EffectfulType DeepResolveEffectful(EffectfulType eft)
    {
        ImmutableArray<EffectType> extraEffects = ResolveEffectRow(eft.RowVariable);
        ImmutableArray<EffectType>.Builder merged = ImmutableArray.CreateBuilder<EffectType>();
        HashSet<string> seen = [];
        foreach (EffectType e in eft.Effects)
        {
            if (seen.Add(e.EffectName.Value))
                merged.Add(e);
        }
        foreach (EffectType e in extraEffects)
        {
            if (seen.Add(e.EffectName.Value))
                merged.Add(e);
        }
        return new EffectfulType(merged.ToImmutable(), DeepResolve(eft.Return));
    }

    void UnifyEffectRows(EffectfulType a, EffectfulType b, SourceSpan span)
    {
        if (a.RowVariable is not null && b.RowVariable is null)
        {
            EffectfulType binding = new(b.Effects, NothingType.s_instance);
            m_substitutions = m_substitutions.Set(a.RowVariable.Id, binding);
        }
        else if (a.RowVariable is null && b.RowVariable is not null)
        {
            EffectfulType binding = new(a.Effects, NothingType.s_instance);
            m_substitutions = m_substitutions.Set(b.RowVariable.Id, binding);
        }
        else if (a.RowVariable is not null && b.RowVariable is not null)
        {
            m_substitutions = m_substitutions.Set(a.RowVariable.Id,
                new EffectfulType(b.Effects, NothingType.s_instance));
            m_substitutions = m_substitutions.Set(b.RowVariable.Id,
                new EffectfulType(a.Effects, NothingType.s_instance));
        }
    }

    bool OccursIn(int varId, CodexType type)
    {
        type = Resolve(type);
        return type switch
        {
            TypeVariable tv => tv.Id == varId,
            FunctionType f => OccursIn(varId, f.Parameter) || OccursIn(varId, f.Return),
            ListType l => OccursIn(varId, l.Element),
            LinkedListType l => OccursIn(varId, l.Element),
            ConstructedType c => c.Arguments.Any(a => OccursIn(varId, a)),
            ForAllType fa => OccursIn(varId, fa.Body),
            SumType s => s.Constructors.Any(c => c.Fields.Any(f => OccursIn(varId, f))),
            RecordType r => r.Fields.Any(f => OccursIn(varId, f.Type)),
            EffectfulType eft => OccursIn(varId, eft.Return),
            LinearType lin => OccursIn(varId, lin.Inner),
            DependentFunctionType dep => OccursIn(varId, dep.ParamType) || OccursIn(varId, dep.Body),
            TypeLevelBinary bin => OccursIn(varId, bin.Left) || OccursIn(varId, bin.Right),
            ProofType proof => OccursIn(varId, proof.Claim),
            LessThanClaim lt => OccursIn(varId, lt.Left) || OccursIn(varId, lt.Right),
            _ => false
        };
    }

    static CodexType NormalizeTypeLevelExpr(CodexType type)
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
        return type;
    }

    void ReportMismatch(CodexType expected, CodexType actual, SourceSpan span)
    {
        string exp = TypeFormatter.Format(DeepResolve(expected));
        string act = TypeFormatter.Format(DeepResolve(actual));
        if (ContextSpan is not null && ContextSpan != span)
        {
            m_diagnostics.Error(CdxCodes.TypeMismatch,
                $"Type mismatch: expected {exp}, but found {act}", span, ContextSpan.Value);
        }
        else
        {
            m_diagnostics.Error(CdxCodes.TypeMismatch,
                $"Type mismatch: expected {exp}, but found {act}", span);
        }
    }
}
