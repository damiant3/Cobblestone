using System.Text;

namespace Codex.IR;

// Shared dispatch skeleton for text-output backends. Each backend inherits
// and overrides the variants it handles; unhandled variants fall through to
// EmitUnhandled (per-backend fallback token — "default", "None", "{- unhandled -}").
// Fuel check lives here so every backend gets CDX9001-equivalent behavior
// without re-implementing the counter.
//
// Scope: text emitters whose EmitExpr signature is (StringBuilder, IRExpr, int).
// Bytecode emitters (IL via InstructionEncoder, Wasm via MemoryStream) and
// register-allocating backends (X86_64/Arm64/RiscV) have incompatible shapes
// and stay hand-rolled.
public abstract class IRExprTextEmitter
{
    protected const int MaxEmitDepth = 256;
    int m_emitDepth;
    bool m_emitFuelExhausted;

    public bool EmitFuelExhausted => m_emitFuelExhausted;
    public int MaxEmitDepthLimit => MaxEmitDepth;

    protected void EmitExpr(StringBuilder sb, IRExpr expr, int indent)
    {
        if (m_emitDepth >= MaxEmitDepth)
        {
            m_emitFuelExhausted = true;
            EmitFuelExhaustedToken(sb);
            return;
        }
        m_emitDepth++;
        try
        {
            switch (expr)
            {
                case IRIntegerLit lit: EmitIntegerLit(sb, lit, indent); break;
                case IRNumberLit lit: EmitNumberLit(sb, lit, indent); break;
                case IRTextLit lit: EmitTextLit(sb, lit, indent); break;
                case IRBoolLit lit: EmitBoolLit(sb, lit, indent); break;
                case IRCharLit lit: EmitCharLit(sb, lit, indent); break;
                case IRName name: EmitName(sb, name, indent); break;
                case IRBinary bin: EmitBinary(sb, bin, indent); break;
                case IRNegate neg: EmitNegate(sb, neg, indent); break;
                case IRIf iff: EmitIf(sb, iff, indent); break;
                case IRLet let: EmitLet(sb, let, indent); break;
                case IRApply app: EmitApply(sb, app, indent); break;
                case IRLambda lam: EmitLambda(sb, lam, indent); break;
                case IRList list: EmitList(sb, list, indent); break;
                case IRMatch match: EmitMatch(sb, match, indent); break;
                case IRAct act: EmitAct(sb, act, indent); break;
                case IRRecord rec: EmitRecord(sb, rec, indent); break;
                case IRFieldAccess fa: EmitFieldAccess(sb, fa, indent); break;
                case IRGetState get: EmitGetState(sb, get, indent); break;
                case IRSetState set: EmitSetState(sb, set, indent); break;
                case IRRunState run: EmitRunState(sb, run, indent); break;
                case IRHandle handle: EmitHandle(sb, handle, indent); break;
                case IRError err: EmitError(sb, err, indent); break;
                default: EmitUnhandled(sb, expr, indent); break;
            }
        }
        finally
        {
            m_emitDepth--;
        }
    }

    protected virtual void EmitIntegerLit(StringBuilder sb, IRIntegerLit lit, int indent) => EmitUnhandled(sb, lit, indent);
    protected virtual void EmitNumberLit(StringBuilder sb, IRNumberLit lit, int indent) => EmitUnhandled(sb, lit, indent);
    protected virtual void EmitTextLit(StringBuilder sb, IRTextLit lit, int indent) => EmitUnhandled(sb, lit, indent);
    protected virtual void EmitBoolLit(StringBuilder sb, IRBoolLit lit, int indent) => EmitUnhandled(sb, lit, indent);
    protected virtual void EmitCharLit(StringBuilder sb, IRCharLit lit, int indent) => EmitUnhandled(sb, lit, indent);
    protected virtual void EmitName(StringBuilder sb, IRName name, int indent) => EmitUnhandled(sb, name, indent);
    protected virtual void EmitBinary(StringBuilder sb, IRBinary bin, int indent) => EmitUnhandled(sb, bin, indent);
    protected virtual void EmitNegate(StringBuilder sb, IRNegate neg, int indent) => EmitUnhandled(sb, neg, indent);
    protected virtual void EmitIf(StringBuilder sb, IRIf iff, int indent) => EmitUnhandled(sb, iff, indent);
    protected virtual void EmitLet(StringBuilder sb, IRLet let, int indent) => EmitUnhandled(sb, let, indent);
    protected virtual void EmitApply(StringBuilder sb, IRApply app, int indent) => EmitUnhandled(sb, app, indent);
    protected virtual void EmitLambda(StringBuilder sb, IRLambda lam, int indent) => EmitUnhandled(sb, lam, indent);
    protected virtual void EmitList(StringBuilder sb, IRList list, int indent) => EmitUnhandled(sb, list, indent);
    protected virtual void EmitMatch(StringBuilder sb, IRMatch match, int indent) => EmitUnhandled(sb, match, indent);
    protected virtual void EmitAct(StringBuilder sb, IRAct act, int indent) => EmitUnhandled(sb, act, indent);
    protected virtual void EmitRecord(StringBuilder sb, IRRecord rec, int indent) => EmitUnhandled(sb, rec, indent);
    protected virtual void EmitFieldAccess(StringBuilder sb, IRFieldAccess fa, int indent) => EmitUnhandled(sb, fa, indent);
    protected virtual void EmitGetState(StringBuilder sb, IRGetState get, int indent) => EmitUnhandled(sb, get, indent);
    protected virtual void EmitSetState(StringBuilder sb, IRSetState set, int indent) => EmitUnhandled(sb, set, indent);
    protected virtual void EmitRunState(StringBuilder sb, IRRunState run, int indent) => EmitUnhandled(sb, run, indent);
    protected virtual void EmitHandle(StringBuilder sb, IRHandle handle, int indent) => EmitUnhandled(sb, handle, indent);
    protected virtual void EmitError(StringBuilder sb, IRError err, int indent) => EmitUnhandled(sb, err, indent);

    protected abstract void EmitUnhandled(StringBuilder sb, IRExpr expr, int indent);
    protected abstract void EmitFuelExhaustedToken(StringBuilder sb);
}
