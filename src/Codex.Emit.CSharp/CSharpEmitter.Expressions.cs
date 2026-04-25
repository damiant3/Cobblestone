using System.Collections.Immutable;
using System.Text;
using Codex.Core;
using Codex.IR;
using Codex.Types;

namespace Codex.Emit.CSharp;

public sealed partial class CSharpEmitter
{
    protected override void EmitFuelExhaustedToken(StringBuilder sb) =>
        sb.Append("/* fuel exhausted */ default");

    protected override void EmitUnhandled(StringBuilder sb, IRExpr expr, int indent) =>
        sb.Append("default");

    protected override void EmitIntegerLit(StringBuilder sb, IRIntegerLit lit, int indent) =>
        sb.Append($"{lit.Value}L");

    protected override void EmitNumberLit(StringBuilder sb, IRNumberLit lit, int indent)
    {
        sb.Append(lit.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        sb.Append('d');
    }

    protected override void EmitTextLit(StringBuilder sb, IRTextLit lit, int indent) =>
        sb.Append($"\"{EscapeCceString(UnicodeToCce(lit.Value))}\"");

    protected override void EmitBoolLit(StringBuilder sb, IRBoolLit lit, int indent) =>
        sb.Append(lit.Value ? "true" : "false");

    protected override void EmitCharLit(StringBuilder sb, IRCharLit lit, int indent) =>
        sb.Append($"{UnicharToCce(lit.Value)}L");

    protected override void EmitName(StringBuilder sb, IRName name, int indent)
    {
        if (name.Name == "read-line")
        {
            sb.Append("_Cce.FromUnicode(Console.ReadLine() ?? \"\")");
        }
        else if (name.Name == "get-args")
        {
            sb.Append("Environment.GetCommandLineArgs().Select(_Cce.FromUnicode).ToList()");
        }
        else if (name.Name == "current-dir")
        {
            sb.Append("_Cce.FromUnicode(Directory.GetCurrentDirectory())");
        }
        else if (name.Name == "__heap-save")
        {
            sb.Append("_Buf.heap_save()");
        }
        else if (name.Name == "__heap-restore")
        {
            sb.Append("new Func<object, long>(_p => _Buf.heap_restore(_p))");
        }
        else if (name.Name == "__heap-advance")
        {
            sb.Append("new Func<object, long>(_n => _Buf.heap_advance(_n))");
        }
        else if (name.Name == "__list-with-capacity")
        {
            sb.Append("new Func<object, List<long>>(_c => _Buf.list_with_capacity(_c))");
        }
        else if (name.Name == "__buf-write-byte")
        {
            sb.Append("new Func<object, Func<object, Func<object, long>>>(_b => _o => _v => _Buf.buf_write_byte(_b, _o, _v))");
        }
        else if (name.Name == "__buf-write-bytes")
        {
            sb.Append("new Func<object, Func<object, Func<object, long>>>(_b => _o => _vs => _Buf.buf_write_bytes(_b, _o, _vs))");
        }
        else if (name.Name == "__buf-read-bytes")
        {
            sb.Append("new Func<object, Func<object, Func<object, List<long>>>>(_b => _o => _n => _Buf.buf_read_bytes(_b, _o, _n))");
        }
        else if (name.Name == "show")
        {
            sb.Append("new Func<object, string>(x => Convert.ToString(x))");
        }
        else if (name.Name == "negate")
        {
            sb.Append("new Func<long, long>(x => -x)");
        }
        else if (name.Name.Length > 0 && char.IsUpper(name.Name[0])
            && name.Type is not FunctionType)
        {
            sb.Append($"new {SanitizeIdentifier(name.Name)}{CtorTypeArgs(name.Type)}()");
        }
        else if (m_definitionArity.TryGet(name.Name, out int nameArity)
            && nameArity == 0
            && name.Type is not FunctionType)
        {
            sb.Append($"{SanitizeIdentifier(name.Name)}()");
        }
        else
        {
            sb.Append(SanitizeIdentifier(name.Name));
        }
    }

    protected override void EmitNegate(StringBuilder sb, IRNegate neg, int indent)
    {
        sb.Append("(-");
        EmitExpr(sb, neg.Operand, indent);
        sb.Append(')');
    }

    protected override void EmitIf(StringBuilder sb, IRIf iff, int indent)
    {
        sb.Append('(');
        EmitExpr(sb, iff.Condition, indent);
        sb.Append(" ? ");
        EmitExpr(sb, iff.Then, indent);
        sb.Append(" : ");
        EmitExpr(sb, iff.Else, indent);
        sb.Append(')');
    }

    protected override void EmitAct(StringBuilder sb, IRAct act, int indent) =>
        EmitActExpr(sb, act, indent);

    protected override void EmitRecord(StringBuilder sb, IRRecord rec, int indent)
    {
        sb.Append($"new {SanitizeIdentifier(rec.TypeName)}(");
        for (int i = 0; i < rec.Fields.Length; i++)
        {
            if (i > 0)
                sb.Append(", ");

            EmitExpr(sb, rec.Fields[i].Value, indent);
        }
        sb.Append(')');
    }

    protected override void EmitFieldAccess(StringBuilder sb, IRFieldAccess fa, int indent)
    {
        EmitExpr(sb, fa.Record, indent);
        sb.Append('.');
        sb.Append(SanitizeIdentifier(fa.FieldName));
    }

    protected override void EmitGetState(StringBuilder sb, IRGetState get, int indent) =>
        sb.Append("__state");

    protected override void EmitSetState(StringBuilder sb, IRSetState setState, int indent)
    {
        sb.Append("__state = ");
        EmitExpr(sb, setState.NewValue, indent);
    }

    protected override void EmitError(StringBuilder sb, IRError err, int indent) =>
        sb.Append($"throw new InvalidOperationException(\"{EscapeString(err.Message)}\")");

    protected override void EmitApply(StringBuilder sb, IRApply app, int indent)
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
        else if (app.Function is IRName fnAbs && fnAbs.Name == "abs")
        {
            sb.Append("Math.Abs(");
            EmitExpr(sb, app.Argument, indent);
            sb.Append(')');
        }
        else if (app.Function is IRName fnBitNot && fnBitNot.Name == "bit-not")
        {
            sb.Append("(~");
            EmitExpr(sb, app.Argument, indent);
            sb.Append(')');
        }
        else if (app.Function is IRName fn3 && fn3.Name == "print-line")
        {
            sb.Append("((Func<object>)(() => { Console.WriteLine(_Cce.ToUnicode(");
            EmitExpr(sb, app.Argument, indent);
            sb.Append(")); return null; }))()");
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
        else if (app.Function is IRName fn11d && fn11d.Name == "text-to-double-bits")
        {
            sb.Append("BitConverter.DoubleToInt64Bits(double.Parse(_Cce.ToUnicode(");
            EmitExpr(sb, app.Argument, indent);
            sb.Append("), System.Globalization.CultureInfo.InvariantCulture))");
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
            sb.Append($"new {SanitizeIdentifier(ctorName)}{CtorTypeArgs(app.Type)}(");
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
            current = inner.Function;
        if (current is IRName name && name.Name.Length > 0 && char.IsUpper(name.Name[0]))
            return name.Name;
        return null;
    }

    static string? FindDefinitionName(IRApply app)
    {
        IRExpr current = app.Function;
        while (current is IRApply inner)
            current = inner.Function;
        if (current is IRName name && name.Name.Length > 0 && char.IsLower(name.Name[0]))
            return name.Name;
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
        {
            return cons.Left;
        }

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
            CollectApplyArgs(inner, args);
        args.Add(app.Argument);
    }

    static readonly Set<string> s_multiArgBuiltins = Set<string>.Of(
        "char-at", "char-code-at", "substring", "list-at", "list-insert-at", "list-set-at", "list-snoc",
        "text-replace", "text-compare", "text-concat-list",
        "write-file", "write-binary", "run-process", "run-process-full", "process-exit", "list-files", "text-split", "text-contains", "text-starts-with",
        "fork", "await", "par", "race",
        "__record-set",
        "__linked-list-empty", "__linked-list-push", "__linked-list-to-list",
        "__heap-save", "__heap-restore", "__heap-advance",
        "__list-with-capacity",
        "__buf-write-byte", "__buf-write-bytes", "__buf-read-bytes",
        "int-mod", "min", "max",
        "bit-and", "bit-or", "bit-xor", "bit-shl", "bit-shr");

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
                // list-insert-at list idx item → alias list and insert in place.
                // Matches list-snoc / list-set-at emit shape; drops a redundant
                // O(N) copy that dominated costs in hot sites like ExprTypes
                // populate, arity-map build, env-bind.
                string elemType = args[0].Type is ListType lt ? EmitType(lt.Element) : "object";
                sb.Append($"((Func<List<{elemType}>>)(() => {{ var _l = ");
                EmitExpr(sb, args[0], indent);
                sb.Append("; _l.Insert((int)");
                EmitExpr(sb, args[1], indent);
                sb.Append(", ");
                EmitExpr(sb, args[2], indent);
                sb.Append("); return _l; }))()");
                return true;
            }

            case "list-set-at" when args.Count == 3:
            {
                // list-set-at list idx val → list (in-place slot set, returns same ref)
                string elemType = args[0].Type is ListType lst ? EmitType(lst.Element) : "object";
                sb.Append($"((Func<List<{elemType}>>)(() => {{ var _l = ");
                EmitExpr(sb, args[0], indent);
                sb.Append("; _l[(int)");
                EmitExpr(sb, args[1], indent);
                sb.Append("] = ");
                EmitExpr(sb, args[2], indent);
                sb.Append("; return _l; }))()");
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
                // File.WriteAllText returns void — wrap to return null for expression context
                sb.Append("((Func<object>)(() => { File.WriteAllText(_Cce.ToUnicode(");
                EmitExpr(sb, args[0], indent);
                sb.Append("), _Cce.ToUnicode(");
                EmitExpr(sb, args[1], indent);
                sb.Append(")); return null; }))()");
                return true;

            case "write-binary" when args.Count == 1:
                // Write List<long> as raw bytes to stdout
                sb.Append("((Func<object>)(() => { var _bl = (List<long>)");
                EmitExpr(sb, args[0], indent);
                sb.Append("; using var _s = Console.OpenStandardOutput(); foreach (var _b in _bl) _s.WriteByte((byte)_b); _s.Flush(); return null; }))()");
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

            case "process-exit" when args.Count == 1:
                // Terminates the process with the given exit code. Emitted as
                // a lambda that returns null so the expression type-checks as
                // Nothing; control never returns from Environment.Exit.
                sb.Append("((Func<object>)(() => { Environment.Exit((int)");
                EmitExpr(sb, args[0], indent);
                sb.Append("); return null; }))()");
                return true;

            case "run-process-full" when args.Count == 2:
                // Returns a ProcessResult record populated with CCE-encoded
                // stdout + stderr and the process's native int exit-code
                // widened to long (Codex Integer). User code gets the fields
                // via record access (r.stdout, r.stderr, r.exit-code).
                sb.Append("((Func<ProcessResult>)(() => { var _psi = new System.Diagnostics.ProcessStartInfo(_Cce.ToUnicode(");
                EmitExpr(sb, args[0], indent);
                sb.Append("), _Cce.ToUnicode(");
                EmitExpr(sb, args[1], indent);
                sb.Append(")) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false }; ");
                sb.Append("var _p = System.Diagnostics.Process.Start(_psi)!; ");
                sb.Append("var _so = _p.StandardOutput.ReadToEnd(); ");
                sb.Append("var _se = _p.StandardError.ReadToEnd(); ");
                sb.Append("_p.WaitForExit(); ");
                sb.Append("return new ProcessResult(_Cce.FromUnicode(_so), _Cce.FromUnicode(_se), (long)_p.ExitCode); }))()");
                return true;

            case "list-files" when args.Count == 2:
                sb.Append("Directory.GetFiles(_Cce.ToUnicode(");
                EmitExpr(sb, args[0], indent);
                sb.Append("), _Cce.ToUnicode(");
                EmitExpr(sb, args[1], indent);
                sb.Append(")).Select(_Cce.FromUnicode).ToList()");
                return true;

            case "text-concat-list" when args.Count == 1:
                sb.Append("string.Concat(");
                EmitExpr(sb, args[0], indent);
                sb.Append(')');
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
                // Ordinal: CCE bytes 13-38 are the lowercase-letter codes (U+000D..U+0026,
                // i.e. control chars and punctuation). Default culture-aware StartsWith
                // treats those as ignorable, so something like "--cli=...".StartsWith("--watchdog=")
                // returns true. Force byte-level comparison.
                EmitExpr(sb, args[0], indent);
                sb.Append(".StartsWith(");
                EmitExpr(sb, args[1], indent);
                sb.Append(", StringComparison.Ordinal)");
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

            case Builtins.LinkedListEmpty when args.Count == 1:
            {
                string elemType = app.Type is LinkedListType llt2 ? EmitType(llt2.Element) : "object";
                sb.Append($"new List<{elemType}>()");
                return true;
            }
            case Builtins.LinkedListPush when args.Count == 2:
            {
                string elemType = app.Type is LinkedListType llt3 ? EmitType(llt3.Element) : "object";
                sb.Append($"((Func<List<{elemType}>, {elemType}, List<{elemType}>>)((_ll, _v) => {{ _ll.Add(_v); return _ll; }}))(");
                EmitExpr(sb, args[0], indent);
                sb.Append(", ");
                EmitExpr(sb, args[1], indent);
                sb.Append(')');
                return true;
            }
            case Builtins.LinkedListToList when args.Count == 1:
                EmitExpr(sb, args[0], indent);
                return true;

            case Builtins.HeapSave:
                sb.Append("_Buf.heap_save()");
                return true;
            case Builtins.HeapRestore when args.Count >= 1:
                sb.Append("_Buf.heap_restore(");
                EmitExpr(sb, args[0], indent);
                sb.Append(')');
                return true;
            case Builtins.HeapAdvance when args.Count >= 1:
                sb.Append("_Buf.heap_advance(");
                EmitExpr(sb, args[0], indent);
                sb.Append(')');
                return true;
            case Builtins.ListWithCapacity when args.Count >= 1:
                {
                    string capElemType = app.Type is ListType clt ? EmitType(clt.Element) : "object";
                    sb.Append($"new List<{capElemType}>((int)(long)");
                    EmitExpr(sb, args[0], indent);
                    sb.Append(')');
                    return true;
                }
            case Builtins.BufWriteByte when args.Count >= 3:
                sb.Append("_Buf.buf_write_byte(");
                EmitExpr(sb, args[0], indent);
                sb.Append(", ");
                EmitExpr(sb, args[1], indent);
                sb.Append(", ");
                EmitExpr(sb, args[2], indent);
                sb.Append(')');
                return true;
            case Builtins.BufWriteBytes when args.Count >= 3:
                sb.Append("_Buf.buf_write_bytes(");
                EmitExpr(sb, args[0], indent);
                sb.Append(", ");
                EmitExpr(sb, args[1], indent);
                sb.Append(", ");
                EmitExpr(sb, args[2], indent);
                sb.Append(')');
                return true;
            case Builtins.BufReadBytes when args.Count >= 3:
                sb.Append("_Buf.buf_read_bytes(");
                EmitExpr(sb, args[0], indent);
                sb.Append(", ");
                EmitExpr(sb, args[1], indent);
                sb.Append(", ");
                EmitExpr(sb, args[2], indent);
                sb.Append(')');
                return true;

            case Builtins.RecordSet when args.Count == 3:
            {
                if (args[1] is IRTextLit fieldLit)
                {
                    RecordType? rt = args[0].Type as RecordType;
                    if (rt is null && args[0].Type is ConstructedType ctRs
                        && m_typeDefsForRecordSet.TryGetValue(ctRs.Constructor.Value, out CodexType? td))
                        {
                            rt = td as RecordType;
                        }

                        if (rt is not null)
                    {
                        CodexType? fieldType = null;
                        for (int fi = 0; fi < rt.Fields.Length; fi++)
                        {
                            if (rt.Fields[fi].FieldName.Value == fieldLit.Value)
                            {
                                fieldType = rt.Fields[fi].Type;
                                break;
                            }
                        }

                        sb.Append($"((Func<{SanitizeIdentifier(rt.TypeName.Value)}, {SanitizeIdentifier(rt.TypeName.Value)}>)((_rs) => new {SanitizeIdentifier(rt.TypeName.Value)}(");
                        for (int i = 0; i < rt.Fields.Length; i++)
                        {
                            if (i > 0) sb.Append(", ");
                            string fn = rt.Fields[i].FieldName.Value;
                            if (fn == fieldLit.Value)
                            {
                                if (args[2] is IRList emptyList && emptyList.Elements.Length == 0
                                    && fieldType is ListType flt)
                                {
                                    sb.Append($"new List<{EmitType(flt.Element)}>()");
                                }
                                else
                                    {
                                        EmitExpr(sb, args[2], indent);
                                    }
                                }
                            else
                            {
                                sb.Append("_rs.");
                                sb.Append(SanitizeIdentifier(fn));
                            }
                        }
                        sb.Append(")))(");
                        EmitExpr(sb, args[0], indent);
                        sb.Append(')');
                    }
                    else
                    {
                        sb.Append('(');
                        EmitExpr(sb, args[0], indent);
                        sb.Append(" with { ");
                        sb.Append(SanitizeIdentifier(fieldLit.Value));
                        sb.Append(" = ");
                        EmitExpr(sb, args[2], indent);
                        sb.Append(" })");
                    }
                }
                return true;
            }

            case "int-mod" when args.Count == 2:
            {
                // Euclidean modulo: result is always in [0, |b|) for any nonzero b.
                // Lambda-wrap so a and b each evaluate once.
                sb.Append("((Func<long, long, long>)((_a, _b) => { long _abs = _b < 0L ? -_b : _b; long _r = _a % _abs; return _r < 0L ? _r + _abs : _r; }))(");
                EmitExpr(sb, args[0], indent);
                sb.Append(", ");
                EmitExpr(sb, args[1], indent);
                sb.Append(")");
                return true;
            }

            case "min" when args.Count == 2:
                sb.Append("Math.Min(");
                EmitExpr(sb, args[0], indent);
                sb.Append(", ");
                EmitExpr(sb, args[1], indent);
                sb.Append(')');
                return true;

            case "max" when args.Count == 2:
                sb.Append("Math.Max(");
                EmitExpr(sb, args[0], indent);
                sb.Append(", ");
                EmitExpr(sb, args[1], indent);
                sb.Append(')');
                return true;

            case "bit-and" when args.Count == 2:
                sb.Append('(');
                EmitExpr(sb, args[0], indent);
                sb.Append(" & ");
                EmitExpr(sb, args[1], indent);
                sb.Append(')');
                return true;

            case "bit-or" when args.Count == 2:
                sb.Append('(');
                EmitExpr(sb, args[0], indent);
                sb.Append(" | ");
                EmitExpr(sb, args[1], indent);
                sb.Append(')');
                return true;

            case "bit-xor" when args.Count == 2:
                sb.Append('(');
                EmitExpr(sb, args[0], indent);
                sb.Append(" ^ ");
                EmitExpr(sb, args[1], indent);
                sb.Append(')');
                return true;

            case "bit-shl" when args.Count == 2:
                sb.Append('(');
                EmitExpr(sb, args[0], indent);
                sb.Append(" << (int)");
                EmitExpr(sb, args[1], indent);
                sb.Append(')');
                return true;

            case "bit-shr" when args.Count == 2:
                sb.Append('(');
                EmitExpr(sb, args[0], indent);
                sb.Append(" >> (int)");
                EmitExpr(sb, args[1], indent);
                sb.Append(')');
                return true;

            default:
                return false;
        }
    }

    protected override void EmitBinary(StringBuilder sb, IRBinary bin, int indent)
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

    protected override void EmitLet(StringBuilder sb, IRLet let, int indent)
    {
        string nameType = EmitType(let.NameType);
        if (nameType == "object") nameType = "dynamic";
        string funcType = $"Func<{nameType}, {EmitType(let.Body.Type)}>";
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

    protected override void EmitLambda(StringBuilder sb, IRLambda lam, int indent)
    {
        if (lam.Parameters.Length == 0)
        {
            sb.Append("() => ");
            EmitExpr(sb, lam.Body, indent);
            return;
        }
        for (int i = 0; i < lam.Parameters.Length; i++)
        {
            sb.Append($"({EmitType(lam.Parameters[i].Type)} {SanitizeIdentifier(lam.Parameters[i].Name)}) => ");
        }
        EmitExpr(sb, lam.Body, indent);
    }

    protected override void EmitList(StringBuilder sb, IRList list, int indent)
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

    protected override void EmitRunState(StringBuilder sb, IRRunState runState, int indent)
    {
        string stateType = EmitType(runState.StateType);
        string resultType = EmitType(runState.ResultType);
        sb.AppendLine($"((Func<{resultType}>)(() => {{");
        string pad = new(' ', (indent + 2) * 4);
        sb.Append(pad);
        sb.Append($"{stateType} __state = ");
        EmitExpr(sb, runState.InitialState, indent + 2);
        sb.AppendLine(";");

        if (runState.Computation is IRAct actExpr)
        {
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

    protected override void EmitHandle(StringBuilder sb, IRHandle handle, int indent)
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
