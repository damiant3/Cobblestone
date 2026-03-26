using System.Collections.Immutable;
using System.Text;
using Codex.Core;
using Codex.IR;
using Codex.Types;

namespace Codex.Emit.CSharp;

public sealed partial class CSharpEmitter
{
    void EmitExpr(StringBuilder sb, IRExpr expr, int indent)
    {
        switch (expr)
        {
            case IRIntegerLit lit:
                sb.Append($"{lit.Value}L");
                break;

            case IRNumberLit lit:
                sb.Append($"{lit.Value}m");
                break;

            case IRTextLit lit:
                // Encode string literal as CCE at compile time
                sb.Append($"\"{EscapeCceString(UnicodeToCce(lit.Value))}\"");
                break;

            case IRBoolLit lit:
                sb.Append(lit.Value ? "true" : "false");
                break;

            case IRCharLit lit:
                // Char is a CCE byte — convert Unicode value to CCE at compile time
                sb.Append($"{UnicharToCce(lit.Value)}L");
                break;

            case IRName name:
                if (name.Name == "read-line")
                    sb.Append("_Cce.FromUnicode(Console.ReadLine() ?? \"\")");
                else if (name.Name == "get-args")
                    sb.Append("Environment.GetCommandLineArgs().Select(_Cce.FromUnicode).ToList()");
                else if (name.Name == "current-dir")
                    sb.Append("_Cce.FromUnicode(Directory.GetCurrentDirectory())");
                else if (name.Name == "show")
                    sb.Append("new Func<object, string>(x => Convert.ToString(x))");
                else if (name.Name == "negate")
                    sb.Append("new Func<long, long>(x => -x)");
                else if (name.Name.Length > 0 && char.IsUpper(name.Name[0])
                    && name.Type is not FunctionType)
                    sb.Append($"new {SanitizeIdentifier(name.Name)}()");
                else if (m_definitionArity.TryGet(name.Name, out int nameArity)
                    && nameArity == 0
                    && name.Type is not FunctionType)
                    sb.Append($"{SanitizeIdentifier(name.Name)}()");
                else
                    sb.Append(SanitizeIdentifier(name.Name));
                break;

            case IRBinary bin:
                EmitBinary(sb, bin, indent);
                break;

            case IRNegate neg:
                sb.Append("(-");
                EmitExpr(sb, neg.Operand, indent);
                sb.Append(')');
                break;

            case IRIf iff:
                sb.Append('(');
                EmitExpr(sb, iff.Condition, indent);
                sb.Append(" ? ");
                EmitExpr(sb, iff.Then, indent);
                sb.Append(" : ");
                EmitExpr(sb, iff.Else, indent);
                sb.Append(')');
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
                EmitDoExpr(sb, doExpr, indent);
                break;

            case IRRecord rec:
                sb.Append($"new {SanitizeIdentifier(rec.TypeName)}(");
                for (int i = 0; i < rec.Fields.Length; i++)
                {
                    if (i > 0) sb.Append(", ");
                    EmitExpr(sb, rec.Fields[i].Value, indent);
                }
                sb.Append(')');
                break;

            case IRFieldAccess fa:
                EmitExpr(sb, fa.Record, indent);
                sb.Append('.');
                sb.Append(SanitizeIdentifier(fa.FieldName));
                break;

            case IRGetState:
                sb.Append("__state");
                break;

            case IRSetState setState:
                sb.Append("__state = ");
                EmitExpr(sb, setState.NewValue, indent);
                break;

            case IRRunState runState:
                EmitRunState(sb, runState, indent);
                break;

            case IRHandle handle:
                EmitHandle(sb, handle, indent);
                break;

            case IRError err:
                sb.Append($"throw new InvalidOperationException(\"{EscapeString(err.Message)}\")");
                break;

            default:
                sb.Append("default");
                break;
        }
    }

    void EmitApply(StringBuilder sb, IRApply app, int indent)
    {
        if (app.Function is IRName fn && fn.Name == "show")
        {
            // show produces CCE-encoded text
            sb.Append("_Cce.FromUnicode(Convert.ToString(");
            EmitExpr(sb, app.Argument, indent);
            sb.Append("))");
        }
        else if (app.Function is IRName fnCtt && fnCtt.Name == "char-to-text")
        {
            // Char is a CCE byte — produce a 1-char CCE string
            sb.Append("((char)");
            EmitExpr(sb, app.Argument, indent);
            sb.Append(").ToString()");
        }
        else if (app.Function is IRName fn2 && fn2.Name == "negate")
        {
            sb.Append("(-");
            EmitExpr(sb, app.Argument, indent);
            sb.Append(')');
        }
        else if (app.Function is IRName fn3 && fn3.Name == "print-line")
        {
            sb.Append("Console.WriteLine(_Cce.ToUnicode(");
            EmitExpr(sb, app.Argument, indent);
            sb.Append("))");
        }
        else if (app.Function is IRName fn4 && fn4.Name == "open-file")
        {
            sb.Append("File.OpenRead(");
            EmitExpr(sb, app.Argument, indent);
            sb.Append(')');
        }
        else if (app.Function is IRName fn5 && fn5.Name == "read-all")
        {
            sb.Append("new System.IO.StreamReader(");
            EmitExpr(sb, app.Argument, indent);
            sb.Append(").ReadToEnd()");
        }
        else if (app.Function is IRName fn6 && fn6.Name == "close-file")
        {
            EmitExpr(sb, app.Argument, indent);
            sb.Append(".Dispose()");
        }
        else if (app.Function is IRName fn6b && fn6b.Name == "read-file")
        {
            // File → Unicode → CCE at boundary
            sb.Append("_Cce.FromUnicode(File.ReadAllText(_Cce.ToUnicode(");
            EmitExpr(sb, app.Argument, indent);
            sb.Append(")))");
        }
        else if (app.Function is IRName fn6c && fn6c.Name == "file-exists")
        {
            // Path is CCE → convert to Unicode for filesystem
            sb.Append("File.Exists(_Cce.ToUnicode(");
            EmitExpr(sb, app.Argument, indent);
            sb.Append("))");
        }
        else if (app.Function is IRName fn6d && fn6d.Name == "get-env")
        {
            sb.Append("_Cce.FromUnicode(Environment.GetEnvironmentVariable(_Cce.ToUnicode(");
            EmitExpr(sb, app.Argument, indent);
            sb.Append(")) ?? \"\")");
        }
        else if (app.Function is IRName fn7 && fn7.Name == "text-length")
        {
            sb.Append("((long)");
            EmitExpr(sb, app.Argument, indent);
            sb.Append(".Length)");
        }
        else if (app.Function is IRName fn8 && fn8.Name == "is-letter")
        {
            // CCE: letters are 13-64
            sb.Append('(');
            EmitExpr(sb, app.Argument, indent);
            sb.Append(" >= 13L && ");
            EmitExpr(sb, app.Argument, indent);
            sb.Append(" <= 64L)");
        }
        else if (app.Function is IRName fn9 && fn9.Name == "is-digit")
        {
            // CCE: digits are 3-12
            sb.Append('(');
            EmitExpr(sb, app.Argument, indent);
            sb.Append(" >= 3L && ");
            EmitExpr(sb, app.Argument, indent);
            sb.Append(" <= 12L)");
        }
        else if (app.Function is IRName fn10 && fn10.Name == "is-whitespace")
        {
            // CCE: whitespace is 0-2
            sb.Append('(');
            EmitExpr(sb, app.Argument, indent);
            sb.Append(" <= 2L)");
        }
        else if (app.Function is IRName fn11 && fn11.Name == "text-to-integer")
        {
            // CCE text → Unicode for parsing
            sb.Append("long.Parse(_Cce.ToUnicode(");
            EmitExpr(sb, app.Argument, indent);
            sb.Append("))");
        }
        else if (app.Function is IRName fn11b && fn11b.Name == "integer-to-text")
        {
            // Number → Unicode string → CCE
            sb.Append("_Cce.FromUnicode(");
            EmitExpr(sb, app.Argument, indent);
            sb.Append(".ToString())");
        }
        else if (app.Function is IRName fn12 && fn12.Name == "char-code")
        {
            // Char -> Integer: identity at runtime (both are long)
            EmitExpr(sb, app.Argument, indent);
        }
        else if (app.Function is IRName fn13 && fn13.Name == "code-to-char")
        {
            // Integer -> Char: identity at runtime (both are long)
            EmitExpr(sb, app.Argument, indent);
        }
        else if (app.Function is IRName fn14 && fn14.Name == "list-length")
        {
            sb.Append("((long)");
            EmitExpr(sb, app.Argument, indent);
            sb.Append(".Count)");
        }
        else if (TryEmitMultiArgBuiltin(sb, app, indent))
        {
        }
        else
        {
            EmitApplyGeneral(sb, app, indent);
        }
    }

    void EmitApplyGeneral(StringBuilder sb, IRApply app, int indent)
    {
        string? ctorName = FindConstructorName(app);
        if (ctorName is not null)
        {
            List<IRExpr> args = [];
            CollectApplyArgs(app, args);
            sb.Append($"new {SanitizeIdentifier(ctorName)}(");
            for (int i = 0; i < args.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                EmitExpr(sb, args[i], indent);
            }
            sb.Append(')');
        }
        else
        {
            string? defName = FindDefinitionName(app);
            if (defName is not null
                && m_definitionArity.TryGet(defName, out int arity)
                && arity > 1)
            {
                List<IRExpr> args = [];
                CollectApplyArgs(app, args);
                if (args.Count == arity)
                {
                    sb.Append(SanitizeIdentifier(defName));
                    sb.Append('(');
                    for (int i = 0; i < args.Count; i++)
                    {
                        if (i > 0) sb.Append(", ");
                        EmitArgument(sb, args[i], indent);
                    }
                    sb.Append(')');
                }
                else if (args.Count < arity)
                {
                    EmitPartialApplication(sb, defName, arity, args, indent);
                }
                else
                {
                    EmitExpr(sb, app.Function, indent);
                    sb.Append('(');
                    EmitArgument(sb, app.Argument, indent);
                    sb.Append(')');
                }
            }
            else
            {
                EmitExpr(sb, app.Function, indent);
                sb.Append('(');
                EmitArgument(sb, app.Argument, indent);
                sb.Append(')');
            }
        }
    }

    static string? FindConstructorName(IRApply app)
    {
        IRExpr current = app.Function;
        while (current is IRApply inner)
        {
            current = inner.Function;
        }
        if (current is IRName name && name.Name.Length > 0 && char.IsUpper(name.Name[0]))
        {
            return name.Name;
        }
        return null;
    }

    static string? FindDefinitionName(IRApply app)
    {
        IRExpr current = app.Function;
        while (current is IRApply inner)
        {
            current = inner.Function;
        }
        if (current is IRName name && name.Name.Length > 0 && char.IsLower(name.Name[0]))
        {
            return name.Name;
        }
        return null;
    }

    static IRExpr? TryGetSingleListElement(IRExpr expr)
    {
        // [x] as IRList with one element
        if (expr is IRList list && list.Elements.Length == 1)
            return list.Elements[0];
        // [x] ++ [] as ConsList(x, emptyList)
        if (expr is IRBinary cons && cons.Op == IRBinaryOp.ConsList
            && cons.Right is IRList empty && empty.Elements.Length == 0)
            return cons.Left;
        return null;
    }

    static void CollectTextConcatParts(IRBinary bin, List<IRExpr> parts)
    {
        if (bin.Left is IRBinary left && left.Op == IRBinaryOp.AppendText)
            CollectTextConcatParts(left, parts);
        else
            parts.Add(bin.Left);

        if (bin.Right is IRBinary right && right.Op == IRBinaryOp.AppendText)
            CollectTextConcatParts(right, parts);
        else
            parts.Add(bin.Right);
    }

    static void CollectApplyArgs(IRApply app, List<IRExpr> args)
    {
        if (app.Function is IRApply inner)
        {
            CollectApplyArgs(inner, args);
        }
        args.Add(app.Argument);
    }

    static readonly Set<string> s_multiArgBuiltins = Set<string>.Of(
        "char-at", "char-code-at", "substring", "list-at", "list-insert-at", "list-snoc",
        "text-replace", "text-compare",
        "write-file", "run-process", "list-files", "text-split", "text-contains", "text-starts-with",
        "fork", "await", "par", "race");

    static string? FindBuiltinRoot(IRApply app)
    {
        IRExpr current = app.Function;
        while (current is IRApply inner)
            current = inner.Function;
        if (current is IRName name && s_multiArgBuiltins.Contains(name.Name))
            return name.Name;
        return null;
    }

    bool TryEmitMultiArgBuiltin(StringBuilder sb, IRApply app, int indent)
    {
        string? name = FindBuiltinRoot(app);
        if (name is null) return false;

        List<IRExpr> args = [];
        CollectApplyArgs(app, args);

        switch (name)
        {
            case "char-at" when args.Count == 2:
                sb.Append("((long)");
                EmitExpr(sb, args[0], indent);
                sb.Append("[(int)");
                EmitExpr(sb, args[1], indent);
                sb.Append("])");
                return true;

            case "char-code-at" when args.Count == 2:
                sb.Append("((long)");
                EmitExpr(sb, args[0], indent);
                sb.Append("[(int)");
                EmitExpr(sb, args[1], indent);
                sb.Append("])");
                return true;

            case "substring" when args.Count == 3:
                EmitExpr(sb, args[0], indent);
                sb.Append(".Substring((int)");
                EmitExpr(sb, args[1], indent);
                sb.Append(", (int)");
                EmitExpr(sb, args[2], indent);
                sb.Append(')');
                return true;

            case "list-at" when args.Count == 2:
                EmitExpr(sb, args[0], indent);
                sb.Append("[(int)");
                EmitExpr(sb, args[1], indent);
                sb.Append(']');
                return true;

            case "list-insert-at" when args.Count == 3:
            {
                // list-insert-at list idx item → new list with item at idx
                string elemType = args[0].Type is ListType lt ? EmitType(lt.Element) : "object";
                sb.Append($"((Func<List<{elemType}>>)(() => {{ var _l = new List<{elemType}>(");
                EmitExpr(sb, args[0], indent);
                sb.Append("); _l.Insert((int)");
                EmitExpr(sb, args[1], indent);
                sb.Append(", ");
                EmitExpr(sb, args[2], indent);
                sb.Append("); return _l; }))()");
                return true;
            }

            case "list-snoc" when args.Count == 2:
            {
                // list-snoc list item → in-place Add, O(1) amortized
                // Safe for linear accumulator patterns (list not shared after call)
                string snocListType = args[0].Type is ListType slt ? $"List<{EmitType(slt.Element)}>" : "List<object>";
                sb.Append($"((Func<{snocListType}>)(() => {{ var _l = ");
                EmitExpr(sb, args[0], indent);
                sb.Append("; _l.Add(");
                EmitExpr(sb, args[1], indent);
                sb.Append("); return _l; }))()");
                return true;
            }

            case "text-compare" when args.Count == 2:
                sb.Append("(long)string.CompareOrdinal(");
                EmitExpr(sb, args[0], indent);
                sb.Append(", ");
                EmitExpr(sb, args[1], indent);
                sb.Append(')');
                return true;

            case "text-replace" when args.Count == 3:
                EmitExpr(sb, args[0], indent);
                sb.Append(".Replace(");
                EmitExpr(sb, args[1], indent);
                sb.Append(", ");
                EmitExpr(sb, args[2], indent);
                sb.Append(')');
                return true;

            case "write-file" when args.Count == 2:
                // CCE path and content → Unicode for filesystem
                sb.Append("File.WriteAllText(_Cce.ToUnicode(");
                EmitExpr(sb, args[0], indent);
                sb.Append("), _Cce.ToUnicode(");
                EmitExpr(sb, args[1], indent);
                sb.Append("))");
                return true;

            case "run-process" when args.Count == 2:
                sb.Append("_Cce.FromUnicode(((Func<string>)(() => { var _psi = new System.Diagnostics.ProcessStartInfo(_Cce.ToUnicode(");
                EmitExpr(sb, args[0], indent);
                sb.Append("), _Cce.ToUnicode(");
                EmitExpr(sb, args[1], indent);
                sb.Append(")) { RedirectStandardOutput = true, UseShellExecute = false }; ");
                sb.Append("var _p = System.Diagnostics.Process.Start(_psi)!; ");
                sb.Append("var _o = _p.StandardOutput.ReadToEnd(); _p.WaitForExit(); return _o; }))())");
                return true;

            case "list-files" when args.Count == 2:
                sb.Append("Directory.GetFiles(_Cce.ToUnicode(");
                EmitExpr(sb, args[0], indent);
                sb.Append("), _Cce.ToUnicode(");
                EmitExpr(sb, args[1], indent);
                sb.Append(")).Select(_Cce.FromUnicode).ToList()");
                return true;

            case "text-split" when args.Count == 2:
                sb.Append("new List<string>(");
                EmitExpr(sb, args[0], indent);
                sb.Append(".Split(");
                EmitExpr(sb, args[1], indent);
                sb.Append("))");
                return true;

            case "text-contains" when args.Count == 2:
                EmitExpr(sb, args[0], indent);
                sb.Append(".Contains(");
                EmitExpr(sb, args[1], indent);
                sb.Append(')');
                return true;

            case "text-starts-with" when args.Count == 2:
                EmitExpr(sb, args[0], indent);
                sb.Append(".StartsWith(");
                EmitExpr(sb, args[1], indent);
                sb.Append(')');
                return true;

            case "fork" when args.Count == 1:
                sb.Append("Task.Run(() => (");
                EmitExpr(sb, args[0], indent);
                sb.Append(")(null))");
                return true;

            case "await" when args.Count == 1:
                sb.Append('(');
                EmitExpr(sb, args[0], indent);
                sb.Append(").Result");
                return true;

            case "par" when args.Count == 2:
                sb.Append("Task.WhenAll(");
                EmitExpr(sb, args[1], indent);
                sb.Append(".Select(_x_ => Task.Run(() => (");
                EmitExpr(sb, args[0], indent);
                sb.Append(")(_x_)))).Result.ToList()");
                return true;

            case "race" when args.Count == 1:
                sb.Append("Task.WhenAny(");
                EmitExpr(sb, args[0], indent);
                sb.Append(".Select(_t_ => Task.Run(() => _t_(null)))).Result.Result");
                return true;

            default:
                return false;
        }
    }

    void EmitBinary(StringBuilder sb, IRBinary bin, int indent)
    {
        switch (bin.Op)
        {
            case IRBinaryOp.AppendText:
            {
                // Flatten chains of ++ into a single string.Concat(a, b, c, ...)
                // to avoid O(n²) intermediate allocations.
                List<IRExpr> parts = [];
                CollectTextConcatParts(bin, parts);
                sb.Append("string.Concat(");
                for (int i = 0; i < parts.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    EmitExpr(sb, parts[i], indent);
                }
                sb.Append(')');
                break;
            }

            case IRBinaryOp.AppendList:
            {
                sb.Append("Enumerable.Concat(");
                EmitExpr(sb, bin.Left, indent);
                sb.Append(", ");
                EmitExpr(sb, bin.Right, indent);
                sb.Append(").ToList()");

                break;
            }

            case IRBinaryOp.ConsList:
            {
                // Detect [x] ++ acc pattern (single element prepend)
                sb.Append("new List<");
                sb.Append(EmitType(bin.Left.Type));
                sb.Append("> { ");
                EmitExpr(sb, bin.Left, indent);
                sb.Append(" }.Concat(");
                EmitExpr(sb, bin.Right, indent);
                sb.Append(").ToList()");
                break;
            }

            case IRBinaryOp.PowInt:
                sb.Append("(long)Math.Pow((double)");
                EmitExpr(sb, bin.Left, indent);
                sb.Append(", (double)");
                EmitExpr(sb, bin.Right, indent);
                sb.Append(')');
                break;

            default:
                string op = bin.Op switch
                {
                    IRBinaryOp.AddInt or IRBinaryOp.AddNum => "+",
                    IRBinaryOp.SubInt or IRBinaryOp.SubNum => "-",
                    IRBinaryOp.MulInt or IRBinaryOp.MulNum => "*",
                    IRBinaryOp.DivInt or IRBinaryOp.DivNum => "/",
                    IRBinaryOp.Eq => "==",
                    IRBinaryOp.NotEq => "!=",
                    IRBinaryOp.Lt => "<",
                    IRBinaryOp.Gt => ">",
                    IRBinaryOp.LtEq => "<=",
                    IRBinaryOp.GtEq => ">=",
                    IRBinaryOp.And => "&&",
                    IRBinaryOp.Or => "||",
                    _ => "+"
                };
                sb.Append('(');
                EmitExpr(sb, bin.Left, indent);
                sb.Append($" {op} ");
                EmitExpr(sb, bin.Right, indent);
                sb.Append(')');
                break;
        }
    }

    void EmitLet(StringBuilder sb, IRLet let, int indent)
    {
        string funcType = $"Func<{EmitType(let.NameType)}, {EmitType(let.Body.Type)}>";
        sb.Append("((" + funcType + ")((");
        sb.Append(SanitizeIdentifier(let.Name));
        sb.Append(") => ");
        EmitExpr(sb, let.Body, indent);
        sb.Append("))(");
        EmitExpr(sb, let.Value, indent);
        sb.Append(')');
    }

    void EmitPartialApplication(
        StringBuilder sb, string defName, int arity, List<IRExpr> appliedArgs, int indent)
    {
        int remaining = arity - appliedArgs.Count;
        int firstRemaining = appliedArgs.Count;
        m_definitionParamNames.TryGet(defName, out ImmutableArray<string> paramNames);

        for (int i = 0; i < remaining; i++)
        {
            int paramIdx = firstRemaining + i;
            string name = paramIdx < paramNames.Length
                ? SanitizeIdentifier(paramNames[paramIdx])
                : $"arg{i}";
            sb.Append($"({name}) => ");
        }
        sb.Append($"{SanitizeIdentifier(defName)}(");
        for (int i = 0; i < appliedArgs.Count; i++)
        {
            EmitArgument(sb, appliedArgs[i], indent);
            sb.Append(", ");
        }
        for (int i = 0; i < remaining; i++)
        {
            if (i > 0) sb.Append(", ");
            int paramIdx = firstRemaining + i;
            string name = paramIdx < paramNames.Length
                ? SanitizeIdentifier(paramNames[paramIdx])
                : $"arg{i}";
            sb.Append(name);
        }
        sb.Append(')');
    }

    void EmitLambda(StringBuilder sb, IRLambda lam, int indent)
    {
        sb.Append('(');
        for (int i = 0; i < lam.Parameters.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append($"{EmitType(lam.Parameters[i].Type)} {SanitizeIdentifier(lam.Parameters[i].Name)}");
        }
        sb.Append(") => ");
        EmitExpr(sb, lam.Body, indent);
    }

    void EmitList(StringBuilder sb, IRList list, int indent)
    {
        sb.Append($"new List<{EmitType(list.ElementType)}>()");
        if (list.Elements.Length > 0)
        {
            sb.Append(" { ");
            for (int i = 0; i < list.Elements.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                EmitExpr(sb, list.Elements[i], indent);
            }
            sb.Append(" }");
        }
    }

    void EmitRunState(StringBuilder sb, IRRunState runState, int indent)
    {
        string stateType = EmitType(runState.StateType);
        string resultType = EmitType(runState.ResultType);
        sb.AppendLine($"((Func<{resultType}>)(() => {{");
        string pad = new(' ', (indent + 2) * 4);
        sb.Append(pad);
        sb.Append($"{stateType} __state = ");
        EmitExpr(sb, runState.InitialState, indent + 2);
        sb.AppendLine(";");

        if (runState.Computation is IRDo doExpr)
        {
            for (int i = 0; i < doExpr.Statements.Length; i++)
            {
                IRDoStatement stmt = doExpr.Statements[i];
                bool isLast = i == doExpr.Statements.Length - 1;
                switch (stmt)
                {
                    case IRDoBind bind:
                        sb.Append(pad);
                        sb.Append($"var {SanitizeIdentifier(bind.Name)} = ");
                        EmitExpr(sb, bind.Value, indent + 2);
                        sb.AppendLine(";");
                        break;
                    case IRDoExec exec:
                        sb.Append(pad);
                        if (isLast && !IsVoidLike(runState.ResultType))
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
        }
        else
        {
            sb.Append(pad);
            sb.Append("return ");
            EmitExpr(sb, runState.Computation, indent + 2);
            sb.AppendLine(";");
        }

        sb.Append(new string(' ', (indent + 1) * 4));
        sb.Append("}))()");
    }

    void EmitHandle(StringBuilder sb, IRHandle handle, int indent)
    {
        string resultType = EmitType(handle.Type);
        sb.AppendLine($"((Func<{resultType}>)(() => {{");
        string pad = new(' ', (indent + 2) * 4);

        foreach (IRHandleClause clause in handle.Clauses)
        {
            string resumeParamType = EmitType(clause.ResumeParamType);

            sb.Append(pad);
            sb.Append($"Func<");
            foreach (CodexType pt in clause.ParameterTypes)
            {
                sb.Append(EmitType(pt));
                sb.Append(", ");
            }
            sb.Append($"Func<{resumeParamType}, {resultType}>, {resultType}>");
            sb.Append($" _handle_{SanitizeIdentifier(clause.OperationName)}_ = (");

            for (int i = 0; i < clause.Parameters.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(SanitizeIdentifier(clause.Parameters[i]));
            }
            if (clause.Parameters.Length > 0) sb.Append(", ");
            sb.Append(SanitizeIdentifier(clause.ResumeName));
            sb.AppendLine(") => {");

            string bodyPad = new(' ', (indent + 3) * 4);
            sb.Append(bodyPad);
            sb.Append("return ");
            EmitExpr(sb, clause.Body, indent + 3);
            sb.AppendLine(";");
            sb.Append(pad);
            sb.AppendLine("};");
        }

        sb.Append(pad);
        sb.Append("return ");
        EmitExpr(sb, handle.Computation, indent + 2);
        sb.AppendLine(";");

        sb.Append(new string(' ', (indent + 1) * 4));
        sb.Append("}))()");
    }
}
