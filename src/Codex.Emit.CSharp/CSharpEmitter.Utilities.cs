using System.Text;
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
            NumberType => "decimal",
            TextType => "string",
            BooleanType => "bool",
            CharType => "long",
            NothingType => "object",
            VoidType => "void",
            EffectfulType eft => EmitType(eft.Return),
            LinearType lin => EmitType(lin.Inner),
            ListType lt => $"List<{EmitType(lt.Element)}>",
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

    // CCE byte → Unicode code point (Tier 0, 128 entries)
    static readonly int[] s_cceToUnicode = {
        0, 10, 32,                                                             // 0-2: whitespace (NUL, LF, space)
        48, 49, 50, 51, 52, 53, 54, 55, 56, 57,                               // 3-12: digits
        101, 116, 97, 111, 105, 110, 115, 104, 114, 100,                      // 13-22: lower
        108, 99, 117, 109, 119, 102, 103, 121, 112, 98,                       // 23-32
        118, 107, 106, 120, 113, 122,                                           // 33-38
        69, 84, 65, 79, 73, 78, 83, 72, 82, 68,                               // 39-48: upper
        76, 67, 85, 77, 87, 70, 71, 89, 80, 66,                               // 49-58
        86, 75, 74, 88, 81, 90,                                                 // 59-64
        46, 44, 33, 63, 58, 59, 39, 34, 45, 40, 41,                           // 65-75: prose punct
        43, 61, 42, 60, 62,                                                     // 76-80: operators
        47, 64, 35, 38, 95, 92, 124, 91, 93, 123, 125, 126, 96,              // 81-93: syntax
        233, 232, 234, 235, 225, 224, 226, 228, 243, 242,                     // 94-103: accented
        244, 246, 250, 249, 251, 252, 241, 231, 237,                           // 104-112
        1072, 1086, 1077, 1080, 1085, 1090, 1089, 1088,                       // 113-120: cyrillic
        1074, 1083, 1082, 1084, 1076, 1087, 1091                              // 121-127
    };

    static readonly Dictionary<int, int> s_unicodeToCce = BuildUnicodeToCce();

    static Dictionary<int, int> BuildUnicodeToCce()
    {
        var d = new Dictionary<int, int>();
        for (int i = 0; i < s_cceToUnicode.Length; i++)
            d[s_cceToUnicode[i]] = i;
        return d;
    }

    /// <summary>Convert a Unicode string to CCE-encoded string at compile time.</summary>
    static string UnicodeToCce(string unicode)
    {
        char[] result = new char[unicode.Length];
        for (int i = 0; i < unicode.Length; i++)
        {
            int u = unicode[i];
            result[i] = s_unicodeToCce.TryGetValue(u, out int cce) ? (char)cce : (char)0;
        }
        return new string(result);
    }

    /// <summary>Convert a Unicode char to its CCE byte value at compile time.</summary>
    static long UnicharToCce(long unicode)
    {
        return s_unicodeToCce.TryGetValue((int)unicode, out int cce) ? cce : 0;
    }

    /// <summary>Escape a CCE-encoded string for C# string literal emission.</summary>
    static string EscapeCceString(string cce)
    {
        var sb = new StringBuilder(cce.Length * 4);
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
