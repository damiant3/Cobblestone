using System.Collections.Immutable;
using System.Text;
using Codex.Core;
using Codex.IR;
using Codex.Types;

namespace Codex.Emit.CSharp;

public sealed partial class CSharpEmitter
{
    static string EmitType(CodexType type)
    {
        return type switch
        {
            IntegerType => "long",
            NumberType => "double",
            TextType => "string",
            BooleanType => "bool",
            CharType => "long",
            NothingType => "object",
            VoidType => "void",
            EffectfulType eft => EmitType(eft.Return),
            LinearType lin => EmitType(lin.Inner),
            ListType lt => $"List<{EmitType(lt.Element)}>",
            LinkedListType llt => $"List<{EmitType(llt.Element)}>",
            SumType st => st.TypeArguments.IsEmpty
                ? SanitizeIdentifier(st.TypeName.Value)
                : $"{SanitizeIdentifier(st.TypeName.Value)}<{string.Join(", ", st.TypeArguments.Select(EmitType))}>",
            RecordType rt => rt.TypeArguments.IsEmpty
                ? SanitizeIdentifier(rt.TypeName.Value)
                : $"{SanitizeIdentifier(rt.TypeName.Value)}<{string.Join(", ", rt.TypeArguments.Select(EmitType))}>",
            ConstructedType ct => ct.Arguments.IsEmpty
                ? SanitizeIdentifier(ct.Constructor.Value)
                : $"{SanitizeIdentifier(ct.Constructor.Value)}<{string.Join(", ", ct.Arguments.Select(EmitType))}>",
            FunctionType ft => $"Func<{EmitType(ft.Parameter)}, {EmitType(ft.Return)}>",
            DependentFunctionType dep => $"Func<{EmitType(dep.ParamType)}, {EmitType(dep.Body)}>",
            TypeLevelValue => "long",
            TypeLevelVar => "long",
            TypeLevelBinary => "long",
            ProofType => "object",
            TypeVariable tv => $"T{tv.Id}",
            ErrorType => "object",
            _ => "object"
        };
    }

    // Extract <T1, T2> suffix for emitting `new Just<Foo>(x)` at constructor
    // call sites. The expression's type carries the instantiated arguments
    // either directly (ConstructedType) or via TypeArguments on SumType.
    static string CtorTypeArgs(CodexType type)
    {
        ImmutableArray<CodexType> args = type switch
        {
            SumType st => st.TypeArguments,
            RecordType rt => rt.TypeArguments,
            ConstructedType ct => ct.Arguments,
            EffectfulType eft => CtorTypeArgsFrom(eft.Return),
            _ => []
        };

        if (args.IsDefaultOrEmpty)
            return "";

        return "<" + string.Join(", ", args.Select(EmitType)) + ">";
    }

    static ImmutableArray<CodexType> CtorTypeArgsFrom(CodexType type) => type switch
    {
        SumType st => st.TypeArguments,
        RecordType rt => rt.TypeArguments,
        ConstructedType ct => ct.Arguments,
        EffectfulType eft => CtorTypeArgsFrom(eft.Return),
        _ => []
    };

    static string EmitSumTypeName(SumType st)
    {
        string baseName = SanitizeIdentifier(st.TypeName.Value);
        HashSet<int> ids = [];
        foreach (SumConstructorType ctor in st.Constructors)
        {
            foreach (CodexType field in ctor.Fields)
                CollectTypeVarIds(field, ids);
        }

        if (ids.Count == 0)
            return baseName;
        return baseName + "<" + string.Join(", ", ids.Order().Select(id => $"T{id}")) + ">";
    }

    static void CollectTypeVarIds(CodexType type, HashSet<int> ids)
    {
        switch (type)
        {
            case TypeVariable tv:
                ids.Add(tv.Id);
                break;
            case FunctionType ft:
                CollectTypeVarIds(ft.Parameter, ids);
                CollectTypeVarIds(ft.Return, ids);
                break;
            case ListType lt:
                CollectTypeVarIds(lt.Element, ids);
                break;
            case ForAllType fa:
                CollectTypeVarIds(fa.Body, ids);
                break;
            case ConstructedType ct:
                foreach (CodexType arg in ct.Arguments)
                    CollectTypeVarIds(arg, ids);
                break;
            case SumType st:
                foreach (CodexType arg in st.TypeArguments)
                    CollectTypeVarIds(arg, ids);

                break;
            case RecordType rt:
                foreach (CodexType arg in rt.TypeArguments)
                    CollectTypeVarIds(arg, ids);

                break;
        }
    }

    static bool IsVoidLikeDefinition(IRDefinition def) =>
        IsVoidLike(def.FinalReturnType());

    // SanitizeIdentifier has a two-stage escape that doesn't fit NameSanitizer's
    // single-affix shape: reserved keywords get "@" prefix (the C# verbatim-
    // identifier mechanism); Object-inherited method names get "_" suffix so
    // user definitions don't silently override Equals/ToString on the emitted
    // records. It also needs an extra "." → "_" character map not shared with
    // other targets.
    static string SanitizeIdentifier(string name)
    {
        string sanitized = name.Replace('-', '_').Replace('.', '_');
        if (s_reservedKeywords.Contains(sanitized))
        {
            return $"@{sanitized}";
        }
        if (s_objectMembers.Contains(sanitized))
        {
            return $"{sanitized}_";
        }
        return sanitized;
    }

    static readonly HashSet<string> s_reservedKeywords =
    [
        "class", "static", "void", "return", "if", "else", "for",
        "while", "do", "switch", "case", "break", "continue",
        "new", "this", "base", "null", "true", "false", "int",
        "long", "string", "bool", "double", "decimal", "object",
        "in", "is", "as", "typeof", "default", "throw", "try",
        "catch", "finally", "using", "namespace", "public", "private",
        "protected", "internal", "abstract", "sealed", "override",
        "virtual", "event", "delegate", "out", "ref", "params",
    ];

    static readonly HashSet<string> s_objectMembers =
    [
        "Equals", "GetHashCode", "ToString", "GetType", "MemberwiseClone",
    ];

    static string EscapeString(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }

    // CCE table — delegates to CceTable (single source of truth in Codex.Core)
    static string UnicodeToCce(string unicode) => CceTable.Encode(unicode);
    static long UnicharToCce(long unicode) => CceTable.UnicharToCce(unicode);

    /// <summary>Escape a CCE-encoded string for C# string literal emission.</summary>
    static string EscapeCceString(string cce)
    {
        StringBuilder sb = new StringBuilder(cce.Length * 4);
        foreach (char c in cce)
        {
            if (c == '\\') sb.Append("\\\\");
            else if (c == '"') sb.Append("\\\"");
            else if (c >= 32 && c < 127) sb.Append(c);
            else sb.Append($"\\u{(int)c:X4}");
        }
        return sb.ToString();
    }

    static bool HasTypeVariable(CodexType type)
    {
        return type switch
        {
            TypeVariable => true,
            FunctionType ft => HasTypeVariable(ft.Parameter) || HasTypeVariable(ft.Return),
            ListType lt => HasTypeVariable(lt.Element),
            _ => false
        };
    }
}
