namespace Codex.IR;

// Structural walker over IRExpr trees (void-returning). Counterpart to
// Codex.Ast.ExprWalker for the post-lowering tree. Same pattern: OnEnter
// hook per node, VisitActStatement for statement-shaped children, override
// Visit on scope-modifying nodes (IRLambda, IRLet, IRMatch, IRAct, IRHandle)
// when the subclass tracks a scope field.
public abstract class IRExprWalker
{
    public virtual void Visit(IRExpr expr)
    {
        OnEnter(expr);
        switch (expr)
        {
            case IRIntegerLit:
            case IRNumberLit:
            case IRTextLit:
            case IRBoolLit:
            case IRCharLit:
            case IRError:
            case IRGetState:
            case IRName:
                break;
            case IRBinary bin:
                Visit(bin.Left);
                Visit(bin.Right);
                break;
            case IRNegate neg:
                Visit(neg.Operand);
                break;
            case IRIf iff:
                Visit(iff.Condition);
                Visit(iff.Then);
                Visit(iff.Else);
                break;
            case IRLet let:
                Visit(let.Value);
                Visit(let.Body);
                break;
            case IRApply app:
                Visit(app.Function);
                Visit(app.Argument);
                break;
            case IRLambda lam:
                Visit(lam.Body);
                break;
            case IRList list:
                foreach (IRExpr e in list.Elements) Visit(e);
                break;
            case IRMatch m:
                Visit(m.Scrutinee);
                foreach (IRMatchBranch br in m.Branches) Visit(br.Body);
                break;
            case IRRecord rec:
                foreach ((string _, IRExpr value) in rec.Fields) Visit(value);
                break;
            case IRFieldAccess fa:
                Visit(fa.Record);
                break;
            case IRAct act:
                foreach (IRActStatement s in act.Statements) VisitActStatement(s);
                break;
            case IRRunState rs:
                Visit(rs.InitialState);
                Visit(rs.Computation);
                break;
            case IRSetState ss:
                Visit(ss.NewValue);
                break;
            case IRHandle h:
                Visit(h.Computation);
                foreach (IRHandleClause c in h.Clauses) Visit(c.Body);
                break;
            default:
                OnUnknownExpr(expr);
                break;
        }
    }

    protected virtual void VisitActStatement(IRActStatement s)
    {
        switch (s)
        {
            case IRActBind bind:
                Visit(bind.Value);
                break;
            case IRActExec exec:
                Visit(exec.Expression);
                break;
            default:
                OnUnknownActStatement(s);
                break;
        }
    }

    protected virtual void OnEnter(IRExpr expr) { }
    protected virtual void OnUnknownExpr(IRExpr expr) { }
    protected virtual void OnUnknownActStatement(IRActStatement s) { }
}
