using System.Collections.Immutable;
using Codex.Core;
using Codex.IR;
using Codex.Types;

namespace Codex.Emit.Wasm;

sealed partial class WasmModuleBuilder
{
    void EmitImports()
    {
        int fdWriteType = AddFuncType(
            new byte[] { WasmI32, WasmI32, WasmI32, WasmI32 },
            new byte[] { WasmI32 });
        m_fdWriteIndex = m_nextFuncIndex++;
        m_imports.Add(new WasmImport("wasi_snapshot_preview1", "fd_write", ImportFunc, fdWriteType));
        m_importCount = m_nextFuncIndex;
    }

    void EmitRuntimeGlobals()
    {
        m_heapPtrGlobalIndex = m_globals.Count;
        m_globals.Add(new WasmGlobal(WasmI32, GlobalMut, m_dataOffset));

        m_regionSpGlobalIndex = m_globals.Count;
        m_globals.Add(new WasmGlobal(WasmI32, GlobalMut, 0));
    }

    void PreRegisterFunctions(IRChapter module)
    {
        m_printI64Index = m_nextFuncIndex++;
        m_functionTypeIndices.Add(-1);

        m_printBoolIndex = m_nextFuncIndex++;
        m_functionTypeIndices.Add(-1);

        m_strEqIndex = m_nextFuncIndex++;
        m_functionTypeIndices.Add(-1);

        foreach (IRDefinition def in module.Definitions)
        {
            m_functionIndex = m_functionIndex.Set(def.Name, m_nextFuncIndex);
            m_nextFuncIndex++;
            m_functionTypeIndices.Add(-1);
        }

        m_functionIndex = m_functionIndex.Set("__wasm_start", m_nextFuncIndex);
        m_nextFuncIndex++;
        m_functionTypeIndices.Add(-1);
    }

    void EmitDefinition(IRDefinition def)
    {
        int paramCount = def.Parameters.Length;
        CodexType returnType = ComputeReturnType(def.Type, paramCount);

        byte[] paramTypes = new byte[paramCount];
        for (int i = 0; i < paramCount; i++)
            paramTypes[i] = WasmTypeFor(def.Parameters[i].Type);

        byte[] resultTypes = returnType is VoidType or NothingType
            ? Array.Empty<byte>() : new byte[] { WasmTypeFor(returnType) };

        int typeIndex = AddFuncType(paramTypes, resultTypes);
        int funcSlot = m_functionIndex.Get(def.Name, 0) - m_importCount;
        m_functionTypeIndices[funcSlot] = typeIndex;

        List<byte> localTypes = new();
        ValueMap<string, int> localMap = ValueMap<string, int>.s_empty;
        for (int i = 0; i < paramCount; i++)
            localMap = localMap.Set(def.Parameters[i].Name, i);

        MemoryStream body = new();
        int nextLocal = paramCount;
        EmitExpr(body, def.Body, localMap, ref nextLocal, localTypes, returnType);
        body.WriteByte(OpEnd);

        m_functionBodies.Add(EncodeFunctionBody(body.ToArray(), localTypes));
    }

    void EmitStartFunction(IRChapter module)
    {
        // _start calls main and prints the result
        IRDefinition? mainDef = null;
        foreach (IRDefinition def in module.Definitions)
        {
            if (def.Name == "main")
            {
                mainDef = def;
                break;
            }
        }

        int startSlot = m_functionIndex.Get("__wasm_start", 0) - m_importCount;

        if (mainDef is null)
        {
            int voidType = AddFuncType(Array.Empty<byte>(), Array.Empty<byte>());
            m_functionTypeIndices[startSlot] = voidType;
            MemoryStream body = new();
            body.WriteByte(OpEnd);
            m_functionBodies.Add(EncodeFunctionBody(body.ToArray(), new List<byte>()));
        }
        else
        {
            int voidType = AddFuncType(Array.Empty<byte>(), Array.Empty<byte>());
            m_functionTypeIndices[startSlot] = voidType;

            MemoryStream body = new();
            List<byte> localTypes = new();
            CodexType returnType = ComputeReturnType(mainDef.Type, mainDef.Parameters.Length);

            int mainIdx = m_functionIndex.Get("main", 0);
            body.WriteByte(OpCall);
            WriteUnsignedLeb128(body, mainIdx);

            EmitPrintResult(body, returnType, localTypes);

            body.WriteByte(OpEnd);
            m_functionBodies.Add(EncodeFunctionBody(body.ToArray(), localTypes));
        }

        int startFuncIndex = m_functionIndex.Get("__wasm_start", 0);
        m_exports.Add(new WasmExport("_start", ExportFunc, startFuncIndex));
        m_exports.Add(new WasmExport("memory", ExportMemory, 0));
    }

    void EmitPrintResult(MemoryStream body, CodexType type, List<byte> localTypes)
    {
        switch (type)
        {
            case IntegerType:
                body.WriteByte(OpCall);
                WriteUnsignedLeb128(body, m_printI64Index);
                break;

            case BooleanType:
                body.WriteByte(OpCall);
                WriteUnsignedLeb128(body, m_printBoolIndex);
                break;

            case TextType:
                int ptrLocal = localTypes.Count;
                localTypes.Add(WasmI32);
                body.WriteByte(OpLocalSet);
                WriteUnsignedLeb128(body, ptrLocal);
                EmitFdWriteFromLengthPrefixed(body, ptrLocal);
                EmitWriteNewline(body, localTypes);
                break;

            case VoidType or NothingType:
                break;

            default:
                body.WriteByte(OpDrop);
                break;
        }
    }

    // ── Expression emission ──────────────────────────────────────

    void EmitExpr(MemoryStream body, IRExpr expr,
        ValueMap<string, int> localMap, ref int nextLocal,
        List<byte> localTypes, CodexType expectedReturn)
    {
        switch (expr)
        {
            case IRIntegerLit intLit:
                body.WriteByte(OpI64Const);
                WriteSignedLeb128(body, intLit.Value);
                break;

            case IRNumberLit numLit:
                body.WriteByte(OpF64Const);
                byte[] f64Bytes = BitConverter.GetBytes((double)numLit.Value);
                body.Write(f64Bytes, 0, 8);
                break;

            case IRBoolLit boolLit:
                body.WriteByte(OpI32Const);
                WriteSignedLeb128(body, boolLit.Value ? 1 : 0);
                break;

            case IRTextLit textLit:
                EmitTextLiteral(body, textLit.Value);
                break;

            case IRName name:
                EmitName(body, name, localMap);
                break;

            case IRBinary bin:
                EmitBinary(body, bin, localMap, ref nextLocal, localTypes);
                break;

            case IRIf ifExpr:
                EmitIf(body, ifExpr, localMap, ref nextLocal, localTypes);
                break;

            case IRLet letExpr:
                EmitLet(body, letExpr, localMap, ref nextLocal, localTypes);
                break;

            case IRApply apply:
                EmitApply(body, apply, localMap, ref nextLocal, localTypes);
                break;

            case IRDo doExpr:
                EmitDo(body, doExpr, localMap, ref nextLocal, localTypes);
                break;

            case IRNegate neg:
                // 0 - operand
                body.WriteByte(OpI64Const);
                WriteSignedLeb128(body, 0);
                EmitExpr(body, neg.Operand, localMap, ref nextLocal, localTypes, neg.Type);
                body.WriteByte(OpI64Sub);
                break;

            case IRMatch match:
                EmitMatch(body, match, localMap, ref nextLocal, localTypes);
                break;

            case IRRegion region:
                EmitRegion(body, region, localMap, ref nextLocal, localTypes);
                break;

            case IRRecord rec:
                EmitRecord(body, rec, localMap, ref nextLocal, localTypes);
                break;

            case IRFieldAccess fa:
                EmitFieldAccess(body, fa, localMap, ref nextLocal, localTypes);
                break;
        }
    }

    void EmitName(MemoryStream body, IRName name, ValueMap<string, int> localMap)
    {
        if (localMap.TryGet(name.Name, out int localIdx))
        {
            body.WriteByte(OpLocalGet);
            WriteUnsignedLeb128(body, localIdx);
        }
        else if (m_functionIndex.TryGet(name.Name, out int funcIdx))
        {
            // Zero-arg function call
            if (name.Type is not FunctionType)
            {
                body.WriteByte(OpCall);
                WriteUnsignedLeb128(body, funcIdx);
            }
        }
        else if (name.Type is SumType sumType)
        {
            // Zero-arg constructor: allocate just a tag word
            int tag = -1;
            for (int i = 0; i < sumType.Constructors.Length; i++)
            {
                if (sumType.Constructors[i].Name.Value == name.Name)
                {
                    tag = i;
                    break;
                }
            }
            if (tag >= 0)
            {
                int ptrLocal = localMap.Count; // borrow a scratch approach
                // inline bump-alloc 8 bytes for tag
                body.WriteByte(OpGlobalGet); WriteUnsignedLeb128(body, m_heapPtrGlobalIndex);
                // dup via local
                int tmpPtr = localMap.Count + 100; // avoid collision — use nextLocal from caller
                // Actually, we can't use nextLocal here since we don't have ref.
                // Just emit the tag at current heap_ptr, advance, return old ptr.
                // Use a simpler pattern: push heap_ptr, store tag, advance heap_ptr
                body.WriteByte(OpGlobalGet); WriteUnsignedLeb128(body, m_heapPtrGlobalIndex);
                body.WriteByte(OpI32Const); WriteSignedLeb128(body, tag);
                body.WriteByte(OpI32Store); body.WriteByte(0x02); WriteUnsignedLeb128(body, 0);
                // Push old heap_ptr as result
                body.WriteByte(OpGlobalGet); WriteUnsignedLeb128(body, m_heapPtrGlobalIndex);
                // Advance heap_ptr by 8
                body.WriteByte(OpGlobalGet); WriteUnsignedLeb128(body, m_heapPtrGlobalIndex);
                body.WriteByte(OpI32Const); WriteSignedLeb128(body, 8);
                body.WriteByte(OpI32Add);
                body.WriteByte(OpGlobalSet); WriteUnsignedLeb128(body, m_heapPtrGlobalIndex);
            }
        }
    }

    void EmitBinary(MemoryStream body, IRBinary bin,
        ValueMap<string, int> localMap, ref int nextLocal, List<byte> localTypes)
    {
        if (bin.Op == IRBinaryOp.AppendText)
        {
            EmitTextAppend(body, bin.Left, bin.Right, localMap, ref nextLocal, localTypes);
            return;
        }

        EmitExpr(body, bin.Left, localMap, ref nextLocal, localTypes, bin.Left.Type);
        EmitExpr(body, bin.Right, localMap, ref nextLocal, localTypes, bin.Right.Type);

        switch (bin.Op)
        {
            case IRBinaryOp.AddInt: body.WriteByte(OpI64Add); break;
            case IRBinaryOp.SubInt: body.WriteByte(OpI64Sub); break;
            case IRBinaryOp.MulInt: body.WriteByte(OpI64Mul); break;
            case IRBinaryOp.DivInt: body.WriteByte(OpI64DivS); break;
            case IRBinaryOp.AddNum: body.WriteByte(OpF64Add); break;
            case IRBinaryOp.SubNum: body.WriteByte(OpF64Sub); break;
            case IRBinaryOp.MulNum: body.WriteByte(OpF64Mul); break;
            case IRBinaryOp.DivNum: body.WriteByte(OpF64Div); break;
            case IRBinaryOp.Eq:
                EmitEq(body, bin.Left.Type);
                break;
            case IRBinaryOp.NotEq:
                EmitEq(body, bin.Left.Type);
                body.WriteByte(OpI32Eqz);
                break;
            case IRBinaryOp.Lt:
                if (bin.Left.Type is NumberType) body.WriteByte(OpF64Lt);
                else body.WriteByte(OpI64LtS);
                break;
            case IRBinaryOp.Gt:
                if (bin.Left.Type is NumberType) body.WriteByte(OpF64Gt);
                else body.WriteByte(OpI64GtS);
                break;
            case IRBinaryOp.LtEq:
                if (bin.Left.Type is NumberType) body.WriteByte(OpF64Le);
                else body.WriteByte(OpI64LeS);
                break;
            case IRBinaryOp.GtEq:
                if (bin.Left.Type is NumberType) body.WriteByte(OpF64Ge);
                else body.WriteByte(OpI64GeS);
                break;
            case IRBinaryOp.And:
                body.WriteByte(OpI32Mul); // both are i32 0/1
                break;
            case IRBinaryOp.Or:
                body.WriteByte(OpI32Add);
                body.WriteByte(OpI32Const); WriteSignedLeb128(body, 0);
                body.WriteByte(OpI32Ne);
                break;
        }
    }

    void EmitEq(MemoryStream body, CodexType type)
    {
        switch (type)
        {
            case NumberType:
                body.WriteByte(OpF64Eq);
                break;
            case BooleanType:
                body.WriteByte(OpI32Eq);
                break;
            case TextType:
                // String equality: compare length-prefixed byte sequences
                body.WriteByte(OpCall);
                WriteUnsignedLeb128(body, m_strEqIndex);
                break;
            default:
                body.WriteByte(OpI64Eq);
                break;
        }
    }

    void EmitIf(MemoryStream body, IRIf ifExpr,
        ValueMap<string, int> localMap, ref int nextLocal, List<byte> localTypes)
    {
        EmitExpr(body, ifExpr.Condition, localMap, ref nextLocal, localTypes, BooleanType.s_instance);

        // If condition is i64 (from comparison), wrap to i32
        if (ifExpr.Condition.Type is IntegerType)
        {
            body.WriteByte(OpI32WrapI64);
        }

        byte blockType = WasmBlockTypeFor(ifExpr.Type);
        body.WriteByte(OpIf);
        body.WriteByte(blockType);

        EmitExpr(body, ifExpr.Then, localMap, ref nextLocal, localTypes, ifExpr.Type);

        body.WriteByte(OpElse);

        EmitExpr(body, ifExpr.Else, localMap, ref nextLocal, localTypes, ifExpr.Type);

        body.WriteByte(OpEnd);
    }

    void EmitLet(MemoryStream body, IRLet letExpr,
        ValueMap<string, int> localMap, ref int nextLocal, List<byte> localTypes)
    {
        EmitExpr(body, letExpr.Value, localMap, ref nextLocal, localTypes, letExpr.NameType);

        int localIdx = nextLocal++;
        localTypes.Add(WasmTypeFor(letExpr.NameType));
        body.WriteByte(OpLocalSet);
        WriteUnsignedLeb128(body, localIdx);

        ValueMap<string, int> innerMap = localMap.Set(letExpr.Name, localIdx);
        EmitExpr(body, letExpr.Body, innerMap, ref nextLocal, localTypes, letExpr.Body.Type);
    }

    void EmitApply(MemoryStream body, IRApply apply,
        ValueMap<string, int> localMap, ref int nextLocal, List<byte> localTypes)
    {
        List<IRExpr> args = new();
        IRExpr func = apply;
        while (func is IRApply inner)
        {
            args.Add(inner.Argument);
            func = inner.Function;
        }
        args.Reverse();

        if (func is IRName funcName)
        {
            if (TryEmitBuiltin(body, funcName.Name, args, localMap, ref nextLocal, localTypes))
                return;

            if (m_functionIndex.TryGet(funcName.Name, out int funcIdx))
            {
                foreach (IRExpr arg in args)
                {
                    EmitExpr(body, arg, localMap, ref nextLocal, localTypes, arg.Type);
                }
                body.WriteByte(OpCall);
                WriteUnsignedLeb128(body, funcIdx);
                return;
            }

            // Sum type constructor: allocate [tag i32][field0 8B][field1 8B]...
            if (TryEmitConstructor(body, funcName.Name, args, apply.Type, localMap, ref nextLocal, localTypes))
                return;
        }
    }

    bool TryEmitConstructor(MemoryStream body, string ctorName, List<IRExpr> args,
        CodexType resultType, ValueMap<string, int> localMap,
        ref int nextLocal, List<byte> localTypes)
    {
        // Walk through the result type to find a SumType
        SumType? sumType = resultType as SumType;
        if (sumType is null)
            return false;

        // Find the tag index
        int tag = -1;
        for (int i = 0; i < sumType.Constructors.Length; i++)
        {
            if (sumType.Constructors[i].Name.Value == ctorName)
            {
                tag = i;
                break;
            }
        }
        if (tag < 0)
            return false;

        int totalSize = 8 + args.Count * 8; // tag + fields

        // Bump-allocate
        int ptrLocal = nextLocal++; localTypes.Add(WasmI32);
        body.WriteByte(OpGlobalGet); WriteUnsignedLeb128(body, m_heapPtrGlobalIndex);
        body.WriteByte(OpLocalSet); WriteUnsignedLeb128(body, ptrLocal);
        body.WriteByte(OpGlobalGet); WriteUnsignedLeb128(body, m_heapPtrGlobalIndex);
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, totalSize);
        body.WriteByte(OpI32Add);
        body.WriteByte(OpGlobalSet); WriteUnsignedLeb128(body, m_heapPtrGlobalIndex);

        // Store tag at offset 0
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, ptrLocal);
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, tag);
        body.WriteByte(OpI32Store); body.WriteByte(0x02); WriteUnsignedLeb128(body, 0);

        // Store fields at offset 8, 16, 24...
        for (int i = 0; i < args.Count; i++)
        {
            body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, ptrLocal);
            EmitExpr(body, args[i], localMap, ref nextLocal, localTypes, args[i].Type);
            int offset = 8 + i * 8;
            if (args[i].Type is IntegerType)
            {
                body.WriteByte(OpI64Store); body.WriteByte(0x03); WriteUnsignedLeb128(body, offset);
            }
            else if (args[i].Type is NumberType)
            {
                body.WriteByte(OpF64Store); body.WriteByte(0x03); WriteUnsignedLeb128(body, offset);
            }
            else
            {
                body.WriteByte(OpI32Store); body.WriteByte(0x02); WriteUnsignedLeb128(body, offset);
            }
        }

        // Return pointer
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, ptrLocal);
        return true;
    }

    void EmitDo(MemoryStream body, IRDo doExpr,
        ValueMap<string, int> localMap, ref int nextLocal, List<byte> localTypes)
    {
        for (int i = 0; i < doExpr.Statements.Length; i++)
        {
            IRDoStatement stmt = doExpr.Statements[i];
            switch (stmt)
            {
                case IRDoExec doExecStmt:
                    EmitExpr(body, doExecStmt.Expression, localMap, ref nextLocal, localTypes, doExecStmt.Expression.Type);
                    // Drop result if not the last statement or if void
                    if (i < doExpr.Statements.Length - 1 && doExecStmt.Expression.Type is not (VoidType or NothingType))
                        body.WriteByte(OpDrop);
                    break;

                case IRDoBind doBind:
                    EmitExpr(body, doBind.Value, localMap, ref nextLocal, localTypes, doBind.NameType);
                    int bindLocal = nextLocal++;
                    localTypes.Add(WasmTypeFor(doBind.NameType));
                    body.WriteByte(OpLocalSet);
                    WriteUnsignedLeb128(body, bindLocal);
                    localMap = localMap.Set(doBind.Name, bindLocal);
                    break;
            }
        }
    }

    void EmitRegion(MemoryStream body, IRRegion region,
        ValueMap<string, int> localMap, ref int nextLocal, List<byte> localTypes)
    {
        // Types with nested heap pointers need deep copy — skip region for now
        if (region.Type is RecordType or SumType or ListType)
        {
            if (TypeHasNestedHeapPointers(region.Type))
            {
                EmitExpr(body, region.Body, localMap, ref nextLocal, localTypes, region.Type);
                return;
            }
        }

        // Closures: skip region (capture types unknown at region exit)
        if (region.Type is FunctionType)
        {
            EmitExpr(body, region.Body, localMap, ref nextLocal, localTypes, region.Type);
            return;
        }

        // Enter region
        EmitRegionEnter(body);

        // Emit body
        EmitExpr(body, region.Body, localMap, ref nextLocal, localTypes, region.Type);

        if (region.NeedsEscapeCopy)
        {
            // Heap escape promotion: copy the return value to the parent region
            int oldPtr = nextLocal++; localTypes.Add(WasmI32);
            int totalSizeLocal = nextLocal++; localTypes.Add(WasmI32);
            int newPtr = nextLocal++; localTypes.Add(WasmI32);

            // Save result pointer
            body.WriteByte(OpLocalSet); WriteUnsignedLeb128(body, oldPtr);

            if (region.Type is TextType)
            {
                // Text: totalSize = 4 + len (4-byte length prefix + data)
                int len = nextLocal++; localTypes.Add(WasmI32);
                body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, oldPtr);
                body.WriteByte(OpI32Load); body.WriteByte(0x02); WriteUnsignedLeb128(body, 0);
                body.WriteByte(OpLocalSet); WriteUnsignedLeb128(body, len);
                body.WriteByte(OpI32Const); WriteSignedLeb128(body, 4);
                body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, len);
                body.WriteByte(OpI32Add);
                body.WriteByte(OpLocalSet); WriteUnsignedLeb128(body, totalSizeLocal);
            }
            else
            {
                // Record/sum with scalar-only fields: fixed-size flat copy
                int flatSize = ComputeFlatSize(region.Type);
                body.WriteByte(OpI32Const); WriteSignedLeb128(body, flatSize);
                body.WriteByte(OpLocalSet); WriteUnsignedLeb128(body, totalSizeLocal);
            }

            // Exit region (heap_ptr restored — old data still physically present)
            EmitRegionExit(body);

            // Bump-allocate in parent region: newPtr = heap_ptr; heap_ptr += totalSize
            body.WriteByte(OpGlobalGet); WriteUnsignedLeb128(body, m_heapPtrGlobalIndex);
            body.WriteByte(OpLocalSet); WriteUnsignedLeb128(body, newPtr);
            body.WriteByte(OpGlobalGet); WriteUnsignedLeb128(body, m_heapPtrGlobalIndex);
            body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, totalSizeLocal);
            body.WriteByte(OpI32Add);
            body.WriteByte(OpGlobalSet); WriteUnsignedLeb128(body, m_heapPtrGlobalIndex);

            // Copy bytes from oldPtr to newPtr
            EmitMemCopyDirect(body, newPtr, oldPtr, 0, totalSizeLocal, ref nextLocal, localTypes);

            // Push new pointer as the result
            body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, newPtr);
        }
        else
        {
            // Scalar return — value is on the WASM stack, survives region exit
            EmitRegionExit(body);
        }
    }

    static bool TypeHasNestedHeapPointers(CodexType type) => type switch
    {
        RecordType rt => rt.Fields.Any(f => IRRegion.TypeNeedsHeapEscape(f.Type)),
        SumType st => st.Constructors.Any(c => c.Fields.Any(f => IRRegion.TypeNeedsHeapEscape(f))),
        ListType lt => IRRegion.TypeNeedsHeapEscape(lt.Element),
        _ => false
    };

    static int ComputeFlatSize(CodexType type) => type switch
    {
        RecordType rt => rt.Fields.Length * 8,
        SumType st => (1 + st.Constructors.Max(c => c.Fields.Length)) * 8,
        _ => 8
    };

    void EmitRegionEnter(MemoryStream body)
    {
        // region_stack[region_sp] = heap_ptr; region_sp++
        body.WriteByte(OpGlobalGet); WriteUnsignedLeb128(body, m_regionSpGlobalIndex);
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 4);
        body.WriteByte(OpI32Mul);
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, RegionStackBase);
        body.WriteByte(OpI32Add);
        body.WriteByte(OpGlobalGet); WriteUnsignedLeb128(body, m_heapPtrGlobalIndex);
        body.WriteByte(OpI32Store); body.WriteByte(0x02); WriteUnsignedLeb128(body, 0);

        body.WriteByte(OpGlobalGet); WriteUnsignedLeb128(body, m_regionSpGlobalIndex);
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 1);
        body.WriteByte(OpI32Add);
        body.WriteByte(OpGlobalSet); WriteUnsignedLeb128(body, m_regionSpGlobalIndex);
    }

    void EmitRegionExit(MemoryStream body)
    {
        // region_sp--; heap_ptr = region_stack[region_sp]
        body.WriteByte(OpGlobalGet); WriteUnsignedLeb128(body, m_regionSpGlobalIndex);
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 1);
        body.WriteByte(OpI32Sub);
        body.WriteByte(OpGlobalSet); WriteUnsignedLeb128(body, m_regionSpGlobalIndex);

        body.WriteByte(OpGlobalGet); WriteUnsignedLeb128(body, m_regionSpGlobalIndex);
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 4);
        body.WriteByte(OpI32Mul);
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, RegionStackBase);
        body.WriteByte(OpI32Add);
        body.WriteByte(OpI32Load); body.WriteByte(0x02); WriteUnsignedLeb128(body, 0);
        body.WriteByte(OpGlobalSet); WriteUnsignedLeb128(body, m_heapPtrGlobalIndex);
    }

    void EmitMatch(MemoryStream body, IRMatch match,
        ValueMap<string, int> localMap, ref int nextLocal, List<byte> localTypes)
    {
        // Evaluate scrutinee and store in local
        EmitExpr(body, match.Scrutinee, localMap, ref nextLocal, localTypes, match.Scrutinee.Type);
        int scrutLocal = nextLocal++;
        localTypes.Add(WasmTypeFor(match.Scrutinee.Type));
        body.WriteByte(OpLocalSet);
        WriteUnsignedLeb128(body, scrutLocal);

        // Emit as nested if/else chain
        byte blockType = WasmBlockTypeFor(match.Type);
        EmitMatchBranches(body, match.Branches, 0, scrutLocal, localMap,
            ref nextLocal, localTypes, blockType);
    }

    void EmitMatchBranches(MemoryStream body, ImmutableArray<IRMatchBranch> branches,
        int index, int scrutLocal, ValueMap<string, int> localMap,
        ref int nextLocal, List<byte> localTypes, byte blockType)
    {
        if (index >= branches.Length)
        {
            body.WriteByte(OpUnreachable);
            return;
        }

        IRMatchBranch branch = branches[index];
        bool isLast = index == branches.Length - 1;

        switch (branch.Pattern)
        {
            case IRWildcardPattern:
                EmitExpr(body, branch.Body, localMap, ref nextLocal, localTypes, branch.Body.Type);
                break;

            case IRVarPattern varPat:
                body.WriteByte(OpLocalGet);
                WriteUnsignedLeb128(body, scrutLocal);
                int varLocal = nextLocal++;
                localTypes.Add(WasmTypeFor(varPat.Type));
                body.WriteByte(OpLocalSet);
                WriteUnsignedLeb128(body, varLocal);
                ValueMap<string, int> varMap = localMap.Set(varPat.Name, varLocal);
                EmitExpr(body, branch.Body, varMap, ref nextLocal, localTypes, branch.Body.Type);
                break;

            case IRLiteralPattern litPat:
                // Load scrutinee and compare
                body.WriteByte(OpLocalGet);
                WriteUnsignedLeb128(body, scrutLocal);
                EmitLiteralValue(body, litPat.Value);
                EmitEq(body, litPat.Type);

                body.WriteByte(OpIf);
                body.WriteByte(blockType);
                EmitExpr(body, branch.Body, localMap, ref nextLocal, localTypes, branch.Body.Type);
                body.WriteByte(OpElse);
                EmitMatchBranches(body, branches, index + 1, scrutLocal, localMap,
                    ref nextLocal, localTypes, blockType);
                body.WriteByte(OpEnd);
                break;

            case IRCtorPattern ctorPat:
                EmitCtorPatternMatch(body, ctorPat, branch, branches, index,
                    scrutLocal, localMap, ref nextLocal, localTypes, blockType);
                break;
        }
    }

    void EmitRecord(MemoryStream body, IRRecord rec,
        ValueMap<string, int> localMap, ref int nextLocal, List<byte> localTypes)
    {
        int fieldCount = rec.Fields.Length;
        int totalSize = fieldCount * 8;

        // Bump-allocate
        int ptrLocal = nextLocal++; localTypes.Add(WasmI32);
        body.WriteByte(OpGlobalGet); WriteUnsignedLeb128(body, m_heapPtrGlobalIndex);
        body.WriteByte(OpLocalSet); WriteUnsignedLeb128(body, ptrLocal);
        body.WriteByte(OpGlobalGet); WriteUnsignedLeb128(body, m_heapPtrGlobalIndex);
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, totalSize);
        body.WriteByte(OpI32Add);
        body.WriteByte(OpGlobalSet); WriteUnsignedLeb128(body, m_heapPtrGlobalIndex);

        // Store each field at ptr + index * 8
        for (int i = 0; i < fieldCount; i++)
        {
            (string _, IRExpr value) = rec.Fields[i];
            body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, ptrLocal);
            EmitExpr(body, value, localMap, ref nextLocal, localTypes, value.Type);
            int offset = i * 8;
            if (value.Type is IntegerType)
            {
                body.WriteByte(OpI64Store); body.WriteByte(0x03); WriteUnsignedLeb128(body, offset);
            }
            else if (value.Type is NumberType)
            {
                body.WriteByte(OpF64Store); body.WriteByte(0x03); WriteUnsignedLeb128(body, offset);
            }
            else
            {
                // Bool, Text ptr, Record ptr — all i32
                body.WriteByte(OpI32Store); body.WriteByte(0x02); WriteUnsignedLeb128(body, offset);
            }
        }

        // Push pointer as result
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, ptrLocal);
    }

    void EmitFieldAccess(MemoryStream body, IRFieldAccess fa,
        ValueMap<string, int> localMap, ref int nextLocal, List<byte> localTypes)
    {
        // Emit the record pointer
        EmitExpr(body, fa.Record, localMap, ref nextLocal, localTypes, fa.Record.Type);

        // Find field index
        int fieldIndex = 0;
        if (fa.Record.Type is RecordType rt)
        {
            for (int i = 0; i < rt.Fields.Length; i++)
            {
                if (rt.Fields[i].FieldName.Value == fa.FieldName)
                {
                    fieldIndex = i;
                    break;
                }
            }
        }

        int offset = fieldIndex * 8;
        if (fa.Type is IntegerType)
        {
            body.WriteByte(OpI64Load); body.WriteByte(0x03); WriteUnsignedLeb128(body, offset);
        }
        else if (fa.Type is NumberType)
        {
            body.WriteByte(OpF64Load); body.WriteByte(0x03); WriteUnsignedLeb128(body, offset);
        }
        else
        {
            body.WriteByte(OpI32Load); body.WriteByte(0x02); WriteUnsignedLeb128(body, offset);
        }
    }

    void EmitCtorPatternMatch(MemoryStream body, IRCtorPattern ctorPat,
        IRMatchBranch branch, ImmutableArray<IRMatchBranch> branches, int index,
        int scrutLocal, ValueMap<string, int> localMap,
        ref int nextLocal, List<byte> localTypes, byte blockType)
    {
        // Sum type layout: [tag: i32 at offset 0][field0 at offset 8][field1 at offset 16]...
        // Find constructor tag index
        int tag = 0;
        CodexType scrutType = ctorPat.Type;
        if (scrutType is SumType sumType)
        {
            for (int i = 0; i < sumType.Constructors.Length; i++)
            {
                if (sumType.Constructors[i].Name.Value == ctorPat.Name)
                {
                    tag = i;
                    break;
                }
            }
        }

        // Load tag from scrutinee and compare
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, scrutLocal);
        body.WriteByte(OpI32Load); body.WriteByte(0x02); WriteUnsignedLeb128(body, 0);
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, tag);
        body.WriteByte(OpI32Eq);

        body.WriteByte(OpIf);
        body.WriteByte(blockType);

        // Bind sub-pattern fields
        ValueMap<string, int> innerMap = localMap;
        for (int i = 0; i < ctorPat.SubPatterns.Length; i++)
        {
            if (ctorPat.SubPatterns[i] is IRVarPattern varPat)
            {
                int fieldLocal = nextLocal++;
                localTypes.Add(WasmTypeFor(varPat.Type));

                body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, scrutLocal);
                int fieldOffset = 8 + i * 8;
                if (varPat.Type is IntegerType)
                {
                    body.WriteByte(OpI64Load); body.WriteByte(0x03);
                    WriteUnsignedLeb128(body, fieldOffset);
                }
                else if (varPat.Type is NumberType)
                {
                    body.WriteByte(OpF64Load); body.WriteByte(0x03);
                    WriteUnsignedLeb128(body, fieldOffset);
                }
                else
                {
                    body.WriteByte(OpI32Load); body.WriteByte(0x02);
                    WriteUnsignedLeb128(body, fieldOffset);
                }

                body.WriteByte(OpLocalSet); WriteUnsignedLeb128(body, fieldLocal);
                innerMap = innerMap.Set(varPat.Name, fieldLocal);
            }
        }

        EmitExpr(body, branch.Body, innerMap, ref nextLocal, localTypes, branch.Body.Type);

        body.WriteByte(OpElse);
        EmitMatchBranches(body, branches, index + 1, scrutLocal, localMap,
            ref nextLocal, localTypes, blockType);
        body.WriteByte(OpEnd);
    }

    void EmitLiteralValue(MemoryStream body, object value)
    {
        switch (value)
        {
            case long l:
                body.WriteByte(OpI64Const);
                WriteSignedLeb128(body, l);
                break;
            case bool b:
                body.WriteByte(OpI32Const);
                WriteSignedLeb128(body, b ? 1 : 0);
                break;
            case string s:
                EmitTextLiteral(body, s);
                break;
        }
    }

    // ── Builtins ─────────────────────────────────────────────────
}
