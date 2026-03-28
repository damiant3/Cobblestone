using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

Codex_Codex_Codex.main();

static class _Cce {
    static readonly int[] _toUni = {
        0, 10, 32,
        48, 49, 50, 51, 52, 53, 54, 55, 56, 57,
        101, 116, 97, 111, 105, 110, 115, 104, 114, 100,
        108, 99, 117, 109, 119, 102, 103, 121, 112, 98,
        118, 107, 106, 120, 113, 122,
        69, 84, 65, 79, 73, 78, 83, 72, 82, 68,
        76, 67, 85, 77, 87, 70, 71, 89, 80, 66,
        86, 75, 74, 88, 81, 90,
        46, 44, 33, 63, 58, 59, 39, 34, 45, 40, 41,
        43, 61, 42, 60, 62,
        47, 64, 35, 38, 95, 92, 124, 91, 93, 123, 125, 126, 96,
        233, 232, 234, 235, 225, 224, 226, 228, 243, 242,
        244, 246, 250, 249, 251, 252, 241, 231, 237,
        1072, 1086, 1077, 1080, 1085, 1090, 1089, 1088,
        1074, 1083, 1082, 1084, 1076, 1087, 1091
    };
    static readonly Dictionary<int, int> _fromUni = new();
    static _Cce() { for (int i = 0; i < 128; i++) _fromUni[_toUni[i]] = i; }
    public static string FromUnicode(string s) {
        var cs = new char[s.Length];
        for (int i = 0; i < s.Length; i++) {
            int u = s[i];
            cs[i] = _fromUni.TryGetValue(u, out int c) ? (char)c : (char)68;
        }
        return new string(cs);
    }
    public static string ToUnicode(string s) {
        var cs = new char[s.Length];
        for (int i = 0; i < s.Length; i++) {
            int b = s[i];
            cs[i] = (b >= 0 && b < 128) ? (char)_toUni[b] : '\uFFFD';
        }
        return new string(cs);
    }
    public static long UniToCce(long u) {
        return _fromUni.TryGetValue((int)u, out int c) ? c : 68;
    }
    public static long CceToUni(long b) {
        return (b >= 0 && b < 128) ? _toUni[(int)b] : 65533;
    }
}

public abstract record LiteralKind;
public sealed record IntLit : LiteralKind;
public sealed record NumLit : LiteralKind;
public sealed record TextLit : LiteralKind;
public sealed record CharLit : LiteralKind;
public sealed record BoolLit : LiteralKind;


public abstract record BinaryOp;
public sealed record OpAdd : BinaryOp;
public sealed record OpSub : BinaryOp;
public sealed record OpMul : BinaryOp;
public sealed record OpDiv : BinaryOp;
public sealed record OpPow : BinaryOp;
public sealed record OpEq : BinaryOp;
public sealed record OpNotEq : BinaryOp;
public sealed record OpLt : BinaryOp;
public sealed record OpGt : BinaryOp;
public sealed record OpLtEq : BinaryOp;
public sealed record OpGtEq : BinaryOp;
public sealed record OpDefEq : BinaryOp;
public sealed record OpAppend : BinaryOp;
public sealed record OpCons : BinaryOp;
public sealed record OpAnd : BinaryOp;
public sealed record OpOr : BinaryOp;


public abstract record AExpr;
public sealed record ALitExpr(string Field0, LiteralKind Field1) : AExpr;
public sealed record ANameExpr(Name Field0) : AExpr;
public sealed record AApplyExpr(AExpr Field0, AExpr Field1) : AExpr;
public sealed record ABinaryExpr(AExpr Field0, BinaryOp Field1, AExpr Field2) : AExpr;
public sealed record AUnaryExpr(AExpr Field0) : AExpr;
public sealed record AIfExpr(AExpr Field0, AExpr Field1, AExpr Field2) : AExpr;
public sealed record ALetExpr(List<ALetBind> Field0, AExpr Field1) : AExpr;
public sealed record ALambdaExpr(List<Name> Field0, AExpr Field1) : AExpr;
public sealed record AMatchExpr(AExpr Field0, List<AMatchArm> Field1) : AExpr;
public sealed record AListExpr(List<AExpr> Field0) : AExpr;
public sealed record ARecordExpr(Name Field0, List<AFieldExpr> Field1) : AExpr;
public sealed record AFieldAccess(AExpr Field0, Name Field1) : AExpr;
public sealed record ADoExpr(List<ADoStmt> Field0) : AExpr;
public sealed record AHandleExpr(Name Field0, AExpr Field1, List<AHandleClause> Field2) : AExpr;
public sealed record AErrorExpr(string Field0) : AExpr;


public sealed record ALetBind(Name name, AExpr value);

public sealed record AMatchArm(APat pattern, AExpr body);

public sealed record AFieldExpr(Name name, AExpr value);

public abstract record ADoStmt;
public sealed record ADoBindStmt(Name Field0, AExpr Field1) : ADoStmt;
public sealed record ADoExprStmt(AExpr Field0) : ADoStmt;


public sealed record AHandleClause(Name op_name, Name resume_name, AExpr body);

public abstract record APat;
public sealed record AVarPat(Name Field0) : APat;
public sealed record ALitPat(string Field0, LiteralKind Field1) : APat;
public sealed record ACtorPat(Name Field0, List<APat> Field1) : APat;
public sealed record AWildPat : APat;


public abstract record ATypeExpr;
public sealed record ANamedType(Name Field0) : ATypeExpr;
public sealed record AFunType(ATypeExpr Field0, ATypeExpr Field1) : ATypeExpr;
public sealed record AAppType(ATypeExpr Field0, List<ATypeExpr> Field1) : ATypeExpr;
public sealed record AEffectType(List<Name> Field0, ATypeExpr Field1) : ATypeExpr;


public sealed record AParam(Name name);

public sealed record ADef(Name name, List<AParam> @params, List<ATypeExpr> declared_type, AExpr body);

public sealed record ARecordFieldDef(Name name, ATypeExpr type_expr);

public sealed record AVariantCtorDef(Name name, List<ATypeExpr> fields);

public abstract record ATypeDef;
public sealed record ARecordTypeDef(Name Field0, List<Name> Field1, List<ARecordFieldDef> Field2) : ATypeDef;
public sealed record AVariantTypeDef(Name Field0, List<Name> Field1, List<AVariantCtorDef> Field2) : ATypeDef;


public sealed record AEffectOpDef(Name name, ATypeExpr type_expr);

public sealed record AEffectDef(Name name, List<AEffectOpDef> ops);

public sealed record AImportDecl(Name module_name);

public sealed record AModule(Name name, List<ADef> defs, List<ATypeDef> type_defs, List<AEffectDef> effect_defs, List<AImportDecl> imports);

public abstract record DiagnosticSeverity;
public sealed record Error : DiagnosticSeverity;
public sealed record Warning : DiagnosticSeverity;
public sealed record Info : DiagnosticSeverity;


public sealed record Diagnostic(string code, string message, DiagnosticSeverity severity);

public sealed record Name(string value);

public sealed record SourcePosition(long line, long column, long offset);

public sealed record SourceSpan(SourcePosition start, SourcePosition end, string file);

public sealed record ArityEntry(string name, long arity);

public sealed record ApplyChain(IRExpr root, List<IRExpr> args);

public abstract record IRBinaryOp;
public sealed record IrAddInt : IRBinaryOp;
public sealed record IrSubInt : IRBinaryOp;
public sealed record IrMulInt : IRBinaryOp;
public sealed record IrDivInt : IRBinaryOp;
public sealed record IrPowInt : IRBinaryOp;
public sealed record IrAddNum : IRBinaryOp;
public sealed record IrSubNum : IRBinaryOp;
public sealed record IrMulNum : IRBinaryOp;
public sealed record IrDivNum : IRBinaryOp;
public sealed record IrEq : IRBinaryOp;
public sealed record IrNotEq : IRBinaryOp;
public sealed record IrLt : IRBinaryOp;
public sealed record IrGt : IRBinaryOp;
public sealed record IrLtEq : IRBinaryOp;
public sealed record IrGtEq : IRBinaryOp;
public sealed record IrAnd : IRBinaryOp;
public sealed record IrOr : IRBinaryOp;
public sealed record IrAppendText : IRBinaryOp;
public sealed record IrAppendList : IRBinaryOp;
public sealed record IrConsList : IRBinaryOp;


public abstract record IRExpr;
public sealed record IrIntLit(long Field0) : IRExpr;
public sealed record IrNumLit(long Field0) : IRExpr;
public sealed record IrTextLit(string Field0) : IRExpr;
public sealed record IrBoolLit(bool Field0) : IRExpr;
public sealed record IrCharLit(long Field0) : IRExpr;
public sealed record IrName(string Field0, CodexType Field1) : IRExpr;
public sealed record IrBinary(IRBinaryOp Field0, IRExpr Field1, IRExpr Field2, CodexType Field3) : IRExpr;
public sealed record IrNegate(IRExpr Field0) : IRExpr;
public sealed record IrIf(IRExpr Field0, IRExpr Field1, IRExpr Field2, CodexType Field3) : IRExpr;
public sealed record IrLet(string Field0, CodexType Field1, IRExpr Field2, IRExpr Field3) : IRExpr;
public sealed record IrApply(IRExpr Field0, IRExpr Field1, CodexType Field2) : IRExpr;
public sealed record IrLambda(List<IRParam> Field0, IRExpr Field1, CodexType Field2) : IRExpr;
public sealed record IrList(List<IRExpr> Field0, CodexType Field1) : IRExpr;
public sealed record IrMatch(IRExpr Field0, List<IRBranch> Field1, CodexType Field2) : IRExpr;
public sealed record IrDo(List<IRDoStmt> Field0, CodexType Field1) : IRExpr;
public sealed record IrHandle(string Field0, IRExpr Field1, List<IRHandleClause> Field2, CodexType Field3) : IRExpr;
public sealed record IrRecord(string Field0, List<IRFieldVal> Field1, CodexType Field2) : IRExpr;
public sealed record IrFieldAccess(IRExpr Field0, string Field1, CodexType Field2) : IRExpr;
public sealed record IrFork(IRExpr Field0, CodexType Field1) : IRExpr;
public sealed record IrAwait(IRExpr Field0, CodexType Field1) : IRExpr;
public sealed record IrError(string Field0, CodexType Field1) : IRExpr;


public sealed record IRParam(string name, CodexType type_val);

public sealed record IRBranch(IRPat pattern, IRExpr body);

public abstract record IRPat;
public sealed record IrVarPat(string Field0, CodexType Field1) : IRPat;
public sealed record IrLitPat(string Field0, CodexType Field1) : IRPat;
public sealed record IrCtorPat(string Field0, List<IRPat> Field1, CodexType Field2) : IRPat;
public sealed record IrWildPat : IRPat;


public abstract record IRDoStmt;
public sealed record IrDoBind(string Field0, CodexType Field1, IRExpr Field2) : IRDoStmt;
public sealed record IrDoExec(IRExpr Field0) : IRDoStmt;


public sealed record IRHandleClause(string op_name, string resume_name, IRExpr body);

public sealed record IRFieldVal(string name, IRExpr value);

public sealed record IRDef(string name, List<IRParam> @params, CodexType type_val, IRExpr body);

public sealed record IRModule(Name name, List<IRDef> defs);

public sealed record LowerCtx(List<TypeBinding> types, UnificationState ust);

public sealed record Scope(List<string> names);

public sealed record ResolveResult(List<Diagnostic> errors, List<string> top_level_names, List<string> type_names, List<string> ctor_names);

public sealed record CollectResult(List<string> names, List<Diagnostic> errors);

public sealed record CtorCollectResult(List<string> type_names, List<string> ctor_names);

public sealed record LexState(string source, long offset, long line, long column);

public abstract record LexResult;
public sealed record LexToken(Token Field0, LexState Field1) : LexResult;
public sealed record LexEnd : LexResult;


public sealed record ImportParseResult(List<ImportDecl> imports, ParseState state);

public sealed record ParseState(List<Token> tokens, long pos);

public abstract record ParseExprResult;
public sealed record ExprOk(Expr Field0, ParseState Field1) : ParseExprResult;


public abstract record ParsePatResult;
public sealed record PatOk(Pat Field0, ParseState Field1) : ParsePatResult;


public abstract record ParseTypeResult;
public sealed record TypeOk(TypeExpr Field0, ParseState Field1) : ParseTypeResult;


public abstract record ParseDefResult;
public sealed record DefOk(Def Field0, ParseState Field1) : ParseDefResult;
public sealed record DefNone(ParseState Field0) : ParseDefResult;


public abstract record ParseTypeDefResult;
public sealed record TypeDefOk(TypeDef Field0, ParseState Field1) : ParseTypeDefResult;
public sealed record TypeDefNone(ParseState Field0) : ParseTypeDefResult;


public sealed record HandleParseResult(List<HandleClause> clauses, ParseState state);

public sealed record HandleParamsResult(List<Token> toks, ParseState state);

public sealed record LambdaParamsResult(List<Token> toks, ParseState state);

public sealed record LetBind(Token name, Expr value);

public sealed record MatchArm(Pat pattern, Expr body);

public sealed record RecordFieldExpr(Token name, Expr value);

public abstract record DoStmt;
public sealed record DoBindStmt(Token Field0, Expr Field1) : DoStmt;
public sealed record DoExprStmt(Expr Field0) : DoStmt;


public sealed record HandleClause(Token op_name, Token resume_name, Expr body);

public abstract record Pat;
public sealed record VarPat(Token Field0) : Pat;
public sealed record LitPat(Token Field0) : Pat;
public sealed record CtorPat(Token Field0, List<Pat> Field1) : Pat;
public sealed record WildPat(Token Field0) : Pat;


public sealed record Def(Token name, List<Token> @params, List<TypeAnn> ann, Expr body);

public sealed record RecordFieldDef(Token name, TypeExpr type_expr);

public sealed record VariantCtorDef(Token name, List<TypeExpr> fields);

public sealed record RecordField(Name name, CodexType type_val);

public sealed record ParamEntry(string param_name, long var_id);

public sealed record ParamResult(CodexType parameterized, List<ParamEntry> entries, UnificationState state);

public sealed record WalkResult(CodexType walked, List<ParamEntry> entries, UnificationState state);

public sealed record WalkListResult(List<CodexType> walked_list, List<ParamEntry> entries, UnificationState state);

public sealed record DefSetup(CodexType expected_type, CodexType remaining_type, UnificationState state, TypeEnv env);

public sealed record DefParamResult(UnificationState state, TypeEnv env, CodexType remaining_type);

public sealed record ModuleResult(List<TypeBinding> types, UnificationState state);

public sealed record CheckResult(CodexType inferred_type, UnificationState state);

public sealed record LetBindResult(UnificationState state, TypeEnv env);

public sealed record LambdaBindResult(UnificationState state, TypeEnv env, List<CodexType> param_types);

public sealed record PatBindResult(UnificationState state, TypeEnv env);

public sealed record UnificationState(List<SubstEntry> substitutions, long next_id, List<Diagnostic> errors);

public sealed record SubstEntry(long var_id, CodexType resolved_type);

public sealed record UnifyResult(bool success, UnificationState state);

public sealed record FreshResult(CodexType var_type, UnificationState state);

public abstract record CompileResult;
public sealed record CompileOk(string Field0, ModuleResult Field1) : CompileResult;
public sealed record CompileError(List<Diagnostic> Field0) : CompileResult;


public static class Codex_Codex_Codex
{
    public static AExpr desugar_expr(Expr node)
    {
        while (true)
        {
            var _tco_s = node;
            if (_tco_s is LitExpr _tco_m0)
            {
                var tok = _tco_m0.Field0;
            return desugar_literal(tok);
            }
            else if (_tco_s is NameExpr _tco_m1)
            {
                var tok = _tco_m1.Field0;
            return new ANameExpr(make_name(tok.text));
            }
            else if (_tco_s is AppExpr _tco_m2)
            {
                var f = _tco_m2.Field0;
                var a = _tco_m2.Field1;
            return new AApplyExpr(desugar_expr(f), desugar_expr(a));
            }
            else if (_tco_s is BinExpr _tco_m3)
            {
                var l = _tco_m3.Field0;
                var op = _tco_m3.Field1;
                var r = _tco_m3.Field2;
            return new ABinaryExpr(desugar_expr(l), desugar_bin_op(op.kind), desugar_expr(r));
            }
            else if (_tco_s is UnaryExpr _tco_m4)
            {
                var op = _tco_m4.Field0;
                var operand = _tco_m4.Field1;
            return new AUnaryExpr(desugar_expr(operand));
            }
            else if (_tco_s is IfExpr _tco_m5)
            {
                var c = _tco_m5.Field0;
                var t = _tco_m5.Field1;
                var e = _tco_m5.Field2;
            return new AIfExpr(desugar_expr(c), desugar_expr(t), desugar_expr(e()));
            }
            else if (_tco_s is LetExpr _tco_m6)
            {
                var bindings = _tco_m6.Field0;
                var body = _tco_m6.Field1;
            return new ALetExpr(map_list(desugar_let_bind, bindings()), desugar_expr(body()));
            }
            else if (_tco_s is MatchExpr _tco_m7)
            {
                var scrut = _tco_m7.Field0;
                var arms = _tco_m7.Field1;
            return new AMatchExpr(desugar_expr(scrut), map_list(desugar_match_arm, arms));
            }
            else if (_tco_s is ListExpr _tco_m8)
            {
                var elems = _tco_m8.Field0;
            return new AListExpr(map_list(desugar_expr, elems));
            }
            else if (_tco_s is RecordExpr _tco_m9)
            {
                var type_tok = _tco_m9.Field0;
                var fields = _tco_m9.Field1;
            return new ARecordExpr(make_name(type_tok.text), map_list(desugar_field_expr, fields));
            }
            else if (_tco_s is FieldExpr _tco_m10)
            {
                var rec = _tco_m10.Field0;
                var field_tok = _tco_m10.Field1;
            return new AFieldAccess(desugar_expr(rec), make_name(field_tok.text));
            }
            else if (_tco_s is ParenExpr _tco_m11)
            {
                var inner = _tco_m11.Field0;
            var _tco_0 = inner;
            node = _tco_0;
            continue;
            }
            else if (_tco_s is DoExpr _tco_m12)
            {
                var stmts = _tco_m12.Field0;
            return new ADoExpr(map_list(desugar_do_stmt, stmts));
            }
            else if (_tco_s is HandleExpr _tco_m13)
            {
                var eff_tok = _tco_m13.Field0;
                var body = _tco_m13.Field1;
                var clauses = _tco_m13.Field2;
            return new AHandleExpr(make_name(eff_tok.text), desugar_expr(body()), map_list(desugar_handle_clause, clauses));
            }
            else if (_tco_s is LambdaExpr _tco_m14)
            {
                var @params = _tco_m14.Field0;
                var body = _tco_m14.Field1;
            return new ALambdaExpr(map_list(desugar_lambda_param, @params), desugar_expr(body()));
            }
            {
                var ErrExpr = _tco_s;
            return tok;
            }
        }
    }

    public static Func<string, AExpr> AErrorExpr() => /* error: . */ default(text());

    public static AExpr desugar_literal(Token tok) => (is_literal(tok.kind) ? new ALitExpr(tok.text, classify_literal(tok.kind)) : new AErrorExpr(tok.text));

    public static LiteralKind classify_literal(TokenKind k) => k switch { IntegerLiteral { } => new IntLit(), NumberLiteral { } => new NumLit(), object TextLiteral => TextLit, };

    public static ALetBind desugar_let_bind(LetBind b) => new ALetBind(name: make_name(b().name.text), value: desugar_expr(b().value));

    public static AMatchArm desugar_match_arm(MatchArm arm) => new AMatchArm(pattern: desugar_pattern(arm.pattern), body: desugar_expr(arm.body));

    public static AFieldExpr desugar_field_expr(RecordFieldExpr f) => new AFieldExpr(name: make_name(f.name.text), value: desugar_expr(f.value));

    public static ADoStmt desugar_do_stmt(DoStmt s) => s switch { DoBindStmt(var tok, var val) => new ADoBindStmt(make_name(tok.text), desugar_expr(val)), DoExprStmt(var e) => new ADoExprStmt(desugar_expr(e())), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static AHandleClause desugar_handle_clause(HandleClause c) => new AHandleClause(op_name: make_name(c.op_name.text), resume_name: make_name(c.resume_name.text), body: desugar_expr(c.body));

    public static Name desugar_lambda_param(Token tok) => make_name(tok.text);

    public static BinaryOp desugar_bin_op(TokenKind k) => k switch { Plus { } => new OpAdd(), Minus { } => new OpSub(), Star { } => new OpMul(), Slash { } => new OpDiv(), Caret { } => new OpPow(), DoubleEquals { } => new OpEq(), NotEquals { } => new OpNotEq(), LessThan { } => new OpLt(), GreaterThan { } => new OpGt(), LessOrEqual { } => new OpLtEq(), GreaterOrEqual { } => new OpGtEq(), object TripleEquals => new OpDefEq(), };

    public static APat desugar_pattern(Pat p) => p switch { VarPat(var tok) => new AVarPat(make_name(tok.text)), LitPat(var tok) => new ALitPat(tok.text, classify_literal(tok.kind)), CtorPat(var tok, var subs) => new ACtorPat(make_name(tok.text), map_list(desugar_pattern, subs)), WildPat(var tok) => new AWildPat(), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static ATypeExpr desugar_type_expr(TypeExpr t)
    {
        while (true)
        {
            var _tco_s = t;
            if (_tco_s is NamedType _tco_m0)
            {
                var tok = _tco_m0.Field0;
            return new ANamedType(make_name(tok.text));
            }
            else if (_tco_s is FunType _tco_m1)
            {
                var param = _tco_m1.Field0;
                var ret = _tco_m1.Field1;
            return new AFunType(desugar_type_expr(param), desugar_type_expr(ret()));
            }
            else if (_tco_s is AppType _tco_m2)
            {
                var ctor = _tco_m2.Field0;
                var args = _tco_m2.Field1;
            return new AAppType(desugar_type_expr(ctor()), map_list(desugar_type_expr, args));
            }
            else if (_tco_s is ParenType _tco_m3)
            {
                var inner = _tco_m3.Field0;
            var _tco_0 = inner;
            t = _tco_0;
            continue;
            }
            else if (_tco_s is ListType _tco_m4)
            {
                var elem = _tco_m4.Field0;
            return new AAppType(new ANamedType(make_name("L\u0011\u0013\u000E")), new List<ATypeExpr> { desugar_type_expr(elem) });
            }
            else if (_tco_s is LinearTypeExpr _tco_m5)
            {
                var inner = _tco_m5.Field0;
            var _tco_0 = inner;
            t = _tco_0;
            continue;
            }
            {
                var EffectTypeExpr = _tco_s;
            return effs;
            }
        }
    }

    public static T479 ret<T479>() => /* error: -> */ default(new AEffectType())(map_list(make_type_param_name, effs))(desugar_type_expr(ret()));

    public static ADef desugar_def(Def d) => ((Func<List<ATypeExpr>, ADef>)((ann_types) => new ADef(name: make_name(d.name.text), @params: map_list(desugar_param, d.@params), declared_type: ann_types, body: desugar_expr(d.body))))(desugar_annotations(d.ann));

    public static List<ATypeExpr> desugar_annotations(List<TypeAnn> anns) => ((((long)anns.Count) == 0) ? new List<ATypeExpr>() : ((Func<TypeAnn, List<ATypeExpr>>)((a) => new List<ATypeExpr> { desugar_type_expr(a.type_expr) }))(anns[(int)0]));

    public static AParam desugar_param(Token tok) => new AParam(name: make_name(tok.text));

    public static ATypeDef desugar_type_def(TypeDef td) => td.body switch { RecordBody(var fields) => new ARecordTypeDef(make_name(td.name.text), map_list(make_type_param_name, td.type_params), map_list(desugar_record_field_def, fields)), VariantBody(var ctors) => new AVariantTypeDef(make_name(td.name.text), map_list(make_type_param_name, td.type_params), map_list(desugar_variant_ctor_def, ctors)), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static Name make_type_param_name(Token tok) => make_name(tok.text);

    public static ARecordFieldDef desugar_record_field_def(RecordFieldDef f) => new ARecordFieldDef(name: make_name(f.name.text), type_expr: desugar_type_expr(f.type_expr));

    public static AVariantCtorDef desugar_variant_ctor_def(VariantCtorDef c) => new AVariantCtorDef(name: make_name(c.name.text), fields: map_list(desugar_type_expr, c.fields));

    public static AModule desugar_document(Document doc, string module_name) => new AModule(name: make_name(module_name), defs: map_list(desugar_def, doc.defs), type_defs: map_list(desugar_type_def, doc.type_defs), effect_defs: map_list(desugar_effect_def, doc.effect_defs), imports: map_list(desugar_import, doc.imports));

    public static AImportDecl desugar_import(ImportDecl imp) => new AImportDecl(module_name: make_name(imp.module_name.text));

    public static AEffectDef desugar_effect_def(EffectDef ed) => new AEffectDef(name: make_name(ed.name.text), ops: map_list(desugar_effect_op, ed.ops));

    public static AEffectOpDef desugar_effect_op(EffectOpDef op) => new AEffectOpDef(name: make_name(op.name.text), type_expr: desugar_type_expr(op.type_expr));

    public static List<b> map_list(Func<a, b> f, List<a> xs) => map_list_loop(f, xs, 0, ((long)xs.Count), new List<b>());

    public static List<b> map_list_loop(Func<a, b> f, List<a> xs, long i, long len, List<b> acc)
    {
        while (true)
        {
            if ((i() == len))
            {
            return acc();
            }
            else
            {
            var _tco_0 = f;
            var _tco_1 = xs;
            var _tco_2 = (i() + 1);
            var _tco_3 = len;
            var _tco_4 = ((Func<List<b>>)(() => { var _l = acc(); _l.Add(f(xs[(int)i()])); return _l; }))();
            f = _tco_0;
            xs = _tco_1;
            i = _tco_2;
            len = _tco_3;
            acc = _tco_4;
            continue;
            }
        }
    }

    public static b fold_list(Func<b, Func<a, b>> f, b z, List<a> xs) => fold_list_loop(f, z, xs, 0, ((long)xs.Count));

    public static b fold_list_loop(Func<b, Func<a, b>> f, b z, List<a> xs, long i, long len)
    {
        while (true)
        {
            if ((i() == len))
            {
            return z;
            }
            else
            {
            var _tco_0 = f;
            var _tco_1 = f(z)(xs[(int)i()]);
            var _tco_2 = xs;
            var _tco_3 = (i() + 1);
            var _tco_4 = len;
            f = _tco_0;
            z = _tco_1;
            xs = _tco_2;
            i = _tco_3;
            len = _tco_4;
            continue;
            }
        }
    }

    public static long bsearch_text_pos(List<TypeBinding> bindings, string name, long lo, long hi)
    {
        while (true)
        {
            if ((lo >= hi))
            {
            return lo;
            }
            else
            {
            var mid = (lo + ((hi - lo) / 2));
            var mid_name = bindings()[(int)mid].name;
            if (((long)string.CompareOrdinal(name(), mid_name) <= 0))
            {
            var _tco_0 = bindings();
            var _tco_1 = name();
            var _tco_2 = lo;
            var _tco_3 = mid;
            bindings = _tco_0;
            name = _tco_1;
            lo = _tco_2;
            hi = _tco_3;
            continue;
            }
            else
            {
            var _tco_0 = bindings();
            var _tco_1 = name();
            var _tco_2 = (mid + 1);
            var _tco_3 = hi;
            bindings = _tco_0;
            name = _tco_1;
            lo = _tco_2;
            hi = _tco_3;
            continue;
            }
            }
        }
    }

    public static long bsearch_int_pos(List<SubstEntry> entries, long key, long lo, long hi)
    {
        while (true)
        {
            if ((lo >= hi))
            {
            return lo;
            }
            else
            {
            var mid = (lo + ((hi - lo) / 2));
            if ((key <= entries()[(int)mid].var_id))
            {
            var _tco_0 = entries();
            var _tco_1 = key;
            var _tco_2 = lo;
            var _tco_3 = mid;
            entries = _tco_0;
            key = _tco_1;
            lo = _tco_2;
            hi = _tco_3;
            continue;
            }
            else
            {
            var _tco_0 = entries();
            var _tco_1 = key;
            var _tco_2 = (mid + 1);
            var _tco_3 = hi;
            entries = _tco_0;
            key = _tco_1;
            lo = _tco_2;
            hi = _tco_3;
            continue;
            }
            }
        }
    }

    public static long bsearch_text_set(List<string> names, string name, long lo, long hi)
    {
        while (true)
        {
            if ((lo >= hi))
            {
            return lo;
            }
            else
            {
            var mid = (lo + ((hi - lo) / 2));
            if (((long)string.CompareOrdinal(name(), names[(int)mid]) <= 0))
            {
            var _tco_0 = names;
            var _tco_1 = name();
            var _tco_2 = lo;
            var _tco_3 = mid;
            names = _tco_0;
            name = _tco_1;
            lo = _tco_2;
            hi = _tco_3;
            continue;
            }
            else
            {
            var _tco_0 = names;
            var _tco_1 = name();
            var _tco_2 = (mid + 1);
            var _tco_3 = hi;
            names = _tco_0;
            name = _tco_1;
            lo = _tco_2;
            hi = _tco_3;
            continue;
            }
            }
        }
    }

    public static Diagnostic make_error(string code, string msg) => new Diagnostic(code: code, message: msg, severity: Error);

    public static Diagnostic make_warning(string code, string msg) => new Diagnostic(code: code, message: msg, severity: new Warning());

    public static string severity_label(DiagnosticSeverity s) => s switch { object Error => "\u000D\u0015\u0015\u0010\u0015", };

    public static string diagnostic_display(Diagnostic d) => (severity_label(d.severity) + ("\u0002" + (d.code + (":\u0002" + d.message))));

    public static Name make_name(string s) => new Name(value: s);

    public static string name_value(Name n) => n.value;

    public static SourcePosition make_position(long line, long col, long offset) => new SourcePosition(line: line(), column: col, offset: offset());

    public static SourceSpan make_span(SourcePosition s, SourcePosition e, string f) => new SourceSpan(start: s, end: e(), file: f);

    public static long span_length(SourceSpan span) => (span.end.offset - span.start.offset);

    public static string emit_type_defs(List<ATypeDef> tds, long i) => ((i() == ((long)tds.Count)) ? "" : (emit_type_def(tds[(int)i()]) + ("\u0001" + emit_type_defs(tds, (i() + 1)))));

    public static string emit_type_def(ATypeDef td) => td switch { ARecordTypeDef(var name, var tparams, var fields) => ((Func<string, string>)((gen) => ("\u001F\u0019b\u0017\u0011\u0018\u0002\u0013\u000D\u000F\u0017\u000D\u0016\u0002\u0015\u000D\u0018\u0010\u0015\u0016\u0002" + (sanitize(name().value) + (gen + ("(" + (emit_record_field_defs(fields, tparams(), 0) + ");\u0001")))))))(emit_tparameter_suffix(tparams())), AVariantTypeDef(var name, var tparams, var ctors) => ((Func<string, string>)((gen) => ("\u001F\u0019b\u0017\u0011\u0018\u0002\u000Fb\u0013\u000E\u0015\u000F\u0018\u000E\u0002\u0015\u000D\u0018\u0010\u0015\u0016\u0002" + (sanitize(name().value) + (gen + (";\u0001" + (emit_variant_ctors(ctors, name(), tparams(), 0) + "\u0001")))))))(emit_tparameter_suffix(tparams())), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static string emit_tparameter_suffix(List<Name> tparams) => ((((long)tparams().Count) == 0) ? "" : ("<" + (emit_tparameter_names(tparams(), 0) + ">")));

    public static string emit_tparameter_names(List<Name> tparams, long i) => ((i() == ((long)tparams().Count)) ? "" : ((i() == (((long)tparams().Count) - 1)) ? ("T" + _Cce.FromUnicode(i().ToString())) : ("T" + (_Cce.FromUnicode(i().ToString()) + (",\u0002" + emit_tparameter_names(tparams(), (i() + 1)))))));

    public static string emit_record_field_defs(List<ARecordFieldDef> fields, List<Name> tparams, long i) => ((i() == ((long)fields.Count)) ? "" : ((Func<ARecordFieldDef, string>)((f) => (emit_type_expr_tp(f.type_expr, tparams()) + ("\u0002" + (sanitize(f.name.value) + (((i() < (((long)fields.Count) - 1)) ? ",\u0002" : "") + emit_record_field_defs(fields, tparams(), (i() + 1))))))))(fields[(int)i()]));

    public static string emit_variant_ctors(List<AVariantCtorDef> ctors, Name base_name, List<Name> tparams, long i) => ((i() == ((long)ctors.Count)) ? "" : ((Func<AVariantCtorDef, string>)((c) => (emit_variant_ctor(c, base_name, tparams()) + emit_variant_ctors(ctors, base_name, tparams(), (i() + 1)))))(ctors[(int)i()]));

    public static string emit_variant_ctor(AVariantCtorDef c, Name base_name, List<Name> tparams) => ((Func<string, string>)((gen) => ((((long)c.fields.Count) == 0) ? ("\u001F\u0019b\u0017\u0011\u0018\u0002\u0013\u000D\u000F\u0017\u000D\u0016\u0002\u0015\u000D\u0018\u0010\u0015\u0016\u0002" + (sanitize(c.name.value) + (gen + ("\u0002:\u0002" + (sanitize(base_name.value) + (gen + ";\u0001")))))) : ("\u001F\u0019b\u0017\u0011\u0018\u0002\u0013\u000D\u000F\u0017\u000D\u0016\u0002\u0015\u000D\u0018\u0010\u0015\u0016\u0002" + (sanitize(c.name.value) + (gen + ("(" + (emit_ctor_fields(c.fields, tparams(), 0) + (")\u0002:\u0002" + (sanitize(base_name.value) + (gen + ";\u0001")))))))))))(emit_tparameter_suffix(tparams()));

    public static string emit_ctor_fields(List<ATypeExpr> fields, List<Name> tparams, long i) => ((i() == ((long)fields.Count)) ? "" : (emit_type_expr_tp(fields[(int)i()], tparams()) + ("\u0002F\u0011\u000D\u0017\u0016" + (_Cce.FromUnicode(i().ToString()) + (((i() < (((long)fields.Count) - 1)) ? ",\u0002" : "") + emit_ctor_fields(fields, tparams(), (i() + 1)))))));

    public static string emit_type_expr(ATypeExpr te) => emit_type_expr_tp(te, new List<Name>());

    public static string emit_type_expr_tp(ATypeExpr te, List<Name> tparams) => te switch { ANamedType(var name) => ((Func<long, string>)((idx) => ((idx >= 0) ? ("T" + _Cce.FromUnicode(idx.ToString())) : when_type_name(name().value))))(find_tparam_index(tparams(), name().value, 0)), AFunType(var p, var r) => ("F\u0019\u0012\u0018<" + (emit_type_expr_tp(p, tparams()) + (",\u0002" + (emit_type_expr_tp(r, tparams()) + ">")))), AAppType(var @base, var args) => (emit_type_expr_tp(@base, tparams()) + ("<" + (emit_type_expr_list_tp(args, tparams(), 0) + ">"))), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static long find_tparam_index(List<Name> tparams, string name, long i)
    {
        while (true)
        {
            if ((i() == ((long)tparams().Count)))
            {
            return (0 - 1);
            }
            else
            {
            if ((tparams()[(int)i()].value == name()))
            {
            return i();
            }
            else
            {
            var _tco_0 = tparams();
            var _tco_1 = name();
            var _tco_2 = (i() + 1);
            tparams = _tco_0;
            name = _tco_1;
            i = _tco_2;
            continue;
            }
            }
        }
    }

    public static string when_type_name(string n) => ((n == "I\u0012\u000E\u000D\u001D\u000D\u0015") ? "\u0017\u0010\u0012\u001D" : ((n == "N\u0019\u001Ab\u000D\u0015") ? "\u0016\u000D\u0018\u0011\u001A\u000F\u0017" : ((n == "T\u000Dx\u000E") ? "\u0013\u000E\u0015\u0011\u0012\u001D" : ((n == "B\u0010\u0010\u0017\u000D\u000F\u0012") ? "b\u0010\u0010\u0017" : ((n == "L\u0011\u0013\u000E") ? "L\u0011\u0013\u000E" : sanitize(n))))));

    public static string emit_type_expr_list(List<ATypeExpr> args, long i) => ((i() == ((long)args.Count)) ? "" : (emit_type_expr(args[(int)i()]) + (((i() < (((long)args.Count) - 1)) ? ",\u0002" : "") + emit_type_expr_list(args, (i() + 1)))));

    public static string emit_type_expr_list_tp(List<ATypeExpr> args, List<Name> tparams, long i) => ((i() == ((long)args.Count)) ? "" : (emit_type_expr_tp(args[(int)i()], tparams()) + (((i() < (((long)args.Count) - 1)) ? ",\u0002" : "") + emit_type_expr_list_tp(args, tparams(), (i() + 1)))));

    public static List<long> collect_type_var_ids(CodexType ty, List<long> acc) => ty() switch { object TypeVar => id(), };

    public static List<long> list_contains_int() => id();

    public static T772 acc<T772>() => list_append_int(acc(), id());

    public static List<long> FunTy(object p, object r) => collect_type_var_ids(r, collect_type_var_ids(p, acc()));

    public static List<long> ListTy(object elem) => collect_type_var_ids(elem, acc());

    public static List<long> ForAllTy(object id, object body) => collect_type_var_ids(body(), acc());

    public static T793 ConstructedTy<T793>(object name, object args) => collect_type_var_ids_list(args)(acc());

    public static T772 acc<T772>() => collect_type_var_ids_list;

    public static T797 List<T797>() => /* error: -> */ default(new List())(new Integer());

    public static T797 List<T797>() => collect_type_var_ids_list(types)(acc());

    public static T805 collect_type_var_ids_list_loop<T805>() => acc()(0)(((long)types.Count));

    public static T805 collect_type_var_ids_list_loop<T805>(object types, object acc, object i, object len)
    {
        while (true)
        {
            if ((i() == len))
            {
            return acc();
            }
            else
            {
            var _tco_0 = types;
            var _tco_1 = collect_type_var_ids(types[(int)i()], acc());
            var _tco_2 = (i() + 1);
            var _tco_3 = len;
            types = _tco_0;
            acc = _tco_1;
            i = _tco_2;
            len = _tco_3;
            continue;
            }
        }
    }

    public static List<long> list_contains_int(object xs, object n) => list_contains_int_loop(xs, n, 0, ((long)xs.Count));

    public static bool list_contains_int_loop(List<long> xs, long n, long i, long len)
    {
        while (true)
        {
            if ((i() == len))
            {
            return false;
            }
            else
            {
            if ((xs[(int)i()] == n))
            {
            return true;
            }
            else
            {
            var _tco_0 = xs;
            var _tco_1 = n;
            var _tco_2 = (i() + 1);
            var _tco_3 = len;
            xs = _tco_0;
            n = _tco_1;
            i = _tco_2;
            len = _tco_3;
            continue;
            }
            }
        }
    }

    public static List<long> list_append_int(List<long> xs, long n) => Enumerable.Concat(xs, new List<long> { n }).ToList();

    public static string generic_suffix(CodexType ty) => ((Func<List<long>, string>)((ids) => ((((long)ids.Count) == 0) ? "" : ("<" + (emit_type_params(ids, 0) + ">")))))(collect_type_var_ids(ty(), new List<long>()));

    public static string emit_type_params(List<long> ids, long i) => ((i() == ((long)ids.Count)) ? "" : ((i() == (((long)ids.Count) - 1)) ? ("T" + _Cce.FromUnicode(ids[(int)i()].ToString())) : ("T" + (_Cce.FromUnicode(ids[(int)i()].ToString()) + (",\u0002" + emit_type_params(ids, (i() + 1)))))));

    public static string extract_ctor_type_args(CodexType ty) => ty() switch { ConstructedTy(var name, var args) => ((((long)args.Count) == 0) ? "" : ("<" + (emit_cs_type_args(args, 0) + ">"))), _ => "", };

    public static bool is_self_call(IRExpr e, string func_name) => ((Func<ApplyChain, bool>)((chain) => is_self_call_root(chain.root, func_name)))(collect_apply_chain(e(), new List<IRExpr>()));

    public static bool is_self_call_root(IRExpr e, string func_name) => e() switch { IrName(var n, var ty) => (n == func_name), _ => false, };

    public static bool has_tail_call(IRExpr e, string func_name)
    {
        while (true)
        {
            var _tco_s = e();
            if (_tco_s is IrIf _tco_m0)
            {
                var c = _tco_m0.Field0;
                var t = _tco_m0.Field1;
                var el = _tco_m0.Field2;
                var ty = _tco_m0.Field3;
            return (has_tail_call(t, func_name) || has_tail_call(el, func_name));
            }
            else if (_tco_s is IrLet _tco_m1)
            {
                var name = _tco_m1.Field0;
                var ty = _tco_m1.Field1;
                var val = _tco_m1.Field2;
                var body = _tco_m1.Field3;
            var _tco_0 = body();
            var _tco_1 = func_name;
            e = _tco_0;
            func_name = _tco_1;
            continue;
            }
            else if (_tco_s is IrMatch _tco_m2)
            {
                var scrut = _tco_m2.Field0;
                var branches = _tco_m2.Field1;
                var ty = _tco_m2.Field2;
            return has_tail_call_branches(branches, func_name, 0);
            }
            else if (_tco_s is IrApply _tco_m3)
            {
                var f = _tco_m3.Field0;
                var a = _tco_m3.Field1;
                var ty = _tco_m3.Field2;
            return is_self_call(e(), func_name);
            }
            {
            return false;
            }
        }
    }

    public static bool has_tail_call_branches(List<IRBranch> branches, string func_name, long i)
    {
        while (true)
        {
            if ((i() == ((long)branches.Count)))
            {
            return false;
            }
            else
            {
            var b = branches[(int)i()];
            if (has_tail_call(b().body, func_name))
            {
            return true;
            }
            else
            {
            var _tco_0 = branches;
            var _tco_1 = func_name;
            var _tco_2 = (i() + 1);
            branches = _tco_0;
            func_name = _tco_1;
            i = _tco_2;
            continue;
            }
            }
        }
    }

    public static bool should_tco(IRDef d) => ((((long)d.@params.Count) == 0) ? false : has_tail_call(d.body, d.name));

    public static string emit_tco_def(IRDef d, List<ArityEntry> arities) => ((Func<CodexType, string>)((ret) => ((Func<string, string>)((gen) => ("\u0002\u0002\u0002\u0002\u001F\u0019b\u0017\u0011\u0018\u0002\u0013\u000E\u000F\u000E\u0011\u0018\u0002" + (cs_type(ret()) + ("\u0002" + (sanitize(d.name) + (gen + ("(" + (emit_def_params(d.@params, 0) + (")\u0001\u0002\u0002\u0002\u0002{\u0001\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u001B\u0014\u0011\u0017\u000D\u0002(\u000E\u0015\u0019\u000D)\u0001\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002{\u0001" + (emit_tco_body(d.body, d.name, d.@params, arities) + "\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002}\u0001\u0002\u0002\u0002\u0002}\u0001")))))))))))(generic_suffix(d.type_val))))(get_return_type(d.type_val, ((long)d.@params.Count)));

    public static string emit_tco_body(IRExpr e, string func_name, List<IRParam> @params, List<ArityEntry> arities) => e() switch { IrIf(var c, var t, var el, var ty) => emit_tco_if(c, t, el, func_name, @params, arities), IrLet(var name, var ty, var val, var body) => emit_tco_let(name(), ty(), val, body(), func_name, @params, arities), IrMatch(var scrut, var branches, var ty) => emit_tco_match(scrut, branches, func_name, @params, arities), IrApply(var f, var a, var rty) => emit_tco_apply(e(), func_name, @params, arities), _ => ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0015\u000D\u000E\u0019\u0015\u0012\u0002" + (emit_expr(e(), arities) + ";\u0001")), };

    public static string emit_tco_apply(IRExpr e, string func_name, List<IRParam> @params, List<ArityEntry> arities) => (is_self_call(e(), func_name) ? emit_tco_jump(e(), @params, arities) : ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0015\u000D\u000E\u0019\u0015\u0012\u0002" + (emit_expr(e(), arities) + ";\u0001")));

    public static string emit_tco_if(IRExpr cond, IRExpr t, IRExpr el, string func_name, List<IRParam> @params, List<ArityEntry> arities) => ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0011\u001C\u0002(" + (emit_expr(cond, arities) + (")\u0001\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002{\u0001" + (emit_tco_body(t, func_name, @params, arities) + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002}\u0001\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u000D\u0017\u0013\u000D\u0001\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002{\u0001" + (emit_tco_body(el, func_name, @params, arities) + "\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002}\u0001"))))));

    public static string emit_tco_let(string name, CodexType ty, IRExpr val, IRExpr body, string func_name, List<IRParam> @params, List<ArityEntry> arities) => ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002v\u000F\u0015\u0002" + (sanitize(name()) + ("\u0002=\u0002" + (emit_expr(val, arities) + (";\u0001" + emit_tco_body(body(), func_name, @params, arities))))));

    public static string emit_tco_match(IRExpr scrut, List<IRBranch> branches, string func_name, List<IRParam> @params, List<ArityEntry> arities) => ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002v\u000F\u0015\u0002_\u000E\u0018\u0010_\u0013\u0002=\u0002" + (emit_expr(scrut, arities) + (";\u0001" + emit_tco_match_branches(branches, func_name, @params, arities, 0, true))));

    public static string emit_tco_match_branches(List<IRBranch> branches, string func_name, List<IRParam> @params, List<ArityEntry> arities, long i, bool is_first) => ((i() == ((long)branches.Count)) ? "" : ((Func<IRBranch, string>)((b) => (emit_tco_match_branch(b(), func_name, @params, arities, i(), is_first) + emit_tco_match_branches(branches, func_name, @params, arities, (i() + 1), false))))(branches[(int)i()]));

    public static string emit_tco_match_branch(IRBranch b, string func_name, List<IRParam> @params, List<ArityEntry> arities, long idx, bool is_first) => b().pattern switch { IrWildPat { } => ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002{\u0001" + (emit_tco_body(b().body, func_name, @params, arities) + "\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002}\u0001")), IrVarPat(var name, var ty) => ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002{\u0001\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002v\u000F\u0015\u0002" + (sanitize(name()) + ("\u0002=\u0002_\u000E\u0018\u0010_\u0013;\u0001" + (emit_tco_body(b().body, func_name, @params, arities) + "\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002}\u0001")))), IrCtorPat(var name, var subs, var ty) => ((Func<string, string>)((keyword) => ((Func<string, string>)((match_var) => ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002" + (keyword + ("\u0002(_\u000E\u0018\u0010_\u0013\u0002\u0011\u0013\u0002" + (sanitize(name()) + ("\u0002" + (match_var + (")\u0001\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002{\u0001" + (emit_tco_ctor_bindings(subs, match_var, 0) + (emit_tco_body(b().body, func_name, @params, arities) + "\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002}\u0001")))))))))))(("_\u000E\u0018\u0010_\u001A" + _Cce.FromUnicode(idx.ToString())))))((is_first ? "\u0011\u001C" : "\u000D\u0017\u0013\u000D\u0002\u0011\u001C")), IrLitPat(var text, var ty) => ((Func<string, string>)((keyword) => ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002" + (keyword + ("\u0002(\u0010bj\u000D\u0018\u000E.Eq\u0019\u000F\u0017\u0013(_\u000E\u0018\u0010_\u0013,\u0002" + (text() + ("))\u0001\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002{\u0001" + (emit_tco_body(b().body, func_name, @params, arities) + "\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002}\u0001"))))))))((is_first ? "\u0011\u001C" : "\u000D\u0017\u0013\u000D\u0002\u0011\u001C")), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static string emit_tco_ctor_bindings(List<IRPat> subs, string match_var, long i) => ((i() == ((long)subs.Count)) ? "" : ((Func<IRPat, string>)((sub) => (emit_tco_ctor_binding(sub, match_var, i()) + emit_tco_ctor_bindings(subs, match_var, (i() + 1)))))(subs[(int)i()]));

    public static string emit_tco_ctor_binding(IRPat sub, string match_var, long i) => sub switch { IrVarPat(var name, var ty) => ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002v\u000F\u0015\u0002" + (sanitize(name()) + ("\u0002=\u0002" + (match_var + (".F\u0011\u000D\u0017\u0016" + (_Cce.FromUnicode(i().ToString()) + ";\u0001")))))), _ => "", };

    public static string emit_tco_jump(IRExpr e, List<IRParam> @params, List<ArityEntry> arities) => ((Func<ApplyChain, string>)((chain) => (emit_tco_temps(chain.args, arities, 0) + (emit_tco_assigns(@params, 0) + "\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0018\u0010\u0012\u000E\u0011\u0012\u0019\u000D;\u0001"))))(collect_apply_chain(e(), new List<IRExpr>()));

    public static string emit_tco_temps(List<IRExpr> args, List<ArityEntry> arities, long i) => ((i() == ((long)args.Count)) ? "" : ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002v\u000F\u0015\u0002_\u000E\u0018\u0010_" + (_Cce.FromUnicode(i().ToString()) + ("\u0002=\u0002" + (emit_expr(args[(int)i()], arities) + (";\u0001" + emit_tco_temps(args, arities, (i() + 1))))))));

    public static string emit_tco_assigns(List<IRParam> @params, long i) => ((i() == ((long)@params.Count)) ? "" : ((Func<IRParam, string>)((p) => ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002" + (sanitize(p.name) + ("\u0002=\u0002_\u000E\u0018\u0010_" + (_Cce.FromUnicode(i().ToString()) + (";\u0001" + emit_tco_assigns(@params, (i() + 1)))))))))(@params[(int)i()]));

    public static string emit_def(IRDef d, List<ArityEntry> arities) => (should_tco(d) ? emit_tco_def(d, arities) : ((Func<CodexType, string>)((ret) => ((Func<string, string>)((gen) => ("\u0002\u0002\u0002\u0002\u001F\u0019b\u0017\u0011\u0018\u0002\u0013\u000E\u000F\u000E\u0011\u0018\u0002" + (cs_type(ret()) + ("\u0002" + (sanitize(d.name) + (gen + ("(" + (emit_def_params(d.@params, 0) + (")\u0002=>\u0002" + (emit_expr(d.body, arities) + ";\u0001")))))))))))(generic_suffix(d.type_val))))(get_return_type(d.type_val, ((long)d.@params.Count))));

    public static CodexType get_return_type(CodexType ty, long n)
    {
        while (true)
        {
            if ((n == 0))
            {
            return strip_forall(ty());
            }
            else
            {
            var _tco_s = strip_forall(ty());
            if (_tco_s is FunTy _tco_m0)
            {
                var p = _tco_m0.Field0;
                var r = _tco_m0.Field1;
            var _tco_0 = r;
            var _tco_1 = (n - 1);
            ty = _tco_0;
            n = _tco_1;
            continue;
            }
            {
            return ty();
            }
            }
        }
    }

    public static CodexType strip_forall(CodexType ty)
    {
        while (true)
        {
            var _tco_s = ty();
            if (_tco_s is ForAllTy _tco_m0)
            {
                var id = _tco_m0.Field0;
                var body = _tco_m0.Field1;
            var _tco_0 = body();
            ty = _tco_0;
            continue;
            }
            {
            return ty();
            }
        }
    }

    public static string emit_def_params(List<IRParam> @params, long i) => ((i() == ((long)@params.Count)) ? "" : ((Func<IRParam, string>)((p) => (cs_type(p.type_val) + ("\u0002" + (sanitize(p.name) + (((i() < (((long)@params.Count) - 1)) ? ",\u0002" : "") + emit_def_params(@params, (i() + 1))))))))(@params[(int)i()]));

    public static string emit_cce_runtime() => ("\u0013\u000E\u000F\u000E\u0011\u0018\u0002\u0018\u0017\u000F\u0013\u0013\u0002_C\u0018\u000D\u0002{\u0001" + ("\u0002\u0002\u0002\u0002\u0013\u000E\u000F\u000E\u0011\u0018\u0002\u0015\u000D\u000F\u0016\u0010\u0012\u0017\u001E\u0002\u0011\u0012\u000E[]\u0002_\u000E\u0010U\u0012\u0011\u0002=\u0002{\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0003,\u0002\u0004\u0003,\u0002\u0006\u0005,\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0007\u000B,\u0002\u0007\u000C,\u0002\u0008\u0003,\u0002\u0008\u0004,\u0002\u0008\u0005,\u0002\u0008\u0006,\u0002\u0008\u0007,\u0002\u0008\u0008,\u0002\u0008\u0009,\u0002\u0008\u000A,\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0004\u0003\u0004,\u0002\u0004\u0004\u0009,\u0002\u000C\u000A,\u0002\u0004\u0004\u0004,\u0002\u0004\u0003\u0008,\u0002\u0004\u0004\u0003,\u0002\u0004\u0004\u0008,\u0002\u0004\u0003\u0007,\u0002\u0004\u0004\u0007,\u0002\u0004\u0003\u0003,\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0004\u0003\u000B,\u0002\u000C\u000C,\u0002\u0004\u0004\u000A,\u0002\u0004\u0003\u000C,\u0002\u0004\u0004\u000C,\u0002\u0004\u0003\u0005,\u0002\u0004\u0003\u0006,\u0002\u0004\u0005\u0004,\u0002\u0004\u0004\u0005,\u0002\u000C\u000B,\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0004\u0004\u000B,\u0002\u0004\u0003\u000A,\u0002\u0004\u0003\u0009,\u0002\u0004\u0005\u0003,\u0002\u0004\u0004\u0006,\u0002\u0004\u0005\u0005,\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0009\u000C,\u0002\u000B\u0007,\u0002\u0009\u0008,\u0002\u000A\u000C,\u0002\u000A\u0006,\u0002\u000A\u000B,\u0002\u000B\u0006,\u0002\u000A\u0005,\u0002\u000B\u0005,\u0002\u0009\u000B,\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u000A\u0009,\u0002\u0009\u000A,\u0002\u000B\u0008,\u0002\u000A\u000A,\u0002\u000B\u000A,\u0002\u000A\u0003,\u0002\u000A\u0004,\u0002\u000B\u000C,\u0002\u000B\u0003,\u0002\u0009\u0009,\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u000B\u0009,\u0002\u000A\u0008,\u0002\u000A\u0007,\u0002\u000B\u000B,\u0002\u000B\u0004,\u0002\u000C\u0003,\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0007\u0009,\u0002\u0007\u0007,\u0002\u0006\u0006,\u0002\u0009\u0006,\u0002\u0008\u000B,\u0002\u0008\u000C,\u0002\u0006\u000C,\u0002\u0006\u0007,\u0002\u0007\u0008,\u0002\u0007\u0003,\u0002\u0007\u0004,\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0007\u0006,\u0002\u0009\u0004,\u0002\u0007\u0005,\u0002\u0009\u0003,\u0002\u0009\u0005,\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0007\u000A,\u0002\u0009\u0007,\u0002\u0006\u0008,\u0002\u0006\u000B,\u0002\u000C\u0008,\u0002\u000C\u0005,\u0002\u0004\u0005\u0007,\u0002\u000C\u0004,\u0002\u000C\u0006,\u0002\u0004\u0005\u0006,\u0002\u0004\u0005\u0008,\u0002\u0004\u0005\u0009,\u0002\u000C\u0009,\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0005\u0006\u0006,\u0002\u0005\u0006\u0005,\u0002\u0005\u0006\u0007,\u0002\u0005\u0006\u0008,\u0002\u0005\u0005\u0008,\u0002\u0005\u0005\u0007,\u0002\u0005\u0005\u0009,\u0002\u0005\u0005\u000B,\u0002\u0005\u0007\u0006,\u0002\u0005\u0007\u0005,\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0005\u0007\u0007,\u0002\u0005\u0007\u0009,\u0002\u0005\u0008\u0003,\u0002\u0005\u0007\u000C,\u0002\u0005\u0008\u0004,\u0002\u0005\u0008\u0005,\u0002\u0005\u0007\u0004,\u0002\u0005\u0006\u0004,\u0002\u0005\u0006\u000A,\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0004\u0003\u000A\u0005,\u0002\u0004\u0003\u000B\u0009,\u0002\u0004\u0003\u000A\u000A,\u0002\u0004\u0003\u000B\u0003,\u0002\u0004\u0003\u000B\u0008,\u0002\u0004\u0003\u000C\u0003,\u0002\u0004\u0003\u000B\u000C,\u0002\u0004\u0003\u000B\u000B,\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0004\u0003\u000A\u0007,\u0002\u0004\u0003\u000B\u0006,\u0002\u0004\u0003\u000B\u0005,\u0002\u0004\u0003\u000B\u0007,\u0002\u0004\u0003\u000A\u0009,\u0002\u0004\u0003\u000B\u000A,\u0002\u0004\u0003\u000C\u0004\u0001" + ("\u0002\u0002\u0002\u0002};\u0001" + ("\u0002\u0002\u0002\u0002\u0013\u000E\u000F\u000E\u0011\u0018\u0002\u0015\u000D\u000F\u0016\u0010\u0012\u0017\u001E\u0002D\u0011\u0018\u000E\u0011\u0010\u0012\u000F\u0015\u001E<\u0011\u0012\u000E,\u0002\u0011\u0012\u000E>\u0002_\u001C\u0015\u0010\u001AU\u0012\u0011\u0002=\u0002\u0012\u000D\u001B();\u0001" + ("\u0002\u0002\u0002\u0002\u0013\u000E\u000F\u000E\u0011\u0018\u0002_C\u0018\u000D()\u0002{\u0002\u001C\u0010\u0015\u0002(\u0011\u0012\u000E\u0002\u0011\u0002=\u0002\u0003;\u0002\u0011\u0002<\u0002\u0004\u0005\u000B;\u0002\u0011++)\u0002_\u001C\u0015\u0010\u001AU\u0012\u0011[_\u000E\u0010U\u0012\u0011[\u0011]]\u0002=\u0002\u0011;\u0002}\u0001" + ("\u0002\u0002\u0002\u0002\u001F\u0019b\u0017\u0011\u0018\u0002\u0013\u000E\u000F\u000E\u0011\u0018\u0002\u0013\u000E\u0015\u0011\u0012\u001D\u0002F\u0015\u0010\u001AU\u0012\u0011\u0018\u0010\u0016\u000D(\u0013\u000E\u0015\u0011\u0012\u001D\u0002\u0013)\u0002{\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002v\u000F\u0015\u0002\u0018\u0013\u0002=\u0002\u0012\u000D\u001B\u0002\u0018\u0014\u000F\u0015[\u0013.L\u000D\u0012\u001D\u000E\u0014];\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u001C\u0010\u0015\u0002(\u0011\u0012\u000E\u0002\u0011\u0002=\u0002\u0003;\u0002\u0011\u0002<\u0002\u0013.L\u000D\u0012\u001D\u000E\u0014;\u0002\u0011++)\u0002{\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0011\u0012\u000E\u0002\u0019\u0002=\u0002\u0013[\u0011];\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0018\u0013[\u0011]\u0002=\u0002_\u001C\u0015\u0010\u001AU\u0012\u0011.T\u0015\u001EG\u000D\u000EV\u000F\u0017\u0019\u000D(\u0019,\u0002\u0010\u0019\u000E\u0002\u0011\u0012\u000E\u0002\u0018)\u0002?\u0002(\u0018\u0014\u000F\u0015)\u0018\u0002:\u0002(\u0018\u0014\u000F\u0015)\u0009\u000B;\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002}\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0015\u000D\u000E\u0019\u0015\u0012\u0002\u0012\u000D\u001B\u0002\u0013\u000E\u0015\u0011\u0012\u001D(\u0018\u0013);\u0001" + ("\u0002\u0002\u0002\u0002}\u0001" + ("\u0002\u0002\u0002\u0002\u001F\u0019b\u0017\u0011\u0018\u0002\u0013\u000E\u000F\u000E\u0011\u0018\u0002\u0013\u000E\u0015\u0011\u0012\u001D\u0002T\u0010U\u0012\u0011\u0018\u0010\u0016\u000D(\u0013\u000E\u0015\u0011\u0012\u001D\u0002\u0013)\u0002{\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002v\u000F\u0015\u0002\u0018\u0013\u0002=\u0002\u0012\u000D\u001B\u0002\u0018\u0014\u000F\u0015[\u0013.L\u000D\u0012\u001D\u000E\u0014];\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u001C\u0010\u0015\u0002(\u0011\u0012\u000E\u0002\u0011\u0002=\u0002\u0003;\u0002\u0011\u0002<\u0002\u0013.L\u000D\u0012\u001D\u000E\u0014;\u0002\u0011++)\u0002{\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0011\u0012\u000E\u0002b\u0002=\u0002\u0013[\u0011];\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0018\u0013[\u0011]\u0002=\u0002(b\u0002>=\u0002\u0003\u0002&&\u0002b\u0002<\u0002\u0004\u0005\u000B)\u0002?\u0002(\u0018\u0014\u000F\u0015)_\u000E\u0010U\u0012\u0011[b]\u0002:\u0002'\\u0019FFFD';\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002}\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0015\u000D\u000E\u0019\u0015\u0012\u0002\u0012\u000D\u001B\u0002\u0013\u000E\u0015\u0011\u0012\u001D(\u0018\u0013);\u0001" + ("\u0002\u0002\u0002\u0002}\u0001" + ("\u0002\u0002\u0002\u0002\u001F\u0019b\u0017\u0011\u0018\u0002\u0013\u000E\u000F\u000E\u0011\u0018\u0002\u0017\u0010\u0012\u001D\u0002U\u0012\u0011T\u0010C\u0018\u000D(\u0017\u0010\u0012\u001D\u0002\u0019)\u0002{\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0015\u000D\u000E\u0019\u0015\u0012\u0002_\u001C\u0015\u0010\u001AU\u0012\u0011.T\u0015\u001EG\u000D\u000EV\u000F\u0017\u0019\u000D((\u0011\u0012\u000E)\u0019,\u0002\u0010\u0019\u000E\u0002\u0011\u0012\u000E\u0002\u0018)\u0002?\u0002\u0018\u0002:\u0002\u0009\u000B;\u0001" + ("\u0002\u0002\u0002\u0002}\u0001" + ("\u0002\u0002\u0002\u0002\u001F\u0019b\u0017\u0011\u0018\u0002\u0013\u000E\u000F\u000E\u0011\u0018\u0002\u0017\u0010\u0012\u001D\u0002C\u0018\u000DT\u0010U\u0012\u0011(\u0017\u0010\u0012\u001D\u0002b)\u0002{\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0015\u000D\u000E\u0019\u0015\u0012\u0002(b\u0002>=\u0002\u0003\u0002&&\u0002b\u0002<\u0002\u0004\u0005\u000B)\u0002?\u0002_\u000E\u0010U\u0012\u0011[(\u0011\u0012\u000E)b]\u0002:\u0002\u0009\u0008\u0008\u0006\u0006;\u0001" + ("\u0002\u0002\u0002\u0002}\u0001" + "}\u0001\u0001"))))))))))))))))))))))))))))))))))))))))));

    public static string emit_full_module(IRModule m, List<ATypeDef> type_defs) => ((Func<List<ArityEntry>, string>)((arities) => ("\u0019\u0013\u0011\u0012\u001D\u0002S\u001E\u0013\u000E\u000D\u001A;\u0001\u0019\u0013\u0011\u0012\u001D\u0002S\u001E\u0013\u000E\u000D\u001A.C\u0010\u0017\u0017\u000D\u0018\u000E\u0011\u0010\u0012\u0013.G\u000D\u0012\u000D\u0015\u0011\u0018;\u0001\u0019\u0013\u0011\u0012\u001D\u0002S\u001E\u0013\u000E\u000D\u001A.IO;\u0001\u0019\u0013\u0011\u0012\u001D\u0002S\u001E\u0013\u000E\u000D\u001A.L\u0011\u0012q;\u0001\u0019\u0013\u0011\u0012\u001D\u0002S\u001E\u0013\u000E\u000D\u001A.T\u0014\u0015\u000D\u000F\u0016\u0011\u0012\u001D.T\u000F\u0013\"\u0013;\u0001\u0001" + ("C\u0010\u0016\u000Dx_" + (sanitize(m.name.value) + (".\u001A\u000F\u0011\u0012();\u0001\u0001" + (emit_cce_runtime() + (emit_type_defs(type_defs, 0) + (emit_class_header(m.name.value) + (emit_defs(m.defs, 0, arities) + "}\u0001"))))))))))(build_arity_map(m.defs, 0));

    public static string emit_module(IRModule m) => ((Func<List<ArityEntry>, string>)((arities) => ("\u0019\u0013\u0011\u0012\u001D\u0002S\u001E\u0013\u000E\u000D\u001A;\u0001\u0019\u0013\u0011\u0012\u001D\u0002S\u001E\u0013\u000E\u000D\u001A.C\u0010\u0017\u0017\u000D\u0018\u000E\u0011\u0010\u0012\u0013.G\u000D\u0012\u000D\u0015\u0011\u0018;\u0001\u0019\u0013\u0011\u0012\u001D\u0002S\u001E\u0013\u000E\u000D\u001A.IO;\u0001\u0019\u0013\u0011\u0012\u001D\u0002S\u001E\u0013\u000E\u000D\u001A.L\u0011\u0012q;\u0001\u0019\u0013\u0011\u0012\u001D\u0002S\u001E\u0013\u000E\u000D\u001A.T\u0014\u0015\u000D\u000F\u0016\u0011\u0012\u001D.T\u000F\u0013\"\u0013;\u0001\u0001" + ("C\u0010\u0016\u000Dx_" + (sanitize(m.name.value) + (".\u001A\u000F\u0011\u0012();\u0001\u0001" + (emit_cce_runtime() + (emit_class_header(m.name.value) + (emit_defs(m.defs, 0, arities) + "}\u0001")))))))))(build_arity_map(m.defs, 0));

    public static string emit_class_header(string name) => ("\u001F\u0019b\u0017\u0011\u0018\u0002\u0013\u000E\u000F\u000E\u0011\u0018\u0002\u0018\u0017\u000F\u0013\u0013\u0002C\u0010\u0016\u000Dx_" + (sanitize(name()) + "\u0001{\u0001"));

    public static string emit_defs(List<IRDef> defs, long i, List<ArityEntry> arities) => ((i() == ((long)defs.Count)) ? "" : (emit_def(defs[(int)i()], arities) + ("\u0001" + emit_defs(defs, (i() + 1), arities))));

    public static bool is_cs_keyword(string n) => ((n == "\u0018\u0017\u000F\u0013\u0013") ? true : ((n == "\u0013\u000E\u000F\u000E\u0011\u0018") ? true : ((n == "v\u0010\u0011\u0016") ? true : ((n == "\u0015\u000D\u000E\u0019\u0015\u0012") ? true : ((n == "\u0011\u001C") ? true : ((n == "\u000D\u0017\u0013\u000D") ? true : ((n == "\u001C\u0010\u0015") ? true : ((n == "\u001B\u0014\u0011\u0017\u000D") ? true : ((n == "\u0016\u0010") ? true : ((n == "\u0013\u001B\u0011\u000E\u0018\u0014") ? true : ((n == "\u0018\u000F\u0013\u000D") ? true : ((n == "b\u0015\u000D\u000F\"") ? true : ((n == "\u0018\u0010\u0012\u000E\u0011\u0012\u0019\u000D") ? true : ((n == "\u0012\u000D\u001B") ? true : ((n == "\u000E\u0014\u0011\u0013") ? true : ((n == "b\u000F\u0013\u000D") ? true : ((n == "\u0012\u0019\u0017\u0017") ? true : ((n == "\u000E\u0015\u0019\u000D") ? true : ((n == "\u001C\u000F\u0017\u0013\u000D") ? true : ((n == "\u0011\u0012\u000E") ? true : ((n == "\u0017\u0010\u0012\u001D") ? true : ((n == "\u0013\u000E\u0015\u0011\u0012\u001D") ? true : ((n == "b\u0010\u0010\u0017") ? true : ((n == "\u0016\u0010\u0019b\u0017\u000D") ? true : ((n == "\u0016\u000D\u0018\u0011\u001A\u000F\u0017") ? true : ((n == "\u0010bj\u000D\u0018\u000E") ? true : ((n == "\u0011\u0012") ? true : ((n == "\u0011\u0013") ? true : ((n == "\u000F\u0013") ? true : ((n == "\u000E\u001E\u001F\u000D\u0010\u001C") ? true : ((n == "\u0016\u000D\u001C\u000F\u0019\u0017\u000E") ? true : ((n == "\u000E\u0014\u0015\u0010\u001B") ? true : ((n == "\u000E\u0015\u001E") ? true : ((n == "\u0018\u000F\u000E\u0018\u0014") ? true : ((n == "\u001C\u0011\u0012\u000F\u0017\u0017\u001E") ? true : ((n == "\u0019\u0013\u0011\u0012\u001D") ? true : ((n == "\u0012\u000F\u001A\u000D\u0013\u001F\u000F\u0018\u000D") ? true : ((n == "\u001F\u0019b\u0017\u0011\u0018") ? true : ((n == "\u001F\u0015\u0011v\u000F\u000E\u000D") ? true : ((n == "\u001F\u0015\u0010\u000E\u000D\u0018\u000E\u000D\u0016") ? true : ((n == "\u0011\u0012\u000E\u000D\u0015\u0012\u000F\u0017") ? true : ((n == "\u000Fb\u0013\u000E\u0015\u000F\u0018\u000E") ? true : ((n == "\u0013\u000D\u000F\u0017\u000D\u0016") ? true : ((n == "\u0010v\u000D\u0015\u0015\u0011\u0016\u000D") ? true : ((n == "v\u0011\u0015\u000E\u0019\u000F\u0017") ? true : ((n == "\u000Dv\u000D\u0012\u000E") ? true : ((n == "\u0016\u000D\u0017\u000D\u001D\u000F\u000E\u000D") ? true : ((n == "\u0010\u0019\u000E") ? true : ((n == "\u0015\u000D\u001C") ? true : ((n == "\u001F\u000F\u0015\u000F\u001A\u0013") ? true : false))))))))))))))))))))))))))))))))))))))))))))))))));

    public static string sanitize(string name) => ((Func<string, string>)((s) => (is_cs_keyword(s) ? ("@" + s) : (is_cs_member_name(s) ? (s + "_") : s))))(name().Replace("-", "_"));

    public static bool is_cs_member_name(string n) => ((n == "Eq\u0019\u000F\u0017\u0013") ? true : ((n == "G\u000D\u000EH\u000F\u0013\u0014C\u0010\u0016\u000D") ? true : ((n == "T\u0010S\u000E\u0015\u0011\u0012\u001D") ? true : ((n == "G\u000D\u000ET\u001E\u001F\u000D") ? true : ((n == "M\u000D\u001Ab\u000D\u0015\u001B\u0011\u0013\u000DC\u0017\u0010\u0012\u000D") ? true : false)))));

    public static string cs_type(CodexType ty) => ty() switch { IntegerTy { } => "\u0017\u0010\u0012\u001D", NumberTy { } => "\u0016\u000D\u0018\u0011\u001A\u000F\u0017", object TextTy => "\u0013\u000E\u0015\u0011\u0012\u001D", };

    public static string integer_to_text() => (new ForAllTy(id(), body()) ? cs_type(body()) : new SumTy(name(), ctors));

    public static Func<string, string> sanitize() => /* error: . */ default(value);

    public static string RecordTy(object name, object fields) => sanitize(name().value);

    public static T793 ConstructedTy<T793>(object name, object args) => ((((long)args.Count) == 0) ? sanitize(name().value) : (sanitize(name().value) + ("<" + (emit_cs_type_args(args, 0) + ">"))));

    public static string EffectfulTy(object effects, object ret) => cs_type(ret());

    public static string emit_cs_type_args(List<CodexType> args, long i) => ((i() == ((long)args.Count)) ? "" : ((Func<string, string>)((t) => ((i() == (((long)args.Count) - 1)) ? t : (t + (",\u0002" + emit_cs_type_args(args, (i() + 1)))))))(cs_type(args[(int)i()])));

    public static List<ArityEntry> build_arity_map(List<IRDef> defs, long i) => ((i() == ((long)defs.Count)) ? new List<ArityEntry>() : ((Func<IRDef, List<ArityEntry>>)((d) => Enumerable.Concat(new List<ArityEntry> { new ArityEntry(name: d.name, arity: ((long)d.@params.Count)) }, build_arity_map(defs, (i() + 1))).ToList()))(defs[(int)i()]));

    public static List<ArityEntry> build_arity_map_from_ast(List<ADef> defs, long i) => ((i() == ((long)defs.Count)) ? new List<ArityEntry>() : ((Func<ADef, List<ArityEntry>>)((d) => Enumerable.Concat(new List<ArityEntry> { new ArityEntry(name: d.name.value, arity: ((long)d.@params.Count)) }, build_arity_map_from_ast(defs, (i() + 1))).ToList()))(defs[(int)i()]));

    public static long lookup_arity(List<ArityEntry> entries, string name) => lookup_arity_loop(entries(), name(), 0, ((long)entries().Count));

    public static long lookup_arity_loop(List<ArityEntry> entries, string name, long i, long len)
    {
        while (true)
        {
            if ((i() == len))
            {
            return (0 - 1);
            }
            else
            {
            var e = entries()[(int)i()];
            if ((e().name == name()))
            {
            return e().arity;
            }
            else
            {
            var _tco_0 = entries();
            var _tco_1 = name();
            var _tco_2 = (i() + 1);
            var _tco_3 = len;
            entries = _tco_0;
            name = _tco_1;
            i = _tco_2;
            len = _tco_3;
            continue;
            }
            }
        }
    }

    public static ApplyChain collect_apply_chain(IRExpr e, List<IRExpr> acc)
    {
        while (true)
        {
            var _tco_s = e();
            if (_tco_s is IrApply _tco_m0)
            {
                var f = _tco_m0.Field0;
                var a = _tco_m0.Field1;
                var ty = _tco_m0.Field2;
            var _tco_0 = f;
            var _tco_1 = Enumerable.Concat(new List<IRExpr> { a }, acc()).ToList();
            e = _tco_0;
            acc = _tco_1;
            continue;
            }
            {
            return new ApplyChain(root: e(), args: acc());
            }
        }
    }

    public static bool is_upper_letter(long c) => ((Func<long, bool>)((code) => ((code >= 41) && (code <= 64))))(c);

    public static string emit_apply_args(List<IRExpr> args, List<ArityEntry> arities, long i) => ((i() == ((long)args.Count)) ? "" : ((i() == (((long)args.Count) - 1)) ? emit_expr(args[(int)i()], arities) : (emit_expr(args[(int)i()], arities) + (",\u0002" + emit_apply_args(args, arities, (i() + 1))))));

    public static string emit_partial_params(long i, long count) => ((i() == count) ? "" : ((i() == (count - 1)) ? ("_\u001F" + (_Cce.FromUnicode(i().ToString()) + "_")) : ("_\u001F" + (_Cce.FromUnicode(i().ToString()) + ("_" + (",\u0002" + emit_partial_params((i() + 1), count)))))));

    public static string emit_partial_wrappers(long i, long count) => ((i() == count) ? "" : ("(_\u001F" + (_Cce.FromUnicode(i().ToString()) + ("_)\u0002=>\u0002" + emit_partial_wrappers((i() + 1), count)))));

    public static bool is_builtin_name(string n) => ((n == "\u0013\u0014\u0010\u001B") ? true : ((n == "\u0012\u000D\u001D\u000F\u000E\u000D") ? true : ((n == "\u001F\u0015\u0011\u0012\u000E-\u0017\u0011\u0012\u000D") ? true : ((n == "\u000E\u000Dx\u000E-\u0017\u000D\u0012\u001D\u000E\u0014") ? true : ((n == "\u0011\u0013-\u0017\u000D\u000E\u000E\u000D\u0015") ? true : ((n == "\u0011\u0013-\u0016\u0011\u001D\u0011\u000E") ? true : ((n == "\u0011\u0013-\u001B\u0014\u0011\u000E\u000D\u0013\u001F\u000F\u0018\u000D") ? true : ((n == "\u000E\u000Dx\u000E-\u000E\u0010-\u0011\u0012\u000E\u000D\u001D\u000D\u0015") ? true : ((n == "\u0011\u0012\u000E\u000D\u001D\u000D\u0015-\u000E\u0010-\u000E\u000Dx\u000E") ? true : ((n == "\u0018\u0014\u000F\u0015-\u0018\u0010\u0016\u000D") ? true : ((n == "\u0018\u0014\u000F\u0015-\u0018\u0010\u0016\u000D-\u000F\u000E") ? true : ((n == "\u0018\u0010\u0016\u000D-\u000E\u0010-\u0018\u0014\u000F\u0015") ? true : ((n == "\u0018\u0014\u000F\u0015-\u000E\u0010-\u000E\u000Dx\u000E") ? true : ((n == "\u0017\u0011\u0013\u000E-\u0017\u000D\u0012\u001D\u000E\u0014") ? true : ((n == "\u0018\u0014\u000F\u0015-\u000F\u000E") ? true : ((n == "\u0013\u0019b\u0013\u000E\u0015\u0011\u0012\u001D") ? true : ((n == "\u0017\u0011\u0013\u000E-\u000F\u000E") ? true : ((n == "\u0017\u0011\u0013\u000E-\u0011\u0012\u0013\u000D\u0015\u000E-\u000F\u000E") ? true : ((n == "\u0017\u0011\u0013\u000E-\u0013\u0012\u0010\u0018") ? true : ((n == "\u000E\u000Dx\u000E-\u0018\u0010\u001A\u001F\u000F\u0015\u000D") ? true : ((n == "\u000E\u000Dx\u000E-\u0015\u000D\u001F\u0017\u000F\u0018\u000D") ? true : ((n == "\u0010\u001F\u000D\u0012-\u001C\u0011\u0017\u000D") ? true : ((n == "\u0015\u000D\u000F\u0016-\u000F\u0017\u0017") ? true : ((n == "\u0018\u0017\u0010\u0013\u000D-\u001C\u0011\u0017\u000D") ? true : ((n == "\u0015\u000D\u000F\u0016-\u0017\u0011\u0012\u000D") ? true : ((n == "\u0015\u000D\u000F\u0016-\u001C\u0011\u0017\u000D") ? true : ((n == "\u001B\u0015\u0011\u000E\u000D-\u001C\u0011\u0017\u000D") ? true : ((n == "\u001C\u0011\u0017\u000D-\u000Dx\u0011\u0013\u000E\u0013") ? true : ((n == "\u0017\u0011\u0013\u000E-\u001C\u0011\u0017\u000D\u0013") ? true : ((n == "\u000E\u000Dx\u000E-\u0018\u0010\u0012\u0018\u000F\u000E-\u0017\u0011\u0013\u000E") ? true : ((n == "\u000E\u000Dx\u000E-\u0013\u001F\u0017\u0011\u000E") ? true : ((n == "\u000E\u000Dx\u000E-\u0018\u0010\u0012\u000E\u000F\u0011\u0012\u0013") ? true : ((n == "\u000E\u000Dx\u000E-\u0013\u000E\u000F\u0015\u000E\u0013-\u001B\u0011\u000E\u0014") ? true : ((n == "\u001D\u000D\u000E-\u000F\u0015\u001D\u0013") ? true : ((n == "\u001D\u000D\u000E-\u000D\u0012v") ? true : ((n == "\u0018\u0019\u0015\u0015\u000D\u0012\u000E-\u0016\u0011\u0015") ? true : ((n == "\u0015\u0019\u0012-\u001F\u0015\u0010\u0018\u000D\u0013\u0013") ? true : ((n == "\u001C\u0010\u0015\"") ? true : ((n == "\u000F\u001B\u000F\u0011\u000E") ? true : ((n == "\u001F\u000F\u0015") ? true : ((n == "\u0015\u000F\u0018\u000D") ? true : false)))))))))))))))))))))))))))))))))))))))));

    public static string emit_builtin(string n, List<IRExpr> args, List<ArityEntry> arities) => ((n == "\u0013\u0014\u0010\u001B") ? ("_C\u0018\u000D.F\u0015\u0010\u001AU\u0012\u0011\u0018\u0010\u0016\u000D(C\u0010\u0012v\u000D\u0015\u000E.T\u0010S\u000E\u0015\u0011\u0012\u001D(" + (emit_expr(args[(int)0], arities) + "))")) : ((n == "\u0012\u000D\u001D\u000F\u000E\u000D") ? ("(-" + (emit_expr(args[(int)0], arities) + ")")) : ((n == "\u001F\u0015\u0011\u0012\u000E-\u0017\u0011\u0012\u000D") ? ("((F\u0019\u0012\u0018<\u0010bj\u000D\u0018\u000E>)(()\u0002=>\u0002{\u0002C\u0010\u0012\u0013\u0010\u0017\u000D.W\u0015\u0011\u000E\u000DL\u0011\u0012\u000D(_C\u0018\u000D.T\u0010U\u0012\u0011\u0018\u0010\u0016\u000D(" + (emit_expr(args[(int)0], arities) + "));\u0002\u0015\u000D\u000E\u0019\u0015\u0012\u0002\u0012\u0019\u0017\u0017;\u0002}))()")) : ((n == "\u000E\u000Dx\u000E-\u0017\u000D\u0012\u001D\u000E\u0014") ? ("((\u0017\u0010\u0012\u001D)" + (emit_expr(args[(int)0], arities) + ".L\u000D\u0012\u001D\u000E\u0014)")) : ((n == "\u0011\u0013-\u0017\u000D\u000E\u000E\u000D\u0015") ? ("(" + (emit_expr(args[(int)0], arities) + ("\u0002>=\u0002\u0004\u0006L\u0002&&\u0002" + (emit_expr(args[(int)0], arities) + "\u0002<=\u0002\u0009\u0007L)")))) : ((n == "\u0011\u0013-\u0016\u0011\u001D\u0011\u000E") ? ("(" + (emit_expr(args[(int)0], arities) + ("\u0002>=\u0002\u0006L\u0002&&\u0002" + (emit_expr(args[(int)0], arities) + "\u0002<=\u0002\u0004\u0005L)")))) : ((n == "\u0011\u0013-\u001B\u0014\u0011\u000E\u000D\u0013\u001F\u000F\u0018\u000D") ? ("(" + (emit_expr(args[(int)0], arities) + "\u0002<=\u0002\u0005L)")) : ((n == "\u000E\u000Dx\u000E-\u000E\u0010-\u0011\u0012\u000E\u000D\u001D\u000D\u0015") ? ("\u0017\u0010\u0012\u001D.P\u000F\u0015\u0013\u000D(_C\u0018\u000D.T\u0010U\u0012\u0011\u0018\u0010\u0016\u000D(" + (emit_expr(args[(int)0], arities) + "))")) : ((n == "\u0011\u0012\u000E\u000D\u001D\u000D\u0015-\u000E\u0010-\u000E\u000Dx\u000E") ? ("_C\u0018\u000D.F\u0015\u0010\u001AU\u0012\u0011\u0018\u0010\u0016\u000D(" + (emit_expr(args[(int)0], arities) + ".T\u0010S\u000E\u0015\u0011\u0012\u001D())")) : ((n == "\u0018\u0014\u000F\u0015-\u0018\u0010\u0016\u000D") ? emit_expr(args[(int)0], arities) : ((n == "\u0018\u0014\u000F\u0015-\u0018\u0010\u0016\u000D-\u000F\u000E") ? ("((\u0017\u0010\u0012\u001D)" + (emit_expr(args[(int)0], arities) + ("[(\u0011\u0012\u000E)" + (emit_expr(args[(int)1], arities) + "])")))) : ((n == "\u0018\u0010\u0016\u000D-\u000E\u0010-\u0018\u0014\u000F\u0015") ? emit_expr(args[(int)0], arities) : ((n == "\u0018\u0014\u000F\u0015-\u000E\u0010-\u000E\u000Dx\u000E") ? ("((\u0018\u0014\u000F\u0015)" + (emit_expr(args[(int)0], arities) + ").T\u0010S\u000E\u0015\u0011\u0012\u001D()")) : ((n == "\u0017\u0011\u0013\u000E-\u0017\u000D\u0012\u001D\u000E\u0014") ? ("((\u0017\u0010\u0012\u001D)" + (emit_expr(args[(int)0], arities) + ".C\u0010\u0019\u0012\u000E)")) : ((n == "\u0018\u0014\u000F\u0015-\u000F\u000E") ? ("((\u0017\u0010\u0012\u001D)" + (emit_expr(args[(int)0], arities) + ("[(\u0011\u0012\u000E)" + (emit_expr(args[(int)1], arities) + "])")))) : ((n == "\u0013\u0019b\u0013\u000E\u0015\u0011\u0012\u001D") ? (emit_expr(args[(int)0], arities) + (".S\u0019b\u0013\u000E\u0015\u0011\u0012\u001D((\u0011\u0012\u000E)" + (emit_expr(args[(int)1], arities) + (",\u0002(\u0011\u0012\u000E)" + (emit_expr(args[(int)2], arities) + ")"))))) : ((n == "\u0017\u0011\u0013\u000E-\u000F\u000E") ? (emit_expr(args[(int)0], arities) + ("[(\u0011\u0012\u000E)" + (emit_expr(args[(int)1], arities) + "]"))) : ((n == "\u0017\u0011\u0013\u000E-\u0011\u0012\u0013\u000D\u0015\u000E-\u000F\u000E") ? ((Func<bool, string>)((elem_ty) => ("((F\u0019\u0012\u0018<L\u0011\u0013\u000E<" + (cs_type(elem_ty) + (">>)(()\u0002=>\u0002{\u0002v\u000F\u0015\u0002_\u0017\u0002=\u0002\u0012\u000D\u001B\u0002L\u0011\u0013\u000E<" + (cs_type(elem_ty) + (">(" + (emit_expr(args[(int)0], arities) + (");\u0002_\u0017.I\u0012\u0013\u000D\u0015\u000E((\u0011\u0012\u000E)" + (emit_expr(args[(int)1], arities) + (",\u0002" + (emit_expr(args[(int)2], arities) + ");\u0002\u0015\u000D\u000E\u0019\u0015\u0012\u0002_\u0017;\u0002}))()"))))))))))))(ir_expr_type(args[(int)0]) switch { ListTy(var et) => et, _ => ErrorTy(), }) : ((n == "\u0017\u0011\u0013\u000E-\u0013\u0012\u0010\u0018") ? ((Func<bool, string>)((elem_ty) => ("((F\u0019\u0012\u0018<L\u0011\u0013\u000E<" + (cs_type(elem_ty) + (">>)(()\u0002=>\u0002{\u0002v\u000F\u0015\u0002_\u0017\u0002=\u0002" + (emit_expr(args[(int)0], arities) + (";\u0002_\u0017.A\u0016\u0016(" + (emit_expr(args[(int)1], arities) + ");\u0002\u0015\u000D\u000E\u0019\u0015\u0012\u0002_\u0017;\u0002}))()"))))))))(ir_expr_type(args[(int)0]) switch { ListTy(var et) => et, _ => ErrorTy(), }) : ((n == "\u000E\u000Dx\u000E-\u0018\u0010\u001A\u001F\u000F\u0015\u000D") ? ("(\u0017\u0010\u0012\u001D)\u0013\u000E\u0015\u0011\u0012\u001D.C\u0010\u001A\u001F\u000F\u0015\u000DO\u0015\u0016\u0011\u0012\u000F\u0017(" + (emit_expr(args[(int)0], arities) + (",\u0002" + (emit_expr(args[(int)1], arities) + ")")))) : ((n == "\u000E\u000Dx\u000E-\u0015\u000D\u001F\u0017\u000F\u0018\u000D") ? (emit_expr(args[(int)0], arities) + (".R\u000D\u001F\u0017\u000F\u0018\u000D(" + (emit_expr(args[(int)1], arities) + (",\u0002" + (emit_expr(args[(int)2], arities) + ")"))))) : ((n == "\u0010\u001F\u000D\u0012-\u001C\u0011\u0017\u000D") ? ("F\u0011\u0017\u000D.O\u001F\u000D\u0012R\u000D\u000F\u0016(" + (emit_expr(args[(int)0], arities) + ")")) : ((n == "\u0015\u000D\u000F\u0016-\u000F\u0017\u0017") ? ("\u0012\u000D\u001B\u0002S\u001E\u0013\u000E\u000D\u001A.IO.S\u000E\u0015\u000D\u000F\u001AR\u000D\u000F\u0016\u000D\u0015(" + (emit_expr(args[(int)0], arities) + ").R\u000D\u000F\u0016T\u0010E\u0012\u0016()")) : ((n == "\u0018\u0017\u0010\u0013\u000D-\u001C\u0011\u0017\u000D") ? (emit_expr(args[(int)0], arities) + ".D\u0011\u0013\u001F\u0010\u0013\u000D()") : ((n == "\u0015\u000D\u000F\u0016-\u0017\u0011\u0012\u000D") ? "_C\u0018\u000D.F\u0015\u0010\u001AU\u0012\u0011\u0018\u0010\u0016\u000D(C\u0010\u0012\u0013\u0010\u0017\u000D.R\u000D\u000F\u0016L\u0011\u0012\u000D()\u0002??\u0002"")" : ((n == "\u0015\u000D\u000F\u0016-\u001C\u0011\u0017\u000D") ? ("_C\u0018\u000D.F\u0015\u0010\u001AU\u0012\u0011\u0018\u0010\u0016\u000D(F\u0011\u0017\u000D.R\u000D\u000F\u0016A\u0017\u0017T\u000Dx\u000E(_C\u0018\u000D.T\u0010U\u0012\u0011\u0018\u0010\u0016\u000D(" + (emit_expr(args[(int)0], arities) + ")))")) : ((n == "\u001B\u0015\u0011\u000E\u000D-\u001C\u0011\u0017\u000D") ? ("F\u0011\u0017\u000D.W\u0015\u0011\u000E\u000DA\u0017\u0017T\u000Dx\u000E(_C\u0018\u000D.T\u0010U\u0012\u0011\u0018\u0010\u0016\u000D(" + (emit_expr(args[(int)0], arities) + ("),\u0002_C\u0018\u000D.T\u0010U\u0012\u0011\u0018\u0010\u0016\u000D(" + (emit_expr(args[(int)1], arities) + "))")))) : ((n == "\u001C\u0011\u0017\u000D-\u000Dx\u0011\u0013\u000E\u0013") ? ("F\u0011\u0017\u000D.Ex\u0011\u0013\u000E\u0013(_C\u0018\u000D.T\u0010U\u0012\u0011\u0018\u0010\u0016\u000D(" + (emit_expr(args[(int)0], arities) + "))")) : ((n == "\u0017\u0011\u0013\u000E-\u001C\u0011\u0017\u000D\u0013") ? ("D\u0011\u0015\u000D\u0018\u000E\u0010\u0015\u001E.G\u000D\u000EF\u0011\u0017\u000D\u0013(_C\u0018\u000D.T\u0010U\u0012\u0011\u0018\u0010\u0016\u000D(" + (emit_expr(args[(int)0], arities) + ("),\u0002_C\u0018\u000D.T\u0010U\u0012\u0011\u0018\u0010\u0016\u000D(" + (emit_expr(args[(int)1], arities) + ")).S\u000D\u0017\u000D\u0018\u000E(_C\u0018\u000D.F\u0015\u0010\u001AU\u0012\u0011\u0018\u0010\u0016\u000D).T\u0010L\u0011\u0013\u000E()")))) : ((n == "\u000E\u000Dx\u000E-\u0018\u0010\u0012\u0018\u000F\u000E-\u0017\u0011\u0013\u000E") ? ("\u0013\u000E\u0015\u0011\u0012\u001D.C\u0010\u0012\u0018\u000F\u000E(" + (emit_expr(args[(int)0], arities) + ")")) : ((n == "\u000E\u000Dx\u000E-\u0013\u001F\u0017\u0011\u000E") ? ("\u0012\u000D\u001B\u0002L\u0011\u0013\u000E<\u0013\u000E\u0015\u0011\u0012\u001D>(" + (emit_expr(args[(int)0], arities) + (".S\u001F\u0017\u0011\u000E(" + (emit_expr(args[(int)1], arities) + "))")))) : ((n == "\u000E\u000Dx\u000E-\u0018\u0010\u0012\u000E\u000F\u0011\u0012\u0013") ? (emit_expr(args[(int)0], arities) + (".C\u0010\u0012\u000E\u000F\u0011\u0012\u0013(" + (emit_expr(args[(int)1], arities) + ")"))) : ((n == "\u000E\u000Dx\u000E-\u0013\u000E\u000F\u0015\u000E\u0013-\u001B\u0011\u000E\u0014") ? (emit_expr(args[(int)0], arities) + (".S\u000E\u000F\u0015\u000E\u0013W\u0011\u000E\u0014(" + (emit_expr(args[(int)1], arities) + ")"))) : ((n == "\u001D\u000D\u000E-\u000F\u0015\u001D\u0013") ? "E\u0012v\u0011\u0015\u0010\u0012\u001A\u000D\u0012\u000E.G\u000D\u000EC\u0010\u001A\u001A\u000F\u0012\u0016L\u0011\u0012\u000DA\u0015\u001D\u0013().S\u000D\u0017\u000D\u0018\u000E(_C\u0018\u000D.F\u0015\u0010\u001AU\u0012\u0011\u0018\u0010\u0016\u000D).T\u0010L\u0011\u0013\u000E()" : ((n == "\u001D\u000D\u000E-\u000D\u0012v") ? ("_C\u0018\u000D.F\u0015\u0010\u001AU\u0012\u0011\u0018\u0010\u0016\u000D(E\u0012v\u0011\u0015\u0010\u0012\u001A\u000D\u0012\u000E.G\u000D\u000EE\u0012v\u0011\u0015\u0010\u0012\u001A\u000D\u0012\u000EV\u000F\u0015\u0011\u000Fb\u0017\u000D(_C\u0018\u000D.T\u0010U\u0012\u0011\u0018\u0010\u0016\u000D(" + (emit_expr(args[(int)0], arities) + "))\u0002??\u0002"")")) : ((n == "\u0015\u0019\u0012-\u001F\u0015\u0010\u0018\u000D\u0013\u0013") ? ("_C\u0018\u000D.F\u0015\u0010\u001AU\u0012\u0011\u0018\u0010\u0016\u000D(((F\u0019\u0012\u0018<\u0013\u000E\u0015\u0011\u0012\u001D>)(()\u0002=>\u0002{\u0002v\u000F\u0015\u0002_\u001F\u0013\u0011\u0002=\u0002\u0012\u000D\u001B\u0002S\u001E\u0013\u000E\u000D\u001A.D\u0011\u000F\u001D\u0012\u0010\u0013\u000E\u0011\u0018\u0013.P\u0015\u0010\u0018\u000D\u0013\u0013S\u000E\u000F\u0015\u000EI\u0012\u001C\u0010(_C\u0018\u000D.T\u0010U\u0012\u0011\u0018\u0010\u0016\u000D(" + (emit_expr(args[(int)0], arities) + ("),\u0002_C\u0018\u000D.T\u0010U\u0012\u0011\u0018\u0010\u0016\u000D(" + (emit_expr(args[(int)1], arities) + "))\u0002{\u0002R\u000D\u0016\u0011\u0015\u000D\u0018\u000ES\u000E\u000F\u0012\u0016\u000F\u0015\u0016O\u0019\u000E\u001F\u0019\u000E\u0002=\u0002\u000E\u0015\u0019\u000D,\u0002U\u0013\u000DS\u0014\u000D\u0017\u0017Ex\u000D\u0018\u0019\u000E\u000D\u0002=\u0002\u001C\u000F\u0017\u0013\u000D\u0002};\u0002v\u000F\u0015\u0002_\u001F\u0002=\u0002S\u001E\u0013\u000E\u000D\u001A.D\u0011\u000F\u001D\u0012\u0010\u0013\u000E\u0011\u0018\u0013.P\u0015\u0010\u0018\u000D\u0013\u0013.S\u000E\u000F\u0015\u000E(_\u001F\u0013\u0011)!;\u0002v\u000F\u0015\u0002_\u0010\u0002=\u0002_\u001F.S\u000E\u000F\u0012\u0016\u000F\u0015\u0016O\u0019\u000E\u001F\u0019\u000E.R\u000D\u000F\u0016T\u0010E\u0012\u0016();\u0002_\u001F.W\u000F\u0011\u000EF\u0010\u0015Ex\u0011\u000E();\u0002\u0015\u000D\u000E\u0019\u0015\u0012\u0002_\u0010;\u0002}))()")))) : ((n == "\u0018\u0019\u0015\u0015\u000D\u0012\u000E-\u0016\u0011\u0015") ? "_C\u0018\u000D.F\u0015\u0010\u001AU\u0012\u0011\u0018\u0010\u0016\u000D(D\u0011\u0015\u000D\u0018\u000E\u0010\u0015\u001E.G\u000D\u000EC\u0019\u0015\u0015\u000D\u0012\u000ED\u0011\u0015\u000D\u0018\u000E\u0010\u0015\u001E())" : ((n == "\u001C\u0010\u0015\"") ? ("T\u000F\u0013\".R\u0019\u0012(()\u0002=>\u0002(" + (emit_expr(args[(int)0], arities) + ")(\u0012\u0019\u0017\u0017))")) : ((n == "\u000F\u001B\u000F\u0011\u000E") ? ("(" + (emit_expr(args[(int)0], arities) + ").R\u000D\u0013\u0019\u0017\u000E")) : ((n == "\u001F\u000F\u0015") ? ("T\u000F\u0013\".W\u0014\u000D\u0012A\u0017\u0017(" + (emit_expr(args[(int)1], arities) + (".S\u000D\u0017\u000D\u0018\u000E(_x_\u0002=>\u0002T\u000F\u0013\".R\u0019\u0012(()\u0002=>\u0002(" + (emit_expr(args[(int)0], arities) + ")(_x_)))).R\u000D\u0013\u0019\u0017\u000E.T\u0010L\u0011\u0013\u000E()")))) : ((n == "\u0015\u000F\u0018\u000D") ? ("T\u000F\u0013\".W\u0014\u000D\u0012A\u0012\u001E(" + (emit_expr(args[(int)0], arities) + ".S\u000D\u0017\u000D\u0018\u000E(_\u000E_\u0002=>\u0002T\u000F\u0013\".R\u0019\u0012(()\u0002=>\u0002_\u000E_(\u0012\u0019\u0017\u0017)))).R\u000D\u0013\u0019\u0017\u000E.R\u000D\u0013\u0019\u0017\u000E")) : "")))))))))))))))))))))))))))))))))))))))));

    public static string emit_apply(IRExpr e, List<ArityEntry> arities) => ((Func<ApplyChain, string>)((chain) => ((Func<IRExpr, string>)((root) => ((Func<List<IRExpr>, string>)((args) => root switch { IrName(var n, var ty) => (is_builtin_name(n) ? emit_builtin(n, args, arities) : (((((long)n.Length) > 0) && is_upper_letter(((long)n[(int)0]))) ? ((Func<CodexType, string>)((result_ty) => ((Func<string, string>)((ctor_type_args) => ("\u0012\u000D\u001B\u0002" + (sanitize(n) + (ctor_type_args + ("(" + (emit_apply_args(args, arities, 0) + ")")))))))(extract_ctor_type_args(result_ty))))(ir_expr_type(e())) : ((Func<long, string>)((ar) => (((ar > 1) && (((long)args.Count) == ar)) ? (sanitize(n) + ("(" + (emit_apply_args(args, arities, 0) + ")"))) : (((ar > 1) && (((long)args.Count) < ar)) ? ((Func<long, string>)((remaining) => (emit_partial_wrappers(0, remaining) + (sanitize(n) + ("(" + (emit_apply_args(args, arities, 0) + (",\u0002" + (emit_partial_params(0, remaining) + ")"))))))))((ar - ((long)args.Count))) : emit_expr_curried(e(), arities)))))(lookup_arity(arities, n)))), _ => emit_expr_curried(e(), arities), }))(chain.args)))(chain.root)))(collect_apply_chain(e(), new List<IRExpr>()));

    public static string emit_expr_curried(IRExpr e, List<ArityEntry> arities) => e() switch { IrApply(var f, var a, var ty) => (emit_expr(f, arities) + ("(" + (emit_expr(a, arities) + ")"))), _ => emit_expr(e(), arities), };

    public static string emit_expr(IRExpr e, List<ArityEntry> arities) => e() switch { IrIntLit(var n) => _Cce.FromUnicode(n.ToString()), IrNumLit(var n) => _Cce.FromUnicode(n.ToString()), IrTextLit(var s) => (""" + (escape_text(s) + """)), IrBoolLit(var b) => (b() ? "\u000E\u0015\u0019\u000D" : "\u001C\u000F\u0017\u0013\u000D"), IrCharLit(var n) => _Cce.FromUnicode(n.ToString()), IrName(var n, var ty) => ((n == "\u0015\u000D\u000F\u0016-\u0017\u0011\u0012\u000D") ? "_C\u0018\u000D.F\u0015\u0010\u001AU\u0012\u0011\u0018\u0010\u0016\u000D(C\u0010\u0012\u0013\u0010\u0017\u000D.R\u000D\u000F\u0016L\u0011\u0012\u000D()\u0002??\u0002"")" : ((n == "\u001D\u000D\u000E-\u000F\u0015\u001D\u0013") ? "E\u0012v\u0011\u0015\u0010\u0012\u001A\u000D\u0012\u000E.G\u000D\u000EC\u0010\u001A\u001A\u000F\u0012\u0016L\u0011\u0012\u000DA\u0015\u001D\u0013().S\u000D\u0017\u000D\u0018\u000E(_C\u0018\u000D.F\u0015\u0010\u001AU\u0012\u0011\u0018\u0010\u0016\u000D).T\u0010L\u0011\u0013\u000E()" : ((n == "\u0018\u0019\u0015\u0015\u000D\u0012\u000E-\u0016\u0011\u0015") ? "_C\u0018\u000D.F\u0015\u0010\u001AU\u0012\u0011\u0018\u0010\u0016\u000D(D\u0011\u0015\u000D\u0018\u000E\u0010\u0015\u001E.G\u000D\u000EC\u0019\u0015\u0015\u000D\u0012\u000ED\u0011\u0015\u000D\u0018\u000E\u0010\u0015\u001E())" : (((((long)n.Length) > 0) && is_upper_letter(((long)n[(int)0]))) ? ("\u0012\u000D\u001B\u0002" + (sanitize(n) + "()")) : ((lookup_arity(arities, n) == 0) ? (sanitize(n) + "()") : ((Func<long, string>)((ar) => ((ar >= 2) ? (emit_partial_wrappers(0, ar) + (sanitize(n) + ("(" + (emit_partial_params(0, ar) + ")")))) : sanitize(n))))(lookup_arity(arities, n))))))), IrBinary(var op, var l, var r, var ty) => emit_binary(op, l, r, ty(), arities), IrNegate(var operand) => ("(-" + (emit_expr(operand, arities) + ")")), IrIf(var c, var t, var el, var ty) => ("(" + (emit_expr(c, arities) + ("\u0002?\u0002" + (emit_expr(t, arities) + ("\u0002:\u0002" + (emit_expr(el, arities) + ")")))))), IrLet(var name, var ty, var val, var body) => emit_let(name(), ty(), val, body(), arities), IrApply(var f, var a, var ty) => emit_apply(e(), arities), IrLambda(var @params, var body, var ty) => emit_lambda(@params, body(), arities), IrList(var elems, var ty) => emit_list(elems, ty(), arities), IrMatch(var scrut, var branches, var ty) => emit_match(scrut, branches, ty(), arities), IrDo(var stmts, var ty) => emit_do(stmts, ty(), arities), IrHandle(var eff, var body, var clauses, var ty) => emit_handle(eff, body(), clauses, ty(), arities), IrRecord(var name, var fields, var ty) => emit_record(name(), fields, arities), IrFieldAccess(var rec, var field, var ty) => (emit_expr(rec, arities) + ("." + sanitize(field()))), IrFork(var body, var ty) => ("T\u000F\u0013\".R\u0019\u0012(()\u0002=>\u0002(" + (emit_expr(body(), arities) + ")(\u0012\u0019\u0017\u0017))")), IrAwait(var task, var ty) => ("(" + (emit_expr(task, arities) + ").R\u000D\u0013\u0019\u0017\u000E")), IrError(var msg, var ty) => ("/*\u0002\u000D\u0015\u0015\u0010\u0015:\u0002" + (msg + "\u0002*/\u0002\u0016\u000D\u001C\u000F\u0019\u0017\u000E")), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static string hex_digit(long n) => ((n == 0) ? "\u0003" : ((n == 1) ? "\u0004" : ((n == 2) ? "\u0005" : ((n == 3) ? "\u0006" : ((n == 4) ? "\u0007" : ((n == 5) ? "\u0008" : ((n == 6) ? "\u0009" : ((n == 7) ? "\u000A" : ((n == 8) ? "\u000B" : ((n == 9) ? "\u000C" : ((n == 10) ? "A" : ((n == 11) ? "B" : ((n == 12) ? "C" : ((n == 13) ? "D" : ((n == 14) ? "E" : ((n == 15) ? "F" : "?"))))))))))))))));

    public static string hex4(long n) => ("\u0003\u0003" + (hex_digit((n / 16)) + hex_digit((n - ((n / 16) * 16)))));

    public static string escape_cce_char(long c) => ((c == 92) ? "\\" : ((c == 34) ? "\"" : ((c >= 32) ? ((c < 127) ? ((char)c).ToString() : ("\\u0019" + hex4(c))) : ("\\u0019" + hex4(c)))));

    public static List<string> escape_text_loop(string s, long i, long len, List<string> acc)
    {
        while (true)
        {
            if ((i() == len))
            {
            return acc();
            }
            else
            {
            var _tco_0 = s;
            var _tco_1 = (i() + 1);
            var _tco_2 = len;
            var _tco_3 = ((Func<List<string>>)(() => { var _l = acc(); _l.Add(escape_cce_char(((long)s[(int)i()]))); return _l; }))();
            s = _tco_0;
            i = _tco_1;
            len = _tco_2;
            acc = _tco_3;
            continue;
            }
        }
    }

    public static string escape_text(string s) => string.Concat(escape_text_loop(s, 0, ((long)s.Length), new List<string>()));

    public static string emit_bin_op(IRBinaryOp op) => op switch { IrAddInt { } => "+", IrSubInt { } => "-", IrMulInt { } => "*", IrDivInt { } => "/", IrPowInt { } => "\u00E0\u0081\u009E", IrAddNum { } => "+", IrSubNum { } => "-", IrMulNum { } => "*", IrDivNum { } => "/", IrEq { } => "==", IrNotEq { } => "!=", IrLt { } => "<", IrGt { } => ">", IrLtEq { } => "<=", IrGtEq { } => ">=", IrAnd { } => "&&", IrOr { } => "||", IrAppendText { } => "+", IrAppendList { } => "+", IrConsList { } => "+", _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static string emit_binary(IRBinaryOp op, IRExpr l, IRExpr r, CodexType ty, List<ArityEntry> arities) => op switch { IrAppendList { } => ("E\u0012\u0019\u001A\u000D\u0015\u000Fb\u0017\u000D.C\u0010\u0012\u0018\u000F\u000E(" + (emit_expr(l, arities) + (",\u0002" + (emit_expr(r, arities) + ").T\u0010L\u0011\u0013\u000E()")))), IrConsList { } => ("\u0012\u000D\u001B\u0002L\u0011\u0013\u000E<" + (cs_type(ir_expr_type(l)) + (">\u0002{\u0002" + (emit_expr(l, arities) + ("\u0002}.C\u0010\u0012\u0018\u000F\u000E(" + (emit_expr(r, arities) + ").T\u0010L\u0011\u0013\u000E()")))))), _ => ("(" + (emit_expr(l, arities) + ("\u0002" + (emit_bin_op(op) + ("\u0002" + (emit_expr(r, arities) + ")")))))), };

    public static string emit_let(string name, CodexType ty, IRExpr val, IRExpr body, List<ArityEntry> arities) => ("((F\u0019\u0012\u0018<" + (cs_type(ty()) + (",\u0002" + (cs_type(ir_expr_type(body())) + (">)((" + (sanitize(name()) + (")\u0002=>\u0002" + (emit_expr(body(), arities) + ("))(" + (emit_expr(val, arities) + ")"))))))))));

    public static string emit_lambda(List<IRParam> @params, IRExpr body, List<ArityEntry> arities) => ((((long)@params.Count) == 0) ? ("(()\u0002=>\u0002" + (emit_expr(body(), arities) + ")")) : ((((long)@params.Count) == 1) ? ((Func<IRParam, string>)((p) => ("((" + (cs_type(p.type_val) + ("\u0002" + (sanitize(p.name) + (")\u0002=>\u0002" + (emit_expr(body(), arities) + ")"))))))))(@params[(int)0]) : ("(()\u0002=>\u0002" + (emit_expr(body(), arities) + ")"))));

    public static string emit_list(List<IRExpr> elems, CodexType ty, List<ArityEntry> arities) => ((((long)elems.Count) == 0) ? ("\u0012\u000D\u001B\u0002L\u0011\u0013\u000E<" + (cs_type(ty()) + ">()")) : ("\u0012\u000D\u001B\u0002L\u0011\u0013\u000E<" + (cs_type(ty()) + (">\u0002{\u0002" + (emit_list_elems(elems, 0, arities) + "\u0002}")))));

    public static string emit_list_elems(List<IRExpr> elems, long i, List<ArityEntry> arities) => ((i() == ((long)elems.Count)) ? "" : ((i() == (((long)elems.Count) - 1)) ? emit_expr(elems[(int)i()], arities) : (emit_expr(elems[(int)i()], arities) + (",\u0002" + emit_list_elems(elems, (i() + 1), arities)))));

    public static string emit_match(IRExpr scrut, List<IRBranch> branches, CodexType ty, List<ArityEntry> arities) => ((Func<string, string>)((arms) => ((Func<bool, string>)((needs_wild) => (emit_expr(scrut, arities) + ("\u0002\u0013\u001B\u0011\u000E\u0018\u0014\u0002{\u0002" + (arms + ((needs_wild ? "_\u0002=>\u0002\u000E\u0014\u0015\u0010\u001B\u0002\u0012\u000D\u001B\u0002I\u0012v\u000F\u0017\u0011\u0016O\u001F\u000D\u0015\u000F\u000E\u0011\u0010\u0012Ex\u0018\u000D\u001F\u000E\u0011\u0010\u0012("N\u0010\u0012-\u000Dx\u0014\u000F\u0019\u0013\u000E\u0011v\u000D\u0002\u001A\u000F\u000E\u0018\u0014"),\u0002" : "") + "}"))))))((has_any_catch_all(branches, 0) ? false : true))))(emit_match_arms(branches, 0, arities));

    public static string emit_match_arms(List<IRBranch> branches, long i, List<ArityEntry> arities) => ((i() == ((long)branches.Count)) ? "" : ((Func<IRBranch, string>)((arm) => ((Func<string, string>)((this_arm) => (is_catch_all(arm.pattern) ? this_arm : (this_arm + emit_match_arms(branches, (i() + 1), arities)))))((emit_pattern(arm.pattern) + ("\u0002=>\u0002" + (emit_expr(arm.body, arities) + ",\u0002"))))))(branches[(int)i()]));

    public static bool is_catch_all(IRPat p) => p switch { IrWildPat { } => true, IrVarPat(var name, var ty) => true, _ => false, };

    public static bool has_any_catch_all(List<IRBranch> branches, long i)
    {
        while (true)
        {
            if ((i() == ((long)branches.Count)))
            {
            return false;
            }
            else
            {
            var b = branches[(int)i()];
            if (is_catch_all(b().pattern))
            {
            return true;
            }
            else
            {
            var _tco_0 = branches;
            var _tco_1 = (i() + 1);
            branches = _tco_0;
            i = _tco_1;
            continue;
            }
            }
        }
    }

    public static string emit_pattern(IRPat p) => p switch { IrVarPat(var name, var ty) => (cs_type(ty()) + ("\u0002" + sanitize(name()))), IrLitPat(var text, var ty) => text(), IrCtorPat(var name, var subs, var ty) => ((((long)subs.Count) == 0) ? (sanitize(name()) + "\u0002{\u0002}") : (sanitize(name()) + ("(" + (emit_sub_patterns(subs, 0) + ")")))), IrWildPat { } => "_", _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static string emit_sub_patterns(List<IRPat> subs, long i) => ((i() == ((long)subs.Count)) ? "" : ((Func<IRPat, string>)((sub) => (emit_sub_pattern(sub) + (((i() < (((long)subs.Count) - 1)) ? ",\u0002" : "") + emit_sub_patterns(subs, (i() + 1))))))(subs[(int)i()]));

    public static string emit_sub_pattern(IRPat p) => p switch { IrVarPat(var name, var ty) => ("v\u000F\u0015\u0002" + sanitize(name())), IrCtorPat(var name, var subs, var ty) => emit_pattern(p), IrWildPat { } => "_", IrLitPat(var text, var ty) => text(), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static string emit_do(List<IRDoStmt> stmts, CodexType ty, List<ArityEntry> arities) => ((Func<string, string>)((ret_type) => ((Func<long, string>)((len) => ty() switch { VoidTy { } => ("((F\u0019\u0012\u0018<\u0010bj\u000D\u0018\u000E>)(()\u0002=>\u0002{\u0002" + (emit_do_stmts(stmts, 0, len, false, arities) + "\u0002\u0015\u000D\u000E\u0019\u0015\u0012\u0002\u0012\u0019\u0017\u0017;\u0002}))()")), NothingTy { } => ("((F\u0019\u0012\u0018<\u0010bj\u000D\u0018\u000E>)(()\u0002=>\u0002{\u0002" + (emit_do_stmts(stmts, 0, len, false, arities) + "\u0002\u0015\u000D\u000E\u0019\u0015\u0012\u0002\u0012\u0019\u0017\u0017;\u0002}))()")), object ErrorTy => ("((F\u0019\u0012\u0018<\u0010bj\u000D\u0018\u000E>)(()\u0002=>\u0002{\u0002" + (emit_do_stmts(stmts, 0, len, false, arities) + "\u0002\u0015\u000D\u000E\u0019\u0015\u0012\u0002\u0012\u0019\u0017\u0017;\u0002}))()")), }))(((long)stmts.Count))))(cs_type(ty()));

    public static string emit_do_stmts(List<IRDoStmt> stmts, long i, long len, bool needs_return, List<ArityEntry> arities) => ((i() == len) ? "" : ((Func<IRDoStmt, string>)((s) => ((Func<bool, string>)((is_last) => ((Func<bool, string>)((use_return) => (emit_do_stmt(s, use_return, arities) + ("\u0002" + emit_do_stmts(stmts, (i() + 1), len, needs_return, arities)))))((is_last ? needs_return : false))))((i() == (len - 1)))))(stmts[(int)i()]));

    public static string emit_do_stmt(IRDoStmt s, bool use_return, List<ArityEntry> arities) => s switch { IrDoBind(var name, var ty, var val) => ("v\u000F\u0015\u0002" + (sanitize(name()) + ("\u0002=\u0002" + (emit_expr(val, arities) + ";")))), IrDoExec(var e) => (use_return ? ("\u0015\u000D\u000E\u0019\u0015\u0012\u0002" + (emit_expr(e(), arities) + ";")) : (emit_expr(e(), arities) + ";")), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static string emit_record(string name, List<IRFieldVal> fields, List<ArityEntry> arities) => ("\u0012\u000D\u001B\u0002" + (sanitize(name()) + ("(" + (emit_record_fields(fields, 0, arities) + ")"))));

    public static string emit_record_fields(List<IRFieldVal> fields, long i, List<ArityEntry> arities) => ((i() == ((long)fields.Count)) ? "" : ((Func<IRFieldVal, string>)((f) => (sanitize(f.name) + (":\u0002" + (emit_expr(f.value, arities) + (((i() < (((long)fields.Count) - 1)) ? ",\u0002" : "") + emit_record_fields(fields, (i() + 1), arities)))))))(fields[(int)i()]));

    public static string emit_handle(string eff, IRExpr body, List<IRHandleClause> clauses, CodexType ty, List<ArityEntry> arities) => ((Func<string, string>)((ret_type) => ("((F\u0019\u0012\u0018<" + (ret_type + (">)(()\u0002=>\u0002{\u0002" + (emit_handle_clauses(clauses, ret_type, arities) + ("\u0015\u000D\u000E\u0019\u0015\u0012\u0002" + (emit_expr(body(), arities) + ";\u0002}))()"))))))))(cs_type(ty()));

    public static string emit_handle_clauses(List<IRHandleClause> clauses, string ret_type, List<ArityEntry> arities) => emit_handle_clauses_loop(clauses, 0, ret_type, arities);

    public static string emit_handle_clauses_loop(List<IRHandleClause> clauses, long i, string ret_type, List<ArityEntry> arities) => ((i() == ((long)clauses.Count)) ? "" : ((Func<IRHandleClause, string>)((c) => ("F\u0019\u0012\u0018<F\u0019\u0012\u0018<" + (ret_type + (",\u0002" + (ret_type + (">,\u0002" + (ret_type + (">\u0002_\u0014\u000F\u0012\u0016\u0017\u000D_" + (sanitize(c.op_name) + ("_\u0002=\u0002(" + (sanitize(c.resume_name) + (")\u0002=>\u0002{\u0002\u0015\u000D\u000E\u0019\u0015\u0012\u0002" + (emit_expr(c.body, arities) + (";\u0002};\u0002" + emit_handle_clauses_loop(clauses, (i() + 1), ret_type, arities))))))))))))))))(clauses[(int)i()]));

    public static string codex_emit_type_defs(List<ATypeDef> tds, long i) => ((i() == ((long)tds.Count)) ? "" : (codex_emit_type_def(tds[(int)i()]) + ("\u0001" + codex_emit_type_defs(tds, (i() + 1)))));

    public static string codex_emit_type_def(ATypeDef td) => td switch { ARecordTypeDef(var name, var tparams, var fields) => (name().value + ("\u0002=\u0002\u0015\u000D\u0018\u0010\u0015\u0016\u0002{" + (codex_emit_record_field_defs(fields, 0) + "\u0001}\u0001"))), AVariantTypeDef(var name, var tparams, var ctors) => (name().value + ("\u0002=\u0001" + codex_emit_variant_ctors(ctors, 0))), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static string codex_emit_record_field_defs(List<ARecordFieldDef> fields, long i) => ((i() == ((long)fields.Count)) ? "" : ((Func<ARecordFieldDef, string>)((f) => ((Func<string, string>)((comma) => (comma + ("\u0001\u0002\u0002" + (f.name.value + ("\u0002:\u0002" + (codex_emit_type_expr(f.type_expr) + codex_emit_record_field_defs(fields, (i() + 1)))))))))(((i() > 0) ? "," : ""))))(fields[(int)i()]));

    public static string codex_emit_variant_ctors(List<AVariantCtorDef> ctors, long i) => ((i() == ((long)ctors.Count)) ? "" : ((Func<AVariantCtorDef, string>)((c) => ("\u0002\u0002|\u0002" + (c.name.value + (codex_emit_ctor_fields(c.fields, 0) + ("\u0001" + codex_emit_variant_ctors(ctors, (i() + 1))))))))(ctors[(int)i()]));

    public static string codex_emit_ctor_fields(List<ATypeExpr> fields, long i) => ((i() == ((long)fields.Count)) ? "" : ("\u0002(" + (codex_emit_type_expr(fields[(int)i()]) + (")" + codex_emit_ctor_fields(fields, (i() + 1))))));

    public static string codex_emit_type_expr(ATypeExpr te) => te switch { ANamedType(var name) => name().value, AFunType(var p, var r) => ("(" + (codex_emit_type_expr(p) + ("\u0002->\u0002" + (codex_emit_type_expr(r) + ")")))), AAppType(var @base, var args) => (codex_emit_type_expr(@base) + ("\u0002" + codex_emit_type_expr_args(args, 0))), AEffectType(var effs, var ret) => ("[" + (codex_emit_effect_names(effs, 0) + ("]\u0002" + codex_emit_type_expr(ret())))), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static string codex_emit_type_expr_args(List<ATypeExpr> args, long i) => ((i() == ((long)args.Count)) ? "" : ((Func<string, string>)((sep) => (sep + (codex_emit_type_expr_wrapped(args[(int)i()]) + codex_emit_type_expr_args(args, (i() + 1))))))(((i() > 0) ? "\u0002" : "")));

    public static string codex_emit_type_expr_wrapped(ATypeExpr te) => te switch { AFunType(var p, var r) => ("(" + (codex_emit_type_expr(te) + ")")), AAppType(var @base, var args) => ("(" + (codex_emit_type_expr(te) + ")")), _ => codex_emit_type_expr(te), };

    public static string codex_emit_effect_names(List<Name> effs, long i) => ((i() == ((long)effs.Count)) ? "" : ((Func<string, string>)((sep) => (sep + (effs[(int)i()].value + codex_emit_effect_names(effs, (i() + 1))))))(((i() > 0) ? ",\u0002" : "")));

    public static string codex_emit_codex_type(CodexType ty) => ty() switch { IntegerTy { } => "I\u0012\u000E\u000D\u001D\u000D\u0015", NumberTy { } => "N\u0019\u001Ab\u000D\u0015", object TextTy => "T\u000Dx\u000E", };

    public static string integer_to_text() => (new ForAllTy(id(), body()) ? codex_emit_codex_type(body()) : new SumTy(name(), ctors));

    public static object name() => value;

    public static string RecordTy(object name, object fields) => name().value;

    public static T793 ConstructedTy<T793>(object name, object args) => name().value;

    public static string EffectfulTy(object effs, object ret) => ("[" + (codex_emit_codex_type_effect_names(effs, 0) + ("]\u0002" + codex_emit_codex_type(ret()))));

    public static string codex_wrap_fun_param(CodexType ty) => ty() switch { FunTy(var p, var r) => ("(" + (codex_emit_codex_type(ty()) + ")")), _ => codex_emit_codex_type(ty()), };

    public static string codex_wrap_complex(CodexType ty) => ty() switch { FunTy(var p, var r) => ("(" + (codex_emit_codex_type(ty()) + ")")), ListTy(var elem) => ("(" + (codex_emit_codex_type(ty()) + ")")), _ => codex_emit_codex_type(ty()), };

    public static string codex_emit_codex_type_effect_names(List<Name> effs, long i) => ((i() == ((long)effs.Count)) ? "" : ((Func<string, string>)((sep) => (sep + (effs[(int)i()].value + codex_emit_codex_type_effect_names(effs, (i() + 1))))))(((i() > 0) ? ",\u0002" : "")));

    public static string codex_emit_def(IRDef d, List<string> ctor_names) => (codex_skip_def(d, ctor_names) ? "" : (d.name + ("\u0002:\u0002" + (codex_emit_codex_type(d.type_val) + ("\u0001" + (d.name + (codex_emit_def_params(d.@params, 0) + ("\u0002=\u0001\u0002\u0002" + (codex_emit_expr(d.body, ctor_names, 1) + "\u0001")))))))));

    public static bool codex_skip_def(IRDef d, List<string> ctor_names) => (list_contains(ctor_names, d.name) ? true : ((((long)d.name.Length) == 0) ? true : ((Func<long, bool>)((first) => ((Func<long, bool>)((code_a) => ((Func<long, bool>)((code_z) => (((first < code_a) || (first > code_z)) ? true : (codex_is_error_body(d.body) ? true : false))))(((long)"z"[(int)0]))))(((long)"\u000F"[(int)0]))))(((long)d.name[(int)0]))));

    public static bool codex_is_upper(long c) => ((Func<long, bool>)((code) => ((Func<long, bool>)((code_z) => ((c >= code) && (c <= code_z))))(((long)"Z"[(int)0]))))(((long)"A"[(int)0]));

    public static bool codex_is_error_body(IRExpr e) => e() switch { IrError(var msg, var ty) => true, _ => false, };

    public static bool codex_is_lower_start(string s) => ((((long)s.Length) == 0) ? false : ((Func<long, bool>)((c) => ((Func<long, bool>)((code_a) => ((Func<long, bool>)((code_z) => ((c >= code_a) && (c <= code_z))))(((long)"z"[(int)0]))))(((long)"\u000F"[(int)0]))))(((long)s[(int)0])));

    public static string codex_emit_def_params(List<IRParam> @params, long i) => ((i() == ((long)@params.Count)) ? "" : ((Func<IRParam, string>)((p) => ("\u0002(" + (p.name + (")" + codex_emit_def_params(@params, (i() + 1)))))))(@params[(int)i()]));

    public static string codex_emit_expr(IRExpr e, List<string> ctors, long indent) => e() switch { IrIntLit(var n) => _Cce.FromUnicode(n.ToString()), IrNumLit(var n) => _Cce.FromUnicode(n.ToString()), IrTextLit(var s) => (""" + (codex_escape_text(s) + """)), IrBoolLit(var b) => (b() ? "T\u0015\u0019\u000D" : "F\u000F\u0017\u0013\u000D"), IrCharLit(var n) => ("'" + (codex_escape_char(n) + "'")), IrName(var n, var ty) => n, IrBinary(var op, var l, var r, var ty) => codex_emit_binary(op, l, r, ctors, indent), IrNegate(var operand) => ("\u0003\u0002-\u0002" + codex_emit_expr(operand, ctors, indent)), IrIf(var c, var t, var el, var ty) => codex_emit_if(c, t, el, ctors, indent), IrLet(var name, var ty, var val, var body) => codex_emit_let(name(), val, body(), ctors, indent), IrApply(var f, var a, var ty) => codex_emit_apply(e(), ctors, indent), IrLambda(var @params, var body, var ty) => codex_emit_lambda(@params, body(), ctors, indent), IrList(var elems, var ty) => codex_emit_list(elems, ctors, indent), IrMatch(var scrut, var branches, var ty) => codex_emit_match(scrut, branches, ctors, indent), IrDo(var stmts, var ty) => codex_emit_do(stmts, ctors, indent), IrRecord(var name, var fields, var ty) => codex_emit_record(name(), fields, ctors, indent), IrFieldAccess(var rec, var field, var ty) => codex_emit_field_access(rec, field(), ctors, indent), IrFork(var body, var ty) => ("\u001C\u0010\u0015\"\u0002(" + (codex_emit_expr(body(), ctors, indent) + ")")), IrAwait(var task, var ty) => ("\u000F\u001B\u000F\u0011\u000E\u0002(" + (codex_emit_expr(task, ctors, indent) + ")")), IrHandle(var eff, var body, var clauses, var ty) => codex_emit_handle(eff, body(), clauses, ctors, indent), IrError(var msg, var ty) => "\u0003", _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static string codex_emit_binary(IRBinaryOp op, IRExpr l, IRExpr r, List<string> ctors, long indent) => (codex_emit_expr(l, ctors, indent) + ("\u0002" + (codex_bin_op_text(op) + ("\u0002" + codex_emit_expr(r, ctors, indent)))));

    public static string codex_bin_op_text(IRBinaryOp op) => op switch { IrAddInt { } => "+", IrSubInt { } => "-", IrMulInt { } => "*", IrDivInt { } => "/", IrPowInt { } => "\u00E0\u0081\u009E", IrAddNum { } => "+", IrSubNum { } => "-", IrMulNum { } => "*", IrDivNum { } => "/", IrEq { } => "==", IrNotEq { } => "/=", IrLt { } => "<", IrGt { } => ">", IrLtEq { } => "<=", IrGtEq { } => ">=", IrAnd { } => "&", IrOr { } => "|", IrAppendText { } => "++", IrAppendList { } => "++", IrConsList { } => "::", _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static string codex_emit_if(IRExpr c, IRExpr t, IRExpr el, List<string> ctors, long indent) => ((codex_is_simple(t) && codex_is_simple(el)) ? ("\u0011\u001C\u0002" + (codex_emit_expr(c, ctors, indent) + ("\u0002\u000E\u0014\u000D\u0012\u0002" + (codex_emit_expr(t, ctors, indent) + ("\u0002\u000D\u0017\u0013\u000D\u0002" + codex_emit_expr(el, ctors, indent)))))) : ("\u0011\u001C\u0002" + (codex_emit_expr(c, ctors, indent) + ("\u0001" + (codex_indent((indent + 1)) + ("\u000E\u0014\u000D\u0012\u0002" + (codex_emit_expr(t, ctors, (indent + 1)) + ("\u0001" + (codex_indent((indent + 1)) + ("\u000D\u0017\u0013\u000D\u0002" + codex_emit_expr(el, ctors, (indent + 1))))))))))));

    public static string codex_emit_let(string name, IRExpr val, IRExpr body, List<string> ctors, long indent) => ("\u0017\u000D\u000E\u0002" + (name() + ("\u0002=\u0002" + (codex_emit_expr(val, ctors, (indent + 1)) + ("\u0001" + (codex_indent(indent) + ("\u0011\u0012\u0002" + codex_emit_expr(body(), ctors, indent))))))));

    public static string codex_emit_apply(IRExpr e, List<string> ctors, long indent) => ((Func<ApplyChain, string>)((chain) => ((Func<IRExpr, string>)((func) => ((Func<List<IRExpr>, string>)((args) => (codex_emit_expr(func, ctors, indent) + codex_emit_apply_args(args, ctors, indent, 0, ((long)args.Count), codex_is_ctor_name(func, ctors)))))(chain.args)))(chain.root)))(collect_apply_chain(e(), new List<IRExpr>()));

    public static string codex_emit_apply_args(List<IRExpr> args, List<string> ctors, long indent, long i, long len, bool is_ctor) => ((i() == len) ? "" : ((Func<IRExpr, string>)((arg) => ("\u0002" + (codex_wrap_arg(arg, ctors, indent, is_ctor) + codex_emit_apply_args(args, ctors, indent, (i() + 1), len, is_ctor)))))(args[(int)i()]));

    public static string codex_wrap_arg(IRExpr e, List<string> ctors, long indent, bool is_ctor) => (codex_needs_parens(e(), is_ctor) ? ("(" + (codex_emit_expr(e(), ctors, indent) + ")")) : codex_emit_expr(e(), ctors, indent));

    public static bool codex_is_ctor_name(IRExpr e, List<string> ctors) => e() switch { IrName(var n, var ty) => list_contains(ctors, n), _ => false, };

    public static bool codex_needs_parens(IRExpr e, bool is_ctor) => e() switch { IrApply(var f, var a, var ty) => true, IrBinary(var op, var l, var r, var ty) => true, IrIf(var c, var t, var el, var ty) => true, IrLet(var name, var ty, var val, var body) => true, IrMatch(var scrut, var branches, var ty) => true, IrNegate(var operand) => true, IrLambda(var @params, var body, var ty) => true, _ => false, };

    public static string codex_emit_lambda(List<IRParam> @params, IRExpr body, List<string> ctors, long indent) => ("\" + (codex_emit_lambda_params(@params, 0) + ("\u0002->\u0002" + codex_emit_expr(body(), ctors, indent))));

    public static string codex_emit_lambda_params(List<IRParam> @params, long i) => ((i() == ((long)@params.Count)) ? "" : ((Func<IRParam, string>)((p) => ((Func<string, string>)((sep) => (sep + (p.name + codex_emit_lambda_params(@params, (i() + 1))))))(((i() > 0) ? "\u0002" : ""))))(@params[(int)i()]));

    public static string codex_emit_list(List<IRExpr> elems, List<string> ctors, long indent) => ("[" + (codex_emit_list_elems(elems, ctors, indent, 0) + "]"));

    public static string codex_emit_list_elems(List<IRExpr> elems, List<string> ctors, long indent, long i) => ((i() == ((long)elems.Count)) ? "" : ((Func<string, string>)((sep) => (sep + (codex_emit_expr(elems[(int)i()], ctors, indent) + codex_emit_list_elems(elems, ctors, indent, (i() + 1))))))(((i() > 0) ? ",\u0002" : "")));

    public static string codex_emit_match(IRExpr scrut, List<IRBranch> branches, List<string> ctors, long indent) => ("\u001B\u0014\u000D\u0012\u0002" + (codex_emit_expr(scrut, ctors, indent) + codex_emit_branches(branches, ctors, indent, 0)));

    public static string codex_emit_branches(List<IRBranch> branches, List<string> ctors, long indent, long i) => ((i() == ((long)branches.Count)) ? "" : ((Func<IRBranch, string>)((b) => ("\u0001" + (codex_indent((indent + 1)) + ("\u0011\u001C\u0002" + (codex_emit_pattern(b().pattern) + ("\u0002->\u0002" + (codex_emit_expr(b().body, ctors, (indent + 1)) + codex_emit_branches(branches, ctors, indent, (i() + 1))))))))))(branches[(int)i()]));

    public static string codex_emit_pattern(IRPat p) => p switch { IrVarPat(var name, var ty) => name(), IrLitPat(var text, var ty) => text(), IrCtorPat(var name, var subs, var ty) => (name() + codex_emit_sub_patterns(subs, 0)), IrWildPat { } => "_", _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static string codex_emit_sub_patterns(List<IRPat> subs, long i) => ((i() == ((long)subs.Count)) ? "" : ("\u0002(" + (codex_emit_pattern(subs[(int)i()]) + (")" + codex_emit_sub_patterns(subs, (i() + 1))))));

    public static string codex_emit_do(List<IRDoStmt> stmts, List<string> ctors, long indent) => ("\u0016\u0010" + codex_emit_do_stmts(stmts, ctors, indent, 0));

    public static string codex_emit_do_stmts(List<IRDoStmt> stmts, List<string> ctors, long indent, long i) => ((i() == ((long)stmts.Count)) ? "" : ((Func<IRDoStmt, string>)((s) => ("\u0001" + (codex_indent((indent + 1)) + (codex_emit_do_stmt(s, ctors, (indent + 1)) + codex_emit_do_stmts(stmts, ctors, indent, (i() + 1)))))))(stmts[(int)i()]));

    public static string codex_emit_do_stmt(IRDoStmt s, List<string> ctors, long indent) => s switch { IrDoBind(var name, var ty, var val) => (name() + ("\u0002<-\u0002" + codex_emit_expr(val, ctors, indent))), IrDoExec(var e) => codex_emit_expr(e(), ctors, indent), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static string codex_emit_record(string name, List<IRFieldVal> fields, List<string> ctors, long indent) => (name() + ("\u0002{" + (codex_emit_record_fields(fields, ctors, indent, 0) + "\u0002}")));

    public static string codex_emit_record_fields(List<IRFieldVal> fields, List<string> ctors, long indent, long i) => ((i() == ((long)fields.Count)) ? "" : ((Func<IRFieldVal, string>)((f) => ((Func<string, string>)((sep) => (sep + ("\u0002" + (f.name + ("\u0002=\u0002" + (codex_emit_expr(f.value, ctors, indent) + codex_emit_record_fields(fields, ctors, indent, (i() + 1)))))))))(((i() > 0) ? "," : ""))))(fields[(int)i()]));

    public static string codex_emit_field_access(IRExpr rec, string field, List<string> ctors, long indent) => rec switch { IrName(var n, var ty) => (n + ("." + field())), IrFieldAccess(var r, var f, var ty) => (codex_emit_field_access(r, f, ctors, indent) + ("." + field())), _ => ("(" + (codex_emit_expr(rec, ctors, indent) + (")." + field()))), };

    public static string codex_emit_handle(string eff, IRExpr body, List<IRHandleClause> clauses, List<string> ctors, long indent) => ("\u0014\u000F\u0012\u0016\u0017\u000D\u0002" + (codex_emit_expr(body(), ctors, indent) + ("\u0002\u001B\u0011\u000E\u0014" + codex_emit_handle_clauses(clauses, ctors, indent, 0))));

    public static string codex_emit_handle_clauses(List<IRHandleClause> clauses, List<string> ctors, long indent, long i) => ((i() == ((long)clauses.Count)) ? "" : ((Func<IRHandleClause, string>)((c) => ("\u0001" + (codex_indent((indent + 1)) + (c.op_name + ("\u0002(" + (c.resume_name + (")\u0002->\u0002" + (codex_emit_expr(c.body, ctors, (indent + 1)) + codex_emit_handle_clauses(clauses, ctors, indent, (i() + 1)))))))))))(clauses[(int)i()]));

    public static List<string> codex_collect_ctor_names(List<ATypeDef> type_defs, long i) => ((i() == ((long)type_defs.Count)) ? new List<string>() : ((Func<ATypeDef, List<string>>)((td) => ((Func<List<string>, List<string>>)((names) => Enumerable.Concat(names, codex_collect_ctor_names(type_defs, (i() + 1))).ToList()))(td switch { AVariantTypeDef(var name, var tparams, var ctors) => codex_collect_variant_ctor_names(ctors, 0), ARecordTypeDef(var name, var tparams, var fields) => new List<string> { name().value }, _ => throw new InvalidOperationException("Non-exhaustive match"), })))(type_defs[(int)i()]));

    public static List<string> codex_collect_variant_ctor_names(List<AVariantCtorDef> ctors, long i) => ((i() == ((long)ctors.Count)) ? new List<string>() : ((Func<AVariantCtorDef, List<string>>)((c) => Enumerable.Concat(new List<string> { c.name.value }, codex_collect_variant_ctor_names(ctors, (i() + 1))).ToList()))(ctors[(int)i()]));

    public static bool codex_is_simple(IRExpr e) => e() switch { IrIntLit(var n) => true, IrNumLit(var n) => true, IrTextLit(var s) => true, IrBoolLit(var b) => true, IrCharLit(var n) => true, IrName(var n, var ty) => true, IrFieldAccess(var r, var f, var ty) => true, _ => false, };

    public static string codex_indent(long n) => ((n == 0) ? "" : ("\u0002\u0002" + codex_indent((n - 1))));

    public static string codex_escape_text(string s) => codex_escape_text_loop(s, 0, ((long)s.Length), "");

    public static string codex_escape_text_loop(string s, long i, long len, string acc)
    {
        while (true)
        {
            if ((i() == len))
            {
            return acc();
            }
            else
            {
            var c = ((long)s[(int)i()]);
            var _tco_0 = s;
            var _tco_1 = (i() + 1);
            var _tco_2 = len;
            var _tco_3 = (acc() + codex_escape_one_char(c));
            s = _tco_0;
            i = _tco_1;
            len = _tco_2;
            acc = _tco_3;
            continue;
            }
        }
    }

    public static string codex_escape_one_char(long c) => ((c == ((long)"\"[(int)0])) ? "\\" : ((c == ((long)"""[(int)0])) ? "\"" : ((c == ((long)"\u0001"[(int)0])) ? "\\u0012" : ((char)c).ToString())));

    public static string codex_escape_char(long c) => ((c == ((long)"\u0001"[(int)0])) ? "\\u0012" : ((c == ((long)"\"[(int)0])) ? "\\" : ((c == ((long)"'"[(int)0])) ? "\'" : ((char)c).ToString())));

    public static string codex_emit_full_module(IRModule m, List<ATypeDef> type_defs) => ((Func<List<string>, string>)((ctor_names) => (codex_emit_type_defs(type_defs, 0) + codex_emit_all_defs(m.defs, ctor_names, 0))))(codex_collect_ctor_names(type_defs, 0));

    public static string codex_emit_all_defs(List<IRDef> defs, List<string> ctor_names, long i) => ((i() == ((long)defs.Count)) ? "" : (codex_emit_def(defs[(int)i()], ctor_names) + ("\u0001" + codex_emit_all_defs(defs, ctor_names, (i() + 1)))));

    public static string codex_emit_def_list(List<IRDef> defs, List<string> ctor_names, long i) => ((i() == ((long)defs.Count)) ? "" : (codex_emit_def(defs[(int)i()], ctor_names) + ("\u0001" + codex_emit_def_list(defs, ctor_names, (i() + 1)))));

    public static List<IRDef> codex_filter_defs(List<IRDef> defs, List<string> ctor_names, long i, long len, List<IRDef> acc)
    {
        while (true)
        {
            if ((i() == len))
            {
            return acc();
            }
            else
            {
            var d = defs[(int)i()];
            if (codex_skip_def(d, ctor_names))
            {
            var _tco_0 = defs;
            var _tco_1 = ctor_names;
            var _tco_2 = (i() + 1);
            var _tco_3 = len;
            var _tco_4 = acc();
            defs = _tco_0;
            ctor_names = _tco_1;
            i = _tco_2;
            len = _tco_3;
            acc = _tco_4;
            continue;
            }
            else
            {
            if (codex_has_def_named(acc(), d.name, 0, ((long)acc().Count)))
            {
            if ((codex_def_score(d) > codex_def_score_named(acc(), d.name, 0, ((long)acc().Count))))
            {
            var _tco_0 = defs;
            var _tco_1 = ctor_names;
            var _tco_2 = (i() + 1);
            var _tco_3 = len;
            var _tco_4 = codex_replace_def(acc(), d.name, d, 0, ((long)acc().Count));
            defs = _tco_0;
            ctor_names = _tco_1;
            i = _tco_2;
            len = _tco_3;
            acc = _tco_4;
            continue;
            }
            else
            {
            var _tco_0 = defs;
            var _tco_1 = ctor_names;
            var _tco_2 = (i() + 1);
            var _tco_3 = len;
            var _tco_4 = acc();
            defs = _tco_0;
            ctor_names = _tco_1;
            i = _tco_2;
            len = _tco_3;
            acc = _tco_4;
            continue;
            }
            }
            else
            {
            var _tco_0 = defs;
            var _tco_1 = ctor_names;
            var _tco_2 = (i() + 1);
            var _tco_3 = len;
            var _tco_4 = ((Func<List<IRDef>>)(() => { var _l = acc(); _l.Add(d); return _l; }))();
            defs = _tco_0;
            ctor_names = _tco_1;
            i = _tco_2;
            len = _tco_3;
            acc = _tco_4;
            continue;
            }
            }
            }
        }
    }

    public static long codex_def_score(IRDef d) => ((((long)d.@params.Count) * 100) + codex_body_depth(d.body));

    public static long codex_def_score_named(List<IRDef> defs, string name, long i, long len)
    {
        while (true)
        {
            if ((i() == len))
            {
            return 0;
            }
            else
            {
            if ((defs[(int)i()].name == name()))
            {
            return codex_def_score(defs[(int)i()]);
            }
            else
            {
            var _tco_0 = defs;
            var _tco_1 = name();
            var _tco_2 = (i() + 1);
            var _tco_3 = len;
            defs = _tco_0;
            name = _tco_1;
            i = _tco_2;
            len = _tco_3;
            continue;
            }
            }
        }
    }

    public static long codex_body_depth(IRExpr e) => e() switch { IrName(var n, var ty) => 1, IrIntLit(var n) => 1, IrTextLit(var s) => 1, IrBoolLit(var b) => 1, IrFieldAccess(var r, var f, var ty) => 2, IrApply(var f, var a, var ty) => (3 + codex_body_depth(f)), IrLet(var name, var ty, var val, var body) => (5 + codex_body_depth(body())), IrIf(var c, var t, var el, var ty) => (5 + codex_body_depth(t)), IrMatch(var s, var bs, var ty) => 10, IrLambda(var ps, var b, var ty) => 5, IrDo(var stmts, var ty) => 5, IrRecord(var n, var fs, var ty) => 3, _ => 1, };

    public static bool codex_has_def_named(List<IRDef> defs, string name, long i, long len)
    {
        while (true)
        {
            if ((i() == len))
            {
            return false;
            }
            else
            {
            if ((defs[(int)i()].name == name()))
            {
            return true;
            }
            else
            {
            var _tco_0 = defs;
            var _tco_1 = name();
            var _tco_2 = (i() + 1);
            var _tco_3 = len;
            defs = _tco_0;
            name = _tco_1;
            i = _tco_2;
            len = _tco_3;
            continue;
            }
            }
        }
    }

    public static List<IRDef> codex_replace_def(List<IRDef> defs, string name, IRDef new_def, long i, long len)
    {
        while (true)
        {
            if ((i() == len))
            {
            return defs;
            }
            else
            {
            if ((defs[(int)i()].name == name()))
            {
            return codex_list_set(defs, i(), new_def);
            }
            else
            {
            var _tco_0 = defs;
            var _tco_1 = name();
            var _tco_2 = new_def;
            var _tco_3 = (i() + 1);
            var _tco_4 = len;
            defs = _tco_0;
            name = _tco_1;
            new_def = _tco_2;
            i = _tco_3;
            len = _tco_4;
            continue;
            }
            }
        }
    }

    public static List<IRDef> codex_list_set(List<IRDef> xs, long idx, IRDef val) => codex_list_set_loop(xs, idx, val, 0, ((long)xs.Count), new List<IRDef>());

    public static List<IRDef> codex_list_set_loop(List<IRDef> xs, long idx, IRDef val, long i, long len, List<IRDef> acc)
    {
        while (true)
        {
            if ((i() == len))
            {
            return acc();
            }
            else
            {
            if ((i() == idx))
            {
            var _tco_0 = xs;
            var _tco_1 = idx;
            var _tco_2 = val;
            var _tco_3 = (i() + 1);
            var _tco_4 = len;
            var _tco_5 = ((Func<List<IRDef>>)(() => { var _l = acc(); _l.Add(val); return _l; }))();
            xs = _tco_0;
            idx = _tco_1;
            val = _tco_2;
            i = _tco_3;
            len = _tco_4;
            acc = _tco_5;
            continue;
            }
            else
            {
            var _tco_0 = xs;
            var _tco_1 = idx;
            var _tco_2 = val;
            var _tco_3 = (i() + 1);
            var _tco_4 = len;
            var _tco_5 = ((Func<List<IRDef>>)(() => { var _l = acc(); _l.Add(xs[(int)i()]); return _l; }))();
            xs = _tco_0;
            idx = _tco_1;
            val = _tco_2;
            i = _tco_3;
            len = _tco_4;
            acc = _tco_5;
            continue;
            }
            }
        }
    }

    public static CodexType ir_expr_type(IRExpr e)
    {
        while (true)
        {
            var _tco_s = e();
            if (_tco_s is IrIntLit _tco_m0)
            {
                var v = _tco_m0.Field0;
            return new IntegerTy();
            }
            else if (_tco_s is IrNumLit _tco_m1)
            {
                var v = _tco_m1.Field0;
            return new IntegerTy();
            }
            else if (_tco_s is IrTextLit _tco_m2)
            {
                var v = _tco_m2.Field0;
            return TextTy();
            }
            else if (_tco_s is IrBoolLit _tco_m3)
            {
                var v = _tco_m3.Field0;
            return new BooleanTy();
            }
            else if (_tco_s is IrCharLit _tco_m4)
            {
                var v = _tco_m4.Field0;
            return new CharTy();
            }
            else if (_tco_s is IrName _tco_m5)
            {
                var n = _tco_m5.Field0;
                var t = _tco_m5.Field1;
            return t;
            }
            else if (_tco_s is IrBinary _tco_m6)
            {
                var op = _tco_m6.Field0;
                var l = _tco_m6.Field1;
                var r = _tco_m6.Field2;
                var t = _tco_m6.Field3;
            return t;
            }
            else if (_tco_s is IrNegate _tco_m7)
            {
                var x = _tco_m7.Field0;
            return new IntegerTy();
            }
            else if (_tco_s is IrIf _tco_m8)
            {
                var c = _tco_m8.Field0;
                var th = _tco_m8.Field1;
                var el = _tco_m8.Field2;
                var t = _tco_m8.Field3;
            return t;
            }
            else if (_tco_s is IrLet _tco_m9)
            {
                var n = _tco_m9.Field0;
                var t = _tco_m9.Field1;
                var v = _tco_m9.Field2;
                var b = _tco_m9.Field3;
            var _tco_0 = b();
            e = _tco_0;
            continue;
            }
            else if (_tco_s is IrApply _tco_m10)
            {
                var f = _tco_m10.Field0;
                var a = _tco_m10.Field1;
                var t = _tco_m10.Field2;
            return t;
            }
            else if (_tco_s is IrLambda _tco_m11)
            {
                var ps = _tco_m11.Field0;
                var b = _tco_m11.Field1;
                var t = _tco_m11.Field2;
            return t;
            }
            else if (_tco_s is IrList _tco_m12)
            {
                var es = _tco_m12.Field0;
                var t = _tco_m12.Field1;
            return new ListTy(t);
            }
            else if (_tco_s is IrMatch _tco_m13)
            {
                var s = _tco_m13.Field0;
                var bs = _tco_m13.Field1;
                var t = _tco_m13.Field2;
            return t;
            }
            else if (_tco_s is IrDo _tco_m14)
            {
                var ss = _tco_m14.Field0;
                var t = _tco_m14.Field1;
            return t;
            }
            else if (_tco_s is IrHandle _tco_m15)
            {
                var eff = _tco_m15.Field0;
                var h = _tco_m15.Field1;
                var cs = _tco_m15.Field2;
                var t = _tco_m15.Field3;
            return t;
            }
            else if (_tco_s is IrRecord _tco_m16)
            {
                var n = _tco_m16.Field0;
                var fs = _tco_m16.Field1;
                var t = _tco_m16.Field2;
            return t;
            }
            else if (_tco_s is IrFieldAccess _tco_m17)
            {
                var r = _tco_m17.Field0;
                var f = _tco_m17.Field1;
                var t = _tco_m17.Field2;
            return t;
            }
            else if (_tco_s is IrFork _tco_m18)
            {
                var body = _tco_m18.Field0;
                var t = _tco_m18.Field1;
            return t;
            }
            else if (_tco_s is IrAwait _tco_m19)
            {
                var task = _tco_m19.Field0;
                var t = _tco_m19.Field1;
            return t;
            }
            else if (_tco_s is IrError _tco_m20)
            {
                var m = _tco_m20.Field0;
                var t = _tco_m20.Field1;
            return t;
            }
        }
    }

    public static IRExpr lower_expr(AExpr e, CodexType ty, LowerCtx ctx) => e() switch { ALitExpr(var text, var kind) => lower_literal(text(), kind()), ANameExpr(var name) => lower_name(name().value, ty(), ctx), AApplyExpr(var f, var a) => lower_apply(f, a, ty(), ctx), ABinaryExpr(var l, var op, var r) => ((Func<IRExpr, IRExpr>)((left_ir) => ((Func<CodexType, IRExpr>)((left_ty) => ((Func<IRExpr, IRExpr>)((right_ir) => new IrBinary(lower_bin_op(op, left_ty), left_ir, right_ir, binary_result_type(op, left_ty, ty()))))(lower_expr(r, ty(), ctx))))(ir_expr_type(left_ir))))(lower_expr(l, ty(), ctx)), AUnaryExpr(var operand) => new IrNegate(lower_expr(operand, new IntegerTy(), ctx)), AIfExpr(var c, var t, var e2) => ((Func<IRExpr, IRExpr>)((then_ir) => ((Func<CodexType, IRExpr>)((then_ty) => ((Func<CodexType, IRExpr>)((result_ty) => ((Func<IRExpr, IRExpr>)((else_ir) => new IrIf(lower_expr(c, new BooleanTy(), ctx), then_ir, else_ir, result_ty)))(lower_expr(e2, result_ty, ctx))))(ty() switch { object ErrorTy => then_ty, })))(ir_expr_type(then_ir))))(lower_expr(t, ty(), ctx)), ALetExpr(var binds, var body) => lower_let(binds, body(), ty(), ctx), ALambdaExpr(var @params, var body) => lower_lambda(@params, body(), ty(), ctx), AMatchExpr(var scrut, var arms) => lower_match(scrut, arms, ty(), ctx), AListExpr(var elems) => lower_list(elems, ty(), ctx), ARecordExpr(var name, var fields) => lower_record(name(), fields, ty(), ctx), AFieldAccess(var rec, var field) => ((Func<IRExpr, IRExpr>)((rec_ir) => ((Func<CodexType, IRExpr>)((rec_ty) => ((Func<CodexType, IRExpr>)((field_ty) => ((Func<CodexType, IRExpr>)((actual_field_ty) => new IrFieldAccess(rec_ir, field().value, actual_field_ty)))(field_ty switch { object ErrorTy => ty(), })))(rec_ty switch { RecordTy(var rname, var rfields) => lookup_record_field(rfields, field().value), ConstructedTy(var cname, var cargs) => ((Func<CodexType, CodexType>)((ctor_raw) => ((Func<CodexType, CodexType>)((resolved_record) => resolved_record switch { RecordTy(var rn, var rf) => lookup_record_field(rf, field().value), _ => ty(), }))(ctor_raw switch { object ErrorTy => ErrorTy(), })))(lookup_type(ctx.types, cname.value)), _ => ty(), })))(deep_resolve(ctx.ust, ir_expr_type(rec_ir)))))(lower_expr(rec, ErrorTy(), ctx)), ADoExpr(var stmts) => lower_do(stmts, ty(), ctx), AHandleExpr(var eff, var body, var clauses) => lower_handle(eff, body(), clauses, ty(), ctx), AErrorExpr(var msg) => new IrError(msg, ty()), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static IRExpr lower_name(string name, CodexType ty, LowerCtx ctx) => ((Func<CodexType, IRExpr>)((raw) => raw switch { object ErrorTy => new IrName(name(), ty()), }))(lookup_type(ctx.types, name()));

    public static IRExpr lower_literal(string text, LiteralKind kind) => kind() switch { IntLit { } => new IrIntLit(long.Parse(_Cce.ToUnicode(text()))), NumLit { } => new IrIntLit(long.Parse(_Cce.ToUnicode(text()))), object TextLit => new IrTextLit(text()), };

    public static IRExpr lower_apply(AExpr f, AExpr a, CodexType ty, LowerCtx ctx) => ((Func<IRExpr, IRExpr>)((func_ir) => ((Func<CodexType, IRExpr>)((func_ty) => ((Func<CodexType, IRExpr>)((arg_ty) => ((Func<CodexType, IRExpr>)((ret_ty) => ((Func<IRExpr, IRExpr>)((arg_ir) => ((Func<CodexType, IRExpr>)((resolved_ret) => ((Func<CodexType, IRExpr>)((actual_ret) => lower_apply_dispatch(func_ir, arg_ir, actual_ret)))(resolved_ret switch { object ErrorTy => ty(), })))(subst_type_vars_from_arg(arg_ty, ir_expr_type(arg_ir), ret_ty))))(lower_expr(a, arg_ty, ctx))))(peel_fun_return(func_ty))))(peel_fun_param(func_ty))))(deep_resolve(ctx.ust, ir_expr_type(func_ir)))))(lower_expr(f, ErrorTy(), ctx));

    public static IRExpr lower_apply_dispatch(IRExpr func_ir, IRExpr arg_ir, CodexType ret_ty) => func_ir switch { IrName(var n, var fty) => ((n == "\u001C\u0010\u0015\"") ? new IrFork(arg_ir, ret_ty) : ((n == "\u000F\u001B\u000F\u0011\u000E") ? new IrAwait(arg_ir, ret_ty) : new IrApply(func_ir, arg_ir, ret_ty))), _ => new IrApply(func_ir, arg_ir, ret_ty), };

    public static IRExpr lower_let(List<ALetBind> binds, AExpr body, CodexType ty, LowerCtx ctx) => ((((long)binds.Count) == 0) ? lower_expr(body(), ty(), ctx) : ((Func<ALetBind, IRExpr>)((b) => ((Func<IRExpr, IRExpr>)((val_ir) => ((Func<CodexType, IRExpr>)((val_ty) => ((Func<LowerCtx, IRExpr>)((ctx2) => new IrLet(b().name.value, val_ty, val_ir, lower_let_rest(binds, body(), ty(), ctx2, 1))))(new LowerCtx(types: Enumerable.Concat(new List<TypeBinding> { TypeBinding, /* error: { */ default(name()), /* error: = */ default(b().name.value), bound_type(), /* error: = */ default(val_ty), /* error: } */ default }, ctx.types).ToList(), ust: ctx.ust))))(deep_resolve(ctx.ust, ir_expr_type(val_ir)))))(lower_expr(b().value, ErrorTy(), ctx))))(binds[(int)0]));

    public static IRExpr lower_let_rest(List<ALetBind> binds, AExpr body, CodexType ty, LowerCtx ctx, long i) => ((i() == ((long)binds.Count)) ? lower_expr(body(), ty(), ctx) : ((Func<ALetBind, IRExpr>)((b) => ((Func<IRExpr, IRExpr>)((val_ir) => ((Func<CodexType, IRExpr>)((val_ty) => ((Func<LowerCtx, IRExpr>)((ctx2) => new IrLet(b().name.value, val_ty, val_ir, lower_let_rest(binds, body(), ty(), ctx2, (i() + 1)))))(new LowerCtx(types: Enumerable.Concat(new List<TypeBinding> { TypeBinding, /* error: { */ default(name()), /* error: = */ default(b().name.value), bound_type(), /* error: = */ default(val_ty), /* error: } */ default }, ctx.types).ToList(), ust: ctx.ust))))(deep_resolve(ctx.ust, ir_expr_type(val_ir)))))(lower_expr(b().value, ErrorTy(), ctx))))(binds[(int)i()]));

    public static IRExpr lower_lambda(List<Name> @params, AExpr body, CodexType ty, LowerCtx ctx) => ((Func<CodexType, IRExpr>)((stripped) => ((Func<List<IRParam>, IRExpr>)((lparams) => ((Func<LowerCtx, IRExpr>)((lctx) => new IrLambda(lparams, lower_expr(body(), get_lambda_return(stripped, ((long)@params.Count)), lctx), ty())))(bind_lambda_to_ctx(ctx, @params, stripped, 0))))(lower_lambda_params(@params, stripped, 0))))(strip_forall_ty(ty()));

    public static LowerCtx bind_lambda_to_ctx(LowerCtx ctx, List<Name> @params, CodexType ty, long i)
    {
        while (true)
        {
            if ((i() == ((long)@params.Count)))
            {
            return ctx;
            }
            else
            {
            var p = @params[(int)i()];
            var param_ty = peel_fun_param(ty());
            var rest_ty = peel_fun_return(ty());
            var ctx2 = new LowerCtx(types: Enumerable.Concat(new List<TypeBinding> { TypeBinding, /* error: { */ default(name()), /* error: = */ default(p.value), bound_type(), /* error: = */ default(param_ty), /* error: } */ default }, ctx.types).ToList(), ust: ctx.ust);
            var _tco_0 = ctx2;
            var _tco_1 = @params;
            var _tco_2 = rest_ty;
            var _tco_3 = (i() + 1);
            ctx = _tco_0;
            @params = _tco_1;
            ty = _tco_2;
            i = _tco_3;
            continue;
            }
        }
    }

    public static List<IRParam> lower_lambda_params(List<Name> @params, CodexType ty, long i) => lower_lambda_params_acc(@params, ty(), i(), new List<IRParam>());

    public static List<IRParam> lower_lambda_params_acc(List<Name> @params, CodexType ty, long i, List<IRParam> acc)
    {
        while (true)
        {
            if ((i() == ((long)@params.Count)))
            {
            return acc();
            }
            else
            {
            var p = @params[(int)i()];
            var param_ty = peel_fun_param(ty());
            var rest_ty = peel_fun_return(ty());
            var _tco_0 = @params;
            var _tco_1 = rest_ty;
            var _tco_2 = (i() + 1);
            var _tco_3 = ((Func<List<IRParam>>)(() => { var _l = acc(); _l.Add(new IRParam(name: p.value, type_val: param_ty)); return _l; }))();
            @params = _tco_0;
            ty = _tco_1;
            i = _tco_2;
            acc = _tco_3;
            continue;
            }
        }
    }

    public static CodexType get_lambda_return(CodexType ty, long n)
    {
        while (true)
        {
            if ((n == 0))
            {
            return ty();
            }
            else
            {
            var _tco_s = ty();
            if (_tco_s is FunTy _tco_m0)
            {
                var p = _tco_m0.Field0;
                var r = _tco_m0.Field1;
            var _tco_0 = r;
            var _tco_1 = (n - 1);
            ty = _tco_0;
            n = _tco_1;
            continue;
            }
            {
            return ErrorTy();
            }
            }
        }
    }

    public static IRExpr lower_match(AExpr scrut, List<AMatchArm> arms, CodexType ty, LowerCtx ctx) => ((Func<IRExpr, IRExpr>)((scrut_ir) => ((Func<CodexType, IRExpr>)((scrut_ty) => ((Func<List<IRBranch>, IRExpr>)((branches) => ((Func<CodexType, IRExpr>)((result_ty) => new IrMatch(scrut_ir, branches, result_ty)))(ty() switch { object ErrorTy => infer_match_type(branches, 0, ((long)branches.Count)), })))(lower_match_arms_loop(arms, ty(), scrut_ty, ctx, 0, ((long)arms.Count)))))(ir_expr_type(scrut_ir))))(lower_expr(scrut, ErrorTy(), ctx));

    public static CodexType infer_match_type(List<IRBranch> branches, long i, long len)
    {
        while (true)
        {
            if ((i() == len))
            {
            return ErrorTy();
            }
            else
            {
            var b = branches[(int)i()];
            var body_ty = ir_expr_type(b().body);
            var _tco_s = body_ty;
            {
                var ErrorTy = _tco_s;
            var _tco_0 = branches;
            var _tco_1 = (i() + 1);
            var _tco_2 = len;
            branches = _tco_0;
            i = _tco_1;
            len = _tco_2;
            continue;
            }
            {
            return body_ty;
            }
            }
        }
    }

    public static List<IRBranch> lower_match_arms_loop(List<AMatchArm> arms, CodexType ty, CodexType scrut_ty, LowerCtx ctx, long i, long len) => lower_match_arms_acc(arms, ty(), scrut_ty, ctx, i(), len, new List<IRBranch>());

    public static List<IRBranch> lower_match_arms_acc(List<AMatchArm> arms, CodexType ty, CodexType scrut_ty, LowerCtx ctx, long i, long len, List<IRBranch> acc)
    {
        while (true)
        {
            if ((i() == len))
            {
            return acc();
            }
            else
            {
            var arm = arms[(int)i()];
            var arm_ctx = bind_pattern_to_ctx(ctx, arm.pattern, scrut_ty);
            var _tco_0 = arms;
            var _tco_1 = ty();
            var _tco_2 = scrut_ty;
            var _tco_3 = ctx;
            var _tco_4 = (i() + 1);
            var _tco_5 = len;
            var _tco_6 = ((Func<List<IRBranch>>)(() => { var _l = acc(); _l.Add(new IRBranch(pattern: lower_pattern(arm.pattern), body: lower_expr(arm.body, ty(), arm_ctx))); return _l; }))();
            arms = _tco_0;
            ty = _tco_1;
            scrut_ty = _tco_2;
            ctx = _tco_3;
            i = _tco_4;
            len = _tco_5;
            acc = _tco_6;
            continue;
            }
        }
    }

    public static LowerCtx bind_pattern_to_ctx(LowerCtx ctx, APat pat, CodexType ty) => pat switch { AVarPat(var name) => new LowerCtx(types: Enumerable.Concat(new List<TypeBinding> { TypeBinding, /* error: { */ default(name()), /* error: = */ default(name().value), bound_type(), /* error: = */ default(ty()), /* error: } */ default }, ctx.types).ToList(), ust: ctx.ust), ACtorPat(var ctor_name, var sub_pats) => ((Func<CodexType, LowerCtx>)((ctor_raw) => ((Func<CodexType, LowerCtx>)((ctor_ty) => ((Func<CodexType, LowerCtx>)((ctor_stripped) => bind_ctor_pattern_fields(ctx, sub_pats, ctor_stripped, 0, ((long)sub_pats.Count))))(strip_forall_ty(ctor_ty))))(deep_resolve(ctx.ust, ctor_raw))))(lookup_type(ctx.types, ctor_name.value)), AWildPat { } => ctx, ALitPat(var text, var kind) => ctx, _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static LowerCtx bind_ctor_pattern_fields(LowerCtx ctx, List<APat> sub_pats, CodexType ctor_ty, long i, long len)
    {
        while (true)
        {
            if ((i() == len))
            {
            return ctx;
            }
            else
            {
            var _tco_s = ctor_ty;
            if (_tco_s is FunTy _tco_m0)
            {
                var param_ty = _tco_m0.Field0;
                var ret_ty = _tco_m0.Field1;
            var ctx2 = bind_pattern_to_ctx(ctx, sub_pats[(int)i()], param_ty);
            var _tco_0 = ctx2;
            var _tco_1 = sub_pats;
            var _tco_2 = ret_ty;
            var _tco_3 = (i() + 1);
            var _tco_4 = len;
            ctx = _tco_0;
            sub_pats = _tco_1;
            ctor_ty = _tco_2;
            i = _tco_3;
            len = _tco_4;
            continue;
            }
            {
            var ctx2 = bind_pattern_to_ctx(ctx, sub_pats[(int)i()], ErrorTy());
            var _tco_0 = ctx2;
            var _tco_1 = sub_pats;
            var _tco_2 = ctor_ty;
            var _tco_3 = (i() + 1);
            var _tco_4 = len;
            ctx = _tco_0;
            sub_pats = _tco_1;
            ctor_ty = _tco_2;
            i = _tco_3;
            len = _tco_4;
            continue;
            }
            }
        }
    }

    public static IRPat lower_pattern(APat p) => p switch { AVarPat(var name) => new IrVarPat(name().value, ErrorTy()), ALitPat(var text, var kind) => new IrLitPat(text(), ErrorTy()), ACtorPat(var name, var subs) => new IrCtorPat(name().value, map_list(lower_pattern, subs), ErrorTy()), AWildPat { } => new IrWildPat(), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static IRExpr lower_list(List<AExpr> elems, CodexType ty, LowerCtx ctx) => ((Func<bool, IRExpr>)((elem_ty) => new IrList(lower_list_elems_loop(elems, elem_ty, ctx, 0, ((long)elems.Count)), elem_ty)))(ty() switch { ListTy(var e) => e(), _ => ((((long)elems.Count) == 0) ? ErrorTy() : ir_expr_type(lower_expr(elems[(int)0], ErrorTy(), ctx))), });

    public static List<IRExpr> lower_list_elems_loop(List<AExpr> elems, CodexType elem_ty, LowerCtx ctx, long i, long len) => lower_list_elems_acc(elems, elem_ty, ctx, i(), len, new List<IRExpr>());

    public static List<IRExpr> lower_list_elems_acc(List<AExpr> elems, CodexType elem_ty, LowerCtx ctx, long i, long len, List<IRExpr> acc)
    {
        while (true)
        {
            if ((i() == len))
            {
            return acc();
            }
            else
            {
            var _tco_0 = elems;
            var _tco_1 = elem_ty;
            var _tco_2 = ctx;
            var _tco_3 = (i() + 1);
            var _tco_4 = len;
            var _tco_5 = ((Func<List<IRExpr>>)(() => { var _l = acc(); _l.Add(lower_expr(elems[(int)i()], elem_ty, ctx)); return _l; }))();
            elems = _tco_0;
            elem_ty = _tco_1;
            ctx = _tco_2;
            i = _tco_3;
            len = _tco_4;
            acc = _tco_5;
            continue;
            }
        }
    }

    public static IRExpr lower_record(Name name, List<AFieldExpr> fields, CodexType ty, LowerCtx ctx) => ((Func<CodexType, IRExpr>)((ctor_raw) => ((Func<CodexType, IRExpr>)((record_ty) => ((Func<CodexType, IRExpr>)((actual_ty) => new IrRecord(name().value, lower_record_fields_typed(fields, actual_ty, ctx, 0, ((long)fields.Count)), actual_ty)))(record_ty switch { object ErrorTy => ty(), })))(ctor_raw switch { object ErrorTy => ty(), })))(lookup_type(ctx.types, name().value));

    public static List<IRFieldVal> lower_record_fields_typed(List<AFieldExpr> fields, CodexType record_ty, LowerCtx ctx, long i, long len) => lower_record_fields_acc(fields, record_ty, ctx, i(), len, new List<IRFieldVal>());

    public static List<IRFieldVal> lower_record_fields_acc(List<AFieldExpr> fields, CodexType record_ty, LowerCtx ctx, long i, long len, List<IRFieldVal> acc)
    {
        while (true)
        {
            if ((i() == len))
            {
            return acc();
            }
            else
            {
            var f = fields[(int)i()];
            var field_expected = record_ty switch { RecordTy(var rname, var rfields) => lookup_record_field(rfields, f.name.value), _ => ErrorTy(), };
            var _tco_0 = fields;
            var _tco_1 = record_ty;
            var _tco_2 = ctx;
            var _tco_3 = (i() + 1);
            var _tco_4 = len;
            var _tco_5 = ((Func<List<IRFieldVal>>)(() => { var _l = acc(); _l.Add(new IRFieldVal(name: f.name.value, value: lower_expr(f.value, field_expected, ctx))); return _l; }))();
            fields = _tco_0;
            record_ty = _tco_1;
            ctx = _tco_2;
            i = _tco_3;
            len = _tco_4;
            acc = _tco_5;
            continue;
            }
        }
    }

    public static IRExpr lower_do(List<ADoStmt> stmts, CodexType ty, LowerCtx ctx) => new IrDo(lower_do_stmts_loop(stmts, ty(), ctx, 0, ((long)stmts.Count)), ty());

    public static List<IRDoStmt> lower_do_stmts_loop(List<ADoStmt> stmts, CodexType ty, LowerCtx ctx, long i, long len) => lower_do_stmts_acc(stmts, ty(), ctx, i(), len, new List<IRDoStmt>());

    public static List<IRDoStmt> lower_do_stmts_acc(List<ADoStmt> stmts, CodexType ty, LowerCtx ctx, long i, long len, List<IRDoStmt> acc)
    {
        while (true)
        {
            if ((i() == len))
            {
            return acc();
            }
            else
            {
            var s = stmts[(int)i()];
            var _tco_s = s;
            if (_tco_s is ADoBindStmt _tco_m0)
            {
                var name = _tco_m0.Field0;
                var val = _tco_m0.Field1;
            var val_ir = lower_expr(val, ty(), ctx);
            var val_ty = ir_expr_type(val_ir);
            var ctx2 = new LowerCtx(types: Enumerable.Concat(new List<TypeBinding> { TypeBinding, /* error: { */ default(name()), /* error: = */ default(name().value), bound_type(), /* error: = */ default(val_ty), /* error: } */ default }, ctx.types).ToList(), ust: ctx.ust);
            var _tco_0 = stmts;
            var _tco_1 = ty();
            var _tco_2 = ctx2;
            var _tco_3 = (i() + 1);
            var _tco_4 = len;
            var _tco_5 = ((Func<List<IRDoStmt>>)(() => { var _l = acc(); _l.Add(new IrDoBind(name().value, val_ty, val_ir)); return _l; }))();
            stmts = _tco_0;
            ty = _tco_1;
            ctx = _tco_2;
            i = _tco_3;
            len = _tco_4;
            acc = _tco_5;
            continue;
            }
            else if (_tco_s is ADoExprStmt _tco_m1)
            {
                var e = _tco_m1.Field0;
            var _tco_0 = stmts;
            var _tco_1 = ty();
            var _tco_2 = ctx;
            var _tco_3 = (i() + 1);
            var _tco_4 = len;
            var _tco_5 = ((Func<List<IRDoStmt>>)(() => { var _l = acc(); _l.Add(new IrDoExec(lower_expr(e(), ty(), ctx))); return _l; }))();
            stmts = _tco_0;
            ty = _tco_1;
            ctx = _tco_2;
            i = _tco_3;
            len = _tco_4;
            acc = _tco_5;
            continue;
            }
            }
        }
    }

    public static IRExpr lower_handle(Name eff, AExpr body, List<AHandleClause> clauses, CodexType ty, LowerCtx ctx) => ((Func<IRExpr, IRExpr>)((body_ir) => new IrHandle(eff.value, body_ir, lower_handle_clauses(clauses, ty(), ctx), ty())))(lower_expr(body(), ty(), ctx));

    public static List<IRHandleClause> lower_handle_clauses(List<AHandleClause> clauses, CodexType ty, LowerCtx ctx) => lower_handle_clauses_loop(clauses, ty(), ctx, 0);

    public static List<IRHandleClause> lower_handle_clauses_loop(List<AHandleClause> clauses, CodexType ty, LowerCtx ctx, long i) => lower_handle_clauses_acc(clauses, ty(), ctx, i(), new List<IRHandleClause>());

    public static List<IRHandleClause> lower_handle_clauses_acc(List<AHandleClause> clauses, CodexType ty, LowerCtx ctx, long i, List<IRHandleClause> acc)
    {
        while (true)
        {
            if ((i() == ((long)clauses.Count)))
            {
            return acc();
            }
            else
            {
            var c = clauses[(int)i()];
            var body_ir = lower_expr(c.body, ty(), ctx);
            var _tco_0 = clauses;
            var _tco_1 = ty();
            var _tco_2 = ctx;
            var _tco_3 = (i() + 1);
            var _tco_4 = ((Func<List<IRHandleClause>>)(() => { var _l = acc(); _l.Add(new IRHandleClause(op_name: c.op_name.value, resume_name: c.resume_name.value, body: body_ir)); return _l; }))();
            clauses = _tco_0;
            ty = _tco_1;
            ctx = _tco_2;
            i = _tco_3;
            acc = _tco_4;
            continue;
            }
        }
    }

    public static IRDef lower_def(ADef d, List<TypeBinding> types, UnificationState ust) => ((Func<CodexType, IRDef>)((raw_type) => ((Func<CodexType, IRDef>)((full_type) => ((Func<CodexType, IRDef>)((stripped) => ((Func<List<IRParam>, IRDef>)((@params) => ((Func<CodexType, IRDef>)((ret_type) => ((Func<LowerCtx, IRDef>)((ctx) => new IRDef(name: d.name.value, @params: @params, type_val: full_type, body: lower_expr(d.body, ret_type, ctx))))(build_def_ctx(types, ust, d.@params, stripped))))(get_return_type_n(stripped, ((long)d.@params.Count)))))(lower_def_params(d.@params, stripped, 0))))(strip_forall_ty(full_type))))(deep_resolve(ust, raw_type))))(lookup_type(types, d.name.value));

    public static LowerCtx build_def_ctx(List<TypeBinding> types, UnificationState ust, List<AParam> @params, CodexType ty) => ((Func<LowerCtx, LowerCtx>)((base_ctx) => bind_params_to_ctx(base_ctx, @params, ty(), 0)))(new LowerCtx(types: types, ust: ust));

    public static LowerCtx bind_params_to_ctx(LowerCtx ctx, List<AParam> @params, CodexType ty, long i)
    {
        while (true)
        {
            if ((i() == ((long)@params.Count)))
            {
            return ctx;
            }
            else
            {
            var p = @params[(int)i()];
            var param_ty = peel_fun_param(ty());
            var rest_ty = peel_fun_return(ty());
            var ctx2 = new LowerCtx(types: Enumerable.Concat(new List<TypeBinding> { TypeBinding, /* error: { */ default(name()), /* error: = */ default(p.name.value), bound_type(), /* error: = */ default(param_ty), /* error: } */ default }, ctx.types).ToList(), ust: ctx.ust);
            var _tco_0 = ctx2;
            var _tco_1 = @params;
            var _tco_2 = rest_ty;
            var _tco_3 = (i() + 1);
            ctx = _tco_0;
            @params = _tco_1;
            ty = _tco_2;
            i = _tco_3;
            continue;
            }
        }
    }

    public static List<IRParam> lower_def_params(List<AParam> @params, CodexType ty, long i) => lower_def_params_acc(@params, ty(), i(), new List<IRParam>());

    public static List<IRParam> lower_def_params_acc(List<AParam> @params, CodexType ty, long i, List<IRParam> acc)
    {
        while (true)
        {
            if ((i() == ((long)@params.Count)))
            {
            return acc();
            }
            else
            {
            var p = @params[(int)i()];
            var param_ty = peel_fun_param(ty());
            var rest_ty = peel_fun_return(ty());
            var _tco_0 = @params;
            var _tco_1 = rest_ty;
            var _tco_2 = (i() + 1);
            var _tco_3 = ((Func<List<IRParam>>)(() => { var _l = acc(); _l.Add(new IRParam(name: p.name.value, type_val: param_ty)); return _l; }))();
            @params = _tco_0;
            ty = _tco_1;
            i = _tco_2;
            acc = _tco_3;
            continue;
            }
        }
    }

    public static CodexType get_return_type_n(CodexType ty, long n)
    {
        while (true)
        {
            if ((n == 0))
            {
            return ty();
            }
            else
            {
            var _tco_s = ty();
            if (_tco_s is FunTy _tco_m0)
            {
                var p = _tco_m0.Field0;
                var r = _tco_m0.Field1;
            var _tco_0 = r;
            var _tco_1 = (n - 1);
            ty = _tco_0;
            n = _tco_1;
            continue;
            }
            {
            return ErrorTy();
            }
            }
        }
    }

    public static IRModule lower_module(AModule m, List<TypeBinding> types, UnificationState ust) => ((Func<List<TypeBinding>, IRModule>)((ctor_types) => ((Func<List<TypeBinding>, IRModule>)((all_types) => new IRModule(name: m.name, defs: lower_defs(m.defs, all_types, ust, 0))))(Enumerable.Concat(ctor_types, Enumerable.Concat(types, builtin_type_env().bindings).ToList()).ToList())))(collect_ctor_bindings(m.type_defs, 0, ((long)m.type_defs.Count), new List<TypeBinding>()));

    public static List<IRDef> lower_defs(List<ADef> defs, List<TypeBinding> types, UnificationState ust, long i) => lower_defs_acc(defs, types, ust, i(), new List<IRDef>());

    public static List<IRDef> lower_defs_acc(List<ADef> defs, List<TypeBinding> types, UnificationState ust, long i, List<IRDef> acc)
    {
        while (true)
        {
            if ((i() == ((long)defs.Count)))
            {
            return acc();
            }
            else
            {
            var _tco_0 = defs;
            var _tco_1 = types;
            var _tco_2 = ust;
            var _tco_3 = (i() + 1);
            var _tco_4 = ((Func<List<IRDef>>)(() => { var _l = acc(); _l.Add(lower_def(defs[(int)i()], types, ust)); return _l; }))();
            defs = _tco_0;
            types = _tco_1;
            ust = _tco_2;
            i = _tco_3;
            acc = _tco_4;
            continue;
            }
        }
    }

    public static CodexType lookup_type(List<TypeBinding> bindings, string name) => lookup_type_loop(bindings(), name(), 0, ((long)bindings().Count));

    public static CodexType lookup_type_loop(List<TypeBinding> bindings, string name, long i, long len)
    {
        while (true)
        {
            if ((i() == len))
            {
            return ErrorTy();
            }
            else
            {
            var b = bindings()[(int)i()];
            if ((b().name == name()))
            {
            return b().bound_type;
            }
            else
            {
            var _tco_0 = bindings();
            var _tco_1 = name();
            var _tco_2 = (i() + 1);
            var _tco_3 = len;
            bindings = _tco_0;
            name = _tco_1;
            i = _tco_2;
            len = _tco_3;
            continue;
            }
            }
        }
    }

    public static CodexType peel_fun_param(CodexType ty)
    {
        while (true)
        {
            var _tco_s = ty();
            if (_tco_s is FunTy _tco_m0)
            {
                var p = _tco_m0.Field0;
                var r = _tco_m0.Field1;
            return p;
            }
            else if (_tco_s is ForAllTy _tco_m1)
            {
                var id = _tco_m1.Field0;
                var body = _tco_m1.Field1;
            var _tco_0 = body();
            ty = _tco_0;
            continue;
            }
            {
            return ErrorTy();
            }
        }
    }

    public static CodexType peel_fun_return(CodexType ty)
    {
        while (true)
        {
            var _tco_s = ty();
            if (_tco_s is FunTy _tco_m0)
            {
                var p = _tco_m0.Field0;
                var r = _tco_m0.Field1;
            return r;
            }
            else if (_tco_s is ForAllTy _tco_m1)
            {
                var id = _tco_m1.Field0;
                var body = _tco_m1.Field1;
            var _tco_0 = body();
            ty = _tco_0;
            continue;
            }
            {
            return ErrorTy();
            }
        }
    }

    public static CodexType strip_forall_ty(CodexType ty)
    {
        while (true)
        {
            var _tco_s = ty();
            if (_tco_s is ForAllTy _tco_m0)
            {
                var id = _tco_m0.Field0;
                var body = _tco_m0.Field1;
            var _tco_0 = body();
            ty = _tco_0;
            continue;
            }
            {
            return ty();
            }
        }
    }

    public static CodexType subst_type_vars_from_arg(CodexType param_ty, CodexType arg_ty, CodexType target) => param_ty switch { object TypeVar => id(), };

    public static T3148 subst_type_var_in_target<T3148>() => id()(arg_ty);

    public static List<long> ListTy(object pe) => subst_from_list(pe, arg_ty, target());

    public static List<long> FunTy(object pp, object pr) => subst_from_fun(pp, pr, arg_ty, target());

    public static Func<T3150, Func<object, Func<CodexType, T3153>>> target<T3150, T3153>() => (_p0_) => (_p1_) => (_p2_) => subst_from_list(_p0_, _p1_, _p2_);

    public static Token CodexType() => new CodexType();

    public static Token CodexType() => new CodexType();

    public static CodexType subst_from_list(object pe, object arg_ty, object target) => arg_ty switch { ListTy(var ae) => subst_type_vars_from_arg(pe, ae, target()), _ => target(), };

    public static CodexType subst_from_fun(CodexType pp, CodexType pr, CodexType arg_ty, CodexType target) => arg_ty switch { FunTy(var ap, var ar) => ((Func<CodexType, CodexType>)((t2) => subst_type_vars_from_arg(pr, ar, t2)))(subst_type_vars_from_arg(pp, ap, target())), _ => target(), };

    public static T3148 subst_type_var_in_target<T3148>(object ty, object var_id, object replacement) => ty() switch { object TypeVar => id(), };

    public static object id() => var_id;

    public static bool replacement() => ty();

    public static List<long> FunTy(object p, object r)
    {
        while (true)
        {
            var _tco_0 = subst_type_var_in_target()(p)(var_id)(replacement());
            var _tco_1 = subst_type_var_in_target()(r)(var_id)(replacement());
            p = _tco_0;
            r = _tco_1;
            continue;
        }
    }

    public static List<long> ListTy(object elem)
    {
        while (true)
        {
            var _tco_0 = subst_type_var_in_target()(elem)(var_id)(replacement());
            elem = _tco_0;
            continue;
        }
    }

    public static List<long> ForAllTy(object fid, object body)
    {
        while (true)
        {
            if ((fid == var_id))
            {
            return ty();
            }
            else
            {
            var _tco_0 = fid;
            var _tco_1 = subst_type_var_in_target()(body())(var_id)(replacement());
            fid = _tco_0;
            body = _tco_1;
            continue;
            }
        }
    }

    public static Func<CodexType, CodexType> ty() => strip_fun_args_lower;

    public static Token CodexType() => new CodexType();

    public static CodexType strip_fun_args_lower(object ty)
    {
        while (true)
        {
            var _tco_s = ty();
            if (_tco_s is FunTy _tco_m0)
            {
                var p = _tco_m0.Field0;
                var r = _tco_m0.Field1;
            var _tco_0 = r;
            ty = _tco_0;
            continue;
            }
            else if (_tco_s is ForAllTy _tco_m1)
            {
                var id = _tco_m1.Field0;
                var body = _tco_m1.Field1;
            var _tco_0 = body();
            ty = _tco_0;
            continue;
            }
            {
            return ty();
            }
        }
    }

    public static bool is_text_type(CodexType ty) => ty() switch { object TextTy => true, };

    public static List<TypeBinding> collect_ctor_bindings(List<ATypeDef> tdefs, long i, long len, List<TypeBinding> acc)
    {
        while (true)
        {
            if ((i() == len))
            {
            return acc();
            }
            else
            {
            var td = tdefs[(int)i()];
            var bindings = ctor_bindings_for_typedef(td);
            var _tco_0 = tdefs;
            var _tco_1 = (i() + 1);
            var _tco_2 = len;
            var _tco_3 = Enumerable.Concat(acc(), bindings()).ToList();
            tdefs = _tco_0;
            i = _tco_1;
            len = _tco_2;
            acc = _tco_3;
            continue;
            }
        }
    }

    public static List<TypeBinding> ctor_bindings_for_typedef(ATypeDef td) => td switch { AVariantTypeDef(var name, var type_params, var ctors) => ((Func<object, List<TypeBinding>>)((result_ty) => collect_variant_ctor_bindings(ctors, result_ty, 0, ((long)ctors.Count), new List<TypeBinding>())))(new ConstructedTy(name(), new List<object>())), ARecordTypeDef(var name, var type_params, var fields) => ((Func<List<RecordField>, List<TypeBinding>>)((resolved_fields) => ((Func<object, List<TypeBinding>>)((result_ty) => ((Func<CodexType, List<TypeBinding>>)((ctor_ty) => new List<TypeBinding> { TypeBinding, /* error: { */ default(name()), /* error: = */ default(name().value), bound_type(), /* error: = */ default(ctor_ty), /* error: } */ default }))(build_record_ctor_type_for_lower(fields, result_ty, 0, ((long)fields.Count)))))(new RecordTy(name(), resolved_fields))))(build_record_fields_for_lower(fields, 0, ((long)fields.Count), new List<RecordField>())), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static List<TypeBinding> collect_variant_ctor_bindings(List<AVariantCtorDef> ctors, CodexType result_ty, long i, long len, List<TypeBinding> acc)
    {
        while (true)
        {
            if ((i() == len))
            {
            return acc();
            }
            else
            {
            var ctor = ctors[(int)i()];
            var ctor_ty = build_ctor_type_for_lower(ctor().fields, result_ty, 0, ((long)ctor().fields.Count));
            var _tco_0 = ctors;
            var _tco_1 = result_ty;
            var _tco_2 = (i() + 1);
            var _tco_3 = len;
            var _tco_4 = ((Func<List<TypeBinding>>)(() => { var _l = acc(); _l.Add(TypeBinding); return _l; }))();
            var _tco_5 = ctor().name.value;
            ctors = _tco_0;
            result_ty = _tco_1;
            i = _tco_2;
            len = _tco_3;
            acc = _tco_4;
            continue;
            }
        }
    }

    public static object bound_type() => ctor_ty;

    public static CodexType build_ctor_type_for_lower(List<ATypeExpr> fields, CodexType result, long i, long len) => ((i() == len) ? result : ((Func<CodexType, CodexType>)((rest) => new FunTy(resolve_type_expr_for_lower(fields[(int)i()]), rest)))(build_ctor_type_for_lower(fields, result, (i() + 1), len)));

    public static List<RecordField> build_record_fields_for_lower(List<ARecordFieldDef> fields, long i, long len, List<RecordField> acc)
    {
        while (true)
        {
            if ((i() == len))
            {
            return acc();
            }
            else
            {
            var f = fields[(int)i()];
            var rfield = new RecordField(name: f.name, type_val: resolve_type_expr_for_lower(f.type_expr));
            var _tco_0 = fields;
            var _tco_1 = (i() + 1);
            var _tco_2 = len;
            var _tco_3 = ((Func<List<RecordField>>)(() => { var _l = acc(); _l.Add(rfield); return _l; }))();
            fields = _tco_0;
            i = _tco_1;
            len = _tco_2;
            acc = _tco_3;
            continue;
            }
        }
    }

    public static CodexType build_record_ctor_type_for_lower(List<ARecordFieldDef> fields, CodexType result, long i, long len) => ((i() == len) ? result : ((Func<ARecordFieldDef, CodexType>)((f) => ((Func<CodexType, CodexType>)((rest) => new FunTy(resolve_type_expr_for_lower(f.type_expr), rest)))(build_record_ctor_type_for_lower(fields, result, (i() + 1), len))))(fields[(int)i()]));

    public static CodexType resolve_type_expr_for_lower(ATypeExpr texpr) => texpr switch { ANamedType(var name) => ((name().value == "I\u0012\u000E\u000D\u001D\u000D\u0015") ? new IntegerTy() : ((name().value == "N\u0019\u001Ab\u000D\u0015") ? new NumberTy() : ((name().value == "T\u000Dx\u000E") ? TextTy() : ((name().value == "B\u0010\u0010\u0017\u000D\u000F\u0012") ? new BooleanTy() : ((name().value == "N\u0010\u000E\u0014\u0011\u0012\u001D") ? new NothingTy() : new ConstructedTy(name(), new List<object>())))))), AFunType(var param, var ret) => new FunTy(resolve_type_expr_for_lower(param), resolve_type_expr_for_lower(ret())), AAppType(var ctor, var args) => ctor() switch { ANamedType(var cname) => ((cname.value == "L\u0011\u0013\u000E") ? ((((long)args.Count) == 1) ? new ListTy(resolve_type_expr_for_lower(args[(int)0])) : new ListTy(ErrorTy())) : new ConstructedTy(cname, new List<object>())), _ => ErrorTy(), }, _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static IRBinaryOp lower_bin_op(BinaryOp op, CodexType ty) => op switch { OpAdd { } => new IrAddInt(), OpSub { } => new IrSubInt(), OpMul { } => new IrMulInt(), OpDiv { } => new IrDivInt(), OpPow { } => new IrPowInt(), OpEq { } => new IrEq(), OpNotEq { } => new IrNotEq(), OpLt { } => new IrLt(), OpGt { } => new IrGt(), OpLtEq { } => new IrLtEq(), OpGtEq { } => new IrGtEq(), OpDefEq { } => new IrEq(), OpAppend { } => (is_text_type(ty()) ? new IrAppendText() : new IrAppendList()), OpCons { } => new IrConsList(), OpAnd { } => new IrAnd(), OpOr { } => new IrOr(), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static CodexType binary_result_type(BinaryOp op, CodexType left_ty, CodexType expected_ty) => op switch { OpEq { } => new BooleanTy(), OpNotEq { } => new BooleanTy(), OpLt { } => new BooleanTy(), OpGt { } => new BooleanTy(), OpLtEq { } => new BooleanTy(), OpGtEq { } => new BooleanTy(), OpDefEq { } => new BooleanTy(), OpAnd { } => new BooleanTy(), OpOr { } => new BooleanTy(), OpAppend { } => (is_text_type(left_ty) ? TextTy() : (is_text_type(expected_ty) ? TextTy() : left_ty)), _ => left_ty, };

    public static Scope empty_scope() => new Scope(names: new List<string>());

    public static bool scope_has(Scope sc, string name) => ((Func<long, bool>)((len) => ((len == 0) ? false : ((Func<long, bool>)((pos) => ((pos() >= len) ? false : (sc.names[(int)pos()] == name()))))(bsearch_text_set(sc.names, name(), 0, len)))))(((long)sc.names.Count));

    public static Scope scope_add(Scope sc, string name) => ((Func<long, Scope>)((len) => ((Func<long, Scope>)((pos) => new Scope(names: ((Func<List<string>>)(() => { var _l = new List<string>(sc.names); _l.Insert((int)pos(), name()); return _l; }))())))(bsearch_text_set(sc.names, name(), 0, len))))(((long)sc.names.Count));

    public static List<string> builtin_names() => new List<string> { "\u0013\u0014\u0010\u001B", "\u0012\u000D\u001D\u000F\u000E\u000D", "T\u0015\u0019\u000D", "F\u000F\u0017\u0013\u000D", "N\u0010\u000E\u0014\u0011\u0012\u001D", "\u001F\u0015\u0011\u0012\u000E-\u0017\u0011\u0012\u000D", "\u0015\u000D\u000F\u0016-\u0017\u0011\u0012\u000D", "\u0015\u000D\u000F\u0016-\u001C\u0011\u0017\u000D", "\u001B\u0015\u0011\u000E\u000D-\u001C\u0011\u0017\u000D", "\u001C\u0011\u0017\u000D-\u000Dx\u0011\u0013\u000E\u0013", "\u0017\u0011\u0013\u000E-\u001C\u0011\u0017\u000D\u0013", "\u0010\u001F\u000D\u0012-\u001C\u0011\u0017\u000D", "\u0015\u000D\u000F\u0016-\u000F\u0017\u0017", "\u0018\u0017\u0010\u0013\u000D-\u001C\u0011\u0017\u000D", "\u0018\u0014\u000F\u0015-\u000F\u000E", "\u0018\u0014\u000F\u0015-\u000E\u0010-\u000E\u000Dx\u000E", "\u000E\u000Dx\u000E-\u0017\u000D\u0012\u001D\u000E\u0014", "\u0013\u0019b\u0013\u000E\u0015\u0011\u0012\u001D", "\u0011\u0013-\u0017\u000D\u000E\u000E\u000D\u0015", "\u0011\u0013-\u0016\u0011\u001D\u0011\u000E", "\u0011\u0013-\u001B\u0014\u0011\u000E\u000D\u0013\u001F\u000F\u0018\u000D", "\u000E\u000Dx\u000E-\u000E\u0010-\u0011\u0012\u000E\u000D\u001D\u000D\u0015", "\u0011\u0012\u000E\u000D\u001D\u000D\u0015-\u000E\u0010-\u000E\u000Dx\u000E", "\u000E\u000Dx\u000E-\u0015\u000D\u001F\u0017\u000F\u0018\u000D", "\u000E\u000Dx\u000E-\u0013\u001F\u0017\u0011\u000E", "\u000E\u000Dx\u000E-\u0018\u0010\u0012\u000E\u000F\u0011\u0012\u0013", "\u000E\u000Dx\u000E-\u0013\u000E\u000F\u0015\u000E\u0013-\u001B\u0011\u000E\u0014", "\u0018\u0014\u000F\u0015-\u0018\u0010\u0016\u000D", "\u0018\u0014\u000F\u0015-\u0018\u0010\u0016\u000D-\u000F\u000E", "\u0018\u0010\u0016\u000D-\u000E\u0010-\u0018\u0014\u000F\u0015", "\u0017\u0011\u0013\u000E-\u0017\u000D\u0012\u001D\u000E\u0014", "\u0017\u0011\u0013\u000E-\u000F\u000E", "\u0017\u0011\u0013\u000E-\u0011\u0012\u0013\u000D\u0015\u000E-\u000F\u000E", "\u0017\u0011\u0013\u000E-\u0013\u0012\u0010\u0018", "\u000E\u000Dx\u000E-\u0018\u0010\u001A\u001F\u000F\u0015\u000D", "\u001D\u000D\u000E-\u000F\u0015\u001D\u0013", "\u001D\u000D\u000E-\u000D\u0012v", "\u0018\u0019\u0015\u0015\u000D\u0012\u000E-\u0016\u0011\u0015", "\u001A\u000F\u001F", "\u001C\u0011\u0017\u000E\u000D\u0015", "\u001C\u0010\u0017\u0016" };

    public static bool is_type_name(string name) => ((((long)name().Length) == 0) ? false : ((((long)name()[(int)0]) >= 13L && ((long)name()[(int)0]) <= 64L) && is_upper_char(((long)name()[(int)0]))));

    public static bool is_upper_char(long c) => ((Func<long, bool>)((code) => ((code >= 41) && (code <= 64))))(c);

    public static CollectResult collect_top_level_names(List<ADef> defs, long i, long len, List<string> acc, List<Diagnostic> errs)
    {
        while (true)
        {
            if ((i() == len))
            {
            return new CollectResult(names: acc(), errors: errs);
            }
            else
            {
            var def = defs[(int)i()];
            var name = def().name.value;
            if (list_contains(acc(), name()))
            {
            var _tco_0 = defs;
            var _tco_1 = (i() + 1);
            var _tco_2 = len;
            var _tco_3 = acc();
            var _tco_4 = Enumerable.Concat(errs, new List<Diagnostic> { make_error("CDX\u0006\u0003\u0003\u0004", ("D\u0019\u001F\u0017\u0011\u0018\u000F\u000E\u000D\u0002\u0016\u000D\u001C\u0011\u0012\u0011\u000E\u0011\u0010\u0012:\u0002" + name())) }).ToList();
            defs = _tco_0;
            i = _tco_1;
            len = _tco_2;
            acc = _tco_3;
            errs = _tco_4;
            continue;
            }
            else
            {
            var pos = bsearch_text_set(acc(), name(), 0, ((long)acc().Count));
            var _tco_0 = defs;
            var _tco_1 = (i() + 1);
            var _tco_2 = len;
            var _tco_3 = ((Func<List<string>>)(() => { var _l = new List<string>(acc()); _l.Insert((int)pos(), name()); return _l; }))();
            var _tco_4 = errs;
            defs = _tco_0;
            i = _tco_1;
            len = _tco_2;
            acc = _tco_3;
            errs = _tco_4;
            continue;
            }
            }
        }
    }

    public static bool list_contains(List<string> xs, string name) => ((Func<long, bool>)((len) => ((len == 0) ? false : ((Func<long, bool>)((pos) => ((pos() >= len) ? false : (xs[(int)pos()] == name()))))(bsearch_text_set(xs, name(), 0, len)))))(((long)xs.Count));

    public static CtorCollectResult collect_ctor_names(List<ATypeDef> type_defs, long i, long len, List<string> type_acc, List<string> ctor_acc)
    {
        while (true)
        {
            if ((i() == len))
            {
            return new CtorCollectResult(type_names: type_acc, ctor_names: ctor_acc);
            }
            else
            {
            var td = type_defs[(int)i()];
            var _tco_s = td;
            if (_tco_s is AVariantTypeDef _tco_m0)
            {
                var name = _tco_m0.Field0;
                var @params = _tco_m0.Field1;
                var ctors = _tco_m0.Field2;
            var new_type_acc = ((Func<List<string>>)(() => { var _l = type_acc; _l.Add(name().value); return _l; }))();
            var new_ctor_acc = collect_variant_ctors(ctors, 0, ((long)ctors.Count), ctor_acc);
            var _tco_0 = type_defs;
            var _tco_1 = (i() + 1);
            var _tco_2 = len;
            var _tco_3 = new_type_acc;
            var _tco_4 = new_ctor_acc;
            type_defs = _tco_0;
            i = _tco_1;
            len = _tco_2;
            type_acc = _tco_3;
            ctor_acc = _tco_4;
            continue;
            }
            else if (_tco_s is ARecordTypeDef _tco_m1)
            {
                var name = _tco_m1.Field0;
                var @params = _tco_m1.Field1;
                var fields = _tco_m1.Field2;
            var _tco_0 = type_defs;
            var _tco_1 = (i() + 1);
            var _tco_2 = len;
            var _tco_3 = ((Func<List<string>>)(() => { var _l = type_acc; _l.Add(name().value); return _l; }))();
            var _tco_4 = ctor_acc;
            type_defs = _tco_0;
            i = _tco_1;
            len = _tco_2;
            type_acc = _tco_3;
            ctor_acc = _tco_4;
            continue;
            }
            }
        }
    }

    public static List<string> collect_variant_ctors(List<AVariantCtorDef> ctors, long i, long len, List<string> acc)
    {
        while (true)
        {
            if ((i() == len))
            {
            return acc();
            }
            else
            {
            var ctor = ctors[(int)i()];
            var _tco_0 = ctors;
            var _tco_1 = (i() + 1);
            var _tco_2 = len;
            var _tco_3 = ((Func<List<string>>)(() => { var _l = acc(); _l.Add(ctor().name.value); return _l; }))();
            ctors = _tco_0;
            i = _tco_1;
            len = _tco_2;
            acc = _tco_3;
            continue;
            }
        }
    }

    public static Scope build_all_names_scope(List<string> top_names, List<string> ctor_names, List<string> builtins) => ((Func<Scope, Scope>)((sc) => ((Func<Scope, Scope>)((sc2) => add_names_to_scope(sc2, builtins, 0, ((long)builtins.Count))))(add_names_to_scope(sc, ctor_names, 0, ((long)ctor_names.Count)))))(add_names_to_scope(empty_scope(), top_names, 0, ((long)top_names.Count)));

    public static Scope add_names_to_scope(Scope sc, List<string> names, long i, long len)
    {
        while (true)
        {
            if ((i() == len))
            {
            return sc;
            }
            else
            {
            var _tco_0 = scope_add(sc, names[(int)i()]);
            var _tco_1 = names;
            var _tco_2 = (i() + 1);
            var _tco_3 = len;
            sc = _tco_0;
            names = _tco_1;
            i = _tco_2;
            len = _tco_3;
            continue;
            }
        }
    }

    public static List<Diagnostic> resolve_expr(Scope sc, AExpr expr)
    {
        while (true)
        {
            var _tco_s = expr;
            if (_tco_s is ALitExpr _tco_m0)
            {
                var val = _tco_m0.Field0;
                var kind = _tco_m0.Field1;
            return new List<Diagnostic>();
            }
            else if (_tco_s is ANameExpr _tco_m1)
            {
                var name = _tco_m1.Field0;
            if ((scope_has(sc, name().value) || is_type_name(name().value)))
            {
            return new List<Diagnostic>();
            }
            else
            {
            return new List<Diagnostic> { make_error("CDX\u0006\u0003\u0003\u0005", ("U\u0012\u0016\u000D\u001C\u0011\u0012\u000D\u0016\u0002\u0012\u000F\u001A\u000D:\u0002" + name().value)) };
            }
            }
            else if (_tco_s is ABinaryExpr _tco_m2)
            {
                var left = _tco_m2.Field0;
                var op = _tco_m2.Field1;
                var right = _tco_m2.Field2;
            return Enumerable.Concat(resolve_expr(sc, left), resolve_expr(sc, right)).ToList();
            }
            else if (_tco_s is AUnaryExpr _tco_m3)
            {
                var operand = _tco_m3.Field0;
            var _tco_0 = sc;
            var _tco_1 = operand;
            sc = _tco_0;
            expr = _tco_1;
            continue;
            }
            else if (_tco_s is AApplyExpr _tco_m4)
            {
                var func = _tco_m4.Field0;
                var arg = _tco_m4.Field1;
            return Enumerable.Concat(resolve_expr(sc, func), resolve_expr(sc, arg)).ToList();
            }
            else if (_tco_s is AIfExpr _tco_m5)
            {
                var cond = _tco_m5.Field0;
                var then_e = _tco_m5.Field1;
                var else_e = _tco_m5.Field2;
            return Enumerable.Concat(resolve_expr(sc, cond), Enumerable.Concat(resolve_expr(sc, then_e), resolve_expr(sc, else_e)).ToList()).ToList();
            }
            else if (_tco_s is ALetExpr _tco_m6)
            {
                var bindings = _tco_m6.Field0;
                var body = _tco_m6.Field1;
            return resolve_let(sc, bindings(), body(), 0, ((long)bindings().Count), new List<Diagnostic>());
            }
            else if (_tco_s is ALambdaExpr _tco_m7)
            {
                var @params = _tco_m7.Field0;
                var body = _tco_m7.Field1;
            var sc2 = add_lambda_params(sc, @params, 0, ((long)@params.Count));
            var _tco_0 = sc2;
            var _tco_1 = body();
            sc = _tco_0;
            expr = _tco_1;
            continue;
            }
            else if (_tco_s is AMatchExpr _tco_m8)
            {
                var scrutinee = _tco_m8.Field0;
                var arms = _tco_m8.Field1;
            return Enumerable.Concat(resolve_expr(sc, scrutinee), resolve_match_arms(sc, arms, 0, ((long)arms.Count), new List<Diagnostic>())).ToList();
            }
            else if (_tco_s is AListExpr _tco_m9)
            {
                var elems = _tco_m9.Field0;
            return resolve_list_elems(sc, elems, 0, ((long)elems.Count), new List<Diagnostic>());
            }
            else if (_tco_s is ARecordExpr _tco_m10)
            {
                var name = _tco_m10.Field0;
                var fields = _tco_m10.Field1;
            return resolve_record_fields(sc, fields, 0, ((long)fields.Count), new List<Diagnostic>());
            }
            else if (_tco_s is AFieldAccess _tco_m11)
            {
                var obj = _tco_m11.Field0;
                var field = _tco_m11.Field1;
            var _tco_0 = sc;
            var _tco_1 = obj;
            sc = _tco_0;
            expr = _tco_1;
            continue;
            }
            else if (_tco_s is ADoExpr _tco_m12)
            {
                var stmts = _tco_m12.Field0;
            return resolve_do_stmts(sc, stmts, 0, ((long)stmts.Count), new List<Diagnostic>());
            }
            else if (_tco_s is AErrorExpr _tco_m13)
            {
                var msg = _tco_m13.Field0;
            return new List<Diagnostic>();
            }
        }
    }

    public static List<Diagnostic> resolve_let(Scope sc, List<ALetBind> bindings, AExpr body, long i, long len, List<Diagnostic> errs)
    {
        while (true)
        {
            if ((i() == len))
            {
            return Enumerable.Concat(errs, resolve_expr(sc, body())).ToList();
            }
            else
            {
            var b = bindings()[(int)i()];
            var bind_errs = resolve_expr(sc, b().value);
            var sc2 = scope_add(sc, b().name.value);
            var _tco_0 = sc2;
            var _tco_1 = bindings();
            var _tco_2 = body();
            var _tco_3 = (i() + 1);
            var _tco_4 = len;
            var _tco_5 = Enumerable.Concat(errs, bind_errs).ToList();
            sc = _tco_0;
            bindings = _tco_1;
            body = _tco_2;
            i = _tco_3;
            len = _tco_4;
            errs = _tco_5;
            continue;
            }
        }
    }

    public static Scope add_lambda_params(Scope sc, List<Name> @params, long i, long len)
    {
        while (true)
        {
            if ((i() == len))
            {
            return sc;
            }
            else
            {
            var p = @params[(int)i()];
            var _tco_0 = scope_add(sc, p.value);
            var _tco_1 = @params;
            var _tco_2 = (i() + 1);
            var _tco_3 = len;
            sc = _tco_0;
            @params = _tco_1;
            i = _tco_2;
            len = _tco_3;
            continue;
            }
        }
    }

    public static List<Diagnostic> resolve_match_arms(Scope sc, List<AMatchArm> arms, long i, long len, List<Diagnostic> errs)
    {
        while (true)
        {
            if ((i() == len))
            {
            return errs;
            }
            else
            {
            var arm = arms[(int)i()];
            var sc2 = collect_pattern_names(sc, arm.pattern);
            var arm_errs = resolve_expr(sc2, arm.body);
            var _tco_0 = sc;
            var _tco_1 = arms;
            var _tco_2 = (i() + 1);
            var _tco_3 = len;
            var _tco_4 = Enumerable.Concat(errs, arm_errs).ToList();
            sc = _tco_0;
            arms = _tco_1;
            i = _tco_2;
            len = _tco_3;
            errs = _tco_4;
            continue;
            }
        }
    }

    public static Scope collect_pattern_names(Scope sc, APat pat) => pat switch { AVarPat(var name) => scope_add(sc, name().value), ACtorPat(var name, var subs) => collect_ctor_pat_names(sc, subs, 0, ((long)subs.Count)), ALitPat(var val, var kind) => sc, AWildPat { } => sc, _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static Scope collect_ctor_pat_names(Scope sc, List<APat> subs, long i, long len)
    {
        while (true)
        {
            if ((i() == len))
            {
            return sc;
            }
            else
            {
            var sub = subs[(int)i()];
            var _tco_0 = collect_pattern_names(sc, sub);
            var _tco_1 = subs;
            var _tco_2 = (i() + 1);
            var _tco_3 = len;
            sc = _tco_0;
            subs = _tco_1;
            i = _tco_2;
            len = _tco_3;
            continue;
            }
        }
    }

    public static List<Diagnostic> resolve_list_elems(Scope sc, List<AExpr> elems, long i, long len, List<Diagnostic> errs)
    {
        while (true)
        {
            if ((i() == len))
            {
            return errs;
            }
            else
            {
            var errs2 = resolve_expr(sc, elems[(int)i()]);
            var _tco_0 = sc;
            var _tco_1 = elems;
            var _tco_2 = (i() + 1);
            var _tco_3 = len;
            var _tco_4 = Enumerable.Concat(errs, errs2).ToList();
            sc = _tco_0;
            elems = _tco_1;
            i = _tco_2;
            len = _tco_3;
            errs = _tco_4;
            continue;
            }
        }
    }

    public static List<Diagnostic> resolve_record_fields(Scope sc, List<AFieldExpr> fields, long i, long len, List<Diagnostic> errs)
    {
        while (true)
        {
            if ((i() == len))
            {
            return errs;
            }
            else
            {
            var f = fields[(int)i()];
            var errs2 = resolve_expr(sc, f.value);
            var _tco_0 = sc;
            var _tco_1 = fields;
            var _tco_2 = (i() + 1);
            var _tco_3 = len;
            var _tco_4 = Enumerable.Concat(errs, errs2).ToList();
            sc = _tco_0;
            fields = _tco_1;
            i = _tco_2;
            len = _tco_3;
            errs = _tco_4;
            continue;
            }
        }
    }

    public static List<Diagnostic> resolve_do_stmts(Scope sc, List<ADoStmt> stmts, long i, long len, List<Diagnostic> errs)
    {
        while (true)
        {
            if ((i() == len))
            {
            return errs;
            }
            else
            {
            var stmt = stmts[(int)i()];
            var _tco_s = stmt;
            if (_tco_s is ADoExprStmt _tco_m0)
            {
                var e = _tco_m0.Field0;
            var errs2 = resolve_expr(sc, e());
            var _tco_0 = sc;
            var _tco_1 = stmts;
            var _tco_2 = (i() + 1);
            var _tco_3 = len;
            var _tco_4 = Enumerable.Concat(errs, errs2).ToList();
            sc = _tco_0;
            stmts = _tco_1;
            i = _tco_2;
            len = _tco_3;
            errs = _tco_4;
            continue;
            }
            else if (_tco_s is ADoBindStmt _tco_m1)
            {
                var name = _tco_m1.Field0;
                var e = _tco_m1.Field1;
            var errs2 = resolve_expr(sc, e());
            var sc2 = scope_add(sc, name().value);
            var _tco_0 = sc2;
            var _tco_1 = stmts;
            var _tco_2 = (i() + 1);
            var _tco_3 = len;
            var _tco_4 = Enumerable.Concat(errs, errs2).ToList();
            sc = _tco_0;
            stmts = _tco_1;
            i = _tco_2;
            len = _tco_3;
            errs = _tco_4;
            continue;
            }
            }
        }
    }

    public static List<Diagnostic> resolve_all_defs(Scope sc, List<ADef> defs, long i, long len, List<Diagnostic> errs)
    {
        while (true)
        {
            if ((i() == len))
            {
            return errs;
            }
            else
            {
            var def = defs[(int)i()];
            var def_scope = add_def_params(sc, def().@params, 0, ((long)def().@params.Count));
            var errs2 = resolve_expr(def_scope, def().body);
            var _tco_0 = sc;
            var _tco_1 = defs;
            var _tco_2 = (i() + 1);
            var _tco_3 = len;
            var _tco_4 = Enumerable.Concat(errs, errs2).ToList();
            sc = _tco_0;
            defs = _tco_1;
            i = _tco_2;
            len = _tco_3;
            errs = _tco_4;
            continue;
            }
        }
    }

    public static Scope add_def_params(Scope sc, List<AParam> @params, long i, long len)
    {
        while (true)
        {
            if ((i() == len))
            {
            return sc;
            }
            else
            {
            var p = @params[(int)i()];
            var _tco_0 = scope_add(sc, p.name.value);
            var _tco_1 = @params;
            var _tco_2 = (i() + 1);
            var _tco_3 = len;
            sc = _tco_0;
            @params = _tco_1;
            i = _tco_2;
            len = _tco_3;
            continue;
            }
        }
    }

    public static ResolveResult resolve_module(AModule mod) => resolve_module_with_imports(mod, new List<ResolveResult>());

    public static ResolveResult resolve_module_with_imports(AModule mod, List<ResolveResult> imported) => ((Func<CollectResult, ResolveResult>)((top) => ((Func<CtorCollectResult, ResolveResult>)((ctors) => ((Func<List<string>, ResolveResult>)((imported_names) => ((Func<List<string>, ResolveResult>)((all_top) => ((Func<Scope, ResolveResult>)((sc) => ((Func<List<Diagnostic>, ResolveResult>)((expr_errs) => new ResolveResult(errors: Enumerable.Concat(top.errors, expr_errs).ToList(), top_level_names: top.names, type_names: ctors.type_names, ctor_names: ctors.ctor_names)))(resolve_all_defs(sc, mod.defs, 0, ((long)mod.defs.Count), new List<Diagnostic>()))))(build_all_names_scope(all_top, ctors.ctor_names, builtin_names()))))(Enumerable.Concat(top.names, imported_names).ToList())))(collect_imported_names(imported, 0, ((long)imported.Count), new List<string>()))))(collect_ctor_names(mod.type_defs, 0, ((long)mod.type_defs.Count), new List<string>(), new List<string>()))))(collect_top_level_names(mod.defs, 0, ((long)mod.defs.Count), new List<string>(), new List<Diagnostic>()));

    public static List<string> collect_imported_names(List<ResolveResult> results, long i, long len, List<string> acc)
    {
        while (true)
        {
            if ((i() == len))
            {
            return acc();
            }
            else
            {
            var r = results[(int)i()];
            var names = Enumerable.Concat(r.top_level_names, r.ctor_names).ToList();
            var _tco_0 = results;
            var _tco_1 = (i() + 1);
            var _tco_2 = len;
            var _tco_3 = Enumerable.Concat(acc(), names).ToList();
            results = _tco_0;
            i = _tco_1;
            len = _tco_2;
            acc = _tco_3;
            continue;
            }
        }
    }

    public static long cc_newline() => 10;

    public static long cc_space() => 2;

    public static long cc_double_quote() => 72;

    public static long cc_single_quote() => 71;

    public static long cc_ampersand() => 84;

    public static long cc_left_paren() => 74;

    public static long cc_right_paren() => 75;

    public static long cc_star() => 78;

    public static long cc_plus() => 76;

    public static long cc_comma() => 66;

    public static long cc_minus() => 73;

    public static long cc_dot() => 65;

    public static long cc_slash() => 81;

    public static long cc_zero() => 3;

    public static long cc_nine() => 12;

    public static long cc_colon() => 69;

    public static long cc_less() => 79;

    public static long cc_equals() => 77;

    public static long cc_greater() => 80;

    public static long cc_upper_a() => 41;

    public static long cc_upper_z() => 64;

    public static long cc_left_bracket() => 88;

    public static long cc_backslash() => 86;

    public static long cc_right_bracket() => 89;

    public static long cc_caret() => 224;

    public static long cc_underscore() => 85;

    public static long cc_lower_a() => 15;

    public static long cc_lower_n() => 18;

    public static long cc_lower_r() => 21;

    public static long cc_lower_t() => 14;

    public static long cc_lower_z() => 38;

    public static long cc_left_brace() => 90;

    public static long cc_pipe() => 87;

    public static long cc_right_brace() => 91;

    public static bool is_letter_code(long c) => (c >= 13L && c <= 64L);

    public static bool is_digit_code(long c) => (c >= 3L && c <= 12L);

    public static LexState make_lex_state(string src) => new LexState(source: src, offset: 0, line: 1, column: 1);

    public static bool is_at_end(LexState st) => (st().offset >= ((long)st().source.Length));

    public static long peek_code(LexState st) => (is_at_end(st()) ? 0 : ((long)st().source[(int)st().offset]));

    public static LexState advance_char(LexState st) => ((peek_code(st()) == cc_newline()) ? new LexState(source: st().source, offset: (st().offset + 1), line: (st().line + 1), column: 1) : new LexState(source: st().source, offset: (st().offset + 1), line: st().line, column: (st().column + 1)));

    public static long skip_spaces_end(string source, long offset, long len)
    {
        while (true)
        {
            if ((offset() >= len))
            {
            return offset();
            }
            else
            {
            if ((((long)source[(int)offset()]) == cc_space()))
            {
            var _tco_0 = source;
            var _tco_1 = (offset() + 1);
            var _tco_2 = len;
            source = _tco_0;
            offset = _tco_1;
            len = _tco_2;
            continue;
            }
            else
            {
            return offset();
            }
            }
        }
    }

    public static LexState skip_spaces(LexState st) => ((Func<long, LexState>)((len) => ((Func<long, LexState>)((end) => ((end == st().offset) ? st() : new LexState(source: st().source, offset: end, line: st().line, column: (st().column + (end - st().offset))))))(skip_spaces_end(st().source, st().offset, len))))(((long)st().source.Length));

    public static long scan_ident_end(string source, long offset, long len)
    {
        while (true)
        {
            if ((offset() >= len))
            {
            return offset();
            }
            else
            {
            var c = ((long)source[(int)offset()]);
            if (is_letter_code(c))
            {
            var _tco_0 = source;
            var _tco_1 = (offset() + 1);
            var _tco_2 = len;
            source = _tco_0;
            offset = _tco_1;
            len = _tco_2;
            continue;
            }
            else
            {
            if (is_digit_code(c))
            {
            var _tco_0 = source;
            var _tco_1 = (offset() + 1);
            var _tco_2 = len;
            source = _tco_0;
            offset = _tco_1;
            len = _tco_2;
            continue;
            }
            else
            {
            if ((c == cc_underscore()))
            {
            var _tco_0 = source;
            var _tco_1 = (offset() + 1);
            var _tco_2 = len;
            source = _tco_0;
            offset = _tco_1;
            len = _tco_2;
            continue;
            }
            else
            {
            if ((c == cc_minus()))
            {
            if (((offset() + 1) >= len))
            {
            return offset();
            }
            else
            {
            if (is_letter_code(((long)source[(int)(offset() + 1)])))
            {
            var _tco_0 = source;
            var _tco_1 = (offset() + 1);
            var _tco_2 = len;
            source = _tco_0;
            offset = _tco_1;
            len = _tco_2;
            continue;
            }
            else
            {
            return offset();
            }
            }
            }
            else
            {
            return offset();
            }
            }
            }
            }
            }
        }
    }

    public static LexState scan_ident_rest(LexState st) => ((Func<long, LexState>)((len) => ((Func<long, LexState>)((end) => ((end == st().offset) ? st() : new LexState(source: st().source, offset: end, line: st().line, column: (st().column + (end - st().offset))))))(scan_ident_end(st().source, st().offset, len))))(((long)st().source.Length));

    public static long scan_digits_end(string source, long offset, long len)
    {
        while (true)
        {
            if ((offset() >= len))
            {
            return offset();
            }
            else
            {
            var c = ((long)source[(int)offset()]);
            if (is_digit_code(c))
            {
            var _tco_0 = source;
            var _tco_1 = (offset() + 1);
            var _tco_2 = len;
            source = _tco_0;
            offset = _tco_1;
            len = _tco_2;
            continue;
            }
            else
            {
            if ((c == cc_underscore()))
            {
            var _tco_0 = source;
            var _tco_1 = (offset() + 1);
            var _tco_2 = len;
            source = _tco_0;
            offset = _tco_1;
            len = _tco_2;
            continue;
            }
            else
            {
            return offset();
            }
            }
            }
        }
    }

    public static LexState scan_digits(LexState st) => ((Func<long, LexState>)((len) => ((Func<long, LexState>)((end) => ((end == st().offset) ? st() : new LexState(source: st().source, offset: end, line: st().line, column: (st().column + (end - st().offset))))))(scan_digits_end(st().source, st().offset, len))))(((long)st().source.Length));

    public static long scan_string_end(string source, long offset, long len)
    {
        while (true)
        {
            if ((offset() >= len))
            {
            return offset();
            }
            else
            {
            var c = ((long)source[(int)offset()]);
            if ((c == cc_double_quote()))
            {
            return (offset() + 1);
            }
            else
            {
            if ((c == cc_newline()))
            {
            return offset();
            }
            else
            {
            if ((c == cc_backslash()))
            {
            var _tco_0 = source;
            var _tco_1 = (offset() + 2);
            var _tco_2 = len;
            source = _tco_0;
            offset = _tco_1;
            len = _tco_2;
            continue;
            }
            else
            {
            var _tco_0 = source;
            var _tco_1 = (offset() + 1);
            var _tco_2 = len;
            source = _tco_0;
            offset = _tco_1;
            len = _tco_2;
            continue;
            }
            }
            }
            }
        }
    }

    public static LexState scan_string_body(LexState st) => ((Func<long, LexState>)((len) => ((Func<long, LexState>)((end) => ((end == st().offset) ? st() : new LexState(source: st().source, offset: end, line: st().line, column: (st().column + (end - st().offset))))))(scan_string_end(st().source, st().offset, len))))(((long)st().source.Length));

    public static string process_escapes(string s, long i, long len, string acc)
    {
        while (true)
        {
            if ((i() >= len))
            {
            return acc();
            }
            else
            {
            var c = ((long)s[(int)i()]);
            if ((c == cc_backslash()))
            {
            if (((i() + 1) < len))
            {
            var nc = ((long)s[(int)(i() + 1)]);
            if ((nc == cc_lower_n()))
            {
            var _tco_0 = s;
            var _tco_1 = (i() + 2);
            var _tco_2 = len;
            var _tco_3 = (acc() + ((char)10).ToString());
            s = _tco_0;
            i = _tco_1;
            len = _tco_2;
            acc = _tco_3;
            continue;
            }
            else
            {
            if ((nc == cc_lower_t()))
            {
            var _tco_0 = s;
            var _tco_1 = (i() + 2);
            var _tco_2 = len;
            var _tco_3 = (acc() + "\u0002\u0002");
            s = _tco_0;
            i = _tco_1;
            len = _tco_2;
            acc = _tco_3;
            continue;
            }
            else
            {
            if ((nc == cc_lower_r()))
            {
            var _tco_0 = s;
            var _tco_1 = (i() + 2);
            var _tco_2 = len;
            var _tco_3 = acc();
            s = _tco_0;
            i = _tco_1;
            len = _tco_2;
            acc = _tco_3;
            continue;
            }
            else
            {
            if ((nc == cc_backslash()))
            {
            var _tco_0 = s;
            var _tco_1 = (i() + 2);
            var _tco_2 = len;
            var _tco_3 = (acc() + "\");
            s = _tco_0;
            i = _tco_1;
            len = _tco_2;
            acc = _tco_3;
            continue;
            }
            else
            {
            if ((nc == cc_double_quote()))
            {
            var _tco_0 = s;
            var _tco_1 = (i() + 2);
            var _tco_2 = len;
            var _tco_3 = (acc() + """);
            s = _tco_0;
            i = _tco_1;
            len = _tco_2;
            acc = _tco_3;
            continue;
            }
            else
            {
            var _tco_0 = s;
            var _tco_1 = (i() + 2);
            var _tco_2 = len;
            var _tco_3 = (acc() + ((char)((long)s[(int)(i() + 1)])).ToString());
            s = _tco_0;
            i = _tco_1;
            len = _tco_2;
            acc = _tco_3;
            continue;
            }
            }
            }
            }
            }
            }
            else
            {
            return (acc() + ((char)((long)s[(int)i()])).ToString());
            }
            }
            else
            {
            var _tco_0 = s;
            var _tco_1 = (i() + 1);
            var _tco_2 = len;
            var _tco_3 = (acc() + ((char)((long)s[(int)i()])).ToString());
            s = _tco_0;
            i = _tco_1;
            len = _tco_2;
            acc = _tco_3;
            continue;
            }
            }
        }
    }

    public static TokenKind classify_word(string w) => ((w == "\u0017\u000D\u000E") ? new LetKeyword() : ((w == "\u0011\u0012") ? new InKeyword() : ((w == "\u0011\u001C") ? new IfKeyword() : ((w == "\u000E\u0014\u000D\u0012") ? ThenKeyword : ((w == "\u000D\u0017\u0013\u000D") ? ElseKeyword() : ((w == "\u001B\u0014\u000D\u0012") ? new WhenKeyword() : ((w == "\u001B\u0014\u000D\u0015\u000D") ? new WhereKeyword() : ((w == "\u0016\u0010") ? new DoKeyword() : ((w == "\u0015\u000D\u0018\u0010\u0015\u0016") ? new RecordKeyword() : ((w == "\u0011\u001A\u001F\u0010\u0015\u000E") ? new ImportKeyword() : ((w == "\u000Dx\u001F\u0010\u0015\u000E") ? ExportKeyword : ((w == "\u0018\u0017\u000F\u0011\u001A") ? new ClaimKeyword() : ((w == "\u001F\u0015\u0010\u0010\u001C") ? new ProofKeyword() : ((w == "\u001C\u0010\u0015\u000F\u0017\u0017") ? new ForAllKeyword() : ((w == "\u000Dx\u0011\u0013\u000E\u0013") ? ThereExistsKeyword : ((w == "\u0017\u0011\u0012\u000D\u000F\u0015") ? new LinearKeyword() : ((w == "\u000D\u001C\u001C\u000D\u0018\u000E") ? EffectKeyword : ((w == "\u001B\u0011\u000E\u0014") ? new WithKeyword() : ((w == "T\u0015\u0019\u000D") ? TrueKeyword : ((w == "F\u000F\u0017\u0013\u000D") ? new FalseKeyword() : ((Func<long, TokenKind>)((first_code) => ((first_code >= cc_upper_a()) ? ((first_code <= cc_upper_z()) ? TypeIdentifier() : new Identifier()) : new Identifier())))(((long)w[(int)0]))))))))))))))))))))));

    public static Token make_token(TokenKind kind, string text, LexState st) => Token();

    public static T48 kind<T48>() => kind();

    public static string text() => text();

    public static T3862 offset<T3862>() => st().offset;

    public static T3864 line<T3864>() => st().line;

    public static T3866 column<T3866>() => st().column;

    public static string extract_text(LexState st, long start, LexState end_st) => st().source.Substring((int)start, (int)(end_st.offset - start));

    public static LexResult scan_token(LexState st) => ((Func<LexState, LexResult>)((s) => (is_at_end(s) ? new LexEnd() : ((Func<long, LexResult>)((c) => ((c == cc_newline()) ? new LexToken(make_token(new Newline(), "\u0001", s), advance_char(s)) : ((c == cc_double_quote()) ? ((Func<long, LexResult>)((start) => ((Func<LexState, LexResult>)((after) => ((Func<long, LexResult>)((text_len) => ((Func<string, LexResult>)((raw) => new LexToken(make_token(TextLiteral, process_escapes(raw, 0, ((long)raw.Length), ""), s), after)))(s.source.Substring((int)start, (int)text_len))))(((after.offset - start) - 1))))(scan_string_body(advance_char(s)))))((s.offset + 1)) : ((c == cc_single_quote()) ? scan_char_literal(s) : (is_letter_code(c) ? ((Func<long, LexResult>)((start) => ((Func<LexState, LexResult>)((after) => ((Func<string, LexResult>)((word) => new LexToken(make_token(classify_word(word), word, s), after)))(extract_text(s, start, after))))(scan_ident_rest(advance_char(s)))))(s.offset) : ((c == cc_underscore()) ? ((Func<long, LexResult>)((start) => ((Func<LexState, LexResult>)((after) => ((Func<string, LexResult>)((word) => ((((long)word.Length) == 1) ? new LexToken(make_token(new Underscore(), "_", s), after) : new LexToken(make_token(classify_word(word), word, s), after))))(extract_text(s, start, after))))(scan_ident_rest(advance_char(s)))))(s.offset) : (is_digit_code(c) ? ((Func<long, LexResult>)((start) => ((Func<LexState, LexResult>)((after) => (is_at_end(after) ? new LexToken(make_token(new IntegerLiteral(), extract_text(s, start, after), s), after) : ((peek_code(after) == cc_dot()) ? ((Func<LexState, LexResult>)((after2) => new LexToken(make_token(new NumberLiteral(), extract_text(s, start, after2), s), after2)))(scan_digits(advance_char(after))) : new LexToken(make_token(new IntegerLiteral(), extract_text(s, start, after), s), after)))))(scan_digits(advance_char(s)))))(s.offset) : scan_operator(s)))))))))(peek_code(s)))))(skip_spaces(st()));

    public static LexResult scan_operator(LexState s) => ((Func<long, LexResult>)((c) => ((Func<LexState, LexResult>)((next) => ((c == cc_left_paren()) ? new LexToken(make_token(new LeftParen(), "(", s), next) : ((c == cc_right_paren()) ? new LexToken(make_token(new RightParen(), ")", s), next) : ((c == cc_left_bracket()) ? new LexToken(make_token(new LeftBracket(), "[", s), next) : ((c == cc_right_bracket()) ? new LexToken(make_token(new RightBracket(), "]", s), next) : ((c == cc_left_brace()) ? new LexToken(make_token(new LeftBrace(), "{", s), next) : ((c == cc_right_brace()) ? new LexToken(make_token(new RightBrace(), "}", s), next) : ((c == cc_comma()) ? new LexToken(make_token(new Comma(), ",", s), next) : ((c == cc_dot()) ? new LexToken(make_token(new Dot(), ".", s), next) : ((c == cc_caret()) ? new LexToken(make_token(new Caret(), "\u00E0\u0081\u009E", s), next) : ((c == cc_ampersand()) ? new LexToken(make_token(new Ampersand(), "&", s), next) : ((c == cc_backslash()) ? new LexToken(make_token(new Backslash(), "\", s), next) : scan_multi_char_operator(s))))))))))))))(advance_char(s))))(peek_code(s));

    public static LexResult scan_multi_char_operator(LexState s) => ((Func<long, LexResult>)((c) => ((Func<LexState, LexResult>)((next) => ((Func<long, LexResult>)((nc) => ((c == cc_plus()) ? ((nc == cc_plus()) ? new LexToken(make_token(new PlusPlus(), "++", s), advance_char(next)) : new LexToken(make_token(new Plus(), "+", s), next)) : ((c == cc_minus()) ? ((nc == cc_greater()) ? new LexToken(make_token(new Arrow(), "->", s), advance_char(next)) : new LexToken(make_token(new Minus(), "-", s), next)) : ((c == cc_star()) ? new LexToken(make_token(new Star(), "*", s), next) : ((c == cc_slash()) ? ((nc == cc_equals()) ? new LexToken(make_token(new NotEquals(), "/=", s), advance_char(next)) : new LexToken(make_token(new Slash(), "/", s), next)) : ((c == cc_equals()) ? ((nc == cc_equals()) ? ((Func<LexState, LexResult>)((next2) => ((Func<long, LexResult>)((nc2) => ((nc2 == cc_equals()) ? new LexToken(make_token(TripleEquals(), "===", s), advance_char(next2)) : new LexToken(make_token(new DoubleEquals(), "==", s), next2))))((is_at_end(next2) ? 0 : peek_code(next2)))))(advance_char(next)) : new LexToken(make_token(Equals_, "=", s), next)) : ((c == cc_colon()) ? ((nc == cc_colon()) ? new LexToken(make_token(new ColonColon(), "::", s), advance_char(next)) : new LexToken(make_token(new Colon(), ":", s), next)) : ((c == cc_pipe()) ? ((nc == cc_minus()) ? new LexToken(make_token(Turnstile, "|-", s), advance_char(next)) : new LexToken(make_token(new Pipe(), "|", s), next)) : ((c == cc_less()) ? ((nc == cc_equals()) ? new LexToken(make_token(new LessOrEqual(), "<=", s), advance_char(next)) : ((nc == cc_minus()) ? new LexToken(make_token(new LeftArrow(), "<-", s), advance_char(next)) : new LexToken(make_token(new LessThan(), "<", s), next))) : ((c == cc_greater()) ? ((nc == cc_equals()) ? new LexToken(make_token(new GreaterOrEqual(), ">=", s), advance_char(next)) : new LexToken(make_token(new GreaterThan(), ">", s), next)) : new LexToken(make_token(ErrorToken(), ((char)((long)s.source[(int)s.offset])).ToString(), s), next))))))))))))((is_at_end(next) ? 0 : peek_code(next)))))(advance_char(s))))(peek_code(s));

    public static LexResult scan_char_literal(LexState s) => ((Func<LexState, LexResult>)((s1) => (is_at_end(s1) ? new LexToken(make_token(ErrorToken(), "'", s), s1) : ((peek_code(s1) == cc_backslash()) ? ((Func<LexState, LexResult>)((s2) => (is_at_end(s2) ? new LexToken(make_token(ErrorToken(), "'\", s), s2) : ((Func<long, LexResult>)((esc_code) => ((Func<long, LexResult>)((char_val) => ((Func<LexState, LexResult>)((s3) => ((Func<LexState, LexResult>)((s4) => new LexToken(make_token(new CharLiteral(), _Cce.FromUnicode(char_val.ToString()), s), s4)))((is_at_end(s3) ? s3 : ((peek_code(s3) == cc_single_quote()) ? advance_char(s3) : s3)))))(advance_char(s2))))(((esc_code == cc_lower_n()) ? 10 : ((esc_code == cc_lower_t()) ? 32 : ((esc_code == cc_lower_r()) ? 10 : ((esc_code == cc_backslash()) ? cc_backslash() : ((esc_code == cc_single_quote()) ? cc_single_quote() : esc_code))))))))(peek_code(s2)))))(advance_char(s1)) : ((Func<long, LexResult>)((char_val) => ((Func<LexState, LexResult>)((s2) => ((Func<LexState, LexResult>)((s3) => new LexToken(make_token(new CharLiteral(), _Cce.FromUnicode(char_val.ToString()), s), s3)))((is_at_end(s2) ? s2 : ((peek_code(s2) == cc_single_quote()) ? advance_char(s2) : s2)))))(advance_char(s1))))(peek_code(s1))))))(advance_char(s));

    public static List<Token> tokenize_loop(LexState st, List<Token> acc)
    {
        while (true)
        {
            var _tco_s = scan_token(st());
            if (_tco_s is LexToken _tco_m0)
            {
                var tok = _tco_m0.Field0;
                var next = _tco_m0.Field1;
            if ((tok.kind == EndOfFile))
            {
            return ((Func<List<Token>>)(() => { var _l = acc(); _l.Add(tok); return _l; }))();
            }
            else
            {
            var _tco_0 = next;
            var _tco_1 = ((Func<List<Token>>)(() => { var _l = acc(); _l.Add(tok); return _l; }))();
            st = _tco_0;
            acc = _tco_1;
            continue;
            }
            }
            else if (_tco_s is LexEnd _tco_m1)
            {
            return ((Func<List<Token>>)(() => { var _l = acc(); _l.Add(make_token(EndOfFile, "", st())); return _l; }))();
            }
        }
    }

    public static List<Token> tokenize(string src) => tokenize_loop(make_lex_state(src), new List<Token>());

    public static ParseTypeResult parse_type(ParseState st) => ((Func<ParseTypeResult, ParseTypeResult>)((result) => unwrap_type_ok(result, (_p0_) => (_p1_) => parse_type_continue(_p0_, _p1_))))(parse_type_atom(st()));

    public static ParseTypeResult parse_type_continue(TypeExpr left, ParseState st) => (is_arrow(current_kind(st())) ? ((Func<object, ParseTypeResult>)((st2) => ((Func<ParseTypeResult, ParseTypeResult>)((right_result) => unwrap_type_ok(right_result, (_p0_) => (_p1_) => make_fun_type(left, _p0_, _p1_))))(parse_type(st2()))))(advance()(st())) : TypeOk(left)(st()));

    public static ParseTypeResult make_fun_type(TypeExpr left, TypeExpr right, ParseState st) => TypeOk(new FunType(left, right))(st());

    public static ParseTypeResult unwrap_type_ok(ParseTypeResult r, Func<TypeExpr, Func<ParseState, ParseTypeResult>> f) => r switch { object TypeOk => t, };

    public static T4211 st<T4211>() => /* error: -> */ default(f)(t)(st());

    public static ParseTypeResult parse_type_atom(ParseState st) => (is_ident(current_kind(st())) ? ((Func<Token, ParseTypeResult>)((tok) => parse_type_args(new NamedType(tok), advance()(st()))))(current(st())) : (is_type_ident(current_kind(st())) ? ((Func<Token, ParseTypeResult>)((tok) => parse_type_args(new NamedType(tok), advance()(st()))))(current(st())) : (is_left_paren(current_kind(st())) ? parse_paren_type(advance()(st())) : (is_left_bracket(current_kind(st())) ? parse_effect_type(advance()(st())) : ((Func<Token, ParseTypeResult>)((tok) => TypeOk(new NamedType(tok))(advance()(st()))))(current(st()))))));

    public static ParseTypeResult parse_paren_type(ParseState st) => ((Func<ParseTypeResult, ParseTypeResult>)((inner) => unwrap_type_ok(inner, (_p0_) => (_p1_) => finish_paren_type(_p0_, _p1_))))(parse_type(st()));

    public static ParseTypeResult finish_paren_type(TypeExpr t, ParseState st) => ((Func<ParseState, ParseTypeResult>)((st2) => TypeOk(new ParenType(t))(st2())))(expect(new RightParen(), st()));

    public static ParseTypeResult parse_effect_type(ParseState st) => ((Func<ParseState, ParseTypeResult>)((st2) => parse_type(st2())))(skip_effect_contents(st()));

    public static ParseState skip_effect_contents(ParseState st)
    {
        while (true)
        {
            if (is_done(st()))
            {
            return st();
            }
            else
            {
            if (is_right_bracket(current_kind(st())))
            {
            return advance()(st());
            }
            else
            {
            var _tco_0 = advance()(st());
            st = _tco_0;
            continue;
            }
            }
        }
    }

    public static ParseTypeResult parse_type_args(TypeExpr base_type, ParseState st) => (is_done(st()) ? TypeOk(base_type)(st()) : (is_type_arg_start(current_kind(st())) ? parse_type_arg_next(base_type, st()) : TypeOk(base_type)(st())));

    public static ParseTypeResult parse_type_arg_next(TypeExpr base_type, ParseState st) => ((Func<ParseTypeResult, ParseTypeResult>)((arg_result) => unwrap_type_ok(arg_result, (_p0_) => (_p1_) => continue_type_args(base_type, _p0_, _p1_))))(parse_type_atom(st()));

    public static ParseTypeResult continue_type_args(TypeExpr base_type, TypeExpr arg, ParseState st) => parse_type_args(new AppType(base_type, new List<TypeExpr> { arg }), st());

    public static ParsePatResult parse_pattern(ParseState st) => (is_underscore(current_kind(st())) ? new PatOk(new WildPat(current(st())), advance()(st())) : (is_literal(current_kind(st())) ? new PatOk(new LitPat(current(st())), advance()(st())) : (is_type_ident(current_kind(st())) ? ((Func<Token, ParsePatResult>)((tok) => parse_ctor_pattern_fields(tok, new List<Pat>(), advance()(st()))))(current(st())) : (is_ident(current_kind(st())) ? new PatOk(new VarPat(current(st())), advance()(st())) : new PatOk(new WildPat(current(st())), advance()(st()))))));

    public static ParsePatResult parse_ctor_pattern_fields(Token ctor, List<Pat> acc, ParseState st) => (is_left_paren(current_kind(st())) ? ((Func<object, ParsePatResult>)((st2) => ((Func<ParsePatResult, ParsePatResult>)((sub) => unwrap_pat_ok(sub, (_p0_) => (_p1_) => continue_ctor_fields(ctor(), acc(), _p0_, _p1_))))(parse_pattern(st2()))))(advance()(st())) : new PatOk(new CtorPat(ctor(), acc()), st()));

    public static ParsePatResult continue_ctor_fields(Token ctor, List<Pat> acc, Pat p, ParseState st) => ((Func<ParseState, ParsePatResult>)((st2) => parse_ctor_pattern_fields(ctor(), ((Func<List<Pat>>)(() => { var _l = acc(); _l.Add(p); return _l; }))(), st2())))(expect(new RightParen(), st()));

    public static ParsePatResult unwrap_pat_ok(ParsePatResult r, Func<Pat, Func<ParseState, ParsePatResult>> f) => r switch { PatOk(var p, var st) => f(p)(st()), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static ParseTypeResult parse_type_annotation(ParseState st) => ((Func<object, ParseTypeResult>)((st2) => ((Func<ParseState, ParseTypeResult>)((st3) => parse_type(st3())))(expect(new Colon(), st2()))))(advance()(st()));

    public static ParseDefResult parse_definition(ParseState st) => (is_done(st()) ? new DefNone(st()) : (is_ident(current_kind(st())) ? try_parse_def(st()) : (is_type_ident(current_kind(st())) ? try_parse_def(st()) : new DefNone(st()))));

    public static ParseDefResult try_parse_def(ParseState st) => (is_colon(peek_kind(st(), 1)) ? ((Func<ParseTypeResult, ParseDefResult>)((ann_result) => unwrap_type_for_def(ann_result)))(parse_type_annotation(st())) : parse_def_body_with_ann()(new List<object>())(st()));

    public static ParseDefResult unwrap_type_for_def(ParseTypeResult r) => r switch { object TypeOk => ann_type, };

    public static T4211 st<T4211>() => /* error: -> */ default;

    public static Token name_tok() => Token();

    public static T48 kind<T48>() => new Identifier();

    public static string text() => "";

    public static T3862 offset<T3862>() => 0;

    public static T3864 line<T3864>() => 0;

    public static T3866 column<T3866>() => 0;

    public static List<object> ann() => new List<object> { TypeAnn(), /* error: { */ default(name()), /* error: = */ default(name_tok()), type_expr, /* error: = */ default(ann_type), /* error: } */ default };

    public static ParseState st2() => skip_newlines(st());

    public static ParseState parse_def_body_with_ann() => st2();

    public static ParseState parse_def_body_with_ann(object ann, object st) => ((Func<Token, ParseDefResult>)((name_tok) => ((Func<object, ParseDefResult>)((st2) => parse_def_params_then(ann(), name_tok(), new List<Token>(), st2())))(advance()(st()))))(current(st()));

    public static ParseDefResult parse_def_params_then(List<TypeAnn> ann, Token name_tok, List<Token> acc, ParseState st)
    {
        while (true)
        {
            if (is_left_paren(current_kind(st())))
            {
            var st2 = advance()(st());
            if (is_ident(current_kind(st2())))
            {
            var param = current(st2());
            var st3 = advance()(st2());
            var st4 = expect(new RightParen(), st3());
            var _tco_0 = ann();
            var _tco_1 = name_tok();
            var _tco_2 = ((Func<List<Token>>)(() => { var _l = acc(); _l.Add(param); return _l; }))();
            var _tco_3 = st4;
            ann = _tco_0;
            name_tok = _tco_1;
            acc = _tco_2;
            st = _tco_3;
            continue;
            }
            else
            {
            return finish_def(ann(), name_tok(), acc(), st());
            }
            }
            else
            {
            return finish_def(ann(), name_tok(), acc(), st());
            }
        }
    }

    public static ParseDefResult finish_def(List<TypeAnn> ann, Token name_tok, List<Token> @params, ParseState st) => ((Func<ParseState, ParseDefResult>)((st2) => ((Func<ParseState, ParseDefResult>)((st3) => ((Func<ParseExprResult, ParseDefResult>)((body_result) => unwrap_def_body(body_result, ann(), name_tok(), @params)))(parse_expr(st3()))))(skip_newlines(st2()))))(expect(Equals_, st()));

    public static ParseDefResult unwrap_def_body(ParseExprResult r, List<TypeAnn> ann, Token name_tok, List<Token> @params) => r switch { object ExprOk => b(), };

    public static T4211 st<T4211>() => /* error: -> */ default;

    public static Func<Def, Func<ParseState, ParseDefResult>> DefOk() => new Def(name: name_tok(), @params: @params, ann: ann(), body: b());

    public static T4211 st<T4211>() => is_paren_type_param;

    public static Func<List<Token>, Func<long, ParseState>> ParseState() => new Boolean();

    public static bool is_paren_type_param(object st) => (is_left_paren(current_kind(st())) ? ((Func<TokenKind, bool>)((k1) => (is_ident(k1) ? is_right_paren(peek_kind(st(), 2)) : (is_type_ident(k1) ? is_right_paren(peek_kind(st(), 2)) : false))))(peek_kind(st(), 1)) : false);

    public static bool is_type_param_pattern(ParseState st) => (is_paren_type_param(st()) ? true : is_ident(current_kind(st())));

    public static ParseState parse_type_params(ParseState st, List<Token> acc)
    {
        while (true)
        {
            if (is_paren_type_param(st()))
            {
            var _tco_0 = advance()(advance()(advance()(st())));
            var _tco_1 = ((Func<List<Token>>)(() => { var _l = acc(); _l.Add(st().tokens[(int)(st().pos + 1)]); return _l; }))();
            st = _tco_0;
            acc = _tco_1;
            continue;
            }
            else
            {
            if (is_ident(current_kind(st())))
            {
            var _tco_0 = advance()(st());
            var _tco_1 = ((Func<List<Token>>)(() => { var _l = acc(); _l.Add(current(st())); return _l; }))();
            st = _tco_0;
            acc = _tco_1;
            continue;
            }
            else
            {
            return st();
            }
            }
        }
    }

    public static List<Token> collect_type_params(ParseState st, List<Token> acc)
    {
        while (true)
        {
            if (is_paren_type_param(st()))
            {
            var _tco_0 = advance()(advance()(advance()(st())));
            var _tco_1 = ((Func<List<Token>>)(() => { var _l = acc(); _l.Add(st().tokens[(int)(st().pos + 1)]); return _l; }))();
            st = _tco_0;
            acc = _tco_1;
            continue;
            }
            else
            {
            if (is_ident(current_kind(st())))
            {
            var _tco_0 = advance()(st());
            var _tco_1 = ((Func<List<Token>>)(() => { var _l = acc(); _l.Add(current(st())); return _l; }))();
            st = _tco_0;
            acc = _tco_1;
            continue;
            }
            else
            {
            return acc();
            }
            }
        }
    }

    public static ParseTypeDefResult parse_type_def(ParseState st) => (is_type_ident(current_kind(st())) ? ((Func<Token, ParseTypeDefResult>)((name_tok) => ((Func<object, ParseTypeDefResult>)((st2) => ((Func<List<Token>, ParseTypeDefResult>)((tparams) => ((Func<ParseState, ParseTypeDefResult>)((st3) => (is_equals(current_kind(st3())) ? ((Func<ParseState, ParseTypeDefResult>)((st4) => (is_record_keyword(current_kind(st4)) ? parse_record_type(name_tok(), tparams(), st4) : (is_pipe(current_kind(st4)) ? parse_variant_type(name_tok(), tparams(), st4) : ((is_type_ident(current_kind(st4)) && looks_like_variant(st4)) ? parse_variant_type(name_tok(), tparams(), st4) : TypeDefNone(st()))))))(skip_newlines(advance()(st3()))) : TypeDefNone(st()))))(parse_type_params(st2(), new List<Token>()))))(collect_type_params(st2(), new List<Token>()))))(advance()(st()))))(current(st())) : TypeDefNone(st()));

    public static ParseTypeDefResult parse_record_type(Token name_tok, List<Token> tparams, ParseState st) => ((Func<object, ParseTypeDefResult>)((st2) => ((Func<ParseState, ParseTypeDefResult>)((st3) => ((Func<ParseState, ParseTypeDefResult>)((st4) => parse_record_fields_loop(name_tok(), tparams(), new List<T4507>(), st4)))(skip_newlines(st3()))))(expect(new LeftBrace(), st2()))))(advance()(st()));

    public static ParseTypeDefResult parse_record_fields_loop<T4507>(Token name_tok, List<Token> tparams, List<T4507> acc, ParseState st) => (is_right_brace(current_kind(st())) ? TypeDefOk(TypeDef())(name()) : name_tok());

    public static Func<List<long>, Func<ParseState, Func<T75, T4557>>> type_params<T75, T4557>() => tparams();

    public static T4520 body<T4520>() => new RecordBody(acc());

    public static object advance() => /* error: ) */ default;

    public static T4528 is_ident<T4528>(object current_kind) => /* error: then */ default(parse_one_record_field)(name_tok())(tparams())(acc())(st());

    public static Func<ParseState, ParseTypeDefResult> TypeDefOk(TypeDef TypeDef) => /* error: = */ default(name_tok());

    public static Func<List<long>, Func<ParseState, Func<T75, T4557>>> type_params<T75, T4557>() => tparams();

    public static T4520 body<T4520>() => new RecordBody(acc());

    public static T4211 st<T4211>() => parse_one_record_field;

    public static T6226 Token<T6226>() => new List(Token());

    public static T797 List<T797>() => /* error: -> */ default(new ParseState());

    public static T4544 ParseTypeDefResult<T4544>() => parse_one_record_field(name_tok())(tparams())(acc())(st());

    public static Token field_name() => current(st());

    public static ParseState st2() => advance()(st());

    public static ParseState st3() => expect(new Colon(), st2());

    public static ParseTypeResult field_type_result() => parse_type(st3());

    public static T4557 unwrap_record_field_type<T4557>() => tparams()(acc())(field_name())(field_type_result());

    public static T4557 unwrap_record_field_type<T4557>(object name_tok, object tparams, object acc, object field_name, object r) => r switch { object TypeOk => ft, };

    public static T4211 st<T4211>() => /* error: -> */ default;

    public static RecordFieldDef field() => new RecordFieldDef(name: field_name(), type_expr: ft);

    public static ParseState st2() => skip_newlines(st());

    public static T4574 is_comma<T4574>(object current_kind) => /* error: then */ default((_p0_) => (_p1_) => (_p2_) => (_p3_) => parse_record_fields_loop(_p0_, _p1_, _p2_, _p3_))(name_tok())(tparams())(((Func<List<object>>)(() => { var _l = acc(); _l.Add(field()); return _l; }))())(skip_newlines(advance()(st2())));

    public static Func<Token, Func<List<Token>, Func<List<T4507>, Func<ParseState, ParseTypeDefResult>>>> parse_record_fields_loop<T4507>() => tparams()(((Func<List<object>>)(() => { var _l = acc(); _l.Add(field()); return _l; }))())(st2());

    public static bool looks_like_variant(ParseState st) => looks_like_variant_scan(st().tokens, (st().pos + 1), ((long)st().tokens.Count));

    public static bool looks_like_variant_scan(List<Token> tokens, long i, long len)
    {
        while (true)
        {
            if ((i() >= len))
            {
            return false;
            }
            else
            {
            var k = tokens[(int)i()].kind;
            if (is_pipe(k))
            {
            return true;
            }
            else
            {
            var _tco_s = k;
            if (_tco_s is Newline _tco_m0)
            {
            return false;
            }
            {
                var EndOfFile = _tco_s;
            return false;
            }
            {
            var _tco_0 = tokens;
            var _tco_1 = (i() + 1);
            var _tco_2 = len;
            tokens = _tco_0;
            i = _tco_1;
            len = _tco_2;
            continue;
            }
            }
            }
        }
    }

    public static ParseTypeDefResult parse_variant_type(Token name_tok, List<Token> tparams, ParseState st) => (is_type_ident(current_kind(st())) ? ((Func<Token, ParseTypeDefResult>)((ctor_name) => ((Func<object, ParseTypeDefResult>)((st2) => parse_ctor_fields()(ctor_name)(new List<object>())(st2())(name_tok())(tparams())(new List<object>())))(advance()(st()))))(current(st())) : parse_variant_ctors(name_tok(), tparams(), new List<T4609>(), st()));

    public static ParseTypeDefResult parse_variant_ctors<T4609>(Token name_tok, List<Token> tparams, List<T4609> acc, ParseState st) => (is_pipe(current_kind(st())) ? ((Func<ParseState, ParseTypeDefResult>)((st2) => ((Func<Token, ParseTypeDefResult>)((ctor_name) => ((Func<object, ParseTypeDefResult>)((st3) => parse_ctor_fields()(ctor_name)(new List<object>())(st3())(name_tok())(tparams())(acc())))(advance()(st2()))))(current(st2()))))(skip_newlines(advance()(st()))) : TypeDefOk(TypeDef())(name()));

    public static Token name_tok() => type_params();

    public static CodexType tparams() => body();

    public static object VariantBody() => /* error: } */ default;

    public static T4211 st<T4211>() => parse_ctor_fields();

    public static T6226 Token<T6226>() => new List(TypeExpr());

    public static Func<List<Token>, Func<long, ParseState>> ParseState() => Token();

    public static T797 List<T797>() => /* error: -> */ default(new List())(new VariantCtorDef());

    public static T4544 ParseTypeDefResult<T4544>() => parse_ctor_fields()(ctor_name)(fields)(st())(name_tok())(tparams())(acc());

    public static object is_left_paren(object current_kind) => /* error: then */ default;

    public static ParseTypeResult field_result() => parse_type(advance()(st()));

    public static T4657 unwrap_ctor_field<T4657>() => ctor_name(fields)(name_tok())(tparams())(acc());

    public static ParseState st2() => skip_newlines(st());

    public static VariantCtorDef ctor() => new VariantCtorDef(name: ctor_name, fields: fields);

    public static Func<Token, Func<List<Token>, Func<List<T4609>, Func<ParseState, ParseTypeDefResult>>>> parse_variant_ctors<T4609>() => tparams()(((Func<List<object>>)(() => { var _l = acc(); _l.Add(ctor()); return _l; }))())(st2());

    public static T4657 unwrap_ctor_field<T4657>(object r, object ctor_name, object fields, object name_tok, object tparams, object acc) => r switch { object TypeOk => ty(), };

    public static T4211 st<T4211>() => /* error: -> */ default;

    public static ParseState st2() => expect(new RightParen(), st());

    public static T4676 parse_ctor_fields<T4676>() => Enumerable.Concat(fields, new List<Func<CodexType, CodexType>> { ty() }).ToList()(st2())(name_tok())(tparams())(acc());

    public static Document parse_document(ParseState st) => ((Func<ParseState, Document>)((st2) => ((Func<ImportParseResult, Document>)((imp_result) => parse_top_level(new List<Def>(), new List<TypeDef>(), new List<EffectDef>(), imp_result.imports, imp_result.state)))(parse_imports(st2(), new List<ImportDecl>()))))(skip_newlines(st()));

    public static ImportParseResult parse_imports(ParseState st, List<ImportDecl> acc)
    {
        while (true)
        {
            if (is_import_keyword(current_kind(st())))
            {
            var st2 = advance()(st());
            var name_tok = current(st2());
            var st3 = skip_newlines(advance()(st2()));
            var _tco_0 = st3();
            var _tco_1 = ((Func<List<ImportDecl>>)(() => { var _l = acc(); _l.Add(new ImportDecl(module_name: name_tok())); return _l; }))();
            st = _tco_0;
            acc = _tco_1;
            continue;
            }
            else
            {
            return new ImportParseResult(imports: acc(), state: st());
            }
        }
    }

    public static Document parse_top_level(List<Def> defs, List<TypeDef> type_defs, List<EffectDef> effect_defs, List<ImportDecl> imports, ParseState st) => (is_done(st()) ? new Document(defs: defs, type_defs: type_defs, effect_defs: effect_defs, imports: imports) : (is_effect_keyword(current_kind(st())) ? parse_top_level_effect(defs, type_defs, effect_defs, imports, st()) : try_top_level_type_def(defs, type_defs, effect_defs, imports, st())));

    public static Document parse_top_level_effect(List<Def> defs, List<TypeDef> type_defs, List<EffectDef> effect_defs, List<ImportDecl> imports, ParseState st) => ((Func<object, Document>)((st1) => ((Func<Token, Document>)((name_tok) => ((Func<object, Document>)((st2) => ((Func<ParseState, Document>)((st3) => ((Func<EffectOpsResult, Document>)((ops) => ((Func<object, Document>)((ed) => /* error: { */ default(name())))(EffectDef())))(parse_effect_ops(st3(), new List<EffectOpDef>()))))((is_where_keyword(current_kind(st2())) ? skip_newlines(advance()(st2())) : st2()))))(advance()(st1))))(current(st1))))(advance()(st()));

    public static Token name_tok() => ops();

    public static string ops() => ops();

    public static Func<List<Def>, Func<List<TypeDef>, Func<List<EffectDef>, Func<List<ImportDecl>, Func<ParseState, Document>>>>> parse_top_level() => type_defs(Enumerable.Concat(effect_defs, new List<object> { ed }).ToList())(imports)(skip_newlines(ops().state));

    public static object EffectOpsResult() => /* error: record */ default;

    public static string ,() => /* error: : */ default(new ParseState());

    public static EffectOpsResult parse_effect_ops(ParseState st, List<EffectOpDef> acc) => (is_ident(current_kind(st())) ? (is_colon(peek_kind(st(), 1)) ? ((Func<Token, EffectOpsResult>)((op_tok) => ((Func<object, EffectOpsResult>)((st2) => ((Func<ParseTypeResult, EffectOpsResult>)((type_result) => type_result switch { object TypeOk => ty(), }))(parse_type(st2()))))(advance()(advance()(st())))))(current(st())) : st3()) : ((Func<object, EffectOpsResult>)((op) => /* error: { */ default(name())))(EffectOpDef()));

    public static object op_tok() => type_expr;

    public static Func<CodexType, CodexType> ty() => /* error: in */ default((_p0_) => (_p1_) => parse_effect_ops(_p0_, _p1_))(skip_newlines(st3()))(((Func<List<object>>)(() => { var _l = acc(); _l.Add(op); return _l; }))());

    public static object EffectOpsResult() => ops();

    public static T772 acc<T772>() => state();

    public static T4211 st<T4211>() => /* error: else */ default(EffectOpsResult());

    public static string ops() => acc();

    public static ParseState state() => st();

    public static Document try_top_level_type_def(List<Def> defs, List<TypeDef> type_defs, List<EffectDef> effect_defs, List<ImportDecl> imports, ParseState st) => ((Func<ParseTypeDefResult, Document>)((td_result) => td_result switch { object TypeDefOk => td, }))(parse_type_def(st()));

    public static ParseState st2() => /* error: -> */ default;

    public static Func<List<Def>, Func<List<TypeDef>, Func<List<EffectDef>, Func<List<ImportDecl>, Func<ParseState, Document>>>>> parse_top_level() => Enumerable.Concat(type_defs, new List<object> { td }).ToList()(effect_defs)(imports)(skip_newlines(st2()));

    public static ParseTypeDefResult TypeDefNone(ParseState st2) => try_top_level_def(defs, type_defs, effect_defs, imports, st());

    public static Document try_top_level_def(List<Def> defs, List<TypeDef> type_defs, List<EffectDef> effect_defs, List<ImportDecl> imports, ParseState st) => ((Func<ParseDefResult, Document>)((def_result) => def_result switch { DefOk(var d, var st2) => parse_top_level(Enumerable.Concat(defs, new List<Def> { d }).ToList(), type_defs, effect_defs, imports, skip_newlines(st2())), DefNone(var st2) => parse_top_level(defs, type_defs, effect_defs, imports, skip_newlines(advance()(st2()))), _ => throw new InvalidOperationException("Non-exhaustive match"), }))(parse_definition(st()));

    public static ParseState make_parse_state(List<Token> toks) => new ParseState(tokens: toks, pos: 0);

    public static Token current(ParseState st) => st().tokens[(int)st().pos];

    public static TokenKind current_kind(ParseState st) => current(st()).kind;

    public static object advance(object st) => ((st().pos >= (((long)st().tokens.Count) - 1)) ? st() : new ParseState(tokens: st().tokens, pos: (st().pos + 1)));

    public static bool is_done(ParseState st) => current_kind(st()) switch { object EndOfFile => true, };

    public static TokenKind peek_kind(ParseState st, long offset) => ((Func<long, TokenKind>)((idx) => ((idx >= ((long)st().tokens.Count)) ? EndOfFile : st().tokens[(int)idx].kind)))((st().pos + offset()));

    public static T4528 is_ident<T4528>(object k) => k switch { Identifier { } => true, _ => false, };

    public static bool is_type_ident(TokenKind k) => k switch { object TypeIdentifier => true, };

    public static bool is_arrow(TokenKind k) => k switch { Arrow { } => true, _ => false, };

    public static bool is_equals(TokenKind k) => k switch { object Equals_ => true, };

    public static bool is_colon(TokenKind k) => k switch { Colon { } => true, _ => false, };

    public static T4574 is_comma<T4574>(object k) => k switch { Comma { } => true, _ => false, };

    public static bool is_pipe(TokenKind k) => k switch { Pipe { } => true, _ => false, };

    public static bool is_dot(TokenKind k) => k switch { Dot { } => true, _ => false, };

    public static object is_left_paren(object k) => k switch { LeftParen { } => true, _ => false, };

    public static bool is_left_brace(TokenKind k) => k switch { LeftBrace { } => true, _ => false, };

    public static bool is_left_bracket(TokenKind k) => k switch { LeftBracket { } => true, _ => false, };

    public static bool is_right_brace(TokenKind k) => k switch { RightBrace { } => true, _ => false, };

    public static bool is_right_bracket(TokenKind k) => k switch { RightBracket { } => true, _ => false, };

    public static bool is_right_paren(TokenKind k) => k switch { RightParen { } => true, _ => false, };

    public static bool is_if_keyword(TokenKind k) => k switch { IfKeyword { } => true, _ => false, };

    public static bool is_let_keyword(TokenKind k) => k switch { LetKeyword { } => true, _ => false, };

    public static bool is_when_keyword(TokenKind k) => k switch { WhenKeyword { } => true, _ => false, };

    public static bool is_do_keyword(TokenKind k) => k switch { DoKeyword { } => true, _ => false, };

    public static bool is_with_keyword(TokenKind k) => k switch { WithKeyword { } => true, _ => false, };

    public static bool is_effect_keyword(TokenKind k) => k switch { object EffectKeyword => true, };

    public static bool is_import_keyword(TokenKind k) => k switch { ImportKeyword { } => true, _ => false, };

    public static bool is_where_keyword(TokenKind k) => k switch { WhereKeyword { } => true, _ => false, };

    public static bool is_in_keyword(TokenKind k) => k switch { InKeyword { } => true, _ => false, };

    public static bool is_minus(TokenKind k) => k switch { Minus { } => true, _ => false, };

    public static bool is_dedent(TokenKind k) => k switch { Dedent { } => true, _ => false, };

    public static bool is_left_arrow(TokenKind k) => k switch { LeftArrow { } => true, _ => false, };

    public static bool is_record_keyword(TokenKind k) => k switch { RecordKeyword { } => true, _ => false, };

    public static bool is_underscore(TokenKind k) => k switch { Underscore { } => true, _ => false, };

    public static bool is_backslash(TokenKind k) => k switch { Backslash { } => true, _ => false, };

    public static bool is_literal(TokenKind k) => k switch { IntegerLiteral { } => true, NumberLiteral { } => true, object TextLiteral => true, };

    public static bool is_app_start(TokenKind k) => k switch { Identifier { } => true, object TypeIdentifier => true, };

    public static bool is_compound(Expr e) => e() switch { MatchExpr(var s, var arms) => true, IfExpr(var c, var t, var el) => true, LetExpr(var binds, var body) => true, DoExpr(var stmts) => true, _ => false, };

    public static bool is_type_arg_start(TokenKind k) => k switch { object TypeIdentifier => true, };

    public static long operator_precedence(TokenKind k) => k switch { PlusPlus { } => 5, ColonColon { } => 5, Plus { } => 6, Minus { } => 6, Star { } => 7, Slash { } => 7, Caret { } => 8, DoubleEquals { } => 4, NotEquals { } => 4, LessThan { } => 4, GreaterThan { } => 4, LessOrEqual { } => 4, GreaterOrEqual { } => 4, object TripleEquals => 4, };

    public static bool is_right_assoc(TokenKind k) => k switch { PlusPlus { } => true, ColonColon { } => true, Caret { } => true, Arrow { } => true, _ => false, };

    public static ParseState expect(TokenKind kind, ParseState st) => (is_done(st()) ? st() : advance()(st()));

    public static long skip_newlines_pos(List<Token> tokens, long pos, long len)
    {
        while (true)
        {
            if ((pos() >= (len - 1)))
            {
            return pos();
            }
            else
            {
            var kind = tokens[(int)pos()].kind;
            var _tco_s = kind();
            if (_tco_s is Newline _tco_m0)
            {
            var _tco_0 = tokens;
            var _tco_1 = (pos() + 1);
            var _tco_2 = len;
            tokens = _tco_0;
            pos = _tco_1;
            len = _tco_2;
            continue;
            }
            else if (_tco_s is Indent _tco_m1)
            {
            var _tco_0 = tokens;
            var _tco_1 = (pos() + 1);
            var _tco_2 = len;
            tokens = _tco_0;
            pos = _tco_1;
            len = _tco_2;
            continue;
            }
            else if (_tco_s is Dedent _tco_m2)
            {
            var _tco_0 = tokens;
            var _tco_1 = (pos() + 1);
            var _tco_2 = len;
            tokens = _tco_0;
            pos = _tco_1;
            len = _tco_2;
            continue;
            }
            {
            return pos();
            }
            }
        }
    }

    public static ParseState skip_newlines(ParseState st) => ((Func<long, ParseState>)((end_pos) => ((end_pos == st().pos) ? st() : new ParseState(tokens: st().tokens, pos: end_pos))))(skip_newlines_pos(st().tokens, st().pos, ((long)st().tokens.Count)));

    public static ParseExprResult parse_expr(ParseState st) => parse_binary(st(), 0);

    public static ParseExprResult unwrap_expr_ok(ParseExprResult r, Func<Expr, Func<ParseState, ParseExprResult>> f) => r switch { object ExprOk => e(), };

    public static T4211 st<T4211>() => /* error: -> */ default(f)(e())(st());

    public static ParseExprResult parse_binary(ParseState st, long min_prec) => ((Func<ParseExprResult, ParseExprResult>)((left_result) => unwrap_expr_ok(left_result, (_p0_) => (_p1_) => start_binary_loop(min_prec, _p0_, _p1_))))(parse_unary(st()));

    public static ParseExprResult start_binary_loop(long min_prec, Expr left, ParseState st) => parse_binary_loop(left, st(), min_prec);

    public static ParseExprResult parse_binary_loop(Expr left, ParseState st, long min_prec) => (is_done(st()) ? ExprOk(left)(st()) : ((Func<long, ParseExprResult>)((prec) => ((prec < min_prec) ? ExprOk(left)(st()) : ((Func<Token, ParseExprResult>)((op) => ((Func<ParseState, ParseExprResult>)((st2) => ((Func<long, ParseExprResult>)((next_min) => ((Func<ParseExprResult, ParseExprResult>)((right_result) => unwrap_expr_ok(right_result, (_p0_) => (_p1_) => continue_binary(left, op, min_prec, _p0_, _p1_))))(parse_binary(st2(), next_min))))((is_right_assoc(op.kind) ? prec : (prec + 1)))))(skip_newlines(advance()(st())))))(current(st())))))(operator_precedence(current_kind(st()))));

    public static ParseExprResult continue_binary(Expr left, Token op, long min_prec, Expr right, ParseState st) => parse_binary_loop(new BinExpr(left, op, right), st(), min_prec);

    public static ParseExprResult parse_unary(ParseState st) => (is_minus(current_kind(st())) ? ((Func<Token, ParseExprResult>)((op) => ((Func<ParseExprResult, ParseExprResult>)((result) => unwrap_expr_ok(result, (_p0_) => (_p1_) => finish_unary(op, _p0_, _p1_))))(parse_unary(advance()(st())))))(current(st())) : parse_application(st()));

    public static ParseExprResult finish_unary(Token op, Expr operand, ParseState st) => ExprOk(new UnaryExpr(op, operand))(st());

    public static ParseExprResult parse_application(ParseState st) => ((Func<ParseExprResult, ParseExprResult>)((func_result) => unwrap_expr_ok(func_result, (_p0_) => (_p1_) => parse_app_loop(_p0_, _p1_))))(parse_atom(st()));

    public static ParseExprResult parse_app_loop(Expr func, ParseState st) => (is_compound(func) ? parse_dot_only(func, st()) : (is_done(st()) ? ExprOk(func)(st()) : (is_app_start(current_kind(st())) ? ((Func<ParseExprResult, ParseExprResult>)((arg_result) => unwrap_expr_ok(arg_result, (_p0_) => (_p1_) => continue_app(func, _p0_, _p1_))))(parse_atom(st())) : parse_field_access(func, st()))));

    public static ParseExprResult continue_app(Expr func, Expr arg, ParseState st) => parse_app_loop(new AppExpr(func, arg), st());

    public static ParseExprResult parse_atom(ParseState st) => (is_literal(current_kind(st())) ? ExprOk(new LitExpr(current(st())))(advance()(st())) : (is_ident(current_kind(st())) ? parse_field_access(new NameExpr(current(st())), advance()(st())) : (is_type_ident(current_kind(st())) ? parse_atom_type_ident(st()) : (is_left_paren(current_kind(st())) ? parse_paren_expr(advance()(st())) : (is_left_bracket(current_kind(st())) ? parse_list_expr(st()) : (is_if_keyword(current_kind(st())) ? parse_if_expr(st()) : (is_let_keyword(current_kind(st())) ? parse_let_expr(st()) : (is_when_keyword(current_kind(st())) ? parse_match_expr(st()) : (is_do_keyword(current_kind(st())) ? parse_do_expr(st()) : (is_with_keyword(current_kind(st())) ? parse_handle_expr(st()) : (is_backslash(current_kind(st())) ? parse_lambda_expr(st()) : ExprOk(ErrExpr(current(st())))(advance()(st())))))))))))));

    public static ParseExprResult parse_field_access(Expr node, ParseState st)
    {
        while (true)
        {
            if (is_dot(current_kind(st())))
            {
            var st2 = advance()(st());
            var field = current(st2());
            var st3 = advance()(st2());
            var _tco_0 = new FieldExpr(node, field());
            var _tco_1 = st3();
            node = _tco_0;
            st = _tco_1;
            continue;
            }
            else
            {
            return ExprOk(node)(st());
            }
        }
    }

    public static ParseExprResult parse_dot_only(Expr node, ParseState st)
    {
        while (true)
        {
            if (is_dot(current_kind(st())))
            {
            var st2 = advance()(st());
            var field = current(st2());
            var st3 = advance()(st2());
            var _tco_0 = new FieldExpr(node, field());
            var _tco_1 = st3();
            node = _tco_0;
            st = _tco_1;
            continue;
            }
            else
            {
            return ExprOk(node)(st());
            }
        }
    }

    public static ParseExprResult parse_atom_type_ident(ParseState st) => ((Func<Token, ParseExprResult>)((tok) => ((Func<object, ParseExprResult>)((st2) => (is_left_brace(current_kind(st2())) ? parse_record_expr(tok, st2()) : ExprOk(new NameExpr(tok))(st2()))))(advance()(st()))))(current(st()));

    public static ParseExprResult parse_paren_expr(ParseState st) => ((Func<ParseState, ParseExprResult>)((st2) => ((Func<ParseExprResult, ParseExprResult>)((inner) => unwrap_expr_ok(inner, (_p0_) => (_p1_) => finish_paren_expr(_p0_, _p1_))))(parse_expr(st2()))))(skip_newlines(st()));

    public static ParseExprResult finish_paren_expr(Expr e, ParseState st) => ((Func<ParseState, ParseExprResult>)((st2) => ((Func<ParseState, ParseExprResult>)((st3) => ExprOk(new ParenExpr(e()))(st3())))(expect(new RightParen(), st2()))))(skip_newlines(st()));

    public static ParseExprResult parse_record_expr(Token type_name, ParseState st) => ((Func<object, ParseExprResult>)((st2) => ((Func<ParseState, ParseExprResult>)((st3) => parse_record_expr_fields(type_name, new List<RecordFieldExpr>(), st3())))(skip_newlines(st2()))))(advance()(st()));

    public static ParseExprResult parse_record_expr_fields(Token type_name, List<RecordFieldExpr> acc, ParseState st) => (is_right_brace(current_kind(st())) ? ExprOk(new RecordExpr(type_name, acc()))(advance()(st())) : (is_ident(current_kind(st())) ? parse_record_field(type_name, acc(), st()) : ExprOk(new RecordExpr(type_name, acc()))(st())));

    public static ParseExprResult parse_record_field(Token type_name, List<RecordFieldExpr> acc, ParseState st) => ((Func<Token, ParseExprResult>)((field_name) => ((Func<object, ParseExprResult>)((st2) => ((Func<ParseState, ParseExprResult>)((st3) => ((Func<ParseExprResult, ParseExprResult>)((val_result) => unwrap_expr_ok(val_result, (_p0_) => (_p1_) => finish_record_field(type_name, acc(), field_name(), _p0_, _p1_))))(parse_expr(st3()))))(expect(Equals_, st2()))))(advance()(st()))))(current(st()));

    public static ParseExprResult finish_record_field(Token type_name, List<RecordFieldExpr> acc, Token field_name, Expr v, ParseState st) => ((Func<RecordFieldExpr, ParseExprResult>)((field) => ((Func<ParseState, ParseExprResult>)((st2) => (is_comma(current_kind(st2())) ? parse_record_expr_fields(type_name, ((Func<List<RecordFieldExpr>>)(() => { var _l = acc(); _l.Add(field()); return _l; }))(), skip_newlines(advance()(st2()))) : parse_record_expr_fields(type_name, ((Func<List<RecordFieldExpr>>)(() => { var _l = acc(); _l.Add(field()); return _l; }))(), st2()))))(skip_newlines(st()))))(new RecordFieldExpr(name: field_name(), value: v));

    public static ParseExprResult parse_list_expr(ParseState st) => ((Func<object, ParseExprResult>)((st2) => ((Func<ParseState, ParseExprResult>)((st3) => parse_list_elements(new List<Expr>(), st3())))(skip_newlines(st2()))))(advance()(st()));

    public static ParseExprResult parse_list_elements(List<Expr> acc, ParseState st) => (is_right_bracket(current_kind(st())) ? ExprOk(new ListExpr(acc()))(advance()(st())) : ((Func<ParseExprResult, ParseExprResult>)((elem) => unwrap_expr_ok(elem, (_p0_) => (_p1_) => finish_list_element(acc(), _p0_, _p1_))))(parse_expr(st())));

    public static ParseExprResult finish_list_element(List<Expr> acc, Expr e, ParseState st) => ((Func<ParseState, ParseExprResult>)((st2) => (is_comma(current_kind(st2())) ? parse_list_elements(((Func<List<Expr>>)(() => { var _l = acc(); _l.Add(e()); return _l; }))(), skip_newlines(advance()(st2()))) : parse_list_elements(((Func<List<Expr>>)(() => { var _l = acc(); _l.Add(e()); return _l; }))(), st2()))))(skip_newlines(st()));

    public static ParseExprResult parse_if_expr(ParseState st) => ((Func<ParseState, ParseExprResult>)((st2) => ((Func<ParseExprResult, ParseExprResult>)((cond) => unwrap_expr_ok(cond, (_p0_) => (_p1_) => parse_if_then(_p0_, _p1_))))(parse_expr(st2()))))(skip_newlines(advance()(st())));

    public static ParseExprResult parse_if_then(Expr c, ParseState st) => ((Func<ParseState, ParseExprResult>)((st2) => ((Func<ParseState, ParseExprResult>)((st3) => ((Func<ParseState, ParseExprResult>)((st4) => ((Func<ParseExprResult, ParseExprResult>)((then_result) => unwrap_expr_ok(then_result, (_p0_) => (_p1_) => parse_if_else(c, _p0_, _p1_))))(parse_expr(st4))))(skip_newlines(st3()))))(expect(ThenKeyword, st2()))))(skip_newlines(st()));

    public static ParseExprResult parse_if_else(Expr c, Expr t, ParseState st) => ((Func<ParseState, ParseExprResult>)((st2) => ((Func<ParseState, ParseExprResult>)((st3) => ((Func<ParseState, ParseExprResult>)((st4) => ((Func<ParseExprResult, ParseExprResult>)((else_result) => unwrap_expr_ok(else_result, (_p0_) => (_p1_) => finish_if(c, t, _p0_, _p1_))))(parse_expr(st4))))(skip_newlines(st3()))))(expect(ElseKeyword(), st2()))))(skip_newlines(st()));

    public static ParseExprResult finish_if(Expr c, Expr t, Expr e, ParseState st) => ExprOk(new IfExpr(c, t, e()))(st());

    public static ParseExprResult parse_let_expr(ParseState st) => ((Func<ParseState, ParseExprResult>)((st2) => parse_let_bindings(new List<LetBind>(), st2())))(skip_newlines(advance()(st())));

    public static ParseExprResult parse_let_bindings(List<LetBind> acc, ParseState st) => (is_ident(current_kind(st())) ? parse_let_binding(acc(), st()) : (is_in_keyword(current_kind(st())) ? ((Func<ParseState, ParseExprResult>)((st2) => ((Func<ParseExprResult, ParseExprResult>)((body) => unwrap_expr_ok(body(), (_p0_) => (_p1_) => finish_let(acc(), _p0_, _p1_))))(parse_expr(st2()))))(skip_newlines(advance()(st()))) : ((Func<ParseExprResult, ParseExprResult>)((body) => unwrap_expr_ok(body(), (_p0_) => (_p1_) => finish_let(acc(), _p0_, _p1_))))(parse_expr(st()))));

    public static ParseExprResult finish_let(List<LetBind> acc, Expr b, ParseState st) => ExprOk(new LetExpr(acc(), b()))(st());

    public static ParseExprResult parse_let_binding(List<LetBind> acc, ParseState st) => ((Func<Token, ParseExprResult>)((name_tok) => ((Func<object, ParseExprResult>)((st2) => ((Func<ParseState, ParseExprResult>)((st3) => ((Func<ParseExprResult, ParseExprResult>)((val_result) => unwrap_expr_ok(val_result, (_p0_) => (_p1_) => finish_let_binding(acc(), name_tok(), _p0_, _p1_))))(parse_expr(st3()))))(expect(Equals_, st2()))))(advance()(st()))))(current(st()));

    public static ParseExprResult finish_let_binding(List<LetBind> acc, Token name_tok, Expr v, ParseState st) => ((Func<LetBind, ParseExprResult>)((binding) => ((Func<ParseState, ParseExprResult>)((st2) => (is_comma(current_kind(st2())) ? parse_let_bindings(((Func<List<LetBind>>)(() => { var _l = acc(); _l.Add(binding); return _l; }))(), skip_newlines(advance()(st2()))) : parse_let_bindings(((Func<List<LetBind>>)(() => { var _l = acc(); _l.Add(binding); return _l; }))(), st2()))))(skip_newlines(st()))))(new LetBind(name: name_tok(), value: v));

    public static ParseExprResult parse_match_expr(ParseState st) => ((Func<object, ParseExprResult>)((st2) => ((Func<ParseExprResult, ParseExprResult>)((scrut) => unwrap_expr_ok(scrut, (_p0_) => (_p1_) => start_match_branches(_p0_, _p1_))))(parse_expr(st2()))))(advance()(st()));

    public static ParseExprResult start_match_branches(Expr s, ParseState st) => ((Func<ParseState, ParseExprResult>)((st2) => ((Func<Token, ParseExprResult>)((tok) => parse_match_branches(s, new List<MatchArm>(), tok.column, tok.line, st2())))(current(st2()))))(skip_newlines(st()));

    public static ParseExprResult parse_match_branches(Expr scrut, List<MatchArm> acc, long col, long ln, ParseState st) => (is_if_keyword(current_kind(st())) ? ((Func<Token, ParseExprResult>)((tok) => ((tok.line == ln) ? parse_one_match_branch(scrut, acc(), col, ln, st()) : ((tok.column == col) ? parse_one_match_branch(scrut, acc(), col, ln, st()) : ExprOk(new MatchExpr(scrut, acc()))(st())))))(current(st())) : ExprOk(new MatchExpr(scrut, acc()))(st()));

    public static ParseExprResult unwrap_pat_for_expr(ParsePatResult r, Func<Pat, Func<ParseState, ParseExprResult>> f) => r switch { PatOk(var p, var st) => f(p)(st()), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static ParseExprResult parse_one_match_branch(Expr scrut, List<MatchArm> acc, long col, long ln, ParseState st) => ((Func<object, ParseExprResult>)((st2) => ((Func<ParsePatResult, ParseExprResult>)((pat) => unwrap_pat_for_expr(pat, (_p0_) => (_p1_) => parse_match_branch_body(scrut, acc(), col, ln, _p0_, _p1_))))(parse_pattern(st2()))))(advance()(st()));

    public static ParseExprResult parse_match_branch_body(Expr scrut, List<MatchArm> acc, long col, long ln, Pat p, ParseState st) => ((Func<ParseState, ParseExprResult>)((st2) => ((Func<ParseState, ParseExprResult>)((st3) => ((Func<ParseExprResult, ParseExprResult>)((body) => unwrap_expr_ok(body(), (_p0_) => (_p1_) => finish_match_branch(scrut, acc(), col, ln, p, _p0_, _p1_))))(parse_expr(st3()))))(skip_newlines(st2()))))(expect(new Arrow(), st()));

    public static ParseExprResult finish_match_branch(Expr scrut, List<MatchArm> acc, long col, long ln, Pat p, Expr b, ParseState st) => ((Func<MatchArm, ParseExprResult>)((arm) => ((Func<ParseState, ParseExprResult>)((st2) => parse_match_branches(scrut, ((Func<List<MatchArm>>)(() => { var _l = acc(); _l.Add(arm); return _l; }))(), col, ln, st2())))(skip_newlines(st()))))(new MatchArm(pattern: p, body: b()));

    public static ParseExprResult parse_do_expr(ParseState st) => ((Func<ParseState, ParseExprResult>)((st2) => parse_do_stmts(new List<DoStmt>(), st2())))(skip_newlines(advance()(st())));

    public static ParseExprResult parse_do_stmts(List<DoStmt> acc, ParseState st) => (is_done(st()) ? ExprOk(new DoExpr(acc()))(st()) : (is_dedent(current_kind(st())) ? ExprOk(new DoExpr(acc()))(st()) : (is_do_bind(st()) ? parse_do_bind_stmt(acc(), st()) : parse_do_expr_stmt(acc(), st()))));

    public static bool is_do_bind(ParseState st) => (is_ident(current_kind(st())) ? is_left_arrow(peek_kind(st(), 1)) : false);

    public static ParseExprResult parse_do_bind_stmt(List<DoStmt> acc, ParseState st) => ((Func<Token, ParseExprResult>)((name_tok) => ((Func<object, ParseExprResult>)((st2) => ((Func<ParseExprResult, ParseExprResult>)((val_result) => unwrap_expr_ok(val_result, (_p0_) => (_p1_) => finish_do_bind(acc(), name_tok(), _p0_, _p1_))))(parse_expr(st2()))))(advance()(advance()(st())))))(current(st()));

    public static ParseExprResult finish_do_bind(List<DoStmt> acc, Token name_tok, Expr v, ParseState st) => ((Func<ParseState, ParseExprResult>)((st2) => parse_do_stmts(((Func<List<DoStmt>>)(() => { var _l = acc(); _l.Add(new DoBindStmt(name_tok(), v)); return _l; }))(), st2())))(skip_newlines(st()));

    public static ParseExprResult parse_do_expr_stmt(List<DoStmt> acc, ParseState st) => ((Func<ParseExprResult, ParseExprResult>)((expr_result) => unwrap_expr_ok(expr_result, (_p0_) => (_p1_) => finish_do_expr(acc(), _p0_, _p1_))))(parse_expr(st()));

    public static ParseExprResult finish_do_expr(List<DoStmt> acc, Expr e, ParseState st) => ((Func<ParseState, ParseExprResult>)((st2) => parse_do_stmts(((Func<List<DoStmt>>)(() => { var _l = acc(); _l.Add(new DoExprStmt(e())); return _l; }))(), st2())))(skip_newlines(st()));

    public static ParseExprResult parse_handle_expr(ParseState st) => ((Func<object, ParseExprResult>)((st1) => ((Func<Token, ParseExprResult>)((eff_tok) => ((Func<object, ParseExprResult>)((st2) => ((Func<ParseExprResult, ParseExprResult>)((body_result) => unwrap_expr_ok(body_result, (_p0_) => (_p1_) => finish_handle_body(eff_tok, _p0_, _p1_))))(parse_expr(st2()))))(advance()(st1))))(current(st1))))(advance()(st()));

    public static ParseExprResult finish_handle_body(Token eff_tok, Expr body, ParseState st) => ((Func<ParseState, ParseExprResult>)((st2) => ((Func<T5321, ParseExprResult>)((clauses) => ExprOk(new HandleExpr(eff_tok, body(), clauses.clauses))(clauses.state)))(parse_handle_clauses(st2(), new List<T5320>()))))(skip_newlines(st()));

    public static T5321 parse_handle_clauses<T5320, T5321>(ParseState st, List<T5320> acc) => (is_ident(current_kind(st())) ? ((Func<Token, T5321>)((op_tok) => ((Func<object, T5321>)((st1) => ((Func<HandleParamsResult, T5321>)((@params) => ((((long)@params.toks.Count) > 0) ? ((Func<Token, HandleParseResult>)((resume_tok) => ((Func<ParseState, HandleParseResult>)((st5) => ((Func<ParseState, HandleParseResult>)((st6) => ((Func<ParseExprResult, HandleParseResult>)((body_result) => unwrap_handle_clause_body(op_tok(), resume_tok, body_result, acc())))(parse_expr(st6))))(skip_newlines(st5))))(expect(Equals_, @params.state))))(@params.toks[(int)(((long)@params.toks.Count) - 1)]) : new HandleParseResult(clauses: acc(), state: st()))))(parse_handle_params(st1, new List<Token>()))))(advance()(st()))))(current(st())) : new HandleParseResult(clauses: acc(), state: st()));

    public static HandleParamsResult parse_handle_params(ParseState st, List<Token> acc)
    {
        while (true)
        {
            if (is_left_paren(current_kind(st())))
            {
            var st1 = advance()(st());
            var tok = current(st1);
            var st2 = advance()(st1);
            var st3 = expect(new RightParen(), st2());
            var _tco_0 = st3();
            var _tco_1 = ((Func<List<Token>>)(() => { var _l = acc(); _l.Add(tok); return _l; }))();
            st = _tco_0;
            acc = _tco_1;
            continue;
            }
            else
            {
            return new HandleParamsResult(toks: acc(), state: st());
            }
        }
    }

    public static HandleParseResult unwrap_handle_clause_body(Token op_tok, Token resume_tok, ParseExprResult result, List<HandleClause> acc) => result switch { object ExprOk => body(), };

    public static T4211 st<T4211>() => /* error: -> */ default;

    public static HandleClause clause() => new HandleClause(op_name: op_tok(), resume_name: resume_tok, body: body());

    public static ParseState st2() => skip_newlines(st());

    public static Func<ParseState, Func<List<T5320>, T5321>> parse_handle_clauses<T5320, T5321>() => ((Func<List<object>>)(() => { var _l = acc(); _l.Add(clause()); return _l; }))();

    public static ParseExprResult parse_lambda_expr(ParseState st) => ((Func<object, ParseExprResult>)((st2) => ((Func<LambdaParamsResult, ParseExprResult>)((params_result) => ((Func<ParseState, ParseExprResult>)((st3) => ((Func<ParseState, ParseExprResult>)((st4) => ((Func<ParseExprResult, ParseExprResult>)((body) => unwrap_expr_ok(body(), (_p0_) => (_p1_) => finish_lambda(params_result.toks, _p0_, _p1_))))(parse_expr(st4))))(skip_newlines(st3()))))(expect(new Arrow(), params_result.state))))(collect_lambda_params(st2(), new List<Token>()))))(advance()(st()));

    public static LambdaParamsResult collect_lambda_params(ParseState st, List<Token> acc)
    {
        while (true)
        {
            if (is_ident(current_kind(st())))
            {
            var tok = current(st());
            var _tco_0 = advance()(st());
            var _tco_1 = ((Func<List<Token>>)(() => { var _l = acc(); _l.Add(tok); return _l; }))();
            st = _tco_0;
            acc = _tco_1;
            continue;
            }
            else
            {
            return new LambdaParamsResult(toks: acc(), state: st());
            }
        }
    }

    public static ParseExprResult finish_lambda(List<Token> @params, Expr body, ParseState st) => ExprOk(new LambdaExpr(@params, body()))(st());

    public static T5400 Expr<T5400>() => /* error: | */ default(new LitExpr())(Token());

    public static T5405 NameExpr<T5405>(object Token) => /* error: | */ default(new AppExpr())(Expr())(Expr());

    public static T5412 BinExpr<T5412>(object Expr, object Token, object Expr) => /* error: | */ default(new UnaryExpr())(Token())(Expr());

    public static T5420 IfExpr<T5420>(object Expr, object Expr, object Expr) => /* error: | */ default(new LetExpr())(new List(new LetBind()))(Expr());

    public static T5423 MatchExpr<T5423>(object Expr) => new List(new MatchArm());

    public static T6226 ListExpr<T6226>() => new List(Expr());

    public static T5428 RecordExpr<T5428>(object Token) => new List(new RecordFieldExpr());

    public static T5433 FieldExpr<T5433>(object Expr, object Token) => /* error: | */ default(new ParenExpr())(Expr());

    public static T6226 DoExpr<T6226>() => new List(new DoStmt());

    public static T5439 HandleExpr<T5439>(object Token, object Expr) => new List(new HandleClause());

    public static T6226 LambdaExpr<T6226>() => new List(Token());

    public static T5400 Expr<T5400>() => /* error: | */ default(ErrExpr)(Token());

    public static T5447 TypeExpr<T5447>() => /* error: | */ default(new NamedType())(Token());

    public static T5454 FunType<T5454>(object TypeExpr, object TypeExpr) => /* error: | */ default(new AppType())(TypeExpr())(new List(TypeExpr()));

    public static T5458 ParenType<T5458>(object TypeExpr) => /* error: | */ default(new ListType())(TypeExpr());

    public static T5464 LinearTypeExpr<T5464>(object TypeExpr) => /* error: | */ default(EffectTypeExpr)(new List(Token()))(TypeExpr());

    public static object TypeAnn() => /* error: record */ default;

    public static string ,() => type_expr;

    public static T5447 TypeExpr<T5447>() => /* error: } */ default;

    public static object EffectOpDef() => /* error: record */ default;

    public static string ,() => type_expr;

    public static T5447 TypeExpr<T5447>() => /* error: } */ default;

    public static object EffectDef() => /* error: record */ default;

    public static string ,() => ops();

    public static T797 List<T797>() => /* error: } */ default;

    public static T5474 TypeBody<T5474>() => /* error: | */ default(new RecordBody())(new List(new RecordFieldDef()));

    public static object VariantBody() => new List(new VariantCtorDef());

    public static object TypeDef() => /* error: record */ default;

    public static string ,() => type_params();

    public static T797 List<T797>() => /* error: , */ default;

    public static List<TypeBinding> }() => new ImportDecl();

    public static List<TypeBinding> }() => new Document();

    public static string ,() => type_defs;

    public static T797 List<T797>() => /* error: , */ default;

    public static string ,() => imports;

    public static T797 List<T797>() => /* error: } */ default;

    public static T6226 Token<T6226>() => /* error: record */ default;

    public static string ,() => text();

    public static T50 Text<T50>() => offset();

    public static T51 Integer<T51>() => line();

    public static T51 Integer<T51>() => column();

    public static T51 Integer<T51>() => /* error: } */ default;

    public static long token_length(Token t) => ((long)t.text.Length);

    public static T5489 TokenKind<T5489>() => /* error: | */ default(EndOfFile);

    public static T5491 Newline<T5491>() => /* error: | */ default(new Indent());

    public static T5493 Dedent<T5493>() => /* error: | */ default(new IntegerLiteral());

    public static T5495 NumberLiteral<T5495>() => /* error: | */ default(TextLiteral);

    public static T5497 CharLiteral<T5497>() => /* error: | */ default(TrueKeyword);

    public static T5499 FalseKeyword<T5499>() => /* error: | */ default(new Identifier());

    public static T5501 TypeIdentifier<T5501>() => /* error: | */ default(new ProseText());

    public static T5503 ChapterHeader<T5503>() => /* error: | */ default(new SectionHeader());

    public static T5505 LetKeyword<T5505>() => /* error: | */ default(new InKeyword());

    public static T5507 IfKeyword<T5507>() => /* error: | */ default(ThenKeyword);

    public static T5509 ElseKeyword<T5509>() => /* error: | */ default(new WhenKeyword());

    public static T5511 WhereKeyword<T5511>() => /* error: | */ default(new SuchThatKeyword());

    public static T5513 DoKeyword<T5513>() => /* error: | */ default(new RecordKeyword());

    public static T5515 ImportKeyword<T5515>() => /* error: | */ default(ExportKeyword);

    public static T5517 ClaimKeyword<T5517>() => /* error: | */ default(new ProofKeyword());

    public static T5519 ForAllKeyword<T5519>() => /* error: | */ default(ThereExistsKeyword);

    public static T5521 LinearKeyword<T5521>() => /* error: | */ default(EffectKeyword);

    public static T5523 WithKeyword<T5523>() => /* error: | */ default(Equals_);

    public static T5525 Colon<T5525>() => /* error: | */ default(new Arrow());

    public static T5527 LeftArrow<T5527>() => /* error: | */ default(new Pipe());

    public static T5529 Ampersand<T5529>() => /* error: | */ default(new Plus());

    public static T5531 Minus<T5531>() => /* error: | */ default(new Star());

    public static T5533 Slash<T5533>() => /* error: | */ default(new Caret());

    public static T5535 PlusPlus<T5535>() => /* error: | */ default(new ColonColon());

    public static T5537 DoubleEquals<T5537>() => /* error: | */ default(new NotEquals());

    public static T5539 LessThan<T5539>() => /* error: | */ default(new GreaterThan());

    public static T5541 LessOrEqual<T5541>() => /* error: | */ default(new GreaterOrEqual());

    public static T5543 TripleEquals<T5543>() => /* error: | */ default(Turnstile);

    public static T5545 LinearProduct<T5545>() => /* error: | */ default(new ForAllSymbol());

    public static T5547 ExistsSymbol<T5547>() => /* error: | */ default(new LeftParen());

    public static T5549 RightParen<T5549>() => /* error: | */ default(new LeftBracket());

    public static T5551 RightBracket<T5551>() => /* error: | */ default(new LeftBrace());

    public static T5553 RightBrace<T5553>() => /* error: | */ default(new Comma());

    public static T5555 Dot<T5555>() => /* error: | */ default(new DashGreater());

    public static T5557 Underscore<T5557>() => /* error: | */ default(new Backslash());

    public static Token ErrorToken() => new CodexType();

    public static T5560 IntegerTy<T5560>() => /* error: | */ default(new NumberTy());

    public static T5562 TextTy<T5562>() => /* error: | */ default(new BooleanTy());

    public static T5564 CharTy<T5564>() => /* error: | */ default(new VoidTy());

    public static T5566 NothingTy<T5566>() => /* error: | */ default(ErrorTy());

    public static List<long> FunTy() => new CodexType();

    public static Token CodexType() => /* error: | */ default(new ListTy())(new CodexType());

    public static T265 TypeVar<T265>() => new Integer();

    public static List<long> ForAllTy() => new Integer();

    public static Token CodexType() => /* error: | */ default(new SumTy())(new Name())(new List(new SumCtor()));

    public static string RecordTy() => new Name();

    public static T797 List<T797>() => /* error: ) */ default;

    public static T793 ConstructedTy<T793>() => new Name();

    public static T797 List<T797>() => /* error: ) */ default;

    public static string EffectfulTy() => new List(new Name());

    public static Token CodexType() => new SumCtor();

    public static string ,() => fields;

    public static T797 List<T797>() => /* error: } */ default;

    public static CodexType resolve_type_expr(List<TypeBinding> tdm, ATypeExpr texpr) => texpr switch { ANamedType(var name) => resolve_type_name(tdm, name().value), AFunType(var param, var ret) => new FunTy(resolve_type_expr(tdm, param), resolve_type_expr(tdm, ret())), AAppType(var ctor, var args) => resolve_applied_type(tdm, ctor(), args), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static CodexType resolve_applied_type(List<TypeBinding> tdm, ATypeExpr ctor, List<ATypeExpr> args) => ctor() switch { ANamedType(var name) => ((name().value == "L\u0011\u0013\u000E") ? ((((long)args.Count) == 1) ? new ListTy(resolve_type_expr(tdm, args[(int)0])) : new ListTy(ErrorTy())) : new ConstructedTy(name(), resolve_type_expr_list(tdm, args, 0, ((long)args.Count), new List<CodexType>()))), _ => resolve_type_expr(tdm, ctor()), };

    public static List<CodexType> resolve_type_expr_list(List<TypeBinding> tdm, List<ATypeExpr> args, long i, long len, List<CodexType> acc)
    {
        while (true)
        {
            if ((i() == len))
            {
            return acc();
            }
            else
            {
            var _tco_0 = tdm;
            var _tco_1 = args;
            var _tco_2 = (i() + 1);
            var _tco_3 = len;
            var _tco_4 = ((Func<List<CodexType>>)(() => { var _l = acc(); _l.Add(resolve_type_expr(tdm, args[(int)i()])); return _l; }))();
            tdm = _tco_0;
            args = _tco_1;
            i = _tco_2;
            len = _tco_3;
            acc = _tco_4;
            continue;
            }
        }
    }

    public static CodexType resolve_type_name(List<TypeBinding> tdm, string name) => ((name() == "I\u0012\u000E\u000D\u001D\u000D\u0015") ? new IntegerTy() : ((name() == "N\u0019\u001Ab\u000D\u0015") ? new NumberTy() : ((name() == "T\u000Dx\u000E") ? TextTy() : ((name() == "B\u0010\u0010\u0017\u000D\u000F\u0012") ? new BooleanTy() : ((name() == "C\u0014\u000F\u0015") ? new CharTy() : ((name() == "N\u0010\u000E\u0014\u0011\u0012\u001D") ? new NothingTy() : lookup_type_def(tdm, name())))))));

    public static CodexType lookup_type_def(List<TypeBinding> tdm, string name) => ((Func<long, CodexType>)((len) => ((len == 0) ? new ConstructedTy(new Name(value: name()), new List<object>()) : ((Func<long, CodexType>)((pos) => ((pos() >= len) ? new ConstructedTy(new Name(value: name()), new List<object>()) : ((Func<TypeBinding, CodexType>)((b) => ((b().name == name()) ? b().bound_type : new ConstructedTy(new Name(value: name()), new List<object>()))))(tdm[(int)pos()]))))(bsearch_text_pos(tdm, name(), 0, len)))))(((long)tdm.Count));

    public static bool is_value_name(string name) => ((((long)name().Length) == 0) ? false : ((Func<long, bool>)((code) => ((code >= 97) && (code <= 122))))(((long)name()[(int)0])));

    public static ParamResult parameterize_type(UnificationState st, CodexType ty) => ((Func<WalkResult, ParamResult>)((r) => ((Func<CodexType, ParamResult>)((wrapped) => new ParamResult(parameterized: wrapped, entries: r.entries, state: r.state)))(wrap_forall_from_entries(r.walked, r.entries, 0, ((long)r.entries.Count)))))(parameterize_walk(st(), new List<ParamEntry>(), ty()));

    public static CodexType wrap_forall_from_entries(CodexType ty, List<ParamEntry> entries, long i, long len) => ((i() == len) ? ty() : ((Func<ParamEntry, CodexType>)((e) => new ForAllTy(e().var_id, wrap_forall_from_entries(ty(), entries(), (i() + 1), len))))(entries()[(int)i()]));

    public static WalkResult parameterize_walk(UnificationState st, List<ParamEntry> entries, CodexType ty) => ty() switch { ConstructedTy(var name, var args) => (((((long)args.Count) == 0) && is_value_name(name().value)) ? ((Func<object, WalkResult>)((looked) => ((looked >= 0) ? new WalkResult(walked: TypeVar()(looked), entries: entries(), state: st()) : ((Func<FreshResult, WalkResult>)((fr) => fr().var_type switch { object TypeVar => new_id, }))(fresh_and_advance(st())))))(find_param_entry()(entries())(name().value)(0)(((long)entries().Count))) : /* error: -> */ default), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static ParamEntry new_entry() => new ParamEntry(param_name: name().value, var_id: new_id);

    public static Func<CodexType, Func<List<ParamEntry>, Func<UnificationState, WalkResult>>> WalkResult() => walked;

    public static object fr() => var_type;

    public static List<ParamEntry> entries() => Enumerable.Concat(entries(), new List<ParamEntry> { new_entry() }).ToList();

    public static ParseState state() => fr().state;

    public static Func<CodexType, Func<List<ParamEntry>, Func<UnificationState, WalkResult>>> WalkResult() => walked;

    public static Func<CodexType, CodexType> ty() => entries();

    public static List<ParamEntry> entries() => state();

    public static object fr() => state();

    public static T5715 args_r<T5715>() => parameterize_walk_list(st(), entries(), args, 0, ((long)args.Count), new List<CodexType>());

    public static Func<CodexType, Func<List<ParamEntry>, Func<UnificationState, WalkResult>>> WalkResult() => walked;

    public static T793 ConstructedTy<T793>() => args_r().walked_list;

    public static List<ParamEntry> entries() => args_r().entries;

    public static ParseState state() => args_r().state;

    public static List<long> FunTy(object param, object ret) => ((Func<WalkResult, WalkResult>)((pr) => ((Func<WalkResult, WalkResult>)((rr) => new WalkResult(walked: new FunTy(pr.walked, rr.walked), entries: rr.entries, state: rr.state)))(parameterize_walk(pr.state, pr.entries, ret()))))(parameterize_walk(st(), entries(), param));

    public static List<long> ListTy(object elem) => ((Func<WalkResult, WalkResult>)((er) => new WalkResult(walked: new ListTy(er.walked), entries: er.entries, state: er.state)))(parameterize_walk(st(), entries(), elem));

    public static List<long> ForAllTy(object id, object body) => ((Func<WalkResult, WalkResult>)((br) => new WalkResult(walked: new ForAllTy(id(), br.walked), entries: br.entries, state: br.state)))(parameterize_walk(st(), entries(), body()));

    public static Func<CodexType, Func<List<ParamEntry>, Func<UnificationState, WalkResult>>> WalkResult() => walked;

    public static Func<CodexType, CodexType> ty() => entries();

    public static List<ParamEntry> entries() => state();

    public static T4211 st<T4211>() => find_param_entry();

    public static T797 List<T797>() => /* error: -> */ default(Text());

    public static T51 Integer<T51>() => new Integer();

    public static T51 Integer<T51>() => find_param_entry()(entries())(name())(i())(len);

    public static object i() => len;

    public static ParamEntry e() => entries()[(int)i()];

    public static ParamEntry e() => (param_name == name());

    public static ParamEntry e() => var_id;

    public static T5778 find_param_entry<T5778>() => name()((i() + 1))(len);

    public static WalkListResult parameterize_walk_list(UnificationState st, List<ParamEntry> entries, List<CodexType> args, long i, long len, List<CodexType> acc)
    {
        while (true)
        {
            if ((i() == len))
            {
            return new WalkListResult(walked_list: acc(), entries: entries(), state: st());
            }
            else
            {
            var r = parameterize_walk(st(), entries(), args[(int)i()]);
            var _tco_0 = r.state;
            var _tco_1 = r.entries;
            var _tco_2 = args;
            var _tco_3 = (i() + 1);
            var _tco_4 = len;
            var _tco_5 = ((Func<List<CodexType>>)(() => { var _l = acc(); _l.Add(r.walked); return _l; }))();
            st = _tco_0;
            entries = _tco_1;
            args = _tco_2;
            i = _tco_3;
            len = _tco_4;
            acc = _tco_5;
            continue;
            }
        }
    }

    public static CheckResult check_def(UnificationState st, TypeEnv env, ADef def) => ((Func<DefSetup, CheckResult>)((declared) => ((Func<DefParamResult, CheckResult>)((env2) => ((Func<CheckResult, CheckResult>)((body_r) => ((Func<UnifyResult, CheckResult>)((u) => new CheckResult(inferred_type: declared.expected_type, state: u.state)))(unify(body_r.state, env2.remaining_type, body_r.inferred_type))))(infer_expr(env2.state, env2.env, def().body))))(bind_def_params(declared.state, declared.env, def().@params, declared.expected_type, 0, ((long)def().@params.Count)))))(resolve_declared_type(st(), env, def()));

    public static DefSetup resolve_declared_type(UnificationState st, TypeEnv env, ADef def) => ((((long)def().declared_type.Count) == 0) ? ((Func<FreshResult, DefSetup>)((fr) => new DefSetup(expected_type: fr().var_type, remaining_type: fr().var_type, state: fr().state, env: env)))(fresh_and_advance(st())) : ((Func<CodexType, DefSetup>)((env_type) => ((Func<FreshResult, DefSetup>)((inst) => new DefSetup(expected_type: inst.var_type, remaining_type: inst.var_type, state: inst.state, env: env)))(instantiate_type(st(), env_type))))(env_lookup(env, def().name.value)));

    public static DefParamResult bind_def_params(UnificationState st, TypeEnv env, List<AParam> @params, CodexType remaining, long i, long len)
    {
        while (true)
        {
            if ((i() == len))
            {
            return new DefParamResult(state: st(), env: env, remaining_type: remaining);
            }
            else
            {
            var p = @params[(int)i()];
            var _tco_s = remaining;
            if (_tco_s is FunTy _tco_m0)
            {
                var param_ty = _tco_m0.Field0;
                var ret_ty = _tco_m0.Field1;
            var env2 = env_bind(env, p.name.value, param_ty);
            var _tco_0 = st();
            var _tco_1 = env2;
            var _tco_2 = @params;
            var _tco_3 = ret_ty;
            var _tco_4 = (i() + 1);
            var _tco_5 = len;
            st = _tco_0;
            env = _tco_1;
            @params = _tco_2;
            remaining = _tco_3;
            i = _tco_4;
            len = _tco_5;
            continue;
            }
            {
            var fr = fresh_and_advance(st());
            var env2 = env_bind(env, p.name.value, fr().var_type);
            var _tco_0 = fr().state;
            var _tco_1 = env2;
            var _tco_2 = @params;
            var _tco_3 = remaining;
            var _tco_4 = (i() + 1);
            var _tco_5 = len;
            st = _tco_0;
            env = _tco_1;
            @params = _tco_2;
            remaining = _tco_3;
            i = _tco_4;
            len = _tco_5;
            continue;
            }
            }
        }
    }

    public static ModuleResult check_module(AModule mod) => ((Func<List<TypeBinding>, ModuleResult>)((tdm) => ((Func<LetBindResult, ModuleResult>)((tenv) => ((Func<LetBindResult, ModuleResult>)((env) => check_all_defs(env.state, env.env, mod.defs, 0, ((long)mod.defs.Count), new List<T5873>())))(register_all_defs(tenv.state, tenv.env, tdm, mod.defs, 0, ((long)mod.defs.Count)))))(register_type_defs(empty_unification_state(), builtin_type_env(), tdm, mod.type_defs, 0, ((long)mod.type_defs.Count)))))(build_type_def_map(mod.type_defs, 0, ((long)mod.type_defs.Count), new List<T5848>()));

    public static LetBindResult register_all_defs(UnificationState st, TypeEnv env, List<TypeBinding> tdm, List<ADef> defs, long i, long len)
    {
        while (true)
        {
            if ((i() == len))
            {
            return new LetBindResult(state: st(), env: env);
            }
            else
            {
            var def = defs[(int)i()];
            var ty = ((((long)def().declared_type.Count) == 0) ? ((Func<FreshResult, LetBindResult>)((fr) => ((Func<TypeEnv, LetBindResult>)((env2) => new LetBindResult(state: fr().state, env: env2)))(env_bind(env, def().name.value, fr().var_type))))(fresh_and_advance(st())) : ((Func<CodexType, LetBindResult>)((resolved) => ((Func<ParamResult, LetBindResult>)((pr) => new LetBindResult(state: pr.state, env: env_bind(env, def().name.value, pr.parameterized))))(parameterize_type(st(), resolved))))(resolve_type_expr(tdm, def().declared_type[(int)0])));
            var _tco_0 = ty().state;
            var _tco_1 = ty().env;
            var _tco_2 = tdm;
            var _tco_3 = defs;
            var _tco_4 = (i() + 1);
            var _tco_5 = len;
            st = _tco_0;
            env = _tco_1;
            tdm = _tco_2;
            defs = _tco_3;
            i = _tco_4;
            len = _tco_5;
            continue;
            }
        }
    }

    public static ModuleResult check_all_defs<T5873>(UnificationState st, TypeEnv env, List<ADef> defs, long i, long len, List<T5873> acc) => ((i() == len) ? new ModuleResult(types: acc(), state: st()) : ((Func<ADef, ModuleResult>)((def) => ((Func<CheckResult, ModuleResult>)((r) => ((Func<CodexType, ModuleResult>)((resolved) => ((Func<object, ModuleResult>)((entry) => /* error: { */ default(name())))(TypeBinding)))(deep_resolve(r.state, r.inferred_type))))(check_def(st(), env, def()))))(defs[(int)i()]));

    public static T5916 def<T5916>() => name().value;

    public static object bound_type() => resolved;

    public static Func<UnificationState, Func<TypeEnv, Func<List<ADef>, Func<long, Func<long, Func<List<T5873>, ModuleResult>>>>>> check_all_defs<T5873>() => /* error: . */ default(state())(env)(defs)((i() + 1))(len)(((Func<List<object>>)(() => { var _l = acc(); _l.Add(entry()); return _l; }))());

    public static List<TypeBinding> build_type_def_map<T5848>(List<ATypeDef> tdefs, long i, long len, List<T5848> acc) => ((i() == len) ? acc() : ((Func<ATypeDef, List<TypeBinding>>)((td) => ((Func<object, List<TypeBinding>>)((entry) => /* error: { */ default(name())))(td switch { AVariantTypeDef(var name, var type_params, var ctors) => ((Func<List<SumCtor>, object>)((sum_ctors) => TypeBinding))(build_sum_ctors(tdefs, ctors, 0, ((long)ctors.Count), new List<SumCtor>(), acc())), _ => throw new InvalidOperationException("Non-exhaustive match"), })))(tdefs[(int)i()]));

    public static object name() => value;

    public static object bound_type() => new SumTy(name(), sum_ctors);

    public static ATypeDef ARecordTypeDef(Name name, List<Name> type_params, List<ARecordFieldDef> fields) => ((Func<List<RecordField>, ATypeDef>)((rec_fields) => TypeBinding))(build_record_fields_for_map(tdefs, fields, 0, ((long)fields.Count), new List<RecordField>(), acc()));

    public static object name() => name().value;

    public static object bound_type() => new RecordTy(name(), rec_fields);

    public static Func<List<ATypeDef>, Func<long, Func<long, Func<List<T5848>, List<TypeBinding>>>>> build_type_def_map<T5848>() => (i() + 1)(len)(((Func<List<object>>)(() => { var _l = new List<object>(acc()); _l.Insert((int)bsearch_text_pos(acc(), entry().name, 0, ((long)acc().Count)), entry()); return _l; }))());

    public static List<SumCtor> build_sum_ctors(List<ATypeDef> tdefs, List<AVariantCtorDef> ctors, long i, long len, List<SumCtor> acc, List<TypeBinding> partial_tdm)
    {
        while (true)
        {
            if ((i() == len))
            {
            return acc();
            }
            else
            {
            var c = ctors[(int)i()];
            var field_types = resolve_type_expr_list_for_map(tdefs, c.fields, 0, ((long)c.fields.Count), new List<CodexType>(), partial_tdm);
            var sc = new SumCtor(name: c.name, fields: field_types);
            var _tco_0 = tdefs;
            var _tco_1 = ctors;
            var _tco_2 = (i() + 1);
            var _tco_3 = len;
            var _tco_4 = ((Func<List<SumCtor>>)(() => { var _l = acc(); _l.Add(sc); return _l; }))();
            var _tco_5 = partial_tdm;
            tdefs = _tco_0;
            ctors = _tco_1;
            i = _tco_2;
            len = _tco_3;
            acc = _tco_4;
            partial_tdm = _tco_5;
            continue;
            }
        }
    }

    public static List<RecordField> build_record_fields_for_map(List<ATypeDef> tdefs, List<ARecordFieldDef> fields, long i, long len, List<RecordField> acc, List<TypeBinding> partial_tdm)
    {
        while (true)
        {
            if ((i() == len))
            {
            return acc();
            }
            else
            {
            var f = fields[(int)i()];
            var rfield = new RecordField(name: f.name, type_val: resolve_type_expr(partial_tdm, f.type_expr));
            var _tco_0 = tdefs;
            var _tco_1 = fields;
            var _tco_2 = (i() + 1);
            var _tco_3 = len;
            var _tco_4 = ((Func<List<RecordField>>)(() => { var _l = acc(); _l.Add(rfield); return _l; }))();
            var _tco_5 = partial_tdm;
            tdefs = _tco_0;
            fields = _tco_1;
            i = _tco_2;
            len = _tco_3;
            acc = _tco_4;
            partial_tdm = _tco_5;
            continue;
            }
        }
    }

    public static List<CodexType> resolve_type_expr_list_for_map(List<ATypeDef> tdefs, List<ATypeExpr> args, long i, long len, List<CodexType> acc, List<TypeBinding> partial_tdm)
    {
        while (true)
        {
            if ((i() == len))
            {
            return acc();
            }
            else
            {
            var _tco_0 = tdefs;
            var _tco_1 = args;
            var _tco_2 = (i() + 1);
            var _tco_3 = len;
            var _tco_4 = ((Func<List<CodexType>>)(() => { var _l = acc(); _l.Add(resolve_type_expr(partial_tdm, args[(int)i()])); return _l; }))();
            var _tco_5 = partial_tdm;
            tdefs = _tco_0;
            args = _tco_1;
            i = _tco_2;
            len = _tco_3;
            acc = _tco_4;
            partial_tdm = _tco_5;
            continue;
            }
        }
    }

    public static LetBindResult register_type_defs(UnificationState st, TypeEnv env, List<TypeBinding> tdm, List<ATypeDef> tdefs, long i, long len)
    {
        while (true)
        {
            if ((i() == len))
            {
            return new LetBindResult(state: st(), env: env);
            }
            else
            {
            var td = tdefs[(int)i()];
            var r = register_one_type_def(st(), env, tdm, td);
            var _tco_0 = r.state;
            var _tco_1 = r.env;
            var _tco_2 = tdm;
            var _tco_3 = tdefs;
            var _tco_4 = (i() + 1);
            var _tco_5 = len;
            st = _tco_0;
            env = _tco_1;
            tdm = _tco_2;
            tdefs = _tco_3;
            i = _tco_4;
            len = _tco_5;
            continue;
            }
        }
    }

    public static LetBindResult register_one_type_def(UnificationState st, TypeEnv env, List<TypeBinding> tdm, ATypeDef td) => td switch { AVariantTypeDef(var name, var type_params, var ctors) => ((Func<CodexType, LetBindResult>)((result_ty) => register_variant_ctors(st(), env, tdm, ctors, result_ty, 0, ((long)ctors.Count))))(lookup_type_def(tdm, name().value)), ARecordTypeDef(var name, var type_params, var fields) => ((Func<List<RecordField>, LetBindResult>)((resolved_fields) => ((Func<object, LetBindResult>)((result_ty) => ((Func<CodexType, LetBindResult>)((ctor_ty) => new LetBindResult(state: st(), env: env_bind(env, name().value, ctor_ty))))(build_record_ctor_type(tdm, fields, result_ty, 0, ((long)fields.Count)))))(new RecordTy(name(), resolved_fields))))(build_record_fields(tdm, fields, 0, ((long)fields.Count), new List<RecordField>())), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static List<RecordField> build_record_fields(List<TypeBinding> tdm, List<ARecordFieldDef> fields, long i, long len, List<RecordField> acc)
    {
        while (true)
        {
            if ((i() == len))
            {
            return acc();
            }
            else
            {
            var f = fields[(int)i()];
            var rfield = new RecordField(name: f.name, type_val: resolve_type_expr(tdm, f.type_expr));
            var _tco_0 = tdm;
            var _tco_1 = fields;
            var _tco_2 = (i() + 1);
            var _tco_3 = len;
            var _tco_4 = ((Func<List<RecordField>>)(() => { var _l = acc(); _l.Add(rfield); return _l; }))();
            tdm = _tco_0;
            fields = _tco_1;
            i = _tco_2;
            len = _tco_3;
            acc = _tco_4;
            continue;
            }
        }
    }

    public static CodexType lookup_record_field(List<RecordField> fields, string name) => ((((long)fields.Count) == 0) ? ErrorTy() : ((Func<RecordField, CodexType>)((f) => ((f.name.value == name()) ? f.type_val : lookup_record_field_loop(fields, name(), 1, ((long)fields.Count)))))(fields[(int)0]));

    public static CodexType lookup_record_field_loop(List<RecordField> fields, string name, long i, long len)
    {
        while (true)
        {
            if ((i() == len))
            {
            return ErrorTy();
            }
            else
            {
            var f = fields[(int)i()];
            if ((f.name.value == name()))
            {
            return f.type_val;
            }
            else
            {
            var _tco_0 = fields;
            var _tco_1 = name();
            var _tco_2 = (i() + 1);
            var _tco_3 = len;
            fields = _tco_0;
            name = _tco_1;
            i = _tco_2;
            len = _tco_3;
            continue;
            }
            }
        }
    }

    public static LetBindResult register_variant_ctors(UnificationState st, TypeEnv env, List<TypeBinding> tdm, List<AVariantCtorDef> ctors, CodexType result_ty, long i, long len)
    {
        while (true)
        {
            if ((i() == len))
            {
            return new LetBindResult(state: st(), env: env);
            }
            else
            {
            var ctor = ctors[(int)i()];
            var ctor_ty = build_ctor_type(tdm, ctor().fields, result_ty, 0, ((long)ctor().fields.Count));
            var env2 = env_bind(env, ctor().name.value, ctor_ty);
            var _tco_0 = st();
            var _tco_1 = env2;
            var _tco_2 = tdm;
            var _tco_3 = ctors;
            var _tco_4 = result_ty;
            var _tco_5 = (i() + 1);
            var _tco_6 = len;
            st = _tco_0;
            env = _tco_1;
            tdm = _tco_2;
            ctors = _tco_3;
            result_ty = _tco_4;
            i = _tco_5;
            len = _tco_6;
            continue;
            }
        }
    }

    public static CodexType build_ctor_type(List<TypeBinding> tdm, List<ATypeExpr> fields, CodexType result, long i, long len) => ((i() == len) ? result : ((Func<CodexType, CodexType>)((rest) => new FunTy(resolve_type_expr(tdm, fields[(int)i()]), rest)))(build_ctor_type(tdm, fields, result, (i() + 1), len)));

    public static CodexType build_record_ctor_type(List<TypeBinding> tdm, List<ARecordFieldDef> fields, CodexType result, long i, long len) => ((i() == len) ? result : ((Func<ARecordFieldDef, CodexType>)((f) => ((Func<CodexType, CodexType>)((rest) => new FunTy(resolve_type_expr(tdm, f.type_expr), rest)))(build_record_ctor_type(tdm, fields, result, (i() + 1), len))))(fields[(int)i()]));

    public static CheckResult infer_literal(UnificationState st, LiteralKind kind) => kind() switch { IntLit { } => new CheckResult(inferred_type: new IntegerTy(), state: st()), NumLit { } => new CheckResult(inferred_type: new NumberTy(), state: st()), object TextLit => new CheckResult(inferred_type: TextTy(), state: st()), };

    public static CheckResult infer_name(UnificationState st, TypeEnv env, string name) => (env_has(env, name()) ? ((Func<CodexType, CheckResult>)((raw) => ((Func<FreshResult, CheckResult>)((inst) => new CheckResult(inferred_type: inst.var_type, state: inst.state)))(instantiate_type(st(), raw))))(env_lookup(env, name())) : new CheckResult(inferred_type: ErrorTy(), state: add_unify_error(st(), "CDX\u0005\u0003\u0003\u0005", ("U\u0012\"\u0012\u0010\u001B\u0012\u0002\u0012\u000F\u001A\u000D:\u0002" + name()))));

    public static FreshResult instantiate_type(UnificationState st, CodexType ty)
    {
        while (true)
        {
            var _tco_s = ty();
            if (_tco_s is ForAllTy _tco_m0)
            {
                var var_id = _tco_m0.Field0;
                var body = _tco_m0.Field1;
            var fr = fresh_and_advance(st());
            var substituted = subst_type_var(body(), var_id, fr().var_type);
            var _tco_0 = fr().state;
            var _tco_1 = substituted;
            st = _tco_0;
            ty = _tco_1;
            continue;
            }
            {
            return new FreshResult(var_type: ty(), state: st());
            }
        }
    }

    public static CodexType subst_type_var(CodexType ty, long var_id, CodexType replacement) => ty() switch { object TypeVar => id(), };

    public static object id() => var_id;

    public static bool replacement() => ty();

    public static List<long> FunTy(object param, object ret)
    {
        while (true)
        {
            var _tco_0 = subst_type_var(param, var_id, replacement());
            var _tco_1 = subst_type_var(ret(), var_id, replacement());
            param = _tco_0;
            ret = _tco_1;
            continue;
        }
    }

    public static List<long> ListTy(object elem)
    {
        while (true)
        {
            var _tco_0 = subst_type_var(elem, var_id, replacement());
            elem = _tco_0;
            continue;
        }
    }

    public static List<long> ForAllTy(object inner_id, object body)
    {
        while (true)
        {
            if ((inner_id == var_id))
            {
            return ty();
            }
            else
            {
            var _tco_0 = inner_id;
            var _tco_1 = subst_type_var(body(), var_id, replacement());
            inner_id = _tco_0;
            body = _tco_1;
            continue;
            }
        }
    }

    public static T793 ConstructedTy<T793>(object name, object args)
    {
        while (true)
        {
            var _tco_0 = name();
            var _tco_1 = map_subst_type_var(args)(var_id)(replacement())(0)(((long)args.Count))(new List<object>());
            name = _tco_0;
            args = _tco_1;
            continue;
        }
    }

    public static bool SumTy(object name, object ctors) => ty();

    public static string RecordTy(object name, object fields) => ty();

    public static Func<CodexType, CodexType> ty() => map_subst_type_var;

    public static T797 List<T797>() => /* error: -> */ default(new Integer());

    public static Token CodexType() => new Integer();

    public static T51 Integer<T51>() => new List(new CodexType());

    public static T797 List<T797>() => map_subst_type_var(args)(var_id)(replacement())(i())(len)(acc());

    public static object i() => len;

    public static T772 acc<T772>() => /* error: else */ default(map_subst_type_var)(args)(var_id)(replacement())((i() + 1))(len)(((Func<List<object>>)(() => { var _l = acc(); _l.Add(subst_type_var(args[(int)i()], var_id, replacement())); return _l; }))());

    public static CheckResult infer_binary(UnificationState st, TypeEnv env, AExpr left, BinaryOp op, AExpr right) => ((Func<CheckResult, CheckResult>)((lr) => ((Func<CheckResult, CheckResult>)((rr) => infer_binary_op(rr.state, lr.inferred_type, rr.inferred_type, op)))(infer_expr(lr.state, env, right))))(infer_expr(st(), env, left));

    public static CheckResult infer_binary_op(UnificationState st, CodexType lt, CodexType rt, BinaryOp op) => op switch { OpAdd { } => infer_arithmetic(st(), lt, rt), OpSub { } => infer_arithmetic(st(), lt, rt), OpMul { } => infer_arithmetic(st(), lt, rt), OpDiv { } => infer_arithmetic(st(), lt, rt), OpPow { } => infer_arithmetic(st(), lt, rt), OpEq { } => infer_comparison(st(), lt, rt), OpNotEq { } => infer_comparison(st(), lt, rt), OpLt { } => infer_comparison(st(), lt, rt), OpGt { } => infer_comparison(st(), lt, rt), OpLtEq { } => infer_comparison(st(), lt, rt), OpGtEq { } => infer_comparison(st(), lt, rt), OpAnd { } => infer_logical(st(), lt, rt), OpOr { } => infer_logical(st(), lt, rt), OpAppend { } => infer_append(st(), lt, rt), OpCons { } => infer_cons(st(), lt, rt), OpDefEq { } => infer_comparison(st(), lt, rt), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static CheckResult infer_arithmetic(UnificationState st, CodexType lt, CodexType rt) => ((Func<UnifyResult, CheckResult>)((r) => new CheckResult(inferred_type: lt, state: r.state)))(unify(st(), lt, rt));

    public static CheckResult infer_comparison(UnificationState st, CodexType lt, CodexType rt) => ((Func<UnifyResult, CheckResult>)((r) => new CheckResult(inferred_type: new BooleanTy(), state: r.state)))(unify(st(), lt, rt));

    public static CheckResult infer_logical(UnificationState st, CodexType lt, CodexType rt) => ((Func<UnifyResult, CheckResult>)((r1) => ((Func<UnifyResult, CheckResult>)((r2) => new CheckResult(inferred_type: new BooleanTy(), state: r2.state)))(unify(r1.state, rt, new BooleanTy()))))(unify(st(), lt, new BooleanTy()));

    public static CheckResult infer_append(UnificationState st, CodexType lt, CodexType rt) => ((Func<CodexType, CheckResult>)((resolved) => resolved switch { object TextTy => ((Func<UnifyResult, CheckResult>)((r) => new CheckResult(inferred_type: TextTy(), state: r.state)))(unify(st(), rt, TextTy())), }))(resolve(st(), lt));

    public static CheckResult infer_cons(UnificationState st, CodexType lt, CodexType rt) => ((Func<object, CheckResult>)((list_ty) => ((Func<UnifyResult, CheckResult>)((r) => new CheckResult(inferred_type: list_ty, state: r.state)))(unify(st(), rt, list_ty))))(new ListTy(lt));

    public static CheckResult infer_if(UnificationState st, TypeEnv env, AExpr cond, AExpr then_e, AExpr else_e) => ((Func<CheckResult, CheckResult>)((cr) => ((Func<UnifyResult, CheckResult>)((r1) => ((Func<CheckResult, CheckResult>)((tr) => ((Func<CheckResult, CheckResult>)((er) => ((Func<UnifyResult, CheckResult>)((r2) => new CheckResult(inferred_type: tr.inferred_type, state: r2.state)))(unify(er.state, tr.inferred_type, er.inferred_type))))(infer_expr(tr.state, env, else_e))))(infer_expr(r1.state, env, then_e))))(unify(cr.state, cr.inferred_type, new BooleanTy()))))(infer_expr(st(), env, cond));

    public static CheckResult infer_let(UnificationState st, TypeEnv env, List<ALetBind> bindings, AExpr body) => ((Func<LetBindResult, CheckResult>)((env2) => infer_expr(env2.state, env2.env, body())))(infer_let_bindings(st(), env, bindings(), 0, ((long)bindings().Count)));

    public static LetBindResult infer_let_bindings(UnificationState st, TypeEnv env, List<ALetBind> bindings, long i, long len)
    {
        while (true)
        {
            if ((i() == len))
            {
            return new LetBindResult(state: st(), env: env);
            }
            else
            {
            var b = bindings()[(int)i()];
            var vr = infer_expr(st(), env, b().value);
            var env2 = env_bind(env, b().name.value, vr.inferred_type);
            var _tco_0 = vr.state;
            var _tco_1 = env2;
            var _tco_2 = bindings();
            var _tco_3 = (i() + 1);
            var _tco_4 = len;
            st = _tco_0;
            env = _tco_1;
            bindings = _tco_2;
            i = _tco_3;
            len = _tco_4;
            continue;
            }
        }
    }

    public static CheckResult infer_lambda(UnificationState st, TypeEnv env, List<Name> @params, AExpr body) => ((Func<LambdaBindResult, CheckResult>)((pr) => ((Func<CheckResult, CheckResult>)((br) => ((Func<CodexType, CheckResult>)((fun_ty) => new CheckResult(inferred_type: fun_ty, state: br.state)))(wrap_fun_type(pr.param_types, br.inferred_type))))(infer_expr(pr.state, pr.env, body()))))(bind_lambda_params(st(), env, @params, 0, ((long)@params.Count), new List<CodexType>()));

    public static LambdaBindResult bind_lambda_params(UnificationState st, TypeEnv env, List<Name> @params, long i, long len, List<CodexType> acc)
    {
        while (true)
        {
            if ((i() == len))
            {
            return new LambdaBindResult(state: st(), env: env, param_types: acc());
            }
            else
            {
            var p = @params[(int)i()];
            var fr = fresh_and_advance(st());
            var env2 = env_bind(env, p.value, fr().var_type);
            var _tco_0 = fr().state;
            var _tco_1 = env2;
            var _tco_2 = @params;
            var _tco_3 = (i() + 1);
            var _tco_4 = len;
            var _tco_5 = ((Func<List<CodexType>>)(() => { var _l = acc(); _l.Add(fr().var_type); return _l; }))();
            st = _tco_0;
            env = _tco_1;
            @params = _tco_2;
            i = _tco_3;
            len = _tco_4;
            acc = _tco_5;
            continue;
            }
        }
    }

    public static CodexType wrap_fun_type(List<CodexType> param_types, CodexType result) => wrap_fun_type_loop(param_types, result, (((long)param_types.Count) - 1));

    public static CodexType wrap_fun_type_loop(List<CodexType> param_types, CodexType result, long i)
    {
        while (true)
        {
            if ((i() < 0))
            {
            return result;
            }
            else
            {
            var _tco_0 = param_types;
            var _tco_1 = new FunTy(param_types[(int)i()], result);
            var _tco_2 = (i() - 1);
            param_types = _tco_0;
            result = _tco_1;
            i = _tco_2;
            continue;
            }
        }
    }

    public static CheckResult infer_application(UnificationState st, TypeEnv env, AExpr func, AExpr arg) => ((Func<CheckResult, CheckResult>)((fr) => ((Func<CheckResult, CheckResult>)((ar) => ((Func<FreshResult, CheckResult>)((ret) => ((Func<UnifyResult, CheckResult>)((r) => new CheckResult(inferred_type: ret().var_type, state: r.state)))(unify(ret().state, fr().inferred_type, new FunTy(ar.inferred_type, ret().var_type)))))(fresh_and_advance(ar.state))))(infer_expr(fr().state, env, arg))))(infer_expr(st(), env, func));

    public static CheckResult infer_list(UnificationState st, TypeEnv env, List<AExpr> elems) => ((((long)elems.Count) == 0) ? ((Func<FreshResult, CheckResult>)((fr) => new CheckResult(inferred_type: new ListTy(fr().var_type), state: fr().state)))(fresh_and_advance(st())) : ((Func<CheckResult, CheckResult>)((first) => ((Func<UnificationState, CheckResult>)((st2) => new CheckResult(inferred_type: new ListTy(first.inferred_type), state: st2())))(unify_list_elems(first.state, env, elems, first.inferred_type, 1, ((long)elems.Count)))))(infer_expr(st(), env, elems[(int)0])));

    public static UnificationState unify_list_elems(UnificationState st, TypeEnv env, List<AExpr> elems, CodexType elem_ty, long i, long len)
    {
        while (true)
        {
            if ((i() == len))
            {
            return st();
            }
            else
            {
            var er = infer_expr(st(), env, elems[(int)i()]);
            var r = unify(er.state, er.inferred_type, elem_ty);
            var _tco_0 = r.state;
            var _tco_1 = env;
            var _tco_2 = elems;
            var _tco_3 = elem_ty;
            var _tco_4 = (i() + 1);
            var _tco_5 = len;
            st = _tco_0;
            env = _tco_1;
            elems = _tco_2;
            elem_ty = _tco_3;
            i = _tco_4;
            len = _tco_5;
            continue;
            }
        }
    }

    public static CheckResult infer_match(UnificationState st, TypeEnv env, AExpr scrutinee, List<AMatchArm> arms) => ((Func<CheckResult, CheckResult>)((sr) => ((Func<FreshResult, CheckResult>)((fr) => ((Func<UnificationState, CheckResult>)((st2) => new CheckResult(inferred_type: fr().var_type, state: st2())))(infer_match_arms(fr().state, env, sr.inferred_type, fr().var_type, arms, 0, ((long)arms.Count)))))(fresh_and_advance(sr.state))))(infer_expr(st(), env, scrutinee));

    public static UnificationState infer_match_arms(UnificationState st, TypeEnv env, CodexType scrut_ty, CodexType result_ty, List<AMatchArm> arms, long i, long len)
    {
        while (true)
        {
            if ((i() == len))
            {
            return st();
            }
            else
            {
            var arm = arms[(int)i()];
            var pr = bind_pattern(st(), env, arm.pattern, scrut_ty);
            var br = infer_expr(pr.state, pr.env, arm.body);
            var r = unify(br.state, br.inferred_type, result_ty);
            var _tco_0 = r.state;
            var _tco_1 = env;
            var _tco_2 = scrut_ty;
            var _tco_3 = result_ty;
            var _tco_4 = arms;
            var _tco_5 = (i() + 1);
            var _tco_6 = len;
            st = _tco_0;
            env = _tco_1;
            scrut_ty = _tco_2;
            result_ty = _tco_3;
            arms = _tco_4;
            i = _tco_5;
            len = _tco_6;
            continue;
            }
        }
    }

    public static PatBindResult bind_pattern(UnificationState st, TypeEnv env, APat pat, CodexType ty) => pat switch { AVarPat(var name) => new PatBindResult(state: st(), env: env_bind(env, name().value, ty())), AWildPat { } => new PatBindResult(state: st(), env: env), ALitPat(var val, var kind) => new PatBindResult(state: st(), env: env), ACtorPat(var ctor_name, var sub_pats) => ((Func<FreshResult, PatBindResult>)((ctor_lookup) => bind_ctor_sub_patterns(ctor_lookup.state, env, sub_pats, ctor_lookup.var_type, 0, ((long)sub_pats.Count))))(instantiate_type(st(), env_lookup(env, ctor_name.value))), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static PatBindResult bind_ctor_sub_patterns(UnificationState st, TypeEnv env, List<APat> sub_pats, CodexType ctor_ty, long i, long len)
    {
        while (true)
        {
            if ((i() == len))
            {
            return new PatBindResult(state: st(), env: env);
            }
            else
            {
            var _tco_s = ctor_ty;
            if (_tco_s is FunTy _tco_m0)
            {
                var param_ty = _tco_m0.Field0;
                var ret_ty = _tco_m0.Field1;
            var pr = bind_pattern(st(), env, sub_pats[(int)i()], param_ty);
            var _tco_0 = pr.state;
            var _tco_1 = pr.env;
            var _tco_2 = sub_pats;
            var _tco_3 = ret_ty;
            var _tco_4 = (i() + 1);
            var _tco_5 = len;
            st = _tco_0;
            env = _tco_1;
            sub_pats = _tco_2;
            ctor_ty = _tco_3;
            i = _tco_4;
            len = _tco_5;
            continue;
            }
            {
            var fr = fresh_and_advance(st());
            var pr = bind_pattern(fr().state, env, sub_pats[(int)i()], fr().var_type);
            var _tco_0 = pr.state;
            var _tco_1 = pr.env;
            var _tco_2 = sub_pats;
            var _tco_3 = ctor_ty;
            var _tco_4 = (i() + 1);
            var _tco_5 = len;
            st = _tco_0;
            env = _tco_1;
            sub_pats = _tco_2;
            ctor_ty = _tco_3;
            i = _tco_4;
            len = _tco_5;
            continue;
            }
            }
        }
    }

    public static CheckResult infer_do(UnificationState st, TypeEnv env, List<ADoStmt> stmts) => infer_do_loop(st(), env, stmts, 0, ((long)stmts.Count), new NothingTy());

    public static CheckResult infer_do_loop(UnificationState st, TypeEnv env, List<ADoStmt> stmts, long i, long len, CodexType last_ty)
    {
        while (true)
        {
            if ((i() == len))
            {
            return new CheckResult(inferred_type: last_ty, state: st());
            }
            else
            {
            var stmt = stmts[(int)i()];
            var _tco_s = stmt;
            if (_tco_s is ADoExprStmt _tco_m0)
            {
                var e = _tco_m0.Field0;
            var er = infer_expr(st(), env, e());
            var _tco_0 = er.state;
            var _tco_1 = env;
            var _tco_2 = stmts;
            var _tco_3 = (i() + 1);
            var _tco_4 = len;
            var _tco_5 = er.inferred_type;
            st = _tco_0;
            env = _tco_1;
            stmts = _tco_2;
            i = _tco_3;
            len = _tco_4;
            last_ty = _tco_5;
            continue;
            }
            else if (_tco_s is ADoBindStmt _tco_m1)
            {
                var name = _tco_m1.Field0;
                var e = _tco_m1.Field1;
            var er = infer_expr(st(), env, e());
            var env2 = env_bind(env, name().value, er.inferred_type);
            var _tco_0 = er.state;
            var _tco_1 = env2;
            var _tco_2 = stmts;
            var _tco_3 = (i() + 1);
            var _tco_4 = len;
            var _tco_5 = er.inferred_type;
            st = _tco_0;
            env = _tco_1;
            stmts = _tco_2;
            i = _tco_3;
            len = _tco_4;
            last_ty = _tco_5;
            continue;
            }
            }
        }
    }

    public static CheckResult infer_expr(UnificationState st, TypeEnv env, AExpr expr) => expr switch { ALitExpr(var val, var kind) => infer_literal(st(), kind()), ANameExpr(var name) => infer_name(st(), env, name().value), ABinaryExpr(var left, var op, var right) => infer_binary(st(), env, left, op, right), AUnaryExpr(var operand) => ((Func<CheckResult, CheckResult>)((r) => ((Func<UnifyResult, CheckResult>)((u) => new CheckResult(inferred_type: new IntegerTy(), state: u.state)))(unify(r.state, r.inferred_type, new IntegerTy()))))(infer_expr(st(), env, operand)), AApplyExpr(var func, var arg) => infer_application(st(), env, func, arg), AIfExpr(var cond, var then_e, var else_e) => infer_if(st(), env, cond, then_e, else_e), ALetExpr(var bindings, var body) => infer_let(st(), env, bindings(), body()), ALambdaExpr(var @params, var body) => infer_lambda(st(), env, @params, body()), AMatchExpr(var scrutinee, var arms) => infer_match(st(), env, scrutinee, arms), AListExpr(var elems) => infer_list(st(), env, elems), ADoExpr(var stmts) => infer_do(st(), env, stmts), AFieldAccess(var obj, var field) => ((Func<CheckResult, CheckResult>)((r) => ((Func<CodexType, CheckResult>)((resolved) => resolved switch { RecordTy(var rname, var rfields) => ((Func<CodexType, CheckResult>)((ftype) => new CheckResult(inferred_type: ftype, state: r.state)))(lookup_record_field(rfields, field().value)), ConstructedTy(var cname, var cargs) => ((Func<CodexType, CheckResult>)((record_type) => record_type switch { RecordTy(var rname, var rfields) => ((Func<CodexType, CheckResult>)((ftype) => new CheckResult(inferred_type: ftype, state: r.state)))(lookup_record_field(rfields, field().value)), _ => ((Func<FreshResult, CheckResult>)((fr) => new CheckResult(inferred_type: fr().var_type, state: fr().state)))(fresh_and_advance(r.state)), }))(resolve_constructed_to_record(env, cname.value)), _ => ((Func<FreshResult, CheckResult>)((fr) => new CheckResult(inferred_type: fr().var_type, state: fr().state)))(fresh_and_advance(r.state)), }))(deep_resolve(r.state, r.inferred_type))))(infer_expr(st(), env, obj)), ARecordExpr(var name, var fields) => ((Func<UnificationState, CheckResult>)((st2) => ((Func<CodexType, CheckResult>)((ctor_type) => ((Func<CodexType, CheckResult>)((result_type) => new CheckResult(inferred_type: result_type, state: st2())))(strip_fun_args(ctor_type))))((env_has(env, name().value) ? env_lookup(env, name().value) : ErrorTy()))))(infer_record_fields(st(), env, fields, 0, ((long)fields.Count))), AErrorExpr(var msg) => new CheckResult(inferred_type: ErrorTy(), state: st()), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static CodexType resolve_constructed_to_record(TypeEnv env, string name) => (env_has(env, name()) ? strip_fun_args(env_lookup(env, name())) : ErrorTy());

    public static UnificationState infer_record_fields(UnificationState st, TypeEnv env, List<AFieldExpr> fields, long i, long len)
    {
        while (true)
        {
            if ((i() == len))
            {
            return st();
            }
            else
            {
            var f = fields[(int)i()];
            var r = infer_expr(st(), env, f.value);
            var _tco_0 = r.state;
            var _tco_1 = env;
            var _tco_2 = fields;
            var _tco_3 = (i() + 1);
            var _tco_4 = len;
            st = _tco_0;
            env = _tco_1;
            fields = _tco_2;
            i = _tco_3;
            len = _tco_4;
            continue;
            }
        }
    }

    public static CodexType strip_fun_args(CodexType ty)
    {
        while (true)
        {
            var _tco_s = ty();
            if (_tco_s is FunTy _tco_m0)
            {
                var p = _tco_m0.Field0;
                var r = _tco_m0.Field1;
            var _tco_0 = r;
            ty = _tco_0;
            continue;
            }
            {
            return ty();
            }
        }
    }

    public static object TypeEnv() => /* error: record */ default;

    public static List<TypeBinding> }() => TypeBinding;

    public static string ,() => bound_type();

    public static Token CodexType() => /* error: } */ default;

    public static TypeEnv empty_type_env() => TypeEnv();

    public static List<T6678> bindings<T6678>() => new List<T6678>();

    public static CodexType env_lookup(TypeEnv env, string name) => ((Func<long, CodexType>)((len) => ((len == 0) ? ErrorTy() : ((Func<long, CodexType>)((pos) => ((pos() >= len) ? ErrorTy() : ((Func<T0, CodexType>)((b) => ((b().name == name()) ? b().bound_type : ErrorTy())))(env.bindings[(int)pos()]))))(bsearch_text_pos(env.bindings, name(), 0, len)))))(((long)env.bindings.Count));

    public static bool env_has(TypeEnv env, string name) => ((Func<long, bool>)((len) => ((len == 0) ? false : ((Func<long, bool>)((pos) => ((pos() >= len) ? false : (env.bindings[(int)pos()].name == name()))))(bsearch_text_pos(env.bindings, name(), 0, len)))))(((long)env.bindings.Count));

    public static TypeEnv env_bind(TypeEnv env, string name, CodexType ty) => ((Func<long, TypeEnv>)((len) => ((Func<long, TypeEnv>)((pos) => TypeEnv()))(bsearch_text_pos(env.bindings, name(), 0, len))))(((long)env.bindings.Count));

    public static List<T6678> bindings<T6678>() => ((Func<List<object>>)(() => { var _l = new List<object>(env.bindings); _l.Insert((int)pos(), TypeBinding); return _l; }))();

    public static object name() => bound_type();

    public static Func<CodexType, CodexType> ty() => /* error: ) */ default;

    public static TypeEnv builtin_type_env() => ((Func<TypeEnv, TypeEnv>)((e) => ((Func<TypeEnv, TypeEnv>)((e2) => ((Func<TypeEnv, TypeEnv>)((e3) => ((Func<TypeEnv, TypeEnv>)((e4) => ((Func<TypeEnv, TypeEnv>)((e5) => ((Func<TypeEnv, TypeEnv>)((e5b) => ((Func<TypeEnv, TypeEnv>)((e6) => ((Func<TypeEnv, TypeEnv>)((e7) => ((Func<TypeEnv, TypeEnv>)((e8) => ((Func<TypeEnv, TypeEnv>)((e9) => ((Func<TypeEnv, TypeEnv>)((e10) => ((Func<TypeEnv, TypeEnv>)((e10b) => ((Func<TypeEnv, TypeEnv>)((e11) => ((Func<TypeEnv, TypeEnv>)((e12) => ((Func<TypeEnv, TypeEnv>)((e13) => ((Func<TypeEnv, TypeEnv>)((e14) => ((Func<TypeEnv, TypeEnv>)((e15) => ((Func<TypeEnv, TypeEnv>)((e16) => ((Func<TypeEnv, TypeEnv>)((e16b) => ((Func<TypeEnv, TypeEnv>)((e16c) => ((Func<TypeEnv, TypeEnv>)((e16d) => ((Func<TypeEnv, TypeEnv>)((e17) => ((Func<TypeEnv, TypeEnv>)((e18) => ((Func<TypeEnv, TypeEnv>)((e19) => ((Func<TypeEnv, TypeEnv>)((e20) => ((Func<TypeEnv, TypeEnv>)((e21) => ((Func<TypeEnv, TypeEnv>)((e22) => ((Func<TypeEnv, TypeEnv>)((e23) => ((Func<TypeEnv, TypeEnv>)((e24) => ((Func<TypeEnv, TypeEnv>)((e25) => ((Func<TypeEnv, TypeEnv>)((e26) => ((Func<TypeEnv, TypeEnv>)((e27) => ((Func<TypeEnv, TypeEnv>)((e28) => ((Func<TypeEnv, TypeEnv>)((e29) => ((Func<TypeEnv, TypeEnv>)((e30) => ((Func<TypeEnv, TypeEnv>)((e31) => ((Func<TypeEnv, TypeEnv>)((e32) => ((Func<TypeEnv, TypeEnv>)((e33) => ((Func<TypeEnv, TypeEnv>)((e34) => ((Func<TypeEnv, TypeEnv>)((e35) => e35))(env_bind(e34, "\u0015\u000F\u0018\u000D", new ForAllTy(0, new FunTy(new ListTy(new FunTy(new NothingTy(), TypeVar()(0))), TypeVar()(0)))))))(env_bind(e33, "\u001F\u000F\u0015", new ForAllTy(0, new ForAllTy(1, new FunTy(new FunTy(TypeVar()(0), TypeVar()(1)), new FunTy(new ListTy(TypeVar()(0)), new ListTy(TypeVar()(1))))))))))(env_bind(e32, "\u000F\u001B\u000F\u0011\u000E", new ForAllTy(0, new FunTy(new ConstructedTy(new Name(value: "T\u000F\u0013\""), new List<object> { TypeVar()(0) }), TypeVar()(0)))))))(env_bind(e31, "\u001C\u0010\u0015\"", new ForAllTy(0, new FunTy(new FunTy(new NothingTy(), TypeVar()(0)), new ConstructedTy(new Name(value: "T\u000F\u0013\""), new List<object> { TypeVar()(0) })))))))(env_bind(e30, "\u0018\u0019\u0015\u0015\u000D\u0012\u000E-\u0016\u0011\u0015", TextTy()))))(env_bind(e29, "\u001D\u000D\u000E-\u000D\u0012v", new FunTy(TextTy(), TextTy())))))(env_bind(e28, "\u001D\u000D\u000E-\u000F\u0015\u001D\u0013", new ListTy(TextTy())))))(env_bind(e27, "\u000E\u000Dx\u000E-\u0013\u000E\u000F\u0015\u000E\u0013-\u001B\u0011\u000E\u0014", new FunTy(TextTy(), new FunTy(TextTy(), new BooleanTy()))))))(env_bind(e26, "\u000E\u000Dx\u000E-\u0018\u0010\u0012\u000E\u000F\u0011\u0012\u0013", new FunTy(TextTy(), new FunTy(TextTy(), new BooleanTy()))))))(env_bind(e25, "\u000E\u000Dx\u000E-\u0013\u001F\u0017\u0011\u000E", new FunTy(TextTy(), new FunTy(TextTy(), new ListTy(TextTy())))))))(env_bind(e24, "\u0017\u0011\u0013\u000E-\u001C\u0011\u0017\u000D\u0013", new FunTy(TextTy(), new FunTy(TextTy(), new ListTy(TextTy())))))))(env_bind(e23, "\u001C\u0011\u0017\u000D-\u000Dx\u0011\u0013\u000E\u0013", new FunTy(TextTy(), new BooleanTy())))))(env_bind(e22, "\u001B\u0015\u0011\u000E\u000D-\u001C\u0011\u0017\u000D", new FunTy(TextTy(), new FunTy(TextTy(), new NothingTy()))))))(env_bind(e21, "\u0015\u000D\u000F\u0016-\u001C\u0011\u0017\u000D", new FunTy(TextTy(), TextTy())))))(env_bind(e20, "\u0015\u000D\u000F\u0016-\u0017\u0011\u0012\u000D", TextTy()))))(env_bind(e19, "\u001C\u0010\u0017\u0016", new ForAllTy(0, new ForAllTy(1, new FunTy(new FunTy(TypeVar()(1), new FunTy(TypeVar()(0), TypeVar()(1))), new FunTy(TypeVar()(1), new FunTy(new ListTy(TypeVar()(0)), TypeVar()(1))))))))))(env_bind(e18, "\u001C\u0011\u0017\u000E\u000D\u0015", new ForAllTy(0, new FunTy(new FunTy(TypeVar()(0), new BooleanTy()), new FunTy(new ListTy(TypeVar()(0)), new ListTy(TypeVar()(0)))))))))(env_bind(e17, "\u001A\u000F\u001F", new ForAllTy(0, new ForAllTy(1, new FunTy(new FunTy(TypeVar()(0), TypeVar()(1)), new FunTy(new ListTy(TypeVar()(0)), new ListTy(TypeVar()(1))))))))))(env_bind(e16d, "\u0017\u0011\u0013\u000E-\u000F\u000E", new ForAllTy(0, new FunTy(new ListTy(TypeVar()(0)), new FunTy(new IntegerTy(), TypeVar()(0))))))))(env_bind(e16c, "\u0017\u0011\u0013\u000E-\u0013\u0012\u0010\u0018", new ForAllTy(0, new FunTy(new ListTy(TypeVar()(0)), new FunTy(TypeVar()(0), new ListTy(TypeVar()(0)))))))))(env_bind(e16b, "\u000E\u000Dx\u000E-\u0018\u0010\u001A\u001F\u000F\u0015\u000D", new FunTy(TextTy(), new FunTy(TextTy(), new IntegerTy()))))))(env_bind(e16, "\u0017\u0011\u0013\u000E-\u0011\u0012\u0013\u000D\u0015\u000E-\u000F\u000E", new ForAllTy(0, new FunTy(new ListTy(TypeVar()(0)), new FunTy(new IntegerTy(), new FunTy(TypeVar()(0), new ListTy(TypeVar()(0))))))))))(env_bind(e15, "\u0017\u0011\u0013\u000E-\u0017\u000D\u0012\u001D\u000E\u0014", new ForAllTy(0, new FunTy(new ListTy(TypeVar()(0)), new IntegerTy()))))))(env_bind(e14, "\u001F\u0015\u0011\u0012\u000E-\u0017\u0011\u0012\u000D", new FunTy(TextTy(), new NothingTy())))))(env_bind(e13, "\u0013\u0014\u0010\u001B", new ForAllTy(0, new FunTy(TypeVar()(0), TextTy()))))))(env_bind(e12, "\u000E\u000Dx\u000E-\u000E\u0010-\u0011\u0012\u000E\u000D\u001D\u000D\u0015", new FunTy(TextTy(), new IntegerTy())))))(env_bind(e11, "\u000E\u000Dx\u000E-\u0015\u000D\u001F\u0017\u000F\u0018\u000D", new FunTy(TextTy(), new FunTy(TextTy(), new FunTy(TextTy(), TextTy())))))))(env_bind(e10b, "\u0018\u0010\u0016\u000D-\u000E\u0010-\u0018\u0014\u000F\u0015", new FunTy(new IntegerTy(), new CharTy())))))(env_bind(e10, "\u0018\u0014\u000F\u0015-\u0018\u0010\u0016\u000D-\u000F\u000E", new FunTy(TextTy(), new FunTy(new IntegerTy(), new IntegerTy()))))))(env_bind(e9, "\u0018\u0014\u000F\u0015-\u0018\u0010\u0016\u000D", new FunTy(new CharTy(), new IntegerTy())))))(env_bind(e8, "\u0011\u0013-\u001B\u0014\u0011\u000E\u000D\u0013\u001F\u000F\u0018\u000D", new FunTy(new CharTy(), new BooleanTy())))))(env_bind(e7, "\u0011\u0013-\u0016\u0011\u001D\u0011\u000E", new FunTy(new CharTy(), new BooleanTy())))))(env_bind(e6, "\u0011\u0013-\u0017\u000D\u000E\u000E\u000D\u0015", new FunTy(new CharTy(), new BooleanTy())))))(env_bind(e5b, "\u0013\u0019b\u0013\u000E\u0015\u0011\u0012\u001D", new FunTy(TextTy(), new FunTy(new IntegerTy(), new FunTy(new IntegerTy(), TextTy())))))))(env_bind(e5, "\u0018\u0014\u000F\u0015-\u000E\u0010-\u000E\u000Dx\u000E", new FunTy(new CharTy(), TextTy())))))(env_bind(e4, "\u0018\u0014\u000F\u0015-\u000F\u000E", new FunTy(TextTy(), new FunTy(new IntegerTy(), new CharTy()))))))(env_bind(e3, "\u0011\u0012\u000E\u000D\u001D\u000D\u0015-\u000E\u0010-\u000E\u000Dx\u000E", new FunTy(new IntegerTy(), TextTy())))))(env_bind(e2, "\u000E\u000Dx\u000E-\u0017\u000D\u0012\u001D\u000E\u0014", new FunTy(TextTy(), new IntegerTy())))))(env_bind(e(), "\u0012\u000D\u001D\u000F\u000E\u000D", new FunTy(new IntegerTy(), new IntegerTy())))))(empty_type_env());

    public static UnificationState empty_unification_state() => new UnificationState(substitutions: new List<SubstEntry>(), next_id: 2, errors: new List<Diagnostic>());

    public static CodexType fresh_var(UnificationState st) => TypeVar()(st().next_id);

    public static UnificationState advance_id(UnificationState st) => new UnificationState(substitutions: st().substitutions, next_id: (st().next_id + 1), errors: st().errors);

    public static FreshResult fresh_and_advance(UnificationState st) => new FreshResult(var_type: TypeVar()(st().next_id), state: advance_id(st()));

    public static CodexType subst_lookup(long var_id, List<SubstEntry> entries) => ((Func<long, CodexType>)((len) => ((len == 0) ? ErrorTy() : ((Func<long, CodexType>)((pos) => ((pos() >= len) ? ErrorTy() : ((Func<SubstEntry, CodexType>)((entry) => ((entry().var_id == var_id) ? entry().resolved_type : ErrorTy())))(entries()[(int)pos()]))))(bsearch_int_pos(entries(), var_id, 0, len)))))(((long)entries().Count));

    public static bool has_subst(object var_id, object entries) => ((Func<long, bool>)((len) => ((len == 0) ? false : ((Func<long, bool>)((pos) => ((pos() >= len) ? false : (entries()[(int)pos()].var_id == var_id))))(bsearch_int_pos(entries(), var_id, 0, len)))))(((long)entries().Count));

    public static CodexType resolve(UnificationState st, CodexType ty) => ty() switch { object TypeVar => id(), };

    public static bool has_subst() => st().substitutions;

    public static Func<UnificationState, Func<CodexType, CodexType>> resolve() => subst_lookup(id(), st().substitutions);

    public static Func<CodexType, CodexType> ty() => (/* error: _ */ default ? ty() : /* error: : */ default(new UnificationState()));

    public static T51 Integer<T51>() => new CodexType();

    public static Func<List<SubstEntry>, Func<long, Func<List<Diagnostic>, UnificationState>>> UnificationState() => add_subst(st())(var_id)(ty());

    public static SubstEntry entry() => new SubstEntry(var_id: var_id, resolved_type: ty());

    public static long pos() => bsearch_int_pos(st().substitutions, var_id, 0, ((long)st().substitutions.Count));

    public static Func<List<SubstEntry>, Func<long, Func<List<Diagnostic>, UnificationState>>> UnificationState() => substitutions;

    public static T7102 list_insert_at<T7102>() => /* error: . */ default(substitutions)(pos())(entry());

    public static object next_id() => st().next_id;

    public static object errors() => st().errors;

    public static UnificationState add_unify_error(UnificationState st, string code, string msg) => new UnificationState(substitutions: st().substitutions, next_id: st().next_id, errors: Enumerable.Concat(st().errors, new List<Diagnostic> { make_error(code, msg) }).ToList());

    public static List<long> occurs_in(object st, object var_id, object ty) => ((Func<CodexType, object>)((resolved) => resolved switch { object TypeVar => id(), }))(resolve(st(), ty()));

    public static object id() => var_id;

    public static List<long> FunTy(object param, object ret) => (occurs_in(st(), var_id, param) || occurs_in(st(), var_id, ret()));

    public static List<long> ListTy(object elem) => occurs_in(st(), var_id, elem);

    public static UnifyResult unify(UnificationState st, CodexType a, CodexType b) => ((Func<CodexType, UnifyResult>)((ra) => ((Func<CodexType, UnifyResult>)((rb) => unify_resolved(st(), ra, rb)))(resolve(st(), b()))))(resolve(st(), a));

    public static UnifyResult unify_resolved(UnificationState st, CodexType a, CodexType b) => (types_equal(a, b()) ? new UnifyResult(success: true, state: st()) : a switch { object TypeVar => id_a, });

    public static List<long> occurs_in() => id_a(b());

    public static Func<bool, Func<UnificationState, UnifyResult>> UnifyResult() => success;

    public static ParseState state() => add_unify_error(st(), "CDX\u0005\u0003\u0004\u0003", "I\u0012\u001C\u0011\u0012\u0011\u000E\u000D\u0002\u000E\u001E\u001F\u000D");

    public static Func<bool, Func<UnificationState, UnifyResult>> UnifyResult() => success;

    public static ParseState state() => add_subst(st())(id_a)(b());

    public static T7151 unify_rhs<T7151>() => a(b());

    public static bool types_equal(CodexType a, CodexType b) => a switch { object TypeVar => id_a, };

    public static bool b() => (TypeVar()(id_b) ? (id_a == id_b) : /* error: _ */ default);

    public static T5560 IntegerTy<T5560>() => b() switch { IntegerTy { } => true, _ => false, };

    public static bool NumberTy() => b() switch { NumberTy { } => true, _ => false, };

    public static T5562 TextTy<T5562>() => b() switch { object TextTy => true, };

    public static bool BooleanTy() => b() switch { BooleanTy { } => true, _ => false, };

    public static T5564 CharTy<T5564>() => b() switch { CharTy { } => true, _ => false, };

    public static T5566 NothingTy<T5566>() => b() switch { NothingTy { } => true, _ => false, };

    public static bool VoidTy() => b() switch { VoidTy { } => true, _ => false, };

    public static bool ErrorTy() => b() switch { object ErrorTy => true, };

    public static T7151 unify_rhs<T7151>(object st, object a, object b) => b() switch { object TypeVar => id_b, };

    public static List<long> occurs_in() => id_b(a);

    public static Func<bool, Func<UnificationState, UnifyResult>> UnifyResult() => success;

    public static ParseState state() => add_unify_error(st(), "CDX\u0005\u0003\u0004\u0003", "I\u0012\u001C\u0011\u0012\u0011\u000E\u000D\u0002\u000E\u001E\u001F\u000D");

    public static Func<bool, Func<UnificationState, UnifyResult>> UnifyResult() => success;

    public static ParseState state() => add_subst(st())(id_b)(a);

    public static T7185 unify_structural<T7185>() => a(b());

    public static T7185 unify_structural<T7185>(object st, object a, object b) => a switch { IntegerTy { } => b() switch { IntegerTy { } => new UnifyResult(success: true, state: st()), object ErrorTy => new UnifyResult(success: true, state: st()), }, NumberTy { } => b() switch { NumberTy { } => new UnifyResult(success: true, state: st()), object ErrorTy => new UnifyResult(success: true, state: st()), }, object TextTy => b() switch { object TextTy => new UnifyResult(success: true, state: st()), }, };

    public static UnifyResult unify_constructed_args(UnificationState st, List<CodexType> args_a, List<CodexType> args_b, long i, long len)
    {
        while (true)
        {
            if ((i() == len))
            {
            return new UnifyResult(success: true, state: st());
            }
            else
            {
            if ((i() >= ((long)args_b.Count)))
            {
            return new UnifyResult(success: true, state: st());
            }
            else
            {
            var r = unify(st(), args_a[(int)i()], args_b[(int)i()]);
            if (r.success)
            {
            var _tco_0 = r.state;
            var _tco_1 = args_a;
            var _tco_2 = args_b;
            var _tco_3 = (i() + 1);
            var _tco_4 = len;
            st = _tco_0;
            args_a = _tco_1;
            args_b = _tco_2;
            i = _tco_3;
            len = _tco_4;
            continue;
            }
            else
            {
            return r;
            }
            }
            }
        }
    }

    public static UnifyResult unify_fun(UnificationState st, CodexType pa, CodexType ra, CodexType pb, CodexType rb) => ((Func<UnifyResult, UnifyResult>)((r1) => (r1.success ? unify(r1.state, ra, rb) : r1)))(unify(st(), pa, pb));

    public static UnifyResult unify_mismatch(UnificationState st, CodexType a, CodexType b) => new UnifyResult(success: false, state: add_unify_error(st(), "CDX\u0005\u0003\u0003\u0004", ("T\u001E\u001F\u000D\u0002\u001A\u0011\u0013\u001A\u000F\u000E\u0018\u0014:\u0002" + (type_tag(a) + ("\u0002v\u0013\u0002" + type_tag(b()))))));

    public static string type_tag(CodexType ty) => ty() switch { IntegerTy { } => "I\u0012\u000E\u000D\u001D\u000D\u0015", NumberTy { } => "N\u0019\u001Ab\u000D\u0015", object TextTy => "T\u000Dx\u000E", };

    public static string integer_to_text() => (new ForAllTy(id(), body()) ? "F\u0010\u0015A\u0017\u0017" : new SumTy(name(), ctors));

    public static object name() => value;

    public static string RecordTy(object name, object fields) => ("R\u000D\u0018:" + name().value);

    public static T793 ConstructedTy<T793>(object name, object args) => ("C\u0010\u0012:" + name().value);

    public static CodexType deep_resolve(UnificationState st, CodexType ty) => ((Func<CodexType, CodexType>)((resolved) => resolved switch { FunTy(var param, var ret) => new FunTy(deep_resolve(st(), param), deep_resolve(st(), ret())), ListTy(var elem) => new ListTy(deep_resolve(st(), elem)), ConstructedTy(var name, var args) => new ConstructedTy(name(), deep_resolve_list(st(), args, 0, ((long)args.Count), new List<CodexType>())), ForAllTy(var id, var body) => new ForAllTy(id(), deep_resolve(st(), body())), SumTy(var name, var ctors) => resolved, RecordTy(var name, var fields) => resolved, _ => resolved, }))(resolve(st(), ty()));

    public static List<CodexType> deep_resolve_list(UnificationState st, List<CodexType> args, long i, long len, List<CodexType> acc)
    {
        while (true)
        {
            if ((i() == len))
            {
            return acc();
            }
            else
            {
            var _tco_0 = st();
            var _tco_1 = args;
            var _tco_2 = (i() + 1);
            var _tco_3 = len;
            var _tco_4 = ((Func<List<CodexType>>)(() => { var _l = acc(); _l.Add(deep_resolve(st(), args[(int)i()])); return _l; }))();
            st = _tco_0;
            args = _tco_1;
            i = _tco_2;
            len = _tco_3;
            acc = _tco_4;
            continue;
            }
        }
    }

    public static string compile(string source, string module_name) => ((Func<List<Token>, string>)((tokens) => ((Func<ParseState, string>)((st) => ((Func<Document, string>)((doc) => ((Func<AModule, string>)((ast) => ((Func<ModuleResult, string>)((check_result) => ((Func<IRModule, string>)((ir) => emit_full_module(ir, ast.type_defs)))(lower_module(ast, check_result.types, check_result.state))))(check_module(ast))))(desugar_document(doc, module_name))))(parse_document(st()))))(make_parse_state(tokens))))(tokenize(source));

    public static CompileResult compile_checked(string source, string module_name) => compile_with_imports(source, module_name, new List<ResolveResult>());

    public static CompileResult compile_with_imports(string source, string module_name, List<ResolveResult> imported) => ((Func<List<Token>, CompileResult>)((tokens) => ((Func<ParseState, CompileResult>)((st) => ((Func<Document, CompileResult>)((doc) => ((Func<AModule, CompileResult>)((ast) => ((Func<ResolveResult, CompileResult>)((resolve_result) => ((((long)resolve_result.errors.Count) > 0) ? new CompileError(resolve_result.errors) : ((Func<ModuleResult, CompileResult>)((check_result) => ((Func<IRModule, CompileResult>)((ir) => new CompileOk(emit_full_module(ir, ast.type_defs), check_result)))(lower_module(ast, check_result.types, check_result.state))))(check_module(ast)))))(resolve_module_with_imports(ast, imported))))(desugar_document(doc, module_name))))(parse_document(st()))))(make_parse_state(tokens))))(tokenize(source));

    public static object compile_streaming(string source, string module_name) => ((Func<List<Token>, object>)((tokens) => ((Func<ParseState, object>)((st) => ((Func<Document, object>)((doc) => ((Func<AModule, object>)((ast) => ((Func<ModuleResult, object>)((check_result) => ((Func<List<TypeBinding>, object>)((ctor_types) => ((Func<List<TypeBinding>, object>)((all_types) => ((Func<List<string>, object>)((ctor_names) => ((Func<object>)(() => { ((Func<object>)(() => { Console.WriteLine(_Cce.ToUnicode(codex_emit_type_defs(ast.type_defs, 0))); return null; }))(); stream_defs(ast.defs)(all_types)(check_result.state)(ctor_names)(0)(((long)ast.defs.Count)); ((Func<object>)(() => { Console.WriteLine(_Cce.ToUnicode("")); return null; }))(); stream_defs; /* error: : */ default(new List())(new ADef()); /* error: -> */ default(new List())(TypeBinding); /* error: -> */ default(new UnificationState()); /* error: -> */ default(new List())(Text()); /* error: -> */ default(new Integer()); /* error: -> */ default(new Integer()); /* error: -> */ default(new List<object> { new Console() })(new Nothing()); stream_defs(defs)(types)(ust)(ctor_names)(i())(len); /* error: = */ default; ((i() == len) ? ((Func<object>)(() => { Console.WriteLine(_Cce.ToUnicode("")); return null; }))() : ((Func<T0, object>)((def) => ((Func<IRDef, object>)((ir_def) => ((Func<string, object>)((text) => ((Func<object>)(() => { ((Func<object>)(() => { Console.WriteLine(_Cce.ToUnicode(text())); return null; }))(); stream_defs(defs)(types)(ust)(ctor_names)((i() + 1))(len); main; /* error: : */ default(new List<object> { new Console(), new FileSystem() })(new Nothing()); main; /* error: = */ default; ((Func<object>)(() => { var path = _Cce.FromUnicode(Console.ReadLine() ?? ""); var source = _Cce.FromUnicode(File.ReadAllText(_Cce.ToUnicode(path))); compile_streaming(source, "P\u0015\u0010\u001D\u0015\u000F\u001A");  return null; }))();  return null; }))()))(codex_emit_def(ir_def, ctor_names))))(lower_def(def(), types, ust))))(defs[(int)i()]));  return null; }))()))(codex_collect_ctor_names(ast.type_defs, 0))))(Enumerable.Concat(ctor_types, Enumerable.Concat(check_result.types, builtin_type_env().bindings).ToList()).ToList())))(collect_ctor_bindings(ast.type_defs, 0, ((long)ast.type_defs.Count), new List<TypeBinding>()))))(check_module(ast))))(desugar_document(doc, module_name))))(parse_document(st()))))(make_parse_state(tokens))))(tokenize(source));

}
