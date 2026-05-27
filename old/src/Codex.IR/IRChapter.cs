using System.Collections.Immutable;
using Codex.Core;
using Codex.Types;

namespace Codex.IR;

public sealed record IRChapter(
    QualifiedName Name,
    ImmutableArray<IRDefinition> Definitions,
    Map<string, CodexType> TypeDefinitions)
{
    public ImmutableArray<IRChapterSection> Sections { get; init; }

    // Scope sets carried forward from NameResolver. Lowering populates these
    // from ResolvedChapter so downstream passes (LoweringInvariants, emit
    // backends asking "is this name a ctor?") don't re-walk Definitions and
    // TypeDefinitions on every call. Empty means "not populated" — callers
    // that need the data fall back to deriving from Definitions/TypeDefinitions
    // (see IRChapterExtensions.CollectConstructorNames and LoweringInvariants).
    public Set<string> TopLevelNames { get; init; } = Set<string>.s_empty;
    public Set<string> ConstructorNames { get; init; } = Set<string>.s_empty;
}

public sealed record IRChapterSection(
    string Name,
    ImmutableArray<(string TypeName, CodexType Type)> TypeDefinitions,
    ImmutableArray<IRDefinition> Definitions)
{
    public string? ChapterTitle { get; init; }
    public string? Prose { get; init; }
    public ImmutableArray<string> SectionTitles { get; init; }
}

public sealed record IRDefinition(
    string Name,
    ImmutableArray<IRParameter> Parameters,
    CodexType Type,
    IRExpr Body)
{
    public string? Section { get; init; }

    // Source location of the top-level Definition that lowered to this IR.
    // Required for bare-metal DWARF function DIEs (low_pc/high_pc/decl_file/
    // decl_line). Hand-built IR (tests, tooling) may leave this as the
    // synthetic default. Lowering.cs wires the real span from Definition.Span.
    public SourceSpan Span { get; init; } = SourceSpan.s_synthetic;
}

public sealed record IRParameter(string Name, CodexType Type);

public abstract record IRExpr(CodexType Type)
{
    // Source span of the AST Expr that lowered to this IR node. Lowering.cs
    // stamps this on every node via `with { Span = expr.Span }` in LowerExpr's
    // outer wrapper. Hand-built IR (tests, codegen tooling) leaves the
    // synthetic default. Used today by DWARF line info (Phase 1, pending) and
    // better-located diagnostics; section F (provenance) will layer a
    // transformation tag on top.
    public SourceSpan Span { get; init; } = SourceSpan.s_synthetic;
}

public sealed record IRIntegerLit(long Value) : IRExpr(IntegerType.s_instance);

public sealed record IRNumberLit(double Value) : IRExpr(NumberType.s_instance);

public sealed record IRTextLit(string Value) : IRExpr(TextType.s_instance);

public sealed record IRBoolLit(bool Value) : IRExpr(BooleanType.s_instance);

public sealed record IRCharLit(long Value) : IRExpr(CharType.s_instance);

public sealed record IRName(string Name, CodexType Type) : IRExpr(Type);

public sealed record IRBinary(IRBinaryOp Op, IRExpr Left, IRExpr Right, CodexType Type) : IRExpr(Type);

public enum IRBinaryOp
{
    AddInt, SubInt, MulInt, DivInt, PowInt,
    AddNum, SubNum, MulNum, DivNum,
    Eq, NotEq, Lt, Gt, LtEq, GtEq,
    And, Or,
    AppendText, AppendList,
    ConsList
}

public sealed record IRNegate(IRExpr Operand) : IRExpr(Operand.Type);

public sealed record IRIf(IRExpr Condition, IRExpr Then, IRExpr Else, CodexType Type) : IRExpr(Type);

public sealed record IRLet(string Name, CodexType NameType, IRExpr Value, IRExpr Body) : IRExpr(Body.Type);

public sealed record IRApply(IRExpr Function, IRExpr Argument, CodexType Type) : IRExpr(Type);

public sealed record IRLambda(ImmutableArray<IRParameter> Parameters, IRExpr Body, CodexType Type) : IRExpr(Type);

public sealed record IRList(ImmutableArray<IRExpr> Elements, CodexType ElementType)
    : IRExpr(new ListType(ElementType));

public sealed record IRMatch(IRExpr Scrutinee, ImmutableArray<IRMatchBranch> Branches, CodexType Type)
    : IRExpr(Type);

public sealed record IRMatchBranch(IRPattern Pattern, IRExpr Body);

public abstract record IRPattern;

public sealed record IRVarPattern(string Name, CodexType Type) : IRPattern;

public sealed record IRLiteralPattern(object Value, CodexType Type) : IRPattern;

public sealed record IRCtorPattern(string Name, ImmutableArray<IRPattern> SubPatterns, CodexType Type) : IRPattern;

public sealed record IRWildcardPattern : IRPattern;

public sealed record IRError(string Message, CodexType Type) : IRExpr(Type);

public sealed record IRAct(ImmutableArray<IRActStatement> Statements, CodexType Type) : IRExpr(Type);

public abstract record IRActStatement;

public sealed record IRActBind(string Name, CodexType NameType, IRExpr Value) : IRActStatement;

public sealed record IRActExec(IRExpr Expression) : IRActStatement;

public sealed record IRRecord(string TypeName, ImmutableArray<(string FieldName, IRExpr Value)> Fields, CodexType Type)
    : IRExpr(Type);

public sealed record IRFieldAccess(IRExpr Record, string FieldName, CodexType Type) : IRExpr(Type);

public sealed record IRRunState(
    IRExpr InitialState,
    IRExpr Computation,
    CodexType StateType,
    CodexType ResultType)
    : IRExpr(ResultType);

public sealed record IRGetState(CodexType StateType) : IRExpr(StateType);

public sealed record IRSetState(IRExpr NewValue, CodexType StateType) : IRExpr(NothingType.s_instance);

public sealed record IRHandle(
    IRExpr Computation,
    string EffectName,
    ImmutableArray<IRHandleClause> Clauses,
    CodexType Type) : IRExpr(Type);

public sealed record IRHandleClause(
    string OperationName,
    ImmutableArray<string> Parameters,
    ImmutableArray<CodexType> ParameterTypes,
    string ResumeName,
    CodexType ResumeParamType,
    IRExpr Body);
