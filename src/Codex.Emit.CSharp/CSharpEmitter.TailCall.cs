using System.Collections.Immutable;
using System.Text;
using Codex.IR;

namespace Codex.Emit.CSharp;

public sealed partial class CSharpEmitter
{
    static bool HasSelfTailCall(IRDefinition def)
    {
        if (def.Parameters.Length == 0)
            return false;
        return def.Body.HasTailCall(def.Name);
    }

    void EmitTailCallDefinition(StringBuilder sb, IRDefinition def)
    {
        string returnType = EmitType(def.PureReturnType());
        string name = SanitizeIdentifier(def.Name);
        string generics = GenericSuffix(def);

        sb.Append($"    public static {returnType} {name}{generics}(");
        for (int i = 0; i < def.Parameters.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            IRParameter param = def.Parameters[i];
            sb.Append($"{EmitType(param.Type)} {SanitizeIdentifier(param.Name)}");
        }
        sb.AppendLine(")");
        sb.AppendLine("    {");
        sb.AppendLine("        while (true)");
        sb.AppendLine("        {");

        EmitTailCallBody(sb, def.Body, def.Name, def.Parameters, 3, 0);

        sb.AppendLine("        }");
        sb.AppendLine("    }");
    }

    void EmitTailCallBody(
        StringBuilder sb, IRExpr expr, string funcName,
        ImmutableArray<IRParameter> parameters, int indent, int matchDepth)
    {
        string pad = new(' ', indent * 4);

        switch (expr)
        {
            case IRIf iff:
                sb.Append($"{pad}if (");
                EmitExpr(sb, iff.Condition, indent);
                sb.AppendLine(")");
                sb.AppendLine($"{pad}{{");
                EmitTailCallBody(sb, iff.Then, funcName, parameters, indent + 1, matchDepth);
                sb.AppendLine($"{pad}}}");
                sb.AppendLine($"{pad}else");
                sb.AppendLine($"{pad}{{");
                EmitTailCallBody(sb, iff.Else, funcName, parameters, indent + 1, matchDepth);
                sb.AppendLine($"{pad}}}");
                break;

            case IRLet let:
                sb.Append($"{pad}var {SanitizeIdentifier(let.Name)} = ");
                EmitExpr(sb, let.Value, indent);
                sb.AppendLine(";");
                EmitTailCallBody(sb, let.Body, funcName, parameters, indent, matchDepth);
                break;

            case IRMatch match:
                EmitTailCallMatch(sb, match, funcName, parameters, indent, matchDepth);
                break;

            case IRApply app when app.IsSelfCall(funcName):
                EmitTailCallJump(sb, app, parameters, indent);
                break;

            default:
                sb.Append($"{pad}return ");
                EmitExpr(sb, expr, indent);
                sb.AppendLine(";");
                break;
        }
    }

    // Scrutinee/match-binding vars are suffixed by nesting depth so that
    // nested when-expressions in the same C# method don't shadow each
    // other's `_tco_s` / `_tco_m*`. Depth 0 keeps the historical unsuffixed
    // names to minimise diff churn for single-match TCO functions.
    static string TcoScrutVar(int depth) =>
        depth == 0 ? "_tco_s" : $"_tco_s{depth}";

    static string TcoMatchVar(int depth, int idx) =>
        depth == 0 ? $"_tco_m{idx}" : $"_tco_m{depth}_{idx}";

    void EmitTailCallMatch(
        StringBuilder sb, IRMatch match, string funcName,
        ImmutableArray<IRParameter> parameters, int indent, int matchDepth)
    {
        string pad = new(' ', indent * 4);
        string scrutineeVar = TcoScrutVar(matchDepth);
        sb.Append($"{pad}var {scrutineeVar} = ");
        EmitExpr(sb, match.Scrutinee, indent);
        sb.AppendLine(";");

        bool first = true;
        int branchIdx = 0;
        foreach (IRMatchBranch branch in match.Branches)
        {
            string keyword = first ? "if" : "else if";
            first = false;
            string matchVar = TcoMatchVar(matchDepth, branchIdx);
            branchIdx++;

            switch (branch.Pattern)
            {
                case IRWildcardPattern:
                case IRVarPattern:
                    sb.AppendLine($"{pad}{{");
                    if (branch.Pattern is IRVarPattern vp)
                        sb.AppendLine($"{pad}    var {SanitizeIdentifier(vp.Name)} = {scrutineeVar};");
                    EmitTailCallBody(sb, branch.Body, funcName, parameters, indent + 1, matchDepth + 1);
                    sb.AppendLine($"{pad}}}");
                    break;

                case IRCtorPattern ctorPat:
                    sb.AppendLine($"{pad}{keyword} ({scrutineeVar} is {SanitizeIdentifier(ctorPat.Name)}{CtorTypeArgs(match.Scrutinee.Type)} {matchVar})");
                    sb.AppendLine($"{pad}{{");
                    for (int i = 0; i < ctorPat.SubPatterns.Length; i++)
                    {
                        if (ctorPat.SubPatterns[i] is IRVarPattern svp)
                            sb.AppendLine($"{pad}    var {SanitizeIdentifier(svp.Name)} = {matchVar}.Field{i};");
                    }
                    EmitTailCallBody(sb, branch.Body, funcName, parameters, indent + 1, matchDepth + 1);
                    sb.AppendLine($"{pad}}}");
                    break;

                case IRLiteralPattern litPat:
                    string litVal = litPat.Value switch
                    {
                        bool b => b ? "true" : "false",
                        long l => $"{l}L",
                        string s => $"\"{EscapeString(s)}\"",
                        _ => litPat.Value.ToString()!
                    };
                    sb.AppendLine($"{pad}{keyword} (object.Equals({scrutineeVar}, {litVal}))");
                    sb.AppendLine($"{pad}{{");
                    EmitTailCallBody(sb, branch.Body, funcName, parameters, indent + 1, matchDepth + 1);
                    sb.AppendLine($"{pad}}}");
                    break;
            }
        }
    }

    void EmitTailCallJump(
        StringBuilder sb, IRApply app,
        ImmutableArray<IRParameter> parameters, int indent)
    {
        string pad = new(' ', indent * 4);

        List<IRExpr> args = [];
        CollectApplyArgs(app, args);

        for (int i = 0; i < args.Count && i < parameters.Length; i++)
        {
            sb.Append($"{pad}var _tco_{i} = ");
            EmitExpr(sb, args[i], indent);
            sb.AppendLine(";");
        }
        for (int i = 0; i < args.Count && i < parameters.Length; i++)
        {
            sb.AppendLine($"{pad}{SanitizeIdentifier(parameters[i].Name)} = _tco_{i};");
        }
        sb.AppendLine($"{pad}continue;");
    }
}
