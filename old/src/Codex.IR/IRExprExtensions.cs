namespace Codex.IR;

// Expression-level helpers shared by every emit backend that does tail-call
// detection. The recursion shape is identical across targets — only the
// emitted code for the self-call differs.
public static class IRExprExtensions
{
    // True if any tail position in this expression is a self-call to funcName.
    // Tail positions follow the standard TCO structure:
    //   IRIf: both branches' bodies
    //   IRLet: the body (not the binding)
    //   IRMatch: any match branch's body
    //   IRAct: the last statement, when it is an IRActExec's expression
    //   IRApply: recurse via IsSelfCall; non-self-calls end the tail chain.
    // All other expression shapes are non-tail by default.
    public static bool HasTailCall(this IRExpr expr, string funcName) => expr switch
    {
        IRIf iff => iff.Then.HasTailCall(funcName) || iff.Else.HasTailCall(funcName),
        IRLet let => let.Body.HasTailCall(funcName),
        IRMatch match => match.Branches.Any(b => b.Body.HasTailCall(funcName)),
        IRAct actExpr => actExpr.Statements.Length > 0
            && actExpr.Statements[^1] is IRActExec exec
            && exec.Expression.HasTailCall(funcName),
        IRApply app => app.IsSelfCall(funcName),
        _ => false
    };

    // True if the function at the head of a (possibly curried) application
    // chain is a bare IRName equal to funcName.
    public static bool IsSelfCall(this IRExpr expr, string funcName)
    {
        IRExpr current = expr;
        while (current is IRApply app)
            current = app.Function;
        return current is IRName name && name.Name == funcName;
    }
}
