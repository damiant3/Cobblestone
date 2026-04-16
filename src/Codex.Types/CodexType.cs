using System.Collections.Immutable;
using Codex.Core;

namespace Codex.Types;

public abstract record CodexType;

public sealed record IntegerType : CodexType
{
    public static readonly IntegerType s_instance = new();
    public override string ToString() => "Integer";
}

public sealed record NumberType : CodexType
{
    public static readonly NumberType s_instance = new();
    public override string ToString() => "Number";
}

public sealed record TextType : CodexType
{
    public static readonly TextType s_instance = new();
    public override string ToString() => "Text";
}

public sealed record BooleanType : CodexType
{
    public static readonly BooleanType s_instance = new();
    public override string ToString() => "Boolean";
}

public sealed record CharType : CodexType
{
    public static readonly CharType s_instance = new();
    public override string ToString() => "Char";
}

public sealed record NothingType : CodexType
{
    public static readonly NothingType s_instance = new();
    public override string ToString() => "Nothing";
}

public sealed record VoidType : CodexType
{
    public static readonly VoidType s_instance = new();
    public override string ToString() => "Void";
}

public sealed record FunctionType(CodexType Parameter, CodexType Return) : CodexType
{
    public override string ToString()
    {
        string paramStr = Parameter is FunctionType ? $"({Parameter})" : Parameter.ToString()!;
        return $"{paramStr} → {Return}";
    }
}

public sealed record ConstructedType(Name Constructor, ImmutableArray<CodexType> Arguments) : CodexType
{
    public override string ToString()
    {
        if (Arguments.IsEmpty)
        {
            return Constructor.Value;
        }

        return $"{Constructor.Value} {string.Join(" ", Arguments)}";
    }
}

public sealed record TypeVariable(int Id) : CodexType
{
    public override string ToString() => $"?t{Id}";
}

public sealed record ForAllType(int VariableId, CodexType Body) : CodexType
{
    public override string ToString() => $"∀t{VariableId}. {Body}";
}

public sealed record ListType(CodexType Element) : CodexType
{
    public override string ToString() => $"List {Element}";
}

public sealed record LinkedListType(CodexType Element) : CodexType
{
    public override string ToString() => $"LinkedList {Element}";
}

public sealed record RecordType(
    Name TypeName,
    ImmutableArray<int> TypeParamIds,
    ImmutableArray<RecordFieldType> Fields) : CodexType
{
    public ImmutableArray<CodexType> TypeArguments { get; init; } = [];

    public override string ToString()
    {
        string fieldsStr = string.Join(", ", Fields.Select(f => $"{f.FieldName.Value} : {f.Type}"));
        return $"{TypeName.Value} {{ {fieldsStr} }}";
    }
}

public sealed record RecordFieldType(Name FieldName, CodexType Type);

public sealed record SumType(
    Name TypeName,
    ImmutableArray<int> TypeParamIds,
    ImmutableArray<SumConstructorType> Constructors) : CodexType
{
    public ImmutableArray<CodexType> TypeArguments { get; init; } = [];

    public override string ToString()
    {
        string ctorsStr = string.Join(" | ", Constructors.Select(c => c.ToString()));
        return $"{TypeName.Value} = {ctorsStr}";
    }
}

public sealed record SumConstructorType(Name Name, ImmutableArray<CodexType> Fields)
{
    public override string ToString()
    {
        if (Fields.IsEmpty)
        {
            return Name.Value;
        }

        return $"{Name.Value} {string.Join(" ", Fields.Select(f => f.ToString()))}";
    }
}

public sealed record ErrorType : CodexType
{
    public static readonly ErrorType s_instance = new();
    public override string ToString() => "<error>";
}

public sealed record EffectType(Name EffectName) : CodexType
{
    public override string ToString() => EffectName.Value;
}

public sealed record EffectRowVariable(int Id) : CodexType
{
    public override string ToString() => $"?e{Id}";
}

public sealed record EffectfulType(
    ImmutableArray<EffectType> Effects,
    CodexType Return,
    EffectRowVariable? RowVariable = null) : CodexType
{
    public override string ToString()
    {
        string effectStr = string.Join(", ", Effects.Select(e => e.EffectName.Value));
        if (RowVariable is not null)
        {
            string rowStr = RowVariable.ToString();
            effectStr = effectStr.Length > 0 ? $"{effectStr}, {rowStr}" : rowStr;
        }
        return $"[{effectStr}] {Return}";
    }
}

public enum Usage
{
    Unrestricted,
    Linear,
    Erased
}

public sealed record LinearType(CodexType Inner) : CodexType
{
    public override string ToString() => $"linear {Inner}";
}

public sealed record DependentFunctionType(string ParamName, CodexType ParamType, CodexType Body) : CodexType
{
    public override string ToString()
    {
        string paramStr = $"({ParamName} : {ParamType})";
        return $"{paramStr} → {Body}";
    }
}

public sealed record TypeLevelValue(long Value) : CodexType
{
    public override string ToString() => Value.ToString();
}

public enum TypeLevelOp { Add, Sub, Mul }

public sealed record TypeLevelBinary(TypeLevelOp Op, CodexType Left, CodexType Right) : CodexType
{
    public override string ToString()
    {
        string opStr = Op switch
        {
            TypeLevelOp.Add => "+",
            TypeLevelOp.Sub => "-",
            TypeLevelOp.Mul => "*",
            _ => "?"
        };
        return $"({Left} {opStr} {Right})";
    }
}

public sealed record TypeLevelVar(string Name) : CodexType
{
    public override string ToString() => Name;
}

public sealed record ProofType(CodexType Claim) : CodexType
{
    public override string ToString() => $"{{proof : {Claim}}}";
}

public sealed record LessThanClaim(CodexType Left, CodexType Right) : CodexType
{
    public override string ToString() => $"{Left} < {Right}";
}

public sealed record EqualityType(CodexType Left, CodexType Right) : CodexType
{
    public override string ToString() => $"{Left} ≡ {Right}";
}

public sealed record ReflProof : CodexType
{
    public static readonly ReflProof s_instance = new();
    public override string ToString() => "Refl";
}

public sealed record CongProof(string FunctionName, CodexType InnerProof) : CodexType
{
    public override string ToString() => $"Cong {FunctionName} {InnerProof}";
}

public sealed record SymProof(CodexType InnerProof) : CodexType
{
    public override string ToString() => $"Sym {InnerProof}";
}

public sealed record TransProof(CodexType Left, CodexType Right) : CodexType
{
    public override string ToString() => $"Trans {Left} {Right}";
}

public sealed record InductionProof(string Variable, CodexType BaseCase, CodexType InductiveStep) : CodexType
{
    public override string ToString() => $"Induction {Variable} (base: {BaseCase}) (step: {InductiveStep})";
}
