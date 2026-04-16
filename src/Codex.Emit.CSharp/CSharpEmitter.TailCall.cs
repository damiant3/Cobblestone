using System.Collections.Immutable;
using System.Text;
using Codex.IR;

namespace Codex.Emit.CSharp;

public sealed partial class CSharpEmitter
{
    static bool HasSelfTailCall(IRDefinition def)
    {
        if (def.Parameters.Length == 0)
        {
            return false;
        }

        return ExprHasTailCall(def.Body, def.Name);
    }

    static bool ExprHasTailCall(IRExpr expr, string funcName)
    {
        return expr switch
        {
            IRIf iff => ExprHasTailCall(iff.Then, funcName)
                     || ExprHasTailCall(iff.Else, funcName),
            IRLet let => ExprHasTailCall(let.Body, funcName),
            IRMatch match => match.Branches.Any(b => ExprHasTailCall(b.Body, funcName)),
            IRApply app => IsSelfCall(app, funcName),
            IRRegion region => ExprHasTailCall(region.Body, funcName),
            _ => false
        };
    }

    static bool IsSelfCall(IRApply app, string funcName)
    {
        IRExpr root = app.Function;
        while (root is IRApply inner)
        {
            root = inner.Function;
        }

        return root is IRName name && name.Name == funcName;
    }

    void EmitTailCallDefinition(StringBuilder sb, IRDefinition def)
    {
        string returnType = EmitType(GetReturnType(def));
        string name = SanitizeIdentifier(def.Name);
        string generics = GenericSuffix(def);

        sb.Append($"    public static {returnType} {name}{generics}(");
        for (int i = 0; i < def.Parameters.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }

            IRParameter param = def.Parameters[i];
            sb.Append($"{EmitType(param.Type)} {SanitizeIdentifier(param.Name)}");
        }
        sb.AppendLine(")");
        sb.AppendLine("    {");
        sb.AppendLine("        while (true)");
        sb.AppendLine("        {");

        EmitTailCallBody(sb, def.Body, def.Name, def.Parameters, 3);

        sb.AppendLine("        }");
        sb.AppendLine("    }");
    }

    void EmitTailCallBody(
        StringBuilder sb, IRExpr expr, string funcName,
        ImmutableArray<IRParameter> parameters, int indent)
    {
        string pad = new(' ', indent * 4);

        switch (expr)
        {
            case IRIf iff:
                sb.Append($"{pad}if (");
                EmitExpr(sb, iff.Condition, indent);
                sb.AppendLine(")");
                sb.AppendLine($"{pad}{{");
                EmitTailCallBody(sb, iff.Then, funcName, parameters, indent + 1);
                sb.AppendLine($"{pad}}}");
                sb.AppendLine($"{pad}else");
                sb.AppendLine($"{pad}{{");
                EmitTailCallBody(sb, iff.Else, funcName, parameters, indent + 1);
                sb.AppendLine($"{pad}}}");
                break;

            case IRLet let:
                sb.Append($"{pad}var {SanitizeIdentifier(let.Name)} = ");
                EmitExpr(sb, let.Value, indent);
                sb.AppendLine(";");
                EmitTailCallBody(sb, let.Body, funcName, parameters, indent);
                break;

            case IRRegion region:
                EmitTailCallBody(sb, region.Body, funcName, parameters, indent);
                break;

            case IRMatch match:
                EmitTailCallMatch(sb, match, funcName, parameters, indent);
                break;

            case IRApply app when IsSelfCall(app, funcName):
                EmitTailCallJump(sb, app, parameters, indent);
                break;

            default:
                sb.Append($"{pad}return ");
                EmitExpr(sb, expr, indent);
                sb.AppendLine(";");
                break;
        }
    }

    void EmitTailCallMatch(
        StringBuilder sb, IRMatch match, string funcName,
        ImmutableArray<IRParameter> parameters, int indent)
    {
        string pad = new(' ', indent * 4);
        string scrutineeVar = "_tco_s";
        sb.Append($"{pad}var {scrutineeVar} = ");
        EmitExpr(sb, match.Scrutinee, indent);
        sb.AppendLine(";");

        bool first = true;
        int branchIdx = 0;
        foreach (IRMatchBranch branch in match.Branches)
        {
            string keyword = first ? "if" : "else if";
            first = false;
            string matchVar = $"_tco_m{branchIdx}";
            branchIdx++;

            switch (branch.Pattern)
            {
                case IRWildcardPattern:
                case IRVarPattern:
                    sb.AppendLine($"{pad}{{");
                    if (branch.Pattern is IRVarPattern vp)
                    {
                        sb.AppendLine($"{pad}    var {SanitizeIdentifier(vp.Name)} = {scrutineeVar};");
                    }

                    EmitTailCallBody(sb, branch.Body, funcName, parameters, indent + 1);
                    sb.AppendLine($"{pad}}}");
                    break;

                case IRCtorPattern ctorPat:
                    sb.AppendLine($"{pad}{keyword} ({scrutineeVar} is {SanitizeIdentifier(ctorPat.Name)}{CtorTypeArgs(match.Scrutinee.Type)} {matchVar})");
                    sb.AppendLine($"{pad}{{");
                    for (int i = 0; i < ctorPat.SubPatterns.Length; i++)
                    {
                        if (ctorPat.SubPatterns[i] is IRVarPattern svp)
                        {
                            sb.AppendLine($"{pad}    var {SanitizeIdentifier(svp.Name)} = {matchVar}.Field{i};");
                        }
                    }
                    EmitTailCallBody(sb, branch.Body, funcName, parameters, indent + 1);
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
                    EmitTailCallBody(sb, branch.Body, funcName, parameters, indent + 1);
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
