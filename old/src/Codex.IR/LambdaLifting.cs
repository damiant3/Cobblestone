using System.Collections.Immutable;
using Codex.Core;
using Codex.Types;

namespace Codex.IR;

// Rewrites every `IRLambda` expression into an `IRName` reference to a newly-
// synthesized top-level `IRDefinition`, with any free variables partially
// applied. Runs after IR lowering, before emission.
//
// Why: X86_64CodeGen has no direct IRLambda handler — native codegen can't
// emit an anonymous function at an arbitrary point in an expression. The
// other emitters (C#, Java, IL, etc.) get first-class closures from their
// host platform. For x86-64 we lift to a real function + closure pair that
// EmitPartialApplication already knows how to handle.
//
// Example:
//   make-adder (n) = \x -> x + n
// becomes:
//   __lam_0 (n, x) = x + n
//   make-adder (n) = __lam_0 n          -- partial application, returns closure
//
// The caller site `(make-adder 10) 5` still works — after lifting, it's an
// indirect call through the closure that `__lam_0 n` produced.
public static class LambdaLifting
{
    public static IRChapter Lift(IRChapter chapter)
    {
        // Scan all pre-existing def names (including cited ones) so we don't
        // collide with something the user wrote.
        HashSet<string> reserved = new();
        foreach (IRDefinition def in chapter.Definitions)
            reserved.Add(def.Name);
        foreach (IRChapterSection sec in chapter.Sections)
        {
            foreach (IRDefinition def in sec.Definitions)
                reserved.Add(def.Name);
        }

        Context ctx = new(reserved);
        ImmutableArray<IRDefinition>.Builder newDefs = ImmutableArray.CreateBuilder<IRDefinition>();

        foreach (IRDefinition def in chapter.Definitions)
        {
            // Eta-like absorption: `f = \x -> body` turns into `f (x) = body`. Without this,
            // f becomes a zero-arg function returning a closure and every call site has to
            // over-apply through a trampoline that wasn't worth emitting.
            IRDefinition flat = AbsorbOuterLambdas(def);

            HashSet<string> enclosing = new();
            foreach (IRParameter p in flat.Parameters)
                enclosing.Add(p.Name);
            IRExpr rewritten = ctx.Transform(flat.Body, enclosing);
            newDefs.Add(flat with { Body = rewritten });
        }

        newDefs.AddRange(ctx.Lifted);

        // Rewrite sections too — sections contain per-source-chapter groupings
        // of the same defs. Keep them in lockstep.
        ImmutableArray<IRChapterSection>.Builder newSections = ImmutableArray.CreateBuilder<IRChapterSection>();
        Dictionary<string, IRDefinition> bodyByName = new();
        foreach (IRDefinition def in newDefs)
            bodyByName[def.Name] = def;
        foreach (IRChapterSection sec in chapter.Sections)
        {
            ImmutableArray<IRDefinition>.Builder secDefs = ImmutableArray.CreateBuilder<IRDefinition>();
            foreach (IRDefinition def in sec.Definitions)
                secDefs.Add(bodyByName.TryGetValue(def.Name, out IRDefinition? updated) ? updated : def);
            newSections.Add(sec with { Definitions = secDefs.ToImmutable() });
        }

        return chapter with
        {
            Definitions = newDefs.ToImmutable(),
            Sections = newSections.ToImmutable(),
        };
    }

    static IRDefinition AbsorbOuterLambdas(IRDefinition def)
    {
        ImmutableArray<IRParameter>.Builder parameters = ImmutableArray.CreateBuilder<IRParameter>();
        parameters.AddRange(def.Parameters);
        IRExpr body = def.Body;
        while (body is IRLambda lam)
        {
            parameters.AddRange(lam.Parameters);
            body = lam.Body;
        }
        return def with { Parameters = parameters.ToImmutable(), Body = body };
    }

    sealed class Context
    {
        readonly HashSet<string> m_reserved;
        public readonly List<IRDefinition> Lifted = [];

        public Context(HashSet<string> reserved)
        {
            m_reserved = reserved;
        }

        public IRExpr Transform(IRExpr expr, HashSet<string> enclosing)
        {
            switch (expr)
            {
                case IRLambda lam:
                    return LiftLambda(lam, enclosing);

                case IRApply app:
                    return new IRApply(
                        Transform(app.Function, enclosing),
                        Transform(app.Argument, enclosing),
                        app.Type);

                case IRBinary bin:
                    return new IRBinary(bin.Op,
                        Transform(bin.Left, enclosing),
                        Transform(bin.Right, enclosing),
                        bin.Type);

                case IRNegate neg:
                    return new IRNegate(Transform(neg.Operand, enclosing));

                case IRIf iff:
                    return new IRIf(
                        Transform(iff.Condition, enclosing),
                        Transform(iff.Then, enclosing),
                        Transform(iff.Else, enclosing),
                        iff.Type);

                case IRLet let:
                    IRExpr letValue = Transform(let.Value, enclosing);
                    HashSet<string> letScope = new(enclosing) { let.Name };
                    IRExpr letBody = Transform(let.Body, letScope);
                    return new IRLet(let.Name, let.NameType, letValue, letBody);

                case IRMatch match:
                    ImmutableArray<IRMatchBranch>.Builder branches = ImmutableArray.CreateBuilder<IRMatchBranch>();
                    foreach (IRMatchBranch b in match.Branches)
                    {
                        HashSet<string> branchScope = new(enclosing);
                        CollectPatternVars(b.Pattern, branchScope);
                        branches.Add(new IRMatchBranch(b.Pattern, Transform(b.Body, branchScope)));
                    }
                    return new IRMatch(
                        Transform(match.Scrutinee, enclosing),
                        branches.ToImmutable(),
                        match.Type);

                case IRList list:
                    ImmutableArray<IRExpr>.Builder elems = ImmutableArray.CreateBuilder<IRExpr>();
                    foreach (IRExpr e in list.Elements)
                        elems.Add(Transform(e, enclosing));
                    return new IRList(elems.ToImmutable(), list.ElementType);

                case IRRecord rec:
                    ImmutableArray<(string, IRExpr)>.Builder fields = ImmutableArray.CreateBuilder<(string, IRExpr)>();
                    foreach ((string fieldName, IRExpr fieldVal) in rec.Fields)
                        fields.Add((fieldName, Transform(fieldVal, enclosing)));
                    return new IRRecord(rec.TypeName, fields.ToImmutable(), rec.Type);

                case IRFieldAccess fa:
                    return new IRFieldAccess(Transform(fa.Record, enclosing), fa.FieldName, fa.Type);

                case IRAct actExpr:
                    ImmutableArray<IRActStatement>.Builder stmts = ImmutableArray.CreateBuilder<IRActStatement>();
                    HashSet<string> actScope = new(enclosing);
                    foreach (IRActStatement s in actExpr.Statements)
                    {
                        switch (s)
                        {
                            case IRActExec exec:
                                stmts.Add(new IRActExec(Transform(exec.Expression, actScope)));
                                break;
                            case IRActBind bind:
                                IRExpr bVal = Transform(bind.Value, actScope);
                                stmts.Add(new IRActBind(bind.Name, bind.NameType, bVal));
                                actScope.Add(bind.Name);
                                break;
                        }
                    }
                    return new IRAct(stmts.ToImmutable(), actExpr.Type);

                // Leaves (no subexpressions) and opaque node types pass through.
                default:
                    return expr;
            }
        }

        IRExpr LiftLambda(IRLambda lam, HashSet<string> enclosing)
        {
            // First, recurse into the body so nested lambdas get lifted too.
            HashSet<string> bodyScope = new(enclosing);
            foreach (IRParameter p in lam.Parameters)
                bodyScope.Add(p.Name);
            IRExpr transformedBody = Transform(lam.Body, bodyScope);

            // Collect free variables: IRName references in the body that are in
            // enclosing scope but NOT bound by lambda params. Top-level names
            // and builtins aren't captured — they're globally addressable.
            HashSet<string> paramNames = [];
            foreach (IRParameter p in lam.Parameters)
                paramNames.Add(p.Name);
            Dictionary<string, CodexType> freeVarTypes = new();
            CollectFreeVarTypes(transformedBody, paramNames, enclosing, freeVarTypes);

            // Deterministic capture order — sorted by name so later runs match.
            List<string> captures = new(freeVarTypes.Keys);
            captures.Sort(StringComparer.Ordinal);


            // Synthesize a unique name.
            string baseName = "__lam";
            int i = 0;
            string name;
            do
            {
                name = $"{baseName}_{i}";
                i++;
            } while (m_reserved.Contains(name));
            m_reserved.Add(name);

            // Build the lifted function's parameter list: captures first, then the
            // lambda's own params. The trampoline EmitPartialApplication generates
            // later relies on this exact ordering.
            ImmutableArray<IRParameter>.Builder liftedParams = ImmutableArray.CreateBuilder<IRParameter>();
            foreach (string cap in captures)
                liftedParams.Add(new IRParameter(cap, freeVarTypes[cap]));
            foreach (IRParameter p in lam.Parameters)
                liftedParams.Add(p);

            // Lifted function's full type: cap0 -> cap1 -> ... -> lamParam0 -> ... -> lamReturn.
            CodexType returnType = InferLambdaReturnType(lam.Type, lam.Parameters.Length);
            CodexType liftedType = BuildFunctionType(liftedParams, returnType);

            Lifted.Add(new IRDefinition(
                name,
                liftedParams.ToImmutable(),
                liftedType,
                transformedBody));

            // Replacement: IRApply chain that partially applies each capture.
            // Example with 2 captures: IRApply(IRApply(IRName lifted, cap0), cap1)
            // Type narrows at each step from full_fn down to lam.Type.
            IRExpr result = new IRName(name, liftedType);
            CodexType currentType = liftedType;
            foreach (string cap in captures)
            {
                // Advance type by one parameter.
                currentType = currentType is FunctionType ft ? ft.Return : currentType;
                result = new IRApply(result, new IRName(cap, freeVarTypes[cap]), currentType);
            }

            return result;
        }

        static void CollectFreeVarTypes(
            IRExpr expr,
            HashSet<string> localBound,
            HashSet<string> capturable,
            Dictionary<string, CodexType> acc)
        {
            switch (expr)
            {
                case IRName name:
                    if (!localBound.Contains(name.Name)
                        && capturable.Contains(name.Name)
                        && !acc.ContainsKey(name.Name))
                    {
                        acc[name.Name] = name.Type;
                    }
                    break;

                case IRLambda innerLam:
                    HashSet<string> lamBound = new(localBound);
                    foreach (IRParameter p in innerLam.Parameters)
                        lamBound.Add(p.Name);
                    CollectFreeVarTypes(innerLam.Body, lamBound, capturable, acc);
                    break;

                case IRApply app:
                    CollectFreeVarTypes(app.Function, localBound, capturable, acc);
                    CollectFreeVarTypes(app.Argument, localBound, capturable, acc);
                    break;

                case IRBinary bin:
                    CollectFreeVarTypes(bin.Left, localBound, capturable, acc);
                    CollectFreeVarTypes(bin.Right, localBound, capturable, acc);
                    break;

                case IRNegate neg:
                    CollectFreeVarTypes(neg.Operand, localBound, capturable, acc);
                    break;

                case IRIf iff:
                    CollectFreeVarTypes(iff.Condition, localBound, capturable, acc);
                    CollectFreeVarTypes(iff.Then, localBound, capturable, acc);
                    CollectFreeVarTypes(iff.Else, localBound, capturable, acc);
                    break;

                case IRLet let:
                    CollectFreeVarTypes(let.Value, localBound, capturable, acc);
                    HashSet<string> letBound = new(localBound) { let.Name };
                    CollectFreeVarTypes(let.Body, letBound, capturable, acc);
                    break;

                case IRMatch match:
                    CollectFreeVarTypes(match.Scrutinee, localBound, capturable, acc);
                    foreach (IRMatchBranch b in match.Branches)
                    {
                        HashSet<string> branchBound = new(localBound);
                        CollectPatternVars(b.Pattern, branchBound);
                        CollectFreeVarTypes(b.Body, branchBound, capturable, acc);
                    }
                    break;

                case IRList list:
                    foreach (IRExpr e in list.Elements)
                        CollectFreeVarTypes(e, localBound, capturable, acc);
                    break;

                case IRRecord rec:
                    foreach ((string _, IRExpr fieldVal) in rec.Fields)
                        CollectFreeVarTypes(fieldVal, localBound, capturable, acc);
                    break;

                case IRFieldAccess fa:
                    CollectFreeVarTypes(fa.Record, localBound, capturable, acc);
                    break;

                case IRAct actExpr:
                    HashSet<string> actBound = new(localBound);
                    foreach (IRActStatement s in actExpr.Statements)
                    {
                        switch (s)
                        {
                            case IRActExec exec:
                                CollectFreeVarTypes(exec.Expression, actBound, capturable, acc);
                                break;
                            case IRActBind bind:
                                CollectFreeVarTypes(bind.Value, actBound, capturable, acc);
                                actBound.Add(bind.Name);
                                break;
                        }
                    }
                    break;
            }
        }

        static void CollectPatternVars(IRPattern pat, HashSet<string> acc)
        {
            switch (pat)
            {
                case IRVarPattern v:
                    acc.Add(v.Name);
                    break;
                case IRCtorPattern ctor:
                    foreach (IRPattern sub in ctor.SubPatterns)
                        CollectPatternVars(sub, acc);
                    break;
            }
        }

        static CodexType InferLambdaReturnType(CodexType lamType, int numParams)
        {
            CodexType t = lamType;
            for (int i = 0; i < numParams; i++)
            {
                if (t is FunctionType ft)
                    t = ft.Return;
                else
                    break;
            }
            return t;
        }

        static CodexType BuildFunctionType(
            ImmutableArray<IRParameter>.Builder parameters, CodexType returnType)
        {
            CodexType result = returnType;
            for (int i = parameters.Count - 1; i >= 0; i--)
                result = new FunctionType(parameters[i].Type, result);
            return result;
        }
    }
}
