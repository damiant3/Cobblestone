using System.Collections.Immutable;
using Codex.Core;
using Codex.Semantics;
using Codex.Types;

namespace Codex.IR;

// Post-lowering invariant pass. Asserts the IRChapter handed to codegen
// satisfies the preconditions downstream backends depend on:
//   (1) every IRName resolves to a top-level def, builtin, ctor, or local
//   (2) every IRRecord construction matches its declared RecordType
//   (3) every IRMatch on a SumType covers all constructors or has a
//       wildcard/var catch-all
//
// One invariant from the doc is deferred:
//   - "no unresolved type variables": TypeChecker strips ForAllType before
//     handing types to Lowering, so polymorphic defs (e.g. map-list) carry
//     unbound-looking TypeVariables that are really erased-quantifier
//     polymorphism. Distinguishing those from genuine unsolved inference
//     state needs ForAllType preserved through lowering.
//
// Violations throw InvariantViolationException with phase "lowering".
public static class LoweringInvariants
{
    public static void Verify(IRChapter chapter)
    {
        Set<string> globals = chapter.CollectTopLevelNames()
            .Union(chapter.CollectConstructorNames())
            .Union(NameResolver.Builtins);

        foreach (IRDefinition def in chapter.Definitions)
        {
            Set<string> scope = globals;
            foreach (IRParameter p in def.Parameters)
                scope = scope.Add(p.Name);
            new ScopeChecker(chapter, def.Name).Check(scope, def.Body);
        }
    }

    sealed class ScopeChecker(IRChapter chapter, string enclosingDef) : IRExprWalker
    {
        Set<string> m_scope = Set<string>.s_empty;

        public void Check(Set<string> initial, IRExpr body)
        {
            m_scope = initial;
            Visit(body);
        }

        public override void Visit(IRExpr expr)
        {
            switch (expr)
            {
                case IRLambda lam:
                {
                    OnEnter(expr);
                    Set<string> saved = m_scope;
                    foreach (IRParameter p in lam.Parameters)
                        m_scope = m_scope.Add(p.Name);
                    Visit(lam.Body);
                    m_scope = saved;
                    return;
                }
                case IRLet let:
                {
                    OnEnter(expr);
                    Visit(let.Value);
                    Set<string> saved = m_scope;
                    m_scope = m_scope.Add(let.Name);
                    Visit(let.Body);
                    m_scope = saved;
                    return;
                }
                case IRMatch m:
                {
                    OnEnter(expr);
                    Visit(m.Scrutinee);
                    foreach (IRMatchBranch br in m.Branches)
                    {
                        Set<string> saved = m_scope;
                        CollectPatternBindings(br.Pattern, ref m_scope, enclosingDef);
                        Visit(br.Body);
                        m_scope = saved;
                    }
                    return;
                }
                case IRAct:
                {
                    OnEnter(expr);
                    Set<string> saved = m_scope;
                    foreach (IRActStatement s in ((IRAct)expr).Statements) VisitActStatement(s);
                    m_scope = saved;
                    return;
                }
                case IRHandle h:
                {
                    OnEnter(expr);
                    Visit(h.Computation);
                    foreach (IRHandleClause c in h.Clauses)
                    {
                        Set<string> saved = m_scope;
                        foreach (string p in c.Parameters) m_scope = m_scope.Add(p);
                        m_scope = m_scope.Add(c.ResumeName);
                        Visit(c.Body);
                        m_scope = saved;
                    }
                    return;
                }
                default:
                    base.Visit(expr);
                    return;
            }
        }

        protected override void VisitActStatement(IRActStatement s)
        {
            switch (s)
            {
                case IRActBind bind:
                    Visit(bind.Value);
                    m_scope = m_scope.Add(bind.Name);
                    break;
                case IRActExec exec:
                    Visit(exec.Expression);
                    break;
                default:
                    OnUnknownActStatement(s);
                    break;
            }
        }

        protected override void OnEnter(IRExpr expr)
        {
            // Name scope check
            if (expr is IRName n && !m_scope.Contains(n.Name))
            {
                throw new InvariantViolationException(
                    phase: "lowering",
                    invariant: "every IRName resolves in its effective scope",
                    detail: $"unbound IRName '{n.Name}' in definition '{enclosingDef}'");
            }

            // Per-node structural checks
            if (expr is IRMatch m)
                CheckMatchTotality(m, enclosingDef);
            if (expr is IRRecord rec)
                CheckRecordArity(rec, chapter, enclosingDef);
        }

        protected override void OnUnknownExpr(IRExpr expr)
        {
            throw new InvariantViolationException(
                phase: "verifier",
                invariant: "exhaustive IR expression coverage",
                detail: $"unknown IRExpr kind {expr.GetType().Name} in definition '{enclosingDef}' — verifier needs a new case");
        }

        protected override void OnUnknownActStatement(IRActStatement s)
        {
            throw new InvariantViolationException(
                phase: "verifier",
                invariant: "exhaustive IR act-statement coverage",
                detail: $"unknown IRActStatement kind {s.GetType().Name} in definition '{enclosingDef}' — verifier needs a new case");
        }
    }

    static void CollectPatternBindings(IRPattern pattern, ref Set<string> scope, string enclosingDef)
    {
        switch (pattern)
        {
            case IRVarPattern v:
                scope = scope.Add(v.Name);
                break;
            case IRLiteralPattern:
            case IRWildcardPattern:
                break;
            case IRCtorPattern ctor:
                foreach (IRPattern sub in ctor.SubPatterns)
                    CollectPatternBindings(sub, ref scope, enclosingDef);
                break;
            default:
                throw new InvariantViolationException(
                    phase: "verifier",
                    invariant: "exhaustive IR pattern coverage",
                    detail: $"unknown IRPattern kind {pattern.GetType().Name} in definition '{enclosingDef}' — verifier needs a new case");
        }
    }

    static void CheckMatchTotality(IRMatch m, string enclosingDef)
    {
        // Catch-all at any position satisfies totality without needing
        // constructor enumeration.
        foreach (IRMatchBranch br in m.Branches)
        {
            if (br.Pattern is IRWildcardPattern or IRVarPattern)
                return;
        }

        // Only SumType scrutinees have a closed constructor set. Primitives
        // and records fall through — the language accepts partial literal
        // matches (they compile to runtime dispatch).
        CodexType scrutineeType = CodexTypeQueries.UnwrapType(m.Scrutinee.Type);
        if (scrutineeType is not SumType sum)
            return;

        HashSet<string> covered = new();
        foreach (IRMatchBranch br in m.Branches)
        {
            if (br.Pattern is IRCtorPattern ctor)
                covered.Add(ctor.Name);
        }

        List<string> missing = new();
        foreach (SumConstructorType ctor in sum.Constructors)
        {
            if (!covered.Contains(ctor.Name.Value))
                missing.Add(ctor.Name.Value);
        }

        if (missing.Count > 0)
        {
            missing.Sort(StringComparer.Ordinal);
            throw new InvariantViolationException(
                phase: "lowering",
                invariant: "every IRMatch on a SumType covers all constructors or has a catch-all",
                detail: $"match on '{sum.TypeName.Value}' in definition '{enclosingDef}' is missing constructor(s): {string.Join(", ", missing)}");
        }
    }

    static void CheckRecordArity(IRRecord rec, IRChapter chapter, string enclosingDef)
    {
        CodexType? declared = chapter.TypeDefinitions[rec.TypeName];
        if (declared is null)
        {
            throw new InvariantViolationException(
                phase: "lowering",
                invariant: "every IRRecord's type name resolves to a declared type",
                detail: $"IRRecord references unknown type '{rec.TypeName}' in definition '{enclosingDef}'");
        }
        if (declared is not RecordType declaredRecord)
        {
            throw new InvariantViolationException(
                phase: "lowering",
                invariant: "every IRRecord's type name resolves to a RecordType",
                detail: $"IRRecord '{rec.TypeName}' resolves to {declared.GetType().Name}, not RecordType, in definition '{enclosingDef}'");
        }
        if (rec.Fields.Length != declaredRecord.Fields.Length)
        {
            throw new InvariantViolationException(
                phase: "lowering",
                invariant: "IRRecord field count matches declared RecordType",
                detail: $"IRRecord '{rec.TypeName}' has {rec.Fields.Length} field(s), declared RecordType has {declaredRecord.Fields.Length}, in definition '{enclosingDef}'");
        }

        HashSet<string> declaredFieldNames = new(declaredRecord.Fields.Select(f => f.FieldName.Value));
        foreach ((string fieldName, IRExpr _) in rec.Fields)
        {
            if (!declaredFieldNames.Contains(fieldName))
            {
                throw new InvariantViolationException(
                    phase: "lowering",
                    invariant: "IRRecord field names match declared RecordType",
                    detail: $"IRRecord '{rec.TypeName}' uses field '{fieldName}' not in declared RecordType, in definition '{enclosingDef}'");
            }
        }
    }
}
