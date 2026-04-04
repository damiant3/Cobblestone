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
        94,
        233, 232, 234, 235, 225, 224, 226, 228, 243,
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

public sealed record ACitesDecl(Name chapter_name, List<Name> selected_names);

public sealed record AChapter(Name name, List<ADef> defs, List<ATypeDef> type_defs, List<AEffectDef> effect_defs, List<ACitesDecl> citations, string chapter_title, string prose, List<string> section_titles);

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

public sealed record FuncOffset(string name, long offset);

public sealed record CallPatch(long patch_offset, string target);

public sealed record LocalBinding(string name, long slot);

public sealed record FuncAddrFixup(long patch_offset, string target);

public sealed record TcoState(bool active, bool in_tail_pos, long loop_top, List<long> param_locals, List<long> temp_locals, string current_func, long saved_next_local, long saved_next_temp);

public sealed record CodegenState(List<long> text, List<long> rodata, List<FuncOffset> func_offsets, List<CallPatch> call_patches, List<FuncAddrFixup> func_addr_fixups, List<LocalBinding> locals, long next_temp, long next_local, long spill_count, long load_local_toggle, TcoState tco);

public sealed record EmitResult(CodegenState state, long reg);

public sealed record TrampolineResult(List<long> bytes, long far_jump_patch_pos);

public sealed record FlatApply(string func_name, List<IRExpr> args);

public sealed record SavedArgs(CodegenState state, List<long> locals);

public sealed record FieldLocal(string name, long slot);

public sealed record EvalFieldsResult(CodegenState state, List<FieldLocal> field_locals);

public sealed record MatchBranchState(CodegenState cg_state, List<long> end_patches);

public sealed record PatchEntry(long pos, long b0, long b1, long b2, long b3);

public sealed record TcoAllocResult(CodegenState alloc_state, List<long> alloc_locals);

public sealed record EmitPatternResult(CodegenState state, long next_branch_patch);

public sealed record ItoaState(CodegenState cg, long jmp_done_zero_pos);

public sealed record StrEqHeadResult(CodegenState cg, long len_ne_pos);

public sealed record StrEqLoopResult(CodegenState cg, long loop_done_pos, long byte_ne_pos);

public sealed record ItoaZeroResult(CodegenState cg, long skip_digits_pos);

public sealed record StrConcatCheckResult(CodegenState cg, long slow_path_pos);

public sealed record StrConcatFastResult(CodegenState cg, long fast_done_pos);

public sealed record HelpResult1(CodegenState cg, long p1);

public sealed record HelpResult2(CodegenState cg, long p1, long p2);

public sealed record HelpResult3(CodegenState cg, long p1, long p2, long p3);

public sealed record HelpResult4(CodegenState cg, long p1, long p2, long p3, long p4);

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

public sealed record IRChapter(Name name, List<IRDef> defs, string chapter_title, string prose, List<string> section_titles);

public sealed record LowerCtx(List<TypeBinding> types, UnificationState ust);

public sealed record ChapterAssignment(string def_name, string chapter_slug);

public sealed record RenameEntry(string original, string mangled);

public sealed record Scope(List<string> names);

public sealed record ResolveResult(List<Diagnostic> errors, List<string> top_level_names, List<string> type_names, List<string> ctor_names);

public sealed record CollectResult(List<string> names, List<Diagnostic> errors);

public sealed record CtorCollectResult(List<string> type_names, List<string> ctor_names);

public sealed record LexState(string source, long offset, long line, long column);

public abstract record LexResult;
public sealed record LexToken(Token Field0, LexState Field1) : LexResult;
public sealed record LexEnd : LexResult;


public sealed record SelectedNamesResult(List<Token> names, ParseState state);

public sealed record ImportParseResult(List<CitesDecl> imports, ParseState state);

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

public sealed record CitesDecl(Token chapter_name, List<Token> selected_names);

public sealed record Document(List<Def> defs, List<TypeDef> type_defs, List<EffectDef> effect_defs, List<CitesDecl> citations, string chapter_title, List<string> section_titles);

public sealed record DefHeader(Token name, List<Token> @params, List<TypeAnn> ann, long body_pos, string chapter_slug);

public abstract record ScanDefResult;
public sealed record DefHeaderOk(DefHeader Field0, ParseState Field1) : ScanDefResult;
public sealed record DefHeaderNone(ParseState Field0) : ScanDefResult;


public sealed record ScanResult(List<TypeDef> type_defs, List<EffectDef> effect_defs, List<DefHeader> def_headers, List<CitesDecl> citations, string chapter_title, List<string> section_titles);

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
public sealed record CitesKeyword : TokenKind;
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

public sealed record ChapterResult(List<TypeBinding> types, UnificationState state);

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
public sealed record CompileOk(string Field0, ChapterResult Field1) : CompileResult;
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

    public static AChapter desugar_document(Document doc, string chapter_name) => new AChapter(name: make_name(chapter_name), defs: map_list(desugar_def, doc.defs), type_defs: map_list(desugar_type_def, doc.type_defs), effect_defs: map_list(desugar_effect_def, doc.effect_defs), citations: map_list(desugar_cite, doc.citations), chapter_title: doc.chapter_title, prose: "", section_titles: doc.section_titles);

    public static ACitesDecl desugar_cite(CitesDecl imp) => new ACitesDecl(chapter_name: make_name(imp.chapter_name.text), selected_names: map_list(desugar_cite_name, imp.selected_names));

    public static Name desugar_cite_name(Token tok) => make_name(tok.text);

    public static AEffectDef desugar_effect_def(EffectDef ed) => new AEffectDef(name: make_name(ed.name.text), ops: map_list(desugar_effect_op, ed.ops));

    public static AEffectOpDef desugar_effect_op(EffectOpDef op) => new AEffectOpDef(name: make_name(op.name.text), type_expr: desugar_type_expr(op.type_expr));

    public static List<T238> map_list<T228, T238>(Func<T228, T238> f, List<T228> xs) => map_list_loop(f, xs, 0, ((long)xs.Count), new List<T238>());

    public static List<T253> map_list_loop<T252, T253>(Func<T252, T253> f, List<T252> xs, long i, long len, List<T253> acc)
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
            var _tco_4 = ((Func<List<T253>>)(() => { var _l = acc; _l.Add(f(xs[(int)i])); return _l; }))();
            f = _tco_0;
            xs = _tco_1;
            i = _tco_2;
            len = _tco_3;
            acc = _tco_4;
            continue;
            }
        }
    }

    public static T266 fold_list<T266, T257>(Func<T266, Func<T257, T266>> f, T266 z, List<T257> xs) => fold_list_loop(f, z, xs, 0, ((long)xs.Count));

    public static T280 fold_list_loop<T280, T275>(Func<T280, Func<T275, T280>> f, T280 z, List<T275> xs, long i, long len)
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

    public static string emit_record_field_defs(List<ARecordFieldDef> fields, long tparams, object i) => ((i == ((long)fields.Count)) ? "" : ((Func<ARecordFieldDef, string>)((f) => (emit_type_expr_tp(f.type_expr, tparams) + (" " + (sanitize(f.name.value) + (((i < (((long)fields.Count) - 1)) ? ", " : "") + emit_record_field_defs(fields, tparams, (i + 1))))))))(fields[(int)i]));

    public static string emit_variant_ctors(List<AVariantCtorDef> ctors, long base_name, object tparams, object i) => ((i == ((long)ctors.Count)) ? "" : ((Func<AVariantCtorDef, string>)((c) => (emit_variant_ctor(c, base_name, tparams) + emit_variant_ctors(ctors, base_name, tparams, (i + 1)))))(ctors[(int)i]));

    public static string emit_variant_ctor(AVariantCtorDef c, Name base_name, List<Name> tparams) => ((Func<string, string>)((gen) => ((((long)c.fields.Count) == 0) ? ("public sealed record " + (sanitize(c.name.value) + (gen + (" : " + (sanitize(base_name.value) + (gen + ";\u0001")))))) : ("public sealed record " + (sanitize(c.name.value) + (gen + ("(" + (emit_ctor_fields(c.fields, tparams, 0) + (") : " + (sanitize(base_name.value) + (gen + ";\u0001")))))))))))(emit_tparameter_suffix(tparams));

    public static string emit_ctor_fields(List<ATypeExpr> fields, long tparams, object i) => ((i == ((long)fields.Count)) ? "" : (emit_type_expr_tp(fields[(int)i], tparams) + (" Field" + (_Cce.FromUnicode(i.ToString()) + (((i < (((long)fields.Count) - 1)) ? ", " : "") + emit_ctor_fields(fields, tparams, (i + 1)))))));

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

    public static string emit_tco_body(IRExpr e, string func_name, List<IRParam> @params, List<ArityEntry> arities) => e switch { IrIf(var c, var t, var el, var ty) => emit_tco_if(c, t, el, func_name, @params, arities), IrLet(var name, var ty, var val, var body) => emit_tco_let(name, ty, val, body, func_name, @params, arities), IrMatch(var scrut, var branches, var ty) => emit_tco_match(scrut, branches, func_name, @params, arities), IrApply(var f, var a, var rty) => emit_tco_apply(e, func_name, @params, arities), _ => ("            return " + Enumerable.Concat(emit_expr(e, arities), ";\u0001").ToList()), };

    public static string emit_tco_apply(IRExpr e, string func_name, List<IRParam> @params, List<ArityEntry> arities) => (is_self_call(e, func_name) ? emit_tco_jump(e, @params, arities) : ("            return " + Enumerable.Concat(emit_expr(e, arities), ";\u0001").ToList()));

    public static string emit_tco_if(IRExpr cond, IRExpr t, IRExpr el, string func_name, List<IRParam> @params, List<ArityEntry> arities) => ("            if (" + Enumerable.Concat(emit_expr(cond, arities), (")\u0001            {\u0001" + (emit_tco_body(t, func_name, @params, arities) + ("            }\u0001            else\u0001            {\u0001" + (emit_tco_body(el, func_name, @params, arities) + "            }\u0001"))))).ToList());

    public static string emit_tco_let(string name, CodexType ty, IRExpr val, IRExpr body, string func_name, List<IRParam> @params, List<ArityEntry> arities) => ("            var " + (sanitize(name) + (" = " + Enumerable.Concat(emit_expr(val, arities), (";\u0001" + emit_tco_body(body, func_name, @params, arities))).ToList())));

    public static string emit_tco_match(IRExpr scrut, List<IRBranch> branches, string func_name, List<IRParam> @params, List<ArityEntry> arities) => ("            var _tco_s = " + Enumerable.Concat(emit_expr(scrut, arities), (";\u0001" + emit_tco_match_branches(branches, func_name, @params, arities, 0, true))).ToList());

    public static string emit_tco_match_branches(List<IRBranch> branches, string func_name, List<IRParam> @params, List<ArityEntry> arities, long i, bool is_first) => ((i == ((long)branches.Count)) ? "" : ((Func<IRBranch, string>)((b) => (emit_tco_match_branch(b, func_name, @params, arities, i, is_first) + emit_tco_match_branches(branches, func_name, @params, arities, (i + 1), false))))(branches[(int)i]));

    public static string emit_tco_match_branch(IRBranch b, string func_name, List<IRParam> @params, List<ArityEntry> arities, long idx, bool is_first) => b.pattern switch { IrWildPat { } => ("            {\u0001" + (emit_tco_body(b.body, func_name, @params, arities) + "            }\u0001")), IrVarPat(var name, var ty) => ("            {\u0001                var " + (sanitize(name) + (" = _tco_s;\u0001" + (emit_tco_body(b.body, func_name, @params, arities) + "            }\u0001")))), IrCtorPat(var name, var subs, var ty) => ((Func<string, string>)((keyword) => ((Func<string, string>)((match_var) => ("            " + (keyword + (" (_tco_s is " + (sanitize(name) + (" " + (match_var + (")\u0001            {\u0001" + (emit_tco_ctor_bindings(subs, match_var, 0) + (emit_tco_body(b.body, func_name, @params, arities) + "            }\u0001")))))))))))(("_tco_m" + _Cce.FromUnicode(idx.ToString())))))((is_first ? "if" : "else if")), IrLitPat(var text, var ty) => ((Func<string, string>)((keyword) => ("            " + (keyword + (" (object.Equals(_tco_s, " + (text + ("))\u0001            {\u0001" + (emit_tco_body(b.body, func_name, @params, arities) + "            }\u0001"))))))))((is_first ? "if" : "else if")), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static string emit_tco_ctor_bindings(List<IRPat> subs, string match_var, long i) => ((i == ((long)subs.Count)) ? "" : ((Func<IRPat, string>)((sub) => (emit_tco_ctor_binding(sub, match_var, i) + emit_tco_ctor_bindings(subs, match_var, (i + 1)))))(subs[(int)i]));

    public static string emit_tco_ctor_binding(IRPat sub, string match_var, long i) => sub switch { IrVarPat(var name, var ty) => ("                var " + (sanitize(name) + (" = " + (match_var + (".Field" + (_Cce.FromUnicode(i.ToString()) + ";\u0001")))))), _ => "", };

    public static string emit_tco_jump(IRExpr e, List<IRParam> @params, List<ArityEntry> arities) => ((Func<ApplyChain, string>)((chain) => (emit_tco_temps(chain.args, arities, 0) + (emit_tco_assigns(@params, 0) + "            continue;\u0001"))))(collect_apply_chain(e, new List<IRExpr>()));

    public static string emit_tco_temps(List<IRExpr> args, List<ArityEntry> arities, long i) => ((i == ((long)args.Count)) ? "" : ("            var _tco_" + (_Cce.FromUnicode(i.ToString()) + (" = " + Enumerable.Concat(emit_expr(args[(int)i], arities), (";\u0001" + emit_tco_temps(args, arities, (i + 1)))).ToList()))));

    public static string emit_tco_assigns(List<IRParam> @params, long i) => ((i == ((long)@params.Count)) ? "" : ((Func<IRParam, string>)((p) => ("            " + (sanitize(p.name) + (" = _tco_" + (_Cce.FromUnicode(i.ToString()) + (";\u0001" + emit_tco_assigns(@params, (i + 1)))))))))(@params[(int)i]));

    public static string emit_def(IRDef d, List<string> arities) => (should_tco(d) ? emit_tco_def(d, arities) : ((Func<CodexType, string>)((ret) => ((Func<string, string>)((gen) => ("    public static " + (cs_type(ret) + (" " + (sanitize(d.name) + (gen + ("(" + (emit_def_params(d.@params, 0) + (") => " + Enumerable.Concat(emit_expr(d.body, arities), ";\u0001").ToList()))))))))))(generic_suffix(d.type_val))))(get_return_type(d.type_val, ((long)d.@params.Count))));

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

    public static string emit_cce_runtime() => ("static class _Cce {\u0001" + ("    static readonly int[] _toUni = {\u0001" + ("        0, 10, 32,\u0001" + ("        48, 49, 50, 51, 52, 53, 54, 55, 56, 57,\u0001" + ("        101, 116, 97, 111, 105, 110, 115, 104, 114, 100,\u0001" + ("        108, 99, 117, 109, 119, 102, 103, 121, 112, 98,\u0001" + ("        118, 107, 106, 120, 113, 122,\u0001" + ("        69, 84, 65, 79, 73, 78, 83, 72, 82, 68,\u0001" + ("        76, 67, 85, 77, 87, 70, 71, 89, 80, 66,\u0001" + ("        86, 75, 74, 88, 81, 90,\u0001" + ("        46, 44, 33, 63, 58, 59, 39, 34, 45, 40, 41,\u0001" + ("        43, 61, 42, 60, 62,\u0001" + ("        47, 64, 35, 38, 95, 92, 124, 91, 93, 123, 125, 126, 96,\u0001" + ("        94,\u0001" + ("        233, 232, 234, 235, 225, 224, 226, 228, 243,\u0001" + ("        244, 246, 250, 249, 251, 252, 241, 231, 237,\u0001" + ("        1072, 1086, 1077, 1080, 1085, 1090, 1089, 1088,\u0001" + ("        1074, 1083, 1082, 1084, 1076, 1087, 1091\u0001" + ("    };\u0001" + ("    static readonly Dictionary<int, int> _fromUni = new();\u0001" + ("    static _Cce() { for (int i = 0; i < 128; i++) _fromUni[_toUni[i]] = i; }\u0001" + ("    public static string FromUnicode(string s) {\u0001" + ("        var cs = new char[s.Length];\u0001" + ("        for (int i = 0; i < s.Length; i++) {\u0001" + ("            int u = s[i];\u0001" + ("            cs[i] = _fromUni.TryGetValue(u, out int c) ? (char)c : (char)68;\u0001" + ("        }\u0001" + ("        return new string(cs);\u0001" + ("    }\u0001" + ("    public static string ToUnicode(string s) {\u0001" + ("        var cs = new char[s.Length];\u0001" + ("        for (int i = 0; i < s.Length; i++) {\u0001" + ("            int b = s[i];\u0001" + ("            cs[i] = (b >= 0 && b < 128) ? (char)_toUni[b] : '\\uFFFD';\u0001" + ("        }\u0001" + ("        return new string(cs);\u0001" + ("    }\u0001" + ("    public static long UniToCce(long u) {\u0001" + ("        return _fromUni.TryGetValue((int)u, out int c) ? c : 68;\u0001" + ("    }\u0001" + ("    public static long CceToUni(long b) {\u0001" + ("        return (b >= 0 && b < 128) ? _toUni[(int)b] : 65533;\u0001" + ("    }\u0001" + "}\u0001\u0001")))))))))))))))))))))))))))))))))))))))))));

    public static string emit_full_chapter(IRChapter m, List<ATypeDef> type_defs) => ((Func<List<ArityEntry>, string>)((arities) => ("using System;\u0001using System.Collections.Generic;\u0001using System.IO;\u0001using System.Linq;\u0001using System.Threading.Tasks;\u0001\u0001" + ("Codex_" + (sanitize(m.name.value) + (".main();\u0001\u0001" + (emit_cce_runtime() + (emit_type_defs(type_defs, 0) + (emit_class_header(m.name.value) + (emit_defs(m.defs, 0, arities) + "}\u0001"))))))))))(build_arity_map(m.defs, 0));

    public static string emit_chapter(IRChapter m) => ((Func<List<ArityEntry>, string>)((arities) => ("using System;\u0001using System.Collections.Generic;\u0001using System.IO;\u0001using System.Linq;\u0001using System.Threading.Tasks;\u0001\u0001" + ("Codex_" + (sanitize(m.name.value) + (".main();\u0001\u0001" + (emit_cce_runtime() + (emit_class_header(m.name.value) + (emit_defs(m.defs, 0, arities) + "}\u0001")))))))))(build_arity_map(m.defs, 0));

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

    public static Func<long, Func<long, Func<bool, string>>> emit_apply_args(List<IRExpr> args, List<string> arities, long i) => ((i == ((long)args.Count)) ? "" : ((i == (((long)args.Count) - 1)) ? emit_expr(args[(int)i], arities) : Enumerable.Concat(emit_expr(args[(int)i], arities), (", " + emit_apply_args(args, arities, (i + 1)))).ToList()));

    public static string emit_partial_params(long i, long count) => ((i == count) ? "" : ((i == (count - 1)) ? ("_p" + (_Cce.FromUnicode(i.ToString()) + "_")) : ("_p" + (_Cce.FromUnicode(i.ToString()) + ("_" + (", " + emit_partial_params((i + 1), count)))))));

    public static string emit_partial_wrappers(long i, long count) => ((i == count) ? "" : ("(_p" + (_Cce.FromUnicode(i.ToString()) + ("_) => " + emit_partial_wrappers((i + 1), count)))));

    public static bool is_builtin_name(string n) => ((n == "show") ? true : ((n == "negate") ? true : ((n == "print-line") ? true : ((n == "text-length") ? true : ((n == "is-letter") ? true : ((n == "is-digit") ? true : ((n == "is-whitespace") ? true : ((n == "text-to-integer") ? true : ((n == "integer-to-text") ? true : ((n == "char-code") ? true : ((n == "char-code-at") ? true : ((n == "code-to-char") ? true : ((n == "char-to-text") ? true : ((n == "list-length") ? true : ((n == "char-at") ? true : ((n == "substring") ? true : ((n == "list-at") ? true : ((n == "list-insert-at") ? true : ((n == "list-snoc") ? true : ((n == "text-compare") ? true : ((n == "text-replace") ? true : ((n == "open-file") ? true : ((n == "read-all") ? true : ((n == "close-file") ? true : ((n == "read-line") ? true : ((n == "read-file") ? true : ((n == "write-file") ? true : ((n == "file-exists") ? true : ((n == "list-files") ? true : ((n == "text-concat-list") ? true : ((n == "text-split") ? true : ((n == "text-contains") ? true : ((n == "text-starts-with") ? true : ((n == "get-args") ? true : ((n == "get-env") ? true : ((n == "current-dir") ? true : ((n == "run-process") ? true : ((n == "fork") ? true : ((n == "await") ? true : ((n == "par") ? true : ((n == "race") ? true : false)))))))))))))))))))))))))))))))))))))))));

    public static EmitResult emit_builtin(CodegenState n, string args, List<IRExpr> arities) => ((n == "show") ? emit_builtin_show(args, arities) : ((n == "negate") ? ("(-" + Enumerable.Concat(emit_expr(args[(int)0], arities), ")").ToList()) : ((n == "print-line") ? emit_builtin_print_line(args, arities) : ((n == "text-length") ? ("((long)" + Enumerable.Concat(emit_expr(args[(int)0], arities), ".Length)").ToList()) : ((n == "is-letter") ? emit_builtin_is_letter(args, arities) : ((n == "is-digit") ? emit_builtin_is_digit(args, arities) : ((n == "is-whitespace") ? ("(" + Enumerable.Concat(emit_expr(args[(int)0], arities), " <= 2L)").ToList()) : ((n == "text-to-integer") ? emit_builtin_text_to_integer(args, arities) : ((n == "integer-to-text") ? emit_builtin_integer_to_text(args, arities) : ((n == "char-code") ? emit_expr(args[(int)0], arities) : ((n == "char-code-at") ? emit_builtin_char_code_at(args, arities) : ((n == "code-to-char") ? emit_expr(args[(int)0], arities) : ((n == "char-to-text") ? ("((char)" + Enumerable.Concat(emit_expr(args[(int)0], arities), ").ToString()").ToList()) : ((n == "list-length") ? ("((long)" + Enumerable.Concat(emit_expr(args[(int)0], arities), ".Count)").ToList()) : ((n == "char-at") ? emit_builtin_char_at(args, arities) : ((n == "substring") ? emit_builtin_substring(args, arities) : ((n == "list-at") ? emit_builtin_list_at(args, arities) : ((n == "list-insert-at") ? emit_builtin_list_insert_at(args, arities) : ((n == "list-snoc") ? emit_builtin_list_snoc(args, arities) : ((n == "text-compare") ? emit_builtin_text_compare(args, arities) : ((n == "text-replace") ? emit_builtin_text_replace(args, arities) : ((n == "open-file") ? ("File.OpenRead(" + Enumerable.Concat(emit_expr(args[(int)0], arities), ")").ToList()) : ((n == "read-all") ? emit_builtin_read_all(args, arities) : ((n == "close-file") ? Enumerable.Concat(emit_expr(args[(int)0], arities), ".Dispose()").ToList() : ((n == "read-line") ? "_Cce.FromUnicode(Console.ReadLine() ?? \"\")" : ((n == "read-file") ? emit_builtin_read_file(args, arities) : ((n == "write-file") ? emit_builtin_write_file(args, arities) : ((n == "file-exists") ? emit_builtin_file_exists(args, arities) : ((n == "list-files") ? emit_builtin_list_files(args, arities) : ((n == "text-concat-list") ? ("string.Concat(" + Enumerable.Concat(emit_expr(args[(int)0], arities), ")").ToList()) : ((n == "text-split") ? emit_builtin_text_split(args, arities) : ((n == "text-contains") ? emit_builtin_text_contains(args, arities) : ((n == "text-starts-with") ? emit_builtin_text_starts_with(args, arities) : ((n == "get-args") ? "Environment.GetCommandLineArgs().Select(_Cce.FromUnicode).ToList()" : ((n == "get-env") ? emit_builtin_get_env(args, arities) : ((n == "run-process") ? emit_builtin_run_process(args, arities) : ((n == "current-dir") ? "_Cce.FromUnicode(Directory.GetCurrentDirectory())" : ((n == "fork") ? ("Task.Run(() => (" + Enumerable.Concat(emit_expr(args[(int)0], arities), ")(null))").ToList()) : ((n == "await") ? ("(" + Enumerable.Concat(emit_expr(args[(int)0], arities), ").Result").ToList()) : ((n == "par") ? emit_builtin_par(args, arities) : ((n == "race") ? emit_builtin_race(args, arities) : "")))))))))))))))))))))))))))))))))))))))));

    public static string emit_builtin_show(List<IRExpr> args, List<ArityEntry> arities) => ((Func<EmitResult, string>)((a0) => ("_Cce.FromUnicode(Convert.ToString(" + Enumerable.Concat(a0, "))").ToList())))(emit_expr(args[(int)0], arities));

    public static string emit_builtin_print_line(List<IRExpr> args, List<ArityEntry> arities) => ((Func<EmitResult, string>)((a0) => ("((Func<object>)(() => { Console.WriteLine(_Cce.ToUnicode(" + Enumerable.Concat(a0, ")); return null; }))()").ToList())))(emit_expr(args[(int)0], arities));

    public static string emit_builtin_is_letter(List<IRExpr> args, List<ArityEntry> arities) => ((Func<EmitResult, string>)((a0) => ("(" + Enumerable.Concat(a0, (" >= 13L && " + Enumerable.Concat(a0, " <= 64L)").ToList())).ToList())))(emit_expr(args[(int)0], arities));

    public static string emit_builtin_is_digit(List<IRExpr> args, List<ArityEntry> arities) => ((Func<EmitResult, string>)((a0) => ("(" + Enumerable.Concat(a0, (" >= 3L && " + Enumerable.Concat(a0, " <= 12L)").ToList())).ToList())))(emit_expr(args[(int)0], arities));

    public static string emit_builtin_text_to_integer(List<IRExpr> args, List<ArityEntry> arities) => ((Func<EmitResult, string>)((a0) => ("long.Parse(_Cce.ToUnicode(" + Enumerable.Concat(a0, "))").ToList())))(emit_expr(args[(int)0], arities));

    public static string emit_builtin_integer_to_text(List<IRExpr> args, List<ArityEntry> arities) => ((Func<EmitResult, string>)((a0) => ("_Cce.FromUnicode(" + Enumerable.Concat(a0, ".ToString())").ToList())))(emit_expr(args[(int)0], arities));

    public static string emit_builtin_char_code_at(List<IRExpr> args, List<ArityEntry> arities) => ((Func<EmitResult, string>)((a0) => ((Func<EmitResult, string>)((a1) => ("((long)" + Enumerable.Concat(a0, ("[(int)" + Enumerable.Concat(a1, "])").ToList())).ToList())))(emit_expr(args[(int)1], arities))))(emit_expr(args[(int)0], arities));

    public static string emit_builtin_char_at(List<IRExpr> args, List<ArityEntry> arities) => ((Func<EmitResult, string>)((a0) => ((Func<EmitResult, string>)((a1) => ("((long)" + Enumerable.Concat(a0, ("[(int)" + Enumerable.Concat(a1, "])").ToList())).ToList())))(emit_expr(args[(int)1], arities))))(emit_expr(args[(int)0], arities));

    public static string emit_builtin_substring(List<IRExpr> args, List<ArityEntry> arities) => ((Func<EmitResult, string>)((a0) => ((Func<EmitResult, string>)((a1) => ((Func<EmitResult, string>)((a2) => Enumerable.Concat(a0, (".Substring((int)" + Enumerable.Concat(a1, (", (int)" + Enumerable.Concat(a2, ")").ToList())).ToList())).ToList()))(emit_expr(args[(int)2], arities))))(emit_expr(args[(int)1], arities))))(emit_expr(args[(int)0], arities));

    public static string emit_builtin_list_at(List<IRExpr> args, List<ArityEntry> arities) => ((Func<EmitResult, string>)((a0) => ((Func<EmitResult, string>)((a1) => Enumerable.Concat(a0, ("[(int)" + Enumerable.Concat(a1, "]").ToList())).ToList()))(emit_expr(args[(int)1], arities))))(emit_expr(args[(int)0], arities));

    public static string emit_builtin_list_insert_at(List<IRExpr> args, List<ArityEntry> arities) => ((Func<CodexType, string>)((elem_ty) => ((Func<string, string>)((ty) => ((Func<EmitResult, string>)((a0) => ((Func<EmitResult, string>)((a1) => ((Func<EmitResult, string>)((a2) => ("((Func<List<" + (ty + (">>)(() => { var _l = new List<" + (ty + (">(" + Enumerable.Concat(a0, ("); _l.Insert((int)" + Enumerable.Concat(a1, (", " + Enumerable.Concat(a2, "); return _l; }))()").ToList())).ToList())).ToList())))))))(emit_expr(args[(int)2], arities))))(emit_expr(args[(int)1], arities))))(emit_expr(args[(int)0], arities))))(cs_type(elem_ty))))(ir_expr_type(args[(int)0]) switch { ListTy(var et) => et, _ => new ErrorTy(), });

    public static string emit_builtin_list_snoc(List<IRExpr> args, List<ArityEntry> arities) => ((Func<CodexType, string>)((elem_ty) => ((Func<string, string>)((ty) => ((Func<EmitResult, string>)((a0) => ((Func<EmitResult, string>)((a1) => ("((Func<List<" + (ty + (">>)(() => { var _l = " + Enumerable.Concat(a0, ("; _l.Add(" + Enumerable.Concat(a1, "); return _l; }))()").ToList())).ToList())))))(emit_expr(args[(int)1], arities))))(emit_expr(args[(int)0], arities))))(cs_type(elem_ty))))(ir_expr_type(args[(int)0]) switch { ListTy(var et) => et, _ => new ErrorTy(), });

    public static string emit_builtin_text_compare(List<IRExpr> args, List<ArityEntry> arities) => ((Func<EmitResult, string>)((a0) => ((Func<EmitResult, string>)((a1) => ("(long)string.CompareOrdinal(" + Enumerable.Concat(a0, (", " + Enumerable.Concat(a1, ")").ToList())).ToList())))(emit_expr(args[(int)1], arities))))(emit_expr(args[(int)0], arities));

    public static string emit_builtin_text_replace(List<IRExpr> args, List<ArityEntry> arities) => ((Func<EmitResult, string>)((a0) => ((Func<EmitResult, string>)((a1) => ((Func<EmitResult, string>)((a2) => Enumerable.Concat(a0, (".Replace(" + Enumerable.Concat(a1, (", " + Enumerable.Concat(a2, ")").ToList())).ToList())).ToList()))(emit_expr(args[(int)2], arities))))(emit_expr(args[(int)1], arities))))(emit_expr(args[(int)0], arities));

    public static string emit_builtin_read_all(List<IRExpr> args, List<ArityEntry> arities) => ((Func<EmitResult, string>)((a0) => ("new System.IO.StreamReader(" + Enumerable.Concat(a0, ").ReadToEnd()").ToList())))(emit_expr(args[(int)0], arities));

    public static string emit_builtin_read_file(List<IRExpr> args, List<ArityEntry> arities) => ((Func<EmitResult, string>)((a0) => ("_Cce.FromUnicode(File.ReadAllText(_Cce.ToUnicode(" + Enumerable.Concat(a0, ")))").ToList())))(emit_expr(args[(int)0], arities));

    public static string emit_builtin_write_file(List<IRExpr> args, List<ArityEntry> arities) => ((Func<EmitResult, string>)((a0) => ((Func<EmitResult, string>)((a1) => ("File.WriteAllText(_Cce.ToUnicode(" + Enumerable.Concat(a0, ("), _Cce.ToUnicode(" + Enumerable.Concat(a1, "))").ToList())).ToList())))(emit_expr(args[(int)1], arities))))(emit_expr(args[(int)0], arities));

    public static string emit_builtin_file_exists(List<IRExpr> args, List<ArityEntry> arities) => ((Func<EmitResult, string>)((a0) => ("File.Exists(_Cce.ToUnicode(" + Enumerable.Concat(a0, "))").ToList())))(emit_expr(args[(int)0], arities));

    public static string emit_builtin_list_files(List<IRExpr> args, List<ArityEntry> arities) => ((Func<EmitResult, string>)((a0) => ((Func<EmitResult, string>)((a1) => ("Directory.GetFiles(_Cce.ToUnicode(" + Enumerable.Concat(a0, ("), _Cce.ToUnicode(" + Enumerable.Concat(a1, ")).Select(_Cce.FromUnicode).ToList()").ToList())).ToList())))(emit_expr(args[(int)1], arities))))(emit_expr(args[(int)0], arities));

    public static string emit_builtin_text_split(List<IRExpr> args, List<ArityEntry> arities) => ((Func<EmitResult, string>)((a0) => ((Func<EmitResult, string>)((a1) => ("new List<string>(" + Enumerable.Concat(a0, (".Split(" + Enumerable.Concat(a1, "))").ToList())).ToList())))(emit_expr(args[(int)1], arities))))(emit_expr(args[(int)0], arities));

    public static string emit_builtin_text_contains(List<IRExpr> args, List<ArityEntry> arities) => ((Func<EmitResult, string>)((a0) => ((Func<EmitResult, string>)((a1) => Enumerable.Concat(a0, (".Contains(" + Enumerable.Concat(a1, ")").ToList())).ToList()))(emit_expr(args[(int)1], arities))))(emit_expr(args[(int)0], arities));

    public static string emit_builtin_text_starts_with(List<IRExpr> args, List<ArityEntry> arities) => ((Func<EmitResult, string>)((a0) => ((Func<EmitResult, string>)((a1) => Enumerable.Concat(a0, (".StartsWith(" + Enumerable.Concat(a1, ")").ToList())).ToList()))(emit_expr(args[(int)1], arities))))(emit_expr(args[(int)0], arities));

    public static string emit_builtin_get_env(List<IRExpr> args, List<ArityEntry> arities) => ((Func<EmitResult, string>)((a0) => ("_Cce.FromUnicode(Environment.GetEnvironmentVariable(_Cce.ToUnicode(" + Enumerable.Concat(a0, ")) ?? \"\")").ToList())))(emit_expr(args[(int)0], arities));

    public static string emit_builtin_run_process(List<IRExpr> args, List<ArityEntry> arities) => ((Func<EmitResult, string>)((a0) => ((Func<EmitResult, string>)((a1) => ("_Cce.FromUnicode(((Func<string>)(() => {" + (" var _psi = new System.Diagnostics.ProcessStartInfo(" + ("_Cce.ToUnicode(" + Enumerable.Concat(a0, ("), _Cce.ToUnicode(" + Enumerable.Concat(a1, ("))" + (" { RedirectStandardOutput = true, UseShellExecute = false };" + (" var _p = System.Diagnostics.Process.Start(_psi)!;" + (" var _o = _p.StandardOutput.ReadToEnd();" + " _p.WaitForExit(); return _o; }))()"))))).ToList())).ToList())))))(emit_expr(args[(int)1], arities))))(emit_expr(args[(int)0], arities));

    public static string emit_builtin_par(List<IRExpr> args, List<ArityEntry> arities) => ((Func<EmitResult, string>)((a0) => ((Func<EmitResult, string>)((a1) => ("Task.WhenAll(" + Enumerable.Concat(a1, (".Select(_x_ => Task.Run(() => (" + Enumerable.Concat(a0, ")(_x_)))).Result.ToList()").ToList())).ToList())))(emit_expr(args[(int)1], arities))))(emit_expr(args[(int)0], arities));

    public static string emit_builtin_race(List<IRExpr> args, List<ArityEntry> arities) => ((Func<EmitResult, string>)((a0) => ("Task.WhenAny(" + Enumerable.Concat(a0, ".Select(_t_ => Task.Run(() => _t_(null)))).Result.Result").ToList())))(emit_expr(args[(int)0], arities));

    public static Func<IRExpr, Func<CodexType, EmitResult>> emit_apply(CodegenState e, IRExpr arities) => ((Func<ApplyChain, Func<IRExpr, Func<CodexType, EmitResult>>>)((chain) => ((Func<IRExpr, Func<IRExpr, Func<CodexType, EmitResult>>>)((root) => ((Func<List<IRExpr>, Func<IRExpr, Func<CodexType, EmitResult>>>)((args) => root switch { IrName(var n, var ty) => (is_builtin_name(n) ? emit_builtin(n, args, arities) : (((((long)n.Length) > 0) && is_upper_letter(((long)n[(int)0]))) ? ((Func<CodexType, string>)((result_ty) => ((Func<string, string>)((ctor_type_args) => ("new " + (sanitize(n) + (ctor_type_args + ("(" + Enumerable.Concat(emit_apply_args(args, arities, 0), ")").ToList()))))))(extract_ctor_type_args(result_ty))))(ir_expr_type(e)) : ((Func<long, Func<IRExpr, Func<CodexType, EmitResult>>>)((ar) => (((ar > 1) && (((long)args.Count) == ar)) ? (sanitize(n) + ("(" + Enumerable.Concat(emit_apply_args(args, arities, 0), ")").ToList())) : (((ar > 1) && (((long)args.Count) < ar)) ? ((Func<long, string>)((remaining) => (emit_partial_wrappers(0, remaining) + (sanitize(n) + ("(" + Enumerable.Concat(emit_apply_args(args, arities, 0), (", " + (emit_partial_params(0, remaining) + ")"))).ToList())))))((ar - ((long)args.Count))) : emit_expr_curried(e, arities)))))(lookup_arity(arities, n)))), _ => emit_expr_curried(e, arities), }))(chain.args)))(chain.root)))(collect_apply_chain(e, new List<IRExpr>()));

    public static string emit_expr_curried(IRExpr e, List<ArityEntry> arities) => e switch { IrApply(var f, var a, var ty) => Enumerable.Concat(emit_expr(f, arities), ("(" + Enumerable.Concat(emit_expr(a, arities), ")").ToList())).ToList(), _ => emit_expr(e, arities), };

    public static EmitResult emit_expr(CodegenState e, IRExpr arities) => e switch { IrIntLit(var n) => _Cce.FromUnicode(n.ToString()), IrNumLit(var n) => _Cce.FromUnicode(n.ToString()), IrTextLit(var s) => ("\"" + (escape_text(s) + "\"")), IrBoolLit(var b) => (b ? "true" : "false"), IrCharLit(var n) => _Cce.FromUnicode(n.ToString()), IrName(var n, var ty) => ((n == "read-line") ? "_Cce.FromUnicode(Console.ReadLine() ?? \"\")" : ((n == "get-args") ? "Environment.GetCommandLineArgs().Select(_Cce.FromUnicode).ToList()" : ((n == "current-dir") ? "_Cce.FromUnicode(Directory.GetCurrentDirectory())" : (((((long)n.Length) > 0) && is_upper_letter(((long)n[(int)0]))) ? ("new " + (sanitize(n) + "()")) : ((lookup_arity(arities, n) == 0) ? (sanitize(n) + "()") : ((Func<long, EmitResult>)((ar) => ((ar >= 2) ? (emit_partial_wrappers(0, ar) + (sanitize(n) + ("(" + (emit_partial_params(0, ar) + ")")))) : sanitize(n))))(lookup_arity(arities, n))))))), IrBinary(var op, var l, var r, var ty) => emit_binary(op, l, r, ty, arities), IrNegate(var operand) => ("(-" + Enumerable.Concat(emit_expr(operand, arities), ")").ToList()), IrIf(var c, var t, var el, var ty) => ("(" + Enumerable.Concat(emit_expr(c, arities), (" ? " + Enumerable.Concat(emit_expr(t, arities), (" : " + Enumerable.Concat(emit_expr(el, arities), ")").ToList())).ToList())).ToList()), IrLet(var name, var ty, var val, var body) => emit_let(name, ty, val, body, arities), IrApply(var f, var a, var ty) => emit_apply(e, arities), IrLambda(var @params, var body, var ty) => emit_lambda(@params, body, arities), IrList(var elems, var ty) => emit_list(elems, ty, arities), IrMatch(var scrut, var branches, var ty) => emit_match(scrut, branches, ty, arities), IrDo(var stmts, var ty) => emit_do(stmts, ty, arities), IrHandle(var eff, var body, var clauses, var ty) => emit_handle(eff, body, clauses, ty, arities), IrRecord(var name, var fields, var ty) => emit_record(name, fields, arities), IrFieldAccess(var rec, var field, var ty) => Enumerable.Concat(emit_expr(rec, arities), ("." + sanitize(field))).ToList(), IrFork(var body, var ty) => ("Task.Run(() => (" + Enumerable.Concat(emit_expr(body, arities), ")(null))").ToList()), IrAwait(var task, var ty) => ("(" + Enumerable.Concat(emit_expr(task, arities), ").Result").ToList()), IrError(var msg, var ty) => ("/* error: " + (msg + " */ default")), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static string hex_digit(long n) => ((n == 0) ? "0" : ((n == 1) ? "1" : ((n == 2) ? "2" : ((n == 3) ? "3" : ((n == 4) ? "4" : ((n == 5) ? "5" : ((n == 6) ? "6" : ((n == 7) ? "7" : ((n == 8) ? "8" : ((n == 9) ? "9" : ((n == 10) ? "A" : ((n == 11) ? "B" : ((n == 12) ? "C" : ((n == 13) ? "D" : ((n == 14) ? "E" : ((n == 15) ? "F" : "?"))))))))))))))));

    public static string hex4(long n) => ("00" + (hex_digit((n / 16)) + hex_digit((n - ((n / 16) * 16)))));

    public static string escape_cce_char(long c) => ((c == 86) ? "\\\\" : ((c == 72) ? "\\\"" : ((c >= 2) ? ((c < 127) ? ((char)c).ToString() : ("\\u" + hex4(c))) : ("\\u" + hex4(c)))));

    public static string escape_text_loop(string s, long i, long len, string acc)
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
            var _tco_3 = ((Func<List<object>>)(() => { var _l = acc; _l.Add(escape_cce_char(((long)s[(int)i]))); return _l; }))();
            s = _tco_0;
            i = _tco_1;
            len = _tco_2;
            acc = _tco_3;
            continue;
            }
        }
    }

    public static string escape_text(string s) => string.Concat(escape_text_loop(s, 0, ((long)s.Length), new List<object>()));

    public static string emit_bin_op(IRBinaryOp op) => op switch { IrAddInt { } => "+", IrSubInt { } => "-", IrMulInt { } => "*", IrDivInt { } => "/", IrPowInt { } => "^", IrAddNum { } => "+", IrSubNum { } => "-", IrMulNum { } => "*", IrDivNum { } => "/", IrEq { } => "==", IrNotEq { } => "!=", IrLt { } => "<", IrGt { } => ">", IrLtEq { } => "<=", IrGtEq { } => ">=", IrAnd { } => "&&", IrOr { } => "||", IrAppendText { } => "+", IrAppendList { } => "+", IrConsList { } => "+", _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static EmitResult emit_binary(CodegenState op, IRBinaryOp l, IRExpr r, IRExpr ty, object arities) => op switch { IrAppendList { } => ("Enumerable.Concat(" + Enumerable.Concat(emit_expr(l, arities), (", " + Enumerable.Concat(emit_expr(r, arities), ").ToList()").ToList())).ToList()), IrConsList { } => ("new List<" + (cs_type(ir_expr_type(l)) + ("> { " + Enumerable.Concat(emit_expr(l, arities), (" }.Concat(" + Enumerable.Concat(emit_expr(r, arities), ").ToList()").ToList())).ToList()))), _ => ("(" + Enumerable.Concat(emit_expr(l, arities), (" " + (emit_bin_op(op) + (" " + Enumerable.Concat(emit_expr(r, arities), ")").ToList())))).ToList()), };

    public static EmitResult emit_let(CodegenState name, string ty, IRExpr val, IRExpr body, object arities) => ("((Func<" + (cs_type(ty) + (", " + (cs_type(ir_expr_type(body)) + (">)((" + (sanitize(name) + (") => " + Enumerable.Concat(emit_expr(body, arities), ("))(" + Enumerable.Concat(emit_expr(val, arities), ")").ToList())).ToList())))))));

    public static Func<long, string> emit_lambda(List<IRParam> @params, IRExpr body, List<string> arities) => ((((long)@params.Count) == 0) ? ("(() => " + Enumerable.Concat(emit_expr(body, arities), ")").ToList()) : ((((long)@params.Count) == 1) ? ((Func<IRParam, string>)((p) => ("((" + (cs_type(p.type_val) + (" " + (sanitize(p.name) + (") => " + Enumerable.Concat(emit_expr(body, arities), ")").ToList())))))))(@params[(int)0]) : ("(() => " + Enumerable.Concat(emit_expr(body, arities), ")").ToList())));

    public static EmitResult emit_list(CodegenState elems, List<IRExpr> ty, object arities) => ((((long)elems.Count) == 0) ? ("new List<" + (cs_type(ty) + ">()")) : ("new List<" + (cs_type(ty) + ("> { " + Enumerable.Concat(emit_list_elems(elems, 0, arities), " }").ToList()))));

    public static Func<long, string> emit_list_elems(List<IRExpr> elems, List<string> i, long arities) => ((i == ((long)elems.Count)) ? "" : ((i == (((long)elems.Count) - 1)) ? emit_expr(elems[(int)i], arities) : Enumerable.Concat(emit_expr(elems[(int)i], arities), (", " + emit_list_elems(elems, (i + 1), arities))).ToList()));

    public static EmitResult emit_match(CodegenState scrut, IRExpr branches, List<IRBranch> ty, object arities) => ((Func<string, EmitResult>)((arms) => ((Func<bool, EmitResult>)((needs_wild) => Enumerable.Concat(emit_expr(scrut, arities), (" switch { " + (arms + ((needs_wild ? "_ => throw new InvalidOperationException(\"Non-exhaustive match\"), " : "") + "}")))).ToList()))((has_any_catch_all(branches, 0) ? false : true))))(emit_match_arms(branches, 0, arities));

    public static string emit_match_arms(List<IRBranch> branches, long i, List<ArityEntry> arities) => ((i == ((long)branches.Count)) ? "" : ((Func<IRBranch, string>)((arm) => ((Func<Func<long, Func<IRPat, EmitPatternResult>>, string>)((this_arm) => (is_catch_all(arm.pattern) ? this_arm : Enumerable.Concat(this_arm, emit_match_arms(branches, (i + 1), arities)).ToList())))(Enumerable.Concat(emit_pattern(arm.pattern), (" => " + Enumerable.Concat(emit_expr(arm.body, arities), ", ").ToList())).ToList())))(branches[(int)i]));

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

    public static Func<long, Func<IRPat, EmitPatternResult>> emit_pattern(CodegenState p) => p switch { IrVarPat(var name, var ty) => (cs_type(ty) + (" " + sanitize(name))), IrLitPat(var text, var ty) => text, IrCtorPat(var name, var subs, var ty) => ((((long)subs.Count) == 0) ? (sanitize(name) + " { }") : (sanitize(name) + ("(" + (emit_sub_patterns(subs, 0) + ")")))), IrWildPat { } => "_", _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static string emit_sub_patterns(List<IRPat> subs, long i) => ((i == ((long)subs.Count)) ? "" : ((Func<IRPat, string>)((sub) => (emit_sub_pattern(sub) + (((i < (((long)subs.Count) - 1)) ? ", " : "") + emit_sub_patterns(subs, (i + 1))))))(subs[(int)i]));

    public static string emit_sub_pattern(IRPat p) => p switch { IrVarPat(var name, var ty) => ("var " + sanitize(name)), IrCtorPat(var name, var subs, var ty) => emit_pattern(p), IrWildPat { } => "_", IrLitPat(var text, var ty) => text, _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static string emit_do(List<IRDoStmt> stmts, List<string> ty, long arities) => ((Func<string, string>)((ret_type) => ((Func<long, string>)((len) => ty switch { VoidTy { } => ("((Func<object>)(() => { " + (emit_do_stmts(stmts, 0, len, false, arities) + " return null; }))()")), NothingTy { } => ("((Func<object>)(() => { " + (emit_do_stmts(stmts, 0, len, false, arities) + " return null; }))()")), ErrorTy { } => ("((Func<object>)(() => { " + (emit_do_stmts(stmts, 0, len, false, arities) + " return null; }))()")), _ => ((len == 0) ? ("((Func<" + (ret_type + ">)(() => { return null; }))()")) : ("((Func<" + (ret_type + (">)(() => { " + (emit_do_stmts(stmts, 0, len, true, arities) + " }))()"))))), }))(((long)stmts.Count))))(cs_type(ty));

    public static string emit_do_stmts(List<IRDoStmt> stmts, List<string> i, long len, long needs_return, object arities) => ((i == len) ? "" : ((Func<IRDoStmt, string>)((s) => ((Func<bool, string>)((is_last) => ((Func<long, string>)((use_return) => (emit_do_stmt(s, use_return, arities) + (" " + emit_do_stmts(stmts, (i + 1), len, needs_return, arities)))))((is_last ? needs_return : false))))((i == (len - 1)))))(stmts[(int)i]));

    public static string emit_do_stmt(IRDoStmt s, List<string> use_return, long arities) => s switch { IrDoBind(var name, var ty, var val) => ("var " + (sanitize(name) + (" = " + Enumerable.Concat(emit_expr(val, arities), ";").ToList()))), IrDoExec(var e) => (use_return ? ("return " + Enumerable.Concat(emit_expr(e, arities), ";").ToList()) : Enumerable.Concat(emit_expr(e, arities), ";").ToList()), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static EmitResult emit_record(CodegenState name, List<IRFieldVal> fields, CodexType arities) => ("new " + (sanitize(name) + ("(" + (emit_record_fields(fields, 0, arities) + ")"))));

    public static string emit_record_fields(List<IRFieldVal> fields, long i, List<ArityEntry> arities) => ((i == ((long)fields.Count)) ? "" : ((Func<IRFieldVal, string>)((f) => (sanitize(f.name) + (": " + Enumerable.Concat(emit_expr(f.value, arities), (((i < (((long)fields.Count) - 1)) ? ", " : "") + emit_record_fields(fields, (i + 1), arities))).ToList()))))(fields[(int)i]));

    public static string emit_handle(string eff, IRExpr body, List<IRHandleClause> clauses, List<string> ty, long arities) => ((Func<string, string>)((ret_type) => ("((Func<" + (ret_type + (">)(() => { " + Enumerable.Concat(emit_handle_clauses(clauses, ret_type, arities), ("return " + Enumerable.Concat(emit_expr(body, arities), "; }))()").ToList())).ToList())))))(cs_type(ty));

    public static Func<long, string> emit_handle_clauses(List<IRHandleClause> clauses, List<string> ret_type, long arities) => emit_handle_clauses_loop(clauses, 0, ret_type, arities);

    public static string emit_handle_clauses_loop(List<IRHandleClause> clauses, long i, string ret_type, List<ArityEntry> arities) => ((i == ((long)clauses.Count)) ? "" : ((Func<IRHandleClause, string>)((c) => ("Func<Func<" + (ret_type + (", " + (ret_type + (">, " + (ret_type + ("> _handle_" + (sanitize(c.op_name) + ("_ = (" + (sanitize(c.resume_name) + (") => { return " + Enumerable.Concat(emit_expr(c.body, arities), ("; }; " + emit_handle_clauses_loop(clauses, (i + 1), ret_type, arities))).ToList())))))))))))))(clauses[(int)i]));

    public static List<long> cdx_magic() => new List<long> { 67, 68, 88, 49 };

    public static long cdx_format_version() => 1;

    public static long cdx_fixed_header_size() => 224;

    public static long cdx_content_hash_size() => 32;

    public static long cdx_author_key_size() => 32;

    public static long cdx_signature_size() => 64;

    public static long cdx_flag_bare_metal() => 1;

    public static long cdx_flag_needs_heap() => 2;

    public static long cdx_flag_needs_stack_guard() => 4;

    public static long cdx_flag_has_proofs() => 8;

    public static List<long> cdx_header_bytes(long flags, long cap_off, long cap_sz, long proof_off, long proof_sz, long text_off, long text_sz, long rodata_off, long rodata_sz, long entry, long stack_sz, long heap_sz) => Enumerable.Concat(cdx_magic(), Enumerable.Concat(write_i16(cdx_format_version()), Enumerable.Concat(write_i16(flags), Enumerable.Concat(pad_zeros(cdx_content_hash_size()), Enumerable.Concat(pad_zeros(cdx_author_key_size()), Enumerable.Concat(pad_zeros(cdx_signature_size()), Enumerable.Concat(write_i64(cap_off), Enumerable.Concat(write_i64(cap_sz), Enumerable.Concat(write_i64(proof_off), Enumerable.Concat(write_i64(proof_sz), Enumerable.Concat(write_i64(text_off), Enumerable.Concat(write_i64(text_sz), Enumerable.Concat(write_i64(rodata_off), Enumerable.Concat(write_i64(rodata_sz), Enumerable.Concat(write_i64(entry), Enumerable.Concat(write_i32(stack_sz), Enumerable.Concat(write_i32(heap_sz), Enumerable.Concat(write_i16(0), Enumerable.Concat(write_i16(0), write_i32(0)).ToList()).ToList()).ToList()).ToList()).ToList()).ToList()).ToList()).ToList()).ToList()).ToList()).ToList()).ToList()).ToList()).ToList()).ToList()).ToList()).ToList()).ToList()).ToList();

    public static List<long> build_cdx(long flags, long entry_offset, long stack_size, long heap_size, List<long> text, List<long> rodata) => ((Func<long, List<long>>)((hdr) => ((Func<long, List<long>>)((text_size) => ((Func<long, List<long>>)((rodata_size) => Enumerable.Concat(cdx_header_bytes(flags, hdr, 0, hdr, 0, hdr, text_size, (hdr + text_size), rodata_size, entry_offset, stack_size, heap_size), Enumerable.Concat(text, rodata).ToList()).ToList()))(((long)rodata.Count))))(((long)text.Count))))(cdx_fixed_header_size());

    public static List<long> build_cdx_bare_metal(long entry_offset, long stack_size, long heap_size, List<long> text, List<long> rodata) => build_cdx((cdx_flag_bare_metal() + cdx_flag_needs_heap()), entry_offset, stack_size, heap_size, text, rodata);

    public static string emit_type_defs(List<ATypeDef> tds, long i) => ((i == ((long)tds.Count)) ? "" : (emit_type_def(tds[(int)i]) + ("\u0001" + emit_type_defs(tds, (i + 1)))));

    public static string emit_type_def(ATypeDef td) => td switch { ARecordTypeDef(var name, var tparams, var fields) => (name.value + (" = record {" + ((_p0_) => emit_record_field_defs(fields, 0, _p0_) + "\u0001}\u0001"))), AVariantTypeDef(var name, var tparams, var ctors) => (name.value + (" =\u0001" + (_p0_) => (_p1_) => emit_variant_ctors(ctors, 0, _p0_, _p1_))), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static string emit_record_field_defs(List<ARecordFieldDef> fields, long i) => ((i == ((long)fields.Count)) ? "" : ((Func<ARecordFieldDef, string>)((f) => ((Func<string, string>)((comma) => (comma + ("\u0001 " + (f.name.value + (" : " + (emit_type_expr(f.type_expr) + (_p0_) => emit_record_field_defs(fields, (i + 1), _p0_))))))))(((i > 0) ? "," : ""))))(fields[(int)i]));

    public static string emit_variant_ctors(List<AVariantCtorDef> ctors, long i) => ((i == ((long)ctors.Count)) ? "" : ((Func<AVariantCtorDef, string>)((c) => (" | " + (c.name.value + ((_p0_) => emit_ctor_fields(c.fields, 0, _p0_) + ("\u0001" + (_p0_) => (_p1_) => emit_variant_ctors(ctors, (i + 1), _p0_, _p1_)))))))(ctors[(int)i]));

    public static string emit_ctor_fields(List<ATypeExpr> fields, long i) => ((i == ((long)fields.Count)) ? "" : (" (" + (emit_type_expr(fields[(int)i]) + (")" + (_p0_) => emit_ctor_fields(fields, (i + 1), _p0_)))));

    public static string emit_type_expr(ATypeExpr te) => te switch { ANamedType(var name) => name.value, AFunType(var p, var r) => ("(" + (emit_type_expr(p) + (" -> " + (emit_type_expr(r) + ")")))), AAppType(var @base, var args) => (emit_type_expr(@base) + (" " + emit_type_expr_args(args, 0))), AEffectType(var effs, var ret) => ("[" + (emit_effect_names(effs, 0) + ("] " + emit_type_expr(ret)))), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static string emit_type_expr_args(List<ATypeExpr> args, long i) => ((i == ((long)args.Count)) ? "" : ((Func<string, string>)((sep) => (sep + (emit_type_expr_wrapped(args[(int)i]) + emit_type_expr_args(args, (i + 1))))))(((i > 0) ? " " : "")));

    public static string emit_type_expr_wrapped(ATypeExpr te) => te switch { AFunType(var p, var r) => ("(" + (emit_type_expr(te) + ")")), AAppType(var @base, var args) => ("(" + (emit_type_expr(te) + ")")), _ => emit_type_expr(te), };

    public static string emit_effect_names(List<Name> effs, long i) => ((i == ((long)effs.Count)) ? "" : ((Func<string, string>)((sep) => (sep + (effs[(int)i].value + emit_effect_names(effs, (i + 1))))))(((i > 0) ? ", " : "")));

    public static string emit_type(CodexType ty)
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
            return (wrap_fun_param(p) + (" -> " + emit_type(r)));
            }
            else if (_tco_s is ListTy _tco_m9)
            {
                var elem = _tco_m9.Field0;
            return ("List " + wrap_complex(elem));
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
            return ("[" + (emit_type_effect_names(effs, 0) + ("] " + emit_type(ret))));
            }
        }
    }

    public static string wrap_fun_param(CodexType ty) => ty switch { FunTy(var p, var r) => ("(" + (emit_type(ty) + ")")), _ => emit_type(ty), };

    public static string wrap_complex(CodexType ty) => ty switch { FunTy(var p, var r) => ("(" + (emit_type(ty) + ")")), ListTy(var elem) => ("(" + (emit_type(ty) + ")")), _ => emit_type(ty), };

    public static string emit_type_effect_names(List<Name> effs, long i) => ((i == ((long)effs.Count)) ? "" : ((Func<string, string>)((sep) => (sep + (effs[(int)i].value + emit_type_effect_names(effs, (i + 1))))))(((i > 0) ? ", " : "")));

    public static string emit_def(IRDef d, List<string> ctor_names) => (skip_def(d, ctor_names) ? "" : ((Func<string, string>)((sig) => (sig + (" =\u0001 " + (emit_expr(d.body, ctor_names)(1) + "\u0001")))))((d.name + (" : " + (emit_type(d.type_val) + ("\u0001" + (d.name + emit_def_params(d.@params, 0))))))));

    public static bool is_match_body(IRExpr e) => e switch { IrMatch(var scrut, var branches, var ty) => true, _ => false, };

    public static bool skip_def(IRDef d, List<string> ctor_names) => (list_contains(ctor_names, d.name) ? true : ((((long)d.name.Length) == 0) ? true : ((Func<long, bool>)((first) => ((first < 13) ? true : ((first > 38) ? true : false))))(((long)d.name[(int)0]))));

    public static bool is_upper(long c) => ((Func<long, bool>)((code) => ((Func<long, bool>)((code_z) => ((c >= code) && (c <= code_z))))(((long)"Z"[(int)0]))))(((long)"A"[(int)0]));

    public static bool is_error_body(IRExpr e) => e switch { IrError(var msg, var ty) => true, _ => false, };

    public static string error_message(IRExpr e) => e switch { IrError(var msg, var ty) => msg, _ => "", };

    public static bool is_lower_start(string s) => ((((long)s.Length) == 0) ? false : ((Func<long, bool>)((c) => ((Func<long, bool>)((code_a) => ((Func<long, bool>)((code_z) => ((c >= code_a) && (c <= code_z))))(((long)"z"[(int)0]))))(((long)"a"[(int)0]))))(((long)s[(int)0])));

    public static string emit_def_params(List<IRParam> @params, long i) => ((i == ((long)@params.Count)) ? "" : ((Func<IRParam, string>)((p) => (" (" + (p.name + (")" + emit_def_params(@params, (i + 1)))))))(@params[(int)i]));

    public static EmitResult emit_expr(CodegenState e, IRExpr ctors, object indent) => e switch { IrIntLit(var n) => _Cce.FromUnicode(n.ToString()), IrNumLit(var n) => _Cce.FromUnicode(n.ToString()), IrTextLit(var s) => ("\"" + (escape_text(s) + "\"")), IrBoolLit(var b) => (b ? "True" : "False"), IrCharLit(var n) => ("'" + (escape_char(n) + "'")), IrName(var n, var ty) => n, IrBinary(var op, var l, var r, var ty) => emit_binary(op, l, r, ctors, indent), IrNegate(var operand) => ("0 - " + emit_expr(operand, ctors)(indent)), IrIf(var c, var t, var el, var ty) => emit_if(c, t, el, ctors, indent, 0), IrLet(var name, var ty, var val, var body) => emit_let(name, val, body, ctors, indent), IrApply(var f, var a, var ty) => emit_apply(e, ctors)(indent), IrLambda(var @params, var body, var ty) => emit_lambda(@params, body, ctors)(indent), IrList(var elems, var ty) => emit_list(elems, ctors, indent), IrMatch(var scrut, var branches, var ty) => emit_match(scrut, branches, ctors, indent), IrDo(var stmts, var ty) => emit_do(stmts, ctors, indent), IrRecord(var name, var fields, var ty) => emit_record(name, fields, ctors)(indent), IrFieldAccess(var rec, var field, var ty) => emit_field_access(rec, field, ctors, indent), IrFork(var body, var ty) => ("fork (" + Enumerable.Concat(emit_expr(body, ctors)(indent), ")").ToList()), IrAwait(var task, var ty) => ("await (" + Enumerable.Concat(emit_expr(task, ctors)(indent), ")").ToList()), IrHandle(var eff, var body, var clauses, var ty) => emit_handle(eff, body, clauses, ctors, indent), IrError(var msg, var ty) => "0", _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static EmitResult emit_binary(CodegenState op, IRBinaryOp l, IRExpr r, IRExpr ctors, object indent) => (wrap_binary_left(op, l, ctors, indent) + (" " + (bin_op_text(op) + (" " + wrap_binary_right(op, r, ctors, indent)))));

    public static string wrap_binary_left(IRBinaryOp parent_op, IRExpr e, List<string> ctors, long indent) => e switch { IrBinary(var child_op, var l2, var r2, var ty) => ((bin_op_prec(child_op) < bin_op_prec(parent_op)) ? ("(" + (emit_expr(e, ctors)(indent) + ")")) : emit_expr(e, ctors)(indent)), _ => emit_expr(e, ctors)(indent), };

    public static string wrap_binary_right(IRBinaryOp parent_op, IRExpr e, List<string> ctors, long indent) => e switch { IrBinary(var child_op, var l2, var r2, var ty) => (needs_right_wrap(parent_op, child_op) ? ("(" + (emit_expr(e, ctors)(indent) + ")")) : emit_expr(e, ctors)(indent)), _ => emit_expr(e, ctors)(indent), };

    public static bool needs_right_wrap(IRBinaryOp parent, IRBinaryOp child) => ((bin_op_prec(child) < bin_op_prec(parent)) ? true : ((bin_op_prec(child) > bin_op_prec(parent)) ? false : ((bin_op_text(parent) == bin_op_text(child)) ? (is_associative_op(parent) ? false : true) : true)));

    public static bool is_associative_op(IRBinaryOp op) => op switch { IrAddInt { } => true, IrAddNum { } => true, IrMulInt { } => true, IrMulNum { } => true, IrAppendText { } => true, IrAppendList { } => true, IrConsList { } => true, IrAnd { } => true, IrOr { } => true, _ => false, };

    public static long bin_op_prec(IRBinaryOp op) => op switch { IrOr { } => 2, IrAnd { } => 3, IrEq { } => 4, IrNotEq { } => 4, IrLt { } => 4, IrGt { } => 4, IrLtEq { } => 4, IrGtEq { } => 4, IrAppendText { } => 5, IrAppendList { } => 5, IrConsList { } => 5, IrAddInt { } => 6, IrSubInt { } => 6, IrAddNum { } => 6, IrSubNum { } => 6, IrMulInt { } => 7, IrDivInt { } => 7, IrMulNum { } => 7, IrDivNum { } => 7, IrPowInt { } => 8, _ => 0, };

    public static string bin_op_text(IRBinaryOp op) => op switch { IrAddInt { } => "+", IrSubInt { } => "-", IrMulInt { } => "*", IrDivInt { } => "/", IrPowInt { } => "^", IrAddNum { } => "+", IrSubNum { } => "-", IrMulNum { } => "*", IrDivNum { } => "/", IrEq { } => "==", IrNotEq { } => "/=", IrLt { } => "<", IrGt { } => ">", IrLtEq { } => "<=", IrGtEq { } => ">=", IrAnd { } => "&", IrOr { } => "|", IrAppendText { } => "++", IrAppendList { } => "++", IrConsList { } => "::", _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static EmitResult emit_if(CodegenState c, IRExpr t, IRExpr el, IRExpr ctors, object indent, object else_offset) => ("if " + Enumerable.Concat(emit_expr(c, ctors)(indent), (" then " + Enumerable.Concat(emit_expr(t, ctors)(indent), ("\u0001" + (make_indent(indent) + (pad_spaces(else_offset) + ("else " + emit_else(el, ctors, indent, else_offset)))))).ToList())).ToList());

    public static string emit_else(IRExpr el, List<string> ctors, long indent, long else_offset) => el switch { IrIf(var c2, var t2, var el2, var ty) => emit_if(c2, t2, el2, ctors, indent, else_offset), _ => emit_expr(el, ctors)(indent), };

    public static string pad_spaces(long n) => ((n <= 0) ? "" : (" " + pad_spaces((n - 1))));

    public static EmitResult emit_let(CodegenState name, string val, IRExpr body, IRExpr ctors, object indent) => ("let " + Enumerable.Concat(name, (" = " + Enumerable.Concat(emit_expr(val, ctors)((indent + 1)), ("\u0001" + (make_indent(indent) + ("in " + emit_let_body(body, ctors, indent))))).ToList())).ToList());

    public static string emit_let_body(IRExpr body, List<string> ctors, long indent) => body switch { IrIf(var c, var t, var el, var ty) => emit_if(c, t, el, ctors, indent, 3), _ => emit_expr(body, ctors)(indent), };

    public static Func<CodexType, EmitResult> emit_apply(CodegenState e, IRExpr ctors, IRExpr indent) => ((Func<ApplyChain, Func<CodexType, EmitResult>>)((chain) => ((Func<IRExpr, Func<CodexType, EmitResult>>)((func) => ((Func<List<IRExpr>, Func<CodexType, EmitResult>>)((args) => Enumerable.Concat(emit_expr(func, ctors)(indent), emit_apply_args(args, ctors, indent)(0)(((long)args.Count))(is_ctor_name(func, ctors))).ToList()))(chain.args)))(chain.root)))(collect_apply_chain(e, new List<IRExpr>()));

    public static string emit_apply_args(List<IRExpr> args, List<string> ctors, long indent, long i, long len, bool is_ctor) => ((i == len) ? "" : ((Func<IRExpr, string>)((arg) => (" " + (wrap_arg(arg, ctors, indent, is_ctor) + emit_apply_args(args, ctors, indent)((i + 1))(len)(is_ctor)))))(args[(int)i]));

    public static string wrap_arg(IRExpr e, List<string> ctors, long indent, bool is_ctor) => (needs_parens(e, is_ctor) ? ("(" + (emit_expr(e, ctors)(indent) + ")")) : emit_expr(e, ctors)(indent));

    public static bool is_ctor_name(IRExpr e, List<string> ctors) => e switch { IrName(var n, var ty) => list_contains(ctors, n), _ => false, };

    public static bool needs_parens(IRExpr e, bool is_ctor) => e switch { IrApply(var f, var a, var ty) => true, IrBinary(var op, var l, var r, var ty) => true, IrIf(var c, var t, var el, var ty) => true, IrLet(var name, var ty, var val, var body) => true, IrMatch(var scrut, var branches, var ty) => true, IrNegate(var operand) => true, IrLambda(var @params, var body, var ty) => true, IrFieldAccess(var rec, var field, var ty) => true, IrRecord(var name, var fields, var ty) => true, _ => false, };

    public static string emit_lambda(List<IRParam> @params, IRExpr body, List<string> ctors, long indent) => ("\\" + (emit_lambda_params(@params, 0) + (" -> " + emit_expr(body, ctors)(indent))));

    public static string emit_lambda_params(List<IRParam> @params, long i) => ((i == ((long)@params.Count)) ? "" : ((Func<IRParam, string>)((p) => ((Func<string, string>)((sep) => (sep + (p.name + emit_lambda_params(@params, (i + 1))))))(((i > 0) ? " " : ""))))(@params[(int)i]));

    public static EmitResult emit_list(CodegenState elems, List<IRExpr> ctors, object indent) => ("[" + (emit_list_elems(elems, ctors, indent)(0) + "]"));

    public static string emit_list_elems(List<IRExpr> elems, List<string> ctors, long indent, long i) => ((i == ((long)elems.Count)) ? "" : ((Func<string, string>)((sep) => (sep + (emit_expr(elems[(int)i], ctors)(indent) + emit_list_elems(elems, ctors, indent)((i + 1))))))(((i > 0) ? ", " : "")));

    public static EmitResult emit_match(CodegenState scrut, IRExpr branches, List<IRBranch> ctors, object indent) => ("when " + Enumerable.Concat(emit_expr(scrut, ctors)(indent), emit_branches(branches, ctors, indent, 0)).ToList());

    public static string emit_branches(List<IRBranch> branches, List<string> ctors, long indent, long i) => ((i == ((long)branches.Count)) ? "" : ((Func<IRBranch, string>)((b) => ("\u0001" + (make_indent((indent + 1)) + ("if " + Enumerable.Concat(emit_pattern(b.pattern), (" -> " + (emit_expr(b.body, ctors)((indent + 1)) + emit_branches(branches, ctors, indent, (i + 1))))).ToList())))))(branches[(int)i]));

    public static Func<long, Func<IRPat, EmitPatternResult>> emit_pattern(CodegenState p) => p switch { IrVarPat(var name, var ty) => name, IrLitPat(var text, var ty) => text, IrCtorPat(var name, var subs, var ty) => (name + emit_sub_patterns(subs, 0)), IrWildPat { } => "_", _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static string emit_sub_patterns(List<IRPat> subs, long i) => ((i == ((long)subs.Count)) ? "" : (" (" + Enumerable.Concat(emit_pattern(subs[(int)i]), (")" + emit_sub_patterns(subs, (i + 1)))).ToList()));

    public static string emit_do(List<IRDoStmt> stmts, List<string> ctors, long indent) => ("do" + (_p0_) => emit_do_stmts(stmts, ctors, indent, 0, _p0_));

    public static string emit_do_stmts(List<IRDoStmt> stmts, List<string> ctors, long indent, long i) => ((i == ((long)stmts.Count)) ? "" : ((Func<IRDoStmt, string>)((s) => ("\u0001" + (make_indent((indent + 1)) + (emit_do_stmt(s, ctors, (indent + 1)) + (_p0_) => emit_do_stmts(stmts, ctors, indent, (i + 1), _p0_))))))(stmts[(int)i]));

    public static string emit_do_stmt(IRDoStmt s, List<string> ctors, long indent) => s switch { IrDoBind(var name, var ty, var val) => (name + (" <- " + emit_expr(val, ctors)(indent))), IrDoExec(var e) => emit_expr(e, ctors)(indent), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static EmitResult emit_record(CodegenState name, List<IRFieldVal> fields, CodexType ctors, object indent) => ((((long)fields.Count) <= 1) ? Enumerable.Concat(name, (" {" + (emit_record_fields_inline(fields, ctors, indent, 0) + " }"))).ToList() : Enumerable.Concat(name, (" {\u0001" + (emit_record_fields_multi(fields, ctors, indent, 0) + (make_indent(indent) + "}")))).ToList());

    public static string emit_record_fields_inline(List<IRFieldVal> fields, List<string> ctors, long indent, long i) => ((i == ((long)fields.Count)) ? "" : ((Func<IRFieldVal, string>)((f) => ((Func<string, string>)((sep) => (sep + (" " + (f.name + (" = " + (emit_expr(f.value, ctors)(indent) + emit_record_fields_inline(fields, ctors, indent, (i + 1)))))))))(((i > 0) ? "," : ""))))(fields[(int)i]));

    public static string emit_record_fields_multi(List<IRFieldVal> fields, List<string> ctors, long indent, long i) => ((i == ((long)fields.Count)) ? "" : ((Func<IRFieldVal, string>)((f) => ((Func<string, string>)((comma) => (make_indent((indent + 1)) + (f.name + (" = " + (emit_expr(f.value, ctors)((indent + 1)) + (comma + ("\u0001" + emit_record_fields_multi(fields, ctors, indent, (i + 1))))))))))(((i < (((long)fields.Count) - 1)) ? "," : ""))))(fields[(int)i]));

    public static EmitResult emit_field_access(CodegenState rec, IRExpr field, string ctors, object indent) => rec switch { IrName(var n, var ty) => (n + ("." + field)), IrFieldAccess(var r, var f, var ty) => Enumerable.Concat(emit_field_access(r, f, ctors, indent), ("." + field)).ToList(), _ => ("(" + Enumerable.Concat(emit_expr(rec, ctors)(indent), (")." + field)).ToList()), };

    public static string emit_handle(string eff, IRExpr body, List<IRHandleClause> clauses, List<string> ctors, long indent) => ("handle " + (emit_expr(body, ctors)(indent) + (" with" + emit_handle_clauses(clauses, ctors, indent)(0))));

    public static string emit_handle_clauses(List<IRHandleClause> clauses, List<string> ctors, long indent, long i) => ((i == ((long)clauses.Count)) ? "" : ((Func<IRHandleClause, string>)((c) => ("\u0001" + (make_indent((indent + 1)) + (c.op_name + (" (" + (c.resume_name + (") -> " + (emit_expr(c.body, ctors)((indent + 1)) + emit_handle_clauses(clauses, ctors, indent)((i + 1)))))))))))(clauses[(int)i]));

    public static Func<long, Func<List<string>, Func<List<string>, CtorCollectResult>>> collect_ctor_names(List<ATypeDef> type_defs, long i) => ((i == ((long)type_defs.Count)) ? new List<object>() : ((Func<ATypeDef, List<string>>)((td) => ((Func<List<string>, List<string>>)((names) => Enumerable.Concat(names, collect_ctor_names(type_defs, (i + 1))).ToList()))(td switch { AVariantTypeDef(var name, var tparams, var ctors) => collect_variant_ctor_names(ctors, 0), ARecordTypeDef(var name, var tparams, var fields) => new List<string> { name.value }, _ => throw new InvalidOperationException("Non-exhaustive match"), })))(type_defs[(int)i]));

    public static List<string> collect_variant_ctor_names(List<AVariantCtorDef> ctors, long i) => ((i == ((long)ctors.Count)) ? new List<string>() : ((Func<AVariantCtorDef, List<string>>)((c) => Enumerable.Concat(new List<string> { c.name.value }, collect_variant_ctor_names(ctors, (i + 1))).ToList()))(ctors[(int)i]));

    public static bool is_simple(IRExpr e) => e switch { IrIntLit(var n) => true, IrNumLit(var n) => true, IrTextLit(var s) => true, IrBoolLit(var b) => true, IrCharLit(var n) => true, IrName(var n, var ty) => true, IrFieldAccess(var r, var f, var ty) => true, _ => false, };

    public static string make_indent(long n) => ((n == 0) ? "" : (" " + make_indent((n - 1))));

    public static string escape_text(string s) => escape_text_loop(s, 0, ((long)s.Length), "");

    public static string escape_text_loop(string s, long i, long len, string acc)
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
            var _tco_3 = (acc + escape_one_char(c));
            s = _tco_0;
            i = _tco_1;
            len = _tco_2;
            acc = _tco_3;
            continue;
            }
        }
    }

    public static string escape_one_char(long c) => ((c == ((long)"\\"[(int)0])) ? "\\\\" : ((c == ((long)"\""[(int)0])) ? "\\\"" : ((c == ((long)"\u0001"[(int)0])) ? "\\n" : ((char)c).ToString())));

    public static string escape_char(long c) => ((c == ((long)"\u0001"[(int)0])) ? "\\n" : ((c == ((long)"\\"[(int)0])) ? "\\\\" : ((c == ((long)"'"[(int)0])) ? "\\'" : ((char)c).ToString())));

    public static string emit_full_chapter(IRChapter m, List<ATypeDef> type_defs) => ((Func<Func<long, Func<List<string>, Func<List<string>, CtorCollectResult>>>, string>)((ctor_names) => ((Func<string, string>)((header) => (header + (emit_type_defs(type_defs, 0) + emit_all_defs(m.defs, ctor_names, 0)))))(((m.chapter_title == "") ? "" : ("Chapter: " + (m.chapter_title + ("\u0001\u0001" + ((m.prose == "") ? "" : (" " + (m.prose + "\u0001\u0001"))))))))))(collect_ctor_names(type_defs, 0));

    public static CodegenState emit_all_defs(CodegenState defs, List<IRDef> ctor_names, long i) => ((i == ((long)defs.Count)) ? "" : (emit_def(defs[(int)i], ctor_names) + ("\u0001" + emit_all_defs(defs, ctor_names, (i + 1)))));

    public static string emit_def_list(List<IRDef> defs, List<string> ctor_names, long i) => ((i == ((long)defs.Count)) ? "" : (emit_def(defs[(int)i], ctor_names) + ("\u0001" + emit_def_list(defs, ctor_names, (i + 1)))));

    public static List<IRDef> filter_defs(List<IRDef> defs, List<string> ctor_names, long i, long len, List<IRDef> acc) => ((i == len) ? acc : ((Func<IRDef, List<IRDef>>)((d) => ((Func<Func<List<IRDef>, List<IRDef>>, List<IRDef>>)((next) => (skip_def(d, ctor_names) ? next(acc) : (has_def_named(acc, d.name, 0, ((long)acc.Count)) ? ((Func<bool, List<IRDef>>)((dominated) => (dominated ? next(replace_def(acc, d.name, d, 0, ((long)acc.Count))) : next(acc))))((def_score(d) > def_score_named(acc, d.name, 0, ((long)acc.Count)))) : next(((Func<List<IRDef>>)(() => { var _l = acc; _l.Add(d); return _l; }))())))))((_p0_) => filter_defs(defs, ctor_names, (i + 1), len, _p0_))))(defs[(int)i]));

    public static long def_score(IRDef d) => ((((long)d.@params.Count) * 100) + body_depth(d.body));

    public static long def_score_named(List<IRDef> defs, string name, long i, long len)
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
            return def_score(defs[(int)i]);
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

    public static long body_depth(IRExpr e) => e switch { IrName(var n, var ty) => 1, IrIntLit(var n) => 1, IrTextLit(var s) => 1, IrBoolLit(var b) => 1, IrFieldAccess(var r, var f, var ty) => 2, IrApply(var f, var a, var ty) => (3 + body_depth(f)), IrLet(var name, var ty, var val, var body) => (5 + body_depth(body)), IrIf(var c, var t, var el, var ty) => (5 + body_depth(t)), IrMatch(var s, var bs, var ty) => 10, IrLambda(var ps, var b, var ty) => 5, IrDo(var stmts, var ty) => 5, IrRecord(var n, var fs, var ty) => 3, _ => 1, };

    public static bool has_def_named(List<IRDef> defs, string name, long i, long len)
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

    public static List<IRDef> replace_def(List<IRDef> defs, string name, IRDef new_def, long i, long len)
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
            return list_set(defs, i, new_def);
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

    public static List<IRDef> list_set(List<IRDef> xs, long idx, IRDef val) => list_set_loop(xs, idx, val, 0, ((long)xs.Count), new List<IRDef>());

    public static List<IRDef> list_set_loop(List<IRDef> xs, long idx, IRDef val, long i, long len, List<IRDef> acc)
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

    public static List<long> elf_magic() => new List<long> { 127, 69, 76, 70 };

    public static long elf_class_32() => 1;

    public static long elf_class_64() => 2;

    public static long elf_data_lsb() => 1;

    public static long elf_version_current() => 1;

    public static long elf_type_exec() => 2;

    public static long elf_machine_386() => 3;

    public static long elf_machine_x86_64() => 62;

    public static long pt_load() => 1;

    public static long pt_note() => 4;

    public static long pf_r() => 4;

    public static long pf_w() => 2;

    public static long pf_x() => 1;

    public static long pf_rwx() => 7;

    public static long pf_rw() => 6;

    public static long xen_elfnote_phys32_entry() => 18;

    public static List<long> xen_name() => new List<long> { 88, 101, 110, 0 };

    public static long elf_bare_metal_load_addr() => 1048576;

    public static long elf_bare_metal_heap_size() => 2097152;

    public static long elf_linux_base_addr() => 4194304;

    public static long elf_page_size() => 4096;

    public static long elf32_header_size() => 52;

    public static long elf32_phdr_size() => 32;

    public static long elf64_header_size() => 64;

    public static long elf64_phdr_size() => 56;

    public static long elf_align(long v, long a) => ((Func<long, long>)((r) => ((r == 0) ? v : ((v + a) - r))))(int_mod(v, a));

    public static List<long> write_i16(long v) => write_bytes(v, 2);

    public static List<long> pad_zeros(long n) => pad_zeros_acc(n, new List<long>());

    public static List<long> pad_zeros_acc(long n, List<long> acc)
    {
        while (true)
        {
            if ((n <= 0))
            {
            return acc;
            }
            else
            {
            var _tco_0 = (n - 1);
            var _tco_1 = ((Func<List<long>>)(() => { var _l = acc; _l.Add(0); return _l; }))();
            n = _tco_0;
            acc = _tco_1;
            continue;
            }
        }
    }

    public static List<long> elf_ident_32() => Enumerable.Concat(elf_magic(), Enumerable.Concat(new List<long> { elf_class_32(), elf_data_lsb(), elf_version_current() }, pad_zeros(9)).ToList()).ToList();

    public static List<long> elf_ident_64() => Enumerable.Concat(elf_magic(), Enumerable.Concat(new List<long> { elf_class_64(), elf_data_lsb(), elf_version_current() }, pad_zeros(9)).ToList()).ToList();

    public static List<long> elf32_header_bytes(long entry, long phoff, long phnum) => Enumerable.Concat(elf_ident_32(), Enumerable.Concat(write_i16(elf_type_exec()), Enumerable.Concat(write_i16(elf_machine_386()), Enumerable.Concat(write_i32(elf_version_current()), Enumerable.Concat(write_i32(entry), Enumerable.Concat(write_i32(phoff), Enumerable.Concat(write_i32(0), Enumerable.Concat(write_i32(0), Enumerable.Concat(write_i16(elf32_header_size()), Enumerable.Concat(write_i16(elf32_phdr_size()), Enumerable.Concat(write_i16(phnum), Enumerable.Concat(write_i16(0), Enumerable.Concat(write_i16(0), write_i16(0)).ToList()).ToList()).ToList()).ToList()).ToList()).ToList()).ToList()).ToList()).ToList()).ToList()).ToList()).ToList()).ToList();

    public static List<long> phdr_32(long ptype, long offset, long vaddr, long paddr, long filesz, long memsz, long flags, long palign) => Enumerable.Concat(write_i32(ptype), Enumerable.Concat(write_i32(offset), Enumerable.Concat(write_i32(vaddr), Enumerable.Concat(write_i32(paddr), Enumerable.Concat(write_i32(filesz), Enumerable.Concat(write_i32(memsz), Enumerable.Concat(write_i32(flags), write_i32(palign)).ToList()).ToList()).ToList()).ToList()).ToList()).ToList()).ToList();

    public static List<long> pvh_note(long entry_addr) => Enumerable.Concat(write_i32(4), Enumerable.Concat(write_i32(4), Enumerable.Concat(write_i32(xen_elfnote_phys32_entry()), Enumerable.Concat(xen_name(), write_i32(entry_addr)).ToList()).ToList()).ToList()).ToList();

    public static List<long> build_elf_32_bare(List<long> text, List<long> rodata, long entry_offset) => ((Func<long, List<long>>)((load_addr) => ((Func<long, List<long>>)((headers_end) => ((Func<long, List<long>>)((note_offset) => ((Func<long, List<long>>)((text_start) => ((Func<long, List<long>>)((text_end) => ((Func<long, List<long>>)((rodata_start) => ((Func<long, List<long>>)((file_size) => ((Func<long, List<long>>)((entry) => ((Func<long, List<long>>)((seg_filesz) => ((Func<long, List<long>>)((seg_memsz) => Enumerable.Concat(elf32_header_bytes(entry, elf32_header_size(), 2), Enumerable.Concat(phdr_32(pt_load(), text_start, load_addr, load_addr, seg_filesz, seg_memsz, pf_rwx(), elf_page_size()), Enumerable.Concat(phdr_32(pt_note(), note_offset, 0, 0, 20, 20, pf_r(), 4), Enumerable.Concat(pad_zeros((note_offset - headers_end)), Enumerable.Concat(pvh_note(entry), Enumerable.Concat(pad_zeros(((text_start - note_offset) - 20)), Enumerable.Concat(text, Enumerable.Concat(pad_zeros((rodata_start - text_end)), rodata).ToList()).ToList()).ToList()).ToList()).ToList()).ToList()).ToList()).ToList()))((seg_filesz + elf_bare_metal_heap_size()))))((file_size - text_start))))((load_addr + entry_offset))))((rodata_start + ((long)rodata.Count)))))(elf_align(text_end, 8))))((text_start + ((long)text.Count)))))(elf_align((note_offset + 20), 16))))(elf_align(headers_end, 4))))((elf32_header_size() + (elf32_phdr_size() * 2)))))(elf_bare_metal_load_addr());

    public static List<long> elf64_header_bytes(long entry, long phoff, long phnum) => Enumerable.Concat(elf_ident_64(), Enumerable.Concat(write_i16(elf_type_exec()), Enumerable.Concat(write_i16(elf_machine_x86_64()), Enumerable.Concat(write_i32(elf_version_current()), Enumerable.Concat(write_i64(entry), Enumerable.Concat(write_i64(phoff), Enumerable.Concat(write_i64(0), Enumerable.Concat(write_i32(0), Enumerable.Concat(write_i16(elf64_header_size()), Enumerable.Concat(write_i16(elf64_phdr_size()), Enumerable.Concat(write_i16(phnum), Enumerable.Concat(write_i16(0), Enumerable.Concat(write_i16(0), write_i16(0)).ToList()).ToList()).ToList()).ToList()).ToList()).ToList()).ToList()).ToList()).ToList()).ToList()).ToList()).ToList()).ToList();

    public static List<long> phdr_64(long ptype, long flags, long offset, long vaddr, long paddr, long filesz, long memsz, long palign) => Enumerable.Concat(write_i32(ptype), Enumerable.Concat(write_i32(flags), Enumerable.Concat(write_i64(offset), Enumerable.Concat(write_i64(vaddr), Enumerable.Concat(write_i64(paddr), Enumerable.Concat(write_i64(filesz), Enumerable.Concat(write_i64(memsz), write_i64(palign)).ToList()).ToList()).ToList()).ToList()).ToList()).ToList()).ToList();

    public static List<long> build_elf_64_linux(List<long> text, List<long> rodata, long entry_offset) => ((Func<long, List<long>>)((base_addr) => ((Func<long, List<long>>)((headers_size) => ((Func<long, List<long>>)((text_file_offset) => ((Func<long, List<long>>)((text_vaddr) => ((Func<long, List<long>>)((entry_point) => ((Func<long, List<long>>)((text_size) => ((Func<long, List<long>>)((text_region_end) => ((Func<long, List<long>>)((rodata_file_offset) => ((Func<long, List<long>>)((rodata_vaddr) => ((Func<long, List<long>>)((rodata_size) => Enumerable.Concat(elf64_header_bytes(entry_point, elf64_header_size(), 2), Enumerable.Concat(phdr_64(pt_load(), pf_rwx(), 0, base_addr, base_addr, text_region_end, text_region_end, elf_page_size()), Enumerable.Concat(phdr_64(pt_load(), pf_rw(), rodata_file_offset, rodata_vaddr, rodata_vaddr, rodata_size, rodata_size, elf_page_size()), Enumerable.Concat(pad_zeros((text_file_offset - headers_size)), Enumerable.Concat(text, Enumerable.Concat(pad_zeros((rodata_file_offset - text_region_end)), rodata).ToList()).ToList()).ToList()).ToList()).ToList()).ToList()))(((long)rodata.Count))))((base_addr + rodata_file_offset))))(elf_align(text_region_end, elf_page_size()))))((text_file_offset + text_size))))(((long)text.Count))))((text_vaddr + entry_offset))))((base_addr + text_file_offset))))(elf_align(headers_size, 16))))((elf64_header_size() + (elf64_phdr_size() * 2)))))(elf_linux_base_addr());

    public static long compute_text_file_offset_64() => elf_align((elf64_header_size() + (elf64_phdr_size() * 2)), 16);

    public static long compute_rodata_vaddr_64(long text_size) => ((Func<long, long>)((text_file_offset) => ((Func<long, long>)((rodata_file_offset) => (elf_linux_base_addr() + rodata_file_offset)))(elf_align((text_file_offset + text_size), elf_page_size()))))(compute_text_file_offset_64());

    public static long compute_text_vaddr_64() => (elf_linux_base_addr() + compute_text_file_offset_64());

    public static long compute_rodata_vaddr_bare(long text_size) => (elf_bare_metal_load_addr() + elf_align(text_size, 8));

    public static long compute_text_start_32() => ((Func<long, long>)((headers_end) => ((Func<long, long>)((note_offset) => elf_align((note_offset + 20), 16)))(elf_align(headers_end, 4))))((elf32_header_size() + (elf32_phdr_size() * 2)));

    public static TcoState default_tco_state() => new TcoState(active: false, in_tail_pos: false, loop_top: 0, param_locals: new List<long>(), temp_locals: new List<long>(), current_func: "", saved_next_local: 0, saved_next_temp: 0);

    public static List<long> temp_regs() => new List<long> { 0, 1, 2, 6, 7, 11 };

    public static List<long> local_regs() => new List<long> { 3, 12, 13, 14 };

    public static long spill_base() => 32;

    public static long bare_metal_load_addr() => 1048576;

    public static long bare_metal_stack_top() => 536870912;

    public static CodegenState empty_codegen_state() => new CodegenState(text: new List<long>(), rodata: new List<long>(), func_offsets: new List<FuncOffset>(), call_patches: new List<CallPatch>(), func_addr_fixups: new List<FuncAddrFixup>(), locals: new List<LocalBinding>(), next_temp: 0, next_local: 0, spill_count: 0, load_local_toggle: 0, tco: default_tco_state());

    public static CodegenState st_append_text(CodegenState st, List<long> bytes) => new CodegenState(text: Enumerable.Concat(st.text, bytes).ToList(), rodata: st.rodata, func_offsets: st.func_offsets, call_patches: st.call_patches, func_addr_fixups: st.func_addr_fixups, locals: st.locals, next_temp: st.next_temp, next_local: st.next_local, spill_count: st.spill_count, load_local_toggle: st.load_local_toggle, tco: st.tco);

    public static CodegenState st_with_text(CodegenState st, List<long> new_text) => new CodegenState(text: new_text, rodata: st.rodata, func_offsets: st.func_offsets, call_patches: st.call_patches, func_addr_fixups: st.func_addr_fixups, locals: st.locals, next_temp: st.next_temp, next_local: st.next_local, spill_count: st.spill_count, load_local_toggle: st.load_local_toggle, tco: st.tco);

    public static CodegenState record_func_offset(CodegenState st, string name) => new CodegenState(text: st.text, rodata: st.rodata, func_offsets: ((Func<List<FuncOffset>>)(() => { var _l = st.func_offsets; _l.Add(new FuncOffset(name: name, offset: ((long)st.text.Count))); return _l; }))(), call_patches: st.call_patches, func_addr_fixups: st.func_addr_fixups, locals: st.locals, next_temp: st.next_temp, next_local: st.next_local, spill_count: st.spill_count, load_local_toggle: st.load_local_toggle, tco: st.tco);

    public static long lookup_func_offset(List<FuncOffset> entries, string name) => lookup_func_offset_loop(entries, name, 0, ((long)entries.Count));

    public static long lookup_func_offset_loop(List<FuncOffset> entries, string name, long i, long len)
    {
        while (true)
        {
            if ((i == len))
            {
            return 0;
            }
            else
            {
            var e = entries[(int)i];
            if ((e.name == name))
            {
            return e.offset;
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

    public static CodegenState reset_func_state(CodegenState st, string name) => new CodegenState(text: st.text, rodata: st.rodata, func_offsets: ((Func<List<FuncOffset>>)(() => { var _l = st.func_offsets; _l.Add(new FuncOffset(name: name, offset: ((long)st.text.Count))); return _l; }))(), call_patches: st.call_patches, func_addr_fixups: st.func_addr_fixups, locals: new List<LocalBinding>(), next_temp: 0, next_local: 0, spill_count: 0, load_local_toggle: 0, tco: default_tco_state());

    public static EmitResult alloc_temp(CodegenState st) => ((Func<long, EmitResult>)((idx) => ((Func<long, EmitResult>)((reg) => new EmitResult(state: new CodegenState(text: st.text, rodata: st.rodata, func_offsets: st.func_offsets, call_patches: st.call_patches, func_addr_fixups: st.func_addr_fixups, locals: st.locals, next_temp: (st.next_temp + 1), next_local: st.next_local, spill_count: st.spill_count, load_local_toggle: st.load_local_toggle, tco: st.tco), reg: reg)))(temp_regs()[(int)idx])))(int_mod(st.next_temp, 6));

    public static EmitResult alloc_local(CodegenState st) => ((st.next_local < 4) ? ((Func<long, EmitResult>)((reg) => new EmitResult(state: new CodegenState(text: st.text, rodata: st.rodata, func_offsets: st.func_offsets, call_patches: st.call_patches, func_addr_fixups: st.func_addr_fixups, locals: st.locals, next_temp: st.next_temp, next_local: (st.next_local + 1), spill_count: st.spill_count, load_local_toggle: st.load_local_toggle, tco: st.tco), reg: reg)))(local_regs()[(int)st.next_local]) : ((Func<long, EmitResult>)((slot) => new EmitResult(state: new CodegenState(text: st.text, rodata: st.rodata, func_offsets: st.func_offsets, call_patches: st.call_patches, func_addr_fixups: st.func_addr_fixups, locals: st.locals, next_temp: st.next_temp, next_local: (st.next_local + 1), spill_count: (st.spill_count + 1), load_local_toggle: st.load_local_toggle, tco: st.tco), reg: slot)))((spill_base() + st.spill_count)));

    public static CodegenState store_local(CodegenState st, long local, long value_reg) => ((local < spill_base()) ? ((local == value_reg) ? st : st_append_text(st, mov_rr(local, value_reg))) : ((Func<long, CodegenState>)((offset) => st_append_text(st, mov_store(reg_rbp(), value_reg, offset))))((0 - ((((local - spill_base()) + 1) * 8) + 32))));

    public static EmitResult load_local(CodegenState st, long local) => ((local < spill_base()) ? new EmitResult(state: st, reg: local) : ((Func<long, EmitResult>)((scratch) => ((Func<long, EmitResult>)((offset) => new EmitResult(state: new CodegenState(text: Enumerable.Concat(st.text, mov_load(scratch, reg_rbp(), offset)).ToList(), rodata: st.rodata, func_offsets: st.func_offsets, call_patches: st.call_patches, func_addr_fixups: st.func_addr_fixups, locals: st.locals, next_temp: st.next_temp, next_local: st.next_local, spill_count: st.spill_count, load_local_toggle: (st.load_local_toggle + 1), tco: st.tco), reg: scratch)))((0 - ((((local - spill_base()) + 1) * 8) + 32)))))(((int_mod(st.load_local_toggle, 2) == 0) ? reg_r8() : reg_r9())));

    public static List<long> patch_i32_at(List<long> bytes, long pos, long value) => ((Func<List<long>, List<long>>)((new_bytes) => patch_4_loop(bytes, pos, new_bytes, 0, ((long)bytes.Count), new List<long>())))(write_i32(value));

    public static List<long> patch_4_loop(List<long> bytes, long pos, List<long> new_bytes, long i, long len, List<long> acc)
    {
        while (true)
        {
            if ((i == len))
            {
            return acc;
            }
            else
            {
            if (((i >= pos) && (i < (pos + 4))))
            {
            var _tco_0 = bytes;
            var _tco_1 = pos;
            var _tco_2 = new_bytes;
            var _tco_3 = (i + 1);
            var _tco_4 = len;
            var _tco_5 = ((Func<List<long>>)(() => { var _l = acc; _l.Add(new_bytes[(int)(i - pos)]); return _l; }))();
            bytes = _tco_0;
            pos = _tco_1;
            new_bytes = _tco_2;
            i = _tco_3;
            len = _tco_4;
            acc = _tco_5;
            continue;
            }
            else
            {
            var _tco_0 = bytes;
            var _tco_1 = pos;
            var _tco_2 = new_bytes;
            var _tco_3 = (i + 1);
            var _tco_4 = len;
            var _tco_5 = ((Func<List<long>>)(() => { var _l = acc; _l.Add(bytes[(int)i]); return _l; }))();
            bytes = _tco_0;
            pos = _tco_1;
            new_bytes = _tco_2;
            i = _tco_3;
            len = _tco_4;
            acc = _tco_5;
            continue;
            }
            }
        }
    }

    public static CodegenState patch_jcc_at(CodegenState st, long jcc_pos, long target_pos) => ((Func<long, CodegenState>)((rel32) => st_with_text(st, patch_i32_at(st.text, (jcc_pos + 2), rel32))))((target_pos - (jcc_pos + 6)));

    public static CodegenState patch_jmp_at(CodegenState st, long jmp_pos, long target_pos) => ((Func<long, CodegenState>)((rel32) => st_with_text(st, patch_i32_at(st.text, (jmp_pos + 1), rel32))))((target_pos - (jmp_pos + 5)));

    public static CodegenState patch_call_at(CodegenState st, long call_pos, long target_pos) => ((Func<long, CodegenState>)((rel32) => st_with_text(st, patch_i32_at(st.text, (call_pos + 1), rel32))))((target_pos - (call_pos + 5)));

    public static PatchEntry make_i32_patch(long pos, long value) => ((Func<List<long>, PatchEntry>)((bs) => new PatchEntry(pos: pos, b0: bs[(int)0], b1: bs[(int)1], b2: bs[(int)2], b3: bs[(int)3])))(write_i32(value));

    public static List<long> apply_all_patches(List<long> bytes, List<PatchEntry> patches) => apply_patch_walk(bytes, patches, 0, ((long)bytes.Count), new List<long>());

    public static List<long> apply_patch_walk(List<long> bytes, List<PatchEntry> patches, long i, long len, List<long> acc)
    {
        while (true)
        {
            if ((i == len))
            {
            return acc;
            }
            else
            {
            var b = find_patch_byte(patches, i, 0, ((long)patches.Count));
            if ((b >= 0))
            {
            var _tco_0 = bytes;
            var _tco_1 = patches;
            var _tco_2 = (i + 1);
            var _tco_3 = len;
            var _tco_4 = ((Func<List<long>>)(() => { var _l = acc; _l.Add(b); return _l; }))();
            bytes = _tco_0;
            patches = _tco_1;
            i = _tco_2;
            len = _tco_3;
            acc = _tco_4;
            continue;
            }
            else
            {
            var _tco_0 = bytes;
            var _tco_1 = patches;
            var _tco_2 = (i + 1);
            var _tco_3 = len;
            var _tco_4 = ((Func<List<long>>)(() => { var _l = acc; _l.Add(bytes[(int)i]); return _l; }))();
            bytes = _tco_0;
            patches = _tco_1;
            i = _tco_2;
            len = _tco_3;
            acc = _tco_4;
            continue;
            }
            }
        }
    }

    public static long find_patch_byte(List<PatchEntry> patches, long i, long j, long len)
    {
        while (true)
        {
            if ((j == len))
            {
            return (0 - 1);
            }
            else
            {
            var p = patches[(int)j];
            if ((i == p.pos))
            {
            return p.b0;
            }
            else
            {
            if ((i == (p.pos + 1)))
            {
            return p.b1;
            }
            else
            {
            if ((i == (p.pos + 2)))
            {
            return p.b2;
            }
            else
            {
            if ((i == (p.pos + 3)))
            {
            return p.b3;
            }
            else
            {
            var _tco_0 = patches;
            var _tco_1 = i;
            var _tco_2 = (j + 1);
            var _tco_3 = len;
            patches = _tco_0;
            i = _tco_1;
            j = _tco_2;
            len = _tco_3;
            continue;
            }
            }
            }
            }
            }
        }
    }

    public static List<PatchEntry> collect_call_patches(List<CallPatch> patches, List<FuncOffset> offsets, long i, List<PatchEntry> acc)
    {
        while (true)
        {
            if ((i == ((long)patches.Count)))
            {
            return acc;
            }
            else
            {
            var p = patches[(int)i];
            var target_offset = lookup_func_offset(offsets, p.target);
            var rel32 = (target_offset - (p.patch_offset + 5));
            var _tco_0 = patches;
            var _tco_1 = offsets;
            var _tco_2 = (i + 1);
            var _tco_3 = ((Func<List<PatchEntry>>)(() => { var _l = acc; _l.Add(make_i32_patch((p.patch_offset + 1), rel32)); return _l; }))();
            patches = _tco_0;
            offsets = _tco_1;
            i = _tco_2;
            acc = _tco_3;
            continue;
            }
        }
    }

    public static CodegenState patch_calls(CodegenState st) => ((Func<List<PatchEntry>, CodegenState>)((entries) => st_with_text(st, apply_all_patches(st.text, entries))))(collect_call_patches(st.call_patches, st.func_offsets, 0, new List<PatchEntry>()));

    public static long align_16(long n) => ((Func<long, long>)((r) => ((r == 0) ? n : ((n + 16) - r))))(int_mod(n, 16));

    public static List<long> emit_sub_rsp_imm32(long imm) => Enumerable.Concat(new List<long> { 72, 129, 236 }, write_i32(imm)).ToList();

    public static CodegenState emit_prologue(CodegenState st) => st_append_text(st, Enumerable.Concat(push_r(reg_rbp()), Enumerable.Concat(mov_rr(reg_rbp(), reg_rsp()), Enumerable.Concat(push_r(reg_rbx()), Enumerable.Concat(push_r(reg_r12()), Enumerable.Concat(push_r(reg_r13()), push_r(reg_r14())).ToList()).ToList()).ToList()).ToList()).ToList());

    public static CodegenState emit_epilogue(CodegenState st) => st_append_text(st, Enumerable.Concat(lea(reg_rsp(), reg_rbp(), (0 - 32)), Enumerable.Concat(pop_r(reg_r14()), Enumerable.Concat(pop_r(reg_r13()), Enumerable.Concat(pop_r(reg_r12()), Enumerable.Concat(pop_r(reg_rbx()), Enumerable.Concat(pop_r(reg_rbp()), x86_ret()).ToList()).ToList()).ToList()).ToList()).ToList()).ToList());

    public static CodegenState bind_params(CodegenState st, List<IRParam> @params, long i)
    {
        while (true)
        {
            if ((i == ((long)@params.Count)))
            {
            return st;
            }
            else
            {
            var p = @params[(int)i];
            var loc = alloc_local(st);
            var st1 = ((i < 6) ? store_local(loc.state, loc.reg, arg_regs()[(int)i]) : ((Func<long, CodegenState>)((stack_offset) => ((Func<EmitResult, CodegenState>)((tmp) => store_local(st_append_text(tmp.state, mov_load(tmp.reg, reg_rbp(), stack_offset)), loc.reg, tmp.reg)))(alloc_temp(loc.state))))((16 + ((i - 6) * 8))));
            var _tco_0 = add_local(st1, p.name, loc.reg);
            var _tco_1 = @params;
            var _tco_2 = (i + 1);
            st = _tco_0;
            @params = _tco_1;
            i = _tco_2;
            continue;
            }
        }
    }

    public static bool is_self_call(IRExpr expr, string func_name)
    {
        while (true)
        {
            var _tco_s = expr;
            if (_tco_s is IrApply _tco_m0)
            {
                var f = _tco_m0.Field0;
                var a = _tco_m0.Field1;
                var t = _tco_m0.Field2;
            var _tco_0 = f;
            var _tco_1 = func_name;
            expr = _tco_0;
            func_name = _tco_1;
            continue;
            }
            else if (_tco_s is IrName _tco_m1)
            {
                var n = _tco_m1.Field0;
                var t = _tco_m1.Field1;
            return (n == func_name);
            }
            {
            return false;
            }
        }
    }

    public static bool has_tail_call(IRExpr expr, string func_name)
    {
        while (true)
        {
            var _tco_s = expr;
            if (_tco_s is IrIf _tco_m0)
            {
                var c = _tco_m0.Field0;
                var th = _tco_m0.Field1;
                var el = _tco_m0.Field2;
                var t = _tco_m0.Field3;
            return (has_tail_call(th, func_name) || has_tail_call(el, func_name));
            }
            else if (_tco_s is IrLet _tco_m1)
            {
                var n = _tco_m1.Field0;
                var t = _tco_m1.Field1;
                var v = _tco_m1.Field2;
                var b = _tco_m1.Field3;
            var _tco_0 = b;
            var _tco_1 = func_name;
            expr = _tco_0;
            func_name = _tco_1;
            continue;
            }
            else if (_tco_s is IrMatch _tco_m2)
            {
                var s = _tco_m2.Field0;
                var bs = _tco_m2.Field1;
                var t = _tco_m2.Field2;
            return has_tail_call_branches(bs, func_name, 0);
            }
            else if (_tco_s is IrApply _tco_m3)
            {
                var f = _tco_m3.Field0;
                var a = _tco_m3.Field1;
                var t = _tco_m3.Field2;
            return is_self_call(expr, func_name);
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

    public static bool should_tco(IRDef def) => ((((long)def.@params.Count) > 0) ? has_tail_call(def.body, def.name) : false);

    public static CodegenState st_set_tail_pos(CodegenState st, bool v) => new CodegenState(text: st.text, rodata: st.rodata, func_offsets: st.func_offsets, call_patches: st.call_patches, func_addr_fixups: st.func_addr_fixups, locals: st.locals, next_temp: st.next_temp, next_local: st.next_local, spill_count: st.spill_count, load_local_toggle: st.load_local_toggle, tco: new TcoState(active: st.tco.active, in_tail_pos: v, loop_top: st.tco.loop_top, param_locals: st.tco.param_locals, temp_locals: st.tco.temp_locals, current_func: st.tco.current_func, saved_next_local: st.tco.saved_next_local, saved_next_temp: st.tco.saved_next_temp));

    public static TcoAllocResult pre_alloc_tco_temps(CodegenState st, long i, long count, List<long> acc)
    {
        while (true)
        {
            if ((i == count))
            {
            return new TcoAllocResult(alloc_state: st, alloc_locals: acc);
            }
            else
            {
            var loc = alloc_local(st);
            var _tco_0 = loc.state;
            var _tco_1 = (i + 1);
            var _tco_2 = count;
            var _tco_3 = ((Func<List<long>>)(() => { var _l = acc; _l.Add(loc.reg); return _l; }))();
            st = _tco_0;
            i = _tco_1;
            count = _tco_2;
            acc = _tco_3;
            continue;
            }
        }
    }

    public static List<long> collect_param_locals(List<LocalBinding> bindings, List<IRParam> @params, long i, List<long> acc)
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
            var slot = lookup_local(bindings, p.name);
            var _tco_0 = bindings;
            var _tco_1 = @params;
            var _tco_2 = (i + 1);
            var _tco_3 = ((Func<List<long>>)(() => { var _l = acc; _l.Add(slot); return _l; }))();
            bindings = _tco_0;
            @params = _tco_1;
            i = _tco_2;
            acc = _tco_3;
            continue;
            }
        }
    }

    public static CodegenState emit_function(CodegenState st, IRDef def) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<long, CodegenState>)((frame_patch_pos) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<bool, CodegenState>)((is_tco) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<EmitResult, CodegenState>)((result) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<long, CodegenState>)((frame_size) => st_with_text(st7, patch_i32_at(st7.text, (frame_patch_pos + 3), frame_size))))(align_16((st7.spill_count * 8)))))(emit_epilogue(st6))))(((result.reg == reg_rax()) ? result.state : st_append_text(result.state, mov_rr(reg_rax(), result.reg))))))(emit_expr(st5, def.body))))((is_tco ? ((Func<TcoAllocResult, CodegenState>)((tco_alloc) => ((Func<CodegenState, CodegenState>)((st_a) => ((Func<List<long>, CodegenState>)((p_locals) => new CodegenState(text: st_a.text, rodata: st_a.rodata, func_offsets: st_a.func_offsets, call_patches: st_a.call_patches, func_addr_fixups: st_a.func_addr_fixups, locals: st_a.locals, next_temp: st_a.next_temp, next_local: st_a.next_local, spill_count: st_a.spill_count, load_local_toggle: st_a.load_local_toggle, tco: new TcoState(active: true, in_tail_pos: true, loop_top: ((long)st_a.text.Count), param_locals: p_locals, temp_locals: tco_alloc.alloc_locals, current_func: def.name, saved_next_local: st_a.next_local, saved_next_temp: st_a.next_temp))))(collect_param_locals(st_a.locals, def.@params, 0, new List<long>()))))(tco_alloc.alloc_state)))(pre_alloc_tco_temps(st4, 0, ((long)def.@params.Count), new List<long>())) : st4))))(should_tco(def))))(bind_params(st3, def.@params, 0))))(st_append_text(st2, emit_sub_rsp_imm32(0)))))(((long)st2.text.Count))))(emit_prologue(st1))))(reset_func_state(st, def.name));

    public static CodegenState emit_all_defs(CodegenState st, List<IRDef> defs, long i)
    {
        while (true)
        {
            if ((i == ((long)defs.Count)))
            {
            return st;
            }
            else
            {
            var _tco_0 = emit_function(st, defs[(int)i]);
            var _tco_1 = defs;
            var _tco_2 = (i + 1);
            st = _tco_0;
            defs = _tco_1;
            i = _tco_2;
            continue;
            }
        }
    }

    public static long lookup_local(List<LocalBinding> bindings, string name) => lookup_local_loop(bindings, name, 0, ((long)bindings.Count));

    public static long lookup_local_loop(List<LocalBinding> bindings, string name, long i, long len)
    {
        while (true)
        {
            if ((i == len))
            {
            return (0 - 1);
            }
            else
            {
            var b = bindings[(int)i];
            if ((b.name == name))
            {
            return b.slot;
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

    public static CodegenState add_local(CodegenState st, string name, long slot) => new CodegenState(text: st.text, rodata: st.rodata, func_offsets: st.func_offsets, call_patches: st.call_patches, func_addr_fixups: st.func_addr_fixups, locals: ((Func<List<LocalBinding>>)(() => { var _l = st.locals; _l.Add(new LocalBinding(name: name, slot: slot)); return _l; }))(), next_temp: st.next_temp, next_local: st.next_local, spill_count: st.spill_count, load_local_toggle: st.load_local_toggle, tco: st.tco);

    public static EmitResult emit_expr(CodegenState st, IRExpr expr) => expr switch { IrIntLit(var value) => emit_int_lit(st, value), IrBoolLit(var value) => emit_int_lit(st, (value ? 1 : 0)), IrName(var name, var ty) => emit_name(st, name, ty), IrLet(var name, var ty, var value, var body) => (_p0_) => emit_let(st, name, value, body, _p0_), IrBinary(var op, var left, var right, var ty) => (_p0_) => emit_binary(st, op, left, right, _p0_), IrIf(var cond, var then_e, var else_e, var ty) => (_p0_) => (_p1_) => emit_if(st, cond, then_e, else_e, _p0_, _p1_), IrApply(var func, var arg, var ty) => emit_apply(st, func)(arg)(ty), IrRecord(var rname, var fields, var ty) => emit_record(st, fields, ty), IrFieldAccess(var rec, var field, var ty) => (_p0_) => emit_field_access(st, rec, field, _p0_), IrMatch(var scrut, var branches, var ty) => (_p0_) => emit_match(st, scrut, branches, _p0_), IrList(var elems, var ty) => (_p0_) => emit_list(st, elems, _p0_), _ => new EmitResult(state: st, reg: reg_rax()), };

    public static EmitResult emit_int_lit(CodegenState st, long value) => ((Func<EmitResult, EmitResult>)((tmp) => new EmitResult(state: st_append_text(tmp.state, li(tmp.reg, value)), reg: tmp.reg)))(alloc_temp(st));

    public static long find_ctor_tag(List<SumCtor> ctors, string name, long i)
    {
        while (true)
        {
            if ((i == ((long)ctors.Count)))
            {
            return (0 - 1);
            }
            else
            {
            var c = ctors[(int)i];
            if ((c.name.value == name))
            {
            return i;
            }
            else
            {
            var _tco_0 = ctors;
            var _tco_1 = name;
            var _tco_2 = (i + 1);
            ctors = _tco_0;
            name = _tco_1;
            i = _tco_2;
            continue;
            }
            }
        }
    }

    public static EmitResult emit_name(CodegenState st, string name, CodexType ty) => ((Func<long, EmitResult>)((slot) => ((slot >= 0) ? load_local(st, slot) : emit_name_nonlocal(st, name, ty))))(lookup_local(st.locals, name));

    public static EmitResult emit_name_nonlocal(CodegenState st, string name, CodexType ty) => ty switch { SumTy(var sname, var ctors) => ((Func<long, EmitResult>)((tag) => ((tag >= 0) ? emit_nullary_ctor(st, tag) : emit_name_as_call(st, name))))(find_ctor_tag(ctors, name, 0)), FunTy(var pt, var rt) => emit_partial_application(st, name, new List<IRExpr>()), _ => emit_name_as_call(st, name), };

    public static EmitResult emit_nullary_ctor(CodegenState st, long tag) => ((Func<EmitResult, EmitResult>)((ptr_loc) => ((Func<EmitResult, EmitResult>)((ptr_tmp) => ((Func<CodegenState, EmitResult>)((st1) => ((Func<CodegenState, EmitResult>)((st2) => ((Func<CodegenState, EmitResult>)((st3) => ((Func<EmitResult, EmitResult>)((tag_tmp) => ((Func<CodegenState, EmitResult>)((st4) => ((Func<EmitResult, EmitResult>)((ptr_load) => ((Func<CodegenState, EmitResult>)((st5) => load_local(st5, ptr_loc.reg)))(st_append_text(ptr_load.state, mov_store(ptr_load.reg, tag_tmp.reg, 0)))))(load_local(st4, ptr_loc.reg))))(st_append_text(tag_tmp.state, li(tag_tmp.reg, tag)))))(alloc_temp(st3))))(st_append_text(st2, add_ri(reg_r10(), 8)))))(store_local(st1, ptr_loc.reg, ptr_tmp.reg))))(st_append_text(ptr_tmp.state, mov_rr(ptr_tmp.reg, reg_r10())))))(alloc_temp(ptr_loc.state))))(alloc_local(st));

    public static EmitResult emit_name_as_call(CodegenState st, string name) => ((Func<CodegenState, EmitResult>)((st1) => ((Func<EmitResult, EmitResult>)((tmp) => new EmitResult(state: st_append_text(tmp.state, mov_rr(tmp.reg, reg_rax())), reg: tmp.reg)))(alloc_temp(st1))))(emit_call_to(st, name));

    public static EmitResult emit_let(CodegenState st, string name, IRExpr value, IRExpr body) => ((Func<bool, EmitResult>)((saved_tail) => ((Func<EmitResult, EmitResult>)((val_result) => ((Func<EmitResult, EmitResult>)((loc) => ((Func<CodegenState, EmitResult>)((st1) => ((Func<CodegenState, EmitResult>)((st2) => emit_expr(st_set_tail_pos(st2, saved_tail), body)))(add_local(st1, name, loc.reg))))(store_local(loc.state, loc.reg, val_result.reg))))(alloc_local(val_result.state))))(emit_expr(st_set_tail_pos(st, false), value))))(st.tco.in_tail_pos);

    public static EmitResult emit_binary(CodegenState st, IRBinaryOp op, IRExpr left, IRExpr right) => ((Func<EmitResult, EmitResult>)((l) => ((Func<EmitResult, EmitResult>)((l_loc) => ((Func<CodegenState, EmitResult>)((st1) => ((Func<EmitResult, EmitResult>)((r) => ((Func<EmitResult, EmitResult>)((r_loc) => ((Func<CodegenState, EmitResult>)((st2) => ((Func<EmitResult, EmitResult>)((l_load) => ((Func<EmitResult, EmitResult>)((r_load) => emit_binary_op(r_load.state, op, l_load.reg, r_load.reg)))(load_local(l_load.state, r_loc.reg))))(load_local(st2, l_loc.reg))))(store_local(r_loc.state, r_loc.reg, r.reg))))(alloc_local(r.state))))(emit_expr(st1, right))))(store_local(l_loc.state, l_loc.reg, l.reg))))(alloc_local(l.state))))(emit_expr(st, left));

    public static EmitResult emit_binary_op(CodegenState st, IRBinaryOp op, long l_reg, long r_reg) => ((Func<EmitResult, EmitResult>)((rd) => op switch { IrAddInt { } => new EmitResult(state: st_append_text(rd.state, Enumerable.Concat(mov_rr(rd.reg, l_reg), add_rr(rd.reg, r_reg)).ToList()), reg: rd.reg), IrSubInt { } => new EmitResult(state: st_append_text(rd.state, Enumerable.Concat(mov_rr(rd.reg, l_reg), sub_rr(rd.reg, r_reg)).ToList()), reg: rd.reg), IrMulInt { } => new EmitResult(state: st_append_text(rd.state, Enumerable.Concat(mov_rr(rd.reg, l_reg), imul_rr(rd.reg, r_reg)).ToList()), reg: rd.reg), IrDivInt { } => new EmitResult(state: st_append_text(rd.state, Enumerable.Concat(mov_rr(reg_rax(), l_reg), Enumerable.Concat(cqo(), Enumerable.Concat(idiv_r(r_reg), mov_rr(rd.reg, reg_rax())).ToList()).ToList()).ToList()), reg: rd.reg), IrEq { } => emit_comparison(st, cc_e(), l_reg, r_reg), IrNotEq { } => emit_comparison(st, cc_ne(), l_reg, r_reg), IrLt { } => emit_comparison(st, cc_l(), l_reg, r_reg), IrGt { } => emit_comparison(st, cc_g(), l_reg, r_reg), IrLtEq { } => emit_comparison(st, cc_le(), l_reg, r_reg), IrGtEq { } => emit_comparison(st, cc_ge(), l_reg, r_reg), IrAnd { } => new EmitResult(state: st_append_text(rd.state, Enumerable.Concat(mov_rr(rd.reg, l_reg), and_rr(rd.reg, r_reg)).ToList()), reg: rd.reg), IrOr { } => new EmitResult(state: st_append_text(rd.state, Enumerable.Concat(mov_rr(rd.reg, l_reg), add_rr(rd.reg, r_reg)).ToList()), reg: rd.reg), _ => new EmitResult(state: st, reg: reg_rax()), }))(alloc_temp(st));

    public static EmitResult emit_comparison(CodegenState st, long cc, long l_reg, long r_reg) => ((Func<EmitResult, EmitResult>)((rd) => new EmitResult(state: st_append_text(rd.state, Enumerable.Concat(cmp_rr(l_reg, r_reg), Enumerable.Concat(setcc(cc, rd.reg), movzx_byte_self(rd.reg)).ToList()).ToList()), reg: rd.reg)))(alloc_temp(st));

    public static EmitResult emit_if(CodegenState st, IRExpr cond, IRExpr then_e, IRExpr else_e) => ((Func<bool, EmitResult>)((saved_tail) => ((Func<EmitResult, EmitResult>)((cond_result) => ((Func<CodegenState, EmitResult>)((st1) => ((Func<long, EmitResult>)((je_false_pos) => ((Func<CodegenState, EmitResult>)((st2) => ((Func<EmitResult, EmitResult>)((then_result) => ((Func<EmitResult, EmitResult>)((result_loc) => ((Func<CodegenState, EmitResult>)((st3) => ((Func<long, EmitResult>)((jmp_end_pos) => ((Func<CodegenState, EmitResult>)((st4) => ((Func<CodegenState, EmitResult>)((st5) => ((Func<EmitResult, EmitResult>)((else_result) => ((Func<CodegenState, EmitResult>)((st6) => ((Func<CodegenState, EmitResult>)((st7) => load_local(st7, result_loc.reg)))(patch_jmp_at(st6, jmp_end_pos, ((long)st6.text.Count)))))(store_local(else_result.state, result_loc.reg, else_result.reg))))(emit_expr(st_set_tail_pos(st5, saved_tail), else_e))))(patch_jcc_at(st4, je_false_pos, ((long)st4.text.Count)))))(st_append_text(st3, jmp(0)))))(((long)st3.text.Count))))(store_local(result_loc.state, result_loc.reg, then_result.reg))))(alloc_local(then_result.state))))(emit_expr(st_set_tail_pos(st2, saved_tail), then_e))))(st_append_text(st1, jcc(cc_e(), 0)))))(((long)st1.text.Count))))(st_append_text(cond_result.state, test_rr(cond_result.reg, cond_result.reg)))))(emit_expr(st_set_tail_pos(st, false), cond))))(st.tco.in_tail_pos);

    public static bool is_string_builtin(string name) => ((name == "text-length") ? true : ((name == "integer-to-text") ? true : ((name == "show") ? true : ((name == "text-to-integer") ? true : ((name == "text-replace") ? true : ((name == "text-contains") ? true : ((name == "text-starts-with") ? true : ((name == "text-compare") ? true : ((name == "text-concat-list") ? true : ((name == "text-split") ? true : ((name == "substring") ? true : false)))))))))));

    public static bool is_list_builtin(string name) => ((name == "list-length") ? true : ((name == "list-at") ? true : ((name == "list-cons") ? true : ((name == "list-append") ? true : ((name == "list-snoc") ? true : ((name == "list-insert-at") ? true : ((name == "list-contains") ? true : false)))))));

    public static bool is_char_builtin(string name) => ((name == "char-at") ? true : ((name == "char-code-at") ? true : ((name == "char-code") ? true : ((name == "code-to-char") ? true : ((name == "char-to-text") ? true : ((name == "is-letter") ? true : ((name == "is-digit") ? true : ((name == "is-whitespace") ? true : false))))))));

    public static bool is_misc_builtin(string name) => ((name == "negate") ? true : ((name == "get-args") ? true : ((name == "current-dir") ? true : ((name == "file-exists") ? true : false))));

    public static bool is_builtin(string name) => (is_string_builtin(name) ? true : (is_list_builtin(name) ? true : (is_char_builtin(name) ? true : is_misc_builtin(name))));

    public static EmitResult emit_helper_call_1(CodegenState st, List<IRExpr> args, string helper) => ((Func<EmitResult, EmitResult>)((r) => ((Func<CodegenState, EmitResult>)((st1) => ((Func<CodegenState, EmitResult>)((st2) => ((Func<EmitResult, EmitResult>)((tmp) => new EmitResult(state: st_append_text(tmp.state, mov_rr(tmp.reg, reg_rax())), reg: tmp.reg)))(alloc_temp(st2))))(emit_call_to(st1, helper))))(st_append_text(r.state, mov_rr(reg_rdi(), r.reg)))))(emit_expr(st_set_tail_pos(st, false), args[(int)0]));

    public static EmitResult emit_helper_call_2(CodegenState st, List<IRExpr> args, string helper) => ((Func<EmitResult, EmitResult>)((r0) => ((Func<EmitResult, EmitResult>)((loc) => ((Func<CodegenState, EmitResult>)((st1) => ((Func<EmitResult, EmitResult>)((r1) => ((Func<EmitResult, EmitResult>)((loaded) => ((Func<CodegenState, EmitResult>)((st2) => ((Func<CodegenState, EmitResult>)((st3) => ((Func<CodegenState, EmitResult>)((st4) => ((Func<EmitResult, EmitResult>)((tmp) => new EmitResult(state: st_append_text(tmp.state, mov_rr(tmp.reg, reg_rax())), reg: tmp.reg)))(alloc_temp(st4))))(emit_call_to(st3, helper))))(st_append_text(st2, mov_rr(reg_rdi(), loaded.reg)))))(st_append_text(loaded.state, mov_rr(reg_rsi(), r1.reg)))))(load_local(r1.state, loc.reg))))(emit_expr(st1, args[(int)1]))))(store_local(loc.state, loc.reg, r0.reg))))(alloc_local(r0.state))))(emit_expr(st_set_tail_pos(st, false), args[(int)0]));

    public static EmitResult emit_helper_call_3(CodegenState st, List<IRExpr> args, string helper) => ((Func<EmitResult, EmitResult>)((r0) => ((Func<EmitResult, EmitResult>)((loc0) => ((Func<CodegenState, EmitResult>)((st1) => ((Func<EmitResult, EmitResult>)((r1) => ((Func<EmitResult, EmitResult>)((loc1) => ((Func<CodegenState, EmitResult>)((st2) => ((Func<EmitResult, EmitResult>)((r2) => ((Func<CodegenState, EmitResult>)((st3) => ((Func<EmitResult, EmitResult>)((ld1) => ((Func<CodegenState, EmitResult>)((st4) => ((Func<EmitResult, EmitResult>)((ld0) => ((Func<CodegenState, EmitResult>)((st5) => ((Func<CodegenState, EmitResult>)((st6) => ((Func<EmitResult, EmitResult>)((tmp) => new EmitResult(state: st_append_text(tmp.state, mov_rr(tmp.reg, reg_rax())), reg: tmp.reg)))(alloc_temp(st6))))(emit_call_to(st5, helper))))(st_append_text(ld0.state, mov_rr(reg_rdi(), ld0.reg)))))(load_local(st4, loc0.reg))))(st_append_text(ld1.state, mov_rr(reg_rsi(), ld1.reg)))))(load_local(st3, loc1.reg))))(st_append_text(r2.state, mov_rr(reg_rdx(), r2.reg)))))(emit_expr(st2, args[(int)2]))))(store_local(loc1.state, loc1.reg, r1.reg))))(alloc_local(r1.state))))(emit_expr(st1, args[(int)1]))))(store_local(loc0.state, loc0.reg, r0.reg))))(alloc_local(r0.state))))(emit_expr(st_set_tail_pos(st, false), args[(int)0]));

    public static EmitResult emit_text_length_builtin(CodegenState st, List<IRExpr> args) => ((Func<EmitResult, EmitResult>)((r) => ((Func<EmitResult, EmitResult>)((tmp) => new EmitResult(state: st_append_text(tmp.state, mov_load(tmp.reg, r.reg, 0)), reg: tmp.reg)))(alloc_temp(r.state))))(emit_expr(st_set_tail_pos(st, false), args[(int)0]));

    public static EmitResult emit_list_length_builtin(CodegenState st, List<IRExpr> args) => ((Func<EmitResult, EmitResult>)((r) => ((Func<EmitResult, EmitResult>)((tmp) => new EmitResult(state: st_append_text(tmp.state, mov_load(tmp.reg, r.reg, 0)), reg: tmp.reg)))(alloc_temp(r.state))))(emit_expr(st_set_tail_pos(st, false), args[(int)0]));

    public static EmitResult emit_list_at_builtin(CodegenState st, List<IRExpr> args) => ((Func<EmitResult, EmitResult>)((r0) => ((Func<EmitResult, EmitResult>)((loc) => ((Func<CodegenState, EmitResult>)((st1) => ((Func<EmitResult, EmitResult>)((r1) => ((Func<EmitResult, EmitResult>)((list_loaded) => ((Func<EmitResult, EmitResult>)((addr) => ((Func<CodegenState, EmitResult>)((st2) => ((Func<CodegenState, EmitResult>)((st3) => ((Func<CodegenState, EmitResult>)((st4) => ((Func<EmitResult, EmitResult>)((rd) => new EmitResult(state: st_append_text(rd.state, mov_load(rd.reg, addr.reg, 8)), reg: rd.reg)))(alloc_temp(st4))))(st_append_text(st3, add_rr(addr.reg, list_loaded.reg)))))(st_append_text(st2, shl_ri(addr.reg, 3)))))(st_append_text(addr.state, mov_rr(addr.reg, r1.reg)))))(alloc_temp(list_loaded.state))))(load_local(r1.state, loc.reg))))(emit_expr(st1, args[(int)1]))))(store_local(loc.state, loc.reg, r0.reg))))(alloc_local(r0.state))))(emit_expr(st_set_tail_pos(st, false), args[(int)0]));

    public static EmitResult emit_char_at_builtin(CodegenState st, List<IRExpr> args) => ((Func<EmitResult, EmitResult>)((r0) => ((Func<EmitResult, EmitResult>)((loc) => ((Func<CodegenState, EmitResult>)((st1) => ((Func<EmitResult, EmitResult>)((r1) => ((Func<EmitResult, EmitResult>)((str_loaded) => ((Func<CodegenState, EmitResult>)((st2) => ((Func<CodegenState, EmitResult>)((st3) => new EmitResult(state: st3, reg: r1.reg)))(st_append_text(st2, movzx_byte(r1.reg, r1.reg, 8)))))(st_append_text(str_loaded.state, add_rr(r1.reg, str_loaded.reg)))))(load_local(r1.state, loc.reg))))(emit_expr(st1, args[(int)1]))))(store_local(loc.state, loc.reg, r0.reg))))(alloc_local(r0.state))))(emit_expr(st_set_tail_pos(st, false), args[(int)0]));

    public static EmitResult emit_char_code_at_builtin(CodegenState st, List<IRExpr> args) => ((Func<EmitResult, EmitResult>)((r0) => ((Func<EmitResult, EmitResult>)((loc) => ((Func<CodegenState, EmitResult>)((st1) => ((Func<EmitResult, EmitResult>)((r1) => ((Func<EmitResult, EmitResult>)((text_loaded) => ((Func<EmitResult, EmitResult>)((rd) => ((Func<CodegenState, EmitResult>)((st2) => ((Func<CodegenState, EmitResult>)((st3) => ((Func<CodegenState, EmitResult>)((st4) => new EmitResult(state: st4, reg: rd.reg)))(st_append_text(st3, movzx_byte(rd.reg, rd.reg, 8)))))(st_append_text(st2, add_rr(rd.reg, r1.reg)))))(st_append_text(rd.state, mov_rr(rd.reg, text_loaded.reg)))))(alloc_temp(text_loaded.state))))(load_local(r1.state, loc.reg))))(emit_expr(st1, args[(int)1]))))(store_local(loc.state, loc.reg, r0.reg))))(alloc_local(r0.state))))(emit_expr(st_set_tail_pos(st, false), args[(int)0]));

    public static EmitResult emit_char_to_text_builtin(CodegenState st, List<IRExpr> args) => ((Func<EmitResult, EmitResult>)((r) => ((Func<EmitResult, EmitResult>)((loc) => ((Func<CodegenState, EmitResult>)((st1) => ((Func<EmitResult, EmitResult>)((ptr_loc) => ((Func<EmitResult, EmitResult>)((ptr_tmp) => ((Func<CodegenState, EmitResult>)((st2) => ((Func<CodegenState, EmitResult>)((st3) => ((Func<CodegenState, EmitResult>)((st4) => ((Func<CodegenState, EmitResult>)((st5) => ((Func<EmitResult, EmitResult>)((ptr_loaded) => ((Func<CodegenState, EmitResult>)((st6) => ((Func<EmitResult, EmitResult>)((code_loaded) => ((Func<EmitResult, EmitResult>)((ptr_loaded2) => ((Func<CodegenState, EmitResult>)((st7) => ((Func<EmitResult, EmitResult>)((result) => new EmitResult(state: result.state, reg: result.reg)))(load_local(st7, ptr_loc.reg))))(st_append_text(ptr_loaded2.state, mov_store_byte(ptr_loaded2.reg, code_loaded.reg, 8)))))(load_local(code_loaded.state, ptr_loc.reg))))(load_local(st6, loc.reg))))(st_append_text(ptr_loaded.state, mov_store(ptr_loaded.reg, reg_r11(), 0)))))(load_local(st5, ptr_loc.reg))))(st_append_text(st4, li(reg_r11(), 1)))))(st_append_text(st3, add_ri(reg_r10(), 16)))))(store_local(st2, ptr_loc.reg, ptr_tmp.reg))))(st_append_text(ptr_tmp.state, mov_rr(ptr_tmp.reg, reg_r10())))))(alloc_temp(ptr_loc.state))))(alloc_local(st1))))(store_local(loc.state, loc.reg, r.reg))))(alloc_local(r.state))))(emit_expr(st_set_tail_pos(st, false), args[(int)0]));

    public static EmitResult emit_is_letter_builtin(CodegenState st, List<IRExpr> args) => ((Func<EmitResult, EmitResult>)((r) => ((Func<CodegenState, EmitResult>)((st1) => ((Func<CodegenState, EmitResult>)((st2) => ((Func<CodegenState, EmitResult>)((st3) => ((Func<CodegenState, EmitResult>)((st4) => new EmitResult(state: st4, reg: r.reg)))(st_append_text(st3, movzx_byte_self(r.reg)))))(st_append_text(st2, setcc(cc_be(), r.reg)))))(st_append_text(st1, cmp_ri(r.reg, 51)))))(st_append_text(r.state, sub_ri(r.reg, 13)))))(emit_expr(st_set_tail_pos(st, false), args[(int)0]));

    public static EmitResult emit_is_digit_builtin(CodegenState st, List<IRExpr> args) => ((Func<EmitResult, EmitResult>)((r) => ((Func<CodegenState, EmitResult>)((st1) => ((Func<CodegenState, EmitResult>)((st2) => ((Func<CodegenState, EmitResult>)((st3) => ((Func<CodegenState, EmitResult>)((st4) => new EmitResult(state: st4, reg: r.reg)))(st_append_text(st3, movzx_byte_self(r.reg)))))(st_append_text(st2, setcc(cc_be(), r.reg)))))(st_append_text(st1, cmp_ri(r.reg, 9)))))(st_append_text(r.state, sub_ri(r.reg, 3)))))(emit_expr(st_set_tail_pos(st, false), args[(int)0]));

    public static EmitResult emit_is_whitespace_builtin(CodegenState st, List<IRExpr> args) => ((Func<EmitResult, EmitResult>)((r) => ((Func<CodegenState, EmitResult>)((st1) => ((Func<CodegenState, EmitResult>)((st2) => ((Func<CodegenState, EmitResult>)((st3) => new EmitResult(state: st3, reg: r.reg)))(st_append_text(st2, movzx_byte_self(r.reg)))))(st_append_text(st1, setcc(cc_be(), r.reg)))))(st_append_text(r.state, cmp_ri(r.reg, 2)))))(emit_expr(st_set_tail_pos(st, false), args[(int)0]));

    public static EmitResult emit_negate_builtin(CodegenState st, List<IRExpr> args) => ((Func<EmitResult, EmitResult>)((r) => ((Func<EmitResult, EmitResult>)((rd) => ((Func<CodegenState, EmitResult>)((st1) => ((Func<CodegenState, EmitResult>)((st2) => new EmitResult(state: st2, reg: rd.reg)))(st_append_text(st1, neg_r(rd.reg)))))(st_append_text(rd.state, mov_rr(rd.reg, r.reg)))))(alloc_temp(r.state))))(emit_expr(st_set_tail_pos(st, false), args[(int)0]));

    public static EmitResult emit_get_args_builtin(CodegenState st) => ((Func<EmitResult, EmitResult>)((rd) => ((Func<CodegenState, EmitResult>)((st1) => ((Func<CodegenState, EmitResult>)((st2) => ((Func<CodegenState, EmitResult>)((st3) => ((Func<CodegenState, EmitResult>)((st4) => ((Func<CodegenState, EmitResult>)((st5) => ((Func<CodegenState, EmitResult>)((st6) => new EmitResult(state: st6, reg: rd.reg)))(st_append_text(st5, add_ri(reg_r10(), 8)))))(st_append_text(st4, mov_store(reg_r10(), reg_r11(), 0)))))(st_append_text(st3, mov_rr(rd.reg, reg_r10())))))(st_append_text(st2, add_ri(reg_r10(), 8)))))(st_append_text(st1, mov_store(reg_r10(), reg_r11(), 0)))))(st_append_text(rd.state, li(reg_r11(), 0)))))(alloc_temp(st));

    public static EmitResult emit_current_dir_builtin(CodegenState st) => ((Func<EmitResult, EmitResult>)((rd) => ((Func<CodegenState, EmitResult>)((st1) => ((Func<CodegenState, EmitResult>)((st2) => ((Func<CodegenState, EmitResult>)((st3) => ((Func<CodegenState, EmitResult>)((st4) => new EmitResult(state: st4, reg: rd.reg)))(st_append_text(st3, add_ri(reg_r10(), 8)))))(st_append_text(st2, mov_store(reg_r10(), reg_r11(), 0)))))(st_append_text(st1, li(reg_r11(), 0)))))(st_append_text(rd.state, mov_rr(rd.reg, reg_r10())))))(alloc_temp(st));

    public static EmitResult emit_file_exists_builtin(CodegenState st, List<IRExpr> args) => ((Func<EmitResult, EmitResult>)((r) => ((Func<EmitResult, EmitResult>)((rd) => ((Func<CodegenState, EmitResult>)((st1) => new EmitResult(state: st1, reg: rd.reg)))(st_append_text(rd.state, li(rd.reg, 1)))))(alloc_temp(r.state))))(emit_expr(st_set_tail_pos(st, false), args[(int)0]));

    public static CodegenState emit_substring_alloc(CodegenState st, long str_loc, long start_loc, long len_loc) => ((Func<EmitResult, CodegenState>)((len_loaded) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => st_append_text(st2, add_rr(reg_r10(), reg_r11()))))(st_append_text(st1, and_ri(reg_r11(), (0 - 8))))))(st_append_text(st0, add_ri(reg_r11(), 15)))))(st_append_text(len_loaded.state, mov_rr(reg_r11(), len_loaded.reg)))))(load_local(st, len_loc));

    public static CodegenState emit_substring_copy(CodegenState st, long str_loc, long start_loc, long len_loc, long ptr_loc) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<long, CodegenState>)((sub_loop) => ((Func<EmitResult, CodegenState>)((len_ld) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<long, CodegenState>)((sub_exit_pos) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<EmitResult, CodegenState>)((src_ld) => ((Func<EmitResult, CodegenState>)((start_ld) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<EmitResult, CodegenState>)((ptr_ld) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => patch_jcc_at(st11, sub_exit_pos, ((long)st11.text.Count))))(st_append_text(st10, jmp((sub_loop - (((long)st10.text.Count) + 5)))))))(st_append_text(st9, add_ri(reg_r11(), 1)))))(st_append_text(st8, mov_store_byte(reg_rdx(), reg_rsi(), 8)))))(st_append_text(st7, add_rr(reg_rdx(), reg_r11())))))(st_append_text(ptr_ld.state, mov_rr(reg_rdx(), ptr_ld.reg)))))(load_local(st6, ptr_loc))))(st_append_text(st5, movzx_byte(reg_rsi(), reg_rsi(), 8)))))(st_append_text(st4, add_rr(reg_rsi(), reg_r11())))))(st_append_text(st3, add_rr(reg_rsi(), start_ld.reg)))))(st_append_text(start_ld.state, mov_rr(reg_rsi(), src_ld.reg)))))(load_local(src_ld.state, start_loc))))(load_local(st2, str_loc))))(st_append_text(st1, jcc(cc_ge(), 0)))))(((long)st1.text.Count))))(st_append_text(len_ld.state, cmp_rr(reg_r11(), len_ld.reg)))))(load_local(st0, len_loc))))(((long)st0.text.Count))))(st_append_text(st, li(reg_r11(), 0)));

    public static EmitResult emit_substring_builtin(CodegenState st, List<IRExpr> args) => ((Func<EmitResult, EmitResult>)((r0) => ((Func<EmitResult, EmitResult>)((loc0) => ((Func<CodegenState, EmitResult>)((st1) => ((Func<EmitResult, EmitResult>)((r1) => ((Func<EmitResult, EmitResult>)((loc1) => ((Func<CodegenState, EmitResult>)((st2) => ((Func<EmitResult, EmitResult>)((r2) => ((Func<EmitResult, EmitResult>)((loc2) => ((Func<CodegenState, EmitResult>)((st3) => ((Func<EmitResult, EmitResult>)((ptr_loc) => ((Func<EmitResult, EmitResult>)((ptr_tmp) => ((Func<CodegenState, EmitResult>)((st4) => ((Func<CodegenState, EmitResult>)((st5) => ((Func<EmitResult, EmitResult>)((len_ld) => ((Func<EmitResult, EmitResult>)((ptr_ld) => ((Func<CodegenState, EmitResult>)((st6) => ((Func<CodegenState, EmitResult>)((st7) => ((Func<CodegenState, EmitResult>)((st8) => ((Func<EmitResult, EmitResult>)((result) => new EmitResult(state: result.state, reg: result.reg)))(load_local(st8, ptr_loc.reg))))(emit_substring_copy(st7, loc0.reg, loc1.reg, loc2.reg, ptr_loc.reg))))(emit_substring_alloc(st6, loc0.reg, loc1.reg, loc2.reg))))(st_append_text(ptr_ld.state, mov_store(ptr_ld.reg, len_ld.reg, 0)))))(load_local(len_ld.state, ptr_loc.reg))))(load_local(st5, loc2.reg))))(store_local(st4, ptr_loc.reg, ptr_tmp.reg))))(st_append_text(ptr_tmp.state, mov_rr(ptr_tmp.reg, reg_r10())))))(alloc_temp(ptr_loc.state))))(alloc_local(st3))))(store_local(loc2.state, loc2.reg, r2.reg))))(alloc_local(r2.state))))(emit_expr(st2, args[(int)2]))))(store_local(loc1.state, loc1.reg, r1.reg))))(alloc_local(r1.state))))(emit_expr(st1, args[(int)1]))))(store_local(loc0.state, loc0.reg, r0.reg))))(alloc_local(r0.state))))(emit_expr(st_set_tail_pos(st, false), args[(int)0]));

    public static EmitResult emit_builtin(CodegenState st, string name, List<IRExpr> args) => ((name == "text-length") ? emit_text_length_builtin(st, args) : ((name == "integer-to-text") ? emit_helper_call_1(st, args, "__itoa") : ((name == "show") ? emit_helper_call_1(st, args, "__itoa") : ((name == "text-to-integer") ? emit_helper_call_1(st, args, "__text_to_int") : ((name == "text-replace") ? emit_helper_call_3(st, args, "__str_replace") : ((name == "text-contains") ? emit_helper_call_2(st, args, "__text_contains") : ((name == "text-starts-with") ? emit_helper_call_2(st, args, "__text_starts_with") : ((name == "text-compare") ? emit_helper_call_2(st, args, "__text_compare") : ((name == "text-concat-list") ? emit_helper_call_1(st, args, "__text_concat_list") : ((name == "text-split") ? emit_helper_call_2(st, args, "__text_split") : ((name == "substring") ? emit_substring_builtin(st, args) : ((name == "list-length") ? emit_list_length_builtin(st, args) : ((name == "list-at") ? emit_list_at_builtin(st, args) : ((name == "list-cons") ? emit_helper_call_2(st, args, "__list_cons") : ((name == "list-append") ? emit_helper_call_2(st, args, "__list_append") : ((name == "list-snoc") ? emit_helper_call_2(st, args, "__list_snoc") : ((name == "list-insert-at") ? emit_helper_call_3(st, args, "__list_insert_at") : ((name == "list-contains") ? emit_helper_call_2(st, args, "__list_contains") : ((name == "char-at") ? emit_char_at_builtin(st, args) : ((name == "char-code-at") ? emit_char_code_at_builtin(st, args) : ((name == "char-code") ? emit_expr(st_set_tail_pos(st, false), args[(int)0]) : ((name == "code-to-char") ? emit_expr(st_set_tail_pos(st, false), args[(int)0]) : ((name == "char-to-text") ? emit_char_to_text_builtin(st, args) : ((name == "is-letter") ? emit_is_letter_builtin(st, args) : ((name == "is-digit") ? emit_is_digit_builtin(st, args) : ((name == "is-whitespace") ? emit_is_whitespace_builtin(st, args) : ((name == "negate") ? emit_negate_builtin(st, args) : ((name == "get-args") ? emit_get_args_builtin(st) : ((name == "current-dir") ? emit_current_dir_builtin(st) : emit_file_exists_builtin(st, args))))))))))))))))))))))))))))));

    public static FlatApply flatten_apply(IRExpr expr, List<IRExpr> acc)
    {
        while (true)
        {
            var _tco_s = expr;
            if (_tco_s is IrApply _tco_m0)
            {
                var f = _tco_m0.Field0;
                var a = _tco_m0.Field1;
                var t = _tco_m0.Field2;
            var _tco_0 = f;
            var _tco_1 = Enumerable.Concat(new List<IRExpr> { a }, acc).ToList();
            expr = _tco_0;
            acc = _tco_1;
            continue;
            }
            else if (_tco_s is IrName _tco_m1)
            {
                var n = _tco_m1.Field0;
                var t = _tco_m1.Field1;
            return new FlatApply(func_name: n, args: acc);
            }
            {
            return new FlatApply(func_name: "", args: acc);
            }
        }
    }

    public static SavedArgs save_args_loop(CodegenState st, List<IRExpr> args, long i, List<long> acc)
    {
        while (true)
        {
            if ((i == ((long)args.Count)))
            {
            return new SavedArgs(state: st, locals: acc);
            }
            else
            {
            var r = emit_expr(st, args[(int)i]);
            var loc = alloc_local(r.state);
            var st1 = store_local(loc.state, loc.reg, r.reg);
            var _tco_0 = st1;
            var _tco_1 = args;
            var _tco_2 = (i + 1);
            var _tco_3 = ((Func<List<long>>)(() => { var _l = acc; _l.Add(loc.reg); return _l; }))();
            st = _tco_0;
            args = _tco_1;
            i = _tco_2;
            acc = _tco_3;
            continue;
            }
        }
    }

    public static CodegenState push_reg_args(CodegenState st, List<long> arg_locals, long i, long count)
    {
        while (true)
        {
            if ((i == count))
            {
            return st;
            }
            else
            {
            var loaded = load_local(st, arg_locals[(int)i]);
            var _tco_0 = st_append_text(loaded.state, push_r(loaded.reg));
            var _tco_1 = arg_locals;
            var _tco_2 = (i + 1);
            var _tco_3 = count;
            st = _tco_0;
            arg_locals = _tco_1;
            i = _tco_2;
            count = _tco_3;
            continue;
            }
        }
    }

    public static CodegenState pop_to_arg_regs(CodegenState st, long i)
    {
        while (true)
        {
            if ((i < 0))
            {
            return st;
            }
            else
            {
            var _tco_0 = st_append_text(st, pop_r(arg_regs()[(int)i]));
            var _tco_1 = (i - 1);
            st = _tco_0;
            i = _tco_1;
            continue;
            }
        }
    }

    public static CodegenState push_stack_args(CodegenState st, List<long> arg_locals, long i)
    {
        while (true)
        {
            if ((i < 6))
            {
            return st;
            }
            else
            {
            var loaded = load_local(st, arg_locals[(int)i]);
            var _tco_0 = st_append_text(loaded.state, push_r(loaded.reg));
            var _tco_1 = arg_locals;
            var _tco_2 = (i - 1);
            st = _tco_0;
            arg_locals = _tco_1;
            i = _tco_2;
            continue;
            }
        }
    }

    public static EmitResult emit_apply(CodegenState st, IRExpr func_expr, IRExpr arg_expr, CodexType result_ty) => ((Func<IRExpr, EmitResult>)((full_expr) => (((st.tco.active && st.tco.in_tail_pos) && is_self_call(full_expr, st.tco.current_func)) ? emit_tail_call(st, func_expr, arg_expr) : ((Func<FlatApply, EmitResult>)((flat) => (is_builtin(flat.func_name) ? emit_builtin(st, flat.func_name, flat.args) : result_ty switch { SumTy(var sname, var ctors) => ((Func<long, EmitResult>)((tag) => ((tag >= 0) ? emit_sum_ctor(st, flat.args, tag) : emit_direct_call(st, flat))))(find_ctor_tag(ctors, flat.func_name, 0)), FunTy(var pt, var rt) => ((Func<bool, EmitResult>)((is_local) => (is_local ? emit_indirect_call(st, flat) : emit_partial_application(st, flat.func_name, flat.args))))((lookup_local(st.locals, flat.func_name) >= 0)), _ => ((Func<bool, EmitResult>)((is_local) => (is_local ? emit_indirect_call(st, flat) : emit_direct_call(st, flat))))((lookup_local(st.locals, flat.func_name) >= 0)), })))(flatten_apply(func_expr, new List<IRExpr> { arg_expr })))))(new IrApply(func_expr, arg_expr, result_ty));

    public static EmitResult emit_direct_call(CodegenState st, FlatApply flat) => ((Func<SavedArgs, EmitResult>)((saved) => ((Func<long, EmitResult>)((arg_count) => ((Func<CodegenState, EmitResult>)((st1) => ((Func<long, EmitResult>)((reg_count) => ((Func<CodegenState, EmitResult>)((st2) => ((Func<CodegenState, EmitResult>)((st3) => ((Func<CodegenState, EmitResult>)((st4) => ((Func<long, EmitResult>)((stack_arg_count) => ((Func<CodegenState, EmitResult>)((st5) => ((Func<EmitResult, EmitResult>)((tmp) => new EmitResult(state: st_append_text(tmp.state, mov_rr(tmp.reg, reg_rax())), reg: tmp.reg)))(alloc_temp(st5))))(((stack_arg_count > 0) ? st_append_text(st4, add_ri(reg_rsp(), (stack_arg_count * 8))) : st4))))((arg_count - 6))))(emit_call_to(st3, flat.func_name))))(pop_to_arg_regs(st2, (reg_count - 1)))))(push_reg_args(st1, saved.locals, 0, reg_count))))(((arg_count < 6) ? arg_count : 6))))(push_stack_args(saved.state, saved.locals, (arg_count - 1)))))(((long)flat.args.Count))))(save_args_loop(st_set_tail_pos(st, false), flat.args, 0, new List<long>()));

    public static EmitResult emit_indirect_call(CodegenState st, FlatApply flat) => ((Func<SavedArgs, EmitResult>)((saved) => ((Func<long, EmitResult>)((arg_count) => ((Func<long, EmitResult>)((reg_count) => ((Func<CodegenState, EmitResult>)((st1) => ((Func<CodegenState, EmitResult>)((st2) => ((Func<EmitResult, EmitResult>)((closure_load) => ((Func<CodegenState, EmitResult>)((st3) => ((Func<CodegenState, EmitResult>)((st4) => ((Func<CodegenState, EmitResult>)((st5) => ((Func<EmitResult, EmitResult>)((tmp) => new EmitResult(state: st_append_text(tmp.state, mov_rr(tmp.reg, reg_rax())), reg: tmp.reg)))(alloc_temp(st5))))(st_append_text(st4, new List<long> { 255, 208 }))))(st_append_text(st3, mov_load(reg_rax(), reg_r11(), 0)))))(st_append_text(closure_load.state, mov_rr(reg_r11(), closure_load.reg)))))(load_local(st2, lookup_local(st2.locals, flat.func_name)))))(pop_to_arg_regs(st1, (reg_count - 1)))))(push_reg_args(saved.state, saved.locals, 0, reg_count))))(((arg_count < 6) ? arg_count : 6))))(((long)flat.args.Count))))(save_args_loop(st_set_tail_pos(st, false), flat.args, 0, new List<long>()));

    public static List<IRExpr> flatten_tail_args(IRExpr expr, List<IRExpr> acc)
    {
        while (true)
        {
            var _tco_s = expr;
            if (_tco_s is IrApply _tco_m0)
            {
                var f = _tco_m0.Field0;
                var a = _tco_m0.Field1;
                var t = _tco_m0.Field2;
            var _tco_0 = f;
            var _tco_1 = Enumerable.Concat(new List<IRExpr> { a }, acc).ToList();
            expr = _tco_0;
            acc = _tco_1;
            continue;
            }
            {
            return acc;
            }
        }
    }

    public static CodegenState eval_tail_args(CodegenState st, List<IRExpr> args, List<long> temp_locals, long i)
    {
        while (true)
        {
            if ((i == ((long)args.Count)))
            {
            return st;
            }
            else
            {
            var st_notail = st_set_tail_pos(st, false);
            var r = emit_expr(st_notail, args[(int)i]);
            var st1 = store_local(r.state, temp_locals[(int)i], r.reg);
            var _tco_0 = st1;
            var _tco_1 = args;
            var _tco_2 = temp_locals;
            var _tco_3 = (i + 1);
            st = _tco_0;
            args = _tco_1;
            temp_locals = _tco_2;
            i = _tco_3;
            continue;
            }
        }
    }

    public static CodegenState copy_temps_to_params(CodegenState st, List<long> temp_locals, List<long> param_locals, long i)
    {
        while (true)
        {
            if ((i == ((long)temp_locals.Count)))
            {
            return st;
            }
            else
            {
            var loaded = load_local(st, temp_locals[(int)i]);
            var st1 = store_local(loaded.state, param_locals[(int)i], loaded.reg);
            var _tco_0 = st1;
            var _tco_1 = temp_locals;
            var _tco_2 = param_locals;
            var _tco_3 = (i + 1);
            st = _tco_0;
            temp_locals = _tco_1;
            param_locals = _tco_2;
            i = _tco_3;
            continue;
            }
        }
    }

    public static EmitResult emit_tail_call(CodegenState st, IRExpr func_expr, IRExpr arg_expr) => ((Func<List<IRExpr>, EmitResult>)((args) => ((Func<CodegenState, EmitResult>)((st1) => ((Func<CodegenState, EmitResult>)((st2) => ((Func<long, EmitResult>)((rel32) => ((Func<CodegenState, EmitResult>)((st3) => ((Func<EmitResult, EmitResult>)((dummy) => new EmitResult(state: st_append_text(dummy.state, li(dummy.reg, 0)), reg: dummy.reg)))(alloc_temp(st3))))(st_append_text(st2, jmp(rel32)))))((st.tco.loop_top - (((long)st2.text.Count) + 5)))))(copy_temps_to_params(st1, st.tco.temp_locals, st.tco.param_locals, 0))))(eval_tail_args(st, args, st.tco.temp_locals, 0))))(flatten_tail_args(func_expr, new List<IRExpr> { arg_expr }));

    public static EmitResult emit_sum_ctor(CodegenState st, List<IRExpr> args, long tag) => ((Func<SavedArgs, EmitResult>)((saved) => ((Func<long, EmitResult>)((field_count) => ((Func<long, EmitResult>)((total_size) => ((Func<EmitResult, EmitResult>)((ptr_loc) => ((Func<EmitResult, EmitResult>)((ptr_tmp) => ((Func<CodegenState, EmitResult>)((st1) => ((Func<CodegenState, EmitResult>)((st2) => ((Func<CodegenState, EmitResult>)((st3) => ((Func<EmitResult, EmitResult>)((tag_tmp) => ((Func<CodegenState, EmitResult>)((st4) => ((Func<EmitResult, EmitResult>)((ptr_load1) => ((Func<CodegenState, EmitResult>)((st5) => ((Func<CodegenState, EmitResult>)((st6) => load_local(st6, ptr_loc.reg)))(emit_store_ctor_fields(st5, saved.locals, ptr_loc.reg, 0))))(st_append_text(ptr_load1.state, mov_store(ptr_load1.reg, tag_tmp.reg, 0)))))(load_local(st4, ptr_loc.reg))))(st_append_text(tag_tmp.state, li(tag_tmp.reg, tag)))))(alloc_temp(st3))))(st_append_text(st2, add_ri(reg_r10(), total_size)))))(store_local(st1, ptr_loc.reg, ptr_tmp.reg))))(st_append_text(ptr_tmp.state, mov_rr(ptr_tmp.reg, reg_r10())))))(alloc_temp(ptr_loc.state))))(alloc_local(saved.state))))(((1 + field_count) * 8))))(((long)args.Count))))(save_args_loop(st, args, 0, new List<long>()));

    public static CodegenState emit_store_ctor_fields(CodegenState st, List<long> field_locals, long ptr_loc, long i)
    {
        while (true)
        {
            if ((i == ((long)field_locals.Count)))
            {
            return st;
            }
            else
            {
            var val = load_local(st, field_locals[(int)i]);
            var ptr = load_local(val.state, ptr_loc);
            var st1 = st_append_text(ptr.state, mov_store(ptr.reg, val.reg, (8 + (i * 8))));
            var _tco_0 = st1;
            var _tco_1 = field_locals;
            var _tco_2 = ptr_loc;
            var _tco_3 = (i + 1);
            st = _tco_0;
            field_locals = _tco_1;
            ptr_loc = _tco_2;
            i = _tco_3;
            continue;
            }
        }
    }

    public static CodegenState emit_load_func_addr(CodegenState st, long reg, string func_name) => ((Func<FuncAddrFixup, CodegenState>)((fixup) => ((Func<CodegenState, CodegenState>)((st1) => new CodegenState(text: st1.text, rodata: st1.rodata, func_offsets: st1.func_offsets, call_patches: st1.call_patches, func_addr_fixups: ((Func<List<FuncAddrFixup>>)(() => { var _l = st1.func_addr_fixups; _l.Add(fixup); return _l; }))(), locals: st1.locals, next_temp: st1.next_temp, next_local: st1.next_local, spill_count: st1.spill_count, load_local_toggle: st1.load_local_toggle, tco: st1.tco)))(st_append_text(st, mov_ri64(reg, 0)))))(new FuncAddrFixup(patch_offset: (((long)st.text.Count) + 2), target: func_name));

    public static CodegenState emit_trampoline_shift_args(CodegenState st, long i, long num_captures)
    {
        while (true)
        {
            if ((i < 0))
            {
            return st;
            }
            else
            {
            if (((i + num_captures) < 6))
            {
            var _tco_0 = st_append_text(st, mov_rr(arg_regs()[(int)(i + num_captures)], arg_regs()[(int)i]));
            var _tco_1 = (i - 1);
            var _tco_2 = num_captures;
            st = _tco_0;
            i = _tco_1;
            num_captures = _tco_2;
            continue;
            }
            else
            {
            var _tco_0 = st;
            var _tco_1 = (i - 1);
            var _tco_2 = num_captures;
            st = _tco_0;
            i = _tco_1;
            num_captures = _tco_2;
            continue;
            }
            }
        }
    }

    public static CodegenState emit_trampoline_load_captures(CodegenState st, long i, long num_captures)
    {
        while (true)
        {
            if ((i == num_captures))
            {
            return st;
            }
            else
            {
            if ((i < 6))
            {
            var _tco_0 = st_append_text(st, mov_load(arg_regs()[(int)i], reg_r11(), (8 + (i * 8))));
            var _tco_1 = (i + 1);
            var _tco_2 = num_captures;
            st = _tco_0;
            i = _tco_1;
            num_captures = _tco_2;
            continue;
            }
            else
            {
            return st;
            }
            }
        }
    }

    public static EmitResult emit_partial_application(CodegenState st, string func_name, List<IRExpr> captured_args) => ((Func<SavedArgs, EmitResult>)((saved) => ((Func<long, EmitResult>)((num_captures) => ((Func<string, EmitResult>)((tramp_name) => ((Func<long, EmitResult>)((jmp_over_pos) => ((Func<CodegenState, EmitResult>)((st1) => ((Func<CodegenState, EmitResult>)((st2) => ((Func<CodegenState, EmitResult>)((st3) => ((Func<CodegenState, EmitResult>)((st4) => ((Func<CodegenState, EmitResult>)((st5) => ((Func<CodegenState, EmitResult>)((st6) => ((Func<CodegenState, EmitResult>)((st7) => ((Func<long, EmitResult>)((closure_size) => ((Func<EmitResult, EmitResult>)((ptr_loc) => ((Func<EmitResult, EmitResult>)((ptr_tmp) => ((Func<CodegenState, EmitResult>)((st8) => ((Func<CodegenState, EmitResult>)((st9) => ((Func<CodegenState, EmitResult>)((st10) => ((Func<CodegenState, EmitResult>)((st11) => ((Func<EmitResult, EmitResult>)((ptr_load1) => ((Func<CodegenState, EmitResult>)((st12) => ((Func<CodegenState, EmitResult>)((st13) => load_local(st13, ptr_loc.reg)))(emit_store_closure_captures(st12, saved.locals, ptr_loc.reg, 0))))(st_append_text(ptr_load1.state, mov_store(ptr_load1.reg, reg_rax(), 0)))))(load_local(st11, ptr_loc.reg))))(emit_load_func_addr(st10, reg_rax(), tramp_name))))(st_append_text(st9, add_ri(reg_r10(), closure_size)))))(store_local(st8, ptr_loc.reg, ptr_tmp.reg))))(st_append_text(ptr_tmp.state, mov_rr(ptr_tmp.reg, reg_r10())))))(alloc_temp(ptr_loc.state))))(alloc_local(st7))))(((1 + num_captures) * 8))))(patch_jmp_at(st6, jmp_over_pos, ((long)st6.text.Count)))))(st_append_text(st5, new List<long> { 255, 224 }))))(emit_load_func_addr(st4, reg_rax(), func_name))))(emit_trampoline_load_captures(st3, 0, num_captures))))(emit_trampoline_shift_args(st2, 5, num_captures))))(record_func_offset(st1, tramp_name))))(st_append_text(saved.state, jmp(0)))))(((long)saved.state.text.Count))))((func_name + ("__tramp__" + (_Cce.FromUnicode(num_captures.ToString()) + ("__" + _Cce.FromUnicode(((long)saved.state.text.Count).ToString()))))))))(((long)captured_args.Count))))(save_args_loop(st_set_tail_pos(st, false), captured_args, 0, new List<long>()));

    public static CodegenState emit_store_closure_captures(CodegenState st, List<long> cap_locals, long ptr_loc, long i)
    {
        while (true)
        {
            if ((i == ((long)cap_locals.Count)))
            {
            return st;
            }
            else
            {
            var val = load_local(st, cap_locals[(int)i]);
            var ptr = load_local(val.state, ptr_loc);
            var st1 = st_append_text(ptr.state, mov_store(ptr.reg, val.reg, (8 + (i * 8))));
            var _tco_0 = st1;
            var _tco_1 = cap_locals;
            var _tco_2 = ptr_loc;
            var _tco_3 = (i + 1);
            st = _tco_0;
            cap_locals = _tco_1;
            ptr_loc = _tco_2;
            i = _tco_3;
            continue;
            }
        }
    }

    public static List<PatchEntry> collect_func_addr_patches(List<FuncAddrFixup> fixups, List<FuncOffset> offsets, long text_base, long i, List<PatchEntry> acc)
    {
        while (true)
        {
            if ((i == ((long)fixups.Count)))
            {
            return acc;
            }
            else
            {
            var f = fixups[(int)i];
            var func_offset = lookup_func_offset(offsets, f.target);
            var addr = (text_base + func_offset);
            var addr_bytes = write_i64(addr);
            var p0 = new PatchEntry(pos: f.patch_offset, b0: addr_bytes[(int)0], b1: addr_bytes[(int)1], b2: addr_bytes[(int)2], b3: addr_bytes[(int)3]);
            var p1 = new PatchEntry(pos: (f.patch_offset + 4), b0: addr_bytes[(int)4], b1: addr_bytes[(int)5], b2: addr_bytes[(int)6], b3: addr_bytes[(int)7]);
            var _tco_0 = fixups;
            var _tco_1 = offsets;
            var _tco_2 = text_base;
            var _tco_3 = (i + 1);
            var _tco_4 = Enumerable.Concat(acc, new List<PatchEntry> { p0, p1 }).ToList();
            fixups = _tco_0;
            offsets = _tco_1;
            text_base = _tco_2;
            i = _tco_3;
            acc = _tco_4;
            continue;
            }
        }
    }

    public static long find_record_field_index(List<RecordField> fields, string name, long i)
    {
        while (true)
        {
            if ((i == ((long)fields.Count)))
            {
            return 0;
            }
            else
            {
            var f = fields[(int)i];
            if ((f.name.value == name))
            {
            return i;
            }
            else
            {
            var _tco_0 = fields;
            var _tco_1 = name;
            var _tco_2 = (i + 1);
            fields = _tco_0;
            name = _tco_1;
            i = _tco_2;
            continue;
            }
            }
        }
    }

    public static EvalFieldsResult emit_eval_record_fields(CodegenState st, List<IRFieldVal> fields, long i, List<FieldLocal> acc)
    {
        while (true)
        {
            if ((i == ((long)fields.Count)))
            {
            return new EvalFieldsResult(state: st, field_locals: acc);
            }
            else
            {
            var fv = fields[(int)i];
            var r = emit_expr(st, fv.value);
            var loc = alloc_local(r.state);
            var st1 = store_local(loc.state, loc.reg, r.reg);
            var _tco_0 = st1;
            var _tco_1 = fields;
            var _tco_2 = (i + 1);
            var _tco_3 = ((Func<List<FieldLocal>>)(() => { var _l = acc; _l.Add(new FieldLocal(name: fv.name, slot: loc.reg)); return _l; }))();
            st = _tco_0;
            fields = _tco_1;
            i = _tco_2;
            acc = _tco_3;
            continue;
            }
        }
    }

    public static long find_field_local_slot(List<FieldLocal> fls, string name, long i)
    {
        while (true)
        {
            if ((i == ((long)fls.Count)))
            {
            return (0 - 1);
            }
            else
            {
            var fl = fls[(int)i];
            if ((fl.name == name))
            {
            return fl.slot;
            }
            else
            {
            var _tco_0 = fls;
            var _tco_1 = name;
            var _tco_2 = (i + 1);
            fls = _tco_0;
            name = _tco_1;
            i = _tco_2;
            continue;
            }
            }
        }
    }

    public static CodegenState emit_store_record_fields_by_type(CodegenState st, List<RecordField> type_fields, List<FieldLocal> field_locals, long ptr_loc, long i)
    {
        while (true)
        {
            if ((i == ((long)type_fields.Count)))
            {
            return st;
            }
            else
            {
            var tf = type_fields[(int)i];
            var slot = find_field_local_slot(field_locals, tf.name.value, 0);
            if ((slot >= 0))
            {
            var val = load_local(st, slot);
            var ptr = load_local(val.state, ptr_loc);
            var st1 = st_append_text(ptr.state, mov_store(ptr.reg, val.reg, (i * 8)));
            var _tco_0 = st1;
            var _tco_1 = type_fields;
            var _tco_2 = field_locals;
            var _tco_3 = ptr_loc;
            var _tco_4 = (i + 1);
            st = _tco_0;
            type_fields = _tco_1;
            field_locals = _tco_2;
            ptr_loc = _tco_3;
            i = _tco_4;
            continue;
            }
            else
            {
            var _tco_0 = st;
            var _tco_1 = type_fields;
            var _tco_2 = field_locals;
            var _tco_3 = ptr_loc;
            var _tco_4 = (i + 1);
            st = _tco_0;
            type_fields = _tco_1;
            field_locals = _tco_2;
            ptr_loc = _tco_3;
            i = _tco_4;
            continue;
            }
            }
        }
    }

    public static CodegenState emit_store_record_fields_by_list(CodegenState st, List<FieldLocal> field_locals, long ptr_loc, long i)
    {
        while (true)
        {
            if ((i == ((long)field_locals.Count)))
            {
            return st;
            }
            else
            {
            var fl = field_locals[(int)i];
            var val = load_local(st, fl.slot);
            var ptr = load_local(val.state, ptr_loc);
            var st1 = st_append_text(ptr.state, mov_store(ptr.reg, val.reg, (i * 8)));
            var _tco_0 = st1;
            var _tco_1 = field_locals;
            var _tco_2 = ptr_loc;
            var _tco_3 = (i + 1);
            st = _tco_0;
            field_locals = _tco_1;
            ptr_loc = _tco_2;
            i = _tco_3;
            continue;
            }
        }
    }

    public static EmitResult emit_record(CodegenState st, List<IRFieldVal> fields, CodexType ty) => ((Func<EvalFieldsResult, EmitResult>)((evaled) => ((Func<long, EmitResult>)((field_count) => ((Func<long, EmitResult>)((total_size) => ((Func<EmitResult, EmitResult>)((ptr_loc) => ((Func<EmitResult, EmitResult>)((ptr_tmp) => ((Func<CodegenState, EmitResult>)((st1) => ((Func<CodegenState, EmitResult>)((st2) => ((Func<CodegenState, EmitResult>)((st3) => ((Func<CodegenState, EmitResult>)((st4) => load_local(st4, ptr_loc.reg)))(ty switch { RecordTy(var rname, var type_fields) => emit_store_record_fields_by_type(st3, type_fields, evaled.field_locals, ptr_loc.reg, 0), _ => emit_store_record_fields_by_list(st3, evaled.field_locals, ptr_loc.reg, 0), })))(st_append_text(st2, add_ri(reg_r10(), total_size)))))(store_local(st1, ptr_loc.reg, ptr_tmp.reg))))(st_append_text(ptr_tmp.state, mov_rr(ptr_tmp.reg, reg_r10())))))(alloc_temp(ptr_loc.state))))(alloc_local(evaled.state))))((field_count * 8))))(((long)fields.Count))))(emit_eval_record_fields(st, fields, 0, new List<FieldLocal>()));

    public static EmitResult emit_field_access(CodegenState st, IRExpr rec_expr, string field_name) => ((Func<CodexType, EmitResult>)((rec_ty) => ((Func<long, EmitResult>)((field_idx) => ((Func<EmitResult, EmitResult>)((rec_result) => ((Func<EmitResult, EmitResult>)((rd) => new EmitResult(state: st_append_text(rd.state, mov_load(rd.reg, rec_result.reg, (field_idx * 8))), reg: rd.reg)))(alloc_temp(rec_result.state))))(emit_expr(st, rec_expr))))(rec_ty switch { RecordTy(var rname, var rfields) => find_record_field_index(rfields, field_name, 0), _ => 0, })))(ir_expr_type(rec_expr));

    public static EmitResult emit_match(CodegenState st, IRExpr scrut_expr, List<IRBranch> branches) => ((Func<bool, EmitResult>)((saved_tail) => ((Func<EmitResult, EmitResult>)((scrut_result) => ((Func<EmitResult, EmitResult>)((scrut_loc) => ((Func<CodegenState, EmitResult>)((st1) => ((Func<CodegenState, EmitResult>)((st1a) => ((Func<EmitResult, EmitResult>)((result_loc) => ((Func<MatchBranchState, EmitResult>)((mbs) => ((Func<MatchBranchState, EmitResult>)((mbs_final) => ((Func<CodegenState, EmitResult>)((st_end) => load_local(st_end, result_loc.reg)))(patch_match_end_jumps(mbs_final.cg_state, mbs_final.end_patches, 0))))(emit_match_branch_loop(mbs, scrut_loc.reg, result_loc.reg, branches, 0, ((long)branches.Count)))))(new MatchBranchState(cg_state: st1a, end_patches: new List<long>()))))(alloc_local(st1a))))(st_set_tail_pos(st1, saved_tail))))(store_local(scrut_loc.state, scrut_loc.reg, scrut_result.reg))))(alloc_local(scrut_result.state))))(emit_expr(st_set_tail_pos(st, false), scrut_expr))))(st.tco.in_tail_pos);

    public static MatchBranchState emit_match_branch_loop(MatchBranchState mbs, long scrut_loc, long result_loc, List<IRBranch> branches, long i, long total)
    {
        while (true)
        {
            if ((i == total))
            {
            return mbs;
            }
            else
            {
            var b = branches[(int)i];
            var mbs1 = emit_one_match_branch(mbs, scrut_loc, result_loc, b, (i < (total - 1)));
            var _tco_0 = mbs1;
            var _tco_1 = scrut_loc;
            var _tco_2 = result_loc;
            var _tco_3 = branches;
            var _tco_4 = (i + 1);
            var _tco_5 = total;
            mbs = _tco_0;
            scrut_loc = _tco_1;
            result_loc = _tco_2;
            branches = _tco_3;
            i = _tco_4;
            total = _tco_5;
            continue;
            }
        }
    }

    public static MatchBranchState emit_one_match_branch(MatchBranchState mbs, long scrut_loc, long result_loc, IRBranch branch, bool needs_jmp_end) => ((Func<EmitPatternResult, MatchBranchState>)((pat_result) => ((Func<EmitResult, MatchBranchState>)((body_result) => ((Func<CodegenState, MatchBranchState>)((st1) => ((Func<MatchBranchState, MatchBranchState>)((st2) => ((Func<MatchBranchState, MatchBranchState>)((st3) => st3))(((pat_result.next_branch_patch >= 0) ? new MatchBranchState(cg_state: patch_jcc_at(st2.cg_state, pat_result.next_branch_patch, ((long)st2.cg_state.text.Count)), end_patches: st2.end_patches) : st2))))((needs_jmp_end ? ((Func<long, MatchBranchState>)((jmp_pos) => new MatchBranchState(cg_state: st_append_text(st1, jmp(0)), end_patches: ((Func<List<long>>)(() => { var _l = mbs.end_patches; _l.Add(jmp_pos); return _l; }))())))(((long)st1.text.Count)) : new MatchBranchState(cg_state: st1, end_patches: mbs.end_patches)))))(store_local(body_result.state, result_loc, body_result.reg))))(emit_expr(pat_result.state, branch.body))))(emit_pattern(mbs.cg_state)(scrut_loc)(branch.pattern));

    public static EmitPatternResult emit_pattern(CodegenState st, long scrut_loc, IRPat pat) => pat switch { IrWildPat { } => new EmitPatternResult(state: st, next_branch_patch: (0 - 1)), IrVarPat(var name, var ty) => ((Func<EmitResult, EmitPatternResult>)((var_loc) => ((Func<EmitResult, EmitPatternResult>)((loaded) => ((Func<CodegenState, EmitPatternResult>)((st1) => new EmitPatternResult(state: add_local(st1, name, var_loc.reg), next_branch_patch: (0 - 1))))(store_local(loaded.state, var_loc.reg, loaded.reg))))(load_local(var_loc.state, scrut_loc))))(alloc_local(st)), IrCtorPat(var name, var sub_pats, var ty) => ((Func<EmitResult, EmitPatternResult>)((scrut_load) => ((Func<EmitResult, EmitPatternResult>)((tag_reg) => ((Func<CodegenState, EmitPatternResult>)((st1) => ((Func<long, EmitPatternResult>)((expected_tag) => ((Func<CodegenState, EmitPatternResult>)((st2) => ((Func<long, EmitPatternResult>)((jcc_pos) => ((Func<CodegenState, EmitPatternResult>)((st3) => ((Func<CodegenState, EmitPatternResult>)((st4) => new EmitPatternResult(state: st4, next_branch_patch: jcc_pos)))(bind_ctor_fields(st3, scrut_loc, sub_pats, 0))))(st_append_text(st2, jcc(cc_ne(), 0)))))(((long)st2.text.Count))))(st_append_text(st1, cmp_ri(tag_reg.reg, expected_tag)))))(ty switch { SumTy(var sname, var ctors) => find_ctor_tag(ctors, name, 0), _ => 0, })))(st_append_text(tag_reg.state, mov_load(tag_reg.reg, scrut_load.reg, 0)))))(alloc_temp(scrut_load.state))))(load_local(st, scrut_loc)), IrLitPat(var value, var ty) => new EmitPatternResult(state: st, next_branch_patch: (0 - 1)), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static CodegenState bind_ctor_fields(CodegenState st, long scrut_loc, List<IRPat> sub_pats, long i)
    {
        while (true)
        {
            if ((i == ((long)sub_pats.Count)))
            {
            return st;
            }
            else
            {
            var sub = sub_pats[(int)i];
            var _tco_s = sub;
            if (_tco_s is IrVarPat _tco_m0)
            {
                var name = _tco_m0.Field0;
                var ty = _tco_m0.Field1;
            var field_loc = alloc_local(st);
            var scrut_load = load_local(field_loc.state, scrut_loc);
            var field_val = alloc_temp(scrut_load.state);
            var st1 = st_append_text(field_val.state, mov_load(field_val.reg, scrut_load.reg, ((1 + i) * 8)));
            var st2 = store_local(st1, field_loc.reg, field_val.reg);
            var _tco_0 = add_local(st2, name, field_loc.reg);
            var _tco_1 = scrut_loc;
            var _tco_2 = sub_pats;
            var _tco_3 = (i + 1);
            st = _tco_0;
            scrut_loc = _tco_1;
            sub_pats = _tco_2;
            i = _tco_3;
            continue;
            }
            {
            var _tco_0 = st;
            var _tco_1 = scrut_loc;
            var _tco_2 = sub_pats;
            var _tco_3 = (i + 1);
            st = _tco_0;
            scrut_loc = _tco_1;
            sub_pats = _tco_2;
            i = _tco_3;
            continue;
            }
            }
        }
    }

    public static CodegenState patch_match_end_jumps(CodegenState st, List<long> patches, long i)
    {
        while (true)
        {
            if ((i == ((long)patches.Count)))
            {
            return st;
            }
            else
            {
            var _tco_0 = patch_jmp_at(st, patches[(int)i], ((long)st.text.Count));
            var _tco_1 = patches;
            var _tco_2 = (i + 1);
            st = _tco_0;
            patches = _tco_1;
            i = _tco_2;
            continue;
            }
        }
    }

    public static SavedArgs emit_eval_list_elems(CodegenState st, List<IRExpr> elems, long i, List<long> acc)
    {
        while (true)
        {
            if ((i == ((long)elems.Count)))
            {
            return new SavedArgs(state: st, locals: acc);
            }
            else
            {
            var r = emit_expr(st, elems[(int)i]);
            var loc = alloc_local(r.state);
            var st1 = store_local(loc.state, loc.reg, r.reg);
            var _tco_0 = st1;
            var _tco_1 = elems;
            var _tco_2 = (i + 1);
            var _tco_3 = ((Func<List<long>>)(() => { var _l = acc; _l.Add(loc.reg); return _l; }))();
            st = _tco_0;
            elems = _tco_1;
            i = _tco_2;
            acc = _tco_3;
            continue;
            }
        }
    }

    public static CodegenState emit_store_list_elems(CodegenState st, List<long> elem_locals, long ptr_loc, long i)
    {
        while (true)
        {
            if ((i == ((long)elem_locals.Count)))
            {
            return st;
            }
            else
            {
            var val = load_local(st, elem_locals[(int)i]);
            var ptr = load_local(val.state, ptr_loc);
            var st1 = st_append_text(ptr.state, mov_store(ptr.reg, val.reg, (8 + (i * 8))));
            var _tco_0 = st1;
            var _tco_1 = elem_locals;
            var _tco_2 = ptr_loc;
            var _tco_3 = (i + 1);
            st = _tco_0;
            elem_locals = _tco_1;
            ptr_loc = _tco_2;
            i = _tco_3;
            continue;
            }
        }
    }

    public static EmitResult emit_list(CodegenState st, List<IRExpr> elems) => ((Func<SavedArgs, EmitResult>)((saved) => ((Func<long, EmitResult>)((count) => ((Func<EmitResult, EmitResult>)((cap_tmp) => ((Func<CodegenState, EmitResult>)((st1) => ((Func<CodegenState, EmitResult>)((st2) => ((Func<CodegenState, EmitResult>)((st3) => ((Func<EmitResult, EmitResult>)((ptr_loc) => ((Func<EmitResult, EmitResult>)((ptr_tmp) => ((Func<CodegenState, EmitResult>)((st4) => ((Func<CodegenState, EmitResult>)((st5) => ((Func<CodegenState, EmitResult>)((st6) => ((Func<EmitResult, EmitResult>)((len_tmp) => ((Func<CodegenState, EmitResult>)((st7) => ((Func<EmitResult, EmitResult>)((ptr_load) => ((Func<CodegenState, EmitResult>)((st8) => ((Func<CodegenState, EmitResult>)((st9) => load_local(st9, ptr_loc.reg)))(emit_store_list_elems(st8, saved.locals, ptr_loc.reg, 0))))(st_append_text(ptr_load.state, mov_store(ptr_load.reg, len_tmp.reg, 0)))))(load_local(st7, ptr_loc.reg))))(st_append_text(len_tmp.state, li(len_tmp.reg, count)))))(alloc_temp(st6))))(st_append_text(st5, add_ri(reg_r10(), ((count + 1) * 8))))))(store_local(st4, ptr_loc.reg, ptr_tmp.reg))))(st_append_text(ptr_tmp.state, mov_rr(ptr_tmp.reg, reg_r10())))))(alloc_temp(ptr_loc.state))))(alloc_local(st3))))(st_append_text(st2, add_ri(reg_r10(), 8)))))(st_append_text(st1, mov_store(reg_r10(), cap_tmp.reg, 0)))))(st_append_text(cap_tmp.state, li(cap_tmp.reg, count)))))(alloc_temp(saved.state))))(((long)elems.Count))))(emit_eval_list_elems(st, elems, 0, new List<long>()));

    public static List<long> multiboot_header() => Enumerable.Concat(write_i32(464367618), Enumerable.Concat(write_i32(0), write_i32(3830599678)).ToList()).ToList();

    public static List<long> tramp_clear_pages() => new List<long> { 250, 191, 0, 16, 0, 0, 185, 0, 12, 0, 0, 49, 192, 243, 171 };

    public static List<long> tramp_page_tables() => new List<long> { 199, 5, 0, 16, 0, 0, 3, 32, 0, 0, 199, 5, 0, 32, 0, 0, 3, 48, 0, 0, 191, 0, 48, 0, 0, 185, 0, 1, 0, 0, 184, 131, 0, 0, 0, 137, 7, 199, 71, 4, 0, 0, 0, 0, 131, 199, 8, 5, 0, 0, 32, 0, 73, 117, 236 };

    public static List<long> tramp_enable_long_mode() => new List<long> { 184, 0, 16, 0, 0, 15, 34, 216, 15, 32, 224, 131, 200, 32, 15, 34, 224, 185, 128, 0, 0, 192, 15, 50, 13, 0, 1, 0, 0, 15, 48, 15, 32, 192, 13, 0, 0, 0, 128, 15, 34, 192 };

    public static List<long> trampoline_code() => Enumerable.Concat(tramp_clear_pages(), Enumerable.Concat(tramp_page_tables(), tramp_enable_long_mode()).ToList()).ToList();

    public static List<long> tramp_gdt_data() => new List<long> { 235, 30, 0, 0, 0, 0, 0, 0, 0, 0, 255, 255, 0, 0, 0, 154, 175, 0, 255, 255, 0, 0, 0, 146, 207, 0 };

    public static List<long> trampoline_gdt_section() => Enumerable.Concat(tramp_gdt_data(), Enumerable.Concat(write_i16(23), Enumerable.Concat(write_i32((bare_metal_load_addr() + 126)), Enumerable.Concat(new List<long> { 15, 1, 21 }, Enumerable.Concat(write_i32((bare_metal_load_addr() + 150)), new List<long> { 234, 0, 0, 0, 0, 8, 0 }).ToList()).ToList()).ToList()).ToList()).ToList();

    public static TrampolineResult bare_metal_trampoline() => ((Func<List<long>, TrampolineResult>)((bytes) => new TrampolineResult(bytes: bytes, far_jump_patch_pos: 163)))(Enumerable.Concat(multiboot_header(), Enumerable.Concat(trampoline_code(), trampoline_gdt_section()).ToList()).ToList());

    public static CodegenState emit_out_byte(CodegenState st, long port, long value) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => st_append_text(st2, out_dx_al())))(st_append_text(st1, li(reg_rax(), value)))))(st_append_text(st, li(reg_rdx(), port)));

    public static CodegenState emit_com1_init(CodegenState st) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => emit_out_byte(st5, 1020, 11)))(emit_out_byte(st4, 1018, 199))))(emit_out_byte(st3, 1019, 3))))(emit_out_byte(st2, 1017, 0))))(emit_out_byte(st1, 1016, 1))))(emit_out_byte(st, 1019, 128));

    public static CodegenState emit_serial_wait_and_send(CodegenState st, long byte_val) => ((Func<long, CodegenState>)((wait_top) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<long, CodegenState>)((jne_pos) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => st_append_text(st8, out_dx_al())))(st_append_text(st7, li(reg_rax(), byte_val)))))(st_append_text(st6, li(reg_rdx(), 1016)))))(patch_jcc_at(st5, jne_pos, ((long)st5.text.Count)))))(st_append_text(st4, jmp((wait_top - (((long)st4.text.Count) + 5)))))))(st_append_text(st3, jcc(cc_ne(), 0)))))(((long)st3.text.Count))))(st_append_text(st2, new List<long> { 168, 32 }))))(st_append_text(st1, new List<long> { 236 }))))(st_append_text(st, li(reg_rdx(), 1021)))))(((long)st.text.Count));

    public static CodegenState emit_serial_send_rdi(CodegenState st) => ((Func<long, CodegenState>)((wait_top) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<long, CodegenState>)((jne_pos) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => st_append_text(st8, out_dx_al())))(st_append_text(st7, mov_rr(reg_rax(), reg_rdi())))))(st_append_text(st6, li(reg_rdx(), 1016)))))(patch_jcc_at(st5, jne_pos, ((long)st5.text.Count)))))(st_append_text(st4, jmp((wait_top - (((long)st4.text.Count) + 5)))))))(st_append_text(st3, jcc(cc_ne(), 0)))))(((long)st3.text.Count))))(st_append_text(st2, new List<long> { 168, 32 }))))(st_append_text(st1, new List<long> { 236 }))))(st_append_text(st, li(reg_rdx(), 1021)))))(((long)st.text.Count));

    public static ItoaState emit_itoa_zero_check(CodegenState st) => ((Func<CodegenState, ItoaState>)((st1) => ((Func<CodegenState, ItoaState>)((st2) => ((Func<long, ItoaState>)((jne_pos) => ((Func<CodegenState, ItoaState>)((st3) => ((Func<CodegenState, ItoaState>)((st4) => ((Func<long, ItoaState>)((jmp_pos) => ((Func<CodegenState, ItoaState>)((st5) => ((Func<CodegenState, ItoaState>)((st6) => new ItoaState(cg: st6, jmp_done_zero_pos: jmp_pos)))(patch_jcc_at(st5, jne_pos, ((long)st5.text.Count)))))(st_append_text(st4, jmp(0)))))(((long)st4.text.Count))))(emit_serial_wait_and_send(st3, 48))))(st_append_text(st2, jcc(cc_ne(), 0)))))(((long)st2.text.Count))))(st_append_text(st1, test_rr(reg_rbx(), reg_rbx())))))(st_append_text(st, mov_rr(reg_rbx(), reg_rax())));

    public static CodegenState emit_itoa_sign_and_digits(CodegenState st) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<long, CodegenState>)((jns_pos) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<long, CodegenState>)((loop_top) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => ((Func<CodegenState, CodegenState>)((st12) => ((Func<CodegenState, CodegenState>)((st13) => ((Func<CodegenState, CodegenState>)((st14) => ((Func<CodegenState, CodegenState>)((st15) => st_append_text(st15, jcc(cc_ne(), (loop_top - (((long)st15.text.Count) + 6))))))(st_append_text(st14, test_rr(reg_rbx(), reg_rbx())))))(st_append_text(st13, add_ri(reg_rcx(), 1)))))(st_append_text(st12, push_r(reg_rdx())))))(st_append_text(st11, add_ri(reg_rdx(), 48)))))(st_append_text(st10, mov_rr(reg_rbx(), reg_rax())))))(st_append_text(st9, idiv_r(reg_r11())))))(st_append_text(st8, cqo()))))(st_append_text(st7, mov_rr(reg_rax(), reg_rbx())))))(((long)st7.text.Count))))(st_append_text(st6, li(reg_r11(), 10)))))(st_append_text(st5, li(reg_rcx(), 0)))))(patch_jcc_at(st4, jns_pos, ((long)st4.text.Count)))))(st_append_text(st3, neg_r(reg_rbx())))))(emit_serial_wait_and_send(st2, 45))))(st_append_text(st1, jcc(cc_ge(), 0)))))(((long)st1.text.Count))))(st_append_text(st, test_rr(reg_rbx(), reg_rbx())));

    public static CodegenState emit_itoa_print_loop(CodegenState st) => ((Func<long, CodegenState>)((loop_top) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<long, CodegenState>)((je_pos) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => patch_jcc_at(st8, je_pos, ((long)st8.text.Count))))(st_append_text(st7, jmp((loop_top - (((long)st7.text.Count) + 5)))))))(st_append_text(st6, sub_ri(reg_rcx(), 1)))))(st_append_text(st5, pop_r(reg_rcx())))))(emit_serial_send_rdi(st4))))(st_append_text(st3, push_r(reg_rcx())))))(st_append_text(st2, pop_r(reg_rdi())))))(st_append_text(st1, jcc(cc_e(), 0)))))(((long)st1.text.Count))))(st_append_text(st, test_rr(reg_rcx(), reg_rcx())))))(((long)st.text.Count));

    public static CodegenState emit_inline_itoa_and_print(CodegenState st) => ((Func<ItoaState, CodegenState>)((zero) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => patch_jmp_at(st2, zero.jmp_done_zero_pos, ((long)st2.text.Count))))(emit_itoa_print_loop(st1))))(emit_itoa_sign_and_digits(zero.cg))))(emit_itoa_zero_check(st));

    public static CodegenState emit_call_to(CodegenState st, string target) => ((Func<long, CodegenState>)((patch_pos) => ((Func<CodegenState, CodegenState>)((st1) => new CodegenState(text: st1.text, rodata: st1.rodata, func_offsets: st1.func_offsets, call_patches: ((Func<List<CallPatch>>)(() => { var _l = st1.call_patches; _l.Add(new CallPatch(patch_offset: patch_pos, target: target)); return _l; }))(), func_addr_fixups: st1.func_addr_fixups, locals: st1.locals, next_temp: st1.next_temp, next_local: st1.next_local, spill_count: st1.spill_count, load_local_toggle: st1.load_local_toggle, tco: st1.tco)))(st_append_text(st, x86_call(0)))))(((long)st.text.Count));

    public static long bare_metal_heap_base() => 4194304;

    public static CodegenState emit_start(CodegenState st) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st3a) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => st_append_text(st7, hlt())))(emit_serial_wait_and_send(st6, 10))))(emit_inline_itoa_and_print(st5))))(emit_call_to(st4, "main"))))(emit_com1_init(st3a))))(st_append_text(st3, li(reg_r10(), bare_metal_heap_base())))))(st_append_text(st2, mov_rr(reg_rbp(), reg_rsp())))))(st_append_text(st1, li(reg_rsp(), bare_metal_stack_top())))))(record_func_offset(st, "__start"));

    public static List<long> x86_64_emit_chapter(IRChapter m) => ((Func<TrampolineResult, List<long>>)((tramp) => ((Func<CodegenState, List<long>>)((st0) => ((Func<CodegenState, List<long>>)((st0a) => ((Func<CodegenState, List<long>>)((st1) => ((Func<CodegenState, List<long>>)((st2) => ((Func<long, List<long>>)((start_offset) => ((Func<long, List<long>>)((start_addr) => ((Func<PatchEntry, List<long>>)((far_jump_entry) => ((Func<List<PatchEntry>, List<long>>)((call_entries) => ((Func<List<PatchEntry>, List<long>>)((addr_entries) => ((Func<List<PatchEntry>, List<long>>)((all_patches) => ((Func<CodegenState, List<long>>)((st3) => build_elf_32_bare(st3.text, st3.rodata, 12)))(st_with_text(st2, apply_all_patches(st2.text, all_patches)))))(Enumerable.Concat(new List<PatchEntry> { far_jump_entry }, Enumerable.Concat(call_entries, addr_entries).ToList()).ToList())))(collect_func_addr_patches(st2.func_addr_fixups, st2.func_offsets, bare_metal_load_addr(), 0, new List<PatchEntry>()))))(collect_call_patches(st2.call_patches, st2.func_offsets, 0, new List<PatchEntry>()))))(make_i32_patch((tramp.far_jump_patch_pos + 1), start_addr))))((bare_metal_load_addr() + start_offset))))(lookup_func_offset(st2.func_offsets, "__start"))))(emit_start(st1))))(emit_all_defs(st0a, m.defs, 0))))(emit_runtime_helpers(st0))))(new CodegenState(text: tramp.bytes, rodata: new List<long>(), func_offsets: new List<FuncOffset>(), call_patches: new List<CallPatch>(), func_addr_fixups: new List<FuncAddrFixup>(), locals: new List<LocalBinding>(), next_temp: 0, next_local: 0, spill_count: 0, load_local_toggle: 0, tco: default_tco_state()))))(bare_metal_trampoline());

    public static long int_mod(long n, long d) => ((Func<long, long>)((r) => ((r < 0) ? (r + d) : r)))((n - ((n / d) * d)));

    public static long floor_div(long n, long d) => ((n >= 0) ? (n / d) : (((n - d) + 1) / d));

    public static long to_byte(long n) => int_mod(n, 256);

    public static long rex(bool w, bool r, bool x, bool b) => ((Func<long, long>)((wv) => ((Func<long, long>)((rv) => ((Func<long, long>)((xv) => ((Func<long, long>)((bv) => ((((64 + wv) + rv) + xv) + bv)))((b ? 1 : 0))))((x ? 2 : 0))))((r ? 4 : 0))))((w ? 8 : 0));

    public static long rex_w(long reg, long rm) => rex(true, (reg >= 8), false, (rm >= 8));

    public static long modrm(long m, long reg, long rm) => (((m * 64) + (int_mod(reg, 8) * 8)) + int_mod(rm, 8));

    public static long sib(long scale, long index, long base_reg) => (((scale * 64) + (int_mod(index, 8) * 8)) + int_mod(base_reg, 8));

    public static List<long> write_bytes(long v, long n) => ((n == 0) ? new List<long>() : Enumerable.Concat(new List<long> { to_byte(v) }, write_bytes(floor_div(v, 256), (n - 1))).ToList());

    public static List<long> write_i8(long v) => new List<long> { to_byte(v) };

    public static List<long> write_i32(long v) => write_bytes(v, 4);

    public static List<long> write_i64(long v) => write_bytes(v, 8);

    public static List<long> emit_mem_operand(long reg, long rm, long offset) => ((Func<long, List<long>>)((rm_low) => ((Func<bool, List<long>>)((needs_sib) => ((Func<bool, List<long>>)((rbp_base) => ((Func<List<long>, List<long>>)((sib_byte) => (((offset == 0) && (rbp_base == false)) ? Enumerable.Concat(new List<long> { modrm(0, reg, rm) }, sib_byte).ToList() : (((offset >= (0 - 128)) && (offset <= 127)) ? Enumerable.Concat(new List<long> { modrm(1, reg, rm) }, Enumerable.Concat(sib_byte, write_i8(offset)).ToList()).ToList() : Enumerable.Concat(new List<long> { modrm(2, reg, rm) }, Enumerable.Concat(sib_byte, write_i32(offset)).ToList()).ToList()))))((needs_sib ? new List<long> { sib(0, 4, rm_low) } : new List<long>()))))((rm_low == 5))))((rm_low == 4))))(int_mod(rm, 8));

    public static long reg_rax() => 0;

    public static long reg_rcx() => 1;

    public static long reg_rdx() => 2;

    public static long reg_rbx() => 3;

    public static long reg_rsp() => 4;

    public static long reg_rbp() => 5;

    public static long reg_rsi() => 6;

    public static long reg_rdi() => 7;

    public static long reg_r8() => 8;

    public static long reg_r9() => 9;

    public static long reg_r10() => 10;

    public static long reg_r11() => 11;

    public static long reg_r12() => 12;

    public static long reg_r13() => 13;

    public static long reg_r14() => 14;

    public static long reg_r15() => 15;

    public static List<long> arg_regs() => new List<long> { 7, 6, 2, 1, 8, 9 };

    public static List<long> callee_saved_regs() => new List<long> { 3, 12, 13, 14, 15 };

    public static long cc_ae() => 3;

    public static long cc_e() => 4;

    public static long cc_ne() => 5;

    public static long cc_be() => 6;

    public static long cc_a() => 7;

    public static long cc_l() => 12;

    public static long cc_ge() => 13;

    public static long cc_le() => 14;

    public static long cc_g() => 15;

    public static List<long> mov_rr(long rd, long rs) => new List<long> { rex_w(rs, rd), 137, modrm(3, rs, rd) };

    public static List<long> mov_ri64(long rd, long imm) => Enumerable.Concat(new List<long> { rex(true, false, false, (rd >= 8)), (184 + int_mod(rd, 8)) }, write_i64(imm)).ToList();

    public static List<long> mov_ri32(long rd, long imm) => Enumerable.Concat(new List<long> { rex_w(0, rd), 199, modrm(3, 0, rd) }, write_i32(imm)).ToList();

    public static List<long> mov_load(long rd, long rs, long offset) => Enumerable.Concat(new List<long> { rex_w(rd, rs), 139 }, emit_mem_operand(rd, rs, offset)).ToList();

    public static List<long> mov_store(long rd, long rs, long offset) => Enumerable.Concat(new List<long> { rex_w(rs, rd), 137 }, emit_mem_operand(rs, rd, offset)).ToList();

    public static List<long> mov_load_rip_rel(long rd, long disp32) => Enumerable.Concat(new List<long> { rex(true, (rd >= 8), false, false), 139, modrm(0, rd, 5) }, write_i32(disp32)).ToList();

    public static List<long> mov_store_rip_rel(long rs, long disp32) => Enumerable.Concat(new List<long> { rex(true, (rs >= 8), false, false), 137, modrm(0, rs, 5) }, write_i32(disp32)).ToList();

    public static List<long> movzx_byte(long rd, long rs, long offset) => Enumerable.Concat(new List<long> { rex_w(rd, rs), 15, 182 }, emit_mem_operand(rd, rs, offset)).ToList();

    public static List<long> mov_store_byte(long rd, long rs, long offset) => ((Func<long, List<long>>)((rex_byte) => ((Func<List<long>, List<long>>)((pfx) => Enumerable.Concat(pfx, Enumerable.Concat(new List<long> { 136 }, emit_mem_operand(rs, rd, offset)).ToList()).ToList()))((((rex_byte != 64) || (rs >= 4)) ? new List<long> { rex_byte } : new List<long>()))))(rex(false, (rs >= 8), false, (rd >= 8)));

    public static List<long> alu_ri(long ext, long rd, long imm) => (((imm >= (0 - 128)) && (imm <= 127)) ? Enumerable.Concat(new List<long> { rex_w(0, rd), 131, modrm(3, ext, rd) }, write_i8(imm)).ToList() : Enumerable.Concat(new List<long> { rex_w(0, rd), 129, modrm(3, ext, rd) }, write_i32(imm)).ToList());

    public static List<long> add_rr(long rd, long rs) => new List<long> { rex_w(rs, rd), 1, modrm(3, rs, rd) };

    public static List<long> add_ri(long rd, long imm) => alu_ri(0, rd, imm);

    public static List<long> sub_rr(long rd, long rs) => new List<long> { rex_w(rs, rd), 41, modrm(3, rs, rd) };

    public static List<long> sub_ri(long rd, long imm) => alu_ri(5, rd, imm);

    public static List<long> imul_rr(long rd, long rs) => new List<long> { rex_w(rd, rs), 15, 175, modrm(3, rd, rs) };

    public static List<long> neg_r(long rd) => new List<long> { rex_w(0, rd), 247, modrm(3, 3, rd) };

    public static List<long> cqo() => new List<long> { rex(true, false, false, false), 153 };

    public static List<long> idiv_r(long rs) => new List<long> { rex_w(0, rs), 247, modrm(3, 7, rs) };

    public static List<long> and_rr(long rd, long rs) => new List<long> { rex_w(rs, rd), 33, modrm(3, rs, rd) };

    public static List<long> and_ri(long rd, long imm) => alu_ri(4, rd, imm);

    public static List<long> shl_ri(long rd, long imm) => new List<long> { rex_w(0, rd), 193, modrm(3, 4, rd), imm };

    public static List<long> shr_ri(long rd, long imm) => new List<long> { rex_w(0, rd), 193, modrm(3, 5, rd), imm };

    public static List<long> sar_ri(long rd, long imm) => new List<long> { rex_w(0, rd), 193, modrm(3, 7, rd), imm };

    public static List<long> cmp_rr(long rd, long rs) => new List<long> { rex_w(rs, rd), 57, modrm(3, rs, rd) };

    public static List<long> cmp_ri(long rd, long imm) => alu_ri(7, rd, imm);

    public static List<long> test_rr(long rd, long rs) => new List<long> { rex_w(rs, rd), 133, modrm(3, rs, rd) };

    public static List<long> setcc(long cc, long rd) => ((Func<List<long>, List<long>>)((pfx) => Enumerable.Concat(pfx, new List<long> { 15, (144 + cc), modrm(3, 0, rd) }).ToList()))(((rd >= 4) ? new List<long> { rex(false, false, false, (rd >= 8)) } : new List<long>()));

    public static List<long> movzx_byte_self(long rd) => new List<long> { rex_w(rd, rd), 15, 182, modrm(3, rd, rd) };

    public static List<long> jcc(long cc, long rel32) => Enumerable.Concat(new List<long> { 15, (128 + cc) }, write_i32(rel32)).ToList();

    public static List<long> jmp(long rel32) => Enumerable.Concat(new List<long> { 233 }, write_i32(rel32)).ToList();

    public static List<long> x86_call(long rel32) => Enumerable.Concat(new List<long> { 232 }, write_i32(rel32)).ToList();

    public static List<long> x86_ret() => new List<long> { 195 };

    public static List<long> x86_nop() => new List<long> { 144 };

    public static List<long> push_r(long rd) => ((Func<List<long>, List<long>>)((pfx) => Enumerable.Concat(pfx, new List<long> { (80 + int_mod(rd, 8)) }).ToList()))(((rd >= 8) ? new List<long> { rex(false, false, false, true) } : new List<long>()));

    public static List<long> pop_r(long rd) => ((Func<List<long>, List<long>>)((pfx) => Enumerable.Concat(pfx, new List<long> { (88 + int_mod(rd, 8)) }).ToList()))(((rd >= 8) ? new List<long> { rex(false, false, false, true) } : new List<long>()));

    public static List<long> lea(long rd, long rs, long offset) => Enumerable.Concat(new List<long> { rex_w(rd, rs), 141 }, emit_mem_operand(rd, rs, offset)).ToList();

    public static List<long> li(long rd, long value) => ((value == 0) ? xor_rr(rd, rd) : (((value >= (0 - 2147483648)) && (value <= 2147483647)) ? mov_ri32(rd, value) : mov_ri64(rd, value)));

    public static List<long> xor_rr(long rd, long rs) => ((Func<long, List<long>>)((rex_byte) => ((Func<List<long>, List<long>>)((pfx) => Enumerable.Concat(pfx, new List<long> { 49, modrm(3, rs, rd) }).ToList()))(((rex_byte != 64) ? new List<long> { rex_byte } : new List<long>()))))(rex(false, (rs >= 8), false, (rd >= 8)));

    public static List<long> x86_syscall() => new List<long> { 15, 5 };

    public static List<long> out_dx_al() => new List<long> { 238 };

    public static List<long> in_al_dx() => new List<long> { 236 };

    public static List<long> hlt() => new List<long> { 244 };

    public static List<long> x86_pause() => new List<long> { 243, 144 };

    public static List<long> cli() => new List<long> { 250 };

    public static List<long> sti() => new List<long> { 251 };

    public static List<long> iretq() => new List<long> { 72, 207 };

    public static List<long> lidt_rdi() => new List<long> { 15, 1, 31 };

    public static List<long> swapgs() => new List<long> { 15, 1, 248 };

    public static StrEqHeadResult emit_str_eq_head(CodegenState st) => ((Func<CodegenState, StrEqHeadResult>)((st0) => ((Func<CodegenState, StrEqHeadResult>)((st1) => ((Func<long, StrEqHeadResult>)((not_same_pos) => ((Func<CodegenState, StrEqHeadResult>)((st2) => ((Func<CodegenState, StrEqHeadResult>)((st3) => ((Func<CodegenState, StrEqHeadResult>)((st4) => ((Func<CodegenState, StrEqHeadResult>)((st5) => ((Func<CodegenState, StrEqHeadResult>)((st6) => ((Func<CodegenState, StrEqHeadResult>)((st7) => ((Func<CodegenState, StrEqHeadResult>)((st8) => ((Func<long, StrEqHeadResult>)((len_ne_pos) => ((Func<CodegenState, StrEqHeadResult>)((st9) => new StrEqHeadResult(cg: st9, len_ne_pos: len_ne_pos)))(st_append_text(st8, jcc(cc_ne(), 0)))))(((long)st8.text.Count))))(st_append_text(st7, cmp_rr(reg_rcx(), reg_rdx())))))(st_append_text(st6, mov_load(reg_rdx(), reg_rsi(), 0)))))(st_append_text(st5, mov_load(reg_rcx(), reg_rdi(), 0)))))(patch_jcc_at(st4, not_same_pos, ((long)st4.text.Count)))))(st_append_text(st3, x86_ret()))))(st_append_text(st2, li(reg_rax(), 1)))))(st_append_text(st1, jcc(cc_ne(), 0)))))(((long)st1.text.Count))))(st_append_text(st0, cmp_rr(reg_rdi(), reg_rsi())))))(record_func_offset(st, "__str_eq"));

    public static StrEqLoopResult emit_str_eq_byte_loop(CodegenState st) => ((Func<CodegenState, StrEqLoopResult>)((st0) => ((Func<long, StrEqLoopResult>)((loop_pos) => ((Func<CodegenState, StrEqLoopResult>)((st1) => ((Func<long, StrEqLoopResult>)((loop_done_pos) => ((Func<CodegenState, StrEqLoopResult>)((st2) => ((Func<CodegenState, StrEqLoopResult>)((st3) => ((Func<CodegenState, StrEqLoopResult>)((st4) => ((Func<CodegenState, StrEqLoopResult>)((st5) => ((Func<CodegenState, StrEqLoopResult>)((st6) => ((Func<CodegenState, StrEqLoopResult>)((st7) => ((Func<CodegenState, StrEqLoopResult>)((st8) => ((Func<CodegenState, StrEqLoopResult>)((st9) => ((Func<long, StrEqLoopResult>)((byte_ne_pos) => ((Func<CodegenState, StrEqLoopResult>)((st10) => ((Func<CodegenState, StrEqLoopResult>)((st11) => ((Func<CodegenState, StrEqLoopResult>)((st12) => new StrEqLoopResult(cg: st12, loop_done_pos: loop_done_pos, byte_ne_pos: byte_ne_pos)))(st_append_text(st11, jmp((loop_pos - (((long)st11.text.Count) + 5)))))))(st_append_text(st10, add_ri(reg_r11(), 1)))))(st_append_text(st9, jcc(cc_ne(), 0)))))(((long)st9.text.Count))))(st_append_text(st8, cmp_rr(reg_rax(), reg_rdx())))))(st_append_text(st7, movzx_byte(reg_rdx(), reg_rdx(), 8)))))(st_append_text(st6, add_rr(reg_rdx(), reg_r11())))))(st_append_text(st5, mov_rr(reg_rdx(), reg_rsi())))))(st_append_text(st4, movzx_byte(reg_rax(), reg_rax(), 8)))))(st_append_text(st3, add_rr(reg_rax(), reg_r11())))))(st_append_text(st2, mov_rr(reg_rax(), reg_rdi())))))(st_append_text(st1, jcc(cc_ge(), 0)))))(((long)st1.text.Count))))(st_append_text(st0, cmp_rr(reg_r11(), reg_rcx())))))(((long)st0.text.Count))))(st_append_text(st, li(reg_r11(), 0)));

    public static CodegenState emit_str_eq(CodegenState st) => ((Func<StrEqHeadResult, CodegenState>)((head) => ((Func<StrEqLoopResult, CodegenState>)((lp) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => st_append_text(st6, x86_ret())))(st_append_text(st5, li(reg_rax(), 0)))))(patch_jcc_at(st4, lp.byte_ne_pos, ((long)st4.text.Count)))))(patch_jcc_at(st3, head.len_ne_pos, ((long)st3.text.Count)))))(st_append_text(st2, x86_ret()))))(st_append_text(st1, li(reg_rax(), 1)))))(patch_jcc_at(lp.cg, lp.loop_done_pos, ((long)lp.cg.text.Count)))))(emit_str_eq_byte_loop(head.cg))))(emit_str_eq_head(st));

    public static CodegenState emit_itoa_setup(CodegenState st) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<long, CodegenState>)((not_neg_pos) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => patch_jcc_at(st9, not_neg_pos, ((long)st9.text.Count))))(st_append_text(st8, li(reg_r12(), 1)))))(st_append_text(st7, neg_r(reg_rbx())))))(st_append_text(st6, jcc(cc_ge(), 0)))))(((long)st6.text.Count))))(st_append_text(st5, cmp_ri(reg_rbx(), 0)))))(st_append_text(st4, li(reg_r12(), 0)))))(st_append_text(st3, mov_rr(reg_rbx(), reg_rdi())))))(st_append_text(st2, sub_ri(reg_rsp(), 32)))))(st_append_text(st1, push_r(reg_r12())))))(st_append_text(st0, push_r(reg_rbx())))))(record_func_offset(st, "__itoa"));

    public static ItoaZeroResult emit_itoa_zero_guard(CodegenState st) => ((Func<CodegenState, ItoaZeroResult>)((st0) => ((Func<CodegenState, ItoaZeroResult>)((st1) => ((Func<CodegenState, ItoaZeroResult>)((st2) => ((Func<long, ItoaZeroResult>)((not_zero_pos) => ((Func<CodegenState, ItoaZeroResult>)((st3) => ((Func<CodegenState, ItoaZeroResult>)((st4) => ((Func<CodegenState, ItoaZeroResult>)((st5) => ((Func<CodegenState, ItoaZeroResult>)((st6) => ((Func<long, ItoaZeroResult>)((skip_digits_pos) => ((Func<CodegenState, ItoaZeroResult>)((st7) => ((Func<CodegenState, ItoaZeroResult>)((st8) => new ItoaZeroResult(cg: st8, skip_digits_pos: skip_digits_pos)))(patch_jcc_at(st7, not_zero_pos, ((long)st7.text.Count)))))(st_append_text(st6, jmp(0)))))(((long)st6.text.Count))))(st_append_text(st5, li(reg_rcx(), 1)))))(st_append_text(st4, mov_store_byte(reg_rsp(), reg_rsi(), 0)))))(st_append_text(st3, li(reg_rsi(), 3)))))(st_append_text(st2, jcc(cc_ne(), 0)))))(((long)st2.text.Count))))(st_append_text(st1, test_rr(reg_rbx(), reg_rbx())))))(st_append_text(st0, li(reg_r11(), 10)))))(st_append_text(st, li(reg_rcx(), 0)));

    public static CodegenState emit_itoa_digit_loop(CodegenState st) => ((Func<long, CodegenState>)((digit_loop) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<long, CodegenState>)((digit_done_pos) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => patch_jcc_at(st11, digit_done_pos, ((long)st11.text.Count))))(st_append_text(st10, jmp((digit_loop - (((long)st10.text.Count) + 5)))))))(st_append_text(st9, add_ri(reg_rcx(), 1)))))(st_append_text(st8, mov_store_byte(reg_rsi(), reg_rdx(), 0)))))(st_append_text(st7, add_rr(reg_rsi(), reg_rcx())))))(st_append_text(st6, mov_rr(reg_rsi(), reg_rsp())))))(st_append_text(st5, add_ri(reg_rdx(), 3)))))(st_append_text(st4, mov_rr(reg_rbx(), reg_rax())))))(st_append_text(st3, idiv_r(reg_r11())))))(st_append_text(st2, cqo()))))(st_append_text(st1, mov_rr(reg_rax(), reg_rbx())))))(st_append_text(st0, jcc(cc_e(), 0)))))(((long)st0.text.Count))))(st_append_text(st, test_rr(reg_rbx(), reg_rbx())))))(((long)st.text.Count));

    public static CodegenState emit_itoa_heap_alloc(CodegenState st) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<long, CodegenState>)((no_minus_pos) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => ((Func<CodegenState, CodegenState>)((st12) => ((Func<CodegenState, CodegenState>)((st13) => patch_jcc_at(st13, no_minus_pos, ((long)st13.text.Count))))(st_append_text(st12, li(reg_r11(), 1)))))(st_append_text(st11, mov_store_byte(reg_rax(), reg_rsi(), 8)))))(st_append_text(st10, li(reg_rsi(), 73)))))(st_append_text(st9, jcc(cc_e(), 0)))))(((long)st9.text.Count))))(st_append_text(st8, test_rr(reg_r12(), reg_r12())))))(st_append_text(st7, li(reg_r11(), 0)))))(st_append_text(st6, mov_store(reg_rax(), reg_rdx(), 0)))))(st_append_text(st5, add_rr(reg_r10(), reg_rsi())))))(st_append_text(st4, and_ri(reg_rsi(), (0 - 8))))))(st_append_text(st3, add_ri(reg_rsi(), 15)))))(st_append_text(st2, mov_rr(reg_rsi(), reg_rdx())))))(st_append_text(st1, mov_rr(reg_rax(), reg_r10())))))(st_append_text(st0, add_rr(reg_rdx(), reg_r12())))))(st_append_text(st, mov_rr(reg_rdx(), reg_rcx())));

    public static CodegenState emit_itoa_copy_and_epilogue(CodegenState st) => ((Func<long, CodegenState>)((copy_loop) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<long, CodegenState>)((copy_done_pos) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => ((Func<CodegenState, CodegenState>)((st12) => ((Func<CodegenState, CodegenState>)((st13) => ((Func<CodegenState, CodegenState>)((st14) => st_append_text(st14, x86_ret())))(st_append_text(st13, pop_r(reg_rbx())))))(st_append_text(st12, pop_r(reg_r12())))))(st_append_text(st11, add_ri(reg_rsp(), 32)))))(patch_jcc_at(st10, copy_done_pos, ((long)st10.text.Count)))))(st_append_text(st9, jmp((copy_loop - (((long)st9.text.Count) + 5)))))))(st_append_text(st8, add_ri(reg_r11(), 1)))))(st_append_text(st7, mov_store_byte(reg_rdx(), reg_rsi(), 8)))))(st_append_text(st6, add_rr(reg_rdx(), reg_r11())))))(st_append_text(st5, mov_rr(reg_rdx(), reg_rax())))))(st_append_text(st4, movzx_byte(reg_rsi(), reg_rsi(), 0)))))(st_append_text(st3, add_rr(reg_rsi(), reg_rcx())))))(st_append_text(st2, mov_rr(reg_rsi(), reg_rsp())))))(st_append_text(st1, sub_ri(reg_rcx(), 1)))))(st_append_text(st0, jcc(cc_e(), 0)))))(((long)st0.text.Count))))(st_append_text(st, test_rr(reg_rcx(), reg_rcx())))))(((long)st.text.Count));

    public static CodegenState emit_itoa(CodegenState st) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<ItoaZeroResult, CodegenState>)((zero) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => emit_itoa_copy_and_epilogue(st3)))(emit_itoa_heap_alloc(st2))))(patch_jmp_at(st1, zero.skip_digits_pos, ((long)st1.text.Count)))))(emit_itoa_digit_loop(zero.cg))))(emit_itoa_zero_guard(st0))))(emit_itoa_setup(st));

    public static StrConcatCheckResult emit_str_concat_prologue(CodegenState st) => ((Func<CodegenState, StrConcatCheckResult>)((st0) => ((Func<CodegenState, StrConcatCheckResult>)((st1) => ((Func<CodegenState, StrConcatCheckResult>)((st2) => ((Func<CodegenState, StrConcatCheckResult>)((st3) => ((Func<CodegenState, StrConcatCheckResult>)((st4) => ((Func<CodegenState, StrConcatCheckResult>)((st5) => ((Func<CodegenState, StrConcatCheckResult>)((st6) => ((Func<CodegenState, StrConcatCheckResult>)((st7) => ((Func<CodegenState, StrConcatCheckResult>)((st8) => ((Func<CodegenState, StrConcatCheckResult>)((st9) => ((Func<CodegenState, StrConcatCheckResult>)((st10) => ((Func<CodegenState, StrConcatCheckResult>)((st11) => ((Func<CodegenState, StrConcatCheckResult>)((st12) => ((Func<CodegenState, StrConcatCheckResult>)((st13) => ((Func<long, StrConcatCheckResult>)((slow_path_pos) => ((Func<CodegenState, StrConcatCheckResult>)((st14) => new StrConcatCheckResult(cg: st14, slow_path_pos: slow_path_pos)))(st_append_text(st13, jcc(cc_ne(), 0)))))(((long)st13.text.Count))))(st_append_text(st12, cmp_rr(reg_r13(), reg_r10())))))(st_append_text(st11, add_rr(reg_r13(), reg_r11())))))(st_append_text(st10, mov_rr(reg_r13(), reg_rbx())))))(st_append_text(st9, and_ri(reg_r11(), (0 - 8))))))(st_append_text(st8, add_ri(reg_r11(), 15)))))(st_append_text(st7, mov_rr(reg_r11(), reg_rcx())))))(st_append_text(st6, mov_load(reg_rdx(), reg_r12(), 0)))))(st_append_text(st5, mov_load(reg_rcx(), reg_rbx(), 0)))))(st_append_text(st4, mov_rr(reg_r12(), reg_rsi())))))(st_append_text(st3, mov_rr(reg_rbx(), reg_rdi())))))(st_append_text(st2, push_r(reg_r13())))))(st_append_text(st1, push_r(reg_r12())))))(st_append_text(st0, push_r(reg_rbx())))))(record_func_offset(st, "__str_concat"));

    public static CodegenState emit_str_concat_fast_copy(CodegenState st) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<long, CodegenState>)((fast_loop) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<long, CodegenState>)((fast_exit_pos) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => ((Func<CodegenState, CodegenState>)((st12) => ((Func<CodegenState, CodegenState>)((st13) => ((Func<CodegenState, CodegenState>)((st14) => patch_jcc_at(st14, fast_exit_pos, ((long)st14.text.Count))))(st_append_text(st13, jmp((fast_loop - (((long)st13.text.Count) + 5)))))))(st_append_text(st12, add_ri(reg_r11(), 1)))))(st_append_text(st11, mov_store_byte(reg_rdi(), reg_rsi(), 8)))))(st_append_text(st10, add_rr(reg_rdi(), reg_r11())))))(st_append_text(st9, add_rr(reg_rdi(), reg_rcx())))))(st_append_text(st8, mov_rr(reg_rdi(), reg_rbx())))))(st_append_text(st7, movzx_byte(reg_rsi(), reg_rsi(), 8)))))(st_append_text(st6, add_rr(reg_rsi(), reg_r11())))))(st_append_text(st5, mov_rr(reg_rsi(), reg_r12())))))(st_append_text(st4, jcc(cc_ge(), 0)))))(((long)st4.text.Count))))(st_append_text(st3, cmp_rr(reg_r11(), reg_rdx())))))(((long)st3.text.Count))))(st_append_text(st2, li(reg_r11(), 0)))))(st_append_text(st1, mov_store(reg_rbx(), reg_r13(), 0)))))(st_append_text(st0, add_rr(reg_r13(), reg_rdx())))))(st_append_text(st, mov_rr(reg_r13(), reg_rcx())));

    public static StrConcatFastResult emit_str_concat_fast_bump(CodegenState st) => ((Func<CodegenState, StrConcatFastResult>)((st0) => ((Func<CodegenState, StrConcatFastResult>)((st1) => ((Func<CodegenState, StrConcatFastResult>)((st2) => ((Func<CodegenState, StrConcatFastResult>)((st3) => ((Func<CodegenState, StrConcatFastResult>)((st4) => ((Func<CodegenState, StrConcatFastResult>)((st5) => ((Func<long, StrConcatFastResult>)((fast_done_pos) => ((Func<CodegenState, StrConcatFastResult>)((st6) => new StrConcatFastResult(cg: st6, fast_done_pos: fast_done_pos)))(st_append_text(st5, jmp(0)))))(((long)st5.text.Count))))(st_append_text(st4, mov_rr(reg_rax(), reg_rbx())))))(st_append_text(st3, add_rr(reg_r10(), reg_r11())))))(st_append_text(st2, lea(reg_r10(), reg_rbx(), 0)))))(st_append_text(st1, and_ri(reg_r11(), (0 - 8))))))(st_append_text(st0, add_ri(reg_r11(), 15)))))(st_append_text(st, mov_rr(reg_r11(), reg_r13())));

    public static CodegenState emit_str_concat_slow_alloc(CodegenState st, long slow_path_pos) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => st_append_text(st7, mov_store(reg_rax(), reg_r13(), 0))))(st_append_text(st6, add_rr(reg_r10(), reg_r11())))))(st_append_text(st5, and_ri(reg_r11(), (0 - 8))))))(st_append_text(st4, add_ri(reg_r11(), 15)))))(st_append_text(st3, mov_rr(reg_r11(), reg_r13())))))(st_append_text(st2, mov_rr(reg_rax(), reg_r10())))))(st_append_text(st1, add_rr(reg_r13(), reg_rdx())))))(st_append_text(st0, mov_rr(reg_r13(), reg_rcx())))))(patch_jcc_at(st, slow_path_pos, ((long)st.text.Count)));

    public static CodegenState emit_str_concat_slow_copy1(CodegenState st) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<long, CodegenState>)((loop1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<long, CodegenState>)((exit1_pos) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => patch_jcc_at(st11, exit1_pos, ((long)st11.text.Count))))(st_append_text(st10, jmp((loop1 - (((long)st10.text.Count) + 5)))))))(st_append_text(st9, add_ri(reg_r11(), 1)))))(st_append_text(st8, mov_store_byte(reg_rsi(), reg_rdx(), 8)))))(st_append_text(st7, add_rr(reg_rsi(), reg_r11())))))(st_append_text(st6, mov_rr(reg_rsi(), reg_rax())))))(st_append_text(st5, movzx_byte(reg_rdx(), reg_rdx(), 8)))))(st_append_text(st4, add_rr(reg_rdx(), reg_r11())))))(st_append_text(st3, mov_rr(reg_rdx(), reg_rbx())))))(st_append_text(st2, jcc(cc_ge(), 0)))))(((long)st2.text.Count))))(st_append_text(st1, cmp_rr(reg_r11(), reg_rcx())))))(((long)st1.text.Count))))(st_append_text(st0, li(reg_r11(), 0)))))(st_append_text(st, mov_load(reg_rcx(), reg_rbx(), 0)));

    public static CodegenState emit_str_concat_slow_copy2(CodegenState st, long fast_done_pos) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<long, CodegenState>)((loop2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<long, CodegenState>)((exit2_pos) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => ((Func<CodegenState, CodegenState>)((st12) => ((Func<CodegenState, CodegenState>)((st13) => ((Func<CodegenState, CodegenState>)((st14) => patch_jmp_at(st14, fast_done_pos, ((long)st14.text.Count))))(patch_jcc_at(st13, exit2_pos, ((long)st13.text.Count)))))(st_append_text(st12, jmp((loop2 - (((long)st12.text.Count) + 5)))))))(st_append_text(st11, add_ri(reg_r11(), 1)))))(st_append_text(st10, mov_store_byte(reg_rdi(), reg_rsi(), 8)))))(st_append_text(st9, add_rr(reg_rdi(), reg_r11())))))(st_append_text(st8, add_rr(reg_rdi(), reg_rcx())))))(st_append_text(st7, mov_rr(reg_rdi(), reg_rax())))))(st_append_text(st6, movzx_byte(reg_rsi(), reg_rsi(), 8)))))(st_append_text(st5, add_rr(reg_rsi(), reg_r11())))))(st_append_text(st4, mov_rr(reg_rsi(), reg_r12())))))(st_append_text(st3, jcc(cc_ge(), 0)))))(((long)st3.text.Count))))(st_append_text(st2, cmp_rr(reg_r11(), reg_rdx())))))(((long)st2.text.Count))))(st_append_text(st1, li(reg_r11(), 0)))))(st_append_text(st0, mov_load(reg_rdx(), reg_r12(), 0)))))(st_append_text(st, mov_load(reg_rcx(), reg_rbx(), 0)));

    public static CodegenState emit_str_concat(CodegenState st) => ((Func<StrConcatCheckResult, CodegenState>)((prologue) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<StrConcatFastResult, CodegenState>)((fast) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => st_append_text(st6, x86_ret())))(st_append_text(st5, pop_r(reg_rbx())))))(st_append_text(st4, pop_r(reg_r12())))))(st_append_text(st3, pop_r(reg_r13())))))(emit_str_concat_slow_copy2(st2, fast.fast_done_pos))))(emit_str_concat_slow_copy1(st1))))(emit_str_concat_slow_alloc(fast.cg, prologue.slow_path_pos))))(emit_str_concat_fast_bump(st0))))(emit_str_concat_fast_copy(prologue.cg))))(emit_str_concat_prologue(st));

    public static HelpResult2 emit_ipow_setup(CodegenState st) => ((Func<CodegenState, HelpResult2>)((st0) => ((Func<CodegenState, HelpResult2>)((st1) => ((Func<CodegenState, HelpResult2>)((st2) => ((Func<long, HelpResult2>)((neg_pos) => ((Func<CodegenState, HelpResult2>)((st3) => ((Func<long, HelpResult2>)((zero_pos) => ((Func<CodegenState, HelpResult2>)((st4) => new HelpResult2(cg: st4, p1: neg_pos, p2: zero_pos)))(st_append_text(st3, jcc(cc_e(), 0)))))(((long)st3.text.Count))))(st_append_text(st2, jcc(cc_l(), 0)))))(((long)st2.text.Count))))(st_append_text(st1, cmp_ri(reg_rsi(), 0)))))(st_append_text(st0, li(reg_rax(), 1)))))(record_func_offset(st, "__ipow"));

    public static CodegenState emit_ipow_loop(CodegenState st) => ((Func<long, CodegenState>)((loop_top) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<long, CodegenState>)((skip_mul_pos) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<long, CodegenState>)((jmp_loop_pos) => ((Func<CodegenState, CodegenState>)((st8) => patch_jcc_at(st8, jmp_loop_pos, loop_top)))(st_append_text(st7, jcc(cc_g(), 0)))))(((long)st7.text.Count))))(st_append_text(st6, cmp_ri(reg_rsi(), 0)))))(st_append_text(st5, shr_ri(reg_rsi(), 1)))))(st_append_text(st4, imul_rr(reg_rdi(), reg_rdi())))))(patch_jcc_at(st3, skip_mul_pos, ((long)st3.text.Count)))))(st_append_text(st2, imul_rr(reg_rax(), reg_rdi())))))(st_append_text(st1, jcc(cc_e(), 0)))))(((long)st1.text.Count))))(st_append_text(st0, and_ri(reg_rcx(), 1)))))(st_append_text(st, mov_rr(reg_rcx(), reg_rsi())))))(((long)st.text.Count));

    public static CodegenState emit_ipow(CodegenState st) => ((Func<HelpResult2, CodegenState>)((setup) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => st_append_text(st4, x86_ret())))(st_append_text(st3, li(reg_rax(), 0)))))(patch_jcc_at(st2, setup.p1, ((long)st2.text.Count)))))(st_append_text(st1, x86_ret()))))(patch_jcc_at(st0, setup.p2, ((long)st0.text.Count)))))(emit_ipow_loop(setup.cg))))(emit_ipow_setup(st));

    public static HelpResult1 emit_text_to_int_setup(CodegenState st) => ((Func<CodegenState, HelpResult1>)((st0) => ((Func<CodegenState, HelpResult1>)((st1) => ((Func<CodegenState, HelpResult1>)((st2) => ((Func<CodegenState, HelpResult1>)((st3) => ((Func<CodegenState, HelpResult1>)((st4) => ((Func<CodegenState, HelpResult1>)((st5) => ((Func<CodegenState, HelpResult1>)((st6) => ((Func<long, HelpResult1>)((empty_pos) => ((Func<CodegenState, HelpResult1>)((st7) => ((Func<CodegenState, HelpResult1>)((st8) => ((Func<CodegenState, HelpResult1>)((st9) => ((Func<long, HelpResult1>)((not_minus_pos) => ((Func<CodegenState, HelpResult1>)((st10) => ((Func<CodegenState, HelpResult1>)((st11) => ((Func<CodegenState, HelpResult1>)((st12) => ((Func<CodegenState, HelpResult1>)((st13) => new HelpResult1(cg: st13, p1: empty_pos)))(patch_jcc_at(st12, not_minus_pos, ((long)st12.text.Count)))))(st_append_text(st11, add_ri(reg_r11(), 1)))))(st_append_text(st10, li(reg_rsi(), 1)))))(st_append_text(st9, jcc(cc_ne(), 0)))))(((long)st9.text.Count))))(st_append_text(st8, cmp_ri(reg_rdx(), 73)))))(st_append_text(st7, movzx_byte(reg_rdx(), reg_rdi(), 0)))))(st_append_text(st6, jcc(cc_e(), 0)))))(((long)st6.text.Count))))(st_append_text(st5, test_rr(reg_rcx(), reg_rcx())))))(st_append_text(st4, li(reg_rsi(), 0)))))(st_append_text(st3, li(reg_r11(), 0)))))(st_append_text(st2, li(reg_rax(), 0)))))(st_append_text(st1, lea(reg_rdi(), reg_rdi(), 8)))))(st_append_text(st0, mov_load(reg_rcx(), reg_rdi(), 0)))))(record_func_offset(st, "__text_to_int"));

    public static CodegenState emit_text_to_int_parse(CodegenState st) => ((Func<long, CodegenState>)((parse_loop) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<long, CodegenState>)((parse_done_pos) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => ((Func<CodegenState, CodegenState>)((st12) => ((Func<CodegenState, CodegenState>)((st13) => ((Func<CodegenState, CodegenState>)((st14) => patch_jcc_at(st14, parse_done_pos, ((long)st14.text.Count))))(st_append_text(st13, jmp((parse_loop - (((long)st13.text.Count) + 5)))))))(st_append_text(st12, add_ri(reg_r11(), 1)))))(st_append_text(st11, add_rr(reg_rax(), reg_rdx())))))(st_append_text(st10, pop_r(reg_rdx())))))(st_append_text(st9, add_rr(reg_rax(), reg_rdx())))))(st_append_text(st8, add_rr(reg_rax(), reg_rdx())))))(st_append_text(st7, shl_ri(reg_rax(), 3)))))(st_append_text(st6, mov_rr(reg_rdx(), reg_rax())))))(st_append_text(st5, push_r(reg_rdx())))))(st_append_text(st4, sub_ri(reg_rdx(), 3)))))(st_append_text(st3, movzx_byte(reg_rdx(), reg_rdx(), 0)))))(st_append_text(st2, add_rr(reg_rdx(), reg_r11())))))(st_append_text(st1, mov_rr(reg_rdx(), reg_rdi())))))(st_append_text(st0, jcc(cc_ge(), 0)))))(((long)st0.text.Count))))(st_append_text(st, cmp_rr(reg_r11(), reg_rcx())))))(((long)st.text.Count));

    public static CodegenState emit_text_to_int(CodegenState st) => ((Func<HelpResult1, CodegenState>)((setup) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<long, CodegenState>)((no_neg_pos) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => st_append_text(st5, x86_ret())))(patch_jcc_at(st4, setup.p1, ((long)st4.text.Count)))))(patch_jcc_at(st3, no_neg_pos, ((long)st3.text.Count)))))(st_append_text(st2, neg_r(reg_rax())))))(st_append_text(st1, jcc(cc_e(), 0)))))(((long)st1.text.Count))))(st_append_text(st0, test_rr(reg_rsi(), reg_rsi())))))(emit_text_to_int_parse(setup.cg))))(emit_text_to_int_setup(st));

    public static HelpResult1 emit_text_starts_with_head(CodegenState st) => ((Func<CodegenState, HelpResult1>)((st0) => ((Func<CodegenState, HelpResult1>)((st1) => ((Func<CodegenState, HelpResult1>)((st2) => ((Func<CodegenState, HelpResult1>)((st3) => ((Func<long, HelpResult1>)((too_long_pos) => ((Func<CodegenState, HelpResult1>)((st4) => new HelpResult1(cg: st4, p1: too_long_pos)))(st_append_text(st3, jcc(cc_g(), 0)))))(((long)st3.text.Count))))(st_append_text(st2, cmp_rr(reg_rdx(), reg_rcx())))))(st_append_text(st1, mov_load(reg_rdx(), reg_rsi(), 0)))))(st_append_text(st0, mov_load(reg_rcx(), reg_rdi(), 0)))))(record_func_offset(st, "__text_starts_with"));

    public static HelpResult1 emit_text_starts_with_loop(CodegenState st) => ((Func<CodegenState, HelpResult1>)((st0) => ((Func<long, HelpResult1>)((loop_top) => ((Func<CodegenState, HelpResult1>)((st1) => ((Func<long, HelpResult1>)((matched_pos) => ((Func<CodegenState, HelpResult1>)((st2) => ((Func<CodegenState, HelpResult1>)((st3) => ((Func<CodegenState, HelpResult1>)((st4) => ((Func<CodegenState, HelpResult1>)((st5) => ((Func<CodegenState, HelpResult1>)((st6) => ((Func<CodegenState, HelpResult1>)((st7) => ((Func<CodegenState, HelpResult1>)((st8) => ((Func<CodegenState, HelpResult1>)((st9) => ((Func<long, HelpResult1>)((mismatch_pos) => ((Func<CodegenState, HelpResult1>)((st10) => ((Func<CodegenState, HelpResult1>)((st11) => ((Func<CodegenState, HelpResult1>)((st12) => ((Func<CodegenState, HelpResult1>)((st13) => ((Func<CodegenState, HelpResult1>)((st14) => ((Func<CodegenState, HelpResult1>)((st15) => new HelpResult1(cg: st15, p1: mismatch_pos)))(st_append_text(st14, x86_ret()))))(st_append_text(st13, li(reg_rax(), 1)))))(patch_jcc_at(st12, matched_pos, ((long)st12.text.Count)))))(st_append_text(st11, jmp((loop_top - (((long)st11.text.Count) + 5)))))))(st_append_text(st10, add_ri(reg_r11(), 1)))))(st_append_text(st9, jcc(cc_ne(), 0)))))(((long)st9.text.Count))))(st_append_text(st8, cmp_rr(reg_rax(), reg_r8())))))(st_append_text(st7, movzx_byte(reg_r8(), reg_r8(), 8)))))(st_append_text(st6, add_rr(reg_r8(), reg_r11())))))(st_append_text(st5, mov_rr(reg_r8(), reg_rsi())))))(st_append_text(st4, movzx_byte(reg_rax(), reg_rax(), 8)))))(st_append_text(st3, add_rr(reg_rax(), reg_r11())))))(st_append_text(st2, mov_rr(reg_rax(), reg_rdi())))))(st_append_text(st1, jcc(cc_ge(), 0)))))(((long)st1.text.Count))))(st_append_text(st0, cmp_rr(reg_r11(), reg_rdx())))))(((long)st0.text.Count))))(st_append_text(st, li(reg_r11(), 0)));

    public static CodegenState emit_text_starts_with(CodegenState st) => ((Func<HelpResult1, CodegenState>)((head) => ((Func<HelpResult1, CodegenState>)((lp) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => st_append_text(st2, x86_ret())))(st_append_text(st1, li(reg_rax(), 0)))))(patch_jcc_at(st0, lp.p1, ((long)st0.text.Count)))))(patch_jcc_at(lp.cg, head.p1, ((long)lp.cg.Count).text))))(emit_text_starts_with_loop(head.cg))))(emit_text_starts_with_head(st));

    public static HelpResult2 emit_text_contains_search(CodegenState st) => ((Func<CodegenState, HelpResult2>)((st0) => ((Func<CodegenState, HelpResult2>)((st1) => ((Func<CodegenState, HelpResult2>)((st2) => ((Func<CodegenState, HelpResult2>)((st3) => ((Func<long, HelpResult2>)((search_loop) => ((Func<CodegenState, HelpResult2>)((st4) => ((Func<CodegenState, HelpResult2>)((st5) => ((Func<CodegenState, HelpResult2>)((st6) => ((Func<CodegenState, HelpResult2>)((st7) => ((Func<long, HelpResult2>)((not_found_pos) => ((Func<CodegenState, HelpResult2>)((st8) => ((Func<CodegenState, HelpResult2>)((st9) => ((Func<CodegenState, HelpResult2>)((st10) => new HelpResult2(cg: st10, p1: not_found_pos, p2: search_loop)))(st_append_text(st9, li(reg_rax(), 0)))))(st_append_text(st8, push_r(reg_r11())))))(st_append_text(st7, jcc(cc_ge(), 0)))))(((long)st7.text.Count))))(st_append_text(st6, cmp_rr(reg_r11(), reg_rax())))))(st_append_text(st5, add_ri(reg_rax(), 1)))))(st_append_text(st4, sub_rr(reg_rax(), reg_rdx())))))(st_append_text(st3, mov_rr(reg_rax(), reg_rcx())))))(((long)st3.text.Count))))(st_append_text(st2, li(reg_r11(), 0)))))(st_append_text(st1, mov_load(reg_rdx(), reg_rsi(), 0)))))(st_append_text(st0, mov_load(reg_rcx(), reg_rdi(), 0)))))(record_func_offset(st, "__text_contains"));

    public static CodegenState emit_text_contains_cmp(CodegenState st, long not_found_pos, long search_loop) => ((Func<long, CodegenState>)((cmp_loop) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<long, CodegenState>)((found_pos) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<long, CodegenState>)((mismatch_pos) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => ((Func<CodegenState, CodegenState>)((st12) => ((Func<CodegenState, CodegenState>)((st13) => ((Func<CodegenState, CodegenState>)((st14) => ((Func<CodegenState, CodegenState>)((st15) => ((Func<CodegenState, CodegenState>)((st16) => ((Func<CodegenState, CodegenState>)((st17) => ((Func<CodegenState, CodegenState>)((st18) => ((Func<CodegenState, CodegenState>)((st19) => ((Func<CodegenState, CodegenState>)((st20) => ((Func<CodegenState, CodegenState>)((st21) => ((Func<CodegenState, CodegenState>)((st22) => st_append_text(st22, x86_ret())))(st_append_text(st21, li(reg_rax(), 0)))))(patch_jcc_at(st20, not_found_pos, ((long)st20.text.Count)))))(st_append_text(st19, jmp((search_loop - (((long)st19.text.Count) + 5)))))))(st_append_text(st18, add_ri(reg_r11(), 1)))))(st_append_text(st17, pop_r(reg_r11())))))(patch_jcc_at(st16, mismatch_pos, ((long)st16.text.Count)))))(st_append_text(st15, x86_ret()))))(st_append_text(st14, li(reg_rax(), 1)))))(st_append_text(st13, pop_r(reg_r11())))))(patch_jcc_at(st12, found_pos, ((long)st12.text.Count)))))(st_append_text(st11, jmp((cmp_loop - (((long)st11.text.Count) + 5)))))))(st_append_text(st10, add_ri(reg_rax(), 1)))))(st_append_text(st9, jcc(cc_ne(), 0)))))(((long)st9.text.Count))))(st_append_text(st8, cmp_rr(reg_r8(), reg_r9())))))(st_append_text(st7, movzx_byte(reg_r9(), reg_r9(), 8)))))(st_append_text(st6, add_rr(reg_r9(), reg_rax())))))(st_append_text(st5, mov_rr(reg_r9(), reg_rsi())))))(st_append_text(st4, movzx_byte(reg_r8(), reg_r8(), 8)))))(st_append_text(st3, add_rr(reg_r8(), reg_rax())))))(st_append_text(st2, add_rr(reg_r8(), reg_r11())))))(st_append_text(st1, mov_rr(reg_r8(), reg_rdi())))))(st_append_text(st0, jcc(cc_ge(), 0)))))(((long)st0.text.Count))))(st_append_text(st, cmp_rr(reg_rax(), reg_rdx())))))(((long)st.text.Count));

    public static CodegenState emit_text_contains(CodegenState st) => ((Func<HelpResult2, CodegenState>)((search) => emit_text_contains_cmp(search.cg, search.p1, search.p2)))(emit_text_contains_search(st));

    public static CodegenState emit_text_compare_prologue(CodegenState st) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<long, CodegenState>)((len1_smaller_pos) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => patch_jcc_at(st8, len1_smaller_pos, ((long)st8.text.Count))))(st_append_text(st7, mov_rr(reg_rbx(), reg_rcx())))))(st_append_text(st6, jcc(cc_le(), 0)))))(((long)st6.text.Count))))(st_append_text(st5, cmp_rr(reg_rbx(), reg_rcx())))))(st_append_text(st4, mov_rr(reg_rbx(), reg_r12())))))(st_append_text(st3, mov_load(reg_rcx(), reg_rsi(), 0)))))(st_append_text(st2, mov_load(reg_r12(), reg_rdi(), 0)))))(st_append_text(st1, push_r(reg_r12())))))(st_append_text(st0, push_r(reg_rbx())))))(record_func_offset(st, "__text_compare"));

    public static HelpResult3 emit_text_compare_byte_load(CodegenState st) => ((Func<CodegenState, HelpResult3>)((st0) => ((Func<long, HelpResult3>)((cmp_loop) => ((Func<CodegenState, HelpResult3>)((st1) => ((Func<long, HelpResult3>)((cmp_done_pos) => ((Func<CodegenState, HelpResult3>)((st2) => ((Func<CodegenState, HelpResult3>)((st3) => ((Func<CodegenState, HelpResult3>)((st4) => ((Func<CodegenState, HelpResult3>)((st5) => ((Func<CodegenState, HelpResult3>)((st6) => ((Func<CodegenState, HelpResult3>)((st7) => ((Func<CodegenState, HelpResult3>)((st8) => ((Func<CodegenState, HelpResult3>)((st9) => ((Func<long, HelpResult3>)((bytes_equal_pos) => ((Func<CodegenState, HelpResult3>)((st10) => new HelpResult3(cg: st10, p1: cmp_done_pos, p2: bytes_equal_pos, p3: cmp_loop)))(st_append_text(st9, jcc(cc_e(), 0)))))(((long)st9.text.Count))))(st_append_text(st8, cmp_rr(reg_rax(), reg_rdx())))))(st_append_text(st7, movzx_byte(reg_rdx(), reg_rdx(), 8)))))(st_append_text(st6, add_rr(reg_rdx(), reg_r11())))))(st_append_text(st5, mov_rr(reg_rdx(), reg_rsi())))))(st_append_text(st4, movzx_byte(reg_rax(), reg_rax(), 8)))))(st_append_text(st3, add_rr(reg_rax(), reg_r11())))))(st_append_text(st2, mov_rr(reg_rax(), reg_rdi())))))(st_append_text(st1, jcc(cc_ge(), 0)))))(((long)st1.text.Count))))(st_append_text(st0, cmp_rr(reg_r11(), reg_rbx())))))(((long)st0.text.Count))))(st_append_text(st, li(reg_r11(), 0)));

    public static HelpResult2 emit_text_compare_diff(CodegenState st, long bytes_equal_pos, long cmp_loop) => ((Func<long, HelpResult2>)((a_greater_pos) => ((Func<CodegenState, HelpResult2>)((st0) => ((Func<CodegenState, HelpResult2>)((st1) => ((Func<long, HelpResult2>)((ret_early1) => ((Func<CodegenState, HelpResult2>)((st2) => ((Func<CodegenState, HelpResult2>)((st3) => ((Func<CodegenState, HelpResult2>)((st4) => ((Func<long, HelpResult2>)((ret_early2) => ((Func<CodegenState, HelpResult2>)((st5) => ((Func<CodegenState, HelpResult2>)((st6) => ((Func<CodegenState, HelpResult2>)((st7) => ((Func<CodegenState, HelpResult2>)((st8) => new HelpResult2(cg: st8, p1: ret_early1, p2: ret_early2)))(st_append_text(st7, jmp((cmp_loop - (((long)st7.text.Count) + 5)))))))(st_append_text(st6, add_ri(reg_r11(), 1)))))(patch_jcc_at(st5, bytes_equal_pos, ((long)st5.text.Count)))))(st_append_text(st4, jmp(0)))))(((long)st4.text.Count))))(st_append_text(st3, li(reg_rax(), 1)))))(patch_jcc_at(st2, a_greater_pos, ((long)st2.text.Count)))))(st_append_text(st1, jmp(0)))))(((long)st1.text.Count))))(st_append_text(st0, li(reg_rax(), (0 - 1))))))(st_append_text(st, jcc(cc_g(), 0)))))(((long)st.text.Count));

    public static HelpResult2 emit_text_compare_len(CodegenState st, long cmp_done_pos) => ((Func<CodegenState, HelpResult2>)((st0) => ((Func<CodegenState, HelpResult2>)((st1) => ((Func<long, HelpResult2>)((len_eq_pos) => ((Func<CodegenState, HelpResult2>)((st2) => ((Func<long, HelpResult2>)((len_gt_pos) => ((Func<CodegenState, HelpResult2>)((st3) => ((Func<CodegenState, HelpResult2>)((st4) => ((Func<long, HelpResult2>)((ret_len1) => ((Func<CodegenState, HelpResult2>)((st5) => ((Func<CodegenState, HelpResult2>)((st6) => ((Func<CodegenState, HelpResult2>)((st7) => ((Func<long, HelpResult2>)((ret_len2) => ((Func<CodegenState, HelpResult2>)((st8) => ((Func<CodegenState, HelpResult2>)((st9) => ((Func<CodegenState, HelpResult2>)((st10) => new HelpResult2(cg: st10, p1: ret_len1, p2: ret_len2)))(st_append_text(st9, li(reg_rax(), 0)))))(patch_jcc_at(st8, len_eq_pos, ((long)st8.text.Count)))))(st_append_text(st7, jmp(0)))))(((long)st7.text.Count))))(st_append_text(st6, li(reg_rax(), 1)))))(patch_jcc_at(st5, len_gt_pos, ((long)st5.text.Count)))))(st_append_text(st4, jmp(0)))))(((long)st4.text.Count))))(st_append_text(st3, li(reg_rax(), (0 - 1))))))(st_append_text(st2, jcc(cc_g(), 0)))))(((long)st2.text.Count))))(st_append_text(st1, jcc(cc_e(), 0)))))(((long)st1.text.Count))))(st_append_text(st0, cmp_rr(reg_r12(), reg_rcx())))))(patch_jcc_at(st, cmp_done_pos, ((long)st.text.Count)));

    public static CodegenState emit_text_compare(CodegenState st) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<HelpResult3, CodegenState>)((bytes) => ((Func<HelpResult2, CodegenState>)((diff) => ((Func<HelpResult2, CodegenState>)((lens) => ((Func<long, CodegenState>)((end_pos) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => st_append_text(st6, x86_ret())))(st_append_text(st5, pop_r(reg_rbx())))))(st_append_text(st4, pop_r(reg_r12())))))(patch_jmp_at(st3, lens.p2, end_pos))))(patch_jmp_at(st2, lens.p1, end_pos))))(patch_jmp_at(st1, diff.p2, end_pos))))(patch_jmp_at(lens.cg, diff.p1, end_pos))))(((long)lens.cg.text.Count))))(emit_text_compare_len(diff.cg, bytes.p1))))(emit_text_compare_diff(bytes.cg, bytes.p2, bytes.p3))))(emit_text_compare_byte_load(st0))))(emit_text_compare_prologue(st));

    public static CodegenState emit_list_contains(CodegenState st) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<long, CodegenState>)((loop_top) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<long, CodegenState>)((not_found_pos) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<long, CodegenState>)((found_pos) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => ((Func<CodegenState, CodegenState>)((st12) => ((Func<CodegenState, CodegenState>)((st13) => ((Func<CodegenState, CodegenState>)((st14) => ((Func<CodegenState, CodegenState>)((st15) => ((Func<CodegenState, CodegenState>)((st16) => ((Func<CodegenState, CodegenState>)((st17) => st_append_text(st17, x86_ret())))(st_append_text(st16, li(reg_rax(), 0)))))(patch_jcc_at(st15, not_found_pos, ((long)st15.text.Count)))))(st_append_text(st14, x86_ret()))))(st_append_text(st13, li(reg_rax(), 1)))))(patch_jcc_at(st12, found_pos, ((long)st12.text.Count)))))(st_append_text(st11, jmp((loop_top - (((long)st11.text.Count) + 5)))))))(st_append_text(st10, add_ri(reg_r11(), 1)))))(st_append_text(st9, jcc(cc_e(), 0)))))(((long)st9.text.Count))))(st_append_text(st8, cmp_rr(reg_rax(), reg_rsi())))))(st_append_text(st7, mov_load(reg_rax(), reg_rax(), 8)))))(st_append_text(st6, add_rr(reg_rax(), reg_rdi())))))(st_append_text(st5, shl_ri(reg_rax(), 3)))))(st_append_text(st4, mov_rr(reg_rax(), reg_r11())))))(st_append_text(st3, jcc(cc_ge(), 0)))))(((long)st3.text.Count))))(st_append_text(st2, cmp_rr(reg_r11(), reg_rcx())))))(((long)st2.text.Count))))(st_append_text(st1, li(reg_r11(), 0)))))(st_append_text(st0, mov_load(reg_rcx(), reg_rdi(), 0)))))(record_func_offset(st, "__list_contains"));

    public static CodegenState emit_list_cons_alloc(CodegenState st) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => st_append_text(st11, mov_store(reg_rax(), reg_rdi(), 8))))(st_append_text(st10, mov_store(reg_rax(), reg_rdx(), 0)))))(st_append_text(st9, add_rr(reg_r10(), reg_r11())))))(st_append_text(st8, shl_ri(reg_r11(), 3)))))(st_append_text(st7, add_ri(reg_r11(), 1)))))(st_append_text(st6, mov_rr(reg_r11(), reg_rdx())))))(st_append_text(st5, mov_rr(reg_rax(), reg_r10())))))(st_append_text(st4, add_ri(reg_r10(), 8)))))(st_append_text(st3, mov_store(reg_r10(), reg_rdx(), 0)))))(st_append_text(st2, add_ri(reg_rdx(), 1)))))(st_append_text(st1, mov_rr(reg_rdx(), reg_rcx())))))(st_append_text(st0, mov_load(reg_rcx(), reg_rsi(), 0)))))(record_func_offset(st, "__list_cons"));

    public static CodegenState emit_list_cons_copy(CodegenState st) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<long, CodegenState>)((loop_top) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<long, CodegenState>)((exit_pos) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => ((Func<CodegenState, CodegenState>)((st12) => st_append_text(st12, x86_ret())))(patch_jcc_at(st11, exit_pos, ((long)st11.text.Count)))))(st_append_text(st10, jmp((loop_top - (((long)st10.text.Count) + 5)))))))(st_append_text(st9, add_ri(reg_r11(), 8)))))(st_append_text(st8, mov_store(reg_rdi(), reg_rdx(), 16)))))(st_append_text(st7, add_rr(reg_rdi(), reg_r11())))))(st_append_text(st6, mov_rr(reg_rdi(), reg_rax())))))(st_append_text(st5, mov_load(reg_rdx(), reg_rdx(), 8)))))(st_append_text(st4, add_rr(reg_rdx(), reg_r11())))))(st_append_text(st3, mov_rr(reg_rdx(), reg_rsi())))))(st_append_text(st2, jcc(cc_ge(), 0)))))(((long)st2.text.Count))))(st_append_text(st1, cmp_rr(reg_r11(), reg_rcx())))))(((long)st1.text.Count))))(st_append_text(st0, li(reg_r11(), 0)))))(st_append_text(st, shl_ri(reg_rcx(), 3)));

    public static CodegenState emit_list_cons(CodegenState st) => ((Func<CodegenState, CodegenState>)((st0) => emit_list_cons_copy(st0)))(emit_list_cons_alloc(st));

    public static CodegenState emit_list_append_alloc(CodegenState st) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => ((Func<CodegenState, CodegenState>)((st12) => ((Func<CodegenState, CodegenState>)((st13) => ((Func<CodegenState, CodegenState>)((st14) => ((Func<CodegenState, CodegenState>)((st15) => ((Func<CodegenState, CodegenState>)((st16) => st_append_text(st16, mov_store(reg_rax(), reg_r13(), 0))))(st_append_text(st15, add_rr(reg_r10(), reg_r11())))))(st_append_text(st14, shl_ri(reg_r11(), 3)))))(st_append_text(st13, add_ri(reg_r11(), 1)))))(st_append_text(st12, mov_rr(reg_r11(), reg_r13())))))(st_append_text(st11, mov_rr(reg_rax(), reg_r10())))))(st_append_text(st10, add_ri(reg_r10(), 8)))))(st_append_text(st9, mov_store(reg_r10(), reg_r13(), 0)))))(st_append_text(st8, add_rr(reg_r13(), reg_rdx())))))(st_append_text(st7, mov_rr(reg_r13(), reg_rcx())))))(st_append_text(st6, mov_load(reg_rdx(), reg_r12(), 0)))))(st_append_text(st5, mov_load(reg_rcx(), reg_rbx(), 0)))))(st_append_text(st4, mov_rr(reg_r12(), reg_rsi())))))(st_append_text(st3, mov_rr(reg_rbx(), reg_rdi())))))(st_append_text(st2, push_r(reg_r13())))))(st_append_text(st1, push_r(reg_r12())))))(st_append_text(st0, push_r(reg_rbx())))))(record_func_offset(st, "__list_append"));

    public static CodegenState emit_list_append_copy1(CodegenState st) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<long, CodegenState>)((loop1) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<long, CodegenState>)((exit1_pos) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => ((Func<CodegenState, CodegenState>)((st12) => patch_jcc_at(st12, exit1_pos, ((long)st12.text.Count))))(st_append_text(st11, jmp((loop1 - (((long)st11.text.Count) + 5)))))))(st_append_text(st10, add_ri(reg_r11(), 8)))))(st_append_text(st9, mov_store(reg_rsi(), reg_rdx(), 8)))))(st_append_text(st8, add_rr(reg_rsi(), reg_r11())))))(st_append_text(st7, mov_rr(reg_rsi(), reg_rax())))))(st_append_text(st6, mov_load(reg_rdx(), reg_rdx(), 8)))))(st_append_text(st5, add_rr(reg_rdx(), reg_r11())))))(st_append_text(st4, mov_rr(reg_rdx(), reg_rbx())))))(st_append_text(st3, jcc(cc_ge(), 0)))))(((long)st3.text.Count))))(st_append_text(st2, cmp_rr(reg_r11(), reg_rcx())))))(((long)st2.text.Count))))(st_append_text(st1, li(reg_r11(), 0)))))(st_append_text(st0, shl_ri(reg_rcx(), 3)))))(st_append_text(st, mov_load(reg_rcx(), reg_rbx(), 0)));

    public static CodegenState emit_list_append_copy2(CodegenState st) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<long, CodegenState>)((loop2) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<long, CodegenState>)((exit2_pos) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => ((Func<CodegenState, CodegenState>)((st12) => ((Func<CodegenState, CodegenState>)((st13) => ((Func<CodegenState, CodegenState>)((st14) => ((Func<CodegenState, CodegenState>)((st15) => patch_jcc_at(st15, exit2_pos, ((long)st15.text.Count))))(st_append_text(st14, jmp((loop2 - (((long)st14.text.Count) + 5)))))))(st_append_text(st13, add_ri(reg_r11(), 8)))))(st_append_text(st12, mov_store(reg_rdi(), reg_rsi(), 8)))))(st_append_text(st11, add_rr(reg_rdi(), reg_r11())))))(st_append_text(st10, add_rr(reg_rdi(), reg_rcx())))))(st_append_text(st9, mov_rr(reg_rdi(), reg_rax())))))(st_append_text(st8, mov_load(reg_rsi(), reg_rsi(), 8)))))(st_append_text(st7, add_rr(reg_rsi(), reg_r11())))))(st_append_text(st6, mov_rr(reg_rsi(), reg_r12())))))(st_append_text(st5, jcc(cc_ge(), 0)))))(((long)st5.text.Count))))(st_append_text(st4, cmp_rr(reg_r11(), reg_rdx())))))(((long)st4.text.Count))))(st_append_text(st3, li(reg_r11(), 0)))))(st_append_text(st2, shl_ri(reg_rdx(), 3)))))(st_append_text(st1, mov_load(reg_rdx(), reg_r12(), 0)))))(st_append_text(st0, shl_ri(reg_rcx(), 3)))))(st_append_text(st, mov_load(reg_rcx(), reg_rbx(), 0)));

    public static CodegenState emit_list_append(CodegenState st) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => st_append_text(st5, x86_ret())))(st_append_text(st4, pop_r(reg_rbx())))))(st_append_text(st3, pop_r(reg_r12())))))(st_append_text(st2, pop_r(reg_r13())))))(emit_list_append_copy2(st1))))(emit_list_append_copy1(st0))))(emit_list_append_alloc(st));

    public static HelpResult1 emit_list_snoc_path1(CodegenState st) => ((Func<CodegenState, HelpResult1>)((st0) => ((Func<CodegenState, HelpResult1>)((st1) => ((Func<CodegenState, HelpResult1>)((st2) => ((Func<CodegenState, HelpResult1>)((st3) => ((Func<long, HelpResult1>)((path2_pos) => ((Func<CodegenState, HelpResult1>)((st4) => ((Func<CodegenState, HelpResult1>)((st5) => ((Func<CodegenState, HelpResult1>)((st6) => ((Func<CodegenState, HelpResult1>)((st7) => ((Func<CodegenState, HelpResult1>)((st8) => ((Func<CodegenState, HelpResult1>)((st9) => ((Func<CodegenState, HelpResult1>)((st10) => ((Func<CodegenState, HelpResult1>)((st11) => ((Func<CodegenState, HelpResult1>)((st12) => new HelpResult1(cg: st12, p1: path2_pos)))(st_append_text(st11, x86_ret()))))(st_append_text(st10, mov_rr(reg_rax(), reg_rdi())))))(st_append_text(st9, mov_store(reg_rdi(), reg_rcx(), 0)))))(st_append_text(st8, add_ri(reg_rcx(), 1)))))(st_append_text(st7, mov_store(reg_rax(), reg_rsi(), 8)))))(st_append_text(st6, add_rr(reg_rax(), reg_rdi())))))(st_append_text(st5, shl_ri(reg_rax(), 3)))))(st_append_text(st4, mov_rr(reg_rax(), reg_rcx())))))(st_append_text(st3, jcc(cc_ge(), 0)))))(((long)st3.text.Count))))(st_append_text(st2, cmp_rr(reg_rcx(), reg_rdx())))))(st_append_text(st1, mov_load(reg_rdx(), reg_rdi(), (0 - 8))))))(st_append_text(st0, mov_load(reg_rcx(), reg_rdi(), 0)))))(record_func_offset(st, "__list_snoc"));

    public static HelpResult1 emit_list_snoc_path2(CodegenState st, long path2_pos) => ((Func<CodegenState, HelpResult1>)((st0) => ((Func<CodegenState, HelpResult1>)((st1) => ((Func<CodegenState, HelpResult1>)((st2) => ((Func<CodegenState, HelpResult1>)((st3) => ((Func<CodegenState, HelpResult1>)((st4) => ((Func<CodegenState, HelpResult1>)((st5) => ((Func<long, HelpResult1>)((path3_pos) => ((Func<CodegenState, HelpResult1>)((st6) => ((Func<CodegenState, HelpResult1>)((st7) => ((Func<CodegenState, HelpResult1>)((st8) => ((Func<CodegenState, HelpResult1>)((st9) => ((Func<long, HelpResult1>)((cap_ok_pos) => ((Func<CodegenState, HelpResult1>)((st10) => ((Func<CodegenState, HelpResult1>)((st11) => ((Func<CodegenState, HelpResult1>)((st12) => ((Func<CodegenState, HelpResult1>)((st13) => ((Func<CodegenState, HelpResult1>)((st14) => ((Func<CodegenState, HelpResult1>)((st15) => ((Func<CodegenState, HelpResult1>)((st16) => ((Func<CodegenState, HelpResult1>)((st17) => ((Func<CodegenState, HelpResult1>)((st18) => new HelpResult1(cg: st18, p1: path3_pos)))(st_append_text(st17, shl_ri(reg_rax(), 3)))))(st_append_text(st16, mov_rr(reg_rax(), reg_rcx())))))(st_append_text(st15, add_rr(reg_r10(), reg_rax())))))(st_append_text(st14, shl_ri(reg_rax(), 3)))))(st_append_text(st13, sub_rr(reg_rax(), reg_rdx())))))(st_append_text(st12, mov_store(reg_rdi(), reg_rax(), (0 - 8))))))(patch_jcc_at(st11, cap_ok_pos, ((long)st11.text.Count)))))(st_append_text(st10, li(reg_rax(), 4)))))(st_append_text(st9, jcc(cc_ge(), 0)))))(((long)st9.text.Count))))(st_append_text(st8, cmp_ri(reg_rax(), 4)))))(st_append_text(st7, shl_ri(reg_rax(), 1)))))(st_append_text(st6, mov_rr(reg_rax(), reg_rdx())))))(st_append_text(st5, jcc(cc_ne(), 0)))))(((long)st5.text.Count))))(st_append_text(st4, cmp_rr(reg_rax(), reg_r10())))))(st_append_text(st3, add_rr(reg_rax(), reg_rdi())))))(st_append_text(st2, shl_ri(reg_rax(), 3)))))(st_append_text(st1, add_ri(reg_rax(), 1)))))(st_append_text(st0, mov_rr(reg_rax(), reg_rdx())))))(patch_jcc_at(st, path2_pos, ((long)st.text.Count)));

    public static CodegenState emit_list_snoc_path2_store(CodegenState st) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => st_append_text(st4, x86_ret())))(st_append_text(st3, mov_rr(reg_rax(), reg_rdi())))))(st_append_text(st2, mov_store(reg_rdi(), reg_rcx(), 0)))))(st_append_text(st1, add_ri(reg_rcx(), 1)))))(st_append_text(st0, mov_store(reg_rax(), reg_rsi(), 8)))))(st_append_text(st, add_rr(reg_rax(), reg_rdi())));

    public static CodegenState emit_list_snoc_path3_alloc(CodegenState st, long path3_pos) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<long, CodegenState>)((cap_ok2_pos) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => ((Func<CodegenState, CodegenState>)((st12) => ((Func<CodegenState, CodegenState>)((st13) => ((Func<CodegenState, CodegenState>)((st14) => ((Func<CodegenState, CodegenState>)((st15) => ((Func<CodegenState, CodegenState>)((st16) => ((Func<CodegenState, CodegenState>)((st17) => st_append_text(st17, add_rr(reg_r10(), reg_rdx()))))(st_append_text(st16, shl_ri(reg_rdx(), 3)))))(st_append_text(st15, add_ri(reg_rdx(), 1)))))(st_append_text(st14, mov_rr(reg_rdx(), reg_r13())))))(st_append_text(st13, mov_rr(reg_rax(), reg_r10())))))(st_append_text(st12, add_ri(reg_r10(), 8)))))(st_append_text(st11, mov_store(reg_r10(), reg_r13(), 0)))))(patch_jcc_at(st10, cap_ok2_pos, ((long)st10.text.Count)))))(st_append_text(st9, li(reg_r13(), 4)))))(st_append_text(st8, jcc(cc_ge(), 0)))))(((long)st8.text.Count))))(st_append_text(st7, cmp_ri(reg_r13(), 4)))))(st_append_text(st6, shl_ri(reg_r13(), 1)))))(st_append_text(st5, mov_rr(reg_r13(), reg_rcx())))))(st_append_text(st4, mov_rr(reg_r12(), reg_rsi())))))(st_append_text(st3, mov_rr(reg_rbx(), reg_rdi())))))(st_append_text(st2, push_r(reg_r13())))))(st_append_text(st1, push_r(reg_r12())))))(st_append_text(st0, push_r(reg_rbx())))))(patch_jcc_at(st, path3_pos, ((long)st.text.Count)));

    public static CodegenState emit_list_snoc_path3_copy(CodegenState st) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<long, CodegenState>)((copy_loop) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<long, CodegenState>)((copy_done_pos) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => ((Func<CodegenState, CodegenState>)((st12) => ((Func<CodegenState, CodegenState>)((st13) => ((Func<CodegenState, CodegenState>)((st14) => ((Func<CodegenState, CodegenState>)((st15) => ((Func<CodegenState, CodegenState>)((st16) => patch_jcc_at(st16, copy_done_pos, ((long)st16.text.Count))))(st_append_text(st15, jmp((copy_loop - (((long)st15.text.Count) + 5)))))))(st_append_text(st14, add_ri(reg_r11(), 1)))))(st_append_text(st13, mov_store(reg_rdi(), reg_rsi(), 8)))))(st_append_text(st12, add_rr(reg_rdi(), reg_rdx())))))(st_append_text(st11, mov_rr(reg_rdi(), reg_rax())))))(st_append_text(st10, mov_load(reg_rsi(), reg_rsi(), 8)))))(st_append_text(st9, add_rr(reg_rsi(), reg_rdx())))))(st_append_text(st8, mov_rr(reg_rsi(), reg_rbx())))))(st_append_text(st7, shl_ri(reg_rdx(), 3)))))(st_append_text(st6, mov_rr(reg_rdx(), reg_r11())))))(st_append_text(st5, jcc(cc_ge(), 0)))))(((long)st5.text.Count))))(st_append_text(st4, cmp_rr(reg_r11(), reg_rcx())))))(((long)st4.text.Count))))(st_append_text(st3, li(reg_r11(), 0)))))(st_append_text(st2, mov_store(reg_rax(), reg_rdx(), 0)))))(st_append_text(st1, add_ri(reg_rdx(), 1)))))(st_append_text(st0, mov_rr(reg_rdx(), reg_rcx())))))(st_append_text(st, mov_load(reg_rcx(), reg_rbx(), 0)));

    public static CodegenState emit_list_snoc_path3_finish(CodegenState st) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => st_append_text(st7, x86_ret())))(st_append_text(st6, pop_r(reg_rbx())))))(st_append_text(st5, pop_r(reg_r12())))))(st_append_text(st4, pop_r(reg_r13())))))(st_append_text(st3, mov_store(reg_rdi(), reg_r12(), 8)))))(st_append_text(st2, add_rr(reg_rdi(), reg_rdx())))))(st_append_text(st1, mov_rr(reg_rdi(), reg_rax())))))(st_append_text(st0, shl_ri(reg_rdx(), 3)))))(st_append_text(st, mov_rr(reg_rdx(), reg_rcx())));

    public static CodegenState emit_list_snoc(CodegenState st) => ((Func<HelpResult1, CodegenState>)((p1) => ((Func<HelpResult1, CodegenState>)((p2) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => emit_list_snoc_path3_finish(st2)))(emit_list_snoc_path3_copy(st1))))(emit_list_snoc_path3_alloc(st0, p2.p1))))(emit_list_snoc_path2_store(p2.cg))))(emit_list_snoc_path2(p1.cg, p1.p1))))(emit_list_snoc_path1(st));

    public static HelpResult2 emit_list_insert_at_prologue(CodegenState st) => ((Func<CodegenState, HelpResult2>)((st0) => ((Func<CodegenState, HelpResult2>)((st1) => ((Func<CodegenState, HelpResult2>)((st2) => ((Func<CodegenState, HelpResult2>)((st3) => ((Func<CodegenState, HelpResult2>)((st4) => ((Func<CodegenState, HelpResult2>)((st5) => ((Func<CodegenState, HelpResult2>)((st6) => ((Func<CodegenState, HelpResult2>)((st7) => ((Func<CodegenState, HelpResult2>)((st8) => ((Func<CodegenState, HelpResult2>)((st9) => ((Func<CodegenState, HelpResult2>)((st10) => ((Func<long, HelpResult2>)((in_place_pos) => ((Func<CodegenState, HelpResult2>)((st11) => ((Func<CodegenState, HelpResult2>)((st12) => ((Func<CodegenState, HelpResult2>)((st13) => ((Func<CodegenState, HelpResult2>)((st14) => ((Func<CodegenState, HelpResult2>)((st15) => ((Func<CodegenState, HelpResult2>)((st16) => ((Func<long, HelpResult2>)((path3_pos) => ((Func<CodegenState, HelpResult2>)((st17) => new HelpResult2(cg: st17, p1: in_place_pos, p2: path3_pos)))(st_append_text(st16, jcc(cc_ne(), 0)))))(((long)st16.text.Count))))(st_append_text(st15, cmp_rr(reg_rax(), reg_r10())))))(st_append_text(st14, add_rr(reg_rax(), reg_rbx())))))(st_append_text(st13, shl_ri(reg_rax(), 3)))))(st_append_text(st12, add_ri(reg_rax(), 1)))))(st_append_text(st11, mov_rr(reg_rax(), reg_rcx())))))(st_append_text(st10, jcc(cc_l(), 0)))))(((long)st10.text.Count))))(st_append_text(st9, cmp_rr(reg_r14(), reg_rcx())))))(st_append_text(st8, mov_load(reg_rcx(), reg_rbx(), (0 - 8))))))(st_append_text(st7, mov_load(reg_r14(), reg_rbx(), 0)))))(st_append_text(st6, mov_rr(reg_r13(), reg_rdx())))))(st_append_text(st5, mov_rr(reg_r12(), reg_rsi())))))(st_append_text(st4, mov_rr(reg_rbx(), reg_rdi())))))(st_append_text(st3, push_r(reg_r14())))))(st_append_text(st2, push_r(reg_r13())))))(st_append_text(st1, push_r(reg_r12())))))(st_append_text(st0, push_r(reg_rbx())))))(record_func_offset(st, "__list_insert_at"));

    public static CodegenState emit_list_insert_at_grow(CodegenState st) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<long, CodegenState>)((cap_ok_pos) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => st_append_text(st8, add_rr(reg_r10(), reg_rax()))))(st_append_text(st7, shl_ri(reg_rax(), 3)))))(st_append_text(st6, sub_rr(reg_rax(), reg_rcx())))))(st_append_text(st5, mov_store(reg_rbx(), reg_rax(), (0 - 8))))))(patch_jcc_at(st4, cap_ok_pos, ((long)st4.text.Count)))))(st_append_text(st3, li(reg_rax(), 4)))))(st_append_text(st2, jcc(cc_ge(), 0)))))(((long)st2.text.Count))))(st_append_text(st1, cmp_ri(reg_rax(), 4)))))(st_append_text(st0, shl_ri(reg_rax(), 1)))))(st_append_text(st, mov_rr(reg_rax(), reg_rcx())));

    public static CodegenState emit_list_insert_at_shift(CodegenState st, long in_place_pos) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<long, CodegenState>)((shift_loop) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<long, CodegenState>)((shift_done_pos) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => ((Func<CodegenState, CodegenState>)((st12) => patch_jcc_at(st12, shift_done_pos, ((long)st12.text.Count))))(st_append_text(st11, jmp((shift_loop - (((long)st11.text.Count) + 5)))))))(st_append_text(st10, sub_ri(reg_r11(), 1)))))(st_append_text(st9, mov_store(reg_rax(), reg_rcx(), 16)))))(st_append_text(st8, mov_load(reg_rcx(), reg_rax(), 8)))))(st_append_text(st7, add_rr(reg_rax(), reg_rdx())))))(st_append_text(st6, mov_rr(reg_rax(), reg_rbx())))))(st_append_text(st5, shl_ri(reg_rdx(), 3)))))(st_append_text(st4, mov_rr(reg_rdx(), reg_r11())))))(st_append_text(st3, jcc(cc_l(), 0)))))(((long)st3.text.Count))))(st_append_text(st2, cmp_rr(reg_r11(), reg_r12())))))(((long)st2.text.Count))))(st_append_text(st1, sub_ri(reg_r11(), 1)))))(st_append_text(st0, mov_rr(reg_r11(), reg_r14())))))(patch_jcc_at(st, in_place_pos, ((long)st.text.Count)));

    public static CodegenState emit_list_insert_at_store(CodegenState st) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<CodegenState, CodegenState>)((st10) => st_append_text(st10, x86_ret())))(st_append_text(st9, pop_r(reg_rbx())))))(st_append_text(st8, pop_r(reg_r12())))))(st_append_text(st7, pop_r(reg_r13())))))(st_append_text(st6, pop_r(reg_r14())))))(st_append_text(st5, mov_rr(reg_rax(), reg_rbx())))))(st_append_text(st4, mov_store(reg_rbx(), reg_r14(), 0)))))(st_append_text(st3, add_ri(reg_r14(), 1)))))(st_append_text(st2, mov_store(reg_rdx(), reg_r13(), 8)))))(st_append_text(st1, add_rr(reg_rdx(), reg_rbx())))))(st_append_text(st0, shl_ri(reg_rdx(), 3)))))(st_append_text(st, mov_rr(reg_rdx(), reg_r12())));

    public static CodegenState emit_list_insert_at_path3_alloc(CodegenState st, long path3_pos) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<long, CodegenState>)((cap_ok_pos) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => ((Func<CodegenState, CodegenState>)((st12) => ((Func<CodegenState, CodegenState>)((st13) => ((Func<CodegenState, CodegenState>)((st14) => ((Func<CodegenState, CodegenState>)((st15) => st_append_text(st15, mov_store(reg_rax(), reg_rdx(), 0))))(st_append_text(st14, add_ri(reg_rdx(), 1)))))(st_append_text(st13, mov_rr(reg_rdx(), reg_r14())))))(st_append_text(st12, add_rr(reg_r10(), reg_rdx())))))(st_append_text(st11, shl_ri(reg_rdx(), 3)))))(st_append_text(st10, add_ri(reg_rdx(), 1)))))(st_append_text(st9, mov_rr(reg_rdx(), reg_rcx())))))(st_append_text(st8, mov_rr(reg_rax(), reg_r10())))))(st_append_text(st7, add_ri(reg_r10(), 8)))))(st_append_text(st6, mov_store(reg_r10(), reg_rcx(), 0)))))(patch_jcc_at(st5, cap_ok_pos, ((long)st5.text.Count)))))(st_append_text(st4, li(reg_rcx(), 4)))))(st_append_text(st3, jcc(cc_ge(), 0)))))(((long)st3.text.Count))))(st_append_text(st2, cmp_ri(reg_rcx(), 4)))))(st_append_text(st1, shl_ri(reg_rcx(), 1)))))(st_append_text(st0, mov_rr(reg_rcx(), reg_r14())))))(patch_jcc_at(st, path3_pos, ((long)st.text.Count)));

    public static CodegenState emit_list_insert_at_path3_pre(CodegenState st) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<long, CodegenState>)((pre_loop) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<long, CodegenState>)((pre_done_pos) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => ((Func<CodegenState, CodegenState>)((st12) => patch_jcc_at(st12, pre_done_pos, ((long)st12.text.Count))))(st_append_text(st11, jmp((pre_loop - (((long)st11.text.Count) + 5)))))))(st_append_text(st10, add_ri(reg_r11(), 1)))))(st_append_text(st9, mov_store(reg_rdi(), reg_rcx(), 8)))))(st_append_text(st8, add_rr(reg_rdi(), reg_rdx())))))(st_append_text(st7, mov_rr(reg_rdi(), reg_rax())))))(st_append_text(st6, mov_load(reg_rcx(), reg_rsi(), 8)))))(st_append_text(st5, add_rr(reg_rsi(), reg_rdx())))))(st_append_text(st4, mov_rr(reg_rsi(), reg_rbx())))))(st_append_text(st3, shl_ri(reg_rdx(), 3)))))(st_append_text(st2, mov_rr(reg_rdx(), reg_r11())))))(st_append_text(st1, jcc(cc_ge(), 0)))))(((long)st1.text.Count))))(st_append_text(st0, cmp_rr(reg_r11(), reg_r12())))))(((long)st0.text.Count))))(st_append_text(st, li(reg_r11(), 0)));

    public static CodegenState emit_list_insert_at_path3_insert(CodegenState st) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => st_append_text(st3, mov_store(reg_rdi(), reg_r13(), 8))))(st_append_text(st2, add_rr(reg_rdi(), reg_rdx())))))(st_append_text(st1, mov_rr(reg_rdi(), reg_rax())))))(st_append_text(st0, shl_ri(reg_rdx(), 3)))))(st_append_text(st, mov_rr(reg_rdx(), reg_r12())));

    public static CodegenState emit_list_insert_at_path3_post(CodegenState st) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<long, CodegenState>)((post_loop) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<long, CodegenState>)((post_done_pos) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => ((Func<CodegenState, CodegenState>)((st12) => ((Func<CodegenState, CodegenState>)((st13) => patch_jcc_at(st13, post_done_pos, ((long)st13.text.Count))))(st_append_text(st12, jmp((post_loop - (((long)st12.text.Count) + 5)))))))(st_append_text(st11, add_ri(reg_r11(), 1)))))(st_append_text(st10, mov_store(reg_rdi(), reg_rcx(), 8)))))(st_append_text(st9, add_rr(reg_rdi(), reg_rdx())))))(st_append_text(st8, mov_rr(reg_rdi(), reg_rax())))))(st_append_text(st7, add_ri(reg_rdx(), 8)))))(st_append_text(st6, mov_load(reg_rcx(), reg_rsi(), 8)))))(st_append_text(st5, add_rr(reg_rsi(), reg_rdx())))))(st_append_text(st4, mov_rr(reg_rsi(), reg_rbx())))))(st_append_text(st3, shl_ri(reg_rdx(), 3)))))(st_append_text(st2, mov_rr(reg_rdx(), reg_r11())))))(st_append_text(st1, jcc(cc_ge(), 0)))))(((long)st1.text.Count))))(st_append_text(st0, cmp_rr(reg_r11(), reg_r14())))))(((long)st0.text.Count))))(st_append_text(st, mov_rr(reg_r11(), reg_r12())));

    public static CodegenState emit_list_insert_at_path3_epilogue(CodegenState st) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => st_append_text(st3, x86_ret())))(st_append_text(st2, pop_r(reg_rbx())))))(st_append_text(st1, pop_r(reg_r12())))))(st_append_text(st0, pop_r(reg_r13())))))(st_append_text(st, pop_r(reg_r14())));

    public static CodegenState emit_list_insert_at(CodegenState st) => ((Func<HelpResult2, CodegenState>)((pro) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => emit_list_insert_at_path3_epilogue(st6)))(emit_list_insert_at_path3_post(st5))))(emit_list_insert_at_path3_insert(st4))))(emit_list_insert_at_path3_pre(st3))))(emit_list_insert_at_path3_alloc(st2, pro.p2))))(emit_list_insert_at_store(st1))))(emit_list_insert_at_shift(st0, pro.p1))))(emit_list_insert_at_grow(pro.cg))))(emit_list_insert_at_prologue(st));

    public static CodegenState emit_text_concat_list_len_pass(CodegenState st) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<long, CodegenState>)((len_loop) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<long, CodegenState>)((len_done_pos) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => ((Func<CodegenState, CodegenState>)((st12) => ((Func<CodegenState, CodegenState>)((st13) => ((Func<CodegenState, CodegenState>)((st14) => ((Func<CodegenState, CodegenState>)((st15) => ((Func<CodegenState, CodegenState>)((st16) => ((Func<CodegenState, CodegenState>)((st17) => ((Func<CodegenState, CodegenState>)((st18) => patch_jcc_at(st18, len_done_pos, ((long)st18.text.Count))))(st_append_text(st17, jmp((len_loop - (((long)st17.text.Count) + 5)))))))(st_append_text(st16, add_ri(reg_r11(), 1)))))(st_append_text(st15, add_rr(reg_r13(), reg_rax())))))(st_append_text(st14, mov_load(reg_rax(), reg_rax(), 0)))))(st_append_text(st13, mov_load(reg_rax(), reg_rax(), 8)))))(st_append_text(st12, add_rr(reg_rax(), reg_rbx())))))(st_append_text(st11, shl_ri(reg_rax(), 3)))))(st_append_text(st10, mov_rr(reg_rax(), reg_r11())))))(st_append_text(st9, jcc(cc_ge(), 0)))))(((long)st9.text.Count))))(st_append_text(st8, cmp_rr(reg_r11(), reg_r12())))))(((long)st8.text.Count))))(st_append_text(st7, li(reg_r11(), 0)))))(st_append_text(st6, li(reg_r13(), 0)))))(st_append_text(st5, mov_load(reg_r12(), reg_rbx(), 0)))))(st_append_text(st4, mov_rr(reg_rbx(), reg_rdi())))))(st_append_text(st3, push_r(reg_r14())))))(st_append_text(st2, push_r(reg_r13())))))(st_append_text(st1, push_r(reg_r12())))))(st_append_text(st0, push_r(reg_rbx())))))(record_func_offset(st, "__text_concat_list"));

    public static CodegenState emit_text_concat_list_alloc(CodegenState st) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => st_append_text(st4, add_rr(reg_r10(), reg_rax()))))(st_append_text(st3, and_ri(reg_rax(), (0 - 8))))))(st_append_text(st2, add_ri(reg_rax(), 15)))))(st_append_text(st1, mov_rr(reg_rax(), reg_r13())))))(st_append_text(st0, mov_store(reg_r14(), reg_r13(), 0)))))(st_append_text(st, mov_rr(reg_r14(), reg_r10())));

    public static HelpResult2 emit_text_concat_list_copy_outer(CodegenState st) => ((Func<CodegenState, HelpResult2>)((st0) => ((Func<CodegenState, HelpResult2>)((st1) => ((Func<long, HelpResult2>)((copy_loop) => ((Func<CodegenState, HelpResult2>)((st2) => ((Func<long, HelpResult2>)((copy_done_pos) => ((Func<CodegenState, HelpResult2>)((st3) => ((Func<CodegenState, HelpResult2>)((st4) => ((Func<CodegenState, HelpResult2>)((st5) => ((Func<CodegenState, HelpResult2>)((st6) => ((Func<CodegenState, HelpResult2>)((st7) => ((Func<CodegenState, HelpResult2>)((st8) => new HelpResult2(cg: st8, p1: copy_loop, p2: copy_done_pos)))(st_append_text(st7, mov_load(reg_rcx(), reg_rdi(), 0)))))(st_append_text(st6, mov_load(reg_rdi(), reg_rax(), 8)))))(st_append_text(st5, add_rr(reg_rax(), reg_rbx())))))(st_append_text(st4, shl_ri(reg_rax(), 3)))))(st_append_text(st3, mov_rr(reg_rax(), reg_r11())))))(st_append_text(st2, jcc(cc_ge(), 0)))))(((long)st2.text.Count))))(st_append_text(st1, cmp_rr(reg_r11(), reg_r12())))))(((long)st1.text.Count))))(st_append_text(st0, li(reg_r13(), 0)))))(st_append_text(st, li(reg_r11(), 0)));

    public static CodegenState emit_text_concat_list_copy_inner(CodegenState st, long copy_loop, long copy_done_pos) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<long, CodegenState>)((byte_loop) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<long, CodegenState>)((byte_done_pos) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => ((Func<CodegenState, CodegenState>)((st12) => ((Func<CodegenState, CodegenState>)((st13) => ((Func<CodegenState, CodegenState>)((st14) => ((Func<CodegenState, CodegenState>)((st15) => patch_jcc_at(st15, copy_done_pos, ((long)st15.text.Count))))(st_append_text(st14, jmp((copy_loop - (((long)st14.text.Count) + 5)))))))(st_append_text(st13, add_ri(reg_r11(), 1)))))(st_append_text(st12, add_rr(reg_r13(), reg_rcx())))))(patch_jcc_at(st11, byte_done_pos, ((long)st11.text.Count)))))(st_append_text(st10, jmp((byte_loop - (((long)st10.text.Count) + 5)))))))(st_append_text(st9, add_ri(reg_rsi(), 1)))))(st_append_text(st8, mov_store_byte(reg_rdx(), reg_rax(), 8)))))(st_append_text(st7, add_rr(reg_rdx(), reg_rsi())))))(st_append_text(st6, add_rr(reg_rdx(), reg_r13())))))(st_append_text(st5, mov_rr(reg_rdx(), reg_r14())))))(st_append_text(st4, movzx_byte(reg_rax(), reg_rax(), 8)))))(st_append_text(st3, add_rr(reg_rax(), reg_rsi())))))(st_append_text(st2, mov_rr(reg_rax(), reg_rdi())))))(st_append_text(st1, jcc(cc_ge(), 0)))))(((long)st1.text.Count))))(st_append_text(st0, cmp_rr(reg_rsi(), reg_rcx())))))(((long)st0.text.Count))))(st_append_text(st, li(reg_rsi(), 0)));

    public static CodegenState emit_text_concat_list(CodegenState st) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<HelpResult2, CodegenState>)((outer) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => st_append_text(st7, x86_ret())))(st_append_text(st6, pop_r(reg_rbx())))))(st_append_text(st5, pop_r(reg_r12())))))(st_append_text(st4, pop_r(reg_r13())))))(st_append_text(st3, pop_r(reg_r14())))))(st_append_text(st2, mov_rr(reg_rax(), reg_r14())))))(emit_text_concat_list_copy_inner(outer.cg, outer.p1, outer.p2))))(emit_text_concat_list_copy_outer(st1))))(emit_text_concat_list_alloc(st0))))(emit_text_concat_list_len_pass(st));

    public static CodegenState emit_str_replace_prologue(CodegenState st) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => st_append_text(st11, mov_load(reg_rax(), reg_rbx(), 0))))(st_append_text(st10, li(reg_rcx(), 0)))))(st_append_text(st9, li(reg_r15(), 0)))))(st_append_text(st8, mov_rr(reg_r14(), reg_r10())))))(st_append_text(st7, mov_rr(reg_r13(), reg_rdx())))))(st_append_text(st6, mov_rr(reg_r12(), reg_rsi())))))(st_append_text(st5, mov_rr(reg_rbx(), reg_rdi())))))(st_append_text(st4, push_r(reg_r15())))))(st_append_text(st3, push_r(reg_r14())))))(st_append_text(st2, push_r(reg_r13())))))(st_append_text(st1, push_r(reg_r12())))))(st_append_text(st0, push_r(reg_rbx())))))(record_func_offset(st, "__str_replace"));

    public static HelpResult4 emit_str_replace_main_head(CodegenState st) => ((Func<long, HelpResult4>)((main_loop) => ((Func<CodegenState, HelpResult4>)((st0) => ((Func<CodegenState, HelpResult4>)((st1) => ((Func<long, HelpResult4>)((done_pos) => ((Func<CodegenState, HelpResult4>)((st2) => ((Func<CodegenState, HelpResult4>)((st3) => ((Func<CodegenState, HelpResult4>)((st4) => ((Func<long, HelpResult4>)((no_match_empty_pos) => ((Func<CodegenState, HelpResult4>)((st5) => ((Func<CodegenState, HelpResult4>)((st6) => ((Func<CodegenState, HelpResult4>)((st7) => ((Func<CodegenState, HelpResult4>)((st8) => ((Func<long, HelpResult4>)((cant_match_pos) => ((Func<CodegenState, HelpResult4>)((st9) => new HelpResult4(cg: st9, p1: done_pos, p2: main_loop, p3: no_match_empty_pos, p4: cant_match_pos)))(st_append_text(st8, jcc(cc_g(), 0)))))(((long)st8.text.Count))))(st_append_text(st7, cmp_rr(reg_rsi(), reg_rax())))))(st_append_text(st6, add_rr(reg_rsi(), reg_rdx())))))(st_append_text(st5, mov_rr(reg_rsi(), reg_rcx())))))(st_append_text(st4, jcc(cc_e(), 0)))))(((long)st4.text.Count))))(st_append_text(st3, test_rr(reg_rdx(), reg_rdx())))))(st_append_text(st2, mov_load(reg_rdx(), reg_r12(), 0)))))(st_append_text(st1, jcc(cc_ge(), 0)))))(((long)st1.text.Count))))(st_append_text(st0, cmp_rr(reg_rcx(), reg_rax())))))(st_append_text(st, mov_load(reg_rax(), reg_rbx(), 0)))))(((long)st.text.Count));

    public static HelpResult2 emit_str_replace_cmp(CodegenState st) => ((Func<CodegenState, HelpResult2>)((st0) => ((Func<long, HelpResult2>)((cmp_loop) => ((Func<CodegenState, HelpResult2>)((st1) => ((Func<long, HelpResult2>)((match_pos) => ((Func<CodegenState, HelpResult2>)((st2) => ((Func<CodegenState, HelpResult2>)((st3) => ((Func<CodegenState, HelpResult2>)((st4) => ((Func<CodegenState, HelpResult2>)((st5) => ((Func<CodegenState, HelpResult2>)((st6) => ((Func<CodegenState, HelpResult2>)((st7) => ((Func<CodegenState, HelpResult2>)((st8) => ((Func<CodegenState, HelpResult2>)((st9) => ((Func<CodegenState, HelpResult2>)((st10) => ((Func<long, HelpResult2>)((mismatch_pos) => ((Func<CodegenState, HelpResult2>)((st11) => ((Func<CodegenState, HelpResult2>)((st12) => ((Func<CodegenState, HelpResult2>)((st13) => new HelpResult2(cg: st13, p1: match_pos, p2: mismatch_pos)))(st_append_text(st12, jmp((cmp_loop - (((long)st12.text.Count) + 5)))))))(st_append_text(st11, add_ri(reg_rsi(), 1)))))(st_append_text(st10, jcc(cc_ne(), 0)))))(((long)st10.text.Count))))(st_append_text(st9, cmp_rr(reg_rax(), reg_rdi())))))(st_append_text(st8, movzx_byte(reg_rdi(), reg_rdi(), 8)))))(st_append_text(st7, add_rr(reg_rdi(), reg_rsi())))))(st_append_text(st6, mov_rr(reg_rdi(), reg_r12())))))(st_append_text(st5, movzx_byte(reg_rax(), reg_rax(), 8)))))(st_append_text(st4, add_rr(reg_rax(), reg_rsi())))))(st_append_text(st3, add_rr(reg_rax(), reg_rcx())))))(st_append_text(st2, mov_rr(reg_rax(), reg_rbx())))))(st_append_text(st1, jcc(cc_ge(), 0)))))(((long)st1.text.Count))))(st_append_text(st0, cmp_rr(reg_rsi(), reg_rdx())))))(((long)st0.text.Count))))(st_append_text(st, li(reg_rsi(), 0)));

    public static CodegenState emit_str_replace_copy_new(CodegenState st, long main_loop) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<long, CodegenState>)((copy_loop) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<long, CodegenState>)((copy_done_pos) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => ((Func<CodegenState, CodegenState>)((st12) => ((Func<CodegenState, CodegenState>)((st13) => ((Func<CodegenState, CodegenState>)((st14) => ((Func<CodegenState, CodegenState>)((st15) => st_append_text(st15, jmp((main_loop - (((long)st15.text.Count) + 5))))))(st_append_text(st14, add_rr(reg_rcx(), reg_rdx())))))(st_append_text(st13, mov_load(reg_rdx(), reg_r12(), 0)))))(patch_jcc_at(st12, copy_done_pos, ((long)st12.text.Count)))))(st_append_text(st11, jmp((copy_loop - (((long)st11.text.Count) + 5)))))))(st_append_text(st10, add_ri(reg_rsi(), 1)))))(st_append_text(st9, add_ri(reg_r15(), 1)))))(st_append_text(st8, mov_store_byte(reg_rdi(), reg_rax(), 8)))))(st_append_text(st7, add_rr(reg_rdi(), reg_r15())))))(st_append_text(st6, mov_rr(reg_rdi(), reg_r14())))))(st_append_text(st5, movzx_byte(reg_rax(), reg_rax(), 8)))))(st_append_text(st4, add_rr(reg_rax(), reg_rsi())))))(st_append_text(st3, mov_rr(reg_rax(), reg_r13())))))(st_append_text(st2, jcc(cc_ge(), 0)))))(((long)st2.text.Count))))(st_append_text(st1, cmp_rr(reg_rsi(), reg_rdx())))))(((long)st1.text.Count))))(st_append_text(st0, li(reg_rsi(), 0)))))(st_append_text(st, mov_load(reg_rdx(), reg_r13(), 0)));

    public static CodegenState emit_str_replace_no_match(CodegenState st, long mismatch_pos, long no_match_empty_pos, long cant_match_pos, long main_loop) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<CodegenState, CodegenState>)((st10) => st_append_text(st10, jmp((main_loop - (((long)st10.text.Count) + 5))))))(st_append_text(st9, add_ri(reg_rcx(), 1)))))(st_append_text(st8, add_ri(reg_r15(), 1)))))(st_append_text(st7, mov_store_byte(reg_rdi(), reg_rax(), 8)))))(st_append_text(st6, add_rr(reg_rdi(), reg_r15())))))(st_append_text(st5, mov_rr(reg_rdi(), reg_r14())))))(st_append_text(st4, movzx_byte(reg_rax(), reg_rax(), 8)))))(st_append_text(st3, add_rr(reg_rax(), reg_rcx())))))(st_append_text(st2, mov_rr(reg_rax(), reg_rbx())))))(patch_jcc_at(st1, cant_match_pos, ((long)st1.text.Count)))))(patch_jcc_at(st0, no_match_empty_pos, ((long)st0.text.Count)))))(patch_jcc_at(st, mismatch_pos, ((long)st.text.Count)));

    public static CodegenState emit_str_replace_done(CodegenState st, long done_pos) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => ((Func<CodegenState, CodegenState>)((st12) => st_append_text(st12, x86_ret())))(st_append_text(st11, pop_r(reg_rbx())))))(st_append_text(st10, pop_r(reg_r12())))))(st_append_text(st9, pop_r(reg_r13())))))(st_append_text(st8, pop_r(reg_r14())))))(st_append_text(st7, pop_r(reg_r15())))))(st_append_text(st6, mov_rr(reg_rax(), reg_r14())))))(st_append_text(st5, add_rr(reg_r10(), reg_rax())))))(st_append_text(st4, lea(reg_r10(), reg_r14(), 0)))))(st_append_text(st3, and_ri(reg_rax(), (0 - 8))))))(st_append_text(st2, add_ri(reg_rax(), 15)))))(st_append_text(st1, mov_rr(reg_rax(), reg_r15())))))(st_append_text(st0, mov_store(reg_r14(), reg_r15(), 0)))))(patch_jcc_at(st, done_pos, ((long)st.text.Count)));

    public static CodegenState emit_str_replace(CodegenState st) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<HelpResult4, CodegenState>)((head) => ((Func<HelpResult2, CodegenState>)((cmp) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => emit_str_replace_done(st2, head.p1)))(emit_str_replace_no_match(st1, cmp.p2, head.p3, head.p4, head.p2))))(emit_str_replace_copy_new(cmp.cg, head.p2))))(emit_str_replace_cmp(head.cg))))(emit_str_replace_main_head(st0))))(emit_str_replace_prologue(st));

    public static CodegenState emit_text_split_prologue(CodegenState st) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => ((Func<CodegenState, CodegenState>)((st12) => ((Func<CodegenState, CodegenState>)((st13) => ((Func<CodegenState, CodegenState>)((st14) => ((Func<CodegenState, CodegenState>)((st15) => st_append_text(st15, mov_rr(reg_rcx(), reg_r11()))))(st_append_text(st14, li(reg_r11(), 0)))))(st_append_text(st13, li(reg_r15(), 0)))))(st_append_text(st12, add_rr(reg_r10(), reg_rax())))))(st_append_text(st11, shl_ri(reg_rax(), 3)))))(st_append_text(st10, add_ri(reg_rax(), 2)))))(st_append_text(st9, mov_rr(reg_rax(), reg_r12())))))(st_append_text(st8, mov_rr(reg_r14(), reg_r10())))))(st_append_text(st7, movzx_byte(reg_r13(), reg_rsi(), 8)))))(st_append_text(st6, mov_load(reg_r12(), reg_rbx(), 0)))))(st_append_text(st5, mov_rr(reg_rbx(), reg_rdi())))))(st_append_text(st4, push_r(reg_r15())))))(st_append_text(st3, push_r(reg_r14())))))(st_append_text(st2, push_r(reg_r13())))))(st_append_text(st1, push_r(reg_r12())))))(st_append_text(st0, push_r(reg_rbx())))))(record_func_offset(st, "__text_split"));

    public static HelpResult2 emit_text_split_scan_head(CodegenState st) => ((Func<long, HelpResult2>)((scan_loop) => ((Func<CodegenState, HelpResult2>)((st0) => ((Func<long, HelpResult2>)((scan_done_pos) => ((Func<CodegenState, HelpResult2>)((st1) => ((Func<CodegenState, HelpResult2>)((st2) => ((Func<CodegenState, HelpResult2>)((st3) => ((Func<CodegenState, HelpResult2>)((st4) => ((Func<CodegenState, HelpResult2>)((st5) => ((Func<long, HelpResult2>)((not_delim_pos) => ((Func<CodegenState, HelpResult2>)((st6) => new HelpResult2(cg: st6, p1: scan_done_pos, p2: not_delim_pos)))(st_append_text(st5, jcc(cc_ne(), 0)))))(((long)st5.text.Count))))(st_append_text(st4, cmp_rr(reg_rax(), reg_r13())))))(st_append_text(st3, movzx_byte(reg_rax(), reg_rax(), 8)))))(st_append_text(st2, add_rr(reg_rax(), reg_r11())))))(st_append_text(st1, mov_rr(reg_rax(), reg_rbx())))))(st_append_text(st0, jcc(cc_ge(), 0)))))(((long)st0.text.Count))))(st_append_text(st, cmp_rr(reg_r11(), reg_r12())))))(((long)st.text.Count));

    public static CodegenState emit_text_split_emit_seg(CodegenState st, long scan_loop) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => st_append_text(st9, li(reg_rsi(), 0))))(st_append_text(st8, push_r(reg_r11())))))(st_append_text(st7, pop_r(reg_rax())))))(st_append_text(st6, add_rr(reg_r10(), reg_rax())))))(st_append_text(st5, and_ri(reg_rax(), (0 - 8))))))(st_append_text(st4, add_ri(reg_rax(), 15)))))(st_append_text(st3, push_r(reg_rax())))))(st_append_text(st2, mov_store(reg_rdi(), reg_rax(), 0)))))(st_append_text(st1, mov_rr(reg_rdi(), reg_r10())))))(st_append_text(st0, sub_rr(reg_rax(), reg_rcx())))))(st_append_text(st, mov_rr(reg_rax(), reg_r11())));

    public static CodegenState emit_text_split_seg_copy(CodegenState st, long scan_loop) => ((Func<long, CodegenState>)((seg_copy) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<long, CodegenState>)((seg_done_pos) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => ((Func<CodegenState, CodegenState>)((st12) => ((Func<CodegenState, CodegenState>)((st13) => ((Func<CodegenState, CodegenState>)((st14) => ((Func<CodegenState, CodegenState>)((st15) => ((Func<CodegenState, CodegenState>)((st16) => ((Func<CodegenState, CodegenState>)((st17) => ((Func<CodegenState, CodegenState>)((st18) => st_append_text(st18, mov_rr(reg_rcx(), reg_r11()))))(st_append_text(st17, add_ri(reg_r11(), 1)))))(st_append_text(st16, add_ri(reg_r15(), 1)))))(st_append_text(st15, mov_store(reg_rax(), reg_rdi(), 8)))))(st_append_text(st14, add_rr(reg_rax(), reg_r14())))))(st_append_text(st13, shl_ri(reg_rax(), 3)))))(st_append_text(st12, mov_rr(reg_rax(), reg_r15())))))(st_append_text(st11, pop_r(reg_r11())))))(patch_jcc_at(st10, seg_done_pos, ((long)st10.text.Count)))))(st_append_text(st9, jmp((seg_copy - (((long)st9.text.Count) + 5)))))))(st_append_text(st8, add_ri(reg_rsi(), 1)))))(st_append_text(st7, mov_store_byte(reg_r11(), reg_rdx(), 8)))))(st_append_text(st6, add_rr(reg_r11(), reg_rsi())))))(st_append_text(st5, mov_rr(reg_r11(), reg_rdi())))))(st_append_text(st4, movzx_byte(reg_rdx(), reg_rdx(), 8)))))(st_append_text(st3, add_rr(reg_rdx(), reg_rsi())))))(st_append_text(st2, add_rr(reg_rdx(), reg_rcx())))))(st_append_text(st1, mov_rr(reg_rdx(), reg_rbx())))))(st_append_text(st0, jcc(cc_ge(), 0)))))(((long)st0.text.Count))))(st_append_text(st, cmp_rr(reg_rsi(), reg_rax())))))(((long)st.text.Count));

    public static CodegenState emit_text_split_not_delim(CodegenState st, long not_delim_pos, long scan_loop) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => st_append_text(st2, jmp((scan_loop - (((long)st2.text.Count) + 5))))))(st_append_text(st1, add_ri(reg_r11(), 1)))))(patch_jcc_at(st0, not_delim_pos, ((long)st0.text.Count)))))(st_append_text(st, jmp((scan_loop - (((long)st.text.Count) + 5)))));

    public static CodegenState emit_text_split_final_seg(CodegenState st, long scan_done_pos) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => st_append_text(st8, pop_r(reg_rax()))))(st_append_text(st7, add_rr(reg_r10(), reg_rax())))))(st_append_text(st6, and_ri(reg_rax(), (0 - 8))))))(st_append_text(st5, add_ri(reg_rax(), 15)))))(st_append_text(st4, push_r(reg_rax())))))(st_append_text(st3, mov_store(reg_rdi(), reg_rax(), 0)))))(st_append_text(st2, mov_rr(reg_rdi(), reg_r10())))))(st_append_text(st1, sub_rr(reg_rax(), reg_rcx())))))(st_append_text(st0, mov_rr(reg_rax(), reg_r12())))))(patch_jcc_at(st, scan_done_pos, ((long)st.text.Count)));

    public static CodegenState emit_text_split_final_copy(CodegenState st) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<long, CodegenState>)((last_copy) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<long, CodegenState>)((last_done_pos) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => patch_jcc_at(st11, last_done_pos, ((long)st11.text.Count))))(st_append_text(st10, jmp((last_copy - (((long)st10.text.Count) + 5)))))))(st_append_text(st9, add_ri(reg_rsi(), 1)))))(st_append_text(st8, mov_store_byte(reg_r11(), reg_rdx(), 8)))))(st_append_text(st7, add_rr(reg_r11(), reg_rsi())))))(st_append_text(st6, mov_rr(reg_r11(), reg_rdi())))))(st_append_text(st5, movzx_byte(reg_rdx(), reg_rdx(), 8)))))(st_append_text(st4, add_rr(reg_rdx(), reg_rsi())))))(st_append_text(st3, add_rr(reg_rdx(), reg_rcx())))))(st_append_text(st2, mov_rr(reg_rdx(), reg_rbx())))))(st_append_text(st1, jcc(cc_ge(), 0)))))(((long)st1.text.Count))))(st_append_text(st0, cmp_rr(reg_rsi(), reg_rax())))))(((long)st0.text.Count))))(st_append_text(st, li(reg_rsi(), 0)));

    public static CodegenState emit_text_split_epilogue(CodegenState st) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => st_append_text(st11, x86_ret())))(st_append_text(st10, pop_r(reg_rbx())))))(st_append_text(st9, pop_r(reg_r12())))))(st_append_text(st8, pop_r(reg_r13())))))(st_append_text(st7, pop_r(reg_r14())))))(st_append_text(st6, pop_r(reg_r15())))))(st_append_text(st5, mov_rr(reg_rax(), reg_r14())))))(st_append_text(st4, mov_store(reg_r14(), reg_r15(), 0)))))(st_append_text(st3, add_ri(reg_r15(), 1)))))(st_append_text(st2, mov_store(reg_rax(), reg_rdi(), 8)))))(st_append_text(st1, add_rr(reg_rax(), reg_r14())))))(st_append_text(st0, shl_ri(reg_rax(), 3)))))(st_append_text(st, mov_rr(reg_rax(), reg_r15())));

    public static CodegenState emit_text_split(CodegenState st) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<HelpResult2, CodegenState>)((head) => ((Func<long, CodegenState>)((scan_loop) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => emit_text_split_epilogue(st5)))(emit_text_split_final_copy(st4))))(emit_text_split_final_seg(st3, head.p1))))(emit_text_split_not_delim(st2, head.p2, scan_loop))))(emit_text_split_seg_copy(st1, scan_loop))))(emit_text_split_emit_seg(head.cg, scan_loop))))(((long)st0.text.Count))))(emit_text_split_scan_head(st0))))(emit_text_split_prologue(st));

    public static CodegenState emit_runtime_helpers(CodegenState st) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => ((Func<CodegenState, CodegenState>)((st12) => ((Func<CodegenState, CodegenState>)((st13) => ((Func<CodegenState, CodegenState>)((st14) => emit_text_split(st14)))(emit_text_concat_list(st13))))(emit_list_contains(st12))))(emit_list_insert_at(st11))))(emit_list_append(st10))))(emit_list_cons(st9))))(emit_list_snoc(st8))))(emit_str_replace(st7))))(emit_text_compare(st6))))(emit_text_contains(st5))))(emit_text_starts_with(st4))))(emit_text_to_int(st3))))(emit_ipow(st2))))(emit_itoa(st1))))(emit_str_eq(st0))))(emit_str_concat(st));

    public static string t_nl_start() => "\u0001hello";

    public static string t_nl_mid() => "hel\u0001lo";

    public static string t_nl_end() => "hello\u0001";

    public static string t_nl_two_adjacent() => "\u0001\u0001";

    public static string t_nl_two_spaced() => "aaa\u0001bbb\u0001ccc";

    public static string t_nl_two_short() => "a\u0001b\u0001";

    public static string t_nl_two_start() => "\u0001\u0001hello";

    public static string t_nl_two_end() => "hello\u0001\u0001";

    public static string t_nl_three() => "a\u0001b\u0001c\u0001d";

    public static string t_nl_four() => "a\u0001b\u0001c\u0001d\u0001e";

    public static string t_nl_five() => "\u0001\u0001\u0001\u0001\u0001";

    public static string t_nl_after_space() => " \u0001ok";

    public static string t_nl_after_letter() => "x\u0001ok";

    public static string t_nl_after_digit() => "9\u0001ok";

    public static string t_nl_after_brace() => "{\u0001ok";

    public static string t_nl_after_close_brace() => "}\u0001ok";

    public static string t_nl_after_paren() => ")\u0001ok";

    public static string t_nl_after_comma() => ",\u0001ok";

    public static string t_nl_after_semicolon() => ";\u0001ok";

    public static string t_nl_after_backslash() => "\\\u0001ok";

    public static string t_nl_after_quote() => "\"\u0001ok";

    public static string t_dq_single() => "\"";

    public static string t_dq_two() => "\"\"";

    public static string t_dq_three() => "\"\"\"";

    public static string t_dq_in_text() => "say \"hello\" world";

    public static string t_dq_at_start() => "\"hello";

    public static string t_dq_at_end() => "hello\"";

    public static string t_bs_single() => "\\";

    public static string t_bs_two() => "\\\\";

    public static string t_bs_in_text() => "a\\b\\c";

    public static string t_mix_bs_nl() => "\\\u0001";

    public static string t_mix_nl_bs() => "\u0001\\";

    public static string t_mix_dq_nl() => "\"\u0001";

    public static string t_mix_nl_dq() => "\u0001\"";

    public static string t_mix_all() => "\\\"\u0001";

    public static string t_mix_all_rev() => "\u0001\"\\";

    public static string t_mix_dq_nl_dq() => "\"\u0001\"";

    public static string t_mix_repeat() => "\\\"\u0001\\\"\u0001";

    public static string t_real_braces() => ")\u0001    {\u0001";

    public static string t_real_while() => ")\u0001    {\u0001        while (true)\u0001        {\u0001";

    public static string t_real_closing() => "        }\u0001    }\u0001";

    public static string t_real_using() => "using System;\u0001using System.IO;\u0001";

    public static string t_real_multiline() => "    public static\u0001    {\u0001        while (true)\u0001        {\u0001            return\u0001        }\u0001    }\u0001";

    public static string t_fuzz_a() => "abcdef\u0001ghijkl";

    public static string t_fuzz_b() => "123456\u0001789012";

    public static string t_fuzz_c() => "+-=*/<>\u0001[]{}()";

    public static string t_fuzz_d() => "abc\u0001def\u0001ghi\u0001jkl\u0001mno";

    public static string t_fuzz_e() => "aaa\u0001.\u0001,\u0001!\u0001?\u0001aaa";

    public static string t_fuzz_f() => "ABCDEFGHIJ\u0001KLMNOPQRST";

    public static string t_fuzz_g() => "the quick brown\u0001fox jumps over\u0001the lazy dog";

    public static string t_fuzz_h() => "0123456789\u00010123456789\u00010123456789";

    public static string t_fuzz_i() => "({[<\u0001>]})";

    public static string t_fuzz_j() => "++==--**\u0001//@@##&&";

    public static string t_len1() => "\u0001";

    public static string t_len3() => "a\u0001b";

    public static string t_len7() => "abc\u0001def";

    public static string t_len8() => "abcd\u0001efg";

    public static string t_len9() => "abcd\u0001efgh";

    public static string t_len15() => "abcdefg\u0001hijklmn";

    public static string t_len16() => "abcdefgh\u0001ijklmno";

    public static string t_len17() => "abcdefgh\u0001ijklmnop";

    public static string t_len31() => "abcdefghijklmno\u0001pqrstuvwxyz0123";

    public static string t_len32() => "abcdefghijklmnop\u0001qrstuvwxyz01234";

    public static string t_align_nl_at_7() => "0123456\u0001rest";

    public static string t_align_nl_at_8() => "01234567\u0001rest";

    public static string t_align_nl_at_9() => "012345678\u0001rest";

    public static string t_align_nl_at_15() => "0123456789abcde\u0001rest";

    public static string t_align_nl_at_16() => "0123456789abcdef\u0001rest";

    public static string t_align_nl_at_17() => "0123456789abcdefg\u0001rest";

    public static string t_gap_0() => "\u0001\u0001";

    public static string t_gap_1() => "\u0001a\u0001";

    public static string t_gap_2() => "\u0001ab\u0001";

    public static string t_gap_3() => "\u0001abc\u0001";

    public static string t_gap_4() => "\u0001abcd\u0001";

    public static string t_gap_5() => "\u0001abcde\u0001";

    public static string t_gap_6() => "\u0001abcdef\u0001";

    public static string t_gap_7() => "\u0001abcdefg\u0001";

    public static string t_gap_8() => "\u0001abcdefgh\u0001";

    public static string t_gap_12() => "\u0001abcdefghijkl\u0001";

    public static string t_gap_16() => "\u0001abcdefghijklmnop\u0001";

    public static string t_pre_0() => "\u0001x\u0001";

    public static string t_pre_1() => "a\u0001x\u0001";

    public static string t_pre_2() => "ab\u0001x\u0001";

    public static string t_pre_3() => "abc\u0001x\u0001";

    public static string t_pre_4() => "abcd\u0001x\u0001";

    public static string t_pre_5() => "abcde\u0001x\u0001";

    public static string t_pre_6() => "abcdef\u0001x\u0001";

    public static string t_pre_7() => "abcdefg\u0001x\u0001";

    public static string t_pre_8() => "abcdefgh\u0001x\u0001";

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

    public static IRExpr lower_expr(AExpr e, CodexType ty, LowerCtx ctx) => e switch { ALitExpr(var text, var kind) => lower_literal(text, kind), ANameExpr(var name) => lower_name(name.value, ty, ctx), AApplyExpr(var f, var a) => lower_apply(f, a, ty, ctx), ABinaryExpr(var l, var op, var r) => ((Func<IRExpr, IRExpr>)((left_ir) => ((Func<CodexType, IRExpr>)((left_ty) => ((Func<IRExpr, IRExpr>)((right_ir) => new IrBinary(lower_bin_op(op, left_ty), left_ir, right_ir, binary_result_type(op, left_ty, ty))))(lower_expr(r, ty, ctx))))(ir_expr_type(left_ir))))(lower_expr(l, ty, ctx)), AUnaryExpr(var operand) => new IrNegate(lower_expr(operand, new IntegerTy(), ctx)), AIfExpr(var c, var t, var e2) => ((Func<CodexType, IRExpr>)((resolved) => ((Func<IRExpr, IRExpr>)((then_ir) => ((Func<CodexType, IRExpr>)((then_ty) => ((Func<CodexType, IRExpr>)((result_ty) => ((Func<IRExpr, IRExpr>)((else_ir) => new IrIf(lower_expr(c, new BooleanTy(), ctx), then_ir, else_ir, result_ty)))(lower_expr(e2, result_ty, ctx))))(resolved switch { ErrorTy { } => then_ty, _ => resolved, })))(ir_expr_type(then_ir))))(lower_expr(t, resolved, ctx))))(deep_resolve(ctx.ust, ty)), ALetExpr(var binds, var body) => lower_let(binds, body, ty, ctx), ALambdaExpr(var @params, var body) => lower_lambda(@params, body, ty, ctx), AMatchExpr(var scrut, var arms) => lower_match(scrut, arms, ty, ctx), AListExpr(var elems) => lower_list(elems, ty, ctx), ARecordExpr(var name, var fields) => lower_record(name, fields, ty, ctx), AFieldAccess(var rec, var field) => ((Func<IRExpr, IRExpr>)((rec_ir) => ((Func<CodexType, IRExpr>)((rec_ty) => ((Func<CodexType, IRExpr>)((field_ty) => ((Func<CodexType, IRExpr>)((actual_field_ty) => new IrFieldAccess(rec_ir, field.value, actual_field_ty)))(field_ty switch { ErrorTy { } => ty, _ => field_ty, })))(rec_ty switch { RecordTy(var rname, var rfields) => lookup_record_field(rfields, field.value), ConstructedTy(var cname, var cargs) => ((Func<CodexType, CodexType>)((ctor_raw) => ((Func<CodexType, CodexType>)((resolved_record) => resolved_record switch { RecordTy(var rn, var rf) => lookup_record_field(rf, field.value), _ => ty, }))(ctor_raw switch { ErrorTy { } => new ErrorTy(), _ => strip_fun_args_lower(deep_resolve(ctx.ust, ctor_raw)), })))(lookup_type(ctx.types, cname.value)), _ => ty, })))(deep_resolve(ctx.ust, ir_expr_type(rec_ir)))))(lower_expr(rec, new ErrorTy(), ctx)), ADoExpr(var stmts) => lower_do(stmts, ty, ctx), AHandleExpr(var eff, var body, var clauses) => lower_handle(eff, body, clauses, ty, ctx), AErrorExpr(var msg) => new IrError(msg, ty), _ => throw new InvalidOperationException("Non-exhaustive match"), };

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

    public static IRExpr lower_list(List<AExpr> elems, CodexType ty, LowerCtx ctx) => ((Func<CodexType, IRExpr>)((resolved) => ((Func<CodexType, IRExpr>)((elem_ty) => new IrList(lower_list_elems_loop(elems, elem_ty, ctx, 0, ((long)elems.Count)), elem_ty)))(resolved switch { ListTy(var e) => e, _ => ((((long)elems.Count) == 0) ? new ErrorTy() : ir_expr_type(lower_expr(elems[(int)0], new ErrorTy(), ctx))), })))(deep_resolve(ctx.ust, ty));

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

    public static IRChapter lower_chapter(AChapter m, List<TypeBinding> types, UnificationState ust) => ((Func<List<TypeBinding>, IRChapter>)((ctor_types) => ((Func<List<TypeBinding>, IRChapter>)((all_types) => new IRChapter(name: m.name, defs: lower_defs(m.defs, all_types, ust, 0), chapter_title: m.chapter_title, prose: m.prose, section_titles: m.section_titles)))(Enumerable.Concat(ctor_types, Enumerable.Concat(types, builtin_type_env().bindings).ToList()).ToList())))(collect_ctor_bindings(m.type_defs, 0, ((long)m.type_defs.Count), new List<TypeBinding>()));

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

    public static List<TypeBinding> ctor_bindings_for_typedef(ATypeDef td) => td switch { AVariantTypeDef(var name, var type_params, var ctors) => ((Func<CodexType, List<TypeBinding>>)((result_ty) => collect_variant_ctor_bindings(ctors, result_ty, 0, ((long)ctors.Count), new List<TypeBinding>())))(new ConstructedTy(name, new List<CodexType>())), ARecordTypeDef(var name, var type_params, var fields) => ((Func<Func<List<RecordField>, List<RecordField>>, List<TypeBinding>>)((resolved_fields) => ((Func<CodexType, List<TypeBinding>>)((result_ty) => ((Func<Func<long, CodexType>, List<TypeBinding>>)((ctor_ty) => new List<TypeBinding> { new TypeBinding(name: name.value, bound_type: ctor_ty) }))(build_record_ctor_type(fields, result_ty, 0, ((long)fields.Count)))))(new RecordTy(name, resolved_fields))))(build_record_fields(fields, 0, ((long)fields.Count), new List<object>())), _ => throw new InvalidOperationException("Non-exhaustive match"), };

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
            var ctor_ty = build_ctor_type(ctor.fields, result_ty, 0, ((long)ctor.fields.Count));
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

    public static Func<long, CodexType> build_ctor_type(List<TypeBinding> fields, List<ATypeExpr> result, CodexType i, long len) => ((i == len) ? result : ((Func<Func<long, CodexType>, CodexType>)((rest) => new FunTy(resolve_type_expr(fields[(int)i]), rest)))(build_ctor_type(fields, result, (i + 1), len)));

    public static Func<List<RecordField>, List<RecordField>> build_record_fields(List<TypeBinding> fields, List<ARecordFieldDef> i, long len, long acc)
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
            var rfield = new RecordField(name: f.name, type_val: resolve_type_expr(f.type_expr));
            var _tco_0 = fields;
            var _tco_1 = (i + 1);
            var _tco_2 = len;
            var _tco_3 = ((Func<List<object>>)(() => { var _l = acc; _l.Add(rfield); return _l; }))();
            fields = _tco_0;
            i = _tco_1;
            len = _tco_2;
            acc = _tco_3;
            continue;
            }
        }
    }

    public static Func<long, CodexType> build_record_ctor_type(List<TypeBinding> fields, List<ARecordFieldDef> result, CodexType i, long len) => ((i == len) ? result : ((Func<TypeBinding, CodexType>)((f) => ((Func<Func<long, CodexType>, CodexType>)((rest) => new FunTy(resolve_type_expr(f.type_expr), rest)))(build_record_ctor_type(fields, result, (i + 1), len))))(fields[(int)i]));

    public static Func<ATypeExpr, CodexType> resolve_type_expr(List<TypeBinding> texpr) => texpr switch { ANamedType(var name) => ((name.value == "Integer") ? new IntegerTy() : ((name.value == "Number") ? new NumberTy() : ((name.value == "Text") ? new TextTy() : ((name.value == "Boolean") ? new BooleanTy() : ((name.value == "Nothing") ? new NothingTy() : new ConstructedTy(name, new List<CodexType>())))))), AFunType(var param, var ret) => new FunTy(resolve_type_expr(param), resolve_type_expr(ret)), AAppType(var ctor, var args) => ctor switch { ANamedType(var cname) => ((cname.value == "List") ? ((((long)args.Count) == 1) ? new ListTy(resolve_type_expr(args[(int)0])) : new ListTy(new ErrorTy())) : new ConstructedTy(cname, new List<CodexType>())), _ => new ErrorTy(), }, _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static IRBinaryOp lower_bin_op(BinaryOp op, CodexType ty) => op switch { OpAdd { } => new IrAddInt(), OpSub { } => new IrSubInt(), OpMul { } => new IrMulInt(), OpDiv { } => new IrDivInt(), OpPow { } => new IrPowInt(), OpEq { } => new IrEq(), OpNotEq { } => new IrNotEq(), OpLt { } => new IrLt(), OpGt { } => new IrGt(), OpLtEq { } => new IrLtEq(), OpGtEq { } => new IrGtEq(), OpDefEq { } => new IrEq(), OpAppend { } => (is_text_type(ty) ? new IrAppendText() : new IrAppendList()), OpCons { } => new IrConsList(), OpAnd { } => new IrAnd(), OpOr { } => new IrOr(), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static CodexType binary_result_type(BinaryOp op, CodexType left_ty, CodexType expected_ty) => op switch { OpEq { } => new BooleanTy(), OpNotEq { } => new BooleanTy(), OpLt { } => new BooleanTy(), OpGt { } => new BooleanTy(), OpLtEq { } => new BooleanTy(), OpGtEq { } => new BooleanTy(), OpDefEq { } => new BooleanTy(), OpAnd { } => new BooleanTy(), OpOr { } => new BooleanTy(), OpAppend { } => (is_text_type(left_ty) ? new TextTy() : (is_text_type(expected_ty) ? new TextTy() : left_ty)), _ => left_ty, };

    public static List<ChapterAssignment> assign_chapters(List<DefHeader> headers, string current_chapter, long i) => ((i == ((long)headers.Count)) ? new List<ChapterAssignment>() : ((Func<DefHeader, List<ChapterAssignment>>)((hdr) => Enumerable.Concat(new List<ChapterAssignment> { new ChapterAssignment(def_name: hdr.name.text, chapter_slug: current_chapter) }, assign_chapters(headers, current_chapter, (i + 1))).ToList()))(headers[(int)i]));

    public static List<string> find_colliding_names(List<ChapterAssignment> assignments) => find_collisions_loop(assignments, 0, ((long)assignments.Count), new List<string>());

    public static List<string> find_collisions_loop(List<ChapterAssignment> assignments, long i, long len, List<string> acc)
    {
        while (true)
        {
            if ((i == len))
            {
            return acc;
            }
            else
            {
            var a = assignments[(int)i];
            if (appears_in_different_chapter(assignments, a.def_name, a.chapter_slug, 0, len))
            {
            if (list_contains(acc, a.def_name))
            {
            var _tco_0 = assignments;
            var _tco_1 = (i + 1);
            var _tco_2 = len;
            var _tco_3 = acc;
            assignments = _tco_0;
            i = _tco_1;
            len = _tco_2;
            acc = _tco_3;
            continue;
            }
            else
            {
            var _tco_0 = assignments;
            var _tco_1 = (i + 1);
            var _tco_2 = len;
            var _tco_3 = ((Func<List<string>>)(() => { var _l = acc; _l.Add(a.def_name); return _l; }))();
            assignments = _tco_0;
            i = _tco_1;
            len = _tco_2;
            acc = _tco_3;
            continue;
            }
            }
            else
            {
            var _tco_0 = assignments;
            var _tco_1 = (i + 1);
            var _tco_2 = len;
            var _tco_3 = acc;
            assignments = _tco_0;
            i = _tco_1;
            len = _tco_2;
            acc = _tco_3;
            continue;
            }
            }
        }
    }

    public static bool appears_in_different_chapter(List<ChapterAssignment> assignments, string name, string chapter_slug, long i, long len)
    {
        while (true)
        {
            if ((i == len))
            {
            return false;
            }
            else
            {
            var a = assignments[(int)i];
            if ((a.def_name == name))
            {
            if ((a.chapter_slug != chapter_slug))
            {
            return true;
            }
            else
            {
            var _tco_0 = assignments;
            var _tco_1 = name;
            var _tco_2 = chapter_slug;
            var _tco_3 = (i + 1);
            var _tco_4 = len;
            assignments = _tco_0;
            name = _tco_1;
            chapter_slug = _tco_2;
            i = _tco_3;
            len = _tco_4;
            continue;
            }
            }
            else
            {
            var _tco_0 = assignments;
            var _tco_1 = name;
            var _tco_2 = chapter_slug;
            var _tco_3 = (i + 1);
            var _tco_4 = len;
            assignments = _tco_0;
            name = _tco_1;
            chapter_slug = _tco_2;
            i = _tco_3;
            len = _tco_4;
            continue;
            }
            }
        }
    }

    public static string mangle_name(string chapter_slug, string def_name) => (chapter_slug + ("_" + def_name));

    public static string lookup_chapter(List<ChapterAssignment> assignments, string name) => lookup_chapter_loop(assignments, name, 0, ((long)assignments.Count));

    public static string lookup_chapter_loop(List<ChapterAssignment> assignments, string name, long i, long len)
    {
        while (true)
        {
            if ((i == len))
            {
            return "";
            }
            else
            {
            var a = assignments[(int)i];
            if ((a.def_name == name))
            {
            return a.chapter_slug;
            }
            else
            {
            var _tco_0 = assignments;
            var _tco_1 = name;
            var _tco_2 = (i + 1);
            var _tco_3 = len;
            assignments = _tco_0;
            name = _tco_1;
            i = _tco_2;
            len = _tco_3;
            continue;
            }
            }
        }
    }

    public static List<RenameEntry> build_rename_map(List<string> colliding, List<ChapterAssignment> assignments, string current_chapter) => build_rename_map_loop(colliding, assignments, current_chapter, 0, ((long)colliding.Count), new List<RenameEntry>());

    public static List<RenameEntry> build_rename_map_loop(List<string> colliding, List<ChapterAssignment> assignments, string current_chapter, long i, long len, List<RenameEntry> acc)
    {
        while (true)
        {
            if ((i == len))
            {
            return acc;
            }
            else
            {
            var name = colliding[(int)i];
            var slug = lookup_chapter(assignments, name);
            var entry = new RenameEntry(original: name, mangled: mangle_name(slug, name));
            var _tco_0 = colliding;
            var _tco_1 = assignments;
            var _tco_2 = current_chapter;
            var _tco_3 = (i + 1);
            var _tco_4 = len;
            var _tco_5 = ((Func<List<RenameEntry>>)(() => { var _l = acc; _l.Add(entry); return _l; }))();
            colliding = _tco_0;
            assignments = _tco_1;
            current_chapter = _tco_2;
            i = _tco_3;
            len = _tco_4;
            acc = _tco_5;
            continue;
            }
        }
    }

    public static string rename_lookup(List<RenameEntry> entries, string name) => rename_lookup_loop(entries, name, 0, ((long)entries.Count));

    public static string rename_lookup_loop(List<RenameEntry> entries, string name, long i, long len)
    {
        while (true)
        {
            if ((i == len))
            {
            return name;
            }
            else
            {
            var e = entries[(int)i];
            if ((e.original == name))
            {
            return e.mangled;
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

    public static bool is_colliding(List<string> names, string name) => list_contains(names, name);

    public static List<RenameEntry> remove_rename(List<RenameEntry> entries, string name) => remove_rename_loop(entries, name, 0, ((long)entries.Count), new List<RenameEntry>());

    public static List<RenameEntry> remove_rename_loop(List<RenameEntry> entries, string name, long i, long len, List<RenameEntry> acc)
    {
        while (true)
        {
            if ((i == len))
            {
            return acc;
            }
            else
            {
            var e = entries[(int)i];
            if ((e.original == name))
            {
            var _tco_0 = entries;
            var _tco_1 = name;
            var _tco_2 = (i + 1);
            var _tco_3 = len;
            var _tco_4 = acc;
            entries = _tco_0;
            name = _tco_1;
            i = _tco_2;
            len = _tco_3;
            acc = _tco_4;
            continue;
            }
            else
            {
            var _tco_0 = entries;
            var _tco_1 = name;
            var _tco_2 = (i + 1);
            var _tco_3 = len;
            var _tco_4 = ((Func<List<RenameEntry>>)(() => { var _l = acc; _l.Add(e); return _l; }))();
            entries = _tco_0;
            name = _tco_1;
            i = _tco_2;
            len = _tco_3;
            acc = _tco_4;
            continue;
            }
            }
        }
    }

    public static List<RenameEntry> remove_renames_for_params(List<RenameEntry> entries, List<IRParam> @params, long i)
    {
        while (true)
        {
            if ((i == ((long)@params.Count)))
            {
            return entries;
            }
            else
            {
            var p = @params[(int)i];
            var _tco_0 = remove_rename(entries, p.name);
            var _tco_1 = @params;
            var _tco_2 = (i + 1);
            entries = _tco_0;
            @params = _tco_1;
            i = _tco_2;
            continue;
            }
        }
    }

    public static IRExpr rename_ir_expr(List<RenameEntry> rn, IRExpr e) => e switch { IrIntLit(var v) => e, IrNumLit(var v) => e, IrTextLit(var v) => e, IrBoolLit(var v) => e, IrCharLit(var v) => e, IrError(var msg, var ty) => e, IrFork(var x, var ty) => new IrFork(rename_ir_expr(rn, x), ty), IrAwait(var x, var ty) => new IrAwait(rename_ir_expr(rn, x), ty), IrNegate(var x) => new IrNegate(rename_ir_expr(rn, x)), IrName(var n, var ty) => new IrName(rename_lookup(rn, n), ty), IrBinary(var op, var l, var r, var ty) => new IrBinary(op, rename_ir_expr(rn, l), rename_ir_expr(rn, r), ty), IrApply(var f, var a, var ty) => new IrApply(rename_ir_expr(rn, f), rename_ir_expr(rn, a), ty), IrIf(var c, var t, var el, var ty) => new IrIf(rename_ir_expr(rn, c), rename_ir_expr(rn, t), rename_ir_expr(rn, el), ty), IrFieldAccess(var rec, var field, var ty) => new IrFieldAccess(rename_ir_expr(rn, rec), field, ty), IrLet(var nm, var ty, var val, var body) => rename_ir_let(rn, nm, ty, val, body), IrLambda(var @params, var body, var ty) => rename_ir_lambda(rn, @params, body, ty), IrMatch(var scrut, var branches, var ty) => new IrMatch(rename_ir_expr(rn, scrut), rename_ir_branches(rn, branches, 0), ty), IrList(var elems, var ty) => new IrList(rename_ir_exprs(rn, elems, 0), ty), IrRecord(var nm, var fields, var ty) => new IrRecord(nm, rename_ir_fields(rn, fields, 0), ty), IrDo(var stmts, var ty) => new IrDo(rename_ir_do_stmts(rn, stmts, 0), ty), IrHandle(var eff, var body, var clauses, var ty) => new IrHandle(eff, rename_ir_expr(rn, body), rename_ir_handle_clauses(rn, clauses, 0), ty), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static IRExpr rename_ir_let(List<RenameEntry> rn, string nm, CodexType ty, IRExpr val, IRExpr body) => ((Func<List<RenameEntry>, IRExpr>)((rn2) => new IrLet(nm, ty, rename_ir_expr(rn, val), rename_ir_expr(rn2, body))))(remove_rename(rn, nm));

    public static IRExpr rename_ir_lambda(List<RenameEntry> rn, List<IRParam> @params, IRExpr body, CodexType ty) => ((Func<List<RenameEntry>, IRExpr>)((rn2) => new IrLambda(@params, rename_ir_expr(rn2, body), ty)))(remove_renames_for_params(rn, @params, 0));

    public static List<IRExpr> rename_ir_exprs(List<RenameEntry> rn, List<IRExpr> elems, long i) => ((i == ((long)elems.Count)) ? new List<IRExpr>() : Enumerable.Concat(new List<IRExpr> { rename_ir_expr(rn, elems[(int)i]) }, rename_ir_exprs(rn, elems, (i + 1))).ToList());

    public static List<IRFieldVal> rename_ir_fields(List<RenameEntry> rn, List<IRFieldVal> fields, long i) => ((i == ((long)fields.Count)) ? new List<IRFieldVal>() : ((Func<IRFieldVal, List<IRFieldVal>>)((f) => Enumerable.Concat(new List<IRFieldVal> { new IRFieldVal(name: f.name, value: rename_ir_expr(rn, f.value)) }, rename_ir_fields(rn, fields, (i + 1))).ToList()))(fields[(int)i]));

    public static List<IRBranch> rename_ir_branches(List<RenameEntry> rn, List<IRBranch> branches, long i) => ((i == ((long)branches.Count)) ? new List<IRBranch>() : ((Func<IRBranch, List<IRBranch>>)((b) => ((Func<List<RenameEntry>, List<IRBranch>>)((rn2) => Enumerable.Concat(new List<IRBranch> { new IRBranch(pattern: b.pattern, body: rename_ir_expr(rn2, b.body)) }, rename_ir_branches(rn, branches, (i + 1))).ToList()))(remove_renames_for_pat(rn, b.pattern))))(branches[(int)i]));

    public static List<RenameEntry> remove_renames_for_pat(List<RenameEntry> rn, IRPat p) => p switch { IrVarPat(var nm, var ty) => remove_rename(rn, nm), IrCtorPat(var nm, var subs, var ty) => remove_renames_for_pats(rn, subs, 0), IrLitPat(var v, var ty) => rn, IrWildPat { } => rn, _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static List<RenameEntry> remove_renames_for_pats(List<RenameEntry> rn, List<IRPat> pats, long i)
    {
        while (true)
        {
            if ((i == ((long)pats.Count)))
            {
            return rn;
            }
            else
            {
            var _tco_0 = remove_renames_for_pat(rn, pats[(int)i]);
            var _tco_1 = pats;
            var _tco_2 = (i + 1);
            rn = _tco_0;
            pats = _tco_1;
            i = _tco_2;
            continue;
            }
        }
    }

    public static List<IRDoStmt> rename_ir_do_stmts(List<RenameEntry> rn, List<IRDoStmt> stmts, long i) => ((i == ((long)stmts.Count)) ? new List<IRDoStmt>() : ((Func<IRDoStmt, List<IRDoStmt>>)((s) => s switch { IrDoBind(var nm, var ty, var expr) => ((Func<List<RenameEntry>, List<IRDoStmt>>)((rn2) => Enumerable.Concat(new List<IRDoStmt> { new IrDoBind(nm, ty, rename_ir_expr(rn, expr)) }, rename_ir_do_stmts(rn2, stmts, (i + 1))).ToList()))(remove_rename(rn, nm)), IrDoExec(var expr) => Enumerable.Concat(new List<IRDoStmt> { new IrDoExec(rename_ir_expr(rn, expr)) }, rename_ir_do_stmts(rn, stmts, (i + 1))).ToList(), _ => throw new InvalidOperationException("Non-exhaustive match"), }))(stmts[(int)i]));

    public static List<IRHandleClause> rename_ir_handle_clauses(List<RenameEntry> rn, List<IRHandleClause> clauses, long i) => ((i == ((long)clauses.Count)) ? new List<IRHandleClause>() : ((Func<IRHandleClause, List<IRHandleClause>>)((c) => ((Func<List<RenameEntry>, List<IRHandleClause>>)((rn2) => Enumerable.Concat(new List<IRHandleClause> { new IRHandleClause(op_name: c.op_name, resume_name: c.resume_name, body: rename_ir_expr(rn2, c.body)) }, rename_ir_handle_clauses(rn, clauses, (i + 1))).ToList()))(remove_rename(rn, c.resume_name))))(clauses[(int)i]));

    public static List<ChapterAssignment> build_all_assignments(List<DefHeader> headers, long i) => ((i == ((long)headers.Count)) ? new List<ChapterAssignment>() : ((Func<DefHeader, List<ChapterAssignment>>)((hdr) => Enumerable.Concat(new List<ChapterAssignment> { new ChapterAssignment(def_name: hdr.name.text, chapter_slug: hdr.chapter_slug) }, build_all_assignments(headers, (i + 1))).ToList()))(headers[(int)i]));

    public static string scope_def_name(List<string> colliding, List<ChapterAssignment> assignments, string name, string cur_chap) => (is_colliding(colliding, name) ? mangle_name(cur_chap, name) : name);

    public static List<RenameEntry> build_chapter_rename_map(List<string> colliding, List<ChapterAssignment> assignments, string cur_chap) => build_mod_renames(colliding, assignments, cur_chap, 0, ((long)assignments.Count), new List<RenameEntry>());

    public static List<RenameEntry> build_mod_renames(List<string> colliding, List<ChapterAssignment> assignments, string cur_chap, long i, long len, List<RenameEntry> acc)
    {
        while (true)
        {
            if ((i == len))
            {
            return acc;
            }
            else
            {
            var a = assignments[(int)i];
            if (is_colliding(colliding, a.def_name))
            {
            if (has_rename(acc, a.def_name))
            {
            var _tco_0 = colliding;
            var _tco_1 = assignments;
            var _tco_2 = cur_chap;
            var _tco_3 = (i + 1);
            var _tco_4 = len;
            var _tco_5 = acc;
            colliding = _tco_0;
            assignments = _tco_1;
            cur_chap = _tco_2;
            i = _tco_3;
            len = _tco_4;
            acc = _tco_5;
            continue;
            }
            else
            {
            if ((a.chapter_slug == cur_chap))
            {
            var _tco_0 = colliding;
            var _tco_1 = assignments;
            var _tco_2 = cur_chap;
            var _tco_3 = (i + 1);
            var _tco_4 = len;
            var _tco_5 = ((Func<List<RenameEntry>>)(() => { var _l = acc; _l.Add(new RenameEntry(original: a.def_name, mangled: mangle_name(cur_chap, a.def_name))); return _l; }))();
            colliding = _tco_0;
            assignments = _tco_1;
            cur_chap = _tco_2;
            i = _tco_3;
            len = _tco_4;
            acc = _tco_5;
            continue;
            }
            else
            {
            var _tco_0 = colliding;
            var _tco_1 = assignments;
            var _tco_2 = cur_chap;
            var _tco_3 = (i + 1);
            var _tco_4 = len;
            var _tco_5 = ((Func<List<RenameEntry>>)(() => { var _l = acc; _l.Add(new RenameEntry(original: a.def_name, mangled: mangle_name(a.chapter_slug, a.def_name))); return _l; }))();
            colliding = _tco_0;
            assignments = _tco_1;
            cur_chap = _tco_2;
            i = _tco_3;
            len = _tco_4;
            acc = _tco_5;
            continue;
            }
            }
            }
            else
            {
            var _tco_0 = colliding;
            var _tco_1 = assignments;
            var _tco_2 = cur_chap;
            var _tco_3 = (i + 1);
            var _tco_4 = len;
            var _tco_5 = acc;
            colliding = _tco_0;
            assignments = _tco_1;
            cur_chap = _tco_2;
            i = _tco_3;
            len = _tco_4;
            acc = _tco_5;
            continue;
            }
            }
        }
    }

    public static bool has_rename(List<RenameEntry> entries, string name) => has_rename_loop(entries, name, 0, ((long)entries.Count));

    public static bool has_rename_loop(List<RenameEntry> entries, string name, long i, long len)
    {
        while (true)
        {
            if ((i == len))
            {
            return false;
            }
            else
            {
            if ((entries[(int)i].original == name))
            {
            return true;
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

    public static ResolveResult resolve_chapter(AChapter mod) => resolve_chapter_with_citations(mod, new List<ResolveResult>());

    public static ResolveResult resolve_chapter_with_citations(AChapter mod, List<ResolveResult> imported) => ((Func<CollectResult, ResolveResult>)((top) => ((Func<CtorCollectResult, ResolveResult>)((ctors) => ((Func<List<string>, ResolveResult>)((cited_names) => ((Func<List<string>, ResolveResult>)((all_top) => ((Func<Scope, ResolveResult>)((sc) => ((Func<List<Diagnostic>, ResolveResult>)((expr_errs) => new ResolveResult(errors: Enumerable.Concat(top.errors, expr_errs).ToList(), top_level_names: top.names, type_names: ctors.type_names, ctor_names: ctors.ctor_names)))(resolve_all_defs(sc, mod.defs, 0, ((long)mod.defs.Count), new List<Diagnostic>()))))(build_all_names_scope(all_top, ctors.ctor_names, builtin_names()))))(Enumerable.Concat(top.names, cited_names).ToList())))(collect_cited_names(imported, 0, ((long)imported.Count), new List<string>()))))(collect_ctor_names(mod.type_defs, 0)(((long)mod.type_defs.Count))(new List<string>())(new List<string>()))))(collect_top_level_names(mod.defs, 0, ((long)mod.defs.Count), new List<string>(), new List<Diagnostic>()));

    public static List<string> collect_cited_names(List<ResolveResult> results, long i, long len, List<string> acc)
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

    public static long cc_newline() => 1;

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

    public static long cc_caret() => 94;

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
            var nc = ((long)source[(int)(offset + 1)]);
            if ((is_letter_code(nc) || is_digit_code(nc)))
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
            var _tco_3 = (acc + ((char)1).ToString());
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

    public static TokenKind classify_word(string w) => ((w == "let") ? new LetKeyword() : ((w == "in") ? new InKeyword() : ((w == "if") ? new IfKeyword() : ((w == "then") ? new ThenKeyword() : ((w == "else") ? new ElseKeyword() : ((w == "when") ? new WhenKeyword() : ((w == "where") ? new WhereKeyword() : ((w == "do") ? new DoKeyword() : ((w == "record") ? new RecordKeyword() : ((w == "cites") ? new CitesKeyword() : ((w == "claim") ? new ClaimKeyword() : ((w == "proof") ? new ProofKeyword() : ((w == "forall") ? new ForAllKeyword() : ((w == "exists") ? new ThereExistsKeyword() : ((w == "linear") ? new LinearKeyword() : ((w == "effect") ? new EffectKeyword() : ((w == "with") ? new WithKeyword() : ((w == "True") ? new TrueKeyword() : ((w == "False") ? new FalseKeyword() : ((Func<long, TokenKind>)((first_code) => ((first_code >= cc_upper_a()) ? ((first_code <= cc_upper_z()) ? new TypeIdentifier() : new Identifier()) : new Identifier())))(((long)w[(int)0])))))))))))))))))))));

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

    public static LexResult scan_operator(LexState s) => ((Func<long, LexResult>)((c) => ((Func<LexState, LexResult>)((next) => ((c == cc_left_paren()) ? new LexToken(make_token(new LeftParen(), "(", s), next) : ((c == cc_right_paren()) ? new LexToken(make_token(new RightParen(), ")", s), next) : ((c == cc_left_bracket()) ? new LexToken(make_token(new LeftBracket(), "[", s), next) : ((c == cc_right_bracket()) ? new LexToken(make_token(new RightBracket(), "]", s), next) : ((c == cc_left_brace()) ? new LexToken(make_token(new LeftBrace(), "{", s), next) : ((c == cc_right_brace()) ? new LexToken(make_token(new RightBrace(), "}", s), next) : ((c == cc_comma()) ? new LexToken(make_token(new Comma(), ",", s), next) : ((c == cc_dot()) ? new LexToken(make_token(new Dot(), ".", s), next) : ((c == cc_caret()) ? new LexToken(make_token(new Caret(), "^", s), next) : ((c == cc_ampersand()) ? new LexToken(make_token(new Ampersand(), "&", s), next) : ((c == cc_backslash()) ? new LexToken(make_token(new Backslash(), "\\", s), next) : scan_multi_char_operator(s))))))))))))))(advance_char(s))))(peek_code(s));

    public static LexResult scan_multi_char_operator(LexState s) => ((Func<long, LexResult>)((c) => ((Func<LexState, LexResult>)((next) => ((Func<long, LexResult>)((nc) => ((c == cc_plus()) ? ((nc == cc_plus()) ? new LexToken(make_token(new PlusPlus(), "++", s), advance_char(next)) : new LexToken(make_token(new Plus(), "+", s), next)) : ((c == cc_minus()) ? ((nc == cc_greater()) ? new LexToken(make_token(new Arrow(), "->", s), advance_char(next)) : new LexToken(make_token(new Minus(), "-", s), next)) : ((c == cc_star()) ? new LexToken(make_token(new Star(), "*", s), next) : ((c == cc_slash()) ? ((nc == cc_equals()) ? new LexToken(make_token(new NotEquals(), "/=", s), advance_char(next)) : new LexToken(make_token(new Slash(), "/", s), next)) : ((c == cc_equals()) ? ((nc == cc_equals()) ? ((Func<LexState, LexResult>)((next2) => ((Func<long, LexResult>)((nc2) => ((nc2 == cc_equals()) ? new LexToken(make_token(new TripleEquals(), "===", s), advance_char(next2)) : new LexToken(make_token(new DoubleEquals(), "==", s), next2))))((is_at_end(next2) ? 0 : peek_code(next2)))))(advance_char(next)) : new LexToken(make_token(new Equals_(), "=", s), next)) : ((c == cc_colon()) ? ((nc == cc_colon()) ? new LexToken(make_token(new ColonColon(), "::", s), advance_char(next)) : new LexToken(make_token(new Colon(), ":", s), next)) : ((c == cc_pipe()) ? ((nc == cc_minus()) ? new LexToken(make_token(new Turnstile(), "|-", s), advance_char(next)) : new LexToken(make_token(new Pipe(), "|", s), next)) : ((c == cc_less()) ? ((nc == cc_equals()) ? new LexToken(make_token(new LessOrEqual(), "<=", s), advance_char(next)) : ((nc == cc_minus()) ? new LexToken(make_token(new LeftArrow(), "<-", s), advance_char(next)) : new LexToken(make_token(new LessThan(), "<", s), next))) : ((c == cc_greater()) ? ((nc == cc_equals()) ? new LexToken(make_token(new GreaterOrEqual(), ">=", s), advance_char(next)) : new LexToken(make_token(new GreaterThan(), ">", s), next)) : new LexToken(make_token(new ErrorToken(), ((char)((long)s.source[(int)s.offset])).ToString(), s), next))))))))))))((is_at_end(next) ? 0 : peek_code(next)))))(advance_char(s))))(peek_code(s));

    public static LexResult scan_char_literal(LexState s) => ((Func<LexState, LexResult>)((s1) => (is_at_end(s1) ? new LexToken(make_token(new ErrorToken(), "'", s), s1) : ((peek_code(s1) == cc_backslash()) ? ((Func<LexState, LexResult>)((s2) => (is_at_end(s2) ? new LexToken(make_token(new ErrorToken(), "'\\", s), s2) : ((Func<long, LexResult>)((esc_code) => ((Func<long, LexResult>)((char_val) => ((Func<LexState, LexResult>)((s3) => ((Func<LexState, LexResult>)((s4) => new LexToken(make_token(new CharLiteral(), _Cce.FromUnicode(char_val.ToString()), s), s4)))((is_at_end(s3) ? s3 : ((peek_code(s3) == cc_single_quote()) ? advance_char(s3) : s3)))))(advance_char(s2))))(((esc_code == cc_lower_n()) ? cc_newline() : ((esc_code == cc_lower_t()) ? cc_space() : ((esc_code == cc_lower_r()) ? cc_newline() : ((esc_code == cc_backslash()) ? cc_backslash() : ((esc_code == cc_single_quote()) ? cc_single_quote() : esc_code))))))))(peek_code(s2)))))(advance_char(s1)) : ((Func<long, LexResult>)((char_val) => ((Func<LexState, LexResult>)((s2) => ((Func<LexState, LexResult>)((s3) => new LexToken(make_token(new CharLiteral(), _Cce.FromUnicode(char_val.ToString()), s), s3)))((is_at_end(s2) ? s2 : ((peek_code(s2) == cc_single_quote()) ? advance_char(s2) : s2)))))(advance_char(s1))))(peek_code(s1))))))(advance_char(s));

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

    public static Document parse_document(ParseState st) => ((Func<ParseState, Document>)((st2) => ((Func<ImportParseResult, Document>)((imp_result) => parse_top_level(new List<Def>(), new List<TypeDef>(), new List<EffectDef>(), imp_result.imports, "", new List<string>(), imp_result.state)))(parse_citations(st2, new List<CitesDecl>()))))(skip_newlines(st));

    public static ImportParseResult parse_citations(ParseState st, List<CitesDecl> acc)
    {
        while (true)
        {
            if (is_cites_keyword(current_kind(st)))
            {
            var st2 = advance(st);
            var name_tok = current(st2);
            var st3 = advance(st2);
            if (is_left_paren(current_kind(st3)))
            {
            var sel = parse_selected_names(advance(st3), new List<Token>());
            var st4 = skip_newlines(sel.state);
            var _tco_0 = st4;
            var _tco_1 = ((Func<List<CitesDecl>>)(() => { var _l = acc; _l.Add(new CitesDecl(chapter_name: name_tok, selected_names: sel.names)); return _l; }))();
            st = _tco_0;
            acc = _tco_1;
            continue;
            }
            else
            {
            var st4 = skip_newlines(st3);
            var _tco_0 = st4;
            var _tco_1 = ((Func<List<CitesDecl>>)(() => { var _l = acc; _l.Add(new CitesDecl(chapter_name: name_tok, selected_names: new List<Token>())); return _l; }))();
            st = _tco_0;
            acc = _tco_1;
            continue;
            }
            }
            else
            {
            return new ImportParseResult(imports: acc, state: st);
            }
        }
    }

    public static SelectedNamesResult parse_selected_names(ParseState st, List<Token> acc)
    {
        while (true)
        {
            if (is_ident(current_kind(st)))
            {
            var tok = current(st);
            var st2 = advance(st);
            if (is_comma(current_kind(st2)))
            {
            var _tco_0 = skip_newlines(advance(st2));
            var _tco_1 = ((Func<List<Token>>)(() => { var _l = acc; _l.Add(tok); return _l; }))();
            st = _tco_0;
            acc = _tco_1;
            continue;
            }
            else
            {
            return new SelectedNamesResult(names: ((Func<List<Token>>)(() => { var _l = acc; _l.Add(tok); return _l; }))(), state: st2);
            }
            }
            else
            {
            if (is_right_paren(current_kind(st)))
            {
            return new SelectedNamesResult(names: acc, state: advance(st));
            }
            else
            {
            return new SelectedNamesResult(names: acc, state: st);
            }
            }
        }
    }

    public static bool is_chapter_header(ParseState st) => (is_type_ident(current_kind(st)) ? ((current(st).text == "Chapter") ? is_colon(peek_kind(st, 1)) : false) : false);

    public static bool is_section_header(ParseState st) => (is_type_ident(current_kind(st)) ? ((current(st).text == "Section") ? is_colon(peek_kind(st, 1)) : false) : false);

    public static bool is_page_marker(ParseState st) => (is_type_ident(current_kind(st)) ? (current(st).text == "Page") : false);

    public static string extract_header_title(ParseState st) => ((Func<ParseState, string>)((st2) => (is_done(st2) ? "" : current(st2).text)))(advance(advance(st)));

    public static bool is_prose_line(ParseState st) => (current(st).column == 2);

    public static ParseState skip_prose_lines(ParseState st)
    {
        while (true)
        {
            var st2 = skip_newlines(st);
            if (is_done(st2))
            {
            return st2;
            }
            else
            {
            if (is_prose_line(st2))
            {
            var _tco_0 = skip_to_next_line(st2);
            st = _tco_0;
            continue;
            }
            else
            {
            return st2;
            }
            }
        }
    }

    public static ParseState skip_to_next_line(ParseState st)
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
            return advance(st);
            }
            {
            var _tco_0 = advance(st);
            st = _tco_0;
            continue;
            }
            }
        }
    }

    public static Document parse_top_level(List<Def> defs, List<TypeDef> type_defs, List<EffectDef> effect_defs, List<CitesDecl> imports, string ch_title, List<string> sec_titles, ParseState st)
    {
        while (true)
        {
            if (is_done(st))
            {
            return new Document(defs: defs, type_defs: type_defs, effect_defs: effect_defs, citations: imports, chapter_title: ch_title, section_titles: sec_titles);
            }
            else
            {
            if (is_chapter_header(st))
            {
            var title = extract_header_title(st);
            var _tco_0 = defs;
            var _tco_1 = type_defs;
            var _tco_2 = effect_defs;
            var _tco_3 = imports;
            var _tco_4 = title;
            var _tco_5 = sec_titles;
            var _tco_6 = skip_prose_lines(skip_to_next_line(st));
            defs = _tco_0;
            type_defs = _tco_1;
            effect_defs = _tco_2;
            imports = _tco_3;
            ch_title = _tco_4;
            sec_titles = _tco_5;
            st = _tco_6;
            continue;
            }
            else
            {
            if (is_section_header(st))
            {
            var title = extract_header_title(st);
            var _tco_0 = defs;
            var _tco_1 = type_defs;
            var _tco_2 = effect_defs;
            var _tco_3 = imports;
            var _tco_4 = ch_title;
            var _tco_5 = Enumerable.Concat(sec_titles, new List<string> { title }).ToList();
            var _tco_6 = skip_prose_lines(skip_to_next_line(st));
            defs = _tco_0;
            type_defs = _tco_1;
            effect_defs = _tco_2;
            imports = _tco_3;
            ch_title = _tco_4;
            sec_titles = _tco_5;
            st = _tco_6;
            continue;
            }
            else
            {
            if (is_page_marker(st))
            {
            var _tco_0 = defs;
            var _tco_1 = type_defs;
            var _tco_2 = effect_defs;
            var _tco_3 = imports;
            var _tco_4 = ch_title;
            var _tco_5 = sec_titles;
            var _tco_6 = skip_to_next_line(st);
            defs = _tco_0;
            type_defs = _tco_1;
            effect_defs = _tco_2;
            imports = _tco_3;
            ch_title = _tco_4;
            sec_titles = _tco_5;
            st = _tco_6;
            continue;
            }
            else
            {
            if (is_cites_keyword(current_kind(st)))
            {
            var cite_result = parse_citations(st, new List<CitesDecl>());
            var _tco_0 = defs;
            var _tco_1 = type_defs;
            var _tco_2 = effect_defs;
            var _tco_3 = Enumerable.Concat(imports, cite_result.imports).ToList();
            var _tco_4 = ch_title;
            var _tco_5 = sec_titles;
            var _tco_6 = cite_result.state;
            defs = _tco_0;
            type_defs = _tco_1;
            effect_defs = _tco_2;
            imports = _tco_3;
            ch_title = _tco_4;
            sec_titles = _tco_5;
            st = _tco_6;
            continue;
            }
            else
            {
            if (is_effect_keyword(current_kind(st)))
            {
            return parse_top_level_effect(defs, type_defs, effect_defs, imports, ch_title, sec_titles, st);
            }
            else
            {
            if (is_prose_line(st))
            {
            var _tco_0 = defs;
            var _tco_1 = type_defs;
            var _tco_2 = effect_defs;
            var _tco_3 = imports;
            var _tco_4 = ch_title;
            var _tco_5 = sec_titles;
            var _tco_6 = skip_prose_lines(st);
            defs = _tco_0;
            type_defs = _tco_1;
            effect_defs = _tco_2;
            imports = _tco_3;
            ch_title = _tco_4;
            sec_titles = _tco_5;
            st = _tco_6;
            continue;
            }
            else
            {
            return try_top_level_type_def(defs, type_defs, effect_defs, imports, ch_title, sec_titles, st);
            }
            }
            }
            }
            }
            }
            }
        }
    }

    public static Document parse_top_level_effect(List<Def> defs, List<TypeDef> type_defs, List<EffectDef> effect_defs, List<CitesDecl> imports, string ch_title, List<string> sec_titles, ParseState st) => ((Func<ParseState, Document>)((st1) => ((Func<Token, Document>)((name_tok) => ((Func<ParseState, Document>)((st2) => ((Func<ParseState, Document>)((st3) => ((Func<EffectOpsResult, Document>)((ops) => ((Func<EffectDef, Document>)((ed) => parse_top_level(defs, type_defs, Enumerable.Concat(effect_defs, new List<EffectDef> { ed }).ToList(), imports, ch_title, sec_titles, skip_newlines(ops.state))))(new EffectDef(name: name_tok, ops: ops.ops))))(parse_effect_ops(st3, new List<EffectOpDef>()))))((is_where_keyword(current_kind(st2)) ? skip_newlines(advance(st2)) : st2))))(advance(st1))))(current(st1))))(advance(st));

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

    public static Document try_top_level_type_def(List<Def> defs, List<TypeDef> type_defs, List<EffectDef> effect_defs, List<CitesDecl> imports, string ch_title, List<string> sec_titles, ParseState st) => ((Func<ParseTypeDefResult, Document>)((td_result) => td_result switch { TypeDefOk(var td, var st2) => parse_top_level(defs, Enumerable.Concat(type_defs, new List<TypeDef> { td }).ToList(), effect_defs, imports, ch_title, sec_titles, skip_newlines(st2)), TypeDefNone(var st2) => try_top_level_def(defs, type_defs, effect_defs, imports, ch_title, sec_titles, st), _ => throw new InvalidOperationException("Non-exhaustive match"), }))(parse_type_def(st));

    public static Document try_top_level_def(List<Def> defs, List<TypeDef> type_defs, List<EffectDef> effect_defs, List<CitesDecl> imports, string ch_title, List<string> sec_titles, ParseState st) => ((Func<ParseDefResult, Document>)((def_result) => def_result switch { DefOk(var d, var st2) => parse_top_level(Enumerable.Concat(defs, new List<Def> { d }).ToList(), type_defs, effect_defs, imports, ch_title, sec_titles, skip_newlines(st2)), DefNone(var st2) => parse_top_level(defs, type_defs, effect_defs, imports, ch_title, sec_titles, skip_newlines(advance(st2))), _ => throw new InvalidOperationException("Non-exhaustive match"), }))(parse_definition(st));

    public static ScanResult scan_document(ParseState st) => ((Func<ParseState, ScanResult>)((st2) => ((Func<ImportParseResult, ScanResult>)((imp_result) => scan_top_level(new List<DefHeader>(), new List<TypeDef>(), new List<EffectDef>(), imp_result.imports, "", "", new List<string>(), imp_result.state)))(parse_citations(st2, new List<CitesDecl>()))))(skip_newlines(st));

    public static ScanResult scan_top_level(List<DefHeader> headers, List<TypeDef> type_defs, List<EffectDef> effect_defs, List<CitesDecl> imports, string cur_chap, string ch_title, List<string> sec_titles, ParseState st)
    {
        while (true)
        {
            if (is_done(st))
            {
            return new ScanResult(type_defs: type_defs, effect_defs: effect_defs, def_headers: headers, citations: imports, chapter_title: ch_title, section_titles: sec_titles);
            }
            else
            {
            if (is_chapter_header(st))
            {
            var title = extract_header_title(st);
            var _tco_0 = headers;
            var _tco_1 = type_defs;
            var _tco_2 = effect_defs;
            var _tco_3 = imports;
            var _tco_4 = cur_chap;
            var _tco_5 = title;
            var _tco_6 = sec_titles;
            var _tco_7 = skip_prose_lines(skip_to_next_line(st));
            headers = _tco_0;
            type_defs = _tco_1;
            effect_defs = _tco_2;
            imports = _tco_3;
            cur_chap = _tco_4;
            ch_title = _tco_5;
            sec_titles = _tco_6;
            st = _tco_7;
            continue;
            }
            else
            {
            if (is_section_header(st))
            {
            var title = extract_header_title(st);
            var _tco_0 = headers;
            var _tco_1 = type_defs;
            var _tco_2 = effect_defs;
            var _tco_3 = imports;
            var _tco_4 = cur_chap;
            var _tco_5 = ch_title;
            var _tco_6 = Enumerable.Concat(sec_titles, new List<string> { title }).ToList();
            var _tco_7 = skip_prose_lines(skip_to_next_line(st));
            headers = _tco_0;
            type_defs = _tco_1;
            effect_defs = _tco_2;
            imports = _tco_3;
            cur_chap = _tco_4;
            ch_title = _tco_5;
            sec_titles = _tco_6;
            st = _tco_7;
            continue;
            }
            else
            {
            if (is_page_marker(st))
            {
            var _tco_0 = headers;
            var _tco_1 = type_defs;
            var _tco_2 = effect_defs;
            var _tco_3 = imports;
            var _tco_4 = cur_chap;
            var _tco_5 = ch_title;
            var _tco_6 = sec_titles;
            var _tco_7 = skip_to_next_line(st);
            headers = _tco_0;
            type_defs = _tco_1;
            effect_defs = _tco_2;
            imports = _tco_3;
            cur_chap = _tco_4;
            ch_title = _tco_5;
            sec_titles = _tco_6;
            st = _tco_7;
            continue;
            }
            else
            {
            if (is_cites_keyword(current_kind(st)))
            {
            var cite_result = parse_citations(st, new List<CitesDecl>());
            var _tco_0 = headers;
            var _tco_1 = type_defs;
            var _tco_2 = effect_defs;
            var _tco_3 = Enumerable.Concat(imports, cite_result.imports).ToList();
            var _tco_4 = cur_chap;
            var _tco_5 = ch_title;
            var _tco_6 = sec_titles;
            var _tco_7 = cite_result.state;
            headers = _tco_0;
            type_defs = _tco_1;
            effect_defs = _tco_2;
            imports = _tco_3;
            cur_chap = _tco_4;
            ch_title = _tco_5;
            sec_titles = _tco_6;
            st = _tco_7;
            continue;
            }
            else
            {
            if (is_effect_keyword(current_kind(st)))
            {
            return scan_top_level_effect(headers, type_defs, effect_defs, imports, cur_chap, ch_title, sec_titles, st);
            }
            else
            {
            if (is_prose_line(st))
            {
            var _tco_0 = headers;
            var _tco_1 = type_defs;
            var _tco_2 = effect_defs;
            var _tco_3 = imports;
            var _tco_4 = cur_chap;
            var _tco_5 = ch_title;
            var _tco_6 = sec_titles;
            var _tco_7 = skip_prose_lines(st);
            headers = _tco_0;
            type_defs = _tco_1;
            effect_defs = _tco_2;
            imports = _tco_3;
            cur_chap = _tco_4;
            ch_title = _tco_5;
            sec_titles = _tco_6;
            st = _tco_7;
            continue;
            }
            else
            {
            return try_scan_type_def(headers, type_defs, effect_defs, imports, cur_chap, ch_title, sec_titles, st);
            }
            }
            }
            }
            }
            }
            }
        }
    }

    public static ScanResult scan_top_level_effect(List<DefHeader> headers, List<TypeDef> type_defs, List<EffectDef> effect_defs, List<CitesDecl> imports, string cur_chap, string ch_title, List<string> sec_titles, ParseState st) => ((Func<ParseState, ScanResult>)((st1) => ((Func<Token, ScanResult>)((name_tok) => ((Func<ParseState, ScanResult>)((st2) => ((Func<ParseState, ScanResult>)((st3) => ((Func<EffectOpsResult, ScanResult>)((ops) => ((Func<EffectDef, ScanResult>)((ed) => scan_top_level(headers, type_defs, Enumerable.Concat(effect_defs, new List<EffectDef> { ed }).ToList(), imports, cur_chap, ch_title, sec_titles, skip_newlines(ops.state))))(new EffectDef(name: name_tok, ops: ops.ops))))(parse_effect_ops(st3, new List<EffectOpDef>()))))((is_where_keyword(current_kind(st2)) ? skip_newlines(advance(st2)) : st2))))(advance(st1))))(current(st1))))(advance(st));

    public static ScanResult try_scan_type_def(List<DefHeader> headers, List<TypeDef> type_defs, List<EffectDef> effect_defs, List<CitesDecl> imports, string cur_chap, string ch_title, List<string> sec_titles, ParseState st) => ((Func<ParseTypeDefResult, ScanResult>)((td_result) => td_result switch { TypeDefOk(var td, var st2) => scan_top_level(headers, Enumerable.Concat(type_defs, new List<TypeDef> { td }).ToList(), effect_defs, imports, cur_chap, ch_title, sec_titles, skip_newlines(st2)), TypeDefNone(var st2) => try_scan_def_header(headers, type_defs, effect_defs, imports, cur_chap, ch_title, sec_titles, st), _ => throw new InvalidOperationException("Non-exhaustive match"), }))(parse_type_def(st));

    public static ScanResult try_scan_def_header(List<DefHeader> headers, List<TypeDef> type_defs, List<EffectDef> effect_defs, List<CitesDecl> imports, string cur_chap, string ch_title, List<string> sec_titles, ParseState st) => ((Func<ScanDefResult, ScanResult>)((hdr_result) => hdr_result switch { DefHeaderOk(var hdr, var st2) => scan_top_level(Enumerable.Concat(headers, new List<DefHeader> { hdr }).ToList(), type_defs, effect_defs, imports, cur_chap, ch_title, sec_titles, skip_newlines(st2)), DefHeaderNone(var st2) => scan_top_level(headers, type_defs, effect_defs, imports, cur_chap, ch_title, sec_titles, skip_newlines(advance(st2))), _ => throw new InvalidOperationException("Non-exhaustive match"), }))(scan_definition(cur_chap, st));

    public static ScanDefResult scan_definition(string cur_chap, ParseState st) => (is_done(st) ? new DefHeaderNone(st) : (is_ident(current_kind(st)) ? try_scan_def(cur_chap, st) : (is_type_ident(current_kind(st)) ? try_scan_def(cur_chap, st) : new DefHeaderNone(st))));

    public static ScanDefResult try_scan_def(string cur_chap, ParseState st) => (is_colon(peek_kind(st, 1)) ? ((Func<ParseTypeResult, ScanDefResult>)((ann_result) => unwrap_type_for_scan(cur_chap, ann_result)))(parse_type_annotation(st)) : scan_def_body_with_ann(cur_chap, new List<TypeAnn>(), st));

    public static ScanDefResult unwrap_type_for_scan(string cur_chap, ParseTypeResult r) => r switch { TypeOk(var ann_type, var st) => ((Func<Token, ScanDefResult>)((name_tok) => ((Func<List<TypeAnn>, ScanDefResult>)((ann) => ((Func<ParseState, ScanDefResult>)((st2) => scan_def_body_with_ann(cur_chap, ann, st2)))(skip_newlines(st))))(new List<TypeAnn> { new TypeAnn(name: name_tok, type_expr: ann_type) })))(new Token(kind: new Identifier(), text: "", offset: 0, line: 0, column: 0)), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static ScanDefResult scan_def_body_with_ann(string cur_chap, List<TypeAnn> ann, ParseState st) => ((Func<Token, ScanDefResult>)((name_tok) => ((Func<ParseState, ScanDefResult>)((st2) => scan_def_params_then(cur_chap, ann, name_tok, new List<Token>(), st2)))(advance(st))))(current(st));

    public static ScanDefResult scan_def_params_then(string cur_chap, List<TypeAnn> ann, Token name_tok, List<Token> acc, ParseState st)
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
            var _tco_0 = cur_chap;
            var _tco_1 = ann;
            var _tco_2 = name_tok;
            var _tco_3 = ((Func<List<Token>>)(() => { var _l = acc; _l.Add(param); return _l; }))();
            var _tco_4 = st4;
            cur_chap = _tco_0;
            ann = _tco_1;
            name_tok = _tco_2;
            acc = _tco_3;
            st = _tco_4;
            continue;
            }
            else
            {
            return finish_def_scan(cur_chap, ann, name_tok, acc, st);
            }
            }
            else
            {
            return finish_def_scan(cur_chap, ann, name_tok, acc, st);
            }
        }
    }

    public static ScanDefResult finish_def_scan(string cur_chap, List<TypeAnn> ann, Token name_tok, List<Token> @params, ParseState st) => ((Func<ParseState, ScanDefResult>)((st2) => ((Func<ParseState, ScanDefResult>)((st3) => ((Func<long, ScanDefResult>)((body_pos) => ((Func<ParseState, ScanDefResult>)((st4) => new DefHeaderOk(new DefHeader(name: name_tok, @params: @params, ann: ann, body_pos: body_pos, chapter_slug: cur_chap), st4)))(skip_body_tokens(st3, name_tok.column))))(st3.pos)))(skip_newlines(st2))))(expect(new Equals_(), st));

    public static ParseState skip_body_tokens(ParseState st, long name_col)
    {
        while (true)
        {
            if (is_done(st))
            {
            return st;
            }
            else
            {
            var tok = current(st);
            var _tco_s = tok.kind;
            if (_tco_s is Newline _tco_m0)
            {
            var _tco_0 = advance(st);
            var _tco_1 = name_col;
            st = _tco_0;
            name_col = _tco_1;
            continue;
            }
            else if (_tco_s is Indent _tco_m1)
            {
            var _tco_0 = advance(st);
            var _tco_1 = name_col;
            st = _tco_0;
            name_col = _tco_1;
            continue;
            }
            else if (_tco_s is Dedent _tco_m2)
            {
            var _tco_0 = advance(st);
            var _tco_1 = name_col;
            st = _tco_0;
            name_col = _tco_1;
            continue;
            }
            {
            if ((tok.column <= name_col))
            {
            return st;
            }
            else
            {
            var _tco_0 = advance(st);
            var _tco_1 = name_col;
            st = _tco_0;
            name_col = _tco_1;
            continue;
            }
            }
            }
        }
    }

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

    public static bool is_cites_keyword(TokenKind k) => k switch { CitesKeyword { } => true, _ => false, };

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

    public static ParseExprResult parse_binary_loop(Expr left, ParseState st, long min_prec) => ((Func<ParseState, ParseExprResult>)((st1) => (is_done(st1) ? new ExprOk(left, st1) : ((Func<long, ParseExprResult>)((prec) => ((prec < min_prec) ? new ExprOk(left, st1) : ((Func<Token, ParseExprResult>)((op) => ((Func<ParseState, ParseExprResult>)((st2) => ((Func<long, ParseExprResult>)((next_min) => ((Func<ParseExprResult, ParseExprResult>)((right_result) => unwrap_expr_ok(right_result, (_p0_) => (_p1_) => continue_binary(left, op, min_prec, _p0_, _p1_))))(parse_binary(st2, next_min))))((is_right_assoc(op.kind) ? prec : (prec + 1)))))(skip_newlines(advance(st1)))))(current(st1)))))(operator_precedence(current_kind(st1))))))(skip_newlines(st));

    public static ParseExprResult continue_binary(Expr left, Token op, long min_prec, Expr right, ParseState st) => parse_binary_loop(new BinExpr(left, op, right), st, min_prec);

    public static ParseExprResult parse_unary(ParseState st) => (is_minus(current_kind(st)) ? ((Func<Token, ParseExprResult>)((op) => ((Func<ParseExprResult, ParseExprResult>)((result) => unwrap_expr_ok(result, (_p0_) => (_p1_) => finish_unary(op, _p0_, _p1_))))(parse_unary(advance(st)))))(current(st)) : parse_application(st));

    public static ParseExprResult finish_unary(Token op, Expr operand, ParseState st) => new ExprOk(new UnaryExpr(op, operand), st);

    public static ParseExprResult parse_application(ParseState st) => ((Func<ParseExprResult, ParseExprResult>)((func_result) => unwrap_expr_ok(func_result, (_p0_) => (_p1_) => parse_app_loop(_p0_, _p1_))))(parse_atom(st));

    public static ParseExprResult parse_app_loop(Expr func, ParseState st) => (is_compound(func) ? parse_dot_only(func, st) : (is_done(st) ? new ExprOk(func, st) : (is_app_start(current_kind(st)) ? ((Func<ParseExprResult, ParseExprResult>)((arg_result) => unwrap_expr_ok(arg_result, (_p0_) => (_p1_) => continue_app(func, _p0_, _p1_))))(parse_atom(st)) : parse_field_access(func, st))));

    public static ParseExprResult continue_app(Expr func, Expr arg, ParseState st) => parse_app_loop(new AppExpr(func, arg), st);

    public static ParseExprResult parse_atom(ParseState st) => (is_literal(current_kind(st)) ? new ExprOk(new LitExpr(current(st)), advance(st)) : (is_ident(current_kind(st)) ? new ExprOk(new NameExpr(current(st)), advance(st)) : (is_type_ident(current_kind(st)) ? parse_atom_type_ident(st) : (is_left_paren(current_kind(st)) ? parse_paren_expr(advance(st)) : (is_left_bracket(current_kind(st)) ? parse_list_expr(st) : (is_if_keyword(current_kind(st)) ? parse_if_expr(st) : (is_let_keyword(current_kind(st)) ? parse_let_expr(st) : (is_when_keyword(current_kind(st)) ? parse_match_expr(st) : (is_do_keyword(current_kind(st)) ? parse_do_expr(st) : (is_with_keyword(current_kind(st)) ? parse_handle_expr(st) : (is_backslash(current_kind(st)) ? parse_lambda_expr(st) : new ExprOk(new ErrExpr(current(st)), advance(st)))))))))))));

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
            return new FunTy(resolve_type_expr(tdm)(param), resolve_type_expr(tdm)(ret));
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

    public static CodexType resolve_applied_type(List<TypeBinding> tdm, ATypeExpr ctor, List<ATypeExpr> args) => ctor switch { ANamedType(var name) => ((name.value == "List") ? ((((long)args.Count) == 1) ? new ListTy(resolve_type_expr(tdm)(args[(int)0])) : new ListTy(new ErrorTy())) : new ConstructedTy(name, resolve_type_expr_list(tdm, args, 0, ((long)args.Count), new List<CodexType>()))), _ => resolve_type_expr(tdm)(ctor), };

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
            var _tco_4 = ((Func<List<CodexType>>)(() => { var _l = acc; _l.Add(resolve_type_expr(tdm)(args[(int)i])); return _l; }))();
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

    public static ChapterResult check_chapter(AChapter mod) => ((Func<List<TypeBinding>, ChapterResult>)((tdm) => ((Func<LetBindResult, ChapterResult>)((tenv) => ((Func<LetBindResult, ChapterResult>)((env) => check_all_defs(env.state, env.env, mod.defs, 0, ((long)mod.defs.Count), new List<TypeBinding>())))(register_all_defs(tenv.state, tenv.env, tdm, mod.defs, 0, ((long)mod.defs.Count)))))(register_type_defs(empty_unification_state(), builtin_type_env(), tdm, mod.type_defs, 0, ((long)mod.type_defs.Count)))))(build_type_def_map(mod.type_defs, 0, ((long)mod.type_defs.Count), new List<TypeBinding>()));

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
            var ty = ((((long)def.declared_type.Count) == 0) ? ((Func<FreshResult, LetBindResult>)((fr) => ((Func<TypeEnv, LetBindResult>)((env2) => new LetBindResult(state: fr.state, env: env2)))(env_bind(env, def.name.value, fr.var_type))))(fresh_and_advance(st)) : ((Func<CodexType, LetBindResult>)((resolved) => ((Func<ParamResult, LetBindResult>)((pr) => new LetBindResult(state: pr.state, env: env_bind(env, def.name.value, pr.parameterized))))(parameterize_type(st, resolved))))(resolve_type_expr(tdm)(def.declared_type[(int)0])));
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

    public static ChapterResult check_all_defs(UnificationState st, TypeEnv env, List<ADef> defs, long i, long len, List<TypeBinding> acc)
    {
        while (true)
        {
            if ((i == len))
            {
            return new ChapterResult(types: acc, state: st);
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
            var rfield = new RecordField(name: f.name, type_val: resolve_type_expr(partial_tdm)(f.type_expr));
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
            var _tco_4 = ((Func<List<CodexType>>)(() => { var _l = acc; _l.Add(resolve_type_expr(partial_tdm)(args[(int)i])); return _l; }))();
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

    public static LetBindResult register_one_type_def(UnificationState st, TypeEnv env, List<TypeBinding> tdm, ATypeDef td) => td switch { AVariantTypeDef(var name, var type_params, var ctors) => ((Func<CodexType, LetBindResult>)((result_ty) => register_variant_ctors(st, env, tdm, ctors, result_ty, 0, ((long)ctors.Count))))(lookup_type_def(tdm, name.value)), ARecordTypeDef(var name, var type_params, var fields) => ((Func<List<RecordField>, LetBindResult>)((resolved_fields) => ((Func<CodexType, LetBindResult>)((result_ty) => ((Func<CodexType, LetBindResult>)((ctor_ty) => new LetBindResult(state: st, env: env_bind(env, name.value, ctor_ty))))(build_record_ctor_type(tdm, fields, result_ty, 0)(((long)fields.Count)))))(new RecordTy(name, resolved_fields))))(build_record_fields(tdm, fields, 0, ((long)fields.Count))(new List<RecordField>())), _ => throw new InvalidOperationException("Non-exhaustive match"), };

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
            var rfield = new RecordField(name: f.name, type_val: resolve_type_expr(tdm)(f.type_expr));
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
            var ctor_ty = build_ctor_type(tdm, ctor.fields, result_ty, 0)(((long)ctor.fields.Count));
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

    public static CodexType build_ctor_type(List<TypeBinding> tdm, List<ATypeExpr> fields, CodexType result, long i, long len) => ((i == len) ? result : ((Func<CodexType, CodexType>)((rest) => new FunTy(resolve_type_expr(tdm)(fields[(int)i]), rest)))(build_ctor_type(tdm, fields, result, (i + 1))(len)));

    public static CodexType build_record_ctor_type(List<TypeBinding> tdm, List<ARecordFieldDef> fields, CodexType result, long i, long len) => ((i == len) ? result : ((Func<ARecordFieldDef, CodexType>)((f) => ((Func<CodexType, CodexType>)((rest) => new FunTy(resolve_type_expr(tdm)(f.type_expr), rest)))(build_record_ctor_type(tdm, fields, result, (i + 1))(len))))(fields[(int)i]));

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

    public static string compile(string source, string chapter_name) => ((Func<List<Token>, string>)((tokens) => ((Func<ParseState, string>)((st) => ((Func<Document, string>)((doc) => ((Func<AChapter, string>)((ast) => ((Func<ChapterResult, string>)((check_result) => ((Func<IRChapter, string>)((ir) => emit_full_chapter(ir, ast.type_defs)))(lower_chapter(ast, check_result.types, check_result.state))))(check_chapter(ast))))(desugar_document(doc, chapter_name))))(parse_document(st))))(make_parse_state(tokens))))(tokenize(source));

    public static CompileResult compile_checked(string source, string chapter_name) => compile_with_citations(source, chapter_name, new List<ResolveResult>());

    public static CompileResult compile_with_citations(string source, string chapter_name, List<ResolveResult> imported) => ((Func<List<Token>, CompileResult>)((tokens) => ((Func<ParseState, CompileResult>)((st) => ((Func<Document, CompileResult>)((doc) => ((Func<AChapter, CompileResult>)((ast) => ((Func<ResolveResult, CompileResult>)((resolve_result) => ((((long)resolve_result.errors.Count) > 0) ? new CompileError(resolve_result.errors) : ((Func<ChapterResult, CompileResult>)((check_result) => ((Func<IRChapter, CompileResult>)((ir) => new CompileOk(emit_full_chapter(ir, ast.type_defs), check_result)))(lower_chapter(ast, check_result.types, check_result.state))))(check_chapter(ast)))))(resolve_chapter_with_citations(ast, imported))))(desugar_document(doc, chapter_name))))(parse_document(st))))(make_parse_state(tokens))))(tokenize(source));

    public static object compile_streaming(string source, string chapter_name) => ((Func<List<Token>, object>)((tokens) => ((Func<ParseState, object>)((st) => ((Func<Document, object>)((doc) => ((Func<AChapter, object>)((ast) => ((Func<ChapterResult, object>)((check_result) => ((Func<List<TypeBinding>, object>)((ctor_types) => ((Func<List<TypeBinding>, object>)((all_types) => ((Func<Func<long, Func<List<string>, Func<List<string>, CtorCollectResult>>>, object>)((ctor_names) => ((Func<object>)(() => { ((Func<object>)(() => { Console.WriteLine(_Cce.ToUnicode(emit_type_defs(ast.type_defs, 0))); return null; }))(); stream_defs(ast.defs, all_types, check_result.state, ctor_names, 0, ((long)ast.defs.Count)); ((Func<object>)(() => { Console.WriteLine(_Cce.ToUnicode("")); return null; }))();  return null; }))()))(collect_ctor_names(ast.type_defs, 0))))(Enumerable.Concat(ctor_types, Enumerable.Concat(check_result.types, builtin_type_env().bindings).ToList()).ToList())))(collect_ctor_bindings(ast.type_defs, 0, ((long)ast.type_defs.Count), new List<TypeBinding>()))))(check_chapter(ast))))(desugar_document(doc, chapter_name))))(parse_document(st))))(make_parse_state(tokens))))(tokenize(source));

    public static object stream_defs(List<ADef> defs, List<TypeBinding> types, UnificationState ust, List<string> ctor_names, long i, long len) => ((i == len) ? ((Func<object>)(() => { Console.WriteLine(_Cce.ToUnicode("")); return null; }))() : ((Func<ADef, object>)((def) => ((Func<IRDef, object>)((ir_def) => (is_error_body(ir_def.body) ? ((Func<object>)(() => { Console.WriteLine(_Cce.ToUnicode(("COMPILE-ERROR: def '" + (ir_def.name + ("' has error body: " + error_message(ir_def.body)))))); return null; }))() : ((Func<string, object>)((text) => ((Func<object>)(() => { ((Func<object>)(() => { Console.WriteLine(_Cce.ToUnicode(text)); return null; }))(); stream_defs(defs, types, ust, ctor_names, (i + 1), len);  return null; }))()))(emit_def(ir_def, ctor_names)))))(lower_def(def, types, ust))))(defs[(int)i]));

    public static object compile_streaming_v2(string source, string chapter_name) => ((Func<List<Token>, object>)((tokens) => ((Func<ParseState, object>)((st) => ((Func<ScanResult, object>)((scan) => ((Func<List<ATypeDef>, object>)((type_defs) => ((Func<List<DefHeader>, object>)((headers) => ((Func<List<ChapterAssignment>, object>)((assignments) => ((Func<List<string>, object>)((colliding) => ((Func<string, object>)((header) => ((Func<object>)(() => { ((Func<object>)(() => { Console.WriteLine(_Cce.ToUnicode(header)); return null; }))(); compile_with_scope(tokens, type_defs, headers, assignments, colliding);  return null; }))()))(((scan.chapter_title == "") ? "" : ("Chapter: " + (scan.chapter_title + "\u0001"))))))(find_colliding_names(assignments))))(build_all_assignments(headers, 0))))(scan.def_headers)))(map_list(desugar_type_def, scan.type_defs))))(scan_document(st))))(make_parse_state(tokens))))(tokenize(source));

    public static object compile_with_scope(List<Token> tokens, List<ATypeDef> type_defs, List<DefHeader> headers, List<ChapterAssignment> assignments, List<string> colliding) => ((Func<List<TypeBinding>, object>)((tdm) => ((Func<LetBindResult, object>)((tenv) => ((Func<LetBindResult, object>)((env) => ((Func<List<TypeBinding>, object>)((ctor_types) => ((Func<List<TypeBinding>, object>)((all_types) => ((Func<Func<long, Func<List<string>, Func<List<string>, CtorCollectResult>>>, object>)((ctor_names) => ((Func<object>)(() => { ((Func<object>)(() => { Console.WriteLine(_Cce.ToUnicode(emit_type_defs(type_defs, 0))); return null; }))(); emit_defs_scoped(tokens, headers, all_types, env.state, ctor_names, colliding, assignments, 0, ((long)headers.Count)); ((Func<object>)(() => { Console.WriteLine(_Cce.ToUnicode("")); return null; }))();  return null; }))()))(collect_ctor_names(type_defs, 0))))(Enumerable.Concat(ctor_types, env.env.bindings).ToList())))(collect_ctor_bindings(type_defs, 0, ((long)type_defs.Count), new List<TypeBinding>()))))(register_def_headers(tenv.state, tenv.env, tdm, headers, 0, ((long)headers.Count)))))(register_type_defs(empty_unification_state(), builtin_type_env(), tdm, type_defs, 0, ((long)type_defs.Count)))))(build_type_def_map(type_defs, 0, ((long)type_defs.Count), new List<TypeBinding>()));

    public static LetBindResult register_def_headers(UnificationState st, TypeEnv env, List<TypeBinding> tdm, List<DefHeader> headers, long i, long len)
    {
        while (true)
        {
            if ((i == len))
            {
            return new LetBindResult(state: st, env: env);
            }
            else
            {
            var hdr = headers[(int)i];
            var declared = desugar_annotations(hdr.ann);
            var ty = ((((long)declared.Count) == 0) ? ((Func<FreshResult, LetBindResult>)((fr) => ((Func<TypeEnv, LetBindResult>)((env2) => new LetBindResult(state: fr.state, env: env2)))(env_bind(env, hdr.name.text, fr.var_type))))(fresh_and_advance(st)) : ((Func<CodexType, LetBindResult>)((resolved) => ((Func<ParamResult, LetBindResult>)((pr) => new LetBindResult(state: pr.state, env: env_bind(env, hdr.name.text, pr.parameterized))))(parameterize_type(st, resolved))))(resolve_type_expr(tdm)(declared[(int)0])));
            var _tco_0 = ty.state;
            var _tco_1 = ty.env;
            var _tco_2 = tdm;
            var _tco_3 = headers;
            var _tco_4 = (i + 1);
            var _tco_5 = len;
            st = _tco_0;
            env = _tco_1;
            tdm = _tco_2;
            headers = _tco_3;
            i = _tco_4;
            len = _tco_5;
            continue;
            }
        }
    }

    public static ChapterResult check_defs_streaming(List<Token> tokens, List<DefHeader> headers, UnificationState ust, TypeEnv env, long i, long len, List<TypeBinding> acc)
    {
        while (true)
        {
            if ((i == len))
            {
            return new ChapterResult(types: acc, state: ust);
            }
            else
            {
            var hdr = headers[(int)i];
            var body_st = new ParseState(tokens: tokens, pos: hdr.body_pos);
            var body_result = parse_expr(body_st);
            var def = new Def(name: hdr.name, @params: hdr.@params, ann: hdr.ann, body: unwrap_body(body_result));
            var adef = desugar_def(def);
            var r = check_def(ust, env, adef);
            var resolved = deep_resolve(r.state, r.inferred_type);
            var entry = new TypeBinding(name: adef.name.value, bound_type: resolved);
            var _tco_0 = tokens;
            var _tco_1 = headers;
            var _tco_2 = r.state;
            var _tco_3 = env;
            var _tco_4 = (i + 1);
            var _tco_5 = len;
            var _tco_6 = ((Func<List<TypeBinding>>)(() => { var _l = acc; _l.Add(entry); return _l; }))();
            tokens = _tco_0;
            headers = _tco_1;
            ust = _tco_2;
            env = _tco_3;
            i = _tco_4;
            len = _tco_5;
            acc = _tco_6;
            continue;
            }
        }
    }

    public static object emit_defs_streaming(List<Token> tokens, List<DefHeader> headers, List<TypeBinding> all_types, UnificationState ust, List<string> ctor_names, long i, long len)
    {
        while (true)
        {
            if ((i == len))
            {
            return ((Func<object>)(() => { Console.WriteLine(_Cce.ToUnicode("")); return null; }))();
            }
            else
            {
            var hdr = headers[(int)i];
            var body_st = new ParseState(tokens: tokens, pos: hdr.body_pos);
            var body_result = parse_expr(body_st);
            var def = new Def(name: hdr.name, @params: hdr.@params, ann: hdr.ann, body: unwrap_body(body_result));
            var adef = desugar_def(def);
            var ir_def = lower_def(adef, all_types, ust);
            if (is_error_body(ir_def.body))
            {
            return ((Func<object>)(() => { Console.WriteLine(_Cce.ToUnicode(("COMPILE-ERROR: def '" + (ir_def.name + ("' has error body: " + error_message(ir_def.body)))))); return null; }))();
            }
            else
            {
            var text = emit_def(ir_def, ctor_names);
            if ((text == ""))
            {
            var _tco_0 = tokens;
            var _tco_1 = headers;
            var _tco_2 = all_types;
            var _tco_3 = ust;
            var _tco_4 = ctor_names;
            var _tco_5 = (i + 1);
            var _tco_6 = len;
            tokens = _tco_0;
            headers = _tco_1;
            all_types = _tco_2;
            ust = _tco_3;
            ctor_names = _tco_4;
            i = _tco_5;
            len = _tco_6;
            continue;
            }
            else
            {
            return ((Func<object>)(() => { ((Func<object>)(() => { Console.WriteLine(_Cce.ToUnicode(text)); return null; }))(); emit_defs_streaming(tokens, headers, all_types, ust, ctor_names, (i + 1), len);  return null; }))();
            }
            }
            }
        }
    }

    public static object emit_defs_scoped(List<Token> tokens, List<DefHeader> headers, List<TypeBinding> all_types, UnificationState ust, List<string> ctor_names, List<string> colliding, List<ChapterAssignment> assignments, long i, long len) => ((i == len) ? ((Func<object>)(() => { Console.WriteLine(_Cce.ToUnicode("")); return null; }))() : ((Func<DefHeader, object>)((hdr) => ((Func<List<RenameEntry>, object>)((rn) => emit_defs_same_chapter(tokens, headers, all_types, ust, ctor_names, colliding, assignments, rn, hdr.chapter_slug, i, len)))(build_chapter_rename_map(colliding, assignments, hdr.chapter_slug))))(headers[(int)i]));

    public static object emit_defs_same_chapter(List<Token> tokens, List<DefHeader> headers, List<TypeBinding> all_types, UnificationState ust, List<string> ctor_names, List<string> colliding, List<ChapterAssignment> assignments, List<RenameEntry> rn, string cur_slug, long i, long len) => ((i == len) ? ((Func<object>)(() => { Console.WriteLine(_Cce.ToUnicode("")); return null; }))() : ((Func<DefHeader, object>)((hdr) => ((hdr.chapter_slug != cur_slug) ? emit_defs_scoped(tokens, headers, all_types, ust, ctor_names, colliding, assignments, i, len) : emit_one_scoped_def(tokens, hdr, all_types, ust, ctor_names, colliding, assignments, rn, headers, i, len))))(headers[(int)i]));

    public static object emit_one_scoped_def(List<Token> tokens, DefHeader hdr, List<TypeBinding> all_types, UnificationState ust, List<string> ctor_names, List<string> colliding, List<ChapterAssignment> assignments, List<RenameEntry> rn, List<DefHeader> headers, long i, long len) => ((Func<ParseState, object>)((body_st) => ((Func<ParseExprResult, object>)((body_result) => ((Func<Def, object>)((def) => ((Func<ADef, object>)((adef) => ((Func<IRDef, object>)((ir_def) => ((Func<string, object>)((scoped_name) => ((Func<IRExpr, object>)((scoped_body) => ((Func<IRDef, object>)((scoped_def) => (is_error_body(scoped_def.body) ? ((Func<object>)(() => { Console.WriteLine(_Cce.ToUnicode(("COMPILE-ERROR: def '" + (scoped_def.name + ("' has error body: " + error_message(scoped_def.body)))))); return null; }))() : ((Func<string, object>)((text) => ((text == "") ? emit_defs_same_chapter(tokens, headers, all_types, ust, ctor_names, colliding, assignments, rn, hdr.chapter_slug, (i + 1), len) : ((Func<object>)(() => { ((Func<object>)(() => { Console.WriteLine(_Cce.ToUnicode(text)); return null; }))(); emit_defs_same_chapter(tokens, headers, all_types, ust, ctor_names, colliding, assignments, rn, hdr.chapter_slug, (i + 1), len);  return null; }))())))(emit_def(scoped_def, ctor_names)))))(new IRDef(name: scoped_name, @params: ir_def.@params, type_val: ir_def.type_val, body: scoped_body))))(rename_ir_expr(rn, ir_def.body))))(scope_def_name(colliding, assignments, ir_def.name, hdr.chapter_slug))))(lower_def(adef, all_types, ust))))(desugar_def(def))))(new Def(name: hdr.name, @params: hdr.@params, ann: hdr.ann, body: unwrap_body(body_result)))))(parse_expr(body_st))))(new ParseState(tokens: tokens, pos: hdr.body_pos));

    public static Expr unwrap_body(ParseExprResult r) => r switch { ExprOk(var e, var st) => e, _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static string normalize_whitespace(string s)
    {
        while (true)
        {
            var r = s.Replace("\u0001\u0001\u0001", "\u0001\u0001");
            if ((((long)r.Length) == ((long)s.Length)))
            {
            return s;
            }
            else
            {
            var _tco_0 = r;
            s = _tco_0;
            continue;
            }
        }
    }

    public static object main() => ((Func<object>)(() => { var path = _Cce.FromUnicode(Console.ReadLine() ?? ""); var source = _Cce.FromUnicode(File.ReadAllText(_Cce.ToUnicode(path))); ((Func<string, object>)((clean) => compile_streaming_v2(clean, "Program")))(normalize_whitespace(source)); new Page(1);  return null; }))();

}
