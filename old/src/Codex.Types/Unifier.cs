using System.Collections.Immutable;
using Codex.Core;

namespace Codex.Types;

public sealed class Unifier(DiagnosticBag diagnostics)
{
    Map<int, CodexType> m_substitutions = Map<int, CodexType>.s_empty;
    readonly DiagnosticBag m_diagnostics = diagnostics;
    int m_nextId;

    // Fuel budget for recursive type-level operations. A legitimate AST is
    // nowhere near 256 deep; exceeding this bound means either a bypassed
    // occurs-check, a substitution cycle, or an adversarial input. Emit
    // CDX9001 and bail rather than stack-overflow.
    const int MaxRecursionDepth = 256;
    int m_depth;

    public SourceSpan? ContextSpan { get; set; }

    public TypeVariable FreshVar() => new(m_nextId++);

    public EffectRowVariable FreshEffectVar() => new(m_nextId++);

    // Test-only: directly install a substitution, bypassing occurs-check.
    // Used to construct a cyclic substitution map to verify Resolve's
    // iteration budget; the public Unify path cannot produce a cycle
    // because its occurs-check rejects them.
    internal void InstallSubstitutionForTesting(int varId, CodexType target) =>
        m_substitutions = m_substitutions.Set(varId, target);

    bool FuelExhausted(string op, SourceSpan? span)
    {
        if (m_depth < MaxRecursionDepth)
            return false;
        m_diagnostics.Error(CdxCodes.ResourceExhausted,
            $"compiler resource exhausted in unifier.{op} (budget {MaxRecursionDepth})",
            // Deliberate last-resort span: fuel can fire before any caller
            // pins the ContextSpan, and both span arguments and ContextSpan
            // may legitimately be null at that point. Don't "clean this up"
            // — an unusable location on a loud error is still better than
            // crashing the diagnostic formatter.
            span ?? ContextSpan ?? SourceSpan.Single(0, 1, 1, "<unknown>"));
        return true;
    }

    public bool Unify(CodexType a, CodexType b, SourceSpan span)
    {
        if (FuelExhausted(nameof(Unify), span)) return false;
        m_depth++;
        try
        {
        a = Resolve(a);
        b = Resolve(b);

        if (a.Equals(b)) return true;

        // Suppress cascading errors: if either side is already an error, unification "succeeds"
        if (a is ErrorType || b is ErrorType) return true;

        // When both sides are TypeVariables, bind the fresher (higher-id) one
        // to the older (lower-id) one. Older ids come from signature resolution
        // and per-body Instantiate; newer ids come from transient inference
        // sites (InferList fresh, fresh return-type slots). Binding "new → old"
        // keeps the canonical representative rooted closer to a signature
        // position, so DeepResolve hands ExprTypes callers a var the emitter
        // can recognize as in-scope (see M3 in REF-DRY-AUDIT.md).
        if (a is TypeVariable va && b is TypeVariable vb0 && va.Id < vb0.Id)
        {
            m_substitutions = m_substitutions.Set(vb0.Id, a);
            return true;
        }

        if (a is TypeVariable va2)
        {
            if (OccursIn(va2.Id, b))
            {
                m_diagnostics.Error(CdxCodes.InfiniteType, $"Infinite type: {va2} occurs in {b}", span);
                return false;
            }
            m_substitutions = m_substitutions.Set(va2.Id, b);
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
            int argCount = Math.Min(sa.TypeArguments.Length, sb.TypeArguments.Length);
            for (int i = 0; i < argCount; i++)
                Unify(sa.TypeArguments[i], sb.TypeArguments[i], span);
            return true;
        }

        if (a is RecordType ra && b is RecordType rb && ra.TypeName == rb.TypeName)
        {
            int fieldCount = Math.Min(ra.Fields.Length, rb.Fields.Length);
            for (int i = 0; i < fieldCount; i++)
                Unify(ra.Fields[i].Type, rb.Fields[i].Type, span);
            int argCount = Math.Min(ra.TypeArguments.Length, rb.TypeArguments.Length);
            for (int i = 0; i < argCount; i++)
                Unify(ra.TypeArguments[i], rb.TypeArguments[i], span);

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

        if (a is ConstructedType ca2 && b is SumType sb2 && ca2.Constructor == sb2.TypeName)
        {
            int n = Math.Min(ca2.Arguments.Length, sb2.TypeArguments.Length);
            for (int i = 0; i < n; i++)
                Unify(ca2.Arguments[i], sb2.TypeArguments[i], span);
            return true;
        }

        if (a is SumType sa3 && b is ConstructedType cb3 && sa3.TypeName == cb3.Constructor)
        {
            int n = Math.Min(sa3.TypeArguments.Length, cb3.Arguments.Length);
            for (int i = 0; i < n; i++)
                Unify(sa3.TypeArguments[i], cb3.Arguments[i], span);
            return true;
        }

        if (a is ConstructedType ca3 && b is RecordType rb3 && ca3.Constructor == rb3.TypeName)
        {
            int n = Math.Min(ca3.Arguments.Length, rb3.TypeArguments.Length);
            for (int i = 0; i < n; i++)
                Unify(ca3.Arguments[i], rb3.TypeArguments[i], span);
            return true;
        }

        if (a is RecordType ra3 && b is ConstructedType cb4 && ra3.TypeName == cb4.Constructor)
        {
            int n = Math.Min(ra3.TypeArguments.Length, cb4.Arguments.Length);
            for (int i = 0; i < n; i++)
                Unify(ra3.TypeArguments[i], cb4.Arguments[i], span);
            return true;
        }

        if (a is ListType la3 && b is ConstructedType cv && cv.Constructor.Value == "Vector"
            && cv.Arguments.Length == 2)
        {
            return Unify(la3.Element, cv.Arguments[1], span);
        }

        if (a is ConstructedType cv2 && cv2.Constructor.Value == "Vector"
            && cv2.Arguments.Length == 2 && b is ListType lb3)
        {
            return Unify(cv2.Arguments[1], lb3.Element, span);
        }

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
            CodexType normA = CodexTypeHelpers.NormalizeTypeLevelExpr(a);
            CodexType normB = CodexTypeHelpers.NormalizeTypeLevelExpr(b);
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
        finally
        {
            m_depth--;
        }
    }

    public CodexType Resolve(CodexType type)
    {
        int steps = 0;
        while (type is TypeVariable tv && m_substitutions.TryGet(tv.Id, out CodexType? resolved))
        {
            if (++steps > MaxRecursionDepth)
            {
                m_diagnostics.Error(CdxCodes.ResourceExhausted,
                    $"compiler resource exhausted in unifier.Resolve (budget {MaxRecursionDepth})",
                    ContextSpan ?? SourceSpan.Single(0, 1, 1, "<unknown>"));
                return ErrorType.s_instance;
            }
            type = resolved;
        }
        while (type is EffectRowVariable erv && m_substitutions.TryGet(erv.Id, out CodexType? resolved2))
        {
            if (++steps > MaxRecursionDepth)
            {
                m_diagnostics.Error(CdxCodes.ResourceExhausted,
                    $"compiler resource exhausted in unifier.Resolve (budget {MaxRecursionDepth})",
                    ContextSpan ?? SourceSpan.Single(0, 1, 1, "<unknown>"));
                return ErrorType.s_instance;
            }
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
        => new DeepResolver(this).Rewrite(type);

    sealed class DeepResolver(Unifier unifier) : CodexTypeRewriter
    {
        public override CodexType Rewrite(CodexType type)
        {
            if (unifier.FuelExhausted(nameof(DeepResolve), null)) return type;
            unifier.m_depth++;
            try
            {
                CodexType resolved = unifier.Resolve(type);
                return resolved switch
                {
                    EffectfulType eft => RewriteEffectful(eft),
                    TypeLevelBinary => CodexTypeHelpers.NormalizeTypeLevelExpr(base.Rewrite(resolved)),
                    _ => base.Rewrite(resolved)
                };
            }
            finally
            {
                unifier.m_depth--;
            }
        }

        EffectfulType RewriteEffectful(EffectfulType eft)
        {
            ImmutableArray<EffectType> extraEffects = unifier.ResolveEffectRow(eft.RowVariable);
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
            return new EffectfulType(merged.ToImmutable(), Rewrite(eft.Return));
        }
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
        => new OccursInFolder(this, varId).Fold(type);

    sealed class OccursInFolder(Unifier unifier, int varId) : CodexTypeFolder<bool>
    {
        protected override bool Zero => false;
        protected override bool Combine(bool a, bool b) => a || b;

        public override bool Fold(CodexType type)
        {
            if (unifier.FuelExhausted(nameof(OccursIn), null)) return false;
            unifier.m_depth++;
            try
            {
                CodexType resolved = unifier.Resolve(type);
                return resolved switch
                {
                    TypeVariable tv => tv.Id == varId,
                    SumType s => s.Constructors.Any(c => c.Fields.Any(f => Fold(f))),
                    RecordType r => r.Fields.Any(f => Fold(f.Type)),
                    _ => base.Fold(resolved)
                };
            }
            finally
            {
                unifier.m_depth--;
            }
        }
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
