using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;



public sealed record ParseState(List<Token> tokens, long pos);

public sealed record LetBindResult(UnificationState state, TypeEnv env);

public sealed record Name(string value);

public sealed record LambdaParamsResult(List<Token> toks, ParseState state);

public abstract record ATypeExpr;

public sealed record ANamedType(Name Field0) : ATypeExpr;
public sealed record AFunType(ATypeExpr Field0, ATypeExpr Field1) : ATypeExpr;
public sealed record AAppType(ATypeExpr Field0, List<ATypeExpr> Field1) : ATypeExpr;
public sealed record AEffectType(List<Name> Field0, ATypeExpr Field1) : ATypeExpr;

public sealed record SubstEntry(long var_id, CodexType resolved_type);

public sealed record UnifyResult(bool success, UnificationState state);

public sealed record IRBranch(IRPat pattern, IRExpr body);

public sealed record HandleParseResult(List<HandleClause> clauses, ParseState state);

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

public sealed record IRParam(string name, CodexType type_val);

public sealed record RecordFieldExpr(Token name, Expr value);

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

public sealed record CollectResult(List<string> names, List<Diagnostic> errors);

public abstract record LexResult;

public sealed record LexToken(Token Field0, LexState Field1) : LexResult;
public sealed record LexEnd : LexResult;

public sealed record WalkResult(CodexType walked, List<ParamEntry> entries, UnificationState state);

public sealed record UnificationState(List<SubstEntry> substitutions, long next_id, List<Diagnostic> errors);

public sealed record EffectOpsResult(List<EffectOpDef> ops, ParseState state);

public sealed record SumCtor(Name name, List<CodexType> fields);

public abstract record ParseTypeDefResult;

public sealed record TypeDefOk(TypeDef Field0, ParseState Field1) : ParseTypeDefResult;
public sealed record TypeDefNone(ParseState Field0) : ParseTypeDefResult;

public abstract record ParseDefResult;

public sealed record DefOk(Def Field0, ParseState Field1) : ParseDefResult;
public sealed record DefNone(ParseState Field0) : ParseDefResult;

public sealed record LambdaBindResult(UnificationState state, TypeEnv env, List<CodexType> param_types);

public abstract record TypeExpr;

public sealed record NamedType(Token Field0) : TypeExpr;
public sealed record FunType(TypeExpr Field0, TypeExpr Field1) : TypeExpr;
public sealed record AppType(TypeExpr Field0, List<TypeExpr> Field1) : TypeExpr;
public sealed record ParenType(TypeExpr Field0) : TypeExpr;
public sealed record ListType(TypeExpr Field0) : TypeExpr;
public sealed record LinearTypeExpr(TypeExpr Field0) : TypeExpr;
public sealed record EffectTypeExpr(List<Token> Field0, TypeExpr Field1) : TypeExpr;

public sealed record IRModule(Name name, List<IRDef> defs);

public abstract record TypeBody;

public sealed record RecordBody(List<RecordFieldDef> Field0) : TypeBody;
public sealed record VariantBody(List<VariantCtorDef> Field0) : TypeBody;

public sealed record LowerCtx(List<TypeBinding> types, UnificationState ust);

public sealed record AFieldExpr(Name name, AExpr value);

public sealed record RecordFieldDef(Token name, TypeExpr type_expr);

public sealed record ArityEntry(string name, long arity);

public sealed record SourcePosition(long line, long column, long offset);

public abstract record CompileResult;

public sealed record CompileOk(string Field0, ModuleResult Field1) : CompileResult;
public sealed record CompileError(List<Diagnostic> Field0) : CompileResult;

public sealed record WalkListResult(List<CodexType> walked_list, List<ParamEntry> entries, UnificationState state);

public abstract record DoStmt;

public sealed record DoBindStmt(Token Field0, Expr Field1) : DoStmt;
public sealed record DoExprStmt(Expr Field0) : DoStmt;

public sealed record EffectDef(Token name, List<EffectOpDef> ops);

public sealed record FreshResult(CodexType var_type, UnificationState state);

public sealed record Diagnostic(string code, string message, DiagnosticSeverity severity);

public sealed record Document(List<Def> defs, List<TypeDef> type_defs, List<EffectDef> effect_defs, List<ImportDecl> imports);

public sealed record ALetBind(Name name, AExpr value);

public sealed record RecordField(Name name, CodexType type_val);

public sealed record AEffectOpDef(Name name, ATypeExpr type_expr);

public sealed record HandleParamsResult(List<Token> toks, ParseState state);

public sealed record ApplyChain(IRExpr root, List<IRExpr> args);

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

public sealed record DefParamResult(UnificationState state, TypeEnv env, CodexType remaining_type);

public sealed record TypeEnv(List<TypeBinding> bindings);

public abstract record Pat;

public sealed record VarPat(Token Field0) : Pat;
public sealed record LitPat(Token Field0) : Pat;
public sealed record CtorPat(Token Field0, List<Pat> Field1) : Pat;
public sealed record WildPat(Token Field0) : Pat;

public abstract record IRPat;

public sealed record IrVarPat(string Field0, CodexType Field1) : IRPat;
public sealed record IrLitPat(string Field0, CodexType Field1) : IRPat;
public sealed record IrCtorPat(string Field0, List<IRPat> Field1, CodexType Field2) : IRPat;
public sealed record IrWildPat : IRPat;

public sealed record CheckResult(CodexType inferred_type, UnificationState state);

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

public sealed record ImportParseResult(List<ImportDecl> imports, ParseState state);

public sealed record LetBind(Token name, Expr value);

public sealed record AMatchArm(APat pattern, AExpr body);

public sealed record EffectOpDef(Token name, TypeExpr type_expr);

public sealed record ADef(Name name, List<AParam> @params, List<ATypeExpr> declared_type, AExpr body);

public sealed record VariantCtorDef(Token name, List<TypeExpr> fields);

public sealed record IRFieldVal(string name, IRExpr value);

public sealed record ParamEntry(string param_name, long var_id);

public sealed record MatchArm(Pat pattern, Expr body);

public sealed record IRHandleClause(string op_name, string resume_name, IRExpr body);

public sealed record PatBindResult(UnificationState state, TypeEnv env);

public abstract record ADoStmt;

public sealed record ADoBindStmt(Name Field0, AExpr Field1) : ADoStmt;
public sealed record ADoExprStmt(AExpr Field0) : ADoStmt;

public sealed record ParamResult(CodexType parameterized, List<ParamEntry> entries, UnificationState state);

public sealed record ARecordFieldDef(Name name, ATypeExpr type_expr);

public abstract record ParsePatResult;

public sealed record PatOk(Pat Field0, ParseState Field1) : ParsePatResult;

public sealed record CtorCollectResult(List<string> type_names, List<string> ctor_names);

public sealed record AImportDecl(Name module_name);

public sealed record TypeBinding(string name, CodexType bound_type);

public sealed record SourceSpan(SourcePosition start, SourcePosition end, string file);

public abstract record ParseTypeResult;

public sealed record TypeOk(TypeExpr Field0, ParseState Field1) : ParseTypeResult;

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

public abstract record IRDoStmt;

public sealed record IrDoBind(string Field0, CodexType Field1, IRExpr Field2) : IRDoStmt;
public sealed record IrDoExec(IRExpr Field0) : IRDoStmt;

public sealed record TypeDef(Token name, List<Token> type_params, TypeBody body);

public sealed record HandleClause(Token op_name, Token resume_name, Expr body);

public sealed record ImportDecl(Token module_name);

public sealed record AHandleClause(Name op_name, Name resume_name, AExpr body);

public sealed record Token(TokenKind kind, string text, long offset, long line, long column);

public sealed record TypeAnn(Token name, TypeExpr type_expr);

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

public sealed record AVariantCtorDef(Name name, List<ATypeExpr> fields);

public abstract record APat;

public sealed record AVarPat(Name Field0) : APat;
public sealed record ALitPat(string Field0, LiteralKind Field1) : APat;
public sealed record ACtorPat(Name Field0, List<APat> Field1) : APat;
public sealed record AWildPat : APat;

public abstract record ParseExprResult;

public sealed record ExprOk(Expr Field0, ParseState Field1) : ParseExprResult;

public sealed record LexState(string source, long offset, long line, long column);

public sealed record DefSetup(CodexType expected_type, CodexType remaining_type, UnificationState state, TypeEnv env);

public abstract record DiagnosticSeverity;

public sealed record Error : DiagnosticSeverity;
public sealed record Warning : DiagnosticSeverity;
public sealed record Info : DiagnosticSeverity;

public sealed record IRDef(string name, List<IRParam> @params, CodexType type_val, IRExpr body);

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

public sealed record Def(Token name, List<Token> @params, List<TypeAnn> ann, Expr body);

public sealed record ResolveResult(List<Diagnostic> errors, List<string> top_level_names, List<string> type_names, List<string> ctor_names);

public sealed record AModule(Name name, List<ADef> defs, List<ATypeDef> type_defs, List<AEffectDef> effect_defs, List<AImportDecl> imports);

public sealed record AParam(Name name);

public sealed record Scope(List<string> names);

public sealed record AEffectDef(Name name, List<AEffectOpDef> ops);

public sealed record ModuleResult(List<TypeBinding> types, UnificationState state);

public abstract record ATypeDef;

public sealed record ARecordTypeDef(Name Field0, List<Name> Field1, List<ARecordFieldDef> Field2) : ATypeDef;
public sealed record AVariantTypeDef(Name Field0, List<Name> Field1, List<AVariantCtorDef> Field2) : ATypeDef;

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
    static readonly Dictionary<int, int> _t1ToUni = new() {
        [0] = 223,
        [1] = 227,
        [2] = 229,
        [3] = 230,
        [4] = 238,
        [5] = 239,
        [6] = 240,
        [7] = 245,
        [8] = 248,
        [9] = 253,
        [10] = 254,
        [11] = 255,
        [12] = 257,
        [13] = 259,
        [14] = 261,
        [15] = 263,
        [16] = 269,
        [17] = 271,
        [18] = 273,
        [19] = 275,
        [20] = 281,
        [21] = 283,
        [22] = 287,
        [23] = 299,
        [24] = 305,
        [25] = 314,
        [26] = 318,
        [27] = 322,
        [28] = 324,
        [29] = 328,
        [30] = 337,
        [31] = 341,
        [32] = 345,
        [33] = 347,
        [34] = 351,
        [35] = 353,
        [36] = 357,
        [37] = 363,
        [38] = 367,
        [39] = 369,
        [40] = 378,
        [41] = 380,
        [42] = 382,
        [43] = 192,
        [44] = 193,
        [45] = 194,
        [46] = 195,
        [47] = 196,
        [48] = 197,
        [49] = 198,
        [50] = 199,
        [51] = 200,
        [52] = 201,
        [53] = 202,
        [54] = 203,
        [55] = 204,
        [56] = 205,
        [57] = 206,
        [58] = 207,
        [59] = 208,
        [60] = 209,
        [61] = 210,
        [62] = 211,
        [63] = 212,
        [64] = 213,
        [65] = 214,
        [66] = 216,
        [67] = 217,
        [68] = 218,
        [69] = 219,
        [70] = 220,
        [71] = 221,
        [72] = 222,
        [73] = 256,
        [74] = 258,
        [75] = 260,
        [76] = 262,
        [77] = 268,
        [78] = 270,
        [79] = 272,
        [80] = 274,
        [81] = 280,
        [82] = 282,
        [83] = 286,
        [84] = 298,
        [85] = 313,
        [86] = 317,
        [87] = 321,
        [88] = 323,
        [89] = 327,
        [90] = 336,
        [91] = 340,
        [92] = 344,
        [93] = 346,
        [94] = 350,
        [95] = 352,
        [96] = 356,
        [97] = 362,
        [98] = 366,
        [99] = 368,
        [100] = 377,
        [101] = 379,
        [102] = 381,
        [103] = 161,
        [104] = 162,
        [105] = 163,
        [106] = 164,
        [107] = 165,
        [108] = 167,
        [109] = 169,
        [110] = 171,
        [111] = 174,
        [112] = 176,
        [113] = 177,
        [114] = 178,
        [115] = 179,
        [116] = 181,
        [117] = 183,
        [118] = 185,
        [119] = 187,
        [120] = 188,
        [121] = 189,
        [122] = 190,
        [123] = 191,
        [124] = 215,
        [125] = 247,
        [128] = 1073,
        [129] = 1075,
        [130] = 1078,
        [131] = 1079,
        [132] = 1081,
        [133] = 1092,
        [134] = 1093,
        [135] = 1094,
        [136] = 1095,
        [137] = 1096,
        [138] = 1097,
        [139] = 1098,
        [140] = 1099,
        [141] = 1100,
        [142] = 1101,
        [143] = 1102,
        [144] = 1103,
        [145] = 1105,
        [146] = 1040,
        [147] = 1041,
        [148] = 1042,
        [149] = 1043,
        [150] = 1044,
        [151] = 1045,
        [152] = 1046,
        [153] = 1047,
        [154] = 1048,
        [155] = 1049,
        [156] = 1050,
        [157] = 1051,
        [158] = 1052,
        [159] = 1053,
        [160] = 1054,
        [161] = 1055,
        [162] = 1056,
        [163] = 1057,
        [164] = 1058,
        [165] = 1059,
        [166] = 1060,
        [167] = 1061,
        [168] = 1062,
        [169] = 1063,
        [170] = 1064,
        [171] = 1065,
        [172] = 1066,
        [173] = 1067,
        [174] = 1068,
        [175] = 1069,
        [176] = 1070,
        [177] = 1071,
        [178] = 1025,
        [179] = 1028,
        [180] = 1030,
        [181] = 1031,
        [182] = 1108,
        [183] = 1110,
        [184] = 1111,
        [185] = 1168,
        [186] = 1169,
        [187] = 1026,
        [188] = 1032,
        [189] = 1033,
        [190] = 1034,
        [191] = 1035,
        [192] = 1039,
        [193] = 1106,
        [194] = 1112,
        [195] = 1113,
        [196] = 1114,
        [197] = 1115,
        [198] = 1119,
        [199] = 1038,
        [200] = 1118,
        [201] = 1027,
        [202] = 1036,
        [203] = 1107,
        [204] = 1116,
        [256] = 945,
        [257] = 946,
        [258] = 947,
        [259] = 948,
        [260] = 949,
        [261] = 950,
        [262] = 951,
        [263] = 952,
        [264] = 953,
        [265] = 954,
        [266] = 955,
        [267] = 956,
        [268] = 957,
        [269] = 958,
        [270] = 959,
        [271] = 960,
        [272] = 961,
        [273] = 963,
        [274] = 964,
        [275] = 965,
        [276] = 966,
        [277] = 967,
        [278] = 968,
        [279] = 969,
        [280] = 962,
        [281] = 913,
        [282] = 914,
        [283] = 915,
        [284] = 916,
        [285] = 917,
        [286] = 918,
        [287] = 919,
        [288] = 920,
        [289] = 921,
        [290] = 922,
        [291] = 923,
        [292] = 924,
        [293] = 925,
        [294] = 926,
        [295] = 927,
        [296] = 928,
        [297] = 929,
        [298] = 931,
        [299] = 932,
        [300] = 933,
        [301] = 934,
        [302] = 935,
        [303] = 936,
        [304] = 937,
        [305] = 8364,
        [306] = 8482,
        [307] = 8240,
        [308] = 8230,
        [309] = 8211,
        [310] = 8212,
        [311] = 8216,
        [312] = 8217,
        [313] = 8220,
        [314] = 8221,
        [315] = 8224,
        [316] = 8225,
        [317] = 8226,
        [318] = 8249,
        [319] = 8250,
        [512] = 1575,
        [513] = 1576,
        [514] = 1578,
        [515] = 1579,
        [516] = 1580,
        [517] = 1581,
        [518] = 1582,
        [519] = 1583,
        [520] = 1584,
        [521] = 1585,
        [522] = 1586,
        [523] = 1587,
        [524] = 1588,
        [525] = 1589,
        [526] = 1590,
        [527] = 1591,
        [528] = 1592,
        [529] = 1593,
        [530] = 1594,
        [531] = 1601,
        [532] = 1602,
        [533] = 1603,
        [534] = 1604,
        [535] = 1605,
        [536] = 1606,
        [537] = 1607,
        [538] = 1608,
        [539] = 1610,
        [540] = 1569,
        [541] = 1570,
        [542] = 1571,
        [543] = 1572,
        [544] = 1573,
        [545] = 1574,
        [546] = 1577,
        [547] = 1609,
        [548] = 1611,
        [549] = 1612,
        [550] = 1613,
        [551] = 1614,
        [552] = 1615,
        [553] = 1616,
        [554] = 1617,
        [555] = 1618,
        [556] = 2309,
        [557] = 2310,
        [558] = 2311,
        [559] = 2312,
        [560] = 2313,
        [561] = 2314,
        [562] = 2319,
        [563] = 2320,
        [564] = 2323,
        [565] = 2324,
        [566] = 2325,
        [567] = 2326,
        [568] = 2327,
        [569] = 2328,
        [570] = 2330,
        [571] = 2331,
        [572] = 2332,
        [573] = 2333,
        [574] = 2335,
        [575] = 2336,
        [576] = 2337,
        [577] = 2338,
        [578] = 2339,
        [579] = 2340,
        [580] = 2341,
        [581] = 2342,
        [582] = 2343,
        [583] = 2344,
        [584] = 2346,
        [585] = 2347,
        [586] = 2348,
        [587] = 2349,
        [588] = 2350,
        [589] = 2351,
        [590] = 2352,
        [591] = 2354,
        [592] = 2357,
        [593] = 2358,
        [594] = 2359,
        [595] = 2360,
        [596] = 2361,
        [597] = 2366,
        [598] = 2367,
        [599] = 2368,
        [600] = 2369,
        [601] = 2370,
        [602] = 2375,
        [603] = 2376,
        [604] = 2379,
        [605] = 2380,
        [606] = 2381,
        [607] = 2306,
        [608] = 2307,
        [1024] = 30340,
        [1025] = 19968,
        [1026] = 26159,
        [1027] = 19981,
        [1028] = 20102,
        [1029] = 20154,
        [1030] = 25105,
        [1031] = 22312,
        [1032] = 26377,
        [1033] = 20182,
        [1034] = 36825,
        [1035] = 22823,
        [1036] = 26469,
        [1037] = 20197,
        [1038] = 22269,
        [1039] = 20013,
        [1040] = 21040,
        [1041] = 20250,
        [1042] = 23601,
        [1043] = 23398,
        [1044] = 35828,
        [1045] = 22320,
        [1046] = 19978,
        [1047] = 37324,
        [1048] = 23545,
        [1049] = 29983,
        [1050] = 26102,
        [1051] = 21487,
        [1052] = 21457,
        [1053] = 22810,
        [1054] = 32463,
        [1055] = 34892,
        [1056] = 24037,
        [1057] = 35201,
        [1058] = 22905,
        [1059] = 27861,
        [1060] = 32780,
        [1061] = 20316,
        [1062] = 29992,
        [1063] = 37117,
        [1064] = 21035,
        [1065] = 20027,
        [1066] = 21407,
        [1067] = 25991,
        [1068] = 21270,
        [1069] = 36824,
        [1070] = 24403,
        [1071] = 24180,
        [1072] = 20160,
        [1073] = 21147,
        [1074] = 22914,
        [1075] = 24515,
        [1076] = 25919,
        [1077] = 24773,
        [1078] = 21516,
        [1079] = 25104,
        [1080] = 27599,
        [1081] = 26041,
        [1082] = 21069,
        [1083] = 20986,
        [1084] = 20840,
        [1085] = 21482,
        [1086] = 31038,
        [1087] = 38271,
        [1088] = 23450,
        [1089] = 31181,
        [1090] = 20851,
        [1091] = 26412,
        [1092] = 30475,
        [1093] = 28857,
        [1094] = 26032,
        [1095] = 20844,
        [1096] = 24320,
        [1097] = 20294,
        [1098] = 35748,
        [1099] = 21518,
        [1100] = 35770,
        [1101] = 26524,
        [1102] = 33258,
        [1103] = 22240,
        [1104] = 22825,
        [1105] = 20854,
        [1106] = 27492,
        [1107] = 28982,
        [1108] = 27665,
        [1109] = 38388,
        [1110] = 36947,
        [1111] = 20004,
        [1112] = 30334,
        [1113] = 24605,
        [1114] = 26376,
        [1115] = 34987,
        [1116] = 21592,
        [1117] = 24819,
        [1118] = 29305,
        [1119] = 30524,
        [1120] = 20449,
        [1121] = 25163,
        [1122] = 26126,
        [1123] = 24213,
        [1124] = 35774,
        [1125] = 37096,
        [1126] = 31561,
        [1127] = 30693,
        [1128] = 28216,
        [1129] = 20998,
        [1130] = 23383,
        [1131] = 22238,
        [1132] = 20307,
        [1133] = 22909,
        [1134] = 26356,
        [1135] = 23478,
        [1136] = 36335,
        [1137] = 20043,
        [1138] = 22763,
        [1139] = 21326,
        [1140] = 36164,
        [1141] = 20301,
        [1142] = 22797,
        [1143] = 24847,
        [1144] = 33021,
        [1145] = 24050,
        [1536] = 12353,
        [1537] = 12354,
        [1538] = 12355,
        [1539] = 12356,
        [1540] = 12357,
        [1541] = 12358,
        [1542] = 12359,
        [1543] = 12360,
        [1544] = 12361,
        [1545] = 12362,
        [1546] = 12363,
        [1547] = 12364,
        [1548] = 12365,
        [1549] = 12366,
        [1550] = 12367,
        [1551] = 12368,
        [1552] = 12369,
        [1553] = 12370,
        [1554] = 12371,
        [1555] = 12372,
        [1556] = 12373,
        [1557] = 12374,
        [1558] = 12375,
        [1559] = 12376,
        [1560] = 12377,
        [1561] = 12378,
        [1562] = 12379,
        [1563] = 12380,
        [1564] = 12381,
        [1565] = 12382,
        [1566] = 12383,
        [1567] = 12384,
        [1568] = 12385,
        [1569] = 12386,
        [1570] = 12387,
        [1571] = 12388,
        [1572] = 12389,
        [1573] = 12390,
        [1574] = 12391,
        [1575] = 12392,
        [1576] = 12393,
        [1577] = 12394,
        [1578] = 12395,
        [1579] = 12396,
        [1580] = 12397,
        [1581] = 12398,
        [1582] = 12399,
        [1583] = 12400,
        [1584] = 12401,
        [1585] = 12402,
        [1586] = 12403,
        [1587] = 12404,
        [1588] = 12405,
        [1589] = 12406,
        [1590] = 12407,
        [1591] = 12408,
        [1592] = 12409,
        [1593] = 12410,
        [1594] = 12411,
        [1595] = 12412,
        [1596] = 12413,
        [1597] = 12414,
        [1598] = 12415,
        [1599] = 12416,
        [1600] = 12417,
        [1601] = 12418,
        [1602] = 12419,
        [1603] = 12420,
        [1604] = 12421,
        [1605] = 12422,
        [1606] = 12423,
        [1607] = 12424,
        [1608] = 12425,
        [1609] = 12426,
        [1610] = 12427,
        [1611] = 12428,
        [1612] = 12429,
        [1613] = 12430,
        [1614] = 12431,
        [1615] = 12432,
        [1616] = 12433,
        [1617] = 12434,
        [1618] = 12435,
        [1619] = 12449,
        [1620] = 12450,
        [1621] = 12451,
        [1622] = 12452,
        [1623] = 12453,
        [1624] = 12454,
        [1625] = 12455,
        [1626] = 12456,
        [1627] = 12457,
        [1628] = 12458,
        [1629] = 12459,
        [1630] = 12460,
        [1631] = 12461,
        [1632] = 12462,
        [1633] = 12463,
        [1634] = 12464,
        [1635] = 12465,
        [1636] = 12466,
        [1637] = 12467,
        [1638] = 12468,
        [1639] = 12469,
        [1640] = 12470,
        [1641] = 12471,
        [1642] = 12472,
        [1643] = 12473,
        [1644] = 12474,
        [1645] = 12475,
        [1646] = 12476,
        [1647] = 12477,
        [1648] = 12478,
        [1649] = 12479,
        [1650] = 12480,
        [1651] = 12481,
        [1652] = 12482,
        [1653] = 12483,
        [1654] = 12484,
        [1655] = 12485,
        [1656] = 12486,
        [1657] = 12487,
        [1658] = 12488,
        [1659] = 12489,
        [1660] = 12490,
        [1661] = 12491,
        [1662] = 12492,
        [1663] = 12493,
        [1664] = 12494,
        [1665] = 12495,
        [1666] = 12496,
        [1667] = 12497,
        [1668] = 12498,
        [1669] = 12499,
        [1670] = 12500,
        [1671] = 12501,
        [1672] = 12502,
        [1673] = 12503,
        [1674] = 12504,
        [1675] = 12505,
        [1676] = 12506,
        [1677] = 12507,
        [1678] = 12508,
        [1679] = 12509,
        [1680] = 12510,
        [1681] = 12511,
        [1682] = 12512,
        [1683] = 12513,
        [1684] = 12514,
        [1685] = 12515,
        [1686] = 12516,
        [1687] = 12517,
        [1688] = 12518,
        [1689] = 12519,
        [1690] = 12520,
        [1691] = 12521,
        [1692] = 12522,
        [1693] = 12523,
        [1694] = 12524,
        [1695] = 12525,
        [1696] = 12526,
        [1697] = 12527,
        [1698] = 12528,
        [1699] = 12529,
        [1700] = 12530,
        [1701] = 12531,
        [1702] = 12532,
        [1703] = 12533,
        [1704] = 12534,
        [1705] = 12289,
        [1706] = 12290,
        [1707] = 12300,
        [1708] = 12301,
        [1709] = 12293,
        [1710] = 12540,
        [1711] = 12539,
        [1792] = 4352,
        [1793] = 4353,
        [1794] = 4354,
        [1795] = 4355,
        [1796] = 4356,
        [1797] = 4357,
        [1798] = 4358,
        [1799] = 4359,
        [1800] = 4360,
        [1801] = 4361,
        [1802] = 4362,
        [1803] = 4363,
        [1804] = 4364,
        [1805] = 4365,
        [1806] = 4366,
        [1807] = 4367,
        [1808] = 4368,
        [1809] = 4369,
        [1810] = 4370,
        [1811] = 4449,
        [1812] = 4450,
        [1813] = 4451,
        [1814] = 4452,
        [1815] = 4453,
        [1816] = 4454,
        [1817] = 4455,
        [1818] = 4456,
        [1819] = 4457,
        [1820] = 4458,
        [1821] = 4459,
        [1822] = 4460,
        [1823] = 4461,
        [1824] = 4462,
        [1825] = 4463,
        [1826] = 4464,
        [1827] = 4465,
        [1828] = 4466,
        [1829] = 4467,
        [1830] = 4468,
        [1831] = 4469,
        [1832] = 4520,
        [1833] = 4521,
        [1834] = 4522,
        [1835] = 4523,
        [1836] = 4524,
        [1837] = 4525,
        [1838] = 4526,
        [1839] = 4527,
        [1840] = 4528,
        [1841] = 4529,
        [1842] = 4530,
        [1843] = 4531,
        [1844] = 4532,
        [1845] = 4533,
        [1846] = 4534,
        [1847] = 4535,
        [1848] = 4536,
        [1849] = 4537,
        [1850] = 4538,
        [1851] = 4539,
        [1852] = 4540,
        [1853] = 4541,
        [1854] = 4542,
        [1855] = 4543,
        [1856] = 4544,
        [1857] = 4545,
        [1858] = 4546,
        [1859] = 44032,
        [1860] = 45208,
        [1861] = 45796,
        [1862] = 46972,
        [1863] = 47560,
        [1864] = 48148,
        [1865] = 49324,
        [1866] = 50500,
        [1867] = 51088,
        [1868] = 52264,
        [1869] = 52852,
        [1870] = 53440,
        [1871] = 54028,
        [1872] = 54616,
        [1873] = 45768,
        [1874] = 46020,
        [1875] = 47484,
        [1876] = 50640,
        [1877] = 51060,
        [1878] = 51032,
        [1879] = 51012,
        [1880] = 51008,
        [1881] = 44163,
        [1882] = 48320,
        [1883] = 49373,
        [1884] = 51068,
        [1885] = 51204,
        [1886] = 51201,
        [1887] = 45908,
        [1888] = 47196,
        [1889] = 46108,
    };
    static readonly Dictionary<int, int> _t1FromUni = new() {
        [223] = 0,
        [227] = 1,
        [229] = 2,
        [230] = 3,
        [238] = 4,
        [239] = 5,
        [240] = 6,
        [245] = 7,
        [248] = 8,
        [253] = 9,
        [254] = 10,
        [255] = 11,
        [257] = 12,
        [259] = 13,
        [261] = 14,
        [263] = 15,
        [269] = 16,
        [271] = 17,
        [273] = 18,
        [275] = 19,
        [281] = 20,
        [283] = 21,
        [287] = 22,
        [299] = 23,
        [305] = 24,
        [314] = 25,
        [318] = 26,
        [322] = 27,
        [324] = 28,
        [328] = 29,
        [337] = 30,
        [341] = 31,
        [345] = 32,
        [347] = 33,
        [351] = 34,
        [353] = 35,
        [357] = 36,
        [363] = 37,
        [367] = 38,
        [369] = 39,
        [378] = 40,
        [380] = 41,
        [382] = 42,
        [192] = 43,
        [193] = 44,
        [194] = 45,
        [195] = 46,
        [196] = 47,
        [197] = 48,
        [198] = 49,
        [199] = 50,
        [200] = 51,
        [201] = 52,
        [202] = 53,
        [203] = 54,
        [204] = 55,
        [205] = 56,
        [206] = 57,
        [207] = 58,
        [208] = 59,
        [209] = 60,
        [210] = 61,
        [211] = 62,
        [212] = 63,
        [213] = 64,
        [214] = 65,
        [216] = 66,
        [217] = 67,
        [218] = 68,
        [219] = 69,
        [220] = 70,
        [221] = 71,
        [222] = 72,
        [256] = 73,
        [258] = 74,
        [260] = 75,
        [262] = 76,
        [268] = 77,
        [270] = 78,
        [272] = 79,
        [274] = 80,
        [280] = 81,
        [282] = 82,
        [286] = 83,
        [298] = 84,
        [313] = 85,
        [317] = 86,
        [321] = 87,
        [323] = 88,
        [327] = 89,
        [336] = 90,
        [340] = 91,
        [344] = 92,
        [346] = 93,
        [350] = 94,
        [352] = 95,
        [356] = 96,
        [362] = 97,
        [366] = 98,
        [368] = 99,
        [377] = 100,
        [379] = 101,
        [381] = 102,
        [161] = 103,
        [162] = 104,
        [163] = 105,
        [164] = 106,
        [165] = 107,
        [167] = 108,
        [169] = 109,
        [171] = 110,
        [174] = 111,
        [176] = 112,
        [177] = 113,
        [178] = 114,
        [179] = 115,
        [181] = 116,
        [183] = 117,
        [185] = 118,
        [187] = 119,
        [188] = 120,
        [189] = 121,
        [190] = 122,
        [191] = 123,
        [215] = 124,
        [247] = 125,
        [1073] = 128,
        [1075] = 129,
        [1078] = 130,
        [1079] = 131,
        [1081] = 132,
        [1092] = 133,
        [1093] = 134,
        [1094] = 135,
        [1095] = 136,
        [1096] = 137,
        [1097] = 138,
        [1098] = 139,
        [1099] = 140,
        [1100] = 141,
        [1101] = 142,
        [1102] = 143,
        [1103] = 144,
        [1105] = 145,
        [1040] = 146,
        [1041] = 147,
        [1042] = 148,
        [1043] = 149,
        [1044] = 150,
        [1045] = 151,
        [1046] = 152,
        [1047] = 153,
        [1048] = 154,
        [1049] = 155,
        [1050] = 156,
        [1051] = 157,
        [1052] = 158,
        [1053] = 159,
        [1054] = 160,
        [1055] = 161,
        [1056] = 162,
        [1057] = 163,
        [1058] = 164,
        [1059] = 165,
        [1060] = 166,
        [1061] = 167,
        [1062] = 168,
        [1063] = 169,
        [1064] = 170,
        [1065] = 171,
        [1066] = 172,
        [1067] = 173,
        [1068] = 174,
        [1069] = 175,
        [1070] = 176,
        [1071] = 177,
        [1025] = 178,
        [1028] = 179,
        [1030] = 180,
        [1031] = 181,
        [1108] = 182,
        [1110] = 183,
        [1111] = 184,
        [1168] = 185,
        [1169] = 186,
        [1026] = 187,
        [1032] = 188,
        [1033] = 189,
        [1034] = 190,
        [1035] = 191,
        [1039] = 192,
        [1106] = 193,
        [1112] = 194,
        [1113] = 195,
        [1114] = 196,
        [1115] = 197,
        [1119] = 198,
        [1038] = 199,
        [1118] = 200,
        [1027] = 201,
        [1036] = 202,
        [1107] = 203,
        [1116] = 204,
        [945] = 256,
        [946] = 257,
        [947] = 258,
        [948] = 259,
        [949] = 260,
        [950] = 261,
        [951] = 262,
        [952] = 263,
        [953] = 264,
        [954] = 265,
        [955] = 266,
        [956] = 267,
        [957] = 268,
        [958] = 269,
        [959] = 270,
        [960] = 271,
        [961] = 272,
        [963] = 273,
        [964] = 274,
        [965] = 275,
        [966] = 276,
        [967] = 277,
        [968] = 278,
        [969] = 279,
        [962] = 280,
        [913] = 281,
        [914] = 282,
        [915] = 283,
        [916] = 284,
        [917] = 285,
        [918] = 286,
        [919] = 287,
        [920] = 288,
        [921] = 289,
        [922] = 290,
        [923] = 291,
        [924] = 292,
        [925] = 293,
        [926] = 294,
        [927] = 295,
        [928] = 296,
        [929] = 297,
        [931] = 298,
        [932] = 299,
        [933] = 300,
        [934] = 301,
        [935] = 302,
        [936] = 303,
        [937] = 304,
        [8364] = 305,
        [8482] = 306,
        [8240] = 307,
        [8230] = 308,
        [8211] = 309,
        [8212] = 310,
        [8216] = 311,
        [8217] = 312,
        [8220] = 313,
        [8221] = 314,
        [8224] = 315,
        [8225] = 316,
        [8226] = 317,
        [8249] = 318,
        [8250] = 319,
        [1575] = 512,
        [1576] = 513,
        [1578] = 514,
        [1579] = 515,
        [1580] = 516,
        [1581] = 517,
        [1582] = 518,
        [1583] = 519,
        [1584] = 520,
        [1585] = 521,
        [1586] = 522,
        [1587] = 523,
        [1588] = 524,
        [1589] = 525,
        [1590] = 526,
        [1591] = 527,
        [1592] = 528,
        [1593] = 529,
        [1594] = 530,
        [1601] = 531,
        [1602] = 532,
        [1603] = 533,
        [1604] = 534,
        [1605] = 535,
        [1606] = 536,
        [1607] = 537,
        [1608] = 538,
        [1610] = 539,
        [1569] = 540,
        [1570] = 541,
        [1571] = 542,
        [1572] = 543,
        [1573] = 544,
        [1574] = 545,
        [1577] = 546,
        [1609] = 547,
        [1611] = 548,
        [1612] = 549,
        [1613] = 550,
        [1614] = 551,
        [1615] = 552,
        [1616] = 553,
        [1617] = 554,
        [1618] = 555,
        [2309] = 556,
        [2310] = 557,
        [2311] = 558,
        [2312] = 559,
        [2313] = 560,
        [2314] = 561,
        [2319] = 562,
        [2320] = 563,
        [2323] = 564,
        [2324] = 565,
        [2325] = 566,
        [2326] = 567,
        [2327] = 568,
        [2328] = 569,
        [2330] = 570,
        [2331] = 571,
        [2332] = 572,
        [2333] = 573,
        [2335] = 574,
        [2336] = 575,
        [2337] = 576,
        [2338] = 577,
        [2339] = 578,
        [2340] = 579,
        [2341] = 580,
        [2342] = 581,
        [2343] = 582,
        [2344] = 583,
        [2346] = 584,
        [2347] = 585,
        [2348] = 586,
        [2349] = 587,
        [2350] = 588,
        [2351] = 589,
        [2352] = 590,
        [2354] = 591,
        [2357] = 592,
        [2358] = 593,
        [2359] = 594,
        [2360] = 595,
        [2361] = 596,
        [2366] = 597,
        [2367] = 598,
        [2368] = 599,
        [2369] = 600,
        [2370] = 601,
        [2375] = 602,
        [2376] = 603,
        [2379] = 604,
        [2380] = 605,
        [2381] = 606,
        [2306] = 607,
        [2307] = 608,
        [30340] = 1024,
        [19968] = 1025,
        [26159] = 1026,
        [19981] = 1027,
        [20102] = 1028,
        [20154] = 1029,
        [25105] = 1030,
        [22312] = 1031,
        [26377] = 1032,
        [20182] = 1033,
        [36825] = 1034,
        [22823] = 1035,
        [26469] = 1036,
        [20197] = 1037,
        [22269] = 1038,
        [20013] = 1039,
        [21040] = 1040,
        [20250] = 1041,
        [23601] = 1042,
        [23398] = 1043,
        [35828] = 1044,
        [22320] = 1045,
        [19978] = 1046,
        [37324] = 1047,
        [23545] = 1048,
        [29983] = 1049,
        [26102] = 1050,
        [21487] = 1051,
        [21457] = 1052,
        [22810] = 1053,
        [32463] = 1054,
        [34892] = 1055,
        [24037] = 1056,
        [35201] = 1057,
        [22905] = 1058,
        [27861] = 1059,
        [32780] = 1060,
        [20316] = 1061,
        [29992] = 1062,
        [37117] = 1063,
        [21035] = 1064,
        [20027] = 1065,
        [21407] = 1066,
        [25991] = 1067,
        [21270] = 1068,
        [36824] = 1069,
        [24403] = 1070,
        [24180] = 1071,
        [20160] = 1072,
        [21147] = 1073,
        [22914] = 1074,
        [24515] = 1075,
        [25919] = 1076,
        [24773] = 1077,
        [21516] = 1078,
        [25104] = 1079,
        [27599] = 1080,
        [26041] = 1081,
        [21069] = 1082,
        [20986] = 1083,
        [20840] = 1084,
        [21482] = 1085,
        [31038] = 1086,
        [38271] = 1087,
        [23450] = 1088,
        [31181] = 1089,
        [20851] = 1090,
        [26412] = 1091,
        [30475] = 1092,
        [28857] = 1093,
        [26032] = 1094,
        [20844] = 1095,
        [24320] = 1096,
        [20294] = 1097,
        [35748] = 1098,
        [21518] = 1099,
        [35770] = 1100,
        [26524] = 1101,
        [33258] = 1102,
        [22240] = 1103,
        [22825] = 1104,
        [20854] = 1105,
        [27492] = 1106,
        [28982] = 1107,
        [27665] = 1108,
        [38388] = 1109,
        [36947] = 1110,
        [20004] = 1111,
        [30334] = 1112,
        [24605] = 1113,
        [26376] = 1114,
        [34987] = 1115,
        [21592] = 1116,
        [24819] = 1117,
        [29305] = 1118,
        [30524] = 1119,
        [20449] = 1120,
        [25163] = 1121,
        [26126] = 1122,
        [24213] = 1123,
        [35774] = 1124,
        [37096] = 1125,
        [31561] = 1126,
        [30693] = 1127,
        [28216] = 1128,
        [20998] = 1129,
        [23383] = 1130,
        [22238] = 1131,
        [20307] = 1132,
        [22909] = 1133,
        [26356] = 1134,
        [23478] = 1135,
        [36335] = 1136,
        [20043] = 1137,
        [22763] = 1138,
        [21326] = 1139,
        [36164] = 1140,
        [20301] = 1141,
        [22797] = 1142,
        [24847] = 1143,
        [33021] = 1144,
        [24050] = 1145,
        [12353] = 1536,
        [12354] = 1537,
        [12355] = 1538,
        [12356] = 1539,
        [12357] = 1540,
        [12358] = 1541,
        [12359] = 1542,
        [12360] = 1543,
        [12361] = 1544,
        [12362] = 1545,
        [12363] = 1546,
        [12364] = 1547,
        [12365] = 1548,
        [12366] = 1549,
        [12367] = 1550,
        [12368] = 1551,
        [12369] = 1552,
        [12370] = 1553,
        [12371] = 1554,
        [12372] = 1555,
        [12373] = 1556,
        [12374] = 1557,
        [12375] = 1558,
        [12376] = 1559,
        [12377] = 1560,
        [12378] = 1561,
        [12379] = 1562,
        [12380] = 1563,
        [12381] = 1564,
        [12382] = 1565,
        [12383] = 1566,
        [12384] = 1567,
        [12385] = 1568,
        [12386] = 1569,
        [12387] = 1570,
        [12388] = 1571,
        [12389] = 1572,
        [12390] = 1573,
        [12391] = 1574,
        [12392] = 1575,
        [12393] = 1576,
        [12394] = 1577,
        [12395] = 1578,
        [12396] = 1579,
        [12397] = 1580,
        [12398] = 1581,
        [12399] = 1582,
        [12400] = 1583,
        [12401] = 1584,
        [12402] = 1585,
        [12403] = 1586,
        [12404] = 1587,
        [12405] = 1588,
        [12406] = 1589,
        [12407] = 1590,
        [12408] = 1591,
        [12409] = 1592,
        [12410] = 1593,
        [12411] = 1594,
        [12412] = 1595,
        [12413] = 1596,
        [12414] = 1597,
        [12415] = 1598,
        [12416] = 1599,
        [12417] = 1600,
        [12418] = 1601,
        [12419] = 1602,
        [12420] = 1603,
        [12421] = 1604,
        [12422] = 1605,
        [12423] = 1606,
        [12424] = 1607,
        [12425] = 1608,
        [12426] = 1609,
        [12427] = 1610,
        [12428] = 1611,
        [12429] = 1612,
        [12430] = 1613,
        [12431] = 1614,
        [12432] = 1615,
        [12433] = 1616,
        [12434] = 1617,
        [12435] = 1618,
        [12449] = 1619,
        [12450] = 1620,
        [12451] = 1621,
        [12452] = 1622,
        [12453] = 1623,
        [12454] = 1624,
        [12455] = 1625,
        [12456] = 1626,
        [12457] = 1627,
        [12458] = 1628,
        [12459] = 1629,
        [12460] = 1630,
        [12461] = 1631,
        [12462] = 1632,
        [12463] = 1633,
        [12464] = 1634,
        [12465] = 1635,
        [12466] = 1636,
        [12467] = 1637,
        [12468] = 1638,
        [12469] = 1639,
        [12470] = 1640,
        [12471] = 1641,
        [12472] = 1642,
        [12473] = 1643,
        [12474] = 1644,
        [12475] = 1645,
        [12476] = 1646,
        [12477] = 1647,
        [12478] = 1648,
        [12479] = 1649,
        [12480] = 1650,
        [12481] = 1651,
        [12482] = 1652,
        [12483] = 1653,
        [12484] = 1654,
        [12485] = 1655,
        [12486] = 1656,
        [12487] = 1657,
        [12488] = 1658,
        [12489] = 1659,
        [12490] = 1660,
        [12491] = 1661,
        [12492] = 1662,
        [12493] = 1663,
        [12494] = 1664,
        [12495] = 1665,
        [12496] = 1666,
        [12497] = 1667,
        [12498] = 1668,
        [12499] = 1669,
        [12500] = 1670,
        [12501] = 1671,
        [12502] = 1672,
        [12503] = 1673,
        [12504] = 1674,
        [12505] = 1675,
        [12506] = 1676,
        [12507] = 1677,
        [12508] = 1678,
        [12509] = 1679,
        [12510] = 1680,
        [12511] = 1681,
        [12512] = 1682,
        [12513] = 1683,
        [12514] = 1684,
        [12515] = 1685,
        [12516] = 1686,
        [12517] = 1687,
        [12518] = 1688,
        [12519] = 1689,
        [12520] = 1690,
        [12521] = 1691,
        [12522] = 1692,
        [12523] = 1693,
        [12524] = 1694,
        [12525] = 1695,
        [12526] = 1696,
        [12527] = 1697,
        [12528] = 1698,
        [12529] = 1699,
        [12530] = 1700,
        [12531] = 1701,
        [12532] = 1702,
        [12533] = 1703,
        [12534] = 1704,
        [12289] = 1705,
        [12290] = 1706,
        [12300] = 1707,
        [12301] = 1708,
        [12293] = 1709,
        [12540] = 1710,
        [12539] = 1711,
        [4352] = 1792,
        [4353] = 1793,
        [4354] = 1794,
        [4355] = 1795,
        [4356] = 1796,
        [4357] = 1797,
        [4358] = 1798,
        [4359] = 1799,
        [4360] = 1800,
        [4361] = 1801,
        [4362] = 1802,
        [4363] = 1803,
        [4364] = 1804,
        [4365] = 1805,
        [4366] = 1806,
        [4367] = 1807,
        [4368] = 1808,
        [4369] = 1809,
        [4370] = 1810,
        [4449] = 1811,
        [4450] = 1812,
        [4451] = 1813,
        [4452] = 1814,
        [4453] = 1815,
        [4454] = 1816,
        [4455] = 1817,
        [4456] = 1818,
        [4457] = 1819,
        [4458] = 1820,
        [4459] = 1821,
        [4460] = 1822,
        [4461] = 1823,
        [4462] = 1824,
        [4463] = 1825,
        [4464] = 1826,
        [4465] = 1827,
        [4466] = 1828,
        [4467] = 1829,
        [4468] = 1830,
        [4469] = 1831,
        [4520] = 1832,
        [4521] = 1833,
        [4522] = 1834,
        [4523] = 1835,
        [4524] = 1836,
        [4525] = 1837,
        [4526] = 1838,
        [4527] = 1839,
        [4528] = 1840,
        [4529] = 1841,
        [4530] = 1842,
        [4531] = 1843,
        [4532] = 1844,
        [4533] = 1845,
        [4534] = 1846,
        [4535] = 1847,
        [4536] = 1848,
        [4537] = 1849,
        [4538] = 1850,
        [4539] = 1851,
        [4540] = 1852,
        [4541] = 1853,
        [4542] = 1854,
        [4543] = 1855,
        [4544] = 1856,
        [4545] = 1857,
        [4546] = 1858,
        [44032] = 1859,
        [45208] = 1860,
        [45796] = 1861,
        [46972] = 1862,
        [47560] = 1863,
        [48148] = 1864,
        [49324] = 1865,
        [50500] = 1866,
        [51088] = 1867,
        [52264] = 1868,
        [52852] = 1869,
        [53440] = 1870,
        [54028] = 1871,
        [54616] = 1872,
        [45768] = 1873,
        [46020] = 1874,
        [47484] = 1875,
        [50640] = 1876,
        [51060] = 1877,
        [51032] = 1878,
        [51012] = 1879,
        [51008] = 1880,
        [44163] = 1881,
        [48320] = 1882,
        [49373] = 1883,
        [51068] = 1884,
        [51204] = 1885,
        [51201] = 1886,
        [45908] = 1887,
        [47196] = 1888,
        [46108] = 1889,
    };
    static readonly Dictionary<int, int> _fromUni = new();
    static _Cce() { for (int i = 0; i < 128; i++) _fromUni[_toUni[i]] = i; }
    public static string FromUnicode(string s) {
        s = s.Replace("\t", "  ").Replace("\r", "");
        var sb = new System.Text.StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++) {
            int u = s[i];
            if (char.IsHighSurrogate((char)u) && i+1 < s.Length && char.IsLowSurrogate(s[i+1])) {
                int full = char.ConvertToUtf32((char)u, s[++i]);
                sb.Append((char)(0xF0|(full>>18))); sb.Append((char)(0x80|((full>>12)&0x3F)));
                sb.Append((char)(0x80|((full>>6)&0x3F))); sb.Append((char)(0x80|(full&0x3F)));
            }
            else if (_fromUni.TryGetValue(u, out int b)) sb.Append((char)b);
            else if (_t1FromUni.TryGetValue(u, out int cp)) {
                sb.Append((char)(0xC0 | (cp >> 6)));
                sb.Append((char)(0x80 | (cp & 0x3F)));
            }
            else { sb.Append((char)(0xE0|(u>>12))); sb.Append((char)(0x80|((u>>6)&0x3F))); sb.Append((char)(0x80|(u&0x3F))); }
        }
        return sb.ToString();
    }
    public static string ToUnicode(string s) {
        var sb = new System.Text.StringBuilder(s.Length);
        int i = 0;
        while (i < s.Length) {
            int b = s[i];
            if (b < 0x80) { sb.Append((char)_toUni[b]); i++; }
            else if (b >= 0xC0 && b < 0xE0 && i+1 < s.Length) {
                int cp = ((b & 0x1F) << 6) | (s[i+1] & 0x3F);
                sb.Append(_t1ToUni.TryGetValue(cp, out int u) ? (char)u : '\uFFFD');
                i += 2;
            }
            else if (b >= 0xE0 && b < 0xF0 && i+2 < s.Length) {
                sb.Append((char)(((b&0x0F)<<12)|((s[i+1]&0x3F)<<6)|(s[i+2]&0x3F))); i += 3;
            }
            else if (b >= 0xF0 && i+3 < s.Length) {
                int full = ((b&0x07)<<18)|((s[i+1]&0x3F)<<12)|((s[i+2]&0x3F)<<6)|(s[i+3]&0x3F);
                sb.Append(full >= 0x10000 ? char.ConvertFromUtf32(full) : "\uFFFD"); i += 4;
            }
            else { sb.Append('\uFFFD'); i++; }
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
            else if (_tco_s is HandleExpr _tco_m13)
            {
                var eff_tok = _tco_m13.Field0;
                var body = _tco_m13.Field1;
                var clauses = _tco_m13.Field2;
                return new AHandleExpr(make_name(eff_tok.text), desugar_expr(body), map_list(new Func<HandleClause, AHandleClause>(desugar_handle_clause), clauses));
            }
            else if (_tco_s is LambdaExpr _tco_m14)
            {
                var @params = _tco_m14.Field0;
                var body = _tco_m14.Field1;
                return new ALambdaExpr(map_list(new Func<Token, Name>(desugar_lambda_param), @params), desugar_expr(body));
            }
            else if (_tco_s is ErrExpr _tco_m15)
            {
                var tok = _tco_m15.Field0;
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
        return ((Func<TokenKind, LiteralKind>)((_scrutinee0_) => (_scrutinee0_ is IntegerLiteral _mIntegerLiteral0_ ? new IntLit() : (_scrutinee0_ is NumberLiteral _mNumberLiteral0_ ? new NumLit() : (_scrutinee0_ is TextLiteral _mTextLiteral0_ ? new TextLit() : (_scrutinee0_ is CharLiteral _mCharLiteral0_ ? new CharLit() : (_scrutinee0_ is TrueKeyword _mTrueKeyword0_ ? new BoolLit() : (_scrutinee0_ is FalseKeyword _mFalseKeyword0_ ? new BoolLit() : ((Func<TokenKind, LiteralKind>)((_) => new TextLit()))(_scrutinee0_)))))))))(k);
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
        return ((Func<DoStmt, ADoStmt>)((_scrutinee1_) => (_scrutinee1_ is DoBindStmt _mDoBindStmt1_ ? ((Func<Expr, ADoStmt>)((val) => ((Func<Token, ADoStmt>)((tok) => new ADoBindStmt(make_name(tok.text), desugar_expr(val))))((Token)_mDoBindStmt1_.Field0)))((Expr)_mDoBindStmt1_.Field1) : (_scrutinee1_ is DoExprStmt _mDoExprStmt1_ ? ((Func<Expr, ADoStmt>)((e) => new ADoExprStmt(desugar_expr(e))))((Expr)_mDoExprStmt1_.Field0) : throw new InvalidOperationException("Non-exhaustive match")))))(s);
    }

    public static AHandleClause desugar_handle_clause(HandleClause c)
    {
        return new AHandleClause(make_name(c.op_name.text), make_name(c.resume_name.text), desugar_expr(c.body));
    }

    public static Name desugar_lambda_param(Token tok)
    {
        return make_name(tok.text);
    }

    public static BinaryOp desugar_bin_op(TokenKind k)
    {
        return ((Func<TokenKind, BinaryOp>)((_scrutinee2_) => (_scrutinee2_ is Plus _mPlus2_ ? new OpAdd() : (_scrutinee2_ is Minus _mMinus2_ ? new OpSub() : (_scrutinee2_ is Star _mStar2_ ? new OpMul() : (_scrutinee2_ is Slash _mSlash2_ ? new OpDiv() : (_scrutinee2_ is Caret _mCaret2_ ? new OpPow() : (_scrutinee2_ is DoubleEquals _mDoubleEquals2_ ? new OpEq() : (_scrutinee2_ is NotEquals _mNotEquals2_ ? new OpNotEq() : (_scrutinee2_ is LessThan _mLessThan2_ ? new OpLt() : (_scrutinee2_ is GreaterThan _mGreaterThan2_ ? new OpGt() : (_scrutinee2_ is LessOrEqual _mLessOrEqual2_ ? new OpLtEq() : (_scrutinee2_ is GreaterOrEqual _mGreaterOrEqual2_ ? new OpGtEq() : (_scrutinee2_ is TripleEquals _mTripleEquals2_ ? new OpDefEq() : (_scrutinee2_ is PlusPlus _mPlusPlus2_ ? new OpAppend() : (_scrutinee2_ is ColonColon _mColonColon2_ ? new OpCons() : (_scrutinee2_ is Ampersand _mAmpersand2_ ? new OpAnd() : (_scrutinee2_ is Pipe _mPipe2_ ? new OpOr() : ((Func<TokenKind, BinaryOp>)((_) => new OpAdd()))(_scrutinee2_)))))))))))))))))))(k);
    }

    public static APat desugar_pattern(Pat p)
    {
        return ((Func<Pat, APat>)((_scrutinee3_) => (_scrutinee3_ is VarPat _mVarPat3_ ? ((Func<Token, APat>)((tok) => new AVarPat(make_name(tok.text))))((Token)_mVarPat3_.Field0) : (_scrutinee3_ is LitPat _mLitPat3_ ? ((Func<Token, APat>)((tok) => new ALitPat(tok.text, classify_literal(tok.kind))))((Token)_mLitPat3_.Field0) : (_scrutinee3_ is CtorPat _mCtorPat3_ ? ((Func<List<Pat>, APat>)((subs) => ((Func<Token, APat>)((tok) => new ACtorPat(make_name(tok.text), map_list(new Func<Pat, APat>(desugar_pattern), subs))))((Token)_mCtorPat3_.Field0)))((List<Pat>)_mCtorPat3_.Field1) : (_scrutinee3_ is WildPat _mWildPat3_ ? ((Func<Token, APat>)((tok) => new AWildPat()))((Token)_mWildPat3_.Field0) : throw new InvalidOperationException("Non-exhaustive match")))))))(p);
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
                return new AAppType(new ANamedType(make_name("1\u0011\u0013\u000E")), new List<ATypeExpr>() { desugar_type_expr(elem) });
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
                return new AEffectType(map_list(new Func<Token, Name>(make_type_param_name), effs), desugar_type_expr(ret));
            }
        }
    }

    public static ADef desugar_def(Def d)
    {
        return ((Func<List<ATypeExpr>, ADef>)((ann_types) => new ADef(make_name(d.name.text), map_list(new Func<Token, AParam>(desugar_param), d.@params), ann_types, desugar_expr(d.body))))(desugar_annotations(d.ann));
    }

    public static List<ATypeExpr> desugar_annotations(List<TypeAnn> anns)
    {
        return ((((long)anns.Count) == 0L) ? new List<ATypeExpr>() : ((Func<TypeAnn, List<ATypeExpr>>)((a) => new List<ATypeExpr>() { desugar_type_expr(a.type_expr) }))(anns[(int)0L]));
    }

    public static AParam desugar_param(Token tok)
    {
        return new AParam(make_name(tok.text));
    }

    public static ATypeDef desugar_type_def(TypeDef td)
    {
        return ((Func<TypeBody, ATypeDef>)((_scrutinee4_) => (_scrutinee4_ is RecordBody _mRecordBody4_ ? ((Func<List<RecordFieldDef>, ATypeDef>)((fields) => new ARecordTypeDef(make_name(td.name.text), map_list(new Func<Token, Name>(make_type_param_name), td.type_params), map_list(new Func<RecordFieldDef, ARecordFieldDef>(desugar_record_field_def), fields))))((List<RecordFieldDef>)_mRecordBody4_.Field0) : (_scrutinee4_ is VariantBody _mVariantBody4_ ? ((Func<List<VariantCtorDef>, ATypeDef>)((ctors) => new AVariantTypeDef(make_name(td.name.text), map_list(new Func<Token, Name>(make_type_param_name), td.type_params), map_list(new Func<VariantCtorDef, AVariantCtorDef>(desugar_variant_ctor_def), ctors))))((List<VariantCtorDef>)_mVariantBody4_.Field0) : throw new InvalidOperationException("Non-exhaustive match")))))(td.body);
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
        return new AModule(make_name(module_name), map_list(new Func<Def, ADef>(desugar_def), doc.defs), map_list(new Func<TypeDef, ATypeDef>(desugar_type_def), doc.type_defs), map_list(new Func<EffectDef, AEffectDef>(desugar_effect_def), doc.effect_defs), map_list(new Func<ImportDecl, AImportDecl>(desugar_import), doc.imports));
    }

    public static AImportDecl desugar_import(ImportDecl imp)
    {
        return new AImportDecl(make_name(imp.module_name.text));
    }

    public static AEffectDef desugar_effect_def(EffectDef ed)
    {
        return new AEffectDef(make_name(ed.name.text), map_list(new Func<EffectOpDef, AEffectOpDef>(desugar_effect_op), ed.ops));
    }

    public static AEffectOpDef desugar_effect_op(EffectOpDef op)
    {
        return new AEffectOpDef(make_name(op.name.text), desugar_type_expr(op.type_expr));
    }

    public static List<T3> map_list<T2, T3>(Func<T2, T3> f, List<T2> xs)
    {
        return map_list_loop(f, xs, 0L, ((long)xs.Count), new List<T3>());
    }

    public static List<T5> map_list_loop<T4, T5>(Func<T4, T5> f, List<T4> xs, long i, long len, List<T5> acc)
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
                var _tco_4 = ((Func<List<T5>>)(() => { var _l = acc; _l.Add(f(xs[(int)i])); return _l; }))();
                f = _tco_0;
                xs = _tco_1;
                i = _tco_2;
                len = _tco_3;
                acc = _tco_4;
                continue;
            }
        }
    }

    public static T6 fold_list<T6, T7>(Func<T6, Func<T7, T6>> f, T6 z, List<T7> xs)
    {
        return fold_list_loop(f, z, xs, 0L, ((long)xs.Count));
    }

    public static T8 fold_list_loop<T8, T9>(Func<T8, Func<T9, T8>> f, T8 z, List<T9> xs, long i, long len)
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
        return ((Func<DiagnosticSeverity, string>)((_scrutinee5_) => (_scrutinee5_ is Error _mError5_ ? "\u000D\u0015\u0015\u0010\u0015" : (_scrutinee5_ is Warning _mWarning5_ ? "\u001B\u000F\u0015\u0012\u0011\u0012\u001D" : (_scrutinee5_ is Info _mInfo5_ ? "\u0011\u0012\u001C\u0010" : throw new InvalidOperationException("Non-exhaustive match"))))))(s);
    }

    public static string diagnostic_display(Diagnostic d)
    {
        return string.Concat(severity_label(d.severity), "\u0002", d.code, "E\u0002", d.message);
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

    public static string emit_type_defs(List<ATypeDef> tds, long i)
    {
        return ((i == ((long)tds.Count)) ? "" : string.Concat(emit_type_def(tds[(int)i]), "\u0001", emit_type_defs(tds, (i + 1L))));
    }

    public static string emit_type_def(ATypeDef td)
    {
        return ((Func<ATypeDef, string>)((_scrutinee6_) => (_scrutinee6_ is ARecordTypeDef _mARecordTypeDef6_ ? ((Func<List<ARecordFieldDef>, string>)((fields) => ((Func<List<Name>, string>)((tparams) => ((Func<Name, string>)((name) => ((Func<string, string>)((gen) => string.Concat("\u001F\u0019 \u0017\u0011\u0018\u0002\u0013\u000D\u000F\u0017\u000D\u0016\u0002\u0015\u000D\u0018\u0010\u0015\u0016\u0002", sanitize(name.value), gen, "J", emit_record_field_defs(fields, tparams, 0L), "KF\u0001")))(emit_tparameter_suffix(tparams))))((Name)_mARecordTypeDef6_.Field0)))((List<Name>)_mARecordTypeDef6_.Field1)))((List<ARecordFieldDef>)_mARecordTypeDef6_.Field2) : (_scrutinee6_ is AVariantTypeDef _mAVariantTypeDef6_ ? ((Func<List<AVariantCtorDef>, string>)((ctors) => ((Func<List<Name>, string>)((tparams) => ((Func<Name, string>)((name) => ((Func<string, string>)((gen) => string.Concat("\u001F\u0019 \u0017\u0011\u0018\u0002\u000F \u0013\u000E\u0015\u000F\u0018\u000E\u0002\u0015\u000D\u0018\u0010\u0015\u0016\u0002", sanitize(name.value), gen, "F\u0001", emit_variant_ctors(ctors, name, tparams, 0L), "\u0001")))(emit_tparameter_suffix(tparams))))((Name)_mAVariantTypeDef6_.Field0)))((List<Name>)_mAVariantTypeDef6_.Field1)))((List<AVariantCtorDef>)_mAVariantTypeDef6_.Field2) : throw new InvalidOperationException("Non-exhaustive match")))))(td);
    }

    public static string emit_tparameter_suffix(List<Name> tparams)
    {
        return ((((long)tparams.Count) == 0L) ? "" : string.Concat("O", emit_tparameter_names(tparams, 0L), "P"));
    }

    public static string emit_tparameter_names(List<Name> tparams, long i)
    {
        return ((i == ((long)tparams.Count)) ? "" : ((i == (((long)tparams.Count) - 1L)) ? string.Concat("(", _Cce.FromUnicode(i.ToString())) : string.Concat("(", _Cce.FromUnicode(i.ToString()), "B\u0002", emit_tparameter_names(tparams, (i + 1L)))));
    }

    public static string emit_record_field_defs(List<ARecordFieldDef> fields, List<Name> tparams, long i)
    {
        return ((i == ((long)fields.Count)) ? "" : ((Func<ARecordFieldDef, string>)((f) => string.Concat(emit_type_expr_tp(f.type_expr, tparams), "\u0002", sanitize(f.name.value), ((i < (((long)fields.Count) - 1L)) ? "B\u0002" : ""), emit_record_field_defs(fields, tparams, (i + 1L)))))(fields[(int)i]));
    }

    public static string emit_variant_ctors(List<AVariantCtorDef> ctors, Name base_name, List<Name> tparams, long i)
    {
        return ((i == ((long)ctors.Count)) ? "" : ((Func<AVariantCtorDef, string>)((c) => string.Concat(emit_variant_ctor(c, base_name, tparams), emit_variant_ctors(ctors, base_name, tparams, (i + 1L)))))(ctors[(int)i]));
    }

    public static string emit_variant_ctor(AVariantCtorDef c, Name base_name, List<Name> tparams)
    {
        return ((Func<string, string>)((gen) => ((((long)c.fields.Count) == 0L) ? string.Concat("\u001F\u0019 \u0017\u0011\u0018\u0002\u0013\u000D\u000F\u0017\u000D\u0016\u0002\u0015\u000D\u0018\u0010\u0015\u0016\u0002", sanitize(c.name.value), gen, "\u0002E\u0002", sanitize(base_name.value), gen, "F\u0001") : string.Concat("\u001F\u0019 \u0017\u0011\u0018\u0002\u0013\u000D\u000F\u0017\u000D\u0016\u0002\u0015\u000D\u0018\u0010\u0015\u0016\u0002", sanitize(c.name.value), gen, "J", emit_ctor_fields(c.fields, tparams, 0L), "K\u0002E\u0002", sanitize(base_name.value), gen, "F\u0001"))))(emit_tparameter_suffix(tparams));
    }

    public static string emit_ctor_fields(List<ATypeExpr> fields, List<Name> tparams, long i)
    {
        return ((i == ((long)fields.Count)) ? "" : string.Concat(emit_type_expr_tp(fields[(int)i], tparams), "\u00026\u0011\u000D\u0017\u0016", _Cce.FromUnicode(i.ToString()), ((i < (((long)fields.Count) - 1L)) ? "B\u0002" : ""), emit_ctor_fields(fields, tparams, (i + 1L))));
    }

    public static string emit_type_expr(ATypeExpr te)
    {
        return emit_type_expr_tp(te, new List<Name>());
    }

    public static string emit_type_expr_tp(ATypeExpr te, List<Name> tparams)
    {
        return ((Func<ATypeExpr, string>)((_scrutinee7_) => (_scrutinee7_ is ANamedType _mANamedType7_ ? ((Func<Name, string>)((name) => ((Func<long, string>)((idx) => ((idx >= 0L) ? string.Concat("(", _Cce.FromUnicode(idx.ToString())) : when_type_name(name.value))))(find_tparam_index(tparams, name.value, 0L))))((Name)_mANamedType7_.Field0) : (_scrutinee7_ is AFunType _mAFunType7_ ? ((Func<ATypeExpr, string>)((r) => ((Func<ATypeExpr, string>)((p) => string.Concat("6\u0019\u0012\u0018O", emit_type_expr_tp(p, tparams), "B\u0002", emit_type_expr_tp(r, tparams), "P")))((ATypeExpr)_mAFunType7_.Field0)))((ATypeExpr)_mAFunType7_.Field1) : (_scrutinee7_ is AAppType _mAAppType7_ ? ((Func<List<ATypeExpr>, string>)((args) => ((Func<ATypeExpr, string>)((@base) => string.Concat(emit_type_expr_tp(@base, tparams), "O", emit_type_expr_list_tp(args, tparams, 0L), "P")))((ATypeExpr)_mAAppType7_.Field0)))((List<ATypeExpr>)_mAAppType7_.Field1) : throw new InvalidOperationException("Non-exhaustive match"))))))(te);
    }

    public static long find_tparam_index(List<Name> tparams, string name, long i)
    {
        while (true)
        {
            if ((i == ((long)tparams.Count)))
            {
                return (0L - 1L);
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

    public static string when_type_name(string n)
    {
        return ((n == "+\u0012\u000E\u000D\u001D\u000D\u0015") ? "\u0017\u0010\u0012\u001D" : ((n == ",\u0019\u001A \u000D\u0015") ? "\u0016\u000D\u0018\u0011\u001A\u000F\u0017" : ((n == "(\u000D$\u000E") ? "\u0013\u000E\u0015\u0011\u0012\u001D" : ((n == ":\u0010\u0010\u0017\u000D\u000F\u0012") ? " \u0010\u0010\u0017" : ((n == "1\u0011\u0013\u000E") ? "1\u0011\u0013\u000E" : sanitize(n))))));
    }

    public static string emit_type_expr_list(List<ATypeExpr> args, long i)
    {
        return ((i == ((long)args.Count)) ? "" : string.Concat(emit_type_expr(args[(int)i]), ((i < (((long)args.Count) - 1L)) ? "B\u0002" : ""), emit_type_expr_list(args, (i + 1L))));
    }

    public static string emit_type_expr_list_tp(List<ATypeExpr> args, List<Name> tparams, long i)
    {
        return ((i == ((long)args.Count)) ? "" : string.Concat(emit_type_expr_tp(args[(int)i], tparams), ((i < (((long)args.Count) - 1L)) ? "B\u0002" : ""), emit_type_expr_list_tp(args, tparams, (i + 1L))));
    }

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
                var _ = _tco_s;
                return acc;
            }
        }
    }

    public static List<long> collect_type_var_ids_list(List<CodexType> types, List<long> acc)
    {
        return collect_type_var_ids_list_loop(types, acc, 0L, ((long)types.Count));
    }

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

    public static bool list_contains_int(List<long> xs, long n)
    {
        return list_contains_int_loop(xs, n, 0L, ((long)xs.Count));
    }

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

    public static List<long> list_append_int(List<long> xs, long n)
    {
        return Enumerable.Concat(xs, new List<long>() { n }).ToList();
    }

    public static string generic_suffix(CodexType ty)
    {
        return ((Func<List<long>, string>)((ids) => ((((long)ids.Count) == 0L) ? "" : string.Concat("O", emit_type_params(ids, 0L), "P"))))(collect_type_var_ids(ty, new List<long>()));
    }

    public static string emit_type_params(List<long> ids, long i)
    {
        return ((i == ((long)ids.Count)) ? "" : ((i == (((long)ids.Count) - 1L)) ? string.Concat("(", _Cce.FromUnicode(ids[(int)i].ToString())) : string.Concat("(", _Cce.FromUnicode(ids[(int)i].ToString()), "B\u0002", emit_type_params(ids, (i + 1L)))));
    }

    public static string extract_ctor_type_args(CodexType ty)
    {
        return (ty is ConstructedTy _mConstructedTy8_ ? ((Func<List<CodexType>, string>)((args) => ((Func<Name, string>)((name) => ((((long)args.Count) == 0L) ? "" : string.Concat("O", emit_cs_type_args(args, 0L), "P"))))((Name)_mConstructedTy8_.Field0)))((List<CodexType>)_mConstructedTy8_.Field1) : ((Func<CodexType, string>)((_) => ""))(ty));
    }

    public static bool is_self_call(IRExpr e, string func_name)
    {
        return ((Func<ApplyChain, bool>)((chain) => is_self_call_root(chain.root, func_name)))(collect_apply_chain(e, new List<IRExpr>()));
    }

    public static bool is_self_call_root(IRExpr e, string func_name)
    {
        return (e is IrName _mIrName9_ ? ((Func<CodexType, bool>)((ty) => ((Func<string, bool>)((n) => (n == func_name)))((string)_mIrName9_.Field0)))((CodexType)_mIrName9_.Field1) : ((Func<IRExpr, bool>)((_) => false))(e));
    }

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
                return has_tail_call_branches(branches, func_name, 0L);
            }
            else if (_tco_s is IrApply _tco_m3)
            {
                var f = _tco_m3.Field0;
                var a = _tco_m3.Field1;
                var ty = _tco_m3.Field2;
                return is_self_call(e, func_name);
            }
            {
                var _ = _tco_s;
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
                    var _tco_2 = (i + 1L);
                    branches = _tco_0;
                    func_name = _tco_1;
                    i = _tco_2;
                    continue;
                }
            }
        }
    }

    public static bool should_tco(IRDef d)
    {
        return ((((long)d.@params.Count) == 0L) ? false : has_tail_call(d.body, d.name));
    }

    public static string emit_tco_def(IRDef d, List<ArityEntry> arities)
    {
        return ((Func<CodexType, string>)((ret) => ((Func<string, string>)((gen) => string.Concat("\u0002\u0002\u0002\u0002\u001F\u0019 \u0017\u0011\u0018\u0002\u0013\u000E\u000F\u000E\u0011\u0018\u0002", cs_type(ret), "\u0002", sanitize(d.name), gen, "J", emit_def_params(d.@params, 0L), "K\u0001\u0002\u0002\u0002\u0002Z\u0001\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u001B\u0014\u0011\u0017\u000D\u0002J\u000E\u0015\u0019\u000DK\u0001\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002Z\u0001", emit_tco_body(d.body, d.name, d.@params, arities), "\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002[\u0001\u0002\u0002\u0002\u0002[\u0001")))(generic_suffix(d.type_val))))(get_return_type(d.type_val, ((long)d.@params.Count)));
    }

    public static string emit_tco_body(IRExpr e, string func_name, List<IRParam> @params, List<ArityEntry> arities)
    {
        return ((Func<IRExpr, string>)((_scrutinee10_) => (_scrutinee10_ is IrIf _mIrIf10_ ? ((Func<CodexType, string>)((ty) => ((Func<IRExpr, string>)((el) => ((Func<IRExpr, string>)((t) => ((Func<IRExpr, string>)((c) => emit_tco_if(c, t, el, func_name, @params, arities)))((IRExpr)_mIrIf10_.Field0)))((IRExpr)_mIrIf10_.Field1)))((IRExpr)_mIrIf10_.Field2)))((CodexType)_mIrIf10_.Field3) : (_scrutinee10_ is IrLet _mIrLet10_ ? ((Func<IRExpr, string>)((body) => ((Func<IRExpr, string>)((val) => ((Func<CodexType, string>)((ty) => ((Func<string, string>)((name) => emit_tco_let(name, ty, val, body, func_name, @params, arities)))((string)_mIrLet10_.Field0)))((CodexType)_mIrLet10_.Field1)))((IRExpr)_mIrLet10_.Field2)))((IRExpr)_mIrLet10_.Field3) : (_scrutinee10_ is IrMatch _mIrMatch10_ ? ((Func<CodexType, string>)((ty) => ((Func<List<IRBranch>, string>)((branches) => ((Func<IRExpr, string>)((scrut) => emit_tco_match(scrut, branches, func_name, @params, arities)))((IRExpr)_mIrMatch10_.Field0)))((List<IRBranch>)_mIrMatch10_.Field1)))((CodexType)_mIrMatch10_.Field2) : (_scrutinee10_ is IrApply _mIrApply10_ ? ((Func<CodexType, string>)((rty) => ((Func<IRExpr, string>)((a) => ((Func<IRExpr, string>)((f) => emit_tco_apply(e, func_name, @params, arities)))((IRExpr)_mIrApply10_.Field0)))((IRExpr)_mIrApply10_.Field1)))((CodexType)_mIrApply10_.Field2) : ((Func<IRExpr, string>)((_) => string.Concat("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0015\u000D\u000E\u0019\u0015\u0012\u0002", emit_expr(e, arities), "F\u0001")))(_scrutinee10_)))))))(e);
    }

    public static string emit_tco_apply(IRExpr e, string func_name, List<IRParam> @params, List<ArityEntry> arities)
    {
        return (is_self_call(e, func_name) ? emit_tco_jump(e, @params, arities) : string.Concat("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0015\u000D\u000E\u0019\u0015\u0012\u0002", emit_expr(e, arities), "F\u0001"));
    }

    public static string emit_tco_if(IRExpr cond, IRExpr t, IRExpr el, string func_name, List<IRParam> @params, List<ArityEntry> arities)
    {
        return string.Concat("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0011\u001C\u0002J", emit_expr(cond, arities), "K\u0001\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002Z\u0001", emit_tco_body(t, func_name, @params, arities), "\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002[\u0001\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u000D\u0017\u0013\u000D\u0001\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002Z\u0001", emit_tco_body(el, func_name, @params, arities), "\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002[\u0001");
    }

    public static string emit_tco_let(string name, CodexType ty, IRExpr val, IRExpr body, string func_name, List<IRParam> @params, List<ArityEntry> arities)
    {
        return string.Concat("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002!\u000F\u0015\u0002", sanitize(name), "\u0002M\u0002", emit_expr(val, arities), "F\u0001", emit_tco_body(body, func_name, @params, arities));
    }

    public static string emit_tco_match(IRExpr scrut, List<IRBranch> branches, string func_name, List<IRParam> @params, List<ArityEntry> arities)
    {
        return string.Concat("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002!\u000F\u0015\u0002U\u000E\u0018\u0010U\u0013\u0002M\u0002", emit_expr(scrut, arities), "F\u0001", emit_tco_match_branches(branches, func_name, @params, arities, 0L, true));
    }

    public static string emit_tco_match_branches(List<IRBranch> branches, string func_name, List<IRParam> @params, List<ArityEntry> arities, long i, bool is_first)
    {
        return ((i == ((long)branches.Count)) ? "" : ((Func<IRBranch, string>)((b) => string.Concat(emit_tco_match_branch(b, func_name, @params, arities, i, is_first), emit_tco_match_branches(branches, func_name, @params, arities, (i + 1L), false))))(branches[(int)i]));
    }

    public static string emit_tco_match_branch(IRBranch b, string func_name, List<IRParam> @params, List<ArityEntry> arities, long idx, bool is_first)
    {
        return ((Func<IRPat, string>)((_scrutinee11_) => (_scrutinee11_ is IrWildPat _mIrWildPat11_ ? string.Concat("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002Z\u0001", emit_tco_body(b.body, func_name, @params, arities), "\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002[\u0001") : (_scrutinee11_ is IrVarPat _mIrVarPat11_ ? ((Func<CodexType, string>)((ty) => ((Func<string, string>)((name) => string.Concat("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002Z\u0001\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002!\u000F\u0015\u0002", sanitize(name), "\u0002M\u0002U\u000E\u0018\u0010U\u0013F\u0001", emit_tco_body(b.body, func_name, @params, arities), "\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002[\u0001")))((string)_mIrVarPat11_.Field0)))((CodexType)_mIrVarPat11_.Field1) : (_scrutinee11_ is IrCtorPat _mIrCtorPat11_ ? ((Func<CodexType, string>)((ty) => ((Func<List<IRPat>, string>)((subs) => ((Func<string, string>)((name) => ((Func<string, string>)((keyword) => ((Func<string, string>)((match_var) => string.Concat("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002", keyword, "\u0002JU\u000E\u0018\u0010U\u0013\u0002\u0011\u0013\u0002", sanitize(name), "\u0002", match_var, "K\u0001\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002Z\u0001", emit_tco_ctor_bindings(subs, match_var, 0L), emit_tco_body(b.body, func_name, @params, arities), "\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002[\u0001")))(string.Concat("U\u000E\u0018\u0010U\u001A", _Cce.FromUnicode(idx.ToString())))))((is_first ? "\u0011\u001C" : "\u000D\u0017\u0013\u000D\u0002\u0011\u001C"))))((string)_mIrCtorPat11_.Field0)))((List<IRPat>)_mIrCtorPat11_.Field1)))((CodexType)_mIrCtorPat11_.Field2) : (_scrutinee11_ is IrLitPat _mIrLitPat11_ ? ((Func<CodexType, string>)((ty) => ((Func<string, string>)((text) => ((Func<string, string>)((keyword) => string.Concat("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002", keyword, "\u0002J\u0010 #\u000D\u0018\u000EA'%\u0019\u000F\u0017\u0013JU\u000E\u0018\u0010U\u0013B\u0002", text, "KK\u0001\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002Z\u0001", emit_tco_body(b.body, func_name, @params, arities), "\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002[\u0001")))((is_first ? "\u0011\u001C" : "\u000D\u0017\u0013\u000D\u0002\u0011\u001C"))))((string)_mIrLitPat11_.Field0)))((CodexType)_mIrLitPat11_.Field1) : throw new InvalidOperationException("Non-exhaustive match")))))))(b.pattern);
    }

    public static string emit_tco_ctor_bindings(List<IRPat> subs, string match_var, long i)
    {
        return ((i == ((long)subs.Count)) ? "" : ((Func<IRPat, string>)((sub) => string.Concat(emit_tco_ctor_binding(sub, match_var, i), emit_tco_ctor_bindings(subs, match_var, (i + 1L)))))(subs[(int)i]));
    }

    public static string emit_tco_ctor_binding(IRPat sub, string match_var, long i)
    {
        return (sub is IrVarPat _mIrVarPat12_ ? ((Func<CodexType, string>)((ty) => ((Func<string, string>)((name) => string.Concat("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002!\u000F\u0015\u0002", sanitize(name), "\u0002M\u0002", match_var, "A6\u0011\u000D\u0017\u0016", _Cce.FromUnicode(i.ToString()), "F\u0001")))((string)_mIrVarPat12_.Field0)))((CodexType)_mIrVarPat12_.Field1) : ((Func<IRPat, string>)((_) => ""))(sub));
    }

    public static string emit_tco_jump(IRExpr e, List<IRParam> @params, List<ArityEntry> arities)
    {
        return ((Func<ApplyChain, string>)((chain) => string.Concat(emit_tco_temps(chain.args, arities, 0L), emit_tco_assigns(@params, 0L), "\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0018\u0010\u0012\u000E\u0011\u0012\u0019\u000DF\u0001")))(collect_apply_chain(e, new List<IRExpr>()));
    }

    public static string emit_tco_temps(List<IRExpr> args, List<ArityEntry> arities, long i)
    {
        return ((i == ((long)args.Count)) ? "" : string.Concat("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002!\u000F\u0015\u0002U\u000E\u0018\u0010U", _Cce.FromUnicode(i.ToString()), "\u0002M\u0002", emit_expr(args[(int)i], arities), "F\u0001", emit_tco_temps(args, arities, (i + 1L))));
    }

    public static string emit_tco_assigns(List<IRParam> @params, long i)
    {
        return ((i == ((long)@params.Count)) ? "" : ((Func<IRParam, string>)((p) => string.Concat("\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002", sanitize(p.name), "\u0002M\u0002U\u000E\u0018\u0010U", _Cce.FromUnicode(i.ToString()), "F\u0001", emit_tco_assigns(@params, (i + 1L)))))(@params[(int)i]));
    }

    public static string emit_def(IRDef d, List<ArityEntry> arities)
    {
        return (should_tco(d) ? emit_tco_def(d, arities) : ((Func<CodexType, string>)((ret) => ((Func<string, string>)((gen) => string.Concat("\u0002\u0002\u0002\u0002\u001F\u0019 \u0017\u0011\u0018\u0002\u0013\u000E\u000F\u000E\u0011\u0018\u0002", cs_type(ret), "\u0002", sanitize(d.name), gen, "J", emit_def_params(d.@params, 0L), "K\u0002MP\u0002", emit_expr(d.body, arities), "F\u0001")))(generic_suffix(d.type_val))))(get_return_type(d.type_val, ((long)d.@params.Count))));
    }

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
                    var _ = _tco_s;
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
                var _ = _tco_s;
                return ty;
            }
        }
    }

    public static string emit_def_params(List<IRParam> @params, long i)
    {
        return ((i == ((long)@params.Count)) ? "" : ((Func<IRParam, string>)((p) => string.Concat(cs_type(p.type_val), "\u0002", sanitize(p.name), ((i < (((long)@params.Count) - 1L)) ? "B\u0002" : ""), emit_def_params(@params, (i + 1L)))))(@params[(int)i]));
    }

    public static string emit_cce_runtime()
    {
        return string.Concat("\u0013\u000E\u000F\u000E\u0011\u0018\u0002\u0018\u0017\u000F\u0013\u0013\u0002U2\u0018\u000D\u0002Z\u0001", "\u0002\u0002\u0002\u0002\u0013\u000E\u000F\u000E\u0011\u0018\u0002\u0015\u000D\u000F\u0016\u0010\u0012\u0017\u001E\u0002\u0011\u0012\u000EXY\u0002U\u000E\u00103\u0012\u0011\u0002M\u0002Z\u0001", "\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0003B\u0002\u0004\u0003B\u0002\u0006\u0005B\u0001", "\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0007\u000BB\u0002\u0007\u000CB\u0002\u0008\u0003B\u0002\u0008\u0004B\u0002\u0008\u0005B\u0002\u0008\u0006B\u0002\u0008\u0007B\u0002\u0008\u0008B\u0002\u0008\u0009B\u0002\u0008\u000AB\u0001", "\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0004\u0003\u0004B\u0002\u0004\u0004\u0009B\u0002\u000C\u000AB\u0002\u0004\u0004\u0004B\u0002\u0004\u0003\u0008B\u0002\u0004\u0004\u0003B\u0002\u0004\u0004\u0008B\u0002\u0004\u0003\u0007B\u0002\u0004\u0004\u0007B\u0002\u0004\u0003\u0003B\u0001", "\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0004\u0003\u000BB\u0002\u000C\u000CB\u0002\u0004\u0004\u000AB\u0002\u0004\u0003\u000CB\u0002\u0004\u0004\u000CB\u0002\u0004\u0003\u0005B\u0002\u0004\u0003\u0006B\u0002\u0004\u0005\u0004B\u0002\u0004\u0004\u0005B\u0002\u000C\u000BB\u0001", "\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0004\u0004\u000BB\u0002\u0004\u0003\u000AB\u0002\u0004\u0003\u0009B\u0002\u0004\u0005\u0003B\u0002\u0004\u0004\u0006B\u0002\u0004\u0005\u0005B\u0001", "\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0009\u000CB\u0002\u000B\u0007B\u0002\u0009\u0008B\u0002\u000A\u000CB\u0002\u000A\u0006B\u0002\u000A\u000BB\u0002\u000B\u0006B\u0002\u000A\u0005B\u0002\u000B\u0005B\u0002\u0009\u000BB\u0001", "\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u000A\u0009B\u0002\u0009\u000AB\u0002\u000B\u0008B\u0002\u000A\u000AB\u0002\u000B\u000AB\u0002\u000A\u0003B\u0002\u000A\u0004B\u0002\u000B\u000CB\u0002\u000B\u0003B\u0002\u0009\u0009B\u0001", "\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u000B\u0009B\u0002\u000A\u0008B\u0002\u000A\u0007B\u0002\u000B\u000BB\u0002\u000B\u0004B\u0002\u000C\u0003B\u0001", "\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0007\u0009B\u0002\u0007\u0007B\u0002\u0006\u0006B\u0002\u0009\u0006B\u0002\u0008\u000BB\u0002\u0008\u000CB\u0002\u0006\u000CB\u0002\u0006\u0007B\u0002\u0007\u0008B\u0002\u0007\u0003B\u0002\u0007\u0004B\u0001", "\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0007\u0006B\u0002\u0009\u0004B\u0002\u0007\u0005B\u0002\u0009\u0003B\u0002\u0009\u0005B\u0001", "\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0007\u000AB\u0002\u0009\u0007B\u0002\u0006\u0008B\u0002\u0006\u000BB\u0002\u000C\u0008B\u0002\u000C\u0005B\u0002\u0004\u0005\u0007B\u0002\u000C\u0004B\u0002\u000C\u0006B\u0002\u0004\u0005\u0006B\u0002\u0004\u0005\u0008B\u0002\u0004\u0005\u0009B\u0002\u000C\u0009B\u0001", "\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0005\u0006\u0006B\u0002\u0005\u0006\u0005B\u0002\u0005\u0006\u0007B\u0002\u0005\u0006\u0008B\u0002\u0005\u0005\u0008B\u0002\u0005\u0005\u0007B\u0002\u0005\u0005\u0009B\u0002\u0005\u0005\u000BB\u0002\u0005\u0007\u0006B\u0002\u0005\u0007\u0005B\u0001", "\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0005\u0007\u0007B\u0002\u0005\u0007\u0009B\u0002\u0005\u0008\u0003B\u0002\u0005\u0007\u000CB\u0002\u0005\u0008\u0004B\u0002\u0005\u0008\u0005B\u0002\u0005\u0007\u0004B\u0002\u0005\u0006\u0004B\u0002\u0005\u0006\u000AB\u0001", "\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0004\u0003\u000A\u0005B\u0002\u0004\u0003\u000B\u0009B\u0002\u0004\u0003\u000A\u000AB\u0002\u0004\u0003\u000B\u0003B\u0002\u0004\u0003\u000B\u0008B\u0002\u0004\u0003\u000C\u0003B\u0002\u0004\u0003\u000B\u000CB\u0002\u0004\u0003\u000B\u000BB\u0001", "\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0004\u0003\u000A\u0007B\u0002\u0004\u0003\u000B\u0006B\u0002\u0004\u0003\u000B\u0005B\u0002\u0004\u0003\u000B\u0007B\u0002\u0004\u0003\u000A\u0009B\u0002\u0004\u0003\u000B\u000AB\u0002\u0004\u0003\u000C\u0004\u0001", "\u0002\u0002\u0002\u0002[F\u0001", "\u0002\u0002\u0002\u0002\u0013\u000E\u000F\u000E\u0011\u0018\u0002\u0015\u000D\u000F\u0016\u0010\u0012\u0017\u001E\u00020\u0011\u0018\u000E\u0011\u0010\u0012\u000F\u0015\u001EO\u0011\u0012\u000EB\u0002\u0011\u0012\u000EP\u0002U\u001C\u0015\u0010\u001A3\u0012\u0011\u0002M\u0002\u0012\u000D\u001BJKF\u0001", "\u0002\u0002\u0002\u0002\u0013\u000E\u000F\u000E\u0011\u0018\u0002U2\u0018\u000DJK\u0002Z\u0002\u001C\u0010\u0015\u0002J\u0011\u0012\u000E\u0002\u0011\u0002M\u0002\u0003F\u0002\u0011\u0002O\u0002\u0004\u0005\u000BF\u0002\u0011LLK\u0002U\u001C\u0015\u0010\u001A3\u0012\u0011XU\u000E\u00103\u0012\u0011X\u0011YY\u0002M\u0002\u0011F\u0002[\u0001", "\u0002\u0002\u0002\u0002\u001F\u0019 \u0017\u0011\u0018\u0002\u0013\u000E\u000F\u000E\u0011\u0018\u0002\u0013\u000E\u0015\u0011\u0012\u001D\u00026\u0015\u0010\u001A3\u0012\u0011\u0018\u0010\u0016\u000DJ\u0013\u000E\u0015\u0011\u0012\u001D\u0002\u0013K\u0002Z\u0001", "\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002!\u000F\u0015\u0002\u0018\u0013\u0002M\u0002\u0012\u000D\u001B\u0002\u0018\u0014\u000F\u0015X\u0013A1\u000D\u0012\u001D\u000E\u0014YF\u0001", "\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u001C\u0010\u0015\u0002J\u0011\u0012\u000E\u0002\u0011\u0002M\u0002\u0003F\u0002\u0011\u0002O\u0002\u0013A1\u000D\u0012\u001D\u000E\u0014F\u0002\u0011LLK\u0002Z\u0001", "\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0011\u0012\u000E\u0002\u0019\u0002M\u0002\u0013X\u0011YF\u0001", "\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0018\u0013X\u0011Y\u0002M\u0002U\u001C\u0015\u0010\u001A3\u0012\u0011A(\u0015\u001E7\u000D\u000E;\u000F\u0017\u0019\u000DJ\u0019B\u0002\u0010\u0019\u000E\u0002\u0011\u0012\u000E\u0002\u0018K\u0002D\u0002J\u0018\u0014\u000F\u0015K\u0018\u0002E\u0002J\u0018\u0014\u000F\u0015K\u0009\u000BF\u0001", "\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002[\u0001", "\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0015\u000D\u000E\u0019\u0015\u0012\u0002\u0012\u000D\u001B\u0002\u0013\u000E\u0015\u0011\u0012\u001DJ\u0018\u0013KF\u0001", "\u0002\u0002\u0002\u0002[\u0001", "\u0002\u0002\u0002\u0002\u001F\u0019 \u0017\u0011\u0018\u0002\u0013\u000E\u000F\u000E\u0011\u0018\u0002\u0013\u000E\u0015\u0011\u0012\u001D\u0002(\u00103\u0012\u0011\u0018\u0010\u0016\u000DJ\u0013\u000E\u0015\u0011\u0012\u001D\u0002\u0013K\u0002Z\u0001", "\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002!\u000F\u0015\u0002\u0018\u0013\u0002M\u0002\u0012\u000D\u001B\u0002\u0018\u0014\u000F\u0015X\u0013A1\u000D\u0012\u001D\u000E\u0014YF\u0001", "\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u001C\u0010\u0015\u0002J\u0011\u0012\u000E\u0002\u0011\u0002M\u0002\u0003F\u0002\u0011\u0002O\u0002\u0013A1\u000D\u0012\u001D\u000E\u0014F\u0002\u0011LLK\u0002Z\u0001", "\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0011\u0012\u000E\u0002 \u0002M\u0002\u0013X\u0011YF\u0001", "\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0018\u0013X\u0011Y\u0002M\u0002J \u0002PM\u0002\u0003\u0002TT\u0002 \u0002O\u0002\u0004\u0005\u000BK\u0002D\u0002J\u0018\u0014\u000F\u0015KU\u000E\u00103\u0012\u0011X Y\u0002E\u0002GV\u00196660GF\u0001", "\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002[\u0001", "\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0015\u000D\u000E\u0019\u0015\u0012\u0002\u0012\u000D\u001B\u0002\u0013\u000E\u0015\u0011\u0012\u001DJ\u0018\u0013KF\u0001", "\u0002\u0002\u0002\u0002[\u0001", "\u0002\u0002\u0002\u0002\u001F\u0019 \u0017\u0011\u0018\u0002\u0013\u000E\u000F\u000E\u0011\u0018\u0002\u0017\u0010\u0012\u001D\u00023\u0012\u0011(\u00102\u0018\u000DJ\u0017\u0010\u0012\u001D\u0002\u0019K\u0002Z\u0001", "\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0015\u000D\u000E\u0019\u0015\u0012\u0002U\u001C\u0015\u0010\u001A3\u0012\u0011A(\u0015\u001E7\u000D\u000E;\u000F\u0017\u0019\u000DJJ\u0011\u0012\u000EK\u0019B\u0002\u0010\u0019\u000E\u0002\u0011\u0012\u000E\u0002\u0018K\u0002D\u0002\u0018\u0002E\u0002\u0009\u000BF\u0001", "\u0002\u0002\u0002\u0002[\u0001", "\u0002\u0002\u0002\u0002\u001F\u0019 \u0017\u0011\u0018\u0002\u0013\u000E\u000F\u000E\u0011\u0018\u0002\u0017\u0010\u0012\u001D\u00022\u0018\u000D(\u00103\u0012\u0011J\u0017\u0010\u0012\u001D\u0002 K\u0002Z\u0001", "\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0002\u0015\u000D\u000E\u0019\u0015\u0012\u0002J \u0002PM\u0002\u0003\u0002TT\u0002 \u0002O\u0002\u0004\u0005\u000BK\u0002D\u0002U\u000E\u00103\u0012\u0011XJ\u0011\u0012\u000EK Y\u0002E\u0002\u0009\u0008\u0008\u0006\u0006F\u0001", "\u0002\u0002\u0002\u0002[\u0001", "[\u0001\u0001");
    }

    public static string emit_full_module(IRModule m, List<ATypeDef> type_defs)
    {
        return ((Func<List<ArityEntry>, string>)((arities) => string.Concat("\u0019\u0013\u0011\u0012\u001D\u0002-\u001E\u0013\u000E\u000D\u001AF\u0001\u0019\u0013\u0011\u0012\u001D\u0002-\u001E\u0013\u000E\u000D\u001AA2\u0010\u0017\u0017\u000D\u0018\u000E\u0011\u0010\u0012\u0013A7\u000D\u0012\u000D\u0015\u0011\u0018F\u0001\u0019\u0013\u0011\u0012\u001D\u0002-\u001E\u0013\u000E\u000D\u001AA+*F\u0001\u0019\u0013\u0011\u0012\u001D\u0002-\u001E\u0013\u000E\u000D\u001AA1\u0011\u0012%F\u0001\u0019\u0013\u0011\u0012\u001D\u0002-\u001E\u0013\u000E\u000D\u001AA(\u0014\u0015\u000D\u000F\u0016\u0011\u0012\u001DA(\u000F\u0013\"\u0013F\u0001\u0001", "2\u0010\u0016\u000D$U", sanitize(m.name.value), "A\u001A\u000F\u0011\u0012JKF\u0001\u0001", emit_cce_runtime(), emit_type_defs(type_defs, 0L), emit_class_header(m.name.value), emit_defs(m.defs, 0L, arities), "[\u0001")))(build_arity_map(m.defs, 0L));
    }

    public static string emit_module(IRModule m)
    {
        return ((Func<List<ArityEntry>, string>)((arities) => string.Concat("\u0019\u0013\u0011\u0012\u001D\u0002-\u001E\u0013\u000E\u000D\u001AF\u0001\u0019\u0013\u0011\u0012\u001D\u0002-\u001E\u0013\u000E\u000D\u001AA2\u0010\u0017\u0017\u000D\u0018\u000E\u0011\u0010\u0012\u0013A7\u000D\u0012\u000D\u0015\u0011\u0018F\u0001\u0019\u0013\u0011\u0012\u001D\u0002-\u001E\u0013\u000E\u000D\u001AA+*F\u0001\u0019\u0013\u0011\u0012\u001D\u0002-\u001E\u0013\u000E\u000D\u001AA1\u0011\u0012%F\u0001\u0019\u0013\u0011\u0012\u001D\u0002-\u001E\u0013\u000E\u000D\u001AA(\u0014\u0015\u000D\u000F\u0016\u0011\u0012\u001DA(\u000F\u0013\"\u0013F\u0001\u0001", "2\u0010\u0016\u000D$U", sanitize(m.name.value), "A\u001A\u000F\u0011\u0012JKF\u0001\u0001", emit_cce_runtime(), emit_class_header(m.name.value), emit_defs(m.defs, 0L, arities), "[\u0001")))(build_arity_map(m.defs, 0L));
    }

    public static string emit_class_header(string name)
    {
        return string.Concat("\u001F\u0019 \u0017\u0011\u0018\u0002\u0013\u000E\u000F\u000E\u0011\u0018\u0002\u0018\u0017\u000F\u0013\u0013\u00022\u0010\u0016\u000D$U", sanitize(name), "\u0001Z\u0001");
    }

    public static string emit_defs(List<IRDef> defs, long i, List<ArityEntry> arities)
    {
        return ((i == ((long)defs.Count)) ? "" : string.Concat(emit_def(defs[(int)i], arities), "\u0001", emit_defs(defs, (i + 1L), arities)));
    }

    public static bool is_cs_keyword(string n)
    {
        return ((n == "\u0018\u0017\u000F\u0013\u0013") ? true : ((n == "\u0013\u000E\u000F\u000E\u0011\u0018") ? true : ((n == "!\u0010\u0011\u0016") ? true : ((n == "\u0015\u000D\u000E\u0019\u0015\u0012") ? true : ((n == "\u0011\u001C") ? true : ((n == "\u000D\u0017\u0013\u000D") ? true : ((n == "\u001C\u0010\u0015") ? true : ((n == "\u001B\u0014\u0011\u0017\u000D") ? true : ((n == "\u0016\u0010") ? true : ((n == "\u0013\u001B\u0011\u000E\u0018\u0014") ? true : ((n == "\u0018\u000F\u0013\u000D") ? true : ((n == " \u0015\u000D\u000F\"") ? true : ((n == "\u0018\u0010\u0012\u000E\u0011\u0012\u0019\u000D") ? true : ((n == "\u0012\u000D\u001B") ? true : ((n == "\u000E\u0014\u0011\u0013") ? true : ((n == " \u000F\u0013\u000D") ? true : ((n == "\u0012\u0019\u0017\u0017") ? true : ((n == "\u000E\u0015\u0019\u000D") ? true : ((n == "\u001C\u000F\u0017\u0013\u000D") ? true : ((n == "\u0011\u0012\u000E") ? true : ((n == "\u0017\u0010\u0012\u001D") ? true : ((n == "\u0013\u000E\u0015\u0011\u0012\u001D") ? true : ((n == " \u0010\u0010\u0017") ? true : ((n == "\u0016\u0010\u0019 \u0017\u000D") ? true : ((n == "\u0016\u000D\u0018\u0011\u001A\u000F\u0017") ? true : ((n == "\u0010 #\u000D\u0018\u000E") ? true : ((n == "\u0011\u0012") ? true : ((n == "\u0011\u0013") ? true : ((n == "\u000F\u0013") ? true : ((n == "\u000E\u001E\u001F\u000D\u0010\u001C") ? true : ((n == "\u0016\u000D\u001C\u000F\u0019\u0017\u000E") ? true : ((n == "\u000E\u0014\u0015\u0010\u001B") ? true : ((n == "\u000E\u0015\u001E") ? true : ((n == "\u0018\u000F\u000E\u0018\u0014") ? true : ((n == "\u001C\u0011\u0012\u000F\u0017\u0017\u001E") ? true : ((n == "\u0019\u0013\u0011\u0012\u001D") ? true : ((n == "\u0012\u000F\u001A\u000D\u0013\u001F\u000F\u0018\u000D") ? true : ((n == "\u001F\u0019 \u0017\u0011\u0018") ? true : ((n == "\u001F\u0015\u0011!\u000F\u000E\u000D") ? true : ((n == "\u001F\u0015\u0010\u000E\u000D\u0018\u000E\u000D\u0016") ? true : ((n == "\u0011\u0012\u000E\u000D\u0015\u0012\u000F\u0017") ? true : ((n == "\u000F \u0013\u000E\u0015\u000F\u0018\u000E") ? true : ((n == "\u0013\u000D\u000F\u0017\u000D\u0016") ? true : ((n == "\u0010!\u000D\u0015\u0015\u0011\u0016\u000D") ? true : ((n == "!\u0011\u0015\u000E\u0019\u000F\u0017") ? true : ((n == "\u000D!\u000D\u0012\u000E") ? true : ((n == "\u0016\u000D\u0017\u000D\u001D\u000F\u000E\u000D") ? true : ((n == "\u0010\u0019\u000E") ? true : ((n == "\u0015\u000D\u001C") ? true : ((n == "\u001F\u000F\u0015\u000F\u001A\u0013") ? true : false))))))))))))))))))))))))))))))))))))))))))))))))));
    }

    public static string sanitize(string name)
    {
        return ((Func<string, string>)((s) => (is_cs_keyword(s) ? string.Concat("R", s) : (is_cs_member_name(s) ? string.Concat(s, "U") : s))))(name.Replace("I", "U"));
    }

    public static bool is_cs_member_name(string n)
    {
        return ((n == "'%\u0019\u000F\u0017\u0013") ? true : ((n == "7\u000D\u000E.\u000F\u0013\u00142\u0010\u0016\u000D") ? true : ((n == "(\u0010-\u000E\u0015\u0011\u0012\u001D") ? true : ((n == "7\u000D\u000E(\u001E\u001F\u000D") ? true : ((n == "4\u000D\u001A \u000D\u0015\u001B\u0011\u0013\u000D2\u0017\u0010\u0012\u000D") ? true : false)))));
    }

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
                return "\u0016\u000D\u0018\u0011\u001A\u000F\u0017";
            }
            else if (_tco_s is TextTy _tco_m2)
            {
                return "\u0013\u000E\u0015\u0011\u0012\u001D";
            }
            else if (_tco_s is BooleanTy _tco_m3)
            {
                return " \u0010\u0010\u0017";
            }
            else if (_tco_s is CharTy _tco_m4)
            {
                return "\u0017\u0010\u0012\u001D";
            }
            else if (_tco_s is VoidTy _tco_m5)
            {
                return "!\u0010\u0011\u0016";
            }
            else if (_tco_s is NothingTy _tco_m6)
            {
                return "\u0010 #\u000D\u0018\u000E";
            }
            else if (_tco_s is ErrorTy _tco_m7)
            {
                return "\u0010 #\u000D\u0018\u000E";
            }
            else if (_tco_s is FunTy _tco_m8)
            {
                var p = _tco_m8.Field0;
                var r = _tco_m8.Field1;
                return string.Concat("6\u0019\u0012\u0018O", cs_type(p), "B\u0002", cs_type(r), "P");
            }
            else if (_tco_s is ListTy _tco_m9)
            {
                var elem = _tco_m9.Field0;
                return string.Concat("1\u0011\u0013\u000EO", cs_type(elem), "P");
            }
            else if (_tco_s is TypeVar _tco_m10)
            {
                var id = _tco_m10.Field0;
                return string.Concat("(", _Cce.FromUnicode(id.ToString()));
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
                if ((((long)args.Count) == 0L))
                {
                    return sanitize(name.value);
                }
                else
                {
                    return string.Concat(sanitize(name.value), "O", emit_cs_type_args(args, 0L), "P");
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

    public static string emit_cs_type_args(List<CodexType> args, long i)
    {
        return ((i == ((long)args.Count)) ? "" : ((Func<string, string>)((t) => ((i == (((long)args.Count) - 1L)) ? t : string.Concat(t, "B\u0002", emit_cs_type_args(args, (i + 1L))))))(cs_type(args[(int)i])));
    }

    public static List<ArityEntry> build_arity_map(List<IRDef> defs, long i)
    {
        return ((i == ((long)defs.Count)) ? new List<ArityEntry>() : ((Func<IRDef, List<ArityEntry>>)((d) => Enumerable.Concat(new List<ArityEntry>() { new ArityEntry(d.name, ((long)d.@params.Count)) }, build_arity_map(defs, (i + 1L))).ToList()))(defs[(int)i]));
    }

    public static List<ArityEntry> build_arity_map_from_ast(List<ADef> defs, long i)
    {
        return ((i == ((long)defs.Count)) ? new List<ArityEntry>() : ((Func<ADef, List<ArityEntry>>)((d) => Enumerable.Concat(new List<ArityEntry>() { new ArityEntry(d.name.value, ((long)d.@params.Count)) }, build_arity_map_from_ast(defs, (i + 1L))).ToList()))(defs[(int)i]));
    }

    public static long lookup_arity(List<ArityEntry> entries, string name)
    {
        return lookup_arity_loop(entries, name, 0L, ((long)entries.Count));
    }

    public static long lookup_arity_loop(List<ArityEntry> entries, string name, long i, long len)
    {
        while (true)
        {
            if ((i == len))
            {
                return (0L - 1L);
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
                var _tco_0 = f;
                var _tco_1 = Enumerable.Concat(new List<IRExpr>() { a }, acc).ToList();
                e = _tco_0;
                acc = _tco_1;
                continue;
            }
            {
                var _ = _tco_s;
                return new ApplyChain(e, acc);
            }
        }
    }

    public static bool is_upper_letter(long c)
    {
        return ((Func<long, bool>)((code) => ((code >= 41L) && (code <= 64L))))(c);
    }

    public static string emit_apply_args(List<IRExpr> args, List<ArityEntry> arities, long i)
    {
        return ((i == ((long)args.Count)) ? "" : ((i == (((long)args.Count) - 1L)) ? emit_expr(args[(int)i], arities) : string.Concat(emit_expr(args[(int)i], arities), "B\u0002", emit_apply_args(args, arities, (i + 1L)))));
    }

    public static string emit_partial_params(long i, long count)
    {
        return ((i == count) ? "" : ((i == (count - 1L)) ? string.Concat("U\u001F", _Cce.FromUnicode(i.ToString()), "U") : string.Concat("U\u001F", _Cce.FromUnicode(i.ToString()), "U", "B\u0002", emit_partial_params((i + 1L), count))));
    }

    public static string emit_partial_wrappers(long i, long count)
    {
        return ((i == count) ? "" : string.Concat("JU\u001F", _Cce.FromUnicode(i.ToString()), "UK\u0002MP\u0002", emit_partial_wrappers((i + 1L), count)));
    }

    public static bool is_builtin_name(string n)
    {
        return ((n == "\u0013\u0014\u0010\u001B") ? true : ((n == "\u0012\u000D\u001D\u000F\u000E\u000D") ? true : ((n == "\u001F\u0015\u0011\u0012\u000EI\u0017\u0011\u0012\u000D") ? true : ((n == "\u000E\u000D$\u000EI\u0017\u000D\u0012\u001D\u000E\u0014") ? true : ((n == "\u0011\u0013I\u0017\u000D\u000E\u000E\u000D\u0015") ? true : ((n == "\u0011\u0013I\u0016\u0011\u001D\u0011\u000E") ? true : ((n == "\u0011\u0013I\u001B\u0014\u0011\u000E\u000D\u0013\u001F\u000F\u0018\u000D") ? true : ((n == "\u000E\u000D$\u000EI\u000E\u0010I\u0011\u0012\u000E\u000D\u001D\u000D\u0015") ? true : ((n == "\u0011\u0012\u000E\u000D\u001D\u000D\u0015I\u000E\u0010I\u000E\u000D$\u000E") ? true : ((n == "\u0018\u0014\u000F\u0015I\u0018\u0010\u0016\u000D") ? true : ((n == "\u0018\u0014\u000F\u0015I\u0018\u0010\u0016\u000DI\u000F\u000E") ? true : ((n == "\u0018\u0010\u0016\u000DI\u000E\u0010I\u0018\u0014\u000F\u0015") ? true : ((n == "\u0018\u0014\u000F\u0015I\u000E\u0010I\u000E\u000D$\u000E") ? true : ((n == "\u0017\u0011\u0013\u000EI\u0017\u000D\u0012\u001D\u000E\u0014") ? true : ((n == "\u0018\u0014\u000F\u0015I\u000F\u000E") ? true : ((n == "\u0013\u0019 \u0013\u000E\u0015\u0011\u0012\u001D") ? true : ((n == "\u0017\u0011\u0013\u000EI\u000F\u000E") ? true : ((n == "\u0017\u0011\u0013\u000EI\u0011\u0012\u0013\u000D\u0015\u000EI\u000F\u000E") ? true : ((n == "\u0017\u0011\u0013\u000EI\u0013\u0012\u0010\u0018") ? true : ((n == "\u000E\u000D$\u000EI\u0018\u0010\u001A\u001F\u000F\u0015\u000D") ? true : ((n == "\u000E\u000D$\u000EI\u0015\u000D\u001F\u0017\u000F\u0018\u000D") ? true : ((n == "\u0010\u001F\u000D\u0012I\u001C\u0011\u0017\u000D") ? true : ((n == "\u0015\u000D\u000F\u0016I\u000F\u0017\u0017") ? true : ((n == "\u0018\u0017\u0010\u0013\u000DI\u001C\u0011\u0017\u000D") ? true : ((n == "\u0015\u000D\u000F\u0016I\u0017\u0011\u0012\u000D") ? true : ((n == "\u0015\u000D\u000F\u0016I\u001C\u0011\u0017\u000D") ? true : ((n == "\u001B\u0015\u0011\u000E\u000DI\u001C\u0011\u0017\u000D") ? true : ((n == "\u001C\u0011\u0017\u000DI\u000D$\u0011\u0013\u000E\u0013") ? true : ((n == "\u0017\u0011\u0013\u000EI\u001C\u0011\u0017\u000D\u0013") ? true : ((n == "\u000E\u000D$\u000EI\u0018\u0010\u0012\u0018\u000F\u000EI\u0017\u0011\u0013\u000E") ? true : ((n == "\u000E\u000D$\u000EI\u0013\u001F\u0017\u0011\u000E") ? true : ((n == "\u000E\u000D$\u000EI\u0018\u0010\u0012\u000E\u000F\u0011\u0012\u0013") ? true : ((n == "\u000E\u000D$\u000EI\u0013\u000E\u000F\u0015\u000E\u0013I\u001B\u0011\u000E\u0014") ? true : ((n == "\u001D\u000D\u000EI\u000F\u0015\u001D\u0013") ? true : ((n == "\u001D\u000D\u000EI\u000D\u0012!") ? true : ((n == "\u0018\u0019\u0015\u0015\u000D\u0012\u000EI\u0016\u0011\u0015") ? true : ((n == "\u0015\u0019\u0012I\u001F\u0015\u0010\u0018\u000D\u0013\u0013") ? true : ((n == "\u001C\u0010\u0015\"") ? true : ((n == "\u000F\u001B\u000F\u0011\u000E") ? true : ((n == "\u001F\u000F\u0015") ? true : ((n == "\u0015\u000F\u0018\u000D") ? true : false)))))))))))))))))))))))))))))))))))))))));
    }

    public static string emit_builtin(string n, List<IRExpr> args, List<ArityEntry> arities)
    {
        return ((n == "\u0013\u0014\u0010\u001B") ? string.Concat("U2\u0018\u000DA6\u0015\u0010\u001A3\u0012\u0011\u0018\u0010\u0016\u000DJ2\u0010\u0012!\u000D\u0015\u000EA(\u0010-\u000E\u0015\u0011\u0012\u001DJ", emit_expr(args[(int)0L], arities), "KK") : ((n == "\u0012\u000D\u001D\u000F\u000E\u000D") ? string.Concat("JI", emit_expr(args[(int)0L], arities), "K") : ((n == "\u001F\u0015\u0011\u0012\u000EI\u0017\u0011\u0012\u000D") ? string.Concat("JJ6\u0019\u0012\u0018O\u0010 #\u000D\u0018\u000EPKJJK\u0002MP\u0002Z\u00022\u0010\u0012\u0013\u0010\u0017\u000DA5\u0015\u0011\u000E\u000D1\u0011\u0012\u000DJU2\u0018\u000DA(\u00103\u0012\u0011\u0018\u0010\u0016\u000DJ", emit_expr(args[(int)0L], arities), "KKF\u0002\u0015\u000D\u000E\u0019\u0015\u0012\u0002\u0012\u0019\u0017\u0017F\u0002[KKJK") : ((n == "\u000E\u000D$\u000EI\u0017\u000D\u0012\u001D\u000E\u0014") ? string.Concat("JJ\u0017\u0010\u0012\u001DK", emit_expr(args[(int)0L], arities), "A1\u000D\u0012\u001D\u000E\u0014K") : ((n == "\u0011\u0013I\u0017\u000D\u000E\u000E\u000D\u0015") ? string.Concat("J", emit_expr(args[(int)0L], arities), "\u0002PM\u0002\u0004\u00061\u0002TT\u0002", emit_expr(args[(int)0L], arities), "\u0002OM\u0002\u0009\u00071K") : ((n == "\u0011\u0013I\u0016\u0011\u001D\u0011\u000E") ? string.Concat("J", emit_expr(args[(int)0L], arities), "\u0002PM\u0002\u00061\u0002TT\u0002", emit_expr(args[(int)0L], arities), "\u0002OM\u0002\u0004\u00051K") : ((n == "\u0011\u0013I\u001B\u0014\u0011\u000E\u000D\u0013\u001F\u000F\u0018\u000D") ? string.Concat("J", emit_expr(args[(int)0L], arities), "\u0002OM\u0002\u00051K") : ((n == "\u000E\u000D$\u000EI\u000E\u0010I\u0011\u0012\u000E\u000D\u001D\u000D\u0015") ? string.Concat("\u0017\u0010\u0012\u001DA9\u000F\u0015\u0013\u000DJU2\u0018\u000DA(\u00103\u0012\u0011\u0018\u0010\u0016\u000DJ", emit_expr(args[(int)0L], arities), "KK") : ((n == "\u0011\u0012\u000E\u000D\u001D\u000D\u0015I\u000E\u0010I\u000E\u000D$\u000E") ? string.Concat("U2\u0018\u000DA6\u0015\u0010\u001A3\u0012\u0011\u0018\u0010\u0016\u000DJ", emit_expr(args[(int)0L], arities), "A(\u0010-\u000E\u0015\u0011\u0012\u001DJKK") : ((n == "\u0018\u0014\u000F\u0015I\u0018\u0010\u0016\u000D") ? emit_expr(args[(int)0L], arities) : ((n == "\u0018\u0014\u000F\u0015I\u0018\u0010\u0016\u000DI\u000F\u000E") ? string.Concat("JJ\u0017\u0010\u0012\u001DK", emit_expr(args[(int)0L], arities), "XJ\u0011\u0012\u000EK", emit_expr(args[(int)1L], arities), "YK") : ((n == "\u0018\u0010\u0016\u000DI\u000E\u0010I\u0018\u0014\u000F\u0015") ? emit_expr(args[(int)0L], arities) : ((n == "\u0018\u0014\u000F\u0015I\u000E\u0010I\u000E\u000D$\u000E") ? string.Concat("JJ\u0018\u0014\u000F\u0015K", emit_expr(args[(int)0L], arities), "KA(\u0010-\u000E\u0015\u0011\u0012\u001DJK") : ((n == "\u0017\u0011\u0013\u000EI\u0017\u000D\u0012\u001D\u000E\u0014") ? string.Concat("JJ\u0017\u0010\u0012\u001DK", emit_expr(args[(int)0L], arities), "A2\u0010\u0019\u0012\u000EK") : ((n == "\u0018\u0014\u000F\u0015I\u000F\u000E") ? string.Concat("JJ\u0017\u0010\u0012\u001DK", emit_expr(args[(int)0L], arities), "XJ\u0011\u0012\u000EK", emit_expr(args[(int)1L], arities), "YK") : ((n == "\u0013\u0019 \u0013\u000E\u0015\u0011\u0012\u001D") ? string.Concat(emit_expr(args[(int)0L], arities), "A-\u0019 \u0013\u000E\u0015\u0011\u0012\u001DJJ\u0011\u0012\u000EK", emit_expr(args[(int)1L], arities), "B\u0002J\u0011\u0012\u000EK", emit_expr(args[(int)2L], arities), "K") : ((n == "\u0017\u0011\u0013\u000EI\u000F\u000E") ? string.Concat(emit_expr(args[(int)0L], arities), "XJ\u0011\u0012\u000EK", emit_expr(args[(int)1L], arities), "Y") : ((n == "\u0017\u0011\u0013\u000EI\u0011\u0012\u0013\u000D\u0015\u000EI\u000F\u000E") ? ((Func<CodexType, string>)((elem_ty) => string.Concat("JJ6\u0019\u0012\u0018O1\u0011\u0013\u000EO", cs_type(elem_ty), "PPKJJK\u0002MP\u0002Z\u0002!\u000F\u0015\u0002U\u0017\u0002M\u0002\u0012\u000D\u001B\u00021\u0011\u0013\u000EO", cs_type(elem_ty), "PJ", emit_expr(args[(int)0L], arities), "KF\u0002U\u0017A+\u0012\u0013\u000D\u0015\u000EJJ\u0011\u0012\u000EK", emit_expr(args[(int)1L], arities), "B\u0002", emit_expr(args[(int)2L], arities), "KF\u0002\u0015\u000D\u000E\u0019\u0015\u0012\u0002U\u0017F\u0002[KKJK")))((ir_expr_type(args[(int)0L]) is ListTy _mListTy13_ ? ((Func<CodexType, CodexType>)((et) => et))((CodexType)_mListTy13_.Field0) : ((Func<CodexType, CodexType>)((_) => new ErrorTy()))(ir_expr_type(args[(int)0L])))) : ((n == "\u0017\u0011\u0013\u000EI\u0013\u0012\u0010\u0018") ? ((Func<CodexType, string>)((elem_ty) => string.Concat("JJ6\u0019\u0012\u0018O1\u0011\u0013\u000EO", cs_type(elem_ty), "PPKJJK\u0002MP\u0002Z\u0002!\u000F\u0015\u0002U\u0017\u0002M\u0002", emit_expr(args[(int)0L], arities), "F\u0002U\u0017A)\u0016\u0016J", emit_expr(args[(int)1L], arities), "KF\u0002\u0015\u000D\u000E\u0019\u0015\u0012\u0002U\u0017F\u0002[KKJK")))((ir_expr_type(args[(int)0L]) is ListTy _mListTy14_ ? ((Func<CodexType, CodexType>)((et) => et))((CodexType)_mListTy14_.Field0) : ((Func<CodexType, CodexType>)((_) => new ErrorTy()))(ir_expr_type(args[(int)0L])))) : ((n == "\u000E\u000D$\u000EI\u0018\u0010\u001A\u001F\u000F\u0015\u000D") ? string.Concat("J\u0017\u0010\u0012\u001DK\u0013\u000E\u0015\u0011\u0012\u001DA2\u0010\u001A\u001F\u000F\u0015\u000D*\u0015\u0016\u0011\u0012\u000F\u0017J", emit_expr(args[(int)0L], arities), "B\u0002", emit_expr(args[(int)1L], arities), "K") : ((n == "\u000E\u000D$\u000EI\u0015\u000D\u001F\u0017\u000F\u0018\u000D") ? string.Concat(emit_expr(args[(int)0L], arities), "A/\u000D\u001F\u0017\u000F\u0018\u000DJ", emit_expr(args[(int)1L], arities), "B\u0002", emit_expr(args[(int)2L], arities), "K") : ((n == "\u0010\u001F\u000D\u0012I\u001C\u0011\u0017\u000D") ? string.Concat("6\u0011\u0017\u000DA*\u001F\u000D\u0012/\u000D\u000F\u0016J", emit_expr(args[(int)0L], arities), "K") : ((n == "\u0015\u000D\u000F\u0016I\u000F\u0017\u0017") ? string.Concat("\u0012\u000D\u001B\u0002-\u001E\u0013\u000E\u000D\u001AA+*A-\u000E\u0015\u000D\u000F\u001A/\u000D\u000F\u0016\u000D\u0015J", emit_expr(args[(int)0L], arities), "KA/\u000D\u000F\u0016(\u0010'\u0012\u0016JK") : ((n == "\u0018\u0017\u0010\u0013\u000DI\u001C\u0011\u0017\u000D") ? string.Concat(emit_expr(args[(int)0L], arities), "A0\u0011\u0013\u001F\u0010\u0013\u000DJK") : ((n == "\u0015\u000D\u000F\u0016I\u0017\u0011\u0012\u000D") ? "U2\u0018\u000DA6\u0015\u0010\u001A3\u0012\u0011\u0018\u0010\u0016\u000DJ2\u0010\u0012\u0013\u0010\u0017\u000DA/\u000D\u000F\u00161\u0011\u0012\u000DJK\u0002DD\u0002HHK" : ((n == "\u0015\u000D\u000F\u0016I\u001C\u0011\u0017\u000D") ? string.Concat("U2\u0018\u000DA6\u0015\u0010\u001A3\u0012\u0011\u0018\u0010\u0016\u000DJ6\u0011\u0017\u000DA/\u000D\u000F\u0016)\u0017\u0017(\u000D$\u000EJU2\u0018\u000DA(\u00103\u0012\u0011\u0018\u0010\u0016\u000DJ", emit_expr(args[(int)0L], arities), "KKK") : ((n == "\u001B\u0015\u0011\u000E\u000DI\u001C\u0011\u0017\u000D") ? string.Concat("6\u0011\u0017\u000DA5\u0015\u0011\u000E\u000D)\u0017\u0017(\u000D$\u000EJU2\u0018\u000DA(\u00103\u0012\u0011\u0018\u0010\u0016\u000DJ", emit_expr(args[(int)0L], arities), "KB\u0002U2\u0018\u000DA(\u00103\u0012\u0011\u0018\u0010\u0016\u000DJ", emit_expr(args[(int)1L], arities), "KK") : ((n == "\u001C\u0011\u0017\u000DI\u000D$\u0011\u0013\u000E\u0013") ? string.Concat("6\u0011\u0017\u000DA'$\u0011\u0013\u000E\u0013JU2\u0018\u000DA(\u00103\u0012\u0011\u0018\u0010\u0016\u000DJ", emit_expr(args[(int)0L], arities), "KK") : ((n == "\u0017\u0011\u0013\u000EI\u001C\u0011\u0017\u000D\u0013") ? string.Concat("0\u0011\u0015\u000D\u0018\u000E\u0010\u0015\u001EA7\u000D\u000E6\u0011\u0017\u000D\u0013JU2\u0018\u000DA(\u00103\u0012\u0011\u0018\u0010\u0016\u000DJ", emit_expr(args[(int)0L], arities), "KB\u0002U2\u0018\u000DA(\u00103\u0012\u0011\u0018\u0010\u0016\u000DJ", emit_expr(args[(int)1L], arities), "KKA-\u000D\u0017\u000D\u0018\u000EJU2\u0018\u000DA6\u0015\u0010\u001A3\u0012\u0011\u0018\u0010\u0016\u000DKA(\u00101\u0011\u0013\u000EJK") : ((n == "\u000E\u000D$\u000EI\u0018\u0010\u0012\u0018\u000F\u000EI\u0017\u0011\u0013\u000E") ? string.Concat("\u0013\u000E\u0015\u0011\u0012\u001DA2\u0010\u0012\u0018\u000F\u000EJ", emit_expr(args[(int)0L], arities), "K") : ((n == "\u000E\u000D$\u000EI\u0013\u001F\u0017\u0011\u000E") ? string.Concat("\u0012\u000D\u001B\u00021\u0011\u0013\u000EO\u0013\u000E\u0015\u0011\u0012\u001DPJ", emit_expr(args[(int)0L], arities), "A-\u001F\u0017\u0011\u000EJ", emit_expr(args[(int)1L], arities), "KK") : ((n == "\u000E\u000D$\u000EI\u0018\u0010\u0012\u000E\u000F\u0011\u0012\u0013") ? string.Concat(emit_expr(args[(int)0L], arities), "A2\u0010\u0012\u000E\u000F\u0011\u0012\u0013J", emit_expr(args[(int)1L], arities), "K") : ((n == "\u000E\u000D$\u000EI\u0013\u000E\u000F\u0015\u000E\u0013I\u001B\u0011\u000E\u0014") ? string.Concat(emit_expr(args[(int)0L], arities), "A-\u000E\u000F\u0015\u000E\u00135\u0011\u000E\u0014J", emit_expr(args[(int)1L], arities), "K") : ((n == "\u001D\u000D\u000EI\u000F\u0015\u001D\u0013") ? "'\u0012!\u0011\u0015\u0010\u0012\u001A\u000D\u0012\u000EA7\u000D\u000E2\u0010\u001A\u001A\u000F\u0012\u00161\u0011\u0012\u000D)\u0015\u001D\u0013JKA-\u000D\u0017\u000D\u0018\u000EJU2\u0018\u000DA6\u0015\u0010\u001A3\u0012\u0011\u0018\u0010\u0016\u000DKA(\u00101\u0011\u0013\u000EJK" : ((n == "\u001D\u000D\u000EI\u000D\u0012!") ? string.Concat("U2\u0018\u000DA6\u0015\u0010\u001A3\u0012\u0011\u0018\u0010\u0016\u000DJ'\u0012!\u0011\u0015\u0010\u0012\u001A\u000D\u0012\u000EA7\u000D\u000E'\u0012!\u0011\u0015\u0010\u0012\u001A\u000D\u0012\u000E;\u000F\u0015\u0011\u000F \u0017\u000DJU2\u0018\u000DA(\u00103\u0012\u0011\u0018\u0010\u0016\u000DJ", emit_expr(args[(int)0L], arities), "KK\u0002DD\u0002HHK") : ((n == "\u0015\u0019\u0012I\u001F\u0015\u0010\u0018\u000D\u0013\u0013") ? string.Concat("U2\u0018\u000DA6\u0015\u0010\u001A3\u0012\u0011\u0018\u0010\u0016\u000DJJJ6\u0019\u0012\u0018O\u0013\u000E\u0015\u0011\u0012\u001DPKJJK\u0002MP\u0002Z\u0002!\u000F\u0015\u0002U\u001F\u0013\u0011\u0002M\u0002\u0012\u000D\u001B\u0002-\u001E\u0013\u000E\u000D\u001AA0\u0011\u000F\u001D\u0012\u0010\u0013\u000E\u0011\u0018\u0013A9\u0015\u0010\u0018\u000D\u0013\u0013-\u000E\u000F\u0015\u000E+\u0012\u001C\u0010JU2\u0018\u000DA(\u00103\u0012\u0011\u0018\u0010\u0016\u000DJ", emit_expr(args[(int)0L], arities), "KB\u0002U2\u0018\u000DA(\u00103\u0012\u0011\u0018\u0010\u0016\u000DJ", emit_expr(args[(int)1L], arities), "KK\u0002Z\u0002/\u000D\u0016\u0011\u0015\u000D\u0018\u000E-\u000E\u000F\u0012\u0016\u000F\u0015\u0016*\u0019\u000E\u001F\u0019\u000E\u0002M\u0002\u000E\u0015\u0019\u000DB\u00023\u0013\u000D-\u0014\u000D\u0017\u0017'$\u000D\u0018\u0019\u000E\u000D\u0002M\u0002\u001C\u000F\u0017\u0013\u000D\u0002[F\u0002!\u000F\u0015\u0002U\u001F\u0002M\u0002-\u001E\u0013\u000E\u000D\u001AA0\u0011\u000F\u001D\u0012\u0010\u0013\u000E\u0011\u0018\u0013A9\u0015\u0010\u0018\u000D\u0013\u0013A-\u000E\u000F\u0015\u000EJU\u001F\u0013\u0011KCF\u0002!\u000F\u0015\u0002U\u0010\u0002M\u0002U\u001FA-\u000E\u000F\u0012\u0016\u000F\u0015\u0016*\u0019\u000E\u001F\u0019\u000EA/\u000D\u000F\u0016(\u0010'\u0012\u0016JKF\u0002U\u001FA5\u000F\u0011\u000E6\u0010\u0015'$\u0011\u000EJKF\u0002\u0015\u000D\u000E\u0019\u0015\u0012\u0002U\u0010F\u0002[KKJK") : ((n == "\u0018\u0019\u0015\u0015\u000D\u0012\u000EI\u0016\u0011\u0015") ? "U2\u0018\u000DA6\u0015\u0010\u001A3\u0012\u0011\u0018\u0010\u0016\u000DJ0\u0011\u0015\u000D\u0018\u000E\u0010\u0015\u001EA7\u000D\u000E2\u0019\u0015\u0015\u000D\u0012\u000E0\u0011\u0015\u000D\u0018\u000E\u0010\u0015\u001EJKK" : ((n == "\u001C\u0010\u0015\"") ? string.Concat("(\u000F\u0013\"A/\u0019\u0012JJK\u0002MP\u0002J", emit_expr(args[(int)0L], arities), "KJ\u0012\u0019\u0017\u0017KK") : ((n == "\u000F\u001B\u000F\u0011\u000E") ? string.Concat("J", emit_expr(args[(int)0L], arities), "KA/\u000D\u0013\u0019\u0017\u000E") : ((n == "\u001F\u000F\u0015") ? string.Concat("(\u000F\u0013\"A5\u0014\u000D\u0012)\u0017\u0017J", emit_expr(args[(int)1L], arities), "A-\u000D\u0017\u000D\u0018\u000EJU$U\u0002MP\u0002(\u000F\u0013\"A/\u0019\u0012JJK\u0002MP\u0002J", emit_expr(args[(int)0L], arities), "KJU$UKKKKA/\u000D\u0013\u0019\u0017\u000EA(\u00101\u0011\u0013\u000EJK") : ((n == "\u0015\u000F\u0018\u000D") ? string.Concat("(\u000F\u0013\"A5\u0014\u000D\u0012)\u0012\u001EJ", emit_expr(args[(int)0L], arities), "A-\u000D\u0017\u000D\u0018\u000EJU\u000EU\u0002MP\u0002(\u000F\u0013\"A/\u0019\u0012JJK\u0002MP\u0002U\u000EUJ\u0012\u0019\u0017\u0017KKKKA/\u000D\u0013\u0019\u0017\u000EA/\u000D\u0013\u0019\u0017\u000E") : "")))))))))))))))))))))))))))))))))))))))));
    }

    public static string emit_apply(IRExpr e, List<ArityEntry> arities)
    {
        return ((Func<ApplyChain, string>)((chain) => ((Func<IRExpr, string>)((root) => ((Func<List<IRExpr>, string>)((args) => (root is IrName _mIrName15_ ? ((Func<CodexType, string>)((ty) => ((Func<string, string>)((n) => (is_builtin_name(n) ? emit_builtin(n, args, arities) : (((((long)n.Length) > 0L) && is_upper_letter(((long)n[(int)0L]))) ? ((Func<CodexType, string>)((result_ty) => ((Func<string, string>)((ctor_type_args) => string.Concat("\u0012\u000D\u001B\u0002", sanitize(n), ctor_type_args, "J", emit_apply_args(args, arities, 0L), "K")))(extract_ctor_type_args(result_ty))))(ir_expr_type(e)) : ((Func<long, string>)((ar) => (((ar > 1L) && (((long)args.Count) == ar)) ? string.Concat(sanitize(n), "J", emit_apply_args(args, arities, 0L), "K") : (((ar > 1L) && (((long)args.Count) < ar)) ? ((Func<long, string>)((remaining) => string.Concat(emit_partial_wrappers(0L, remaining), sanitize(n), "J", emit_apply_args(args, arities, 0L), "B\u0002", emit_partial_params(0L, remaining), "K")))((ar - ((long)args.Count))) : emit_expr_curried(e, arities)))))(lookup_arity(arities, n))))))((string)_mIrName15_.Field0)))((CodexType)_mIrName15_.Field1) : ((Func<IRExpr, string>)((_) => emit_expr_curried(e, arities)))(root))))(chain.args)))(chain.root)))(collect_apply_chain(e, new List<IRExpr>()));
    }

    public static string emit_expr_curried(IRExpr e, List<ArityEntry> arities)
    {
        return (e is IrApply _mIrApply16_ ? ((Func<CodexType, string>)((ty) => ((Func<IRExpr, string>)((a) => ((Func<IRExpr, string>)((f) => string.Concat(emit_expr(f, arities), "J", emit_expr(a, arities), "K")))((IRExpr)_mIrApply16_.Field0)))((IRExpr)_mIrApply16_.Field1)))((CodexType)_mIrApply16_.Field2) : ((Func<IRExpr, string>)((_) => emit_expr(e, arities)))(e));
    }

    public static string emit_expr(IRExpr e, List<ArityEntry> arities)
    {
        return ((Func<IRExpr, string>)((_scrutinee17_) => (_scrutinee17_ is IrIntLit _mIrIntLit17_ ? ((Func<long, string>)((n) => _Cce.FromUnicode(n.ToString())))((long)_mIrIntLit17_.Field0) : (_scrutinee17_ is IrNumLit _mIrNumLit17_ ? ((Func<long, string>)((n) => _Cce.FromUnicode(n.ToString())))((long)_mIrNumLit17_.Field0) : (_scrutinee17_ is IrTextLit _mIrTextLit17_ ? ((Func<string, string>)((s) => string.Concat("H", escape_text(s), "H")))((string)_mIrTextLit17_.Field0) : (_scrutinee17_ is IrBoolLit _mIrBoolLit17_ ? ((Func<bool, string>)((b) => (b ? "\u000E\u0015\u0019\u000D" : "\u001C\u000F\u0017\u0013\u000D")))((bool)_mIrBoolLit17_.Field0) : (_scrutinee17_ is IrCharLit _mIrCharLit17_ ? ((Func<long, string>)((n) => _Cce.FromUnicode(n.ToString())))((long)_mIrCharLit17_.Field0) : (_scrutinee17_ is IrName _mIrName17_ ? ((Func<CodexType, string>)((ty) => ((Func<string, string>)((n) => ((n == "\u0015\u000D\u000F\u0016I\u0017\u0011\u0012\u000D") ? "U2\u0018\u000DA6\u0015\u0010\u001A3\u0012\u0011\u0018\u0010\u0016\u000DJ2\u0010\u0012\u0013\u0010\u0017\u000DA/\u000D\u000F\u00161\u0011\u0012\u000DJK\u0002DD\u0002HHK" : ((n == "\u001D\u000D\u000EI\u000F\u0015\u001D\u0013") ? "'\u0012!\u0011\u0015\u0010\u0012\u001A\u000D\u0012\u000EA7\u000D\u000E2\u0010\u001A\u001A\u000F\u0012\u00161\u0011\u0012\u000D)\u0015\u001D\u0013JKA-\u000D\u0017\u000D\u0018\u000EJU2\u0018\u000DA6\u0015\u0010\u001A3\u0012\u0011\u0018\u0010\u0016\u000DKA(\u00101\u0011\u0013\u000EJK" : ((n == "\u0018\u0019\u0015\u0015\u000D\u0012\u000EI\u0016\u0011\u0015") ? "U2\u0018\u000DA6\u0015\u0010\u001A3\u0012\u0011\u0018\u0010\u0016\u000DJ0\u0011\u0015\u000D\u0018\u000E\u0010\u0015\u001EA7\u000D\u000E2\u0019\u0015\u0015\u000D\u0012\u000E0\u0011\u0015\u000D\u0018\u000E\u0010\u0015\u001EJKK" : (((((long)n.Length) > 0L) && is_upper_letter(((long)n[(int)0L]))) ? string.Concat("\u0012\u000D\u001B\u0002", sanitize(n), "JK") : ((lookup_arity(arities, n) == 0L) ? string.Concat(sanitize(n), "JK") : ((Func<long, string>)((ar) => ((ar >= 2L) ? string.Concat(emit_partial_wrappers(0L, ar), sanitize(n), "J", emit_partial_params(0L, ar), "K") : sanitize(n))))(lookup_arity(arities, n)))))))))((string)_mIrName17_.Field0)))((CodexType)_mIrName17_.Field1) : (_scrutinee17_ is IrBinary _mIrBinary17_ ? ((Func<CodexType, string>)((ty) => ((Func<IRExpr, string>)((r) => ((Func<IRExpr, string>)((l) => ((Func<IRBinaryOp, string>)((op) => emit_binary(op, l, r, ty, arities)))((IRBinaryOp)_mIrBinary17_.Field0)))((IRExpr)_mIrBinary17_.Field1)))((IRExpr)_mIrBinary17_.Field2)))((CodexType)_mIrBinary17_.Field3) : (_scrutinee17_ is IrNegate _mIrNegate17_ ? ((Func<IRExpr, string>)((operand) => string.Concat("JI", emit_expr(operand, arities), "K")))((IRExpr)_mIrNegate17_.Field0) : (_scrutinee17_ is IrIf _mIrIf17_ ? ((Func<CodexType, string>)((ty) => ((Func<IRExpr, string>)((el) => ((Func<IRExpr, string>)((t) => ((Func<IRExpr, string>)((c) => string.Concat("J", emit_expr(c, arities), "\u0002D\u0002", emit_expr(t, arities), "\u0002E\u0002", emit_expr(el, arities), "K")))((IRExpr)_mIrIf17_.Field0)))((IRExpr)_mIrIf17_.Field1)))((IRExpr)_mIrIf17_.Field2)))((CodexType)_mIrIf17_.Field3) : (_scrutinee17_ is IrLet _mIrLet17_ ? ((Func<IRExpr, string>)((body) => ((Func<IRExpr, string>)((val) => ((Func<CodexType, string>)((ty) => ((Func<string, string>)((name) => emit_let(name, ty, val, body, arities)))((string)_mIrLet17_.Field0)))((CodexType)_mIrLet17_.Field1)))((IRExpr)_mIrLet17_.Field2)))((IRExpr)_mIrLet17_.Field3) : (_scrutinee17_ is IrApply _mIrApply17_ ? ((Func<CodexType, string>)((ty) => ((Func<IRExpr, string>)((a) => ((Func<IRExpr, string>)((f) => emit_apply(e, arities)))((IRExpr)_mIrApply17_.Field0)))((IRExpr)_mIrApply17_.Field1)))((CodexType)_mIrApply17_.Field2) : (_scrutinee17_ is IrLambda _mIrLambda17_ ? ((Func<CodexType, string>)((ty) => ((Func<IRExpr, string>)((body) => ((Func<List<IRParam>, string>)((@params) => emit_lambda(@params, body, arities)))((List<IRParam>)_mIrLambda17_.Field0)))((IRExpr)_mIrLambda17_.Field1)))((CodexType)_mIrLambda17_.Field2) : (_scrutinee17_ is IrList _mIrList17_ ? ((Func<CodexType, string>)((ty) => ((Func<List<IRExpr>, string>)((elems) => emit_list(elems, ty, arities)))((List<IRExpr>)_mIrList17_.Field0)))((CodexType)_mIrList17_.Field1) : (_scrutinee17_ is IrMatch _mIrMatch17_ ? ((Func<CodexType, string>)((ty) => ((Func<List<IRBranch>, string>)((branches) => ((Func<IRExpr, string>)((scrut) => emit_match(scrut, branches, ty, arities)))((IRExpr)_mIrMatch17_.Field0)))((List<IRBranch>)_mIrMatch17_.Field1)))((CodexType)_mIrMatch17_.Field2) : (_scrutinee17_ is IrDo _mIrDo17_ ? ((Func<CodexType, string>)((ty) => ((Func<List<IRDoStmt>, string>)((stmts) => emit_do(stmts, ty, arities)))((List<IRDoStmt>)_mIrDo17_.Field0)))((CodexType)_mIrDo17_.Field1) : (_scrutinee17_ is IrHandle _mIrHandle17_ ? ((Func<CodexType, string>)((ty) => ((Func<List<IRHandleClause>, string>)((clauses) => ((Func<IRExpr, string>)((body) => ((Func<string, string>)((eff) => emit_handle(eff, body, clauses, ty, arities)))((string)_mIrHandle17_.Field0)))((IRExpr)_mIrHandle17_.Field1)))((List<IRHandleClause>)_mIrHandle17_.Field2)))((CodexType)_mIrHandle17_.Field3) : (_scrutinee17_ is IrRecord _mIrRecord17_ ? ((Func<CodexType, string>)((ty) => ((Func<List<IRFieldVal>, string>)((fields) => ((Func<string, string>)((name) => emit_record(name, fields, arities)))((string)_mIrRecord17_.Field0)))((List<IRFieldVal>)_mIrRecord17_.Field1)))((CodexType)_mIrRecord17_.Field2) : (_scrutinee17_ is IrFieldAccess _mIrFieldAccess17_ ? ((Func<CodexType, string>)((ty) => ((Func<string, string>)((field) => ((Func<IRExpr, string>)((rec) => string.Concat(emit_expr(rec, arities), "A", sanitize(field))))((IRExpr)_mIrFieldAccess17_.Field0)))((string)_mIrFieldAccess17_.Field1)))((CodexType)_mIrFieldAccess17_.Field2) : (_scrutinee17_ is IrFork _mIrFork17_ ? ((Func<CodexType, string>)((ty) => ((Func<IRExpr, string>)((body) => string.Concat("(\u000F\u0013\"A/\u0019\u0012JJK\u0002MP\u0002J", emit_expr(body, arities), "KJ\u0012\u0019\u0017\u0017KK")))((IRExpr)_mIrFork17_.Field0)))((CodexType)_mIrFork17_.Field1) : (_scrutinee17_ is IrAwait _mIrAwait17_ ? ((Func<CodexType, string>)((ty) => ((Func<IRExpr, string>)((task) => string.Concat("J", emit_expr(task, arities), "KA/\u000D\u0013\u0019\u0017\u000E")))((IRExpr)_mIrAwait17_.Field0)))((CodexType)_mIrAwait17_.Field1) : (_scrutinee17_ is IrError _mIrError17_ ? ((Func<CodexType, string>)((ty) => ((Func<string, string>)((msg) => string.Concat("QN\u0002\u000D\u0015\u0015\u0010\u0015E\u0002", msg, "\u0002NQ\u0002\u0016\u000D\u001C\u000F\u0019\u0017\u000E")))((string)_mIrError17_.Field0)))((CodexType)_mIrError17_.Field1) : throw new InvalidOperationException("Non-exhaustive match"))))))))))))))))))))))))(e);
    }

    public static string hex_digit(long n)
    {
        return ((n == 0L) ? "\u0003" : ((n == 1L) ? "\u0004" : ((n == 2L) ? "\u0005" : ((n == 3L) ? "\u0006" : ((n == 4L) ? "\u0007" : ((n == 5L) ? "\u0008" : ((n == 6L) ? "\u0009" : ((n == 7L) ? "\u000A" : ((n == 8L) ? "\u000B" : ((n == 9L) ? "\u000C" : ((n == 10L) ? ")" : ((n == 11L) ? ":" : ((n == 12L) ? "2" : ((n == 13L) ? "0" : ((n == 14L) ? "'" : ((n == 15L) ? "6" : "D"))))))))))))))));
    }

    public static string hex4(long n)
    {
        return string.Concat("\u0003\u0003", hex_digit((n / 16L)), hex_digit((n - ((n / 16L) * 16L))));
    }

    public static string escape_cce_char(long c)
    {
        return ((c == 92L) ? "VV" : ((c == 34L) ? "VH" : ((c >= 32L) ? ((c < 127L) ? ((char)c).ToString() : string.Concat("V\u0019", hex4(c))) : string.Concat("V\u0019", hex4(c)))));
    }

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

    public static string escape_text(string s)
    {
        return string.Concat(escape_text_loop(s, 0L, ((long)s.Length), new List<string>()));
    }

    public static string emit_bin_op(IRBinaryOp op)
    {
        return ((Func<IRBinaryOp, string>)((_scrutinee18_) => (_scrutinee18_ is IrAddInt _mIrAddInt18_ ? "L" : (_scrutinee18_ is IrSubInt _mIrSubInt18_ ? "I" : (_scrutinee18_ is IrMulInt _mIrMulInt18_ ? "N" : (_scrutinee18_ is IrDivInt _mIrDivInt18_ ? "Q" : (_scrutinee18_ is IrPowInt _mIrPowInt18_ ? "\u00E0\u0081\u009E" : (_scrutinee18_ is IrAddNum _mIrAddNum18_ ? "L" : (_scrutinee18_ is IrSubNum _mIrSubNum18_ ? "I" : (_scrutinee18_ is IrMulNum _mIrMulNum18_ ? "N" : (_scrutinee18_ is IrDivNum _mIrDivNum18_ ? "Q" : (_scrutinee18_ is IrEq _mIrEq18_ ? "MM" : (_scrutinee18_ is IrNotEq _mIrNotEq18_ ? "CM" : (_scrutinee18_ is IrLt _mIrLt18_ ? "O" : (_scrutinee18_ is IrGt _mIrGt18_ ? "P" : (_scrutinee18_ is IrLtEq _mIrLtEq18_ ? "OM" : (_scrutinee18_ is IrGtEq _mIrGtEq18_ ? "PM" : (_scrutinee18_ is IrAnd _mIrAnd18_ ? "TT" : (_scrutinee18_ is IrOr _mIrOr18_ ? "WW" : (_scrutinee18_ is IrAppendText _mIrAppendText18_ ? "L" : (_scrutinee18_ is IrAppendList _mIrAppendList18_ ? "L" : (_scrutinee18_ is IrConsList _mIrConsList18_ ? "L" : throw new InvalidOperationException("Non-exhaustive match")))))))))))))))))))))))(op);
    }

    public static string emit_binary(IRBinaryOp op, IRExpr l, IRExpr r, CodexType ty, List<ArityEntry> arities)
    {
        return ((Func<IRBinaryOp, string>)((_scrutinee19_) => (_scrutinee19_ is IrAppendList _mIrAppendList19_ ? string.Concat("'\u0012\u0019\u001A\u000D\u0015\u000F \u0017\u000DA2\u0010\u0012\u0018\u000F\u000EJ", emit_expr(l, arities), "B\u0002", emit_expr(r, arities), "KA(\u00101\u0011\u0013\u000EJK") : (_scrutinee19_ is IrConsList _mIrConsList19_ ? string.Concat("\u0012\u000D\u001B\u00021\u0011\u0013\u000EO", cs_type(ir_expr_type(l)), "P\u0002Z\u0002", emit_expr(l, arities), "\u0002[A2\u0010\u0012\u0018\u000F\u000EJ", emit_expr(r, arities), "KA(\u00101\u0011\u0013\u000EJK") : ((Func<IRBinaryOp, string>)((_) => string.Concat("J", emit_expr(l, arities), "\u0002", emit_bin_op(op), "\u0002", emit_expr(r, arities), "K")))(_scrutinee19_)))))(op);
    }

    public static string emit_let(string name, CodexType ty, IRExpr val, IRExpr body, List<ArityEntry> arities)
    {
        return string.Concat("JJ6\u0019\u0012\u0018O", cs_type(ty), "B\u0002", cs_type(ir_expr_type(body)), "PKJJ", sanitize(name), "K\u0002MP\u0002", emit_expr(body, arities), "KKJ", emit_expr(val, arities), "K");
    }

    public static string emit_lambda(List<IRParam> @params, IRExpr body, List<ArityEntry> arities)
    {
        return ((((long)@params.Count) == 0L) ? string.Concat("JJK\u0002MP\u0002", emit_expr(body, arities), "K") : ((((long)@params.Count) == 1L) ? ((Func<IRParam, string>)((p) => string.Concat("JJ", cs_type(p.type_val), "\u0002", sanitize(p.name), "K\u0002MP\u0002", emit_expr(body, arities), "K")))(@params[(int)0L]) : string.Concat("JJK\u0002MP\u0002", emit_expr(body, arities), "K")));
    }

    public static string emit_list(List<IRExpr> elems, CodexType ty, List<ArityEntry> arities)
    {
        return ((((long)elems.Count) == 0L) ? string.Concat("\u0012\u000D\u001B\u00021\u0011\u0013\u000EO", cs_type(ty), "PJK") : string.Concat("\u0012\u000D\u001B\u00021\u0011\u0013\u000EO", cs_type(ty), "P\u0002Z\u0002", emit_list_elems(elems, 0L, arities), "\u0002["));
    }

    public static string emit_list_elems(List<IRExpr> elems, long i, List<ArityEntry> arities)
    {
        return ((i == ((long)elems.Count)) ? "" : ((i == (((long)elems.Count) - 1L)) ? emit_expr(elems[(int)i], arities) : string.Concat(emit_expr(elems[(int)i], arities), "B\u0002", emit_list_elems(elems, (i + 1L), arities))));
    }

    public static string emit_match(IRExpr scrut, List<IRBranch> branches, CodexType ty, List<ArityEntry> arities)
    {
        return ((Func<string, string>)((arms) => ((Func<bool, string>)((needs_wild) => string.Concat(emit_expr(scrut, arities), "\u0002\u0013\u001B\u0011\u000E\u0018\u0014\u0002Z\u0002", arms, (needs_wild ? "U\u0002MP\u0002\u000E\u0014\u0015\u0010\u001B\u0002\u0012\u000D\u001B\u0002+\u0012!\u000F\u0017\u0011\u0016*\u001F\u000D\u0015\u000F\u000E\u0011\u0010\u0012'$\u0018\u000D\u001F\u000E\u0011\u0010\u0012JH,\u0010\u0012I\u000D$\u0014\u000F\u0019\u0013\u000E\u0011!\u000D\u0002\u001A\u000F\u000E\u0018\u0014HKB\u0002" : ""), "[")))((has_any_catch_all(branches, 0L) ? false : true))))(emit_match_arms(branches, 0L, arities));
    }

    public static string emit_match_arms(List<IRBranch> branches, long i, List<ArityEntry> arities)
    {
        return ((i == ((long)branches.Count)) ? "" : ((Func<IRBranch, string>)((arm) => ((Func<string, string>)((this_arm) => (is_catch_all(arm.pattern) ? this_arm : string.Concat(this_arm, emit_match_arms(branches, (i + 1L), arities)))))(string.Concat(emit_pattern(arm.pattern), "\u0002MP\u0002", emit_expr(arm.body, arities), "B\u0002"))))(branches[(int)i]));
    }

    public static bool is_catch_all(IRPat p)
    {
        return ((Func<IRPat, bool>)((_scrutinee20_) => (_scrutinee20_ is IrWildPat _mIrWildPat20_ ? true : (_scrutinee20_ is IrVarPat _mIrVarPat20_ ? ((Func<CodexType, bool>)((ty) => ((Func<string, bool>)((name) => true))((string)_mIrVarPat20_.Field0)))((CodexType)_mIrVarPat20_.Field1) : ((Func<IRPat, bool>)((_) => false))(_scrutinee20_)))))(p);
    }

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

    public static string emit_pattern(IRPat p)
    {
        return ((Func<IRPat, string>)((_scrutinee21_) => (_scrutinee21_ is IrVarPat _mIrVarPat21_ ? ((Func<CodexType, string>)((ty) => ((Func<string, string>)((name) => string.Concat(cs_type(ty), "\u0002", sanitize(name))))((string)_mIrVarPat21_.Field0)))((CodexType)_mIrVarPat21_.Field1) : (_scrutinee21_ is IrLitPat _mIrLitPat21_ ? ((Func<CodexType, string>)((ty) => ((Func<string, string>)((text) => text))((string)_mIrLitPat21_.Field0)))((CodexType)_mIrLitPat21_.Field1) : (_scrutinee21_ is IrCtorPat _mIrCtorPat21_ ? ((Func<CodexType, string>)((ty) => ((Func<List<IRPat>, string>)((subs) => ((Func<string, string>)((name) => ((((long)subs.Count) == 0L) ? string.Concat(sanitize(name), "\u0002Z\u0002[") : string.Concat(sanitize(name), "J", emit_sub_patterns(subs, 0L), "K"))))((string)_mIrCtorPat21_.Field0)))((List<IRPat>)_mIrCtorPat21_.Field1)))((CodexType)_mIrCtorPat21_.Field2) : (_scrutinee21_ is IrWildPat _mIrWildPat21_ ? "U" : throw new InvalidOperationException("Non-exhaustive match")))))))(p);
    }

    public static string emit_sub_patterns(List<IRPat> subs, long i)
    {
        return ((i == ((long)subs.Count)) ? "" : ((Func<IRPat, string>)((sub) => string.Concat(emit_sub_pattern(sub), ((i < (((long)subs.Count) - 1L)) ? "B\u0002" : ""), emit_sub_patterns(subs, (i + 1L)))))(subs[(int)i]));
    }

    public static string emit_sub_pattern(IRPat p)
    {
        return ((Func<IRPat, string>)((_scrutinee22_) => (_scrutinee22_ is IrVarPat _mIrVarPat22_ ? ((Func<CodexType, string>)((ty) => ((Func<string, string>)((name) => string.Concat("!\u000F\u0015\u0002", sanitize(name))))((string)_mIrVarPat22_.Field0)))((CodexType)_mIrVarPat22_.Field1) : (_scrutinee22_ is IrCtorPat _mIrCtorPat22_ ? ((Func<CodexType, string>)((ty) => ((Func<List<IRPat>, string>)((subs) => ((Func<string, string>)((name) => emit_pattern(p)))((string)_mIrCtorPat22_.Field0)))((List<IRPat>)_mIrCtorPat22_.Field1)))((CodexType)_mIrCtorPat22_.Field2) : (_scrutinee22_ is IrWildPat _mIrWildPat22_ ? "U" : (_scrutinee22_ is IrLitPat _mIrLitPat22_ ? ((Func<CodexType, string>)((ty) => ((Func<string, string>)((text) => text))((string)_mIrLitPat22_.Field0)))((CodexType)_mIrLitPat22_.Field1) : throw new InvalidOperationException("Non-exhaustive match")))))))(p);
    }

    public static string emit_do(List<IRDoStmt> stmts, CodexType ty, List<ArityEntry> arities)
    {
        return ((Func<string, string>)((ret_type) => ((Func<long, string>)((len) => ((Func<CodexType, string>)((_scrutinee23_) => (_scrutinee23_ is VoidTy _mVoidTy23_ ? string.Concat("JJ6\u0019\u0012\u0018O\u0010 #\u000D\u0018\u000EPKJJK\u0002MP\u0002Z\u0002", emit_do_stmts(stmts, 0L, len, false, arities), "\u0002\u0015\u000D\u000E\u0019\u0015\u0012\u0002\u0012\u0019\u0017\u0017F\u0002[KKJK") : (_scrutinee23_ is NothingTy _mNothingTy23_ ? string.Concat("JJ6\u0019\u0012\u0018O\u0010 #\u000D\u0018\u000EPKJJK\u0002MP\u0002Z\u0002", emit_do_stmts(stmts, 0L, len, false, arities), "\u0002\u0015\u000D\u000E\u0019\u0015\u0012\u0002\u0012\u0019\u0017\u0017F\u0002[KKJK") : (_scrutinee23_ is ErrorTy _mErrorTy23_ ? string.Concat("JJ6\u0019\u0012\u0018O\u0010 #\u000D\u0018\u000EPKJJK\u0002MP\u0002Z\u0002", emit_do_stmts(stmts, 0L, len, false, arities), "\u0002\u0015\u000D\u000E\u0019\u0015\u0012\u0002\u0012\u0019\u0017\u0017F\u0002[KKJK") : ((Func<CodexType, string>)((_) => ((len == 0L) ? string.Concat("JJ6\u0019\u0012\u0018O", ret_type, "PKJJK\u0002MP\u0002Z\u0002\u0015\u000D\u000E\u0019\u0015\u0012\u0002\u0012\u0019\u0017\u0017F\u0002[KKJK") : string.Concat("JJ6\u0019\u0012\u0018O", ret_type, "PKJJK\u0002MP\u0002Z\u0002", emit_do_stmts(stmts, 0L, len, true, arities), "\u0002[KKJK"))))(_scrutinee23_))))))(ty)))(((long)stmts.Count))))(cs_type(ty));
    }

    public static string emit_do_stmts(List<IRDoStmt> stmts, long i, long len, bool needs_return, List<ArityEntry> arities)
    {
        return ((i == len) ? "" : ((Func<IRDoStmt, string>)((s) => ((Func<bool, string>)((is_last) => ((Func<bool, string>)((use_return) => string.Concat(emit_do_stmt(s, use_return, arities), "\u0002", emit_do_stmts(stmts, (i + 1L), len, needs_return, arities))))((is_last ? needs_return : false))))((i == (len - 1L)))))(stmts[(int)i]));
    }

    public static string emit_do_stmt(IRDoStmt s, bool use_return, List<ArityEntry> arities)
    {
        return ((Func<IRDoStmt, string>)((_scrutinee24_) => (_scrutinee24_ is IrDoBind _mIrDoBind24_ ? ((Func<IRExpr, string>)((val) => ((Func<CodexType, string>)((ty) => ((Func<string, string>)((name) => string.Concat("!\u000F\u0015\u0002", sanitize(name), "\u0002M\u0002", emit_expr(val, arities), "F")))((string)_mIrDoBind24_.Field0)))((CodexType)_mIrDoBind24_.Field1)))((IRExpr)_mIrDoBind24_.Field2) : (_scrutinee24_ is IrDoExec _mIrDoExec24_ ? ((Func<IRExpr, string>)((e) => (use_return ? string.Concat("\u0015\u000D\u000E\u0019\u0015\u0012\u0002", emit_expr(e, arities), "F") : string.Concat(emit_expr(e, arities), "F"))))((IRExpr)_mIrDoExec24_.Field0) : throw new InvalidOperationException("Non-exhaustive match")))))(s);
    }

    public static string emit_record(string name, List<IRFieldVal> fields, List<ArityEntry> arities)
    {
        return string.Concat("\u0012\u000D\u001B\u0002", sanitize(name), "J", emit_record_fields(fields, 0L, arities), "K");
    }

    public static string emit_record_fields(List<IRFieldVal> fields, long i, List<ArityEntry> arities)
    {
        return ((i == ((long)fields.Count)) ? "" : ((Func<IRFieldVal, string>)((f) => string.Concat(sanitize(f.name), "E\u0002", emit_expr(f.value, arities), ((i < (((long)fields.Count) - 1L)) ? "B\u0002" : ""), emit_record_fields(fields, (i + 1L), arities))))(fields[(int)i]));
    }

    public static string emit_handle(string eff, IRExpr body, List<IRHandleClause> clauses, CodexType ty, List<ArityEntry> arities)
    {
        return ((Func<string, string>)((ret_type) => string.Concat("JJ6\u0019\u0012\u0018O", ret_type, "PKJJK\u0002MP\u0002Z\u0002", emit_handle_clauses(clauses, ret_type, arities), "\u0015\u000D\u000E\u0019\u0015\u0012\u0002", emit_expr(body, arities), "F\u0002[KKJK")))(cs_type(ty));
    }

    public static string emit_handle_clauses(List<IRHandleClause> clauses, string ret_type, List<ArityEntry> arities)
    {
        return emit_handle_clauses_loop(clauses, 0L, ret_type, arities);
    }

    public static string emit_handle_clauses_loop(List<IRHandleClause> clauses, long i, string ret_type, List<ArityEntry> arities)
    {
        return ((i == ((long)clauses.Count)) ? "" : ((Func<IRHandleClause, string>)((c) => string.Concat("6\u0019\u0012\u0018O6\u0019\u0012\u0018O", ret_type, "B\u0002", ret_type, "PB\u0002", ret_type, "P\u0002U\u0014\u000F\u0012\u0016\u0017\u000DU", sanitize(c.op_name), "U\u0002M\u0002J", sanitize(c.resume_name), "K\u0002MP\u0002Z\u0002\u0015\u000D\u000E\u0019\u0015\u0012\u0002", emit_expr(c.body, arities), "F\u0002[F\u0002", emit_handle_clauses_loop(clauses, (i + 1L), ret_type, arities))))(clauses[(int)i]));
    }

    public static string codex_emit_type_defs(List<ATypeDef> tds, long i)
    {
        return ((i == ((long)tds.Count)) ? "" : string.Concat(codex_emit_type_def(tds[(int)i]), "\u0001", codex_emit_type_defs(tds, (i + 1L))));
    }

    public static string codex_emit_type_def(ATypeDef td)
    {
        return ((Func<ATypeDef, string>)((_scrutinee25_) => (_scrutinee25_ is ARecordTypeDef _mARecordTypeDef25_ ? ((Func<List<ARecordFieldDef>, string>)((fields) => ((Func<List<Name>, string>)((tparams) => ((Func<Name, string>)((name) => string.Concat(name.value, "\u0002M\u0002\u0015\u000D\u0018\u0010\u0015\u0016\u0002Z", codex_emit_record_field_defs(fields, 0L), "\u0001[\u0001")))((Name)_mARecordTypeDef25_.Field0)))((List<Name>)_mARecordTypeDef25_.Field1)))((List<ARecordFieldDef>)_mARecordTypeDef25_.Field2) : (_scrutinee25_ is AVariantTypeDef _mAVariantTypeDef25_ ? ((Func<List<AVariantCtorDef>, string>)((ctors) => ((Func<List<Name>, string>)((tparams) => ((Func<Name, string>)((name) => string.Concat(name.value, "\u0002M\u0001", codex_emit_variant_ctors(ctors, 0L))))((Name)_mAVariantTypeDef25_.Field0)))((List<Name>)_mAVariantTypeDef25_.Field1)))((List<AVariantCtorDef>)_mAVariantTypeDef25_.Field2) : throw new InvalidOperationException("Non-exhaustive match")))))(td);
    }

    public static string codex_emit_record_field_defs(List<ARecordFieldDef> fields, long i)
    {
        return ((i == ((long)fields.Count)) ? "" : ((Func<ARecordFieldDef, string>)((f) => ((Func<string, string>)((comma) => string.Concat(comma, "\u0001\u0002\u0002", f.name.value, "\u0002E\u0002", codex_emit_type_expr(f.type_expr), codex_emit_record_field_defs(fields, (i + 1L)))))(((i > 0L) ? "B" : ""))))(fields[(int)i]));
    }

    public static string codex_emit_variant_ctors(List<AVariantCtorDef> ctors, long i)
    {
        return ((i == ((long)ctors.Count)) ? "" : ((Func<AVariantCtorDef, string>)((c) => string.Concat("\u0002\u0002W\u0002", c.name.value, codex_emit_ctor_fields(c.fields, 0L), "\u0001", codex_emit_variant_ctors(ctors, (i + 1L)))))(ctors[(int)i]));
    }

    public static string codex_emit_ctor_fields(List<ATypeExpr> fields, long i)
    {
        return ((i == ((long)fields.Count)) ? "" : string.Concat("\u0002J", codex_emit_type_expr(fields[(int)i]), "K", codex_emit_ctor_fields(fields, (i + 1L))));
    }

    public static string codex_emit_type_expr(ATypeExpr te)
    {
        return ((Func<ATypeExpr, string>)((_scrutinee26_) => (_scrutinee26_ is ANamedType _mANamedType26_ ? ((Func<Name, string>)((name) => name.value))((Name)_mANamedType26_.Field0) : (_scrutinee26_ is AFunType _mAFunType26_ ? ((Func<ATypeExpr, string>)((r) => ((Func<ATypeExpr, string>)((p) => string.Concat("J", codex_emit_type_expr(p), "\u0002IP\u0002", codex_emit_type_expr(r), "K")))((ATypeExpr)_mAFunType26_.Field0)))((ATypeExpr)_mAFunType26_.Field1) : (_scrutinee26_ is AAppType _mAAppType26_ ? ((Func<List<ATypeExpr>, string>)((args) => ((Func<ATypeExpr, string>)((@base) => string.Concat(codex_emit_type_expr(@base), "\u0002", codex_emit_type_expr_args(args, 0L))))((ATypeExpr)_mAAppType26_.Field0)))((List<ATypeExpr>)_mAAppType26_.Field1) : (_scrutinee26_ is AEffectType _mAEffectType26_ ? ((Func<ATypeExpr, string>)((ret) => ((Func<List<Name>, string>)((effs) => string.Concat("X", codex_emit_effect_names(effs, 0L), "Y\u0002", codex_emit_type_expr(ret))))((List<Name>)_mAEffectType26_.Field0)))((ATypeExpr)_mAEffectType26_.Field1) : throw new InvalidOperationException("Non-exhaustive match")))))))(te);
    }

    public static string codex_emit_type_expr_args(List<ATypeExpr> args, long i)
    {
        return ((i == ((long)args.Count)) ? "" : ((Func<string, string>)((sep) => string.Concat(sep, codex_emit_type_expr_wrapped(args[(int)i]), codex_emit_type_expr_args(args, (i + 1L)))))(((i > 0L) ? "\u0002" : "")));
    }

    public static string codex_emit_type_expr_wrapped(ATypeExpr te)
    {
        return ((Func<ATypeExpr, string>)((_scrutinee27_) => (_scrutinee27_ is AFunType _mAFunType27_ ? ((Func<ATypeExpr, string>)((r) => ((Func<ATypeExpr, string>)((p) => string.Concat("J", codex_emit_type_expr(te), "K")))((ATypeExpr)_mAFunType27_.Field0)))((ATypeExpr)_mAFunType27_.Field1) : (_scrutinee27_ is AAppType _mAAppType27_ ? ((Func<List<ATypeExpr>, string>)((args) => ((Func<ATypeExpr, string>)((@base) => string.Concat("J", codex_emit_type_expr(te), "K")))((ATypeExpr)_mAAppType27_.Field0)))((List<ATypeExpr>)_mAAppType27_.Field1) : ((Func<ATypeExpr, string>)((_) => codex_emit_type_expr(te)))(_scrutinee27_)))))(te);
    }

    public static string codex_emit_effect_names(List<Name> effs, long i)
    {
        return ((i == ((long)effs.Count)) ? "" : ((Func<string, string>)((sep) => string.Concat(sep, effs[(int)i].value, codex_emit_effect_names(effs, (i + 1L)))))(((i > 0L) ? "B\u0002" : "")));
    }

    public static string codex_emit_codex_type(CodexType ty)
    {
        while (true)
        {
            var _tco_s = ty;
            if (_tco_s is IntegerTy _tco_m0)
            {
                return "+\u0012\u000E\u000D\u001D\u000D\u0015";
            }
            else if (_tco_s is NumberTy _tco_m1)
            {
                return ",\u0019\u001A \u000D\u0015";
            }
            else if (_tco_s is TextTy _tco_m2)
            {
                return "(\u000D$\u000E";
            }
            else if (_tco_s is BooleanTy _tco_m3)
            {
                return ":\u0010\u0010\u0017\u000D\u000F\u0012";
            }
            else if (_tco_s is CharTy _tco_m4)
            {
                return "2\u0014\u000F\u0015";
            }
            else if (_tco_s is VoidTy _tco_m5)
            {
                return ",\u0010\u000E\u0014\u0011\u0012\u001D";
            }
            else if (_tco_s is NothingTy _tco_m6)
            {
                return ",\u0010\u000E\u0014\u0011\u0012\u001D";
            }
            else if (_tco_s is ErrorTy _tco_m7)
            {
                return "3\u0012\"\u0012\u0010\u001B\u0012";
            }
            else if (_tco_s is FunTy _tco_m8)
            {
                var p = _tco_m8.Field0;
                var r = _tco_m8.Field1;
                return string.Concat(codex_wrap_fun_param(p), "\u0002IP\u0002", codex_emit_codex_type(r));
            }
            else if (_tco_s is ListTy _tco_m9)
            {
                var elem = _tco_m9.Field0;
                return string.Concat("1\u0011\u0013\u000E\u0002", codex_wrap_complex(elem));
            }
            else if (_tco_s is TypeVar _tco_m10)
            {
                var id = _tco_m10.Field0;
                return string.Concat("\u000F", _Cce.FromUnicode(id.ToString()));
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
                return string.Concat("X", codex_emit_codex_type_effect_names(effs, 0L), "Y\u0002", codex_emit_codex_type(ret));
            }
        }
    }

    public static string codex_wrap_fun_param(CodexType ty)
    {
        return (ty is FunTy _mFunTy28_ ? ((Func<CodexType, string>)((r) => ((Func<CodexType, string>)((p) => string.Concat("J", codex_emit_codex_type(ty), "K")))((CodexType)_mFunTy28_.Field0)))((CodexType)_mFunTy28_.Field1) : ((Func<CodexType, string>)((_) => codex_emit_codex_type(ty)))(ty));
    }

    public static string codex_wrap_complex(CodexType ty)
    {
        return ((Func<CodexType, string>)((_scrutinee29_) => (_scrutinee29_ is FunTy _mFunTy29_ ? ((Func<CodexType, string>)((r) => ((Func<CodexType, string>)((p) => string.Concat("J", codex_emit_codex_type(ty), "K")))((CodexType)_mFunTy29_.Field0)))((CodexType)_mFunTy29_.Field1) : (_scrutinee29_ is ListTy _mListTy29_ ? ((Func<CodexType, string>)((elem) => string.Concat("J", codex_emit_codex_type(ty), "K")))((CodexType)_mListTy29_.Field0) : ((Func<CodexType, string>)((_) => codex_emit_codex_type(ty)))(_scrutinee29_)))))(ty);
    }

    public static string codex_emit_codex_type_effect_names(List<Name> effs, long i)
    {
        return ((i == ((long)effs.Count)) ? "" : ((Func<string, string>)((sep) => string.Concat(sep, effs[(int)i].value, codex_emit_codex_type_effect_names(effs, (i + 1L)))))(((i > 0L) ? "B\u0002" : "")));
    }

    public static string codex_emit_def(IRDef d, List<string> ctor_names)
    {
        return (codex_skip_def(d, ctor_names) ? "" : string.Concat(d.name, "\u0002E\u0002", codex_emit_codex_type(d.type_val), "\u0001", d.name, codex_emit_def_params(d.@params, 0L), "\u0002M\u0001\u0002\u0002", codex_emit_expr(d.body, ctor_names, 1L), "\u0001"));
    }

    public static bool codex_skip_def(IRDef d, List<string> ctor_names)
    {
        return (ctor_names.Contains(d.name) ? true : ((((long)d.name.Length) == 0L) ? true : ((Func<long, bool>)((first) => ((Func<long, bool>)((code_a) => ((Func<long, bool>)((code_z) => (((first < code_a) || (first > code_z)) ? true : (codex_is_error_body(d.body) ? true : false))))(((long)"&"[(int)0L]))))(((long)"\u000F"[(int)0L]))))(((long)d.name[(int)0L]))));
    }

    public static bool codex_is_upper(long c)
    {
        return ((Func<long, bool>)((code) => ((Func<long, bool>)((code_z) => ((c >= code) && (c <= code_z))))(((long)"@"[(int)0L]))))(((long)")"[(int)0L]));
    }

    public static bool codex_is_error_body(IRExpr e)
    {
        return (e is IrError _mIrError30_ ? ((Func<CodexType, bool>)((ty) => ((Func<string, bool>)((msg) => true))((string)_mIrError30_.Field0)))((CodexType)_mIrError30_.Field1) : ((Func<IRExpr, bool>)((_) => false))(e));
    }

    public static bool codex_is_lower_start(string s)
    {
        return ((((long)s.Length) == 0L) ? false : ((Func<long, bool>)((c) => ((Func<long, bool>)((code_a) => ((Func<long, bool>)((code_z) => ((c >= code_a) && (c <= code_z))))(((long)"&"[(int)0L]))))(((long)"\u000F"[(int)0L]))))(((long)s[(int)0L])));
    }

    public static string codex_emit_def_params(List<IRParam> @params, long i)
    {
        return ((i == ((long)@params.Count)) ? "" : ((Func<IRParam, string>)((p) => string.Concat("\u0002J", p.name, "K", codex_emit_def_params(@params, (i + 1L)))))(@params[(int)i]));
    }

    public static string codex_emit_expr(IRExpr e, List<string> ctors, long indent)
    {
        return ((Func<IRExpr, string>)((_scrutinee31_) => (_scrutinee31_ is IrIntLit _mIrIntLit31_ ? ((Func<long, string>)((n) => _Cce.FromUnicode(n.ToString())))((long)_mIrIntLit31_.Field0) : (_scrutinee31_ is IrNumLit _mIrNumLit31_ ? ((Func<long, string>)((n) => _Cce.FromUnicode(n.ToString())))((long)_mIrNumLit31_.Field0) : (_scrutinee31_ is IrTextLit _mIrTextLit31_ ? ((Func<string, string>)((s) => string.Concat("H", codex_escape_text(s), "H")))((string)_mIrTextLit31_.Field0) : (_scrutinee31_ is IrBoolLit _mIrBoolLit31_ ? ((Func<bool, string>)((b) => (b ? "(\u0015\u0019\u000D" : "6\u000F\u0017\u0013\u000D")))((bool)_mIrBoolLit31_.Field0) : (_scrutinee31_ is IrCharLit _mIrCharLit31_ ? ((Func<long, string>)((n) => string.Concat("G", codex_escape_char(n), "G")))((long)_mIrCharLit31_.Field0) : (_scrutinee31_ is IrName _mIrName31_ ? ((Func<CodexType, string>)((ty) => ((Func<string, string>)((n) => n))((string)_mIrName31_.Field0)))((CodexType)_mIrName31_.Field1) : (_scrutinee31_ is IrBinary _mIrBinary31_ ? ((Func<CodexType, string>)((ty) => ((Func<IRExpr, string>)((r) => ((Func<IRExpr, string>)((l) => ((Func<IRBinaryOp, string>)((op) => codex_emit_binary(op, l, r, ctors, indent)))((IRBinaryOp)_mIrBinary31_.Field0)))((IRExpr)_mIrBinary31_.Field1)))((IRExpr)_mIrBinary31_.Field2)))((CodexType)_mIrBinary31_.Field3) : (_scrutinee31_ is IrNegate _mIrNegate31_ ? ((Func<IRExpr, string>)((operand) => string.Concat("\u0003\u0002I\u0002", codex_emit_expr(operand, ctors, indent))))((IRExpr)_mIrNegate31_.Field0) : (_scrutinee31_ is IrIf _mIrIf31_ ? ((Func<CodexType, string>)((ty) => ((Func<IRExpr, string>)((el) => ((Func<IRExpr, string>)((t) => ((Func<IRExpr, string>)((c) => codex_emit_if(c, t, el, ctors, indent)))((IRExpr)_mIrIf31_.Field0)))((IRExpr)_mIrIf31_.Field1)))((IRExpr)_mIrIf31_.Field2)))((CodexType)_mIrIf31_.Field3) : (_scrutinee31_ is IrLet _mIrLet31_ ? ((Func<IRExpr, string>)((body) => ((Func<IRExpr, string>)((val) => ((Func<CodexType, string>)((ty) => ((Func<string, string>)((name) => codex_emit_let(name, val, body, ctors, indent)))((string)_mIrLet31_.Field0)))((CodexType)_mIrLet31_.Field1)))((IRExpr)_mIrLet31_.Field2)))((IRExpr)_mIrLet31_.Field3) : (_scrutinee31_ is IrApply _mIrApply31_ ? ((Func<CodexType, string>)((ty) => ((Func<IRExpr, string>)((a) => ((Func<IRExpr, string>)((f) => codex_emit_apply(e, ctors, indent)))((IRExpr)_mIrApply31_.Field0)))((IRExpr)_mIrApply31_.Field1)))((CodexType)_mIrApply31_.Field2) : (_scrutinee31_ is IrLambda _mIrLambda31_ ? ((Func<CodexType, string>)((ty) => ((Func<IRExpr, string>)((body) => ((Func<List<IRParam>, string>)((@params) => codex_emit_lambda(@params, body, ctors, indent)))((List<IRParam>)_mIrLambda31_.Field0)))((IRExpr)_mIrLambda31_.Field1)))((CodexType)_mIrLambda31_.Field2) : (_scrutinee31_ is IrList _mIrList31_ ? ((Func<CodexType, string>)((ty) => ((Func<List<IRExpr>, string>)((elems) => codex_emit_list(elems, ctors, indent)))((List<IRExpr>)_mIrList31_.Field0)))((CodexType)_mIrList31_.Field1) : (_scrutinee31_ is IrMatch _mIrMatch31_ ? ((Func<CodexType, string>)((ty) => ((Func<List<IRBranch>, string>)((branches) => ((Func<IRExpr, string>)((scrut) => codex_emit_match(scrut, branches, ctors, indent)))((IRExpr)_mIrMatch31_.Field0)))((List<IRBranch>)_mIrMatch31_.Field1)))((CodexType)_mIrMatch31_.Field2) : (_scrutinee31_ is IrDo _mIrDo31_ ? ((Func<CodexType, string>)((ty) => ((Func<List<IRDoStmt>, string>)((stmts) => codex_emit_do(stmts, ctors, indent)))((List<IRDoStmt>)_mIrDo31_.Field0)))((CodexType)_mIrDo31_.Field1) : (_scrutinee31_ is IrRecord _mIrRecord31_ ? ((Func<CodexType, string>)((ty) => ((Func<List<IRFieldVal>, string>)((fields) => ((Func<string, string>)((name) => codex_emit_record(name, fields, ctors, indent)))((string)_mIrRecord31_.Field0)))((List<IRFieldVal>)_mIrRecord31_.Field1)))((CodexType)_mIrRecord31_.Field2) : (_scrutinee31_ is IrFieldAccess _mIrFieldAccess31_ ? ((Func<CodexType, string>)((ty) => ((Func<string, string>)((field) => ((Func<IRExpr, string>)((rec) => codex_emit_field_access(rec, field, ctors, indent)))((IRExpr)_mIrFieldAccess31_.Field0)))((string)_mIrFieldAccess31_.Field1)))((CodexType)_mIrFieldAccess31_.Field2) : (_scrutinee31_ is IrFork _mIrFork31_ ? ((Func<CodexType, string>)((ty) => ((Func<IRExpr, string>)((body) => string.Concat("\u001C\u0010\u0015\"\u0002J", codex_emit_expr(body, ctors, indent), "K")))((IRExpr)_mIrFork31_.Field0)))((CodexType)_mIrFork31_.Field1) : (_scrutinee31_ is IrAwait _mIrAwait31_ ? ((Func<CodexType, string>)((ty) => ((Func<IRExpr, string>)((task) => string.Concat("\u000F\u001B\u000F\u0011\u000E\u0002J", codex_emit_expr(task, ctors, indent), "K")))((IRExpr)_mIrAwait31_.Field0)))((CodexType)_mIrAwait31_.Field1) : (_scrutinee31_ is IrHandle _mIrHandle31_ ? ((Func<CodexType, string>)((ty) => ((Func<List<IRHandleClause>, string>)((clauses) => ((Func<IRExpr, string>)((body) => ((Func<string, string>)((eff) => codex_emit_handle(eff, body, clauses, ctors, indent)))((string)_mIrHandle31_.Field0)))((IRExpr)_mIrHandle31_.Field1)))((List<IRHandleClause>)_mIrHandle31_.Field2)))((CodexType)_mIrHandle31_.Field3) : (_scrutinee31_ is IrError _mIrError31_ ? ((Func<CodexType, string>)((ty) => ((Func<string, string>)((msg) => "\u0003"))((string)_mIrError31_.Field0)))((CodexType)_mIrError31_.Field1) : throw new InvalidOperationException("Non-exhaustive match"))))))))))))))))))))))))(e);
    }

    public static string codex_emit_binary(IRBinaryOp op, IRExpr l, IRExpr r, List<string> ctors, long indent)
    {
        return string.Concat(codex_emit_expr(l, ctors, indent), "\u0002", codex_bin_op_text(op), "\u0002", codex_emit_expr(r, ctors, indent));
    }

    public static string codex_bin_op_text(IRBinaryOp op)
    {
        return ((Func<IRBinaryOp, string>)((_scrutinee32_) => (_scrutinee32_ is IrAddInt _mIrAddInt32_ ? "L" : (_scrutinee32_ is IrSubInt _mIrSubInt32_ ? "I" : (_scrutinee32_ is IrMulInt _mIrMulInt32_ ? "N" : (_scrutinee32_ is IrDivInt _mIrDivInt32_ ? "Q" : (_scrutinee32_ is IrPowInt _mIrPowInt32_ ? "\u00E0\u0081\u009E" : (_scrutinee32_ is IrAddNum _mIrAddNum32_ ? "L" : (_scrutinee32_ is IrSubNum _mIrSubNum32_ ? "I" : (_scrutinee32_ is IrMulNum _mIrMulNum32_ ? "N" : (_scrutinee32_ is IrDivNum _mIrDivNum32_ ? "Q" : (_scrutinee32_ is IrEq _mIrEq32_ ? "MM" : (_scrutinee32_ is IrNotEq _mIrNotEq32_ ? "QM" : (_scrutinee32_ is IrLt _mIrLt32_ ? "O" : (_scrutinee32_ is IrGt _mIrGt32_ ? "P" : (_scrutinee32_ is IrLtEq _mIrLtEq32_ ? "OM" : (_scrutinee32_ is IrGtEq _mIrGtEq32_ ? "PM" : (_scrutinee32_ is IrAnd _mIrAnd32_ ? "T" : (_scrutinee32_ is IrOr _mIrOr32_ ? "W" : (_scrutinee32_ is IrAppendText _mIrAppendText32_ ? "LL" : (_scrutinee32_ is IrAppendList _mIrAppendList32_ ? "LL" : (_scrutinee32_ is IrConsList _mIrConsList32_ ? "EE" : throw new InvalidOperationException("Non-exhaustive match")))))))))))))))))))))))(op);
    }

    public static string codex_emit_if(IRExpr c, IRExpr t, IRExpr el, List<string> ctors, long indent)
    {
        return ((codex_is_simple(t) && codex_is_simple(el)) ? string.Concat("\u0011\u001C\u0002", codex_emit_expr(c, ctors, indent), "\u0002\u000E\u0014\u000D\u0012\u0002", codex_emit_expr(t, ctors, indent), "\u0002\u000D\u0017\u0013\u000D\u0002", codex_emit_expr(el, ctors, indent)) : string.Concat("\u0011\u001C\u0002", codex_emit_expr(c, ctors, indent), "\u0001", codex_indent((indent + 1L)), "\u000E\u0014\u000D\u0012\u0002", codex_emit_expr(t, ctors, (indent + 1L)), "\u0001", codex_indent((indent + 1L)), "\u000D\u0017\u0013\u000D\u0002", codex_emit_expr(el, ctors, (indent + 1L))));
    }

    public static string codex_emit_let(string name, IRExpr val, IRExpr body, List<string> ctors, long indent)
    {
        return string.Concat("\u0017\u000D\u000E\u0002", name, "\u0002M\u0002", codex_emit_expr(val, ctors, (indent + 1L)), "\u0001", codex_indent(indent), "\u0011\u0012\u0002", codex_emit_expr(body, ctors, indent));
    }

    public static string codex_emit_apply(IRExpr e, List<string> ctors, long indent)
    {
        return ((Func<ApplyChain, string>)((chain) => ((Func<IRExpr, string>)((func) => ((Func<List<IRExpr>, string>)((args) => string.Concat(codex_emit_expr(func, ctors, indent), codex_emit_apply_args(args, ctors, indent, 0L, ((long)args.Count), codex_is_ctor_name(func, ctors)))))(chain.args)))(chain.root)))(collect_apply_chain(e, new List<IRExpr>()));
    }

    public static string codex_emit_apply_args(List<IRExpr> args, List<string> ctors, long indent, long i, long len, bool is_ctor)
    {
        return ((i == len) ? "" : ((Func<IRExpr, string>)((arg) => string.Concat("\u0002", codex_wrap_arg(arg, ctors, indent, is_ctor), codex_emit_apply_args(args, ctors, indent, (i + 1L), len, is_ctor))))(args[(int)i]));
    }

    public static string codex_wrap_arg(IRExpr e, List<string> ctors, long indent, bool is_ctor)
    {
        return (codex_needs_parens(e, is_ctor) ? string.Concat("J", codex_emit_expr(e, ctors, indent), "K") : codex_emit_expr(e, ctors, indent));
    }

    public static bool codex_is_ctor_name(IRExpr e, List<string> ctors)
    {
        return (e is IrName _mIrName33_ ? ((Func<CodexType, bool>)((ty) => ((Func<string, bool>)((n) => ctors.Contains(n)))((string)_mIrName33_.Field0)))((CodexType)_mIrName33_.Field1) : ((Func<IRExpr, bool>)((_) => false))(e));
    }

    public static bool codex_needs_parens(IRExpr e, bool is_ctor)
    {
        return ((Func<IRExpr, bool>)((_scrutinee34_) => (_scrutinee34_ is IrApply _mIrApply34_ ? ((Func<CodexType, bool>)((ty) => ((Func<IRExpr, bool>)((a) => ((Func<IRExpr, bool>)((f) => true))((IRExpr)_mIrApply34_.Field0)))((IRExpr)_mIrApply34_.Field1)))((CodexType)_mIrApply34_.Field2) : (_scrutinee34_ is IrBinary _mIrBinary34_ ? ((Func<CodexType, bool>)((ty) => ((Func<IRExpr, bool>)((r) => ((Func<IRExpr, bool>)((l) => ((Func<IRBinaryOp, bool>)((op) => true))((IRBinaryOp)_mIrBinary34_.Field0)))((IRExpr)_mIrBinary34_.Field1)))((IRExpr)_mIrBinary34_.Field2)))((CodexType)_mIrBinary34_.Field3) : (_scrutinee34_ is IrIf _mIrIf34_ ? ((Func<CodexType, bool>)((ty) => ((Func<IRExpr, bool>)((el) => ((Func<IRExpr, bool>)((t) => ((Func<IRExpr, bool>)((c) => true))((IRExpr)_mIrIf34_.Field0)))((IRExpr)_mIrIf34_.Field1)))((IRExpr)_mIrIf34_.Field2)))((CodexType)_mIrIf34_.Field3) : (_scrutinee34_ is IrLet _mIrLet34_ ? ((Func<IRExpr, bool>)((body) => ((Func<IRExpr, bool>)((val) => ((Func<CodexType, bool>)((ty) => ((Func<string, bool>)((name) => true))((string)_mIrLet34_.Field0)))((CodexType)_mIrLet34_.Field1)))((IRExpr)_mIrLet34_.Field2)))((IRExpr)_mIrLet34_.Field3) : (_scrutinee34_ is IrMatch _mIrMatch34_ ? ((Func<CodexType, bool>)((ty) => ((Func<List<IRBranch>, bool>)((branches) => ((Func<IRExpr, bool>)((scrut) => true))((IRExpr)_mIrMatch34_.Field0)))((List<IRBranch>)_mIrMatch34_.Field1)))((CodexType)_mIrMatch34_.Field2) : (_scrutinee34_ is IrNegate _mIrNegate34_ ? ((Func<IRExpr, bool>)((operand) => true))((IRExpr)_mIrNegate34_.Field0) : (_scrutinee34_ is IrLambda _mIrLambda34_ ? ((Func<CodexType, bool>)((ty) => ((Func<IRExpr, bool>)((body) => ((Func<List<IRParam>, bool>)((@params) => true))((List<IRParam>)_mIrLambda34_.Field0)))((IRExpr)_mIrLambda34_.Field1)))((CodexType)_mIrLambda34_.Field2) : ((Func<IRExpr, bool>)((_) => false))(_scrutinee34_))))))))))(e);
    }

    public static string codex_emit_lambda(List<IRParam> @params, IRExpr body, List<string> ctors, long indent)
    {
        return string.Concat("V", codex_emit_lambda_params(@params, 0L), "\u0002IP\u0002", codex_emit_expr(body, ctors, indent));
    }

    public static string codex_emit_lambda_params(List<IRParam> @params, long i)
    {
        return ((i == ((long)@params.Count)) ? "" : ((Func<IRParam, string>)((p) => ((Func<string, string>)((sep) => string.Concat(sep, p.name, codex_emit_lambda_params(@params, (i + 1L)))))(((i > 0L) ? "\u0002" : ""))))(@params[(int)i]));
    }

    public static string codex_emit_list(List<IRExpr> elems, List<string> ctors, long indent)
    {
        return string.Concat("X", codex_emit_list_elems(elems, ctors, indent, 0L), "Y");
    }

    public static string codex_emit_list_elems(List<IRExpr> elems, List<string> ctors, long indent, long i)
    {
        return ((i == ((long)elems.Count)) ? "" : ((Func<string, string>)((sep) => string.Concat(sep, codex_emit_expr(elems[(int)i], ctors, indent), codex_emit_list_elems(elems, ctors, indent, (i + 1L)))))(((i > 0L) ? "B\u0002" : "")));
    }

    public static string codex_emit_match(IRExpr scrut, List<IRBranch> branches, List<string> ctors, long indent)
    {
        return string.Concat("\u001B\u0014\u000D\u0012\u0002", codex_emit_expr(scrut, ctors, indent), codex_emit_branches(branches, ctors, indent, 0L));
    }

    public static string codex_emit_branches(List<IRBranch> branches, List<string> ctors, long indent, long i)
    {
        return ((i == ((long)branches.Count)) ? "" : ((Func<IRBranch, string>)((b) => string.Concat("\u0001", codex_indent((indent + 1L)), "\u0011\u001C\u0002", codex_emit_pattern(b.pattern), "\u0002IP\u0002", codex_emit_expr(b.body, ctors, (indent + 1L)), codex_emit_branches(branches, ctors, indent, (i + 1L)))))(branches[(int)i]));
    }

    public static string codex_emit_pattern(IRPat p)
    {
        return ((Func<IRPat, string>)((_scrutinee35_) => (_scrutinee35_ is IrVarPat _mIrVarPat35_ ? ((Func<CodexType, string>)((ty) => ((Func<string, string>)((name) => name))((string)_mIrVarPat35_.Field0)))((CodexType)_mIrVarPat35_.Field1) : (_scrutinee35_ is IrLitPat _mIrLitPat35_ ? ((Func<CodexType, string>)((ty) => ((Func<string, string>)((text) => text))((string)_mIrLitPat35_.Field0)))((CodexType)_mIrLitPat35_.Field1) : (_scrutinee35_ is IrCtorPat _mIrCtorPat35_ ? ((Func<CodexType, string>)((ty) => ((Func<List<IRPat>, string>)((subs) => ((Func<string, string>)((name) => string.Concat(name, codex_emit_sub_patterns(subs, 0L))))((string)_mIrCtorPat35_.Field0)))((List<IRPat>)_mIrCtorPat35_.Field1)))((CodexType)_mIrCtorPat35_.Field2) : (_scrutinee35_ is IrWildPat _mIrWildPat35_ ? "U" : throw new InvalidOperationException("Non-exhaustive match")))))))(p);
    }

    public static string codex_emit_sub_patterns(List<IRPat> subs, long i)
    {
        return ((i == ((long)subs.Count)) ? "" : string.Concat("\u0002J", codex_emit_pattern(subs[(int)i]), "K", codex_emit_sub_patterns(subs, (i + 1L))));
    }

    public static string codex_emit_do(List<IRDoStmt> stmts, List<string> ctors, long indent)
    {
        return string.Concat("\u0016\u0010", codex_emit_do_stmts(stmts, ctors, indent, 0L));
    }

    public static string codex_emit_do_stmts(List<IRDoStmt> stmts, List<string> ctors, long indent, long i)
    {
        return ((i == ((long)stmts.Count)) ? "" : ((Func<IRDoStmt, string>)((s) => string.Concat("\u0001", codex_indent((indent + 1L)), codex_emit_do_stmt(s, ctors, (indent + 1L)), codex_emit_do_stmts(stmts, ctors, indent, (i + 1L)))))(stmts[(int)i]));
    }

    public static string codex_emit_do_stmt(IRDoStmt s, List<string> ctors, long indent)
    {
        return ((Func<IRDoStmt, string>)((_scrutinee36_) => (_scrutinee36_ is IrDoBind _mIrDoBind36_ ? ((Func<IRExpr, string>)((val) => ((Func<CodexType, string>)((ty) => ((Func<string, string>)((name) => string.Concat(name, "\u0002OI\u0002", codex_emit_expr(val, ctors, indent))))((string)_mIrDoBind36_.Field0)))((CodexType)_mIrDoBind36_.Field1)))((IRExpr)_mIrDoBind36_.Field2) : (_scrutinee36_ is IrDoExec _mIrDoExec36_ ? ((Func<IRExpr, string>)((e) => codex_emit_expr(e, ctors, indent)))((IRExpr)_mIrDoExec36_.Field0) : throw new InvalidOperationException("Non-exhaustive match")))))(s);
    }

    public static string codex_emit_record(string name, List<IRFieldVal> fields, List<string> ctors, long indent)
    {
        return string.Concat(name, "\u0002Z", codex_emit_record_fields(fields, ctors, indent, 0L), "\u0002[");
    }

    public static string codex_emit_record_fields(List<IRFieldVal> fields, List<string> ctors, long indent, long i)
    {
        return ((i == ((long)fields.Count)) ? "" : ((Func<IRFieldVal, string>)((f) => ((Func<string, string>)((sep) => string.Concat(sep, "\u0002", f.name, "\u0002M\u0002", codex_emit_expr(f.value, ctors, indent), codex_emit_record_fields(fields, ctors, indent, (i + 1L)))))(((i > 0L) ? "B" : ""))))(fields[(int)i]));
    }

    public static string codex_emit_field_access(IRExpr rec, string field, List<string> ctors, long indent)
    {
        return ((Func<IRExpr, string>)((_scrutinee37_) => (_scrutinee37_ is IrName _mIrName37_ ? ((Func<CodexType, string>)((ty) => ((Func<string, string>)((n) => string.Concat(n, "A", field)))((string)_mIrName37_.Field0)))((CodexType)_mIrName37_.Field1) : (_scrutinee37_ is IrFieldAccess _mIrFieldAccess37_ ? ((Func<CodexType, string>)((ty) => ((Func<string, string>)((f) => ((Func<IRExpr, string>)((r) => string.Concat(codex_emit_field_access(r, f, ctors, indent), "A", field)))((IRExpr)_mIrFieldAccess37_.Field0)))((string)_mIrFieldAccess37_.Field1)))((CodexType)_mIrFieldAccess37_.Field2) : ((Func<IRExpr, string>)((_) => string.Concat("J", codex_emit_expr(rec, ctors, indent), "KA", field)))(_scrutinee37_)))))(rec);
    }

    public static string codex_emit_handle(string eff, IRExpr body, List<IRHandleClause> clauses, List<string> ctors, long indent)
    {
        return string.Concat("\u0014\u000F\u0012\u0016\u0017\u000D\u0002", codex_emit_expr(body, ctors, indent), "\u0002\u001B\u0011\u000E\u0014", codex_emit_handle_clauses(clauses, ctors, indent, 0L));
    }

    public static string codex_emit_handle_clauses(List<IRHandleClause> clauses, List<string> ctors, long indent, long i)
    {
        return ((i == ((long)clauses.Count)) ? "" : ((Func<IRHandleClause, string>)((c) => string.Concat("\u0001", codex_indent((indent + 1L)), c.op_name, "\u0002J", c.resume_name, "K\u0002IP\u0002", codex_emit_expr(c.body, ctors, (indent + 1L)), codex_emit_handle_clauses(clauses, ctors, indent, (i + 1L)))))(clauses[(int)i]));
    }

    public static List<string> codex_collect_ctor_names(List<ATypeDef> type_defs, long i)
    {
        return ((i == ((long)type_defs.Count)) ? new List<string>() : ((Func<ATypeDef, List<string>>)((td) => ((Func<List<string>, List<string>>)((names) => Enumerable.Concat(names, codex_collect_ctor_names(type_defs, (i + 1L))).ToList()))(((Func<ATypeDef, List<string>>)((_scrutinee38_) => (_scrutinee38_ is AVariantTypeDef _mAVariantTypeDef38_ ? ((Func<List<AVariantCtorDef>, List<string>>)((ctors) => ((Func<List<Name>, List<string>>)((tparams) => ((Func<Name, List<string>>)((name) => codex_collect_variant_ctor_names(ctors, 0L)))((Name)_mAVariantTypeDef38_.Field0)))((List<Name>)_mAVariantTypeDef38_.Field1)))((List<AVariantCtorDef>)_mAVariantTypeDef38_.Field2) : (_scrutinee38_ is ARecordTypeDef _mARecordTypeDef38_ ? ((Func<List<ARecordFieldDef>, List<string>>)((fields) => ((Func<List<Name>, List<string>>)((tparams) => ((Func<Name, List<string>>)((name) => new List<string>() { name.value }))((Name)_mARecordTypeDef38_.Field0)))((List<Name>)_mARecordTypeDef38_.Field1)))((List<ARecordFieldDef>)_mARecordTypeDef38_.Field2) : throw new InvalidOperationException("Non-exhaustive match")))))(td))))(type_defs[(int)i]));
    }

    public static List<string> codex_collect_variant_ctor_names(List<AVariantCtorDef> ctors, long i)
    {
        return ((i == ((long)ctors.Count)) ? new List<string>() : ((Func<AVariantCtorDef, List<string>>)((c) => Enumerable.Concat(new List<string>() { c.name.value }, codex_collect_variant_ctor_names(ctors, (i + 1L))).ToList()))(ctors[(int)i]));
    }

    public static bool codex_is_simple(IRExpr e)
    {
        return ((Func<IRExpr, bool>)((_scrutinee39_) => (_scrutinee39_ is IrIntLit _mIrIntLit39_ ? ((Func<long, bool>)((n) => true))((long)_mIrIntLit39_.Field0) : (_scrutinee39_ is IrNumLit _mIrNumLit39_ ? ((Func<long, bool>)((n) => true))((long)_mIrNumLit39_.Field0) : (_scrutinee39_ is IrTextLit _mIrTextLit39_ ? ((Func<string, bool>)((s) => true))((string)_mIrTextLit39_.Field0) : (_scrutinee39_ is IrBoolLit _mIrBoolLit39_ ? ((Func<bool, bool>)((b) => true))((bool)_mIrBoolLit39_.Field0) : (_scrutinee39_ is IrCharLit _mIrCharLit39_ ? ((Func<long, bool>)((n) => true))((long)_mIrCharLit39_.Field0) : (_scrutinee39_ is IrName _mIrName39_ ? ((Func<CodexType, bool>)((ty) => ((Func<string, bool>)((n) => true))((string)_mIrName39_.Field0)))((CodexType)_mIrName39_.Field1) : (_scrutinee39_ is IrFieldAccess _mIrFieldAccess39_ ? ((Func<CodexType, bool>)((ty) => ((Func<string, bool>)((f) => ((Func<IRExpr, bool>)((r) => true))((IRExpr)_mIrFieldAccess39_.Field0)))((string)_mIrFieldAccess39_.Field1)))((CodexType)_mIrFieldAccess39_.Field2) : ((Func<IRExpr, bool>)((_) => false))(_scrutinee39_))))))))))(e);
    }

    public static string codex_indent(long n)
    {
        return ((n == 0L) ? "" : string.Concat("\u0002\u0002", codex_indent((n - 1L))));
    }

    public static string codex_escape_text(string s)
    {
        return codex_escape_text_loop(s, 0L, ((long)s.Length), "");
    }

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
                var _tco_1 = (i + 1L);
                var _tco_2 = len;
                var _tco_3 = string.Concat(acc, codex_escape_one_char(c));
                s = _tco_0;
                i = _tco_1;
                len = _tco_2;
                acc = _tco_3;
                continue;
            }
        }
    }

    public static string codex_escape_one_char(long c)
    {
        return ((c == ((long)"V"[(int)0L])) ? "VV" : ((c == ((long)"H"[(int)0L])) ? "VH" : ((c == ((long)"\u0001"[(int)0L])) ? "V\u0012" : ((char)c).ToString())));
    }

    public static string codex_escape_char(long c)
    {
        return ((c == ((long)"\u0001"[(int)0L])) ? "V\u0012" : ((c == ((long)"V"[(int)0L])) ? "VV" : ((c == ((long)"G"[(int)0L])) ? "VG" : ((char)c).ToString())));
    }

    public static string codex_emit_full_module(IRModule m, List<ATypeDef> type_defs)
    {
        return ((Func<List<string>, string>)((ctor_names) => string.Concat(codex_emit_type_defs(type_defs, 0L), codex_emit_all_defs(m.defs, ctor_names, 0L))))(codex_collect_ctor_names(type_defs, 0L));
    }

    public static string codex_emit_all_defs(List<IRDef> defs, List<string> ctor_names, long i)
    {
        return ((i == ((long)defs.Count)) ? "" : string.Concat(codex_emit_def(defs[(int)i], ctor_names), "\u0001", codex_emit_all_defs(defs, ctor_names, (i + 1L))));
    }

    public static string codex_emit_def_list(List<IRDef> defs, List<string> ctor_names, long i)
    {
        return ((i == ((long)defs.Count)) ? "" : string.Concat(codex_emit_def(defs[(int)i], ctor_names), "\u0001", codex_emit_def_list(defs, ctor_names, (i + 1L))));
    }

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
                    var _tco_2 = (i + 1L);
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
                    if (codex_has_def_named(acc, d.name, 0L, ((long)acc.Count)))
                    {
                        if ((codex_def_score(d) > codex_def_score_named(acc, d.name, 0L, ((long)acc.Count))))
                        {
                            var _tco_0 = defs;
                            var _tco_1 = ctor_names;
                            var _tco_2 = (i + 1L);
                            var _tco_3 = len;
                            var _tco_4 = codex_replace_def(acc, d.name, d, 0L, ((long)acc.Count));
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
                            var _tco_2 = (i + 1L);
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
                        var _tco_2 = (i + 1L);
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

    public static long codex_def_score(IRDef d)
    {
        return ((((long)d.@params.Count) * 100L) + codex_body_depth(d.body));
    }

    public static long codex_def_score_named(List<IRDef> defs, string name, long i, long len)
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
                    return codex_def_score(defs[(int)i]);
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

    public static long codex_body_depth(IRExpr e)
    {
        return ((Func<IRExpr, long>)((_scrutinee40_) => (_scrutinee40_ is IrName _mIrName40_ ? ((Func<CodexType, long>)((ty) => ((Func<string, long>)((n) => 1L))((string)_mIrName40_.Field0)))((CodexType)_mIrName40_.Field1) : (_scrutinee40_ is IrIntLit _mIrIntLit40_ ? ((Func<long, long>)((n) => 1L))((long)_mIrIntLit40_.Field0) : (_scrutinee40_ is IrTextLit _mIrTextLit40_ ? ((Func<string, long>)((s) => 1L))((string)_mIrTextLit40_.Field0) : (_scrutinee40_ is IrBoolLit _mIrBoolLit40_ ? ((Func<bool, long>)((b) => 1L))((bool)_mIrBoolLit40_.Field0) : (_scrutinee40_ is IrFieldAccess _mIrFieldAccess40_ ? ((Func<CodexType, long>)((ty) => ((Func<string, long>)((f) => ((Func<IRExpr, long>)((r) => 2L))((IRExpr)_mIrFieldAccess40_.Field0)))((string)_mIrFieldAccess40_.Field1)))((CodexType)_mIrFieldAccess40_.Field2) : (_scrutinee40_ is IrApply _mIrApply40_ ? ((Func<CodexType, long>)((ty) => ((Func<IRExpr, long>)((a) => ((Func<IRExpr, long>)((f) => (3L + codex_body_depth(f))))((IRExpr)_mIrApply40_.Field0)))((IRExpr)_mIrApply40_.Field1)))((CodexType)_mIrApply40_.Field2) : (_scrutinee40_ is IrLet _mIrLet40_ ? ((Func<IRExpr, long>)((body) => ((Func<IRExpr, long>)((val) => ((Func<CodexType, long>)((ty) => ((Func<string, long>)((name) => (5L + codex_body_depth(body))))((string)_mIrLet40_.Field0)))((CodexType)_mIrLet40_.Field1)))((IRExpr)_mIrLet40_.Field2)))((IRExpr)_mIrLet40_.Field3) : (_scrutinee40_ is IrIf _mIrIf40_ ? ((Func<CodexType, long>)((ty) => ((Func<IRExpr, long>)((el) => ((Func<IRExpr, long>)((t) => ((Func<IRExpr, long>)((c) => (5L + codex_body_depth(t))))((IRExpr)_mIrIf40_.Field0)))((IRExpr)_mIrIf40_.Field1)))((IRExpr)_mIrIf40_.Field2)))((CodexType)_mIrIf40_.Field3) : (_scrutinee40_ is IrMatch _mIrMatch40_ ? ((Func<CodexType, long>)((ty) => ((Func<List<IRBranch>, long>)((bs) => ((Func<IRExpr, long>)((s) => 10L))((IRExpr)_mIrMatch40_.Field0)))((List<IRBranch>)_mIrMatch40_.Field1)))((CodexType)_mIrMatch40_.Field2) : (_scrutinee40_ is IrLambda _mIrLambda40_ ? ((Func<CodexType, long>)((ty) => ((Func<IRExpr, long>)((b) => ((Func<List<IRParam>, long>)((ps) => 5L))((List<IRParam>)_mIrLambda40_.Field0)))((IRExpr)_mIrLambda40_.Field1)))((CodexType)_mIrLambda40_.Field2) : (_scrutinee40_ is IrDo _mIrDo40_ ? ((Func<CodexType, long>)((ty) => ((Func<List<IRDoStmt>, long>)((stmts) => 5L))((List<IRDoStmt>)_mIrDo40_.Field0)))((CodexType)_mIrDo40_.Field1) : (_scrutinee40_ is IrRecord _mIrRecord40_ ? ((Func<CodexType, long>)((ty) => ((Func<List<IRFieldVal>, long>)((fs) => ((Func<string, long>)((n) => 3L))((string)_mIrRecord40_.Field0)))((List<IRFieldVal>)_mIrRecord40_.Field1)))((CodexType)_mIrRecord40_.Field2) : ((Func<IRExpr, long>)((_) => 1L))(_scrutinee40_)))))))))))))))(e);
    }

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

    public static List<IRDef> codex_list_set(List<IRDef> xs, long idx, IRDef val)
    {
        return codex_list_set_loop(xs, idx, val, 0L, ((long)xs.Count), new List<IRDef>());
    }

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

    public static IRExpr lower_expr(AExpr e, CodexType ty, LowerCtx ctx)
    {
        return ((Func<AExpr, IRExpr>)((_scrutinee41_) => (_scrutinee41_ is ALitExpr _mALitExpr41_ ? ((Func<LiteralKind, IRExpr>)((kind) => ((Func<string, IRExpr>)((text) => lower_literal(text, kind)))((string)_mALitExpr41_.Field0)))((LiteralKind)_mALitExpr41_.Field1) : (_scrutinee41_ is ANameExpr _mANameExpr41_ ? ((Func<Name, IRExpr>)((name) => lower_name(name.value, ty, ctx)))((Name)_mANameExpr41_.Field0) : (_scrutinee41_ is AApplyExpr _mAApplyExpr41_ ? ((Func<AExpr, IRExpr>)((a) => ((Func<AExpr, IRExpr>)((f) => lower_apply(f, a, ty, ctx)))((AExpr)_mAApplyExpr41_.Field0)))((AExpr)_mAApplyExpr41_.Field1) : (_scrutinee41_ is ABinaryExpr _mABinaryExpr41_ ? ((Func<AExpr, IRExpr>)((r) => ((Func<BinaryOp, IRExpr>)((op) => ((Func<AExpr, IRExpr>)((l) => ((Func<IRExpr, IRExpr>)((left_ir) => ((Func<CodexType, IRExpr>)((left_ty) => ((Func<IRExpr, IRExpr>)((right_ir) => new IrBinary(lower_bin_op(op, left_ty), left_ir, right_ir, binary_result_type(op, left_ty, ty))))(lower_expr(r, ty, ctx))))(ir_expr_type(left_ir))))(lower_expr(l, ty, ctx))))((AExpr)_mABinaryExpr41_.Field0)))((BinaryOp)_mABinaryExpr41_.Field1)))((AExpr)_mABinaryExpr41_.Field2) : (_scrutinee41_ is AUnaryExpr _mAUnaryExpr41_ ? ((Func<AExpr, IRExpr>)((operand) => new IrNegate(lower_expr(operand, new IntegerTy(), ctx))))((AExpr)_mAUnaryExpr41_.Field0) : (_scrutinee41_ is AIfExpr _mAIfExpr41_ ? ((Func<AExpr, IRExpr>)((e2) => ((Func<AExpr, IRExpr>)((t) => ((Func<AExpr, IRExpr>)((c) => ((Func<IRExpr, IRExpr>)((then_ir) => ((Func<CodexType, IRExpr>)((then_ty) => ((Func<CodexType, IRExpr>)((result_ty) => ((Func<IRExpr, IRExpr>)((else_ir) => new IrIf(lower_expr(c, new BooleanTy(), ctx), then_ir, else_ir, result_ty)))(lower_expr(e2, result_ty, ctx))))((ty is ErrorTy _mErrorTy42_ ? then_ty : ((Func<CodexType, CodexType>)((_) => ty))(ty)))))(ir_expr_type(then_ir))))(lower_expr(t, ty, ctx))))((AExpr)_mAIfExpr41_.Field0)))((AExpr)_mAIfExpr41_.Field1)))((AExpr)_mAIfExpr41_.Field2) : (_scrutinee41_ is ALetExpr _mALetExpr41_ ? ((Func<AExpr, IRExpr>)((body) => ((Func<List<ALetBind>, IRExpr>)((binds) => lower_let(binds, body, ty, ctx)))((List<ALetBind>)_mALetExpr41_.Field0)))((AExpr)_mALetExpr41_.Field1) : (_scrutinee41_ is ALambdaExpr _mALambdaExpr41_ ? ((Func<AExpr, IRExpr>)((body) => ((Func<List<Name>, IRExpr>)((@params) => lower_lambda(@params, body, ty, ctx)))((List<Name>)_mALambdaExpr41_.Field0)))((AExpr)_mALambdaExpr41_.Field1) : (_scrutinee41_ is AMatchExpr _mAMatchExpr41_ ? ((Func<List<AMatchArm>, IRExpr>)((arms) => ((Func<AExpr, IRExpr>)((scrut) => lower_match(scrut, arms, ty, ctx)))((AExpr)_mAMatchExpr41_.Field0)))((List<AMatchArm>)_mAMatchExpr41_.Field1) : (_scrutinee41_ is AListExpr _mAListExpr41_ ? ((Func<List<AExpr>, IRExpr>)((elems) => lower_list(elems, ty, ctx)))((List<AExpr>)_mAListExpr41_.Field0) : (_scrutinee41_ is ARecordExpr _mARecordExpr41_ ? ((Func<List<AFieldExpr>, IRExpr>)((fields) => ((Func<Name, IRExpr>)((name) => lower_record(name, fields, ty, ctx)))((Name)_mARecordExpr41_.Field0)))((List<AFieldExpr>)_mARecordExpr41_.Field1) : (_scrutinee41_ is AFieldAccess _mAFieldAccess41_ ? ((Func<Name, IRExpr>)((field) => ((Func<AExpr, IRExpr>)((rec) => ((Func<IRExpr, IRExpr>)((rec_ir) => ((Func<CodexType, IRExpr>)((rec_ty) => ((Func<CodexType, IRExpr>)((field_ty) => ((Func<CodexType, IRExpr>)((actual_field_ty) => new IrFieldAccess(rec_ir, field.value, actual_field_ty)))((field_ty is ErrorTy _mErrorTy43_ ? ty : ((Func<CodexType, CodexType>)((_) => field_ty))(field_ty)))))(((Func<CodexType, CodexType>)((_scrutinee44_) => (_scrutinee44_ is RecordTy _mRecordTy44_ ? ((Func<List<RecordField>, CodexType>)((rfields) => ((Func<Name, CodexType>)((rname) => lookup_record_field(rfields, field.value)))((Name)_mRecordTy44_.Field0)))((List<RecordField>)_mRecordTy44_.Field1) : (_scrutinee44_ is ConstructedTy _mConstructedTy44_ ? ((Func<List<CodexType>, CodexType>)((cargs) => ((Func<Name, CodexType>)((cname) => ((Func<CodexType, CodexType>)((ctor_raw) => ((Func<CodexType, CodexType>)((resolved_record) => (resolved_record is RecordTy _mRecordTy45_ ? ((Func<List<RecordField>, CodexType>)((rf) => ((Func<Name, CodexType>)((rn) => lookup_record_field(rf, field.value)))((Name)_mRecordTy45_.Field0)))((List<RecordField>)_mRecordTy45_.Field1) : ((Func<CodexType, CodexType>)((_) => ty))(resolved_record))))((ctor_raw is ErrorTy _mErrorTy46_ ? new ErrorTy() : ((Func<CodexType, CodexType>)((_) => strip_fun_args_lower(deep_resolve(ctx.ust, ctor_raw))))(ctor_raw)))))(lookup_type(ctx.types, cname.value))))((Name)_mConstructedTy44_.Field0)))((List<CodexType>)_mConstructedTy44_.Field1) : ((Func<CodexType, CodexType>)((_) => ty))(_scrutinee44_)))))(rec_ty))))(deep_resolve(ctx.ust, ir_expr_type(rec_ir)))))(lower_expr(rec, new ErrorTy(), ctx))))((AExpr)_mAFieldAccess41_.Field0)))((Name)_mAFieldAccess41_.Field1) : (_scrutinee41_ is ADoExpr _mADoExpr41_ ? ((Func<List<ADoStmt>, IRExpr>)((stmts) => lower_do(stmts, ty, ctx)))((List<ADoStmt>)_mADoExpr41_.Field0) : (_scrutinee41_ is AHandleExpr _mAHandleExpr41_ ? ((Func<List<AHandleClause>, IRExpr>)((clauses) => ((Func<AExpr, IRExpr>)((body) => ((Func<Name, IRExpr>)((eff) => lower_handle(eff, body, clauses, ty, ctx)))((Name)_mAHandleExpr41_.Field0)))((AExpr)_mAHandleExpr41_.Field1)))((List<AHandleClause>)_mAHandleExpr41_.Field2) : (_scrutinee41_ is AErrorExpr _mAErrorExpr41_ ? ((Func<string, IRExpr>)((msg) => new IrError(msg, ty)))((string)_mAErrorExpr41_.Field0) : throw new InvalidOperationException("Non-exhaustive match"))))))))))))))))))(e);
    }

    public static IRExpr lower_name(string name, CodexType ty, LowerCtx ctx)
    {
        return ((Func<CodexType, IRExpr>)((raw) => (raw is ErrorTy _mErrorTy47_ ? new IrName(name, ty) : ((Func<CodexType, IRExpr>)((_) => ((Func<CodexType, IRExpr>)((resolved) => ((Func<CodexType, IRExpr>)((stripped) => new IrName(name, stripped)))(strip_forall_ty(resolved))))(deep_resolve(ctx.ust, raw))))(raw))))(lookup_type(ctx.types, name));
    }

    public static IRExpr lower_literal(string text, LiteralKind kind)
    {
        return ((Func<LiteralKind, IRExpr>)((_scrutinee48_) => (_scrutinee48_ is IntLit _mIntLit48_ ? new IrIntLit(long.Parse(_Cce.ToUnicode(text))) : (_scrutinee48_ is NumLit _mNumLit48_ ? new IrIntLit(long.Parse(_Cce.ToUnicode(text))) : (_scrutinee48_ is TextLit _mTextLit48_ ? new IrTextLit(text) : (_scrutinee48_ is CharLit _mCharLit48_ ? new IrCharLit(long.Parse(_Cce.ToUnicode(text))) : (_scrutinee48_ is BoolLit _mBoolLit48_ ? new IrBoolLit((text == "(\u0015\u0019\u000D")) : throw new InvalidOperationException("Non-exhaustive match"))))))))(kind);
    }

    public static IRExpr lower_apply(AExpr f, AExpr a, CodexType ty, LowerCtx ctx)
    {
        return ((Func<IRExpr, IRExpr>)((func_ir) => ((Func<CodexType, IRExpr>)((func_ty) => ((Func<CodexType, IRExpr>)((arg_ty) => ((Func<CodexType, IRExpr>)((ret_ty) => ((Func<IRExpr, IRExpr>)((arg_ir) => ((Func<CodexType, IRExpr>)((resolved_ret) => ((Func<CodexType, IRExpr>)((actual_ret) => lower_apply_dispatch(func_ir, arg_ir, actual_ret)))((resolved_ret is ErrorTy _mErrorTy49_ ? ty : ((Func<CodexType, CodexType>)((_) => resolved_ret))(resolved_ret)))))(subst_type_vars_from_arg(arg_ty, ir_expr_type(arg_ir), ret_ty))))(lower_expr(a, arg_ty, ctx))))(peel_fun_return(func_ty))))(peel_fun_param(func_ty))))(deep_resolve(ctx.ust, ir_expr_type(func_ir)))))(lower_expr(f, new ErrorTy(), ctx));
    }

    public static IRExpr lower_apply_dispatch(IRExpr func_ir, IRExpr arg_ir, CodexType ret_ty)
    {
        return (func_ir is IrName _mIrName50_ ? ((Func<CodexType, IRExpr>)((fty) => ((Func<string, IRExpr>)((n) => ((n == "\u001C\u0010\u0015\"") ? new IrFork(arg_ir, ret_ty) : ((n == "\u000F\u001B\u000F\u0011\u000E") ? new IrAwait(arg_ir, ret_ty) : new IrApply(func_ir, arg_ir, ret_ty)))))((string)_mIrName50_.Field0)))((CodexType)_mIrName50_.Field1) : ((Func<IRExpr, IRExpr>)((_) => new IrApply(func_ir, arg_ir, ret_ty)))(func_ir));
    }

    public static IRExpr lower_let(List<ALetBind> binds, AExpr body, CodexType ty, LowerCtx ctx)
    {
        return ((((long)binds.Count) == 0L) ? lower_expr(body, ty, ctx) : ((Func<ALetBind, IRExpr>)((b) => ((Func<IRExpr, IRExpr>)((val_ir) => ((Func<CodexType, IRExpr>)((val_ty) => ((Func<LowerCtx, IRExpr>)((ctx2) => new IrLet(b.name.value, val_ty, val_ir, lower_let_rest(binds, body, ty, ctx2, 1L))))(new LowerCtx(Enumerable.Concat(new List<TypeBinding>() { new TypeBinding(b.name.value, val_ty) }, ctx.types).ToList(), ctx.ust))))(deep_resolve(ctx.ust, ir_expr_type(val_ir)))))(lower_expr(b.value, new ErrorTy(), ctx))))(binds[(int)0L]));
    }

    public static IRExpr lower_let_rest(List<ALetBind> binds, AExpr body, CodexType ty, LowerCtx ctx, long i)
    {
        return ((i == ((long)binds.Count)) ? lower_expr(body, ty, ctx) : ((Func<ALetBind, IRExpr>)((b) => ((Func<IRExpr, IRExpr>)((val_ir) => ((Func<CodexType, IRExpr>)((val_ty) => ((Func<LowerCtx, IRExpr>)((ctx2) => new IrLet(b.name.value, val_ty, val_ir, lower_let_rest(binds, body, ty, ctx2, (i + 1L)))))(new LowerCtx(Enumerable.Concat(new List<TypeBinding>() { new TypeBinding(b.name.value, val_ty) }, ctx.types).ToList(), ctx.ust))))(deep_resolve(ctx.ust, ir_expr_type(val_ir)))))(lower_expr(b.value, new ErrorTy(), ctx))))(binds[(int)i]));
    }

    public static IRExpr lower_lambda(List<Name> @params, AExpr body, CodexType ty, LowerCtx ctx)
    {
        return ((Func<CodexType, IRExpr>)((stripped) => ((Func<List<IRParam>, IRExpr>)((lparams) => ((Func<LowerCtx, IRExpr>)((lctx) => new IrLambda(lparams, lower_expr(body, get_lambda_return(stripped, ((long)@params.Count)), lctx), ty)))(bind_lambda_to_ctx(ctx, @params, stripped, 0L))))(lower_lambda_params(@params, stripped, 0L))))(strip_forall_ty(ty));
    }

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
                var ctx2 = new LowerCtx(Enumerable.Concat(new List<TypeBinding>() { new TypeBinding(p.value, param_ty) }, ctx.types).ToList(), ctx.ust);
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

    public static List<IRParam> lower_lambda_params(List<Name> @params, CodexType ty, long i)
    {
        return lower_lambda_params_acc(@params, ty, i, new List<IRParam>());
    }

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
                var _tco_2 = (i + 1L);
                var _tco_3 = ((Func<List<IRParam>>)(() => { var _l = acc; _l.Add(new IRParam(p.value, param_ty)); return _l; }))();
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
                    var _ = _tco_s;
                    return new ErrorTy();
                }
            }
        }
    }

    public static IRExpr lower_match(AExpr scrut, List<AMatchArm> arms, CodexType ty, LowerCtx ctx)
    {
        return ((Func<IRExpr, IRExpr>)((scrut_ir) => ((Func<CodexType, IRExpr>)((scrut_ty) => ((Func<List<IRBranch>, IRExpr>)((branches) => ((Func<CodexType, IRExpr>)((result_ty) => new IrMatch(scrut_ir, branches, result_ty)))((ty is ErrorTy _mErrorTy51_ ? infer_match_type(branches, 0L, ((long)branches.Count)) : ((Func<CodexType, CodexType>)((_) => ty))(ty)))))(lower_match_arms_loop(arms, ty, scrut_ty, ctx, 0L, ((long)arms.Count)))))(ir_expr_type(scrut_ir))))(lower_expr(scrut, new ErrorTy(), ctx));
    }

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
                    var _ = _tco_s;
                    return body_ty;
                }
            }
        }
    }

    public static List<IRBranch> lower_match_arms_loop(List<AMatchArm> arms, CodexType ty, CodexType scrut_ty, LowerCtx ctx, long i, long len)
    {
        return lower_match_arms_acc(arms, ty, scrut_ty, ctx, i, len, new List<IRBranch>());
    }

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
                var _tco_6 = ((Func<List<IRBranch>>)(() => { var _l = acc; _l.Add(new IRBranch(lower_pattern(arm.pattern), lower_expr(arm.body, ty, arm_ctx))); return _l; }))();
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

    public static LowerCtx bind_pattern_to_ctx(LowerCtx ctx, APat pat, CodexType ty)
    {
        return ((Func<APat, LowerCtx>)((_scrutinee52_) => (_scrutinee52_ is AVarPat _mAVarPat52_ ? ((Func<Name, LowerCtx>)((name) => new LowerCtx(Enumerable.Concat(new List<TypeBinding>() { new TypeBinding(name.value, ty) }, ctx.types).ToList(), ctx.ust)))((Name)_mAVarPat52_.Field0) : (_scrutinee52_ is ACtorPat _mACtorPat52_ ? ((Func<List<APat>, LowerCtx>)((sub_pats) => ((Func<Name, LowerCtx>)((ctor_name) => ((Func<CodexType, LowerCtx>)((ctor_raw) => ((Func<CodexType, LowerCtx>)((ctor_ty) => ((Func<CodexType, LowerCtx>)((ctor_stripped) => bind_ctor_pattern_fields(ctx, sub_pats, ctor_stripped, 0L, ((long)sub_pats.Count))))(strip_forall_ty(ctor_ty))))(deep_resolve(ctx.ust, ctor_raw))))(lookup_type(ctx.types, ctor_name.value))))((Name)_mACtorPat52_.Field0)))((List<APat>)_mACtorPat52_.Field1) : (_scrutinee52_ is AWildPat _mAWildPat52_ ? ctx : (_scrutinee52_ is ALitPat _mALitPat52_ ? ((Func<LiteralKind, LowerCtx>)((kind) => ((Func<string, LowerCtx>)((text) => ctx))((string)_mALitPat52_.Field0)))((LiteralKind)_mALitPat52_.Field1) : throw new InvalidOperationException("Non-exhaustive match")))))))(pat);
    }

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
                    var _ = _tco_s;
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

    public static IRPat lower_pattern(APat p)
    {
        return ((Func<APat, IRPat>)((_scrutinee53_) => (_scrutinee53_ is AVarPat _mAVarPat53_ ? ((Func<Name, IRPat>)((name) => new IrVarPat(name.value, new ErrorTy())))((Name)_mAVarPat53_.Field0) : (_scrutinee53_ is ALitPat _mALitPat53_ ? ((Func<LiteralKind, IRPat>)((kind) => ((Func<string, IRPat>)((text) => new IrLitPat(text, new ErrorTy())))((string)_mALitPat53_.Field0)))((LiteralKind)_mALitPat53_.Field1) : (_scrutinee53_ is ACtorPat _mACtorPat53_ ? ((Func<List<APat>, IRPat>)((subs) => ((Func<Name, IRPat>)((name) => new IrCtorPat(name.value, map_list(new Func<APat, IRPat>(lower_pattern), subs), new ErrorTy())))((Name)_mACtorPat53_.Field0)))((List<APat>)_mACtorPat53_.Field1) : (_scrutinee53_ is AWildPat _mAWildPat53_ ? new IrWildPat() : throw new InvalidOperationException("Non-exhaustive match")))))))(p);
    }

    public static IRExpr lower_list(List<AExpr> elems, CodexType ty, LowerCtx ctx)
    {
        return ((Func<CodexType, IRExpr>)((elem_ty) => new IrList(lower_list_elems_loop(elems, elem_ty, ctx, 0L, ((long)elems.Count)), elem_ty)))((ty is ListTy _mListTy54_ ? ((Func<CodexType, CodexType>)((e) => e))((CodexType)_mListTy54_.Field0) : ((Func<CodexType, CodexType>)((_) => ((((long)elems.Count) == 0L) ? new ErrorTy() : ir_expr_type(lower_expr(elems[(int)0L], new ErrorTy(), ctx)))))(ty)));
    }

    public static List<IRExpr> lower_list_elems_loop(List<AExpr> elems, CodexType elem_ty, LowerCtx ctx, long i, long len)
    {
        return lower_list_elems_acc(elems, elem_ty, ctx, i, len, new List<IRExpr>());
    }

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

    public static IRExpr lower_record(Name name, List<AFieldExpr> fields, CodexType ty, LowerCtx ctx)
    {
        return ((Func<CodexType, IRExpr>)((ctor_raw) => ((Func<CodexType, IRExpr>)((record_ty) => ((Func<CodexType, IRExpr>)((actual_ty) => new IrRecord(name.value, lower_record_fields_typed(fields, actual_ty, ctx, 0L, ((long)fields.Count)), actual_ty)))((record_ty is ErrorTy _mErrorTy55_ ? ty : ((Func<CodexType, CodexType>)((_) => record_ty))(record_ty)))))((ctor_raw is ErrorTy _mErrorTy56_ ? ty : ((Func<CodexType, CodexType>)((_) => strip_fun_args_lower(deep_resolve(ctx.ust, ctor_raw))))(ctor_raw)))))(lookup_type(ctx.types, name.value));
    }

    public static List<IRFieldVal> lower_record_fields_typed(List<AFieldExpr> fields, CodexType record_ty, LowerCtx ctx, long i, long len)
    {
        return lower_record_fields_acc(fields, record_ty, ctx, i, len, new List<IRFieldVal>());
    }

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
                var field_expected = (record_ty is RecordTy _mRecordTy57_ ? ((Func<List<RecordField>, CodexType>)((rfields) => ((Func<Name, CodexType>)((rname) => lookup_record_field(rfields, f.name.value)))((Name)_mRecordTy57_.Field0)))((List<RecordField>)_mRecordTy57_.Field1) : ((Func<CodexType, CodexType>)((_) => new ErrorTy()))(record_ty));
                var _tco_0 = fields;
                var _tco_1 = record_ty;
                var _tco_2 = ctx;
                var _tco_3 = (i + 1L);
                var _tco_4 = len;
                var _tco_5 = ((Func<List<IRFieldVal>>)(() => { var _l = acc; _l.Add(new IRFieldVal(f.name.value, lower_expr(f.value, field_expected, ctx))); return _l; }))();
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

    public static IRExpr lower_do(List<ADoStmt> stmts, CodexType ty, LowerCtx ctx)
    {
        return new IrDo(lower_do_stmts_loop(stmts, ty, ctx, 0L, ((long)stmts.Count)), ty);
    }

    public static List<IRDoStmt> lower_do_stmts_loop(List<ADoStmt> stmts, CodexType ty, LowerCtx ctx, long i, long len)
    {
        return lower_do_stmts_acc(stmts, ty, ctx, i, len, new List<IRDoStmt>());
    }

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
                    var ctx2 = new LowerCtx(Enumerable.Concat(new List<TypeBinding>() { new TypeBinding(name.value, val_ty) }, ctx.types).ToList(), ctx.ust);
                    var _tco_0 = stmts;
                    var _tco_1 = ty;
                    var _tco_2 = ctx2;
                    var _tco_3 = (i + 1L);
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
                    var _tco_3 = (i + 1L);
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

    public static IRExpr lower_handle(Name eff, AExpr body, List<AHandleClause> clauses, CodexType ty, LowerCtx ctx)
    {
        return ((Func<IRExpr, IRExpr>)((body_ir) => new IrHandle(eff.value, body_ir, lower_handle_clauses(clauses, ty, ctx), ty)))(lower_expr(body, ty, ctx));
    }

    public static List<IRHandleClause> lower_handle_clauses(List<AHandleClause> clauses, CodexType ty, LowerCtx ctx)
    {
        return lower_handle_clauses_loop(clauses, ty, ctx, 0L);
    }

    public static List<IRHandleClause> lower_handle_clauses_loop(List<AHandleClause> clauses, CodexType ty, LowerCtx ctx, long i)
    {
        return lower_handle_clauses_acc(clauses, ty, ctx, i, new List<IRHandleClause>());
    }

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
                var _tco_4 = ((Func<List<IRHandleClause>>)(() => { var _l = acc; _l.Add(new IRHandleClause(c.op_name.value, c.resume_name.value, body_ir)); return _l; }))();
                clauses = _tco_0;
                ty = _tco_1;
                ctx = _tco_2;
                i = _tco_3;
                acc = _tco_4;
                continue;
            }
        }
    }

    public static IRDef lower_def(ADef d, List<TypeBinding> types, UnificationState ust)
    {
        return ((Func<CodexType, IRDef>)((raw_type) => ((Func<CodexType, IRDef>)((full_type) => ((Func<CodexType, IRDef>)((stripped) => ((Func<List<IRParam>, IRDef>)((@params) => ((Func<CodexType, IRDef>)((ret_type) => ((Func<LowerCtx, IRDef>)((ctx) => new IRDef(d.name.value, @params, full_type, lower_expr(d.body, ret_type, ctx))))(build_def_ctx(types, ust, d.@params, stripped))))(get_return_type_n(stripped, ((long)d.@params.Count)))))(lower_def_params(d.@params, stripped, 0L))))(strip_forall_ty(full_type))))(deep_resolve(ust, raw_type))))(lookup_type(types, d.name.value));
    }

    public static LowerCtx build_def_ctx(List<TypeBinding> types, UnificationState ust, List<AParam> @params, CodexType ty)
    {
        return ((Func<LowerCtx, LowerCtx>)((base_ctx) => bind_params_to_ctx(base_ctx, @params, ty, 0L)))(new LowerCtx(types, ust));
    }

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
                var ctx2 = new LowerCtx(Enumerable.Concat(new List<TypeBinding>() { new TypeBinding(p.name.value, param_ty) }, ctx.types).ToList(), ctx.ust);
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

    public static List<IRParam> lower_def_params(List<AParam> @params, CodexType ty, long i)
    {
        return lower_def_params_acc(@params, ty, i, new List<IRParam>());
    }

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
                var _tco_3 = ((Func<List<IRParam>>)(() => { var _l = acc; _l.Add(new IRParam(p.name.value, param_ty)); return _l; }))();
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
                    var _ = _tco_s;
                    return new ErrorTy();
                }
            }
        }
    }

    public static IRModule lower_module(AModule m, List<TypeBinding> types, UnificationState ust)
    {
        return ((Func<List<TypeBinding>, IRModule>)((ctor_types) => ((Func<List<TypeBinding>, IRModule>)((all_types) => new IRModule(m.name, lower_defs(m.defs, all_types, ust, 0L))))(Enumerable.Concat(ctor_types, Enumerable.Concat(types, builtin_type_env().bindings).ToList()).ToList())))(collect_ctor_bindings(m.type_defs, 0L, ((long)m.type_defs.Count), new List<TypeBinding>()));
    }

    public static List<IRDef> lower_defs(List<ADef> defs, List<TypeBinding> types, UnificationState ust, long i)
    {
        return lower_defs_acc(defs, types, ust, i, new List<IRDef>());
    }

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

    public static CodexType lookup_type(List<TypeBinding> bindings, string name)
    {
        return lookup_type_loop(bindings, name, 0L, ((long)bindings.Count));
    }

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
                var _ = _tco_s;
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
                var _ = _tco_s;
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
                var _ = _tco_s;
                return ty;
            }
        }
    }

    public static CodexType subst_type_vars_from_arg(CodexType param_ty, CodexType arg_ty, CodexType target)
    {
        return ((Func<CodexType, CodexType>)((_scrutinee58_) => (_scrutinee58_ is TypeVar _mTypeVar58_ ? ((Func<long, CodexType>)((id) => subst_type_var_in_target(target, id, arg_ty)))((long)_mTypeVar58_.Field0) : (_scrutinee58_ is ListTy _mListTy58_ ? ((Func<CodexType, CodexType>)((pe) => subst_from_list(pe, arg_ty, target)))((CodexType)_mListTy58_.Field0) : (_scrutinee58_ is FunTy _mFunTy58_ ? ((Func<CodexType, CodexType>)((pr) => ((Func<CodexType, CodexType>)((pp) => subst_from_fun(pp, pr, arg_ty, target)))((CodexType)_mFunTy58_.Field0)))((CodexType)_mFunTy58_.Field1) : ((Func<CodexType, CodexType>)((_) => target))(_scrutinee58_))))))(param_ty);
    }

    public static CodexType subst_from_list(CodexType pe, CodexType arg_ty, CodexType target)
    {
        return (arg_ty is ListTy _mListTy59_ ? ((Func<CodexType, CodexType>)((ae) => subst_type_vars_from_arg(pe, ae, target)))((CodexType)_mListTy59_.Field0) : ((Func<CodexType, CodexType>)((_) => target))(arg_ty));
    }

    public static CodexType subst_from_fun(CodexType pp, CodexType pr, CodexType arg_ty, CodexType target)
    {
        return (arg_ty is FunTy _mFunTy60_ ? ((Func<CodexType, CodexType>)((ar) => ((Func<CodexType, CodexType>)((ap) => ((Func<CodexType, CodexType>)((t2) => subst_type_vars_from_arg(pr, ar, t2)))(subst_type_vars_from_arg(pp, ap, target))))((CodexType)_mFunTy60_.Field0)))((CodexType)_mFunTy60_.Field1) : ((Func<CodexType, CodexType>)((_) => target))(arg_ty));
    }

    public static CodexType subst_type_var_in_target(CodexType ty, long var_id, CodexType replacement)
    {
        return ((Func<CodexType, CodexType>)((_scrutinee61_) => (_scrutinee61_ is TypeVar _mTypeVar61_ ? ((Func<long, CodexType>)((id) => ((id == var_id) ? replacement : ty)))((long)_mTypeVar61_.Field0) : (_scrutinee61_ is FunTy _mFunTy61_ ? ((Func<CodexType, CodexType>)((r) => ((Func<CodexType, CodexType>)((p) => new FunTy(subst_type_var_in_target(p, var_id, replacement), subst_type_var_in_target(r, var_id, replacement))))((CodexType)_mFunTy61_.Field0)))((CodexType)_mFunTy61_.Field1) : (_scrutinee61_ is ListTy _mListTy61_ ? ((Func<CodexType, CodexType>)((elem) => new ListTy(subst_type_var_in_target(elem, var_id, replacement))))((CodexType)_mListTy61_.Field0) : (_scrutinee61_ is ForAllTy _mForAllTy61_ ? ((Func<CodexType, CodexType>)((body) => ((Func<long, CodexType>)((fid) => ((fid == var_id) ? ty : new ForAllTy(fid, subst_type_var_in_target(body, var_id, replacement)))))((long)_mForAllTy61_.Field0)))((CodexType)_mForAllTy61_.Field1) : ((Func<CodexType, CodexType>)((_) => ty))(_scrutinee61_)))))))(ty);
    }

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
                var _ = _tco_s;
                return ty;
            }
        }
    }

    public static bool is_text_type(CodexType ty)
    {
        return (ty is TextTy _mTextTy62_ ? true : ((Func<CodexType, bool>)((_) => false))(ty));
    }

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

    public static List<TypeBinding> ctor_bindings_for_typedef(ATypeDef td)
    {
        return ((Func<ATypeDef, List<TypeBinding>>)((_scrutinee63_) => (_scrutinee63_ is AVariantTypeDef _mAVariantTypeDef63_ ? ((Func<List<AVariantCtorDef>, List<TypeBinding>>)((ctors) => ((Func<List<Name>, List<TypeBinding>>)((type_params) => ((Func<Name, List<TypeBinding>>)((name) => ((Func<CodexType, List<TypeBinding>>)((result_ty) => collect_variant_ctor_bindings(ctors, result_ty, 0L, ((long)ctors.Count), new List<TypeBinding>())))(new ConstructedTy(name, new List<CodexType>()))))((Name)_mAVariantTypeDef63_.Field0)))((List<Name>)_mAVariantTypeDef63_.Field1)))((List<AVariantCtorDef>)_mAVariantTypeDef63_.Field2) : (_scrutinee63_ is ARecordTypeDef _mARecordTypeDef63_ ? ((Func<List<ARecordFieldDef>, List<TypeBinding>>)((fields) => ((Func<List<Name>, List<TypeBinding>>)((type_params) => ((Func<Name, List<TypeBinding>>)((name) => ((Func<List<RecordField>, List<TypeBinding>>)((resolved_fields) => ((Func<CodexType, List<TypeBinding>>)((result_ty) => ((Func<CodexType, List<TypeBinding>>)((ctor_ty) => new List<TypeBinding>() { new TypeBinding(name.value, ctor_ty) }))(build_record_ctor_type_for_lower(fields, result_ty, 0L, ((long)fields.Count)))))(new RecordTy(name, resolved_fields))))(build_record_fields_for_lower(fields, 0L, ((long)fields.Count), new List<RecordField>()))))((Name)_mARecordTypeDef63_.Field0)))((List<Name>)_mARecordTypeDef63_.Field1)))((List<ARecordFieldDef>)_mARecordTypeDef63_.Field2) : throw new InvalidOperationException("Non-exhaustive match")))))(td);
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
                var ctor_ty = build_ctor_type_for_lower(ctor.fields, result_ty, 0L, ((long)ctor.fields.Count));
                var _tco_0 = ctors;
                var _tco_1 = result_ty;
                var _tco_2 = (i + 1L);
                var _tco_3 = len;
                var _tco_4 = ((Func<List<TypeBinding>>)(() => { var _l = acc; _l.Add(new TypeBinding(ctor.name.value, ctor_ty)); return _l; }))();
                ctors = _tco_0;
                result_ty = _tco_1;
                i = _tco_2;
                len = _tco_3;
                acc = _tco_4;
                continue;
            }
        }
    }

    public static CodexType build_ctor_type_for_lower(List<ATypeExpr> fields, CodexType result, long i, long len)
    {
        return ((i == len) ? result : ((Func<CodexType, CodexType>)((rest) => new FunTy(resolve_type_expr_for_lower(fields[(int)i]), rest)))(build_ctor_type_for_lower(fields, result, (i + 1L), len)));
    }

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
                var rfield = new RecordField(f.name, resolve_type_expr_for_lower(f.type_expr));
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

    public static CodexType build_record_ctor_type_for_lower(List<ARecordFieldDef> fields, CodexType result, long i, long len)
    {
        return ((i == len) ? result : ((Func<ARecordFieldDef, CodexType>)((f) => ((Func<CodexType, CodexType>)((rest) => new FunTy(resolve_type_expr_for_lower(f.type_expr), rest)))(build_record_ctor_type_for_lower(fields, result, (i + 1L), len))))(fields[(int)i]));
    }

    public static CodexType resolve_type_expr_for_lower(ATypeExpr texpr)
    {
        return ((Func<ATypeExpr, CodexType>)((_scrutinee64_) => (_scrutinee64_ is ANamedType _mANamedType64_ ? ((Func<Name, CodexType>)((name) => ((name.value == "+\u0012\u000E\u000D\u001D\u000D\u0015") ? new IntegerTy() : ((name.value == ",\u0019\u001A \u000D\u0015") ? new NumberTy() : ((name.value == "(\u000D$\u000E") ? new TextTy() : ((name.value == ":\u0010\u0010\u0017\u000D\u000F\u0012") ? new BooleanTy() : ((name.value == ",\u0010\u000E\u0014\u0011\u0012\u001D") ? new NothingTy() : new ConstructedTy(name, new List<CodexType>()))))))))((Name)_mANamedType64_.Field0) : (_scrutinee64_ is AFunType _mAFunType64_ ? ((Func<ATypeExpr, CodexType>)((ret) => ((Func<ATypeExpr, CodexType>)((param) => new FunTy(resolve_type_expr_for_lower(param), resolve_type_expr_for_lower(ret))))((ATypeExpr)_mAFunType64_.Field0)))((ATypeExpr)_mAFunType64_.Field1) : (_scrutinee64_ is AAppType _mAAppType64_ ? ((Func<List<ATypeExpr>, CodexType>)((args) => ((Func<ATypeExpr, CodexType>)((ctor) => (ctor is ANamedType _mANamedType65_ ? ((Func<Name, CodexType>)((cname) => ((cname.value == "1\u0011\u0013\u000E") ? ((((long)args.Count) == 1L) ? new ListTy(resolve_type_expr_for_lower(args[(int)0L])) : new ListTy(new ErrorTy())) : new ConstructedTy(cname, new List<CodexType>()))))((Name)_mANamedType65_.Field0) : ((Func<ATypeExpr, CodexType>)((_) => new ErrorTy()))(ctor))))((ATypeExpr)_mAAppType64_.Field0)))((List<ATypeExpr>)_mAAppType64_.Field1) : throw new InvalidOperationException("Non-exhaustive match"))))))(texpr);
    }

    public static IRBinaryOp lower_bin_op(BinaryOp op, CodexType ty)
    {
        return ((Func<BinaryOp, IRBinaryOp>)((_scrutinee66_) => (_scrutinee66_ is OpAdd _mOpAdd66_ ? new IrAddInt() : (_scrutinee66_ is OpSub _mOpSub66_ ? new IrSubInt() : (_scrutinee66_ is OpMul _mOpMul66_ ? new IrMulInt() : (_scrutinee66_ is OpDiv _mOpDiv66_ ? new IrDivInt() : (_scrutinee66_ is OpPow _mOpPow66_ ? new IrPowInt() : (_scrutinee66_ is OpEq _mOpEq66_ ? new IrEq() : (_scrutinee66_ is OpNotEq _mOpNotEq66_ ? new IrNotEq() : (_scrutinee66_ is OpLt _mOpLt66_ ? new IrLt() : (_scrutinee66_ is OpGt _mOpGt66_ ? new IrGt() : (_scrutinee66_ is OpLtEq _mOpLtEq66_ ? new IrLtEq() : (_scrutinee66_ is OpGtEq _mOpGtEq66_ ? new IrGtEq() : (_scrutinee66_ is OpDefEq _mOpDefEq66_ ? new IrEq() : (_scrutinee66_ is OpAppend _mOpAppend66_ ? (is_text_type(ty) ? new IrAppendText() : new IrAppendList()) : (_scrutinee66_ is OpCons _mOpCons66_ ? new IrConsList() : (_scrutinee66_ is OpAnd _mOpAnd66_ ? new IrAnd() : (_scrutinee66_ is OpOr _mOpOr66_ ? new IrOr() : throw new InvalidOperationException("Non-exhaustive match")))))))))))))))))))(op);
    }

    public static CodexType binary_result_type(BinaryOp op, CodexType left_ty, CodexType expected_ty)
    {
        return ((Func<BinaryOp, CodexType>)((_scrutinee67_) => (_scrutinee67_ is OpEq _mOpEq67_ ? new BooleanTy() : (_scrutinee67_ is OpNotEq _mOpNotEq67_ ? new BooleanTy() : (_scrutinee67_ is OpLt _mOpLt67_ ? new BooleanTy() : (_scrutinee67_ is OpGt _mOpGt67_ ? new BooleanTy() : (_scrutinee67_ is OpLtEq _mOpLtEq67_ ? new BooleanTy() : (_scrutinee67_ is OpGtEq _mOpGtEq67_ ? new BooleanTy() : (_scrutinee67_ is OpDefEq _mOpDefEq67_ ? new BooleanTy() : (_scrutinee67_ is OpAnd _mOpAnd67_ ? new BooleanTy() : (_scrutinee67_ is OpOr _mOpOr67_ ? new BooleanTy() : (_scrutinee67_ is OpAppend _mOpAppend67_ ? (is_text_type(left_ty) ? new TextTy() : (is_text_type(expected_ty) ? new TextTy() : left_ty)) : ((Func<BinaryOp, CodexType>)((_) => left_ty))(_scrutinee67_)))))))))))))(op);
    }

    public static Scope empty_scope()
    {
        return new Scope(new List<string>());
    }

    public static bool scope_has(Scope sc, string name)
    {
        return ((Func<long, bool>)((len) => ((len == 0L) ? false : ((Func<long, bool>)((pos) => ((pos >= len) ? false : (sc.names[(int)pos] == name))))(bsearch_text_set(sc.names, name, 0L, len)))))(((long)sc.names.Count));
    }

    public static Scope scope_add(Scope sc, string name)
    {
        return ((Func<long, Scope>)((len) => ((Func<long, Scope>)((pos) => new Scope(((Func<List<string>>)(() => { var _l = new List<string>(sc.names); _l.Insert((int)pos, name); return _l; }))())))(bsearch_text_set(sc.names, name, 0L, len))))(((long)sc.names.Count));
    }

    public static List<string> builtin_names()
    {
        return new List<string>() { "\u0013\u0014\u0010\u001B", "\u0012\u000D\u001D\u000F\u000E\u000D", "(\u0015\u0019\u000D", "6\u000F\u0017\u0013\u000D", ",\u0010\u000E\u0014\u0011\u0012\u001D", "\u001F\u0015\u0011\u0012\u000EI\u0017\u0011\u0012\u000D", "\u0015\u000D\u000F\u0016I\u0017\u0011\u0012\u000D", "\u0015\u000D\u000F\u0016I\u001C\u0011\u0017\u000D", "\u001B\u0015\u0011\u000E\u000DI\u001C\u0011\u0017\u000D", "\u001C\u0011\u0017\u000DI\u000D$\u0011\u0013\u000E\u0013", "\u0017\u0011\u0013\u000EI\u001C\u0011\u0017\u000D\u0013", "\u0010\u001F\u000D\u0012I\u001C\u0011\u0017\u000D", "\u0015\u000D\u000F\u0016I\u000F\u0017\u0017", "\u0018\u0017\u0010\u0013\u000DI\u001C\u0011\u0017\u000D", "\u0018\u0014\u000F\u0015I\u000F\u000E", "\u0018\u0014\u000F\u0015I\u000E\u0010I\u000E\u000D$\u000E", "\u000E\u000D$\u000EI\u0017\u000D\u0012\u001D\u000E\u0014", "\u0013\u0019 \u0013\u000E\u0015\u0011\u0012\u001D", "\u0011\u0013I\u0017\u000D\u000E\u000E\u000D\u0015", "\u0011\u0013I\u0016\u0011\u001D\u0011\u000E", "\u0011\u0013I\u001B\u0014\u0011\u000E\u000D\u0013\u001F\u000F\u0018\u000D", "\u000E\u000D$\u000EI\u000E\u0010I\u0011\u0012\u000E\u000D\u001D\u000D\u0015", "\u0011\u0012\u000E\u000D\u001D\u000D\u0015I\u000E\u0010I\u000E\u000D$\u000E", "\u000E\u000D$\u000EI\u0015\u000D\u001F\u0017\u000F\u0018\u000D", "\u000E\u000D$\u000EI\u0013\u001F\u0017\u0011\u000E", "\u000E\u000D$\u000EI\u0018\u0010\u0012\u000E\u000F\u0011\u0012\u0013", "\u000E\u000D$\u000EI\u0013\u000E\u000F\u0015\u000E\u0013I\u001B\u0011\u000E\u0014", "\u0018\u0014\u000F\u0015I\u0018\u0010\u0016\u000D", "\u0018\u0014\u000F\u0015I\u0018\u0010\u0016\u000DI\u000F\u000E", "\u0018\u0010\u0016\u000DI\u000E\u0010I\u0018\u0014\u000F\u0015", "\u0017\u0011\u0013\u000EI\u0017\u000D\u0012\u001D\u000E\u0014", "\u0017\u0011\u0013\u000EI\u000F\u000E", "\u0017\u0011\u0013\u000EI\u0011\u0012\u0013\u000D\u0015\u000EI\u000F\u000E", "\u0017\u0011\u0013\u000EI\u0013\u0012\u0010\u0018", "\u000E\u000D$\u000EI\u0018\u0010\u001A\u001F\u000F\u0015\u000D", "\u001D\u000D\u000EI\u000F\u0015\u001D\u0013", "\u001D\u000D\u000EI\u000D\u0012!", "\u0018\u0019\u0015\u0015\u000D\u0012\u000EI\u0016\u0011\u0015", "\u001A\u000F\u001F", "\u001C\u0011\u0017\u000E\u000D\u0015", "\u001C\u0010\u0017\u0016" };
    }

    public static bool is_type_name(string name)
    {
        return ((((long)name.Length) == 0L) ? false : ((((long)name[(int)0L]) >= 13L && ((long)name[(int)0L]) <= 64L) && is_upper_char(((long)name[(int)0L]))));
    }

    public static bool is_upper_char(long c)
    {
        return ((Func<long, bool>)((code) => ((code >= 41L) && (code <= 64L))))(c);
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
                if (acc.Contains(name))
                {
                    var _tco_0 = defs;
                    var _tco_1 = (i + 1L);
                    var _tco_2 = len;
                    var _tco_3 = acc;
                    var _tco_4 = Enumerable.Concat(errs, new List<Diagnostic>() { make_error("20>\u0006\u0003\u0003\u0004", string.Concat("0\u0019\u001F\u0017\u0011\u0018\u000F\u000E\u000D\u0002\u0016\u000D\u001C\u0011\u0012\u0011\u000E\u0011\u0010\u0012E\u0002", name)) }).ToList();
                    defs = _tco_0;
                    i = _tco_1;
                    len = _tco_2;
                    acc = _tco_3;
                    errs = _tco_4;
                    continue;
                }
                else
                {
                    var pos = bsearch_text_set(acc, name, 0L, ((long)acc.Count));
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

    public static bool list_contains(List<string> xs, string name)
    {
        return ((Func<long, bool>)((len) => ((len == 0L) ? false : ((Func<long, bool>)((pos) => ((pos >= len) ? false : (xs[(int)pos] == name))))(bsearch_text_set(xs, name, 0L, len)))))(((long)xs.Count));
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
                    return new List<Diagnostic>() { make_error("20>\u0006\u0003\u0003\u0005", string.Concat("3\u0012\u0016\u000D\u001C\u0011\u0012\u000D\u0016\u0002\u0012\u000F\u001A\u000DE\u0002", name.value)) };
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
        return ((Func<APat, Scope>)((_scrutinee68_) => (_scrutinee68_ is AVarPat _mAVarPat68_ ? ((Func<Name, Scope>)((name) => scope_add(sc, name.value)))((Name)_mAVarPat68_.Field0) : (_scrutinee68_ is ACtorPat _mACtorPat68_ ? ((Func<List<APat>, Scope>)((subs) => ((Func<Name, Scope>)((name) => collect_ctor_pat_names(sc, subs, 0L, ((long)subs.Count))))((Name)_mACtorPat68_.Field0)))((List<APat>)_mACtorPat68_.Field1) : (_scrutinee68_ is ALitPat _mALitPat68_ ? ((Func<LiteralKind, Scope>)((kind) => ((Func<string, Scope>)((val) => sc))((string)_mALitPat68_.Field0)))((LiteralKind)_mALitPat68_.Field1) : (_scrutinee68_ is AWildPat _mAWildPat68_ ? sc : throw new InvalidOperationException("Non-exhaustive match")))))))(pat);
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
        return resolve_module_with_imports(mod, new List<ResolveResult>());
    }

    public static ResolveResult resolve_module_with_imports(AModule mod, List<ResolveResult> imported)
    {
        return ((Func<CollectResult, ResolveResult>)((top) => ((Func<CtorCollectResult, ResolveResult>)((ctors) => ((Func<List<string>, ResolveResult>)((imported_names) => ((Func<List<string>, ResolveResult>)((all_top) => ((Func<Scope, ResolveResult>)((sc) => ((Func<List<Diagnostic>, ResolveResult>)((expr_errs) => new ResolveResult(Enumerable.Concat(top.errors, expr_errs).ToList(), top.names, ctors.type_names, ctors.ctor_names)))(resolve_all_defs(sc, mod.defs, 0L, ((long)mod.defs.Count), new List<Diagnostic>()))))(build_all_names_scope(all_top, ctors.ctor_names, builtin_names()))))(Enumerable.Concat(top.names, imported_names).ToList())))(collect_imported_names(imported, 0L, ((long)imported.Count), new List<string>()))))(collect_ctor_names(mod.type_defs, 0L, ((long)mod.type_defs.Count), new List<string>(), new List<string>()))))(collect_top_level_names(mod.defs, 0L, ((long)mod.defs.Count), new List<string>(), new List<Diagnostic>()));
    }

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

    public static long cc_newline()
    {
        return 1L;
    }

    public static long cc_cr()
    {
        return 13L;
    }

    public static long cc_space()
    {
        return 2L;
    }

    public static long cc_double_quote()
    {
        return 72L;
    }

    public static long cc_single_quote()
    {
        return 71L;
    }

    public static long cc_ampersand()
    {
        return 84L;
    }

    public static long cc_left_paren()
    {
        return 74L;
    }

    public static long cc_right_paren()
    {
        return 75L;
    }

    public static long cc_star()
    {
        return 78L;
    }

    public static long cc_plus()
    {
        return 76L;
    }

    public static long cc_comma()
    {
        return 66L;
    }

    public static long cc_minus()
    {
        return 73L;
    }

    public static long cc_dot()
    {
        return 65L;
    }

    public static long cc_slash()
    {
        return 81L;
    }

    public static long cc_zero()
    {
        return 3L;
    }

    public static long cc_nine()
    {
        return 12L;
    }

    public static long cc_colon()
    {
        return 69L;
    }

    public static long cc_less()
    {
        return 79L;
    }

    public static long cc_equals()
    {
        return 77L;
    }

    public static long cc_greater()
    {
        return 80L;
    }

    public static long cc_upper_a()
    {
        return 41L;
    }

    public static long cc_upper_z()
    {
        return 64L;
    }

    public static long cc_left_bracket()
    {
        return 88L;
    }

    public static long cc_backslash()
    {
        return 86L;
    }

    public static long cc_right_bracket()
    {
        return 89L;
    }

    public static long cc_caret()
    {
        return 68L;
    }

    public static long cc_underscore()
    {
        return 85L;
    }

    public static long cc_lower_a()
    {
        return 15L;
    }

    public static long cc_lower_n()
    {
        return 18L;
    }

    public static long cc_lower_r()
    {
        return 21L;
    }

    public static long cc_lower_t()
    {
        return 14L;
    }

    public static long cc_lower_z()
    {
        return 38L;
    }

    public static long cc_left_brace()
    {
        return 90L;
    }

    public static long cc_pipe()
    {
        return 87L;
    }

    public static long cc_right_brace()
    {
        return 91L;
    }

    public static bool is_letter_code(long c)
    {
        return (c >= 13L && c <= 64L);
    }

    public static bool is_digit_code(long c)
    {
        return (c >= 3L && c <= 12L);
    }

    public static LexState make_lex_state(string src)
    {
        return new LexState(src, 0L, 1L, 1L);
    }

    public static bool is_at_end(LexState st)
    {
        return (st.offset >= ((long)st.source.Length));
    }

    public static long peek_code(LexState st)
    {
        return (is_at_end(st) ? 0L : ((long)st.source[(int)st.offset]));
    }

    public static LexState advance_char(LexState st)
    {
        return ((peek_code(st) == cc_newline()) ? new LexState(st.source, (st.offset + 1L), (st.line + 1L), 1L) : new LexState(st.source, (st.offset + 1L), st.line, (st.column + 1L)));
    }

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

    public static LexState skip_spaces(LexState st)
    {
        return ((Func<long, LexState>)((len) => ((Func<long, LexState>)((end) => ((end == st.offset) ? st : new LexState(st.source, end, st.line, (st.column + (end - st.offset))))))(skip_spaces_end(st.source, st.offset, len))))(((long)st.source.Length));
    }

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
                                    if (is_letter_code(((long)source[(int)(offset + 1L)])))
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

    public static LexState scan_ident_rest(LexState st)
    {
        return ((Func<long, LexState>)((len) => ((Func<long, LexState>)((end) => ((end == st.offset) ? st : new LexState(st.source, end, st.line, (st.column + (end - st.offset))))))(scan_ident_end(st.source, st.offset, len))))(((long)st.source.Length));
    }

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

    public static LexState scan_digits(LexState st)
    {
        return ((Func<long, LexState>)((len) => ((Func<long, LexState>)((end) => ((end == st.offset) ? st : new LexState(st.source, end, st.line, (st.column + (end - st.offset))))))(scan_digits_end(st.source, st.offset, len))))(((long)st.source.Length));
    }

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

    public static LexState scan_string_body(LexState st)
    {
        return ((Func<long, LexState>)((len) => ((Func<long, LexState>)((end) => ((end == st.offset) ? st : new LexState(st.source, end, st.line, (st.column + (end - st.offset))))))(scan_string_end(st.source, st.offset, len))))(((long)st.source.Length));
    }

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
                    if (((i + 1L) < len))
                    {
                        var nc = ((long)s[(int)(i + 1L)]);
                        if ((nc == cc_lower_n()))
                        {
                            var _tco_0 = s;
                            var _tco_1 = (i + 2L);
                            var _tco_2 = len;
                            var _tco_3 = string.Concat(acc, ((char)1L).ToString());
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
                                var _tco_3 = string.Concat(acc, "\u0002\u0002");
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
                                        var _tco_3 = string.Concat(acc, "V");
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
                                            var _tco_3 = string.Concat(acc, "H");
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
                                            var _tco_3 = string.Concat(acc, ((char)((long)s[(int)(i + 1L)])).ToString());
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
                        return string.Concat(acc, ((char)((long)s[(int)i])).ToString());
                    }
                }
                else
                {
                    var _tco_0 = s;
                    var _tco_1 = (i + 1L);
                    var _tco_2 = len;
                    var _tco_3 = string.Concat(acc, ((char)((long)s[(int)i])).ToString());
                    s = _tco_0;
                    i = _tco_1;
                    len = _tco_2;
                    acc = _tco_3;
                    continue;
                }
            }
        }
    }

    public static TokenKind classify_word(string w)
    {
        return ((w == "\u0017\u000D\u000E") ? new LetKeyword() : ((w == "\u0011\u0012") ? new InKeyword() : ((w == "\u0011\u001C") ? new IfKeyword() : ((w == "\u000E\u0014\u000D\u0012") ? new ThenKeyword() : ((w == "\u000D\u0017\u0013\u000D") ? new ElseKeyword() : ((w == "\u001B\u0014\u000D\u0012") ? new WhenKeyword() : ((w == "\u001B\u0014\u000D\u0015\u000D") ? new WhereKeyword() : ((w == "\u0016\u0010") ? new DoKeyword() : ((w == "\u0015\u000D\u0018\u0010\u0015\u0016") ? new RecordKeyword() : ((w == "\u0011\u001A\u001F\u0010\u0015\u000E") ? new ImportKeyword() : ((w == "\u000D$\u001F\u0010\u0015\u000E") ? new ExportKeyword() : ((w == "\u0018\u0017\u000F\u0011\u001A") ? new ClaimKeyword() : ((w == "\u001F\u0015\u0010\u0010\u001C") ? new ProofKeyword() : ((w == "\u001C\u0010\u0015\u000F\u0017\u0017") ? new ForAllKeyword() : ((w == "\u000D$\u0011\u0013\u000E\u0013") ? new ThereExistsKeyword() : ((w == "\u0017\u0011\u0012\u000D\u000F\u0015") ? new LinearKeyword() : ((w == "\u000D\u001C\u001C\u000D\u0018\u000E") ? new EffectKeyword() : ((w == "\u001B\u0011\u000E\u0014") ? new WithKeyword() : ((w == "(\u0015\u0019\u000D") ? new TrueKeyword() : ((w == "6\u000F\u0017\u0013\u000D") ? new FalseKeyword() : ((Func<long, TokenKind>)((first_code) => ((first_code >= cc_upper_a()) ? ((first_code <= cc_upper_z()) ? new TypeIdentifier() : new Identifier()) : new Identifier())))(((long)w[(int)0L]))))))))))))))))))))));
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
                            return new LexToken(make_token(new TextLiteral(), process_escapes(raw, 0L, ((long)raw.Length), ""), s), after);
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
                                            return new LexToken(make_token(new Underscore(), "U", s), after);
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

    public static LexResult scan_operator(LexState s)
    {
        return ((Func<long, LexResult>)((c) => ((Func<LexState, LexResult>)((next) => ((c == cc_left_paren()) ? new LexToken(make_token(new LeftParen(), "J", s), next) : ((c == cc_right_paren()) ? new LexToken(make_token(new RightParen(), "K", s), next) : ((c == cc_left_bracket()) ? new LexToken(make_token(new LeftBracket(), "X", s), next) : ((c == cc_right_bracket()) ? new LexToken(make_token(new RightBracket(), "Y", s), next) : ((c == cc_left_brace()) ? new LexToken(make_token(new LeftBrace(), "Z", s), next) : ((c == cc_right_brace()) ? new LexToken(make_token(new RightBrace(), "[", s), next) : ((c == cc_comma()) ? new LexToken(make_token(new Comma(), "B", s), next) : ((c == cc_dot()) ? new LexToken(make_token(new Dot(), "A", s), next) : ((c == cc_caret()) ? new LexToken(make_token(new Caret(), "\u00E0\u0081\u009E", s), next) : ((c == cc_ampersand()) ? new LexToken(make_token(new Ampersand(), "T", s), next) : ((c == cc_backslash()) ? new LexToken(make_token(new Backslash(), "V", s), next) : scan_multi_char_operator(s))))))))))))))(advance_char(s))))(peek_code(s));
    }

    public static LexResult scan_multi_char_operator(LexState s)
    {
        return ((Func<long, LexResult>)((c) => ((Func<LexState, LexResult>)((next) => ((Func<long, LexResult>)((nc) => ((c == cc_plus()) ? ((nc == cc_plus()) ? new LexToken(make_token(new PlusPlus(), "LL", s), advance_char(next)) : new LexToken(make_token(new Plus(), "L", s), next)) : ((c == cc_minus()) ? ((nc == cc_greater()) ? new LexToken(make_token(new Arrow(), "IP", s), advance_char(next)) : new LexToken(make_token(new Minus(), "I", s), next)) : ((c == cc_star()) ? new LexToken(make_token(new Star(), "N", s), next) : ((c == cc_slash()) ? ((nc == cc_equals()) ? new LexToken(make_token(new NotEquals(), "QM", s), advance_char(next)) : new LexToken(make_token(new Slash(), "Q", s), next)) : ((c == cc_equals()) ? ((nc == cc_equals()) ? ((Func<LexState, LexResult>)((next2) => ((Func<long, LexResult>)((nc2) => ((nc2 == cc_equals()) ? new LexToken(make_token(new TripleEquals(), "MMM", s), advance_char(next2)) : new LexToken(make_token(new DoubleEquals(), "MM", s), next2))))((is_at_end(next2) ? 0L : peek_code(next2)))))(advance_char(next)) : new LexToken(make_token(new Equals_(), "M", s), next)) : ((c == cc_colon()) ? ((nc == cc_colon()) ? new LexToken(make_token(new ColonColon(), "EE", s), advance_char(next)) : new LexToken(make_token(new Colon(), "E", s), next)) : ((c == cc_pipe()) ? ((nc == cc_minus()) ? new LexToken(make_token(new Turnstile(), "WI", s), advance_char(next)) : new LexToken(make_token(new Pipe(), "W", s), next)) : ((c == cc_less()) ? ((nc == cc_equals()) ? new LexToken(make_token(new LessOrEqual(), "OM", s), advance_char(next)) : ((nc == cc_minus()) ? new LexToken(make_token(new LeftArrow(), "OI", s), advance_char(next)) : new LexToken(make_token(new LessThan(), "O", s), next))) : ((c == cc_greater()) ? ((nc == cc_equals()) ? new LexToken(make_token(new GreaterOrEqual(), "PM", s), advance_char(next)) : new LexToken(make_token(new GreaterThan(), "P", s), next)) : new LexToken(make_token(new ErrorToken(), ((char)((long)s.source[(int)s.offset])).ToString(), s), next))))))))))))((is_at_end(next) ? 0L : peek_code(next)))))(advance_char(s))))(peek_code(s));
    }

    public static LexResult scan_char_literal(LexState s)
    {
        return ((Func<LexState, LexResult>)((s1) => (is_at_end(s1) ? new LexToken(make_token(new ErrorToken(), "G", s), s1) : ((peek_code(s1) == cc_backslash()) ? ((Func<LexState, LexResult>)((s2) => (is_at_end(s2) ? new LexToken(make_token(new ErrorToken(), "GV", s), s2) : ((Func<long, LexResult>)((esc_code) => ((Func<long, LexResult>)((char_val) => ((Func<LexState, LexResult>)((s3) => ((Func<LexState, LexResult>)((s4) => new LexToken(make_token(new CharLiteral(), _Cce.FromUnicode(char_val.ToString()), s), s4)))((is_at_end(s3) ? s3 : ((peek_code(s3) == cc_single_quote()) ? advance_char(s3) : s3)))))(advance_char(s2))))(((esc_code == cc_lower_n()) ? 10L : ((esc_code == cc_lower_t()) ? 32L : ((esc_code == cc_lower_r()) ? 10L : ((esc_code == cc_backslash()) ? cc_backslash() : ((esc_code == cc_single_quote()) ? cc_single_quote() : esc_code))))))))(peek_code(s2)))))(advance_char(s1)) : ((Func<long, LexResult>)((char_val) => ((Func<LexState, LexResult>)((s2) => ((Func<LexState, LexResult>)((s3) => new LexToken(make_token(new CharLiteral(), _Cce.FromUnicode(char_val.ToString()), s), s3)))((is_at_end(s2) ? s2 : ((peek_code(s2) == cc_single_quote()) ? advance_char(s2) : s2)))))(advance_char(s1))))(peek_code(s1))))))(advance_char(s));
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

    public static List<Token> tokenize(string src)
    {
        return tokenize_loop(make_lex_state(src), new List<Token>());
    }

    public static ParseTypeResult parse_type(ParseState st)
    {
        return ((Func<ParseTypeResult, ParseTypeResult>)((result) => unwrap_type_ok(result, (left) => (st) => parse_type_continue(left, st))))(parse_type_atom(st));
    }

    public static ParseTypeResult parse_type_continue(TypeExpr left, ParseState st)
    {
        return (is_arrow(current_kind(st)) ? ((Func<ParseState, ParseTypeResult>)((st2) => ((Func<ParseTypeResult, ParseTypeResult>)((right_result) => unwrap_type_ok(right_result, (right) => (st) => make_fun_type(left, right, st))))(parse_type(st2))))(advance(st)) : new TypeOk(left, st));
    }

    public static ParseTypeResult make_fun_type(TypeExpr left, TypeExpr right, ParseState st)
    {
        return new TypeOk(new FunType(left, right), st);
    }

    public static ParseTypeResult unwrap_type_ok(ParseTypeResult r, Func<TypeExpr, Func<ParseState, ParseTypeResult>> f)
    {
        return (r is TypeOk _mTypeOk69_ ? ((Func<ParseState, ParseTypeResult>)((st) => ((Func<TypeExpr, ParseTypeResult>)((t) => f(t)(st)))((TypeExpr)_mTypeOk69_.Field0)))((ParseState)_mTypeOk69_.Field1) : throw new InvalidOperationException("Non-exhaustive match"));
    }

    public static ParseTypeResult parse_type_atom(ParseState st)
    {
        return (is_ident(current_kind(st)) ? ((Func<Token, ParseTypeResult>)((tok) => parse_type_args(new NamedType(tok), advance(st))))(current(st)) : (is_type_ident(current_kind(st)) ? ((Func<Token, ParseTypeResult>)((tok) => parse_type_args(new NamedType(tok), advance(st))))(current(st)) : (is_left_paren(current_kind(st)) ? parse_paren_type(advance(st)) : (is_left_bracket(current_kind(st)) ? parse_effect_type(advance(st)) : ((Func<Token, ParseTypeResult>)((tok) => new TypeOk(new NamedType(tok), advance(st))))(current(st))))));
    }

    public static ParseTypeResult parse_paren_type(ParseState st)
    {
        return ((Func<ParseTypeResult, ParseTypeResult>)((inner) => unwrap_type_ok(inner, (t) => (st) => finish_paren_type(t, st))))(parse_type(st));
    }

    public static ParseTypeResult finish_paren_type(TypeExpr t, ParseState st)
    {
        return ((Func<ParseState, ParseTypeResult>)((st2) => new TypeOk(new ParenType(t), st2)))(expect(new RightParen(), st));
    }

    public static ParseTypeResult parse_effect_type(ParseState st)
    {
        return ((Func<ParseState, ParseTypeResult>)((st2) => parse_type(st2)))(skip_effect_contents(st));
    }

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

    public static ParseTypeResult parse_type_args(TypeExpr base_type, ParseState st)
    {
        return (is_done(st) ? new TypeOk(base_type, st) : (is_type_arg_start(current_kind(st)) ? parse_type_arg_next(base_type, st) : new TypeOk(base_type, st)));
    }

    public static ParseTypeResult parse_type_arg_next(TypeExpr base_type, ParseState st)
    {
        return ((Func<ParseTypeResult, ParseTypeResult>)((arg_result) => unwrap_type_ok(arg_result, (arg) => (st) => continue_type_args(base_type, arg, st))))(parse_type_atom(st));
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
        return (is_left_paren(current_kind(st)) ? ((Func<ParseState, ParsePatResult>)((st2) => ((Func<ParsePatResult, ParsePatResult>)((sub) => unwrap_pat_ok(sub, (p) => (st) => continue_ctor_fields(ctor, acc, p, st))))(parse_pattern(st2))))(advance(st)) : new PatOk(new CtorPat(ctor, acc), st));
    }

    public static ParsePatResult continue_ctor_fields(Token ctor, List<Pat> acc, Pat p, ParseState st)
    {
        return ((Func<ParseState, ParsePatResult>)((st2) => parse_ctor_pattern_fields(ctor, ((Func<List<Pat>>)(() => { var _l = acc; _l.Add(p); return _l; }))(), st2)))(expect(new RightParen(), st));
    }

    public static ParsePatResult unwrap_pat_ok(ParsePatResult r, Func<Pat, Func<ParseState, ParsePatResult>> f)
    {
        return (r is PatOk _mPatOk70_ ? ((Func<ParseState, ParsePatResult>)((st) => ((Func<Pat, ParsePatResult>)((p) => f(p)(st)))((Pat)_mPatOk70_.Field0)))((ParseState)_mPatOk70_.Field1) : throw new InvalidOperationException("Non-exhaustive match"));
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
        return (r is TypeOk _mTypeOk71_ ? ((Func<ParseState, ParseDefResult>)((st) => ((Func<TypeExpr, ParseDefResult>)((ann_type) => ((Func<Token, ParseDefResult>)((name_tok) => ((Func<List<TypeAnn>, ParseDefResult>)((ann) => ((Func<ParseState, ParseDefResult>)((st2) => parse_def_body_with_ann(ann, st2)))(skip_newlines(st))))(new List<TypeAnn>() { new TypeAnn(name_tok, ann_type) })))(new Token(new Identifier(), "", 0L, 0L, 0L))))((TypeExpr)_mTypeOk71_.Field0)))((ParseState)_mTypeOk71_.Field1) : throw new InvalidOperationException("Non-exhaustive match"));
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

    public static ParseDefResult finish_def(List<TypeAnn> ann, Token name_tok, List<Token> @params, ParseState st)
    {
        return ((Func<ParseState, ParseDefResult>)((st2) => ((Func<ParseState, ParseDefResult>)((st3) => ((Func<ParseExprResult, ParseDefResult>)((body_result) => unwrap_def_body(body_result, ann, name_tok, @params)))(parse_expr(st3))))(skip_newlines(st2))))(expect(new Equals_(), st));
    }

    public static ParseDefResult unwrap_def_body(ParseExprResult r, List<TypeAnn> ann, Token name_tok, List<Token> @params)
    {
        return (r is ExprOk _mExprOk72_ ? ((Func<ParseState, ParseDefResult>)((st) => ((Func<Expr, ParseDefResult>)((b) => new DefOk(new Def(name_tok, @params, ann, b), st)))((Expr)_mExprOk72_.Field0)))((ParseState)_mExprOk72_.Field1) : throw new InvalidOperationException("Non-exhaustive match"));
    }

    public static bool is_paren_type_param(ParseState st)
    {
        return (is_left_paren(current_kind(st)) ? ((Func<TokenKind, bool>)((k1) => (is_ident(k1) ? is_right_paren(peek_kind(st, 2L)) : (is_type_ident(k1) ? is_right_paren(peek_kind(st, 2L)) : false))))(peek_kind(st, 1L)) : false);
    }

    public static bool is_type_param_pattern(ParseState st)
    {
        return (is_paren_type_param(st) ? true : is_ident(current_kind(st)));
    }

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

    public static ParseTypeDefResult parse_type_def(ParseState st)
    {
        return (is_type_ident(current_kind(st)) ? ((Func<Token, ParseTypeDefResult>)((name_tok) => ((Func<ParseState, ParseTypeDefResult>)((st2) => ((Func<List<Token>, ParseTypeDefResult>)((tparams) => ((Func<ParseState, ParseTypeDefResult>)((st3) => (is_equals(current_kind(st3)) ? ((Func<ParseState, ParseTypeDefResult>)((st4) => (is_record_keyword(current_kind(st4)) ? parse_record_type(name_tok, tparams, st4) : (is_pipe(current_kind(st4)) ? parse_variant_type(name_tok, tparams, st4) : ((is_type_ident(current_kind(st4)) && looks_like_variant(st4)) ? parse_variant_type(name_tok, tparams, st4) : new TypeDefNone(st))))))(skip_newlines(advance(st3))) : new TypeDefNone(st))))(parse_type_params(st2, new List<Token>()))))(collect_type_params(st2, new List<Token>()))))(advance(st))))(current(st)) : new TypeDefNone(st));
    }

    public static ParseTypeDefResult parse_record_type(Token name_tok, List<Token> tparams, ParseState st)
    {
        return ((Func<ParseState, ParseTypeDefResult>)((st2) => ((Func<ParseState, ParseTypeDefResult>)((st3) => ((Func<ParseState, ParseTypeDefResult>)((st4) => parse_record_fields_loop(name_tok, tparams, new List<RecordFieldDef>(), st4)))(skip_newlines(st3))))(expect(new LeftBrace(), st2))))(advance(st));
    }

    public static ParseTypeDefResult parse_record_fields_loop(Token name_tok, List<Token> tparams, List<RecordFieldDef> acc, ParseState st)
    {
        return (is_right_brace(current_kind(st)) ? new TypeDefOk(new TypeDef(name_tok, tparams, new RecordBody(acc)), advance(st)) : (is_ident(current_kind(st)) ? parse_one_record_field(name_tok, tparams, acc, st) : new TypeDefOk(new TypeDef(name_tok, tparams, new RecordBody(acc)), st)));
    }

    public static ParseTypeDefResult parse_one_record_field(Token name_tok, List<Token> tparams, List<RecordFieldDef> acc, ParseState st)
    {
        return ((Func<Token, ParseTypeDefResult>)((field_name) => ((Func<ParseState, ParseTypeDefResult>)((st2) => ((Func<ParseState, ParseTypeDefResult>)((st3) => ((Func<ParseTypeResult, ParseTypeDefResult>)((field_type_result) => unwrap_record_field_type(name_tok, tparams, acc, field_name, field_type_result)))(parse_type(st3))))(expect(new Colon(), st2))))(advance(st))))(current(st));
    }

    public static ParseTypeDefResult unwrap_record_field_type(Token name_tok, List<Token> tparams, List<RecordFieldDef> acc, Token field_name, ParseTypeResult r)
    {
        return (r is TypeOk _mTypeOk73_ ? ((Func<ParseState, ParseTypeDefResult>)((st) => ((Func<TypeExpr, ParseTypeDefResult>)((ft) => ((Func<RecordFieldDef, ParseTypeDefResult>)((field) => ((Func<ParseState, ParseTypeDefResult>)((st2) => (is_comma(current_kind(st2)) ? parse_record_fields_loop(name_tok, tparams, ((Func<List<RecordFieldDef>>)(() => { var _l = acc; _l.Add(field); return _l; }))(), skip_newlines(advance(st2))) : parse_record_fields_loop(name_tok, tparams, ((Func<List<RecordFieldDef>>)(() => { var _l = acc; _l.Add(field); return _l; }))(), st2))))(skip_newlines(st))))(new RecordFieldDef(field_name, ft))))((TypeExpr)_mTypeOk73_.Field0)))((ParseState)_mTypeOk73_.Field1) : throw new InvalidOperationException("Non-exhaustive match"));
    }

    public static bool looks_like_variant(ParseState st)
    {
        return looks_like_variant_scan(st.tokens, (st.pos + 1L), ((long)st.tokens.Count));
    }

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
                        var _ = _tco_s;
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

    public static ParseTypeDefResult parse_variant_type(Token name_tok, List<Token> tparams, ParseState st)
    {
        return (is_type_ident(current_kind(st)) ? ((Func<Token, ParseTypeDefResult>)((ctor_name) => ((Func<ParseState, ParseTypeDefResult>)((st2) => parse_ctor_fields(ctor_name, new List<TypeExpr>(), st2, name_tok, tparams, new List<VariantCtorDef>())))(advance(st))))(current(st)) : parse_variant_ctors(name_tok, tparams, new List<VariantCtorDef>(), st));
    }

    public static ParseTypeDefResult parse_variant_ctors(Token name_tok, List<Token> tparams, List<VariantCtorDef> acc, ParseState st)
    {
        return (is_pipe(current_kind(st)) ? ((Func<ParseState, ParseTypeDefResult>)((st2) => ((Func<Token, ParseTypeDefResult>)((ctor_name) => ((Func<ParseState, ParseTypeDefResult>)((st3) => parse_ctor_fields(ctor_name, new List<TypeExpr>(), st3, name_tok, tparams, acc)))(advance(st2))))(current(st2))))(skip_newlines(advance(st))) : new TypeDefOk(new TypeDef(name_tok, tparams, new VariantBody(acc)), st));
    }

    public static ParseTypeDefResult parse_ctor_fields(Token ctor_name, List<TypeExpr> fields, ParseState st, Token name_tok, List<Token> tparams, List<VariantCtorDef> acc)
    {
        return (is_left_paren(current_kind(st)) ? ((Func<ParseTypeResult, ParseTypeDefResult>)((field_result) => unwrap_ctor_field(field_result, ctor_name, fields, name_tok, tparams, acc)))(parse_type(advance(st))) : ((Func<ParseState, ParseTypeDefResult>)((st2) => ((Func<VariantCtorDef, ParseTypeDefResult>)((ctor) => parse_variant_ctors(name_tok, tparams, ((Func<List<VariantCtorDef>>)(() => { var _l = acc; _l.Add(ctor); return _l; }))(), st2)))(new VariantCtorDef(ctor_name, fields))))(skip_newlines(st)));
    }

    public static ParseTypeDefResult unwrap_ctor_field(ParseTypeResult r, Token ctor_name, List<TypeExpr> fields, Token name_tok, List<Token> tparams, List<VariantCtorDef> acc)
    {
        return (r is TypeOk _mTypeOk74_ ? ((Func<ParseState, ParseTypeDefResult>)((st) => ((Func<TypeExpr, ParseTypeDefResult>)((ty) => ((Func<ParseState, ParseTypeDefResult>)((st2) => parse_ctor_fields(ctor_name, Enumerable.Concat(fields, new List<TypeExpr>() { ty }).ToList(), st2, name_tok, tparams, acc)))(expect(new RightParen(), st))))((TypeExpr)_mTypeOk74_.Field0)))((ParseState)_mTypeOk74_.Field1) : throw new InvalidOperationException("Non-exhaustive match"));
    }

    public static Document parse_document(ParseState st)
    {
        return ((Func<ParseState, Document>)((st2) => ((Func<ImportParseResult, Document>)((imp_result) => parse_top_level(new List<Def>(), new List<TypeDef>(), new List<EffectDef>(), imp_result.imports, imp_result.state)))(parse_imports(st2, new List<ImportDecl>()))))(skip_newlines(st));
    }

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
                var _tco_1 = ((Func<List<ImportDecl>>)(() => { var _l = acc; _l.Add(new ImportDecl(name_tok)); return _l; }))();
                st = _tco_0;
                acc = _tco_1;
                continue;
            }
            else
            {
                return new ImportParseResult(acc, st);
            }
        }
    }

    public static Document parse_top_level(List<Def> defs, List<TypeDef> type_defs, List<EffectDef> effect_defs, List<ImportDecl> imports, ParseState st)
    {
        return (is_done(st) ? new Document(defs, type_defs, effect_defs, imports) : (is_effect_keyword(current_kind(st)) ? parse_top_level_effect(defs, type_defs, effect_defs, imports, st) : try_top_level_type_def(defs, type_defs, effect_defs, imports, st)));
    }

    public static Document parse_top_level_effect(List<Def> defs, List<TypeDef> type_defs, List<EffectDef> effect_defs, List<ImportDecl> imports, ParseState st)
    {
        return ((Func<ParseState, Document>)((st1) => ((Func<Token, Document>)((name_tok) => ((Func<ParseState, Document>)((st2) => ((Func<ParseState, Document>)((st3) => ((Func<EffectOpsResult, Document>)((ops) => ((Func<EffectDef, Document>)((ed) => parse_top_level(defs, type_defs, Enumerable.Concat(effect_defs, new List<EffectDef>() { ed }).ToList(), imports, skip_newlines(ops.state))))(new EffectDef(name_tok, ops.ops))))(parse_effect_ops(st3, new List<EffectOpDef>()))))((is_where_keyword(current_kind(st2)) ? skip_newlines(advance(st2)) : st2))))(advance(st1))))(current(st1))))(advance(st));
    }

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
                        var op = new EffectOpDef(op_tok, ty);
                        var _tco_0 = skip_newlines(st3);
                        var _tco_1 = ((Func<List<EffectOpDef>>)(() => { var _l = acc; _l.Add(op); return _l; }))();
                        st = _tco_0;
                        acc = _tco_1;
                        continue;
                    }
                }
                else
                {
                    return new EffectOpsResult(acc, st);
                }
            }
            else
            {
                return new EffectOpsResult(acc, st);
            }
        }
    }

    public static Document try_top_level_type_def(List<Def> defs, List<TypeDef> type_defs, List<EffectDef> effect_defs, List<ImportDecl> imports, ParseState st)
    {
        return ((Func<ParseTypeDefResult, Document>)((td_result) => ((Func<ParseTypeDefResult, Document>)((_scrutinee75_) => (_scrutinee75_ is TypeDefOk _mTypeDefOk75_ ? ((Func<ParseState, Document>)((st2) => ((Func<TypeDef, Document>)((td) => parse_top_level(defs, Enumerable.Concat(type_defs, new List<TypeDef>() { td }).ToList(), effect_defs, imports, skip_newlines(st2))))((TypeDef)_mTypeDefOk75_.Field0)))((ParseState)_mTypeDefOk75_.Field1) : (_scrutinee75_ is TypeDefNone _mTypeDefNone75_ ? ((Func<ParseState, Document>)((st2) => try_top_level_def(defs, type_defs, effect_defs, imports, st)))((ParseState)_mTypeDefNone75_.Field0) : throw new InvalidOperationException("Non-exhaustive match")))))(td_result)))(parse_type_def(st));
    }

    public static Document try_top_level_def(List<Def> defs, List<TypeDef> type_defs, List<EffectDef> effect_defs, List<ImportDecl> imports, ParseState st)
    {
        return ((Func<ParseDefResult, Document>)((def_result) => ((Func<ParseDefResult, Document>)((_scrutinee76_) => (_scrutinee76_ is DefOk _mDefOk76_ ? ((Func<ParseState, Document>)((st2) => ((Func<Def, Document>)((d) => parse_top_level(Enumerable.Concat(defs, new List<Def>() { d }).ToList(), type_defs, effect_defs, imports, skip_newlines(st2))))((Def)_mDefOk76_.Field0)))((ParseState)_mDefOk76_.Field1) : (_scrutinee76_ is DefNone _mDefNone76_ ? ((Func<ParseState, Document>)((st2) => parse_top_level(defs, type_defs, effect_defs, imports, skip_newlines(advance(st2)))))((ParseState)_mDefNone76_.Field0) : throw new InvalidOperationException("Non-exhaustive match")))))(def_result)))(parse_definition(st));
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
        return ((st.pos >= (((long)st.tokens.Count) - 1L)) ? st : new ParseState(st.tokens, (st.pos + 1L)));
    }

    public static bool is_done(ParseState st)
    {
        return (current_kind(st) is EndOfFile _mEndOfFile77_ ? true : ((Func<TokenKind, bool>)((_) => false))(current_kind(st)));
    }

    public static TokenKind peek_kind(ParseState st, long offset)
    {
        return ((Func<long, TokenKind>)((idx) => ((idx >= ((long)st.tokens.Count)) ? new EndOfFile() : st.tokens[(int)idx].kind)))((st.pos + offset));
    }

    public static bool is_ident(TokenKind k)
    {
        return (k is Identifier _mIdentifier78_ ? true : ((Func<TokenKind, bool>)((_) => false))(k));
    }

    public static bool is_type_ident(TokenKind k)
    {
        return (k is TypeIdentifier _mTypeIdentifier79_ ? true : ((Func<TokenKind, bool>)((_) => false))(k));
    }

    public static bool is_arrow(TokenKind k)
    {
        return (k is Arrow _mArrow80_ ? true : ((Func<TokenKind, bool>)((_) => false))(k));
    }

    public static bool is_equals(TokenKind k)
    {
        return (k is Equals_ _mEquals_81_ ? true : ((Func<TokenKind, bool>)((_) => false))(k));
    }

    public static bool is_colon(TokenKind k)
    {
        return (k is Colon _mColon82_ ? true : ((Func<TokenKind, bool>)((_) => false))(k));
    }

    public static bool is_comma(TokenKind k)
    {
        return (k is Comma _mComma83_ ? true : ((Func<TokenKind, bool>)((_) => false))(k));
    }

    public static bool is_pipe(TokenKind k)
    {
        return (k is Pipe _mPipe84_ ? true : ((Func<TokenKind, bool>)((_) => false))(k));
    }

    public static bool is_dot(TokenKind k)
    {
        return (k is Dot _mDot85_ ? true : ((Func<TokenKind, bool>)((_) => false))(k));
    }

    public static bool is_left_paren(TokenKind k)
    {
        return (k is LeftParen _mLeftParen86_ ? true : ((Func<TokenKind, bool>)((_) => false))(k));
    }

    public static bool is_left_brace(TokenKind k)
    {
        return (k is LeftBrace _mLeftBrace87_ ? true : ((Func<TokenKind, bool>)((_) => false))(k));
    }

    public static bool is_left_bracket(TokenKind k)
    {
        return (k is LeftBracket _mLeftBracket88_ ? true : ((Func<TokenKind, bool>)((_) => false))(k));
    }

    public static bool is_right_brace(TokenKind k)
    {
        return (k is RightBrace _mRightBrace89_ ? true : ((Func<TokenKind, bool>)((_) => false))(k));
    }

    public static bool is_right_bracket(TokenKind k)
    {
        return (k is RightBracket _mRightBracket90_ ? true : ((Func<TokenKind, bool>)((_) => false))(k));
    }

    public static bool is_right_paren(TokenKind k)
    {
        return (k is RightParen _mRightParen91_ ? true : ((Func<TokenKind, bool>)((_) => false))(k));
    }

    public static bool is_if_keyword(TokenKind k)
    {
        return (k is IfKeyword _mIfKeyword92_ ? true : ((Func<TokenKind, bool>)((_) => false))(k));
    }

    public static bool is_let_keyword(TokenKind k)
    {
        return (k is LetKeyword _mLetKeyword93_ ? true : ((Func<TokenKind, bool>)((_) => false))(k));
    }

    public static bool is_when_keyword(TokenKind k)
    {
        return (k is WhenKeyword _mWhenKeyword94_ ? true : ((Func<TokenKind, bool>)((_) => false))(k));
    }

    public static bool is_do_keyword(TokenKind k)
    {
        return (k is DoKeyword _mDoKeyword95_ ? true : ((Func<TokenKind, bool>)((_) => false))(k));
    }

    public static bool is_with_keyword(TokenKind k)
    {
        return (k is WithKeyword _mWithKeyword96_ ? true : ((Func<TokenKind, bool>)((_) => false))(k));
    }

    public static bool is_effect_keyword(TokenKind k)
    {
        return (k is EffectKeyword _mEffectKeyword97_ ? true : ((Func<TokenKind, bool>)((_) => false))(k));
    }

    public static bool is_import_keyword(TokenKind k)
    {
        return (k is ImportKeyword _mImportKeyword98_ ? true : ((Func<TokenKind, bool>)((_) => false))(k));
    }

    public static bool is_where_keyword(TokenKind k)
    {
        return (k is WhereKeyword _mWhereKeyword99_ ? true : ((Func<TokenKind, bool>)((_) => false))(k));
    }

    public static bool is_in_keyword(TokenKind k)
    {
        return (k is InKeyword _mInKeyword100_ ? true : ((Func<TokenKind, bool>)((_) => false))(k));
    }

    public static bool is_minus(TokenKind k)
    {
        return (k is Minus _mMinus101_ ? true : ((Func<TokenKind, bool>)((_) => false))(k));
    }

    public static bool is_dedent(TokenKind k)
    {
        return (k is Dedent _mDedent102_ ? true : ((Func<TokenKind, bool>)((_) => false))(k));
    }

    public static bool is_left_arrow(TokenKind k)
    {
        return (k is LeftArrow _mLeftArrow103_ ? true : ((Func<TokenKind, bool>)((_) => false))(k));
    }

    public static bool is_record_keyword(TokenKind k)
    {
        return (k is RecordKeyword _mRecordKeyword104_ ? true : ((Func<TokenKind, bool>)((_) => false))(k));
    }

    public static bool is_underscore(TokenKind k)
    {
        return (k is Underscore _mUnderscore105_ ? true : ((Func<TokenKind, bool>)((_) => false))(k));
    }

    public static bool is_backslash(TokenKind k)
    {
        return (k is Backslash _mBackslash106_ ? true : ((Func<TokenKind, bool>)((_) => false))(k));
    }

    public static bool is_literal(TokenKind k)
    {
        return ((Func<TokenKind, bool>)((_scrutinee107_) => (_scrutinee107_ is IntegerLiteral _mIntegerLiteral107_ ? true : (_scrutinee107_ is NumberLiteral _mNumberLiteral107_ ? true : (_scrutinee107_ is TextLiteral _mTextLiteral107_ ? true : (_scrutinee107_ is CharLiteral _mCharLiteral107_ ? true : (_scrutinee107_ is TrueKeyword _mTrueKeyword107_ ? true : (_scrutinee107_ is FalseKeyword _mFalseKeyword107_ ? true : ((Func<TokenKind, bool>)((_) => false))(_scrutinee107_)))))))))(k);
    }

    public static bool is_app_start(TokenKind k)
    {
        return ((Func<TokenKind, bool>)((_scrutinee108_) => (_scrutinee108_ is Identifier _mIdentifier108_ ? true : (_scrutinee108_ is TypeIdentifier _mTypeIdentifier108_ ? true : (_scrutinee108_ is IntegerLiteral _mIntegerLiteral108_ ? true : (_scrutinee108_ is NumberLiteral _mNumberLiteral108_ ? true : (_scrutinee108_ is TextLiteral _mTextLiteral108_ ? true : (_scrutinee108_ is CharLiteral _mCharLiteral108_ ? true : (_scrutinee108_ is TrueKeyword _mTrueKeyword108_ ? true : (_scrutinee108_ is FalseKeyword _mFalseKeyword108_ ? true : (_scrutinee108_ is LeftParen _mLeftParen108_ ? true : (_scrutinee108_ is LeftBracket _mLeftBracket108_ ? true : ((Func<TokenKind, bool>)((_) => false))(_scrutinee108_)))))))))))))(k);
    }

    public static bool is_compound(Expr e)
    {
        return ((Func<Expr, bool>)((_scrutinee109_) => (_scrutinee109_ is MatchExpr _mMatchExpr109_ ? ((Func<List<MatchArm>, bool>)((arms) => ((Func<Expr, bool>)((s) => true))((Expr)_mMatchExpr109_.Field0)))((List<MatchArm>)_mMatchExpr109_.Field1) : (_scrutinee109_ is IfExpr _mIfExpr109_ ? ((Func<Expr, bool>)((el) => ((Func<Expr, bool>)((t) => ((Func<Expr, bool>)((c) => true))((Expr)_mIfExpr109_.Field0)))((Expr)_mIfExpr109_.Field1)))((Expr)_mIfExpr109_.Field2) : (_scrutinee109_ is LetExpr _mLetExpr109_ ? ((Func<Expr, bool>)((body) => ((Func<List<LetBind>, bool>)((binds) => true))((List<LetBind>)_mLetExpr109_.Field0)))((Expr)_mLetExpr109_.Field1) : (_scrutinee109_ is DoExpr _mDoExpr109_ ? ((Func<List<DoStmt>, bool>)((stmts) => true))((List<DoStmt>)_mDoExpr109_.Field0) : ((Func<Expr, bool>)((_) => false))(_scrutinee109_)))))))(e);
    }

    public static bool is_type_arg_start(TokenKind k)
    {
        return ((Func<TokenKind, bool>)((_scrutinee110_) => (_scrutinee110_ is TypeIdentifier _mTypeIdentifier110_ ? true : (_scrutinee110_ is Identifier _mIdentifier110_ ? true : (_scrutinee110_ is LeftParen _mLeftParen110_ ? true : ((Func<TokenKind, bool>)((_) => false))(_scrutinee110_))))))(k);
    }

    public static long operator_precedence(TokenKind k)
    {
        return ((Func<TokenKind, long>)((_scrutinee111_) => (_scrutinee111_ is PlusPlus _mPlusPlus111_ ? 5L : (_scrutinee111_ is ColonColon _mColonColon111_ ? 5L : (_scrutinee111_ is Plus _mPlus111_ ? 6L : (_scrutinee111_ is Minus _mMinus111_ ? 6L : (_scrutinee111_ is Star _mStar111_ ? 7L : (_scrutinee111_ is Slash _mSlash111_ ? 7L : (_scrutinee111_ is Caret _mCaret111_ ? 8L : (_scrutinee111_ is DoubleEquals _mDoubleEquals111_ ? 4L : (_scrutinee111_ is NotEquals _mNotEquals111_ ? 4L : (_scrutinee111_ is LessThan _mLessThan111_ ? 4L : (_scrutinee111_ is GreaterThan _mGreaterThan111_ ? 4L : (_scrutinee111_ is LessOrEqual _mLessOrEqual111_ ? 4L : (_scrutinee111_ is GreaterOrEqual _mGreaterOrEqual111_ ? 4L : (_scrutinee111_ is TripleEquals _mTripleEquals111_ ? 4L : (_scrutinee111_ is Ampersand _mAmpersand111_ ? 3L : (_scrutinee111_ is Pipe _mPipe111_ ? 2L : ((Func<TokenKind, long>)((_) => (0L - 1L)))(_scrutinee111_)))))))))))))))))))(k);
    }

    public static bool is_right_assoc(TokenKind k)
    {
        return ((Func<TokenKind, bool>)((_scrutinee112_) => (_scrutinee112_ is PlusPlus _mPlusPlus112_ ? true : (_scrutinee112_ is ColonColon _mColonColon112_ ? true : (_scrutinee112_ is Caret _mCaret112_ ? true : (_scrutinee112_ is Arrow _mArrow112_ ? true : ((Func<TokenKind, bool>)((_) => false))(_scrutinee112_)))))))(k);
    }

    public static ParseState expect(TokenKind kind, ParseState st)
    {
        return (is_done(st) ? st : advance(st));
    }

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
                    var _ = _tco_s;
                    return pos;
                }
            }
        }
    }

    public static ParseState skip_newlines(ParseState st)
    {
        return ((Func<long, ParseState>)((end_pos) => ((end_pos == st.pos) ? st : new ParseState(st.tokens, end_pos))))(skip_newlines_pos(st.tokens, st.pos, ((long)st.tokens.Count)));
    }

    public static ParseExprResult parse_expr(ParseState st)
    {
        return parse_binary(st, 0L);
    }

    public static ParseExprResult unwrap_expr_ok(ParseExprResult r, Func<Expr, Func<ParseState, ParseExprResult>> f)
    {
        return (r is ExprOk _mExprOk113_ ? ((Func<ParseState, ParseExprResult>)((st) => ((Func<Expr, ParseExprResult>)((e) => f(e)(st)))((Expr)_mExprOk113_.Field0)))((ParseState)_mExprOk113_.Field1) : throw new InvalidOperationException("Non-exhaustive match"));
    }

    public static ParseExprResult parse_binary(ParseState st, long min_prec)
    {
        return ((Func<ParseExprResult, ParseExprResult>)((left_result) => unwrap_expr_ok(left_result, (left) => (st) => start_binary_loop(min_prec, left, st))))(parse_unary(st));
    }

    public static ParseExprResult start_binary_loop(long min_prec, Expr left, ParseState st)
    {
        return parse_binary_loop(left, st, min_prec);
    }

    public static ParseExprResult parse_binary_loop(Expr left, ParseState st, long min_prec)
    {
        return (is_done(st) ? new ExprOk(left, st) : ((Func<long, ParseExprResult>)((prec) => ((prec < min_prec) ? new ExprOk(left, st) : ((Func<Token, ParseExprResult>)((op) => ((Func<ParseState, ParseExprResult>)((st2) => ((Func<long, ParseExprResult>)((next_min) => ((Func<ParseExprResult, ParseExprResult>)((right_result) => unwrap_expr_ok(right_result, (right) => (st) => continue_binary(left, op, min_prec, right, st))))(parse_binary(st2, next_min))))((is_right_assoc(op.kind) ? prec : (prec + 1L)))))(skip_newlines(advance(st)))))(current(st)))))(operator_precedence(current_kind(st))));
    }

    public static ParseExprResult continue_binary(Expr left, Token op, long min_prec, Expr right, ParseState st)
    {
        return parse_binary_loop(new BinExpr(left, op, right), st, min_prec);
    }

    public static ParseExprResult parse_unary(ParseState st)
    {
        return (is_minus(current_kind(st)) ? ((Func<Token, ParseExprResult>)((op) => ((Func<ParseExprResult, ParseExprResult>)((result) => unwrap_expr_ok(result, (operand) => (st) => finish_unary(op, operand, st))))(parse_unary(advance(st)))))(current(st)) : parse_application(st));
    }

    public static ParseExprResult finish_unary(Token op, Expr operand, ParseState st)
    {
        return new ExprOk(new UnaryExpr(op, operand), st);
    }

    public static ParseExprResult parse_application(ParseState st)
    {
        return ((Func<ParseExprResult, ParseExprResult>)((func_result) => unwrap_expr_ok(func_result, (func) => (st) => parse_app_loop(func, st))))(parse_atom(st));
    }

    public static ParseExprResult parse_app_loop(Expr func, ParseState st)
    {
        return (is_compound(func) ? parse_dot_only(func, st) : (is_done(st) ? new ExprOk(func, st) : (is_app_start(current_kind(st)) ? ((Func<ParseExprResult, ParseExprResult>)((arg_result) => unwrap_expr_ok(arg_result, (arg) => (st) => continue_app(func, arg, st))))(parse_atom(st)) : parse_field_access(func, st))));
    }

    public static ParseExprResult continue_app(Expr func, Expr arg, ParseState st)
    {
        return parse_app_loop(new AppExpr(func, arg), st);
    }

    public static ParseExprResult parse_atom(ParseState st)
    {
        return (is_literal(current_kind(st)) ? new ExprOk(new LitExpr(current(st)), advance(st)) : (is_ident(current_kind(st)) ? parse_field_access(new NameExpr(current(st)), advance(st)) : (is_type_ident(current_kind(st)) ? parse_atom_type_ident(st) : (is_left_paren(current_kind(st)) ? parse_paren_expr(advance(st)) : (is_left_bracket(current_kind(st)) ? parse_list_expr(st) : (is_if_keyword(current_kind(st)) ? parse_if_expr(st) : (is_let_keyword(current_kind(st)) ? parse_let_expr(st) : (is_when_keyword(current_kind(st)) ? parse_match_expr(st) : (is_do_keyword(current_kind(st)) ? parse_do_expr(st) : (is_with_keyword(current_kind(st)) ? parse_handle_expr(st) : (is_backslash(current_kind(st)) ? parse_lambda_expr(st) : new ExprOk(new ErrExpr(current(st)), advance(st)))))))))))));
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

    public static ParseExprResult parse_atom_type_ident(ParseState st)
    {
        return ((Func<Token, ParseExprResult>)((tok) => ((Func<ParseState, ParseExprResult>)((st2) => (is_left_brace(current_kind(st2)) ? parse_record_expr(tok, st2) : new ExprOk(new NameExpr(tok), st2))))(advance(st))))(current(st));
    }

    public static ParseExprResult parse_paren_expr(ParseState st)
    {
        return ((Func<ParseState, ParseExprResult>)((st2) => ((Func<ParseExprResult, ParseExprResult>)((inner) => unwrap_expr_ok(inner, (e) => (st) => finish_paren_expr(e, st))))(parse_expr(st2))))(skip_newlines(st));
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
        return ((Func<Token, ParseExprResult>)((field_name) => ((Func<ParseState, ParseExprResult>)((st2) => ((Func<ParseState, ParseExprResult>)((st3) => ((Func<ParseExprResult, ParseExprResult>)((val_result) => unwrap_expr_ok(val_result, (v) => (st) => finish_record_field(type_name, acc, field_name, v, st))))(parse_expr(st3))))(expect(new Equals_(), st2))))(advance(st))))(current(st));
    }

    public static ParseExprResult finish_record_field(Token type_name, List<RecordFieldExpr> acc, Token field_name, Expr v, ParseState st)
    {
        return ((Func<RecordFieldExpr, ParseExprResult>)((field) => ((Func<ParseState, ParseExprResult>)((st2) => (is_comma(current_kind(st2)) ? parse_record_expr_fields(type_name, ((Func<List<RecordFieldExpr>>)(() => { var _l = acc; _l.Add(field); return _l; }))(), skip_newlines(advance(st2))) : parse_record_expr_fields(type_name, ((Func<List<RecordFieldExpr>>)(() => { var _l = acc; _l.Add(field); return _l; }))(), st2))))(skip_newlines(st))))(new RecordFieldExpr(field_name, v));
    }

    public static ParseExprResult parse_list_expr(ParseState st)
    {
        return ((Func<ParseState, ParseExprResult>)((st2) => ((Func<ParseState, ParseExprResult>)((st3) => parse_list_elements(new List<Expr>(), st3)))(skip_newlines(st2))))(advance(st));
    }

    public static ParseExprResult parse_list_elements(List<Expr> acc, ParseState st)
    {
        return (is_right_bracket(current_kind(st)) ? new ExprOk(new ListExpr(acc), advance(st)) : ((Func<ParseExprResult, ParseExprResult>)((elem) => unwrap_expr_ok(elem, (e) => (st) => finish_list_element(acc, e, st))))(parse_expr(st)));
    }

    public static ParseExprResult finish_list_element(List<Expr> acc, Expr e, ParseState st)
    {
        return ((Func<ParseState, ParseExprResult>)((st2) => (is_comma(current_kind(st2)) ? parse_list_elements(((Func<List<Expr>>)(() => { var _l = acc; _l.Add(e); return _l; }))(), skip_newlines(advance(st2))) : parse_list_elements(((Func<List<Expr>>)(() => { var _l = acc; _l.Add(e); return _l; }))(), st2))))(skip_newlines(st));
    }

    public static ParseExprResult parse_if_expr(ParseState st)
    {
        return ((Func<ParseState, ParseExprResult>)((st2) => ((Func<ParseExprResult, ParseExprResult>)((cond) => unwrap_expr_ok(cond, (c) => (st) => parse_if_then(c, st))))(parse_expr(st2))))(skip_newlines(advance(st)));
    }

    public static ParseExprResult parse_if_then(Expr c, ParseState st)
    {
        return ((Func<ParseState, ParseExprResult>)((st2) => ((Func<ParseState, ParseExprResult>)((st3) => ((Func<ParseState, ParseExprResult>)((st4) => ((Func<ParseExprResult, ParseExprResult>)((then_result) => unwrap_expr_ok(then_result, (t) => (st) => parse_if_else(c, t, st))))(parse_expr(st4))))(skip_newlines(st3))))(expect(new ThenKeyword(), st2))))(skip_newlines(st));
    }

    public static ParseExprResult parse_if_else(Expr c, Expr t, ParseState st)
    {
        return ((Func<ParseState, ParseExprResult>)((st2) => ((Func<ParseState, ParseExprResult>)((st3) => ((Func<ParseState, ParseExprResult>)((st4) => ((Func<ParseExprResult, ParseExprResult>)((else_result) => unwrap_expr_ok(else_result, (e) => (st) => finish_if(c, t, e, st))))(parse_expr(st4))))(skip_newlines(st3))))(expect(new ElseKeyword(), st2))))(skip_newlines(st));
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
        return (is_ident(current_kind(st)) ? parse_let_binding(acc, st) : (is_in_keyword(current_kind(st)) ? ((Func<ParseState, ParseExprResult>)((st2) => ((Func<ParseExprResult, ParseExprResult>)((body) => unwrap_expr_ok(body, (b) => (st) => finish_let(acc, b, st))))(parse_expr(st2))))(skip_newlines(advance(st))) : ((Func<ParseExprResult, ParseExprResult>)((body) => unwrap_expr_ok(body, (b) => (st) => finish_let(acc, b, st))))(parse_expr(st))));
    }

    public static ParseExprResult finish_let(List<LetBind> acc, Expr b, ParseState st)
    {
        return new ExprOk(new LetExpr(acc, b), st);
    }

    public static ParseExprResult parse_let_binding(List<LetBind> acc, ParseState st)
    {
        return ((Func<Token, ParseExprResult>)((name_tok) => ((Func<ParseState, ParseExprResult>)((st2) => ((Func<ParseState, ParseExprResult>)((st3) => ((Func<ParseExprResult, ParseExprResult>)((val_result) => unwrap_expr_ok(val_result, (v) => (st) => finish_let_binding(acc, name_tok, v, st))))(parse_expr(st3))))(expect(new Equals_(), st2))))(advance(st))))(current(st));
    }

    public static ParseExprResult finish_let_binding(List<LetBind> acc, Token name_tok, Expr v, ParseState st)
    {
        return ((Func<LetBind, ParseExprResult>)((binding) => ((Func<ParseState, ParseExprResult>)((st2) => (is_comma(current_kind(st2)) ? parse_let_bindings(((Func<List<LetBind>>)(() => { var _l = acc; _l.Add(binding); return _l; }))(), skip_newlines(advance(st2))) : parse_let_bindings(((Func<List<LetBind>>)(() => { var _l = acc; _l.Add(binding); return _l; }))(), st2))))(skip_newlines(st))))(new LetBind(name_tok, v));
    }

    public static ParseExprResult parse_match_expr(ParseState st)
    {
        return ((Func<ParseState, ParseExprResult>)((st2) => ((Func<ParseExprResult, ParseExprResult>)((scrut) => unwrap_expr_ok(scrut, (s) => (st) => start_match_branches(s, st))))(parse_expr(st2))))(advance(st));
    }

    public static ParseExprResult start_match_branches(Expr s, ParseState st)
    {
        return ((Func<ParseState, ParseExprResult>)((st2) => ((Func<Token, ParseExprResult>)((tok) => parse_match_branches(s, new List<MatchArm>(), tok.column, tok.line, st2)))(current(st2))))(skip_newlines(st));
    }

    public static ParseExprResult parse_match_branches(Expr scrut, List<MatchArm> acc, long col, long ln, ParseState st)
    {
        return (is_if_keyword(current_kind(st)) ? ((Func<Token, ParseExprResult>)((tok) => ((tok.line == ln) ? parse_one_match_branch(scrut, acc, col, ln, st) : ((tok.column == col) ? parse_one_match_branch(scrut, acc, col, ln, st) : new ExprOk(new MatchExpr(scrut, acc), st)))))(current(st)) : new ExprOk(new MatchExpr(scrut, acc), st));
    }

    public static ParseExprResult unwrap_pat_for_expr(ParsePatResult r, Func<Pat, Func<ParseState, ParseExprResult>> f)
    {
        return (r is PatOk _mPatOk114_ ? ((Func<ParseState, ParseExprResult>)((st) => ((Func<Pat, ParseExprResult>)((p) => f(p)(st)))((Pat)_mPatOk114_.Field0)))((ParseState)_mPatOk114_.Field1) : throw new InvalidOperationException("Non-exhaustive match"));
    }

    public static ParseExprResult parse_one_match_branch(Expr scrut, List<MatchArm> acc, long col, long ln, ParseState st)
    {
        return ((Func<ParseState, ParseExprResult>)((st2) => ((Func<ParsePatResult, ParseExprResult>)((pat) => unwrap_pat_for_expr(pat, (p) => (st) => parse_match_branch_body(scrut, acc, col, ln, p, st))))(parse_pattern(st2))))(advance(st));
    }

    public static ParseExprResult parse_match_branch_body(Expr scrut, List<MatchArm> acc, long col, long ln, Pat p, ParseState st)
    {
        return ((Func<ParseState, ParseExprResult>)((st2) => ((Func<ParseState, ParseExprResult>)((st3) => ((Func<ParseExprResult, ParseExprResult>)((body) => unwrap_expr_ok(body, (b) => (st) => finish_match_branch(scrut, acc, col, ln, p, b, st))))(parse_expr(st3))))(skip_newlines(st2))))(expect(new Arrow(), st));
    }

    public static ParseExprResult finish_match_branch(Expr scrut, List<MatchArm> acc, long col, long ln, Pat p, Expr b, ParseState st)
    {
        return ((Func<MatchArm, ParseExprResult>)((arm) => ((Func<ParseState, ParseExprResult>)((st2) => parse_match_branches(scrut, ((Func<List<MatchArm>>)(() => { var _l = acc; _l.Add(arm); return _l; }))(), col, ln, st2)))(skip_newlines(st))))(new MatchArm(p, b));
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
        return ((Func<Token, ParseExprResult>)((name_tok) => ((Func<ParseState, ParseExprResult>)((st2) => ((Func<ParseExprResult, ParseExprResult>)((val_result) => unwrap_expr_ok(val_result, (v) => (st) => finish_do_bind(acc, name_tok, v, st))))(parse_expr(st2))))(advance(advance(st)))))(current(st));
    }

    public static ParseExprResult finish_do_bind(List<DoStmt> acc, Token name_tok, Expr v, ParseState st)
    {
        return ((Func<ParseState, ParseExprResult>)((st2) => parse_do_stmts(((Func<List<DoStmt>>)(() => { var _l = acc; _l.Add(new DoBindStmt(name_tok, v)); return _l; }))(), st2)))(skip_newlines(st));
    }

    public static ParseExprResult parse_do_expr_stmt(List<DoStmt> acc, ParseState st)
    {
        return ((Func<ParseExprResult, ParseExprResult>)((expr_result) => unwrap_expr_ok(expr_result, (e) => (st) => finish_do_expr(acc, e, st))))(parse_expr(st));
    }

    public static ParseExprResult finish_do_expr(List<DoStmt> acc, Expr e, ParseState st)
    {
        return ((Func<ParseState, ParseExprResult>)((st2) => parse_do_stmts(((Func<List<DoStmt>>)(() => { var _l = acc; _l.Add(new DoExprStmt(e)); return _l; }))(), st2)))(skip_newlines(st));
    }

    public static ParseExprResult parse_handle_expr(ParseState st)
    {
        return ((Func<ParseState, ParseExprResult>)((st1) => ((Func<Token, ParseExprResult>)((eff_tok) => ((Func<ParseState, ParseExprResult>)((st2) => ((Func<ParseExprResult, ParseExprResult>)((body_result) => unwrap_expr_ok(body_result, (body) => (st) => finish_handle_body(eff_tok, body, st))))(parse_expr(st2))))(advance(st1))))(current(st1))))(advance(st));
    }

    public static ParseExprResult finish_handle_body(Token eff_tok, Expr body, ParseState st)
    {
        return ((Func<ParseState, ParseExprResult>)((st2) => ((Func<HandleParseResult, ParseExprResult>)((clauses) => new ExprOk(new HandleExpr(eff_tok, body, clauses.clauses), clauses.state)))(parse_handle_clauses(st2, new List<HandleClause>()))))(skip_newlines(st));
    }

    public static HandleParseResult parse_handle_clauses(ParseState st, List<HandleClause> acc)
    {
        return (is_ident(current_kind(st)) ? ((Func<Token, HandleParseResult>)((op_tok) => ((Func<ParseState, HandleParseResult>)((st1) => ((Func<HandleParamsResult, HandleParseResult>)((@params) => ((((long)@params.toks.Count) > 0L) ? ((Func<Token, HandleParseResult>)((resume_tok) => ((Func<ParseState, HandleParseResult>)((st5) => ((Func<ParseState, HandleParseResult>)((st6) => ((Func<ParseExprResult, HandleParseResult>)((body_result) => unwrap_handle_clause_body(op_tok, resume_tok, body_result, acc)))(parse_expr(st6))))(skip_newlines(st5))))(expect(new Equals_(), @params.state))))(@params.toks[(int)(((long)@params.toks.Count) - 1L)]) : new HandleParseResult(acc, st))))(parse_handle_params(st1, new List<Token>()))))(advance(st))))(current(st)) : new HandleParseResult(acc, st));
    }

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
                return new HandleParamsResult(acc, st);
            }
        }
    }

    public static HandleParseResult unwrap_handle_clause_body(Token op_tok, Token resume_tok, ParseExprResult result, List<HandleClause> acc)
    {
        return (result is ExprOk _mExprOk115_ ? ((Func<ParseState, HandleParseResult>)((st) => ((Func<Expr, HandleParseResult>)((body) => ((Func<HandleClause, HandleParseResult>)((clause) => ((Func<ParseState, HandleParseResult>)((st2) => parse_handle_clauses(st2, ((Func<List<HandleClause>>)(() => { var _l = acc; _l.Add(clause); return _l; }))())))(skip_newlines(st))))(new HandleClause(op_tok, resume_tok, body))))((Expr)_mExprOk115_.Field0)))((ParseState)_mExprOk115_.Field1) : throw new InvalidOperationException("Non-exhaustive match"));
    }

    public static ParseExprResult parse_lambda_expr(ParseState st)
    {
        return ((Func<ParseState, ParseExprResult>)((st2) => ((Func<LambdaParamsResult, ParseExprResult>)((params_result) => ((Func<ParseState, ParseExprResult>)((st3) => ((Func<ParseState, ParseExprResult>)((st4) => ((Func<ParseExprResult, ParseExprResult>)((body) => unwrap_expr_ok(body, (body) => (st) => finish_lambda(params_result.toks, body, st))))(parse_expr(st4))))(skip_newlines(st3))))(expect(new Arrow(), params_result.state))))(collect_lambda_params(st2, new List<Token>()))))(advance(st));
    }

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
                return new LambdaParamsResult(acc, st);
            }
        }
    }

    public static ParseExprResult finish_lambda(List<Token> @params, Expr body, ParseState st)
    {
        return new ExprOk(new LambdaExpr(@params, body), st);
    }

    public static long token_length(Token t)
    {
        return ((long)t.text.Length);
    }

    public static CodexType resolve_type_expr(List<TypeBinding> tdm, ATypeExpr texpr)
    {
        return ((Func<ATypeExpr, CodexType>)((_scrutinee116_) => (_scrutinee116_ is ANamedType _mANamedType116_ ? ((Func<Name, CodexType>)((name) => resolve_type_name(tdm, name.value)))((Name)_mANamedType116_.Field0) : (_scrutinee116_ is AFunType _mAFunType116_ ? ((Func<ATypeExpr, CodexType>)((ret) => ((Func<ATypeExpr, CodexType>)((param) => new FunTy(resolve_type_expr(tdm, param), resolve_type_expr(tdm, ret))))((ATypeExpr)_mAFunType116_.Field0)))((ATypeExpr)_mAFunType116_.Field1) : (_scrutinee116_ is AAppType _mAAppType116_ ? ((Func<List<ATypeExpr>, CodexType>)((args) => ((Func<ATypeExpr, CodexType>)((ctor) => resolve_applied_type(tdm, ctor, args)))((ATypeExpr)_mAAppType116_.Field0)))((List<ATypeExpr>)_mAAppType116_.Field1) : throw new InvalidOperationException("Non-exhaustive match"))))))(texpr);
    }

    public static CodexType resolve_applied_type(List<TypeBinding> tdm, ATypeExpr ctor, List<ATypeExpr> args)
    {
        return (ctor is ANamedType _mANamedType117_ ? ((Func<Name, CodexType>)((name) => ((name.value == "1\u0011\u0013\u000E") ? ((((long)args.Count) == 1L) ? new ListTy(resolve_type_expr(tdm, args[(int)0L])) : new ListTy(new ErrorTy())) : new ConstructedTy(name, resolve_type_expr_list(tdm, args, 0L, ((long)args.Count), new List<CodexType>())))))((Name)_mANamedType117_.Field0) : ((Func<ATypeExpr, CodexType>)((_) => resolve_type_expr(tdm, ctor)))(ctor));
    }

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
                var _tco_2 = (i + 1L);
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

    public static CodexType resolve_type_name(List<TypeBinding> tdm, string name)
    {
        return ((name == "+\u0012\u000E\u000D\u001D\u000D\u0015") ? new IntegerTy() : ((name == ",\u0019\u001A \u000D\u0015") ? new NumberTy() : ((name == "(\u000D$\u000E") ? new TextTy() : ((name == ":\u0010\u0010\u0017\u000D\u000F\u0012") ? new BooleanTy() : ((name == "2\u0014\u000F\u0015") ? new CharTy() : ((name == ",\u0010\u000E\u0014\u0011\u0012\u001D") ? new NothingTy() : lookup_type_def(tdm, name)))))));
    }

    public static CodexType lookup_type_def(List<TypeBinding> tdm, string name)
    {
        return ((Func<long, CodexType>)((len) => ((len == 0L) ? new ConstructedTy(new Name(name), new List<CodexType>()) : ((Func<long, CodexType>)((pos) => ((pos >= len) ? new ConstructedTy(new Name(name), new List<CodexType>()) : ((Func<TypeBinding, CodexType>)((b) => ((b.name == name) ? b.bound_type : new ConstructedTy(new Name(name), new List<CodexType>()))))(tdm[(int)pos]))))(bsearch_text_pos(tdm, name, 0L, len)))))(((long)tdm.Count));
    }

    public static bool is_value_name(string name)
    {
        return ((((long)name.Length) == 0L) ? false : ((Func<long, bool>)((code) => ((code >= 97L) && (code <= 122L))))(((long)name[(int)0L])));
    }

    public static ParamResult parameterize_type(UnificationState st, CodexType ty)
    {
        return ((Func<WalkResult, ParamResult>)((r) => ((Func<CodexType, ParamResult>)((wrapped) => new ParamResult(wrapped, r.entries, r.state)))(wrap_forall_from_entries(r.walked, r.entries, 0L, ((long)r.entries.Count)))))(parameterize_walk(st, new List<ParamEntry>(), ty));
    }

    public static CodexType wrap_forall_from_entries(CodexType ty, List<ParamEntry> entries, long i, long len)
    {
        return ((i == len) ? ty : ((Func<ParamEntry, CodexType>)((e) => new ForAllTy(e.var_id, wrap_forall_from_entries(ty, entries, (i + 1L), len))))(entries[(int)i]));
    }

    public static WalkResult parameterize_walk(UnificationState st, List<ParamEntry> entries, CodexType ty)
    {
        return ((Func<CodexType, WalkResult>)((_scrutinee118_) => (_scrutinee118_ is ConstructedTy _mConstructedTy118_ ? ((Func<List<CodexType>, WalkResult>)((args) => ((Func<Name, WalkResult>)((name) => (((((long)args.Count) == 0L) && is_value_name(name.value)) ? ((Func<long, WalkResult>)((looked) => ((looked >= 0L) ? new WalkResult(new TypeVar(looked), entries, st) : ((Func<FreshResult, WalkResult>)((fr) => (fr.var_type is TypeVar _mTypeVar119_ ? ((Func<long, WalkResult>)((new_id) => ((Func<ParamEntry, WalkResult>)((new_entry) => new WalkResult(fr.var_type, Enumerable.Concat(entries, new List<ParamEntry>() { new_entry }).ToList(), fr.state)))(new ParamEntry(name.value, new_id))))((long)_mTypeVar119_.Field0) : ((Func<CodexType, WalkResult>)((_) => new WalkResult(ty, entries, fr.state)))(fr.var_type))))(fresh_and_advance(st)))))(find_param_entry(entries, name.value, 0L, ((long)entries.Count))) : ((Func<WalkListResult, WalkResult>)((args_r) => new WalkResult(new ConstructedTy(name, args_r.walked_list), args_r.entries, args_r.state)))(parameterize_walk_list(st, entries, args, 0L, ((long)args.Count), new List<CodexType>())))))((Name)_mConstructedTy118_.Field0)))((List<CodexType>)_mConstructedTy118_.Field1) : (_scrutinee118_ is FunTy _mFunTy118_ ? ((Func<CodexType, WalkResult>)((ret) => ((Func<CodexType, WalkResult>)((param) => ((Func<WalkResult, WalkResult>)((pr) => ((Func<WalkResult, WalkResult>)((rr) => new WalkResult(new FunTy(pr.walked, rr.walked), rr.entries, rr.state)))(parameterize_walk(pr.state, pr.entries, ret))))(parameterize_walk(st, entries, param))))((CodexType)_mFunTy118_.Field0)))((CodexType)_mFunTy118_.Field1) : (_scrutinee118_ is ListTy _mListTy118_ ? ((Func<CodexType, WalkResult>)((elem) => ((Func<WalkResult, WalkResult>)((er) => new WalkResult(new ListTy(er.walked), er.entries, er.state)))(parameterize_walk(st, entries, elem))))((CodexType)_mListTy118_.Field0) : (_scrutinee118_ is ForAllTy _mForAllTy118_ ? ((Func<CodexType, WalkResult>)((body) => ((Func<long, WalkResult>)((id) => ((Func<WalkResult, WalkResult>)((br) => new WalkResult(new ForAllTy(id, br.walked), br.entries, br.state)))(parameterize_walk(st, entries, body))))((long)_mForAllTy118_.Field0)))((CodexType)_mForAllTy118_.Field1) : ((Func<CodexType, WalkResult>)((_) => new WalkResult(ty, entries, st)))(_scrutinee118_)))))))(ty);
    }

    public static long find_param_entry(List<ParamEntry> entries, string name, long i, long len)
    {
        while (true)
        {
            if ((i == len))
            {
                return (0L - 1L);
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
                return new WalkListResult(acc, entries, st);
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

    public static CheckResult check_def(UnificationState st, TypeEnv env, ADef def)
    {
        return ((Func<DefSetup, CheckResult>)((declared) => ((Func<DefParamResult, CheckResult>)((env2) => ((Func<CheckResult, CheckResult>)((body_r) => ((Func<UnifyResult, CheckResult>)((u) => new CheckResult(declared.expected_type, u.state)))(unify(body_r.state, env2.remaining_type, body_r.inferred_type))))(infer_expr(env2.state, env2.env, def.body))))(bind_def_params(declared.state, declared.env, def.@params, declared.expected_type, 0L, ((long)def.@params.Count)))))(resolve_declared_type(st, env, def));
    }

    public static DefSetup resolve_declared_type(UnificationState st, TypeEnv env, ADef def)
    {
        return ((((long)def.declared_type.Count) == 0L) ? ((Func<FreshResult, DefSetup>)((fr) => new DefSetup(fr.var_type, fr.var_type, fr.state, env)))(fresh_and_advance(st)) : ((Func<CodexType, DefSetup>)((env_type) => ((Func<FreshResult, DefSetup>)((inst) => new DefSetup(inst.var_type, inst.var_type, inst.state, env)))(instantiate_type(st, env_type))))(env_lookup(env, def.name.value)));
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
        return ((Func<List<TypeBinding>, ModuleResult>)((tdm) => ((Func<LetBindResult, ModuleResult>)((tenv) => ((Func<LetBindResult, ModuleResult>)((env) => check_all_defs(env.state, env.env, mod.defs, 0L, ((long)mod.defs.Count), new List<TypeBinding>())))(register_all_defs(tenv.state, tenv.env, tdm, mod.defs, 0L, ((long)mod.defs.Count)))))(register_type_defs(empty_unification_state(), builtin_type_env(), tdm, mod.type_defs, 0L, ((long)mod.type_defs.Count)))))(build_type_def_map(mod.type_defs, 0L, ((long)mod.type_defs.Count), new List<TypeBinding>()));
    }

    public static LetBindResult register_all_defs(UnificationState st, TypeEnv env, List<TypeBinding> tdm, List<ADef> defs, long i, long len)
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
                var ty = ((((long)def.declared_type.Count) == 0L) ? ((Func<FreshResult, LetBindResult>)((fr) => ((Func<TypeEnv, LetBindResult>)((env2) => new LetBindResult(fr.state, env2)))(env_bind(env, def.name.value, fr.var_type))))(fresh_and_advance(st)) : ((Func<CodexType, LetBindResult>)((resolved) => ((Func<ParamResult, LetBindResult>)((pr) => new LetBindResult(pr.state, env_bind(env, def.name.value, pr.parameterized))))(parameterize_type(st, resolved))))(resolve_type_expr(tdm, def.declared_type[(int)0L])));
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
                var entry = ((Func<ATypeDef, TypeBinding>)((_scrutinee120_) => (_scrutinee120_ is AVariantTypeDef _mAVariantTypeDef120_ ? ((Func<List<AVariantCtorDef>, TypeBinding>)((ctors) => ((Func<List<Name>, TypeBinding>)((type_params) => ((Func<Name, TypeBinding>)((name) => ((Func<List<SumCtor>, TypeBinding>)((sum_ctors) => new TypeBinding(name.value, new SumTy(name, sum_ctors))))(build_sum_ctors(tdefs, ctors, 0L, ((long)ctors.Count), new List<SumCtor>(), acc))))((Name)_mAVariantTypeDef120_.Field0)))((List<Name>)_mAVariantTypeDef120_.Field1)))((List<AVariantCtorDef>)_mAVariantTypeDef120_.Field2) : (_scrutinee120_ is ARecordTypeDef _mARecordTypeDef120_ ? ((Func<List<ARecordFieldDef>, TypeBinding>)((fields) => ((Func<List<Name>, TypeBinding>)((type_params) => ((Func<Name, TypeBinding>)((name) => ((Func<List<RecordField>, TypeBinding>)((rec_fields) => new TypeBinding(name.value, new RecordTy(name, rec_fields))))(build_record_fields_for_map(tdefs, fields, 0L, ((long)fields.Count), new List<RecordField>(), acc))))((Name)_mARecordTypeDef120_.Field0)))((List<Name>)_mARecordTypeDef120_.Field1)))((List<ARecordFieldDef>)_mARecordTypeDef120_.Field2) : throw new InvalidOperationException("Non-exhaustive match")))))(td);
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
                var field_types = resolve_type_expr_list_for_map(tdefs, c.fields, 0L, ((long)c.fields.Count), new List<CodexType>(), partial_tdm);
                var sc = new SumCtor(c.name, field_types);
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
                var rfield = new RecordField(f.name, resolve_type_expr(partial_tdm, f.type_expr));
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
                return new LetBindResult(st, env);
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

    public static LetBindResult register_one_type_def(UnificationState st, TypeEnv env, List<TypeBinding> tdm, ATypeDef td)
    {
        return ((Func<ATypeDef, LetBindResult>)((_scrutinee121_) => (_scrutinee121_ is AVariantTypeDef _mAVariantTypeDef121_ ? ((Func<List<AVariantCtorDef>, LetBindResult>)((ctors) => ((Func<List<Name>, LetBindResult>)((type_params) => ((Func<Name, LetBindResult>)((name) => ((Func<CodexType, LetBindResult>)((result_ty) => register_variant_ctors(st, env, tdm, ctors, result_ty, 0L, ((long)ctors.Count))))(lookup_type_def(tdm, name.value))))((Name)_mAVariantTypeDef121_.Field0)))((List<Name>)_mAVariantTypeDef121_.Field1)))((List<AVariantCtorDef>)_mAVariantTypeDef121_.Field2) : (_scrutinee121_ is ARecordTypeDef _mARecordTypeDef121_ ? ((Func<List<ARecordFieldDef>, LetBindResult>)((fields) => ((Func<List<Name>, LetBindResult>)((type_params) => ((Func<Name, LetBindResult>)((name) => ((Func<List<RecordField>, LetBindResult>)((resolved_fields) => ((Func<CodexType, LetBindResult>)((result_ty) => ((Func<CodexType, LetBindResult>)((ctor_ty) => new LetBindResult(st, env_bind(env, name.value, ctor_ty))))(build_record_ctor_type(tdm, fields, result_ty, 0L, ((long)fields.Count)))))(new RecordTy(name, resolved_fields))))(build_record_fields(tdm, fields, 0L, ((long)fields.Count), new List<RecordField>()))))((Name)_mARecordTypeDef121_.Field0)))((List<Name>)_mARecordTypeDef121_.Field1)))((List<ARecordFieldDef>)_mARecordTypeDef121_.Field2) : throw new InvalidOperationException("Non-exhaustive match")))))(td);
    }

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
                var rfield = new RecordField(f.name, resolve_type_expr(tdm, f.type_expr));
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

    public static CodexType lookup_record_field(List<RecordField> fields, string name)
    {
        return ((((long)fields.Count) == 0L) ? new ErrorTy() : ((Func<RecordField, CodexType>)((f) => ((f.name.value == name) ? f.type_val : lookup_record_field_loop(fields, name, 1L, ((long)fields.Count)))))(fields[(int)0L]));
    }

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
                return new LetBindResult(st, env);
            }
            else
            {
                var ctor = ctors[(int)i];
                var ctor_ty = build_ctor_type(tdm, ctor.fields, result_ty, 0L, ((long)ctor.fields.Count));
                var env2 = env_bind(env, ctor.name.value, ctor_ty);
                var _tco_0 = st;
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

    public static CodexType build_ctor_type(List<TypeBinding> tdm, List<ATypeExpr> fields, CodexType result, long i, long len)
    {
        return ((i == len) ? result : ((Func<CodexType, CodexType>)((rest) => new FunTy(resolve_type_expr(tdm, fields[(int)i]), rest)))(build_ctor_type(tdm, fields, result, (i + 1L), len)));
    }

    public static CodexType build_record_ctor_type(List<TypeBinding> tdm, List<ARecordFieldDef> fields, CodexType result, long i, long len)
    {
        return ((i == len) ? result : ((Func<ARecordFieldDef, CodexType>)((f) => ((Func<CodexType, CodexType>)((rest) => new FunTy(resolve_type_expr(tdm, f.type_expr), rest)))(build_record_ctor_type(tdm, fields, result, (i + 1L), len))))(fields[(int)i]));
    }

    public static CheckResult infer_literal(UnificationState st, LiteralKind kind)
    {
        return ((Func<LiteralKind, CheckResult>)((_scrutinee122_) => (_scrutinee122_ is IntLit _mIntLit122_ ? new CheckResult(new IntegerTy(), st) : (_scrutinee122_ is NumLit _mNumLit122_ ? new CheckResult(new NumberTy(), st) : (_scrutinee122_ is TextLit _mTextLit122_ ? new CheckResult(new TextTy(), st) : (_scrutinee122_ is CharLit _mCharLit122_ ? new CheckResult(new CharTy(), st) : (_scrutinee122_ is BoolLit _mBoolLit122_ ? new CheckResult(new BooleanTy(), st) : throw new InvalidOperationException("Non-exhaustive match"))))))))(kind);
    }

    public static CheckResult infer_name(UnificationState st, TypeEnv env, string name)
    {
        return (env_has(env, name) ? ((Func<CodexType, CheckResult>)((raw) => ((Func<FreshResult, CheckResult>)((inst) => new CheckResult(inst.var_type, inst.state)))(instantiate_type(st, raw))))(env_lookup(env, name)) : new CheckResult(new ErrorTy(), add_unify_error(st, "20>\u0005\u0003\u0003\u0005", string.Concat("3\u0012\"\u0012\u0010\u001B\u0012\u0002\u0012\u000F\u001A\u000DE\u0002", name))));
    }

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
                var _ = _tco_s;
                return new FreshResult(ty, st);
            }
        }
    }

    public static CodexType subst_type_var(CodexType ty, long var_id, CodexType replacement)
    {
        return ((Func<CodexType, CodexType>)((_scrutinee123_) => (_scrutinee123_ is TypeVar _mTypeVar123_ ? ((Func<long, CodexType>)((id) => ((id == var_id) ? replacement : ty)))((long)_mTypeVar123_.Field0) : (_scrutinee123_ is FunTy _mFunTy123_ ? ((Func<CodexType, CodexType>)((ret) => ((Func<CodexType, CodexType>)((param) => new FunTy(subst_type_var(param, var_id, replacement), subst_type_var(ret, var_id, replacement))))((CodexType)_mFunTy123_.Field0)))((CodexType)_mFunTy123_.Field1) : (_scrutinee123_ is ListTy _mListTy123_ ? ((Func<CodexType, CodexType>)((elem) => new ListTy(subst_type_var(elem, var_id, replacement))))((CodexType)_mListTy123_.Field0) : (_scrutinee123_ is ForAllTy _mForAllTy123_ ? ((Func<CodexType, CodexType>)((body) => ((Func<long, CodexType>)((inner_id) => ((inner_id == var_id) ? ty : new ForAllTy(inner_id, subst_type_var(body, var_id, replacement)))))((long)_mForAllTy123_.Field0)))((CodexType)_mForAllTy123_.Field1) : (_scrutinee123_ is ConstructedTy _mConstructedTy123_ ? ((Func<List<CodexType>, CodexType>)((args) => ((Func<Name, CodexType>)((name) => new ConstructedTy(name, map_subst_type_var(args, var_id, replacement, 0L, ((long)args.Count), new List<CodexType>()))))((Name)_mConstructedTy123_.Field0)))((List<CodexType>)_mConstructedTy123_.Field1) : (_scrutinee123_ is SumTy _mSumTy123_ ? ((Func<List<SumCtor>, CodexType>)((ctors) => ((Func<Name, CodexType>)((name) => ty))((Name)_mSumTy123_.Field0)))((List<SumCtor>)_mSumTy123_.Field1) : (_scrutinee123_ is RecordTy _mRecordTy123_ ? ((Func<List<RecordField>, CodexType>)((fields) => ((Func<Name, CodexType>)((name) => ty))((Name)_mRecordTy123_.Field0)))((List<RecordField>)_mRecordTy123_.Field1) : ((Func<CodexType, CodexType>)((_) => ty))(_scrutinee123_))))))))))(ty);
    }

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

    public static CheckResult infer_binary(UnificationState st, TypeEnv env, AExpr left, BinaryOp op, AExpr right)
    {
        return ((Func<CheckResult, CheckResult>)((lr) => ((Func<CheckResult, CheckResult>)((rr) => infer_binary_op(rr.state, lr.inferred_type, rr.inferred_type, op)))(infer_expr(lr.state, env, right))))(infer_expr(st, env, left));
    }

    public static CheckResult infer_binary_op(UnificationState st, CodexType lt, CodexType rt, BinaryOp op)
    {
        return ((Func<BinaryOp, CheckResult>)((_scrutinee124_) => (_scrutinee124_ is OpAdd _mOpAdd124_ ? infer_arithmetic(st, lt, rt) : (_scrutinee124_ is OpSub _mOpSub124_ ? infer_arithmetic(st, lt, rt) : (_scrutinee124_ is OpMul _mOpMul124_ ? infer_arithmetic(st, lt, rt) : (_scrutinee124_ is OpDiv _mOpDiv124_ ? infer_arithmetic(st, lt, rt) : (_scrutinee124_ is OpPow _mOpPow124_ ? infer_arithmetic(st, lt, rt) : (_scrutinee124_ is OpEq _mOpEq124_ ? infer_comparison(st, lt, rt) : (_scrutinee124_ is OpNotEq _mOpNotEq124_ ? infer_comparison(st, lt, rt) : (_scrutinee124_ is OpLt _mOpLt124_ ? infer_comparison(st, lt, rt) : (_scrutinee124_ is OpGt _mOpGt124_ ? infer_comparison(st, lt, rt) : (_scrutinee124_ is OpLtEq _mOpLtEq124_ ? infer_comparison(st, lt, rt) : (_scrutinee124_ is OpGtEq _mOpGtEq124_ ? infer_comparison(st, lt, rt) : (_scrutinee124_ is OpAnd _mOpAnd124_ ? infer_logical(st, lt, rt) : (_scrutinee124_ is OpOr _mOpOr124_ ? infer_logical(st, lt, rt) : (_scrutinee124_ is OpAppend _mOpAppend124_ ? infer_append(st, lt, rt) : (_scrutinee124_ is OpCons _mOpCons124_ ? infer_cons(st, lt, rt) : (_scrutinee124_ is OpDefEq _mOpDefEq124_ ? infer_comparison(st, lt, rt) : throw new InvalidOperationException("Non-exhaustive match")))))))))))))))))))(op);
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
        return ((Func<CodexType, CheckResult>)((resolved) => (resolved is TextTy _mTextTy125_ ? ((Func<UnifyResult, CheckResult>)((r) => new CheckResult(new TextTy(), r.state)))(unify(st, rt, new TextTy())) : ((Func<CodexType, CheckResult>)((_) => ((Func<UnifyResult, CheckResult>)((r) => new CheckResult(lt, r.state)))(unify(st, lt, rt))))(resolved))))(resolve(st, lt));
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
                var pr = bind_pattern(st, env, arm.pattern, scrut_ty);
                var br = infer_expr(pr.state, pr.env, arm.body);
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

    public static PatBindResult bind_pattern(UnificationState st, TypeEnv env, APat pat, CodexType ty)
    {
        return ((Func<APat, PatBindResult>)((_scrutinee126_) => (_scrutinee126_ is AVarPat _mAVarPat126_ ? ((Func<Name, PatBindResult>)((name) => new PatBindResult(st, env_bind(env, name.value, ty))))((Name)_mAVarPat126_.Field0) : (_scrutinee126_ is AWildPat _mAWildPat126_ ? new PatBindResult(st, env) : (_scrutinee126_ is ALitPat _mALitPat126_ ? ((Func<LiteralKind, PatBindResult>)((kind) => ((Func<string, PatBindResult>)((val) => new PatBindResult(st, env)))((string)_mALitPat126_.Field0)))((LiteralKind)_mALitPat126_.Field1) : (_scrutinee126_ is ACtorPat _mACtorPat126_ ? ((Func<List<APat>, PatBindResult>)((sub_pats) => ((Func<Name, PatBindResult>)((ctor_name) => ((Func<FreshResult, PatBindResult>)((ctor_lookup) => bind_ctor_sub_patterns(ctor_lookup.state, env, sub_pats, ctor_lookup.var_type, 0L, ((long)sub_pats.Count))))(instantiate_type(st, env_lookup(env, ctor_name.value)))))((Name)_mACtorPat126_.Field0)))((List<APat>)_mACtorPat126_.Field1) : throw new InvalidOperationException("Non-exhaustive match")))))))(pat);
    }

    public static PatBindResult bind_ctor_sub_patterns(UnificationState st, TypeEnv env, List<APat> sub_pats, CodexType ctor_ty, long i, long len)
    {
        while (true)
        {
            if ((i == len))
            {
                return new PatBindResult(st, env);
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
                    var _ = _tco_s;
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
        return ((Func<AExpr, CheckResult>)((_scrutinee127_) => (_scrutinee127_ is ALitExpr _mALitExpr127_ ? ((Func<LiteralKind, CheckResult>)((kind) => ((Func<string, CheckResult>)((val) => infer_literal(st, kind)))((string)_mALitExpr127_.Field0)))((LiteralKind)_mALitExpr127_.Field1) : (_scrutinee127_ is ANameExpr _mANameExpr127_ ? ((Func<Name, CheckResult>)((name) => infer_name(st, env, name.value)))((Name)_mANameExpr127_.Field0) : (_scrutinee127_ is ABinaryExpr _mABinaryExpr127_ ? ((Func<AExpr, CheckResult>)((right) => ((Func<BinaryOp, CheckResult>)((op) => ((Func<AExpr, CheckResult>)((left) => infer_binary(st, env, left, op, right)))((AExpr)_mABinaryExpr127_.Field0)))((BinaryOp)_mABinaryExpr127_.Field1)))((AExpr)_mABinaryExpr127_.Field2) : (_scrutinee127_ is AUnaryExpr _mAUnaryExpr127_ ? ((Func<AExpr, CheckResult>)((operand) => ((Func<CheckResult, CheckResult>)((r) => ((Func<UnifyResult, CheckResult>)((u) => new CheckResult(new IntegerTy(), u.state)))(unify(r.state, r.inferred_type, new IntegerTy()))))(infer_expr(st, env, operand))))((AExpr)_mAUnaryExpr127_.Field0) : (_scrutinee127_ is AApplyExpr _mAApplyExpr127_ ? ((Func<AExpr, CheckResult>)((arg) => ((Func<AExpr, CheckResult>)((func) => infer_application(st, env, func, arg)))((AExpr)_mAApplyExpr127_.Field0)))((AExpr)_mAApplyExpr127_.Field1) : (_scrutinee127_ is AIfExpr _mAIfExpr127_ ? ((Func<AExpr, CheckResult>)((else_e) => ((Func<AExpr, CheckResult>)((then_e) => ((Func<AExpr, CheckResult>)((cond) => infer_if(st, env, cond, then_e, else_e)))((AExpr)_mAIfExpr127_.Field0)))((AExpr)_mAIfExpr127_.Field1)))((AExpr)_mAIfExpr127_.Field2) : (_scrutinee127_ is ALetExpr _mALetExpr127_ ? ((Func<AExpr, CheckResult>)((body) => ((Func<List<ALetBind>, CheckResult>)((bindings) => infer_let(st, env, bindings, body)))((List<ALetBind>)_mALetExpr127_.Field0)))((AExpr)_mALetExpr127_.Field1) : (_scrutinee127_ is ALambdaExpr _mALambdaExpr127_ ? ((Func<AExpr, CheckResult>)((body) => ((Func<List<Name>, CheckResult>)((@params) => infer_lambda(st, env, @params, body)))((List<Name>)_mALambdaExpr127_.Field0)))((AExpr)_mALambdaExpr127_.Field1) : (_scrutinee127_ is AMatchExpr _mAMatchExpr127_ ? ((Func<List<AMatchArm>, CheckResult>)((arms) => ((Func<AExpr, CheckResult>)((scrutinee) => infer_match(st, env, scrutinee, arms)))((AExpr)_mAMatchExpr127_.Field0)))((List<AMatchArm>)_mAMatchExpr127_.Field1) : (_scrutinee127_ is AListExpr _mAListExpr127_ ? ((Func<List<AExpr>, CheckResult>)((elems) => infer_list(st, env, elems)))((List<AExpr>)_mAListExpr127_.Field0) : (_scrutinee127_ is ADoExpr _mADoExpr127_ ? ((Func<List<ADoStmt>, CheckResult>)((stmts) => infer_do(st, env, stmts)))((List<ADoStmt>)_mADoExpr127_.Field0) : (_scrutinee127_ is AFieldAccess _mAFieldAccess127_ ? ((Func<Name, CheckResult>)((field) => ((Func<AExpr, CheckResult>)((obj) => ((Func<CheckResult, CheckResult>)((r) => ((Func<CodexType, CheckResult>)((resolved) => ((Func<CodexType, CheckResult>)((_scrutinee128_) => (_scrutinee128_ is RecordTy _mRecordTy128_ ? ((Func<List<RecordField>, CheckResult>)((rfields) => ((Func<Name, CheckResult>)((rname) => ((Func<CodexType, CheckResult>)((ftype) => new CheckResult(ftype, r.state)))(lookup_record_field(rfields, field.value))))((Name)_mRecordTy128_.Field0)))((List<RecordField>)_mRecordTy128_.Field1) : (_scrutinee128_ is ConstructedTy _mConstructedTy128_ ? ((Func<List<CodexType>, CheckResult>)((cargs) => ((Func<Name, CheckResult>)((cname) => ((Func<CodexType, CheckResult>)((record_type) => (record_type is RecordTy _mRecordTy129_ ? ((Func<List<RecordField>, CheckResult>)((rfields) => ((Func<Name, CheckResult>)((rname) => ((Func<CodexType, CheckResult>)((ftype) => new CheckResult(ftype, r.state)))(lookup_record_field(rfields, field.value))))((Name)_mRecordTy129_.Field0)))((List<RecordField>)_mRecordTy129_.Field1) : ((Func<CodexType, CheckResult>)((_) => ((Func<FreshResult, CheckResult>)((fr) => new CheckResult(fr.var_type, fr.state)))(fresh_and_advance(r.state))))(record_type))))(resolve_constructed_to_record(env, cname.value))))((Name)_mConstructedTy128_.Field0)))((List<CodexType>)_mConstructedTy128_.Field1) : ((Func<CodexType, CheckResult>)((_) => ((Func<FreshResult, CheckResult>)((fr) => new CheckResult(fr.var_type, fr.state)))(fresh_and_advance(r.state))))(_scrutinee128_)))))(resolved)))(deep_resolve(r.state, r.inferred_type))))(infer_expr(st, env, obj))))((AExpr)_mAFieldAccess127_.Field0)))((Name)_mAFieldAccess127_.Field1) : (_scrutinee127_ is ARecordExpr _mARecordExpr127_ ? ((Func<List<AFieldExpr>, CheckResult>)((fields) => ((Func<Name, CheckResult>)((name) => ((Func<UnificationState, CheckResult>)((st2) => ((Func<CodexType, CheckResult>)((ctor_type) => ((Func<CodexType, CheckResult>)((result_type) => new CheckResult(result_type, st2)))(strip_fun_args(ctor_type))))((env_has(env, name.value) ? env_lookup(env, name.value) : new ErrorTy()))))(infer_record_fields(st, env, fields, 0L, ((long)fields.Count)))))((Name)_mARecordExpr127_.Field0)))((List<AFieldExpr>)_mARecordExpr127_.Field1) : (_scrutinee127_ is AErrorExpr _mAErrorExpr127_ ? ((Func<string, CheckResult>)((msg) => new CheckResult(new ErrorTy(), st)))((string)_mAErrorExpr127_.Field0) : throw new InvalidOperationException("Non-exhaustive match")))))))))))))))))(expr);
    }

    public static CodexType resolve_constructed_to_record(TypeEnv env, string name)
    {
        return (env_has(env, name) ? strip_fun_args(env_lookup(env, name)) : new ErrorTy());
    }

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
                var _ = _tco_s;
                return ty;
            }
        }
    }

    public static TypeEnv empty_type_env()
    {
        return new TypeEnv(new List<TypeBinding>());
    }

    public static CodexType env_lookup(TypeEnv env, string name)
    {
        return ((Func<long, CodexType>)((len) => ((len == 0L) ? new ErrorTy() : ((Func<long, CodexType>)((pos) => ((pos >= len) ? new ErrorTy() : ((Func<TypeBinding, CodexType>)((b) => ((b.name == name) ? b.bound_type : new ErrorTy())))(env.bindings[(int)pos]))))(bsearch_text_pos(env.bindings, name, 0L, len)))))(((long)env.bindings.Count));
    }

    public static bool env_has(TypeEnv env, string name)
    {
        return ((Func<long, bool>)((len) => ((len == 0L) ? false : ((Func<long, bool>)((pos) => ((pos >= len) ? false : (env.bindings[(int)pos].name == name))))(bsearch_text_pos(env.bindings, name, 0L, len)))))(((long)env.bindings.Count));
    }

    public static TypeEnv env_bind(TypeEnv env, string name, CodexType ty)
    {
        return ((Func<long, TypeEnv>)((len) => ((Func<long, TypeEnv>)((pos) => new TypeEnv(((Func<List<TypeBinding>>)(() => { var _l = new List<TypeBinding>(env.bindings); _l.Insert((int)pos, new TypeBinding(name, ty)); return _l; }))())))(bsearch_text_pos(env.bindings, name, 0L, len))))(((long)env.bindings.Count));
    }

    public static TypeEnv builtin_type_env()
    {
        return ((Func<TypeEnv, TypeEnv>)((e) => ((Func<TypeEnv, TypeEnv>)((e2) => ((Func<TypeEnv, TypeEnv>)((e3) => ((Func<TypeEnv, TypeEnv>)((e4) => ((Func<TypeEnv, TypeEnv>)((e5) => ((Func<TypeEnv, TypeEnv>)((e5b) => ((Func<TypeEnv, TypeEnv>)((e6) => ((Func<TypeEnv, TypeEnv>)((e7) => ((Func<TypeEnv, TypeEnv>)((e8) => ((Func<TypeEnv, TypeEnv>)((e9) => ((Func<TypeEnv, TypeEnv>)((e10) => ((Func<TypeEnv, TypeEnv>)((e10b) => ((Func<TypeEnv, TypeEnv>)((e11) => ((Func<TypeEnv, TypeEnv>)((e12) => ((Func<TypeEnv, TypeEnv>)((e13) => ((Func<TypeEnv, TypeEnv>)((e14) => ((Func<TypeEnv, TypeEnv>)((e15) => ((Func<TypeEnv, TypeEnv>)((e16) => ((Func<TypeEnv, TypeEnv>)((e16b) => ((Func<TypeEnv, TypeEnv>)((e16c) => ((Func<TypeEnv, TypeEnv>)((e16d) => ((Func<TypeEnv, TypeEnv>)((e17) => ((Func<TypeEnv, TypeEnv>)((e18) => ((Func<TypeEnv, TypeEnv>)((e19) => ((Func<TypeEnv, TypeEnv>)((e20) => ((Func<TypeEnv, TypeEnv>)((e21) => ((Func<TypeEnv, TypeEnv>)((e22) => ((Func<TypeEnv, TypeEnv>)((e23) => ((Func<TypeEnv, TypeEnv>)((e24) => ((Func<TypeEnv, TypeEnv>)((e25) => ((Func<TypeEnv, TypeEnv>)((e26) => ((Func<TypeEnv, TypeEnv>)((e27) => ((Func<TypeEnv, TypeEnv>)((e28) => ((Func<TypeEnv, TypeEnv>)((e29) => ((Func<TypeEnv, TypeEnv>)((e30) => ((Func<TypeEnv, TypeEnv>)((e31) => ((Func<TypeEnv, TypeEnv>)((e32) => ((Func<TypeEnv, TypeEnv>)((e33) => ((Func<TypeEnv, TypeEnv>)((e34) => ((Func<TypeEnv, TypeEnv>)((e35) => e35))(env_bind(e34, "\u0015\u000F\u0018\u000D", new ForAllTy(0L, new FunTy(new ListTy(new FunTy(new NothingTy(), new TypeVar(0L))), new TypeVar(0L)))))))(env_bind(e33, "\u001F\u000F\u0015", new ForAllTy(0L, new ForAllTy(1L, new FunTy(new FunTy(new TypeVar(0L), new TypeVar(1L)), new FunTy(new ListTy(new TypeVar(0L)), new ListTy(new TypeVar(1L))))))))))(env_bind(e32, "\u000F\u001B\u000F\u0011\u000E", new ForAllTy(0L, new FunTy(new ConstructedTy(new Name("(\u000F\u0013\""), new List<CodexType>() { new TypeVar(0L) }), new TypeVar(0L)))))))(env_bind(e31, "\u001C\u0010\u0015\"", new ForAllTy(0L, new FunTy(new FunTy(new NothingTy(), new TypeVar(0L)), new ConstructedTy(new Name("(\u000F\u0013\""), new List<CodexType>() { new TypeVar(0L) })))))))(env_bind(e30, "\u0018\u0019\u0015\u0015\u000D\u0012\u000EI\u0016\u0011\u0015", new TextTy()))))(env_bind(e29, "\u001D\u000D\u000EI\u000D\u0012!", new FunTy(new TextTy(), new TextTy())))))(env_bind(e28, "\u001D\u000D\u000EI\u000F\u0015\u001D\u0013", new ListTy(new TextTy())))))(env_bind(e27, "\u000E\u000D$\u000EI\u0013\u000E\u000F\u0015\u000E\u0013I\u001B\u0011\u000E\u0014", new FunTy(new TextTy(), new FunTy(new TextTy(), new BooleanTy()))))))(env_bind(e26, "\u000E\u000D$\u000EI\u0018\u0010\u0012\u000E\u000F\u0011\u0012\u0013", new FunTy(new TextTy(), new FunTy(new TextTy(), new BooleanTy()))))))(env_bind(e25, "\u000E\u000D$\u000EI\u0013\u001F\u0017\u0011\u000E", new FunTy(new TextTy(), new FunTy(new TextTy(), new ListTy(new TextTy())))))))(env_bind(e24, "\u0017\u0011\u0013\u000EI\u001C\u0011\u0017\u000D\u0013", new FunTy(new TextTy(), new FunTy(new TextTy(), new ListTy(new TextTy())))))))(env_bind(e23, "\u001C\u0011\u0017\u000DI\u000D$\u0011\u0013\u000E\u0013", new FunTy(new TextTy(), new BooleanTy())))))(env_bind(e22, "\u001B\u0015\u0011\u000E\u000DI\u001C\u0011\u0017\u000D", new FunTy(new TextTy(), new FunTy(new TextTy(), new NothingTy()))))))(env_bind(e21, "\u0015\u000D\u000F\u0016I\u001C\u0011\u0017\u000D", new FunTy(new TextTy(), new TextTy())))))(env_bind(e20, "\u0015\u000D\u000F\u0016I\u0017\u0011\u0012\u000D", new TextTy()))))(env_bind(e19, "\u001C\u0010\u0017\u0016", new ForAllTy(0L, new ForAllTy(1L, new FunTy(new FunTy(new TypeVar(1L), new FunTy(new TypeVar(0L), new TypeVar(1L))), new FunTy(new TypeVar(1L), new FunTy(new ListTy(new TypeVar(0L)), new TypeVar(1L))))))))))(env_bind(e18, "\u001C\u0011\u0017\u000E\u000D\u0015", new ForAllTy(0L, new FunTy(new FunTy(new TypeVar(0L), new BooleanTy()), new FunTy(new ListTy(new TypeVar(0L)), new ListTy(new TypeVar(0L)))))))))(env_bind(e17, "\u001A\u000F\u001F", new ForAllTy(0L, new ForAllTy(1L, new FunTy(new FunTy(new TypeVar(0L), new TypeVar(1L)), new FunTy(new ListTy(new TypeVar(0L)), new ListTy(new TypeVar(1L))))))))))(env_bind(e16d, "\u0017\u0011\u0013\u000EI\u000F\u000E", new ForAllTy(0L, new FunTy(new ListTy(new TypeVar(0L)), new FunTy(new IntegerTy(), new TypeVar(0L))))))))(env_bind(e16c, "\u0017\u0011\u0013\u000EI\u0013\u0012\u0010\u0018", new ForAllTy(0L, new FunTy(new ListTy(new TypeVar(0L)), new FunTy(new TypeVar(0L), new ListTy(new TypeVar(0L)))))))))(env_bind(e16b, "\u000E\u000D$\u000EI\u0018\u0010\u001A\u001F\u000F\u0015\u000D", new FunTy(new TextTy(), new FunTy(new TextTy(), new IntegerTy()))))))(env_bind(e16, "\u0017\u0011\u0013\u000EI\u0011\u0012\u0013\u000D\u0015\u000EI\u000F\u000E", new ForAllTy(0L, new FunTy(new ListTy(new TypeVar(0L)), new FunTy(new IntegerTy(), new FunTy(new TypeVar(0L), new ListTy(new TypeVar(0L))))))))))(env_bind(e15, "\u0017\u0011\u0013\u000EI\u0017\u000D\u0012\u001D\u000E\u0014", new ForAllTy(0L, new FunTy(new ListTy(new TypeVar(0L)), new IntegerTy()))))))(env_bind(e14, "\u001F\u0015\u0011\u0012\u000EI\u0017\u0011\u0012\u000D", new FunTy(new TextTy(), new NothingTy())))))(env_bind(e13, "\u0013\u0014\u0010\u001B", new ForAllTy(0L, new FunTy(new TypeVar(0L), new TextTy()))))))(env_bind(e12, "\u000E\u000D$\u000EI\u000E\u0010I\u0011\u0012\u000E\u000D\u001D\u000D\u0015", new FunTy(new TextTy(), new IntegerTy())))))(env_bind(e11, "\u000E\u000D$\u000EI\u0015\u000D\u001F\u0017\u000F\u0018\u000D", new FunTy(new TextTy(), new FunTy(new TextTy(), new FunTy(new TextTy(), new TextTy())))))))(env_bind(e10b, "\u0018\u0010\u0016\u000DI\u000E\u0010I\u0018\u0014\u000F\u0015", new FunTy(new IntegerTy(), new CharTy())))))(env_bind(e10, "\u0018\u0014\u000F\u0015I\u0018\u0010\u0016\u000DI\u000F\u000E", new FunTy(new TextTy(), new FunTy(new IntegerTy(), new IntegerTy()))))))(env_bind(e9, "\u0018\u0014\u000F\u0015I\u0018\u0010\u0016\u000D", new FunTy(new CharTy(), new IntegerTy())))))(env_bind(e8, "\u0011\u0013I\u001B\u0014\u0011\u000E\u000D\u0013\u001F\u000F\u0018\u000D", new FunTy(new CharTy(), new BooleanTy())))))(env_bind(e7, "\u0011\u0013I\u0016\u0011\u001D\u0011\u000E", new FunTy(new CharTy(), new BooleanTy())))))(env_bind(e6, "\u0011\u0013I\u0017\u000D\u000E\u000E\u000D\u0015", new FunTy(new CharTy(), new BooleanTy())))))(env_bind(e5b, "\u0013\u0019 \u0013\u000E\u0015\u0011\u0012\u001D", new FunTy(new TextTy(), new FunTy(new IntegerTy(), new FunTy(new IntegerTy(), new TextTy())))))))(env_bind(e5, "\u0018\u0014\u000F\u0015I\u000E\u0010I\u000E\u000D$\u000E", new FunTy(new CharTy(), new TextTy())))))(env_bind(e4, "\u0018\u0014\u000F\u0015I\u000F\u000E", new FunTy(new TextTy(), new FunTy(new IntegerTy(), new CharTy()))))))(env_bind(e3, "\u0011\u0012\u000E\u000D\u001D\u000D\u0015I\u000E\u0010I\u000E\u000D$\u000E", new FunTy(new IntegerTy(), new TextTy())))))(env_bind(e2, "\u000E\u000D$\u000EI\u0017\u000D\u0012\u001D\u000E\u0014", new FunTy(new TextTy(), new IntegerTy())))))(env_bind(e, "\u0012\u000D\u001D\u000F\u000E\u000D", new FunTy(new IntegerTy(), new IntegerTy())))))(empty_type_env());
    }

    public static UnificationState empty_unification_state()
    {
        return new UnificationState(new List<SubstEntry>(), 2L, new List<Diagnostic>());
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
        return ((Func<long, CodexType>)((len) => ((len == 0L) ? new ErrorTy() : ((Func<long, CodexType>)((pos) => ((pos >= len) ? new ErrorTy() : ((Func<SubstEntry, CodexType>)((entry) => ((entry.var_id == var_id) ? entry.resolved_type : new ErrorTy())))(entries[(int)pos]))))(bsearch_int_pos(entries, var_id, 0L, len)))))(((long)entries.Count));
    }

    public static bool has_subst(long var_id, List<SubstEntry> entries)
    {
        return ((Func<long, bool>)((len) => ((len == 0L) ? false : ((Func<long, bool>)((pos) => ((pos >= len) ? false : (entries[(int)pos].var_id == var_id))))(bsearch_int_pos(entries, var_id, 0L, len)))))(((long)entries.Count));
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
        return ((Func<SubstEntry, UnificationState>)((entry) => ((Func<long, UnificationState>)((pos) => new UnificationState(((Func<List<SubstEntry>>)(() => { var _l = new List<SubstEntry>(st.substitutions); _l.Insert((int)pos, entry); return _l; }))(), st.next_id, st.errors)))(bsearch_int_pos(st.substitutions, var_id, 0L, ((long)st.substitutions.Count)))))(new SubstEntry(var_id, ty));
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
        return (types_equal(a, b) ? new UnifyResult(true, st) : (a is TypeVar _mTypeVar130_ ? ((Func<long, UnifyResult>)((id_a) => (occurs_in(st, id_a, b) ? new UnifyResult(false, add_unify_error(st, "20>\u0005\u0003\u0004\u0003", "+\u0012\u001C\u0011\u0012\u0011\u000E\u000D\u0002\u000E\u001E\u001F\u000D")) : new UnifyResult(true, add_subst(st, id_a, b)))))((long)_mTypeVar130_.Field0) : ((Func<CodexType, UnifyResult>)((_) => unify_rhs(st, a, b)))(a)));
    }

    public static bool types_equal(CodexType a, CodexType b)
    {
        return ((Func<CodexType, bool>)((_scrutinee131_) => (_scrutinee131_ is TypeVar _mTypeVar131_ ? ((Func<long, bool>)((id_a) => (b is TypeVar _mTypeVar132_ ? ((Func<long, bool>)((id_b) => (id_a == id_b)))((long)_mTypeVar132_.Field0) : ((Func<CodexType, bool>)((_) => false))(b))))((long)_mTypeVar131_.Field0) : (_scrutinee131_ is IntegerTy _mIntegerTy131_ ? (b is IntegerTy _mIntegerTy133_ ? true : ((Func<CodexType, bool>)((_) => false))(b)) : (_scrutinee131_ is NumberTy _mNumberTy131_ ? (b is NumberTy _mNumberTy134_ ? true : ((Func<CodexType, bool>)((_) => false))(b)) : (_scrutinee131_ is TextTy _mTextTy131_ ? (b is TextTy _mTextTy135_ ? true : ((Func<CodexType, bool>)((_) => false))(b)) : (_scrutinee131_ is BooleanTy _mBooleanTy131_ ? (b is BooleanTy _mBooleanTy136_ ? true : ((Func<CodexType, bool>)((_) => false))(b)) : (_scrutinee131_ is CharTy _mCharTy131_ ? (b is CharTy _mCharTy137_ ? true : ((Func<CodexType, bool>)((_) => false))(b)) : (_scrutinee131_ is NothingTy _mNothingTy131_ ? (b is NothingTy _mNothingTy138_ ? true : ((Func<CodexType, bool>)((_) => false))(b)) : (_scrutinee131_ is VoidTy _mVoidTy131_ ? (b is VoidTy _mVoidTy139_ ? true : ((Func<CodexType, bool>)((_) => false))(b)) : (_scrutinee131_ is ErrorTy _mErrorTy131_ ? (b is ErrorTy _mErrorTy140_ ? true : ((Func<CodexType, bool>)((_) => false))(b)) : ((Func<CodexType, bool>)((_) => false))(_scrutinee131_))))))))))))(a);
    }

    public static UnifyResult unify_rhs(UnificationState st, CodexType a, CodexType b)
    {
        return (b is TypeVar _mTypeVar141_ ? ((Func<long, UnifyResult>)((id_b) => (occurs_in(st, id_b, a) ? new UnifyResult(false, add_unify_error(st, "20>\u0005\u0003\u0004\u0003", "+\u0012\u001C\u0011\u0012\u0011\u000E\u000D\u0002\u000E\u001E\u001F\u000D")) : new UnifyResult(true, add_subst(st, id_b, a)))))((long)_mTypeVar141_.Field0) : ((Func<CodexType, UnifyResult>)((_) => unify_structural(st, a, b)))(b));
    }

    public static UnifyResult unify_structural(UnificationState st, CodexType a, CodexType b)
    {
        return ((Func<CodexType, UnifyResult>)((_scrutinee142_) => (_scrutinee142_ is IntegerTy _mIntegerTy142_ ? ((Func<CodexType, UnifyResult>)((_scrutinee143_) => (_scrutinee143_ is IntegerTy _mIntegerTy143_ ? new UnifyResult(true, st) : (_scrutinee143_ is ErrorTy _mErrorTy143_ ? new UnifyResult(true, st) : ((Func<CodexType, UnifyResult>)((_) => unify_mismatch(st, a, b)))(_scrutinee143_)))))(b) : (_scrutinee142_ is NumberTy _mNumberTy142_ ? ((Func<CodexType, UnifyResult>)((_scrutinee144_) => (_scrutinee144_ is NumberTy _mNumberTy144_ ? new UnifyResult(true, st) : (_scrutinee144_ is ErrorTy _mErrorTy144_ ? new UnifyResult(true, st) : ((Func<CodexType, UnifyResult>)((_) => unify_mismatch(st, a, b)))(_scrutinee144_)))))(b) : (_scrutinee142_ is TextTy _mTextTy142_ ? ((Func<CodexType, UnifyResult>)((_scrutinee145_) => (_scrutinee145_ is TextTy _mTextTy145_ ? new UnifyResult(true, st) : (_scrutinee145_ is ErrorTy _mErrorTy145_ ? new UnifyResult(true, st) : ((Func<CodexType, UnifyResult>)((_) => unify_mismatch(st, a, b)))(_scrutinee145_)))))(b) : (_scrutinee142_ is BooleanTy _mBooleanTy142_ ? ((Func<CodexType, UnifyResult>)((_scrutinee146_) => (_scrutinee146_ is BooleanTy _mBooleanTy146_ ? new UnifyResult(true, st) : (_scrutinee146_ is ErrorTy _mErrorTy146_ ? new UnifyResult(true, st) : ((Func<CodexType, UnifyResult>)((_) => unify_mismatch(st, a, b)))(_scrutinee146_)))))(b) : (_scrutinee142_ is NothingTy _mNothingTy142_ ? ((Func<CodexType, UnifyResult>)((_scrutinee147_) => (_scrutinee147_ is NothingTy _mNothingTy147_ ? new UnifyResult(true, st) : (_scrutinee147_ is ErrorTy _mErrorTy147_ ? new UnifyResult(true, st) : ((Func<CodexType, UnifyResult>)((_) => unify_mismatch(st, a, b)))(_scrutinee147_)))))(b) : (_scrutinee142_ is VoidTy _mVoidTy142_ ? ((Func<CodexType, UnifyResult>)((_scrutinee148_) => (_scrutinee148_ is VoidTy _mVoidTy148_ ? new UnifyResult(true, st) : (_scrutinee148_ is ErrorTy _mErrorTy148_ ? new UnifyResult(true, st) : ((Func<CodexType, UnifyResult>)((_) => unify_mismatch(st, a, b)))(_scrutinee148_)))))(b) : (_scrutinee142_ is ErrorTy _mErrorTy142_ ? new UnifyResult(true, st) : (_scrutinee142_ is FunTy _mFunTy142_ ? ((Func<CodexType, UnifyResult>)((ra) => ((Func<CodexType, UnifyResult>)((pa) => ((Func<CodexType, UnifyResult>)((_scrutinee149_) => (_scrutinee149_ is FunTy _mFunTy149_ ? ((Func<CodexType, UnifyResult>)((rb) => ((Func<CodexType, UnifyResult>)((pb) => unify_fun(st, pa, ra, pb, rb)))((CodexType)_mFunTy149_.Field0)))((CodexType)_mFunTy149_.Field1) : (_scrutinee149_ is ErrorTy _mErrorTy149_ ? new UnifyResult(true, st) : ((Func<CodexType, UnifyResult>)((_) => unify_mismatch(st, a, b)))(_scrutinee149_)))))(b)))((CodexType)_mFunTy142_.Field0)))((CodexType)_mFunTy142_.Field1) : (_scrutinee142_ is ListTy _mListTy142_ ? ((Func<CodexType, UnifyResult>)((ea) => ((Func<CodexType, UnifyResult>)((_scrutinee150_) => (_scrutinee150_ is ListTy _mListTy150_ ? ((Func<CodexType, UnifyResult>)((eb) => unify(st, ea, eb)))((CodexType)_mListTy150_.Field0) : (_scrutinee150_ is ErrorTy _mErrorTy150_ ? new UnifyResult(true, st) : ((Func<CodexType, UnifyResult>)((_) => unify_mismatch(st, a, b)))(_scrutinee150_)))))(b)))((CodexType)_mListTy142_.Field0) : (_scrutinee142_ is ConstructedTy _mConstructedTy142_ ? ((Func<List<CodexType>, UnifyResult>)((args_a) => ((Func<Name, UnifyResult>)((na) => ((Func<CodexType, UnifyResult>)((_scrutinee151_) => (_scrutinee151_ is ConstructedTy _mConstructedTy151_ ? ((Func<List<CodexType>, UnifyResult>)((args_b) => ((Func<Name, UnifyResult>)((nb) => ((na.value == nb.value) ? unify_constructed_args(st, args_a, args_b, 0L, ((long)args_a.Count)) : unify_mismatch(st, a, b))))((Name)_mConstructedTy151_.Field0)))((List<CodexType>)_mConstructedTy151_.Field1) : (_scrutinee151_ is SumTy _mSumTy151_ ? ((Func<List<SumCtor>, UnifyResult>)((sb_ctors) => ((Func<Name, UnifyResult>)((sb_name) => ((na.value == sb_name.value) ? new UnifyResult(true, st) : unify_mismatch(st, a, b))))((Name)_mSumTy151_.Field0)))((List<SumCtor>)_mSumTy151_.Field1) : (_scrutinee151_ is RecordTy _mRecordTy151_ ? ((Func<List<RecordField>, UnifyResult>)((rb_fields) => ((Func<Name, UnifyResult>)((rb_name) => ((na.value == rb_name.value) ? new UnifyResult(true, st) : unify_mismatch(st, a, b))))((Name)_mRecordTy151_.Field0)))((List<RecordField>)_mRecordTy151_.Field1) : (_scrutinee151_ is ErrorTy _mErrorTy151_ ? new UnifyResult(true, st) : ((Func<CodexType, UnifyResult>)((_) => unify_mismatch(st, a, b)))(_scrutinee151_)))))))(b)))((Name)_mConstructedTy142_.Field0)))((List<CodexType>)_mConstructedTy142_.Field1) : (_scrutinee142_ is SumTy _mSumTy142_ ? ((Func<List<SumCtor>, UnifyResult>)((sa_ctors) => ((Func<Name, UnifyResult>)((sa_name) => ((Func<CodexType, UnifyResult>)((_scrutinee152_) => (_scrutinee152_ is SumTy _mSumTy152_ ? ((Func<List<SumCtor>, UnifyResult>)((sb_ctors) => ((Func<Name, UnifyResult>)((sb_name) => ((sa_name.value == sb_name.value) ? new UnifyResult(true, st) : unify_mismatch(st, a, b))))((Name)_mSumTy152_.Field0)))((List<SumCtor>)_mSumTy152_.Field1) : (_scrutinee152_ is ConstructedTy _mConstructedTy152_ ? ((Func<List<CodexType>, UnifyResult>)((args_b) => ((Func<Name, UnifyResult>)((nb) => ((sa_name.value == nb.value) ? new UnifyResult(true, st) : unify_mismatch(st, a, b))))((Name)_mConstructedTy152_.Field0)))((List<CodexType>)_mConstructedTy152_.Field1) : (_scrutinee152_ is ErrorTy _mErrorTy152_ ? new UnifyResult(true, st) : ((Func<CodexType, UnifyResult>)((_) => unify_mismatch(st, a, b)))(_scrutinee152_))))))(b)))((Name)_mSumTy142_.Field0)))((List<SumCtor>)_mSumTy142_.Field1) : (_scrutinee142_ is RecordTy _mRecordTy142_ ? ((Func<List<RecordField>, UnifyResult>)((ra_fields) => ((Func<Name, UnifyResult>)((ra_name) => ((Func<CodexType, UnifyResult>)((_scrutinee153_) => (_scrutinee153_ is RecordTy _mRecordTy153_ ? ((Func<List<RecordField>, UnifyResult>)((rb_fields) => ((Func<Name, UnifyResult>)((rb_name) => ((ra_name.value == rb_name.value) ? new UnifyResult(true, st) : unify_mismatch(st, a, b))))((Name)_mRecordTy153_.Field0)))((List<RecordField>)_mRecordTy153_.Field1) : (_scrutinee153_ is ConstructedTy _mConstructedTy153_ ? ((Func<List<CodexType>, UnifyResult>)((args_b) => ((Func<Name, UnifyResult>)((nb) => ((ra_name.value == nb.value) ? new UnifyResult(true, st) : unify_mismatch(st, a, b))))((Name)_mConstructedTy153_.Field0)))((List<CodexType>)_mConstructedTy153_.Field1) : (_scrutinee153_ is ErrorTy _mErrorTy153_ ? new UnifyResult(true, st) : ((Func<CodexType, UnifyResult>)((_) => unify_mismatch(st, a, b)))(_scrutinee153_))))))(b)))((Name)_mRecordTy142_.Field0)))((List<RecordField>)_mRecordTy142_.Field1) : (_scrutinee142_ is ForAllTy _mForAllTy142_ ? ((Func<CodexType, UnifyResult>)((body) => ((Func<long, UnifyResult>)((id) => unify(st, body, b)))((long)_mForAllTy142_.Field0)))((CodexType)_mForAllTy142_.Field1) : ((Func<CodexType, UnifyResult>)((_) => (b is ErrorTy _mErrorTy154_ ? new UnifyResult(true, st) : ((Func<CodexType, UnifyResult>)((_) => unify_mismatch(st, a, b)))(b))))(_scrutinee142_))))))))))))))))(a);
    }

    public static UnifyResult unify_constructed_args(UnificationState st, List<CodexType> args_a, List<CodexType> args_b, long i, long len)
    {
        while (true)
        {
            if ((i == len))
            {
                return new UnifyResult(true, st);
            }
            else
            {
                if ((i >= ((long)args_b.Count)))
                {
                    return new UnifyResult(true, st);
                }
                else
                {
                    var r = unify(st, args_a[(int)i], args_b[(int)i]);
                    if (r.success)
                    {
                        var _tco_0 = r.state;
                        var _tco_1 = args_a;
                        var _tco_2 = args_b;
                        var _tco_3 = (i + 1L);
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

    public static UnifyResult unify_fun(UnificationState st, CodexType pa, CodexType ra, CodexType pb, CodexType rb)
    {
        return ((Func<UnifyResult, UnifyResult>)((r1) => (r1.success ? unify(r1.state, ra, rb) : r1)))(unify(st, pa, pb));
    }

    public static UnifyResult unify_mismatch(UnificationState st, CodexType a, CodexType b)
    {
        return new UnifyResult(false, add_unify_error(st, "20>\u0005\u0003\u0003\u0004", string.Concat("(\u001E\u001F\u000D\u0002\u001A\u0011\u0013\u001A\u000F\u000E\u0018\u0014E\u0002", type_tag(a), "\u0002!\u0013\u0002", type_tag(b))));
    }

    public static string type_tag(CodexType ty)
    {
        return ((Func<CodexType, string>)((_scrutinee155_) => (_scrutinee155_ is IntegerTy _mIntegerTy155_ ? "+\u0012\u000E\u000D\u001D\u000D\u0015" : (_scrutinee155_ is NumberTy _mNumberTy155_ ? ",\u0019\u001A \u000D\u0015" : (_scrutinee155_ is TextTy _mTextTy155_ ? "(\u000D$\u000E" : (_scrutinee155_ is BooleanTy _mBooleanTy155_ ? ":\u0010\u0010\u0017\u000D\u000F\u0012" : (_scrutinee155_ is CharTy _mCharTy155_ ? "2\u0014\u000F\u0015" : (_scrutinee155_ is VoidTy _mVoidTy155_ ? ";\u0010\u0011\u0016" : (_scrutinee155_ is NothingTy _mNothingTy155_ ? ",\u0010\u000E\u0014\u0011\u0012\u001D" : (_scrutinee155_ is ErrorTy _mErrorTy155_ ? "'\u0015\u0015\u0010\u0015" : (_scrutinee155_ is FunTy _mFunTy155_ ? ((Func<CodexType, string>)((r) => ((Func<CodexType, string>)((p) => "6\u0019\u0012"))((CodexType)_mFunTy155_.Field0)))((CodexType)_mFunTy155_.Field1) : (_scrutinee155_ is ListTy _mListTy155_ ? ((Func<CodexType, string>)((e) => "1\u0011\u0013\u000E"))((CodexType)_mListTy155_.Field0) : (_scrutinee155_ is TypeVar _mTypeVar155_ ? ((Func<long, string>)((id) => string.Concat("(", _Cce.FromUnicode(id.ToString()))))((long)_mTypeVar155_.Field0) : (_scrutinee155_ is ForAllTy _mForAllTy155_ ? ((Func<CodexType, string>)((body) => ((Func<long, string>)((id) => "6\u0010\u0015)\u0017\u0017"))((long)_mForAllTy155_.Field0)))((CodexType)_mForAllTy155_.Field1) : (_scrutinee155_ is SumTy _mSumTy155_ ? ((Func<List<SumCtor>, string>)((ctors) => ((Func<Name, string>)((name) => string.Concat("-\u0019\u001AE", name.value)))((Name)_mSumTy155_.Field0)))((List<SumCtor>)_mSumTy155_.Field1) : (_scrutinee155_ is RecordTy _mRecordTy155_ ? ((Func<List<RecordField>, string>)((fields) => ((Func<Name, string>)((name) => string.Concat("/\u000D\u0018E", name.value)))((Name)_mRecordTy155_.Field0)))((List<RecordField>)_mRecordTy155_.Field1) : (_scrutinee155_ is ConstructedTy _mConstructedTy155_ ? ((Func<List<CodexType>, string>)((args) => ((Func<Name, string>)((name) => string.Concat("2\u0010\u0012E", name.value)))((Name)_mConstructedTy155_.Field0)))((List<CodexType>)_mConstructedTy155_.Field1) : throw new InvalidOperationException("Non-exhaustive match"))))))))))))))))))(ty);
    }

    public static CodexType deep_resolve(UnificationState st, CodexType ty)
    {
        return ((Func<CodexType, CodexType>)((resolved) => ((Func<CodexType, CodexType>)((_scrutinee156_) => (_scrutinee156_ is FunTy _mFunTy156_ ? ((Func<CodexType, CodexType>)((ret) => ((Func<CodexType, CodexType>)((param) => new FunTy(deep_resolve(st, param), deep_resolve(st, ret))))((CodexType)_mFunTy156_.Field0)))((CodexType)_mFunTy156_.Field1) : (_scrutinee156_ is ListTy _mListTy156_ ? ((Func<CodexType, CodexType>)((elem) => new ListTy(deep_resolve(st, elem))))((CodexType)_mListTy156_.Field0) : (_scrutinee156_ is ConstructedTy _mConstructedTy156_ ? ((Func<List<CodexType>, CodexType>)((args) => ((Func<Name, CodexType>)((name) => new ConstructedTy(name, deep_resolve_list(st, args, 0L, ((long)args.Count), new List<CodexType>()))))((Name)_mConstructedTy156_.Field0)))((List<CodexType>)_mConstructedTy156_.Field1) : (_scrutinee156_ is ForAllTy _mForAllTy156_ ? ((Func<CodexType, CodexType>)((body) => ((Func<long, CodexType>)((id) => new ForAllTy(id, deep_resolve(st, body))))((long)_mForAllTy156_.Field0)))((CodexType)_mForAllTy156_.Field1) : (_scrutinee156_ is SumTy _mSumTy156_ ? ((Func<List<SumCtor>, CodexType>)((ctors) => ((Func<Name, CodexType>)((name) => resolved))((Name)_mSumTy156_.Field0)))((List<SumCtor>)_mSumTy156_.Field1) : (_scrutinee156_ is RecordTy _mRecordTy156_ ? ((Func<List<RecordField>, CodexType>)((fields) => ((Func<Name, CodexType>)((name) => resolved))((Name)_mRecordTy156_.Field0)))((List<RecordField>)_mRecordTy156_.Field1) : ((Func<CodexType, CodexType>)((_) => resolved))(_scrutinee156_)))))))))(resolved)))(resolve(st, ty));
    }

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

    public static string compile(string source, string module_name)
    {
        return ((Func<List<Token>, string>)((tokens) => ((Func<ParseState, string>)((st) => ((Func<Document, string>)((doc) => ((Func<AModule, string>)((ast) => ((Func<ModuleResult, string>)((check_result) => ((Func<IRModule, string>)((ir) => emit_full_module(ir, ast.type_defs)))(lower_module(ast, check_result.types, check_result.state))))(check_module(ast))))(desugar_document(doc, module_name))))(parse_document(st))))(make_parse_state(tokens))))(tokenize(source));
    }

    public static CompileResult compile_checked(string source, string module_name)
    {
        return compile_with_imports(source, module_name, new List<ResolveResult>());
    }

    public static CompileResult compile_with_imports(string source, string module_name, List<ResolveResult> imported)
    {
        return ((Func<List<Token>, CompileResult>)((tokens) => ((Func<ParseState, CompileResult>)((st) => ((Func<Document, CompileResult>)((doc) => ((Func<AModule, CompileResult>)((ast) => ((Func<ResolveResult, CompileResult>)((resolve_result) => ((((long)resolve_result.errors.Count) > 0L) ? new CompileError(resolve_result.errors) : ((Func<ModuleResult, CompileResult>)((check_result) => ((Func<IRModule, CompileResult>)((ir) => new CompileOk(emit_full_module(ir, ast.type_defs), check_result)))(lower_module(ast, check_result.types, check_result.state))))(check_module(ast)))))(resolve_module_with_imports(ast, imported))))(desugar_document(doc, module_name))))(parse_document(st))))(make_parse_state(tokens))))(tokenize(source));
    }

    public static object compile_streaming(string source, string module_name)
    {
        return ((Func<List<Token>, object>)((tokens) => ((Func<ParseState, object>)((st) => ((Func<Document, object>)((doc) => ((Func<AModule, object>)((ast) => ((Func<ModuleResult, object>)((check_result) => ((Func<List<TypeBinding>, object>)((ctor_types) => ((Func<List<TypeBinding>, object>)((all_types) => ((Func<List<string>, object>)((ctor_names) => ((Func<object>)(() => {
                ((Func<object>)(() => { Console.WriteLine(_Cce.ToUnicode(codex_emit_type_defs(ast.type_defs, 0L))); return null; }))();
                stream_defs(ast.defs, all_types, check_result.state, ctor_names, 0L, ((long)ast.defs.Count));
                ((Func<object>)(() => { Console.WriteLine(_Cce.ToUnicode("")); return null; }))();
                return null;
            }))()))(codex_collect_ctor_names(ast.type_defs, 0L))))(Enumerable.Concat(ctor_types, Enumerable.Concat(check_result.types, builtin_type_env().bindings).ToList()).ToList())))(collect_ctor_bindings(ast.type_defs, 0L, ((long)ast.type_defs.Count), new List<TypeBinding>()))))(check_module(ast))))(desugar_document(doc, module_name))))(parse_document(st))))(make_parse_state(tokens))))(tokenize(source));
    }

    public static object stream_defs(List<ADef> defs, List<TypeBinding> types, UnificationState ust, List<string> ctor_names, long i, long len)
    {
        return ((i == len) ? ((Func<object>)(() => { Console.WriteLine(_Cce.ToUnicode("")); return null; }))() : ((Func<ADef, object>)((def) => ((Func<IRDef, object>)((ir_def) => ((Func<string, object>)((text) => ((Func<object>)(() => {
                ((Func<object>)(() => { Console.WriteLine(_Cce.ToUnicode(text)); return null; }))();
                stream_defs(defs, types, ust, ctor_names, (i + 1L), len);
                return null;
            }))()))(codex_emit_def(ir_def, ctor_names))))(lower_def(def, types, ust))))(defs[(int)i]));
    }

    public static object main()
    {
        return ((Func<object>)(() => {
                var path = _Cce.FromUnicode(Console.ReadLine() ?? "");
                var source = _Cce.FromUnicode(File.ReadAllText(_Cce.ToUnicode(path)));
                compile_streaming(source, "9\u0015\u0010\u001D\u0015\u000F\u001A");
                return null;
            }))();
    }

}
