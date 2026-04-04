using System.Collections.Immutable;
using Codex.Core;
using Codex.Types;

namespace Codex.IR;

public sealed record IRModule(
    QualifiedName Name,
    ImmutableArray<IRDefinition> Definitions,
    Map<string, CodexType> TypeDefinitions)
{
    public ImmutableArray<IRModuleSection> Sections { get; init; }
}

public sealed record IRModuleSection(
    string Name,
    ImmutableArray<(string TypeName, CodexType Type)> TypeDefinitions,
    ImmutableArray<IRDefinition> Definitions);

public sealed record IRDefinition(
    string Name,
    ImmutableArray<IRParameter> Parameters,
    CodexType Type,
    IRExpr Body);

public sealed record IRParameter(string Name, CodexType Type);

public abstract record IRExpr(CodexType Type);

public sealed record IRIntegerLit(long Value) : IRExpr(IntegerType.s_instance);

public sealed record IRNumberLit(decimal Value) : IRExpr(NumberType.s_instance);

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

public sealed record IRDo(ImmutableArray<IRDoStatement> Statements, CodexType Type) : IRExpr(Type);

public abstract record IRDoStatement;

public sealed record IRDoBind(string Name, CodexType NameType, IRExpr Value) : IRDoStatement;

public sealed record IRDoExec(IRExpr Expression) : IRDoStatement;

public sealed record IRRecord(string TypeName, ImmutableArray<(string FieldName, IRExpr Value)> Fields, CodexType Type)
    : IRExpr(Type);

public sealed record IRFieldAccess(IRExpr Record, string FieldName, CodexType Type) : IRExpr(Type);

public sealed record IRRegion(IRExpr Body, CodexType Type, bool NeedsEscapeCopy) : IRExpr(Type)
{
    public static bool TypeNeedsHeapEscape(CodexType type) => type is
        TextType or RecordType or SumType or ListType or ConstructedType
        or FunctionType { Return: not null };
}

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
