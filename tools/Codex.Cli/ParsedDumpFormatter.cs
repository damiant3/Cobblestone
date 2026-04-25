using Codex.Syntax;

namespace Codex.Cli;

// Indented pretty-printer for post-parse DocumentNode. Output is deterministic
// and omits source spans so diffs surface structural changes only.
static class ParsedDumpFormatter
{
    public static void Write(TextWriter w, DocumentNode doc)
    {
        if (doc.Page is PageMarker pm)
        {
            string total = pm.TotalPages is int t ? $" of {t}" : "";
            w.WriteLine($"Page {pm.PageNumber}{total}");
        }

        foreach (CitesNode c in doc.Citations)
        {
            WriteCites(w, c, 0);
        }

        foreach (ChapterNode ch in doc.Chapters)
        {
            WriteChapter(w, ch, 0);
        }

        foreach (EffectDefinitionNode e in doc.EffectDefinitions)
        {
            WriteEffectDef(w, e, 0);
        }

        foreach (TypeDefinitionNode td in doc.TypeDefinitions)
        {
            WriteTypeDef(w, td, 0);
        }

        foreach (DefinitionNode d in doc.Definitions)
        {
            WriteDefinition(w, d, 0);
        }

        foreach (ClaimNode c in doc.Claims)
        {
            WriteClaim(w, c, 0);
        }

        foreach (ProofNode p in doc.Proofs)
        {
            WriteProof(w, p, 0);
        }
    }

    static void WriteCites(TextWriter w, CitesNode c, int indent)
    {
        string picks = c.SelectedNames.Count == 0
            ? ""
            : " select=[" + string.Join(",", c.SelectedNames.Select(t => t.Text)) + "]";
        WriteLine(w, indent, $"(cites quire={c.Quire.Text} chapter={c.ChapterTitle}{picks})");
    }

    static void WriteChapter(TextWriter w, ChapterNode ch, int indent)
    {
        WriteLine(w, indent, $"(chapter \"{ch.Title}\"");
        foreach (DocumentMember m in ch.Members)
        {
            WriteMember(w, m, indent + 1);
        }
        WriteLine(w, indent, ")");
    }

    static void WriteMember(TextWriter w, DocumentMember m, int indent)
    {
        switch (m)
        {
            case ProseBlockNode prose:
                WriteLine(w, indent, $"(prose text-length={prose.Text.Length})");
                break;
            case SectionNode sec:
                WriteLine(w, indent, $"(section \"{sec.Title}\"");
                foreach (DocumentMember sub in sec.Members)
                {
                    WriteMember(w, sub, indent + 1);
                }
                WriteLine(w, indent, ")");
                break;
            case NotationBlockNode nb:
                WriteLine(w, indent, "(notation-block");
                foreach (TypeDefinitionNode td in nb.TypeDefinitions)
                {
                    WriteTypeDef(w, td, indent + 1);
                }
                foreach (DefinitionNode d in nb.Definitions)
                {
                    WriteDefinition(w, d, indent + 1);
                }
                foreach (ClaimNode c in nb.Claims)
                {
                    WriteClaim(w, c, indent + 1);
                }
                foreach (ProofNode p in nb.Proofs)
                {
                    WriteProof(w, p, indent + 1);
                }
                WriteLine(w, indent, ")");
                break;
            default:
                WriteLine(w, indent, $"(unknown-member {m.Kind})");
                break;
        }
    }

    static void WriteDefinition(TextWriter w, DefinitionNode d, int indent)
    {
        string @params = d.Parameters.Count == 0 ? "" : " params=" + string.Join(",", d.Parameters.Select(p => p.Text));
        WriteLine(w, indent, $"(def {d.Name.Text}{@params}");
        if (d.TypeAnnotation is not null)
        {
            WriteLine(w, indent + 1, "(type");
            WriteType(w, d.TypeAnnotation.Type, indent + 2);
            WriteLine(w, indent + 1, ")");
        }
        WriteLine(w, indent + 1, "(body");
        WriteExpr(w, d.Body, indent + 2);
        WriteLine(w, indent + 1, ")");
        WriteLine(w, indent, ")");
    }

    static void WriteTypeDef(TextWriter w, TypeDefinitionNode td, int indent)
    {
        string tparams = td.TypeParameters.Count == 0 ? "" : " tparams=" + string.Join(",", td.TypeParameters.Select(p => p.Text));
        WriteLine(w, indent, $"(type-def {td.Name.Text}{tparams}");
        WriteTypeBody(w, td.Body, indent + 1);
        WriteLine(w, indent, ")");
    }

    static void WriteTypeBody(TextWriter w, TypeDefinitionBody body, int indent)
    {
        switch (body)
        {
            case RecordTypeBody rt:
                WriteLine(w, indent, "(record");
                foreach (RecordTypeFieldNode f in rt.Fields)
                {
                    WriteLine(w, indent + 1, $"(field {f.Name.Text}");
                    WriteType(w, f.Type, indent + 2);
                    WriteLine(w, indent + 1, ")");
                }
                WriteLine(w, indent, ")");
                break;
            case VariantTypeBody vt:
                WriteLine(w, indent, "(variant");
                foreach (VariantConstructorNode c in vt.Constructors)
                {
                    WriteLine(w, indent + 1, $"(ctor {c.Name.Text}");
                    foreach (VariantFieldNode vf in c.Fields)
                    {
                        string fn = vf.FieldName is null ? "_" : vf.FieldName.Text;
                        WriteLine(w, indent + 2, $"(field {fn}");
                        WriteType(w, vf.Type, indent + 3);
                        WriteLine(w, indent + 2, ")");
                    }
                    WriteLine(w, indent + 1, ")");
                }
                WriteLine(w, indent, ")");
                break;
            case ErrorTypeBody:
                WriteLine(w, indent, "(error-type-body)");
                break;
        }
    }

    static void WriteClaim(TextWriter w, ClaimNode c, int indent)
    {
        string @params = c.Parameters.Count == 0 ? "" : " params=" + string.Join(",", c.Parameters.Select(p => p.Text));
        WriteLine(w, indent, $"(claim {c.Name.Text}{@params}");
        WriteLine(w, indent + 1, "(lhs");
        WriteType(w, c.Left, indent + 2);
        WriteLine(w, indent + 1, ")");
        WriteLine(w, indent + 1, "(rhs");
        WriteType(w, c.Right, indent + 2);
        WriteLine(w, indent + 1, ")");
        WriteLine(w, indent, ")");
    }

    static void WriteProof(TextWriter w, ProofNode p, int indent)
    {
        string @params = p.Parameters.Count == 0 ? "" : " params=" + string.Join(",", p.Parameters.Select(x => x.Text));
        WriteLine(w, indent, $"(proof {p.Name.Text}{@params}");
        WriteProofExpr(w, p.Body, indent + 1);
        WriteLine(w, indent, ")");
    }

    static void WriteProofExpr(TextWriter w, ProofExprNode p, int indent)
    {
        switch (p)
        {
            case ReflNode:
                WriteLine(w, indent, "(refl)"); break;
            case AssumeNode:
                WriteLine(w, indent, "(assume)"); break;
            case SymNode s:
                WriteLine(w, indent, "(sym");
                WriteProofExpr(w, s.Inner, indent + 1);
                WriteLine(w, indent, ")"); break;
            case TransNode t:
                WriteLine(w, indent, "(trans");
                WriteProofExpr(w, t.Left, indent + 1);
                WriteProofExpr(w, t.Right, indent + 1);
                WriteLine(w, indent, ")"); break;
            case CongNode c:
                WriteLine(w, indent, $"(cong {c.FunctionName.Text}");
                WriteProofExpr(w, c.Inner, indent + 1);
                WriteLine(w, indent, ")"); break;
            case InductionNode i:
                WriteLine(w, indent, $"(induction var={i.Variable.Text}");
                foreach (ProofCaseNode c in i.Cases)
                {
                    WriteLine(w, indent + 1, "(case");
                    WritePattern(w, c.Pattern, indent + 2);
                    WriteProofExpr(w, c.Body, indent + 2);
                    WriteLine(w, indent + 1, ")");
                }
                WriteLine(w, indent, ")"); break;
            case ProofNameNode n:
                WriteLine(w, indent, $"(proof-ref {n.Name.Text})"); break;
            case ProofApplyNode a:
                WriteLine(w, indent, $"(apply {a.LemmaName.Text}");
                foreach (ExpressionNode arg in a.Arguments)
                {
                    WriteExpr(w, arg, indent + 1);
                }
                WriteLine(w, indent, ")"); break;
        }
    }

    static void WriteEffectDef(TextWriter w, EffectDefinitionNode e, int indent)
    {
        WriteLine(w, indent, $"(effect {e.Name.Text}");
        foreach (EffectOperationNode op in e.Operations)
        {
            WriteLine(w, indent + 1, $"(op {op.Name.Text}");
            WriteType(w, op.Type, indent + 2);
            WriteLine(w, indent + 1, ")");
        }
        WriteLine(w, indent, ")");
    }

    static void WriteExpr(TextWriter w, ExpressionNode e, int indent)
    {
        switch (e)
        {
            case LiteralExpressionNode lit:
                WriteLine(w, indent, $"(lit {lit.Literal.Kind} {EscapeLit(lit.Literal.Text)})");
                break;
            case NameExpressionNode name:
                WriteLine(w, indent, $"(name {name.Name.Text})");
                break;
            case ApplicationExpressionNode app:
                WriteLine(w, indent, "(apply");
                WriteExpr(w, app.Function, indent + 1);
                WriteExpr(w, app.Argument, indent + 1);
                WriteLine(w, indent, ")");
                break;
            case BinaryExpressionNode bin:
                WriteLine(w, indent, $"(binop {bin.Operator.Text}");
                WriteExpr(w, bin.Left, indent + 1);
                WriteExpr(w, bin.Right, indent + 1);
                WriteLine(w, indent, ")");
                break;
            case UnaryExpressionNode un:
                WriteLine(w, indent, $"(unop {un.Operator.Text}");
                WriteExpr(w, un.Operand, indent + 1);
                WriteLine(w, indent, ")");
                break;
            case IfExpressionNode iff:
                WriteLine(w, indent, "(if");
                WriteExpr(w, iff.Condition, indent + 1);
                WriteExpr(w, iff.Then, indent + 1);
                WriteExpr(w, iff.Else, indent + 1);
                WriteLine(w, indent, ")");
                break;
            case LetExpressionNode let:
                WriteLine(w, indent, "(let");
                foreach (LetBinding b in let.Bindings)
                {
                    WriteLine(w, indent + 1, $"(bind {b.Name.Text}");
                    WriteExpr(w, b.Value, indent + 2);
                    WriteLine(w, indent + 1, ")");
                }
                WriteLine(w, indent + 1, "(body");
                WriteExpr(w, let.Body, indent + 2);
                WriteLine(w, indent + 1, ")");
                WriteLine(w, indent, ")");
                break;
            case LambdaExpressionNode lam:
                string lp = string.Join(",", lam.Parameters.Select(p => p.Text));
                WriteLine(w, indent, $"(lambda params={lp}");
                WriteExpr(w, lam.Body, indent + 1);
                WriteLine(w, indent, ")");
                break;
            case MatchExpressionNode m:
                WriteLine(w, indent, "(match");
                WriteExpr(w, m.Scrutinee, indent + 1);
                foreach (MatchBranchNode br in m.Branches)
                {
                    WriteLine(w, indent + 1, "(branch");
                    WritePattern(w, br.Pattern, indent + 2);
                    WriteExpr(w, br.Body, indent + 2);
                    WriteLine(w, indent + 1, ")");
                }
                WriteLine(w, indent, ")");
                break;
            case ListExpressionNode list:
                WriteLine(w, indent, "(list");
                foreach (ExpressionNode el in list.Elements)
                {
                    WriteExpr(w, el, indent + 1);
                }
                WriteLine(w, indent, ")");
                break;
            case RecordExpressionNode rec:
                string rty = rec.TypeName is null ? "" : $" type={rec.TypeName.Text}";
                WriteLine(w, indent, $"(record{rty}");
                foreach (RecordFieldNode rf in rec.Fields)
                {
                    WriteLine(w, indent + 1, $"(field {rf.Name.Text}");
                    WriteExpr(w, rf.Value, indent + 2);
                    WriteLine(w, indent + 1, ")");
                }
                WriteLine(w, indent, ")");
                break;
            case FieldAccessExpressionNode fa:
                WriteLine(w, indent, $"(field-access {fa.FieldName.Text}");
                WriteExpr(w, fa.Record, indent + 1);
                WriteLine(w, indent, ")");
                break;
            case ParenthesizedExpressionNode paren:
                WriteExpr(w, paren.Inner, indent);
                break;
            case ActExpressionNode act:
                WriteLine(w, indent, "(act");
                foreach (ActStatementNode s in act.Statements)
                {
                    WriteActStatement(w, s, indent + 1);
                }
                WriteLine(w, indent, ")");
                break;
            case HandleExpressionNode h:
                WriteLine(w, indent, $"(handle effect={h.EffectName.Text}");
                WriteLine(w, indent + 1, "(compute");
                WriteExpr(w, h.Computation, indent + 2);
                WriteLine(w, indent + 1, ")");
                foreach (HandleClauseNode cl in h.Clauses)
                {
                    string cp = string.Join(",", cl.Parameters.Select(p => p.Text));
                    WriteLine(w, indent + 1, $"(clause op={cl.OperationName.Text} params={cp} resume={cl.ResumeName.Text}");
                    WriteExpr(w, cl.Body, indent + 2);
                    WriteLine(w, indent + 1, ")");
                }
                WriteLine(w, indent, ")");
                break;
            case InterpolatedStringNode interp:
                WriteLine(w, indent, "(interp");
                foreach (ExpressionNode p in interp.Parts)
                {
                    WriteExpr(w, p, indent + 1);
                }
                WriteLine(w, indent, ")");
                break;
            case ErrorExpressionNode err:
                WriteLine(w, indent, $"(error {EscapeLit(err.ErrorToken.Text)})");
                break;
        }
    }

    static void WriteActStatement(TextWriter w, ActStatementNode s, int indent)
    {
        switch (s)
        {
            case ActBindStatementNode b:
                WriteLine(w, indent, $"(bind {b.Name.Text}");
                WriteExpr(w, b.Value, indent + 1);
                WriteLine(w, indent, ")");
                break;
            case ActExprStatementNode e:
                WriteLine(w, indent, "(stmt");
                WriteExpr(w, e.Expression, indent + 1);
                WriteLine(w, indent, ")");
                break;
        }
    }

    static void WritePattern(TextWriter w, PatternNode p, int indent)
    {
        switch (p)
        {
            case VariablePatternNode v:
                WriteLine(w, indent, $"(var-pat {v.Name.Text})"); break;
            case LiteralPatternNode l:
                WriteLine(w, indent, $"(lit-pat {l.Literal.Kind} {EscapeLit(l.Literal.Text)})"); break;
            case ConstructorPatternNode c:
                WriteLine(w, indent, $"(ctor-pat {c.Constructor.Text}");
                foreach (PatternNode sub in c.SubPatterns)
                {
                    WritePattern(w, sub, indent + 1);
                }
                WriteLine(w, indent, ")"); break;
            case WildcardPatternNode:
                WriteLine(w, indent, "(wild-pat)"); break;
        }
    }

    static void WriteType(TextWriter w, TypeNode t, int indent)
    {
        switch (t)
        {
            case NamedTypeNode n:
                WriteLine(w, indent, $"(named-type {n.Name.Text})"); break;
            case FunctionTypeNode f:
                WriteLine(w, indent, "(fn-type");
                WriteType(w, f.Parameter, indent + 1);
                WriteType(w, f.Return, indent + 1);
                WriteLine(w, indent, ")"); break;
            case ApplicationTypeNode at:
                WriteLine(w, indent, "(app-type");
                WriteType(w, at.Constructor, indent + 1);
                foreach (TypeNode a in at.Arguments)
                {
                    WriteType(w, a, indent + 1);
                }
                WriteLine(w, indent, ")"); break;
            case ParenthesizedTypeNode p:
                WriteType(w, p.Inner, indent); break;
            case EffectfulTypeNode eff:
                WriteLine(w, indent, "(eff-type");
                WriteLine(w, indent + 1, "(effects");
                foreach (TypeNode e in eff.Effects)
                {
                    WriteType(w, e, indent + 2);
                }
                WriteLine(w, indent + 1, ")");
                WriteLine(w, indent + 1, "(return");
                WriteType(w, eff.Return, indent + 2);
                WriteLine(w, indent + 1, ")");
                WriteLine(w, indent, ")"); break;
            case LinearTypeNode lin:
                WriteLine(w, indent, "(linear-type");
                WriteType(w, lin.Inner, indent + 1);
                WriteLine(w, indent, ")"); break;
            case DependentTypeNode dep:
                WriteLine(w, indent, $"(dep-type param={dep.ParamName.Text}");
                WriteLine(w, indent + 1, "(param-type");
                WriteType(w, dep.ParamType, indent + 2);
                WriteLine(w, indent + 1, ")");
                WriteLine(w, indent + 1, "(body");
                WriteType(w, dep.Body, indent + 2);
                WriteLine(w, indent + 1, ")");
                WriteLine(w, indent, ")"); break;
            case IntegerTypeNode it:
                WriteLine(w, indent, $"(int-type {it.Literal.Text})"); break;
            case BinaryTypeNode bt:
                WriteLine(w, indent, $"(binop-type {bt.Operator.Text}");
                WriteType(w, bt.Left, indent + 1);
                WriteType(w, bt.Right, indent + 1);
                WriteLine(w, indent, ")"); break;
            case ProofConstraintNode pc:
                WriteLine(w, indent, $"(proof-constraint {pc.Operator.Text}");
                WriteType(w, pc.Left, indent + 1);
                WriteType(w, pc.Right, indent + 1);
                WriteLine(w, indent, ")"); break;
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
        // Keep dumps diff-safe: collapse newlines, bound length.
        string trimmed = s.Length > 80 ? s[..77] + "..." : s;
        return "\"" + trimmed.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "") + "\"";
    }
}
