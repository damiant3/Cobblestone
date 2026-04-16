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
        s = s.Replace("\t", "  ").Replace("\r", "");
        var sb = new System.Text.StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++) {
            int u = s[i];
            if (_fromUni.TryGetValue(u, out int b)) sb.Append((char)b);
            else if (u > 127) { sb.Append((char)(0xE0|(u>>12))); sb.Append((char)(0x80|((u>>6)&0x3F))); sb.Append((char)(0x80|(u&0x3F))); }
            else sb.Append((char)68);
        }
        return sb.ToString();
    }
    public static string ToUnicode(string s) {
        var sb = new System.Text.StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++) {
            int b = s[i];
            if (b < 128) sb.Append((char)_toUni[b]);
            else if ((b & 0xE0) == 0xE0 && i+2 < s.Length) {
                int u = ((b & 0x0F) << 12) | ((s[i+1] & 0x3F) << 6) | (s[i+2] & 0x3F);
                sb.Append((char)u); i += 2;
            }
            else sb.Append('\uFFFD');
        }
        return sb.ToString();
    }
    public static long UniToCce(long u) {
        return _fromUni.TryGetValue((int)u, out int c) ? c : 68;
    }
    public static long CceToUni(long b) {
        return (b >= 0 && b < 128) ? _toUni[(int)b] : 65533;
    }
}

static class _Buf {
    static byte[] _mem = new byte[16 * 1024 * 1024];
    static long _ptr = 0;
    public static long heap_save() => _ptr;
    public static long heap_restore(object p) { _ptr = (long)p; return 0; }
    public static long heap_advance(object n) { _ptr += (long)n; return 0; }
    public static long buf_write_byte(object b, object off, object v) { _mem[(long)b + (long)off] = (byte)(long)v; return (long)off + 1; }
    public static long buf_write_bytes(object b, object off, object vs) {
        var list = (List<long>)vs; long o = (long)off; long ba = (long)b;
        for (int i = 0; i < list.Count; i++) _mem[ba + o + i] = (byte)list[i];
        return o + list.Count;
    }
    public static List<long> buf_read_bytes(object b, object off, object n) {
        long ba = (long)b; long o = (long)off; int cnt = (int)(long)n;
        var r = new List<long>(cnt);
        for (int i = 0; i < cnt; i++) r.Add(_mem[ba + o + i]);
        return r;
    }
    public static dynamic list_with_capacity(object cap) => new List<object>((int)(long)cap);
}

public abstract record Maybe<T0>;
public sealed record Just<T0>(T0 Field0) : Maybe<T0>;
public sealed record None<T0> : Maybe<T0>;


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
public sealed record ALitExpr(string Field0, LiteralKind Field1, SourceSpan Field2) : AExpr;
public sealed record ANameExpr(Name Field0, SourceSpan Field1) : AExpr;
public sealed record AApplyExpr(AExpr Field0, AExpr Field1, SourceSpan Field2) : AExpr;
public sealed record ABinaryExpr(AExpr Field0, BinaryOp Field1, AExpr Field2, SourceSpan Field3) : AExpr;
public sealed record AUnaryExpr(AExpr Field0, SourceSpan Field1) : AExpr;
public sealed record AIfExpr(AExpr Field0, AExpr Field1, AExpr Field2, SourceSpan Field3) : AExpr;
public sealed record ALetExpr(List<ALetBind> Field0, AExpr Field1, SourceSpan Field2) : AExpr;
public sealed record ALambdaExpr(List<Name> Field0, AExpr Field1, SourceSpan Field2) : AExpr;
public sealed record AMatchExpr(AExpr Field0, List<AMatchArm> Field1, SourceSpan Field2) : AExpr;
public sealed record AListExpr(List<AExpr> Field0, SourceSpan Field1) : AExpr;
public sealed record ARecordExpr(Name Field0, List<AFieldExpr> Field1, SourceSpan Field2) : AExpr;
public sealed record AFieldAccess(AExpr Field0, Name Field1, SourceSpan Field2) : AExpr;
public sealed record AActExpr(List<AActStmt> Field0, SourceSpan Field1) : AExpr;
public sealed record AHandleExpr(Name Field0, AExpr Field1, List<AHandleClause> Field2, SourceSpan Field3) : AExpr;
public sealed record AErrorExpr(string Field0, SourceSpan Field1) : AExpr;


public sealed record ALetBind(Name name, AExpr value, SourceSpan span);

public sealed record AMatchArm(APat pattern, AExpr body, SourceSpan span);

public sealed record AFieldExpr(Name name, AExpr value, SourceSpan span);

public abstract record AActStmt;
public sealed record AActBindStmt(Name Field0, AExpr Field1, SourceSpan Field2) : AActStmt;
public sealed record AActExprStmt(AExpr Field0, SourceSpan Field1) : AActStmt;


public sealed record AHandleClause(Name op_name, Name resume_name, AExpr body, SourceSpan span);

public abstract record APat;
public sealed record AVarPat(Name Field0, SourceSpan Field1) : APat;
public sealed record ALitPat(string Field0, LiteralKind Field1, SourceSpan Field2) : APat;
public sealed record ACtorPat(Name Field0, List<APat> Field1, SourceSpan Field2) : APat;
public sealed record AWildPat(SourceSpan Field0) : APat;


public abstract record ATypeExpr;
public sealed record ANamedType(Name Field0, SourceSpan Field1) : ATypeExpr;
public sealed record AFunType(ATypeExpr Field0, ATypeExpr Field1, SourceSpan Field2) : ATypeExpr;
public sealed record AAppType(ATypeExpr Field0, List<ATypeExpr> Field1, SourceSpan Field2) : ATypeExpr;
public sealed record AEffectType(List<Name> Field0, ATypeExpr Field1, SourceSpan Field2) : ATypeExpr;


public sealed record AParam(Name name, SourceSpan span);

public sealed record ADef(Name name, List<AParam> @params, List<ATypeExpr> declared_type, AExpr body, string chapter_slug, SourceSpan span);

public sealed record ARecordFieldDef(Name name, ATypeExpr type_expr, SourceSpan span);

public sealed record AVariantCtorDef(Name name, List<ATypeExpr> fields, SourceSpan span);

public abstract record ATypeDef;
public sealed record ARecordTypeDef(Name Field0, List<Name> Field1, List<ARecordFieldDef> Field2, SourceSpan Field3) : ATypeDef;
public sealed record AVariantTypeDef(Name Field0, List<Name> Field1, List<AVariantCtorDef> Field2, SourceSpan Field3) : ATypeDef;


public sealed record AEffectOpDef(Name name, ATypeExpr type_expr, SourceSpan span);

public sealed record AEffectDef(Name name, List<AEffectOpDef> ops, SourceSpan span);

public sealed record ACitesDecl(Name quire, Name chapter_name, List<Name> selected_names, string citing_chapter, SourceSpan span);

public sealed record AChapter(Name name, List<ADef> defs, List<ATypeDef> type_defs, List<AEffectDef> effect_defs, List<ACitesDecl> citations, string chapter_title, string prose, List<string> section_titles, SourceSpan span);

public sealed record CdxCodeInfo(long code, string name, long severity, long phase, string summary);

public sealed record Diagnostic(long code, string message, long severity, SourceSpan span, List<SourceSpan> related_spans);

public sealed record DiagnosticBag(List<Diagnostic> diagnostics, long error_count, bool truncated);

public sealed record Name(string value);

public sealed record OffsetTable(List<string> keys, List<long> values);

public sealed record TextSet(List<string> items);

public abstract record Provenance;
public sealed record ProvParsed : Provenance;
public sealed record ProvDesugared : Provenance;
public sealed record ProvLowered : Provenance;
public sealed record ProvSynthetic : Provenance;


public sealed record SourcePosition(long line, long column, long offset);

public sealed record SourceSpan(SourcePosition start, SourcePosition end, long file_id, Provenance provenance);

public sealed record FileTable(List<string> names);

public sealed record ArityEntry(string name, long arity);

public sealed record ApplyChain(IRExpr root, List<IRExpr> args);

public sealed record BuiltinEmitter(string name, Func<List<IRExpr>, Func<List<ArityEntry>, Func<CodexType, string>>> emit);

public sealed record TypeVarMap(List<long> entries, long next_id);

public sealed record FuncOffset(string name, long offset);

public sealed record CallPatch(long patch_offset, string target);

public sealed record LocalBinding(string name, long slot);

public sealed record FuncAddrFixup(long patch_offset, string target);

public sealed record RodataFixup(long patch_offset, long rodata_offset);

public sealed record TcoState(bool active, bool in_tail_pos, long loop_top, List<long> param_locals, List<long> temp_locals, string current_func, long saved_next_local, long saved_next_temp);

public sealed record CodegenState(long text_buf_addr, long text_len, long rodata_buf_addr, long rodata_len, List<FuncOffset> func_offsets, List<CallPatch> call_patches, List<FuncAddrFixup> func_addr_fixups, List<RodataFixup> rodata_fixups, List<PatchEntry> deferred_patches, List<LocalBinding> locals, long next_temp, long next_local, long spill_count, long load_local_toggle, TcoState tco, List<TypeBinding> type_defs, List<long> stack_overflow_checks, DiagnosticBag bag);

public sealed record EmitResult(CodegenState state, long reg);

public sealed record TrampolineResult(List<long> bytes, long far_jump_patch_pos);

public sealed record EmitChapterResult(List<long> bytes, DiagnosticBag bag);

public sealed record FlatApply(string func_name, List<IRExpr> args);

public sealed record SavedArgs(CodegenState state, List<long> locals);

public sealed record FieldLocal(string name, long slot);

public sealed record EvalFieldsResult(CodegenState state, List<FieldLocal> field_locals);

public sealed record MatchBranchState(CodegenState cg_state, List<long> end_patches);

public sealed record PatchEntry(long pos, long b0, long b1, long b2, long b3);

public sealed record TcoAllocResult(CodegenState alloc_state, List<long> alloc_locals);

public sealed record ConcatManyEval(CodegenState state, List<long> locals);

public sealed record EmitPatternResult(CodegenState state, long next_branch_patch);

public sealed record ItoaState(CodegenState cg, long jmp_done_zero_pos);

public sealed record IsrStubResult(CodegenState state, long first_stub_vaddr);

public sealed record SyscallResult(CodegenState state, long handler_offset);

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
public sealed record IrIntLit(long Field0, SourceSpan Field1) : IRExpr;
public sealed record IrNumLit(long Field0, SourceSpan Field1) : IRExpr;
public sealed record IrTextLit(string Field0, SourceSpan Field1) : IRExpr;
public sealed record IrBoolLit(bool Field0, SourceSpan Field1) : IRExpr;
public sealed record IrCharLit(long Field0, SourceSpan Field1) : IRExpr;
public sealed record IrName(string Field0, CodexType Field1, SourceSpan Field2) : IRExpr;
public sealed record IrBinary(IRBinaryOp Field0, IRExpr Field1, IRExpr Field2, CodexType Field3, SourceSpan Field4) : IRExpr;
public sealed record IrNegate(IRExpr Field0, SourceSpan Field1) : IRExpr;
public sealed record IrIf(IRExpr Field0, IRExpr Field1, IRExpr Field2, CodexType Field3, SourceSpan Field4) : IRExpr;
public sealed record IrLet(string Field0, CodexType Field1, IRExpr Field2, IRExpr Field3, SourceSpan Field4) : IRExpr;
public sealed record IrApply(IRExpr Field0, IRExpr Field1, CodexType Field2, SourceSpan Field3) : IRExpr;
public sealed record IrLambda(List<IRParam> Field0, IRExpr Field1, CodexType Field2, SourceSpan Field3) : IRExpr;
public sealed record IrList(List<IRExpr> Field0, CodexType Field1, SourceSpan Field2) : IRExpr;
public sealed record IrMatch(IRExpr Field0, List<IRBranch> Field1, CodexType Field2, SourceSpan Field3) : IRExpr;
public sealed record IrAct(List<IRActStmt> Field0, CodexType Field1, SourceSpan Field2) : IRExpr;
public sealed record IrHandle(string Field0, IRExpr Field1, List<IRHandleClause> Field2, CodexType Field3, SourceSpan Field4) : IRExpr;
public sealed record IrRecord(string Field0, List<IRFieldVal> Field1, CodexType Field2, SourceSpan Field3) : IRExpr;
public sealed record IrFieldAccess(IRExpr Field0, string Field1, CodexType Field2, SourceSpan Field3) : IRExpr;
public sealed record IrFork(IRExpr Field0, CodexType Field1, SourceSpan Field2) : IRExpr;
public sealed record IrAwait(IRExpr Field0, CodexType Field1, SourceSpan Field2) : IRExpr;
public sealed record IrError(string Field0, CodexType Field1, SourceSpan Field2) : IRExpr;


public sealed record IRParam(string name, CodexType type_val, SourceSpan span);

public sealed record IRBranch(IRPat pattern, IRExpr body, SourceSpan span);

public abstract record IRPat;
public sealed record IrVarPat(string Field0, CodexType Field1, SourceSpan Field2) : IRPat;
public sealed record IrLitPat(string Field0, CodexType Field1, SourceSpan Field2) : IRPat;
public sealed record IrCtorPat(string Field0, List<IRPat> Field1, CodexType Field2, SourceSpan Field3) : IRPat;
public sealed record IrWildPat(SourceSpan Field0) : IRPat;


public abstract record IRActStmt;
public sealed record IrDoBind(string Field0, CodexType Field1, IRExpr Field2, SourceSpan Field3) : IRActStmt;
public sealed record IrDoExec(IRExpr Field0, SourceSpan Field1) : IRActStmt;


public sealed record IRHandleClause(string op_name, string resume_name, IRExpr body, SourceSpan span);

public sealed record IRFieldVal(string name, IRExpr value, SourceSpan span);

public sealed record IRDef(string name, List<IRParam> @params, CodexType type_val, IRExpr body, string chapter_slug, SourceSpan span);

public sealed record IRChapter(Name name, List<IRDef> defs, string chapter_title, string prose, List<string> section_titles, SourceSpan span);

public sealed record LowerCtx(List<TypeBinding> overlay, List<TypeBinding> @base, UnificationState ust);

public sealed record ChapterAssignment(string def_name, string chapter_slug);

public sealed record RenameEntry(string original, string mangled);

public sealed record Scope(List<string> names);

public sealed record ResolveResult(DiagnosticBag bag, List<string> top_level_names, List<string> type_names, List<string> ctor_names);

public sealed record CollectResult(List<string> names, List<Diagnostic> errors);

public sealed record CtorCollectResult(List<string> type_names, List<string> ctor_names);

public sealed record LexState(string source, long offset, long line, long column, long file_id);

public abstract record LexResult;
public sealed record LexToken(Token Field0, LexState Field1) : LexResult;
public sealed record LexEnd : LexResult;


public sealed record EffectNamesResult(List<Token> names, ParseState state);

public sealed record CiteTitleResult(string title, ParseState state);

public sealed record SelectedNamesResult(List<Token> names, ParseState state);

public sealed record ImportParseResult(List<CitesDecl> imports, ParseState state);

public sealed record EffectOpsResult(List<EffectOpDef> ops, ParseState state);

public sealed record ParseState(List<Token> tokens, long pos, DiagnosticBag bag, long paren_depth);

public abstract record ParseExprResult;
public sealed record ExprOk(Expr Field0, ParseState Field1) : ParseExprResult;


public abstract record ParsePatResult;
public sealed record PatOk(Pat Field0, ParseState Field1) : ParsePatResult;


public abstract record ParseTypeResult;
public sealed record TypeOk(TypeExpr Field0, ParseState Field1) : ParseTypeResult;


public sealed record ParseDefResult(Maybe<Def> maybe_def, ParseState state);

public sealed record ParseTypeDefResult(Maybe<TypeDef> maybe_type_def, ParseState state);

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
public sealed record ActExpr(List<ActStmt> Field0) : Expr;
public sealed record HandleExpr(Token Field0, Expr Field1, List<HandleClause> Field2) : Expr;
public sealed record LambdaExpr(List<Token> Field0, Expr Field1) : Expr;
public sealed record ErrExpr(Token Field0) : Expr;


public sealed record LetBind(Token name, Expr value);

public sealed record MatchArm(Pat pattern, Expr body);

public sealed record RecordFieldExpr(Token name, Expr value);

public abstract record ActStmt;
public sealed record ActBindStmt(Token Field0, Expr Field1) : ActStmt;
public sealed record ActExprStmt(Expr Field0) : ActStmt;


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

public sealed record Def(Token name, List<Token> @params, List<TypeAnn> ann, Expr body, string chapter_slug);

public sealed record RecordFieldDef(Token name, TypeExpr type_expr);

public sealed record VariantCtorDef(Token name, List<TypeExpr> fields);

public sealed record EffectOpDef(Token name, TypeExpr type_expr);

public sealed record EffectDef(Token name, List<EffectOpDef> ops);

public abstract record TypeBody;
public sealed record RecordBody(List<RecordFieldDef> Field0) : TypeBody;
public sealed record VariantBody(List<VariantCtorDef> Field0) : TypeBody;


public sealed record TypeDef(Token name, List<Token> type_params, TypeBody body);

public sealed record CitesDecl(Token quire, string chapter_name, List<Token> selected_names, string citing_chapter);

public sealed record Document(List<Def> defs, List<TypeDef> type_defs, List<EffectDef> effect_defs, List<CitesDecl> citations, string chapter_title, List<string> section_titles, DiagnosticBag parse_bag);

public sealed record DefHeader(Token name, List<Token> @params, List<TypeAnn> ann, long body_pos, string chapter_slug);

public sealed record ScanDefResult(Maybe<DefHeader> maybe_header, ParseState state);

public sealed record ScanResult(List<TypeDef> type_defs, List<EffectDef> effect_defs, List<DefHeader> def_headers, List<CitesDecl> citations, string chapter_title, List<string> section_titles);

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
public sealed record IsKeyword : TokenKind;
public sealed record OtherwiseKeyword : TokenKind;
public sealed record ThenKeyword : TokenKind;
public sealed record ElseKeyword : TokenKind;
public sealed record WhenKeyword : TokenKind;
public sealed record WhereKeyword : TokenKind;
public sealed record SuchThatKeyword : TokenKind;
public sealed record ActKeyword : TokenKind;
public sealed record EndKeyword : TokenKind;
public sealed record RecordKeyword : TokenKind;
public sealed record CitesKeyword : TokenKind;
public sealed record ClaimKeyword : TokenKind;
public sealed record ProofKeyword : TokenKind;
public sealed record QedKeyword : TokenKind;
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


public sealed record Token(TokenKind kind, string text, long offset, long line, long column, long file_id);

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
public sealed record LinkedListTy(CodexType Field0) : CodexType;
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

public sealed record UnwrapResult(CodexType ty, UnificationState state);

public sealed record LambdaBindResult(UnificationState state, TypeEnv env, List<CodexType> param_types);

public sealed record PatBindResult(UnificationState state, TypeEnv env);

public sealed record TypeEnv(List<TypeBinding> bindings);

public sealed record TypeBinding(string name, CodexType bound_type);

public sealed record UnificationState(List<SubstEntry> substitutions, long next_id, DiagnosticBag bag);

public sealed record SubstEntry(long var_id, CodexType resolved_type);

public sealed record UnifyResult(bool success, UnificationState state);

public sealed record FreshResult(CodexType var_type, UnificationState state);

public static class Codex_Codex_Codex
{
    public static T19 from_maybe<T19>(Maybe<T19> m, T19 @default) => m switch { Just<T19>(var x) => (T19)(x), None<T19> { } => (T19)(@default), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static bool is_just<T21>(Maybe<T21> m) => m switch { Just<T21>(var x) => (bool)(true), None<T21> { } => (bool)(false), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static bool is_none<T24>(Maybe<T24> m) => m switch { Just<T24>(var x) => (bool)(false), None<T24> { } => (bool)(true), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static Maybe<T32> maybe_map<T30, T32>(Func<T30, T32> f, Maybe<T30> m) => m switch { Just<T30>(var x) => (Maybe<T32>)(new Just<T32>(f(x))), None<T30> { } => (Maybe<T32>)(new None<T32>()), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static Maybe<T35> maybe_bind<T37, T35>(Maybe<T37> m, Func<T37, Maybe<T35>> f) => m switch { Just<T37>(var x) => (Maybe<T35>)(f(x)), None<T37> { } => (Maybe<T35>)(new None<T35>()), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static SourceSpan aexpr_span(AExpr e) => e switch { ALitExpr(var t, var k, var s) => (SourceSpan)(s), ANameExpr(var n, var s) => (SourceSpan)(s), AApplyExpr(var f, var a, var s) => (SourceSpan)(s), ABinaryExpr(var l, var op, var r, var s) => (SourceSpan)(s), AUnaryExpr(var x, var s) => (SourceSpan)(s), AIfExpr(var c, var t, var el, var s) => (SourceSpan)(s), ALetExpr(var bs, var b, var s) => (SourceSpan)(s), ALambdaExpr(var ps, var b, var s) => (SourceSpan)(s), AMatchExpr(var sc, var arms, var s) => (SourceSpan)(s), AListExpr(var es, var s) => (SourceSpan)(s), ARecordExpr(var n, var fs, var s) => (SourceSpan)(s), AFieldAccess(var r, var f, var s) => (SourceSpan)(s), AActExpr(var ss, var s) => (SourceSpan)(s), AHandleExpr(var eff, var b, var cs, var s) => (SourceSpan)(s), AErrorExpr(var m, var s) => (SourceSpan)(s), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static SourceSpan token_span(Token tok) => span_at(tok.line, tok.column, tok.offset, ((long)tok.text.Length), tok.file_id);

    public static SourceSpan desugared_span(SourceSpan s) => span_with_provenance(s, new ProvDesugared());

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
            return new ANameExpr(make_name(tok.text), token_span(tok));
            }
            else if (_tco_s is AppExpr _tco_m2)
            {
                var f = _tco_m2.Field0;
                var a = _tco_m2.Field1;
            var df = desugar_expr(f);
            var da = desugar_expr(a);
            return new AApplyExpr(df, da, desugared_span(aexpr_span(df)));
            }
            else if (_tco_s is BinExpr _tco_m3)
            {
                var l = _tco_m3.Field0;
                var op = _tco_m3.Field1;
                var r = _tco_m3.Field2;
            var dl = desugar_expr(l);
            var dr = desugar_expr(r);
            return new ABinaryExpr(dl, desugar_bin_op(op.kind), dr, token_span(op));
            }
            else if (_tco_s is UnaryExpr _tco_m4)
            {
                var op = _tco_m4.Field0;
                var operand = _tco_m4.Field1;
            return new AUnaryExpr(desugar_expr(operand), token_span(op));
            }
            else if (_tco_s is IfExpr _tco_m5)
            {
                var c = _tco_m5.Field0;
                var t = _tco_m5.Field1;
                var e = _tco_m5.Field2;
            var dc = desugar_expr(c);
            return new AIfExpr(dc, desugar_expr(t), desugar_expr(e), desugared_span(aexpr_span(dc)));
            }
            else if (_tco_s is LetExpr _tco_m6)
            {
                var bindings = _tco_m6.Field0;
                var body = _tco_m6.Field1;
            var db = desugar_expr(body);
            return new ALetExpr(map_list(desugar_let_bind, bindings), db, desugared_span(aexpr_span(db)));
            }
            else if (_tco_s is MatchExpr _tco_m7)
            {
                var scrut = _tco_m7.Field0;
                var arms = _tco_m7.Field1;
            var ds = desugar_expr(scrut);
            return new AMatchExpr(ds, map_list(desugar_match_arm, arms), desugared_span(aexpr_span(ds)));
            }
            else if (_tco_s is ListExpr _tco_m8)
            {
                var elems = _tco_m8.Field0;
            return new AListExpr(map_list(desugar_expr, elems), synthetic_span());
            }
            else if (_tco_s is RecordExpr _tco_m9)
            {
                var type_tok = _tco_m9.Field0;
                var fields = _tco_m9.Field1;
            return new ARecordExpr(make_name(type_tok.text), map_list(desugar_field_expr, fields), token_span(type_tok));
            }
            else if (_tco_s is FieldExpr _tco_m10)
            {
                var rec = _tco_m10.Field0;
                var field_tok = _tco_m10.Field1;
            return new AFieldAccess(desugar_expr(rec), make_name(field_tok.text), token_span(field_tok));
            }
            else if (_tco_s is ParenExpr _tco_m11)
            {
                var inner = _tco_m11.Field0;
            var _tco_0 = inner;
            node = _tco_0;
            continue;
            }
            else if (_tco_s is ActExpr _tco_m12)
            {
                var stmts = _tco_m12.Field0;
            return new AActExpr(map_list(desugar_act_stmt, stmts), synthetic_span());
            }
            else if (_tco_s is HandleExpr _tco_m13)
            {
                var eff_tok = _tco_m13.Field0;
                var body = _tco_m13.Field1;
                var clauses = _tco_m13.Field2;
            return new AHandleExpr(make_name(eff_tok.text), desugar_expr(body), map_list(desugar_handle_clause, clauses), token_span(eff_tok));
            }
            else if (_tco_s is LambdaExpr _tco_m14)
            {
                var @params = _tco_m14.Field0;
                var body = _tco_m14.Field1;
            return new ALambdaExpr(map_list(desugar_lambda_param, @params), desugar_expr(body), synthetic_span());
            }
            else if (_tco_s is ErrExpr _tco_m15)
            {
                var tok = _tco_m15.Field0;
            return new AErrorExpr(tok.text, token_span(tok));
            }
        }
    }

    public static AExpr desugar_literal(Token tok) => (is_literal(tok.kind) ? new ALitExpr(tok.text, classify_literal(tok.kind), token_span(tok)) : new AErrorExpr(tok.text, token_span(tok)));

    public static LiteralKind classify_literal(TokenKind k) => k switch { IntegerLiteral { } => (LiteralKind)(new IntLit()), NumberLiteral { } => (LiteralKind)(new NumLit()), TextLiteral { } => (LiteralKind)(new TextLit()), CharLiteral { } => (LiteralKind)(new CharLit()), TrueKeyword { } => (LiteralKind)(new BoolLit()), FalseKeyword { } => (LiteralKind)(new BoolLit()), _ => (LiteralKind)(new TextLit()), };

    public static ALetBind desugar_let_bind(LetBind b) => new ALetBind(name: make_name(b.name.text), value: desugar_expr(b.value), span: token_span(b.name));

    public static AMatchArm desugar_match_arm(MatchArm arm) => ((Func<APat, AMatchArm>)((dp) => new AMatchArm(pattern: dp, body: desugar_expr(arm.body), span: apat_span(dp))))(desugar_pattern(arm.pattern));

    public static AFieldExpr desugar_field_expr(RecordFieldExpr f) => new AFieldExpr(name: make_name(f.name.text), value: desugar_expr(f.value), span: token_span(f.name));

    public static AActStmt desugar_act_stmt(ActStmt s) => s switch { ActBindStmt(var tok, var val) => (AActStmt)(new AActBindStmt(make_name(tok.text), desugar_expr(val), token_span(tok))), ActExprStmt(var e) => (AActStmt)(((Func<AExpr, AActStmt>)((de) => new AActExprStmt(de, aexpr_span(de))))(desugar_expr(e))), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static AHandleClause desugar_handle_clause(HandleClause c) => new AHandleClause(op_name: make_name(c.op_name.text), resume_name: make_name(c.resume_name.text), body: desugar_expr(c.body), span: token_span(c.op_name));

    public static Name desugar_lambda_param(Token tok) => make_name(tok.text);

    public static BinaryOp desugar_bin_op(TokenKind k) => k switch { Plus { } => (BinaryOp)(new OpAdd()), Minus { } => (BinaryOp)(new OpSub()), Star { } => (BinaryOp)(new OpMul()), Slash { } => (BinaryOp)(new OpDiv()), Caret { } => (BinaryOp)(new OpPow()), DoubleEquals { } => (BinaryOp)(new OpEq()), NotEquals { } => (BinaryOp)(new OpNotEq()), LessThan { } => (BinaryOp)(new OpLt()), GreaterThan { } => (BinaryOp)(new OpGt()), LessOrEqual { } => (BinaryOp)(new OpLtEq()), GreaterOrEqual { } => (BinaryOp)(new OpGtEq()), TripleEquals { } => (BinaryOp)(new OpDefEq()), PlusPlus { } => (BinaryOp)(new OpAppend()), ColonColon { } => (BinaryOp)(new OpCons()), Ampersand { } => (BinaryOp)(new OpAnd()), Pipe { } => (BinaryOp)(new OpOr()), _ => (BinaryOp)(new OpAdd()), };

    public static SourceSpan apat_span(APat p) => p switch { AVarPat(var n, var s) => (SourceSpan)(s), ALitPat(var t, var k, var s) => (SourceSpan)(s), ACtorPat(var n, var sub, var s) => (SourceSpan)(s), AWildPat(var s) => (SourceSpan)(s), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static APat desugar_pattern(Pat p) => p switch { VarPat(var tok) => (APat)(new AVarPat(make_name(tok.text), token_span(tok))), LitPat(var tok) => (APat)(new ALitPat(tok.text, classify_literal(tok.kind), token_span(tok))), CtorPat(var tok, var subs) => (APat)(new ACtorPat(make_name(tok.text), map_list(desugar_pattern, subs), token_span(tok))), WildPat(var tok) => (APat)(new AWildPat(token_span(tok))), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static ATypeExpr desugar_type_expr(TypeExpr t)
    {
        while (true)
        {
            var _tco_s = t;
            if (_tco_s is NamedType _tco_m0)
            {
                var tok = _tco_m0.Field0;
            return new ANamedType(make_name(tok.text), token_span(tok));
            }
            else if (_tco_s is FunType _tco_m1)
            {
                var param = _tco_m1.Field0;
                var ret = _tco_m1.Field1;
            var dp = desugar_type_expr(param);
            return new AFunType(dp, desugar_type_expr(ret), atype_span(dp));
            }
            else if (_tco_s is AppType _tco_m2)
            {
                var ctor = _tco_m2.Field0;
                var args = _tco_m2.Field1;
            var dc = desugar_type_expr(ctor);
            return new AAppType(dc, map_list(desugar_type_expr, args), atype_span(dc));
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
            var de = desugar_type_expr(elem);
            return new AAppType(new ANamedType(make_name("\u0031\u0011\u0013\u000E"), atype_span(de)), new List<ATypeExpr> { de }, atype_span(de));
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
            var dr = desugar_type_expr(ret);
            return new AEffectType(map_list(make_type_param_name, effs), dr, atype_span(dr));
            }
        }
    }

    public static SourceSpan atype_span(ATypeExpr t) => t switch { ANamedType(var n, var s) => (SourceSpan)(s), AFunType(var p, var r, var s) => (SourceSpan)(s), AAppType(var c, var args, var s) => (SourceSpan)(s), AEffectType(var effs, var r, var s) => (SourceSpan)(s), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static ADef desugar_def(Def d) => ((Func<List<ATypeExpr>, ADef>)((ann_types) => new ADef(name: make_name(d.name.text), @params: map_list(desugar_param, d.@params), declared_type: ann_types, body: desugar_expr(d.body), chapter_slug: d.chapter_slug, span: token_span(d.name))))(desugar_annotations(d.ann));

    public static List<ATypeExpr> desugar_annotations(List<TypeAnn> anns) => ((((long)anns.Count) == 0L) ? new List<ATypeExpr>() : ((Func<TypeAnn, List<ATypeExpr>>)((a) => new List<ATypeExpr> { desugar_type_expr(a.type_expr) }))(anns[(int)0L]));

    public static AParam desugar_param(Token tok) => new AParam(name: make_name(tok.text), span: token_span(tok));

    public static ATypeDef desugar_type_def(TypeDef td) => td.body switch { RecordBody(var fields) => (ATypeDef)(new ARecordTypeDef(make_name(td.name.text), map_list(make_type_param_name, td.type_params), map_list(desugar_record_field_def, fields), token_span(td.name))), VariantBody(var ctors) => (ATypeDef)(new AVariantTypeDef(make_name(td.name.text), map_list(make_type_param_name, td.type_params), map_list(desugar_variant_ctor_def, ctors), token_span(td.name))), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static Name make_type_param_name(Token tok) => make_name(tok.text);

    public static ARecordFieldDef desugar_record_field_def(RecordFieldDef f) => new ARecordFieldDef(name: make_name(f.name.text), type_expr: desugar_type_expr(f.type_expr), span: token_span(f.name));

    public static AVariantCtorDef desugar_variant_ctor_def(VariantCtorDef c) => new AVariantCtorDef(name: make_name(c.name.text), fields: map_list(desugar_type_expr, c.fields), span: token_span(c.name));

    public static AChapter desugar_document(Document doc, string chapter_name) => new AChapter(name: make_name(chapter_name), defs: map_list(desugar_def, doc.defs), type_defs: map_list(desugar_type_def, doc.type_defs), effect_defs: map_list(desugar_effect_def, doc.effect_defs), citations: map_list(desugar_cite, doc.citations), chapter_title: doc.chapter_title, prose: "", section_titles: doc.section_titles, span: synthetic_span());

    public static ACitesDecl desugar_cite(CitesDecl imp) => new ACitesDecl(quire: make_name(imp.quire.text), chapter_name: make_name(imp.chapter_name), selected_names: map_list(desugar_cite_name, imp.selected_names), citing_chapter: imp.citing_chapter, span: token_span(imp.quire));

    public static Name desugar_cite_name(Token tok) => make_name(tok.text);

    public static AEffectDef desugar_effect_def(EffectDef ed) => new AEffectDef(name: make_name(ed.name.text), ops: map_list(desugar_effect_op, ed.ops), span: token_span(ed.name));

    public static AEffectOpDef desugar_effect_op(EffectOpDef op) => new AEffectOpDef(name: make_name(op.name.text), type_expr: desugar_type_expr(op.type_expr), span: token_span(op.name));

    public static long cdx_too_many_errors() => 1L;

    public static long cdx_expected_token_kind() => 1000L;

    public static long cdx_reserved_keyword_as_identifier() => 1060L;

    public static long cdx_ir_error() => 2000L;

    public static long cdx_type_mismatch() => 2001L;

    public static long cdx_unknown_name() => 2002L;

    public static long cdx_infinite_type() => 2010L;

    public static long cdx_let_binds_effectful_value() => 2033L;

    public static long cdx_unresolved_func_offset() => 2040L;

    public static long cdx_error_type_in_ir() => 2041L;

    public static long cdx_duplicate_definition() => 3001L;

    public static long cdx_undefined_name() => 3002L;

    public static long cdx_duplicate_cite() => 3003L;

    public static CdxCodeInfo mk_cdx(long code, string name, long sev, long phase, string summary) => new CdxCodeInfo(code: code, name: name, severity: sev, phase: phase, summary: summary);

    public static List<CdxCodeInfo> cdx_registry() => new List<CdxCodeInfo> { mk_cdx(cdx_too_many_errors(), "\u0028\u0010\u0010\u0034\u000F\u0012\u001E\u0027\u0015\u0015\u0010\u0015\u0013", sev_error(), phase_infrastructure(), "\u0027\u0015\u0015\u0010\u0015\u0002\u0018\u000F\u001F\u0002\u0015\u000D\u000F\u0018\u0014\u000D\u0016\u0046\u0002\u001C\u0019\u0015\u000E\u0014\u000D\u0015\u0002\u000D\u0015\u0015\u0010\u0015\u0013\u0002\u0013\u0019\u001F\u001F\u0015\u000D\u0013\u0013\u000D\u0016\u0041"), mk_cdx(cdx_expected_token_kind(), "\u0027\u0024\u001F\u000D\u0018\u000E\u000D\u0016\u0028\u0010\u0022\u000D\u0012\u003C\u0011\u0012\u0016", sev_error(), phase_parser(), "\u0029\u0002\u0013\u001F\u000D\u0018\u0011\u001C\u0011\u0018\u0002\u000E\u0010\u0022\u000D\u0012\u0002\u0022\u0011\u0012\u0016\u0002\u001B\u000F\u0013\u0002\u0015\u000D\u0025\u0019\u0011\u0015\u000D\u0016\u0002\u0020\u0019\u000E\u0002\u000F\u0002\u0016\u0011\u001C\u001C\u000D\u0015\u000D\u0012\u000E\u0002\u0010\u0012\u000D\u0002\u001B\u000F\u0013\u0002\u001C\u0010\u0019\u0012\u0016\u0041"), mk_cdx(cdx_reserved_keyword_as_identifier(), "\u002F\u000D\u0013\u000D\u0015\u0021\u000D\u0016\u003C\u000D\u001E\u001B\u0010\u0015\u0016\u0029\u0013\u002B\u0016\u000D\u0012\u000E\u0011\u001C\u0011\u000D\u0015", sev_error(), phase_parser(), "\u0029\u0002\u0015\u000D\u0013\u000D\u0015\u0021\u000D\u0016\u0002\u0022\u000D\u001E\u001B\u0010\u0015\u0016\u0002\u001B\u000F\u0013\u0002\u0019\u0013\u000D\u0016\u0002\u001B\u0014\u000D\u0015\u000D\u0002\u000F\u0012\u0002\u0011\u0016\u000D\u0012\u000E\u0011\u001C\u0011\u000D\u0015\u0002\u001B\u000F\u0013\u0002\u0015\u000D\u0025\u0019\u0011\u0015\u000D\u0016\u0046\u0002\u0015\u000D\u0012\u000F\u001A\u000D\u0002\u0011\u000E\u0041"), mk_cdx(cdx_ir_error(), "\u002B\u0015\u0027\u0015\u0015\u0010\u0015", sev_error(), phase_codegen(), "\u0032\u0010\u0016\u000D\u001D\u000D\u0012\u0002\u000D\u0012\u0018\u0010\u0019\u0012\u000E\u000D\u0015\u000D\u0016\u0002\u000F\u0012\u0002\u002B\u0015\u0027\u0015\u0015\u0010\u0015\u0002\u0012\u0010\u0016\u000D\u0042\u0002\u0011\u0012\u0016\u0011\u0018\u000F\u000E\u0011\u0012\u001D\u0002\u000F\u0012\u0002\u000D\u000F\u0015\u0017\u0011\u000D\u0015\u0002\u001F\u0014\u000F\u0013\u000D\u0002\u0013\u0011\u0017\u000D\u0012\u000E\u0017\u001E\u0002\u001C\u000F\u0011\u0017\u000D\u0016\u0041"), mk_cdx(cdx_type_mismatch(), "\u0028\u001E\u001F\u000D\u0034\u0011\u0013\u001A\u000F\u000E\u0018\u0014", sev_error(), phase_type_checker(), "\u0028\u001B\u0010\u0002\u000E\u001E\u001F\u000D\u0013\u0002\u0018\u0010\u0019\u0017\u0016\u0002\u0012\u0010\u000E\u0002\u0020\u000D\u0002\u0019\u0012\u0011\u001C\u0011\u000D\u0016\u0041"), mk_cdx(cdx_unknown_name(), "\u0033\u0012\u0022\u0012\u0010\u001B\u0012\u002C\u000F\u001A\u000D", sev_error(), phase_type_checker(), "\u002C\u000F\u001A\u000D\u0002\u001B\u000F\u0013\u0002\u0012\u0010\u000E\u0002\u001C\u0010\u0019\u0012\u0016\u0002\u0011\u0012\u0002\u000E\u0014\u000D\u0002\u000E\u001E\u001F\u000D\u0002\u000D\u0012\u0021\u0011\u0015\u0010\u0012\u001A\u000D\u0012\u000E\u0002\u0016\u0019\u0015\u0011\u0012\u001D\u0002\u0018\u0014\u000D\u0018\u0022\u0011\u0012\u001D\u0041"), mk_cdx(cdx_infinite_type(), "\u002B\u0012\u001C\u0011\u0012\u0011\u000E\u000D\u0028\u001E\u001F\u000D", sev_error(), phase_type_checker(), "\u002A\u0018\u0018\u0019\u0015\u0013\u0002\u0018\u0014\u000D\u0018\u0022\u0002\u001C\u000F\u0011\u0017\u000D\u0016\u0002\u0049\u0002\u0019\u0012\u0011\u001C\u0011\u0018\u000F\u000E\u0011\u0010\u0012\u0002\u001B\u0010\u0019\u0017\u0016\u0002\u001F\u0015\u0010\u0016\u0019\u0018\u000D\u0002\u000F\u0012\u0002\u0011\u0012\u001C\u0011\u0012\u0011\u000E\u000D\u0002\u000E\u001E\u001F\u000D\u0041"), mk_cdx(cdx_let_binds_effectful_value(), "\u0031\u000D\u000E\u003A\u0011\u0012\u0016\u0013\u0027\u001C\u001C\u000D\u0018\u000E\u001C\u0019\u0017\u003B\u000F\u0017\u0019\u000D", sev_error(), phase_type_checker(), "\u0017\u000D\u000E\u0049\u0020\u0011\u0012\u0016\u0011\u0012\u001D\u0002\u000E\u0014\u000D\u0002\u0015\u000D\u0013\u0019\u0017\u000E\u0002\u0010\u001C\u0002\u000F\u0012\u0002\u000D\u001C\u001C\u000D\u0018\u000E\u001C\u0019\u0017\u0002\u0018\u000F\u0017\u0017\u0002\u0013\u0011\u0017\u000D\u0012\u000E\u0017\u001E\u0002\u0018\u0010\u0015\u0015\u0019\u001F\u000E\u0013\u0002\u0010\u0012\u0002\u0020\u000F\u0015\u000D\u0002\u001A\u000D\u000E\u000F\u0017\u0046\u0002\u0019\u0013\u000D\u0002\u000F\u0018\u000E\u0049\u0020\u0011\u0012\u0016\u0002\u004A\u003E\u0002\u004F\u0049\u0002\u000D\u0024\u001F\u0015\u004B\u0002\u0011\u0012\u0013\u0011\u0016\u000D\u0002\u000F\u0012\u0002\u000F\u0018\u000E\u0002\u0020\u0017\u0010\u0018\u0022\u0041"), mk_cdx(cdx_unresolved_func_offset(), "\u0033\u0012\u0015\u000D\u0013\u0010\u0017\u0021\u000D\u0016\u0036\u0019\u0012\u0018\u002A\u001C\u001C\u0013\u000D\u000E", sev_error(), phase_codegen(), "\u0029\u0002\u0018\u000F\u0017\u0017\u0002\u0010\u0015\u0002\u001C\u0019\u0012\u0018\u0049\u000F\u0016\u0016\u0015\u0002\u001C\u0011\u0024\u0019\u001F\u0002\u0015\u000D\u001C\u000D\u0015\u0013\u0002\u000E\u0010\u0002\u000F\u0002\u001C\u0019\u0012\u0018\u000E\u0011\u0010\u0012\u0002\u0012\u0010\u000E\u0002\u001F\u0015\u000D\u0013\u000D\u0012\u000E\u0002\u0011\u0012\u0002\u000E\u0014\u000D\u0002\u0010\u001C\u001C\u0013\u000D\u000E\u0002\u000E\u000F\u0020\u0017\u000D\u0046\u0002\u000D\u001A\u0011\u0013\u0013\u0011\u0010\u0012\u0002\u001B\u0010\u0019\u0017\u0016\u0002\u001F\u0015\u0010\u0016\u0019\u0018\u000D\u0002\u001B\u0015\u0010\u0012\u001D\u0002\u0018\u0010\u0016\u000D\u0041"), mk_cdx(cdx_error_type_in_ir(), "\u0027\u0015\u0015\u0010\u0015\u0028\u001E\u001F\u000D\u002B\u0012\u002B\u002F", sev_error(), phase_codegen(), "\u0029\u0012\u0002\u002B\u002F\u0002\u0012\u0010\u0016\u000D\u0002\u0015\u000D\u000F\u0018\u0014\u000D\u0016\u0002\u0018\u0010\u0016\u000D\u001D\u000D\u0012\u0002\u001B\u0011\u000E\u0014\u0002\u0027\u0015\u0015\u0010\u0015\u0028\u001E\u0002\u0011\u0012\u0002\u0011\u000E\u0013\u0002\u000E\u001E\u001F\u000D\u0046\u0002\u000F\u0012\u0002\u000D\u000F\u0015\u0017\u0011\u000D\u0015\u0002\u001F\u0014\u000F\u0013\u000D\u0002\u0013\u0011\u0017\u000D\u0012\u000E\u0017\u001E\u0002\u001C\u000F\u0011\u0017\u000D\u0016\u0002\u000E\u0010\u0002\u0015\u000D\u0013\u0010\u0017\u0021\u000D\u0002\u000E\u0014\u000D\u0002\u000E\u001E\u001F\u000D\u0041"), mk_cdx(cdx_duplicate_definition(), "\u0030\u0019\u001F\u0017\u0011\u0018\u000F\u000E\u000D\u0030\u000D\u001C\u0011\u0012\u0011\u000E\u0011\u0010\u0012", sev_error(), phase_name_resolver(), "\u0028\u0014\u000D\u0002\u0013\u000F\u001A\u000D\u0002\u0012\u000F\u001A\u000D\u0002\u0011\u0013\u0002\u0011\u0012\u000E\u0015\u0010\u0016\u0019\u0018\u000D\u0016\u0002\u000E\u001B\u0011\u0018\u000D\u0002\u000F\u000E\u0002\u0018\u0014\u000F\u001F\u000E\u000D\u0015\u0002\u0013\u0018\u0010\u001F\u000D\u0041"), mk_cdx(cdx_undefined_name(), "\u0033\u0012\u0016\u000D\u001C\u0011\u0012\u000D\u0016\u002C\u000F\u001A\u000D", sev_error(), phase_name_resolver(), "\u0029\u0002\u0012\u000F\u001A\u000D\u0002\u001B\u000F\u0013\u0002\u0015\u000D\u001C\u000D\u0015\u000D\u0012\u0018\u000D\u0016\u0002\u0020\u0019\u000E\u0002\u0011\u0013\u0002\u0012\u0010\u000E\u0002\u0011\u0012\u0002\u000F\u0012\u001E\u0002\u0021\u0011\u0013\u0011\u0020\u0017\u000D\u0002\u0013\u0018\u0010\u001F\u000D\u0041"), mk_cdx(cdx_duplicate_cite(), "\u0030\u0019\u001F\u0017\u0011\u0018\u000F\u000E\u000D\u0032\u0011\u000E\u000D", sev_error(), phase_name_resolver(), "\u0028\u001B\u0010\u0002\u0018\u0011\u000E\u000D\u0002\u0016\u000D\u0018\u0017\u000F\u0015\u000F\u000E\u0011\u0010\u0012\u0013\u0002\u0011\u0012\u0002\u000E\u0014\u000D\u0002\u0013\u000F\u001A\u000D\u0002\u0018\u0014\u000F\u001F\u000E\u000D\u0015\u0002\u0013\u000D\u0017\u000D\u0018\u000E\u0002\u000E\u0014\u000D\u0002\u0013\u000F\u001A\u000D\u0002\u0012\u000F\u001A\u000D\u0046\u0002\u000E\u0014\u000D\u0002\u0017\u000F\u000E\u000D\u0015\u0002\u0018\u0011\u000E\u000D\u0002\u001B\u0010\u0019\u0017\u0016\u0002\u0013\u0011\u0017\u000D\u0012\u000E\u0017\u001E\u0002\u0013\u0014\u000F\u0016\u0010\u001B\u0002\u000E\u0014\u000D\u0002\u000D\u000F\u0015\u0017\u0011\u000D\u0015\u0041") };

    public static CdxCodeInfo cdx_lookup_loop(List<CdxCodeInfo> reg, long code, long i, long len)
    {
        while (true)
        {
            if ((i >= len))
            {
            return mk_cdx(0L, "\u0033\u0012\u0022\u0012\u0010\u001B\u0012", sev_error(), phase_infrastructure(), "\u0033\u0012\u0015\u000D\u001D\u0011\u0013\u000E\u000D\u0015\u000D\u0016\u0002\u0032\u0030\u003E\u0002\u0018\u0010\u0016\u000D\u0041");
            }
            else
            {
            var entry = reg[(int)i];
            if ((entry.code == code))
            {
            return entry;
            }
            else
            {
            var _tco_0 = reg;
            var _tco_1 = code;
            var _tco_2 = (i + 1L);
            var _tco_3 = len;
            reg = _tco_0;
            code = _tco_1;
            i = _tco_2;
            len = _tco_3;
            continue;
            }
            }
        }
    }

    public static CdxCodeInfo cdx_lookup(long code) => cdx_lookup_loop(cdx_registry(), code, 0L, ((long)cdx_registry().Count));

    public static List<T434> map_list<T424, T434>(Func<T424, T434> f, List<T424> xs) => map_list_loop(f, xs, 0L, ((long)xs.Count), new List<T434>());

    public static List<T449> map_list_loop<T448, T449>(Func<T448, T449> f, List<T448> xs, long i, long len, List<T449> acc)
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
            var _tco_4 = ((Func<List<T449>>)(() => { var _l = acc; _l.Add(f(xs[(int)i])); return _l; }))();
            f = _tco_0;
            xs = _tco_1;
            i = _tco_2;
            len = _tco_3;
            acc = _tco_4;
            continue;
            }
        }
    }

    public static T462 fold_list<T462, T453>(Func<T462, Func<T453, T462>> f, T462 z, List<T453> xs) => fold_list_loop(f, z, xs, 0L, ((long)xs.Count));

    public static T476 fold_list_loop<T476, T471>(Func<T476, Func<T471, T476>> f, T476 z, List<T471> xs, long i, long len)
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
            var mid = (lo + ((hi - lo) / 2L));
            var mid_name = bindings[(int)mid].name;
            if (((long)string.CompareOrdinal(name, mid_name) <= 0L))
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
            var _tco_2 = (mid + 1L);
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
            var mid = (lo + ((hi - lo) / 2L));
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
            var _tco_2 = (mid + 1L);
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
            var mid = (lo + ((hi - lo) / 2L));
            if (((long)string.CompareOrdinal(name, names[(int)mid]) <= 0L))
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
            var _tco_2 = (mid + 1L);
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

    public static bool text_set_has(List<string> names, string name) => ((Func<long, bool>)((len) => ((Func<long, bool>)((pos) => ((pos >= len) ? false : (names[(int)pos] == name))))(bsearch_text_set(names, name, 0L, len))))(((long)names.Count));

    public static List<string> sorted_insert(List<string> xs, string name) => ((Func<long, List<string>>)((len) => ((Func<long, List<string>>)((pos) => ((Func<List<string>>)(() => { var _l = new List<string>(xs); _l.Insert((int)pos, name); return _l; }))()))(bsearch_text_set(xs, name, 0L, len))))(((long)xs.Count));

    public static List<string> sort_text_list(List<string> xs) => fold_list((_p0_) => (_p1_) => sorted_insert(_p0_, _p1_), new List<string>(), xs);

    public static Diagnostic make_diagnostic(long sev, long code, string msg, SourceSpan span) => new Diagnostic(code: code, message: msg, severity: sev, span: span, related_spans: new List<SourceSpan>());

    public static Diagnostic make_error(long code, string msg, SourceSpan span) => make_diagnostic(sev_error(), code, msg, span);

    public static Diagnostic make_warning(long code, string msg, SourceSpan span) => make_diagnostic(sev_warning(), code, msg, span);

    public static Diagnostic make_info(long code, string msg, SourceSpan span) => make_diagnostic(sev_info(), code, msg, span);

    public static Diagnostic make_hint(long code, string msg, SourceSpan span) => make_diagnostic(sev_hint(), code, msg, span);

    public static Diagnostic make_error_related(long code, string msg, SourceSpan span, List<SourceSpan> related) => new Diagnostic(code: code, message: msg, severity: sev_error(), span: span, related_spans: related);

    public static string format_cdx_code(long n) => ("\u0032\u0030\u003E" + integer_to_text_padded(n, 4L));

    public static string diagnostic_display(Diagnostic d, FileTable table) => ((Func<string, string>)((loc) => ((Func<string, string>)((prefix) => (prefix + (severity_label(d.severity) + ("\u0002" + (format_cdx_code(d.code) + ("\u0045\u0002" + d.message)))))))(((loc == "") ? "" : (loc + "\u0045\u0002")))))(span_display(d.span, table));

    public static long max_errors() => 20L;

    public static DiagnosticBag empty_bag() => new DiagnosticBag(diagnostics: new List<Diagnostic>(), error_count: 0L, truncated: false);

    public static DiagnosticBag bag_from_list(List<Diagnostic> ds) => bag_from_list_loop(empty_bag(), ds, 0L, ((long)ds.Count));

    public static DiagnosticBag bag_from_list_loop(DiagnosticBag bag, List<Diagnostic> ds, long i, long len)
    {
        while (true)
        {
            if ((i == len))
            {
            return bag;
            }
            else
            {
            var _tco_0 = bag_add(bag, ds[(int)i]);
            var _tco_1 = ds;
            var _tco_2 = (i + 1L);
            var _tco_3 = len;
            bag = _tco_0;
            ds = _tco_1;
            i = _tco_2;
            len = _tco_3;
            continue;
            }
        }
    }

    public static DiagnosticBag bag_add(DiagnosticBag bag, Diagnostic d) => ((d.severity == sev_error()) ? bag_add_error(bag, d) : new DiagnosticBag(diagnostics: ((Func<List<Diagnostic>>)(() => { var _l = bag.diagnostics; _l.Add(d); return _l; }))(), error_count: bag.error_count, truncated: bag.truncated));

    public static DiagnosticBag bag_add_error(DiagnosticBag bag, Diagnostic d) => ((bag.error_count >= max_errors()) ? (bag.truncated ? bag : new DiagnosticBag(diagnostics: ((Func<List<Diagnostic>>)(() => { var _l = bag.diagnostics; _l.Add(make_error(cdx_too_many_errors(), "\u0028\u0010\u0010\u0002\u001A\u000F\u0012\u001E\u0002\u000D\u0015\u0015\u0010\u0015\u0013\u0041\u0002\u0036\u0019\u0015\u000E\u0014\u000D\u0015\u0002\u000D\u0015\u0015\u0010\u0015\u0013\u0002\u0013\u0019\u001F\u001F\u0015\u000D\u0013\u0013\u000D\u0016\u0041", synthetic_span())); return _l; }))(), error_count: (bag.error_count + 1L), truncated: true)) : new DiagnosticBag(diagnostics: ((Func<List<Diagnostic>>)(() => { var _l = bag.diagnostics; _l.Add(d); return _l; }))(), error_count: (bag.error_count + 1L), truncated: bag.truncated));

    public static DiagnosticBag bag_concat(DiagnosticBag bag, List<Diagnostic> ds) => bag_concat_loop(bag, ds, 0L, ((long)ds.Count));

    public static DiagnosticBag bag_concat_loop(DiagnosticBag bag, List<Diagnostic> ds, long i, long len)
    {
        while (true)
        {
            if ((i == len))
            {
            return bag;
            }
            else
            {
            var _tco_0 = bag_add(bag, ds[(int)i]);
            var _tco_1 = ds;
            var _tco_2 = (i + 1L);
            var _tco_3 = len;
            bag = _tco_0;
            ds = _tco_1;
            i = _tco_2;
            len = _tco_3;
            continue;
            }
        }
    }

    public static DiagnosticBag bag_merge(DiagnosticBag bag, DiagnosticBag other) => bag_concat(bag, other.diagnostics);

    public static bool bag_has_errors(DiagnosticBag bag) => (bag.error_count > 0L);

    public static long bag_count(DiagnosticBag bag) => ((long)bag.diagnostics.Count);

    public static List<Diagnostic> bag_diagnostics(DiagnosticBag bag) => bag.diagnostics;

    public static bool bag_is_truncated(DiagnosticBag bag) => bag.truncated;

    public static Name make_name(string s) => new Name(value: s);

    public static string name_value(Name n) => n.value;

    public static long djb2_hash(string s) => djb2_loop(s, 0L, ((long)s.Length), 5381L);

    public static long djb2_loop(string s, long i, long len, long h)
    {
        while (true)
        {
            if ((i == len))
            {
            return h;
            }
            else
            {
            var _tco_0 = s;
            var _tco_1 = (i + 1L);
            var _tco_2 = len;
            var _tco_3 = int_mod(((h * 33L) + ((long)s[(int)i])), 2147483647L);
            s = _tco_0;
            i = _tco_1;
            len = _tco_2;
            h = _tco_3;
            continue;
            }
        }
    }

    public static long offset_table_size() => 8192L;

    public static long offset_table_mask() => 8191L;

    public static List<string> fill_empty_keys(long n, long i, List<string> acc)
    {
        while (true)
        {
            if ((i >= n))
            {
            return acc;
            }
            else
            {
            var _tco_0 = n;
            var _tco_1 = (i + 1L);
            var _tco_2 = ((Func<List<string>>)(() => { var _l = acc; _l.Add(""); return _l; }))();
            n = _tco_0;
            i = _tco_1;
            acc = _tco_2;
            continue;
            }
        }
    }

    public static List<long> fill_empty_values(long n, long i, List<long> acc)
    {
        while (true)
        {
            if ((i >= n))
            {
            return acc;
            }
            else
            {
            var _tco_0 = n;
            var _tco_1 = (i + 1L);
            var _tco_2 = ((Func<List<long>>)(() => { var _l = acc; _l.Add((0L - 1L)); return _l; }))();
            n = _tco_0;
            i = _tco_1;
            acc = _tco_2;
            continue;
            }
        }
    }

    public static OffsetTable offset_table_empty() => new OffsetTable(keys: fill_empty_keys(offset_table_size(), 0L, _Buf.list_with_capacity(offset_table_size())), values: fill_empty_values(offset_table_size(), 0L, _Buf.list_with_capacity(offset_table_size())));

    public static OffsetTable offset_table_insert_at(List<string> ks, List<long> vs, long slot, string k, long v)
    {
        while (true)
        {
            var existing = ks[(int)slot];
            if ((existing == ""))
            {
            return new OffsetTable(keys: offset_table_text_replace_at(ks, slot, k), values: offset_table_int_replace_at(vs, slot, v));
            }
            else
            {
            if ((existing == k))
            {
            return new OffsetTable(keys: ks, values: offset_table_int_replace_at(vs, slot, v));
            }
            else
            {
            var _tco_0 = ks;
            var _tco_1 = vs;
            var _tco_2 = ((slot + 1L) & offset_table_mask());
            var _tco_3 = k;
            var _tco_4 = v;
            ks = _tco_0;
            vs = _tco_1;
            slot = _tco_2;
            k = _tco_3;
            v = _tco_4;
            continue;
            }
            }
        }
    }

    public static OffsetTable offset_table_set(OffsetTable m, string k, long v) => ((Func<long, OffsetTable>)((h) => ((Func<long, OffsetTable>)((slot) => offset_table_insert_at(m.keys, m.values, slot, k, v)))((h & offset_table_mask()))))(djb2_hash(k));

    public static long offset_table_lookup_probe(List<string> ks, List<long> vs, long slot, string k, long steps)
    {
        while (true)
        {
            if ((steps >= offset_table_size()))
            {
            return (0L - 1L);
            }
            else
            {
            var existing = ks[(int)slot];
            if ((existing == ""))
            {
            return (0L - 1L);
            }
            else
            {
            if ((existing == k))
            {
            return vs[(int)slot];
            }
            else
            {
            var _tco_0 = ks;
            var _tco_1 = vs;
            var _tco_2 = ((slot + 1L) & offset_table_mask());
            var _tco_3 = k;
            var _tco_4 = (steps + 1L);
            ks = _tco_0;
            vs = _tco_1;
            slot = _tco_2;
            k = _tco_3;
            steps = _tco_4;
            continue;
            }
            }
            }
        }
    }

    public static long offset_table_get(OffsetTable m, string k) => ((Func<long, long>)((h) => ((Func<long, long>)((slot) => offset_table_lookup_probe(m.keys, m.values, slot, k, 0L)))((h & offset_table_mask()))))(djb2_hash(k));

    public static List<string> offset_table_text_replace_at(List<string> xs, long idx, string x) => ((Func<List<string>>)(() => { var _l = xs; _l[(int)idx] = x; return _l; }))();

    public static List<long> offset_table_int_replace_at(List<long> xs, long idx, long x) => ((Func<List<long>>)(() => { var _l = xs; _l[(int)idx] = x; return _l; }))();

    public static OffsetTable build_offset_table(List<FuncOffset> offsets) => build_offset_table_loop(offsets, 0L, ((long)offsets.Count), offset_table_empty());

    public static OffsetTable build_offset_table_loop(List<FuncOffset> offsets, long i, long len, OffsetTable m)
    {
        while (true)
        {
            if ((i >= len))
            {
            return m;
            }
            else
            {
            var fo = offsets[(int)i];
            var _tco_0 = offsets;
            var _tco_1 = (i + 1L);
            var _tco_2 = len;
            var _tco_3 = offset_table_set(m, fo.name, fo.offset);
            offsets = _tco_0;
            i = _tco_1;
            len = _tco_2;
            m = _tco_3;
            continue;
            }
        }
    }

    public static long offset_table_lookup(OffsetTable m, string name) => offset_table_get(m, name);

    public static long phase_infrastructure() => 0L;

    public static long phase_lexer() => 1L;

    public static long phase_parser() => 2L;

    public static long phase_prose_validation() => 3L;

    public static long phase_name_resolver() => 4L;

    public static long phase_type_checker() => 5L;

    public static long phase_linearity() => 6L;

    public static long phase_proof_checker() => 7L;

    public static long phase_capability_checker() => 8L;

    public static long phase_codegen() => 9L;

    public static string phase_label(long p) => ((p == phase_infrastructure()) ? "\u002B\u0012\u001C\u0015\u000F\u0013\u000E\u0015\u0019\u0018\u000E\u0019\u0015\u000D" : ((p == phase_lexer()) ? "\u0031\u000D\u0024\u000D\u0015" : ((p == phase_parser()) ? "\u0039\u000F\u0015\u0013\u000D\u0015" : ((p == phase_prose_validation()) ? "\u0039\u0015\u0010\u0013\u000D\u003B\u000F\u0017\u0011\u0016\u000F\u000E\u0011\u0010\u0012" : ((p == phase_name_resolver()) ? "\u002C\u000F\u001A\u000D\u002F\u000D\u0013\u0010\u0017\u0021\u000D\u0015" : ((p == phase_type_checker()) ? "\u0028\u001E\u001F\u000D\u0032\u0014\u000D\u0018\u0022\u000D\u0015" : ((p == phase_linearity()) ? "\u0031\u0011\u0012\u000D\u000F\u0015\u0011\u000E\u001E" : ((p == phase_proof_checker()) ? "\u0039\u0015\u0010\u0010\u001C\u0032\u0014\u000D\u0018\u0022\u000D\u0015" : ((p == phase_capability_checker()) ? "\u0032\u000F\u001F\u000F\u0020\u0011\u0017\u0011\u000E\u001E\u0032\u0014\u000D\u0018\u0022\u000D\u0015" : ((p == phase_codegen()) ? "\u0032\u0010\u0016\u000D\u0037\u000D\u0012" : "\u0033\u0012\u0022\u0012\u0010\u001B\u0012"))))))))));

    public static TextSet set_empty() => new TextSet(items: new List<string>());

    public static TextSet set_insert(TextSet s, string x) => ((Func<List<string>, TextSet>)((items) => ((Func<long, TextSet>)((len) => ((Func<long, TextSet>)((pos) => (((pos < len) && (items[(int)pos] == x)) ? s : new TextSet(items: ((Func<List<string>>)(() => { var _l = new List<string>(items); _l.Insert((int)pos, x); return _l; }))()))))(bsearch_text_set(items, x, 0L, len))))(((long)items.Count))))(s.items);

    public static bool set_contains(TextSet s, string x) => text_set_has(s.items, x);

    public static long set_size(TextSet s) => ((long)s.items.Count);

    public static bool set_is_empty(TextSet s) => (((long)s.items.Count) == 0L);

    public static List<string> set_to_list(TextSet s) => s.items;

    public static TextSet set_from_list(List<string> xs) => set_from_list_loop(xs, 0L, ((long)xs.Count), set_empty());

    public static TextSet set_from_list_loop(List<string> xs, long i, long len, TextSet acc)
    {
        while (true)
        {
            if ((i >= len))
            {
            return acc;
            }
            else
            {
            var _tco_0 = xs;
            var _tco_1 = (i + 1L);
            var _tco_2 = len;
            var _tco_3 = set_insert(acc, xs[(int)i]);
            xs = _tco_0;
            i = _tco_1;
            len = _tco_2;
            acc = _tco_3;
            continue;
            }
        }
    }

    public static long sev_error() => 0L;

    public static long sev_warning() => 1L;

    public static long sev_info() => 2L;

    public static long sev_hint() => 3L;

    public static string severity_label(long s) => ((s == sev_error()) ? "\u000D\u0015\u0015\u0010\u0015" : ((s == sev_warning()) ? "\u001B\u000F\u0015\u0012\u0011\u0012\u001D" : ((s == sev_info()) ? "\u0011\u0012\u001C\u0010" : ((s == sev_hint()) ? "\u0014\u0011\u0012\u000E" : "\u0019\u0012\u0022\u0012\u0010\u001B\u0012"))));

    public static FileTable empty_file_table() => new FileTable(names: new List<string>());

    public static FileTable file_table_add(FileTable table, string name) => new FileTable(names: ((Func<List<string>>)(() => { var _l = table.names; _l.Add(name); return _l; }))());

    public static string file_table_name(FileTable table, long id) => ((id <= 0L) ? "" : ((id > ((long)table.names.Count)) ? "" : table.names[(int)(id - 1L)]));

    public static long file_table_count(FileTable table) => ((long)table.names.Count);

    public static SourcePosition make_position(long line, long col, long offset) => new SourcePosition(line: line, column: col, offset: offset);

    public static SourceSpan make_span(SourcePosition s, SourcePosition e, long fid, Provenance prov) => new SourceSpan(start: s, end: e, file_id: fid, provenance: prov);

    public static long span_length(SourceSpan span) => (span.end.offset - span.start.offset);

    public static SourceSpan synthetic_span() => new SourceSpan(start: make_position(0L, 0L, 0L), end: make_position(0L, 0L, 0L), file_id: 0L, provenance: new ProvSynthetic());

    public static bool is_synthetic_span(SourceSpan span) => (span.file_id == 0L);

    public static SourceSpan span_at(long line, long col, long offset, long len, long fid) => ((Func<SourcePosition, SourceSpan>)((start) => ((Func<SourcePosition, SourceSpan>)((stop) => make_span(start, stop, fid, new ProvParsed())))(make_position(line, (col + len), (offset + len)))))(make_position(line, col, offset));

    public static SourceSpan span_with_provenance(SourceSpan span, Provenance prov) => new SourceSpan(start: span.start, end: span.end, file_id: span.file_id, provenance: prov);

    public static string span_display(SourceSpan span, FileTable table) => (is_synthetic_span(span) ? "" : ((Func<string, string>)((name) => (((name == "") ? "" : (name + "\u0045")) + (_Cce.FromUnicode(span.start.line.ToString()) + ("\u0045" + _Cce.FromUnicode(span.start.column.ToString()))))))(file_table_name(table, span.file_id)));

    public static string pad_zeros_loop(long remaining, string s)
    {
        while (true)
        {
            if ((remaining <= 0L))
            {
            return s;
            }
            else
            {
            var _tco_0 = (remaining - 1L);
            var _tco_1 = ("\u0003" + s);
            remaining = _tco_0;
            s = _tco_1;
            continue;
            }
        }
    }

    public static string integer_to_text_padded(long n, long width) => ((Func<string, string>)((s) => ((Func<long, string>)((len) => ((len >= width) ? s : pad_zeros_loop((width - len), s))))(((long)s.Length))))(_Cce.FromUnicode(n.ToString()));

    public static string emit__csharp_emitter_emit_type_defs(List<ATypeDef> tds, long i) => ((i == ((long)tds.Count)) ? "" : (emit__csharp_emitter_emit_type_def(tds[(int)i]) + ("\u0001" + emit__csharp_emitter_emit_type_defs(tds, (i + 1L)))));

    public static string emit__csharp_emitter_emit_type_def(ATypeDef td) => td switch { ARecordTypeDef(var name, var tparams, var fields, var s) => (string)(((Func<string, string>)((gen) => ("\u001F\u0019\u0020\u0017\u0011\u0018\u0002\u0013\u000D\u000F\u0017\u000D\u0016\u0002\u0015\u000D\u0018\u0010\u0015\u0016\u0002" + (sanitize(name.value) + (gen + ("\u004A" + (emit__csharp_emitter_emit_record_field_defs(fields, tparams, 0L) + "\u004B\u0046\u0001")))))))(emit_tparameter_suffix(tparams))), AVariantTypeDef(var name, var tparams, var ctors, var s) => (string)(((Func<string, string>)((gen) => ("\u001F\u0019\u0020\u0017\u0011\u0018\u0002\u000F\u0020\u0013\u000E\u0015\u000F\u0018\u000E\u0002\u0015\u000D\u0018\u0010\u0015\u0016\u0002" + (sanitize(name.value) + (gen + ("\u0046\u0001" + (emit__csharp_emitter_emit_variant_ctors(ctors, name, tparams, 0L) + "\u0001")))))))(emit_tparameter_suffix(tparams))), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static string emit_tparameter_suffix(List<Name> tparams) => ((((long)tparams.Count) == 0L) ? "" : ("\u004F" + (emit_tparameter_names(tparams, 0L) + "\u0050")));

    public static string emit_tparameter_names(List<Name> tparams, long i) => ((i == ((long)tparams.Count)) ? "" : ((i == (((long)tparams.Count) - 1L)) ? ("\u0028" + _Cce.FromUnicode(i.ToString())) : ("\u0028" + (_Cce.FromUnicode(i.ToString()) + ("\u0042\u0002" + emit_tparameter_names(tparams, (i + 1L)))))));

    public static string emit__csharp_emitter_emit_record_field_defs(List<ARecordFieldDef> fields, List<Name> tparams, long i) => ((i == ((long)fields.Count)) ? "" : ((Func<ARecordFieldDef, string>)((f) => (emit_type_expr_tp(f.type_expr, tparams) + ("\u0002" + (sanitize(f.name.value) + (((i < (((long)fields.Count) - 1L)) ? "\u0042\u0002" : "") + emit__csharp_emitter_emit_record_field_defs(fields, tparams, (i + 1L))))))))(fields[(int)i]));

    public static string emit__csharp_emitter_emit_variant_ctors(List<AVariantCtorDef> ctors, Name base_name, List<Name> tparams, long i) => ((i == ((long)ctors.Count)) ? "" : ((Func<AVariantCtorDef, string>)((c) => (emit_variant_ctor(c, base_name, tparams) + emit__csharp_emitter_emit_variant_ctors(ctors, base_name, tparams, (i + 1L)))))(ctors[(int)i]));

    public static string emit_variant_ctor(AVariantCtorDef c, Name base_name, List<Name> tparams) => ((Func<string, string>)((gen) => ((((long)c.fields.Count) == 0L) ? ("\u001F\u0019\u0020\u0017\u0011\u0018\u0002\u0013\u000D\u000F\u0017\u000D\u0016\u0002\u0015\u000D\u0018\u0010\u0015\u0016\u0002" + (sanitize(c.name.value) + (gen + ("\u0002\u0045\u0002" + (sanitize(base_name.value) + (gen + "\u0046\u0001")))))) : ("\u001F\u0019\u0020\u0017\u0011\u0018\u0002\u0013\u000D\u000F\u0017\u000D\u0016\u0002\u0015\u000D\u0018\u0010\u0015\u0016\u0002" + (sanitize(c.name.value) + (gen + ("\u004A" + (emit__csharp_emitter_emit_ctor_fields(c.fields, tparams, 0L) + ("\u004B\u0002\u0045\u0002" + (sanitize(base_name.value) + (gen + "\u0046\u0001")))))))))))(emit_tparameter_suffix(tparams));

    public static string emit__csharp_emitter_emit_ctor_fields(List<ATypeExpr> fields, List<Name> tparams, long i) => ((i == ((long)fields.Count)) ? "" : (emit_type_expr_tp(fields[(int)i], tparams) + ("\u0002\u0036\u0011\u000D\u0017\u0016" + (_Cce.FromUnicode(i.ToString()) + (((i < (((long)fields.Count) - 1L)) ? "\u0042\u0002" : "") + emit__csharp_emitter_emit_ctor_fields(fields, tparams, (i + 1L)))))));

    public static string emit__csharp_emitter_emit_type_expr(ATypeExpr te) => emit_type_expr_tp(te, new List<Name>());

    public static string emit_type_expr_tp(ATypeExpr te, List<Name> tparams) => te switch { ANamedType(var name, var s) => (string)(((Func<long, string>)((idx) => ((idx >= 0L) ? ("\u0028" + _Cce.FromUnicode(idx.ToString())) : when_type_name(name.value))))(find_tparam_index(tparams, name.value, 0L))), AFunType(var p, var r, var s) => (string)(("\u0036\u0019\u0012\u0018\u004F" + (emit_type_expr_tp(p, tparams) + ("\u0042\u0002" + (emit_type_expr_tp(r, tparams) + "\u0050"))))), AAppType(var @base, var args, var s) => (string)((emit_type_expr_tp(@base, tparams) + ("\u004F" + (emit_type_expr_list_tp(args, tparams, 0L) + "\u0050")))), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static long find_tparam_index(List<Name> tparams, string name, long i)
    {
        while (true)
        {
            if ((i == ((long)tparams.Count)))
            {
            return (-1L);
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
            var _tco_2 = (i + 1L);
            tparams = _tco_0;
            name = _tco_1;
            i = _tco_2;
            continue;
            }
            }
        }
    }

    public static string when_type_name(string n) => ((n == "\u002B\u0012\u000E\u000D\u001D\u000D\u0015") ? "\u0017\u0010\u0012\u001D" : ((n == "\u002C\u0019\u001A\u0020\u000D\u0015") ? "\u0016\u0010\u0019\u0020\u0017\u000D" : ((n == "\u0028\u000D\u0024\u000E") ? "\u0013\u000E\u0015\u0011\u0012\u001D" : ((n == "\u003A\u0010\u0010\u0017\u000D\u000F\u0012") ? "\u0020\u0010\u0010\u0017" : ((n == "\u0031\u0011\u0013\u000E") ? "\u0031\u0011\u0013\u000E" : ((n == "\u0031\u0011\u0012\u0022\u000D\u0016\u0031\u0011\u0013\u000E") ? "\u0031\u0011\u0013\u000E" : sanitize(n)))))));

    public static string emit_type_expr_list(List<ATypeExpr> args, long i) => ((i == ((long)args.Count)) ? "" : (emit__csharp_emitter_emit_type_expr(args[(int)i]) + (((i < (((long)args.Count) - 1L)) ? "\u0042\u0002" : "") + emit_type_expr_list(args, (i + 1L)))));

    public static string emit_type_expr_list_tp(List<ATypeExpr> args, List<Name> tparams, long i) => ((i == ((long)args.Count)) ? "" : (emit_type_expr_tp(args[(int)i], tparams) + (((i < (((long)args.Count) - 1L)) ? "\u0042\u0002" : "") + emit_type_expr_list_tp(args, tparams, (i + 1L)))));

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

    public static List<long> collect_type_var_ids_list(List<CodexType> types, List<long> acc) => collect_type_var_ids_list_loop(types, acc, 0L, ((long)types.Count));

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
            var _tco_2 = (i + 1L);
            var _tco_3 = len;
            types = _tco_0;
            acc = _tco_1;
            i = _tco_2;
            len = _tco_3;
            continue;
            }
        }
    }

    public static bool list_contains_int(List<long> xs, long n) => list_contains_int_loop(xs, n, 0L, ((long)xs.Count));

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
            var _tco_2 = (i + 1L);
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

    public static string generic_suffix(CodexType ty) => ((Func<List<long>, string>)((ids) => ((((long)ids.Count) == 0L) ? "" : ("\u004F" + (emit_type_params(ids, 0L) + "\u0050")))))(collect_type_var_ids(ty, new List<long>()));

    public static string emit_type_params(List<long> ids, long i) => ((i == ((long)ids.Count)) ? "" : ((i == (((long)ids.Count) - 1L)) ? ("\u0028" + _Cce.FromUnicode(ids[(int)i].ToString())) : ("\u0028" + (_Cce.FromUnicode(ids[(int)i].ToString()) + ("\u0042\u0002" + emit_type_params(ids, (i + 1L)))))));

    public static string extract_ctor_type_args(CodexType ty) => ty switch { ConstructedTy(var name, var args) => (string)(((((long)args.Count) == 0L) ? "" : ("\u004F" + (emit_cs_type_args(args, 0L) + "\u0050")))), _ => (string)(""), };

    public static bool emit__csharp_emitter_is_self_call(IRExpr e, string func_name) => ((Func<ApplyChain, bool>)((chain) => is_self_call_root(chain.root, func_name)))(collect_apply_chain(e, new List<IRExpr>()));

    public static bool is_self_call_root(IRExpr e, string func_name) => e switch { IrName(var n, var ty, var sp) => (bool)((n == func_name)), _ => (bool)(false), };

    public static bool emit__csharp_emitter_has_tail_call(IRExpr e, string func_name)
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
                var sp = _tco_m0.Field4;
            return (emit__csharp_emitter_has_tail_call(t, func_name) || emit__csharp_emitter_has_tail_call(el, func_name));
            }
            else if (_tco_s is IrLet _tco_m1)
            {
                var name = _tco_m1.Field0;
                var ty = _tco_m1.Field1;
                var val = _tco_m1.Field2;
                var body = _tco_m1.Field3;
                var sp = _tco_m1.Field4;
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
                var sp = _tco_m2.Field3;
            return emit__csharp_emitter_has_tail_call_branches(branches, func_name, 0L);
            }
            else if (_tco_s is IrApply _tco_m3)
            {
                var f = _tco_m3.Field0;
                var a = _tco_m3.Field1;
                var ty = _tco_m3.Field2;
                var sp = _tco_m3.Field3;
            return emit__csharp_emitter_is_self_call(e, func_name);
            }
            {
            return false;
            }
        }
    }

    public static bool emit__csharp_emitter_has_tail_call_branches(List<IRBranch> branches, string func_name, long i)
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
            if (emit__csharp_emitter_has_tail_call(b.body, func_name))
            {
            return true;
            }
            else
            {
            var _tco_0 = branches;
            var _tco_1 = func_name;
            var _tco_2 = (i + 1L);
            branches = _tco_0;
            func_name = _tco_1;
            i = _tco_2;
            continue;
            }
            }
        }
    }

    public static bool emit__csharp_emitter_should_tco(IRDef d) => ((((long)d.@params.Count) == 0L) ? false : emit__csharp_emitter_has_tail_call(d.body, d.name));

    public static string emit_tco_def(IRDef d, List<ArityEntry> arities) => ((Func<CodexType, string>)((ret) => ((Func<string, string>)((gen) => ("\u0002\u0002\u0002\u0002\u001F\u0019\u0020\u0017\u0011\u0018\u0002\u0013\u000E\u000F\u000E\u0011\u0018\u0002" + (cs_type(ret) + ("\u0002" + (sanitize(d.name) + (gen + ("\u004A" + (emit__csharp_emitter_emit_def_params(d.@params, 0L) + ("\u004B\u0001\u0002\u0002\u0002\u0002\u005A\u0001\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u001B\u0014\u0011\u0017\u000D\u0002\u004A\u000E\u0015\u0019\u000D\u004B\u0001\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u005A\u0001" + (emit_tco_body(d.body, d.name, d.@params, arities) + "\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u005B\u0001\u0002\u0002\u0002\u0002\u005B\u0001")))))))))))(generic_suffix(d.type_val))))(get_return_type(d.type_val, ((long)d.@params.Count)));

    public static string emit_tco_body(IRExpr e, string func_name, List<IRParam> @params, List<ArityEntry> arities) => e switch { IrIf(var c, var t, var el, var ty, var sp) => (string)(emit_tco_if(c, t, el, func_name, @params, arities)), IrLet(var name, var ty, var val, var body, var sp) => (string)(emit_tco_let(name, ty, val, body, func_name, @params, arities)), IrMatch(var scrut, var branches, var ty, var sp) => (string)(emit_tco_match(scrut, branches, func_name, @params, arities)), IrApply(var f, var a, var rty, var sp) => (string)(emit_tco_apply(e, func_name, @params, arities)), _ => (string)(("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0015\u000D\u000E\u0019\u0015\u0012\u0002" + (emit__csharp_emitter_emit_expr(e, arities) + "\u0046\u0001"))), };

    public static string emit_tco_apply(IRExpr e, string func_name, List<IRParam> @params, List<ArityEntry> arities) => (emit__csharp_emitter_is_self_call(e, func_name) ? emit_tco_jump(e, @params, arities) : ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0015\u000D\u000E\u0019\u0015\u0012\u0002" + (emit__csharp_emitter_emit_expr(e, arities) + "\u0046\u0001")));

    public static string emit_tco_if(IRExpr cond, IRExpr t, IRExpr el, string func_name, List<IRParam> @params, List<ArityEntry> arities) => ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0011\u001C\u0002\u004A" + (emit__csharp_emitter_emit_expr(cond, arities) + ("\u004B\u0001\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u005A\u0001" + (emit_tco_body(t, func_name, @params, arities) + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u005B\u0001\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u000D\u0017\u0013\u000D\u0001\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u005A\u0001" + (emit_tco_body(el, func_name, @params, arities) + "\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u005B\u0001"))))));

    public static string emit_tco_let(string name, CodexType ty, IRExpr val, IRExpr body, string func_name, List<IRParam> @params, List<ArityEntry> arities) => ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0021\u000F\u0015\u0002" + (sanitize(name) + ("\u0002\u004D\u0002" + (emit__csharp_emitter_emit_expr(val, arities) + ("\u0046\u0001" + emit_tco_body(body, func_name, @params, arities))))));

    public static string emit_tco_match(IRExpr scrut, List<IRBranch> branches, string func_name, List<IRParam> @params, List<ArityEntry> arities) => ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0021\u000F\u0015\u0002\u0055\u000E\u0018\u0010\u0055\u0013\u0002\u004D\u0002" + (emit__csharp_emitter_emit_expr(scrut, arities) + ("\u0046\u0001" + emit_tco_match_branches(branches, func_name, @params, arities, 0L, true))));

    public static string emit_tco_match_branches(List<IRBranch> branches, string func_name, List<IRParam> @params, List<ArityEntry> arities, long i, bool is_first) => ((i == ((long)branches.Count)) ? "" : ((Func<IRBranch, string>)((b) => (emit_tco_match_branch(b, func_name, @params, arities, i, is_first) + emit_tco_match_branches(branches, func_name, @params, arities, (i + 1L), false))))(branches[(int)i]));

    public static string emit_tco_match_branch(IRBranch b, string func_name, List<IRParam> @params, List<ArityEntry> arities, long idx, bool is_first) => b.pattern switch { IrWildPat(var sp) => (string)(("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u005A\u0001" + (emit_tco_body(b.body, func_name, @params, arities) + "\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u005B\u0001"))), IrVarPat(var name, var ty, var sp) => (string)(("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u005A\u0001\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0021\u000F\u0015\u0002" + (sanitize(name) + ("\u0002\u004D\u0002\u0055\u000E\u0018\u0010\u0055\u0013\u0046\u0001" + (emit_tco_body(b.body, func_name, @params, arities) + "\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u005B\u0001"))))), IrCtorPat(var name, var subs, var ty, var sp) => (string)(((Func<string, string>)((keyword) => ((Func<string, string>)((match_var) => ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002" + (keyword + ("\u0002\u004A\u0055\u000E\u0018\u0010\u0055\u0013\u0002\u0011\u0013\u0002" + (sanitize(name) + (extract_ctor_type_args(ty) + ("\u0002" + (match_var + ("\u004B\u0001\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u005A\u0001" + (emit_tco_ctor_bindings(subs, match_var, 0L) + (emit_tco_body(b.body, func_name, @params, arities) + "\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u005B\u0001"))))))))))))(("\u0055\u000E\u0018\u0010\u0055\u001A" + _Cce.FromUnicode(idx.ToString())))))((is_first ? "\u0011\u001C" : "\u000D\u0017\u0013\u000D\u0002\u0011\u001C"))), IrLitPat(var text, var ty, var sp) => (string)(((Func<string, string>)((keyword) => ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002" + (keyword + ("\u0002\u004A\u0010\u0020\u0023\u000D\u0018\u000E\u0041\u0027\u0025\u0019\u000F\u0017\u0013\u004A\u0055\u000E\u0018\u0010\u0055\u0013\u0042\u0002" + (text + ("\u004B\u004B\u0001\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u005A\u0001" + (emit_tco_body(b.body, func_name, @params, arities) + "\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u005B\u0001"))))))))((is_first ? "\u0011\u001C" : "\u000D\u0017\u0013\u000D\u0002\u0011\u001C"))), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static string emit_tco_ctor_bindings(List<IRPat> subs, string match_var, long i) => ((i == ((long)subs.Count)) ? "" : ((Func<IRPat, string>)((sub) => (emit_tco_ctor_binding(sub, match_var, i) + emit_tco_ctor_bindings(subs, match_var, (i + 1L)))))(subs[(int)i]));

    public static string emit_tco_ctor_binding(IRPat sub, string match_var, long i) => sub switch { IrVarPat(var name, var ty, var sp) => (string)(("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0021\u000F\u0015\u0002" + (sanitize(name) + ("\u0002\u004D\u0002" + (match_var + ("\u0041\u0036\u0011\u000D\u0017\u0016" + (_Cce.FromUnicode(i.ToString()) + "\u0046\u0001"))))))), _ => (string)(""), };

    public static string emit_tco_jump(IRExpr e, List<IRParam> @params, List<ArityEntry> arities) => ((Func<ApplyChain, string>)((chain) => (emit_tco_temps(chain.args, arities, 0L) + (emit_tco_assigns(@params, 0L) + "\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0018\u0010\u0012\u000E\u0011\u0012\u0019\u000D\u0046\u0001"))))(collect_apply_chain(e, new List<IRExpr>()));

    public static string emit_tco_temps(List<IRExpr> args, List<ArityEntry> arities, long i) => ((i == ((long)args.Count)) ? "" : ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0021\u000F\u0015\u0002\u0055\u000E\u0018\u0010\u0055" + (_Cce.FromUnicode(i.ToString()) + ("\u0002\u004D\u0002" + (emit__csharp_emitter_emit_expr(args[(int)i], arities) + ("\u0046\u0001" + emit_tco_temps(args, arities, (i + 1L))))))));

    public static string emit_tco_assigns(List<IRParam> @params, long i) => ((i == ((long)@params.Count)) ? "" : ((Func<IRParam, string>)((p) => ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002" + (sanitize(p.name) + ("\u0002\u004D\u0002\u0055\u000E\u0018\u0010\u0055" + (_Cce.FromUnicode(i.ToString()) + ("\u0046\u0001" + emit_tco_assigns(@params, (i + 1L)))))))))(@params[(int)i]));

    public static string emit__csharp_emitter_emit_def(IRDef d, List<ArityEntry> arities) => (emit__csharp_emitter_should_tco(d) ? emit_tco_def(d, arities) : ((Func<CodexType, string>)((ret) => ((Func<string, string>)((gen) => ("\u0002\u0002\u0002\u0002\u001F\u0019\u0020\u0017\u0011\u0018\u0002\u0013\u000E\u000F\u000E\u0011\u0018\u0002" + (cs_type(ret) + ("\u0002" + (sanitize(d.name) + (gen + ("\u004A" + (emit__csharp_emitter_emit_def_params(d.@params, 0L) + ("\u004B\u0002\u004D\u0050\u0002" + (emit__csharp_emitter_emit_expr(d.body, arities) + "\u0046\u0001")))))))))))(generic_suffix(d.type_val))))(get_return_type(d.type_val, ((long)d.@params.Count))));

    public static CodexType get_return_type(CodexType ty, long n)
    {
        while (true)
        {
            if ((n == 0L))
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
            var _tco_1 = (n - 1L);
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

    public static string emit__csharp_emitter_emit_def_params(List<IRParam> @params, long i) => ((i == ((long)@params.Count)) ? "" : ((Func<IRParam, string>)((p) => (cs_type(p.type_val) + ("\u0002" + (sanitize(p.name) + (((i < (((long)@params.Count) - 1L)) ? "\u0042\u0002" : "") + emit__csharp_emitter_emit_def_params(@params, (i + 1L))))))))(@params[(int)i]));

    public static string emit_cce_runtime() => ("\u0013\u000E\u000F\u000E\u0011\u0018\u0002\u0018\u0017\u000F\u0013\u0013\u0002\u0055\u0032\u0018\u000D\u0002\u005A\u0001" + ("\u0002\u0002\u0002\u0002\u0013\u000E\u000F\u000E\u0011\u0018\u0002\u0015\u000D\u000F\u0016\u0010\u0012\u0017\u001E\u0002\u0011\u0012\u000E\u0058\u0059\u0002\u0055\u000E\u0010\u0033\u0012\u0011\u0002\u004D\u0002\u005A\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0003\u0042\u0002\u0004\u0003\u0042\u0002\u0006\u0005\u0042\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0007\u000B\u0042\u0002\u0007\u000C\u0042\u0002\u0008\u0003\u0042\u0002\u0008\u0004\u0042\u0002\u0008\u0005\u0042\u0002\u0008\u0006\u0042\u0002\u0008\u0007\u0042\u0002\u0008\u0008\u0042\u0002\u0008\u0009\u0042\u0002\u0008\u000A\u0042\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0004\u0003\u0004\u0042\u0002\u0004\u0004\u0009\u0042\u0002\u000C\u000A\u0042\u0002\u0004\u0004\u0004\u0042\u0002\u0004\u0003\u0008\u0042\u0002\u0004\u0004\u0003\u0042\u0002\u0004\u0004\u0008\u0042\u0002\u0004\u0003\u0007\u0042\u0002\u0004\u0004\u0007\u0042\u0002\u0004\u0003\u0003\u0042\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0004\u0003\u000B\u0042\u0002\u000C\u000C\u0042\u0002\u0004\u0004\u000A\u0042\u0002\u0004\u0003\u000C\u0042\u0002\u0004\u0004\u000C\u0042\u0002\u0004\u0003\u0005\u0042\u0002\u0004\u0003\u0006\u0042\u0002\u0004\u0005\u0004\u0042\u0002\u0004\u0004\u0005\u0042\u0002\u000C\u000B\u0042\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0004\u0004\u000B\u0042\u0002\u0004\u0003\u000A\u0042\u0002\u0004\u0003\u0009\u0042\u0002\u0004\u0005\u0003\u0042\u0002\u0004\u0004\u0006\u0042\u0002\u0004\u0005\u0005\u0042\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0009\u000C\u0042\u0002\u000B\u0007\u0042\u0002\u0009\u0008\u0042\u0002\u000A\u000C\u0042\u0002\u000A\u0006\u0042\u0002\u000A\u000B\u0042\u0002\u000B\u0006\u0042\u0002\u000A\u0005\u0042\u0002\u000B\u0005\u0042\u0002\u0009\u000B\u0042\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u000A\u0009\u0042\u0002\u0009\u000A\u0042\u0002\u000B\u0008\u0042\u0002\u000A\u000A\u0042\u0002\u000B\u000A\u0042\u0002\u000A\u0003\u0042\u0002\u000A\u0004\u0042\u0002\u000B\u000C\u0042\u0002\u000B\u0003\u0042\u0002\u0009\u0009\u0042\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u000B\u0009\u0042\u0002\u000A\u0008\u0042\u0002\u000A\u0007\u0042\u0002\u000B\u000B\u0042\u0002\u000B\u0004\u0042\u0002\u000C\u0003\u0042\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0007\u0009\u0042\u0002\u0007\u0007\u0042\u0002\u0006\u0006\u0042\u0002\u0009\u0006\u0042\u0002\u0008\u000B\u0042\u0002\u0008\u000C\u0042\u0002\u0006\u000C\u0042\u0002\u0006\u0007\u0042\u0002\u0007\u0008\u0042\u0002\u0007\u0003\u0042\u0002\u0007\u0004\u0042\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0007\u0006\u0042\u0002\u0009\u0004\u0042\u0002\u0007\u0005\u0042\u0002\u0009\u0003\u0042\u0002\u0009\u0005\u0042\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0007\u000A\u0042\u0002\u0009\u0007\u0042\u0002\u0006\u0008\u0042\u0002\u0006\u000B\u0042\u0002\u000C\u0008\u0042\u0002\u000C\u0005\u0042\u0002\u0004\u0005\u0007\u0042\u0002\u000C\u0004\u0042\u0002\u000C\u0006\u0042\u0002\u0004\u0005\u0006\u0042\u0002\u0004\u0005\u0008\u0042\u0002\u0004\u0005\u0009\u0042\u0002\u000C\u0009\u0042\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u000C\u0007\u0042\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0005\u0006\u0006\u0042\u0002\u0005\u0006\u0005\u0042\u0002\u0005\u0006\u0007\u0042\u0002\u0005\u0006\u0008\u0042\u0002\u0005\u0005\u0008\u0042\u0002\u0005\u0005\u0007\u0042\u0002\u0005\u0005\u0009\u0042\u0002\u0005\u0005\u000B\u0042\u0002\u0005\u0007\u0006\u0042\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0005\u0007\u0007\u0042\u0002\u0005\u0007\u0009\u0042\u0002\u0005\u0008\u0003\u0042\u0002\u0005\u0007\u000C\u0042\u0002\u0005\u0008\u0004\u0042\u0002\u0005\u0008\u0005\u0042\u0002\u0005\u0007\u0004\u0042\u0002\u0005\u0006\u0004\u0042\u0002\u0005\u0006\u000A\u0042\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0004\u0003\u000A\u0005\u0042\u0002\u0004\u0003\u000B\u0009\u0042\u0002\u0004\u0003\u000A\u000A\u0042\u0002\u0004\u0003\u000B\u0003\u0042\u0002\u0004\u0003\u000B\u0008\u0042\u0002\u0004\u0003\u000C\u0003\u0042\u0002\u0004\u0003\u000B\u000C\u0042\u0002\u0004\u0003\u000B\u000B\u0042\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0004\u0003\u000A\u0007\u0042\u0002\u0004\u0003\u000B\u0006\u0042\u0002\u0004\u0003\u000B\u0005\u0042\u0002\u0004\u0003\u000B\u0007\u0042\u0002\u0004\u0003\u000A\u0009\u0042\u0002\u0004\u0003\u000B\u000A\u0042\u0002\u0004\u0003\u000C\u0004\u0001" + ("\u0002\u0002\u0002\u0002\u005B\u0046\u0001" + ("\u0002\u0002\u0002\u0002\u0013\u000E\u000F\u000E\u0011\u0018\u0002\u0015\u000D\u000F\u0016\u0010\u0012\u0017\u001E\u0002\u0030\u0011\u0018\u000E\u0011\u0010\u0012\u000F\u0015\u001E\u004F\u0011\u0012\u000E\u0042\u0002\u0011\u0012\u000E\u0050\u0002\u0055\u001C\u0015\u0010\u001A\u0033\u0012\u0011\u0002\u004D\u0002\u0012\u000D\u001B\u004A\u004B\u0046\u0001" + ("\u0002\u0002\u0002\u0002\u0013\u000E\u000F\u000E\u0011\u0018\u0002\u0055\u0032\u0018\u000D\u004A\u004B\u0002\u005A\u0002\u001C\u0010\u0015\u0002\u004A\u0011\u0012\u000E\u0002\u0011\u0002\u004D\u0002\u0003\u0046\u0002\u0011\u0002\u004F\u0002\u0004\u0005\u000B\u0046\u0002\u0011\u004C\u004C\u004B\u0002\u0055\u001C\u0015\u0010\u001A\u0033\u0012\u0011\u0058\u0055\u000E\u0010\u0033\u0012\u0011\u0058\u0011\u0059\u0059\u0002\u004D\u0002\u0011\u0046\u0002\u005B\u0001" + ("\u0002\u0002\u0002\u0002\u001F\u0019\u0020\u0017\u0011\u0018\u0002\u0013\u000E\u000F\u000E\u0011\u0018\u0002\u0013\u000E\u0015\u0011\u0012\u001D\u0002\u0036\u0015\u0010\u001A\u0033\u0012\u0011\u0018\u0010\u0016\u000D\u004A\u0013\u000E\u0015\u0011\u0012\u001D\u0002\u0013\u004B\u0002\u005A\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0013\u0002\u004D\u0002\u0013\u0041\u002F\u000D\u001F\u0017\u000F\u0018\u000D\u004A\u0048\u0056\u000E\u0048\u0042\u0002\u0048\u0002\u0002\u0048\u004B\u0041\u002F\u000D\u001F\u0017\u000F\u0018\u000D\u004A\u0048\u0056\u0015\u0048\u0042\u0002\u0048\u0048\u004B\u0046\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0021\u000F\u0015\u0002\u0013\u0020\u0002\u004D\u0002\u0012\u000D\u001B\u0002\u002D\u001E\u0013\u000E\u000D\u001A\u0041\u0028\u000D\u0024\u000E\u0041\u002D\u000E\u0015\u0011\u0012\u001D\u003A\u0019\u0011\u0017\u0016\u000D\u0015\u004A\u0013\u0041\u0031\u000D\u0012\u001D\u000E\u0014\u004B\u0046\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u001C\u0010\u0015\u0002\u004A\u0011\u0012\u000E\u0002\u0011\u0002\u004D\u0002\u0003\u0046\u0002\u0011\u0002\u004F\u0002\u0013\u0041\u0031\u000D\u0012\u001D\u000E\u0014\u0046\u0002\u0011\u004C\u004C\u004B\u0002\u005A\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0011\u0012\u000E\u0002\u0019\u0002\u004D\u0002\u0013\u0058\u0011\u0059\u0046\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0011\u001C\u0002\u004A\u0055\u001C\u0015\u0010\u001A\u0033\u0012\u0011\u0041\u0028\u0015\u001E\u0037\u000D\u000E\u003B\u000F\u0017\u0019\u000D\u004A\u0019\u0042\u0002\u0010\u0019\u000E\u0002\u0011\u0012\u000E\u0002\u0020\u004B\u004B\u0002\u0013\u0020\u0041\u0029\u001F\u001F\u000D\u0012\u0016\u004A\u004A\u0018\u0014\u000F\u0015\u004B\u0020\u004B\u0046\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u000D\u0017\u0013\u000D\u0002\u0011\u001C\u0002\u004A\u0019\u0002\u0050\u0002\u0004\u0005\u000A\u004B\u0002\u005A\u0002\u0013\u0020\u0041\u0029\u001F\u001F\u000D\u0012\u0016\u004A\u004A\u0018\u0014\u000F\u0015\u004B\u004A\u0003\u0024\u0027\u0003\u0057\u004A\u0019\u0050\u0050\u0004\u0005\u004B\u004B\u004B\u0046\u0002\u0013\u0020\u0041\u0029\u001F\u001F\u000D\u0012\u0016\u004A\u004A\u0018\u0014\u000F\u0015\u004B\u004A\u0003\u0024\u000B\u0003\u0057\u004A\u004A\u0019\u0050\u0050\u0009\u004B\u0054\u0003\u0024\u0006\u0036\u004B\u004B\u004B\u0046\u0002\u0013\u0020\u0041\u0029\u001F\u001F\u000D\u0012\u0016\u004A\u004A\u0018\u0014\u000F\u0015\u004B\u004A\u0003\u0024\u000B\u0003\u0057\u004A\u0019\u0054\u0003\u0024\u0006\u0036\u004B\u004B\u004B\u0046\u0002\u005B\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u000D\u0017\u0013\u000D\u0002\u0013\u0020\u0041\u0029\u001F\u001F\u000D\u0012\u0016\u004A\u004A\u0018\u0014\u000F\u0015\u004B\u0009\u000B\u004B\u0046\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u005B\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0015\u000D\u000E\u0019\u0015\u0012\u0002\u0013\u0020\u0041\u0028\u0010\u002D\u000E\u0015\u0011\u0012\u001D\u004A\u004B\u0046\u0001" + ("\u0002\u0002\u0002\u0002\u005B\u0001" + ("\u0002\u0002\u0002\u0002\u001F\u0019\u0020\u0017\u0011\u0018\u0002\u0013\u000E\u000F\u000E\u0011\u0018\u0002\u0013\u000E\u0015\u0011\u0012\u001D\u0002\u0028\u0010\u0033\u0012\u0011\u0018\u0010\u0016\u000D\u004A\u0013\u000E\u0015\u0011\u0012\u001D\u0002\u0013\u004B\u0002\u005A\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0021\u000F\u0015\u0002\u0013\u0020\u0002\u004D\u0002\u0012\u000D\u001B\u0002\u002D\u001E\u0013\u000E\u000D\u001A\u0041\u0028\u000D\u0024\u000E\u0041\u002D\u000E\u0015\u0011\u0012\u001D\u003A\u0019\u0011\u0017\u0016\u000D\u0015\u004A\u0013\u0041\u0031\u000D\u0012\u001D\u000E\u0014\u004B\u0046\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u001C\u0010\u0015\u0002\u004A\u0011\u0012\u000E\u0002\u0011\u0002\u004D\u0002\u0003\u0046\u0002\u0011\u0002\u004F\u0002\u0013\u0041\u0031\u000D\u0012\u001D\u000E\u0014\u0046\u0002\u0011\u004C\u004C\u004B\u0002\u005A\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0011\u0012\u000E\u0002\u0020\u0002\u004D\u0002\u0013\u0058\u0011\u0059\u0046\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0011\u001C\u0002\u004A\u0020\u0002\u004F\u0002\u0004\u0005\u000B\u004B\u0002\u0013\u0020\u0041\u0029\u001F\u001F\u000D\u0012\u0016\u004A\u004A\u0018\u0014\u000F\u0015\u004B\u0055\u000E\u0010\u0033\u0012\u0011\u0058\u0020\u0059\u004B\u0046\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u000D\u0017\u0013\u000D\u0002\u0011\u001C\u0002\u004A\u004A\u0020\u0002\u0054\u0002\u0003\u0024\u0027\u0003\u004B\u0002\u004D\u004D\u0002\u0003\u0024\u0027\u0003\u0002\u0054\u0054\u0002\u0011\u004C\u0005\u0002\u004F\u0002\u0013\u0041\u0031\u000D\u0012\u001D\u000E\u0014\u004B\u0002\u005A\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0011\u0012\u000E\u0002\u0019\u0002\u004D\u0002\u004A\u004A\u0020\u0002\u0054\u0002\u0003\u0024\u0003\u0036\u004B\u0002\u004F\u004F\u0002\u0004\u0005\u004B\u0002\u0057\u0002\u004A\u004A\u0013\u0058\u0011\u004C\u0004\u0059\u0002\u0054\u0002\u0003\u0024\u0006\u0036\u004B\u0002\u004F\u004F\u0002\u0009\u004B\u0002\u0057\u0002\u004A\u0013\u0058\u0011\u004C\u0005\u0059\u0002\u0054\u0002\u0003\u0024\u0006\u0036\u004B\u0046\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0013\u0020\u0041\u0029\u001F\u001F\u000D\u0012\u0016\u004A\u004A\u0018\u0014\u000F\u0015\u004B\u0019\u004B\u0046\u0002\u0011\u0002\u004C\u004D\u0002\u0005\u0046\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u005B\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u000D\u0017\u0013\u000D\u0002\u0013\u0020\u0041\u0029\u001F\u001F\u000D\u0012\u0016\u004A\u0047\u0056\u0019\u0036\u0036\u0036\u0030\u0047\u004B\u0046\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u005B\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0015\u000D\u000E\u0019\u0015\u0012\u0002\u0013\u0020\u0041\u0028\u0010\u002D\u000E\u0015\u0011\u0012\u001D\u004A\u004B\u0046\u0001" + ("\u0002\u0002\u0002\u0002\u005B\u0001" + ("\u0002\u0002\u0002\u0002\u001F\u0019\u0020\u0017\u0011\u0018\u0002\u0013\u000E\u000F\u000E\u0011\u0018\u0002\u0017\u0010\u0012\u001D\u0002\u0033\u0012\u0011\u0028\u0010\u0032\u0018\u000D\u004A\u0017\u0010\u0012\u001D\u0002\u0019\u004B\u0002\u005A\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0015\u000D\u000E\u0019\u0015\u0012\u0002\u0055\u001C\u0015\u0010\u001A\u0033\u0012\u0011\u0041\u0028\u0015\u001E\u0037\u000D\u000E\u003B\u000F\u0017\u0019\u000D\u004A\u004A\u0011\u0012\u000E\u004B\u0019\u0042\u0002\u0010\u0019\u000E\u0002\u0011\u0012\u000E\u0002\u0018\u004B\u0002\u0044\u0002\u0018\u0002\u0045\u0002\u0009\u000B\u0046\u0001" + ("\u0002\u0002\u0002\u0002\u005B\u0001" + ("\u0002\u0002\u0002\u0002\u001F\u0019\u0020\u0017\u0011\u0018\u0002\u0013\u000E\u000F\u000E\u0011\u0018\u0002\u0017\u0010\u0012\u001D\u0002\u0032\u0018\u000D\u0028\u0010\u0033\u0012\u0011\u004A\u0017\u0010\u0012\u001D\u0002\u0020\u004B\u0002\u005A\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0015\u000D\u000E\u0019\u0015\u0012\u0002\u004A\u0020\u0002\u0050\u004D\u0002\u0003\u0002\u0054\u0054\u0002\u0020\u0002\u004F\u0002\u0004\u0005\u000B\u004B\u0002\u0044\u0002\u0055\u000E\u0010\u0033\u0012\u0011\u0058\u004A\u0011\u0012\u000E\u004B\u0020\u0059\u0002\u0045\u0002\u0009\u0008\u0008\u0006\u0006\u0046\u0001" + ("\u0002\u0002\u0002\u0002\u005B\u0001" + "\u005B\u0001\u0001")))))))))))))))))))))))))))))))))))))))))))))))))));

    public static string emit_buf_runtime() => ("\u0013\u000E\u000F\u000E\u0011\u0018\u0002\u0018\u0017\u000F\u0013\u0013\u0002\u0055\u003A\u0019\u001C\u0002\u005A\u0001" + ("\u0002\u0002\u0002\u0002\u0013\u000E\u000F\u000E\u0011\u0018\u0002\u0020\u001E\u000E\u000D\u0058\u0059\u0002\u0055\u001A\u000D\u001A\u0002\u004D\u0002\u0012\u000D\u001B\u0002\u0020\u001E\u000E\u000D\u0058\u0004\u0009\u0002\u004E\u0002\u0004\u0003\u0005\u0007\u0002\u004E\u0002\u0004\u0003\u0005\u0007\u0059\u0046\u0001" + ("\u0002\u0002\u0002\u0002\u0013\u000E\u000F\u000E\u0011\u0018\u0002\u0017\u0010\u0012\u001D\u0002\u0055\u001F\u000E\u0015\u0002\u004D\u0002\u0003\u0046\u0001" + ("\u0002\u0002\u0002\u0002\u001F\u0019\u0020\u0017\u0011\u0018\u0002\u0013\u000E\u000F\u000E\u0011\u0018\u0002\u0017\u0010\u0012\u001D\u0002\u0014\u000D\u000F\u001F\u0055\u0013\u000F\u0021\u000D\u004A\u004B\u0002\u004D\u0050\u0002\u0055\u001F\u000E\u0015\u0046\u0001" + ("\u0002\u0002\u0002\u0002\u001F\u0019\u0020\u0017\u0011\u0018\u0002\u0013\u000E\u000F\u000E\u0011\u0018\u0002\u0017\u0010\u0012\u001D\u0002\u0014\u000D\u000F\u001F\u0055\u0015\u000D\u0013\u000E\u0010\u0015\u000D\u004A\u0010\u0020\u0023\u000D\u0018\u000E\u0002\u001F\u004B\u0002\u005A\u0002\u0055\u001F\u000E\u0015\u0002\u004D\u0002\u004A\u0017\u0010\u0012\u001D\u004B\u001F\u0046\u0002\u0015\u000D\u000E\u0019\u0015\u0012\u0002\u0003\u0046\u0002\u005B\u0001" + ("\u0002\u0002\u0002\u0002\u001F\u0019\u0020\u0017\u0011\u0018\u0002\u0013\u000E\u000F\u000E\u0011\u0018\u0002\u0017\u0010\u0012\u001D\u0002\u0014\u000D\u000F\u001F\u0055\u000F\u0016\u0021\u000F\u0012\u0018\u000D\u004A\u0010\u0020\u0023\u000D\u0018\u000E\u0002\u0012\u004B\u0002\u005A\u0002\u0055\u001F\u000E\u0015\u0002\u004C\u004D\u0002\u004A\u0017\u0010\u0012\u001D\u004B\u0012\u0046\u0002\u0015\u000D\u000E\u0019\u0015\u0012\u0002\u0003\u0046\u0002\u005B\u0001" + ("\u0002\u0002\u0002\u0002\u001F\u0019\u0020\u0017\u0011\u0018\u0002\u0013\u000E\u000F\u000E\u0011\u0018\u0002\u0017\u0010\u0012\u001D\u0002\u0020\u0019\u001C\u0055\u001B\u0015\u0011\u000E\u000D\u0055\u0020\u001E\u000E\u000D\u004A\u0010\u0020\u0023\u000D\u0018\u000E\u0002\u0020\u0042\u0002\u0010\u0020\u0023\u000D\u0018\u000E\u0002\u0010\u001C\u001C\u0042\u0002\u0010\u0020\u0023\u000D\u0018\u000E\u0002\u0021\u004B\u0002\u005A\u0002\u0055\u001A\u000D\u001A\u0058\u004A\u0017\u0010\u0012\u001D\u004B\u0020\u0002\u004C\u0002\u004A\u0017\u0010\u0012\u001D\u004B\u0010\u001C\u001C\u0059\u0002\u004D\u0002\u004A\u0020\u001E\u000E\u000D\u004B\u004A\u0017\u0010\u0012\u001D\u004B\u0021\u0046\u0002\u0015\u000D\u000E\u0019\u0015\u0012\u0002\u004A\u0017\u0010\u0012\u001D\u004B\u0010\u001C\u001C\u0002\u004C\u0002\u0004\u0046\u0002\u005B\u0001" + ("\u0002\u0002\u0002\u0002\u001F\u0019\u0020\u0017\u0011\u0018\u0002\u0013\u000E\u000F\u000E\u0011\u0018\u0002\u0017\u0010\u0012\u001D\u0002\u0020\u0019\u001C\u0055\u001B\u0015\u0011\u000E\u000D\u0055\u0020\u001E\u000E\u000D\u0013\u004A\u0010\u0020\u0023\u000D\u0018\u000E\u0002\u0020\u0042\u0002\u0010\u0020\u0023\u000D\u0018\u000E\u0002\u0010\u001C\u001C\u0042\u0002\u0010\u0020\u0023\u000D\u0018\u000E\u0002\u0021\u0013\u004B\u0002\u005A\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0021\u000F\u0015\u0002\u0017\u0011\u0013\u000E\u0002\u004D\u0002\u004A\u0031\u0011\u0013\u000E\u004F\u0017\u0010\u0012\u001D\u0050\u004B\u0021\u0013\u0046\u0002\u0017\u0010\u0012\u001D\u0002\u0010\u0002\u004D\u0002\u004A\u0017\u0010\u0012\u001D\u004B\u0010\u001C\u001C\u0046\u0002\u0017\u0010\u0012\u001D\u0002\u0020\u000F\u0002\u004D\u0002\u004A\u0017\u0010\u0012\u001D\u004B\u0020\u0046\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u001C\u0010\u0015\u0002\u004A\u0011\u0012\u000E\u0002\u0011\u0002\u004D\u0002\u0003\u0046\u0002\u0011\u0002\u004F\u0002\u0017\u0011\u0013\u000E\u0041\u0032\u0010\u0019\u0012\u000E\u0046\u0002\u0011\u004C\u004C\u004B\u0002\u0055\u001A\u000D\u001A\u0058\u0020\u000F\u0002\u004C\u0002\u0010\u0002\u004C\u0002\u0011\u0059\u0002\u004D\u0002\u004A\u0020\u001E\u000E\u000D\u004B\u0017\u0011\u0013\u000E\u0058\u0011\u0059\u0046\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0015\u000D\u000E\u0019\u0015\u0012\u0002\u0010\u0002\u004C\u0002\u0017\u0011\u0013\u000E\u0041\u0032\u0010\u0019\u0012\u000E\u0046\u0001" + ("\u0002\u0002\u0002\u0002\u005B\u0001" + ("\u0002\u0002\u0002\u0002\u001F\u0019\u0020\u0017\u0011\u0018\u0002\u0013\u000E\u000F\u000E\u0011\u0018\u0002\u0031\u0011\u0013\u000E\u004F\u0017\u0010\u0012\u001D\u0050\u0002\u0020\u0019\u001C\u0055\u0015\u000D\u000F\u0016\u0055\u0020\u001E\u000E\u000D\u0013\u004A\u0010\u0020\u0023\u000D\u0018\u000E\u0002\u0020\u0042\u0002\u0010\u0020\u0023\u000D\u0018\u000E\u0002\u0010\u001C\u001C\u0042\u0002\u0010\u0020\u0023\u000D\u0018\u000E\u0002\u0012\u004B\u0002\u005A\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0017\u0010\u0012\u001D\u0002\u0020\u000F\u0002\u004D\u0002\u004A\u0017\u0010\u0012\u001D\u004B\u0020\u0046\u0002\u0017\u0010\u0012\u001D\u0002\u0010\u0002\u004D\u0002\u004A\u0017\u0010\u0012\u001D\u004B\u0010\u001C\u001C\u0046\u0002\u0011\u0012\u000E\u0002\u0018\u0012\u000E\u0002\u004D\u0002\u004A\u0011\u0012\u000E\u004B\u004A\u0017\u0010\u0012\u001D\u004B\u0012\u0046\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0021\u000F\u0015\u0002\u0015\u0002\u004D\u0002\u0012\u000D\u001B\u0002\u0031\u0011\u0013\u000E\u004F\u0017\u0010\u0012\u001D\u0050\u004A\u0018\u0012\u000E\u004B\u0046\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u001C\u0010\u0015\u0002\u004A\u0011\u0012\u000E\u0002\u0011\u0002\u004D\u0002\u0003\u0046\u0002\u0011\u0002\u004F\u0002\u0018\u0012\u000E\u0046\u0002\u0011\u004C\u004C\u004B\u0002\u0015\u0041\u0029\u0016\u0016\u004A\u0055\u001A\u000D\u001A\u0058\u0020\u000F\u0002\u004C\u0002\u0010\u0002\u004C\u0002\u0011\u0059\u004B\u0046\u0001" + ("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0015\u000D\u000E\u0019\u0015\u0012\u0002\u0015\u0046\u0001" + ("\u0002\u0002\u0002\u0002\u005B\u0001" + ("\u0002\u0002\u0002\u0002\u001F\u0019\u0020\u0017\u0011\u0018\u0002\u0013\u000E\u000F\u000E\u0011\u0018\u0002\u0016\u001E\u0012\u000F\u001A\u0011\u0018\u0002\u0017\u0011\u0013\u000E\u0055\u001B\u0011\u000E\u0014\u0055\u0018\u000F\u001F\u000F\u0018\u0011\u000E\u001E\u004A\u0010\u0020\u0023\u000D\u0018\u000E\u0002\u0018\u000F\u001F\u004B\u0002\u004D\u0050\u0002\u0012\u000D\u001B\u0002\u0031\u0011\u0013\u000E\u004F\u0010\u0020\u0023\u000D\u0018\u000E\u0050\u004A\u004A\u0011\u0012\u000E\u004B\u004A\u0017\u0010\u0012\u001D\u004B\u0018\u000F\u001F\u004B\u0046\u0001" + "\u005B\u0001\u0001")))))))))))))))))));

    public static string emit__csharp_emitter_emit_full_chapter(IRChapter m, List<ATypeDef> type_defs) => ((Func<List<ArityEntry>, string>)((arities) => ((Func<string, string>)((header) => ((Func<string, string>)((entry) => string.Concat(Enumerable.Concat(new List<string> { header, entry, emit_cce_runtime(), emit_buf_runtime(), emit__csharp_emitter_emit_type_defs(type_defs, 0L), emit_class_header(m.name.value) }, Enumerable.Concat(emit_defs_list(m.defs, 0L, arities), new List<string> { "\u005B\u0001" }).ToList()).ToList())))(("\u0032\u0010\u0016\u000D\u0024\u0055" + (sanitize(m.name.value) + "\u0041\u001A\u000F\u0011\u0012\u004A\u004B\u0046\u0001\u0001")))))("\u0019\u0013\u0011\u0012\u001D\u0002\u002D\u001E\u0013\u000E\u000D\u001A\u0046\u0001\u0019\u0013\u0011\u0012\u001D\u0002\u002D\u001E\u0013\u000E\u000D\u001A\u0041\u0032\u0010\u0017\u0017\u000D\u0018\u000E\u0011\u0010\u0012\u0013\u0041\u0037\u000D\u0012\u000D\u0015\u0011\u0018\u0046\u0001\u0019\u0013\u0011\u0012\u001D\u0002\u002D\u001E\u0013\u000E\u000D\u001A\u0041\u002B\u002A\u0046\u0001\u0019\u0013\u0011\u0012\u001D\u0002\u002D\u001E\u0013\u000E\u000D\u001A\u0041\u0031\u0011\u0012\u0025\u0046\u0001\u0019\u0013\u0011\u0012\u001D\u0002\u002D\u001E\u0013\u000E\u000D\u001A\u0041\u0028\u0014\u0015\u000D\u000F\u0016\u0011\u0012\u001D\u0041\u0028\u000F\u0013\u0022\u0013\u0046\u0001\u0001")))(build_arity_map(m.defs, 0L, new List<ArityEntry>()));

    public static string emit_chapter(IRChapter m) => ((Func<List<ArityEntry>, string>)((arities) => ((Func<string, string>)((header) => ((Func<string, string>)((entry) => string.Concat(Enumerable.Concat(new List<string> { header, entry, emit_cce_runtime(), emit_buf_runtime(), emit_class_header(m.name.value) }, Enumerable.Concat(emit_defs_list(m.defs, 0L, arities), new List<string> { "\u005B\u0001" }).ToList()).ToList())))(("\u0032\u0010\u0016\u000D\u0024\u0055" + (sanitize(m.name.value) + "\u0041\u001A\u000F\u0011\u0012\u004A\u004B\u0046\u0001\u0001")))))("\u0019\u0013\u0011\u0012\u001D\u0002\u002D\u001E\u0013\u000E\u000D\u001A\u0046\u0001\u0019\u0013\u0011\u0012\u001D\u0002\u002D\u001E\u0013\u000E\u000D\u001A\u0041\u0032\u0010\u0017\u0017\u000D\u0018\u000E\u0011\u0010\u0012\u0013\u0041\u0037\u000D\u0012\u000D\u0015\u0011\u0018\u0046\u0001\u0019\u0013\u0011\u0012\u001D\u0002\u002D\u001E\u0013\u000E\u000D\u001A\u0041\u002B\u002A\u0046\u0001\u0019\u0013\u0011\u0012\u001D\u0002\u002D\u001E\u0013\u000E\u000D\u001A\u0041\u0031\u0011\u0012\u0025\u0046\u0001\u0019\u0013\u0011\u0012\u001D\u0002\u002D\u001E\u0013\u000E\u000D\u001A\u0041\u0028\u0014\u0015\u000D\u000F\u0016\u0011\u0012\u001D\u0041\u0028\u000F\u0013\u0022\u0013\u0046\u0001\u0001")))(build_arity_map(m.defs, 0L, new List<ArityEntry>()));

    public static string emit_class_header(string name) => ("\u001F\u0019\u0020\u0017\u0011\u0018\u0002\u0013\u000E\u000F\u000E\u0011\u0018\u0002\u0018\u0017\u000F\u0013\u0013\u0002\u0032\u0010\u0016\u000D\u0024\u0055" + (sanitize(name) + "\u0001\u005A\u0001"));

    public static List<string> emit_defs_list(List<IRDef> defs, long i, List<ArityEntry> arities) => ((i == ((long)defs.Count)) ? new List<string>() : Enumerable.Concat(new List<string> { emit__csharp_emitter_emit_def(defs[(int)i], arities), "\u0001" }, emit_defs_list(defs, (i + 1L), arities)).ToList());

    public static List<string> cs_keywords() => ((Func<List<string>, List<string>>)((unsorted) => sort_text_list(unsorted)))(new List<string> { "\u000F\u0020\u0013\u000E\u0015\u000F\u0018\u000E", "\u000F\u0013", "\u0020\u000F\u0013\u000D", "\u0020\u0010\u0010\u0017", "\u0020\u0015\u000D\u000F\u0022", "\u0018\u000F\u0013\u000D", "\u0018\u000F\u000E\u0018\u0014", "\u0018\u0017\u000F\u0013\u0013", "\u0018\u0010\u0012\u000E\u0011\u0012\u0019\u000D", "\u0016\u000D\u0018\u0011\u001A\u000F\u0017", "\u0016\u000D\u001C\u000F\u0019\u0017\u000E", "\u0016\u000D\u0017\u000D\u001D\u000F\u000E\u000D", "\u0016\u0010", "\u0016\u0010\u0019\u0020\u0017\u000D", "\u000D\u0017\u0013\u000D", "\u000D\u0021\u000D\u0012\u000E", "\u001C\u000F\u0017\u0013\u000D", "\u001C\u0011\u0012\u000F\u0017\u0017\u001E", "\u001C\u0010\u0015", "\u0011\u001C", "\u0011\u0012", "\u0011\u0012\u000E", "\u0011\u0012\u000E\u000D\u0015\u0012\u000F\u0017", "\u0011\u0013", "\u0017\u0010\u0012\u001D", "\u0012\u000F\u001A\u000D\u0013\u001F\u000F\u0018\u000D", "\u0012\u000D\u001B", "\u0012\u0019\u0017\u0017", "\u0010\u0020\u0023\u000D\u0018\u000E", "\u0010\u0019\u000E", "\u0010\u0021\u000D\u0015\u0015\u0011\u0016\u000D", "\u001F\u000F\u0015\u000F\u001A\u0013", "\u001F\u0015\u0011\u0021\u000F\u000E\u000D", "\u001F\u0015\u0010\u000E\u000D\u0018\u000E\u000D\u0016", "\u001F\u0019\u0020\u0017\u0011\u0018", "\u0015\u000D\u001C", "\u0015\u000D\u000E\u0019\u0015\u0012", "\u0013\u000D\u000F\u0017\u000D\u0016", "\u0013\u000E\u000F\u000E\u0011\u0018", "\u0013\u000E\u0015\u0011\u0012\u001D", "\u0013\u001B\u0011\u000E\u0018\u0014", "\u000E\u0014\u0011\u0013", "\u000E\u0014\u0015\u0010\u001B", "\u000E\u0015\u0019\u000D", "\u000E\u0015\u001E", "\u000E\u001E\u001F\u000D\u0010\u001C", "\u0019\u0013\u0011\u0012\u001D", "\u0021\u0011\u0015\u000E\u0019\u000F\u0017", "\u0021\u0010\u0011\u0016", "\u001B\u0014\u0011\u0017\u000D" });

    public static bool is_cs_keyword(string n) => text_set_has(cs_keywords(), n);

    public static string sanitize(string name) => ((Func<string, string>)((s) => (is_cs_keyword(s) ? ("\u0052" + s) : (is_cs_member_name(s) ? (s + "\u0055") : s))))(name.Replace("\u0049", "\u0055"));

    public static List<string> cs_member_names() => ((Func<List<string>, List<string>>)((unsorted) => sort_text_list(unsorted)))(new List<string> { "\u0027\u0025\u0019\u000F\u0017\u0013", "\u0037\u000D\u000E\u002E\u000F\u0013\u0014\u0032\u0010\u0016\u000D", "\u0037\u000D\u000E\u0028\u001E\u001F\u000D", "\u0034\u000D\u001A\u0020\u000D\u0015\u001B\u0011\u0013\u000D\u0032\u0017\u0010\u0012\u000D", "\u0028\u0010\u002D\u000E\u0015\u0011\u0012\u001D" });

    public static bool is_cs_member_name(string n) => text_set_has(cs_member_names(), n);

    public static string cs_type(CodexType ty)
    {
        while (true)
        {
            var _tco_s = ty;
            if (_tco_s is IntegerTy _tco_m0)
            {
            return "\u0017\u0010\u0012\u001D";
            }
            else if (_tco_s is NumberTy _tco_m1)
            {
            return "\u0016\u0010\u0019\u0020\u0017\u000D";
            }
            else if (_tco_s is TextTy _tco_m2)
            {
            return "\u0013\u000E\u0015\u0011\u0012\u001D";
            }
            else if (_tco_s is BooleanTy _tco_m3)
            {
            return "\u0020\u0010\u0010\u0017";
            }
            else if (_tco_s is CharTy _tco_m4)
            {
            return "\u0017\u0010\u0012\u001D";
            }
            else if (_tco_s is VoidTy _tco_m5)
            {
            return "\u0021\u0010\u0011\u0016";
            }
            else if (_tco_s is NothingTy _tco_m6)
            {
            return "\u0010\u0020\u0023\u000D\u0018\u000E";
            }
            else if (_tco_s is ErrorTy _tco_m7)
            {
            return "\u0010\u0020\u0023\u000D\u0018\u000E";
            }
            else if (_tco_s is FunTy _tco_m8)
            {
                var p = _tco_m8.Field0;
                var r = _tco_m8.Field1;
            return ("\u0036\u0019\u0012\u0018\u004F" + (cs_type(p) + ("\u0042\u0002" + (cs_type(r) + "\u0050"))));
            }
            else if (_tco_s is ListTy _tco_m9)
            {
                var elem = _tco_m9.Field0;
            return ("\u0031\u0011\u0013\u000E\u004F" + (cs_type(elem) + "\u0050"));
            }
            else if (_tco_s is LinkedListTy _tco_m10)
            {
                var elem = _tco_m10.Field0;
            return ("\u0031\u0011\u0013\u000E\u004F" + (cs_type(elem) + "\u0050"));
            }
            else if (_tco_s is TypeVar _tco_m11)
            {
                var id = _tco_m11.Field0;
            return ("\u0028" + _Cce.FromUnicode(id.ToString()));
            }
            else if (_tco_s is ForAllTy _tco_m12)
            {
                var id = _tco_m12.Field0;
                var body = _tco_m12.Field1;
            var _tco_0 = body;
            ty = _tco_0;
            continue;
            }
            else if (_tco_s is SumTy _tco_m13)
            {
                var name = _tco_m13.Field0;
                var ctors = _tco_m13.Field1;
            return sanitize(name.value);
            }
            else if (_tco_s is RecordTy _tco_m14)
            {
                var name = _tco_m14.Field0;
                var fields = _tco_m14.Field1;
            return sanitize(name.value);
            }
            else if (_tco_s is ConstructedTy _tco_m15)
            {
                var name = _tco_m15.Field0;
                var args = _tco_m15.Field1;
            if ((((long)args.Count) == 0L))
            {
            return sanitize(name.value);
            }
            else
            {
            return (sanitize(name.value) + ("\u004F" + (emit_cs_type_args(args, 0L) + "\u0050")));
            }
            }
            else if (_tco_s is EffectfulTy _tco_m16)
            {
                var effects = _tco_m16.Field0;
                var ret = _tco_m16.Field1;
            var _tco_0 = ret;
            ty = _tco_0;
            continue;
            }
        }
    }

    public static string emit_cs_type_args(List<CodexType> args, long i) => ((i == ((long)args.Count)) ? "" : ((Func<string, string>)((t) => ((i == (((long)args.Count) - 1L)) ? t : (t + ("\u0042\u0002" + emit_cs_type_args(args, (i + 1L)))))))(cs_type(args[(int)i])));

    public static List<ArityEntry> build_arity_map(List<IRDef> defs, long i, List<ArityEntry> acc)
    {
        while (true)
        {
            if ((i == ((long)defs.Count)))
            {
            return acc;
            }
            else
            {
            var d = defs[(int)i];
            var _tco_0 = defs;
            var _tco_1 = (i + 1L);
            var _tco_2 = ((Func<List<ArityEntry>>)(() => { var _l = acc; _l.Add(new ArityEntry(name: d.name, arity: ((long)d.@params.Count))); return _l; }))();
            defs = _tco_0;
            i = _tco_1;
            acc = _tco_2;
            continue;
            }
        }
    }

    public static List<ArityEntry> build_arity_map_from_ast(List<ADef> defs, long i, List<ArityEntry> acc)
    {
        while (true)
        {
            if ((i == ((long)defs.Count)))
            {
            return acc;
            }
            else
            {
            var d = defs[(int)i];
            var _tco_0 = defs;
            var _tco_1 = (i + 1L);
            var _tco_2 = ((Func<List<ArityEntry>>)(() => { var _l = acc; _l.Add(new ArityEntry(name: d.name.value, arity: ((long)d.@params.Count))); return _l; }))();
            defs = _tco_0;
            i = _tco_1;
            acc = _tco_2;
            continue;
            }
        }
    }

    public static long lookup_arity(List<ArityEntry> entries, string name) => lookup_arity_loop(entries, name, 0L, ((long)entries.Count));

    public static long lookup_arity_loop(List<ArityEntry> entries, string name, long i, long len)
    {
        while (true)
        {
            if ((i == len))
            {
            return (-1L);
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
            var _tco_2 = (i + 1L);
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
                var sp = _tco_m0.Field3;
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

    public static string emit__csharp_emitter_emit_apply_args(List<IRExpr> args, List<ArityEntry> arities, long i) => ((i == ((long)args.Count)) ? "" : ((i == (((long)args.Count) - 1L)) ? emit__csharp_emitter_emit_expr(args[(int)i], arities) : (emit__csharp_emitter_emit_expr(args[(int)i], arities) + ("\u0042\u0002" + emit__csharp_emitter_emit_apply_args(args, arities, (i + 1L))))));

    public static string emit_partial_params(long i, long count) => ((i == count) ? "" : ((i == (count - 1L)) ? ("\u0055\u001F" + (_Cce.FromUnicode(i.ToString()) + "\u0055")) : ("\u0055\u001F" + (_Cce.FromUnicode(i.ToString()) + ("\u0055" + ("\u0042\u0002" + emit_partial_params((i + 1L), count)))))));

    public static string emit_partial_wrappers(long i, long count) => ((i == count) ? "" : ("\u004A\u0055\u001F" + (_Cce.FromUnicode(i.ToString()) + ("\u0055\u004B\u0002\u004D\u0050\u0002" + emit_partial_wrappers((i + 1L), count)))));

    public static long bsearch_emitter_pos(List<BuiltinEmitter> entries, string name, long lo, long hi)
    {
        while (true)
        {
            if ((lo >= hi))
            {
            return lo;
            }
            else
            {
            var mid = (lo + ((hi - lo) / 2L));
            var mid_name = entries[(int)mid].name;
            if (((long)string.CompareOrdinal(name, mid_name) <= 0L))
            {
            var _tco_0 = entries;
            var _tco_1 = name;
            var _tco_2 = lo;
            var _tco_3 = mid;
            entries = _tco_0;
            name = _tco_1;
            lo = _tco_2;
            hi = _tco_3;
            continue;
            }
            else
            {
            var _tco_0 = entries;
            var _tco_1 = name;
            var _tco_2 = (mid + 1L);
            var _tco_3 = hi;
            entries = _tco_0;
            name = _tco_1;
            lo = _tco_2;
            hi = _tco_3;
            continue;
            }
            }
        }
    }

    public static List<BuiltinEmitter> sorted_insert_emitter(List<BuiltinEmitter> xs, BuiltinEmitter e) => ((Func<long, List<BuiltinEmitter>>)((len) => ((Func<long, List<BuiltinEmitter>>)((pos) => ((Func<List<BuiltinEmitter>>)(() => { var _l = new List<BuiltinEmitter>(xs); _l.Insert((int)pos, e); return _l; }))()))(bsearch_emitter_pos(xs, e.name, 0L, len))))(((long)xs.Count));

    public static List<BuiltinEmitter> sort_emitters(List<BuiltinEmitter> xs) => fold_list((_p0_) => (_p1_) => sorted_insert_emitter(_p0_, _p1_), new List<BuiltinEmitter>(), xs);

    public static List<BuiltinEmitter> builtin_emitters() => sort_emitters(new List<BuiltinEmitter> { new BuiltinEmitter(name: "\u0013\u0014\u0010\u001B", emit: ((List<IRExpr> args) => ((List<ArityEntry> arities) => ((CodexType ty) => emit_builtin_show(args, arities))))), new BuiltinEmitter(name: "\u0012\u000D\u001D\u000F\u000E\u000D", emit: ((List<IRExpr> args) => ((List<ArityEntry> arities) => ((CodexType ty) => ("\u004A\u0049" + (emit__csharp_emitter_emit_expr(args[(int)0L], arities) + "\u004B")))))), new BuiltinEmitter(name: "\u001F\u0015\u0011\u0012\u000E\u0049\u0017\u0011\u0012\u000D", emit: ((List<IRExpr> args) => ((List<ArityEntry> arities) => ((CodexType ty) => emit_builtin_print_line(args, arities))))), new BuiltinEmitter(name: "\u000E\u000D\u0024\u000E\u0049\u0017\u000D\u0012\u001D\u000E\u0014", emit: ((List<IRExpr> args) => ((List<ArityEntry> arities) => ((CodexType ty) => ("\u004A\u004A\u0017\u0010\u0012\u001D\u004B" + (emit__csharp_emitter_emit_expr(args[(int)0L], arities) + "\u0041\u0031\u000D\u0012\u001D\u000E\u0014\u004B")))))), new BuiltinEmitter(name: "\u0011\u0013\u0049\u0017\u000D\u000E\u000E\u000D\u0015", emit: ((List<IRExpr> args) => ((List<ArityEntry> arities) => ((CodexType ty) => emit_builtin_is_letter(args, arities))))), new BuiltinEmitter(name: "\u0011\u0013\u0049\u0016\u0011\u001D\u0011\u000E", emit: ((List<IRExpr> args) => ((List<ArityEntry> arities) => ((CodexType ty) => emit_builtin_is_digit(args, arities))))), new BuiltinEmitter(name: "\u0011\u0013\u0049\u001B\u0014\u0011\u000E\u000D\u0013\u001F\u000F\u0018\u000D", emit: ((List<IRExpr> args) => ((List<ArityEntry> arities) => ((CodexType ty) => ("\u004A" + (emit__csharp_emitter_emit_expr(args[(int)0L], arities) + "\u0002\u004F\u004D\u0002\u0005\u0031\u004B")))))), new BuiltinEmitter(name: "\u000E\u000D\u0024\u000E\u0049\u000E\u0010\u0049\u0011\u0012\u000E\u000D\u001D\u000D\u0015", emit: ((List<IRExpr> args) => ((List<ArityEntry> arities) => ((CodexType ty) => emit_builtin_text_to_integer(args, arities))))), new BuiltinEmitter(name: "\u000E\u000D\u0024\u000E\u0049\u000E\u0010\u0049\u0016\u0010\u0019\u0020\u0017\u000D\u0049\u0020\u0011\u000E\u0013", emit: ((List<IRExpr> args) => ((List<ArityEntry> arities) => ((CodexType ty) => emit_builtin_text_to_double_bits(args, arities))))), new BuiltinEmitter(name: "\u0011\u0012\u000E\u000D\u001D\u000D\u0015\u0049\u000E\u0010\u0049\u000E\u000D\u0024\u000E", emit: ((List<IRExpr> args) => ((List<ArityEntry> arities) => ((CodexType ty) => emit_builtin_integer_to_text(args, arities))))), new BuiltinEmitter(name: "\u0018\u0014\u000F\u0015\u0049\u0018\u0010\u0016\u000D", emit: ((List<IRExpr> args) => ((List<ArityEntry> arities) => ((CodexType ty) => emit__csharp_emitter_emit_expr(args[(int)0L], arities))))), new BuiltinEmitter(name: "\u0018\u0014\u000F\u0015\u0049\u0018\u0010\u0016\u000D\u0049\u000F\u000E", emit: ((List<IRExpr> args) => ((List<ArityEntry> arities) => ((CodexType ty) => emit_builtin_char_code_at(args, arities))))), new BuiltinEmitter(name: "\u0018\u0010\u0016\u000D\u0049\u000E\u0010\u0049\u0018\u0014\u000F\u0015", emit: ((List<IRExpr> args) => ((List<ArityEntry> arities) => ((CodexType ty) => emit__csharp_emitter_emit_expr(args[(int)0L], arities))))), new BuiltinEmitter(name: "\u0018\u0014\u000F\u0015\u0049\u000E\u0010\u0049\u000E\u000D\u0024\u000E", emit: ((List<IRExpr> args) => ((List<ArityEntry> arities) => ((CodexType ty) => ("\u004A\u004A\u0018\u0014\u000F\u0015\u004B" + (emit__csharp_emitter_emit_expr(args[(int)0L], arities) + "\u004B\u0041\u0028\u0010\u002D\u000E\u0015\u0011\u0012\u001D\u004A\u004B")))))), new BuiltinEmitter(name: "\u0017\u0011\u0013\u000E\u0049\u0017\u000D\u0012\u001D\u000E\u0014", emit: ((List<IRExpr> args) => ((List<ArityEntry> arities) => ((CodexType ty) => ("\u004A\u004A\u0017\u0010\u0012\u001D\u004B" + (emit__csharp_emitter_emit_expr(args[(int)0L], arities) + "\u0041\u0032\u0010\u0019\u0012\u000E\u004B")))))), new BuiltinEmitter(name: "\u0018\u0014\u000F\u0015\u0049\u000F\u000E", emit: ((List<IRExpr> args) => ((List<ArityEntry> arities) => ((CodexType ty) => emit_builtin_char_at(args, arities))))), new BuiltinEmitter(name: "\u0013\u0019\u0020\u0013\u000E\u0015\u0011\u0012\u001D", emit: ((List<IRExpr> args) => ((List<ArityEntry> arities) => ((CodexType ty) => emit_builtin_substring(args, arities))))), new BuiltinEmitter(name: "\u0017\u0011\u0013\u000E\u0049\u000F\u000E", emit: ((List<IRExpr> args) => ((List<ArityEntry> arities) => ((CodexType ty) => emit_builtin_list_at(args, arities))))), new BuiltinEmitter(name: "\u0017\u0011\u0013\u000E\u0049\u0011\u0012\u0013\u000D\u0015\u000E\u0049\u000F\u000E", emit: ((List<IRExpr> args) => ((List<ArityEntry> arities) => ((CodexType ty) => emit_builtin_list_insert_at(args, arities))))), new BuiltinEmitter(name: "\u0017\u0011\u0013\u000E\u0049\u0013\u000D\u000E\u0049\u000F\u000E", emit: ((List<IRExpr> args) => ((List<ArityEntry> arities) => ((CodexType ty) => emit_builtin_list_set_at(args, arities))))), new BuiltinEmitter(name: "\u0017\u0011\u0013\u000E\u0049\u0013\u0012\u0010\u0018", emit: ((List<IRExpr> args) => ((List<ArityEntry> arities) => ((CodexType ty) => emit_builtin_list_snoc(args, arities))))), new BuiltinEmitter(name: "\u0017\u0011\u0013\u000E\u0049\u0018\u0010\u0012\u000E\u000F\u0011\u0012\u0013", emit: ((List<IRExpr> args) => ((List<ArityEntry> arities) => ((CodexType ty) => (emit__csharp_emitter_emit_expr(args[(int)0L], arities) + ("\u0041\u0032\u0010\u0012\u000E\u000F\u0011\u0012\u0013\u004A" + (emit__csharp_emitter_emit_expr(args[(int)1L], arities) + "\u004B"))))))), new BuiltinEmitter(name: "\u000E\u000D\u0024\u000E\u0049\u0018\u0010\u001A\u001F\u000F\u0015\u000D", emit: ((List<IRExpr> args) => ((List<ArityEntry> arities) => ((CodexType ty) => emit_builtin_text_compare(args, arities))))), new BuiltinEmitter(name: "\u000E\u000D\u0024\u000E\u0049\u0015\u000D\u001F\u0017\u000F\u0018\u000D", emit: ((List<IRExpr> args) => ((List<ArityEntry> arities) => ((CodexType ty) => emit_builtin_text_replace(args, arities))))), new BuiltinEmitter(name: "\u0010\u001F\u000D\u0012\u0049\u001C\u0011\u0017\u000D", emit: ((List<IRExpr> args) => ((List<ArityEntry> arities) => ((CodexType ty) => ("\u0036\u0011\u0017\u000D\u0041\u002A\u001F\u000D\u0012\u002F\u000D\u000F\u0016\u004A" + (emit__csharp_emitter_emit_expr(args[(int)0L], arities) + "\u004B")))))), new BuiltinEmitter(name: "\u0015\u000D\u000F\u0016\u0049\u000F\u0017\u0017", emit: ((List<IRExpr> args) => ((List<ArityEntry> arities) => ((CodexType ty) => emit_builtin_read_all(args, arities))))), new BuiltinEmitter(name: "\u0018\u0017\u0010\u0013\u000D\u0049\u001C\u0011\u0017\u000D", emit: ((List<IRExpr> args) => ((List<ArityEntry> arities) => ((CodexType ty) => (emit__csharp_emitter_emit_expr(args[(int)0L], arities) + "\u0041\u0030\u0011\u0013\u001F\u0010\u0013\u000D\u004A\u004B"))))), new BuiltinEmitter(name: "\u0015\u000D\u000F\u0016\u0049\u0017\u0011\u0012\u000D", emit: ((List<IRExpr> args) => ((List<ArityEntry> arities) => ((CodexType ty) => "\u0055\u0032\u0018\u000D\u0041\u0036\u0015\u0010\u001A\u0033\u0012\u0011\u0018\u0010\u0016\u000D\u004A\u0032\u0010\u0012\u0013\u0010\u0017\u000D\u0041\u002F\u000D\u000F\u0016\u0031\u0011\u0012\u000D\u004A\u004B\u0002\u0044\u0044\u0002\u0048\u0048\u004B")))), new BuiltinEmitter(name: "\u0015\u000D\u000F\u0016\u0049\u001C\u0011\u0017\u000D", emit: ((List<IRExpr> args) => ((List<ArityEntry> arities) => ((CodexType ty) => emit_builtin_read_file(args, arities))))), new BuiltinEmitter(name: "\u001B\u0015\u0011\u000E\u000D\u0049\u001C\u0011\u0017\u000D", emit: ((List<IRExpr> args) => ((List<ArityEntry> arities) => ((CodexType ty) => emit_builtin_write_file(args, arities))))), new BuiltinEmitter(name: "\u001B\u0015\u0011\u000E\u000D\u0049\u0020\u0011\u0012\u000F\u0015\u001E", emit: ((List<IRExpr> args) => ((List<ArityEntry> arities) => ((CodexType ty) => ("\u004A\u004A\u0036\u0019\u0012\u0018\u004F\u0010\u0020\u0023\u000D\u0018\u000E\u0050\u004B\u004A\u004A\u004B\u0002\u004D\u0050\u0002\u005A\u0002\u0021\u000F\u0015\u0002\u0055\u0020\u0017\u0002\u004D\u0002\u004A\u0031\u0011\u0013\u000E\u004F\u0017\u0010\u0012\u001D\u0050\u004B" + (emit__csharp_emitter_emit_expr(args[(int)0L], arities) + "\u0046\u0002\u0019\u0013\u0011\u0012\u001D\u0002\u0021\u000F\u0015\u0002\u0055\u0013\u0002\u004D\u0002\u0032\u0010\u0012\u0013\u0010\u0017\u000D\u0041\u002A\u001F\u000D\u0012\u002D\u000E\u000F\u0012\u0016\u000F\u0015\u0016\u002A\u0019\u000E\u001F\u0019\u000E\u004A\u004B\u0046\u0002\u001C\u0010\u0015\u000D\u000F\u0018\u0014\u0002\u004A\u0021\u000F\u0015\u0002\u0055\u0020\u0002\u0011\u0012\u0002\u0055\u0020\u0017\u004B\u0002\u0055\u0013\u0041\u0035\u0015\u0011\u000E\u000D\u003A\u001E\u000E\u000D\u004A\u004A\u0020\u001E\u000E\u000D\u004B\u0055\u0020\u004B\u0046\u0002\u0055\u0013\u0041\u0036\u0017\u0019\u0013\u0014\u004A\u004B\u0046\u0002\u0015\u000D\u000E\u0019\u0015\u0012\u0002\u0012\u0019\u0017\u0017\u0046\u0002\u005B\u004B\u004B\u004A\u004B")))))), new BuiltinEmitter(name: "\u001C\u0011\u0017\u000D\u0049\u000D\u0024\u0011\u0013\u000E\u0013", emit: ((List<IRExpr> args) => ((List<ArityEntry> arities) => ((CodexType ty) => emit_builtin_file_exists(args, arities))))), new BuiltinEmitter(name: "\u0017\u0011\u0013\u000E\u0049\u001C\u0011\u0017\u000D\u0013", emit: ((List<IRExpr> args) => ((List<ArityEntry> arities) => ((CodexType ty) => emit_builtin_list_files(args, arities))))), new BuiltinEmitter(name: "\u000E\u000D\u0024\u000E\u0049\u0018\u0010\u0012\u0018\u000F\u000E\u0049\u0017\u0011\u0013\u000E", emit: ((List<IRExpr> args) => ((List<ArityEntry> arities) => ((CodexType ty) => ("\u0013\u000E\u0015\u0011\u0012\u001D\u0041\u0032\u0010\u0012\u0018\u000F\u000E\u004A" + (emit__csharp_emitter_emit_expr(args[(int)0L], arities) + "\u004B")))))), new BuiltinEmitter(name: "\u000E\u000D\u0024\u000E\u0049\u0013\u001F\u0017\u0011\u000E", emit: ((List<IRExpr> args) => ((List<ArityEntry> arities) => ((CodexType ty) => emit_builtin_text_split(args, arities))))), new BuiltinEmitter(name: "\u000E\u000D\u0024\u000E\u0049\u0018\u0010\u0012\u000E\u000F\u0011\u0012\u0013", emit: ((List<IRExpr> args) => ((List<ArityEntry> arities) => ((CodexType ty) => emit_builtin_text_contains(args, arities))))), new BuiltinEmitter(name: "\u000E\u000D\u0024\u000E\u0049\u0013\u000E\u000F\u0015\u000E\u0013\u0049\u001B\u0011\u000E\u0014", emit: ((List<IRExpr> args) => ((List<ArityEntry> arities) => ((CodexType ty) => emit_builtin_text_starts_with(args, arities))))), new BuiltinEmitter(name: "\u001D\u000D\u000E\u0049\u000F\u0015\u001D\u0013", emit: ((List<IRExpr> args) => ((List<ArityEntry> arities) => ((CodexType ty) => "\u0027\u0012\u0021\u0011\u0015\u0010\u0012\u001A\u000D\u0012\u000E\u0041\u0037\u000D\u000E\u0032\u0010\u001A\u001A\u000F\u0012\u0016\u0031\u0011\u0012\u000D\u0029\u0015\u001D\u0013\u004A\u004B\u0041\u002D\u000D\u0017\u000D\u0018\u000E\u004A\u0055\u0032\u0018\u000D\u0041\u0036\u0015\u0010\u001A\u0033\u0012\u0011\u0018\u0010\u0016\u000D\u004B\u0041\u0028\u0010\u0031\u0011\u0013\u000E\u004A\u004B")))), new BuiltinEmitter(name: "\u001D\u000D\u000E\u0049\u000D\u0012\u0021", emit: ((List<IRExpr> args) => ((List<ArityEntry> arities) => ((CodexType ty) => emit_builtin_get_env(args, arities))))), new BuiltinEmitter(name: "\u0018\u0019\u0015\u0015\u000D\u0012\u000E\u0049\u0016\u0011\u0015", emit: ((List<IRExpr> args) => ((List<ArityEntry> arities) => ((CodexType ty) => "\u0055\u0032\u0018\u000D\u0041\u0036\u0015\u0010\u001A\u0033\u0012\u0011\u0018\u0010\u0016\u000D\u004A\u0030\u0011\u0015\u000D\u0018\u000E\u0010\u0015\u001E\u0041\u0037\u000D\u000E\u0032\u0019\u0015\u0015\u000D\u0012\u000E\u0030\u0011\u0015\u000D\u0018\u000E\u0010\u0015\u001E\u004A\u004B\u004B")))), new BuiltinEmitter(name: "\u0015\u0019\u0012\u0049\u001F\u0015\u0010\u0018\u000D\u0013\u0013", emit: ((List<IRExpr> args) => ((List<ArityEntry> arities) => ((CodexType ty) => emit_builtin_run_process(args, arities))))), new BuiltinEmitter(name: "\u001C\u0010\u0015\u0022", emit: ((List<IRExpr> args) => ((List<ArityEntry> arities) => ((CodexType ty) => ("\u0028\u000F\u0013\u0022\u0041\u002F\u0019\u0012\u004A\u004A\u004B\u0002\u004D\u0050\u0002\u004A" + (emit__csharp_emitter_emit_expr(args[(int)0L], arities) + "\u004B\u004A\u0012\u0019\u0017\u0017\u004B\u004B")))))), new BuiltinEmitter(name: "\u000F\u001B\u000F\u0011\u000E", emit: ((List<IRExpr> args) => ((List<ArityEntry> arities) => ((CodexType ty) => ("\u004A" + (emit__csharp_emitter_emit_expr(args[(int)0L], arities) + "\u004B\u0041\u002F\u000D\u0013\u0019\u0017\u000E")))))), new BuiltinEmitter(name: "\u001F\u000F\u0015", emit: ((List<IRExpr> args) => ((List<ArityEntry> arities) => ((CodexType ty) => emit_builtin_par(args, arities))))), new BuiltinEmitter(name: "\u0015\u000F\u0018\u000D", emit: ((List<IRExpr> args) => ((List<ArityEntry> arities) => ((CodexType ty) => emit_builtin_race(args, arities))))), new BuiltinEmitter(name: "\u0015\u000D\u0018\u0010\u0015\u0016\u0049\u0013\u000D\u000E", emit: ((List<IRExpr> args) => ((List<ArityEntry> arities) => ((CodexType ty) => emit_builtin_record_set(args, arities))))), new BuiltinEmitter(name: "\u0017\u0011\u0012\u0022\u000D\u0016\u0049\u0017\u0011\u0013\u000E\u0049\u000D\u001A\u001F\u000E\u001E", emit: ((List<IRExpr> args) => ((List<ArityEntry> arities) => ((CodexType ty) => ((Func<string, string>)((et) => ("\u0012\u000D\u001B\u0002\u0031\u0011\u0013\u000E\u004F" + (et + "\u0050\u004A\u004B"))))(ty switch { LinkedListTy(var elem) => (string)(cs_type(elem)), _ => (string)("\u0010\u0020\u0023\u000D\u0018\u000E"), }))))), new BuiltinEmitter(name: "\u0017\u0011\u0012\u0022\u000D\u0016\u0049\u0017\u0011\u0013\u000E\u0049\u001F\u0019\u0013\u0014", emit: ((List<IRExpr> args) => ((List<ArityEntry> arities) => ((CodexType ty) => ((Func<string, string>)((et) => ("\u004A\u004A\u0036\u0019\u0012\u0018\u004F\u0031\u0011\u0013\u000E\u004F" + (et + ("\u0050\u0042\u0002" + (et + ("\u0042\u0002\u0031\u0011\u0013\u000E\u004F" + (et + ("\u0050\u0050\u004B\u004A\u004A\u0055\u0017\u0017\u0042\u0002\u0055\u0021\u004B\u0002\u004D\u0050\u0002\u005A\u0002\u0055\u0017\u0017\u0041\u0029\u0016\u0016\u004A\u0055\u0021\u004B\u0046\u0002\u0015\u000D\u000E\u0019\u0015\u0012\u0002\u0055\u0017\u0017\u0046\u0002\u005B\u004B\u004B\u004A" + (emit__csharp_emitter_emit_expr(args[(int)0L], arities) + ("\u0042\u0002" + (emit__csharp_emitter_emit_expr(args[(int)1L], arities) + "\u004B"))))))))))))(ty switch { LinkedListTy(var elem) => (string)(cs_type(elem)), _ => (string)("\u0010\u0020\u0023\u000D\u0018\u000E"), }))))), new BuiltinEmitter(name: "\u0017\u0011\u0012\u0022\u000D\u0016\u0049\u0017\u0011\u0013\u000E\u0049\u000E\u0010\u0049\u0017\u0011\u0013\u000E", emit: ((List<IRExpr> args) => ((List<ArityEntry> arities) => ((CodexType ty) => emit__csharp_emitter_emit_expr(args[(int)0L], arities))))), new BuiltinEmitter(name: "\u0014\u000D\u000F\u001F\u0049\u0013\u000F\u0021\u000D", emit: ((List<IRExpr> args) => ((List<ArityEntry> arities) => ((CodexType ty) => "\u0055\u003A\u0019\u001C\u0041\u0014\u000D\u000F\u001F\u0055\u0013\u000F\u0021\u000D\u004A\u004B")))), new BuiltinEmitter(name: "\u0014\u000D\u000F\u001F\u0049\u0015\u000D\u0013\u000E\u0010\u0015\u000D", emit: ((List<IRExpr> args) => ((List<ArityEntry> arities) => ((CodexType ty) => emit_buf_call_1("\u0014\u000D\u000F\u001F\u0049\u0015\u000D\u0013\u000E\u0010\u0015\u000D", args, arities))))), new BuiltinEmitter(name: "\u0014\u000D\u000F\u001F\u0049\u000F\u0016\u0021\u000F\u0012\u0018\u000D", emit: ((List<IRExpr> args) => ((List<ArityEntry> arities) => ((CodexType ty) => emit_buf_call_1("\u0014\u000D\u000F\u001F\u0049\u000F\u0016\u0021\u000F\u0012\u0018\u000D", args, arities))))), new BuiltinEmitter(name: "\u0017\u0011\u0013\u000E\u0049\u001B\u0011\u000E\u0014\u0049\u0018\u000F\u001F\u000F\u0018\u0011\u000E\u001E", emit: ((List<IRExpr> args) => ((List<ArityEntry> arities) => ((CodexType ty) => emit_buf_call_1("\u0017\u0011\u0013\u000E\u0049\u001B\u0011\u000E\u0014\u0049\u0018\u000F\u001F\u000F\u0018\u0011\u000E\u001E", args, arities))))), new BuiltinEmitter(name: "\u0020\u0019\u001C\u0049\u001B\u0015\u0011\u000E\u000D\u0049\u0020\u001E\u000E\u000D", emit: ((List<IRExpr> args) => ((List<ArityEntry> arities) => ((CodexType ty) => emit_buf_call_3("\u0020\u0019\u001C\u0049\u001B\u0015\u0011\u000E\u000D\u0049\u0020\u001E\u000E\u000D", args, arities))))), new BuiltinEmitter(name: "\u0020\u0019\u001C\u0049\u001B\u0015\u0011\u000E\u000D\u0049\u0020\u001E\u000E\u000D\u0013", emit: ((List<IRExpr> args) => ((List<ArityEntry> arities) => ((CodexType ty) => emit_buf_call_3("\u0020\u0019\u001C\u0049\u001B\u0015\u0011\u000E\u000D\u0049\u0020\u001E\u000E\u000D\u0013", args, arities))))), new BuiltinEmitter(name: "\u0020\u0019\u001C\u0049\u0015\u000D\u000F\u0016\u0049\u0020\u001E\u000E\u000D\u0013", emit: ((List<IRExpr> args) => ((List<ArityEntry> arities) => ((CodexType ty) => emit_buf_call_3("\u0020\u0019\u001C\u0049\u0015\u000D\u000F\u0016\u0049\u0020\u001E\u000E\u000D\u0013", args, arities))))), new BuiltinEmitter(name: "\u0020\u0011\u000E\u0049\u000F\u0012\u0016", emit: ((List<IRExpr> args) => ((List<ArityEntry> arities) => ((CodexType ty) => emit_bit_builtin("\u0020\u0011\u000E\u0049\u000F\u0012\u0016", args, arities))))), new BuiltinEmitter(name: "\u0020\u0011\u000E\u0049\u0010\u0015", emit: ((List<IRExpr> args) => ((List<ArityEntry> arities) => ((CodexType ty) => emit_bit_builtin("\u0020\u0011\u000E\u0049\u0010\u0015", args, arities))))), new BuiltinEmitter(name: "\u0020\u0011\u000E\u0049\u0024\u0010\u0015", emit: ((List<IRExpr> args) => ((List<ArityEntry> arities) => ((CodexType ty) => emit_bit_builtin("\u0020\u0011\u000E\u0049\u0024\u0010\u0015", args, arities))))), new BuiltinEmitter(name: "\u0020\u0011\u000E\u0049\u0013\u0014\u0017", emit: ((List<IRExpr> args) => ((List<ArityEntry> arities) => ((CodexType ty) => emit_bit_builtin("\u0020\u0011\u000E\u0049\u0013\u0014\u0017", args, arities))))), new BuiltinEmitter(name: "\u0020\u0011\u000E\u0049\u0013\u0014\u0015", emit: ((List<IRExpr> args) => ((List<ArityEntry> arities) => ((CodexType ty) => emit_bit_builtin("\u0020\u0011\u000E\u0049\u0013\u0014\u0015", args, arities))))), new BuiltinEmitter(name: "\u0020\u0011\u000E\u0049\u0012\u0010\u000E", emit: ((List<IRExpr> args) => ((List<ArityEntry> arities) => ((CodexType ty) => emit_bit_builtin("\u0020\u0011\u000E\u0049\u0012\u0010\u000E", args, arities))))) });

    public static bool is_builtin_name(string n) => ((Func<long, bool>)((len) => ((Func<long, bool>)((pos) => ((pos >= len) ? false : (builtin_emitters()[(int)pos].name == n))))(bsearch_emitter_pos(builtin_emitters(), n, 0L, len))))(((long)builtin_emitters().Count));

    public static string emit__csharp_emitter_emit_builtin(string n, List<IRExpr> args, List<ArityEntry> arities, CodexType result_ty) => ((Func<long, string>)((len) => ((Func<long, string>)((pos) => ((pos >= len) ? "" : ((Func<BuiltinEmitter, string>)((entry) => ((entry.name == n) ? entry.emit(args)(arities)(result_ty) : "")))(builtin_emitters()[(int)pos]))))(bsearch_emitter_pos(builtin_emitters(), n, 0L, len))))(((long)builtin_emitters().Count));

    public static string cs_name_for(string n) => n.Replace("\u0049", "\u0055");

    public static string emit_buf_call_1(string n, List<IRExpr> args, List<ArityEntry> arities) => ("\u0055\u003A\u0019\u001C\u0041" + (cs_name_for(n) + ("\u004A" + (emit__csharp_emitter_emit_expr(args[(int)0L], arities) + "\u004B"))));

    public static string emit_buf_call_3(string n, List<IRExpr> args, List<ArityEntry> arities) => ("\u0055\u003A\u0019\u001C\u0041" + (cs_name_for(n) + ("\u004A" + (emit__csharp_emitter_emit_expr(args[(int)0L], arities) + ("\u0042\u0002" + (emit__csharp_emitter_emit_expr(args[(int)1L], arities) + ("\u0042\u0002" + (emit__csharp_emitter_emit_expr(args[(int)2L], arities) + "\u004B"))))))));

    public static string bit_op_symbol(string n) => ((n == "\u0020\u0011\u000E\u0049\u000F\u0012\u0016") ? "\u0002\u0054\u0002" : ((n == "\u0020\u0011\u000E\u0049\u0010\u0015") ? "\u0002\u0057\u0002" : ((n == "\u0020\u0011\u000E\u0049\u0024\u0010\u0015") ? "\u0002\u005E\u0002" : ((n == "\u0020\u0011\u000E\u0049\u0013\u0014\u0017") ? "\u0002\u004F\u004F\u0002\u004A\u0011\u0012\u000E\u004B" : ((n == "\u0020\u0011\u000E\u0049\u0013\u0014\u0015") ? "\u0002\u0050\u0050\u0002\u004A\u0011\u0012\u000E\u004B" : "")))));

    public static string emit_bit_builtin(string n, List<IRExpr> args, List<ArityEntry> arities) => ((n == "\u0020\u0011\u000E\u0049\u0012\u0010\u000E") ? ("\u004A\u005C" + (emit__csharp_emitter_emit_expr(args[(int)0L], arities) + "\u004B")) : ("\u004A" + (emit__csharp_emitter_emit_expr(args[(int)0L], arities) + (bit_op_symbol(n) + (emit__csharp_emitter_emit_expr(args[(int)1L], arities) + "\u004B")))));

    public static string emit_builtin_record_set(List<IRExpr> args, List<ArityEntry> arities) => ((Func<string, string>)((field_name) => ("\u004A" + (emit__csharp_emitter_emit_expr(args[(int)0L], arities) + ("\u0002\u001B\u0011\u000E\u0014\u0002\u005A\u0002" + (sanitize(field_name) + ("\u0002\u004D\u0002" + (emit__csharp_emitter_emit_expr(args[(int)2L], arities) + "\u0002\u005B\u004B"))))))))(args[(int)1L] switch { IrTextLit(var s, var sp) => (string)(s), _ => (string)(""), });

    public static string emit_builtin_show(List<IRExpr> args, List<ArityEntry> arities) => ((Func<string, string>)((a0) => ("\u0055\u0032\u0018\u000D\u0041\u0036\u0015\u0010\u001A\u0033\u0012\u0011\u0018\u0010\u0016\u000D\u004A\u0032\u0010\u0012\u0021\u000D\u0015\u000E\u0041\u0028\u0010\u002D\u000E\u0015\u0011\u0012\u001D\u004A" + (a0 + "\u004B\u004B"))))(emit__csharp_emitter_emit_expr(args[(int)0L], arities));

    public static string emit_builtin_print_line(List<IRExpr> args, List<ArityEntry> arities) => ((Func<string, string>)((a0) => ("\u004A\u004A\u0036\u0019\u0012\u0018\u004F\u0010\u0020\u0023\u000D\u0018\u000E\u0050\u004B\u004A\u004A\u004B\u0002\u004D\u0050\u0002\u005A\u0002\u0032\u0010\u0012\u0013\u0010\u0017\u000D\u0041\u0035\u0015\u0011\u000E\u000D\u0031\u0011\u0012\u000D\u004A\u0055\u0032\u0018\u000D\u0041\u0028\u0010\u0033\u0012\u0011\u0018\u0010\u0016\u000D\u004A" + (a0 + "\u004B\u004B\u0046\u0002\u0015\u000D\u000E\u0019\u0015\u0012\u0002\u0012\u0019\u0017\u0017\u0046\u0002\u005B\u004B\u004B\u004A\u004B"))))(emit__csharp_emitter_emit_expr(args[(int)0L], arities));

    public static string emit_builtin_is_letter(List<IRExpr> args, List<ArityEntry> arities) => ((Func<string, string>)((a0) => ("\u004A" + (a0 + ("\u0002\u0050\u004D\u0002\u0004\u0006\u0031\u0002\u0054\u0054\u0002" + (a0 + "\u0002\u004F\u004D\u0002\u0009\u0007\u0031\u004B"))))))(emit__csharp_emitter_emit_expr(args[(int)0L], arities));

    public static string emit_builtin_is_digit(List<IRExpr> args, List<ArityEntry> arities) => ((Func<string, string>)((a0) => ("\u004A" + (a0 + ("\u0002\u0050\u004D\u0002\u0006\u0031\u0002\u0054\u0054\u0002" + (a0 + "\u0002\u004F\u004D\u0002\u0004\u0005\u0031\u004B"))))))(emit__csharp_emitter_emit_expr(args[(int)0L], arities));

    public static string emit_builtin_text_to_integer(List<IRExpr> args, List<ArityEntry> arities) => ((Func<string, string>)((a0) => ("\u0017\u0010\u0012\u001D\u0041\u0039\u000F\u0015\u0013\u000D\u004A\u0055\u0032\u0018\u000D\u0041\u0028\u0010\u0033\u0012\u0011\u0018\u0010\u0016\u000D\u004A" + (a0 + "\u004B\u004B"))))(emit__csharp_emitter_emit_expr(args[(int)0L], arities));

    public static string emit_builtin_text_to_double_bits(List<IRExpr> args, List<ArityEntry> arities) => ((Func<string, string>)((a0) => ("\u003A\u0011\u000E\u0032\u0010\u0012\u0021\u000D\u0015\u000E\u000D\u0015\u0041\u0030\u0010\u0019\u0020\u0017\u000D\u0028\u0010\u002B\u0012\u000E\u0009\u0007\u003A\u0011\u000E\u0013\u004A\u0016\u0010\u0019\u0020\u0017\u000D\u0041\u0039\u000F\u0015\u0013\u000D\u004A\u0055\u0032\u0018\u000D\u0041\u0028\u0010\u0033\u0012\u0011\u0018\u0010\u0016\u000D\u004A" + (a0 + "\u004B\u0042\u0002\u002D\u001E\u0013\u000E\u000D\u001A\u0041\u0037\u0017\u0010\u0020\u000F\u0017\u0011\u0026\u000F\u000E\u0011\u0010\u0012\u0041\u0032\u0019\u0017\u000E\u0019\u0015\u000D\u002B\u0012\u001C\u0010\u0041\u002B\u0012\u0021\u000F\u0015\u0011\u000F\u0012\u000E\u0032\u0019\u0017\u000E\u0019\u0015\u000D\u004B\u004B"))))(emit__csharp_emitter_emit_expr(args[(int)0L], arities));

    public static string emit_builtin_integer_to_text(List<IRExpr> args, List<ArityEntry> arities) => ((Func<string, string>)((a0) => ("\u0055\u0032\u0018\u000D\u0041\u0036\u0015\u0010\u001A\u0033\u0012\u0011\u0018\u0010\u0016\u000D\u004A" + (a0 + "\u0041\u0028\u0010\u002D\u000E\u0015\u0011\u0012\u001D\u004A\u004B\u004B"))))(emit__csharp_emitter_emit_expr(args[(int)0L], arities));

    public static string emit_builtin_char_code_at(List<IRExpr> args, List<ArityEntry> arities) => ((Func<string, string>)((a0) => ((Func<string, string>)((a1) => ("\u004A\u004A\u0017\u0010\u0012\u001D\u004B" + (a0 + ("\u0058\u004A\u0011\u0012\u000E\u004B" + (a1 + "\u0059\u004B"))))))(emit__csharp_emitter_emit_expr(args[(int)1L], arities))))(emit__csharp_emitter_emit_expr(args[(int)0L], arities));

    public static string emit_builtin_char_at(List<IRExpr> args, List<ArityEntry> arities) => ((Func<string, string>)((a0) => ((Func<string, string>)((a1) => ("\u004A\u004A\u0017\u0010\u0012\u001D\u004B" + (a0 + ("\u0058\u004A\u0011\u0012\u000E\u004B" + (a1 + "\u0059\u004B"))))))(emit__csharp_emitter_emit_expr(args[(int)1L], arities))))(emit__csharp_emitter_emit_expr(args[(int)0L], arities));

    public static string emit_builtin_substring(List<IRExpr> args, List<ArityEntry> arities) => ((Func<string, string>)((a0) => ((Func<string, string>)((a1) => ((Func<string, string>)((a2) => (a0 + ("\u0041\u002D\u0019\u0020\u0013\u000E\u0015\u0011\u0012\u001D\u004A\u004A\u0011\u0012\u000E\u004B" + (a1 + ("\u0042\u0002\u004A\u0011\u0012\u000E\u004B" + (a2 + "\u004B")))))))(emit__csharp_emitter_emit_expr(args[(int)2L], arities))))(emit__csharp_emitter_emit_expr(args[(int)1L], arities))))(emit__csharp_emitter_emit_expr(args[(int)0L], arities));

    public static string emit_builtin_list_at(List<IRExpr> args, List<ArityEntry> arities) => ((Func<string, string>)((a0) => ((Func<string, string>)((a1) => (a0 + ("\u0058\u004A\u0011\u0012\u000E\u004B" + (a1 + "\u0059")))))(emit__csharp_emitter_emit_expr(args[(int)1L], arities))))(emit__csharp_emitter_emit_expr(args[(int)0L], arities));

    public static string emit_builtin_list_insert_at(List<IRExpr> args, List<ArityEntry> arities) => ((Func<CodexType, string>)((elem_ty) => ((Func<string, string>)((ty) => ((Func<string, string>)((a0) => ((Func<string, string>)((a1) => ((Func<string, string>)((a2) => ("\u004A\u004A\u0036\u0019\u0012\u0018\u004F\u0031\u0011\u0013\u000E\u004F" + (ty + ("\u0050\u0050\u004B\u004A\u004A\u004B\u0002\u004D\u0050\u0002\u005A\u0002\u0021\u000F\u0015\u0002\u0055\u0017\u0002\u004D\u0002\u0012\u000D\u001B\u0002\u0031\u0011\u0013\u000E\u004F" + (ty + ("\u0050\u004A" + (a0 + ("\u004B\u0046\u0002\u0055\u0017\u0041\u002B\u0012\u0013\u000D\u0015\u000E\u004A\u004A\u0011\u0012\u000E\u004B" + (a1 + ("\u0042\u0002" + (a2 + "\u004B\u0046\u0002\u0015\u000D\u000E\u0019\u0015\u0012\u0002\u0055\u0017\u0046\u0002\u005B\u004B\u004B\u004A\u004B"))))))))))))(emit__csharp_emitter_emit_expr(args[(int)2L], arities))))(emit__csharp_emitter_emit_expr(args[(int)1L], arities))))(emit__csharp_emitter_emit_expr(args[(int)0L], arities))))(cs_type(elem_ty))))(ir_expr_type(args[(int)0L]) switch { ListTy(var et) => (CodexType)(et), _ => (CodexType)(new ErrorTy()), });

    public static string emit_builtin_list_set_at(List<IRExpr> args, List<ArityEntry> arities) => ((Func<CodexType, string>)((elem_ty) => ((Func<string, string>)((ty) => ((Func<string, string>)((a0) => ((Func<string, string>)((a1) => ((Func<string, string>)((a2) => ("\u004A\u004A\u0036\u0019\u0012\u0018\u004F\u0031\u0011\u0013\u000E\u004F" + (ty + ("\u0050\u0050\u004B\u004A\u004A\u004B\u0002\u004D\u0050\u0002\u005A\u0002\u0021\u000F\u0015\u0002\u0055\u0017\u0002\u004D\u0002" + (a0 + ("\u0046\u0002\u0055\u0017\u0058\u004A\u0011\u0012\u000E\u004B" + (a1 + ("\u0059\u0002\u004D\u0002" + (a2 + "\u0046\u0002\u0015\u000D\u000E\u0019\u0015\u0012\u0002\u0055\u0017\u0046\u0002\u005B\u004B\u004B\u004A\u004B"))))))))))(emit__csharp_emitter_emit_expr(args[(int)2L], arities))))(emit__csharp_emitter_emit_expr(args[(int)1L], arities))))(emit__csharp_emitter_emit_expr(args[(int)0L], arities))))(cs_type(elem_ty))))(ir_expr_type(args[(int)0L]) switch { ListTy(var et) => (CodexType)(et), _ => (CodexType)(new ErrorTy()), });

    public static string emit_builtin_list_snoc(List<IRExpr> args, List<ArityEntry> arities) => ((Func<CodexType, string>)((elem_ty) => ((Func<string, string>)((ty) => ((Func<string, string>)((a0) => ((Func<string, string>)((a1) => ("\u004A\u004A\u0036\u0019\u0012\u0018\u004F\u0031\u0011\u0013\u000E\u004F" + (ty + ("\u0050\u0050\u004B\u004A\u004A\u004B\u0002\u004D\u0050\u0002\u005A\u0002\u0021\u000F\u0015\u0002\u0055\u0017\u0002\u004D\u0002" + (a0 + ("\u0046\u0002\u0055\u0017\u0041\u0029\u0016\u0016\u004A" + (a1 + "\u004B\u0046\u0002\u0015\u000D\u000E\u0019\u0015\u0012\u0002\u0055\u0017\u0046\u0002\u005B\u004B\u004B\u004A\u004B"))))))))(emit__csharp_emitter_emit_expr(args[(int)1L], arities))))(emit__csharp_emitter_emit_expr(args[(int)0L], arities))))(cs_type(elem_ty))))(ir_expr_type(args[(int)0L]) switch { ListTy(var et) => (CodexType)(et), _ => (CodexType)(new ErrorTy()), });

    public static string emit_builtin_text_compare(List<IRExpr> args, List<ArityEntry> arities) => ((Func<string, string>)((a0) => ((Func<string, string>)((a1) => ("\u004A\u0017\u0010\u0012\u001D\u004B\u0013\u000E\u0015\u0011\u0012\u001D\u0041\u0032\u0010\u001A\u001F\u000F\u0015\u000D\u002A\u0015\u0016\u0011\u0012\u000F\u0017\u004A" + (a0 + ("\u0042\u0002" + (a1 + "\u004B"))))))(emit__csharp_emitter_emit_expr(args[(int)1L], arities))))(emit__csharp_emitter_emit_expr(args[(int)0L], arities));

    public static string emit_builtin_text_replace(List<IRExpr> args, List<ArityEntry> arities) => ((Func<string, string>)((a0) => ((Func<string, string>)((a1) => ((Func<string, string>)((a2) => (a0 + ("\u0041\u002F\u000D\u001F\u0017\u000F\u0018\u000D\u004A" + (a1 + ("\u0042\u0002" + (a2 + "\u004B")))))))(emit__csharp_emitter_emit_expr(args[(int)2L], arities))))(emit__csharp_emitter_emit_expr(args[(int)1L], arities))))(emit__csharp_emitter_emit_expr(args[(int)0L], arities));

    public static string emit_builtin_read_all(List<IRExpr> args, List<ArityEntry> arities) => ((Func<string, string>)((a0) => ("\u0012\u000D\u001B\u0002\u002D\u001E\u0013\u000E\u000D\u001A\u0041\u002B\u002A\u0041\u002D\u000E\u0015\u000D\u000F\u001A\u002F\u000D\u000F\u0016\u000D\u0015\u004A" + (a0 + "\u004B\u0041\u002F\u000D\u000F\u0016\u0028\u0010\u0027\u0012\u0016\u004A\u004B"))))(emit__csharp_emitter_emit_expr(args[(int)0L], arities));

    public static string emit_builtin_read_file(List<IRExpr> args, List<ArityEntry> arities) => ((Func<string, string>)((a0) => ("\u0055\u0032\u0018\u000D\u0041\u0036\u0015\u0010\u001A\u0033\u0012\u0011\u0018\u0010\u0016\u000D\u004A\u0036\u0011\u0017\u000D\u0041\u002F\u000D\u000F\u0016\u0029\u0017\u0017\u0028\u000D\u0024\u000E\u004A\u0055\u0032\u0018\u000D\u0041\u0028\u0010\u0033\u0012\u0011\u0018\u0010\u0016\u000D\u004A" + (a0 + "\u004B\u004B\u004B"))))(emit__csharp_emitter_emit_expr(args[(int)0L], arities));

    public static string emit_builtin_write_file(List<IRExpr> args, List<ArityEntry> arities) => ((Func<string, string>)((a0) => ((Func<string, string>)((a1) => ("\u0036\u0011\u0017\u000D\u0041\u0035\u0015\u0011\u000E\u000D\u0029\u0017\u0017\u0028\u000D\u0024\u000E\u004A\u0055\u0032\u0018\u000D\u0041\u0028\u0010\u0033\u0012\u0011\u0018\u0010\u0016\u000D\u004A" + (a0 + ("\u004B\u0042\u0002\u0055\u0032\u0018\u000D\u0041\u0028\u0010\u0033\u0012\u0011\u0018\u0010\u0016\u000D\u004A" + (a1 + "\u004B\u004B"))))))(emit__csharp_emitter_emit_expr(args[(int)1L], arities))))(emit__csharp_emitter_emit_expr(args[(int)0L], arities));

    public static string emit_builtin_file_exists(List<IRExpr> args, List<ArityEntry> arities) => ((Func<string, string>)((a0) => ("\u0036\u0011\u0017\u000D\u0041\u0027\u0024\u0011\u0013\u000E\u0013\u004A\u0055\u0032\u0018\u000D\u0041\u0028\u0010\u0033\u0012\u0011\u0018\u0010\u0016\u000D\u004A" + (a0 + "\u004B\u004B"))))(emit__csharp_emitter_emit_expr(args[(int)0L], arities));

    public static string emit_builtin_list_files(List<IRExpr> args, List<ArityEntry> arities) => ((Func<string, string>)((a0) => ((Func<string, string>)((a1) => ("\u0030\u0011\u0015\u000D\u0018\u000E\u0010\u0015\u001E\u0041\u0037\u000D\u000E\u0036\u0011\u0017\u000D\u0013\u004A\u0055\u0032\u0018\u000D\u0041\u0028\u0010\u0033\u0012\u0011\u0018\u0010\u0016\u000D\u004A" + (a0 + ("\u004B\u0042\u0002\u0055\u0032\u0018\u000D\u0041\u0028\u0010\u0033\u0012\u0011\u0018\u0010\u0016\u000D\u004A" + (a1 + "\u004B\u004B\u0041\u002D\u000D\u0017\u000D\u0018\u000E\u004A\u0055\u0032\u0018\u000D\u0041\u0036\u0015\u0010\u001A\u0033\u0012\u0011\u0018\u0010\u0016\u000D\u004B\u0041\u0028\u0010\u0031\u0011\u0013\u000E\u004A\u004B"))))))(emit__csharp_emitter_emit_expr(args[(int)1L], arities))))(emit__csharp_emitter_emit_expr(args[(int)0L], arities));

    public static string emit_builtin_text_split(List<IRExpr> args, List<ArityEntry> arities) => ((Func<string, string>)((a0) => ((Func<string, string>)((a1) => ("\u0012\u000D\u001B\u0002\u0031\u0011\u0013\u000E\u004F\u0013\u000E\u0015\u0011\u0012\u001D\u0050\u004A" + (a0 + ("\u0041\u002D\u001F\u0017\u0011\u000E\u004A" + (a1 + "\u004B\u004B"))))))(emit__csharp_emitter_emit_expr(args[(int)1L], arities))))(emit__csharp_emitter_emit_expr(args[(int)0L], arities));

    public static string emit_builtin_text_contains(List<IRExpr> args, List<ArityEntry> arities) => ((Func<string, string>)((a0) => ((Func<string, string>)((a1) => (a0 + ("\u0041\u0032\u0010\u0012\u000E\u000F\u0011\u0012\u0013\u004A" + (a1 + "\u004B")))))(emit__csharp_emitter_emit_expr(args[(int)1L], arities))))(emit__csharp_emitter_emit_expr(args[(int)0L], arities));

    public static string emit_builtin_text_starts_with(List<IRExpr> args, List<ArityEntry> arities) => ((Func<string, string>)((a0) => ((Func<string, string>)((a1) => (a0 + ("\u0041\u002D\u000E\u000F\u0015\u000E\u0013\u0035\u0011\u000E\u0014\u004A" + (a1 + "\u004B")))))(emit__csharp_emitter_emit_expr(args[(int)1L], arities))))(emit__csharp_emitter_emit_expr(args[(int)0L], arities));

    public static string emit_builtin_get_env(List<IRExpr> args, List<ArityEntry> arities) => ((Func<string, string>)((a0) => ("\u0055\u0032\u0018\u000D\u0041\u0036\u0015\u0010\u001A\u0033\u0012\u0011\u0018\u0010\u0016\u000D\u004A\u0027\u0012\u0021\u0011\u0015\u0010\u0012\u001A\u000D\u0012\u000E\u0041\u0037\u000D\u000E\u0027\u0012\u0021\u0011\u0015\u0010\u0012\u001A\u000D\u0012\u000E\u003B\u000F\u0015\u0011\u000F\u0020\u0017\u000D\u004A\u0055\u0032\u0018\u000D\u0041\u0028\u0010\u0033\u0012\u0011\u0018\u0010\u0016\u000D\u004A" + (a0 + "\u004B\u004B\u0002\u0044\u0044\u0002\u0048\u0048\u004B"))))(emit__csharp_emitter_emit_expr(args[(int)0L], arities));

    public static string emit_builtin_run_process(List<IRExpr> args, List<ArityEntry> arities) => ((Func<string, string>)((a0) => ((Func<string, string>)((a1) => ("\u0055\u0032\u0018\u000D\u0041\u0036\u0015\u0010\u001A\u0033\u0012\u0011\u0018\u0010\u0016\u000D\u004A\u004A\u004A\u0036\u0019\u0012\u0018\u004F\u0013\u000E\u0015\u0011\u0012\u001D\u0050\u004B\u004A\u004A\u004B\u0002\u004D\u0050\u0002\u005A" + ("\u0002\u0021\u000F\u0015\u0002\u0055\u001F\u0013\u0011\u0002\u004D\u0002\u0012\u000D\u001B\u0002\u002D\u001E\u0013\u000E\u000D\u001A\u0041\u0030\u0011\u000F\u001D\u0012\u0010\u0013\u000E\u0011\u0018\u0013\u0041\u0039\u0015\u0010\u0018\u000D\u0013\u0013\u002D\u000E\u000F\u0015\u000E\u002B\u0012\u001C\u0010\u004A" + ("\u0055\u0032\u0018\u000D\u0041\u0028\u0010\u0033\u0012\u0011\u0018\u0010\u0016\u000D\u004A" + (a0 + ("\u004B\u0042\u0002\u0055\u0032\u0018\u000D\u0041\u0028\u0010\u0033\u0012\u0011\u0018\u0010\u0016\u000D\u004A" + (a1 + ("\u004B\u004B" + ("\u0002\u005A\u0002\u002F\u000D\u0016\u0011\u0015\u000D\u0018\u000E\u002D\u000E\u000F\u0012\u0016\u000F\u0015\u0016\u002A\u0019\u000E\u001F\u0019\u000E\u0002\u004D\u0002\u000E\u0015\u0019\u000D\u0042\u0002\u0033\u0013\u000D\u002D\u0014\u000D\u0017\u0017\u0027\u0024\u000D\u0018\u0019\u000E\u000D\u0002\u004D\u0002\u001C\u000F\u0017\u0013\u000D\u0002\u005B\u0046" + ("\u0002\u0021\u000F\u0015\u0002\u0055\u001F\u0002\u004D\u0002\u002D\u001E\u0013\u000E\u000D\u001A\u0041\u0030\u0011\u000F\u001D\u0012\u0010\u0013\u000E\u0011\u0018\u0013\u0041\u0039\u0015\u0010\u0018\u000D\u0013\u0013\u0041\u002D\u000E\u000F\u0015\u000E\u004A\u0055\u001F\u0013\u0011\u004B\u0043\u0046" + ("\u0002\u0021\u000F\u0015\u0002\u0055\u0010\u0002\u004D\u0002\u0055\u001F\u0041\u002D\u000E\u000F\u0012\u0016\u000F\u0015\u0016\u002A\u0019\u000E\u001F\u0019\u000E\u0041\u002F\u000D\u000F\u0016\u0028\u0010\u0027\u0012\u0016\u004A\u004B\u0046" + "\u0002\u0055\u001F\u0041\u0035\u000F\u0011\u000E\u0036\u0010\u0015\u0027\u0024\u0011\u000E\u004A\u004B\u0046\u0002\u0015\u000D\u000E\u0019\u0015\u0012\u0002\u0055\u0010\u0046\u0002\u005B\u004B\u004B\u004A\u004B"))))))))))))(emit__csharp_emitter_emit_expr(args[(int)1L], arities))))(emit__csharp_emitter_emit_expr(args[(int)0L], arities));

    public static string emit_builtin_par(List<IRExpr> args, List<ArityEntry> arities) => ((Func<string, string>)((a0) => ((Func<string, string>)((a1) => ("\u0028\u000F\u0013\u0022\u0041\u0035\u0014\u000D\u0012\u0029\u0017\u0017\u004A" + (a1 + ("\u0041\u002D\u000D\u0017\u000D\u0018\u000E\u004A\u0055\u0024\u0055\u0002\u004D\u0050\u0002\u0028\u000F\u0013\u0022\u0041\u002F\u0019\u0012\u004A\u004A\u004B\u0002\u004D\u0050\u0002\u004A" + (a0 + "\u004B\u004A\u0055\u0024\u0055\u004B\u004B\u004B\u004B\u0041\u002F\u000D\u0013\u0019\u0017\u000E\u0041\u0028\u0010\u0031\u0011\u0013\u000E\u004A\u004B"))))))(emit__csharp_emitter_emit_expr(args[(int)1L], arities))))(emit__csharp_emitter_emit_expr(args[(int)0L], arities));

    public static string emit_builtin_race(List<IRExpr> args, List<ArityEntry> arities) => ((Func<string, string>)((a0) => ("\u0028\u000F\u0013\u0022\u0041\u0035\u0014\u000D\u0012\u0029\u0012\u001E\u004A" + (a0 + "\u0041\u002D\u000D\u0017\u000D\u0018\u000E\u004A\u0055\u000E\u0055\u0002\u004D\u0050\u0002\u0028\u000F\u0013\u0022\u0041\u002F\u0019\u0012\u004A\u004A\u004B\u0002\u004D\u0050\u0002\u0055\u000E\u0055\u004A\u0012\u0019\u0017\u0017\u004B\u004B\u004B\u004B\u0041\u002F\u000D\u0013\u0019\u0017\u000E\u0041\u002F\u000D\u0013\u0019\u0017\u000E"))))(emit__csharp_emitter_emit_expr(args[(int)0L], arities));

    public static string emit__csharp_emitter_emit_apply(IRExpr e, List<ArityEntry> arities) => ((Func<ApplyChain, string>)((chain) => ((Func<IRExpr, string>)((root) => ((Func<List<IRExpr>, string>)((args) => root switch { IrName(var n, var ty, var sp) => (string)((is_builtin_name(n) ? emit__csharp_emitter_emit_builtin(n, args, arities, ir_expr_type(e)) : (((((long)n.Length) > 0L) && is_upper_letter(((long)n[(int)0L]))) ? ((Func<CodexType, string>)((result_ty) => ((Func<string, string>)((ctor_type_args) => ("\u0012\u000D\u001B\u0002" + (sanitize(n) + (ctor_type_args + ("\u004A" + (emit__csharp_emitter_emit_apply_args(args, arities, 0L) + "\u004B")))))))(extract_ctor_type_args(result_ty))))(ir_expr_type(e)) : ((Func<long, string>)((ar) => (((ar > 1L) && (((long)args.Count) == ar)) ? (sanitize(n) + ("\u004A" + (emit__csharp_emitter_emit_apply_args(args, arities, 0L) + "\u004B"))) : (((ar > 1L) && (((long)args.Count) < ar)) ? ((Func<long, string>)((remaining) => (emit_partial_wrappers(0L, remaining) + (sanitize(n) + ("\u004A" + (emit__csharp_emitter_emit_apply_args(args, arities, 0L) + ("\u0042\u0002" + (emit_partial_params(0L, remaining) + "\u004B"))))))))((ar - ((long)args.Count))) : emit_expr_curried(e, arities)))))(lookup_arity(arities, n))))), _ => (string)(emit_expr_curried(e, arities)), }))(chain.args)))(chain.root)))(collect_apply_chain(e, new List<IRExpr>()));

    public static string emit_expr_curried(IRExpr e, List<ArityEntry> arities) => e switch { IrApply(var f, var a, var ty, var sp) => (string)((emit__csharp_emitter_emit_expr(f, arities) + ("\u004A" + (emit__csharp_emitter_emit_expr(a, arities) + "\u004B")))), _ => (string)(emit__csharp_emitter_emit_expr(e, arities)), };

    public static string emit__csharp_emitter_emit_expr(IRExpr e, List<ArityEntry> arities) => e switch { IrIntLit(var n, var sp) => (string)((_Cce.FromUnicode(n.ToString()) + "\u0031")), IrNumLit(var n, var sp) => (string)(("\u003A\u0011\u000E\u0032\u0010\u0012\u0021\u000D\u0015\u000E\u000D\u0015\u0041\u002B\u0012\u000E\u0009\u0007\u003A\u0011\u000E\u0013\u0028\u0010\u0030\u0010\u0019\u0020\u0017\u000D\u004A" + (_Cce.FromUnicode(n.ToString()) + "\u0031\u004B"))), IrTextLit(var s, var sp) => (string)(("\u0048" + (emit__csharp_emitter_escape_text(s) + "\u0048"))), IrBoolLit(var b, var sp) => (string)((b ? "\u000E\u0015\u0019\u000D" : "\u001C\u000F\u0017\u0013\u000D")), IrCharLit(var n, var sp) => (string)(_Cce.FromUnicode(n.ToString())), IrName(var n, var ty, var sp) => (string)(((n == "\u0015\u000D\u000F\u0016\u0049\u0017\u0011\u0012\u000D") ? "\u0055\u0032\u0018\u000D\u0041\u0036\u0015\u0010\u001A\u0033\u0012\u0011\u0018\u0010\u0016\u000D\u004A\u0032\u0010\u0012\u0013\u0010\u0017\u000D\u0041\u002F\u000D\u000F\u0016\u0031\u0011\u0012\u000D\u004A\u004B\u0002\u0044\u0044\u0002\u0048\u0048\u004B" : ((n == "\u001D\u000D\u000E\u0049\u000F\u0015\u001D\u0013") ? "\u0027\u0012\u0021\u0011\u0015\u0010\u0012\u001A\u000D\u0012\u000E\u0041\u0037\u000D\u000E\u0032\u0010\u001A\u001A\u000F\u0012\u0016\u0031\u0011\u0012\u000D\u0029\u0015\u001D\u0013\u004A\u004B\u0041\u002D\u000D\u0017\u000D\u0018\u000E\u004A\u0055\u0032\u0018\u000D\u0041\u0036\u0015\u0010\u001A\u0033\u0012\u0011\u0018\u0010\u0016\u000D\u004B\u0041\u0028\u0010\u0031\u0011\u0013\u000E\u004A\u004B" : ((n == "\u0018\u0019\u0015\u0015\u000D\u0012\u000E\u0049\u0016\u0011\u0015") ? "\u0055\u0032\u0018\u000D\u0041\u0036\u0015\u0010\u001A\u0033\u0012\u0011\u0018\u0010\u0016\u000D\u004A\u0030\u0011\u0015\u000D\u0018\u000E\u0010\u0015\u001E\u0041\u0037\u000D\u000E\u0032\u0019\u0015\u0015\u000D\u0012\u000E\u0030\u0011\u0015\u000D\u0018\u000E\u0010\u0015\u001E\u004A\u004B\u004B" : ((n == "\u0014\u000D\u000F\u001F\u0049\u0013\u000F\u0021\u000D") ? "\u0055\u003A\u0019\u001C\u0041\u0014\u000D\u000F\u001F\u0055\u0013\u000F\u0021\u000D\u004A\u004B" : ((n == "\u0014\u000D\u000F\u001F\u0049\u0015\u000D\u0013\u000E\u0010\u0015\u000D") ? "\u0012\u000D\u001B\u0002\u0036\u0019\u0012\u0018\u004F\u0010\u0020\u0023\u000D\u0018\u000E\u0042\u0002\u0017\u0010\u0012\u001D\u0050\u004A\u0055\u001F\u0002\u004D\u0050\u0002\u0055\u003A\u0019\u001C\u0041\u0014\u000D\u000F\u001F\u0055\u0015\u000D\u0013\u000E\u0010\u0015\u000D\u004A\u0055\u001F\u004B\u004B" : ((n == "\u0014\u000D\u000F\u001F\u0049\u000F\u0016\u0021\u000F\u0012\u0018\u000D") ? "\u0012\u000D\u001B\u0002\u0036\u0019\u0012\u0018\u004F\u0010\u0020\u0023\u000D\u0018\u000E\u0042\u0002\u0017\u0010\u0012\u001D\u0050\u004A\u0055\u0012\u0002\u004D\u0050\u0002\u0055\u003A\u0019\u001C\u0041\u0014\u000D\u000F\u001F\u0055\u000F\u0016\u0021\u000F\u0012\u0018\u000D\u004A\u0055\u0012\u004B\u004B" : ((n == "\u0017\u0011\u0013\u000E\u0049\u001B\u0011\u000E\u0014\u0049\u0018\u000F\u001F\u000F\u0018\u0011\u000E\u001E") ? "\u0012\u000D\u001B\u0002\u0036\u0019\u0012\u0018\u004F\u0010\u0020\u0023\u000D\u0018\u000E\u0042\u0002\u0031\u0011\u0013\u000E\u004F\u0017\u0010\u0012\u001D\u0050\u0050\u004A\u0055\u0018\u0002\u004D\u0050\u0002\u0055\u003A\u0019\u001C\u0041\u0017\u0011\u0013\u000E\u0055\u001B\u0011\u000E\u0014\u0055\u0018\u000F\u001F\u000F\u0018\u0011\u000E\u001E\u004A\u0055\u0018\u004B\u004B" : ((n == "\u0020\u0019\u001C\u0049\u001B\u0015\u0011\u000E\u000D\u0049\u0020\u001E\u000E\u000D") ? "\u0012\u000D\u001B\u0002\u0036\u0019\u0012\u0018\u004F\u0010\u0020\u0023\u000D\u0018\u000E\u0042\u0002\u0036\u0019\u0012\u0018\u004F\u0010\u0020\u0023\u000D\u0018\u000E\u0042\u0002\u0036\u0019\u0012\u0018\u004F\u0010\u0020\u0023\u000D\u0018\u000E\u0042\u0002\u0017\u0010\u0012\u001D\u0050\u0050\u0050\u004A\u0055\u0020\u0002\u004D\u0050\u0002\u0055\u0010\u0002\u004D\u0050\u0002\u0055\u0021\u0002\u004D\u0050\u0002\u0055\u003A\u0019\u001C\u0041\u0020\u0019\u001C\u0055\u001B\u0015\u0011\u000E\u000D\u0055\u0020\u001E\u000E\u000D\u004A\u0055\u0020\u0042\u0002\u0055\u0010\u0042\u0002\u0055\u0021\u004B\u004B" : ((n == "\u0020\u0019\u001C\u0049\u001B\u0015\u0011\u000E\u000D\u0049\u0020\u001E\u000E\u000D\u0013") ? "\u0012\u000D\u001B\u0002\u0036\u0019\u0012\u0018\u004F\u0010\u0020\u0023\u000D\u0018\u000E\u0042\u0002\u0036\u0019\u0012\u0018\u004F\u0010\u0020\u0023\u000D\u0018\u000E\u0042\u0002\u0036\u0019\u0012\u0018\u004F\u0010\u0020\u0023\u000D\u0018\u000E\u0042\u0002\u0017\u0010\u0012\u001D\u0050\u0050\u0050\u004A\u0055\u0020\u0002\u004D\u0050\u0002\u0055\u0010\u0002\u004D\u0050\u0002\u0055\u0021\u0013\u0002\u004D\u0050\u0002\u0055\u003A\u0019\u001C\u0041\u0020\u0019\u001C\u0055\u001B\u0015\u0011\u000E\u000D\u0055\u0020\u001E\u000E\u000D\u0013\u004A\u0055\u0020\u0042\u0002\u0055\u0010\u0042\u0002\u0055\u0021\u0013\u004B\u004B" : ((n == "\u0020\u0019\u001C\u0049\u0015\u000D\u000F\u0016\u0049\u0020\u001E\u000E\u000D\u0013") ? "\u0012\u000D\u001B\u0002\u0036\u0019\u0012\u0018\u004F\u0010\u0020\u0023\u000D\u0018\u000E\u0042\u0002\u0036\u0019\u0012\u0018\u004F\u0010\u0020\u0023\u000D\u0018\u000E\u0042\u0002\u0036\u0019\u0012\u0018\u004F\u0010\u0020\u0023\u000D\u0018\u000E\u0042\u0002\u0031\u0011\u0013\u000E\u004F\u0017\u0010\u0012\u001D\u0050\u0050\u0050\u0050\u004A\u0055\u0020\u0002\u004D\u0050\u0002\u0055\u0010\u0002\u004D\u0050\u0002\u0055\u0012\u0002\u004D\u0050\u0002\u0055\u003A\u0019\u001C\u0041\u0020\u0019\u001C\u0055\u0015\u000D\u000F\u0016\u0055\u0020\u001E\u000E\u000D\u0013\u004A\u0055\u0020\u0042\u0002\u0055\u0010\u0042\u0002\u0055\u0012\u004B\u004B" : (((((long)n.Length) > 0L) && is_upper_letter(((long)n[(int)0L]))) ? ("\u0012\u000D\u001B\u0002" + (sanitize(n) + (extract_ctor_type_args(ty) + "\u004A\u004B"))) : ((lookup_arity(arities, n) == 0L) ? (sanitize(n) + "\u004A\u004B") : ((Func<long, string>)((ar) => ((ar >= 2L) ? (emit_partial_wrappers(0L, ar) + (sanitize(n) + ("\u004A" + (emit_partial_params(0L, ar) + "\u004B")))) : sanitize(n))))(lookup_arity(arities, n))))))))))))))), IrBinary(var op, var l, var r, var ty, var sp) => (string)(emit__csharp_emitter_emit_binary(op, l, r, ty, arities)), IrNegate(var operand, var sp) => (string)(("\u004A\u0049" + (emit__csharp_emitter_emit_expr(operand, arities) + "\u004B"))), IrIf(var c, var t, var el, var ty, var sp) => (string)(("\u004A" + (emit__csharp_emitter_emit_expr(c, arities) + ("\u0002\u0044\u0002" + (emit__csharp_emitter_emit_expr(t, arities) + ("\u0002\u0045\u0002" + (emit__csharp_emitter_emit_expr(el, arities) + "\u004B"))))))), IrLet(var name, var ty, var val, var body, var sp) => (string)(emit__csharp_emitter_emit_let(name, ty, val, body, arities)), IrApply(var f, var a, var ty, var sp) => (string)(emit__csharp_emitter_emit_apply(e, arities)), IrLambda(var @params, var body, var ty, var sp) => (string)(emit__csharp_emitter_emit_lambda(@params, body, arities)), IrList(var elems, var ty, var sp) => (string)(emit__csharp_emitter_emit_list(elems, ty, arities)), IrMatch(var scrut, var branches, var ty, var sp) => (string)(emit__csharp_emitter_emit_match(scrut, branches, ty, arities)), IrAct(var stmts, var ty, var sp) => (string)(emit__csharp_emitter_emit_act(stmts, ty, arities)), IrHandle(var eff, var body, var clauses, var ty, var sp) => (string)(emit__csharp_emitter_emit_handle(eff, body, clauses, ty, arities)), IrRecord(var name, var fields, var ty, var sp) => (string)(emit__csharp_emitter_emit_record(name, fields, arities)), IrFieldAccess(var rec, var field, var ty, var sp) => (string)((emit__csharp_emitter_emit_expr(rec, arities) + ("\u0041" + sanitize(field)))), IrFork(var body, var ty, var sp) => (string)(("\u0028\u000F\u0013\u0022\u0041\u002F\u0019\u0012\u004A\u004A\u004B\u0002\u004D\u0050\u0002\u004A" + (emit__csharp_emitter_emit_expr(body, arities) + "\u004B\u004A\u0012\u0019\u0017\u0017\u004B\u004B"))), IrAwait(var task, var ty, var sp) => (string)(("\u004A" + (emit__csharp_emitter_emit_expr(task, arities) + "\u004B\u0041\u002F\u000D\u0013\u0019\u0017\u000E"))), IrError(var msg, var ty, var sp) => (string)(("\u0051\u004E\u0002\u000D\u0015\u0015\u0010\u0015\u0045\u0002" + (msg + "\u0002\u004E\u0051\u0002\u0016\u000D\u001C\u000F\u0019\u0017\u000E"))), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static string hex_digit(long n) => ((n == 0L) ? "\u0003" : ((n == 1L) ? "\u0004" : ((n == 2L) ? "\u0005" : ((n == 3L) ? "\u0006" : ((n == 4L) ? "\u0007" : ((n == 5L) ? "\u0008" : ((n == 6L) ? "\u0009" : ((n == 7L) ? "\u000A" : ((n == 8L) ? "\u000B" : ((n == 9L) ? "\u000C" : ((n == 10L) ? "\u0029" : ((n == 11L) ? "\u003A" : ((n == 12L) ? "\u0032" : ((n == 13L) ? "\u0030" : ((n == 14L) ? "\u0027" : ((n == 15L) ? "\u0036" : "\u0044"))))))))))))))));

    public static string hex4(long n) => ("\u0003\u0003" + (hex_digit((n / 16L)) + hex_digit((n - ((n / 16L) * 16L)))));

    public static string escape_cce_char(long c) => ("\u0056\u0019" + hex4(c));

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
            var _tco_1 = (i + 1L);
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

    public static string emit__csharp_emitter_escape_text(string s) => string.Concat(escape_text_loop(s, 0L, ((long)s.Length), new List<string>()));

    public static string emit_bin_op(IRBinaryOp op) => op switch { IrAddInt { } => (string)("\u004C"), IrSubInt { } => (string)("\u0049"), IrMulInt { } => (string)("\u004E"), IrDivInt { } => (string)("\u0051"), IrPowInt { } => (string)("\u005E"), IrAddNum { } => (string)("\u004C"), IrSubNum { } => (string)("\u0049"), IrMulNum { } => (string)("\u004E"), IrDivNum { } => (string)("\u0051"), IrEq { } => (string)("\u004D\u004D"), IrNotEq { } => (string)("\u0043\u004D"), IrLt { } => (string)("\u004F"), IrGt { } => (string)("\u0050"), IrLtEq { } => (string)("\u004F\u004D"), IrGtEq { } => (string)("\u0050\u004D"), IrAnd { } => (string)("\u0054\u0054"), IrOr { } => (string)("\u0057\u0057"), IrAppendText { } => (string)("\u004C"), IrAppendList { } => (string)("\u004C"), IrConsList { } => (string)("\u004C"), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static string emit__csharp_emitter_emit_binary(IRBinaryOp op, IRExpr l, IRExpr r, CodexType ty, List<ArityEntry> arities) => op switch { IrAppendList { } => (string)(("\u0027\u0012\u0019\u001A\u000D\u0015\u000F\u0020\u0017\u000D\u0041\u0032\u0010\u0012\u0018\u000F\u000E\u004A" + (emit__csharp_emitter_emit_expr(l, arities) + ("\u0042\u0002" + (emit__csharp_emitter_emit_expr(r, arities) + "\u004B\u0041\u0028\u0010\u0031\u0011\u0013\u000E\u004A\u004B"))))), IrConsList { } => (string)(("\u0012\u000D\u001B\u0002\u0031\u0011\u0013\u000E\u004F" + (cs_type(ir_expr_type(l)) + ("\u0050\u0002\u005A\u0002" + (emit__csharp_emitter_emit_expr(l, arities) + ("\u0002\u005B\u0041\u0032\u0010\u0012\u0018\u000F\u000E\u004A" + (emit__csharp_emitter_emit_expr(r, arities) + "\u004B\u0041\u0028\u0010\u0031\u0011\u0013\u000E\u004A\u004B"))))))), _ => (string)(("\u004A" + (emit__csharp_emitter_emit_expr(l, arities) + ("\u0002" + (emit_bin_op(op) + ("\u0002" + (emit__csharp_emitter_emit_expr(r, arities) + "\u004B"))))))), };

    public static string cs_type_or_dynamic(CodexType ty) => ((Func<string, string>)((t) => ((t == "\u0010\u0020\u0023\u000D\u0018\u000E") ? "\u0016\u001E\u0012\u000F\u001A\u0011\u0018" : t)))(cs_type(ty));

    public static string emit__csharp_emitter_emit_let(string name, CodexType ty, IRExpr val, IRExpr body, List<ArityEntry> arities) => ("\u004A\u004A\u0036\u0019\u0012\u0018\u004F" + (cs_type_or_dynamic(ty) + ("\u0042\u0002" + (cs_type(ir_expr_type(body)) + ("\u0050\u004B\u004A\u004A" + (sanitize(name) + ("\u004B\u0002\u004D\u0050\u0002" + (emit__csharp_emitter_emit_expr(body, arities) + ("\u004B\u004B\u004A" + (emit__csharp_emitter_emit_expr(val, arities) + "\u004B"))))))))));

    public static string emit__csharp_emitter_emit_lambda(List<IRParam> @params, IRExpr body, List<ArityEntry> arities) => ((((long)@params.Count) == 0L) ? ("\u004A\u004A\u004B\u0002\u004D\u0050\u0002" + (emit__csharp_emitter_emit_expr(body, arities) + "\u004B")) : ("\u004A" + (emit__csharp_emitter_emit_lambda_params(@params, 0L, ((long)@params.Count)) + (emit__csharp_emitter_emit_expr(body, arities) + "\u004B"))));

    public static string emit__csharp_emitter_emit_lambda_params(List<IRParam> @params, long i, long len) => ((i == len) ? "" : ((Func<IRParam, string>)((p) => ("\u004A" + (cs_type(p.type_val) + ("\u0002" + (sanitize(p.name) + ("\u004B\u0002\u004D\u0050\u0002" + emit__csharp_emitter_emit_lambda_params(@params, (i + 1L), len))))))))(@params[(int)i]));

    public static string emit__csharp_emitter_emit_list(List<IRExpr> elems, CodexType ty, List<ArityEntry> arities) => ((((long)elems.Count) == 0L) ? ("\u0012\u000D\u001B\u0002\u0031\u0011\u0013\u000E\u004F" + (cs_type(ty) + "\u0050\u004A\u004B")) : ("\u0012\u000D\u001B\u0002\u0031\u0011\u0013\u000E\u004F" + (cs_type(ty) + ("\u0050\u0002\u005A\u0002" + (emit__csharp_emitter_emit_list_elems(elems, 0L, arities) + "\u0002\u005B")))));

    public static string emit__csharp_emitter_emit_list_elems(List<IRExpr> elems, long i, List<ArityEntry> arities) => ((i == ((long)elems.Count)) ? "" : ((i == (((long)elems.Count) - 1L)) ? emit__csharp_emitter_emit_expr(elems[(int)i], arities) : (emit__csharp_emitter_emit_expr(elems[(int)i], arities) + ("\u0042\u0002" + emit__csharp_emitter_emit_list_elems(elems, (i + 1L), arities)))));

    public static string emit__csharp_emitter_emit_match(IRExpr scrut, List<IRBranch> branches, CodexType ty, List<ArityEntry> arities) => ((Func<string, string>)((result_cast) => ((Func<string, string>)((arms) => ((Func<bool, string>)((needs_wild) => (emit__csharp_emitter_emit_expr(scrut, arities) + ("\u0002\u0013\u001B\u0011\u000E\u0018\u0014\u0002\u005A\u0002" + (arms + ((needs_wild ? "\u0055\u0002\u004D\u0050\u0002\u000E\u0014\u0015\u0010\u001B\u0002\u0012\u000D\u001B\u0002\u002B\u0012\u0021\u000F\u0017\u0011\u0016\u002A\u001F\u000D\u0015\u000F\u000E\u0011\u0010\u0012\u0027\u0024\u0018\u000D\u001F\u000E\u0011\u0010\u0012\u004A\u0048\u002C\u0010\u0012\u0049\u000D\u0024\u0014\u000F\u0019\u0013\u000E\u0011\u0021\u000D\u0002\u001A\u000F\u000E\u0018\u0014\u0048\u004B\u0042\u0002" : "") + "\u005B"))))))((has_any_catch_all(branches, 0L) ? false : true))))(emit_match_arms(branches, 0L, arities, result_cast))))(("\u004A" + (cs_type(ty) + "\u004B")));

    public static string emit_match_arms(List<IRBranch> branches, long i, List<ArityEntry> arities, string result_cast) => ((i == ((long)branches.Count)) ? "" : ((Func<IRBranch, string>)((arm) => ((Func<string, string>)((this_arm) => (is_catch_all(arm.pattern) ? this_arm : (this_arm + emit_match_arms(branches, (i + 1L), arities, result_cast)))))((emit__csharp_emitter_emit_pattern(arm.pattern) + ("\u0002\u004D\u0050\u0002" + (result_cast + ("\u004A" + (emit__csharp_emitter_emit_expr(arm.body, arities) + "\u004B\u0042\u0002"))))))))(branches[(int)i]));

    public static bool is_catch_all(IRPat p) => p switch { IrWildPat(var sp) => (bool)(true), IrVarPat(var name, var ty, var sp) => (bool)(true), _ => (bool)(false), };

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
            var _tco_1 = (i + 1L);
            branches = _tco_0;
            i = _tco_1;
            continue;
            }
            }
        }
    }

    public static string emit__csharp_emitter_emit_pattern(IRPat p) => p switch { IrVarPat(var name, var ty, var sp) => (string)((cs_type(ty) + ("\u0002" + sanitize(name)))), IrLitPat(var text, var ty, var sp) => (string)(text), IrCtorPat(var name, var subs, var ty, var sp) => (string)(((((long)subs.Count) == 0L) ? (sanitize(name) + (extract_ctor_type_args(ty) + "\u0002\u005A\u0002\u005B")) : (sanitize(name) + (extract_ctor_type_args(ty) + ("\u004A" + (emit__csharp_emitter_emit_sub_patterns(subs, 0L) + "\u004B")))))), IrWildPat(var sp) => (string)("\u0055"), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static string emit__csharp_emitter_emit_sub_patterns(List<IRPat> subs, long i) => ((i == ((long)subs.Count)) ? "" : ((Func<IRPat, string>)((sub) => (emit_sub_pattern(sub) + (((i < (((long)subs.Count) - 1L)) ? "\u0042\u0002" : "") + emit__csharp_emitter_emit_sub_patterns(subs, (i + 1L))))))(subs[(int)i]));

    public static string emit_sub_pattern(IRPat p) => p switch { IrVarPat(var name, var ty, var sp) => (string)(("\u0021\u000F\u0015\u0002" + sanitize(name))), IrCtorPat(var name, var subs, var ty, var sp) => (string)(emit__csharp_emitter_emit_pattern(p)), IrWildPat(var sp) => (string)("\u0055"), IrLitPat(var text, var ty, var sp) => (string)(text), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static string emit__csharp_emitter_emit_act(List<IRActStmt> stmts, CodexType ty, List<ArityEntry> arities) => ((Func<CodexType, string>)((actual_ty) => ((Func<string, string>)((ret_type) => ((Func<long, string>)((len) => actual_ty switch { VoidTy { } => (string)(("\u004A\u004A\u0036\u0019\u0012\u0018\u004F\u0010\u0020\u0023\u000D\u0018\u000E\u0050\u004B\u004A\u004A\u004B\u0002\u004D\u0050\u0002\u005A\u0002" + (emit__csharp_emitter_emit_act_stmts(stmts, 0L, len, false, arities) + "\u0002\u0015\u000D\u000E\u0019\u0015\u0012\u0002\u0012\u0019\u0017\u0017\u0046\u0002\u005B\u004B\u004B\u004A\u004B"))), NothingTy { } => (string)(("\u004A\u004A\u0036\u0019\u0012\u0018\u004F\u0010\u0020\u0023\u000D\u0018\u000E\u0050\u004B\u004A\u004A\u004B\u0002\u004D\u0050\u0002\u005A\u0002" + (emit__csharp_emitter_emit_act_stmts(stmts, 0L, len, false, arities) + "\u0002\u0015\u000D\u000E\u0019\u0015\u0012\u0002\u0012\u0019\u0017\u0017\u0046\u0002\u005B\u004B\u004B\u004A\u004B"))), ErrorTy { } => (string)(("\u004A\u004A\u0036\u0019\u0012\u0018\u004F\u0010\u0020\u0023\u000D\u0018\u000E\u0050\u004B\u004A\u004A\u004B\u0002\u004D\u0050\u0002\u005A\u0002" + (emit__csharp_emitter_emit_act_stmts(stmts, 0L, len, false, arities) + "\u0002\u0015\u000D\u000E\u0019\u0015\u0012\u0002\u0012\u0019\u0017\u0017\u0046\u0002\u005B\u004B\u004B\u004A\u004B"))), _ => (string)(((len == 0L) ? ("\u004A\u004A\u0036\u0019\u0012\u0018\u004F" + (ret_type + "\u0050\u004B\u004A\u004A\u004B\u0002\u004D\u0050\u0002\u005A\u0002\u0015\u000D\u000E\u0019\u0015\u0012\u0002\u0012\u0019\u0017\u0017\u0046\u0002\u005B\u004B\u004B\u004A\u004B")) : ("\u004A\u004A\u0036\u0019\u0012\u0018\u004F" + (ret_type + ("\u0050\u004B\u004A\u004A\u004B\u0002\u004D\u0050\u0002\u005A\u0002" + (emit__csharp_emitter_emit_act_stmts(stmts, 0L, len, true, arities) + "\u0002\u005B\u004B\u004B\u004A\u004B")))))), }))(((long)stmts.Count))))(cs_type(actual_ty))))(ty switch { EffectfulTy(var effs, var ret) => (CodexType)(ret), _ => (CodexType)(ty), });

    public static string emit__csharp_emitter_emit_act_stmts(List<IRActStmt> stmts, long i, long len, bool needs_return, List<ArityEntry> arities) => ((i == len) ? "" : ((Func<IRActStmt, string>)((s) => ((Func<bool, string>)((is_last) => ((Func<bool, string>)((use_return) => (emit__csharp_emitter_emit_act_stmt(s, use_return, arities) + ("\u0002" + emit__csharp_emitter_emit_act_stmts(stmts, (i + 1L), len, needs_return, arities)))))((is_last ? needs_return : false))))((i == (len - 1L)))))(stmts[(int)i]));

    public static string emit__csharp_emitter_emit_act_stmt(IRActStmt s, bool use_return, List<ArityEntry> arities) => s switch { IrDoBind(var name, var ty, var val, var sp) => (string)(("\u0021\u000F\u0015\u0002" + (sanitize(name) + ("\u0002\u004D\u0002" + (emit__csharp_emitter_emit_expr(val, arities) + "\u0046"))))), IrDoExec(var e, var sp) => (string)((use_return ? ("\u0015\u000D\u000E\u0019\u0015\u0012\u0002" + (emit__csharp_emitter_emit_expr(e, arities) + "\u0046")) : (emit__csharp_emitter_emit_expr(e, arities) + "\u0046"))), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static string emit__csharp_emitter_emit_record(string name, List<IRFieldVal> fields, List<ArityEntry> arities) => ("\u0012\u000D\u001B\u0002" + (sanitize(name) + ("\u004A" + (emit_record_fields(fields, 0L, arities) + "\u004B"))));

    public static string emit_record_fields(List<IRFieldVal> fields, long i, List<ArityEntry> arities) => ((i == ((long)fields.Count)) ? "" : ((Func<IRFieldVal, string>)((f) => (sanitize(f.name) + ("\u0045\u0002" + (emit__csharp_emitter_emit_expr(f.value, arities) + (((i < (((long)fields.Count) - 1L)) ? "\u0042\u0002" : "") + emit_record_fields(fields, (i + 1L), arities)))))))(fields[(int)i]));

    public static string emit__csharp_emitter_emit_handle(string eff, IRExpr body, List<IRHandleClause> clauses, CodexType ty, List<ArityEntry> arities) => ((Func<string, string>)((ret_type) => ("\u004A\u004A\u0036\u0019\u0012\u0018\u004F" + (ret_type + ("\u0050\u004B\u004A\u004A\u004B\u0002\u004D\u0050\u0002\u005A\u0002" + (emit__csharp_emitter_emit_handle_clauses(clauses, ret_type, arities) + ("\u0015\u000D\u000E\u0019\u0015\u0012\u0002" + (emit__csharp_emitter_emit_expr(body, arities) + "\u0046\u0002\u005B\u004B\u004B\u004A\u004B"))))))))(cs_type(ty));

    public static string emit__csharp_emitter_emit_handle_clauses(List<IRHandleClause> clauses, string ret_type, List<ArityEntry> arities) => emit_handle_clauses_loop(clauses, 0L, ret_type, arities);

    public static string emit_handle_clauses_loop(List<IRHandleClause> clauses, long i, string ret_type, List<ArityEntry> arities) => ((i == ((long)clauses.Count)) ? "" : ((Func<IRHandleClause, string>)((c) => ("\u0036\u0019\u0012\u0018\u004F\u0036\u0019\u0012\u0018\u004F" + (ret_type + ("\u0042\u0002" + (ret_type + ("\u0050\u0042\u0002" + (ret_type + ("\u0050\u0002\u0055\u0014\u000F\u0012\u0016\u0017\u000D\u0055" + (sanitize(c.op_name) + ("\u0055\u0002\u004D\u0002\u004A" + (sanitize(c.resume_name) + ("\u004B\u0002\u004D\u0050\u0002\u005A\u0002\u0015\u000D\u000E\u0019\u0015\u0012\u0002" + (emit__csharp_emitter_emit_expr(c.body, arities) + ("\u0046\u0002\u005B\u0046\u0002" + emit_handle_clauses_loop(clauses, (i + 1L), ret_type, arities))))))))))))))))(clauses[(int)i]));

    public static List<long> cdx_magic() => new List<long> { 67L, 68L, 88L, 49L };

    public static long cdx_format_version() => 1L;

    public static long cdx_fixed_header_size() => 224L;

    public static long cdx_content_hash_size() => 32L;

    public static long cdx_author_key_size() => 32L;

    public static long cdx_signature_size() => 64L;

    public static long cdx_flag_bare_metal() => 1L;

    public static long cdx_flag_needs_heap() => 2L;

    public static long cdx_flag_needs_stack_guard() => 4L;

    public static long cdx_flag_has_proofs() => 8L;

    public static List<long> cdx_header_bytes(long flags, long cap_off, long cap_sz, long proof_off, long proof_sz, long text_off, long text_sz, long rodata_off, long rodata_sz, long entry, long stack_sz, long heap_sz) => Enumerable.Concat(cdx_magic(), Enumerable.Concat(write_i16(cdx_format_version()), Enumerable.Concat(write_i16(flags), Enumerable.Concat(pad_zeros(cdx_content_hash_size()), Enumerable.Concat(pad_zeros(cdx_author_key_size()), Enumerable.Concat(pad_zeros(cdx_signature_size()), Enumerable.Concat(write_i64(cap_off), Enumerable.Concat(write_i64(cap_sz), Enumerable.Concat(write_i64(proof_off), Enumerable.Concat(write_i64(proof_sz), Enumerable.Concat(write_i64(text_off), Enumerable.Concat(write_i64(text_sz), Enumerable.Concat(write_i64(rodata_off), Enumerable.Concat(write_i64(rodata_sz), Enumerable.Concat(write_i64(entry), Enumerable.Concat(write_i32(stack_sz), Enumerable.Concat(write_i32(heap_sz), Enumerable.Concat(write_i16(0L), Enumerable.Concat(write_i16(0L), write_i32(0L)).ToList()).ToList()).ToList()).ToList()).ToList()).ToList()).ToList()).ToList()).ToList()).ToList()).ToList()).ToList()).ToList()).ToList()).ToList()).ToList()).ToList()).ToList()).ToList();

    public static List<long> build_cdx(long flags, long entry_offset, long stack_size, long heap_size, List<long> text, List<long> rodata) => ((Func<long, List<long>>)((hdr) => ((Func<long, List<long>>)((text_size) => ((Func<long, List<long>>)((rodata_size) => Enumerable.Concat(cdx_header_bytes(flags, hdr, 0L, hdr, 0L, hdr, text_size, (hdr + text_size), rodata_size, entry_offset, stack_size, heap_size), Enumerable.Concat(text, rodata).ToList()).ToList()))(((long)rodata.Count))))(((long)text.Count))))(cdx_fixed_header_size());

    public static List<long> build_cdx_bare_metal(long entry_offset, long stack_size, long heap_size, List<long> text, List<long> rodata) => build_cdx((cdx_flag_bare_metal() + cdx_flag_needs_heap()), entry_offset, stack_size, heap_size, text, rodata);

    public static string emit__codex_emitter_emit_type_defs(List<ATypeDef> tds, long i) => ((i == ((long)tds.Count)) ? "" : (emit__codex_emitter_emit_type_def(tds[(int)i]) + ("\u0001" + emit__codex_emitter_emit_type_defs(tds, (i + 1L)))));

    public static string emit__codex_emitter_emit_type_def(ATypeDef td) => td switch { ARecordTypeDef(var name, var tparams, var fields, var s) => (string)((name.value + ("\u0002\u004D\u0002\u0015\u000D\u0018\u0010\u0015\u0016\u0002\u005A" + (emit__codex_emitter_emit_record_field_defs(fields, 0L) + "\u0001\u005B\u0001")))), AVariantTypeDef(var name, var tparams, var ctors, var s) => (string)((name.value + ("\u0002\u004D\u0001" + emit__codex_emitter_emit_variant_ctors(ctors, 0L)))), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static string emit__codex_emitter_emit_record_field_defs(List<ARecordFieldDef> fields, long i) => ((i == ((long)fields.Count)) ? "" : ((Func<ARecordFieldDef, string>)((f) => ((Func<string, string>)((comma) => (comma + ("\u0001\u0002" + (f.name.value + ("\u0002\u0045\u0002" + (emit__codex_emitter_emit_type_expr(f.type_expr) + emit__codex_emitter_emit_record_field_defs(fields, (i + 1L)))))))))(((i > 0L) ? "\u0042" : ""))))(fields[(int)i]));

    public static string emit__codex_emitter_emit_variant_ctors(List<AVariantCtorDef> ctors, long i) => ((i == ((long)ctors.Count)) ? "" : ((Func<AVariantCtorDef, string>)((c) => ("\u0002\u0057\u0002" + (c.name.value + (emit__codex_emitter_emit_ctor_fields(c.fields, 0L) + ("\u0001" + emit__codex_emitter_emit_variant_ctors(ctors, (i + 1L))))))))(ctors[(int)i]));

    public static string emit__codex_emitter_emit_ctor_fields(List<ATypeExpr> fields, long i) => ((i == ((long)fields.Count)) ? "" : ("\u0002\u004A" + (emit__codex_emitter_emit_type_expr(fields[(int)i]) + ("\u004B" + emit__codex_emitter_emit_ctor_fields(fields, (i + 1L))))));

    public static string emit__codex_emitter_emit_type_expr(ATypeExpr te) => te switch { ANamedType(var name, var s) => (string)(name.value), AFunType(var p, var r, var s) => (string)((emit_fun_param(p) + ("\u0002\u0049\u0050\u0002" + emit__codex_emitter_emit_type_expr(r)))), AAppType(var @base, var args, var s) => (string)((emit__codex_emitter_emit_type_expr(@base) + ("\u0002" + emit_type_expr_args(args, 0L)))), AEffectType(var effs, var ret, var s) => (string)(("\u0058" + (emit_effect_names(effs, 0L) + ("\u0059\u0002" + emit__codex_emitter_emit_type_expr(ret))))), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static string emit_fun_param(ATypeExpr te) => te switch { AFunType(var p, var r, var s) => (string)(("\u004A" + (emit__codex_emitter_emit_type_expr(te) + "\u004B"))), _ => (string)(emit__codex_emitter_emit_type_expr(te)), };

    public static string emit_type_expr_args(List<ATypeExpr> args, long i) => ((i == ((long)args.Count)) ? "" : ((Func<string, string>)((sep) => (sep + (emit_type_expr_wrapped(args[(int)i]) + emit_type_expr_args(args, (i + 1L))))))(((i > 0L) ? "\u0002" : "")));

    public static string emit_type_expr_wrapped(ATypeExpr te) => te switch { AFunType(var p, var r, var s) => (string)(("\u004A" + (emit__codex_emitter_emit_type_expr(te) + "\u004B"))), AAppType(var @base, var args, var s) => (string)(("\u004A" + (emit__codex_emitter_emit_type_expr(te) + "\u004B"))), _ => (string)(emit__codex_emitter_emit_type_expr(te)), };

    public static string emit_effect_names(List<Name> effs, long i) => ((i == ((long)effs.Count)) ? "" : ((Func<string, string>)((sep) => (sep + (effs[(int)i].value + emit_effect_names(effs, (i + 1L))))))(((i > 0L) ? "\u0042\u0002" : "")));

    public static TypeVarMap empty_tvar_map() => new TypeVarMap(entries: new List<long>(), next_id: 0L);

    public static long tvar_map_lookup(List<long> entries, long id, long i)
    {
        while (true)
        {
            if ((i >= ((long)entries.Count)))
            {
            return (-1L);
            }
            else
            {
            if ((entries[(int)i] == id))
            {
            return i;
            }
            else
            {
            var _tco_0 = entries;
            var _tco_1 = id;
            var _tco_2 = (i + 1L);
            entries = _tco_0;
            id = _tco_1;
            i = _tco_2;
            continue;
            }
            }
        }
    }

    public static TypeVarMap tvar_map_insert(TypeVarMap m, long id) => ((tvar_map_lookup(m.entries, id, 0L) >= 0L) ? m : new TypeVarMap(entries: ((Func<List<long>>)(() => { var _l = m.entries; _l.Add(id); return _l; }))(), next_id: (m.next_id + 1L)));

    public static TypeVarMap collect_tvars(CodexType ty, TypeVarMap m)
    {
        while (true)
        {
            var _tco_s = ty;
            if (_tco_s is TypeVar _tco_m0)
            {
                var id = _tco_m0.Field0;
            return tvar_map_insert(m, id);
            }
            else if (_tco_s is ForAllTy _tco_m1)
            {
                var id = _tco_m1.Field0;
                var body = _tco_m1.Field1;
            var _tco_0 = body;
            var _tco_1 = tvar_map_insert(m, id);
            ty = _tco_0;
            m = _tco_1;
            continue;
            }
            else if (_tco_s is FunTy _tco_m2)
            {
                var p = _tco_m2.Field0;
                var r = _tco_m2.Field1;
            var _tco_0 = r;
            var _tco_1 = collect_tvars(p, m);
            ty = _tco_0;
            m = _tco_1;
            continue;
            }
            else if (_tco_s is ListTy _tco_m3)
            {
                var elem = _tco_m3.Field0;
            var _tco_0 = elem;
            var _tco_1 = m;
            ty = _tco_0;
            m = _tco_1;
            continue;
            }
            else if (_tco_s is LinkedListTy _tco_m4)
            {
                var elem = _tco_m4.Field0;
            var _tco_0 = elem;
            var _tco_1 = m;
            ty = _tco_0;
            m = _tco_1;
            continue;
            }
            else if (_tco_s is EffectfulTy _tco_m5)
            {
                var effs = _tco_m5.Field0;
                var ret = _tco_m5.Field1;
            var _tco_0 = ret;
            var _tco_1 = m;
            ty = _tco_0;
            m = _tco_1;
            continue;
            }
            {
            return m;
            }
        }
    }

    public static CodexType normalize_type(CodexType ty, TypeVarMap m) => ty switch { TypeVar(var id) => (CodexType)(new TypeVar(tvar_map_lookup(m.entries, id, 0L))), ForAllTy(var id, var body) => (CodexType)(new ForAllTy(tvar_map_lookup(m.entries, id, 0L), normalize_type(body, m))), FunTy(var p, var r) => (CodexType)(new FunTy(normalize_type(p, m), normalize_type(r, m))), ListTy(var elem) => (CodexType)(new ListTy(normalize_type(elem, m))), LinkedListTy(var elem) => (CodexType)(new LinkedListTy(normalize_type(elem, m))), EffectfulTy(var effs, var ret) => (CodexType)(new EffectfulTy(effs, normalize_type(ret, m))), _ => (CodexType)(ty), };

    public static CodexType normalize_type_vars(CodexType ty) => ((Func<TypeVarMap, CodexType>)((m) => ((m.next_id == 0L) ? ty : normalize_type(ty, m))))(collect_tvars(ty, empty_tvar_map()));

    public static string emit_type(CodexType ty)
    {
        while (true)
        {
            var _tco_s = ty;
            if (_tco_s is IntegerTy _tco_m0)
            {
            return "\u002B\u0012\u000E\u000D\u001D\u000D\u0015";
            }
            else if (_tco_s is NumberTy _tco_m1)
            {
            return "\u002C\u0019\u001A\u0020\u000D\u0015";
            }
            else if (_tco_s is TextTy _tco_m2)
            {
            return "\u0028\u000D\u0024\u000E";
            }
            else if (_tco_s is BooleanTy _tco_m3)
            {
            return "\u003A\u0010\u0010\u0017\u000D\u000F\u0012";
            }
            else if (_tco_s is CharTy _tco_m4)
            {
            return "\u0032\u0014\u000F\u0015";
            }
            else if (_tco_s is VoidTy _tco_m5)
            {
            return "\u002C\u0010\u000E\u0014\u0011\u0012\u001D";
            }
            else if (_tco_s is NothingTy _tco_m6)
            {
            return "\u002C\u0010\u000E\u0014\u0011\u0012\u001D";
            }
            else if (_tco_s is ErrorTy _tco_m7)
            {
            return "\u0033\u0012\u0022\u0012\u0010\u001B\u0012";
            }
            else if (_tco_s is FunTy _tco_m8)
            {
                var p = _tco_m8.Field0;
                var r = _tco_m8.Field1;
            return (wrap_fun_param(p) + ("\u0002\u0049\u0050\u0002" + emit_type(r)));
            }
            else if (_tco_s is ListTy _tco_m9)
            {
                var elem = _tco_m9.Field0;
            return ("\u0031\u0011\u0013\u000E\u0002" + wrap_complex(elem));
            }
            else if (_tco_s is LinkedListTy _tco_m10)
            {
                var elem = _tco_m10.Field0;
            return ("\u0031\u0011\u0012\u0022\u000D\u0016\u0031\u0011\u0013\u000E\u0002" + wrap_complex(elem));
            }
            else if (_tco_s is TypeVar _tco_m11)
            {
                var id = _tco_m11.Field0;
            return ("\u000F" + _Cce.FromUnicode(id.ToString()));
            }
            else if (_tco_s is ForAllTy _tco_m12)
            {
                var id = _tco_m12.Field0;
                var body = _tco_m12.Field1;
            var _tco_0 = body;
            ty = _tco_0;
            continue;
            }
            else if (_tco_s is SumTy _tco_m13)
            {
                var name = _tco_m13.Field0;
                var ctors = _tco_m13.Field1;
            return name.value;
            }
            else if (_tco_s is RecordTy _tco_m14)
            {
                var name = _tco_m14.Field0;
                var fields = _tco_m14.Field1;
            return name.value;
            }
            else if (_tco_s is ConstructedTy _tco_m15)
            {
                var name = _tco_m15.Field0;
                var args = _tco_m15.Field1;
            return name.value;
            }
            else if (_tco_s is EffectfulTy _tco_m16)
            {
                var effs = _tco_m16.Field0;
                var ret = _tco_m16.Field1;
            return ("\u0058" + (emit_type_effect_names(effs, 0L) + ("\u0059\u0002" + emit_type(ret))));
            }
        }
    }

    public static string wrap_fun_param(CodexType ty) => ty switch { FunTy(var p, var r) => (string)(("\u004A" + (emit_type(ty) + "\u004B"))), _ => (string)(emit_type(ty)), };

    public static string wrap_complex(CodexType ty) => ty switch { FunTy(var p, var r) => (string)(("\u004A" + (emit_type(ty) + "\u004B"))), ListTy(var elem) => (string)(("\u004A" + (emit_type(ty) + "\u004B"))), LinkedListTy(var elem) => (string)(("\u004A" + (emit_type(ty) + "\u004B"))), _ => (string)(emit_type(ty)), };

    public static string emit_type_effect_names(List<Name> effs, long i) => ((i == ((long)effs.Count)) ? "" : ((Func<string, string>)((sep) => (sep + (effs[(int)i].value + emit_type_effect_names(effs, (i + 1L))))))(((i > 0L) ? "\u0042\u0002" : "")));

    public static string emit__codex_emitter_emit_def(IRDef d, List<string> ctor_names) => (skip_def(d, ctor_names) ? "" : ((Func<CodexType, string>)((norm_ty) => ((Func<string, string>)((sig) => (sig + ("\u0002\u004D\u0001\u0002" + (emit__codex_emitter_emit_expr(d.body, ctor_names, 1L) + "\u0001")))))((d.name + ("\u0002\u0045\u0002" + (emit_type(norm_ty) + ("\u0001" + (d.name + emit__codex_emitter_emit_def_params(d.@params, 0L)))))))))(normalize_type_vars(d.type_val)));

    public static bool is_match_body(IRExpr e) => e switch { IrMatch(var scrut, var branches, var ty, var sp) => (bool)(true), _ => (bool)(false), };

    public static bool skip_def(IRDef d, List<string> ctor_names) => (ctor_names.Contains(d.name) ? true : ((((long)d.name.Length) == 0L) ? true : ((Func<long, bool>)((first) => ((first < 13L) ? true : ((first > 38L) ? true : false))))(((long)d.name[(int)0L]))));

    public static bool is_upper(long c) => ((Func<long, bool>)((code) => ((Func<long, bool>)((code_z) => ((c >= code) && (c <= code_z))))(((long)"\u0040"[(int)0L]))))(((long)"\u0029"[(int)0L]));

    public static bool is_error_body(IRExpr e) => e switch { IrError(var msg, var ty, var sp) => (bool)(true), _ => (bool)(false), };

    public static string error_message(IRExpr e) => e switch { IrError(var msg, var ty, var sp) => (string)(msg), _ => (string)(""), };

    public static bool is_lower_start(string s) => ((((long)s.Length) == 0L) ? false : ((Func<long, bool>)((c) => ((Func<long, bool>)((code_a) => ((Func<long, bool>)((code_z) => ((c >= code_a) && (c <= code_z))))(((long)"\u0026"[(int)0L]))))(((long)"\u000F"[(int)0L]))))(((long)s[(int)0L])));

    public static string emit__codex_emitter_emit_def_params(List<IRParam> @params, long i) => ((i == ((long)@params.Count)) ? "" : ((Func<IRParam, string>)((p) => ("\u0002\u004A" + (p.name + ("\u004B" + emit__codex_emitter_emit_def_params(@params, (i + 1L)))))))(@params[(int)i]));

    public static string emit__codex_emitter_emit_expr(IRExpr e, List<string> ctors, long indent) => e switch { IrIntLit(var n, var sp) => (string)(_Cce.FromUnicode(n.ToString())), IrNumLit(var n, var sp) => (string)(_Cce.FromUnicode(n.ToString())), IrTextLit(var s, var sp) => (string)(("\u0048" + (emit__codex_emitter_escape_text(s) + "\u0048"))), IrBoolLit(var b, var sp) => (string)((b ? "\u0028\u0015\u0019\u000D" : "\u0036\u000F\u0017\u0013\u000D")), IrCharLit(var n, var sp) => (string)(("\u0047" + (escape_char(n) + "\u0047"))), IrName(var n, var ty, var sp) => (string)(n), IrBinary(var op, var l, var r, var ty, var sp) => (string)(emit__codex_emitter_emit_binary(op, l, r, ctors, indent)), IrNegate(var operand, var sp) => (string)(("\u0049" + emit__codex_emitter_emit_expr(operand, ctors, indent))), IrIf(var c, var t, var el, var ty, var sp) => (string)(emit__codex_emitter_emit_if(c, t, el, ctors, indent, 0L)), IrLet(var name, var ty, var val, var body, var sp) => (string)(emit__codex_emitter_emit_let(name, val, body, ctors, indent)), IrApply(var f, var a, var ty, var sp) => (string)(emit__codex_emitter_emit_apply(e, ctors, indent)), IrLambda(var @params, var body, var ty, var sp) => (string)(emit__codex_emitter_emit_lambda(@params, body, ctors, indent)), IrList(var elems, var ty, var sp) => (string)(emit__codex_emitter_emit_list(elems, ctors, indent)), IrMatch(var scrut, var branches, var ty, var sp) => (string)(emit__codex_emitter_emit_match(scrut, branches, ctors, indent)), IrAct(var stmts, var ty, var sp) => (string)(emit__codex_emitter_emit_act(stmts, ctors, indent)), IrRecord(var name, var fields, var ty, var sp) => (string)(emit__codex_emitter_emit_record(name, fields, ctors, indent)), IrFieldAccess(var rec, var field, var ty, var sp) => (string)(emit__codex_emitter_emit_field_access(rec, field, ctors, indent)), IrFork(var body, var ty, var sp) => (string)(("\u001C\u0010\u0015\u0022\u0002\u004A" + (emit__codex_emitter_emit_expr(body, ctors, indent) + "\u004B"))), IrAwait(var task, var ty, var sp) => (string)(("\u000F\u001B\u000F\u0011\u000E\u0002\u004A" + (emit__codex_emitter_emit_expr(task, ctors, indent) + "\u004B"))), IrHandle(var eff, var body, var clauses, var ty, var sp) => (string)(emit__codex_emitter_emit_handle(eff, body, clauses, ctors, indent)), IrError(var msg, var ty, var sp) => (string)("\u0003"), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static string emit__codex_emitter_emit_binary(IRBinaryOp op, IRExpr l, IRExpr r, List<string> ctors, long indent) => (wrap_binary_left(op, l, ctors, indent) + ("\u0002" + (bin_op_text(op) + ("\u0002" + wrap_binary_right(op, r, ctors, indent)))));

    public static string wrap_binary_left(IRBinaryOp parent_op, IRExpr e, List<string> ctors, long indent) => e switch { IrBinary(var child_op, var l2, var r2, var ty, var sp) => (string)(((bin_op_prec(child_op) < bin_op_prec(parent_op)) ? ("\u004A" + (emit__codex_emitter_emit_expr(e, ctors, indent) + "\u004B")) : emit__codex_emitter_emit_expr(e, ctors, indent))), IrIf(var c, var t, var el, var ty, var sp) => (string)(("\u004A" + (emit__codex_emitter_emit_expr(e, ctors, indent) + "\u004B"))), _ => (string)(emit__codex_emitter_emit_expr(e, ctors, indent)), };

    public static string wrap_binary_right(IRBinaryOp parent_op, IRExpr e, List<string> ctors, long indent) => e switch { IrBinary(var child_op, var l2, var r2, var ty, var sp) => (string)((needs_right_wrap(parent_op, child_op) ? ("\u004A" + (emit__codex_emitter_emit_expr(e, ctors, indent) + "\u004B")) : emit__codex_emitter_emit_expr(e, ctors, indent))), IrIf(var c, var t, var el, var ty, var sp) => (string)(("\u004A" + (emit__codex_emitter_emit_expr(e, ctors, indent) + "\u004B"))), _ => (string)(emit__codex_emitter_emit_expr(e, ctors, indent)), };

    public static bool needs_right_wrap(IRBinaryOp parent, IRBinaryOp child) => ((bin_op_prec(child) < bin_op_prec(parent)) ? true : ((bin_op_prec(child) > bin_op_prec(parent)) ? false : ((bin_op_text(parent) == bin_op_text(child)) ? (is_associative_op(parent) ? false : true) : true)));

    public static bool is_associative_op(IRBinaryOp op) => op switch { IrAddInt { } => (bool)(true), IrAddNum { } => (bool)(true), IrMulInt { } => (bool)(true), IrMulNum { } => (bool)(true), IrAppendText { } => (bool)(true), IrAppendList { } => (bool)(true), IrConsList { } => (bool)(true), IrAnd { } => (bool)(true), IrOr { } => (bool)(true), _ => (bool)(false), };

    public static long bin_op_prec(IRBinaryOp op) => op switch { IrOr { } => (long)(2L), IrAnd { } => (long)(3L), IrEq { } => (long)(4L), IrNotEq { } => (long)(4L), IrLt { } => (long)(4L), IrGt { } => (long)(4L), IrLtEq { } => (long)(4L), IrGtEq { } => (long)(4L), IrAppendText { } => (long)(5L), IrAppendList { } => (long)(5L), IrConsList { } => (long)(5L), IrAddInt { } => (long)(6L), IrSubInt { } => (long)(6L), IrAddNum { } => (long)(6L), IrSubNum { } => (long)(6L), IrMulInt { } => (long)(7L), IrDivInt { } => (long)(7L), IrMulNum { } => (long)(7L), IrDivNum { } => (long)(7L), IrPowInt { } => (long)(8L), _ => (long)(0L), };

    public static string bin_op_text(IRBinaryOp op) => op switch { IrAddInt { } => (string)("\u004C"), IrSubInt { } => (string)("\u0049"), IrMulInt { } => (string)("\u004E"), IrDivInt { } => (string)("\u0051"), IrPowInt { } => (string)("\u005E"), IrAddNum { } => (string)("\u004C"), IrSubNum { } => (string)("\u0049"), IrMulNum { } => (string)("\u004E"), IrDivNum { } => (string)("\u0051"), IrEq { } => (string)("\u004D\u004D"), IrNotEq { } => (string)("\u0051\u004D"), IrLt { } => (string)("\u004F"), IrGt { } => (string)("\u0050"), IrLtEq { } => (string)("\u004F\u004D"), IrGtEq { } => (string)("\u0050\u004D"), IrAnd { } => (string)("\u0054"), IrOr { } => (string)("\u0057"), IrAppendText { } => (string)("\u004C\u004C"), IrAppendList { } => (string)("\u004C\u004C"), IrConsList { } => (string)("\u0045\u0045"), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static string emit__codex_emitter_emit_if(IRExpr c, IRExpr t, IRExpr el, List<string> ctors, long indent, long else_offset) => ("\u0011\u001C\u0002" + (emit__codex_emitter_emit_expr(c, ctors, indent) + ("\u0002\u000E\u0014\u000D\u0012\u0002" + (emit__codex_emitter_emit_expr(t, ctors, indent) + ("\u0001" + (make_indent(indent) + (pad_spaces(else_offset) + ("\u000D\u0017\u0013\u000D\u0002" + emit_else(el, ctors, indent, else_offset)))))))));

    public static string emit_else(IRExpr el, List<string> ctors, long indent, long else_offset) => el switch { IrIf(var c2, var t2, var el2, var ty, var sp) => (string)(emit__codex_emitter_emit_if(c2, t2, el2, ctors, indent, else_offset)), _ => (string)(emit__codex_emitter_emit_expr(el, ctors, indent)), };

    public static string pad_spaces(long n) => ((n <= 0L) ? "" : ("\u0002" + pad_spaces((n - 1L))));

    public static string emit__codex_emitter_emit_let(string name, IRExpr val, IRExpr body, List<string> ctors, long indent) => ("\u0017\u000D\u000E\u0002" + (name + ("\u0002\u004D\u0002" + (emit__codex_emitter_emit_expr(val, ctors, (indent + 1L)) + ("\u0001" + (make_indent(indent) + ("\u0011\u0012\u0002" + emit_let_body(body, ctors, indent))))))));

    public static string emit_let_body(IRExpr body, List<string> ctors, long indent) => body switch { IrIf(var c, var t, var el, var ty, var sp) => (string)(emit__codex_emitter_emit_if(c, t, el, ctors, indent, 3L)), _ => (string)(emit__codex_emitter_emit_expr(body, ctors, indent)), };

    public static string emit__codex_emitter_emit_apply(IRExpr e, List<string> ctors, long indent) => ((Func<ApplyChain, string>)((chain) => ((Func<IRExpr, string>)((func) => ((Func<List<IRExpr>, string>)((args) => (emit__codex_emitter_emit_expr(func, ctors, indent) + emit__codex_emitter_emit_apply_args(args, ctors, indent, 0L, ((long)args.Count), is_ctor_name(func, ctors)))))(chain.args)))(chain.root)))(collect_apply_chain(e, new List<IRExpr>()));

    public static string emit__codex_emitter_emit_apply_args(List<IRExpr> args, List<string> ctors, long indent, long i, long len, bool is_ctor) => ((i == len) ? "" : ((Func<IRExpr, string>)((arg) => ("\u0002" + (wrap_arg(arg, ctors, indent, is_ctor) + emit__codex_emitter_emit_apply_args(args, ctors, indent, (i + 1L), len, is_ctor)))))(args[(int)i]));

    public static string wrap_arg(IRExpr e, List<string> ctors, long indent, bool is_ctor) => (needs_parens(e, is_ctor) ? ("\u004A" + (emit__codex_emitter_emit_expr(e, ctors, indent) + "\u004B")) : emit__codex_emitter_emit_expr(e, ctors, indent));

    public static bool is_ctor_name(IRExpr e, List<string> ctors) => e switch { IrName(var n, var ty, var sp) => (bool)(ctors.Contains(n)), _ => (bool)(false), };

    public static bool needs_parens(IRExpr e, bool is_ctor) => e switch { IrApply(var f, var a, var ty, var sp) => (bool)(true), IrBinary(var op, var l, var r, var ty, var sp) => (bool)(true), IrIf(var c, var t, var el, var ty, var sp) => (bool)(true), IrLet(var name, var ty, var val, var body, var sp) => (bool)(true), IrMatch(var scrut, var branches, var ty, var sp) => (bool)(true), IrNegate(var operand, var sp) => (bool)(true), IrLambda(var @params, var body, var ty, var sp) => (bool)(true), IrFieldAccess(var rec, var field, var ty, var sp) => (bool)(true), IrRecord(var name, var fields, var ty, var sp) => (bool)(true), _ => (bool)(false), };

    public static string emit__codex_emitter_emit_lambda(List<IRParam> @params, IRExpr body, List<string> ctors, long indent) => ("\u0056" + (emit__codex_emitter_emit_lambda_params(@params, 0L) + ("\u0002\u0049\u0050\u0002" + emit__codex_emitter_emit_expr(body, ctors, indent))));

    public static string emit__codex_emitter_emit_lambda_params(List<IRParam> @params, long i) => ((i == ((long)@params.Count)) ? "" : ((Func<IRParam, string>)((p) => ((Func<string, string>)((sep) => (sep + (p.name + emit__codex_emitter_emit_lambda_params(@params, (i + 1L))))))(((i > 0L) ? "\u0002" : ""))))(@params[(int)i]));

    public static string emit__codex_emitter_emit_list(List<IRExpr> elems, List<string> ctors, long indent) => ("\u0058" + (emit__codex_emitter_emit_list_elems(elems, ctors, indent, 0L) + "\u0059"));

    public static string emit__codex_emitter_emit_list_elems(List<IRExpr> elems, List<string> ctors, long indent, long i) => ((i == ((long)elems.Count)) ? "" : ((Func<string, string>)((sep) => (sep + (emit__codex_emitter_emit_expr(elems[(int)i], ctors, indent) + emit__codex_emitter_emit_list_elems(elems, ctors, indent, (i + 1L))))))(((i > 0L) ? "\u0042\u0002" : "")));

    public static string emit__codex_emitter_emit_match(IRExpr scrut, List<IRBranch> branches, List<string> ctors, long indent) => ("\u001B\u0014\u000D\u0012\u0002" + (emit__codex_emitter_emit_expr(scrut, ctors, indent) + emit_branches(branches, ctors, indent, 0L)));

    public static string emit_branches(List<IRBranch> branches, List<string> ctors, long indent, long i) => ((i == ((long)branches.Count)) ? "" : ((Func<IRBranch, string>)((b) => ("\u0001" + (make_indent((indent + 1L)) + ("\u0011\u0013\u0002" + (emit__codex_emitter_emit_pattern(b.pattern) + ("\u0002\u0049\u0050\u0002" + (emit__codex_emitter_emit_expr(b.body, ctors, (indent + 1L)) + emit_branches(branches, ctors, indent, (i + 1L))))))))))(branches[(int)i]));

    public static string emit__codex_emitter_emit_pattern(IRPat p) => p switch { IrVarPat(var name, var ty, var sp) => (string)(name), IrLitPat(var text, var ty, var sp) => (string)(text), IrCtorPat(var name, var subs, var ty, var sp) => (string)((name + emit__codex_emitter_emit_sub_patterns(subs, 0L))), IrWildPat(var sp) => (string)("\u0010\u000E\u0014\u000D\u0015\u001B\u0011\u0013\u000D"), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static string emit__codex_emitter_emit_sub_patterns(List<IRPat> subs, long i) => ((i == ((long)subs.Count)) ? "" : ("\u0002\u004A" + (emit__codex_emitter_emit_pattern(subs[(int)i]) + ("\u004B" + emit__codex_emitter_emit_sub_patterns(subs, (i + 1L))))));

    public static string emit__codex_emitter_emit_act(List<IRActStmt> stmts, List<string> ctors, long indent) => ("\u000F\u0018\u000E" + (emit__codex_emitter_emit_act_stmts(stmts, ctors, indent, 0L) + ("\u0001" + (make_indent(indent) + "\u000D\u0012\u0016"))));

    public static string emit__codex_emitter_emit_act_stmts(List<IRActStmt> stmts, List<string> ctors, long indent, long i) => ((i == ((long)stmts.Count)) ? "" : ((Func<IRActStmt, string>)((s) => ("\u0001" + (make_indent((indent + 1L)) + (emit__codex_emitter_emit_act_stmt(s, ctors, (indent + 1L)) + emit__codex_emitter_emit_act_stmts(stmts, ctors, indent, (i + 1L)))))))(stmts[(int)i]));

    public static string emit__codex_emitter_emit_act_stmt(IRActStmt s, List<string> ctors, long indent) => s switch { IrDoBind(var name, var ty, var val, var sp) => (string)((name + ("\u0002\u004F\u0049\u0002" + emit__codex_emitter_emit_expr(val, ctors, indent)))), IrDoExec(var e, var sp) => (string)(emit__codex_emitter_emit_expr(e, ctors, indent)), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static string emit__codex_emitter_emit_record(string name, List<IRFieldVal> fields, List<string> ctors, long indent) => ((((long)fields.Count) <= 1L) ? (name + ("\u0002\u005A" + (emit_record_fields_inline(fields, ctors, indent, 0L) + "\u0002\u005B"))) : (name + ("\u0002\u005A\u0001" + (emit_record_fields_multi(fields, ctors, indent, 0L) + (make_indent(indent) + "\u005B")))));

    public static string emit_record_fields_inline(List<IRFieldVal> fields, List<string> ctors, long indent, long i) => ((i == ((long)fields.Count)) ? "" : ((Func<IRFieldVal, string>)((f) => ((Func<string, string>)((sep) => (sep + ("\u0002" + (f.name + ("\u0002\u004D\u0002" + (emit__codex_emitter_emit_expr(f.value, ctors, indent) + emit_record_fields_inline(fields, ctors, indent, (i + 1L)))))))))(((i > 0L) ? "\u0042" : ""))))(fields[(int)i]));

    public static string emit_record_fields_multi(List<IRFieldVal> fields, List<string> ctors, long indent, long i) => ((i == ((long)fields.Count)) ? "" : ((Func<IRFieldVal, string>)((f) => ((Func<string, string>)((comma) => (make_indent((indent + 1L)) + (f.name + ("\u0002\u004D\u0002" + (emit__codex_emitter_emit_expr(f.value, ctors, (indent + 1L)) + (comma + ("\u0001" + emit_record_fields_multi(fields, ctors, indent, (i + 1L))))))))))(((i < (((long)fields.Count) - 1L)) ? "\u0042" : ""))))(fields[(int)i]));

    public static string emit__codex_emitter_emit_field_access(IRExpr rec, string field, List<string> ctors, long indent) => rec switch { IrName(var n, var ty, var sp) => (string)((n + ("\u0041" + field))), IrFieldAccess(var r, var f, var ty, var sp) => (string)((emit__codex_emitter_emit_field_access(r, f, ctors, indent) + ("\u0041" + field))), _ => (string)(("\u004A" + (emit__codex_emitter_emit_expr(rec, ctors, indent) + ("\u004B\u0041" + field)))), };

    public static string emit__codex_emitter_emit_handle(string eff, IRExpr body, List<IRHandleClause> clauses, List<string> ctors, long indent) => ("\u0014\u000F\u0012\u0016\u0017\u000D\u0002" + (emit__codex_emitter_emit_expr(body, ctors, indent) + ("\u0002\u001B\u0011\u000E\u0014" + emit__codex_emitter_emit_handle_clauses(clauses, ctors, indent, 0L))));

    public static string emit__codex_emitter_emit_handle_clauses(List<IRHandleClause> clauses, List<string> ctors, long indent, long i) => ((i == ((long)clauses.Count)) ? "" : ((Func<IRHandleClause, string>)((c) => ("\u0001" + (make_indent((indent + 1L)) + (c.op_name + ("\u0002\u004A" + (c.resume_name + ("\u004B\u0002\u0049\u0050\u0002" + (emit__codex_emitter_emit_expr(c.body, ctors, (indent + 1L)) + emit__codex_emitter_emit_handle_clauses(clauses, ctors, indent, (i + 1L)))))))))))(clauses[(int)i]));

    public static List<string> emit__codex_emitter_collect_ctor_names(List<ATypeDef> type_defs, long i) => ((i == ((long)type_defs.Count)) ? new List<string>() : ((Func<ATypeDef, List<string>>)((td) => ((Func<List<string>, List<string>>)((names) => Enumerable.Concat(names, emit__codex_emitter_collect_ctor_names(type_defs, (i + 1L))).ToList()))(td switch { AVariantTypeDef(var name, var tparams, var ctors, var s) => (List<string>)(collect_variant_ctor_names(ctors, 0L, new List<string>())), ARecordTypeDef(var name, var tparams, var fields, var s) => (List<string>)(new List<string> { name.value }), _ => throw new InvalidOperationException("Non-exhaustive match"), })))(type_defs[(int)i]));

    public static List<string> collect_variant_ctor_names(List<AVariantCtorDef> ctors, long i, List<string> acc)
    {
        while (true)
        {
            if ((i == ((long)ctors.Count)))
            {
            return acc;
            }
            else
            {
            var c = ctors[(int)i];
            var _tco_0 = ctors;
            var _tco_1 = (i + 1L);
            var _tco_2 = ((Func<List<string>>)(() => { var _l = acc; _l.Add(c.name.value); return _l; }))();
            ctors = _tco_0;
            i = _tco_1;
            acc = _tco_2;
            continue;
            }
        }
    }

    public static bool is_simple(IRExpr e) => e switch { IrIntLit(var n, var sp) => (bool)(true), IrNumLit(var n, var sp) => (bool)(true), IrTextLit(var s, var sp) => (bool)(true), IrBoolLit(var b, var sp) => (bool)(true), IrCharLit(var n, var sp) => (bool)(true), IrName(var n, var ty, var sp) => (bool)(true), IrFieldAccess(var r, var f, var ty, var sp) => (bool)(true), _ => (bool)(false), };

    public static string make_indent(long n) => ((n == 0L) ? "" : ("\u0002" + make_indent((n - 1L))));

    public static string emit__codex_emitter_escape_text(string s) => string.Concat(escape_text_collect(s, 0L, ((long)s.Length), new List<string>()));

    public static List<string> escape_text_collect(string s, long i, long len, List<string> acc)
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
            var _tco_1 = (i + 1L);
            var _tco_2 = len;
            var _tco_3 = ((Func<List<string>>)(() => { var _l = acc; _l.Add(escape_one_char(c)); return _l; }))();
            s = _tco_0;
            i = _tco_1;
            len = _tco_2;
            acc = _tco_3;
            continue;
            }
        }
    }

    public static string escape_one_char(long c) => ((c == ((long)"\u0056"[(int)0L])) ? "\u0056\u0056" : ((c == ((long)"\u0048"[(int)0L])) ? "\u0056\u0048" : ((c == ((long)"\u0001"[(int)0L])) ? "\u0056\u0012" : ((char)c).ToString())));

    public static string escape_char(long c) => ((c == ((long)"\u0001"[(int)0L])) ? "\u0056\u0012" : ((c == ((long)"\u0056"[(int)0L])) ? "\u0056\u0056" : ((c == ((long)"\u0047"[(int)0L])) ? "\u0056\u0047" : ((char)c).ToString())));

    public static string emit__codex_emitter_emit_full_chapter(IRChapter m, List<ATypeDef> type_defs) => ((Func<List<string>, string>)((ctor_names) => ((Func<string, string>)((header) => (header + (emit__codex_emitter_emit_type_defs(type_defs, 0L) + emit__codex_emitter_emit_all_defs(m.defs, ctor_names, 0L)))))(((m.chapter_title == "") ? "" : ("\u0032\u0014\u000F\u001F\u000E\u000D\u0015\u0045\u0002" + (m.chapter_title + ("\u0001\u0001" + ((m.prose == "") ? "" : ("\u0002" + (m.prose + "\u0001\u0001"))))))))))(emit__codex_emitter_collect_ctor_names(type_defs, 0L));

    public static string emit__codex_emitter_emit_all_defs(List<IRDef> defs, List<string> ctor_names, long i) => ((i == ((long)defs.Count)) ? "" : (emit__codex_emitter_emit_def(defs[(int)i], ctor_names) + ("\u0001" + emit__codex_emitter_emit_all_defs(defs, ctor_names, (i + 1L)))));

    public static string emit_def_list(List<IRDef> defs, List<string> ctor_names, long i) => ((i == ((long)defs.Count)) ? "" : (emit__codex_emitter_emit_def(defs[(int)i], ctor_names) + ("\u0001" + emit_def_list(defs, ctor_names, (i + 1L)))));

    public static List<IRDef> filter_defs(List<IRDef> defs, List<string> ctor_names, long i, long len, List<IRDef> acc) => ((i == len) ? acc : ((Func<IRDef, List<IRDef>>)((d) => ((Func<Func<List<IRDef>, List<IRDef>>, List<IRDef>>)((next) => (skip_def(d, ctor_names) ? next(acc) : (has_def_named(acc, d.name, 0L, ((long)acc.Count)) ? ((Func<bool, List<IRDef>>)((dominated) => (dominated ? next(replace_def(acc, d.name, d, 0L, ((long)acc.Count))) : next(acc))))((def_score(d) > def_score_named(acc, d.name, 0L, ((long)acc.Count)))) : next(((Func<List<IRDef>>)(() => { var _l = acc; _l.Add(d); return _l; }))())))))((_p0_) => filter_defs(defs, ctor_names, (i + 1L), len, _p0_))))(defs[(int)i]));

    public static long def_score(IRDef d) => ((((long)d.@params.Count) * 100L) + body_depth(d.body));

    public static long def_score_named(List<IRDef> defs, string name, long i, long len)
    {
        while (true)
        {
            if ((i == len))
            {
            return 0L;
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
            var _tco_2 = (i + 1L);
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

    public static long body_depth(IRExpr e) => e switch { IrName(var n, var ty, var sp) => (long)(1L), IrIntLit(var n, var sp) => (long)(1L), IrTextLit(var s, var sp) => (long)(1L), IrBoolLit(var b, var sp) => (long)(1L), IrFieldAccess(var r, var f, var ty, var sp) => (long)(2L), IrApply(var f, var a, var ty, var sp) => (long)((3L + body_depth(f))), IrLet(var name, var ty, var val, var body, var sp) => (long)((5L + body_depth(body))), IrIf(var c, var t, var el, var ty, var sp) => (long)((5L + body_depth(t))), IrMatch(var s, var bs, var ty, var sp) => (long)(10L), IrLambda(var ps, var b, var ty, var sp) => (long)(5L), IrAct(var stmts, var ty, var sp) => (long)(5L), IrRecord(var n, var fs, var ty, var sp) => (long)(3L), _ => (long)(1L), };

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
            var _tco_2 = (i + 1L);
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
            var _tco_3 = (i + 1L);
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

    public static List<IRDef> list_set(List<IRDef> xs, long idx, IRDef val) => list_set_loop(xs, idx, val, 0L, ((long)xs.Count), new List<IRDef>());

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
            var _tco_3 = (i + 1L);
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
            var _tco_3 = (i + 1L);
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

    public static List<long> elf_magic() => new List<long> { 127L, 69L, 76L, 70L };

    public static long elf_class_32() => 1L;

    public static long elf_class_64() => 2L;

    public static long elf_data_lsb() => 1L;

    public static long elf_version_current() => 1L;

    public static long elf_type_exec() => 2L;

    public static long elf_machine_386() => 3L;

    public static long elf_machine_x86_64() => 62L;

    public static long pt_load() => 1L;

    public static long pt_note() => 4L;

    public static long pf_r() => 4L;

    public static long pf_w() => 2L;

    public static long pf_x() => 1L;

    public static long pf_rwx() => 7L;

    public static long pf_rw() => 6L;

    public static long xen_elfnote_phys32_entry() => 18L;

    public static List<long> xen_name() => new List<long> { 88L, 101L, 110L, 0L };

    public static long elf_bare_metal_load_addr() => 1048576L;

    public static long elf_bare_metal_heap_size() => 1069547520L;

    public static long elf_linux_base_addr() => 4194304L;

    public static long elf_page_size() => 4096L;

    public static long elf32_header_size() => 52L;

    public static long elf32_phdr_size() => 32L;

    public static long elf64_header_size() => 64L;

    public static long elf64_phdr_size() => 56L;

    public static long elf_align(long v, long a) => ((Func<long, long>)((r) => ((r == 0L) ? v : ((v + a) - r))))(int_mod(v, a));

    public static List<long> write_i16(long v) => write_bytes(v, 2L);

    public static List<long> pad_zeros(long n) => pad_zeros_acc(n, new List<long>());

    public static List<long> pad_zeros_acc(long n, List<long> acc)
    {
        while (true)
        {
            if ((n <= 0L))
            {
            return acc;
            }
            else
            {
            var _tco_0 = (n - 1L);
            var _tco_1 = ((Func<List<long>>)(() => { var _l = acc; _l.Add(0L); return _l; }))();
            n = _tco_0;
            acc = _tco_1;
            continue;
            }
        }
    }

    public static List<long> elf_ident_32() => Enumerable.Concat(elf_magic(), Enumerable.Concat(new List<long> { elf_class_32(), elf_data_lsb(), elf_version_current() }, pad_zeros(9L)).ToList()).ToList();

    public static List<long> elf_ident_64() => Enumerable.Concat(elf_magic(), Enumerable.Concat(new List<long> { elf_class_64(), elf_data_lsb(), elf_version_current() }, pad_zeros(9L)).ToList()).ToList();

    public static List<long> elf32_header_bytes(long entry, long phoff, long phnum) => Enumerable.Concat(elf_ident_32(), Enumerable.Concat(write_i16(elf_type_exec()), Enumerable.Concat(write_i16(elf_machine_386()), Enumerable.Concat(write_i32(elf_version_current()), Enumerable.Concat(write_i32(entry), Enumerable.Concat(write_i32(phoff), Enumerable.Concat(write_i32(0L), Enumerable.Concat(write_i32(0L), Enumerable.Concat(write_i16(elf32_header_size()), Enumerable.Concat(write_i16(elf32_phdr_size()), Enumerable.Concat(write_i16(phnum), Enumerable.Concat(write_i16(0L), Enumerable.Concat(write_i16(0L), write_i16(0L)).ToList()).ToList()).ToList()).ToList()).ToList()).ToList()).ToList()).ToList()).ToList()).ToList()).ToList()).ToList()).ToList();

    public static List<long> phdr_32(long ptype, long offset, long vaddr, long paddr, long filesz, long memsz, long flags, long palign) => Enumerable.Concat(write_i32(ptype), Enumerable.Concat(write_i32(offset), Enumerable.Concat(write_i32(vaddr), Enumerable.Concat(write_i32(paddr), Enumerable.Concat(write_i32(filesz), Enumerable.Concat(write_i32(memsz), Enumerable.Concat(write_i32(flags), write_i32(palign)).ToList()).ToList()).ToList()).ToList()).ToList()).ToList()).ToList();

    public static List<long> pvh_note(long entry_addr) => Enumerable.Concat(write_i32(4L), Enumerable.Concat(write_i32(4L), Enumerable.Concat(write_i32(xen_elfnote_phys32_entry()), Enumerable.Concat(xen_name(), write_i32(entry_addr)).ToList()).ToList()).ToList()).ToList();

    public static List<long> build_elf_32_bare(List<long> text, List<long> rodata, long entry_offset) => ((Func<long, List<long>>)((load_addr) => ((Func<long, List<long>>)((headers_end) => ((Func<long, List<long>>)((note_offset) => ((Func<long, List<long>>)((text_start) => ((Func<long, List<long>>)((text_end) => ((Func<long, List<long>>)((rodata_start) => ((Func<long, List<long>>)((file_size) => ((Func<long, List<long>>)((entry) => ((Func<long, List<long>>)((seg_filesz) => ((Func<long, List<long>>)((seg_memsz) => Enumerable.Concat(elf32_header_bytes(entry, elf32_header_size(), 2L), Enumerable.Concat(phdr_32(pt_load(), text_start, load_addr, load_addr, seg_filesz, seg_memsz, pf_rwx(), elf_page_size()), Enumerable.Concat(phdr_32(pt_note(), note_offset, 0L, 0L, 20L, 20L, pf_r(), 4L), Enumerable.Concat(pad_zeros((note_offset - headers_end)), Enumerable.Concat(pvh_note(entry), Enumerable.Concat(pad_zeros(((text_start - note_offset) - 20L)), Enumerable.Concat(text, Enumerable.Concat(pad_zeros((rodata_start - text_end)), rodata).ToList()).ToList()).ToList()).ToList()).ToList()).ToList()).ToList()).ToList()))((seg_filesz + elf_bare_metal_heap_size()))))((file_size - text_start))))((load_addr + entry_offset))))((rodata_start + ((long)rodata.Count)))))(elf_align(text_end, 8L))))((text_start + ((long)text.Count)))))(elf_align((note_offset + 20L), 16L))))(elf_align(headers_end, 4L))))((elf32_header_size() + (elf32_phdr_size() * 2L)))))(elf_bare_metal_load_addr());

    public static List<long> elf64_header_bytes(long entry, long phoff, long phnum) => Enumerable.Concat(elf_ident_64(), Enumerable.Concat(write_i16(elf_type_exec()), Enumerable.Concat(write_i16(elf_machine_x86_64()), Enumerable.Concat(write_i32(elf_version_current()), Enumerable.Concat(write_i64(entry), Enumerable.Concat(write_i64(phoff), Enumerable.Concat(write_i64(0L), Enumerable.Concat(write_i32(0L), Enumerable.Concat(write_i16(elf64_header_size()), Enumerable.Concat(write_i16(elf64_phdr_size()), Enumerable.Concat(write_i16(phnum), Enumerable.Concat(write_i16(0L), Enumerable.Concat(write_i16(0L), write_i16(0L)).ToList()).ToList()).ToList()).ToList()).ToList()).ToList()).ToList()).ToList()).ToList()).ToList()).ToList()).ToList()).ToList();

    public static List<long> phdr_64(long ptype, long flags, long offset, long vaddr, long paddr, long filesz, long memsz, long palign) => Enumerable.Concat(write_i32(ptype), Enumerable.Concat(write_i32(flags), Enumerable.Concat(write_i64(offset), Enumerable.Concat(write_i64(vaddr), Enumerable.Concat(write_i64(paddr), Enumerable.Concat(write_i64(filesz), Enumerable.Concat(write_i64(memsz), write_i64(palign)).ToList()).ToList()).ToList()).ToList()).ToList()).ToList()).ToList();

    public static List<long> build_elf_64_linux(List<long> text, List<long> rodata, long entry_offset) => ((Func<long, List<long>>)((base_addr) => ((Func<long, List<long>>)((headers_size) => ((Func<long, List<long>>)((text_file_offset) => ((Func<long, List<long>>)((text_vaddr) => ((Func<long, List<long>>)((entry_point) => ((Func<long, List<long>>)((text_size) => ((Func<long, List<long>>)((text_region_end) => ((Func<long, List<long>>)((rodata_file_offset) => ((Func<long, List<long>>)((rodata_vaddr) => ((Func<long, List<long>>)((rodata_size) => Enumerable.Concat(elf64_header_bytes(entry_point, elf64_header_size(), 2L), Enumerable.Concat(phdr_64(pt_load(), pf_rwx(), 0L, base_addr, base_addr, text_region_end, text_region_end, elf_page_size()), Enumerable.Concat(phdr_64(pt_load(), pf_rw(), rodata_file_offset, rodata_vaddr, rodata_vaddr, rodata_size, rodata_size, elf_page_size()), Enumerable.Concat(pad_zeros((text_file_offset - headers_size)), Enumerable.Concat(text, Enumerable.Concat(pad_zeros((rodata_file_offset - text_region_end)), rodata).ToList()).ToList()).ToList()).ToList()).ToList()).ToList()))(((long)rodata.Count))))((base_addr + rodata_file_offset))))(elf_align(text_region_end, elf_page_size()))))((text_file_offset + text_size))))(((long)text.Count))))((text_vaddr + entry_offset))))((base_addr + text_file_offset))))(elf_align(headers_size, 16L))))((elf64_header_size() + (elf64_phdr_size() * 2L)))))(elf_linux_base_addr());

    public static long compute_text_file_offset_64() => elf_align((elf64_header_size() + (elf64_phdr_size() * 2L)), 16L);

    public static long compute_rodata_vaddr_64(long text_size) => ((Func<long, long>)((text_file_offset) => ((Func<long, long>)((rodata_file_offset) => (elf_linux_base_addr() + rodata_file_offset)))(elf_align((text_file_offset + text_size), elf_page_size()))))(compute_text_file_offset_64());

    public static long compute_text_vaddr_64() => (elf_linux_base_addr() + compute_text_file_offset_64());

    public static long compute_rodata_vaddr_bare(long text_size) => (elf_bare_metal_load_addr() + elf_align(text_size, 8L));

    public static long compute_text_start_32() => ((Func<long, long>)((headers_end) => ((Func<long, long>)((note_offset) => elf_align((note_offset + 20L), 16L)))(elf_align(headers_end, 4L))))((elf32_header_size() + (elf32_phdr_size() * 2L)));

    public static TcoState default_tco_state() => new TcoState(active: false, in_tail_pos: false, loop_top: 0L, param_locals: new List<long>(), temp_locals: new List<long>(), current_func: "", saved_next_local: 0L, saved_next_temp: 0L);

    public static List<long> temp_regs() => new List<long> { 0L, 1L, 2L, 6L, 7L, 11L };

    public static List<long> local_regs() => new List<long> { 3L, 12L, 13L, 14L };

    public static long spill_base() => 32L;

    public static long bare_metal_load_addr() => 1048576L;

    public static long bare_metal_stack_top() => 1073741824L;

    public static List<long> cce_to_unicode_table() => new List<long> { 0L, 10L, 32L, 48L, 49L, 50L, 51L, 52L, 53L, 54L, 55L, 56L, 57L, 101L, 116L, 97L, 111L, 105L, 110L, 115L, 104L, 114L, 100L, 108L, 99L, 117L, 109L, 119L, 102L, 103L, 121L, 112L, 98L, 118L, 107L, 106L, 120L, 113L, 122L, 69L, 84L, 65L, 79L, 73L, 78L, 83L, 72L, 82L, 68L, 76L, 67L, 85L, 77L, 87L, 70L, 71L, 89L, 80L, 66L, 86L, 75L, 74L, 88L, 81L, 90L, 46L, 44L, 33L, 63L, 58L, 59L, 39L, 34L, 45L, 40L, 41L, 43L, 61L, 42L, 60L, 62L, 47L, 64L, 35L, 38L, 95L, 92L, 124L, 91L, 93L, 123L, 125L, 126L, 96L, 94L, 233L, 232L, 234L, 235L, 225L, 224L, 226L, 228L, 243L, 244L, 246L, 250L, 249L, 251L, 252L, 241L, 231L, 237L, 48L, 62L, 53L, 56L, 61L, 66L, 65L, 64L, 50L, 59L, 58L, 60L, 52L, 63L, 67L };

    public static List<long> unicode_to_cce_table() => new List<long> { 0L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 1L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 2L, 67L, 72L, 83L, 68L, 68L, 84L, 71L, 74L, 75L, 78L, 76L, 66L, 73L, 65L, 81L, 3L, 4L, 5L, 6L, 7L, 8L, 9L, 10L, 11L, 12L, 69L, 70L, 79L, 77L, 80L, 68L, 82L, 41L, 58L, 50L, 48L, 39L, 54L, 55L, 46L, 43L, 61L, 60L, 49L, 52L, 44L, 42L, 57L, 63L, 47L, 45L, 40L, 51L, 59L, 53L, 62L, 56L, 64L, 88L, 86L, 89L, 94L, 85L, 93L, 15L, 32L, 24L, 22L, 13L, 28L, 29L, 20L, 17L, 35L, 34L, 23L, 26L, 18L, 16L, 31L, 37L, 21L, 19L, 14L, 25L, 33L, 27L, 36L, 30L, 38L, 90L, 87L, 91L, 92L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 68L, 100L, 99L, 101L, 68L, 102L, 68L, 68L, 111L, 96L, 95L, 97L, 98L, 68L, 112L, 68L, 68L, 68L, 110L, 68L, 103L, 104L, 68L, 105L, 68L, 68L, 107L, 106L, 108L, 109L, 68L, 68L, 68L };

    public static long result_base_rodata_offset() => 0L;

    public static long fwd_table_rodata_offset() => 8L;

    public static long cce_to_unicode_rodata_offset() => 16L;

    public static long unicode_to_cce_rodata_offset() => 144L;

    public static List<long> init_rodata() => ((Func<List<long>, List<long>>)((zeros_8) => Enumerable.Concat(zeros_8, Enumerable.Concat(zeros_8, Enumerable.Concat(cce_to_unicode_table(), unicode_to_cce_table()).ToList()).ToList()).ToList()))(new List<long> { 0L, 0L, 0L, 0L, 0L, 0L, 0L, 0L });

    public static CodegenState empty_codegen_state() => new CodegenState(text_buf_addr: 0L, text_len: 0L, rodata_buf_addr: 0L, rodata_len: 0L, func_offsets: new List<FuncOffset>(), call_patches: new List<CallPatch>(), func_addr_fixups: new List<FuncAddrFixup>(), rodata_fixups: new List<RodataFixup>(), deferred_patches: new List<PatchEntry>(), locals: new List<LocalBinding>(), next_temp: 0L, next_local: 0L, spill_count: 0L, load_local_toggle: 0L, tco: new TcoState(active: false, in_tail_pos: false, loop_top: 0L, param_locals: new List<long>(), temp_locals: new List<long>(), current_func: "", saved_next_local: 0L, saved_next_temp: 0L), type_defs: new List<TypeBinding>(), stack_overflow_checks: new List<long>(), bag: empty_bag());

    public static CodegenState st_add_error(CodegenState st, long code, string msg, SourceSpan sp) => (st with { bag = bag_add(st.bag, make_error(code, msg, sp)) });

    public static CodegenState st_append_text(CodegenState st, List<long> bytes) => ((Func<long, CodegenState>)((new_len) => (st with { text_len = new_len })))(_Buf.buf_write_bytes(st.text_buf_addr, st.text_len, bytes));

    public static CodegenState record_func_offset(CodegenState st, string name) => (st with { func_offsets = ((Func<List<FuncOffset>>)(() => { var _l = st.func_offsets; _l.Add(new FuncOffset(name: name, offset: st.text_len)); return _l; }))() });

    public static List<LocalBinding> fresh_empty_locals(long x) => new List<LocalBinding>();

    public static CodegenState reset_func_state(CodegenState st, string name) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => (st6 with { tco = new TcoState(active: false, in_tail_pos: false, loop_top: 0L, param_locals: new List<long>(), temp_locals: new List<long>(), current_func: "", saved_next_local: 0L, saved_next_temp: 0L) })))((st5 with { load_local_toggle = 0L }))))((st4 with { spill_count = 0L }))))((st3 with { next_local = 0L }))))((st2 with { next_temp = 0L }))))((st1 with { locals = fresh_empty_locals(0L) }))))((st with { func_offsets = ((Func<List<FuncOffset>>)(() => { var _l = st.func_offsets; _l.Add(new FuncOffset(name: name, offset: st.text_len)); return _l; }))() }));

    public static EmitResult alloc_temp(CodegenState st) => ((Func<long, EmitResult>)((idx) => ((Func<long, EmitResult>)((reg) => new EmitResult(state: (st with { next_temp = (st.next_temp + 1L) }), reg: reg)))(temp_regs()[(int)idx])))(int_mod(st.next_temp, 6L));

    public static EmitResult alloc_local(CodegenState st) => ((st.next_local < 4L) ? ((Func<long, EmitResult>)((reg) => new EmitResult(state: (st with { next_local = (st.next_local + 1L) }), reg: reg)))(local_regs()[(int)st.next_local]) : ((Func<long, EmitResult>)((slot) => ((Func<CodegenState, EmitResult>)((st1) => new EmitResult(state: (st1 with { spill_count = (st.spill_count + 1L) }), reg: slot)))((st with { next_local = (st.next_local + 1L) }))))((spill_base() + st.spill_count)));

    public static CodegenState store_local(CodegenState st, long local, long value_reg) => ((local < spill_base()) ? ((local == value_reg) ? st : st_append_text(st, mov_rr(local, value_reg))) : ((Func<long, CodegenState>)((offset) => st_append_text(st, mov_store(reg_rbp(), value_reg, offset))))((0L - ((((local - spill_base()) + 1L) * 8L) + 32L))));

    public static long load_local_scratch_for(long toggle) => ((Func<long, long>)((idx) => ((idx == 0L) ? reg_r8() : ((idx == 1L) ? reg_r9() : reg_r15()))))(int_mod(toggle, 3L));

    public static EmitResult load_local(CodegenState st, long local) => ((local < spill_base()) ? new EmitResult(state: st, reg: local) : ((Func<long, EmitResult>)((scratch) => ((Func<long, EmitResult>)((offset) => ((Func<CodegenState, EmitResult>)((st1) => new EmitResult(state: (st1 with { load_local_toggle = (st.load_local_toggle + 1L) }), reg: scratch)))(st_append_text(st, mov_load(scratch, reg_rbp(), offset)))))((0L - ((((local - spill_base()) + 1L) * 8L) + 32L)))))(load_local_scratch_for(st.load_local_toggle)));

    public static List<long> patch_i32_at(List<long> bytes, long pos, long value) => ((Func<List<long>, List<long>>)((new_bytes) => patch_4_loop(bytes, pos, new_bytes, 0L, ((long)bytes.Count), new List<long>())))(write_i32(value));

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
            if (((i >= pos) && (i < (pos + 4L))))
            {
            var _tco_0 = bytes;
            var _tco_1 = pos;
            var _tco_2 = new_bytes;
            var _tco_3 = (i + 1L);
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
            var _tco_3 = (i + 1L);
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

    public static CodegenState st_add_deferred_patch(CodegenState st, PatchEntry entry) => (st with { deferred_patches = ((Func<List<PatchEntry>>)(() => { var _l = st.deferred_patches; _l.Add(entry); return _l; }))() });

    public static CodegenState patch_jcc_at(CodegenState st, long jcc_pos, long target_pos) => ((Func<long, CodegenState>)((rel32) => st_add_deferred_patch(st, make_i32_patch((jcc_pos + 2L), rel32))))((target_pos - (jcc_pos + 6L)));

    public static CodegenState patch_jmp_at(CodegenState st, long jmp_pos, long target_pos) => ((Func<long, CodegenState>)((rel32) => st_add_deferred_patch(st, make_i32_patch((jmp_pos + 1L), rel32))))((target_pos - (jmp_pos + 5L)));

    public static CodegenState patch_call_at(CodegenState st, long call_pos, long target_pos) => ((Func<long, CodegenState>)((rel32) => st_add_deferred_patch(st, make_i32_patch((call_pos + 1L), rel32))))((target_pos - (call_pos + 5L)));

    public static PatchEntry make_i32_patch(long pos, long value) => ((Func<List<long>, PatchEntry>)((bs) => new PatchEntry(pos: pos, b0: bs[(int)0L], b1: bs[(int)1L], b2: bs[(int)2L], b3: bs[(int)3L])))(write_i32(value));

    public static List<long> apply_all_patches(List<long> bytes, List<PatchEntry> patches) => apply_patch_walk(bytes, patches, 0L, ((long)bytes.Count), new List<long>());

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
            var b = find_patch_byte(patches, i, 0L, ((long)patches.Count));
            if ((b >= 0L))
            {
            var _tco_0 = bytes;
            var _tco_1 = patches;
            var _tco_2 = (i + 1L);
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
            var _tco_2 = (i + 1L);
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
            return (-1L);
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
            if ((i == (p.pos + 1L)))
            {
            return p.b1;
            }
            else
            {
            if ((i == (p.pos + 2L)))
            {
            return p.b2;
            }
            else
            {
            if ((i == (p.pos + 3L)))
            {
            return p.b3;
            }
            else
            {
            var _tco_0 = patches;
            var _tco_1 = i;
            var _tco_2 = (j + 1L);
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

    public static List<PatchEntry> collect_call_patches(List<CallPatch> patches, OffsetTable offset_map, long i, List<PatchEntry> acc)
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
            var target_offset = offset_table_lookup(offset_map, p.target);
            var rel32 = (target_offset - (p.patch_offset + 5L));
            var _tco_0 = patches;
            var _tco_1 = offset_map;
            var _tco_2 = (i + 1L);
            var _tco_3 = ((Func<List<PatchEntry>>)(() => { var _l = acc; _l.Add(make_i32_patch((p.patch_offset + 1L), rel32)); return _l; }))();
            patches = _tco_0;
            offset_map = _tco_1;
            i = _tco_2;
            acc = _tco_3;
            continue;
            }
        }
    }

    public static CodegenState check_call_patch_targets(CodegenState st, OffsetTable fm, long i)
    {
        while (true)
        {
            var patches = st.call_patches;
            if ((i == ((long)patches.Count)))
            {
            return st;
            }
            else
            {
            var p = patches[(int)i];
            var off = offset_table_lookup(fm, p.target);
            if ((off == (0L - 1L)))
            {
            var _tco_0 = st_add_error(st, cdx_unresolved_func_offset(), ("\u0033\u0012\u0015\u000D\u0013\u0010\u0017\u0021\u000D\u0016\u0002\u0018\u000F\u0017\u0017\u0002\u000E\u0010\u0002\u0047" + (p.target + "\u0047")), synthetic_span());
            var _tco_1 = fm;
            var _tco_2 = (i + 1L);
            st = _tco_0;
            fm = _tco_1;
            i = _tco_2;
            continue;
            }
            else
            {
            var _tco_0 = st;
            var _tco_1 = fm;
            var _tco_2 = (i + 1L);
            st = _tco_0;
            fm = _tco_1;
            i = _tco_2;
            continue;
            }
            }
        }
    }

    public static CodegenState check_func_addr_targets(CodegenState st, OffsetTable fm, long i)
    {
        while (true)
        {
            var fixups = st.func_addr_fixups;
            if ((i == ((long)fixups.Count)))
            {
            return st;
            }
            else
            {
            var f = fixups[(int)i];
            var off = offset_table_lookup(fm, f.target);
            if ((off == (0L - 1L)))
            {
            var _tco_0 = st_add_error(st, cdx_unresolved_func_offset(), ("\u0033\u0012\u0015\u000D\u0013\u0010\u0017\u0021\u000D\u0016\u0002\u001C\u0019\u0012\u0018\u0049\u000F\u0016\u0016\u0015\u0002\u0015\u000D\u001C\u000D\u0015\u000D\u0012\u0018\u000D\u0002\u000E\u0010\u0002\u0047" + (f.target + "\u0047")), synthetic_span());
            var _tco_1 = fm;
            var _tco_2 = (i + 1L);
            st = _tco_0;
            fm = _tco_1;
            i = _tco_2;
            continue;
            }
            else
            {
            var _tco_0 = st;
            var _tco_1 = fm;
            var _tco_2 = (i + 1L);
            st = _tco_0;
            fm = _tco_1;
            i = _tco_2;
            continue;
            }
            }
        }
    }

    public static CodegenState check_runtime_symbol(CodegenState st, OffsetTable fm, string name) => ((Func<long, CodegenState>)((off) => ((off == (0L - 1L)) ? st_add_error(st, cdx_unresolved_func_offset(), ("\u0034\u0011\u0013\u0013\u0011\u0012\u001D\u0002\u0015\u0019\u0012\u000E\u0011\u001A\u000D\u0002\u0013\u001E\u001A\u0020\u0010\u0017\u0002\u0047" + (name + "\u0047")), synthetic_span()) : st)))(offset_table_lookup(fm, name));

    public static long align_16(long n) => ((Func<long, long>)((r) => ((r == 0L) ? n : ((n + 16L) - r))))(int_mod(n, 16L));

    public static List<long> emit_sub_rsp_imm32(long imm) => Enumerable.Concat(new List<long> { 72L, 129L, 236L }, write_i32(imm)).ToList();

    public static CodegenState emit_prologue(CodegenState st) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<long, CodegenState>)((skip_pos) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<long, CodegenState>)((check_pos) => ((Func<CodegenState, CodegenState>)((st10) => (st10 with { stack_overflow_checks = ((Func<List<long>>)(() => { var _l = st10.stack_overflow_checks; _l.Add(check_pos); return _l; }))() })))(st_append_text(st9, jcc(cc_b(), 0L)))))(st9.text_len)))(st_append_text(st8, cmp_rr(reg_rsp(), reg_r10())))))(patch_jcc_at(st7, skip_pos, st7.text_len))))(st_append_text(st6, mov_store(reg_r11(), reg_rsp(), 0L)))))(st_append_text(st5, li(reg_r11(), stack_min_rsp_addr())))))(st_append_text(st4, jcc(cc_ae(), 0L)))))(st4.text_len)))(st_append_text(st3, cmp_rr(reg_rsp(), reg_r11())))))(st_append_text(st2, mov_load(reg_r11(), reg_r11(), 0L)))))(st_append_text(st1, li(reg_r11(), stack_min_rsp_addr())))))(st_append_text(st, Enumerable.Concat(push_r(reg_rbp()), Enumerable.Concat(mov_rr(reg_rbp(), reg_rsp()), Enumerable.Concat(push_r(reg_rbx()), Enumerable.Concat(push_r(reg_r12()), Enumerable.Concat(push_r(reg_r13()), push_r(reg_r14())).ToList()).ToList()).ToList()).ToList()).ToList()));

    public static CodegenState emit_epilogue(CodegenState st) => st_append_text(st, Enumerable.Concat(lea(reg_rsp(), reg_rbp(), (0L - 32L)), Enumerable.Concat(pop_r(reg_r14()), Enumerable.Concat(pop_r(reg_r13()), Enumerable.Concat(pop_r(reg_r12()), Enumerable.Concat(pop_r(reg_rbx()), Enumerable.Concat(pop_r(reg_rbp()), x86_ret()).ToList()).ToList()).ToList()).ToList()).ToList()).ToList());

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
            var st1 = ((i < 6L) ? store_local(loc.state, loc.reg, arg_regs()[(int)i]) : ((Func<long, CodegenState>)((stack_offset) => ((Func<EmitResult, CodegenState>)((tmp) => store_local(st_append_text(tmp.state, mov_load(tmp.reg, reg_rbp(), stack_offset)), loc.reg, tmp.reg)))(alloc_temp(loc.state))))((16L + ((i - 6L) * 8L))));
            var _tco_0 = add_local(st1, p.name, loc.reg);
            var _tco_1 = @params;
            var _tco_2 = (i + 1L);
            st = _tco_0;
            @params = _tco_1;
            i = _tco_2;
            continue;
            }
        }
    }

    public static bool emit__x86_64_code_generator_is_self_call(IRExpr expr, string func_name)
    {
        while (true)
        {
            var _tco_s = expr;
            if (_tco_s is IrApply _tco_m0)
            {
                var f = _tco_m0.Field0;
                var a = _tco_m0.Field1;
                var t = _tco_m0.Field2;
                var sp = _tco_m0.Field3;
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
                var sp = _tco_m1.Field2;
            return (n == func_name);
            }
            {
            return false;
            }
        }
    }

    public static bool emit__x86_64_code_generator_has_tail_call(IRExpr expr, string func_name)
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
                var sp = _tco_m0.Field4;
            return (emit__x86_64_code_generator_has_tail_call(th, func_name) || emit__x86_64_code_generator_has_tail_call(el, func_name));
            }
            else if (_tco_s is IrLet _tco_m1)
            {
                var n = _tco_m1.Field0;
                var t = _tco_m1.Field1;
                var v = _tco_m1.Field2;
                var b = _tco_m1.Field3;
                var sp = _tco_m1.Field4;
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
                var sp = _tco_m2.Field3;
            return emit__x86_64_code_generator_has_tail_call_branches(bs, func_name, 0L);
            }
            else if (_tco_s is IrApply _tco_m3)
            {
                var f = _tco_m3.Field0;
                var a = _tco_m3.Field1;
                var t = _tco_m3.Field2;
                var sp = _tco_m3.Field3;
            return emit__x86_64_code_generator_is_self_call(expr, func_name);
            }
            else if (_tco_s is IrAct _tco_m4)
            {
                var stmts = _tco_m4.Field0;
                var t = _tco_m4.Field1;
                var sp = _tco_m4.Field2;
            return has_tail_call_act(stmts, func_name);
            }
            {
            return false;
            }
        }
    }

    public static bool has_tail_call_act(List<IRActStmt> stmts, string func_name) => ((Func<long, bool>)((len) => ((len == 0L) ? false : ((Func<IRActStmt, bool>)((last) => last switch { IrDoExec(var e, var sp) => (bool)(emit__x86_64_code_generator_has_tail_call(e, func_name)), _ => (bool)(false), }))(stmts[(int)(len - 1L)]))))(((long)stmts.Count));

    public static bool emit__x86_64_code_generator_has_tail_call_branches(List<IRBranch> branches, string func_name, long i)
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
            if (emit__x86_64_code_generator_has_tail_call(b.body, func_name))
            {
            return true;
            }
            else
            {
            var _tco_0 = branches;
            var _tco_1 = func_name;
            var _tco_2 = (i + 1L);
            branches = _tco_0;
            func_name = _tco_1;
            i = _tco_2;
            continue;
            }
            }
        }
    }

    public static bool emit__x86_64_code_generator_should_tco(IRDef def) => ((((long)def.@params.Count) > 0L) ? emit__x86_64_code_generator_has_tail_call(def.body, def.name) : false);

    public static CodegenState st_set_tail_pos(CodegenState st, bool v) => (st with { tco = (st.tco with { in_tail_pos = v }) });

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
            var _tco_1 = (i + 1L);
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
            var _tco_2 = (i + 1L);
            var _tco_3 = ((Func<List<long>>)(() => { var _l = acc; _l.Add(slot); return _l; }))();
            bindings = _tco_0;
            @params = _tco_1;
            i = _tco_2;
            acc = _tco_3;
            continue;
            }
        }
    }

    public static CodegenState emit_function(CodegenState st, IRDef def) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<long, CodegenState>)((frame_patch_pos) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<bool, CodegenState>)((is_tco) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<EmitResult, CodegenState>)((result) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<long, CodegenState>)((frame_size) => st_add_deferred_patch(st7, make_i32_patch((frame_patch_pos + 3L), frame_size))))(align_16((st7.spill_count * 8L)))))(emit_epilogue(st6))))(((result.reg == reg_rax()) ? result.state : st_append_text(result.state, mov_rr(reg_rax(), result.reg))))))(emit__x86_64_code_generator_emit_expr(st5, def.body))))((is_tco ? ((Func<TcoAllocResult, CodegenState>)((tco_alloc) => ((Func<CodegenState, CodegenState>)((st_a) => ((Func<List<long>, CodegenState>)((p_locals) => (st_a with { tco = new TcoState(active: true, in_tail_pos: true, loop_top: st_a.text_len, param_locals: p_locals, temp_locals: tco_alloc.alloc_locals, current_func: def.name, saved_next_local: st_a.next_local, saved_next_temp: st_a.next_temp) })))(collect_param_locals(st_a.locals, def.@params, 0L, new List<long>()))))(tco_alloc.alloc_state)))(pre_alloc_tco_temps(st4, 0L, ((long)def.@params.Count), new List<long>())) : st4))))(emit__x86_64_code_generator_should_tco(def))))(bind_params(st3, def.@params, 0L))))(st_append_text(st2, emit_sub_rsp_imm32(0L)))))(st2.text_len)))(emit_prologue(st1))))(reset_func_state(st, def.name));

    public static CodegenState emit__x86_64_code_generator_emit_all_defs(CodegenState st, List<IRDef> defs, long i)
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
            var _tco_2 = (i + 1L);
            st = _tco_0;
            defs = _tco_1;
            i = _tco_2;
            continue;
            }
        }
    }

    public static long lookup_local(List<LocalBinding> bindings, string name) => lookup_local_loop(bindings, name, 0L, ((long)bindings.Count));

    public static long lookup_local_loop(List<LocalBinding> bindings, string name, long i, long len)
    {
        while (true)
        {
            if ((i == len))
            {
            return (-1L);
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

    public static CodegenState add_local(CodegenState st, string name, long slot) => (st with { locals = Enumerable.Concat(new List<LocalBinding> { new LocalBinding(name: name, slot: slot) }, st.locals).ToList() });

    public static EmitResult emit__x86_64_code_generator_emit_expr(CodegenState st, IRExpr expr) => expr switch { IrIntLit(var value, var sp) => (EmitResult)(emit_int_lit(st, value)), IrNumLit(var value, var sp) => (EmitResult)(emit_int_lit(st, value)), IrBoolLit(var value, var sp) => (EmitResult)(emit_int_lit(st, (value ? 1L : 0L))), IrCharLit(var value, var sp) => (EmitResult)(emit_int_lit(st, value)), IrTextLit(var value, var sp) => (EmitResult)(emit_text_lit(st, value)), IrNegate(var operand, var sp) => (EmitResult)(emit_negate(st, operand)), IrError(var msg, var ty, var sp) => (EmitResult)(emit_int_lit(st_add_error(st, cdx_ir_error(), msg, sp), 0L)), IrName(var name, var ty, var sp) => (EmitResult)(emit_name(st, name, ty)), IrLet(var name, var ty, var value, var body, var sp) => (EmitResult)(emit__x86_64_code_generator_emit_let(st, name, value, body)), IrBinary(var op, var left, var right, var ty, var sp) => (EmitResult)(emit__x86_64_code_generator_emit_binary(st, op, left, right)), IrIf(var cond, var then_e, var else_e, var ty, var sp) => (EmitResult)(emit__x86_64_code_generator_emit_if(st, cond, then_e, else_e)), IrApply(var func, var arg, var ty, var sp) => (EmitResult)(emit__x86_64_code_generator_emit_apply(st, func, arg, ty)), IrRecord(var rname, var fields, var ty, var sp) => (EmitResult)(emit__x86_64_code_generator_emit_record(st, fields, ty)), IrFieldAccess(var rec, var field, var ty, var sp) => (EmitResult)(emit__x86_64_code_generator_emit_field_access(st, rec, field)), IrMatch(var scrut, var branches, var ty, var sp) => (EmitResult)(emit__x86_64_code_generator_emit_match(st, scrut, branches)), IrList(var elems, var ty, var sp) => (EmitResult)(emit__x86_64_code_generator_emit_list(st, elems)), IrAct(var stmts, var ty, var sp) => (EmitResult)(emit__x86_64_code_generator_emit_act_stmts(st, stmts, 0L, ((long)stmts.Count))), _ => (EmitResult)(new EmitResult(state: st, reg: reg_rax())), };

    public static EmitResult emit__x86_64_code_generator_emit_act_stmts(CodegenState st, List<IRActStmt> stmts, long i, long len)
    {
        while (true)
        {
            if ((i == len))
            {
            return new EmitResult(state: st, reg: reg_rax());
            }
            else
            {
            var s = stmts[(int)i];
            var _tco_s = s;
            if (_tco_s is IrDoBind _tco_m0)
            {
                var name = _tco_m0.Field0;
                var ty = _tco_m0.Field1;
                var val = _tco_m0.Field2;
                var sp = _tco_m0.Field3;
            var val_result = emit__x86_64_code_generator_emit_expr(st, val);
            var loc = alloc_local(val_result.state);
            var st1 = store_local(loc.state, loc.reg, val_result.reg);
            var st2 = add_local(st1, name, loc.reg);
            var _tco_0 = st2;
            var _tco_1 = stmts;
            var _tco_2 = (i + 1L);
            var _tco_3 = len;
            st = _tco_0;
            stmts = _tco_1;
            i = _tco_2;
            len = _tco_3;
            continue;
            }
            else if (_tco_s is IrDoExec _tco_m1)
            {
                var e = _tco_m1.Field0;
                var sp = _tco_m1.Field1;
            if ((i == (len - 1L)))
            {
            return emit__x86_64_code_generator_emit_expr(st, e);
            }
            else
            {
            var r = emit__x86_64_code_generator_emit_expr(st, e);
            var _tco_0 = r.state;
            var _tco_1 = stmts;
            var _tco_2 = (i + 1L);
            var _tco_3 = len;
            st = _tco_0;
            stmts = _tco_1;
            i = _tco_2;
            len = _tco_3;
            continue;
            }
            }
            }
        }
    }

    public static EmitResult emit_text_lit(CodegenState st, string value) => ((Func<long, EmitResult>)((len) => ((Func<long, EmitResult>)((rodata_off) => ((Func<List<long>, EmitResult>)((str_bytes) => ((Func<List<long>, EmitResult>)((padded) => ((Func<long, EmitResult>)((new_rodata_len) => ((Func<CodegenState, EmitResult>)((st1) => ((Func<RodataFixup, EmitResult>)((fixup) => ((Func<CodegenState, EmitResult>)((st2) => ((Func<CodegenState, EmitResult>)((st3) => ((Func<EmitResult, EmitResult>)((rd) => new EmitResult(state: st_append_text(rd.state, mov_rr(rd.reg, reg_rax())), reg: rd.reg)))(alloc_temp(st3))))((st2 with { rodata_fixups = ((Func<List<RodataFixup>>)(() => { var _l = st2.rodata_fixups; _l.Add(fixup); return _l; }))() }))))(st_append_text(st1, mov_ri64(reg_rax(), 0L)))))(new RodataFixup(patch_offset: (st1.text_len + 2L), rodata_offset: rodata_off))))((st with { rodata_len = new_rodata_len }))))(_Buf.buf_write_bytes(st.rodata_buf_addr, st.rodata_len, padded))))(pad_to_8(str_bytes))))(append_text_bytes(write_i64(len), value, 0L, len))))(st.rodata_len)))(((long)value.Length));

    public static List<long> append_text_bytes(List<long> acc, string s, long i, long len)
    {
        while (true)
        {
            if ((i == len))
            {
            return acc;
            }
            else
            {
            var _tco_0 = ((Func<List<long>>)(() => { var _l = acc; _l.Add(((long)s[(int)i])); return _l; }))();
            var _tco_1 = s;
            var _tco_2 = (i + 1L);
            var _tco_3 = len;
            acc = _tco_0;
            s = _tco_1;
            i = _tco_2;
            len = _tco_3;
            continue;
            }
        }
    }

    public static List<long> pad_to_8(List<long> xs)
    {
        while (true)
        {
            var rem = int_mod(((long)xs.Count), 8L);
            if ((rem == 0L))
            {
            return xs;
            }
            else
            {
            var _tco_0 = ((Func<List<long>>)(() => { var _l = xs; _l.Add(0L); return _l; }))();
            xs = _tco_0;
            continue;
            }
        }
    }

    public static EmitResult emit_negate(CodegenState st, IRExpr operand) => ((Func<EmitResult, EmitResult>)((r) => ((Func<EmitResult, EmitResult>)((rd) => ((Func<CodegenState, EmitResult>)((st1) => new EmitResult(state: st_append_text(st1, neg_r(rd.reg)), reg: rd.reg)))(st_append_text(rd.state, mov_rr(rd.reg, r.reg)))))(alloc_temp(r.state))))(emit__x86_64_code_generator_emit_expr(st, operand));

    public static EmitResult emit_show_builtin(CodegenState st, List<IRExpr> args) => ((Func<IRExpr, EmitResult>)((arg) => ir_expr_type(arg) switch { BooleanTy { } => (EmitResult)(emit_show_bool(st, arg)), _ => (EmitResult)(emit_helper_call_1(st, args, "\u0055\u0055\u0011\u000E\u0010\u000F")), }))(args[(int)0L]);

    public static EmitResult emit_show_bool(CodegenState st, IRExpr arg) => ((Func<EmitResult, EmitResult>)((val_result) => ((Func<CodegenState, EmitResult>)((st1) => ((Func<long, EmitResult>)((je_false_pos) => ((Func<CodegenState, EmitResult>)((st2) => ((Func<EmitResult, EmitResult>)((true_result) => ((Func<EmitResult, EmitResult>)((result_loc) => ((Func<CodegenState, EmitResult>)((st3) => ((Func<long, EmitResult>)((jmp_end_pos) => ((Func<CodegenState, EmitResult>)((st4) => ((Func<CodegenState, EmitResult>)((st5) => ((Func<EmitResult, EmitResult>)((false_result) => ((Func<CodegenState, EmitResult>)((st6) => ((Func<CodegenState, EmitResult>)((st7) => load_local(st7, result_loc.reg)))(patch_jmp_at(st6, jmp_end_pos, st6.text_len))))(store_local(false_result.state, result_loc.reg, false_result.reg))))(emit_text_lit(st5, "\u0036\u000F\u0017\u0013\u000D"))))(patch_jcc_at(st4, je_false_pos, st4.text_len))))(st_append_text(st3, jmp(0L)))))(st3.text_len)))(store_local(result_loc.state, result_loc.reg, true_result.reg))))(alloc_local(true_result.state))))(emit_text_lit(st2, "\u0028\u0015\u0019\u000D"))))(st_append_text(st1, jcc(cc_e(), 0L)))))(st1.text_len)))(st_append_text(val_result.state, test_rr(val_result.reg, val_result.reg)))))(emit__x86_64_code_generator_emit_expr(st_set_tail_pos(st, false), arg));

    public static EmitResult emit_num_arith(CodegenState st, long l_reg, long r_reg, long sse_op) => ((Func<EmitResult, EmitResult>)((rd) => new EmitResult(state: st_append_text(rd.state, Enumerable.Concat(movq_to_xmm(0L, l_reg), Enumerable.Concat(movq_to_xmm(1L, r_reg), Enumerable.Concat(new List<long> { 242L, 15L, sse_op, modrm(3L, 0L, 1L) }, movq_from_xmm(rd.reg, 0L)).ToList()).ToList()).ToList()), reg: rd.reg)))(alloc_temp(st));

    public static EmitResult emit_num_comparison(CodegenState st, long cc, long l_reg, long r_reg) => ((Func<EmitResult, EmitResult>)((rd) => new EmitResult(state: st_append_text(rd.state, Enumerable.Concat(movq_to_xmm(0L, l_reg), Enumerable.Concat(movq_to_xmm(1L, r_reg), Enumerable.Concat(ucomisd_xmm(0L, 1L), Enumerable.Concat(setcc(cc, rd.reg), movzx_byte_self(rd.reg)).ToList()).ToList()).ToList()).ToList()), reg: rd.reg)))(alloc_temp(st));

    public static EmitResult emit_int_lit(CodegenState st, long value) => ((Func<EmitResult, EmitResult>)((tmp) => new EmitResult(state: st_append_text(tmp.state, li(tmp.reg, value)), reg: tmp.reg)))(alloc_temp(st));

    public static long find_ctor_tag(List<SumCtor> ctors, string name, long i)
    {
        while (true)
        {
            if ((i == ((long)ctors.Count)))
            {
            return (-1L);
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
            var _tco_2 = (i + 1L);
            ctors = _tco_0;
            name = _tco_1;
            i = _tco_2;
            continue;
            }
            }
        }
    }

    public static EmitResult emit_name(CodegenState st, string name, CodexType ty) => ((Func<long, EmitResult>)((slot) => ((slot >= 0L) ? load_local(st, slot) : emit_name_nonlocal(st, name, ty))))(lookup_local(st.locals, name));

    public static EmitResult emit_name_nonlocal(CodegenState st, string name, CodexType ty) => ((name == "\u0014\u000D\u000F\u001F\u0049\u0013\u000F\u0021\u000D") ? emit_heap_save_builtin(st) : ((Func<CodexType, EmitResult>)((resolved) => resolved switch { SumTy(var sname, var ctors) => (EmitResult)(((Func<long, EmitResult>)((tag) => ((tag >= 0L) ? emit_nullary_ctor(st, tag) : emit_name_as_call(st, name))))(find_ctor_tag(ctors, name, 0L))), FunTy(var pt, var rt) => (EmitResult)(emit_partial_application(st, name, new List<IRExpr>())), _ => (EmitResult)(emit_name_as_call(st, name)), }))(resolve_to_sum_with_defs(st, ty)));

    public static CodexType resolve_to_sum(CodexType ty)
    {
        while (true)
        {
            var _tco_s = ty;
            if (_tco_s is SumTy _tco_m0)
            {
                var sn = _tco_m0.Field0;
                var cs = _tco_m0.Field1;
            return ty;
            }
            else if (_tco_s is EffectfulTy _tco_m1)
            {
                var effs = _tco_m1.Field0;
                var ret = _tco_m1.Field1;
            var _tco_0 = ret;
            ty = _tco_0;
            continue;
            }
            {
            return ty;
            }
        }
    }

    public static CodexType resolve_to_sum_with_defs(CodegenState st, CodexType ty)
    {
        while (true)
        {
            var resolved = resolve_constructed_ty(st, ty);
            var _tco_s = resolved;
            if (_tco_s is SumTy _tco_m0)
            {
                var sn = _tco_m0.Field0;
                var cs = _tco_m0.Field1;
            return resolved;
            }
            else if (_tco_s is EffectfulTy _tco_m1)
            {
                var effs = _tco_m1.Field0;
                var ret = _tco_m1.Field1;
            var _tco_0 = st;
            var _tco_1 = ret;
            st = _tco_0;
            ty = _tco_1;
            continue;
            }
            {
            return resolved;
            }
        }
    }

    public static EmitResult emit_nullary_ctor(CodegenState st, long tag) => ((Func<EmitResult, EmitResult>)((ptr_loc) => ((Func<EmitResult, EmitResult>)((ptr_tmp) => ((Func<CodegenState, EmitResult>)((st1) => ((Func<CodegenState, EmitResult>)((st2) => ((Func<CodegenState, EmitResult>)((st3) => ((Func<EmitResult, EmitResult>)((tag_tmp) => ((Func<CodegenState, EmitResult>)((st4) => ((Func<EmitResult, EmitResult>)((ptr_load) => ((Func<CodegenState, EmitResult>)((st5) => load_local(st5, ptr_loc.reg)))(st_append_text(ptr_load.state, mov_store(ptr_load.reg, tag_tmp.reg, 0L)))))(load_local(st4, ptr_loc.reg))))(st_append_text(tag_tmp.state, li(tag_tmp.reg, tag)))))(alloc_temp(st3))))(st_append_text(st2, add_ri(reg_r10(), 8L)))))(store_local(st1, ptr_loc.reg, ptr_tmp.reg))))(st_append_text(ptr_tmp.state, mov_rr(ptr_tmp.reg, reg_r10())))))(alloc_temp(ptr_loc.state))))(alloc_local(st));

    public static EmitResult emit_name_as_call(CodegenState st, string name) => ((name == "\u0015\u000D\u000F\u0016\u0049\u0017\u0011\u0012\u000D") ? emit_read_line_builtin(st) : ((Func<CodegenState, EmitResult>)((st1) => ((Func<EmitResult, EmitResult>)((tmp) => new EmitResult(state: st_append_text(tmp.state, mov_rr(tmp.reg, reg_rax())), reg: tmp.reg)))(alloc_temp(st1))))(emit_call_to(st, name)));

    public static EmitResult emit__x86_64_code_generator_emit_let(CodegenState st, string name, IRExpr value, IRExpr body) => ((Func<bool, EmitResult>)((saved_tail) => ((Func<EmitResult, EmitResult>)((val_result) => ((Func<EmitResult, EmitResult>)((loc) => ((Func<CodegenState, EmitResult>)((st1) => ((Func<CodegenState, EmitResult>)((st2) => emit__x86_64_code_generator_emit_expr(st_set_tail_pos(st2, saved_tail), body)))(add_local(st1, name, loc.reg))))(store_local(loc.state, loc.reg, val_result.reg))))(alloc_local(val_result.state))))(emit__x86_64_code_generator_emit_expr(st_set_tail_pos(st, false), value))))(st.tco.in_tail_pos);

    public static bool is_append_list_op(IRBinaryOp op) => op switch { IrAppendList { } => (bool)(true), _ => (bool)(false), };

    public static List<IRExpr> flatten_append_chain(IRExpr e, List<IRExpr> acc)
    {
        while (true)
        {
            var _tco_s = e;
            if (_tco_s is IrBinary _tco_m0)
            {
                var op = _tco_m0.Field0;
                var l = _tco_m0.Field1;
                var r = _tco_m0.Field2;
                var ty = _tco_m0.Field3;
                var sp = _tco_m0.Field4;
            if (is_append_list_op(op))
            {
            var _tco_0 = l;
            var _tco_1 = flatten_append_chain(r, acc);
            e = _tco_0;
            acc = _tco_1;
            continue;
            }
            else
            {
            return Enumerable.Concat(new List<IRExpr> { e }, acc).ToList();
            }
            }
            {
            return Enumerable.Concat(new List<IRExpr> { e }, acc).ToList();
            }
        }
    }

    public static ConcatManyEval emit_concat_many_eval(CodegenState st, List<IRExpr> exprs, long i, long n, List<long> locals)
    {
        while (true)
        {
            if ((i >= n))
            {
            return new ConcatManyEval(state: st, locals: locals);
            }
            else
            {
            var r = emit__x86_64_code_generator_emit_expr(st_set_tail_pos(st, false), exprs[(int)i]);
            var loc = alloc_local(r.state);
            var st1 = store_local(loc.state, loc.reg, r.reg);
            var _tco_0 = st1;
            var _tco_1 = exprs;
            var _tco_2 = (i + 1L);
            var _tco_3 = n;
            var _tco_4 = ((Func<List<long>>)(() => { var _l = locals; _l.Add(loc.reg); return _l; }))();
            st = _tco_0;
            exprs = _tco_1;
            i = _tco_2;
            n = _tco_3;
            locals = _tco_4;
            continue;
            }
        }
    }

    public static CodegenState emit_concat_many_store_ptrs(CodegenState st, List<long> locals, long i, long n, long arr_reg)
    {
        while (true)
        {
            if ((i >= n))
            {
            return st;
            }
            else
            {
            var loaded = load_local(st, locals[(int)i]);
            var st1 = st_append_text(loaded.state, mov_store(arr_reg, loaded.reg, (i * 8L)));
            var _tco_0 = st1;
            var _tco_1 = locals;
            var _tco_2 = (i + 1L);
            var _tco_3 = n;
            var _tco_4 = arr_reg;
            st = _tco_0;
            locals = _tco_1;
            i = _tco_2;
            n = _tco_3;
            arr_reg = _tco_4;
            continue;
            }
        }
    }

    public static EmitResult emit_concat_many(CodegenState st, List<IRExpr> exprs) => ((Func<long, EmitResult>)((n) => ((Func<ConcatManyEval, EmitResult>)((eval) => ((Func<CodegenState, EmitResult>)((st1) => ((Func<EmitResult, EmitResult>)((arr) => ((Func<CodegenState, EmitResult>)((st2) => ((Func<CodegenState, EmitResult>)((st3) => ((Func<CodegenState, EmitResult>)((st4) => ((Func<CodegenState, EmitResult>)((st5) => ((Func<CodegenState, EmitResult>)((st6) => ((Func<CodegenState, EmitResult>)((st7) => ((Func<EmitResult, EmitResult>)((rd) => new EmitResult(state: st_append_text(rd.state, mov_rr(rd.reg, reg_rax())), reg: rd.reg)))(alloc_temp(st7))))(emit_call_to(st6, "\u0055\u0055\u0017\u0011\u0013\u000E\u0055\u0018\u0010\u0012\u0018\u000F\u000E\u0055\u001A\u000F\u0012\u001E"))))(st_append_text(st5, li(reg_rsi(), n)))))(st_append_text(st4, mov_rr(reg_rdi(), arr.reg)))))(emit_concat_many_store_ptrs(st3, eval.locals, 0L, n, arr.reg))))(st_append_text(st2, add_ri(reg_r10(), (n * 8L))))))(st_append_text(arr.state, mov_rr(arr.reg, reg_r10())))))(alloc_temp(st1))))(eval.state)))(emit_concat_many_eval(st, exprs, 0L, n, new List<long>()))))(((long)exprs.Count));

    public static EmitResult emit__x86_64_code_generator_emit_binary(CodegenState st, IRBinaryOp op, IRExpr left, IRExpr right) => (is_append_list_op(op) ? emit_append_list(st, left, right) : emit_binary_standard(st, op, left, right));

    public static EmitResult emit_append_list(CodegenState st, IRExpr left, IRExpr right) => ((Func<List<IRExpr>, EmitResult>)((chain) => emit_concat_many(st, chain)))(flatten_append_chain(left, new List<IRExpr> { right }));

    public static EmitResult emit_binary_standard(CodegenState st, IRBinaryOp op, IRExpr left, IRExpr right) => ((Func<EmitResult, EmitResult>)((l) => ((Func<EmitResult, EmitResult>)((l_loc) => ((Func<CodegenState, EmitResult>)((st1) => ((Func<EmitResult, EmitResult>)((r) => ((Func<EmitResult, EmitResult>)((r_loc) => ((Func<CodegenState, EmitResult>)((st2) => ((Func<EmitResult, EmitResult>)((l_load) => ((Func<EmitResult, EmitResult>)((r_load) => ((Func<CodexType, EmitResult>)((operand_ty) => emit_binary_op(r_load.state, op, l_load.reg, r_load.reg, operand_ty)))(ir_expr_type(left))))(load_local(l_load.state, r_loc.reg))))(load_local(st2, l_loc.reg))))(store_local(r_loc.state, r_loc.reg, r.reg))))(alloc_local(r.state))))(emit__x86_64_code_generator_emit_expr(st1, right))))(store_local(l_loc.state, l_loc.reg, l.reg))))(alloc_local(l.state))))(emit__x86_64_code_generator_emit_expr(st, left));

    public static EmitResult emit_binary_op(CodegenState st, IRBinaryOp op, long l_reg, long r_reg, CodexType operand_ty) => ((Func<EmitResult, EmitResult>)((rd) => op switch { IrAddInt { } => (EmitResult)(new EmitResult(state: st_append_text(rd.state, Enumerable.Concat(mov_rr(rd.reg, l_reg), add_rr(rd.reg, r_reg)).ToList()), reg: rd.reg)), IrSubInt { } => (EmitResult)(new EmitResult(state: st_append_text(rd.state, Enumerable.Concat(mov_rr(rd.reg, l_reg), sub_rr(rd.reg, r_reg)).ToList()), reg: rd.reg)), IrMulInt { } => (EmitResult)(new EmitResult(state: st_append_text(rd.state, Enumerable.Concat(mov_rr(rd.reg, l_reg), imul_rr(rd.reg, r_reg)).ToList()), reg: rd.reg)), IrDivInt { } => (EmitResult)(new EmitResult(state: st_append_text(rd.state, Enumerable.Concat(mov_rr(reg_rax(), l_reg), Enumerable.Concat(cqo(), Enumerable.Concat(idiv_r(r_reg), mov_rr(rd.reg, reg_rax())).ToList()).ToList()).ToList()), reg: rd.reg)), IrAddNum { } => (EmitResult)(emit_num_arith(st, l_reg, r_reg, 88L)), IrSubNum { } => (EmitResult)(emit_num_arith(st, l_reg, r_reg, 92L)), IrMulNum { } => (EmitResult)(emit_num_arith(st, l_reg, r_reg, 89L)), IrDivNum { } => (EmitResult)(emit_num_arith(st, l_reg, r_reg, 94L)), IrEq { } => (EmitResult)(emit_eq_op(st, l_reg, r_reg, operand_ty)), IrNotEq { } => (EmitResult)(emit_neq_op(st, l_reg, r_reg, operand_ty)), IrLt { } => (EmitResult)(emit_comparison(st, cc_l(), l_reg, r_reg)), IrGt { } => (EmitResult)(emit_comparison(st, cc_g(), l_reg, r_reg)), IrLtEq { } => (EmitResult)(emit_comparison(st, cc_le(), l_reg, r_reg)), IrGtEq { } => (EmitResult)(emit_comparison(st, cc_ge(), l_reg, r_reg)), IrAnd { } => (EmitResult)(new EmitResult(state: st_append_text(rd.state, Enumerable.Concat(mov_rr(rd.reg, l_reg), and_rr(rd.reg, r_reg)).ToList()), reg: rd.reg)), IrOr { } => (EmitResult)(new EmitResult(state: st_append_text(rd.state, Enumerable.Concat(mov_rr(rd.reg, l_reg), add_rr(rd.reg, r_reg)).ToList()), reg: rd.reg)), IrAppendText { } => (EmitResult)(emit_helper_call_2_regs(st, l_reg, r_reg, "\u0055\u0055\u0013\u000E\u0015\u0055\u0018\u0010\u0012\u0018\u000F\u000E")), IrAppendList { } => (EmitResult)(emit_helper_call_2_regs(st, l_reg, r_reg, "\u0055\u0055\u0017\u0011\u0013\u000E\u0055\u000F\u001F\u001F\u000D\u0012\u0016")), IrConsList { } => (EmitResult)(emit_helper_call_2_regs(st, l_reg, r_reg, "\u0055\u0055\u0017\u0011\u0013\u000E\u0055\u0018\u0010\u0012\u0013")), _ => (EmitResult)(new EmitResult(state: st, reg: reg_rax())), }))(alloc_temp(st));

    public static EmitResult emit_helper_call_2_regs(CodegenState st, long l_reg, long r_reg, string name) => ((Func<CodegenState, EmitResult>)((st1) => ((Func<CodegenState, EmitResult>)((st2) => ((Func<EmitResult, EmitResult>)((rd) => new EmitResult(state: st_append_text(rd.state, mov_rr(rd.reg, reg_rax())), reg: rd.reg)))(alloc_temp(st2))))(emit_call_to(st1, name))))(st_append_text(st, Enumerable.Concat(mov_rr(reg_rdi(), l_reg), mov_rr(reg_rsi(), r_reg)).ToList()));

    public static EmitResult emit_eq_op(CodegenState st, long l_reg, long r_reg, CodexType ty) => ((Func<CodexType, EmitResult>)((resolved) => resolved switch { TextTy { } => (EmitResult)(emit_helper_call_2_regs(st, l_reg, r_reg, "\u0055\u0055\u0013\u000E\u0015\u0055\u000D\u0025")), NumberTy { } => (EmitResult)(emit_num_comparison(st, cc_e(), l_reg, r_reg)), SumTy(var sn, var cs) => (EmitResult)(emit_sum_tag_comparison(st, cc_e(), l_reg, r_reg)), _ => (EmitResult)(emit_comparison(st, cc_e(), l_reg, r_reg)), }))(resolve_to_sum_with_defs(st, ty));

    public static EmitResult emit_neq_op(CodegenState st, long l_reg, long r_reg, CodexType ty) => ((Func<CodexType, EmitResult>)((resolved) => resolved switch { TextTy { } => (EmitResult)(((Func<EmitResult, EmitResult>)((eq_result) => ((Func<EmitResult, EmitResult>)((rd) => new EmitResult(state: st_append_text(rd.state, Enumerable.Concat(cmp_ri(eq_result.reg, 0L), Enumerable.Concat(setcc(cc_e(), rd.reg), movzx_byte_self(rd.reg)).ToList()).ToList()), reg: rd.reg)))(alloc_temp(eq_result.state))))(emit_helper_call_2_regs(st, l_reg, r_reg, "\u0055\u0055\u0013\u000E\u0015\u0055\u000D\u0025"))), NumberTy { } => (EmitResult)(emit_num_comparison(st, cc_ne(), l_reg, r_reg)), SumTy(var sn, var cs) => (EmitResult)(emit_sum_tag_comparison(st, cc_ne(), l_reg, r_reg)), _ => (EmitResult)(emit_comparison(st, cc_ne(), l_reg, r_reg)), }))(resolve_to_sum_with_defs(st, ty));

    public static EmitResult emit_sum_tag_comparison(CodegenState st, long cc, long l_reg, long r_reg) => ((Func<EmitResult, EmitResult>)((l_tag) => ((Func<CodegenState, EmitResult>)((st1) => ((Func<EmitResult, EmitResult>)((r_tag) => ((Func<CodegenState, EmitResult>)((st2) => emit_comparison(st2, cc, l_tag.reg, r_tag.reg)))(st_append_text(r_tag.state, mov_load(r_tag.reg, r_reg, 0L)))))(alloc_temp(st1))))(st_append_text(l_tag.state, mov_load(l_tag.reg, l_reg, 0L)))))(alloc_temp(st));

    public static EmitResult emit_comparison(CodegenState st, long cc, long l_reg, long r_reg) => ((Func<EmitResult, EmitResult>)((rd) => new EmitResult(state: st_append_text(rd.state, Enumerable.Concat(cmp_rr(l_reg, r_reg), Enumerable.Concat(setcc(cc, rd.reg), movzx_byte_self(rd.reg)).ToList()).ToList()), reg: rd.reg)))(alloc_temp(st));

    public static EmitResult emit__x86_64_code_generator_emit_if(CodegenState st, IRExpr cond, IRExpr then_e, IRExpr else_e) => ((Func<bool, EmitResult>)((saved_tail) => ((Func<EmitResult, EmitResult>)((cond_result) => ((Func<CodegenState, EmitResult>)((st1) => ((Func<long, EmitResult>)((je_false_pos) => ((Func<CodegenState, EmitResult>)((st2) => ((Func<EmitResult, EmitResult>)((then_result) => ((Func<EmitResult, EmitResult>)((result_loc) => ((Func<CodegenState, EmitResult>)((st3) => ((Func<long, EmitResult>)((jmp_end_pos) => ((Func<CodegenState, EmitResult>)((st4) => ((Func<CodegenState, EmitResult>)((st5) => ((Func<EmitResult, EmitResult>)((else_result) => ((Func<CodegenState, EmitResult>)((st6) => ((Func<CodegenState, EmitResult>)((st7) => load_local(st7, result_loc.reg)))(patch_jmp_at(st6, jmp_end_pos, st6.text_len))))(store_local(else_result.state, result_loc.reg, else_result.reg))))(emit__x86_64_code_generator_emit_expr(st_set_tail_pos(st5, saved_tail), else_e))))(patch_jcc_at(st4, je_false_pos, st4.text_len))))(st_append_text(st3, jmp(0L)))))(st3.text_len)))(store_local(result_loc.state, result_loc.reg, then_result.reg))))(alloc_local(then_result.state))))(emit__x86_64_code_generator_emit_expr(st_set_tail_pos(st2, saved_tail), then_e))))(st_append_text(st1, jcc(cc_e(), 0L)))))(st1.text_len)))(st_append_text(cond_result.state, test_rr(cond_result.reg, cond_result.reg)))))(emit__x86_64_code_generator_emit_expr(st_set_tail_pos(st, false), cond))))(st.tco.in_tail_pos);

    public static bool is_string_builtin(string name) => ((name == "\u000E\u000D\u0024\u000E\u0049\u0017\u000D\u0012\u001D\u000E\u0014") ? true : ((name == "\u0011\u0012\u000E\u000D\u001D\u000D\u0015\u0049\u000E\u0010\u0049\u000E\u000D\u0024\u000E") ? true : ((name == "\u0013\u0014\u0010\u001B") ? true : ((name == "\u000E\u000D\u0024\u000E\u0049\u000E\u0010\u0049\u0011\u0012\u000E\u000D\u001D\u000D\u0015") ? true : ((name == "\u000E\u000D\u0024\u000E\u0049\u0015\u000D\u001F\u0017\u000F\u0018\u000D") ? true : ((name == "\u000E\u000D\u0024\u000E\u0049\u0018\u0010\u0012\u000E\u000F\u0011\u0012\u0013") ? true : ((name == "\u000E\u000D\u0024\u000E\u0049\u0013\u000E\u000F\u0015\u000E\u0013\u0049\u001B\u0011\u000E\u0014") ? true : ((name == "\u000E\u000D\u0024\u000E\u0049\u0018\u0010\u001A\u001F\u000F\u0015\u000D") ? true : ((name == "\u000E\u000D\u0024\u000E\u0049\u0018\u0010\u0012\u0018\u000F\u000E\u0049\u0017\u0011\u0013\u000E") ? true : ((name == "\u000E\u000D\u0024\u000E\u0049\u0013\u001F\u0017\u0011\u000E") ? true : ((name == "\u0013\u0019\u0020\u0013\u000E\u0015\u0011\u0012\u001D") ? true : false)))))))))));

    public static bool is_list_builtin(string name) => ((name == "\u0017\u0011\u0013\u000E\u0049\u0017\u000D\u0012\u001D\u000E\u0014") ? true : ((name == "\u0017\u0011\u0013\u000E\u0049\u000F\u000E") ? true : ((name == "\u0017\u0011\u0013\u000E\u0049\u0018\u0010\u0012\u0013") ? true : ((name == "\u0017\u0011\u0013\u000E\u0049\u000F\u001F\u001F\u000D\u0012\u0016") ? true : ((name == "\u0017\u0011\u0013\u000E\u0049\u0013\u0012\u0010\u0018") ? true : ((name == "\u0017\u0011\u0013\u000E\u0049\u0011\u0012\u0013\u000D\u0015\u000E\u0049\u000F\u000E") ? true : ((name == "\u0017\u0011\u0013\u000E\u0049\u0013\u000D\u000E\u0049\u000F\u000E") ? true : ((name == "\u0017\u0011\u0013\u000E\u0049\u0018\u0010\u0012\u000E\u000F\u0011\u0012\u0013") ? true : false))))))));

    public static bool is_char_builtin(string name) => ((name == "\u0018\u0014\u000F\u0015\u0049\u000F\u000E") ? true : ((name == "\u0018\u0014\u000F\u0015\u0049\u0018\u0010\u0016\u000D\u0049\u000F\u000E") ? true : ((name == "\u0018\u0014\u000F\u0015\u0049\u0018\u0010\u0016\u000D") ? true : ((name == "\u0018\u0010\u0016\u000D\u0049\u000E\u0010\u0049\u0018\u0014\u000F\u0015") ? true : ((name == "\u0018\u0014\u000F\u0015\u0049\u000E\u0010\u0049\u000E\u000D\u0024\u000E") ? true : ((name == "\u0011\u0013\u0049\u0017\u000D\u000E\u000E\u000D\u0015") ? true : ((name == "\u0011\u0013\u0049\u0016\u0011\u001D\u0011\u000E") ? true : ((name == "\u0011\u0013\u0049\u001B\u0014\u0011\u000E\u000D\u0013\u001F\u000F\u0018\u000D") ? true : false))))))));

    public static bool is_io_builtin(string name) => ((name == "\u001F\u0015\u0011\u0012\u000E\u0049\u0017\u0011\u0012\u000D") ? true : ((name == "\u0015\u000D\u000F\u0016\u0049\u001C\u0011\u0017\u000D") ? true : ((name == "\u001B\u0015\u0011\u000E\u000D\u0049\u001C\u0011\u0017\u000D") ? true : ((name == "\u0015\u000D\u000F\u0016\u0049\u0017\u0011\u0012\u000D") ? true : ((name == "\u001B\u0015\u0011\u000E\u000D\u0049\u0020\u0011\u0012\u000F\u0015\u001E") ? true : false)))));

    public static bool is_misc_builtin(string name) => ((name == "\u0012\u000D\u001D\u000F\u000E\u000D") ? true : ((name == "\u001D\u000D\u000E\u0049\u000F\u0015\u001D\u0013") ? true : ((name == "\u0018\u0019\u0015\u0015\u000D\u0012\u000E\u0049\u0016\u0011\u0015") ? true : ((name == "\u001C\u0011\u0017\u000D\u0049\u000D\u0024\u0011\u0013\u000E\u0013") ? true : ((name == "\u0015\u000D\u0018\u0010\u0015\u0016\u0049\u0013\u000D\u000E") ? true : ((name == "\u0017\u0011\u0012\u0022\u000D\u0016\u0049\u0017\u0011\u0013\u000E\u0049\u000D\u001A\u001F\u000E\u001E") ? true : ((name == "\u0017\u0011\u0012\u0022\u000D\u0016\u0049\u0017\u0011\u0013\u000E\u0049\u001F\u0019\u0013\u0014") ? true : ((name == "\u0017\u0011\u0012\u0022\u000D\u0016\u0049\u0017\u0011\u0013\u000E\u0049\u000E\u0010\u0049\u0017\u0011\u0013\u000E") ? true : ((name == "\u0014\u000D\u000F\u001F\u0049\u0013\u000F\u0021\u000D") ? true : ((name == "\u0014\u000D\u000F\u001F\u0049\u0015\u000D\u0013\u000E\u0010\u0015\u000D") ? true : ((name == "\u0014\u000D\u000F\u001F\u0049\u000F\u0016\u0021\u000F\u0012\u0018\u000D") ? true : ((name == "\u0017\u0011\u0013\u000E\u0049\u001B\u0011\u000E\u0014\u0049\u0018\u000F\u001F\u000F\u0018\u0011\u000E\u001E") ? true : ((name == "\u0020\u0019\u001C\u0049\u001B\u0015\u0011\u000E\u000D\u0049\u0020\u001E\u000E\u000D") ? true : ((name == "\u0020\u0019\u001C\u0049\u001B\u0015\u0011\u000E\u000D\u0049\u0020\u001E\u000E\u000D\u0013") ? true : ((name == "\u0020\u0019\u001C\u0049\u0015\u000D\u000F\u0016\u0049\u0020\u001E\u000E\u000D\u0013") ? true : ((name == "\u0020\u0011\u000E\u0049\u000F\u0012\u0016") ? true : ((name == "\u0020\u0011\u000E\u0049\u0010\u0015") ? true : ((name == "\u0020\u0011\u000E\u0049\u0024\u0010\u0015") ? true : ((name == "\u0020\u0011\u000E\u0049\u0013\u0014\u0017") ? true : ((name == "\u0020\u0011\u000E\u0049\u0013\u0014\u0015") ? true : ((name == "\u0020\u0011\u000E\u0049\u0012\u0010\u000E") ? true : false)))))))))))))))))))));

    public static bool is_builtin(string name) => (is_string_builtin(name) ? true : (is_list_builtin(name) ? true : (is_char_builtin(name) ? true : (is_io_builtin(name) ? true : is_misc_builtin(name)))));

    public static EmitResult emit_helper_call_1(CodegenState st, List<IRExpr> args, string helper) => ((Func<EmitResult, EmitResult>)((r) => ((Func<CodegenState, EmitResult>)((st1) => ((Func<CodegenState, EmitResult>)((st2) => ((Func<EmitResult, EmitResult>)((tmp) => new EmitResult(state: st_append_text(tmp.state, mov_rr(tmp.reg, reg_rax())), reg: tmp.reg)))(alloc_temp(st2))))(emit_call_to(st1, helper))))(st_append_text(r.state, mov_rr(reg_rdi(), r.reg)))))(emit__x86_64_code_generator_emit_expr(st_set_tail_pos(st, false), args[(int)0L]));

    public static EmitResult emit_helper_call_2(CodegenState st, List<IRExpr> args, string helper) => ((Func<EmitResult, EmitResult>)((r0) => ((Func<EmitResult, EmitResult>)((loc) => ((Func<CodegenState, EmitResult>)((st1) => ((Func<EmitResult, EmitResult>)((r1) => ((Func<EmitResult, EmitResult>)((loaded) => ((Func<CodegenState, EmitResult>)((st2) => ((Func<CodegenState, EmitResult>)((st3) => ((Func<CodegenState, EmitResult>)((st4) => ((Func<EmitResult, EmitResult>)((tmp) => new EmitResult(state: st_append_text(tmp.state, mov_rr(tmp.reg, reg_rax())), reg: tmp.reg)))(alloc_temp(st4))))(emit_call_to(st3, helper))))(st_append_text(st2, mov_rr(reg_rdi(), loaded.reg)))))(st_append_text(loaded.state, mov_rr(reg_rsi(), r1.reg)))))(load_local(r1.state, loc.reg))))(emit__x86_64_code_generator_emit_expr(st1, args[(int)1L]))))(store_local(loc.state, loc.reg, r0.reg))))(alloc_local(r0.state))))(emit__x86_64_code_generator_emit_expr(st_set_tail_pos(st, false), args[(int)0L]));

    public static EmitResult emit_helper_call_3(CodegenState st, List<IRExpr> args, string helper) => ((Func<EmitResult, EmitResult>)((r0) => ((Func<EmitResult, EmitResult>)((loc0) => ((Func<CodegenState, EmitResult>)((st1) => ((Func<EmitResult, EmitResult>)((r1) => ((Func<EmitResult, EmitResult>)((loc1) => ((Func<CodegenState, EmitResult>)((st2) => ((Func<EmitResult, EmitResult>)((r2) => ((Func<CodegenState, EmitResult>)((st3) => ((Func<EmitResult, EmitResult>)((ld1) => ((Func<CodegenState, EmitResult>)((st4) => ((Func<EmitResult, EmitResult>)((ld0) => ((Func<CodegenState, EmitResult>)((st5) => ((Func<CodegenState, EmitResult>)((st6) => ((Func<EmitResult, EmitResult>)((tmp) => new EmitResult(state: st_append_text(tmp.state, mov_rr(tmp.reg, reg_rax())), reg: tmp.reg)))(alloc_temp(st6))))(emit_call_to(st5, helper))))(st_append_text(ld0.state, mov_rr(reg_rdi(), ld0.reg)))))(load_local(st4, loc0.reg))))(st_append_text(ld1.state, mov_rr(reg_rsi(), ld1.reg)))))(load_local(st3, loc1.reg))))(st_append_text(r2.state, mov_rr(reg_rdx(), r2.reg)))))(emit__x86_64_code_generator_emit_expr(st2, args[(int)2L]))))(store_local(loc1.state, loc1.reg, r1.reg))))(alloc_local(r1.state))))(emit__x86_64_code_generator_emit_expr(st1, args[(int)1L]))))(store_local(loc0.state, loc0.reg, r0.reg))))(alloc_local(r0.state))))(emit__x86_64_code_generator_emit_expr(st_set_tail_pos(st, false), args[(int)0L]));

    public static EmitResult emit_text_length_builtin(CodegenState st, List<IRExpr> args) => ((Func<EmitResult, EmitResult>)((r) => ((Func<EmitResult, EmitResult>)((tmp) => new EmitResult(state: st_append_text(tmp.state, mov_load(tmp.reg, r.reg, 0L)), reg: tmp.reg)))(alloc_temp(r.state))))(emit__x86_64_code_generator_emit_expr(st_set_tail_pos(st, false), args[(int)0L]));

    public static EmitResult emit_list_length_builtin(CodegenState st, List<IRExpr> args) => ((Func<EmitResult, EmitResult>)((r) => ((Func<EmitResult, EmitResult>)((tmp) => new EmitResult(state: st_append_text(tmp.state, mov_load(tmp.reg, r.reg, 0L)), reg: tmp.reg)))(alloc_temp(r.state))))(emit__x86_64_code_generator_emit_expr(st_set_tail_pos(st, false), args[(int)0L]));

    public static EmitResult emit_list_at_builtin(CodegenState st, List<IRExpr> args) => ((Func<EmitResult, EmitResult>)((r0) => ((Func<EmitResult, EmitResult>)((loc) => ((Func<CodegenState, EmitResult>)((st1) => ((Func<EmitResult, EmitResult>)((r1) => ((Func<EmitResult, EmitResult>)((list_loaded) => ((Func<EmitResult, EmitResult>)((addr) => ((Func<CodegenState, EmitResult>)((st2) => ((Func<CodegenState, EmitResult>)((st3) => ((Func<CodegenState, EmitResult>)((st4) => ((Func<EmitResult, EmitResult>)((rd) => new EmitResult(state: st_append_text(rd.state, mov_load(rd.reg, addr.reg, 8L)), reg: rd.reg)))(alloc_temp(st4))))(st_append_text(st3, add_rr(addr.reg, list_loaded.reg)))))(st_append_text(st2, shl_ri(addr.reg, 3L)))))(st_append_text(addr.state, mov_rr(addr.reg, r1.reg)))))(alloc_temp(list_loaded.state))))(load_local(r1.state, loc.reg))))(emit__x86_64_code_generator_emit_expr(st1, args[(int)1L]))))(store_local(loc.state, loc.reg, r0.reg))))(alloc_local(r0.state))))(emit__x86_64_code_generator_emit_expr(st_set_tail_pos(st, false), args[(int)0L]));

    public static EmitResult emit_char_at_builtin(CodegenState st, List<IRExpr> args) => ((Func<EmitResult, EmitResult>)((r0) => ((Func<EmitResult, EmitResult>)((loc) => ((Func<CodegenState, EmitResult>)((st1) => ((Func<EmitResult, EmitResult>)((r1) => ((Func<EmitResult, EmitResult>)((str_loaded) => ((Func<CodegenState, EmitResult>)((st2) => ((Func<CodegenState, EmitResult>)((st3) => new EmitResult(state: st3, reg: r1.reg)))(st_append_text(st2, movzx_byte(r1.reg, r1.reg, 8L)))))(st_append_text(str_loaded.state, add_rr(r1.reg, str_loaded.reg)))))(load_local(r1.state, loc.reg))))(emit__x86_64_code_generator_emit_expr(st1, args[(int)1L]))))(store_local(loc.state, loc.reg, r0.reg))))(alloc_local(r0.state))))(emit__x86_64_code_generator_emit_expr(st_set_tail_pos(st, false), args[(int)0L]));

    public static EmitResult emit_char_code_at_builtin(CodegenState st, List<IRExpr> args) => ((Func<EmitResult, EmitResult>)((r0) => ((Func<EmitResult, EmitResult>)((loc) => ((Func<CodegenState, EmitResult>)((st1) => ((Func<EmitResult, EmitResult>)((r1) => ((Func<EmitResult, EmitResult>)((text_loaded) => ((Func<EmitResult, EmitResult>)((rd) => ((Func<CodegenState, EmitResult>)((st2) => ((Func<CodegenState, EmitResult>)((st3) => ((Func<CodegenState, EmitResult>)((st4) => new EmitResult(state: st4, reg: rd.reg)))(st_append_text(st3, movzx_byte(rd.reg, rd.reg, 8L)))))(st_append_text(st2, add_rr(rd.reg, r1.reg)))))(st_append_text(rd.state, mov_rr(rd.reg, text_loaded.reg)))))(alloc_temp(text_loaded.state))))(load_local(r1.state, loc.reg))))(emit__x86_64_code_generator_emit_expr(st1, args[(int)1L]))))(store_local(loc.state, loc.reg, r0.reg))))(alloc_local(r0.state))))(emit__x86_64_code_generator_emit_expr(st_set_tail_pos(st, false), args[(int)0L]));

    public static EmitResult emit_char_to_text_builtin(CodegenState st, List<IRExpr> args) => ((Func<EmitResult, EmitResult>)((r) => ((Func<EmitResult, EmitResult>)((loc) => ((Func<CodegenState, EmitResult>)((st1) => ((Func<EmitResult, EmitResult>)((ptr_loc) => ((Func<EmitResult, EmitResult>)((ptr_tmp) => ((Func<CodegenState, EmitResult>)((st2) => ((Func<CodegenState, EmitResult>)((st3) => ((Func<CodegenState, EmitResult>)((st4) => ((Func<CodegenState, EmitResult>)((st5) => ((Func<EmitResult, EmitResult>)((ptr_loaded) => ((Func<CodegenState, EmitResult>)((st6) => ((Func<EmitResult, EmitResult>)((code_loaded) => ((Func<EmitResult, EmitResult>)((ptr_loaded2) => ((Func<CodegenState, EmitResult>)((st7) => ((Func<EmitResult, EmitResult>)((result) => new EmitResult(state: result.state, reg: result.reg)))(load_local(st7, ptr_loc.reg))))(st_append_text(ptr_loaded2.state, mov_store_byte(ptr_loaded2.reg, code_loaded.reg, 8L)))))(load_local(code_loaded.state, ptr_loc.reg))))(load_local(st6, loc.reg))))(st_append_text(ptr_loaded.state, mov_store(ptr_loaded.reg, reg_r11(), 0L)))))(load_local(st5, ptr_loc.reg))))(st_append_text(st4, li(reg_r11(), 1L)))))(st_append_text(st3, add_ri(reg_r10(), 16L)))))(store_local(st2, ptr_loc.reg, ptr_tmp.reg))))(st_append_text(ptr_tmp.state, mov_rr(ptr_tmp.reg, reg_r10())))))(alloc_temp(ptr_loc.state))))(alloc_local(st1))))(store_local(loc.state, loc.reg, r.reg))))(alloc_local(r.state))))(emit__x86_64_code_generator_emit_expr(st_set_tail_pos(st, false), args[(int)0L]));

    public static EmitResult emit_is_letter_builtin(CodegenState st, List<IRExpr> args) => ((Func<EmitResult, EmitResult>)((r) => ((Func<CodegenState, EmitResult>)((st1) => ((Func<CodegenState, EmitResult>)((st2) => ((Func<CodegenState, EmitResult>)((st3) => ((Func<CodegenState, EmitResult>)((st4) => new EmitResult(state: st4, reg: r.reg)))(st_append_text(st3, movzx_byte_self(r.reg)))))(st_append_text(st2, setcc(cc_be(), r.reg)))))(st_append_text(st1, cmp_ri(r.reg, 51L)))))(st_append_text(r.state, sub_ri(r.reg, 13L)))))(emit__x86_64_code_generator_emit_expr(st_set_tail_pos(st, false), args[(int)0L]));

    public static EmitResult emit_is_digit_builtin(CodegenState st, List<IRExpr> args) => ((Func<EmitResult, EmitResult>)((r) => ((Func<CodegenState, EmitResult>)((st1) => ((Func<CodegenState, EmitResult>)((st2) => ((Func<CodegenState, EmitResult>)((st3) => ((Func<CodegenState, EmitResult>)((st4) => new EmitResult(state: st4, reg: r.reg)))(st_append_text(st3, movzx_byte_self(r.reg)))))(st_append_text(st2, setcc(cc_be(), r.reg)))))(st_append_text(st1, cmp_ri(r.reg, 9L)))))(st_append_text(r.state, sub_ri(r.reg, 3L)))))(emit__x86_64_code_generator_emit_expr(st_set_tail_pos(st, false), args[(int)0L]));

    public static EmitResult emit_is_whitespace_builtin(CodegenState st, List<IRExpr> args) => ((Func<EmitResult, EmitResult>)((r) => ((Func<CodegenState, EmitResult>)((st1) => ((Func<CodegenState, EmitResult>)((st2) => ((Func<CodegenState, EmitResult>)((st3) => new EmitResult(state: st3, reg: r.reg)))(st_append_text(st2, movzx_byte_self(r.reg)))))(st_append_text(st1, setcc(cc_be(), r.reg)))))(st_append_text(r.state, cmp_ri(r.reg, 2L)))))(emit__x86_64_code_generator_emit_expr(st_set_tail_pos(st, false), args[(int)0L]));

    public static EmitResult emit_negate_builtin(CodegenState st, List<IRExpr> args) => ((Func<EmitResult, EmitResult>)((r) => ((Func<EmitResult, EmitResult>)((rd) => ((Func<CodegenState, EmitResult>)((st1) => ((Func<CodegenState, EmitResult>)((st2) => new EmitResult(state: st2, reg: rd.reg)))(st_append_text(st1, neg_r(rd.reg)))))(st_append_text(rd.state, mov_rr(rd.reg, r.reg)))))(alloc_temp(r.state))))(emit__x86_64_code_generator_emit_expr(st_set_tail_pos(st, false), args[(int)0L]));

    public static EmitResult emit_get_args_builtin(CodegenState st) => ((Func<EmitResult, EmitResult>)((rd) => ((Func<CodegenState, EmitResult>)((st1) => ((Func<CodegenState, EmitResult>)((st2) => ((Func<CodegenState, EmitResult>)((st3) => ((Func<CodegenState, EmitResult>)((st4) => ((Func<CodegenState, EmitResult>)((st5) => ((Func<CodegenState, EmitResult>)((st6) => new EmitResult(state: st6, reg: rd.reg)))(st_append_text(st5, add_ri(reg_r10(), 8L)))))(st_append_text(st4, mov_store(reg_r10(), reg_r11(), 0L)))))(st_append_text(st3, mov_rr(rd.reg, reg_r10())))))(st_append_text(st2, add_ri(reg_r10(), 8L)))))(st_append_text(st1, mov_store(reg_r10(), reg_r11(), 0L)))))(st_append_text(rd.state, li(reg_r11(), 0L)))))(alloc_temp(st));

    public static EmitResult emit_current_dir_builtin(CodegenState st) => ((Func<EmitResult, EmitResult>)((rd) => ((Func<CodegenState, EmitResult>)((st1) => ((Func<CodegenState, EmitResult>)((st2) => ((Func<CodegenState, EmitResult>)((st3) => ((Func<CodegenState, EmitResult>)((st4) => new EmitResult(state: st4, reg: rd.reg)))(st_append_text(st3, add_ri(reg_r10(), 8L)))))(st_append_text(st2, mov_store(reg_r10(), reg_r11(), 0L)))))(st_append_text(st1, li(reg_r11(), 0L)))))(st_append_text(rd.state, mov_rr(rd.reg, reg_r10())))))(alloc_temp(st));

    public static EmitResult emit_file_exists_builtin(CodegenState st, List<IRExpr> args) => ((Func<EmitResult, EmitResult>)((r) => ((Func<EmitResult, EmitResult>)((rd) => ((Func<CodegenState, EmitResult>)((st1) => new EmitResult(state: st1, reg: rd.reg)))(st_append_text(rd.state, li(rd.reg, 1L)))))(alloc_temp(r.state))))(emit__x86_64_code_generator_emit_expr(st_set_tail_pos(st, false), args[(int)0L]));

    public static CodegenState emit_substring_alloc(CodegenState st, long str_loc, long start_loc, long len_loc) => ((Func<EmitResult, CodegenState>)((len_loaded) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => st_append_text(st2, add_rr(reg_r10(), reg_r11()))))(st_append_text(st1, and_ri(reg_r11(), (0L - 8L))))))(st_append_text(st0, add_ri(reg_r11(), 15L)))))(st_append_text(len_loaded.state, mov_rr(reg_r11(), len_loaded.reg)))))(load_local(st, len_loc));

    public static CodegenState emit_substring_copy(CodegenState st, long str_loc, long start_loc, long len_loc, long ptr_loc) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<long, CodegenState>)((sub_loop) => ((Func<EmitResult, CodegenState>)((len_ld) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<long, CodegenState>)((sub_exit_pos) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<EmitResult, CodegenState>)((src_ld) => ((Func<EmitResult, CodegenState>)((start_ld) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<EmitResult, CodegenState>)((ptr_ld) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => patch_jcc_at(st11, sub_exit_pos, st11.text_len)))(st_append_text(st10, jmp((sub_loop - (st10.text_len + 5L)))))))(st_append_text(st9, add_ri(reg_r11(), 1L)))))(st_append_text(st8, mov_store_byte(reg_rdx(), reg_rsi(), 8L)))))(st_append_text(st7, add_rr(reg_rdx(), reg_r11())))))(st_append_text(ptr_ld.state, mov_rr(reg_rdx(), ptr_ld.reg)))))(load_local(st6, ptr_loc))))(st_append_text(st5, movzx_byte(reg_rsi(), reg_rsi(), 8L)))))(st_append_text(st4, add_rr(reg_rsi(), reg_r11())))))(st_append_text(st3, add_rr(reg_rsi(), start_ld.reg)))))(st_append_text(start_ld.state, mov_rr(reg_rsi(), src_ld.reg)))))(load_local(src_ld.state, start_loc))))(load_local(st2, str_loc))))(st_append_text(st1, jcc(cc_ge(), 0L)))))(st1.text_len)))(st_append_text(len_ld.state, cmp_rr(reg_r11(), len_ld.reg)))))(load_local(st0, len_loc))))(st0.text_len)))(st_append_text(st, li(reg_r11(), 0L)));

    public static EmitResult emit_substring_builtin(CodegenState st, List<IRExpr> args) => ((Func<EmitResult, EmitResult>)((r0) => ((Func<EmitResult, EmitResult>)((loc0) => ((Func<CodegenState, EmitResult>)((st1) => ((Func<EmitResult, EmitResult>)((r1) => ((Func<EmitResult, EmitResult>)((loc1) => ((Func<CodegenState, EmitResult>)((st2) => ((Func<EmitResult, EmitResult>)((r2) => ((Func<EmitResult, EmitResult>)((loc2) => ((Func<CodegenState, EmitResult>)((st3) => ((Func<EmitResult, EmitResult>)((ptr_loc) => ((Func<EmitResult, EmitResult>)((ptr_tmp) => ((Func<CodegenState, EmitResult>)((st4) => ((Func<CodegenState, EmitResult>)((st5) => ((Func<EmitResult, EmitResult>)((len_ld) => ((Func<EmitResult, EmitResult>)((ptr_ld) => ((Func<CodegenState, EmitResult>)((st6) => ((Func<CodegenState, EmitResult>)((st7) => ((Func<CodegenState, EmitResult>)((st8) => ((Func<EmitResult, EmitResult>)((result) => new EmitResult(state: result.state, reg: result.reg)))(load_local(st8, ptr_loc.reg))))(emit_substring_copy(st7, loc0.reg, loc1.reg, loc2.reg, ptr_loc.reg))))(emit_substring_alloc(st6, loc0.reg, loc1.reg, loc2.reg))))(st_append_text(ptr_ld.state, mov_store(ptr_ld.reg, len_ld.reg, 0L)))))(load_local(len_ld.state, ptr_loc.reg))))(load_local(st5, loc2.reg))))(store_local(st4, ptr_loc.reg, ptr_tmp.reg))))(st_append_text(ptr_tmp.state, mov_rr(ptr_tmp.reg, reg_r10())))))(alloc_temp(ptr_loc.state))))(alloc_local(st3))))(store_local(loc2.state, loc2.reg, r2.reg))))(alloc_local(r2.state))))(emit__x86_64_code_generator_emit_expr(st2, args[(int)2L]))))(store_local(loc1.state, loc1.reg, r1.reg))))(alloc_local(r1.state))))(emit__x86_64_code_generator_emit_expr(st1, args[(int)1L]))))(store_local(loc0.state, loc0.reg, r0.reg))))(alloc_local(r0.state))))(emit__x86_64_code_generator_emit_expr(st_set_tail_pos(st, false), args[(int)0L]));

    public static EmitResult emit_print_line_builtin(CodegenState st, List<IRExpr> args) => ((Func<EmitResult, EmitResult>)((r) => ((Func<CodegenState, EmitResult>)((st1) => ((Func<EmitResult, EmitResult>)((rd) => new EmitResult(state: st_append_text(rd.state, li(rd.reg, 0L)), reg: rd.reg)))(alloc_temp(st1))))(emit_print_text(r.state, r.reg))))(emit__x86_64_code_generator_emit_expr(st_set_tail_pos(st, false), args[(int)0L]));

    public static EmitResult emit_read_file_builtin(CodegenState st, List<IRExpr> args) => ((Func<EmitResult, EmitResult>)((r) => ((Func<CodegenState, EmitResult>)((st1) => ((Func<CodegenState, EmitResult>)((st2) => ((Func<EmitResult, EmitResult>)((rd) => new EmitResult(state: st_append_text(rd.state, mov_rr(rd.reg, reg_rax())), reg: rd.reg)))(alloc_temp(st2))))(emit_call_to(st1, "\u0055\u0055\u0020\u000F\u0015\u000D\u0055\u001A\u000D\u000E\u000F\u0017\u0055\u0015\u000D\u000F\u0016\u0055\u0013\u000D\u0015\u0011\u000F\u0017"))))(st_append_text(r.state, mov_rr(reg_rdi(), r.reg)))))(emit__x86_64_code_generator_emit_expr(st_set_tail_pos(st, false), args[(int)0L]));

    public static EmitResult emit_write_file_builtin(CodegenState st, List<IRExpr> args) => ((Func<EmitResult, EmitResult>)((r0) => ((Func<EmitResult, EmitResult>)((r1) => ((Func<CodegenState, EmitResult>)((st1) => ((Func<EmitResult, EmitResult>)((rd) => new EmitResult(state: st_append_text(rd.state, li(rd.reg, 0L)), reg: rd.reg)))(alloc_temp(st1))))(emit_print_text_no_newline(r1.state, r1.reg))))(emit__x86_64_code_generator_emit_expr(r0.state, args[(int)1L]))))(emit__x86_64_code_generator_emit_expr(st_set_tail_pos(st, false), args[(int)0L]));

    public static EmitResult emit_read_line_builtin(CodegenState st) => ((Func<CodegenState, EmitResult>)((st1) => ((Func<EmitResult, EmitResult>)((rd) => new EmitResult(state: st_append_text(rd.state, mov_rr(rd.reg, reg_rax())), reg: rd.reg)))(alloc_temp(st1))))(emit_call_to(st, "\u0055\u0055\u0015\u000D\u000F\u0016\u0055\u0017\u0011\u0012\u000D"));

    public static EmitResult emit_write_binary_builtin(CodegenState st, List<IRExpr> args) => ((Func<EmitResult, EmitResult>)((r) => ((Func<CodegenState, EmitResult>)((st1) => ((Func<CodegenState, EmitResult>)((st2) => ((Func<EmitResult, EmitResult>)((rd) => new EmitResult(state: st_append_text(rd.state, li(rd.reg, 0L)), reg: rd.reg)))(alloc_temp(st2))))(emit_call_to(st1, "\u0055\u0055\u001B\u0015\u0011\u000E\u000D\u0055\u0020\u0011\u0012\u000F\u0015\u001E"))))(st_append_text(r.state, mov_rr(reg_rdi(), r.reg)))))(emit__x86_64_code_generator_emit_expr(st_set_tail_pos(st, false), args[(int)0L]));

    public static EmitResult emit_linked_list_empty_builtin(CodegenState st, List<IRExpr> args) => ((Func<EmitResult, EmitResult>)((rd) => new EmitResult(state: st_append_text(rd.state, li(rd.reg, 0L)), reg: rd.reg)))(alloc_temp(st));

    public static EmitResult emit_linked_list_push_builtin(CodegenState st, List<IRExpr> args) => ((Func<EmitResult, EmitResult>)((list_result) => ((Func<EmitResult, EmitResult>)((list_loc) => ((Func<CodegenState, EmitResult>)((st1) => ((Func<EmitResult, EmitResult>)((val_result) => ((Func<EmitResult, EmitResult>)((val_loc) => ((Func<CodegenState, EmitResult>)((st2) => ((Func<EmitResult, EmitResult>)((ptr) => ((Func<CodegenState, EmitResult>)((st3) => ((Func<EmitResult, EmitResult>)((vr) => ((Func<CodegenState, EmitResult>)((st4) => ((Func<EmitResult, EmitResult>)((lr) => ((Func<CodegenState, EmitResult>)((st5) => ((Func<CodegenState, EmitResult>)((st6) => new EmitResult(state: st6, reg: ptr.reg)))(st_append_text(st5, add_ri(reg_r10(), 16L)))))(st_append_text(lr.state, mov_store(ptr.reg, lr.reg, 8L)))))(load_local(st4, list_loc.reg))))(st_append_text(vr.state, mov_store(ptr.reg, vr.reg, 0L)))))(load_local(st3, val_loc.reg))))(st_append_text(ptr.state, mov_rr(ptr.reg, reg_r10())))))(alloc_temp(st2))))(store_local(val_loc.state, val_loc.reg, val_result.reg))))(alloc_local(val_result.state))))(emit__x86_64_code_generator_emit_expr(st1, args[(int)1L]))))(store_local(list_loc.state, list_loc.reg, list_result.reg))))(alloc_local(list_result.state))))(emit__x86_64_code_generator_emit_expr(st_set_tail_pos(st, false), args[(int)0L]));

    public static EmitResult emit_linked_list_to_list_builtin(CodegenState st, List<IRExpr> args) => ((Func<EmitResult, EmitResult>)((head_result) => ((Func<CodegenState, EmitResult>)((st1) => ((Func<CodegenState, EmitResult>)((st2) => ((Func<EmitResult, EmitResult>)((rd) => new EmitResult(state: st_append_text(rd.state, mov_rr(rd.reg, reg_rax())), reg: rd.reg)))(alloc_temp(st2))))(emit_call_to(st1, "\u0055\u0055\u0017\u0011\u0012\u0022\u000D\u0016\u0055\u0017\u0011\u0013\u000E\u0055\u000E\u0010\u0055\u0017\u0011\u0013\u000E"))))(st_append_text(head_result.state, mov_rr(reg_rdi(), head_result.reg)))))(emit__x86_64_code_generator_emit_expr(st_set_tail_pos(st, false), args[(int)0L]));

    public static EmitResult emit_record_set_builtin(CodegenState st, List<IRExpr> args) => ((Func<EmitResult, EmitResult>)((rec_result) => ((Func<EmitResult, EmitResult>)((rec_loc) => ((Func<CodegenState, EmitResult>)((st1) => ((Func<string, EmitResult>)((field_name) => ((Func<CodexType, EmitResult>)((rec_ty) => ((Func<EmitResult, EmitResult>)((val_result) => ((Func<EmitResult, EmitResult>)((loaded) => rec_ty switch { RecordTy(var rname, var rfields) => (EmitResult)(((Func<long, EmitResult>)((field_idx) => new EmitResult(state: st_append_text(loaded.state, mov_store(loaded.reg, val_result.reg, (field_idx * 8L))), reg: loaded.reg)))(find_record_field_index(rfields, field_name, 0L))), _ => (EmitResult)(((Func<CodegenState, EmitResult>)((st_err) => new EmitResult(state: st_append_text(st_err, new List<long> { 15L, 11L }), reg: loaded.reg)))(st_add_error(loaded.state, cdx_ir_error(), ("\u0015\u000D\u0018\u0010\u0015\u0016\u0049\u0013\u000D\u000E\u0045\u0002\u0019\u0012\u0015\u000D\u0013\u0010\u0017\u0021\u000D\u0016\u0002\u000E\u001E\u001F\u000D\u0002\u001C\u0010\u0015\u0002\u001C\u0011\u000D\u0017\u0016\u0002\u0047" + (field_name + "\u0047")), ir_expr_span(args[(int)0L])))), }))(load_local(val_result.state, rec_loc.reg))))(emit__x86_64_code_generator_emit_expr(st1, args[(int)2L]))))(resolve_constructed_ty(st, ir_expr_type(args[(int)0L])))))(args[(int)1L] switch { IrTextLit(var s, var sp) => (string)(s), _ => (string)(""), })))(store_local(rec_loc.state, rec_loc.reg, rec_result.reg))))(alloc_local(rec_result.state))))(emit__x86_64_code_generator_emit_expr(st_set_tail_pos(st, false), args[(int)0L]));

    public static EmitResult emit_heap_save_builtin(CodegenState st) => ((Func<EmitResult, EmitResult>)((rd) => new EmitResult(state: st_append_text(rd.state, mov_rr(rd.reg, reg_r10())), reg: rd.reg)))(alloc_temp(st));

    public static EmitResult emit_heap_restore_builtin(CodegenState st, List<IRExpr> args) => ((Func<EmitResult, EmitResult>)((r) => ((Func<EmitResult, EmitResult>)((rd) => new EmitResult(state: st_append_text(rd.state, li(rd.reg, 0L)), reg: rd.reg)))(alloc_temp(r.state))))(emit__x86_64_code_generator_emit_expr(st_set_tail_pos(st, false), args[(int)0L]));

    public static EmitResult emit_heap_advance_builtin(CodegenState st, List<IRExpr> args) => ((Func<EmitResult, EmitResult>)((r) => ((Func<EmitResult, EmitResult>)((rd) => ((Func<CodegenState, EmitResult>)((st1) => new EmitResult(state: st_append_text(st1, li(rd.reg, 0L)), reg: rd.reg)))(st_append_text(rd.state, add_rr(reg_r10(), r.reg)))))(alloc_temp(r.state))))(emit__x86_64_code_generator_emit_expr(st_set_tail_pos(st, false), args[(int)0L]));

    public static EmitResult emit_list_with_capacity_builtin(CodegenState st, List<IRExpr> args) => ((Func<EmitResult, EmitResult>)((cap_result) => ((Func<EmitResult, EmitResult>)((rd) => ((Func<CodegenState, EmitResult>)((st1) => ((Func<CodegenState, EmitResult>)((st2) => ((Func<CodegenState, EmitResult>)((st3) => ((Func<EmitResult, EmitResult>)((zero_tmp) => ((Func<CodegenState, EmitResult>)((st4) => ((Func<CodegenState, EmitResult>)((st5) => ((Func<EmitResult, EmitResult>)((adv_tmp) => ((Func<CodegenState, EmitResult>)((st6) => ((Func<CodegenState, EmitResult>)((st7) => ((Func<CodegenState, EmitResult>)((st8) => new EmitResult(state: st_append_text(st8, add_rr(reg_r10(), adv_tmp.reg)), reg: rd.reg)))(st_append_text(st7, add_ri(adv_tmp.reg, 8L)))))(st_append_text(st6, shl_ri(adv_tmp.reg, 3L)))))(st_append_text(adv_tmp.state, mov_rr(adv_tmp.reg, cap_result.reg)))))(alloc_temp(st5))))(st_append_text(st4, mov_store(reg_r10(), zero_tmp.reg, 0L)))))(st_append_text(zero_tmp.state, li(zero_tmp.reg, 0L)))))(alloc_temp(st3))))(st_append_text(st2, mov_rr(rd.reg, reg_r10())))))(st_append_text(st1, add_ri(reg_r10(), 8L)))))(st_append_text(rd.state, mov_store(reg_r10(), cap_result.reg, 0L)))))(alloc_temp(cap_result.state))))(emit__x86_64_code_generator_emit_expr(st_set_tail_pos(st, false), args[(int)0L]));

    public static EmitResult emit_buf_write_byte_builtin(CodegenState st, List<IRExpr> args) => ((Func<EmitResult, EmitResult>)((base_result) => ((Func<EmitResult, EmitResult>)((base_loc) => ((Func<CodegenState, EmitResult>)((st1) => ((Func<EmitResult, EmitResult>)((off_result) => ((Func<EmitResult, EmitResult>)((off_loc) => ((Func<CodegenState, EmitResult>)((st2) => ((Func<EmitResult, EmitResult>)((byte_result) => ((Func<EmitResult, EmitResult>)((byte_loc) => ((Func<CodegenState, EmitResult>)((st3) => ((Func<EmitResult, EmitResult>)((addr) => ((Func<EmitResult, EmitResult>)((base2) => ((Func<EmitResult, EmitResult>)((off2) => ((Func<CodegenState, EmitResult>)((st4) => ((Func<CodegenState, EmitResult>)((st5) => ((Func<EmitResult, EmitResult>)((byte2) => ((Func<CodegenState, EmitResult>)((st6) => ((Func<EmitResult, EmitResult>)((rd) => ((Func<EmitResult, EmitResult>)((off3) => ((Func<CodegenState, EmitResult>)((st7) => new EmitResult(state: st_append_text(st7, add_ri(rd.reg, 1L)), reg: rd.reg)))(st_append_text(off3.state, mov_rr(rd.reg, off3.reg)))))(load_local(rd.state, off_loc.reg))))(alloc_temp(st6))))(st_append_text(byte2.state, mov_store_byte(addr.reg, byte2.reg, 0L)))))(load_local(st5, byte_loc.reg))))(st_append_text(st4, add_rr(addr.reg, off2.reg)))))(st_append_text(off2.state, mov_rr(addr.reg, base2.reg)))))(load_local(base2.state, off_loc.reg))))(load_local(addr.state, base_loc.reg))))(alloc_temp(st3))))(store_local(byte_loc.state, byte_loc.reg, byte_result.reg))))(alloc_local(byte_result.state))))(emit__x86_64_code_generator_emit_expr(st2, args[(int)2L]))))(store_local(off_loc.state, off_loc.reg, off_result.reg))))(alloc_local(off_result.state))))(emit__x86_64_code_generator_emit_expr(st1, args[(int)1L]))))(store_local(base_loc.state, base_loc.reg, base_result.reg))))(alloc_local(base_result.state))))(emit__x86_64_code_generator_emit_expr(st_set_tail_pos(st, false), args[(int)0L]));

    public static EmitResult emit_buf_write_bytes_builtin(CodegenState st, List<IRExpr> args) => ((Func<EmitResult, EmitResult>)((base_result) => ((Func<EmitResult, EmitResult>)((base_loc) => ((Func<CodegenState, EmitResult>)((st1) => ((Func<EmitResult, EmitResult>)((off_result) => ((Func<EmitResult, EmitResult>)((off_loc) => ((Func<CodegenState, EmitResult>)((st2) => ((Func<EmitResult, EmitResult>)((list_result) => ((Func<CodegenState, EmitResult>)((st3) => ((Func<EmitResult, EmitResult>)((off2) => ((Func<CodegenState, EmitResult>)((st4) => ((Func<EmitResult, EmitResult>)((base2) => ((Func<CodegenState, EmitResult>)((st5) => ((Func<CodegenState, EmitResult>)((st6) => ((Func<EmitResult, EmitResult>)((rd) => new EmitResult(state: st_append_text(rd.state, mov_rr(rd.reg, reg_rax())), reg: rd.reg)))(alloc_temp(st6))))(emit_call_to(st5, "\u0055\u0055\u0020\u0019\u001C\u0055\u001B\u0015\u0011\u000E\u000D\u0055\u0020\u001E\u000E\u000D\u0013"))))(st_append_text(base2.state, mov_rr(reg_rdi(), base2.reg)))))(load_local(st4, base_loc.reg))))(st_append_text(off2.state, mov_rr(reg_rsi(), off2.reg)))))(load_local(st3, off_loc.reg))))(st_append_text(list_result.state, mov_rr(reg_rdx(), list_result.reg)))))(emit__x86_64_code_generator_emit_expr(st2, args[(int)2L]))))(store_local(off_loc.state, off_loc.reg, off_result.reg))))(alloc_local(off_result.state))))(emit__x86_64_code_generator_emit_expr(st1, args[(int)1L]))))(store_local(base_loc.state, base_loc.reg, base_result.reg))))(alloc_local(base_result.state))))(emit__x86_64_code_generator_emit_expr(st_set_tail_pos(st, false), args[(int)0L]));

    public static EmitResult emit_buf_read_bytes_builtin(CodegenState st, List<IRExpr> args) => ((Func<EmitResult, EmitResult>)((base_result) => ((Func<EmitResult, EmitResult>)((base_loc) => ((Func<CodegenState, EmitResult>)((st1) => ((Func<EmitResult, EmitResult>)((off_result) => ((Func<EmitResult, EmitResult>)((off_loc) => ((Func<CodegenState, EmitResult>)((st2) => ((Func<EmitResult, EmitResult>)((count_result) => ((Func<CodegenState, EmitResult>)((st3) => ((Func<EmitResult, EmitResult>)((off2) => ((Func<CodegenState, EmitResult>)((st4) => ((Func<EmitResult, EmitResult>)((base2) => ((Func<CodegenState, EmitResult>)((st5) => ((Func<CodegenState, EmitResult>)((st6) => ((Func<EmitResult, EmitResult>)((rd) => new EmitResult(state: st_append_text(rd.state, mov_rr(rd.reg, reg_rax())), reg: rd.reg)))(alloc_temp(st6))))(emit_call_to(st5, "\u0055\u0055\u0020\u0019\u001C\u0055\u0015\u000D\u000F\u0016\u0055\u0020\u001E\u000E\u000D\u0013"))))(st_append_text(base2.state, mov_rr(reg_rdi(), base2.reg)))))(load_local(st4, base_loc.reg))))(st_append_text(off2.state, mov_rr(reg_rsi(), off2.reg)))))(load_local(st3, off_loc.reg))))(st_append_text(count_result.state, mov_rr(reg_rdx(), count_result.reg)))))(emit__x86_64_code_generator_emit_expr(st2, args[(int)2L]))))(store_local(off_loc.state, off_loc.reg, off_result.reg))))(alloc_local(off_result.state))))(emit__x86_64_code_generator_emit_expr(st1, args[(int)1L]))))(store_local(base_loc.state, base_loc.reg, base_result.reg))))(alloc_local(base_result.state))))(emit__x86_64_code_generator_emit_expr(st_set_tail_pos(st, false), args[(int)0L]));

    public static EmitResult emit_bit_op_2(CodegenState st, List<IRExpr> args, Func<long, Func<long, List<long>>> op_bytes) => ((Func<EmitResult, EmitResult>)((r0) => ((Func<EmitResult, EmitResult>)((loc0) => ((Func<CodegenState, EmitResult>)((st1) => ((Func<EmitResult, EmitResult>)((r1) => ((Func<EmitResult, EmitResult>)((ld0) => ((Func<EmitResult, EmitResult>)((rd) => new EmitResult(state: st_append_text(rd.state, Enumerable.Concat(mov_rr(rd.reg, ld0.reg), op_bytes(rd.reg)(r1.reg)).ToList()), reg: rd.reg)))(alloc_temp(ld0.state))))(load_local(r1.state, loc0.reg))))(emit__x86_64_code_generator_emit_expr(st1, args[(int)1L]))))(store_local(loc0.state, loc0.reg, r0.reg))))(alloc_local(r0.state))))(emit__x86_64_code_generator_emit_expr(st_set_tail_pos(st, false), args[(int)0L]));

    public static EmitResult emit_bit_shift(CodegenState st, List<IRExpr> args, Func<long, List<long>> shift_bytes) => ((Func<EmitResult, EmitResult>)((r0) => ((Func<EmitResult, EmitResult>)((loc0) => ((Func<CodegenState, EmitResult>)((st1) => ((Func<EmitResult, EmitResult>)((r1) => ((Func<CodegenState, EmitResult>)((st2) => ((Func<EmitResult, EmitResult>)((ld0) => ((Func<EmitResult, EmitResult>)((rd) => new EmitResult(state: st_append_text(rd.state, Enumerable.Concat(mov_rr(rd.reg, ld0.reg), shift_bytes(rd.reg)).ToList()), reg: rd.reg)))(alloc_temp(ld0.state))))(load_local(st2, loc0.reg))))(st_append_text(r1.state, mov_rr(reg_rcx(), r1.reg)))))(emit__x86_64_code_generator_emit_expr(st1, args[(int)1L]))))(store_local(loc0.state, loc0.reg, r0.reg))))(alloc_local(r0.state))))(emit__x86_64_code_generator_emit_expr(st_set_tail_pos(st, false), args[(int)0L]));

    public static EmitResult emit_bit_not(CodegenState st, List<IRExpr> args) => ((Func<EmitResult, EmitResult>)((r0) => ((Func<EmitResult, EmitResult>)((rd) => new EmitResult(state: st_append_text(rd.state, Enumerable.Concat(mov_rr(rd.reg, r0.reg), not_r(rd.reg)).ToList()), reg: rd.reg)))(alloc_temp(r0.state))))(emit__x86_64_code_generator_emit_expr(st_set_tail_pos(st, false), args[(int)0L]));

    public static EmitResult emit__x86_64_code_generator_emit_builtin(CodegenState st, string name, List<IRExpr> args) => ((name == "\u001F\u0015\u0011\u0012\u000E\u0049\u0017\u0011\u0012\u000D") ? emit_print_line_builtin(st, args) : ((name == "\u0015\u000D\u000F\u0016\u0049\u001C\u0011\u0017\u000D") ? emit_read_file_builtin(st, args) : ((name == "\u001B\u0015\u0011\u000E\u000D\u0049\u001C\u0011\u0017\u000D") ? emit_write_file_builtin(st, args) : ((name == "\u0015\u000D\u000F\u0016\u0049\u0017\u0011\u0012\u000D") ? emit_read_line_builtin(st) : ((name == "\u001B\u0015\u0011\u000E\u000D\u0049\u0020\u0011\u0012\u000F\u0015\u001E") ? emit_write_binary_builtin(st, args) : ((name == "\u000E\u000D\u0024\u000E\u0049\u0017\u000D\u0012\u001D\u000E\u0014") ? emit_text_length_builtin(st, args) : ((name == "\u0011\u0012\u000E\u000D\u001D\u000D\u0015\u0049\u000E\u0010\u0049\u000E\u000D\u0024\u000E") ? emit_helper_call_1(st, args, "\u0055\u0055\u0011\u000E\u0010\u000F") : ((name == "\u0013\u0014\u0010\u001B") ? emit_show_builtin(st, args) : ((name == "\u000E\u000D\u0024\u000E\u0049\u000E\u0010\u0049\u0011\u0012\u000E\u000D\u001D\u000D\u0015") ? emit_helper_call_1(st, args, "\u0055\u0055\u000E\u000D\u0024\u000E\u0055\u000E\u0010\u0055\u0011\u0012\u000E") : ((name == "\u000E\u000D\u0024\u000E\u0049\u0015\u000D\u001F\u0017\u000F\u0018\u000D") ? emit_helper_call_3(st, args, "\u0055\u0055\u0013\u000E\u0015\u0055\u0015\u000D\u001F\u0017\u000F\u0018\u000D") : ((name == "\u000E\u000D\u0024\u000E\u0049\u0018\u0010\u0012\u000E\u000F\u0011\u0012\u0013") ? emit_helper_call_2(st, args, "\u0055\u0055\u000E\u000D\u0024\u000E\u0055\u0018\u0010\u0012\u000E\u000F\u0011\u0012\u0013") : ((name == "\u000E\u000D\u0024\u000E\u0049\u0013\u000E\u000F\u0015\u000E\u0013\u0049\u001B\u0011\u000E\u0014") ? emit_helper_call_2(st, args, "\u0055\u0055\u000E\u000D\u0024\u000E\u0055\u0013\u000E\u000F\u0015\u000E\u0013\u0055\u001B\u0011\u000E\u0014") : ((name == "\u000E\u000D\u0024\u000E\u0049\u0018\u0010\u001A\u001F\u000F\u0015\u000D") ? emit_helper_call_2(st, args, "\u0055\u0055\u000E\u000D\u0024\u000E\u0055\u0018\u0010\u001A\u001F\u000F\u0015\u000D") : ((name == "\u000E\u000D\u0024\u000E\u0049\u0018\u0010\u0012\u0018\u000F\u000E\u0049\u0017\u0011\u0013\u000E") ? emit_helper_call_1(st, args, "\u0055\u0055\u000E\u000D\u0024\u000E\u0055\u0018\u0010\u0012\u0018\u000F\u000E\u0055\u0017\u0011\u0013\u000E") : ((name == "\u000E\u000D\u0024\u000E\u0049\u0013\u001F\u0017\u0011\u000E") ? emit_helper_call_2(st, args, "\u0055\u0055\u000E\u000D\u0024\u000E\u0055\u0013\u001F\u0017\u0011\u000E") : ((name == "\u0013\u0019\u0020\u0013\u000E\u0015\u0011\u0012\u001D") ? emit_substring_builtin(st, args) : ((name == "\u0017\u0011\u0013\u000E\u0049\u0017\u000D\u0012\u001D\u000E\u0014") ? emit_list_length_builtin(st, args) : ((name == "\u0017\u0011\u0013\u000E\u0049\u000F\u000E") ? emit_list_at_builtin(st, args) : ((name == "\u0017\u0011\u0013\u000E\u0049\u0018\u0010\u0012\u0013") ? emit_helper_call_2(st, args, "\u0055\u0055\u0017\u0011\u0013\u000E\u0055\u0018\u0010\u0012\u0013") : ((name == "\u0017\u0011\u0013\u000E\u0049\u000F\u001F\u001F\u000D\u0012\u0016") ? emit_helper_call_2(st, args, "\u0055\u0055\u0017\u0011\u0013\u000E\u0055\u000F\u001F\u001F\u000D\u0012\u0016") : ((name == "\u0017\u0011\u0013\u000E\u0049\u0013\u0012\u0010\u0018") ? emit_helper_call_2(st, args, "\u0055\u0055\u0017\u0011\u0013\u000E\u0055\u0013\u0012\u0010\u0018") : ((name == "\u0017\u0011\u0013\u000E\u0049\u0011\u0012\u0013\u000D\u0015\u000E\u0049\u000F\u000E") ? emit_helper_call_3(st, args, "\u0055\u0055\u0017\u0011\u0013\u000E\u0055\u0011\u0012\u0013\u000D\u0015\u000E\u0055\u000F\u000E") : ((name == "\u0017\u0011\u0013\u000E\u0049\u0013\u000D\u000E\u0049\u000F\u000E") ? emit_helper_call_3(st, args, "\u0055\u0055\u0017\u0011\u0013\u000E\u0055\u0013\u000D\u000E\u0055\u000F\u000E") : ((name == "\u0017\u0011\u0013\u000E\u0049\u0018\u0010\u0012\u000E\u000F\u0011\u0012\u0013") ? emit_helper_call_2(st, args, "\u0055\u0055\u0017\u0011\u0013\u000E\u0055\u0018\u0010\u0012\u000E\u000F\u0011\u0012\u0013") : ((name == "\u0018\u0014\u000F\u0015\u0049\u000F\u000E") ? emit_char_at_builtin(st, args) : ((name == "\u0018\u0014\u000F\u0015\u0049\u0018\u0010\u0016\u000D\u0049\u000F\u000E") ? emit_char_code_at_builtin(st, args) : ((name == "\u0018\u0014\u000F\u0015\u0049\u0018\u0010\u0016\u000D") ? emit__x86_64_code_generator_emit_expr(st_set_tail_pos(st, false), args[(int)0L]) : ((name == "\u0018\u0010\u0016\u000D\u0049\u000E\u0010\u0049\u0018\u0014\u000F\u0015") ? emit__x86_64_code_generator_emit_expr(st_set_tail_pos(st, false), args[(int)0L]) : ((name == "\u0018\u0014\u000F\u0015\u0049\u000E\u0010\u0049\u000E\u000D\u0024\u000E") ? emit_char_to_text_builtin(st, args) : ((name == "\u0011\u0013\u0049\u0017\u000D\u000E\u000E\u000D\u0015") ? emit_is_letter_builtin(st, args) : ((name == "\u0011\u0013\u0049\u0016\u0011\u001D\u0011\u000E") ? emit_is_digit_builtin(st, args) : ((name == "\u0011\u0013\u0049\u001B\u0014\u0011\u000E\u000D\u0013\u001F\u000F\u0018\u000D") ? emit_is_whitespace_builtin(st, args) : ((name == "\u0012\u000D\u001D\u000F\u000E\u000D") ? emit_negate_builtin(st, args) : ((name == "\u001D\u000D\u000E\u0049\u000F\u0015\u001D\u0013") ? emit_get_args_builtin(st) : ((name == "\u0018\u0019\u0015\u0015\u000D\u0012\u000E\u0049\u0016\u0011\u0015") ? emit_current_dir_builtin(st) : ((name == "\u0015\u000D\u0018\u0010\u0015\u0016\u0049\u0013\u000D\u000E") ? emit_record_set_builtin(st, args) : ((name == "\u0017\u0011\u0012\u0022\u000D\u0016\u0049\u0017\u0011\u0013\u000E\u0049\u000D\u001A\u001F\u000E\u001E") ? emit_linked_list_empty_builtin(st, args) : ((name == "\u0017\u0011\u0012\u0022\u000D\u0016\u0049\u0017\u0011\u0013\u000E\u0049\u001F\u0019\u0013\u0014") ? emit_linked_list_push_builtin(st, args) : ((name == "\u0017\u0011\u0012\u0022\u000D\u0016\u0049\u0017\u0011\u0013\u000E\u0049\u000E\u0010\u0049\u0017\u0011\u0013\u000E") ? emit_linked_list_to_list_builtin(st, args) : ((name == "\u0014\u000D\u000F\u001F\u0049\u0013\u000F\u0021\u000D") ? emit_heap_save_builtin(st) : ((name == "\u0014\u000D\u000F\u001F\u0049\u0015\u000D\u0013\u000E\u0010\u0015\u000D") ? emit_heap_restore_builtin(st, args) : ((name == "\u0014\u000D\u000F\u001F\u0049\u000F\u0016\u0021\u000F\u0012\u0018\u000D") ? emit_heap_advance_builtin(st, args) : ((name == "\u0017\u0011\u0013\u000E\u0049\u001B\u0011\u000E\u0014\u0049\u0018\u000F\u001F\u000F\u0018\u0011\u000E\u001E") ? emit_list_with_capacity_builtin(st, args) : ((name == "\u0020\u0019\u001C\u0049\u001B\u0015\u0011\u000E\u000D\u0049\u0020\u001E\u000E\u000D") ? emit_buf_write_byte_builtin(st, args) : ((name == "\u0020\u0019\u001C\u0049\u001B\u0015\u0011\u000E\u000D\u0049\u0020\u001E\u000E\u000D\u0013") ? emit_buf_write_bytes_builtin(st, args) : ((name == "\u0020\u0019\u001C\u0049\u0015\u000D\u000F\u0016\u0049\u0020\u001E\u000E\u000D\u0013") ? emit_buf_read_bytes_builtin(st, args) : ((name == "\u0020\u0011\u000E\u0049\u000F\u0012\u0016") ? emit_bit_op_2(st, args, (_p0_) => (_p1_) => and_rr(_p0_, _p1_)) : ((name == "\u0020\u0011\u000E\u0049\u0010\u0015") ? emit_bit_op_2(st, args, (_p0_) => (_p1_) => or_rr(_p0_, _p1_)) : ((name == "\u0020\u0011\u000E\u0049\u0024\u0010\u0015") ? emit_bit_op_2(st, args, (_p0_) => (_p1_) => xor_rr(_p0_, _p1_)) : ((name == "\u0020\u0011\u000E\u0049\u0013\u0014\u0017") ? emit_bit_shift(st, args, shl_cl) : ((name == "\u0020\u0011\u000E\u0049\u0013\u0014\u0015") ? emit_bit_shift(st, args, shr_cl) : ((name == "\u0020\u0011\u000E\u0049\u0012\u0010\u000E") ? emit_bit_not(st, args) : emit_file_exists_builtin(st, args)))))))))))))))))))))))))))))))))))))))))))))))))))));

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
                var sp = _tco_m0.Field3;
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
                var sp = _tco_m1.Field2;
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
            var r = emit__x86_64_code_generator_emit_expr(st, args[(int)i]);
            var loc = alloc_local(r.state);
            var st1 = store_local(loc.state, loc.reg, r.reg);
            var _tco_0 = st1;
            var _tco_1 = args;
            var _tco_2 = (i + 1L);
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
            var _tco_2 = (i + 1L);
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
            if ((i < 0L))
            {
            return st;
            }
            else
            {
            var _tco_0 = st_append_text(st, pop_r(arg_regs()[(int)i]));
            var _tco_1 = (i - 1L);
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
            if ((i < 6L))
            {
            return st;
            }
            else
            {
            var loaded = load_local(st, arg_locals[(int)i]);
            var _tco_0 = st_append_text(loaded.state, push_r(loaded.reg));
            var _tco_1 = arg_locals;
            var _tco_2 = (i - 1L);
            st = _tco_0;
            arg_locals = _tco_1;
            i = _tco_2;
            continue;
            }
        }
    }

    public static EmitResult emit__x86_64_code_generator_emit_apply(CodegenState st, IRExpr func_expr, IRExpr arg_expr, CodexType result_ty) => ((Func<bool, EmitResult>)((saved_tail) => ((Func<IRExpr, EmitResult>)((full_expr) => ((Func<EmitResult, EmitResult>)((r) => new EmitResult(state: st_set_tail_pos(r.state, saved_tail), reg: r.reg)))((((st.tco.active && saved_tail) && emit__x86_64_code_generator_is_self_call(full_expr, st.tco.current_func)) ? emit_tail_call(st, func_expr, arg_expr) : ((Func<FlatApply, EmitResult>)((flat) => (is_builtin(flat.func_name) ? emit__x86_64_code_generator_emit_builtin(st, flat.func_name, flat.args) : ((Func<CodexType, EmitResult>)((resolved_result_ty) => resolved_result_ty switch { SumTy(var sname, var ctors) => (EmitResult)(((Func<long, EmitResult>)((tag) => ((tag >= 0L) ? emit_sum_ctor(st, flat.args, tag) : ((Func<bool, EmitResult>)((is_local_sum) => (is_local_sum ? emit_indirect_call(st, flat) : emit_direct_call(st, flat))))((lookup_local(st.locals, flat.func_name) >= 0L)))))(find_ctor_tag(ctors, flat.func_name, 0L))), FunTy(var pt, var rt) => (EmitResult)(((Func<bool, EmitResult>)((is_local) => (is_local ? emit_indirect_call(st, flat) : emit_partial_application(st, flat.func_name, flat.args))))((lookup_local(st.locals, flat.func_name) >= 0L))), _ => (EmitResult)(((Func<bool, EmitResult>)((is_local) => (is_local ? emit_indirect_call(st, flat) : emit_direct_call(st, flat))))((lookup_local(st.locals, flat.func_name) >= 0L))), }))(resolve_constructed_ty(st, result_ty)))))(flatten_apply(func_expr, new List<IRExpr> { arg_expr }))))))(new IrApply(func_expr, arg_expr, result_ty, ir_expr_span(func_expr)))))(st.tco.in_tail_pos);

    public static EmitResult emit_direct_call(CodegenState st, FlatApply flat) => ((Func<SavedArgs, EmitResult>)((saved) => ((Func<long, EmitResult>)((arg_count) => ((Func<CodegenState, EmitResult>)((st1) => ((Func<long, EmitResult>)((reg_count) => ((Func<CodegenState, EmitResult>)((st2) => ((Func<CodegenState, EmitResult>)((st3) => ((Func<CodegenState, EmitResult>)((st4) => ((Func<long, EmitResult>)((stack_arg_count) => ((Func<CodegenState, EmitResult>)((st5) => ((Func<EmitResult, EmitResult>)((tmp) => new EmitResult(state: st_append_text(tmp.state, mov_rr(tmp.reg, reg_rax())), reg: tmp.reg)))(alloc_temp(st5))))(((stack_arg_count > 0L) ? st_append_text(st4, add_ri(reg_rsp(), (stack_arg_count * 8L))) : st4))))((arg_count - 6L))))(emit_call_to(st3, flat.func_name))))(pop_to_arg_regs(st2, (reg_count - 1L)))))(push_reg_args(st1, saved.locals, 0L, reg_count))))(((arg_count < 6L) ? arg_count : 6L))))(push_stack_args(saved.state, saved.locals, (arg_count - 1L)))))(((long)flat.args.Count))))(save_args_loop(st_set_tail_pos(st, false), flat.args, 0L, new List<long>()));

    public static EmitResult emit_indirect_call(CodegenState st, FlatApply flat) => ((Func<SavedArgs, EmitResult>)((saved) => ((Func<long, EmitResult>)((arg_count) => ((Func<long, EmitResult>)((reg_count) => ((Func<CodegenState, EmitResult>)((st1) => ((Func<CodegenState, EmitResult>)((st2) => ((Func<EmitResult, EmitResult>)((closure_load) => ((Func<CodegenState, EmitResult>)((st3) => ((Func<CodegenState, EmitResult>)((st4) => ((Func<CodegenState, EmitResult>)((st5) => ((Func<EmitResult, EmitResult>)((tmp) => new EmitResult(state: st_append_text(tmp.state, mov_rr(tmp.reg, reg_rax())), reg: tmp.reg)))(alloc_temp(st5))))(st_append_text(st4, new List<long> { 255L, 208L }))))(st_append_text(st3, mov_load(reg_rax(), reg_r11(), 0L)))))(st_append_text(closure_load.state, mov_rr(reg_r11(), closure_load.reg)))))(load_local(st2, lookup_local(st2.locals, flat.func_name)))))(pop_to_arg_regs(st1, (reg_count - 1L)))))(push_reg_args(saved.state, saved.locals, 0L, reg_count))))(((arg_count < 6L) ? arg_count : 6L))))(((long)flat.args.Count))))(save_args_loop(st_set_tail_pos(st, false), flat.args, 0L, new List<long>()));

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
                var sp = _tco_m0.Field3;
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
            var r = emit__x86_64_code_generator_emit_expr(st_notail, args[(int)i]);
            var st1 = store_local(r.state, temp_locals[(int)i], r.reg);
            var _tco_0 = st1;
            var _tco_1 = args;
            var _tco_2 = temp_locals;
            var _tco_3 = (i + 1L);
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
            var _tco_3 = (i + 1L);
            st = _tco_0;
            temp_locals = _tco_1;
            param_locals = _tco_2;
            i = _tco_3;
            continue;
            }
        }
    }

    public static EmitResult emit_tail_call(CodegenState st, IRExpr func_expr, IRExpr arg_expr) => ((Func<List<IRExpr>, EmitResult>)((args) => ((Func<CodegenState, EmitResult>)((st1) => ((Func<CodegenState, EmitResult>)((st2) => ((Func<long, EmitResult>)((rel32) => ((Func<CodegenState, EmitResult>)((st3) => ((Func<EmitResult, EmitResult>)((dummy) => new EmitResult(state: st_append_text(dummy.state, li(dummy.reg, 0L)), reg: dummy.reg)))(alloc_temp(st3))))(st_append_text(st2, jmp(rel32)))))((st.tco.loop_top - (st2.text_len + 5L)))))(copy_temps_to_params(st1, st.tco.temp_locals, st.tco.param_locals, 0L))))(eval_tail_args(st, args, st.tco.temp_locals, 0L))))(flatten_tail_args(func_expr, new List<IRExpr> { arg_expr }));

    public static EmitResult emit_sum_ctor(CodegenState st, List<IRExpr> args, long tag) => ((Func<SavedArgs, EmitResult>)((saved) => ((Func<long, EmitResult>)((field_count) => ((Func<long, EmitResult>)((total_size) => ((Func<EmitResult, EmitResult>)((ptr_loc) => ((Func<EmitResult, EmitResult>)((ptr_tmp) => ((Func<CodegenState, EmitResult>)((st1) => ((Func<CodegenState, EmitResult>)((st2) => ((Func<CodegenState, EmitResult>)((st3) => ((Func<EmitResult, EmitResult>)((tag_tmp) => ((Func<CodegenState, EmitResult>)((st4) => ((Func<EmitResult, EmitResult>)((ptr_load1) => ((Func<CodegenState, EmitResult>)((st5) => ((Func<CodegenState, EmitResult>)((st6) => load_local(st6, ptr_loc.reg)))(emit_store_ctor_fields(st5, saved.locals, ptr_loc.reg, 0L))))(st_append_text(ptr_load1.state, mov_store(ptr_load1.reg, tag_tmp.reg, 0L)))))(load_local(st4, ptr_loc.reg))))(st_append_text(tag_tmp.state, li(tag_tmp.reg, tag)))))(alloc_temp(st3))))(st_append_text(st2, add_ri(reg_r10(), total_size)))))(store_local(st1, ptr_loc.reg, ptr_tmp.reg))))(st_append_text(ptr_tmp.state, mov_rr(ptr_tmp.reg, reg_r10())))))(alloc_temp(ptr_loc.state))))(alloc_local(saved.state))))(((1L + field_count) * 8L))))(((long)args.Count))))(save_args_loop(st, args, 0L, new List<long>()));

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
            var st1 = st_append_text(ptr.state, mov_store(ptr.reg, val.reg, (8L + (i * 8L))));
            var _tco_0 = st1;
            var _tco_1 = field_locals;
            var _tco_2 = ptr_loc;
            var _tco_3 = (i + 1L);
            st = _tco_0;
            field_locals = _tco_1;
            ptr_loc = _tco_2;
            i = _tco_3;
            continue;
            }
        }
    }

    public static CodegenState emit_load_rodata_addr(CodegenState st, long reg, long rodata_off) => ((Func<RodataFixup, CodegenState>)((fixup) => ((Func<CodegenState, CodegenState>)((st1) => (st1 with { rodata_fixups = ((Func<List<RodataFixup>>)(() => { var _l = st1.rodata_fixups; _l.Add(fixup); return _l; }))() })))(st_append_text(st, mov_ri64(reg, 0L)))))(new RodataFixup(patch_offset: (st.text_len + 2L), rodata_offset: rodata_off));

    public static CodegenState emit_load_func_addr(CodegenState st, long reg, string func_name) => ((Func<FuncAddrFixup, CodegenState>)((fixup) => ((Func<CodegenState, CodegenState>)((st1) => (st1 with { func_addr_fixups = ((Func<List<FuncAddrFixup>>)(() => { var _l = st1.func_addr_fixups; _l.Add(fixup); return _l; }))() })))(st_append_text(st, mov_ri64(reg, 0L)))))(new FuncAddrFixup(patch_offset: (st.text_len + 2L), target: func_name));

    public static CodegenState emit_trampoline_shift_args(CodegenState st, long i, long num_captures)
    {
        while (true)
        {
            if ((i < 0L))
            {
            return st;
            }
            else
            {
            if (((i + num_captures) < 6L))
            {
            var _tco_0 = st_append_text(st, mov_rr(arg_regs()[(int)(i + num_captures)], arg_regs()[(int)i]));
            var _tco_1 = (i - 1L);
            var _tco_2 = num_captures;
            st = _tco_0;
            i = _tco_1;
            num_captures = _tco_2;
            continue;
            }
            else
            {
            var _tco_0 = st;
            var _tco_1 = (i - 1L);
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
            if ((i < 6L))
            {
            var _tco_0 = st_append_text(st, mov_load(arg_regs()[(int)i], reg_r11(), (8L + (i * 8L))));
            var _tco_1 = (i + 1L);
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

    public static EmitResult emit_partial_application(CodegenState st, string func_name, List<IRExpr> captured_args) => ((Func<SavedArgs, EmitResult>)((saved) => ((Func<long, EmitResult>)((num_captures) => ((Func<string, EmitResult>)((tramp_name) => ((Func<long, EmitResult>)((jmp_over_pos) => ((Func<CodegenState, EmitResult>)((st1) => ((Func<CodegenState, EmitResult>)((st2) => ((Func<CodegenState, EmitResult>)((st3) => ((Func<CodegenState, EmitResult>)((st4) => ((Func<CodegenState, EmitResult>)((st5) => ((Func<CodegenState, EmitResult>)((st6) => ((Func<CodegenState, EmitResult>)((st7) => ((Func<long, EmitResult>)((closure_size) => ((Func<EmitResult, EmitResult>)((ptr_loc) => ((Func<EmitResult, EmitResult>)((ptr_tmp) => ((Func<CodegenState, EmitResult>)((st8) => ((Func<CodegenState, EmitResult>)((st9) => ((Func<CodegenState, EmitResult>)((st10) => ((Func<CodegenState, EmitResult>)((st11) => ((Func<EmitResult, EmitResult>)((ptr_load1) => ((Func<CodegenState, EmitResult>)((st12) => ((Func<CodegenState, EmitResult>)((st13) => load_local(st13, ptr_loc.reg)))(emit_store_closure_captures(st12, saved.locals, ptr_loc.reg, 0L))))(st_append_text(ptr_load1.state, mov_store(ptr_load1.reg, reg_rax(), 0L)))))(load_local(st11, ptr_loc.reg))))(emit_load_func_addr(st10, reg_rax(), tramp_name))))(st_append_text(st9, add_ri(reg_r10(), closure_size)))))(store_local(st8, ptr_loc.reg, ptr_tmp.reg))))(st_append_text(ptr_tmp.state, mov_rr(ptr_tmp.reg, reg_r10())))))(alloc_temp(ptr_loc.state))))(alloc_local(st7))))(((1L + num_captures) * 8L))))(patch_jmp_at(st6, jmp_over_pos, st6.text_len))))(st_append_text(st5, new List<long> { 255L, 224L }))))(emit_load_func_addr(st4, reg_rax(), func_name))))(emit_trampoline_load_captures(st3, 0L, num_captures))))(emit_trampoline_shift_args(st2, 5L, num_captures))))(record_func_offset(st1, tramp_name))))(st_append_text(saved.state, jmp(0L)))))(saved.state.text_len)))((func_name + ("\u0055\u0055\u000E\u0015\u000F\u001A\u001F\u0055\u0055" + (_Cce.FromUnicode(num_captures.ToString()) + ("\u0055\u0055" + _Cce.FromUnicode(saved.state.text_len.ToString()))))))))(((long)captured_args.Count))))(save_args_loop(st_set_tail_pos(st, false), captured_args, 0L, new List<long>()));

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
            var st1 = st_append_text(ptr.state, mov_store(ptr.reg, val.reg, (8L + (i * 8L))));
            var _tco_0 = st1;
            var _tco_1 = cap_locals;
            var _tco_2 = ptr_loc;
            var _tco_3 = (i + 1L);
            st = _tco_0;
            cap_locals = _tco_1;
            ptr_loc = _tco_2;
            i = _tco_3;
            continue;
            }
        }
    }

    public static List<PatchEntry> collect_func_addr_patches(List<FuncAddrFixup> fixups, OffsetTable offset_map, long text_base, long i, List<PatchEntry> acc)
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
            var func_offset = offset_table_lookup(offset_map, f.target);
            var addr = (text_base + func_offset);
            var addr_bytes = write_i64(addr);
            var p0 = new PatchEntry(pos: f.patch_offset, b0: addr_bytes[(int)0L], b1: addr_bytes[(int)1L], b2: addr_bytes[(int)2L], b3: addr_bytes[(int)3L]);
            var p1 = new PatchEntry(pos: (f.patch_offset + 4L), b0: addr_bytes[(int)4L], b1: addr_bytes[(int)5L], b2: addr_bytes[(int)6L], b3: addr_bytes[(int)7L]);
            var _tco_0 = fixups;
            var _tco_1 = offset_map;
            var _tco_2 = text_base;
            var _tco_3 = (i + 1L);
            var _tco_4 = Enumerable.Concat(acc, new List<PatchEntry> { p0, p1 }).ToList();
            fixups = _tco_0;
            offset_map = _tco_1;
            text_base = _tco_2;
            i = _tco_3;
            acc = _tco_4;
            continue;
            }
        }
    }

    public static List<PatchEntry> collect_rodata_patches(List<RodataFixup> fixups, long rodata_vaddr, long i, List<PatchEntry> acc)
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
            var addr = (rodata_vaddr + f.rodata_offset);
            var addr_bytes = write_i64(addr);
            var p0 = new PatchEntry(pos: f.patch_offset, b0: addr_bytes[(int)0L], b1: addr_bytes[(int)1L], b2: addr_bytes[(int)2L], b3: addr_bytes[(int)3L]);
            var p1 = new PatchEntry(pos: (f.patch_offset + 4L), b0: addr_bytes[(int)4L], b1: addr_bytes[(int)5L], b2: addr_bytes[(int)6L], b3: addr_bytes[(int)7L]);
            var _tco_0 = fixups;
            var _tco_1 = rodata_vaddr;
            var _tco_2 = (i + 1L);
            var _tco_3 = Enumerable.Concat(acc, new List<PatchEntry> { p0, p1 }).ToList();
            fixups = _tco_0;
            rodata_vaddr = _tco_1;
            i = _tco_2;
            acc = _tco_3;
            continue;
            }
        }
    }

    public static CodexType resolve_constructed_raw(CodexType raw, CodexType fallback) => raw switch { RecordTy(var rn, var rf) => (CodexType)(raw), SumTy(var sn, var sc) => (CodexType)(raw), FunTy(var p, var r) => (CodexType)(strip_fun_args_emitter(r)), _ => (CodexType)(fallback), };

    public static CodexType resolve_constructed_ty(CodegenState st, CodexType ty)
    {
        while (true)
        {
            var _tco_s = ty;
            if (_tco_s is RecordTy _tco_m0)
            {
                var rn = _tco_m0.Field0;
                var rf = _tco_m0.Field1;
            return ty;
            }
            else if (_tco_s is SumTy _tco_m1)
            {
                var sn = _tco_m1.Field0;
                var sc = _tco_m1.Field1;
            return ty;
            }
            else if (_tco_s is ConstructedTy _tco_m2)
            {
                var cname = _tco_m2.Field0;
                var cargs = _tco_m2.Field1;
            var raw = lookup_type_binding(st.type_defs, cname.value);
            return resolve_constructed_raw(raw, ty);
            }
            else if (_tco_s is EffectfulTy _tco_m3)
            {
                var effs = _tco_m3.Field0;
                var ret = _tco_m3.Field1;
            var _tco_0 = st;
            var _tco_1 = ret;
            st = _tco_0;
            ty = _tco_1;
            continue;
            }
            else if (_tco_s is ForAllTy _tco_m4)
            {
                var id = _tco_m4.Field0;
                var body = _tco_m4.Field1;
            var _tco_0 = st;
            var _tco_1 = body;
            st = _tco_0;
            ty = _tco_1;
            continue;
            }
            {
            return ty;
            }
        }
    }

    public static CodexType strip_fun_args_emitter(CodexType ty)
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

    public static CodexType lookup_type_binding(List<TypeBinding> bindings, string name) => lookup_type_binding_loop(bindings, name, 0L, ((long)bindings.Count));

    public static CodexType lookup_type_binding_loop(List<TypeBinding> bindings, string name, long i, long len)
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

    public static long find_record_field_index(List<RecordField> fields, string name, long i)
    {
        while (true)
        {
            if ((i == ((long)fields.Count)))
            {
            return 0L;
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
            var _tco_2 = (i + 1L);
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
            var r = emit__x86_64_code_generator_emit_expr(st, fv.value);
            var loc = alloc_local(r.state);
            var st1 = store_local(loc.state, loc.reg, r.reg);
            var _tco_0 = st1;
            var _tco_1 = fields;
            var _tco_2 = (i + 1L);
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
            return (-1L);
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
            var _tco_2 = (i + 1L);
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
            var slot = find_field_local_slot(field_locals, tf.name.value, 0L);
            if ((slot >= 0L))
            {
            var val = load_local(st, slot);
            var ptr = load_local(val.state, ptr_loc);
            var st1 = st_append_text(ptr.state, mov_store(ptr.reg, val.reg, (i * 8L)));
            var _tco_0 = st1;
            var _tco_1 = type_fields;
            var _tco_2 = field_locals;
            var _tco_3 = ptr_loc;
            var _tco_4 = (i + 1L);
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
            var _tco_4 = (i + 1L);
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
            var st1 = st_append_text(ptr.state, mov_store(ptr.reg, val.reg, (i * 8L)));
            var _tco_0 = st1;
            var _tco_1 = field_locals;
            var _tco_2 = ptr_loc;
            var _tco_3 = (i + 1L);
            st = _tco_0;
            field_locals = _tco_1;
            ptr_loc = _tco_2;
            i = _tco_3;
            continue;
            }
        }
    }

    public static EmitResult emit__x86_64_code_generator_emit_record(CodegenState st, List<IRFieldVal> fields, CodexType ty) => ((Func<EvalFieldsResult, EmitResult>)((evaled) => ((Func<long, EmitResult>)((field_count) => ((Func<long, EmitResult>)((total_size) => ((Func<EmitResult, EmitResult>)((ptr_loc) => ((Func<EmitResult, EmitResult>)((ptr_tmp) => ((Func<CodegenState, EmitResult>)((st1) => ((Func<CodegenState, EmitResult>)((st2) => ((Func<CodegenState, EmitResult>)((st3) => ((Func<CodexType, EmitResult>)((resolved_ty) => ((Func<CodegenState, EmitResult>)((st4) => load_local(st4, ptr_loc.reg)))(resolved_ty switch { RecordTy(var rname, var type_fields) => (CodegenState)(emit_store_record_fields_by_type(st3, type_fields, evaled.field_locals, ptr_loc.reg, 0L)), _ => (CodegenState)(emit_store_record_fields_by_list(st3, evaled.field_locals, ptr_loc.reg, 0L)), })))(resolve_constructed_ty(st, ty))))(st_append_text(st2, add_ri(reg_r10(), total_size)))))(store_local(st1, ptr_loc.reg, ptr_tmp.reg))))(st_append_text(ptr_tmp.state, mov_rr(ptr_tmp.reg, reg_r10())))))(alloc_temp(ptr_loc.state))))(alloc_local(evaled.state))))((field_count * 8L))))(((long)fields.Count))))(emit_eval_record_fields(st, fields, 0L, new List<FieldLocal>()));

    public static EmitResult emit__x86_64_code_generator_emit_field_access(CodegenState st, IRExpr rec_expr, string field_name) => ((Func<CodexType, EmitResult>)((rec_ty) => ((Func<EmitResult, EmitResult>)((rec_result) => rec_ty switch { RecordTy(var rname, var rfields) => (EmitResult)(((Func<long, EmitResult>)((field_idx) => ((Func<EmitResult, EmitResult>)((rd) => new EmitResult(state: st_append_text(rd.state, mov_load(rd.reg, rec_result.reg, (field_idx * 8L))), reg: rd.reg)))(alloc_temp(rec_result.state))))(find_record_field_index(rfields, field_name, 0L))), _ => (EmitResult)(((Func<CodegenState, EmitResult>)((st_err) => ((Func<EmitResult, EmitResult>)((rd) => new EmitResult(state: st_append_text(rd.state, new List<long> { 15L, 11L }), reg: rd.reg)))(alloc_temp(st_err))))(st_add_error(rec_result.state, cdx_ir_error(), ("\u000D\u001A\u0011\u000E\u0049\u001C\u0011\u000D\u0017\u0016\u0049\u000F\u0018\u0018\u000D\u0013\u0013\u0045\u0002\u0019\u0012\u0015\u000D\u0013\u0010\u0017\u0021\u000D\u0016\u0002\u000E\u001E\u001F\u000D\u0002\u001C\u0010\u0015\u0002\u001C\u0011\u000D\u0017\u0016\u0002\u0047" + (field_name + "\u0047")), ir_expr_span(rec_expr)))), }))(emit__x86_64_code_generator_emit_expr(st, rec_expr))))(resolve_constructed_ty(st, ir_expr_type(rec_expr)));

    public static EmitResult emit__x86_64_code_generator_emit_match(CodegenState st, IRExpr scrut_expr, List<IRBranch> branches) => ((Func<bool, EmitResult>)((saved_tail) => ((Func<CodexType, EmitResult>)((scrut_ty) => ((Func<EmitResult, EmitResult>)((scrut_result) => ((Func<EmitResult, EmitResult>)((scrut_loc) => ((Func<CodegenState, EmitResult>)((st1) => ((Func<CodegenState, EmitResult>)((st1a) => ((Func<EmitResult, EmitResult>)((result_loc) => ((Func<MatchBranchState, EmitResult>)((mbs) => ((Func<MatchBranchState, EmitResult>)((mbs_final) => ((Func<CodegenState, EmitResult>)((st_end) => load_local(st_end, result_loc.reg)))(patch_match_end_jumps(mbs_final.cg_state, mbs_final.end_patches, 0L))))(emit_match_branch_loop(mbs, scrut_loc.reg, result_loc.reg, branches, 0L, ((long)branches.Count), scrut_ty))))(new MatchBranchState(cg_state: st1a, end_patches: new List<long>()))))(alloc_local(st1a))))(st_set_tail_pos(st1, saved_tail))))(store_local(scrut_loc.state, scrut_loc.reg, scrut_result.reg))))(alloc_local(scrut_result.state))))(emit__x86_64_code_generator_emit_expr(st_set_tail_pos(st, false), scrut_expr))))(resolve_constructed_ty(st, ir_expr_type(scrut_expr)))))(st.tco.in_tail_pos);

    public static MatchBranchState emit_match_branch_loop(MatchBranchState mbs, long scrut_loc, long result_loc, List<IRBranch> branches, long i, long total, CodexType scrut_ty)
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
            var mbs1 = emit_one_match_branch(mbs, scrut_loc, result_loc, b, (i < (total - 1L)), scrut_ty);
            var _tco_0 = mbs1;
            var _tco_1 = scrut_loc;
            var _tco_2 = result_loc;
            var _tco_3 = branches;
            var _tco_4 = (i + 1L);
            var _tco_5 = total;
            var _tco_6 = scrut_ty;
            mbs = _tco_0;
            scrut_loc = _tco_1;
            result_loc = _tco_2;
            branches = _tco_3;
            i = _tco_4;
            total = _tco_5;
            scrut_ty = _tco_6;
            continue;
            }
        }
    }

    public static MatchBranchState emit_one_match_branch(MatchBranchState mbs, long scrut_loc, long result_loc, IRBranch branch, bool needs_jmp_end, CodexType scrut_ty) => ((Func<EmitPatternResult, MatchBranchState>)((pat_result) => ((Func<EmitResult, MatchBranchState>)((body_result) => ((Func<CodegenState, MatchBranchState>)((st1) => ((Func<MatchBranchState, MatchBranchState>)((st2) => ((Func<MatchBranchState, MatchBranchState>)((st3) => st3))(((pat_result.next_branch_patch >= 0L) ? new MatchBranchState(cg_state: patch_jcc_at(st2.cg_state, pat_result.next_branch_patch, st2.cg_state.text_len), end_patches: st2.end_patches) : st2))))((needs_jmp_end ? ((Func<long, MatchBranchState>)((jmp_pos) => new MatchBranchState(cg_state: st_append_text(st1, jmp(0L)), end_patches: ((Func<List<long>>)(() => { var _l = mbs.end_patches; _l.Add(jmp_pos); return _l; }))())))(st1.text_len) : new MatchBranchState(cg_state: st1, end_patches: mbs.end_patches)))))(store_local(body_result.state, result_loc, body_result.reg))))(emit__x86_64_code_generator_emit_expr(pat_result.state, branch.body))))(emit__x86_64_code_generator_emit_pattern(mbs.cg_state, scrut_loc, branch.pattern, scrut_ty));

    public static EmitPatternResult emit__x86_64_code_generator_emit_pattern(CodegenState st, long scrut_loc, IRPat pat, CodexType scrut_ty) => pat switch { IrWildPat(var sp) => (EmitPatternResult)(new EmitPatternResult(state: st, next_branch_patch: (-1L))), IrVarPat(var name, var ty, var sp) => (EmitPatternResult)(((Func<EmitResult, EmitPatternResult>)((var_loc) => ((Func<EmitResult, EmitPatternResult>)((loaded) => ((Func<CodegenState, EmitPatternResult>)((st1) => new EmitPatternResult(state: add_local(st1, name, var_loc.reg), next_branch_patch: (-1L))))(store_local(loaded.state, var_loc.reg, loaded.reg))))(load_local(var_loc.state, scrut_loc))))(alloc_local(st))), IrCtorPat(var name, var sub_pats, var ty, var sp) => (EmitPatternResult)(((Func<EmitResult, EmitPatternResult>)((scrut_load) => ((Func<EmitResult, EmitPatternResult>)((tag_reg) => ((Func<CodegenState, EmitPatternResult>)((st1) => ((Func<CodexType, EmitPatternResult>)((resolve_ty) => ((Func<long, EmitPatternResult>)((expected_tag) => ((Func<CodegenState, EmitPatternResult>)((st2) => ((Func<long, EmitPatternResult>)((jcc_pos) => ((Func<CodegenState, EmitPatternResult>)((st3) => ((Func<CodegenState, EmitPatternResult>)((st4) => new EmitPatternResult(state: st4, next_branch_patch: jcc_pos)))(bind_ctor_fields(st3, scrut_loc, sub_pats, 0L))))(st_append_text(st2, jcc(cc_ne(), 0L)))))(st2.text_len)))(st_append_text(st1, cmp_ri(tag_reg.reg, expected_tag)))))(resolve_ty switch { SumTy(var sname, var ctors) => (long)(find_ctor_tag(ctors, name, 0L)), _ => (long)(0L), })))(scrut_ty switch { SumTy(var sn, var cs) => (CodexType)(scrut_ty), _ => (CodexType)(resolve_constructed_ty(st, ty)), })))(st_append_text(tag_reg.state, mov_load(tag_reg.reg, scrut_load.reg, 0L)))))(alloc_temp(scrut_load.state))))(load_local(st, scrut_loc))), IrLitPat(var value, var ty, var sp) => (EmitPatternResult)(new EmitPatternResult(state: st, next_branch_patch: (-1L))), _ => throw new InvalidOperationException("Non-exhaustive match"), };

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
                var sp = _tco_m0.Field2;
            var field_loc = alloc_local(st);
            var scrut_load = load_local(field_loc.state, scrut_loc);
            var field_val = alloc_temp(scrut_load.state);
            var st1 = st_append_text(field_val.state, mov_load(field_val.reg, scrut_load.reg, ((1L + i) * 8L)));
            var st2 = store_local(st1, field_loc.reg, field_val.reg);
            var _tco_0 = add_local(st2, name, field_loc.reg);
            var _tco_1 = scrut_loc;
            var _tco_2 = sub_pats;
            var _tco_3 = (i + 1L);
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
            var _tco_3 = (i + 1L);
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
            var _tco_0 = patch_jmp_at(st, patches[(int)i], st.text_len);
            var _tco_1 = patches;
            var _tco_2 = (i + 1L);
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
            var r = emit__x86_64_code_generator_emit_expr(st, elems[(int)i]);
            var loc = alloc_local(r.state);
            var st1 = store_local(loc.state, loc.reg, r.reg);
            var _tco_0 = st1;
            var _tco_1 = elems;
            var _tco_2 = (i + 1L);
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
            var st1 = st_append_text(ptr.state, mov_store(ptr.reg, val.reg, (8L + (i * 8L))));
            var _tco_0 = st1;
            var _tco_1 = elem_locals;
            var _tco_2 = ptr_loc;
            var _tco_3 = (i + 1L);
            st = _tco_0;
            elem_locals = _tco_1;
            ptr_loc = _tco_2;
            i = _tco_3;
            continue;
            }
        }
    }

    public static EmitResult emit__x86_64_code_generator_emit_list(CodegenState st, List<IRExpr> elems) => ((Func<SavedArgs, EmitResult>)((saved) => ((Func<long, EmitResult>)((count) => ((Func<EmitResult, EmitResult>)((cap_tmp) => ((Func<CodegenState, EmitResult>)((st1) => ((Func<CodegenState, EmitResult>)((st2) => ((Func<CodegenState, EmitResult>)((st3) => ((Func<EmitResult, EmitResult>)((ptr_loc) => ((Func<EmitResult, EmitResult>)((ptr_tmp) => ((Func<CodegenState, EmitResult>)((st4) => ((Func<CodegenState, EmitResult>)((st5) => ((Func<CodegenState, EmitResult>)((st6) => ((Func<EmitResult, EmitResult>)((len_tmp) => ((Func<CodegenState, EmitResult>)((st7) => ((Func<EmitResult, EmitResult>)((ptr_load) => ((Func<CodegenState, EmitResult>)((st8) => ((Func<CodegenState, EmitResult>)((st9) => load_local(st9, ptr_loc.reg)))(emit_store_list_elems(st8, saved.locals, ptr_loc.reg, 0L))))(st_append_text(ptr_load.state, mov_store(ptr_load.reg, len_tmp.reg, 0L)))))(load_local(st7, ptr_loc.reg))))(st_append_text(len_tmp.state, li(len_tmp.reg, count)))))(alloc_temp(st6))))(st_append_text(st5, add_ri(reg_r10(), ((count + 1L) * 8L))))))(store_local(st4, ptr_loc.reg, ptr_tmp.reg))))(st_append_text(ptr_tmp.state, mov_rr(ptr_tmp.reg, reg_r10())))))(alloc_temp(ptr_loc.state))))(alloc_local(st3))))(st_append_text(st2, add_ri(reg_r10(), 8L)))))(st_append_text(st1, mov_store(reg_r10(), cap_tmp.reg, 0L)))))(st_append_text(cap_tmp.state, li(cap_tmp.reg, count)))))(alloc_temp(saved.state))))(((long)elems.Count))))(emit_eval_list_elems(st, elems, 0L, new List<long>()));

    public static List<long> multiboot_header() => Enumerable.Concat(write_i32(464367618L), Enumerable.Concat(write_i32(0L), write_i32(3830599678L)).ToList()).ToList();

    public static List<long> tramp_clear_pages() => new List<long> { 250L, 191L, 0L, 16L, 0L, 0L, 185L, 0L, 12L, 0L, 0L, 49L, 192L, 243L, 171L };

    public static List<long> tramp_page_tables() => new List<long> { 199L, 5L, 0L, 16L, 0L, 0L, 3L, 32L, 0L, 0L, 199L, 5L, 0L, 32L, 0L, 0L, 3L, 48L, 0L, 0L, 191L, 0L, 48L, 0L, 0L, 185L, 0L, 1L, 0L, 0L, 184L, 131L, 0L, 0L, 0L, 137L, 7L, 199L, 71L, 4L, 0L, 0L, 0L, 0L, 131L, 199L, 8L, 5L, 0L, 0L, 32L, 0L, 73L, 117L, 236L };

    public static List<long> tramp_enable_long_mode() => new List<long> { 184L, 0L, 16L, 0L, 0L, 15L, 34L, 216L, 15L, 32L, 224L, 131L, 200L, 32L, 15L, 34L, 224L, 185L, 128L, 0L, 0L, 192L, 15L, 50L, 13L, 0L, 1L, 0L, 0L, 15L, 48L, 15L, 32L, 192L, 13L, 0L, 0L, 0L, 128L, 15L, 34L, 192L };

    public static List<long> trampoline_code() => Enumerable.Concat(tramp_clear_pages(), Enumerable.Concat(tramp_page_tables(), tramp_enable_long_mode()).ToList()).ToList();

    public static List<long> tramp_gdt_data() => new List<long> { 235L, 30L, 0L, 0L, 0L, 0L, 0L, 0L, 0L, 0L, 255L, 255L, 0L, 0L, 0L, 154L, 175L, 0L, 255L, 255L, 0L, 0L, 0L, 146L, 207L, 0L };

    public static List<long> trampoline_gdt_section() => Enumerable.Concat(tramp_gdt_data(), Enumerable.Concat(write_i16(23L), Enumerable.Concat(write_i32((bare_metal_load_addr() + 126L)), Enumerable.Concat(new List<long> { 15L, 1L, 21L }, Enumerable.Concat(write_i32((bare_metal_load_addr() + 150L)), new List<long> { 234L, 0L, 0L, 0L, 0L, 8L, 0L }).ToList()).ToList()).ToList()).ToList()).ToList();

    public static TrampolineResult bare_metal_trampoline() => ((Func<List<long>, TrampolineResult>)((bytes) => new TrampolineResult(bytes: bytes, far_jump_patch_pos: 163L)))(Enumerable.Concat(multiboot_header(), Enumerable.Concat(trampoline_code(), trampoline_gdt_section()).ToList()).ToList());

    public static CodegenState emit_out_byte(CodegenState st, long port, long value) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => st_append_text(st2, out_dx_al())))(st_append_text(st1, li(reg_rax(), value)))))(st_append_text(st, li(reg_rdx(), port)));

    public static CodegenState emit_com1_init(CodegenState st) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => emit_out_byte(st5, 1020L, 11L)))(emit_out_byte(st4, 1018L, 199L))))(emit_out_byte(st3, 1019L, 3L))))(emit_out_byte(st2, 1017L, 0L))))(emit_out_byte(st1, 1016L, 1L))))(emit_out_byte(st, 1019L, 128L));

    public static CodegenState emit_serial_wait_and_send(CodegenState st, long byte_val) => ((Func<long, CodegenState>)((wait_top) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<long, CodegenState>)((jne_pos) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => st_append_text(st8, out_dx_al())))(st_append_text(st7, li(reg_rax(), byte_val)))))(st_append_text(st6, li(reg_rdx(), 1016L)))))(patch_jcc_at(st5, jne_pos, st5.text_len))))(st_append_text(st4, jmp((wait_top - (st4.text_len + 5L)))))))(st_append_text(st3, jcc(cc_ne(), 0L)))))(st3.text_len)))(st_append_text(st2, new List<long> { 168L, 32L }))))(st_append_text(st1, new List<long> { 236L }))))(st_append_text(st, li(reg_rdx(), 1021L)))))(st.text_len);

    public static CodegenState emit_serial_send_rdi(CodegenState st) => ((Func<long, CodegenState>)((wait_top) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<long, CodegenState>)((jne_pos) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => st_append_text(st8, out_dx_al())))(st_append_text(st7, mov_rr(reg_rax(), reg_rdi())))))(st_append_text(st6, li(reg_rdx(), 1016L)))))(patch_jcc_at(st5, jne_pos, st5.text_len))))(st_append_text(st4, jmp((wait_top - (st4.text_len + 5L)))))))(st_append_text(st3, jcc(cc_ne(), 0L)))))(st3.text_len)))(st_append_text(st2, new List<long> { 168L, 32L }))))(st_append_text(st1, new List<long> { 236L }))))(st_append_text(st, li(reg_rdx(), 1021L)))))(st.text_len);

    public static ItoaState emit_itoa_zero_check(CodegenState st) => ((Func<CodegenState, ItoaState>)((st1) => ((Func<CodegenState, ItoaState>)((st2) => ((Func<long, ItoaState>)((jne_pos) => ((Func<CodegenState, ItoaState>)((st3) => ((Func<CodegenState, ItoaState>)((st4) => ((Func<long, ItoaState>)((jmp_pos) => ((Func<CodegenState, ItoaState>)((st5) => ((Func<CodegenState, ItoaState>)((st6) => new ItoaState(cg: st6, jmp_done_zero_pos: jmp_pos)))(patch_jcc_at(st5, jne_pos, st5.text_len))))(st_append_text(st4, jmp(0L)))))(st4.text_len)))(emit_serial_wait_and_send(st3, 48L))))(st_append_text(st2, jcc(cc_ne(), 0L)))))(st2.text_len)))(st_append_text(st1, test_rr(reg_rbx(), reg_rbx())))))(st_append_text(st, mov_rr(reg_rbx(), reg_rax())));

    public static CodegenState emit_itoa_sign_and_digits(CodegenState st) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<long, CodegenState>)((jns_pos) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<long, CodegenState>)((loop_top) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => ((Func<CodegenState, CodegenState>)((st12) => ((Func<CodegenState, CodegenState>)((st13) => ((Func<CodegenState, CodegenState>)((st14) => ((Func<CodegenState, CodegenState>)((st15) => st_append_text(st15, jcc(cc_ne(), (loop_top - (st15.text_len + 6L))))))(st_append_text(st14, test_rr(reg_rbx(), reg_rbx())))))(st_append_text(st13, add_ri(reg_rcx(), 1L)))))(st_append_text(st12, push_r(reg_rdx())))))(st_append_text(st11, add_ri(reg_rdx(), 48L)))))(st_append_text(st10, mov_rr(reg_rbx(), reg_rax())))))(st_append_text(st9, idiv_r(reg_r11())))))(st_append_text(st8, cqo()))))(st_append_text(st7, mov_rr(reg_rax(), reg_rbx())))))(st7.text_len)))(st_append_text(st6, li(reg_r11(), 10L)))))(st_append_text(st5, li(reg_rcx(), 0L)))))(patch_jcc_at(st4, jns_pos, st4.text_len))))(st_append_text(st3, neg_r(reg_rbx())))))(emit_serial_wait_and_send(st2, 45L))))(st_append_text(st1, jcc(cc_ge(), 0L)))))(st1.text_len)))(st_append_text(st, test_rr(reg_rbx(), reg_rbx())));

    public static CodegenState emit_itoa_print_loop(CodegenState st) => ((Func<long, CodegenState>)((loop_top) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<long, CodegenState>)((je_pos) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => patch_jcc_at(st8, je_pos, st8.text_len)))(st_append_text(st7, jmp((loop_top - (st7.text_len + 5L)))))))(st_append_text(st6, sub_ri(reg_rcx(), 1L)))))(st_append_text(st5, pop_r(reg_rcx())))))(emit_serial_send_rdi(st4))))(st_append_text(st3, push_r(reg_rcx())))))(st_append_text(st2, pop_r(reg_rdi())))))(st_append_text(st1, jcc(cc_e(), 0L)))))(st1.text_len)))(st_append_text(st, test_rr(reg_rcx(), reg_rcx())))))(st.text_len);

    public static CodegenState emit_inline_itoa_and_print(CodegenState st) => ((Func<ItoaState, CodegenState>)((zero) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => patch_jmp_at(st2, zero.jmp_done_zero_pos, st2.text_len)))(emit_itoa_print_loop(st1))))(emit_itoa_sign_and_digits(zero.cg))))(emit_itoa_zero_check(st));

    public static CodegenState emit_serial_wait_thr(CodegenState st) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<long, CodegenState>)((wait_top) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<long, CodegenState>)((jne_pos) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => st_append_text(st7, pop_r(reg_rax()))))(patch_jcc_at(st6, jne_pos, st6.text_len))))(st_append_text(st5, jmp((wait_top - (st5.text_len + 5L)))))))(st_append_text(st4, jcc(cc_ne(), 0L)))))(st4.text_len)))(st_append_text(st3, new List<long> { 168L, 32L }))))(st_append_text(st2, new List<long> { 236L }))))(st_append_text(st1, li(reg_rdx(), 1021L)))))(st1.text_len)))(st_append_text(st, push_r(reg_rax())));

    public static CodegenState emit_print_newline(CodegenState st) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => st_append_text(st3, out_dx_al())))(st_append_text(st2, li(reg_rdx(), 1016L)))))(st_append_text(st1, li(reg_rax(), 10L)))))(emit_serial_wait_thr(st));

    public static CodegenState emit_print_text_loop(CodegenState st, long saved_ptr, long saved_table, long saved_len, long saved_idx) => ((Func<long, CodegenState>)((loop_top) => ((Func<EmitResult, CodegenState>)((idx) => ((Func<EmitResult, CodegenState>)((len_check) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<long, CodegenState>)((done_jump) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<EmitResult, CodegenState>)((ptr_l) => ((Func<EmitResult, CodegenState>)((idx2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<EmitResult, CodegenState>)((tbl) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<EmitResult, CodegenState>)((idx3) => ((Func<CodegenState, CodegenState>)((st11) => ((Func<CodegenState, CodegenState>)((st12) => ((Func<CodegenState, CodegenState>)((st13) => patch_jcc_at(st13, done_jump, st13.text_len)))(st_append_text(st12, jmp((loop_top - (st12.text_len + 5L)))))))(store_local(st11, saved_idx, idx3.reg))))(st_append_text(idx3.state, add_ri(idx3.reg, 1L)))))(load_local(st10, saved_idx))))(st_append_text(st9, out_dx_al()))))(st_append_text(st8, li(reg_rdx(), 1016L)))))(emit_serial_wait_thr(st7))))(st_append_text(st6, movzx_byte(reg_rax(), reg_rax(), 0L)))))(st_append_text(tbl.state, add_rr(reg_rax(), tbl.reg)))))(load_local(st5, saved_table))))(st_append_text(st4, movzx_byte(reg_rax(), reg_rsi(), 0L)))))(st_append_text(st3, add_rr(reg_rsi(), idx2.reg)))))(st_append_text(idx2.state, lea(reg_rsi(), ptr_l.reg, 8L)))))(load_local(ptr_l.state, saved_idx))))(load_local(st2, saved_ptr))))(st_append_text(st1, jcc(cc_ge(), 0L)))))(st1.text_len)))(st_append_text(len_check.state, cmp_rr(idx.reg, len_check.reg)))))(load_local(idx.state, saved_len))))(load_local(st, saved_idx))))(st.text_len);

    public static CodegenState emit_print_text(CodegenState st, long ptr_reg) => ((Func<EmitResult, CodegenState>)((saved_ptr) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<EmitResult, CodegenState>)((saved_table) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st2a) => ((Func<EmitResult, CodegenState>)((ptr_ld) => ((Func<EmitResult, CodegenState>)((tmp_len) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<EmitResult, CodegenState>)((saved_len) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<EmitResult, CodegenState>)((saved_idx) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => emit_print_newline(st7)))(emit_print_text_loop(st6, saved_ptr.reg, saved_table.reg, saved_len.reg, saved_idx.reg))))(store_local(st5, saved_idx.reg, reg_r11()))))(st_append_text(saved_idx.state, li(reg_r11(), 0L)))))(alloc_local(st4))))(store_local(saved_len.state, saved_len.reg, tmp_len.reg))))(alloc_local(st3))))(st_append_text(tmp_len.state, mov_load(tmp_len.reg, ptr_ld.reg, 0L)))))(alloc_temp(ptr_ld.state))))(load_local(st2a, saved_ptr.reg))))(store_local(st2, saved_table.reg, reg_r11()))))(emit_load_rodata_addr(saved_table.state, reg_r11(), cce_to_unicode_rodata_offset()))))(alloc_local(st1))))(store_local(saved_ptr.state, saved_ptr.reg, ptr_reg))))(alloc_local(st));

    public static CodegenState emit_print_text_no_newline(CodegenState st, long ptr_reg) => ((Func<EmitResult, CodegenState>)((saved_ptr) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<EmitResult, CodegenState>)((saved_table) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st2a) => ((Func<EmitResult, CodegenState>)((ptr_ld) => ((Func<EmitResult, CodegenState>)((tmp_len) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<EmitResult, CodegenState>)((saved_len) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<EmitResult, CodegenState>)((saved_idx) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => emit_print_text_loop(st6, saved_ptr.reg, saved_table.reg, saved_len.reg, saved_idx.reg)))(store_local(st5, saved_idx.reg, reg_r11()))))(st_append_text(saved_idx.state, li(reg_r11(), 0L)))))(alloc_local(st4))))(store_local(saved_len.state, saved_len.reg, tmp_len.reg))))(alloc_local(st3))))(st_append_text(tmp_len.state, mov_load(tmp_len.reg, ptr_ld.reg, 0L)))))(alloc_temp(ptr_ld.state))))(load_local(st2a, saved_ptr.reg))))(store_local(st2, saved_table.reg, reg_r11()))))(emit_load_rodata_addr(saved_table.state, reg_r11(), cce_to_unicode_rodata_offset()))))(alloc_local(st1))))(store_local(saved_ptr.state, saved_ptr.reg, ptr_reg))))(alloc_local(st));

    public static CodegenState emit_call_to(CodegenState st, string target) => ((Func<long, CodegenState>)((patch_pos) => ((Func<CodegenState, CodegenState>)((st1) => (st1 with { call_patches = ((Func<List<CallPatch>>)(() => { var _l = st1.call_patches; _l.Add(new CallPatch(patch_offset: patch_pos, target: target)); return _l; }))() })))(st_append_text(st, x86_call(0L)))))(st.text_len);

    public static long bare_metal_heap_base() => 4194304L;

    public static CodegenState emit_oom_string(CodegenState st, List<long> chars, long i)
    {
        while (true)
        {
            if ((i >= ((long)chars.Count)))
            {
            return st;
            }
            else
            {
            var _tco_0 = emit_serial_wait_and_send(st, chars[(int)i]);
            var _tco_1 = chars;
            var _tco_2 = (i + 1L);
            st = _tco_0;
            chars = _tco_1;
            i = _tco_2;
            continue;
            }
        }
    }

    public static CodegenState emit_out_of_memory_handler(CodegenState st) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => st_append_text(st7, jmp((0L - 6L)))))(st_append_text(st6, new List<long> { 244L }))))(emit_oom_string(st5, new List<long> { 79L, 85L, 84L, 32L, 79L, 70L, 32L, 77L, 69L, 77L, 79L, 82L, 89L, 10L }, 0L))))(st_append_text(st4, mov_rr(reg_rbp(), reg_rsp())))))(st_append_text(st3, li(reg_rsp(), bare_metal_stack_top())))))(st_append_text(st2, mov_rr(reg_r12(), reg_r10())))))(st_append_text(st1, mov_rr(reg_rbx(), reg_rsp())))))(record_func_offset(st, "\u0055\u0055\u0010\u0019\u000E\u0055\u0010\u001C\u0055\u001A\u000D\u001A\u0010\u0015\u001E"));

    public static List<PatchEntry> collect_overflow_patches(List<long> checks, long oom_offset, long i, List<PatchEntry> acc)
    {
        while (true)
        {
            if ((i >= ((long)checks.Count)))
            {
            return acc;
            }
            else
            {
            var pos = checks[(int)i];
            var rel32 = (oom_offset - (pos + 6L));
            var _tco_0 = checks;
            var _tco_1 = oom_offset;
            var _tco_2 = (i + 1L);
            var _tco_3 = ((Func<List<PatchEntry>>)(() => { var _l = acc; _l.Add(make_i32_patch((pos + 2L), rel32)); return _l; }))();
            checks = _tco_0;
            oom_offset = _tco_1;
            i = _tco_2;
            acc = _tco_3;
            continue;
            }
        }
    }

    public static CodegenState emit_start(CodegenState st, long first_stub_vaddr, long syscall_handler_offset) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => ((Func<CodegenState, CodegenState>)((st12) => ((Func<CodegenState, CodegenState>)((st13) => ((Func<CodegenState, CodegenState>)((st14) => ((Func<CodegenState, CodegenState>)((st15) => ((Func<long, CodegenState>)((syscall_vaddr) => ((Func<CodegenState, CodegenState>)((st16) => ((Func<CodegenState, CodegenState>)((st17) => ((Func<CodegenState, CodegenState>)((st18) => ((Func<CodegenState, CodegenState>)((st19) => ((Func<CodegenState, CodegenState>)((st20) => ((Func<CodegenState, CodegenState>)((st21) => ((Func<CodegenState, CodegenState>)((st22) => ((Func<CodegenState, CodegenState>)((st23) => ((Func<CodegenState, CodegenState>)((st24) => ((Func<CodegenState, CodegenState>)((st25) => ((Func<CodegenState, CodegenState>)((st26) => ((Func<CodegenState, CodegenState>)((st27) => ((Func<CodegenState, CodegenState>)((st28) => ((Func<CodegenState, CodegenState>)((st29) => ((Func<CodegenState, CodegenState>)((st30) => ((Func<CodegenState, CodegenState>)((st31) => ((Func<CodegenState, CodegenState>)((st32) => ((Func<CodegenState, CodegenState>)((st33) => ((Func<CodegenState, CodegenState>)((st34) => ((Func<CodegenState, CodegenState>)((st35) => ((Func<CodegenState, CodegenState>)((st36) => ((Func<CodegenState, CodegenState>)((st37) => ((Func<CodegenState, CodegenState>)((st38) => ((Func<CodegenState, CodegenState>)((st39) => ((Func<CodegenState, CodegenState>)((st40) => ((Func<CodegenState, CodegenState>)((st41) => ((Func<long, CodegenState>)((repl_loop) => ((Func<CodegenState, CodegenState>)((st42) => ((Func<CodegenState, CodegenState>)((st43) => ((Func<CodegenState, CodegenState>)((st44) => ((Func<CodegenState, CodegenState>)((st45) => ((Func<CodegenState, CodegenState>)((st46) => ((Func<CodegenState, CodegenState>)((st47) => ((Func<CodegenState, CodegenState>)((st48) => ((Func<CodegenState, CodegenState>)((st48a) => ((Func<CodegenState, CodegenState>)((st48b) => ((Func<CodegenState, CodegenState>)((st49) => ((Func<CodegenState, CodegenState>)((st50) => ((Func<CodegenState, CodegenState>)((st51) => ((Func<CodegenState, CodegenState>)((st52) => ((Func<CodegenState, CodegenState>)((st53) => ((Func<CodegenState, CodegenState>)((st54) => ((Func<CodegenState, CodegenState>)((st55) => ((Func<CodegenState, CodegenState>)((st56) => ((Func<CodegenState, CodegenState>)((st57) => ((Func<CodegenState, CodegenState>)((st58) => ((Func<CodegenState, CodegenState>)((st59) => ((Func<CodegenState, CodegenState>)((st60) => ((Func<CodegenState, CodegenState>)((st61) => ((Func<CodegenState, CodegenState>)((st62) => ((Func<CodegenState, CodegenState>)((st63) => ((Func<CodegenState, CodegenState>)((st64) => ((Func<CodegenState, CodegenState>)((st65) => st_append_text(st65, jmp((repl_loop - (st65.text_len + 5L))))))(st_append_text(st64, mov_store(reg_r11(), reg_rax(), 0L)))))(st_append_text(st63, li(reg_rax(), bare_metal_stack_top())))))(st_append_text(st62, li(reg_r11(), stack_min_rsp_addr())))))(emit_serial_wait_and_send(st61, 10L))))(emit_inline_itoa_and_print(st60))))(st_append_text(st59, mov_rr(reg_rax(), reg_rsi())))))(st_append_text(st58, sub_rr(reg_rsi(), reg_rdi())))))(st_append_text(st57, mov_load(reg_rdi(), reg_rdi(), 0L)))))(st_append_text(st56, li(reg_rdi(), stack_min_rsp_addr())))))(st_append_text(st55, li(reg_rsi(), bare_metal_stack_top())))))(emit_serial_string(st54, new List<long> { 83L, 84L, 65L, 67L, 75L, 58L }))))(emit_serial_wait_and_send(st53, 10L))))(emit_inline_itoa_and_print(st52))))(st_append_text(st51, mov_load(reg_rax(), reg_rdi(), 0L)))))(st_append_text(st50, li(reg_rdi(), heap_hwm_addr())))))(emit_serial_string(st49, new List<long> { 72L, 69L, 65L, 80L, 58L }))))(emit_update_heap_hwm(st48b))))(emit_serial_wait_and_send(st48a, 10L))))(emit_inline_itoa_and_print(st48))))(emit_call_to(st47, "\u001A\u000F\u0011\u0012"))))(st_append_text(st46, mov_store(reg_rdi(), reg_r10(), 0L)))))(st_append_text(st45, li(reg_rdi(), heap_hwm_addr())))))(st_append_text(st44, mov_load(reg_r15(), reg_rdi(), 0L)))))(st_append_text(st43, li(reg_rdi(), result_arena_base_addr())))))(st_append_text(st42, mov_load(reg_r10(), reg_rdi(), 0L)))))(st_append_text(st41, li(reg_rdi(), arena_base_addr())))))(st41.text_len)))(st_append_text(st40, mov_store(reg_rdi(), reg_r15(), 0L)))))(st_append_text(st39, li(reg_rdi(), result_arena_base_addr())))))(st_append_text(st38, mov_store(reg_rdi(), reg_r10(), 0L)))))(st_append_text(st37, li(reg_rdi(), arena_base_addr())))))(emit_serial_string(st36, new List<long> { 82L, 69L, 65L, 68L, 89L, 10L }))))(emit_interrupt_setup(st35))))(emit_idt_entries(st34, first_stub_vaddr))))(st_append_text(st33, mov_store(reg_rdi(), reg_rax(), 0L)))))(st_append_text(st32, li(reg_rdi(), serial_read_pos_addr())))))(st_append_text(st31, mov_store(reg_rdi(), reg_rax(), 0L)))))(st_append_text(st30, li(reg_rax(), 0L)))))(st_append_text(st29, li(reg_rdi(), serial_write_pos_addr())))))(emit_out_byte(st28, 1017L, 1L))))(emit_out_byte(st27, 1020L, 11L))))(emit_out_byte(st26, 1018L, 199L))))(emit_out_byte(st25, 1019L, 3L))))(emit_out_byte(st24, 1017L, 0L))))(emit_out_byte(st23, 1016L, 1L))))(emit_out_byte(st22, 1019L, 128L))))(st_append_text(st21, new List<long> { 15L, 48L }))))(st_append_text(st20, new List<long> { 72L, 131L, 200L, 1L }))))(st_append_text(st19, new List<long> { 15L, 50L }))))(st_append_text(st18, li(reg_rcx(), 3221225600L)))))(emit_write_msr(st17, 3221225604L, 512L))))(emit_write_msr(st16, 3221225602L, syscall_vaddr))))(emit_write_msr(st15, 3221225601L, (8L * 4294967296L)))))((bare_metal_load_addr() + syscall_handler_offset))))(emit_grant_capability(st14, 1L, cap_console()))))(emit_grant_capability(st13, 0L, cap_concurrent()))))(emit_grant_capability(st12, 0L, cap_console()))))(emit_process_setup(st11))))(st_append_text(st10, mov_store(reg_r11(), reg_r15(), 0L)))))(emit_load_rodata_addr(st9, reg_r11(), result_base_rodata_offset()))))(st_append_text(st8, mov_store(reg_r11(), reg_rax(), 0L)))))(st_append_text(st7, li(reg_rax(), bare_metal_stack_top())))))(st_append_text(st6, li(reg_r11(), stack_min_rsp_addr())))))(st_append_text(st5, mov_rr(reg_r15(), reg_r10())))))(st_append_text(st4, li(reg_r10(), bare_metal_heap_base())))))(st_append_text(st3, mov_rr(reg_rbp(), reg_rsp())))))(st_append_text(st2, li(reg_rsp(), bare_metal_stack_top())))))(st_append_text(st1, cli()))))(record_func_offset(st, "\u0055\u0055\u0013\u000E\u000F\u0015\u000E"));

    public static List<long> flatten_chunks(List<List<long>> chunks) => ((Func<List<List<long>>, List<long>>)((chunk_list) => flatten_chunks_loop(chunk_list, 0L, ((long)chunk_list.Count), new List<long>())))(chunks);

    public static List<long> flatten_chunks_loop(List<List<long>> chunks, long i, long len, List<long> acc) => ((i == len) ? acc : flatten_one_chunk(chunks[(int)i], 0L, ((long)chunks[(int)i].Count), acc, chunks, (i + 1L), len));

    public static List<long> flatten_one_chunk(List<long> chunk, long j, long clen, List<long> acc, List<List<long>> chunks, long next_i, long len)
    {
        while (true)
        {
            if ((j == clen))
            {
            return flatten_chunks_loop(chunks, next_i, len, acc);
            }
            else
            {
            var _tco_0 = chunk;
            var _tco_1 = (j + 1L);
            var _tco_2 = clen;
            var _tco_3 = ((Func<List<long>>)(() => { var _l = acc; _l.Add(chunk[(int)j]); return _l; }))();
            var _tco_4 = chunks;
            var _tco_5 = next_i;
            var _tco_6 = len;
            chunk = _tco_0;
            j = _tco_1;
            clen = _tco_2;
            acc = _tco_3;
            chunks = _tco_4;
            next_i = _tco_5;
            len = _tco_6;
            continue;
            }
        }
    }

    public static long text_buf_size() => 2097152L;

    public static long rodata_buf_size() => 524288L;

    public static long apply_one_patch_buf(long base_addr, PatchEntry p) => (((_Buf.buf_write_byte(base_addr, p.pos, p.b0) + _Buf.buf_write_byte(base_addr, (p.pos + 1L), p.b1)) + _Buf.buf_write_byte(base_addr, (p.pos + 2L), p.b2)) + _Buf.buf_write_byte(base_addr, (p.pos + 3L), p.b3));

    public static long apply_all_patches_buf(long base_addr, List<PatchEntry> patches, long i)
    {
        while (true)
        {
            if ((i >= ((long)patches.Count)))
            {
            return 0L;
            }
            else
            {
            var r = apply_one_patch_buf(base_addr, patches[(int)i]);
            var _tco_0 = base_addr;
            var _tco_1 = patches;
            var _tco_2 = (i + 1L);
            base_addr = _tco_0;
            patches = _tco_1;
            i = _tco_2;
            continue;
            }
        }
    }

    public static CodegenState x86_64_init_codegen_streaming(List<TypeBinding> tdefs, long n_defs) => ((Func<TrampolineResult, CodegenState>)((tramp) => ((Func<long, CodegenState>)((text_addr) => ((Func<dynamic, CodegenState>)((ha1) => ((Func<long, CodegenState>)((rodata_addr) => ((Func<dynamic, CodegenState>)((ha2) => ((Func<long, CodegenState>)((bw1) => ((Func<long, CodegenState>)((bw2) => new CodegenState(text_buf_addr: text_addr, text_len: ((long)tramp.bytes.Count), rodata_buf_addr: rodata_addr, rodata_len: ((long)init_rodata().Count), func_offsets: _Buf.list_with_capacity((n_defs + 64L)), call_patches: _Buf.list_with_capacity((n_defs * 12L)), func_addr_fixups: _Buf.list_with_capacity((n_defs * 4L)), rodata_fixups: _Buf.list_with_capacity((n_defs * 8L)), deferred_patches: _Buf.list_with_capacity((n_defs * 4L)), locals: new List<LocalBinding>(), next_temp: 0L, next_local: 0L, spill_count: 0L, load_local_toggle: 0L, tco: new TcoState(active: false, in_tail_pos: false, loop_top: 0L, param_locals: new List<long>(), temp_locals: new List<long>(), current_func: "", saved_next_local: 0L, saved_next_temp: 0L), type_defs: tdefs, stack_overflow_checks: _Buf.list_with_capacity((n_defs + 16L)), bag: empty_bag())))(_Buf.buf_write_bytes(rodata_addr, 0L, init_rodata()))))(_Buf.buf_write_bytes(text_addr, 0L, tramp.bytes))))(_Buf.heap_advance(rodata_buf_size()))))(_Buf.heap_save())))(_Buf.heap_advance(text_buf_size()))))(_Buf.heap_save())))(bare_metal_trampoline());

    public static CodegenState x86_64_init_codegen(List<TypeBinding> tdefs) => ((Func<TrampolineResult, CodegenState>)((tramp) => ((Func<long, CodegenState>)((text_addr) => ((Func<dynamic, CodegenState>)((ha1) => ((Func<long, CodegenState>)((rodata_addr) => ((Func<dynamic, CodegenState>)((ha2) => ((Func<long, CodegenState>)((bw1) => ((Func<long, CodegenState>)((bw2) => new CodegenState(text_buf_addr: text_addr, text_len: ((long)tramp.bytes.Count), rodata_buf_addr: rodata_addr, rodata_len: ((long)init_rodata().Count), func_offsets: new List<FuncOffset>(), call_patches: new List<CallPatch>(), func_addr_fixups: new List<FuncAddrFixup>(), rodata_fixups: new List<RodataFixup>(), deferred_patches: new List<PatchEntry>(), locals: new List<LocalBinding>(), next_temp: 0L, next_local: 0L, spill_count: 0L, load_local_toggle: 0L, tco: new TcoState(active: false, in_tail_pos: false, loop_top: 0L, param_locals: new List<long>(), temp_locals: new List<long>(), current_func: "", saved_next_local: 0L, saved_next_temp: 0L), type_defs: tdefs, stack_overflow_checks: new List<long>(), bag: empty_bag())))(_Buf.buf_write_bytes(rodata_addr, 0L, init_rodata()))))(_Buf.buf_write_bytes(text_addr, 0L, tramp.bytes))))(_Buf.heap_advance(rodata_buf_size()))))(_Buf.heap_save())))(_Buf.heap_advance(text_buf_size()))))(_Buf.heap_save())))(bare_metal_trampoline());

    public static EmitChapterResult x86_64_finalize(CodegenState st1, long far_jump_patch_pos) => ((Func<CodegenState, EmitChapterResult>)((st1a) => ((Func<IsrStubResult, EmitChapterResult>)((isr_result) => ((Func<SyscallResult, EmitChapterResult>)((syscall_result) => ((Func<CodegenState, EmitChapterResult>)((st2) => ((Func<OffsetTable, EmitChapterResult>)((func_map) => ((Func<CodegenState, EmitChapterResult>)((st2a) => ((Func<CodegenState, EmitChapterResult>)((st2b) => ((Func<CodegenState, EmitChapterResult>)((st2c) => ((Func<CodegenState, EmitChapterResult>)((st2d) => ((Func<long, EmitChapterResult>)((start_offset) => ((Func<long, EmitChapterResult>)((start_addr) => ((Func<PatchEntry, EmitChapterResult>)((far_jump_entry) => ((Func<long, EmitChapterResult>)((n_call) => ((Func<List<PatchEntry>, EmitChapterResult>)((call_entries) => ((Func<long, EmitChapterResult>)((n_addr) => ((Func<List<PatchEntry>, EmitChapterResult>)((addr_entries) => ((Func<long, EmitChapterResult>)((rodata_vaddr) => ((Func<long, EmitChapterResult>)((n_rodata) => ((Func<List<PatchEntry>, EmitChapterResult>)((rodata_entries) => ((Func<long, EmitChapterResult>)((oom_offset) => ((Func<long, EmitChapterResult>)((n_overflow) => ((Func<List<PatchEntry>, EmitChapterResult>)((overflow_entries) => ((Func<List<PatchEntry>, EmitChapterResult>)((all_patches) => ((Func<long, EmitChapterResult>)((patched) => ((Func<long, EmitChapterResult>)((text_len) => ((Func<long, EmitChapterResult>)((rodata_len) => ((Func<long, EmitChapterResult>)((load_addr) => ((Func<long, EmitChapterResult>)((headers_end) => ((Func<long, EmitChapterResult>)((note_offset) => ((Func<long, EmitChapterResult>)((text_start) => ((Func<long, EmitChapterResult>)((text_end) => ((Func<long, EmitChapterResult>)((rodata_start) => ((Func<long, EmitChapterResult>)((file_size) => ((Func<long, EmitChapterResult>)((entry) => ((Func<long, EmitChapterResult>)((seg_filesz) => ((Func<long, EmitChapterResult>)((seg_memsz) => ((Func<long, EmitChapterResult>)((elf_buf) => ((Func<dynamic, EmitChapterResult>)((ha) => ((Func<List<long>, EmitChapterResult>)((hdr) => ((Func<long, EmitChapterResult>)((bw0) => ((Func<List<long>, EmitChapterResult>)((ph1) => ((Func<long, EmitChapterResult>)((bw1) => ((Func<List<long>, EmitChapterResult>)((ph2) => ((Func<long, EmitChapterResult>)((bw2) => ((Func<List<long>, EmitChapterResult>)((note) => ((Func<long, EmitChapterResult>)((bw3) => ((Func<long, EmitChapterResult>)((bw4) => ((Func<long, EmitChapterResult>)((bw5) => ((Func<List<long>, EmitChapterResult>)((elf_bytes) => new EmitChapterResult(bytes: elf_bytes, bag: st2d.bag)))(_Buf.buf_read_bytes(elf_buf, 0L, file_size))))(_Buf.buf_write_bytes(elf_buf, rodata_start, _Buf.buf_read_bytes(st2d.rodata_buf_addr, 0L, rodata_len)))))(_Buf.buf_write_bytes(elf_buf, text_start, _Buf.buf_read_bytes(st2d.text_buf_addr, 0L, text_len)))))(_Buf.buf_write_bytes(elf_buf, note_offset, note))))(pvh_note(entry))))(_Buf.buf_write_bytes(elf_buf, (elf32_header_size() + elf32_phdr_size()), ph2))))(phdr_32(pt_note(), note_offset, 0L, 0L, 20L, 20L, pf_r(), 4L))))(_Buf.buf_write_bytes(elf_buf, elf32_header_size(), ph1))))(phdr_32(pt_load(), text_start, load_addr, load_addr, seg_filesz, seg_memsz, pf_rwx(), elf_page_size()))))(_Buf.buf_write_bytes(elf_buf, 0L, hdr))))(elf32_header_bytes(entry, elf32_header_size(), 2L))))(_Buf.heap_advance(file_size))))(_Buf.heap_save())))((seg_filesz + elf_bare_metal_heap_size()))))((file_size - text_start))))((load_addr + 12L))))((rodata_start + rodata_len))))(elf_align(text_end, 8L))))((text_start + text_len))))(elf_align((note_offset + 20L), 16L))))(elf_align(headers_end, 4L))))((elf32_header_size() + (elf32_phdr_size() * 2L)))))(bare_metal_load_addr())))(st2d.rodata_len)))(st2d.text_len)))(apply_all_patches_buf(st2d.text_buf_addr, all_patches, 0L))))(Enumerable.Concat(new List<PatchEntry> { far_jump_entry }, Enumerable.Concat(call_entries, Enumerable.Concat(addr_entries, Enumerable.Concat(rodata_entries, Enumerable.Concat(overflow_entries, st2d.deferred_patches).ToList()).ToList()).ToList()).ToList()).ToList())))(collect_overflow_patches(st2d.stack_overflow_checks, oom_offset, 0L, _Buf.list_with_capacity(n_overflow)))))(((long)st2d.stack_overflow_checks.Count))))(offset_table_lookup(func_map, "\u0055\u0055\u0010\u0019\u000E\u0055\u0010\u001C\u0055\u001A\u000D\u001A\u0010\u0015\u001E"))))(collect_rodata_patches(st2d.rodata_fixups, rodata_vaddr, 0L, _Buf.list_with_capacity((n_rodata * 2L))))))(((long)st2d.rodata_fixups.Count))))(compute_rodata_vaddr_bare(st2d.text_len))))(collect_func_addr_patches(st2d.func_addr_fixups, func_map, bare_metal_load_addr(), 0L, _Buf.list_with_capacity((n_addr * 2L))))))(((long)st2d.func_addr_fixups.Count))))(collect_call_patches(st2d.call_patches, func_map, 0L, _Buf.list_with_capacity(n_call)))))(((long)st2d.call_patches.Count))))(make_i32_patch((far_jump_patch_pos + 1L), start_addr))))((bare_metal_load_addr() + start_offset))))(offset_table_lookup(func_map, "\u0055\u0055\u0013\u000E\u000F\u0015\u000E"))))(check_runtime_symbol(st2c, func_map, "\u0055\u0055\u0010\u0019\u000E\u0055\u0010\u001C\u0055\u001A\u000D\u001A\u0010\u0015\u001E"))))(check_runtime_symbol(st2b, func_map, "\u0055\u0055\u0013\u000E\u000F\u0015\u000E"))))(check_func_addr_targets(st2a, func_map, 0L))))(check_call_patch_targets(st2, func_map, 0L))))(build_offset_table(st2.func_offsets))))(emit_start(syscall_result.state, isr_result.first_stub_vaddr, syscall_result.handler_offset))))(emit_syscall_handler(isr_result.state))))(emit_isr_stubs(st1a))))(emit_out_of_memory_handler(st1));

    public static CodexType resolve_ty_deep(List<TypeBinding> tdefs, CodexType ty) => ty switch { ConstructedTy(var cname, var cargs) => (CodexType)(((Func<CodexType, CodexType>)((raw) => raw switch { RecordTy(var rn, var rf) => (CodexType)(raw), SumTy(var sn, var sc) => (CodexType)(raw), FunTy(var p, var r) => (CodexType)(strip_fun_args_emitter(r)), _ => (CodexType)(new ConstructedTy(cname, resolve_ty_list(tdefs, cargs, 0L, ((long)cargs.Count), new List<CodexType>()))), }))(lookup_type_binding(tdefs, cname.value))), FunTy(var p, var r) => (CodexType)(new FunTy(resolve_ty_deep(tdefs, p), resolve_ty_deep(tdefs, r))), ListTy(var e) => (CodexType)(new ListTy(resolve_ty_deep(tdefs, e))), LinkedListTy(var e) => (CodexType)(new LinkedListTy(resolve_ty_deep(tdefs, e))), ForAllTy(var id, var body) => (CodexType)(new ForAllTy(id, resolve_ty_deep(tdefs, body))), EffectfulTy(var effs, var ret) => (CodexType)(new EffectfulTy(effs, resolve_ty_deep(tdefs, ret))), _ => (CodexType)(ty), };

    public static List<CodexType> resolve_ty_list(List<TypeBinding> tdefs, List<CodexType> tys, long i, long len, List<CodexType> acc)
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
            var _tco_1 = tys;
            var _tco_2 = (i + 1L);
            var _tco_3 = len;
            var _tco_4 = ((Func<List<CodexType>>)(() => { var _l = acc; _l.Add(resolve_ty_deep(tdefs, tys[(int)i])); return _l; }))();
            tdefs = _tco_0;
            tys = _tco_1;
            i = _tco_2;
            len = _tco_3;
            acc = _tco_4;
            continue;
            }
        }
    }

    public static IRPat rewrite_ir_pat(List<TypeBinding> tdefs, IRPat p) => p switch { IrVarPat(var name, var t, var s) => (IRPat)(new IrVarPat(name, resolve_ty_deep(tdefs, t), s)), IrLitPat(var v, var t, var s) => (IRPat)(new IrLitPat(v, resolve_ty_deep(tdefs, t), s)), IrCtorPat(var name, var subs, var t, var s) => (IRPat)(new IrCtorPat(name, rewrite_ir_pat_list(tdefs, subs, 0L, ((long)subs.Count), new List<IRPat>()), resolve_ty_deep(tdefs, t), s)), IrWildPat(var s) => (IRPat)(p), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static List<IRPat> rewrite_ir_pat_list(List<TypeBinding> tdefs, List<IRPat> ps, long i, long len, List<IRPat> acc)
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
            var _tco_1 = ps;
            var _tco_2 = (i + 1L);
            var _tco_3 = len;
            var _tco_4 = ((Func<List<IRPat>>)(() => { var _l = acc; _l.Add(rewrite_ir_pat(tdefs, ps[(int)i])); return _l; }))();
            tdefs = _tco_0;
            ps = _tco_1;
            i = _tco_2;
            len = _tco_3;
            acc = _tco_4;
            continue;
            }
        }
    }

    public static IRExpr rewrite_ir_expr(List<TypeBinding> tdefs, IRExpr e) => e switch { IrIntLit(var v, var s) => (IRExpr)(e), IrNumLit(var v, var s) => (IRExpr)(e), IrTextLit(var v, var s) => (IRExpr)(e), IrBoolLit(var v, var s) => (IRExpr)(e), IrCharLit(var v, var s) => (IRExpr)(e), IrName(var n, var t, var s) => (IRExpr)(new IrName(n, resolve_ty_deep(tdefs, t), s)), IrBinary(var op, var l, var r, var t, var s) => (IRExpr)(new IrBinary(op, rewrite_ir_expr(tdefs, l), rewrite_ir_expr(tdefs, r), resolve_ty_deep(tdefs, t), s)), IrNegate(var x, var s) => (IRExpr)(new IrNegate(rewrite_ir_expr(tdefs, x), s)), IrIf(var c, var th, var el, var t, var s) => (IRExpr)(new IrIf(rewrite_ir_expr(tdefs, c), rewrite_ir_expr(tdefs, th), rewrite_ir_expr(tdefs, el), resolve_ty_deep(tdefs, t), s)), IrLet(var n, var t, var v, var b, var s) => (IRExpr)(new IrLet(n, resolve_ty_deep(tdefs, t), rewrite_ir_expr(tdefs, v), rewrite_ir_expr(tdefs, b), s)), IrApply(var f, var a, var t, var s) => (IRExpr)(new IrApply(rewrite_ir_expr(tdefs, f), rewrite_ir_expr(tdefs, a), resolve_ty_deep(tdefs, t), s)), IrLambda(var ps, var b, var t, var s) => (IRExpr)(new IrLambda(rewrite_ir_params(tdefs, ps, 0L, ((long)ps.Count), new List<IRParam>()), rewrite_ir_expr(tdefs, b), resolve_ty_deep(tdefs, t), s)), IrList(var es, var t, var s) => (IRExpr)(new IrList(rewrite_ir_expr_list(tdefs, es, 0L, ((long)es.Count), new List<IRExpr>()), resolve_ty_deep(tdefs, t), s)), IrMatch(var sc, var bs, var t, var s) => (IRExpr)(new IrMatch(rewrite_ir_expr(tdefs, sc), rewrite_ir_branches(tdefs, bs, 0L, ((long)bs.Count), new List<IRBranch>()), resolve_ty_deep(tdefs, t), s)), IrAct(var ss, var t, var s) => (IRExpr)(new IrAct(rewrite_ir_act_stmts(tdefs, ss, 0L, ((long)ss.Count), new List<IRActStmt>()), resolve_ty_deep(tdefs, t), s)), IrHandle(var eff, var h, var cs, var t, var s) => (IRExpr)(new IrHandle(eff, rewrite_ir_expr(tdefs, h), rewrite_ir_handle_clauses(tdefs, cs, 0L, ((long)cs.Count), new List<IRHandleClause>()), resolve_ty_deep(tdefs, t), s)), IrRecord(var n, var fs, var t, var s) => (IRExpr)(new IrRecord(n, rewrite_ir_record_fields(tdefs, fs, 0L, ((long)fs.Count), new List<IRFieldVal>()), resolve_ty_deep(tdefs, t), s)), IrFieldAccess(var r, var f, var t, var s) => (IRExpr)(new IrFieldAccess(rewrite_ir_expr(tdefs, r), f, resolve_ty_deep(tdefs, t), s)), IrFork(var body, var t, var s) => (IRExpr)(new IrFork(rewrite_ir_expr(tdefs, body), resolve_ty_deep(tdefs, t), s)), IrAwait(var task, var t, var s) => (IRExpr)(new IrAwait(rewrite_ir_expr(tdefs, task), resolve_ty_deep(tdefs, t), s)), IrError(var m, var t, var s) => (IRExpr)(new IrError(m, resolve_ty_deep(tdefs, t), s)), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static List<IRExpr> rewrite_ir_expr_list(List<TypeBinding> tdefs, List<IRExpr> es, long i, long len, List<IRExpr> acc)
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
            var _tco_1 = es;
            var _tco_2 = (i + 1L);
            var _tco_3 = len;
            var _tco_4 = ((Func<List<IRExpr>>)(() => { var _l = acc; _l.Add(rewrite_ir_expr(tdefs, es[(int)i])); return _l; }))();
            tdefs = _tco_0;
            es = _tco_1;
            i = _tco_2;
            len = _tco_3;
            acc = _tco_4;
            continue;
            }
        }
    }

    public static List<IRParam> rewrite_ir_params(List<TypeBinding> tdefs, List<IRParam> ps, long i, long len, List<IRParam> acc)
    {
        while (true)
        {
            if ((i == len))
            {
            return acc;
            }
            else
            {
            var p = ps[(int)i];
            var _tco_0 = tdefs;
            var _tco_1 = ps;
            var _tco_2 = (i + 1L);
            var _tco_3 = len;
            var _tco_4 = ((Func<List<IRParam>>)(() => { var _l = acc; _l.Add(new IRParam(name: p.name, type_val: resolve_ty_deep(tdefs, p.type_val), span: p.span)); return _l; }))();
            tdefs = _tco_0;
            ps = _tco_1;
            i = _tco_2;
            len = _tco_3;
            acc = _tco_4;
            continue;
            }
        }
    }

    public static List<IRBranch> rewrite_ir_branches(List<TypeBinding> tdefs, List<IRBranch> bs, long i, long len, List<IRBranch> acc)
    {
        while (true)
        {
            if ((i == len))
            {
            return acc;
            }
            else
            {
            var b = bs[(int)i];
            var _tco_0 = tdefs;
            var _tco_1 = bs;
            var _tco_2 = (i + 1L);
            var _tco_3 = len;
            var _tco_4 = ((Func<List<IRBranch>>)(() => { var _l = acc; _l.Add(new IRBranch(pattern: rewrite_ir_pat(tdefs, b.pattern), body: rewrite_ir_expr(tdefs, b.body), span: b.span)); return _l; }))();
            tdefs = _tco_0;
            bs = _tco_1;
            i = _tco_2;
            len = _tco_3;
            acc = _tco_4;
            continue;
            }
        }
    }

    public static List<IRActStmt> rewrite_ir_act_stmts(List<TypeBinding> tdefs, List<IRActStmt> ss, long i, long len, List<IRActStmt> acc)
    {
        while (true)
        {
            if ((i == len))
            {
            return acc;
            }
            else
            {
            var s = ss[(int)i];
            var ns = s switch { IrDoBind(var n, var t, var v, var sp) => (IRActStmt)(new IrDoBind(n, resolve_ty_deep(tdefs, t), rewrite_ir_expr(tdefs, v), sp)), IrDoExec(var v, var sp) => (IRActStmt)(new IrDoExec(rewrite_ir_expr(tdefs, v), sp)), _ => throw new InvalidOperationException("Non-exhaustive match"), };
            var _tco_0 = tdefs;
            var _tco_1 = ss;
            var _tco_2 = (i + 1L);
            var _tco_3 = len;
            var _tco_4 = ((Func<List<IRActStmt>>)(() => { var _l = acc; _l.Add(ns); return _l; }))();
            tdefs = _tco_0;
            ss = _tco_1;
            i = _tco_2;
            len = _tco_3;
            acc = _tco_4;
            continue;
            }
        }
    }

    public static List<IRHandleClause> rewrite_ir_handle_clauses(List<TypeBinding> tdefs, List<IRHandleClause> cs, long i, long len, List<IRHandleClause> acc)
    {
        while (true)
        {
            if ((i == len))
            {
            return acc;
            }
            else
            {
            var c = cs[(int)i];
            var _tco_0 = tdefs;
            var _tco_1 = cs;
            var _tco_2 = (i + 1L);
            var _tco_3 = len;
            var _tco_4 = ((Func<List<IRHandleClause>>)(() => { var _l = acc; _l.Add(new IRHandleClause(op_name: c.op_name, resume_name: c.resume_name, body: rewrite_ir_expr(tdefs, c.body), span: c.span)); return _l; }))();
            tdefs = _tco_0;
            cs = _tco_1;
            i = _tco_2;
            len = _tco_3;
            acc = _tco_4;
            continue;
            }
        }
    }

    public static List<IRFieldVal> rewrite_ir_record_fields(List<TypeBinding> tdefs, List<IRFieldVal> fs, long i, long len, List<IRFieldVal> acc)
    {
        while (true)
        {
            if ((i == len))
            {
            return acc;
            }
            else
            {
            var f = fs[(int)i];
            var _tco_0 = tdefs;
            var _tco_1 = fs;
            var _tco_2 = (i + 1L);
            var _tco_3 = len;
            var _tco_4 = ((Func<List<IRFieldVal>>)(() => { var _l = acc; _l.Add(new IRFieldVal(name: f.name, value: rewrite_ir_expr(tdefs, f.value), span: f.span)); return _l; }))();
            tdefs = _tco_0;
            fs = _tco_1;
            i = _tco_2;
            len = _tco_3;
            acc = _tco_4;
            continue;
            }
        }
    }

    public static IRDef rewrite_ir_def(List<TypeBinding> tdefs, IRDef d) => new IRDef(name: d.name, @params: rewrite_ir_params(tdefs, d.@params, 0L, ((long)d.@params.Count), new List<IRParam>()), type_val: resolve_ty_deep(tdefs, d.type_val), body: rewrite_ir_expr(tdefs, d.body), chapter_slug: d.chapter_slug, span: d.span);

    public static List<IRDef> rewrite_ir_defs(List<TypeBinding> tdefs, List<IRDef> defs, long i, long len, List<IRDef> acc)
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
            var _tco_1 = defs;
            var _tco_2 = (i + 1L);
            var _tco_3 = len;
            var _tco_4 = ((Func<List<IRDef>>)(() => { var _l = acc; _l.Add(rewrite_ir_def(tdefs, defs[(int)i])); return _l; }))();
            tdefs = _tco_0;
            defs = _tco_1;
            i = _tco_2;
            len = _tco_3;
            acc = _tco_4;
            continue;
            }
        }
    }

    public static bool type_has_errorty(CodexType ty)
    {
        while (true)
        {
            var _tco_s = ty;
            if (_tco_s is ErrorTy _tco_m0)
            {
            return true;
            }
            else if (_tco_s is FunTy _tco_m1)
            {
                var p = _tco_m1.Field0;
                var r = _tco_m1.Field1;
            if (type_has_errorty(p))
            {
            return true;
            }
            else
            {
            var _tco_0 = r;
            ty = _tco_0;
            continue;
            }
            }
            else if (_tco_s is ListTy _tco_m2)
            {
                var e = _tco_m2.Field0;
            var _tco_0 = e;
            ty = _tco_0;
            continue;
            }
            else if (_tco_s is LinkedListTy _tco_m3)
            {
                var e = _tco_m3.Field0;
            var _tco_0 = e;
            ty = _tco_0;
            continue;
            }
            else if (_tco_s is ForAllTy _tco_m4)
            {
                var id = _tco_m4.Field0;
                var body = _tco_m4.Field1;
            var _tco_0 = body;
            ty = _tco_0;
            continue;
            }
            else if (_tco_s is EffectfulTy _tco_m5)
            {
                var effs = _tco_m5.Field0;
                var ret = _tco_m5.Field1;
            var _tco_0 = ret;
            ty = _tco_0;
            continue;
            }
            {
            return false;
            }
        }
    }

    public static CodegenState check_ir_expr_errorty(CodegenState st, IRExpr e)
    {
        while (true)
        {
            var ty = ir_expr_type(e);
            var st1 = (type_has_errorty(ty) ? st_add_error(st, cdx_error_type_in_ir(), "\u002B\u002F\u0002\u0012\u0010\u0016\u000D\u0002\u0015\u000D\u000F\u0018\u0014\u000D\u0016\u0002\u0018\u0010\u0016\u000D\u001D\u000D\u0012\u0002\u001B\u0011\u000E\u0014\u0002\u0027\u0015\u0015\u0010\u0015\u0028\u001E\u0002\u0011\u0012\u0002\u0011\u000E\u0013\u0002\u000E\u001E\u001F\u000D", ir_expr_span(e)) : st);
            var _tco_s = e;
            if (_tco_s is IrBinary _tco_m0)
            {
                var op = _tco_m0.Field0;
                var l = _tco_m0.Field1;
                var r = _tco_m0.Field2;
                var t = _tco_m0.Field3;
                var s = _tco_m0.Field4;
            var _tco_0 = check_ir_expr_errorty(st1, l);
            var _tco_1 = r;
            st = _tco_0;
            e = _tco_1;
            continue;
            }
            else if (_tco_s is IrNegate _tco_m1)
            {
                var x = _tco_m1.Field0;
                var s = _tco_m1.Field1;
            var _tco_0 = st1;
            var _tco_1 = x;
            st = _tco_0;
            e = _tco_1;
            continue;
            }
            else if (_tco_s is IrIf _tco_m2)
            {
                var c = _tco_m2.Field0;
                var th = _tco_m2.Field1;
                var el = _tco_m2.Field2;
                var t = _tco_m2.Field3;
                var s = _tco_m2.Field4;
            var _tco_0 = check_ir_expr_errorty(check_ir_expr_errorty(st1, c), th);
            var _tco_1 = el;
            st = _tco_0;
            e = _tco_1;
            continue;
            }
            else if (_tco_s is IrLet _tco_m3)
            {
                var n = _tco_m3.Field0;
                var t = _tco_m3.Field1;
                var v = _tco_m3.Field2;
                var b = _tco_m3.Field3;
                var s = _tco_m3.Field4;
            var _tco_0 = check_ir_expr_errorty(st1, v);
            var _tco_1 = b;
            st = _tco_0;
            e = _tco_1;
            continue;
            }
            else if (_tco_s is IrApply _tco_m4)
            {
                var f = _tco_m4.Field0;
                var a = _tco_m4.Field1;
                var t = _tco_m4.Field2;
                var s = _tco_m4.Field3;
            var _tco_0 = check_ir_expr_errorty(st1, f);
            var _tco_1 = a;
            st = _tco_0;
            e = _tco_1;
            continue;
            }
            else if (_tco_s is IrLambda _tco_m5)
            {
                var ps = _tco_m5.Field0;
                var b = _tco_m5.Field1;
                var t = _tco_m5.Field2;
                var s = _tco_m5.Field3;
            var _tco_0 = st1;
            var _tco_1 = b;
            st = _tco_0;
            e = _tco_1;
            continue;
            }
            else if (_tco_s is IrList _tco_m6)
            {
                var es = _tco_m6.Field0;
                var t = _tco_m6.Field1;
                var s = _tco_m6.Field2;
            return check_ir_list_errorty(st1, es, 0L);
            }
            else if (_tco_s is IrMatch _tco_m7)
            {
                var sc = _tco_m7.Field0;
                var bs = _tco_m7.Field1;
                var t = _tco_m7.Field2;
                var s = _tco_m7.Field3;
            return check_ir_branches_errorty(check_ir_expr_errorty(st1, sc), bs, 0L);
            }
            else if (_tco_s is IrAct _tco_m8)
            {
                var ss = _tco_m8.Field0;
                var t = _tco_m8.Field1;
                var s = _tco_m8.Field2;
            return check_ir_act_stmts_errorty(st1, ss, 0L);
            }
            else if (_tco_s is IrHandle _tco_m9)
            {
                var eff = _tco_m9.Field0;
                var h = _tco_m9.Field1;
                var cs = _tco_m9.Field2;
                var t = _tco_m9.Field3;
                var s = _tco_m9.Field4;
            return check_ir_handle_clauses_errorty(check_ir_expr_errorty(st1, h), cs, 0L);
            }
            else if (_tco_s is IrRecord _tco_m10)
            {
                var n = _tco_m10.Field0;
                var fs = _tco_m10.Field1;
                var t = _tco_m10.Field2;
                var s = _tco_m10.Field3;
            return check_ir_record_fields_errorty(st1, fs, 0L);
            }
            else if (_tco_s is IrFieldAccess _tco_m11)
            {
                var r = _tco_m11.Field0;
                var f = _tco_m11.Field1;
                var t = _tco_m11.Field2;
                var s = _tco_m11.Field3;
            var _tco_0 = st1;
            var _tco_1 = r;
            st = _tco_0;
            e = _tco_1;
            continue;
            }
            else if (_tco_s is IrFork _tco_m12)
            {
                var body = _tco_m12.Field0;
                var t = _tco_m12.Field1;
                var s = _tco_m12.Field2;
            var _tco_0 = st1;
            var _tco_1 = body;
            st = _tco_0;
            e = _tco_1;
            continue;
            }
            else if (_tco_s is IrAwait _tco_m13)
            {
                var task = _tco_m13.Field0;
                var t = _tco_m13.Field1;
                var s = _tco_m13.Field2;
            var _tco_0 = st1;
            var _tco_1 = task;
            st = _tco_0;
            e = _tco_1;
            continue;
            }
            {
            return st1;
            }
        }
    }

    public static CodegenState check_ir_list_errorty(CodegenState st, List<IRExpr> es, long i)
    {
        while (true)
        {
            if ((i == ((long)es.Count)))
            {
            return st;
            }
            else
            {
            var _tco_0 = check_ir_expr_errorty(st, es[(int)i]);
            var _tco_1 = es;
            var _tco_2 = (i + 1L);
            st = _tco_0;
            es = _tco_1;
            i = _tco_2;
            continue;
            }
        }
    }

    public static CodegenState check_ir_branches_errorty(CodegenState st, List<IRBranch> bs, long i)
    {
        while (true)
        {
            if ((i == ((long)bs.Count)))
            {
            return st;
            }
            else
            {
            var b = bs[(int)i];
            var _tco_0 = check_ir_expr_errorty(st, b.body);
            var _tco_1 = bs;
            var _tco_2 = (i + 1L);
            st = _tco_0;
            bs = _tco_1;
            i = _tco_2;
            continue;
            }
        }
    }

    public static CodegenState check_ir_act_stmts_errorty(CodegenState st, List<IRActStmt> ss, long i)
    {
        while (true)
        {
            if ((i == ((long)ss.Count)))
            {
            return st;
            }
            else
            {
            var s = ss[(int)i];
            var _tco_s = s;
            if (_tco_s is IrDoBind _tco_m0)
            {
                var n = _tco_m0.Field0;
                var t = _tco_m0.Field1;
                var v = _tco_m0.Field2;
                var sp = _tco_m0.Field3;
            var _tco_0 = check_ir_expr_errorty(st, v);
            var _tco_1 = ss;
            var _tco_2 = (i + 1L);
            st = _tco_0;
            ss = _tco_1;
            i = _tco_2;
            continue;
            }
            else if (_tco_s is IrDoExec _tco_m1)
            {
                var v = _tco_m1.Field0;
                var sp = _tco_m1.Field1;
            var _tco_0 = check_ir_expr_errorty(st, v);
            var _tco_1 = ss;
            var _tco_2 = (i + 1L);
            st = _tco_0;
            ss = _tco_1;
            i = _tco_2;
            continue;
            }
            }
        }
    }

    public static CodegenState check_ir_handle_clauses_errorty(CodegenState st, List<IRHandleClause> cs, long i)
    {
        while (true)
        {
            if ((i == ((long)cs.Count)))
            {
            return st;
            }
            else
            {
            var c = cs[(int)i];
            var _tco_0 = check_ir_expr_errorty(st, c.body);
            var _tco_1 = cs;
            var _tco_2 = (i + 1L);
            st = _tco_0;
            cs = _tco_1;
            i = _tco_2;
            continue;
            }
        }
    }

    public static CodegenState check_ir_record_fields_errorty(CodegenState st, List<IRFieldVal> fs, long i)
    {
        while (true)
        {
            if ((i == ((long)fs.Count)))
            {
            return st;
            }
            else
            {
            var f = fs[(int)i];
            var _tco_0 = check_ir_expr_errorty(st, f.value);
            var _tco_1 = fs;
            var _tco_2 = (i + 1L);
            st = _tco_0;
            fs = _tco_1;
            i = _tco_2;
            continue;
            }
        }
    }

    public static CodegenState check_ir_defs_errorty(CodegenState st, List<IRDef> defs, long i)
    {
        while (true)
        {
            if ((i == ((long)defs.Count)))
            {
            return st;
            }
            else
            {
            var d = defs[(int)i];
            var st1 = (type_has_errorty(d.type_val) ? st_add_error(st, cdx_error_type_in_ir(), ("\u0030\u000D\u001C\u0011\u0012\u0011\u000E\u0011\u0010\u0012\u0002\u0047" + (d.name + "\u0047\u0002\u0014\u000F\u0013\u0002\u0027\u0015\u0015\u0010\u0015\u0028\u001E\u0002\u0011\u0012\u0002\u0011\u000E\u0013\u0002\u000E\u001E\u001F\u000D")), d.span) : st);
            var _tco_0 = check_ir_expr_errorty(st1, d.body);
            var _tco_1 = defs;
            var _tco_2 = (i + 1L);
            st = _tco_0;
            defs = _tco_1;
            i = _tco_2;
            continue;
            }
        }
    }

    public static EmitChapterResult x86_64_emit_chapter(IRChapter m, List<TypeBinding> tdefs) => ((Func<TrampolineResult, EmitChapterResult>)((tramp) => ((Func<CodegenState, EmitChapterResult>)((st0) => ((Func<List<IRDef>, EmitChapterResult>)((defs_resolved) => ((Func<CodegenState, EmitChapterResult>)((st_checked) => ((Func<CodegenState, EmitChapterResult>)((st0a) => ((Func<CodegenState, EmitChapterResult>)((st1) => x86_64_finalize(st1, tramp.far_jump_patch_pos)))(emit__x86_64_code_generator_emit_all_defs(st0a, defs_resolved, 0L))))(emit_runtime_helpers(st_checked))))(check_ir_defs_errorty(st0, defs_resolved, 0L))))(rewrite_ir_defs(tdefs, m.defs, 0L, ((long)m.defs.Count), new List<IRDef>()))))(x86_64_init_codegen(tdefs))))(bare_metal_trampoline());

    public static long idt_base() => 24576L;

    public static long tick_count_addr() => 28672L;

    public static long key_buffer_addr() => 28680L;

    public static long current_proc_addr() => 28688L;

    public static long arena_base_addr() => 28696L;

    public static long serial_write_pos_addr() => 28704L;

    public static long serial_read_pos_addr() => 28712L;

    public static long result_arena_base_addr() => 28720L;

    public static long heap_hwm_addr() => 28728L;

    public static long stack_min_rsp_addr() => 28736L;

    public static long serial_ring_buf_addr() => 3145728L;

    public static long serial_ring_buf_size() => 1048576L;

    public static long serial_ring_buf_mask() => 1048575L;

    public static long proc_table_base() => 20480L;

    public static long proc_entry_size() => 256L;

    public static long proc_cap_offset() => 56L;

    public static long cap_console() => 0L;

    public static long cap_concurrent() => 3L;

    public static CodegenState emit_pic_init(CodegenState st) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => emit_out_byte(st9, 161L, 255L)))(emit_out_byte(st8, 33L, 236L))))(emit_out_byte(st7, 161L, 1L))))(emit_out_byte(st6, 33L, 1L))))(emit_out_byte(st5, 161L, 2L))))(emit_out_byte(st4, 33L, 4L))))(emit_out_byte(st3, 161L, 40L))))(emit_out_byte(st2, 33L, 32L))))(emit_out_byte(st1, 160L, 17L))))(emit_out_byte(st, 32L, 17L));

    public static CodegenState emit_print_hex_nibble_rdi(CodegenState st) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<long, CodegenState>)((jge_pos) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<long, CodegenState>)((jmp_pos) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => emit_serial_send_rdi(st8)))(patch_jmp_at(st7, jmp_pos, st7.text_len))))(st_append_text(st6, add_ri(reg_rdi(), 87L)))))(patch_jcc_at(st5, jge_pos, st5.text_len))))(st_append_text(st4, jmp(0L)))))(st4.text_len)))(st_append_text(st3, add_ri(reg_rdi(), 48L)))))(st_append_text(st2, jcc(cc_ge(), 0L)))))(st2.text_len)))(st_append_text(st1, cmp_ri(reg_rdi(), 10L)))))(st_append_text(st, and_ri(reg_rdi(), 15L)));

    public static CodegenState emit_print_hex_qword_rdi_loop(CodegenState st, long nibble_idx)
    {
        while (true)
        {
            if ((nibble_idx < 0L))
            {
            return st;
            }
            else
            {
            var st1 = st_append_text(st, push_r(reg_rdi()));
            var shift = (nibble_idx * 4L);
            var st2 = ((shift == 0L) ? st1 : st_append_text(st1, shr_ri(reg_rdi(), shift)));
            var st3 = emit_print_hex_nibble_rdi(st2);
            var st4 = st_append_text(st3, pop_r(reg_rdi()));
            var _tco_0 = st4;
            var _tco_1 = (nibble_idx - 1L);
            st = _tco_0;
            nibble_idx = _tco_1;
            continue;
            }
        }
    }

    public static CodegenState emit_print_hex_qword_rdi(CodegenState st) => emit_print_hex_qword_rdi_loop(st, 15L);

    public static CodegenState emit_print_hex_byte_rdi(CodegenState st) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => emit_print_hex_nibble_rdi(st4)))(st_append_text(st3, pop_r(reg_rdi())))))(emit_print_hex_nibble_rdi(st2))))(st_append_text(st1, shr_ri(reg_rdi(), 4L)))))(st_append_text(st, push_r(reg_rdi())));

    public static CodegenState emit_cpu_exception_dump(CodegenState st) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => ((Func<CodegenState, CodegenState>)((st12) => ((Func<CodegenState, CodegenState>)((st13) => ((Func<CodegenState, CodegenState>)((st13a) => ((Func<CodegenState, CodegenState>)((st13b) => ((Func<CodegenState, CodegenState>)((st13c) => ((Func<CodegenState, CodegenState>)((st13d) => ((Func<CodegenState, CodegenState>)((st13e) => ((Func<CodegenState, CodegenState>)((st13f) => ((Func<CodegenState, CodegenState>)((st13g) => ((Func<CodegenState, CodegenState>)((st13h) => ((Func<CodegenState, CodegenState>)((st13i) => ((Func<CodegenState, CodegenState>)((st13j) => ((Func<CodegenState, CodegenState>)((st13k) => ((Func<CodegenState, CodegenState>)((st13l) => ((Func<CodegenState, CodegenState>)((st13m) => ((Func<CodegenState, CodegenState>)((st13n) => ((Func<CodegenState, CodegenState>)((st13o) => ((Func<CodegenState, CodegenState>)((st13p) => ((Func<CodegenState, CodegenState>)((st13q) => ((Func<CodegenState, CodegenState>)((st13r) => ((Func<CodegenState, CodegenState>)((st13s) => ((Func<CodegenState, CodegenState>)((st13t) => ((Func<CodegenState, CodegenState>)((st13u) => ((Func<CodegenState, CodegenState>)((st13v) => ((Func<CodegenState, CodegenState>)((st13w) => ((Func<CodegenState, CodegenState>)((st13x) => ((Func<CodegenState, CodegenState>)((st13y) => ((Func<CodegenState, CodegenState>)((st13z) => ((Func<CodegenState, CodegenState>)((st13aa) => ((Func<CodegenState, CodegenState>)((st13ab) => ((Func<CodegenState, CodegenState>)((st13ac) => ((Func<CodegenState, CodegenState>)((st13ad) => ((Func<CodegenState, CodegenState>)((st13ae) => ((Func<CodegenState, CodegenState>)((st13af) => ((Func<CodegenState, CodegenState>)((st13ag) => ((Func<CodegenState, CodegenState>)((st13ah) => ((Func<CodegenState, CodegenState>)((st13ai) => ((Func<CodegenState, CodegenState>)((st13aj) => ((Func<CodegenState, CodegenState>)((st13ak) => ((Func<CodegenState, CodegenState>)((st13al) => ((Func<CodegenState, CodegenState>)((st13am) => ((Func<CodegenState, CodegenState>)((st13an) => ((Func<CodegenState, CodegenState>)((st13ao) => ((Func<CodegenState, CodegenState>)((st13ap) => ((Func<CodegenState, CodegenState>)((st13aq) => ((Func<CodegenState, CodegenState>)((st13ar) => ((Func<CodegenState, CodegenState>)((st13as) => ((Func<CodegenState, CodegenState>)((st13at) => ((Func<CodegenState, CodegenState>)((st13au) => ((Func<CodegenState, CodegenState>)((st13av) => ((Func<CodegenState, CodegenState>)((st13aw) => ((Func<CodegenState, CodegenState>)((st13ax) => ((Func<CodegenState, CodegenState>)((st13ay) => ((Func<CodegenState, CodegenState>)((st13az) => ((Func<CodegenState, CodegenState>)((st13ba) => ((Func<CodegenState, CodegenState>)((st13bb) => ((Func<CodegenState, CodegenState>)((st13bc) => ((Func<CodegenState, CodegenState>)((st13bd) => ((Func<CodegenState, CodegenState>)((st13be) => ((Func<CodegenState, CodegenState>)((st13bf) => ((Func<CodegenState, CodegenState>)((st14) => ((Func<CodegenState, CodegenState>)((st15) => ((Func<long, CodegenState>)((halt_pos) => ((Func<CodegenState, CodegenState>)((st16) => st_append_text(st16, jmp((halt_pos - (st16.text_len + 5L))))))(st_append_text(st15, hlt()))))(st15.text_len)))(st_append_text(st14, cli()))))(emit_serial_wait_and_send(st13bf, 10L))))(emit_print_hex_qword_rdi(st13be))))(st_append_text(st13bd, mov_load(reg_rdi(), reg_rsp(), 64L)))))(emit_serial_wait_and_send(st13bc, 61L))))(emit_serial_wait_and_send(st13bb, 82L))))(emit_serial_wait_and_send(st13ba, 108L))))(emit_serial_wait_and_send(st13az, 108L))))(emit_serial_wait_and_send(st13ay, 97L))))(emit_serial_wait_and_send(st13ax, 67L))))(emit_serial_wait_and_send(st13aw, 32L))))(emit_print_hex_qword_rdi(st13av))))(st_append_text(st13au, mov_load(reg_rdi(), reg_rsp(), 8L)))))(emit_serial_wait_and_send(st13at, 61L))))(emit_serial_wait_and_send(st13as, 73L))))(emit_serial_wait_and_send(st13ar, 83L))))(emit_serial_wait_and_send(st13aq, 82L))))(emit_serial_wait_and_send(st13ap, 32L))))(emit_print_hex_qword_rdi(st13ao))))(st_append_text(st13an, mov_load(reg_rdi(), reg_rsp(), 0L)))))(emit_serial_wait_and_send(st13am, 61L))))(emit_serial_wait_and_send(st13al, 73L))))(emit_serial_wait_and_send(st13ak, 68L))))(emit_serial_wait_and_send(st13aj, 82L))))(emit_serial_wait_and_send(st13ai, 32L))))(emit_print_hex_qword_rdi(st13ah))))(st_append_text(st13ag, mov_rr(reg_rdi(), reg_r10())))))(emit_serial_wait_and_send(st13af, 61L))))(emit_serial_wait_and_send(st13ae, 48L))))(emit_serial_wait_and_send(st13ad, 49L))))(emit_serial_wait_and_send(st13ac, 82L))))(emit_serial_wait_and_send(st13ab, 32L))))(emit_print_hex_qword_rdi(st13aa))))(st_append_text(st13z, mov_rr(reg_rdi(), reg_r14())))))(emit_serial_wait_and_send(st13y, 61L))))(emit_serial_wait_and_send(st13x, 52L))))(emit_serial_wait_and_send(st13w, 49L))))(emit_serial_wait_and_send(st13v, 82L))))(emit_serial_wait_and_send(st13u, 32L))))(emit_print_hex_qword_rdi(st13t))))(st_append_text(st13s, mov_rr(reg_rdi(), reg_r13())))))(emit_serial_wait_and_send(st13r, 61L))))(emit_serial_wait_and_send(st13q, 51L))))(emit_serial_wait_and_send(st13p, 49L))))(emit_serial_wait_and_send(st13o, 82L))))(emit_serial_wait_and_send(st13n, 32L))))(emit_print_hex_qword_rdi(st13m))))(st_append_text(st13l, mov_rr(reg_rdi(), reg_r12())))))(emit_serial_wait_and_send(st13k, 61L))))(emit_serial_wait_and_send(st13j, 50L))))(emit_serial_wait_and_send(st13i, 49L))))(emit_serial_wait_and_send(st13h, 82L))))(emit_serial_wait_and_send(st13g, 32L))))(emit_print_hex_qword_rdi(st13f))))(st_append_text(st13e, mov_rr(reg_rdi(), reg_rbx())))))(emit_serial_wait_and_send(st13d, 61L))))(emit_serial_wait_and_send(st13c, 88L))))(emit_serial_wait_and_send(st13b, 66L))))(emit_serial_wait_and_send(st13a, 82L))))(emit_serial_wait_and_send(st13, 32L))))(emit_print_hex_qword_rdi(st12))))(st_append_text(st11, mov_load(reg_rdi(), reg_rsp(), 40L)))))(emit_serial_wait_and_send(st10, 61L))))(emit_serial_wait_and_send(st9, 80L))))(emit_serial_wait_and_send(st8, 73L))))(emit_serial_wait_and_send(st7, 82L))))(emit_serial_wait_and_send(st6, 32L))))(emit_print_hex_byte_rdi(st5))))(emit_serial_wait_and_send(st4, 61L))))(emit_serial_wait_and_send(st3, 67L))))(emit_serial_wait_and_send(st2, 88L))))(emit_serial_wait_and_send(st1, 69L))))(emit_serial_wait_and_send(st0, 33L))))(st_append_text(st, mov_rr(reg_rdi(), reg_rax())));

    public static CodegenState emit_common_interrupt_handler(CodegenState st) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<long, CodegenState>)((exc_jcc) => ((Func<CodegenState, CodegenState>)((st6a) => ((Func<long, CodegenState>)((not_timer) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => ((Func<CodegenState, CodegenState>)((st12) => ((Func<CodegenState, CodegenState>)((st13) => ((Func<CodegenState, CodegenState>)((st14) => ((Func<CodegenState, CodegenState>)((st15) => ((Func<CodegenState, CodegenState>)((st16) => ((Func<CodegenState, CodegenState>)((st17) => ((Func<CodegenState, CodegenState>)((st18) => ((Func<CodegenState, CodegenState>)((st19) => ((Func<CodegenState, CodegenState>)((st20) => ((Func<long, CodegenState>)((not_keyboard) => ((Func<CodegenState, CodegenState>)((st21) => ((Func<CodegenState, CodegenState>)((st22) => ((Func<CodegenState, CodegenState>)((st23) => ((Func<CodegenState, CodegenState>)((st24) => ((Func<CodegenState, CodegenState>)((st25) => ((Func<CodegenState, CodegenState>)((st26) => ((Func<CodegenState, CodegenState>)((st27) => ((Func<CodegenState, CodegenState>)((st28) => ((Func<long, CodegenState>)((not_serial) => ((Func<CodegenState, CodegenState>)((st29) => ((Func<long, CodegenState>)((serial_drain_loop) => ((Func<CodegenState, CodegenState>)((st30) => ((Func<CodegenState, CodegenState>)((st31) => ((Func<CodegenState, CodegenState>)((st32) => ((Func<CodegenState, CodegenState>)((st33) => ((Func<CodegenState, CodegenState>)((st34) => ((Func<long, CodegenState>)((serial_drain_done) => ((Func<CodegenState, CodegenState>)((st35) => ((Func<CodegenState, CodegenState>)((st36) => ((Func<CodegenState, CodegenState>)((st37) => ((Func<CodegenState, CodegenState>)((st38) => ((Func<CodegenState, CodegenState>)((st39) => ((Func<CodegenState, CodegenState>)((st40) => ((Func<CodegenState, CodegenState>)((st41) => ((Func<CodegenState, CodegenState>)((st42) => ((Func<CodegenState, CodegenState>)((st43) => ((Func<CodegenState, CodegenState>)((st44) => ((Func<CodegenState, CodegenState>)((st45) => ((Func<CodegenState, CodegenState>)((st46) => ((Func<CodegenState, CodegenState>)((st47) => ((Func<CodegenState, CodegenState>)((st48) => ((Func<CodegenState, CodegenState>)((st49) => ((Func<CodegenState, CodegenState>)((st50) => ((Func<CodegenState, CodegenState>)((st51) => ((Func<CodegenState, CodegenState>)((st52) => ((Func<CodegenState, CodegenState>)((st53) => ((Func<CodegenState, CodegenState>)((st54) => ((Func<CodegenState, CodegenState>)((st55) => ((Func<CodegenState, CodegenState>)((st56) => ((Func<long, CodegenState>)((exc_handler_pos) => ((Func<CodegenState, CodegenState>)((st57) => patch_jcc_at(st57, exc_jcc, exc_handler_pos)))(emit_cpu_exception_dump(st56))))(st56.text_len)))(st_append_text(st55, iretq()))))(st_append_text(st54, pop_r(reg_rax())))))(st_append_text(st53, pop_r(reg_rcx())))))(st_append_text(st52, pop_r(reg_rdx())))))(st_append_text(st51, pop_r(reg_rsi())))))(st_append_text(st50, pop_r(reg_rdi())))))(emit_out_byte(st49, 32L, 32L))))(patch_jcc_at(st48, not_serial, st48.text_len))))(patch_jcc_at(st47, serial_drain_done, st47.text_len))))(st_append_text(st46, jmp((serial_drain_loop - (st46.text_len + 5L)))))))(st_append_text(st45, mov_store(reg_rdi(), reg_rcx(), 0L)))))(st_append_text(st44, add_ri(reg_rcx(), 1L)))))(st_append_text(st43, new List<long> { 136L, 6L }))))(st_append_text(st42, add_ri(reg_rsi(), serial_ring_buf_addr())))))(st_append_text(st41, and_ri(reg_rsi(), serial_ring_buf_mask())))))(st_append_text(st40, mov_rr(reg_rsi(), reg_rcx())))))(st_append_text(st39, mov_load(reg_rcx(), reg_rdi(), 0L)))))(st_append_text(st38, li(reg_rdi(), serial_write_pos_addr())))))(st_append_text(st37, new List<long> { 72L, 15L, 182L, 192L }))))(st_append_text(st36, in_al_dx()))))(st_append_text(st35, li(reg_rdx(), 1016L)))))(st_append_text(st34, jcc(cc_e(), 0L)))))(st34.text_len)))(st_append_text(st33, test_rr(reg_rax(), reg_rax())))))(st_append_text(st32, and_rr(reg_rax(), reg_rcx())))))(st_append_text(st31, li(reg_rcx(), 1L)))))(st_append_text(st30, in_al_dx()))))(st_append_text(st29, li(reg_rdx(), 1021L)))))(st29.text_len)))(st_append_text(st28, jcc(cc_ne(), 0L)))))(st28.text_len)))(st_append_text(st27, cmp_ri(reg_rax(), 36L)))))(patch_jcc_at(st26, not_keyboard, st26.text_len))))(st_append_text(st25, mov_store(reg_rdi(), reg_rax(), 0L)))))(st_append_text(st24, new List<long> { 72L, 15L, 182L, 192L }))))(st_append_text(st23, li(reg_rdi(), key_buffer_addr())))))(st_append_text(st22, in_al_dx()))))(st_append_text(st21, li(reg_rdx(), 96L)))))(st_append_text(st20, jcc(cc_ne(), 0L)))))(st20.text_len)))(st_append_text(st19, cmp_ri(reg_rax(), 33L)))))(patch_jcc_at(st18, not_timer, st18.text_len))))(st_append_text(st17, iretq()))))(st_append_text(st16, pop_r(reg_rax())))))(st_append_text(st15, pop_r(reg_rcx())))))(st_append_text(st14, pop_r(reg_rdx())))))(st_append_text(st13, pop_r(reg_rsi())))))(st_append_text(st12, pop_r(reg_rdi())))))(emit_out_byte(st11, 32L, 32L))))(st_append_text(st10, mov_store(reg_rdi(), reg_rsi(), 0L)))))(st_append_text(st9, add_ri(reg_rsi(), 1L)))))(st_append_text(st8, mov_load(reg_rsi(), reg_rdi(), 0L)))))(st_append_text(st7, li(reg_rdi(), tick_count_addr())))))(st_append_text(st6a, jcc(cc_ne(), 0L)))))(st6a.text_len)))(st_append_text(st6, jcc(cc_b(), 0L)))))(st6.text_len)))(st_append_text(st5, cmp_ri(reg_rax(), 32L)))))(st_append_text(st4, new List<long> { 15L, 182L, 192L }))))(st_append_text(st3, push_r(reg_rdi())))))(st_append_text(st2, push_r(reg_rsi())))))(st_append_text(st1, push_r(reg_rdx())))))(st_append_text(st, push_r(reg_rcx())));

    public static bool has_error_code(long vec) => ((vec == 8L) ? true : ((vec == 10L) ? true : ((vec == 11L) ? true : ((vec == 12L) ? true : ((vec == 13L) ? true : ((vec == 14L) ? true : ((vec == 17L) ? true : ((vec == 21L) ? true : ((vec == 29L) ? true : ((vec == 30L) ? true : false))))))))));

    public static CodegenState emit_isr_stub(CodegenState st, long vec, long common_handler_offset) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<long, CodegenState>)((rel) => st_append_text(st3, jmp(rel))))((common_handler_offset - (st3.text_len + 5L)))))(st_append_text(st2, new List<long> { 176L, vec }))))(st_append_text(st1, push_r(reg_rax())))))((has_error_code(vec) ? st_append_text(st, add_ri(reg_rsp(), 8L)) : st_append_text(st, new List<long> { 15L, 31L, 64L, 0L })));

    public static CodegenState emit_isr_stubs_loop(CodegenState st, long vec, long common_handler_offset)
    {
        while (true)
        {
            if ((vec == 256L))
            {
            return st;
            }
            else
            {
            var _tco_0 = emit_isr_stub(st, vec, common_handler_offset);
            var _tco_1 = (vec + 1L);
            var _tco_2 = common_handler_offset;
            st = _tco_0;
            vec = _tco_1;
            common_handler_offset = _tco_2;
            continue;
            }
        }
    }

    public static IsrStubResult emit_isr_stubs(CodegenState st) => ((Func<long, IsrStubResult>)((common_handler_offset) => ((Func<CodegenState, IsrStubResult>)((st1) => ((Func<long, IsrStubResult>)((first_stub_offset) => ((Func<CodegenState, IsrStubResult>)((st2) => new IsrStubResult(state: st2, first_stub_vaddr: (bare_metal_load_addr() + first_stub_offset))))(emit_isr_stubs_loop(st1, 0L, common_handler_offset))))(st1.text_len)))(emit_common_interrupt_handler(st))))(st.text_len);

    public static CodegenState emit_idt_entries(CodegenState st, long first_stub_vaddr) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<long, CodegenState>)((loop_top) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => ((Func<CodegenState, CodegenState>)((st12) => ((Func<CodegenState, CodegenState>)((st13) => ((Func<CodegenState, CodegenState>)((st14) => ((Func<CodegenState, CodegenState>)((st15) => ((Func<CodegenState, CodegenState>)((st16) => ((Func<CodegenState, CodegenState>)((st17) => ((Func<long, CodegenState>)((jcc_pos) => ((Func<CodegenState, CodegenState>)((st18) => patch_jcc_at(st18, jcc_pos, loop_top)))(st_append_text(st17, jcc(cc_ne(), 0L)))))(st17.text_len)))(st_append_text(st16, sub_ri(reg_rcx(), 1L)))))(st_append_text(st15, add_ri(reg_rsi(), 12L)))))(st_append_text(st14, add_ri(reg_rdi(), 16L)))))(st_append_text(st13, mov_store(reg_rdi(), reg_rax(), 8L)))))(st_append_text(st12, li(reg_rax(), 0L)))))(st_append_text(st11, mov_store(reg_rdi(), reg_rax(), 4L)))))(st_append_text(st10, add_ri(reg_rax(), 36352L)))))(st_append_text(st9, shl_ri(reg_rax(), 16L)))))(st_append_text(st8, shr_ri(reg_rax(), 16L)))))(st_append_text(st7, mov_rr(reg_rax(), reg_rsi())))))(st_append_text(st6, mov_store(reg_rdi(), reg_rax(), 0L)))))(st_append_text(st5, add_ri(reg_rax(), 524288L)))))(st_append_text(st4, and_ri(reg_rax(), 65535L)))))(st_append_text(st3, mov_rr(reg_rax(), reg_rsi())))))(st3.text_len)))(st_append_text(st2, li(reg_rsi(), first_stub_vaddr)))))(st_append_text(st1, li(reg_rdi(), idt_base())))))(st_append_text(st, li(reg_rcx(), 256L)));

    public static CodegenState emit_interrupt_setup(CodegenState st) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<long, CodegenState>)((idtr_low) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => ((Func<CodegenState, CodegenState>)((st12) => ((Func<CodegenState, CodegenState>)((st13) => ((Func<CodegenState, CodegenState>)((st14) => ((Func<CodegenState, CodegenState>)((st15) => ((Func<CodegenState, CodegenState>)((st16) => ((Func<CodegenState, CodegenState>)((st17) => ((Func<CodegenState, CodegenState>)((st18) => st_append_text(st18, sti())))(st_append_text(st17, mov_store(reg_rdi(), reg_rax(), 0L)))))(st_append_text(st16, li(reg_rdi(), result_arena_base_addr())))))(st_append_text(st15, mov_store(reg_rdi(), reg_rax(), 0L)))))(st_append_text(st14, li(reg_rdi(), arena_base_addr())))))(st_append_text(st13, mov_store(reg_rdi(), reg_rax(), 0L)))))(st_append_text(st12, li(reg_rdi(), key_buffer_addr())))))(st_append_text(st11, mov_store(reg_rdi(), reg_rax(), 0L)))))(st_append_text(st10, li(reg_rax(), 0L)))))(st_append_text(st9, li(reg_rdi(), tick_count_addr())))))(st_append_text(st8, add_ri(reg_rsp(), 16L)))))(st_append_text(st7, lidt_rdi()))))(st_append_text(st6, mov_rr(reg_rdi(), reg_rsp())))))(st_append_text(st5, mov_store(reg_rsp(), reg_rax(), 8L)))))(st_append_text(st4, li(reg_rax(), 0L)))))(st_append_text(st3, mov_store(reg_rsp(), reg_rax(), 0L)))))(st_append_text(st2, li(reg_rax(), idtr_low)))))((int_mod(((256L * 16L) - 1L), 65536L) + (idt_base() * 65536L)))))(st_append_text(st1, sub_ri(reg_rsp(), 16L)))))(emit_pic_init(st));

    public static CodegenState emit_zero_region(CodegenState st, long addr, long qwords) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => st_append_text(st3, new List<long> { 72L, 243L, 171L })))(st_append_text(st2, li(reg_rax(), 0L)))))(st_append_text(st1, li(reg_rcx(), qwords)))))(st_append_text(st, li(reg_rdi(), addr)));

    public static CodegenState emit_pd_entries(CodegenState st, long i, long n) => ((Func<long, CodegenState>)((count) => ((count <= 0L) ? st : ((Func<long, CodegenState>)((phys_start) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<long, CodegenState>)((loop_start) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<long, CodegenState>)((cur) => st_append_text(st6, jcc(cc_ne(), (loop_start - (cur + 6L))))))(st6.text_len)))(st_append_text(st5, sub_ri(reg_rcx(), 1L)))))(st_append_text(st4, add_ri(reg_rax(), 2097152L)))))(st_append_text(st3, add_ri(reg_rdi(), 8L)))))(st_append_text(st2, mov_store(reg_rdi(), reg_rax(), 0L)))))(st2.text_len)))(st_append_text(st1, li(reg_rcx(), count)))))(st_append_text(st, li(reg_rax(), (phys_start + 131L))))))((i * 2097152L)))))((n - i));

    public static CodegenState emit_build_process_page_tables(CodegenState st, long pml4_addr, long heap_phys) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<long, CodegenState>)((pd_addr) => ((Func<CodegenState, CodegenState>)((st8) => emit_pd_entries(st8, 0L, 512L)))(st_append_text(st7, li(reg_rdi(), pd_addr)))))((pml4_addr + 8192L))))(st_append_text(st6, mov_store(reg_rdi(), reg_rax(), 0L)))))(st_append_text(st5, li(reg_rax(), ((pml4_addr + 8192L) + 3L))))))(st_append_text(st4, li(reg_rdi(), (pml4_addr + 4096L))))))(st_append_text(st3, mov_store(reg_rdi(), reg_rax(), 0L)))))(st_append_text(st2, li(reg_rax(), ((pml4_addr + 4096L) + 3L))))))(st_append_text(st1, li(reg_rdi(), pml4_addr)))))(emit_zero_region(st, pml4_addr, 1536L));

    public static CodegenState emit_create_process(CodegenState st, long proc_index, long state_val, long cr3_val, long heap_base) => ((Func<long, CodegenState>)((entry_addr) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<CodegenState, CodegenState>)((st10) => st_append_text(st10, mov_store(reg_rdi(), reg_rax(), 0L))))(st_append_text(st9, li(reg_rdi(), (entry_addr + 48L))))))(st_append_text(st8, mov_store(reg_rdi(), reg_rax(), 0L)))))(st_append_text(st7, li(reg_rax(), heap_base)))))(st_append_text(st6, li(reg_rdi(), (entry_addr + 40L))))))(st_append_text(st5, mov_store(reg_rdi(), reg_rax(), 0L)))))(st_append_text(st4, li(reg_rax(), cr3_val)))))(st_append_text(st3, li(reg_rdi(), (entry_addr + 16L))))))(st_append_text(st2, mov_store(reg_rdi(), reg_rax(), 0L)))))(st_append_text(st1, li(reg_rax(), state_val)))))(st_append_text(st, li(reg_rdi(), entry_addr)))))((proc_table_base() + (proc_index * proc_entry_size())));

    public static CodegenState emit_process_setup(CodegenState st) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => st_append_text(st8, li(reg_r10(), 4194304L))))(st_append_text(st7, new List<long> { 15L, 34L, 216L }))))(st_append_text(st6, li(reg_rax(), 32768L)))))(emit_create_process(st5, 0L, 1L, 32768L, 4194304L))))(emit_build_process_page_tables(st4, 32768L, 4194304L))))(st_append_text(st3, mov_store(reg_rdi(), reg_rax(), 0L)))))(st_append_text(st2, li(reg_rax(), 0L)))))(st_append_text(st1, li(reg_rdi(), current_proc_addr())))))(emit_zero_region(st, proc_table_base(), ((16L * proc_entry_size()) / 8L)));

    public static CodegenState emit_grant_capability(CodegenState st, long proc_index, long cap_bit) => ((Func<long, CodegenState>)((cap_addr) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<long, CodegenState>)((bit_mask) => ((Func<CodegenState, CodegenState>)((st3) => st_append_text(st3, mov_store(reg_rdi(), reg_rax(), 0L))))(st_append_text(st2, or_ri(reg_rax(), bit_mask)))))(shl_1(cap_bit))))(st_append_text(st1, mov_load(reg_rax(), reg_rdi(), 0L)))))(st_append_text(st, li(reg_rdi(), cap_addr)))))(((proc_table_base() + (proc_index * proc_entry_size())) + proc_cap_offset()));

    public static long shl_1(long n) => ((n == 0L) ? 1L : (2L * shl_1((n - 1L))));

    public static CodegenState emit_write_msr(CodegenState st, long msr_addr, long value) => ((Func<long, CodegenState>)((low32) => ((Func<long, CodegenState>)((high32) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => st_append_text(st3, new List<long> { 15L, 48L })))(st_append_text(st2, li(reg_rdx(), high32)))))(st_append_text(st1, li(reg_rax(), low32)))))(st_append_text(st, li(reg_rcx(), msr_addr)))))((value / 4294967296L))))(int_mod(value, 4294967296L));

    public static CodegenState emit_check_capability(CodegenState st) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => ((Func<CodegenState, CodegenState>)((st12) => st_append_text(st12, pop_r(reg_rcx()))))(st_append_text(st11, pop_r(reg_r11())))))(st_append_text(st10, new List<long> { 72L, 15L, 163L, 209L }))))(st_append_text(st9, mov_load(reg_rcx(), reg_rcx(), 0L)))))(st_append_text(st8, add_ri(reg_rcx(), proc_cap_offset())))))(st_append_text(st7, add_ri(reg_rcx(), proc_table_base())))))(st_append_text(st6, new List<long> { 72L, 15L, 175L, 203L }))))(st_append_text(st5, li(reg_rcx(), proc_entry_size())))))(st_append_text(st4, mov_load(reg_r11(), reg_r11(), 0L)))))(st_append_text(st3, li(reg_r11(), current_proc_addr())))))(st_append_text(st2, push_r(reg_r11())))))(st_append_text(st1, li(reg_rcx(), proc_table_base())))))(st_append_text(st, push_r(reg_rcx())));

    public static SyscallResult emit_syscall_handler(CodegenState st) => ((Func<long, SyscallResult>)((handler_offset) => ((Func<CodegenState, SyscallResult>)((st1) => ((Func<CodegenState, SyscallResult>)((st2) => ((Func<CodegenState, SyscallResult>)((st3) => ((Func<long, SyscallResult>)((not_write) => ((Func<CodegenState, SyscallResult>)((st4) => ((Func<CodegenState, SyscallResult>)((st5) => ((Func<CodegenState, SyscallResult>)((st6) => ((Func<long, SyscallResult>)((no_write_cap) => ((Func<CodegenState, SyscallResult>)((st7) => ((Func<CodegenState, SyscallResult>)((st8) => ((Func<CodegenState, SyscallResult>)((st9) => ((Func<CodegenState, SyscallResult>)((st10) => ((Func<CodegenState, SyscallResult>)((st11) => ((Func<CodegenState, SyscallResult>)((st12) => ((Func<long, SyscallResult>)((write_done) => ((Func<CodegenState, SyscallResult>)((st13) => ((Func<CodegenState, SyscallResult>)((st14) => ((Func<CodegenState, SyscallResult>)((st15) => ((Func<long, SyscallResult>)((not_read_key) => ((Func<CodegenState, SyscallResult>)((st16) => ((Func<CodegenState, SyscallResult>)((st17) => ((Func<CodegenState, SyscallResult>)((st18) => ((Func<long, SyscallResult>)((read_done) => ((Func<CodegenState, SyscallResult>)((st19) => ((Func<CodegenState, SyscallResult>)((st20) => ((Func<CodegenState, SyscallResult>)((st21) => ((Func<long, SyscallResult>)((not_get_ticks) => ((Func<CodegenState, SyscallResult>)((st22) => ((Func<CodegenState, SyscallResult>)((st23) => ((Func<CodegenState, SyscallResult>)((st24) => ((Func<long, SyscallResult>)((ticks_done) => ((Func<CodegenState, SyscallResult>)((st25) => ((Func<CodegenState, SyscallResult>)((st26) => ((Func<CodegenState, SyscallResult>)((st27) => ((Func<long, SyscallResult>)((common_exit) => ((Func<CodegenState, SyscallResult>)((st28) => ((Func<CodegenState, SyscallResult>)((st29) => ((Func<CodegenState, SyscallResult>)((st30) => ((Func<CodegenState, SyscallResult>)((st31) => ((Func<CodegenState, SyscallResult>)((st32) => ((Func<CodegenState, SyscallResult>)((st33) => ((Func<CodegenState, SyscallResult>)((st34) => ((Func<CodegenState, SyscallResult>)((st35) => new SyscallResult(state: st35, handler_offset: handler_offset)))(st_append_text(st34, new List<long> { 255L, 225L }))))(st_append_text(st33, new List<long> { 157L }))))(st_append_text(st32, push_r(reg_r11())))))(st_append_text(st31, pop_r(reg_rcx())))))(st_append_text(st30, pop_r(reg_r11())))))(patch_jmp_at(st29, ticks_done, common_exit))))(patch_jmp_at(st28, read_done, common_exit))))(patch_jmp_at(st27, write_done, common_exit))))(st27.text_len)))(st_append_text(st26, li(reg_rax(), (-1L))))))(patch_jcc_at(st25, not_get_ticks, st25.text_len))))(st_append_text(st24, jmp(0L)))))(st24.text_len)))(st_append_text(st23, mov_load(reg_rax(), reg_rdi(), 0L)))))(st_append_text(st22, li(reg_rdi(), tick_count_addr())))))(st_append_text(st21, jcc(cc_ne(), 0L)))))(st21.text_len)))(st_append_text(st20, cmp_ri(reg_rax(), 3L)))))(patch_jcc_at(st19, not_read_key, st19.text_len))))(st_append_text(st18, jmp(0L)))))(st18.text_len)))(st_append_text(st17, mov_load(reg_rax(), reg_rdi(), 0L)))))(st_append_text(st16, li(reg_rdi(), key_buffer_addr())))))(st_append_text(st15, jcc(cc_ne(), 0L)))))(st15.text_len)))(st_append_text(st14, cmp_ri(reg_rax(), 2L)))))(patch_jcc_at(st13, not_write, st13.text_len))))(st_append_text(st12, jmp(0L)))))(st12.text_len)))(st_append_text(st11, li(reg_rax(), 0L)))))(patch_jcc_at(st10, no_write_cap, st10.text_len))))(st_append_text(st9, out_dx_al()))))(st_append_text(st8, li(reg_rdx(), 1016L)))))(st_append_text(st7, mov_rr(reg_rax(), reg_rdx())))))(st_append_text(st6, jcc(cc_e(), 0L)))))(st6.text_len)))(emit_check_capability(st5))))(st_append_text(st4, mov_rr(reg_rdx(), reg_rdi())))))(st_append_text(st3, jcc(cc_ne(), 0L)))))(st3.text_len)))(st_append_text(st2, cmp_ri(reg_rax(), 1L)))))(st_append_text(st1, push_r(reg_r11())))))(st_append_text(st, push_r(reg_rcx())))))(st.text_len);

    public static CodegenState emit_serial_string_loop(CodegenState st, List<long> chars, long i)
    {
        while (true)
        {
            if ((i == ((long)chars.Count)))
            {
            return st;
            }
            else
            {
            var st1 = emit_serial_wait_and_send(st, chars[(int)i]);
            var _tco_0 = st1;
            var _tco_1 = chars;
            var _tco_2 = (i + 1L);
            st = _tco_0;
            chars = _tco_1;
            i = _tco_2;
            continue;
            }
        }
    }

    public static CodegenState emit_serial_string(CodegenState st, List<long> chars) => emit_serial_string_loop(st, chars, 0L);

    public static CodegenState emit_update_heap_hwm(CodegenState st) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<long, CodegenState>)((skip_update) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => st_append_text(st9, pop_r(reg_r11()))))(st_append_text(st8, pop_r(reg_rax())))))(patch_jcc_at(st7, skip_update, st7.text_len))))(st_append_text(st6, mov_store(reg_r11(), reg_r10(), 0L)))))(st_append_text(st5, jcc(cc_le(), 0L)))))(st5.text_len)))(st_append_text(st4, cmp_rr(reg_r10(), reg_rax())))))(st_append_text(st3, mov_load(reg_rax(), reg_r11(), 0L)))))(st_append_text(st2, push_r(reg_rax())))))(st_append_text(st1, li(reg_r11(), heap_hwm_addr())))))(st_append_text(st, push_r(reg_r11())));

    public static long int_mod(long n, long d) => ((Func<long, long>)((abs_d) => ((Func<long, long>)((r) => ((r < 0L) ? (r + abs_d) : r)))((n - ((n / d) * d)))))(((d < 0L) ? (0L - d) : d));

    public static long floor_div(long n, long d) => ((n >= 0L) ? (n / d) : (((n - d) + 1L) / d));

    public static long to_byte(long n) => int_mod(n, 256L);

    public static long rex(bool w, bool r, bool x, bool b) => ((Func<long, long>)((wv) => ((Func<long, long>)((rv) => ((Func<long, long>)((xv) => ((Func<long, long>)((bv) => ((((64L + wv) + rv) + xv) + bv)))((b ? 1L : 0L))))((x ? 2L : 0L))))((r ? 4L : 0L))))((w ? 8L : 0L));

    public static long rex_w(long reg, long rm) => rex(true, (reg >= 8L), false, (rm >= 8L));

    public static long modrm(long m, long reg, long rm) => (((m * 64L) + (int_mod(reg, 8L) * 8L)) + int_mod(rm, 8L));

    public static long sib(long scale, long index, long base_reg) => (((scale * 64L) + (int_mod(index, 8L) * 8L)) + int_mod(base_reg, 8L));

    public static List<long> write_bytes(long v, long n) => ((n == 0L) ? new List<long>() : Enumerable.Concat(new List<long> { to_byte(v) }, write_bytes(floor_div(v, 256L), (n - 1L))).ToList());

    public static List<long> write_i8(long v) => new List<long> { to_byte(v) };

    public static List<long> write_i32(long v) => write_bytes(v, 4L);

    public static List<long> write_i64(long v) => write_bytes(v, 8L);

    public static List<long> emit_mem_operand(long reg, long rm, long offset) => ((Func<long, List<long>>)((rm_low) => ((Func<bool, List<long>>)((needs_sib) => ((Func<bool, List<long>>)((rbp_base) => ((Func<List<long>, List<long>>)((sib_byte) => (((offset == 0L) && (rbp_base == false)) ? Enumerable.Concat(new List<long> { modrm(0L, reg, rm) }, sib_byte).ToList() : (((offset >= (-128L)) && (offset <= 127L)) ? Enumerable.Concat(new List<long> { modrm(1L, reg, rm) }, Enumerable.Concat(sib_byte, write_i8(offset)).ToList()).ToList() : Enumerable.Concat(new List<long> { modrm(2L, reg, rm) }, Enumerable.Concat(sib_byte, write_i32(offset)).ToList()).ToList()))))((needs_sib ? new List<long> { sib(0L, 4L, rm_low) } : new List<long>()))))((rm_low == 5L))))((rm_low == 4L))))(int_mod(rm, 8L));

    public static long reg_rax() => 0L;

    public static long reg_rcx() => 1L;

    public static long reg_rdx() => 2L;

    public static long reg_rbx() => 3L;

    public static long reg_rsp() => 4L;

    public static long reg_rbp() => 5L;

    public static long reg_rsi() => 6L;

    public static long reg_rdi() => 7L;

    public static long reg_r8() => 8L;

    public static long reg_r9() => 9L;

    public static long reg_r10() => 10L;

    public static long reg_r11() => 11L;

    public static long reg_r12() => 12L;

    public static long reg_r13() => 13L;

    public static long reg_r14() => 14L;

    public static long reg_r15() => 15L;

    public static List<long> arg_regs() => new List<long> { 7L, 6L, 2L, 1L, 8L, 9L };

    public static List<long> callee_saved_regs() => new List<long> { 3L, 12L, 13L, 14L, 15L };

    public static long cc_b() => 2L;

    public static long cc_ae() => 3L;

    public static long cc_e() => 4L;

    public static long cc_ne() => 5L;

    public static long cc_be() => 6L;

    public static long cc_a() => 7L;

    public static long cc_l() => 12L;

    public static long cc_ge() => 13L;

    public static long cc_le() => 14L;

    public static long cc_g() => 15L;

    public static List<long> mov_rr(long rd, long rs) => new List<long> { rex_w(rs, rd), 137L, modrm(3L, rs, rd) };

    public static List<long> mov_ri64(long rd, long imm) => Enumerable.Concat(new List<long> { rex(true, false, false, (rd >= 8L)), (184L + int_mod(rd, 8L)) }, write_i64(imm)).ToList();

    public static List<long> mov_ri32(long rd, long imm) => Enumerable.Concat(new List<long> { rex_w(0L, rd), 199L, modrm(3L, 0L, rd) }, write_i32(imm)).ToList();

    public static List<long> mov_load(long rd, long rs, long offset) => Enumerable.Concat(new List<long> { rex_w(rd, rs), 139L }, emit_mem_operand(rd, rs, offset)).ToList();

    public static List<long> mov_store(long rd, long rs, long offset) => Enumerable.Concat(new List<long> { rex_w(rs, rd), 137L }, emit_mem_operand(rs, rd, offset)).ToList();

    public static List<long> mov_load_rip_rel(long rd, long disp32) => Enumerable.Concat(new List<long> { rex(true, (rd >= 8L), false, false), 139L, modrm(0L, rd, 5L) }, write_i32(disp32)).ToList();

    public static List<long> mov_store_rip_rel(long rs, long disp32) => Enumerable.Concat(new List<long> { rex(true, (rs >= 8L), false, false), 137L, modrm(0L, rs, 5L) }, write_i32(disp32)).ToList();

    public static List<long> movzx_byte(long rd, long rs, long offset) => Enumerable.Concat(new List<long> { rex_w(rd, rs), 15L, 182L }, emit_mem_operand(rd, rs, offset)).ToList();

    public static List<long> mov_store_byte(long rd, long rs, long offset) => ((Func<long, List<long>>)((rex_byte) => ((Func<List<long>, List<long>>)((pfx) => Enumerable.Concat(pfx, Enumerable.Concat(new List<long> { 136L }, emit_mem_operand(rs, rd, offset)).ToList()).ToList()))((((rex_byte != 64L) || (rs >= 4L)) ? new List<long> { rex_byte } : new List<long>()))))(rex(false, (rs >= 8L), false, (rd >= 8L)));

    public static List<long> alu_ri(long ext, long rd, long imm) => (((imm >= (-128L)) && (imm <= 127L)) ? Enumerable.Concat(new List<long> { rex_w(0L, rd), 131L, modrm(3L, ext, rd) }, write_i8(imm)).ToList() : Enumerable.Concat(new List<long> { rex_w(0L, rd), 129L, modrm(3L, ext, rd) }, write_i32(imm)).ToList());

    public static List<long> add_rr(long rd, long rs) => new List<long> { rex_w(rs, rd), 1L, modrm(3L, rs, rd) };

    public static List<long> add_ri(long rd, long imm) => alu_ri(0L, rd, imm);

    public static List<long> sub_rr(long rd, long rs) => new List<long> { rex_w(rs, rd), 41L, modrm(3L, rs, rd) };

    public static List<long> sub_ri(long rd, long imm) => alu_ri(5L, rd, imm);

    public static List<long> imul_rr(long rd, long rs) => new List<long> { rex_w(rd, rs), 15L, 175L, modrm(3L, rd, rs) };

    public static List<long> neg_r(long rd) => new List<long> { rex_w(0L, rd), 247L, modrm(3L, 3L, rd) };

    public static List<long> cqo() => new List<long> { rex(true, false, false, false), 153L };

    public static List<long> idiv_r(long rs) => new List<long> { rex_w(0L, rs), 247L, modrm(3L, 7L, rs) };

    public static List<long> and_rr(long rd, long rs) => new List<long> { rex_w(rs, rd), 33L, modrm(3L, rs, rd) };

    public static List<long> and_ri(long rd, long imm) => alu_ri(4L, rd, imm);

    public static List<long> or_rr(long rd, long rs) => new List<long> { rex_w(rs, rd), 9L, modrm(3L, rs, rd) };

    public static List<long> or_ri(long rd, long imm) => alu_ri(1L, rd, imm);

    public static List<long> shl_ri(long rd, long imm) => new List<long> { rex_w(0L, rd), 193L, modrm(3L, 4L, rd), imm };

    public static List<long> shr_ri(long rd, long imm) => new List<long> { rex_w(0L, rd), 193L, modrm(3L, 5L, rd), imm };

    public static List<long> shl_cl(long rd) => new List<long> { rex_w(0L, rd), 211L, modrm(3L, 4L, rd) };

    public static List<long> shr_cl(long rd) => new List<long> { rex_w(0L, rd), 211L, modrm(3L, 5L, rd) };

    public static List<long> not_r(long rd) => new List<long> { rex_w(0L, rd), 247L, modrm(3L, 2L, rd) };

    public static List<long> sar_ri(long rd, long imm) => new List<long> { rex_w(0L, rd), 193L, modrm(3L, 7L, rd), imm };

    public static List<long> cmp_rr(long rd, long rs) => new List<long> { rex_w(rs, rd), 57L, modrm(3L, rs, rd) };

    public static List<long> cmp_ri(long rd, long imm) => alu_ri(7L, rd, imm);

    public static List<long> test_rr(long rd, long rs) => new List<long> { rex_w(rs, rd), 133L, modrm(3L, rs, rd) };

    public static List<long> setcc(long cc, long rd) => ((Func<List<long>, List<long>>)((pfx) => Enumerable.Concat(pfx, new List<long> { 15L, (144L + cc), modrm(3L, 0L, rd) }).ToList()))(((rd >= 4L) ? new List<long> { rex(false, false, false, (rd >= 8L)) } : new List<long>()));

    public static List<long> movzx_byte_self(long rd) => new List<long> { rex_w(rd, rd), 15L, 182L, modrm(3L, rd, rd) };

    public static List<long> jcc(long cc, long rel32) => Enumerable.Concat(new List<long> { 15L, (128L + cc) }, write_i32(rel32)).ToList();

    public static List<long> jmp(long rel32) => Enumerable.Concat(new List<long> { 233L }, write_i32(rel32)).ToList();

    public static List<long> x86_call(long rel32) => Enumerable.Concat(new List<long> { 232L }, write_i32(rel32)).ToList();

    public static List<long> x86_ret() => new List<long> { 195L };

    public static List<long> x86_nop() => new List<long> { 144L };

    public static List<long> push_r(long rd) => ((Func<List<long>, List<long>>)((pfx) => Enumerable.Concat(pfx, new List<long> { (80L + int_mod(rd, 8L)) }).ToList()))(((rd >= 8L) ? new List<long> { rex(false, false, false, true) } : new List<long>()));

    public static List<long> pop_r(long rd) => ((Func<List<long>, List<long>>)((pfx) => Enumerable.Concat(pfx, new List<long> { (88L + int_mod(rd, 8L)) }).ToList()))(((rd >= 8L) ? new List<long> { rex(false, false, false, true) } : new List<long>()));

    public static List<long> lea(long rd, long rs, long offset) => Enumerable.Concat(new List<long> { rex_w(rd, rs), 141L }, emit_mem_operand(rd, rs, offset)).ToList();

    public static List<long> li(long rd, long value) => ((value == 0L) ? xor_rr(rd, rd) : (((value >= (0L - 2147483648L)) && (value <= 2147483647L)) ? mov_ri32(rd, value) : mov_ri64(rd, value)));

    public static List<long> xor_rr(long rd, long rs) => ((Func<long, List<long>>)((rex_byte) => ((Func<List<long>, List<long>>)((pfx) => Enumerable.Concat(pfx, new List<long> { 49L, modrm(3L, rs, rd) }).ToList()))(((rex_byte != 64L) ? new List<long> { rex_byte } : new List<long>()))))(rex(false, (rs >= 8L), false, (rd >= 8L)));

    public static List<long> x86_syscall() => new List<long> { 15L, 5L };

    public static List<long> out_dx_al() => new List<long> { 238L };

    public static List<long> in_al_dx() => new List<long> { 236L };

    public static List<long> hlt() => new List<long> { 244L };

    public static List<long> x86_pause() => new List<long> { 243L, 144L };

    public static List<long> cli() => new List<long> { 250L };

    public static List<long> sti() => new List<long> { 251L };

    public static List<long> iretq() => new List<long> { 72L, 207L };

    public static List<long> lidt_rdi() => new List<long> { 15L, 1L, 31L };

    public static List<long> swapgs() => new List<long> { 15L, 1L, 248L };

    public static List<long> movq_to_xmm(long xmm, long gpr) => new List<long> { 102L, rex_w(xmm, gpr), 15L, 110L, modrm(3L, xmm, gpr) };

    public static List<long> movq_from_xmm(long gpr, long xmm) => new List<long> { 102L, rex_w(xmm, gpr), 15L, 126L, modrm(3L, xmm, gpr) };

    public static List<long> addsd_xmm(long dst, long src) => new List<long> { 242L, 15L, 88L, modrm(3L, dst, src) };

    public static List<long> subsd_xmm(long dst, long src) => new List<long> { 242L, 15L, 92L, modrm(3L, dst, src) };

    public static List<long> mulsd_xmm(long dst, long src) => new List<long> { 242L, 15L, 89L, modrm(3L, dst, src) };

    public static List<long> divsd_xmm(long dst, long src) => new List<long> { 242L, 15L, 94L, modrm(3L, dst, src) };

    public static List<long> ucomisd_xmm(long a, long b) => new List<long> { 102L, 15L, 46L, modrm(3L, a, b) };

    public static StrEqHeadResult emit_str_eq_head(CodegenState st) => ((Func<CodegenState, StrEqHeadResult>)((st0) => ((Func<CodegenState, StrEqHeadResult>)((st1) => ((Func<long, StrEqHeadResult>)((not_same_pos) => ((Func<CodegenState, StrEqHeadResult>)((st2) => ((Func<CodegenState, StrEqHeadResult>)((st3) => ((Func<CodegenState, StrEqHeadResult>)((st4) => ((Func<CodegenState, StrEqHeadResult>)((st5) => ((Func<CodegenState, StrEqHeadResult>)((st6) => ((Func<CodegenState, StrEqHeadResult>)((st7) => ((Func<CodegenState, StrEqHeadResult>)((st8) => ((Func<long, StrEqHeadResult>)((len_ne_pos) => ((Func<CodegenState, StrEqHeadResult>)((st9) => new StrEqHeadResult(cg: st9, len_ne_pos: len_ne_pos)))(st_append_text(st8, jcc(cc_ne(), 0L)))))(st8.text_len)))(st_append_text(st7, cmp_rr(reg_rcx(), reg_rdx())))))(st_append_text(st6, mov_load(reg_rdx(), reg_rsi(), 0L)))))(st_append_text(st5, mov_load(reg_rcx(), reg_rdi(), 0L)))))(patch_jcc_at(st4, not_same_pos, st4.text_len))))(st_append_text(st3, x86_ret()))))(st_append_text(st2, li(reg_rax(), 1L)))))(st_append_text(st1, jcc(cc_ne(), 0L)))))(st1.text_len)))(st_append_text(st0, cmp_rr(reg_rdi(), reg_rsi())))))(record_func_offset(st, "\u0055\u0055\u0013\u000E\u0015\u0055\u000D\u0025"));

    public static StrEqLoopResult emit_str_eq_byte_loop(CodegenState st) => ((Func<CodegenState, StrEqLoopResult>)((st0) => ((Func<long, StrEqLoopResult>)((loop_pos) => ((Func<CodegenState, StrEqLoopResult>)((st1) => ((Func<long, StrEqLoopResult>)((loop_done_pos) => ((Func<CodegenState, StrEqLoopResult>)((st2) => ((Func<CodegenState, StrEqLoopResult>)((st3) => ((Func<CodegenState, StrEqLoopResult>)((st4) => ((Func<CodegenState, StrEqLoopResult>)((st5) => ((Func<CodegenState, StrEqLoopResult>)((st6) => ((Func<CodegenState, StrEqLoopResult>)((st7) => ((Func<CodegenState, StrEqLoopResult>)((st8) => ((Func<CodegenState, StrEqLoopResult>)((st9) => ((Func<long, StrEqLoopResult>)((byte_ne_pos) => ((Func<CodegenState, StrEqLoopResult>)((st10) => ((Func<CodegenState, StrEqLoopResult>)((st11) => ((Func<CodegenState, StrEqLoopResult>)((st12) => new StrEqLoopResult(cg: st12, loop_done_pos: loop_done_pos, byte_ne_pos: byte_ne_pos)))(st_append_text(st11, jmp((loop_pos - (st11.text_len + 5L)))))))(st_append_text(st10, add_ri(reg_r11(), 1L)))))(st_append_text(st9, jcc(cc_ne(), 0L)))))(st9.text_len)))(st_append_text(st8, cmp_rr(reg_rax(), reg_rdx())))))(st_append_text(st7, movzx_byte(reg_rdx(), reg_rdx(), 8L)))))(st_append_text(st6, add_rr(reg_rdx(), reg_r11())))))(st_append_text(st5, mov_rr(reg_rdx(), reg_rsi())))))(st_append_text(st4, movzx_byte(reg_rax(), reg_rax(), 8L)))))(st_append_text(st3, add_rr(reg_rax(), reg_r11())))))(st_append_text(st2, mov_rr(reg_rax(), reg_rdi())))))(st_append_text(st1, jcc(cc_ge(), 0L)))))(st1.text_len)))(st_append_text(st0, cmp_rr(reg_r11(), reg_rcx())))))(st0.text_len)))(st_append_text(st, li(reg_r11(), 0L)));

    public static CodegenState emit_str_eq(CodegenState st) => ((Func<StrEqHeadResult, CodegenState>)((head) => ((Func<StrEqLoopResult, CodegenState>)((lp) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => st_append_text(st6, x86_ret())))(st_append_text(st5, li(reg_rax(), 0L)))))(patch_jcc_at(st4, lp.byte_ne_pos, st4.text_len))))(patch_jcc_at(st3, head.len_ne_pos, st3.text_len))))(st_append_text(st2, x86_ret()))))(st_append_text(st1, li(reg_rax(), 1L)))))(patch_jcc_at(lp.cg, lp.loop_done_pos, lp.cg.text_len))))(emit_str_eq_byte_loop(head.cg))))(emit_str_eq_head(st));

    public static CodegenState emit_itoa_setup(CodegenState st) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<long, CodegenState>)((not_neg_pos) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => patch_jcc_at(st9, not_neg_pos, st9.text_len)))(st_append_text(st8, li(reg_r12(), 1L)))))(st_append_text(st7, neg_r(reg_rbx())))))(st_append_text(st6, jcc(cc_ge(), 0L)))))(st6.text_len)))(st_append_text(st5, cmp_ri(reg_rbx(), 0L)))))(st_append_text(st4, li(reg_r12(), 0L)))))(st_append_text(st3, mov_rr(reg_rbx(), reg_rdi())))))(st_append_text(st2, sub_ri(reg_rsp(), 32L)))))(st_append_text(st1, push_r(reg_r12())))))(st_append_text(st0, push_r(reg_rbx())))))(record_func_offset(st, "\u0055\u0055\u0011\u000E\u0010\u000F"));

    public static ItoaZeroResult emit_itoa_zero_guard(CodegenState st) => ((Func<CodegenState, ItoaZeroResult>)((st0) => ((Func<CodegenState, ItoaZeroResult>)((st1) => ((Func<CodegenState, ItoaZeroResult>)((st2) => ((Func<long, ItoaZeroResult>)((not_zero_pos) => ((Func<CodegenState, ItoaZeroResult>)((st3) => ((Func<CodegenState, ItoaZeroResult>)((st4) => ((Func<CodegenState, ItoaZeroResult>)((st5) => ((Func<CodegenState, ItoaZeroResult>)((st6) => ((Func<long, ItoaZeroResult>)((skip_digits_pos) => ((Func<CodegenState, ItoaZeroResult>)((st7) => ((Func<CodegenState, ItoaZeroResult>)((st8) => new ItoaZeroResult(cg: st8, skip_digits_pos: skip_digits_pos)))(patch_jcc_at(st7, not_zero_pos, st7.text_len))))(st_append_text(st6, jmp(0L)))))(st6.text_len)))(st_append_text(st5, li(reg_rcx(), 1L)))))(st_append_text(st4, mov_store_byte(reg_rsp(), reg_rsi(), 0L)))))(st_append_text(st3, li(reg_rsi(), 3L)))))(st_append_text(st2, jcc(cc_ne(), 0L)))))(st2.text_len)))(st_append_text(st1, test_rr(reg_rbx(), reg_rbx())))))(st_append_text(st0, li(reg_r11(), 10L)))))(st_append_text(st, li(reg_rcx(), 0L)));

    public static CodegenState emit_itoa_digit_loop(CodegenState st) => ((Func<long, CodegenState>)((digit_loop) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<long, CodegenState>)((digit_done_pos) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => patch_jcc_at(st11, digit_done_pos, st11.text_len)))(st_append_text(st10, jmp((digit_loop - (st10.text_len + 5L)))))))(st_append_text(st9, add_ri(reg_rcx(), 1L)))))(st_append_text(st8, mov_store_byte(reg_rsi(), reg_rdx(), 0L)))))(st_append_text(st7, add_rr(reg_rsi(), reg_rcx())))))(st_append_text(st6, mov_rr(reg_rsi(), reg_rsp())))))(st_append_text(st5, add_ri(reg_rdx(), 3L)))))(st_append_text(st4, mov_rr(reg_rbx(), reg_rax())))))(st_append_text(st3, idiv_r(reg_r11())))))(st_append_text(st2, cqo()))))(st_append_text(st1, mov_rr(reg_rax(), reg_rbx())))))(st_append_text(st0, jcc(cc_e(), 0L)))))(st0.text_len)))(st_append_text(st, test_rr(reg_rbx(), reg_rbx())))))(st.text_len);

    public static CodegenState emit_itoa_heap_alloc(CodegenState st) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<long, CodegenState>)((no_minus_pos) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => ((Func<CodegenState, CodegenState>)((st12) => ((Func<CodegenState, CodegenState>)((st13) => patch_jcc_at(st13, no_minus_pos, st13.text_len)))(st_append_text(st12, li(reg_r11(), 1L)))))(st_append_text(st11, mov_store_byte(reg_rax(), reg_rsi(), 8L)))))(st_append_text(st10, li(reg_rsi(), 73L)))))(st_append_text(st9, jcc(cc_e(), 0L)))))(st9.text_len)))(st_append_text(st8, test_rr(reg_r12(), reg_r12())))))(st_append_text(st7, li(reg_r11(), 0L)))))(st_append_text(st6, mov_store(reg_rax(), reg_rdx(), 0L)))))(st_append_text(st5, add_rr(reg_r10(), reg_rsi())))))(st_append_text(st4, and_ri(reg_rsi(), (0L - 8L))))))(st_append_text(st3, add_ri(reg_rsi(), 15L)))))(st_append_text(st2, mov_rr(reg_rsi(), reg_rdx())))))(st_append_text(st1, mov_rr(reg_rax(), reg_r10())))))(st_append_text(st0, add_rr(reg_rdx(), reg_r12())))))(st_append_text(st, mov_rr(reg_rdx(), reg_rcx())));

    public static CodegenState emit_itoa_copy_and_epilogue(CodegenState st) => ((Func<long, CodegenState>)((copy_loop) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<long, CodegenState>)((copy_done_pos) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => ((Func<CodegenState, CodegenState>)((st12) => ((Func<CodegenState, CodegenState>)((st13) => ((Func<CodegenState, CodegenState>)((st14) => st_append_text(st14, x86_ret())))(st_append_text(st13, pop_r(reg_rbx())))))(st_append_text(st12, pop_r(reg_r12())))))(st_append_text(st11, add_ri(reg_rsp(), 32L)))))(patch_jcc_at(st10, copy_done_pos, st10.text_len))))(st_append_text(st9, jmp((copy_loop - (st9.text_len + 5L)))))))(st_append_text(st8, add_ri(reg_r11(), 1L)))))(st_append_text(st7, mov_store_byte(reg_rdx(), reg_rsi(), 8L)))))(st_append_text(st6, add_rr(reg_rdx(), reg_r11())))))(st_append_text(st5, mov_rr(reg_rdx(), reg_rax())))))(st_append_text(st4, movzx_byte(reg_rsi(), reg_rsi(), 0L)))))(st_append_text(st3, add_rr(reg_rsi(), reg_rcx())))))(st_append_text(st2, mov_rr(reg_rsi(), reg_rsp())))))(st_append_text(st1, sub_ri(reg_rcx(), 1L)))))(st_append_text(st0, jcc(cc_e(), 0L)))))(st0.text_len)))(st_append_text(st, test_rr(reg_rcx(), reg_rcx())))))(st.text_len);

    public static CodegenState emit_itoa(CodegenState st) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<ItoaZeroResult, CodegenState>)((zero) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => emit_itoa_copy_and_epilogue(st3)))(emit_itoa_heap_alloc(st2))))(patch_jmp_at(st1, zero.skip_digits_pos, st1.text_len))))(emit_itoa_digit_loop(zero.cg))))(emit_itoa_zero_guard(st0))))(emit_itoa_setup(st));

    public static StrConcatCheckResult emit_str_concat_prologue(CodegenState st) => ((Func<CodegenState, StrConcatCheckResult>)((st0) => ((Func<CodegenState, StrConcatCheckResult>)((st1) => ((Func<CodegenState, StrConcatCheckResult>)((st2) => ((Func<CodegenState, StrConcatCheckResult>)((st3) => ((Func<CodegenState, StrConcatCheckResult>)((st4) => ((Func<CodegenState, StrConcatCheckResult>)((st5) => ((Func<CodegenState, StrConcatCheckResult>)((st6) => ((Func<CodegenState, StrConcatCheckResult>)((st7) => ((Func<CodegenState, StrConcatCheckResult>)((st8) => ((Func<CodegenState, StrConcatCheckResult>)((st9) => ((Func<CodegenState, StrConcatCheckResult>)((st10) => ((Func<CodegenState, StrConcatCheckResult>)((st11) => ((Func<CodegenState, StrConcatCheckResult>)((st12) => ((Func<CodegenState, StrConcatCheckResult>)((st13) => ((Func<long, StrConcatCheckResult>)((slow_path_pos) => ((Func<CodegenState, StrConcatCheckResult>)((st14) => new StrConcatCheckResult(cg: st14, slow_path_pos: slow_path_pos)))(st_append_text(st13, jcc(cc_ne(), 0L)))))(st13.text_len)))(st_append_text(st12, cmp_rr(reg_r13(), reg_r10())))))(st_append_text(st11, add_rr(reg_r13(), reg_r11())))))(st_append_text(st10, mov_rr(reg_r13(), reg_rbx())))))(st_append_text(st9, and_ri(reg_r11(), (0L - 8L))))))(st_append_text(st8, add_ri(reg_r11(), 15L)))))(st_append_text(st7, mov_rr(reg_r11(), reg_rcx())))))(st_append_text(st6, mov_load(reg_rdx(), reg_r12(), 0L)))))(st_append_text(st5, mov_load(reg_rcx(), reg_rbx(), 0L)))))(st_append_text(st4, mov_rr(reg_r12(), reg_rsi())))))(st_append_text(st3, mov_rr(reg_rbx(), reg_rdi())))))(st_append_text(st2, push_r(reg_r13())))))(st_append_text(st1, push_r(reg_r12())))))(st_append_text(st0, push_r(reg_rbx())))))(record_func_offset(st, "\u0055\u0055\u0013\u000E\u0015\u0055\u0018\u0010\u0012\u0018\u000F\u000E"));

    public static CodegenState emit_str_concat_fast_copy(CodegenState st) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<long, CodegenState>)((fast_loop) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<long, CodegenState>)((fast_exit_pos) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => ((Func<CodegenState, CodegenState>)((st12) => ((Func<CodegenState, CodegenState>)((st13) => ((Func<CodegenState, CodegenState>)((st14) => patch_jcc_at(st14, fast_exit_pos, st14.text_len)))(st_append_text(st13, jmp((fast_loop - (st13.text_len + 5L)))))))(st_append_text(st12, add_ri(reg_r11(), 1L)))))(st_append_text(st11, mov_store_byte(reg_rdi(), reg_rsi(), 8L)))))(st_append_text(st10, add_rr(reg_rdi(), reg_r11())))))(st_append_text(st9, add_rr(reg_rdi(), reg_rcx())))))(st_append_text(st8, mov_rr(reg_rdi(), reg_rbx())))))(st_append_text(st7, movzx_byte(reg_rsi(), reg_rsi(), 8L)))))(st_append_text(st6, add_rr(reg_rsi(), reg_r11())))))(st_append_text(st5, mov_rr(reg_rsi(), reg_r12())))))(st_append_text(st4, jcc(cc_ge(), 0L)))))(st4.text_len)))(st_append_text(st3, cmp_rr(reg_r11(), reg_rdx())))))(st3.text_len)))(st_append_text(st2, li(reg_r11(), 0L)))))(st_append_text(st1, mov_store(reg_rbx(), reg_r13(), 0L)))))(st_append_text(st0, add_rr(reg_r13(), reg_rdx())))))(st_append_text(st, mov_rr(reg_r13(), reg_rcx())));

    public static StrConcatFastResult emit_str_concat_fast_bump(CodegenState st) => ((Func<CodegenState, StrConcatFastResult>)((st0) => ((Func<CodegenState, StrConcatFastResult>)((st1) => ((Func<CodegenState, StrConcatFastResult>)((st2) => ((Func<CodegenState, StrConcatFastResult>)((st3) => ((Func<CodegenState, StrConcatFastResult>)((st4) => ((Func<CodegenState, StrConcatFastResult>)((st5) => ((Func<long, StrConcatFastResult>)((fast_done_pos) => ((Func<CodegenState, StrConcatFastResult>)((st6) => new StrConcatFastResult(cg: st6, fast_done_pos: fast_done_pos)))(st_append_text(st5, jmp(0L)))))(st5.text_len)))(st_append_text(st4, mov_rr(reg_rax(), reg_rbx())))))(st_append_text(st3, add_rr(reg_r10(), reg_r11())))))(st_append_text(st2, lea(reg_r10(), reg_rbx(), 0L)))))(st_append_text(st1, and_ri(reg_r11(), (0L - 8L))))))(st_append_text(st0, add_ri(reg_r11(), 15L)))))(st_append_text(st, mov_rr(reg_r11(), reg_r13())));

    public static CodegenState emit_str_concat_slow_alloc(CodegenState st, long slow_path_pos) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => st_append_text(st7, mov_store(reg_rax(), reg_r13(), 0L))))(st_append_text(st6, add_rr(reg_r10(), reg_r11())))))(st_append_text(st5, and_ri(reg_r11(), (0L - 8L))))))(st_append_text(st4, add_ri(reg_r11(), 15L)))))(st_append_text(st3, mov_rr(reg_r11(), reg_r13())))))(st_append_text(st2, mov_rr(reg_rax(), reg_r10())))))(st_append_text(st1, add_rr(reg_r13(), reg_rdx())))))(st_append_text(st0, mov_rr(reg_r13(), reg_rcx())))))(patch_jcc_at(st, slow_path_pos, st.text_len));

    public static CodegenState emit_str_concat_slow_copy1(CodegenState st) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<long, CodegenState>)((loop1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<long, CodegenState>)((exit1_pos) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => patch_jcc_at(st11, exit1_pos, st11.text_len)))(st_append_text(st10, jmp((loop1 - (st10.text_len + 5L)))))))(st_append_text(st9, add_ri(reg_r11(), 1L)))))(st_append_text(st8, mov_store_byte(reg_rsi(), reg_rdx(), 8L)))))(st_append_text(st7, add_rr(reg_rsi(), reg_r11())))))(st_append_text(st6, mov_rr(reg_rsi(), reg_rax())))))(st_append_text(st5, movzx_byte(reg_rdx(), reg_rdx(), 8L)))))(st_append_text(st4, add_rr(reg_rdx(), reg_r11())))))(st_append_text(st3, mov_rr(reg_rdx(), reg_rbx())))))(st_append_text(st2, jcc(cc_ge(), 0L)))))(st2.text_len)))(st_append_text(st1, cmp_rr(reg_r11(), reg_rcx())))))(st1.text_len)))(st_append_text(st0, li(reg_r11(), 0L)))))(st_append_text(st, mov_load(reg_rcx(), reg_rbx(), 0L)));

    public static CodegenState emit_str_concat_slow_copy2(CodegenState st, long fast_done_pos) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<long, CodegenState>)((loop2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<long, CodegenState>)((exit2_pos) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => ((Func<CodegenState, CodegenState>)((st12) => ((Func<CodegenState, CodegenState>)((st13) => ((Func<CodegenState, CodegenState>)((st14) => patch_jmp_at(st14, fast_done_pos, st14.text_len)))(patch_jcc_at(st13, exit2_pos, st13.text_len))))(st_append_text(st12, jmp((loop2 - (st12.text_len + 5L)))))))(st_append_text(st11, add_ri(reg_r11(), 1L)))))(st_append_text(st10, mov_store_byte(reg_rdi(), reg_rsi(), 8L)))))(st_append_text(st9, add_rr(reg_rdi(), reg_r11())))))(st_append_text(st8, add_rr(reg_rdi(), reg_rcx())))))(st_append_text(st7, mov_rr(reg_rdi(), reg_rax())))))(st_append_text(st6, movzx_byte(reg_rsi(), reg_rsi(), 8L)))))(st_append_text(st5, add_rr(reg_rsi(), reg_r11())))))(st_append_text(st4, mov_rr(reg_rsi(), reg_r12())))))(st_append_text(st3, jcc(cc_ge(), 0L)))))(st3.text_len)))(st_append_text(st2, cmp_rr(reg_r11(), reg_rdx())))))(st2.text_len)))(st_append_text(st1, li(reg_r11(), 0L)))))(st_append_text(st0, mov_load(reg_rdx(), reg_r12(), 0L)))))(st_append_text(st, mov_load(reg_rcx(), reg_rbx(), 0L)));

    public static CodegenState emit_str_concat(CodegenState st) => ((Func<StrConcatCheckResult, CodegenState>)((prologue) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<StrConcatFastResult, CodegenState>)((fast) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => st_append_text(st6, x86_ret())))(st_append_text(st5, pop_r(reg_rbx())))))(st_append_text(st4, pop_r(reg_r12())))))(st_append_text(st3, pop_r(reg_r13())))))(emit_str_concat_slow_copy2(st2, fast.fast_done_pos))))(emit_str_concat_slow_copy1(st1))))(emit_str_concat_slow_alloc(fast.cg, prologue.slow_path_pos))))(emit_str_concat_fast_bump(st0))))(emit_str_concat_fast_copy(prologue.cg))))(emit_str_concat_prologue(st));

    public static HelpResult2 emit_ipow_setup(CodegenState st) => ((Func<CodegenState, HelpResult2>)((st0) => ((Func<CodegenState, HelpResult2>)((st1) => ((Func<CodegenState, HelpResult2>)((st2) => ((Func<long, HelpResult2>)((neg_pos) => ((Func<CodegenState, HelpResult2>)((st3) => ((Func<long, HelpResult2>)((zero_pos) => ((Func<CodegenState, HelpResult2>)((st4) => new HelpResult2(cg: st4, p1: neg_pos, p2: zero_pos)))(st_append_text(st3, jcc(cc_e(), 0L)))))(st3.text_len)))(st_append_text(st2, jcc(cc_l(), 0L)))))(st2.text_len)))(st_append_text(st1, cmp_ri(reg_rsi(), 0L)))))(st_append_text(st0, li(reg_rax(), 1L)))))(record_func_offset(st, "\u0055\u0055\u0011\u001F\u0010\u001B"));

    public static CodegenState emit_ipow_loop(CodegenState st) => ((Func<long, CodegenState>)((loop_top) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<long, CodegenState>)((skip_mul_pos) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<long, CodegenState>)((jmp_loop_pos) => ((Func<CodegenState, CodegenState>)((st8) => patch_jcc_at(st8, jmp_loop_pos, loop_top)))(st_append_text(st7, jcc(cc_g(), 0L)))))(st7.text_len)))(st_append_text(st6, cmp_ri(reg_rsi(), 0L)))))(st_append_text(st5, shr_ri(reg_rsi(), 1L)))))(st_append_text(st4, imul_rr(reg_rdi(), reg_rdi())))))(patch_jcc_at(st3, skip_mul_pos, st3.text_len))))(st_append_text(st2, imul_rr(reg_rax(), reg_rdi())))))(st_append_text(st1, jcc(cc_e(), 0L)))))(st1.text_len)))(st_append_text(st0, and_ri(reg_rcx(), 1L)))))(st_append_text(st, mov_rr(reg_rcx(), reg_rsi())))))(st.text_len);

    public static CodegenState emit_ipow(CodegenState st) => ((Func<HelpResult2, CodegenState>)((setup) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => st_append_text(st4, x86_ret())))(st_append_text(st3, li(reg_rax(), 0L)))))(patch_jcc_at(st2, setup.p1, st2.text_len))))(st_append_text(st1, x86_ret()))))(patch_jcc_at(st0, setup.p2, st0.text_len))))(emit_ipow_loop(setup.cg))))(emit_ipow_setup(st));

    public static HelpResult1 emit_text_to_int_setup(CodegenState st) => ((Func<CodegenState, HelpResult1>)((st0) => ((Func<CodegenState, HelpResult1>)((st1) => ((Func<CodegenState, HelpResult1>)((st2) => ((Func<CodegenState, HelpResult1>)((st3) => ((Func<CodegenState, HelpResult1>)((st4) => ((Func<CodegenState, HelpResult1>)((st5) => ((Func<CodegenState, HelpResult1>)((st6) => ((Func<long, HelpResult1>)((empty_pos) => ((Func<CodegenState, HelpResult1>)((st7) => ((Func<CodegenState, HelpResult1>)((st8) => ((Func<CodegenState, HelpResult1>)((st9) => ((Func<long, HelpResult1>)((not_minus_pos) => ((Func<CodegenState, HelpResult1>)((st10) => ((Func<CodegenState, HelpResult1>)((st11) => ((Func<CodegenState, HelpResult1>)((st12) => ((Func<CodegenState, HelpResult1>)((st13) => new HelpResult1(cg: st13, p1: empty_pos)))(patch_jcc_at(st12, not_minus_pos, st12.text_len))))(st_append_text(st11, add_ri(reg_r11(), 1L)))))(st_append_text(st10, li(reg_rsi(), 1L)))))(st_append_text(st9, jcc(cc_ne(), 0L)))))(st9.text_len)))(st_append_text(st8, cmp_ri(reg_rdx(), 73L)))))(st_append_text(st7, movzx_byte(reg_rdx(), reg_rdi(), 0L)))))(st_append_text(st6, jcc(cc_e(), 0L)))))(st6.text_len)))(st_append_text(st5, test_rr(reg_rcx(), reg_rcx())))))(st_append_text(st4, li(reg_rsi(), 0L)))))(st_append_text(st3, li(reg_r11(), 0L)))))(st_append_text(st2, li(reg_rax(), 0L)))))(st_append_text(st1, lea(reg_rdi(), reg_rdi(), 8L)))))(st_append_text(st0, mov_load(reg_rcx(), reg_rdi(), 0L)))))(record_func_offset(st, "\u0055\u0055\u000E\u000D\u0024\u000E\u0055\u000E\u0010\u0055\u0011\u0012\u000E"));

    public static CodegenState emit_text_to_int_parse(CodegenState st) => ((Func<long, CodegenState>)((parse_loop) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<long, CodegenState>)((parse_done_pos) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => ((Func<CodegenState, CodegenState>)((st12) => ((Func<CodegenState, CodegenState>)((st13) => ((Func<CodegenState, CodegenState>)((st14) => patch_jcc_at(st14, parse_done_pos, st14.text_len)))(st_append_text(st13, jmp((parse_loop - (st13.text_len + 5L)))))))(st_append_text(st12, add_ri(reg_r11(), 1L)))))(st_append_text(st11, add_rr(reg_rax(), reg_rdx())))))(st_append_text(st10, pop_r(reg_rdx())))))(st_append_text(st9, add_rr(reg_rax(), reg_rdx())))))(st_append_text(st8, add_rr(reg_rax(), reg_rdx())))))(st_append_text(st7, shl_ri(reg_rax(), 3L)))))(st_append_text(st6, mov_rr(reg_rdx(), reg_rax())))))(st_append_text(st5, push_r(reg_rdx())))))(st_append_text(st4, sub_ri(reg_rdx(), 3L)))))(st_append_text(st3, movzx_byte(reg_rdx(), reg_rdx(), 0L)))))(st_append_text(st2, add_rr(reg_rdx(), reg_r11())))))(st_append_text(st1, mov_rr(reg_rdx(), reg_rdi())))))(st_append_text(st0, jcc(cc_ge(), 0L)))))(st0.text_len)))(st_append_text(st, cmp_rr(reg_r11(), reg_rcx())))))(st.text_len);

    public static CodegenState emit_text_to_int(CodegenState st) => ((Func<HelpResult1, CodegenState>)((setup) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<long, CodegenState>)((no_neg_pos) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => st_append_text(st5, x86_ret())))(patch_jcc_at(st4, setup.p1, st4.text_len))))(patch_jcc_at(st3, no_neg_pos, st3.text_len))))(st_append_text(st2, neg_r(reg_rax())))))(st_append_text(st1, jcc(cc_e(), 0L)))))(st1.text_len)))(st_append_text(st0, test_rr(reg_rsi(), reg_rsi())))))(emit_text_to_int_parse(setup.cg))))(emit_text_to_int_setup(st));

    public static HelpResult1 emit_text_starts_with_head(CodegenState st) => ((Func<CodegenState, HelpResult1>)((st0) => ((Func<CodegenState, HelpResult1>)((st1) => ((Func<CodegenState, HelpResult1>)((st2) => ((Func<CodegenState, HelpResult1>)((st3) => ((Func<long, HelpResult1>)((too_long_pos) => ((Func<CodegenState, HelpResult1>)((st4) => new HelpResult1(cg: st4, p1: too_long_pos)))(st_append_text(st3, jcc(cc_g(), 0L)))))(st3.text_len)))(st_append_text(st2, cmp_rr(reg_rdx(), reg_rcx())))))(st_append_text(st1, mov_load(reg_rdx(), reg_rsi(), 0L)))))(st_append_text(st0, mov_load(reg_rcx(), reg_rdi(), 0L)))))(record_func_offset(st, "\u0055\u0055\u000E\u000D\u0024\u000E\u0055\u0013\u000E\u000F\u0015\u000E\u0013\u0055\u001B\u0011\u000E\u0014"));

    public static HelpResult1 emit_text_starts_with_loop(CodegenState st) => ((Func<CodegenState, HelpResult1>)((st0) => ((Func<long, HelpResult1>)((loop_top) => ((Func<CodegenState, HelpResult1>)((st1) => ((Func<long, HelpResult1>)((matched_pos) => ((Func<CodegenState, HelpResult1>)((st2) => ((Func<CodegenState, HelpResult1>)((st3) => ((Func<CodegenState, HelpResult1>)((st4) => ((Func<CodegenState, HelpResult1>)((st5) => ((Func<CodegenState, HelpResult1>)((st6) => ((Func<CodegenState, HelpResult1>)((st7) => ((Func<CodegenState, HelpResult1>)((st8) => ((Func<CodegenState, HelpResult1>)((st9) => ((Func<long, HelpResult1>)((mismatch_pos) => ((Func<CodegenState, HelpResult1>)((st10) => ((Func<CodegenState, HelpResult1>)((st11) => ((Func<CodegenState, HelpResult1>)((st12) => ((Func<CodegenState, HelpResult1>)((st13) => ((Func<CodegenState, HelpResult1>)((st14) => ((Func<CodegenState, HelpResult1>)((st15) => new HelpResult1(cg: st15, p1: mismatch_pos)))(st_append_text(st14, x86_ret()))))(st_append_text(st13, li(reg_rax(), 1L)))))(patch_jcc_at(st12, matched_pos, st12.text_len))))(st_append_text(st11, jmp((loop_top - (st11.text_len + 5L)))))))(st_append_text(st10, add_ri(reg_r11(), 1L)))))(st_append_text(st9, jcc(cc_ne(), 0L)))))(st9.text_len)))(st_append_text(st8, cmp_rr(reg_rax(), reg_r8())))))(st_append_text(st7, movzx_byte(reg_r8(), reg_r8(), 8L)))))(st_append_text(st6, add_rr(reg_r8(), reg_r11())))))(st_append_text(st5, mov_rr(reg_r8(), reg_rsi())))))(st_append_text(st4, movzx_byte(reg_rax(), reg_rax(), 8L)))))(st_append_text(st3, add_rr(reg_rax(), reg_r11())))))(st_append_text(st2, mov_rr(reg_rax(), reg_rdi())))))(st_append_text(st1, jcc(cc_ge(), 0L)))))(st1.text_len)))(st_append_text(st0, cmp_rr(reg_r11(), reg_rdx())))))(st0.text_len)))(st_append_text(st, li(reg_r11(), 0L)));

    public static CodegenState emit_text_starts_with(CodegenState st) => ((Func<HelpResult1, CodegenState>)((head) => ((Func<HelpResult1, CodegenState>)((lp) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => st_append_text(st2, x86_ret())))(st_append_text(st1, li(reg_rax(), 0L)))))(patch_jcc_at(st0, lp.p1, st0.text_len))))(patch_jcc_at(lp.cg, head.p1, lp.cg.text_len))))(emit_text_starts_with_loop(head.cg))))(emit_text_starts_with_head(st));

    public static HelpResult2 emit_text_contains_search(CodegenState st) => ((Func<CodegenState, HelpResult2>)((st0) => ((Func<CodegenState, HelpResult2>)((st1) => ((Func<CodegenState, HelpResult2>)((st2) => ((Func<CodegenState, HelpResult2>)((st3) => ((Func<long, HelpResult2>)((search_loop) => ((Func<CodegenState, HelpResult2>)((st4) => ((Func<CodegenState, HelpResult2>)((st5) => ((Func<CodegenState, HelpResult2>)((st6) => ((Func<CodegenState, HelpResult2>)((st7) => ((Func<long, HelpResult2>)((not_found_pos) => ((Func<CodegenState, HelpResult2>)((st8) => ((Func<CodegenState, HelpResult2>)((st9) => ((Func<CodegenState, HelpResult2>)((st10) => new HelpResult2(cg: st10, p1: not_found_pos, p2: search_loop)))(st_append_text(st9, li(reg_rax(), 0L)))))(st_append_text(st8, push_r(reg_r11())))))(st_append_text(st7, jcc(cc_ge(), 0L)))))(st7.text_len)))(st_append_text(st6, cmp_rr(reg_r11(), reg_rax())))))(st_append_text(st5, add_ri(reg_rax(), 1L)))))(st_append_text(st4, sub_rr(reg_rax(), reg_rdx())))))(st_append_text(st3, mov_rr(reg_rax(), reg_rcx())))))(st3.text_len)))(st_append_text(st2, li(reg_r11(), 0L)))))(st_append_text(st1, mov_load(reg_rdx(), reg_rsi(), 0L)))))(st_append_text(st0, mov_load(reg_rcx(), reg_rdi(), 0L)))))(record_func_offset(st, "\u0055\u0055\u000E\u000D\u0024\u000E\u0055\u0018\u0010\u0012\u000E\u000F\u0011\u0012\u0013"));

    public static CodegenState emit_text_contains_cmp(CodegenState st, long not_found_pos, long search_loop) => ((Func<long, CodegenState>)((cmp_loop) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<long, CodegenState>)((found_pos) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<long, CodegenState>)((mismatch_pos) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => ((Func<CodegenState, CodegenState>)((st12) => ((Func<CodegenState, CodegenState>)((st13) => ((Func<CodegenState, CodegenState>)((st14) => ((Func<CodegenState, CodegenState>)((st15) => ((Func<CodegenState, CodegenState>)((st16) => ((Func<CodegenState, CodegenState>)((st17) => ((Func<CodegenState, CodegenState>)((st18) => ((Func<CodegenState, CodegenState>)((st19) => ((Func<CodegenState, CodegenState>)((st20) => ((Func<CodegenState, CodegenState>)((st21) => ((Func<CodegenState, CodegenState>)((st22) => st_append_text(st22, x86_ret())))(st_append_text(st21, li(reg_rax(), 0L)))))(patch_jcc_at(st20, not_found_pos, st20.text_len))))(st_append_text(st19, jmp((search_loop - (st19.text_len + 5L)))))))(st_append_text(st18, add_ri(reg_r11(), 1L)))))(st_append_text(st17, pop_r(reg_r11())))))(patch_jcc_at(st16, mismatch_pos, st16.text_len))))(st_append_text(st15, x86_ret()))))(st_append_text(st14, li(reg_rax(), 1L)))))(st_append_text(st13, pop_r(reg_r11())))))(patch_jcc_at(st12, found_pos, st12.text_len))))(st_append_text(st11, jmp((cmp_loop - (st11.text_len + 5L)))))))(st_append_text(st10, add_ri(reg_rax(), 1L)))))(st_append_text(st9, jcc(cc_ne(), 0L)))))(st9.text_len)))(st_append_text(st8, cmp_rr(reg_r8(), reg_r9())))))(st_append_text(st7, movzx_byte(reg_r9(), reg_r9(), 8L)))))(st_append_text(st6, add_rr(reg_r9(), reg_rax())))))(st_append_text(st5, mov_rr(reg_r9(), reg_rsi())))))(st_append_text(st4, movzx_byte(reg_r8(), reg_r8(), 8L)))))(st_append_text(st3, add_rr(reg_r8(), reg_rax())))))(st_append_text(st2, add_rr(reg_r8(), reg_r11())))))(st_append_text(st1, mov_rr(reg_r8(), reg_rdi())))))(st_append_text(st0, jcc(cc_ge(), 0L)))))(st0.text_len)))(st_append_text(st, cmp_rr(reg_rax(), reg_rdx())))))(st.text_len);

    public static CodegenState emit_text_contains(CodegenState st) => ((Func<HelpResult2, CodegenState>)((search) => emit_text_contains_cmp(search.cg, search.p1, search.p2)))(emit_text_contains_search(st));

    public static CodegenState emit_text_compare_prologue(CodegenState st) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<long, CodegenState>)((len1_smaller_pos) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => patch_jcc_at(st8, len1_smaller_pos, st8.text_len)))(st_append_text(st7, mov_rr(reg_rbx(), reg_rcx())))))(st_append_text(st6, jcc(cc_le(), 0L)))))(st6.text_len)))(st_append_text(st5, cmp_rr(reg_rbx(), reg_rcx())))))(st_append_text(st4, mov_rr(reg_rbx(), reg_r12())))))(st_append_text(st3, mov_load(reg_rcx(), reg_rsi(), 0L)))))(st_append_text(st2, mov_load(reg_r12(), reg_rdi(), 0L)))))(st_append_text(st1, push_r(reg_r12())))))(st_append_text(st0, push_r(reg_rbx())))))(record_func_offset(st, "\u0055\u0055\u000E\u000D\u0024\u000E\u0055\u0018\u0010\u001A\u001F\u000F\u0015\u000D"));

    public static HelpResult3 emit_text_compare_byte_load(CodegenState st) => ((Func<CodegenState, HelpResult3>)((st0) => ((Func<long, HelpResult3>)((cmp_loop) => ((Func<CodegenState, HelpResult3>)((st1) => ((Func<long, HelpResult3>)((cmp_done_pos) => ((Func<CodegenState, HelpResult3>)((st2) => ((Func<CodegenState, HelpResult3>)((st3) => ((Func<CodegenState, HelpResult3>)((st4) => ((Func<CodegenState, HelpResult3>)((st5) => ((Func<CodegenState, HelpResult3>)((st6) => ((Func<CodegenState, HelpResult3>)((st7) => ((Func<CodegenState, HelpResult3>)((st8) => ((Func<CodegenState, HelpResult3>)((st9) => ((Func<long, HelpResult3>)((bytes_equal_pos) => ((Func<CodegenState, HelpResult3>)((st10) => new HelpResult3(cg: st10, p1: cmp_done_pos, p2: bytes_equal_pos, p3: cmp_loop)))(st_append_text(st9, jcc(cc_e(), 0L)))))(st9.text_len)))(st_append_text(st8, cmp_rr(reg_rax(), reg_rdx())))))(st_append_text(st7, movzx_byte(reg_rdx(), reg_rdx(), 8L)))))(st_append_text(st6, add_rr(reg_rdx(), reg_r11())))))(st_append_text(st5, mov_rr(reg_rdx(), reg_rsi())))))(st_append_text(st4, movzx_byte(reg_rax(), reg_rax(), 8L)))))(st_append_text(st3, add_rr(reg_rax(), reg_r11())))))(st_append_text(st2, mov_rr(reg_rax(), reg_rdi())))))(st_append_text(st1, jcc(cc_ge(), 0L)))))(st1.text_len)))(st_append_text(st0, cmp_rr(reg_r11(), reg_rbx())))))(st0.text_len)))(st_append_text(st, li(reg_r11(), 0L)));

    public static HelpResult2 emit_text_compare_diff(CodegenState st, long bytes_equal_pos, long cmp_loop) => ((Func<long, HelpResult2>)((a_greater_pos) => ((Func<CodegenState, HelpResult2>)((st0) => ((Func<CodegenState, HelpResult2>)((st1) => ((Func<long, HelpResult2>)((ret_early1) => ((Func<CodegenState, HelpResult2>)((st2) => ((Func<CodegenState, HelpResult2>)((st3) => ((Func<CodegenState, HelpResult2>)((st4) => ((Func<long, HelpResult2>)((ret_early2) => ((Func<CodegenState, HelpResult2>)((st5) => ((Func<CodegenState, HelpResult2>)((st6) => ((Func<CodegenState, HelpResult2>)((st7) => ((Func<CodegenState, HelpResult2>)((st8) => new HelpResult2(cg: st8, p1: ret_early1, p2: ret_early2)))(st_append_text(st7, jmp((cmp_loop - (st7.text_len + 5L)))))))(st_append_text(st6, add_ri(reg_r11(), 1L)))))(patch_jcc_at(st5, bytes_equal_pos, st5.text_len))))(st_append_text(st4, jmp(0L)))))(st4.text_len)))(st_append_text(st3, li(reg_rax(), 1L)))))(patch_jcc_at(st2, a_greater_pos, st2.text_len))))(st_append_text(st1, jmp(0L)))))(st1.text_len)))(st_append_text(st0, li(reg_rax(), (-1L))))))(st_append_text(st, jcc(cc_g(), 0L)))))(st.text_len);

    public static HelpResult2 emit_text_compare_len(CodegenState st, long cmp_done_pos) => ((Func<CodegenState, HelpResult2>)((st0) => ((Func<CodegenState, HelpResult2>)((st1) => ((Func<long, HelpResult2>)((len_eq_pos) => ((Func<CodegenState, HelpResult2>)((st2) => ((Func<long, HelpResult2>)((len_gt_pos) => ((Func<CodegenState, HelpResult2>)((st3) => ((Func<CodegenState, HelpResult2>)((st4) => ((Func<long, HelpResult2>)((ret_len1) => ((Func<CodegenState, HelpResult2>)((st5) => ((Func<CodegenState, HelpResult2>)((st6) => ((Func<CodegenState, HelpResult2>)((st7) => ((Func<long, HelpResult2>)((ret_len2) => ((Func<CodegenState, HelpResult2>)((st8) => ((Func<CodegenState, HelpResult2>)((st9) => ((Func<CodegenState, HelpResult2>)((st10) => new HelpResult2(cg: st10, p1: ret_len1, p2: ret_len2)))(st_append_text(st9, li(reg_rax(), 0L)))))(patch_jcc_at(st8, len_eq_pos, st8.text_len))))(st_append_text(st7, jmp(0L)))))(st7.text_len)))(st_append_text(st6, li(reg_rax(), 1L)))))(patch_jcc_at(st5, len_gt_pos, st5.text_len))))(st_append_text(st4, jmp(0L)))))(st4.text_len)))(st_append_text(st3, li(reg_rax(), (-1L))))))(st_append_text(st2, jcc(cc_g(), 0L)))))(st2.text_len)))(st_append_text(st1, jcc(cc_e(), 0L)))))(st1.text_len)))(st_append_text(st0, cmp_rr(reg_r12(), reg_rcx())))))(patch_jcc_at(st, cmp_done_pos, st.text_len));

    public static CodegenState emit_text_compare(CodegenState st) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<HelpResult3, CodegenState>)((bytes) => ((Func<HelpResult2, CodegenState>)((diff) => ((Func<HelpResult2, CodegenState>)((lens) => ((Func<long, CodegenState>)((end_pos) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => st_append_text(st6, x86_ret())))(st_append_text(st5, pop_r(reg_rbx())))))(st_append_text(st4, pop_r(reg_r12())))))(patch_jmp_at(st3, lens.p2, end_pos))))(patch_jmp_at(st2, lens.p1, end_pos))))(patch_jmp_at(st1, diff.p2, end_pos))))(patch_jmp_at(lens.cg, diff.p1, end_pos))))(lens.cg.text_len)))(emit_text_compare_len(diff.cg, bytes.p1))))(emit_text_compare_diff(bytes.cg, bytes.p2, bytes.p3))))(emit_text_compare_byte_load(st0))))(emit_text_compare_prologue(st));

    public static CodegenState emit_list_contains(CodegenState st) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<long, CodegenState>)((loop_top) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<long, CodegenState>)((not_found_pos) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<long, CodegenState>)((found_pos) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => ((Func<CodegenState, CodegenState>)((st12) => ((Func<CodegenState, CodegenState>)((st13) => ((Func<CodegenState, CodegenState>)((st14) => ((Func<CodegenState, CodegenState>)((st15) => ((Func<CodegenState, CodegenState>)((st16) => ((Func<CodegenState, CodegenState>)((st17) => st_append_text(st17, x86_ret())))(st_append_text(st16, li(reg_rax(), 0L)))))(patch_jcc_at(st15, not_found_pos, st15.text_len))))(st_append_text(st14, x86_ret()))))(st_append_text(st13, li(reg_rax(), 1L)))))(patch_jcc_at(st12, found_pos, st12.text_len))))(st_append_text(st11, jmp((loop_top - (st11.text_len + 5L)))))))(st_append_text(st10, add_ri(reg_r11(), 1L)))))(st_append_text(st9, jcc(cc_e(), 0L)))))(st9.text_len)))(st_append_text(st8, cmp_rr(reg_rax(), reg_rsi())))))(st_append_text(st7, mov_load(reg_rax(), reg_rax(), 8L)))))(st_append_text(st6, add_rr(reg_rax(), reg_rdi())))))(st_append_text(st5, shl_ri(reg_rax(), 3L)))))(st_append_text(st4, mov_rr(reg_rax(), reg_r11())))))(st_append_text(st3, jcc(cc_ge(), 0L)))))(st3.text_len)))(st_append_text(st2, cmp_rr(reg_r11(), reg_rcx())))))(st2.text_len)))(st_append_text(st1, li(reg_r11(), 0L)))))(st_append_text(st0, mov_load(reg_rcx(), reg_rdi(), 0L)))))(record_func_offset(st, "\u0055\u0055\u0017\u0011\u0013\u000E\u0055\u0018\u0010\u0012\u000E\u000F\u0011\u0012\u0013"));

    public static CodegenState emit_list_cons_alloc(CodegenState st) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<long, CodegenState>)((cap_ok_pos) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => ((Func<CodegenState, CodegenState>)((st12) => ((Func<CodegenState, CodegenState>)((st13) => ((Func<CodegenState, CodegenState>)((st14) => ((Func<CodegenState, CodegenState>)((st15) => ((Func<CodegenState, CodegenState>)((st16) => st_append_text(st16, mov_store(reg_rax(), reg_rdi(), 8L))))(st_append_text(st15, mov_store(reg_rax(), reg_rdx(), 0L)))))(st_append_text(st14, add_rr(reg_r10(), reg_r11())))))(st_append_text(st13, shl_ri(reg_r11(), 3L)))))(st_append_text(st12, add_ri(reg_r11(), 1L)))))(st_append_text(st11, mov_rr(reg_rax(), reg_r10())))))(st_append_text(st10, add_ri(reg_r10(), 8L)))))(st_append_text(st9, mov_store(reg_r10(), reg_r11(), 0L)))))(patch_jcc_at(st8, cap_ok_pos, st8.text_len))))(st_append_text(st7, li(reg_r11(), 4L)))))(st_append_text(st6, jcc(cc_ge(), 0L)))))(st6.text_len)))(st_append_text(st5, cmp_ri(reg_r11(), 4L)))))(st_append_text(st4, shl_ri(reg_r11(), 1L)))))(st_append_text(st3, mov_rr(reg_r11(), reg_rdx())))))(st_append_text(st2, add_ri(reg_rdx(), 1L)))))(st_append_text(st1, mov_rr(reg_rdx(), reg_rcx())))))(st_append_text(st0, mov_load(reg_rcx(), reg_rsi(), 0L)))))(record_func_offset(st, "\u0055\u0055\u0017\u0011\u0013\u000E\u0055\u0018\u0010\u0012\u0013"));

    public static CodegenState emit_list_cons_copy(CodegenState st) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<long, CodegenState>)((loop_top) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<long, CodegenState>)((exit_pos) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => ((Func<CodegenState, CodegenState>)((st12) => st_append_text(st12, x86_ret())))(patch_jcc_at(st11, exit_pos, st11.text_len))))(st_append_text(st10, jmp((loop_top - (st10.text_len + 5L)))))))(st_append_text(st9, add_ri(reg_r11(), 8L)))))(st_append_text(st8, mov_store(reg_rdi(), reg_rdx(), 16L)))))(st_append_text(st7, add_rr(reg_rdi(), reg_r11())))))(st_append_text(st6, mov_rr(reg_rdi(), reg_rax())))))(st_append_text(st5, mov_load(reg_rdx(), reg_rdx(), 8L)))))(st_append_text(st4, add_rr(reg_rdx(), reg_r11())))))(st_append_text(st3, mov_rr(reg_rdx(), reg_rsi())))))(st_append_text(st2, jcc(cc_ge(), 0L)))))(st2.text_len)))(st_append_text(st1, cmp_rr(reg_r11(), reg_rcx())))))(st1.text_len)))(st_append_text(st0, li(reg_r11(), 0L)))))(st_append_text(st, shl_ri(reg_rcx(), 3L)));

    public static CodegenState emit_list_cons(CodegenState st) => ((Func<CodegenState, CodegenState>)((st0) => emit_list_cons_copy(st0)))(emit_list_cons_alloc(st));

    public static HelpResult1 emit_list_append_prologue(CodegenState st) => ((Func<CodegenState, HelpResult1>)((st0) => ((Func<CodegenState, HelpResult1>)((st1) => ((Func<CodegenState, HelpResult1>)((st2) => ((Func<CodegenState, HelpResult1>)((st3) => ((Func<CodegenState, HelpResult1>)((st4) => ((Func<CodegenState, HelpResult1>)((st5) => ((Func<CodegenState, HelpResult1>)((st6) => ((Func<long, HelpResult1>)((p2_pos) => ((Func<CodegenState, HelpResult1>)((st7) => new HelpResult1(cg: st7, p1: p2_pos)))(st_append_text(st6, jcc(cc_l(), 0L)))))(st6.text_len)))(st_append_text(st5, cmp_rr(reg_rax(), reg_rdx())))))(st_append_text(st4, sub_rr(reg_rax(), reg_rcx())))))(st_append_text(st3, mov_rr(reg_rax(), reg_r11())))))(st_append_text(st2, mov_load(reg_r11(), reg_rdi(), (0L - 8L))))))(st_append_text(st1, mov_load(reg_rdx(), reg_rsi(), 0L)))))(st_append_text(st0, mov_load(reg_rcx(), reg_rdi(), 0L)))))(record_func_offset(st, "\u0055\u0055\u0017\u0011\u0013\u000E\u0055\u000F\u001F\u001F\u000D\u0012\u0016"));

    public static CodegenState emit_list_append_path1(CodegenState st) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<long, CodegenState>)((skip_pos) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<long, CodegenState>)((loop_pos) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => ((Func<CodegenState, CodegenState>)((st12) => ((Func<CodegenState, CodegenState>)((st13) => ((Func<CodegenState, CodegenState>)((st14) => ((Func<CodegenState, CodegenState>)((st15) => ((Func<CodegenState, CodegenState>)((st16) => ((Func<CodegenState, CodegenState>)((st17) => st_append_text(st17, x86_ret())))(st_append_text(st16, mov_rr(reg_rax(), reg_rdi())))))(st_append_text(st15, mov_store(reg_rdi(), reg_rcx(), 0L)))))(st_append_text(st14, add_rr(reg_rcx(), reg_rdx())))))(st_append_text(st13, mov_load(reg_rdx(), reg_rsi(), 0L)))))(st_append_text(st12, mov_load(reg_rcx(), reg_rdi(), 0L)))))(patch_jcc_at(st11, skip_pos, st11.text_len))))(st_append_text(st10, jcc(cc_ne(), (loop_pos - (st10.text_len + 6L)))))))(st_append_text(st9, sub_ri(reg_rdx(), 1L)))))(st_append_text(st8, add_ri(reg_r11(), 8L)))))(st_append_text(st7, add_ri(reg_rax(), 8L)))))(st_append_text(st6, mov_store(reg_rax(), reg_rcx(), 8L)))))(st_append_text(st5, mov_load(reg_rcx(), reg_r11(), 8L)))))(st5.text_len)))(st_append_text(st4, jcc(cc_e(), 0L)))))(st4.text_len)))(st_append_text(st3, cmp_ri(reg_rdx(), 0L)))))(st_append_text(st2, mov_rr(reg_r11(), reg_rsi())))))(st_append_text(st1, add_rr(reg_rax(), reg_rdi())))))(st_append_text(st0, shl_ri(reg_rax(), 3L)))))(st_append_text(st, mov_rr(reg_rax(), reg_rcx())));

    public static CodegenState emit_list_append_path2_alloc(CodegenState st, long p2_pos) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => ((Func<CodegenState, CodegenState>)((st12) => ((Func<long, CodegenState>)((cap_ok_pos) => ((Func<CodegenState, CodegenState>)((st13) => ((Func<CodegenState, CodegenState>)((st14) => ((Func<CodegenState, CodegenState>)((st15) => ((Func<CodegenState, CodegenState>)((st16) => ((Func<CodegenState, CodegenState>)((st17) => ((Func<CodegenState, CodegenState>)((st18) => ((Func<CodegenState, CodegenState>)((st19) => ((Func<CodegenState, CodegenState>)((st20) => ((Func<CodegenState, CodegenState>)((st21) => ((Func<CodegenState, CodegenState>)((st22) => st_append_text(st22, mov_store(reg_rsi(), reg_r13(), 0L))))(st_append_text(st21, add_rr(reg_r10(), reg_r11())))))(st_append_text(st20, shl_ri(reg_r11(), 3L)))))(st_append_text(st19, add_ri(reg_r11(), 1L)))))(st_append_text(st18, mov_rr(reg_r11(), reg_rax())))))(st_append_text(st17, mov_rr(reg_rsi(), reg_r10())))))(st_append_text(st16, add_ri(reg_r10(), 8L)))))(st_append_text(st15, mov_store(reg_r10(), reg_rax(), 0L)))))(patch_jcc_at(st14, cap_ok_pos, st14.text_len))))(st_append_text(st13, li(reg_rax(), 4L)))))(st_append_text(st12, jcc(cc_ge(), 0L)))))(st12.text_len)))(st_append_text(st11, cmp_ri(reg_rax(), 4L)))))(st_append_text(st10, shl_ri(reg_rax(), 1L)))))(st_append_text(st9, mov_rr(reg_rax(), reg_r13())))))(st_append_text(st8, add_rr(reg_r13(), reg_rdx())))))(st_append_text(st7, mov_rr(reg_r13(), reg_rcx())))))(st_append_text(st6, mov_load(reg_rdx(), reg_r12(), 0L)))))(st_append_text(st5, mov_load(reg_rcx(), reg_rbx(), 0L)))))(st_append_text(st4, mov_rr(reg_r12(), reg_rsi())))))(st_append_text(st3, mov_rr(reg_rbx(), reg_rdi())))))(st_append_text(st2, push_r(reg_r13())))))(st_append_text(st1, push_r(reg_r12())))))(st_append_text(st0, push_r(reg_rbx())))))(patch_jcc_at(st, p2_pos, st.text_len));

    public static CodegenState emit_list_append_path2_copy_a(CodegenState st) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<long, CodegenState>)((skip_pos) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<long, CodegenState>)((loop_pos) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => patch_jcc_at(st9, skip_pos, st9.text_len)))(st_append_text(st8, jcc(cc_ne(), (loop_pos - (st8.text_len + 6L)))))))(st_append_text(st7, sub_ri(reg_rcx(), 1L)))))(st_append_text(st6, add_ri(reg_rdi(), 8L)))))(st_append_text(st5, add_ri(reg_rax(), 8L)))))(st_append_text(st4, mov_store(reg_rdi(), reg_r11(), 8L)))))(st_append_text(st3, mov_load(reg_r11(), reg_rax(), 8L)))))(st3.text_len)))(st_append_text(st2, jcc(cc_e(), 0L)))))(st2.text_len)))(st_append_text(st1, cmp_ri(reg_rcx(), 0L)))))(st_append_text(st0, mov_rr(reg_rdi(), reg_rsi())))))(st_append_text(st, mov_rr(reg_rax(), reg_rbx())));

    public static CodegenState emit_list_append_path2_copy_b(CodegenState st) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<long, CodegenState>)((skip_pos) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<long, CodegenState>)((loop_pos) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => ((Func<CodegenState, CodegenState>)((st12) => ((Func<CodegenState, CodegenState>)((st13) => st_append_text(st13, x86_ret())))(st_append_text(st12, pop_r(reg_rbx())))))(st_append_text(st11, pop_r(reg_r12())))))(st_append_text(st10, pop_r(reg_r13())))))(st_append_text(st9, mov_rr(reg_rax(), reg_rsi())))))(patch_jcc_at(st8, skip_pos, st8.text_len))))(st_append_text(st7, jcc(cc_ne(), (loop_pos - (st7.text_len + 6L)))))))(st_append_text(st6, sub_ri(reg_rdx(), 1L)))))(st_append_text(st5, add_ri(reg_rdi(), 8L)))))(st_append_text(st4, add_ri(reg_rax(), 8L)))))(st_append_text(st3, mov_store(reg_rdi(), reg_r11(), 8L)))))(st_append_text(st2, mov_load(reg_r11(), reg_rax(), 8L)))))(st2.text_len)))(st_append_text(st1, jcc(cc_e(), 0L)))))(st1.text_len)))(st_append_text(st0, cmp_ri(reg_rdx(), 0L)))))(st_append_text(st, mov_rr(reg_rax(), reg_r12())));

    public static CodegenState emit_list_append(CodegenState st) => ((Func<HelpResult1, CodegenState>)((h) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => emit_list_append_path2_copy_b(st3)))(emit_list_append_path2_copy_a(st2))))(emit_list_append_path2_alloc(st1, h.p1))))(emit_list_append_path1(h.cg))))(emit_list_append_prologue(st));

    public static HelpResult1 emit_list_snoc_path1(CodegenState st) => ((Func<CodegenState, HelpResult1>)((st0) => ((Func<CodegenState, HelpResult1>)((st1) => ((Func<CodegenState, HelpResult1>)((st2) => ((Func<CodegenState, HelpResult1>)((st3) => ((Func<long, HelpResult1>)((path2_pos) => ((Func<CodegenState, HelpResult1>)((st4) => ((Func<CodegenState, HelpResult1>)((st5) => ((Func<CodegenState, HelpResult1>)((st6) => ((Func<CodegenState, HelpResult1>)((st7) => ((Func<CodegenState, HelpResult1>)((st8) => ((Func<CodegenState, HelpResult1>)((st9) => ((Func<CodegenState, HelpResult1>)((st10) => ((Func<CodegenState, HelpResult1>)((st11) => ((Func<CodegenState, HelpResult1>)((st12) => new HelpResult1(cg: st12, p1: path2_pos)))(st_append_text(st11, x86_ret()))))(st_append_text(st10, mov_rr(reg_rax(), reg_rdi())))))(st_append_text(st9, mov_store(reg_rdi(), reg_rcx(), 0L)))))(st_append_text(st8, add_ri(reg_rcx(), 1L)))))(st_append_text(st7, mov_store(reg_rax(), reg_rsi(), 8L)))))(st_append_text(st6, add_rr(reg_rax(), reg_rdi())))))(st_append_text(st5, shl_ri(reg_rax(), 3L)))))(st_append_text(st4, mov_rr(reg_rax(), reg_rcx())))))(st_append_text(st3, jcc(cc_ge(), 0L)))))(st3.text_len)))(st_append_text(st2, cmp_rr(reg_rcx(), reg_rdx())))))(st_append_text(st1, mov_load(reg_rdx(), reg_rdi(), (0L - 8L))))))(st_append_text(st0, mov_load(reg_rcx(), reg_rdi(), 0L)))))(record_func_offset(st, "\u0055\u0055\u0017\u0011\u0013\u000E\u0055\u0013\u0012\u0010\u0018"));

    public static HelpResult1 emit_list_snoc_path2(CodegenState st, long path2_pos) => ((Func<CodegenState, HelpResult1>)((st0) => ((Func<CodegenState, HelpResult1>)((st1) => ((Func<CodegenState, HelpResult1>)((st2) => ((Func<CodegenState, HelpResult1>)((st3) => ((Func<CodegenState, HelpResult1>)((st4) => ((Func<CodegenState, HelpResult1>)((st5) => ((Func<long, HelpResult1>)((path3_pos) => ((Func<CodegenState, HelpResult1>)((st6) => ((Func<CodegenState, HelpResult1>)((st7) => ((Func<CodegenState, HelpResult1>)((st8) => ((Func<CodegenState, HelpResult1>)((st9) => ((Func<long, HelpResult1>)((cap_ok_pos) => ((Func<CodegenState, HelpResult1>)((st10) => ((Func<CodegenState, HelpResult1>)((st11) => ((Func<CodegenState, HelpResult1>)((st12) => ((Func<CodegenState, HelpResult1>)((st13) => ((Func<CodegenState, HelpResult1>)((st14) => ((Func<CodegenState, HelpResult1>)((st15) => ((Func<CodegenState, HelpResult1>)((st16) => ((Func<CodegenState, HelpResult1>)((st17) => ((Func<CodegenState, HelpResult1>)((st18) => new HelpResult1(cg: st18, p1: path3_pos)))(st_append_text(st17, shl_ri(reg_rax(), 3L)))))(st_append_text(st16, mov_rr(reg_rax(), reg_rcx())))))(st_append_text(st15, add_rr(reg_r10(), reg_rax())))))(st_append_text(st14, shl_ri(reg_rax(), 3L)))))(st_append_text(st13, sub_rr(reg_rax(), reg_rdx())))))(st_append_text(st12, mov_store(reg_rdi(), reg_rax(), (0L - 8L))))))(patch_jcc_at(st11, cap_ok_pos, st11.text_len))))(st_append_text(st10, li(reg_rax(), 4L)))))(st_append_text(st9, jcc(cc_ge(), 0L)))))(st9.text_len)))(st_append_text(st8, cmp_ri(reg_rax(), 4L)))))(st_append_text(st7, shl_ri(reg_rax(), 1L)))))(st_append_text(st6, mov_rr(reg_rax(), reg_rdx())))))(st_append_text(st5, jcc(cc_ne(), 0L)))))(st5.text_len)))(st_append_text(st4, cmp_rr(reg_rax(), reg_r10())))))(st_append_text(st3, add_rr(reg_rax(), reg_rdi())))))(st_append_text(st2, shl_ri(reg_rax(), 3L)))))(st_append_text(st1, add_ri(reg_rax(), 1L)))))(st_append_text(st0, mov_rr(reg_rax(), reg_rdx())))))(patch_jcc_at(st, path2_pos, st.text_len));

    public static CodegenState emit_list_snoc_path2_store(CodegenState st) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => st_append_text(st4, x86_ret())))(st_append_text(st3, mov_rr(reg_rax(), reg_rdi())))))(st_append_text(st2, mov_store(reg_rdi(), reg_rcx(), 0L)))))(st_append_text(st1, add_ri(reg_rcx(), 1L)))))(st_append_text(st0, mov_store(reg_rax(), reg_rsi(), 8L)))))(st_append_text(st, add_rr(reg_rax(), reg_rdi())));

    public static CodegenState emit_list_snoc_path3_alloc(CodegenState st, long path3_pos) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<long, CodegenState>)((cap_ok2_pos) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => ((Func<CodegenState, CodegenState>)((st12) => ((Func<CodegenState, CodegenState>)((st13) => ((Func<CodegenState, CodegenState>)((st14) => ((Func<CodegenState, CodegenState>)((st15) => ((Func<CodegenState, CodegenState>)((st16) => ((Func<CodegenState, CodegenState>)((st17) => st_append_text(st17, add_rr(reg_r10(), reg_rdx()))))(st_append_text(st16, shl_ri(reg_rdx(), 3L)))))(st_append_text(st15, add_ri(reg_rdx(), 1L)))))(st_append_text(st14, mov_rr(reg_rdx(), reg_r13())))))(st_append_text(st13, mov_rr(reg_rax(), reg_r10())))))(st_append_text(st12, add_ri(reg_r10(), 8L)))))(st_append_text(st11, mov_store(reg_r10(), reg_r13(), 0L)))))(patch_jcc_at(st10, cap_ok2_pos, st10.text_len))))(st_append_text(st9, li(reg_r13(), 4L)))))(st_append_text(st8, jcc(cc_ge(), 0L)))))(st8.text_len)))(st_append_text(st7, cmp_ri(reg_r13(), 4L)))))(st_append_text(st6, shl_ri(reg_r13(), 1L)))))(st_append_text(st5, mov_rr(reg_r13(), reg_rcx())))))(st_append_text(st4, mov_rr(reg_r12(), reg_rsi())))))(st_append_text(st3, mov_rr(reg_rbx(), reg_rdi())))))(st_append_text(st2, push_r(reg_r13())))))(st_append_text(st1, push_r(reg_r12())))))(st_append_text(st0, push_r(reg_rbx())))))(patch_jcc_at(st, path3_pos, st.text_len));

    public static CodegenState emit_list_snoc_path3_copy(CodegenState st) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<long, CodegenState>)((copy_loop) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<long, CodegenState>)((copy_done_pos) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => ((Func<CodegenState, CodegenState>)((st12) => ((Func<CodegenState, CodegenState>)((st13) => ((Func<CodegenState, CodegenState>)((st14) => ((Func<CodegenState, CodegenState>)((st15) => ((Func<CodegenState, CodegenState>)((st16) => patch_jcc_at(st16, copy_done_pos, st16.text_len)))(st_append_text(st15, jmp((copy_loop - (st15.text_len + 5L)))))))(st_append_text(st14, add_ri(reg_r11(), 1L)))))(st_append_text(st13, mov_store(reg_rdi(), reg_rsi(), 8L)))))(st_append_text(st12, add_rr(reg_rdi(), reg_rdx())))))(st_append_text(st11, mov_rr(reg_rdi(), reg_rax())))))(st_append_text(st10, mov_load(reg_rsi(), reg_rsi(), 8L)))))(st_append_text(st9, add_rr(reg_rsi(), reg_rdx())))))(st_append_text(st8, mov_rr(reg_rsi(), reg_rbx())))))(st_append_text(st7, shl_ri(reg_rdx(), 3L)))))(st_append_text(st6, mov_rr(reg_rdx(), reg_r11())))))(st_append_text(st5, jcc(cc_ge(), 0L)))))(st5.text_len)))(st_append_text(st4, cmp_rr(reg_r11(), reg_rcx())))))(st4.text_len)))(st_append_text(st3, li(reg_r11(), 0L)))))(st_append_text(st2, mov_store(reg_rax(), reg_rdx(), 0L)))))(st_append_text(st1, add_ri(reg_rdx(), 1L)))))(st_append_text(st0, mov_rr(reg_rdx(), reg_rcx())))))(st_append_text(st, mov_load(reg_rcx(), reg_rbx(), 0L)));

    public static CodegenState emit_list_snoc_path3_finish(CodegenState st) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => st_append_text(st7, x86_ret())))(st_append_text(st6, pop_r(reg_rbx())))))(st_append_text(st5, pop_r(reg_r12())))))(st_append_text(st4, pop_r(reg_r13())))))(st_append_text(st3, mov_store(reg_rdi(), reg_r12(), 8L)))))(st_append_text(st2, add_rr(reg_rdi(), reg_rdx())))))(st_append_text(st1, mov_rr(reg_rdi(), reg_rax())))))(st_append_text(st0, shl_ri(reg_rdx(), 3L)))))(st_append_text(st, mov_rr(reg_rdx(), reg_rcx())));

    public static CodegenState emit_list_snoc(CodegenState st) => ((Func<HelpResult1, CodegenState>)((p1) => ((Func<HelpResult1, CodegenState>)((p2) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => emit_list_snoc_path3_finish(st2)))(emit_list_snoc_path3_copy(st1))))(emit_list_snoc_path3_alloc(st0, p2.p1))))(emit_list_snoc_path2_store(p2.cg))))(emit_list_snoc_path2(p1.cg, p1.p1))))(emit_list_snoc_path1(st));

    public static HelpResult2 emit_list_insert_at_prologue(CodegenState st) => ((Func<CodegenState, HelpResult2>)((st0) => ((Func<CodegenState, HelpResult2>)((st1) => ((Func<CodegenState, HelpResult2>)((st2) => ((Func<CodegenState, HelpResult2>)((st3) => ((Func<CodegenState, HelpResult2>)((st4) => ((Func<CodegenState, HelpResult2>)((st5) => ((Func<CodegenState, HelpResult2>)((st6) => ((Func<CodegenState, HelpResult2>)((st7) => ((Func<CodegenState, HelpResult2>)((st8) => ((Func<CodegenState, HelpResult2>)((st9) => ((Func<CodegenState, HelpResult2>)((st10) => ((Func<long, HelpResult2>)((in_place_pos) => ((Func<CodegenState, HelpResult2>)((st11) => ((Func<CodegenState, HelpResult2>)((st12) => ((Func<CodegenState, HelpResult2>)((st13) => ((Func<CodegenState, HelpResult2>)((st14) => ((Func<CodegenState, HelpResult2>)((st15) => ((Func<CodegenState, HelpResult2>)((st16) => ((Func<long, HelpResult2>)((path3_pos) => ((Func<CodegenState, HelpResult2>)((st17) => new HelpResult2(cg: st17, p1: in_place_pos, p2: path3_pos)))(st_append_text(st16, jcc(cc_ne(), 0L)))))(st16.text_len)))(st_append_text(st15, cmp_rr(reg_rax(), reg_r10())))))(st_append_text(st14, add_rr(reg_rax(), reg_rbx())))))(st_append_text(st13, shl_ri(reg_rax(), 3L)))))(st_append_text(st12, add_ri(reg_rax(), 1L)))))(st_append_text(st11, mov_rr(reg_rax(), reg_rcx())))))(st_append_text(st10, jcc(cc_l(), 0L)))))(st10.text_len)))(st_append_text(st9, cmp_rr(reg_r14(), reg_rcx())))))(st_append_text(st8, mov_load(reg_rcx(), reg_rbx(), (0L - 8L))))))(st_append_text(st7, mov_load(reg_r14(), reg_rbx(), 0L)))))(st_append_text(st6, mov_rr(reg_r13(), reg_rdx())))))(st_append_text(st5, mov_rr(reg_r12(), reg_rsi())))))(st_append_text(st4, mov_rr(reg_rbx(), reg_rdi())))))(st_append_text(st3, push_r(reg_r14())))))(st_append_text(st2, push_r(reg_r13())))))(st_append_text(st1, push_r(reg_r12())))))(st_append_text(st0, push_r(reg_rbx())))))(record_func_offset(st, "\u0055\u0055\u0017\u0011\u0013\u000E\u0055\u0011\u0012\u0013\u000D\u0015\u000E\u0055\u000F\u000E"));

    public static CodegenState emit_list_insert_at_grow(CodegenState st) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<long, CodegenState>)((cap_ok_pos) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => st_append_text(st8, add_rr(reg_r10(), reg_rax()))))(st_append_text(st7, shl_ri(reg_rax(), 3L)))))(st_append_text(st6, sub_rr(reg_rax(), reg_rcx())))))(st_append_text(st5, mov_store(reg_rbx(), reg_rax(), (0L - 8L))))))(patch_jcc_at(st4, cap_ok_pos, st4.text_len))))(st_append_text(st3, li(reg_rax(), 4L)))))(st_append_text(st2, jcc(cc_ge(), 0L)))))(st2.text_len)))(st_append_text(st1, cmp_ri(reg_rax(), 4L)))))(st_append_text(st0, shl_ri(reg_rax(), 1L)))))(st_append_text(st, mov_rr(reg_rax(), reg_rcx())));

    public static CodegenState emit_list_insert_at_shift(CodegenState st, long in_place_pos) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<long, CodegenState>)((shift_loop) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<long, CodegenState>)((shift_done_pos) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => ((Func<CodegenState, CodegenState>)((st12) => patch_jcc_at(st12, shift_done_pos, st12.text_len)))(st_append_text(st11, jmp((shift_loop - (st11.text_len + 5L)))))))(st_append_text(st10, sub_ri(reg_r11(), 1L)))))(st_append_text(st9, mov_store(reg_rax(), reg_rcx(), 16L)))))(st_append_text(st8, mov_load(reg_rcx(), reg_rax(), 8L)))))(st_append_text(st7, add_rr(reg_rax(), reg_rdx())))))(st_append_text(st6, mov_rr(reg_rax(), reg_rbx())))))(st_append_text(st5, shl_ri(reg_rdx(), 3L)))))(st_append_text(st4, mov_rr(reg_rdx(), reg_r11())))))(st_append_text(st3, jcc(cc_l(), 0L)))))(st3.text_len)))(st_append_text(st2, cmp_rr(reg_r11(), reg_r12())))))(st2.text_len)))(st_append_text(st1, sub_ri(reg_r11(), 1L)))))(st_append_text(st0, mov_rr(reg_r11(), reg_r14())))))(patch_jcc_at(st, in_place_pos, st.text_len));

    public static CodegenState emit_list_insert_at_store(CodegenState st) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<CodegenState, CodegenState>)((st10) => st_append_text(st10, x86_ret())))(st_append_text(st9, pop_r(reg_rbx())))))(st_append_text(st8, pop_r(reg_r12())))))(st_append_text(st7, pop_r(reg_r13())))))(st_append_text(st6, pop_r(reg_r14())))))(st_append_text(st5, mov_rr(reg_rax(), reg_rbx())))))(st_append_text(st4, mov_store(reg_rbx(), reg_r14(), 0L)))))(st_append_text(st3, add_ri(reg_r14(), 1L)))))(st_append_text(st2, mov_store(reg_rdx(), reg_r13(), 8L)))))(st_append_text(st1, add_rr(reg_rdx(), reg_rbx())))))(st_append_text(st0, shl_ri(reg_rdx(), 3L)))))(st_append_text(st, mov_rr(reg_rdx(), reg_r12())));

    public static CodegenState emit_list_insert_at_path3_alloc(CodegenState st, long path3_pos) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<long, CodegenState>)((cap_ok_pos) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => ((Func<CodegenState, CodegenState>)((st12) => ((Func<CodegenState, CodegenState>)((st13) => ((Func<CodegenState, CodegenState>)((st14) => ((Func<CodegenState, CodegenState>)((st15) => st_append_text(st15, mov_store(reg_rax(), reg_rdx(), 0L))))(st_append_text(st14, add_ri(reg_rdx(), 1L)))))(st_append_text(st13, mov_rr(reg_rdx(), reg_r14())))))(st_append_text(st12, add_rr(reg_r10(), reg_rdx())))))(st_append_text(st11, shl_ri(reg_rdx(), 3L)))))(st_append_text(st10, add_ri(reg_rdx(), 1L)))))(st_append_text(st9, mov_rr(reg_rdx(), reg_rcx())))))(st_append_text(st8, mov_rr(reg_rax(), reg_r10())))))(st_append_text(st7, add_ri(reg_r10(), 8L)))))(st_append_text(st6, mov_store(reg_r10(), reg_rcx(), 0L)))))(patch_jcc_at(st5, cap_ok_pos, st5.text_len))))(st_append_text(st4, li(reg_rcx(), 4L)))))(st_append_text(st3, jcc(cc_ge(), 0L)))))(st3.text_len)))(st_append_text(st2, cmp_ri(reg_rcx(), 4L)))))(st_append_text(st1, shl_ri(reg_rcx(), 1L)))))(st_append_text(st0, mov_rr(reg_rcx(), reg_r14())))))(patch_jcc_at(st, path3_pos, st.text_len));

    public static CodegenState emit_list_insert_at_path3_pre(CodegenState st) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<long, CodegenState>)((pre_loop) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<long, CodegenState>)((pre_done_pos) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => ((Func<CodegenState, CodegenState>)((st12) => patch_jcc_at(st12, pre_done_pos, st12.text_len)))(st_append_text(st11, jmp((pre_loop - (st11.text_len + 5L)))))))(st_append_text(st10, add_ri(reg_r11(), 1L)))))(st_append_text(st9, mov_store(reg_rdi(), reg_rcx(), 8L)))))(st_append_text(st8, add_rr(reg_rdi(), reg_rdx())))))(st_append_text(st7, mov_rr(reg_rdi(), reg_rax())))))(st_append_text(st6, mov_load(reg_rcx(), reg_rsi(), 8L)))))(st_append_text(st5, add_rr(reg_rsi(), reg_rdx())))))(st_append_text(st4, mov_rr(reg_rsi(), reg_rbx())))))(st_append_text(st3, shl_ri(reg_rdx(), 3L)))))(st_append_text(st2, mov_rr(reg_rdx(), reg_r11())))))(st_append_text(st1, jcc(cc_ge(), 0L)))))(st1.text_len)))(st_append_text(st0, cmp_rr(reg_r11(), reg_r12())))))(st0.text_len)))(st_append_text(st, li(reg_r11(), 0L)));

    public static CodegenState emit_list_insert_at_path3_insert(CodegenState st) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => st_append_text(st3, mov_store(reg_rdi(), reg_r13(), 8L))))(st_append_text(st2, add_rr(reg_rdi(), reg_rdx())))))(st_append_text(st1, mov_rr(reg_rdi(), reg_rax())))))(st_append_text(st0, shl_ri(reg_rdx(), 3L)))))(st_append_text(st, mov_rr(reg_rdx(), reg_r12())));

    public static CodegenState emit_list_insert_at_path3_post(CodegenState st) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<long, CodegenState>)((post_loop) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<long, CodegenState>)((post_done_pos) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => ((Func<CodegenState, CodegenState>)((st12) => ((Func<CodegenState, CodegenState>)((st13) => patch_jcc_at(st13, post_done_pos, st13.text_len)))(st_append_text(st12, jmp((post_loop - (st12.text_len + 5L)))))))(st_append_text(st11, add_ri(reg_r11(), 1L)))))(st_append_text(st10, mov_store(reg_rdi(), reg_rcx(), 8L)))))(st_append_text(st9, add_rr(reg_rdi(), reg_rdx())))))(st_append_text(st8, mov_rr(reg_rdi(), reg_rax())))))(st_append_text(st7, add_ri(reg_rdx(), 8L)))))(st_append_text(st6, mov_load(reg_rcx(), reg_rsi(), 8L)))))(st_append_text(st5, add_rr(reg_rsi(), reg_rdx())))))(st_append_text(st4, mov_rr(reg_rsi(), reg_rbx())))))(st_append_text(st3, shl_ri(reg_rdx(), 3L)))))(st_append_text(st2, mov_rr(reg_rdx(), reg_r11())))))(st_append_text(st1, jcc(cc_ge(), 0L)))))(st1.text_len)))(st_append_text(st0, cmp_rr(reg_r11(), reg_r14())))))(st0.text_len)))(st_append_text(st, mov_rr(reg_r11(), reg_r12())));

    public static CodegenState emit_list_insert_at_path3_epilogue(CodegenState st) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => st_append_text(st3, x86_ret())))(st_append_text(st2, pop_r(reg_rbx())))))(st_append_text(st1, pop_r(reg_r12())))))(st_append_text(st0, pop_r(reg_r13())))))(st_append_text(st, pop_r(reg_r14())));

    public static CodegenState emit_list_insert_at(CodegenState st) => ((Func<HelpResult2, CodegenState>)((pro) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => emit_list_insert_at_path3_epilogue(st6)))(emit_list_insert_at_path3_post(st5))))(emit_list_insert_at_path3_insert(st4))))(emit_list_insert_at_path3_pre(st3))))(emit_list_insert_at_path3_alloc(st2, pro.p2))))(emit_list_insert_at_store(st1))))(emit_list_insert_at_shift(st0, pro.p1))))(emit_list_insert_at_grow(pro.cg))))(emit_list_insert_at_prologue(st));

    public static CodegenState emit_list_set_at(CodegenState st) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => st_append_text(st4, x86_ret())))(st_append_text(st3, mov_rr(reg_rax(), reg_rdi())))))(st_append_text(st2, mov_store(reg_rsi(), reg_rdx(), 8L)))))(st_append_text(st1, add_rr(reg_rsi(), reg_rdi())))))(st_append_text(st0, shl_ri(reg_rsi(), 3L)))))(record_func_offset(st, "\u0055\u0055\u0017\u0011\u0013\u000E\u0055\u0013\u000D\u000E\u0055\u000F\u000E"));

    public static CodegenState emit_text_concat_list_len_pass(CodegenState st) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<long, CodegenState>)((len_loop) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<long, CodegenState>)((len_done_pos) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => ((Func<CodegenState, CodegenState>)((st12) => ((Func<CodegenState, CodegenState>)((st13) => ((Func<CodegenState, CodegenState>)((st14) => ((Func<CodegenState, CodegenState>)((st15) => ((Func<CodegenState, CodegenState>)((st16) => ((Func<CodegenState, CodegenState>)((st17) => ((Func<CodegenState, CodegenState>)((st18) => patch_jcc_at(st18, len_done_pos, st18.text_len)))(st_append_text(st17, jmp((len_loop - (st17.text_len + 5L)))))))(st_append_text(st16, add_ri(reg_r11(), 1L)))))(st_append_text(st15, add_rr(reg_r13(), reg_rax())))))(st_append_text(st14, mov_load(reg_rax(), reg_rax(), 0L)))))(st_append_text(st13, mov_load(reg_rax(), reg_rax(), 8L)))))(st_append_text(st12, add_rr(reg_rax(), reg_rbx())))))(st_append_text(st11, shl_ri(reg_rax(), 3L)))))(st_append_text(st10, mov_rr(reg_rax(), reg_r11())))))(st_append_text(st9, jcc(cc_ge(), 0L)))))(st9.text_len)))(st_append_text(st8, cmp_rr(reg_r11(), reg_r12())))))(st8.text_len)))(st_append_text(st7, li(reg_r11(), 0L)))))(st_append_text(st6, li(reg_r13(), 0L)))))(st_append_text(st5, mov_load(reg_r12(), reg_rbx(), 0L)))))(st_append_text(st4, mov_rr(reg_rbx(), reg_rdi())))))(st_append_text(st3, push_r(reg_r14())))))(st_append_text(st2, push_r(reg_r13())))))(st_append_text(st1, push_r(reg_r12())))))(st_append_text(st0, push_r(reg_rbx())))))(record_func_offset(st, "\u0055\u0055\u000E\u000D\u0024\u000E\u0055\u0018\u0010\u0012\u0018\u000F\u000E\u0055\u0017\u0011\u0013\u000E"));

    public static CodegenState emit_text_concat_list_alloc(CodegenState st) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => st_append_text(st4, add_rr(reg_r10(), reg_rax()))))(st_append_text(st3, and_ri(reg_rax(), (0L - 8L))))))(st_append_text(st2, add_ri(reg_rax(), 15L)))))(st_append_text(st1, mov_rr(reg_rax(), reg_r13())))))(st_append_text(st0, mov_store(reg_r14(), reg_r13(), 0L)))))(st_append_text(st, mov_rr(reg_r14(), reg_r10())));

    public static HelpResult2 emit_text_concat_list_copy_outer(CodegenState st) => ((Func<CodegenState, HelpResult2>)((st0) => ((Func<CodegenState, HelpResult2>)((st1) => ((Func<long, HelpResult2>)((copy_loop) => ((Func<CodegenState, HelpResult2>)((st2) => ((Func<long, HelpResult2>)((copy_done_pos) => ((Func<CodegenState, HelpResult2>)((st3) => ((Func<CodegenState, HelpResult2>)((st4) => ((Func<CodegenState, HelpResult2>)((st5) => ((Func<CodegenState, HelpResult2>)((st6) => ((Func<CodegenState, HelpResult2>)((st7) => ((Func<CodegenState, HelpResult2>)((st8) => new HelpResult2(cg: st8, p1: copy_loop, p2: copy_done_pos)))(st_append_text(st7, mov_load(reg_rcx(), reg_rdi(), 0L)))))(st_append_text(st6, mov_load(reg_rdi(), reg_rax(), 8L)))))(st_append_text(st5, add_rr(reg_rax(), reg_rbx())))))(st_append_text(st4, shl_ri(reg_rax(), 3L)))))(st_append_text(st3, mov_rr(reg_rax(), reg_r11())))))(st_append_text(st2, jcc(cc_ge(), 0L)))))(st2.text_len)))(st_append_text(st1, cmp_rr(reg_r11(), reg_r12())))))(st1.text_len)))(st_append_text(st0, li(reg_r13(), 0L)))))(st_append_text(st, li(reg_r11(), 0L)));

    public static CodegenState emit_text_concat_list_copy_inner(CodegenState st, long copy_loop, long copy_done_pos) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<long, CodegenState>)((byte_loop) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<long, CodegenState>)((byte_done_pos) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => ((Func<CodegenState, CodegenState>)((st12) => ((Func<CodegenState, CodegenState>)((st13) => ((Func<CodegenState, CodegenState>)((st14) => ((Func<CodegenState, CodegenState>)((st15) => patch_jcc_at(st15, copy_done_pos, st15.text_len)))(st_append_text(st14, jmp((copy_loop - (st14.text_len + 5L)))))))(st_append_text(st13, add_ri(reg_r11(), 1L)))))(st_append_text(st12, add_rr(reg_r13(), reg_rcx())))))(patch_jcc_at(st11, byte_done_pos, st11.text_len))))(st_append_text(st10, jmp((byte_loop - (st10.text_len + 5L)))))))(st_append_text(st9, add_ri(reg_rsi(), 1L)))))(st_append_text(st8, mov_store_byte(reg_rdx(), reg_rax(), 8L)))))(st_append_text(st7, add_rr(reg_rdx(), reg_rsi())))))(st_append_text(st6, add_rr(reg_rdx(), reg_r13())))))(st_append_text(st5, mov_rr(reg_rdx(), reg_r14())))))(st_append_text(st4, movzx_byte(reg_rax(), reg_rax(), 8L)))))(st_append_text(st3, add_rr(reg_rax(), reg_rsi())))))(st_append_text(st2, mov_rr(reg_rax(), reg_rdi())))))(st_append_text(st1, jcc(cc_ge(), 0L)))))(st1.text_len)))(st_append_text(st0, cmp_rr(reg_rsi(), reg_rcx())))))(st0.text_len)))(st_append_text(st, li(reg_rsi(), 0L)));

    public static CodegenState emit_text_concat_list(CodegenState st) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<HelpResult2, CodegenState>)((outer) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => st_append_text(st7, x86_ret())))(st_append_text(st6, pop_r(reg_rbx())))))(st_append_text(st5, pop_r(reg_r12())))))(st_append_text(st4, pop_r(reg_r13())))))(st_append_text(st3, pop_r(reg_r14())))))(st_append_text(st2, mov_rr(reg_rax(), reg_r14())))))(emit_text_concat_list_copy_inner(outer.cg, outer.p1, outer.p2))))(emit_text_concat_list_copy_outer(st1))))(emit_text_concat_list_alloc(st0))))(emit_text_concat_list_len_pass(st));

    public static CodegenState emit_str_replace_prologue(CodegenState st) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => st_append_text(st11, mov_load(reg_rax(), reg_rbx(), 0L))))(st_append_text(st10, li(reg_rcx(), 0L)))))(st_append_text(st9, li(reg_r15(), 0L)))))(st_append_text(st8, mov_rr(reg_r14(), reg_r10())))))(st_append_text(st7, mov_rr(reg_r13(), reg_rdx())))))(st_append_text(st6, mov_rr(reg_r12(), reg_rsi())))))(st_append_text(st5, mov_rr(reg_rbx(), reg_rdi())))))(st_append_text(st4, push_r(reg_r15())))))(st_append_text(st3, push_r(reg_r14())))))(st_append_text(st2, push_r(reg_r13())))))(st_append_text(st1, push_r(reg_r12())))))(st_append_text(st0, push_r(reg_rbx())))))(record_func_offset(st, "\u0055\u0055\u0013\u000E\u0015\u0055\u0015\u000D\u001F\u0017\u000F\u0018\u000D"));

    public static HelpResult4 emit_str_replace_main_head(CodegenState st) => ((Func<long, HelpResult4>)((main_loop) => ((Func<CodegenState, HelpResult4>)((st0) => ((Func<CodegenState, HelpResult4>)((st1) => ((Func<long, HelpResult4>)((done_pos) => ((Func<CodegenState, HelpResult4>)((st2) => ((Func<CodegenState, HelpResult4>)((st3) => ((Func<CodegenState, HelpResult4>)((st4) => ((Func<long, HelpResult4>)((no_match_empty_pos) => ((Func<CodegenState, HelpResult4>)((st5) => ((Func<CodegenState, HelpResult4>)((st6) => ((Func<CodegenState, HelpResult4>)((st7) => ((Func<CodegenState, HelpResult4>)((st8) => ((Func<long, HelpResult4>)((cant_match_pos) => ((Func<CodegenState, HelpResult4>)((st9) => new HelpResult4(cg: st9, p1: done_pos, p2: main_loop, p3: no_match_empty_pos, p4: cant_match_pos)))(st_append_text(st8, jcc(cc_g(), 0L)))))(st8.text_len)))(st_append_text(st7, cmp_rr(reg_rsi(), reg_rax())))))(st_append_text(st6, add_rr(reg_rsi(), reg_rdx())))))(st_append_text(st5, mov_rr(reg_rsi(), reg_rcx())))))(st_append_text(st4, jcc(cc_e(), 0L)))))(st4.text_len)))(st_append_text(st3, test_rr(reg_rdx(), reg_rdx())))))(st_append_text(st2, mov_load(reg_rdx(), reg_r12(), 0L)))))(st_append_text(st1, jcc(cc_ge(), 0L)))))(st1.text_len)))(st_append_text(st0, cmp_rr(reg_rcx(), reg_rax())))))(st_append_text(st, mov_load(reg_rax(), reg_rbx(), 0L)))))(st.text_len);

    public static HelpResult2 emit_str_replace_cmp(CodegenState st) => ((Func<CodegenState, HelpResult2>)((st0) => ((Func<long, HelpResult2>)((cmp_loop) => ((Func<CodegenState, HelpResult2>)((st1) => ((Func<long, HelpResult2>)((match_pos) => ((Func<CodegenState, HelpResult2>)((st2) => ((Func<CodegenState, HelpResult2>)((st3) => ((Func<CodegenState, HelpResult2>)((st4) => ((Func<CodegenState, HelpResult2>)((st5) => ((Func<CodegenState, HelpResult2>)((st6) => ((Func<CodegenState, HelpResult2>)((st7) => ((Func<CodegenState, HelpResult2>)((st8) => ((Func<CodegenState, HelpResult2>)((st9) => ((Func<CodegenState, HelpResult2>)((st10) => ((Func<long, HelpResult2>)((mismatch_pos) => ((Func<CodegenState, HelpResult2>)((st11) => ((Func<CodegenState, HelpResult2>)((st12) => ((Func<CodegenState, HelpResult2>)((st13) => new HelpResult2(cg: st13, p1: match_pos, p2: mismatch_pos)))(st_append_text(st12, jmp((cmp_loop - (st12.text_len + 5L)))))))(st_append_text(st11, add_ri(reg_rsi(), 1L)))))(st_append_text(st10, jcc(cc_ne(), 0L)))))(st10.text_len)))(st_append_text(st9, cmp_rr(reg_rax(), reg_rdi())))))(st_append_text(st8, movzx_byte(reg_rdi(), reg_rdi(), 8L)))))(st_append_text(st7, add_rr(reg_rdi(), reg_rsi())))))(st_append_text(st6, mov_rr(reg_rdi(), reg_r12())))))(st_append_text(st5, movzx_byte(reg_rax(), reg_rax(), 8L)))))(st_append_text(st4, add_rr(reg_rax(), reg_rsi())))))(st_append_text(st3, add_rr(reg_rax(), reg_rcx())))))(st_append_text(st2, mov_rr(reg_rax(), reg_rbx())))))(st_append_text(st1, jcc(cc_ge(), 0L)))))(st1.text_len)))(st_append_text(st0, cmp_rr(reg_rsi(), reg_rdx())))))(st0.text_len)))(st_append_text(st, li(reg_rsi(), 0L)));

    public static CodegenState emit_str_replace_copy_new(CodegenState st, long main_loop) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<long, CodegenState>)((copy_loop) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<long, CodegenState>)((copy_done_pos) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => ((Func<CodegenState, CodegenState>)((st12) => ((Func<CodegenState, CodegenState>)((st13) => ((Func<CodegenState, CodegenState>)((st14) => ((Func<CodegenState, CodegenState>)((st15) => st_append_text(st15, jmp((main_loop - (st15.text_len + 5L))))))(st_append_text(st14, add_rr(reg_rcx(), reg_rdx())))))(st_append_text(st13, mov_load(reg_rdx(), reg_r12(), 0L)))))(patch_jcc_at(st12, copy_done_pos, st12.text_len))))(st_append_text(st11, jmp((copy_loop - (st11.text_len + 5L)))))))(st_append_text(st10, add_ri(reg_rsi(), 1L)))))(st_append_text(st9, add_ri(reg_r15(), 1L)))))(st_append_text(st8, mov_store_byte(reg_rdi(), reg_rax(), 8L)))))(st_append_text(st7, add_rr(reg_rdi(), reg_r15())))))(st_append_text(st6, mov_rr(reg_rdi(), reg_r14())))))(st_append_text(st5, movzx_byte(reg_rax(), reg_rax(), 8L)))))(st_append_text(st4, add_rr(reg_rax(), reg_rsi())))))(st_append_text(st3, mov_rr(reg_rax(), reg_r13())))))(st_append_text(st2, jcc(cc_ge(), 0L)))))(st2.text_len)))(st_append_text(st1, cmp_rr(reg_rsi(), reg_rdx())))))(st1.text_len)))(st_append_text(st0, li(reg_rsi(), 0L)))))(st_append_text(st, mov_load(reg_rdx(), reg_r13(), 0L)));

    public static CodegenState emit_str_replace_no_match(CodegenState st, long mismatch_pos, long no_match_empty_pos, long cant_match_pos, long main_loop) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<CodegenState, CodegenState>)((st10) => st_append_text(st10, jmp((main_loop - (st10.text_len + 5L))))))(st_append_text(st9, add_ri(reg_rcx(), 1L)))))(st_append_text(st8, add_ri(reg_r15(), 1L)))))(st_append_text(st7, mov_store_byte(reg_rdi(), reg_rax(), 8L)))))(st_append_text(st6, add_rr(reg_rdi(), reg_r15())))))(st_append_text(st5, mov_rr(reg_rdi(), reg_r14())))))(st_append_text(st4, movzx_byte(reg_rax(), reg_rax(), 8L)))))(st_append_text(st3, add_rr(reg_rax(), reg_rcx())))))(st_append_text(st2, mov_rr(reg_rax(), reg_rbx())))))(patch_jcc_at(st1, cant_match_pos, st1.text_len))))(patch_jcc_at(st0, no_match_empty_pos, st0.text_len))))(patch_jcc_at(st, mismatch_pos, st.text_len));

    public static CodegenState emit_str_replace_done(CodegenState st, long done_pos) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => ((Func<CodegenState, CodegenState>)((st12) => st_append_text(st12, x86_ret())))(st_append_text(st11, pop_r(reg_rbx())))))(st_append_text(st10, pop_r(reg_r12())))))(st_append_text(st9, pop_r(reg_r13())))))(st_append_text(st8, pop_r(reg_r14())))))(st_append_text(st7, pop_r(reg_r15())))))(st_append_text(st6, mov_rr(reg_rax(), reg_r14())))))(st_append_text(st5, add_rr(reg_r10(), reg_rax())))))(st_append_text(st4, lea(reg_r10(), reg_r14(), 0L)))))(st_append_text(st3, and_ri(reg_rax(), (0L - 8L))))))(st_append_text(st2, add_ri(reg_rax(), 15L)))))(st_append_text(st1, mov_rr(reg_rax(), reg_r15())))))(st_append_text(st0, mov_store(reg_r14(), reg_r15(), 0L)))))(patch_jcc_at(st, done_pos, st.text_len));

    public static CodegenState emit_str_replace(CodegenState st) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<HelpResult4, CodegenState>)((head) => ((Func<HelpResult2, CodegenState>)((cmp) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => emit_str_replace_done(st3, head.p1)))(emit_str_replace_no_match(st2, cmp.p2, head.p3, head.p4, head.p2))))(emit_str_replace_copy_new(st1, head.p2))))(patch_jcc_at(cmp.cg, cmp.p1, cmp.cg.text_len))))(emit_str_replace_cmp(head.cg))))(emit_str_replace_main_head(st0))))(emit_str_replace_prologue(st));

    public static CodegenState emit_text_split_prologue(CodegenState st) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => ((Func<CodegenState, CodegenState>)((st12) => ((Func<CodegenState, CodegenState>)((st13) => ((Func<CodegenState, CodegenState>)((st14) => ((Func<CodegenState, CodegenState>)((st15) => st_append_text(st15, mov_rr(reg_rcx(), reg_r11()))))(st_append_text(st14, li(reg_r11(), 0L)))))(st_append_text(st13, li(reg_r15(), 0L)))))(st_append_text(st12, add_rr(reg_r10(), reg_rax())))))(st_append_text(st11, shl_ri(reg_rax(), 3L)))))(st_append_text(st10, add_ri(reg_rax(), 2L)))))(st_append_text(st9, mov_rr(reg_rax(), reg_r12())))))(st_append_text(st8, mov_rr(reg_r14(), reg_r10())))))(st_append_text(st7, movzx_byte(reg_r13(), reg_rsi(), 8L)))))(st_append_text(st6, mov_load(reg_r12(), reg_rbx(), 0L)))))(st_append_text(st5, mov_rr(reg_rbx(), reg_rdi())))))(st_append_text(st4, push_r(reg_r15())))))(st_append_text(st3, push_r(reg_r14())))))(st_append_text(st2, push_r(reg_r13())))))(st_append_text(st1, push_r(reg_r12())))))(st_append_text(st0, push_r(reg_rbx())))))(record_func_offset(st, "\u0055\u0055\u000E\u000D\u0024\u000E\u0055\u0013\u001F\u0017\u0011\u000E"));

    public static HelpResult2 emit_text_split_scan_head(CodegenState st) => ((Func<long, HelpResult2>)((scan_loop) => ((Func<CodegenState, HelpResult2>)((st0) => ((Func<long, HelpResult2>)((scan_done_pos) => ((Func<CodegenState, HelpResult2>)((st1) => ((Func<CodegenState, HelpResult2>)((st2) => ((Func<CodegenState, HelpResult2>)((st3) => ((Func<CodegenState, HelpResult2>)((st4) => ((Func<CodegenState, HelpResult2>)((st5) => ((Func<long, HelpResult2>)((not_delim_pos) => ((Func<CodegenState, HelpResult2>)((st6) => new HelpResult2(cg: st6, p1: scan_done_pos, p2: not_delim_pos)))(st_append_text(st5, jcc(cc_ne(), 0L)))))(st5.text_len)))(st_append_text(st4, cmp_rr(reg_rax(), reg_r13())))))(st_append_text(st3, movzx_byte(reg_rax(), reg_rax(), 8L)))))(st_append_text(st2, add_rr(reg_rax(), reg_r11())))))(st_append_text(st1, mov_rr(reg_rax(), reg_rbx())))))(st_append_text(st0, jcc(cc_ge(), 0L)))))(st0.text_len)))(st_append_text(st, cmp_rr(reg_r11(), reg_r12())))))(st.text_len);

    public static CodegenState emit_text_split_emit_seg(CodegenState st, long scan_loop) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => st_append_text(st9, li(reg_rsi(), 0L))))(st_append_text(st8, push_r(reg_r11())))))(st_append_text(st7, pop_r(reg_rax())))))(st_append_text(st6, add_rr(reg_r10(), reg_rax())))))(st_append_text(st5, and_ri(reg_rax(), (0L - 8L))))))(st_append_text(st4, add_ri(reg_rax(), 15L)))))(st_append_text(st3, push_r(reg_rax())))))(st_append_text(st2, mov_store(reg_rdi(), reg_rax(), 0L)))))(st_append_text(st1, mov_rr(reg_rdi(), reg_r10())))))(st_append_text(st0, sub_rr(reg_rax(), reg_rcx())))))(st_append_text(st, mov_rr(reg_rax(), reg_r11())));

    public static CodegenState emit_text_split_seg_copy(CodegenState st, long scan_loop) => ((Func<long, CodegenState>)((seg_copy) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<long, CodegenState>)((seg_done_pos) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => ((Func<CodegenState, CodegenState>)((st12) => ((Func<CodegenState, CodegenState>)((st13) => ((Func<CodegenState, CodegenState>)((st14) => ((Func<CodegenState, CodegenState>)((st15) => ((Func<CodegenState, CodegenState>)((st16) => ((Func<CodegenState, CodegenState>)((st17) => ((Func<CodegenState, CodegenState>)((st18) => st_append_text(st18, mov_rr(reg_rcx(), reg_r11()))))(st_append_text(st17, add_ri(reg_r11(), 1L)))))(st_append_text(st16, add_ri(reg_r15(), 1L)))))(st_append_text(st15, mov_store(reg_rax(), reg_rdi(), 8L)))))(st_append_text(st14, add_rr(reg_rax(), reg_r14())))))(st_append_text(st13, shl_ri(reg_rax(), 3L)))))(st_append_text(st12, mov_rr(reg_rax(), reg_r15())))))(st_append_text(st11, pop_r(reg_r11())))))(patch_jcc_at(st10, seg_done_pos, st10.text_len))))(st_append_text(st9, jmp((seg_copy - (st9.text_len + 5L)))))))(st_append_text(st8, add_ri(reg_rsi(), 1L)))))(st_append_text(st7, mov_store_byte(reg_r11(), reg_rdx(), 8L)))))(st_append_text(st6, add_rr(reg_r11(), reg_rsi())))))(st_append_text(st5, mov_rr(reg_r11(), reg_rdi())))))(st_append_text(st4, movzx_byte(reg_rdx(), reg_rdx(), 8L)))))(st_append_text(st3, add_rr(reg_rdx(), reg_rsi())))))(st_append_text(st2, add_rr(reg_rdx(), reg_rcx())))))(st_append_text(st1, mov_rr(reg_rdx(), reg_rbx())))))(st_append_text(st0, jcc(cc_ge(), 0L)))))(st0.text_len)))(st_append_text(st, cmp_rr(reg_rsi(), reg_rax())))))(st.text_len);

    public static CodegenState emit_text_split_not_delim(CodegenState st, long not_delim_pos, long scan_loop) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => st_append_text(st2, jmp((scan_loop - (st2.text_len + 5L))))))(st_append_text(st1, add_ri(reg_r11(), 1L)))))(patch_jcc_at(st0, not_delim_pos, st0.text_len))))(st_append_text(st, jmp((scan_loop - (st.text_len + 5L)))));

    public static CodegenState emit_text_split_final_seg(CodegenState st, long scan_done_pos) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => st_append_text(st8, pop_r(reg_rax()))))(st_append_text(st7, add_rr(reg_r10(), reg_rax())))))(st_append_text(st6, and_ri(reg_rax(), (0L - 8L))))))(st_append_text(st5, add_ri(reg_rax(), 15L)))))(st_append_text(st4, push_r(reg_rax())))))(st_append_text(st3, mov_store(reg_rdi(), reg_rax(), 0L)))))(st_append_text(st2, mov_rr(reg_rdi(), reg_r10())))))(st_append_text(st1, sub_rr(reg_rax(), reg_rcx())))))(st_append_text(st0, mov_rr(reg_rax(), reg_r12())))))(patch_jcc_at(st, scan_done_pos, st.text_len));

    public static CodegenState emit_text_split_final_copy(CodegenState st) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<long, CodegenState>)((last_copy) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<long, CodegenState>)((last_done_pos) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => patch_jcc_at(st11, last_done_pos, st11.text_len)))(st_append_text(st10, jmp((last_copy - (st10.text_len + 5L)))))))(st_append_text(st9, add_ri(reg_rsi(), 1L)))))(st_append_text(st8, mov_store_byte(reg_r11(), reg_rdx(), 8L)))))(st_append_text(st7, add_rr(reg_r11(), reg_rsi())))))(st_append_text(st6, mov_rr(reg_r11(), reg_rdi())))))(st_append_text(st5, movzx_byte(reg_rdx(), reg_rdx(), 8L)))))(st_append_text(st4, add_rr(reg_rdx(), reg_rsi())))))(st_append_text(st3, add_rr(reg_rdx(), reg_rcx())))))(st_append_text(st2, mov_rr(reg_rdx(), reg_rbx())))))(st_append_text(st1, jcc(cc_ge(), 0L)))))(st1.text_len)))(st_append_text(st0, cmp_rr(reg_rsi(), reg_rax())))))(st0.text_len)))(st_append_text(st, li(reg_rsi(), 0L)));

    public static CodegenState emit_text_split_epilogue(CodegenState st) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => st_append_text(st11, x86_ret())))(st_append_text(st10, pop_r(reg_rbx())))))(st_append_text(st9, pop_r(reg_r12())))))(st_append_text(st8, pop_r(reg_r13())))))(st_append_text(st7, pop_r(reg_r14())))))(st_append_text(st6, pop_r(reg_r15())))))(st_append_text(st5, mov_rr(reg_rax(), reg_r14())))))(st_append_text(st4, mov_store(reg_r14(), reg_r15(), 0L)))))(st_append_text(st3, add_ri(reg_r15(), 1L)))))(st_append_text(st2, mov_store(reg_rax(), reg_rdi(), 8L)))))(st_append_text(st1, add_rr(reg_rax(), reg_r14())))))(st_append_text(st0, shl_ri(reg_rax(), 3L)))))(st_append_text(st, mov_rr(reg_rax(), reg_r15())));

    public static CodegenState emit_text_split(CodegenState st) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<HelpResult2, CodegenState>)((head) => ((Func<long, CodegenState>)((scan_loop) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => emit_text_split_epilogue(st5)))(emit_text_split_final_copy(st4))))(emit_text_split_final_seg(st3, head.p1))))(emit_text_split_not_delim(st2, head.p2, scan_loop))))(emit_text_split_seg_copy(st1, scan_loop))))(emit_text_split_emit_seg(head.cg, scan_loop))))(st0.text_len)))(emit_text_split_scan_head(st0))))(emit_text_split_prologue(st));

    public static CodegenState emit_cce_to_unicode_helper(CodegenState st) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => st_append_text(st4, x86_ret())))(st_append_text(st3, movzx_byte(reg_rax(), reg_rax(), 0L)))))(st_append_text(st2, add_rr(reg_rax(), reg_rdi())))))(emit_load_rodata_addr(st1, reg_rax(), cce_to_unicode_rodata_offset()))))(record_func_offset(st, "\u0055\u0055\u0018\u0018\u000D\u0055\u000E\u0010\u0055\u0019\u0012\u0011\u0018\u0010\u0016\u000D"));

    public static CodegenState emit_unicode_to_cce_helper(CodegenState st) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => st_append_text(st4, x86_ret())))(st_append_text(st3, movzx_byte(reg_rax(), reg_rax(), 0L)))))(st_append_text(st2, add_rr(reg_rax(), reg_rdi())))))(emit_load_rodata_addr(st1, reg_rax(), unicode_to_cce_rodata_offset()))))(record_func_offset(st, "\u0055\u0055\u0019\u0012\u0011\u0018\u0010\u0016\u000D\u0055\u000E\u0010\u0055\u0018\u0018\u000D"));

    public static CodegenState emit_read_line_helper(CodegenState st) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<long, CodegenState>)((read_byte) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<long, CodegenState>)((wait_loop) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => ((Func<CodegenState, CodegenState>)((st12) => ((Func<long, CodegenState>)((has_data) => ((Func<CodegenState, CodegenState>)((st13) => ((Func<CodegenState, CodegenState>)((st14) => ((Func<CodegenState, CodegenState>)((st15) => ((Func<CodegenState, CodegenState>)((st16) => ((Func<CodegenState, CodegenState>)((st17) => ((Func<CodegenState, CodegenState>)((st18) => ((Func<CodegenState, CodegenState>)((st19) => ((Func<CodegenState, CodegenState>)((st20) => ((Func<CodegenState, CodegenState>)((st21) => ((Func<CodegenState, CodegenState>)((st22) => ((Func<CodegenState, CodegenState>)((st23) => ((Func<CodegenState, CodegenState>)((st24) => ((Func<long, CodegenState>)((skip_cr) => ((Func<CodegenState, CodegenState>)((st25) => ((Func<CodegenState, CodegenState>)((st26) => ((Func<CodegenState, CodegenState>)((st27) => ((Func<CodegenState, CodegenState>)((st28) => ((Func<CodegenState, CodegenState>)((st29) => ((Func<CodegenState, CodegenState>)((st30) => ((Func<CodegenState, CodegenState>)((st31) => ((Func<CodegenState, CodegenState>)((st32) => ((Func<CodegenState, CodegenState>)((st33) => ((Func<CodegenState, CodegenState>)((st34) => ((Func<CodegenState, CodegenState>)((st35) => ((Func<long, CodegenState>)((eof) => ((Func<CodegenState, CodegenState>)((st36) => ((Func<CodegenState, CodegenState>)((st37) => ((Func<CodegenState, CodegenState>)((st38) => ((Func<CodegenState, CodegenState>)((st39) => ((Func<CodegenState, CodegenState>)((st40) => ((Func<long, CodegenState>)((got_nl) => ((Func<CodegenState, CodegenState>)((st41) => ((Func<CodegenState, CodegenState>)((st42) => ((Func<CodegenState, CodegenState>)((st43) => ((Func<CodegenState, CodegenState>)((st44) => ((Func<CodegenState, CodegenState>)((st45) => ((Func<CodegenState, CodegenState>)((st46) => ((Func<CodegenState, CodegenState>)((st47) => ((Func<CodegenState, CodegenState>)((st48) => ((Func<CodegenState, CodegenState>)((st49) => ((Func<CodegenState, CodegenState>)((st50) => ((Func<CodegenState, CodegenState>)((st51) => ((Func<CodegenState, CodegenState>)((st52) => ((Func<CodegenState, CodegenState>)((st53) => ((Func<CodegenState, CodegenState>)((st54) => ((Func<CodegenState, CodegenState>)((st55) => ((Func<CodegenState, CodegenState>)((st56) => ((Func<CodegenState, CodegenState>)((st57) => ((Func<CodegenState, CodegenState>)((st58) => ((Func<CodegenState, CodegenState>)((st59) => ((Func<CodegenState, CodegenState>)((st60) => st_append_text(st60, x86_ret())))(st_append_text(st59, pop_r(reg_rbx())))))(st_append_text(st58, pop_r(reg_r12())))))(st_append_text(st57, mov_rr(reg_rax(), reg_rbx())))))(st_append_text(st56, add_rr(reg_r10(), reg_rax())))))(st_append_text(st55, and_ri(reg_rax(), (0L - 8L))))))(st_append_text(st54, add_ri(reg_rax(), 15L)))))(st_append_text(st53, mov_rr(reg_rax(), reg_rcx())))))(st_append_text(st52, mov_store(reg_rbx(), reg_rcx(), 0L)))))(patch_jcc_at(st51, got_nl, st51.text_len))))(patch_jcc_at(st50, eof, st50.text_len))))(st_append_text(st49, jmp((read_byte - (st49.text_len + 5L)))))))(st_append_text(st48, add_ri(reg_rcx(), 1L)))))(st_append_text(st47, new List<long> { 136L, 6L }))))(st_append_text(st46, movzx_byte(reg_rax(), reg_rax(), 0L)))))(st_append_text(st45, add_rr(reg_rax(), reg_r12())))))(st_append_text(st44, movzx_byte(reg_rax(), reg_rsi(), 0L)))))(st_append_text(st43, add_ri(reg_rsi(), 8L)))))(st_append_text(st42, add_rr(reg_rsi(), reg_rcx())))))(st_append_text(st41, mov_rr(reg_rsi(), reg_rbx())))))(st_append_text(st40, jcc(cc_e(), 0L)))))(st40.text_len)))(st_append_text(st39, cmp_ri(reg_rdx(), 10L)))))(st_append_text(st38, movzx_byte(reg_rdx(), reg_rdx(), 8L)))))(st_append_text(st37, add_rr(reg_rdx(), reg_rcx())))))(st_append_text(st36, mov_rr(reg_rdx(), reg_rbx())))))(st_append_text(st35, jcc(cc_le(), 0L)))))(st35.text_len)))(st_append_text(st34, test_rr(reg_rax(), reg_rax())))))(st_append_text(st33, pop_r(reg_rcx())))))(patch_jcc_at(st32, skip_cr, wait_loop))))(st_append_text(st31, li(reg_rax(), 1L)))))(st_append_text(st30, new List<long> { 136L, 6L }))))(st_append_text(st29, add_ri(reg_rsi(), 8L)))))(st_append_text(st28, add_rr(reg_rsi(), reg_rcx())))))(st_append_text(st27, mov_rr(reg_rsi(), reg_rbx())))))(st_append_text(st26, push_r(reg_rcx())))))(st_append_text(st25, pop_r(reg_rcx())))))(st_append_text(st24, jcc(cc_e(), 0L)))))(st24.text_len)))(st_append_text(st23, cmp_ri(reg_rax(), 13L)))))(st_append_text(st22, mov_store(reg_rdi(), reg_r11(), 0L)))))(st_append_text(st21, li(reg_rdi(), serial_read_pos_addr())))))(st_append_text(st20, add_ri(reg_r11(), 1L)))))(st_append_text(st19, movzx_byte(reg_rax(), reg_rax(), 0L)))))(st_append_text(st18, add_ri(reg_rax(), serial_ring_buf_addr())))))(st_append_text(st17, and_ri(reg_rax(), serial_ring_buf_mask())))))(st_append_text(st16, mov_rr(reg_rax(), reg_r11())))))(patch_jcc_at(st15, has_data, st15.text_len))))(st_append_text(st14, jmp((wait_loop - (st14.text_len + 5L)))))))(st_append_text(st13, hlt()))))(st_append_text(st12, jcc(cc_ne(), 0L)))))(st12.text_len)))(st_append_text(st11, cmp_rr(reg_rsi(), reg_r11())))))(st_append_text(st10, mov_load(reg_r11(), reg_rdi(), 0L)))))(st_append_text(st9, li(reg_rdi(), serial_read_pos_addr())))))(st_append_text(st8, mov_load(reg_rsi(), reg_rdi(), 0L)))))(st_append_text(st7, li(reg_rdi(), serial_write_pos_addr())))))(st7.text_len)))(st_append_text(st6, push_r(reg_rcx())))))(st6.text_len)))(st_append_text(st5, li(reg_rcx(), 0L)))))(st_append_text(st4, mov_rr(reg_rbx(), reg_r10())))))(emit_load_rodata_addr(st3, reg_r12(), unicode_to_cce_rodata_offset()))))(st_append_text(st2, push_r(reg_r12())))))(st_append_text(st1, push_r(reg_rbx())))))(record_func_offset(st, "\u0055\u0055\u0015\u000D\u000F\u0016\u0055\u0017\u0011\u0012\u000D"));

    public static CodegenState emit_read_file_helper(CodegenState st) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => st_append_text(st2, x86_ret())))(emit_call_to(st1, "\u0055\u0055\u0020\u000F\u0015\u000D\u0055\u001A\u000D\u000E\u000F\u0017\u0055\u0015\u000D\u000F\u0016\u0055\u0013\u000D\u0015\u0011\u000F\u0017"))))(record_func_offset(st, "\u0055\u0055\u0015\u000D\u000F\u0016\u0055\u001C\u0011\u0017\u000D"));

    public static CodegenState emit_bare_metal_read_serial(CodegenState st) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st3a) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<long, CodegenState>)((read_loop) => ((Func<long, CodegenState>)((wait_loop) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => ((Func<long, CodegenState>)((has_data) => ((Func<CodegenState, CodegenState>)((st12) => ((Func<CodegenState, CodegenState>)((st13) => ((Func<CodegenState, CodegenState>)((st14) => ((Func<CodegenState, CodegenState>)((st15) => ((Func<CodegenState, CodegenState>)((st16) => ((Func<CodegenState, CodegenState>)((st17) => ((Func<CodegenState, CodegenState>)((st18) => ((Func<CodegenState, CodegenState>)((st19) => ((Func<CodegenState, CodegenState>)((st20) => ((Func<CodegenState, CodegenState>)((st21) => ((Func<CodegenState, CodegenState>)((st22) => ((Func<CodegenState, CodegenState>)((st23) => ((Func<long, CodegenState>)((got_eot) => ((Func<CodegenState, CodegenState>)((st24) => ((Func<CodegenState, CodegenState>)((st25) => ((Func<long, CodegenState>)((got_null) => ((Func<CodegenState, CodegenState>)((st26) => ((Func<CodegenState, CodegenState>)((st27) => ((Func<long, CodegenState>)((skip_cr) => ((Func<CodegenState, CodegenState>)((st28) => ((Func<CodegenState, CodegenState>)((st29) => ((Func<CodegenState, CodegenState>)((st30) => ((Func<CodegenState, CodegenState>)((st31) => ((Func<CodegenState, CodegenState>)((st32) => ((Func<CodegenState, CodegenState>)((st33) => ((Func<CodegenState, CodegenState>)((st34) => ((Func<CodegenState, CodegenState>)((st35) => ((Func<CodegenState, CodegenState>)((st36) => ((Func<CodegenState, CodegenState>)((st37) => ((Func<CodegenState, CodegenState>)((st38) => ((Func<CodegenState, CodegenState>)((st39) => ((Func<CodegenState, CodegenState>)((st40) => ((Func<CodegenState, CodegenState>)((st41) => ((Func<CodegenState, CodegenState>)((st42) => ((Func<CodegenState, CodegenState>)((st43) => ((Func<CodegenState, CodegenState>)((st44) => ((Func<CodegenState, CodegenState>)((st45) => ((Func<CodegenState, CodegenState>)((st46) => ((Func<CodegenState, CodegenState>)((st47) => ((Func<CodegenState, CodegenState>)((st48) => ((Func<CodegenState, CodegenState>)((st49) => st_append_text(st49, x86_ret())))(st_append_text(st48, pop_r(reg_rbx())))))(st_append_text(st47, pop_r(reg_rcx())))))(st_append_text(st46, pop_r(reg_r12())))))(st_append_text(st45, mov_rr(reg_rax(), reg_rbx())))))(st_append_text(st44, add_rr(reg_r10(), reg_rax())))))(st_append_text(st43, add_ri(reg_rax(), 8L)))))(st_append_text(st42, and_ri(reg_rax(), (0L - 8L))))))(st_append_text(st41, add_ri(reg_rax(), 15L)))))(st_append_text(st40, mov_rr(reg_rax(), reg_rcx())))))(st_append_text(st39, mov_store(reg_rbx(), reg_rcx(), 0L)))))(patch_jcc_at(st38, got_null, st38.text_len))))(patch_jcc_at(st37, got_eot, st37.text_len))))(st_append_text(st36, jmp((read_loop - (st36.text_len + 5L)))))))(patch_jcc_at(st35, skip_cr, st35.text_len))))(st_append_text(st34, add_ri(reg_rcx(), 1L)))))(st_append_text(st33, new List<long> { 136L, 6L }))))(st_append_text(st32, add_ri(reg_rsi(), 8L)))))(st_append_text(st31, add_rr(reg_rsi(), reg_rcx())))))(st_append_text(st30, mov_rr(reg_rsi(), reg_rbx())))))(st_append_text(st29, movzx_byte(reg_rax(), reg_rax(), 0L)))))(st_append_text(st28, add_rr(reg_rax(), reg_r12())))))(st_append_text(st27, jcc(cc_e(), 0L)))))(st27.text_len)))(st_append_text(st26, cmp_ri(reg_rax(), 13L)))))(st_append_text(st25, jcc(cc_e(), 0L)))))(st25.text_len)))(st_append_text(st24, test_rr(reg_rax(), reg_rax())))))(st_append_text(st23, jcc(cc_e(), 0L)))))(st23.text_len)))(st_append_text(st22, cmp_ri(reg_rax(), 4L)))))(st_append_text(st21, mov_store(reg_rdi(), reg_r11(), 0L)))))(st_append_text(st20, li(reg_rdi(), serial_read_pos_addr())))))(st_append_text(st19, add_ri(reg_r11(), 1L)))))(st_append_text(st18, movzx_byte(reg_rax(), reg_rax(), 0L)))))(st_append_text(st17, add_ri(reg_rax(), serial_ring_buf_addr())))))(st_append_text(st16, and_ri(reg_rax(), serial_ring_buf_mask())))))(st_append_text(st15, mov_rr(reg_rax(), reg_r11())))))(patch_jcc_at(st14, has_data, st14.text_len))))(st_append_text(st13, jmp((wait_loop - (st13.text_len + 5L)))))))(st_append_text(st12, hlt()))))(st_append_text(st11, jcc(cc_ne(), 0L)))))(st11.text_len)))(st_append_text(st10, cmp_rr(reg_rsi(), reg_r11())))))(st_append_text(st9, mov_load(reg_r11(), reg_rdi(), 0L)))))(st_append_text(st8, li(reg_rdi(), serial_read_pos_addr())))))(st_append_text(st7, mov_load(reg_rsi(), reg_rdi(), 0L)))))(st_append_text(st6, li(reg_rdi(), serial_write_pos_addr())))))(st6.text_len)))(st6.text_len)))(st_append_text(st5, li(reg_rcx(), 0L)))))(st_append_text(st4, mov_rr(reg_rbx(), reg_r10())))))(emit_load_rodata_addr(st3a, reg_r12(), unicode_to_cce_rodata_offset()))))(st_append_text(st3, push_r(reg_r12())))))(st_append_text(st2, push_r(reg_rcx())))))(st_append_text(st1, push_r(reg_rbx())))))(record_func_offset(st, "\u0055\u0055\u0020\u000F\u0015\u000D\u0055\u001A\u000D\u000E\u000F\u0017\u0055\u0015\u000D\u000F\u0016\u0055\u0013\u000D\u0015\u0011\u000F\u0017"));

    public static CodegenState emit_runtime_helpers(CodegenState st) => ((Func<CodegenState, CodegenState>)((st0) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => ((Func<CodegenState, CodegenState>)((st12) => ((Func<CodegenState, CodegenState>)((st12b) => ((Func<CodegenState, CodegenState>)((st13) => ((Func<CodegenState, CodegenState>)((st14) => ((Func<CodegenState, CodegenState>)((st15) => ((Func<CodegenState, CodegenState>)((st16) => ((Func<CodegenState, CodegenState>)((st17) => ((Func<CodegenState, CodegenState>)((st18) => ((Func<CodegenState, CodegenState>)((st19) => ((Func<CodegenState, CodegenState>)((st20) => ((Func<CodegenState, CodegenState>)((st21) => ((Func<CodegenState, CodegenState>)((st22) => ((Func<CodegenState, CodegenState>)((st23) => emit_list_concat_many_helper(st23)))(emit_buf_read_bytes_helper(st22))))(emit_buf_write_bytes_helper(st21))))(emit_write_binary_helper(st20))))(emit_bare_metal_read_serial(st19))))(emit_read_file_helper(st18))))(emit_read_line_helper(st17))))(emit_unicode_to_cce_helper(st16))))(emit_cce_to_unicode_helper(st15))))(emit_text_split(st14))))(emit_text_concat_list(st13))))(emit_list_contains(st12b))))(emit_list_set_at(st12))))(emit_list_insert_at(st11))))(emit_list_append(st10))))(emit_list_cons(st9))))(emit_list_snoc(st8))))(emit_str_replace(st7))))(emit_text_compare(st6))))(emit_text_contains(st5))))(emit_text_starts_with(st4))))(emit_text_to_int(st3))))(emit_ipow(st2))))(emit_itoa(st1))))(emit_str_eq(st0))))(emit_str_concat(st));

    public static CodegenState emit_buf_write_bytes_helper(CodegenState st) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<long, CodegenState>)((skip_pos) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<long, CodegenState>)((loop_top) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => ((Func<CodegenState, CodegenState>)((st12) => ((Func<CodegenState, CodegenState>)((st13) => ((Func<CodegenState, CodegenState>)((st14) => ((Func<CodegenState, CodegenState>)((st15) => ((Func<CodegenState, CodegenState>)((st16) => ((Func<CodegenState, CodegenState>)((st17) => ((Func<long, CodegenState>)((back_pos) => ((Func<CodegenState, CodegenState>)((st18) => ((Func<CodegenState, CodegenState>)((st19) => ((Func<CodegenState, CodegenState>)((st20) => ((Func<CodegenState, CodegenState>)((st21) => ((Func<CodegenState, CodegenState>)((st22) => st_append_text(st22, Enumerable.Concat(pop_r(reg_r13()), Enumerable.Concat(pop_r(reg_r12()), Enumerable.Concat(pop_r(reg_rbx()), x86_ret()).ToList()).ToList()).ToList())))(st_append_text(st21, mov_rr(reg_rax(), reg_rsi())))))(st_append_text(st20, add_rr(reg_rsi(), reg_r13())))))(patch_jcc_at(st19, skip_pos, st19.text_len))))(patch_jcc_at(st18, back_pos, loop_top))))(st_append_text(st17, jcc(cc_l(), 0L)))))(st17.text_len)))(st_append_text(st16, cmp_rr(reg_rcx(), reg_r13())))))(st_append_text(st15, add_ri(reg_rcx(), 1L)))))(st_append_text(st14, add_ri(reg_rbx(), 1L)))))(st_append_text(st13, mov_store_byte(reg_rbx(), reg_rax(), 0L)))))(st_append_text(st12, mov_load(reg_rax(), reg_rax(), 8L)))))(st_append_text(st11, add_rr(reg_rax(), reg_r12())))))(st_append_text(st10, shl_ri(reg_rax(), 3L)))))(st_append_text(st9, mov_rr(reg_rax(), reg_rcx())))))(st9.text_len)))(st_append_text(st8, jcc(cc_ge(), 0L)))))(st8.text_len)))(st_append_text(st7, cmp_rr(reg_rcx(), reg_r13())))))(st_append_text(st6, li(reg_rcx(), 0L)))))(st_append_text(st5, mov_load(reg_r13(), reg_r12(), 0L)))))(st_append_text(st4, mov_rr(reg_r12(), reg_rdx())))))(st_append_text(st3, add_rr(reg_rbx(), reg_rsi())))))(st_append_text(st2, mov_rr(reg_rbx(), reg_rdi())))))(st_append_text(st1, Enumerable.Concat(push_r(reg_rbx()), Enumerable.Concat(push_r(reg_r12()), push_r(reg_r13())).ToList()).ToList()))))(record_func_offset(st, "\u0055\u0055\u0020\u0019\u001C\u0055\u001B\u0015\u0011\u000E\u000D\u0055\u0020\u001E\u000E\u000D\u0013"));

    public static CodegenState emit_buf_read_bytes_helper(CodegenState st) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => ((Func<CodegenState, CodegenState>)((st12) => ((Func<CodegenState, CodegenState>)((st13) => ((Func<CodegenState, CodegenState>)((st14) => ((Func<CodegenState, CodegenState>)((st15) => ((Func<long, CodegenState>)((skip_pos) => ((Func<CodegenState, CodegenState>)((st16) => ((Func<long, CodegenState>)((loop_top) => ((Func<CodegenState, CodegenState>)((st17) => ((Func<CodegenState, CodegenState>)((st18) => ((Func<CodegenState, CodegenState>)((st19) => ((Func<CodegenState, CodegenState>)((st20) => ((Func<CodegenState, CodegenState>)((st21) => ((Func<CodegenState, CodegenState>)((st22) => ((Func<CodegenState, CodegenState>)((st23) => ((Func<CodegenState, CodegenState>)((st24) => ((Func<CodegenState, CodegenState>)((st25) => ((Func<long, CodegenState>)((back_pos) => ((Func<CodegenState, CodegenState>)((st26) => ((Func<CodegenState, CodegenState>)((st27) => ((Func<CodegenState, CodegenState>)((st28) => ((Func<CodegenState, CodegenState>)((st29) => st_append_text(st29, Enumerable.Concat(pop_r(reg_r14()), Enumerable.Concat(pop_r(reg_r13()), Enumerable.Concat(pop_r(reg_r12()), Enumerable.Concat(pop_r(reg_rbx()), x86_ret()).ToList()).ToList()).ToList()).ToList())))(st_append_text(st28, mov_rr(reg_rax(), reg_rbx())))))(patch_jcc_at(st27, skip_pos, st27.text_len))))(patch_jcc_at(st26, back_pos, loop_top))))(st_append_text(st25, jcc(cc_l(), 0L)))))(st25.text_len)))(st_append_text(st24, cmp_rr(reg_rcx(), reg_r13())))))(st_append_text(st23, add_ri(reg_rcx(), 1L)))))(st_append_text(st22, mov_store(reg_rax(), reg_r14(), 8L)))))(st_append_text(st21, add_rr(reg_rax(), reg_rbx())))))(st_append_text(st20, shl_ri(reg_rax(), 3L)))))(st_append_text(st19, mov_rr(reg_rax(), reg_rcx())))))(st_append_text(st18, movzx_byte(reg_r14(), reg_r14(), 0L)))))(st_append_text(st17, add_rr(reg_r14(), reg_rcx())))))(st_append_text(st16, mov_rr(reg_r14(), reg_r12())))))(st16.text_len)))(st_append_text(st15, jcc(cc_ge(), 0L)))))(st15.text_len)))(st_append_text(st14, cmp_rr(reg_rcx(), reg_r13())))))(st_append_text(st13, li(reg_rcx(), 0L)))))(st_append_text(st12, add_rr(reg_r10(), reg_rax())))))(st_append_text(st11, shl_ri(reg_rax(), 3L)))))(st_append_text(st10, add_ri(reg_rax(), 1L)))))(st_append_text(st9, mov_rr(reg_rax(), reg_r13())))))(st_append_text(st8, mov_store(reg_r10(), reg_r13(), 0L)))))(st_append_text(st7, mov_rr(reg_rbx(), reg_r10())))))(st_append_text(st6, add_ri(reg_r10(), 8L)))))(st_append_text(st5, mov_store(reg_r10(), reg_r13(), 0L)))))(st_append_text(st4, mov_rr(reg_r13(), reg_rdx())))))(st_append_text(st3, add_rr(reg_r12(), reg_rsi())))))(st_append_text(st2, mov_rr(reg_r12(), reg_rdi())))))(st_append_text(st1, Enumerable.Concat(push_r(reg_rbx()), Enumerable.Concat(push_r(reg_r12()), Enumerable.Concat(push_r(reg_r13()), push_r(reg_r14())).ToList()).ToList()).ToList()))))(record_func_offset(st, "\u0055\u0055\u0020\u0019\u001C\u0055\u0015\u000D\u000F\u0016\u0055\u0020\u001E\u000E\u000D\u0013"));

    public static CodegenState emit_list_concat_many_helper(CodegenState st) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<long, CodegenState>)((skip_sum) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<long, CodegenState>)((sum_top) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => ((Func<CodegenState, CodegenState>)((st12) => ((Func<CodegenState, CodegenState>)((st13) => ((Func<CodegenState, CodegenState>)((st14) => ((Func<CodegenState, CodegenState>)((st15) => ((Func<CodegenState, CodegenState>)((st16) => ((Func<long, CodegenState>)((sum_back) => ((Func<CodegenState, CodegenState>)((st17) => ((Func<CodegenState, CodegenState>)((st18) => ((Func<CodegenState, CodegenState>)((st19) => ((Func<CodegenState, CodegenState>)((st20) => ((Func<CodegenState, CodegenState>)((st21) => ((Func<CodegenState, CodegenState>)((st22) => ((Func<CodegenState, CodegenState>)((st23) => ((Func<CodegenState, CodegenState>)((st24) => ((Func<CodegenState, CodegenState>)((st25) => ((Func<CodegenState, CodegenState>)((st26) => ((Func<CodegenState, CodegenState>)((st27) => ((Func<CodegenState, CodegenState>)((st28) => ((Func<CodegenState, CodegenState>)((st29) => ((Func<CodegenState, CodegenState>)((st30) => ((Func<long, CodegenState>)((skip_copy) => ((Func<CodegenState, CodegenState>)((st31) => ((Func<long, CodegenState>)((outer_top) => ((Func<CodegenState, CodegenState>)((st32) => ((Func<CodegenState, CodegenState>)((st33) => ((Func<CodegenState, CodegenState>)((st34) => ((Func<CodegenState, CodegenState>)((st35) => ((Func<CodegenState, CodegenState>)((st36) => ((Func<CodegenState, CodegenState>)((st37) => ((Func<CodegenState, CodegenState>)((st38) => ((Func<long, CodegenState>)((skip_inner) => ((Func<CodegenState, CodegenState>)((st39) => ((Func<long, CodegenState>)((inner_top) => ((Func<CodegenState, CodegenState>)((st40) => ((Func<CodegenState, CodegenState>)((st41) => ((Func<CodegenState, CodegenState>)((st42) => ((Func<CodegenState, CodegenState>)((st43) => ((Func<CodegenState, CodegenState>)((st44) => ((Func<CodegenState, CodegenState>)((st45) => ((Func<CodegenState, CodegenState>)((st46) => ((Func<CodegenState, CodegenState>)((st47) => ((Func<CodegenState, CodegenState>)((st48) => ((Func<CodegenState, CodegenState>)((st49) => ((Func<CodegenState, CodegenState>)((st50) => ((Func<long, CodegenState>)((inner_back) => ((Func<CodegenState, CodegenState>)((st51) => ((Func<CodegenState, CodegenState>)((st52) => ((Func<CodegenState, CodegenState>)((st53) => ((Func<CodegenState, CodegenState>)((st54) => ((Func<CodegenState, CodegenState>)((st55) => ((Func<long, CodegenState>)((outer_back) => ((Func<CodegenState, CodegenState>)((st56) => ((Func<CodegenState, CodegenState>)((st57) => ((Func<CodegenState, CodegenState>)((st58) => ((Func<CodegenState, CodegenState>)((st59) => st_append_text(st59, Enumerable.Concat(pop_r(reg_r15()), Enumerable.Concat(pop_r(reg_r14()), Enumerable.Concat(pop_r(reg_r13()), Enumerable.Concat(pop_r(reg_r12()), Enumerable.Concat(pop_r(reg_rbx()), x86_ret()).ToList()).ToList()).ToList()).ToList()).ToList())))(st_append_text(st58, mov_rr(reg_rax(), reg_rbx())))))(patch_jcc_at(st57, skip_copy, st57.text_len))))(patch_jcc_at(st56, outer_back, outer_top))))(st_append_text(st55, jcc(cc_l(), 0L)))))(st55.text_len)))(st_append_text(st54, cmp_rr(reg_rcx(), reg_r13())))))(st_append_text(st53, add_ri(reg_rcx(), 1L)))))(patch_jcc_at(st52, skip_inner, st52.text_len))))(patch_jcc_at(st51, inner_back, inner_top))))(st_append_text(st50, jcc(cc_l(), 0L)))))(st50.text_len)))(st_append_text(st49, cmp_rr(reg_rdx(), reg_rsi())))))(st_append_text(st48, add_ri(reg_rdx(), 1L)))))(st_append_text(st47, add_ri(reg_r15(), 1L)))))(st_append_text(st46, mov_store(reg_r14(), reg_rax(), 8L)))))(st_append_text(st45, add_rr(reg_r14(), reg_rbx())))))(st_append_text(st44, shl_ri(reg_r14(), 3L)))))(st_append_text(st43, mov_rr(reg_r14(), reg_r15())))))(st_append_text(st42, mov_load(reg_rax(), reg_rax(), 8L)))))(st_append_text(st41, add_rr(reg_rax(), reg_rdi())))))(st_append_text(st40, shl_ri(reg_rax(), 3L)))))(st_append_text(st39, mov_rr(reg_rax(), reg_rdx())))))(st39.text_len)))(st_append_text(st38, jcc(cc_ge(), 0L)))))(st38.text_len)))(st_append_text(st37, cmp_rr(reg_rdx(), reg_rsi())))))(st_append_text(st36, li(reg_rdx(), 0L)))))(st_append_text(st35, mov_load(reg_rsi(), reg_rdi(), 0L)))))(st_append_text(st34, mov_load(reg_rdi(), reg_rax(), 0L)))))(st_append_text(st33, add_rr(reg_rax(), reg_r12())))))(st_append_text(st32, shl_ri(reg_rax(), 3L)))))(st_append_text(st31, mov_rr(reg_rax(), reg_rcx())))))(st31.text_len)))(st_append_text(st30, jcc(cc_ge(), 0L)))))(st30.text_len)))(st_append_text(st29, cmp_rr(reg_rcx(), reg_r13())))))(st_append_text(st28, li(reg_rcx(), 0L)))))(st_append_text(st27, li(reg_r15(), 0L)))))(st_append_text(st26, add_rr(reg_r10(), reg_rax())))))(st_append_text(st25, shl_ri(reg_rax(), 3L)))))(st_append_text(st24, add_ri(reg_rax(), 1L)))))(st_append_text(st23, mov_rr(reg_rax(), reg_r14())))))(st_append_text(st22, mov_store(reg_r10(), reg_r14(), 0L)))))(st_append_text(st21, mov_rr(reg_rbx(), reg_r10())))))(st_append_text(st20, add_ri(reg_r10(), 8L)))))(st_append_text(st19, mov_store(reg_r10(), reg_r14(), 0L)))))(patch_jcc_at(st18, skip_sum, st18.text_len))))(patch_jcc_at(st17, sum_back, sum_top))))(st_append_text(st16, jcc(cc_l(), 0L)))))(st16.text_len)))(st_append_text(st15, cmp_rr(reg_rcx(), reg_r13())))))(st_append_text(st14, add_ri(reg_rcx(), 1L)))))(st_append_text(st13, add_rr(reg_r14(), reg_rax())))))(st_append_text(st12, mov_load(reg_rax(), reg_rax(), 0L)))))(st_append_text(st11, mov_load(reg_rax(), reg_rax(), 0L)))))(st_append_text(st10, add_rr(reg_rax(), reg_r12())))))(st_append_text(st9, shl_ri(reg_rax(), 3L)))))(st_append_text(st8, mov_rr(reg_rax(), reg_rcx())))))(st8.text_len)))(st_append_text(st7, jcc(cc_ge(), 0L)))))(st7.text_len)))(st_append_text(st6, cmp_rr(reg_rcx(), reg_r13())))))(st_append_text(st5, li(reg_rcx(), 0L)))))(st_append_text(st4, li(reg_r14(), 0L)))))(st_append_text(st3, mov_rr(reg_r13(), reg_rsi())))))(st_append_text(st2, mov_rr(reg_r12(), reg_rdi())))))(st_append_text(st1, Enumerable.Concat(push_r(reg_rbx()), Enumerable.Concat(push_r(reg_r12()), Enumerable.Concat(push_r(reg_r13()), Enumerable.Concat(push_r(reg_r14()), push_r(reg_r15())).ToList()).ToList()).ToList()).ToList()))))(record_func_offset(st, "\u0055\u0055\u0017\u0011\u0013\u000E\u0055\u0018\u0010\u0012\u0018\u000F\u000E\u0055\u001A\u000F\u0012\u001E"));

    public static CodegenState emit_write_binary_helper(CodegenState st) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<long, CodegenState>)((loop_top) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<long, CodegenState>)((done_pos) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<CodegenState, CodegenState>)((st11) => ((Func<CodegenState, CodegenState>)((st12) => ((Func<CodegenState, CodegenState>)((st13) => ((Func<CodegenState, CodegenState>)((st14) => ((Func<long, CodegenState>)((wait_top) => ((Func<CodegenState, CodegenState>)((st15) => ((Func<CodegenState, CodegenState>)((st16) => ((Func<CodegenState, CodegenState>)((st17) => ((Func<long, CodegenState>)((thr_ready) => ((Func<CodegenState, CodegenState>)((st18) => ((Func<CodegenState, CodegenState>)((st19) => ((Func<CodegenState, CodegenState>)((st20) => ((Func<CodegenState, CodegenState>)((st21) => ((Func<CodegenState, CodegenState>)((st22) => ((Func<CodegenState, CodegenState>)((st23) => ((Func<CodegenState, CodegenState>)((st24) => ((Func<CodegenState, CodegenState>)((st25) => ((Func<CodegenState, CodegenState>)((st26) => ((Func<CodegenState, CodegenState>)((st27) => ((Func<CodegenState, CodegenState>)((st28) => ((Func<CodegenState, CodegenState>)((st29) => st_append_text(st29, x86_ret())))(st_append_text(st28, pop_r(reg_rbx())))))(st_append_text(st27, pop_r(reg_rcx())))))(st_append_text(st26, pop_r(reg_r11())))))(patch_jcc_at(st25, done_pos, st25.text_len))))(st_append_text(st24, jmp((loop_top - (st24.text_len + 5L)))))))(st_append_text(st23, add_ri(reg_r11(), 1L)))))(st_append_text(st22, out_dx_al()))))(st_append_text(st21, li(reg_rdx(), 1016L)))))(st_append_text(st20, pop_r(reg_rax())))))(patch_jcc_at(st19, thr_ready, st19.text_len))))(st_append_text(st18, jmp((wait_top - (st18.text_len + 5L)))))))(st_append_text(st17, jcc(cc_ne(), 0L)))))(st17.text_len)))(st_append_text(st16, new List<long> { 168L, 32L }))))(st_append_text(st15, new List<long> { 236L }))))(st_append_text(st14, li(reg_rdx(), 1021L)))))(st14.text_len)))(st_append_text(st13, push_r(reg_rax())))))(st_append_text(st12, mov_load(reg_rax(), reg_rax(), 8L)))))(st_append_text(st11, add_rr(reg_rax(), reg_rbx())))))(st_append_text(st10, shl_ri(reg_rax(), 3L)))))(st_append_text(st9, mov_rr(reg_rax(), reg_r11())))))(st_append_text(st8, jcc(cc_ge(), 0L)))))(st8.text_len)))(st_append_text(st7, cmp_rr(reg_r11(), reg_rcx())))))(st7.text_len)))(st_append_text(st6, li(reg_r11(), 0L)))))(st_append_text(st5, mov_load(reg_rcx(), reg_rbx(), 0L)))))(st_append_text(st4, mov_rr(reg_rbx(), reg_rdi())))))(st_append_text(st3, push_r(reg_r11())))))(st_append_text(st2, push_r(reg_rcx())))))(st_append_text(st1, push_r(reg_rbx())))))(record_func_offset(st, "\u0055\u0055\u001B\u0015\u0011\u000E\u000D\u0055\u0020\u0011\u0012\u000F\u0015\u001E"));

    public static CodegenState emit_linked_list_to_list_helper(CodegenState st) => ((Func<CodegenState, CodegenState>)((st1) => ((Func<CodegenState, CodegenState>)((st2) => ((Func<CodegenState, CodegenState>)((st3) => ((Func<CodegenState, CodegenState>)((st4) => ((Func<CodegenState, CodegenState>)((st5) => ((Func<CodegenState, CodegenState>)((st6) => ((Func<CodegenState, CodegenState>)((st7) => ((Func<CodegenState, CodegenState>)((st8) => ((Func<CodegenState, CodegenState>)((st9) => ((Func<long, CodegenState>)((count_loop) => ((Func<CodegenState, CodegenState>)((st10) => ((Func<long, CodegenState>)((count_done) => ((Func<CodegenState, CodegenState>)((st11) => ((Func<CodegenState, CodegenState>)((st12) => ((Func<CodegenState, CodegenState>)((st13) => ((Func<CodegenState, CodegenState>)((st14) => ((Func<CodegenState, CodegenState>)((st15) => ((Func<CodegenState, CodegenState>)((st16) => ((Func<CodegenState, CodegenState>)((st17) => ((Func<CodegenState, CodegenState>)((st18) => ((Func<CodegenState, CodegenState>)((st19) => ((Func<CodegenState, CodegenState>)((st20) => ((Func<CodegenState, CodegenState>)((st21) => ((Func<CodegenState, CodegenState>)((st22) => ((Func<CodegenState, CodegenState>)((st23) => ((Func<long, CodegenState>)((fill_loop) => ((Func<CodegenState, CodegenState>)((st24) => ((Func<long, CodegenState>)((fill_done) => ((Func<CodegenState, CodegenState>)((st25) => ((Func<CodegenState, CodegenState>)((st26) => ((Func<CodegenState, CodegenState>)((st27) => ((Func<CodegenState, CodegenState>)((st28) => ((Func<CodegenState, CodegenState>)((st29) => ((Func<CodegenState, CodegenState>)((st30) => ((Func<CodegenState, CodegenState>)((st31) => ((Func<CodegenState, CodegenState>)((st32) => ((Func<CodegenState, CodegenState>)((st33) => ((Func<CodegenState, CodegenState>)((st34) => ((Func<CodegenState, CodegenState>)((st35) => ((Func<CodegenState, CodegenState>)((st36) => ((Func<CodegenState, CodegenState>)((st37) => ((Func<CodegenState, CodegenState>)((st38) => ((Func<CodegenState, CodegenState>)((st39) => ((Func<CodegenState, CodegenState>)((st40) => st_append_text(st40, x86_ret())))(st_append_text(st39, pop_r(reg_rbp())))))(st_append_text(st38, pop_r(reg_rbx())))))(st_append_text(st37, pop_r(reg_r12())))))(st_append_text(st36, pop_r(reg_r13())))))(st_append_text(st35, mov_rr(reg_rax(), reg_r13())))))(patch_jcc_at(st34, fill_done, st34.text_len))))(st_append_text(st33, jmp((fill_loop - (st33.text_len + 5L)))))))(st_append_text(st32, mov_load(reg_rcx(), reg_rcx(), 8L)))))(st_append_text(st31, mov_store(reg_rsi(), reg_rax(), 0L)))))(st_append_text(st30, add_rr(reg_rsi(), reg_r13())))))(st_append_text(st29, shl_ri(reg_rsi(), 3L)))))(st_append_text(st28, add_ri(reg_rsi(), 1L)))))(st_append_text(st27, mov_rr(reg_rsi(), reg_rdx())))))(st_append_text(st26, mov_load(reg_rax(), reg_rcx(), 0L)))))(st_append_text(st25, sub_ri(reg_rdx(), 1L)))))(st_append_text(st24, jcc(cc_e(), 0L)))))(st24.text_len)))(st_append_text(st23, test_rr(reg_rcx(), reg_rcx())))))(st23.text_len)))(st_append_text(st22, mov_rr(reg_rdx(), reg_r12())))))(st_append_text(st21, mov_rr(reg_rcx(), reg_rbx())))))(st_append_text(st20, add_rr(reg_r10(), reg_rax())))))(st_append_text(st19, shl_ri(reg_rax(), 3L)))))(st_append_text(st18, add_ri(reg_rax(), 1L)))))(st_append_text(st17, mov_rr(reg_rax(), reg_r12())))))(st_append_text(st16, mov_store(reg_r13(), reg_r12(), 0L)))))(st_append_text(st15, mov_rr(reg_r13(), reg_r10())))))(patch_jcc_at(st14, count_done, st14.text_len))))(st_append_text(st13, jmp((count_loop - (st13.text_len + 5L)))))))(st_append_text(st12, mov_load(reg_rcx(), reg_rcx(), 8L)))))(st_append_text(st11, add_ri(reg_r12(), 1L)))))(st_append_text(st10, jcc(cc_e(), 0L)))))(st10.text_len)))(st_append_text(st9, test_rr(reg_rcx(), reg_rcx())))))(st9.text_len)))(st_append_text(st8, mov_rr(reg_rcx(), reg_rbx())))))(st_append_text(st7, li(reg_r12(), 0L)))))(st_append_text(st6, mov_rr(reg_rbx(), reg_rdi())))))(st_append_text(st5, push_r(reg_r13())))))(st_append_text(st4, push_r(reg_r12())))))(st_append_text(st3, push_r(reg_rbx())))))(st_append_text(st2, mov_rr(reg_rbp(), reg_rsp())))))(st_append_text(st1, push_r(reg_rbp())))))(record_func_offset(st, "\u0055\u0055\u0017\u0011\u0012\u0022\u000D\u0016\u0055\u0017\u0011\u0013\u000E\u0055\u000E\u0010\u0055\u0017\u0011\u0013\u000E"));

    public static CodexType ir_expr_type(IRExpr e)
    {
        while (true)
        {
            var _tco_s = e;
            if (_tco_s is IrIntLit _tco_m0)
            {
                var v = _tco_m0.Field0;
                var s = _tco_m0.Field1;
            return new IntegerTy();
            }
            else if (_tco_s is IrNumLit _tco_m1)
            {
                var v = _tco_m1.Field0;
                var s = _tco_m1.Field1;
            return new NumberTy();
            }
            else if (_tco_s is IrTextLit _tco_m2)
            {
                var v = _tco_m2.Field0;
                var s = _tco_m2.Field1;
            return new TextTy();
            }
            else if (_tco_s is IrBoolLit _tco_m3)
            {
                var v = _tco_m3.Field0;
                var s = _tco_m3.Field1;
            return new BooleanTy();
            }
            else if (_tco_s is IrCharLit _tco_m4)
            {
                var v = _tco_m4.Field0;
                var s = _tco_m4.Field1;
            return new CharTy();
            }
            else if (_tco_s is IrName _tco_m5)
            {
                var n = _tco_m5.Field0;
                var t = _tco_m5.Field1;
                var s = _tco_m5.Field2;
            return t;
            }
            else if (_tco_s is IrBinary _tco_m6)
            {
                var op = _tco_m6.Field0;
                var l = _tco_m6.Field1;
                var r = _tco_m6.Field2;
                var t = _tco_m6.Field3;
                var s = _tco_m6.Field4;
            return t;
            }
            else if (_tco_s is IrNegate _tco_m7)
            {
                var x = _tco_m7.Field0;
                var s = _tco_m7.Field1;
            return new IntegerTy();
            }
            else if (_tco_s is IrIf _tco_m8)
            {
                var c = _tco_m8.Field0;
                var th = _tco_m8.Field1;
                var el = _tco_m8.Field2;
                var t = _tco_m8.Field3;
                var s = _tco_m8.Field4;
            return t;
            }
            else if (_tco_s is IrLet _tco_m9)
            {
                var n = _tco_m9.Field0;
                var t = _tco_m9.Field1;
                var v = _tco_m9.Field2;
                var b = _tco_m9.Field3;
                var s = _tco_m9.Field4;
            var _tco_0 = b;
            e = _tco_0;
            continue;
            }
            else if (_tco_s is IrApply _tco_m10)
            {
                var f = _tco_m10.Field0;
                var a = _tco_m10.Field1;
                var t = _tco_m10.Field2;
                var s = _tco_m10.Field3;
            return t;
            }
            else if (_tco_s is IrLambda _tco_m11)
            {
                var ps = _tco_m11.Field0;
                var b = _tco_m11.Field1;
                var t = _tco_m11.Field2;
                var s = _tco_m11.Field3;
            return t;
            }
            else if (_tco_s is IrList _tco_m12)
            {
                var es = _tco_m12.Field0;
                var t = _tco_m12.Field1;
                var s = _tco_m12.Field2;
            return new ListTy(t);
            }
            else if (_tco_s is IrMatch _tco_m13)
            {
                var sc = _tco_m13.Field0;
                var bs = _tco_m13.Field1;
                var t = _tco_m13.Field2;
                var s = _tco_m13.Field3;
            return t;
            }
            else if (_tco_s is IrAct _tco_m14)
            {
                var ss = _tco_m14.Field0;
                var t = _tco_m14.Field1;
                var s = _tco_m14.Field2;
            return t;
            }
            else if (_tco_s is IrHandle _tco_m15)
            {
                var eff = _tco_m15.Field0;
                var h = _tco_m15.Field1;
                var cs = _tco_m15.Field2;
                var t = _tco_m15.Field3;
                var s = _tco_m15.Field4;
            return t;
            }
            else if (_tco_s is IrRecord _tco_m16)
            {
                var n = _tco_m16.Field0;
                var fs = _tco_m16.Field1;
                var t = _tco_m16.Field2;
                var s = _tco_m16.Field3;
            return t;
            }
            else if (_tco_s is IrFieldAccess _tco_m17)
            {
                var r = _tco_m17.Field0;
                var f = _tco_m17.Field1;
                var t = _tco_m17.Field2;
                var s = _tco_m17.Field3;
            return t;
            }
            else if (_tco_s is IrFork _tco_m18)
            {
                var body = _tco_m18.Field0;
                var t = _tco_m18.Field1;
                var s = _tco_m18.Field2;
            return t;
            }
            else if (_tco_s is IrAwait _tco_m19)
            {
                var task = _tco_m19.Field0;
                var t = _tco_m19.Field1;
                var s = _tco_m19.Field2;
            return t;
            }
            else if (_tco_s is IrError _tco_m20)
            {
                var m = _tco_m20.Field0;
                var t = _tco_m20.Field1;
                var s = _tco_m20.Field2;
            return t;
            }
        }
    }

    public static IRExpr set_ir_expr_type(IRExpr e, CodexType new_ty) => e switch { IrName(var n, var t, var s) => (IRExpr)(new IrName(n, new_ty, s)), IrApply(var f, var a, var t, var s) => (IRExpr)(new IrApply(f, a, new_ty, s)), IrLet(var n, var t, var v, var b, var s) => (IRExpr)(new IrLet(n, new_ty, v, b, s)), IrIf(var c, var th, var el, var t, var s) => (IRExpr)(new IrIf(c, th, el, new_ty, s)), IrMatch(var sc, var bs, var t, var s) => (IRExpr)(new IrMatch(sc, bs, new_ty, s)), IrAct(var ss, var t, var s) => (IRExpr)(new IrAct(ss, new_ty, s)), IrRecord(var n, var fs, var t, var s) => (IRExpr)(new IrRecord(n, fs, new_ty, s)), IrFieldAccess(var r, var f, var t, var s) => (IRExpr)(new IrFieldAccess(r, f, new_ty, s)), _ => (IRExpr)(e), };

    public static SourceSpan ir_expr_span(IRExpr e) => e switch { IrIntLit(var v, var s) => (SourceSpan)(s), IrNumLit(var v, var s) => (SourceSpan)(s), IrTextLit(var v, var s) => (SourceSpan)(s), IrBoolLit(var v, var s) => (SourceSpan)(s), IrCharLit(var v, var s) => (SourceSpan)(s), IrName(var n, var t, var s) => (SourceSpan)(s), IrBinary(var op, var l, var r, var t, var s) => (SourceSpan)(s), IrNegate(var x, var s) => (SourceSpan)(s), IrIf(var c, var th, var el, var t, var s) => (SourceSpan)(s), IrLet(var n, var t, var v, var b, var s) => (SourceSpan)(s), IrApply(var f, var a, var t, var s) => (SourceSpan)(s), IrLambda(var ps, var b, var t, var s) => (SourceSpan)(s), IrList(var es, var t, var s) => (SourceSpan)(s), IrMatch(var sc, var bs, var t, var s) => (SourceSpan)(s), IrAct(var ss, var t, var s) => (SourceSpan)(s), IrHandle(var eff, var h, var cs, var t, var s) => (SourceSpan)(s), IrRecord(var n, var fs, var t, var s) => (SourceSpan)(s), IrFieldAccess(var r, var f, var t, var s) => (SourceSpan)(s), IrFork(var body, var t, var s) => (SourceSpan)(s), IrAwait(var task, var t, var s) => (SourceSpan)(s), IrError(var m, var t, var s) => (SourceSpan)(s), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static SourceSpan lowered_span(SourceSpan s) => span_with_provenance(s, new ProvLowered());

    public static IRExpr lower_expr(AExpr e, CodexType ty, LowerCtx ctx) => e switch { ALitExpr(var text, var kind, var s) => (IRExpr)(lower_literal(text, kind, lowered_span(s))), ANameExpr(var name, var s) => (IRExpr)(lower_name(name.value, ty, ctx, lowered_span(s))), AApplyExpr(var f, var a, var s) => (IRExpr)(lower_apply(f, a, ty, ctx, lowered_span(s))), ABinaryExpr(var l, var op, var r, var s) => (IRExpr)(((Func<IRExpr, IRExpr>)((left_ir) => ((Func<CodexType, IRExpr>)((left_ty) => ((Func<IRExpr, IRExpr>)((right_ir) => new IrBinary(lower_bin_op(op, left_ty), left_ir, right_ir, binary_result_type(op, left_ty, ty), lowered_span(s))))(lower_expr(r, ty, ctx))))(ir_expr_type(left_ir))))(lower_expr(l, ty, ctx))), AUnaryExpr(var operand, var s) => (IRExpr)(new IrNegate(lower_expr(operand, new IntegerTy(), ctx), lowered_span(s))), AIfExpr(var c, var t, var e2, var s) => (IRExpr)(((Func<CodexType, IRExpr>)((resolved) => ((Func<IRExpr, IRExpr>)((then_ir) => ((Func<CodexType, IRExpr>)((then_ty) => ((Func<CodexType, IRExpr>)((result_ty) => ((Func<IRExpr, IRExpr>)((else_ir) => new IrIf(lower_expr(c, new BooleanTy(), ctx), then_ir, else_ir, result_ty, lowered_span(s))))(lower_expr(e2, result_ty, ctx))))(resolved switch { ErrorTy { } => (CodexType)(then_ty), _ => (CodexType)(resolved), })))(ir_expr_type(then_ir))))(lower_expr(t, resolved, ctx))))(deep_resolve(ctx.ust, ty))), ALetExpr(var binds, var body, var s) => (IRExpr)(lower_let(binds, body, ty, ctx, lowered_span(s))), ALambdaExpr(var @params, var body, var s) => (IRExpr)(lower_lambda(@params, body, ty, ctx, lowered_span(s))), AMatchExpr(var scrut, var arms, var s) => (IRExpr)(lower_match(scrut, arms, ty, ctx, lowered_span(s))), AListExpr(var elems, var s) => (IRExpr)(lower_list(elems, ty, ctx, lowered_span(s))), ARecordExpr(var name, var fields, var s) => (IRExpr)(lower_record(name, fields, ty, ctx, lowered_span(s))), AFieldAccess(var rec, var field, var s) => (IRExpr)(((Func<IRExpr, IRExpr>)((rec_ir) => ((Func<CodexType, IRExpr>)((rec_ty) => ((Func<CodexType, IRExpr>)((stripped_rec_ty) => ((Func<CodexType, IRExpr>)((resolved_rec_ty) => ((Func<CodexType, IRExpr>)((field_ty) => ((Func<CodexType, IRExpr>)((actual_field_ty) => ((Func<IRExpr, IRExpr>)((fixed_rec) => new IrFieldAccess(fixed_rec, field.value, actual_field_ty, lowered_span(s))))(rec_ty switch { RecordTy(var rn, var rf) => (IRExpr)(rec_ir), _ => (IRExpr)(set_ir_expr_type(rec_ir, resolved_rec_ty)), })))(field_ty switch { ErrorTy { } => (CodexType)(ty), _ => (CodexType)(field_ty), })))(resolved_rec_ty switch { RecordTy(var rname, var rfields) => (CodexType)(lookup_record_field(rfields, field.value)), _ => (CodexType)(ty), })))(stripped_rec_ty switch { RecordTy(var rn, var rf) => (CodexType)(stripped_rec_ty), ConstructedTy(var cname, var cargs) => (CodexType)(((Func<CodexType, CodexType>)((ctor_raw) => ((Func<CodexType, CodexType>)((resolved_record) => resolved_record switch { RecordTy(var rn, var rf) => (CodexType)(resolved_record), _ => (CodexType)(stripped_rec_ty), }))(ctor_raw switch { ErrorTy { } => (CodexType)(stripped_rec_ty), _ => (CodexType)(strip_fun_args_lower(deep_resolve(ctx.ust, ctor_raw))), })))(lookup_type_split(ctx.overlay, ctx.@base, cname.value))), _ => (CodexType)(stripped_rec_ty), })))(rec_ty switch { EffectfulTy(var effs, var ret) => (CodexType)(deep_resolve(ctx.ust, ret)), ForAllTy(var id, var body) => (CodexType)(deep_resolve(ctx.ust, body)), _ => (CodexType)(rec_ty), })))(deep_resolve(ctx.ust, ir_expr_type(rec_ir)))))(lower_expr(rec, new ErrorTy(), ctx))), AActExpr(var stmts, var s) => (IRExpr)(lower_act(stmts, ty, ctx, lowered_span(s))), AHandleExpr(var eff, var body, var clauses, var s) => (IRExpr)(lower_handle(eff, body, clauses, ty, ctx, lowered_span(s))), AErrorExpr(var msg, var s) => (IRExpr)(new IrError(msg, ty, lowered_span(s))), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static IRExpr lower_name(string name, CodexType ty, LowerCtx ctx, SourceSpan sp) => ((Func<CodexType, IRExpr>)((raw) => raw switch { ErrorTy { } => (IRExpr)(new IrName(name, ty, sp)), _ => (IRExpr)(((Func<CodexType, IRExpr>)((resolved) => ((Func<CodexType, IRExpr>)((stripped) => new IrName(name, prefer_applied_ty(ty, stripped), sp)))(strip_forall_ty(resolved))))(deep_resolve(ctx.ust, raw))), }))(lookup_type_split(ctx.overlay, ctx.@base, name));

    public static CodexType peel_effectful_ty(CodexType ty)
    {
        while (true)
        {
            var _tco_s = ty;
            if (_tco_s is EffectfulTy _tco_m0)
            {
                var effs = _tco_m0.Field0;
                var ret = _tco_m0.Field1;
            var _tco_0 = ret;
            ty = _tco_0;
            continue;
            }
            {
            return ty;
            }
        }
    }

    public static CodexType prefer_applied_ty(CodexType expected, CodexType stripped) => ((Func<CodexType, CodexType>)((peeled) => peeled switch { ConstructedTy(var en, var eargs) => (CodexType)(((((long)eargs.Count) == 0L) ? stripped : stripped switch { SumTy(var sn, var sc) => (CodexType)(((en.value == sn.value) ? peeled : stripped)), RecordTy(var rn, var rf) => (CodexType)(((en.value == rn.value) ? peeled : stripped)), ConstructedTy(var cn, var cargs) => (CodexType)((((en.value == cn.value) && (((long)cargs.Count) == 0L)) ? peeled : stripped)), _ => (CodexType)(stripped), })), _ => (CodexType)(stripped), }))(peel_effectful_ty(expected));

    public static IRExpr lower_literal(string text, LiteralKind kind, SourceSpan sp) => kind switch { IntLit { } => (IRExpr)(new IrIntLit(long.Parse(_Cce.ToUnicode(text)), sp)), NumLit { } => (IRExpr)(new IrNumLit(BitConverter.DoubleToInt64Bits(double.Parse(_Cce.ToUnicode(text), System.Globalization.CultureInfo.InvariantCulture)), sp)), TextLit { } => (IRExpr)(new IrTextLit(text, sp)), CharLit { } => (IRExpr)(new IrCharLit(long.Parse(_Cce.ToUnicode(text)), sp)), BoolLit { } => (IRExpr)(new IrBoolLit((text == "\u0028\u0015\u0019\u000D"), sp)), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static IRExpr lower_apply(AExpr f, AExpr a, CodexType ty, LowerCtx ctx, SourceSpan sp) => ((Func<IRExpr, IRExpr>)((func_ir) => ((Func<CodexType, IRExpr>)((func_ty) => ((Func<CodexType, IRExpr>)((arg_ty) => ((Func<CodexType, IRExpr>)((ret_ty) => ((Func<IRExpr, IRExpr>)((arg_ir) => ((Func<CodexType, IRExpr>)((resolved_ret) => ((Func<CodexType, IRExpr>)((actual_ret) => ((Func<CodexType, IRExpr>)((preferred_ret) => lower_apply_dispatch(func_ir, arg_ir, preferred_ret, sp)))(prefer_applied_ty(ty, actual_ret))))(resolved_ret switch { ErrorTy { } => (CodexType)(ty), _ => (CodexType)(resolved_ret), })))(subst_type_vars_from_arg(arg_ty, ir_expr_type(arg_ir), ret_ty))))(lower_expr(a, arg_ty, ctx))))(peel_fun_return(func_ty))))(peel_fun_param(func_ty))))(deep_resolve(ctx.ust, ir_expr_type(func_ir)))))(lower_expr(f, new ErrorTy(), ctx));

    public static IRExpr lower_apply_dispatch(IRExpr func_ir, IRExpr arg_ir, CodexType ret_ty, SourceSpan sp) => func_ir switch { IrName(var n, var fty, var ns) => (IRExpr)(((n == "\u001C\u0010\u0015\u0022") ? new IrFork(arg_ir, ret_ty, sp) : ((n == "\u000F\u001B\u000F\u0011\u000E") ? new IrAwait(arg_ir, ret_ty, sp) : new IrApply(func_ir, arg_ir, ret_ty, sp)))), _ => (IRExpr)(new IrApply(func_ir, arg_ir, ret_ty, sp)), };

    public static IRExpr lower_let(List<ALetBind> binds, AExpr body, CodexType ty, LowerCtx ctx, SourceSpan sp) => ((((long)binds.Count) == 0L) ? lower_expr(body, ty, ctx) : ((Func<ALetBind, IRExpr>)((b) => ((Func<IRExpr, IRExpr>)((val_ir) => ((Func<CodexType, IRExpr>)((val_ty) => ((Func<LowerCtx, IRExpr>)((ctx2) => new IrLet(b.name.value, val_ty, val_ir, lower_let_rest(binds, body, ty, ctx2, 1L, sp), sp)))(new LowerCtx(overlay: Enumerable.Concat(new List<TypeBinding> { new TypeBinding(name: b.name.value, bound_type: val_ty) }, ctx.overlay).ToList(), @base: ctx.@base, ust: ctx.ust))))(deep_resolve(ctx.ust, ir_expr_type(val_ir)))))(lower_expr(b.value, new ErrorTy(), ctx))))(binds[(int)0L]));

    public static IRExpr lower_let_rest(List<ALetBind> binds, AExpr body, CodexType ty, LowerCtx ctx, long i, SourceSpan sp) => ((i == ((long)binds.Count)) ? lower_expr(body, ty, ctx) : ((Func<ALetBind, IRExpr>)((b) => ((Func<IRExpr, IRExpr>)((val_ir) => ((Func<CodexType, IRExpr>)((val_ty) => ((Func<LowerCtx, IRExpr>)((ctx2) => new IrLet(b.name.value, val_ty, val_ir, lower_let_rest(binds, body, ty, ctx2, (i + 1L), sp), sp)))(new LowerCtx(overlay: Enumerable.Concat(new List<TypeBinding> { new TypeBinding(name: b.name.value, bound_type: val_ty) }, ctx.overlay).ToList(), @base: ctx.@base, ust: ctx.ust))))(deep_resolve(ctx.ust, ir_expr_type(val_ir)))))(lower_expr(b.value, new ErrorTy(), ctx))))(binds[(int)i]));

    public static IRExpr lower_lambda(List<Name> @params, AExpr body, CodexType ty, LowerCtx ctx, SourceSpan sp) => ((Func<CodexType, IRExpr>)((stripped) => ((Func<List<IRParam>, IRExpr>)((lparams) => ((Func<LowerCtx, IRExpr>)((lctx) => new IrLambda(lparams, lower_expr(body, get_lambda_return(stripped, ((long)@params.Count)), lctx), ty, sp)))(bind_lambda_to_ctx(ctx, @params, stripped, 0L))))(lower_lambda_params(@params, stripped, 0L, sp))))(strip_forall_ty(ty));

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
            var ctx2 = new LowerCtx(overlay: Enumerable.Concat(new List<TypeBinding> { new TypeBinding(name: p.value, bound_type: param_ty) }, ctx.overlay).ToList(), @base: ctx.@base, ust: ctx.ust);
            var _tco_0 = ctx2;
            var _tco_1 = @params;
            var _tco_2 = rest_ty;
            var _tco_3 = (i + 1L);
            ctx = _tco_0;
            @params = _tco_1;
            ty = _tco_2;
            i = _tco_3;
            continue;
            }
        }
    }

    public static List<IRParam> lower_lambda_params(List<Name> @params, CodexType ty, long i, SourceSpan sp) => lower_lambda_params_acc(@params, ty, i, sp, new List<IRParam>());

    public static List<IRParam> lower_lambda_params_acc(List<Name> @params, CodexType ty, long i, SourceSpan sp, List<IRParam> acc)
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
            var _tco_2 = (i + 1L);
            var _tco_3 = sp;
            var _tco_4 = ((Func<List<IRParam>>)(() => { var _l = acc; _l.Add(new IRParam(name: p.value, type_val: param_ty, span: sp)); return _l; }))();
            @params = _tco_0;
            ty = _tco_1;
            i = _tco_2;
            sp = _tco_3;
            acc = _tco_4;
            continue;
            }
        }
    }

    public static CodexType get_lambda_return(CodexType ty, long n)
    {
        while (true)
        {
            if ((n == 0L))
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
            var _tco_1 = (n - 1L);
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

    public static IRExpr lower_match(AExpr scrut, List<AMatchArm> arms, CodexType ty, LowerCtx ctx, SourceSpan sp) => ((Func<IRExpr, IRExpr>)((scrut_ir) => ((Func<CodexType, IRExpr>)((scrut_ty) => ((Func<List<IRBranch>, IRExpr>)((branches) => ((Func<CodexType, IRExpr>)((result_ty) => new IrMatch(scrut_ir, branches, result_ty, sp)))(ty switch { ErrorTy { } => (CodexType)(infer_match_type(branches, 0L, ((long)branches.Count))), _ => (CodexType)(ty), })))(lower_match_arms_loop(arms, ty, scrut_ty, ctx, 0L, ((long)arms.Count)))))(ir_expr_type(scrut_ir))))(lower_expr(scrut, new ErrorTy(), ctx));

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
            var _tco_1 = (i + 1L);
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
            var _tco_4 = (i + 1L);
            var _tco_5 = len;
            var _tco_6 = ((Func<List<IRBranch>>)(() => { var _l = acc; _l.Add(new IRBranch(pattern: lower_pattern(arm.pattern, scrut_ty), body: lower_expr(arm.body, ty, arm_ctx), span: arm.span)); return _l; }))();
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

    public static LowerCtx bind_pattern_to_ctx(LowerCtx ctx, APat pat, CodexType ty) => pat switch { AVarPat(var name, var s) => (LowerCtx)(new LowerCtx(overlay: Enumerable.Concat(new List<TypeBinding> { new TypeBinding(name: name.value, bound_type: ty) }, ctx.overlay).ToList(), @base: ctx.@base, ust: ctx.ust)), ACtorPat(var ctor_name, var sub_pats, var s) => (LowerCtx)(((Func<CodexType, LowerCtx>)((ctor_raw) => ((Func<CodexType, LowerCtx>)((ctor_ty) => ((Func<CodexType, LowerCtx>)((ctor_stripped) => bind_ctor_pattern_fields(ctx, sub_pats, ctor_stripped, 0L, ((long)sub_pats.Count))))(strip_forall_ty(ctor_ty))))(deep_resolve(ctx.ust, ctor_raw))))(lookup_type_split(ctx.overlay, ctx.@base, ctor_name.value))), AWildPat(var s) => (LowerCtx)(ctx), ALitPat(var text, var kind, var s) => (LowerCtx)(ctx), _ => throw new InvalidOperationException("Non-exhaustive match"), };

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
            var _tco_3 = (i + 1L);
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
            var _tco_3 = (i + 1L);
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

    public static IRPat lower_pattern(APat p, CodexType scrut_ty) => p switch { AVarPat(var name, var s) => (IRPat)(new IrVarPat(name.value, scrut_ty, lowered_span(s))), ALitPat(var text, var kind, var s) => (IRPat)(new IrLitPat(text, scrut_ty, lowered_span(s))), ACtorPat(var name, var subs, var s) => (IRPat)(new IrCtorPat(name.value, lower_ctor_sub_patterns(subs, scrut_ty, name.value, 0L), scrut_ty, lowered_span(s))), AWildPat(var s) => (IRPat)(new IrWildPat(lowered_span(s))), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static List<IRPat> lower_ctor_sub_patterns(List<APat> subs, CodexType scrut_ty, string ctor_name, long i) => ((i == ((long)subs.Count)) ? new List<IRPat>() : ((Func<CodexType, List<IRPat>>)((field_ty) => Enumerable.Concat(new List<IRPat> { lower_pattern(subs[(int)i], field_ty) }, lower_ctor_sub_patterns(subs, scrut_ty, ctor_name, (i + 1L))).ToList()))(lookup_ctor_field_type(scrut_ty, ctor_name, i)));

    public static CodexType lookup_ctor_field_type(CodexType ty, string ctor_name, long field_idx) => ty switch { SumTy(var sname, var ctors) => (CodexType)(find_ctor_field_type(ctors, ctor_name, field_idx, 0L)), _ => (CodexType)(new ErrorTy()), };

    public static CodexType find_ctor_field_type(List<SumCtor> ctors, string ctor_name, long field_idx, long i)
    {
        while (true)
        {
            if ((i == ((long)ctors.Count)))
            {
            return new ErrorTy();
            }
            else
            {
            var c = ctors[(int)i];
            if ((c.name.value == ctor_name))
            {
            if ((field_idx < ((long)c.fields.Count)))
            {
            return c.fields[(int)field_idx];
            }
            else
            {
            return new ErrorTy();
            }
            }
            else
            {
            var _tco_0 = ctors;
            var _tco_1 = ctor_name;
            var _tco_2 = field_idx;
            var _tco_3 = (i + 1L);
            ctors = _tco_0;
            ctor_name = _tco_1;
            field_idx = _tco_2;
            i = _tco_3;
            continue;
            }
            }
        }
    }

    public static IRExpr lower_list(List<AExpr> elems, CodexType ty, LowerCtx ctx, SourceSpan sp) => ((Func<CodexType, IRExpr>)((resolved) => ((Func<CodexType, IRExpr>)((elem_ty) => new IrList(lower_list_elems_loop(elems, elem_ty, ctx, 0L, ((long)elems.Count)), elem_ty, sp)))(resolved switch { ListTy(var e) => (CodexType)(e), _ => (CodexType)(((((long)elems.Count) == 0L) ? new ErrorTy() : ir_expr_type(lower_expr(elems[(int)0L], new ErrorTy(), ctx)))), })))(deep_resolve(ctx.ust, ty));

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
            var _tco_3 = (i + 1L);
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

    public static IRExpr lower_record(Name name, List<AFieldExpr> fields, CodexType ty, LowerCtx ctx, SourceSpan sp) => ((Func<CodexType, IRExpr>)((ctor_raw) => ((Func<CodexType, IRExpr>)((record_ty) => ((Func<CodexType, IRExpr>)((actual_ty) => new IrRecord(name.value, lower_record_fields_typed(fields, actual_ty, ctx, 0L, ((long)fields.Count)), actual_ty, sp)))(record_ty switch { ErrorTy { } => (CodexType)(ty), _ => (CodexType)(record_ty), })))(ctor_raw switch { ErrorTy { } => (CodexType)(ty), _ => (CodexType)(strip_fun_args_lower(deep_resolve(ctx.ust, ctor_raw))), })))(lookup_type_split(ctx.overlay, ctx.@base, name.value));

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
            var field_expected = record_ty switch { RecordTy(var rname, var rfields) => (CodexType)(lookup_record_field(rfields, f.name.value)), _ => (CodexType)(new ErrorTy()), };
            var _tco_0 = fields;
            var _tco_1 = record_ty;
            var _tco_2 = ctx;
            var _tco_3 = (i + 1L);
            var _tco_4 = len;
            var _tco_5 = ((Func<List<IRFieldVal>>)(() => { var _l = acc; _l.Add(new IRFieldVal(name: f.name.value, value: lower_expr(f.value, field_expected, ctx), span: f.span)); return _l; }))();
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

    public static IRExpr lower_act(List<AActStmt> stmts, CodexType ty, LowerCtx ctx, SourceSpan sp) => ((Func<List<IRActStmt>, IRExpr>)((lowered) => ((Func<CodexType, IRExpr>)((act_ty) => new IrAct(lowered, act_ty, sp)))(act_block_type(lowered, ty))))(lower_act_stmts_loop(stmts, ty, ctx, 0L, ((long)stmts.Count)));

    public static CodexType act_block_type(List<IRActStmt> stmts, CodexType fallback) => ((((long)stmts.Count) == 0L) ? fallback : ((Func<IRActStmt, CodexType>)((last) => last switch { IrDoExec(var e, var sp) => (CodexType)(ir_expr_type(e)), IrDoBind(var nm, var bty, var e, var sp) => (CodexType)(bty), _ => throw new InvalidOperationException("Non-exhaustive match"), }))(stmts[(int)(((long)stmts.Count) - 1L)]));

    public static List<IRActStmt> lower_act_stmts_loop(List<AActStmt> stmts, CodexType ty, LowerCtx ctx, long i, long len) => lower_act_stmts_acc(stmts, ty, ctx, i, len, new List<IRActStmt>());

    public static List<IRActStmt> lower_act_stmts_acc(List<AActStmt> stmts, CodexType ty, LowerCtx ctx, long i, long len, List<IRActStmt> acc)
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
            if (_tco_s is AActBindStmt _tco_m0)
            {
                var name = _tco_m0.Field0;
                var val = _tco_m0.Field1;
                var sp = _tco_m0.Field2;
            var val_ir = lower_expr(val, ty, ctx);
            var val_ty = ir_expr_type(val_ir);
            var ctx2 = new LowerCtx(overlay: Enumerable.Concat(new List<TypeBinding> { new TypeBinding(name: name.value, bound_type: val_ty) }, ctx.overlay).ToList(), @base: ctx.@base, ust: ctx.ust);
            var _tco_0 = stmts;
            var _tco_1 = ty;
            var _tco_2 = ctx2;
            var _tco_3 = (i + 1L);
            var _tco_4 = len;
            var _tco_5 = ((Func<List<IRActStmt>>)(() => { var _l = acc; _l.Add(new IrDoBind(name.value, val_ty, val_ir, lowered_span(sp))); return _l; }))();
            stmts = _tco_0;
            ty = _tco_1;
            ctx = _tco_2;
            i = _tco_3;
            len = _tco_4;
            acc = _tco_5;
            continue;
            }
            else if (_tco_s is AActExprStmt _tco_m1)
            {
                var e = _tco_m1.Field0;
                var sp = _tco_m1.Field1;
            var _tco_0 = stmts;
            var _tco_1 = ty;
            var _tco_2 = ctx;
            var _tco_3 = (i + 1L);
            var _tco_4 = len;
            var _tco_5 = ((Func<List<IRActStmt>>)(() => { var _l = acc; _l.Add(new IrDoExec(lower_expr(e, ty, ctx), lowered_span(sp))); return _l; }))();
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

    public static IRExpr lower_handle(Name eff, AExpr body, List<AHandleClause> clauses, CodexType ty, LowerCtx ctx, SourceSpan sp) => ((Func<IRExpr, IRExpr>)((body_ir) => new IrHandle(eff.value, body_ir, lower_handle_clauses(clauses, ty, ctx), ty, sp)))(lower_expr(body, ty, ctx));

    public static List<IRHandleClause> lower_handle_clauses(List<AHandleClause> clauses, CodexType ty, LowerCtx ctx) => lower_handle_clauses_loop(clauses, ty, ctx, 0L);

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
            var _tco_3 = (i + 1L);
            var _tco_4 = ((Func<List<IRHandleClause>>)(() => { var _l = acc; _l.Add(new IRHandleClause(op_name: c.op_name.value, resume_name: c.resume_name.value, body: body_ir, span: c.span)); return _l; }))();
            clauses = _tco_0;
            ty = _tco_1;
            ctx = _tco_2;
            i = _tco_3;
            acc = _tco_4;
            continue;
            }
        }
    }

    public static IRDef lower_def(ADef d, List<TypeBinding> types, UnificationState ust) => ((Func<CodexType, IRDef>)((raw_type) => ((Func<CodexType, IRDef>)((full_type) => ((Func<CodexType, IRDef>)((stripped) => ((Func<List<IRParam>, IRDef>)((@params) => ((Func<CodexType, IRDef>)((ret_type) => ((Func<LowerCtx, IRDef>)((ctx) => new IRDef(name: d.name.value, @params: @params, type_val: full_type, body: lower_expr(d.body, ret_type, ctx), chapter_slug: d.chapter_slug, span: d.span)))(build_def_ctx(types, ust, d.@params, stripped))))(get_return_type_n(stripped, ((long)d.@params.Count)))))(lower_def_params(d.@params, stripped, 0L))))(strip_forall_ty(full_type))))(deep_resolve(ust, raw_type))))(lookup_type_bsearch(types, d.name.value));

    public static LowerCtx build_def_ctx(List<TypeBinding> types, UnificationState ust, List<AParam> @params, CodexType ty) => ((Func<LowerCtx, LowerCtx>)((base_ctx) => bind_params_to_ctx(base_ctx, @params, ty, 0L)))(new LowerCtx(overlay: new List<TypeBinding>(), @base: types, ust: ust));

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
            var ctx2 = new LowerCtx(overlay: Enumerable.Concat(new List<TypeBinding> { new TypeBinding(name: p.name.value, bound_type: param_ty) }, ctx.overlay).ToList(), @base: ctx.@base, ust: ctx.ust);
            var _tco_0 = ctx2;
            var _tco_1 = @params;
            var _tco_2 = rest_ty;
            var _tco_3 = (i + 1L);
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
            var _tco_2 = (i + 1L);
            var _tco_3 = ((Func<List<IRParam>>)(() => { var _l = acc; _l.Add(new IRParam(name: p.name.value, type_val: param_ty, span: p.span)); return _l; }))();
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
            if ((n == 0L))
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
            var _tco_1 = (n - 1L);
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

    public static IRChapter lower_chapter(AChapter m, List<TypeBinding> types, UnificationState ust) => ((Func<List<TypeBinding>, IRChapter>)((ctor_types) => ((Func<List<TypeBinding>, IRChapter>)((all_types) => new IRChapter(name: m.name, defs: lower_defs(m.defs, all_types, ust, 0L), chapter_title: m.chapter_title, prose: m.prose, section_titles: m.section_titles, span: m.span)))(sort_bindings(Enumerable.Concat(ctor_types, Enumerable.Concat(types, builtin_type_env().bindings).ToList()).ToList()))))(collect_ctor_bindings(m.type_defs, 0L, ((long)m.type_defs.Count), new List<TypeBinding>()));

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
            var _tco_3 = (i + 1L);
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

    public static CodexType lookup_type(List<TypeBinding> bindings, string name) => lookup_type_loop(bindings, name, 0L, ((long)bindings.Count));

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

    public static CodexType lookup_type_bsearch(List<TypeBinding> bindings, string name) => ((Func<long, CodexType>)((len) => ((len == 0L) ? new ErrorTy() : ((Func<long, CodexType>)((pos) => ((pos >= len) ? new ErrorTy() : ((Func<TypeBinding, CodexType>)((b) => ((b.name == name) ? b.bound_type : new ErrorTy())))(bindings[(int)pos]))))(bsearch_text_pos(bindings, name, 0L, len)))))(((long)bindings.Count));

    public static CodexType lookup_type_split(List<TypeBinding> overlay, List<TypeBinding> @base, string name) => ((Func<CodexType, CodexType>)((overlay_hit) => overlay_hit switch { ErrorTy { } => (CodexType)(lookup_type_bsearch(@base, name)), _ => (CodexType)(overlay_hit), }))(lookup_type_loop(overlay, name, 0L, ((long)overlay.Count)));

    public static List<TypeBinding> sort_bindings_loop(List<TypeBinding> xs, long i, long len, List<TypeBinding> acc)
    {
        while (true)
        {
            if ((i == len))
            {
            return acc;
            }
            else
            {
            var b = xs[(int)i];
            var acc_len = ((long)acc.Count);
            var pos = bsearch_text_pos(acc, b.name, 0L, acc_len);
            if ((pos < acc_len))
            {
            if ((acc[(int)pos].name == b.name))
            {
            var _tco_0 = xs;
            var _tco_1 = (i + 1L);
            var _tco_2 = len;
            var _tco_3 = acc;
            xs = _tco_0;
            i = _tco_1;
            len = _tco_2;
            acc = _tco_3;
            continue;
            }
            else
            {
            var _tco_0 = xs;
            var _tco_1 = (i + 1L);
            var _tco_2 = len;
            var _tco_3 = ((Func<List<TypeBinding>>)(() => { var _l = new List<TypeBinding>(acc); _l.Insert((int)pos, b); return _l; }))();
            xs = _tco_0;
            i = _tco_1;
            len = _tco_2;
            acc = _tco_3;
            continue;
            }
            }
            else
            {
            var _tco_0 = xs;
            var _tco_1 = (i + 1L);
            var _tco_2 = len;
            var _tco_3 = ((Func<List<TypeBinding>>)(() => { var _l = new List<TypeBinding>(acc); _l.Insert((int)pos, b); return _l; }))();
            xs = _tco_0;
            i = _tco_1;
            len = _tco_2;
            acc = _tco_3;
            continue;
            }
            }
        }
    }

    public static List<TypeBinding> sort_bindings(List<TypeBinding> xs) => sort_bindings_loop(xs, 0L, ((long)xs.Count), new List<TypeBinding>());

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

    public static CodexType subst_type_vars_from_arg(CodexType param_ty, CodexType arg_ty, CodexType target) => param_ty switch { TypeVar(var id) => (CodexType)(subst_type_var_in_target(target, id, arg_ty)), ListTy(var pe) => (CodexType)(subst_from_list(pe, arg_ty, target)), FunTy(var pp, var pr) => (CodexType)(subst_from_fun(pp, pr, arg_ty, target)), _ => (CodexType)(target), };

    public static CodexType subst_from_list(CodexType pe, CodexType arg_ty, CodexType target) => arg_ty switch { ListTy(var ae) => (CodexType)(subst_type_vars_from_arg(pe, ae, target)), _ => (CodexType)(target), };

    public static CodexType subst_from_fun(CodexType pp, CodexType pr, CodexType arg_ty, CodexType target) => arg_ty switch { FunTy(var ap, var ar) => (CodexType)(((Func<CodexType, CodexType>)((t2) => subst_type_vars_from_arg(pr, ar, t2)))(subst_type_vars_from_arg(pp, ap, target))), _ => (CodexType)(target), };

    public static CodexType subst_type_var_in_target(CodexType ty, long var_id, CodexType replacement) => ty switch { TypeVar(var id) => (CodexType)(((id == var_id) ? replacement : ty)), FunTy(var p, var r) => (CodexType)(new FunTy(subst_type_var_in_target(p, var_id, replacement), subst_type_var_in_target(r, var_id, replacement))), ListTy(var elem) => (CodexType)(new ListTy(subst_type_var_in_target(elem, var_id, replacement))), ForAllTy(var fid, var body) => (CodexType)(((fid == var_id) ? ty : new ForAllTy(fid, subst_type_var_in_target(body, var_id, replacement)))), _ => (CodexType)(ty), };

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

    public static bool is_text_type(CodexType ty) => ty switch { TextTy { } => (bool)(true), _ => (bool)(false), };

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
            var _tco_1 = (i + 1L);
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

    public static List<TypeBinding> ctor_bindings_for_typedef(ATypeDef td) => td switch { AVariantTypeDef(var name, var type_params, var ctors, var s) => (List<TypeBinding>)(((Func<List<SumCtor>, List<TypeBinding>>)((sum_ctors) => ((Func<CodexType, List<TypeBinding>>)((result_ty) => Enumerable.Concat(new List<TypeBinding> { new TypeBinding(name: name.value, bound_type: result_ty) }, collect_variant_ctor_bindings(ctors, result_ty, 0L, ((long)ctors.Count), new List<TypeBinding>())).ToList()))(new SumTy(name, sum_ctors))))(ir__lowering_build_sum_ctors(ctors, 0L, ((long)ctors.Count), new List<SumCtor>()))), ARecordTypeDef(var name, var type_params, var fields, var s) => (List<TypeBinding>)(((Func<List<RecordField>, List<TypeBinding>>)((resolved_fields) => ((Func<CodexType, List<TypeBinding>>)((result_ty) => ((Func<CodexType, List<TypeBinding>>)((ctor_ty) => new List<TypeBinding> { new TypeBinding(name: name.value, bound_type: ctor_ty) }))(ir__lowering_build_record_ctor_type(fields, result_ty, 0L, ((long)fields.Count)))))(new RecordTy(name, resolved_fields))))(ir__lowering_build_record_fields(fields, 0L, ((long)fields.Count), new List<RecordField>()))), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static List<SumCtor> ir__lowering_build_sum_ctors(List<AVariantCtorDef> ctors, long i, long len, List<SumCtor> acc)
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
            var _tco_0 = ctors;
            var _tco_1 = (i + 1L);
            var _tco_2 = len;
            var _tco_3 = ((Func<List<SumCtor>>)(() => { var _l = acc; _l.Add(new SumCtor(name: c.name, fields: new List<CodexType>())); return _l; }))();
            ctors = _tco_0;
            i = _tco_1;
            len = _tco_2;
            acc = _tco_3;
            continue;
            }
        }
    }

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
            var ctor_ty = ir__lowering_build_ctor_type(ctor.fields, result_ty, 0L, ((long)ctor.fields.Count));
            var _tco_0 = ctors;
            var _tco_1 = result_ty;
            var _tco_2 = (i + 1L);
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

    public static CodexType ir__lowering_build_ctor_type(List<ATypeExpr> fields, CodexType result, long i, long len) => ((i == len) ? result : ((Func<CodexType, CodexType>)((rest) => new FunTy(ir__lowering_resolve_type_expr(fields[(int)i]), rest)))(ir__lowering_build_ctor_type(fields, result, (i + 1L), len)));

    public static List<RecordField> ir__lowering_build_record_fields(List<ARecordFieldDef> fields, long i, long len, List<RecordField> acc)
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
            var rfield = new RecordField(name: f.name, type_val: ir__lowering_resolve_type_expr(f.type_expr));
            var _tco_0 = fields;
            var _tco_1 = (i + 1L);
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

    public static CodexType ir__lowering_build_record_ctor_type(List<ARecordFieldDef> fields, CodexType result, long i, long len) => ((i == len) ? result : ((Func<ARecordFieldDef, CodexType>)((f) => ((Func<CodexType, CodexType>)((rest) => new FunTy(ir__lowering_resolve_type_expr(f.type_expr), rest)))(ir__lowering_build_record_ctor_type(fields, result, (i + 1L), len))))(fields[(int)i]));

    public static List<CodexType> ir__lowering_resolve_type_expr_list(List<ATypeExpr> args, long i, long len, List<CodexType> acc)
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
            var _tco_1 = (i + 1L);
            var _tco_2 = len;
            var _tco_3 = ((Func<List<CodexType>>)(() => { var _l = acc; _l.Add(ir__lowering_resolve_type_expr(args[(int)i])); return _l; }))();
            args = _tco_0;
            i = _tco_1;
            len = _tco_2;
            acc = _tco_3;
            continue;
            }
        }
    }

    public static CodexType ir__lowering_resolve_type_expr(ATypeExpr texpr) => texpr switch { ANamedType(var name, var s) => (CodexType)(((name.value == "\u002B\u0012\u000E\u000D\u001D\u000D\u0015") ? new IntegerTy() : ((name.value == "\u002C\u0019\u001A\u0020\u000D\u0015") ? new NumberTy() : ((name.value == "\u0028\u000D\u0024\u000E") ? new TextTy() : ((name.value == "\u003A\u0010\u0010\u0017\u000D\u000F\u0012") ? new BooleanTy() : ((name.value == "\u002C\u0010\u000E\u0014\u0011\u0012\u001D") ? new NothingTy() : new ConstructedTy(name, new List<CodexType>()))))))), AFunType(var param, var ret, var s) => (CodexType)(new FunTy(ir__lowering_resolve_type_expr(param), ir__lowering_resolve_type_expr(ret))), AAppType(var ctor, var args, var s) => (CodexType)(ctor switch { ANamedType(var cname, var s2) => (CodexType)(((cname.value == "\u0031\u0011\u0013\u000E") ? ((((long)args.Count) == 1L) ? new ListTy(ir__lowering_resolve_type_expr(args[(int)0L])) : new ListTy(new ErrorTy())) : new ConstructedTy(cname, ir__lowering_resolve_type_expr_list(args, 0L, ((long)args.Count), new List<CodexType>())))), _ => (CodexType)(new ErrorTy()), }), AEffectType(var effs, var ret, var s) => (CodexType)(new EffectfulTy(effs, ir__lowering_resolve_type_expr(ret))), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static bool is_number_type(CodexType ty) => ty switch { NumberTy { } => (bool)(true), _ => (bool)(false), };

    public static IRBinaryOp lower_bin_op(BinaryOp op, CodexType ty) => op switch { OpAdd { } => (IRBinaryOp)((is_number_type(ty) ? new IrAddNum() : new IrAddInt())), OpSub { } => (IRBinaryOp)((is_number_type(ty) ? new IrSubNum() : new IrSubInt())), OpMul { } => (IRBinaryOp)((is_number_type(ty) ? new IrMulNum() : new IrMulInt())), OpDiv { } => (IRBinaryOp)((is_number_type(ty) ? new IrDivNum() : new IrDivInt())), OpPow { } => (IRBinaryOp)(new IrPowInt()), OpEq { } => (IRBinaryOp)(new IrEq()), OpNotEq { } => (IRBinaryOp)(new IrNotEq()), OpLt { } => (IRBinaryOp)(new IrLt()), OpGt { } => (IRBinaryOp)(new IrGt()), OpLtEq { } => (IRBinaryOp)(new IrLtEq()), OpGtEq { } => (IRBinaryOp)(new IrGtEq()), OpDefEq { } => (IRBinaryOp)(new IrEq()), OpAppend { } => (IRBinaryOp)((is_text_type(ty) ? new IrAppendText() : new IrAppendList())), OpCons { } => (IRBinaryOp)(new IrConsList()), OpAnd { } => (IRBinaryOp)(new IrAnd()), OpOr { } => (IRBinaryOp)(new IrOr()), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static CodexType binary_result_type(BinaryOp op, CodexType left_ty, CodexType expected_ty) => op switch { OpEq { } => (CodexType)(new BooleanTy()), OpNotEq { } => (CodexType)(new BooleanTy()), OpLt { } => (CodexType)(new BooleanTy()), OpGt { } => (CodexType)(new BooleanTy()), OpLtEq { } => (CodexType)(new BooleanTy()), OpGtEq { } => (CodexType)(new BooleanTy()), OpDefEq { } => (CodexType)(new BooleanTy()), OpAnd { } => (CodexType)(new BooleanTy()), OpOr { } => (CodexType)(new BooleanTy()), OpAppend { } => (CodexType)((is_text_type(left_ty) ? new TextTy() : (is_text_type(expected_ty) ? new TextTy() : left_ty))), _ => (CodexType)(left_ty), };

    public static List<ChapterAssignment> assign_chapters(List<DefHeader> headers, string current_chapter, long i, List<ChapterAssignment> acc)
    {
        while (true)
        {
            if ((i == ((long)headers.Count)))
            {
            return acc;
            }
            else
            {
            var hdr = headers[(int)i];
            var _tco_0 = headers;
            var _tco_1 = current_chapter;
            var _tco_2 = (i + 1L);
            var _tco_3 = ((Func<List<ChapterAssignment>>)(() => { var _l = acc; _l.Add(new ChapterAssignment(def_name: hdr.name.text, chapter_slug: current_chapter)); return _l; }))();
            headers = _tco_0;
            current_chapter = _tco_1;
            i = _tco_2;
            acc = _tco_3;
            continue;
            }
        }
    }

    public static List<string> find_colliding_names(List<ChapterAssignment> assignments) => find_collisions_loop(assignments, 0L, ((long)assignments.Count), new List<string>());

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
            if (acc.Contains(a.def_name))
            {
            var _tco_0 = assignments;
            var _tco_1 = (i + 1L);
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
            if (appears_in_different_chapter(assignments, a.def_name, a.chapter_slug, 0L, len))
            {
            var _tco_0 = assignments;
            var _tco_1 = (i + 1L);
            var _tco_2 = len;
            var _tco_3 = ((Func<List<string>>)(() => { var _l = acc; _l.Add(a.def_name); return _l; }))();
            assignments = _tco_0;
            i = _tco_1;
            len = _tco_2;
            acc = _tco_3;
            continue;
            }
            else
            {
            var _tco_0 = assignments;
            var _tco_1 = (i + 1L);
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
            var _tco_3 = (i + 1L);
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
            var _tco_3 = (i + 1L);
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

    public static string mangle_name(string chapter_slug, string def_name) => (slugify(chapter_slug) + ("\u0055" + def_name));

    public static List<string> slugify_loop(string s, long i, long len, List<string> acc)
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
            if ((c == ((long)"\u0002"[(int)0L])))
            {
            var _tco_0 = s;
            var _tco_1 = (i + 1L);
            var _tco_2 = len;
            var _tco_3 = ((Func<List<string>>)(() => { var _l = acc; _l.Add("\u0049"); return _l; }))();
            s = _tco_0;
            i = _tco_1;
            len = _tco_2;
            acc = _tco_3;
            continue;
            }
            else
            {
            if ((c >= cc_first_upper()))
            {
            if ((c <= cc_upper_z()))
            {
            var _tco_0 = s;
            var _tco_1 = (i + 1L);
            var _tco_2 = len;
            var _tco_3 = ((Func<List<string>>)(() => { var _l = acc; _l.Add(((char)(c - 26L)).ToString()); return _l; }))();
            s = _tco_0;
            i = _tco_1;
            len = _tco_2;
            acc = _tco_3;
            continue;
            }
            else
            {
            var _tco_0 = s;
            var _tco_1 = (i + 1L);
            var _tco_2 = len;
            var _tco_3 = ((Func<List<string>>)(() => { var _l = acc; _l.Add(((char)c).ToString()); return _l; }))();
            s = _tco_0;
            i = _tco_1;
            len = _tco_2;
            acc = _tco_3;
            continue;
            }
            }
            else
            {
            var _tco_0 = s;
            var _tco_1 = (i + 1L);
            var _tco_2 = len;
            var _tco_3 = ((Func<List<string>>)(() => { var _l = acc; _l.Add(((char)c).ToString()); return _l; }))();
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

    public static string slugify(string s) => string.Concat(slugify_loop(s, 0L, ((long)s.Length), new List<string>()));

    public static string lookup_chapter(List<ChapterAssignment> assignments, string name) => lookup_chapter_loop(assignments, name, 0L, ((long)assignments.Count));

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
            var _tco_2 = (i + 1L);
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

    public static List<RenameEntry> build_rename_map(List<string> colliding, List<ChapterAssignment> assignments, string current_chapter) => build_rename_map_loop(colliding, assignments, current_chapter, 0L, ((long)colliding.Count), new List<RenameEntry>());

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
            var _tco_3 = (i + 1L);
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

    public static string rename_lookup(List<RenameEntry> entries, string name) => rename_lookup_loop(entries, name, 0L, ((long)entries.Count));

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
            var _tco_2 = (i + 1L);
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

    public static bool is_colliding(List<string> names, string name) => names.Contains(name);

    public static List<RenameEntry> remove_rename(List<RenameEntry> entries, string name) => remove_rename_loop(entries, name, 0L, ((long)entries.Count), new List<RenameEntry>());

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
            var _tco_2 = (i + 1L);
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
            var _tco_2 = (i + 1L);
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
            var _tco_2 = (i + 1L);
            entries = _tco_0;
            @params = _tco_1;
            i = _tco_2;
            continue;
            }
        }
    }

    public static IRExpr rename_ir_expr(List<RenameEntry> rn, IRExpr e) => e switch { IrIntLit(var v, var sp) => (IRExpr)(e), IrNumLit(var v, var sp) => (IRExpr)(e), IrTextLit(var v, var sp) => (IRExpr)(e), IrBoolLit(var v, var sp) => (IRExpr)(e), IrCharLit(var v, var sp) => (IRExpr)(e), IrError(var msg, var ty, var sp) => (IRExpr)(e), IrFork(var x, var ty, var sp) => (IRExpr)(new IrFork(rename_ir_expr(rn, x), ty, sp)), IrAwait(var x, var ty, var sp) => (IRExpr)(new IrAwait(rename_ir_expr(rn, x), ty, sp)), IrNegate(var x, var sp) => (IRExpr)(new IrNegate(rename_ir_expr(rn, x), sp)), IrName(var n, var ty, var sp) => (IRExpr)(new IrName(rename_lookup(rn, n), ty, sp)), IrBinary(var op, var l, var r, var ty, var sp) => (IRExpr)(new IrBinary(op, rename_ir_expr(rn, l), rename_ir_expr(rn, r), ty, sp)), IrApply(var f, var a, var ty, var sp) => (IRExpr)(new IrApply(rename_ir_expr(rn, f), rename_ir_expr(rn, a), ty, sp)), IrIf(var c, var t, var el, var ty, var sp) => (IRExpr)(new IrIf(rename_ir_expr(rn, c), rename_ir_expr(rn, t), rename_ir_expr(rn, el), ty, sp)), IrFieldAccess(var rec, var field, var ty, var sp) => (IRExpr)(new IrFieldAccess(rename_ir_expr(rn, rec), field, ty, sp)), IrLet(var nm, var ty, var val, var body, var sp) => (IRExpr)(rename_ir_let(rn, nm, ty, val, body, sp)), IrLambda(var @params, var body, var ty, var sp) => (IRExpr)(rename_ir_lambda(rn, @params, body, ty, sp)), IrMatch(var scrut, var branches, var ty, var sp) => (IRExpr)(new IrMatch(rename_ir_expr(rn, scrut), rename_ir_branches(rn, branches, 0L, new List<IRBranch>()), ty, sp)), IrList(var elems, var ty, var sp) => (IRExpr)(new IrList(rename_ir_exprs(rn, elems, 0L, new List<IRExpr>()), ty, sp)), IrRecord(var nm, var fields, var ty, var sp) => (IRExpr)(new IrRecord(nm, rename_ir_fields(rn, fields, 0L, new List<IRFieldVal>()), ty, sp)), IrAct(var stmts, var ty, var sp) => (IRExpr)(new IrAct(rename_ir_act_stmts(rn, stmts, 0L, new List<IRActStmt>()), ty, sp)), IrHandle(var eff, var body, var clauses, var ty, var sp) => (IRExpr)(new IrHandle(eff, rename_ir_expr(rn, body), rename_ir_handle_clauses(rn, clauses, 0L, new List<IRHandleClause>()), ty, sp)), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static IRExpr rename_ir_let(List<RenameEntry> rn, string nm, CodexType ty, IRExpr val, IRExpr body, SourceSpan sp) => ((Func<List<RenameEntry>, IRExpr>)((rn2) => new IrLet(nm, ty, rename_ir_expr(rn, val), rename_ir_expr(rn2, body), sp)))(remove_rename(rn, nm));

    public static IRExpr rename_ir_lambda(List<RenameEntry> rn, List<IRParam> @params, IRExpr body, CodexType ty, SourceSpan sp) => ((Func<List<RenameEntry>, IRExpr>)((rn2) => new IrLambda(@params, rename_ir_expr(rn2, body), ty, sp)))(remove_renames_for_params(rn, @params, 0L));

    public static List<IRExpr> rename_ir_exprs(List<RenameEntry> rn, List<IRExpr> elems, long i, List<IRExpr> acc)
    {
        while (true)
        {
            if ((i == ((long)elems.Count)))
            {
            return acc;
            }
            else
            {
            var _tco_0 = rn;
            var _tco_1 = elems;
            var _tco_2 = (i + 1L);
            var _tco_3 = ((Func<List<IRExpr>>)(() => { var _l = acc; _l.Add(rename_ir_expr(rn, elems[(int)i])); return _l; }))();
            rn = _tco_0;
            elems = _tco_1;
            i = _tco_2;
            acc = _tco_3;
            continue;
            }
        }
    }

    public static List<IRFieldVal> rename_ir_fields(List<RenameEntry> rn, List<IRFieldVal> fields, long i, List<IRFieldVal> acc)
    {
        while (true)
        {
            if ((i == ((long)fields.Count)))
            {
            return acc;
            }
            else
            {
            var f = fields[(int)i];
            var _tco_0 = rn;
            var _tco_1 = fields;
            var _tco_2 = (i + 1L);
            var _tco_3 = ((Func<List<IRFieldVal>>)(() => { var _l = acc; _l.Add(new IRFieldVal(name: f.name, value: rename_ir_expr(rn, f.value), span: f.span)); return _l; }))();
            rn = _tco_0;
            fields = _tco_1;
            i = _tco_2;
            acc = _tco_3;
            continue;
            }
        }
    }

    public static List<IRBranch> rename_ir_branches(List<RenameEntry> rn, List<IRBranch> branches, long i, List<IRBranch> acc)
    {
        while (true)
        {
            if ((i == ((long)branches.Count)))
            {
            return acc;
            }
            else
            {
            var b = branches[(int)i];
            var rn2 = remove_renames_for_pat(rn, b.pattern);
            var _tco_0 = rn;
            var _tco_1 = branches;
            var _tco_2 = (i + 1L);
            var _tco_3 = ((Func<List<IRBranch>>)(() => { var _l = acc; _l.Add(new IRBranch(pattern: b.pattern, body: rename_ir_expr(rn2, b.body), span: b.span)); return _l; }))();
            rn = _tco_0;
            branches = _tco_1;
            i = _tco_2;
            acc = _tco_3;
            continue;
            }
        }
    }

    public static List<RenameEntry> remove_renames_for_pat(List<RenameEntry> rn, IRPat p) => p switch { IrVarPat(var nm, var ty, var sp) => (List<RenameEntry>)(remove_rename(rn, nm)), IrCtorPat(var nm, var subs, var ty, var sp) => (List<RenameEntry>)(remove_renames_for_pats(rn, subs, 0L)), IrLitPat(var v, var ty, var sp) => (List<RenameEntry>)(rn), IrWildPat(var sp) => (List<RenameEntry>)(rn), _ => throw new InvalidOperationException("Non-exhaustive match"), };

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
            var _tco_2 = (i + 1L);
            rn = _tco_0;
            pats = _tco_1;
            i = _tco_2;
            continue;
            }
        }
    }

    public static List<IRActStmt> rename_ir_act_stmts(List<RenameEntry> rn, List<IRActStmt> stmts, long i, List<IRActStmt> acc)
    {
        while (true)
        {
            if ((i == ((long)stmts.Count)))
            {
            return acc;
            }
            else
            {
            var stmt = stmts[(int)i];
            var _tco_s = stmt;
            if (_tco_s is IrDoBind _tco_m0)
            {
                var nm = _tco_m0.Field0;
                var ty = _tco_m0.Field1;
                var expr = _tco_m0.Field2;
                var sp = _tco_m0.Field3;
            var rn2 = remove_rename(rn, nm);
            var _tco_0 = rn2;
            var _tco_1 = stmts;
            var _tco_2 = (i + 1L);
            var _tco_3 = ((Func<List<IRActStmt>>)(() => { var _l = acc; _l.Add(new IrDoBind(nm, ty, rename_ir_expr(rn, expr), sp)); return _l; }))();
            rn = _tco_0;
            stmts = _tco_1;
            i = _tco_2;
            acc = _tco_3;
            continue;
            }
            else if (_tco_s is IrDoExec _tco_m1)
            {
                var expr = _tco_m1.Field0;
                var sp = _tco_m1.Field1;
            var _tco_0 = rn;
            var _tco_1 = stmts;
            var _tco_2 = (i + 1L);
            var _tco_3 = ((Func<List<IRActStmt>>)(() => { var _l = acc; _l.Add(new IrDoExec(rename_ir_expr(rn, expr), sp)); return _l; }))();
            rn = _tco_0;
            stmts = _tco_1;
            i = _tco_2;
            acc = _tco_3;
            continue;
            }
            }
        }
    }

    public static List<IRHandleClause> rename_ir_handle_clauses(List<RenameEntry> rn, List<IRHandleClause> clauses, long i, List<IRHandleClause> acc)
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
            var rn2 = remove_rename(rn, c.resume_name);
            var _tco_0 = rn;
            var _tco_1 = clauses;
            var _tco_2 = (i + 1L);
            var _tco_3 = ((Func<List<IRHandleClause>>)(() => { var _l = acc; _l.Add(new IRHandleClause(op_name: c.op_name, resume_name: c.resume_name, body: rename_ir_expr(rn2, c.body), span: c.span)); return _l; }))();
            rn = _tco_0;
            clauses = _tco_1;
            i = _tco_2;
            acc = _tco_3;
            continue;
            }
        }
    }

    public static List<ChapterAssignment> build_all_assignments(List<DefHeader> headers, long i, List<ChapterAssignment> acc)
    {
        while (true)
        {
            if ((i == ((long)headers.Count)))
            {
            return acc;
            }
            else
            {
            var hdr = headers[(int)i];
            var _tco_0 = headers;
            var _tco_1 = (i + 1L);
            var _tco_2 = ((Func<List<ChapterAssignment>>)(() => { var _l = acc; _l.Add(new ChapterAssignment(def_name: hdr.name.text, chapter_slug: hdr.chapter_slug)); return _l; }))();
            headers = _tco_0;
            i = _tco_1;
            acc = _tco_2;
            continue;
            }
        }
    }

    public static string scope_def_name(List<string> colliding, List<ChapterAssignment> assignments, string name, string cur_chap) => (is_colliding(colliding, name) ? mangle_name(cur_chap, name) : name);

    public static List<RenameEntry> build_chapter_rename_map(List<string> colliding, List<ChapterAssignment> assignments, string cur_chap) => build_mod_renames(colliding, assignments, cur_chap, 0L, ((long)assignments.Count), new List<RenameEntry>());

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
            if ((a.chapter_slug == cur_chap))
            {
            var cleaned = remove_rename(acc, a.def_name);
            var _tco_0 = colliding;
            var _tco_1 = assignments;
            var _tco_2 = cur_chap;
            var _tco_3 = (i + 1L);
            var _tco_4 = len;
            var _tco_5 = ((Func<List<RenameEntry>>)(() => { var _l = cleaned; _l.Add(new RenameEntry(original: a.def_name, mangled: mangle_name(cur_chap, a.def_name))); return _l; }))();
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
            if (has_rename(acc, a.def_name))
            {
            var _tco_0 = colliding;
            var _tco_1 = assignments;
            var _tco_2 = cur_chap;
            var _tco_3 = (i + 1L);
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
            var _tco_0 = colliding;
            var _tco_1 = assignments;
            var _tco_2 = cur_chap;
            var _tco_3 = (i + 1L);
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
            var _tco_3 = (i + 1L);
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

    public static bool has_rename(List<RenameEntry> entries, string name) => has_rename_loop(entries, name, 0L, ((long)entries.Count));

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
            var _tco_2 = (i + 1L);
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

    public static IRChapter scope_ir_chapter(IRChapter ir, List<string> colliding, List<ChapterAssignment> assignments) => new IRChapter(name: ir.name, defs: scope_ir_defs(ir.defs, colliding, assignments, "", new List<RenameEntry>(), 0L, new List<IRDef>()), chapter_title: ir.chapter_title, prose: ir.prose, section_titles: ir.section_titles, span: ir.span);

    public static List<IRDef> scope_ir_defs(List<IRDef> defs, List<string> colliding, List<ChapterAssignment> assignments, string cur_slug, List<RenameEntry> cur_rn, long i, List<IRDef> acc)
    {
        while (true)
        {
            if ((i == ((long)defs.Count)))
            {
            return acc;
            }
            else
            {
            var d = defs[(int)i];
            var rn = ((d.chapter_slug == cur_slug) ? cur_rn : build_chapter_rename_map(colliding, assignments, d.chapter_slug));
            var new_name = rename_lookup(rn, d.name);
            var new_body = rename_ir_expr(rn, d.body);
            var _tco_0 = defs;
            var _tco_1 = colliding;
            var _tco_2 = assignments;
            var _tco_3 = d.chapter_slug;
            var _tco_4 = rn;
            var _tco_5 = (i + 1L);
            var _tco_6 = ((Func<List<IRDef>>)(() => { var _l = acc; _l.Add(new IRDef(name: new_name, @params: d.@params, type_val: d.type_val, body: new_body, chapter_slug: d.chapter_slug, span: d.span)); return _l; }))();
            defs = _tco_0;
            colliding = _tco_1;
            assignments = _tco_2;
            cur_slug = _tco_3;
            cur_rn = _tco_4;
            i = _tco_5;
            acc = _tco_6;
            continue;
            }
        }
    }

    public static AExpr rename_aexpr(List<RenameEntry> rn, AExpr e) => e switch { ALitExpr(var v, var k, var s) => (AExpr)(e), AErrorExpr(var msg, var s) => (AExpr)(e), ANameExpr(var n, var s) => (AExpr)(new ANameExpr(make_name(rename_lookup(rn, n.value)), s)), ABinaryExpr(var l, var op, var r, var s) => (AExpr)(new ABinaryExpr(rename_aexpr(rn, l), op, rename_aexpr(rn, r), s)), AUnaryExpr(var x, var s) => (AExpr)(new AUnaryExpr(rename_aexpr(rn, x), s)), AApplyExpr(var f, var a, var s) => (AExpr)(new AApplyExpr(rename_aexpr(rn, f), rename_aexpr(rn, a), s)), AIfExpr(var c, var t, var el, var s) => (AExpr)(new AIfExpr(rename_aexpr(rn, c), rename_aexpr(rn, t), rename_aexpr(rn, el), s)), ALetExpr(var binds, var body, var s) => (AExpr)(rename_alet(rn, binds, body, s)), ALambdaExpr(var @params, var body, var s) => (AExpr)(rename_alambda(rn, @params, body, s)), AMatchExpr(var scrut, var arms, var s) => (AExpr)(new AMatchExpr(rename_aexpr(rn, scrut), rename_amatch_arms(rn, arms, 0L, new List<AMatchArm>()), s)), AListExpr(var elems, var s) => (AExpr)(new AListExpr(rename_aexprs(rn, elems, 0L, new List<AExpr>()), s)), ARecordExpr(var name, var fields, var s) => (AExpr)(new ARecordExpr(name, rename_afields(rn, fields, 0L, new List<AFieldExpr>()), s)), AFieldAccess(var rec, var field, var s) => (AExpr)(new AFieldAccess(rename_aexpr(rn, rec), field, s)), AActExpr(var stmts, var s) => (AExpr)(new AActExpr(rename_act_stmts(rn, stmts, 0L, new List<AActStmt>()), s)), AHandleExpr(var eff, var body, var clauses, var s) => (AExpr)(new AHandleExpr(eff, rename_aexpr(rn, body), rename_ahandle_clauses(rn, clauses, 0L, new List<AHandleClause>()), s)), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static AExpr rename_alet(List<RenameEntry> rn, List<ALetBind> binds, AExpr body, SourceSpan s) => ((Func<List<ALetBind>, AExpr>)((new_binds) => ((Func<List<RenameEntry>, AExpr>)((rn2) => new ALetExpr(new_binds, rename_aexpr(rn2, body), s)))(remove_renames_for_let_binds(rn, binds, 0L))))(rename_alet_binds(rn, binds, 0L, new List<ALetBind>()));

    public static List<ALetBind> rename_alet_binds(List<RenameEntry> rn, List<ALetBind> binds, long i, List<ALetBind> acc)
    {
        while (true)
        {
            if ((i == ((long)binds.Count)))
            {
            return acc;
            }
            else
            {
            var b = binds[(int)i];
            var _tco_0 = rn;
            var _tco_1 = binds;
            var _tco_2 = (i + 1L);
            var _tco_3 = ((Func<List<ALetBind>>)(() => { var _l = acc; _l.Add(new ALetBind(name: b.name, value: rename_aexpr(rn, b.value), span: b.span)); return _l; }))();
            rn = _tco_0;
            binds = _tco_1;
            i = _tco_2;
            acc = _tco_3;
            continue;
            }
        }
    }

    public static List<RenameEntry> remove_renames_for_let_binds(List<RenameEntry> rn, List<ALetBind> binds, long i)
    {
        while (true)
        {
            if ((i == ((long)binds.Count)))
            {
            return rn;
            }
            else
            {
            var _tco_0 = remove_rename(rn, binds[(int)i].name.value);
            var _tco_1 = binds;
            var _tco_2 = (i + 1L);
            rn = _tco_0;
            binds = _tco_1;
            i = _tco_2;
            continue;
            }
        }
    }

    public static AExpr rename_alambda(List<RenameEntry> rn, List<Name> @params, AExpr body, SourceSpan s) => ((Func<List<RenameEntry>, AExpr>)((rn2) => new ALambdaExpr(@params, rename_aexpr(rn2, body), s)))(remove_renames_for_names(rn, @params, 0L));

    public static List<RenameEntry> remove_renames_for_names(List<RenameEntry> rn, List<Name> names, long i)
    {
        while (true)
        {
            if ((i == ((long)names.Count)))
            {
            return rn;
            }
            else
            {
            var _tco_0 = remove_rename(rn, names[(int)i].value);
            var _tco_1 = names;
            var _tco_2 = (i + 1L);
            rn = _tco_0;
            names = _tco_1;
            i = _tco_2;
            continue;
            }
        }
    }

    public static List<AExpr> rename_aexprs(List<RenameEntry> rn, List<AExpr> elems, long i, List<AExpr> acc)
    {
        while (true)
        {
            if ((i == ((long)elems.Count)))
            {
            return acc;
            }
            else
            {
            var _tco_0 = rn;
            var _tco_1 = elems;
            var _tco_2 = (i + 1L);
            var _tco_3 = ((Func<List<AExpr>>)(() => { var _l = acc; _l.Add(rename_aexpr(rn, elems[(int)i])); return _l; }))();
            rn = _tco_0;
            elems = _tco_1;
            i = _tco_2;
            acc = _tco_3;
            continue;
            }
        }
    }

    public static List<AFieldExpr> rename_afields(List<RenameEntry> rn, List<AFieldExpr> fields, long i, List<AFieldExpr> acc)
    {
        while (true)
        {
            if ((i == ((long)fields.Count)))
            {
            return acc;
            }
            else
            {
            var f = fields[(int)i];
            var _tco_0 = rn;
            var _tco_1 = fields;
            var _tco_2 = (i + 1L);
            var _tco_3 = ((Func<List<AFieldExpr>>)(() => { var _l = acc; _l.Add(new AFieldExpr(name: f.name, value: rename_aexpr(rn, f.value), span: f.span)); return _l; }))();
            rn = _tco_0;
            fields = _tco_1;
            i = _tco_2;
            acc = _tco_3;
            continue;
            }
        }
    }

    public static List<AMatchArm> rename_amatch_arms(List<RenameEntry> rn, List<AMatchArm> arms, long i, List<AMatchArm> acc)
    {
        while (true)
        {
            if ((i == ((long)arms.Count)))
            {
            return acc;
            }
            else
            {
            var a = arms[(int)i];
            var rn2 = remove_renames_for_apat(rn, a.pattern);
            var _tco_0 = rn;
            var _tco_1 = arms;
            var _tco_2 = (i + 1L);
            var _tco_3 = ((Func<List<AMatchArm>>)(() => { var _l = acc; _l.Add(new AMatchArm(pattern: a.pattern, body: rename_aexpr(rn2, a.body), span: a.span)); return _l; }))();
            rn = _tco_0;
            arms = _tco_1;
            i = _tco_2;
            acc = _tco_3;
            continue;
            }
        }
    }

    public static List<RenameEntry> remove_renames_for_apat(List<RenameEntry> rn, APat p) => p switch { AVarPat(var n, var s) => (List<RenameEntry>)(remove_rename(rn, n.value)), ACtorPat(var n, var subs, var s) => (List<RenameEntry>)(remove_renames_for_apats(rn, subs, 0L)), ALitPat(var v, var k, var s) => (List<RenameEntry>)(rn), AWildPat(var s) => (List<RenameEntry>)(rn), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static List<RenameEntry> remove_renames_for_apats(List<RenameEntry> rn, List<APat> pats, long i)
    {
        while (true)
        {
            if ((i == ((long)pats.Count)))
            {
            return rn;
            }
            else
            {
            var _tco_0 = remove_renames_for_apat(rn, pats[(int)i]);
            var _tco_1 = pats;
            var _tco_2 = (i + 1L);
            rn = _tco_0;
            pats = _tco_1;
            i = _tco_2;
            continue;
            }
        }
    }

    public static List<AActStmt> rename_act_stmts(List<RenameEntry> rn, List<AActStmt> stmts, long i, List<AActStmt> acc)
    {
        while (true)
        {
            if ((i == ((long)stmts.Count)))
            {
            return acc;
            }
            else
            {
            var s = stmts[(int)i];
            var _tco_s = s;
            if (_tco_s is AActBindStmt _tco_m0)
            {
                var nm = _tco_m0.Field0;
                var expr = _tco_m0.Field1;
                var sp = _tco_m0.Field2;
            var rn2 = remove_rename(rn, nm.value);
            var _tco_0 = rn2;
            var _tco_1 = stmts;
            var _tco_2 = (i + 1L);
            var _tco_3 = ((Func<List<AActStmt>>)(() => { var _l = acc; _l.Add(new AActBindStmt(nm, rename_aexpr(rn, expr), sp)); return _l; }))();
            rn = _tco_0;
            stmts = _tco_1;
            i = _tco_2;
            acc = _tco_3;
            continue;
            }
            else if (_tco_s is AActExprStmt _tco_m1)
            {
                var expr = _tco_m1.Field0;
                var sp = _tco_m1.Field1;
            var _tco_0 = rn;
            var _tco_1 = stmts;
            var _tco_2 = (i + 1L);
            var _tco_3 = ((Func<List<AActStmt>>)(() => { var _l = acc; _l.Add(new AActExprStmt(rename_aexpr(rn, expr), sp)); return _l; }))();
            rn = _tco_0;
            stmts = _tco_1;
            i = _tco_2;
            acc = _tco_3;
            continue;
            }
            }
        }
    }

    public static List<AHandleClause> rename_ahandle_clauses(List<RenameEntry> rn, List<AHandleClause> clauses, long i, List<AHandleClause> acc)
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
            var rn2 = remove_rename(rn, c.resume_name.value);
            var _tco_0 = rn;
            var _tco_1 = clauses;
            var _tco_2 = (i + 1L);
            var _tco_3 = ((Func<List<AHandleClause>>)(() => { var _l = acc; _l.Add(new AHandleClause(op_name: c.op_name, resume_name: c.resume_name, body: rename_aexpr(rn2, c.body), span: c.span)); return _l; }))();
            rn = _tco_0;
            clauses = _tco_1;
            i = _tco_2;
            acc = _tco_3;
            continue;
            }
        }
    }

    public static bool cite_selects_name(ACitesDecl c, string name) => cite_selects_name_loop(c.selected_names, name, 0L, ((long)c.selected_names.Count));

    public static bool cite_selects_name_loop(List<Name> names, string target, long j, long len)
    {
        while (true)
        {
            if ((j == len))
            {
            return false;
            }
            else
            {
            if ((names[(int)j].value == target))
            {
            return true;
            }
            else
            {
            var _tco_0 = names;
            var _tco_1 = target;
            var _tco_2 = (j + 1L);
            var _tco_3 = len;
            names = _tco_0;
            target = _tco_1;
            j = _tco_2;
            len = _tco_3;
            continue;
            }
            }
        }
    }

    public static string find_cite_conflict(List<ACitesDecl> citations, string citing, string name, long k, long total)
    {
        while (true)
        {
            if ((k == total))
            {
            return "";
            }
            else
            {
            var c = citations[(int)k];
            if ((c.citing_chapter == citing))
            {
            if (cite_selects_name(c, name))
            {
            return c.chapter_name.value;
            }
            else
            {
            var _tco_0 = citations;
            var _tco_1 = citing;
            var _tco_2 = name;
            var _tco_3 = (k + 1L);
            var _tco_4 = total;
            citations = _tco_0;
            citing = _tco_1;
            name = _tco_2;
            k = _tco_3;
            total = _tco_4;
            continue;
            }
            }
            else
            {
            var _tco_0 = citations;
            var _tco_1 = citing;
            var _tco_2 = name;
            var _tco_3 = (k + 1L);
            var _tco_4 = total;
            citations = _tco_0;
            citing = _tco_1;
            name = _tco_2;
            k = _tco_3;
            total = _tco_4;
            continue;
            }
            }
        }
    }

    public static DiagnosticBag add_duplicate_cite_error(DiagnosticBag bag, ACitesDecl cite, string name_val, string conflict) => bag_add(bag, make_error(cdx_duplicate_cite(), ("\u002C\u000F\u001A\u000D\u0002\u0047" + (name_val + ("\u0047\u0002\u0011\u0013\u0002\u0018\u0011\u000E\u000D\u0016\u0002\u001C\u0015\u0010\u001A\u0002\u0020\u0010\u000E\u0014\u0002\u0047" + (cite.chapter_name.value + ("\u0047\u0002\u000F\u0012\u0016\u0002\u0047" + (conflict + ("\u0047\u0002\u0011\u0012\u0002\u0018\u0014\u000F\u001F\u000E\u000D\u0015\u0002\u0047" + (cite.citing_chapter + "\u0047")))))))), cite.span));

    public static DiagnosticBag check_cite_names(ACitesDecl cite, long j, long nlen, List<ACitesDecl> all_citations, long i, long total, DiagnosticBag bag)
    {
        while (true)
        {
            if ((j == nlen))
            {
            return bag;
            }
            else
            {
            var name = cite.selected_names[(int)j];
            var conflict = find_cite_conflict(all_citations, cite.citing_chapter, name.value, (i + 1L), total);
            var bag2 = ((conflict == "") ? bag : add_duplicate_cite_error(bag, cite, name.value, conflict));
            var _tco_0 = cite;
            var _tco_1 = (j + 1L);
            var _tco_2 = nlen;
            var _tco_3 = all_citations;
            var _tco_4 = i;
            var _tco_5 = total;
            var _tco_6 = bag2;
            cite = _tco_0;
            j = _tco_1;
            nlen = _tco_2;
            all_citations = _tco_3;
            i = _tco_4;
            total = _tco_5;
            bag = _tco_6;
            continue;
            }
        }
    }

    public static DiagnosticBag check_duplicate_cites_loop(List<ACitesDecl> citations, long i, long len, DiagnosticBag bag)
    {
        while (true)
        {
            if ((i == len))
            {
            return bag;
            }
            else
            {
            var cite_i = citations[(int)i];
            var bag2 = check_cite_names(cite_i, 0L, ((long)cite_i.selected_names.Count), citations, i, len, bag);
            var _tco_0 = citations;
            var _tco_1 = (i + 1L);
            var _tco_2 = len;
            var _tco_3 = bag2;
            citations = _tco_0;
            i = _tco_1;
            len = _tco_2;
            bag = _tco_3;
            continue;
            }
        }
    }

    public static DiagnosticBag check_duplicate_cites(List<ACitesDecl> citations) => check_duplicate_cites_loop(citations, 0L, ((long)citations.Count), empty_bag());

    public static AChapter scope_achapter(AChapter ast, List<string> colliding, List<ChapterAssignment> assignments) => new AChapter(name: ast.name, defs: scope_adefs(ast.defs, colliding, assignments, ast.citations, "", new List<RenameEntry>(), 0L, new List<ADef>()), type_defs: ast.type_defs, effect_defs: ast.effect_defs, citations: ast.citations, chapter_title: ast.chapter_title, prose: ast.prose, section_titles: ast.section_titles, span: ast.span);

    public static List<ADef> scope_adefs(List<ADef> defs, List<string> colliding, List<ChapterAssignment> assignments, List<ACitesDecl> citations, string cur_slug, List<RenameEntry> cur_rn, long i, List<ADef> acc)
    {
        while (true)
        {
            if ((i == ((long)defs.Count)))
            {
            return acc;
            }
            else
            {
            var d = defs[(int)i];
            var rn = ((d.chapter_slug == cur_slug) ? cur_rn : apply_cite_overrides(build_chapter_rename_map(colliding, assignments, d.chapter_slug), citations, colliding, assignments, d.chapter_slug, 0L));
            var new_name = make_name(rename_lookup(rn, d.name.value));
            var new_body = rename_aexpr(rn, d.body);
            var _tco_0 = defs;
            var _tco_1 = colliding;
            var _tco_2 = assignments;
            var _tco_3 = citations;
            var _tco_4 = d.chapter_slug;
            var _tco_5 = rn;
            var _tco_6 = (i + 1L);
            var _tco_7 = ((Func<List<ADef>>)(() => { var _l = acc; _l.Add(new ADef(name: new_name, @params: d.@params, declared_type: d.declared_type, body: new_body, chapter_slug: d.chapter_slug, span: d.span)); return _l; }))();
            defs = _tco_0;
            colliding = _tco_1;
            assignments = _tco_2;
            citations = _tco_3;
            cur_slug = _tco_4;
            cur_rn = _tco_5;
            i = _tco_6;
            acc = _tco_7;
            continue;
            }
        }
    }

    public static List<RenameEntry> apply_cite_overrides(List<RenameEntry> rn, List<ACitesDecl> citations, List<string> colliding, List<ChapterAssignment> assignments, string cur_slug, long i)
    {
        while (true)
        {
            if ((i == ((long)citations.Count)))
            {
            return rn;
            }
            else
            {
            var cite = citations[(int)i];
            var full_cite_name = (cite.quire.value + ("\u0049\u0049" + cite.chapter_name.value));
            var cite_slug = find_slug_for_cite_name(full_cite_name, assignments, 0L);
            var rn2 = apply_cite_selected(rn, cite.selected_names, cite_slug, colliding, assignments, cur_slug, 0L);
            var _tco_0 = rn2;
            var _tco_1 = citations;
            var _tco_2 = colliding;
            var _tco_3 = assignments;
            var _tco_4 = cur_slug;
            var _tco_5 = (i + 1L);
            rn = _tco_0;
            citations = _tco_1;
            colliding = _tco_2;
            assignments = _tco_3;
            cur_slug = _tco_4;
            i = _tco_5;
            continue;
            }
        }
    }

    public static string find_slug_for_cite_name(string cite_name, List<ChapterAssignment> assignments, long i)
    {
        while (true)
        {
            if ((i == ((long)assignments.Count)))
            {
            return "";
            }
            else
            {
            var a = assignments[(int)i];
            if (slug_matches_cite(a.chapter_slug, cite_name))
            {
            return a.chapter_slug;
            }
            else
            {
            var _tco_0 = cite_name;
            var _tco_1 = assignments;
            var _tco_2 = (i + 1L);
            cite_name = _tco_0;
            assignments = _tco_1;
            i = _tco_2;
            continue;
            }
            }
        }
    }

    public static bool slug_matches_cite(string slug, string cite_name) => (slugify(slug) == slugify(cite_name));

    public static List<RenameEntry> apply_cite_selected(List<RenameEntry> rn, List<Name> names, string cite_slug, List<string> colliding, List<ChapterAssignment> assignments, string cur_slug, long i)
    {
        while (true)
        {
            if ((i == ((long)names.Count)))
            {
            return rn;
            }
            else
            {
            var n = names[(int)i].value;
            if (is_colliding(colliding, n))
            {
            if (chapter_defines_name(assignments, cur_slug, n))
            {
            var _tco_0 = rn;
            var _tco_1 = names;
            var _tco_2 = cite_slug;
            var _tco_3 = colliding;
            var _tco_4 = assignments;
            var _tco_5 = cur_slug;
            var _tco_6 = (i + 1L);
            rn = _tco_0;
            names = _tco_1;
            cite_slug = _tco_2;
            colliding = _tco_3;
            assignments = _tco_4;
            cur_slug = _tco_5;
            i = _tco_6;
            continue;
            }
            else
            {
            var cleaned = remove_rename(rn, n);
            var _tco_0 = ((Func<List<RenameEntry>>)(() => { var _l = cleaned; _l.Add(new RenameEntry(original: n, mangled: mangle_name(cite_slug, n))); return _l; }))();
            var _tco_1 = names;
            var _tco_2 = cite_slug;
            var _tco_3 = colliding;
            var _tco_4 = assignments;
            var _tco_5 = cur_slug;
            var _tco_6 = (i + 1L);
            rn = _tco_0;
            names = _tco_1;
            cite_slug = _tco_2;
            colliding = _tco_3;
            assignments = _tco_4;
            cur_slug = _tco_5;
            i = _tco_6;
            continue;
            }
            }
            else
            {
            var _tco_0 = rn;
            var _tco_1 = names;
            var _tco_2 = cite_slug;
            var _tco_3 = colliding;
            var _tco_4 = assignments;
            var _tco_5 = cur_slug;
            var _tco_6 = (i + 1L);
            rn = _tco_0;
            names = _tco_1;
            cite_slug = _tco_2;
            colliding = _tco_3;
            assignments = _tco_4;
            cur_slug = _tco_5;
            i = _tco_6;
            continue;
            }
            }
        }
    }

    public static bool chapter_defines_name(List<ChapterAssignment> assignments, string slug, string name) => chapter_defines_loop(assignments, slug, name, 0L, ((long)assignments.Count));

    public static bool chapter_defines_loop(List<ChapterAssignment> assignments, string slug, string name, long i, long len)
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
            if ((a.chapter_slug == slug))
            {
            return true;
            }
            else
            {
            var _tco_0 = assignments;
            var _tco_1 = slug;
            var _tco_2 = name;
            var _tco_3 = (i + 1L);
            var _tco_4 = len;
            assignments = _tco_0;
            slug = _tco_1;
            name = _tco_2;
            i = _tco_3;
            len = _tco_4;
            continue;
            }
            }
            else
            {
            var _tco_0 = assignments;
            var _tco_1 = slug;
            var _tco_2 = name;
            var _tco_3 = (i + 1L);
            var _tco_4 = len;
            assignments = _tco_0;
            slug = _tco_1;
            name = _tco_2;
            i = _tco_3;
            len = _tco_4;
            continue;
            }
            }
        }
    }

    public static Scope empty_scope() => new Scope(names: new List<string>());

    public static bool scope_has(Scope sc, string name) => ((Func<long, bool>)((len) => ((len == 0L) ? false : ((Func<long, bool>)((pos) => ((pos >= len) ? false : (sc.names[(int)pos] == name))))(bsearch_text_set(sc.names, name, 0L, len)))))(((long)sc.names.Count));

    public static Scope scope_add(Scope sc, string name) => ((Func<long, Scope>)((len) => ((Func<long, Scope>)((pos) => new Scope(names: ((Func<List<string>>)(() => { var _l = new List<string>(sc.names); _l.Insert((int)pos, name); return _l; }))())))(bsearch_text_set(sc.names, name, 0L, len))))(((long)sc.names.Count));

    public static List<string> builtin_names() => new List<string> { "\u0013\u0014\u0010\u001B", "\u0012\u000D\u001D\u000F\u000E\u000D", "\u0028\u0015\u0019\u000D", "\u0036\u000F\u0017\u0013\u000D", "\u002C\u0010\u000E\u0014\u0011\u0012\u001D", "\u001F\u0015\u0011\u0012\u000E\u0049\u0017\u0011\u0012\u000D", "\u0015\u000D\u000F\u0016\u0049\u0017\u0011\u0012\u000D", "\u0015\u000D\u000F\u0016\u0049\u001C\u0011\u0017\u000D", "\u001B\u0015\u0011\u000E\u000D\u0049\u001C\u0011\u0017\u000D", "\u001B\u0015\u0011\u000E\u000D\u0049\u0020\u0011\u0012\u000F\u0015\u001E", "\u001C\u0011\u0017\u000D\u0049\u000D\u0024\u0011\u0013\u000E\u0013", "\u0017\u0011\u0013\u000E\u0049\u001C\u0011\u0017\u000D\u0013", "\u0010\u001F\u000D\u0012\u0049\u001C\u0011\u0017\u000D", "\u0015\u000D\u000F\u0016\u0049\u000F\u0017\u0017", "\u0018\u0017\u0010\u0013\u000D\u0049\u001C\u0011\u0017\u000D", "\u0018\u0014\u000F\u0015\u0049\u000F\u000E", "\u0018\u0014\u000F\u0015\u0049\u000E\u0010\u0049\u000E\u000D\u0024\u000E", "\u000E\u000D\u0024\u000E\u0049\u0017\u000D\u0012\u001D\u000E\u0014", "\u0013\u0019\u0020\u0013\u000E\u0015\u0011\u0012\u001D", "\u0011\u0013\u0049\u0017\u000D\u000E\u000E\u000D\u0015", "\u0011\u0013\u0049\u0016\u0011\u001D\u0011\u000E", "\u0011\u0013\u0049\u001B\u0014\u0011\u000E\u000D\u0013\u001F\u000F\u0018\u000D", "\u000E\u000D\u0024\u000E\u0049\u000E\u0010\u0049\u0011\u0012\u000E\u000D\u001D\u000D\u0015", "\u0011\u0012\u000E\u000D\u001D\u000D\u0015\u0049\u000E\u0010\u0049\u000E\u000D\u0024\u000E", "\u000E\u000D\u0024\u000E\u0049\u0015\u000D\u001F\u0017\u000F\u0018\u000D", "\u000E\u000D\u0024\u000E\u0049\u0013\u001F\u0017\u0011\u000E", "\u000E\u000D\u0024\u000E\u0049\u0018\u0010\u0012\u000E\u000F\u0011\u0012\u0013", "\u000E\u000D\u0024\u000E\u0049\u0013\u000E\u000F\u0015\u000E\u0013\u0049\u001B\u0011\u000E\u0014", "\u0018\u0014\u000F\u0015\u0049\u0018\u0010\u0016\u000D", "\u0018\u0014\u000F\u0015\u0049\u0018\u0010\u0016\u000D\u0049\u000F\u000E", "\u0018\u0010\u0016\u000D\u0049\u000E\u0010\u0049\u0018\u0014\u000F\u0015", "\u0017\u0011\u0013\u000E\u0049\u0017\u000D\u0012\u001D\u000E\u0014", "\u0017\u0011\u0013\u000E\u0049\u000F\u000E", "\u0017\u0011\u0013\u000E\u0049\u0011\u0012\u0013\u000D\u0015\u000E\u0049\u000F\u000E", "\u0017\u0011\u0013\u000E\u0049\u0013\u000D\u000E\u0049\u000F\u000E", "\u0017\u0011\u0013\u000E\u0049\u0013\u0012\u0010\u0018", "\u000E\u000D\u0024\u000E\u0049\u0018\u0010\u001A\u001F\u000F\u0015\u000D", "\u001D\u000D\u000E\u0049\u000F\u0015\u001D\u0013", "\u001D\u000D\u000E\u0049\u000D\u0012\u0021", "\u0018\u0019\u0015\u0015\u000D\u0012\u000E\u0049\u0016\u0011\u0015", "\u001A\u000F\u001F", "\u001C\u0011\u0017\u000E\u000D\u0015", "\u001C\u0010\u0017\u0016", "\u000E\u000D\u0024\u000E\u0049\u0018\u0010\u0012\u0018\u000F\u000E\u0049\u0017\u0011\u0013\u000E", "\u0015\u000D\u0018\u0010\u0015\u0016\u0049\u0013\u000D\u000E", "\u0017\u0011\u0012\u0022\u000D\u0016\u0049\u0017\u0011\u0013\u000E\u0049\u000D\u001A\u001F\u000E\u001E", "\u0017\u0011\u0012\u0022\u000D\u0016\u0049\u0017\u0011\u0013\u000E\u0049\u001F\u0019\u0013\u0014", "\u0017\u0011\u0012\u0022\u000D\u0016\u0049\u0017\u0011\u0013\u000E\u0049\u000E\u0010\u0049\u0017\u0011\u0013\u000E", "\u0020\u0011\u000E\u0049\u000F\u0012\u0016", "\u0020\u0011\u000E\u0049\u0010\u0015", "\u0020\u0011\u000E\u0049\u0024\u0010\u0015", "\u0020\u0011\u000E\u0049\u0013\u0014\u0017", "\u0020\u0011\u000E\u0049\u0013\u0014\u0015", "\u0020\u0011\u000E\u0049\u0012\u0010\u000E" };

    public static bool is_type_name(string name) => ((((long)name.Length) == 0L) ? false : ((((long)name[(int)0L]) >= 13L && ((long)name[(int)0L]) <= 64L) && is_upper_char(((long)name[(int)0L]))));

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
            var acc_len = ((long)acc.Count);
            var pos = bsearch_text_set(acc, name, 0L, acc_len);
            if ((pos < acc_len))
            {
            if ((acc[(int)pos] == name))
            {
            var _tco_0 = defs;
            var _tco_1 = (i + 1L);
            var _tco_2 = len;
            var _tco_3 = acc;
            var _tco_4 = Enumerable.Concat(errs, new List<Diagnostic> { make_error(cdx_duplicate_definition(), ("\u0030\u0019\u001F\u0017\u0011\u0018\u000F\u000E\u000D\u0002\u0016\u000D\u001C\u0011\u0012\u0011\u000E\u0011\u0010\u0012\u0045\u0002" + name), def.span) }).ToList();
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
            else
            {
            var _tco_0 = defs;
            var _tco_1 = (i + 1L);
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

    public static bool list_contains(List<string> xs, string name) => ((Func<long, bool>)((len) => ((len == 0L) ? false : ((Func<long, bool>)((pos) => ((pos >= len) ? false : (xs[(int)pos] == name))))(bsearch_text_set(xs, name, 0L, len)))))(((long)xs.Count));

    public static CtorCollectResult semantics__name_resolution_collect_ctor_names(List<ATypeDef> type_defs, long i, long len, List<string> type_acc, List<string> ctor_acc)
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
                var s = _tco_m0.Field3;
            var new_type_acc = ((Func<List<string>>)(() => { var _l = type_acc; _l.Add(name.value); return _l; }))();
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
                var s = _tco_m1.Field3;
            var _tco_0 = type_defs;
            var _tco_1 = (i + 1L);
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
            var _tco_1 = (i + 1L);
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

    public static Scope build_all_names_scope(List<string> top_names, List<string> ctor_names, List<string> builtins) => ((Func<Scope, Scope>)((sc) => ((Func<Scope, Scope>)((sc2) => add_names_to_scope(sc2, builtins, 0L, ((long)builtins.Count))))(add_names_to_scope(sc, ctor_names, 0L, ((long)ctor_names.Count)))))(add_names_to_scope(empty_scope(), top_names, 0L, ((long)top_names.Count)));

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
                var s = _tco_m0.Field2;
            return new List<Diagnostic>();
            }
            else if (_tco_s is ANameExpr _tco_m1)
            {
                var name = _tco_m1.Field0;
                var s = _tco_m1.Field1;
            if ((scope_has(sc, name.value) || is_type_name(name.value)))
            {
            return new List<Diagnostic>();
            }
            else
            {
            return new List<Diagnostic> { make_error(cdx_undefined_name(), ("\u0033\u0012\u0016\u000D\u001C\u0011\u0012\u000D\u0016\u0002\u0012\u000F\u001A\u000D\u0045\u0002" + name.value), s) };
            }
            }
            else if (_tco_s is ABinaryExpr _tco_m2)
            {
                var left = _tco_m2.Field0;
                var op = _tco_m2.Field1;
                var right = _tco_m2.Field2;
                var s = _tco_m2.Field3;
            return Enumerable.Concat(resolve_expr(sc, left), resolve_expr(sc, right)).ToList();
            }
            else if (_tco_s is AUnaryExpr _tco_m3)
            {
                var operand = _tco_m3.Field0;
                var s = _tco_m3.Field1;
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
                var s = _tco_m4.Field2;
            return Enumerable.Concat(resolve_expr(sc, func), resolve_expr(sc, arg)).ToList();
            }
            else if (_tco_s is AIfExpr _tco_m5)
            {
                var cond = _tco_m5.Field0;
                var then_e = _tco_m5.Field1;
                var else_e = _tco_m5.Field2;
                var s = _tco_m5.Field3;
            return Enumerable.Concat(resolve_expr(sc, cond), Enumerable.Concat(resolve_expr(sc, then_e), resolve_expr(sc, else_e)).ToList()).ToList();
            }
            else if (_tco_s is ALetExpr _tco_m6)
            {
                var bindings = _tco_m6.Field0;
                var body = _tco_m6.Field1;
                var s = _tco_m6.Field2;
            return resolve_let(sc, bindings, body, 0L, ((long)bindings.Count), new List<Diagnostic>());
            }
            else if (_tco_s is ALambdaExpr _tco_m7)
            {
                var @params = _tco_m7.Field0;
                var body = _tco_m7.Field1;
                var s = _tco_m7.Field2;
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
                var s = _tco_m8.Field2;
            return Enumerable.Concat(resolve_expr(sc, scrutinee), resolve_match_arms(sc, arms, 0L, ((long)arms.Count), new List<Diagnostic>())).ToList();
            }
            else if (_tco_s is AListExpr _tco_m9)
            {
                var elems = _tco_m9.Field0;
                var s = _tco_m9.Field1;
            return resolve_list_elems(sc, elems, 0L, ((long)elems.Count), new List<Diagnostic>());
            }
            else if (_tco_s is ARecordExpr _tco_m10)
            {
                var name = _tco_m10.Field0;
                var fields = _tco_m10.Field1;
                var s = _tco_m10.Field2;
            return resolve_record_fields(sc, fields, 0L, ((long)fields.Count), new List<Diagnostic>());
            }
            else if (_tco_s is AFieldAccess _tco_m11)
            {
                var obj = _tco_m11.Field0;
                var field = _tco_m11.Field1;
                var s = _tco_m11.Field2;
            var _tco_0 = sc;
            var _tco_1 = obj;
            sc = _tco_0;
            expr = _tco_1;
            continue;
            }
            else if (_tco_s is AActExpr _tco_m12)
            {
                var stmts = _tco_m12.Field0;
                var s = _tco_m12.Field1;
            return resolve_act_stmts(sc, stmts, 0L, ((long)stmts.Count), new List<Diagnostic>());
            }
            else if (_tco_s is AErrorExpr _tco_m13)
            {
                var msg = _tco_m13.Field0;
                var s = _tco_m13.Field1;
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

    public static Scope collect_pattern_names(Scope sc, APat pat) => pat switch { AVarPat(var name, var s) => (Scope)(scope_add(sc, name.value)), ACtorPat(var name, var subs, var s) => (Scope)(collect_ctor_pat_names(sc, subs, 0L, ((long)subs.Count))), ALitPat(var val, var kind, var s) => (Scope)(sc), AWildPat(var s) => (Scope)(sc), _ => throw new InvalidOperationException("Non-exhaustive match"), };

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

    public static List<Diagnostic> resolve_act_stmts(Scope sc, List<AActStmt> stmts, long i, long len, List<Diagnostic> errs)
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
            if (_tco_s is AActExprStmt _tco_m0)
            {
                var e = _tco_m0.Field0;
                var s = _tco_m0.Field1;
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
            else if (_tco_s is AActBindStmt _tco_m1)
            {
                var name = _tco_m1.Field0;
                var e = _tco_m1.Field1;
                var s = _tco_m1.Field2;
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

    public static ResolveResult resolve_chapter(AChapter mod) => resolve_chapter_with_citations(mod, new List<ResolveResult>());

    public static ResolveResult resolve_chapter_with_citations(AChapter mod, List<ResolveResult> imported) => ((Func<CollectResult, ResolveResult>)((top) => ((Func<CtorCollectResult, ResolveResult>)((ctors) => ((Func<List<string>, ResolveResult>)((cited_names) => ((Func<List<string>, ResolveResult>)((all_top) => ((Func<Scope, ResolveResult>)((sc) => ((Func<List<Diagnostic>, ResolveResult>)((expr_errs) => new ResolveResult(bag: bag_from_list(Enumerable.Concat(top.errors, expr_errs).ToList()), top_level_names: top.names, type_names: ctors.type_names, ctor_names: ctors.ctor_names)))(resolve_all_defs(sc, mod.defs, 0L, ((long)mod.defs.Count), new List<Diagnostic>()))))(build_all_names_scope(all_top, ctors.ctor_names, builtin_names()))))(Enumerable.Concat(top.names, cited_names).ToList())))(collect_cited_names(imported, 0L, ((long)imported.Count), new List<string>()))))(semantics__name_resolution_collect_ctor_names(mod.type_defs, 0L, ((long)mod.type_defs.Count), new List<string>(), new List<string>()))))(collect_top_level_names(mod.defs, 0L, ((long)mod.defs.Count), new List<string>(), new List<Diagnostic>()));

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
            var _tco_1 = (i + 1L);
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

    public static long cc_cr() => (-1L);

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

    public static long cc_first_upper() => 39;

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

    public static LexState make_lex_state(string src, long fid) => new LexState(source: src, offset: 0L, line: 1L, column: 1L, file_id: fid);

    public static bool is_at_end(LexState st) => (st.offset >= ((long)st.source.Length));

    public static long peek_code(LexState st) => (is_at_end(st) ? 0L : ((long)st.source[(int)st.offset]));

    public static LexState advance_char(LexState st) => ((peek_code(st) == cc_newline()) ? new LexState(source: st.source, offset: (st.offset + 1L), line: (st.line + 1L), column: 1L, file_id: st.file_id) : new LexState(source: st.source, offset: (st.offset + 1L), line: st.line, column: (st.column + 1L), file_id: st.file_id));

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
            var _tco_1 = (offset + 1L);
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

    public static LexState skip_spaces(LexState st) => ((Func<long, LexState>)((len) => ((Func<long, LexState>)((stop) => ((stop == st.offset) ? st : new LexState(source: st.source, offset: stop, line: st.line, column: (st.column + (stop - st.offset)), file_id: st.file_id))))(skip_spaces_end(st.source, st.offset, len))))(((long)st.source.Length));

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
            var _tco_1 = (offset + 1L);
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
            var _tco_1 = (offset + 1L);
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
            var _tco_1 = (offset + 1L);
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
            if (((offset + 1L) >= len))
            {
            return offset;
            }
            else
            {
            var nc = ((long)source[(int)(offset + 1L)]);
            if ((is_letter_code(nc) || is_digit_code(nc)))
            {
            var _tco_0 = source;
            var _tco_1 = (offset + 1L);
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

    public static LexState scan_ident_rest(LexState st) => ((Func<long, LexState>)((len) => ((Func<long, LexState>)((stop) => ((stop == st.offset) ? st : new LexState(source: st.source, offset: stop, line: st.line, column: (st.column + (stop - st.offset)), file_id: st.file_id))))(scan_ident_end(st.source, st.offset, len))))(((long)st.source.Length));

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
            var _tco_1 = (offset + 1L);
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
            var _tco_1 = (offset + 1L);
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

    public static LexState scan_digits(LexState st) => ((Func<long, LexState>)((len) => ((Func<long, LexState>)((stop) => ((stop == st.offset) ? st : new LexState(source: st.source, offset: stop, line: st.line, column: (st.column + (stop - st.offset)), file_id: st.file_id))))(scan_digits_end(st.source, st.offset, len))))(((long)st.source.Length));

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
            return (offset + 1L);
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
            var _tco_1 = (offset + 2L);
            var _tco_2 = len;
            source = _tco_0;
            offset = _tco_1;
            len = _tco_2;
            continue;
            }
            else
            {
            var _tco_0 = source;
            var _tco_1 = (offset + 1L);
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

    public static LexState scan_string_body(LexState st) => ((Func<long, LexState>)((len) => ((Func<long, LexState>)((stop) => ((stop == st.offset) ? st : new LexState(source: st.source, offset: stop, line: st.line, column: (st.column + (stop - st.offset)), file_id: st.file_id))))(scan_string_end(st.source, st.offset, len))))(((long)st.source.Length));

    public static List<string> process_escapes_loop(string s, long i, long len, List<string> acc)
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
            if (((i + 1L) < len))
            {
            var nc = ((long)s[(int)(i + 1L)]);
            if ((nc == cc_lower_n()))
            {
            var _tco_0 = s;
            var _tco_1 = (i + 2L);
            var _tco_2 = len;
            var _tco_3 = ((Func<List<string>>)(() => { var _l = acc; _l.Add(((char)1).ToString()); return _l; }))();
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
            var _tco_1 = (i + 2L);
            var _tco_2 = len;
            var _tco_3 = ((Func<List<string>>)(() => { var _l = acc; _l.Add("\u0002\u0002"); return _l; }))();
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
            var _tco_1 = (i + 2L);
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
            var _tco_1 = (i + 2L);
            var _tco_2 = len;
            var _tco_3 = ((Func<List<string>>)(() => { var _l = acc; _l.Add("\u0056"); return _l; }))();
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
            var _tco_1 = (i + 2L);
            var _tco_2 = len;
            var _tco_3 = ((Func<List<string>>)(() => { var _l = acc; _l.Add("\u0048"); return _l; }))();
            s = _tco_0;
            i = _tco_1;
            len = _tco_2;
            acc = _tco_3;
            continue;
            }
            else
            {
            var _tco_0 = s;
            var _tco_1 = (i + 2L);
            var _tco_2 = len;
            var _tco_3 = ((Func<List<string>>)(() => { var _l = acc; _l.Add(((char)((long)s[(int)(i + 1L)])).ToString()); return _l; }))();
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
            return ((Func<List<string>>)(() => { var _l = acc; _l.Add(((char)((long)s[(int)i])).ToString()); return _l; }))();
            }
            }
            else
            {
            var _tco_0 = s;
            var _tco_1 = (i + 1L);
            var _tco_2 = len;
            var _tco_3 = ((Func<List<string>>)(() => { var _l = acc; _l.Add(((char)((long)s[(int)i])).ToString()); return _l; }))();
            s = _tco_0;
            i = _tco_1;
            len = _tco_2;
            acc = _tco_3;
            continue;
            }
            }
        }
    }

    public static string process_escapes(string s, long i, long len) => string.Concat(process_escapes_loop(s, i, len, new List<string>()));

    public static TokenKind classify_word(string w) => ((w == "\u0017\u000D\u000E") ? new LetKeyword() : ((w == "\u0011\u0012") ? new InKeyword() : ((w == "\u0011\u001C") ? new IfKeyword() : ((w == "\u0011\u0013") ? new IsKeyword() : ((w == "\u0010\u000E\u0014\u000D\u0015\u001B\u0011\u0013\u000D") ? new OtherwiseKeyword() : ((w == "\u000E\u0014\u000D\u0012") ? new ThenKeyword() : ((w == "\u000D\u0017\u0013\u000D") ? new ElseKeyword() : ((w == "\u001B\u0014\u000D\u0012") ? new WhenKeyword() : ((w == "\u001B\u0014\u000D\u0015\u000D") ? new WhereKeyword() : ((w == "\u000F\u0018\u000E") ? new ActKeyword() : ((w == "\u000D\u0012\u0016") ? new EndKeyword() : ((w == "\u0015\u000D\u0018\u0010\u0015\u0016") ? new RecordKeyword() : ((w == "\u0018\u0011\u000E\u000D\u0013") ? new CitesKeyword() : ((w == "\u0018\u0017\u000F\u0011\u001A") ? new ClaimKeyword() : ((w == "\u001F\u0015\u0010\u0010\u001C") ? new ProofKeyword() : ((w == "\u0025\u000D\u0016") ? new QedKeyword() : ((w == "\u001C\u0010\u0015\u000F\u0017\u0017") ? new ForAllKeyword() : ((w == "\u000D\u0024\u0011\u0013\u000E\u0013") ? new ThereExistsKeyword() : ((w == "\u0017\u0011\u0012\u000D\u000F\u0015") ? new LinearKeyword() : ((w == "\u000D\u001C\u001C\u000D\u0018\u000E") ? new EffectKeyword() : ((w == "\u001B\u0011\u000E\u0014") ? new WithKeyword() : ((w == "\u0028\u0015\u0019\u000D") ? new TrueKeyword() : ((w == "\u0036\u000F\u0017\u0013\u000D") ? new FalseKeyword() : ((Func<long, TokenKind>)((first_code) => ((first_code >= cc_first_upper()) ? ((first_code <= cc_upper_z()) ? new TypeIdentifier() : new Identifier()) : new Identifier())))(((long)w[(int)0L])))))))))))))))))))))))));

    public static Token make_token(TokenKind kind, string text, LexState st) => new Token(kind: kind, text: text, offset: st.offset, line: st.line, column: st.column, file_id: st.file_id);

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
            var start = (s.offset + 1L);
            var after = scan_string_body(advance_char(s));
            var text_len = ((after.offset - start) - 1L);
            var raw = s.source.Substring((int)start, (int)text_len);
            return new LexToken(make_token(new TextLiteral(), process_escapes(raw, 0L, ((long)raw.Length)), s), after);
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
            if ((((long)word.Length) == 1L))
            {
            return new LexToken(make_token(new Underscore(), "\u0055", s), after);
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

    public static LexResult scan_operator(LexState s) => ((Func<long, LexResult>)((c) => ((Func<LexState, LexResult>)((next) => ((c == cc_left_paren()) ? new LexToken(make_token(new LeftParen(), "\u004A", s), next) : ((c == cc_right_paren()) ? new LexToken(make_token(new RightParen(), "\u004B", s), next) : ((c == cc_left_bracket()) ? new LexToken(make_token(new LeftBracket(), "\u0058", s), next) : ((c == cc_right_bracket()) ? new LexToken(make_token(new RightBracket(), "\u0059", s), next) : ((c == cc_left_brace()) ? new LexToken(make_token(new LeftBrace(), "\u005A", s), next) : ((c == cc_right_brace()) ? new LexToken(make_token(new RightBrace(), "\u005B", s), next) : ((c == cc_comma()) ? new LexToken(make_token(new Comma(), "\u0042", s), next) : ((c == cc_dot()) ? new LexToken(make_token(new Dot(), "\u0041", s), next) : ((c == cc_caret()) ? new LexToken(make_token(new Caret(), "\u005E", s), next) : ((c == cc_ampersand()) ? new LexToken(make_token(new Ampersand(), "\u0054", s), next) : ((c == cc_backslash()) ? new LexToken(make_token(new Backslash(), "\u0056", s), next) : scan_multi_char_operator(s))))))))))))))(advance_char(s))))(peek_code(s));

    public static LexResult scan_multi_char_operator(LexState s) => ((Func<long, LexResult>)((c) => ((Func<LexState, LexResult>)((next) => ((Func<long, LexResult>)((nc) => ((c == cc_plus()) ? ((nc == cc_plus()) ? new LexToken(make_token(new PlusPlus(), "\u004C\u004C", s), advance_char(next)) : new LexToken(make_token(new Plus(), "\u004C", s), next)) : ((c == cc_minus()) ? ((nc == cc_greater()) ? new LexToken(make_token(new Arrow(), "\u0049\u0050", s), advance_char(next)) : new LexToken(make_token(new Minus(), "\u0049", s), next)) : ((c == cc_star()) ? new LexToken(make_token(new Star(), "\u004E", s), next) : ((c == cc_slash()) ? ((nc == cc_equals()) ? new LexToken(make_token(new NotEquals(), "\u0051\u004D", s), advance_char(next)) : new LexToken(make_token(new Slash(), "\u0051", s), next)) : ((c == cc_equals()) ? ((nc == cc_equals()) ? ((Func<LexState, LexResult>)((next2) => ((Func<long, LexResult>)((nc2) => ((nc2 == cc_equals()) ? new LexToken(make_token(new TripleEquals(), "\u004D\u004D\u004D", s), advance_char(next2)) : new LexToken(make_token(new DoubleEquals(), "\u004D\u004D", s), next2))))((is_at_end(next2) ? 0L : peek_code(next2)))))(advance_char(next)) : new LexToken(make_token(new Equals_(), "\u004D", s), next)) : ((c == cc_colon()) ? ((nc == cc_colon()) ? new LexToken(make_token(new ColonColon(), "\u0045\u0045", s), advance_char(next)) : new LexToken(make_token(new Colon(), "\u0045", s), next)) : ((c == cc_pipe()) ? ((nc == cc_minus()) ? new LexToken(make_token(new Turnstile(), "\u0057\u0049", s), advance_char(next)) : new LexToken(make_token(new Pipe(), "\u0057", s), next)) : ((c == cc_less()) ? ((nc == cc_equals()) ? new LexToken(make_token(new LessOrEqual(), "\u004F\u004D", s), advance_char(next)) : ((nc == cc_minus()) ? new LexToken(make_token(new LeftArrow(), "\u004F\u0049", s), advance_char(next)) : new LexToken(make_token(new LessThan(), "\u004F", s), next))) : ((c == cc_greater()) ? ((nc == cc_equals()) ? new LexToken(make_token(new GreaterOrEqual(), "\u0050\u004D", s), advance_char(next)) : new LexToken(make_token(new GreaterThan(), "\u0050", s), next)) : new LexToken(make_token(new ErrorToken(), ((char)((long)s.source[(int)s.offset])).ToString(), s), next))))))))))))((is_at_end(next) ? 0L : peek_code(next)))))(advance_char(s))))(peek_code(s));

    public static LexResult scan_char_literal(LexState s) => ((Func<LexState, LexResult>)((s1) => (is_at_end(s1) ? new LexToken(make_token(new ErrorToken(), "\u0047", s), s1) : ((peek_code(s1) == cc_backslash()) ? ((Func<LexState, LexResult>)((s2) => (is_at_end(s2) ? new LexToken(make_token(new ErrorToken(), "\u0047\u0056", s), s2) : ((Func<long, LexResult>)((esc_code) => ((Func<long, LexResult>)((char_val) => ((Func<LexState, LexResult>)((s3) => ((Func<LexState, LexResult>)((s4) => new LexToken(make_token(new CharLiteral(), _Cce.FromUnicode(char_val.ToString()), s), s4)))((is_at_end(s3) ? s3 : ((peek_code(s3) == cc_single_quote()) ? advance_char(s3) : s3)))))(advance_char(s2))))(((esc_code == cc_lower_n()) ? cc_newline() : ((esc_code == cc_lower_t()) ? cc_space() : ((esc_code == cc_lower_r()) ? cc_newline() : ((esc_code == cc_backslash()) ? cc_backslash() : ((esc_code == cc_single_quote()) ? cc_single_quote() : esc_code))))))))(peek_code(s2)))))(advance_char(s1)) : ((Func<long, LexResult>)((char_val) => ((Func<LexState, LexResult>)((s2) => ((Func<LexState, LexResult>)((s3) => new LexToken(make_token(new CharLiteral(), _Cce.FromUnicode(char_val.ToString()), s), s3)))((is_at_end(s2) ? s2 : ((peek_code(s2) == cc_single_quote()) ? advance_char(s2) : s2)))))(advance_char(s1))))(peek_code(s1))))))(advance_char(s));

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

    public static List<Token> tokenize(string src, long fid) => tokenize_loop(make_lex_state(src, fid), new List<Token>());

    public static ParseTypeResult parse_type(ParseState st) => ((Func<ParseTypeResult, ParseTypeResult>)((result) => unwrap_type_ok(result, (_p0_) => (_p1_) => parse_type_continue(_p0_, _p1_))))(parse_type_atom(st));

    public static ParseTypeResult parse_type_continue(TypeExpr left, ParseState st) => (is_arrow(current_kind(st)) ? ((Func<ParseState, ParseTypeResult>)((st2) => ((Func<ParseTypeResult, ParseTypeResult>)((right_result) => unwrap_type_ok(right_result, (_p0_) => (_p1_) => make_fun_type(left, _p0_, _p1_))))(parse_type(st2))))(advance(st)) : new TypeOk(left, st));

    public static ParseTypeResult make_fun_type(TypeExpr left, TypeExpr right, ParseState st) => new TypeOk(new FunType(left, right), st);

    public static ParseTypeResult unwrap_type_ok(ParseTypeResult r, Func<TypeExpr, Func<ParseState, ParseTypeResult>> f) => r switch { TypeOk(var t, var st) => (ParseTypeResult)(f(t)(st)), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static ParseTypeResult parse_type_atom(ParseState st) => (is_ident(current_kind(st)) ? ((Func<Token, ParseTypeResult>)((tok) => parse_type_args(new NamedType(tok), advance(st))))(current(st)) : (is_type_ident(current_kind(st)) ? ((Func<Token, ParseTypeResult>)((tok) => parse_type_args(new NamedType(tok), advance(st))))(current(st)) : (is_left_paren(current_kind(st)) ? parse_paren_type(advance(st)) : (is_left_bracket(current_kind(st)) ? parse_effect_type(advance(st)) : ((Func<Token, ParseTypeResult>)((tok) => new TypeOk(new NamedType(tok), advance(st))))(current(st))))));

    public static ParseTypeResult parse_paren_type(ParseState st) => ((Func<ParseTypeResult, ParseTypeResult>)((inner) => unwrap_type_ok(inner, (_p0_) => (_p1_) => finish_paren_type(_p0_, _p1_))))(parse_type(st));

    public static ParseTypeResult finish_paren_type(TypeExpr t, ParseState st) => ((Func<ParseState, ParseTypeResult>)((st2) => new TypeOk(new ParenType(t), st2)))(expect(new RightParen(), st));

    public static ParseTypeResult parse_effect_type(ParseState st) => ((Func<EffectNamesResult, ParseTypeResult>)((effs) => ((Func<ParseTypeResult, ParseTypeResult>)((ret) => unwrap_type_ok(ret, (_p0_) => (_p1_) => finish_effect_type(effs.names, _p0_, _p1_))))(parse_type(effs.state))))(parse_effect_names(st, new List<Token>()));

    public static ParseTypeResult finish_effect_type(List<Token> effs, TypeExpr ret, ParseState st) => new TypeOk(new EffectTypeExpr(effs, ret), st);

    public static EffectNamesResult parse_effect_names(ParseState st, List<Token> acc)
    {
        while (true)
        {
            if (is_done(st))
            {
            return new EffectNamesResult(names: acc, state: st);
            }
            else
            {
            if (is_right_bracket(current_kind(st)))
            {
            return new EffectNamesResult(names: acc, state: advance(st));
            }
            else
            {
            if (is_comma(current_kind(st)))
            {
            var _tco_0 = advance(st);
            var _tco_1 = acc;
            st = _tco_0;
            acc = _tco_1;
            continue;
            }
            else
            {
            if ((is_ident(current_kind(st)) || is_type_ident(current_kind(st))))
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
            var _tco_0 = advance(st);
            var _tco_1 = acc;
            st = _tco_0;
            acc = _tco_1;
            continue;
            }
            }
            }
            }
        }
    }

    public static ParseTypeResult parse_type_args(TypeExpr base_type, ParseState st) => (is_done(st) ? new TypeOk(base_type, st) : (is_type_arg_start(current_kind(st)) ? parse_type_arg_next(base_type, st) : new TypeOk(base_type, st)));

    public static ParseTypeResult parse_type_arg_next(TypeExpr base_type, ParseState st) => ((Func<ParseTypeResult, ParseTypeResult>)((arg_result) => unwrap_type_ok(arg_result, (_p0_) => (_p1_) => continue_type_args(base_type, _p0_, _p1_))))(parse_type_atom(st));

    public static ParseTypeResult continue_type_args(TypeExpr base_type, TypeExpr arg, ParseState st) => parse_type_args(new AppType(base_type, new List<TypeExpr> { arg }), st);

    public static ParsePatResult parse_pattern(ParseState st) => (is_wildcard_pat(current_kind(st)) ? new PatOk(new WildPat(current(st)), advance(st)) : (is_literal(current_kind(st)) ? new PatOk(new LitPat(current(st)), advance(st)) : (is_type_ident(current_kind(st)) ? ((Func<Token, ParsePatResult>)((tok) => parse_ctor_pattern_fields(tok, new List<Pat>(), advance(st))))(current(st)) : (is_ident(current_kind(st)) ? new PatOk(new VarPat(current(st)), advance(st)) : new PatOk(new WildPat(current(st)), advance(st))))));

    public static ParsePatResult parse_ctor_pattern_fields(Token ctor, List<Pat> acc, ParseState st) => (is_left_paren(current_kind(st)) ? ((Func<ParseState, ParsePatResult>)((st2) => ((Func<ParsePatResult, ParsePatResult>)((sub) => unwrap_pat_ok(sub, (_p0_) => (_p1_) => continue_ctor_fields(ctor, acc, _p0_, _p1_))))(parse_pattern(st2))))(advance(st)) : new PatOk(new CtorPat(ctor, acc), st));

    public static ParsePatResult continue_ctor_fields(Token ctor, List<Pat> acc, Pat p, ParseState st) => ((Func<ParseState, ParsePatResult>)((st2) => parse_ctor_pattern_fields(ctor, ((Func<List<Pat>>)(() => { var _l = acc; _l.Add(p); return _l; }))(), st2)))(expect(new RightParen(), st));

    public static ParsePatResult unwrap_pat_ok(ParsePatResult r, Func<Pat, Func<ParseState, ParsePatResult>> f) => r switch { PatOk(var p, var st) => (ParsePatResult)(f(p)(st)), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static ParseTypeResult parse_type_annotation(ParseState st) => ((Func<ParseState, ParseTypeResult>)((st2) => ((Func<ParseState, ParseTypeResult>)((st3) => parse_type(st3)))(expect(new Colon(), st2))))(advance(st));

    public static ParseDefResult parse_definition(ParseState st) => (is_done(st) ? new ParseDefResult(maybe_def: new None<Def>(), state: st) : (is_ident(current_kind(st)) ? try_parse_def(st) : (is_type_ident(current_kind(st)) ? try_parse_def(st) : new ParseDefResult(maybe_def: new None<Def>(), state: st))));

    public static ParseDefResult try_parse_def(ParseState st) => (is_colon(peek_kind(st, 1L)) ? ((Func<Token, ParseDefResult>)((first_name) => ((Func<ParseTypeResult, ParseDefResult>)((ann_result) => unwrap_type_for_def(first_name, ann_result)))(parse_type_annotation(st))))(current(st)) : parse_def_body_with_ann(new List<TypeAnn>(), st));

    public static ParseDefResult unwrap_type_for_def(Token first_name, ParseTypeResult r) => r switch { TypeOk(var ann_type, var st) => (ParseDefResult)((is_equals(current_kind(st)) ? ((Func<List<TypeAnn>, ParseDefResult>)((ann) => ((Func<ParseState, ParseDefResult>)((st2) => ((Func<ParseState, ParseDefResult>)((st3) => ((Func<ParseExprResult, ParseDefResult>)((body_result) => unwrap_def_body(body_result, ann, first_name, new List<Token>())))(parse_expr_col(st3, first_name.column))))(skip_newlines(st2))))(advance(st))))(new List<TypeAnn> { new TypeAnn(name: first_name, type_expr: ann_type) }) : ((Func<Token, ParseDefResult>)((name_tok) => ((Func<List<TypeAnn>, ParseDefResult>)((ann) => ((Func<ParseState, ParseDefResult>)((st2) => parse_def_body_with_ann(ann, st2)))(skip_newlines(st))))(new List<TypeAnn> { new TypeAnn(name: name_tok, type_expr: ann_type) })))(new Token(kind: new Identifier(), text: "", offset: 0L, line: 0L, column: 0L, file_id: 0L)))), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static ParseDefResult parse_def_body_with_ann(List<TypeAnn> ann, ParseState st) => ((Func<Token, ParseDefResult>)((name_tok) => ((Func<ParseState, ParseDefResult>)((st2) => parse_def_params_then(ann, name_tok, new List<Token>(), st2)))(advance(st))))(current(st));

    public static ParseDefResult parse_def_params_then(List<TypeAnn> ann, Token name_tok, List<Token> acc, ParseState st) => (is_left_paren(current_kind(st)) ? parse_def_params_after_lparen(ann, name_tok, acc, advance(st)) : finish_def(ann, name_tok, acc, st));

    public static ParseDefResult parse_def_params_after_lparen(List<TypeAnn> ann, Token name_tok, List<Token> acc, ParseState st) => (is_ident(current_kind(st)) ? ((Func<Token, ParseDefResult>)((param) => ((Func<ParseState, ParseDefResult>)((st2) => parse_def_params_then(ann, name_tok, ((Func<List<Token>>)(() => { var _l = acc; _l.Add(param); return _l; }))(), st2)))(expect(new RightParen(), advance(st)))))(current(st)) : (is_reserved_keyword(current_kind(st)) ? ((Func<ParseState, ParseDefResult>)((st2) => ((Func<ParseState, ParseDefResult>)((st3) => parse_def_params_then(ann, name_tok, acc, st3)))(expect(new RightParen(), advance(st2)))))(report_reserved_keyword("\u000F\u0002\u001F\u000F\u0015\u000F\u001A\u000D\u000E\u000D\u0015\u0002\u0012\u000F\u001A\u000D", st)) : finish_def(ann, name_tok, acc, st)));

    public static ParseDefResult finish_def(List<TypeAnn> ann, Token name_tok, List<Token> @params, ParseState st) => (is_equals(current_kind(st)) ? ((Func<ParseState, ParseDefResult>)((st2) => ((Func<ParseState, ParseDefResult>)((st3) => ((Func<ParseExprResult, ParseDefResult>)((body_result) => unwrap_def_body(body_result, ann, name_tok, @params)))(parse_expr_col(st3, name_tok.column))))(skip_newlines(st2))))(advance(st)) : ((Func<Token, ParseDefResult>)((tok) => ((Func<ParseState, ParseDefResult>)((err_st) => ((Func<ParseState, ParseDefResult>)((st2) => new ParseDefResult(maybe_def: new Just<Def>(new Def(name: name_tok, @params: @params, ann: ann, body: new ErrExpr(tok), chapter_slug: "")), state: st2)))(skip_body_tokens(err_st, name_tok.column))))(expect(new Equals_(), st))))(current(st)));

    public static ParseDefResult unwrap_def_body(ParseExprResult r, List<TypeAnn> ann, Token name_tok, List<Token> @params) => r switch { ExprOk(var b, var st) => (ParseDefResult)(new ParseDefResult(maybe_def: new Just<Def>(new Def(name: name_tok, @params: @params, ann: ann, body: b, chapter_slug: "")), state: st)), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static bool is_paren_type_param(ParseState st) => (is_left_paren(current_kind(st)) ? ((Func<TokenKind, bool>)((k1) => (is_ident(k1) ? is_right_paren(peek_kind(st, 2L)) : (is_type_ident(k1) ? is_right_paren(peek_kind(st, 2L)) : false))))(peek_kind(st, 1L)) : false);

    public static bool is_type_param_pattern(ParseState st) => (is_paren_type_param(st) ? true : is_ident(current_kind(st)));

    public static ParseState parse_type_params(ParseState st, List<Token> acc)
    {
        while (true)
        {
            if (is_paren_type_param(st))
            {
            var _tco_0 = advance(advance(advance(st)));
            var _tco_1 = ((Func<List<Token>>)(() => { var _l = acc; _l.Add(st.tokens[(int)(st.pos + 1L)]); return _l; }))();
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
            var _tco_1 = ((Func<List<Token>>)(() => { var _l = acc; _l.Add(st.tokens[(int)(st.pos + 1L)]); return _l; }))();
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

    public static ParseTypeDefResult parse_type_def(ParseState st) => (is_type_ident(current_kind(st)) ? ((Func<Token, ParseTypeDefResult>)((name_tok) => ((Func<ParseState, ParseTypeDefResult>)((st2) => ((Func<List<Token>, ParseTypeDefResult>)((tparams) => ((Func<ParseState, ParseTypeDefResult>)((st3) => (is_equals(current_kind(st3)) ? ((Func<ParseState, ParseTypeDefResult>)((st4) => (is_record_keyword(current_kind(st4)) ? parse_record_type(name_tok, tparams, st4) : (is_pipe(current_kind(st4)) ? parse_variant_type(name_tok, tparams, st4) : ((is_type_ident(current_kind(st4)) && looks_like_variant(st4)) ? parse_variant_type(name_tok, tparams, st4) : new ParseTypeDefResult(maybe_type_def: new None<TypeDef>(), state: st))))))(skip_newlines(advance(st3))) : new ParseTypeDefResult(maybe_type_def: new None<TypeDef>(), state: st))))(parse_type_params(st2, new List<Token>()))))(collect_type_params(st2, new List<Token>()))))(advance(st))))(current(st)) : new ParseTypeDefResult(maybe_type_def: new None<TypeDef>(), state: st));

    public static ParseTypeDefResult parse_record_type(Token name_tok, List<Token> tparams, ParseState st) => ((Func<ParseState, ParseTypeDefResult>)((st2) => ((Func<ParseState, ParseTypeDefResult>)((st3) => ((Func<ParseState, ParseTypeDefResult>)((st4) => parse_record_fields_loop(name_tok, tparams, new List<RecordFieldDef>(), st4)))(skip_newlines(st3))))(expect(new LeftBrace(), st2))))(advance(st));

    public static ParseTypeDefResult parse_record_fields_loop(Token name_tok, List<Token> tparams, List<RecordFieldDef> acc, ParseState st) => (is_right_brace(current_kind(st)) ? new ParseTypeDefResult(maybe_type_def: new Just<TypeDef>(new TypeDef(name: name_tok, type_params: tparams, body: new RecordBody(acc))), state: advance(st)) : (is_ident_like(current_kind(st)) ? parse_one_record_field(name_tok, tparams, acc, st) : new ParseTypeDefResult(maybe_type_def: new Just<TypeDef>(new TypeDef(name: name_tok, type_params: tparams, body: new RecordBody(acc))), state: st)));

    public static ParseTypeDefResult parse_one_record_field(Token name_tok, List<Token> tparams, List<RecordFieldDef> acc, ParseState st) => ((Func<Token, ParseTypeDefResult>)((field_name) => ((Func<ParseState, ParseTypeDefResult>)((st2) => ((Func<ParseState, ParseTypeDefResult>)((st3) => ((Func<ParseTypeResult, ParseTypeDefResult>)((field_type_result) => unwrap_record_field_type(name_tok, tparams, acc, field_name, field_type_result)))(parse_type(st3))))(expect(new Colon(), st2))))(advance(st))))(current(st));

    public static ParseTypeDefResult unwrap_record_field_type(Token name_tok, List<Token> tparams, List<RecordFieldDef> acc, Token field_name, ParseTypeResult r) => r switch { TypeOk(var ft, var st) => (ParseTypeDefResult)(((Func<RecordFieldDef, ParseTypeDefResult>)((field) => ((Func<ParseState, ParseTypeDefResult>)((st2) => (is_comma(current_kind(st2)) ? parse_record_fields_loop(name_tok, tparams, ((Func<List<RecordFieldDef>>)(() => { var _l = acc; _l.Add(field); return _l; }))(), skip_newlines(advance(st2))) : parse_record_fields_loop(name_tok, tparams, ((Func<List<RecordFieldDef>>)(() => { var _l = acc; _l.Add(field); return _l; }))(), st2))))(skip_newlines(st))))(new RecordFieldDef(name: field_name, type_expr: ft))), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static bool looks_like_variant(ParseState st) => looks_like_variant_scan(st.tokens, (st.pos + 1L), ((long)st.tokens.Count));

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
            var _tco_1 = (i + 1L);
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

    public static ParseTypeDefResult parse_variant_ctors(Token name_tok, List<Token> tparams, List<VariantCtorDef> acc, ParseState st) => (is_pipe(current_kind(st)) ? ((Func<ParseState, ParseTypeDefResult>)((st2) => ((Func<Token, ParseTypeDefResult>)((ctor_name) => ((Func<ParseState, ParseTypeDefResult>)((st3) => parse_ctor_fields(ctor_name, new List<TypeExpr>(), st3, name_tok, tparams, acc)))(advance(st2))))(current(st2))))(skip_newlines(advance(st))) : new ParseTypeDefResult(maybe_type_def: new Just<TypeDef>(new TypeDef(name: name_tok, type_params: tparams, body: new VariantBody(acc))), state: st));

    public static ParseTypeDefResult parse_ctor_fields(Token ctor_name, List<TypeExpr> fields, ParseState st, Token name_tok, List<Token> tparams, List<VariantCtorDef> acc) => (is_left_paren(current_kind(st)) ? ((Func<ParseTypeResult, ParseTypeDefResult>)((field_result) => unwrap_ctor_field(field_result, ctor_name, fields, name_tok, tparams, acc)))(parse_type(advance(st))) : ((Func<ParseState, ParseTypeDefResult>)((st2) => ((Func<VariantCtorDef, ParseTypeDefResult>)((ctor) => parse_variant_ctors(name_tok, tparams, ((Func<List<VariantCtorDef>>)(() => { var _l = acc; _l.Add(ctor); return _l; }))(), st2)))(new VariantCtorDef(name: ctor_name, fields: fields))))(skip_newlines(st)));

    public static ParseTypeDefResult unwrap_ctor_field(ParseTypeResult r, Token ctor_name, List<TypeExpr> fields, Token name_tok, List<Token> tparams, List<VariantCtorDef> acc) => r switch { TypeOk(var ty, var st) => (ParseTypeDefResult)(((Func<ParseState, ParseTypeDefResult>)((st2) => parse_ctor_fields(ctor_name, Enumerable.Concat(fields, new List<TypeExpr> { ty }).ToList(), st2, name_tok, tparams, acc)))(expect(new RightParen(), st))), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static Document parse_document(ParseState st) => ((Func<ParseState, Document>)((st2) => ((Func<ImportParseResult, Document>)((imp_result) => parse_top_level(new List<Def>(), new List<TypeDef>(), new List<EffectDef>(), imp_result.imports, "", new List<string>(), imp_result.state)))(parse_citations(st2, "", new List<CitesDecl>()))))(skip_newlines(st));

    public static CiteTitleResult collect_cite_title(ParseState st, string acc)
    {
        while (true)
        {
            if (is_done(st))
            {
            return new CiteTitleResult(title: acc, state: st);
            }
            else
            {
            var _tco_s = current_kind(st);
            if (_tco_s is Newline _tco_m0)
            {
            return new CiteTitleResult(title: acc, state: st);
            }
            else if (_tco_s is LeftParen _tco_m1)
            {
            return new CiteTitleResult(title: acc, state: st);
            }
            {
            var tok = current(st);
            var _tco_0 = advance(st);
            var _tco_1 = join_title_parts(acc, tok.text);
            st = _tco_0;
            acc = _tco_1;
            continue;
            }
            }
        }
    }

    public static ImportParseResult parse_citations(ParseState st, string citing, List<CitesDecl> acc)
    {
        while (true)
        {
            if (is_cites_keyword(current_kind(st)))
            {
            var st2 = advance(st);
            var quire_tok = current(st2);
            var st3 = advance(st2);
            var st4 = advance(st3);
            var title_result = collect_cite_title(st4, "");
            var st5 = title_result.state;
            if (is_left_paren(current_kind(st5)))
            {
            var sel = parse_selected_names(advance(st5), new List<Token>());
            var st6 = skip_newlines(sel.state);
            var _tco_0 = st6;
            var _tco_1 = citing;
            var _tco_2 = ((Func<List<CitesDecl>>)(() => { var _l = acc; _l.Add(new CitesDecl(quire: quire_tok, chapter_name: title_result.title, selected_names: sel.names, citing_chapter: citing)); return _l; }))();
            st = _tco_0;
            citing = _tco_1;
            acc = _tco_2;
            continue;
            }
            else
            {
            var st6 = skip_newlines(st5);
            var _tco_0 = st6;
            var _tco_1 = citing;
            var _tco_2 = ((Func<List<CitesDecl>>)(() => { var _l = acc; _l.Add(new CitesDecl(quire: quire_tok, chapter_name: title_result.title, selected_names: new List<Token>(), citing_chapter: citing)); return _l; }))();
            st = _tco_0;
            citing = _tco_1;
            acc = _tco_2;
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

    public static bool is_chapter_header(ParseState st) => (is_type_ident(current_kind(st)) ? ((current(st).text == "\u0032\u0014\u000F\u001F\u000E\u000D\u0015") ? is_colon(peek_kind(st, 1L)) : false) : false);

    public static bool is_section_header(ParseState st) => (is_type_ident(current_kind(st)) ? ((current(st).text == "\u002D\u000D\u0018\u000E\u0011\u0010\u0012") ? is_colon(peek_kind(st, 1L)) : false) : false);

    public static bool is_page_marker(ParseState st) => (is_type_ident(current_kind(st)) ? (current(st).text == "\u0039\u000F\u001D\u000D") : false);

    public static string extract_header_title(ParseState st) => ((Func<ParseState, string>)((st2) => collect_title_tokens(st2, "")))(advance(advance(st)));

    public static string join_title_parts(string acc, string next) => ((((long)acc.Length) == 0L) ? next : ((((long)next.Length) == 0L) ? acc : ((Func<long, string>)((prev_last) => ((Func<long, string>)((next_first) => (((is_letter_code(prev_last) || is_digit_code(prev_last)) && (is_letter_code(next_first) || is_digit_code(next_first))) ? (acc + ("\u0002" + next)) : (acc + next))))(((long)next[(int)0L]))))(((long)acc[(int)(((long)acc.Length) - 1L)]))));

    public static string collect_title_tokens(ParseState st, string acc)
    {
        while (true)
        {
            if (is_done(st))
            {
            return acc;
            }
            else
            {
            var _tco_s = current_kind(st);
            if (_tco_s is Newline _tco_m0)
            {
            return acc;
            }
            {
            var tok = current(st);
            var _tco_0 = advance(st);
            var _tco_1 = join_title_parts(acc, tok.text);
            st = _tco_0;
            acc = _tco_1;
            continue;
            }
            }
        }
    }

    public static bool is_prose_line(ParseState st) => (current(st).column == 2L);

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
            return new Document(defs: defs, type_defs: type_defs, effect_defs: effect_defs, citations: imports, chapter_title: ch_title, section_titles: sec_titles, parse_bag: st.bag);
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
            var cite_result = parse_citations(st, ch_title, new List<CitesDecl>());
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
            if (is_colon(peek_kind(st, 1L)))
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

    public static Document try_top_level_type_def(List<Def> defs, List<TypeDef> type_defs, List<EffectDef> effect_defs, List<CitesDecl> imports, string ch_title, List<string> sec_titles, ParseState st) => ((Func<ParseTypeDefResult, Document>)((td_result) => ((Func<ParseState, Document>)((st2) => td_result.maybe_type_def switch { Just<TypeDef>(var td) => (Document)(parse_top_level(defs, Enumerable.Concat(type_defs, new List<TypeDef> { td }).ToList(), effect_defs, imports, ch_title, sec_titles, skip_newlines(st2))), None<TypeDef> { } => (Document)(try_top_level_def(defs, type_defs, effect_defs, imports, ch_title, sec_titles, st)), _ => throw new InvalidOperationException("Non-exhaustive match"), }))(td_result.state)))(parse_type_def(st));

    public static Document try_top_level_def(List<Def> defs, List<TypeDef> type_defs, List<EffectDef> effect_defs, List<CitesDecl> imports, string ch_title, List<string> sec_titles, ParseState st) => ((Func<ParseDefResult, Document>)((def_result) => ((Func<ParseState, Document>)((st2) => def_result.maybe_def switch { Just<Def>(var d) => (Document)(((Func<Def, Document>)((tagged) => parse_top_level(Enumerable.Concat(defs, new List<Def> { tagged }).ToList(), type_defs, effect_defs, imports, ch_title, sec_titles, skip_newlines(st2))))(new Def(name: d.name, @params: d.@params, ann: d.ann, body: d.body, chapter_slug: ch_title))), None<Def> { } => (Document)(parse_top_level(defs, type_defs, effect_defs, imports, ch_title, sec_titles, skip_newlines(advance(st2)))), _ => throw new InvalidOperationException("Non-exhaustive match"), }))(def_result.state)))(parse_definition(st));

    public static ScanResult scan_document(ParseState st) => ((Func<ParseState, ScanResult>)((st2) => ((Func<ImportParseResult, ScanResult>)((imp_result) => scan_top_level(new List<DefHeader>(), new List<TypeDef>(), new List<EffectDef>(), imp_result.imports, "", "", new List<string>(), imp_result.state)))(parse_citations(st2, "", new List<CitesDecl>()))))(skip_newlines(st));

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
            var _tco_4 = title;
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
            var cite_result = parse_citations(st, cur_chap, new List<CitesDecl>());
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

    public static ScanResult try_scan_type_def(List<DefHeader> headers, List<TypeDef> type_defs, List<EffectDef> effect_defs, List<CitesDecl> imports, string cur_chap, string ch_title, List<string> sec_titles, ParseState st) => ((Func<ParseTypeDefResult, ScanResult>)((td_result) => ((Func<ParseState, ScanResult>)((st2) => td_result.maybe_type_def switch { Just<TypeDef>(var td) => (ScanResult)(scan_top_level(headers, Enumerable.Concat(type_defs, new List<TypeDef> { td }).ToList(), effect_defs, imports, cur_chap, ch_title, sec_titles, skip_newlines(st2))), None<TypeDef> { } => (ScanResult)(try_scan_def_header(headers, type_defs, effect_defs, imports, cur_chap, ch_title, sec_titles, st)), _ => throw new InvalidOperationException("Non-exhaustive match"), }))(td_result.state)))(parse_type_def(st));

    public static ScanResult try_scan_def_header(List<DefHeader> headers, List<TypeDef> type_defs, List<EffectDef> effect_defs, List<CitesDecl> imports, string cur_chap, string ch_title, List<string> sec_titles, ParseState st) => ((Func<ScanDefResult, ScanResult>)((hdr_result) => ((Func<ParseState, ScanResult>)((st2) => hdr_result.maybe_header switch { Just<DefHeader>(var hdr) => (ScanResult)(scan_top_level(Enumerable.Concat(headers, new List<DefHeader> { hdr }).ToList(), type_defs, effect_defs, imports, cur_chap, ch_title, sec_titles, skip_newlines(st2))), None<DefHeader> { } => (ScanResult)(scan_top_level(headers, type_defs, effect_defs, imports, cur_chap, ch_title, sec_titles, skip_newlines(advance(st2)))), _ => throw new InvalidOperationException("Non-exhaustive match"), }))(hdr_result.state)))(scan_definition(cur_chap, st));

    public static ScanDefResult scan_definition(string cur_chap, ParseState st) => (is_done(st) ? new ScanDefResult(maybe_header: new None<DefHeader>(), state: st) : (is_ident(current_kind(st)) ? try_scan_def(cur_chap, st) : (is_type_ident(current_kind(st)) ? try_scan_def(cur_chap, st) : new ScanDefResult(maybe_header: new None<DefHeader>(), state: st))));

    public static ScanDefResult try_scan_def(string cur_chap, ParseState st) => (is_colon(peek_kind(st, 1L)) ? ((Func<Token, ScanDefResult>)((first_name) => ((Func<ParseTypeResult, ScanDefResult>)((ann_result) => unwrap_type_for_scan(cur_chap, first_name, ann_result)))(parse_type_annotation(st))))(current(st)) : scan_def_body_with_ann(cur_chap, new List<TypeAnn>(), st));

    public static ScanDefResult unwrap_type_for_scan(string cur_chap, Token first_name, ParseTypeResult r) => r switch { TypeOk(var ann_type, var st) => (ScanDefResult)((is_equals(current_kind(st)) ? ((Func<List<TypeAnn>, ScanDefResult>)((ann) => ((Func<ParseState, ScanDefResult>)((st2) => ((Func<ParseState, ScanDefResult>)((st3) => ((Func<long, ScanDefResult>)((body_pos) => ((Func<ParseState, ScanDefResult>)((st4) => new ScanDefResult(maybe_header: new Just<DefHeader>(new DefHeader(name: first_name, @params: new List<Token>(), ann: ann, body_pos: body_pos, chapter_slug: cur_chap)), state: st4)))(skip_body_tokens(st3, first_name.column))))(st3.pos)))(skip_newlines(st2))))(advance(st))))(new List<TypeAnn> { new TypeAnn(name: first_name, type_expr: ann_type) }) : ((Func<Token, ScanDefResult>)((name_tok) => ((Func<List<TypeAnn>, ScanDefResult>)((ann) => ((Func<ParseState, ScanDefResult>)((st2) => scan_def_body_with_ann(cur_chap, ann, st2)))(skip_newlines(st))))(new List<TypeAnn> { new TypeAnn(name: name_tok, type_expr: ann_type) })))(new Token(kind: new Identifier(), text: "", offset: 0L, line: 0L, column: 0L, file_id: 0L)))), _ => throw new InvalidOperationException("Non-exhaustive match"), };

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

    public static ScanDefResult finish_def_scan(string cur_chap, List<TypeAnn> ann, Token name_tok, List<Token> @params, ParseState st) => ((is_equals(current_kind(st)) == false) ? new ScanDefResult(maybe_header: new None<DefHeader>(), state: st) : ((Func<ParseState, ScanDefResult>)((st2) => ((Func<ParseState, ScanDefResult>)((st3) => ((Func<long, ScanDefResult>)((body_pos) => ((Func<ParseState, ScanDefResult>)((st4) => new ScanDefResult(maybe_header: new Just<DefHeader>(new DefHeader(name: name_tok, @params: @params, ann: ann, body_pos: body_pos, chapter_slug: cur_chap)), state: st4)))(skip_body_tokens(st3, name_tok.column))))(st3.pos)))(skip_newlines(st2))))(advance(st)));

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

    public static ParseState make_parse_state(List<Token> toks) => new ParseState(tokens: toks, pos: 0L, bag: empty_bag(), paren_depth: 0L);

    public static Token current(ParseState st) => st.tokens[(int)st.pos];

    public static TokenKind current_kind(ParseState st) => current(st).kind;

    public static ParseState advance(ParseState st) => ((st.pos >= (((long)st.tokens.Count) - 1L)) ? st : new ParseState(tokens: st.tokens, pos: (st.pos + 1L), bag: st.bag, paren_depth: st.paren_depth));

    public static ParseState enter_paren(ParseState st) => new ParseState(tokens: st.tokens, pos: st.pos, bag: st.bag, paren_depth: (st.paren_depth + 1L));

    public static ParseState exit_paren(ParseState st) => new ParseState(tokens: st.tokens, pos: st.pos, bag: st.bag, paren_depth: (st.paren_depth - 1L));

    public static bool is_done(ParseState st) => current_kind(st) switch { EndOfFile { } => (bool)(true), _ => (bool)(false), };

    public static TokenKind peek_kind(ParseState st, long offset) => ((Func<long, TokenKind>)((idx) => ((idx >= ((long)st.tokens.Count)) ? new EndOfFile() : st.tokens[(int)idx].kind)))((st.pos + offset));

    public static bool is_ident(TokenKind k) => k switch { Identifier { } => (bool)(true), _ => (bool)(false), };

    public static bool is_ident_like(TokenKind k) => k switch { Identifier { } => (bool)(true), ActKeyword { } => (bool)(true), EndKeyword { } => (bool)(true), QedKeyword { } => (bool)(true), _ => (bool)(false), };

    public static bool is_type_ident(TokenKind k) => k switch { TypeIdentifier { } => (bool)(true), _ => (bool)(false), };

    public static bool is_arrow(TokenKind k) => k switch { Arrow { } => (bool)(true), _ => (bool)(false), };

    public static bool is_equals(TokenKind k) => k switch { Equals_ { } => (bool)(true), _ => (bool)(false), };

    public static bool is_colon(TokenKind k) => k switch { Colon { } => (bool)(true), _ => (bool)(false), };

    public static bool is_comma(TokenKind k) => k switch { Comma { } => (bool)(true), _ => (bool)(false), };

    public static bool is_pipe(TokenKind k) => k switch { Pipe { } => (bool)(true), _ => (bool)(false), };

    public static bool is_dot(TokenKind k) => k switch { Dot { } => (bool)(true), _ => (bool)(false), };

    public static bool is_left_paren(TokenKind k) => k switch { LeftParen { } => (bool)(true), _ => (bool)(false), };

    public static bool is_left_brace(TokenKind k) => k switch { LeftBrace { } => (bool)(true), _ => (bool)(false), };

    public static bool is_left_bracket(TokenKind k) => k switch { LeftBracket { } => (bool)(true), _ => (bool)(false), };

    public static bool is_right_brace(TokenKind k) => k switch { RightBrace { } => (bool)(true), _ => (bool)(false), };

    public static bool is_right_bracket(TokenKind k) => k switch { RightBracket { } => (bool)(true), _ => (bool)(false), };

    public static bool is_right_paren(TokenKind k) => k switch { RightParen { } => (bool)(true), _ => (bool)(false), };

    public static bool is_if_keyword(TokenKind k) => k switch { IfKeyword { } => (bool)(true), _ => (bool)(false), };

    public static bool is_is_keyword(TokenKind k) => k switch { IsKeyword { } => (bool)(true), _ => (bool)(false), };

    public static bool is_match_arm_start(TokenKind k) => is_is_keyword(k);

    public static bool is_otherwise_keyword(TokenKind k) => k switch { OtherwiseKeyword { } => (bool)(true), _ => (bool)(false), };

    public static bool is_wildcard_pat(TokenKind k) => is_otherwise_keyword(k);

    public static bool is_let_keyword(TokenKind k) => k switch { LetKeyword { } => (bool)(true), _ => (bool)(false), };

    public static bool is_when_keyword(TokenKind k) => k switch { WhenKeyword { } => (bool)(true), _ => (bool)(false), };

    public static bool is_act_keyword(TokenKind k) => k switch { ActKeyword { } => (bool)(true), _ => (bool)(false), };

    public static bool is_end_keyword(TokenKind k) => k switch { EndKeyword { } => (bool)(true), _ => (bool)(false), };

    public static bool is_with_keyword(TokenKind k) => k switch { WithKeyword { } => (bool)(true), _ => (bool)(false), };

    public static bool is_effect_keyword(TokenKind k) => k switch { EffectKeyword { } => (bool)(true), _ => (bool)(false), };

    public static bool is_cites_keyword(TokenKind k) => k switch { CitesKeyword { } => (bool)(true), _ => (bool)(false), };

    public static bool is_where_keyword(TokenKind k) => k switch { WhereKeyword { } => (bool)(true), _ => (bool)(false), };

    public static bool is_in_keyword(TokenKind k) => k switch { InKeyword { } => (bool)(true), _ => (bool)(false), };

    public static bool is_else_keyword(TokenKind k) => k switch { ElseKeyword { } => (bool)(true), _ => (bool)(false), };

    public static bool is_reserved_keyword(TokenKind k) => k switch { LetKeyword { } => (bool)(true), InKeyword { } => (bool)(true), IfKeyword { } => (bool)(true), IsKeyword { } => (bool)(true), OtherwiseKeyword { } => (bool)(true), ThenKeyword { } => (bool)(true), ElseKeyword { } => (bool)(true), WhenKeyword { } => (bool)(true), WhereKeyword { } => (bool)(true), SuchThatKeyword { } => (bool)(true), ActKeyword { } => (bool)(true), EndKeyword { } => (bool)(true), RecordKeyword { } => (bool)(true), CitesKeyword { } => (bool)(true), ClaimKeyword { } => (bool)(true), ProofKeyword { } => (bool)(true), QedKeyword { } => (bool)(true), ForAllKeyword { } => (bool)(true), ThereExistsKeyword { } => (bool)(true), LinearKeyword { } => (bool)(true), EffectKeyword { } => (bool)(true), WithKeyword { } => (bool)(true), TrueKeyword { } => (bool)(true), FalseKeyword { } => (bool)(true), _ => (bool)(false), };

    public static bool is_minus(TokenKind k) => k switch { Minus { } => (bool)(true), _ => (bool)(false), };

    public static bool is_dedent(TokenKind k) => k switch { Dedent { } => (bool)(true), _ => (bool)(false), };

    public static bool is_left_arrow(TokenKind k) => k switch { LeftArrow { } => (bool)(true), _ => (bool)(false), };

    public static bool is_record_keyword(TokenKind k) => k switch { RecordKeyword { } => (bool)(true), _ => (bool)(false), };

    public static bool is_underscore(TokenKind k) => k switch { Underscore { } => (bool)(true), _ => (bool)(false), };

    public static bool is_backslash(TokenKind k) => k switch { Backslash { } => (bool)(true), _ => (bool)(false), };

    public static bool is_literal(TokenKind k) => k switch { IntegerLiteral { } => (bool)(true), NumberLiteral { } => (bool)(true), TextLiteral { } => (bool)(true), CharLiteral { } => (bool)(true), TrueKeyword { } => (bool)(true), FalseKeyword { } => (bool)(true), _ => (bool)(false), };

    public static bool is_app_start(TokenKind k) => k switch { Identifier { } => (bool)(true), TypeIdentifier { } => (bool)(true), IntegerLiteral { } => (bool)(true), NumberLiteral { } => (bool)(true), TextLiteral { } => (bool)(true), CharLiteral { } => (bool)(true), TrueKeyword { } => (bool)(true), FalseKeyword { } => (bool)(true), LeftParen { } => (bool)(true), LeftBracket { } => (bool)(true), ActKeyword { } => (bool)(true), WithKeyword { } => (bool)(true), _ => (bool)(false), };

    public static bool is_compound(Expr e) => e switch { MatchExpr(var s, var arms) => (bool)(true), IfExpr(var c, var t, var el) => (bool)(true), LetExpr(var binds, var body) => (bool)(true), ActExpr(var stmts) => (bool)(true), _ => (bool)(false), };

    public static bool is_type_arg_start(TokenKind k) => k switch { TypeIdentifier { } => (bool)(true), Identifier { } => (bool)(true), LeftParen { } => (bool)(true), IntegerLiteral { } => (bool)(true), _ => (bool)(false), };

    public static long operator_precedence(TokenKind k) => k switch { PlusPlus { } => (long)(5L), ColonColon { } => (long)(5L), Plus { } => (long)(6L), Minus { } => (long)(6L), Star { } => (long)(7L), Slash { } => (long)(7L), Caret { } => (long)(8L), DoubleEquals { } => (long)(4L), NotEquals { } => (long)(4L), LessThan { } => (long)(4L), GreaterThan { } => (long)(4L), LessOrEqual { } => (long)(4L), GreaterOrEqual { } => (long)(4L), TripleEquals { } => (long)(4L), Ampersand { } => (long)(3L), Pipe { } => (long)(2L), _ => (long)((-1L)), };

    public static bool is_right_assoc(TokenKind k) => k switch { PlusPlus { } => (bool)(true), ColonColon { } => (bool)(true), Caret { } => (bool)(true), Arrow { } => (bool)(true), _ => (bool)(false), };

    public static ParseState expect(TokenKind kind, ParseState st) => (is_done(st) ? st : ((current_kind(st) == kind) ? advance(st) : ((Func<Token, ParseState>)((tok) => new ParseState(tokens: st.tokens, pos: st.pos, bag: bag_add(st.bag, make_error(cdx_expected_token_kind(), ("\u0027\u0024\u001F\u000D\u0018\u000E\u000D\u0016\u0002\u000E\u0010\u0022\u000D\u0012\u0002\u0022\u0011\u0012\u0016\u0002\u001A\u0011\u0013\u001A\u000F\u000E\u0018\u0014\u0042\u0002\u001D\u0010\u000E\u0002\u0047" + (tok.text + "\u0047")), span_at(tok.line, tok.column, tok.offset, ((long)tok.text.Length), tok.file_id))), paren_depth: st.paren_depth)))(current(st))));

    public static ParseState report_reserved_keyword(string context, ParseState st) => ((Func<Token, ParseState>)((tok) => new ParseState(tokens: st.tokens, pos: st.pos, bag: bag_add(st.bag, make_error(cdx_reserved_keyword_as_identifier(), ("\u0047" + (tok.text + ("\u0047\u0002\u0011\u0013\u0002\u000F\u0002\u0015\u000D\u0013\u000D\u0015\u0021\u000D\u0016\u0002\u0022\u000D\u001E\u001B\u0010\u0015\u0016\u0002\u000F\u0012\u0016\u0002\u0018\u000F\u0012\u0012\u0010\u000E\u0002\u0020\u000D\u0002\u0019\u0013\u000D\u0016\u0002\u000F\u0013\u0002" + (context + "\u0046\u0002\u0015\u000D\u0012\u000F\u001A\u000D\u0002\u0011\u000E")))), span_at(tok.line, tok.column, tok.offset, ((long)tok.text.Length), tok.file_id))), paren_depth: st.paren_depth)))(current(st));

    public static long skip_newlines_pos(List<Token> tokens, long pos, long len)
    {
        while (true)
        {
            if ((pos >= (len - 1L)))
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
            var _tco_1 = (pos + 1L);
            var _tco_2 = len;
            tokens = _tco_0;
            pos = _tco_1;
            len = _tco_2;
            continue;
            }
            else if (_tco_s is Indent _tco_m1)
            {
            var _tco_0 = tokens;
            var _tco_1 = (pos + 1L);
            var _tco_2 = len;
            tokens = _tco_0;
            pos = _tco_1;
            len = _tco_2;
            continue;
            }
            else if (_tco_s is Dedent _tco_m2)
            {
            var _tco_0 = tokens;
            var _tco_1 = (pos + 1L);
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

    public static ParseState skip_newlines(ParseState st) => ((Func<long, ParseState>)((end_pos) => ((end_pos == st.pos) ? st : new ParseState(tokens: st.tokens, pos: end_pos, bag: st.bag, paren_depth: st.paren_depth))))(skip_newlines_pos(st.tokens, st.pos, ((long)st.tokens.Count)));

    public static ParseExprResult parse_expr(ParseState st) => parse_expr_col(st, 0L);

    public static ParseExprResult parse_expr_col(ParseState st, long min_col) => parse_binary(st, 0L, min_col);

    public static ParseExprResult unwrap_expr_ok(ParseExprResult r, Func<Expr, Func<ParseState, ParseExprResult>> f) => r switch { ExprOk(var e, var st) => (ParseExprResult)(f(e)(st)), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static ParseExprResult parse_binary(ParseState st, long min_prec, long min_col) => ((Func<ParseExprResult, ParseExprResult>)((left_result) => unwrap_expr_ok(left_result, (_p0_) => (_p1_) => start_binary_loop(min_prec, min_col, _p0_, _p1_))))(parse_unary(st));

    public static ParseExprResult start_binary_loop(long min_prec, long min_col, Expr left, ParseState st) => parse_binary_loop(left, st, min_prec, min_col);

    public static ParseExprResult parse_binary_loop(Expr left, ParseState st, long min_prec, long min_col) => ((Func<ParseState, ParseExprResult>)((st1) => (is_done(st1) ? new ExprOk(left, st1) : ((((min_col > 0L) && (current(st1).column > 0L)) && (current(st1).column <= min_col)) ? new ExprOk(left, st1) : ((Func<long, ParseExprResult>)((prec) => ((prec < min_prec) ? new ExprOk(left, st1) : ((Func<Token, ParseExprResult>)((op) => ((Func<ParseState, ParseExprResult>)((st2) => ((Func<long, ParseExprResult>)((next_min) => ((Func<ParseExprResult, ParseExprResult>)((right_result) => unwrap_expr_ok(right_result, (_p0_) => (_p1_) => continue_binary(left, op, min_prec, min_col, _p0_, _p1_))))(parse_binary(st2, next_min, min_col))))((is_right_assoc(op.kind) ? prec : (prec + 1L)))))(skip_newlines(advance(st1)))))(current(st1)))))(operator_precedence(current_kind(st1)))))))(skip_newlines(st));

    public static ParseExprResult continue_binary(Expr left, Token op, long min_prec, long min_col, Expr right, ParseState st) => parse_binary_loop(new BinExpr(left, op, right), st, min_prec, min_col);

    public static ParseExprResult parse_unary(ParseState st) => (is_minus(current_kind(st)) ? ((Func<Token, ParseExprResult>)((op) => ((Func<ParseExprResult, ParseExprResult>)((result) => unwrap_expr_ok(result, (_p0_) => (_p1_) => finish_unary(op, _p0_, _p1_))))(parse_unary(advance(st)))))(current(st)) : parse_application(st));

    public static ParseExprResult finish_unary(Token op, Expr operand, ParseState st) => new ExprOk(new UnaryExpr(op, operand), st);

    public static ParseExprResult parse_application(ParseState st) => ((Func<ParseExprResult, ParseExprResult>)((func_result) => unwrap_expr_ok(func_result, (_p0_) => (_p1_) => parse_app_loop(_p0_, _p1_))))(parse_atom(st));

    public static ParseExprResult parse_app_loop(Expr func, ParseState st) => (is_compound(func) ? parse_dot_only(func, st) : (is_done(st) ? new ExprOk(func, st) : ((Func<ParseState, ParseExprResult>)((st1) => (is_app_start(current_kind(st1)) ? ((Func<ParseExprResult, ParseExprResult>)((arg_result) => unwrap_expr_ok(arg_result, (_p0_) => (_p1_) => continue_app(func, _p0_, _p1_))))(parse_atom(st1)) : parse_field_access(func, st1))))(((st.paren_depth > 0L) ? skip_newlines(st) : st))));

    public static ParseExprResult continue_app(Expr func, Expr arg, ParseState st) => parse_app_loop(new AppExpr(func, arg), st);

    public static ParseExprResult parse_atom(ParseState st) => (is_literal(current_kind(st)) ? new ExprOk(new LitExpr(current(st)), advance(st)) : (is_ident(current_kind(st)) ? parse_field_access(new NameExpr(current(st)), advance(st)) : (is_type_ident(current_kind(st)) ? parse_atom_type_ident(st) : (is_left_paren(current_kind(st)) ? parse_paren_expr(advance(st)) : (is_left_bracket(current_kind(st)) ? parse_list_expr(st) : (is_if_keyword(current_kind(st)) ? parse_if_expr(st) : (is_let_keyword(current_kind(st)) ? parse_let_expr(st) : (is_when_keyword(current_kind(st)) ? parse_match_expr(st) : (is_act_keyword(current_kind(st)) ? parse_act_expr(st) : (is_with_keyword(current_kind(st)) ? parse_handle_expr(st) : (is_backslash(current_kind(st)) ? parse_lambda_expr(st) : new ExprOk(new ErrExpr(current(st)), advance(st)))))))))))));

    public static ParseExprResult parse_field_access(Expr node, ParseState st)
    {
        while (true)
        {
            if (is_dot(current_kind(st)))
            {
            var st2 = advance(st);
            if (is_ident_like(current_kind(st2)))
            {
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
            if (is_ident_like(current_kind(st2)))
            {
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
            else
            {
            return new ExprOk(node, st);
            }
        }
    }

    public static ParseExprResult parse_atom_type_ident(ParseState st) => ((Func<Token, ParseExprResult>)((tok) => ((Func<ParseState, ParseExprResult>)((st2) => (is_left_brace(current_kind(st2)) ? ((Func<ParseExprResult, ParseExprResult>)((rec_result) => unwrap_expr_ok(rec_result, (_p0_) => (_p1_) => parse_field_access(_p0_, _p1_))))(parse_record_expr(tok, st2)) : parse_field_access(new NameExpr(tok), st2))))(advance(st))))(current(st));

    public static ParseExprResult parse_paren_expr(ParseState st) => ((Func<ParseState, ParseExprResult>)((st2) => ((Func<ParseExprResult, ParseExprResult>)((inner) => unwrap_expr_ok(inner, (_p0_) => (_p1_) => finish_paren_expr(_p0_, _p1_))))(parse_expr(st2))))(skip_newlines(enter_paren(st)));

    public static ParseExprResult finish_paren_expr(Expr e, ParseState st) => ((Func<ParseState, ParseExprResult>)((st2) => ((Func<ParseState, ParseExprResult>)((st3) => parse_field_access(new ParenExpr(e), exit_paren(st3))))(expect(new RightParen(), st2))))(skip_newlines(st));

    public static ParseExprResult parse_record_expr(Token type_name, ParseState st) => ((Func<ParseState, ParseExprResult>)((st2) => ((Func<ParseState, ParseExprResult>)((st3) => parse_record_expr_fields(type_name, new List<RecordFieldExpr>(), st3)))(skip_newlines(st2))))(advance(st));

    public static ParseExprResult parse_record_expr_fields(Token type_name, List<RecordFieldExpr> acc, ParseState st) => ((Func<TokenKind, ParseExprResult>)((kind) => (is_right_brace(kind) ? new ExprOk(new RecordExpr(type_name, acc), advance(st)) : (is_ident_like(kind) ? parse_record_field(type_name, acc, st) : new ExprOk(new RecordExpr(type_name, acc), st)))))(current_kind(st));

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

    public static ParseExprResult parse_let_bindings(List<LetBind> acc, ParseState st) => (is_ident(current_kind(st)) ? parse_let_binding(acc, st) : (is_reserved_keyword_let_name(st) ? parse_let_binding(acc, report_reserved_keyword("\u000F\u0002\u0017\u000D\u000E\u0049\u0020\u0011\u0012\u0016\u0011\u0012\u001D\u0002\u0012\u000F\u001A\u000D", st)) : (is_in_keyword(current_kind(st)) ? ((Func<ParseState, ParseExprResult>)((st2) => ((Func<ParseExprResult, ParseExprResult>)((body) => unwrap_expr_ok(body, (_p0_) => (_p1_) => finish_let(acc, _p0_, _p1_))))(parse_expr(st2))))(skip_newlines(advance(st))) : ((Func<ParseExprResult, ParseExprResult>)((body) => unwrap_expr_ok(body, (_p0_) => (_p1_) => finish_let(acc, _p0_, _p1_))))(parse_expr(st)))));

    public static bool is_reserved_keyword_let_name(ParseState st) => (is_reserved_keyword(current_kind(st)) ? is_equals(peek_kind(st, 1L)) : false);

    public static ParseExprResult finish_let(List<LetBind> acc, Expr b, ParseState st) => new ExprOk(new LetExpr(acc, b), st);

    public static ParseExprResult parse_let_binding(List<LetBind> acc, ParseState st) => ((Func<Token, ParseExprResult>)((name_tok) => ((Func<ParseState, ParseExprResult>)((st2) => ((Func<ParseState, ParseExprResult>)((st3) => ((Func<ParseExprResult, ParseExprResult>)((val_result) => unwrap_expr_ok(val_result, (_p0_) => (_p1_) => finish_let_binding(acc, name_tok, _p0_, _p1_))))(parse_expr(st3))))(expect(new Equals_(), st2))))(advance(st))))(current(st));

    public static ParseExprResult finish_let_binding(List<LetBind> acc, Token name_tok, Expr v, ParseState st) => ((Func<LetBind, ParseExprResult>)((binding) => ((Func<ParseState, ParseExprResult>)((st2) => (is_comma(current_kind(st2)) ? parse_let_bindings(((Func<List<LetBind>>)(() => { var _l = acc; _l.Add(binding); return _l; }))(), skip_newlines(advance(st2))) : parse_let_bindings(((Func<List<LetBind>>)(() => { var _l = acc; _l.Add(binding); return _l; }))(), st2))))(skip_newlines(st))))(new LetBind(name: name_tok, value: v));

    public static ParseExprResult parse_match_expr(ParseState st) => ((Func<ParseState, ParseExprResult>)((st2) => ((Func<ParseExprResult, ParseExprResult>)((scrut) => unwrap_expr_ok(scrut, (_p0_) => (_p1_) => start_match_branches(_p0_, _p1_))))(parse_expr(st2))))(advance(st));

    public static ParseExprResult start_match_branches(Expr s, ParseState st) => ((Func<ParseState, ParseExprResult>)((st2) => ((Func<Token, ParseExprResult>)((tok) => parse_match_branches(s, new List<MatchArm>(), tok.column, tok.line, st2)))(current(st2))))(skip_newlines(st));

    public static ParseExprResult parse_match_branches(Expr scrut, List<MatchArm> acc, long col, long ln, ParseState st) => ((Func<Token, ParseExprResult>)((tok) => ((is_match_arm_start(current_kind(st)) && ((tok.line == ln) || (tok.column == col))) ? parse_one_match_branch(scrut, acc, col, ln, st) : new ExprOk(new MatchExpr(scrut, acc), st))))(current(st));

    public static ParseExprResult unwrap_pat_for_expr(ParsePatResult r, Func<Pat, Func<ParseState, ParseExprResult>> f) => r switch { PatOk(var p, var st) => (ParseExprResult)(f(p)(st)), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static ParseExprResult parse_one_match_branch(Expr scrut, List<MatchArm> acc, long col, long ln, ParseState st) => ((Func<ParseState, ParseExprResult>)((st2) => ((Func<ParsePatResult, ParseExprResult>)((pat) => unwrap_pat_for_expr(pat, (_p0_) => (_p1_) => parse_match_branch_body(scrut, acc, col, ln, _p0_, _p1_))))(parse_pattern(st2))))(advance(st));

    public static ParseExprResult parse_match_branch_body(Expr scrut, List<MatchArm> acc, long col, long ln, Pat p, ParseState st) => ((Func<ParseState, ParseExprResult>)((st2) => ((Func<ParseState, ParseExprResult>)((st3) => ((Func<ParseExprResult, ParseExprResult>)((body) => unwrap_expr_ok(body, (_p0_) => (_p1_) => finish_match_branch(scrut, acc, col, ln, p, _p0_, _p1_))))(parse_expr(st3))))(skip_newlines(st2))))(expect(new Arrow(), st));

    public static ParseExprResult finish_match_branch(Expr scrut, List<MatchArm> acc, long col, long ln, Pat p, Expr b, ParseState st) => ((Func<MatchArm, ParseExprResult>)((arm) => ((Func<ParseState, ParseExprResult>)((st2) => parse_match_branches(scrut, ((Func<List<MatchArm>>)(() => { var _l = acc; _l.Add(arm); return _l; }))(), col, ln, st2)))(skip_newlines(st))))(new MatchArm(pattern: p, body: b));

    public static bool looks_like_top_level_def(ParseState st) => ((is_ident(current_kind(st)) || is_type_ident(current_kind(st))) ? is_colon(peek_kind(st, 1L)) : false);

    public static bool is_act_bind(ParseState st) => (is_ident(current_kind(st)) ? is_left_arrow(peek_kind(st, 1L)) : false);

    public static ParseExprResult parse_act_expr(ParseState st) => ((Func<ParseState, ParseExprResult>)((st2) => parse_act_stmts(new List<ActStmt>(), st2)))(skip_newlines(advance(st)));

    public static ParseExprResult parse_act_stmts(List<ActStmt> acc, ParseState st) => (is_done(st) ? new ExprOk(new ActExpr(acc), st) : (is_end_keyword(current_kind(st)) ? new ExprOk(new ActExpr(acc), advance(st)) : (is_act_bind(st) ? parse_act_bind_stmt(acc, st) : parse_act_expr_stmt(acc, st))));

    public static ParseExprResult parse_act_bind_stmt(List<ActStmt> acc, ParseState st) => ((Func<Token, ParseExprResult>)((name_tok) => ((Func<ParseState, ParseExprResult>)((st2) => ((Func<ParseExprResult, ParseExprResult>)((val_result) => unwrap_expr_ok(val_result, (_p0_) => (_p1_) => finish_act_bind(acc, name_tok, _p0_, _p1_))))(parse_expr(st2))))(advance(advance(st)))))(current(st));

    public static ParseExprResult finish_act_bind(List<ActStmt> acc, Token name_tok, Expr v, ParseState st) => ((Func<ParseState, ParseExprResult>)((st2) => parse_act_stmts(((Func<List<ActStmt>>)(() => { var _l = acc; _l.Add(new ActBindStmt(name_tok, v)); return _l; }))(), st2)))(skip_newlines(st));

    public static ParseExprResult parse_act_expr_stmt(List<ActStmt> acc, ParseState st) => ((Func<ParseExprResult, ParseExprResult>)((expr_result) => unwrap_expr_ok(expr_result, (_p0_) => (_p1_) => finish_act_expr(acc, _p0_, _p1_))))(parse_expr(st));

    public static ParseExprResult finish_act_expr(List<ActStmt> acc, Expr e, ParseState st) => ((Func<ParseState, ParseExprResult>)((st2) => parse_act_stmts(((Func<List<ActStmt>>)(() => { var _l = acc; _l.Add(new ActExprStmt(e)); return _l; }))(), st2)))(skip_newlines(st));

    public static ParseExprResult parse_handle_expr(ParseState st) => ((Func<ParseState, ParseExprResult>)((st1) => ((Func<Token, ParseExprResult>)((eff_tok) => ((Func<ParseState, ParseExprResult>)((st2) => ((Func<ParseExprResult, ParseExprResult>)((body_result) => unwrap_expr_ok(body_result, (_p0_) => (_p1_) => finish_handle_body(eff_tok, _p0_, _p1_))))(parse_expr(st2))))(advance(st1))))(current(st1))))(advance(st));

    public static ParseExprResult finish_handle_body(Token eff_tok, Expr body, ParseState st) => ((Func<ParseState, ParseExprResult>)((st2) => ((Func<HandleParseResult, ParseExprResult>)((clauses) => new ExprOk(new HandleExpr(eff_tok, body, clauses.clauses), clauses.state)))(parse_handle_clauses(st2, new List<HandleClause>()))))(skip_newlines(st));

    public static HandleParseResult parse_handle_clauses(ParseState st, List<HandleClause> acc) => (is_ident(current_kind(st)) ? ((Func<Token, HandleParseResult>)((op_tok) => ((Func<ParseState, HandleParseResult>)((st1) => ((Func<HandleParamsResult, HandleParseResult>)((@params) => ((((long)@params.toks.Count) > 0L) ? ((Func<Token, HandleParseResult>)((resume_tok) => ((Func<ParseState, HandleParseResult>)((st5) => ((Func<ParseState, HandleParseResult>)((st6) => ((Func<ParseExprResult, HandleParseResult>)((body_result) => unwrap_handle_clause_body(op_tok, resume_tok, body_result, acc)))(parse_expr(st6))))(skip_newlines(st5))))(expect(new Equals_(), @params.state))))(@params.toks[(int)(((long)@params.toks.Count) - 1L)]) : new HandleParseResult(clauses: acc, state: st))))(parse_handle_params(st1, new List<Token>()))))(advance(st))))(current(st)) : new HandleParseResult(clauses: acc, state: st));

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

    public static HandleParseResult unwrap_handle_clause_body(Token op_tok, Token resume_tok, ParseExprResult result, List<HandleClause> acc) => result switch { ExprOk(var body, var st) => (HandleParseResult)(((Func<HandleClause, HandleParseResult>)((clause) => ((Func<ParseState, HandleParseResult>)((st2) => parse_handle_clauses(st2, ((Func<List<HandleClause>>)(() => { var _l = acc; _l.Add(clause); return _l; }))())))(skip_newlines(st))))(new HandleClause(op_name: op_tok, resume_name: resume_tok, body: body))), _ => throw new InvalidOperationException("Non-exhaustive match"), };

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

    public static CodexType types__type_checker_resolve_type_expr(List<TypeBinding> tdm, ATypeExpr texpr) => texpr switch { ANamedType(var name, var s) => (CodexType)(resolve_type_name(tdm, name.value)), AFunType(var param, var ret, var s) => (CodexType)(new FunTy(types__type_checker_resolve_type_expr(tdm, param), types__type_checker_resolve_type_expr(tdm, ret))), AAppType(var ctor, var args, var s) => (CodexType)(resolve_applied_type(tdm, ctor, args)), AEffectType(var effs, var ret, var s) => (CodexType)(new EffectfulTy(effs, types__type_checker_resolve_type_expr(tdm, ret))), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static CodexType resolve_applied_type(List<TypeBinding> tdm, ATypeExpr ctor, List<ATypeExpr> args) => ctor switch { ANamedType(var name, var s) => (CodexType)(((name.value == "\u0031\u0011\u0013\u000E") ? ((((long)args.Count) == 1L) ? new ListTy(types__type_checker_resolve_type_expr(tdm, args[(int)0L])) : new ListTy(new ErrorTy())) : ((name.value == "\u0031\u0011\u0012\u0022\u000D\u0016\u0031\u0011\u0013\u000E") ? ((((long)args.Count) == 1L) ? new LinkedListTy(types__type_checker_resolve_type_expr(tdm, args[(int)0L])) : new LinkedListTy(new ErrorTy())) : new ConstructedTy(name, types__type_checker_resolve_type_expr_list(tdm, args, 0L, ((long)args.Count), new List<CodexType>()))))), _ => (CodexType)(types__type_checker_resolve_type_expr(tdm, ctor)), };

    public static List<CodexType> types__type_checker_resolve_type_expr_list(List<TypeBinding> tdm, List<ATypeExpr> args, long i, long len, List<CodexType> acc)
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
            var _tco_2 = (i + 1L);
            var _tco_3 = len;
            var _tco_4 = ((Func<List<CodexType>>)(() => { var _l = acc; _l.Add(types__type_checker_resolve_type_expr(tdm, args[(int)i])); return _l; }))();
            tdm = _tco_0;
            args = _tco_1;
            i = _tco_2;
            len = _tco_3;
            acc = _tco_4;
            continue;
            }
        }
    }

    public static CodexType resolve_type_name(List<TypeBinding> tdm, string name) => ((name == "\u002B\u0012\u000E\u000D\u001D\u000D\u0015") ? new IntegerTy() : ((name == "\u002C\u0019\u001A\u0020\u000D\u0015") ? new NumberTy() : ((name == "\u0028\u000D\u0024\u000E") ? new TextTy() : ((name == "\u003A\u0010\u0010\u0017\u000D\u000F\u0012") ? new BooleanTy() : ((name == "\u0032\u0014\u000F\u0015") ? new CharTy() : ((name == "\u002C\u0010\u000E\u0014\u0011\u0012\u001D") ? new NothingTy() : lookup_type_def(tdm, name)))))));

    public static CodexType lookup_type_def(List<TypeBinding> tdm, string name) => ((Func<long, CodexType>)((len) => ((len == 0L) ? new ConstructedTy(new Name(value: name), new List<CodexType>()) : ((Func<long, CodexType>)((pos) => ((pos >= len) ? new ConstructedTy(new Name(value: name), new List<CodexType>()) : ((Func<TypeBinding, CodexType>)((b) => ((b.name == name) ? b.bound_type : new ConstructedTy(new Name(value: name), new List<CodexType>()))))(tdm[(int)pos]))))(bsearch_text_pos(tdm, name, 0L, len)))))(((long)tdm.Count));

    public static bool is_value_name(string name) => ((((long)name.Length) == 0L) ? false : ((Func<long, bool>)((code) => ((code >= 13) && (code <= 38))))(((long)name[(int)0L])));

    public static ParamResult parameterize_type(UnificationState st, CodexType ty) => ((Func<WalkResult, ParamResult>)((r) => ((Func<CodexType, ParamResult>)((wrapped) => new ParamResult(parameterized: wrapped, entries: r.entries, state: r.state)))(wrap_forall_from_entries(r.walked, r.entries, 0L, ((long)r.entries.Count)))))(parameterize_walk(st, new List<ParamEntry>(), ty));

    public static CodexType wrap_forall_from_entries(CodexType ty, List<ParamEntry> entries, long i, long len) => ((i == len) ? ty : ((Func<ParamEntry, CodexType>)((e) => new ForAllTy(e.var_id, wrap_forall_from_entries(ty, entries, (i + 1L), len))))(entries[(int)i]));

    public static WalkResult parameterize_walk(UnificationState st, List<ParamEntry> entries, CodexType ty) => ty switch { ConstructedTy(var name, var args) => (WalkResult)((((((long)args.Count) == 0L) && is_value_name(name.value)) ? ((Func<long, WalkResult>)((looked) => ((looked >= 0L) ? new WalkResult(walked: new TypeVar(looked), entries: entries, state: st) : ((Func<FreshResult, WalkResult>)((fr) => fr.var_type switch { TypeVar(var new_id) => (WalkResult)(((Func<ParamEntry, WalkResult>)((new_entry) => new WalkResult(walked: fr.var_type, entries: Enumerable.Concat(entries, new List<ParamEntry> { new_entry }).ToList(), state: fr.state)))(new ParamEntry(param_name: name.value, var_id: new_id))), _ => (WalkResult)(new WalkResult(walked: ty, entries: entries, state: fr.state)), }))(fresh_and_advance(st)))))(find_param_entry(entries, name.value, 0L, ((long)entries.Count))) : ((Func<WalkListResult, WalkResult>)((args_r) => new WalkResult(walked: new ConstructedTy(name, args_r.walked_list), entries: args_r.entries, state: args_r.state)))(parameterize_walk_list(st, entries, args, 0L, ((long)args.Count), new List<CodexType>())))), FunTy(var param, var ret) => (WalkResult)(((Func<WalkResult, WalkResult>)((pr) => ((Func<WalkResult, WalkResult>)((rr) => new WalkResult(walked: new FunTy(pr.walked, rr.walked), entries: rr.entries, state: rr.state)))(parameterize_walk(pr.state, pr.entries, ret))))(parameterize_walk(st, entries, param))), ListTy(var elem) => (WalkResult)(((Func<WalkResult, WalkResult>)((er) => new WalkResult(walked: new ListTy(er.walked), entries: er.entries, state: er.state)))(parameterize_walk(st, entries, elem))), LinkedListTy(var elem) => (WalkResult)(((Func<WalkResult, WalkResult>)((er) => new WalkResult(walked: new LinkedListTy(er.walked), entries: er.entries, state: er.state)))(parameterize_walk(st, entries, elem))), ForAllTy(var id, var body) => (WalkResult)(((Func<WalkResult, WalkResult>)((br) => new WalkResult(walked: new ForAllTy(id, br.walked), entries: br.entries, state: br.state)))(parameterize_walk(st, entries, body))), _ => (WalkResult)(new WalkResult(walked: ty, entries: entries, state: st)), };

    public static long find_param_entry(List<ParamEntry> entries, string name, long i, long len)
    {
        while (true)
        {
            if ((i == len))
            {
            return (-1L);
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
            var _tco_2 = (i + 1L);
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
            var _tco_3 = (i + 1L);
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

    public static CheckResult check_def(UnificationState st, TypeEnv env, ADef def) => ((Func<DefSetup, CheckResult>)((declared) => ((Func<DefParamResult, CheckResult>)((env2) => ((Func<CheckResult, CheckResult>)((body_r) => ((Func<UnifyResult, CheckResult>)((u) => new CheckResult(inferred_type: declared.expected_type, state: u.state)))(unify(body_r.state, env2.remaining_type, body_r.inferred_type, aexpr_span(def.body)))))(infer_expr(env2.state, env2.env, def.body))))(bind_def_params(declared.state, declared.env, def.@params, declared.expected_type, 0L, ((long)def.@params.Count)))))(resolve_declared_type(st, env, def));

    public static DefSetup resolve_declared_type(UnificationState st, TypeEnv env, ADef def) => ((((long)def.declared_type.Count) == 0L) ? ((Func<FreshResult, DefSetup>)((fr) => new DefSetup(expected_type: fr.var_type, remaining_type: fr.var_type, state: fr.state, env: env)))(fresh_and_advance(st)) : ((Func<CodexType, DefSetup>)((env_type) => ((Func<FreshResult, DefSetup>)((inst) => new DefSetup(expected_type: inst.var_type, remaining_type: inst.var_type, state: inst.state, env: env)))(instantiate_type(st, env_type))))(env_lookup(env, def.name.value)));

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

    public static ChapterResult check_chapter(AChapter mod) => ((Func<List<TypeBinding>, ChapterResult>)((tdm) => ((Func<LetBindResult, ChapterResult>)((tenv) => ((Func<LetBindResult, ChapterResult>)((env) => check_all_defs(env.state, env.env, mod.defs, 0L, ((long)mod.defs.Count), new List<TypeBinding>())))(register_all_defs(tenv.state, tenv.env, tdm, mod.defs, 0L, ((long)mod.defs.Count)))))(register_type_defs(empty_unification_state(), builtin_type_env(), tdm, mod.type_defs, 0L, ((long)mod.type_defs.Count)))))(build_type_def_map(mod.type_defs, 0L, ((long)mod.type_defs.Count), new List<TypeBinding>()));

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
            var ty = ((((long)def.declared_type.Count) == 0L) ? ((Func<FreshResult, LetBindResult>)((fr) => ((Func<TypeEnv, LetBindResult>)((env2) => new LetBindResult(state: fr.state, env: env2)))(env_bind(env, def.name.value, fr.var_type))))(fresh_and_advance(st)) : ((Func<CodexType, LetBindResult>)((resolved) => ((Func<ParamResult, LetBindResult>)((pr) => new LetBindResult(state: pr.state, env: env_bind(env, def.name.value, pr.parameterized))))(parameterize_type(st, resolved))))(types__type_checker_resolve_type_expr(tdm, def.declared_type[(int)0L])));
            var _tco_0 = ty.state;
            var _tco_1 = ty.env;
            var _tco_2 = tdm;
            var _tco_3 = defs;
            var _tco_4 = (i + 1L);
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
            var _tco_3 = (i + 1L);
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
            var entry = td switch { AVariantTypeDef(var name, var type_params, var ctors, var s) => (TypeBinding)(((Func<List<SumCtor>, TypeBinding>)((sum_ctors) => new TypeBinding(name: name.value, bound_type: new SumTy(name, sum_ctors))))(types__type_checker_build_sum_ctors(tdefs, ctors, 0L, ((long)ctors.Count), new List<SumCtor>(), acc))), ARecordTypeDef(var name, var type_params, var fields, var s) => (TypeBinding)(((Func<List<RecordField>, TypeBinding>)((rec_fields) => new TypeBinding(name: name.value, bound_type: new RecordTy(name, rec_fields))))(build_record_fields_for_map(tdefs, fields, 0L, ((long)fields.Count), new List<RecordField>(), acc))), _ => throw new InvalidOperationException("Non-exhaustive match"), };
            var _tco_0 = tdefs;
            var _tco_1 = (i + 1L);
            var _tco_2 = len;
            var _tco_3 = ((Func<List<TypeBinding>>)(() => { var _l = new List<TypeBinding>(acc); _l.Insert((int)bsearch_text_pos(acc, entry.name, 0L, ((long)acc.Count)), entry); return _l; }))();
            tdefs = _tco_0;
            i = _tco_1;
            len = _tco_2;
            acc = _tco_3;
            continue;
            }
        }
    }

    public static List<SumCtor> types__type_checker_build_sum_ctors(List<ATypeDef> tdefs, List<AVariantCtorDef> ctors, long i, long len, List<SumCtor> acc, List<TypeBinding> partial_tdm)
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
            var field_types = resolve_type_expr_list_for_map(tdefs, c.fields, 0L, ((long)c.fields.Count), new List<CodexType>(), partial_tdm);
            var sc = new SumCtor(name: c.name, fields: field_types);
            var _tco_0 = tdefs;
            var _tco_1 = ctors;
            var _tco_2 = (i + 1L);
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
            var rfield = new RecordField(name: f.name, type_val: types__type_checker_resolve_type_expr(partial_tdm, f.type_expr));
            var _tco_0 = tdefs;
            var _tco_1 = fields;
            var _tco_2 = (i + 1L);
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
            var _tco_2 = (i + 1L);
            var _tco_3 = len;
            var _tco_4 = ((Func<List<CodexType>>)(() => { var _l = acc; _l.Add(types__type_checker_resolve_type_expr(partial_tdm, args[(int)i])); return _l; }))();
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
            var _tco_4 = (i + 1L);
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

    public static LetBindResult register_one_type_def(UnificationState st, TypeEnv env, List<TypeBinding> tdm, ATypeDef td) => td switch { AVariantTypeDef(var name, var type_params, var ctors, var s) => (LetBindResult)(((Func<CodexType, LetBindResult>)((result_ty) => register_variant_ctors(st, env, tdm, ctors, result_ty, 0L, ((long)ctors.Count))))(lookup_type_def(tdm, name.value))), ARecordTypeDef(var name, var type_params, var fields, var s) => (LetBindResult)(((Func<List<RecordField>, LetBindResult>)((resolved_fields) => ((Func<CodexType, LetBindResult>)((result_ty) => ((Func<CodexType, LetBindResult>)((ctor_ty) => ((Func<ParamResult, LetBindResult>)((pr) => new LetBindResult(state: pr.state, env: env_bind(env, name.value, pr.parameterized))))(parameterize_type(st, ctor_ty))))(types__type_checker_build_record_ctor_type(tdm, fields, result_ty, 0L, ((long)fields.Count)))))(new RecordTy(name, resolved_fields))))(types__type_checker_build_record_fields(tdm, fields, 0L, ((long)fields.Count), new List<RecordField>()))), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static List<RecordField> types__type_checker_build_record_fields(List<TypeBinding> tdm, List<ARecordFieldDef> fields, long i, long len, List<RecordField> acc)
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
            var rfield = new RecordField(name: f.name, type_val: types__type_checker_resolve_type_expr(tdm, f.type_expr));
            var _tco_0 = tdm;
            var _tco_1 = fields;
            var _tco_2 = (i + 1L);
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

    public static CodexType lookup_record_field(List<RecordField> fields, string name) => ((((long)fields.Count) == 0L) ? new ErrorTy() : ((Func<RecordField, CodexType>)((f) => ((f.name.value == name) ? f.type_val : lookup_record_field_loop(fields, name, 1L, ((long)fields.Count)))))(fields[(int)0L]));

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
            var _tco_2 = (i + 1L);
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
            var ctor_ty = types__type_checker_build_ctor_type(tdm, ctor.fields, result_ty, 0L, ((long)ctor.fields.Count));
            var pr = parameterize_type(st, ctor_ty);
            var env2 = env_bind(env, ctor.name.value, pr.parameterized);
            var _tco_0 = pr.state;
            var _tco_1 = env2;
            var _tco_2 = tdm;
            var _tco_3 = ctors;
            var _tco_4 = result_ty;
            var _tco_5 = (i + 1L);
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

    public static CodexType types__type_checker_build_ctor_type(List<TypeBinding> tdm, List<ATypeExpr> fields, CodexType result, long i, long len) => ((i == len) ? result : ((Func<CodexType, CodexType>)((rest) => new FunTy(types__type_checker_resolve_type_expr(tdm, fields[(int)i]), rest)))(types__type_checker_build_ctor_type(tdm, fields, result, (i + 1L), len)));

    public static CodexType types__type_checker_build_record_ctor_type(List<TypeBinding> tdm, List<ARecordFieldDef> fields, CodexType result, long i, long len) => ((i == len) ? result : ((Func<ARecordFieldDef, CodexType>)((f) => ((Func<CodexType, CodexType>)((rest) => new FunTy(types__type_checker_resolve_type_expr(tdm, f.type_expr), rest)))(types__type_checker_build_record_ctor_type(tdm, fields, result, (i + 1L), len))))(fields[(int)i]));

    public static CheckResult infer_literal(UnificationState st, LiteralKind kind) => kind switch { IntLit { } => (CheckResult)(new CheckResult(inferred_type: new IntegerTy(), state: st)), NumLit { } => (CheckResult)(new CheckResult(inferred_type: new NumberTy(), state: st)), TextLit { } => (CheckResult)(new CheckResult(inferred_type: new TextTy(), state: st)), CharLit { } => (CheckResult)(new CheckResult(inferred_type: new CharTy(), state: st)), BoolLit { } => (CheckResult)(new CheckResult(inferred_type: new BooleanTy(), state: st)), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static CheckResult infer_name(UnificationState st, TypeEnv env, string name, SourceSpan span) => (env_has(env, name) ? ((Func<CodexType, CheckResult>)((raw) => ((Func<FreshResult, CheckResult>)((inst) => new CheckResult(inferred_type: inst.var_type, state: inst.state)))(instantiate_type(st, raw))))(env_lookup(env, name)) : new CheckResult(inferred_type: new ErrorTy(), state: add_unify_error(st, cdx_unknown_name(), ("\u0033\u0012\u0022\u0012\u0010\u001B\u0012\u0002\u0012\u000F\u001A\u000D\u0045\u0002" + name), span)));

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

    public static CodexType subst_type_var(CodexType ty, long var_id, CodexType replacement) => ty switch { TypeVar(var id) => (CodexType)(((id == var_id) ? replacement : ty)), FunTy(var param, var ret) => (CodexType)(new FunTy(subst_type_var(param, var_id, replacement), subst_type_var(ret, var_id, replacement))), ListTy(var elem) => (CodexType)(new ListTy(subst_type_var(elem, var_id, replacement))), LinkedListTy(var elem) => (CodexType)(new LinkedListTy(subst_type_var(elem, var_id, replacement))), ForAllTy(var inner_id, var body) => (CodexType)(((inner_id == var_id) ? ty : new ForAllTy(inner_id, subst_type_var(body, var_id, replacement)))), ConstructedTy(var name, var args) => (CodexType)(new ConstructedTy(name, map_subst_type_var(args, var_id, replacement, 0L, ((long)args.Count), new List<CodexType>()))), SumTy(var name, var ctors) => (CodexType)(ty), RecordTy(var name, var fields) => (CodexType)(ty), _ => (CodexType)(ty), };

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
            var _tco_3 = (i + 1L);
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

    public static CheckResult infer_binary(UnificationState st, TypeEnv env, AExpr left, BinaryOp op, AExpr right, SourceSpan span) => ((Func<CheckResult, CheckResult>)((lr) => ((Func<CheckResult, CheckResult>)((rr) => infer_binary_op(rr.state, lr.inferred_type, rr.inferred_type, op, span)))(infer_expr(lr.state, env, right))))(infer_expr(st, env, left));

    public static CheckResult infer_binary_op(UnificationState st, CodexType lt, CodexType rt, BinaryOp op, SourceSpan span) => op switch { OpAdd { } => (CheckResult)(infer_arithmetic(st, lt, rt, span)), OpSub { } => (CheckResult)(infer_arithmetic(st, lt, rt, span)), OpMul { } => (CheckResult)(infer_arithmetic(st, lt, rt, span)), OpDiv { } => (CheckResult)(infer_arithmetic(st, lt, rt, span)), OpPow { } => (CheckResult)(infer_arithmetic(st, lt, rt, span)), OpEq { } => (CheckResult)(infer_comparison(st, lt, rt, span)), OpNotEq { } => (CheckResult)(infer_comparison(st, lt, rt, span)), OpLt { } => (CheckResult)(infer_comparison(st, lt, rt, span)), OpGt { } => (CheckResult)(infer_comparison(st, lt, rt, span)), OpLtEq { } => (CheckResult)(infer_comparison(st, lt, rt, span)), OpGtEq { } => (CheckResult)(infer_comparison(st, lt, rt, span)), OpAnd { } => (CheckResult)(infer_logical(st, lt, rt, span)), OpOr { } => (CheckResult)(infer_logical(st, lt, rt, span)), OpAppend { } => (CheckResult)(infer_append(st, lt, rt, span)), OpCons { } => (CheckResult)(infer_cons(st, lt, rt, span)), OpDefEq { } => (CheckResult)(infer_comparison(st, lt, rt, span)), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static CheckResult infer_arithmetic(UnificationState st, CodexType lt, CodexType rt, SourceSpan span) => ((Func<UnifyResult, CheckResult>)((r) => new CheckResult(inferred_type: lt, state: r.state)))(unify(st, lt, rt, span));

    public static CheckResult infer_comparison(UnificationState st, CodexType lt, CodexType rt, SourceSpan span) => ((Func<UnifyResult, CheckResult>)((r) => new CheckResult(inferred_type: new BooleanTy(), state: r.state)))(unify(st, lt, rt, span));

    public static CheckResult infer_logical(UnificationState st, CodexType lt, CodexType rt, SourceSpan span) => ((Func<UnifyResult, CheckResult>)((r1) => ((Func<UnifyResult, CheckResult>)((r2) => new CheckResult(inferred_type: new BooleanTy(), state: r2.state)))(unify(r1.state, rt, new BooleanTy(), span))))(unify(st, lt, new BooleanTy(), span));

    public static CheckResult infer_append(UnificationState st, CodexType lt, CodexType rt, SourceSpan span) => ((Func<CodexType, CheckResult>)((resolved) => resolved switch { TextTy { } => (CheckResult)(((Func<UnifyResult, CheckResult>)((r) => new CheckResult(inferred_type: new TextTy(), state: r.state)))(unify(st, rt, new TextTy(), span))), _ => (CheckResult)(((Func<UnifyResult, CheckResult>)((r) => new CheckResult(inferred_type: lt, state: r.state)))(unify(st, lt, rt, span))), }))(resolve(st, lt));

    public static CheckResult infer_cons(UnificationState st, CodexType lt, CodexType rt, SourceSpan span) => ((Func<CodexType, CheckResult>)((list_ty) => ((Func<UnifyResult, CheckResult>)((r) => new CheckResult(inferred_type: list_ty, state: r.state)))(unify(st, rt, list_ty, span))))(new ListTy(lt));

    public static CheckResult infer_if(UnificationState st, TypeEnv env, AExpr cond, AExpr then_e, AExpr else_e, SourceSpan span) => ((Func<CheckResult, CheckResult>)((cr) => ((Func<UnifyResult, CheckResult>)((r1) => ((Func<CheckResult, CheckResult>)((tr) => ((Func<CheckResult, CheckResult>)((er) => ((Func<UnifyResult, CheckResult>)((r2) => new CheckResult(inferred_type: tr.inferred_type, state: r2.state)))(unify(er.state, tr.inferred_type, er.inferred_type, span))))(infer_expr(tr.state, env, else_e))))(infer_expr(r1.state, env, then_e))))(unify(cr.state, cr.inferred_type, new BooleanTy(), aexpr_span(cond)))))(infer_expr(st, env, cond));

    public static CheckResult infer_let(UnificationState st, TypeEnv env, List<ALetBind> bindings, AExpr body) => ((Func<LetBindResult, CheckResult>)((env2) => infer_expr(env2.state, env2.env, body)))(infer_let_bindings(st, env, bindings, 0L, ((long)bindings.Count)));

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
            var bound_ty = unwrap_effectful_or_error(vr.inferred_type, b.name.value, aexpr_span(b.value), vr.state);
            var env2 = env_bind(env, b.name.value, bound_ty.ty);
            var _tco_0 = bound_ty.state;
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

    public static UnwrapResult unwrap_effectful_or_error(CodexType ty, string name, SourceSpan span, UnificationState st) => ty switch { EffectfulTy(var effs, var inner) => (UnwrapResult)(((((long)effs.Count) == 0L) ? new UnwrapResult(ty: ty, state: st) : new UnwrapResult(ty: inner, state: add_unify_error(st, cdx_let_binds_effectful_value(), ("\u0017\u000D\u000E\u0049\u0020\u0011\u0012\u0016\u0011\u0012\u001D\u0002\u0047" + (name + ("\u0047\u0002\u000E\u0010\u0002\u000F\u0012\u0002\u000D\u001C\u001C\u000D\u0018\u000E\u001C\u0019\u0017\u0002\u0021\u000F\u0017\u0019\u000D\u0002\u0011\u0013\u0002\u0012\u0010\u000E\u0002\u000F\u0017\u0017\u0010\u001B\u000D\u0016\u0002\u0010\u0019\u000E\u0013\u0011\u0016\u000D\u0002\u000F\u0012\u0002\u000F\u0018\u000E\u0049\u0020\u0011\u0012\u0016\u0041\u0002\u0033\u0013\u000D\u0002\u0047" + (name + "\u0002\u004F\u0049\u0002\u0041\u0041\u0041\u0047\u0002\u0011\u0012\u0013\u0011\u0016\u000D\u0002\u000F\u0012\u0002\u000F\u0018\u000E\u0002\u0020\u0017\u0010\u0018\u0022\u0041")))), span)))), _ => (UnwrapResult)(new UnwrapResult(ty: ty, state: st)), };

    public static CheckResult infer_lambda(UnificationState st, TypeEnv env, List<Name> @params, AExpr body) => ((Func<LambdaBindResult, CheckResult>)((pr) => ((Func<CheckResult, CheckResult>)((br) => ((Func<CodexType, CheckResult>)((fun_ty) => new CheckResult(inferred_type: fun_ty, state: br.state)))(wrap_fun_type(pr.param_types, br.inferred_type))))(infer_expr(pr.state, pr.env, body))))(bind_lambda_params(st, env, @params, 0L, ((long)@params.Count), new List<CodexType>()));

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
            var _tco_3 = (i + 1L);
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

    public static CodexType wrap_fun_type(List<CodexType> param_types, CodexType result) => wrap_fun_type_loop(param_types, result, (((long)param_types.Count) - 1L));

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

    public static CheckResult infer_application(UnificationState st, TypeEnv env, AExpr func, AExpr arg, SourceSpan span) => ((Func<CheckResult, CheckResult>)((fr) => ((Func<CheckResult, CheckResult>)((ar) => ((Func<FreshResult, CheckResult>)((ret) => ((Func<UnifyResult, CheckResult>)((r) => new CheckResult(inferred_type: ret.var_type, state: r.state)))(unify(ret.state, fr.inferred_type, new FunTy(ar.inferred_type, ret.var_type), span))))(fresh_and_advance(ar.state))))(infer_expr(fr.state, env, arg))))(infer_expr(st, env, func));

    public static CheckResult infer_list(UnificationState st, TypeEnv env, List<AExpr> elems) => ((((long)elems.Count) == 0L) ? ((Func<FreshResult, CheckResult>)((fr) => new CheckResult(inferred_type: new ListTy(fr.var_type), state: fr.state)))(fresh_and_advance(st)) : ((Func<CheckResult, CheckResult>)((first) => ((Func<UnificationState, CheckResult>)((st2) => new CheckResult(inferred_type: new ListTy(first.inferred_type), state: st2)))(unify_list_elems(first.state, env, elems, first.inferred_type, 1L, ((long)elems.Count)))))(infer_expr(st, env, elems[(int)0L])));

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
            var e = elems[(int)i];
            var er = infer_expr(st, env, e);
            var r = unify(er.state, er.inferred_type, elem_ty, aexpr_span(e));
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

    public static CheckResult infer_match(UnificationState st, TypeEnv env, AExpr scrutinee, List<AMatchArm> arms) => ((Func<CheckResult, CheckResult>)((sr) => ((Func<FreshResult, CheckResult>)((fr) => ((Func<UnificationState, CheckResult>)((st2) => new CheckResult(inferred_type: fr.var_type, state: st2)))(infer_match_arms(fr.state, env, sr.inferred_type, fr.var_type, arms, 0L, ((long)arms.Count)))))(fresh_and_advance(sr.state))))(infer_expr(st, env, scrutinee));

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
            var r = unify(br.state, br.inferred_type, result_ty, aexpr_span(arm.body));
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

    public static PatBindResult bind_pattern(UnificationState st, TypeEnv env, APat pat, CodexType ty) => pat switch { AVarPat(var name, var s) => (PatBindResult)(new PatBindResult(state: st, env: env_bind(env, name.value, ty))), AWildPat(var s) => (PatBindResult)(new PatBindResult(state: st, env: env)), ALitPat(var val, var kind, var s) => (PatBindResult)(new PatBindResult(state: st, env: env)), ACtorPat(var ctor_name, var sub_pats, var s) => (PatBindResult)(((Func<FreshResult, PatBindResult>)((ctor_lookup) => bind_ctor_sub_patterns(ctor_lookup.state, env, sub_pats, ctor_lookup.var_type, 0L, ((long)sub_pats.Count))))(instantiate_type(st, env_lookup(env, ctor_name.value)))), _ => throw new InvalidOperationException("Non-exhaustive match"), };

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
            var _tco_4 = (i + 1L);
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
            var _tco_4 = (i + 1L);
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

    public static CheckResult infer_act(UnificationState st, TypeEnv env, List<AActStmt> stmts) => infer_act_loop(st, env, stmts, 0L, ((long)stmts.Count), new NothingTy());

    public static CheckResult infer_act_loop(UnificationState st, TypeEnv env, List<AActStmt> stmts, long i, long len, CodexType last_ty)
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
            if (_tco_s is AActExprStmt _tco_m0)
            {
                var e = _tco_m0.Field0;
                var s = _tco_m0.Field1;
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
            else if (_tco_s is AActBindStmt _tco_m1)
            {
                var name = _tco_m1.Field0;
                var e = _tco_m1.Field1;
                var s = _tco_m1.Field2;
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

    public static CheckResult infer_expr(UnificationState st, TypeEnv env, AExpr expr) => expr switch { ALitExpr(var val, var kind, var s) => (CheckResult)(infer_literal(st, kind)), ANameExpr(var name, var s) => (CheckResult)(infer_name(st, env, name.value, s)), ABinaryExpr(var left, var op, var right, var s) => (CheckResult)(infer_binary(st, env, left, op, right, s)), AUnaryExpr(var operand, var s) => (CheckResult)(((Func<CheckResult, CheckResult>)((r) => ((Func<UnifyResult, CheckResult>)((u) => new CheckResult(inferred_type: new IntegerTy(), state: u.state)))(unify(r.state, r.inferred_type, new IntegerTy(), aexpr_span(operand)))))(infer_expr(st, env, operand))), AApplyExpr(var func, var arg, var s) => (CheckResult)(infer_application(st, env, func, arg, s)), AIfExpr(var cond, var then_e, var else_e, var s) => (CheckResult)(infer_if(st, env, cond, then_e, else_e, s)), ALetExpr(var bindings, var body, var s) => (CheckResult)(infer_let(st, env, bindings, body)), ALambdaExpr(var @params, var body, var s) => (CheckResult)(infer_lambda(st, env, @params, body)), AMatchExpr(var scrutinee, var arms, var s) => (CheckResult)(infer_match(st, env, scrutinee, arms)), AListExpr(var elems, var s) => (CheckResult)(infer_list(st, env, elems)), AActExpr(var stmts, var s) => (CheckResult)(infer_act(st, env, stmts)), AFieldAccess(var obj, var field, var s) => (CheckResult)(((Func<CheckResult, CheckResult>)((r) => ((Func<CodexType, CheckResult>)((resolved) => resolved switch { RecordTy(var rname, var rfields) => (CheckResult)(((Func<CodexType, CheckResult>)((ftype) => new CheckResult(inferred_type: ftype, state: r.state)))(lookup_record_field(rfields, field.value))), ConstructedTy(var cname, var cargs) => (CheckResult)(((Func<CodexType, CheckResult>)((record_type) => record_type switch { RecordTy(var rname, var rfields) => (CheckResult)(((Func<CodexType, CheckResult>)((ftype) => new CheckResult(inferred_type: ftype, state: r.state)))(lookup_record_field(rfields, field.value))), _ => (CheckResult)(((Func<FreshResult, CheckResult>)((fr) => new CheckResult(inferred_type: fr.var_type, state: fr.state)))(fresh_and_advance(r.state))), }))(resolve_constructed_to_record(env, cname.value))), _ => (CheckResult)(((Func<FreshResult, CheckResult>)((fr) => new CheckResult(inferred_type: fr.var_type, state: fr.state)))(fresh_and_advance(r.state))), }))(deep_resolve(r.state, r.inferred_type))))(infer_expr(st, env, obj))), ARecordExpr(var name, var fields, var s) => (CheckResult)(((Func<UnificationState, CheckResult>)((st2) => ((Func<CodexType, CheckResult>)((ctor_type) => ((Func<CodexType, CheckResult>)((result_type) => new CheckResult(inferred_type: result_type, state: st2)))(strip_fun_args(ctor_type))))((env_has(env, name.value) ? env_lookup(env, name.value) : new ErrorTy()))))(infer_record_fields(st, env, fields, 0L, ((long)fields.Count)))), AErrorExpr(var msg, var s) => (CheckResult)(new CheckResult(inferred_type: new ErrorTy(), state: st)), _ => throw new InvalidOperationException("Non-exhaustive match"), };

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
            var _tco_3 = (i + 1L);
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

    public static CodexType env_lookup(TypeEnv env, string name) => ((Func<long, CodexType>)((len) => ((len == 0L) ? new ErrorTy() : ((Func<long, CodexType>)((pos) => ((pos >= len) ? new ErrorTy() : ((Func<TypeBinding, CodexType>)((b) => ((b.name == name) ? b.bound_type : new ErrorTy())))(env.bindings[(int)pos]))))(bsearch_text_pos(env.bindings, name, 0L, len)))))(((long)env.bindings.Count));

    public static bool env_has(TypeEnv env, string name) => ((Func<long, bool>)((len) => ((len == 0L) ? false : ((Func<long, bool>)((pos) => ((pos >= len) ? false : (env.bindings[(int)pos].name == name))))(bsearch_text_pos(env.bindings, name, 0L, len)))))(((long)env.bindings.Count));

    public static TypeEnv env_bind(TypeEnv env, string name, CodexType ty) => ((Func<long, TypeEnv>)((len) => ((Func<long, TypeEnv>)((pos) => new TypeEnv(bindings: ((Func<List<TypeBinding>>)(() => { var _l = new List<TypeBinding>(env.bindings); _l.Insert((int)pos, new TypeBinding(name: name, bound_type: ty)); return _l; }))())))(bsearch_text_pos(env.bindings, name, 0L, len))))(((long)env.bindings.Count));

    public static TypeEnv builtin_type_env() => ((Func<TypeEnv, TypeEnv>)((e) => ((Func<TypeEnv, TypeEnv>)((e2) => ((Func<TypeEnv, TypeEnv>)((e3) => ((Func<TypeEnv, TypeEnv>)((e4) => ((Func<TypeEnv, TypeEnv>)((e5) => ((Func<TypeEnv, TypeEnv>)((e5b) => ((Func<TypeEnv, TypeEnv>)((e6) => ((Func<TypeEnv, TypeEnv>)((e7) => ((Func<TypeEnv, TypeEnv>)((e8) => ((Func<TypeEnv, TypeEnv>)((e9) => ((Func<TypeEnv, TypeEnv>)((e10) => ((Func<TypeEnv, TypeEnv>)((e10b) => ((Func<TypeEnv, TypeEnv>)((e11) => ((Func<TypeEnv, TypeEnv>)((e12) => ((Func<TypeEnv, TypeEnv>)((e12b) => ((Func<TypeEnv, TypeEnv>)((e13) => ((Func<TypeEnv, TypeEnv>)((e14) => ((Func<TypeEnv, TypeEnv>)((e15) => ((Func<TypeEnv, TypeEnv>)((e16) => ((Func<TypeEnv, TypeEnv>)((e16b) => ((Func<TypeEnv, TypeEnv>)((e16b2) => ((Func<TypeEnv, TypeEnv>)((e16c) => ((Func<TypeEnv, TypeEnv>)((e16d) => ((Func<TypeEnv, TypeEnv>)((e17) => ((Func<TypeEnv, TypeEnv>)((e18) => ((Func<TypeEnv, TypeEnv>)((e19) => ((Func<TypeEnv, TypeEnv>)((e20) => ((Func<TypeEnv, TypeEnv>)((e21) => ((Func<TypeEnv, TypeEnv>)((e22) => ((Func<TypeEnv, TypeEnv>)((e23) => ((Func<TypeEnv, TypeEnv>)((e23a) => ((Func<TypeEnv, TypeEnv>)((e24) => ((Func<TypeEnv, TypeEnv>)((e25) => ((Func<TypeEnv, TypeEnv>)((e26) => ((Func<TypeEnv, TypeEnv>)((e27) => ((Func<TypeEnv, TypeEnv>)((e28) => ((Func<TypeEnv, TypeEnv>)((e29) => ((Func<TypeEnv, TypeEnv>)((e30) => ((Func<TypeEnv, TypeEnv>)((e31) => ((Func<TypeEnv, TypeEnv>)((e32) => ((Func<TypeEnv, TypeEnv>)((e33) => ((Func<TypeEnv, TypeEnv>)((e34) => ((Func<TypeEnv, TypeEnv>)((e35) => ((Func<TypeEnv, TypeEnv>)((e36) => ((Func<TypeEnv, TypeEnv>)((e37) => ((Func<TypeEnv, TypeEnv>)((e38) => ((Func<TypeEnv, TypeEnv>)((e39) => ((Func<TypeEnv, TypeEnv>)((e40) => ((Func<TypeEnv, TypeEnv>)((e41) => ((Func<TypeEnv, TypeEnv>)((e42) => ((Func<TypeEnv, TypeEnv>)((e43) => ((Func<TypeEnv, TypeEnv>)((e44) => ((Func<TypeEnv, TypeEnv>)((e45) => ((Func<TypeEnv, TypeEnv>)((e46) => ((Func<TypeEnv, TypeEnv>)((e47) => ((Func<TypeEnv, TypeEnv>)((e48) => ((Func<TypeEnv, TypeEnv>)((e49) => ((Func<TypeEnv, TypeEnv>)((e50) => ((Func<TypeEnv, TypeEnv>)((e51) => ((Func<TypeEnv, TypeEnv>)((e52) => ((Func<TypeEnv, TypeEnv>)((e53) => e53))(env_bind(e52, "\u0020\u0011\u000E\u0049\u0012\u0010\u000E", new FunTy(new IntegerTy(), new IntegerTy())))))(env_bind(e51, "\u0020\u0011\u000E\u0049\u0013\u0014\u0015", new FunTy(new IntegerTy(), new FunTy(new IntegerTy(), new IntegerTy()))))))(env_bind(e50, "\u0020\u0011\u000E\u0049\u0013\u0014\u0017", new FunTy(new IntegerTy(), new FunTy(new IntegerTy(), new IntegerTy()))))))(env_bind(e49, "\u0020\u0011\u000E\u0049\u0024\u0010\u0015", new FunTy(new IntegerTy(), new FunTy(new IntegerTy(), new IntegerTy()))))))(env_bind(e48, "\u0020\u0011\u000E\u0049\u0010\u0015", new FunTy(new IntegerTy(), new FunTy(new IntegerTy(), new IntegerTy()))))))(env_bind(e47, "\u0020\u0011\u000E\u0049\u000F\u0012\u0016", new FunTy(new IntegerTy(), new FunTy(new IntegerTy(), new IntegerTy()))))))(env_bind(e46, "\u0020\u0019\u001C\u0049\u0015\u000D\u000F\u0016\u0049\u0020\u001E\u000E\u000D\u0013", new FunTy(new IntegerTy(), new FunTy(new IntegerTy(), new FunTy(new IntegerTy(), new ListTy(new IntegerTy()))))))))(env_bind(e45, "\u0020\u0019\u001C\u0049\u001B\u0015\u0011\u000E\u000D\u0049\u0020\u001E\u000E\u000D\u0013", new FunTy(new IntegerTy(), new FunTy(new IntegerTy(), new FunTy(new ListTy(new IntegerTy()), new IntegerTy())))))))(env_bind(e44, "\u0020\u0019\u001C\u0049\u001B\u0015\u0011\u000E\u000D\u0049\u0020\u001E\u000E\u000D", new FunTy(new IntegerTy(), new FunTy(new IntegerTy(), new FunTy(new IntegerTy(), new IntegerTy())))))))(env_bind(e43, "\u0017\u0011\u0013\u000E\u0049\u001B\u0011\u000E\u0014\u0049\u0018\u000F\u001F\u000F\u0018\u0011\u000E\u001E", new ForAllTy(0L, new FunTy(new IntegerTy(), new ListTy(new TypeVar(0L))))))))(env_bind(e42, "\u0014\u000D\u000F\u001F\u0049\u000F\u0016\u0021\u000F\u0012\u0018\u000D", new FunTy(new IntegerTy(), new NothingTy())))))(env_bind(e41, "\u0014\u000D\u000F\u001F\u0049\u0015\u000D\u0013\u000E\u0010\u0015\u000D", new FunTy(new IntegerTy(), new NothingTy())))))(env_bind(e40, "\u0014\u000D\u000F\u001F\u0049\u0013\u000F\u0021\u000D", new IntegerTy()))))(env_bind(e39, "\u0015\u000D\u0018\u0010\u0015\u0016\u0049\u0013\u000D\u000E", new ForAllTy(0L, new ForAllTy(1L, new FunTy(new TypeVar(0L), new FunTy(new TextTy(), new FunTy(new TypeVar(1L), new TypeVar(0L))))))))))(env_bind(e38, "\u0017\u0011\u0012\u0022\u000D\u0016\u0049\u0017\u0011\u0013\u000E\u0049\u000E\u0010\u0049\u0017\u0011\u0013\u000E", new FunTy(new LinkedListTy(new ListTy(new IntegerTy())), new ListTy(new ListTy(new IntegerTy())))))))(env_bind(e37, "\u0017\u0011\u0012\u0022\u000D\u0016\u0049\u0017\u0011\u0013\u000E\u0049\u001F\u0019\u0013\u0014", new FunTy(new LinkedListTy(new ListTy(new IntegerTy())), new FunTy(new ListTy(new IntegerTy()), new LinkedListTy(new ListTy(new IntegerTy()))))))))(env_bind(e36, "\u0017\u0011\u0012\u0022\u000D\u0016\u0049\u0017\u0011\u0013\u000E\u0049\u000D\u001A\u001F\u000E\u001E", new FunTy(new IntegerTy(), new LinkedListTy(new ListTy(new IntegerTy())))))))(env_bind(e35, "\u000E\u000D\u0024\u000E\u0049\u0018\u0010\u0012\u0018\u000F\u000E\u0049\u0017\u0011\u0013\u000E", new FunTy(new ListTy(new TextTy()), new TextTy())))))(env_bind(e34, "\u0015\u000F\u0018\u000D", new ForAllTy(0L, new FunTy(new ListTy(new FunTy(new NothingTy(), new TypeVar(0L))), new TypeVar(0L)))))))(env_bind(e33, "\u001F\u000F\u0015", new ForAllTy(0L, new ForAllTy(1L, new FunTy(new FunTy(new TypeVar(0L), new TypeVar(1L)), new FunTy(new ListTy(new TypeVar(0L)), new ListTy(new TypeVar(1L))))))))))(env_bind(e32, "\u000F\u001B\u000F\u0011\u000E", new ForAllTy(0L, new FunTy(new ConstructedTy(new Name(value: "\u0028\u000F\u0013\u0022"), new List<CodexType> { new TypeVar(0L) }), new TypeVar(0L)))))))(env_bind(e31, "\u001C\u0010\u0015\u0022", new ForAllTy(0L, new FunTy(new FunTy(new NothingTy(), new TypeVar(0L)), new ConstructedTy(new Name(value: "\u0028\u000F\u0013\u0022"), new List<CodexType> { new TypeVar(0L) })))))))(env_bind(e30, "\u0018\u0019\u0015\u0015\u000D\u0012\u000E\u0049\u0016\u0011\u0015", new TextTy()))))(env_bind(e29, "\u001D\u000D\u000E\u0049\u000D\u0012\u0021", new FunTy(new TextTy(), new TextTy())))))(env_bind(e28, "\u001D\u000D\u000E\u0049\u000F\u0015\u001D\u0013", new ListTy(new TextTy())))))(env_bind(e27, "\u000E\u000D\u0024\u000E\u0049\u0013\u000E\u000F\u0015\u000E\u0013\u0049\u001B\u0011\u000E\u0014", new FunTy(new TextTy(), new FunTy(new TextTy(), new BooleanTy()))))))(env_bind(e26, "\u000E\u000D\u0024\u000E\u0049\u0018\u0010\u0012\u000E\u000F\u0011\u0012\u0013", new FunTy(new TextTy(), new FunTy(new TextTy(), new BooleanTy()))))))(env_bind(e25, "\u000E\u000D\u0024\u000E\u0049\u0013\u001F\u0017\u0011\u000E", new FunTy(new TextTy(), new FunTy(new TextTy(), new ListTy(new TextTy())))))))(env_bind(e24, "\u0017\u0011\u0013\u000E\u0049\u001C\u0011\u0017\u000D\u0013", new FunTy(new TextTy(), new FunTy(new TextTy(), new ListTy(new TextTy())))))))(env_bind(e23a, "\u001C\u0011\u0017\u000D\u0049\u000D\u0024\u0011\u0013\u000E\u0013", new FunTy(new TextTy(), new BooleanTy())))))(env_bind(e23, "\u001B\u0015\u0011\u000E\u000D\u0049\u0020\u0011\u0012\u000F\u0015\u001E", new FunTy(new ListTy(new IntegerTy()), new NothingTy())))))(env_bind(e22, "\u001B\u0015\u0011\u000E\u000D\u0049\u001C\u0011\u0017\u000D", new FunTy(new TextTy(), new FunTy(new TextTy(), new NothingTy()))))))(env_bind(e21, "\u0015\u000D\u000F\u0016\u0049\u001C\u0011\u0017\u000D", new FunTy(new TextTy(), new TextTy())))))(env_bind(e20, "\u0015\u000D\u000F\u0016\u0049\u0017\u0011\u0012\u000D", new TextTy()))))(env_bind(e19, "\u001C\u0010\u0017\u0016", new ForAllTy(0L, new ForAllTy(1L, new FunTy(new FunTy(new TypeVar(1L), new FunTy(new TypeVar(0L), new TypeVar(1L))), new FunTy(new TypeVar(1L), new FunTy(new ListTy(new TypeVar(0L)), new TypeVar(1L))))))))))(env_bind(e18, "\u001C\u0011\u0017\u000E\u000D\u0015", new ForAllTy(0L, new FunTy(new FunTy(new TypeVar(0L), new BooleanTy()), new FunTy(new ListTy(new TypeVar(0L)), new ListTy(new TypeVar(0L)))))))))(env_bind(e17, "\u001A\u000F\u001F", new ForAllTy(0L, new ForAllTy(1L, new FunTy(new FunTy(new TypeVar(0L), new TypeVar(1L)), new FunTy(new ListTy(new TypeVar(0L)), new ListTy(new TypeVar(1L))))))))))(env_bind(e16d, "\u0017\u0011\u0013\u000E\u0049\u000F\u000E", new ForAllTy(0L, new FunTy(new ListTy(new TypeVar(0L)), new FunTy(new IntegerTy(), new TypeVar(0L))))))))(env_bind(e16c, "\u0017\u0011\u0013\u000E\u0049\u0013\u0012\u0010\u0018", new ForAllTy(0L, new FunTy(new ListTy(new TypeVar(0L)), new FunTy(new TypeVar(0L), new ListTy(new TypeVar(0L)))))))))(env_bind(e16b2, "\u000E\u000D\u0024\u000E\u0049\u0018\u0010\u001A\u001F\u000F\u0015\u000D", new FunTy(new TextTy(), new FunTy(new TextTy(), new IntegerTy()))))))(env_bind(e16b, "\u0017\u0011\u0013\u000E\u0049\u0013\u000D\u000E\u0049\u000F\u000E", new ForAllTy(0L, new FunTy(new ListTy(new TypeVar(0L)), new FunTy(new IntegerTy(), new FunTy(new TypeVar(0L), new ListTy(new TypeVar(0L))))))))))(env_bind(e16, "\u0017\u0011\u0013\u000E\u0049\u0011\u0012\u0013\u000D\u0015\u000E\u0049\u000F\u000E", new ForAllTy(0L, new FunTy(new ListTy(new TypeVar(0L)), new FunTy(new IntegerTy(), new FunTy(new TypeVar(0L), new ListTy(new TypeVar(0L))))))))))(env_bind(e15, "\u0017\u0011\u0013\u000E\u0049\u0017\u000D\u0012\u001D\u000E\u0014", new ForAllTy(0L, new FunTy(new ListTy(new TypeVar(0L)), new IntegerTy()))))))(env_bind(e14, "\u001F\u0015\u0011\u0012\u000E\u0049\u0017\u0011\u0012\u000D", new FunTy(new TextTy(), new NothingTy())))))(env_bind(e13, "\u0013\u0014\u0010\u001B", new ForAllTy(0L, new FunTy(new TypeVar(0L), new TextTy()))))))(env_bind(e12b, "\u000E\u000D\u0024\u000E\u0049\u000E\u0010\u0049\u0011\u0012\u000E\u000D\u001D\u000D\u0015", new FunTy(new TextTy(), new IntegerTy())))))(env_bind(e12, "\u000E\u000D\u0024\u000E\u0049\u000E\u0010\u0049\u0016\u0010\u0019\u0020\u0017\u000D\u0049\u0020\u0011\u000E\u0013", new FunTy(new TextTy(), new IntegerTy())))))(env_bind(e11, "\u000E\u000D\u0024\u000E\u0049\u0015\u000D\u001F\u0017\u000F\u0018\u000D", new FunTy(new TextTy(), new FunTy(new TextTy(), new FunTy(new TextTy(), new TextTy())))))))(env_bind(e10b, "\u0018\u0010\u0016\u000D\u0049\u000E\u0010\u0049\u0018\u0014\u000F\u0015", new FunTy(new IntegerTy(), new CharTy())))))(env_bind(e10, "\u0018\u0014\u000F\u0015\u0049\u0018\u0010\u0016\u000D\u0049\u000F\u000E", new FunTy(new TextTy(), new FunTy(new IntegerTy(), new IntegerTy()))))))(env_bind(e9, "\u0018\u0014\u000F\u0015\u0049\u0018\u0010\u0016\u000D", new FunTy(new CharTy(), new IntegerTy())))))(env_bind(e8, "\u0011\u0013\u0049\u001B\u0014\u0011\u000E\u000D\u0013\u001F\u000F\u0018\u000D", new FunTy(new CharTy(), new BooleanTy())))))(env_bind(e7, "\u0011\u0013\u0049\u0016\u0011\u001D\u0011\u000E", new FunTy(new CharTy(), new BooleanTy())))))(env_bind(e6, "\u0011\u0013\u0049\u0017\u000D\u000E\u000E\u000D\u0015", new FunTy(new CharTy(), new BooleanTy())))))(env_bind(e5b, "\u0013\u0019\u0020\u0013\u000E\u0015\u0011\u0012\u001D", new FunTy(new TextTy(), new FunTy(new IntegerTy(), new FunTy(new IntegerTy(), new TextTy())))))))(env_bind(e5, "\u0018\u0014\u000F\u0015\u0049\u000E\u0010\u0049\u000E\u000D\u0024\u000E", new FunTy(new CharTy(), new TextTy())))))(env_bind(e4, "\u0018\u0014\u000F\u0015\u0049\u000F\u000E", new FunTy(new TextTy(), new FunTy(new IntegerTy(), new CharTy()))))))(env_bind(e3, "\u0011\u0012\u000E\u000D\u001D\u000D\u0015\u0049\u000E\u0010\u0049\u000E\u000D\u0024\u000E", new FunTy(new IntegerTy(), new TextTy())))))(env_bind(e2, "\u000E\u000D\u0024\u000E\u0049\u0017\u000D\u0012\u001D\u000E\u0014", new FunTy(new TextTy(), new IntegerTy())))))(env_bind(e, "\u0012\u000D\u001D\u000F\u000E\u000D", new FunTy(new IntegerTy(), new IntegerTy())))))(empty_type_env());

    public static UnificationState empty_unification_state() => new UnificationState(substitutions: new List<SubstEntry>(), next_id: 2L, bag: empty_bag());

    public static CodexType fresh_var(UnificationState st) => new TypeVar(st.next_id);

    public static UnificationState advance_id(UnificationState st) => new UnificationState(substitutions: st.substitutions, next_id: (st.next_id + 1L), bag: st.bag);

    public static FreshResult fresh_and_advance(UnificationState st) => new FreshResult(var_type: new TypeVar(st.next_id), state: advance_id(st));

    public static CodexType subst_lookup(long var_id, List<SubstEntry> entries) => ((Func<long, CodexType>)((len) => ((len == 0L) ? new ErrorTy() : ((Func<long, CodexType>)((pos) => ((pos >= len) ? new ErrorTy() : ((Func<SubstEntry, CodexType>)((entry) => ((entry.var_id == var_id) ? entry.resolved_type : new ErrorTy())))(entries[(int)pos]))))(bsearch_int_pos(entries, var_id, 0L, len)))))(((long)entries.Count));

    public static bool has_subst(long var_id, List<SubstEntry> entries) => ((Func<long, bool>)((len) => ((len == 0L) ? false : ((Func<long, bool>)((pos) => ((pos >= len) ? false : (entries[(int)pos].var_id == var_id))))(bsearch_int_pos(entries, var_id, 0L, len)))))(((long)entries.Count));

    public static CodexType resolve(UnificationState st, CodexType ty)
    {
        while (true)
        {
            var _tco_s = ty;
            if (_tco_s is TypeVar _tco_m0)
            {
                var id = _tco_m0.Field0;
            var entries = st.substitutions;
            var len = ((long)entries.Count);
            if ((len == 0L))
            {
            return ty;
            }
            else
            {
            var pos = bsearch_int_pos(entries, id, 0L, len);
            if ((pos >= len))
            {
            return ty;
            }
            else
            {
            var entry = entries[(int)pos];
            if ((entry.var_id == id))
            {
            var _tco_0 = st;
            var _tco_1 = entry.resolved_type;
            st = _tco_0;
            ty = _tco_1;
            continue;
            }
            else
            {
            return ty;
            }
            }
            }
            }
            {
            return ty;
            }
        }
    }

    public static UnificationState add_subst(UnificationState st, long var_id, CodexType ty) => ((Func<SubstEntry, UnificationState>)((entry) => ((Func<long, UnificationState>)((pos) => new UnificationState(substitutions: ((Func<List<SubstEntry>>)(() => { var _l = new List<SubstEntry>(st.substitutions); _l.Insert((int)pos, entry); return _l; }))(), next_id: st.next_id, bag: st.bag)))(bsearch_int_pos(st.substitutions, var_id, 0L, ((long)st.substitutions.Count)))))(new SubstEntry(var_id: var_id, resolved_type: ty));

    public static UnificationState add_unify_error(UnificationState st, long code, string msg, SourceSpan span) => new UnificationState(substitutions: st.substitutions, next_id: st.next_id, bag: bag_add(st.bag, make_error(code, msg, span)));

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
            else if (_tco_s is LinkedListTy _tco_m3)
            {
                var elem = _tco_m3.Field0;
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

    public static UnifyResult unify(UnificationState st, CodexType a, CodexType b, SourceSpan span) => ((Func<CodexType, UnifyResult>)((ra) => ((Func<CodexType, UnifyResult>)((rb) => unify_resolved(st, ra, rb, span)))(resolve(st, b))))(resolve(st, a));

    public static UnifyResult unify_resolved(UnificationState st, CodexType a, CodexType b, SourceSpan span) => (types_equal(a, b) ? new UnifyResult(success: true, state: st) : a switch { TypeVar(var id_a) => (UnifyResult)((occurs_in(st, id_a, b) ? new UnifyResult(success: false, state: add_unify_error(st, cdx_infinite_type(), "\u002B\u0012\u001C\u0011\u0012\u0011\u000E\u000D\u0002\u000E\u001E\u001F\u000D", span)) : new UnifyResult(success: true, state: add_subst(st, id_a, b)))), _ => (UnifyResult)(unify_rhs(st, a, b, span)), });

    public static bool types_equal(CodexType a, CodexType b) => a switch { TypeVar(var id_a) => (bool)(b switch { TypeVar(var id_b) => (bool)((id_a == id_b)), _ => (bool)(false), }), IntegerTy { } => (bool)(b switch { IntegerTy { } => (bool)(true), _ => (bool)(false), }), NumberTy { } => (bool)(b switch { NumberTy { } => (bool)(true), _ => (bool)(false), }), TextTy { } => (bool)(b switch { TextTy { } => (bool)(true), _ => (bool)(false), }), BooleanTy { } => (bool)(b switch { BooleanTy { } => (bool)(true), _ => (bool)(false), }), CharTy { } => (bool)(b switch { CharTy { } => (bool)(true), _ => (bool)(false), }), NothingTy { } => (bool)(b switch { NothingTy { } => (bool)(true), _ => (bool)(false), }), VoidTy { } => (bool)(b switch { VoidTy { } => (bool)(true), _ => (bool)(false), }), ErrorTy { } => (bool)(b switch { ErrorTy { } => (bool)(true), _ => (bool)(false), }), _ => (bool)(false), };

    public static UnifyResult unify_rhs(UnificationState st, CodexType a, CodexType b, SourceSpan span) => b switch { TypeVar(var id_b) => (UnifyResult)((occurs_in(st, id_b, a) ? new UnifyResult(success: false, state: add_unify_error(st, cdx_infinite_type(), "\u002B\u0012\u001C\u0011\u0012\u0011\u000E\u000D\u0002\u000E\u001E\u001F\u000D", span)) : new UnifyResult(success: true, state: add_subst(st, id_b, a)))), _ => (UnifyResult)(unify_structural(st, a, b, span)), };

    public static UnifyResult unify_structural(UnificationState st, CodexType a, CodexType b, SourceSpan span) => a switch { IntegerTy { } => (UnifyResult)(b switch { IntegerTy { } => (UnifyResult)(new UnifyResult(success: true, state: st)), ErrorTy { } => (UnifyResult)(new UnifyResult(success: true, state: st)), _ => (UnifyResult)(unify_mismatch(st, a, b, span)), }), NumberTy { } => (UnifyResult)(b switch { NumberTy { } => (UnifyResult)(new UnifyResult(success: true, state: st)), ErrorTy { } => (UnifyResult)(new UnifyResult(success: true, state: st)), _ => (UnifyResult)(unify_mismatch(st, a, b, span)), }), TextTy { } => (UnifyResult)(b switch { TextTy { } => (UnifyResult)(new UnifyResult(success: true, state: st)), ErrorTy { } => (UnifyResult)(new UnifyResult(success: true, state: st)), _ => (UnifyResult)(unify_mismatch(st, a, b, span)), }), BooleanTy { } => (UnifyResult)(b switch { BooleanTy { } => (UnifyResult)(new UnifyResult(success: true, state: st)), ErrorTy { } => (UnifyResult)(new UnifyResult(success: true, state: st)), _ => (UnifyResult)(unify_mismatch(st, a, b, span)), }), NothingTy { } => (UnifyResult)(b switch { NothingTy { } => (UnifyResult)(new UnifyResult(success: true, state: st)), ErrorTy { } => (UnifyResult)(new UnifyResult(success: true, state: st)), _ => (UnifyResult)(unify_mismatch(st, a, b, span)), }), VoidTy { } => (UnifyResult)(b switch { VoidTy { } => (UnifyResult)(new UnifyResult(success: true, state: st)), ErrorTy { } => (UnifyResult)(new UnifyResult(success: true, state: st)), _ => (UnifyResult)(unify_mismatch(st, a, b, span)), }), ErrorTy { } => (UnifyResult)(new UnifyResult(success: true, state: st)), FunTy(var pa, var ra) => (UnifyResult)(b switch { FunTy(var pb, var rb) => (UnifyResult)(unify_fun(st, pa, ra, pb, rb, span)), ErrorTy { } => (UnifyResult)(new UnifyResult(success: true, state: st)), _ => (UnifyResult)(unify_mismatch(st, a, b, span)), }), ListTy(var ea) => (UnifyResult)(b switch { ListTy(var eb) => (UnifyResult)(unify(st, ea, eb, span)), ErrorTy { } => (UnifyResult)(new UnifyResult(success: true, state: st)), _ => (UnifyResult)(unify_mismatch(st, a, b, span)), }), LinkedListTy(var ea) => (UnifyResult)(b switch { LinkedListTy(var eb) => (UnifyResult)(unify(st, ea, eb, span)), ErrorTy { } => (UnifyResult)(new UnifyResult(success: true, state: st)), _ => (UnifyResult)(unify_mismatch(st, a, b, span)), }), ConstructedTy(var na, var args_a) => (UnifyResult)(b switch { ConstructedTy(var nb, var args_b) => (UnifyResult)(((na.value == nb.value) ? unify_constructed_args(st, args_a, args_b, 0L, ((long)args_a.Count), span) : unify_mismatch(st, a, b, span))), SumTy(var sb_name, var sb_ctors) => (UnifyResult)(((na.value == sb_name.value) ? new UnifyResult(success: true, state: st) : unify_mismatch(st, a, b, span))), RecordTy(var rb_name, var rb_fields) => (UnifyResult)(((na.value == rb_name.value) ? new UnifyResult(success: true, state: st) : unify_mismatch(st, a, b, span))), ErrorTy { } => (UnifyResult)(new UnifyResult(success: true, state: st)), _ => (UnifyResult)(unify_mismatch(st, a, b, span)), }), SumTy(var sa_name, var sa_ctors) => (UnifyResult)(b switch { SumTy(var sb_name, var sb_ctors) => (UnifyResult)(((sa_name.value == sb_name.value) ? new UnifyResult(success: true, state: st) : unify_mismatch(st, a, b, span))), ConstructedTy(var nb, var args_b) => (UnifyResult)(((sa_name.value == nb.value) ? new UnifyResult(success: true, state: st) : unify_mismatch(st, a, b, span))), ErrorTy { } => (UnifyResult)(new UnifyResult(success: true, state: st)), _ => (UnifyResult)(unify_mismatch(st, a, b, span)), }), RecordTy(var ra_name, var ra_fields) => (UnifyResult)(b switch { RecordTy(var rb_name, var rb_fields) => (UnifyResult)(((ra_name.value == rb_name.value) ? new UnifyResult(success: true, state: st) : unify_mismatch(st, a, b, span))), ConstructedTy(var nb, var args_b) => (UnifyResult)(((ra_name.value == nb.value) ? new UnifyResult(success: true, state: st) : unify_mismatch(st, a, b, span))), ErrorTy { } => (UnifyResult)(new UnifyResult(success: true, state: st)), _ => (UnifyResult)(unify_mismatch(st, a, b, span)), }), ForAllTy(var id, var body) => (UnifyResult)(unify(st, body, b, span)), EffectfulTy(var effs_a, var ret_a) => (UnifyResult)(b switch { EffectfulTy(var effs_b, var ret_b) => (UnifyResult)(unify(st, ret_a, ret_b, span)), ErrorTy { } => (UnifyResult)(new UnifyResult(success: true, state: st)), _ => (UnifyResult)(unify(st, ret_a, b, span)), }), _ => (UnifyResult)(b switch { EffectfulTy(var effs_b, var ret_b) => (UnifyResult)(unify(st, a, ret_b, span)), ErrorTy { } => (UnifyResult)(new UnifyResult(success: true, state: st)), _ => (UnifyResult)(unify_mismatch(st, a, b, span)), }), };

    public static UnifyResult unify_constructed_args(UnificationState st, List<CodexType> args_a, List<CodexType> args_b, long i, long len, SourceSpan span)
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
            var r = unify(st, args_a[(int)i], args_b[(int)i], span);
            if (r.success)
            {
            var _tco_0 = r.state;
            var _tco_1 = args_a;
            var _tco_2 = args_b;
            var _tco_3 = (i + 1L);
            var _tco_4 = len;
            var _tco_5 = span;
            st = _tco_0;
            args_a = _tco_1;
            args_b = _tco_2;
            i = _tco_3;
            len = _tco_4;
            span = _tco_5;
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

    public static UnifyResult unify_fun(UnificationState st, CodexType pa, CodexType ra, CodexType pb, CodexType rb, SourceSpan span) => ((Func<UnifyResult, UnifyResult>)((r1) => (r1.success ? unify(r1.state, ra, rb, span) : r1)))(unify(st, pa, pb, span));

    public static UnifyResult unify_mismatch(UnificationState st, CodexType a, CodexType b, SourceSpan span) => new UnifyResult(success: false, state: add_unify_error(st, cdx_type_mismatch(), ("\u0028\u001E\u001F\u000D\u0002\u001A\u0011\u0013\u001A\u000F\u000E\u0018\u0014\u0045\u0002" + (type_tag(a) + ("\u0002\u0021\u0013\u0002" + type_tag(b)))), span));

    public static string type_tag(CodexType ty) => ty switch { IntegerTy { } => (string)("\u002B\u0012\u000E\u000D\u001D\u000D\u0015"), NumberTy { } => (string)("\u002C\u0019\u001A\u0020\u000D\u0015"), TextTy { } => (string)("\u0028\u000D\u0024\u000E"), BooleanTy { } => (string)("\u003A\u0010\u0010\u0017\u000D\u000F\u0012"), CharTy { } => (string)("\u0032\u0014\u000F\u0015"), VoidTy { } => (string)("\u003B\u0010\u0011\u0016"), NothingTy { } => (string)("\u002C\u0010\u000E\u0014\u0011\u0012\u001D"), ErrorTy { } => (string)("\u0027\u0015\u0015\u0010\u0015"), FunTy(var p, var r) => (string)("\u0036\u0019\u0012"), ListTy(var e) => (string)("\u0031\u0011\u0013\u000E"), LinkedListTy(var e) => (string)("\u0031\u0011\u0012\u0022\u000D\u0016\u0031\u0011\u0013\u000E"), TypeVar(var id) => (string)(("\u0028" + _Cce.FromUnicode(id.ToString()))), ForAllTy(var id, var body) => (string)("\u0036\u0010\u0015\u0029\u0017\u0017"), SumTy(var name, var ctors) => (string)(("\u002D\u0019\u001A\u0045" + name.value)), RecordTy(var name, var fields) => (string)(("\u002F\u000D\u0018\u0045" + name.value)), ConstructedTy(var name, var args) => (string)(("\u0032\u0010\u0012\u0045" + name.value)), EffectfulTy(var effs, var ret) => (string)("\u0027\u001C\u001C\u000D\u0018\u000E\u001C\u0019\u0017"), _ => throw new InvalidOperationException("Non-exhaustive match"), };

    public static CodexType deep_resolve(UnificationState st, CodexType ty) => ((Func<CodexType, CodexType>)((resolved) => resolved switch { FunTy(var param, var ret) => (CodexType)(new FunTy(deep_resolve(st, param), deep_resolve(st, ret))), ListTy(var elem) => (CodexType)(new ListTy(deep_resolve(st, elem))), LinkedListTy(var elem) => (CodexType)(new LinkedListTy(deep_resolve(st, elem))), ConstructedTy(var name, var args) => (CodexType)(new ConstructedTy(name, deep_resolve_list(st, args, 0L, ((long)args.Count), new List<CodexType>()))), ForAllTy(var id, var body) => (CodexType)(new ForAllTy(id, deep_resolve(st, body))), EffectfulTy(var effs, var ret) => (CodexType)(new EffectfulTy(effs, deep_resolve(st, ret))), SumTy(var name, var ctors) => (CodexType)(resolved), RecordTy(var name, var fields) => (CodexType)(resolved), _ => (CodexType)(resolved), }))(resolve(st, ty));

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
            var _tco_2 = (i + 1L);
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

    public static string compile_text(string source, string chapter_name) => ((Func<List<Token>, string>)((tokens) => ((Func<ParseState, string>)((st) => ((Func<ScanResult, string>)((scan) => ((Func<List<ChapterAssignment>, string>)((assignments) => ((Func<List<string>, string>)((colliding) => ((Func<Document, string>)((doc) => ((Func<AChapter, string>)((ast) => ((Func<AChapter, string>)((scoped) => ((Func<ChapterResult, string>)((check_result) => ((Func<IRChapter, string>)((ir) => ((Func<List<string>, string>)((ctor_names) => (emit__codex_emitter_emit_type_defs(scoped.type_defs, 0L) + emit_text_defs(ir.defs, ctor_names, 0L))))(emit__codex_emitter_collect_ctor_names(scoped.type_defs, 0L))))(lower_chapter(scoped, check_result.types, check_result.state))))(check_chapter(scoped))))(scope_achapter(ast, colliding, assignments))))(desugar_document(doc, chapter_name))))(parse_document(make_parse_state(tokens)))))(find_colliding_names(assignments))))(build_all_assignments(scan.def_headers, 0L, new List<ChapterAssignment>()))))(scan_document(st))))(make_parse_state(tokens))))(tokenize(source, 1L));

    public static string emit_text_defs(List<IRDef> defs, List<string> ctor_names, long i) => ((i == ((long)defs.Count)) ? "" : (emit__codex_emitter_emit_def(defs[(int)i], ctor_names) + ("\u0001" + emit_text_defs(defs, ctor_names, (i + 1L)))));

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

    public static EmitChapterResult compile_to_binary(string source, string chapter_name) => ((Func<List<Token>, EmitChapterResult>)((tokens) => ((Func<ParseState, EmitChapterResult>)((st) => ((Func<ScanResult, EmitChapterResult>)((scan) => ((Func<List<ChapterAssignment>, EmitChapterResult>)((assignments) => ((Func<List<string>, EmitChapterResult>)((colliding) => ((Func<Document, EmitChapterResult>)((doc) => ((Func<AChapter, EmitChapterResult>)((ast) => ((Func<AChapter, EmitChapterResult>)((scoped) => ((Func<DiagnosticBag, EmitChapterResult>)((cite_bag) => ((Func<ChapterResult, EmitChapterResult>)((check_result) => ((Func<IRChapter, EmitChapterResult>)((ir) => ((Func<List<TypeBinding>, EmitChapterResult>)((ctor_types) => ((Func<EmitChapterResult, EmitChapterResult>)((emit_result) => new EmitChapterResult(bytes: emit_result.bytes, bag: bag_merge(cite_bag, emit_result.bag))))(x86_64_emit_chapter(ir, ctor_types))))(collect_ctor_bindings(scoped.type_defs, 0L, ((long)scoped.type_defs.Count), new List<TypeBinding>()))))(lower_chapter(scoped, check_result.types, check_result.state))))(check_chapter(scoped))))(check_duplicate_cites(scoped.citations))))(scope_achapter(ast, colliding, assignments))))(desugar_document(doc, chapter_name))))(parse_document(make_parse_state(tokens)))))(find_colliding_names(assignments))))(build_all_assignments(scan.def_headers, 0L, new List<ChapterAssignment>()))))(scan_document(st))))(make_parse_state(tokens))))(tokenize(source, 1L));

    public static object print_codegen_errors(List<Diagnostic> errs, long i, long len) => ((i == len) ? ((Func<object>)(() => { Console.WriteLine(_Cce.ToUnicode("")); return null; }))() : ((Func<object>)(() => { ((Func<object>)(() => { Console.WriteLine(_Cce.ToUnicode(diagnostic_display(errs[(int)i], empty_file_table()))); return null; }))(); print_codegen_errors(errs, (i + 1L), len);  return null; }))());

    public static object print_codegen_error_header(List<Diagnostic> errs) => ((Func<long, object>)((n) => ((n == 0L) ? ((Func<object>)(() => { Console.WriteLine(_Cce.ToUnicode("")); return null; }))() : ((Func<object>)(() => { ((Func<object>)(() => { Console.WriteLine(_Cce.ToUnicode(("\u0032\u002A\u0030\u0027\u0037\u0027\u002C\u0049\u0027\u002F\u002F\u002A\u002F\u002D\u0045" + _Cce.FromUnicode(n.ToString())))); return null; }))(); print_codegen_errors(errs, 0L, n);  return null; }))())))(((long)errs.Count));

    public static object main_emit_binary(string source) => ((Func<EmitChapterResult, object>)((result) => ((Func<object>)(() => { print_codegen_error_header(bag_diagnostics(result.bag)); ((Func<object>)(() => { Console.WriteLine(_Cce.ToUnicode(("\u002D\u002B\u0040\u0027\u0045" + _Cce.FromUnicode(((long)result.bytes.Count).ToString())))); return null; }))(); ((Func<object>)(() => { var _bl = (List<long>)result.bytes; using var _s = Console.OpenStandardOutput(); foreach (var _b in _bl) _s.WriteByte((byte)_b); _s.Flush(); return null; }))();  return null; }))()))(compile_to_binary(source, "\u0032\u0010\u0016\u000D\u0024\u0055\u0032\u0010\u0016\u000D\u0024"));

    public static object main() => ((Func<object>)(() => { var mode = _Cce.FromUnicode(Console.ReadLine() ?? ""); var source = _Cce.FromUnicode(File.ReadAllText(_Cce.ToUnicode(mode))); ((Func<string, object>)((clean) => ((mode == "\u003A\u002B\u002C\u0029\u002F\u0038") ? main_emit_binary(clean) : ((Func<object>)(() => { Console.WriteLine(_Cce.ToUnicode(compile_text(clean, "\u0039\u0015\u0010\u001D\u0015\u000F\u001A"))); return null; }))())))(normalize_whitespace(source));  return null; }))();

}
