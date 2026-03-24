using System.Text;
using Codex.Core;
using Codex.IR;
using Codex.Types;

namespace Codex.Emit.X86_64;

sealed class X86_64CodeGen
{
    readonly List<byte> m_text = [];
    readonly List<byte> m_rodata = [];
    readonly Dictionary<string, int> m_functionOffsets = [];
    readonly List<(int PatchOffset, string Target)> m_callPatches = [];
    readonly List<RodataFixup> m_rodataFixups = [];
    readonly Dictionary<string, int> m_stringOffsets = [];
    Map<string, CodexType> m_typeDefs = Map<string, CodexType>.s_empty;
    readonly Dictionary<string, string> m_escapeHelperNames = [];
    readonly Queue<(string Key, string Name, CodexType Type)> m_escapeHelperQueue = new();

    // Register allocator state (per-function)
    // Temps: RAX, RCX, RDX, RSI, RDI, R11 (caller-saved, recycled)
    // Locals: RBX, R12-R15 (callee-saved, monotonic)
    // Spill scratch: R8, R9 (used by LoadLocal for spilled values — NOT in TempRegs)
    // Reserved: RSP (stack), RBP (frame), R10 (heap pointer)
    const byte HeapReg = Reg.R10; // global heap pointer — NOT callee-saved
    static readonly byte[] TempRegs = [Reg.RAX, Reg.RCX, Reg.RDX, Reg.RSI, Reg.RDI, Reg.R11];
    static readonly byte[] LocalRegs = [Reg.RBX, Reg.R12, Reg.R13, Reg.R14, Reg.R15];
    const uint SpillBase = 32; // virtual register numbers for spilled locals

    int m_nextTemp;
    int m_nextLocal;
    int m_spillCount;
    int m_loadLocalToggle;
    Dictionary<string, byte> m_locals = [];

    readonly record struct RodataFixup(int PatchOffset, int RodataOffset);

    public void EmitModule(IRModule module)
    {
        m_typeDefs = module.TypeDefinitions;
        m_escapeHelperNames["text"] = "__escape_text";

        EmitRuntimeHelpers();

        foreach (IRDefinition def in module.Definitions)
            EmitFunction(def);

        EmitEscapeCopyHelpers();
        EmitStart(module);
        PatchCalls();
        PatchRodataRefs();
    }

    public byte[] BuildElf()
    {
        byte[] text = m_text.ToArray();
        byte[] rodata = m_rodata.ToArray();

        if (m_functionOffsets.TryGetValue("__start", out int startOffset))
            return ElfWriterX86_64.WriteExecutable(text, rodata, (ulong)startOffset);

        return ElfWriterX86_64.WriteExecutable(text, rodata, 0);
    }

    // ── Function emission ────────────────────────────────────────

    void EmitFunction(IRDefinition def)
    {
        m_functionOffsets[def.Name] = m_text.Count;
        m_nextTemp = 0;
        m_nextLocal = 0;
        m_spillCount = 0;
        m_loadLocalToggle = 0;
        m_locals = [];

        // Prologue: push rbp; mov rbp, rsp; push callee-saved; sub rsp, <spillFrame>
        // Callee-saved pushes come BEFORE sub rsp so spill offsets (below callee
        // saves, relative to rbp) don't collide with the saved registers.
        X86_64Encoder.PushR(m_text, Reg.RBP);
        X86_64Encoder.MovRR(m_text, Reg.RBP, Reg.RSP);

        // Save callee-saved registers (immediately after rbp)
        foreach (byte reg in LocalRegs)
            X86_64Encoder.PushR(m_text, reg);

        int frameSizePatchOffset = m_text.Count;
        // Always use imm32 encoding for sub rsp so patch works for any frame size
        EmitSubRspImm32(0); // placeholder — patched after body emission

        // Bind parameters
        for (int i = 0; i < def.Parameters.Length; i++)
        {
            if (i < Reg.ArgRegs.Length)
            {
                byte local = AllocLocal();
                StoreLocal(local, Reg.ArgRegs[i]);
                m_locals[def.Parameters[i].Name] = local;
            }
        }

        // Emit body
        byte result = EmitExpr(def.Body);

        // Move result to RAX
        if (result != Reg.RAX)
            X86_64Encoder.MovRR(m_text, Reg.RAX, result);

        // Epilogue: skip spill space, restore callee-saved, pop rbp, ret
        // lea rsp, [rbp - 40] points rsp at saved r15 (5 callee-saved × 8 bytes)
        X86_64Encoder.Lea(m_text, Reg.RSP, Reg.RBP, -LocalRegs.Length * 8);
        for (int i = LocalRegs.Length - 1; i >= 0; i--)
            X86_64Encoder.PopR(m_text, LocalRegs[i]);

        X86_64Encoder.PopR(m_text, Reg.RBP);
        X86_64Encoder.Ret(m_text);

        // Patch frame size — space for spill slots
        int frameSize = m_spillCount * 8;
        frameSize = (frameSize + 15) & ~15; // 16-byte align
        // Patch the imm32 in the sub rsp instruction (REX 81 EC <imm32>)
        // imm32 starts at frameSizePatchOffset + 3
        m_text[frameSizePatchOffset + 3] = (byte)(frameSize & 0xFF);
        m_text[frameSizePatchOffset + 4] = (byte)((frameSize >> 8) & 0xFF);
        m_text[frameSizePatchOffset + 5] = (byte)((frameSize >> 16) & 0xFF);
        m_text[frameSizePatchOffset + 6] = (byte)((frameSize >> 24) & 0xFF);
    }

    byte EmitExpr(IRExpr expr) => expr switch
    {
        IRIntegerLit intLit => EmitIntegerLit(intLit.Value),
        IRBoolLit boolLit => EmitIntegerLit(boolLit.Value ? 1 : 0),
        IRTextLit textLit => EmitTextLit(textLit.Value),
        IRName name => EmitName(name),
        IRBinary bin => EmitBinary(bin),
        IRIf ifExpr => EmitIf(ifExpr),
        IRLet letExpr => EmitLet(letExpr),
        IRApply apply => EmitApply(apply),
        IRNegate neg => EmitNegate(neg),
        IRDo doExpr => EmitDo(doExpr),
        IRRecord rec => EmitRecord(rec),
        IRFieldAccess fa => EmitFieldAccess(fa),
        IRMatch match => EmitMatch(match),
        IRList list => EmitList(list),
        IRError err => EmitError(err),
        IRRegion region => EmitRegion(region),
        _ => EmitUnhandled(expr)
    };

    // ── Literals ─────────────────────────────────────────────────

    byte EmitIntegerLit(long value)
    {
        byte rd = AllocTemp();
        X86_64Encoder.Li(m_text, rd, value);
        return rd;
    }

    byte EmitTextLit(string value)
    {
        if (!m_stringOffsets.TryGetValue(value, out int rodataOffset))
        {
            rodataOffset = m_rodata.Count;
            byte[] utf8 = Encoding.UTF8.GetBytes(value);
            // Length-prefixed: 8-byte i64 length + UTF-8 data, 8-byte aligned
            m_rodata.AddRange(BitConverter.GetBytes((long)utf8.Length));
            m_rodata.AddRange(utf8);
            while (m_rodata.Count % 8 != 0) m_rodata.Add(0);
            m_stringOffsets[value] = rodataOffset;
        }

        byte rd = AllocTemp();
        // Emit: movabs rd, <rodata_vaddr + offset> (patched later)
        m_rodataFixups.Add(new RodataFixup(m_text.Count + 2, rodataOffset));
        X86_64Encoder.MovRI64(m_text, rd, 0); // placeholder — patched by PatchRodataRefs
        return rd;
    }

    byte EmitName(IRName name)
    {
        if (m_locals.TryGetValue(name.Name, out byte local))
            return LoadLocal(local);

        // Try zero-arg builtins
        byte builtinResult = TryEmitBuiltin(name.Name, []);
        if (builtinResult != byte.MaxValue)
            return builtinResult;

        // Function reference — return as closure/pointer
        // For now, just return 0 for unknown names
        byte rd = AllocTemp();
        X86_64Encoder.Li(m_text, rd, 0);
        return rd;
    }

    // ── Binary operations ────────────────────────────────────────

    byte EmitBinary(IRBinary bin)
    {
        byte left = EmitExpr(bin.Left);
        byte savedLeft = AllocLocal();
        StoreLocal(savedLeft, left);

        byte right = EmitExpr(bin.Right);
        byte savedRight = AllocLocal();
        StoreLocal(savedRight, right);

        byte lReg = LoadLocal(savedLeft);
        byte rReg = LoadLocal(savedRight);

        byte rd = AllocTemp();

        switch (bin.Op)
        {
            case IRBinaryOp.AddInt:
                X86_64Encoder.MovRR(m_text, rd, lReg);
                X86_64Encoder.AddRR(m_text, rd, rReg);
                break;
            case IRBinaryOp.SubInt:
                X86_64Encoder.MovRR(m_text, rd, lReg);
                X86_64Encoder.SubRR(m_text, rd, rReg);
                break;
            case IRBinaryOp.MulInt:
                X86_64Encoder.MovRR(m_text, rd, lReg);
                X86_64Encoder.ImulRR(m_text, rd, rReg);
                break;
            case IRBinaryOp.DivInt:
                X86_64Encoder.MovRR(m_text, Reg.RAX, lReg);
                X86_64Encoder.Cqo(m_text);
                X86_64Encoder.IdivR(m_text, rReg);
                X86_64Encoder.MovRR(m_text, rd, Reg.RAX);
                break;
            case IRBinaryOp.PowInt:
                // TODO: power — stub as 0
                X86_64Encoder.Li(m_text, rd, 0);
                break;
            case IRBinaryOp.Eq:
                return EmitComparison(X86_64Encoder.CC_E, lReg, rReg, bin.Left.Type);
            case IRBinaryOp.NotEq:
                return EmitComparison(X86_64Encoder.CC_NE, lReg, rReg, bin.Left.Type);
            case IRBinaryOp.Lt:
                return EmitComparison(X86_64Encoder.CC_L, lReg, rReg, bin.Left.Type);
            case IRBinaryOp.Gt:
                return EmitComparison(X86_64Encoder.CC_G, lReg, rReg, bin.Left.Type);
            case IRBinaryOp.LtEq:
                return EmitComparison(X86_64Encoder.CC_LE, lReg, rReg, bin.Left.Type);
            case IRBinaryOp.GtEq:
                return EmitComparison(X86_64Encoder.CC_GE, lReg, rReg, bin.Left.Type);
            case IRBinaryOp.AppendText:
                X86_64Encoder.MovRR(m_text, Reg.RDI, lReg);
                X86_64Encoder.MovRR(m_text, Reg.RSI, rReg);
                EmitCallTo("__str_concat");
                byte concatResult = AllocTemp();
                X86_64Encoder.MovRR(m_text, concatResult, Reg.RAX);
                return concatResult;
            default:
                X86_64Encoder.Li(m_text, rd, 0);
                break;
        }

        return rd;
    }

    byte EmitComparison(byte cc, byte lReg, byte rReg, CodexType operandType)
    {
        if (operandType is TextType && (cc == X86_64Encoder.CC_E || cc == X86_64Encoder.CC_NE))
        {
            X86_64Encoder.MovRR(m_text, Reg.RDI, lReg);
            X86_64Encoder.MovRR(m_text, Reg.RSI, rReg);
            EmitCallTo("__str_eq");
            byte rd = AllocTemp();
            if (cc == X86_64Encoder.CC_NE)
            {
                X86_64Encoder.CmpRI(m_text, Reg.RAX, 0);
                X86_64Encoder.Setcc(m_text, X86_64Encoder.CC_E, rd);
                X86_64Encoder.MovzxByteSelf(m_text, rd);
            }
            else
            {
                X86_64Encoder.MovRR(m_text, rd, Reg.RAX);
            }
            return rd;
        }

        X86_64Encoder.CmpRR(m_text, lReg, rReg);
        byte result = AllocTemp();
        X86_64Encoder.Setcc(m_text, cc, result);
        X86_64Encoder.MovzxByteSelf(m_text, result);
        return result;
    }

    // ── Control flow ─────────────────────────────────────────────

    byte EmitIf(IRIf ifExpr)
    {
        byte cond = EmitExpr(ifExpr.Condition);
        X86_64Encoder.TestRR(m_text, cond, cond);

        // je else_branch
        int jeFalseOffset = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_E, 0); // patched

        // Then branch
        byte thenResult = EmitExpr(ifExpr.Then);
        byte resultLocal = AllocLocal();
        StoreLocal(resultLocal, thenResult);

        // jmp end
        int jmpEndOffset = m_text.Count;
        X86_64Encoder.Jmp(m_text, 0); // patched

        // Else branch
        int elseStart = m_text.Count;
        PatchJcc(jeFalseOffset, elseStart);

        byte elseResult = EmitExpr(ifExpr.Else);
        StoreLocal(resultLocal, elseResult);

        int endOffset = m_text.Count;
        PatchJmp(jmpEndOffset, endOffset);

        return LoadLocal(resultLocal);
    }

    byte EmitLet(IRLet letExpr)
    {
        byte value = EmitExpr(letExpr.Value);
        byte local = AllocLocal();
        StoreLocal(local, value);
        m_locals[letExpr.Name] = local;
        return EmitExpr(letExpr.Body);
    }

    byte EmitDo(IRDo doExpr)
    {
        byte lastReg = AllocTemp();
        X86_64Encoder.Li(m_text, lastReg, 0);
        foreach (IRDoStatement stmt in doExpr.Statements)
        {
            switch (stmt)
            {
                case IRDoExec exec:
                    lastReg = EmitExpr(exec.Expression);
                    break;
                case IRDoBind bind:
                    byte valReg = EmitExpr(bind.Value);
                    byte savedReg = AllocLocal();
                    StoreLocal(savedReg, valReg);
                    m_locals[bind.Name] = savedReg;
                    break;
            }
        }
        return lastReg;
    }

    // ── Function calls ───────────────────────────────────────────

    byte EmitApply(IRApply apply)
    {
        // Collect function name and all arguments
        string? funcName = null;
        List<IRExpr> args = [apply.Argument];
        IRExpr func = apply.Function;
        while (func is IRApply inner)
        {
            args.Insert(0, inner.Argument);
            func = inner.Function;
        }
        if (func is IRName name)
            funcName = name.Name;

        // Try builtins first
        if (funcName is not null)
        {
            byte builtinResult = TryEmitBuiltin(funcName, args);
            if (builtinResult != byte.MaxValue)
                return builtinResult;
        }

        // Regular function call — evaluate args, place in arg registers
        List<byte> argLocals = [];
        foreach (IRExpr arg in args)
        {
            byte r = EmitExpr(arg);
            byte saved = AllocLocal();
            StoreLocal(saved, r);
            argLocals.Add(saved);
        }

        for (int i = 0; i < argLocals.Count && i < Reg.ArgRegs.Length; i++)
        {
            byte loaded = LoadLocal(argLocals[i]);
            X86_64Encoder.MovRR(m_text, Reg.ArgRegs[i], loaded);
        }

        if (funcName is not null)
            EmitCallTo(funcName);

        byte result = AllocTemp();
        X86_64Encoder.MovRR(m_text, result, Reg.RAX);
        return result;
    }

    byte EmitNegate(IRNegate neg)
    {
        byte operand = EmitExpr(neg.Operand);
        byte rd = AllocTemp();
        X86_64Encoder.MovRR(m_text, rd, operand);
        X86_64Encoder.NegR(m_text, rd);
        return rd;
    }

    // ── Records ──────────────────────────────────────────────────

    byte EmitRecord(IRRecord rec)
    {
        Dictionary<string, byte> fieldMap = [];
        foreach ((string name, IRExpr value) in rec.Fields)
        {
            byte r = EmitExpr(value);
            byte saved = AllocLocal();
            StoreLocal(saved, r);
            fieldMap[name] = saved;
        }

        int totalSize = rec.Fields.Length * 8;
        byte ptrReg = AllocTemp();
        X86_64Encoder.MovRR(m_text, ptrReg, HeapReg);
        X86_64Encoder.AddRI(m_text, HeapReg, totalSize);

        if (rec.Type is RecordType rt)
        {
            for (int i = 0; i < rt.Fields.Length; i++)
            {
                string fieldName = rt.Fields[i].FieldName.Value;
                if (fieldMap.TryGetValue(fieldName, out byte saved))
                {
                    byte val = LoadLocal(saved);
                    X86_64Encoder.MovStore(m_text, ptrReg, val, i * 8);
                }
            }
        }

        return ptrReg;
    }

    byte EmitFieldAccess(IRFieldAccess fa)
    {
        byte baseReg = EmitExpr(fa.Record);
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

        byte rd = AllocTemp();
        X86_64Encoder.MovLoad(m_text, rd, baseReg, fieldIndex * 8);
        return rd;
    }

    // ── Pattern matching ─────────────────────────────────────────

    byte EmitMatch(IRMatch match)
    {
        byte scrutReg = EmitExpr(match.Scrutinee);
        byte savedScrut = AllocLocal();
        StoreLocal(savedScrut, scrutReg);

        byte resultLocal = AllocLocal();

        List<int> jumpToEndOffsets = [];

        for (int i = 0; i < match.Branches.Length; i++)
        {
            IRMatchBranch branch = match.Branches[i];
            int nextBranchPatch = -1;

            switch (branch.Pattern)
            {
                case IRWildcardPattern:
                {
                    byte bodyResult = EmitExpr(branch.Body);
                    StoreLocal(resultLocal, bodyResult);
                    break;
                }
                case IRVarPattern varPat:
                {
                    byte local = AllocLocal();
                    StoreLocal(local, LoadLocal(savedScrut));
                    m_locals[varPat.Name] = local;
                    byte bodyResult = EmitExpr(branch.Body);
                    StoreLocal(resultLocal, bodyResult);
                    break;
                }
                case IRLiteralPattern litPat:
                {
                    byte scrut = LoadLocal(savedScrut);
                    X86_64Encoder.CmpRI(m_text, scrut, Convert.ToInt32(litPat.Value));
                    nextBranchPatch = m_text.Count;
                    X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_NE, 0);

                    byte bodyResult = EmitExpr(branch.Body);
                    StoreLocal(resultLocal, bodyResult);
                    break;
                }
                case IRCtorPattern ctorPat:
                {
                    // Resolve tag from SumType
                    int expectedTag = 0;
                    if (ctorPat.Type is SumType sumType)
                    {
                        for (int t = 0; t < sumType.Constructors.Length; t++)
                        {
                            if (sumType.Constructors[t].Name.Value == ctorPat.Name)
                            {
                                expectedTag = t;
                                break;
                            }
                        }
                    }

                    byte scrut = LoadLocal(savedScrut);
                    byte tagReg = AllocTemp();
                    X86_64Encoder.MovLoad(m_text, tagReg, scrut, 0);

                    X86_64Encoder.CmpRI(m_text, tagReg, expectedTag);
                    nextBranchPatch = m_text.Count;
                    X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_NE, 0);

                    // Bind constructor fields
                    for (int f = 0; f < ctorPat.SubPatterns.Length; f++)
                    {
                        if (ctorPat.SubPatterns[f] is IRVarPattern fieldVar)
                        {
                            byte fieldLocal = AllocLocal();
                            byte scrutReload = LoadLocal(savedScrut);
                            byte fieldVal = AllocTemp();
                            X86_64Encoder.MovLoad(m_text, fieldVal, scrutReload, (1 + f) * 8);
                            StoreLocal(fieldLocal, fieldVal);
                            m_locals[fieldVar.Name] = fieldLocal;
                        }
                    }

                    byte bodyResult = EmitExpr(branch.Body);
                    StoreLocal(resultLocal, bodyResult);
                    break;
                }
            }

            if (i < match.Branches.Length - 1)
            {
                jumpToEndOffsets.Add(m_text.Count);
                X86_64Encoder.Jmp(m_text, 0);
            }

            if (nextBranchPatch >= 0)
                PatchJcc(nextBranchPatch, m_text.Count);
        }

        int endOffset = m_text.Count;
        foreach (int patchOffset in jumpToEndOffsets)
            PatchJmp(patchOffset, endOffset);

        return LoadLocal(resultLocal);
    }

    // ── Lists ────────────────────────────────────────────────────

    byte EmitList(IRList list)
    {
        List<byte> elemLocals = [];
        foreach (IRExpr elem in list.Elements)
        {
            byte r = EmitExpr(elem);
            byte saved = AllocLocal();
            StoreLocal(saved, r);
            elemLocals.Add(saved);
        }

        int totalSize = (1 + list.Elements.Length) * 8;
        byte ptrReg = AllocTemp();
        X86_64Encoder.MovRR(m_text, ptrReg, HeapReg);
        X86_64Encoder.AddRI(m_text, HeapReg, totalSize);

        // Store length
        byte lenReg = AllocTemp();
        X86_64Encoder.Li(m_text, lenReg, list.Elements.Length);
        X86_64Encoder.MovStore(m_text, ptrReg, lenReg, 0);

        // Store elements
        for (int i = 0; i < elemLocals.Count; i++)
        {
            byte val = LoadLocal(elemLocals[i]);
            X86_64Encoder.MovStore(m_text, ptrReg, val, 8 + i * 8);
        }

        return ptrReg;
    }

    // ── Regions ──────────────────────────────────────────────────

    byte EmitRegion(IRRegion region)
    {
        if (region.Type is FunctionType)
            return EmitExpr(region.Body);

        byte savedHeap = AllocLocal();
        StoreLocal(savedHeap, HeapReg);

        byte bodyResult = EmitExpr(region.Body);

        if (!region.NeedsEscapeCopy)
        {
            byte restored = LoadLocal(savedHeap);
            X86_64Encoder.MovRR(m_text, HeapReg, restored);
            return bodyResult;
        }

        byte savedResult = AllocLocal();
        StoreLocal(savedResult, bodyResult);

        byte heapRestored = LoadLocal(savedHeap);
        X86_64Encoder.MovRR(m_text, HeapReg, heapRestored);

        return EmitEscapeCopy(savedResult, region.Type);
    }

    byte EmitEscapeCopy(byte srcLocal, CodexType type)
    {
        CodexType resolved = ResolveType(type);
        if (!IRRegion.TypeNeedsHeapEscape(resolved))
            return LoadLocal(srcLocal);

        string helperName = GetOrQueueEscapeHelper(resolved);
        byte src = LoadLocal(srcLocal);
        X86_64Encoder.MovRR(m_text, Reg.RDI, src);
        EmitCallTo(helperName);
        byte result = AllocTemp();
        X86_64Encoder.MovRR(m_text, result, Reg.RAX);
        return result;
    }

    // ── Error / unhandled ────────────────────────────────────────

    byte EmitError(IRError err)
    {
        byte rd = AllocTemp();
        X86_64Encoder.Li(m_text, rd, 0);
        return rd;
    }

    byte EmitUnhandled(IRExpr expr)
    {
        byte rd = AllocTemp();
        X86_64Encoder.Li(m_text, rd, 0);
        return rd;
    }

    // ── Builtins ─────────────────────────────────────────────────

    byte TryEmitBuiltin(string name, List<IRExpr> args)
    {
        switch (name)
        {
            case "print-line":
                return EmitPrintLine(args);
            case "text-length":
                if (args.Count >= 1)
                {
                    byte ptr = EmitExpr(args[0]);
                    byte rd = AllocTemp();
                    X86_64Encoder.MovLoad(m_text, rd, ptr, 0);
                    return rd;
                }
                return byte.MaxValue;
            case "integer-to-text":
                if (args.Count >= 1)
                {
                    byte val = EmitExpr(args[0]);
                    X86_64Encoder.MovRR(m_text, Reg.RDI, val);
                    EmitCallTo("__itoa");
                    byte rd = AllocTemp();
                    X86_64Encoder.MovRR(m_text, rd, Reg.RAX);
                    return rd;
                }
                return byte.MaxValue;
            case "text-to-integer":
                if (args.Count >= 1)
                {
                    byte ptr = EmitExpr(args[0]);
                    X86_64Encoder.MovRR(m_text, Reg.RDI, ptr);
                    EmitCallTo("__text_to_int");
                    byte rd = AllocTemp();
                    X86_64Encoder.MovRR(m_text, rd, Reg.RAX);
                    return rd;
                }
                return byte.MaxValue;
            case "read-file":
                if (args.Count >= 1)
                {
                    byte path = EmitExpr(args[0]);
                    X86_64Encoder.MovRR(m_text, Reg.RDI, path);
                    EmitCallTo("__read_file");
                    byte rd = AllocTemp();
                    X86_64Encoder.MovRR(m_text, rd, Reg.RAX);
                    return rd;
                }
                return byte.MaxValue;
            case "read-line" when args.Count == 0:
            {
                EmitCallTo("__read_line");
                byte rd = AllocTemp();
                X86_64Encoder.MovRR(m_text, rd, Reg.RAX);
                return rd;
            }
            case "char-at":
                if (args.Count >= 2)
                {
                    byte str = EmitExpr(args[0]);
                    byte savedStr = AllocLocal();
                    StoreLocal(savedStr, str);
                    byte idx = EmitExpr(args[1]);
                    byte strLoaded = LoadLocal(savedStr);
                    // Result = str[8 + idx] as a 1-char string on heap
                    X86_64Encoder.AddRR(m_text, idx, strLoaded);
                    X86_64Encoder.MovzxByte(m_text, idx, idx, 8);
                    // Allocate 1-char string on heap: [len=1][byte]
                    byte rd = AllocTemp();
                    X86_64Encoder.MovRR(m_text, rd, HeapReg);
                    X86_64Encoder.AddRI(m_text, HeapReg, 16); // 8 len + 1 byte + padding
                    X86_64Encoder.Li(m_text, Reg.R11, 1);
                    X86_64Encoder.MovStore(m_text, rd, Reg.R11, 0);
                    X86_64Encoder.MovStoreByte(m_text, rd, idx, 8);
                    return rd;
                }
                return byte.MaxValue;
            case "substring":
                if (args.Count >= 3)
                {
                    byte str = EmitExpr(args[0]);
                    byte savedStr = AllocLocal();
                    StoreLocal(savedStr, str);
                    byte start = EmitExpr(args[1]);
                    byte savedStart = AllocLocal();
                    StoreLocal(savedStart, start);
                    byte len = EmitExpr(args[2]);
                    byte savedLen = AllocLocal();
                    StoreLocal(savedLen, len);
                    // Allocate result: [len][bytes]
                    byte rd = AllocTemp();
                    X86_64Encoder.MovRR(m_text, rd, HeapReg);
                    byte lenLoaded = LoadLocal(savedLen);
                    X86_64Encoder.MovRR(m_text, Reg.R11, lenLoaded);
                    X86_64Encoder.AddRI(m_text, Reg.R11, 15);
                    X86_64Encoder.AndRI(m_text, Reg.R11, -8);
                    X86_64Encoder.AddRR(m_text, HeapReg, Reg.R11);
                    lenLoaded = LoadLocal(savedLen);
                    X86_64Encoder.MovStore(m_text, rd, lenLoaded, 0);
                    // Copy bytes
                    X86_64Encoder.Li(m_text, Reg.R11, 0);
                    int subLoop = m_text.Count;
                    byte lenCmp = LoadLocal(savedLen);
                    X86_64Encoder.CmpRR(m_text, Reg.R11, lenCmp);
                    int subExit = m_text.Count;
                    X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_GE, 0);
                    byte srcBase = LoadLocal(savedStr);
                    byte startOff = LoadLocal(savedStart);
                    X86_64Encoder.MovRR(m_text, Reg.RSI, srcBase);
                    X86_64Encoder.AddRR(m_text, Reg.RSI, startOff);
                    X86_64Encoder.AddRR(m_text, Reg.RSI, Reg.R11);
                    X86_64Encoder.MovzxByte(m_text, Reg.RSI, Reg.RSI, 8);
                    X86_64Encoder.MovRR(m_text, Reg.RDX, rd);
                    X86_64Encoder.AddRR(m_text, Reg.RDX, Reg.R11);
                    X86_64Encoder.MovStoreByte(m_text, Reg.RDX, Reg.RSI, 8);
                    X86_64Encoder.AddRI(m_text, Reg.R11, 1);
                    X86_64Encoder.Jmp(m_text, subLoop - (m_text.Count + 5));
                    PatchJcc(subExit, m_text.Count);
                    return rd;
                }
                return byte.MaxValue;
            case "list-cons":
                if (args.Count >= 2)
                {
                    byte head = EmitExpr(args[0]);
                    byte savedHead = AllocLocal();
                    StoreLocal(savedHead, head);
                    byte tail = EmitExpr(args[1]);
                    X86_64Encoder.MovRR(m_text, Reg.RSI, tail);
                    X86_64Encoder.MovRR(m_text, Reg.RDI, LoadLocal(savedHead));
                    EmitCallTo("__list_cons");
                    byte rd = AllocTemp();
                    X86_64Encoder.MovRR(m_text, rd, Reg.RAX);
                    return rd;
                }
                return byte.MaxValue;
            case "list-append":
                if (args.Count >= 2)
                {
                    byte l1 = EmitExpr(args[0]);
                    byte savedL1 = AllocLocal();
                    StoreLocal(savedL1, l1);
                    byte l2 = EmitExpr(args[1]);
                    X86_64Encoder.MovRR(m_text, Reg.RSI, l2);
                    X86_64Encoder.MovRR(m_text, Reg.RDI, LoadLocal(savedL1));
                    EmitCallTo("__list_append");
                    byte rd = AllocTemp();
                    X86_64Encoder.MovRR(m_text, rd, Reg.RAX);
                    return rd;
                }
                return byte.MaxValue;
            case "list-at":
                if (args.Count >= 2)
                {
                    byte list = EmitExpr(args[0]);
                    byte savedList = AllocLocal();
                    StoreLocal(savedList, list);
                    byte idx = EmitExpr(args[1]);
                    byte listLoaded = LoadLocal(savedList);
                    // elem = list[8 + idx*8]
                    X86_64Encoder.ShlRI(m_text, idx, 3);
                    X86_64Encoder.AddRR(m_text, idx, listLoaded);
                    byte rd = AllocTemp();
                    X86_64Encoder.MovLoad(m_text, rd, idx, 8);
                    return rd;
                }
                return byte.MaxValue;
            case "list-length":
                if (args.Count >= 1)
                {
                    byte list = EmitExpr(args[0]);
                    byte rd = AllocTemp();
                    X86_64Encoder.MovLoad(m_text, rd, list, 0);
                    return rd;
                }
                return byte.MaxValue;
            default:
                return byte.MaxValue;
        }
    }

    byte EmitPrintLine(List<IRExpr> args)
    {
        if (args.Count < 1)
        {
            byte empty = AllocTemp();
            X86_64Encoder.Li(m_text, empty, 0);
            return empty;
        }

        byte value = EmitExpr(args[0]);
        CodexType argType = args[0].Type;

        if (argType is IntegerType)
        {
            // Convert integer to text first
            X86_64Encoder.MovRR(m_text, Reg.RDI, value);
            EmitCallTo("__itoa");
            EmitPrintText(Reg.RAX);
        }
        else if (argType is BooleanType)
        {
            EmitPrintBool(value);
        }
        else
        {
            // Assume text pointer
            EmitPrintText(value);
        }

        byte rd = AllocTemp();
        X86_64Encoder.Li(m_text, rd, 0);
        return rd;
    }

    void EmitPrintText(byte ptrReg)
    {
        // Linux x86-64 syscall write: rax=1, rdi=1(stdout), rsi=buf, rdx=len
        byte savedPtr = AllocLocal();
        StoreLocal(savedPtr, ptrReg);

        byte ptr = LoadLocal(savedPtr);
        // Load data pointer BEFORE length (ptr might alias RDX)
        X86_64Encoder.Lea(m_text, Reg.RSI, ptr, 8);
        X86_64Encoder.MovLoad(m_text, Reg.RDX, ptr, 0);
        X86_64Encoder.Li(m_text, Reg.RAX, 1); // sys_write
        X86_64Encoder.Li(m_text, Reg.RDI, 1); // stdout
        X86_64Encoder.Syscall(m_text);

        // Print newline
        EmitPrintNewline();
    }

    void EmitPrintBool(byte valueReg)
    {
        // TODO: emit "True"/"False" string
        byte savedVal = AllocLocal();
        StoreLocal(savedVal, valueReg);

        int trueOffset = AddRodataString("True");
        int falseOffset = AddRodataString("False");

        byte val = LoadLocal(savedVal);
        X86_64Encoder.TestRR(m_text, val, val);
        int jeFalse = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_E, 0);

        // True path
        byte trueReg = AllocTemp();
        EmitLoadRodataAddress(trueReg, trueOffset);
        EmitPrintTextNoNewline(trueReg);
        int jmpEnd = m_text.Count;
        X86_64Encoder.Jmp(m_text, 0);

        // False path
        int falseStart = m_text.Count;
        PatchJcc(jeFalse, falseStart);
        byte falseReg = AllocTemp();
        EmitLoadRodataAddress(falseReg, falseOffset);
        EmitPrintTextNoNewline(falseReg);

        int endPos = m_text.Count;
        PatchJmp(jmpEnd, endPos);

        EmitPrintNewline();
    }

    void EmitPrintTextNoNewline(byte ptrReg)
    {
        X86_64Encoder.Lea(m_text, Reg.RSI, ptrReg, 8);
        X86_64Encoder.MovLoad(m_text, Reg.RDX, ptrReg, 0);
        X86_64Encoder.Li(m_text, Reg.RAX, 1);
        X86_64Encoder.Li(m_text, Reg.RDI, 1);
        X86_64Encoder.Syscall(m_text);
    }

    void EmitPrintNewline()
    {
        int nlOffset = AddRodataString("\n");
        byte nlReg = AllocTemp();
        EmitLoadRodataAddress(nlReg, nlOffset);
        X86_64Encoder.Lea(m_text, Reg.RSI, nlReg, 8);
        X86_64Encoder.MovLoad(m_text, Reg.RDX, nlReg, 0);
        X86_64Encoder.Li(m_text, Reg.RAX, 1);
        X86_64Encoder.Li(m_text, Reg.RDI, 1);
        X86_64Encoder.Syscall(m_text);
    }

    // ── Runtime helpers ──────────────────────────────────────────

    void EmitRuntimeHelpers()
    {
        EmitStrConcatHelper();
        EmitStrEqHelper();
        EmitItoaHelper();
        EmitTextToIntHelper();
        EmitReadFileHelper();
        EmitReadLineHelper();
        EmitListConsHelper();
        EmitListAppendHelper();
        EmitEscapeTextHelper();
    }

    void EmitStrConcatHelper()
    {
        // __str_concat: rdi=ptr1, rsi=ptr2 → rax=new string on heap
        m_functionOffsets["__str_concat"] = m_text.Count;

        X86_64Encoder.PushR(m_text, Reg.RBX);
        X86_64Encoder.PushR(m_text, Reg.R12);
        X86_64Encoder.PushR(m_text, Reg.R13);

        X86_64Encoder.MovRR(m_text, Reg.RBX, Reg.RDI);   // rbx = ptr1
        X86_64Encoder.MovRR(m_text, Reg.R12, Reg.RSI);    // r12 = ptr2
        X86_64Encoder.MovLoad(m_text, Reg.RCX, Reg.RBX, 0); // rcx = len1
        X86_64Encoder.MovLoad(m_text, Reg.RDX, Reg.R12, 0); // rdx = len2
        X86_64Encoder.MovRR(m_text, Reg.R13, Reg.RCX);
        X86_64Encoder.AddRR(m_text, Reg.R13, Reg.RDX);    // r13 = total_len

        // Allocate: rax = HeapReg; HeapReg += align8(8 + total)
        X86_64Encoder.MovRR(m_text, Reg.RAX, HeapReg);
        X86_64Encoder.MovRR(m_text, Reg.R11, Reg.R13);
        X86_64Encoder.AddRI(m_text, Reg.R11, 15);
        X86_64Encoder.AndRI(m_text, Reg.R11, -8);
        X86_64Encoder.AddRR(m_text, HeapReg, Reg.R11);
        X86_64Encoder.MovStore(m_text, Reg.RAX, Reg.R13, 0); // store total length

        // Copy first string: i=0; while i<len1: dst[8+i]=src1[8+i]; i++
        X86_64Encoder.MovLoad(m_text, Reg.RCX, Reg.RBX, 0);
        X86_64Encoder.Li(m_text, Reg.R11, 0);
        int loop1 = m_text.Count;
        X86_64Encoder.CmpRR(m_text, Reg.R11, Reg.RCX);
        int exit1 = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_GE, 0);
        X86_64Encoder.MovRR(m_text, Reg.RDX, Reg.RBX);
        X86_64Encoder.AddRR(m_text, Reg.RDX, Reg.R11);
        X86_64Encoder.MovzxByte(m_text, Reg.RDX, Reg.RDX, 8);
        X86_64Encoder.MovRR(m_text, Reg.RSI, Reg.RAX);
        X86_64Encoder.AddRR(m_text, Reg.RSI, Reg.R11);
        X86_64Encoder.MovStoreByte(m_text, Reg.RSI, Reg.RDX, 8);
        X86_64Encoder.AddRI(m_text, Reg.R11, 1);
        X86_64Encoder.Jmp(m_text, loop1 - (m_text.Count + 5));
        PatchJcc(exit1, m_text.Count);

        // Copy second string: i=0; while i<len2: dst[8+len1+i]=src2[8+i]; i++
        X86_64Encoder.MovLoad(m_text, Reg.RCX, Reg.RBX, 0); // len1
        X86_64Encoder.MovLoad(m_text, Reg.RDX, Reg.R12, 0); // len2
        X86_64Encoder.Li(m_text, Reg.R11, 0);
        int loop2 = m_text.Count;
        X86_64Encoder.CmpRR(m_text, Reg.R11, Reg.RDX);
        int exit2 = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_GE, 0);
        X86_64Encoder.MovRR(m_text, Reg.RSI, Reg.R12);
        X86_64Encoder.AddRR(m_text, Reg.RSI, Reg.R11);
        X86_64Encoder.MovzxByte(m_text, Reg.RSI, Reg.RSI, 8);
        X86_64Encoder.MovRR(m_text, Reg.RDI, Reg.RAX);
        X86_64Encoder.AddRR(m_text, Reg.RDI, Reg.RCX);
        X86_64Encoder.AddRR(m_text, Reg.RDI, Reg.R11);
        X86_64Encoder.MovStoreByte(m_text, Reg.RDI, Reg.RSI, 8);
        X86_64Encoder.AddRI(m_text, Reg.R11, 1);
        X86_64Encoder.Jmp(m_text, loop2 - (m_text.Count + 5));
        PatchJcc(exit2, m_text.Count);

        X86_64Encoder.PopR(m_text, Reg.R13);
        X86_64Encoder.PopR(m_text, Reg.R12);
        X86_64Encoder.PopR(m_text, Reg.RBX);
        X86_64Encoder.Ret(m_text);
    }

    void EmitStrEqHelper()
    {
        // __str_eq: rdi=ptr1, rsi=ptr2 → rax=1 if equal, 0 if not
        m_functionOffsets["__str_eq"] = m_text.Count;

        X86_64Encoder.CmpRR(m_text, Reg.RDI, Reg.RSI);
        int notSame = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_NE, 0);
        X86_64Encoder.Li(m_text, Reg.RAX, 1);
        X86_64Encoder.Ret(m_text);
        PatchJcc(notSame, m_text.Count);

        X86_64Encoder.MovLoad(m_text, Reg.RCX, Reg.RDI, 0);
        X86_64Encoder.MovLoad(m_text, Reg.RDX, Reg.RSI, 0);
        X86_64Encoder.CmpRR(m_text, Reg.RCX, Reg.RDX);
        int lenNe = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_NE, 0);

        X86_64Encoder.Li(m_text, Reg.R11, 0);
        int loop = m_text.Count;
        X86_64Encoder.CmpRR(m_text, Reg.R11, Reg.RCX);
        int loopDone = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_GE, 0);
        X86_64Encoder.MovRR(m_text, Reg.RAX, Reg.RDI);
        X86_64Encoder.AddRR(m_text, Reg.RAX, Reg.R11);
        X86_64Encoder.MovzxByte(m_text, Reg.RAX, Reg.RAX, 8);
        X86_64Encoder.MovRR(m_text, Reg.RDX, Reg.RSI);
        X86_64Encoder.AddRR(m_text, Reg.RDX, Reg.R11);
        X86_64Encoder.MovzxByte(m_text, Reg.RDX, Reg.RDX, 8);
        X86_64Encoder.CmpRR(m_text, Reg.RAX, Reg.RDX);
        int byteNe = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_NE, 0);
        X86_64Encoder.AddRI(m_text, Reg.R11, 1);
        X86_64Encoder.Jmp(m_text, loop - (m_text.Count + 5));

        PatchJcc(loopDone, m_text.Count);
        X86_64Encoder.Li(m_text, Reg.RAX, 1);
        X86_64Encoder.Ret(m_text);

        PatchJcc(lenNe, m_text.Count);
        PatchJcc(byteNe, m_text.Count);
        X86_64Encoder.Li(m_text, Reg.RAX, 0);
        X86_64Encoder.Ret(m_text);
    }

    void EmitItoaHelper()
    {
        // __itoa: rdi=integer → rax=ptr to length-prefixed string on heap
        m_functionOffsets["__itoa"] = m_text.Count;

        X86_64Encoder.PushR(m_text, Reg.RBX);
        X86_64Encoder.PushR(m_text, Reg.R12);
        X86_64Encoder.SubRI(m_text, Reg.RSP, 32);

        X86_64Encoder.MovRR(m_text, Reg.RBX, Reg.RDI);
        X86_64Encoder.Li(m_text, Reg.R12, 0);

        // Check negative
        X86_64Encoder.CmpRI(m_text, Reg.RBX, 0);
        int notNeg = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_GE, 0);
        X86_64Encoder.NegR(m_text, Reg.RBX);
        X86_64Encoder.Li(m_text, Reg.R12, 1);
        PatchJcc(notNeg, m_text.Count);

        X86_64Encoder.Li(m_text, Reg.RCX, 0); // digit count
        X86_64Encoder.Li(m_text, Reg.R11, 10); // divisor

        // Handle zero
        X86_64Encoder.TestRR(m_text, Reg.RBX, Reg.RBX);
        int notZero = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_NE, 0);
        X86_64Encoder.Li(m_text, Reg.RSI, '0');
        X86_64Encoder.MovStoreByte(m_text, Reg.RSP, Reg.RSI, 0);
        X86_64Encoder.Li(m_text, Reg.RCX, 1);
        int skipDigits = m_text.Count;
        X86_64Encoder.Jmp(m_text, 0);
        PatchJcc(notZero, m_text.Count);

        // Digit loop
        int digitLoop = m_text.Count;
        X86_64Encoder.TestRR(m_text, Reg.RBX, Reg.RBX);
        int digitDone = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_E, 0);
        X86_64Encoder.MovRR(m_text, Reg.RAX, Reg.RBX);
        X86_64Encoder.Cqo(m_text);
        X86_64Encoder.IdivR(m_text, Reg.R11);
        X86_64Encoder.MovRR(m_text, Reg.RBX, Reg.RAX);
        X86_64Encoder.AddRI(m_text, Reg.RDX, '0');
        X86_64Encoder.MovRR(m_text, Reg.RSI, Reg.RSP);
        X86_64Encoder.AddRR(m_text, Reg.RSI, Reg.RCX);
        X86_64Encoder.MovStoreByte(m_text, Reg.RSI, Reg.RDX, 0);
        X86_64Encoder.AddRI(m_text, Reg.RCX, 1);
        X86_64Encoder.Jmp(m_text, digitLoop - (m_text.Count + 5));
        PatchJcc(digitDone, m_text.Count);
        PatchJmp(skipDigits, m_text.Count);

        // Total length = digits + sign
        X86_64Encoder.MovRR(m_text, Reg.RDX, Reg.RCX);
        X86_64Encoder.AddRR(m_text, Reg.RDX, Reg.R12);

        // Allocate on heap
        X86_64Encoder.MovRR(m_text, Reg.RAX, HeapReg);
        X86_64Encoder.MovRR(m_text, Reg.RSI, Reg.RDX);
        X86_64Encoder.AddRI(m_text, Reg.RSI, 15);
        X86_64Encoder.AndRI(m_text, Reg.RSI, -8);
        X86_64Encoder.AddRR(m_text, HeapReg, Reg.RSI);
        X86_64Encoder.MovStore(m_text, Reg.RAX, Reg.RDX, 0);

        // Write '-' if negative
        X86_64Encoder.Li(m_text, Reg.R11, 0);
        X86_64Encoder.TestRR(m_text, Reg.R12, Reg.R12);
        int noMinus = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_E, 0);
        X86_64Encoder.Li(m_text, Reg.RSI, '-');
        X86_64Encoder.MovStoreByte(m_text, Reg.RAX, Reg.RSI, 8);
        X86_64Encoder.Li(m_text, Reg.R11, 1);
        PatchJcc(noMinus, m_text.Count);

        // Copy digits in reverse
        int copyLoop = m_text.Count;
        X86_64Encoder.TestRR(m_text, Reg.RCX, Reg.RCX);
        int copyDone = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_E, 0);
        X86_64Encoder.SubRI(m_text, Reg.RCX, 1);
        X86_64Encoder.MovRR(m_text, Reg.RSI, Reg.RSP);
        X86_64Encoder.AddRR(m_text, Reg.RSI, Reg.RCX);
        X86_64Encoder.MovzxByte(m_text, Reg.RSI, Reg.RSI, 0);
        X86_64Encoder.MovRR(m_text, Reg.RDX, Reg.RAX);
        X86_64Encoder.AddRR(m_text, Reg.RDX, Reg.R11);
        X86_64Encoder.MovStoreByte(m_text, Reg.RDX, Reg.RSI, 8);
        X86_64Encoder.AddRI(m_text, Reg.R11, 1);
        X86_64Encoder.Jmp(m_text, copyLoop - (m_text.Count + 5));
        PatchJcc(copyDone, m_text.Count);

        X86_64Encoder.AddRI(m_text, Reg.RSP, 32);
        X86_64Encoder.PopR(m_text, Reg.R12);
        X86_64Encoder.PopR(m_text, Reg.RBX);
        X86_64Encoder.Ret(m_text);
    }

    void EmitEscapeTextHelper()
    {
        // __escape_text: rdi=old text ptr → rax=new text ptr
        m_functionOffsets["__escape_text"] = m_text.Count;

        X86_64Encoder.MovLoad(m_text, Reg.RCX, Reg.RDI, 0);
        X86_64Encoder.MovRR(m_text, Reg.RAX, HeapReg);
        X86_64Encoder.MovRR(m_text, Reg.R11, Reg.RCX);
        X86_64Encoder.AddRI(m_text, Reg.R11, 15);
        X86_64Encoder.AndRI(m_text, Reg.R11, -8);
        X86_64Encoder.AddRR(m_text, HeapReg, Reg.R11);
        X86_64Encoder.MovStore(m_text, Reg.RAX, Reg.RCX, 0);

        X86_64Encoder.Li(m_text, Reg.R11, 0);
        int loop = m_text.Count;
        X86_64Encoder.CmpRR(m_text, Reg.R11, Reg.RCX);
        int exit = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_GE, 0);
        X86_64Encoder.MovRR(m_text, Reg.RSI, Reg.RDI);
        X86_64Encoder.AddRR(m_text, Reg.RSI, Reg.R11);
        X86_64Encoder.MovzxByte(m_text, Reg.RSI, Reg.RSI, 8);
        X86_64Encoder.MovRR(m_text, Reg.RDX, Reg.RAX);
        X86_64Encoder.AddRR(m_text, Reg.RDX, Reg.R11);
        X86_64Encoder.MovStoreByte(m_text, Reg.RDX, Reg.RSI, 8);
        X86_64Encoder.AddRI(m_text, Reg.R11, 1);
        X86_64Encoder.Jmp(m_text, loop - (m_text.Count + 5));
        PatchJcc(exit, m_text.Count);

        X86_64Encoder.Ret(m_text);
    }

    void EmitTextToIntHelper()
    {
        // __text_to_int: rdi=text ptr → rax=integer
        m_functionOffsets["__text_to_int"] = m_text.Count;

        X86_64Encoder.MovLoad(m_text, Reg.RCX, Reg.RDI, 0);  // rcx = length
        X86_64Encoder.Lea(m_text, Reg.RDI, Reg.RDI, 8);       // rdi = data
        X86_64Encoder.Li(m_text, Reg.RAX, 0);                  // result
        X86_64Encoder.Li(m_text, Reg.R11, 0);                  // index
        X86_64Encoder.Li(m_text, Reg.RSI, 0);                  // is_negative

        // Check '-'
        X86_64Encoder.TestRR(m_text, Reg.RCX, Reg.RCX);
        int emptyStr = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_E, 0);
        X86_64Encoder.MovzxByte(m_text, Reg.RDX, Reg.RDI, 0);
        X86_64Encoder.CmpRI(m_text, Reg.RDX, '-');
        int notMinus = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_NE, 0);
        X86_64Encoder.Li(m_text, Reg.RSI, 1);
        X86_64Encoder.AddRI(m_text, Reg.R11, 1);
        PatchJcc(notMinus, m_text.Count);

        // Parse loop
        int parseLoop = m_text.Count;
        X86_64Encoder.CmpRR(m_text, Reg.R11, Reg.RCX);
        int parseDone = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_GE, 0);
        X86_64Encoder.MovRR(m_text, Reg.RDX, Reg.RDI);
        X86_64Encoder.AddRR(m_text, Reg.RDX, Reg.R11);
        X86_64Encoder.MovzxByte(m_text, Reg.RDX, Reg.RDX, 0);
        X86_64Encoder.SubRI(m_text, Reg.RDX, '0');
        // rax = rax * 10 + rdx
        X86_64Encoder.PushR(m_text, Reg.RDX);
        X86_64Encoder.MovRR(m_text, Reg.RDX, Reg.RAX);
        X86_64Encoder.ShlRI(m_text, Reg.RAX, 3);  // rax * 8
        X86_64Encoder.AddRR(m_text, Reg.RAX, Reg.RDX);
        X86_64Encoder.AddRR(m_text, Reg.RAX, Reg.RDX); // rax * 10
        X86_64Encoder.PopR(m_text, Reg.RDX);
        X86_64Encoder.AddRR(m_text, Reg.RAX, Reg.RDX);
        X86_64Encoder.AddRI(m_text, Reg.R11, 1);
        X86_64Encoder.Jmp(m_text, parseLoop - (m_text.Count + 5));
        PatchJcc(parseDone, m_text.Count);

        // Negate if needed
        X86_64Encoder.TestRR(m_text, Reg.RSI, Reg.RSI);
        int noNeg = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_E, 0);
        X86_64Encoder.NegR(m_text, Reg.RAX);
        PatchJcc(noNeg, m_text.Count);
        PatchJcc(emptyStr, m_text.Count);
        X86_64Encoder.Ret(m_text);
    }

    void EmitReadFileHelper()
    {
        // __read_file: rdi=path_ptr (length-prefixed) → rax=content_ptr
        // x86-64 Linux syscalls: openat=257, read=0, close=3
        m_functionOffsets["__read_file"] = m_text.Count;

        X86_64Encoder.PushR(m_text, Reg.RBX);
        X86_64Encoder.PushR(m_text, Reg.R12);
        X86_64Encoder.PushR(m_text, Reg.R13);
        X86_64Encoder.PushR(m_text, Reg.R14);

        // Null-terminate path on heap temporarily
        X86_64Encoder.MovLoad(m_text, Reg.RBX, Reg.RDI, 0);  // rbx = path length
        X86_64Encoder.Lea(m_text, Reg.R12, Reg.RDI, 8);       // r12 = path data

        // Copy path bytes to heap, add \0
        X86_64Encoder.MovRR(m_text, Reg.R13, HeapReg);        // r13 = temp path on heap
        X86_64Encoder.Li(m_text, Reg.R11, 0);
        int cpLoop = m_text.Count;
        X86_64Encoder.CmpRR(m_text, Reg.R11, Reg.RBX);
        int cpExit = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_GE, 0);
        X86_64Encoder.MovRR(m_text, Reg.RAX, Reg.R12);
        X86_64Encoder.AddRR(m_text, Reg.RAX, Reg.R11);
        X86_64Encoder.MovzxByte(m_text, Reg.RAX, Reg.RAX, 0);
        X86_64Encoder.MovRR(m_text, Reg.RDX, Reg.R13);
        X86_64Encoder.AddRR(m_text, Reg.RDX, Reg.R11);
        X86_64Encoder.MovStoreByte(m_text, Reg.RDX, Reg.RAX, 0);
        X86_64Encoder.AddRI(m_text, Reg.R11, 1);
        X86_64Encoder.Jmp(m_text, cpLoop - (m_text.Count + 5));
        PatchJcc(cpExit, m_text.Count);
        // Null terminate
        X86_64Encoder.MovRR(m_text, Reg.RAX, Reg.R13);
        X86_64Encoder.AddRR(m_text, Reg.RAX, Reg.RBX);
        X86_64Encoder.Li(m_text, Reg.RDX, 0);
        X86_64Encoder.MovStoreByte(m_text, Reg.RAX, Reg.RDX, 0);

        // openat(AT_FDCWD=-100, path, O_RDONLY=0, 0)
        X86_64Encoder.Li(m_text, Reg.RDI, -100);              // AT_FDCWD
        X86_64Encoder.MovRR(m_text, Reg.RSI, Reg.R13);        // path
        X86_64Encoder.Li(m_text, Reg.RDX, 0);                 // O_RDONLY
        X86_64Encoder.Li(m_text, Reg.RAX, 257);               // SYS_openat
        X86_64Encoder.Syscall(m_text);

        X86_64Encoder.MovRR(m_text, Reg.R14, Reg.RAX);        // r14 = fd

        // Result buffer: after null-terminated path on heap
        X86_64Encoder.MovRR(m_text, Reg.R13, HeapReg);
        X86_64Encoder.AddRR(m_text, Reg.R13, Reg.RBX);
        X86_64Encoder.AddRI(m_text, Reg.R13, 16);             // skip path + align
        X86_64Encoder.Li(m_text, Reg.RBX, 0);                 // rbx = total bytes read

        // Read loop: read(fd, buf, 4096) until 0
        int readLoop = m_text.Count;
        X86_64Encoder.MovRR(m_text, Reg.RDI, Reg.R14);        // fd
        X86_64Encoder.Lea(m_text, Reg.RSI, Reg.R13, 8);       // buf (after length)
        X86_64Encoder.AddRR(m_text, Reg.RSI, Reg.RBX);        // + offset
        X86_64Encoder.Li(m_text, Reg.RDX, 4096);              // count
        X86_64Encoder.Li(m_text, Reg.RAX, 0);                 // SYS_read
        X86_64Encoder.Syscall(m_text);
        X86_64Encoder.TestRR(m_text, Reg.RAX, Reg.RAX);
        int readDone = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_LE, 0);    // EOF or error
        X86_64Encoder.AddRR(m_text, Reg.RBX, Reg.RAX);
        X86_64Encoder.Jmp(m_text, readLoop - (m_text.Count + 5));
        PatchJcc(readDone, m_text.Count);

        // close(fd)
        X86_64Encoder.MovRR(m_text, Reg.RDI, Reg.R14);
        X86_64Encoder.Li(m_text, Reg.RAX, 3);                 // SYS_close
        X86_64Encoder.Syscall(m_text);

        // Store length, bump heap
        X86_64Encoder.MovStore(m_text, Reg.R13, Reg.RBX, 0);
        X86_64Encoder.MovRR(m_text, Reg.RAX, Reg.RBX);
        X86_64Encoder.AddRI(m_text, Reg.RAX, 15);
        X86_64Encoder.AndRI(m_text, Reg.RAX, -8);
        X86_64Encoder.AddRI(m_text, Reg.RAX, 8);
        X86_64Encoder.AddRR(m_text, HeapReg, Reg.RAX);
        X86_64Encoder.MovRR(m_text, Reg.RAX, Reg.R13);        // return result ptr

        X86_64Encoder.PopR(m_text, Reg.R14);
        X86_64Encoder.PopR(m_text, Reg.R13);
        X86_64Encoder.PopR(m_text, Reg.R12);
        X86_64Encoder.PopR(m_text, Reg.RBX);
        X86_64Encoder.Ret(m_text);
    }

    void EmitReadLineHelper()
    {
        // __read_line: → rax=string ptr (reads stdin until \n)
        m_functionOffsets["__read_line"] = m_text.Count;

        X86_64Encoder.PushR(m_text, Reg.RBX);
        // Read one byte at a time into heap buffer
        X86_64Encoder.MovRR(m_text, Reg.RBX, HeapReg);
        X86_64Encoder.Li(m_text, Reg.RCX, 0);  // length counter

        int readByte = m_text.Count;
        // read(0, heap+8+rcx, 1)
        X86_64Encoder.PushR(m_text, Reg.RCX);   // save counter (clobbered by syscall)
        X86_64Encoder.Li(m_text, Reg.RDI, 0);   // stdin
        X86_64Encoder.Lea(m_text, Reg.RSI, Reg.RBX, 8);
        X86_64Encoder.PopR(m_text, Reg.RCX);
        X86_64Encoder.PushR(m_text, Reg.RCX);
        X86_64Encoder.AddRR(m_text, Reg.RSI, Reg.RCX);
        X86_64Encoder.Li(m_text, Reg.RDX, 1);
        X86_64Encoder.Li(m_text, Reg.RAX, 0);   // SYS_read
        X86_64Encoder.Syscall(m_text);
        X86_64Encoder.PopR(m_text, Reg.RCX);

        // Check EOF
        X86_64Encoder.TestRR(m_text, Reg.RAX, Reg.RAX);
        int eof = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_LE, 0);

        // Check newline
        X86_64Encoder.MovRR(m_text, Reg.RDX, Reg.RBX);
        X86_64Encoder.AddRR(m_text, Reg.RDX, Reg.RCX);
        X86_64Encoder.MovzxByte(m_text, Reg.RDX, Reg.RDX, 8);
        X86_64Encoder.CmpRI(m_text, Reg.RDX, '\n');
        int gotNl = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_E, 0);

        X86_64Encoder.AddRI(m_text, Reg.RCX, 1);
        X86_64Encoder.Jmp(m_text, readByte - (m_text.Count + 5));

        PatchJcc(eof, m_text.Count);
        PatchJcc(gotNl, m_text.Count);

        // Store length, bump heap
        X86_64Encoder.MovStore(m_text, Reg.RBX, Reg.RCX, 0);
        X86_64Encoder.MovRR(m_text, Reg.RAX, Reg.RCX);
        X86_64Encoder.AddRI(m_text, Reg.RAX, 15);
        X86_64Encoder.AndRI(m_text, Reg.RAX, -8);
        X86_64Encoder.AddRR(m_text, HeapReg, Reg.RAX);
        X86_64Encoder.MovRR(m_text, Reg.RAX, Reg.RBX);

        X86_64Encoder.PopR(m_text, Reg.RBX);
        X86_64Encoder.Ret(m_text);
    }

    void EmitListConsHelper()
    {
        // __list_cons: rdi=head, rsi=tail_list_ptr → rax=new list
        m_functionOffsets["__list_cons"] = m_text.Count;

        X86_64Encoder.MovLoad(m_text, Reg.RCX, Reg.RSI, 0);   // old length
        X86_64Encoder.MovRR(m_text, Reg.RDX, Reg.RCX);
        X86_64Encoder.AddRI(m_text, Reg.RDX, 1);               // new length

        // Allocate: (newLen + 1) * 8
        X86_64Encoder.MovRR(m_text, Reg.RAX, HeapReg);
        X86_64Encoder.MovRR(m_text, Reg.R11, Reg.RDX);
        X86_64Encoder.AddRI(m_text, Reg.R11, 1);
        X86_64Encoder.ShlRI(m_text, Reg.R11, 3);
        X86_64Encoder.AddRR(m_text, HeapReg, Reg.R11);

        // Store new length and head
        X86_64Encoder.MovStore(m_text, Reg.RAX, Reg.RDX, 0);
        X86_64Encoder.MovStore(m_text, Reg.RAX, Reg.RDI, 8);

        // Copy old elements: i=0; while i < oldLen*8
        X86_64Encoder.ShlRI(m_text, Reg.RCX, 3);  // oldLen * 8
        X86_64Encoder.Li(m_text, Reg.R11, 0);
        int consLoop = m_text.Count;
        X86_64Encoder.CmpRR(m_text, Reg.R11, Reg.RCX);
        int consExit = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_GE, 0);
        X86_64Encoder.MovRR(m_text, Reg.RDX, Reg.RSI);
        X86_64Encoder.AddRR(m_text, Reg.RDX, Reg.R11);
        X86_64Encoder.MovLoad(m_text, Reg.RDX, Reg.RDX, 8);   // old[8+i]
        X86_64Encoder.MovRR(m_text, Reg.RDI, Reg.RAX);
        X86_64Encoder.AddRR(m_text, Reg.RDI, Reg.R11);
        X86_64Encoder.MovStore(m_text, Reg.RDI, Reg.RDX, 16);  // new[16+i]
        X86_64Encoder.AddRI(m_text, Reg.R11, 8);
        X86_64Encoder.Jmp(m_text, consLoop - (m_text.Count + 5));
        PatchJcc(consExit, m_text.Count);

        X86_64Encoder.Ret(m_text);
    }

    void EmitListAppendHelper()
    {
        // __list_append: rdi=list1, rsi=list2 → rax=concatenated list
        m_functionOffsets["__list_append"] = m_text.Count;

        X86_64Encoder.PushR(m_text, Reg.RBX);
        X86_64Encoder.PushR(m_text, Reg.R12);
        X86_64Encoder.PushR(m_text, Reg.R13);

        X86_64Encoder.MovRR(m_text, Reg.RBX, Reg.RDI);        // list1
        X86_64Encoder.MovRR(m_text, Reg.R12, Reg.RSI);        // list2
        X86_64Encoder.MovLoad(m_text, Reg.RCX, Reg.RBX, 0);  // len1
        X86_64Encoder.MovLoad(m_text, Reg.RDX, Reg.R12, 0);  // len2
        X86_64Encoder.MovRR(m_text, Reg.R13, Reg.RCX);
        X86_64Encoder.AddRR(m_text, Reg.R13, Reg.RDX);        // total

        // Allocate
        X86_64Encoder.MovRR(m_text, Reg.RAX, HeapReg);
        X86_64Encoder.MovRR(m_text, Reg.R11, Reg.R13);
        X86_64Encoder.AddRI(m_text, Reg.R11, 1);
        X86_64Encoder.ShlRI(m_text, Reg.R11, 3);
        X86_64Encoder.AddRR(m_text, HeapReg, Reg.R11);
        X86_64Encoder.MovStore(m_text, Reg.RAX, Reg.R13, 0);

        // Copy list1 elements
        X86_64Encoder.MovLoad(m_text, Reg.RCX, Reg.RBX, 0);
        X86_64Encoder.ShlRI(m_text, Reg.RCX, 3);
        X86_64Encoder.Li(m_text, Reg.R11, 0);
        int l1Loop = m_text.Count;
        X86_64Encoder.CmpRR(m_text, Reg.R11, Reg.RCX);
        int l1Exit = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_GE, 0);
        X86_64Encoder.MovRR(m_text, Reg.RDX, Reg.RBX);
        X86_64Encoder.AddRR(m_text, Reg.RDX, Reg.R11);
        X86_64Encoder.MovLoad(m_text, Reg.RDX, Reg.RDX, 8);
        X86_64Encoder.MovRR(m_text, Reg.RSI, Reg.RAX);
        X86_64Encoder.AddRR(m_text, Reg.RSI, Reg.R11);
        X86_64Encoder.MovStore(m_text, Reg.RSI, Reg.RDX, 8);
        X86_64Encoder.AddRI(m_text, Reg.R11, 8);
        X86_64Encoder.Jmp(m_text, l1Loop - (m_text.Count + 5));
        PatchJcc(l1Exit, m_text.Count);

        // Copy list2 elements (offset by len1*8)
        X86_64Encoder.MovLoad(m_text, Reg.RCX, Reg.RBX, 0);  // len1
        X86_64Encoder.ShlRI(m_text, Reg.RCX, 3);              // len1 bytes
        X86_64Encoder.MovLoad(m_text, Reg.RDX, Reg.R12, 0);  // len2
        X86_64Encoder.ShlRI(m_text, Reg.RDX, 3);              // len2 bytes
        X86_64Encoder.Li(m_text, Reg.R11, 0);
        int l2Loop = m_text.Count;
        X86_64Encoder.CmpRR(m_text, Reg.R11, Reg.RDX);
        int l2Exit = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_GE, 0);
        X86_64Encoder.MovRR(m_text, Reg.RSI, Reg.R12);
        X86_64Encoder.AddRR(m_text, Reg.RSI, Reg.R11);
        X86_64Encoder.MovLoad(m_text, Reg.RSI, Reg.RSI, 8);
        X86_64Encoder.MovRR(m_text, Reg.RDI, Reg.RAX);
        X86_64Encoder.AddRR(m_text, Reg.RDI, Reg.RCX);
        X86_64Encoder.AddRR(m_text, Reg.RDI, Reg.R11);
        X86_64Encoder.MovStore(m_text, Reg.RDI, Reg.RSI, 8);
        X86_64Encoder.AddRI(m_text, Reg.R11, 8);
        X86_64Encoder.Jmp(m_text, l2Loop - (m_text.Count + 5));
        PatchJcc(l2Exit, m_text.Count);

        X86_64Encoder.PopR(m_text, Reg.R13);
        X86_64Encoder.PopR(m_text, Reg.R12);
        X86_64Encoder.PopR(m_text, Reg.RBX);
        X86_64Encoder.Ret(m_text);
    }

    // ── Escape copy helpers (same pattern as RISC-V) ─────────────

    CodexType ResolveType(CodexType type)
    {
        if (type is ConstructedType ct && m_typeDefs[ct.Constructor.Value] is CodexType resolved)
            return resolved;
        return type;
    }

    static string EscapeCopyKey(CodexType type) => type switch
    {
        TextType => "text",
        RecordType rt => $"record_{rt.TypeName.Value}",
        SumType st => $"sum_{st.TypeName.Value}",
        ListType lt => $"list_{EscapeCopyKey(lt.Element)}",
        _ => $"type_{type.GetType().Name}"
    };

    string GetOrQueueEscapeHelper(CodexType type)
    {
        string key = EscapeCopyKey(type);
        if (m_escapeHelperNames.TryGetValue(key, out string? name))
            return name;
        name = $"__escape_{key}";
        m_escapeHelperNames[key] = name;
        m_escapeHelperQueue.Enqueue((key, name, type));
        return name;
    }

    void EmitEscapeCopyHelpers()
    {
        while (m_escapeHelperQueue.Count > 0)
        {
            (string _, string name, CodexType type) = m_escapeHelperQueue.Dequeue();
            // TODO: emit per-type helpers (same architecture as RISC-V)
            if (type is not TextType)
            {
                // Stub: return input unchanged
                m_functionOffsets[name] = m_text.Count;
                X86_64Encoder.MovRR(m_text, Reg.RAX, Reg.RDI);
                X86_64Encoder.Ret(m_text);
            }
        }
    }

    // ── _start entry point ───────────────────────────────────────

    void EmitStart(IRModule module)
    {
        m_functionOffsets["__start"] = m_text.Count;

        // Set up heap via brk(0) then brk(brk_result + 1MB)
        X86_64Encoder.Li(m_text, Reg.RAX, 12); // sys_brk
        X86_64Encoder.Li(m_text, Reg.RDI, 0);
        X86_64Encoder.Syscall(m_text);
        X86_64Encoder.MovRR(m_text, HeapReg, Reg.RAX); // heap start

        // Grow by 1MB
        X86_64Encoder.MovRR(m_text, Reg.RDI, Reg.RAX);
        X86_64Encoder.AddRI(m_text, Reg.RDI, 1024 * 1024);
        // Actually need to add a large immediate — use Li + add
        // For simplicity, use two-step:
        byte growReg = Reg.R11;
        X86_64Encoder.Li(m_text, growReg, 1024 * 1024);
        X86_64Encoder.MovRR(m_text, Reg.RDI, Reg.RAX);
        X86_64Encoder.AddRR(m_text, Reg.RDI, growReg);
        X86_64Encoder.Li(m_text, Reg.RAX, 12); // sys_brk
        X86_64Encoder.Syscall(m_text);

        // Call main
        EmitCallTo("main");

        // Exit with return value
        X86_64Encoder.MovRR(m_text, Reg.RDI, Reg.RAX);
        X86_64Encoder.Li(m_text, Reg.RAX, 60); // sys_exit
        X86_64Encoder.Syscall(m_text);
    }

    // ── Register allocation ──────────────────────────────────────

    byte AllocTemp()
    {
        byte reg = TempRegs[m_nextTemp % TempRegs.Length];
        m_nextTemp++;
        return reg;
    }

    byte AllocLocal()
    {
        if (m_nextLocal < LocalRegs.Length)
        {
            byte reg = LocalRegs[m_nextLocal];
            m_nextLocal++;
            return reg;
        }
        // Spill to stack
        byte slot = (byte)(SpillBase + m_spillCount);
        m_spillCount++;
        return slot;
    }

    void StoreLocal(byte local, byte valueReg)
    {
        if (local < SpillBase)
        {
            if (local != valueReg)
                X86_64Encoder.MovRR(m_text, local, valueReg);
        }
        else
        {
            int offset = -((int)(local - SpillBase) + 1) * 8 - LocalRegs.Length * 8;
            X86_64Encoder.MovStore(m_text, Reg.RBP, valueReg, offset);
        }
    }

    byte LoadLocal(byte local)
    {
        if (local < SpillBase)
            return local;
        byte scratch = (m_loadLocalToggle++ % 2 == 0) ? Reg.R8 : Reg.R9;
        int offset = -((int)(local - SpillBase) + 1) * 8 - LocalRegs.Length * 8;
        X86_64Encoder.MovLoad(m_text, scratch, Reg.RBP, offset);
        return scratch;
    }

    // ── Rodata helpers ───────────────────────────────────────────

    int AddRodataString(string value)
    {
        if (m_stringOffsets.TryGetValue(value, out int offset))
            return offset;
        offset = m_rodata.Count;
        byte[] utf8 = Encoding.UTF8.GetBytes(value);
        m_rodata.AddRange(BitConverter.GetBytes((long)utf8.Length));
        m_rodata.AddRange(utf8);
        while (m_rodata.Count % 8 != 0) m_rodata.Add(0);
        m_stringOffsets[value] = offset;
        return offset;
    }

    void EmitLoadRodataAddress(byte rd, int rodataOffset)
    {
        m_rodataFixups.Add(new RodataFixup(m_text.Count + 2, rodataOffset));
        X86_64Encoder.MovRI64(m_text, rd, 0); // placeholder
    }

    // ── Call and patch infrastructure ────────────────────────────

    void EmitCallTo(string targetName)
    {
        m_callPatches.Add((m_text.Count, targetName));
        X86_64Encoder.Call(m_text, 0); // placeholder — patched after all code emitted
    }

    // Emit sub rsp, imm32 (always 7 bytes — REX.W 81 EC <imm32>)
    void EmitSubRspImm32(int imm)
    {
        m_text.Add(0x48); // REX.W
        m_text.Add(0x81); // sub r/m64, imm32
        m_text.Add(0xEC); // ModRM: mod=11, reg=5(sub), rm=4(RSP)
        m_text.Add((byte)(imm & 0xFF));
        m_text.Add((byte)((imm >> 8) & 0xFF));
        m_text.Add((byte)((imm >> 16) & 0xFF));
        m_text.Add((byte)((imm >> 24) & 0xFF));
    }

    void PatchCalls()
    {
        foreach ((int patchOffset, string target) in m_callPatches)
        {
            if (m_functionOffsets.TryGetValue(target, out int targetOffset))
            {
                // call rel32: the rel32 starts at patchOffset+1, relative to patchOffset+5
                int rel32 = targetOffset - (patchOffset + 5);
                m_text[patchOffset + 1] = (byte)(rel32 & 0xFF);
                m_text[patchOffset + 2] = (byte)((rel32 >> 8) & 0xFF);
                m_text[patchOffset + 3] = (byte)((rel32 >> 16) & 0xFF);
                m_text[patchOffset + 4] = (byte)((rel32 >> 24) & 0xFF);
            }
        }
    }

    void PatchRodataRefs()
    {
        int textSize = m_text.Count;
        ulong rodataVaddr = ElfWriterX86_64.ComputeRodataVaddr(textSize);

        foreach (RodataFixup fixup in m_rodataFixups)
        {
            ulong addr = rodataVaddr + (ulong)fixup.RodataOffset;
            byte[] bytes = BitConverter.GetBytes((long)addr);
            for (int i = 0; i < 8; i++)
                m_text[fixup.PatchOffset + i] = bytes[i];
        }
    }

    void PatchJcc(int patchOffset, int targetOffset)
    {
        // jcc rel32: 0F 8x [rel32] — rel32 starts at patchOffset+2, relative to patchOffset+6
        int rel32 = targetOffset - (patchOffset + 6);
        m_text[patchOffset + 2] = (byte)(rel32 & 0xFF);
        m_text[patchOffset + 3] = (byte)((rel32 >> 8) & 0xFF);
        m_text[patchOffset + 4] = (byte)((rel32 >> 16) & 0xFF);
        m_text[patchOffset + 5] = (byte)((rel32 >> 24) & 0xFF);
    }

    void PatchJmp(int patchOffset, int targetOffset)
    {
        // jmp rel32: E9 [rel32] — rel32 starts at patchOffset+1, relative to patchOffset+5
        int rel32 = targetOffset - (patchOffset + 5);
        m_text[patchOffset + 1] = (byte)(rel32 & 0xFF);
        m_text[patchOffset + 2] = (byte)((rel32 >> 8) & 0xFF);
        m_text[patchOffset + 3] = (byte)((rel32 >> 16) & 0xFF);
        m_text[patchOffset + 4] = (byte)((rel32 >> 24) & 0xFF);
    }
}
