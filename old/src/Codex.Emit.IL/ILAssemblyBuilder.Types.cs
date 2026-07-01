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

        EmitToString(typeName, fields);
    }

    // Emits `override string ToString()` matching C# record auto-format:
    //   "TypeName { FieldA = <A.ToString>, FieldB = <B.ToString> }"
    //   "TypeName { }" for no fields.
    //
    // Value-type fields are boxed before the virtual ToString dispatch;
    // reference fields dispatch directly; null fields render as "" via a
    // per-field dup+brtrue guard.
    //
    // Bounds stack usage with RuntimeHelpers.TryEnsureSufficientExecutionStack
    // as a recursion guard: self-referential records (e.g.
    // `Tree = record { parent : Tree }`) with a non-null cycle would
    // otherwise StackOverflow on the first non-null parent; the guard
    // returns "TypeName { … }" before the stack runs out.
    //
    // Builds the result in one string.Concat(string[]) call — O(total
    // length), vs the 2-arg chain form's O(F²) copy-of-accumulator cost.
    void EmitToString(string typeName, List<(string Name, CodexType Type)> fields)
    {
        ControlFlowBuilder cf = new();
        InstructionEncoder il = new(new BlobBuilder(), cf);

        // Recursion guard: if the stack is too low for another frame,
        // return the truncated form and don't recurse into any field.
        LabelHandle stackOk = il.DefineLabel();
        il.Call(m_tryEnsureStackRef);
        il.Branch(ILOpCode.Brtrue, stackOk);
        il.LoadString(m_metadata.GetOrAddUserString($"{typeName} {{ … }}"));
        il.OpCode(ILOpCode.Ret);
        il.MarkLabel(stackOk);

        if (fields.Count == 0)
        {
            il.LoadString(m_metadata.GetOrAddUserString($"{typeName} {{ }}"));
            il.OpCode(ILOpCode.Ret);
        }
        else
        {
            // Allocate a string[] of size 2 + 2*F (open, per-field sep +
            // value.ToString, close), then Concat it in one pass.
            int parts = 2 + 2 * fields.Count;
            il.LoadConstantI4(parts);
            il.OpCode(ILOpCode.Newarr);
            il.Token(m_stringRef);

            int slot = 0;
            StoreStringSlot(il, slot++, m_metadata.GetOrAddUserString($"{typeName} {{ "));
            for (int i = 0; i < fields.Count; i++)
            {
                string sep = i == 0 ? $"{fields[i].Name} = " : $", {fields[i].Name} = ";
                StoreStringSlot(il, slot++, m_metadata.GetOrAddUserString(sep));

                // Value slot: compute field.ToString inline, then stelem_ref.
                il.OpCode(ILOpCode.Dup);
                il.LoadConstantI4(slot++);
                EmitFieldToString(il, typeName, fields[i]);
                il.OpCode(ILOpCode.Stelem_ref);
            }
            StoreStringSlot(il, slot, m_metadata.GetOrAddUserString(" }"));

            il.Call(m_stringConcatArrayRef);
            il.OpCode(ILOpCode.Ret);
        }

        int bodyOffset = m_methodBodies.AddMethodBody(il);

        BlobBuilder sig = new();
        new BlobEncoder(sig).MethodSignature(SignatureCallingConvention.Default, 0, isInstanceMethod: true)
            .Parameters(0, r => r.Type().String(), _ => { });

        m_metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            MethodImplAttributes.IL | MethodImplAttributes.Managed,
            m_metadata.GetOrAddString("ToString"),
            m_metadata.GetOrAddBlob(sig),
            bodyOffset,
            default);
    }

    // Stack layout: [..., string[]] → [..., string[]]. Dups the array so
    // stelem_ref's pop doesn't consume it.
    void StoreStringSlot(InstructionEncoder il, int slot, UserStringHandle value)
    {
        il.OpCode(ILOpCode.Dup);
        il.LoadConstantI4(slot);
        il.LoadString(value);
        il.OpCode(ILOpCode.Stelem_ref);
    }

    // Leaves the stringified field value on the stack as `string`. Null
    // field values render as "" via a dup+brtrue guard around the virtual
    // ToString call so the dispatch never NRE's.
    void EmitFieldToString(InstructionEncoder il, string typeName, (string Name, CodexType Type) field)
    {
        il.LoadArgument(0);
        il.OpCode(ILOpCode.Ldfld);
        string fieldKey = $"{typeName}.{field.Name}";
        if (!m_fieldDefs.TryGet(fieldKey, out FieldDefinitionHandle fh))
        {
            throw new InvalidOperationException(
                $"EmitToString: field {fieldKey} not in m_fieldDefs");
        }
        il.Token(fh);

        TypeReferenceHandle? boxTarget = field.Type switch
        {
            IntegerType or CharType => m_int64Ref,
            NumberType => m_doubleRef,
            BooleanType => m_booleanRef,
            _ => null
        };
        if (boxTarget is not null)
        {
            il.OpCode(ILOpCode.Box);
            il.Token(boxTarget.Value);
        }

        LabelHandle notNull = il.DefineLabel();
        LabelHandle after = il.DefineLabel();
        il.OpCode(ILOpCode.Dup);
        il.Branch(ILOpCode.Brtrue, notNull);
        il.OpCode(ILOpCode.Pop);
        il.LoadString(m_metadata.GetOrAddUserString(""));
        il.Branch(ILOpCode.Br, after);
        il.MarkLabel(notNull);
        il.OpCode(ILOpCode.Callvirt);
        il.Token(m_objectToStringRef);
        il.MarkLabel(after);
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
        List<(string Name, CodexType Type)>? fieldTypes = m_typeFields[typeName];
        int ai = 0;
        foreach ((string _, IRExpr value) in rec.Fields)
        {
            EmitExpr(il, value, locals, parameters);
            if (fieldTypes is not null && ai < fieldTypes.Count)
                EmitBoxIfNeeded(il, value.Type, fieldTypes[ai].Type);
            ai++;
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

            // If the field was declared with a generic type (TypeVariable),
            // its IL slot is `object`. The use-site IRFieldAccess.Type carries
            // the substituted concrete type — unbox_any it so downstream ops
            // see a value of the right IL kind.
            List<(string Name, CodexType Type)>? fieldTypes = m_typeFields[ownerTypeName];
            if (fieldTypes is not null)
            {
                foreach ((string name, CodexType declared) in fieldTypes)
                {
                    if (name == fieldName)
                    {
                        EmitUnboxIfNeeded(il, declared, fa.Type);
                        break;
                    }
                }
            }
        }
    }

    void EmitMatch(InstructionEncoder il, IRMatch match, LocalsBuilder locals,
        ImmutableArray<IRParameter> parameters)
    {
        // Evaluate scrutinee once and store in a local
        EmitExpr(il, match.Scrutinee, locals, parameters);
        int scrutineeLocal = locals.AddLocal("__scrutinee", match.Scrutinee.Type);
        il.StoreLocal(scrutineeLocal);

        // Void-like arms leave the stack empty — don't try to capture their
        // "result" in a local. Storing from an empty stack fails IL verification
        // at runtime (InvalidProgramException).
        bool isVoid = IsVoidLike(match.Type);
        int resultLocal = isVoid ? -1 : locals.AddLocal("__match_result", match.Type);

        LabelHandle endLabel = il.DefineLabel();

        for (int i = 0; i < match.Branches.Length; i++)
        {
            IRMatchBranch branch = match.Branches[i];
            bool isLast = i == match.Branches.Length - 1;

            switch (branch.Pattern)
            {
                case IRWildcardPattern:
                    EmitExpr(il, branch.Body, locals, parameters);
                    if (!isVoid) il.StoreLocal(resultLocal);
                    if (!isLast) il.Branch(ILOpCode.Br, endLabel);
                    break;

                case IRVarPattern varPat:
                    il.LoadLocal(scrutineeLocal);
                    int varLocal = locals.AddLocal(varPat.Name, varPat.Type);
                    il.StoreLocal(varLocal);
                    EmitExpr(il, branch.Body, locals, parameters);
                    if (!isVoid) il.StoreLocal(resultLocal);
                    if (!isLast) il.Branch(ILOpCode.Br, endLabel);
                    break;

                case IRLiteralPattern litPat:
                    EmitLiteralPatternBranch(il, litPat, scrutineeLocal, branch.Body,
                        locals, parameters, endLabel, resultLocal, isVoid);
                    break;

                case IRCtorPattern ctorPat:
                    EmitCtorPatternBranch(il, ctorPat, scrutineeLocal, branch.Body,
                        locals, parameters, endLabel, resultLocal, isVoid);
                    break;
            }
        }

        il.MarkLabel(endLabel);
        if (!isVoid) il.LoadLocal(resultLocal);
    }

    void EmitLiteralPatternBranch(InstructionEncoder il, IRLiteralPattern litPat,
        int scrutineeLocal, IRExpr body, LocalsBuilder locals,
        ImmutableArray<IRParameter> parameters, LabelHandle endLabel, int resultLocal,
        bool isVoid)
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
        if (!isVoid) il.StoreLocal(resultLocal);
        il.Branch(ILOpCode.Br, endLabel);

        il.MarkLabel(nextLabel);
    }

    void EmitCtorPatternBranch(InstructionEncoder il, IRCtorPattern ctorPat,
        int scrutineeLocal, IRExpr body, LocalsBuilder locals,
        ImmutableArray<IRParameter> parameters, LabelHandle endLabel, int resultLocal,
        bool isVoid)
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
        if (!isVoid) il.StoreLocal(resultLocal);
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
                            EmitUnboxIfNeeded(il, storedType, vp.Type);
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
                            EmitUnboxIfNeeded(il, storedType, nested.Type);
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
