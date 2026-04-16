using System.Text;
using Codex.Core;
using Codex.IR;
using Codex.Types;

namespace Codex.Emit.CSharp;

public sealed partial class CSharpEmitter
{
    void EmitMatch(StringBuilder sb, IRMatch match, int indent)
    {
        int matchId = m_matchCounter++;
        bool hasMultipleCtorBranches = match.Branches
            .Count(b => b.Pattern is IRCtorPattern or IRLiteralPattern) > 1;

        string scrutineeRef;
        if (hasMultipleCtorBranches)
        {
            string scrutineeType = EmitType(match.Scrutinee.Type);
            sb.Append($"((Func<{scrutineeType}, {EmitType(match.Type)}>)((_scrutinee{matchId}_) => ");
            scrutineeRef = $"_scrutinee{matchId}_";
        }
        else
        {
            scrutineeRef = "";
        }

        bool first = true;
        int openParens = 0;
        foreach (IRMatchBranch branch in match.Branches)
        {
            if (!first)
            {
                sb.Append(" : ");
            }

            first = false;

            switch (branch.Pattern)
            {
                case IRLiteralPattern litPat:
                    sb.Append('(');
                    openParens++;
                    if (hasMultipleCtorBranches)
                    {
                        sb.Append(scrutineeRef);
                    }
                    else
                    {
                        EmitExpr(sb, match.Scrutinee, indent);
                    }

                    sb.Append(litPat.Value switch
                    {
                        bool b => $".Equals({(b ? "true" : "false")})",
                        long l => $" == {l}L",
                        string s => $" == \"{EscapeString(s)}\"",
                        _ => $".Equals({litPat.Value})"
                    });
                    sb.Append(" ? ");
                    EmitExpr(sb, branch.Body, indent);
                    break;

                case IRCtorPattern ctorPat:
                    string ctorId = SanitizeIdentifier(ctorPat.Name);
                    string binding = $"_m{ctorId}{matchId}_";
                    sb.Append('(');
                    openParens++;
                    if (hasMultipleCtorBranches)
                    {
                        sb.Append(scrutineeRef);
                    }
                    else
                    {
                        EmitExpr(sb, match.Scrutinee, indent);
                    }

                    sb.Append($" is {ctorId} {binding} ? ");
                    EmitCtorPatternBody(sb, ctorPat, binding, branch.Body, indent);
                    break;

                case IRVarPattern varPat:
                    string varFuncType = $"Func<{EmitType(varPat.Type)}, {EmitType(branch.Body.Type)}>";
                    sb.Append("((" + varFuncType + ")((");
                    sb.Append(SanitizeIdentifier(varPat.Name));
                    sb.Append(") => ");
                    EmitExpr(sb, branch.Body, indent);
                    sb.Append("))(");
                    if (hasMultipleCtorBranches)
                    {
                        sb.Append(scrutineeRef);
                    }
                    else
                    {
                        EmitExpr(sb, match.Scrutinee, indent);
                    }

                    sb.Append(')');
                    for (int i = 0; i < openParens; i++)
                    {
                        sb.Append(')');
                    }

                    if (hasMultipleCtorBranches)
                    {
                        sb.Append("))(");
                        EmitExpr(sb, match.Scrutinee, indent);
                        sb.Append(')');
                    }
                    return;

                case IRWildcardPattern:
                    EmitExpr(sb, branch.Body, indent);
                    for (int i = 0; i < openParens; i++)
                    {
                        sb.Append(')');
                    }

                    if (hasMultipleCtorBranches)
                    {
                        sb.Append("))(");
                        EmitExpr(sb, match.Scrutinee, indent);
                        sb.Append(')');
                    }
                    return;
            }
        }

        sb.Append($" : throw new InvalidOperationException(\"Non-exhaustive match\")");
        for (int i = 0; i < openParens; i++)
        {
            sb.Append(')');
        }

        if (hasMultipleCtorBranches)
        {
            sb.Append("))(");
            EmitExpr(sb, match.Scrutinee, indent);
            sb.Append(')');
        }
    }

    void EmitCtorPatternBody(
        StringBuilder sb, IRCtorPattern ctorPat, string bindingName,
        IRExpr body, int indent)
    {
        List<(string Name, string Access, CodexType Type)> varBindings = [];
        List<(IRCtorPattern SubCtor, string Access)> nestedCtors = [];
        CollectPatternBindings(ctorPat, bindingName, varBindings, nestedCtors);

        if (nestedCtors.Count > 0)
        {
            EmitNestedCtorChecks(sb, nestedCtors, 0, varBindings, body, indent);
            return;
        }

        EmitVarBindingsAndBody(sb, varBindings, body, indent);
    }

    static void CollectPatternBindings(
        IRCtorPattern ctorPat, string bindingName,
        List<(string Name, string Access, CodexType Type)> varBindings,
        List<(IRCtorPattern SubCtor, string Access)> nestedCtors)
    {
        for (int i = 0; i < ctorPat.SubPatterns.Length; i++)
        {
            switch (ctorPat.SubPatterns[i])
            {
                case IRVarPattern vp:
                    varBindings.Add((vp.Name, $"{bindingName}.Field{i}", vp.Type));
                    break;
                case IRCtorPattern nested:
                    nestedCtors.Add((nested, $"{bindingName}.Field{i}"));
                    CollectPatternBindings(nested, $"_m{SanitizeIdentifier(nested.Name)}_{nestedCtors.Count}_", varBindings, nestedCtors);
                    break;
            }
        }
    }

    void EmitNestedCtorChecks(
        StringBuilder sb,
        List<(IRCtorPattern SubCtor, string Access)> nestedCtors,
        int idx,
        List<(string Name, string Access, CodexType Type)> varBindings,
        IRExpr body, int indent)
    {
        if (idx >= nestedCtors.Count)
        {
            EmitVarBindingsAndBody(sb, varBindings, body, indent);
            return;
        }

        (IRCtorPattern subCtor, string access) = nestedCtors[idx];
        string subCtorId = SanitizeIdentifier(subCtor.Name);
        string subBinding = $"_m{subCtorId}_{idx}_";
        sb.Append($"({access} is {subCtorId} {subBinding} ? ");

        List<(string, string, CodexType)> patchedBindings = [];
        foreach ((string name, string acc, CodexType type) in varBindings)
        {
            string patchedAccess = acc;
            string oldPrefix = $"_m{subCtorId}_{nestedCtors.Count}_";
            if (acc.StartsWith(oldPrefix))
            {
                patchedAccess = subBinding + acc[oldPrefix.Length..];
            }
            patchedBindings.Add((name, patchedAccess, type));
        }

        EmitNestedCtorChecks(sb, nestedCtors, idx + 1, patchedBindings, body, indent);
        sb.Append($" : throw new InvalidOperationException(\"Pattern match failed\"))");
    }

    void EmitVarBindingsAndBody(
        StringBuilder sb,
        List<(string Name, string Access, CodexType Type)> bindings,
        IRExpr body, int indent)
    {
        if (bindings.Count == 0)
        {
            EmitExpr(sb, body, indent);
            return;
        }

        for (int i = bindings.Count - 1; i >= 0; i--)
        {
            (string name, string _, CodexType type) = bindings[i];
            string funcType = $"Func<{EmitType(type)}, {EmitType(body.Type)}>";
            sb.Append("((" + funcType + ")((");
            sb.Append(SanitizeIdentifier(name));
            sb.Append(") => ");
        }

        EmitExpr(sb, body, indent);

        for (int i = 0; i < bindings.Count; i++)
        {
            (string _, string access, CodexType type) = bindings[i];
            string castStr = type is not (ErrorType or TypeVariable or NothingType or VoidType)
                ? $"({EmitType(type)})"
                : "";
            sb.Append("))(");
            sb.Append(castStr);
            sb.Append(access);
            sb.Append(')');
        }
    }

    void EmitActExpr(StringBuilder sb, IRAct actExpr, int indent)
    {
        string returnType = EmitType(actExpr.Type);
        sb.AppendLine($"((Func<{returnType}>)(() => {{");
        string pad = new(' ', (indent + 2) * 4);

        for (int i = 0; i < actExpr.Statements.Length; i++)
        {
            IRActStatement stmt = actExpr.Statements[i];
            bool isLast = i == actExpr.Statements.Length - 1;

            switch (stmt)
            {
                case IRActBind bind:
                    sb.Append(pad);
                    sb.Append($"var {SanitizeIdentifier(bind.Name)} = ");
                    EmitExpr(sb, bind.Value, indent + 2);
                    sb.AppendLine(";");
                    break;
                case IRActExec exec:
                    sb.Append(pad);
                    if (isLast && !IsVoidLike(actExpr.Type))
                    {
                        sb.Append("return ");
                        EmitExpr(sb, exec.Expression, indent + 2);
                        sb.AppendLine(";");
                    }
                    else if (isLast)
                    {
                        EmitExpr(sb, exec.Expression, indent + 2);
                        sb.AppendLine(";");
                        sb.Append(pad);
                        sb.AppendLine("return null;");
                    }
                    else
                    {
                        EmitExpr(sb, exec.Expression, indent + 2);
                        sb.AppendLine(";");
                    }
                    break;
            }
        }

        // If the last statement was a bind (not an expression), return the bound value
        if (actExpr.Statements.Length > 0 && actExpr.Statements[^1] is IRActBind lastBind)
        {
            sb.Append(pad);
            sb.AppendLine($"return {SanitizeIdentifier(lastBind.Name)};");
        }

        sb.Append(new string(' ', (indent + 1) * 4));
        sb.Append("}))()");
    }

    static bool IsVoidLike(CodexType type) => type switch
    {
        VoidType => true,
        NothingType => true,
        EffectfulType eft => IsVoidLike(eft.Return),
        _ => false
    };
}
