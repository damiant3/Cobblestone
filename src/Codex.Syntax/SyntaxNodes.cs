using Codex.Core;

namespace Codex.Syntax;

public enum SyntaxKind
{
    // Top-level
    Document,
    Definition,
    TypeDefinition,
    EffectDefinition,
    EffectOperation,
    HandleExpression,
    HandleClause,

    // Prose structure
    Chapter,
    Section,
    ProseBlock,

    // Expressions
    LiteralExpression,
    NameExpression,
    ApplicationExpression,
    BinaryExpression,
    UnaryExpression,
    IfExpression,
    LetExpression,
    LambdaExpression,
    MatchExpression,
    MatchBranch,
    ListExpression,
    RecordExpression,
    RecordField,
    FieldAccessExpression,
    ParenthesizedExpression,
    DoExpression,
    DoBindStatement,
    DoExprStatement,
    InterpolatedStringExpression,

    // Patterns
    VariablePattern,
    LiteralPattern,
    ConstructorPattern,
    WildcardPattern,

    // Types
    NamedType,
    FunctionType,
    EffectfulType,
    ApplicationType,
    ParenthesizedType,
    LinearType,
    DependentType,
    IntegerLiteralType,
    BinaryType,
    ProofConstraintType,
    RecordType,
    RecordTypeField,
    VariantType,
    VariantConstructor,

    // Type signature
    TypeAnnotation,

    // Claims and Proofs
    ClaimDefinition,
    ProofDefinition,
    ProofRefl,
    ProofAssume,
    ProofSym,
    ProofTrans,
    ProofCong,
    ProofInduction,
    ProofCase,
    ProofName,
    ProofApply,

    // Parameters
    Parameter,

    // Citations and Exports
    Cites,

    // Error recovery
    ErrorNode,
}

public abstract record SyntaxNode(SyntaxKind Kind, SourceSpan Span)
{
    public abstract IEnumerable<SyntaxNode> Children { get; }
}

public sealed record TokenNode(Token Token)
    : SyntaxNode(SyntaxKind.LiteralExpression, Token.Span)
{
    public override IEnumerable<SyntaxNode> Children => [];
}

public sealed record DocumentNode(
    IReadOnlyList<DefinitionNode> Definitions,
    IReadOnlyList<TypeDefinitionNode> TypeDefinitions,
    IReadOnlyList<ClaimNode> Claims,
    IReadOnlyList<ProofNode> Proofs,
    IReadOnlyList<ChapterNode> Chapters,
    SourceSpan Span)
    : SyntaxNode(SyntaxKind.Document, Span)
{
    public IReadOnlyList<CitesNode> Citations { get; init; } = [];
    public IReadOnlyList<EffectDefinitionNode> EffectDefinitions { get; init; } = [];
    public PageMarker? Page { get; init; }

    public DocumentNode(IReadOnlyList<DefinitionNode> Definitions, SourceSpan Span)
        : this(Definitions, Array.Empty<TypeDefinitionNode>(), Array.Empty<ClaimNode>(),
               Array.Empty<ProofNode>(), Array.Empty<ChapterNode>(), Span) { }

    public DocumentNode(IReadOnlyList<DefinitionNode> Definitions, IReadOnlyList<ChapterNode> Chapters, SourceSpan Span)
        : this(Definitions, Array.Empty<TypeDefinitionNode>(), Array.Empty<ClaimNode>(),
               Array.Empty<ProofNode>(), Chapters, Span) { }

    public override IEnumerable<SyntaxNode> Children
    {
        get
        {
            foreach (CitesNode cite in Citations) yield return cite;
            foreach (EffectDefinitionNode eff in EffectDefinitions) yield return eff;
            foreach (ChapterNode ch in Chapters) yield return ch;
            foreach (TypeDefinitionNode td in TypeDefinitions) yield return td;
            foreach (ClaimNode cl in Claims) yield return cl;
            foreach (ProofNode pr in Proofs) yield return pr;
            foreach (DefinitionNode def in Definitions) yield return def;
        }
    }
}

public sealed record ChapterNode(
    string Title,
    IReadOnlyList<DocumentMember> Members,
    SourceSpan Span)
    : SyntaxNode(SyntaxKind.Chapter, Span)
{
    public override IEnumerable<SyntaxNode> Children => Members;
}

public abstract record DocumentMember(SyntaxKind Kind, SourceSpan Span) : SyntaxNode(Kind, Span);

public enum ProseTransitionKind { None, WeSay, ThisIsWritten, ToDefine }

public readonly record struct FailClause(string? Reason, string Condition);
public readonly record struct ProseConstraint(string Keyword, string Text);

public sealed record FunctionTemplateInfo(
    string FunctionName,
    IReadOnlyList<(string Name, string Type)> Parameters,
    string? ReturnType,
    SourceSpan Span)
{
    public IReadOnlyList<FailClause> FailClauses { get; init; } = [];
}

public readonly record struct InlineCodeRef(string Code, int StartOffset, int EndOffset);
public readonly record struct InlineTypeRef(string TypeName, int StartOffset, int EndOffset);

public readonly record struct ProseClaimInfo(string Description);
public readonly record struct ProseProofInfo(string Strategy);

public enum ProcedureStepKind { Let, Set, Return, FailWith, If }

public sealed record ProcedureStep(
    ProcedureStepKind Kind,
    string Marker,
    string Text)
{
    public string? Binding { get; init; }
    public string? Value { get; init; }
    public string? Condition { get; init; }
    public string? Otherwise { get; init; }
}

public sealed record ProseProcedure(IReadOnlyList<ProcedureStep> Steps);

public enum QuantifierKind { ForEvery, ThereExists, No }

public readonly record struct ProseQuantifiedStatement(
    QuantifierKind Quantifier,
    string BoundVariable,
    string Collection,
    string? Qualifier,
    string Claim);

public sealed record ProseBlockNode(string Text, SourceSpan Span)
    : DocumentMember(SyntaxKind.ProseBlock, Span)
{
    public FunctionTemplateInfo? FunctionTemplate { get; init; }
    public ProseClaimInfo? ClaimTemplate { get; init; }
    public ProseProofInfo? ProofTemplate { get; init; }
    public ProseProcedure? Procedure { get; init; }
    public IReadOnlyList<ProseQuantifiedStatement> QuantifiedStatements { get; init; } = [];
    public ProseTransitionKind Transition { get; init; } = ProseTransitionKind.None;
    public IReadOnlyList<InlineCodeRef> CodeRefs { get; init; } = [];
    public IReadOnlyList<InlineTypeRef> TypeRefs { get; init; } = [];
    public override IEnumerable<SyntaxNode> Children => [];
}

public sealed record SectionNode(
    string Title,
    IReadOnlyList<DocumentMember> Members,
    SourceSpan Span)
    : DocumentMember(SyntaxKind.Section, Span)
{
    public override IEnumerable<SyntaxNode> Children => Members;
}

public sealed record NotationBlockNode(
    IReadOnlyList<DefinitionNode> Definitions,
    IReadOnlyList<TypeDefinitionNode> TypeDefinitions,
    SourceSpan Span)
    : DocumentMember(SyntaxKind.Definition, Span)
{
    public IReadOnlyList<ClaimNode> Claims { get; init; } = [];
    public IReadOnlyList<ProofNode> Proofs { get; init; } = [];
    public IReadOnlyList<CitesNode> Citations { get; init; } = [];
    public IReadOnlyList<EffectDefinitionNode> EffectDefinitions { get; init; } = [];

    public NotationBlockNode(IReadOnlyList<DefinitionNode> Definitions, SourceSpan Span)
        : this(Definitions, Array.Empty<TypeDefinitionNode>(), Span) { }

    public override IEnumerable<SyntaxNode> Children => Definitions;
}

public sealed record CitesNode(Token Name, SourceSpan Span)
    : SyntaxNode(SyntaxKind.Cites, Span)
{
    public IReadOnlyList<Token> SelectedNames { get; init; } = [];
    public override IEnumerable<SyntaxNode> Children => [];
}

public sealed record PageMarker(int PageNumber, int? TotalPages, SourceSpan Span);

public sealed record DefinitionNode(
    Token Name,
    IReadOnlyList<Token> Parameters,
    TypeAnnotationNode? TypeAnnotation,
    ExpressionNode Body,
    SourceSpan Span)
    : SyntaxNode(SyntaxKind.Definition, Span)
{
    public override IEnumerable<SyntaxNode> Children
    {
        get
        {
            if (TypeAnnotation is not null) yield return TypeAnnotation;
            yield return Body;
        }
    }
}

public abstract record ExpressionNode(SyntaxKind Kind, SourceSpan Span) : SyntaxNode(Kind, Span);

public sealed record LiteralExpressionNode(Token Literal)
    : ExpressionNode(SyntaxKind.LiteralExpression, Literal.Span)
{
    public override IEnumerable<SyntaxNode> Children => [];
}

public sealed record NameExpressionNode(Token Name)
    : ExpressionNode(SyntaxKind.NameExpression, Name.Span)
{
    public override IEnumerable<SyntaxNode> Children => [];
}

public sealed record ApplicationExpressionNode(ExpressionNode Function, ExpressionNode Argument, SourceSpan Span)
    : ExpressionNode(SyntaxKind.ApplicationExpression, Span)
{
    public override IEnumerable<SyntaxNode> Children => [Function, Argument];
}

public sealed record BinaryExpressionNode(ExpressionNode Left, Token Operator, ExpressionNode Right, SourceSpan Span)
    : ExpressionNode(SyntaxKind.BinaryExpression, Span)
{
    public override IEnumerable<SyntaxNode> Children => [Left, Right];
}

public sealed record UnaryExpressionNode(Token Operator, ExpressionNode Operand, SourceSpan Span)
    : ExpressionNode(SyntaxKind.UnaryExpression, Span)
{
    public override IEnumerable<SyntaxNode> Children => [Operand];
}

public sealed record IfExpressionNode(ExpressionNode Condition, ExpressionNode Then, ExpressionNode Else, SourceSpan Span)
    : ExpressionNode(SyntaxKind.IfExpression, Span)
{
    public override IEnumerable<SyntaxNode> Children => [Condition, Then, Else];
}

public sealed record LetExpressionNode(IReadOnlyList<LetBinding> Bindings, ExpressionNode Body, SourceSpan Span)
    : ExpressionNode(SyntaxKind.LetExpression, Span)
{
    public override IEnumerable<SyntaxNode> Children
    {
        get
        {
            foreach (LetBinding b in Bindings) yield return b.Value;
            yield return Body;
        }
    }
}

public sealed record LetBinding(Token Name, ExpressionNode Value);

public sealed record LambdaExpressionNode(IReadOnlyList<Token> Parameters, ExpressionNode Body, SourceSpan Span)
    : ExpressionNode(SyntaxKind.LambdaExpression, Span)
{
    public override IEnumerable<SyntaxNode> Children => [Body];
}

public sealed record MatchExpressionNode(ExpressionNode Scrutinee, IReadOnlyList<MatchBranchNode> Branches, SourceSpan Span)
    : ExpressionNode(SyntaxKind.MatchExpression, Span)
{
    public override IEnumerable<SyntaxNode> Children
    {
        get
        {
            yield return Scrutinee;
            foreach (MatchBranchNode b in Branches) yield return b;
        }
    }
}

public sealed record MatchBranchNode(PatternNode Pattern, ExpressionNode Body, SourceSpan Span)
    : SyntaxNode(SyntaxKind.MatchBranch, Span)
{
    public override IEnumerable<SyntaxNode> Children => [Pattern, Body];
}

public sealed record ListExpressionNode(IReadOnlyList<ExpressionNode> Elements, SourceSpan Span)
    : ExpressionNode(SyntaxKind.ListExpression, Span)
{
    public override IEnumerable<SyntaxNode> Children => Elements;
}

public sealed record RecordExpressionNode(Token? TypeName, IReadOnlyList<RecordFieldNode> Fields, SourceSpan Span)
    : ExpressionNode(SyntaxKind.RecordExpression, Span)
{
    public override IEnumerable<SyntaxNode> Children => Fields;
}

public sealed record RecordFieldNode(Token Name, ExpressionNode Value, SourceSpan Span)
    : SyntaxNode(SyntaxKind.RecordField, Span)
{
    public override IEnumerable<SyntaxNode> Children => [Value];
}

public sealed record FieldAccessExpressionNode(ExpressionNode Record, Token FieldName, SourceSpan Span)
    : ExpressionNode(SyntaxKind.FieldAccessExpression, Span)
{
    public override IEnumerable<SyntaxNode> Children => [Record];
}

public sealed record ParenthesizedExpressionNode(ExpressionNode Inner, SourceSpan Span)
    : ExpressionNode(SyntaxKind.ParenthesizedExpression, Span)
{
    public override IEnumerable<SyntaxNode> Children => [Inner];
}

public sealed record ErrorExpressionNode(Token ErrorToken)
    : ExpressionNode(SyntaxKind.ErrorNode, ErrorToken.Span)
{
    public override IEnumerable<SyntaxNode> Children => [];
}

public sealed record DoExpressionNode(IReadOnlyList<DoStatementNode> Statements, SourceSpan Span)
    : ExpressionNode(SyntaxKind.DoExpression, Span)
{
    public override IEnumerable<SyntaxNode> Children => Statements;
}

public abstract record DoStatementNode(SyntaxKind Kind, SourceSpan Span) : SyntaxNode(Kind, Span);

public sealed record DoBindStatementNode(Token Name, ExpressionNode Value, SourceSpan Span)
    : DoStatementNode(SyntaxKind.DoBindStatement, Span)
{
    public override IEnumerable<SyntaxNode> Children => [Value];
}

public sealed record DoExprStatementNode(ExpressionNode Expression, SourceSpan Span)
    : DoStatementNode(SyntaxKind.DoExprStatement, Span)
{
    public override IEnumerable<SyntaxNode> Children => [Expression];
}

public sealed record InterpolatedStringNode(IReadOnlyList<ExpressionNode> Parts, SourceSpan Span)
    : ExpressionNode(SyntaxKind.InterpolatedStringExpression, Span)
{
    public override IEnumerable<SyntaxNode> Children => Parts;
}

public abstract record PatternNode(SyntaxKind Kind, SourceSpan Span) : SyntaxNode(Kind, Span);

public sealed record VariablePatternNode(Token Name)
    : PatternNode(SyntaxKind.VariablePattern, Name.Span)
{
    public override IEnumerable<SyntaxNode> Children => [];
}

public sealed record LiteralPatternNode(Token Literal)
    : PatternNode(SyntaxKind.LiteralPattern, Literal.Span)
{
    public override IEnumerable<SyntaxNode> Children => [];
}

public sealed record ConstructorPatternNode(Token Constructor, IReadOnlyList<PatternNode> SubPatterns, SourceSpan Span)
    : PatternNode(SyntaxKind.ConstructorPattern, Span)
{
    public override IEnumerable<SyntaxNode> Children => SubPatterns;
}

public sealed record WildcardPatternNode(Token Underscore)
    : PatternNode(SyntaxKind.WildcardPattern, Underscore.Span)
{
    public override IEnumerable<SyntaxNode> Children => [];
}

public abstract record TypeNode(SyntaxKind Kind, SourceSpan Span) : SyntaxNode(Kind, Span);

public sealed record NamedTypeNode(Token Name)
    : TypeNode(SyntaxKind.NamedType, Name.Span)
{
    public override IEnumerable<SyntaxNode> Children => [];
}

public sealed record FunctionTypeNode(TypeNode Parameter, TypeNode Return, SourceSpan Span)
    : TypeNode(SyntaxKind.FunctionType, Span)
{
    public override IEnumerable<SyntaxNode> Children => [Parameter, Return];
}

public sealed record ApplicationTypeNode(TypeNode Constructor, IReadOnlyList<TypeNode> Arguments, SourceSpan Span)
    : TypeNode(SyntaxKind.ApplicationType, Span)
{
    public override IEnumerable<SyntaxNode> Children
    {
        get
        {
            yield return Constructor;
            foreach (TypeNode a in Arguments) yield return a;
        }
    }
}

public sealed record ParenthesizedTypeNode(TypeNode Inner, SourceSpan Span)
    : TypeNode(SyntaxKind.ParenthesizedType, Span)
{
    public override IEnumerable<SyntaxNode> Children => [Inner];
}

public sealed record EffectfulTypeNode(IReadOnlyList<TypeNode> Effects, TypeNode Return, SourceSpan Span)
    : TypeNode(SyntaxKind.EffectfulType, Span)
{
    public override IEnumerable<SyntaxNode> Children
    {
        get
        {
            foreach (TypeNode e in Effects) yield return e;
            yield return Return;
        }
    }
}

public sealed record LinearTypeNode(TypeNode Inner, SourceSpan Span)
    : TypeNode(SyntaxKind.LinearType, Span)
{
    public override IEnumerable<SyntaxNode> Children => [Inner];
}

public sealed record DependentTypeNode(Token ParamName, TypeNode ParamType, TypeNode Body, SourceSpan Span)
    : TypeNode(SyntaxKind.DependentType, Span)
{
    public override IEnumerable<SyntaxNode> Children => [ParamType, Body];
}

public sealed record IntegerTypeNode(Token Literal, SourceSpan Span)
    : TypeNode(SyntaxKind.IntegerLiteralType, Span)
{
    public override IEnumerable<SyntaxNode> Children => [];
}

public sealed record BinaryTypeNode(TypeNode Left, Token Operator, TypeNode Right, SourceSpan Span)
    : TypeNode(SyntaxKind.BinaryType, Span)
{
    public override IEnumerable<SyntaxNode> Children => [Left, Right];
}

public sealed record ProofConstraintNode(TypeNode Left, Token Operator, TypeNode Right, SourceSpan Span)
    : TypeNode(SyntaxKind.ProofConstraintType, Span)
{
    public override IEnumerable<SyntaxNode> Children => [Left, Right];
}

public sealed record TypeAnnotationNode(Token Name, TypeNode Type, SourceSpan Span)
    : SyntaxNode(SyntaxKind.TypeAnnotation, Span)
{
    public override IEnumerable<SyntaxNode> Children => [Type];
}

public sealed record TypeDefinitionNode(
    Token Name,
    IReadOnlyList<Token> TypeParameters,
    TypeDefinitionBody Body,
    SourceSpan Span)
    : SyntaxNode(SyntaxKind.TypeDefinition, Span)
{
    public override IEnumerable<SyntaxNode> Children => [Body];
}

public abstract record TypeDefinitionBody(SyntaxKind Kind, SourceSpan Span) : SyntaxNode(Kind, Span);

public sealed record RecordTypeBody(IReadOnlyList<RecordTypeFieldNode> Fields, SourceSpan Span)
    : TypeDefinitionBody(SyntaxKind.RecordType, Span)
{
    public IReadOnlyList<ProseConstraint> Constraints { get; init; } = [];
    public override IEnumerable<SyntaxNode> Children => Fields;
}

public sealed record RecordTypeFieldNode(Token Name, TypeNode Type, SourceSpan Span)
    : SyntaxNode(SyntaxKind.RecordTypeField, Span)
{
    public override IEnumerable<SyntaxNode> Children => [Type];
}

public sealed record VariantTypeBody(IReadOnlyList<VariantConstructorNode> Constructors, SourceSpan Span)
    : TypeDefinitionBody(SyntaxKind.VariantType, Span)
{
    public IReadOnlyList<ProseConstraint> Constraints { get; init; } = [];
    public override IEnumerable<SyntaxNode> Children => Constructors;
}

public sealed record VariantConstructorNode(
    Token Name,
    IReadOnlyList<VariantFieldNode> Fields,
    SourceSpan Span)
    : SyntaxNode(SyntaxKind.VariantConstructor, Span)
{
    public override IEnumerable<SyntaxNode> Children => Fields;
}

public sealed record VariantFieldNode(Token? FieldName, TypeNode Type, SourceSpan Span)
    : SyntaxNode(SyntaxKind.RecordTypeField, Span)
{
    public override IEnumerable<SyntaxNode> Children => [Type];
}

public sealed record ErrorTypeBody(SourceSpan Span)
    : TypeDefinitionBody(SyntaxKind.ErrorNode, Span)
{
    public override IEnumerable<SyntaxNode> Children => [];
}

public sealed record EffectOperationNode(Token Name, TypeNode Type, SourceSpan Span)
    : SyntaxNode(SyntaxKind.EffectOperation, Span)
{
    public override IEnumerable<SyntaxNode> Children => [Type];
}

public sealed record EffectDefinitionNode(
    Token Name,
    IReadOnlyList<EffectOperationNode> Operations,
    SourceSpan Span)
    : SyntaxNode(SyntaxKind.EffectDefinition, Span)
{
    public override IEnumerable<SyntaxNode> Children => Operations;
}

public sealed record HandleClauseNode(
    Token OperationName,
    IReadOnlyList<Token> Parameters,
    Token ResumeName,
    ExpressionNode Body,
    SourceSpan Span)
    : SyntaxNode(SyntaxKind.HandleClause, Span)
{
    public override IEnumerable<SyntaxNode> Children => [Body];
}

public sealed record HandleExpressionNode(
    ExpressionNode Computation,
    Token EffectName,
    IReadOnlyList<HandleClauseNode> Clauses,
    SourceSpan Span)
    : ExpressionNode(SyntaxKind.HandleExpression, Span)
{
    public override IEnumerable<SyntaxNode> Children
    {
        get
        {
            yield return Computation;
            foreach (HandleClauseNode c in Clauses) yield return c;
        }
    }
}

public sealed record ClaimNode(
    Token Name,
    IReadOnlyList<Token> Parameters,
    TypeNode Left,
    TypeNode Right,
    SourceSpan Span)
    : SyntaxNode(SyntaxKind.ClaimDefinition, Span)
{
    public override IEnumerable<SyntaxNode> Children => [Left, Right];
}

public sealed record ProofNode(
    Token Name,
    IReadOnlyList<Token> Parameters,
    ProofExprNode Body,
    SourceSpan Span)
    : SyntaxNode(SyntaxKind.ProofDefinition, Span)
{
    public override IEnumerable<SyntaxNode> Children => [Body];
}

public abstract record ProofExprNode(SyntaxKind Kind, SourceSpan Span)
    : SyntaxNode(Kind, Span);

public sealed record ReflNode(SourceSpan Span)
    : ProofExprNode(SyntaxKind.ProofRefl, Span)
{
    public override IEnumerable<SyntaxNode> Children => [];
}

public sealed record AssumeNode(SourceSpan Span)
    : ProofExprNode(SyntaxKind.ProofAssume, Span)
{
    public override IEnumerable<SyntaxNode> Children => [];
}

public sealed record SymNode(ProofExprNode Inner, SourceSpan Span)
    : ProofExprNode(SyntaxKind.ProofSym, Span)
{
    public override IEnumerable<SyntaxNode> Children => [Inner];
}

public sealed record TransNode(ProofExprNode Left, ProofExprNode Right, SourceSpan Span)
    : ProofExprNode(SyntaxKind.ProofTrans, Span)
{
    public override IEnumerable<SyntaxNode> Children => [Left, Right];
}

public sealed record CongNode(Token FunctionName, ProofExprNode Inner, SourceSpan Span)
    : ProofExprNode(SyntaxKind.ProofCong, Span)
{
    public override IEnumerable<SyntaxNode> Children => [Inner];
}

public sealed record InductionNode(
    Token Variable,
    IReadOnlyList<ProofCaseNode> Cases,
    SourceSpan Span)
    : ProofExprNode(SyntaxKind.ProofInduction, Span)
{
    public override IEnumerable<SyntaxNode> Children
    {
        get
        {
            foreach (ProofCaseNode c in Cases) yield return c;
        }
    }
}

public sealed record ProofCaseNode(
    PatternNode Pattern,
    ProofExprNode Body,
    SourceSpan Span)
    : SyntaxNode(SyntaxKind.ProofCase, Span)
{
    public override IEnumerable<SyntaxNode> Children => [Pattern, Body];
}

public sealed record ProofNameNode(Token Name, SourceSpan Span)
    : ProofExprNode(SyntaxKind.ProofName, Span)
{
    public override IEnumerable<SyntaxNode> Children => [];
}

public sealed record ProofApplyNode(
    Token LemmaName,
    IReadOnlyList<ExpressionNode> Arguments,
    SourceSpan Span)
    : ProofExprNode(SyntaxKind.ProofApply, Span)
{
    public override IEnumerable<SyntaxNode> Children
    {
        get
        {
            foreach (ExpressionNode a in Arguments) yield return a;
        }
    }
}
