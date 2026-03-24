using System.Text;
using Codex.Core;
using Codex.IR;
using Codex.Types;

namespace Codex.Emit.Arm64;

sealed class Arm64CodeGen
{
    readonly List<uint> m_instructions = [];
    readonly List<byte> m_rodata = [];
    readonly Dictionary<string, int> m_functionOffsets = [];
    readonly List<(int InsnIndex, string Target)> m_callPatches = [];
    readonly List<RodataFixup> m_rodataFixups = [];
    readonly List<FuncAddrFixup> m_funcAddrFixups = [];
    readonly Dictionary<string, int> m_stringOffsets = [];
    readonly Dictionary<string, int> m_spillCounts = [];

    // x28 is reserved as the global heap pointer (callee-saved).
    const uint HeapReg = Arm64Reg.X28;

    // Temps: x9--x15 (caller-saved). We rotate through x12--x15 for AllocTemp.
    uint m_nextTemp = Arm64Reg.X12;
    // Locals: x19--x27 (callee-saved, x28=heap). Monotonic allocation.
    uint m_nextLocal = Arm64Reg.X19;
    int m_spillCount;
    int m_prologueIndex = -1;
    Dictionary<string, uint> m_locals = [];

    static readonly uint[] CalleeSaved = {
        Arm64Reg.X19, Arm64Reg.X20, Arm64Reg.X21, Arm64Reg.X22,
        Arm64Reg.X23, Arm64Reg.X24, Arm64Reg.X25, Arm64Reg.X26,
        Arm64Reg.X27
    };

    public void EmitModule(IRModule module)
    {
        EmitRuntimeHelpers();

        foreach (IRDefinition def in module.Definitions)
            EmitFunction(def);

        EmitStart(module);
        PatchCalls();
    }

    public byte[] BuildElf()
    {
        byte[] textSection = new byte[m_instructions.Count * 4];
        for (int i = 0; i < m_instructions.Count; i++)
        {
            uint insn = m_instructions[i];
            textSection[i * 4 + 0] = (byte)(insn & 0xFF);
            textSection[i * 4 + 1] = (byte)((insn >> 8) & 0xFF);
            textSection[i * 4 + 2] = (byte)((insn >> 16) & 0xFF);
            textSection[i * 4 + 3] = (byte)((insn >> 24) & 0xFF);
        }

        int startOffset = m_functionOffsets["__start"] * 4;
        return ElfWriterArm64.WriteExecutable(textSection, m_rodata.ToArray(), (ulong)startOffset);
    }

    // -- Function emission ----------------------------------------

    void EmitFunction(IRDefinition def)
    {
        m_functionOffsets[def.Name] = m_instructions.Count;
        m_locals = new Dictionary<string, uint>();
        m_nextTemp = Arm64Reg.X12;
        m_nextLocal = Arm64Reg.X19;
        m_spillCount = 0;

        // Prologue: save FP, LR, callee-saved regs, allocate frame.
        // Base frame: 16 (FP+LR) + 9*8 (callee-saved x19-x27) + 8 (x28/heap) = 96 bytes.
        // Spills grow the frame. We reserve 3 instruction slots for frame adjustment.
        m_prologueIndex = m_instructions.Count;
        Emit(Arm64Encoder.SubImm(Arm64Reg.Sp, Arm64Reg.Sp, 96)); // patched
        Emit(Arm64Encoder.Nop()); // reserve slot 1
        Emit(Arm64Encoder.Nop()); // reserve slot 2

        // Save FP and LR
        Emit(Arm64Encoder.Stp(Arm64Reg.Fp, Arm64Reg.Lr, Arm64Reg.Sp, 0));
        // Save callee-saved registers (pairs where possible)
        Emit(Arm64Encoder.Stp(Arm64Reg.X19, Arm64Reg.X20, Arm64Reg.Sp, 16));
        Emit(Arm64Encoder.Stp(Arm64Reg.X21, Arm64Reg.X22, Arm64Reg.Sp, 32));
        Emit(Arm64Encoder.Stp(Arm64Reg.X23, Arm64Reg.X24, Arm64Reg.Sp, 48));
        Emit(Arm64Encoder.Stp(Arm64Reg.X25, Arm64Reg.X26, Arm64Reg.Sp, 64));
        Emit(Arm64Encoder.Stp(Arm64Reg.X27, Arm64Reg.X28, Arm64Reg.Sp, 80));

        // Set FP = SP
        Emit(Arm64Encoder.Mov(Arm64Reg.Fp, Arm64Reg.Sp));

        // Save parameters to callee-saved locals
        for (int i = 0; i < def.Parameters.Length && i < 8; i++)
        {
            uint savedReg = AllocLocal();
            StoreLocal(savedReg, Arm64Reg.X0 + (uint)i);
            m_locals[def.Parameters[i].Name] = savedReg;
        }

        uint resultReg = EmitExpr(def.Body);
        if (resultReg >= SpillBase)
            EmitSpillLoad(Arm64Reg.X0, resultReg);
        else if (resultReg != Arm64Reg.X0)
            Emit(Arm64Encoder.Mov(Arm64Reg.X0, resultReg));

        m_spillCounts[def.Name] = m_spillCount;

        int frameSize = 96 + m_spillCount * 8;
        if (frameSize % 16 != 0) frameSize += 8;

        // Patch prologue
        PatchFrameAdjust(m_prologueIndex, frameSize);

        // Epilogue: restore callee-saved, LR, FP, deallocate frame
        Emit(Arm64Encoder.Ldp(Arm64Reg.X27, Arm64Reg.X28, Arm64Reg.Sp, 80));
        Emit(Arm64Encoder.Ldp(Arm64Reg.X25, Arm64Reg.X26, Arm64Reg.Sp, 64));
        Emit(Arm64Encoder.Ldp(Arm64Reg.X23, Arm64Reg.X24, Arm64Reg.Sp, 48));
        Emit(Arm64Encoder.Ldp(Arm64Reg.X21, Arm64Reg.X22, Arm64Reg.Sp, 32));
        Emit(Arm64Encoder.Ldp(Arm64Reg.X19, Arm64Reg.X20, Arm64Reg.Sp, 16));
        Emit(Arm64Encoder.Ldp(Arm64Reg.Fp, Arm64Reg.Lr, Arm64Reg.Sp, 0));
        EmitAddSp(frameSize);
        Emit(Arm64Encoder.Ret());
    }

    void PatchFrameAdjust(int index, int frameSize)
    {
        if (frameSize <= 4095)
        {
            m_instructions[index] = Arm64Encoder.SubImm(Arm64Reg.Sp, Arm64Reg.Sp, frameSize);
            m_instructions[index + 1] = Arm64Encoder.Nop();
            m_instructions[index + 2] = Arm64Encoder.Nop();
        }
        else
        {
            uint[] li = Arm64Encoder.Li(Arm64Reg.X9, frameSize);
            m_instructions[index] = li[0];
            m_instructions[index + 1] = li.Length > 1 ? li[1] : Arm64Encoder.Nop();
            m_instructions[index + 2] = Arm64Encoder.Sub(Arm64Reg.Sp, Arm64Reg.Sp, Arm64Reg.X9);
        }
    }

    void EmitAddSp(int imm)
    {
        if (imm <= 4095)
        {
            Emit(Arm64Encoder.AddImm(Arm64Reg.Sp, Arm64Reg.Sp, imm));
        }
        else
        {
            foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X9, imm)) Emit(insn);
            Emit(Arm64Encoder.Add(Arm64Reg.Sp, Arm64Reg.Sp, Arm64Reg.X9));
        }
    }

    // -- Expression emission --------------------------------------

    uint EmitExpr(IRExpr expr) => expr switch
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

    uint EmitUnhandled(IRExpr expr)
    {
        Console.Error.WriteLine($"ARM64 WARNING: unhandled IR node type {expr.GetType().Name}");
        return Arm64Reg.Xzr;
    }

    uint EmitIntegerLit(long value)
    {
        uint rd = AllocTemp();
        foreach (uint insn in Arm64Encoder.Li(rd, value))
            Emit(insn);
        return rd;
    }

    uint EmitTextLit(string value)
    {
        if (!m_stringOffsets.TryGetValue(value, out int rodataOffset))
        {
            rodataOffset = m_rodata.Count;
            byte[] utf8 = Encoding.UTF8.GetBytes(value);
            m_rodata.AddRange(BitConverter.GetBytes((long)utf8.Length));
            m_rodata.AddRange(utf8);
            while (m_rodata.Count % 8 != 0) m_rodata.Add(0);
            m_stringOffsets[value] = rodataOffset;
        }

        uint rd = AllocTemp();
        EmitLoadRodataAddress(rd, rodataOffset);
        return rd;
    }

    void EmitLoadRodataAddress(uint rd, int rodataOffset)
    {
        m_rodataFixups.Add(new RodataFixup(m_instructions.Count, rd, rodataOffset));
        Emit(Arm64Encoder.Nop());
        Emit(Arm64Encoder.Nop());
    }

    uint EmitName(IRName name)
    {
        if (m_locals.TryGetValue(name.Name, out uint reg))
            return LoadLocal(reg);

        if (name.Type is FunctionType)
            return EmitPartialApplication(name.Name, new List<IRExpr>());

        if (name.Type is SumType sumType)
        {
            int tag = -1;
            for (int i = 0; i < sumType.Constructors.Length; i++)
            {
                if (sumType.Constructors[i].Name.Value == name.Name)
                { tag = i; break; }
            }
            if (tag >= 0)
            {
                uint ptrReg = AllocTemp();
                Emit(Arm64Encoder.Mov(ptrReg, HeapReg));
                Emit(Arm64Encoder.AddImm(HeapReg, HeapReg, 8));
                uint tagReg = AllocTemp();
                foreach (uint insn in Arm64Encoder.Li(tagReg, tag)) Emit(insn);
                Emit(Arm64Encoder.Str(tagReg, ptrReg, 0));
                return ptrReg;
            }
        }

        if (TryEmitBuiltin(name.Name, new List<IRExpr>()))
        {
            uint rd = AllocTemp();
            Emit(Arm64Encoder.Mov(rd, Arm64Reg.X0));
            return rd;
        }
        {
            uint rd = AllocTemp();
            EmitCallTo(name.Name);
            Emit(Arm64Encoder.Mov(rd, Arm64Reg.X0));
            return rd;
        }
    }

    uint EmitBinary(IRBinary bin)
    {
        uint left = EmitExpr(bin.Left);
        uint savedLeft = AllocLocal();
        StoreLocal(savedLeft, left);

        uint right = EmitExpr(bin.Right);
        uint leftReg = LoadLocal(savedLeft);
        uint rd = AllocTemp();

        switch (bin.Op)
        {
            case IRBinaryOp.AddInt: Emit(Arm64Encoder.Add(rd, leftReg, right)); break;
            case IRBinaryOp.SubInt: Emit(Arm64Encoder.Sub(rd, leftReg, right)); break;
            case IRBinaryOp.MulInt: Emit(Arm64Encoder.Mul(rd, leftReg, right)); break;
            case IRBinaryOp.DivInt: Emit(Arm64Encoder.Sdiv(rd, leftReg, right)); break;
            case IRBinaryOp.Eq:
                if (bin.Left.Type is TextType)
                {
                    Emit(Arm64Encoder.Mov(Arm64Reg.X0, leftReg));
                    Emit(Arm64Encoder.Mov(Arm64Reg.X1, right));
                    EmitCallTo("__str_eq");
                    Emit(Arm64Encoder.Mov(rd, Arm64Reg.X0));
                }
                else
                {
                    Emit(Arm64Encoder.Cmp(leftReg, right));
                    Emit(Arm64Encoder.Csel(rd, leftReg, Arm64Reg.Xzr, Arm64Encoder.CondEq));
                    // Normalize to 1/0: we need CSET which is CSINC xd, xzr, xzr, invert(cond)
                    // CSET EQ = CSINC xd, xzr, xzr, NE
                    m_instructions[^1] = 0x9A9F17E0u | rd; // CSINC rd, XZR, XZR, NE -> 1 if EQ
                }
                break;
            case IRBinaryOp.NotEq:
                if (bin.Left.Type is TextType)
                {
                    Emit(Arm64Encoder.Mov(Arm64Reg.X0, leftReg));
                    Emit(Arm64Encoder.Mov(Arm64Reg.X1, right));
                    EmitCallTo("__str_eq");
                    // Invert: XOR with 1
                    Emit(Arm64Encoder.Mov(rd, Arm64Reg.X0));
                    uint oneReg = AllocTemp();
                    foreach (uint insn in Arm64Encoder.Li(oneReg, 1)) Emit(insn);
                    Emit(Arm64Encoder.Xor(rd, rd, oneReg));
                }
                else
                {
                    Emit(Arm64Encoder.Cmp(leftReg, right));
                    m_instructions.Add(0x9A9F07E0u | rd); // CSINC rd, XZR, XZR, EQ -> 1 if NE
                }
                break;
            case IRBinaryOp.AppendText:
                Emit(Arm64Encoder.Mov(Arm64Reg.X0, leftReg));
                Emit(Arm64Encoder.Mov(Arm64Reg.X1, right));
                EmitCallTo("__str_concat");
                Emit(Arm64Encoder.Mov(rd, Arm64Reg.X0));
                break;
            case IRBinaryOp.Lt:
                Emit(Arm64Encoder.Cmp(leftReg, right));
                m_instructions.Add(0x9A9FA7E0u | rd); // CSINC rd, XZR, XZR, GE -> 1 if LT
                break;
            case IRBinaryOp.Gt:
                Emit(Arm64Encoder.Cmp(leftReg, right));
                m_instructions.Add(0x9A9FD7E0u | rd); // CSINC rd, XZR, XZR, LE -> 1 if GT
                break;
            case IRBinaryOp.LtEq:
                Emit(Arm64Encoder.Cmp(leftReg, right));
                m_instructions.Add(0x9A9FC7E0u | rd); // CSINC rd, XZR, XZR, GT -> 1 if LE
                break;
            case IRBinaryOp.GtEq:
                Emit(Arm64Encoder.Cmp(leftReg, right));
                m_instructions.Add(0x9A9FB7E0u | rd); // CSINC rd, XZR, XZR, LT -> 1 if GE
                break;
            case IRBinaryOp.And: Emit(Arm64Encoder.And(rd, leftReg, right)); break;
            case IRBinaryOp.Or:  Emit(Arm64Encoder.Or(rd, leftReg, right)); break;
            case IRBinaryOp.ConsList:
                Emit(Arm64Encoder.Mov(Arm64Reg.X0, leftReg));
                Emit(Arm64Encoder.Mov(Arm64Reg.X1, right));
                EmitCallTo("__list_cons");
                Emit(Arm64Encoder.Mov(rd, Arm64Reg.X0));
                break;
            case IRBinaryOp.AppendList:
                Emit(Arm64Encoder.Mov(Arm64Reg.X0, leftReg));
                Emit(Arm64Encoder.Mov(Arm64Reg.X1, right));
                EmitCallTo("__list_append");
                Emit(Arm64Encoder.Mov(rd, Arm64Reg.X0));
                break;
            default:
                Console.Error.WriteLine($"ARM64 WARNING: unhandled binary op {bin.Op}");
                Emit(Arm64Encoder.Mov(rd, Arm64Reg.Xzr)); break;
        }

        return rd;
    }

    uint EmitIf(IRIf ifExpr)
    {
        uint cond = EmitExpr(ifExpr.Condition);

        int cbzIndex = m_instructions.Count;
        Emit(Arm64Encoder.Nop()); // patched: CBZ -> else

        uint thenReg = EmitExpr(ifExpr.Then);
        uint resultReg = AllocLocal();
        StoreLocal(resultReg, thenReg);

        int jEndIndex = m_instructions.Count;
        Emit(Arm64Encoder.Nop()); // patched: B -> end

        int elseStart = m_instructions.Count;
        uint elseReg = EmitExpr(ifExpr.Else);
        StoreLocal(resultReg, elseReg);

        int endIndex = m_instructions.Count;

        m_instructions[cbzIndex] = Arm64Encoder.Cbz(cond, (elseStart - cbzIndex) * 4);
        m_instructions[jEndIndex] = Arm64Encoder.B((endIndex - jEndIndex) * 4);

        return LoadLocal(resultReg);
    }

    uint EmitLet(IRLet letExpr)
    {
        uint valReg = EmitExpr(letExpr.Value);
        uint savedReg = AllocLocal();
        StoreLocal(savedReg, valReg);
        m_locals[letExpr.Name] = savedReg;
        return EmitExpr(letExpr.Body);
    }

    uint EmitApply(IRApply apply)
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
            if (TryEmitBuiltin(funcName.Name, args))
                return Arm64Reg.X0;

            if (apply.Type is SumType sumType)
            {
                uint ctorResult = EmitConstructor(funcName.Name, args, sumType);
                if (ctorResult != Arm64Reg.Xzr)
                    return ctorResult;
            }

            if (apply.Type is FunctionType && !m_locals.ContainsKey(funcName.Name))
                return EmitPartialApplication(funcName.Name, args);

            List<uint> argRegs = new();
            foreach (IRExpr arg in args)
            {
                uint r = EmitExpr(arg);
                uint saved = AllocLocal();
                StoreLocal(saved, r);
                argRegs.Add(saved);
            }

            for (int i = 0; i < argRegs.Count && i < 8; i++)
            {
                uint argVal = LoadLocal(argRegs[i]);
                if (argVal != Arm64Reg.X0 + (uint)i)
                    Emit(Arm64Encoder.Mov(Arm64Reg.X0 + (uint)i, argVal));
            }

            if (m_locals.ContainsKey(funcName.Name))
            {
                uint closureReg = LoadLocal(m_locals[funcName.Name]);
                Emit(Arm64Encoder.Mov(Arm64Reg.X11, closureReg)); // X11 = closure ptr (like T2 on RISC-V)
                Emit(Arm64Encoder.Ldr(Arm64Reg.X9, Arm64Reg.X11, 0)); // code_ptr
                Emit(Arm64Encoder.Blr(Arm64Reg.X9));
            }
            else
            {
                EmitCallTo(funcName.Name);
            }
            uint rd = AllocTemp();
            Emit(Arm64Encoder.Mov(rd, Arm64Reg.X0));
            return rd;
        }

        Console.Error.WriteLine($"ARM64 WARNING: EmitApply fallthrough -- {func.GetType().Name}");
        return Arm64Reg.Xzr;
    }

    uint EmitPartialApplication(string funcName, List<IRExpr> capturedArgs)
    {
        List<uint> capRegs = new();
        foreach (IRExpr arg in capturedArgs)
        {
            uint r = EmitExpr(arg);
            uint saved = AllocLocal();
            StoreLocal(saved, r);
            capRegs.Add(saved);
        }

        int numCaptures = capRegs.Count;
        string trampolineName = $"__tramp_{funcName}_{numCaptures}_{m_instructions.Count}";

        int jumpOverIdx = m_instructions.Count;
        Emit(Arm64Encoder.Nop());

        m_functionOffsets[trampolineName] = m_instructions.Count;

        // Shift visible args right by numCaptures positions
        for (int i = 7; i >= 0; i--)
        {
            if (i + numCaptures <= 7)
                Emit(Arm64Encoder.Mov(Arm64Reg.X0 + (uint)(i + numCaptures), Arm64Reg.X0 + (uint)i));
        }

        // Load captured args from closure (X11) into X0..X(numCaptures-1)
        for (int i = 0; i < numCaptures; i++)
            Emit(Arm64Encoder.Ldr(Arm64Reg.X0 + (uint)i, Arm64Reg.X11, 8 + i * 8));

        // Tail-jump to real function
        EmitLoadFunctionAddress(Arm64Reg.X9, funcName);
        Emit(Arm64Encoder.Br(Arm64Reg.X9));

        int afterTrampoline = m_instructions.Count;
        m_instructions[jumpOverIdx] = Arm64Encoder.B((afterTrampoline - jumpOverIdx) * 4);

        // Allocate closure on heap
        int closureSize = (1 + numCaptures) * 8;
        uint ptrReg = AllocTemp();
        Emit(Arm64Encoder.Mov(ptrReg, HeapReg));
        Emit(Arm64Encoder.AddImm(HeapReg, HeapReg, closureSize));

        EmitLoadFunctionAddress(Arm64Reg.X9, trampolineName);
        Emit(Arm64Encoder.Str(Arm64Reg.X9, ptrReg, 0));

        for (int i = 0; i < capRegs.Count; i++)
            Emit(Arm64Encoder.Str(LoadLocal(capRegs[i]), ptrReg, 8 + i * 8));

        return ptrReg;
    }

    uint EmitNegate(IRNegate neg)
    {
        uint operand = EmitExpr(neg.Operand);
        uint rd = AllocTemp();
        Emit(Arm64Encoder.Neg(rd, operand));
        return rd;
    }

    uint EmitDo(IRDo doExpr)
    {
        uint lastReg = Arm64Reg.Xzr;
        foreach (IRDoStatement stmt in doExpr.Statements)
        {
            switch (stmt)
            {
                case IRDoExec exec:
                    lastReg = EmitExpr(exec.Expression);
                    break;
                case IRDoBind bind:
                    uint valReg = EmitExpr(bind.Value);
                    uint savedReg = AllocLocal();
                    StoreLocal(savedReg, valReg);
                    m_locals[bind.Name] = savedReg;
                    break;
            }
        }
        return lastReg;
    }

    // -- Records, sum types, pattern matching ---------------------

    uint EmitRecord(IRRecord rec)
    {
        Dictionary<string, uint> fieldMap = new();
        foreach ((string name, IRExpr value) in rec.Fields)
        {
            uint r = EmitExpr(value);
            uint saved = AllocLocal();
            StoreLocal(saved, r);
            fieldMap[name] = saved;
        }

        int totalSize = rec.Fields.Length * 8;
        uint ptrReg = AllocTemp();
        Emit(Arm64Encoder.Mov(ptrReg, HeapReg));
        Emit(Arm64Encoder.AddImm(HeapReg, HeapReg, totalSize));

        if (rec.Type is RecordType rt)
        {
            for (int i = 0; i < rt.Fields.Length; i++)
            {
                string fieldName = rt.Fields[i].FieldName.Value;
                if (fieldMap.TryGetValue(fieldName, out uint saved))
                    Emit(Arm64Encoder.Str(LoadLocal(saved), ptrReg, i * 8));
            }
        }
        else
        {
            int i = 0;
            foreach ((string _, IRExpr _) in rec.Fields)
            {
                Emit(Arm64Encoder.Str(LoadLocal(fieldMap.Values.ElementAt(i)), ptrReg, i * 8));
                i++;
            }
        }

        return ptrReg;
    }

    uint EmitFieldAccess(IRFieldAccess fa)
    {
        uint baseReg = EmitExpr(fa.Record);
        int fieldIndex = 0;
        if (fa.Record.Type is RecordType rt)
        {
            for (int i = 0; i < rt.Fields.Length; i++)
            {
                if (rt.Fields[i].FieldName.Value == fa.FieldName)
                { fieldIndex = i; break; }
            }
        }
        uint rd = AllocTemp();
        Emit(Arm64Encoder.Ldr(rd, baseReg, fieldIndex * 8));
        return rd;
    }

    uint EmitConstructor(string ctorName, List<IRExpr> args, SumType sumType)
    {
        int tag = -1;
        for (int i = 0; i < sumType.Constructors.Length; i++)
        {
            if (sumType.Constructors[i].Name.Value == ctorName)
            { tag = i; break; }
        }
        if (tag < 0) return Arm64Reg.Xzr;

        List<uint> argRegs = new();
        foreach (IRExpr arg in args)
        {
            uint r = EmitExpr(arg);
            uint saved = AllocLocal();
            StoreLocal(saved, r);
            argRegs.Add(saved);
        }

        int totalSize = (1 + args.Count) * 8;
        uint ptrReg = AllocTemp();
        Emit(Arm64Encoder.Mov(ptrReg, HeapReg));
        Emit(Arm64Encoder.AddImm(HeapReg, HeapReg, totalSize));

        uint tagReg = AllocTemp();
        foreach (uint insn in Arm64Encoder.Li(tagReg, tag)) Emit(insn);
        Emit(Arm64Encoder.Str(tagReg, ptrReg, 0));

        for (int i = 0; i < argRegs.Count; i++)
            Emit(Arm64Encoder.Str(LoadLocal(argRegs[i]), ptrReg, 8 + i * 8));

        return ptrReg;
    }

    uint EmitMatch(IRMatch match)
    {
        uint scrutReg = EmitExpr(match.Scrutinee);
        uint savedScrut = AllocLocal();
        StoreLocal(savedScrut, scrutReg);

        uint resultReg = AllocTemp();
        EmitMatchBranches(match, 0, savedScrut, resultReg);
        return resultReg;
    }

    void EmitMatchBranches(IRMatch match, int index, uint scrutReg, uint resultReg)
    {
        if (index >= match.Branches.Length) return;
        IRMatchBranch branch = match.Branches[index];

        switch (branch.Pattern)
        {
            case IRWildcardPattern:
            {
                uint bodyReg = EmitExpr(branch.Body);
                StoreLocal(resultReg, bodyReg);
                break;
            }
            case IRVarPattern varPat:
            {
                uint localReg = AllocLocal();
                StoreLocal(localReg, LoadLocal(scrutReg));
                m_locals[varPat.Name] = localReg;
                uint bodyReg = EmitExpr(branch.Body);
                StoreLocal(resultReg, bodyReg);
                break;
            }
            case IRLiteralPattern litPat:
            {
                uint litReg = EmitLiteralValue(litPat.Value);
                uint scrutVal = LoadLocal(scrutReg);
                Emit(Arm64Encoder.Cmp(scrutVal, litReg));
                int branchIdx = m_instructions.Count;
                Emit(Arm64Encoder.Nop()); // patched: B.NE -> not_eq

                uint bodyReg = EmitExpr(branch.Body);
                StoreLocal(resultReg, bodyReg);
                int jumpEndIdx = m_instructions.Count;
                Emit(Arm64Encoder.Nop()); // patched: B -> end

                int nextStart = m_instructions.Count;
                EmitMatchBranches(match, index + 1, scrutReg, resultReg);
                int endIdx = m_instructions.Count;

                m_instructions[branchIdx] = Arm64Encoder.Bcond(Arm64Encoder.CondNe,
                    (nextStart - branchIdx) * 4);
                m_instructions[jumpEndIdx] = Arm64Encoder.B((endIdx - jumpEndIdx) * 4);
                break;
            }
            case IRCtorPattern ctorPat:
            {
                uint tagReg = AllocTemp();
                Emit(Arm64Encoder.Ldr(tagReg, LoadLocal(scrutReg), 0));

                int expectedTag = 0;
                if (ctorPat.Type is SumType sumType)
                {
                    for (int i = 0; i < sumType.Constructors.Length; i++)
                    {
                        if (sumType.Constructors[i].Name.Value == ctorPat.Name)
                        { expectedTag = i; break; }
                    }
                }

                uint expectedReg = AllocTemp();
                foreach (uint insn in Arm64Encoder.Li(expectedReg, expectedTag)) Emit(insn);

                Emit(Arm64Encoder.Cmp(tagReg, expectedReg));
                int branchIdx = m_instructions.Count;
                Emit(Arm64Encoder.Nop()); // patched: B.NE -> next

                for (int i = 0; i < ctorPat.SubPatterns.Length; i++)
                {
                    if (ctorPat.SubPatterns[i] is IRVarPattern varPat)
                    {
                        uint fieldReg = AllocLocal();
                        uint scrut = LoadLocal(scrutReg);
                        uint tmp = AllocTemp();
                        Emit(Arm64Encoder.Ldr(tmp, scrut, 8 + i * 8));
                        StoreLocal(fieldReg, tmp);
                        m_locals[varPat.Name] = fieldReg;
                    }
                }

                uint bodyReg = EmitExpr(branch.Body);
                StoreLocal(resultReg, bodyReg);
                int jumpEndIdx = m_instructions.Count;
                Emit(Arm64Encoder.Nop());

                int nextStart = m_instructions.Count;
                EmitMatchBranches(match, index + 1, scrutReg, resultReg);
                int endIdx = m_instructions.Count;

                m_instructions[branchIdx] = Arm64Encoder.Bcond(Arm64Encoder.CondNe,
                    (nextStart - branchIdx) * 4);
                m_instructions[jumpEndIdx] = Arm64Encoder.B((endIdx - jumpEndIdx) * 4);
                break;
            }
        }
    }

    uint EmitRegion(IRRegion region) => EmitExpr(region.Body);

    uint EmitList(IRList list)
    {
        List<uint> elemRegs = new();
        foreach (IRExpr elem in list.Elements)
        {
            uint r = EmitExpr(elem);
            uint saved = AllocLocal();
            StoreLocal(saved, r);
            elemRegs.Add(saved);
        }

        int totalSize = (1 + list.Elements.Length) * 8;
        uint ptrReg = AllocTemp();
        Emit(Arm64Encoder.Mov(ptrReg, HeapReg));
        Emit(Arm64Encoder.AddImm(HeapReg, HeapReg, totalSize));

        uint lenReg = AllocTemp();
        foreach (uint insn in Arm64Encoder.Li(lenReg, list.Elements.Length)) Emit(insn);
        Emit(Arm64Encoder.Str(lenReg, ptrReg, 0));

        for (int i = 0; i < elemRegs.Count; i++)
            Emit(Arm64Encoder.Str(LoadLocal(elemRegs[i]), ptrReg, 8 + i * 8));

        return ptrReg;
    }

    uint EmitError(IRError err)
    {
        uint msgReg = EmitTextLit(err.Message);
        Emit(Arm64Encoder.Ldr(Arm64Reg.X2, msgReg, 0));       // len
        Emit(Arm64Encoder.AddImm(Arm64Reg.X1, msgReg, 8));    // buf
        foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X8, 64)) Emit(insn);  // SYS_write
        foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X0, 2)) Emit(insn);   // stderr
        Emit(Arm64Encoder.Svc());
        foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X0, 1)) Emit(insn);
        EmitSyscallExit();
        return Arm64Reg.Xzr;
    }

    void EmitLoadFunctionAddress(uint rd, string funcName)
    {
        m_funcAddrFixups.Add(new FuncAddrFixup(m_instructions.Count, rd, funcName));
        Emit(Arm64Encoder.Nop());
        Emit(Arm64Encoder.Nop());
    }

    uint EmitLiteralValue(object value) => value switch
    {
        long l => EmitIntegerLit(l),
        bool b => EmitIntegerLit(b ? 1 : 0),
        string s => EmitTextLit(s),
        _ => Arm64Reg.Xzr
    };

    // -- Builtins -------------------------------------------------

    bool TryEmitBuiltin(string name, List<IRExpr> args)
    {
        switch (name)
        {
            case "print-line" when args.Count == 1:
                EmitPrintLine(EmitExpr(args[0]), args[0].Type);
                return true;

            case "text-length" when args.Count == 1:
            {
                uint ptr = EmitExpr(args[0]);
                Emit(Arm64Encoder.Ldr(Arm64Reg.X0, ptr, 0));
                return true;
            }

            case "integer-to-text" when args.Count == 1:
            {
                uint val = EmitExpr(args[0]);
                Emit(Arm64Encoder.Mov(Arm64Reg.X0, val));
                EmitCallTo("__itoa");
                return true;
            }

            case "text-to-integer" when args.Count == 1:
            {
                uint ptr = EmitExpr(args[0]);
                Emit(Arm64Encoder.Mov(Arm64Reg.X0, ptr));
                EmitCallTo("__text_to_int");
                return true;
            }

            case "char-at" when args.Count == 2:
            {
                uint textReg = EmitExpr(args[0]);
                uint savedText = AllocLocal();
                StoreLocal(savedText, textReg);
                uint indexReg = EmitExpr(args[1]);
                Emit(Arm64Encoder.Mov(Arm64Reg.X0, HeapReg));
                Emit(Arm64Encoder.AddImm(HeapReg, HeapReg, 16));
                foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X9, 1)) Emit(insn);
                Emit(Arm64Encoder.Str(Arm64Reg.X9, Arm64Reg.X0, 0)); // length=1
                Emit(Arm64Encoder.Mov(Arm64Reg.X11, indexReg));
                uint textVal = LoadLocal(savedText);
                Emit(Arm64Encoder.Add(Arm64Reg.X9, textVal, Arm64Reg.X11));
                Emit(Arm64Encoder.Ldrb(Arm64Reg.X9, Arm64Reg.X9, 8));
                Emit(Arm64Encoder.Strb(Arm64Reg.X9, Arm64Reg.X0, 8));
                return true;
            }

            case "substring" when args.Count == 3:
            {
                uint textReg = EmitExpr(args[0]);
                uint savedText = AllocLocal();
                StoreLocal(savedText, textReg);
                uint startReg = EmitExpr(args[1]);
                uint savedStart = AllocLocal();
                StoreLocal(savedStart, startReg);
                uint lenReg = EmitExpr(args[2]);
                Emit(Arm64Encoder.Mov(Arm64Reg.X10, lenReg)); // save len

                Emit(Arm64Encoder.Mov(Arm64Reg.X0, HeapReg));
                // alloc: align8(8 + len)
                Emit(Arm64Encoder.AddImm(Arm64Reg.X9, Arm64Reg.X10, 15));
                Emit(Arm64Encoder.AndImm(Arm64Reg.X9, Arm64Reg.X9, -8));
                Emit(Arm64Encoder.Add(HeapReg, HeapReg, Arm64Reg.X9));
                Emit(Arm64Encoder.Str(Arm64Reg.X10, Arm64Reg.X0, 0)); // store length

                uint subTextVal = LoadLocal(savedText);
                uint subStartVal = LoadLocal(savedStart);
                Emit(Arm64Encoder.Add(Arm64Reg.X9, subTextVal, subStartVal));
                Emit(Arm64Encoder.AddImm(Arm64Reg.X9, Arm64Reg.X9, 8)); // src = text+8+start
                Emit(Arm64Encoder.AddImm(Arm64Reg.X11, Arm64Reg.X0, 8)); // dst = result+8

                // Copy loop
                foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X12, 0)) Emit(insn);
                int loopStart = m_instructions.Count;
                Emit(Arm64Encoder.Cmp(Arm64Reg.X12, Arm64Reg.X10));
                int exitIdx = m_instructions.Count;
                Emit(Arm64Encoder.Nop()); // B.GE -> exit
                Emit(Arm64Encoder.LdrbReg(Arm64Reg.X13, Arm64Reg.X9, Arm64Reg.X12));
                Emit(Arm64Encoder.StrbReg(Arm64Reg.X13, Arm64Reg.X11, Arm64Reg.X12));
                Emit(Arm64Encoder.AddImm(Arm64Reg.X12, Arm64Reg.X12, 1));
                Emit(Arm64Encoder.B((loopStart - m_instructions.Count) * 4));
                int exitTarget = m_instructions.Count;
                m_instructions[exitIdx] = Arm64Encoder.Bcond(Arm64Encoder.CondGe, (exitTarget - exitIdx) * 4);
                return true;
            }

            case "show" when args.Count == 1:
            {
                uint valReg = EmitExpr(args[0]);
                if (args[0].Type is IntegerType)
                {
                    Emit(Arm64Encoder.Mov(Arm64Reg.X0, valReg));
                    EmitCallTo("__itoa");
                }
                else
                {
                    Emit(Arm64Encoder.Mov(Arm64Reg.X0, valReg));
                }
                return true;
            }

            case "list-length" when args.Count == 1:
            {
                uint ptr = EmitExpr(args[0]);
                Emit(Arm64Encoder.Ldr(Arm64Reg.X0, ptr, 0));
                return true;
            }

            case "list-at" when args.Count == 2:
            {
                uint listReg = EmitExpr(args[0]);
                uint savedList = AllocLocal();
                StoreLocal(savedList, listReg);
                uint idxReg = EmitExpr(args[1]);
                Emit(Arm64Encoder.Mov(Arm64Reg.X11, idxReg));
                foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X9, 8)) Emit(insn);
                Emit(Arm64Encoder.Mul(Arm64Reg.X11, Arm64Reg.X11, Arm64Reg.X9));
                uint listVal = LoadLocal(savedList);
                Emit(Arm64Encoder.Add(Arm64Reg.X9, listVal, Arm64Reg.X11));
                Emit(Arm64Encoder.Ldr(Arm64Reg.X0, Arm64Reg.X9, 8));
                return true;
            }

            case "read-line" when args.Count == 0:
                EmitCallTo("__read_line");
                return true;

            case "read-file" when args.Count == 1:
            {
                uint pathReg = EmitExpr(args[0]);
                Emit(Arm64Encoder.Mov(Arm64Reg.X0, pathReg));
                EmitCallTo("__read_file");
                return true;
            }

            case "write-file" when args.Count == 2:
            {
                uint pathReg = EmitExpr(args[0]);
                uint savedPath = AllocLocal();
                StoreLocal(savedPath, pathReg);
                uint contentReg = EmitExpr(args[1]);
                Emit(Arm64Encoder.Ldr(Arm64Reg.X2, contentReg, 0));
                Emit(Arm64Encoder.AddImm(Arm64Reg.X1, contentReg, 8));
                EmitSyscallWrite();
                Emit(Arm64Encoder.Mov(Arm64Reg.X0, Arm64Reg.Xzr));
                return true;
            }

            case "file-exists" when args.Count == 1:
            {
                uint pathReg = EmitExpr(args[0]);
                Emit(Arm64Encoder.Mov(Arm64Reg.X0, pathReg));
                EmitCallTo("__read_file");
                foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X0, 1)) Emit(insn);
                return true;
            }

            case "get-args" when args.Count == 0:
            {
                Emit(Arm64Encoder.Mov(Arm64Reg.X0, HeapReg));
                Emit(Arm64Encoder.Str(Arm64Reg.Xzr, HeapReg, 0));
                Emit(Arm64Encoder.AddImm(HeapReg, HeapReg, 8));
                return true;
            }

            case "current-dir" when args.Count == 0:
            {
                uint dotReg = EmitTextLit(".");
                Emit(Arm64Encoder.Mov(Arm64Reg.X0, dotReg));
                return true;
            }

            case "char-code-at" when args.Count == 2:
            {
                uint textReg = EmitExpr(args[0]);
                uint savedText = AllocLocal();
                StoreLocal(savedText, textReg);
                uint idxReg = EmitExpr(args[1]);
                Emit(Arm64Encoder.Mov(Arm64Reg.X11, idxReg));
                uint textVal = LoadLocal(savedText);
                Emit(Arm64Encoder.Add(Arm64Reg.X9, textVal, Arm64Reg.X11));
                Emit(Arm64Encoder.Ldrb(Arm64Reg.X0, Arm64Reg.X9, 8));
                return true;
            }

            case "char-code" when args.Count == 1:
            {
                uint textReg = EmitExpr(args[0]);
                Emit(Arm64Encoder.Ldrb(Arm64Reg.X0, textReg, 8));
                return true;
            }

            case "code-to-char" when args.Count == 1:
            {
                uint codeReg = EmitExpr(args[0]);
                Emit(Arm64Encoder.Mov(Arm64Reg.X0, HeapReg));
                Emit(Arm64Encoder.AddImm(HeapReg, HeapReg, 16));
                foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X9, 1)) Emit(insn);
                Emit(Arm64Encoder.Str(Arm64Reg.X9, Arm64Reg.X0, 0));
                Emit(Arm64Encoder.Strb(codeReg, Arm64Reg.X0, 8));
                return true;
            }

            case "is-letter" when args.Count == 1:
            {
                uint textReg = EmitExpr(args[0]);
                Emit(Arm64Encoder.Ldrb(Arm64Reg.X9, textReg, 8));
                // Check lowercase a-z
                foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X10, 'a')) Emit(insn);
                Emit(Arm64Encoder.Sub(Arm64Reg.X11, Arm64Reg.X9, Arm64Reg.X10));
                Emit(Arm64Encoder.CmpImm(Arm64Reg.X11, 26));
                // X12 = 1 if < 26 (unsigned), else 0
                m_instructions.Add(0x9A9F37E0u | Arm64Reg.X12); // CSINC X12, XZR, XZR, CC (carry clear = unsigned <)
                // Check uppercase A-Z
                foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X10, 'A')) Emit(insn);
                Emit(Arm64Encoder.Sub(Arm64Reg.X11, Arm64Reg.X9, Arm64Reg.X10));
                Emit(Arm64Encoder.CmpImm(Arm64Reg.X11, 26));
                m_instructions.Add(0x9A9F37E0u | Arm64Reg.X13); // CSINC X13, XZR, XZR, CC
                Emit(Arm64Encoder.Or(Arm64Reg.X0, Arm64Reg.X12, Arm64Reg.X13));
                return true;
            }

            case "text-replace" when args.Count == 3:
            {
                uint textReg = EmitExpr(args[0]);
                uint savedText = AllocLocal();
                StoreLocal(savedText, textReg);
                uint oldReg = EmitExpr(args[1]);
                uint savedOld = AllocLocal();
                StoreLocal(savedOld, oldReg);
                uint newReg = EmitExpr(args[2]);
                Emit(Arm64Encoder.Mov(Arm64Reg.X2, newReg));
                Emit(Arm64Encoder.Mov(Arm64Reg.X1, LoadLocal(savedOld)));
                Emit(Arm64Encoder.Mov(Arm64Reg.X0, LoadLocal(savedText)));
                EmitCallTo("__str_replace");
                return true;
            }

            default:
                return false;
        }
    }

    void EmitPrintLine(uint valueReg, CodexType type)
    {
        switch (type)
        {
            case IntegerType: EmitPrintI64(valueReg); break;
            case BooleanType: EmitPrintBool(valueReg); break;
            case TextType:    EmitPrintText(valueReg); break;
        }
    }

    void EmitPrintI64(uint valueReg)
    {
        Emit(Arm64Encoder.Mov(Arm64Reg.X0, valueReg));
        EmitCallTo("__itoa");
        // Print the resulting string + newline
        Emit(Arm64Encoder.Mov(Arm64Reg.X10, Arm64Reg.X0)); // save ptr
        Emit(Arm64Encoder.Ldr(Arm64Reg.X2, Arm64Reg.X10, 0));
        Emit(Arm64Encoder.AddImm(Arm64Reg.X1, Arm64Reg.X10, 8));
        EmitSyscallWrite();
        // Newline
        int nlOff = AddRodataString("\n");
        uint nlReg = AllocTemp();
        EmitLoadRodataAddress(nlReg, nlOff);
        Emit(Arm64Encoder.Ldr(Arm64Reg.X2, nlReg, 0));
        Emit(Arm64Encoder.AddImm(Arm64Reg.X1, nlReg, 8));
        EmitSyscallWrite();
    }

    void EmitPrintBool(uint valueReg)
    {
        int trueOffset = AddRodataString("True\n");
        int falseOffset = AddRodataString("False\n");

        int branchIndex = m_instructions.Count;
        Emit(Arm64Encoder.Nop()); // patched: CBZ -> false

        uint trueReg = AllocTemp();
        EmitLoadRodataAddress(trueReg, trueOffset);
        Emit(Arm64Encoder.Ldr(Arm64Reg.X2, trueReg, 0));
        Emit(Arm64Encoder.AddImm(Arm64Reg.X1, trueReg, 8));
        EmitSyscallWrite();
        int jEndIndex = m_instructions.Count;
        Emit(Arm64Encoder.Nop()); // patched: B -> end

        int falseStart = m_instructions.Count;
        uint falseReg = AllocTemp();
        EmitLoadRodataAddress(falseReg, falseOffset);
        Emit(Arm64Encoder.Ldr(Arm64Reg.X2, falseReg, 0));
        Emit(Arm64Encoder.AddImm(Arm64Reg.X1, falseReg, 8));
        EmitSyscallWrite();

        int endIndex = m_instructions.Count;
        m_instructions[branchIndex] = Arm64Encoder.Cbz(valueReg, (falseStart - branchIndex) * 4);
        m_instructions[jEndIndex] = Arm64Encoder.B((endIndex - jEndIndex) * 4);
    }

    void EmitPrintText(uint ptrReg)
    {
        Emit(Arm64Encoder.Ldr(Arm64Reg.X2, ptrReg, 0));
        Emit(Arm64Encoder.AddImm(Arm64Reg.X1, ptrReg, 8));
        EmitSyscallWrite();
        int nlOff = AddRodataString("\n");
        uint nlReg = AllocTemp();
        EmitLoadRodataAddress(nlReg, nlOff);
        Emit(Arm64Encoder.Ldr(Arm64Reg.X2, nlReg, 0));
        Emit(Arm64Encoder.AddImm(Arm64Reg.X1, nlReg, 8));
        EmitSyscallWrite();
    }

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

    // -- Runtime helpers ------------------------------------------
    // These are simplified stubs. Full implementations mirror RISC-V
    // runtime helpers but with ARM64 instructions.

    void EmitRuntimeHelpers()
    {
        EmitStrEqHelper();
        EmitStrConcatHelper();
        EmitItoaHelper();
        EmitTextToIntHelper();
        EmitListConsHelper();
        EmitListAppendHelper();
        EmitReadFileHelper();
        EmitReadLineHelper();
        EmitStrReplaceHelper();
    }

    void EmitStrEqHelper()
    {
        m_functionOffsets["__str_eq"] = m_instructions.Count;
        Emit(Arm64Encoder.Ldr(Arm64Reg.X9, Arm64Reg.X0, 0));   // len1
        Emit(Arm64Encoder.Ldr(Arm64Reg.X10, Arm64Reg.X1, 0));  // len2
        Emit(Arm64Encoder.Cmp(Arm64Reg.X9, Arm64Reg.X10));
        int bneLen = m_instructions.Count;
        Emit(Arm64Encoder.Nop()); // B.NE -> not_eq

        Emit(Arm64Encoder.AddImm(Arm64Reg.X0, Arm64Reg.X0, 8));
        Emit(Arm64Encoder.AddImm(Arm64Reg.X1, Arm64Reg.X1, 8));
        foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X11, 0)) Emit(insn);

        int loopStart = m_instructions.Count;
        Emit(Arm64Encoder.Cmp(Arm64Reg.X11, Arm64Reg.X9));
        int bgeIdx = m_instructions.Count;
        Emit(Arm64Encoder.Nop()); // B.GE -> equal

        Emit(Arm64Encoder.LdrbReg(Arm64Reg.X12, Arm64Reg.X0, Arm64Reg.X11));
        Emit(Arm64Encoder.LdrbReg(Arm64Reg.X13, Arm64Reg.X1, Arm64Reg.X11));
        Emit(Arm64Encoder.Cmp(Arm64Reg.X12, Arm64Reg.X13));
        int bneByte = m_instructions.Count;
        Emit(Arm64Encoder.Nop()); // B.NE -> not_eq

        Emit(Arm64Encoder.AddImm(Arm64Reg.X11, Arm64Reg.X11, 1));
        Emit(Arm64Encoder.B((loopStart - m_instructions.Count) * 4));
        int equalLabel = m_instructions.Count;
        foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X0, 1)) Emit(insn);
        Emit(Arm64Encoder.Ret());

        int notEqLabel = m_instructions.Count;
        foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X0, 0)) Emit(insn);
        Emit(Arm64Encoder.Ret());

        m_instructions[bneLen] = Arm64Encoder.Bcond(Arm64Encoder.CondNe, (notEqLabel - bneLen) * 4);
        m_instructions[bgeIdx] = Arm64Encoder.Bcond(Arm64Encoder.CondGe, (equalLabel - bgeIdx) * 4);
        m_instructions[bneByte] = Arm64Encoder.Bcond(Arm64Encoder.CondNe, (notEqLabel - bneByte) * 4);
    }

    void EmitStrConcatHelper()
    {
        m_functionOffsets["__str_concat"] = m_instructions.Count;
        // Simplified: save inputs, compute total, alloc, copy
        Emit(Arm64Encoder.Ldr(Arm64Reg.X9, Arm64Reg.X0, 0));    // len1
        Emit(Arm64Encoder.Ldr(Arm64Reg.X10, Arm64Reg.X1, 0));   // len2
        Emit(Arm64Encoder.Mov(Arm64Reg.X13, Arm64Reg.X0));       // save ptr1
        Emit(Arm64Encoder.Mov(Arm64Reg.X14, Arm64Reg.X1));       // save ptr2
        Emit(Arm64Encoder.Add(Arm64Reg.X15, Arm64Reg.X9, Arm64Reg.X10)); // total

        // Allocate: x0 = HeapReg; HeapReg += align8(8 + total)
        Emit(Arm64Encoder.Mov(Arm64Reg.X0, HeapReg));
        Emit(Arm64Encoder.AddImm(Arm64Reg.X11, Arm64Reg.X15, 15));
        Emit(Arm64Encoder.AndImm(Arm64Reg.X11, Arm64Reg.X11, -8));
        Emit(Arm64Encoder.Add(HeapReg, HeapReg, Arm64Reg.X11));

        Emit(Arm64Encoder.Str(Arm64Reg.X15, Arm64Reg.X0, 0));

        // Copy first string
        Emit(Arm64Encoder.AddImm(Arm64Reg.X11, Arm64Reg.X0, 8));  // dst
        Emit(Arm64Encoder.AddImm(Arm64Reg.X12, Arm64Reg.X13, 8)); // src1
        foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X2, 0)) Emit(insn);
        int loop1 = m_instructions.Count;
        Emit(Arm64Encoder.Cmp(Arm64Reg.X2, Arm64Reg.X9));
        int exit1 = m_instructions.Count;
        Emit(Arm64Encoder.Nop());
        Emit(Arm64Encoder.LdrbReg(Arm64Reg.X3, Arm64Reg.X12, Arm64Reg.X2));
        Emit(Arm64Encoder.StrbReg(Arm64Reg.X3, Arm64Reg.X11, Arm64Reg.X2));
        Emit(Arm64Encoder.AddImm(Arm64Reg.X2, Arm64Reg.X2, 1));
        Emit(Arm64Encoder.B((loop1 - m_instructions.Count) * 4));
        m_instructions[exit1] = Arm64Encoder.Bcond(Arm64Encoder.CondGe, (m_instructions.Count - exit1) * 4);

        // Copy second string
        Emit(Arm64Encoder.Add(Arm64Reg.X11, Arm64Reg.X11, Arm64Reg.X9)); // dst += len1
        Emit(Arm64Encoder.AddImm(Arm64Reg.X12, Arm64Reg.X14, 8));        // src2
        foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X2, 0)) Emit(insn);
        int loop2 = m_instructions.Count;
        Emit(Arm64Encoder.Cmp(Arm64Reg.X2, Arm64Reg.X10));
        int exit2 = m_instructions.Count;
        Emit(Arm64Encoder.Nop());
        Emit(Arm64Encoder.LdrbReg(Arm64Reg.X3, Arm64Reg.X12, Arm64Reg.X2));
        Emit(Arm64Encoder.StrbReg(Arm64Reg.X3, Arm64Reg.X11, Arm64Reg.X2));
        Emit(Arm64Encoder.AddImm(Arm64Reg.X2, Arm64Reg.X2, 1));
        Emit(Arm64Encoder.B((loop2 - m_instructions.Count) * 4));
        m_instructions[exit2] = Arm64Encoder.Bcond(Arm64Encoder.CondGe, (m_instructions.Count - exit2) * 4);

        Emit(Arm64Encoder.Ret());
    }
    void EmitItoaHelper()
    {
        m_functionOffsets["__itoa"] = m_instructions.Count;
        Emit(Arm64Encoder.SubImm(Arm64Reg.Sp, Arm64Reg.Sp, 48));
        Emit(Arm64Encoder.Str(Arm64Reg.Lr, Arm64Reg.Sp, 40));
        Emit(Arm64Encoder.Mov(Arm64Reg.X13, Arm64Reg.X0));
        Emit(Arm64Encoder.Cmp(Arm64Reg.X13, Arm64Reg.Xzr));
        // X14 = is_negative: CSINC X14, XZR, XZR, GE -> 1 if LT (negative)
        m_instructions.Add(0x9A9FA7E0u | Arm64Reg.X14);

        int skipNeg = m_instructions.Count;
        Emit(Arm64Encoder.Nop());
        Emit(Arm64Encoder.Neg(Arm64Reg.X13, Arm64Reg.X13));
        int posLabel = m_instructions.Count;
        m_instructions[skipNeg] = Arm64Encoder.Bcond(Arm64Encoder.CondGe, (posLabel - skipNeg) * 4);

        Emit(Arm64Encoder.AddImm(Arm64Reg.X9, Arm64Reg.Sp, 30));
        foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X10, 0)) Emit(insn);

        int skipZero = m_instructions.Count;
        Emit(Arm64Encoder.Nop());
        foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X11, '0')) Emit(insn);
        Emit(Arm64Encoder.Strb(Arm64Reg.X11, Arm64Reg.X9, 0));
        Emit(Arm64Encoder.SubImm(Arm64Reg.X9, Arm64Reg.X9, 1));
        Emit(Arm64Encoder.AddImm(Arm64Reg.X10, Arm64Reg.X10, 1));
        int jSign = m_instructions.Count;
        Emit(Arm64Encoder.Nop());

        int digitLoop = m_instructions.Count;
        m_instructions[skipZero] = Arm64Encoder.Cbnz(Arm64Reg.X13, (digitLoop - skipZero) * 4);

        int loopExit = m_instructions.Count;
        Emit(Arm64Encoder.Nop());
        foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X12, 10)) Emit(insn);
        Emit(Arm64Encoder.Sdiv(Arm64Reg.X11, Arm64Reg.X13, Arm64Reg.X12));
        Emit(Arm64Encoder.Msub(Arm64Reg.X15, Arm64Reg.X11, Arm64Reg.X12, Arm64Reg.X13));
        Emit(Arm64Encoder.AddImm(Arm64Reg.X15, Arm64Reg.X15, '0'));
        Emit(Arm64Encoder.Strb(Arm64Reg.X15, Arm64Reg.X9, 0));
        Emit(Arm64Encoder.SubImm(Arm64Reg.X9, Arm64Reg.X9, 1));
        Emit(Arm64Encoder.AddImm(Arm64Reg.X10, Arm64Reg.X10, 1));
        Emit(Arm64Encoder.Mov(Arm64Reg.X13, Arm64Reg.X11));
        Emit(Arm64Encoder.B((digitLoop - m_instructions.Count) * 4));

        int signCheck = m_instructions.Count;
        m_instructions[loopExit] = Arm64Encoder.Cbz(Arm64Reg.X13, (signCheck - loopExit) * 4);
        m_instructions[jSign] = Arm64Encoder.B((signCheck - jSign) * 4);

        int skipMinus = m_instructions.Count;
        Emit(Arm64Encoder.Nop());
        foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X11, '-')) Emit(insn);
        Emit(Arm64Encoder.Strb(Arm64Reg.X11, Arm64Reg.X9, 0));
        Emit(Arm64Encoder.SubImm(Arm64Reg.X9, Arm64Reg.X9, 1));
        Emit(Arm64Encoder.AddImm(Arm64Reg.X10, Arm64Reg.X10, 1));
        int afterMinus = m_instructions.Count;
        m_instructions[skipMinus] = Arm64Encoder.Cbz(Arm64Reg.X14, (afterMinus - skipMinus) * 4);

        Emit(Arm64Encoder.AddImm(Arm64Reg.X9, Arm64Reg.X9, 1));
        Emit(Arm64Encoder.Mov(Arm64Reg.X0, HeapReg));
        Emit(Arm64Encoder.AddImm(Arm64Reg.X11, Arm64Reg.X10, 15));
        Emit(Arm64Encoder.AndImm(Arm64Reg.X11, Arm64Reg.X11, -8));
        Emit(Arm64Encoder.Add(HeapReg, HeapReg, Arm64Reg.X11));
        Emit(Arm64Encoder.Str(Arm64Reg.X10, Arm64Reg.X0, 0));

        foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X11, 0)) Emit(insn);
        int copyLoop = m_instructions.Count;
        Emit(Arm64Encoder.Cmp(Arm64Reg.X11, Arm64Reg.X10));
        int copyExit = m_instructions.Count;
        Emit(Arm64Encoder.Nop());
        Emit(Arm64Encoder.LdrbReg(Arm64Reg.X12, Arm64Reg.X9, Arm64Reg.X11));
        Emit(Arm64Encoder.Add(Arm64Reg.X13, Arm64Reg.X0, Arm64Reg.X11));
        Emit(Arm64Encoder.Strb(Arm64Reg.X12, Arm64Reg.X13, 8));
        Emit(Arm64Encoder.AddImm(Arm64Reg.X11, Arm64Reg.X11, 1));
        Emit(Arm64Encoder.B((copyLoop - m_instructions.Count) * 4));
        m_instructions[copyExit] = Arm64Encoder.Bcond(Arm64Encoder.CondGe, (m_instructions.Count - copyExit) * 4);

        Emit(Arm64Encoder.Ldr(Arm64Reg.Lr, Arm64Reg.Sp, 40));
        Emit(Arm64Encoder.AddImm(Arm64Reg.Sp, Arm64Reg.Sp, 48));
        Emit(Arm64Encoder.Ret());
    }

    void EmitTextToIntHelper()
    {
        m_functionOffsets["__text_to_int"] = m_instructions.Count;
        Emit(Arm64Encoder.Ldr(Arm64Reg.X9, Arm64Reg.X0, 0));
        Emit(Arm64Encoder.AddImm(Arm64Reg.X0, Arm64Reg.X0, 8));
        foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X10, 0)) Emit(insn);
        foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X11, 0)) Emit(insn);
        foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X12, 0)) Emit(insn);

        int emptyCheck = m_instructions.Count;
        Emit(Arm64Encoder.Nop());
        Emit(Arm64Encoder.Ldrb(Arm64Reg.X13, Arm64Reg.X0, 0));
        Emit(Arm64Encoder.CmpImm(Arm64Reg.X13, '-'));
        int noMinus = m_instructions.Count;
        Emit(Arm64Encoder.Nop());
        foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X12, 1)) Emit(insn);
        Emit(Arm64Encoder.AddImm(Arm64Reg.X11, Arm64Reg.X11, 1));

        int parseLoop = m_instructions.Count;
        m_instructions[noMinus] = Arm64Encoder.Bcond(Arm64Encoder.CondNe, (parseLoop - noMinus) * 4);

        Emit(Arm64Encoder.Cmp(Arm64Reg.X11, Arm64Reg.X9));
        int parseExit = m_instructions.Count;
        Emit(Arm64Encoder.Nop());
        Emit(Arm64Encoder.LdrbReg(Arm64Reg.X13, Arm64Reg.X0, Arm64Reg.X11));
        Emit(Arm64Encoder.SubImm(Arm64Reg.X13, Arm64Reg.X13, '0'));
        foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X14, 10)) Emit(insn);
        Emit(Arm64Encoder.Mul(Arm64Reg.X10, Arm64Reg.X10, Arm64Reg.X14));
        Emit(Arm64Encoder.Add(Arm64Reg.X10, Arm64Reg.X10, Arm64Reg.X13));
        Emit(Arm64Encoder.AddImm(Arm64Reg.X11, Arm64Reg.X11, 1));
        Emit(Arm64Encoder.B((parseLoop - m_instructions.Count) * 4));

        int negCheck = m_instructions.Count;
        m_instructions[parseExit] = Arm64Encoder.Bcond(Arm64Encoder.CondGe, (negCheck - parseExit) * 4);

        int skipNeg = m_instructions.Count;
        Emit(Arm64Encoder.Nop());
        Emit(Arm64Encoder.Neg(Arm64Reg.X10, Arm64Reg.X10));

        int doneLabel = m_instructions.Count;
        m_instructions[emptyCheck] = Arm64Encoder.Cbz(Arm64Reg.X9, (doneLabel - emptyCheck) * 4);
        m_instructions[skipNeg] = Arm64Encoder.Cbz(Arm64Reg.X12, (doneLabel - skipNeg) * 4);
        Emit(Arm64Encoder.Mov(Arm64Reg.X0, Arm64Reg.X10));
        Emit(Arm64Encoder.Ret());
    }

    void EmitListConsHelper()
    {
        m_functionOffsets["__list_cons"] = m_instructions.Count;
        Emit(Arm64Encoder.Ldr(Arm64Reg.X9, Arm64Reg.X1, 0));
        Emit(Arm64Encoder.AddImm(Arm64Reg.X10, Arm64Reg.X9, 1));
        Emit(Arm64Encoder.Mov(Arm64Reg.X12, Arm64Reg.X0));
        Emit(Arm64Encoder.Mov(Arm64Reg.X13, Arm64Reg.X1));

        Emit(Arm64Encoder.Mov(Arm64Reg.X0, HeapReg));
        Emit(Arm64Encoder.AddImm(Arm64Reg.X11, Arm64Reg.X10, 1));
        foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X14, 8)) Emit(insn);
        Emit(Arm64Encoder.Mul(Arm64Reg.X11, Arm64Reg.X11, Arm64Reg.X14));
        Emit(Arm64Encoder.Add(HeapReg, HeapReg, Arm64Reg.X11));

        Emit(Arm64Encoder.Str(Arm64Reg.X10, Arm64Reg.X0, 0));
        Emit(Arm64Encoder.Str(Arm64Reg.X12, Arm64Reg.X0, 8));

        foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X14, 8)) Emit(insn);
        Emit(Arm64Encoder.Mul(Arm64Reg.X14, Arm64Reg.X9, Arm64Reg.X14));
        foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X11, 0)) Emit(insn);

        int loopStart = m_instructions.Count;
        Emit(Arm64Encoder.Cmp(Arm64Reg.X11, Arm64Reg.X14));
        int exitIdx = m_instructions.Count;
        Emit(Arm64Encoder.Nop());
        Emit(Arm64Encoder.Add(Arm64Reg.X15, Arm64Reg.X13, Arm64Reg.X11));
        Emit(Arm64Encoder.Ldr(Arm64Reg.X15, Arm64Reg.X15, 8));
        Emit(Arm64Encoder.Add(Arm64Reg.X12, Arm64Reg.X0, Arm64Reg.X11));
        Emit(Arm64Encoder.Str(Arm64Reg.X15, Arm64Reg.X12, 16));
        Emit(Arm64Encoder.AddImm(Arm64Reg.X11, Arm64Reg.X11, 8));
        Emit(Arm64Encoder.B((loopStart - m_instructions.Count) * 4));
        m_instructions[exitIdx] = Arm64Encoder.Bcond(Arm64Encoder.CondGe, (m_instructions.Count - exitIdx) * 4);

        Emit(Arm64Encoder.Ret());
    }

    void EmitListAppendHelper()
    {
        m_functionOffsets["__list_append"] = m_instructions.Count;
        Emit(Arm64Encoder.Ldr(Arm64Reg.X9, Arm64Reg.X0, 0));
        Emit(Arm64Encoder.Ldr(Arm64Reg.X10, Arm64Reg.X1, 0));
        Emit(Arm64Encoder.Add(Arm64Reg.X15, Arm64Reg.X9, Arm64Reg.X10));
        Emit(Arm64Encoder.Mov(Arm64Reg.X13, Arm64Reg.X0));
        Emit(Arm64Encoder.Mov(Arm64Reg.X14, Arm64Reg.X1));

        Emit(Arm64Encoder.Mov(Arm64Reg.X0, HeapReg));
        Emit(Arm64Encoder.AddImm(Arm64Reg.X11, Arm64Reg.X15, 1));
        foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X12, 8)) Emit(insn);
        Emit(Arm64Encoder.Mul(Arm64Reg.X11, Arm64Reg.X11, Arm64Reg.X12));
        Emit(Arm64Encoder.Add(HeapReg, HeapReg, Arm64Reg.X11));
        Emit(Arm64Encoder.Str(Arm64Reg.X15, Arm64Reg.X0, 0));

        foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X12, 8)) Emit(insn);
        Emit(Arm64Encoder.Mul(Arm64Reg.X11, Arm64Reg.X9, Arm64Reg.X12));
        foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X12, 0)) Emit(insn);
        int loop1 = m_instructions.Count;
        Emit(Arm64Encoder.Cmp(Arm64Reg.X12, Arm64Reg.X11));
        int exit1 = m_instructions.Count;
        Emit(Arm64Encoder.Nop());
        Emit(Arm64Encoder.Add(Arm64Reg.X2, Arm64Reg.X13, Arm64Reg.X12));
        Emit(Arm64Encoder.Ldr(Arm64Reg.X2, Arm64Reg.X2, 8));
        Emit(Arm64Encoder.Add(Arm64Reg.X3, Arm64Reg.X0, Arm64Reg.X12));
        Emit(Arm64Encoder.Str(Arm64Reg.X2, Arm64Reg.X3, 8));
        Emit(Arm64Encoder.AddImm(Arm64Reg.X12, Arm64Reg.X12, 8));
        Emit(Arm64Encoder.B((loop1 - m_instructions.Count) * 4));
        m_instructions[exit1] = Arm64Encoder.Bcond(Arm64Encoder.CondGe, (m_instructions.Count - exit1) * 4);

        foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X2, 8)) Emit(insn);
        Emit(Arm64Encoder.Mul(Arm64Reg.X11, Arm64Reg.X10, Arm64Reg.X2));
        foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X2, 0)) Emit(insn);
        int loop2 = m_instructions.Count;
        Emit(Arm64Encoder.Cmp(Arm64Reg.X2, Arm64Reg.X11));
        int exit2 = m_instructions.Count;
        Emit(Arm64Encoder.Nop());
        Emit(Arm64Encoder.Add(Arm64Reg.X3, Arm64Reg.X14, Arm64Reg.X2));
        Emit(Arm64Encoder.Ldr(Arm64Reg.X3, Arm64Reg.X3, 8));
        Emit(Arm64Encoder.Add(Arm64Reg.X4, Arm64Reg.X12, Arm64Reg.X2));
        Emit(Arm64Encoder.Add(Arm64Reg.X4, Arm64Reg.X0, Arm64Reg.X4));
        Emit(Arm64Encoder.Str(Arm64Reg.X3, Arm64Reg.X4, 8));
        Emit(Arm64Encoder.AddImm(Arm64Reg.X2, Arm64Reg.X2, 8));
        Emit(Arm64Encoder.B((loop2 - m_instructions.Count) * 4));
        m_instructions[exit2] = Arm64Encoder.Bcond(Arm64Encoder.CondGe, (m_instructions.Count - exit2) * 4);

        Emit(Arm64Encoder.Ret());
    }

    void EmitReadFileHelper()
    {
        m_functionOffsets["__read_file"] = m_instructions.Count;
        Emit(Arm64Encoder.SubImm(Arm64Reg.Sp, Arm64Reg.Sp, 48));
        Emit(Arm64Encoder.Str(Arm64Reg.Lr, Arm64Reg.Sp, 40));

        Emit(Arm64Encoder.Ldr(Arm64Reg.X9, Arm64Reg.X0, 0));
        Emit(Arm64Encoder.AddImm(Arm64Reg.X10, Arm64Reg.X0, 8));

        Emit(Arm64Encoder.Mov(Arm64Reg.X13, HeapReg));
        foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X11, 0)) Emit(insn);
        int cpLoop = m_instructions.Count;
        Emit(Arm64Encoder.Cmp(Arm64Reg.X11, Arm64Reg.X9));
        int cpExit = m_instructions.Count;
        Emit(Arm64Encoder.Nop());
        Emit(Arm64Encoder.LdrbReg(Arm64Reg.X12, Arm64Reg.X10, Arm64Reg.X11));
        Emit(Arm64Encoder.StrbReg(Arm64Reg.X12, Arm64Reg.X13, Arm64Reg.X11));
        Emit(Arm64Encoder.AddImm(Arm64Reg.X11, Arm64Reg.X11, 1));
        Emit(Arm64Encoder.B((cpLoop - m_instructions.Count) * 4));
        m_instructions[cpExit] = Arm64Encoder.Bcond(Arm64Encoder.CondGe, (m_instructions.Count - cpExit) * 4);
        Emit(Arm64Encoder.Add(Arm64Reg.X12, Arm64Reg.X13, Arm64Reg.X9));
        Emit(Arm64Encoder.Strb(Arm64Reg.Xzr, Arm64Reg.X12, 0));

        foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X0, -100)) Emit(insn);
        Emit(Arm64Encoder.Mov(Arm64Reg.X1, Arm64Reg.X13));
        foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X2, 0)) Emit(insn);
        foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X3, 0)) Emit(insn);
        foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X8, 56)) Emit(insn);
        Emit(Arm64Encoder.Svc());

        Emit(Arm64Encoder.Str(Arm64Reg.X0, Arm64Reg.Sp, 0));
        Emit(Arm64Encoder.Add(Arm64Reg.X13, Arm64Reg.X13, Arm64Reg.X9));
        Emit(Arm64Encoder.AddImm(Arm64Reg.X13, Arm64Reg.X13, 8));
        Emit(Arm64Encoder.Str(Arm64Reg.X13, Arm64Reg.Sp, 8));

        Emit(Arm64Encoder.Mov(Arm64Reg.X14, Arm64Reg.X13));
        foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X15, 0)) Emit(insn);

        int readLoop = m_instructions.Count;
        Emit(Arm64Encoder.Ldr(Arm64Reg.X0, Arm64Reg.Sp, 0));
        Emit(Arm64Encoder.Add(Arm64Reg.X1, Arm64Reg.X14, Arm64Reg.X15));
        Emit(Arm64Encoder.AddImm(Arm64Reg.X1, Arm64Reg.X1, 8));
        foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X2, 4096)) Emit(insn);
        foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X8, 63)) Emit(insn);
        Emit(Arm64Encoder.Svc());
        int doneRead = m_instructions.Count;
        Emit(Arm64Encoder.Nop());
        Emit(Arm64Encoder.Add(Arm64Reg.X15, Arm64Reg.X15, Arm64Reg.X0));
        Emit(Arm64Encoder.B((readLoop - m_instructions.Count) * 4));
        int doneTarget = m_instructions.Count;
        m_instructions[doneRead] = Arm64Encoder.Cbz(Arm64Reg.X0, (doneTarget - doneRead) * 4);

        Emit(Arm64Encoder.Ldr(Arm64Reg.X0, Arm64Reg.Sp, 0));
        foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X8, 57)) Emit(insn);
        Emit(Arm64Encoder.Svc());

        Emit(Arm64Encoder.Ldr(Arm64Reg.X14, Arm64Reg.Sp, 8));
        Emit(Arm64Encoder.Str(Arm64Reg.X15, Arm64Reg.X14, 0));
        Emit(Arm64Encoder.AddImm(Arm64Reg.X0, Arm64Reg.X15, 15));
        Emit(Arm64Encoder.AndImm(Arm64Reg.X0, Arm64Reg.X0, -8));
        Emit(Arm64Encoder.AddImm(Arm64Reg.X0, Arm64Reg.X0, 8));
        Emit(Arm64Encoder.Add(HeapReg, Arm64Reg.X14, Arm64Reg.X0));
        Emit(Arm64Encoder.Mov(Arm64Reg.X0, Arm64Reg.X14));

        Emit(Arm64Encoder.Ldr(Arm64Reg.Lr, Arm64Reg.Sp, 40));
        Emit(Arm64Encoder.AddImm(Arm64Reg.Sp, Arm64Reg.Sp, 48));
        Emit(Arm64Encoder.Ret());
    }

    void EmitReadLineHelper()
    {
        m_functionOffsets["__read_line"] = m_instructions.Count;
        Emit(Arm64Encoder.Mov(Arm64Reg.X13, HeapReg));
        foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X14, 0)) Emit(insn);

        int rdLoop = m_instructions.Count;
        Emit(Arm64Encoder.SubImm(Arm64Reg.Sp, Arm64Reg.Sp, 16));
        foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X0, 0)) Emit(insn);
        Emit(Arm64Encoder.Mov(Arm64Reg.X1, Arm64Reg.Sp));
        foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X2, 1)) Emit(insn);
        foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X8, 63)) Emit(insn);
        Emit(Arm64Encoder.Svc());
        Emit(Arm64Encoder.Ldrb(Arm64Reg.X9, Arm64Reg.Sp, 0));
        Emit(Arm64Encoder.AddImm(Arm64Reg.Sp, Arm64Reg.Sp, 16));

        int eofCheck = m_instructions.Count;
        Emit(Arm64Encoder.Nop());
        Emit(Arm64Encoder.CmpImm(Arm64Reg.X9, '\n'));
        int nlCheck = m_instructions.Count;
        Emit(Arm64Encoder.Nop());

        Emit(Arm64Encoder.Add(Arm64Reg.X10, Arm64Reg.X13, Arm64Reg.X14));
        Emit(Arm64Encoder.Strb(Arm64Reg.X9, Arm64Reg.X10, 8));
        Emit(Arm64Encoder.AddImm(Arm64Reg.X14, Arm64Reg.X14, 1));
        Emit(Arm64Encoder.B((rdLoop - m_instructions.Count) * 4));

        int doneLabel = m_instructions.Count;
        m_instructions[eofCheck] = Arm64Encoder.Cbz(Arm64Reg.X0, (doneLabel - eofCheck) * 4);
        m_instructions[nlCheck] = Arm64Encoder.Bcond(Arm64Encoder.CondEq, (doneLabel - nlCheck) * 4);

        Emit(Arm64Encoder.Str(Arm64Reg.X14, Arm64Reg.X13, 0));
        Emit(Arm64Encoder.AddImm(Arm64Reg.X0, Arm64Reg.X14, 15));
        Emit(Arm64Encoder.AndImm(Arm64Reg.X0, Arm64Reg.X0, -8));
        Emit(Arm64Encoder.Add(HeapReg, Arm64Reg.X13, Arm64Reg.X0));
        Emit(Arm64Encoder.Mov(Arm64Reg.X0, Arm64Reg.X13));
        Emit(Arm64Encoder.Ret());
    }

    void EmitStrReplaceHelper()
    {
        m_functionOffsets["__str_replace"] = m_instructions.Count;
        // Simplified stub: return original text (full impl needed for self-host)
        Emit(Arm64Encoder.Ret());
    }

    // -- Syscalls -------------------------------------------------

    void EmitSyscallWrite()
    {
        foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X8, 64)) Emit(insn);
        foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X0, 1)) Emit(insn);
        Emit(Arm64Encoder.Svc());
    }

    void EmitSyscallExit()
    {
        foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X8, 93)) Emit(insn);
        Emit(Arm64Encoder.Svc());
    }

    // -- _start ---------------------------------------------------

    void EmitStart(IRModule module)
    {
        m_functionOffsets["__start"] = m_instructions.Count;

        foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X0, 0)) Emit(insn);
        foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X8, 214)) Emit(insn);
        Emit(Arm64Encoder.Svc());
        Emit(Arm64Encoder.Mov(HeapReg, Arm64Reg.X0));
        foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X9, 1048576)) Emit(insn);
        Emit(Arm64Encoder.Add(Arm64Reg.X0, HeapReg, Arm64Reg.X9));
        foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X8, 214)) Emit(insn);
        Emit(Arm64Encoder.Svc());

        IRDefinition? mainDef = null;
        foreach (IRDefinition def in module.Definitions)
        {
            if (def.Name == "main") { mainDef = def; break; }
        }

        if (mainDef is null)
        {
            foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X0, 0)) Emit(insn);
            EmitSyscallExit();
            return;
        }

        EmitCallTo("main");

        CodexType returnType = ComputeReturnType(mainDef.Type, mainDef.Parameters.Length);
        switch (returnType)
        {
            case IntegerType: EmitPrintI64(Arm64Reg.X0); break;
            case BooleanType: EmitPrintBool(Arm64Reg.X0); break;
            case TextType:    EmitPrintText(Arm64Reg.X0); break;
        }

        foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X0, 0)) Emit(insn);
        EmitSyscallExit();
    }

    // -- Call patching --------------------------------------------

    void EmitCallTo(string targetName)
    {
        m_callPatches.Add((m_instructions.Count, targetName));
        Emit(Arm64Encoder.Nop());
    }

    void PatchCalls()
    {
        foreach ((int insnIndex, string target) in m_callPatches)
        {
            if (m_functionOffsets.TryGetValue(target, out int targetIndex))
                m_instructions[insnIndex] = Arm64Encoder.Bl((targetIndex - insnIndex) * 4);
            else
                Console.Error.WriteLine($"ARM64 WARNING: unresolved call to '{target}' at {insnIndex}");
        }

        int textSizeBytes = m_instructions.Count * 4;
        ulong rodataVaddr = ElfWriterArm64.ComputeRodataVaddr(textSizeBytes);

        foreach (RodataFixup fixup in m_rodataFixups)
        {
            long addr = (long)(rodataVaddr + (ulong)fixup.RodataOffset);
            uint[] insns = Arm64Encoder.Li(fixup.Register, addr);
            for (int i = 0; i < 2 && i < insns.Length; i++)
                m_instructions[fixup.InstructionIndex + i] = insns[i];
        }

        ulong textVaddr = 0x400000UL + (ulong)ElfWriterArm64.ComputeTextFileOffset();
        foreach (FuncAddrFixup fixup in m_funcAddrFixups)
        {
            if (m_functionOffsets.TryGetValue(fixup.FunctionName, out int funcIndex))
            {
                long funcAddr = (long)(textVaddr + (ulong)(funcIndex * 4));
                uint[] insns = Arm64Encoder.Li(fixup.Register, funcAddr);
                for (int i = 0; i < 2 && i < insns.Length; i++)
                    m_instructions[fixup.InstructionIndex + i] = insns[i];
            }
        }
    }

    // -- Register allocation --------------------------------------

    uint AllocTemp()
    {
        uint reg = m_nextTemp;
        m_nextTemp++;
        if (m_nextTemp > Arm64Reg.X15) m_nextTemp = Arm64Reg.X12;
        return reg;
    }

    const uint SpillBase = 32;

    uint AllocLocal()
    {
        if (m_nextLocal <= Arm64Reg.X27)
        {
            uint reg = m_nextLocal;
            m_nextLocal++;
            return reg;
        }
        uint slot = SpillBase + (uint)m_spillCount;
        m_spillCount++;
        return slot;
    }

    int SpillOffset(uint virtualReg) => 96 + ((int)(virtualReg - SpillBase)) * 8;

    void StoreLocal(uint local, uint valueReg)
    {
        if (local < SpillBase)
            Emit(Arm64Encoder.Mov(local, valueReg));
        else
            EmitSpillStore(valueReg, local);
    }

    uint m_loadLocalToggle;

    uint LoadLocal(uint local)
    {
        if (local < SpillBase)
            return local;
        uint scratch = (m_loadLocalToggle++ % 2 == 0) ? Arm64Reg.X9 : Arm64Reg.X10;
        EmitSpillLoad(scratch, local);
        return scratch;
    }

    void EmitSpillStore(uint valueReg, uint slot)
    {
        int offset = SpillOffset(slot);
        if (offset >= 0 && offset <= 32760 && offset % 8 == 0)
        {
            Emit(Arm64Encoder.Str(valueReg, Arm64Reg.Sp, offset));
        }
        else
        {
            foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X11, offset)) Emit(insn);
            Emit(Arm64Encoder.Add(Arm64Reg.X11, Arm64Reg.Sp, Arm64Reg.X11));
            Emit(Arm64Encoder.Str(valueReg, Arm64Reg.X11, 0));
        }
    }

    void EmitSpillLoad(uint rd, uint slot)
    {
        int offset = SpillOffset(slot);
        if (offset >= 0 && offset <= 32760 && offset % 8 == 0)
        {
            Emit(Arm64Encoder.Ldr(rd, Arm64Reg.Sp, offset));
        }
        else
        {
            foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X11, offset)) Emit(insn);
            Emit(Arm64Encoder.Add(Arm64Reg.X11, Arm64Reg.Sp, Arm64Reg.X11));
            Emit(Arm64Encoder.Ldr(rd, Arm64Reg.X11, 0));
        }
    }

    void Emit(uint instruction) => m_instructions.Add(instruction);

    static CodexType ComputeReturnType(CodexType type, int paramCount)
    {
        CodexType current = type;
        for (int i = 0; i < paramCount; i++)
        {
            if (current is FunctionType ft) current = ft.Return;
            else break;
        }
        return current;
    }
}

sealed record RodataFixup(int InstructionIndex, uint Register, int RodataOffset);
sealed record FuncAddrFixup(int InstructionIndex, uint Register, string FunctionName);
