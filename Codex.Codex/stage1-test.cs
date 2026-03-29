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

public sealed record EffectOpsResult(List<EffectOpDef> ops, ParseState state);

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

public abstract record Expr;
public sealed record LitExpr(Token Field0) : Expr;
public sealed record NameExpr(Token Field0) : Expr;
public sealed record AppExpr(Expr Field0, Expr Field1) : Expr;
public sealed record BinExpr(Expr Field0, Token Field1, Expr Field2) : Expr;
public sealed record UnaryExpr(Token Field0, Expr Field1) : Expr;
public sealed record IfExpr(Expr Field0, Expr Field1, Expr Field2) : Expr;
public sealed record LetExpr(List<LetBind> Field0, Expr Field1) : Expr;
public sealed record MatchExpr(Expr Field0, List<MatchArm> Field1) : Expr;
public sealed record ListExpr(List<Expr> Field0) : Expr;
public sealed record RecordExpr(Token Field0, List<RecordFieldExpr> Field1) : Expr;
public sealed record FieldExpr(Expr Field0, Token Field1) : Expr;
public sealed record ParenExpr(Expr Field0) : Expr;
public sealed record DoExpr(List<DoStmt> Field0) : Expr;
public sealed record HandleExpr(Token Field0, Expr Field1, List<HandleClause> Field2) : Expr;
public sealed record LambdaExpr(List<Token> Field0, Expr Field1) : Expr;
public sealed record ErrExpr(Token Field0) : Expr;


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


public abstract record TypeExpr;
public sealed record NamedType(Token Field0) : TypeExpr;
public sealed record FunType(TypeExpr Field0, TypeExpr Field1) : TypeExpr;
public sealed record AppType(TypeExpr Field0, List<TypeExpr> Field1) : TypeExpr;
public sealed record ParenType(TypeExpr Field0) : TypeExpr;
public sealed record ListType(TypeExpr Field0) : TypeExpr;
public sealed record LinearTypeExpr(TypeExpr Field0) : TypeExpr;
public sealed record EffectTypeExpr(List<Token> Field0, TypeExpr Field1) : TypeExpr;


public sealed record TypeAnn(Token name, TypeExpr type_expr);

public sealed record Def(Token name, List<Token> @params, List<TypeAnn> ann, Expr body);

public sealed record RecordFieldDef(Token name, TypeExpr type_expr);

public sealed record VariantCtorDef(Token name, List<TypeExpr> fields);

public sealed record EffectOpDef(Token name, TypeExpr type_expr);

public sealed record EffectDef(Token name, List<EffectOpDef> ops);

public abstract record TypeBody;
public sealed record RecordBody(List<RecordFieldDef> Field0) : TypeBody;
public sealed record VariantBody(List<VariantCtorDef> Field0) : TypeBody;


public sealed record TypeDef(Token name, List<Token> type_params, TypeBody body);

public sealed record ImportDecl(Token module_name);

public sealed record Document(List<Def> defs, List<TypeDef> type_defs, List<EffectDef> effect_defs, List<ImportDecl> imports);

public sealed record Token(TokenKind kind, string text, long offset, long line, long column);

public abstract record TokenKind;
public sealed record EndOfFile : TokenKind;
public sealed record Newline : TokenKind;
public sealed record Indent : TokenKind;
public sealed record Dedent : TokenKind;
public sealed record IntegerLiteral : TokenKind;
public sealed record NumberLiteral : TokenKind;
public sealed record TextLiteral : TokenKind;
public sealed record CharLiteral : TokenKind;
public sealed record TrueKeyword : TokenKind;
public sealed record FalseKeyword : TokenKind;
public sealed record Identifier : TokenKind;
public sealed record TypeIdentifier : TokenKind;
public sealed record ProseText : TokenKind;
public sealed record ChapterHeader : TokenKind;
public sealed record SectionHeader : TokenKind;
public sealed record LetKeyword : TokenKind;
public sealed record InKeyword : TokenKind;
public sealed record IfKeyword : TokenKind;
public sealed record ThenKeyword : TokenKind;
public sealed record ElseKeyword : TokenKind;
public sealed record WhenKeyword : TokenKind;
public sealed record WhereKeyword : TokenKind;
public sealed record SuchThatKeyword : TokenKind;
public sealed record DoKeyword : TokenKind;
public sealed record RecordKeyword : TokenKind;
public sealed record ImportKeyword : TokenKind;
public sealed record ExportKeyword : TokenKind;
public sealed record ClaimKeyword : TokenKind;
public sealed record ProofKeyword : TokenKind;
public sealed record ForAllKeyword : TokenKind;
public sealed record ThereExistsKeyword : TokenKind;
public sealed record LinearKeyword : TokenKind;
public sealed record EffectKeyword : TokenKind;
public sealed record WithKeyword : TokenKind;
public sealed record Equals_ : TokenKind;
public sealed record Colon : TokenKind;
public sealed record Arrow : TokenKind;
public sealed record LeftArrow : TokenKind;
public sealed record Pipe : TokenKind;
public sealed record Ampersand : TokenKind;
public sealed record Plus : TokenKind;
public sealed record Minus : TokenKind;
public sealed record Star : TokenKind;
public sealed record Slash : TokenKind;
public sealed record Caret : TokenKind;
public sealed record PlusPlus : TokenKind;
public sealed record ColonColon : TokenKind;
public sealed record DoubleEquals : TokenKind;
public sealed record NotEquals : TokenKind;
public sealed record LessThan : TokenKind;
public sealed record GreaterThan : TokenKind;
public sealed record LessOrEqual : TokenKind;
public sealed record GreaterOrEqual : TokenKind;
public sealed record TripleEquals : TokenKind;
public sealed record Turnstile : TokenKind;
public sealed record LinearProduct : TokenKind;
public sealed record ForAllSymbol : TokenKind;
public sealed record ExistsSymbol : TokenKind;
public sealed record LeftParen : TokenKind;
public sealed record RightParen : TokenKind;
public sealed record LeftBracket : TokenKind;
public sealed record RightBracket : TokenKind;
public sealed record LeftBrace : TokenKind;
public sealed record RightBrace : TokenKind;
public sealed record Comma : TokenKind;
public sealed record Dot : TokenKind;
public sealed record DashGreater : TokenKind;
public sealed record Underscore : TokenKind;
public sealed record Backslash : TokenKind;
public sealed record ErrorToken : TokenKind;


public abstract record CodexType;
public sealed record IntegerTy : CodexType;
public sealed record NumberTy : CodexType;
public sealed record TextTy : CodexType;
public sealed record BooleanTy : CodexType;
public sealed record CharTy : CodexType;
public sealed record VoidTy : CodexType;
public sealed record NothingTy : CodexType;
public sealed record ErrorTy : CodexType;
public sealed record FunTy(CodexType Field0, CodexType Field1) : CodexType;
public sealed record ListTy(CodexType Field0) : CodexType;
public sealed record TypeVar(long Field0) : CodexType;
public sealed record ForAllTy(long Field0, CodexType Field1) : CodexType;
public sealed record SumTy(Name Field0, List<SumCtor> Field1) : CodexType;
public sealed record RecordTy(Name Field0, List<RecordField> Field1) : CodexType;
public sealed record ConstructedTy(Name Field0, List<CodexType> Field1) : CodexType;
public sealed record EffectfulTy(List<Name> Field0, CodexType Field1) : CodexType;


public sealed record SumCtor(Name name, List<CodexType> fields);

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

public sealed record TypeEnv(List<TypeBinding> bindings);

public sealed record TypeBinding(string name, CodexType bound_type);

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
            return new AIfExpr(desugar_expr(c), desugar_expr(t), desugar_expr(e));
            }
            else if (_tco_s is LetExpr _tco_m6)
            {
                var bindings = _tco_m6.Field0;
                var body = _tco_m6.Field1;
            return new ALetExpr(map_list(desugar_let_bind, bindings), desugar_expr(body));
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
            return new AHandleExpr(make_name(eff_tok.text), desugar_expr(body), map_list(desugar_handle_clause, clauses));
            }
            else if (_tco_s is LambdaExpr _tco_m14)
            {
                var @params = _tco_m14.Field0;
                var body = _tco_m14.Field1;
            return new ALambdaExpr(map_list(desugar_lambda_param, @params), desugar_expr(body));
            }
            else if (_tco_s is ErrExpr _tco_m15)
            {
                var tok = _tco_m15.Field0;
            return new AErrorExpr(tok.text);
            }
        }
    }

    public static AExpr desugar_literal(Token tok) => (is_literal(tok.kind) ? new ALitExpr(tok.text, classify_literal(tok.kind)) : new AErrorExpr(tok.text));

    public static LiteralKind classify_literal(TokenKind k) => k switch { IntegerLiteral { } => new IntLit(), NumberLiteral { } => new NumLit(), TextLiteral { } => new TextLit(), CharLiteral { } => new CharLit(), TrueKeyword { } => new BoolLit(), FalseKeyword { } => new BoolLit(), _ => new TextLit(), };

    public static ALetBind desugar_let_bind(LetBind b) => new ALetBind(name: make_name(b.name.text), value: desugar_expr(b.value));

    public static AMatchArm desugar_match_arm(MatchArm arm) => new AMatchArm(pattern: desugar_pattern(arm.pattern), body: desugar_expr(arm.body));

    public static AFieldExpr desugar_field_expr(RecordFieldExpr f) => new AFieldExpr(name: make_name(f.name.text), value: desugar_expr(f.value));

    public static ADoStmt desugar_do_stmt(DoStmt s) => s switch { DoBindStmt(var tok, var val) => new ADoBindStmt(make_name(tok.text), desugar_expr(val)), DoExprStmt(var e) => new ADoExprStmt(desugar_expr(e)), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static AHandleClause desugar_handle_clause(HandleClause c) => new AHandleClause(op_name: make_name(c.op_name.text), resume_name: make_name(c.resume_name.text), body: desugar_expr(c.body));

    public static Name desugar_lambda_param(Token tok) => make_name(tok.text);

    public static BinaryOp desugar_bin_op(TokenKind k) => k switch { Plus { } => new OpAdd(), Minus { } => new OpSub(), Star { } => new OpMul(), Slash { } => new OpDiv(), Caret { } => new OpPow(), DoubleEquals { } => new OpEq(), NotEquals { } => new OpNotEq(), LessThan { } => new OpLt(), GreaterThan { } => new OpGt(), LessOrEqual { } => new OpLtEq(), GreaterOrEqual { } => new OpGtEq(), TripleEquals { } => new OpDefEq(), PlusPlus { } => new OpAppend(), ColonColon { } => new OpCons(), Ampersand { } => new OpAnd(), Pipe { } => new OpOr(), _ => new OpAdd(), };

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
            return new AFunType(desugar_type_expr(param), desugar_type_expr(ret));
            }
            else if (_tco_s is AppType _tco_m2)
            {
                var ctor = _tco_m2.Field0;
                var args = _tco_m2.Field1;
            return new AAppType(desugar_type_expr(ctor), map_list(desugar_type_expr, args));
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
            return new AAppType(new ANamedType(make_name("List")), new List<ATypeExpr> { desugar_type_expr(elem) });
            }
            else if (_tco_s is LinearTypeExpr _tco_m5)
            {
                var inner = _tco_m5.Field0;
            var _tco_0 = inner;
            t = _tco_0;
            continue;
            }
            else if (_tco_s is EffectTypeExpr _tco_m6)
            {
                var effs = _tco_m6.Field0;
                var ret = _tco_m6.Field1;
            return new AEffectType(map_list(make_type_param_name, effs), desugar_type_expr(ret));
            }
        }
    }

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

    public static List<T233> map_list<T223, T233>(Func<T223, T233> f, List<T223> xs) => map_list_loop(f, xs, 0, ((long)xs.Count), new List<T233>());

    public static List<T248> map_list_loop<T247, T248>(Func<T247, T248> f, List<T247> xs, long i, long len, List<T248> acc)
    {
        while (true)
        {
            if ((i == len))
            {
            return acc;
            }
            else
            {
            var _tco_0 = f;
            var _tco_1 = xs;
            var _tco_2 = (i + 1);
            var _tco_3 = len;
            var _tco_4 = ((Func<List<T248>>)(() => { var _l = acc; _l.Add(f(xs[(int)i])); return _l; }))();
            f = _tco_0;
            xs = _tco_1;
            i = _tco_2;
            len = _tco_3;
            acc = _tco_4;
            continue;
            }
        }
    }

    public static T261 fold_list<T261, T252>(Func<T261, Func<T252, T261>> f, T261 z, List<T252> xs) => fold_list_loop(f, z, xs, 0, ((long)xs.Count));

    public static T275 fold_list_loop<T275, T270>(Func<T275, Func<T270, T275>> f, T275 z, List<T270> xs, long i, long len)
    {
        while (true)
        {
            if ((i == len))
            {
            return z;
            }
            else
            {
            var _tco_0 = f;
            var _tco_1 = f(z)(xs[(int)i]);
            var _tco_2 = xs;
            var _tco_3 = (i + 1);
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
            var mid_name = bindings[(int)mid].name;
            if (((long)string.CompareOrdinal(name, mid_name) <= 0))
            {
            var _tco_0 = bindings;
            var _tco_1 = name;
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
            var _tco_0 = bindings;
            var _tco_1 = name;
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
            if ((key <= entries[(int)mid].var_id))
            {
            var _tco_0 = entries;
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
            var _tco_0 = entries;
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
            if (((long)string.CompareOrdinal(name, names[(int)mid]) <= 0))
            {
            var _tco_0 = names;
            var _tco_1 = name;
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
            var _tco_1 = name;
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

    public static Diagnostic make_error(string code, string msg) => new Diagnostic(code: code, message: msg, severity: new Error());

    public static Diagnostic make_warning(string code, string msg) => new Diagnostic(code: code, message: msg, severity: new Warning());

    public static string severity_label(DiagnosticSeverity s) => s switch { Error { } => "error", Warning { } => "warning", Info { } => "info", _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static string diagnostic_display(Diagnostic d) => (severity_label(d.severity) + (" " + (d.code + (": " + d.message))));

    public static Name make_name(string s) => new Name(value: s);

    public static string name_value(Name n) => n.value;

    public static SourcePosition make_position(long line, long col, long offset) => new SourcePosition(line: line, column: col, offset: offset);

    public static SourceSpan make_span(SourcePosition s, SourcePosition e, string f) => new SourceSpan(start: s, end: e, file: f);

    public static long span_length(SourceSpan span) => (span.end.offset - span.start.offset);

    public static string emit_type_defs(List<ATypeDef> tds, long i) => ((i == ((long)tds.Count)) ? "" : (emit_type_def(tds[(int)i]) + ("\u0001" + emit_type_defs(tds, (i + 1)))));

    public static string emit_type_def(ATypeDef td) => td switch { ARecordTypeDef(var name, var tparams, var fields) => ((Func<string, string>)((gen) => ("public sealed record " + (sanitize(name.value) + (gen + ("(" + (emit_record_field_defs(fields, tparams, 0) + ");\u0001")))))))(emit_tparameter_suffix(tparams)), AVariantTypeDef(var name, var tparams, var ctors) => ((Func<string, string>)((gen) => ("public abstract record " + (sanitize(name.value) + (gen + (";\u0001" + (emit_variant_ctors(ctors, name, tparams, 0) + "\u0001")))))))(emit_tparameter_suffix(tparams)), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static string emit_tparameter_suffix(List<Name> tparams) => ((((long)tparams.Count) == 0) ? "" : ("<" + (emit_tparameter_names(tparams, 0) + ">")));

    public static string emit_tparameter_names(List<Name> tparams, long i) => ((i == ((long)tparams.Count)) ? "" : ((i == (((long)tparams.Count) - 1)) ? ("T" + _Cce.FromUnicode(i.ToString())) : ("T" + (_Cce.FromUnicode(i.ToString()) + (", " + emit_tparameter_names(tparams, (i + 1)))))));

    public static string emit_record_field_defs(List<ARecordFieldDef> fields, List<Name> tparams, long i) => ((i == ((long)fields.Count)) ? "" : ((Func<ARecordFieldDef, string>)((f) => (emit_type_expr_tp(f.type_expr, tparams) + (" " + (sanitize(f.name.value) + (((i < (((long)fields.Count) - 1)) ? ", " : "") + emit_record_field_defs(fields, tparams, (i + 1))))))))(fields[(int)i]));

    public static string emit_variant_ctors(List<AVariantCtorDef> ctors, Name base_name, List<Name> tparams, long i) => ((i == ((long)ctors.Count)) ? "" : ((Func<AVariantCtorDef, string>)((c) => (emit_variant_ctor(c, base_name, tparams) + emit_variant_ctors(ctors, base_name, tparams, (i + 1)))))(ctors[(int)i]));

    public static string emit_variant_ctor(AVariantCtorDef c, Name base_name, List<Name> tparams) => ((Func<string, string>)((gen) => ((((long)c.fields.Count) == 0) ? ("public sealed record " + (sanitize(c.name.value) + (gen + (" : " + (sanitize(base_name.value) + (gen + ";\u0001")))))) : ("public sealed record " + (sanitize(c.name.value) + (gen + ("(" + (emit_ctor_fields(c.fields, tparams, 0) + (") : " + (sanitize(base_name.value) + (gen + ";\u0001")))))))))))(emit_tparameter_suffix(tparams));

    public static string emit_ctor_fields(List<ATypeExpr> fields, List<Name> tparams, long i) => ((i == ((long)fields.Count)) ? "" : (emit_type_expr_tp(fields[(int)i], tparams) + (" Field" + (_Cce.FromUnicode(i.ToString()) + (((i < (((long)fields.Count) - 1)) ? ", " : "") + emit_ctor_fields(fields, tparams, (i + 1)))))));

    public static string emit_type_expr(ATypeExpr te) => emit_type_expr_tp(te, new List<Name>());

    public static string emit_type_expr_tp(ATypeExpr te, List<Name> tparams) => te switch { ANamedType(var name) => ((Func<long, string>)((idx) => ((idx >= 0) ? ("T" + _Cce.FromUnicode(idx.ToString())) : when_type_name(name.value))))(find_tparam_index(tparams, name.value, 0)), AFunType(var p, var r) => ("Func<" + (emit_type_expr_tp(p, tparams) + (", " + (emit_type_expr_tp(r, tparams) + ">")))), AAppType(var @base, var args) => (emit_type_expr_tp(@base, tparams) + ("<" + (emit_type_expr_list_tp(args, tparams, 0) + ">"))), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static long find_tparam_index(List<Name> tparams, string name, long i)
    {
        while (true)
        {
            if ((i == ((long)tparams.Count)))
            {
            return (0 - 1);
            }
            else
            {
            if ((tparams[(int)i].value == name))
            {
            return i;
            }
            else
            {
            var _tco_0 = tparams;
            var _tco_1 = name;
            var _tco_2 = (i + 1);
            tparams = _tco_0;
            name = _tco_1;
            i = _tco_2;
            continue;
            }
            }
        }
    }

    public static string when_type_name(string n) => ((n == "Integer") ? "long" : ((n == "Number") ? "decimal" : ((n == "Text") ? "string" : ((n == "Boolean") ? "bool" : ((n == "List") ? "List" : sanitize(n))))));

    public static string emit_type_expr_list(List<ATypeExpr> args, long i) => ((i == ((long)args.Count)) ? "" : (emit_type_expr(args[(int)i]) + (((i < (((long)args.Count) - 1)) ? ", " : "") + emit_type_expr_list(args, (i + 1)))));

    public static string emit_type_expr_list_tp(List<ATypeExpr> args, List<Name> tparams, long i) => ((i == ((long)args.Count)) ? "" : (emit_type_expr_tp(args[(int)i], tparams) + (((i < (((long)args.Count) - 1)) ? ", " : "") + emit_type_expr_list_tp(args, tparams, (i + 1)))));

    public static List<long> collect_type_var_ids(CodexType ty, List<long> acc)
    {
        while (true)
        {
            var _tco_s = ty;
            if (_tco_s is TypeVar _tco_m0)
            {
                var id = _tco_m0.Field0;
            if (list_contains_int(acc, id))
            {
            return acc;
            }
            else
            {
            return list_append_int(acc, id);
            }
            }
            else if (_tco_s is FunTy _tco_m1)
            {
                var p = _tco_m1.Field0;
                var r = _tco_m1.Field1;
            var _tco_0 = r;
            var _tco_1 = collect_type_var_ids(p, acc);
            ty = _tco_0;
            acc = _tco_1;
            continue;
            }
            else if (_tco_s is ListTy _tco_m2)
            {
                var elem = _tco_m2.Field0;
            var _tco_0 = elem;
            var _tco_1 = acc;
            ty = _tco_0;
            acc = _tco_1;
            continue;
            }
            else if (_tco_s is ForAllTy _tco_m3)
            {
                var id = _tco_m3.Field0;
                var body = _tco_m3.Field1;
            var _tco_0 = body;
            var _tco_1 = acc;
            ty = _tco_0;
            acc = _tco_1;
            continue;
            }
            else if (_tco_s is ConstructedTy _tco_m4)
            {
                var name = _tco_m4.Field0;
                var args = _tco_m4.Field1;
            return collect_type_var_ids_list(args, acc);
            }
            {
            return acc;
            }
        }
    }

    public static List<long> collect_type_var_ids_list(List<CodexType> types, List<long> acc) => collect_type_var_ids_list_loop(types, acc, 0, ((long)types.Count));

    public static List<long> collect_type_var_ids_list_loop(List<CodexType> types, List<long> acc, long i, long len)
    {
        while (true)
        {
            if ((i == len))
            {
            return acc;
            }
            else
            {
            var _tco_0 = types;
            var _tco_1 = collect_type_var_ids(types[(int)i], acc);
            var _tco_2 = (i + 1);
            var _tco_3 = len;
            types = _tco_0;
            acc = _tco_1;
            i = _tco_2;
            len = _tco_3;
            continue;
            }
        }
    }

    public static bool list_contains_int(List<long> xs, long n) => list_contains_int_loop(xs, n, 0, ((long)xs.Count));

    public static bool list_contains_int_loop(List<long> xs, long n, long i, long len)
    {
        while (true)
        {
            if ((i == len))
            {
            return false;
            }
            else
            {
            if ((xs[(int)i] == n))
            {
            return true;
            }
            else
            {
            var _tco_0 = xs;
            var _tco_1 = n;
            var _tco_2 = (i + 1);
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

    public static string generic_suffix(CodexType ty) => ((Func<List<long>, string>)((ids) => ((((long)ids.Count) == 0) ? "" : ("<" + (emit_type_params(ids, 0) + ">")))))(collect_type_var_ids(ty, new List<long>()));

    public static string emit_type_params(List<long> ids, long i) => ((i == ((long)ids.Count)) ? "" : ((i == (((long)ids.Count) - 1)) ? ("T" + _Cce.FromUnicode(ids[(int)i].ToString())) : ("T" + (_Cce.FromUnicode(ids[(int)i].ToString()) + (", " + emit_type_params(ids, (i + 1)))))));

    public static string extract_ctor_type_args(CodexType ty) => ty switch { ConstructedTy(var name, var args) => ((((long)args.Count) == 0) ? "" : ("<" + (emit_cs_type_args(args, 0) + ">"))), _ => "", };

    public static bool is_self_call(IRExpr e, string func_name) => ((Func<ApplyChain, bool>)((chain) => is_self_call_root(chain.root, func_name)))(collect_apply_chain(e, new List<IRExpr>()));

    public static bool is_self_call_root(IRExpr e, string func_name) => e switch { IrName(var n, var ty) => (n == func_name), _ => false, };

    public static bool has_tail_call(IRExpr e, string func_name)
    {
        while (true)
        {
            var _tco_s = e;
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
            var _tco_0 = body;
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
            return is_self_call(e, func_name);
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
            if ((i == ((long)branches.Count)))
            {
            return false;
            }
            else
            {
            var b = branches[(int)i];
            if (has_tail_call(b.body, func_name))
            {
            return true;
            }
            else
            {
            var _tco_0 = branches;
            var _tco_1 = func_name;
            var _tco_2 = (i + 1);
            branches = _tco_0;
            func_name = _tco_1;
            i = _tco_2;
            continue;
            }
            }
        }
    }

    public static bool should_tco(IRDef d) => ((((long)d.@params.Count) == 0) ? false : has_tail_call(d.body, d.name));

    public static string emit_tco_def(IRDef d, List<ArityEntry> arities) => ((Func<CodexType, string>)((ret) => ((Func<string, string>)((gen) => ("    public static " + (cs_type(ret) + (" " + (sanitize(d.name) + (gen + ("(" + (emit_def_params(d.@params, 0) + (")\u0001    {\u0001        while (true)\u0001        {\u0001" + (emit_tco_body(d.body, d.name, d.@params, arities) + "        }\u0001    }\u0001")))))))))))(generic_suffix(d.type_val))))(get_return_type(d.type_val, ((long)d.@params.Count)));

    public static string emit_tco_body(IRExpr e, string func_name, List<IRParam> @params, List<ArityEntry> arities) => e switch { IrIf(var c, var t, var el, var ty) => emit_tco_if(c, t, el, func_name, @params, arities), IrLet(var name, var ty, var val, var body) => emit_tco_let(name, ty, val, body, func_name, @params, arities), IrMatch(var scrut, var branches, var ty) => emit_tco_match(scrut, branches, func_name, @params, arities), IrApply(var f, var a, var rty) => emit_tco_apply(e, func_name, @params, arities), _ => ("            return " + (emit_expr(e, arities) + ";\u0001")), };

    public static string emit_tco_apply(IRExpr e, string func_name, List<IRParam> @params, List<ArityEntry> arities) => (is_self_call(e, func_name) ? emit_tco_jump(e, @params, arities) : ("            return " + (emit_expr(e, arities) + ";\u0001")));

    public static string emit_tco_if(IRExpr cond, IRExpr t, IRExpr el, string func_name, List<IRParam> @params, List<ArityEntry> arities) => ("            if (" + (emit_expr(cond, arities) + (")\u0001            {\u0001" + (emit_tco_body(t, func_name, @params, arities) + ("            }\u0001            else\u0001            {\u0001" + (emit_tco_body(el, func_name, @params, arities) + "            }\u0001"))))));

    public static string emit_tco_let(string name, CodexType ty, IRExpr val, IRExpr body, string func_name, List<IRParam> @params, List<ArityEntry> arities) => ("            var " + (sanitize(name) + (" = " + (emit_expr(val, arities) + (";\u0001" + emit_tco_body(body, func_name, @params, arities))))));

    public static string emit_tco_match(IRExpr scrut, List<IRBranch> branches, string func_name, List<IRParam> @params, List<ArityEntry> arities) => ("            var _tco_s = " + (emit_expr(scrut, arities) + (";\u0001" + emit_tco_match_branches(branches, func_name, @params, arities, 0, true))));

    public static string emit_tco_match_branches(List<IRBranch> branches, string func_name, List<IRParam> @params, List<ArityEntry> arities, long i, bool is_first) => ((i == ((long)branches.Count)) ? "" : ((Func<IRBranch, string>)((b) => (emit_tco_match_branch(b, func_name, @params, arities, i, is_first) + emit_tco_match_branches(branches, func_name, @params, arities, (i + 1), false))))(branches[(int)i]));

    public static string emit_tco_match_branch(IRBranch b, string func_name, List<IRParam> @params, List<ArityEntry> arities, long idx, bool is_first) => b.pattern switch { IrWildPat { } => ("            {\u0001" + (emit_tco_body(b.body, func_name, @params, arities) + "            }\u0001")), IrVarPat(var name, var ty) => ("            {\u0001                var " + (sanitize(name) + (" = _tco_s;\u0001" + (emit_tco_body(b.body, func_name, @params, arities) + "            }\u0001")))), IrCtorPat(var name, var subs, var ty) => ((Func<string, string>)((keyword) => ((Func<string, string>)((match_var) => ("            " + (keyword + (" (_tco_s is " + (sanitize(name) + (" " + (match_var + (")\u0001            {\u0001" + (emit_tco_ctor_bindings(subs, match_var, 0) + (emit_tco_body(b.body, func_name, @params, arities) + "            }\u0001")))))))))))(("_tco_m" + _Cce.FromUnicode(idx.ToString())))))((is_first ? "if" : "else if")), IrLitPat(var text, var ty) => ((Func<string, string>)((keyword) => ("            " + (keyword + (" (object.Equals(_tco_s, " + (text + ("))\u0001            {\u0001" + (emit_tco_body(b.body, func_name, @params, arities) + "            }\u0001"))))))))((is_first ? "if" : "else if")), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static string emit_tco_ctor_bindings(List<IRPat> subs, string match_var, long i) => ((i == ((long)subs.Count)) ? "" : ((Func<IRPat, string>)((sub) => (emit_tco_ctor_binding(sub, match_var, i) + emit_tco_ctor_bindings(subs, match_var, (i + 1)))))(subs[(int)i]));

    public static string emit_tco_ctor_binding(IRPat sub, string match_var, long i) => sub switch { IrVarPat(var name, var ty) => ("                var " + (sanitize(name) + (" = " + (match_var + (".Field" + (_Cce.FromUnicode(i.ToString()) + ";\u0001")))))), _ => "", };

    public static string emit_tco_jump(IRExpr e, List<IRParam> @params, List<ArityEntry> arities) => ((Func<ApplyChain, string>)((chain) => (emit_tco_temps(chain.args, arities, 0) + (emit_tco_assigns(@params, 0) + "            continue;\u0001"))))(collect_apply_chain(e, new List<IRExpr>()));

    public static string emit_tco_temps(List<IRExpr> args, List<ArityEntry> arities, long i) => ((i == ((long)args.Count)) ? "" : ("            var _tco_" + (_Cce.FromUnicode(i.ToString()) + (" = " + (emit_expr(args[(int)i], arities) + (";\u0001" + emit_tco_temps(args, arities, (i + 1))))))));

    public static string emit_tco_assigns(List<IRParam> @params, long i) => ((i == ((long)@params.Count)) ? "" : ((Func<IRParam, string>)((p) => ("            " + (sanitize(p.name) + (" = _tco_" + (_Cce.FromUnicode(i.ToString()) + (";\u0001" + emit_tco_assigns(@params, (i + 1)))))))))(@params[(int)i]));

    public static string emit_def(IRDef d, List<ArityEntry> arities) => (should_tco(d) ? emit_tco_def(d, arities) : ((Func<CodexType, string>)((ret) => ((Func<string, string>)((gen) => ("    public static " + (cs_type(ret) + (" " + (sanitize(d.name) + (gen + ("(" + (emit_def_params(d.@params, 0) + (") => " + (emit_expr(d.body, arities) + ";\u0001")))))))))))(generic_suffix(d.type_val))))(get_return_type(d.type_val, ((long)d.@params.Count))));

    public static CodexType get_return_type(CodexType ty, long n)
    {
        while (true)
        {
            if ((n == 0))
            {
            return strip_forall(ty);
            }
            else
            {
            var _tco_s = strip_forall(ty);
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
            return ty;
            }
            }
        }
    }

    public static CodexType strip_forall(CodexType ty)
    {
        while (true)
        {
            var _tco_s = ty;
            if (_tco_s is ForAllTy _tco_m0)
            {
                var id = _tco_m0.Field0;
                var body = _tco_m0.Field1;
            var _tco_0 = body;
            ty = _tco_0;
            continue;
            }
            {
            return ty;
            }
        }
    }

    public static string emit_def_params(List<IRParam> @params, long i) => ((i == ((long)@params.Count)) ? "" : ((Func<IRParam, string>)((p) => (cs_type(p.type_val) + (" " + (sanitize(p.name) + (((i < (((long)@params.Count) - 1)) ? ", " : "") + emit_def_params(@params, (i + 1))))))))(@params[(int)i]));

    public static string emit_cce_runtime() => ("static class _Cce {\u0001" + ("    static readonly int[] _toUni = {\u0001" + ("        0, 10, 32,\u0001" + ("        48, 49, 50, 51, 52, 53, 54, 55, 56, 57,\u0001" + ("        101, 116, 97, 111, 105, 110, 115, 104, 114, 100,\u0001" + ("        108, 99, 117, 109, 119, 102, 103, 121, 112, 98,\u0001" + ("        118, 107, 106, 120, 113, 122,\u0001" + ("        69, 84, 65, 79, 73, 78, 83, 72, 82, 68,\u0001" + ("        76, 67, 85, 77, 87, 70, 71, 89, 80, 66,\u0001" + ("        86, 75, 74, 88, 81, 90,\u0001" + ("        46, 44, 33, 63, 58, 59, 39, 34, 45, 40, 41,\u0001" + ("        43, 61, 42, 60, 62,\u0001" + ("        47, 64, 35, 38, 95, 92, 124, 91, 93, 123, 125, 126, 96,\u0001" + ("        233, 232, 234, 235, 225, 224, 226, 228, 243, 242,\u0001" + ("        244, 246, 250, 249, 251, 252, 241, 231, 237,\u0001" + ("        1072, 1086, 1077, 1080, 1085, 1090, 1089, 1088,\u0001" + ("        1074, 1083, 1082, 1084, 1076, 1087, 1091\u0001" + ("    };\u0001" + ("    static readonly Dictionary<int, int> _fromUni = new();\u0001" + ("    static _Cce() { for (int i = 0; i < 128; i++) _fromUni[_toUni[i]] = i; }\u0001" + ("    public static string FromUnicode(string s) {\u0001" + ("        var cs = new char[s.Length];\u0001" + ("        for (int i = 0; i < s.Length; i++) {\u0001" + ("            int u = s[i];\u0001" + ("            cs[i] = _fromUni.TryGetValue(u, out int c) ? (char)c : (char)68;\u0001" + ("        }\u0001" + ("        return new string(cs);\u0001" + ("    }\u0001" + ("    public static string ToUnicode(string s) {\u0001" + ("        var cs = new char[s.Length];\u0001" + ("        for (int i = 0; i < s.Length; i++) {\u0001" + ("            int b = s[i];\u0001" + ("            cs[i] = (b >= 0 && b < 128) ? (char)_toUni[b] : '\\uFFFD';\u0001" + ("        }\u0001" + ("        return new string(cs);\u0001" + ("    }\u0001" + ("    public static long UniToCce(long u) {\u0001" + ("        return _fromUni.TryGetValue((int)u, out int c) ? c : 68;\u0001" + ("    }\u0001" + ("    public static long CceToUni(long b) {\u0001" + ("        return (b >= 0 && b < 128) ? _toUni[(int)b] : 65533;\u0001" + ("    }\u0001" + "}\u0001\u0001"))))))))))))))))))))))))))))))))))))))))));

    public static string emit_full_module(IRModule m, List<ATypeDef> type_defs) => ((Func<List<ArityEntry>, string>)((arities) => ("using System;\u0001using System.Collections.Generic;\u0001using System.IO;\u0001using System.Linq;\u0001using System.Threading.Tasks;\u0001\u0001" + ("Codex_" + (sanitize(m.name.value) + (".main();\u0001\u0001" + (emit_cce_runtime() + (emit_type_defs(type_defs, 0) + (emit_class_header(m.name.value) + (emit_defs(m.defs, 0, arities) + "}\u0001"))))))))))(build_arity_map(m.defs, 0));

    public static string emit_module(IRModule m) => ((Func<List<ArityEntry>, string>)((arities) => ("using System;\u0001using System.Collections.Generic;\u0001using System.IO;\u0001using System.Linq;\u0001using System.Threading.Tasks;\u0001\u0001" + ("Codex_" + (sanitize(m.name.value) + (".main();\u0001\u0001" + (emit_cce_runtime() + (emit_class_header(m.name.value) + (emit_defs(m.defs, 0, arities) + "}\u0001")))))))))(build_arity_map(m.defs, 0));

    public static string emit_class_header(string name) => ("public static class Codex_" + (sanitize(name) + "\u0001{\u0001"));

    public static string emit_defs(List<IRDef> defs, long i, List<ArityEntry> arities) => ((i == ((long)defs.Count)) ? "" : (emit_def(defs[(int)i], arities) + ("\u0001" + emit_defs(defs, (i + 1), arities))));

    public static bool is_cs_keyword(string n) => ((n == "class") ? true : ((n == "static") ? true : ((n == "void") ? true : ((n == "return") ? true : ((n == "if") ? true : ((n == "else") ? true : ((n == "for") ? true : ((n == "while") ? true : ((n == "do") ? true : ((n == "switch") ? true : ((n == "case") ? true : ((n == "break") ? true : ((n == "continue") ? true : ((n == "new") ? true : ((n == "this") ? true : ((n == "base") ? true : ((n == "null") ? true : ((n == "true") ? true : ((n == "false") ? true : ((n == "int") ? true : ((n == "long") ? true : ((n == "string") ? true : ((n == "bool") ? true : ((n == "double") ? true : ((n == "decimal") ? true : ((n == "object") ? true : ((n == "in") ? true : ((n == "is") ? true : ((n == "as") ? true : ((n == "typeof") ? true : ((n == "default") ? true : ((n == "throw") ? true : ((n == "try") ? true : ((n == "catch") ? true : ((n == "finally") ? true : ((n == "using") ? true : ((n == "namespace") ? true : ((n == "public") ? true : ((n == "private") ? true : ((n == "protected") ? true : ((n == "internal") ? true : ((n == "abstract") ? true : ((n == "sealed") ? true : ((n == "override") ? true : ((n == "virtual") ? true : ((n == "event") ? true : ((n == "delegate") ? true : ((n == "out") ? true : ((n == "ref") ? true : ((n == "params") ? true : false))))))))))))))))))))))))))))))))))))))))))))))))));

    public static string sanitize(string name) => ((Func<string, string>)((s) => (is_cs_keyword(s) ? ("@" + s) : (is_cs_member_name(s) ? (s + "_") : s))))(name.Replace("-", "_"));

    public static bool is_cs_member_name(string n) => ((n == "Equals") ? true : ((n == "GetHashCode") ? true : ((n == "ToString") ? true : ((n == "GetType") ? true : ((n == "MemberwiseClone") ? true : false)))));

    public static string cs_type(CodexType ty)
    {
        while (true)
        {
            var _tco_s = ty;
            if (_tco_s is IntegerTy _tco_m0)
            {
            return "long";
            }
            else if (_tco_s is NumberTy _tco_m1)
            {
            return "decimal";
            }
            else if (_tco_s is TextTy _tco_m2)
            {
            return "string";
            }
            else if (_tco_s is BooleanTy _tco_m3)
            {
            return "bool";
            }
            else if (_tco_s is CharTy _tco_m4)
            {
            return "long";
            }
            else if (_tco_s is VoidTy _tco_m5)
            {
            return "void";
            }
            else if (_tco_s is NothingTy _tco_m6)
            {
            return "object";
            }
            else if (_tco_s is ErrorTy _tco_m7)
            {
            return "object";
            }
            else if (_tco_s is FunTy _tco_m8)
            {
                var p = _tco_m8.Field0;
                var r = _tco_m8.Field1;
            return ("Func<" + (cs_type(p) + (", " + (cs_type(r) + ">"))));
            }
            else if (_tco_s is ListTy _tco_m9)
            {
                var elem = _tco_m9.Field0;
            return ("List<" + (cs_type(elem) + ">"));
            }
            else if (_tco_s is TypeVar _tco_m10)
            {
                var id = _tco_m10.Field0;
            return ("T" + _Cce.FromUnicode(id.ToString()));
            }
            else if (_tco_s is ForAllTy _tco_m11)
            {
                var id = _tco_m11.Field0;
                var body = _tco_m11.Field1;
            var _tco_0 = body;
            ty = _tco_0;
            continue;
            }
            else if (_tco_s is SumTy _tco_m12)
            {
                var name = _tco_m12.Field0;
                var ctors = _tco_m12.Field1;
            return sanitize(name.value);
            }
            else if (_tco_s is RecordTy _tco_m13)
            {
                var name = _tco_m13.Field0;
                var fields = _tco_m13.Field1;
            return sanitize(name.value);
            }
            else if (_tco_s is ConstructedTy _tco_m14)
            {
                var name = _tco_m14.Field0;
                var args = _tco_m14.Field1;
            if ((((long)args.Count) == 0))
            {
            return sanitize(name.value);
            }
            else
            {
            return (sanitize(name.value) + ("<" + (emit_cs_type_args(args, 0) + ">")));
            }
            }
            else if (_tco_s is EffectfulTy _tco_m15)
            {
                var effects = _tco_m15.Field0;
                var ret = _tco_m15.Field1;
            var _tco_0 = ret;
            ty = _tco_0;
            continue;
            }
        }
    }

    public static string emit_cs_type_args(List<CodexType> args, long i) => ((i == ((long)args.Count)) ? "" : ((Func<string, string>)((t) => ((i == (((long)args.Count) - 1)) ? t : (t + (", " + emit_cs_type_args(args, (i + 1)))))))(cs_type(args[(int)i])));

    public static List<ArityEntry> build_arity_map(List<IRDef> defs, long i) => ((i == ((long)defs.Count)) ? new List<ArityEntry>() : ((Func<IRDef, List<ArityEntry>>)((d) => Enumerable.Concat(new List<ArityEntry> { new ArityEntry(name: d.name, arity: ((long)d.@params.Count)) }, build_arity_map(defs, (i + 1))).ToList()))(defs[(int)i]));

    public static List<ArityEntry> build_arity_map_from_ast(List<ADef> defs, long i) => ((i == ((long)defs.Count)) ? new List<ArityEntry>() : ((Func<ADef, List<ArityEntry>>)((d) => Enumerable.Concat(new List<ArityEntry> { new ArityEntry(name: d.name.value, arity: ((long)d.@params.Count)) }, build_arity_map_from_ast(defs, (i + 1))).ToList()))(defs[(int)i]));

    public static long lookup_arity(List<ArityEntry> entries, string name) => lookup_arity_loop(entries, name, 0, ((long)entries.Count));

    public static long lookup_arity_loop(List<ArityEntry> entries, string name, long i, long len)
    {
        while (true)
        {
            if ((i == len))
            {
            return (0 - 1);
            }
            else
            {
            var e = entries[(int)i];
            if ((e.name == name))
            {
            return e.arity;
            }
            else
            {
            var _tco_0 = entries;
            var _tco_1 = name;
            var _tco_2 = (i + 1);
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
            var _tco_s = e;
            if (_tco_s is IrApply _tco_m0)
            {
                var f = _tco_m0.Field0;
                var a = _tco_m0.Field1;
                var ty = _tco_m0.Field2;
            var _tco_0 = f;
            var _tco_1 = Enumerable.Concat(new List<IRExpr> { a }, acc).ToList();
            e = _tco_0;
            acc = _tco_1;
            continue;
            }
            {
            return new ApplyChain(root: e, args: acc);
            }
        }
    }

    public static bool is_upper_letter(long c) => ((Func<long, bool>)((code) => ((code >= 39) && (code <= 64))))(c);

    public static string emit_apply_args(List<IRExpr> args, List<ArityEntry> arities, long i) => ((i == ((long)args.Count)) ? "" : ((i == (((long)args.Count) - 1)) ? emit_expr(args[(int)i], arities) : (emit_expr(args[(int)i], arities) + (", " + emit_apply_args(args, arities, (i + 1))))));

    public static string emit_partial_params(long i, long count) => ((i == count) ? "" : ((i == (count - 1)) ? ("_p" + (_Cce.FromUnicode(i.ToString()) + "_")) : ("_p" + (_Cce.FromUnicode(i.ToString()) + ("_" + (", " + emit_partial_params((i + 1), count)))))));

    public static string emit_partial_wrappers(long i, long count) => ((i == count) ? "" : ("(_p" + (_Cce.FromUnicode(i.ToString()) + ("_) => " + emit_partial_wrappers((i + 1), count)))));

    public static bool is_builtin_name(string n) => ((n == "show") ? true : ((n == "negate") ? true : ((n == "print-line") ? true : ((n == "text-length") ? true : ((n == "is-letter") ? true : ((n == "is-digit") ? true : ((n == "is-whitespace") ? true : ((n == "text-to-integer") ? true : ((n == "integer-to-text") ? true : ((n == "char-code") ? true : ((n == "char-code-at") ? true : ((n == "code-to-char") ? true : ((n == "char-to-text") ? true : ((n == "list-length") ? true : ((n == "char-at") ? true : ((n == "substring") ? true : ((n == "list-at") ? true : ((n == "list-insert-at") ? true : ((n == "list-snoc") ? true : ((n == "text-compare") ? true : ((n == "text-replace") ? true : ((n == "open-file") ? true : ((n == "read-all") ? true : ((n == "close-file") ? true : ((n == "read-line") ? true : ((n == "read-file") ? true : ((n == "write-file") ? true : ((n == "file-exists") ? true : ((n == "list-files") ? true : ((n == "text-concat-list") ? true : ((n == "text-split") ? true : ((n == "text-contains") ? true : ((n == "text-starts-with") ? true : ((n == "get-args") ? true : ((n == "get-env") ? true : ((n == "current-dir") ? true : ((n == "run-process") ? true : ((n == "fork") ? true : ((n == "await") ? true : ((n == "par") ? true : ((n == "race") ? true : false)))))))))))))))))))))))))))))))))))))))));

    public static string emit_builtin(string n, List<IRExpr> args, List<ArityEntry> arities) => ((n == "show") ? ("_Cce.FromUnicode(Convert.ToString(" + (emit_expr(args[(int)0], arities) + "))")) : ((n == "negate") ? ("(-" + (emit_expr(args[(int)0], arities) + ")")) : ((n == "print-line") ? ("((Func<object>)(() => { Console.WriteLine(_Cce.ToUnicode(" + (emit_expr(args[(int)0], arities) + ")); return null; }))()")) : ((n == "text-length") ? ("((long)" + (emit_expr(args[(int)0], arities) + ".Length)")) : ((n == "is-letter") ? ("(" + (emit_expr(args[(int)0], arities) + (" >= 13L && " + (emit_expr(args[(int)0], arities) + " <= 64L)")))) : ((n == "is-digit") ? ("(" + (emit_expr(args[(int)0], arities) + (" >= 3L && " + (emit_expr(args[(int)0], arities) + " <= 12L)")))) : ((n == "is-whitespace") ? ("(" + (emit_expr(args[(int)0], arities) + " <= 2L)")) : ((n == "text-to-integer") ? ("long.Parse(_Cce.ToUnicode(" + (emit_expr(args[(int)0], arities) + "))")) : ((n == "integer-to-text") ? ("_Cce.FromUnicode(" + (emit_expr(args[(int)0], arities) + ".ToString())")) : ((n == "char-code") ? emit_expr(args[(int)0], arities) : ((n == "char-code-at") ? ("((long)" + (emit_expr(args[(int)0], arities) + ("[(int)" + (emit_expr(args[(int)1], arities) + "])")))) : ((n == "code-to-char") ? emit_expr(args[(int)0], arities) : ((n == "char-to-text") ? ("((char)" + (emit_expr(args[(int)0], arities) + ").ToString()")) : ((n == "list-length") ? ("((long)" + (emit_expr(args[(int)0], arities) + ".Count)")) : ((n == "char-at") ? ("((long)" + (emit_expr(args[(int)0], arities) + ("[(int)" + (emit_expr(args[(int)1], arities) + "])")))) : ((n == "substring") ? (emit_expr(args[(int)0], arities) + (".Substring((int)" + (emit_expr(args[(int)1], arities) + (", (int)" + (emit_expr(args[(int)2], arities) + ")"))))) : ((n == "list-at") ? (emit_expr(args[(int)0], arities) + ("[(int)" + (emit_expr(args[(int)1], arities) + "]"))) : ((n == "list-insert-at") ? ((Func<CodexType, string>)((elem_ty) => ("((Func<List<" + (cs_type(elem_ty) + (">>)(() => { var _l = new List<" + (cs_type(elem_ty) + (">(" + (emit_expr(args[(int)0], arities) + ("); _l.Insert((int)" + (emit_expr(args[(int)1], arities) + (", " + (emit_expr(args[(int)2], arities) + "); return _l; }))()"))))))))))))(ir_expr_type(args[(int)0]) switch { ListTy(var et) => et, _ => new ErrorTy(), }) : ((n == "list-snoc") ? ((Func<CodexType, string>)((elem_ty) => ("((Func<List<" + (cs_type(elem_ty) + (">>)(() => { var _l = " + (emit_expr(args[(int)0], arities) + ("; _l.Add(" + (emit_expr(args[(int)1], arities) + "); return _l; }))()"))))))))(ir_expr_type(args[(int)0]) switch { ListTy(var et) => et, _ => new ErrorTy(), }) : ((n == "text-compare") ? ("(long)string.CompareOrdinal(" + (emit_expr(args[(int)0], arities) + (", " + (emit_expr(args[(int)1], arities) + ")")))) : ((n == "text-replace") ? (emit_expr(args[(int)0], arities) + (".Replace(" + (emit_expr(args[(int)1], arities) + (", " + (emit_expr(args[(int)2], arities) + ")"))))) : ((n == "open-file") ? ("File.OpenRead(" + (emit_expr(args[(int)0], arities) + ")")) : ((n == "read-all") ? ("new System.IO.StreamReader(" + (emit_expr(args[(int)0], arities) + ").ReadToEnd()")) : ((n == "close-file") ? (emit_expr(args[(int)0], arities) + ".Dispose()") : ((n == "read-line") ? "_Cce.FromUnicode(Console.ReadLine() ?? \"\")" : ((n == "read-file") ? ("_Cce.FromUnicode(File.ReadAllText(_Cce.ToUnicode(" + (emit_expr(args[(int)0], arities) + ")))")) : ((n == "write-file") ? ("File.WriteAllText(_Cce.ToUnicode(" + (emit_expr(args[(int)0], arities) + ("), _Cce.ToUnicode(" + (emit_expr(args[(int)1], arities) + "))")))) : ((n == "file-exists") ? ("File.Exists(_Cce.ToUnicode(" + (emit_expr(args[(int)0], arities) + "))")) : ((n == "list-files") ? ("Directory.GetFiles(_Cce.ToUnicode(" + (emit_expr(args[(int)0], arities) + ("), _Cce.ToUnicode(" + (emit_expr(args[(int)1], arities) + ")).Select(_Cce.FromUnicode).ToList()")))) : ((n == "text-concat-list") ? ("string.Concat(" + (emit_expr(args[(int)0], arities) + ")")) : ((n == "text-split") ? ("new List<string>(" + (emit_expr(args[(int)0], arities) + (".Split(" + (emit_expr(args[(int)1], arities) + "))")))) : ((n == "text-contains") ? (emit_expr(args[(int)0], arities) + (".Contains(" + (emit_expr(args[(int)1], arities) + ")"))) : ((n == "text-starts-with") ? (emit_expr(args[(int)0], arities) + (".StartsWith(" + (emit_expr(args[(int)1], arities) + ")"))) : ((n == "get-args") ? "Environment.GetCommandLineArgs().Select(_Cce.FromUnicode).ToList()" : ((n == "get-env") ? ("_Cce.FromUnicode(Environment.GetEnvironmentVariable(_Cce.ToUnicode(" + (emit_expr(args[(int)0], arities) + ")) ?? \"\")")) : ((n == "run-process") ? ("_Cce.FromUnicode(((Func<string>)(() => { var _psi = new System.Diagnostics.ProcessStartInfo(_Cce.ToUnicode(" + (emit_expr(args[(int)0], arities) + ("), _Cce.ToUnicode(" + (emit_expr(args[(int)1], arities) + ")) { RedirectStandardOutput = true, UseShellExecute = false }; var _p = System.Diagnostics.Process.Start(_psi)!; var _o = _p.StandardOutput.ReadToEnd(); _p.WaitForExit(); return _o; }))()")))) : ((n == "current-dir") ? "_Cce.FromUnicode(Directory.GetCurrentDirectory())" : ((n == "fork") ? ("Task.Run(() => (" + (emit_expr(args[(int)0], arities) + ")(null))")) : ((n == "await") ? ("(" + (emit_expr(args[(int)0], arities) + ").Result")) : ((n == "par") ? ("Task.WhenAll(" + (emit_expr(args[(int)1], arities) + (".Select(_x_ => Task.Run(() => (" + (emit_expr(args[(int)0], arities) + ")(_x_)))).Result.ToList()")))) : ((n == "race") ? ("Task.WhenAny(" + (emit_expr(args[(int)0], arities) + ".Select(_t_ => Task.Run(() => _t_(null)))).Result.Result")) : "")))))))))))))))))))))))))))))))))))))))));

    public static string emit_apply(IRExpr e, List<ArityEntry> arities) => ((Func<ApplyChain, string>)((chain) => ((Func<IRExpr, string>)((root) => ((Func<List<IRExpr>, string>)((args) => root switch { IrName(var n, var ty) => (is_builtin_name(n) ? emit_builtin(n, args, arities) : (((((long)n.Length) > 0) && is_upper_letter(((long)n[(int)0]))) ? ((Func<CodexType, string>)((result_ty) => ((Func<string, string>)((ctor_type_args) => ("new " + (sanitize(n) + (ctor_type_args + ("(" + (emit_apply_args(args, arities, 0) + ")")))))))(extract_ctor_type_args(result_ty))))(ir_expr_type(e)) : ((Func<long, string>)((ar) => (((ar > 1) && (((long)args.Count) == ar)) ? (sanitize(n) + ("(" + (emit_apply_args(args, arities, 0) + ")"))) : (((ar > 1) && (((long)args.Count) < ar)) ? ((Func<long, string>)((remaining) => (emit_partial_wrappers(0, remaining) + (sanitize(n) + ("(" + (emit_apply_args(args, arities, 0) + (", " + (emit_partial_params(0, remaining) + ")"))))))))((ar - ((long)args.Count))) : emit_expr_curried(e, arities)))))(lookup_arity(arities, n)))), _ => emit_expr_curried(e, arities), }))(chain.args)))(chain.root)))(collect_apply_chain(e, new List<IRExpr>()));

    public static string emit_expr_curried(IRExpr e, List<ArityEntry> arities) => e switch { IrApply(var f, var a, var ty) => (emit_expr(f, arities) + ("(" + (emit_expr(a, arities) + ")"))), _ => emit_expr(e, arities), };

    public static string emit_expr(IRExpr e, List<ArityEntry> arities) => e switch { IrIntLit(var n) => _Cce.FromUnicode(n.ToString()), IrNumLit(var n) => _Cce.FromUnicode(n.ToString()), IrTextLit(var s) => ("\"" + (escape_text(s) + "\"")), IrBoolLit(var b) => (b ? "true" : "false"), IrCharLit(var n) => _Cce.FromUnicode(n.ToString()), IrName(var n, var ty) => ((n == "read-line") ? "_Cce.FromUnicode(Console.ReadLine() ?? \"\")" : ((n == "get-args") ? "Environment.GetCommandLineArgs().Select(_Cce.FromUnicode).ToList()" : ((n == "current-dir") ? "_Cce.FromUnicode(Directory.GetCurrentDirectory())" : (((((long)n.Length) > 0) && is_upper_letter(((long)n[(int)0]))) ? ("new " + (sanitize(n) + "()")) : ((lookup_arity(arities, n) == 0) ? (sanitize(n) + "()") : ((Func<long, string>)((ar) => ((ar >= 2) ? (emit_partial_wrappers(0, ar) + (sanitize(n) + ("(" + (emit_partial_params(0, ar) + ")")))) : sanitize(n))))(lookup_arity(arities, n))))))), IrBinary(var op, var l, var r, var ty) => emit_binary(op, l, r, ty, arities), IrNegate(var operand) => ("(-" + (emit_expr(operand, arities) + ")")), IrIf(var c, var t, var el, var ty) => ("(" + (emit_expr(c, arities) + (" ? " + (emit_expr(t, arities) + (" : " + (emit_expr(el, arities) + ")")))))), IrLet(var name, var ty, var val, var body) => emit_let(name, ty, val, body, arities), IrApply(var f, var a, var ty) => emit_apply(e, arities), IrLambda(var @params, var body, var ty) => emit_lambda(@params, body, arities), IrList(var elems, var ty) => emit_list(elems, ty, arities), IrMatch(var scrut, var branches, var ty) => emit_match(scrut, branches, ty, arities), IrDo(var stmts, var ty) => emit_do(stmts, ty, arities), IrHandle(var eff, var body, var clauses, var ty) => emit_handle(eff, body, clauses, ty, arities), IrRecord(var name, var fields, var ty) => emit_record(name, fields, arities), IrFieldAccess(var rec, var field, var ty) => (emit_expr(rec, arities) + ("." + sanitize(field))), IrFork(var body, var ty) => ("Task.Run(() => (" + (emit_expr(body, arities) + ")(null))")), IrAwait(var task, var ty) => ("(" + (emit_expr(task, arities) + ").Result")), IrError(var msg, var ty) => ("/* error: " + (msg + " */ default")), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static string hex_digit(long n) => ((n == 0) ? "0" : ((n == 1) ? "1" : ((n == 2) ? "2" : ((n == 3) ? "3" : ((n == 4) ? "4" : ((n == 5) ? "5" : ((n == 6) ? "6" : ((n == 7) ? "7" : ((n == 8) ? "8" : ((n == 9) ? "9" : ((n == 10) ? "A" : ((n == 11) ? "B" : ((n == 12) ? "C" : ((n == 13) ? "D" : ((n == 14) ? "E" : ((n == 15) ? "F" : "?"))))))))))))))));

    public static string hex4(long n) => ("00" + (hex_digit((n / 16)) + hex_digit((n - ((n / 16) * 16)))));

    public static string escape_cce_char(long c) => ((c == 86) ? "\\\\" : ((c == 72) ? "\\\"" : ((c >= 2) ? ((c < 127) ? ((char)c).ToString() : ("\\u" + hex4(c))) : ("\\u" + hex4(c)))));

    public static List<string> escape_text_loop(string s, long i, long len, List<string> acc)
    {
        while (true)
        {
            if ((i == len))
            {
            return acc;
            }
            else
            {
            var _tco_0 = s;
            var _tco_1 = (i + 1);
            var _tco_2 = len;
            var _tco_3 = ((Func<List<string>>)(() => { var _l = acc; _l.Add(escape_cce_char(((long)s[(int)i]))); return _l; }))();
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

    public static string emit_binary(IRBinaryOp op, IRExpr l, IRExpr r, CodexType ty, List<ArityEntry> arities) => op switch { IrAppendList { } => ("Enumerable.Concat(" + (emit_expr(l, arities) + (", " + (emit_expr(r, arities) + ").ToList()")))), IrConsList { } => ("new List<" + (cs_type(ir_expr_type(l)) + ("> { " + (emit_expr(l, arities) + (" }.Concat(" + (emit_expr(r, arities) + ").ToList()")))))), _ => ("(" + (emit_expr(l, arities) + (" " + (emit_bin_op(op) + (" " + (emit_expr(r, arities) + ")")))))), };

    public static string emit_let(string name, CodexType ty, IRExpr val, IRExpr body, List<ArityEntry> arities) => ("((Func<" + (cs_type(ty) + (", " + (cs_type(ir_expr_type(body)) + (">)((" + (sanitize(name) + (") => " + (emit_expr(body, arities) + ("))(" + (emit_expr(val, arities) + ")"))))))))));

    public static string emit_lambda(List<IRParam> @params, IRExpr body, List<ArityEntry> arities) => ((((long)@params.Count) == 0) ? ("(() => " + (emit_expr(body, arities) + ")")) : ((((long)@params.Count) == 1) ? ((Func<IRParam, string>)((p) => ("((" + (cs_type(p.type_val) + (" " + (sanitize(p.name) + (") => " + (emit_expr(body, arities) + ")"))))))))(@params[(int)0]) : ("(() => " + (emit_expr(body, arities) + ")"))));

    public static string emit_list(List<IRExpr> elems, CodexType ty, List<ArityEntry> arities) => ((((long)elems.Count) == 0) ? ("new List<" + (cs_type(ty) + ">()")) : ("new List<" + (cs_type(ty) + ("> { " + (emit_list_elems(elems, 0, arities) + " }")))));

    public static string emit_list_elems(List<IRExpr> elems, long i, List<ArityEntry> arities) => ((i == ((long)elems.Count)) ? "" : ((i == (((long)elems.Count) - 1)) ? emit_expr(elems[(int)i], arities) : (emit_expr(elems[(int)i], arities) + (", " + emit_list_elems(elems, (i + 1), arities)))));

    public static string emit_match(IRExpr scrut, List<IRBranch> branches, CodexType ty, List<ArityEntry> arities) => ((Func<string, string>)((arms) => ((Func<bool, string>)((needs_wild) => (emit_expr(scrut, arities) + (" switch { " + (arms + ((needs_wild ? "_ => throw new InvalidOperationException(\"Non-exhaustive match\"), " : "") + "}"))))))((has_any_catch_all(branches, 0) ? false : true))))(emit_match_arms(branches, 0, arities));

    public static string emit_match_arms(List<IRBranch> branches, long i, List<ArityEntry> arities) => ((i == ((long)branches.Count)) ? "" : ((Func<IRBranch, string>)((arm) => ((Func<string, string>)((this_arm) => (is_catch_all(arm.pattern) ? this_arm : (this_arm + emit_match_arms(branches, (i + 1), arities)))))((emit_pattern(arm.pattern) + (" => " + (emit_expr(arm.body, arities) + ", "))))))(branches[(int)i]));

    public static bool is_catch_all(IRPat p) => p switch { IrWildPat { } => true, IrVarPat(var name, var ty) => true, _ => false, };

    public static bool has_any_catch_all(List<IRBranch> branches, long i)
    {
        while (true)
        {
            if ((i == ((long)branches.Count)))
            {
            return false;
            }
            else
            {
            var b = branches[(int)i];
            if (is_catch_all(b.pattern))
            {
            return true;
            }
            else
            {
            var _tco_0 = branches;
            var _tco_1 = (i + 1);
            branches = _tco_0;
            i = _tco_1;
            continue;
            }
            }
        }
    }

    public static string emit_pattern(IRPat p) => p switch { IrVarPat(var name, var ty) => (cs_type(ty) + (" " + sanitize(name))), IrLitPat(var text, var ty) => text, IrCtorPat(var name, var subs, var ty) => ((((long)subs.Count) == 0) ? (sanitize(name) + " { }") : (sanitize(name) + ("(" + (emit_sub_patterns(subs, 0) + ")")))), IrWildPat { } => "_", _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static string emit_sub_patterns(List<IRPat> subs, long i) => ((i == ((long)subs.Count)) ? "" : ((Func<IRPat, string>)((sub) => (emit_sub_pattern(sub) + (((i < (((long)subs.Count) - 1)) ? ", " : "") + emit_sub_patterns(subs, (i + 1))))))(subs[(int)i]));

    public static string emit_sub_pattern(IRPat p) => p switch { IrVarPat(var name, var ty) => ("var " + sanitize(name)), IrCtorPat(var name, var subs, var ty) => emit_pattern(p), IrWildPat { } => "_", IrLitPat(var text, var ty) => text, _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static string emit_do(List<IRDoStmt> stmts, CodexType ty, List<ArityEntry> arities) => ((Func<string, string>)((ret_type) => ((Func<long, string>)((len) => ty switch { VoidTy { } => ("((Func<object>)(() => { " + (emit_do_stmts(stmts, 0, len, false, arities) + " return null; }))()")), NothingTy { } => ("((Func<object>)(() => { " + (emit_do_stmts(stmts, 0, len, false, arities) + " return null; }))()")), ErrorTy { } => ("((Func<object>)(() => { " + (emit_do_stmts(stmts, 0, len, false, arities) + " return null; }))()")), _ => ((len == 0) ? ("((Func<" + (ret_type + ">)(() => { return null; }))()")) : ("((Func<" + (ret_type + (">)(() => { " + (emit_do_stmts(stmts, 0, len, true, arities) + " }))()"))))), }))(((long)stmts.Count))))(cs_type(ty));

    public static string emit_do_stmts(List<IRDoStmt> stmts, long i, long len, bool needs_return, List<ArityEntry> arities) => ((i == len) ? "" : ((Func<IRDoStmt, string>)((s) => ((Func<bool, string>)((is_last) => ((Func<bool, string>)((use_return) => (emit_do_stmt(s, use_return, arities) + (" " + emit_do_stmts(stmts, (i + 1), len, needs_return, arities)))))((is_last ? needs_return : false))))((i == (len - 1)))))(stmts[(int)i]));

    public static string emit_do_stmt(IRDoStmt s, bool use_return, List<ArityEntry> arities) => s switch { IrDoBind(var name, var ty, var val) => ("var " + (sanitize(name) + (" = " + (emit_expr(val, arities) + ";")))), IrDoExec(var e) => (use_return ? ("return " + (emit_expr(e, arities) + ";")) : (emit_expr(e, arities) + ";")), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static string emit_record(string name, List<IRFieldVal> fields, List<ArityEntry> arities) => ("new " + (sanitize(name) + ("(" + (emit_record_fields(fields, 0, arities) + ")"))));

    public static string emit_record_fields(List<IRFieldVal> fields, long i, List<ArityEntry> arities) => ((i == ((long)fields.Count)) ? "" : ((Func<IRFieldVal, string>)((f) => (sanitize(f.name) + (": " + (emit_expr(f.value, arities) + (((i < (((long)fields.Count) - 1)) ? ", " : "") + emit_record_fields(fields, (i + 1), arities)))))))(fields[(int)i]));

    public static string emit_handle(string eff, IRExpr body, List<IRHandleClause> clauses, CodexType ty, List<ArityEntry> arities) => ((Func<string, string>)((ret_type) => ("((Func<" + (ret_type + (">)(() => { " + (emit_handle_clauses(clauses, ret_type, arities) + ("return " + (emit_expr(body, arities) + "; }))()"))))))))(cs_type(ty));

    public static string emit_handle_clauses(List<IRHandleClause> clauses, string ret_type, List<ArityEntry> arities) => emit_handle_clauses_loop(clauses, 0, ret_type, arities);

    public static string emit_handle_clauses_loop(List<IRHandleClause> clauses, long i, string ret_type, List<ArityEntry> arities) => ((i == ((long)clauses.Count)) ? "" : ((Func<IRHandleClause, string>)((c) => ("Func<Func<" + (ret_type + (", " + (ret_type + (">, " + (ret_type + ("> _handle_" + (sanitize(c.op_name) + ("_ = (" + (sanitize(c.resume_name) + (") => { return " + (emit_expr(c.body, arities) + ("; }; " + emit_handle_clauses_loop(clauses, (i + 1), ret_type, arities))))))))))))))))(clauses[(int)i]));

    public static string codex_emit_type_defs(List<ATypeDef> tds, long i) => ((i == ((long)tds.Count)) ? "" : (codex_emit_type_def(tds[(int)i]) + ("\u0001" + codex_emit_type_defs(tds, (i + 1)))));

    public static string codex_emit_type_def(ATypeDef td) => td switch { ARecordTypeDef(var name, var tparams, var fields) => (name.value + (" = record {" + (codex_emit_record_field_defs(fields, 0) + "\u0001}\u0001"))), AVariantTypeDef(var name, var tparams, var ctors) => (name.value + (" =\u0001" + codex_emit_variant_ctors(ctors, 0))), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static string codex_emit_record_field_defs(List<ARecordFieldDef> fields, long i) => ((i == ((long)fields.Count)) ? "" : ((Func<ARecordFieldDef, string>)((f) => ((Func<string, string>)((comma) => (comma + ("\u0001  " + (f.name.value + (" : " + (codex_emit_type_expr(f.type_expr) + codex_emit_record_field_defs(fields, (i + 1)))))))))(((i > 0) ? "," : ""))))(fields[(int)i]));

    public static string codex_emit_variant_ctors(List<AVariantCtorDef> ctors, long i) => ((i == ((long)ctors.Count)) ? "" : ((Func<AVariantCtorDef, string>)((c) => ("  | " + (c.name.value + (codex_emit_ctor_fields(c.fields, 0) + ("\u0001" + codex_emit_variant_ctors(ctors, (i + 1))))))))(ctors[(int)i]));

    public static string codex_emit_ctor_fields(List<ATypeExpr> fields, long i) => ((i == ((long)fields.Count)) ? "" : (" (" + (codex_emit_type_expr(fields[(int)i]) + (")" + codex_emit_ctor_fields(fields, (i + 1))))));

    public static string codex_emit_type_expr(ATypeExpr te) => te switch { ANamedType(var name) => name.value, AFunType(var p, var r) => ("(" + (codex_emit_type_expr(p) + (" -> " + (codex_emit_type_expr(r) + ")")))), AAppType(var @base, var args) => (codex_emit_type_expr(@base) + (" " + codex_emit_type_expr_args(args, 0))), AEffectType(var effs, var ret) => ("[" + (codex_emit_effect_names(effs, 0) + ("] " + codex_emit_type_expr(ret)))), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static string codex_emit_type_expr_args(List<ATypeExpr> args, long i) => ((i == ((long)args.Count)) ? "" : ((Func<string, string>)((sep) => (sep + (codex_emit_type_expr_wrapped(args[(int)i]) + codex_emit_type_expr_args(args, (i + 1))))))(((i > 0) ? " " : "")));

    public static string codex_emit_type_expr_wrapped(ATypeExpr te) => te switch { AFunType(var p, var r) => ("(" + (codex_emit_type_expr(te) + ")")), AAppType(var @base, var args) => ("(" + (codex_emit_type_expr(te) + ")")), _ => codex_emit_type_expr(te), };

    public static string codex_emit_effect_names(List<Name> effs, long i) => ((i == ((long)effs.Count)) ? "" : ((Func<string, string>)((sep) => (sep + (effs[(int)i].value + codex_emit_effect_names(effs, (i + 1))))))(((i > 0) ? ", " : "")));

    public static string codex_emit_codex_type(CodexType ty)
    {
        while (true)
        {
            var _tco_s = ty;
            if (_tco_s is IntegerTy _tco_m0)
            {
            return "Integer";
            }
            else if (_tco_s is NumberTy _tco_m1)
            {
            return "Number";
            }
            else if (_tco_s is TextTy _tco_m2)
            {
            return "Text";
            }
            else if (_tco_s is BooleanTy _tco_m3)
            {
            return "Boolean";
            }
            else if (_tco_s is CharTy _tco_m4)
            {
            return "Char";
            }
            else if (_tco_s is VoidTy _tco_m5)
            {
            return "Nothing";
            }
            else if (_tco_s is NothingTy _tco_m6)
            {
            return "Nothing";
            }
            else if (_tco_s is ErrorTy _tco_m7)
            {
            return "Unknown";
            }
            else if (_tco_s is FunTy _tco_m8)
            {
                var p = _tco_m8.Field0;
                var r = _tco_m8.Field1;
            return (codex_wrap_fun_param(p) + (" -> " + codex_emit_codex_type(r)));
            }
            else if (_tco_s is ListTy _tco_m9)
            {
                var elem = _tco_m9.Field0;
            return ("List " + codex_wrap_complex(elem));
            }
            else if (_tco_s is TypeVar _tco_m10)
            {
                var id = _tco_m10.Field0;
            return ("a" + _Cce.FromUnicode(id.ToString()));
            }
            else if (_tco_s is ForAllTy _tco_m11)
            {
                var id = _tco_m11.Field0;
                var body = _tco_m11.Field1;
            var _tco_0 = body;
            ty = _tco_0;
            continue;
            }
            else if (_tco_s is SumTy _tco_m12)
            {
                var name = _tco_m12.Field0;
                var ctors = _tco_m12.Field1;
            return name.value;
            }
            else if (_tco_s is RecordTy _tco_m13)
            {
                var name = _tco_m13.Field0;
                var fields = _tco_m13.Field1;
            return name.value;
            }
            else if (_tco_s is ConstructedTy _tco_m14)
            {
                var name = _tco_m14.Field0;
                var args = _tco_m14.Field1;
            return name.value;
            }
            else if (_tco_s is EffectfulTy _tco_m15)
            {
                var effs = _tco_m15.Field0;
                var ret = _tco_m15.Field1;
            return ("[" + (codex_emit_codex_type_effect_names(effs, 0) + ("] " + codex_emit_codex_type(ret))));
            }
        }
    }

    public static string codex_wrap_fun_param(CodexType ty) => ty switch { FunTy(var p, var r) => ("(" + (codex_emit_codex_type(ty) + ")")), _ => codex_emit_codex_type(ty), };

    public static string codex_wrap_complex(CodexType ty) => ty switch { FunTy(var p, var r) => ("(" + (codex_emit_codex_type(ty) + ")")), ListTy(var elem) => ("(" + (codex_emit_codex_type(ty) + ")")), _ => codex_emit_codex_type(ty), };

    public static string codex_emit_codex_type_effect_names(List<Name> effs, long i) => ((i == ((long)effs.Count)) ? "" : ((Func<string, string>)((sep) => (sep + (effs[(int)i].value + codex_emit_codex_type_effect_names(effs, (i + 1))))))(((i > 0) ? ", " : "")));

    public static string codex_emit_def(IRDef d, List<string> ctor_names) => (codex_skip_def(d, ctor_names) ? "" : (d.name + (" : " + (codex_emit_codex_type(d.type_val) + ("\u0001" + (d.name + (codex_emit_def_params(d.@params, 0) + (" =\u0001  " + (codex_emit_expr(d.body, ctor_names, 1) + "\u0001")))))))));

    public static bool codex_skip_def(IRDef d, List<string> ctor_names) => (list_contains(ctor_names, d.name) ? true : ((((long)d.name.Length) == 0) ? true : ((Func<long, bool>)((first) => ((Func<long, bool>)((code_a) => ((Func<long, bool>)((code_z) => (((first < code_a) || (first > code_z)) ? true : (codex_is_error_body(d.body) ? true : false))))(((long)"z"[(int)0]))))(((long)"a"[(int)0]))))(((long)d.name[(int)0]))));

    public static bool codex_is_upper(long c) => ((Func<long, bool>)((code) => ((Func<long, bool>)((code_z) => ((c >= code) && (c <= code_z))))(((long)"Z"[(int)0]))))(((long)"A"[(int)0]));

    public static bool codex_is_error_body(IRExpr e) => e switch { IrError(var msg, var ty) => true, _ => false, };

    public static bool codex_is_lower_start(string s) => ((((long)s.Length) == 0) ? false : ((Func<long, bool>)((c) => ((Func<long, bool>)((code_a) => ((Func<long, bool>)((code_z) => ((c >= code_a) && (c <= code_z))))(((long)"z"[(int)0]))))(((long)"a"[(int)0]))))(((long)s[(int)0])));

    public static string codex_emit_def_params(List<IRParam> @params, long i) => ((i == ((long)@params.Count)) ? "" : ((Func<IRParam, string>)((p) => (" (" + (p.name + (")" + codex_emit_def_params(@params, (i + 1)))))))(@params[(int)i]));

    public static string codex_emit_expr(IRExpr e, List<string> ctors, long indent) => e switch { IrIntLit(var n) => _Cce.FromUnicode(n.ToString()), IrNumLit(var n) => _Cce.FromUnicode(n.ToString()), IrTextLit(var s) => ("\"" + (codex_escape_text(s) + "\"")), IrBoolLit(var b) => (b ? "True" : "False"), IrCharLit(var n) => ("'" + (codex_escape_char(n) + "'")), IrName(var n, var ty) => n, IrBinary(var op, var l, var r, var ty) => codex_emit_binary(op, l, r, ctors, indent), IrNegate(var operand) => ("0 - " + codex_emit_expr(operand, ctors, indent)), IrIf(var c, var t, var el, var ty) => codex_emit_if(c, t, el, ctors, indent), IrLet(var name, var ty, var val, var body) => codex_emit_let(name, val, body, ctors, indent), IrApply(var f, var a, var ty) => codex_emit_apply(e, ctors, indent), IrLambda(var @params, var body, var ty) => codex_emit_lambda(@params, body, ctors, indent), IrList(var elems, var ty) => codex_emit_list(elems, ctors, indent), IrMatch(var scrut, var branches, var ty) => codex_emit_match(scrut, branches, ctors, indent), IrDo(var stmts, var ty) => codex_emit_do(stmts, ctors, indent), IrRecord(var name, var fields, var ty) => codex_emit_record(name, fields, ctors, indent), IrFieldAccess(var rec, var field, var ty) => codex_emit_field_access(rec, field, ctors, indent), IrFork(var body, var ty) => ("fork (" + (codex_emit_expr(body, ctors, indent) + ")")), IrAwait(var task, var ty) => ("await (" + (codex_emit_expr(task, ctors, indent) + ")")), IrHandle(var eff, var body, var clauses, var ty) => codex_emit_handle(eff, body, clauses, ctors, indent), IrError(var msg, var ty) => "0", _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static string codex_emit_binary(IRBinaryOp op, IRExpr l, IRExpr r, List<string> ctors, long indent) => (codex_emit_expr(l, ctors, indent) + (" " + (codex_bin_op_text(op) + (" " + codex_emit_expr(r, ctors, indent)))));

    public static string codex_bin_op_text(IRBinaryOp op) => op switch { IrAddInt { } => "+", IrSubInt { } => "-", IrMulInt { } => "*", IrDivInt { } => "/", IrPowInt { } => "\u00E0\u0081\u009E", IrAddNum { } => "+", IrSubNum { } => "-", IrMulNum { } => "*", IrDivNum { } => "/", IrEq { } => "==", IrNotEq { } => "/=", IrLt { } => "<", IrGt { } => ">", IrLtEq { } => "<=", IrGtEq { } => ">=", IrAnd { } => "&", IrOr { } => "|", IrAppendText { } => "++", IrAppendList { } => "++", IrConsList { } => "::", _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static string codex_emit_if(IRExpr c, IRExpr t, IRExpr el, List<string> ctors, long indent) => ((codex_is_simple(t) && codex_is_simple(el)) ? ("if " + (codex_emit_expr(c, ctors, indent) + (" then " + (codex_emit_expr(t, ctors, indent) + (" else " + codex_emit_expr(el, ctors, indent)))))) : ("if " + (codex_emit_expr(c, ctors, indent) + ("\u0001" + (codex_indent((indent + 1)) + ("then " + (codex_emit_expr(t, ctors, (indent + 1)) + ("\u0001" + (codex_indent((indent + 1)) + ("else " + codex_emit_expr(el, ctors, (indent + 1))))))))))));

    public static string codex_emit_let(string name, IRExpr val, IRExpr body, List<string> ctors, long indent) => ("let " + (name + (" = " + (codex_emit_expr(val, ctors, (indent + 1)) + ("\u0001" + (codex_indent(indent) + ("in " + codex_emit_expr(body, ctors, indent))))))));

    public static string codex_emit_apply(IRExpr e, List<string> ctors, long indent) => ((Func<ApplyChain, string>)((chain) => ((Func<IRExpr, string>)((func) => ((Func<List<IRExpr>, string>)((args) => (codex_emit_expr(func, ctors, indent) + codex_emit_apply_args(args, ctors, indent, 0, ((long)args.Count), codex_is_ctor_name(func, ctors)))))(chain.args)))(chain.root)))(collect_apply_chain(e, new List<IRExpr>()));

    public static string codex_emit_apply_args(List<IRExpr> args, List<string> ctors, long indent, long i, long len, bool is_ctor) => ((i == len) ? "" : ((Func<IRExpr, string>)((arg) => (" " + (codex_wrap_arg(arg, ctors, indent, is_ctor) + codex_emit_apply_args(args, ctors, indent, (i + 1), len, is_ctor)))))(args[(int)i]));

    public static string codex_wrap_arg(IRExpr e, List<string> ctors, long indent, bool is_ctor) => (codex_needs_parens(e, is_ctor) ? ("(" + (codex_emit_expr(e, ctors, indent) + ")")) : codex_emit_expr(e, ctors, indent));

    public static bool codex_is_ctor_name(IRExpr e, List<string> ctors) => e switch { IrName(var n, var ty) => list_contains(ctors, n), _ => false, };

    public static bool codex_needs_parens(IRExpr e, bool is_ctor) => e switch { IrApply(var f, var a, var ty) => true, IrBinary(var op, var l, var r, var ty) => true, IrIf(var c, var t, var el, var ty) => true, IrLet(var name, var ty, var val, var body) => true, IrMatch(var scrut, var branches, var ty) => true, IrNegate(var operand) => true, IrLambda(var @params, var body, var ty) => true, _ => false, };

    public static string codex_emit_lambda(List<IRParam> @params, IRExpr body, List<string> ctors, long indent) => ("\\" + (codex_emit_lambda_params(@params, 0) + (" -> " + codex_emit_expr(body, ctors, indent))));

    public static string codex_emit_lambda_params(List<IRParam> @params, long i) => ((i == ((long)@params.Count)) ? "" : ((Func<IRParam, string>)((p) => ((Func<string, string>)((sep) => (sep + (p.name + codex_emit_lambda_params(@params, (i + 1))))))(((i > 0) ? " " : ""))))(@params[(int)i]));

    public static string codex_emit_list(List<IRExpr> elems, List<string> ctors, long indent) => ("[" + (codex_emit_list_elems(elems, ctors, indent, 0) + "]"));

    public static string codex_emit_list_elems(List<IRExpr> elems, List<string> ctors, long indent, long i) => ((i == ((long)elems.Count)) ? "" : ((Func<string, string>)((sep) => (sep + (codex_emit_expr(elems[(int)i], ctors, indent) + codex_emit_list_elems(elems, ctors, indent, (i + 1))))))(((i > 0) ? ", " : "")));

    public static string codex_emit_match(IRExpr scrut, List<IRBranch> branches, List<string> ctors, long indent) => ("when " + (codex_emit_expr(scrut, ctors, indent) + codex_emit_branches(branches, ctors, indent, 0)));

    public static string codex_emit_branches(List<IRBranch> branches, List<string> ctors, long indent, long i) => ((i == ((long)branches.Count)) ? "" : ((Func<IRBranch, string>)((b) => ("\u0001" + (codex_indent((indent + 1)) + ("if " + (codex_emit_pattern(b.pattern) + (" -> " + (codex_emit_expr(b.body, ctors, (indent + 1)) + codex_emit_branches(branches, ctors, indent, (i + 1))))))))))(branches[(int)i]));

    public static string codex_emit_pattern(IRPat p) => p switch { IrVarPat(var name, var ty) => name, IrLitPat(var text, var ty) => text, IrCtorPat(var name, var subs, var ty) => (name + codex_emit_sub_patterns(subs, 0)), IrWildPat { } => "_", _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static string codex_emit_sub_patterns(List<IRPat> subs, long i) => ((i == ((long)subs.Count)) ? "" : (" (" + (codex_emit_pattern(subs[(int)i]) + (")" + codex_emit_sub_patterns(subs, (i + 1))))));

    public static string codex_emit_do(List<IRDoStmt> stmts, List<string> ctors, long indent) => ("do" + codex_emit_do_stmts(stmts, ctors, indent, 0));

    public static string codex_emit_do_stmts(List<IRDoStmt> stmts, List<string> ctors, long indent, long i) => ((i == ((long)stmts.Count)) ? "" : ((Func<IRDoStmt, string>)((s) => ("\u0001" + (codex_indent((indent + 1)) + (codex_emit_do_stmt(s, ctors, (indent + 1)) + codex_emit_do_stmts(stmts, ctors, indent, (i + 1)))))))(stmts[(int)i]));

    public static string codex_emit_do_stmt(IRDoStmt s, List<string> ctors, long indent) => s switch { IrDoBind(var name, var ty, var val) => (name + (" <- " + codex_emit_expr(val, ctors, indent))), IrDoExec(var e) => codex_emit_expr(e, ctors, indent), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static string codex_emit_record(string name, List<IRFieldVal> fields, List<string> ctors, long indent) => (name + (" {" + (codex_emit_record_fields(fields, ctors, indent, 0) + " }")));

    public static string codex_emit_record_fields(List<IRFieldVal> fields, List<string> ctors, long indent, long i) => ((i == ((long)fields.Count)) ? "" : ((Func<IRFieldVal, string>)((f) => ((Func<string, string>)((sep) => (sep + (" " + (f.name + (" = " + (codex_emit_expr(f.value, ctors, indent) + codex_emit_record_fields(fields, ctors, indent, (i + 1)))))))))(((i > 0) ? "," : ""))))(fields[(int)i]));

    public static string codex_emit_field_access(IRExpr rec, string field, List<string> ctors, long indent) => rec switch { IrName(var n, var ty) => (n + ("." + field)), IrFieldAccess(var r, var f, var ty) => (codex_emit_field_access(r, f, ctors, indent) + ("." + field)), _ => ("(" + (codex_emit_expr(rec, ctors, indent) + (")." + field))), };

    public static string codex_emit_handle(string eff, IRExpr body, List<IRHandleClause> clauses, List<string> ctors, long indent) => ("handle " + (codex_emit_expr(body, ctors, indent) + (" with" + codex_emit_handle_clauses(clauses, ctors, indent, 0))));

    public static string codex_emit_handle_clauses(List<IRHandleClause> clauses, List<string> ctors, long indent, long i) => ((i == ((long)clauses.Count)) ? "" : ((Func<IRHandleClause, string>)((c) => ("\u0001" + (codex_indent((indent + 1)) + (c.op_name + (" (" + (c.resume_name + (") -> " + (codex_emit_expr(c.body, ctors, (indent + 1)) + codex_emit_handle_clauses(clauses, ctors, indent, (i + 1)))))))))))(clauses[(int)i]));

    public static List<string> codex_collect_ctor_names(List<ATypeDef> type_defs, long i) => ((i == ((long)type_defs.Count)) ? new List<string>() : ((Func<ATypeDef, List<string>>)((td) => ((Func<List<string>, List<string>>)((names) => Enumerable.Concat(names, codex_collect_ctor_names(type_defs, (i + 1))).ToList()))(td switch { AVariantTypeDef(var name, var tparams, var ctors) => codex_collect_variant_ctor_names(ctors, 0), ARecordTypeDef(var name, var tparams, var fields) => new List<string> { name.value }, _ => throw new InvalidOperationException("Non-exhaustive match"), })))(type_defs[(int)i]));

    public static List<string> codex_collect_variant_ctor_names(List<AVariantCtorDef> ctors, long i) => ((i == ((long)ctors.Count)) ? new List<string>() : ((Func<AVariantCtorDef, List<string>>)((c) => Enumerable.Concat(new List<string> { c.name.value }, codex_collect_variant_ctor_names(ctors, (i + 1))).ToList()))(ctors[(int)i]));

    public static bool codex_is_simple(IRExpr e) => e switch { IrIntLit(var n) => true, IrNumLit(var n) => true, IrTextLit(var s) => true, IrBoolLit(var b) => true, IrCharLit(var n) => true, IrName(var n, var ty) => true, IrFieldAccess(var r, var f, var ty) => true, _ => false, };

    public static string codex_indent(long n) => ((n == 0) ? "" : ("  " + codex_indent((n - 1))));

    public static string codex_escape_text(string s) => codex_escape_text_loop(s, 0, ((long)s.Length), "");

    public static string codex_escape_text_loop(string s, long i, long len, string acc)
    {
        while (true)
        {
            if ((i == len))
            {
            return acc;
            }
            else
            {
            var c = ((long)s[(int)i]);
            var _tco_0 = s;
            var _tco_1 = (i + 1);
            var _tco_2 = len;
            var _tco_3 = (acc + codex_escape_one_char(c));
            s = _tco_0;
            i = _tco_1;
            len = _tco_2;
            acc = _tco_3;
            continue;
            }
        }
    }

    public static string codex_escape_one_char(long c) => ((c == ((long)"\\"[(int)0])) ? "\\\\" : ((c == ((long)"\""[(int)0])) ? "\\\"" : ((c == ((long)"\u0001"[(int)0])) ? "\\n" : ((char)c).ToString())));

    public static string codex_escape_char(long c) => ((c == ((long)"\u0001"[(int)0])) ? "\\n" : ((c == ((long)"\\"[(int)0])) ? "\\\\" : ((c == ((long)"'"[(int)0])) ? "\\'" : ((char)c).ToString())));

    public static string codex_emit_full_module(IRModule m, List<ATypeDef> type_defs) => ((Func<List<string>, string>)((ctor_names) => (codex_emit_type_defs(type_defs, 0) + codex_emit_all_defs(m.defs, ctor_names, 0))))(codex_collect_ctor_names(type_defs, 0));

    public static string codex_emit_all_defs(List<IRDef> defs, List<string> ctor_names, long i) => ((i == ((long)defs.Count)) ? "" : (codex_emit_def(defs[(int)i], ctor_names) + ("\u0001" + codex_emit_all_defs(defs, ctor_names, (i + 1)))));

    public static string codex_emit_def_list(List<IRDef> defs, List<string> ctor_names, long i) => ((i == ((long)defs.Count)) ? "" : (codex_emit_def(defs[(int)i], ctor_names) + ("\u0001" + codex_emit_def_list(defs, ctor_names, (i + 1)))));

    public static List<IRDef> codex_filter_defs(List<IRDef> defs, List<string> ctor_names, long i, long len, List<IRDef> acc)
    {
        while (true)
        {
            if ((i == len))
            {
            return acc;
            }
            else
            {
            var d = defs[(int)i];
            if (codex_skip_def(d, ctor_names))
            {
            var _tco_0 = defs;
            var _tco_1 = ctor_names;
            var _tco_2 = (i + 1);
            var _tco_3 = len;
            var _tco_4 = acc;
            defs = _tco_0;
            ctor_names = _tco_1;
            i = _tco_2;
            len = _tco_3;
            acc = _tco_4;
            continue;
            }
            else
            {
            if (codex_has_def_named(acc, d.name, 0, ((long)acc.Count)))
            {
            if ((codex_def_score(d) > codex_def_score_named(acc, d.name, 0, ((long)acc.Count))))
            {
            var _tco_0 = defs;
            var _tco_1 = ctor_names;
            var _tco_2 = (i + 1);
            var _tco_3 = len;
            var _tco_4 = codex_replace_def(acc, d.name, d, 0, ((long)acc.Count));
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
            var _tco_2 = (i + 1);
            var _tco_3 = len;
            var _tco_4 = acc;
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
            var _tco_2 = (i + 1);
            var _tco_3 = len;
            var _tco_4 = ((Func<List<IRDef>>)(() => { var _l = acc; _l.Add(d); return _l; }))();
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
            if ((i == len))
            {
            return 0;
            }
            else
            {
            if ((defs[(int)i].name == name))
            {
            return codex_def_score(defs[(int)i]);
            }
            else
            {
            var _tco_0 = defs;
            var _tco_1 = name;
            var _tco_2 = (i + 1);
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

    public static long codex_body_depth(IRExpr e) => e switch { IrName(var n, var ty) => 1, IrIntLit(var n) => 1, IrTextLit(var s) => 1, IrBoolLit(var b) => 1, IrFieldAccess(var r, var f, var ty) => 2, IrApply(var f, var a, var ty) => (3 + codex_body_depth(f)), IrLet(var name, var ty, var val, var body) => (5 + codex_body_depth(body)), IrIf(var c, var t, var el, var ty) => (5 + codex_body_depth(t)), IrMatch(var s, var bs, var ty) => 10, IrLambda(var ps, var b, var ty) => 5, IrDo(var stmts, var ty) => 5, IrRecord(var n, var fs, var ty) => 3, _ => 1, };

    public static bool codex_has_def_named(List<IRDef> defs, string name, long i, long len)
    {
        while (true)
        {
            if ((i == len))
            {
            return false;
            }
            else
            {
            if ((defs[(int)i].name == name))
            {
            return true;
            }
            else
            {
            var _tco_0 = defs;
            var _tco_1 = name;
            var _tco_2 = (i + 1);
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
            if ((i == len))
            {
            return defs;
            }
            else
            {
            if ((defs[(int)i].name == name))
            {
            return codex_list_set(defs, i, new_def);
            }
            else
            {
            var _tco_0 = defs;
            var _tco_1 = name;
            var _tco_2 = new_def;
            var _tco_3 = (i + 1);
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
            if ((i == len))
            {
            return acc;
            }
            else
            {
            if ((i == idx))
            {
            var _tco_0 = xs;
            var _tco_1 = idx;
            var _tco_2 = val;
            var _tco_3 = (i + 1);
            var _tco_4 = len;
            var _tco_5 = ((Func<List<IRDef>>)(() => { var _l = acc; _l.Add(val); return _l; }))();
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
            var _tco_3 = (i + 1);
            var _tco_4 = len;
            var _tco_5 = ((Func<List<IRDef>>)(() => { var _l = acc; _l.Add(xs[(int)i]); return _l; }))();
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
            var _tco_s = e;
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
            return new TextTy();
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
            var _tco_0 = b;
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

    public static IRExpr lower_expr(AExpr e, CodexType ty, LowerCtx ctx) => e switch { ALitExpr(var text, var kind) => lower_literal(text, kind), ANameExpr(var name) => lower_name(name.value, ty, ctx), AApplyExpr(var f, var a) => lower_apply(f, a, ty, ctx), ABinaryExpr(var l, var op, var r) => ((Func<IRExpr, IRExpr>)((left_ir) => ((Func<CodexType, IRExpr>)((left_ty) => ((Func<IRExpr, IRExpr>)((right_ir) => new IrBinary(lower_bin_op(op, left_ty), left_ir, right_ir, binary_result_type(op, left_ty, ty))))(lower_expr(r, ty, ctx))))(ir_expr_type(left_ir))))(lower_expr(l, ty, ctx)), AUnaryExpr(var operand) => new IrNegate(lower_expr(operand, new IntegerTy(), ctx)), AIfExpr(var c, var t, var e2) => ((Func<IRExpr, IRExpr>)((then_ir) => ((Func<CodexType, IRExpr>)((then_ty) => ((Func<CodexType, IRExpr>)((result_ty) => ((Func<IRExpr, IRExpr>)((else_ir) => new IrIf(lower_expr(c, new BooleanTy(), ctx), then_ir, else_ir, result_ty)))(lower_expr(e2, result_ty, ctx))))(ty switch { ErrorTy { } => then_ty, _ => ty, })))(ir_expr_type(then_ir))))(lower_expr(t, ty, ctx)), ALetExpr(var binds, var body) => lower_let(binds, body, ty, ctx), ALambdaExpr(var @params, var body) => lower_lambda(@params, body, ty, ctx), AMatchExpr(var scrut, var arms) => lower_match(scrut, arms, ty, ctx), AListExpr(var elems) => lower_list(elems, ty, ctx), ARecordExpr(var name, var fields) => lower_record(name, fields, ty, ctx), AFieldAccess(var rec, var field) => ((Func<IRExpr, IRExpr>)((rec_ir) => ((Func<CodexType, IRExpr>)((rec_ty) => ((Func<CodexType, IRExpr>)((field_ty) => ((Func<CodexType, IRExpr>)((actual_field_ty) => new IrFieldAccess(rec_ir, field.value, actual_field_ty)))(field_ty switch { ErrorTy { } => ty, _ => field_ty, })))(rec_ty switch { RecordTy(var rname, var rfields) => lookup_record_field(rfields, field.value), ConstructedTy(var cname, var cargs) => ((Func<CodexType, CodexType>)((ctor_raw) => ((Func<CodexType, CodexType>)((resolved_record) => resolved_record switch { RecordTy(var rn, var rf) => lookup_record_field(rf, field.value), _ => ty, }))(ctor_raw switch { ErrorTy { } => new ErrorTy(), _ => strip_fun_args_lower(deep_resolve(ctx.ust, ctor_raw)), })))(lookup_type(ctx.types, cname.value)), _ => ty, })))(deep_resolve(ctx.ust, ir_expr_type(rec_ir)))))(lower_expr(rec, new ErrorTy(), ctx)), ADoExpr(var stmts) => lower_do(stmts, ty, ctx), AHandleExpr(var eff, var body, var clauses) => lower_handle(eff, body, clauses, ty, ctx), AErrorExpr(var msg) => new IrError(msg, ty), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static IRExpr lower_name(string name, CodexType ty, LowerCtx ctx) => ((Func<CodexType, IRExpr>)((raw) => raw switch { ErrorTy { } => new IrName(name, ty), _ => ((Func<CodexType, IRExpr>)((resolved) => ((Func<CodexType, IRExpr>)((stripped) => new IrName(name, stripped)))(strip_forall_ty(resolved))))(deep_resolve(ctx.ust, raw)), }))(lookup_type(ctx.types, name));

    public static IRExpr lower_literal(string text, LiteralKind kind) => kind switch { IntLit { } => new IrIntLit(long.Parse(_Cce.ToUnicode(text))), NumLit { } => new IrIntLit(long.Parse(_Cce.ToUnicode(text))), TextLit { } => new IrTextLit(text), CharLit { } => new IrCharLit(long.Parse(_Cce.ToUnicode(text))), BoolLit { } => new IrBoolLit((text == "True")), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static IRExpr lower_apply(AExpr f, AExpr a, CodexType ty, LowerCtx ctx) => ((Func<IRExpr, IRExpr>)((func_ir) => ((Func<CodexType, IRExpr>)((func_ty) => ((Func<CodexType, IRExpr>)((arg_ty) => ((Func<CodexType, IRExpr>)((ret_ty) => ((Func<IRExpr, IRExpr>)((arg_ir) => ((Func<CodexType, IRExpr>)((resolved_ret) => ((Func<CodexType, IRExpr>)((actual_ret) => lower_apply_dispatch(func_ir, arg_ir, actual_ret)))(resolved_ret switch { ErrorTy { } => ty, _ => resolved_ret, })))(subst_type_vars_from_arg(arg_ty, ir_expr_type(arg_ir), ret_ty))))(lower_expr(a, arg_ty, ctx))))(peel_fun_return(func_ty))))(peel_fun_param(func_ty))))(deep_resolve(ctx.ust, ir_expr_type(func_ir)))))(lower_expr(f, new ErrorTy(), ctx));

    public static IRExpr lower_apply_dispatch(IRExpr func_ir, IRExpr arg_ir, CodexType ret_ty) => func_ir switch { IrName(var n, var fty) => ((n == "fork") ? new IrFork(arg_ir, ret_ty) : ((n == "await") ? new IrAwait(arg_ir, ret_ty) : new IrApply(func_ir, arg_ir, ret_ty))), _ => new IrApply(func_ir, arg_ir, ret_ty), };

    public static IRExpr lower_let(List<ALetBind> binds, AExpr body, CodexType ty, LowerCtx ctx) => ((((long)binds.Count) == 0) ? lower_expr(body, ty, ctx) : ((Func<ALetBind, IRExpr>)((b) => ((Func<IRExpr, IRExpr>)((val_ir) => ((Func<CodexType, IRExpr>)((val_ty) => ((Func<LowerCtx, IRExpr>)((ctx2) => new IrLet(b.name.value, val_ty, val_ir, lower_let_rest(binds, body, ty, ctx2, 1))))(new LowerCtx(types: Enumerable.Concat(new List<TypeBinding> { new TypeBinding(name: b.name.value, bound_type: val_ty) }, ctx.types).ToList(), ust: ctx.ust))))(deep_resolve(ctx.ust, ir_expr_type(val_ir)))))(lower_expr(b.value, new ErrorTy(), ctx))))(binds[(int)0]));

    public static IRExpr lower_let_rest(List<ALetBind> binds, AExpr body, CodexType ty, LowerCtx ctx, long i) => ((i == ((long)binds.Count)) ? lower_expr(body, ty, ctx) : ((Func<ALetBind, IRExpr>)((b) => ((Func<IRExpr, IRExpr>)((val_ir) => ((Func<CodexType, IRExpr>)((val_ty) => ((Func<LowerCtx, IRExpr>)((ctx2) => new IrLet(b.name.value, val_ty, val_ir, lower_let_rest(binds, body, ty, ctx2, (i + 1)))))(new LowerCtx(types: Enumerable.Concat(new List<TypeBinding> { new TypeBinding(name: b.name.value, bound_type: val_ty) }, ctx.types).ToList(), ust: ctx.ust))))(deep_resolve(ctx.ust, ir_expr_type(val_ir)))))(lower_expr(b.value, new ErrorTy(), ctx))))(binds[(int)i]));

    public static IRExpr lower_lambda(List<Name> @params, AExpr body, CodexType ty, LowerCtx ctx) => ((Func<CodexType, IRExpr>)((stripped) => ((Func<List<IRParam>, IRExpr>)((lparams) => ((Func<LowerCtx, IRExpr>)((lctx) => new IrLambda(lparams, lower_expr(body, get_lambda_return(stripped, ((long)@params.Count)), lctx), ty)))(bind_lambda_to_ctx(ctx, @params, stripped, 0))))(lower_lambda_params(@params, stripped, 0))))(strip_forall_ty(ty));

    public static LowerCtx bind_lambda_to_ctx(LowerCtx ctx, List<Name> @params, CodexType ty, long i)
    {
        while (true)
        {
            if ((i == ((long)@params.Count)))
            {
            return ctx;
            }
            else
            {
            var p = @params[(int)i];
            var param_ty = peel_fun_param(ty);
            var rest_ty = peel_fun_return(ty);
            var ctx2 = new LowerCtx(types: Enumerable.Concat(new List<TypeBinding> { new TypeBinding(name: p.value, bound_type: param_ty) }, ctx.types).ToList(), ust: ctx.ust);
            var _tco_0 = ctx2;
            var _tco_1 = @params;
            var _tco_2 = rest_ty;
            var _tco_3 = (i + 1);
            ctx = _tco_0;
            @params = _tco_1;
            ty = _tco_2;
            i = _tco_3;
            continue;
            }
        }
    }

    public static List<IRParam> lower_lambda_params(List<Name> @params, CodexType ty, long i) => lower_lambda_params_acc(@params, ty, i, new List<IRParam>());

    public static List<IRParam> lower_lambda_params_acc(List<Name> @params, CodexType ty, long i, List<IRParam> acc)
    {
        while (true)
        {
            if ((i == ((long)@params.Count)))
            {
            return acc;
            }
            else
            {
            var p = @params[(int)i];
            var param_ty = peel_fun_param(ty);
            var rest_ty = peel_fun_return(ty);
            var _tco_0 = @params;
            var _tco_1 = rest_ty;
            var _tco_2 = (i + 1);
            var _tco_3 = ((Func<List<IRParam>>)(() => { var _l = acc; _l.Add(new IRParam(name: p.value, type_val: param_ty)); return _l; }))();
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
            return ty;
            }
            else
            {
            var _tco_s = ty;
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
            return new ErrorTy();
            }
            }
        }
    }

    public static IRExpr lower_match(AExpr scrut, List<AMatchArm> arms, CodexType ty, LowerCtx ctx) => ((Func<IRExpr, IRExpr>)((scrut_ir) => ((Func<CodexType, IRExpr>)((scrut_ty) => ((Func<List<IRBranch>, IRExpr>)((branches) => ((Func<CodexType, IRExpr>)((result_ty) => new IrMatch(scrut_ir, branches, result_ty)))(ty switch { ErrorTy { } => infer_match_type(branches, 0, ((long)branches.Count)), _ => ty, })))(lower_match_arms_loop(arms, ty, scrut_ty, ctx, 0, ((long)arms.Count)))))(ir_expr_type(scrut_ir))))(lower_expr(scrut, new ErrorTy(), ctx));

    public static CodexType infer_match_type(List<IRBranch> branches, long i, long len)
    {
        while (true)
        {
            if ((i == len))
            {
            return new ErrorTy();
            }
            else
            {
            var b = branches[(int)i];
            var body_ty = ir_expr_type(b.body);
            var _tco_s = body_ty;
            if (_tco_s is ErrorTy _tco_m0)
            {
            var _tco_0 = branches;
            var _tco_1 = (i + 1);
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

    public static List<IRBranch> lower_match_arms_loop(List<AMatchArm> arms, CodexType ty, CodexType scrut_ty, LowerCtx ctx, long i, long len) => lower_match_arms_acc(arms, ty, scrut_ty, ctx, i, len, new List<IRBranch>());

    public static List<IRBranch> lower_match_arms_acc(List<AMatchArm> arms, CodexType ty, CodexType scrut_ty, LowerCtx ctx, long i, long len, List<IRBranch> acc)
    {
        while (true)
        {
            if ((i == len))
            {
            return acc;
            }
            else
            {
            var arm = arms[(int)i];
            var arm_ctx = bind_pattern_to_ctx(ctx, arm.pattern, scrut_ty);
            var _tco_0 = arms;
            var _tco_1 = ty;
            var _tco_2 = scrut_ty;
            var _tco_3 = ctx;
            var _tco_4 = (i + 1);
            var _tco_5 = len;
            var _tco_6 = ((Func<List<IRBranch>>)(() => { var _l = acc; _l.Add(new IRBranch(pattern: lower_pattern(arm.pattern), body: lower_expr(arm.body, ty, arm_ctx))); return _l; }))();
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

    public static LowerCtx bind_pattern_to_ctx(LowerCtx ctx, APat pat, CodexType ty) => pat switch { AVarPat(var name) => new LowerCtx(types: Enumerable.Concat(new List<TypeBinding> { new TypeBinding(name: name.value, bound_type: ty) }, ctx.types).ToList(), ust: ctx.ust), ACtorPat(var ctor_name, var sub_pats) => ((Func<CodexType, LowerCtx>)((ctor_raw) => ((Func<CodexType, LowerCtx>)((ctor_ty) => ((Func<CodexType, LowerCtx>)((ctor_stripped) => bind_ctor_pattern_fields(ctx, sub_pats, ctor_stripped, 0, ((long)sub_pats.Count))))(strip_forall_ty(ctor_ty))))(deep_resolve(ctx.ust, ctor_raw))))(lookup_type(ctx.types, ctor_name.value)), AWildPat { } => ctx, ALitPat(var text, var kind) => ctx, _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static LowerCtx bind_ctor_pattern_fields(LowerCtx ctx, List<APat> sub_pats, CodexType ctor_ty, long i, long len)
    {
        while (true)
        {
            if ((i == len))
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
            var ctx2 = bind_pattern_to_ctx(ctx, sub_pats[(int)i], param_ty);
            var _tco_0 = ctx2;
            var _tco_1 = sub_pats;
            var _tco_2 = ret_ty;
            var _tco_3 = (i + 1);
            var _tco_4 = len;
            ctx = _tco_0;
            sub_pats = _tco_1;
            ctor_ty = _tco_2;
            i = _tco_3;
            len = _tco_4;
            continue;
            }
            {
            var ctx2 = bind_pattern_to_ctx(ctx, sub_pats[(int)i], new ErrorTy());
            var _tco_0 = ctx2;
            var _tco_1 = sub_pats;
            var _tco_2 = ctor_ty;
            var _tco_3 = (i + 1);
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

    public static IRPat lower_pattern(APat p) => p switch { AVarPat(var name) => new IrVarPat(name.value, new ErrorTy()), ALitPat(var text, var kind) => new IrLitPat(text, new ErrorTy()), ACtorPat(var name, var subs) => new IrCtorPat(name.value, map_list(lower_pattern, subs), new ErrorTy()), AWildPat { } => new IrWildPat(), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static IRExpr lower_list(List<AExpr> elems, CodexType ty, LowerCtx ctx) => ((Func<CodexType, IRExpr>)((elem_ty) => new IrList(lower_list_elems_loop(elems, elem_ty, ctx, 0, ((long)elems.Count)), elem_ty)))(ty switch { ListTy(var e) => e, _ => ((((long)elems.Count) == 0) ? new ErrorTy() : ir_expr_type(lower_expr(elems[(int)0], new ErrorTy(), ctx))), });

    public static List<IRExpr> lower_list_elems_loop(List<AExpr> elems, CodexType elem_ty, LowerCtx ctx, long i, long len) => lower_list_elems_acc(elems, elem_ty, ctx, i, len, new List<IRExpr>());

    public static List<IRExpr> lower_list_elems_acc(List<AExpr> elems, CodexType elem_ty, LowerCtx ctx, long i, long len, List<IRExpr> acc)
    {
        while (true)
        {
            if ((i == len))
            {
            return acc;
            }
            else
            {
            var _tco_0 = elems;
            var _tco_1 = elem_ty;
            var _tco_2 = ctx;
            var _tco_3 = (i + 1);
            var _tco_4 = len;
            var _tco_5 = ((Func<List<IRExpr>>)(() => { var _l = acc; _l.Add(lower_expr(elems[(int)i], elem_ty, ctx)); return _l; }))();
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

    public static IRExpr lower_record(Name name, List<AFieldExpr> fields, CodexType ty, LowerCtx ctx) => ((Func<CodexType, IRExpr>)((ctor_raw) => ((Func<CodexType, IRExpr>)((record_ty) => ((Func<CodexType, IRExpr>)((actual_ty) => new IrRecord(name.value, lower_record_fields_typed(fields, actual_ty, ctx, 0, ((long)fields.Count)), actual_ty)))(record_ty switch { ErrorTy { } => ty, _ => record_ty, })))(ctor_raw switch { ErrorTy { } => ty, _ => strip_fun_args_lower(deep_resolve(ctx.ust, ctor_raw)), })))(lookup_type(ctx.types, name.value));

    public static List<IRFieldVal> lower_record_fields_typed(List<AFieldExpr> fields, CodexType record_ty, LowerCtx ctx, long i, long len) => lower_record_fields_acc(fields, record_ty, ctx, i, len, new List<IRFieldVal>());

    public static List<IRFieldVal> lower_record_fields_acc(List<AFieldExpr> fields, CodexType record_ty, LowerCtx ctx, long i, long len, List<IRFieldVal> acc)
    {
        while (true)
        {
            if ((i == len))
            {
            return acc;
            }
            else
            {
            var f = fields[(int)i];
            var field_expected = record_ty switch { RecordTy(var rname, var rfields) => lookup_record_field(rfields, f.name.value), _ => new ErrorTy(), };
            var _tco_0 = fields;
            var _tco_1 = record_ty;
            var _tco_2 = ctx;
            var _tco_3 = (i + 1);
            var _tco_4 = len;
            var _tco_5 = ((Func<List<IRFieldVal>>)(() => { var _l = acc; _l.Add(new IRFieldVal(name: f.name.value, value: lower_expr(f.value, field_expected, ctx))); return _l; }))();
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

    public static IRExpr lower_do(List<ADoStmt> stmts, CodexType ty, LowerCtx ctx) => new IrDo(lower_do_stmts_loop(stmts, ty, ctx, 0, ((long)stmts.Count)), ty);

    public static List<IRDoStmt> lower_do_stmts_loop(List<ADoStmt> stmts, CodexType ty, LowerCtx ctx, long i, long len) => lower_do_stmts_acc(stmts, ty, ctx, i, len, new List<IRDoStmt>());

    public static List<IRDoStmt> lower_do_stmts_acc(List<ADoStmt> stmts, CodexType ty, LowerCtx ctx, long i, long len, List<IRDoStmt> acc)
    {
        while (true)
        {
            if ((i == len))
            {
            return acc;
            }
            else
            {
            var s = stmts[(int)i];
            var _tco_s = s;
            if (_tco_s is ADoBindStmt _tco_m0)
            {
                var name = _tco_m0.Field0;
                var val = _tco_m0.Field1;
            var val_ir = lower_expr(val, ty, ctx);
            var val_ty = ir_expr_type(val_ir);
            var ctx2 = new LowerCtx(types: Enumerable.Concat(new List<TypeBinding> { new TypeBinding(name: name.value, bound_type: val_ty) }, ctx.types).ToList(), ust: ctx.ust);
            var _tco_0 = stmts;
            var _tco_1 = ty;
            var _tco_2 = ctx2;
            var _tco_3 = (i + 1);
            var _tco_4 = len;
            var _tco_5 = ((Func<List<IRDoStmt>>)(() => { var _l = acc; _l.Add(new IrDoBind(name.value, val_ty, val_ir)); return _l; }))();
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
            var _tco_1 = ty;
            var _tco_2 = ctx;
            var _tco_3 = (i + 1);
            var _tco_4 = len;
            var _tco_5 = ((Func<List<IRDoStmt>>)(() => { var _l = acc; _l.Add(new IrDoExec(lower_expr(e, ty, ctx))); return _l; }))();
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

    public static IRExpr lower_handle(Name eff, AExpr body, List<AHandleClause> clauses, CodexType ty, LowerCtx ctx) => ((Func<IRExpr, IRExpr>)((body_ir) => new IrHandle(eff.value, body_ir, lower_handle_clauses(clauses, ty, ctx), ty)))(lower_expr(body, ty, ctx));

    public static List<IRHandleClause> lower_handle_clauses(List<AHandleClause> clauses, CodexType ty, LowerCtx ctx) => lower_handle_clauses_loop(clauses, ty, ctx, 0);

    public static List<IRHandleClause> lower_handle_clauses_loop(List<AHandleClause> clauses, CodexType ty, LowerCtx ctx, long i) => lower_handle_clauses_acc(clauses, ty, ctx, i, new List<IRHandleClause>());

    public static List<IRHandleClause> lower_handle_clauses_acc(List<AHandleClause> clauses, CodexType ty, LowerCtx ctx, long i, List<IRHandleClause> acc)
    {
        while (true)
        {
            if ((i == ((long)clauses.Count)))
            {
            return acc;
            }
            else
            {
            var c = clauses[(int)i];
            var body_ir = lower_expr(c.body, ty, ctx);
            var _tco_0 = clauses;
            var _tco_1 = ty;
            var _tco_2 = ctx;
            var _tco_3 = (i + 1);
            var _tco_4 = ((Func<List<IRHandleClause>>)(() => { var _l = acc; _l.Add(new IRHandleClause(op_name: c.op_name.value, resume_name: c.resume_name.value, body: body_ir)); return _l; }))();
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

    public static LowerCtx build_def_ctx(List<TypeBinding> types, UnificationState ust, List<AParam> @params, CodexType ty) => ((Func<LowerCtx, LowerCtx>)((base_ctx) => bind_params_to_ctx(base_ctx, @params, ty, 0)))(new LowerCtx(types: types, ust: ust));

    public static LowerCtx bind_params_to_ctx(LowerCtx ctx, List<AParam> @params, CodexType ty, long i)
    {
        while (true)
        {
            if ((i == ((long)@params.Count)))
            {
            return ctx;
            }
            else
            {
            var p = @params[(int)i];
            var param_ty = peel_fun_param(ty);
            var rest_ty = peel_fun_return(ty);
            var ctx2 = new LowerCtx(types: Enumerable.Concat(new List<TypeBinding> { new TypeBinding(name: p.name.value, bound_type: param_ty) }, ctx.types).ToList(), ust: ctx.ust);
            var _tco_0 = ctx2;
            var _tco_1 = @params;
            var _tco_2 = rest_ty;
            var _tco_3 = (i + 1);
            ctx = _tco_0;
            @params = _tco_1;
            ty = _tco_2;
            i = _tco_3;
            continue;
            }
        }
    }

    public static List<IRParam> lower_def_params(List<AParam> @params, CodexType ty, long i) => lower_def_params_acc(@params, ty, i, new List<IRParam>());

    public static List<IRParam> lower_def_params_acc(List<AParam> @params, CodexType ty, long i, List<IRParam> acc)
    {
        while (true)
        {
            if ((i == ((long)@params.Count)))
            {
            return acc;
            }
            else
            {
            var p = @params[(int)i];
            var param_ty = peel_fun_param(ty);
            var rest_ty = peel_fun_return(ty);
            var _tco_0 = @params;
            var _tco_1 = rest_ty;
            var _tco_2 = (i + 1);
            var _tco_3 = ((Func<List<IRParam>>)(() => { var _l = acc; _l.Add(new IRParam(name: p.name.value, type_val: param_ty)); return _l; }))();
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
            return ty;
            }
            else
            {
            var _tco_s = ty;
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
            return new ErrorTy();
            }
            }
        }
    }

    public static IRModule lower_module(AModule m, List<TypeBinding> types, UnificationState ust) => ((Func<List<TypeBinding>, IRModule>)((ctor_types) => ((Func<List<TypeBinding>, IRModule>)((all_types) => new IRModule(name: m.name, defs: lower_defs(m.defs, all_types, ust, 0))))(Enumerable.Concat(ctor_types, Enumerable.Concat(types, builtin_type_env().bindings).ToList()).ToList())))(collect_ctor_bindings(m.type_defs, 0, ((long)m.type_defs.Count), new List<TypeBinding>()));

    public static List<IRDef> lower_defs(List<ADef> defs, List<TypeBinding> types, UnificationState ust, long i) => lower_defs_acc(defs, types, ust, i, new List<IRDef>());

    public static List<IRDef> lower_defs_acc(List<ADef> defs, List<TypeBinding> types, UnificationState ust, long i, List<IRDef> acc)
    {
        while (true)
        {
            if ((i == ((long)defs.Count)))
            {
            return acc;
            }
            else
            {
            var _tco_0 = defs;
            var _tco_1 = types;
            var _tco_2 = ust;
            var _tco_3 = (i + 1);
            var _tco_4 = ((Func<List<IRDef>>)(() => { var _l = acc; _l.Add(lower_def(defs[(int)i], types, ust)); return _l; }))();
            defs = _tco_0;
            types = _tco_1;
            ust = _tco_2;
            i = _tco_3;
            acc = _tco_4;
            continue;
            }
        }
    }

    public static CodexType lookup_type(List<TypeBinding> bindings, string name) => lookup_type_loop(bindings, name, 0, ((long)bindings.Count));

    public static CodexType lookup_type_loop(List<TypeBinding> bindings, string name, long i, long len)
    {
        while (true)
        {
            if ((i == len))
            {
            return new ErrorTy();
            }
            else
            {
            var b = bindings[(int)i];
            if ((b.name == name))
            {
            return b.bound_type;
            }
            else
            {
            var _tco_0 = bindings;
            var _tco_1 = name;
            var _tco_2 = (i + 1);
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
            var _tco_s = ty;
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
            var _tco_0 = body;
            ty = _tco_0;
            continue;
            }
            {
            return new ErrorTy();
            }
        }
    }

    public static CodexType peel_fun_return(CodexType ty)
    {
        while (true)
        {
            var _tco_s = ty;
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
            var _tco_0 = body;
            ty = _tco_0;
            continue;
            }
            {
            return new ErrorTy();
            }
        }
    }

    public static CodexType strip_forall_ty(CodexType ty)
    {
        while (true)
        {
            var _tco_s = ty;
            if (_tco_s is ForAllTy _tco_m0)
            {
                var id = _tco_m0.Field0;
                var body = _tco_m0.Field1;
            var _tco_0 = body;
            ty = _tco_0;
            continue;
            }
            {
            return ty;
            }
        }
    }

    public static CodexType subst_type_vars_from_arg(CodexType param_ty, CodexType arg_ty, CodexType target) => param_ty switch { TypeVar(var id) => subst_type_var_in_target(target, id, arg_ty), ListTy(var pe) => subst_from_list(pe, arg_ty, target), FunTy(var pp, var pr) => subst_from_fun(pp, pr, arg_ty, target), _ => target, };

    public static CodexType subst_from_list(CodexType pe, CodexType arg_ty, CodexType target) => arg_ty switch { ListTy(var ae) => subst_type_vars_from_arg(pe, ae, target), _ => target, };

    public static CodexType subst_from_fun(CodexType pp, CodexType pr, CodexType arg_ty, CodexType target) => arg_ty switch { FunTy(var ap, var ar) => ((Func<CodexType, CodexType>)((t2) => subst_type_vars_from_arg(pr, ar, t2)))(subst_type_vars_from_arg(pp, ap, target)), _ => target, };

    public static CodexType subst_type_var_in_target(CodexType ty, long var_id, CodexType replacement) => ty switch { TypeVar(var id) => ((id == var_id) ? replacement : ty), FunTy(var p, var r) => new FunTy(subst_type_var_in_target(p, var_id, replacement), subst_type_var_in_target(r, var_id, replacement)), ListTy(var elem) => new ListTy(subst_type_var_in_target(elem, var_id, replacement)), ForAllTy(var fid, var body) => ((fid == var_id) ? ty : new ForAllTy(fid, subst_type_var_in_target(body, var_id, replacement))), _ => ty, };

    public static CodexType strip_fun_args_lower(CodexType ty)
    {
        while (true)
        {
            var _tco_s = ty;
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
            var _tco_0 = body;
            ty = _tco_0;
            continue;
            }
            {
            return ty;
            }
        }
    }

    public static bool is_text_type(CodexType ty) => ty switch { TextTy { } => true, _ => false, };

    public static List<TypeBinding> collect_ctor_bindings(List<ATypeDef> tdefs, long i, long len, List<TypeBinding> acc)
    {
        while (true)
        {
            if ((i == len))
            {
            return acc;
            }
            else
            {
            var td = tdefs[(int)i];
            var bindings = ctor_bindings_for_typedef(td);
            var _tco_0 = tdefs;
            var _tco_1 = (i + 1);
            var _tco_2 = len;
            var _tco_3 = Enumerable.Concat(acc, bindings).ToList();
            tdefs = _tco_0;
            i = _tco_1;
            len = _tco_2;
            acc = _tco_3;
            continue;
            }
        }
    }

    public static List<TypeBinding> ctor_bindings_for_typedef(ATypeDef td) => td switch { AVariantTypeDef(var name, var type_params, var ctors) => ((Func<CodexType, List<TypeBinding>>)((result_ty) => collect_variant_ctor_bindings(ctors, result_ty, 0, ((long)ctors.Count), new List<TypeBinding>())))(new ConstructedTy(name, new List<CodexType>())), ARecordTypeDef(var name, var type_params, var fields) => ((Func<List<RecordField>, List<TypeBinding>>)((resolved_fields) => ((Func<CodexType, List<TypeBinding>>)((result_ty) => ((Func<CodexType, List<TypeBinding>>)((ctor_ty) => new List<TypeBinding> { new TypeBinding(name: name.value, bound_type: ctor_ty) }))(build_record_ctor_type_for_lower(fields, result_ty, 0, ((long)fields.Count)))))(new RecordTy(name, resolved_fields))))(build_record_fields_for_lower(fields, 0, ((long)fields.Count), new List<RecordField>())), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static List<TypeBinding> collect_variant_ctor_bindings(List<AVariantCtorDef> ctors, CodexType result_ty, long i, long len, List<TypeBinding> acc)
    {
        while (true)
        {
            if ((i == len))
            {
            return acc;
            }
            else
            {
            var ctor = ctors[(int)i];
            var ctor_ty = build_ctor_type_for_lower(ctor.fields, result_ty, 0, ((long)ctor.fields.Count));
            var _tco_0 = ctors;
            var _tco_1 = result_ty;
            var _tco_2 = (i + 1);
            var _tco_3 = len;
            var _tco_4 = ((Func<List<TypeBinding>>)(() => { var _l = acc; _l.Add(new TypeBinding(name: ctor.name.value, bound_type: ctor_ty)); return _l; }))();
            ctors = _tco_0;
            result_ty = _tco_1;
            i = _tco_2;
            len = _tco_3;
            acc = _tco_4;
            continue;
            }
        }
    }

    public static CodexType build_ctor_type_for_lower(List<ATypeExpr> fields, CodexType result, long i, long len) => ((i == len) ? result : ((Func<CodexType, CodexType>)((rest) => new FunTy(resolve_type_expr_for_lower(fields[(int)i]), rest)))(build_ctor_type_for_lower(fields, result, (i + 1), len)));

    public static List<RecordField> build_record_fields_for_lower(List<ARecordFieldDef> fields, long i, long len, List<RecordField> acc)
    {
        while (true)
        {
            if ((i == len))
            {
            return acc;
            }
            else
            {
            var f = fields[(int)i];
            var rfield = new RecordField(name: f.name, type_val: resolve_type_expr_for_lower(f.type_expr));
            var _tco_0 = fields;
            var _tco_1 = (i + 1);
            var _tco_2 = len;
            var _tco_3 = ((Func<List<RecordField>>)(() => { var _l = acc; _l.Add(rfield); return _l; }))();
            fields = _tco_0;
            i = _tco_1;
            len = _tco_2;
            acc = _tco_3;
            continue;
            }
        }
    }

    public static CodexType build_record_ctor_type_for_lower(List<ARecordFieldDef> fields, CodexType result, long i, long len) => ((i == len) ? result : ((Func<ARecordFieldDef, CodexType>)((f) => ((Func<CodexType, CodexType>)((rest) => new FunTy(resolve_type_expr_for_lower(f.type_expr), rest)))(build_record_ctor_type_for_lower(fields, result, (i + 1), len))))(fields[(int)i]));

    public static CodexType resolve_type_expr_for_lower(ATypeExpr texpr) => texpr switch { ANamedType(var name) => ((name.value == "Integer") ? new IntegerTy() : ((name.value == "Number") ? new NumberTy() : ((name.value == "Text") ? new TextTy() : ((name.value == "Boolean") ? new BooleanTy() : ((name.value == "Nothing") ? new NothingTy() : new ConstructedTy(name, new List<CodexType>())))))), AFunType(var param, var ret) => new FunTy(resolve_type_expr_for_lower(param), resolve_type_expr_for_lower(ret)), AAppType(var ctor, var args) => ctor switch { ANamedType(var cname) => ((cname.value == "List") ? ((((long)args.Count) == 1) ? new ListTy(resolve_type_expr_for_lower(args[(int)0])) : new ListTy(new ErrorTy())) : new ConstructedTy(cname, new List<CodexType>())), _ => new ErrorTy(), }, _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static IRBinaryOp lower_bin_op(BinaryOp op, CodexType ty) => op switch { OpAdd { } => new IrAddInt(), OpSub { } => new IrSubInt(), OpMul { } => new IrMulInt(), OpDiv { } => new IrDivInt(), OpPow { } => new IrPowInt(), OpEq { } => new IrEq(), OpNotEq { } => new IrNotEq(), OpLt { } => new IrLt(), OpGt { } => new IrGt(), OpLtEq { } => new IrLtEq(), OpGtEq { } => new IrGtEq(), OpDefEq { } => new IrEq(), OpAppend { } => (is_text_type(ty) ? new IrAppendText() : new IrAppendList()), OpCons { } => new IrConsList(), OpAnd { } => new IrAnd(), OpOr { } => new IrOr(), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static CodexType binary_result_type(BinaryOp op, CodexType left_ty, CodexType expected_ty) => op switch { OpEq { } => new BooleanTy(), OpNotEq { } => new BooleanTy(), OpLt { } => new BooleanTy(), OpGt { } => new BooleanTy(), OpLtEq { } => new BooleanTy(), OpGtEq { } => new BooleanTy(), OpDefEq { } => new BooleanTy(), OpAnd { } => new BooleanTy(), OpOr { } => new BooleanTy(), OpAppend { } => (is_text_type(left_ty) ? new TextTy() : (is_text_type(expected_ty) ? new TextTy() : left_ty)), _ => left_ty, };

    public static Scope empty_scope() => new Scope(names: new List<string>());

    public static bool scope_has(Scope sc, string name) => ((Func<long, bool>)((len) => ((len == 0) ? false : ((Func<long, bool>)((pos) => ((pos >= len) ? false : (sc.names[(int)pos] == name))))(bsearch_text_set(sc.names, name, 0, len)))))(((long)sc.names.Count));

    public static Scope scope_add(Scope sc, string name) => ((Func<long, Scope>)((len) => ((Func<long, Scope>)((pos) => new Scope(names: ((Func<List<string>>)(() => { var _l = new List<string>(sc.names); _l.Insert((int)pos, name); return _l; }))())))(bsearch_text_set(sc.names, name, 0, len))))(((long)sc.names.Count));

    public static List<string> builtin_names() => new List<string> { "show", "negate", "True", "False", "Nothing", "print-line", "read-line", "read-file", "write-file", "file-exists", "list-files", "open-file", "read-all", "close-file", "char-at", "char-to-text", "text-length", "substring", "is-letter", "is-digit", "is-whitespace", "text-to-integer", "integer-to-text", "text-replace", "text-split", "text-contains", "text-starts-with", "char-code", "char-code-at", "code-to-char", "list-length", "list-at", "list-insert-at", "list-snoc", "text-compare", "get-args", "get-env", "current-dir", "map", "filter", "fold", "text-concat-list" };

    public static bool is_type_name(string name) => ((((long)name.Length) == 0) ? false : ((((long)name[(int)0]) >= 13L && ((long)name[(int)0]) <= 64L) && is_upper_char(((long)name[(int)0]))));

    public static bool is_upper_char(long c) => ((Func<long, bool>)((code) => ((code >= 39) && (code <= 64))))(c);

    public static CollectResult collect_top_level_names(List<ADef> defs, long i, long len, List<string> acc, List<Diagnostic> errs)
    {
        while (true)
        {
            if ((i == len))
            {
            return new CollectResult(names: acc, errors: errs);
            }
            else
            {
            var def = defs[(int)i];
            var name = def.name.value;
            if (list_contains(acc, name))
            {
            var _tco_0 = defs;
            var _tco_1 = (i + 1);
            var _tco_2 = len;
            var _tco_3 = acc;
            var _tco_4 = Enumerable.Concat(errs, new List<Diagnostic> { make_error("CDX3001", ("Duplicate definition: " + name)) }).ToList();
            defs = _tco_0;
            i = _tco_1;
            len = _tco_2;
            acc = _tco_3;
            errs = _tco_4;
            continue;
            }
            else
            {
            var pos = bsearch_text_set(acc, name, 0, ((long)acc.Count));
            var _tco_0 = defs;
            var _tco_1 = (i + 1);
            var _tco_2 = len;
            var _tco_3 = ((Func<List<string>>)(() => { var _l = new List<string>(acc); _l.Insert((int)pos, name); return _l; }))();
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

    public static bool list_contains(List<string> xs, string name) => ((Func<long, bool>)((len) => ((len == 0) ? false : ((Func<long, bool>)((pos) => ((pos >= len) ? false : (xs[(int)pos] == name))))(bsearch_text_set(xs, name, 0, len)))))(((long)xs.Count));

    public static CtorCollectResult collect_ctor_names(List<ATypeDef> type_defs, long i, long len, List<string> type_acc, List<string> ctor_acc)
    {
        while (true)
        {
            if ((i == len))
            {
            return new CtorCollectResult(type_names: type_acc, ctor_names: ctor_acc);
            }
            else
            {
            var td = type_defs[(int)i];
            var _tco_s = td;
            if (_tco_s is AVariantTypeDef _tco_m0)
            {
                var name = _tco_m0.Field0;
                var @params = _tco_m0.Field1;
                var ctors = _tco_m0.Field2;
            var new_type_acc = ((Func<List<string>>)(() => { var _l = type_acc; _l.Add(name.value); return _l; }))();
            var new_ctor_acc = collect_variant_ctors(ctors, 0, ((long)ctors.Count), ctor_acc);
            var _tco_0 = type_defs;
            var _tco_1 = (i + 1);
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
            var _tco_1 = (i + 1);
            var _tco_2 = len;
            var _tco_3 = ((Func<List<string>>)(() => { var _l = type_acc; _l.Add(name.value); return _l; }))();
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
            if ((i == len))
            {
            return acc;
            }
            else
            {
            var ctor = ctors[(int)i];
            var _tco_0 = ctors;
            var _tco_1 = (i + 1);
            var _tco_2 = len;
            var _tco_3 = ((Func<List<string>>)(() => { var _l = acc; _l.Add(ctor.name.value); return _l; }))();
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
            if ((i == len))
            {
            return sc;
            }
            else
            {
            var _tco_0 = scope_add(sc, names[(int)i]);
            var _tco_1 = names;
            var _tco_2 = (i + 1);
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
            if ((scope_has(sc, name.value) || is_type_name(name.value)))
            {
            return new List<Diagnostic>();
            }
            else
            {
            return new List<Diagnostic> { make_error("CDX3002", ("Undefined name: " + name.value)) };
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
            return resolve_let(sc, bindings, body, 0, ((long)bindings.Count), new List<Diagnostic>());
            }
            else if (_tco_s is ALambdaExpr _tco_m7)
            {
                var @params = _tco_m7.Field0;
                var body = _tco_m7.Field1;
            var sc2 = add_lambda_params(sc, @params, 0, ((long)@params.Count));
            var _tco_0 = sc2;
            var _tco_1 = body;
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
            if ((i == len))
            {
            return Enumerable.Concat(errs, resolve_expr(sc, body)).ToList();
            }
            else
            {
            var b = bindings[(int)i];
            var bind_errs = resolve_expr(sc, b.value);
            var sc2 = scope_add(sc, b.name.value);
            var _tco_0 = sc2;
            var _tco_1 = bindings;
            var _tco_2 = body;
            var _tco_3 = (i + 1);
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
            if ((i == len))
            {
            return sc;
            }
            else
            {
            var p = @params[(int)i];
            var _tco_0 = scope_add(sc, p.value);
            var _tco_1 = @params;
            var _tco_2 = (i + 1);
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
            if ((i == len))
            {
            return errs;
            }
            else
            {
            var arm = arms[(int)i];
            var sc2 = collect_pattern_names(sc, arm.pattern);
            var arm_errs = resolve_expr(sc2, arm.body);
            var _tco_0 = sc;
            var _tco_1 = arms;
            var _tco_2 = (i + 1);
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

    public static Scope collect_pattern_names(Scope sc, APat pat) => pat switch { AVarPat(var name) => scope_add(sc, name.value), ACtorPat(var name, var subs) => collect_ctor_pat_names(sc, subs, 0, ((long)subs.Count)), ALitPat(var val, var kind) => sc, AWildPat { } => sc, _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static Scope collect_ctor_pat_names(Scope sc, List<APat> subs, long i, long len)
    {
        while (true)
        {
            if ((i == len))
            {
            return sc;
            }
            else
            {
            var sub = subs[(int)i];
            var _tco_0 = collect_pattern_names(sc, sub);
            var _tco_1 = subs;
            var _tco_2 = (i + 1);
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
            if ((i == len))
            {
            return errs;
            }
            else
            {
            var errs2 = resolve_expr(sc, elems[(int)i]);
            var _tco_0 = sc;
            var _tco_1 = elems;
            var _tco_2 = (i + 1);
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
            if ((i == len))
            {
            return errs;
            }
            else
            {
            var f = fields[(int)i];
            var errs2 = resolve_expr(sc, f.value);
            var _tco_0 = sc;
            var _tco_1 = fields;
            var _tco_2 = (i + 1);
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
            if ((i == len))
            {
            return errs;
            }
            else
            {
            var stmt = stmts[(int)i];
            var _tco_s = stmt;
            if (_tco_s is ADoExprStmt _tco_m0)
            {
                var e = _tco_m0.Field0;
            var errs2 = resolve_expr(sc, e);
            var _tco_0 = sc;
            var _tco_1 = stmts;
            var _tco_2 = (i + 1);
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
            var errs2 = resolve_expr(sc, e);
            var sc2 = scope_add(sc, name.value);
            var _tco_0 = sc2;
            var _tco_1 = stmts;
            var _tco_2 = (i + 1);
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
            if ((i == len))
            {
            return errs;
            }
            else
            {
            var def = defs[(int)i];
            var def_scope = add_def_params(sc, def.@params, 0, ((long)def.@params.Count));
            var errs2 = resolve_expr(def_scope, def.body);
            var _tco_0 = sc;
            var _tco_1 = defs;
            var _tco_2 = (i + 1);
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
            if ((i == len))
            {
            return sc;
            }
            else
            {
            var p = @params[(int)i];
            var _tco_0 = scope_add(sc, p.name.value);
            var _tco_1 = @params;
            var _tco_2 = (i + 1);
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
            if ((i == len))
            {
            return acc;
            }
            else
            {
            var r = results[(int)i];
            var names = Enumerable.Concat(r.top_level_names, r.ctor_names).ToList();
            var _tco_0 = results;
            var _tco_1 = (i + 1);
            var _tco_2 = len;
            var _tco_3 = Enumerable.Concat(acc, names).ToList();
            results = _tco_0;
            i = _tco_1;
            len = _tco_2;
            acc = _tco_3;
            continue;
            }
        }
    }

    public static long cc_newline() => 10;

    public static long cc_cr() => (0 - 1);

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

    public static long cc_upper_a() => 39;

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

    public static bool is_at_end(LexState st) => (st.offset >= ((long)st.source.Length));

    public static long peek_code(LexState st) => (is_at_end(st) ? 0 : ((long)st.source[(int)st.offset]));

    public static LexState advance_char(LexState st) => ((peek_code(st) == cc_newline()) ? new LexState(source: st.source, offset: (st.offset + 1), line: (st.line + 1), column: 1) : new LexState(source: st.source, offset: (st.offset + 1), line: st.line, column: (st.column + 1)));

    public static long skip_spaces_end(string source, long offset, long len)
    {
        while (true)
        {
            if ((offset >= len))
            {
            return offset;
            }
            else
            {
            if ((((long)source[(int)offset]) == cc_space()))
            {
            var _tco_0 = source;
            var _tco_1 = (offset + 1);
            var _tco_2 = len;
            source = _tco_0;
            offset = _tco_1;
            len = _tco_2;
            continue;
            }
            else
            {
            return offset;
            }
            }
        }
    }

    public static LexState skip_spaces(LexState st) => ((Func<long, LexState>)((len) => ((Func<long, LexState>)((end) => ((end == st.offset) ? st : new LexState(source: st.source, offset: end, line: st.line, column: (st.column + (end - st.offset))))))(skip_spaces_end(st.source, st.offset, len))))(((long)st.source.Length));

    public static long scan_ident_end(string source, long offset, long len)
    {
        while (true)
        {
            if ((offset >= len))
            {
            return offset;
            }
            else
            {
            var c = ((long)source[(int)offset]);
            if (is_letter_code(c))
            {
            var _tco_0 = source;
            var _tco_1 = (offset + 1);
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
            var _tco_1 = (offset + 1);
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
            var _tco_1 = (offset + 1);
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
            if (((offset + 1) >= len))
            {
            return offset;
            }
            else
            {
            if (is_letter_code(((long)source[(int)(offset + 1)])))
            {
            var _tco_0 = source;
            var _tco_1 = (offset + 1);
            var _tco_2 = len;
            source = _tco_0;
            offset = _tco_1;
            len = _tco_2;
            continue;
            }
            else
            {
            return offset;
            }
            }
            }
            else
            {
            return offset;
            }
            }
            }
            }
            }
        }
    }

    public static LexState scan_ident_rest(LexState st) => ((Func<long, LexState>)((len) => ((Func<long, LexState>)((end) => ((end == st.offset) ? st : new LexState(source: st.source, offset: end, line: st.line, column: (st.column + (end - st.offset))))))(scan_ident_end(st.source, st.offset, len))))(((long)st.source.Length));

    public static long scan_digits_end(string source, long offset, long len)
    {
        while (true)
        {
            if ((offset >= len))
            {
            return offset;
            }
            else
            {
            var c = ((long)source[(int)offset]);
            if (is_digit_code(c))
            {
            var _tco_0 = source;
            var _tco_1 = (offset + 1);
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
            var _tco_1 = (offset + 1);
            var _tco_2 = len;
            source = _tco_0;
            offset = _tco_1;
            len = _tco_2;
            continue;
            }
            else
            {
            return offset;
            }
            }
            }
        }
    }

    public static LexState scan_digits(LexState st) => ((Func<long, LexState>)((len) => ((Func<long, LexState>)((end) => ((end == st.offset) ? st : new LexState(source: st.source, offset: end, line: st.line, column: (st.column + (end - st.offset))))))(scan_digits_end(st.source, st.offset, len))))(((long)st.source.Length));

    public static long scan_string_end(string source, long offset, long len)
    {
        while (true)
        {
            if ((offset >= len))
            {
            return offset;
            }
            else
            {
            var c = ((long)source[(int)offset]);
            if ((c == cc_double_quote()))
            {
            return (offset + 1);
            }
            else
            {
            if ((c == cc_newline()))
            {
            return offset;
            }
            else
            {
            if ((c == cc_backslash()))
            {
            var _tco_0 = source;
            var _tco_1 = (offset + 2);
            var _tco_2 = len;
            source = _tco_0;
            offset = _tco_1;
            len = _tco_2;
            continue;
            }
            else
            {
            var _tco_0 = source;
            var _tco_1 = (offset + 1);
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

    public static LexState scan_string_body(LexState st) => ((Func<long, LexState>)((len) => ((Func<long, LexState>)((end) => ((end == st.offset) ? st : new LexState(source: st.source, offset: end, line: st.line, column: (st.column + (end - st.offset))))))(scan_string_end(st.source, st.offset, len))))(((long)st.source.Length));

    public static string process_escapes(string s, long i, long len, string acc)
    {
        while (true)
        {
            if ((i >= len))
            {
            return acc;
            }
            else
            {
            var c = ((long)s[(int)i]);
            if ((c == cc_backslash()))
            {
            if (((i + 1) < len))
            {
            var nc = ((long)s[(int)(i + 1)]);
            if ((nc == cc_lower_n()))
            {
            var _tco_0 = s;
            var _tco_1 = (i + 2);
            var _tco_2 = len;
            var _tco_3 = (acc + ((char)10).ToString());
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
            var _tco_1 = (i + 2);
            var _tco_2 = len;
            var _tco_3 = (acc + "  ");
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
            var _tco_1 = (i + 2);
            var _tco_2 = len;
            var _tco_3 = acc;
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
            var _tco_1 = (i + 2);
            var _tco_2 = len;
            var _tco_3 = (acc + "\\");
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
            var _tco_1 = (i + 2);
            var _tco_2 = len;
            var _tco_3 = (acc + "\"");
            s = _tco_0;
            i = _tco_1;
            len = _tco_2;
            acc = _tco_3;
            continue;
            }
            else
            {
            var _tco_0 = s;
            var _tco_1 = (i + 2);
            var _tco_2 = len;
            var _tco_3 = (acc + ((char)((long)s[(int)(i + 1)])).ToString());
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
            return (acc + ((char)((long)s[(int)i])).ToString());
            }
            }
            else
            {
            var _tco_0 = s;
            var _tco_1 = (i + 1);
            var _tco_2 = len;
            var _tco_3 = (acc + ((char)((long)s[(int)i])).ToString());
            s = _tco_0;
            i = _tco_1;
            len = _tco_2;
            acc = _tco_3;
            continue;
            }
            }
        }
    }

    public static TokenKind classify_word(string w) => ((w == "let") ? new LetKeyword() : ((w == "in") ? new InKeyword() : ((w == "if") ? new IfKeyword() : ((w == "then") ? new ThenKeyword() : ((w == "else") ? new ElseKeyword() : ((w == "when") ? new WhenKeyword() : ((w == "where") ? new WhereKeyword() : ((w == "do") ? new DoKeyword() : ((w == "record") ? new RecordKeyword() : ((w == "import") ? new ImportKeyword() : ((w == "export") ? new ExportKeyword() : ((w == "claim") ? new ClaimKeyword() : ((w == "proof") ? new ProofKeyword() : ((w == "forall") ? new ForAllKeyword() : ((w == "exists") ? new ThereExistsKeyword() : ((w == "linear") ? new LinearKeyword() : ((w == "effect") ? new EffectKeyword() : ((w == "with") ? new WithKeyword() : ((w == "True") ? new TrueKeyword() : ((w == "False") ? new FalseKeyword() : ((Func<long, TokenKind>)((first_code) => ((first_code >= cc_upper_a()) ? ((first_code <= cc_upper_z()) ? new TypeIdentifier() : new Identifier()) : new Identifier())))(((long)w[(int)0]))))))))))))))))))))));

    public static Token make_token(TokenKind kind, string text, LexState st) => new Token(kind: kind, text: text, offset: st.offset, line: st.line, column: st.column);

    public static string extract_text(LexState st, long start, LexState end_st) => st.source.Substring((int)start, (int)(end_st.offset - start));

    public static LexResult scan_token(LexState st)
    {
        while (true)
        {
            var s = skip_spaces(st);
            if (is_at_end(s))
            {
            return new LexEnd();
            }
            else
            {
            var c = peek_code(s);
            if ((c == cc_cr()))
            {
            var _tco_0 = advance_char(s);
            st = _tco_0;
            continue;
            }
            else
            {
            if ((c == cc_newline()))
            {
            return new LexToken(make_token(new Newline(), "\u0001", s), advance_char(s));
            }
            else
            {
            if ((c == cc_double_quote()))
            {
            var start = (s.offset + 1);
            var after = scan_string_body(advance_char(s));
            var text_len = ((after.offset - start) - 1);
            var raw = s.source.Substring((int)start, (int)text_len);
            return new LexToken(make_token(new TextLiteral(), process_escapes(raw, 0, ((long)raw.Length), ""), s), after);
            }
            else
            {
            if ((c == cc_single_quote()))
            {
            return scan_char_literal(s);
            }
            else
            {
            if (is_letter_code(c))
            {
            var start = s.offset;
            var after = scan_ident_rest(advance_char(s));
            var word = extract_text(s, start, after);
            return new LexToken(make_token(classify_word(word), word, s), after);
            }
            else
            {
            if ((c == cc_underscore()))
            {
            var start = s.offset;
            var after = scan_ident_rest(advance_char(s));
            var word = extract_text(s, start, after);
            if ((((long)word.Length) == 1))
            {
            return new LexToken(make_token(new Underscore(), "_", s), after);
            }
            else
            {
            return new LexToken(make_token(classify_word(word), word, s), after);
            }
            }
            else
            {
            if (is_digit_code(c))
            {
            var start = s.offset;
            var after = scan_digits(advance_char(s));
            if (is_at_end(after))
            {
            return new LexToken(make_token(new IntegerLiteral(), extract_text(s, start, after), s), after);
            }
            else
            {
            if ((peek_code(after) == cc_dot()))
            {
            var after2 = scan_digits(advance_char(after));
            return new LexToken(make_token(new NumberLiteral(), extract_text(s, start, after2), s), after2);
            }
            else
            {
            return new LexToken(make_token(new IntegerLiteral(), extract_text(s, start, after), s), after);
            }
            }
            }
            else
            {
            return scan_operator(s);
            }
            }
            }
            }
            }
            }
            }
            }
        }
    }

    public static LexResult scan_operator(LexState s) => ((Func<long, LexResult>)((c) => ((Func<LexState, LexResult>)((next) => ((c == cc_left_paren()) ? new LexToken(make_token(new LeftParen(), "(", s), next) : ((c == cc_right_paren()) ? new LexToken(make_token(new RightParen(), ")", s), next) : ((c == cc_left_bracket()) ? new LexToken(make_token(new LeftBracket(), "[", s), next) : ((c == cc_right_bracket()) ? new LexToken(make_token(new RightBracket(), "]", s), next) : ((c == cc_left_brace()) ? new LexToken(make_token(new LeftBrace(), "{", s), next) : ((c == cc_right_brace()) ? new LexToken(make_token(new RightBrace(), "}", s), next) : ((c == cc_comma()) ? new LexToken(make_token(new Comma(), ",", s), next) : ((c == cc_dot()) ? new LexToken(make_token(new Dot(), ".", s), next) : ((c == cc_caret()) ? new LexToken(make_token(new Caret(), "\u00E0\u0081\u009E", s), next) : ((c == cc_ampersand()) ? new LexToken(make_token(new Ampersand(), "&", s), next) : ((c == cc_backslash()) ? new LexToken(make_token(new Backslash(), "\\", s), next) : scan_multi_char_operator(s))))))))))))))(advance_char(s))))(peek_code(s));

    public static LexResult scan_multi_char_operator(LexState s) => ((Func<long, LexResult>)((c) => ((Func<LexState, LexResult>)((next) => ((Func<long, LexResult>)((nc) => ((c == cc_plus()) ? ((nc == cc_plus()) ? new LexToken(make_token(new PlusPlus(), "++", s), advance_char(next)) : new LexToken(make_token(new Plus(), "+", s), next)) : ((c == cc_minus()) ? ((nc == cc_greater()) ? new LexToken(make_token(new Arrow(), "->", s), advance_char(next)) : new LexToken(make_token(new Minus(), "-", s), next)) : ((c == cc_star()) ? new LexToken(make_token(new Star(), "*", s), next) : ((c == cc_slash()) ? ((nc == cc_equals()) ? new LexToken(make_token(new NotEquals(), "/=", s), advance_char(next)) : new LexToken(make_token(new Slash(), "/", s), next)) : ((c == cc_equals()) ? ((nc == cc_equals()) ? ((Func<LexState, LexResult>)((next2) => ((Func<long, LexResult>)((nc2) => ((nc2 == cc_equals()) ? new LexToken(make_token(new TripleEquals(), "===", s), advance_char(next2)) : new LexToken(make_token(new DoubleEquals(), "==", s), next2))))((is_at_end(next2) ? 0 : peek_code(next2)))))(advance_char(next)) : new LexToken(make_token(new Equals_(), "=", s), next)) : ((c == cc_colon()) ? ((nc == cc_colon()) ? new LexToken(make_token(new ColonColon(), "::", s), advance_char(next)) : new LexToken(make_token(new Colon(), ":", s), next)) : ((c == cc_pipe()) ? ((nc == cc_minus()) ? new LexToken(make_token(new Turnstile(), "|-", s), advance_char(next)) : new LexToken(make_token(new Pipe(), "|", s), next)) : ((c == cc_less()) ? ((nc == cc_equals()) ? new LexToken(make_token(new LessOrEqual(), "<=", s), advance_char(next)) : ((nc == cc_minus()) ? new LexToken(make_token(new LeftArrow(), "<-", s), advance_char(next)) : new LexToken(make_token(new LessThan(), "<", s), next))) : ((c == cc_greater()) ? ((nc == cc_equals()) ? new LexToken(make_token(new GreaterOrEqual(), ">=", s), advance_char(next)) : new LexToken(make_token(new GreaterThan(), ">", s), next)) : new LexToken(make_token(new ErrorToken(), ((char)((long)s.source[(int)s.offset])).ToString(), s), next))))))))))))((is_at_end(next) ? 0 : peek_code(next)))))(advance_char(s))))(peek_code(s));

    public static LexResult scan_char_literal(LexState s) => ((Func<LexState, LexResult>)((s1) => (is_at_end(s1) ? new LexToken(make_token(new ErrorToken(), "'", s), s1) : ((peek_code(s1) == cc_backslash()) ? ((Func<LexState, LexResult>)((s2) => (is_at_end(s2) ? new LexToken(make_token(new ErrorToken(), "'\\", s), s2) : ((Func<long, LexResult>)((esc_code) => ((Func<long, LexResult>)((char_val) => ((Func<LexState, LexResult>)((s3) => ((Func<LexState, LexResult>)((s4) => new LexToken(make_token(new CharLiteral(), _Cce.FromUnicode(char_val.ToString()), s), s4)))((is_at_end(s3) ? s3 : ((peek_code(s3) == cc_single_quote()) ? advance_char(s3) : s3)))))(advance_char(s2))))(((esc_code == cc_lower_n()) ? 10 : ((esc_code == cc_lower_t()) ? 32 : ((esc_code == cc_lower_r()) ? 10 : ((esc_code == cc_backslash()) ? cc_backslash() : ((esc_code == cc_single_quote()) ? cc_single_quote() : esc_code))))))))(peek_code(s2)))))(advance_char(s1)) : ((Func<long, LexResult>)((char_val) => ((Func<LexState, LexResult>)((s2) => ((Func<LexState, LexResult>)((s3) => new LexToken(make_token(new CharLiteral(), _Cce.FromUnicode(char_val.ToString()), s), s3)))((is_at_end(s2) ? s2 : ((peek_code(s2) == cc_single_quote()) ? advance_char(s2) : s2)))))(advance_char(s1))))(peek_code(s1))))))(advance_char(s));

    public static List<Token> tokenize_loop(LexState st, List<Token> acc)
    {
        while (true)
        {
            var _tco_s = scan_token(st);
            if (_tco_s is LexToken _tco_m0)
            {
                var tok = _tco_m0.Field0;
                var next = _tco_m0.Field1;
            if ((tok.kind == new EndOfFile()))
            {
            return ((Func<List<Token>>)(() => { var _l = acc; _l.Add(tok); return _l; }))();
            }
            else
            {
            var _tco_0 = next;
            var _tco_1 = ((Func<List<Token>>)(() => { var _l = acc; _l.Add(tok); return _l; }))();
            st = _tco_0;
            acc = _tco_1;
            continue;
            }
            }
            else if (_tco_s is LexEnd _tco_m1)
            {
            return ((Func<List<Token>>)(() => { var _l = acc; _l.Add(make_token(new EndOfFile(), "", st)); return _l; }))();
            }
        }
    }

    public static List<Token> tokenize(string src) => tokenize_loop(make_lex_state(src), new List<Token>());

    public static ParseTypeResult parse_type(ParseState st) => ((Func<ParseTypeResult, ParseTypeResult>)((result) => unwrap_type_ok(result, (_p0_) => (_p1_) => parse_type_continue(_p0_, _p1_))))(parse_type_atom(st));

    public static ParseTypeResult parse_type_continue(TypeExpr left, ParseState st) => (is_arrow(current_kind(st)) ? ((Func<ParseState, ParseTypeResult>)((st2) => ((Func<ParseTypeResult, ParseTypeResult>)((right_result) => unwrap_type_ok(right_result, (_p0_) => (_p1_) => make_fun_type(left, _p0_, _p1_))))(parse_type(st2))))(advance(st)) : new TypeOk(left, st));

    public static ParseTypeResult make_fun_type(TypeExpr left, TypeExpr right, ParseState st) => new TypeOk(new FunType(left, right), st);

    public static ParseTypeResult unwrap_type_ok(ParseTypeResult r, Func<TypeExpr, Func<ParseState, ParseTypeResult>> f) => r switch { TypeOk(var t, var st) => f(t)(st), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static ParseTypeResult parse_type_atom(ParseState st) => (is_ident(current_kind(st)) ? ((Func<Token, ParseTypeResult>)((tok) => parse_type_args(new NamedType(tok), advance(st))))(current(st)) : (is_type_ident(current_kind(st)) ? ((Func<Token, ParseTypeResult>)((tok) => parse_type_args(new NamedType(tok), advance(st))))(current(st)) : (is_left_paren(current_kind(st)) ? parse_paren_type(advance(st)) : (is_left_bracket(current_kind(st)) ? parse_effect_type(advance(st)) : ((Func<Token, ParseTypeResult>)((tok) => new TypeOk(new NamedType(tok), advance(st))))(current(st))))));

    public static ParseTypeResult parse_paren_type(ParseState st) => ((Func<ParseTypeResult, ParseTypeResult>)((inner) => unwrap_type_ok(inner, (_p0_) => (_p1_) => finish_paren_type(_p0_, _p1_))))(parse_type(st));

    public static ParseTypeResult finish_paren_type(TypeExpr t, ParseState st) => ((Func<ParseState, ParseTypeResult>)((st2) => new TypeOk(new ParenType(t), st2)))(expect(new RightParen(), st));

    public static ParseTypeResult parse_effect_type(ParseState st) => ((Func<ParseState, ParseTypeResult>)((st2) => parse_type(st2)))(skip_effect_contents(st));

    public static ParseState skip_effect_contents(ParseState st)
    {
        while (true)
        {
            if (is_done(st))
            {
            return st;
            }
            else
            {
            if (is_right_bracket(current_kind(st)))
            {
            return advance(st);
            }
            else
            {
            var _tco_0 = advance(st);
            st = _tco_0;
            continue;
            }
            }
        }
    }

    public static ParseTypeResult parse_type_args(TypeExpr base_type, ParseState st) => (is_done(st) ? new TypeOk(base_type, st) : (is_type_arg_start(current_kind(st)) ? parse_type_arg_next(base_type, st) : new TypeOk(base_type, st)));

    public static ParseTypeResult parse_type_arg_next(TypeExpr base_type, ParseState st) => ((Func<ParseTypeResult, ParseTypeResult>)((arg_result) => unwrap_type_ok(arg_result, (_p0_) => (_p1_) => continue_type_args(base_type, _p0_, _p1_))))(parse_type_atom(st));

    public static ParseTypeResult continue_type_args(TypeExpr base_type, TypeExpr arg, ParseState st) => parse_type_args(new AppType(base_type, new List<TypeExpr> { arg }), st);

    public static ParsePatResult parse_pattern(ParseState st) => (is_underscore(current_kind(st)) ? new PatOk(new WildPat(current(st)), advance(st)) : (is_literal(current_kind(st)) ? new PatOk(new LitPat(current(st)), advance(st)) : (is_type_ident(current_kind(st)) ? ((Func<Token, ParsePatResult>)((tok) => parse_ctor_pattern_fields(tok, new List<Pat>(), advance(st))))(current(st)) : (is_ident(current_kind(st)) ? new PatOk(new VarPat(current(st)), advance(st)) : new PatOk(new WildPat(current(st)), advance(st))))));

    public static ParsePatResult parse_ctor_pattern_fields(Token ctor, List<Pat> acc, ParseState st) => (is_left_paren(current_kind(st)) ? ((Func<ParseState, ParsePatResult>)((st2) => ((Func<ParsePatResult, ParsePatResult>)((sub) => unwrap_pat_ok(sub, (_p0_) => (_p1_) => continue_ctor_fields(ctor, acc, _p0_, _p1_))))(parse_pattern(st2))))(advance(st)) : new PatOk(new CtorPat(ctor, acc), st));

    public static ParsePatResult continue_ctor_fields(Token ctor, List<Pat> acc, Pat p, ParseState st) => ((Func<ParseState, ParsePatResult>)((st2) => parse_ctor_pattern_fields(ctor, ((Func<List<Pat>>)(() => { var _l = acc; _l.Add(p); return _l; }))(), st2)))(expect(new RightParen(), st));

    public static ParsePatResult unwrap_pat_ok(ParsePatResult r, Func<Pat, Func<ParseState, ParsePatResult>> f) => r switch { PatOk(var p, var st) => f(p)(st), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static ParseTypeResult parse_type_annotation(ParseState st) => ((Func<ParseState, ParseTypeResult>)((st2) => ((Func<ParseState, ParseTypeResult>)((st3) => parse_type(st3)))(expect(new Colon(), st2))))(advance(st));

    public static ParseDefResult parse_definition(ParseState st) => (is_done(st) ? new DefNone(st) : (is_ident(current_kind(st)) ? try_parse_def(st) : (is_type_ident(current_kind(st)) ? try_parse_def(st) : new DefNone(st))));

    public static ParseDefResult try_parse_def(ParseState st) => (is_colon(peek_kind(st, 1)) ? ((Func<ParseTypeResult, ParseDefResult>)((ann_result) => unwrap_type_for_def(ann_result)))(parse_type_annotation(st)) : parse_def_body_with_ann(new List<TypeAnn>(), st));

    public static ParseDefResult unwrap_type_for_def(ParseTypeResult r) => r switch { TypeOk(var ann_type, var st) => ((Func<Token, ParseDefResult>)((name_tok) => ((Func<List<TypeAnn>, ParseDefResult>)((ann) => ((Func<ParseState, ParseDefResult>)((st2) => parse_def_body_with_ann(ann, st2)))(skip_newlines(st))))(new List<TypeAnn> { new TypeAnn(name: name_tok, type_expr: ann_type) })))(new Token(kind: new Identifier(), text: "", offset: 0, line: 0, column: 0)), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static ParseDefResult parse_def_body_with_ann(List<TypeAnn> ann, ParseState st) => ((Func<Token, ParseDefResult>)((name_tok) => ((Func<ParseState, ParseDefResult>)((st2) => parse_def_params_then(ann, name_tok, new List<Token>(), st2)))(advance(st))))(current(st));

    public static ParseDefResult parse_def_params_then(List<TypeAnn> ann, Token name_tok, List<Token> acc, ParseState st)
    {
        while (true)
        {
            if (is_left_paren(current_kind(st)))
            {
            var st2 = advance(st);
            if (is_ident(current_kind(st2)))
            {
            var param = current(st2);
            var st3 = advance(st2);
            var st4 = expect(new RightParen(), st3);
            var _tco_0 = ann;
            var _tco_1 = name_tok;
            var _tco_2 = ((Func<List<Token>>)(() => { var _l = acc; _l.Add(param); return _l; }))();
            var _tco_3 = st4;
            ann = _tco_0;
            name_tok = _tco_1;
            acc = _tco_2;
            st = _tco_3;
            continue;
            }
            else
            {
            return finish_def(ann, name_tok, acc, st);
            }
            }
            else
            {
            return finish_def(ann, name_tok, acc, st);
            }
        }
    }

    public static ParseDefResult finish_def(List<TypeAnn> ann, Token name_tok, List<Token> @params, ParseState st) => ((Func<ParseState, ParseDefResult>)((st2) => ((Func<ParseState, ParseDefResult>)((st3) => ((Func<ParseExprResult, ParseDefResult>)((body_result) => unwrap_def_body(body_result, ann, name_tok, @params)))(parse_expr(st3))))(skip_newlines(st2))))(expect(new Equals_(), st));

    public static ParseDefResult unwrap_def_body(ParseExprResult r, List<TypeAnn> ann, Token name_tok, List<Token> @params) => r switch { ExprOk(var b, var st) => new DefOk(new Def(name: name_tok, @params: @params, ann: ann, body: b), st), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static bool is_paren_type_param(ParseState st) => (is_left_paren(current_kind(st)) ? ((Func<TokenKind, bool>)((k1) => (is_ident(k1) ? is_right_paren(peek_kind(st, 2)) : (is_type_ident(k1) ? is_right_paren(peek_kind(st, 2)) : false))))(peek_kind(st, 1)) : false);

    public static bool is_type_param_pattern(ParseState st) => (is_paren_type_param(st) ? true : is_ident(current_kind(st)));

    public static ParseState parse_type_params(ParseState st, List<Token> acc)
    {
        while (true)
        {
            if (is_paren_type_param(st))
            {
            var _tco_0 = advance(advance(advance(st)));
            var _tco_1 = ((Func<List<Token>>)(() => { var _l = acc; _l.Add(st.tokens[(int)(st.pos + 1)]); return _l; }))();
            st = _tco_0;
            acc = _tco_1;
            continue;
            }
            else
            {
            if (is_ident(current_kind(st)))
            {
            var _tco_0 = advance(st);
            var _tco_1 = ((Func<List<Token>>)(() => { var _l = acc; _l.Add(current(st)); return _l; }))();
            st = _tco_0;
            acc = _tco_1;
            continue;
            }
            else
            {
            return st;
            }
            }
        }
    }

    public static List<Token> collect_type_params(ParseState st, List<Token> acc)
    {
        while (true)
        {
            if (is_paren_type_param(st))
            {
            var _tco_0 = advance(advance(advance(st)));
            var _tco_1 = ((Func<List<Token>>)(() => { var _l = acc; _l.Add(st.tokens[(int)(st.pos + 1)]); return _l; }))();
            st = _tco_0;
            acc = _tco_1;
            continue;
            }
            else
            {
            if (is_ident(current_kind(st)))
            {
            var _tco_0 = advance(st);
            var _tco_1 = ((Func<List<Token>>)(() => { var _l = acc; _l.Add(current(st)); return _l; }))();
            st = _tco_0;
            acc = _tco_1;
            continue;
            }
            else
            {
            return acc;
            }
            }
        }
    }

    public static ParseTypeDefResult parse_type_def(ParseState st) => (is_type_ident(current_kind(st)) ? ((Func<Token, ParseTypeDefResult>)((name_tok) => ((Func<ParseState, ParseTypeDefResult>)((st2) => ((Func<List<Token>, ParseTypeDefResult>)((tparams) => ((Func<ParseState, ParseTypeDefResult>)((st3) => (is_equals(current_kind(st3)) ? ((Func<ParseState, ParseTypeDefResult>)((st4) => (is_record_keyword(current_kind(st4)) ? parse_record_type(name_tok, tparams, st4) : (is_pipe(current_kind(st4)) ? parse_variant_type(name_tok, tparams, st4) : ((is_type_ident(current_kind(st4)) && looks_like_variant(st4)) ? parse_variant_type(name_tok, tparams, st4) : new TypeDefNone(st))))))(skip_newlines(advance(st3))) : new TypeDefNone(st))))(parse_type_params(st2, new List<Token>()))))(collect_type_params(st2, new List<Token>()))))(advance(st))))(current(st)) : new TypeDefNone(st));

    public static ParseTypeDefResult parse_record_type(Token name_tok, List<Token> tparams, ParseState st) => ((Func<ParseState, ParseTypeDefResult>)((st2) => ((Func<ParseState, ParseTypeDefResult>)((st3) => ((Func<ParseState, ParseTypeDefResult>)((st4) => parse_record_fields_loop(name_tok, tparams, new List<RecordFieldDef>(), st4)))(skip_newlines(st3))))(expect(new LeftBrace(), st2))))(advance(st));

    public static ParseTypeDefResult parse_record_fields_loop(Token name_tok, List<Token> tparams, List<RecordFieldDef> acc, ParseState st) => (is_right_brace(current_kind(st)) ? new TypeDefOk(new TypeDef(name: name_tok, type_params: tparams, body: new RecordBody(acc)), advance(st)) : (is_ident(current_kind(st)) ? parse_one_record_field(name_tok, tparams, acc, st) : new TypeDefOk(new TypeDef(name: name_tok, type_params: tparams, body: new RecordBody(acc)), st)));

    public static ParseTypeDefResult parse_one_record_field(Token name_tok, List<Token> tparams, List<RecordFieldDef> acc, ParseState st) => ((Func<Token, ParseTypeDefResult>)((field_name) => ((Func<ParseState, ParseTypeDefResult>)((st2) => ((Func<ParseState, ParseTypeDefResult>)((st3) => ((Func<ParseTypeResult, ParseTypeDefResult>)((field_type_result) => unwrap_record_field_type(name_tok, tparams, acc, field_name, field_type_result)))(parse_type(st3))))(expect(new Colon(), st2))))(advance(st))))(current(st));

    public static ParseTypeDefResult unwrap_record_field_type(Token name_tok, List<Token> tparams, List<RecordFieldDef> acc, Token field_name, ParseTypeResult r) => r switch { TypeOk(var ft, var st) => ((Func<RecordFieldDef, ParseTypeDefResult>)((field) => ((Func<ParseState, ParseTypeDefResult>)((st2) => (is_comma(current_kind(st2)) ? parse_record_fields_loop(name_tok, tparams, ((Func<List<RecordFieldDef>>)(() => { var _l = acc; _l.Add(field); return _l; }))(), skip_newlines(advance(st2))) : parse_record_fields_loop(name_tok, tparams, ((Func<List<RecordFieldDef>>)(() => { var _l = acc; _l.Add(field); return _l; }))(), st2))))(skip_newlines(st))))(new RecordFieldDef(name: field_name, type_expr: ft)), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static bool looks_like_variant(ParseState st) => looks_like_variant_scan(st.tokens, (st.pos + 1), ((long)st.tokens.Count));

    public static bool looks_like_variant_scan(List<Token> tokens, long i, long len)
    {
        while (true)
        {
            if ((i >= len))
            {
            return false;
            }
            else
            {
            var k = tokens[(int)i].kind;
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
            else if (_tco_s is EndOfFile _tco_m1)
            {
            return false;
            }
            {
            var _tco_0 = tokens;
            var _tco_1 = (i + 1);
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

    public static ParseTypeDefResult parse_variant_type(Token name_tok, List<Token> tparams, ParseState st) => (is_type_ident(current_kind(st)) ? ((Func<Token, ParseTypeDefResult>)((ctor_name) => ((Func<ParseState, ParseTypeDefResult>)((st2) => parse_ctor_fields(ctor_name, new List<TypeExpr>(), st2, name_tok, tparams, new List<VariantCtorDef>())))(advance(st))))(current(st)) : parse_variant_ctors(name_tok, tparams, new List<VariantCtorDef>(), st));

    public static ParseTypeDefResult parse_variant_ctors(Token name_tok, List<Token> tparams, List<VariantCtorDef> acc, ParseState st) => (is_pipe(current_kind(st)) ? ((Func<ParseState, ParseTypeDefResult>)((st2) => ((Func<Token, ParseTypeDefResult>)((ctor_name) => ((Func<ParseState, ParseTypeDefResult>)((st3) => parse_ctor_fields(ctor_name, new List<TypeExpr>(), st3, name_tok, tparams, acc)))(advance(st2))))(current(st2))))(skip_newlines(advance(st))) : new TypeDefOk(new TypeDef(name: name_tok, type_params: tparams, body: new VariantBody(acc)), st));

    public static ParseTypeDefResult parse_ctor_fields(Token ctor_name, List<TypeExpr> fields, ParseState st, Token name_tok, List<Token> tparams, List<VariantCtorDef> acc) => (is_left_paren(current_kind(st)) ? ((Func<ParseTypeResult, ParseTypeDefResult>)((field_result) => unwrap_ctor_field(field_result, ctor_name, fields, name_tok, tparams, acc)))(parse_type(advance(st))) : ((Func<ParseState, ParseTypeDefResult>)((st2) => ((Func<VariantCtorDef, ParseTypeDefResult>)((ctor) => parse_variant_ctors(name_tok, tparams, ((Func<List<VariantCtorDef>>)(() => { var _l = acc; _l.Add(ctor); return _l; }))(), st2)))(new VariantCtorDef(name: ctor_name, fields: fields))))(skip_newlines(st)));

    public static ParseTypeDefResult unwrap_ctor_field(ParseTypeResult r, Token ctor_name, List<TypeExpr> fields, Token name_tok, List<Token> tparams, List<VariantCtorDef> acc) => r switch { TypeOk(var ty, var st) => ((Func<ParseState, ParseTypeDefResult>)((st2) => parse_ctor_fields(ctor_name, Enumerable.Concat(fields, new List<TypeExpr> { ty }).ToList(), st2, name_tok, tparams, acc)))(expect(new RightParen(), st)), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static Document parse_document(ParseState st) => ((Func<ParseState, Document>)((st2) => ((Func<ImportParseResult, Document>)((imp_result) => parse_top_level(new List<Def>(), new List<TypeDef>(), new List<EffectDef>(), imp_result.imports, imp_result.state)))(parse_imports(st2, new List<ImportDecl>()))))(skip_newlines(st));

    public static ImportParseResult parse_imports(ParseState st, List<ImportDecl> acc)
    {
        while (true)
        {
            if (is_import_keyword(current_kind(st)))
            {
            var st2 = advance(st);
            var name_tok = current(st2);
            var st3 = skip_newlines(advance(st2));
            var _tco_0 = st3;
            var _tco_1 = ((Func<List<ImportDecl>>)(() => { var _l = acc; _l.Add(new ImportDecl(module_name: name_tok)); return _l; }))();
            st = _tco_0;
            acc = _tco_1;
            continue;
            }
            else
            {
            return new ImportParseResult(imports: acc, state: st);
            }
        }
    }

    public static Document parse_top_level(List<Def> defs, List<TypeDef> type_defs, List<EffectDef> effect_defs, List<ImportDecl> imports, ParseState st) => (is_done(st) ? new Document(defs: defs, type_defs: type_defs, effect_defs: effect_defs, imports: imports) : (is_effect_keyword(current_kind(st)) ? parse_top_level_effect(defs, type_defs, effect_defs, imports, st) : try_top_level_type_def(defs, type_defs, effect_defs, imports, st)));

    public static Document parse_top_level_effect(List<Def> defs, List<TypeDef> type_defs, List<EffectDef> effect_defs, List<ImportDecl> imports, ParseState st) => ((Func<ParseState, Document>)((st1) => ((Func<Token, Document>)((name_tok) => ((Func<ParseState, Document>)((st2) => ((Func<ParseState, Document>)((st3) => ((Func<EffectOpsResult, Document>)((ops) => ((Func<EffectDef, Document>)((ed) => parse_top_level(defs, type_defs, Enumerable.Concat(effect_defs, new List<EffectDef> { ed }).ToList(), imports, skip_newlines(ops.state))))(new EffectDef(name: name_tok, ops: ops.ops))))(parse_effect_ops(st3, new List<EffectOpDef>()))))((is_where_keyword(current_kind(st2)) ? skip_newlines(advance(st2)) : st2))))(advance(st1))))(current(st1))))(advance(st));

    public static EffectOpsResult parse_effect_ops(ParseState st, List<EffectOpDef> acc)
    {
        while (true)
        {
            if (is_ident(current_kind(st)))
            {
            if (is_colon(peek_kind(st, 1)))
            {
            var op_tok = current(st);
            var st2 = advance(advance(st));
            var type_result = parse_type(st2);
            var _tco_s = type_result;
            if (_tco_s is TypeOk _tco_m0)
            {
                var ty = _tco_m0.Field0;
                var st3 = _tco_m0.Field1;
            var op = new EffectOpDef(name: op_tok, type_expr: ty);
            var _tco_0 = skip_newlines(st3);
            var _tco_1 = ((Func<List<EffectOpDef>>)(() => { var _l = acc; _l.Add(op); return _l; }))();
            st = _tco_0;
            acc = _tco_1;
            continue;
            }
            }
            else
            {
            return new EffectOpsResult(ops: acc, state: st);
            }
            }
            else
            {
            return new EffectOpsResult(ops: acc, state: st);
            }
        }
    }

    public static Document try_top_level_type_def(List<Def> defs, List<TypeDef> type_defs, List<EffectDef> effect_defs, List<ImportDecl> imports, ParseState st) => ((Func<ParseTypeDefResult, Document>)((td_result) => td_result switch { TypeDefOk(var td, var st2) => parse_top_level(defs, Enumerable.Concat(type_defs, new List<TypeDef> { td }).ToList(), effect_defs, imports, skip_newlines(st2)), TypeDefNone(var st2) => try_top_level_def(defs, type_defs, effect_defs, imports, st), _ => throw new InvalidOperationException("Non-exhaustive match"), }))(parse_type_def(st));

    public static Document try_top_level_def(List<Def> defs, List<TypeDef> type_defs, List<EffectDef> effect_defs, List<ImportDecl> imports, ParseState st) => ((Func<ParseDefResult, Document>)((def_result) => def_result switch { DefOk(var d, var st2) => parse_top_level(Enumerable.Concat(defs, new List<Def> { d }).ToList(), type_defs, effect_defs, imports, skip_newlines(st2)), DefNone(var st2) => parse_top_level(defs, type_defs, effect_defs, imports, skip_newlines(advance(st2))), _ => throw new InvalidOperationException("Non-exhaustive match"), }))(parse_definition(st));

    public static ParseState make_parse_state(List<Token> toks) => new ParseState(tokens: toks, pos: 0);

    public static Token current(ParseState st) => st.tokens[(int)st.pos];

    public static TokenKind current_kind(ParseState st) => current(st).kind;

    public static ParseState advance(ParseState st) => ((st.pos >= (((long)st.tokens.Count) - 1)) ? st : new ParseState(tokens: st.tokens, pos: (st.pos + 1)));

    public static bool is_done(ParseState st) => current_kind(st) switch { EndOfFile { } => true, _ => false, };

    public static TokenKind peek_kind(ParseState st, long offset) => ((Func<long, TokenKind>)((idx) => ((idx >= ((long)st.tokens.Count)) ? new EndOfFile() : st.tokens[(int)idx].kind)))((st.pos + offset));

    public static bool is_ident(TokenKind k) => k switch { Identifier { } => true, _ => false, };

    public static bool is_type_ident(TokenKind k) => k switch { TypeIdentifier { } => true, _ => false, };

    public static bool is_arrow(TokenKind k) => k switch { Arrow { } => true, _ => false, };

    public static bool is_equals(TokenKind k) => k switch { Equals_ { } => true, _ => false, };

    public static bool is_colon(TokenKind k) => k switch { Colon { } => true, _ => false, };

    public static bool is_comma(TokenKind k) => k switch { Comma { } => true, _ => false, };

    public static bool is_pipe(TokenKind k) => k switch { Pipe { } => true, _ => false, };

    public static bool is_dot(TokenKind k) => k switch { Dot { } => true, _ => false, };

    public static bool is_left_paren(TokenKind k) => k switch { LeftParen { } => true, _ => false, };

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

    public static bool is_effect_keyword(TokenKind k) => k switch { EffectKeyword { } => true, _ => false, };

    public static bool is_import_keyword(TokenKind k) => k switch { ImportKeyword { } => true, _ => false, };

    public static bool is_where_keyword(TokenKind k) => k switch { WhereKeyword { } => true, _ => false, };

    public static bool is_in_keyword(TokenKind k) => k switch { InKeyword { } => true, _ => false, };

    public static bool is_minus(TokenKind k) => k switch { Minus { } => true, _ => false, };

    public static bool is_dedent(TokenKind k) => k switch { Dedent { } => true, _ => false, };

    public static bool is_left_arrow(TokenKind k) => k switch { LeftArrow { } => true, _ => false, };

    public static bool is_record_keyword(TokenKind k) => k switch { RecordKeyword { } => true, _ => false, };

    public static bool is_underscore(TokenKind k) => k switch { Underscore { } => true, _ => false, };

    public static bool is_backslash(TokenKind k) => k switch { Backslash { } => true, _ => false, };

    public static bool is_literal(TokenKind k) => k switch { IntegerLiteral { } => true, NumberLiteral { } => true, TextLiteral { } => true, CharLiteral { } => true, TrueKeyword { } => true, FalseKeyword { } => true, _ => false, };

    public static bool is_app_start(TokenKind k) => k switch { Identifier { } => true, TypeIdentifier { } => true, IntegerLiteral { } => true, NumberLiteral { } => true, TextLiteral { } => true, CharLiteral { } => true, TrueKeyword { } => true, FalseKeyword { } => true, LeftParen { } => true, LeftBracket { } => true, _ => false, };

    public static bool is_compound(Expr e) => e switch { MatchExpr(var s, var arms) => true, IfExpr(var c, var t, var el) => true, LetExpr(var binds, var body) => true, DoExpr(var stmts) => true, _ => false, };

    public static bool is_type_arg_start(TokenKind k) => k switch { TypeIdentifier { } => true, Identifier { } => true, LeftParen { } => true, _ => false, };

    public static long operator_precedence(TokenKind k) => k switch { PlusPlus { } => 5, ColonColon { } => 5, Plus { } => 6, Minus { } => 6, Star { } => 7, Slash { } => 7, Caret { } => 8, DoubleEquals { } => 4, NotEquals { } => 4, LessThan { } => 4, GreaterThan { } => 4, LessOrEqual { } => 4, GreaterOrEqual { } => 4, TripleEquals { } => 4, Ampersand { } => 3, Pipe { } => 2, _ => (0 - 1), };

    public static bool is_right_assoc(TokenKind k) => k switch { PlusPlus { } => true, ColonColon { } => true, Caret { } => true, Arrow { } => true, _ => false, };

    public static ParseState expect(TokenKind kind, ParseState st) => (is_done(st) ? st : advance(st));

    public static long skip_newlines_pos(List<Token> tokens, long pos, long len)
    {
        while (true)
        {
            if ((pos >= (len - 1)))
            {
            return pos;
            }
            else
            {
            var kind = tokens[(int)pos].kind;
            var _tco_s = kind;
            if (_tco_s is Newline _tco_m0)
            {
            var _tco_0 = tokens;
            var _tco_1 = (pos + 1);
            var _tco_2 = len;
            tokens = _tco_0;
            pos = _tco_1;
            len = _tco_2;
            continue;
            }
            else if (_tco_s is Indent _tco_m1)
            {
            var _tco_0 = tokens;
            var _tco_1 = (pos + 1);
            var _tco_2 = len;
            tokens = _tco_0;
            pos = _tco_1;
            len = _tco_2;
            continue;
            }
            else if (_tco_s is Dedent _tco_m2)
            {
            var _tco_0 = tokens;
            var _tco_1 = (pos + 1);
            var _tco_2 = len;
            tokens = _tco_0;
            pos = _tco_1;
            len = _tco_2;
            continue;
            }
            {
            return pos;
            }
            }
        }
    }

    public static ParseState skip_newlines(ParseState st) => ((Func<long, ParseState>)((end_pos) => ((end_pos == st.pos) ? st : new ParseState(tokens: st.tokens, pos: end_pos))))(skip_newlines_pos(st.tokens, st.pos, ((long)st.tokens.Count)));

    public static ParseExprResult parse_expr(ParseState st) => parse_binary(st, 0);

    public static ParseExprResult unwrap_expr_ok(ParseExprResult r, Func<Expr, Func<ParseState, ParseExprResult>> f) => r switch { ExprOk(var e, var st) => f(e)(st), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static ParseExprResult parse_binary(ParseState st, long min_prec) => ((Func<ParseExprResult, ParseExprResult>)((left_result) => unwrap_expr_ok(left_result, (_p0_) => (_p1_) => start_binary_loop(min_prec, _p0_, _p1_))))(parse_unary(st));

    public static ParseExprResult start_binary_loop(long min_prec, Expr left, ParseState st) => parse_binary_loop(left, st, min_prec);

    public static ParseExprResult parse_binary_loop(Expr left, ParseState st, long min_prec) => (is_done(st) ? new ExprOk(left, st) : ((Func<long, ParseExprResult>)((prec) => ((prec < min_prec) ? new ExprOk(left, st) : ((Func<Token, ParseExprResult>)((op) => ((Func<ParseState, ParseExprResult>)((st2) => ((Func<long, ParseExprResult>)((next_min) => ((Func<ParseExprResult, ParseExprResult>)((right_result) => unwrap_expr_ok(right_result, (_p0_) => (_p1_) => continue_binary(left, op, min_prec, _p0_, _p1_))))(parse_binary(st2, next_min))))((is_right_assoc(op.kind) ? prec : (prec + 1)))))(skip_newlines(advance(st)))))(current(st)))))(operator_precedence(current_kind(st))));

    public static ParseExprResult continue_binary(Expr left, Token op, long min_prec, Expr right, ParseState st) => parse_binary_loop(new BinExpr(left, op, right), st, min_prec);

    public static ParseExprResult parse_unary(ParseState st) => (is_minus(current_kind(st)) ? ((Func<Token, ParseExprResult>)((op) => ((Func<ParseExprResult, ParseExprResult>)((result) => unwrap_expr_ok(result, (_p0_) => (_p1_) => finish_unary(op, _p0_, _p1_))))(parse_unary(advance(st)))))(current(st)) : parse_application(st));

    public static ParseExprResult finish_unary(Token op, Expr operand, ParseState st) => new ExprOk(new UnaryExpr(op, operand), st);

    public static ParseExprResult parse_application(ParseState st) => ((Func<ParseExprResult, ParseExprResult>)((func_result) => unwrap_expr_ok(func_result, (_p0_) => (_p1_) => parse_app_loop(_p0_, _p1_))))(parse_atom(st));

    public static ParseExprResult parse_app_loop(Expr func, ParseState st) => (is_compound(func) ? parse_dot_only(func, st) : (is_done(st) ? new ExprOk(func, st) : (is_app_start(current_kind(st)) ? ((Func<ParseExprResult, ParseExprResult>)((arg_result) => unwrap_expr_ok(arg_result, (_p0_) => (_p1_) => continue_app(func, _p0_, _p1_))))(parse_atom(st)) : parse_field_access(func, st))));

    public static ParseExprResult continue_app(Expr func, Expr arg, ParseState st) => parse_app_loop(new AppExpr(func, arg), st);

    public static ParseExprResult parse_atom(ParseState st) => (is_literal(current_kind(st)) ? new ExprOk(new LitExpr(current(st)), advance(st)) : (is_ident(current_kind(st)) ? parse_field_access(new NameExpr(current(st)), advance(st)) : (is_type_ident(current_kind(st)) ? parse_atom_type_ident(st) : (is_left_paren(current_kind(st)) ? parse_paren_expr(advance(st)) : (is_left_bracket(current_kind(st)) ? parse_list_expr(st) : (is_if_keyword(current_kind(st)) ? parse_if_expr(st) : (is_let_keyword(current_kind(st)) ? parse_let_expr(st) : (is_when_keyword(current_kind(st)) ? parse_match_expr(st) : (is_do_keyword(current_kind(st)) ? parse_do_expr(st) : (is_with_keyword(current_kind(st)) ? parse_handle_expr(st) : (is_backslash(current_kind(st)) ? parse_lambda_expr(st) : new ExprOk(new ErrExpr(current(st)), advance(st)))))))))))));

    public static ParseExprResult parse_field_access(Expr node, ParseState st)
    {
        while (true)
        {
            if (is_dot(current_kind(st)))
            {
            var st2 = advance(st);
            var field = current(st2);
            var st3 = advance(st2);
            var _tco_0 = new FieldExpr(node, field);
            var _tco_1 = st3;
            node = _tco_0;
            st = _tco_1;
            continue;
            }
            else
            {
            return new ExprOk(node, st);
            }
        }
    }

    public static ParseExprResult parse_dot_only(Expr node, ParseState st)
    {
        while (true)
        {
            if (is_dot(current_kind(st)))
            {
            var st2 = advance(st);
            var field = current(st2);
            var st3 = advance(st2);
            var _tco_0 = new FieldExpr(node, field);
            var _tco_1 = st3;
            node = _tco_0;
            st = _tco_1;
            continue;
            }
            else
            {
            return new ExprOk(node, st);
            }
        }
    }

    public static ParseExprResult parse_atom_type_ident(ParseState st) => ((Func<Token, ParseExprResult>)((tok) => ((Func<ParseState, ParseExprResult>)((st2) => (is_left_brace(current_kind(st2)) ? parse_record_expr(tok, st2) : new ExprOk(new NameExpr(tok), st2))))(advance(st))))(current(st));

    public static ParseExprResult parse_paren_expr(ParseState st) => ((Func<ParseState, ParseExprResult>)((st2) => ((Func<ParseExprResult, ParseExprResult>)((inner) => unwrap_expr_ok(inner, (_p0_) => (_p1_) => finish_paren_expr(_p0_, _p1_))))(parse_expr(st2))))(skip_newlines(st));

    public static ParseExprResult finish_paren_expr(Expr e, ParseState st) => ((Func<ParseState, ParseExprResult>)((st2) => ((Func<ParseState, ParseExprResult>)((st3) => new ExprOk(new ParenExpr(e), st3)))(expect(new RightParen(), st2))))(skip_newlines(st));

    public static ParseExprResult parse_record_expr(Token type_name, ParseState st) => ((Func<ParseState, ParseExprResult>)((st2) => ((Func<ParseState, ParseExprResult>)((st3) => parse_record_expr_fields(type_name, new List<RecordFieldExpr>(), st3)))(skip_newlines(st2))))(advance(st));

    public static ParseExprResult parse_record_expr_fields(Token type_name, List<RecordFieldExpr> acc, ParseState st) => (is_right_brace(current_kind(st)) ? new ExprOk(new RecordExpr(type_name, acc), advance(st)) : (is_ident(current_kind(st)) ? parse_record_field(type_name, acc, st) : new ExprOk(new RecordExpr(type_name, acc), st)));

    public static ParseExprResult parse_record_field(Token type_name, List<RecordFieldExpr> acc, ParseState st) => ((Func<Token, ParseExprResult>)((field_name) => ((Func<ParseState, ParseExprResult>)((st2) => ((Func<ParseState, ParseExprResult>)((st3) => ((Func<ParseExprResult, ParseExprResult>)((val_result) => unwrap_expr_ok(val_result, (_p0_) => (_p1_) => finish_record_field(type_name, acc, field_name, _p0_, _p1_))))(parse_expr(st3))))(expect(new Equals_(), st2))))(advance(st))))(current(st));

    public static ParseExprResult finish_record_field(Token type_name, List<RecordFieldExpr> acc, Token field_name, Expr v, ParseState st) => ((Func<RecordFieldExpr, ParseExprResult>)((field) => ((Func<ParseState, ParseExprResult>)((st2) => (is_comma(current_kind(st2)) ? parse_record_expr_fields(type_name, ((Func<List<RecordFieldExpr>>)(() => { var _l = acc; _l.Add(field); return _l; }))(), skip_newlines(advance(st2))) : parse_record_expr_fields(type_name, ((Func<List<RecordFieldExpr>>)(() => { var _l = acc; _l.Add(field); return _l; }))(), st2))))(skip_newlines(st))))(new RecordFieldExpr(name: field_name, value: v));

    public static ParseExprResult parse_list_expr(ParseState st) => ((Func<ParseState, ParseExprResult>)((st2) => ((Func<ParseState, ParseExprResult>)((st3) => parse_list_elements(new List<Expr>(), st3)))(skip_newlines(st2))))(advance(st));

    public static ParseExprResult parse_list_elements(List<Expr> acc, ParseState st) => (is_right_bracket(current_kind(st)) ? new ExprOk(new ListExpr(acc), advance(st)) : ((Func<ParseExprResult, ParseExprResult>)((elem) => unwrap_expr_ok(elem, (_p0_) => (_p1_) => finish_list_element(acc, _p0_, _p1_))))(parse_expr(st)));

    public static ParseExprResult finish_list_element(List<Expr> acc, Expr e, ParseState st) => ((Func<ParseState, ParseExprResult>)((st2) => (is_comma(current_kind(st2)) ? parse_list_elements(((Func<List<Expr>>)(() => { var _l = acc; _l.Add(e); return _l; }))(), skip_newlines(advance(st2))) : parse_list_elements(((Func<List<Expr>>)(() => { var _l = acc; _l.Add(e); return _l; }))(), st2))))(skip_newlines(st));

    public static ParseExprResult parse_if_expr(ParseState st) => ((Func<ParseState, ParseExprResult>)((st2) => ((Func<ParseExprResult, ParseExprResult>)((cond) => unwrap_expr_ok(cond, (_p0_) => (_p1_) => parse_if_then(_p0_, _p1_))))(parse_expr(st2))))(skip_newlines(advance(st)));

    public static ParseExprResult parse_if_then(Expr c, ParseState st) => ((Func<ParseState, ParseExprResult>)((st2) => ((Func<ParseState, ParseExprResult>)((st3) => ((Func<ParseState, ParseExprResult>)((st4) => ((Func<ParseExprResult, ParseExprResult>)((then_result) => unwrap_expr_ok(then_result, (_p0_) => (_p1_) => parse_if_else(c, _p0_, _p1_))))(parse_expr(st4))))(skip_newlines(st3))))(expect(new ThenKeyword(), st2))))(skip_newlines(st));

    public static ParseExprResult parse_if_else(Expr c, Expr t, ParseState st) => ((Func<ParseState, ParseExprResult>)((st2) => ((Func<ParseState, ParseExprResult>)((st3) => ((Func<ParseState, ParseExprResult>)((st4) => ((Func<ParseExprResult, ParseExprResult>)((else_result) => unwrap_expr_ok(else_result, (_p0_) => (_p1_) => finish_if(c, t, _p0_, _p1_))))(parse_expr(st4))))(skip_newlines(st3))))(expect(new ElseKeyword(), st2))))(skip_newlines(st));

    public static ParseExprResult finish_if(Expr c, Expr t, Expr e, ParseState st) => new ExprOk(new IfExpr(c, t, e), st);

    public static ParseExprResult parse_let_expr(ParseState st) => ((Func<ParseState, ParseExprResult>)((st2) => parse_let_bindings(new List<LetBind>(), st2)))(skip_newlines(advance(st)));

    public static ParseExprResult parse_let_bindings(List<LetBind> acc, ParseState st) => (is_ident(current_kind(st)) ? parse_let_binding(acc, st) : (is_in_keyword(current_kind(st)) ? ((Func<ParseState, ParseExprResult>)((st2) => ((Func<ParseExprResult, ParseExprResult>)((body) => unwrap_expr_ok(body, (_p0_) => (_p1_) => finish_let(acc, _p0_, _p1_))))(parse_expr(st2))))(skip_newlines(advance(st))) : ((Func<ParseExprResult, ParseExprResult>)((body) => unwrap_expr_ok(body, (_p0_) => (_p1_) => finish_let(acc, _p0_, _p1_))))(parse_expr(st))));

    public static ParseExprResult finish_let(List<LetBind> acc, Expr b, ParseState st) => new ExprOk(new LetExpr(acc, b), st);

    public static ParseExprResult parse_let_binding(List<LetBind> acc, ParseState st) => ((Func<Token, ParseExprResult>)((name_tok) => ((Func<ParseState, ParseExprResult>)((st2) => ((Func<ParseState, ParseExprResult>)((st3) => ((Func<ParseExprResult, ParseExprResult>)((val_result) => unwrap_expr_ok(val_result, (_p0_) => (_p1_) => finish_let_binding(acc, name_tok, _p0_, _p1_))))(parse_expr(st3))))(expect(new Equals_(), st2))))(advance(st))))(current(st));

    public static ParseExprResult finish_let_binding(List<LetBind> acc, Token name_tok, Expr v, ParseState st) => ((Func<LetBind, ParseExprResult>)((binding) => ((Func<ParseState, ParseExprResult>)((st2) => (is_comma(current_kind(st2)) ? parse_let_bindings(((Func<List<LetBind>>)(() => { var _l = acc; _l.Add(binding); return _l; }))(), skip_newlines(advance(st2))) : parse_let_bindings(((Func<List<LetBind>>)(() => { var _l = acc; _l.Add(binding); return _l; }))(), st2))))(skip_newlines(st))))(new LetBind(name: name_tok, value: v));

    public static ParseExprResult parse_match_expr(ParseState st) => ((Func<ParseState, ParseExprResult>)((st2) => ((Func<ParseExprResult, ParseExprResult>)((scrut) => unwrap_expr_ok(scrut, (_p0_) => (_p1_) => start_match_branches(_p0_, _p1_))))(parse_expr(st2))))(advance(st));

    public static ParseExprResult start_match_branches(Expr s, ParseState st) => ((Func<ParseState, ParseExprResult>)((st2) => ((Func<Token, ParseExprResult>)((tok) => parse_match_branches(s, new List<MatchArm>(), tok.column, tok.line, st2)))(current(st2))))(skip_newlines(st));

    public static ParseExprResult parse_match_branches(Expr scrut, List<MatchArm> acc, long col, long ln, ParseState st) => (is_if_keyword(current_kind(st)) ? ((Func<Token, ParseExprResult>)((tok) => ((tok.line == ln) ? parse_one_match_branch(scrut, acc, col, ln, st) : ((tok.column == col) ? parse_one_match_branch(scrut, acc, col, ln, st) : new ExprOk(new MatchExpr(scrut, acc), st)))))(current(st)) : new ExprOk(new MatchExpr(scrut, acc), st));

    public static ParseExprResult unwrap_pat_for_expr(ParsePatResult r, Func<Pat, Func<ParseState, ParseExprResult>> f) => r switch { PatOk(var p, var st) => f(p)(st), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static ParseExprResult parse_one_match_branch(Expr scrut, List<MatchArm> acc, long col, long ln, ParseState st) => ((Func<ParseState, ParseExprResult>)((st2) => ((Func<ParsePatResult, ParseExprResult>)((pat) => unwrap_pat_for_expr(pat, (_p0_) => (_p1_) => parse_match_branch_body(scrut, acc, col, ln, _p0_, _p1_))))(parse_pattern(st2))))(advance(st));

    public static ParseExprResult parse_match_branch_body(Expr scrut, List<MatchArm> acc, long col, long ln, Pat p, ParseState st) => ((Func<ParseState, ParseExprResult>)((st2) => ((Func<ParseState, ParseExprResult>)((st3) => ((Func<ParseExprResult, ParseExprResult>)((body) => unwrap_expr_ok(body, (_p0_) => (_p1_) => finish_match_branch(scrut, acc, col, ln, p, _p0_, _p1_))))(parse_expr(st3))))(skip_newlines(st2))))(expect(new Arrow(), st));

    public static ParseExprResult finish_match_branch(Expr scrut, List<MatchArm> acc, long col, long ln, Pat p, Expr b, ParseState st) => ((Func<MatchArm, ParseExprResult>)((arm) => ((Func<ParseState, ParseExprResult>)((st2) => parse_match_branches(scrut, ((Func<List<MatchArm>>)(() => { var _l = acc; _l.Add(arm); return _l; }))(), col, ln, st2)))(skip_newlines(st))))(new MatchArm(pattern: p, body: b));

    public static ParseExprResult parse_do_expr(ParseState st) => ((Func<ParseState, ParseExprResult>)((st2) => parse_do_stmts(new List<DoStmt>(), st2)))(skip_newlines(advance(st)));

    public static ParseExprResult parse_do_stmts(List<DoStmt> acc, ParseState st) => (is_done(st) ? new ExprOk(new DoExpr(acc), st) : (is_dedent(current_kind(st)) ? new ExprOk(new DoExpr(acc), st) : (looks_like_top_level_def(st) ? new ExprOk(new DoExpr(acc), st) : (is_do_bind(st) ? parse_do_bind_stmt(acc, st) : parse_do_expr_stmt(acc, st)))));

    public static bool looks_like_top_level_def(ParseState st) => ((is_ident(current_kind(st)) || is_type_ident(current_kind(st))) ? is_colon(peek_kind(st, 1)) : false);

    public static bool is_do_bind(ParseState st) => (is_ident(current_kind(st)) ? is_left_arrow(peek_kind(st, 1)) : false);

    public static ParseExprResult parse_do_bind_stmt(List<DoStmt> acc, ParseState st) => ((Func<Token, ParseExprResult>)((name_tok) => ((Func<ParseState, ParseExprResult>)((st2) => ((Func<ParseExprResult, ParseExprResult>)((val_result) => unwrap_expr_ok(val_result, (_p0_) => (_p1_) => finish_do_bind(acc, name_tok, _p0_, _p1_))))(parse_expr(st2))))(advance(advance(st)))))(current(st));

    public static ParseExprResult finish_do_bind(List<DoStmt> acc, Token name_tok, Expr v, ParseState st) => ((Func<ParseState, ParseExprResult>)((st2) => parse_do_stmts(((Func<List<DoStmt>>)(() => { var _l = acc; _l.Add(new DoBindStmt(name_tok, v)); return _l; }))(), st2)))(skip_newlines(st));

    public static ParseExprResult parse_do_expr_stmt(List<DoStmt> acc, ParseState st) => ((Func<ParseExprResult, ParseExprResult>)((expr_result) => unwrap_expr_ok(expr_result, (_p0_) => (_p1_) => finish_do_expr(acc, _p0_, _p1_))))(parse_expr(st));

    public static ParseExprResult finish_do_expr(List<DoStmt> acc, Expr e, ParseState st) => ((Func<ParseState, ParseExprResult>)((st2) => parse_do_stmts(((Func<List<DoStmt>>)(() => { var _l = acc; _l.Add(new DoExprStmt(e)); return _l; }))(), st2)))(skip_newlines(st));

    public static ParseExprResult parse_handle_expr(ParseState st) => ((Func<ParseState, ParseExprResult>)((st1) => ((Func<Token, ParseExprResult>)((eff_tok) => ((Func<ParseState, ParseExprResult>)((st2) => ((Func<ParseExprResult, ParseExprResult>)((body_result) => unwrap_expr_ok(body_result, (_p0_) => (_p1_) => finish_handle_body(eff_tok, _p0_, _p1_))))(parse_expr(st2))))(advance(st1))))(current(st1))))(advance(st));

    public static ParseExprResult finish_handle_body(Token eff_tok, Expr body, ParseState st) => ((Func<ParseState, ParseExprResult>)((st2) => ((Func<HandleParseResult, ParseExprResult>)((clauses) => new ExprOk(new HandleExpr(eff_tok, body, clauses.clauses), clauses.state)))(parse_handle_clauses(st2, new List<HandleClause>()))))(skip_newlines(st));

    public static HandleParseResult parse_handle_clauses(ParseState st, List<HandleClause> acc) => (is_ident(current_kind(st)) ? ((Func<Token, HandleParseResult>)((op_tok) => ((Func<ParseState, HandleParseResult>)((st1) => ((Func<HandleParamsResult, HandleParseResult>)((@params) => ((((long)@params.toks.Count) > 0) ? ((Func<Token, HandleParseResult>)((resume_tok) => ((Func<ParseState, HandleParseResult>)((st5) => ((Func<ParseState, HandleParseResult>)((st6) => ((Func<ParseExprResult, HandleParseResult>)((body_result) => unwrap_handle_clause_body(op_tok, resume_tok, body_result, acc)))(parse_expr(st6))))(skip_newlines(st5))))(expect(new Equals_(), @params.state))))(@params.toks[(int)(((long)@params.toks.Count) - 1)]) : new HandleParseResult(clauses: acc, state: st))))(parse_handle_params(st1, new List<Token>()))))(advance(st))))(current(st)) : new HandleParseResult(clauses: acc, state: st));

    public static HandleParamsResult parse_handle_params(ParseState st, List<Token> acc)
    {
        while (true)
        {
            if (is_left_paren(current_kind(st)))
            {
            var st1 = advance(st);
            var tok = current(st1);
            var st2 = advance(st1);
            var st3 = expect(new RightParen(), st2);
            var _tco_0 = st3;
            var _tco_1 = ((Func<List<Token>>)(() => { var _l = acc; _l.Add(tok); return _l; }))();
            st = _tco_0;
            acc = _tco_1;
            continue;
            }
            else
            {
            return new HandleParamsResult(toks: acc, state: st);
            }
        }
    }

    public static HandleParseResult unwrap_handle_clause_body(Token op_tok, Token resume_tok, ParseExprResult result, List<HandleClause> acc) => result switch { ExprOk(var body, var st) => ((Func<HandleClause, HandleParseResult>)((clause) => ((Func<ParseState, HandleParseResult>)((st2) => parse_handle_clauses(st2, ((Func<List<HandleClause>>)(() => { var _l = acc; _l.Add(clause); return _l; }))())))(skip_newlines(st))))(new HandleClause(op_name: op_tok, resume_name: resume_tok, body: body)), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static ParseExprResult parse_lambda_expr(ParseState st) => ((Func<ParseState, ParseExprResult>)((st2) => ((Func<LambdaParamsResult, ParseExprResult>)((params_result) => ((Func<ParseState, ParseExprResult>)((st3) => ((Func<ParseState, ParseExprResult>)((st4) => ((Func<ParseExprResult, ParseExprResult>)((body) => unwrap_expr_ok(body, (_p0_) => (_p1_) => finish_lambda(params_result.toks, _p0_, _p1_))))(parse_expr(st4))))(skip_newlines(st3))))(expect(new Arrow(), params_result.state))))(collect_lambda_params(st2, new List<Token>()))))(advance(st));

    public static LambdaParamsResult collect_lambda_params(ParseState st, List<Token> acc)
    {
        while (true)
        {
            if (is_ident(current_kind(st)))
            {
            var tok = current(st);
            var _tco_0 = advance(st);
            var _tco_1 = ((Func<List<Token>>)(() => { var _l = acc; _l.Add(tok); return _l; }))();
            st = _tco_0;
            acc = _tco_1;
            continue;
            }
            else
            {
            return new LambdaParamsResult(toks: acc, state: st);
            }
        }
    }

    public static ParseExprResult finish_lambda(List<Token> @params, Expr body, ParseState st) => new ExprOk(new LambdaExpr(@params, body), st);

    public static long token_length(Token t) => ((long)t.text.Length);

    public static CodexType resolve_type_expr(List<TypeBinding> tdm, ATypeExpr texpr)
    {
        while (true)
        {
            var _tco_s = texpr;
            if (_tco_s is ANamedType _tco_m0)
            {
                var name = _tco_m0.Field0;
            return resolve_type_name(tdm, name.value);
            }
            else if (_tco_s is AFunType _tco_m1)
            {
                var param = _tco_m1.Field0;
                var ret = _tco_m1.Field1;
            return new FunTy(resolve_type_expr(tdm, param), resolve_type_expr(tdm, ret));
            }
            else if (_tco_s is AAppType _tco_m2)
            {
                var ctor = _tco_m2.Field0;
                var args = _tco_m2.Field1;
            return resolve_applied_type(tdm, ctor, args);
            }
            else if (_tco_s is AEffectType _tco_m3)
            {
                var effs = _tco_m3.Field0;
                var ret = _tco_m3.Field1;
            var _tco_0 = tdm;
            var _tco_1 = ret;
            tdm = _tco_0;
            texpr = _tco_1;
            continue;
            }
        }
    }

    public static CodexType resolve_applied_type(List<TypeBinding> tdm, ATypeExpr ctor, List<ATypeExpr> args) => ctor switch { ANamedType(var name) => ((name.value == "List") ? ((((long)args.Count) == 1) ? new ListTy(resolve_type_expr(tdm, args[(int)0])) : new ListTy(new ErrorTy())) : new ConstructedTy(name, resolve_type_expr_list(tdm, args, 0, ((long)args.Count), new List<CodexType>()))), _ => resolve_type_expr(tdm, ctor), };

    public static List<CodexType> resolve_type_expr_list(List<TypeBinding> tdm, List<ATypeExpr> args, long i, long len, List<CodexType> acc)
    {
        while (true)
        {
            if ((i == len))
            {
            return acc;
            }
            else
            {
            var _tco_0 = tdm;
            var _tco_1 = args;
            var _tco_2 = (i + 1);
            var _tco_3 = len;
            var _tco_4 = ((Func<List<CodexType>>)(() => { var _l = acc; _l.Add(resolve_type_expr(tdm, args[(int)i])); return _l; }))();
            tdm = _tco_0;
            args = _tco_1;
            i = _tco_2;
            len = _tco_3;
            acc = _tco_4;
            continue;
            }
        }
    }

    public static CodexType resolve_type_name(List<TypeBinding> tdm, string name) => ((name == "Integer") ? new IntegerTy() : ((name == "Number") ? new NumberTy() : ((name == "Text") ? new TextTy() : ((name == "Boolean") ? new BooleanTy() : ((name == "Char") ? new CharTy() : ((name == "Nothing") ? new NothingTy() : lookup_type_def(tdm, name)))))));

    public static CodexType lookup_type_def(List<TypeBinding> tdm, string name) => ((Func<long, CodexType>)((len) => ((len == 0) ? new ConstructedTy(new Name(value: name), new List<CodexType>()) : ((Func<long, CodexType>)((pos) => ((pos >= len) ? new ConstructedTy(new Name(value: name), new List<CodexType>()) : ((Func<TypeBinding, CodexType>)((b) => ((b.name == name) ? b.bound_type : new ConstructedTy(new Name(value: name), new List<CodexType>()))))(tdm[(int)pos]))))(bsearch_text_pos(tdm, name, 0, len)))))(((long)tdm.Count));

    public static bool is_value_name(string name) => ((((long)name.Length) == 0) ? false : ((Func<long, bool>)((code) => ((code >= 13) && (code <= 38))))(((long)name[(int)0])));

    public static ParamResult parameterize_type(UnificationState st, CodexType ty) => ((Func<WalkResult, ParamResult>)((r) => ((Func<CodexType, ParamResult>)((wrapped) => new ParamResult(parameterized: wrapped, entries: r.entries, state: r.state)))(wrap_forall_from_entries(r.walked, r.entries, 0, ((long)r.entries.Count)))))(parameterize_walk(st, new List<ParamEntry>(), ty));

    public static CodexType wrap_forall_from_entries(CodexType ty, List<ParamEntry> entries, long i, long len) => ((i == len) ? ty : ((Func<ParamEntry, CodexType>)((e) => new ForAllTy(e.var_id, wrap_forall_from_entries(ty, entries, (i + 1), len))))(entries[(int)i]));

    public static WalkResult parameterize_walk(UnificationState st, List<ParamEntry> entries, CodexType ty) => ty switch { ConstructedTy(var name, var args) => (((((long)args.Count) == 0) && is_value_name(name.value)) ? ((Func<long, WalkResult>)((looked) => ((looked >= 0) ? new WalkResult(walked: new TypeVar(looked), entries: entries, state: st) : ((Func<FreshResult, WalkResult>)((fr) => fr.var_type switch { TypeVar(var new_id) => ((Func<ParamEntry, WalkResult>)((new_entry) => new WalkResult(walked: fr.var_type, entries: Enumerable.Concat(entries, new List<ParamEntry> { new_entry }).ToList(), state: fr.state)))(new ParamEntry(param_name: name.value, var_id: new_id)), _ => new WalkResult(walked: ty, entries: entries, state: fr.state), }))(fresh_and_advance(st)))))(find_param_entry(entries, name.value, 0, ((long)entries.Count))) : ((Func<WalkListResult, WalkResult>)((args_r) => new WalkResult(walked: new ConstructedTy(name, args_r.walked_list), entries: args_r.entries, state: args_r.state)))(parameterize_walk_list(st, entries, args, 0, ((long)args.Count), new List<CodexType>()))), FunTy(var param, var ret) => ((Func<WalkResult, WalkResult>)((pr) => ((Func<WalkResult, WalkResult>)((rr) => new WalkResult(walked: new FunTy(pr.walked, rr.walked), entries: rr.entries, state: rr.state)))(parameterize_walk(pr.state, pr.entries, ret))))(parameterize_walk(st, entries, param)), ListTy(var elem) => ((Func<WalkResult, WalkResult>)((er) => new WalkResult(walked: new ListTy(er.walked), entries: er.entries, state: er.state)))(parameterize_walk(st, entries, elem)), ForAllTy(var id, var body) => ((Func<WalkResult, WalkResult>)((br) => new WalkResult(walked: new ForAllTy(id, br.walked), entries: br.entries, state: br.state)))(parameterize_walk(st, entries, body)), _ => new WalkResult(walked: ty, entries: entries, state: st), };

    public static long find_param_entry(List<ParamEntry> entries, string name, long i, long len)
    {
        while (true)
        {
            if ((i == len))
            {
            return (0 - 1);
            }
            else
            {
            var e = entries[(int)i];
            if ((e.param_name == name))
            {
            return e.var_id;
            }
            else
            {
            var _tco_0 = entries;
            var _tco_1 = name;
            var _tco_2 = (i + 1);
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

    public static WalkListResult parameterize_walk_list(UnificationState st, List<ParamEntry> entries, List<CodexType> args, long i, long len, List<CodexType> acc)
    {
        while (true)
        {
            if ((i == len))
            {
            return new WalkListResult(walked_list: acc, entries: entries, state: st);
            }
            else
            {
            var r = parameterize_walk(st, entries, args[(int)i]);
            var _tco_0 = r.state;
            var _tco_1 = r.entries;
            var _tco_2 = args;
            var _tco_3 = (i + 1);
            var _tco_4 = len;
            var _tco_5 = ((Func<List<CodexType>>)(() => { var _l = acc; _l.Add(r.walked); return _l; }))();
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

    public static CheckResult check_def(UnificationState st, TypeEnv env, ADef def) => ((Func<DefSetup, CheckResult>)((declared) => ((Func<DefParamResult, CheckResult>)((env2) => ((Func<CheckResult, CheckResult>)((body_r) => ((Func<UnifyResult, CheckResult>)((u) => new CheckResult(inferred_type: declared.expected_type, state: u.state)))(unify(body_r.state, env2.remaining_type, body_r.inferred_type))))(infer_expr(env2.state, env2.env, def.body))))(bind_def_params(declared.state, declared.env, def.@params, declared.expected_type, 0, ((long)def.@params.Count)))))(resolve_declared_type(st, env, def));

    public static DefSetup resolve_declared_type(UnificationState st, TypeEnv env, ADef def) => ((((long)def.declared_type.Count) == 0) ? ((Func<FreshResult, DefSetup>)((fr) => new DefSetup(expected_type: fr.var_type, remaining_type: fr.var_type, state: fr.state, env: env)))(fresh_and_advance(st)) : ((Func<CodexType, DefSetup>)((env_type) => ((Func<FreshResult, DefSetup>)((inst) => new DefSetup(expected_type: inst.var_type, remaining_type: inst.var_type, state: inst.state, env: env)))(instantiate_type(st, env_type))))(env_lookup(env, def.name.value)));

    public static DefParamResult bind_def_params(UnificationState st, TypeEnv env, List<AParam> @params, CodexType remaining, long i, long len)
    {
        while (true)
        {
            if ((i == len))
            {
            return new DefParamResult(state: st, env: env, remaining_type: remaining);
            }
            else
            {
            var p = @params[(int)i];
            var _tco_s = remaining;
            if (_tco_s is FunTy _tco_m0)
            {
                var param_ty = _tco_m0.Field0;
                var ret_ty = _tco_m0.Field1;
            var env2 = env_bind(env, p.name.value, param_ty);
            var _tco_0 = st;
            var _tco_1 = env2;
            var _tco_2 = @params;
            var _tco_3 = ret_ty;
            var _tco_4 = (i + 1);
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
            var fr = fresh_and_advance(st);
            var env2 = env_bind(env, p.name.value, fr.var_type);
            var _tco_0 = fr.state;
            var _tco_1 = env2;
            var _tco_2 = @params;
            var _tco_3 = remaining;
            var _tco_4 = (i + 1);
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

    public static ModuleResult check_module(AModule mod) => ((Func<List<TypeBinding>, ModuleResult>)((tdm) => ((Func<LetBindResult, ModuleResult>)((tenv) => ((Func<LetBindResult, ModuleResult>)((env) => check_all_defs(env.state, env.env, mod.defs, 0, ((long)mod.defs.Count), new List<TypeBinding>())))(register_all_defs(tenv.state, tenv.env, tdm, mod.defs, 0, ((long)mod.defs.Count)))))(register_type_defs(empty_unification_state(), builtin_type_env(), tdm, mod.type_defs, 0, ((long)mod.type_defs.Count)))))(build_type_def_map(mod.type_defs, 0, ((long)mod.type_defs.Count), new List<TypeBinding>()));

    public static LetBindResult register_all_defs(UnificationState st, TypeEnv env, List<TypeBinding> tdm, List<ADef> defs, long i, long len)
    {
        while (true)
        {
            if ((i == len))
            {
            return new LetBindResult(state: st, env: env);
            }
            else
            {
            var def = defs[(int)i];
            var ty = ((((long)def.declared_type.Count) == 0) ? ((Func<FreshResult, LetBindResult>)((fr) => ((Func<TypeEnv, LetBindResult>)((env2) => new LetBindResult(state: fr.state, env: env2)))(env_bind(env, def.name.value, fr.var_type))))(fresh_and_advance(st)) : ((Func<CodexType, LetBindResult>)((resolved) => ((Func<ParamResult, LetBindResult>)((pr) => new LetBindResult(state: pr.state, env: env_bind(env, def.name.value, pr.parameterized))))(parameterize_type(st, resolved))))(resolve_type_expr(tdm, def.declared_type[(int)0])));
            var _tco_0 = ty.state;
            var _tco_1 = ty.env;
            var _tco_2 = tdm;
            var _tco_3 = defs;
            var _tco_4 = (i + 1);
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

    public static ModuleResult check_all_defs(UnificationState st, TypeEnv env, List<ADef> defs, long i, long len, List<TypeBinding> acc)
    {
        while (true)
        {
            if ((i == len))
            {
            return new ModuleResult(types: acc, state: st);
            }
            else
            {
            var def = defs[(int)i];
            var r = check_def(st, env, def);
            var resolved = deep_resolve(r.state, r.inferred_type);
            var entry = new TypeBinding(name: def.name.value, bound_type: resolved);
            var _tco_0 = r.state;
            var _tco_1 = env;
            var _tco_2 = defs;
            var _tco_3 = (i + 1);
            var _tco_4 = len;
            var _tco_5 = ((Func<List<TypeBinding>>)(() => { var _l = acc; _l.Add(entry); return _l; }))();
            st = _tco_0;
            env = _tco_1;
            defs = _tco_2;
            i = _tco_3;
            len = _tco_4;
            acc = _tco_5;
            continue;
            }
        }
    }

    public static List<TypeBinding> build_type_def_map(List<ATypeDef> tdefs, long i, long len, List<TypeBinding> acc)
    {
        while (true)
        {
            if ((i == len))
            {
            return acc;
            }
            else
            {
            var td = tdefs[(int)i];
            var entry = td switch { AVariantTypeDef(var name, var type_params, var ctors) => ((Func<List<SumCtor>, TypeBinding>)((sum_ctors) => new TypeBinding(name: name.value, bound_type: new SumTy(name, sum_ctors))))(build_sum_ctors(tdefs, ctors, 0, ((long)ctors.Count), new List<SumCtor>(), acc)), ARecordTypeDef(var name, var type_params, var fields) => ((Func<List<RecordField>, TypeBinding>)((rec_fields) => new TypeBinding(name: name.value, bound_type: new RecordTy(name, rec_fields))))(build_record_fields_for_map(tdefs, fields, 0, ((long)fields.Count), new List<RecordField>(), acc)), _ => throw new InvalidOperationException("Non-exhaustive match"), };
            var _tco_0 = tdefs;
            var _tco_1 = (i + 1);
            var _tco_2 = len;
            var _tco_3 = ((Func<List<TypeBinding>>)(() => { var _l = new List<TypeBinding>(acc); _l.Insert((int)bsearch_text_pos(acc, entry.name, 0, ((long)acc.Count)), entry); return _l; }))();
            tdefs = _tco_0;
            i = _tco_1;
            len = _tco_2;
            acc = _tco_3;
            continue;
            }
        }
    }

    public static List<SumCtor> build_sum_ctors(List<ATypeDef> tdefs, List<AVariantCtorDef> ctors, long i, long len, List<SumCtor> acc, List<TypeBinding> partial_tdm)
    {
        while (true)
        {
            if ((i == len))
            {
            return acc;
            }
            else
            {
            var c = ctors[(int)i];
            var field_types = resolve_type_expr_list_for_map(tdefs, c.fields, 0, ((long)c.fields.Count), new List<CodexType>(), partial_tdm);
            var sc = new SumCtor(name: c.name, fields: field_types);
            var _tco_0 = tdefs;
            var _tco_1 = ctors;
            var _tco_2 = (i + 1);
            var _tco_3 = len;
            var _tco_4 = ((Func<List<SumCtor>>)(() => { var _l = acc; _l.Add(sc); return _l; }))();
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
            if ((i == len))
            {
            return acc;
            }
            else
            {
            var f = fields[(int)i];
            var rfield = new RecordField(name: f.name, type_val: resolve_type_expr(partial_tdm, f.type_expr));
            var _tco_0 = tdefs;
            var _tco_1 = fields;
            var _tco_2 = (i + 1);
            var _tco_3 = len;
            var _tco_4 = ((Func<List<RecordField>>)(() => { var _l = acc; _l.Add(rfield); return _l; }))();
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
            if ((i == len))
            {
            return acc;
            }
            else
            {
            var _tco_0 = tdefs;
            var _tco_1 = args;
            var _tco_2 = (i + 1);
            var _tco_3 = len;
            var _tco_4 = ((Func<List<CodexType>>)(() => { var _l = acc; _l.Add(resolve_type_expr(partial_tdm, args[(int)i])); return _l; }))();
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
            if ((i == len))
            {
            return new LetBindResult(state: st, env: env);
            }
            else
            {
            var td = tdefs[(int)i];
            var r = register_one_type_def(st, env, tdm, td);
            var _tco_0 = r.state;
            var _tco_1 = r.env;
            var _tco_2 = tdm;
            var _tco_3 = tdefs;
            var _tco_4 = (i + 1);
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

    public static LetBindResult register_one_type_def(UnificationState st, TypeEnv env, List<TypeBinding> tdm, ATypeDef td) => td switch { AVariantTypeDef(var name, var type_params, var ctors) => ((Func<CodexType, LetBindResult>)((result_ty) => register_variant_ctors(st, env, tdm, ctors, result_ty, 0, ((long)ctors.Count))))(lookup_type_def(tdm, name.value)), ARecordTypeDef(var name, var type_params, var fields) => ((Func<List<RecordField>, LetBindResult>)((resolved_fields) => ((Func<CodexType, LetBindResult>)((result_ty) => ((Func<CodexType, LetBindResult>)((ctor_ty) => new LetBindResult(state: st, env: env_bind(env, name.value, ctor_ty))))(build_record_ctor_type(tdm, fields, result_ty, 0, ((long)fields.Count)))))(new RecordTy(name, resolved_fields))))(build_record_fields(tdm, fields, 0, ((long)fields.Count), new List<RecordField>())), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static List<RecordField> build_record_fields(List<TypeBinding> tdm, List<ARecordFieldDef> fields, long i, long len, List<RecordField> acc)
    {
        while (true)
        {
            if ((i == len))
            {
            return acc;
            }
            else
            {
            var f = fields[(int)i];
            var rfield = new RecordField(name: f.name, type_val: resolve_type_expr(tdm, f.type_expr));
            var _tco_0 = tdm;
            var _tco_1 = fields;
            var _tco_2 = (i + 1);
            var _tco_3 = len;
            var _tco_4 = ((Func<List<RecordField>>)(() => { var _l = acc; _l.Add(rfield); return _l; }))();
            tdm = _tco_0;
            fields = _tco_1;
            i = _tco_2;
            len = _tco_3;
            acc = _tco_4;
            continue;
            }
        }
    }

    public static CodexType lookup_record_field(List<RecordField> fields, string name) => ((((long)fields.Count) == 0) ? new ErrorTy() : ((Func<RecordField, CodexType>)((f) => ((f.name.value == name) ? f.type_val : lookup_record_field_loop(fields, name, 1, ((long)fields.Count)))))(fields[(int)0]));

    public static CodexType lookup_record_field_loop(List<RecordField> fields, string name, long i, long len)
    {
        while (true)
        {
            if ((i == len))
            {
            return new ErrorTy();
            }
            else
            {
            var f = fields[(int)i];
            if ((f.name.value == name))
            {
            return f.type_val;
            }
            else
            {
            var _tco_0 = fields;
            var _tco_1 = name;
            var _tco_2 = (i + 1);
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
            if ((i == len))
            {
            return new LetBindResult(state: st, env: env);
            }
            else
            {
            var ctor = ctors[(int)i];
            var ctor_ty = build_ctor_type(tdm, ctor.fields, result_ty, 0, ((long)ctor.fields.Count));
            var env2 = env_bind(env, ctor.name.value, ctor_ty);
            var _tco_0 = st;
            var _tco_1 = env2;
            var _tco_2 = tdm;
            var _tco_3 = ctors;
            var _tco_4 = result_ty;
            var _tco_5 = (i + 1);
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

    public static CodexType build_ctor_type(List<TypeBinding> tdm, List<ATypeExpr> fields, CodexType result, long i, long len) => ((i == len) ? result : ((Func<CodexType, CodexType>)((rest) => new FunTy(resolve_type_expr(tdm, fields[(int)i]), rest)))(build_ctor_type(tdm, fields, result, (i + 1), len)));

    public static CodexType build_record_ctor_type(List<TypeBinding> tdm, List<ARecordFieldDef> fields, CodexType result, long i, long len) => ((i == len) ? result : ((Func<ARecordFieldDef, CodexType>)((f) => ((Func<CodexType, CodexType>)((rest) => new FunTy(resolve_type_expr(tdm, f.type_expr), rest)))(build_record_ctor_type(tdm, fields, result, (i + 1), len))))(fields[(int)i]));

    public static CheckResult infer_literal(UnificationState st, LiteralKind kind) => kind switch { IntLit { } => new CheckResult(inferred_type: new IntegerTy(), state: st), NumLit { } => new CheckResult(inferred_type: new NumberTy(), state: st), TextLit { } => new CheckResult(inferred_type: new TextTy(), state: st), CharLit { } => new CheckResult(inferred_type: new CharTy(), state: st), BoolLit { } => new CheckResult(inferred_type: new BooleanTy(), state: st), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static CheckResult infer_name(UnificationState st, TypeEnv env, string name) => (env_has(env, name) ? ((Func<CodexType, CheckResult>)((raw) => ((Func<FreshResult, CheckResult>)((inst) => new CheckResult(inferred_type: inst.var_type, state: inst.state)))(instantiate_type(st, raw))))(env_lookup(env, name)) : new CheckResult(inferred_type: new ErrorTy(), state: add_unify_error(st, "CDX2002", ("Unknown name: " + name))));

    public static FreshResult instantiate_type(UnificationState st, CodexType ty)
    {
        while (true)
        {
            var _tco_s = ty;
            if (_tco_s is ForAllTy _tco_m0)
            {
                var var_id = _tco_m0.Field0;
                var body = _tco_m0.Field1;
            var fr = fresh_and_advance(st);
            var substituted = subst_type_var(body, var_id, fr.var_type);
            var _tco_0 = fr.state;
            var _tco_1 = substituted;
            st = _tco_0;
            ty = _tco_1;
            continue;
            }
            {
            return new FreshResult(var_type: ty, state: st);
            }
        }
    }

    public static CodexType subst_type_var(CodexType ty, long var_id, CodexType replacement) => ty switch { TypeVar(var id) => ((id == var_id) ? replacement : ty), FunTy(var param, var ret) => new FunTy(subst_type_var(param, var_id, replacement), subst_type_var(ret, var_id, replacement)), ListTy(var elem) => new ListTy(subst_type_var(elem, var_id, replacement)), ForAllTy(var inner_id, var body) => ((inner_id == var_id) ? ty : new ForAllTy(inner_id, subst_type_var(body, var_id, replacement))), ConstructedTy(var name, var args) => new ConstructedTy(name, map_subst_type_var(args, var_id, replacement, 0, ((long)args.Count), new List<CodexType>())), SumTy(var name, var ctors) => ty, RecordTy(var name, var fields) => ty, _ => ty, };

    public static List<CodexType> map_subst_type_var(List<CodexType> args, long var_id, CodexType replacement, long i, long len, List<CodexType> acc)
    {
        while (true)
        {
            if ((i == len))
            {
            return acc;
            }
            else
            {
            var _tco_0 = args;
            var _tco_1 = var_id;
            var _tco_2 = replacement;
            var _tco_3 = (i + 1);
            var _tco_4 = len;
            var _tco_5 = ((Func<List<CodexType>>)(() => { var _l = acc; _l.Add(subst_type_var(args[(int)i], var_id, replacement)); return _l; }))();
            args = _tco_0;
            var_id = _tco_1;
            replacement = _tco_2;
            i = _tco_3;
            len = _tco_4;
            acc = _tco_5;
            continue;
            }
        }
    }

    public static CheckResult infer_binary(UnificationState st, TypeEnv env, AExpr left, BinaryOp op, AExpr right) => ((Func<CheckResult, CheckResult>)((lr) => ((Func<CheckResult, CheckResult>)((rr) => infer_binary_op(rr.state, lr.inferred_type, rr.inferred_type, op)))(infer_expr(lr.state, env, right))))(infer_expr(st, env, left));

    public static CheckResult infer_binary_op(UnificationState st, CodexType lt, CodexType rt, BinaryOp op) => op switch { OpAdd { } => infer_arithmetic(st, lt, rt), OpSub { } => infer_arithmetic(st, lt, rt), OpMul { } => infer_arithmetic(st, lt, rt), OpDiv { } => infer_arithmetic(st, lt, rt), OpPow { } => infer_arithmetic(st, lt, rt), OpEq { } => infer_comparison(st, lt, rt), OpNotEq { } => infer_comparison(st, lt, rt), OpLt { } => infer_comparison(st, lt, rt), OpGt { } => infer_comparison(st, lt, rt), OpLtEq { } => infer_comparison(st, lt, rt), OpGtEq { } => infer_comparison(st, lt, rt), OpAnd { } => infer_logical(st, lt, rt), OpOr { } => infer_logical(st, lt, rt), OpAppend { } => infer_append(st, lt, rt), OpCons { } => infer_cons(st, lt, rt), OpDefEq { } => infer_comparison(st, lt, rt), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static CheckResult infer_arithmetic(UnificationState st, CodexType lt, CodexType rt) => ((Func<UnifyResult, CheckResult>)((r) => new CheckResult(inferred_type: lt, state: r.state)))(unify(st, lt, rt));

    public static CheckResult infer_comparison(UnificationState st, CodexType lt, CodexType rt) => ((Func<UnifyResult, CheckResult>)((r) => new CheckResult(inferred_type: new BooleanTy(), state: r.state)))(unify(st, lt, rt));

    public static CheckResult infer_logical(UnificationState st, CodexType lt, CodexType rt) => ((Func<UnifyResult, CheckResult>)((r1) => ((Func<UnifyResult, CheckResult>)((r2) => new CheckResult(inferred_type: new BooleanTy(), state: r2.state)))(unify(r1.state, rt, new BooleanTy()))))(unify(st, lt, new BooleanTy()));

    public static CheckResult infer_append(UnificationState st, CodexType lt, CodexType rt) => ((Func<CodexType, CheckResult>)((resolved) => resolved switch { TextTy { } => ((Func<UnifyResult, CheckResult>)((r) => new CheckResult(inferred_type: new TextTy(), state: r.state)))(unify(st, rt, new TextTy())), _ => ((Func<UnifyResult, CheckResult>)((r) => new CheckResult(inferred_type: lt, state: r.state)))(unify(st, lt, rt)), }))(resolve(st, lt));

    public static CheckResult infer_cons(UnificationState st, CodexType lt, CodexType rt) => ((Func<CodexType, CheckResult>)((list_ty) => ((Func<UnifyResult, CheckResult>)((r) => new CheckResult(inferred_type: list_ty, state: r.state)))(unify(st, rt, list_ty))))(new ListTy(lt));

    public static CheckResult infer_if(UnificationState st, TypeEnv env, AExpr cond, AExpr then_e, AExpr else_e) => ((Func<CheckResult, CheckResult>)((cr) => ((Func<UnifyResult, CheckResult>)((r1) => ((Func<CheckResult, CheckResult>)((tr) => ((Func<CheckResult, CheckResult>)((er) => ((Func<UnifyResult, CheckResult>)((r2) => new CheckResult(inferred_type: tr.inferred_type, state: r2.state)))(unify(er.state, tr.inferred_type, er.inferred_type))))(infer_expr(tr.state, env, else_e))))(infer_expr(r1.state, env, then_e))))(unify(cr.state, cr.inferred_type, new BooleanTy()))))(infer_expr(st, env, cond));

    public static CheckResult infer_let(UnificationState st, TypeEnv env, List<ALetBind> bindings, AExpr body) => ((Func<LetBindResult, CheckResult>)((env2) => infer_expr(env2.state, env2.env, body)))(infer_let_bindings(st, env, bindings, 0, ((long)bindings.Count)));

    public static LetBindResult infer_let_bindings(UnificationState st, TypeEnv env, List<ALetBind> bindings, long i, long len)
    {
        while (true)
        {
            if ((i == len))
            {
            return new LetBindResult(state: st, env: env);
            }
            else
            {
            var b = bindings[(int)i];
            var vr = infer_expr(st, env, b.value);
            var env2 = env_bind(env, b.name.value, vr.inferred_type);
            var _tco_0 = vr.state;
            var _tco_1 = env2;
            var _tco_2 = bindings;
            var _tco_3 = (i + 1);
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

    public static CheckResult infer_lambda(UnificationState st, TypeEnv env, List<Name> @params, AExpr body) => ((Func<LambdaBindResult, CheckResult>)((pr) => ((Func<CheckResult, CheckResult>)((br) => ((Func<CodexType, CheckResult>)((fun_ty) => new CheckResult(inferred_type: fun_ty, state: br.state)))(wrap_fun_type(pr.param_types, br.inferred_type))))(infer_expr(pr.state, pr.env, body))))(bind_lambda_params(st, env, @params, 0, ((long)@params.Count), new List<CodexType>()));

    public static LambdaBindResult bind_lambda_params(UnificationState st, TypeEnv env, List<Name> @params, long i, long len, List<CodexType> acc)
    {
        while (true)
        {
            if ((i == len))
            {
            return new LambdaBindResult(state: st, env: env, param_types: acc);
            }
            else
            {
            var p = @params[(int)i];
            var fr = fresh_and_advance(st);
            var env2 = env_bind(env, p.value, fr.var_type);
            var _tco_0 = fr.state;
            var _tco_1 = env2;
            var _tco_2 = @params;
            var _tco_3 = (i + 1);
            var _tco_4 = len;
            var _tco_5 = ((Func<List<CodexType>>)(() => { var _l = acc; _l.Add(fr.var_type); return _l; }))();
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
            if ((i < 0))
            {
            return result;
            }
            else
            {
            var _tco_0 = param_types;
            var _tco_1 = new FunTy(param_types[(int)i], result);
            var _tco_2 = (i - 1);
            param_types = _tco_0;
            result = _tco_1;
            i = _tco_2;
            continue;
            }
        }
    }

    public static CheckResult infer_application(UnificationState st, TypeEnv env, AExpr func, AExpr arg) => ((Func<CheckResult, CheckResult>)((fr) => ((Func<CheckResult, CheckResult>)((ar) => ((Func<FreshResult, CheckResult>)((ret) => ((Func<UnifyResult, CheckResult>)((r) => new CheckResult(inferred_type: ret.var_type, state: r.state)))(unify(ret.state, fr.inferred_type, new FunTy(ar.inferred_type, ret.var_type)))))(fresh_and_advance(ar.state))))(infer_expr(fr.state, env, arg))))(infer_expr(st, env, func));

    public static CheckResult infer_list(UnificationState st, TypeEnv env, List<AExpr> elems) => ((((long)elems.Count) == 0) ? ((Func<FreshResult, CheckResult>)((fr) => new CheckResult(inferred_type: new ListTy(fr.var_type), state: fr.state)))(fresh_and_advance(st)) : ((Func<CheckResult, CheckResult>)((first) => ((Func<UnificationState, CheckResult>)((st2) => new CheckResult(inferred_type: new ListTy(first.inferred_type), state: st2)))(unify_list_elems(first.state, env, elems, first.inferred_type, 1, ((long)elems.Count)))))(infer_expr(st, env, elems[(int)0])));

    public static UnificationState unify_list_elems(UnificationState st, TypeEnv env, List<AExpr> elems, CodexType elem_ty, long i, long len)
    {
        while (true)
        {
            if ((i == len))
            {
            return st;
            }
            else
            {
            var er = infer_expr(st, env, elems[(int)i]);
            var r = unify(er.state, er.inferred_type, elem_ty);
            var _tco_0 = r.state;
            var _tco_1 = env;
            var _tco_2 = elems;
            var _tco_3 = elem_ty;
            var _tco_4 = (i + 1);
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

    public static CheckResult infer_match(UnificationState st, TypeEnv env, AExpr scrutinee, List<AMatchArm> arms) => ((Func<CheckResult, CheckResult>)((sr) => ((Func<FreshResult, CheckResult>)((fr) => ((Func<UnificationState, CheckResult>)((st2) => new CheckResult(inferred_type: fr.var_type, state: st2)))(infer_match_arms(fr.state, env, sr.inferred_type, fr.var_type, arms, 0, ((long)arms.Count)))))(fresh_and_advance(sr.state))))(infer_expr(st, env, scrutinee));

    public static UnificationState infer_match_arms(UnificationState st, TypeEnv env, CodexType scrut_ty, CodexType result_ty, List<AMatchArm> arms, long i, long len)
    {
        while (true)
        {
            if ((i == len))
            {
            return st;
            }
            else
            {
            var arm = arms[(int)i];
            var pr = bind_pattern(st, env, arm.pattern, scrut_ty);
            var br = infer_expr(pr.state, pr.env, arm.body);
            var r = unify(br.state, br.inferred_type, result_ty);
            var _tco_0 = r.state;
            var _tco_1 = env;
            var _tco_2 = scrut_ty;
            var _tco_3 = result_ty;
            var _tco_4 = arms;
            var _tco_5 = (i + 1);
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

    public static PatBindResult bind_pattern(UnificationState st, TypeEnv env, APat pat, CodexType ty) => pat switch { AVarPat(var name) => new PatBindResult(state: st, env: env_bind(env, name.value, ty)), AWildPat { } => new PatBindResult(state: st, env: env), ALitPat(var val, var kind) => new PatBindResult(state: st, env: env), ACtorPat(var ctor_name, var sub_pats) => ((Func<FreshResult, PatBindResult>)((ctor_lookup) => bind_ctor_sub_patterns(ctor_lookup.state, env, sub_pats, ctor_lookup.var_type, 0, ((long)sub_pats.Count))))(instantiate_type(st, env_lookup(env, ctor_name.value))), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static PatBindResult bind_ctor_sub_patterns(UnificationState st, TypeEnv env, List<APat> sub_pats, CodexType ctor_ty, long i, long len)
    {
        while (true)
        {
            if ((i == len))
            {
            return new PatBindResult(state: st, env: env);
            }
            else
            {
            var _tco_s = ctor_ty;
            if (_tco_s is FunTy _tco_m0)
            {
                var param_ty = _tco_m0.Field0;
                var ret_ty = _tco_m0.Field1;
            var pr = bind_pattern(st, env, sub_pats[(int)i], param_ty);
            var _tco_0 = pr.state;
            var _tco_1 = pr.env;
            var _tco_2 = sub_pats;
            var _tco_3 = ret_ty;
            var _tco_4 = (i + 1);
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
            var fr = fresh_and_advance(st);
            var pr = bind_pattern(fr.state, env, sub_pats[(int)i], fr.var_type);
            var _tco_0 = pr.state;
            var _tco_1 = pr.env;
            var _tco_2 = sub_pats;
            var _tco_3 = ctor_ty;
            var _tco_4 = (i + 1);
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

    public static CheckResult infer_do(UnificationState st, TypeEnv env, List<ADoStmt> stmts) => infer_do_loop(st, env, stmts, 0, ((long)stmts.Count), new NothingTy());

    public static CheckResult infer_do_loop(UnificationState st, TypeEnv env, List<ADoStmt> stmts, long i, long len, CodexType last_ty)
    {
        while (true)
        {
            if ((i == len))
            {
            return new CheckResult(inferred_type: last_ty, state: st);
            }
            else
            {
            var stmt = stmts[(int)i];
            var _tco_s = stmt;
            if (_tco_s is ADoExprStmt _tco_m0)
            {
                var e = _tco_m0.Field0;
            var er = infer_expr(st, env, e);
            var _tco_0 = er.state;
            var _tco_1 = env;
            var _tco_2 = stmts;
            var _tco_3 = (i + 1);
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
            var er = infer_expr(st, env, e);
            var env2 = env_bind(env, name.value, er.inferred_type);
            var _tco_0 = er.state;
            var _tco_1 = env2;
            var _tco_2 = stmts;
            var _tco_3 = (i + 1);
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

    public static CheckResult infer_expr(UnificationState st, TypeEnv env, AExpr expr) => expr switch { ALitExpr(var val, var kind) => infer_literal(st, kind), ANameExpr(var name) => infer_name(st, env, name.value), ABinaryExpr(var left, var op, var right) => infer_binary(st, env, left, op, right), AUnaryExpr(var operand) => ((Func<CheckResult, CheckResult>)((r) => ((Func<UnifyResult, CheckResult>)((u) => new CheckResult(inferred_type: new IntegerTy(), state: u.state)))(unify(r.state, r.inferred_type, new IntegerTy()))))(infer_expr(st, env, operand)), AApplyExpr(var func, var arg) => infer_application(st, env, func, arg), AIfExpr(var cond, var then_e, var else_e) => infer_if(st, env, cond, then_e, else_e), ALetExpr(var bindings, var body) => infer_let(st, env, bindings, body), ALambdaExpr(var @params, var body) => infer_lambda(st, env, @params, body), AMatchExpr(var scrutinee, var arms) => infer_match(st, env, scrutinee, arms), AListExpr(var elems) => infer_list(st, env, elems), ADoExpr(var stmts) => infer_do(st, env, stmts), AFieldAccess(var obj, var field) => ((Func<CheckResult, CheckResult>)((r) => ((Func<CodexType, CheckResult>)((resolved) => resolved switch { RecordTy(var rname, var rfields) => ((Func<CodexType, CheckResult>)((ftype) => new CheckResult(inferred_type: ftype, state: r.state)))(lookup_record_field(rfields, field.value)), ConstructedTy(var cname, var cargs) => ((Func<CodexType, CheckResult>)((record_type) => record_type switch { RecordTy(var rname, var rfields) => ((Func<CodexType, CheckResult>)((ftype) => new CheckResult(inferred_type: ftype, state: r.state)))(lookup_record_field(rfields, field.value)), _ => ((Func<FreshResult, CheckResult>)((fr) => new CheckResult(inferred_type: fr.var_type, state: fr.state)))(fresh_and_advance(r.state)), }))(resolve_constructed_to_record(env, cname.value)), _ => ((Func<FreshResult, CheckResult>)((fr) => new CheckResult(inferred_type: fr.var_type, state: fr.state)))(fresh_and_advance(r.state)), }))(deep_resolve(r.state, r.inferred_type))))(infer_expr(st, env, obj)), ARecordExpr(var name, var fields) => ((Func<UnificationState, CheckResult>)((st2) => ((Func<CodexType, CheckResult>)((ctor_type) => ((Func<CodexType, CheckResult>)((result_type) => new CheckResult(inferred_type: result_type, state: st2)))(strip_fun_args(ctor_type))))((env_has(env, name.value) ? env_lookup(env, name.value) : new ErrorTy()))))(infer_record_fields(st, env, fields, 0, ((long)fields.Count))), AErrorExpr(var msg) => new CheckResult(inferred_type: new ErrorTy(), state: st), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static CodexType resolve_constructed_to_record(TypeEnv env, string name) => (env_has(env, name) ? strip_fun_args(env_lookup(env, name)) : new ErrorTy());

    public static UnificationState infer_record_fields(UnificationState st, TypeEnv env, List<AFieldExpr> fields, long i, long len)
    {
        while (true)
        {
            if ((i == len))
            {
            return st;
            }
            else
            {
            var f = fields[(int)i];
            var r = infer_expr(st, env, f.value);
            var _tco_0 = r.state;
            var _tco_1 = env;
            var _tco_2 = fields;
            var _tco_3 = (i + 1);
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
            var _tco_s = ty;
            if (_tco_s is FunTy _tco_m0)
            {
                var p = _tco_m0.Field0;
                var r = _tco_m0.Field1;
            var _tco_0 = r;
            ty = _tco_0;
            continue;
            }
            {
            return ty;
            }
        }
    }

    public static TypeEnv empty_type_env() => new TypeEnv(bindings: new List<TypeBinding>());

    public static CodexType env_lookup(TypeEnv env, string name) => ((Func<long, CodexType>)((len) => ((len == 0) ? new ErrorTy() : ((Func<long, CodexType>)((pos) => ((pos >= len) ? new ErrorTy() : ((Func<TypeBinding, CodexType>)((b) => ((b.name == name) ? b.bound_type : new ErrorTy())))(env.bindings[(int)pos]))))(bsearch_text_pos(env.bindings, name, 0, len)))))(((long)env.bindings.Count));

    public static bool env_has(TypeEnv env, string name) => ((Func<long, bool>)((len) => ((len == 0) ? false : ((Func<long, bool>)((pos) => ((pos >= len) ? false : (env.bindings[(int)pos].name == name))))(bsearch_text_pos(env.bindings, name, 0, len)))))(((long)env.bindings.Count));

    public static TypeEnv env_bind(TypeEnv env, string name, CodexType ty) => ((Func<long, TypeEnv>)((len) => ((Func<long, TypeEnv>)((pos) => new TypeEnv(bindings: ((Func<List<TypeBinding>>)(() => { var _l = new List<TypeBinding>(env.bindings); _l.Insert((int)pos, new TypeBinding(name: name, bound_type: ty)); return _l; }))())))(bsearch_text_pos(env.bindings, name, 0, len))))(((long)env.bindings.Count));

    public static TypeEnv builtin_type_env() => ((Func<TypeEnv, TypeEnv>)((e) => ((Func<TypeEnv, TypeEnv>)((e2) => ((Func<TypeEnv, TypeEnv>)((e3) => ((Func<TypeEnv, TypeEnv>)((e4) => ((Func<TypeEnv, TypeEnv>)((e5) => ((Func<TypeEnv, TypeEnv>)((e5b) => ((Func<TypeEnv, TypeEnv>)((e6) => ((Func<TypeEnv, TypeEnv>)((e7) => ((Func<TypeEnv, TypeEnv>)((e8) => ((Func<TypeEnv, TypeEnv>)((e9) => ((Func<TypeEnv, TypeEnv>)((e10) => ((Func<TypeEnv, TypeEnv>)((e10b) => ((Func<TypeEnv, TypeEnv>)((e11) => ((Func<TypeEnv, TypeEnv>)((e12) => ((Func<TypeEnv, TypeEnv>)((e13) => ((Func<TypeEnv, TypeEnv>)((e14) => ((Func<TypeEnv, TypeEnv>)((e15) => ((Func<TypeEnv, TypeEnv>)((e16) => ((Func<TypeEnv, TypeEnv>)((e16b) => ((Func<TypeEnv, TypeEnv>)((e16c) => ((Func<TypeEnv, TypeEnv>)((e16d) => ((Func<TypeEnv, TypeEnv>)((e17) => ((Func<TypeEnv, TypeEnv>)((e18) => ((Func<TypeEnv, TypeEnv>)((e19) => ((Func<TypeEnv, TypeEnv>)((e20) => ((Func<TypeEnv, TypeEnv>)((e21) => ((Func<TypeEnv, TypeEnv>)((e22) => ((Func<TypeEnv, TypeEnv>)((e23) => ((Func<TypeEnv, TypeEnv>)((e24) => ((Func<TypeEnv, TypeEnv>)((e25) => ((Func<TypeEnv, TypeEnv>)((e26) => ((Func<TypeEnv, TypeEnv>)((e27) => ((Func<TypeEnv, TypeEnv>)((e28) => ((Func<TypeEnv, TypeEnv>)((e29) => ((Func<TypeEnv, TypeEnv>)((e30) => ((Func<TypeEnv, TypeEnv>)((e31) => ((Func<TypeEnv, TypeEnv>)((e32) => ((Func<TypeEnv, TypeEnv>)((e33) => ((Func<TypeEnv, TypeEnv>)((e34) => ((Func<TypeEnv, TypeEnv>)((e35) => ((Func<TypeEnv, TypeEnv>)((e36) => e36))(env_bind(e35, "text-concat-list", new FunTy(new ListTy(new TextTy()), new TextTy())))))(env_bind(e34, "race", new ForAllTy(0, new FunTy(new ListTy(new FunTy(new NothingTy(), new TypeVar(0))), new TypeVar(0)))))))(env_bind(e33, "par", new ForAllTy(0, new ForAllTy(1, new FunTy(new FunTy(new TypeVar(0), new TypeVar(1)), new FunTy(new ListTy(new TypeVar(0)), new ListTy(new TypeVar(1))))))))))(env_bind(e32, "await", new ForAllTy(0, new FunTy(new ConstructedTy(new Name(value: "Task"), new List<CodexType> { new TypeVar(0) }), new TypeVar(0)))))))(env_bind(e31, "fork", new ForAllTy(0, new FunTy(new FunTy(new NothingTy(), new TypeVar(0)), new ConstructedTy(new Name(value: "Task"), new List<CodexType> { new TypeVar(0) })))))))(env_bind(e30, "current-dir", new TextTy()))))(env_bind(e29, "get-env", new FunTy(new TextTy(), new TextTy())))))(env_bind(e28, "get-args", new ListTy(new TextTy())))))(env_bind(e27, "text-starts-with", new FunTy(new TextTy(), new FunTy(new TextTy(), new BooleanTy()))))))(env_bind(e26, "text-contains", new FunTy(new TextTy(), new FunTy(new TextTy(), new BooleanTy()))))))(env_bind(e25, "text-split", new FunTy(new TextTy(), new FunTy(new TextTy(), new ListTy(new TextTy())))))))(env_bind(e24, "list-files", new FunTy(new TextTy(), new FunTy(new TextTy(), new ListTy(new TextTy())))))))(env_bind(e23, "file-exists", new FunTy(new TextTy(), new BooleanTy())))))(env_bind(e22, "write-file", new FunTy(new TextTy(), new FunTy(new TextTy(), new NothingTy()))))))(env_bind(e21, "read-file", new FunTy(new TextTy(), new TextTy())))))(env_bind(e20, "read-line", new TextTy()))))(env_bind(e19, "fold", new ForAllTy(0, new ForAllTy(1, new FunTy(new FunTy(new TypeVar(1), new FunTy(new TypeVar(0), new TypeVar(1))), new FunTy(new TypeVar(1), new FunTy(new ListTy(new TypeVar(0)), new TypeVar(1))))))))))(env_bind(e18, "filter", new ForAllTy(0, new FunTy(new FunTy(new TypeVar(0), new BooleanTy()), new FunTy(new ListTy(new TypeVar(0)), new ListTy(new TypeVar(0)))))))))(env_bind(e17, "map", new ForAllTy(0, new ForAllTy(1, new FunTy(new FunTy(new TypeVar(0), new TypeVar(1)), new FunTy(new ListTy(new TypeVar(0)), new ListTy(new TypeVar(1))))))))))(env_bind(e16d, "list-at", new ForAllTy(0, new FunTy(new ListTy(new TypeVar(0)), new FunTy(new IntegerTy(), new TypeVar(0))))))))(env_bind(e16c, "list-snoc", new ForAllTy(0, new FunTy(new ListTy(new TypeVar(0)), new FunTy(new TypeVar(0), new ListTy(new TypeVar(0)))))))))(env_bind(e16b, "text-compare", new FunTy(new TextTy(), new FunTy(new TextTy(), new IntegerTy()))))))(env_bind(e16, "list-insert-at", new ForAllTy(0, new FunTy(new ListTy(new TypeVar(0)), new FunTy(new IntegerTy(), new FunTy(new TypeVar(0), new ListTy(new TypeVar(0))))))))))(env_bind(e15, "list-length", new ForAllTy(0, new FunTy(new ListTy(new TypeVar(0)), new IntegerTy()))))))(env_bind(e14, "print-line", new FunTy(new TextTy(), new NothingTy())))))(env_bind(e13, "show", new ForAllTy(0, new FunTy(new TypeVar(0), new TextTy()))))))(env_bind(e12, "text-to-integer", new FunTy(new TextTy(), new IntegerTy())))))(env_bind(e11, "text-replace", new FunTy(new TextTy(), new FunTy(new TextTy(), new FunTy(new TextTy(), new TextTy())))))))(env_bind(e10b, "code-to-char", new FunTy(new IntegerTy(), new CharTy())))))(env_bind(e10, "char-code-at", new FunTy(new TextTy(), new FunTy(new IntegerTy(), new IntegerTy()))))))(env_bind(e9, "char-code", new FunTy(new CharTy(), new IntegerTy())))))(env_bind(e8, "is-whitespace", new FunTy(new CharTy(), new BooleanTy())))))(env_bind(e7, "is-digit", new FunTy(new CharTy(), new BooleanTy())))))(env_bind(e6, "is-letter", new FunTy(new CharTy(), new BooleanTy())))))(env_bind(e5b, "substring", new FunTy(new TextTy(), new FunTy(new IntegerTy(), new FunTy(new IntegerTy(), new TextTy())))))))(env_bind(e5, "char-to-text", new FunTy(new CharTy(), new TextTy())))))(env_bind(e4, "char-at", new FunTy(new TextTy(), new FunTy(new IntegerTy(), new CharTy()))))))(env_bind(e3, "integer-to-text", new FunTy(new IntegerTy(), new TextTy())))))(env_bind(e2, "text-length", new FunTy(new TextTy(), new IntegerTy())))))(env_bind(e, "negate", new FunTy(new IntegerTy(), new IntegerTy())))))(empty_type_env());

    public static UnificationState empty_unification_state() => new UnificationState(substitutions: new List<SubstEntry>(), next_id: 2, errors: new List<Diagnostic>());

    public static CodexType fresh_var(UnificationState st) => new TypeVar(st.next_id);

    public static UnificationState advance_id(UnificationState st) => new UnificationState(substitutions: st.substitutions, next_id: (st.next_id + 1), errors: st.errors);

    public static FreshResult fresh_and_advance(UnificationState st) => new FreshResult(var_type: new TypeVar(st.next_id), state: advance_id(st));

    public static CodexType subst_lookup(long var_id, List<SubstEntry> entries) => ((Func<long, CodexType>)((len) => ((len == 0) ? new ErrorTy() : ((Func<long, CodexType>)((pos) => ((pos >= len) ? new ErrorTy() : ((Func<SubstEntry, CodexType>)((entry) => ((entry.var_id == var_id) ? entry.resolved_type : new ErrorTy())))(entries[(int)pos]))))(bsearch_int_pos(entries, var_id, 0, len)))))(((long)entries.Count));

    public static bool has_subst(long var_id, List<SubstEntry> entries) => ((Func<long, bool>)((len) => ((len == 0) ? false : ((Func<long, bool>)((pos) => ((pos >= len) ? false : (entries[(int)pos].var_id == var_id))))(bsearch_int_pos(entries, var_id, 0, len)))))(((long)entries.Count));

    public static CodexType resolve(UnificationState st, CodexType ty)
    {
        while (true)
        {
            var _tco_s = ty;
            if (_tco_s is TypeVar _tco_m0)
            {
                var id = _tco_m0.Field0;
            if (has_subst(id, st.substitutions))
            {
            var _tco_0 = st;
            var _tco_1 = subst_lookup(id, st.substitutions);
            st = _tco_0;
            ty = _tco_1;
            continue;
            }
            else
            {
            return ty;
            }
            }
            {
            return ty;
            }
        }
    }

    public static UnificationState add_subst(UnificationState st, long var_id, CodexType ty) => ((Func<SubstEntry, UnificationState>)((entry) => ((Func<long, UnificationState>)((pos) => new UnificationState(substitutions: ((Func<List<SubstEntry>>)(() => { var _l = new List<SubstEntry>(st.substitutions); _l.Insert((int)pos, entry); return _l; }))(), next_id: st.next_id, errors: st.errors)))(bsearch_int_pos(st.substitutions, var_id, 0, ((long)st.substitutions.Count)))))(new SubstEntry(var_id: var_id, resolved_type: ty));

    public static UnificationState add_unify_error(UnificationState st, string code, string msg) => new UnificationState(substitutions: st.substitutions, next_id: st.next_id, errors: Enumerable.Concat(st.errors, new List<Diagnostic> { make_error(code, msg) }).ToList());

    public static bool occurs_in(UnificationState st, long var_id, CodexType ty)
    {
        while (true)
        {
            var resolved = resolve(st, ty);
            var _tco_s = resolved;
            if (_tco_s is TypeVar _tco_m0)
            {
                var id = _tco_m0.Field0;
            return (id == var_id);
            }
            else if (_tco_s is FunTy _tco_m1)
            {
                var param = _tco_m1.Field0;
                var ret = _tco_m1.Field1;
            return (occurs_in(st, var_id, param) || occurs_in(st, var_id, ret));
            }
            else if (_tco_s is ListTy _tco_m2)
            {
                var elem = _tco_m2.Field0;
            var _tco_0 = st;
            var _tco_1 = var_id;
            var _tco_2 = elem;
            st = _tco_0;
            var_id = _tco_1;
            ty = _tco_2;
            continue;
            }
            {
            return false;
            }
        }
    }

    public static UnifyResult unify(UnificationState st, CodexType a, CodexType b) => ((Func<CodexType, UnifyResult>)((ra) => ((Func<CodexType, UnifyResult>)((rb) => unify_resolved(st, ra, rb)))(resolve(st, b))))(resolve(st, a));

    public static UnifyResult unify_resolved(UnificationState st, CodexType a, CodexType b) => (types_equal(a, b) ? new UnifyResult(success: true, state: st) : a switch { TypeVar(var id_a) => (occurs_in(st, id_a, b) ? new UnifyResult(success: false, state: add_unify_error(st, "CDX2010", "Infinite type")) : new UnifyResult(success: true, state: add_subst(st, id_a, b))), _ => unify_rhs(st, a, b), });

    public static bool types_equal(CodexType a, CodexType b) => a switch { TypeVar(var id_a) => b switch { TypeVar(var id_b) => (id_a == id_b), _ => false, }, IntegerTy { } => b switch { IntegerTy { } => true, _ => false, }, NumberTy { } => b switch { NumberTy { } => true, _ => false, }, TextTy { } => b switch { TextTy { } => true, _ => false, }, BooleanTy { } => b switch { BooleanTy { } => true, _ => false, }, CharTy { } => b switch { CharTy { } => true, _ => false, }, NothingTy { } => b switch { NothingTy { } => true, _ => false, }, VoidTy { } => b switch { VoidTy { } => true, _ => false, }, ErrorTy { } => b switch { ErrorTy { } => true, _ => false, }, _ => false, };

    public static UnifyResult unify_rhs(UnificationState st, CodexType a, CodexType b) => b switch { TypeVar(var id_b) => (occurs_in(st, id_b, a) ? new UnifyResult(success: false, state: add_unify_error(st, "CDX2010", "Infinite type")) : new UnifyResult(success: true, state: add_subst(st, id_b, a))), _ => unify_structural(st, a, b), };

    public static UnifyResult unify_structural(UnificationState st, CodexType a, CodexType b) => a switch { IntegerTy { } => b switch { IntegerTy { } => new UnifyResult(success: true, state: st), ErrorTy { } => new UnifyResult(success: true, state: st), _ => unify_mismatch(st, a, b), }, NumberTy { } => b switch { NumberTy { } => new UnifyResult(success: true, state: st), ErrorTy { } => new UnifyResult(success: true, state: st), _ => unify_mismatch(st, a, b), }, TextTy { } => b switch { TextTy { } => new UnifyResult(success: true, state: st), ErrorTy { } => new UnifyResult(success: true, state: st), _ => unify_mismatch(st, a, b), }, BooleanTy { } => b switch { BooleanTy { } => new UnifyResult(success: true, state: st), ErrorTy { } => new UnifyResult(success: true, state: st), _ => unify_mismatch(st, a, b), }, NothingTy { } => b switch { NothingTy { } => new UnifyResult(success: true, state: st), ErrorTy { } => new UnifyResult(success: true, state: st), _ => unify_mismatch(st, a, b), }, VoidTy { } => b switch { VoidTy { } => new UnifyResult(success: true, state: st), ErrorTy { } => new UnifyResult(success: true, state: st), _ => unify_mismatch(st, a, b), }, ErrorTy { } => new UnifyResult(success: true, state: st), FunTy(var pa, var ra) => b switch { FunTy(var pb, var rb) => unify_fun(st, pa, ra, pb, rb), ErrorTy { } => new UnifyResult(success: true, state: st), _ => unify_mismatch(st, a, b), }, ListTy(var ea) => b switch { ListTy(var eb) => unify(st, ea, eb), ErrorTy { } => new UnifyResult(success: true, state: st), _ => unify_mismatch(st, a, b), }, ConstructedTy(var na, var args_a) => b switch { ConstructedTy(var nb, var args_b) => ((na.value == nb.value) ? unify_constructed_args(st, args_a, args_b, 0, ((long)args_a.Count)) : unify_mismatch(st, a, b)), SumTy(var sb_name, var sb_ctors) => ((na.value == sb_name.value) ? new UnifyResult(success: true, state: st) : unify_mismatch(st, a, b)), RecordTy(var rb_name, var rb_fields) => ((na.value == rb_name.value) ? new UnifyResult(success: true, state: st) : unify_mismatch(st, a, b)), ErrorTy { } => new UnifyResult(success: true, state: st), _ => unify_mismatch(st, a, b), }, SumTy(var sa_name, var sa_ctors) => b switch { SumTy(var sb_name, var sb_ctors) => ((sa_name.value == sb_name.value) ? new UnifyResult(success: true, state: st) : unify_mismatch(st, a, b)), ConstructedTy(var nb, var args_b) => ((sa_name.value == nb.value) ? new UnifyResult(success: true, state: st) : unify_mismatch(st, a, b)), ErrorTy { } => new UnifyResult(success: true, state: st), _ => unify_mismatch(st, a, b), }, RecordTy(var ra_name, var ra_fields) => b switch { RecordTy(var rb_name, var rb_fields) => ((ra_name.value == rb_name.value) ? new UnifyResult(success: true, state: st) : unify_mismatch(st, a, b)), ConstructedTy(var nb, var args_b) => ((ra_name.value == nb.value) ? new UnifyResult(success: true, state: st) : unify_mismatch(st, a, b)), ErrorTy { } => new UnifyResult(success: true, state: st), _ => unify_mismatch(st, a, b), }, ForAllTy(var id, var body) => unify(st, body, b), _ => b switch { ErrorTy { } => new UnifyResult(success: true, state: st), _ => unify_mismatch(st, a, b), }, };

    public static UnifyResult unify_constructed_args(UnificationState st, List<CodexType> args_a, List<CodexType> args_b, long i, long len)
    {
        while (true)
        {
            if ((i == len))
            {
            return new UnifyResult(success: true, state: st);
            }
            else
            {
            if ((i >= ((long)args_b.Count)))
            {
            return new UnifyResult(success: true, state: st);
            }
            else
            {
            var r = unify(st, args_a[(int)i], args_b[(int)i]);
            if (r.success)
            {
            var _tco_0 = r.state;
            var _tco_1 = args_a;
            var _tco_2 = args_b;
            var _tco_3 = (i + 1);
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

    public static UnifyResult unify_fun(UnificationState st, CodexType pa, CodexType ra, CodexType pb, CodexType rb) => ((Func<UnifyResult, UnifyResult>)((r1) => (r1.success ? unify(r1.state, ra, rb) : r1)))(unify(st, pa, pb));

    public static UnifyResult unify_mismatch(UnificationState st, CodexType a, CodexType b) => new UnifyResult(success: false, state: add_unify_error(st, "CDX2001", ("Type mismatch: " + (type_tag(a) + (" vs " + type_tag(b))))));

    public static string type_tag(CodexType ty) => ty switch { IntegerTy { } => "Integer", NumberTy { } => "Number", TextTy { } => "Text", BooleanTy { } => "Boolean", CharTy { } => "Char", VoidTy { } => "Void", NothingTy { } => "Nothing", ErrorTy { } => "Error", FunTy(var p, var r) => "Fun", ListTy(var e) => "List", TypeVar(var id) => ("T" + _Cce.FromUnicode(id.ToString())), ForAllTy(var id, var body) => "ForAll", SumTy(var name, var ctors) => ("Sum:" + name.value), RecordTy(var name, var fields) => ("Rec:" + name.value), ConstructedTy(var name, var args) => ("Con:" + name.value), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static CodexType deep_resolve(UnificationState st, CodexType ty) => ((Func<CodexType, CodexType>)((resolved) => resolved switch { FunTy(var param, var ret) => new FunTy(deep_resolve(st, param), deep_resolve(st, ret)), ListTy(var elem) => new ListTy(deep_resolve(st, elem)), ConstructedTy(var name, var args) => new ConstructedTy(name, deep_resolve_list(st, args, 0, ((long)args.Count), new List<CodexType>())), ForAllTy(var id, var body) => new ForAllTy(id, deep_resolve(st, body)), SumTy(var name, var ctors) => resolved, RecordTy(var name, var fields) => resolved, _ => resolved, }))(resolve(st, ty));

    public static List<CodexType> deep_resolve_list(UnificationState st, List<CodexType> args, long i, long len, List<CodexType> acc)
    {
        while (true)
        {
            if ((i == len))
            {
            return acc;
            }
            else
            {
            var _tco_0 = st;
            var _tco_1 = args;
            var _tco_2 = (i + 1);
            var _tco_3 = len;
            var _tco_4 = ((Func<List<CodexType>>)(() => { var _l = acc; _l.Add(deep_resolve(st, args[(int)i])); return _l; }))();
            st = _tco_0;
            args = _tco_1;
            i = _tco_2;
            len = _tco_3;
            acc = _tco_4;
            continue;
            }
        }
    }

    public static string compile(string source, string module_name) => ((Func<List<Token>, string>)((tokens) => ((Func<ParseState, string>)((st) => ((Func<Document, string>)((doc) => ((Func<AModule, string>)((ast) => ((Func<ModuleResult, string>)((check_result) => ((Func<IRModule, string>)((ir) => emit_full_module(ir, ast.type_defs)))(lower_module(ast, check_result.types, check_result.state))))(check_module(ast))))(desugar_document(doc, module_name))))(parse_document(st))))(make_parse_state(tokens))))(tokenize(source));

    public static CompileResult compile_checked(string source, string module_name) => compile_with_imports(source, module_name, new List<ResolveResult>());

    public static CompileResult compile_with_imports(string source, string module_name, List<ResolveResult> imported) => ((Func<List<Token>, CompileResult>)((tokens) => ((Func<ParseState, CompileResult>)((st) => ((Func<Document, CompileResult>)((doc) => ((Func<AModule, CompileResult>)((ast) => ((Func<ResolveResult, CompileResult>)((resolve_result) => ((((long)resolve_result.errors.Count) > 0) ? new CompileError(resolve_result.errors) : ((Func<ModuleResult, CompileResult>)((check_result) => ((Func<IRModule, CompileResult>)((ir) => new CompileOk(emit_full_module(ir, ast.type_defs), check_result)))(lower_module(ast, check_result.types, check_result.state))))(check_module(ast)))))(resolve_module_with_imports(ast, imported))))(desugar_document(doc, module_name))))(parse_document(st))))(make_parse_state(tokens))))(tokenize(source));

    public static object compile_streaming(string source, string module_name) => ((Func<List<Token>, object>)((tokens) => ((Func<ParseState, object>)((st) => ((Func<Document, object>)((doc) => ((Func<AModule, object>)((ast) => ((Func<ModuleResult, object>)((check_result) => ((Func<List<TypeBinding>, object>)((ctor_types) => ((Func<List<TypeBinding>, object>)((all_types) => ((Func<List<string>, object>)((ctor_names) => ((Func<object>)(() => { ((Func<object>)(() => { Console.WriteLine(_Cce.ToUnicode(codex_emit_type_defs(ast.type_defs, 0))); return null; }))(); stream_defs(ast.defs, all_types, check_result.state, ctor_names, 0, ((long)ast.defs.Count)); ((Func<object>)(() => { Console.WriteLine(_Cce.ToUnicode("")); return null; }))();  return null; }))()))(codex_collect_ctor_names(ast.type_defs, 0))))(Enumerable.Concat(ctor_types, Enumerable.Concat(check_result.types, builtin_type_env().bindings).ToList()).ToList())))(collect_ctor_bindings(ast.type_defs, 0, ((long)ast.type_defs.Count), new List<TypeBinding>()))))(check_module(ast))))(desugar_document(doc, module_name))))(parse_document(st))))(make_parse_state(tokens))))(tokenize(source));

    public static object stream_defs(List<ADef> defs, List<TypeBinding> types, UnificationState ust, List<string> ctor_names, long i, long len) => ((i == len) ? ((Func<object>)(() => { Console.WriteLine(_Cce.ToUnicode("")); return null; }))() : ((Func<ADef, object>)((def) => ((Func<IRDef, object>)((ir_def) => ((Func<string, object>)((text) => ((Func<object>)(() => { ((Func<object>)(() => { Console.WriteLine(_Cce.ToUnicode(text)); return null; }))(); stream_defs(defs, types, ust, ctor_names, (i + 1), len);  return null; }))()))(codex_emit_def(ir_def, ctor_names))))(lower_def(def, types, ust))))(defs[(int)i]));

    public static object main() => ((Func<object>)(() => { var path = _Cce.FromUnicode(Console.ReadLine() ?? ""); var source = _Cce.FromUnicode(File.ReadAllText(_Cce.ToUnicode(path))); ((Func<string, object>)((output) => ((Func<object>)(() => { Console.WriteLine(_Cce.ToUnicode(output)); return null; }))()))(compile(source, "Codex_Codex_Codex"));  return null; }))();

}
