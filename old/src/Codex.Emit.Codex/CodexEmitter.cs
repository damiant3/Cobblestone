using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Codex.Core;
using Codex.IR;
using Codex.Types;

namespace Codex.Emit.Codex;

public sealed class CodexEmitter : IRExprTextEmitter, ICodeEmitter
{
    Set<string> m_constructorNames = Set<string>.s_empty;

    public string TargetName => "Codex";
    public string FileExtension => ".codex";

    public string Emit(IRChapter module)
    {
        m_constructorNames = module.CollectConstructorNames();
        StringBuilder sb = new();

        if (!module.Sections.IsDefault && module.Sections.Length > 0)
        {
            int sectionIndex = 0;
            foreach (IRChapterSection section in module.Sections)
            {
                // Emit Chapter header and prose
                if (section.ChapterTitle is not null)
                {
                    if (sectionIndex > 0) sb.AppendLine();
                    sb.AppendLine($"Chapter: {section.ChapterTitle}");
                    sb.AppendLine();
                    if (section.Prose is not null)
                    {
                        foreach (string line in section.Prose.Split('\n'))
                            sb.AppendLine($" {line}");
                        sb.AppendLine();
                    }
                }

                // Which Section header (if any) owns this chapter section's
                // content? We detect it from the first Definition that carries
                // a source-level Section tag. If there is one, every type def
                // and function def gets emitted inside that Section with a
                // two-space indent — the parser treats un-indented `Foo = ...`
                // between Chapter and Section as prose, not a type def, and
                // un-indented function defs as top-level (section abandoned).
                //
                // Assumes the current IRChapterSection grouping invariant:
                // within a single chapter-section, either no def has a
                // Section tag, or defs share a leading run of the same
                // Section (possibly followed by other Sections in order —
                // the loop below re-emits subsequent Section headers as they
                // change). A mixed "some null-tagged defs first, then
                // Section-tagged defs" layout inside ONE chapter-section
                // would be misindented here (leading null-tagged defs would
                // pick up the later Section's indent). That shape isn't
                // produced today; if IR grouping ever changes, revisit.
                string? sectionName = null;
                foreach (IRDefinition d in section.Definitions)
                {
                    if (d.Section is not null)
                    {
                        sectionName = d.Section;
                        break;
                    }
                }

                if (sectionName is not null)
                {
                    sb.AppendLine($"Section: {sectionName}");
                    sb.AppendLine();
                }

                string indent = sectionName is not null ? "  " : "";

                foreach ((string tdName, CodexType tdType) in section.TypeDefinitions)
                {
                    StringBuilder tdBuf = new();
                    EmitTypeDefinition(tdBuf, tdName, tdType);
                    if (indent.Length > 0)
                        AppendIndented(sb, tdBuf, indent);
                    else
                        sb.Append(tdBuf);
                    sb.AppendLine();
                }

                string? currentSection = sectionName;
                foreach (IRDefinition def in section.Definitions)
                {
                    if (def.Section is not null && def.Section != currentSection)
                    {
                        // Subsequent Section within the same chapter-section:
                        // emit its header un-indented, then keep indenting its
                        // contents.
                        currentSection = def.Section;
                        sb.AppendLine($"Section: {currentSection}");
                        sb.AppendLine();
                    }

                    StringBuilder defBuf = new();
                    EmitDefinition(defBuf, def);
                    if (currentSection is not null)
                        AppendIndented(sb, defBuf, "  ");
                    else
                        sb.Append(defBuf);
                    sb.AppendLine();
                }

                sectionIndex++;
            }
        }
        else
        {
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
        }

        return sb.ToString().TrimEnd('\r', '\n') + "\n";
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
            sb.Append($" | {ctor.Name.Value}");
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
            sb.Append($" {field.FieldName.Value} : {EmitType(field.Type)}");
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
            LinkedListType llt => $"LinkedList {WrapComplex(llt.Element)}",
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
        if (type is FunctionType or ListType or LinkedListType)
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

    protected override void EmitFuelExhaustedToken(StringBuilder sb) => sb.Append("?fuel-exhausted");

    protected override void EmitUnhandled(StringBuilder sb, IRExpr expr, int indent) => sb.Append("{- unhandled -}");

    protected override void EmitIntegerLit(StringBuilder sb, IRIntegerLit lit, int indent) => sb.Append(lit.Value);

    protected override void EmitNumberLit(StringBuilder sb, IRNumberLit lit, int indent) => sb.Append(lit.Value);

    protected override void EmitTextLit(StringBuilder sb, IRTextLit lit, int indent) => sb.Append($"\"{EscapeString(lit.Value)}\"");

    protected override void EmitBoolLit(StringBuilder sb, IRBoolLit lit, int indent) => sb.Append(lit.Value ? "True" : "False");

    protected override void EmitCharLit(StringBuilder sb, IRCharLit lit, int indent) => sb.Append($"'{EscapeChar(lit.Value)}'");

    protected override void EmitName(StringBuilder sb, IRName name, int indent) => sb.Append(name.Name);

    protected override void EmitNegate(StringBuilder sb, IRNegate neg, int indent)
    {
        sb.Append('-');
        EmitExpr(sb, neg.Operand, indent);
    }

    protected override void EmitFieldAccess(StringBuilder sb, IRFieldAccess fa, int indent)
    {
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
    }

    protected override void EmitError(StringBuilder sb, IRError err, int indent) =>
        sb.Append($"{{- error: {err.Message} -}}");

    // ── Binary operators ─────────────────────────────────────────

    protected override void EmitBinary(StringBuilder sb, IRBinary bin, int indent)
    {
        if (bin.Op == IRBinaryOp.AppendText || bin.Op == IRBinaryOp.AppendList)
        {
            // Left side of ++ may also need wrapping (e.g., if-expressions)
            bool wrapLeft = bin.Left is IRIf or IRLet or IRMatch or IRLambda;
            if (wrapLeft) sb.Append('(');
            EmitExpr(sb, bin.Left, indent);
            if (wrapLeft) sb.Append(')');
            sb.Append(" ++ ");
            bool wrapRight = bin.Right is IRIf or IRLet or IRMatch or IRLambda;
            if (wrapRight) sb.Append('(');
            EmitExpr(sb, bin.Right, indent);
            if (wrapRight) sb.Append(')');
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

        int outerPrec = BinPrecedence(bin.Op);
        bool leftNeedsParens = bin.Left is IRBinary lb && BinPrecedence(lb.Op) < outerPrec;
        bool rightNeedsParens = bin.Right is IRIf
            || (bin.Right is IRBinary rb && BinPrecedence(rb.Op) <= outerPrec
                && !(rb.Op == bin.Op && IsAssociative(bin.Op))); // only skip parens for same associative op

        if (leftNeedsParens) sb.Append('(');
        EmitExpr(sb, bin.Left, indent);
        if (leftNeedsParens) sb.Append(')');
        sb.Append($" {op} ");
        if (rightNeedsParens) sb.Append('(');
        EmitExpr(sb, bin.Right, indent);
        if (rightNeedsParens) sb.Append(')');
    }

    static int BinPrecedence(IRBinaryOp op) => op switch
    {
        IRBinaryOp.Or => 1,
        IRBinaryOp.And => 2,
        IRBinaryOp.Eq or IRBinaryOp.NotEq => 3,
        IRBinaryOp.Lt or IRBinaryOp.Gt or IRBinaryOp.LtEq or IRBinaryOp.GtEq => 4,
        IRBinaryOp.AppendText or IRBinaryOp.AppendList or IRBinaryOp.ConsList => 5,
        IRBinaryOp.AddInt or IRBinaryOp.AddNum or IRBinaryOp.SubInt or IRBinaryOp.SubNum => 6,
        IRBinaryOp.MulInt or IRBinaryOp.MulNum or IRBinaryOp.DivInt or IRBinaryOp.DivNum => 7,
        IRBinaryOp.PowInt => 8,
        _ => 0
    };

    static bool IsAssociative(IRBinaryOp op) => op is
        IRBinaryOp.AddInt or IRBinaryOp.AddNum or
        IRBinaryOp.MulInt or IRBinaryOp.MulNum or
        IRBinaryOp.AppendText or IRBinaryOp.AppendList or IRBinaryOp.ConsList or
        IRBinaryOp.And or IRBinaryOp.Or;

    // ── If/then/else ─────────────────────────────────────────────

    protected override void EmitIf(StringBuilder sb, IRIf iff, int indent)
    {
        // Long flat dispatch chains (>3 branches, all simple thens): emit flat
        if (CountIfChainDepth(iff) > 3)
        {
            EmitIfFlat(sb, iff, indent);
            return;
        }

        // Track column where "if" starts — else must align here
        int ifColumn = GetCurrentColumn(sb);
        sb.Append("if ");
        EmitExpr(sb, iff.Condition, indent);
        if (IsCompactExpr(iff.Then))
        {
            sb.Append(" then ");
            EmitExpr(sb, iff.Then, indent);
            sb.AppendLine();
            EmitSpaces(sb, ifColumn);
            sb.Append("else ");
            if (iff.Else is IRIf elseIf)
                EmitIf(sb, elseIf, indent);
            else
                EmitExpr(sb, iff.Else, indent);
        }
        else
        {
            sb.AppendLine();
            EmitIndent(sb, indent + 1);
            sb.Append("then ");
            EmitExpr(sb, iff.Then, indent + 1);
            sb.AppendLine();
            EmitSpaces(sb, ifColumn);
            sb.Append("else ");
            if (iff.Else is IRIf elseIf2)
                EmitIf(sb, elseIf2, indent);
            else
                EmitExpr(sb, iff.Else, indent + 1);
        }
    }

    void EmitIfFlat(StringBuilder sb, IRIf iff, int indent)
    {
        sb.Append("if ");
        EmitExpr(sb, iff.Condition, indent);
        sb.Append(" then ");
        EmitExpr(sb, iff.Then, indent);
        sb.AppendLine();
        EmitIndent(sb, indent);
        sb.Append("else ");
        if (iff.Else is IRIf elseIf)
            EmitIfFlat(sb, elseIf, indent);
        else
            EmitExpr(sb, iff.Else, indent);
    }

    static int CountIfChainDepth(IRIf iff)
    {
        int depth = 1;
        IRExpr e = iff.Else;
        while (e is IRIf next) { depth++; e = next.Else; }
        return depth;
    }

    static int GetCurrentColumn(StringBuilder sb)
    {
        int col = 0;
        for (int i = sb.Length - 1; i >= 0; i--)
        {
            if (sb[i] == '\n') break;
            col++;
        }
        return col;
    }

    static void EmitSpaces(StringBuilder sb, int count)
    {
        for (int i = 0; i < count; i++)
            sb.Append(' ');
    }

    // ── Let ──────────────────────────────────────────────────────

    protected override void EmitLet(StringBuilder sb, IRLet let, int indent)
    {
        sb.Append($"let {let.Name} = ");
        EmitExpr(sb, let.Value, indent + 1);
        sb.AppendLine();
        EmitIndent(sb, indent);
        sb.Append("in ");
        EmitExpr(sb, let.Body, indent);
    }

    // ── Apply ────────────────────────────────────────────────────

    protected override void EmitApply(StringBuilder sb, IRApply app, int indent)
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

    protected override void EmitLambda(StringBuilder sb, IRLambda lam, int indent)
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

    protected override void EmitList(StringBuilder sb, IRList list, int indent)
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

    protected override void EmitMatch(StringBuilder sb, IRMatch match, int indent)
    {
        sb.Append("when ");
        EmitExpr(sb, match.Scrutinee, indent);
        foreach (IRMatchBranch branch in match.Branches)
        {
            sb.AppendLine();
            EmitIndent(sb, indent + 1);
            sb.Append("is ");
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
                sb.Append("otherwise");
                break;
        }
    }

    // ── Act blocks ────────────────────────────────────────────────

    protected override void EmitAct(StringBuilder sb, IRAct actExpr, int indent)
    {
        sb.Append("act");
        foreach (IRActStatement stmt in actExpr.Statements)
        {
            sb.AppendLine();
            EmitIndent(sb, indent + 1);
            switch (stmt)
            {
                case IRActBind bind:
                    sb.Append($"{bind.Name} <- ");
                    EmitExpr(sb, bind.Value, indent + 1);
                    break;
                case IRActExec exec:
                    EmitExpr(sb, exec.Expression, indent + 1);
                    break;
            }
        }
        sb.AppendLine();
        EmitIndent(sb, indent);
        sb.Append("end");
    }

    // ── Record construction ──────────────────────────────────────

    protected override void EmitRecord(StringBuilder sb, IRRecord rec, int indent)
    {
        if (rec.Fields.Length <= 1 || (rec.Fields.Length <= 2 && AllSimpleFields(rec)))
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
        else
        {
            sb.AppendLine($"{rec.TypeName} {{");
            for (int i = 0; i < rec.Fields.Length; i++)
            {
                EmitIndent(sb, indent + 1);
                sb.Append($"{rec.Fields[i].FieldName} = ");
                EmitExpr(sb, rec.Fields[i].Value, indent + 1);
                if (i < rec.Fields.Length - 1) sb.Append(',');
                sb.AppendLine();
            }
            EmitIndent(sb, indent);
            sb.Append("}");
        }
    }

    // ── Utilities ────────────────────────────────────────────────

    static bool IsSimpleExpr(IRExpr expr) => expr is
        IRIntegerLit or IRNumberLit or IRTextLit or IRBoolLit
        or IRCharLit or IRName or IRFieldAccess;

    static bool IsCompactExpr(IRExpr expr) => expr is not
        (IRIf or IRLet or IRMatch or IRAct or IRRecord);

    static bool AllSimpleFields(IRRecord rec)
    {
        foreach ((string FieldName, IRExpr Value) field in rec.Fields)
            if (!IsSimpleExpr(field.Value)) return false;
        return true;
    }

    static bool NeedsParens(IRExpr expr, bool isCtor)
    {
        if (expr is IRApply or IRBinary or IRIf or IRLet
            or IRMatch or IRNegate or IRLambda or IRFieldAccess or IRRecord)
        {
            return true;
        }

        if (isCtor && expr is IRName { Type: FunctionType })
            return true;
        return false;
    }

    static void EmitIndent(StringBuilder sb, int indent)
    {
        for (int i = 0; i < indent; i++)
            sb.Append(' ');
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

    // Prepend `indent` to every non-empty line of `src` as it's appended to
    // `dst`. Trailing blank lines stay blank (no trailing indent on an
    // otherwise-empty line) so the output is diff-clean.
    static void AppendIndented(StringBuilder dst, StringBuilder src, string indent)
    {
        string text = src.ToString();
        int pos = 0;
        while (pos < text.Length)
        {
            int newline = text.IndexOf('\n', pos);
            int lineEnd = newline < 0 ? text.Length : newline;
            if (lineEnd > pos)
            {
                dst.Append(indent);
                dst.Append(text, pos, lineEnd - pos);
            }
            if (newline < 0)
                break;
            dst.Append('\n');
            pos = newline + 1;
        }
    }
}
