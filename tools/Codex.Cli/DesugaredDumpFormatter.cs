using Codex.Ast;

namespace Codex.Cli;

// Indented pretty-printer for post-desugar Chapter. Deterministic, span-free;
// diff between two dumps shows exactly which desugaring output changed.
static class DesugaredDumpFormatter
{
    public static void Write(TextWriter w, Chapter chapter)
    {
        string quire = chapter.Quire is null ? "" : $" quire={chapter.Quire}";
        WriteLine(w, 0, $"(chapter {chapter.Name}{quire})");
        if (chapter.ChapterTitle is not null)
        {
            WriteLine(w, 0, $"(title \"{chapter.ChapterTitle}\")");
        }

        foreach (CitesDecl c in chapter.Citations)
        {
            WriteCites(w, c, 0);
        }

        foreach (EffectDef e in chapter.EffectDefs)
        {
            WriteEffect(w, e, 0);
        }

        foreach (TypeDef td in chapter.TypeDefinitions)
        {
            WriteTypeDef(w, td, 0);
        }

        foreach (Definition d in chapter.Definitions)
        {
            WriteDefinition(w, d, 0);
        }

        foreach (ClaimDef c in chapter.Claims)
        {
            WriteClaim(w, c, 0);
        }

        foreach (ProofDef p in chapter.Proofs)
        {
            WriteProof(w, p, 0);
        }
    }

    static void WriteCites(TextWriter w, CitesDecl c, int indent)
    {
        string picks = c.SelectedNames.Count == 0
            ? ""
            : " select=[" + string.Join(",", c.SelectedNames.Select(n => n.Value)) + "]";
        WriteLine(w, indent, $"(cites quire={c.Quire.Value} chapter={c.ChapterName.Value}{picks})");
    }

    static void WriteEffect(TextWriter w, EffectDef e, int indent)
    {
        WriteLine(w, indent, $"(effect {e.EffectName.Value}");
        foreach (EffectOperationDef op in e.Operations)
        {
            WriteLine(w, indent + 1, $"(op {op.Name.Value}");
            WriteType(w, op.Type, indent + 2);
            WriteLine(w, indent + 1, ")");
        }
        WriteLine(w, indent, ")");
    }

    static void WriteDefinition(TextWriter w, Definition d, int indent)
    {
        string @params = FormatParams(d.Parameters);
        string section = d.Section is null ? "" : $" section=\"{d.Section}\"";
        WriteLine(w, indent, $"(def {d.Name.Value}{@params}{section}");
        if (d.DeclaredType is not null)
        {
            WriteLine(w, indent + 1, "(declared-type");
            WriteType(w, d.DeclaredType, indent + 2);
            WriteLine(w, indent + 1, ")");
        }
        WriteLine(w, indent + 1, "(body");
        WriteExpr(w, d.Body, indent + 2);
        WriteLine(w, indent + 1, ")");
        WriteLine(w, indent, ")");
    }

    static string FormatParams(IReadOnlyList<Parameter> parameters)
    {
        if (parameters.Count == 0)
        {
            return "";
        }
        return " params=[" + string.Join(",", parameters.Select(p =>
            p.TypeAnnotation is null ? p.Name.Value : $"{p.Name.Value}:<typed>")) + "]";
    }

    static void WriteTypeDef(TextWriter w, TypeDef td, int indent)
    {
        string tparams = td.TypeParameters.Count == 0 ? "" : " tparams=[" + string.Join(",", td.TypeParameters.Select(p => p.Value)) + "]";
        switch (td)
        {
            case RecordTypeDef rt:
                WriteLine(w, indent, $"(record-type {rt.Name.Value}{tparams}");
                foreach (RecordFieldDef f in rt.Fields)
                {
                    WriteLine(w, indent + 1, $"(field {f.FieldName.Value}");
                    WriteType(w, f.Type, indent + 2);
                    WriteLine(w, indent + 1, ")");
                }
                WriteLine(w, indent, ")");
                break;
            case VariantTypeDef vt:
                WriteLine(w, indent, $"(variant-type {vt.Name.Value}{tparams}");
                foreach (VariantCtorDef c in vt.Constructors)
                {
                    WriteLine(w, indent + 1, $"(ctor {c.Name.Value}");
                    foreach (VariantFieldDef vf in c.Fields)
                    {
                        string fn = vf.FieldName?.Value ?? "_";
                        WriteLine(w, indent + 2, $"(field {fn}");
                        WriteType(w, vf.Type, indent + 3);
                        WriteLine(w, indent + 2, ")");
                    }
                    WriteLine(w, indent + 1, ")");
                }
                WriteLine(w, indent, ")");
                break;
        }
    }

    static void WriteClaim(TextWriter w, ClaimDef c, int indent)
    {
        WriteLine(w, indent, $"(claim {c.Name.Value}{FormatParams(c.Parameters)}");
        WriteLine(w, indent + 1, "(lhs");
        WriteType(w, c.Left, indent + 2);
        WriteLine(w, indent + 1, ")");
        WriteLine(w, indent + 1, "(rhs");
        WriteType(w, c.Right, indent + 2);
        WriteLine(w, indent + 1, ")");
        WriteLine(w, indent, ")");
    }

    static void WriteProof(TextWriter w, ProofDef p, int indent)
    {
        WriteLine(w, indent, $"(proof {p.Name.Value}{FormatParams(p.Parameters)}");
        WriteProofExpr(w, p.Body, indent + 1);
        WriteLine(w, indent, ")");
    }

    static void WriteProofExpr(TextWriter w, ProofExpr p, int indent)
    {
        switch (p)
        {
            case ReflProofExpr:
                WriteLine(w, indent, "(refl)"); break;
            case AssumeProofExpr:
                WriteLine(w, indent, "(assume)"); break;
            case SymProofExpr s:
                WriteLine(w, indent, "(sym");
                WriteProofExpr(w, s.Inner, indent + 1);
                WriteLine(w, indent, ")"); break;
            case TransProofExpr t:
                WriteLine(w, indent, "(trans");
                WriteProofExpr(w, t.Left, indent + 1);
                WriteProofExpr(w, t.Right, indent + 1);
                WriteLine(w, indent, ")"); break;
            case CongProofExpr c:
                WriteLine(w, indent, $"(cong {c.FunctionName.Value}");
                WriteProofExpr(w, c.Inner, indent + 1);
                WriteLine(w, indent, ")"); break;
            case InductionProofExpr i:
                WriteLine(w, indent, $"(induction var={i.Variable.Value}");
                foreach (ProofCase c in i.Cases)
                {
                    WriteLine(w, indent + 1, "(case");
                    WritePattern(w, c.Pattern, indent + 2);
                    WriteProofExpr(w, c.Body, indent + 2);
                    WriteLine(w, indent + 1, ")");
                }
                WriteLine(w, indent, ")"); break;
            case NameProofExpr n:
                WriteLine(w, indent, $"(proof-ref {n.Name.Value})"); break;
            case ApplyProofExpr a:
                WriteLine(w, indent, $"(apply {a.LemmaName.Value}");
                foreach (Expr arg in a.Arguments)
                {
                    WriteExpr(w, arg, indent + 1);
                }
                WriteLine(w, indent, ")"); break;
        }
    }

    public static void WriteExpr(TextWriter w, Expr e, int indent)
    {
        switch (e)
        {
            case LiteralExpr lit:
                WriteLine(w, indent, $"(lit {lit.Kind} {EscapeLit(lit.Value?.ToString() ?? "null")})");
                break;
            case NameExpr n:
                WriteLine(w, indent, $"(name {n.Name.Value})");
                break;
            case ApplyExpr app:
                WriteLine(w, indent, "(apply");
                WriteExpr(w, app.Function, indent + 1);
                WriteExpr(w, app.Argument, indent + 1);
                WriteLine(w, indent, ")");
                break;
            case BinaryExpr bin:
                WriteLine(w, indent, $"(binop {bin.Op}");
                WriteExpr(w, bin.Left, indent + 1);
                WriteExpr(w, bin.Right, indent + 1);
                WriteLine(w, indent, ")");
                break;
            case UnaryExpr un:
                WriteLine(w, indent, $"(unop {un.Op}");
                WriteExpr(w, un.Operand, indent + 1);
                WriteLine(w, indent, ")");
                break;
            case IfExpr iff:
                WriteLine(w, indent, "(if");
                WriteExpr(w, iff.Condition, indent + 1);
                WriteExpr(w, iff.Then, indent + 1);
                WriteExpr(w, iff.Else, indent + 1);
                WriteLine(w, indent, ")");
                break;
            case LetExpr let:
                WriteLine(w, indent, "(let");
                foreach (LetBinding b in let.Bindings)
                {
                    WriteLine(w, indent + 1, $"(bind {b.Name.Value}");
                    WriteExpr(w, b.Value, indent + 2);
                    WriteLine(w, indent + 1, ")");
                }
                WriteLine(w, indent + 1, "(body");
                WriteExpr(w, let.Body, indent + 2);
                WriteLine(w, indent + 1, ")");
                WriteLine(w, indent, ")");
                break;
            case LambdaExpr lam:
                WriteLine(w, indent, $"(lambda{FormatParams(lam.Parameters)}");
                WriteExpr(w, lam.Body, indent + 1);
                WriteLine(w, indent, ")");
                break;
            case MatchExpr m:
                WriteLine(w, indent, "(match");
                WriteExpr(w, m.Scrutinee, indent + 1);
                foreach (MatchBranch br in m.Branches)
                {
                    WriteLine(w, indent + 1, "(branch");
                    WritePattern(w, br.Pattern, indent + 2);
                    WriteExpr(w, br.Body, indent + 2);
                    WriteLine(w, indent + 1, ")");
                }
                WriteLine(w, indent, ")");
                break;
            case ListExpr list:
                WriteLine(w, indent, "(list");
                foreach (Expr el in list.Elements)
                {
                    WriteExpr(w, el, indent + 1);
                }
                WriteLine(w, indent, ")");
                break;
            case RecordExpr rec:
                string rty = rec.TypeName is null ? "" : $" type={rec.TypeName.Value.Value}";
                WriteLine(w, indent, $"(record{rty}");
                foreach (RecordFieldExpr rf in rec.Fields)
                {
                    WriteLine(w, indent + 1, $"(field {rf.FieldName.Value}");
                    WriteExpr(w, rf.Value, indent + 2);
                    WriteLine(w, indent + 1, ")");
                }
                WriteLine(w, indent, ")");
                break;
            case FieldAccessExpr fa:
                WriteLine(w, indent, $"(field-access {fa.FieldName.Value}");
                WriteExpr(w, fa.Record, indent + 1);
                WriteLine(w, indent, ")");
                break;
            case ActExpr act:
                WriteLine(w, indent, "(act");
                foreach (ActStatement s in act.Statements)
                {
                    switch (s)
                    {
                        case ActBindStatement b:
                            WriteLine(w, indent + 1, $"(bind {b.Name.Value}");
                            WriteExpr(w, b.Value, indent + 2);
                            WriteLine(w, indent + 1, ")");
                            break;
                        case ActExprStatement es:
                            WriteLine(w, indent + 1, "(stmt");
                            WriteExpr(w, es.Expression, indent + 2);
                            WriteLine(w, indent + 1, ")");
                            break;
                    }
                }
                WriteLine(w, indent, ")");
                break;
            case HandleExpr h:
                WriteLine(w, indent, $"(handle effect={h.EffectName.Value}");
                WriteLine(w, indent + 1, "(compute");
                WriteExpr(w, h.Computation, indent + 2);
                WriteLine(w, indent + 1, ")");
                foreach (HandleClause cl in h.Clauses)
                {
                    string cp = string.Join(",", cl.Parameters.Select(p => p.Value));
                    WriteLine(w, indent + 1, $"(clause op={cl.OperationName.Value} params={cp} resume={cl.ResumeName.Value}");
                    WriteExpr(w, cl.Body, indent + 2);
                    WriteLine(w, indent + 1, ")");
                }
                WriteLine(w, indent, ")");
                break;
            case ErrorExpr err:
                WriteLine(w, indent, $"(error {EscapeLit(err.Message)})");
                break;
        }
    }

    static void WritePattern(TextWriter w, Pattern p, int indent)
    {
        switch (p)
        {
            case VarPattern v:
                WriteLine(w, indent, $"(var-pat {v.Name.Value})"); break;
            case LiteralPattern l:
                WriteLine(w, indent, $"(lit-pat {l.Kind} {EscapeLit(l.Value?.ToString() ?? "null")})"); break;
            case CtorPattern c:
                WriteLine(w, indent, $"(ctor-pat {c.Constructor.Value}");
                foreach (Pattern sub in c.SubPatterns)
                {
                    WritePattern(w, sub, indent + 1);
                }
                WriteLine(w, indent, ")"); break;
            case WildcardPattern:
                WriteLine(w, indent, "(wild-pat)"); break;
        }
    }

    public static void WriteType(TextWriter w, TypeExpr t, int indent)
    {
        switch (t)
        {
            case NamedTypeExpr n:
                WriteLine(w, indent, $"(named-type {n.Name.Value})"); break;
            case FunctionTypeExpr f:
                WriteLine(w, indent, "(fn-type");
                WriteType(w, f.Parameter, indent + 1);
                WriteType(w, f.Return, indent + 1);
                WriteLine(w, indent, ")"); break;
            case AppliedTypeExpr at:
                WriteLine(w, indent, "(app-type");
                WriteType(w, at.Constructor, indent + 1);
                foreach (TypeExpr a in at.Arguments)
                {
                    WriteType(w, a, indent + 1);
                }
                WriteLine(w, indent, ")"); break;
            case EffectfulTypeExpr eff:
                WriteLine(w, indent, "(eff-type");
                WriteLine(w, indent + 1, "(effects");
                foreach (TypeExpr e in eff.Effects)
                {
                    WriteType(w, e, indent + 2);
                }
                WriteLine(w, indent + 1, ")");
                WriteLine(w, indent + 1, "(return");
                WriteType(w, eff.Return, indent + 2);
                WriteLine(w, indent + 1, ")");
                WriteLine(w, indent, ")"); break;
            case LinearTypeExpr lin:
                WriteLine(w, indent, "(linear-type");
                WriteType(w, lin.Inner, indent + 1);
                WriteLine(w, indent, ")"); break;
            case DependentTypeExpr dep:
                WriteLine(w, indent, $"(dep-type param={dep.ParamName.Value}");
                WriteLine(w, indent + 1, "(param-type");
                WriteType(w, dep.ParamType, indent + 2);
                WriteLine(w, indent + 1, ")");
                WriteLine(w, indent + 1, "(body");
                WriteType(w, dep.Body, indent + 2);
                WriteLine(w, indent + 1, ")");
                WriteLine(w, indent, ")"); break;
            case IntegerLiteralTypeExpr it:
                WriteLine(w, indent, $"(int-type {it.Value})"); break;
            case BinaryTypeExpr bt:
                WriteLine(w, indent, $"(binop-type {bt.Op}");
                WriteType(w, bt.Left, indent + 1);
                WriteType(w, bt.Right, indent + 1);
                WriteLine(w, indent, ")"); break;
            case ProofConstraintExpr pc:
                WriteLine(w, indent, $"(proof-constraint {pc.Op}");
                WriteType(w, pc.Left, indent + 1);
                WriteType(w, pc.Right, indent + 1);
                WriteLine(w, indent, ")"); break;
        }
    }

    public static void WriteLine(TextWriter w, int indent, string text)
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
