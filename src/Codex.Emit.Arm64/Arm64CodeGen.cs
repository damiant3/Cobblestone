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
    readonly Dictionary<string, string> m_escapeHelperNames = [];
    readonly Queue<(string Key, string Name, CodexType Type)> m_escapeHelperQueue = [];
    Map<string, CodexType> m_typeDefs = Map<string, CodexType>.s_empty;

    // x28 is reserved as the global heap pointer (callee-saved).
    const uint HeapReg = Arm64Reg.X28;

    // Temps: x9--x15 (caller-saved). We rotate through x12--x15 for AllocTemp.
    uint m_nextTemp = Arm64Reg.X12;
    // Locals: x19--x27 (callee-saved, x28=heap). Monotonic allocation.
    uint m_nextLocal = Arm64Reg.X19;
    int m_spillCount;
    int m_prologueIndex = -1;
    Dictionary<string, uint> m_locals = [];
    string m_currentFunction = "";

    // TCO
    bool m_inTCOFunction;
    bool m_inTailPosition;
    int m_tcoLoopTop;
    uint[] m_tcoParamLocals = [];
    uint[] m_tcoTempLocals = [];
    int m_tcoSavedNextLocal;
    uint m_tcoSavedNextTemp;

    static readonly uint[] CalleeSaved = {
        Arm64Reg.X19, Arm64Reg.X20, Arm64Reg.X21, Arm64Reg.X22,
        Arm64Reg.X23, Arm64Reg.X24, Arm64Reg.X25, Arm64Reg.X26,
        Arm64Reg.X27
    };

    public void EmitModule(IRChapter module)
    {
        m_typeDefs = module.TypeDefinitions;
        m_escapeHelperNames["text"] = "__escape_text";

        EmitRuntimeHelpers();

        foreach (IRDefinition def in module.Definitions)
            EmitFunction(def);

        EmitEscapeCopyHelpers();
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

    static bool ShouldTCO(IRDefinition def)
    {
        return def.Parameters.Length > 0 && HasTailCall(def.Body, def.Name);
    }
    static bool HasTailCall(IRExpr expr, string fn) => expr switch
    {
        IRIf iff => HasTailCall(iff.Then, fn) || HasTailCall(iff.Else, fn),
        IRLet let => HasTailCall(let.Body, fn),
        IRMatch m => m.Branches.Any(b => HasTailCall(b.Body, fn)),
        IRRegion region => HasTailCall(region.Body, fn),
        IRApply app => IsSelfCall(app, fn),
        IRDo d => d.Statements.Length > 0 && d.Statements[^1] is IRDoExec e && HasTailCall(e.Expression, fn),
        _ => false
    };
    static bool IsSelfCall(IRExpr expr, string fn)
    {
        IRExpr c = expr; while (c is IRApply a) c = a.Function;
        return c is IRName n && n.Name == fn;
    }

    void EmitFunction(IRDefinition def)
    {
        m_functionOffsets[def.Name] = m_instructions.Count;
        m_currentFunction = def.Name;
        m_locals = new Dictionary<string, uint>();
        m_nextTemp = Arm64Reg.X12;
        m_nextLocal = Arm64Reg.X19;
        m_spillCount = 0;
        m_inTCOFunction = ShouldTCO(def);

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
        m_tcoParamLocals = new uint[def.Parameters.Length];
        for (int i = 0; i < def.Parameters.Length && i < 8; i++)
        {
            uint savedReg = AllocLocal();
            StoreLocal(savedReg, Arm64Reg.X0 + (uint)i);
            m_locals[def.Parameters[i].Name] = savedReg;
            m_tcoParamLocals[i] = savedReg;
        }
        if (m_inTCOFunction)
        {
            m_tcoTempLocals = new uint[def.Parameters.Length];
            for (int i = 0; i < def.Parameters.Length; i++)
                m_tcoTempLocals[i] = AllocLocal();
        }
        m_tcoLoopTop = m_instructions.Count;
        m_tcoSavedNextLocal = (int)m_nextLocal;
        m_tcoSavedNextTemp = m_nextTemp;
        m_inTailPosition = m_inTCOFunction;

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
        // Binary operands are NEVER in tail position — the result is consumed
        // by the operator.  Without this, a self-recursive call inside `++`
        // would be mis-identified as a tail call and jump instead of returning.
        bool savedTail = m_inTailPosition;
        m_inTailPosition = false;

        uint left = EmitExpr(bin.Left);
        uint savedLeft = AllocLocal();
        StoreLocal(savedLeft, left);

        uint right = EmitExpr(bin.Right);
        m_inTailPosition = savedTail;
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
        bool savedTail = m_inTailPosition;
        m_inTailPosition = false;
        uint cond = EmitExpr(ifExpr.Condition);

        int cbzIndex = m_instructions.Count;
        Emit(Arm64Encoder.Nop());

        m_inTailPosition = savedTail;
        uint thenReg = EmitExpr(ifExpr.Then);
        uint resultReg = AllocLocal();
        StoreLocal(resultReg, thenReg);

        int jEndIndex = m_instructions.Count;
        Emit(Arm64Encoder.Nop());

        int elseStart = m_instructions.Count;
        m_inTailPosition = savedTail;
        uint elseReg = EmitExpr(ifExpr.Else);
        StoreLocal(resultReg, elseReg);

        int endIndex = m_instructions.Count;

        m_instructions[cbzIndex] = Arm64Encoder.Cbz(cond, (elseStart - cbzIndex) * 4);
        m_instructions[jEndIndex] = Arm64Encoder.B((endIndex - jEndIndex) * 4);

        return LoadLocal(resultReg);
    }

    uint EmitLet(IRLet letExpr)
    {
        bool savedTail = m_inTailPosition;
        m_inTailPosition = false;
        uint valReg = EmitExpr(letExpr.Value);
        uint savedReg = AllocLocal();
        StoreLocal(savedReg, valReg);
        m_locals[letExpr.Name] = savedReg;
        m_inTailPosition = savedTail;
        return EmitExpr(letExpr.Body);
    }

    void EmitArm64TailCall(IRApply app)
    {
        List<IRExpr> args = new();
        IRExpr cur = app;
        while (cur is IRApply a) { args.Insert(0, a.Argument); cur = a.Function; }

        m_nextLocal = (uint)m_tcoSavedNextLocal;
        m_nextTemp = m_tcoSavedNextTemp;

        for (int i = 0; i < args.Count && i < m_tcoTempLocals.Length; i++)
        {
            bool saved = m_inTailPosition; m_inTailPosition = false;
            uint r = EmitExpr(args[i]);
            m_inTailPosition = saved;
            StoreLocal(m_tcoTempLocals[i], r);
        }
        for (int i = 0; i < args.Count && i < m_tcoParamLocals.Length; i++)
        {
            uint val = LoadLocal(m_tcoTempLocals[i]);
            StoreLocal(m_tcoParamLocals[i], val);
        }
        Emit(Arm64Encoder.B((m_tcoLoopTop - m_instructions.Count) * 4));
    }

    uint EmitApply(IRApply apply)
    {
        if (m_inTCOFunction && m_inTailPosition && IsSelfCall(apply, m_currentFunction))
        {
            EmitArm64TailCall(apply);
            uint dummy = AllocTemp();
            foreach (uint insn in Arm64Encoder.Li(dummy, 0)) Emit(insn);
            return dummy;
        }
        // Sub-expressions of a call are NOT in tail position
        bool savedTailPos = m_inTailPosition;
        m_inTailPosition = false;

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
            m_inTailPosition = savedTailPos;
            uint rd = AllocTemp();
            Emit(Arm64Encoder.Mov(rd, Arm64Reg.X0));
            return rd;
        }

        m_inTailPosition = savedTailPos;
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
        bool savedTail = m_inTailPosition;
        m_inTailPosition = false;
        uint scrutReg = EmitExpr(match.Scrutinee);
        m_inTailPosition = savedTail;
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

    uint EmitRegion(IRRegion region)
    {
        // Closures: skip region (capture types unknown at region exit)
        if (region.Type is FunctionType)
            return EmitExpr(region.Body);

        // Heap-returning functions: skip region reclamation.
        // Pattern matching extracts pointers to intermediate heap allocations
        // that are still live in locals — reclaiming corrupts them.
        // Only scalar-returning regions are safe to reclaim.
        if (region.NeedsEscapeCopy)
            return EmitExpr(region.Body);

        // Save heap pointer (region entry)
        uint savedHeap = AllocLocal();
        uint hpTmp = AllocTemp();
        Emit(Arm64Encoder.Mov(hpTmp, HeapReg));
        StoreLocal(savedHeap, hpTmp);

        uint bodyResult = EmitExpr(region.Body);

        // Scalar return — restore HeapReg, value survives in register
        Emit(Arm64Encoder.Mov(HeapReg, LoadLocal(savedHeap)));
        return bodyResult;
    }

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
                // char-at returns byte value as integer: ldrb from [text+8+idx]
                uint textReg = EmitExpr(args[0]);
                uint savedText = AllocLocal();
                StoreLocal(savedText, textReg);
                uint indexReg = EmitExpr(args[1]);
                Emit(Arm64Encoder.Mov(Arm64Reg.X11, indexReg));
                uint textVal = LoadLocal(savedText);
                Emit(Arm64Encoder.Add(Arm64Reg.X9, textVal, Arm64Reg.X11));
                Emit(Arm64Encoder.Ldrb(Arm64Reg.X0, Arm64Reg.X9, 8));
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
                // char-code: identity — Char is already an integer
                uint charReg = EmitExpr(args[0]);
                Emit(Arm64Encoder.Mov(Arm64Reg.X0, charReg));
                return true;
            }

            case "code-to-char" when args.Count == 1:
            {
                // code-to-char: identity — Char is already an integer
                uint codeReg = EmitExpr(args[0]);
                Emit(Arm64Encoder.Mov(Arm64Reg.X0, codeReg));
                return true;
            }

            case "char-to-text" when args.Count == 1:
            {
                // Allocate 1-char string on heap: [len=1][byte]
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
                // CCE: letters are 13-64 (lowercase 13-38, uppercase 39-64)
                // Single range check: (val - 13) <= 51
                uint charReg = EmitExpr(args[0]);
                Emit(Arm64Encoder.Mov(Arm64Reg.X9, charReg));
                foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X10, 13)) Emit(insn);
                Emit(Arm64Encoder.Sub(Arm64Reg.X11, Arm64Reg.X9, Arm64Reg.X10));
                Emit(Arm64Encoder.CmpImm(Arm64Reg.X11, 51));
                m_instructions.Add(0x9A9F87E0u | Arm64Reg.X0); // CSINC X0, XZR, XZR, HI (1 if LS)
                return true;
            }

            case "is-digit" when args.Count == 1:
            {
                // CCE: digits are 3-12
                // (val - 3) <= 9
                uint charReg = EmitExpr(args[0]);
                Emit(Arm64Encoder.Mov(Arm64Reg.X9, charReg));
                foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X10, 3)) Emit(insn);
                Emit(Arm64Encoder.Sub(Arm64Reg.X11, Arm64Reg.X9, Arm64Reg.X10));
                Emit(Arm64Encoder.CmpImm(Arm64Reg.X11, 9));
                m_instructions.Add(0x9A9F87E0u | Arm64Reg.X0); // CSINC X0, XZR, XZR, HI (1 if LS)
                return true;
            }

            case "is-whitespace" when args.Count == 1:
            {
                // CCE: whitespace is 0-2 (NUL, LF, Space)
                // val <= 2
                uint charReg = EmitExpr(args[0]);
                Emit(Arm64Encoder.Mov(Arm64Reg.X9, charReg));
                Emit(Arm64Encoder.CmpImm(Arm64Reg.X9, 2));
                m_instructions.Add(0x9A9F87E0u | Arm64Reg.X0); // CSINC X0, XZR, XZR, HI (1 if LS)
                return true;
            }

            case "negate" when args.Count == 1:
            {
                uint val = EmitExpr(args[0]);
                Emit(Arm64Encoder.Sub(Arm64Reg.X0, Arm64Reg.Xzr, val));
                return true;
            }

            case "text-contains" when args.Count == 2:
            {
                uint textReg = EmitExpr(args[0]);
                uint savedText = AllocLocal();
                StoreLocal(savedText, textReg);
                uint needleReg = EmitExpr(args[1]);
                Emit(Arm64Encoder.Mov(Arm64Reg.X1, needleReg));
                Emit(Arm64Encoder.Mov(Arm64Reg.X0, LoadLocal(savedText)));
                EmitCallTo("__text_contains");
                return true;
            }

            case "text-starts-with" when args.Count == 2:
            {
                uint textReg = EmitExpr(args[0]);
                uint savedText = AllocLocal();
                StoreLocal(savedText, textReg);
                uint prefixReg = EmitExpr(args[1]);
                Emit(Arm64Encoder.Mov(Arm64Reg.X1, prefixReg));
                Emit(Arm64Encoder.Mov(Arm64Reg.X0, LoadLocal(savedText)));
                EmitCallTo("__text_starts_with");
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

            case "fork" when args.Count == 1:
            {
                // Sequential fork: evaluate thunk, store result in task slot
                uint thunk = EmitExpr(args[0]);
                uint savedThunk = AllocLocal();
                StoreLocal(savedThunk, thunk);

                // Allocate task: [8B done_flag] [8B result]
                uint taskPtr = AllocTemp();
                Emit(Arm64Encoder.Mov(taskPtr, HeapReg));
                Emit(Arm64Encoder.AddImm(HeapReg, HeapReg, 16));
                foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X9, 0)) Emit(insn);
                Emit(Arm64Encoder.Str(Arm64Reg.X9, taskPtr, 0)); // done = 0
                Emit(Arm64Encoder.Str(Arm64Reg.X9, taskPtr, 8)); // result = 0 (byte offset 8)
                uint savedTask = AllocLocal();
                StoreLocal(savedTask, taskPtr);

                // Call thunk(null): thunk is a closure [code_ptr, caps...], load code ptr then call
                // Trampoline expects X11 = closure pointer (for captured arg access)
                uint thunkLoaded = LoadLocal(savedThunk);
                foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X0, 0)) Emit(insn);
                Emit(Arm64Encoder.Mov(Arm64Reg.X11, thunkLoaded)); // X11 = closure (trampoline convention)
                Emit(Arm64Encoder.Ldr(Arm64Reg.X9, thunkLoaded, 0)); // X9 = [thunk+0] = code ptr
                Emit(Arm64Encoder.Blr(Arm64Reg.X9));

                // Store result (X0) into task[8], set done
                uint taskLoaded = LoadLocal(savedTask);
                Emit(Arm64Encoder.Str(Arm64Reg.X0, taskLoaded, 8)); // task[8] = result
                foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X9, 1)) Emit(insn);
                Emit(Arm64Encoder.Str(Arm64Reg.X9, taskLoaded, 0)); // task[0] = 1

                // TryEmitBuiltin caller expects result in X0
                Emit(Arm64Encoder.Mov(Arm64Reg.X0, taskLoaded));
                return true;
            }

            case "await" when args.Count == 1:
            {
                // Sequential: just load result from task[8]
                uint taskPtr = EmitExpr(args[0]);
                Emit(Arm64Encoder.Ldr(Arm64Reg.X0, taskPtr, 8)); // byte offset 8
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
        EmitTextContainsHelper();
        EmitTextStartsWithHelper();
        EmitEscapeTextHelper();
    }

    void EmitTextContainsHelper()
    {
        // __text_contains: x0=text, x1=needle → x0=1/0
        m_functionOffsets["__text_contains"] = m_instructions.Count;

        Emit(Arm64Encoder.SubImm(Arm64Reg.Sp, Arm64Reg.Sp, 48));
        Emit(Arm64Encoder.Stp(Arm64Reg.Fp, Arm64Reg.Lr, Arm64Reg.Sp, 0));
        Emit(Arm64Encoder.Mov(Arm64Reg.Fp, Arm64Reg.Sp));
        // x19=text, x20=needle, x21=text_len, x22=needle_len, x23=i
        Emit(Arm64Encoder.Stp(Arm64Reg.X19, Arm64Reg.X20, Arm64Reg.Sp, 16));
        Emit(Arm64Encoder.Stp(Arm64Reg.X21, Arm64Reg.X22, Arm64Reg.Sp, 32));

        Emit(Arm64Encoder.Mov(Arm64Reg.X19, Arm64Reg.X0));
        Emit(Arm64Encoder.Mov(Arm64Reg.X20, Arm64Reg.X1));
        Emit(Arm64Encoder.Ldr(Arm64Reg.X21, Arm64Reg.X19, 0));
        Emit(Arm64Encoder.Ldr(Arm64Reg.X22, Arm64Reg.X20, 0));
        Emit(Arm64Encoder.Mov(Arm64Reg.X23, Arm64Reg.Xzr)); // i=0

        // Outer: i <= text_len - needle_len
        int outer = m_instructions.Count;
        Emit(Arm64Encoder.Sub(Arm64Reg.X9, Arm64Reg.X21, Arm64Reg.X22));
        Emit(Arm64Encoder.AddImm(Arm64Reg.X9, Arm64Reg.X9, 1));
        Emit(Arm64Encoder.Cmp(Arm64Reg.X23, Arm64Reg.X9));
        int notFoundBr = m_instructions.Count;
        Emit(Arm64Encoder.Nop()); // B.GE → not found

        // Inner: j from 0 to needle_len
        Emit(Arm64Encoder.Mov(Arm64Reg.X10, Arm64Reg.Xzr)); // j=0
        int inner = m_instructions.Count;
        Emit(Arm64Encoder.Cmp(Arm64Reg.X10, Arm64Reg.X22));
        int foundBr = m_instructions.Count;
        Emit(Arm64Encoder.Nop()); // B.GE → found

        Emit(Arm64Encoder.Add(Arm64Reg.X11, Arm64Reg.X19, Arm64Reg.X23));
        Emit(Arm64Encoder.Add(Arm64Reg.X11, Arm64Reg.X11, Arm64Reg.X10));
        Emit(Arm64Encoder.Ldrb(Arm64Reg.X11, Arm64Reg.X11, 8));
        Emit(Arm64Encoder.Add(Arm64Reg.X12, Arm64Reg.X20, Arm64Reg.X10));
        Emit(Arm64Encoder.Ldrb(Arm64Reg.X12, Arm64Reg.X12, 8));
        Emit(Arm64Encoder.Cmp(Arm64Reg.X11, Arm64Reg.X12));
        int mismatchBr = m_instructions.Count;
        Emit(Arm64Encoder.Nop()); // B.NE → mismatch
        Emit(Arm64Encoder.AddImm(Arm64Reg.X10, Arm64Reg.X10, 1));
        Emit(Arm64Encoder.B((inner - m_instructions.Count) * 4));

        // Found
        int foundLbl = m_instructions.Count;
        m_instructions[foundBr] = Arm64Encoder.Bge(foundLbl - foundBr);
        foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X0, 1)) Emit(insn);
        int doneBr = m_instructions.Count;
        Emit(Arm64Encoder.Nop()); // B → done

        // Mismatch
        int mismatchLbl = m_instructions.Count;
        m_instructions[mismatchBr] = Arm64Encoder.Bne(mismatchLbl - mismatchBr);
        Emit(Arm64Encoder.AddImm(Arm64Reg.X23, Arm64Reg.X23, 1));
        Emit(Arm64Encoder.B((outer - m_instructions.Count) * 4));

        // Not found
        int notFoundLbl = m_instructions.Count;
        m_instructions[notFoundBr] = Arm64Encoder.Bge(notFoundLbl - notFoundBr);
        foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X0, 0)) Emit(insn);

        // Done
        int doneLbl = m_instructions.Count;
        m_instructions[doneBr] = Arm64Encoder.B((doneLbl - doneBr) * 4);
        Emit(Arm64Encoder.Ldp(Arm64Reg.X21, Arm64Reg.X22, Arm64Reg.Sp, 32));
        Emit(Arm64Encoder.Ldp(Arm64Reg.X19, Arm64Reg.X20, Arm64Reg.Sp, 16));
        Emit(Arm64Encoder.Ldp(Arm64Reg.Fp, Arm64Reg.Lr, Arm64Reg.Sp, 0));
        Emit(Arm64Encoder.AddImm(Arm64Reg.Sp, Arm64Reg.Sp, 48));
        Emit(Arm64Encoder.Ret());
    }

    void EmitTextStartsWithHelper()
    {
        // __text_starts_with: x0=text, x1=prefix → x0=1/0
        m_functionOffsets["__text_starts_with"] = m_instructions.Count;

        Emit(Arm64Encoder.Ldr(Arm64Reg.X9, Arm64Reg.X0, 0));  // text_len
        Emit(Arm64Encoder.Ldr(Arm64Reg.X10, Arm64Reg.X1, 0)); // prefix_len

        // prefix_len > text_len → false
        Emit(Arm64Encoder.Cmp(Arm64Reg.X9, Arm64Reg.X10));
        int tooLongBr = m_instructions.Count;
        Emit(Arm64Encoder.Nop()); // B.LT → false

        Emit(Arm64Encoder.Mov(Arm64Reg.X11, Arm64Reg.Xzr)); // i=0

        int loop = m_instructions.Count;
        Emit(Arm64Encoder.Cmp(Arm64Reg.X11, Arm64Reg.X10));
        int matchedBr = m_instructions.Count;
        Emit(Arm64Encoder.Nop()); // B.GE → true

        Emit(Arm64Encoder.Add(Arm64Reg.X12, Arm64Reg.X0, Arm64Reg.X11));
        Emit(Arm64Encoder.Ldrb(Arm64Reg.X12, Arm64Reg.X12, 8));
        Emit(Arm64Encoder.Add(Arm64Reg.X13, Arm64Reg.X1, Arm64Reg.X11));
        Emit(Arm64Encoder.Ldrb(Arm64Reg.X13, Arm64Reg.X13, 8));
        Emit(Arm64Encoder.Cmp(Arm64Reg.X12, Arm64Reg.X13));
        int mismatchBr = m_instructions.Count;
        Emit(Arm64Encoder.Nop()); // B.NE → false
        Emit(Arm64Encoder.AddImm(Arm64Reg.X11, Arm64Reg.X11, 1));
        Emit(Arm64Encoder.B((loop - m_instructions.Count) * 4));

        // Matched
        int matchedLbl = m_instructions.Count;
        m_instructions[matchedBr] = Arm64Encoder.Bge(matchedLbl - matchedBr);
        foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X0, 1)) Emit(insn);
        Emit(Arm64Encoder.Ret());

        // False
        int falseLbl = m_instructions.Count;
        m_instructions[tooLongBr] = Arm64Encoder.Blt(falseLbl - tooLongBr);
        m_instructions[mismatchBr] = Arm64Encoder.Bne(falseLbl - mismatchBr);
        foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X0, 0)) Emit(insn);
        Emit(Arm64Encoder.Ret());
    }

    // __escape_text: x0=old text ptr → x0=new text ptr
    // Layout: [len:8B][data:len bytes, padded to 8]
    void EmitEscapeTextHelper()
    {
        m_functionOffsets["__escape_text"] = m_instructions.Count;
        // Null guard: if non-null, skip to copy code; null falls through to return 0
        Emit(Arm64Encoder.Cbnz(Arm64Reg.X0, 3 * 4));
        foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X0, 0)) Emit(insn);
        Emit(Arm64Encoder.Ret());
        // Save LR + x19-x21
        Emit(Arm64Encoder.SubImm(Arm64Reg.Sp, Arm64Reg.Sp, 32));
        Emit(Arm64Encoder.Str(Arm64Reg.Lr, Arm64Reg.Sp, 0));
        Emit(Arm64Encoder.Stp(Arm64Reg.X19, Arm64Reg.X20, Arm64Reg.Sp, 16));
        Emit(Arm64Encoder.Mov(Arm64Reg.X19, Arm64Reg.X0)); // x19 = old ptr
        // x20 = length
        Emit(Arm64Encoder.Ldr(Arm64Reg.X20, Arm64Reg.X19, 0));
        // Allocate: totalBytes = align8(8 + len)
        Emit(Arm64Encoder.AddImm(Arm64Reg.X9, Arm64Reg.X20, 8 + 7));
        Emit(Arm64Encoder.AndImm(Arm64Reg.X9, Arm64Reg.X9, -8));
        // new ptr = HeapReg; HeapReg += totalBytes
        Emit(Arm64Encoder.Mov(Arm64Reg.X0, HeapReg));
        Emit(Arm64Encoder.Add(HeapReg, HeapReg, Arm64Reg.X9));
        // Store length
        Emit(Arm64Encoder.Str(Arm64Reg.X20, Arm64Reg.X0, 0));
        // Copy bytes: index in x9
        foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X9, 0)) Emit(insn);
        int loopStart = m_instructions.Count;
        Emit(Arm64Encoder.Cmp(Arm64Reg.X9, Arm64Reg.X20));
        int exitIdx = m_instructions.Count;
        Emit(Arm64Encoder.Nop()); // B.GE -> done
        // Load byte from old[8+i], store to new[8+i]
        Emit(Arm64Encoder.AddImm(Arm64Reg.X10, Arm64Reg.X19, 8));
        Emit(Arm64Encoder.LdrbReg(Arm64Reg.X11, Arm64Reg.X10, Arm64Reg.X9));
        Emit(Arm64Encoder.AddImm(Arm64Reg.X10, Arm64Reg.X0, 8));
        Emit(Arm64Encoder.StrbReg(Arm64Reg.X11, Arm64Reg.X10, Arm64Reg.X9));
        Emit(Arm64Encoder.AddImm(Arm64Reg.X9, Arm64Reg.X9, 1));
        Emit(Arm64Encoder.B((loopStart - m_instructions.Count) * 4));
        m_instructions[exitIdx] = Arm64Encoder.Bcond(Arm64Encoder.CondGe,
            (m_instructions.Count - exitIdx) * 4);
        // Restore and return (x0 already has new ptr)
        Emit(Arm64Encoder.Ldr(Arm64Reg.Lr, Arm64Reg.Sp, 0));
        Emit(Arm64Encoder.Ldp(Arm64Reg.X19, Arm64Reg.X20, Arm64Reg.Sp, 16));
        Emit(Arm64Encoder.AddImm(Arm64Reg.Sp, Arm64Reg.Sp, 32));
        Emit(Arm64Encoder.Ret());
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

        // Copy second string — byte by byte, same pattern as first loop
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
        // __str_replace: x0=text, x1=old, x2=new → x0=result string
        // Scans text for occurrences of old, replaces with new.
        m_functionOffsets["__str_replace"] = m_instructions.Count;

        // Frame: 80 bytes — save LR + 8 slots
        Emit(Arm64Encoder.SubImm(Arm64Reg.Sp, Arm64Reg.Sp, 80));
        Emit(Arm64Encoder.Str(Arm64Reg.Lr, Arm64Reg.Sp, 72));
        Emit(Arm64Encoder.Str(Arm64Reg.X0, Arm64Reg.Sp, 0));   // [sp+0]  = text
        Emit(Arm64Encoder.Str(Arm64Reg.X1, Arm64Reg.Sp, 8));   // [sp+8]  = old
        Emit(Arm64Encoder.Str(Arm64Reg.X2, Arm64Reg.Sp, 16));  // [sp+16] = new

        // Load lengths
        Emit(Arm64Encoder.Ldr(Arm64Reg.X9, Arm64Reg.X0, 0));   // x9  = text_len
        Emit(Arm64Encoder.Ldr(Arm64Reg.X10, Arm64Reg.X1, 0));  // x10 = old_len
        Emit(Arm64Encoder.Ldr(Arm64Reg.X11, Arm64Reg.X2, 0));  // x11 = new_len
        Emit(Arm64Encoder.Str(Arm64Reg.X9, Arm64Reg.Sp, 24));  // [sp+24] = text_len
        Emit(Arm64Encoder.Str(Arm64Reg.X10, Arm64Reg.Sp, 32)); // [sp+32] = old_len
        Emit(Arm64Encoder.Str(Arm64Reg.X11, Arm64Reg.Sp, 40)); // [sp+40] = new_len

        // Result buffer at current heap ptr
        Emit(Arm64Encoder.Mov(Arm64Reg.X12, HeapReg));          // x12 = result base
        Emit(Arm64Encoder.Str(Arm64Reg.X12, Arm64Reg.Sp, 48)); // [sp+48] = result base
        foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X13, 0)) Emit(insn); // x13 = out_len
        foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X14, 0)) Emit(insn); // x14 = source index i

        // ── Main loop ──
        int mainLoop = m_instructions.Count;

        // if i >= text_len → done
        Emit(Arm64Encoder.Ldr(Arm64Reg.X9, Arm64Reg.Sp, 24));
        Emit(Arm64Encoder.Cmp(Arm64Reg.X14, Arm64Reg.X9));
        int doneCheck = m_instructions.Count;
        Emit(Arm64Encoder.Nop()); // B.GE → done

        // Check if old_len == 0 → skip match
        Emit(Arm64Encoder.Ldr(Arm64Reg.X10, Arm64Reg.Sp, 32));
        int skipMatch = m_instructions.Count;
        Emit(Arm64Encoder.Nop()); // CBZ → no_match

        // Check if i + old_len > text_len → can't match
        Emit(Arm64Encoder.Add(Arm64Reg.X11, Arm64Reg.X14, Arm64Reg.X10));
        Emit(Arm64Encoder.Cmp(Arm64Reg.X11, Arm64Reg.X9));
        int cantMatch = m_instructions.Count;
        Emit(Arm64Encoder.Nop()); // B.GT → no_match

        // Compare text[i..i+old_len] with old
        foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X15, 0)) Emit(insn); // j = 0
        Emit(Arm64Encoder.Ldr(Arm64Reg.X0, Arm64Reg.Sp, 0));   // text_ptr
        Emit(Arm64Encoder.Ldr(Arm64Reg.X1, Arm64Reg.Sp, 8));   // old_ptr

        int cmpLoop = m_instructions.Count;
        // if j >= old_len → full match
        Emit(Arm64Encoder.Cmp(Arm64Reg.X15, Arm64Reg.X10));
        int matchFound = m_instructions.Count;
        Emit(Arm64Encoder.Nop()); // B.GE → match

        // Load text[i+j+8] and old[j+8]
        Emit(Arm64Encoder.Add(Arm64Reg.X11, Arm64Reg.X14, Arm64Reg.X15)); // i + j
        Emit(Arm64Encoder.Add(Arm64Reg.X11, Arm64Reg.X0, Arm64Reg.X11)); // text + i + j
        Emit(Arm64Encoder.Ldrb(Arm64Reg.X11, Arm64Reg.X11, 8));          // text_data[i+j]
        Emit(Arm64Encoder.Add(Arm64Reg.X3, Arm64Reg.X1, Arm64Reg.X15)); // old + j
        Emit(Arm64Encoder.Ldrb(Arm64Reg.X3, Arm64Reg.X3, 8));            // old_data[j]

        Emit(Arm64Encoder.Cmp(Arm64Reg.X11, Arm64Reg.X3));
        int mismatch = m_instructions.Count;
        Emit(Arm64Encoder.Nop()); // B.NE → no_match

        Emit(Arm64Encoder.AddImm(Arm64Reg.X15, Arm64Reg.X15, 1));
        Emit(Arm64Encoder.B((cmpLoop - m_instructions.Count) * 4));

        // ── Match found: copy new_str bytes to output ──
        int matchLabel = m_instructions.Count;
        m_instructions[matchFound] = Arm64Encoder.Bcond(Arm64Encoder.CondGe,
            (matchLabel - matchFound) * 4);

        Emit(Arm64Encoder.Ldr(Arm64Reg.X2, Arm64Reg.Sp, 16));  // new_ptr
        Emit(Arm64Encoder.Ldr(Arm64Reg.X11, Arm64Reg.Sp, 40)); // new_len
        foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X15, 0)) Emit(insn); // j = 0
        Emit(Arm64Encoder.Ldr(Arm64Reg.X12, Arm64Reg.Sp, 48)); // result base

        int copyNewLoop = m_instructions.Count;
        Emit(Arm64Encoder.Cmp(Arm64Reg.X15, Arm64Reg.X11));
        int copyNewExit = m_instructions.Count;
        Emit(Arm64Encoder.Nop()); // B.GE → done copying

        Emit(Arm64Encoder.Add(Arm64Reg.X3, Arm64Reg.X2, Arm64Reg.X15));
        Emit(Arm64Encoder.Ldrb(Arm64Reg.X3, Arm64Reg.X3, 8));            // new_data[j]
        Emit(Arm64Encoder.Add(Arm64Reg.X4, Arm64Reg.X12, Arm64Reg.X13));
        Emit(Arm64Encoder.Strb(Arm64Reg.X3, Arm64Reg.X4, 8));            // out[out_len]
        Emit(Arm64Encoder.AddImm(Arm64Reg.X13, Arm64Reg.X13, 1));
        Emit(Arm64Encoder.AddImm(Arm64Reg.X15, Arm64Reg.X15, 1));
        Emit(Arm64Encoder.B((copyNewLoop - m_instructions.Count) * 4));

        int copyNewTarget = m_instructions.Count;
        m_instructions[copyNewExit] = Arm64Encoder.Bcond(Arm64Encoder.CondGe,
            (copyNewTarget - copyNewExit) * 4);

        // Advance source by old_len
        Emit(Arm64Encoder.Ldr(Arm64Reg.X10, Arm64Reg.Sp, 32));
        Emit(Arm64Encoder.Add(Arm64Reg.X14, Arm64Reg.X14, Arm64Reg.X10));
        Emit(Arm64Encoder.B((mainLoop - m_instructions.Count) * 4));

        // ── No match: copy one byte ──
        int noMatchLabel = m_instructions.Count;
        m_instructions[skipMatch] = Arm64Encoder.Cbz(Arm64Reg.X10,
            (noMatchLabel - skipMatch) * 4);
        m_instructions[cantMatch] = Arm64Encoder.Bcond(Arm64Encoder.CondGt,
            (noMatchLabel - cantMatch) * 4);
        m_instructions[mismatch] = Arm64Encoder.Bcond(Arm64Encoder.CondNe,
            (noMatchLabel - mismatch) * 4);

        Emit(Arm64Encoder.Ldr(Arm64Reg.X0, Arm64Reg.Sp, 0));    // text_ptr
        Emit(Arm64Encoder.Add(Arm64Reg.X9, Arm64Reg.X0, Arm64Reg.X14));
        Emit(Arm64Encoder.Ldrb(Arm64Reg.X9, Arm64Reg.X9, 8));   // text_data[i]
        Emit(Arm64Encoder.Ldr(Arm64Reg.X12, Arm64Reg.Sp, 48));  // result base
        Emit(Arm64Encoder.Add(Arm64Reg.X10, Arm64Reg.X12, Arm64Reg.X13));
        Emit(Arm64Encoder.Strb(Arm64Reg.X9, Arm64Reg.X10, 8));  // out[out_len]
        Emit(Arm64Encoder.AddImm(Arm64Reg.X13, Arm64Reg.X13, 1));
        Emit(Arm64Encoder.AddImm(Arm64Reg.X14, Arm64Reg.X14, 1));
        Emit(Arm64Encoder.B((mainLoop - m_instructions.Count) * 4));

        // ── Done ──
        int doneLabel = m_instructions.Count;
        m_instructions[doneCheck] = Arm64Encoder.Bcond(Arm64Encoder.CondGe,
            (doneLabel - doneCheck) * 4);

        // Store length and finalize
        Emit(Arm64Encoder.Ldr(Arm64Reg.X12, Arm64Reg.Sp, 48));  // result base
        Emit(Arm64Encoder.Str(Arm64Reg.X13, Arm64Reg.X12, 0));  // store length

        // Bump heap: HeapReg = result + align8(8 + out_len)
        Emit(Arm64Encoder.AddImm(Arm64Reg.X0, Arm64Reg.X13, 15));
        Emit(Arm64Encoder.AndImm(Arm64Reg.X0, Arm64Reg.X0, -8));
        Emit(Arm64Encoder.Add(HeapReg, Arm64Reg.X12, Arm64Reg.X0));

        Emit(Arm64Encoder.Mov(Arm64Reg.X0, Arm64Reg.X12));      // return result
        Emit(Arm64Encoder.Ldr(Arm64Reg.Lr, Arm64Reg.Sp, 72));
        Emit(Arm64Encoder.AddImm(Arm64Reg.Sp, Arm64Reg.Sp, 80));
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

    // -- Escape copy infrastructure ---------------------------------

    uint EmitEscapeCopy(uint srcLocal, CodexType type)
    {
        CodexType resolved = ResolveType(type);
        if (!IRRegion.TypeNeedsHeapEscape(resolved))
            return LoadLocal(srcLocal);

        string helperName = GetOrQueueEscapeHelper(resolved);
        uint src = LoadLocal(srcLocal);
        Emit(Arm64Encoder.Mov(Arm64Reg.X0, src));
        EmitCallTo(helperName);
        uint result = AllocTemp();
        Emit(Arm64Encoder.Mov(result, Arm64Reg.X0));
        return result;
    }

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
            switch (type)
            {
                case TextType:
                    break; // __escape_text already emitted in runtime helpers
                case RecordType rt:
                    EmitRecordEscapeHelper(name, rt);
                    break;
                case ListType lt:
                    EmitListEscapeHelper(name, lt);
                    break;
                case SumType st:
                    EmitSumTypeEscapeHelper(name, st);
                    break;
            }
        }
    }

    // All escape helpers: X0 = old ptr in, X0 = new ptr out
    // Working regs: X19=old, X20=new, X21=extra1, X22=extra2
    // Frame: 48 bytes (x30, x19, x20, x21, x22 + pad)

    void EmitEscapeHelperPrologue(string name)
    {
        m_functionOffsets[name] = m_instructions.Count;
        // Null guard: if x0 == 0, return 0 immediately
        Emit(Arm64Encoder.Cbz(Arm64Reg.X0, 2 * 4)); // skip ret
        foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X0, 0)) Emit(insn);
        Emit(Arm64Encoder.Ret());
        // Save callee-saved regs + LR
        Emit(Arm64Encoder.SubImm(Arm64Reg.Sp, Arm64Reg.Sp, 48));
        Emit(Arm64Encoder.Str(Arm64Reg.Lr, Arm64Reg.Sp, 0));
        Emit(Arm64Encoder.Stp(Arm64Reg.X19, Arm64Reg.X20, Arm64Reg.Sp, 16));
        Emit(Arm64Encoder.Stp(Arm64Reg.X21, Arm64Reg.X22, Arm64Reg.Sp, 32));
        Emit(Arm64Encoder.Mov(Arm64Reg.X19, Arm64Reg.X0)); // x19 = old ptr
    }

    void EmitEscapeHelperEpilogue()
    {
        Emit(Arm64Encoder.Mov(Arm64Reg.X0, Arm64Reg.X20)); // return new ptr
        Emit(Arm64Encoder.Ldr(Arm64Reg.Lr, Arm64Reg.Sp, 0));
        Emit(Arm64Encoder.Ldp(Arm64Reg.X19, Arm64Reg.X20, Arm64Reg.Sp, 16));
        Emit(Arm64Encoder.Ldp(Arm64Reg.X21, Arm64Reg.X22, Arm64Reg.Sp, 32));
        Emit(Arm64Encoder.AddImm(Arm64Reg.Sp, Arm64Reg.Sp, 48));
        Emit(Arm64Encoder.Ret());
    }

    void EmitEscapeFieldCopy(int srcOffset, int dstOffset, CodexType fieldType)
    {
        CodexType resolved = ResolveType(fieldType);
        if (IRRegion.TypeNeedsHeapEscape(resolved))
        {
            string helper = GetOrQueueEscapeHelper(resolved);
            Emit(Arm64Encoder.Ldr(Arm64Reg.X0, Arm64Reg.X19, srcOffset));
            EmitCallTo(helper);
            Emit(Arm64Encoder.Str(Arm64Reg.X0, Arm64Reg.X20, dstOffset));
        }
        else
        {
            Emit(Arm64Encoder.Ldr(Arm64Reg.X9, Arm64Reg.X19, srcOffset));
            Emit(Arm64Encoder.Str(Arm64Reg.X9, Arm64Reg.X20, dstOffset));
        }
    }

    void EmitRecordEscapeHelper(string name, RecordType rt)
    {
        EmitEscapeHelperPrologue(name);

        int totalSize = rt.Fields.Length * 8;
        Emit(Arm64Encoder.Mov(Arm64Reg.X20, HeapReg));
        Emit(Arm64Encoder.AddImm(HeapReg, HeapReg, totalSize));

        for (int i = 0; i < rt.Fields.Length; i++)
            EmitEscapeFieldCopy(i * 8, i * 8, rt.Fields[i].Type);

        EmitEscapeHelperEpilogue();
    }

    void EmitListEscapeHelper(string name, ListType lt)
    {
        EmitEscapeHelperPrologue(name);

        // x21 = length
        Emit(Arm64Encoder.Ldr(Arm64Reg.X21, Arm64Reg.X19, 0));
        // totalSize = (1 + len) * 8
        Emit(Arm64Encoder.AddImm(Arm64Reg.X9, Arm64Reg.X21, 1));
        foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X10, 3)) Emit(insn);
        Emit(Arm64Encoder.Lsl(Arm64Reg.X9, Arm64Reg.X9, Arm64Reg.X10));
        Emit(Arm64Encoder.Mov(Arm64Reg.X20, HeapReg));
        Emit(Arm64Encoder.Add(HeapReg, HeapReg, Arm64Reg.X9));
        // Store length
        Emit(Arm64Encoder.Str(Arm64Reg.X21, Arm64Reg.X20, 0));

        CodexType elemType = ResolveType(lt.Element);
        bool deepCopy = IRRegion.TypeNeedsHeapEscape(elemType);
        string? elemHelper = deepCopy ? GetOrQueueEscapeHelper(elemType) : null;

        // x22 = index = 0
        foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X22, 0)) Emit(insn);
        int loopStart = m_instructions.Count;
        Emit(Arm64Encoder.Cmp(Arm64Reg.X22, Arm64Reg.X21));
        int exitIdx = m_instructions.Count;
        Emit(Arm64Encoder.Nop()); // B.GE -> exit

        // Load element: x0 = old[8 + index*8]
        foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X10, 3)) Emit(insn);
        Emit(Arm64Encoder.Lsl(Arm64Reg.X9, Arm64Reg.X22, Arm64Reg.X10));
        Emit(Arm64Encoder.Add(Arm64Reg.X9, Arm64Reg.X9, Arm64Reg.X19));
        Emit(Arm64Encoder.Ldr(Arm64Reg.X0, Arm64Reg.X9, 8));

        if (deepCopy)
        {
            EmitCallTo(elemHelper!);
            // x0 = copied element
        }

        // Store to new list: new[8 + index*8] = x0
        foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X10, 3)) Emit(insn);
        Emit(Arm64Encoder.Lsl(Arm64Reg.X9, Arm64Reg.X22, Arm64Reg.X10));
        Emit(Arm64Encoder.Add(Arm64Reg.X9, Arm64Reg.X9, Arm64Reg.X20));
        Emit(Arm64Encoder.Str(Arm64Reg.X0, Arm64Reg.X9, 8));

        Emit(Arm64Encoder.AddImm(Arm64Reg.X22, Arm64Reg.X22, 1));
        Emit(Arm64Encoder.B((loopStart - m_instructions.Count) * 4));
        m_instructions[exitIdx] = Arm64Encoder.Bcond(Arm64Encoder.CondGe,
            (m_instructions.Count - exitIdx) * 4);

        EmitEscapeHelperEpilogue();
    }

    void EmitSumTypeEscapeHelper(string name, SumType st)
    {
        EmitEscapeHelperPrologue(name);

        // x21 = tag
        Emit(Arm64Encoder.Ldr(Arm64Reg.X21, Arm64Reg.X19, 0));

        List<int> jumpToEndIdxs = [];

        for (int ctorIdx = 0; ctorIdx < st.Constructors.Length; ctorIdx++)
        {
            SumConstructorType ctor = st.Constructors[ctorIdx];
            int totalSize = (1 + ctor.Fields.Length) * 8;

            if (ctorIdx < st.Constructors.Length - 1)
            {
                Emit(Arm64Encoder.CmpImm(Arm64Reg.X21, ctorIdx));
                int branchIdx = m_instructions.Count;
                Emit(Arm64Encoder.Nop()); // B.NE -> next ctor

                EmitSumCtorEscapeCopy(ctor, totalSize);

                jumpToEndIdxs.Add(m_instructions.Count);
                Emit(Arm64Encoder.Nop()); // B -> end

                m_instructions[branchIdx] = Arm64Encoder.Bcond(Arm64Encoder.CondNe,
                    (m_instructions.Count - branchIdx) * 4);
            }
            else
            {
                EmitSumCtorEscapeCopy(ctor, totalSize);
            }
        }

        int endIdx = m_instructions.Count;
        foreach (int jIdx in jumpToEndIdxs)
            m_instructions[jIdx] = Arm64Encoder.B((endIdx - jIdx) * 4);

        EmitEscapeHelperEpilogue();
    }

    void EmitSumCtorEscapeCopy(SumConstructorType ctor, int totalSize)
    {
        Emit(Arm64Encoder.Mov(Arm64Reg.X20, HeapReg));
        Emit(Arm64Encoder.AddImm(HeapReg, HeapReg, totalSize));
        // Copy tag
        Emit(Arm64Encoder.Str(Arm64Reg.X21, Arm64Reg.X20, 0));
        // Copy fields
        for (int i = 0; i < ctor.Fields.Length; i++)
            EmitEscapeFieldCopy((1 + i) * 8, (1 + i) * 8, ctor.Fields[i]);
    }

    // -- _start ---------------------------------------------------

    void EmitStart(IRChapter module)
    {
        m_functionOffsets["__start"] = m_instructions.Count;

        foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X0, 0)) Emit(insn);
        foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X8, 214)) Emit(insn);
        Emit(Arm64Encoder.Svc());
        Emit(Arm64Encoder.Mov(HeapReg, Arm64Reg.X0));
        foreach (uint insn in Arm64Encoder.Li(Arm64Reg.X9, 64 * 1024 * 1024)) Emit(insn); // 64MB heap
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
