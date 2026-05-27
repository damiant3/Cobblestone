using System.Globalization;
using System.Text;
using Codex.Core;

namespace Codex.Ast;

// Pretty-prints a post-desugar Chapter back into re-parseable .codex source
// text. Pairs with DocumentParser.Parse + Desugarer to support the
// G/1 round-trip (parse → print → parse == original AST) and the H
// property test (parse(print(ast)) == ast).
//
// Indent convention is load-bearing — see project memory on parser indent
// sensitivity:
//   * `Chapter:` and `Section:` headers live at column 0.
//   * Under a `Section:` header, every def/typedef must be 2-space indented
//     or it is read as a new top-level item and silently abandons the
//     section for anything after.
//   * Un-indented defs between `Chapter:` and the first `Section:` are
//     parsed as prose, not typedefs.
//
// The Section-grouping strategy mirrors CodexEmitter's BACKLOG-#10 fix
// ("first def.Section tag wins").
public static class ChapterPrettyPrinter
{
    public static string Print(Chapter chapter)
    {
        StringBuilder sb = new();
        PrintChapter(sb, chapter);
        string text = sb.ToString();
        text = text.TrimEnd('\r', '\n');
        return text + "\n";
    }

    static void PrintChapter(StringBuilder sb, Chapter chapter)
    {
        bool proseMode = chapter.ChapterTitle is not null;

        if (proseMode)
        {
            sb.AppendLine($"Chapter: {chapter.ChapterTitle}");
            foreach (CitesDecl c in chapter.Citations)
                PrintCites(sb, c, "  ");
            sb.AppendLine();
            if (!string.IsNullOrEmpty(chapter.Prose))
            {
                foreach (string line in chapter.Prose!.Split('\n'))
                {
                    if (line.Length == 0)
                    {
                        sb.AppendLine();
                    }
                    else
                    {
                        sb.Append(' ');
                        sb.AppendLine(line);
                    }
                }
                sb.AppendLine();
            }
        }
        else
        {
            foreach (CitesDecl c in chapter.Citations)
            {
                PrintCites(sb, c, "");
                sb.AppendLine();
            }
        }

        foreach (EffectDef e in chapter.EffectDefs)
        {
            PrintEffect(sb, e, "");
            sb.AppendLine();
        }

        // Group type defs + function defs by Section. Within a group,
        // emit type defs first, then function defs — matches CodexEmitter.
        IEnumerable<string?> sectionOrder = EnumerateSectionOrder(chapter);

        foreach (string? section in sectionOrder)
        {
            string indent;
            if (section is not null)
            {
                sb.AppendLine($"Section: {section}");
                sb.AppendLine();
                indent = "  ";
            }
            else
            {
                // Null-tagged items before any Section: inside a prose-mode
                // chapter, function defs un-indented parse as prose. Indent
                // them 2 spaces; the notation-block recognizer picks them
                // up without needing a synthesized Section header (see
                // ProseParser.IsNotationIndent == indent>=2).
                indent = proseMode ? "  " : "";
            }

            foreach (TypeDef td in chapter.TypeDefinitions)
            {
                if (td.Section == section)
                {
                    PrintTypeDef(sb, td, indent);
                    sb.AppendLine();
                }
            }

            foreach (Definition d in chapter.Definitions)
            {
                if (d.Section == section)
                {
                    PrintDefinition(sb, d, indent);
                    sb.AppendLine();
                }
            }
        }

        // Claims and proofs have no Section tag today. Emit at chapter
        // level (or 2-indent in prose mode so the notation-block recognizer
        // picks them up).
        string tailIndent = proseMode ? "  " : "";
        foreach (ClaimDef c in chapter.Claims)
        {
            PrintClaim(sb, c, tailIndent);
            sb.AppendLine();
        }

        foreach (ProofDef p in chapter.Proofs)
        {
            PrintProof(sb, p, tailIndent);
            sb.AppendLine();
        }
    }

    static IEnumerable<string?> EnumerateSectionOrder(Chapter chapter)
    {
        // Preserve source-order sequence of Section tags across typedefs +
        // definitions. Null comes first if any null-tagged item exists,
        // then each distinct non-null Section in first-appearance order.
        HashSet<string?> seen = new();
        List<string?> order = new();

        // Null goes first when present (matches parser: un-tagged items
        // appear between Chapter and the first Section header).
        bool anyNull = false;
        foreach (TypeDef td in chapter.TypeDefinitions)
        {
            if (td.Section is null) { anyNull = true; break; }
        }
        if (!anyNull)
        {
            foreach (Definition d in chapter.Definitions)
            {
                if (d.Section is null) { anyNull = true; break; }
            }
        }
        if (anyNull)
        {
            order.Add(null);
            seen.Add(null);
        }

        foreach (TypeDef td in chapter.TypeDefinitions)
        {
            if (td.Section is not null && seen.Add(td.Section))
                order.Add(td.Section);
        }
        foreach (Definition d in chapter.Definitions)
        {
            if (d.Section is not null && seen.Add(d.Section))
                order.Add(d.Section);
        }

        if (order.Count == 0)
            order.Add(null);
        return order;
    }

    static void PrintCites(StringBuilder sb, CitesDecl c, string indent)
    {
        sb.Append(indent);
        sb.Append($"cites {c.Quire.Value} chapter {c.ChapterName.Value}");
        if (c.SelectedNames.Count > 0)
        {
            sb.Append(" (");
            for (int i = 0; i < c.SelectedNames.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(c.SelectedNames[i].Value);
            }
            sb.Append(')');
        }
        sb.AppendLine();
    }

    static void PrintEffect(StringBuilder sb, EffectDef e, string indent)
    {
        sb.Append(indent);
        sb.AppendLine($"effect {e.EffectName.Value} where");
        foreach (EffectOperationDef op in e.Operations)
        {
            sb.Append(indent);
            sb.AppendLine($"  {op.Name.Value} : {PrintType(op.Type, 0)}");
        }
    }

    static void PrintTypeDef(StringBuilder sb, TypeDef td, string indent)
    {
        switch (td)
        {
            case RecordTypeDef rec:
                PrintRecordTypeDef(sb, rec, indent);
                break;
            case VariantTypeDef var:
                PrintVariantTypeDef(sb, var, indent);
                break;
        }
    }

    static void PrintRecordTypeDef(StringBuilder sb, RecordTypeDef rec, string indent)
    {
        sb.Append(indent);
        sb.Append(rec.Name.Value);
        foreach (Name tp in rec.TypeParameters)
        {
            sb.Append($" ({tp.Value})");
        }
        sb.AppendLine(" = record {");
        for (int i = 0; i < rec.Fields.Count; i++)
        {
            RecordFieldDef f = rec.Fields[i];
            sb.Append(indent);
            sb.Append("  ");
            sb.Append($"{f.FieldName.Value} : {PrintType(f.Type, 0)}");
            if (i < rec.Fields.Count - 1)
                sb.Append(',');
            sb.AppendLine();
        }
        sb.Append(indent);
        sb.AppendLine("}");
    }

    static void PrintVariantTypeDef(StringBuilder sb, VariantTypeDef v, string indent)
    {
        sb.Append(indent);
        sb.Append(v.Name.Value);
        foreach (Name tp in v.TypeParameters)
        {
            sb.Append($" ({tp.Value})");
        }
        sb.AppendLine(" =");
        foreach (VariantCtorDef ctor in v.Constructors)
        {
            sb.Append(indent);
            sb.Append($"  | {ctor.Name.Value}");
            foreach (VariantFieldDef f in ctor.Fields)
            {
                // Field name is tracked on the AST but the surface syntax
                // only accepts `Ctor (name: Type)` or `Ctor (Type)`.
                if (f.FieldName is not null)
                {
                    sb.Append($" ({f.FieldName.Value}: {PrintType(f.Type, 0)})");
                }
                else
                {
                    sb.Append($" ({PrintType(f.Type, 0)})");
                }
            }
            sb.AppendLine();
        }
    }

    static void PrintDefinition(StringBuilder sb, Definition d, string indent)
    {
        if (d.DeclaredType is not null)
        {
            sb.Append(indent);
            sb.AppendLine($"{d.Name.Value} : {PrintType(d.DeclaredType, 0)}");
        }

        sb.Append(indent);
        sb.Append(d.Name.Value);
        foreach (Parameter p in d.Parameters)
        {
            sb.Append($" ({p.Name.Value})");
        }
        sb.Append(" =");

        int bodyIndent = indent.Length / 2 + 1;
        if (IsSimple(d.Body))
        {
            sb.Append(' ');
            PrintExpr(sb, d.Body, bodyIndent);
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine();
            EmitSpaces(sb, bodyIndent * 2);
            PrintExpr(sb, d.Body, bodyIndent);
            sb.AppendLine();
        }
    }

    static void PrintClaim(StringBuilder sb, ClaimDef c, string indent)
    {
        sb.Append(indent);
        sb.Append($"claim {c.Name.Value}");
        foreach (Parameter p in c.Parameters)
        {
            sb.Append($" ({p.Name.Value})");
        }
        sb.AppendLine($" : {PrintType(c.Left, 0)} === {PrintType(c.Right, 0)}");
    }

    static void PrintProof(StringBuilder sb, ProofDef p, string indent)
    {
        sb.Append(indent);
        sb.Append($"proof {p.Name.Value}");
        foreach (Parameter par in p.Parameters)
        {
            sb.Append($" ({par.Name.Value})");
        }
        sb.Append(" =");
        sb.AppendLine();
        sb.Append(indent);
        sb.Append("  ");
        PrintProofExpr(sb, p.Body, indent.Length / 2 + 1);
        sb.AppendLine();
        sb.Append(indent);
        sb.AppendLine("qed");
    }

    // ── Types ────────────────────────────────────────────────────

    static string PrintType(TypeExpr type, int depth)
    {
        if (depth > 256)
            return "?fuel";

        return type switch
        {
            NamedTypeExpr n => n.Name.Value,
            FunctionTypeExpr f =>
                $"{WrapFunctionParam(f.Parameter, depth + 1)} -> {PrintType(f.Return, depth + 1)}",
            AppliedTypeExpr a =>
                PrintAppliedType(a, depth),
            EffectfulTypeExpr e =>
                $"[{string.Join(", ", e.Effects.Select(et => PrintType(et, depth + 1)))}] {PrintType(e.Return, depth + 1)}",
            LinearTypeExpr lin => $"linear {WrapTypeAtom(lin.Inner, depth + 1)}",
            DependentTypeExpr dep =>
                $"({dep.ParamName.Value} : {PrintType(dep.ParamType, depth + 1)}) -> {PrintType(dep.Body, depth + 1)}",
            IntegerLiteralTypeExpr i => i.Value.ToString(CultureInfo.InvariantCulture),
            BinaryTypeExpr b =>
                $"({PrintType(b.Left, depth + 1)} {BinaryOpSymbol(b.Op)} {PrintType(b.Right, depth + 1)})",
            ProofConstraintExpr p =>
                $"{{{PrintType(p.Left, depth + 1)} {BinaryOpSymbol(p.Op)} {PrintType(p.Right, depth + 1)}}}",
            _ => "?unknown"
        };
    }

    static string PrintAppliedType(AppliedTypeExpr a, int depth)
    {
        StringBuilder sb = new();
        sb.Append(PrintType(a.Constructor, depth + 1));
        foreach (TypeExpr arg in a.Arguments)
        {
            sb.Append(' ');
            sb.Append(WrapTypeAtom(arg, depth + 1));
        }
        return sb.ToString();
    }

    // Type atoms are what ParseTypeAtomSimple accepts as a bare atom:
    // NamedType, IntegerLiteral, or a parenthesized type. Anything else
    // (function, applied, effectful, linear, dependent) must be parenthesized
    // when it appears as a type argument.
    static string WrapTypeAtom(TypeExpr t, int depth)
    {
        return t switch
        {
            NamedTypeExpr or IntegerLiteralTypeExpr or BinaryTypeExpr or ProofConstraintExpr
                => PrintType(t, depth),
            _ => $"({PrintType(t, depth)})"
        };
    }

    static string WrapFunctionParam(TypeExpr t, int depth)
    {
        return t is FunctionTypeExpr
            ? $"({PrintType(t, depth)})"
            : PrintType(t, depth);
    }

    // ── Expressions ──────────────────────────────────────────────

    static void PrintExpr(StringBuilder sb, Expr expr, int indent)
    {
        PrintExpr(sb, expr, indent, 0);
    }

    static void PrintExpr(StringBuilder sb, Expr expr, int indent, int minPrec)
    {
        switch (expr)
        {
            case LiteralExpr lit:
                sb.Append(FormatLiteral(lit));
                break;

            case NameExpr n:
                sb.Append(n.Name.Value);
                break;

            case ApplyExpr app:
                PrintApply(sb, app, indent);
                break;

            case BinaryExpr bin:
                PrintBinary(sb, bin, indent, minPrec);
                break;

            case UnaryExpr un:
                sb.Append('-');
                PrintExprInParens(sb, un.Operand, indent, NeedsUnaryParens(un.Operand));
                break;

            case IfExpr iff:
                PrintIf(sb, iff, indent);
                break;

            case LetExpr let:
                PrintLet(sb, let, indent);
                break;

            case LambdaExpr lam:
                PrintLambda(sb, lam, indent);
                break;

            case MatchExpr m:
                PrintMatch(sb, m, indent);
                break;

            case ListExpr l:
                PrintList(sb, l, indent);
                break;

            case RecordExpr r:
                PrintRecord(sb, r, indent);
                break;

            case FieldAccessExpr fa:
                PrintFieldAccess(sb, fa, indent);
                break;

            case ActExpr act:
                PrintAct(sb, act, indent);
                break;

            case HandleExpr h:
                PrintHandle(sb, h, indent);
                break;

            case ErrorExpr err:
                sb.Append($"{{- error: {err.Message} -}}");
                break;

            default:
                sb.Append("{- unhandled -}");
                break;
        }
    }

    static string FormatLiteral(LiteralExpr lit) => lit.Kind switch
    {
        LiteralKind.Integer => Convert.ToString(lit.Value, CultureInfo.InvariantCulture)!,
        LiteralKind.Number => FormatNumberLiteral(lit.Value),
        LiteralKind.Text => $"\"{EscapeString(Convert.ToString(lit.Value, CultureInfo.InvariantCulture) ?? "")}\"",
        LiteralKind.Boolean => ConvertBool(lit.Value) ? "True" : "False",
        LiteralKind.Char => $"'{EscapeChar(lit.Value)}'",
        _ => "?lit"
    };

    static string FormatNumberLiteral(object value)
    {
        if (value is double d)
        {
            string s = d.ToString("R", CultureInfo.InvariantCulture);
            if (!s.Contains('.') && !s.Contains('e') && !s.Contains('E'))
                s += ".0";
            return s;
        }
        return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "0";
    }

    static bool ConvertBool(object value) => value switch
    {
        bool b => b,
        string s => s == "True" || s == "true",
        _ => false
    };

    static void PrintApply(StringBuilder sb, ApplyExpr app, int indent)
    {
        // Unwrap curried application: `f a b c` vs AST's nested Apply.
        List<Expr> args = new();
        Expr fn = app;
        while (fn is ApplyExpr a)
        {
            args.Insert(0, a.Argument);
            fn = a.Function;
        }

        PrintExprInParens(sb, fn, indent, NeedsHeadParens(fn));
        foreach (Expr arg in args)
        {
            sb.Append(' ');
            PrintExprInParens(sb, arg, indent, NeedsArgParens(arg));
        }
    }

    static bool NeedsHeadParens(Expr e) => e is
        LambdaExpr or LetExpr or IfExpr or MatchExpr or BinaryExpr
        or UnaryExpr or ActExpr or HandleExpr or RecordExpr;

    static bool NeedsArgParens(Expr e) => e is
        ApplyExpr or BinaryExpr or UnaryExpr or LambdaExpr
        or LetExpr or IfExpr or MatchExpr or ActExpr or HandleExpr or RecordExpr;

    static bool NeedsUnaryParens(Expr e) => e is BinaryExpr or ApplyExpr;

    static void PrintExprInParens(StringBuilder sb, Expr e, int indent, bool wrap)
    {
        if (wrap)
        {
            sb.Append('(');
            PrintExpr(sb, e, indent);
            sb.Append(')');
        }
        else
        {
            PrintExpr(sb, e, indent);
        }
    }

    static void PrintBinary(StringBuilder sb, BinaryExpr bin, int indent, int minPrec)
    {
        int prec = BinPrecedence(bin.Op);
        bool assocRight = IsRightAssoc(bin.Op);
        bool wrap = prec < minPrec;
        if (wrap) sb.Append('(');

        int leftMin = assocRight ? prec + 1 : prec;
        int rightMin = assocRight ? prec : prec + 1;
        PrintOperand(sb, bin.Left, indent, leftMin);
        sb.Append($" {BinaryOpSymbol(bin.Op)} ");
        PrintOperand(sb, bin.Right, indent, rightMin);

        if (wrap) sb.Append(')');
    }

    // Compound expressions (lambda, let, if, match, act, handle, record)
    // would greedily swallow the surrounding binary op as their body;
    // always wrap them as a binary operand.
    static void PrintOperand(StringBuilder sb, Expr e, int indent, int minPrec)
    {
        if (e is LambdaExpr or LetExpr or IfExpr or MatchExpr
            or ActExpr or HandleExpr or RecordExpr)
        {
            sb.Append('(');
            PrintExpr(sb, e, indent);
            sb.Append(')');
        }
        else
        {
            PrintExpr(sb, e, indent, minPrec);
        }
    }

    static int BinPrecedence(BinaryOp op) => op switch
    {
        BinaryOp.Or => 2,
        BinaryOp.And => 3,
        BinaryOp.Eq or BinaryOp.NotEq or BinaryOp.Lt or BinaryOp.Gt
            or BinaryOp.LtEq or BinaryOp.GtEq or BinaryOp.DefEq => 4,
        BinaryOp.Append or BinaryOp.Cons => 5,
        BinaryOp.Add or BinaryOp.Sub => 6,
        BinaryOp.Mul or BinaryOp.Div => 7,
        BinaryOp.Pow => 8,
        _ => 0
    };

    static bool IsRightAssoc(BinaryOp op) =>
        op is BinaryOp.Append or BinaryOp.Cons or BinaryOp.Pow;

    static string BinaryOpSymbol(BinaryOp op) => op switch
    {
        BinaryOp.Add => "+",
        BinaryOp.Sub => "-",
        BinaryOp.Mul => "*",
        BinaryOp.Div => "/",
        BinaryOp.Pow => "^",
        BinaryOp.Eq => "==",
        BinaryOp.NotEq => "/=",
        BinaryOp.Lt => "<",
        BinaryOp.Gt => ">",
        BinaryOp.LtEq => "<=",
        BinaryOp.GtEq => ">=",
        BinaryOp.DefEq => "===",
        BinaryOp.Append => "++",
        BinaryOp.Cons => "::",
        BinaryOp.And => "&",
        BinaryOp.Or => "|",
        _ => "?"
    };

    static void PrintIf(StringBuilder sb, IfExpr iff, int indent)
    {
        sb.Append("if ");
        PrintExpr(sb, iff.Condition, indent);
        sb.Append(" then ");
        PrintExpr(sb, iff.Then, indent);
        sb.Append(" else ");
        PrintExpr(sb, iff.Else, indent);
    }

    static void PrintLet(StringBuilder sb, LetExpr let, int indent)
    {
        sb.Append("let ");
        for (int i = 0; i < let.Bindings.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            LetBinding b = let.Bindings[i];
            sb.Append($"{b.Name.Value} = ");
            PrintExpr(sb, b.Value, indent + 1);
        }
        sb.Append(" in ");
        PrintExpr(sb, let.Body, indent);
    }

    static void PrintLambda(StringBuilder sb, LambdaExpr lam, int indent)
    {
        sb.Append('\\');
        for (int i = 0; i < lam.Parameters.Count; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(lam.Parameters[i].Name.Value);
        }
        sb.Append(" -> ");
        PrintExpr(sb, lam.Body, indent);
    }

    static void PrintMatch(StringBuilder sb, MatchExpr m, int indent)
    {
        sb.Append("when ");
        PrintExpr(sb, m.Scrutinee, indent);
        foreach (MatchBranch b in m.Branches)
        {
            sb.AppendLine();
            EmitSpaces(sb, (indent + 1) * 2);
            sb.Append("is ");
            PrintPattern(sb, b.Pattern);
            sb.Append(" -> ");
            PrintExpr(sb, b.Body, indent + 1);
        }
    }

    static void PrintPattern(StringBuilder sb, Pattern p)
    {
        switch (p)
        {
            case VarPattern v:
                sb.Append(v.Name.Value);
                break;
            case LiteralPattern l:
                sb.Append(FormatLiteral(new LiteralExpr(l.Value, l.Kind, l.Span)));
                break;
            case CtorPattern c:
                sb.Append(c.Constructor.Value);
                foreach (Pattern sub in c.SubPatterns)
                {
                    sb.Append(" (");
                    PrintPattern(sb, sub);
                    sb.Append(')');
                }
                break;
            case WildcardPattern:
                sb.Append("otherwise");
                break;
        }
    }

    static void PrintList(StringBuilder sb, ListExpr l, int indent)
    {
        sb.Append('[');
        for (int i = 0; i < l.Elements.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            PrintExpr(sb, l.Elements[i], indent);
        }
        sb.Append(']');
    }

    static void PrintRecord(StringBuilder sb, RecordExpr r, int indent)
    {
        if (r.TypeName is not null)
        {
            sb.Append(r.TypeName.Value.Value);
            sb.Append(' ');
        }
        sb.Append('{');
        for (int i = 0; i < r.Fields.Count; i++)
        {
            RecordFieldExpr f = r.Fields[i];
            if (i > 0) sb.Append(',');
            sb.Append(' ');
            sb.Append($"{f.FieldName.Value} = ");
            PrintExpr(sb, f.Value, indent);
        }
        sb.Append(" }");
    }

    static void PrintFieldAccess(StringBuilder sb, FieldAccessExpr fa, int indent)
    {
        // Field-access chains onto the result of ParseAtom; anything
        // that's not a bare atom needs parens.
        bool wrap = fa.Record is not (NameExpr or FieldAccessExpr or RecordExpr);
        PrintExprInParens(sb, fa.Record, indent, wrap);
        sb.Append('.');
        sb.Append(fa.FieldName.Value);
    }

    static void PrintAct(StringBuilder sb, ActExpr act, int indent)
    {
        sb.Append("act");
        foreach (ActStatement stmt in act.Statements)
        {
            sb.AppendLine();
            EmitSpaces(sb, (indent + 1) * 2);
            switch (stmt)
            {
                case ActBindStatement bind:
                    sb.Append($"{bind.Name.Value} <- ");
                    PrintExpr(sb, bind.Value, indent + 1);
                    break;
                case ActExprStatement es:
                    PrintExpr(sb, es.Expression, indent + 1);
                    break;
            }
        }
        sb.AppendLine();
        EmitSpaces(sb, indent * 2);
        sb.Append("end");
    }

    static void PrintHandle(StringBuilder sb, HandleExpr h, int indent)
    {
        sb.Append("with ");
        sb.Append(h.EffectName.Value);
        sb.Append(" { ");
        for (int i = 0; i < h.Clauses.Count; i++)
        {
            if (i > 0) sb.Append("; ");
            HandleClause c = h.Clauses[i];
            sb.Append(c.OperationName.Value);
            foreach (Name p in c.Parameters)
            {
                sb.Append($" ({p.Value})");
            }
            sb.Append($" [{c.ResumeName.Value}] -> ");
            PrintExpr(sb, c.Body, indent);
        }
        sb.Append(" } handle ");
        PrintExpr(sb, h.Computation, indent);
    }

    // ── Proof expressions ───────────────────────────────────────

    static void PrintProofExpr(StringBuilder sb, ProofExpr pe, int indent)
    {
        switch (pe)
        {
            case ReflProofExpr: sb.Append("Refl"); break;
            case AssumeProofExpr: sb.Append("assume"); break;
            case SymProofExpr sym:
                sb.Append("sym ");
                PrintProofExpr(sb, sym.Inner, indent);
                break;
            case TransProofExpr tr:
                sb.Append("trans ");
                PrintProofAtom(sb, tr.Left, indent);
                sb.Append(' ');
                PrintProofAtom(sb, tr.Right, indent);
                break;
            case CongProofExpr cg:
                sb.Append($"cong {cg.FunctionName.Value} ");
                PrintProofExpr(sb, cg.Inner, indent);
                break;
            case InductionProofExpr ind:
                sb.Append($"induction {ind.Variable.Value}");
                foreach (ProofCase pc in ind.Cases)
                {
                    sb.AppendLine();
                    EmitSpaces(sb, (indent + 1) * 2);
                    sb.Append("is ");
                    PrintPattern(sb, pc.Pattern);
                    sb.Append(" -> ");
                    PrintProofExpr(sb, pc.Body, indent + 1);
                }
                break;
            case NameProofExpr np:
                sb.Append(np.Name.Value);
                break;
            case ApplyProofExpr ap:
                sb.Append(ap.LemmaName.Value);
                foreach (Expr arg in ap.Arguments)
                {
                    sb.Append(" (");
                    PrintExpr(sb, arg, indent);
                    sb.Append(')');
                }
                break;
        }
    }

    static void PrintProofAtom(StringBuilder sb, ProofExpr pe, int indent)
    {
        if (pe is NameProofExpr or ReflProofExpr or AssumeProofExpr)
        {
            PrintProofExpr(sb, pe, indent);
        }
        else
        {
            sb.Append('(');
            PrintProofExpr(sb, pe, indent);
            sb.Append(')');
        }
    }

    // ── Utilities ────────────────────────────────────────────────

    static bool IsSimple(Expr e) => e is
        LiteralExpr or NameExpr or FieldAccessExpr or ListExpr;

    static void EmitSpaces(StringBuilder sb, int count)
    {
        for (int i = 0; i < count; i++) sb.Append(' ');
    }

    static string EscapeString(string s)
    {
        StringBuilder sb = new();
        foreach (char c in s)
        {
            sb.Append(c switch
            {
                '\\' => "\\\\",
                '"' => "\\\"",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ => c.ToString()
            });
        }
        return sb.ToString();
    }

    static string EscapeChar(object value)
    {
        long code = value switch
        {
            long l => l,
            int i => i,
            char c => c,
            string s when s.Length == 1 => s[0],
            _ => 0
        };
        return code switch
        {
            '\n' => "\\n",
            '\r' => "\\r",
            '\t' => "\\t",
            '\\' => "\\\\",
            '\'' => "\\'",
            _ => ((char)code).ToString()
        };
    }
}
