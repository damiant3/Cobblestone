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
            SumType st => SanitizeIdentifier(st.TypeName.Value),
            RecordType rt => SanitizeIdentifier(rt.TypeName.Value),
            ConstructedType ct => SanitizeIdentifier(ct.Constructor.Value),
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

    static string EmitSumTypeName(SumType st)
    {
        string baseName = SanitizeIdentifier(st.TypeName.Value);
        HashSet<int> ids = [];
        foreach (SumConstructorType ctor in st.Constructors)
            foreach (CodexType field in ctor.Fields)
                CollectTypeVarIds(field, ids);
        if (ids.Count == 0)
            return baseName;
        return baseName + "<" + string.Join(", ", ids.Order().Select(id => $"T{id}")) + ">";
    }

    static CodexType GetReturnType(IRDefinition def)
    {
        CodexType type = def.Type;
        for (int i = 0; i < def.Parameters.Length; i++)
        {
            while (type is FunctionType pft && pft.Parameter is ProofType)
                type = pft.Return;
            if (type is FunctionType ft)
                type = ft.Return;
            else if (type is DependentFunctionType dep)
                type = dep.Body;
            else
                break;
        }
        while (type is FunctionType pft2 && pft2.Parameter is ProofType)
            type = pft2.Return;
        if (type is EffectfulType eft)
            type = eft.Return;
        return type;
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
        }
    }

    static CodexType FinalReturnType(IRDefinition def)
    {
        CodexType type = def.Type;
        for (int i = 0; i < def.Parameters.Length; i++)
        {
            while (type is FunctionType pft && pft.Parameter is ProofType)
                type = pft.Return;
            if (type is FunctionType ft)
                type = ft.Return;
            else if (type is DependentFunctionType dep)
                type = dep.Body;
            else
                break;
        }
        while (type is FunctionType pft2 && pft2.Parameter is ProofType)
            type = pft2.Return;
        return type;
    }

    static bool IsEffectfulDefinition(IRDefinition def) =>
        FinalReturnType(def) is EffectfulType;

    static bool IsVoidLikeDefinition(IRDefinition def) =>
        IsVoidLike(FinalReturnType(def));

    static string SanitizeIdentifier(string name)
    {
        string sanitized = name.Replace('-', '_').Replace('.', '_');

        return sanitized switch
        {
            "class" or "static" or "void" or "return" or "if" or "else" or "for"
            or "while" or "do" or "switch" or "case" or "break" or "continue"
            or "new" or "this" or "base" or "null" or "true" or "false" or "int"
            or "long" or "string" or "bool" or "double" or "decimal" or "object"
            or "in" or "is" or "as" or "typeof" or "default" or "throw" or "try"
            or "catch" or "finally" or "using" or "namespace" or "public" or "private"
            or "protected" or "internal" or "abstract" or "sealed" or "override"
            or "virtual" or "event" or "delegate" or "out" or "ref" or "params"
                => $"@{sanitized}",
            "Equals" or "GetHashCode" or "ToString" or "GetType" or "MemberwiseClone"
                => $"{sanitized}_",
            _ => sanitized
        };
    }

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
