using System.Collections.Immutable;
using System.Text;
using Codex.Core;
using Codex.IR;
using Codex.Types;

namespace Codex.Emit.CSharp;

public sealed partial class CSharpEmitter : ICodeEmitter
{
    Set<string> m_constructorNames = Set<string>.s_empty;
    ValueMap<string, int> m_definitionArity = ValueMap<string, int>.s_empty;
    ValueMap<string, ImmutableArray<string>> m_definitionParamNames =
        ValueMap<string, ImmutableArray<string>>.s_empty;
    int m_matchCounter;

    public string TargetName => "C#";
    public string FileExtension => ".cs";

    public string Emit(IRModule module)
    {
        m_constructorNames = CollectConstructorNames(module);
        m_definitionArity = ValueMap<string, int>.s_empty;
        m_definitionParamNames = ValueMap<string, ImmutableArray<string>>.s_empty;
        m_matchCounter = 0;
        foreach (IRDefinition d in module.Definitions)
        {
            m_definitionArity = m_definitionArity.Set(d.Name, d.Parameters.Length);
            m_definitionParamNames = m_definitionParamNames.Set(d.Name,
                d.Parameters.Select(p => p.Name).ToImmutableArray());
        }

        StringBuilder sb = new();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.IO;");
        sb.AppendLine("using System.Linq;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine();

        string className = "Codex_" + SanitizeIdentifier(module.Name.Leaf.Value);

        IRDefinition? mainDef = module.Definitions
            .FirstOrDefault(d => d.Name == "main");

        if (mainDef is not null && mainDef.Parameters.Length == 0)
        {
            if (IsEffectfulDefinition(mainDef))
            {
                sb.AppendLine($"{className}.main();");
            }
            else
            {
                sb.AppendLine($"Console.WriteLine({className}.main());");
            }
            sb.AppendLine();
        }
        else if (module.Definitions.Length == 0)
        {
            sb.AppendLine("Console.WriteLine(\"All proofs verified at compile time.\");");
            sb.AppendLine();
        }

        EmitTypeDefinitions(sb, module);
        EmitCceRuntime(sb);

        sb.AppendLine($"public static class {className}");
        sb.AppendLine("{");

        foreach (IRDefinition def in module.Definitions)
        {
            EmitDefinition(sb, def);
            sb.AppendLine();
        }

        sb.AppendLine("}");

        return sb.ToString();
    }

    static void EmitTypeDefinitions(StringBuilder sb, IRModule module)
    {
        foreach (KeyValuePair<string, CodexType> kv in module.TypeDefinitions)
        {
            switch (kv.Value)
            {
                case SumType sum:
                    EmitSumType(sb, sum);
                    break;
                case RecordType rec:
                    EmitRecordType(sb, rec);
                    break;
            }
        }
    }

    static void EmitSumType(StringBuilder sb, SumType sum)
    {
        string baseName = SanitizeIdentifier(sum.TypeName.Value);
        sb.AppendLine($"public abstract record {baseName};");
        sb.AppendLine();

        foreach (SumConstructorType ctor in sum.Constructors)
        {
            string ctorName = SanitizeIdentifier(ctor.Name.Value);
            if (ctor.Fields.IsEmpty)
            {
                sb.AppendLine($"public sealed record {ctorName} : {baseName};");
            }
            else
            {
                sb.Append($"public sealed record {ctorName}(");
                for (int i = 0; i < ctor.Fields.Length; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append($"{EmitType(ctor.Fields[i])} Field{i}");
                }
                sb.AppendLine($") : {baseName};");
            }
        }
        sb.AppendLine();
    }

    static void EmitRecordType(StringBuilder sb, RecordType rec)
    {
        string name = SanitizeIdentifier(rec.TypeName.Value);
        sb.Append($"public sealed record {name}(");
        for (int i = 0; i < rec.Fields.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            RecordFieldType field = rec.Fields[i];
            sb.Append($"{EmitType(field.Type)} {SanitizeIdentifier(field.FieldName.Value)}");
        }
        sb.AppendLine(");");
        sb.AppendLine();
    }

    static Set<string> CollectConstructorNames(IRModule module)
    {
        Set<string> names = Set<string>.s_empty;
        foreach (KeyValuePair<string, CodexType> kv in module.TypeDefinitions)
        {
            if (kv.Value is SumType sum)
            {
                foreach (SumConstructorType ctor in sum.Constructors)
                {
                    names = names.Add(ctor.Name.Value);
                }
            }
        }
        return names;
    }

    void EmitArgument(StringBuilder sb, IRExpr arg, int indent)
    {
        if (arg is IRName name && name.Name == "show")
        {
            sb.Append("new Func<object, string>(x => Convert.ToString(x))");
        }
        else if (arg is IRName name2 && name2.Name == "negate")
        {
            sb.Append("new Func<long, long>(x => -x)");
        }
        else if (arg is IRName fnName && fnName.Type is FunctionType ft
            && !m_constructorNames.Contains(fnName.Name)
            && !HasTypeVariable(ft))
        {
            if (m_definitionArity.TryGet(fnName.Name, out int arity) && arity > 1)
            {
                EmitPartialApplication(sb, fnName.Name, arity, [], indent);
            }
            else
            {
                string pType = EmitType(ft.Parameter);
                string rType = EmitType(ft.Return);
                sb.Append($"new Func<{pType}, {rType}>(");
                sb.Append(SanitizeIdentifier(fnName.Name));
                sb.Append(')');
            }
        }
        else
        {
            EmitExpr(sb, arg, indent);
        }
    }

    void EmitDefinition(StringBuilder sb, IRDefinition def)
    {
        if (HasSelfTailCall(def))
        {
            EmitTailCallDefinition(sb, def);
            return;
        }

        string returnType = EmitType(GetReturnType(def));
        string name = SanitizeIdentifier(def.Name);
        string generics = GenericSuffix(def);

        sb.Append($"    public static {returnType} {name}{generics}(");

        for (int i = 0; i < def.Parameters.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            IRParameter param = def.Parameters[i];
            sb.Append($"{EmitType(param.Type)} {SanitizeIdentifier(param.Name)}");
        }

        sb.AppendLine(")");
        sb.AppendLine("    {");

        if (IsEffectfulDefinition(def) && IsVoidLikeDefinition(def))
        {
            sb.Append("        ");
            EmitExpr(sb, def.Body, 2);
            sb.AppendLine(";");
            sb.AppendLine("        return null;");
        }
        else
        {
            sb.Append("        return ");
            EmitExpr(sb, def.Body, 2);
            sb.AppendLine(";");
        }

        sb.AppendLine("    }");
    }

    static void EmitCceRuntime(StringBuilder sb)
    {
        sb.AppendLine("static class _Cce {");
        // CCE byte → Unicode code point (128 entries)
        sb.AppendLine("    static readonly int[] _toUni = {");
        sb.AppendLine("        0, 10, 32,");                                                   // 0-2: whitespace (NUL, LF, space)
        sb.AppendLine("        48, 49, 50, 51, 52, 53, 54, 55, 56, 57,");                     // 3-12: digits
        sb.AppendLine("        101, 116, 97, 111, 105, 110, 115, 104, 114, 100,");            // 13-22: lower
        sb.AppendLine("        108, 99, 117, 109, 119, 102, 103, 121, 112, 98,");             // 23-32
        sb.AppendLine("        118, 107, 106, 120, 113, 122,");                                // 33-38
        sb.AppendLine("        69, 84, 65, 79, 73, 78, 83, 72, 82, 68,");                     // 39-48: upper
        sb.AppendLine("        76, 67, 85, 77, 87, 70, 71, 89, 80, 66,");                     // 49-58
        sb.AppendLine("        86, 75, 74, 88, 81, 90,");                                      // 59-64
        sb.AppendLine("        46, 44, 33, 63, 58, 59, 39, 34, 45, 40, 41,");                 // 65-75: prose punct
        sb.AppendLine("        43, 61, 42, 60, 62,");                                          // 76-80: operators
        sb.AppendLine("        47, 64, 35, 38, 95, 92, 124, 91, 93, 123, 125, 126, 96,");    // 81-93: syntax
        sb.AppendLine("        233, 232, 234, 235, 225, 224, 226, 228, 243, 242,");           // 94-103: accented
        sb.AppendLine("        244, 246, 250, 249, 251, 252, 241, 231, 237,");                 // 104-112
        sb.AppendLine("        1072, 1086, 1077, 1080, 1085, 1090, 1089, 1088,");             // 113-120: cyrillic
        sb.AppendLine("        1074, 1083, 1082, 1084, 1076, 1087, 1091");                     // 121-127
        sb.AppendLine("    };");

        // Unicode → CCE: sparse lookup via dictionary (covers all mapped code points)
        sb.AppendLine("    static readonly Dictionary<int, int> _fromUni = new();");
        sb.AppendLine("    static _Cce() { for (int i = 0; i < 128; i++) _fromUni[_toUni[i]] = i; }");

        // String conversion: Unicode → CCE
        sb.AppendLine("    public static string FromUnicode(string s) {");
        sb.AppendLine("        var cs = new char[s.Length];");
        sb.AppendLine("        for (int i = 0; i < s.Length; i++) {");
        sb.AppendLine("            int u = s[i];");
        sb.AppendLine("            cs[i] = _fromUni.TryGetValue(u, out int c) ? (char)c : (char)0;");
        sb.AppendLine("        }");
        sb.AppendLine("        return new string(cs);");
        sb.AppendLine("    }");

        // String conversion: CCE → Unicode
        sb.AppendLine("    public static string ToUnicode(string s) {");
        sb.AppendLine("        var cs = new char[s.Length];");
        sb.AppendLine("        for (int i = 0; i < s.Length; i++) {");
        sb.AppendLine("            int b = s[i];");
        sb.AppendLine("            cs[i] = (b >= 0 && b < 128) ? (char)_toUni[b] : '\\uFFFD';");
        sb.AppendLine("        }");
        sb.AppendLine("        return new string(cs);");
        sb.AppendLine("    }");

        // Single char: Unicode code point → CCE byte
        sb.AppendLine("    public static long UniToCce(long u) {");
        sb.AppendLine("        return _fromUni.TryGetValue((int)u, out int c) ? c : 0;");
        sb.AppendLine("    }");

        // Single char: CCE byte → Unicode code point
        sb.AppendLine("    public static long CceToUni(long b) {");
        sb.AppendLine("        return (b >= 0 && b < 128) ? _toUni[(int)b] : 65533;");
        sb.AppendLine("    }");

        sb.AppendLine("}");
        sb.AppendLine();
    }
}
