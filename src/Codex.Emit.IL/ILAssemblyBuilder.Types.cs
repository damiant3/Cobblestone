using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using Codex.Core;
using Codex.IR;
using Codex.Types;

namespace Codex.Emit.IL;

sealed partial class ILAssemblyBuilder
{
    void EmitTypeDefinitions(IRChapter module)
    {
        foreach (KeyValuePair<string, CodexType> kv in module.TypeDefinitions)
        {
            switch (kv.Value)
            {
                case RecordType rec:
                    EmitRecordTypeDef(rec);
                    break;
                case SumType sum:
                    EmitSumTypeDef(sum);
                    break;
            }
        }
    }

    void EmitRecordTypeDef(RecordType rec)
    {
        string typeName = SanitizeName(rec.TypeName.Value);

        TypeDefinitionHandle typeDef = m_metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            m_metadata.GetOrAddString(""),
            m_metadata.GetOrAddString(typeName),
            m_objectRef,
            MetadataTokens.FieldDefinitionHandle(m_metadata.GetRowCount(TableIndex.Field) + 1),
            MetadataTokens.MethodDefinitionHandle(m_metadata.GetRowCount(TableIndex.MethodDef) + 1));

        m_emittedTypes = m_emittedTypes.Set(typeName, typeDef);

        List<(string Name, CodexType Type)> fields = new();
        foreach (RecordFieldType f in rec.Fields)
        {
            string fieldName = SanitizeName(f.FieldName.Value);
            fields.Add((fieldName, f.Type));
            EmitFieldDef(typeName, fieldName, f.Type);
        }
        m_typeFields = m_typeFields.Set(typeName, fields);

        EmitConstructor(typeName, typeDef, fields);
    }

    void EmitSumTypeDef(SumType sum)
    {
        string baseName = SanitizeName(sum.TypeName.Value);

        TypeDefinitionHandle baseDef = m_metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.BeforeFieldInit,
            m_metadata.GetOrAddString(""),
            m_metadata.GetOrAddString(baseName),
            m_objectRef,
            MetadataTokens.FieldDefinitionHandle(m_metadata.GetRowCount(TableIndex.Field) + 1),
            MetadataTokens.MethodDefinitionHandle(m_metadata.GetRowCount(TableIndex.MethodDef) + 1));

        m_emittedTypes = m_emittedTypes.Set(baseName, baseDef);

        // Base type gets a protected no-arg ctor that calls Object::.ctor
        EmitBaseConstructor(baseName, baseDef);

        foreach (SumConstructorType ctor in sum.Constructors)
        {
            string ctorName = SanitizeName(ctor.Name.Value);

            TypeDefinitionHandle ctorTypeDef = m_metadata.AddTypeDefinition(
                TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
                m_metadata.GetOrAddString(""),
                m_metadata.GetOrAddString(ctorName),
                baseDef,
                MetadataTokens.FieldDefinitionHandle(m_metadata.GetRowCount(TableIndex.Field) + 1),
                MetadataTokens.MethodDefinitionHandle(m_metadata.GetRowCount(TableIndex.MethodDef) + 1));

            m_emittedTypes = m_emittedTypes.Set(ctorName, ctorTypeDef);
            m_ctorToBaseType = m_ctorToBaseType.Set(ctorName, baseName);

            List<(string Name, CodexType Type)> fields = new();
            for (int i = 0; i < ctor.Fields.Length; i++)
            {
                string fieldName = $"Field{i}";
                fields.Add((fieldName, ctor.Fields[i]));
                EmitFieldDef(ctorName, fieldName, ctor.Fields[i]);
            }
            m_typeFields = m_typeFields.Set(ctorName, fields);

            EmitConstructor(ctorName, ctorTypeDef, fields);
        }
    }

    void EmitFieldDef(string ownerTypeName, string fieldName, CodexType fieldType)
    {
        BlobBuilder sig = new();
        BlobEncoder encoder = new(sig);
        FieldTypeEncoder fieldSig = encoder.Field();
        EncodeType(fieldSig.Type(), fieldType);

        FieldDefinitionHandle fieldDef = m_metadata.AddFieldDefinition(
            FieldAttributes.Public,
            m_metadata.GetOrAddString(fieldName),
            m_metadata.GetOrAddBlob(sig));

        string key = $"{ownerTypeName}.{fieldName}";
        m_fieldDefs = m_fieldDefs.Set(key, fieldDef);
    }

    void EmitConstructor(string typeName, TypeDefinitionHandle ownerType,
        List<(string Name, CodexType Type)> fields)
    {
        ControlFlowBuilder controlFlow = new();
        InstructionEncoder il = new(new BlobBuilder(), controlFlow);

        // Call base ctor: ldarg.0 then call Object::.ctor (or base sum ctor)
        il.LoadArgument(0);

        if (m_ctorToBaseType.TryGet(typeName, out string? baseTypeName)
            && m_ctorDefs.TryGet($"{baseTypeName}..ctor", out MethodDefinitionHandle baseCtorDef))
        {
            il.Call(baseCtorDef);
        }
        else
        {
            il.Call(m_objectCtorRef);
        }

        // Store each argument into the corresponding field
        for (int i = 0; i < fields.Count; i++)
        {
            il.LoadArgument(0);
            il.LoadArgument(i + 1);

            string fieldKey = $"{typeName}.{fields[i].Name}";
            if (m_fieldDefs.TryGet(fieldKey, out FieldDefinitionHandle fieldHandle))
            {
                il.OpCode(ILOpCode.Stfld);
                il.Token(fieldHandle);
            }
        }

        il.OpCode(ILOpCode.Ret);

        int bodyOffset = m_methodBodies.AddMethodBody(il);

        Action<ParameterTypeEncoder>[] paramEncoders = new Action<ParameterTypeEncoder>[fields.Count];
        for (int i = 0; i < fields.Count; i++)
        {
            CodexType ft = fields[i].Type;
            paramEncoders[i] = p => EncodeType(p.Type(), ft);
        }

        BlobHandle ctorSig = EncodeCtorSignature(paramEncoders);

        MethodDefinitionHandle ctorDef = m_metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.RTSpecialName | MethodAttributes.SpecialName
                | MethodAttributes.HideBySig,
            MethodImplAttributes.IL | MethodImplAttributes.Managed,
            m_metadata.GetOrAddString(".ctor"),
            ctorSig,
            bodyOffset,
            default);

        for (int i = 0; i < fields.Count; i++)
        {
            m_metadata.AddParameter(
                ParameterAttributes.None,
                m_metadata.GetOrAddString(fields[i].Name),
                i + 1);
        }

        m_ctorDefs = m_ctorDefs.Set(typeName, ctorDef);
        // Also store under typeName..ctor for base ctor lookup
        m_ctorDefs = m_ctorDefs.Set($"{typeName}..ctor", ctorDef);
    }

    void EmitBaseConstructor(string typeName, TypeDefinitionHandle ownerType)
    {
        ControlFlowBuilder controlFlow = new();
        InstructionEncoder il = new(new BlobBuilder(), controlFlow);

        il.LoadArgument(0);
        il.Call(m_objectCtorRef);
        il.OpCode(ILOpCode.Ret);

        int bodyOffset = m_methodBodies.AddMethodBody(il);
        BlobHandle ctorSig = EncodeCtorSignature(Array.Empty<Action<ParameterTypeEncoder>>());

        MethodDefinitionHandle ctorDef = m_metadata.AddMethodDefinition(
            MethodAttributes.Family | MethodAttributes.RTSpecialName | MethodAttributes.SpecialName
                | MethodAttributes.HideBySig,
            MethodImplAttributes.IL | MethodImplAttributes.Managed,
            m_metadata.GetOrAddString(".ctor"),
            ctorSig,
            bodyOffset,
            default);

        m_ctorDefs = m_ctorDefs.Set($"{typeName}..ctor", ctorDef);
    }

    BlobHandle EncodeCtorSignature(Action<ParameterTypeEncoder>[] parameters)
    {
        BlobBuilder sig = new();
        BlobEncoder encoder = new(sig);
        MethodSignatureEncoder methodSig = encoder.MethodSignature(
            SignatureCallingConvention.Default, 0, isInstanceMethod: true);
        methodSig.Parameters(parameters.Length,
            returnType => returnType.Void(),
            p =>
            {
                foreach (Action<ParameterTypeEncoder> param in parameters)
                {
                    ParameterTypeEncoder paramEncoder = p.AddParameter();
                    param(paramEncoder);
                }
            });
        return m_metadata.GetOrAddBlob(sig);
    }

    void EmitRecordConstruction(InstructionEncoder il, IRRecord rec, LocalsBuilder locals,
        ImmutableArray<IRParameter> parameters)
    {
        string typeName = SanitizeName(rec.TypeName);
        foreach ((string _, IRExpr value) in rec.Fields)
        {
            EmitExpr(il, value, locals, parameters);
        }

        if (m_ctorDefs.TryGet(typeName, out MethodDefinitionHandle ctorDef))
        {
            il.OpCode(ILOpCode.Newobj);
            il.Token(ctorDef);
        }
    }

    void EmitFieldAccess(InstructionEncoder il, IRFieldAccess fa, LocalsBuilder locals,
        ImmutableArray<IRParameter> parameters)
    {
        EmitExpr(il, fa.Record, locals, parameters);

        string ownerTypeName = ResolveOwnerTypeName(fa.Record.Type);
        string fieldName = SanitizeName(fa.FieldName);
        string fieldKey = $"{ownerTypeName}.{fieldName}";

        if (m_fieldDefs.TryGet(fieldKey, out FieldDefinitionHandle fieldHandle))
        {
            il.OpCode(ILOpCode.Ldfld);
            il.Token(fieldHandle);
        }
    }

    void EmitMatch(InstructionEncoder il, IRMatch match, LocalsBuilder locals,
        ImmutableArray<IRParameter> parameters)
    {
        // Evaluate scrutinee once and store in a local
        EmitExpr(il, match.Scrutinee, locals, parameters);
        int scrutineeLocal = locals.AddLocal("__scrutinee", match.Scrutinee.Type);
        il.StoreLocal(scrutineeLocal);

        // Result local ensures consistent stack depth at the join point
        int resultLocal = locals.AddLocal("__match_result", match.Type);

        LabelHandle endLabel = il.DefineLabel();

        for (int i = 0; i < match.Branches.Length; i++)
        {
            IRMatchBranch branch = match.Branches[i];
            bool isLast = i == match.Branches.Length - 1;

            switch (branch.Pattern)
            {
                case IRWildcardPattern:
                    EmitExpr(il, branch.Body, locals, parameters);
                    il.StoreLocal(resultLocal);
                    if (!isLast)
                    {
                        il.Branch(ILOpCode.Br, endLabel);
                    }

                    break;

                case IRVarPattern varPat:
                    il.LoadLocal(scrutineeLocal);
                    int varLocal = locals.AddLocal(varPat.Name, varPat.Type);
                    il.StoreLocal(varLocal);
                    EmitExpr(il, branch.Body, locals, parameters);
                    il.StoreLocal(resultLocal);
                    if (!isLast)
                    {
                        il.Branch(ILOpCode.Br, endLabel);
                    }

                    break;

                case IRLiteralPattern litPat:
                    EmitLiteralPatternBranch(il, litPat, scrutineeLocal, branch.Body,
                        locals, parameters, endLabel, resultLocal);
                    break;

                case IRCtorPattern ctorPat:
                    EmitCtorPatternBranch(il, ctorPat, scrutineeLocal, branch.Body,
                        locals, parameters, endLabel, resultLocal);
                    break;
            }
        }

        il.MarkLabel(endLabel);
        il.LoadLocal(resultLocal);
    }

    void EmitLiteralPatternBranch(InstructionEncoder il, IRLiteralPattern litPat,
        int scrutineeLocal, IRExpr body, LocalsBuilder locals,
        ImmutableArray<IRParameter> parameters, LabelHandle endLabel, int resultLocal)
    {
        LabelHandle nextLabel = il.DefineLabel();

        il.LoadLocal(scrutineeLocal);
        switch (litPat.Value)
        {
            case long l:
                il.LoadConstantI8(l);
                break;
            case bool b:
                il.LoadConstantI4(b ? 1 : 0);
                break;
            case string s:
                il.LoadString(m_metadata.GetOrAddUserString(s));
                break;
            default:
                il.LoadConstantI8(0);
                break;
        }
        il.OpCode(ILOpCode.Ceq);
        il.Branch(ILOpCode.Brfalse, nextLabel);

        EmitExpr(il, body, locals, parameters);
        il.StoreLocal(resultLocal);
        il.Branch(ILOpCode.Br, endLabel);

        il.MarkLabel(nextLabel);
    }

    void EmitCtorPatternBranch(InstructionEncoder il, IRCtorPattern ctorPat,
        int scrutineeLocal, IRExpr body, LocalsBuilder locals,
        ImmutableArray<IRParameter> parameters, LabelHandle endLabel, int resultLocal)
    {
        string ctorName = SanitizeName(ctorPat.Name);
        LabelHandle nextLabel = il.DefineLabel();

        if (!m_emittedTypes.TryGet(ctorName, out TypeDefinitionHandle ctorTypeDef))
        {
            il.MarkLabel(nextLabel);
            return;
        }

        // isinst check
        il.LoadLocal(scrutineeLocal);
        il.OpCode(ILOpCode.Isinst);
        il.Token(ctorTypeDef);
        il.OpCode(ILOpCode.Dup);
        il.Branch(ILOpCode.Brfalse, nextLabel);

        // Store the casted value
        int castLocal = locals.AddLocal($"__cast_{ctorName}", ctorPat.Type);
        il.StoreLocal(castLocal);

        // Bind sub-pattern variables by loading fields
        BindCtorSubPatterns(il, ctorPat, ctorName, castLocal, locals, parameters);

        EmitExpr(il, body, locals, parameters);
        il.StoreLocal(resultLocal);
        il.Branch(ILOpCode.Br, endLabel);

        il.MarkLabel(nextLabel);
        il.OpCode(ILOpCode.Pop); // pop the null from failed isinst+dup
    }

    void BindCtorSubPatterns(InstructionEncoder il, IRCtorPattern ctorPat,
        string ctorName, int castLocal, LocalsBuilder locals,
        ImmutableArray<IRParameter> parameters)
    {
        List<(string Name, CodexType Type)>? fieldTypes = m_typeFields[ctorName];

        for (int i = 0; i < ctorPat.SubPatterns.Length; i++)
        {
            string fieldKey = $"{ctorName}.Field{i}";
            CodexType? storedType = (fieldTypes is not null && i < fieldTypes.Count)
                ? fieldTypes[i].Type : null;

            switch (ctorPat.SubPatterns[i])
            {
                case IRVarPattern vp:
                    if (m_fieldDefs.TryGet(fieldKey, out FieldDefinitionHandle fh))
                    {
                        il.LoadLocal(castLocal);
                        il.OpCode(ILOpCode.Ldfld);
                        il.Token(fh);
                        if (storedType is not null)
                        {
                            EmitUnboxIfNeeded(il, storedType, vp.Type);
                        }

                        int varLocal = locals.AddLocal(vp.Name, vp.Type);
                        il.StoreLocal(varLocal);
                    }
                    break;

                case IRWildcardPattern:
                    break;

                case IRCtorPattern nested:
                    if (m_fieldDefs.TryGet(fieldKey, out FieldDefinitionHandle nestedFh))
                    {
                        il.LoadLocal(castLocal);
                        il.OpCode(ILOpCode.Ldfld);
                        il.Token(nestedFh);
                        if (storedType is not null)
                        {
                            EmitUnboxIfNeeded(il, storedType, nested.Type);
                        }

                        int nestedLocal = locals.AddLocal($"__nested_{i}", nested.Type);
                        il.StoreLocal(nestedLocal);
                        string nestedName = SanitizeName(nested.Name);
                        BindCtorSubPatterns(il, nested, nestedName, nestedLocal,
                            locals, parameters);
                    }
                    break;
            }
        }
    }

    string ResolveOwnerTypeName(CodexType type)
    {
        return type switch
        {
            RecordType rec => SanitizeName(rec.TypeName.Value),
            SumType sum => SanitizeName(sum.TypeName.Value),
            ConstructedType ct => SanitizeName(ct.Constructor.Value),
            _ => "object"
        };
    }
}
