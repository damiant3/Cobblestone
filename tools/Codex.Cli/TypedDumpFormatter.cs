using Codex.Ast;
using Codex.Core;
using Codex.Semantics;
using Codex.Types;

namespace Codex.Cli;

// Pretty-printer for the elaborated AST (section D/1 ExprTypes map).
// Produces the same s-expression shape as DesugaredDumpFormatter but
// annotates every Expr-level node with `: <type>` so diffs highlight
// inference changes, not just structural ones.
static class TypedDumpFormatter
{
    public static void Write(
        TextWriter w,
        ResolvedChapter resolved,
        Map<string, CodexType> topLevelTypes,
        IReadOnlyDictionary<Expr, CodexType> exprTypes)
    {
        WriteLine(w, 0, $"(typed-chapter {resolved.Chapter.Name})");

        List<string> sortedNames = [];
        foreach (KeyValuePair<string, CodexType> kv in topLevelTypes)
        {
            sortedNames.Add(kv.Key);
        }
        sortedNames.Sort(StringComparer.Ordinal);
        WriteLine(w, 0, "(top-level-types");
        foreach (string name in sortedNames)
        {
            WriteLine(w, 1, $"({name} : {topLevelTypes[name]})");
        }
        WriteLine(w, 0, ")");
        w.WriteLine();

        foreach (Definition def in resolved.Chapter.Definitions)
        {
            WriteDefinition(w, def, exprTypes, 0);
        }
    }

    static void WriteDefinition(
        TextWriter w,
        Definition def,
        IReadOnlyDictionary<Expr, CodexType> exprTypes,
        int indent)
    {
        WriteLine(w, indent, $"(def {def.Name.Value}");
        WriteExpr(w, def.Body, exprTypes, indent + 1);
        WriteLine(w, indent, ")");
    }

    static void WriteExpr(
        TextWriter w,
        Expr expr,
        IReadOnlyDictionary<Expr, CodexType> exprTypes,
        int indent)
    {
        string annot = exprTypes.TryGetValue(expr, out CodexType? t)
            ? $" : {t}"
            : " : <missing>";

        switch (expr)
        {
            case LiteralExpr lit:
                WriteLine(w, indent, $"(lit {lit.Kind} {EscapeLit(lit.Value?.ToString() ?? "null")}{annot})");
                break;
            case NameExpr n:
                WriteLine(w, indent, $"(name {n.Name.Value}{annot})");
                break;
            case ApplyExpr app:
                WriteLine(w, indent, $"(apply{annot}");
                WriteExpr(w, app.Function, exprTypes, indent + 1);
                WriteExpr(w, app.Argument, exprTypes, indent + 1);
                WriteLine(w, indent, ")");
                break;
            case BinaryExpr bin:
                WriteLine(w, indent, $"(binop {bin.Op}{annot}");
                WriteExpr(w, bin.Left, exprTypes, indent + 1);
                WriteExpr(w, bin.Right, exprTypes, indent + 1);
                WriteLine(w, indent, ")");
                break;
            case UnaryExpr un:
                WriteLine(w, indent, $"(unop {un.Op}{annot}");
                WriteExpr(w, un.Operand, exprTypes, indent + 1);
                WriteLine(w, indent, ")");
                break;
            case IfExpr iff:
                WriteLine(w, indent, $"(if{annot}");
                WriteExpr(w, iff.Condition, exprTypes, indent + 1);
                WriteExpr(w, iff.Then, exprTypes, indent + 1);
                WriteExpr(w, iff.Else, exprTypes, indent + 1);
                WriteLine(w, indent, ")");
                break;
            case LetExpr let:
                WriteLine(w, indent, $"(let{annot}");
                foreach (LetBinding b in let.Bindings)
                {
                    WriteLine(w, indent + 1, $"(bind {b.Name.Value}");
                    WriteExpr(w, b.Value, exprTypes, indent + 2);
                    WriteLine(w, indent + 1, ")");
                }
                WriteLine(w, indent + 1, "(body");
                WriteExpr(w, let.Body, exprTypes, indent + 2);
                WriteLine(w, indent + 1, ")");
                WriteLine(w, indent, ")");
                break;
            case LambdaExpr lam:
                WriteLine(w, indent, $"(lambda{annot}");
                WriteExpr(w, lam.Body, exprTypes, indent + 1);
                WriteLine(w, indent, ")");
                break;
            case MatchExpr m:
                WriteLine(w, indent, $"(match{annot}");
                WriteExpr(w, m.Scrutinee, exprTypes, indent + 1);
                foreach (MatchBranch br in m.Branches)
                {
                    WriteLine(w, indent + 1, "(branch");
                    WriteExpr(w, br.Body, exprTypes, indent + 2);
                    WriteLine(w, indent + 1, ")");
                }
                WriteLine(w, indent, ")");
                break;
            case ListExpr list:
                WriteLine(w, indent, $"(list{annot}");
                foreach (Expr el in list.Elements)
                {
                    WriteExpr(w, el, exprTypes, indent + 1);
                }
                WriteLine(w, indent, ")");
                break;
            case RecordExpr rec:
                string rty = rec.TypeName is null ? "" : $" type={rec.TypeName.Value.Value}";
                WriteLine(w, indent, $"(record{rty}{annot}");
                foreach (RecordFieldExpr rf in rec.Fields)
                {
                    WriteLine(w, indent + 1, $"(field {rf.FieldName.Value}");
                    WriteExpr(w, rf.Value, exprTypes, indent + 2);
                    WriteLine(w, indent + 1, ")");
                }
                WriteLine(w, indent, ")");
                break;
            case FieldAccessExpr fa:
                WriteLine(w, indent, $"(field-access {fa.FieldName.Value}{annot}");
                WriteExpr(w, fa.Record, exprTypes, indent + 1);
                WriteLine(w, indent, ")");
                break;
            case ActExpr act:
                WriteLine(w, indent, $"(act{annot}");
                foreach (ActStatement s in act.Statements)
                {
                    switch (s)
                    {
                        case ActBindStatement b:
                            WriteLine(w, indent + 1, $"(bind {b.Name.Value}");
                            WriteExpr(w, b.Value, exprTypes, indent + 2);
                            WriteLine(w, indent + 1, ")");
                            break;
                        case ActExprStatement es:
                            WriteLine(w, indent + 1, "(stmt");
                            WriteExpr(w, es.Expression, exprTypes, indent + 2);
                            WriteLine(w, indent + 1, ")");
                            break;
                    }
                }
                WriteLine(w, indent, ")");
                break;
            case HandleExpr h:
                WriteLine(w, indent, $"(handle effect={h.EffectName.Value}{annot}");
                WriteExpr(w, h.Computation, exprTypes, indent + 1);
                foreach (HandleClause cl in h.Clauses)
                {
                    WriteLine(w, indent + 1, $"(clause op={cl.OperationName.Value} resume={cl.ResumeName.Value}");
                    WriteExpr(w, cl.Body, exprTypes, indent + 2);
                    WriteLine(w, indent + 1, ")");
                }
                WriteLine(w, indent, ")");
                break;
            case ErrorExpr err:
                WriteLine(w, indent, $"(error {EscapeLit(err.Message)}{annot})");
                break;
            default:
                WriteLine(w, indent, $"(unknown {expr.GetType().Name}{annot})");
                break;
        }
    }

    static void WriteLine(TextWriter w, int indent, string text)
    {
        for (int i = 0; i < indent; i++)
        {
            w.Write("  ");
        }
        w.WriteLine(text);
    }

    static string EscapeLit(string s)
    {
        string trimmed = s.Length > 80 ? s[..77] + "..." : s;
        return "\"" + trimmed.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "") + "\"";
    }
}
