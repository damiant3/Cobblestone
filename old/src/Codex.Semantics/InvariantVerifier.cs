using Codex.Core;
using Codex.Ast;

namespace Codex.Semantics;

// Opt-in verifier passes that assert each phase's output satisfies the
// preconditions of the next phase. A violation is a compiler bug, not
// user error, so failures throw InvariantViolationException with a
// structured pointer to what broke. Enabled via --verify-invariants or
// CODEX_VERIFY_INVARIANTS=1; off by default so bootstrap can't regress
// on a verifier tightening in isolation.
public static partial class InvariantVerifier
{
    // After name resolution, every value-name reference in every definition
    // body must resolve to a binding in its effective scope. The NameResolver
    // already reports undefined names as diagnostics; this pass re-checks
    // independently to catch cases the resolver (or a future change) missed.
    public static void AfterResolution(ResolvedChapter resolved)
    {
        Set<string> globals = resolved.TopLevelNames
            .Union(resolved.ConstructorNames)
            .Union(NameResolver.Builtins);

        foreach (Definition def in resolved.Chapter.Definitions)
        {
            Set<string> scope = globals;
            foreach (Parameter p in def.Parameters)
                scope = scope.Add(p.Name.Value);
            new ScopeChecker(resolved, def.Name.Value).Check(scope, def.Body);
        }
    }

    internal static bool IsTypeReference(Name name, ResolvedChapter resolved) =>
        name.IsTypeName || resolved.TypeNames.Contains(name.Value);

    sealed class ScopeChecker(ResolvedChapter resolved, string enclosingDef) : ExprWalker
    {
        Set<string> m_scope = Set<string>.s_empty;

        public void Check(Set<string> initial, Expr body)
        {
            m_scope = initial;
            Visit(body);
        }

        public override void Visit(Expr expr)
        {
            switch (expr)
            {
                case LambdaExpr lam:
                {
                    OnEnter(expr);
                    Set<string> saved = m_scope;
                    foreach (Parameter p in lam.Parameters)
                        m_scope = m_scope.Add(p.Name.Value);
                    Visit(lam.Body);
                    m_scope = saved;
                    return;
                }
                case LetExpr let:
                {
                    OnEnter(expr);
                    Set<string> saved = m_scope;
                    foreach (LetBinding b in let.Bindings)
                    {
                        Visit(b.Value);
                        m_scope = m_scope.Add(b.Name.Value);
                    }
                    Visit(let.Body);
                    m_scope = saved;
                    return;
                }
                case MatchExpr m:
                {
                    OnEnter(expr);
                    Visit(m.Scrutinee);
                    foreach (MatchBranch br in m.Branches)
                    {
                        Set<string> saved = m_scope;
                        CollectPatternBindings(br.Pattern, ref m_scope);
                        Visit(br.Body);
                        m_scope = saved;
                    }
                    return;
                }
                case ActExpr act:
                {
                    OnEnter(expr);
                    Set<string> saved = m_scope;
                    foreach (ActStatement s in act.Statements) VisitActStatement(s);
                    m_scope = saved;
                    return;
                }
                case HandleExpr h:
                {
                    OnEnter(expr);
                    Visit(h.Computation);
                    foreach (HandleClause c in h.Clauses)
                    {
                        Set<string> saved = m_scope;
                        foreach (Name p in c.Parameters) m_scope = m_scope.Add(p.Value);
                        m_scope = m_scope.Add(c.ResumeName.Value);
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

        protected override void VisitActStatement(ActStatement s)
        {
            switch (s)
            {
                case ActBindStatement bind:
                    Visit(bind.Value);
                    m_scope = m_scope.Add(bind.Name.Value);
                    break;
                case ActExprStatement es:
                    Visit(es.Expression);
                    break;
                default:
                    OnUnknownActStatement(s);
                    break;
            }
        }

        protected override void OnEnter(Expr expr)
        {
            if (expr is NameExpr name
                && !m_scope.Contains(name.Name.Value)
                && !IsTypeReference(name.Name, resolved))
            {
                throw new InvariantViolationException(
                    phase: "name-resolution",
                    invariant: "every NameExpr resolves in its effective scope",
                    detail: $"unbound name '{name.Name.Value}' in definition '{enclosingDef}'",
                    location: name.Span);
            }
        }

        protected override void OnUnknownExpr(Expr expr)
        {
            throw new InvariantViolationException(
                phase: "verifier",
                invariant: "exhaustive expression coverage",
                detail: $"unknown expr kind {expr.GetType().Name} in definition '{enclosingDef}' — verifier needs a new case",
                location: expr.Span);
        }

        protected override void OnUnknownActStatement(ActStatement s)
        {
            throw new InvariantViolationException(
                phase: "verifier",
                invariant: "exhaustive act-statement coverage",
                detail: $"unknown act-statement kind {s.GetType().Name} in definition '{enclosingDef}' — verifier needs a new case",
                location: s.Span);
        }

        static void CollectPatternBindings(Pattern pattern, ref Set<string> scope)
        {
            switch (pattern)
            {
                case VarPattern v:
                    scope = scope.Add(v.Name.Value);
                    break;
                case CtorPattern ctor:
                    foreach (Pattern sub in ctor.SubPatterns)
                        CollectPatternBindings(sub, ref scope);
                    break;
                case WildcardPattern:
                case LiteralPattern:
                    break;
                default:
                    throw new InvariantViolationException(
                        phase: "verifier",
                        invariant: "exhaustive pattern coverage",
                        detail: $"unknown pattern kind {pattern.GetType().Name} — verifier needs a new case",
                        location: pattern.Span);
            }
        }
    }
}
