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
    Dictionary<string, CodexType> m_typeDefsForRecordSet = [];
    int m_matchCounter;

    public string TargetName => "C#";
    public string FileExtension => ".cs";

    public string Emit(IRChapter module)
    {
        m_constructorNames = CollectConstructorNames(module);
        m_definitionArity = ValueMap<string, int>.s_empty;
        m_definitionParamNames = ValueMap<string, ImmutableArray<string>>.s_empty;
        m_typeDefsForRecordSet = [];
        foreach (KeyValuePair<string, CodexType> kv in module.TypeDefinitions)
            m_typeDefsForRecordSet[kv.Key] = kv.Value;
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
        EmitBufRuntime(sb);

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

    static void EmitTypeDefinitions(StringBuilder sb, IRChapter module)
    {
        foreach (KeyValuePair<string, CodexType> kv in module.TypeDefinitions
            .OrderBy(kv => kv.Key, StringComparer.Ordinal))
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

    static Set<string> CollectConstructorNames(IRChapter module)
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
            sb.Append("        return ");
            EmitExpr(sb, def.Body, 2);
            sb.AppendLine(";");
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
        sb.Append(CceTable.GenerateRuntimeSource());
    }

    static void EmitBufRuntime(StringBuilder sb)
    {
        sb.AppendLine("static class _Buf {");
        sb.AppendLine("    static byte[] _mem = new byte[16 * 1024 * 1024];");
        sb.AppendLine("    static long _ptr = 0;");
        sb.AppendLine("    public static long heap_save() => _ptr;");
        sb.AppendLine("    public static long heap_restore(object p) { _ptr = (long)p; return 0; }");
        sb.AppendLine("    public static long heap_advance(object n) { _ptr += (long)n; return 0; }");
        sb.AppendLine("    public static long buf_write_byte(object b, object off, object v) { _mem[(long)b + (long)off] = (byte)(long)v; return (long)off + 1; }");
        sb.AppendLine("    public static long buf_write_bytes(object b, object off, object vs) {");
        sb.AppendLine("        var list = (List<long>)vs; long o = (long)off; long ba = (long)b;");
        sb.AppendLine("        for (int i = 0; i < list.Count; i++) _mem[ba + o + i] = (byte)list[i];");
        sb.AppendLine("        return o + list.Count;");
        sb.AppendLine("    }");
        sb.AppendLine("    public static List<long> buf_read_bytes(object b, object off, object n) {");
        sb.AppendLine("        long ba = (long)b; long o = (long)off; int cnt = (int)(long)n;");
        sb.AppendLine("        var r = new List<long>(cnt);");
        sb.AppendLine("        for (int i = 0; i < cnt; i++) r.Add(_mem[ba + o + i]);");
        sb.AppendLine("        return r;");
        sb.AppendLine("    }");
        sb.AppendLine("    public static dynamic list_with_capacity(object cap) => new List<object>((int)(long)cap);");
        sb.AppendLine("}");
        sb.AppendLine();
    }
}
