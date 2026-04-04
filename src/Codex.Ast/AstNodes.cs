using Codex.Core;

namespace Codex.Ast;

public sealed record Chapter(
    QualifiedName Name,
    IReadOnlyList<Definition> Definitions,
    IReadOnlyList<TypeDef> TypeDefinitions,
    IReadOnlyList<ClaimDef> Claims,
    IReadOnlyList<ProofDef> Proofs,
    SourceSpan Span)
{
    public IReadOnlyList<CitesDecl> Citations { get; init; } = [];
    public IReadOnlyList<ExportDecl> Exports { get; init; } = [];
    public IReadOnlyList<EffectDef> EffectDefs { get; init; } = [];
}

public sealed record Definition(
    Name Name,
    IReadOnlyList<Parameter> Parameters,
    TypeExpr? DeclaredType,
    Expr Body,
    SourceSpan Span)
{
    public string? SourceChapter { get; init; }
}

public sealed record Parameter(Name Name, TypeExpr? TypeAnnotation, SourceSpan Span);

public sealed record CitesDecl(Name ChapterName, SourceSpan Span)
{
    public IReadOnlyList<Name> SelectedNames { get; init; } = [];
}

public sealed record ExportDecl(IReadOnlyList<Name> Names, SourceSpan Span);

public sealed record EffectDef(
    Name EffectName,
    IReadOnlyList<EffectOperationDef> Operations,
    SourceSpan Span);

public sealed record EffectOperationDef(Name Name, TypeExpr Type, SourceSpan Span);

public abstract record Expr(SourceSpan Span);

public sealed record LiteralExpr(object Value, LiteralKind Kind, SourceSpan Span) : Expr(Span);

public enum LiteralKind
{
    Integer,
    Number,
    Text,
    Boolean,
    Char
}

public sealed record NameExpr(Name Name, SourceSpan Span) : Expr(Span);

public sealed record ApplyExpr(Expr Function, Expr Argument, SourceSpan Span) : Expr(Span);

public sealed record BinaryExpr(Expr Left, BinaryOp Op, Expr Right, SourceSpan Span) : Expr(Span);

public enum BinaryOp
{
    Add, Sub, Mul, Div, Pow,
    Eq, NotEq, Lt, Gt, LtEq, GtEq, DefEq,
    Append, Cons,
    And, Or
}

public sealed record UnaryExpr(UnaryOp Op, Expr Operand, SourceSpan Span) : Expr(Span);

public enum UnaryOp { Negate }

public sealed record IfExpr(Expr Condition, Expr Then, Expr Else, SourceSpan Span) : Expr(Span);

public sealed record LetExpr(IReadOnlyList<LetBinding> Bindings, Expr Body, SourceSpan Span) : Expr(Span);

public sealed record LetBinding(Name Name, Expr Value);

public sealed record LambdaExpr(IReadOnlyList<Parameter> Parameters, Expr Body, SourceSpan Span) : Expr(Span);

public sealed record MatchExpr(Expr Scrutinee, IReadOnlyList<MatchBranch> Branches, SourceSpan Span) : Expr(Span);

public sealed record MatchBranch(Pattern Pattern, Expr Body, SourceSpan Span);

public sealed record ListExpr(IReadOnlyList<Expr> Elements, SourceSpan Span) : Expr(Span);

public sealed record RecordExpr(Name? TypeName, IReadOnlyList<RecordFieldExpr> Fields, SourceSpan Span) : Expr(Span);

public sealed record RecordFieldExpr(Name FieldName, Expr Value, SourceSpan Span);

public sealed record FieldAccessExpr(Expr Record, Name FieldName, SourceSpan Span) : Expr(Span);

public sealed record ErrorExpr(string Message, SourceSpan Span) : Expr(Span);

public sealed record DoExpr(IReadOnlyList<DoStatement> Statements, SourceSpan Span) : Expr(Span);

public sealed record HandleExpr(
    Expr Computation,
    Name EffectName,
    IReadOnlyList<HandleClause> Clauses,
    SourceSpan Span) : Expr(Span);

public sealed record HandleClause(
    Name OperationName,
    IReadOnlyList<Name> Parameters,
    Name ResumeName,
    Expr Body,
    SourceSpan Span);

public abstract record DoStatement(SourceSpan Span);

public sealed record DoBindStatement(Name Name, Expr Value, SourceSpan Span) : DoStatement(Span);

public sealed record DoExprStatement(Expr Expression, SourceSpan Span) : DoStatement(Span);

public abstract record Pattern(SourceSpan Span);

public sealed record VarPattern(Name Name, SourceSpan Span) : Pattern(Span);

public sealed record LiteralPattern(object Value, LiteralKind Kind, SourceSpan Span) : Pattern(Span);

public sealed record CtorPattern(Name Constructor, IReadOnlyList<Pattern> SubPatterns, SourceSpan Span) : Pattern(Span);

public sealed record WildcardPattern(SourceSpan Span) : Pattern(Span);

public abstract record TypeExpr(SourceSpan Span);

public sealed record NamedTypeExpr(Name Name, SourceSpan Span) : TypeExpr(Span);

public sealed record FunctionTypeExpr(TypeExpr Parameter, TypeExpr Return, SourceSpan Span) : TypeExpr(Span);

public sealed record AppliedTypeExpr(TypeExpr Constructor, IReadOnlyList<TypeExpr> Arguments, SourceSpan Span) : TypeExpr(Span);

public sealed record EffectfulTypeExpr(IReadOnlyList<TypeExpr> Effects, TypeExpr Return, SourceSpan Span) : TypeExpr(Span);

public sealed record LinearTypeExpr(TypeExpr Inner, SourceSpan Span) : TypeExpr(Span);

public sealed record DependentTypeExpr(Name ParamName, TypeExpr ParamType, TypeExpr Body, SourceSpan Span) : TypeExpr(Span);

public sealed record IntegerLiteralTypeExpr(long Value, SourceSpan Span) : TypeExpr(Span);

public sealed record BinaryTypeExpr(TypeExpr Left, BinaryOp Op, TypeExpr Right, SourceSpan Span) : TypeExpr(Span);

public sealed record ProofConstraintExpr(TypeExpr Left, BinaryOp Op, TypeExpr Right, SourceSpan Span) : TypeExpr(Span);

public abstract record TypeDef(Name Name, IReadOnlyList<Name> TypeParameters, SourceSpan Span)
{
    public string? SourceChapter { get; init; }
}

public sealed record RecordTypeDef(
    Name Name,
    IReadOnlyList<Name> TypeParameters,
    IReadOnlyList<RecordFieldDef> Fields,
    SourceSpan Span)
    : TypeDef(Name, TypeParameters, Span);

public sealed record RecordFieldDef(Name FieldName, TypeExpr Type, SourceSpan Span);

public sealed record VariantTypeDef(
    Name Name,
    IReadOnlyList<Name> TypeParameters,
    IReadOnlyList<VariantCtorDef> Constructors,
    SourceSpan Span)
    : TypeDef(Name, TypeParameters, Span);

public sealed record VariantCtorDef(
    Name Name,
    IReadOnlyList<VariantFieldDef> Fields,
    SourceSpan Span);

public sealed record VariantFieldDef(Name? FieldName, TypeExpr Type, SourceSpan Span);

public sealed record ClaimDef(Name Name, IReadOnlyList<Parameter> Parameters, TypeExpr Left, TypeExpr Right, SourceSpan Span);

public sealed record ProofDef(Name Name, IReadOnlyList<Parameter> Parameters, ProofExpr Body, SourceSpan Span);

public abstract record ProofExpr(SourceSpan Span);

public sealed record ReflProofExpr(SourceSpan Span) : ProofExpr(Span);

public sealed record AssumeProofExpr(SourceSpan Span) : ProofExpr(Span);

public sealed record SymProofExpr(ProofExpr Inner, SourceSpan Span) : ProofExpr(Span);

public sealed record TransProofExpr(ProofExpr Left, ProofExpr Right, SourceSpan Span) : ProofExpr(Span);

public sealed record CongProofExpr(Name FunctionName, ProofExpr Inner, SourceSpan Span) : ProofExpr(Span);

public sealed record InductionProofExpr(
    Name Variable,
    IReadOnlyList<ProofCase> Cases,
    SourceSpan Span) : ProofExpr(Span);

public sealed record ProofCase(Pattern Pattern, ProofExpr Body, SourceSpan Span);

public sealed record NameProofExpr(Name Name, SourceSpan Span) : ProofExpr(Span);

public sealed record ApplyProofExpr(Name LemmaName, IReadOnlyList<Expr> Arguments, SourceSpan Span) : ProofExpr(Span);
