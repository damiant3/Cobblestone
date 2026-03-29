using System;
using System.Collections.Generic;
using System.Linq;

Codex_codex_src.main();

public abstract record ATypeExpr;

public sealed record ANamedType(Name Field0) : ATypeExpr;
public sealed record AFunType(ATypeExpr Field0, ATypeExpr Field1) : ATypeExpr;
public sealed record AAppType(ATypeExpr Field0, List<ATypeExpr> Field1) : ATypeExpr;

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

public sealed record UnificationState(List<SubstEntry> substitutions, long next_id, List<Diagnostic> errors);

public abstract record ADoStmt;

public sealed record ADoBindStmt(Name Field0, AExpr Field1) : ADoStmt;
public sealed record ADoExprStmt(AExpr Field0) : ADoStmt;

public sealed record Diagnostic(string code, string message, DiagnosticSeverity severity);

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
public sealed record ErrExpr(Token Field0) : Expr;

public sealed record IRFieldVal(string name, IRExpr value);

public abstract record TokenKind;

public sealed record EndOfFile : TokenKind;
public sealed record Newline : TokenKind;
public sealed record Indent : TokenKind;
public sealed record Dedent : TokenKind;
public sealed record IntegerLiteral : TokenKind;
public sealed record NumberLiteral : TokenKind;
public sealed record TextLiteral : TokenKind;
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
public sealed record ErrorToken : TokenKind;

public abstract record ParseDefResult;

public sealed record DefOk(Def Field0, ParseState Field1) : ParseDefResult;
public sealed record DefNone(ParseState Field0) : ParseDefResult;

public sealed record AMatchArm(APat pattern, AExpr body);

public sealed record Document(List<Def> defs, List<TypeDef> type_defs);

public abstract record DiagnosticSeverity;

public sealed record Error : DiagnosticSeverity;
public sealed record Warning : DiagnosticSeverity;
public sealed record Info : DiagnosticSeverity;

public sealed record VariantCtorDef(Token name, List<TypeExpr> fields);

public abstract record APat;

public sealed record AVarPat(Name Field0) : APat;
public sealed record ALitPat(string Field0, LiteralKind Field1) : APat;
public sealed record ACtorPat(Name Field0, List<APat> Field1) : APat;
public sealed record AWildPat : APat;

public sealed record RecordField(Name name, CodexType type_val);

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
public sealed record AErrorExpr(string Field0) : AExpr;

public sealed record RecordFieldExpr(Token name, Expr value);

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

public sealed record ALetBind(Name name, AExpr value);

public sealed record RecordFieldDef(Token name, TypeExpr type_expr);

public sealed record ADef(Name name, List<AParam> @params, List<ATypeExpr> declared_type, AExpr body);

public abstract record Pat;

public sealed record VarPat(Token Field0) : Pat;
public sealed record LitPat(Token Field0) : Pat;
public sealed record CtorPat(Token Field0, List<Pat> Field1) : Pat;
public sealed record WildPat(Token Field0) : Pat;

public sealed record CtorCollectResult(List<string> type_names, List<string> ctor_names);

public sealed record AVariantCtorDef(Name name, List<ATypeExpr> fields);

public sealed record TypeDef(Token name, List<Token> type_params, TypeBody body);

public sealed record TypeEnv(List<TypeBinding> bindings);

public abstract record ParsePatResult;

public sealed record PatOk(Pat Field0, ParseState Field1) : ParsePatResult;

public abstract record ATypeDef;

public sealed record ARecordTypeDef(Name Field0, List<Name> Field1, List<ARecordFieldDef> Field2) : ATypeDef;
public sealed record AVariantTypeDef(Name Field0, List<Name> Field1, List<AVariantCtorDef> Field2) : ATypeDef;

public abstract record IRPat;

public sealed record IrVarPat(string Field0, CodexType Field1) : IRPat;
public sealed record IrLitPat(string Field0, CodexType Field1) : IRPat;
public sealed record IrCtorPat(string Field0, List<IRPat> Field1, CodexType Field2) : IRPat;
public sealed record IrWildPat : IRPat;

public sealed record SourceSpan(SourcePosition start, SourcePosition end, string file);

public sealed record ModuleResult(List<TypeBinding> types, UnificationState state);

public sealed record Name(string value);

public sealed record AModule(Name name, List<ADef> defs, List<ATypeDef> type_defs);

public abstract record ParseTypeDefResult;

public sealed record TypeDefOk(TypeDef Field0, ParseState Field1) : ParseTypeDefResult;
public sealed record TypeDefNone(ParseState Field0) : ParseTypeDefResult;

public sealed record Token(TokenKind kind, string text, long offset, long line, long column);

public sealed record IRBranch(IRPat pattern, IRExpr body);

public sealed record IRParam(string name, CodexType type_val);

public sealed record ParseState(List<Token> tokens, long pos);

public sealed record LetBind(Token name, Expr value);

public abstract record TypeExpr;

public sealed record NamedType(Token Field0) : TypeExpr;
public sealed record FunType(TypeExpr Field0, TypeExpr Field1) : TypeExpr;
public sealed record AppType(TypeExpr Field0, List<TypeExpr> Field1) : TypeExpr;
public sealed record ParenType(TypeExpr Field0) : TypeExpr;
public sealed record ListType(TypeExpr Field0) : TypeExpr;
public sealed record LinearTypeExpr(TypeExpr Field0) : TypeExpr;

public abstract record LexResult;

public sealed record LexToken(Token Field0, LexState Field1) : LexResult;
public sealed record LexEnd : LexResult;

public sealed record Scope(List<string> names);

public sealed record TypeBinding(string name, CodexType bound_type);

public sealed record Def(Token name, List<Token> @params, List<TypeAnn> ann, Expr body);

public sealed record IRDef(string name, List<IRParam> @params, CodexType type_val, IRExpr body);

public abstract record LiteralKind;

public sealed record IntLit : LiteralKind;
public sealed record NumLit : LiteralKind;
public sealed record TextLit : LiteralKind;
public sealed record BoolLit : LiteralKind;

public sealed record DefSetup(CodexType expected_type, CodexType remaining_type, UnificationState state, TypeEnv env);

public sealed record TypeAnn(Token name, TypeExpr type_expr);

public abstract record CompileResult;

public sealed record CompileOk(string Field0, ModuleResult Field1) : CompileResult;
public sealed record CompileError(List<Diagnostic> Field0) : CompileResult;

public sealed record FreshResult(CodexType var_type, UnificationState state);

public sealed record ResolveResult(List<Diagnostic> errors, List<string> top_level_names, List<string> type_names, List<string> ctor_names);

public sealed record UnifyResult(bool success, UnificationState state);

public abstract record CodexType;

public sealed record IntegerTy : CodexType;
public sealed record NumberTy : CodexType;
public sealed record TextTy : CodexType;
public sealed record BooleanTy : CodexType;
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

public sealed record LambdaBindResult(UnificationState state, TypeEnv env, List<CodexType> param_types);

public sealed record AParam(Name name);

public sealed record SumCtor(Name name, List<CodexType> fields);

public sealed record AFieldExpr(Name name, AExpr value);

public sealed record LexState(string source, long offset, long line, long column);

public sealed record DefParamResult(UnificationState state, TypeEnv env, CodexType remaining_type);

public sealed record IRModule(Name name, List<IRDef> defs);

public sealed record CollectResult(List<string> names, List<Diagnostic> errors);

public sealed record ARecordFieldDef(Name name, ATypeExpr type_expr);

public abstract record IRExpr;

public sealed record IrIntLit(long Field0) : IRExpr;
public sealed record IrNumLit(long Field0) : IRExpr;
public sealed record IrTextLit(string Field0) : IRExpr;
public sealed record IrBoolLit(bool Field0) : IRExpr;
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
public sealed record IrRecord(string Field0, List<IRFieldVal> Field1, CodexType Field2) : IRExpr;
public sealed record IrFieldAccess(IRExpr Field0, string Field1, CodexType Field2) : IRExpr;
public sealed record IrError(string Field0, CodexType Field1) : IRExpr;

public abstract record DoStmt;

public sealed record DoBindStmt(Token Field0, Expr Field1) : DoStmt;
public sealed record DoExprStmt(Expr Field0) : DoStmt;

public abstract record ParseTypeResult;

public sealed record TypeOk(TypeExpr Field0, ParseState Field1) : ParseTypeResult;

public abstract record IRDoStmt;

public sealed record IrDoBind(string Field0, CodexType Field1, IRExpr Field2) : IRDoStmt;
public sealed record IrDoExec(IRExpr Field0) : IRDoStmt;

public abstract record TypeBody;

public sealed record RecordBody(List<RecordFieldDef> Field0) : TypeBody;
public sealed record VariantBody(List<VariantCtorDef> Field0) : TypeBody;

public sealed record MatchArm(Pat pattern, Expr body);

public sealed record CheckResult(CodexType inferred_type, UnificationState state);

public sealed record SourcePosition(long line, long column, long offset);

public sealed record SubstEntry(long var_id, CodexType resolved_type);

public sealed record LetBindResult(UnificationState state, TypeEnv env);

public abstract record ParseExprResult;

public sealed record ExprOk(Expr Field0, ParseState Field1) : ParseExprResult;

public static class Codex_codex_src
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
                return new ALetExpr(map_list(new Func<LetBind, ALetBind>(desugar_let_bind), bindings), desugar_expr(body));
            }
            else if (_tco_s is MatchExpr _tco_m7)
            {
                var scrut = _tco_m7.Field0;
                var arms = _tco_m7.Field1;
                return new AMatchExpr(desugar_expr(scrut), map_list(new Func<MatchArm, AMatchArm>(desugar_match_arm), arms));
            }
            else if (_tco_s is ListExpr _tco_m8)
            {
                var elems = _tco_m8.Field0;
                return new AListExpr(map_list(new Func<Expr, AExpr>(desugar_expr), elems));
            }
            else if (_tco_s is RecordExpr _tco_m9)
            {
                var type_tok = _tco_m9.Field0;
                var fields = _tco_m9.Field1;
                return new ARecordExpr(make_name(type_tok.text), map_list(new Func<RecordFieldExpr, AFieldExpr>(desugar_field_expr), fields));
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
                return new ADoExpr(map_list(new Func<DoStmt, ADoStmt>(desugar_do_stmt), stmts));
            }
            else if (_tco_s is ErrExpr _tco_m13)
            {
                var tok = _tco_m13.Field0;
                return new AErrorExpr(tok.text);
            }
        }
    }

    public static AExpr desugar_literal(Token tok)
    {
        return (is_literal(tok.kind) ? new ALitExpr(tok.text, classify_literal(tok.kind)) : new AErrorExpr(tok.text));
    }

    public static LiteralKind classify_literal(TokenKind k)
    {
        return ((Func<TokenKind, LiteralKind>)((_scrutinee_) => (_scrutinee_ is IntegerLiteral _mIntegerLiteral_ ? new IntLit() : (_scrutinee_ is NumberLiteral _mNumberLiteral_ ? new NumLit() : (_scrutinee_ is TextLiteral _mTextLiteral_ ? new TextLit() : (_scrutinee_ is TrueKeyword _mTrueKeyword_ ? new BoolLit() : (_scrutinee_ is FalseKeyword _mFalseKeyword_ ? new BoolLit() : ((Func<TokenKind, LiteralKind>)((_) => new TextLit()))(_scrutinee_))))))))(k);
    }

    public static ALetBind desugar_let_bind(LetBind b)
    {
        return new ALetBind(make_name(b.name.text), desugar_expr(b.value));
    }

    public static AMatchArm desugar_match_arm(MatchArm arm)
    {
        return new AMatchArm(desugar_pattern(arm.pattern), desugar_expr(arm.body));
    }

    public static AFieldExpr desugar_field_expr(RecordFieldExpr f)
    {
        return new AFieldExpr(make_name(f.name.text), desugar_expr(f.value));
    }

    public static ADoStmt desugar_do_stmt(DoStmt s)
    {
        return ((Func<DoStmt, ADoStmt>)((_scrutinee_) => (_scrutinee_ is DoBindStmt _mDoBindStmt_ ? ((Func<Expr, ADoStmt>)((val) => ((Func<Token, ADoStmt>)((tok) => new ADoBindStmt(make_name(tok.text), desugar_expr(val))))((Token)_mDoBindStmt_.Field0)))((Expr)_mDoBindStmt_.Field1) : (_scrutinee_ is DoExprStmt _mDoExprStmt_ ? ((Func<Expr, ADoStmt>)((e) => new ADoExprStmt(desugar_expr(e))))((Expr)_mDoExprStmt_.Field0) : throw new InvalidOperationException("Non-exhaustive match")))))(s);
    }

    public static BinaryOp desugar_bin_op(TokenKind k)
    {
        return ((Func<TokenKind, BinaryOp>)((_scrutinee_) => (_scrutinee_ is Plus _mPlus_ ? new OpAdd() : (_scrutinee_ is Minus _mMinus_ ? new OpSub() : (_scrutinee_ is Star _mStar_ ? new OpMul() : (_scrutinee_ is Slash _mSlash_ ? new OpDiv() : (_scrutinee_ is Caret _mCaret_ ? new OpPow() : (_scrutinee_ is DoubleEquals _mDoubleEquals_ ? new OpEq() : (_scrutinee_ is NotEquals _mNotEquals_ ? new OpNotEq() : (_scrutinee_ is LessThan _mLessThan_ ? new OpLt() : (_scrutinee_ is GreaterThan _mGreaterThan_ ? new OpGt() : (_scrutinee_ is LessOrEqual _mLessOrEqual_ ? new OpLtEq() : (_scrutinee_ is GreaterOrEqual _mGreaterOrEqual_ ? new OpGtEq() : (_scrutinee_ is TripleEquals _mTripleEquals_ ? new OpDefEq() : (_scrutinee_ is PlusPlus _mPlusPlus_ ? new OpAppend() : (_scrutinee_ is ColonColon _mColonColon_ ? new OpCons() : (_scrutinee_ is Ampersand _mAmpersand_ ? new OpAnd() : (_scrutinee_ is Pipe _mPipe_ ? new OpOr() : ((Func<TokenKind, BinaryOp>)((_) => new OpAdd()))(_scrutinee_)))))))))))))))))))(k);
    }

    public static APat desugar_pattern(Pat p)
    {
        return ((Func<Pat, APat>)((_scrutinee_) => (_scrutinee_ is VarPat _mVarPat_ ? ((Func<Token, APat>)((tok) => new AVarPat(make_name(tok.text))))((Token)_mVarPat_.Field0) : (_scrutinee_ is LitPat _mLitPat_ ? ((Func<Token, APat>)((tok) => new ALitPat(tok.text, classify_literal(tok.kind))))((Token)_mLitPat_.Field0) : (_scrutinee_ is CtorPat _mCtorPat_ ? ((Func<List<Pat>, APat>)((subs) => ((Func<Token, APat>)((tok) => new ACtorPat(make_name(tok.text), map_list(new Func<Pat, APat>(desugar_pattern), subs))))((Token)_mCtorPat_.Field0)))((List<Pat>)_mCtorPat_.Field1) : (_scrutinee_ is WildPat _mWildPat_ ? ((Func<Token, APat>)((tok) => new AWildPat()))((Token)_mWildPat_.Field0) : throw new InvalidOperationException("Non-exhaustive match")))))))(p);
    }

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
                return new AAppType(desugar_type_expr(ctor), map_list(new Func<TypeExpr, ATypeExpr>(desugar_type_expr), args));
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
                return new AAppType(new ANamedType(make_name("List")), new List<ATypeExpr>() { desugar_type_expr(elem) });
            }
            else if (_tco_s is LinearTypeExpr _tco_m5)
            {
                var inner = _tco_m5.Field0;
                var _tco_0 = inner;
                t = _tco_0;
                continue;
            }
        }
    }

    public static ADef desugar_def(Def d)
    {
        return new ADef(make_name(d.name.text), map_list(new Func<Token, AParam>(desugar_param), d.@params), new List<ATypeExpr>(), desugar_expr(d.body));
    }

    public static AParam desugar_param(Token tok)
    {
        return new AParam(make_name(tok.text));
    }

    public static ATypeDef desugar_type_def(TypeDef td)
    {
        return ((Func<TypeBody, ATypeDef>)((_scrutinee_) => (_scrutinee_ is RecordBody _mRecordBody_ ? ((Func<List<RecordFieldDef>, ATypeDef>)((fields) => new ARecordTypeDef(make_name(td.name.text), map_list(new Func<Token, Name>(make_type_param_name), td.type_params), map_list(new Func<RecordFieldDef, ARecordFieldDef>(desugar_record_field_def), fields))))((List<RecordFieldDef>)_mRecordBody_.Field0) : (_scrutinee_ is VariantBody _mVariantBody_ ? ((Func<List<VariantCtorDef>, ATypeDef>)((ctors) => new AVariantTypeDef(make_name(td.name.text), map_list(new Func<Token, Name>(make_type_param_name), td.type_params), map_list(new Func<VariantCtorDef, AVariantCtorDef>(desugar_variant_ctor_def), ctors))))((List<VariantCtorDef>)_mVariantBody_.Field0) : throw new InvalidOperationException("Non-exhaustive match")))))(td.body);
    }

    public static Name make_type_param_name(Token tok)
    {
        return make_name(tok.text);
    }

    public static ARecordFieldDef desugar_record_field_def(RecordFieldDef f)
    {
        return new ARecordFieldDef(make_name(f.name.text), desugar_type_expr(f.type_expr));
    }

    public static AVariantCtorDef desugar_variant_ctor_def(VariantCtorDef c)
    {
        return new AVariantCtorDef(make_name(c.name.text), map_list(new Func<TypeExpr, ATypeExpr>(desugar_type_expr), c.fields));
    }

    public static AModule desugar_document(Document doc, string module_name)
    {
        return new AModule(make_name(module_name), map_list(new Func<Def, ADef>(desugar_def), doc.defs), map_list(new Func<TypeDef, ATypeDef>(desugar_type_def), doc.type_defs));
    }

    public static List<T1> map_list<T0, T1>(Func<T0, T1> f, List<T0> xs)
    {
        return map_list_loop(f, xs, 0L, ((long)xs.Count), new List<T1>());
    }

    public static List<T3> map_list_loop<T2, T3>(Func<T2, T3> f, List<T2> xs, long i, long len, List<T3> acc)
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
                var _tco_2 = (i + 1L);
                var _tco_3 = len;
                var _tco_4 = Enumerable.Concat(acc, new List<T3>() { f(xs[(int)i]) }).ToList();
                f = _tco_0;
                xs = _tco_1;
                i = _tco_2;
                len = _tco_3;
                acc = _tco_4;
                continue;
            }
        }
    }

    public static T4 fold_list<T4, T5>(Func<T4, Func<T5, T4>> f, T4 z, List<T5> xs)
    {
        return fold_list_loop(f, z, xs, 0L, ((long)xs.Count));
    }

    public static T6 fold_list_loop<T6, T7>(Func<T6, Func<T7, T6>> f, T6 z, List<T7> xs, long i, long len)
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
                var _tco_3 = (i + 1L);
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

    public static Diagnostic make_error(string code, string msg)
    {
        return new Diagnostic(code, msg, new Error());
    }

    public static Diagnostic make_warning(string code, string msg)
    {
        return new Diagnostic(code, msg, new Warning());
    }

    public static string severity_label(DiagnosticSeverity s)
    {
        return ((Func<DiagnosticSeverity, string>)((_scrutinee_) => (_scrutinee_ is Error _mError_ ? "error" : (_scrutinee_ is Warning _mWarning_ ? "warning" : (_scrutinee_ is Info _mInfo_ ? "info" : throw new InvalidOperationException("Non-exhaustive match"))))))(s);
    }

    public static string diagnostic_display(Diagnostic d)
    {
        return string.Concat(severity_label(d.severity), string.Concat(" ", string.Concat(d.code, string.Concat(": ", d.message))));
    }

    public static Name make_name(string s)
    {
        return new Name(s);
    }

    public static string name_value(Name n)
    {
        return n.value;
    }

    public static SourcePosition make_position(long line, long col, long offset)
    {
        return new SourcePosition(line, col, offset);
    }

    public static SourceSpan make_span(SourcePosition s, SourcePosition e, string f)
    {
        return new SourceSpan(s, e, f);
    }

    public static long span_length(SourceSpan span)
    {
        return (span.end.offset - span.start.offset);
    }

    public static string sanitize(string name)
    {
        return name.Replace("-", "_");
    }

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
            else if (_tco_s is VoidTy _tco_m4)
            {
                return "void";
            }
            else if (_tco_s is NothingTy _tco_m5)
            {
                return "object";
            }
            else if (_tco_s is ErrorTy _tco_m6)
            {
                return "object";
            }
            else if (_tco_s is FunTy _tco_m7)
            {
                var p = _tco_m7.Field0;
                var r = _tco_m7.Field1;
                return string.Concat("Func<", string.Concat(cs_type(p), string.Concat(", ", string.Concat(cs_type(r), ">"))));
            }
            else if (_tco_s is ListTy _tco_m8)
            {
                var elem = _tco_m8.Field0;
                return string.Concat("List<", string.Concat(cs_type(elem), ">"));
            }
            else if (_tco_s is TypeVar _tco_m9)
            {
                var id = _tco_m9.Field0;
                return "object";
            }
            else if (_tco_s is ForAllTy _tco_m10)
            {
                var id = _tco_m10.Field0;
                var body = _tco_m10.Field1;
                var _tco_0 = body;
                ty = _tco_0;
                continue;
            }
            else if (_tco_s is SumTy _tco_m11)
            {
                var name = _tco_m11.Field0;
                var ctors = _tco_m11.Field1;
                return sanitize(name.value);
            }
            else if (_tco_s is RecordTy _tco_m12)
            {
                var name = _tco_m12.Field0;
                var fields = _tco_m12.Field1;
                return sanitize(name.value);
            }
            else if (_tco_s is ConstructedTy _tco_m13)
            {
                var name = _tco_m13.Field0;
                var args = _tco_m13.Field1;
                return sanitize(name.value);
            }
        }
    }

    public static string emit_expr(IRExpr e)
    {
        return ((Func<IRExpr, string>)((_scrutinee_) => (_scrutinee_ is IrIntLit _mIrIntLit_ ? ((Func<long, string>)((n) => (n).ToString()))((long)_mIrIntLit_.Field0) : (_scrutinee_ is IrNumLit _mIrNumLit_ ? ((Func<long, string>)((n) => (n).ToString()))((long)_mIrNumLit_.Field0) : (_scrutinee_ is IrTextLit _mIrTextLit_ ? ((Func<string, string>)((s) => string.Concat("\"", string.Concat(escape_text(s), "\""))))((string)_mIrTextLit_.Field0) : (_scrutinee_ is IrBoolLit _mIrBoolLit_ ? ((Func<bool, string>)((b) => (b ? "true" : "false")))((bool)_mIrBoolLit_.Field0) : (_scrutinee_ is IrName _mIrName_ ? ((Func<CodexType, string>)((ty) => ((Func<string, string>)((n) => sanitize(n)))((string)_mIrName_.Field0)))((CodexType)_mIrName_.Field1) : (_scrutinee_ is IrBinary _mIrBinary_ ? ((Func<CodexType, string>)((ty) => ((Func<IRExpr, string>)((r) => ((Func<IRExpr, string>)((l) => ((Func<IRBinaryOp, string>)((op) => string.Concat("(", string.Concat(emit_expr(l), string.Concat(" ", string.Concat(emit_bin_op(op), string.Concat(" ", string.Concat(emit_expr(r), ")"))))))))((IRBinaryOp)_mIrBinary_.Field0)))((IRExpr)_mIrBinary_.Field1)))((IRExpr)_mIrBinary_.Field2)))((CodexType)_mIrBinary_.Field3) : (_scrutinee_ is IrNegate _mIrNegate_ ? ((Func<IRExpr, string>)((operand) => string.Concat("(-", string.Concat(emit_expr(operand), ")"))))((IRExpr)_mIrNegate_.Field0) : (_scrutinee_ is IrIf _mIrIf_ ? ((Func<CodexType, string>)((ty) => ((Func<IRExpr, string>)((el) => ((Func<IRExpr, string>)((t) => ((Func<IRExpr, string>)((c) => string.Concat("(", string.Concat(emit_expr(c), string.Concat(" ? ", string.Concat(emit_expr(t), string.Concat(" : ", string.Concat(emit_expr(el), ")"))))))))((IRExpr)_mIrIf_.Field0)))((IRExpr)_mIrIf_.Field1)))((IRExpr)_mIrIf_.Field2)))((CodexType)_mIrIf_.Field3) : (_scrutinee_ is IrLet _mIrLet_ ? ((Func<IRExpr, string>)((body) => ((Func<IRExpr, string>)((val) => ((Func<CodexType, string>)((ty) => ((Func<string, string>)((name) => emit_let(name, ty, val, body)))((string)_mIrLet_.Field0)))((CodexType)_mIrLet_.Field1)))((IRExpr)_mIrLet_.Field2)))((IRExpr)_mIrLet_.Field3) : (_scrutinee_ is IrApply _mIrApply_ ? ((Func<CodexType, string>)((ty) => ((Func<IRExpr, string>)((a) => ((Func<IRExpr, string>)((f) => string.Concat(emit_expr(f), string.Concat("(", string.Concat(emit_expr(a), ")")))))((IRExpr)_mIrApply_.Field0)))((IRExpr)_mIrApply_.Field1)))((CodexType)_mIrApply_.Field2) : (_scrutinee_ is IrLambda _mIrLambda_ ? ((Func<CodexType, string>)((ty) => ((Func<IRExpr, string>)((body) => ((Func<List<IRParam>, string>)((@params) => emit_lambda(@params, body)))((List<IRParam>)_mIrLambda_.Field0)))((IRExpr)_mIrLambda_.Field1)))((CodexType)_mIrLambda_.Field2) : (_scrutinee_ is IrList _mIrList_ ? ((Func<CodexType, string>)((ty) => ((Func<List<IRExpr>, string>)((elems) => emit_list(elems, ty)))((List<IRExpr>)_mIrList_.Field0)))((CodexType)_mIrList_.Field1) : (_scrutinee_ is IrMatch _mIrMatch_ ? ((Func<CodexType, string>)((ty) => ((Func<List<IRBranch>, string>)((branches) => ((Func<IRExpr, string>)((scrut) => emit_match(scrut, branches, ty)))((IRExpr)_mIrMatch_.Field0)))((List<IRBranch>)_mIrMatch_.Field1)))((CodexType)_mIrMatch_.Field2) : (_scrutinee_ is IrDo _mIrDo_ ? ((Func<CodexType, string>)((ty) => ((Func<List<IRDoStmt>, string>)((stmts) => emit_do(stmts)))((List<IRDoStmt>)_mIrDo_.Field0)))((CodexType)_mIrDo_.Field1) : (_scrutinee_ is IrRecord _mIrRecord_ ? ((Func<CodexType, string>)((ty) => ((Func<List<IRFieldVal>, string>)((fields) => ((Func<string, string>)((name) => emit_record(name, fields)))((string)_mIrRecord_.Field0)))((List<IRFieldVal>)_mIrRecord_.Field1)))((CodexType)_mIrRecord_.Field2) : (_scrutinee_ is IrFieldAccess _mIrFieldAccess_ ? ((Func<CodexType, string>)((ty) => ((Func<string, string>)((field) => ((Func<IRExpr, string>)((rec) => string.Concat(emit_expr(rec), string.Concat(".", sanitize(field)))))((IRExpr)_mIrFieldAccess_.Field0)))((string)_mIrFieldAccess_.Field1)))((CodexType)_mIrFieldAccess_.Field2) : (_scrutinee_ is IrError _mIrError_ ? ((Func<CodexType, string>)((ty) => ((Func<string, string>)((msg) => string.Concat("/* error: ", string.Concat(msg, " */ default"))))((string)_mIrError_.Field0)))((CodexType)_mIrError_.Field1) : throw new InvalidOperationException("Non-exhaustive match"))))))))))))))))))))(e);
    }

    public static string escape_text(string s)
    {
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    public static string emit_bin_op(IRBinaryOp op)
    {
        return ((Func<IRBinaryOp, string>)((_scrutinee_) => (_scrutinee_ is IrAddInt _mIrAddInt_ ? "+" : (_scrutinee_ is IrSubInt _mIrSubInt_ ? "-" : (_scrutinee_ is IrMulInt _mIrMulInt_ ? "*" : (_scrutinee_ is IrDivInt _mIrDivInt_ ? "/" : (_scrutinee_ is IrPowInt _mIrPowInt_ ? "^" : (_scrutinee_ is IrAddNum _mIrAddNum_ ? "+" : (_scrutinee_ is IrSubNum _mIrSubNum_ ? "-" : (_scrutinee_ is IrMulNum _mIrMulNum_ ? "*" : (_scrutinee_ is IrDivNum _mIrDivNum_ ? "/" : (_scrutinee_ is IrEq _mIrEq_ ? "==" : (_scrutinee_ is IrNotEq _mIrNotEq_ ? "!=" : (_scrutinee_ is IrLt _mIrLt_ ? "<" : (_scrutinee_ is IrGt _mIrGt_ ? ">" : (_scrutinee_ is IrLtEq _mIrLtEq_ ? "<=" : (_scrutinee_ is IrGtEq _mIrGtEq_ ? ">=" : (_scrutinee_ is IrAnd _mIrAnd_ ? "&&" : (_scrutinee_ is IrOr _mIrOr_ ? "||" : (_scrutinee_ is IrAppendText _mIrAppendText_ ? "+" : (_scrutinee_ is IrAppendList _mIrAppendList_ ? "+" : (_scrutinee_ is IrConsList _mIrConsList_ ? "+" : throw new InvalidOperationException("Non-exhaustive match")))))))))))))))))))))))(op);
    }

    public static string emit_let(string name, CodexType ty, IRExpr val, IRExpr body)
    {
        return string.Concat("((", string.Concat(cs_type(ty), string.Concat(" ", string.Concat(sanitize(name), string.Concat(" = ", string.Concat(emit_expr(val), string.Concat(") is var _ ? ", string.Concat(emit_expr(body), " : default)"))))))));
    }

    public static string emit_lambda(List<IRParam> @params, IRExpr body)
    {
        return ((((long)@params.Count) == 0L) ? string.Concat("(() => ", string.Concat(emit_expr(body), ")")) : ((((long)@params.Count) == 1L) ? ((Func<IRParam, string>)((p) => string.Concat("((", string.Concat(cs_type(p.type_val), string.Concat(" ", string.Concat(sanitize(p.name), string.Concat(") => ", string.Concat(emit_expr(body), ")"))))))))(@params[(int)0L]) : string.Concat("(() => ", string.Concat(emit_expr(body), ")"))));
    }

    public static string emit_list(List<IRExpr> elems, CodexType ty)
    {
        return ((((long)elems.Count) == 0L) ? string.Concat("new List<", string.Concat(cs_type(ty), ">()")) : string.Concat("new List<", string.Concat(cs_type(ty), string.Concat("> { ", string.Concat(emit_list_elems(elems, 0L), " }")))));
    }

    public static string emit_list_elems(List<IRExpr> elems, long i)
    {
        return ((i == ((long)elems.Count)) ? "" : ((i == (((long)elems.Count) - 1L)) ? emit_expr(elems[(int)i]) : string.Concat(emit_expr(elems[(int)i]), string.Concat(", ", emit_list_elems(elems, (i + 1L))))));
    }

    public static string emit_match(IRExpr scrut, List<IRBranch> branches, CodexType ty)
    {
        return string.Concat(emit_expr(scrut), string.Concat(" switch { ", string.Concat(emit_match_arms(branches, 0L), " }")));
    }

    public static string emit_match_arms(List<IRBranch> branches, long i)
    {
        return ((i == ((long)branches.Count)) ? "" : ((Func<IRBranch, string>)((arm) => string.Concat(emit_pattern(arm.pattern), string.Concat(" => ", string.Concat(emit_expr(arm.body), string.Concat(", ", emit_match_arms(branches, (i + 1L))))))))(branches[(int)i]));
    }

    public static string emit_pattern(IRPat p)
    {
        return ((Func<IRPat, string>)((_scrutinee_) => (_scrutinee_ is IrVarPat _mIrVarPat_ ? ((Func<CodexType, string>)((ty) => ((Func<string, string>)((name) => string.Concat(cs_type(ty), string.Concat(" ", sanitize(name)))))((string)_mIrVarPat_.Field0)))((CodexType)_mIrVarPat_.Field1) : (_scrutinee_ is IrLitPat _mIrLitPat_ ? ((Func<CodexType, string>)((ty) => ((Func<string, string>)((text) => text))((string)_mIrLitPat_.Field0)))((CodexType)_mIrLitPat_.Field1) : (_scrutinee_ is IrCtorPat _mIrCtorPat_ ? ((Func<CodexType, string>)((ty) => ((Func<List<IRPat>, string>)((subs) => ((Func<string, string>)((name) => ((((long)subs.Count) == 0L) ? string.Concat(sanitize(name), " { }") : string.Concat(sanitize(name), string.Concat("(", string.Concat(emit_sub_patterns(subs, 0L), ")"))))))((string)_mIrCtorPat_.Field0)))((List<IRPat>)_mIrCtorPat_.Field1)))((CodexType)_mIrCtorPat_.Field2) : (_scrutinee_ is IrWildPat _mIrWildPat_ ? "_" : throw new InvalidOperationException("Non-exhaustive match")))))))(p);
    }

    public static string emit_sub_patterns(List<IRPat> subs, long i)
    {
        return ((i == ((long)subs.Count)) ? "" : ((Func<IRPat, string>)((sub) => string.Concat(emit_sub_pattern(sub), string.Concat(((i < (((long)subs.Count) - 1L)) ? ", " : ""), emit_sub_patterns(subs, (i + 1L))))))(subs[(int)i]));
    }

    public static string emit_sub_pattern(IRPat p)
    {
        return ((Func<IRPat, string>)((_scrutinee_) => (_scrutinee_ is IrVarPat _mIrVarPat_ ? ((Func<CodexType, string>)((ty) => ((Func<string, string>)((name) => string.Concat("var ", sanitize(name))))((string)_mIrVarPat_.Field0)))((CodexType)_mIrVarPat_.Field1) : (_scrutinee_ is IrCtorPat _mIrCtorPat_ ? ((Func<CodexType, string>)((ty) => ((Func<List<IRPat>, string>)((subs) => ((Func<string, string>)((name) => emit_pattern(p)))((string)_mIrCtorPat_.Field0)))((List<IRPat>)_mIrCtorPat_.Field1)))((CodexType)_mIrCtorPat_.Field2) : (_scrutinee_ is IrWildPat _mIrWildPat_ ? "_" : (_scrutinee_ is IrLitPat _mIrLitPat_ ? ((Func<CodexType, string>)((ty) => ((Func<string, string>)((text) => text))((string)_mIrLitPat_.Field0)))((CodexType)_mIrLitPat_.Field1) : throw new InvalidOperationException("Non-exhaustive match")))))))(p);
    }

    public static string emit_do(List<IRDoStmt> stmts)
    {
        return string.Concat("{ ", string.Concat(emit_do_stmts(stmts, 0L), " }"));
    }

    public static string emit_do_stmts(List<IRDoStmt> stmts, long i)
    {
        return ((i == ((long)stmts.Count)) ? "" : ((Func<IRDoStmt, string>)((s) => string.Concat(emit_do_stmt(s), string.Concat(" ", emit_do_stmts(stmts, (i + 1L))))))(stmts[(int)i]));
    }

    public static string emit_do_stmt(IRDoStmt s)
    {
        return ((Func<IRDoStmt, string>)((_scrutinee_) => (_scrutinee_ is IrDoBind _mIrDoBind_ ? ((Func<IRExpr, string>)((val) => ((Func<CodexType, string>)((ty) => ((Func<string, string>)((name) => string.Concat("var ", string.Concat(sanitize(name), string.Concat(" = ", string.Concat(emit_expr(val), ";"))))))((string)_mIrDoBind_.Field0)))((CodexType)_mIrDoBind_.Field1)))((IRExpr)_mIrDoBind_.Field2) : (_scrutinee_ is IrDoExec _mIrDoExec_ ? ((Func<IRExpr, string>)((e) => string.Concat(emit_expr(e), ";")))((IRExpr)_mIrDoExec_.Field0) : throw new InvalidOperationException("Non-exhaustive match")))))(s);
    }

    public static string emit_record(string name, List<IRFieldVal> fields)
    {
        return string.Concat("new ", string.Concat(sanitize(name), string.Concat("(", string.Concat(emit_record_fields(fields, 0L), ")"))));
    }

    public static string emit_record_fields(List<IRFieldVal> fields, long i)
    {
        return ((i == ((long)fields.Count)) ? "" : ((Func<IRFieldVal, string>)((f) => string.Concat(sanitize(f.name), string.Concat(": ", string.Concat(emit_expr(f.value), string.Concat(((i < (((long)fields.Count) - 1L)) ? ", " : ""), emit_record_fields(fields, (i + 1L))))))))(fields[(int)i]));
    }

    public static string emit_type_defs(List<ATypeDef> tds, long i)
    {
        return ((i == ((long)tds.Count)) ? "" : string.Concat(emit_type_def(tds[(int)i]), string.Concat("\n", emit_type_defs(tds, (i + 1L)))));
    }

    public static string emit_type_def(ATypeDef td)
    {
        return ((Func<ATypeDef, string>)((_scrutinee_) => (_scrutinee_ is ARecordTypeDef _mARecordTypeDef_ ? ((Func<List<ARecordFieldDef>, string>)((fields) => ((Func<List<Name>, string>)((tparams) => ((Func<Name, string>)((name) => string.Concat("public sealed record ", string.Concat(sanitize(name.value), string.Concat("(", string.Concat(emit_record_field_defs(fields, 0L), ");\n"))))))((Name)_mARecordTypeDef_.Field0)))((List<Name>)_mARecordTypeDef_.Field1)))((List<ARecordFieldDef>)_mARecordTypeDef_.Field2) : (_scrutinee_ is AVariantTypeDef _mAVariantTypeDef_ ? ((Func<List<AVariantCtorDef>, string>)((ctors) => ((Func<List<Name>, string>)((tparams) => ((Func<Name, string>)((name) => string.Concat("public abstract record ", string.Concat(sanitize(name.value), string.Concat(";\n", string.Concat(emit_variant_ctors(ctors, name, 0L), "\n"))))))((Name)_mAVariantTypeDef_.Field0)))((List<Name>)_mAVariantTypeDef_.Field1)))((List<AVariantCtorDef>)_mAVariantTypeDef_.Field2) : throw new InvalidOperationException("Non-exhaustive match")))))(td);
    }

    public static string emit_record_field_defs(List<ARecordFieldDef> fields, long i)
    {
        return ((i == ((long)fields.Count)) ? "" : ((Func<ARecordFieldDef, string>)((f) => string.Concat(emit_type_expr(f.type_expr), string.Concat(" ", string.Concat(sanitize(f.name.value), string.Concat(((i < (((long)fields.Count) - 1L)) ? ", " : ""), emit_record_field_defs(fields, (i + 1L))))))))(fields[(int)i]));
    }

    public static string emit_variant_ctors(List<AVariantCtorDef> ctors, Name base_name, long i)
    {
        return ((i == ((long)ctors.Count)) ? "" : ((Func<AVariantCtorDef, string>)((c) => string.Concat(emit_variant_ctor(c, base_name), emit_variant_ctors(ctors, base_name, (i + 1L)))))(ctors[(int)i]));
    }

    public static string emit_variant_ctor(AVariantCtorDef c, Name base_name)
    {
        return ((((long)c.fields.Count) == 0L) ? string.Concat("public sealed record ", string.Concat(sanitize(c.name.value), string.Concat(" : ", string.Concat(sanitize(base_name.value), ";\n")))) : string.Concat("public sealed record ", string.Concat(sanitize(c.name.value), string.Concat("(", string.Concat(emit_ctor_fields(c.fields, 0L), string.Concat(") : ", string.Concat(sanitize(base_name.value), ";\n")))))));
    }

    public static string emit_ctor_fields(List<ATypeExpr> fields, long i)
    {
        return ((i == ((long)fields.Count)) ? "" : string.Concat(emit_type_expr(fields[(int)i]), string.Concat(" Field", string.Concat((i).ToString(), string.Concat(((i < (((long)fields.Count) - 1L)) ? ", " : ""), emit_ctor_fields(fields, (i + 1L)))))));
    }

    public static string emit_type_expr(ATypeExpr te)
    {
        return ((Func<ATypeExpr, string>)((_scrutinee_) => (_scrutinee_ is ANamedType _mANamedType_ ? ((Func<Name, string>)((name) => when_type_name(name.value)))((Name)_mANamedType_.Field0) : (_scrutinee_ is AFunType _mAFunType_ ? ((Func<ATypeExpr, string>)((r) => ((Func<ATypeExpr, string>)((p) => string.Concat("Func<", string.Concat(emit_type_expr(p), string.Concat(", ", string.Concat(emit_type_expr(r), ">"))))))((ATypeExpr)_mAFunType_.Field0)))((ATypeExpr)_mAFunType_.Field1) : (_scrutinee_ is AAppType _mAAppType_ ? ((Func<List<ATypeExpr>, string>)((args) => ((Func<ATypeExpr, string>)((@base) => string.Concat(emit_type_expr(@base), string.Concat("<", string.Concat(emit_type_expr_list(args, 0L), ">")))))((ATypeExpr)_mAAppType_.Field0)))((List<ATypeExpr>)_mAAppType_.Field1) : throw new InvalidOperationException("Non-exhaustive match"))))))(te);
    }

    public static string when_type_name(string n)
    {
        return ((n == "Integer") ? "long" : ((n == "Number") ? "decimal" : ((n == "Text") ? "string" : ((n == "Boolean") ? "bool" : ((n == "List") ? "List" : sanitize(n))))));
    }

    public static string emit_type_expr_list(List<ATypeExpr> args, long i)
    {
        return ((i == ((long)args.Count)) ? "" : string.Concat(emit_type_expr(args[(int)i]), string.Concat(((i < (((long)args.Count) - 1L)) ? ", " : ""), emit_type_expr_list(args, (i + 1L)))));
    }

    public static string emit_def(IRDef d)
    {
        return string.Concat("    public static ", string.Concat(cs_type(d.type_val), string.Concat(" ", string.Concat(sanitize(d.name), string.Concat("(", string.Concat(emit_def_params(d.@params, 0L), string.Concat(") => ", string.Concat(emit_expr(d.body), ";\n"))))))));
    }

    public static string emit_def_params(List<IRParam> @params, long i)
    {
        return ((i == ((long)@params.Count)) ? "" : ((Func<IRParam, string>)((p) => string.Concat(cs_type(p.type_val), string.Concat(" ", string.Concat(sanitize(p.name), string.Concat(((i < (((long)@params.Count) - 1L)) ? ", " : ""), emit_def_params(@params, (i + 1L))))))))(@params[(int)i]));
    }

    public static string emit_full_module(IRModule m, List<ATypeDef> type_defs)
    {
        return string.Concat("using System;\nusing System.Collections.Generic;\nusing System.Linq;\n\n", string.Concat(emit_type_defs(type_defs, 0L), string.Concat(emit_class_header(m.name.value), string.Concat(emit_defs(m.defs, 0L), "}\n"))));
    }

    public static string emit_module(IRModule m)
    {
        return string.Concat("using System;\nusing System.Collections.Generic;\nusing System.Linq;\n\n", string.Concat(emit_class_header(m.name.value), string.Concat(emit_defs(m.defs, 0L), "}\n")));
    }

    public static string emit_class_header(string name)
    {
        return string.Concat("public static class Codex_", string.Concat(sanitize(name), "\n{\n"));
    }

    public static string emit_defs(List<IRDef> defs, long i)
    {
        return ((i == ((long)defs.Count)) ? "" : string.Concat(emit_def(defs[(int)i]), string.Concat("\n", emit_defs(defs, (i + 1L)))));
    }

    public static IRBinaryOp lower_bin_op(BinaryOp op, CodexType ty)
    {
        return ((Func<BinaryOp, IRBinaryOp>)((_scrutinee_) => (_scrutinee_ is OpAdd _mOpAdd_ ? new IrAddInt() : (_scrutinee_ is OpSub _mOpSub_ ? new IrSubInt() : (_scrutinee_ is OpMul _mOpMul_ ? new IrMulInt() : (_scrutinee_ is OpDiv _mOpDiv_ ? new IrDivInt() : (_scrutinee_ is OpPow _mOpPow_ ? new IrPowInt() : (_scrutinee_ is OpEq _mOpEq_ ? new IrEq() : (_scrutinee_ is OpNotEq _mOpNotEq_ ? new IrNotEq() : (_scrutinee_ is OpLt _mOpLt_ ? new IrLt() : (_scrutinee_ is OpGt _mOpGt_ ? new IrGt() : (_scrutinee_ is OpLtEq _mOpLtEq_ ? new IrLtEq() : (_scrutinee_ is OpGtEq _mOpGtEq_ ? new IrGtEq() : (_scrutinee_ is OpDefEq _mOpDefEq_ ? new IrEq() : (_scrutinee_ is OpAppend _mOpAppend_ ? new IrAppendList() : (_scrutinee_ is OpCons _mOpCons_ ? new IrConsList() : (_scrutinee_ is OpAnd _mOpAnd_ ? new IrAnd() : (_scrutinee_ is OpOr _mOpOr_ ? new IrOr() : throw new InvalidOperationException("Non-exhaustive match")))))))))))))))))))(op);
    }

    public static IRExpr lower_expr(AExpr e, CodexType ty)
    {
        return ((Func<AExpr, IRExpr>)((_scrutinee_) => (_scrutinee_ is ALitExpr _mALitExpr_ ? ((Func<LiteralKind, IRExpr>)((kind) => ((Func<string, IRExpr>)((text) => lower_literal(text, kind)))((string)_mALitExpr_.Field0)))((LiteralKind)_mALitExpr_.Field1) : (_scrutinee_ is ANameExpr _mANameExpr_ ? ((Func<Name, IRExpr>)((name) => new IrName(name.value, ty)))((Name)_mANameExpr_.Field0) : (_scrutinee_ is AApplyExpr _mAApplyExpr_ ? ((Func<AExpr, IRExpr>)((a) => ((Func<AExpr, IRExpr>)((f) => lower_apply(f, a, ty)))((AExpr)_mAApplyExpr_.Field0)))((AExpr)_mAApplyExpr_.Field1) : (_scrutinee_ is ABinaryExpr _mABinaryExpr_ ? ((Func<AExpr, IRExpr>)((r) => ((Func<BinaryOp, IRExpr>)((op) => ((Func<AExpr, IRExpr>)((l) => new IrBinary(lower_bin_op(op, ty), lower_expr(l, ty), lower_expr(r, ty), ty)))((AExpr)_mABinaryExpr_.Field0)))((BinaryOp)_mABinaryExpr_.Field1)))((AExpr)_mABinaryExpr_.Field2) : (_scrutinee_ is AUnaryExpr _mAUnaryExpr_ ? ((Func<AExpr, IRExpr>)((operand) => new IrNegate(lower_expr(operand, new IntegerTy()))))((AExpr)_mAUnaryExpr_.Field0) : (_scrutinee_ is AIfExpr _mAIfExpr_ ? ((Func<AExpr, IRExpr>)((e2) => ((Func<AExpr, IRExpr>)((t) => ((Func<AExpr, IRExpr>)((c) => new IrIf(lower_expr(c, new BooleanTy()), lower_expr(t, ty), lower_expr(e2, ty), ty)))((AExpr)_mAIfExpr_.Field0)))((AExpr)_mAIfExpr_.Field1)))((AExpr)_mAIfExpr_.Field2) : (_scrutinee_ is ALetExpr _mALetExpr_ ? ((Func<AExpr, IRExpr>)((body) => ((Func<List<ALetBind>, IRExpr>)((binds) => lower_let(binds, body, ty)))((List<ALetBind>)_mALetExpr_.Field0)))((AExpr)_mALetExpr_.Field1) : (_scrutinee_ is ALambdaExpr _mALambdaExpr_ ? ((Func<AExpr, IRExpr>)((body) => ((Func<List<Name>, IRExpr>)((@params) => lower_lambda(@params, body, ty)))((List<Name>)_mALambdaExpr_.Field0)))((AExpr)_mALambdaExpr_.Field1) : (_scrutinee_ is AMatchExpr _mAMatchExpr_ ? ((Func<List<AMatchArm>, IRExpr>)((arms) => ((Func<AExpr, IRExpr>)((scrut) => lower_match(scrut, arms, ty)))((AExpr)_mAMatchExpr_.Field0)))((List<AMatchArm>)_mAMatchExpr_.Field1) : (_scrutinee_ is AListExpr _mAListExpr_ ? ((Func<List<AExpr>, IRExpr>)((elems) => lower_list(elems, ty)))((List<AExpr>)_mAListExpr_.Field0) : (_scrutinee_ is ARecordExpr _mARecordExpr_ ? ((Func<List<AFieldExpr>, IRExpr>)((fields) => ((Func<Name, IRExpr>)((name) => lower_record(name, fields, ty)))((Name)_mARecordExpr_.Field0)))((List<AFieldExpr>)_mARecordExpr_.Field1) : (_scrutinee_ is AFieldAccess _mAFieldAccess_ ? ((Func<Name, IRExpr>)((field) => ((Func<AExpr, IRExpr>)((rec) => new IrFieldAccess(lower_expr(rec, ty), field.value, ty)))((AExpr)_mAFieldAccess_.Field0)))((Name)_mAFieldAccess_.Field1) : (_scrutinee_ is ADoExpr _mADoExpr_ ? ((Func<List<ADoStmt>, IRExpr>)((stmts) => lower_do(stmts, ty)))((List<ADoStmt>)_mADoExpr_.Field0) : (_scrutinee_ is AErrorExpr _mAErrorExpr_ ? ((Func<string, IRExpr>)((msg) => new IrError(msg, ty)))((string)_mAErrorExpr_.Field0) : throw new InvalidOperationException("Non-exhaustive match")))))))))))))))))(e);
    }

    public static IRExpr lower_literal(string text, LiteralKind kind)
    {
        return ((Func<LiteralKind, IRExpr>)((_scrutinee_) => (_scrutinee_ is IntLit _mIntLit_ ? new IrIntLit(long.Parse(text)) : (_scrutinee_ is NumLit _mNumLit_ ? new IrIntLit(long.Parse(text)) : (_scrutinee_ is TextLit _mTextLit_ ? new IrTextLit(text) : (_scrutinee_ is BoolLit _mBoolLit_ ? new IrBoolLit((text == "True")) : throw new InvalidOperationException("Non-exhaustive match")))))))(kind);
    }

    public static IRExpr lower_apply(AExpr f, AExpr a, CodexType ty)
    {
        return new IrApply(lower_expr(f, ty), lower_expr(a, ty), ty);
    }

    public static IRExpr lower_let(List<ALetBind> binds, AExpr body, CodexType ty)
    {
        return ((((long)binds.Count) == 0L) ? lower_expr(body, ty) : ((Func<ALetBind, IRExpr>)((b) => new IrLet(b.name.value, ty, lower_expr(b.value, ty), lower_let_rest(binds, body, ty, 1L))))(binds[(int)0L]));
    }

    public static IRExpr lower_let_rest(List<ALetBind> binds, AExpr body, CodexType ty, long i)
    {
        return ((i == ((long)binds.Count)) ? lower_expr(body, ty) : ((Func<ALetBind, IRExpr>)((b) => new IrLet(b.name.value, ty, lower_expr(b.value, ty), lower_let_rest(binds, body, ty, (i + 1L)))))(binds[(int)i]));
    }

    public static IRExpr lower_lambda(List<Name> @params, AExpr body, CodexType ty)
    {
        return new IrLambda(map_list(new Func<Name, IRParam>(lower_param), @params), lower_expr(body, ty), ty);
    }

    public static IRParam lower_param(Name n)
    {
        return new IRParam(n.value, new ErrorTy());
    }

    public static IRExpr lower_match(AExpr scrut, List<AMatchArm> arms, CodexType ty)
    {
        return new IrMatch(lower_expr(scrut, ty), map_list((_p0_) => lower_arm(ty, _p0_), arms), ty);
    }

    public static IRBranch lower_arm(CodexType ty, AMatchArm arm)
    {
        return new IRBranch(lower_pattern(arm.pattern), lower_expr(arm.body, ty));
    }

    public static IRPat lower_pattern(APat p)
    {
        return ((Func<APat, IRPat>)((_scrutinee_) => (_scrutinee_ is AVarPat _mAVarPat_ ? ((Func<Name, IRPat>)((name) => new IrVarPat(name.value, new ErrorTy())))((Name)_mAVarPat_.Field0) : (_scrutinee_ is ALitPat _mALitPat_ ? ((Func<LiteralKind, IRPat>)((kind) => ((Func<string, IRPat>)((text) => new IrLitPat(text, new ErrorTy())))((string)_mALitPat_.Field0)))((LiteralKind)_mALitPat_.Field1) : (_scrutinee_ is ACtorPat _mACtorPat_ ? ((Func<List<APat>, IRPat>)((subs) => ((Func<Name, IRPat>)((name) => new IrCtorPat(name.value, map_list(new Func<APat, IRPat>(lower_pattern), subs), new ErrorTy())))((Name)_mACtorPat_.Field0)))((List<APat>)_mACtorPat_.Field1) : (_scrutinee_ is AWildPat _mAWildPat_ ? new IrWildPat() : throw new InvalidOperationException("Non-exhaustive match")))))))(p);
    }

    public static IRExpr lower_list(List<AExpr> elems, CodexType ty)
    {
        return new IrList(map_list((_p0_) => lower_elem(ty, _p0_), elems), ty);
    }

    public static IRExpr lower_elem(CodexType ty, AExpr e)
    {
        return lower_expr(e, ty);
    }

    public static IRExpr lower_record(Name name, List<AFieldExpr> fields, CodexType ty)
    {
        return new IrRecord(name.value, map_list((_p0_) => lower_field_val(ty, _p0_), fields), ty);
    }

    public static IRFieldVal lower_field_val(CodexType ty, AFieldExpr f)
    {
        return new IRFieldVal(f.name.value, lower_expr(f.value, ty));
    }

    public static IRExpr lower_do(List<ADoStmt> stmts, CodexType ty)
    {
        return new IrDo(map_list((_p0_) => lower_do_stmt(ty, _p0_), stmts), ty);
    }

    public static IRDoStmt lower_do_stmt(CodexType ty, ADoStmt s)
    {
        return ((Func<ADoStmt, IRDoStmt>)((_scrutinee_) => (_scrutinee_ is ADoBindStmt _mADoBindStmt_ ? ((Func<AExpr, IRDoStmt>)((val) => ((Func<Name, IRDoStmt>)((name) => new IrDoBind(name.value, ty, lower_expr(val, ty))))((Name)_mADoBindStmt_.Field0)))((AExpr)_mADoBindStmt_.Field1) : (_scrutinee_ is ADoExprStmt _mADoExprStmt_ ? ((Func<AExpr, IRDoStmt>)((e) => new IrDoExec(lower_expr(e, ty))))((AExpr)_mADoExprStmt_.Field0) : throw new InvalidOperationException("Non-exhaustive match")))))(s);
    }

    public static IRDef lower_def(ADef d)
    {
        return new IRDef(d.name.value, map_list(new Func<AParam, IRParam>(lower_def_param), d.@params), new ErrorTy(), lower_expr(d.body, new ErrorTy()));
    }

    public static IRParam lower_def_param(AParam p)
    {
        return new IRParam(p.name.value, new ErrorTy());
    }

    public static IRModule lower_module(AModule m)
    {
        return new IRModule(m.name, map_list(new Func<ADef, IRDef>(lower_def), m.defs));
    }

    public static string compile(string source, string module_name)
    {
        return ((Func<List<Token>, string>)((tokens) => ((Func<ParseState, string>)((st) => ((Func<Document, string>)((doc) => ((Func<AModule, string>)((ast) => ((Func<IRModule, string>)((ir) => emit_full_module(ir, ast.type_defs)))(lower_module(ast))))(desugar_document(doc, module_name))))(parse_document(st))))(make_parse_state(tokens))))(tokenize(source));
    }

    public static CompileResult compile_checked(string source, string module_name)
    {
        return ((Func<List<Token>, CompileResult>)((tokens) => ((Func<ParseState, CompileResult>)((st) => ((Func<Document, CompileResult>)((doc) => ((Func<AModule, CompileResult>)((ast) => ((Func<ResolveResult, CompileResult>)((resolve_result) => ((((long)resolve_result.errors.Count) > 0L) ? new CompileError(resolve_result.errors) : ((Func<ModuleResult, CompileResult>)((check_result) => ((Func<IRModule, CompileResult>)((ir) => new CompileOk(emit_full_module(ir, ast.type_defs), check_result)))(lower_module(ast))))(check_module(ast)))))(resolve_module(ast))))(desugar_document(doc, module_name))))(parse_document(st))))(make_parse_state(tokens))))(tokenize(source));
    }

    public static string test_source()
    {
        return "square : Integer -> Integer\nsquare (x) = x * x\nmain = square 5";
    }

    public static object main()
    {
        ((Func<object>)(() => {
                Console.WriteLine(compile(test_source(), "test"));
                return null;
            }))();
        return null;
    }

    public static Scope empty_scope()
    {
        return new Scope(new List<string>());
    }

    public static bool scope_has(Scope sc, string name)
    {
        return scope_has_loop(sc.names, name, 0L, ((long)sc.names.Count));
    }

    public static bool scope_has_loop(List<string> names, string name, long i, long len)
    {
        while (true)
        {
            if ((i == len))
            {
                return false;
            }
            else
            {
                if ((names[(int)i] == name))
                {
                    return true;
                }
                else
                {
                    var _tco_0 = names;
                    var _tco_1 = name;
                    var _tco_2 = (i + 1L);
                    var _tco_3 = len;
                    names = _tco_0;
                    name = _tco_1;
                    i = _tco_2;
                    len = _tco_3;
                    continue;
                }
            }
        }
    }

    public static Scope scope_add(Scope sc, string name)
    {
        return new Scope(Enumerable.Concat(new List<string>() { name }, sc.names).ToList());
    }

    public static List<string> builtin_names()
    {
        return new List<string>() { "show", "negate", "True", "False", "Nothing", "print-line", "read-line", "open-file", "read-all", "close-file", "char-at", "text-length", "substring", "is-letter", "is-digit", "is-whitespace", "text-to-integer", "integer-to-text", "text-replace", "char-code", "code-to-char", "list-length", "list-at", "map", "filter", "fold" };
    }

    public static bool is_type_name(string name)
    {
        return ((((long)name.Length) == 0L) ? false : ((name[(int)0L].ToString().Length > 0 && char.IsLetter(name[(int)0L].ToString()[0])) && is_upper_char(name[(int)0L].ToString())));
    }

    public static bool is_upper_char(string c)
    {
        return ((Func<long, bool>)((code) => ((code >= 65L) && (code <= 90L))))(((long)c[0]));
    }

    public static CollectResult collect_top_level_names(List<ADef> defs, long i, long len, List<string> acc, List<Diagnostic> errs)
    {
        while (true)
        {
            if ((i == len))
            {
                return new CollectResult(acc, errs);
            }
            else
            {
                var def = defs[(int)i];
                var name = def.name.value;
                if (list_contains(acc, name))
                {
                    var _tco_0 = defs;
                    var _tco_1 = (i + 1L);
                    var _tco_2 = len;
                    var _tco_3 = acc;
                    var _tco_4 = Enumerable.Concat(errs, new List<Diagnostic>() { make_error("CDX3001", string.Concat("Duplicate definition: ", name)) }).ToList();
                    defs = _tco_0;
                    i = _tco_1;
                    len = _tco_2;
                    acc = _tco_3;
                    errs = _tco_4;
                    continue;
                }
                else
                {
                    var _tco_0 = defs;
                    var _tco_1 = (i + 1L);
                    var _tco_2 = len;
                    var _tco_3 = Enumerable.Concat(acc, new List<string>() { name }).ToList();
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

    public static bool list_contains(List<string> xs, string name)
    {
        return list_contains_loop(xs, name, 0L, ((long)xs.Count));
    }

    public static bool list_contains_loop(List<string> xs, string name, long i, long len)
    {
        while (true)
        {
            if ((i == len))
            {
                return false;
            }
            else
            {
                if ((xs[(int)i] == name))
                {
                    return true;
                }
                else
                {
                    var _tco_0 = xs;
                    var _tco_1 = name;
                    var _tco_2 = (i + 1L);
                    var _tco_3 = len;
                    xs = _tco_0;
                    name = _tco_1;
                    i = _tco_2;
                    len = _tco_3;
                    continue;
                }
            }
        }
    }

    public static CtorCollectResult collect_ctor_names(List<ATypeDef> type_defs, long i, long len, List<string> type_acc, List<string> ctor_acc)
    {
        while (true)
        {
            if ((i == len))
            {
                return new CtorCollectResult(type_acc, ctor_acc);
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
                    var new_type_acc = Enumerable.Concat(type_acc, new List<string>() { name.value }).ToList();
                    var new_ctor_acc = collect_variant_ctors(ctors, 0L, ((long)ctors.Count), ctor_acc);
                    var _tco_0 = type_defs;
                    var _tco_1 = (i + 1L);
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
                    var _tco_1 = (i + 1L);
                    var _tco_2 = len;
                    var _tco_3 = Enumerable.Concat(type_acc, new List<string>() { name.value }).ToList();
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
                var _tco_1 = (i + 1L);
                var _tco_2 = len;
                var _tco_3 = Enumerable.Concat(acc, new List<string>() { ctor.name.value }).ToList();
                ctors = _tco_0;
                i = _tco_1;
                len = _tco_2;
                acc = _tco_3;
                continue;
            }
        }
    }

    public static Scope build_all_names_scope(List<string> top_names, List<string> ctor_names, List<string> builtins)
    {
        return ((Func<Scope, Scope>)((sc) => ((Func<Scope, Scope>)((sc2) => add_names_to_scope(sc2, builtins, 0L, ((long)builtins.Count))))(add_names_to_scope(sc, ctor_names, 0L, ((long)ctor_names.Count)))))(add_names_to_scope(empty_scope(), top_names, 0L, ((long)top_names.Count)));
    }

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
                var _tco_2 = (i + 1L);
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
                    return new List<Diagnostic>() { make_error("CDX3002", string.Concat("Undefined name: ", name.value)) };
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
                return resolve_let(sc, bindings, body, 0L, ((long)bindings.Count), new List<Diagnostic>());
            }
            else if (_tco_s is ALambdaExpr _tco_m7)
            {
                var @params = _tco_m7.Field0;
                var body = _tco_m7.Field1;
                var sc2 = add_lambda_params(sc, @params, 0L, ((long)@params.Count));
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
                return Enumerable.Concat(resolve_expr(sc, scrutinee), resolve_match_arms(sc, arms, 0L, ((long)arms.Count), new List<Diagnostic>())).ToList();
            }
            else if (_tco_s is AListExpr _tco_m9)
            {
                var elems = _tco_m9.Field0;
                return resolve_list_elems(sc, elems, 0L, ((long)elems.Count), new List<Diagnostic>());
            }
            else if (_tco_s is ARecordExpr _tco_m10)
            {
                var name = _tco_m10.Field0;
                var fields = _tco_m10.Field1;
                return resolve_record_fields(sc, fields, 0L, ((long)fields.Count), new List<Diagnostic>());
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
                return resolve_do_stmts(sc, stmts, 0L, ((long)stmts.Count), new List<Diagnostic>());
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
                var _tco_3 = (i + 1L);
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
                var _tco_2 = (i + 1L);
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
                var _tco_2 = (i + 1L);
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

    public static Scope collect_pattern_names(Scope sc, APat pat)
    {
        return ((Func<APat, Scope>)((_scrutinee_) => (_scrutinee_ is AVarPat _mAVarPat_ ? ((Func<Name, Scope>)((name) => scope_add(sc, name.value)))((Name)_mAVarPat_.Field0) : (_scrutinee_ is ACtorPat _mACtorPat_ ? ((Func<List<APat>, Scope>)((subs) => ((Func<Name, Scope>)((name) => collect_ctor_pat_names(sc, subs, 0L, ((long)subs.Count))))((Name)_mACtorPat_.Field0)))((List<APat>)_mACtorPat_.Field1) : (_scrutinee_ is ALitPat _mALitPat_ ? ((Func<LiteralKind, Scope>)((kind) => ((Func<string, Scope>)((val) => sc))((string)_mALitPat_.Field0)))((LiteralKind)_mALitPat_.Field1) : (_scrutinee_ is AWildPat _mAWildPat_ ? sc : throw new InvalidOperationException("Non-exhaustive match")))))))(pat);
    }

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
                var _tco_2 = (i + 1L);
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
                var _tco_2 = (i + 1L);
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
                var _tco_2 = (i + 1L);
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
                    var _tco_2 = (i + 1L);
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
                    var _tco_2 = (i + 1L);
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
                var def_scope = add_def_params(sc, def.@params, 0L, ((long)def.@params.Count));
                var errs2 = resolve_expr(def_scope, def.body);
                var _tco_0 = sc;
                var _tco_1 = defs;
                var _tco_2 = (i + 1L);
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
                var _tco_2 = (i + 1L);
                var _tco_3 = len;
                sc = _tco_0;
                @params = _tco_1;
                i = _tco_2;
                len = _tco_3;
                continue;
            }
        }
    }

    public static ResolveResult resolve_module(AModule mod)
    {
        return ((Func<CollectResult, ResolveResult>)((top) => ((Func<CtorCollectResult, ResolveResult>)((ctors) => ((Func<Scope, ResolveResult>)((sc) => ((Func<List<Diagnostic>, ResolveResult>)((expr_errs) => new ResolveResult(Enumerable.Concat(top.errors, expr_errs).ToList(), top.names, ctors.type_names, ctors.ctor_names)))(resolve_all_defs(sc, mod.defs, 0L, ((long)mod.defs.Count), new List<Diagnostic>()))))(build_all_names_scope(top.names, ctors.ctor_names, builtin_names()))))(collect_ctor_names(mod.type_defs, 0L, ((long)mod.type_defs.Count), new List<string>(), new List<string>()))))(collect_top_level_names(mod.defs, 0L, ((long)mod.defs.Count), new List<string>(), new List<Diagnostic>()));
    }

    public static LexState make_lex_state(string src)
    {
        return new LexState(src, 0L, 1L, 1L);
    }

    public static bool is_at_end(LexState st)
    {
        return (st.offset >= ((long)st.source.Length));
    }

    public static string peek_char(LexState st)
    {
        return (is_at_end(st) ? "" : st.source[(int)st.offset].ToString());
    }

    public static LexState advance_char(LexState st)
    {
        return ((peek_char(st) == "\n") ? new LexState(st.source, (st.offset + 1L), (st.line + 1L), 1L) : new LexState(st.source, (st.offset + 1L), st.line, (st.column + 1L)));
    }

    public static LexState skip_spaces(LexState st)
    {
        while (true)
        {
            if (is_at_end(st))
            {
                return st;
            }
            else
            {
                if ((peek_char(st) == " "))
                {
                    var _tco_0 = advance_char(st);
                    st = _tco_0;
                    continue;
                }
                else
                {
                    return st;
                }
            }
        }
    }

    public static LexState scan_ident_rest(LexState st)
    {
        while (true)
        {
            if (is_at_end(st))
            {
                return st;
            }
            else
            {
                var ch = peek_char(st);
                if ((ch.Length > 0 && char.IsLetter(ch[0])))
                {
                    var _tco_0 = advance_char(st);
                    st = _tco_0;
                    continue;
                }
                else
                {
                    if ((ch.Length > 0 && char.IsDigit(ch[0])))
                    {
                        var _tco_0 = advance_char(st);
                        st = _tco_0;
                        continue;
                    }
                    else
                    {
                        if ((ch == "_"))
                        {
                            var _tco_0 = advance_char(st);
                            st = _tco_0;
                            continue;
                        }
                        else
                        {
                            if ((ch == "-"))
                            {
                                var next = advance_char(st);
                                if (is_at_end(next))
                                {
                                    return st;
                                }
                                else
                                {
                                    if ((peek_char(next).Length > 0 && char.IsLetter(peek_char(next)[0])))
                                    {
                                        var _tco_0 = next;
                                        st = _tco_0;
                                        continue;
                                    }
                                    else
                                    {
                                        return st;
                                    }
                                }
                            }
                            else
                            {
                                return st;
                            }
                        }
                    }
                }
            }
        }
    }

    public static LexState scan_digits(LexState st)
    {
        while (true)
        {
            if (is_at_end(st))
            {
                return st;
            }
            else
            {
                var ch = peek_char(st);
                if ((ch.Length > 0 && char.IsDigit(ch[0])))
                {
                    var _tco_0 = advance_char(st);
                    st = _tco_0;
                    continue;
                }
                else
                {
                    if ((ch == "_"))
                    {
                        var _tco_0 = advance_char(st);
                        st = _tco_0;
                        continue;
                    }
                    else
                    {
                        return st;
                    }
                }
            }
        }
    }

    public static LexState scan_string_body(LexState st)
    {
        while (true)
        {
            if (is_at_end(st))
            {
                return st;
            }
            else
            {
                var ch = peek_char(st);
                if ((ch == "\""))
                {
                    return advance_char(st);
                }
                else
                {
                    if ((ch == "\n"))
                    {
                        return st;
                    }
                    else
                    {
                        if ((ch == "\\"))
                        {
                            var _tco_0 = advance_char(advance_char(st));
                            st = _tco_0;
                            continue;
                        }
                        else
                        {
                            var _tco_0 = advance_char(st);
                            st = _tco_0;
                            continue;
                        }
                    }
                }
            }
        }
    }

    public static TokenKind classify_word(string w)
    {
        return ((w == "let") ? new LetKeyword() : ((w == "in") ? new InKeyword() : ((w == "if") ? new IfKeyword() : ((w == "then") ? new ThenKeyword() : ((w == "else") ? new ElseKeyword() : ((w == "when") ? new WhenKeyword() : ((w == "where") ? new WhereKeyword() : ((w == "do") ? new DoKeyword() : ((w == "record") ? new RecordKeyword() : ((w == "import") ? new ImportKeyword() : ((w == "export") ? new ExportKeyword() : ((w == "claim") ? new ClaimKeyword() : ((w == "proof") ? new ProofKeyword() : ((w == "forall") ? new ForAllKeyword() : ((w == "exists") ? new ThereExistsKeyword() : ((w == "linear") ? new LinearKeyword() : ((w == "True") ? new TrueKeyword() : ((w == "False") ? new FalseKeyword() : ((Func<long, TokenKind>)((first_code) => ((first_code >= 65L) ? ((first_code <= 90L) ? new TypeIdentifier() : new Identifier()) : new Identifier())))(((long)w[(int)0L].ToString()[0]))))))))))))))))))));
    }

    public static Token make_token(TokenKind kind, string text, LexState st)
    {
        return new Token(kind, text, st.offset, st.line, st.column);
    }

    public static string extract_text(LexState st, long start, LexState end_st)
    {
        return st.source.Substring((int)start, (int)(end_st.offset - start));
    }

    public static LexResult scan_token(LexState st)
    {
        return ((Func<LexState, LexResult>)((s) => (is_at_end(s) ? new LexEnd() : ((Func<string, LexResult>)((ch) => ((ch == "\n") ? new LexToken(make_token(new Newline(), "\n", s), advance_char(s)) : ((ch == "\"") ? ((Func<long, LexResult>)((start) => ((Func<LexState, LexResult>)((after) => ((Func<long, LexResult>)((text_len) => new LexToken(make_token(new TextLiteral(), s.source.Substring((int)start, (int)text_len), s), after)))(((after.offset - start) - 1L))))(scan_string_body(advance_char(s)))))((s.offset + 1L)) : ((ch.Length > 0 && char.IsLetter(ch[0])) ? ((Func<long, LexResult>)((start) => ((Func<LexState, LexResult>)((after) => ((Func<string, LexResult>)((word) => new LexToken(make_token(classify_word(word), word, s), after)))(extract_text(s, start, after))))(scan_ident_rest(advance_char(s)))))(s.offset) : ((ch == "_") ? ((Func<long, LexResult>)((start) => ((Func<LexState, LexResult>)((after) => ((Func<string, LexResult>)((word) => ((((long)word.Length) == 1L) ? new LexToken(make_token(new Underscore(), "_", s), after) : new LexToken(make_token(classify_word(word), word, s), after))))(extract_text(s, start, after))))(scan_ident_rest(advance_char(s)))))(s.offset) : ((ch.Length > 0 && char.IsDigit(ch[0])) ? ((Func<long, LexResult>)((start) => ((Func<LexState, LexResult>)((after) => (is_at_end(after) ? new LexToken(make_token(new IntegerLiteral(), extract_text(s, start, after), s), after) : ((peek_char(after) == ".") ? ((Func<LexState, LexResult>)((after2) => new LexToken(make_token(new NumberLiteral(), extract_text(s, start, after2), s), after2)))(scan_digits(advance_char(after))) : new LexToken(make_token(new IntegerLiteral(), extract_text(s, start, after), s), after)))))(scan_digits(advance_char(s)))))(s.offset) : scan_operator(s))))))))(peek_char(s)))))(skip_spaces(st));
    }

    public static LexResult scan_operator(LexState s)
    {
        return ((Func<string, LexResult>)((ch) => ((Func<LexState, LexResult>)((next) => ((ch == "(") ? new LexToken(make_token(new LeftParen(), "(", s), next) : ((ch == ")") ? new LexToken(make_token(new RightParen(), ")", s), next) : ((ch == "[") ? new LexToken(make_token(new LeftBracket(), "[", s), next) : ((ch == "]") ? new LexToken(make_token(new RightBracket(), "]", s), next) : ((ch == "{") ? new LexToken(make_token(new LeftBrace(), "{", s), next) : ((ch == "}") ? new LexToken(make_token(new RightBrace(), "}", s), next) : ((ch == ",") ? new LexToken(make_token(new Comma(), ",", s), next) : ((ch == ".") ? new LexToken(make_token(new Dot(), ".", s), next) : ((ch == "^") ? new LexToken(make_token(new Caret(), "^", s), next) : ((ch == "&") ? new LexToken(make_token(new Ampersand(), "&", s), next) : scan_multi_char_operator(s)))))))))))))(advance_char(s))))(peek_char(s));
    }

    public static LexResult scan_multi_char_operator(LexState s)
    {
        return ((Func<string, LexResult>)((ch) => ((Func<LexState, LexResult>)((next) => ((Func<string, LexResult>)((next_ch) => ((ch == "+") ? ((next_ch == "+") ? new LexToken(make_token(new PlusPlus(), "++", s), advance_char(next)) : new LexToken(make_token(new Plus(), "+", s), next)) : ((ch == "-") ? ((next_ch == ">") ? new LexToken(make_token(new Arrow(), "->", s), advance_char(next)) : new LexToken(make_token(new Minus(), "-", s), next)) : ((ch == "*") ? new LexToken(make_token(new Star(), "*", s), next) : ((ch == "/") ? ((next_ch == "=") ? new LexToken(make_token(new NotEquals(), "/=", s), advance_char(next)) : new LexToken(make_token(new Slash(), "/", s), next)) : ((ch == "=") ? ((next_ch == "=") ? ((Func<LexState, LexResult>)((next2) => ((Func<string, LexResult>)((next2_ch) => ((next2_ch == "=") ? new LexToken(make_token(new TripleEquals(), "===", s), advance_char(next2)) : new LexToken(make_token(new DoubleEquals(), "==", s), next2))))((is_at_end(next2) ? "" : peek_char(next2)))))(advance_char(next)) : new LexToken(make_token(new Equals_(), "=", s), next)) : ((ch == ":") ? ((next_ch == ":") ? new LexToken(make_token(new ColonColon(), "::", s), advance_char(next)) : new LexToken(make_token(new Colon(), ":", s), next)) : ((ch == "|") ? ((next_ch == "-") ? new LexToken(make_token(new Turnstile(), "|-", s), advance_char(next)) : new LexToken(make_token(new Pipe(), "|", s), next)) : ((ch == "<") ? ((next_ch == "=") ? new LexToken(make_token(new LessOrEqual(), "<=", s), advance_char(next)) : ((next_ch == "-") ? new LexToken(make_token(new LeftArrow(), "<-", s), advance_char(next)) : new LexToken(make_token(new LessThan(), "<", s), next))) : ((ch == ">") ? ((next_ch == "=") ? new LexToken(make_token(new GreaterOrEqual(), ">=", s), advance_char(next)) : new LexToken(make_token(new GreaterThan(), ">", s), next)) : new LexToken(make_token(new ErrorToken(), s.source[(int)s.offset].ToString(), s), next))))))))))))((is_at_end(next) ? "" : peek_char(next)))))(advance_char(s))))(peek_char(s));
    }

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
                    return Enumerable.Concat(acc, new List<Token>() { tok }).ToList();
                }
                else
                {
                    var _tco_0 = next;
                    var _tco_1 = Enumerable.Concat(acc, new List<Token>() { tok }).ToList();
                    st = _tco_0;
                    acc = _tco_1;
                    continue;
                }
            }
            else if (_tco_s is LexEnd _tco_m1)
            {
                return Enumerable.Concat(acc, new List<Token>() { make_token(new EndOfFile(), "", st) }).ToList();
            }
        }
    }

    public static List<Token> tokenize(string src)
    {
        return tokenize_loop(make_lex_state(src), new List<Token>());
    }

    public static ParseState make_parse_state(List<Token> toks)
    {
        return new ParseState(toks, 0L);
    }

    public static Token current(ParseState st)
    {
        return st.tokens[(int)st.pos];
    }

    public static TokenKind current_kind(ParseState st)
    {
        return current(st).kind;
    }

    public static ParseState advance(ParseState st)
    {
        return new ParseState(st.tokens, (st.pos + 1L));
    }

    public static bool is_done(ParseState st)
    {
        return (current_kind(st) is EndOfFile _mEndOfFile_ ? true : ((Func<TokenKind, bool>)((_) => false))(current_kind(st)));
    }

    public static TokenKind peek_kind(ParseState st, long offset)
    {
        return st.tokens[(int)(st.pos + offset)].kind;
    }

    public static bool is_ident(TokenKind k)
    {
        return (k is Identifier _mIdentifier_ ? true : ((Func<TokenKind, bool>)((_) => false))(k));
    }

    public static bool is_type_ident(TokenKind k)
    {
        return (k is TypeIdentifier _mTypeIdentifier_ ? true : ((Func<TokenKind, bool>)((_) => false))(k));
    }

    public static bool is_arrow(TokenKind k)
    {
        return (k is Arrow _mArrow_ ? true : ((Func<TokenKind, bool>)((_) => false))(k));
    }

    public static bool is_equals(TokenKind k)
    {
        return (k is Equals_ _mEquals__ ? true : ((Func<TokenKind, bool>)((_) => false))(k));
    }

    public static bool is_colon(TokenKind k)
    {
        return (k is Colon _mColon_ ? true : ((Func<TokenKind, bool>)((_) => false))(k));
    }

    public static bool is_comma(TokenKind k)
    {
        return (k is Comma _mComma_ ? true : ((Func<TokenKind, bool>)((_) => false))(k));
    }

    public static bool is_pipe(TokenKind k)
    {
        return (k is Pipe _mPipe_ ? true : ((Func<TokenKind, bool>)((_) => false))(k));
    }

    public static bool is_dot(TokenKind k)
    {
        return (k is Dot _mDot_ ? true : ((Func<TokenKind, bool>)((_) => false))(k));
    }

    public static bool is_left_paren(TokenKind k)
    {
        return (k is LeftParen _mLeftParen_ ? true : ((Func<TokenKind, bool>)((_) => false))(k));
    }

    public static bool is_left_brace(TokenKind k)
    {
        return (k is LeftBrace _mLeftBrace_ ? true : ((Func<TokenKind, bool>)((_) => false))(k));
    }

    public static bool is_left_bracket(TokenKind k)
    {
        return (k is LeftBracket _mLeftBracket_ ? true : ((Func<TokenKind, bool>)((_) => false))(k));
    }

    public static bool is_right_brace(TokenKind k)
    {
        return (k is RightBrace _mRightBrace_ ? true : ((Func<TokenKind, bool>)((_) => false))(k));
    }

    public static bool is_right_bracket(TokenKind k)
    {
        return (k is RightBracket _mRightBracket_ ? true : ((Func<TokenKind, bool>)((_) => false))(k));
    }

    public static bool is_if_keyword(TokenKind k)
    {
        return (k is IfKeyword _mIfKeyword_ ? true : ((Func<TokenKind, bool>)((_) => false))(k));
    }

    public static bool is_let_keyword(TokenKind k)
    {
        return (k is LetKeyword _mLetKeyword_ ? true : ((Func<TokenKind, bool>)((_) => false))(k));
    }

    public static bool is_when_keyword(TokenKind k)
    {
        return (k is WhenKeyword _mWhenKeyword_ ? true : ((Func<TokenKind, bool>)((_) => false))(k));
    }

    public static bool is_do_keyword(TokenKind k)
    {
        return (k is DoKeyword _mDoKeyword_ ? true : ((Func<TokenKind, bool>)((_) => false))(k));
    }

    public static bool is_in_keyword(TokenKind k)
    {
        return (k is InKeyword _mInKeyword_ ? true : ((Func<TokenKind, bool>)((_) => false))(k));
    }

    public static bool is_minus(TokenKind k)
    {
        return (k is Minus _mMinus_ ? true : ((Func<TokenKind, bool>)((_) => false))(k));
    }

    public static bool is_dedent(TokenKind k)
    {
        return (k is Dedent _mDedent_ ? true : ((Func<TokenKind, bool>)((_) => false))(k));
    }

    public static bool is_left_arrow(TokenKind k)
    {
        return (k is LeftArrow _mLeftArrow_ ? true : ((Func<TokenKind, bool>)((_) => false))(k));
    }

    public static bool is_record_keyword(TokenKind k)
    {
        return (k is RecordKeyword _mRecordKeyword_ ? true : ((Func<TokenKind, bool>)((_) => false))(k));
    }

    public static bool is_underscore(TokenKind k)
    {
        return (k is Underscore _mUnderscore_ ? true : ((Func<TokenKind, bool>)((_) => false))(k));
    }

    public static bool is_literal(TokenKind k)
    {
        return ((Func<TokenKind, bool>)((_scrutinee_) => (_scrutinee_ is IntegerLiteral _mIntegerLiteral_ ? true : (_scrutinee_ is NumberLiteral _mNumberLiteral_ ? true : (_scrutinee_ is TextLiteral _mTextLiteral_ ? true : (_scrutinee_ is TrueKeyword _mTrueKeyword_ ? true : (_scrutinee_ is FalseKeyword _mFalseKeyword_ ? true : ((Func<TokenKind, bool>)((_) => false))(_scrutinee_))))))))(k);
    }

    public static bool is_app_start(TokenKind k)
    {
        return ((Func<TokenKind, bool>)((_scrutinee_) => (_scrutinee_ is Identifier _mIdentifier_ ? true : (_scrutinee_ is TypeIdentifier _mTypeIdentifier_ ? true : (_scrutinee_ is IntegerLiteral _mIntegerLiteral_ ? true : (_scrutinee_ is NumberLiteral _mNumberLiteral_ ? true : (_scrutinee_ is TextLiteral _mTextLiteral_ ? true : (_scrutinee_ is TrueKeyword _mTrueKeyword_ ? true : (_scrutinee_ is FalseKeyword _mFalseKeyword_ ? true : (_scrutinee_ is LeftParen _mLeftParen_ ? true : (_scrutinee_ is LeftBracket _mLeftBracket_ ? true : ((Func<TokenKind, bool>)((_) => false))(_scrutinee_))))))))))))(k);
    }

    public static bool is_compound(Expr e)
    {
        return ((Func<Expr, bool>)((_scrutinee_) => (_scrutinee_ is MatchExpr _mMatchExpr_ ? ((Func<List<MatchArm>, bool>)((arms) => ((Func<Expr, bool>)((s) => true))((Expr)_mMatchExpr_.Field0)))((List<MatchArm>)_mMatchExpr_.Field1) : (_scrutinee_ is IfExpr _mIfExpr_ ? ((Func<Expr, bool>)((el) => ((Func<Expr, bool>)((t) => ((Func<Expr, bool>)((c) => true))((Expr)_mIfExpr_.Field0)))((Expr)_mIfExpr_.Field1)))((Expr)_mIfExpr_.Field2) : (_scrutinee_ is LetExpr _mLetExpr_ ? ((Func<Expr, bool>)((body) => ((Func<List<LetBind>, bool>)((binds) => true))((List<LetBind>)_mLetExpr_.Field0)))((Expr)_mLetExpr_.Field1) : (_scrutinee_ is DoExpr _mDoExpr_ ? ((Func<List<DoStmt>, bool>)((stmts) => true))((List<DoStmt>)_mDoExpr_.Field0) : ((Func<Expr, bool>)((_) => false))(_scrutinee_)))))))(e);
    }

    public static bool is_type_arg_start(TokenKind k)
    {
        return ((Func<TokenKind, bool>)((_scrutinee_) => (_scrutinee_ is TypeIdentifier _mTypeIdentifier_ ? true : (_scrutinee_ is Identifier _mIdentifier_ ? true : (_scrutinee_ is LeftParen _mLeftParen_ ? true : ((Func<TokenKind, bool>)((_) => false))(_scrutinee_))))))(k);
    }

    public static long operator_precedence(TokenKind k)
    {
        return ((Func<TokenKind, long>)((_scrutinee_) => (_scrutinee_ is PlusPlus _mPlusPlus_ ? 5L : (_scrutinee_ is ColonColon _mColonColon_ ? 5L : (_scrutinee_ is Plus _mPlus_ ? 6L : (_scrutinee_ is Minus _mMinus_ ? 6L : (_scrutinee_ is Star _mStar_ ? 7L : (_scrutinee_ is Slash _mSlash_ ? 7L : (_scrutinee_ is Caret _mCaret_ ? 8L : (_scrutinee_ is DoubleEquals _mDoubleEquals_ ? 4L : (_scrutinee_ is NotEquals _mNotEquals_ ? 4L : (_scrutinee_ is LessThan _mLessThan_ ? 4L : (_scrutinee_ is GreaterThan _mGreaterThan_ ? 4L : (_scrutinee_ is LessOrEqual _mLessOrEqual_ ? 4L : (_scrutinee_ is GreaterOrEqual _mGreaterOrEqual_ ? 4L : (_scrutinee_ is TripleEquals _mTripleEquals_ ? 4L : (_scrutinee_ is Ampersand _mAmpersand_ ? 3L : (_scrutinee_ is Pipe _mPipe_ ? 2L : ((Func<TokenKind, long>)((_) => (0L - 1L)))(_scrutinee_)))))))))))))))))))(k);
    }

    public static ParseState expect(TokenKind kind, ParseState st)
    {
        return (is_done(st) ? st : advance(st));
    }

    public static ParseState skip_newlines(ParseState st)
    {
        while (true)
        {
            if (is_done(st))
            {
                return st;
            }
            else
            {
                var _tco_s = current_kind(st);
                if (_tco_s is Newline _tco_m0)
                {
                    var _tco_0 = advance(st);
                    st = _tco_0;
                    continue;
                }
                else if (_tco_s is Indent _tco_m1)
                {
                    var _tco_0 = advance(st);
                    st = _tco_0;
                    continue;
                }
                else if (_tco_s is Dedent _tco_m2)
                {
                    var _tco_0 = advance(st);
                    st = _tco_0;
                    continue;
                }
                {
                    var _ = _tco_s;
                    return st;
                }
            }
        }
    }

    public static ParseTypeResult parse_type(ParseState st)
    {
        return ((Func<ParseTypeResult, ParseTypeResult>)((result) => unwrap_type_ok(result, (_p0_) => (_p1_) => parse_type_continue(_p0_, _p1_))))(parse_type_atom(st));
    }

    public static ParseTypeResult parse_type_continue(TypeExpr left, ParseState st)
    {
        return (is_arrow(current_kind(st)) ? ((Func<ParseState, ParseTypeResult>)((st2) => ((Func<ParseTypeResult, ParseTypeResult>)((right_result) => unwrap_type_ok(right_result, (_p0_) => (_p1_) => make_fun_type(left, _p0_, _p1_))))(parse_type(st2))))(advance(st)) : new TypeOk(left, st));
    }

    public static ParseTypeResult make_fun_type(TypeExpr left, TypeExpr right, ParseState st)
    {
        return new TypeOk(new FunType(left, right), st);
    }

    public static ParseTypeResult unwrap_type_ok(ParseTypeResult r, Func<TypeExpr, Func<ParseState, ParseTypeResult>> f)
    {
        return (r is TypeOk _mTypeOk_ ? ((Func<ParseState, ParseTypeResult>)((st) => ((Func<TypeExpr, ParseTypeResult>)((t) => f(t)(st)))((TypeExpr)_mTypeOk_.Field0)))((ParseState)_mTypeOk_.Field1) : throw new InvalidOperationException("Non-exhaustive match"));
    }

    public static ParseTypeResult parse_type_atom(ParseState st)
    {
        return (is_ident(current_kind(st)) ? ((Func<Token, ParseTypeResult>)((tok) => parse_type_args(new NamedType(tok), advance(st))))(current(st)) : (is_type_ident(current_kind(st)) ? ((Func<Token, ParseTypeResult>)((tok) => parse_type_args(new NamedType(tok), advance(st))))(current(st)) : (is_left_paren(current_kind(st)) ? parse_paren_type(advance(st)) : ((Func<Token, ParseTypeResult>)((tok) => new TypeOk(new NamedType(tok), advance(st))))(current(st)))));
    }

    public static ParseTypeResult parse_paren_type(ParseState st)
    {
        return ((Func<ParseTypeResult, ParseTypeResult>)((inner) => unwrap_type_ok(inner, (_p0_) => (_p1_) => finish_paren_type(_p0_, _p1_))))(parse_type(st));
    }

    public static ParseTypeResult finish_paren_type(TypeExpr t, ParseState st)
    {
        return ((Func<ParseState, ParseTypeResult>)((st2) => new TypeOk(new ParenType(t), st2)))(expect(new RightParen(), st));
    }

    public static ParseTypeResult parse_type_args(TypeExpr base_type, ParseState st)
    {
        return (is_done(st) ? new TypeOk(base_type, st) : (is_type_arg_start(current_kind(st)) ? parse_type_arg_next(base_type, st) : new TypeOk(base_type, st)));
    }

    public static ParseTypeResult parse_type_arg_next(TypeExpr base_type, ParseState st)
    {
        return ((Func<ParseTypeResult, ParseTypeResult>)((arg_result) => unwrap_type_ok(arg_result, (_p0_) => (_p1_) => continue_type_args(base_type, _p0_, _p1_))))(parse_type_atom(st));
    }

    public static ParseTypeResult continue_type_args(TypeExpr base_type, TypeExpr arg, ParseState st)
    {
        return parse_type_args(new AppType(base_type, new List<TypeExpr>() { arg }), st);
    }

    public static ParsePatResult parse_pattern(ParseState st)
    {
        return (is_underscore(current_kind(st)) ? new PatOk(new WildPat(current(st)), advance(st)) : (is_literal(current_kind(st)) ? new PatOk(new LitPat(current(st)), advance(st)) : (is_type_ident(current_kind(st)) ? ((Func<Token, ParsePatResult>)((tok) => parse_ctor_pattern_fields(tok, new List<Pat>(), advance(st))))(current(st)) : (is_ident(current_kind(st)) ? new PatOk(new VarPat(current(st)), advance(st)) : new PatOk(new WildPat(current(st)), advance(st))))));
    }

    public static ParsePatResult parse_ctor_pattern_fields(Token ctor, List<Pat> acc, ParseState st)
    {
        return (is_left_paren(current_kind(st)) ? ((Func<ParseState, ParsePatResult>)((st2) => ((Func<ParsePatResult, ParsePatResult>)((sub) => unwrap_pat_ok(sub, (_p0_) => (_p1_) => continue_ctor_fields(ctor, acc, _p0_, _p1_))))(parse_pattern(st2))))(advance(st)) : new PatOk(new CtorPat(ctor, acc), st));
    }

    public static ParsePatResult continue_ctor_fields(Token ctor, List<Pat> acc, Pat p, ParseState st)
    {
        return ((Func<ParseState, ParsePatResult>)((st2) => parse_ctor_pattern_fields(ctor, Enumerable.Concat(acc, new List<Pat>() { p }).ToList(), st2)))(expect(new RightParen(), st));
    }

    public static ParsePatResult unwrap_pat_ok(ParsePatResult r, Func<Pat, Func<ParseState, ParsePatResult>> f)
    {
        return (r is PatOk _mPatOk_ ? ((Func<ParseState, ParsePatResult>)((st) => ((Func<Pat, ParsePatResult>)((p) => f(p)(st)))((Pat)_mPatOk_.Field0)))((ParseState)_mPatOk_.Field1) : throw new InvalidOperationException("Non-exhaustive match"));
    }

    public static ParseExprResult parse_expr(ParseState st)
    {
        return parse_binary(st, 0L);
    }

    public static ParseExprResult unwrap_expr_ok(ParseExprResult r, Func<Expr, Func<ParseState, ParseExprResult>> f)
    {
        return (r is ExprOk _mExprOk_ ? ((Func<ParseState, ParseExprResult>)((st) => ((Func<Expr, ParseExprResult>)((e) => f(e)(st)))((Expr)_mExprOk_.Field0)))((ParseState)_mExprOk_.Field1) : throw new InvalidOperationException("Non-exhaustive match"));
    }

    public static ParseExprResult parse_binary(ParseState st, long min_prec)
    {
        return ((Func<ParseExprResult, ParseExprResult>)((left_result) => unwrap_expr_ok(left_result, (_p0_) => (_p1_) => start_binary_loop(min_prec, _p0_, _p1_))))(parse_unary(st));
    }

    public static ParseExprResult start_binary_loop(long min_prec, Expr left, ParseState st)
    {
        return parse_binary_loop(left, st, min_prec);
    }

    public static ParseExprResult parse_binary_loop(Expr left, ParseState st, long min_prec)
    {
        return (is_done(st) ? new ExprOk(left, st) : ((Func<long, ParseExprResult>)((prec) => ((prec < min_prec) ? new ExprOk(left, st) : ((Func<Token, ParseExprResult>)((op) => ((Func<ParseState, ParseExprResult>)((st2) => ((Func<ParseExprResult, ParseExprResult>)((right_result) => unwrap_expr_ok(right_result, (_p0_) => (_p1_) => continue_binary(left, op, min_prec, _p0_, _p1_))))(parse_binary(st2, (prec + 1L)))))(skip_newlines(advance(st)))))(current(st)))))(operator_precedence(current_kind(st))));
    }

    public static ParseExprResult continue_binary(Expr left, Token op, long min_prec, Expr right, ParseState st)
    {
        return parse_binary_loop(new BinExpr(left, op, right), st, min_prec);
    }

    public static ParseExprResult parse_unary(ParseState st)
    {
        return (is_minus(current_kind(st)) ? ((Func<Token, ParseExprResult>)((op) => ((Func<ParseExprResult, ParseExprResult>)((result) => unwrap_expr_ok(result, (_p0_) => (_p1_) => finish_unary(op, _p0_, _p1_))))(parse_unary(advance(st)))))(current(st)) : parse_application(st));
    }

    public static ParseExprResult finish_unary(Token op, Expr operand, ParseState st)
    {
        return new ExprOk(new UnaryExpr(op, operand), st);
    }

    public static ParseExprResult parse_application(ParseState st)
    {
        return ((Func<ParseExprResult, ParseExprResult>)((func_result) => unwrap_expr_ok(func_result, (_p0_) => (_p1_) => parse_app_loop(_p0_, _p1_))))(parse_atom(st));
    }

    public static ParseExprResult parse_app_loop(Expr func, ParseState st)
    {
        return (is_compound(func) ? parse_field_access(func, st) : (is_done(st) ? new ExprOk(func, st) : (is_app_start(current_kind(st)) ? ((Func<ParseExprResult, ParseExprResult>)((arg_result) => unwrap_expr_ok(arg_result, (_p0_) => (_p1_) => continue_app(func, _p0_, _p1_))))(parse_atom(st)) : parse_field_access(func, st))));
    }

    public static ParseExprResult continue_app(Expr func, Expr arg, ParseState st)
    {
        return parse_app_loop(new AppExpr(func, arg), st);
    }

    public static ParseExprResult parse_atom(ParseState st)
    {
        return (is_literal(current_kind(st)) ? new ExprOk(new LitExpr(current(st)), advance(st)) : (is_ident(current_kind(st)) ? parse_field_access(new NameExpr(current(st)), advance(st)) : (is_type_ident(current_kind(st)) ? parse_atom_type_ident(st) : (is_left_paren(current_kind(st)) ? parse_paren_expr(advance(st)) : (is_left_bracket(current_kind(st)) ? parse_list_expr(st) : (is_if_keyword(current_kind(st)) ? parse_if_expr(st) : (is_let_keyword(current_kind(st)) ? parse_let_expr(st) : (is_when_keyword(current_kind(st)) ? parse_match_expr(st) : (is_do_keyword(current_kind(st)) ? parse_do_expr(st) : new ExprOk(new ErrExpr(current(st)), advance(st)))))))))));
    }

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

    public static ParseExprResult parse_atom_type_ident(ParseState st)
    {
        return ((Func<Token, ParseExprResult>)((tok) => ((Func<ParseState, ParseExprResult>)((st2) => (is_left_brace(current_kind(st2)) ? parse_record_expr(tok, st2) : new ExprOk(new NameExpr(tok), st2))))(advance(st))))(current(st));
    }

    public static ParseExprResult parse_paren_expr(ParseState st)
    {
        return ((Func<ParseState, ParseExprResult>)((st2) => ((Func<ParseExprResult, ParseExprResult>)((inner) => unwrap_expr_ok(inner, (_p0_) => (_p1_) => finish_paren_expr(_p0_, _p1_))))(parse_expr(st2))))(skip_newlines(st));
    }

    public static ParseExprResult finish_paren_expr(Expr e, ParseState st)
    {
        return ((Func<ParseState, ParseExprResult>)((st2) => ((Func<ParseState, ParseExprResult>)((st3) => new ExprOk(new ParenExpr(e), st3)))(expect(new RightParen(), st2))))(skip_newlines(st));
    }

    public static ParseExprResult parse_record_expr(Token type_name, ParseState st)
    {
        return ((Func<ParseState, ParseExprResult>)((st2) => ((Func<ParseState, ParseExprResult>)((st3) => parse_record_expr_fields(type_name, new List<RecordFieldExpr>(), st3)))(skip_newlines(st2))))(advance(st));
    }

    public static ParseExprResult parse_record_expr_fields(Token type_name, List<RecordFieldExpr> acc, ParseState st)
    {
        return (is_right_brace(current_kind(st)) ? new ExprOk(new RecordExpr(type_name, acc), advance(st)) : (is_ident(current_kind(st)) ? parse_record_field(type_name, acc, st) : new ExprOk(new RecordExpr(type_name, acc), st)));
    }

    public static ParseExprResult parse_record_field(Token type_name, List<RecordFieldExpr> acc, ParseState st)
    {
        return ((Func<Token, ParseExprResult>)((field_name) => ((Func<ParseState, ParseExprResult>)((st2) => ((Func<ParseState, ParseExprResult>)((st3) => ((Func<ParseExprResult, ParseExprResult>)((val_result) => unwrap_expr_ok(val_result, (_p0_) => (_p1_) => finish_record_field(type_name, acc, field_name, _p0_, _p1_))))(parse_expr(st3))))(expect(new Equals_(), st2))))(advance(st))))(current(st));
    }

    public static ParseExprResult finish_record_field(Token type_name, List<RecordFieldExpr> acc, Token field_name, Expr v, ParseState st)
    {
        return ((Func<RecordFieldExpr, ParseExprResult>)((field) => ((Func<ParseState, ParseExprResult>)((st2) => (is_comma(current_kind(st2)) ? parse_record_expr_fields(type_name, Enumerable.Concat(acc, new List<RecordFieldExpr>() { field }).ToList(), skip_newlines(advance(st2))) : parse_record_expr_fields(type_name, Enumerable.Concat(acc, new List<RecordFieldExpr>() { field }).ToList(), st2))))(skip_newlines(st))))(new RecordFieldExpr(field_name, v));
    }

    public static ParseExprResult parse_list_expr(ParseState st)
    {
        return ((Func<ParseState, ParseExprResult>)((st2) => ((Func<ParseState, ParseExprResult>)((st3) => parse_list_elements(new List<Expr>(), st3)))(skip_newlines(st2))))(advance(st));
    }

    public static ParseExprResult parse_list_elements(List<Expr> acc, ParseState st)
    {
        return (is_right_bracket(current_kind(st)) ? new ExprOk(new ListExpr(acc), advance(st)) : ((Func<ParseExprResult, ParseExprResult>)((elem) => unwrap_expr_ok(elem, (_p0_) => (_p1_) => finish_list_element(acc, _p0_, _p1_))))(parse_expr(st)));
    }

    public static ParseExprResult finish_list_element(List<Expr> acc, Expr e, ParseState st)
    {
        return ((Func<ParseState, ParseExprResult>)((st2) => (is_comma(current_kind(st2)) ? parse_list_elements(Enumerable.Concat(acc, new List<Expr>() { e }).ToList(), skip_newlines(advance(st2))) : parse_list_elements(Enumerable.Concat(acc, new List<Expr>() { e }).ToList(), st2))))(skip_newlines(st));
    }

    public static ParseExprResult parse_if_expr(ParseState st)
    {
        return ((Func<ParseState, ParseExprResult>)((st2) => ((Func<ParseExprResult, ParseExprResult>)((cond) => unwrap_expr_ok(cond, (_p0_) => (_p1_) => parse_if_then(_p0_, _p1_))))(parse_expr(st2))))(skip_newlines(advance(st)));
    }

    public static ParseExprResult parse_if_then(Expr c, ParseState st)
    {
        return ((Func<ParseState, ParseExprResult>)((st2) => ((Func<ParseState, ParseExprResult>)((st3) => ((Func<ParseState, ParseExprResult>)((st4) => ((Func<ParseExprResult, ParseExprResult>)((then_result) => unwrap_expr_ok(then_result, (_p0_) => (_p1_) => parse_if_else(c, _p0_, _p1_))))(parse_expr(st4))))(skip_newlines(st3))))(expect(new ThenKeyword(), st2))))(skip_newlines(st));
    }

    public static ParseExprResult parse_if_else(Expr c, Expr t, ParseState st)
    {
        return ((Func<ParseState, ParseExprResult>)((st2) => ((Func<ParseState, ParseExprResult>)((st3) => ((Func<ParseState, ParseExprResult>)((st4) => ((Func<ParseExprResult, ParseExprResult>)((else_result) => unwrap_expr_ok(else_result, (_p0_) => (_p1_) => finish_if(c, t, _p0_, _p1_))))(parse_expr(st4))))(skip_newlines(st3))))(expect(new ElseKeyword(), st2))))(skip_newlines(st));
    }

    public static ParseExprResult finish_if(Expr c, Expr t, Expr e, ParseState st)
    {
        return new ExprOk(new IfExpr(c, t, e), st);
    }

    public static ParseExprResult parse_let_expr(ParseState st)
    {
        return ((Func<ParseState, ParseExprResult>)((st2) => parse_let_bindings(new List<LetBind>(), st2)))(skip_newlines(advance(st)));
    }

    public static ParseExprResult parse_let_bindings(List<LetBind> acc, ParseState st)
    {
        return (is_ident(current_kind(st)) ? parse_let_binding(acc, st) : (is_in_keyword(current_kind(st)) ? ((Func<ParseState, ParseExprResult>)((st2) => ((Func<ParseExprResult, ParseExprResult>)((body) => unwrap_expr_ok(body, (_p0_) => (_p1_) => finish_let(acc, _p0_, _p1_))))(parse_expr(st2))))(skip_newlines(advance(st))) : ((Func<ParseExprResult, ParseExprResult>)((body) => unwrap_expr_ok(body, (_p0_) => (_p1_) => finish_let(acc, _p0_, _p1_))))(parse_expr(st))));
    }

    public static ParseExprResult finish_let(List<LetBind> acc, Expr b, ParseState st)
    {
        return new ExprOk(new LetExpr(acc, b), st);
    }

    public static ParseExprResult parse_let_binding(List<LetBind> acc, ParseState st)
    {
        return ((Func<Token, ParseExprResult>)((name_tok) => ((Func<ParseState, ParseExprResult>)((st2) => ((Func<ParseState, ParseExprResult>)((st3) => ((Func<ParseExprResult, ParseExprResult>)((val_result) => unwrap_expr_ok(val_result, (_p0_) => (_p1_) => finish_let_binding(acc, name_tok, _p0_, _p1_))))(parse_expr(st3))))(expect(new Equals_(), st2))))(advance(st))))(current(st));
    }

    public static ParseExprResult finish_let_binding(List<LetBind> acc, Token name_tok, Expr v, ParseState st)
    {
        return ((Func<LetBind, ParseExprResult>)((binding) => ((Func<ParseState, ParseExprResult>)((st2) => (is_comma(current_kind(st2)) ? parse_let_bindings(Enumerable.Concat(acc, new List<LetBind>() { binding }).ToList(), skip_newlines(advance(st2))) : parse_let_bindings(Enumerable.Concat(acc, new List<LetBind>() { binding }).ToList(), st2))))(skip_newlines(st))))(new LetBind(name_tok, v));
    }

    public static ParseExprResult parse_match_expr(ParseState st)
    {
        return ((Func<ParseState, ParseExprResult>)((st2) => ((Func<ParseExprResult, ParseExprResult>)((scrut) => unwrap_expr_ok(scrut, (_p0_) => (_p1_) => start_match_branches(_p0_, _p1_))))(parse_expr(st2))))(advance(st));
    }

    public static ParseExprResult start_match_branches(Expr s, ParseState st)
    {
        return ((Func<ParseState, ParseExprResult>)((st2) => parse_match_branches(s, new List<MatchArm>(), st2)))(skip_newlines(st));
    }

    public static ParseExprResult parse_match_branches(Expr scrut, List<MatchArm> acc, ParseState st)
    {
        return (is_if_keyword(current_kind(st)) ? parse_one_match_branch(scrut, acc, st) : new ExprOk(new MatchExpr(scrut, acc), st));
    }

    public static ParseExprResult unwrap_pat_for_expr(ParsePatResult r, Func<Pat, Func<ParseState, ParseExprResult>> f)
    {
        return (r is PatOk _mPatOk_ ? ((Func<ParseState, ParseExprResult>)((st) => ((Func<Pat, ParseExprResult>)((p) => f(p)(st)))((Pat)_mPatOk_.Field0)))((ParseState)_mPatOk_.Field1) : throw new InvalidOperationException("Non-exhaustive match"));
    }

    public static ParseExprResult parse_one_match_branch(Expr scrut, List<MatchArm> acc, ParseState st)
    {
        return ((Func<ParseState, ParseExprResult>)((st2) => ((Func<ParsePatResult, ParseExprResult>)((pat) => unwrap_pat_for_expr(pat, (_p0_) => (_p1_) => parse_match_branch_body(scrut, acc, _p0_, _p1_))))(parse_pattern(st2))))(advance(st));
    }

    public static ParseExprResult parse_match_branch_body(Expr scrut, List<MatchArm> acc, Pat p, ParseState st)
    {
        return ((Func<ParseState, ParseExprResult>)((st2) => ((Func<ParseState, ParseExprResult>)((st3) => ((Func<ParseExprResult, ParseExprResult>)((body) => unwrap_expr_ok(body, (_p0_) => (_p1_) => finish_match_branch(scrut, acc, p, _p0_, _p1_))))(parse_expr(st3))))(skip_newlines(st2))))(expect(new Arrow(), st));
    }

    public static ParseExprResult finish_match_branch(Expr scrut, List<MatchArm> acc, Pat p, Expr b, ParseState st)
    {
        return ((Func<MatchArm, ParseExprResult>)((arm) => ((Func<ParseState, ParseExprResult>)((st2) => parse_match_branches(scrut, Enumerable.Concat(acc, new List<MatchArm>() { arm }).ToList(), st2)))(skip_newlines(st))))(new MatchArm(p, b));
    }

    public static ParseExprResult parse_do_expr(ParseState st)
    {
        return ((Func<ParseState, ParseExprResult>)((st2) => parse_do_stmts(new List<DoStmt>(), st2)))(skip_newlines(advance(st)));
    }

    public static ParseExprResult parse_do_stmts(List<DoStmt> acc, ParseState st)
    {
        return (is_done(st) ? new ExprOk(new DoExpr(acc), st) : (is_dedent(current_kind(st)) ? new ExprOk(new DoExpr(acc), st) : (is_do_bind(st) ? parse_do_bind_stmt(acc, st) : parse_do_expr_stmt(acc, st))));
    }

    public static bool is_do_bind(ParseState st)
    {
        return (is_ident(current_kind(st)) ? is_left_arrow(peek_kind(st, 1L)) : false);
    }

    public static ParseExprResult parse_do_bind_stmt(List<DoStmt> acc, ParseState st)
    {
        return ((Func<Token, ParseExprResult>)((name_tok) => ((Func<ParseState, ParseExprResult>)((st2) => ((Func<ParseExprResult, ParseExprResult>)((val_result) => unwrap_expr_ok(val_result, (_p0_) => (_p1_) => finish_do_bind(acc, name_tok, _p0_, _p1_))))(parse_expr(st2))))(advance(advance(st)))))(current(st));
    }

    public static ParseExprResult finish_do_bind(List<DoStmt> acc, Token name_tok, Expr v, ParseState st)
    {
        return ((Func<ParseState, ParseExprResult>)((st2) => parse_do_stmts(Enumerable.Concat(acc, new List<DoStmt>() { new DoBindStmt(name_tok, v) }).ToList(), st2)))(skip_newlines(st));
    }

    public static ParseExprResult parse_do_expr_stmt(List<DoStmt> acc, ParseState st)
    {
        return ((Func<ParseExprResult, ParseExprResult>)((expr_result) => unwrap_expr_ok(expr_result, (_p0_) => (_p1_) => finish_do_expr(acc, _p0_, _p1_))))(parse_expr(st));
    }

    public static ParseExprResult finish_do_expr(List<DoStmt> acc, Expr e, ParseState st)
    {
        return ((Func<ParseState, ParseExprResult>)((st2) => parse_do_stmts(Enumerable.Concat(acc, new List<DoStmt>() { new DoExprStmt(e) }).ToList(), st2)))(skip_newlines(st));
    }

    public static ParseTypeResult parse_type_annotation(ParseState st)
    {
        return ((Func<ParseState, ParseTypeResult>)((st2) => ((Func<ParseState, ParseTypeResult>)((st3) => parse_type(st3)))(expect(new Colon(), st2))))(advance(st));
    }

    public static ParseDefResult parse_definition(ParseState st)
    {
        return (is_done(st) ? new DefNone(st) : (is_ident(current_kind(st)) ? try_parse_def(st) : (is_type_ident(current_kind(st)) ? try_parse_def(st) : new DefNone(st))));
    }

    public static ParseDefResult try_parse_def(ParseState st)
    {
        return (is_colon(peek_kind(st, 1L)) ? ((Func<ParseTypeResult, ParseDefResult>)((ann_result) => unwrap_type_for_def(ann_result)))(parse_type_annotation(st)) : parse_def_body_with_ann(new List<TypeAnn>(), st));
    }

    public static ParseDefResult unwrap_type_for_def(ParseTypeResult r)
    {
        return (r is TypeOk _mTypeOk_ ? ((Func<ParseState, ParseDefResult>)((st) => ((Func<TypeExpr, ParseDefResult>)((ann_type) => ((Func<Token, ParseDefResult>)((name_tok) => ((Func<List<TypeAnn>, ParseDefResult>)((ann) => ((Func<ParseState, ParseDefResult>)((st2) => parse_def_body_with_ann(ann, st2)))(skip_newlines(st))))(new List<TypeAnn>() { new TypeAnn(name_tok, ann_type) })))(new Token(new Identifier(), "", 0L, 0L, 0L))))((TypeExpr)_mTypeOk_.Field0)))((ParseState)_mTypeOk_.Field1) : throw new InvalidOperationException("Non-exhaustive match"));
    }

    public static ParseDefResult parse_def_body_with_ann(List<TypeAnn> ann, ParseState st)
    {
        return ((Func<Token, ParseDefResult>)((name_tok) => ((Func<ParseState, ParseDefResult>)((st2) => parse_def_params_then(ann, name_tok, new List<Token>(), st2)))(advance(st))))(current(st));
    }

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
                    var _tco_2 = Enumerable.Concat(acc, new List<Token>() { param }).ToList();
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

    public static ParseDefResult finish_def(List<TypeAnn> ann, Token name_tok, List<Token> @params, ParseState st)
    {
        return ((Func<ParseState, ParseDefResult>)((st2) => ((Func<ParseState, ParseDefResult>)((st3) => ((Func<ParseExprResult, ParseDefResult>)((body_result) => unwrap_def_body(body_result, ann, name_tok, @params)))(parse_expr(st3))))(skip_newlines(st2))))(expect(new Equals_(), st));
    }

    public static ParseDefResult unwrap_def_body(ParseExprResult r, List<TypeAnn> ann, Token name_tok, List<Token> @params)
    {
        return (r is ExprOk _mExprOk_ ? ((Func<ParseState, ParseDefResult>)((st) => ((Func<Expr, ParseDefResult>)((b) => new DefOk(new Def(name_tok, @params, ann, b), st)))((Expr)_mExprOk_.Field0)))((ParseState)_mExprOk_.Field1) : throw new InvalidOperationException("Non-exhaustive match"));
    }

    public static ParseTypeDefResult parse_type_def(ParseState st)
    {
        return (is_type_ident(current_kind(st)) ? ((Func<Token, ParseTypeDefResult>)((name_tok) => ((Func<ParseState, ParseTypeDefResult>)((st2) => (is_equals(current_kind(st2)) ? ((Func<ParseState, ParseTypeDefResult>)((st3) => (is_record_keyword(current_kind(st3)) ? parse_record_type(name_tok, st3) : (is_pipe(current_kind(st3)) ? parse_variant_type(name_tok, st3) : new TypeDefNone(st)))))(skip_newlines(advance(st2))) : new TypeDefNone(st))))(advance(st))))(current(st)) : new TypeDefNone(st));
    }

    public static ParseTypeDefResult parse_record_type(Token name_tok, ParseState st)
    {
        return ((Func<ParseState, ParseTypeDefResult>)((st2) => ((Func<ParseState, ParseTypeDefResult>)((st3) => ((Func<ParseState, ParseTypeDefResult>)((st4) => parse_record_fields_loop(name_tok, new List<RecordFieldDef>(), st4)))(skip_newlines(st3))))(expect(new LeftBrace(), st2))))(advance(st));
    }

    public static ParseTypeDefResult parse_record_fields_loop(Token name_tok, List<RecordFieldDef> acc, ParseState st)
    {
        return (is_right_brace(current_kind(st)) ? new TypeDefOk(new TypeDef(name_tok, new List<Token>(), new RecordBody(acc)), advance(st)) : (is_ident(current_kind(st)) ? parse_one_record_field(name_tok, acc, st) : new TypeDefOk(new TypeDef(name_tok, new List<Token>(), new RecordBody(acc)), st)));
    }

    public static ParseTypeDefResult parse_one_record_field(Token name_tok, List<RecordFieldDef> acc, ParseState st)
    {
        return ((Func<Token, ParseTypeDefResult>)((field_name) => ((Func<ParseState, ParseTypeDefResult>)((st2) => ((Func<ParseState, ParseTypeDefResult>)((st3) => ((Func<ParseTypeResult, ParseTypeDefResult>)((field_type_result) => unwrap_record_field_type(name_tok, acc, field_name, field_type_result)))(parse_type(st3))))(expect(new Colon(), st2))))(advance(st))))(current(st));
    }

    public static ParseTypeDefResult unwrap_record_field_type(Token name_tok, List<RecordFieldDef> acc, Token field_name, ParseTypeResult r)
    {
        return (r is TypeOk _mTypeOk_ ? ((Func<ParseState, ParseTypeDefResult>)((st) => ((Func<TypeExpr, ParseTypeDefResult>)((ft) => ((Func<RecordFieldDef, ParseTypeDefResult>)((field) => ((Func<ParseState, ParseTypeDefResult>)((st2) => (is_comma(current_kind(st2)) ? parse_record_fields_loop(name_tok, Enumerable.Concat(acc, new List<RecordFieldDef>() { field }).ToList(), skip_newlines(advance(st2))) : parse_record_fields_loop(name_tok, Enumerable.Concat(acc, new List<RecordFieldDef>() { field }).ToList(), st2))))(skip_newlines(st))))(new RecordFieldDef(field_name, ft))))((TypeExpr)_mTypeOk_.Field0)))((ParseState)_mTypeOk_.Field1) : throw new InvalidOperationException("Non-exhaustive match"));
    }

    public static ParseTypeDefResult parse_variant_type(Token name_tok, ParseState st)
    {
        return parse_variant_ctors(name_tok, new List<VariantCtorDef>(), st);
    }

    public static ParseTypeDefResult parse_variant_ctors(Token name_tok, List<VariantCtorDef> acc, ParseState st)
    {
        return (is_pipe(current_kind(st)) ? ((Func<ParseState, ParseTypeDefResult>)((st2) => ((Func<Token, ParseTypeDefResult>)((ctor_name) => ((Func<ParseState, ParseTypeDefResult>)((st3) => parse_ctor_fields(ctor_name, new List<TypeExpr>(), st3, name_tok, acc)))(advance(st2))))(current(st2))))(skip_newlines(advance(st))) : new TypeDefOk(new TypeDef(name_tok, new List<Token>(), new VariantBody(acc)), st));
    }

    public static ParseTypeDefResult parse_ctor_fields(Token ctor_name, List<TypeExpr> fields, ParseState st, Token name_tok, List<VariantCtorDef> acc)
    {
        return (is_left_paren(current_kind(st)) ? ((Func<ParseTypeResult, ParseTypeDefResult>)((field_result) => unwrap_ctor_field(field_result, ctor_name, fields, name_tok, acc)))(parse_type(advance(st))) : ((Func<ParseState, ParseTypeDefResult>)((st2) => ((Func<VariantCtorDef, ParseTypeDefResult>)((ctor) => parse_variant_ctors(name_tok, Enumerable.Concat(acc, new List<VariantCtorDef>() { ctor }).ToList(), st2)))(new VariantCtorDef(ctor_name, fields))))(skip_newlines(st)));
    }

    public static ParseTypeDefResult unwrap_ctor_field(ParseTypeResult r, Token ctor_name, List<TypeExpr> fields, Token name_tok, List<VariantCtorDef> acc)
    {
        return (r is TypeOk _mTypeOk_ ? ((Func<ParseState, ParseTypeDefResult>)((st) => ((Func<TypeExpr, ParseTypeDefResult>)((ty) => ((Func<ParseState, ParseTypeDefResult>)((st2) => parse_ctor_fields(ctor_name, Enumerable.Concat(fields, new List<TypeExpr>() { ty }).ToList(), st2, name_tok, acc)))(expect(new RightParen(), st))))((TypeExpr)_mTypeOk_.Field0)))((ParseState)_mTypeOk_.Field1) : throw new InvalidOperationException("Non-exhaustive match"));
    }

    public static Document parse_document(ParseState st)
    {
        return ((Func<ParseState, Document>)((st2) => parse_top_level(new List<Def>(), new List<TypeDef>(), st2)))(skip_newlines(st));
    }

    public static Document parse_top_level(List<Def> defs, List<TypeDef> type_defs, ParseState st)
    {
        return (is_done(st) ? new Document(defs, type_defs) : try_top_level_type_def(defs, type_defs, st));
    }

    public static Document try_top_level_type_def(List<Def> defs, List<TypeDef> type_defs, ParseState st)
    {
        return ((Func<ParseTypeDefResult, Document>)((td_result) => ((Func<ParseTypeDefResult, Document>)((_scrutinee_) => (_scrutinee_ is TypeDefOk _mTypeDefOk_ ? ((Func<ParseState, Document>)((st2) => ((Func<TypeDef, Document>)((td) => parse_top_level(defs, Enumerable.Concat(type_defs, new List<TypeDef>() { td }).ToList(), skip_newlines(st2))))((TypeDef)_mTypeDefOk_.Field0)))((ParseState)_mTypeDefOk_.Field1) : (_scrutinee_ is TypeDefNone _mTypeDefNone_ ? ((Func<ParseState, Document>)((st2) => try_top_level_def(defs, type_defs, st)))((ParseState)_mTypeDefNone_.Field0) : throw new InvalidOperationException("Non-exhaustive match")))))(td_result)))(parse_type_def(st));
    }

    public static Document try_top_level_def(List<Def> defs, List<TypeDef> type_defs, ParseState st)
    {
        return ((Func<ParseDefResult, Document>)((def_result) => ((Func<ParseDefResult, Document>)((_scrutinee_) => (_scrutinee_ is DefOk _mDefOk_ ? ((Func<ParseState, Document>)((st2) => ((Func<Def, Document>)((d) => parse_top_level(Enumerable.Concat(defs, new List<Def>() { d }).ToList(), type_defs, skip_newlines(st2))))((Def)_mDefOk_.Field0)))((ParseState)_mDefOk_.Field1) : (_scrutinee_ is DefNone _mDefNone_ ? ((Func<ParseState, Document>)((st2) => parse_top_level(defs, type_defs, skip_newlines(advance(st2)))))((ParseState)_mDefNone_.Field0) : throw new InvalidOperationException("Non-exhaustive match")))))(def_result)))(parse_definition(st));
    }

    public static long token_length(Token t)
    {
        return ((long)t.text.Length);
    }

    public static CheckResult infer_literal(UnificationState st, LiteralKind kind)
    {
        return ((Func<LiteralKind, CheckResult>)((_scrutinee_) => (_scrutinee_ is IntLit _mIntLit_ ? new CheckResult(new IntegerTy(), st) : (_scrutinee_ is NumLit _mNumLit_ ? new CheckResult(new NumberTy(), st) : (_scrutinee_ is TextLit _mTextLit_ ? new CheckResult(new TextTy(), st) : (_scrutinee_ is BoolLit _mBoolLit_ ? new CheckResult(new BooleanTy(), st) : throw new InvalidOperationException("Non-exhaustive match")))))))(kind);
    }

    public static CheckResult infer_name(UnificationState st, TypeEnv env, string name)
    {
        return (env_has(env, name) ? new CheckResult(env_lookup(env, name), st) : new CheckResult(new ErrorTy(), add_unify_error(st, "CDX2002", string.Concat("Unknown name: ", name))));
    }

    public static CheckResult infer_binary(UnificationState st, TypeEnv env, AExpr left, BinaryOp op, AExpr right)
    {
        return ((Func<CheckResult, CheckResult>)((lr) => ((Func<CheckResult, CheckResult>)((rr) => infer_binary_op(rr.state, lr.inferred_type, rr.inferred_type, op)))(infer_expr(lr.state, env, right))))(infer_expr(st, env, left));
    }

    public static CheckResult infer_binary_op(UnificationState st, CodexType lt, CodexType rt, BinaryOp op)
    {
        return ((Func<BinaryOp, CheckResult>)((_scrutinee_) => (_scrutinee_ is OpAdd _mOpAdd_ ? infer_arithmetic(st, lt, rt) : (_scrutinee_ is OpSub _mOpSub_ ? infer_arithmetic(st, lt, rt) : (_scrutinee_ is OpMul _mOpMul_ ? infer_arithmetic(st, lt, rt) : (_scrutinee_ is OpDiv _mOpDiv_ ? infer_arithmetic(st, lt, rt) : (_scrutinee_ is OpPow _mOpPow_ ? infer_arithmetic(st, lt, rt) : (_scrutinee_ is OpEq _mOpEq_ ? infer_comparison(st, lt, rt) : (_scrutinee_ is OpNotEq _mOpNotEq_ ? infer_comparison(st, lt, rt) : (_scrutinee_ is OpLt _mOpLt_ ? infer_comparison(st, lt, rt) : (_scrutinee_ is OpGt _mOpGt_ ? infer_comparison(st, lt, rt) : (_scrutinee_ is OpLtEq _mOpLtEq_ ? infer_comparison(st, lt, rt) : (_scrutinee_ is OpGtEq _mOpGtEq_ ? infer_comparison(st, lt, rt) : (_scrutinee_ is OpAnd _mOpAnd_ ? infer_logical(st, lt, rt) : (_scrutinee_ is OpOr _mOpOr_ ? infer_logical(st, lt, rt) : (_scrutinee_ is OpAppend _mOpAppend_ ? infer_append(st, lt, rt) : (_scrutinee_ is OpCons _mOpCons_ ? infer_cons(st, lt, rt) : (_scrutinee_ is OpDefEq _mOpDefEq_ ? infer_comparison(st, lt, rt) : throw new InvalidOperationException("Non-exhaustive match")))))))))))))))))))(op);
    }

    public static CheckResult infer_arithmetic(UnificationState st, CodexType lt, CodexType rt)
    {
        return ((Func<UnifyResult, CheckResult>)((r) => new CheckResult(lt, r.state)))(unify(st, lt, rt));
    }

    public static CheckResult infer_comparison(UnificationState st, CodexType lt, CodexType rt)
    {
        return ((Func<UnifyResult, CheckResult>)((r) => new CheckResult(new BooleanTy(), r.state)))(unify(st, lt, rt));
    }

    public static CheckResult infer_logical(UnificationState st, CodexType lt, CodexType rt)
    {
        return ((Func<UnifyResult, CheckResult>)((r1) => ((Func<UnifyResult, CheckResult>)((r2) => new CheckResult(new BooleanTy(), r2.state)))(unify(r1.state, rt, new BooleanTy()))))(unify(st, lt, new BooleanTy()));
    }

    public static CheckResult infer_append(UnificationState st, CodexType lt, CodexType rt)
    {
        return ((Func<CodexType, CheckResult>)((resolved) => (resolved is TextTy _mTextTy_ ? ((Func<UnifyResult, CheckResult>)((r) => new CheckResult(new TextTy(), r.state)))(unify(st, rt, new TextTy())) : ((Func<CodexType, CheckResult>)((_) => ((Func<UnifyResult, CheckResult>)((r) => new CheckResult(lt, r.state)))(unify(st, lt, rt))))(resolved))))(resolve(st, lt));
    }

    public static CheckResult infer_cons(UnificationState st, CodexType lt, CodexType rt)
    {
        return ((Func<CodexType, CheckResult>)((list_ty) => ((Func<UnifyResult, CheckResult>)((r) => new CheckResult(list_ty, r.state)))(unify(st, rt, list_ty))))(new ListTy(lt));
    }

    public static CheckResult infer_if(UnificationState st, TypeEnv env, AExpr cond, AExpr then_e, AExpr else_e)
    {
        return ((Func<CheckResult, CheckResult>)((cr) => ((Func<UnifyResult, CheckResult>)((r1) => ((Func<CheckResult, CheckResult>)((tr) => ((Func<CheckResult, CheckResult>)((er) => ((Func<UnifyResult, CheckResult>)((r2) => new CheckResult(tr.inferred_type, r2.state)))(unify(er.state, tr.inferred_type, er.inferred_type))))(infer_expr(tr.state, env, else_e))))(infer_expr(r1.state, env, then_e))))(unify(cr.state, cr.inferred_type, new BooleanTy()))))(infer_expr(st, env, cond));
    }

    public static CheckResult infer_let(UnificationState st, TypeEnv env, List<ALetBind> bindings, AExpr body)
    {
        return ((Func<LetBindResult, CheckResult>)((env2) => infer_expr(env2.state, env2.env, body)))(infer_let_bindings(st, env, bindings, 0L, ((long)bindings.Count)));
    }

    public static LetBindResult infer_let_bindings(UnificationState st, TypeEnv env, List<ALetBind> bindings, long i, long len)
    {
        while (true)
        {
            if ((i == len))
            {
                return new LetBindResult(st, env);
            }
            else
            {
                var b = bindings[(int)i];
                var vr = infer_expr(st, env, b.value);
                var env2 = env_bind(env, b.name.value, vr.inferred_type);
                var _tco_0 = vr.state;
                var _tco_1 = env2;
                var _tco_2 = bindings;
                var _tco_3 = (i + 1L);
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

    public static CheckResult infer_lambda(UnificationState st, TypeEnv env, List<Name> @params, AExpr body)
    {
        return ((Func<LambdaBindResult, CheckResult>)((pr) => ((Func<CheckResult, CheckResult>)((br) => ((Func<CodexType, CheckResult>)((fun_ty) => new CheckResult(fun_ty, br.state)))(wrap_fun_type(pr.param_types, br.inferred_type))))(infer_expr(pr.state, pr.env, body))))(bind_lambda_params(st, env, @params, 0L, ((long)@params.Count), new List<CodexType>()));
    }

    public static LambdaBindResult bind_lambda_params(UnificationState st, TypeEnv env, List<Name> @params, long i, long len, List<CodexType> acc)
    {
        while (true)
        {
            if ((i == len))
            {
                return new LambdaBindResult(st, env, acc);
            }
            else
            {
                var p = @params[(int)i];
                var fr = fresh_and_advance(st);
                var env2 = env_bind(env, p.value, fr.var_type);
                var _tco_0 = fr.state;
                var _tco_1 = env2;
                var _tco_2 = @params;
                var _tco_3 = (i + 1L);
                var _tco_4 = len;
                var _tco_5 = Enumerable.Concat(acc, new List<CodexType>() { fr.var_type }).ToList();
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

    public static CodexType wrap_fun_type(List<CodexType> param_types, CodexType result)
    {
        return wrap_fun_type_loop(param_types, result, (((long)param_types.Count) - 1L));
    }

    public static CodexType wrap_fun_type_loop(List<CodexType> param_types, CodexType result, long i)
    {
        while (true)
        {
            if ((i < 0L))
            {
                return result;
            }
            else
            {
                var _tco_0 = param_types;
                var _tco_1 = new FunTy(param_types[(int)i], result);
                var _tco_2 = (i - 1L);
                param_types = _tco_0;
                result = _tco_1;
                i = _tco_2;
                continue;
            }
        }
    }

    public static CheckResult infer_application(UnificationState st, TypeEnv env, AExpr func, AExpr arg)
    {
        return ((Func<CheckResult, CheckResult>)((fr) => ((Func<CheckResult, CheckResult>)((ar) => ((Func<FreshResult, CheckResult>)((ret) => ((Func<UnifyResult, CheckResult>)((r) => new CheckResult(ret.var_type, r.state)))(unify(ret.state, fr.inferred_type, new FunTy(ar.inferred_type, ret.var_type)))))(fresh_and_advance(ar.state))))(infer_expr(fr.state, env, arg))))(infer_expr(st, env, func));
    }

    public static CheckResult infer_list(UnificationState st, TypeEnv env, List<AExpr> elems)
    {
        return ((((long)elems.Count) == 0L) ? ((Func<FreshResult, CheckResult>)((fr) => new CheckResult(new ListTy(fr.var_type), fr.state)))(fresh_and_advance(st)) : ((Func<CheckResult, CheckResult>)((first) => ((Func<UnificationState, CheckResult>)((st2) => new CheckResult(new ListTy(first.inferred_type), st2)))(unify_list_elems(first.state, env, elems, first.inferred_type, 1L, ((long)elems.Count)))))(infer_expr(st, env, elems[(int)0L])));
    }

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
                var _tco_4 = (i + 1L);
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

    public static CheckResult infer_match(UnificationState st, TypeEnv env, AExpr scrutinee, List<AMatchArm> arms)
    {
        return ((Func<CheckResult, CheckResult>)((sr) => ((Func<FreshResult, CheckResult>)((fr) => ((Func<UnificationState, CheckResult>)((st2) => new CheckResult(fr.var_type, st2)))(infer_match_arms(fr.state, env, sr.inferred_type, fr.var_type, arms, 0L, ((long)arms.Count)))))(fresh_and_advance(sr.state))))(infer_expr(st, env, scrutinee));
    }

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
                var env2 = bind_pattern(env, arm.pattern, scrut_ty);
                var br = infer_expr(st, env2, arm.body);
                var r = unify(br.state, br.inferred_type, result_ty);
                var _tco_0 = r.state;
                var _tco_1 = env;
                var _tco_2 = scrut_ty;
                var _tco_3 = result_ty;
                var _tco_4 = arms;
                var _tco_5 = (i + 1L);
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

    public static TypeEnv bind_pattern(TypeEnv env, APat pat, CodexType ty)
    {
        return ((Func<APat, TypeEnv>)((_scrutinee_) => (_scrutinee_ is AVarPat _mAVarPat_ ? ((Func<Name, TypeEnv>)((name) => env_bind(env, name.value, ty)))((Name)_mAVarPat_.Field0) : (_scrutinee_ is AWildPat _mAWildPat_ ? env : ((Func<APat, TypeEnv>)((_) => env))(_scrutinee_)))))(pat);
    }

    public static CheckResult infer_do(UnificationState st, TypeEnv env, List<ADoStmt> stmts)
    {
        return infer_do_loop(st, env, stmts, 0L, ((long)stmts.Count), new NothingTy());
    }

    public static CheckResult infer_do_loop(UnificationState st, TypeEnv env, List<ADoStmt> stmts, long i, long len, CodexType last_ty)
    {
        while (true)
        {
            if ((i == len))
            {
                return new CheckResult(last_ty, st);
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
                    var _tco_3 = (i + 1L);
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
                    var _tco_3 = (i + 1L);
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

    public static CheckResult infer_expr(UnificationState st, TypeEnv env, AExpr expr)
    {
        return ((Func<AExpr, CheckResult>)((_scrutinee_) => (_scrutinee_ is ALitExpr _mALitExpr_ ? ((Func<LiteralKind, CheckResult>)((kind) => ((Func<string, CheckResult>)((val) => infer_literal(st, kind)))((string)_mALitExpr_.Field0)))((LiteralKind)_mALitExpr_.Field1) : (_scrutinee_ is ANameExpr _mANameExpr_ ? ((Func<Name, CheckResult>)((name) => infer_name(st, env, name.value)))((Name)_mANameExpr_.Field0) : (_scrutinee_ is ABinaryExpr _mABinaryExpr_ ? ((Func<AExpr, CheckResult>)((right) => ((Func<BinaryOp, CheckResult>)((op) => ((Func<AExpr, CheckResult>)((left) => infer_binary(st, env, left, op, right)))((AExpr)_mABinaryExpr_.Field0)))((BinaryOp)_mABinaryExpr_.Field1)))((AExpr)_mABinaryExpr_.Field2) : (_scrutinee_ is AUnaryExpr _mAUnaryExpr_ ? ((Func<AExpr, CheckResult>)((operand) => ((Func<CheckResult, CheckResult>)((r) => ((Func<UnifyResult, CheckResult>)((u) => new CheckResult(new IntegerTy(), u.state)))(unify(r.state, r.inferred_type, new IntegerTy()))))(infer_expr(st, env, operand))))((AExpr)_mAUnaryExpr_.Field0) : (_scrutinee_ is AApplyExpr _mAApplyExpr_ ? ((Func<AExpr, CheckResult>)((arg) => ((Func<AExpr, CheckResult>)((func) => infer_application(st, env, func, arg)))((AExpr)_mAApplyExpr_.Field0)))((AExpr)_mAApplyExpr_.Field1) : (_scrutinee_ is AIfExpr _mAIfExpr_ ? ((Func<AExpr, CheckResult>)((else_e) => ((Func<AExpr, CheckResult>)((then_e) => ((Func<AExpr, CheckResult>)((cond) => infer_if(st, env, cond, then_e, else_e)))((AExpr)_mAIfExpr_.Field0)))((AExpr)_mAIfExpr_.Field1)))((AExpr)_mAIfExpr_.Field2) : (_scrutinee_ is ALetExpr _mALetExpr_ ? ((Func<AExpr, CheckResult>)((body) => ((Func<List<ALetBind>, CheckResult>)((bindings) => infer_let(st, env, bindings, body)))((List<ALetBind>)_mALetExpr_.Field0)))((AExpr)_mALetExpr_.Field1) : (_scrutinee_ is ALambdaExpr _mALambdaExpr_ ? ((Func<AExpr, CheckResult>)((body) => ((Func<List<Name>, CheckResult>)((@params) => infer_lambda(st, env, @params, body)))((List<Name>)_mALambdaExpr_.Field0)))((AExpr)_mALambdaExpr_.Field1) : (_scrutinee_ is AMatchExpr _mAMatchExpr_ ? ((Func<List<AMatchArm>, CheckResult>)((arms) => ((Func<AExpr, CheckResult>)((scrutinee) => infer_match(st, env, scrutinee, arms)))((AExpr)_mAMatchExpr_.Field0)))((List<AMatchArm>)_mAMatchExpr_.Field1) : (_scrutinee_ is AListExpr _mAListExpr_ ? ((Func<List<AExpr>, CheckResult>)((elems) => infer_list(st, env, elems)))((List<AExpr>)_mAListExpr_.Field0) : (_scrutinee_ is ADoExpr _mADoExpr_ ? ((Func<List<ADoStmt>, CheckResult>)((stmts) => infer_do(st, env, stmts)))((List<ADoStmt>)_mADoExpr_.Field0) : (_scrutinee_ is AFieldAccess _mAFieldAccess_ ? ((Func<Name, CheckResult>)((field) => ((Func<AExpr, CheckResult>)((obj) => ((Func<CheckResult, CheckResult>)((r) => new CheckResult(new ErrorTy(), r.state)))(infer_expr(st, env, obj))))((AExpr)_mAFieldAccess_.Field0)))((Name)_mAFieldAccess_.Field1) : (_scrutinee_ is ARecordExpr _mARecordExpr_ ? ((Func<List<AFieldExpr>, CheckResult>)((fields) => ((Func<Name, CheckResult>)((name) => new CheckResult(new ErrorTy(), st)))((Name)_mARecordExpr_.Field0)))((List<AFieldExpr>)_mARecordExpr_.Field1) : (_scrutinee_ is AErrorExpr _mAErrorExpr_ ? ((Func<string, CheckResult>)((msg) => new CheckResult(new ErrorTy(), st)))((string)_mAErrorExpr_.Field0) : throw new InvalidOperationException("Non-exhaustive match")))))))))))))))))(expr);
    }

    public static CodexType resolve_type_expr(ATypeExpr texpr)
    {
        while (true)
        {
            var _tco_s = texpr;
            if (_tco_s is ANamedType _tco_m0)
            {
                var name = _tco_m0.Field0;
                return resolve_type_name(name.value);
            }
            else if (_tco_s is AFunType _tco_m1)
            {
                var param = _tco_m1.Field0;
                var ret = _tco_m1.Field1;
                return new FunTy(resolve_type_expr(param), resolve_type_expr(ret));
            }
            else if (_tco_s is AAppType _tco_m2)
            {
                var ctor = _tco_m2.Field0;
                var args = _tco_m2.Field1;
                var _tco_0 = ctor;
                texpr = _tco_0;
                continue;
            }
        }
    }

    public static CodexType resolve_type_name(string name)
    {
        return ((name == "Integer") ? new IntegerTy() : ((name == "Number") ? new NumberTy() : ((name == "Text") ? new TextTy() : ((name == "Boolean") ? new BooleanTy() : ((name == "Nothing") ? new NothingTy() : new ConstructedTy(new Name(name), new List<CodexType>()))))));
    }

    public static CheckResult check_def(UnificationState st, TypeEnv env, ADef def)
    {
        return ((Func<DefSetup, CheckResult>)((declared) => ((Func<DefParamResult, CheckResult>)((env2) => ((Func<CheckResult, CheckResult>)((body_r) => ((Func<UnifyResult, CheckResult>)((u) => new CheckResult(declared.expected_type, u.state)))(unify(body_r.state, env2.remaining_type, body_r.inferred_type))))(infer_expr(env2.state, env2.env, def.body))))(bind_def_params(declared.state, declared.env, def.@params, declared.expected_type, 0L, ((long)def.@params.Count)))))(resolve_declared_type(st, def));
    }

    public static DefSetup resolve_declared_type(UnificationState st, ADef def)
    {
        return ((((long)def.declared_type.Count) == 0L) ? ((Func<FreshResult, DefSetup>)((fr) => new DefSetup(fr.var_type, fr.var_type, fr.state, builtin_type_env())))(fresh_and_advance(st)) : ((Func<CodexType, DefSetup>)((ty) => new DefSetup(ty, ty, st, builtin_type_env())))(resolve_type_expr(def.declared_type[(int)0L])));
    }

    public static DefParamResult bind_def_params(UnificationState st, TypeEnv env, List<AParam> @params, CodexType remaining, long i, long len)
    {
        while (true)
        {
            if ((i == len))
            {
                return new DefParamResult(st, env, remaining);
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
                    var _tco_4 = (i + 1L);
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
                    var _ = _tco_s;
                    var fr = fresh_and_advance(st);
                    var env2 = env_bind(env, p.name.value, fr.var_type);
                    var _tco_0 = fr.state;
                    var _tco_1 = env2;
                    var _tco_2 = @params;
                    var _tco_3 = remaining;
                    var _tco_4 = (i + 1L);
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

    public static ModuleResult check_module(AModule mod)
    {
        return ((Func<LetBindResult, ModuleResult>)((env) => check_all_defs(env.state, env.env, mod.defs, 0L, ((long)mod.defs.Count), new List<TypeBinding>())))(register_all_defs(empty_unification_state(), builtin_type_env(), mod.defs, 0L, ((long)mod.defs.Count)));
    }

    public static LetBindResult register_all_defs(UnificationState st, TypeEnv env, List<ADef> defs, long i, long len)
    {
        while (true)
        {
            if ((i == len))
            {
                return new LetBindResult(st, env);
            }
            else
            {
                var def = defs[(int)i];
                var ty = ((((long)def.declared_type.Count) == 0L) ? ((Func<FreshResult, LetBindResult>)((fr) => ((Func<TypeEnv, LetBindResult>)((env2) => new LetBindResult(fr.state, env2)))(env_bind(env, def.name.value, fr.var_type))))(fresh_and_advance(st)) : ((Func<CodexType, LetBindResult>)((resolved) => new LetBindResult(st, env_bind(env, def.name.value, resolved))))(resolve_type_expr(def.declared_type[(int)0L])));
                var _tco_0 = ty.state;
                var _tco_1 = ty.env;
                var _tco_2 = defs;
                var _tco_3 = (i + 1L);
                var _tco_4 = len;
                st = _tco_0;
                env = _tco_1;
                defs = _tco_2;
                i = _tco_3;
                len = _tco_4;
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
                return new ModuleResult(acc, st);
            }
            else
            {
                var def = defs[(int)i];
                var r = check_def(st, env, def);
                var resolved = deep_resolve(r.state, r.inferred_type);
                var entry = new TypeBinding(def.name.value, resolved);
                var _tco_0 = r.state;
                var _tco_1 = env;
                var _tco_2 = defs;
                var _tco_3 = (i + 1L);
                var _tco_4 = len;
                var _tco_5 = Enumerable.Concat(acc, new List<TypeBinding>() { entry }).ToList();
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

    public static TypeEnv empty_type_env()
    {
        return new TypeEnv(new List<TypeBinding>());
    }

    public static CodexType env_lookup(TypeEnv env, string name)
    {
        return env_lookup_loop(env.bindings, name, 0L, ((long)env.bindings.Count));
    }

    public static CodexType env_lookup_loop(List<TypeBinding> bindings, string name, long i, long len)
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
                    var _tco_2 = (i + 1L);
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

    public static bool env_has(TypeEnv env, string name)
    {
        return env_has_loop(env.bindings, name, 0L, ((long)env.bindings.Count));
    }

    public static bool env_has_loop(List<TypeBinding> bindings, string name, long i, long len)
    {
        while (true)
        {
            if ((i == len))
            {
                return false;
            }
            else
            {
                var b = bindings[(int)i];
                if ((b.name == name))
                {
                    return true;
                }
                else
                {
                    var _tco_0 = bindings;
                    var _tco_1 = name;
                    var _tco_2 = (i + 1L);
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

    public static TypeEnv env_bind(TypeEnv env, string name, CodexType ty)
    {
        return new TypeEnv(Enumerable.Concat(new List<TypeBinding>() { new TypeBinding(name, ty) }, env.bindings).ToList());
    }

    public static TypeEnv builtin_type_env()
    {
        return ((Func<TypeEnv, TypeEnv>)((e) => ((Func<TypeEnv, TypeEnv>)((e2) => ((Func<TypeEnv, TypeEnv>)((e3) => ((Func<TypeEnv, TypeEnv>)((e4) => ((Func<TypeEnv, TypeEnv>)((e5) => ((Func<TypeEnv, TypeEnv>)((e6) => ((Func<TypeEnv, TypeEnv>)((e7) => ((Func<TypeEnv, TypeEnv>)((e8) => ((Func<TypeEnv, TypeEnv>)((e9) => ((Func<TypeEnv, TypeEnv>)((e10) => ((Func<TypeEnv, TypeEnv>)((e11) => ((Func<TypeEnv, TypeEnv>)((e12) => ((Func<TypeEnv, TypeEnv>)((e13) => e13))(env_bind(e12, "text-to-integer", new FunTy(new TextTy(), new IntegerTy())))))(env_bind(e11, "text-replace", new FunTy(new TextTy(), new FunTy(new TextTy(), new FunTy(new TextTy(), new TextTy())))))))(env_bind(e10, "code-to-char", new FunTy(new IntegerTy(), new TextTy())))))(env_bind(e9, "char-code", new FunTy(new TextTy(), new IntegerTy())))))(env_bind(e8, "is-whitespace", new FunTy(new TextTy(), new BooleanTy())))))(env_bind(e7, "is-digit", new FunTy(new TextTy(), new BooleanTy())))))(env_bind(e6, "is-letter", new FunTy(new TextTy(), new BooleanTy())))))(env_bind(e5, "substring", new FunTy(new TextTy(), new FunTy(new IntegerTy(), new FunTy(new IntegerTy(), new TextTy())))))))(env_bind(e4, "char-at", new FunTy(new TextTy(), new FunTy(new IntegerTy(), new TextTy()))))))(env_bind(e3, "integer-to-text", new FunTy(new IntegerTy(), new TextTy())))))(env_bind(e2, "text-length", new FunTy(new TextTy(), new IntegerTy())))))(env_bind(e, "negate", new FunTy(new IntegerTy(), new IntegerTy())))))(empty_type_env());
    }

    public static UnificationState empty_unification_state()
    {
        return new UnificationState(new List<SubstEntry>(), 0L, new List<Diagnostic>());
    }

    public static CodexType fresh_var(UnificationState st)
    {
        return new TypeVar(st.next_id);
    }

    public static UnificationState advance_id(UnificationState st)
    {
        return new UnificationState(st.substitutions, (st.next_id + 1L), st.errors);
    }

    public static FreshResult fresh_and_advance(UnificationState st)
    {
        return new FreshResult(new TypeVar(st.next_id), advance_id(st));
    }

    public static CodexType subst_lookup(long var_id, List<SubstEntry> entries)
    {
        return subst_lookup_loop(var_id, entries, 0L, ((long)entries.Count));
    }

    public static CodexType subst_lookup_loop(long var_id, List<SubstEntry> entries, long i, long len)
    {
        while (true)
        {
            if ((i == len))
            {
                return new ErrorTy();
            }
            else
            {
                var entry = entries[(int)i];
                if ((entry.var_id == var_id))
                {
                    return entry.resolved_type;
                }
                else
                {
                    var _tco_0 = var_id;
                    var _tco_1 = entries;
                    var _tco_2 = (i + 1L);
                    var _tco_3 = len;
                    var_id = _tco_0;
                    entries = _tco_1;
                    i = _tco_2;
                    len = _tco_3;
                    continue;
                }
            }
        }
    }

    public static bool has_subst(long var_id, List<SubstEntry> entries)
    {
        return has_subst_loop(var_id, entries, 0L, ((long)entries.Count));
    }

    public static bool has_subst_loop(long var_id, List<SubstEntry> entries, long i, long len)
    {
        while (true)
        {
            if ((i == len))
            {
                return false;
            }
            else
            {
                var entry = entries[(int)i];
                if ((entry.var_id == var_id))
                {
                    return true;
                }
                else
                {
                    var _tco_0 = var_id;
                    var _tco_1 = entries;
                    var _tco_2 = (i + 1L);
                    var _tco_3 = len;
                    var_id = _tco_0;
                    entries = _tco_1;
                    i = _tco_2;
                    len = _tco_3;
                    continue;
                }
            }
        }
    }

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
                var _ = _tco_s;
                return ty;
            }
        }
    }

    public static UnificationState add_subst(UnificationState st, long var_id, CodexType ty)
    {
        return new UnificationState(Enumerable.Concat(st.substitutions, new List<SubstEntry>() { new SubstEntry(var_id, ty) }).ToList(), st.next_id, st.errors);
    }

    public static UnificationState add_unify_error(UnificationState st, string code, string msg)
    {
        return new UnificationState(st.substitutions, st.next_id, Enumerable.Concat(st.errors, new List<Diagnostic>() { make_error(code, msg) }).ToList());
    }

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
                var _ = _tco_s;
                return false;
            }
        }
    }

    public static UnifyResult unify(UnificationState st, CodexType a, CodexType b)
    {
        return ((Func<CodexType, UnifyResult>)((ra) => ((Func<CodexType, UnifyResult>)((rb) => unify_resolved(st, ra, rb)))(resolve(st, b))))(resolve(st, a));
    }

    public static UnifyResult unify_resolved(UnificationState st, CodexType a, CodexType b)
    {
        return (a is TypeVar _mTypeVar_ ? ((Func<long, UnifyResult>)((id_a) => (occurs_in(st, id_a, b) ? new UnifyResult(false, add_unify_error(st, "CDX2010", "Infinite type")) : new UnifyResult(true, add_subst(st, id_a, b)))))((long)_mTypeVar_.Field0) : ((Func<CodexType, UnifyResult>)((_) => unify_rhs(st, a, b)))(a));
    }

    public static UnifyResult unify_rhs(UnificationState st, CodexType a, CodexType b)
    {
        return (b is TypeVar _mTypeVar_ ? ((Func<long, UnifyResult>)((id_b) => (occurs_in(st, id_b, a) ? new UnifyResult(false, add_unify_error(st, "CDX2010", "Infinite type")) : new UnifyResult(true, add_subst(st, id_b, a)))))((long)_mTypeVar_.Field0) : ((Func<CodexType, UnifyResult>)((_) => unify_structural(st, a, b)))(b));
    }

    public static UnifyResult unify_structural(UnificationState st, CodexType a, CodexType b)
    {
        return (a is IntegerTy _mIntegerTy_ ? ((Func<CodexType, UnifyResult>)((_scrutinee_) => (_scrutinee_ is IntegerTy _mIntegerTy_ ? new UnifyResult(true, st) : ((Func<CodexType, UnifyResult>)((_) => unify_mismatch(st, a, b)))(_scrutinee_))))(b) : throw new InvalidOperationException("Non-exhaustive match"));
    }

    public static UnifyResult unify_fun(UnificationState st, CodexType pa, CodexType ra, CodexType pb, CodexType rb)
    {
        return ((Func<UnifyResult, UnifyResult>)((r1) => (r1.success ? unify(r1.state, ra, rb) : r1)))(unify(st, pa, pb));
    }

    public static UnifyResult unify_mismatch(UnificationState st, CodexType a, CodexType b)
    {
        return new UnifyResult(false, add_unify_error(st, "CDX2001", "Type mismatch"));
    }

    public static CodexType deep_resolve(UnificationState st, CodexType ty)
    {
        return ((Func<CodexType, CodexType>)((resolved) => ((Func<CodexType, CodexType>)((_scrutinee_) => (_scrutinee_ is FunTy _mFunTy_ ? ((Func<CodexType, CodexType>)((ret) => ((Func<CodexType, CodexType>)((param) => new FunTy(deep_resolve(st, param), deep_resolve(st, ret))))((CodexType)_mFunTy_.Field0)))((CodexType)_mFunTy_.Field1) : (_scrutinee_ is ListTy _mListTy_ ? ((Func<CodexType, CodexType>)((elem) => new ListTy(deep_resolve(st, elem))))((CodexType)_mListTy_.Field0) : ((Func<CodexType, CodexType>)((_) => resolved))(_scrutinee_)))))(resolved)))(resolve(st, ty));
    }

}
