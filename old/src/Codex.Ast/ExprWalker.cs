namespace Codex.Ast;

// Structural walker over Expr trees (void-returning). Default Visit recurses
// into every child expression; subclasses hook OnEnter for per-node checks and
// override Visit for scope-modifying nodes (LambdaExpr, LetExpr, MatchExpr,
// ActExpr, HandleExpr) where the subclass needs to extend a scope field
// around the children.
//
// Exhaustiveness: OnUnknownExpr and OnUnknownActStatement are called when a
// new variant lands that the base's switch doesn't cover. Subclasses can
// override to throw, preserving the "new variant forces an audit" invariant
// that every verifier used to enforce with its own default-throw arm.
public abstract class ExprWalker
{
    public virtual void Visit(Expr expr)
    {
        OnEnter(expr);
        switch (expr)
        {
            case LiteralExpr:
            case NameExpr:
            case ErrorExpr:
                break;
            case BinaryExpr bin:
                Visit(bin.Left);
                Visit(bin.Right);
                break;
            case UnaryExpr un:
                Visit(un.Operand);
                break;
            case ApplyExpr app:
                Visit(app.Function);
                Visit(app.Argument);
                break;
            case IfExpr iff:
                Visit(iff.Condition);
                Visit(iff.Then);
                Visit(iff.Else);
                break;
            case LetExpr let:
                foreach (LetBinding b in let.Bindings) Visit(b.Value);
                Visit(let.Body);
                break;
            case LambdaExpr lam:
                Visit(lam.Body);
                break;
            case MatchExpr m:
                Visit(m.Scrutinee);
                foreach (MatchBranch br in m.Branches) Visit(br.Body);
                break;
            case ListExpr list:
                foreach (Expr el in list.Elements) Visit(el);
                break;
            case RecordExpr rec:
                foreach (RecordFieldExpr f in rec.Fields) Visit(f.Value);
                break;
            case FieldAccessExpr fa:
                Visit(fa.Record);
                break;
            case ActExpr act:
                foreach (ActStatement s in act.Statements) VisitActStatement(s);
                break;
            case HandleExpr h:
                Visit(h.Computation);
                foreach (HandleClause c in h.Clauses) Visit(c.Body);
                break;
            default:
                OnUnknownExpr(expr);
                break;
        }
    }

    protected virtual void VisitActStatement(ActStatement s)
    {
        switch (s)
        {
            case ActBindStatement bind:
                Visit(bind.Value);
                break;
            case ActExprStatement es:
                Visit(es.Expression);
                break;
            default:
                OnUnknownActStatement(s);
                break;
        }
    }

    protected virtual void OnEnter(Expr expr) { }
    protected virtual void OnUnknownExpr(Expr expr) { }
    protected virtual void OnUnknownActStatement(ActStatement s) { }
}
