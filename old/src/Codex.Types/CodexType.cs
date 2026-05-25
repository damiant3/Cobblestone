using System.Collections.Immutable;
using Codex.Core;

namespace Codex.Types;

public abstract record CodexType
{
    public sealed override string ToString() => TypeFormatter.Format(this);
}

public sealed record IntegerType : CodexType
{
    public static readonly IntegerType s_instance = new();
}

public sealed record NumberType : CodexType
{
    public static readonly NumberType s_instance = new();
}

public sealed record TextType : CodexType
{
    public static readonly TextType s_instance = new();
}

public sealed record BooleanType : CodexType
{
    public static readonly BooleanType s_instance = new();
}

public sealed record CharType : CodexType
{
    public static readonly CharType s_instance = new();
}

public sealed record NothingType : CodexType
{
    public static readonly NothingType s_instance = new();
}

public sealed record VoidType : CodexType
{
    public static readonly VoidType s_instance = new();
}

public sealed record FunctionType(CodexType Parameter, CodexType Return) : CodexType;

public sealed record ConstructedType(Name Constructor, ImmutableArray<CodexType> Arguments) : CodexType;

public sealed record TypeVariable(int Id) : CodexType;

public sealed record ForAllType(int VariableId, CodexType Body) : CodexType;

public sealed record ListType(CodexType Element) : CodexType;

public sealed record LinkedListType(CodexType Element) : CodexType;

public sealed record RecordType(
    Name TypeName,
    ImmutableArray<int> TypeParamIds,
    ImmutableArray<RecordFieldType> Fields) : CodexType
{
    public ImmutableArray<CodexType> TypeArguments { get; init; } = [];
}

public sealed record RecordFieldType(Name FieldName, CodexType Type);

public sealed record SumType(
    Name TypeName,
    ImmutableArray<int> TypeParamIds,
    ImmutableArray<SumConstructorType> Constructors) : CodexType
{
    public ImmutableArray<CodexType> TypeArguments { get; init; } = [];
}

public sealed record SumConstructorType(Name Name, ImmutableArray<CodexType> Fields)
{
    public override string ToString()
    {
        if (Fields.IsEmpty) return Name.Value;
        return $"{Name.Value} {string.Join(" ", Fields.Select(f => f.ToString()))}";
    }
}

public sealed record ErrorType : CodexType
{
    public static readonly ErrorType s_instance = new();
}

public sealed record EffectType(Name EffectName) : CodexType;

public sealed record EffectRowVariable(int Id) : CodexType;

public sealed record EffectfulType(
    ImmutableArray<EffectType> Effects,
    CodexType Return,
    EffectRowVariable? RowVariable = null) : CodexType;

public enum Usage
{
    Unrestricted,
    Linear,
    Erased
}

public sealed record LinearType(CodexType Inner) : CodexType;

public sealed record DependentFunctionType(string ParamName, CodexType ParamType, CodexType Body) : CodexType;

public sealed record TypeLevelValue(long Value) : CodexType;

public enum TypeLevelOp { Add, Sub, Mul }

public sealed record TypeLevelBinary(TypeLevelOp Op, CodexType Left, CodexType Right) : CodexType;

public sealed record TypeLevelVar(string Name) : CodexType;

public sealed record ProofType(CodexType Claim) : CodexType;

public sealed record LessThanClaim(CodexType Left, CodexType Right) : CodexType;

public sealed record EqualityType(CodexType Left, CodexType Right) : CodexType;

public sealed record ReflProof : CodexType
{
    public static readonly ReflProof s_instance = new();
}

public sealed record CongProof(string FunctionName, CodexType InnerProof) : CodexType;

public sealed record SymProof(CodexType InnerProof) : CodexType;

public sealed record TransProof(CodexType Left, CodexType Right) : CodexType;

public sealed record InductionProof(string Variable, CodexType BaseCase, CodexType InductiveStep) : CodexType;
