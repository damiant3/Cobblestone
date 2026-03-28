using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Codex.Core;
using Codex.IR;
using Codex.Types;

namespace Codex.Emit.Codex;

public sealed class CodexEmitter : ICodeEmitter
{
    Set<string> m_constructorNames = Set<string>.s_empty;

    public string TargetName => "Codex";
    public string FileExtension => ".codex";

    public string Emit(IRModule module)
    {
        m_constructorNames = CollectConstructorNames(module);
        StringBuilder sb = new();

        foreach (KeyValuePair<string, CodexType> kv in module.TypeDefinitions)
        {
            EmitTypeDefinition(sb, kv.Key, kv.Value);
            sb.AppendLine();
        }

        foreach (IRDefinition def in module.Definitions)
        {
            EmitDefinition(sb, def);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    // ── Type definitions ─────────────────────────────────────────

    static void EmitTypeDefinition(StringBuilder sb, string name, CodexType type)
    {
        switch (type)
        {
            case SumType sum:
                EmitSumType(sb, sum);
                break;
            case RecordType rec:
                EmitRecordType(sb, rec);
                break;
        }
    }

    static void EmitSumType(StringBuilder sb, SumType sum)
    {
        sb.AppendLine($"{sum.TypeName.Value} =");
        foreach (SumConstructorType ctor in sum.Constructors)
        {
            sb.Append($"  | {ctor.Name.Value}");
            foreach (CodexType field in ctor.Fields)
            {
                sb.Append($" ({EmitType(field)})");
            }
            sb.AppendLine();
        }
    }

    static void EmitRecordType(StringBuilder sb, RecordType rec)
    {
        sb.Append($"{rec.TypeName.Value} = record {{");
        for (int i = 0; i < rec.Fields.Length; i++)
        {
            if (i > 0) sb.Append(',');
            RecordFieldType field = rec.Fields[i];
            sb.AppendLine();
            sb.Append($"  {field.FieldName.Value} : {EmitType(field.Type)}");
        }
        sb.AppendLine();
        sb.AppendLine("}");
    }

    // ── Definitions ──────────────────────────────────────────────

    void EmitDefinition(StringBuilder sb, IRDefinition def)
    {
        string name = def.Name;
        CodexType sigType = def.Type;

        // Type signature
        sb.AppendLine($"{name} : {EmitType(sigType)}");

        // Definition with parameters
        sb.Append(name);
        foreach (IRParameter param in def.Parameters)
        {
            sb.Append($" ({param.Name})");
        }
        sb.Append(" =");

        if (IsSimpleExpr(def.Body))
        {
            sb.Append(' ');
            EmitExpr(sb, def.Body, 1);
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine();
            EmitIndent(sb, 1);
            EmitExpr(sb, def.Body, 1);
            sb.AppendLine();
        }
    }

    // ── Types ────────────────────────────────────────────────────

    static string EmitType(CodexType type)
    {
        return type switch
        {
            IntegerType => "Integer",
            NumberType => "Number",
            TextType => "Text",
            BooleanType => "Boolean",
            CharType => "Char",
            NothingType => "Nothing",
            VoidType => "Nothing",
            EffectfulType eft => $"[{EffectName(eft)}] {EmitType(eft.Return)}",
            LinearType lin => $"linear {EmitType(lin.Inner)}",
            ListType lt => $"List {WrapComplex(lt.Element)}",
            SumType st => st.TypeName.Value,
            RecordType rt => rt.TypeName.Value,
            ConstructedType ct => ct.Constructor.Value,
            FunctionType ft => $"{WrapFunctionParam(ft.Parameter)} -> {EmitType(ft.Return)}",
            TypeVariable tv => $"a{tv.Id}",
            ForAllType fa => EmitType(fa.Body),
            _ => "Unknown"
        };
    }

    static string WrapComplex(CodexType type)
    {
        if (type is FunctionType or ListType)
            return $"({EmitType(type)})";
        return EmitType(type);
    }

    static string WrapFunctionParam(CodexType type)
    {
        if (type is FunctionType)
            return $"({EmitType(type)})";
        return EmitType(type);
    }

    static string EffectName(EffectfulType eft)
    {
        return string.Join(", ", eft.Effects.Select(e => e.EffectName.Value));
    }

    // ── Expressions ──────────────────────────────────────────────

    void EmitExpr(StringBuilder sb, IRExpr expr, int indent)
    {
        switch (expr)
        {
            case IRIntegerLit lit:
                sb.Append(lit.Value);
                break;

            case IRNumberLit lit:
                sb.Append(lit.Value);
                break;

            case IRTextLit lit:
                sb.Append($"\"{EscapeString(lit.Value)}\"");
                break;

            case IRBoolLit lit:
                sb.Append(lit.Value ? "True" : "False");
                break;

            case IRCharLit lit:
                sb.Append($"'{EscapeChar(lit.Value)}'");
                break;

            case IRName name:
                sb.Append(name.Name);
                break;

            case IRBinary bin:
                EmitBinary(sb, bin, indent);
                break;

            case IRNegate neg:
                sb.Append('-');
                EmitExpr(sb, neg.Operand, indent);
                break;

            case IRIf iff:
                EmitIf(sb, iff, indent);
                break;

            case IRLet let:
                EmitLet(sb, let, indent);
                break;

            case IRApply app:
                EmitApply(sb, app, indent);
                break;

            case IRLambda lam:
                EmitLambda(sb, lam, indent);
                break;

            case IRList list:
                EmitList(sb, list, indent);
                break;

            case IRRegion region:
                EmitExpr(sb, region.Body, indent);
                break;

            case IRMatch match:
                EmitMatch(sb, match, indent);
                break;

            case IRDo doExpr:
                EmitDo(sb, doExpr, indent);
                break;

            case IRRecord rec:
                EmitRecord(sb, rec, indent);
                break;

            case IRFieldAccess fa:
                if (fa.Record is IRName or IRFieldAccess)
                {
                    EmitExpr(sb, fa.Record, indent);
                }
                else
                {
                    sb.Append('(');
                    EmitExpr(sb, fa.Record, indent);
                    sb.Append(')');
                }
                sb.Append('.');
                sb.Append(fa.FieldName);
                break;

            case IRError err:
                sb.Append($"{{- error: {err.Message} -}}");
                break;

            default:
                sb.Append("{- unhandled -}");
                break;
        }
    }

    // ── Binary operators ─────────────────────────────────────────

    void EmitBinary(StringBuilder sb, IRBinary bin, int indent)
    {
        bool needsParens = bin.Left is IRBinary || bin.Right is IRBinary;

        if (bin.Op == IRBinaryOp.AppendText)
        {
            EmitExpr(sb, bin.Left, indent);
            sb.Append(" ++ ");
            EmitExpr(sb, bin.Right, indent);
            return;
        }

        if (bin.Op == IRBinaryOp.AppendList)
        {
            EmitExpr(sb, bin.Left, indent);
            sb.Append(" ++ ");
            EmitExpr(sb, bin.Right, indent);
            return;
        }

        if (bin.Op == IRBinaryOp.ConsList)
        {
            EmitExpr(sb, bin.Left, indent);
            sb.Append(" :: ");
            EmitExpr(sb, bin.Right, indent);
            return;
        }

        string op = bin.Op switch
        {
            IRBinaryOp.AddInt or IRBinaryOp.AddNum => "+",
            IRBinaryOp.SubInt or IRBinaryOp.SubNum => "-",
            IRBinaryOp.MulInt or IRBinaryOp.MulNum => "*",
            IRBinaryOp.DivInt or IRBinaryOp.DivNum => "/",
            IRBinaryOp.PowInt => "^",
            IRBinaryOp.Eq => "==",
            IRBinaryOp.NotEq => "/=",
            IRBinaryOp.Lt => "<",
            IRBinaryOp.Gt => ">",
            IRBinaryOp.LtEq => "<=",
            IRBinaryOp.GtEq => ">=",
            IRBinaryOp.And => "&",
            IRBinaryOp.Or => "|",
            _ => "?"
        };

        if (needsParens) sb.Append('(');
        EmitExpr(sb, bin.Left, indent);
        sb.Append($" {op} ");
        EmitExpr(sb, bin.Right, indent);
        if (needsParens) sb.Append(')');
    }

    // ── If/then/else ─────────────────────────────────────────────

    void EmitIf(StringBuilder sb, IRIf iff, int indent)
    {
        sb.Append("if ");
        EmitExpr(sb, iff.Condition, indent);
        if (IsSimpleExpr(iff.Then) && IsSimpleExpr(iff.Else))
        {
            sb.Append(" then ");
            EmitExpr(sb, iff.Then, indent);
            sb.Append(" else ");
            EmitExpr(sb, iff.Else, indent);
        }
        else
        {
            sb.AppendLine();
            EmitIndent(sb, indent + 1);
            sb.Append("then ");
            EmitExpr(sb, iff.Then, indent + 1);
            sb.AppendLine();
            EmitIndent(sb, indent + 1);
            sb.Append("else ");
            EmitExpr(sb, iff.Else, indent + 1);
        }
    }

    // ── Let ──────────────────────────────────────────────────────

    void EmitLet(StringBuilder sb, IRLet let, int indent)
    {
        sb.Append($"let {let.Name} = ");
        EmitExpr(sb, let.Value, indent + 1);
        sb.AppendLine();
        EmitIndent(sb, indent);
        sb.Append("in ");
        EmitExpr(sb, let.Body, indent);
    }

    // ── Apply ────────────────────────────────────────────────────

    void EmitApply(StringBuilder sb, IRApply app, int indent)
    {
        // Collect curried args: f a b c → [f, a, b, c]
        List<IRExpr> args = [];
        IRExpr func = app;
        while (func is IRApply a)
        {
            args.Insert(0, a.Argument);
            func = a.Function;
        }

        bool isCtor = func is IRName n && m_constructorNames.Contains(n.Name);

        EmitExpr(sb, func, indent);
        foreach (IRExpr arg in args)
        {
            sb.Append(' ');
            if (NeedsParens(arg, isCtor))
            {
                sb.Append('(');
                EmitExpr(sb, arg, indent);
                sb.Append(')');
            }
            else
            {
                EmitExpr(sb, arg, indent);
            }
        }
    }

    // ── Lambda ───────────────────────────────────────────────────

    void EmitLambda(StringBuilder sb, IRLambda lam, int indent)
    {
        sb.Append('\\');
        for (int i = 0; i < lam.Parameters.Length; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(lam.Parameters[i].Name);
        }
        sb.Append(" -> ");
        EmitExpr(sb, lam.Body, indent);
    }

    // ── List literal ─────────────────────────────────────────────

    void EmitList(StringBuilder sb, IRList list, int indent)
    {
        sb.Append('[');
        for (int i = 0; i < list.Elements.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            EmitExpr(sb, list.Elements[i], indent);
        }
        sb.Append(']');
    }

    // ── Match (when/if) ──────────────────────────────────────────

    void EmitMatch(StringBuilder sb, IRMatch match, int indent)
    {
        sb.Append("when ");
        EmitExpr(sb, match.Scrutinee, indent);
        foreach (IRMatchBranch branch in match.Branches)
        {
            sb.AppendLine();
            EmitIndent(sb, indent + 1);
            sb.Append("if ");
            EmitPattern(sb, branch.Pattern);
            sb.Append(" -> ");
            EmitExpr(sb, branch.Body, indent + 1);
        }
    }

    static void EmitPattern(StringBuilder sb, IRPattern pattern)
    {
        switch (pattern)
        {
            case IRVarPattern vp:
                sb.Append(vp.Name);
                break;
            case IRLiteralPattern lp:
                sb.Append(lp.Value);
                break;
            case IRCtorPattern cp:
                sb.Append(cp.Name);
                foreach (IRPattern sub in cp.SubPatterns)
                {
                    sb.Append(" (");
                    EmitPattern(sb, sub);
                    sb.Append(')');
                }
                break;
            case IRWildcardPattern:
                sb.Append('_');
                break;
        }
    }

    // ── Do blocks ────────────────────────────────────────────────

    void EmitDo(StringBuilder sb, IRDo doExpr, int indent)
    {
        sb.Append("do");
        foreach (IRDoStatement stmt in doExpr.Statements)
        {
            sb.AppendLine();
            EmitIndent(sb, indent + 1);
            switch (stmt)
            {
                case IRDoBind bind:
                    sb.Append($"{bind.Name} <- ");
                    EmitExpr(sb, bind.Value, indent + 1);
                    break;
                case IRDoExec exec:
                    EmitExpr(sb, exec.Expression, indent + 1);
                    break;
            }
        }
    }

    // ── Record construction ──────────────────────────────────────

    void EmitRecord(StringBuilder sb, IRRecord rec, int indent)
    {
        sb.Append($"{rec.TypeName} {{");
        for (int i = 0; i < rec.Fields.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append($" {rec.Fields[i].FieldName} = ");
            EmitExpr(sb, rec.Fields[i].Value, indent);
        }
        sb.Append(" }");
    }

    // ── Utilities ────────────────────────────────────────────────

    static Set<string> CollectConstructorNames(IRModule module)
    {
        Set<string> names = Set<string>.s_empty;
        foreach (KeyValuePair<string, CodexType> kv in module.TypeDefinitions)
        {
            if (kv.Value is SumType sum)
            {
                foreach (SumConstructorType ctor in sum.Constructors)
                    names = names.Add(ctor.Name.Value);
            }
        }
        return names;
    }

    static bool IsSimpleExpr(IRExpr expr) => expr is
        IRIntegerLit or IRNumberLit or IRTextLit or IRBoolLit
        or IRCharLit or IRName or IRFieldAccess;

    static bool NeedsParens(IRExpr expr, bool isCtor)
    {
        if (expr is IRApply or IRBinary or IRIf or IRLet
            or IRMatch or IRNegate or IRLambda)
            return true;
        if (isCtor && expr is IRName { Type: FunctionType })
            return true;
        return false;
    }

    static void EmitIndent(StringBuilder sb, int indent)
    {
        for (int i = 0; i < indent; i++)
            sb.Append("  ");
    }

    static string EscapeString(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "");
    }

    static string EscapeChar(long value)
    {
        return value switch
        {
            '\n' => "\\n",
            '\r' => "\\r",
            '\t' => "\\t",
            '\\' => "\\\\",
            '\'' => "\\'",
            _ => ((char)value).ToString()
        };
    }
}
