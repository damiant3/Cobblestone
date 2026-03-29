using System.Text;
using Codex.Core;
using Codex.IR;
using Codex.Types;

namespace Codex.Emit.RiscV;

sealed class RiscVCodeGen(RiscVTarget target = RiscVTarget.LinuxUser)
{
    readonly List<uint> m_instructions = [];
    readonly List<byte> m_rodata = [];
    readonly Dictionary<string, int> m_functionOffsets = [];
    readonly List<(int InsnIndex, string Target)> m_callPatches = [];
    readonly List<RodataFixup> m_rodataFixups = [];
    readonly List<FuncAddrFixup> m_funcAddrFixups = [];
    readonly Dictionary<string, int> m_stringOffsets = [];
    readonly Dictionary<string, int> m_spillCounts = [];
    readonly RiscVTarget m_target = target;

    // QEMU virt machine UART0 address (NS16550A compatible)
    const long UartBase = 0x10000000;

    int m_cceToUnicodeTableOffset = -1; // rodata offset for 128-byte CCE→Unicode lookup
    int m_unicodeToCceTableOffset = -1; // rodata offset for 256-byte Unicode→CCE lookup

    uint m_nextTemp = Reg.T0;
    uint m_nextLocal = Reg.S2;

    string m_currentFunction = "";

    // TCO state
    bool m_inTCOFunction;
    bool m_inTailPosition;
    int m_tcoLoopTop;
    uint[] m_tcoParamLocals = [];
    uint[] m_tcoTempLocals = [];
    uint m_tcoSavedNextLocal;
    uint m_tcoSavedNextTemp;
    uint m_tcoHeapMarkLocal;              // stack local: S1 value at TCO loop top
    CodexType[] m_tcoParamTypes = [];     // parameter types for TCO heap-reset check
    uint[]?[] m_tcoDecompLocals = [];     // [paramIdx] = field locals, or null if not decomposed
    int m_spillCount;           // number of spilled locals beyond S-registers
    int m_prologueIndex = -1;   // instruction index of the frame size addi (patched)
    Dictionary<string, uint> m_locals = [];
    Map<string, CodexType> m_typeDefs = Map<string, CodexType>.s_empty;
    readonly Dictionary<string, string> m_escapeHelperNames = [];
    readonly Queue<(string Key, string Name, CodexType Type)> m_escapeHelperQueue = new();
    readonly Dictionary<string, string> m_relocateHelperNames = [];
    readonly Queue<(string Key, string Name, CodexType Type)> m_relocateHelperQueue = new();

    // S1 = working-space heap pointer.  S11 = result-space heap pointer.
    // S10 = result-space BASE (set once at startup, never changes) for escape-copy skipping.
    // None of these are saved/restored by functions — they are global state.
    const uint ResultReg = Reg.S11;
    const uint ResultBaseReg = Reg.S10;
    static readonly uint[] CalleeSaved = {
        Reg.S2, Reg.S3, Reg.S4, Reg.S5, Reg.S6,
        Reg.S7, Reg.S8, Reg.S9
    };

    public void EmitModule(IRModule module)
    {
        // Bare metal: reserve first instruction slot for jump-to-start trampoline.
        // CPU begins execution at byte 0, but _start is emitted after all functions.
        int trampolineIndex = -1;
        if (m_target == RiscVTarget.BareMetal)
        {
            trampolineIndex = m_instructions.Count;
            Emit(RiscVEncoder.Nop()); // patched after _start is emitted
        }

        m_typeDefs = module.TypeDefinitions;
        m_escapeHelperNames["text"] = "__escape_text"; // pre-registered

        // Emit CCE↔Unicode lookup tables into .rodata
        m_cceToUnicodeTableOffset = m_rodata.Count;
        for (int i = 0; i < 128; i++)
            m_rodata.Add((byte)CceTable.ToUnicode[i]);
        while (m_rodata.Count % 8 != 0) m_rodata.Add(0);

        m_unicodeToCceTableOffset = m_rodata.Count;
        for (int i = 0; i < 256; i++)
            m_rodata.Add((byte)(CceTable.FromUnicode.TryGetValue(i, out int cce) ? cce : CceTable.ReplacementCce));
        while (m_rodata.Count % 8 != 0) m_rodata.Add(0);

        EmitRuntimeHelpers();

        foreach (IRDefinition def in module.Definitions)
            EmitFunction(def);

        EmitEscapeCopyHelpers();
        EmitRelocateHelpers();

        EmitStart(module);

        // Patch the trampoline to jump to _start
        if (trampolineIndex >= 0 && m_functionOffsets.TryGetValue("__start", out int startIndex))
            m_instructions[trampolineIndex] = RiscVEncoder.J((startIndex - trampolineIndex) * 4);

        PatchCalls();

        // Function address map for QEMU trace analysis (stderr)
        ulong textBase = m_target == RiscVTarget.BareMetal
            ? 0x80000000UL
            : 0x10000UL + (ulong)ElfWriter.ComputeTextFileOffset(m_target);
        foreach ((string name, int idx) in m_functionOffsets.OrderBy(kv => kv.Value))
        {
            int spills = m_spillCounts.GetValueOrDefault(name);
            if (name.StartsWith("__tramp_")) continue; // skip trampolines
            Console.Error.WriteLine($"RISCV FUNC: 0x{textBase + (ulong)(idx * 4):x} {name} (spills={spills})");
        }
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

        if (m_target == RiscVTarget.BareMetal)
            return ElfWriter.WriteFlatBinary(textSection, m_rodata.ToArray());

        int startOffset = m_functionOffsets["__start"] * 4;
        return ElfWriter.WriteExecutable(textSection, m_rodata.ToArray(), (ulong)startOffset, m_target);
    }

    // ── Function emission ────────────────────────────────────────

    static readonly string s_blacklistPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tools", "_tco_blacklist.txt");
    static HashSet<string> s_tcoBlacklist = new(
        File.Exists(s_blacklistPath)
            ? File.ReadAllLines(s_blacklistPath).Where(l => l.Trim().Length > 0)
            : Array.Empty<string>()
    );
    static bool ShouldTCO(IRDefinition def)
    {
        if (s_tcoBlacklist.Contains(def.Name)) return false;
        return def.Parameters.Length > 0 && HasTailCall(def.Body, def.Name);
    }

    static bool HasTailCall(IRExpr expr, string funcName) => expr switch
    {
        IRIf iff => HasTailCall(iff.Then, funcName) || HasTailCall(iff.Else, funcName),
        IRLet let => HasTailCall(let.Body, funcName),
        IRMatch match => match.Branches.Any(b => HasTailCall(b.Body, funcName)),
        IRRegion region => HasTailCall(region.Body, funcName),
        IRApply app => IsSelfCall(app, funcName),
        IRDo doExpr => doExpr.Statements.Length > 0 &&
            doExpr.Statements[^1] is IRDoExec exec &&
            HasTailCall(exec.Expression, funcName),
        _ => false
    };

    static bool IsSelfCall(IRExpr expr, string funcName)
    {
        IRExpr current = expr;
        while (current is IRApply app) current = app.Function;
        return current is IRName name && name.Name == funcName;
    }

    void EmitFunction(IRDefinition def)
    {
        m_functionOffsets[def.Name] = m_instructions.Count;
        m_currentFunction = def.Name;
        m_locals = new Dictionary<string, uint>();
        m_nextTemp = Reg.T3;
        m_nextLocal = Reg.S2;
        m_spillCount = 0;
        m_inTCOFunction = ShouldTCO(def);
        if (m_inTCOFunction)
            Console.Error.WriteLine($"RISCV TCO: {def.Name}");

        // Prologue: base frame = 80 bytes (ra + s0 + s2-s9; S10=ResultBaseReg, S11=ResultReg).
        // If spills are needed, frame grows — patched after body emission.
        // For large frames (>2047 bytes), addi immediate overflows.
        // Reserve space for a multi-instruction prologue using T0 as scratch.
        m_prologueIndex = m_instructions.Count;
        // Reserve 3 slots: up to 2 for Li(T0, frameSize) + 1 for sub(sp, sp, t0)
        Emit(RiscVEncoder.Addi(Reg.Sp, Reg.Sp, -80)); // slot 0: patched
        Emit(RiscVEncoder.Nop());                       // slot 1: patched or nop
        Emit(RiscVEncoder.Nop());                       // slot 2: patched or nop
        Emit(RiscVEncoder.Sd(Reg.Sp, Reg.Ra, 72));
        Emit(RiscVEncoder.Sd(Reg.Sp, Reg.S0, 64));
        for (int i = 0; i < CalleeSaved.Length; i++)
            Emit(RiscVEncoder.Sd(Reg.Sp, CalleeSaved[i], 56 - i * 8));
        // S0 = frame pointer: reserve 3 slots for large frame
        Emit(RiscVEncoder.Addi(Reg.S0, Reg.Sp, 80));   // slot A: patched
        Emit(RiscVEncoder.Nop());                        // slot B: patched or nop
        Emit(RiscVEncoder.Nop());                        // slot C: patched or nop

        m_tcoParamLocals = new uint[def.Parameters.Length];
        for (int i = 0; i < def.Parameters.Length; i++)
        {
            uint savedReg = AllocLocal();
            if (i < 8)
            {
                StoreLocal(savedReg, Reg.A0 + (uint)i);
            }
            else
            {
                // Stack params: caller pushed at [old_sp + (i-8)*8].
                // S0 = old SP (frame pointer), so param is at S0 + (i-8)*8.
                int stackOffset = (i - 8) * 8;
                Emit(RiscVEncoder.Ld(Reg.T0, Reg.S0, stackOffset));
                StoreLocal(savedReg, Reg.T0);
            }
            m_locals[def.Parameters[i].Name] = savedReg;
            m_tcoParamLocals[i] = savedReg;
        }

        if (m_inTCOFunction)
        {
            m_tcoTempLocals = new uint[def.Parameters.Length];
            for (int i = 0; i < def.Parameters.Length; i++)
                m_tcoTempLocals[i] = AllocLocal();

            // Store parameter types for heap-reset check in EmitRiscVTailCall
            m_tcoParamTypes = new CodexType[def.Parameters.Length];
            for (int i = 0; i < def.Parameters.Length; i++)
                m_tcoParamTypes[i] = def.Parameters[i].Type;

            // Pre-allocate field locals for record decomposition (Phase 2b).
            m_tcoDecompLocals = new uint[]?[def.Parameters.Length];
            for (int i = 0; i < def.Parameters.Length; i++)
            {
                CodexType resolved = ResolveType(def.Parameters[i].Type);
                if (resolved is RecordType rt)
                {
                    m_tcoDecompLocals[i] = new uint[rt.Fields.Length];
                    for (int f = 0; f < rt.Fields.Length; f++)
                        m_tcoDecompLocals[i]![f] = AllocLocal();
                }
            }

            // Allocate local for heap mark (persists across iterations)
            m_tcoHeapMarkLocal = AllocLocal();
        }
        m_tcoLoopTop = m_instructions.Count;

        // TCO heap reset: save S1 (heap ptr) at each iteration start.
        if (m_inTCOFunction)
        {
            uint hp = AllocTemp();
            Emit(RiscVEncoder.Mv(hp, Reg.S1));
            StoreLocal(m_tcoHeapMarkLocal, hp);
        }

        m_tcoSavedNextLocal = m_nextLocal;
        m_tcoSavedNextTemp = m_nextTemp;
        m_inTailPosition = m_inTCOFunction;

        uint resultReg = EmitExpr(def.Body);
        if (resultReg >= SpillBase)
            EmitSpillLoad(Reg.A0, resultReg);
        else if (resultReg != Reg.A0)
            Emit(RiscVEncoder.Mv(Reg.A0, resultReg));

        m_spillCounts[def.Name] = m_spillCount;

        // Patch frame size if spills were needed
        int frameSize = 80 + m_spillCount * 8;
        // Align to 16 bytes
        if (frameSize % 16 != 0) frameSize += 8;

        // Patch prologue: SP adjustment (3 instruction slots)
        PatchFrameAdjust(m_prologueIndex, Reg.Sp, Reg.Sp, -frameSize);
        // Patch S0 setup (3 instruction slots after callee-saved stores)
        int s0Index = m_prologueIndex + 3 + 2 + CalleeSaved.Length; // 3 prologue + sd ra + sd s0 + callee saves
        PatchFrameAdjust(s0Index, Reg.S0, Reg.Sp, frameSize);

        // Epilogue
        for (int i = 0; i < CalleeSaved.Length; i++)
            Emit(RiscVEncoder.Ld(CalleeSaved[i], Reg.Sp, 56 - i * 8));
        Emit(RiscVEncoder.Ld(Reg.Ra, Reg.Sp, 72));
        Emit(RiscVEncoder.Ld(Reg.S0, Reg.Sp, 64));
        EmitAddSp(frameSize);
        Emit(RiscVEncoder.Ret());
    }

    // Patch 3 instruction slots with rd = rs1 + imm (handles large immediates).
    void PatchFrameAdjust(int index, uint rd, uint rs1, int imm)
    {
        if (imm >= -2048 && imm <= 2047)
        {
            m_instructions[index] = RiscVEncoder.Addi(rd, rs1, imm);
            m_instructions[index + 1] = RiscVEncoder.Nop();
            m_instructions[index + 2] = RiscVEncoder.Nop();
        }
        else
        {
            // li t0, imm; add rd, rs1, t0
            uint[] li = RiscVEncoder.Li(Reg.T0, imm);
            m_instructions[index] = li[0];
            m_instructions[index + 1] = li.Length > 1 ? li[1] : RiscVEncoder.Nop();
            m_instructions[index + 2] = RiscVEncoder.Add(rd, rs1, Reg.T0);
        }
    }

    // Emit SP += imm (handles large immediates).
    void EmitAddSp(int imm)
    {
        if (imm >= -2048 && imm <= 2047)
        {
            Emit(RiscVEncoder.Addi(Reg.Sp, Reg.Sp, imm));
        }
        else
        {
            foreach (uint insn in RiscVEncoder.Li(Reg.T0, imm)) Emit(insn);
            Emit(RiscVEncoder.Add(Reg.Sp, Reg.Sp, Reg.T0));
        }
    }

    // ── Expression emission ──────────────────────────────────────

    uint EmitExpr(IRExpr expr) => expr switch
    {
        IRIntegerLit intLit => EmitIntegerLit(intLit.Value),
        IRBoolLit boolLit => EmitIntegerLit(boolLit.Value ? 1 : 0),
        IRCharLit charLit => EmitIntegerLit(CceTable.UnicharToCce(charLit.Value)),
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
        Console.Error.WriteLine($"RISCV WARNING: unhandled IR node type {expr.GetType().Name} in EmitExpr");
        return Reg.Zero;
    }

    uint EmitIntegerLit(long value)
    {
        uint rd = AllocTemp();
        foreach (uint insn in RiscVEncoder.Li(rd, value))
            Emit(insn);
        return rd;
    }

    uint EmitTextLit(string value)
    {
        if (!m_stringOffsets.TryGetValue(value, out int rodataOffset))
        {
            rodataOffset = m_rodata.Count;
            // CCE-encode, matching x86-64 and AddRodataString
            string cceEncoded = CceTable.Encode(value);
            byte[] cceBytes = new byte[cceEncoded.Length];
            for (int i = 0; i < cceEncoded.Length; i++)
                cceBytes[i] = (byte)cceEncoded[i];
            m_rodata.AddRange(BitConverter.GetBytes((long)cceBytes.Length));
            m_rodata.AddRange(cceBytes);
            while (m_rodata.Count % 8 != 0) m_rodata.Add(0);
            m_stringOffsets[value] = rodataOffset;
        }

        uint rd = AllocTemp();
        EmitLoadRodataAddress(rd, rodataOffset);
        return rd;
    }

    void EmitLoadRodataAddress(uint rd, int rodataOffset)
    {
        // Reserve 2 nop slots, patched in PatchCalls once text size is known
        m_rodataFixups.Add(new RodataFixup(m_instructions.Count, rd, rodataOffset));
        Emit(RiscVEncoder.Nop());
        Emit(RiscVEncoder.Nop());
    }

    uint EmitName(IRName name)
    {
        if (m_locals.TryGetValue(name.Name, out uint reg))
            return LoadLocal(reg);

        // Try zero-arg builtins first (matches x86-64 order)
        if (TryEmitBuiltin(name.Name, new List<IRExpr>()))
        {
            uint brd = AllocTemp();
            Emit(RiscVEncoder.Mv(brd, Reg.A0));
            return brd;
        }

        // Function used as a value (e.g., passed to map-list) — wrap as 0-capture closure
        if (name.Type is FunctionType)
            return EmitPartialApplication(name.Name, new List<IRExpr>());

        // Zero-arg sum type constructor
        if (name.Type is SumType sumType)
        {
            int tag = -1;
            for (int i = 0; i < sumType.Constructors.Length; i++)
            {
                if (sumType.Constructors[i].Name.Value == name.Name)
                {
                    tag = i;
                    break;
                }
            }
            if (tag >= 0 && sumType.Constructors[tag].Fields.IsEmpty)
            {
                uint ptrReg = AllocTemp();
                Emit(RiscVEncoder.Mv(ptrReg, Reg.S1));
                Emit(RiscVEncoder.Addi(Reg.S1, Reg.S1, 8));
                uint tagReg = AllocTemp();
                foreach (uint insn in RiscVEncoder.Li(tagReg, tag)) Emit(insn);
                Emit(RiscVEncoder.Sd(ptrReg, tagReg, 0));
                return ptrReg;
            }
        }

        // Top-level constant or zero-arg function — call it
        {
            uint rd = AllocTemp();
            EmitCallTo(name.Name);
            Emit(RiscVEncoder.Mv(rd, Reg.A0));
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
            case IRBinaryOp.AddInt: Emit(RiscVEncoder.Add(rd, leftReg, right)); break;
            case IRBinaryOp.SubInt: Emit(RiscVEncoder.Sub(rd, leftReg, right)); break;
            case IRBinaryOp.MulInt: Emit(RiscVEncoder.Mul(rd, leftReg, right)); break;
            case IRBinaryOp.DivInt: Emit(RiscVEncoder.Div(rd, leftReg, right)); break;
            case IRBinaryOp.Eq:
                if (bin.Left.Type is TextType)
                {
                    // Set A1 first — right may be A0, and Mv(A0,...) would clobber it
                    Emit(RiscVEncoder.Mv(Reg.A1, right));
                    Emit(RiscVEncoder.Mv(Reg.A0, leftReg));
                    EmitCallTo("__str_eq");
                    Emit(RiscVEncoder.Mv(rd, Reg.A0));
                }
                else
                {
                    Emit(RiscVEncoder.Sub(rd, leftReg, right));
                    Emit(RiscVEncoder.Sltu(rd, Reg.Zero, rd));
                    Emit(RiscVEncoder.Xori(rd, rd, 1));
                }
                break;
            case IRBinaryOp.NotEq:
                if (bin.Left.Type is TextType)
                {
                    Emit(RiscVEncoder.Mv(Reg.A1, right));
                    Emit(RiscVEncoder.Mv(Reg.A0, leftReg));
                    EmitCallTo("__str_eq");
                    Emit(RiscVEncoder.Xori(Reg.A0, Reg.A0, 1));
                    Emit(RiscVEncoder.Mv(rd, Reg.A0));
                }
                else
                {
                    Emit(RiscVEncoder.Sub(rd, leftReg, right));
                    Emit(RiscVEncoder.Sltu(rd, Reg.Zero, rd));
                }
                break;
            case IRBinaryOp.AppendText:
                Emit(RiscVEncoder.Mv(Reg.A1, right));
                Emit(RiscVEncoder.Mv(Reg.A0, leftReg));
                EmitCallTo("__str_concat");
                Emit(RiscVEncoder.Mv(rd, Reg.A0));
                break;
            case IRBinaryOp.Lt:  Emit(RiscVEncoder.Slt(rd, leftReg, right)); break;
            case IRBinaryOp.Gt:  Emit(RiscVEncoder.Slt(rd, right, leftReg)); break;
            case IRBinaryOp.LtEq:
                Emit(RiscVEncoder.Slt(rd, right, leftReg));
                Emit(RiscVEncoder.Xori(rd, rd, 1));
                break;
            case IRBinaryOp.GtEq:
                Emit(RiscVEncoder.Slt(rd, leftReg, right));
                Emit(RiscVEncoder.Xori(rd, rd, 1));
                break;
            case IRBinaryOp.And: Emit(RiscVEncoder.And(rd, leftReg, right)); break;
            case IRBinaryOp.Or:  Emit(RiscVEncoder.Or(rd, leftReg, right)); break;
            case IRBinaryOp.ConsList:
                // Cons head tail: alloc [len+1][head][tail elements...]
                Emit(RiscVEncoder.Mv(Reg.A1, right));
                Emit(RiscVEncoder.Mv(Reg.A0, leftReg));
                EmitCallTo("__list_cons");
                Emit(RiscVEncoder.Mv(rd, Reg.A0));
                break;
            case IRBinaryOp.AppendList:
                Emit(RiscVEncoder.Mv(Reg.A1, right));
                Emit(RiscVEncoder.Mv(Reg.A0, leftReg));
                EmitCallTo("__list_append");
                Emit(RiscVEncoder.Mv(rd, Reg.A0));
                break;
            case IRBinaryOp.PowInt:
                Emit(RiscVEncoder.Mv(Reg.A1, right));
                Emit(RiscVEncoder.Mv(Reg.A0, leftReg));
                EmitCallTo("__ipow");
                Emit(RiscVEncoder.Mv(rd, Reg.A0));
                break;
            default:
                Console.Error.WriteLine($"RISCV WARNING: unhandled binary op {bin.Op} in EmitBinary");
                Emit(RiscVEncoder.Mv(rd, Reg.Zero)); break;
        }

        return rd;
    }

    uint EmitIf(IRIf ifExpr)
    {
        bool savedTail = m_inTailPosition;
        m_inTailPosition = false;
        uint cond = EmitExpr(ifExpr.Condition);

        int beqzIndex = m_instructions.Count;
        Emit(RiscVEncoder.Nop());

        m_inTailPosition = savedTail;
        uint thenReg = EmitExpr(ifExpr.Then);
        uint resultReg = AllocLocal();
        StoreLocal(resultReg, thenReg);

        int jEndIndex = m_instructions.Count;
        Emit(RiscVEncoder.Nop());

        int elseStart = m_instructions.Count;
        m_inTailPosition = savedTail;
        uint elseReg = EmitExpr(ifExpr.Else);
        StoreLocal(resultReg, elseReg);

        int endIndex = m_instructions.Count;

        m_instructions[beqzIndex] = RiscVEncoder.Beq(cond, Reg.Zero, (elseStart - beqzIndex) * 4);
        m_instructions[jEndIndex] = RiscVEncoder.J((endIndex - jEndIndex) * 4);

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

    void EmitRiscVTailCall(IRApply app)
    {
        List<IRExpr> args = new();
        IRExpr cur = app;
        while (cur is IRApply a) { args.Insert(0, a.Argument); cur = a.Function; }

        // Save caller's register-local and temp state (not spillCount —
        // spill slots must grow monotonically for correct frame sizing).
        uint callerLocal = m_nextLocal;
        uint callerTemp = m_nextTemp;

        m_nextLocal = m_tcoSavedNextLocal;
        m_nextTemp = m_tcoSavedNextTemp;

        for (int i = 0; i < args.Count && i < m_tcoTempLocals.Length; i++)
        {
            bool saved = m_inTailPosition; m_inTailPosition = false;
            uint r = EmitExpr(args[i]);
            m_inTailPosition = saved;
            StoreLocal(m_tcoTempLocals[i], r);
        }

        // ── TCO heap reset (Phase 2a + 2b) ─────────────────────
        // Record-typed args are decomposed into fields so the check
        // inspects field pointers (often pre-existing) rather than
        // the record pointer (always freshly allocated).
        {
            // Phase 1: decompose record-typed heap args into field locals
            List<int> decompIndices = [];
            List<int> plainHeapIndices = [];
            for (int i = 0; i < args.Count && i < m_tcoParamTypes.Length; i++)
            {
                CodexType resolved = ResolveType(m_tcoParamTypes[i]);
                if (!IRRegion.TypeNeedsHeapEscape(resolved))
                    continue;
                if (resolved is RecordType rt && i < m_tcoDecompLocals.Length
                    && m_tcoDecompLocals[i] is uint[] fieldLocals)
                {
                    uint ptr = LoadLocal(m_tcoTempLocals[i]);
                    for (int f = 0; f < rt.Fields.Length; f++)
                    {
                        uint fv = AllocTemp();
                        Emit(RiscVEncoder.Ld(fv, ptr, f * 8));
                        StoreLocal(fieldLocals[f], fv);
                    }
                    decompIndices.Add(i);
                }
                else
                {
                    plainHeapIndices.Add(i);
                }
            }

            // Phase 2: determine if any runtime pointer checks are needed
            bool anyChecks = plainHeapIndices.Count > 0;
            if (!anyChecks)
            {
                foreach (int idx in decompIndices)
                {
                    RecordType rt = (RecordType)ResolveType(m_tcoParamTypes[idx]);
                    for (int f = 0; f < rt.Fields.Length; f++)
                    {
                        if (IRRegion.TypeNeedsHeapEscape(ResolveType(rt.Fields[f].Type)))
                        { anyChecks = true; break; }
                    }
                    if (anyChecks) break;
                }
            }

            if (!anyChecks)
            {
                // All scalar (or decomposed records with only scalar fields)
                Emit(RiscVEncoder.Mv(Reg.S1, LoadLocal(m_tcoHeapMarkLocal)));

                // Reconstruct decomposed records at the reset heap position
                foreach (int idx in decompIndices)
                {
                    RecordType rt = (RecordType)ResolveType(m_tcoParamTypes[idx]);
                    uint newPtr = AllocTemp();
                    Emit(RiscVEncoder.Mv(newPtr, Reg.S1));
                    Emit(RiscVEncoder.Addi(Reg.S1, Reg.S1, rt.Fields.Length * 8));
                    for (int f = 0; f < rt.Fields.Length; f++)
                    {
                        uint fv = LoadLocal(m_tcoDecompLocals[idx]![f]);
                        Emit(RiscVEncoder.Sd(newPtr, fv, f * 8));
                    }
                    StoreLocal(m_tcoTempLocals[idx], newPtr);
                }
            }
            else
            {
                // Load mark into a temp register
                uint markReg = AllocTemp();
                Emit(RiscVEncoder.Mv(markReg, LoadLocal(m_tcoHeapMarkLocal)));

                List<(int Index, uint Rs1, uint Rs2)> skipBranches = [];

                // Check plain heap args
                foreach (int idx in plainHeapIndices)
                {
                    uint argVal = LoadLocal(m_tcoTempLocals[idx]);
                    int branchIdx = m_instructions.Count;
                    Emit(RiscVEncoder.Bge(argVal, markReg, 0));
                    skipBranches.Add((branchIdx, argVal, markReg));
                }

                // Check decomposed record pointer fields
                foreach (int idx in decompIndices)
                {
                    RecordType rt = (RecordType)ResolveType(m_tcoParamTypes[idx]);
                    for (int f = 0; f < rt.Fields.Length; f++)
                    {
                        CodexType ft = ResolveType(rt.Fields[f].Type);
                        if (IRRegion.TypeNeedsHeapEscape(ft))
                        {
                            uint fv = LoadLocal(m_tcoDecompLocals[idx]![f]);
                            int branchIdx = m_instructions.Count;
                            Emit(RiscVEncoder.Bge(fv, markReg, 0));
                            skipBranches.Add((branchIdx, fv, markReg));
                        }
                    }
                }

                // All checks passed — reset heap to mark
                Emit(RiscVEncoder.Mv(Reg.S1, markReg));

                // Reconstruct decomposed records at the reset heap position
                foreach (int idx in decompIndices)
                {
                    RecordType rt = (RecordType)ResolveType(m_tcoParamTypes[idx]);
                    uint newPtr = AllocTemp();
                    Emit(RiscVEncoder.Mv(newPtr, Reg.S1));
                    Emit(RiscVEncoder.Addi(Reg.S1, Reg.S1, rt.Fields.Length * 8));
                    for (int f = 0; f < rt.Fields.Length; f++)
                    {
                        uint fv = LoadLocal(m_tcoDecompLocals[idx]![f]);
                        Emit(RiscVEncoder.Sd(newPtr, fv, f * 8));
                    }
                    StoreLocal(m_tcoTempLocals[idx], newPtr);
                }

                // Patch all skip-reset branches to land here
                int noResetTarget = m_instructions.Count;
                foreach (var (brIdx, rs1, rs2) in skipBranches)
                    m_instructions[brIdx] = RiscVEncoder.Bge(rs1, rs2,
                        (noResetTarget - brIdx) * 4);
            }
        }

        for (int i = 0; i < args.Count && i < m_tcoParamLocals.Length; i++)
        {
            uint val = LoadLocal(m_tcoTempLocals[i]);
            StoreLocal(m_tcoParamLocals[i], val);
        }
        Emit(RiscVEncoder.J((m_tcoLoopTop - m_instructions.Count) * 4));

        // Restore — code after the jump is only reached when a different
        // match branch matched, so it needs the pre-reset allocation state.
        m_nextLocal = callerLocal;
        m_nextTemp = callerTemp;
    }

    uint EmitApply(IRApply apply)
    {
        // TCO interception
        if (m_inTCOFunction && m_inTailPosition && IsSelfCall(apply, m_currentFunction))
        {
            EmitRiscVTailCall(apply);
            uint dummy = AllocTemp();
            foreach (uint insn in RiscVEncoder.Li(dummy, 0)) Emit(insn);
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
                return Reg.A0;

            // Sum type constructor: allocate [tag][field0][field1]... on heap
            SumType? sumType = apply.Type as SumType;
            if (sumType is null && apply.Type is ConstructedType ctApply)
                sumType = m_typeDefs[ctApply.Constructor.Value] as SumType;
            if (sumType is SumType st)
            {
                uint ctorResult = EmitConstructor(funcName.Name, args, st);
                if (ctorResult != Reg.Zero)
                    return ctorResult;
            }

            // Partial application: result is a function → create closure
            // Closure: [trampoline_addr:8][cap_0:8][cap_1:8]...
            // Trampoline: loads captures from T2 (closure ptr), shifts visible args, tail-calls real fn
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

            // Push stack args (9th+) onto the stack before setting register args.
            // Store at negative offsets from current SP BEFORE adjusting SP,
            // so that spill-slot LoadLocal offsets remain valid.
            int stackArgCount = Math.Max(0, argRegs.Count - 8);
            if (stackArgCount > 0)
            {
                int totalStackSize = stackArgCount * 8;
                for (int i = 0; i < stackArgCount; i++)
                {
                    uint argVal = LoadLocal(argRegs[8 + i]);
                    // Store below current SP: at offset -(totalSize) + i*8
                    Emit(RiscVEncoder.Sd(Reg.Sp, argVal, -totalStackSize + i * 8));
                }
                // Now adjust SP past the stored args
                Emit(RiscVEncoder.Addi(Reg.Sp, Reg.Sp, -totalStackSize));
            }

            // Load register args (1st-8th) into A0-A7
            int regArgCount = Math.Min(argRegs.Count, 8);
            for (int i = 0; i < regArgCount; i++)
            {
                uint argVal = LoadLocal(argRegs[i]);
                if (argVal != Reg.A0 + (uint)i)
                    Emit(RiscVEncoder.Mv(Reg.A0 + (uint)i, argVal));
            }

            // Check if it's a local (closure) or a top-level function
            if (m_locals.ContainsKey(funcName.Name))
            {
                // Indirect call via closure: T2=closure, load code_ptr, jump
                uint closureReg = LoadLocal(m_locals[funcName.Name]);
                Emit(RiscVEncoder.Mv(Reg.T2, closureReg));
                Emit(RiscVEncoder.Ld(Reg.T0, Reg.T2, 0)); // code_ptr
                Emit(RiscVEncoder.Jalr(Reg.Ra, Reg.T0, 0));
            }
            else
            {
                EmitCallTo(funcName.Name);
            }

            // Clean up stack args after call
            if (stackArgCount > 0)
                Emit(RiscVEncoder.Addi(Reg.Sp, Reg.Sp, stackArgCount * 8));

            m_inTailPosition = savedTailPos;
            uint rd = AllocTemp();
            Emit(RiscVEncoder.Mv(rd, Reg.A0));
            return rd;
        }

        m_inTailPosition = savedTailPos;
        Console.Error.WriteLine($"RISCV WARNING: EmitApply fallthrough — function expr is {func.GetType().Name}, not IRName");
        return Reg.Zero;
    }

    // Emit a partial application as a closure.
    // Creates a trampoline that unpacks captures and tail-calls the real function.
    // Closure layout: [trampoline_addr:8][cap_0:8][cap_1:8]...
    // Convention: caller sets T2=closure_ptr before jumping to trampoline.
    uint EmitPartialApplication(string funcName, List<IRExpr> capturedArgs)
    {
        // Evaluate and save captured args
        List<uint> capRegs = new();
        foreach (IRExpr arg in capturedArgs)
        {
            uint r = EmitExpr(arg);
            uint saved = AllocLocal();
            StoreLocal(saved, r);
            capRegs.Add(saved);
        }

        // Generate trampoline: a small function that shuffles args and tail-calls funcName.
        // Trampoline convention: T2=closure, A0..A(K-1)=visible args
        // Goal: A0..A(N-1)=captured, A(N)..A(N+K-1)=visible, then jump to funcName
        int numCaptures = capRegs.Count;
        string trampolineName = $"__tramp_{funcName}_{numCaptures}_{m_instructions.Count}";

        // Emit trampoline code (deferred — emitted inline, jumped over)
        int jumpOverIdx = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // patched: jump over trampoline

        m_functionOffsets[trampolineName] = m_instructions.Count;

        // Shift visible args right by numCaptures positions (backward to avoid clobbering)
        // We don't know exactly how many visible args, but we know max is 8 - numCaptures.
        // Shift ALL possible arg slots to be safe.
        for (int i = 7; i >= 0; i--)
        {
            if (i + numCaptures <= 7)
                Emit(RiscVEncoder.Mv(Reg.A0 + (uint)(i + numCaptures), Reg.A0 + (uint)i));
        }

        // Load captured args from closure (T2) into A0..A(numCaptures-1)
        for (int i = 0; i < numCaptures; i++)
            Emit(RiscVEncoder.Ld(Reg.A0 + (uint)i, Reg.T2, 8 + i * 8));

        // Tail-call the real function (no prologue/epilogue needed)
        EmitCallTo(funcName);
        // Actually, this is a tail-call — we want to jump, not call.
        // But EmitCallTo uses jal (saves RA). The trampoline has no frame,
        // so RA is the original caller's RA. Use j instead.
        // Replace the last instruction (jal) with a simple j (rd=zero).
        // Actually, EmitCallTo emits a NOP that gets patched to jal ra, offset.
        // For a tail-call, we want jal zero, offset (= j offset).
        // Let me just use the last emitted call and patch it differently.
        // Hmm, PatchCalls always uses Call (= Jal ra). Let me add a tail-call mechanism.

        // Simpler: the trampoline is NOT a function — it has no frame.
        // RA already points to the real caller. When the real function returns,
        // it returns to the real caller. Using jal ra sets RA to after the trampoline
        // (which is in the middle of the calling function). That's wrong.

        // Fix: use jalr zero, t0, 0 (= jr t0) for a tail-jump.
        // Remove the EmitCallTo and do it manually.
        m_instructions.RemoveAt(m_instructions.Count - 1); // remove the NOP from EmitCallTo
        m_callPatches.RemoveAt(m_callPatches.Count - 1);   // remove the patch entry

        // Instead: load function address and tail-jump
        EmitLoadFunctionAddress(Reg.T0, funcName);
        Emit(RiscVEncoder.Jalr(Reg.Zero, Reg.T0, 0)); // jr t0 (tail-jump, doesn't modify RA)

        // Patch jump-over
        int afterTrampoline = m_instructions.Count;
        m_instructions[jumpOverIdx] = RiscVEncoder.J((afterTrampoline - jumpOverIdx) * 4);

        // Allocate closure on heap: [trampoline_addr][cap_0][cap_1]...
        int closureSize = (1 + numCaptures) * 8;
        uint ptrReg = AllocTemp();
        Emit(RiscVEncoder.Mv(ptrReg, Reg.S1));
        Emit(RiscVEncoder.Addi(Reg.S1, Reg.S1, closureSize));

        // Store trampoline address
        EmitLoadFunctionAddress(Reg.T0, trampolineName);
        Emit(RiscVEncoder.Sd(ptrReg, Reg.T0, 0));

        // Store captured args
        for (int i = 0; i < capRegs.Count; i++)
            Emit(RiscVEncoder.Sd(ptrReg, LoadLocal(capRegs[i]), 8 + i * 8));

        return ptrReg;
    }

    uint EmitNegate(IRNegate neg)
    {
        uint operand = EmitExpr(neg.Operand);
        uint rd = AllocTemp();
        Emit(RiscVEncoder.Sub(rd, Reg.Zero, operand));
        return rd;
    }

    uint EmitDo(IRDo doExpr)
    {
        uint lastReg = Reg.Zero;
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

    // ── Records, sum types, pattern matching ─────────────────────

    uint EmitRecord(IRRecord rec)
    {
        // Evaluate all field values and save to locals
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
        Emit(RiscVEncoder.Mv(ptrReg, Reg.S1));
        Emit(RiscVEncoder.Addi(Reg.S1, Reg.S1, totalSize));

        // Store fields in RecordType field order (matches EmitFieldAccess lookup).
        // If source order differs from type definition order, this reorders.
        RecordType? rt = rec.Type as RecordType;
        if (rt is null && rec.Type is ConstructedType ctRec)
            rt = m_typeDefs[ctRec.Constructor.Value] as RecordType;
        if (rt is RecordType)
        {
            if (rt.Fields.Length != rec.Fields.Length)
                Console.Error.WriteLine($"RISCV WARNING: record field count mismatch — IR has {rec.Fields.Length} fields, RecordType has {rt.Fields.Length} for {rec.TypeName}");
            // Check for field name mismatches
            foreach ((string name, _) in rec.Fields)
            {
                if (!rt.Fields.Any(f => f.FieldName.Value == name))
                    Console.Error.WriteLine($"RISCV WARNING: IR field '{name}' not found in RecordType for {rec.TypeName}");
            }
            for (int i = 0; i < rt.Fields.Length; i++)
            {
                string fieldName = rt.Fields[i].FieldName.Value;
                if (fieldMap.TryGetValue(fieldName, out uint saved))
                    Emit(RiscVEncoder.Sd(ptrReg, LoadLocal(saved), i * 8));
            }
        }
        else
        {
            Console.Error.WriteLine($"RISCV WARNING: EmitRecord without RecordType — type={rec.Type?.GetType().Name} typeName={rec.TypeName} fields={string.Join(",", rec.Fields.Select(f => f.FieldName))}");
            // Fallback: store in IR order
            int i = 0;
            foreach ((string _, IRExpr _) in rec.Fields)
            {
                Emit(RiscVEncoder.Sd(ptrReg, LoadLocal(fieldMap.Values.ElementAt(i)), i * 8));
                i++;
            }
        }

        return ptrReg;
    }

    uint EmitFieldAccess(IRFieldAccess fa)
    {
        uint baseReg = EmitExpr(fa.Record);

        int fieldIndex = 0;
        RecordType? rt = fa.Record.Type as RecordType;
        if (rt is null && fa.Record.Type is ConstructedType ctFa)
            rt = m_typeDefs[ctFa.Constructor.Value] as RecordType;
        if (rt is RecordType)
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
        uint rd = AllocTemp();
        Emit(RiscVEncoder.Ld(rd, baseReg, fieldIndex * 8));
        return rd;
    }

    uint EmitConstructor(string ctorName, List<IRExpr> args, SumType sumType)
    {
        int tag = -1;
        for (int i = 0; i < sumType.Constructors.Length; i++)
        {
            if (sumType.Constructors[i].Name.Value == ctorName)
            {
                tag = i;
                break;
            }
        }
        if (tag < 0) return Reg.Zero;

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
        Emit(RiscVEncoder.Mv(ptrReg, Reg.S1));
        Emit(RiscVEncoder.Addi(Reg.S1, Reg.S1, totalSize));

        uint tagReg = AllocTemp();
        foreach (uint insn in RiscVEncoder.Li(tagReg, tag)) Emit(insn);
        Emit(RiscVEncoder.Sd(ptrReg, tagReg, 0));

        for (int i = 0; i < argRegs.Count; i++)
            Emit(RiscVEncoder.Sd(ptrReg, LoadLocal(argRegs[i]), 8 + i * 8));

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
        if (index >= match.Branches.Length)
            return;

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
                uint cmpReg = AllocTemp();
                Emit(RiscVEncoder.Sub(cmpReg, LoadLocal(scrutReg), litReg));
                Emit(RiscVEncoder.Sltu(cmpReg, Reg.Zero, cmpReg));

                int branchIdx = m_instructions.Count;
                Emit(RiscVEncoder.Nop());

                uint bodyReg = EmitExpr(branch.Body);
                StoreLocal(resultReg, bodyReg);


                int jumpEndIdx = m_instructions.Count;
                Emit(RiscVEncoder.Nop());

                int nextStart = m_instructions.Count;
                EmitMatchBranches(match, index + 1, scrutReg, resultReg);
                int endIdx = m_instructions.Count;

                m_instructions[branchIdx] = RiscVEncoder.Bne(cmpReg, Reg.Zero,
                    (nextStart - branchIdx) * 4);
                m_instructions[jumpEndIdx] = RiscVEncoder.J(
                    (endIdx - jumpEndIdx) * 4);
                break;
            }

            case IRCtorPattern ctorPat:
            {
                uint tagReg = AllocTemp();
                Emit(RiscVEncoder.Ld(tagReg, LoadLocal(scrutReg), 0));

                int expectedTag = 0;
                SumType? matchSumType = ctorPat.Type as SumType;
                if (matchSumType is null && ctorPat.Type is ConstructedType ctMatch)
                    matchSumType = m_typeDefs[ctMatch.Constructor.Value] as SumType;
                if (matchSumType is SumType sumType)
                {
                    for (int i = 0; i < sumType.Constructors.Length; i++)
                    {
                        if (sumType.Constructors[i].Name.Value == ctorPat.Name)
                        {
                            expectedTag = i;
                            break;
                        }
                    }
                }

                uint expectedReg = AllocTemp();
                foreach (uint insn in RiscVEncoder.Li(expectedReg, expectedTag)) Emit(insn);

                int branchIdx = m_instructions.Count;
                Emit(RiscVEncoder.Nop());

                for (int i = 0; i < ctorPat.SubPatterns.Length; i++)
                {
                    if (ctorPat.SubPatterns[i] is IRVarPattern varPat)
                    {
                        uint fieldReg = AllocLocal();
                        uint scrut = LoadLocal(scrutReg);
                        uint tmp = AllocTemp();
                        Emit(RiscVEncoder.Ld(tmp, scrut, 8 + i * 8));
                        StoreLocal(fieldReg, tmp);
                        m_locals[varPat.Name] = fieldReg;
                    }
                }

                uint bodyReg = EmitExpr(branch.Body);
                StoreLocal(resultReg, bodyReg);


                int jumpEndIdx = m_instructions.Count;
                Emit(RiscVEncoder.Nop());

                int nextStart = m_instructions.Count;
                EmitMatchBranches(match, index + 1, scrutReg, resultReg);
                int endIdx = m_instructions.Count;

                m_instructions[branchIdx] = RiscVEncoder.Bne(tagReg, expectedReg,
                    (nextStart - branchIdx) * 4);
                m_instructions[jumpEndIdx] = RiscVEncoder.J(
                    (endIdx - jumpEndIdx) * 4);
                break;
            }
        }
    }

    uint EmitRegion(IRRegion region)
    {
        if (region.Type is FunctionType)
            return EmitExpr(region.Body);

        if (!region.NeedsEscapeCopy)
        {
            // Scalar return — save/restore S1 (heap ptr) to reclaim intermediates.
            uint mark = AllocLocal();
            uint hpTmp = AllocTemp();
            Emit(RiscVEncoder.Mv(hpTmp, Reg.S1));
            StoreLocal(mark, hpTmp);

            uint bodyResult = EmitExpr(region.Body);

            Emit(RiscVEncoder.Mv(Reg.S1, LoadLocal(mark)));
            return bodyResult;
        }

        // Heap return — pass-through (escape-copy crashes on cross-references).
        return EmitExpr(region.Body);
    }

    void EmitRelocateCall(uint ptrLocal, uint deltaLocal, CodexType type)
    {
        CodexType resolved = ResolveType(type);
        if (!IRRegion.TypeNeedsHeapEscape(resolved))
            return;

        // Text: no internal pointers to relocate (only the pointer TO text
        // needs adjusting, which was done by the caller's subtract)
        if (resolved is TextType)
            return;

        string helperName = GetOrQueueRelocateHelper(resolved);
        Emit(RiscVEncoder.Mv(Reg.A0, LoadLocal(ptrLocal)));
        Emit(RiscVEncoder.Mv(Reg.A1, LoadLocal(deltaLocal)));
        EmitCallTo(helperName);
    }

    uint EmitEscapeCopy(uint srcLocal, CodexType type)
    {
        CodexType resolved = ResolveType(type);
        if (!IRRegion.TypeNeedsHeapEscape(resolved))
            return LoadLocal(srcLocal);

        string helperName = GetOrQueueEscapeHelper(resolved);
        uint src = LoadLocal(srcLocal);
        Emit(RiscVEncoder.Mv(Reg.A0, src));
        EmitCallTo(helperName);
        uint result = AllocTemp();
        Emit(RiscVEncoder.Mv(result, Reg.A0));
        return result;
    }

    CodexType ResolveType(CodexType type)
    {
        if (type is ConstructedType ct && m_typeDefs[ct.Constructor.Value] is CodexType resolved)
            return resolved;
        if (type is ListType lt)
        {
            CodexType resolvedElem = ResolveType(lt.Element);
            if (!ReferenceEquals(resolvedElem, lt.Element))
                return new ListType(resolvedElem);
        }
        return type;
    }

    static string EscapeCopyKey(CodexType type) => type switch
    {
        TextType => "text",
        RecordType rt => $"record_{rt.TypeName.Value}",
        SumType st => $"sum_{st.TypeName.Value}",
        ListType lt => $"list_{EscapeCopyKey(lt.Element)}",
        ConstructedType ct => $"ctor_{ct.Constructor.Value}",
        _ => $"type_{type.GetType().Name}"
    };

    string GetOrQueueEscapeHelper(CodexType type)
    {
        type = ResolveType(type);
        string key = EscapeCopyKey(type);
        if (m_escapeHelperNames.TryGetValue(key, out string? name))
            return name;
        name = $"__escape_{key}";
        m_escapeHelperNames[key] = name;
        m_escapeHelperQueue.Enqueue((key, name, type));
        return name;
    }

    // ── Escape copy helper emission (standalone functions) ───────

    void EmitEscapeCopyHelpers()
    {
        // Drain queue — helpers may enqueue new types for nested fields
        while (m_escapeHelperQueue.Count > 0)
        {
            (string _, string name, CodexType type) = m_escapeHelperQueue.Dequeue();
            switch (type)
            {
                case TextType:
                    break; // __escape_text already emitted in EmitRuntimeHelpers
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

    // All escape helpers use a fixed convention:
    //   A0 = old pointer in, A0 = new pointer out
    //   S2 = old ptr, S3 = new ptr, S4/S5 = extra (tag, length, index)
    //   Frame: 48 bytes (ra, s2, s3, s4, s5, pad)
    const int EscapeFrameSize = 48;

    void EmitEscapeHelperPrologue(string name)
    {
        m_functionOffsets[name] = m_instructions.Count;
        // Null guard: if a0 == 0, return 0 immediately
        int notNull = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // patched: bnez a0 → continue
        Emit(RiscVEncoder.Mv(Reg.A0, Reg.Zero));
        Emit(RiscVEncoder.Ret());
        int continueLabel = m_instructions.Count;
        m_instructions[notNull] = RiscVEncoder.Bne(Reg.A0, Reg.Zero,
            (continueLabel - notNull) * 4);

        Emit(RiscVEncoder.Addi(Reg.Sp, Reg.Sp, -EscapeFrameSize));
        Emit(RiscVEncoder.Sd(Reg.Sp, Reg.Ra, 40));
        Emit(RiscVEncoder.Sd(Reg.Sp, Reg.S2, 32));
        Emit(RiscVEncoder.Sd(Reg.Sp, Reg.S3, 24));
        Emit(RiscVEncoder.Sd(Reg.Sp, Reg.S4, 16));
        Emit(RiscVEncoder.Sd(Reg.Sp, Reg.S5, 8));
        Emit(RiscVEncoder.Mv(Reg.S2, Reg.A0)); // s2 = old ptr
    }

    void EmitEscapeHelperEpilogue()
    {
        Emit(RiscVEncoder.Mv(Reg.A0, Reg.S3)); // return new ptr
        Emit(RiscVEncoder.Ld(Reg.S5, Reg.Sp, 8));
        Emit(RiscVEncoder.Ld(Reg.S4, Reg.Sp, 16));
        Emit(RiscVEncoder.Ld(Reg.S3, Reg.Sp, 24));
        Emit(RiscVEncoder.Ld(Reg.S2, Reg.Sp, 32));
        Emit(RiscVEncoder.Ld(Reg.Ra, Reg.Sp, 40));
        Emit(RiscVEncoder.Addi(Reg.Sp, Reg.Sp, EscapeFrameSize));
        Emit(RiscVEncoder.Ret());
    }

    void EmitEscapeFieldCopy(int srcOffset, int dstOffset, CodexType fieldType)
    {
        CodexType resolved = ResolveType(fieldType);
        if (IRRegion.TypeNeedsHeapEscape(resolved))
        {
            string helper = GetOrQueueEscapeHelper(resolved);
            Emit(RiscVEncoder.Ld(Reg.A0, Reg.S2, srcOffset));
            // Skip copy if pointer is already in result space
            int skipIdx = m_instructions.Count;
            Emit(RiscVEncoder.Nop()); // patched: bge a0, s10 → skip
            EmitCallTo(helper);
            int doneIdx = m_instructions.Count;
            Emit(RiscVEncoder.Nop()); // patched: j → done
            int skipTarget = m_instructions.Count;
            m_instructions[skipIdx] = RiscVEncoder.Bge(Reg.A0, ResultBaseReg,
                (skipTarget - skipIdx) * 4);
            // Already in result space — A0 unchanged
            int doneTarget = m_instructions.Count;
            m_instructions[doneIdx] = RiscVEncoder.J((doneTarget - doneIdx) * 4);
            Emit(RiscVEncoder.Sd(Reg.S3, Reg.A0, dstOffset));
        }
        else
        {
            Emit(RiscVEncoder.Ld(Reg.T0, Reg.S2, srcOffset));
            Emit(RiscVEncoder.Sd(Reg.S3, Reg.T0, dstOffset));
        }
    }

    void EmitRecordEscapeHelper(string name, RecordType rt)
    {
        EmitEscapeHelperPrologue(name);

        int totalSize = rt.Fields.Length * 8;
        Emit(RiscVEncoder.Mv(Reg.S3, Reg.S1));
        Emit(RiscVEncoder.Addi(Reg.S1, Reg.S1, totalSize));

        for (int i = 0; i < rt.Fields.Length; i++)
            EmitEscapeFieldCopy(i * 8, i * 8, rt.Fields[i].Type);

        EmitEscapeHelperEpilogue();
    }

    void EmitListEscapeHelper(string name, ListType lt)
    {
        EmitEscapeHelperPrologue(name);

        // s4 = count
        Emit(RiscVEncoder.Ld(Reg.S4, Reg.S2, 0));

        // Allocate [capacity | count | elements]: (count + 2) * 8, capacity = count (tight)
        Emit(RiscVEncoder.Sd(Reg.S1, Reg.S4, 0));         // capacity = count
        Emit(RiscVEncoder.Addi(Reg.S1, Reg.S1, 8));       // past capacity
        Emit(RiscVEncoder.Mv(Reg.S3, Reg.S1));            // S3 = new list ptr
        Emit(RiscVEncoder.Addi(Reg.T0, Reg.S4, 1));
        Emit(RiscVEncoder.Slli(Reg.T0, Reg.T0, 3));
        Emit(RiscVEncoder.Add(Reg.S1, Reg.S1, Reg.T0));   // advance past count+elements

        // Store count
        Emit(RiscVEncoder.Sd(Reg.S3, Reg.S4, 0));

        // s5 = loop index = 0
        foreach (uint insn in RiscVEncoder.Li(Reg.S5, 0)) Emit(insn);

        // Loop: bge s5, s4 → done
        int loopStart = m_instructions.Count;
        int exitIdx = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // patched

        // Load element: a0 = old[8 + s5*8]
        Emit(RiscVEncoder.Slli(Reg.T0, Reg.S5, 3));
        Emit(RiscVEncoder.Add(Reg.T0, Reg.S2, Reg.T0));
        Emit(RiscVEncoder.Ld(Reg.A0, Reg.T0, 8));

        // Deep copy element if needed (skip if already in result space)
        CodexType elemType = ResolveType(lt.Element);
        if (IRRegion.TypeNeedsHeapEscape(elemType))
        {
            string elemHelper = GetOrQueueEscapeHelper(elemType);
            int elemSkip = m_instructions.Count;
            Emit(RiscVEncoder.Nop()); // patched: bge a0, s10 → skip
            EmitCallTo(elemHelper);
            int elemDone = m_instructions.Count;
            Emit(RiscVEncoder.Nop()); // patched: j → done
            int elemSkipTarget = m_instructions.Count;
            m_instructions[elemSkip] = RiscVEncoder.Bge(Reg.A0, ResultBaseReg,
                (elemSkipTarget - elemSkip) * 4);
            // a0 unchanged (already in result space)
            int elemDoneTarget = m_instructions.Count;
            m_instructions[elemDone] = RiscVEncoder.J((elemDoneTarget - elemDone) * 4);
            // a0 = copied (or existing) element
        }

        // Store: new[8 + s5*8] = a0
        Emit(RiscVEncoder.Slli(Reg.T0, Reg.S5, 3));
        Emit(RiscVEncoder.Add(Reg.T0, Reg.S3, Reg.T0));
        Emit(RiscVEncoder.Sd(Reg.T0, Reg.A0, 8));

        // s5++
        Emit(RiscVEncoder.Addi(Reg.S5, Reg.S5, 1));
        Emit(RiscVEncoder.J((loopStart - m_instructions.Count) * 4));

        int exitTarget = m_instructions.Count;
        m_instructions[exitIdx] = RiscVEncoder.Bge(Reg.S5, Reg.S4,
            (exitTarget - exitIdx) * 4);

        EmitEscapeHelperEpilogue();
    }

    void EmitSumTypeEscapeHelper(string name, SumType st)
    {
        EmitEscapeHelperPrologue(name);

        // s4 = tag
        Emit(RiscVEncoder.Ld(Reg.S4, Reg.S2, 0));

        List<int> jumpToEndIdxs = new();

        for (int ctorIdx = 0; ctorIdx < st.Constructors.Length; ctorIdx++)
        {
            SumConstructorType ctor = st.Constructors[ctorIdx];
            int totalSize = (1 + ctor.Fields.Length) * 8;

            if (ctorIdx < st.Constructors.Length - 1)
            {
                // Branch: bne s4, expected → next constructor
                foreach (uint insn in RiscVEncoder.Li(Reg.T0, ctorIdx)) Emit(insn);
                int branchIdx = m_instructions.Count;
                Emit(RiscVEncoder.Nop()); // patched

                EmitSumCtorHelper(ctor, totalSize);

                jumpToEndIdxs.Add(m_instructions.Count);
                Emit(RiscVEncoder.Nop()); // j → end

                int nextStart = m_instructions.Count;
                m_instructions[branchIdx] = RiscVEncoder.Bne(Reg.S4, Reg.T0,
                    (nextStart - branchIdx) * 4);
            }
            else
            {
                EmitSumCtorHelper(ctor, totalSize);
            }
        }

        int endIdx = m_instructions.Count;
        foreach (int jIdx in jumpToEndIdxs)
            m_instructions[jIdx] = RiscVEncoder.J((endIdx - jIdx) * 4);

        EmitEscapeHelperEpilogue();
    }

    void EmitSumCtorHelper(SumConstructorType ctor, int totalSize)
    {
        // Allocate in parent region
        Emit(RiscVEncoder.Mv(Reg.S3, Reg.S1));
        Emit(RiscVEncoder.Addi(Reg.S1, Reg.S1, totalSize));

        // Copy tag
        Emit(RiscVEncoder.Sd(Reg.S3, Reg.S4, 0));

        // Copy fields
        for (int i = 0; i < ctor.Fields.Length; i++)
            EmitEscapeFieldCopy((1 + i) * 8, (1 + i) * 8, ctor.Fields[i]);
    }

    // ── Relocate helpers (pointer fixup after memmove) ──────────

    string GetOrQueueRelocateHelper(CodexType type)
    {
        type = ResolveType(type);
        string key = EscapeCopyKey(type);
        if (m_relocateHelperNames.TryGetValue(key, out string? name))
            return name;
        name = $"__relocate_{key}";
        m_relocateHelperNames[key] = name;
        m_relocateHelperQueue.Enqueue((key, name, type));
        return name;
    }

    void EmitRelocateHelpers()
    {
        while (m_relocateHelperQueue.Count > 0)
        {
            (string _, string name, CodexType type) = m_relocateHelperQueue.Dequeue();
            switch (type)
            {
                case TextType:
                    break; // text has no internal pointers
                case RecordType rt:
                    EmitRecordRelocateHelper(name, rt);
                    break;
                case ListType lt:
                    EmitListRelocateHelper(name, lt);
                    break;
                case SumType st:
                    EmitSumTypeRelocateHelper(name, st);
                    break;
            }
        }
    }

    // All relocate helpers: a0=ptr (already at final position), a1=delta
    // In-place: subtract delta from each heap-typed field pointer, then recurse.
    // Convention: s2=ptr, s3=delta. Frame: 32 bytes (ra, s2, s3, pad).

    void EmitRelocateHelperPrologue(string name)
    {
        m_functionOffsets[name] = m_instructions.Count;
        // Null guard
        int notNull = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // patched: bnez a0 → continue
        Emit(RiscVEncoder.Ret());
        int continueLabel = m_instructions.Count;
        m_instructions[notNull] = RiscVEncoder.Bne(Reg.A0, Reg.Zero,
            (continueLabel - notNull) * 4);

        Emit(RiscVEncoder.Addi(Reg.Sp, Reg.Sp, -48));
        Emit(RiscVEncoder.Sd(Reg.Sp, Reg.Ra, 40));
        Emit(RiscVEncoder.Sd(Reg.Sp, Reg.S2, 32));
        Emit(RiscVEncoder.Sd(Reg.Sp, Reg.S3, 24));
        Emit(RiscVEncoder.Sd(Reg.Sp, Reg.S4, 16));
        Emit(RiscVEncoder.Sd(Reg.Sp, Reg.S5, 8));
        Emit(RiscVEncoder.Mv(Reg.S2, Reg.A0)); // s2 = ptr
        Emit(RiscVEncoder.Mv(Reg.S3, Reg.A1)); // s3 = delta
    }

    void EmitRelocateHelperEpilogue()
    {
        Emit(RiscVEncoder.Ld(Reg.S5, Reg.Sp, 8));
        Emit(RiscVEncoder.Ld(Reg.S4, Reg.Sp, 16));
        Emit(RiscVEncoder.Ld(Reg.S3, Reg.Sp, 24));
        Emit(RiscVEncoder.Ld(Reg.S2, Reg.Sp, 32));
        Emit(RiscVEncoder.Ld(Reg.Ra, Reg.Sp, 40));
        Emit(RiscVEncoder.Addi(Reg.Sp, Reg.Sp, 48));
        Emit(RiscVEncoder.Ret());
    }

    void EmitRelocateFieldAdjust(int offset, CodexType fieldType)
    {
        CodexType resolved = ResolveType(fieldType);
        if (!IRRegion.TypeNeedsHeapEscape(resolved))
            return;

        // Load field pointer, subtract delta, store back
        Emit(RiscVEncoder.Ld(Reg.T0, Reg.S2, offset));
        // Null guard for the field pointer
        int skipNull = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // patched: beqz t0 → skip
        Emit(RiscVEncoder.Sub(Reg.T0, Reg.T0, Reg.S3));
        Emit(RiscVEncoder.Sd(Reg.S2, Reg.T0, offset));

        // Recurse into nested type (if not text — text has no internal pointers)
        if (resolved is not TextType)
        {
            string helper = GetOrQueueRelocateHelper(resolved);
            Emit(RiscVEncoder.Mv(Reg.A0, Reg.T0));
            Emit(RiscVEncoder.Mv(Reg.A1, Reg.S3));
            EmitCallTo(helper);
        }

        int skipLabel = m_instructions.Count;
        m_instructions[skipNull] = RiscVEncoder.Beq(Reg.T0, Reg.Zero,
            (skipLabel - skipNull) * 4);
    }

    void EmitRecordRelocateHelper(string name, RecordType rt)
    {
        EmitRelocateHelperPrologue(name);
        for (int i = 0; i < rt.Fields.Length; i++)
            EmitRelocateFieldAdjust(i * 8, rt.Fields[i].Type);
        EmitRelocateHelperEpilogue();
    }

    void EmitListRelocateHelper(string name, ListType lt)
    {
        EmitRelocateHelperPrologue(name);

        CodexType elemType = ResolveType(lt.Element);
        bool needsReloc = IRRegion.TypeNeedsHeapEscape(elemType);
        if (!needsReloc)
        {
            EmitRelocateHelperEpilogue();
            return;
        }

        string? elemHelper = elemType is not TextType
            ? GetOrQueueRelocateHelper(elemType)
            : null;

        // s4 = length, s5 = index
        Emit(RiscVEncoder.Ld(Reg.S4, Reg.S2, 0));
        foreach (uint insn in RiscVEncoder.Li(Reg.S5, 0)) Emit(insn);

        int loopStart = m_instructions.Count;
        int loopExit = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // patched: bge s5, s4 → done

        // offset = 8 + s5 * 8
        Emit(RiscVEncoder.Slli(Reg.T0, Reg.S5, 3));
        Emit(RiscVEncoder.Addi(Reg.T0, Reg.T0, 8));

        // Load element pointer, subtract delta, store back
        Emit(RiscVEncoder.Add(Reg.T1, Reg.S2, Reg.T0));
        Emit(RiscVEncoder.Ld(Reg.T2, Reg.T1, 0));
        // Null guard
        int skipElem = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // patched: beqz t2 → skip
        Emit(RiscVEncoder.Sub(Reg.T2, Reg.T2, Reg.S3));
        Emit(RiscVEncoder.Sd(Reg.T1, Reg.T2, 0));

        // Recurse if needed
        if (elemHelper is not null)
        {
            Emit(RiscVEncoder.Mv(Reg.A0, Reg.T2));
            Emit(RiscVEncoder.Mv(Reg.A1, Reg.S3));
            EmitCallTo(elemHelper);
        }

        int skipLabel = m_instructions.Count;
        m_instructions[skipElem] = RiscVEncoder.Beq(Reg.T2, Reg.Zero,
            (skipLabel - skipElem) * 4);

        Emit(RiscVEncoder.Addi(Reg.S5, Reg.S5, 1));
        Emit(RiscVEncoder.J((loopStart - m_instructions.Count) * 4));

        int doneLabel = m_instructions.Count;
        m_instructions[loopExit] = RiscVEncoder.Bge(Reg.S5, Reg.S4,
            (doneLabel - loopExit) * 4);

        EmitRelocateHelperEpilogue();
    }

    void EmitSumTypeRelocateHelper(string name, SumType st)
    {
        EmitRelocateHelperPrologue(name);

        // s4 = tag
        Emit(RiscVEncoder.Ld(Reg.S4, Reg.S2, 0));

        List<int> jumpToEndIdxs = [];

        for (int ctorIdx = 0; ctorIdx < st.Constructors.Length; ctorIdx++)
        {
            SumConstructorType ctor = st.Constructors[ctorIdx];

            if (ctorIdx < st.Constructors.Length - 1)
            {
                foreach (uint insn in RiscVEncoder.Li(Reg.T0, ctorIdx)) Emit(insn);
                int branchIdx = m_instructions.Count;
                Emit(RiscVEncoder.Nop()); // patched: bne s4, t0 → next

                for (int i = 0; i < ctor.Fields.Length; i++)
                    EmitRelocateFieldAdjust((1 + i) * 8, ctor.Fields[i]);

                jumpToEndIdxs.Add(m_instructions.Count);
                Emit(RiscVEncoder.Nop()); // patched: j → end

                int nextLabel = m_instructions.Count;
                m_instructions[branchIdx] = RiscVEncoder.Bne(Reg.S4, Reg.T0,
                    (nextLabel - branchIdx) * 4);
            }
            else
            {
                for (int i = 0; i < ctor.Fields.Length; i++)
                    EmitRelocateFieldAdjust((1 + i) * 8, ctor.Fields[i]);
            }
        }

        int endLabel = m_instructions.Count;
        foreach (int jIdx in jumpToEndIdxs)
            m_instructions[jIdx] = RiscVEncoder.J((endLabel - jIdx) * 4);

        EmitRelocateHelperEpilogue();
    }

    uint EmitList(IRList list)
    {
        // Layout: [capacity @ -8 | count @ 0 | elem0 @ 8 | ...]
        List<uint> elemRegs = new();
        foreach (IRExpr elem in list.Elements)
        {
            uint r = EmitExpr(elem);
            uint saved = AllocLocal();
            StoreLocal(saved, r);
            elemRegs.Add(saved);
        }

        int count = list.Elements.Length;

        // Store capacity = count at [S1] (tight allocation)
        uint capReg = AllocTemp();
        foreach (uint insn in RiscVEncoder.Li(capReg, count)) Emit(insn);
        Emit(RiscVEncoder.Sd(Reg.S1, capReg, 0));
        Emit(RiscVEncoder.Addi(Reg.S1, Reg.S1, 8)); // past capacity word

        // List pointer = S1 (now pointing at count word)
        uint ptrReg = AllocTemp();
        Emit(RiscVEncoder.Mv(ptrReg, Reg.S1));
        Emit(RiscVEncoder.Addi(Reg.S1, Reg.S1, (count + 1) * 8)); // count word + elements

        // Store count
        uint lenReg = AllocTemp();
        foreach (uint insn in RiscVEncoder.Li(lenReg, count)) Emit(insn);
        Emit(RiscVEncoder.Sd(ptrReg, lenReg, 0));

        // Store elements (offsets unchanged: 8 + i*8)
        for (int i = 0; i < elemRegs.Count; i++)
            Emit(RiscVEncoder.Sd(ptrReg, LoadLocal(elemRegs[i]), 8 + i * 8));

        return ptrReg;
    }

    uint EmitError(IRError err)
    {
        // Print error message to stderr and exit(1)
        uint msgReg = EmitTextLit(err.Message);
        Emit(RiscVEncoder.Ld(Reg.A2, msgReg, 0));
        Emit(RiscVEncoder.Addi(Reg.A1, msgReg, 8));
        if (m_target != RiscVTarget.BareMetal)
        {
            foreach (uint insn in RiscVEncoder.Li(Reg.A7, 64)) Emit(insn);
            foreach (uint insn in RiscVEncoder.Li(Reg.A0, 2)) Emit(insn); // stderr
            Emit(RiscVEncoder.Ecall());
        }
        foreach (uint insn in RiscVEncoder.Li(Reg.A0, 1)) Emit(insn);
        EmitSyscallExit(Reg.A0);
        return Reg.Zero;
    }

    void EmitLoadFunctionAddress(uint rd, string funcName)
    {
        // Reserve 2 nop slots, patched in PatchCalls with the function's absolute address
        m_funcAddrFixups.Add(new FuncAddrFixup(m_instructions.Count, rd, funcName));
        Emit(RiscVEncoder.Nop());
        Emit(RiscVEncoder.Nop());
    }

    uint EmitLiteralValue(object value) => value switch
    {
        long l => EmitIntegerLit(l),
        bool b => EmitIntegerLit(b ? 1 : 0),
        string s => EmitTextLit(s),
        _ => Reg.Zero
    };

    // ── Builtins ─────────────────────────────────────────────────

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
                Emit(RiscVEncoder.Ld(Reg.A0, ptr, 0));
                return true;
            }

            case "integer-to-text" when args.Count == 1:
            {
                uint val = EmitExpr(args[0]);
                Emit(RiscVEncoder.Mv(Reg.A0, val));
                EmitCallTo("__itoa");
                return true;
            }

            case "text-to-integer" when args.Count == 1:
            {
                uint ptr = EmitExpr(args[0]);
                Emit(RiscVEncoder.Mv(Reg.A0, ptr));
                EmitCallTo("__text_to_int");
                return true;
            }

            case "char-at" when args.Count == 2:
            {
                // char-at returns byte value as integer: lbu from [text+8+idx]
                uint textReg = EmitExpr(args[0]);
                uint savedText = AllocLocal();
                StoreLocal(savedText, textReg);
                uint indexReg = EmitExpr(args[1]);
                Emit(RiscVEncoder.Mv(Reg.T2, indexReg)); // save index before LoadLocal
                uint textVal = LoadLocal(savedText);
                Emit(RiscVEncoder.Add(Reg.T0, textVal, Reg.T2));
                Emit(RiscVEncoder.Lbu(Reg.A0, Reg.T0, 8)); // skip 8-byte length prefix
                return true;
            }

            case "substring" when args.Count == 3:
                EmitSubstring(args);
                return true;

            case "show" when args.Count == 1:
                EmitShow(args[0]);
                return true;

            case "list-length" when args.Count == 1:
            {
                uint ptr = EmitExpr(args[0]);
                Emit(RiscVEncoder.Ld(Reg.A0, ptr, 0));
                return true;
            }

            case "list-at" when args.Count == 2:
            {
                uint listReg = EmitExpr(args[0]);
                uint savedList = AllocLocal();
                StoreLocal(savedList, listReg);
                uint idxReg = EmitExpr(args[1]);
                // Load element at offset 8 + idx * 8
                // Use T2 for idx*8 to avoid LoadLocal clobbering T0
                foreach (uint insn in RiscVEncoder.Li(Reg.T2, 8)) Emit(insn);
                Emit(RiscVEncoder.Mul(Reg.T2, idxReg, Reg.T2));
                uint listVal = LoadLocal(savedList);
                Emit(RiscVEncoder.Add(Reg.T0, listVal, Reg.T2));
                Emit(RiscVEncoder.Ld(Reg.A0, Reg.T0, 8));
                return true;
            }

            case "read-line" when args.Count == 0:
                EmitCallTo("__read_line");
                return true;

            case "read-file" when args.Count == 1:
            {
                uint pathReg = EmitExpr(args[0]);
                Emit(RiscVEncoder.Mv(Reg.A0, pathReg));
                EmitCallTo("__read_file");
                return true;
            }

            case "write-file" when args.Count == 2:
            {
                // Stub: writes content to stdout with CCE→Unicode conversion.
                // Path is evaluated but ignored. Proper file I/O is future work.
                uint pathReg = EmitExpr(args[0]);
                uint savedPath = AllocLocal();
                StoreLocal(savedPath, pathReg);
                uint contentReg = EmitExpr(args[1]);
                // Reuse EmitPrintText for CCE→Unicode output (without trailing newline)
                // For now, call the same per-byte path but skip the newline:
                EmitPrintTextNoNewline(contentReg);
                Emit(RiscVEncoder.Mv(Reg.A0, Reg.Zero));
                return true;
            }

            case "file-exists" when args.Count == 1:
            {
                // Simplified: try openat, if fd >= 0 exists, close and return true
                uint pathReg = EmitExpr(args[0]);
                Emit(RiscVEncoder.Mv(Reg.A0, pathReg));
                EmitCallTo("__read_file"); // reuse read_file, if it doesn't crash it exists
                // Simplified: just return true (1) for now
                foreach (uint insn in RiscVEncoder.Li(Reg.A0, 1)) Emit(insn);
                return true;
            }

            case "get-args" when args.Count == 0:
            {
                // Return empty list: [capacity=0 | count=0]
                Emit(RiscVEncoder.Sd(Reg.S1, Reg.Zero, 0));       // capacity = 0
                Emit(RiscVEncoder.Addi(Reg.S1, Reg.S1, 8));       // past capacity
                Emit(RiscVEncoder.Mv(Reg.A0, Reg.S1));            // list ptr = count word
                Emit(RiscVEncoder.Sd(Reg.S1, Reg.Zero, 0));       // count = 0
                Emit(RiscVEncoder.Addi(Reg.S1, Reg.S1, 8));
                return true;
            }

            case "current-dir" when args.Count == 0:
            {
                // Return "." as current dir
                uint dotReg = EmitTextLit(".");
                Emit(RiscVEncoder.Mv(Reg.A0, dotReg));
                return true;
            }

            case "char-code-at" when args.Count == 2:
            {
                // (long)text[index] — load byte at text_data + index
                uint textReg = EmitExpr(args[0]);
                uint savedText = AllocLocal();
                StoreLocal(savedText, textReg);
                uint idxReg = EmitExpr(args[1]);
                Emit(RiscVEncoder.Mv(Reg.T2, idxReg)); // save idx before LoadLocal
                uint textVal = LoadLocal(savedText);
                Emit(RiscVEncoder.Add(Reg.T0, textVal, Reg.T2));
                Emit(RiscVEncoder.Lbu(Reg.A0, Reg.T0, 8)); // skip 8-byte length prefix
                return true;
            }

            case "char-code" when args.Count == 1:
            {
                // char-code: identity — Char is already an integer
                uint charReg = EmitExpr(args[0]);
                Emit(RiscVEncoder.Mv(Reg.A0, charReg));
                return true;
            }

            case "code-to-char" when args.Count == 1:
            {
                // code-to-char: identity — Char is already an integer
                uint codeReg = EmitExpr(args[0]);
                Emit(RiscVEncoder.Mv(Reg.A0, codeReg));
                return true;
            }

            case "char-to-text" when args.Count == 1:
            {
                // Allocate 1-char string on heap: [8-byte len=1][1 byte data][7 padding]
                uint codeReg = EmitExpr(args[0]);
                Emit(RiscVEncoder.Mv(Reg.T2, codeReg));      // save char before clobbering A0
                Emit(RiscVEncoder.Mv(Reg.A0, Reg.S1));       // result = heap ptr
                Emit(RiscVEncoder.Addi(Reg.S1, Reg.S1, 16)); // alloc 16 bytes
                foreach (uint insn in RiscVEncoder.Li(Reg.T0, 1)) Emit(insn);
                Emit(RiscVEncoder.Sd(Reg.A0, Reg.T0, 0));    // length = 1
                Emit(RiscVEncoder.Sb(Reg.A0, Reg.T2, 8));    // store char byte
                return true;
            }

            case "is-letter" when args.Count == 1:
            {
                // CCE: letters are 13-64 (lowercase 13-38, uppercase 39-64)
                // Single range check: (val - 13) < (64 - 13 + 1) unsigned
                uint charReg = EmitExpr(args[0]);
                Emit(RiscVEncoder.Addi(Reg.T0, charReg, -13));          // t0 = val - 13
                Emit(RiscVEncoder.Sltiu(Reg.A0, Reg.T0, 64 - 13 + 1)); // a0 = (t0 < 52) unsigned
                return true;
            }

            case "is-digit" when args.Count == 1:
            {
                // CCE: digits are 3-12
                // (val - 3) < 10 unsigned
                uint charReg = EmitExpr(args[0]);
                Emit(RiscVEncoder.Addi(Reg.T0, charReg, -3));     // t0 = val - 3
                Emit(RiscVEncoder.Sltiu(Reg.A0, Reg.T0, 10));     // a0 = (t0 < 10) unsigned
                return true;
            }

            case "is-whitespace" when args.Count == 1:
            {
                // CCE: whitespace is 0-2 (NUL=0, LF=1, Space=2)
                uint charReg = EmitExpr(args[0]);
                Emit(RiscVEncoder.Sltiu(Reg.A0, charReg, 3));     // a0 = (val < 3) unsigned
                return true;
            }

            case "negate" when args.Count == 1:
            {
                uint val = EmitExpr(args[0]);
                Emit(RiscVEncoder.Sub(Reg.A0, Reg.Zero, val));
                return true;
            }

            case "text-contains" when args.Count == 2:
            {
                uint textReg = EmitExpr(args[0]);
                uint savedText = AllocLocal();
                StoreLocal(savedText, textReg);
                uint needleReg = EmitExpr(args[1]);
                Emit(RiscVEncoder.Mv(Reg.A1, needleReg));
                Emit(RiscVEncoder.Mv(Reg.A0, LoadLocal(savedText)));
                EmitCallTo("__text_contains");
                return true;
            }

            case "text-starts-with" when args.Count == 2:
            {
                uint textReg = EmitExpr(args[0]);
                uint savedText = AllocLocal();
                StoreLocal(savedText, textReg);
                uint prefixReg = EmitExpr(args[1]);
                Emit(RiscVEncoder.Mv(Reg.A1, prefixReg));
                Emit(RiscVEncoder.Mv(Reg.A0, LoadLocal(savedText)));
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
                Emit(RiscVEncoder.Mv(Reg.A2, newReg));
                Emit(RiscVEncoder.Mv(Reg.A1, LoadLocal(savedOld)));
                Emit(RiscVEncoder.Mv(Reg.A0, LoadLocal(savedText)));
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
                Emit(RiscVEncoder.Mv(taskPtr, Reg.S1));
                Emit(RiscVEncoder.Sd(Reg.S1, Reg.Zero, 0)); // done = 0
                Emit(RiscVEncoder.Sd(Reg.S1, Reg.Zero, 8)); // result = 0
                Emit(RiscVEncoder.Addi(Reg.S1, Reg.S1, 16));
                uint savedTask = AllocLocal();
                StoreLocal(savedTask, taskPtr);

                // Call thunk(null): thunk is a closure [code_ptr, caps...], load code ptr then call
                // Trampoline expects T2 = closure pointer (for captured arg access)
                uint thunkLoaded = LoadLocal(savedThunk);
                Emit(RiscVEncoder.Mv(Reg.A0, Reg.Zero)); // arg = null
                Emit(RiscVEncoder.Mv(Reg.T2, thunkLoaded)); // T2 = closure (trampoline convention)
                Emit(RiscVEncoder.Ld(Reg.T0, thunkLoaded, 0)); // T0 = [thunk+0] = code ptr
                Emit(RiscVEncoder.Jalr(Reg.Ra, Reg.T0, 0));

                // Store result (A0) into task[8], set done
                uint taskLoaded = LoadLocal(savedTask);
                Emit(RiscVEncoder.Sd(taskLoaded, Reg.A0, 8)); // task[8] = result
                foreach (uint insn in RiscVEncoder.Li(Reg.T0, 1)) Emit(insn);
                Emit(RiscVEncoder.Sd(taskLoaded, Reg.T0, 0)); // task[0] = 1

                // TryEmitBuiltin caller expects result in A0
                Emit(RiscVEncoder.Mv(Reg.A0, taskLoaded));
                return true;
            }

            case "await" when args.Count == 1:
            {
                // Sequential: just load result from task[8]
                uint taskPtr = EmitExpr(args[0]);
                Emit(RiscVEncoder.Ld(Reg.A0, taskPtr, 8));
                return true;
            }

            case "text-compare" when args.Count == 2:
            {
                uint aReg = EmitExpr(args[0]);
                uint savedA = AllocLocal();
                StoreLocal(savedA, aReg);
                uint bReg = EmitExpr(args[1]);
                Emit(RiscVEncoder.Mv(Reg.A1, bReg));
                Emit(RiscVEncoder.Mv(Reg.A0, LoadLocal(savedA)));
                EmitCallTo("__text_compare");
                return true;
            }

            case "list-cons" when args.Count == 2:
            {
                uint headReg = EmitExpr(args[0]);
                uint savedHead = AllocLocal();
                StoreLocal(savedHead, headReg);
                uint tailReg = EmitExpr(args[1]);
                Emit(RiscVEncoder.Mv(Reg.A1, tailReg));
                Emit(RiscVEncoder.Mv(Reg.A0, LoadLocal(savedHead)));
                EmitCallTo("__list_cons");
                return true;
            }

            case "list-append" when args.Count == 2:
            {
                uint l1 = EmitExpr(args[0]);
                uint savedL1 = AllocLocal();
                StoreLocal(savedL1, l1);
                uint l2 = EmitExpr(args[1]);
                Emit(RiscVEncoder.Mv(Reg.A1, l2));
                Emit(RiscVEncoder.Mv(Reg.A0, LoadLocal(savedL1)));
                EmitCallTo("__list_append");
                return true;
            }

            case "list-snoc" when args.Count == 2:
            {
                uint listReg = EmitExpr(args[0]);
                uint savedList = AllocLocal();
                StoreLocal(savedList, listReg);
                uint elemReg = EmitExpr(args[1]);
                Emit(RiscVEncoder.Mv(Reg.A1, elemReg));
                Emit(RiscVEncoder.Mv(Reg.A0, LoadLocal(savedList)));
                EmitCallTo("__list_snoc");
                return true;
            }

            case "list-insert-at" when args.Count == 3:
            {
                uint listReg = EmitExpr(args[0]);
                uint savedList = AllocLocal();
                StoreLocal(savedList, listReg);
                uint idxReg = EmitExpr(args[1]);
                uint savedIdx = AllocLocal();
                StoreLocal(savedIdx, idxReg);
                uint elemReg = EmitExpr(args[2]);
                Emit(RiscVEncoder.Mv(Reg.A2, elemReg));
                Emit(RiscVEncoder.Mv(Reg.A1, LoadLocal(savedIdx)));
                Emit(RiscVEncoder.Mv(Reg.A0, LoadLocal(savedList)));
                EmitCallTo("__list_insert_at");
                return true;
            }

            case "list-contains" when args.Count == 2:
            {
                uint listReg = EmitExpr(args[0]);
                uint savedList = AllocLocal();
                StoreLocal(savedList, listReg);
                uint elemReg = EmitExpr(args[1]);
                Emit(RiscVEncoder.Mv(Reg.A1, elemReg));
                Emit(RiscVEncoder.Mv(Reg.A0, LoadLocal(savedList)));
                EmitCallTo("__list_contains");
                return true;
            }

            case "text-concat-list" when args.Count == 1:
            {
                uint listReg = EmitExpr(args[0]);
                Emit(RiscVEncoder.Mv(Reg.A0, listReg));
                EmitCallTo("__text_concat_list");
                return true;
            }

            case "text-split" when args.Count == 2:
            {
                uint textReg = EmitExpr(args[0]);
                uint savedText = AllocLocal();
                StoreLocal(savedText, textReg);
                uint delimReg = EmitExpr(args[1]);
                Emit(RiscVEncoder.Mv(Reg.A1, delimReg));
                Emit(RiscVEncoder.Mv(Reg.A0, LoadLocal(savedText)));
                EmitCallTo("__text_split");
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
        // Save heap pointer (s1 is used as scratch in itoa)
        Emit(RiscVEncoder.Addi(Reg.Sp, Reg.Sp, -16));
        Emit(RiscVEncoder.Sd(Reg.Sp, Reg.S1, 0));

        // itoa on stack: divide by 10, store digits right-to-left, then write(2, buf, len)
        Emit(RiscVEncoder.Mv(Reg.S1, valueReg));
        Emit(RiscVEncoder.Addi(Reg.Sp, Reg.Sp, -32));

        // t0 = is_negative
        Emit(RiscVEncoder.Slt(Reg.T0, Reg.S1, Reg.Zero));
        int skipNegIndex = m_instructions.Count;
        Emit(RiscVEncoder.Nop());
        Emit(RiscVEncoder.Sub(Reg.S1, Reg.Zero, Reg.S1));
        int skipNegTarget = m_instructions.Count;
        m_instructions[skipNegIndex] = RiscVEncoder.Beq(Reg.T0, Reg.Zero,
            (skipNegTarget - skipNegIndex) * 4);

        // t1 = write cursor (fills right-to-left from sp+30)
        Emit(RiscVEncoder.Addi(Reg.T1, Reg.Sp, 30));

        // Newline at end
        foreach (uint insn in RiscVEncoder.Li(Reg.T2, '\n')) Emit(insn);
        Emit(RiscVEncoder.Sb(Reg.T1, Reg.T2, 0));
        Emit(RiscVEncoder.Addi(Reg.T1, Reg.T1, -1));

        // Zero special case
        int skipZeroIndex = m_instructions.Count;
        Emit(RiscVEncoder.Nop());
        foreach (uint insn in RiscVEncoder.Li(Reg.T2, '0')) Emit(insn);
        Emit(RiscVEncoder.Sb(Reg.T1, Reg.T2, 0));
        Emit(RiscVEncoder.Addi(Reg.T1, Reg.T1, -1));
        int skipZeroJumpIndex = m_instructions.Count;
        Emit(RiscVEncoder.Nop());

        // Digit loop
        int digitLoopStart = m_instructions.Count;
        m_instructions[skipZeroIndex] = RiscVEncoder.Bne(Reg.S1, Reg.Zero,
            (digitLoopStart - skipZeroIndex) * 4);

        int loopExitIndex = m_instructions.Count;
        Emit(RiscVEncoder.Nop());

        foreach (uint insn in RiscVEncoder.Li(Reg.T3, 10)) Emit(insn);
        Emit(RiscVEncoder.Rem(Reg.T2, Reg.S1, Reg.T3));
        Emit(RiscVEncoder.Addi(Reg.T2, Reg.T2, '0'));
        Emit(RiscVEncoder.Sb(Reg.T1, Reg.T2, 0));
        Emit(RiscVEncoder.Addi(Reg.T1, Reg.T1, -1));
        Emit(RiscVEncoder.Div(Reg.S1, Reg.S1, Reg.T3));
        Emit(RiscVEncoder.J((digitLoopStart - m_instructions.Count) * 4));

        int doneDigits = m_instructions.Count;
        m_instructions[loopExitIndex] = RiscVEncoder.Beq(Reg.S1, Reg.Zero,
            (doneDigits - loopExitIndex) * 4);
        m_instructions[skipZeroJumpIndex] = RiscVEncoder.J(
            (doneDigits - skipZeroJumpIndex) * 4);

        // Negative sign
        int skipMinusIndex = m_instructions.Count;
        Emit(RiscVEncoder.Nop());
        foreach (uint insn in RiscVEncoder.Li(Reg.T2, '-')) Emit(insn);
        Emit(RiscVEncoder.Sb(Reg.T1, Reg.T2, 0));
        Emit(RiscVEncoder.Addi(Reg.T1, Reg.T1, -1));
        int skipMinusTarget = m_instructions.Count;
        m_instructions[skipMinusIndex] = RiscVEncoder.Beq(Reg.T0, Reg.Zero,
            (skipMinusTarget - skipMinusIndex) * 4);

        // write(1, buf_start, len)
        Emit(RiscVEncoder.Addi(Reg.T1, Reg.T1, 1));
        Emit(RiscVEncoder.Addi(Reg.T2, Reg.Sp, 31));
        Emit(RiscVEncoder.Sub(Reg.A2, Reg.T2, Reg.T1));
        Emit(RiscVEncoder.Mv(Reg.A1, Reg.T1));
        EmitSyscallWrite();

        Emit(RiscVEncoder.Addi(Reg.Sp, Reg.Sp, 32));

        // Restore heap pointer
        Emit(RiscVEncoder.Ld(Reg.S1, Reg.Sp, 0));
        Emit(RiscVEncoder.Addi(Reg.Sp, Reg.Sp, 16));
    }

    void EmitPrintBool(uint valueReg)
    {
        int trueOffset = AddRodataString("True\n");
        int falseOffset = AddRodataString("False\n");

        int branchIndex = m_instructions.Count;
        Emit(RiscVEncoder.Nop());

        uint trueReg = AllocTemp();
        EmitLoadRodataAddress(trueReg, trueOffset);
        Emit(RiscVEncoder.Ld(Reg.A2, trueReg, 0));
        Emit(RiscVEncoder.Addi(Reg.A1, trueReg, 8));
        EmitSyscallWrite();
        int jEndIndex = m_instructions.Count;
        Emit(RiscVEncoder.Nop());

        int falseStart = m_instructions.Count;
        uint falseReg = AllocTemp();
        EmitLoadRodataAddress(falseReg, falseOffset);
        Emit(RiscVEncoder.Ld(Reg.A2, falseReg, 0));
        Emit(RiscVEncoder.Addi(Reg.A1, falseReg, 8));
        EmitSyscallWrite();

        int endIndex = m_instructions.Count;
        m_instructions[branchIndex] = RiscVEncoder.Beq(valueReg, Reg.Zero,
            (falseStart - branchIndex) * 4);
        m_instructions[jEndIndex] = RiscVEncoder.J((endIndex - jEndIndex) * 4);
    }

    void EmitPrintTextNoNewline(uint ptrReg)
    {
        // Strings are CCE-encoded. Output requires CCE→Unicode conversion per byte.
        // The CceToUnicode table (128 bytes) lives in .rodata at m_cceToUnicodeTableOffset.

        // Save ptrReg and load table address into callee-saved regs for the loop.
        uint savedPtr = AllocLocal();
        StoreLocal(savedPtr, ptrReg);
        uint savedTable = AllocLocal();
        uint tableReg = AllocTemp();
        EmitLoadRodataAddress(tableReg, m_cceToUnicodeTableOffset);
        StoreLocal(savedTable, tableReg);

        // Load length
        uint ptr = LoadLocal(savedPtr);
        Emit(RiscVEncoder.Ld(Reg.T4, ptr, 0));  // t4 = length
        uint savedLen = AllocLocal();
        StoreLocal(savedLen, Reg.T4);

        // idx = 0
        uint savedIdx = AllocLocal();
        foreach (uint insn in RiscVEncoder.Li(Reg.T5, 0)) Emit(insn);
        StoreLocal(savedIdx, Reg.T5);

        // Loop: for each CCE byte, convert to Unicode and write.
        // Use explicit T-register assignments to avoid m_loadLocalToggle parity issues.
        // The loop body uses LoadLocal which advances the toggle; if the total number
        // of spill loads per iteration is odd, subsequent iterations (or subsequent
        // inlined copies of this loop) would swap T0/T1 assignments, causing clobbers.
        int loopTop = m_instructions.Count;
        uint idxTop = LoadLocal(savedIdx);
        uint lenTop = LoadLocal(savedLen);
        int doneJump = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // patched: bge idxTop, lenTop → done

        // Load CCE byte: ptr[8 + idx]
        uint ptrL = LoadLocal(savedPtr);
        uint idx = LoadLocal(savedIdx);
        Emit(RiscVEncoder.Add(Reg.T2, ptrL, idx));
        Emit(RiscVEncoder.Lbu(Reg.T2, Reg.T2, 8)); // t2 = CCE byte

        // Convert CCE→Unicode: table[CCE byte]
        uint tbl = LoadLocal(savedTable);
        Emit(RiscVEncoder.Add(Reg.T0, tbl, Reg.T2));
        Emit(RiscVEncoder.Lbu(Reg.T0, Reg.T0, 0)); // t0 = Unicode byte

        if (m_target == RiscVTarget.BareMetal)
        {
            foreach (uint insn in RiscVEncoder.Li(Reg.T1, UartBase)) Emit(insn);
            Emit(RiscVEncoder.Sb(Reg.T1, Reg.T0, 0));
        }
        else
        {
            Emit(RiscVEncoder.Addi(Reg.Sp, Reg.Sp, -8));
            Emit(RiscVEncoder.Sb(Reg.Sp, Reg.T0, 0));
            foreach (uint insn in RiscVEncoder.Li(Reg.A0, 1)) Emit(insn);
            Emit(RiscVEncoder.Mv(Reg.A1, Reg.Sp));
            foreach (uint insn in RiscVEncoder.Li(Reg.A2, 1)) Emit(insn);
            foreach (uint insn in RiscVEncoder.Li(Reg.A7, 64)) Emit(insn);
            Emit(RiscVEncoder.Ecall());
            Emit(RiscVEncoder.Addi(Reg.Sp, Reg.Sp, 8));
        }

        // idx++
        idx = LoadLocal(savedIdx);
        Emit(RiscVEncoder.Addi(idx, idx, 1));
        StoreLocal(savedIdx, idx);
        Emit(RiscVEncoder.J((loopTop - m_instructions.Count) * 4));

        int doneTarget = m_instructions.Count;
        m_instructions[doneJump] = RiscVEncoder.Bge(idxTop, lenTop,
            (doneTarget - doneJump) * 4);
    }

    void EmitPrintText(uint ptrReg)
    {
        EmitPrintTextNoNewline(ptrReg);

        // Newline: output literal Unicode 0x0A (not via CCE)
        if (m_target == RiscVTarget.BareMetal)
        {
            foreach (uint insn in RiscVEncoder.Li(Reg.T0, UartBase)) Emit(insn);
            foreach (uint insn in RiscVEncoder.Li(Reg.T1, '\n')) Emit(insn);
            Emit(RiscVEncoder.Sb(Reg.T0, Reg.T1, 0));
        }
        else
        {
            Emit(RiscVEncoder.Addi(Reg.Sp, Reg.Sp, -8));
            foreach (uint insn in RiscVEncoder.Li(Reg.T0, '\n')) Emit(insn);
            Emit(RiscVEncoder.Sb(Reg.Sp, Reg.T0, 0));
            foreach (uint insn in RiscVEncoder.Li(Reg.A0, 1)) Emit(insn);
            Emit(RiscVEncoder.Mv(Reg.A1, Reg.Sp));
            foreach (uint insn in RiscVEncoder.Li(Reg.A2, 1)) Emit(insn);
            foreach (uint insn in RiscVEncoder.Li(Reg.A7, 64)) Emit(insn);
            Emit(RiscVEncoder.Ecall());
            Emit(RiscVEncoder.Addi(Reg.Sp, Reg.Sp, 8));
        }
    }

    int AddRodataString(string value)
    {
        if (m_stringOffsets.TryGetValue(value, out int offset))
            return offset;
        offset = m_rodata.Count;
        // CCE-encode, matching EmitTextLit
        string cceEncoded = CceTable.Encode(value);
        byte[] cceBytes = new byte[cceEncoded.Length];
        for (int i = 0; i < cceEncoded.Length; i++)
            cceBytes[i] = (byte)cceEncoded[i];
        m_rodata.AddRange(BitConverter.GetBytes((long)cceBytes.Length));
        m_rodata.AddRange(cceBytes);
        while (m_rodata.Count % 8 != 0) m_rodata.Add(0);
        m_stringOffsets[value] = offset;
        return offset;
    }

    void EmitSubstring(List<IRExpr> args)
    {
        uint textReg = EmitExpr(args[0]);
        uint savedText = AllocLocal();
        StoreLocal(savedText, textReg);

        uint startReg = EmitExpr(args[1]);
        uint savedStart = AllocLocal();
        StoreLocal(savedStart, startReg);

        uint lenReg = EmitExpr(args[2]);
        Emit(RiscVEncoder.Mv(Reg.T5, lenReg));

        // Alloc: result = s1; s1 += align8(8 + len)
        Emit(RiscVEncoder.Mv(Reg.A0, Reg.S1));
        Emit(RiscVEncoder.Addi(Reg.T0, Reg.T5, 15));
        Emit(RiscVEncoder.Andi(Reg.T0, Reg.T0, -8));
        Emit(RiscVEncoder.Add(Reg.S1, Reg.S1, Reg.T0));

        Emit(RiscVEncoder.Sd(Reg.A0, Reg.T5, 0));

        // Copy: src = text + 8 + start, dst = result + 8
        // Load both into T2/T0 separately to avoid LoadLocal clobber
        uint subTextVal = LoadLocal(savedText);
        Emit(RiscVEncoder.Mv(Reg.T2, subTextVal));
        uint subStartVal = LoadLocal(savedStart);
        Emit(RiscVEncoder.Add(Reg.T0, Reg.T2, subStartVal));
        Emit(RiscVEncoder.Addi(Reg.T0, Reg.T0, 8));
        Emit(RiscVEncoder.Addi(Reg.T1, Reg.A0, 8));

        foreach (uint insn in RiscVEncoder.Li(Reg.T2, 0)) Emit(insn);
        int loopStart = m_instructions.Count;
        int exitIdx = m_instructions.Count;
        Emit(RiscVEncoder.Nop());
        Emit(RiscVEncoder.Add(Reg.T3, Reg.T0, Reg.T2));
        Emit(RiscVEncoder.Lbu(Reg.T3, Reg.T3, 0));
        Emit(RiscVEncoder.Add(Reg.T4, Reg.T1, Reg.T2));
        Emit(RiscVEncoder.Sb(Reg.T4, Reg.T3, 0));
        Emit(RiscVEncoder.Addi(Reg.T2, Reg.T2, 1));
        Emit(RiscVEncoder.J((loopStart - m_instructions.Count) * 4));
        int exitTarget = m_instructions.Count;
        m_instructions[exitIdx] = RiscVEncoder.Bge(Reg.T2, Reg.T5, (exitTarget - exitIdx) * 4);
    }

    void EmitShow(IRExpr arg)
    {
        uint valReg = EmitExpr(arg);
        switch (arg.Type)
        {
            case IntegerType:
                Emit(RiscVEncoder.Mv(Reg.A0, valReg));
                EmitCallTo("__itoa");
                break;
            case BooleanType:
            {
                int trueOff = AddRodataString("True");
                int falseOff = AddRodataString("False");
                int brIdx = m_instructions.Count;
                Emit(RiscVEncoder.Nop());
                uint trReg = AllocTemp();
                EmitLoadRodataAddress(trReg, trueOff);
                Emit(RiscVEncoder.Mv(Reg.A0, trReg));
                int jIdx = m_instructions.Count;
                Emit(RiscVEncoder.Nop());
                int flLbl = m_instructions.Count;
                uint flReg = AllocTemp();
                EmitLoadRodataAddress(flReg, falseOff);
                Emit(RiscVEncoder.Mv(Reg.A0, flReg));
                int endLbl = m_instructions.Count;
                m_instructions[brIdx] = RiscVEncoder.Beq(valReg, Reg.Zero, (flLbl - brIdx) * 4);
                m_instructions[jIdx] = RiscVEncoder.J((endLbl - jIdx) * 4);
                break;
            }
            default:
                Emit(RiscVEncoder.Mv(Reg.A0, valReg));
                break;
        }
    }

    // ── Runtime helper functions ─────────────────────────────────

    void EmitRuntimeHelpers()
    {
        EmitStrEqHelper();
        EmitStrConcatHelper();
        EmitItoaHelper();
        EmitTextToIntHelper();
        EmitListConsHelper();
        EmitListAppendHelper();
        EmitIpowHelper();
        EmitReadFileHelper();
        EmitReadLineHelper();
        EmitStrReplaceHelper();
        EmitTextContainsHelper();
        EmitTextStartsWithHelper();
        EmitTextCompareHelper();
        EmitListSnocHelper();
        EmitListInsertAtHelper();
        EmitListContainsHelper();
        EmitTextConcatListHelper();
        EmitTextSplitHelper();
        EmitEscapeTextHelper();
        EmitMemmoveHelper();
        if (m_target == RiscVTarget.BareMetal)
            EmitBareMetalReadSerialHelper();
    }

    void EmitTextContainsHelper()
    {
        // __text_contains: a0=text, a1=needle → a0=1/0
        m_functionOffsets["__text_contains"] = m_instructions.Count;

        // Save callee-saved regs
        Emit(RiscVEncoder.Addi(Reg.Sp, Reg.Sp, -48));
        Emit(RiscVEncoder.Sd(Reg.Sp, Reg.Ra, 40));
        Emit(RiscVEncoder.Sd(Reg.Sp, Reg.S2, 32));
        Emit(RiscVEncoder.Sd(Reg.Sp, Reg.S3, 24));
        Emit(RiscVEncoder.Sd(Reg.Sp, Reg.S4, 16));
        Emit(RiscVEncoder.Sd(Reg.Sp, Reg.S5, 8));

        Emit(RiscVEncoder.Mv(Reg.S2, Reg.A0)); // s2 = text
        Emit(RiscVEncoder.Mv(Reg.S3, Reg.A1)); // s3 = needle
        Emit(RiscVEncoder.Ld(Reg.S4, Reg.S2, 0)); // s4 = text_len
        Emit(RiscVEncoder.Ld(Reg.S5, Reg.S3, 0)); // s5 = needle_len
        foreach (uint insn in RiscVEncoder.Li(Reg.T0, 0)) Emit(insn); // t0 = i

        // Outer loop: i from 0 to text_len - needle_len
        int outerLoop = m_instructions.Count;
        Emit(RiscVEncoder.Sub(Reg.T1, Reg.S4, Reg.S5));
        Emit(RiscVEncoder.Addi(Reg.T1, Reg.T1, 1)); // max = text_len - needle_len + 1
        int notFound = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // patched: bge t0, t1 → not found

        // Inner loop: compare text[i+j] with needle[j]
        foreach (uint insn in RiscVEncoder.Li(Reg.T2, 0)) Emit(insn); // t2 = j
        int innerLoop = m_instructions.Count;
        int foundMatch = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // patched: bge t2, s5 → found

        Emit(RiscVEncoder.Add(Reg.T3, Reg.S2, Reg.T0));
        Emit(RiscVEncoder.Add(Reg.T3, Reg.T3, Reg.T2));
        Emit(RiscVEncoder.Lbu(Reg.T3, Reg.T3, 8)); // text[i+j]
        Emit(RiscVEncoder.Add(Reg.T4, Reg.S3, Reg.T2));
        Emit(RiscVEncoder.Lbu(Reg.T4, Reg.T4, 8)); // needle[j]
        int mismatch = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // patched: bne → mismatch
        Emit(RiscVEncoder.Addi(Reg.T2, Reg.T2, 1));
        Emit(RiscVEncoder.J((innerLoop - m_instructions.Count) * 4));

        // Found
        int foundLabel = m_instructions.Count;
        m_instructions[foundMatch] = RiscVEncoder.Bge(Reg.T2, Reg.S5, (foundLabel - foundMatch) * 4);
        foreach (uint insn in RiscVEncoder.Li(Reg.A0, 1)) Emit(insn);
        int doneJmp = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // j done

        // Mismatch — advance i
        int mismatchLabel = m_instructions.Count;
        m_instructions[mismatch] = RiscVEncoder.Bne(Reg.T3, Reg.T4, (mismatchLabel - mismatch) * 4);
        Emit(RiscVEncoder.Addi(Reg.T0, Reg.T0, 1));
        Emit(RiscVEncoder.J((outerLoop - m_instructions.Count) * 4));

        // Not found
        int notFoundLabel = m_instructions.Count;
        m_instructions[notFound] = RiscVEncoder.Bge(Reg.T0, Reg.T1, (notFoundLabel - notFound) * 4);
        foreach (uint insn in RiscVEncoder.Li(Reg.A0, 0)) Emit(insn);

        // Done — restore and return
        int doneLabel = m_instructions.Count;
        m_instructions[doneJmp] = RiscVEncoder.J((doneLabel - doneJmp) * 4);
        Emit(RiscVEncoder.Ld(Reg.S5, Reg.Sp, 8));
        Emit(RiscVEncoder.Ld(Reg.S4, Reg.Sp, 16));
        Emit(RiscVEncoder.Ld(Reg.S3, Reg.Sp, 24));
        Emit(RiscVEncoder.Ld(Reg.S2, Reg.Sp, 32));
        Emit(RiscVEncoder.Ld(Reg.Ra, Reg.Sp, 40));
        Emit(RiscVEncoder.Addi(Reg.Sp, Reg.Sp, 48));
        Emit(RiscVEncoder.Ret());
    }

    void EmitTextStartsWithHelper()
    {
        // __text_starts_with: a0=text, a1=prefix → a0=1/0
        m_functionOffsets["__text_starts_with"] = m_instructions.Count;

        Emit(RiscVEncoder.Ld(Reg.T0, Reg.A0, 0)); // text_len
        Emit(RiscVEncoder.Ld(Reg.T1, Reg.A1, 0)); // prefix_len

        // If prefix_len > text_len → false
        int tooLong = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // patched: blt t0, t1 → false

        foreach (uint insn in RiscVEncoder.Li(Reg.T2, 0)) Emit(insn); // i = 0

        int loop = m_instructions.Count;
        int matched = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // patched: bge t2, t1 → true

        Emit(RiscVEncoder.Add(Reg.T3, Reg.A0, Reg.T2));
        Emit(RiscVEncoder.Lbu(Reg.T3, Reg.T3, 8));
        Emit(RiscVEncoder.Add(Reg.T4, Reg.A1, Reg.T2));
        Emit(RiscVEncoder.Lbu(Reg.T4, Reg.T4, 8));
        int mismatch = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // patched: bne → false
        Emit(RiscVEncoder.Addi(Reg.T2, Reg.T2, 1));
        Emit(RiscVEncoder.J((loop - m_instructions.Count) * 4));

        // Matched
        int matchedLabel = m_instructions.Count;
        m_instructions[matched] = RiscVEncoder.Bge(Reg.T2, Reg.T1, (matchedLabel - matched) * 4);
        foreach (uint insn in RiscVEncoder.Li(Reg.A0, 1)) Emit(insn);
        Emit(RiscVEncoder.Ret());

        // False (too long or mismatch)
        int falseLabel = m_instructions.Count;
        m_instructions[tooLong] = RiscVEncoder.Blt(Reg.T0, Reg.T1, (falseLabel - tooLong) * 4);
        m_instructions[mismatch] = RiscVEncoder.Bne(Reg.T3, Reg.T4, (falseLabel - mismatch) * 4);
        foreach (uint insn in RiscVEncoder.Li(Reg.A0, 0)) Emit(insn);
        Emit(RiscVEncoder.Ret());
    }

    void EmitTextCompareHelper()
    {
        // __text_compare: a0=text1, a1=text2 → a0=-1/0/+1
        m_functionOffsets["__text_compare"] = m_instructions.Count;

        Emit(RiscVEncoder.Addi(Reg.Sp, Reg.Sp, -32));
        Emit(RiscVEncoder.Sd(Reg.Sp, Reg.Ra, 24));
        Emit(RiscVEncoder.Sd(Reg.Sp, Reg.S2, 16));
        Emit(RiscVEncoder.Sd(Reg.Sp, Reg.S3, 8));
        Emit(RiscVEncoder.Sd(Reg.Sp, Reg.S4, 0));

        // s2=text1, s3=text2
        Emit(RiscVEncoder.Mv(Reg.S2, Reg.A0));
        Emit(RiscVEncoder.Mv(Reg.S3, Reg.A1));
        // t0=len1, t1=len2
        Emit(RiscVEncoder.Ld(Reg.T0, Reg.S2, 0));
        Emit(RiscVEncoder.Ld(Reg.T1, Reg.S3, 0));
        // s4=min(len1,len2)
        Emit(RiscVEncoder.Mv(Reg.S4, Reg.T0));
        int len1Smaller = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // patched: bge t0, t1 → skip
        Emit(RiscVEncoder.Mv(Reg.S4, Reg.T0)); // redundant but harmless
        int skipMin = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // patched: j past
        int setLen2 = m_instructions.Count;
        m_instructions[len1Smaller] = RiscVEncoder.Blt(Reg.T0, Reg.T1, (setLen2 - len1Smaller) * 4);
        Emit(RiscVEncoder.Mv(Reg.S4, Reg.T1));
        int pastMin = m_instructions.Count;
        m_instructions[skipMin] = RiscVEncoder.J((pastMin - skipMin) * 4);
        // Wait — simpler: use Slt to pick min
        // Actually let me just redo this cleanly:
        // s4 = min(t0, t1): if t0 <= t1 then s4=t0, else s4=t1
        // The above code is already emitted so let me just fix the logic.
        // Actually the emitted code works: if t0 < t1, branch to setLen2 which sets s4=t1.
        // If t0 >= t1, s4 stays as t0, then jumps past. But wait, if t0 >= t1, we want min=t1.
        // Bug! Let me re-think. I need min. If t0 < t1, min=t0 (already set). If t0 >= t1, min=t1.
        // So the branch should be: if t0 >= t1, go to setLen2.
        // Fix: use Bge instead of Blt.
        m_instructions[len1Smaller] = RiscVEncoder.Bge(Reg.T0, Reg.T1, (setLen2 - len1Smaller) * 4);
        // Now: if t0 >= t1, jump to setLen2 which sets s4=t1. Otherwise s4 stays t0. Correct.

        // Loop: compare bytes i=0..min-1
        foreach (uint insn in RiscVEncoder.Li(Reg.T2, 0)) Emit(insn); // i
        int cmpLoop = m_instructions.Count;
        int cmpDone = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // patched: bge t2, s4 → lengths
        // Load byte from each string: str[8+i]
        Emit(RiscVEncoder.Add(Reg.T3, Reg.S2, Reg.T2));
        Emit(RiscVEncoder.Lbu(Reg.T3, Reg.T3, 8));
        Emit(RiscVEncoder.Add(Reg.T4, Reg.S3, Reg.T2));
        Emit(RiscVEncoder.Lbu(Reg.T4, Reg.T4, 8));
        int bytesEqual = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // patched: beq t3, t4 → next
        // Bytes differ: a < b → -1, a > b → +1
        int aGreater = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // patched: bgt t3, t4 → +1
        foreach (uint insn in RiscVEncoder.Li(Reg.A0, -1)) Emit(insn);
        int retEarly1 = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // patched: j → epilogue
        int setPlus1 = m_instructions.Count;
        m_instructions[aGreater] = RiscVEncoder.Blt(Reg.T4, Reg.T3, (setPlus1 - aGreater) * 4);
        foreach (uint insn in RiscVEncoder.Li(Reg.A0, 1)) Emit(insn);
        int retEarly2 = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // patched: j → epilogue
        // Bytes equal: next iteration
        int nextIter = m_instructions.Count;
        m_instructions[bytesEqual] = RiscVEncoder.Beq(Reg.T3, Reg.T4, (nextIter - bytesEqual) * 4);
        Emit(RiscVEncoder.Addi(Reg.T2, Reg.T2, 1));
        Emit(RiscVEncoder.J((cmpLoop - m_instructions.Count) * 4));

        // All min bytes equal — compare lengths
        int cmpLengths = m_instructions.Count;
        m_instructions[cmpDone] = RiscVEncoder.Bge(Reg.T2, Reg.S4, (cmpLengths - cmpDone) * 4);
        // Reload lengths
        Emit(RiscVEncoder.Ld(Reg.T0, Reg.S2, 0));
        Emit(RiscVEncoder.Ld(Reg.T1, Reg.S3, 0));
        int lenEqual = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // patched: beq t0, t1 → 0
        int lenGreater = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // patched: blt t1, t0 → +1
        foreach (uint insn in RiscVEncoder.Li(Reg.A0, -1)) Emit(insn);
        int retLen1 = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // j → epilogue
        int setLenPlus1 = m_instructions.Count;
        m_instructions[lenGreater] = RiscVEncoder.Blt(Reg.T1, Reg.T0, (setLenPlus1 - lenGreater) * 4);
        foreach (uint insn in RiscVEncoder.Li(Reg.A0, 1)) Emit(insn);
        int retLen2 = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // j → epilogue
        int setZero = m_instructions.Count;
        m_instructions[lenEqual] = RiscVEncoder.Beq(Reg.T0, Reg.T1, (setZero - lenEqual) * 4);
        foreach (uint insn in RiscVEncoder.Li(Reg.A0, 0)) Emit(insn);

        // Epilogue
        int epilogue = m_instructions.Count;
        m_instructions[retEarly1] = RiscVEncoder.J((epilogue - retEarly1) * 4);
        m_instructions[retEarly2] = RiscVEncoder.J((epilogue - retEarly2) * 4);
        m_instructions[retLen1] = RiscVEncoder.J((epilogue - retLen1) * 4);
        m_instructions[retLen2] = RiscVEncoder.J((epilogue - retLen2) * 4);

        Emit(RiscVEncoder.Ld(Reg.S4, Reg.Sp, 0));
        Emit(RiscVEncoder.Ld(Reg.S3, Reg.Sp, 8));
        Emit(RiscVEncoder.Ld(Reg.S2, Reg.Sp, 16));
        Emit(RiscVEncoder.Ld(Reg.Ra, Reg.Sp, 24));
        Emit(RiscVEncoder.Addi(Reg.Sp, Reg.Sp, 32));
        Emit(RiscVEncoder.Ret());
    }

    void EmitListSnocHelper()
    {
        // __list_snoc: a0=list_ptr, a1=element → a0=new list with element appended
        // Layout: [capacity @ -8 | count @ 0 | elem0 @ 8 | ...]
        // Path 1: count < capacity → in-place O(1)
        // Path 2: count == capacity, at heap top → grow capacity O(1)
        // Path 3: count == capacity, not at top → copy with doubling O(N) amortized O(1)
        m_functionOffsets["__list_snoc"] = m_instructions.Count;

        // t0 = count, t1 = capacity
        Emit(RiscVEncoder.Ld(Reg.T0, Reg.A0, 0));         // count
        Emit(RiscVEncoder.Ld(Reg.T1, Reg.A0, -8));        // capacity

        // Path 1: count < capacity → in-place append
        int path2 = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // patched: bge t0, t1 → path2

        // Store element at [list + 8 + count*8]
        Emit(RiscVEncoder.Slli(Reg.T2, Reg.T0, 3));       // count * 8
        Emit(RiscVEncoder.Add(Reg.T2, Reg.A0, Reg.T2));   // list + count*8
        Emit(RiscVEncoder.Sd(Reg.T2, Reg.A1, 8));         // [list + 8 + count*8] = element
        // Increment count
        Emit(RiscVEncoder.Addi(Reg.T0, Reg.T0, 1));
        Emit(RiscVEncoder.Sd(Reg.A0, Reg.T0, 0));         // [list] = count+1
        Emit(RiscVEncoder.Ret());                           // return a0 (unchanged)

        // Path 2: count == capacity, check heap top
        int path2Target = m_instructions.Count;
        m_instructions[path2] = RiscVEncoder.Bge(Reg.T0, Reg.T1, (path2Target - path2) * 4);

        // End of allocation = list_ptr + (capacity + 1) * 8
        Emit(RiscVEncoder.Addi(Reg.T2, Reg.T1, 1));       // capacity + 1
        Emit(RiscVEncoder.Slli(Reg.T2, Reg.T2, 3));
        Emit(RiscVEncoder.Add(Reg.T2, Reg.A0, Reg.T2));   // list + (cap+1)*8
        int path3 = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // patched: bne t2, s1 → path3

        // At heap top: grow capacity = max(capacity * 2, 4)
        Emit(RiscVEncoder.Slli(Reg.T2, Reg.T1, 1));       // capacity * 2
        foreach (uint insn in RiscVEncoder.Li(Reg.T3, 4)) Emit(insn);
        int capOk = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // patched: bge t2, t3 → capOk
        Emit(RiscVEncoder.Mv(Reg.T2, Reg.T3));            // use 16
        int capOkTarget = m_instructions.Count;
        m_instructions[capOk] = RiscVEncoder.Bge(Reg.T2, Reg.T3, (capOkTarget - capOk) * 4);
        // t2 = new capacity. Update capacity word and bump heap
        Emit(RiscVEncoder.Sd(Reg.A0, Reg.T2, -8));        // [list-8] = new capacity
        Emit(RiscVEncoder.Sub(Reg.T3, Reg.T2, Reg.T1));   // newCap - oldCap
        Emit(RiscVEncoder.Slli(Reg.T3, Reg.T3, 3));
        Emit(RiscVEncoder.Add(Reg.S1, Reg.S1, Reg.T3));   // bump heap
        // Store element and increment count (same as path 1)
        Emit(RiscVEncoder.Slli(Reg.T2, Reg.T0, 3));
        Emit(RiscVEncoder.Add(Reg.T2, Reg.A0, Reg.T2));
        Emit(RiscVEncoder.Sd(Reg.T2, Reg.A1, 8));
        Emit(RiscVEncoder.Addi(Reg.T0, Reg.T0, 1));
        Emit(RiscVEncoder.Sd(Reg.A0, Reg.T0, 0));
        Emit(RiscVEncoder.Ret());

        // Path 3: not at heap top — allocate with doubled capacity, copy
        int path3Target = m_instructions.Count;
        m_instructions[path3] = RiscVEncoder.Bne(Reg.T2, Reg.S1, (path3Target - path3) * 4);

        Emit(RiscVEncoder.Addi(Reg.Sp, Reg.Sp, -40));
        Emit(RiscVEncoder.Sd(Reg.Sp, Reg.Ra, 32));
        Emit(RiscVEncoder.Sd(Reg.Sp, Reg.S2, 24));
        Emit(RiscVEncoder.Sd(Reg.Sp, Reg.S3, 16));
        Emit(RiscVEncoder.Sd(Reg.Sp, Reg.S4, 8));
        Emit(RiscVEncoder.Sd(Reg.Sp, Reg.S5, 0));

        Emit(RiscVEncoder.Mv(Reg.S2, Reg.A0));            // old list
        Emit(RiscVEncoder.Mv(Reg.S3, Reg.A1));            // element
        Emit(RiscVEncoder.Ld(Reg.S4, Reg.S2, 0));         // count

        // S5 = new capacity = max(count * 2, 4)
        Emit(RiscVEncoder.Slli(Reg.S5, Reg.S4, 1));       // count * 2
        foreach (uint insn in RiscVEncoder.Li(Reg.T0, 4)) Emit(insn);
        int capOk2 = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // patched: bge s5, t0 → capOk2
        Emit(RiscVEncoder.Mv(Reg.S5, Reg.T0));
        int capOk2Target = m_instructions.Count;
        m_instructions[capOk2] = RiscVEncoder.Bge(Reg.S5, Reg.T0, (capOk2Target - capOk2) * 4);

        // Allocate [capacity | count | slots]: capacity at [S1], list ptr at S1+8
        Emit(RiscVEncoder.Sd(Reg.S1, Reg.S5, 0));         // capacity word
        Emit(RiscVEncoder.Addi(Reg.S1, Reg.S1, 8));
        Emit(RiscVEncoder.Mv(Reg.A0, Reg.S1));            // A0 = new list ptr
        Emit(RiscVEncoder.Addi(Reg.T0, Reg.S5, 1));       // newCap + 1
        Emit(RiscVEncoder.Slli(Reg.T0, Reg.T0, 3));
        Emit(RiscVEncoder.Add(Reg.S1, Reg.S1, Reg.T0));   // advance past count+slots

        // Store new count = oldCount + 1
        Emit(RiscVEncoder.Addi(Reg.T0, Reg.S4, 1));
        Emit(RiscVEncoder.Sd(Reg.A0, Reg.T0, 0));

        // Copy old elements: for i in 0..count-1
        foreach (uint insn in RiscVEncoder.Li(Reg.T0, 0)) Emit(insn);
        int copyLoop = m_instructions.Count;
        int copyDone = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // patched: bge t0, s4 → done
        Emit(RiscVEncoder.Slli(Reg.T1, Reg.T0, 3));
        Emit(RiscVEncoder.Add(Reg.T2, Reg.S2, Reg.T1));
        Emit(RiscVEncoder.Ld(Reg.T2, Reg.T2, 8));
        Emit(RiscVEncoder.Add(Reg.T3, Reg.A0, Reg.T1));
        Emit(RiscVEncoder.Sd(Reg.T3, Reg.T2, 8));
        Emit(RiscVEncoder.Addi(Reg.T0, Reg.T0, 1));
        Emit(RiscVEncoder.J((copyLoop - m_instructions.Count) * 4));
        int copyEnd = m_instructions.Count;
        m_instructions[copyDone] = RiscVEncoder.Bge(Reg.T0, Reg.S4, (copyEnd - copyDone) * 4);

        // Store new element at new[8 + count*8]
        Emit(RiscVEncoder.Slli(Reg.T0, Reg.S4, 3));
        Emit(RiscVEncoder.Add(Reg.T1, Reg.A0, Reg.T0));
        Emit(RiscVEncoder.Sd(Reg.T1, Reg.S3, 8));

        Emit(RiscVEncoder.Ld(Reg.S5, Reg.Sp, 0));
        Emit(RiscVEncoder.Ld(Reg.S4, Reg.Sp, 8));
        Emit(RiscVEncoder.Ld(Reg.S3, Reg.Sp, 16));
        Emit(RiscVEncoder.Ld(Reg.S2, Reg.Sp, 24));
        Emit(RiscVEncoder.Ld(Reg.Ra, Reg.Sp, 32));
        Emit(RiscVEncoder.Addi(Reg.Sp, Reg.Sp, 40));
        Emit(RiscVEncoder.Ret());
    }

    void EmitListInsertAtHelper()
    {
        // __list_insert_at: a0=list, a1=index, a2=element → a0=new list
        // List layout: [capacity @ -8 | count @ 0 | elem0 @ 8 | ...]
        // Path 1: count < capacity → in-place shift O(N), zero alloc
        // Path 2: count == capacity, at heap top → grow capacity, then shift
        // Path 3: count == capacity, not at top → copy-with-gap + doubling
        m_functionOffsets["__list_insert_at"] = m_instructions.Count;

        Emit(RiscVEncoder.Addi(Reg.Sp, Reg.Sp, -48));
        Emit(RiscVEncoder.Sd(Reg.Sp, Reg.Ra, 40));
        Emit(RiscVEncoder.Sd(Reg.Sp, Reg.S2, 32));
        Emit(RiscVEncoder.Sd(Reg.Sp, Reg.S3, 24));
        Emit(RiscVEncoder.Sd(Reg.Sp, Reg.S4, 16));
        Emit(RiscVEncoder.Sd(Reg.Sp, Reg.S5, 8));
        Emit(RiscVEncoder.Sd(Reg.Sp, Reg.S6, 0));

        Emit(RiscVEncoder.Mv(Reg.S2, Reg.A0));            // S2 = list
        Emit(RiscVEncoder.Mv(Reg.S3, Reg.A1));            // S3 = index
        Emit(RiscVEncoder.Mv(Reg.S4, Reg.A2));            // S4 = element
        Emit(RiscVEncoder.Ld(Reg.S5, Reg.S2, 0));         // S5 = count
        Emit(RiscVEncoder.Ld(Reg.S6, Reg.S2, -8));        // S6 = capacity

        // Path 1: count < capacity → jump to in-place shift
        int inPlaceJmp = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // patched: blt s5, s6 → inPlace

        // Path 2: count == capacity, check heap top
        // End of alloc = list + (capacity + 1) * 8
        Emit(RiscVEncoder.Addi(Reg.T0, Reg.S6, 1));
        Emit(RiscVEncoder.Slli(Reg.T0, Reg.T0, 3));
        Emit(RiscVEncoder.Add(Reg.T0, Reg.S2, Reg.T0));
        int path3Jmp = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // patched: bne t0, s1 → path3

        // At heap top: newCap = max(capacity * 2, 4)
        Emit(RiscVEncoder.Slli(Reg.T0, Reg.S6, 1));       // capacity * 2
        foreach (uint insn in RiscVEncoder.Li(Reg.T1, 4)) Emit(insn);
        int capOk2 = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // patched: bge t0, t1 → capOk
        Emit(RiscVEncoder.Mv(Reg.T0, Reg.T1));
        int capOk2Target = m_instructions.Count;
        m_instructions[capOk2] = RiscVEncoder.Bge(Reg.T0, Reg.T1, (capOk2Target - capOk2) * 4);
        // Update capacity, bump heap
        Emit(RiscVEncoder.Sd(Reg.S2, Reg.T0, -8));        // [list-8] = new capacity
        Emit(RiscVEncoder.Sub(Reg.T1, Reg.T0, Reg.S6));   // newCap - oldCap
        Emit(RiscVEncoder.Slli(Reg.T1, Reg.T1, 3));
        Emit(RiscVEncoder.Add(Reg.S1, Reg.S1, Reg.T1));   // bump heap
        // Fall through to in-place shift

        // ── In-place shift (shared by Path 1 and Path 2) ──
        int inPlaceTarget = m_instructions.Count;
        m_instructions[inPlaceJmp] = RiscVEncoder.Blt(Reg.S5, Reg.S6, (inPlaceTarget - inPlaceJmp) * 4);
        // Shift elements [index..count-1] right by 1 (backward: i = count-1 down to index)
        Emit(RiscVEncoder.Addi(Reg.T0, Reg.S5, -1));      // i = count - 1
        int shiftLoop = m_instructions.Count;
        int shiftDone = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // patched: blt t0, s3 → done
        Emit(RiscVEncoder.Slli(Reg.T1, Reg.T0, 3));
        Emit(RiscVEncoder.Add(Reg.T2, Reg.S2, Reg.T1));
        Emit(RiscVEncoder.Ld(Reg.T3, Reg.T2, 8));         // list[8 + i*8]
        Emit(RiscVEncoder.Sd(Reg.T2, Reg.T3, 16));        // list[8 + (i+1)*8]
        Emit(RiscVEncoder.Addi(Reg.T0, Reg.T0, -1));      // i--
        Emit(RiscVEncoder.J((shiftLoop - m_instructions.Count) * 4));
        int shiftEnd = m_instructions.Count;
        m_instructions[shiftDone] = RiscVEncoder.Blt(Reg.T0, Reg.S3, (shiftEnd - shiftDone) * 4);
        // Store element at [list + 8 + index*8]
        Emit(RiscVEncoder.Slli(Reg.T0, Reg.S3, 3));
        Emit(RiscVEncoder.Add(Reg.T0, Reg.S2, Reg.T0));
        Emit(RiscVEncoder.Sd(Reg.T0, Reg.S4, 8));
        // Increment count, return same ptr
        Emit(RiscVEncoder.Addi(Reg.T0, Reg.S5, 1));
        Emit(RiscVEncoder.Sd(Reg.S2, Reg.T0, 0));
        Emit(RiscVEncoder.Mv(Reg.A0, Reg.S2));
        // Epilog (shared)
        int epilog = m_instructions.Count;
        Emit(RiscVEncoder.Ld(Reg.S6, Reg.Sp, 0));
        Emit(RiscVEncoder.Ld(Reg.S5, Reg.Sp, 8));
        Emit(RiscVEncoder.Ld(Reg.S4, Reg.Sp, 16));
        Emit(RiscVEncoder.Ld(Reg.S3, Reg.Sp, 24));
        Emit(RiscVEncoder.Ld(Reg.S2, Reg.Sp, 32));
        Emit(RiscVEncoder.Ld(Reg.Ra, Reg.Sp, 40));
        Emit(RiscVEncoder.Addi(Reg.Sp, Reg.Sp, 48));
        Emit(RiscVEncoder.Ret());

        // ── Path 3: not at heap top — copy-with-gap + doubled capacity ──
        int path3Target = m_instructions.Count;
        m_instructions[path3Jmp] = RiscVEncoder.Bne(Reg.T0, Reg.S1, (path3Target - path3Jmp) * 4);
        // newCap = max(count * 2, 4)
        Emit(RiscVEncoder.Slli(Reg.T0, Reg.S5, 1));       // count * 2
        foreach (uint insn in RiscVEncoder.Li(Reg.T1, 4)) Emit(insn);
        int capOk3 = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // patched: bge t0, t1 → capOk3
        Emit(RiscVEncoder.Mv(Reg.T0, Reg.T1));
        int capOk3Target = m_instructions.Count;
        m_instructions[capOk3] = RiscVEncoder.Bge(Reg.T0, Reg.T1, (capOk3Target - capOk3) * 4);
        // Allocate [capacity | count | slots...]
        Emit(RiscVEncoder.Sd(Reg.S1, Reg.T0, 0));         // capacity word
        Emit(RiscVEncoder.Addi(Reg.S1, Reg.S1, 8));
        Emit(RiscVEncoder.Mv(Reg.S6, Reg.S1));            // S6 = new list ptr
        Emit(RiscVEncoder.Addi(Reg.T0, Reg.T0, 1));       // newCap + 1
        Emit(RiscVEncoder.Slli(Reg.T0, Reg.T0, 3));
        Emit(RiscVEncoder.Add(Reg.S1, Reg.S1, Reg.T0));   // bump heap
        // Store new count = oldCount + 1
        Emit(RiscVEncoder.Addi(Reg.T0, Reg.S5, 1));
        Emit(RiscVEncoder.Sd(Reg.S6, Reg.T0, 0));
        // Copy elements before index: 0..index-1
        foreach (uint insn in RiscVEncoder.Li(Reg.T0, 0)) Emit(insn);
        int preCopy = m_instructions.Count;
        int preDone = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // patched: bge t0, s3 → done
        Emit(RiscVEncoder.Slli(Reg.T1, Reg.T0, 3));
        Emit(RiscVEncoder.Add(Reg.T2, Reg.S2, Reg.T1));
        Emit(RiscVEncoder.Ld(Reg.T2, Reg.T2, 8));
        Emit(RiscVEncoder.Add(Reg.T3, Reg.S6, Reg.T1));
        Emit(RiscVEncoder.Sd(Reg.T3, Reg.T2, 8));
        Emit(RiscVEncoder.Addi(Reg.T0, Reg.T0, 1));
        Emit(RiscVEncoder.J((preCopy - m_instructions.Count) * 4));
        int preEnd = m_instructions.Count;
        m_instructions[preDone] = RiscVEncoder.Bge(Reg.T0, Reg.S3, (preEnd - preDone) * 4);
        // Store inserted element at new[8 + index*8]
        Emit(RiscVEncoder.Slli(Reg.T0, Reg.S3, 3));
        Emit(RiscVEncoder.Add(Reg.T1, Reg.S6, Reg.T0));
        Emit(RiscVEncoder.Sd(Reg.T1, Reg.S4, 8));
        // Copy elements after index: old[index..count-1] → new[index+1..]
        Emit(RiscVEncoder.Mv(Reg.T0, Reg.S3));
        int postCopy = m_instructions.Count;
        int postDone = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // patched: bge t0, s5 → done
        Emit(RiscVEncoder.Slli(Reg.T1, Reg.T0, 3));
        Emit(RiscVEncoder.Add(Reg.T2, Reg.S2, Reg.T1));
        Emit(RiscVEncoder.Ld(Reg.T2, Reg.T2, 8));
        Emit(RiscVEncoder.Addi(Reg.T3, Reg.T1, 8));
        Emit(RiscVEncoder.Add(Reg.T3, Reg.S6, Reg.T3));
        Emit(RiscVEncoder.Sd(Reg.T3, Reg.T2, 8));
        Emit(RiscVEncoder.Addi(Reg.T0, Reg.T0, 1));
        Emit(RiscVEncoder.J((postCopy - m_instructions.Count) * 4));
        int postEnd = m_instructions.Count;
        m_instructions[postDone] = RiscVEncoder.Bge(Reg.T0, Reg.S5, (postEnd - postDone) * 4);
        // Return new list ptr
        Emit(RiscVEncoder.Mv(Reg.A0, Reg.S6));
        Emit(RiscVEncoder.J((epilog - m_instructions.Count) * 4)); // jump to shared epilog
    }

    void EmitListContainsHelper()
    {
        // __list_contains: a0=list, a1=element → a0=1 if found, 0 if not
        m_functionOffsets["__list_contains"] = m_instructions.Count;

        Emit(RiscVEncoder.Ld(Reg.T0, Reg.A0, 0)); // length
        foreach (uint insn in RiscVEncoder.Li(Reg.T1, 0)) Emit(insn); // index

        int loop = m_instructions.Count;
        int notFound = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // patched: bge t1, t0 → not found

        // Load element at [list + 8 + i*8]
        Emit(RiscVEncoder.Slli(Reg.T2, Reg.T1, 3));
        Emit(RiscVEncoder.Add(Reg.T2, Reg.A0, Reg.T2));
        Emit(RiscVEncoder.Ld(Reg.T2, Reg.T2, 8));

        // Compare with target
        int found = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // patched: beq t2, a1 → found

        Emit(RiscVEncoder.Addi(Reg.T1, Reg.T1, 1));
        Emit(RiscVEncoder.J((loop - m_instructions.Count) * 4));

        int foundLabel = m_instructions.Count;
        m_instructions[found] = RiscVEncoder.Beq(Reg.T2, Reg.A1, (foundLabel - found) * 4);
        foreach (uint insn in RiscVEncoder.Li(Reg.A0, 1)) Emit(insn);
        Emit(RiscVEncoder.Ret());

        int notFoundLabel = m_instructions.Count;
        m_instructions[notFound] = RiscVEncoder.Bge(Reg.T1, Reg.T0, (notFoundLabel - notFound) * 4);
        foreach (uint insn in RiscVEncoder.Li(Reg.A0, 0)) Emit(insn);
        Emit(RiscVEncoder.Ret());
    }

    void EmitTextConcatListHelper()
    {
        // __text_concat_list: a0=list of text ptrs → a0=concatenated text
        m_functionOffsets["__text_concat_list"] = m_instructions.Count;

        Emit(RiscVEncoder.Addi(Reg.Sp, Reg.Sp, -48));
        Emit(RiscVEncoder.Sd(Reg.Sp, Reg.Ra, 40));
        Emit(RiscVEncoder.Sd(Reg.Sp, Reg.S2, 32));
        Emit(RiscVEncoder.Sd(Reg.Sp, Reg.S3, 24));
        Emit(RiscVEncoder.Sd(Reg.Sp, Reg.S4, 16));
        Emit(RiscVEncoder.Sd(Reg.Sp, Reg.S5, 8));
        Emit(RiscVEncoder.Sd(Reg.Sp, Reg.S6, 0));

        Emit(RiscVEncoder.Mv(Reg.S2, Reg.A0));        // list ptr
        Emit(RiscVEncoder.Ld(Reg.S3, Reg.S2, 0));     // list length

        // Pass 1: compute total byte length
        foreach (uint insn in RiscVEncoder.Li(Reg.S4, 0)) Emit(insn); // total bytes
        foreach (uint insn in RiscVEncoder.Li(Reg.T0, 0)) Emit(insn); // index
        int lenLoop = m_instructions.Count;
        int lenDone = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // patched: bge t0, s3 → done
        Emit(RiscVEncoder.Slli(Reg.T1, Reg.T0, 3));
        Emit(RiscVEncoder.Add(Reg.T1, Reg.S2, Reg.T1));
        Emit(RiscVEncoder.Ld(Reg.T1, Reg.T1, 8));     // str ptr
        Emit(RiscVEncoder.Ld(Reg.T1, Reg.T1, 0));     // str length
        Emit(RiscVEncoder.Add(Reg.S4, Reg.S4, Reg.T1));
        Emit(RiscVEncoder.Addi(Reg.T0, Reg.T0, 1));
        Emit(RiscVEncoder.J((lenLoop - m_instructions.Count) * 4));
        int lenEnd = m_instructions.Count;
        m_instructions[lenDone] = RiscVEncoder.Bge(Reg.T0, Reg.S3, (lenEnd - lenDone) * 4);

        // Allocate result: 8 + align8(total)
        Emit(RiscVEncoder.Mv(Reg.S5, Reg.S1));         // result ptr
        Emit(RiscVEncoder.Sd(Reg.S5, Reg.S4, 0));      // store total length
        Emit(RiscVEncoder.Addi(Reg.T0, Reg.S4, 15));
        Emit(RiscVEncoder.Andi(Reg.T0, Reg.T0, -8));
        Emit(RiscVEncoder.Add(Reg.S1, Reg.S1, Reg.T0));

        // Pass 2: copy bytes from each string
        foreach (uint insn in RiscVEncoder.Li(Reg.T0, 0)) Emit(insn); // list index
        foreach (uint insn in RiscVEncoder.Li(Reg.S6, 0)) Emit(insn); // dest offset
        int copyLoop = m_instructions.Count;
        int copyDone = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // patched: bge t0, s3 → done

        // Load string ptr from list
        Emit(RiscVEncoder.Slli(Reg.T1, Reg.T0, 3));
        Emit(RiscVEncoder.Add(Reg.T1, Reg.S2, Reg.T1));
        Emit(RiscVEncoder.Ld(Reg.T1, Reg.T1, 8));     // str ptr
        Emit(RiscVEncoder.Ld(Reg.T2, Reg.T1, 0));     // str len

        // Copy bytes: for j=0..strLen-1
        foreach (uint insn in RiscVEncoder.Li(Reg.T3, 0)) Emit(insn); // j
        int byteLoop = m_instructions.Count;
        int byteDone = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // patched: bge t3, t2 → done
        // Load src byte: src[8+j]
        Emit(RiscVEncoder.Add(Reg.T4, Reg.T1, Reg.T3));
        Emit(RiscVEncoder.Lbu(Reg.T4, Reg.T4, 8));
        // Store at dst[8 + destOff + j]
        Emit(RiscVEncoder.Add(Reg.T5, Reg.S5, Reg.S6));
        Emit(RiscVEncoder.Add(Reg.T5, Reg.T5, Reg.T3));
        Emit(RiscVEncoder.Sb(Reg.T5, Reg.T4, 8));
        Emit(RiscVEncoder.Addi(Reg.T3, Reg.T3, 1));
        Emit(RiscVEncoder.J((byteLoop - m_instructions.Count) * 4));
        int byteEnd = m_instructions.Count;
        m_instructions[byteDone] = RiscVEncoder.Bge(Reg.T3, Reg.T2, (byteEnd - byteDone) * 4);

        Emit(RiscVEncoder.Add(Reg.S6, Reg.S6, Reg.T2)); // destOff += strLen
        Emit(RiscVEncoder.Addi(Reg.T0, Reg.T0, 1));
        Emit(RiscVEncoder.J((copyLoop - m_instructions.Count) * 4));
        int copyEnd = m_instructions.Count;
        m_instructions[copyDone] = RiscVEncoder.Bge(Reg.T0, Reg.S3, (copyEnd - copyDone) * 4);

        Emit(RiscVEncoder.Mv(Reg.A0, Reg.S5));

        Emit(RiscVEncoder.Ld(Reg.S6, Reg.Sp, 0));
        Emit(RiscVEncoder.Ld(Reg.S5, Reg.Sp, 8));
        Emit(RiscVEncoder.Ld(Reg.S4, Reg.Sp, 16));
        Emit(RiscVEncoder.Ld(Reg.S3, Reg.Sp, 24));
        Emit(RiscVEncoder.Ld(Reg.S2, Reg.Sp, 32));
        Emit(RiscVEncoder.Ld(Reg.Ra, Reg.Sp, 40));
        Emit(RiscVEncoder.Addi(Reg.Sp, Reg.Sp, 48));
        Emit(RiscVEncoder.Ret());
    }

    void EmitTextSplitHelper()
    {
        // __text_split: a0=text, a1=delimiter → a0=list of text ptrs
        // Simple single-char delimiter split
        m_functionOffsets["__text_split"] = m_instructions.Count;

        Emit(RiscVEncoder.Addi(Reg.Sp, Reg.Sp, -48));
        Emit(RiscVEncoder.Sd(Reg.Sp, Reg.Ra, 40));
        Emit(RiscVEncoder.Sd(Reg.Sp, Reg.S2, 32));
        Emit(RiscVEncoder.Sd(Reg.Sp, Reg.S3, 24));
        Emit(RiscVEncoder.Sd(Reg.Sp, Reg.S4, 16));
        Emit(RiscVEncoder.Sd(Reg.Sp, Reg.S5, 8));
        Emit(RiscVEncoder.Sd(Reg.Sp, Reg.S6, 0));

        Emit(RiscVEncoder.Mv(Reg.S2, Reg.A0));        // text ptr
        Emit(RiscVEncoder.Ld(Reg.S3, Reg.S2, 0));     // text length
        // Load delimiter byte (first byte of delimiter string)
        Emit(RiscVEncoder.Lbu(Reg.S4, Reg.A1, 8));    // delim byte

        // s5 = result list start on heap
        Emit(RiscVEncoder.Mv(Reg.S5, Reg.S1));
        // Reserve space for max possible segments (textLen+2 elements + length slot)
        Emit(RiscVEncoder.Addi(Reg.T0, Reg.S3, 2));
        Emit(RiscVEncoder.Slli(Reg.T0, Reg.T0, 3));
        Emit(RiscVEncoder.Add(Reg.S1, Reg.S1, Reg.T0));

        foreach (uint insn in RiscVEncoder.Li(Reg.S6, 0)) Emit(insn); // segment count
        foreach (uint insn in RiscVEncoder.Li(Reg.T0, 0)) Emit(insn); // scan position i
        Emit(RiscVEncoder.Mv(Reg.T5, Reg.T0)); // segment start

        int scanLoop = m_instructions.Count;
        int scanDone = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // patched: bge t0, s3 → done

        // Load byte at text[8+i]
        Emit(RiscVEncoder.Add(Reg.T1, Reg.S2, Reg.T0));
        Emit(RiscVEncoder.Lbu(Reg.T1, Reg.T1, 8));
        int notDelim = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // patched: bne t1, s4 → notDelim

        // Found delimiter at i — emit segment [start..i)
        // segLen = i - start
        Emit(RiscVEncoder.Sub(Reg.T1, Reg.T0, Reg.T5)); // segLen
        // Allocate segment string on heap
        Emit(RiscVEncoder.Mv(Reg.T2, Reg.S1));       // segment ptr
        Emit(RiscVEncoder.Sd(Reg.T2, Reg.T1, 0));    // store length
        Emit(RiscVEncoder.Addi(Reg.T3, Reg.T1, 15));
        Emit(RiscVEncoder.Andi(Reg.T3, Reg.T3, -8));
        Emit(RiscVEncoder.Add(Reg.S1, Reg.S1, Reg.T3));

        // Copy bytes: text[8+start..8+i) → seg[8..]
        foreach (uint insn in RiscVEncoder.Li(Reg.T3, 0)) Emit(insn); // j
        int segCopy = m_instructions.Count;
        int segCopyDone = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // patched: bge t3, t1 → done
        Emit(RiscVEncoder.Add(Reg.T4, Reg.S2, Reg.T5));
        Emit(RiscVEncoder.Add(Reg.T4, Reg.T4, Reg.T3));
        Emit(RiscVEncoder.Lbu(Reg.T4, Reg.T4, 8));
        Emit(RiscVEncoder.Add(Reg.T6, Reg.T2, Reg.T3));
        Emit(RiscVEncoder.Sb(Reg.T6, Reg.T4, 8));
        Emit(RiscVEncoder.Addi(Reg.T3, Reg.T3, 1));
        Emit(RiscVEncoder.J((segCopy - m_instructions.Count) * 4));
        int segCopyEnd = m_instructions.Count;
        m_instructions[segCopyDone] = RiscVEncoder.Bge(Reg.T3, Reg.T1, (segCopyEnd - segCopyDone) * 4);

        // Store segment ptr in result list[8 + segCount*8]
        Emit(RiscVEncoder.Slli(Reg.T3, Reg.S6, 3));
        Emit(RiscVEncoder.Add(Reg.T3, Reg.S5, Reg.T3));
        Emit(RiscVEncoder.Sd(Reg.T3, Reg.T2, 8));
        Emit(RiscVEncoder.Addi(Reg.S6, Reg.S6, 1));

        // Next segment starts at i+1
        Emit(RiscVEncoder.Addi(Reg.T5, Reg.T0, 1));
        int skipNotDelim = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // patched: j → advance

        int notDelimLabel = m_instructions.Count;
        m_instructions[notDelim] = RiscVEncoder.Bne(Reg.T1, Reg.S4, (notDelimLabel - notDelim) * 4);

        int advanceLabel = m_instructions.Count;
        m_instructions[skipNotDelim] = RiscVEncoder.J((advanceLabel - skipNotDelim) * 4);
        Emit(RiscVEncoder.Addi(Reg.T0, Reg.T0, 1));
        Emit(RiscVEncoder.J((scanLoop - m_instructions.Count) * 4));

        // Scan done — emit final segment [start..textLen)
        int scanEndLabel = m_instructions.Count;
        m_instructions[scanDone] = RiscVEncoder.Bge(Reg.T0, Reg.S3, (scanEndLabel - scanDone) * 4);

        Emit(RiscVEncoder.Sub(Reg.T1, Reg.S3, Reg.T5)); // segLen = textLen - start
        Emit(RiscVEncoder.Mv(Reg.T2, Reg.S1));
        Emit(RiscVEncoder.Sd(Reg.T2, Reg.T1, 0));
        Emit(RiscVEncoder.Addi(Reg.T3, Reg.T1, 15));
        Emit(RiscVEncoder.Andi(Reg.T3, Reg.T3, -8));
        Emit(RiscVEncoder.Add(Reg.S1, Reg.S1, Reg.T3));

        // Copy final segment bytes
        foreach (uint insn in RiscVEncoder.Li(Reg.T3, 0)) Emit(insn);
        int finalCopy = m_instructions.Count;
        int finalCopyDone = m_instructions.Count;
        Emit(RiscVEncoder.Nop());
        Emit(RiscVEncoder.Add(Reg.T4, Reg.S2, Reg.T5));
        Emit(RiscVEncoder.Add(Reg.T4, Reg.T4, Reg.T3));
        Emit(RiscVEncoder.Lbu(Reg.T4, Reg.T4, 8));
        Emit(RiscVEncoder.Add(Reg.T6, Reg.T2, Reg.T3));
        Emit(RiscVEncoder.Sb(Reg.T6, Reg.T4, 8));
        Emit(RiscVEncoder.Addi(Reg.T3, Reg.T3, 1));
        Emit(RiscVEncoder.J((finalCopy - m_instructions.Count) * 4));
        int finalCopyEnd = m_instructions.Count;
        m_instructions[finalCopyDone] = RiscVEncoder.Bge(Reg.T3, Reg.T1, (finalCopyEnd - finalCopyDone) * 4);

        // Store final segment in list
        Emit(RiscVEncoder.Slli(Reg.T3, Reg.S6, 3));
        Emit(RiscVEncoder.Add(Reg.T3, Reg.S5, Reg.T3));
        Emit(RiscVEncoder.Sd(Reg.T3, Reg.T2, 8));
        Emit(RiscVEncoder.Addi(Reg.S6, Reg.S6, 1));

        // Store segment count as list length
        Emit(RiscVEncoder.Sd(Reg.S5, Reg.S6, 0));
        Emit(RiscVEncoder.Mv(Reg.A0, Reg.S5));

        Emit(RiscVEncoder.Ld(Reg.S6, Reg.Sp, 0));
        Emit(RiscVEncoder.Ld(Reg.S5, Reg.Sp, 8));
        Emit(RiscVEncoder.Ld(Reg.S4, Reg.Sp, 16));
        Emit(RiscVEncoder.Ld(Reg.S3, Reg.Sp, 24));
        Emit(RiscVEncoder.Ld(Reg.S2, Reg.Sp, 32));
        Emit(RiscVEncoder.Ld(Reg.Ra, Reg.Sp, 40));
        Emit(RiscVEncoder.Addi(Reg.Sp, Reg.Sp, 48));
        Emit(RiscVEncoder.Ret());
    }

    void EmitEscapeTextHelper()
    {
        // __escape_text: a0 = old text ptr → a0 = new text ptr (allocated at S1)
        // Copies length-prefixed string [8-byte len][data] to parent region.
        // Skip if already in result space (ptr >= ResultBaseReg).
        // Leaf function — no stack frame needed.
        m_functionOffsets["__escape_text"] = m_instructions.Count;

        // Skip copy if already in result space
        int skipAll = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // patched: bge a0, s10 → ret (skip entire copy)

        // t0 = length
        Emit(RiscVEncoder.Ld(Reg.T0, Reg.A0, 0));

        // t1 = align8(8 + length) = (length + 15) & ~7
        Emit(RiscVEncoder.Addi(Reg.T1, Reg.T0, 15));
        Emit(RiscVEncoder.Andi(Reg.T1, Reg.T1, -8));

        // a1 = new ptr = s1; s1 += t1
        Emit(RiscVEncoder.Mv(Reg.A1, Reg.S1));
        Emit(RiscVEncoder.Add(Reg.S1, Reg.S1, Reg.T1));

        // Store length at new ptr
        Emit(RiscVEncoder.Sd(Reg.A1, Reg.T0, 0));

        // Byte copy loop: for t2=0; t2 < t0; t2++
        foreach (uint insn in RiscVEncoder.Li(Reg.T2, 0)) Emit(insn);

        int loopStart = m_instructions.Count;
        int exitIdx = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // patched: bge t2, t0 → done

        Emit(RiscVEncoder.Add(Reg.T3, Reg.A0, Reg.T2));
        Emit(RiscVEncoder.Lbu(Reg.T3, Reg.T3, 8));     // byte = old[8+i]
        Emit(RiscVEncoder.Add(Reg.T4, Reg.A1, Reg.T2));
        Emit(RiscVEncoder.Sb(Reg.T4, Reg.T3, 8));       // new[8+i] = byte
        Emit(RiscVEncoder.Addi(Reg.T2, Reg.T2, 1));
        Emit(RiscVEncoder.J((loopStart - m_instructions.Count) * 4));

        int exitTarget = m_instructions.Count;
        m_instructions[exitIdx] = RiscVEncoder.Bge(Reg.T2, Reg.T0,
            (exitTarget - exitIdx) * 4);

        // Return new pointer
        Emit(RiscVEncoder.Mv(Reg.A0, Reg.A1));
        Emit(RiscVEncoder.Ret());

        // Skip target: already in result space, return a0 unchanged
        int skipTarget = m_instructions.Count;
        m_instructions[skipAll] = RiscVEncoder.Bge(Reg.A0, ResultBaseReg,
            (skipTarget - skipAll) * 4);
        Emit(RiscVEncoder.Ret());
    }

    void EmitMemmoveHelper()
    {
        // __memmove: a0=dst, a1=src, a2=len → copies len bytes from src to dst
        // Forward copy (safe when dst < src, which is always our case).
        // Leaf function — no stack frame.
        m_functionOffsets["__memmove"] = m_instructions.Count;

        // if len == 0, return immediately
        int exitCheck = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // patched: beqz a2 → done

        // t0 = index = 0
        foreach (uint insn in RiscVEncoder.Li(Reg.T0, 0)) Emit(insn);

        int loopStart = m_instructions.Count;
        // if t0 >= a2 → done
        int loopExit = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // patched: bge t0, a2 → done

        // t1 = src[t0]
        Emit(RiscVEncoder.Add(Reg.T1, Reg.A1, Reg.T0));
        Emit(RiscVEncoder.Lbu(Reg.T1, Reg.T1, 0));
        // dst[t0] = t1
        Emit(RiscVEncoder.Add(Reg.T2, Reg.A0, Reg.T0));
        Emit(RiscVEncoder.Sb(Reg.T2, Reg.T1, 0));

        Emit(RiscVEncoder.Addi(Reg.T0, Reg.T0, 1));
        Emit(RiscVEncoder.J((loopStart - m_instructions.Count) * 4));

        int doneLabel = m_instructions.Count;
        m_instructions[exitCheck] = RiscVEncoder.Beq(Reg.A2, Reg.Zero,
            (doneLabel - exitCheck) * 4);
        m_instructions[loopExit] = RiscVEncoder.Bge(Reg.T0, Reg.A2,
            (doneLabel - loopExit) * 4);

        Emit(RiscVEncoder.Ret());
    }

    void EmitIpowHelper()
    {
        // __ipow: a0=base, a1=exponent → a0=base^exponent
        // Exponentiation by squaring. Leaf function.
        m_functionOffsets["__ipow"] = m_instructions.Count;

        // result (t0) = 1
        foreach (uint insn in RiscVEncoder.Li(Reg.T0, 1)) Emit(insn);

        // if exponent < 0 → return 0
        int jmpNeg = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // patched: blt a1, zero → negPath

        // if exponent == 0 → return 1
        int jmpZero = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // patched: beq a1, zero → done

        // Loop: while exponent > 0
        int loopTop = m_instructions.Count;

        // if (exponent & 1) result *= base
        Emit(RiscVEncoder.Andi(Reg.T1, Reg.A1, 1));
        int skipMul = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // patched: beqz t1 → skipMulTarget
        Emit(RiscVEncoder.Mul(Reg.T0, Reg.T0, Reg.A0)); // result *= base
        int skipMulTarget = m_instructions.Count;
        m_instructions[skipMul] = RiscVEncoder.Beq(Reg.T1, Reg.Zero,
            (skipMulTarget - skipMul) * 4);

        // base *= base
        Emit(RiscVEncoder.Mul(Reg.A0, Reg.A0, Reg.A0));
        // exponent >>= 1
        Emit(RiscVEncoder.Srli(Reg.A1, Reg.A1, 1));

        // if exponent > 0, loop
        int jmpLoop = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // patched: bne a1, zero → loopTop
        m_instructions[jmpLoop] = RiscVEncoder.Bne(Reg.A1, Reg.Zero,
            (loopTop - jmpLoop) * 4);

        // done: return result
        int done = m_instructions.Count;
        m_instructions[jmpZero] = RiscVEncoder.Beq(Reg.A1, Reg.Zero,
            (done - jmpZero) * 4);
        Emit(RiscVEncoder.Mv(Reg.A0, Reg.T0));
        Emit(RiscVEncoder.Ret());

        // negPath: return 0
        int negPath = m_instructions.Count;
        m_instructions[jmpNeg] = RiscVEncoder.Blt(Reg.A1, Reg.Zero,
            (negPath - jmpNeg) * 4);
        Emit(RiscVEncoder.Mv(Reg.A0, Reg.Zero));
        Emit(RiscVEncoder.Ret());
    }

    void EmitStrEqHelper()
    {
        // __str_eq: a0=ptr1, a1=ptr2 → a0=1 if equal, 0 if not
        // Uses only caller-saved registers — no prologue needed.
        m_functionOffsets["__str_eq"] = m_instructions.Count;

        Emit(RiscVEncoder.Ld(Reg.T0, Reg.A0, 0));    // t0 = len1
        Emit(RiscVEncoder.Ld(Reg.T1, Reg.A1, 0));    // t1 = len2

        int bneLen = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // patched: bne t0, t1 → not_eq

        Emit(RiscVEncoder.Addi(Reg.A0, Reg.A0, 8));  // data1
        Emit(RiscVEncoder.Addi(Reg.A1, Reg.A1, 8));  // data2
        foreach (uint insn in RiscVEncoder.Li(Reg.T2, 0)) Emit(insn);

        int loopStart = m_instructions.Count;
        int bgeIdx = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // patched: bge t2, t0 → equal

        Emit(RiscVEncoder.Add(Reg.T3, Reg.A0, Reg.T2));
        Emit(RiscVEncoder.Lbu(Reg.T3, Reg.T3, 0));
        Emit(RiscVEncoder.Add(Reg.T4, Reg.A1, Reg.T2));
        Emit(RiscVEncoder.Lbu(Reg.T4, Reg.T4, 0));

        int bneByte = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // patched: bne t3, t4 → not_eq

        Emit(RiscVEncoder.Addi(Reg.T2, Reg.T2, 1));
        Emit(RiscVEncoder.J((loopStart - m_instructions.Count) * 4));

        int equalLabel = m_instructions.Count;
        foreach (uint insn in RiscVEncoder.Li(Reg.A0, 1)) Emit(insn);
        Emit(RiscVEncoder.Ret());

        int notEqLabel = m_instructions.Count;
        foreach (uint insn in RiscVEncoder.Li(Reg.A0, 0)) Emit(insn);
        Emit(RiscVEncoder.Ret());

        m_instructions[bneLen] = RiscVEncoder.Bne(Reg.T0, Reg.T1, (notEqLabel - bneLen) * 4);
        m_instructions[bgeIdx] = RiscVEncoder.Bge(Reg.T2, Reg.T0, (equalLabel - bgeIdx) * 4);
        m_instructions[bneByte] = RiscVEncoder.Bne(Reg.T3, Reg.T4, (notEqLabel - bneByte) * 4);
    }

    void EmitStrConcatHelper()
    {
        // __str_concat: a0=ptr1, a1=ptr2 → a0=new string on heap
        m_functionOffsets["__str_concat"] = m_instructions.Count;

        Emit(RiscVEncoder.Ld(Reg.T0, Reg.A0, 0));    // t0 = len1
        Emit(RiscVEncoder.Ld(Reg.T1, Reg.A1, 0));    // t1 = len2
        Emit(RiscVEncoder.Mv(Reg.T4, Reg.A0));        // t4 = ptr1
        Emit(RiscVEncoder.Mv(Reg.T5, Reg.A1));        // t5 = ptr2
        Emit(RiscVEncoder.Add(Reg.T6, Reg.T0, Reg.T1)); // t6 = total_len

        // Alloc: a0 = result = s1; s1 += align8(8 + total)
        Emit(RiscVEncoder.Mv(Reg.A0, Reg.S1));
        Emit(RiscVEncoder.Addi(Reg.A1, Reg.T6, 15));
        Emit(RiscVEncoder.Andi(Reg.A1, Reg.A1, -8));
        Emit(RiscVEncoder.Add(Reg.S1, Reg.S1, Reg.A1));

        // Store total length
        Emit(RiscVEncoder.Sd(Reg.A0, Reg.T6, 0));

        // Copy first string: src=t4+8, dst=a0+8, len=t0
        Emit(RiscVEncoder.Addi(Reg.A1, Reg.A0, 8));   // a1 = dst
        Emit(RiscVEncoder.Addi(Reg.A2, Reg.T4, 8));   // a2 = src
        foreach (uint insn in RiscVEncoder.Li(Reg.T2, 0)) Emit(insn);

        int loop1Start = m_instructions.Count;
        int exit1Idx = m_instructions.Count;
        Emit(RiscVEncoder.Nop());
        Emit(RiscVEncoder.Add(Reg.T3, Reg.A2, Reg.T2));
        Emit(RiscVEncoder.Lbu(Reg.T3, Reg.T3, 0));
        Emit(RiscVEncoder.Add(Reg.A3, Reg.A1, Reg.T2));
        Emit(RiscVEncoder.Sb(Reg.A3, Reg.T3, 0));
        Emit(RiscVEncoder.Addi(Reg.T2, Reg.T2, 1));
        Emit(RiscVEncoder.J((loop1Start - m_instructions.Count) * 4));
        int exit1Target = m_instructions.Count;
        m_instructions[exit1Idx] = RiscVEncoder.Bge(Reg.T2, Reg.T0, (exit1Target - exit1Idx) * 4);

        // Copy second string: src=t5+8, dst=a1+t0 (= result+8+len1), len=t1
        Emit(RiscVEncoder.Add(Reg.A1, Reg.A1, Reg.T0));  // dst += len1
        Emit(RiscVEncoder.Addi(Reg.A2, Reg.T5, 8));      // src = ptr2+8
        foreach (uint insn in RiscVEncoder.Li(Reg.T2, 0)) Emit(insn);

        int loop2Start = m_instructions.Count;
        int exit2Idx = m_instructions.Count;
        Emit(RiscVEncoder.Nop());
        Emit(RiscVEncoder.Add(Reg.T3, Reg.A2, Reg.T2));
        Emit(RiscVEncoder.Lbu(Reg.T3, Reg.T3, 0));
        Emit(RiscVEncoder.Add(Reg.A3, Reg.A1, Reg.T2));
        Emit(RiscVEncoder.Sb(Reg.A3, Reg.T3, 0));
        Emit(RiscVEncoder.Addi(Reg.T2, Reg.T2, 1));
        Emit(RiscVEncoder.J((loop2Start - m_instructions.Count) * 4));
        int exit2Target = m_instructions.Count;
        m_instructions[exit2Idx] = RiscVEncoder.Bge(Reg.T2, Reg.T1, (exit2Target - exit2Idx) * 4);

        Emit(RiscVEncoder.Ret());
    }

    void EmitItoaHelper()
    {
        // __itoa: a0=integer → a0=ptr to length-prefixed string on heap
        m_functionOffsets["__itoa"] = m_instructions.Count;

        Emit(RiscVEncoder.Addi(Reg.Sp, Reg.Sp, -32));
        Emit(RiscVEncoder.Mv(Reg.T4, Reg.A0));           // t4 = value
        Emit(RiscVEncoder.Slt(Reg.T5, Reg.T4, Reg.Zero)); // t5 = is_negative

        int skipNeg = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // patched: beqz t5 → positive
        Emit(RiscVEncoder.Sub(Reg.T4, Reg.Zero, Reg.T4));
        int posLabel = m_instructions.Count;
        m_instructions[skipNeg] = RiscVEncoder.Beq(Reg.T5, Reg.Zero, (posLabel - skipNeg) * 4);

        Emit(RiscVEncoder.Addi(Reg.T0, Reg.Sp, 30));     // t0 = cursor
        foreach (uint insn in RiscVEncoder.Li(Reg.T1, 0)) Emit(insn); // t1 = count

        // Zero check
        int skipZero = m_instructions.Count;
        Emit(RiscVEncoder.Nop());
        foreach (uint insn in RiscVEncoder.Li(Reg.T2, 3)) Emit(insn); // CCE '0' = 3
        Emit(RiscVEncoder.Sb(Reg.T0, Reg.T2, 0));
        Emit(RiscVEncoder.Addi(Reg.T0, Reg.T0, -1));
        Emit(RiscVEncoder.Addi(Reg.T1, Reg.T1, 1));
        int jSign = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // j → sign_check

        int digitLoop = m_instructions.Count;
        m_instructions[skipZero] = RiscVEncoder.Bne(Reg.T4, Reg.Zero, (digitLoop - skipZero) * 4);

        int loopExit = m_instructions.Count;
        Emit(RiscVEncoder.Nop());
        foreach (uint insn in RiscVEncoder.Li(Reg.T3, 10)) Emit(insn);
        Emit(RiscVEncoder.Rem(Reg.T2, Reg.T4, Reg.T3));
        Emit(RiscVEncoder.Addi(Reg.T2, Reg.T2, 3)); // CCE digit offset: '0' = 3
        Emit(RiscVEncoder.Sb(Reg.T0, Reg.T2, 0));
        Emit(RiscVEncoder.Addi(Reg.T0, Reg.T0, -1));
        Emit(RiscVEncoder.Addi(Reg.T1, Reg.T1, 1));
        Emit(RiscVEncoder.Div(Reg.T4, Reg.T4, Reg.T3));
        Emit(RiscVEncoder.J((digitLoop - m_instructions.Count) * 4));

        int signCheck = m_instructions.Count;
        m_instructions[loopExit] = RiscVEncoder.Beq(Reg.T4, Reg.Zero, (signCheck - loopExit) * 4);
        m_instructions[jSign] = RiscVEncoder.J((signCheck - jSign) * 4);

        int skipMinus = m_instructions.Count;
        Emit(RiscVEncoder.Nop());
        foreach (uint insn in RiscVEncoder.Li(Reg.T2, 73)) Emit(insn); // CCE '-' = 73
        Emit(RiscVEncoder.Sb(Reg.T0, Reg.T2, 0));
        Emit(RiscVEncoder.Addi(Reg.T0, Reg.T0, -1));
        Emit(RiscVEncoder.Addi(Reg.T1, Reg.T1, 1));
        int afterMinus = m_instructions.Count;
        m_instructions[skipMinus] = RiscVEncoder.Beq(Reg.T5, Reg.Zero, (afterMinus - skipMinus) * 4);

        // t0+1 = start of digits, t1 = length. Alloc on heap.
        Emit(RiscVEncoder.Addi(Reg.T0, Reg.T0, 1));
        Emit(RiscVEncoder.Mv(Reg.A0, Reg.S1));
        Emit(RiscVEncoder.Addi(Reg.T2, Reg.T1, 15));
        Emit(RiscVEncoder.Andi(Reg.T2, Reg.T2, -8));
        Emit(RiscVEncoder.Add(Reg.S1, Reg.S1, Reg.T2));

        Emit(RiscVEncoder.Sd(Reg.A0, Reg.T1, 0));

        // Copy from stack to heap
        foreach (uint insn in RiscVEncoder.Li(Reg.T2, 0)) Emit(insn);
        int copyLoop = m_instructions.Count;
        int copyExit = m_instructions.Count;
        Emit(RiscVEncoder.Nop());
        Emit(RiscVEncoder.Add(Reg.T3, Reg.T0, Reg.T2));
        Emit(RiscVEncoder.Lbu(Reg.T3, Reg.T3, 0));
        Emit(RiscVEncoder.Add(Reg.T4, Reg.A0, Reg.T2));
        Emit(RiscVEncoder.Sb(Reg.T4, Reg.T3, 8));
        Emit(RiscVEncoder.Addi(Reg.T2, Reg.T2, 1));
        Emit(RiscVEncoder.J((copyLoop - m_instructions.Count) * 4));
        int copyExitTarget = m_instructions.Count;
        m_instructions[copyExit] = RiscVEncoder.Bge(Reg.T2, Reg.T1, (copyExitTarget - copyExit) * 4);

        Emit(RiscVEncoder.Addi(Reg.Sp, Reg.Sp, 32));
        Emit(RiscVEncoder.Ret());
    }

    void EmitTextToIntHelper()
    {
        // __text_to_int: a0=ptr → a0=integer
        m_functionOffsets["__text_to_int"] = m_instructions.Count;

        Emit(RiscVEncoder.Ld(Reg.T0, Reg.A0, 0));       // t0 = length
        Emit(RiscVEncoder.Addi(Reg.A0, Reg.A0, 8));      // a0 = data
        foreach (uint insn in RiscVEncoder.Li(Reg.T1, 0)) Emit(insn); // t1 = result
        foreach (uint insn in RiscVEncoder.Li(Reg.T2, 0)) Emit(insn); // t2 = index
        foreach (uint insn in RiscVEncoder.Li(Reg.T3, 0)) Emit(insn); // t3 = is_negative

        // Check for '-'
        int skipNegCheck = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // beqz t0 → done (empty string)
        Emit(RiscVEncoder.Lbu(Reg.T4, Reg.A0, 0));
        foreach (uint insn in RiscVEncoder.Li(Reg.T5, 73)) Emit(insn); // CCE '-' = 73
        int skipMinus = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // bne t4, t5 → parse
        foreach (uint insn in RiscVEncoder.Li(Reg.T3, 1)) Emit(insn);
        Emit(RiscVEncoder.Addi(Reg.T2, Reg.T2, 1));

        int parseLoop = m_instructions.Count;
        m_instructions[skipMinus] = RiscVEncoder.Bne(Reg.T4, Reg.T5, (parseLoop - skipMinus) * 4);

        int parseExit = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // bge t2, t0 → negate
        Emit(RiscVEncoder.Add(Reg.T4, Reg.A0, Reg.T2));
        Emit(RiscVEncoder.Lbu(Reg.T4, Reg.T4, 0));
        Emit(RiscVEncoder.Addi(Reg.T4, Reg.T4, -3)); // CCE '0' = 3
        foreach (uint insn in RiscVEncoder.Li(Reg.T5, 10)) Emit(insn);
        Emit(RiscVEncoder.Mul(Reg.T1, Reg.T1, Reg.T5));
        Emit(RiscVEncoder.Add(Reg.T1, Reg.T1, Reg.T4));
        Emit(RiscVEncoder.Addi(Reg.T2, Reg.T2, 1));
        Emit(RiscVEncoder.J((parseLoop - m_instructions.Count) * 4));

        int negateCheck = m_instructions.Count;
        m_instructions[parseExit] = RiscVEncoder.Bge(Reg.T2, Reg.T0, (negateCheck - parseExit) * 4);

        int skipNegate = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // beqz t3 → done
        Emit(RiscVEncoder.Sub(Reg.T1, Reg.Zero, Reg.T1));

        int doneLabel = m_instructions.Count;
        m_instructions[skipNegCheck] = RiscVEncoder.Beq(Reg.T0, Reg.Zero, (doneLabel - skipNegCheck) * 4);
        m_instructions[skipNegate] = RiscVEncoder.Beq(Reg.T3, Reg.Zero, (doneLabel - skipNegate) * 4);

        Emit(RiscVEncoder.Mv(Reg.A0, Reg.T1));
        Emit(RiscVEncoder.Ret());
    }

    void EmitListConsHelper()
    {
        // __list_cons: a0=head, a1=tail_list_ptr → a0=new list with head prepended
        // Layout: [capacity @ -8 | count @ 0 | elem0 @ 8 | ...]
        m_functionOffsets["__list_cons"] = m_instructions.Count;

        Emit(RiscVEncoder.Ld(Reg.T0, Reg.A1, 0));         // t0 = old length
        Emit(RiscVEncoder.Addi(Reg.T1, Reg.T0, 1));       // t1 = new length
        Emit(RiscVEncoder.Mv(Reg.T3, Reg.A0));            // t3 = head value
        Emit(RiscVEncoder.Mv(Reg.T4, Reg.A1));            // t4 = tail list ptr

        // Alloc: [capacity | count | elements] = (newLen + 2) * 8
        Emit(RiscVEncoder.Sd(Reg.S1, Reg.T1, 0));         // capacity = newLen
        Emit(RiscVEncoder.Addi(Reg.S1, Reg.S1, 8));       // past capacity
        Emit(RiscVEncoder.Mv(Reg.A0, Reg.S1));            // result = list ptr
        Emit(RiscVEncoder.Addi(Reg.T2, Reg.T1, 1));       // t2 = newLen + 1
        foreach (uint insn in RiscVEncoder.Li(Reg.T5, 8)) Emit(insn);
        Emit(RiscVEncoder.Mul(Reg.T2, Reg.T2, Reg.T5));   // t2 = (newLen+1)*8
        Emit(RiscVEncoder.Add(Reg.S1, Reg.S1, Reg.T2));   // bump heap

        // Store new length and head
        Emit(RiscVEncoder.Sd(Reg.A0, Reg.T1, 0));         // [0] = new length
        Emit(RiscVEncoder.Sd(Reg.A0, Reg.T3, 8));         // [8] = head

        // Copy old elements: t2 = byte offset (0, 8, 16, ...), t5 = oldLen*8
        foreach (uint insn in RiscVEncoder.Li(Reg.T5, 8)) Emit(insn);
        Emit(RiscVEncoder.Mul(Reg.T5, Reg.T0, Reg.T5));   // t5 = oldLen * 8
        foreach (uint insn in RiscVEncoder.Li(Reg.T2, 0)) Emit(insn);

        int loopStart = m_instructions.Count;
        int exitIdx = m_instructions.Count;
        Emit(RiscVEncoder.Nop());                          // patched: bge t2, t5 → exit
        Emit(RiscVEncoder.Add(Reg.T6, Reg.T4, Reg.T2));
        Emit(RiscVEncoder.Ld(Reg.T6, Reg.T6, 8));         // src[8 + t2]
        Emit(RiscVEncoder.Add(Reg.T3, Reg.A0, Reg.T2));
        Emit(RiscVEncoder.Sd(Reg.T3, Reg.T6, 16));        // dst[16 + t2]
        Emit(RiscVEncoder.Addi(Reg.T2, Reg.T2, 8));
        Emit(RiscVEncoder.J((loopStart - m_instructions.Count) * 4));
        int exitTarget = m_instructions.Count;
        m_instructions[exitIdx] = RiscVEncoder.Bge(Reg.T2, Reg.T5, (exitTarget - exitIdx) * 4);

        Emit(RiscVEncoder.Ret());
    }

    void EmitListAppendHelper()
    {
        // __list_append: a0=list1, a1=list2 → a0=concatenated list
        m_functionOffsets["__list_append"] = m_instructions.Count;

        Emit(RiscVEncoder.Ld(Reg.T0, Reg.A0, 0));        // t0 = len1
        Emit(RiscVEncoder.Ld(Reg.T1, Reg.A1, 0));        // t1 = len2
        Emit(RiscVEncoder.Add(Reg.T6, Reg.T0, Reg.T1));  // t6 = total len
        Emit(RiscVEncoder.Mv(Reg.T4, Reg.A0));            // save list1
        Emit(RiscVEncoder.Mv(Reg.T5, Reg.A1));            // save list2

        // Alloc: [capacity | count | elements] = (totalLen + 2) * 8
        Emit(RiscVEncoder.Sd(Reg.S1, Reg.T6, 0));         // capacity = total
        Emit(RiscVEncoder.Addi(Reg.S1, Reg.S1, 8));       // past capacity
        Emit(RiscVEncoder.Mv(Reg.A0, Reg.S1));            // list ptr
        Emit(RiscVEncoder.Addi(Reg.A1, Reg.T6, 1));       // total + 1
        foreach (uint insn in RiscVEncoder.Li(Reg.A2, 8)) Emit(insn);
        Emit(RiscVEncoder.Mul(Reg.A1, Reg.A1, Reg.A2));
        Emit(RiscVEncoder.Add(Reg.S1, Reg.S1, Reg.A1));

        // Store total length
        Emit(RiscVEncoder.Sd(Reg.A0, Reg.T6, 0));

        // Copy list1 elements: src=t4+8, dst=a0+8, count=t0 (as 8-byte units)
        foreach (uint insn in RiscVEncoder.Li(Reg.T2, 0)) Emit(insn);
        foreach (uint insn in RiscVEncoder.Li(Reg.T3, 8)) Emit(insn);
        Emit(RiscVEncoder.Mul(Reg.A1, Reg.T0, Reg.T3)); // a1 = len1 * 8

        int loop1Start = m_instructions.Count;
        int exit1Idx = m_instructions.Count;
        Emit(RiscVEncoder.Nop());
        Emit(RiscVEncoder.Add(Reg.T3, Reg.T4, Reg.T2));
        Emit(RiscVEncoder.Ld(Reg.T3, Reg.T3, 8));
        Emit(RiscVEncoder.Add(Reg.A2, Reg.A0, Reg.T2));
        Emit(RiscVEncoder.Sd(Reg.A2, Reg.T3, 8));
        Emit(RiscVEncoder.Addi(Reg.T2, Reg.T2, 8));
        Emit(RiscVEncoder.J((loop1Start - m_instructions.Count) * 4));
        int exit1Target = m_instructions.Count;
        m_instructions[exit1Idx] = RiscVEncoder.Bge(Reg.T2, Reg.A1, (exit1Target - exit1Idx) * 4);

        // Copy list2 elements: src=t5+8, dst=a0+8+len1*8, count=t1
        // t2 already = len1*8, use as dst offset base
        foreach (uint insn in RiscVEncoder.Li(Reg.T3, 8)) Emit(insn);
        Emit(RiscVEncoder.Mul(Reg.A1, Reg.T1, Reg.T3)); // a1 = len2 * 8
        foreach (uint insn in RiscVEncoder.Li(Reg.T3, 0)) Emit(insn); // t3 = src index

        int loop2Start = m_instructions.Count;
        int exit2Idx = m_instructions.Count;
        Emit(RiscVEncoder.Nop());
        Emit(RiscVEncoder.Add(Reg.A2, Reg.T5, Reg.T3));
        Emit(RiscVEncoder.Ld(Reg.A2, Reg.A2, 8));
        Emit(RiscVEncoder.Add(Reg.A3, Reg.T2, Reg.T3));
        Emit(RiscVEncoder.Add(Reg.A3, Reg.A0, Reg.A3));
        Emit(RiscVEncoder.Sd(Reg.A3, Reg.A2, 8));
        Emit(RiscVEncoder.Addi(Reg.T3, Reg.T3, 8));
        Emit(RiscVEncoder.J((loop2Start - m_instructions.Count) * 4));
        int exit2Target = m_instructions.Count;
        m_instructions[exit2Idx] = RiscVEncoder.Bge(Reg.T3, Reg.A1, (exit2Target - exit2Idx) * 4);

        Emit(RiscVEncoder.Ret());
    }

    void EmitReadFileHelper()
    {
        // __read_file: a0=path_ptr (length-prefixed) → a0=content_ptr (length-prefixed)
        // Linux only: openat(AT_FDCWD, path, O_RDONLY) → read loop → close
        m_functionOffsets["__read_file"] = m_instructions.Count;

        if (m_target == RiscVTarget.BareMetal)
        {
            // Bare metal: read source from UART until EOT (0x04)
            EmitCallTo("__bare_metal_read_serial");
            Emit(RiscVEncoder.Ret());
            return;
        }

        // Save path data ptr in t4, skip length prefix
        Emit(RiscVEncoder.Addi(Reg.Sp, Reg.Sp, -48));
        Emit(RiscVEncoder.Sd(Reg.Sp, Reg.Ra, 40));

        // We need a null-terminated path for the syscall.
        // Copy path bytes to stack buffer and add null terminator.
        // For simplicity, use heap as temp: copy path, add \0, openat, then restore heap.
        Emit(RiscVEncoder.Ld(Reg.T0, Reg.A0, 0));          // t0 = path length
        Emit(RiscVEncoder.Addi(Reg.T1, Reg.A0, 8));         // t1 = path data

        // Null-terminate: copy to heap with CCE→Unicode conversion (output boundary to OS).
        Emit(RiscVEncoder.Mv(Reg.T4, Reg.S1));              // save heap ptr
        // Load CCE→Unicode table address into a saved register
        Emit(RiscVEncoder.Sd(Reg.Sp, Reg.S2, 32));          // save s2
        m_rodataFixups.Add(new RodataFixup(m_instructions.Count, Reg.S2, m_cceToUnicodeTableOffset));
        Emit(RiscVEncoder.Nop());                            // 2 slots for rodata fixup
        Emit(RiscVEncoder.Nop());
        // Copy path bytes to heap with CCE→Unicode conversion
        foreach (uint insn in RiscVEncoder.Li(Reg.T2, 0)) Emit(insn);
        int cpLoop = m_instructions.Count;
        int cpExit = m_instructions.Count;
        Emit(RiscVEncoder.Nop());
        Emit(RiscVEncoder.Add(Reg.T3, Reg.T1, Reg.T2));
        Emit(RiscVEncoder.Lbu(Reg.T3, Reg.T3, 0));          // t3 = CCE byte
        // CCE→Unicode: t3 = table[t3]
        Emit(RiscVEncoder.Add(Reg.T5, Reg.S2, Reg.T3));
        Emit(RiscVEncoder.Lbu(Reg.T3, Reg.T5, 0));          // t3 = Unicode byte
        Emit(RiscVEncoder.Add(Reg.T5, Reg.T4, Reg.T2));
        Emit(RiscVEncoder.Sb(Reg.T5, Reg.T3, 0));
        Emit(RiscVEncoder.Addi(Reg.T2, Reg.T2, 1));
        Emit(RiscVEncoder.J((cpLoop - m_instructions.Count) * 4));
        int cpTarget = m_instructions.Count;
        m_instructions[cpExit] = RiscVEncoder.Bge(Reg.T2, Reg.T0, (cpTarget - cpExit) * 4);
        // Null terminate
        Emit(RiscVEncoder.Add(Reg.T5, Reg.T4, Reg.T0));
        Emit(RiscVEncoder.Sb(Reg.T5, Reg.Zero, 0));

        // openat(AT_FDCWD=-100, path, O_RDONLY=0, 0)
        foreach (uint insn in RiscVEncoder.Li(Reg.A0, -100)) Emit(insn);
        Emit(RiscVEncoder.Mv(Reg.A1, Reg.T4));
        foreach (uint insn in RiscVEncoder.Li(Reg.A2, 0)) Emit(insn);
        foreach (uint insn in RiscVEncoder.Li(Reg.A3, 0)) Emit(insn);
        foreach (uint insn in RiscVEncoder.Li(Reg.A7, 56)) Emit(insn); // SYS_openat
        Emit(RiscVEncoder.Ecall());

        Emit(RiscVEncoder.Sd(Reg.Sp, Reg.A0, 0));          // save fd
        // Result buffer starts after path temp on heap
        Emit(RiscVEncoder.Add(Reg.T4, Reg.T4, Reg.T0));
        Emit(RiscVEncoder.Addi(Reg.T4, Reg.T4, 8));         // skip null term + align
        Emit(RiscVEncoder.Sd(Reg.Sp, Reg.T4, 8));           // save result base

        // Read loop: read(fd, buf, 4096) until 0
        Emit(RiscVEncoder.Mv(Reg.T5, Reg.T4));              // t5 = write cursor
        foreach (uint insn in RiscVEncoder.Li(Reg.T6, 0)) Emit(insn); // t6 = total bytes

        int readLoop = m_instructions.Count;
        Emit(RiscVEncoder.Ld(Reg.A0, Reg.Sp, 0));           // fd
        Emit(RiscVEncoder.Addi(Reg.A1, Reg.T5, 8));         // buf (after length prefix)
        Emit(RiscVEncoder.Add(Reg.A1, Reg.A1, Reg.T6));     // + offset
        foreach (uint insn in RiscVEncoder.Li(Reg.A2, 4096)) Emit(insn);
        foreach (uint insn in RiscVEncoder.Li(Reg.A7, 63)) Emit(insn); // SYS_read
        Emit(RiscVEncoder.Ecall());
        // a0 = bytes read, 0 = EOF
        int doneRead = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // patched: beqz a0 → done
        Emit(RiscVEncoder.Add(Reg.T6, Reg.T6, Reg.A0));
        Emit(RiscVEncoder.J((readLoop - m_instructions.Count) * 4));
        int doneTarget = m_instructions.Count;
        m_instructions[doneRead] = RiscVEncoder.Beq(Reg.A0, Reg.Zero, (doneTarget - doneRead) * 4);

        // close(fd)
        Emit(RiscVEncoder.Ld(Reg.A0, Reg.Sp, 0));
        foreach (uint insn in RiscVEncoder.Li(Reg.A7, 57)) Emit(insn); // SYS_close
        Emit(RiscVEncoder.Ecall());

        // Convert file content from Unicode→CCE in place (input boundary).
        m_rodataFixups.Add(new RodataFixup(m_instructions.Count, Reg.S2, m_unicodeToCceTableOffset));
        Emit(RiscVEncoder.Nop());                            // 2 slots for rodata fixup
        Emit(RiscVEncoder.Nop());
        Emit(RiscVEncoder.Ld(Reg.T5, Reg.Sp, 8));           // result base
        foreach (uint insn in RiscVEncoder.Li(Reg.T2, 0)) Emit(insn);
        int convLoop = m_instructions.Count;
        int convExit = m_instructions.Count;
        Emit(RiscVEncoder.Nop());
        Emit(RiscVEncoder.Add(Reg.T3, Reg.T5, Reg.T2));
        Emit(RiscVEncoder.Lbu(Reg.T3, Reg.T3, 8));          // Unicode byte at data[i]
        Emit(RiscVEncoder.Add(Reg.T0, Reg.S2, Reg.T3));
        Emit(RiscVEncoder.Lbu(Reg.T3, Reg.T0, 0));          // CCE byte = table[unicode]
        Emit(RiscVEncoder.Add(Reg.T0, Reg.T5, Reg.T2));
        Emit(RiscVEncoder.Sb(Reg.T0, Reg.T3, 8));           // store CCE byte back
        Emit(RiscVEncoder.Addi(Reg.T2, Reg.T2, 1));
        Emit(RiscVEncoder.J((convLoop - m_instructions.Count) * 4));
        int convTarget = m_instructions.Count;
        m_instructions[convExit] = RiscVEncoder.Bge(Reg.T2, Reg.T6, (convTarget - convExit) * 4);

        // Build result: store length at t5, bump heap past data
        Emit(RiscVEncoder.Sd(Reg.T5, Reg.T6, 0));           // store length

        // Bump heap past the result string
        Emit(RiscVEncoder.Addi(Reg.A0, Reg.T6, 15));
        Emit(RiscVEncoder.Andi(Reg.A0, Reg.A0, -8));
        Emit(RiscVEncoder.Addi(Reg.A0, Reg.A0, 8));         // + length prefix
        Emit(RiscVEncoder.Add(Reg.S1, Reg.T5, Reg.A0));

        Emit(RiscVEncoder.Mv(Reg.A0, Reg.T5));              // return result ptr
        Emit(RiscVEncoder.Ld(Reg.S2, Reg.Sp, 32));          // restore s2
        Emit(RiscVEncoder.Ld(Reg.Ra, Reg.Sp, 40));
        Emit(RiscVEncoder.Addi(Reg.Sp, Reg.Sp, 48));
        Emit(RiscVEncoder.Ret());
    }

    void EmitReadLineHelper()
    {
        // __read_line: → a0=string ptr (reads from stdin until \n)
        m_functionOffsets["__read_line"] = m_instructions.Count;

        if (m_target == RiscVTarget.BareMetal)
        {
            // Return hardcoded "test.codex" path (ignored by read-file on bare metal)
            int pathOffset = AddRodataString("test.codex");
            EmitLoadRodataAddress(Reg.A0, pathOffset);
            Emit(RiscVEncoder.Ret());
            return;
        }

        // Read byte-by-byte into heap, stop at \n or EOF.
        // Convert Unicode→CCE before storing (input boundary).
        Emit(RiscVEncoder.Mv(Reg.T4, Reg.S1));              // result base
        foreach (uint insn in RiscVEncoder.Li(Reg.T5, 0)) Emit(insn); // byte count

        // Load Unicode→CCE table address into T6 (survives across syscall)
        m_rodataFixups.Add(new RodataFixup(m_instructions.Count, Reg.T6, m_unicodeToCceTableOffset));
        Emit(RiscVEncoder.Nop());                            // 2 slots for rodata fixup
        Emit(RiscVEncoder.Nop());

        int rdLoop = m_instructions.Count;
        // read(0, &byte_on_stack, 1)
        Emit(RiscVEncoder.Addi(Reg.Sp, Reg.Sp, -8));
        foreach (uint insn in RiscVEncoder.Li(Reg.A0, 0)) Emit(insn);
        Emit(RiscVEncoder.Mv(Reg.A1, Reg.Sp));
        foreach (uint insn in RiscVEncoder.Li(Reg.A2, 1)) Emit(insn);
        foreach (uint insn in RiscVEncoder.Li(Reg.A7, 63)) Emit(insn);
        Emit(RiscVEncoder.Ecall());
        Emit(RiscVEncoder.Lbu(Reg.T0, Reg.Sp, 0));
        Emit(RiscVEncoder.Addi(Reg.Sp, Reg.Sp, 8));

        // Check EOF (a0 <= 0) or newline (t0 == '\n') — check Unicode newline BEFORE CCE conversion
        int eofCheck = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // patched: beqz a0 → done
        foreach (uint insn in RiscVEncoder.Li(Reg.T1, '\n')) Emit(insn);
        int nlCheck = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // patched: beq t0, t1 → done

        // Convert Unicode→CCE: t0 = table[t0]
        Emit(RiscVEncoder.Add(Reg.T1, Reg.T6, Reg.T0));
        Emit(RiscVEncoder.Lbu(Reg.T0, Reg.T1, 0));

        // Store CCE byte at result + 8 + count
        Emit(RiscVEncoder.Add(Reg.T1, Reg.T4, Reg.T5));
        Emit(RiscVEncoder.Sb(Reg.T1, Reg.T0, 8));
        Emit(RiscVEncoder.Addi(Reg.T5, Reg.T5, 1));
        Emit(RiscVEncoder.J((rdLoop - m_instructions.Count) * 4));

        int doneLabel = m_instructions.Count;
        m_instructions[eofCheck] = RiscVEncoder.Beq(Reg.A0, Reg.Zero, (doneLabel - eofCheck) * 4);
        m_instructions[nlCheck] = RiscVEncoder.Beq(Reg.T0, Reg.T1, (doneLabel - nlCheck) * 4);

        // Store length and bump heap past length prefix + data
        Emit(RiscVEncoder.Sd(Reg.T4, Reg.T5, 0));
        Emit(RiscVEncoder.Addi(Reg.A0, Reg.T5, 15));
        Emit(RiscVEncoder.Andi(Reg.A0, Reg.A0, -8));
        Emit(RiscVEncoder.Addi(Reg.A0, Reg.A0, 8));         // + length prefix
        Emit(RiscVEncoder.Add(Reg.S1, Reg.T4, Reg.A0));
        Emit(RiscVEncoder.Mv(Reg.A0, Reg.T4));
        Emit(RiscVEncoder.Ret());
    }

    void EmitStrReplaceHelper()
    {
        // __str_replace: a0=text, a1=old, a2=new → a0=result string
        // Scans text for occurrences of old, replaces with new.
        // Uses stack frame to save inputs.
        m_functionOffsets["__str_replace"] = m_instructions.Count;

        // Save ra and inputs on stack
        Emit(RiscVEncoder.Addi(Reg.Sp, Reg.Sp, -64));
        Emit(RiscVEncoder.Sd(Reg.Sp, Reg.Ra, 56));
        Emit(RiscVEncoder.Sd(Reg.Sp, Reg.A0, 0));   // [sp+0]  = text
        Emit(RiscVEncoder.Sd(Reg.Sp, Reg.A1, 8));   // [sp+8]  = old
        Emit(RiscVEncoder.Sd(Reg.Sp, Reg.A2, 16));  // [sp+16] = new

        // Load lengths
        Emit(RiscVEncoder.Ld(Reg.T0, Reg.A0, 0));   // t0 = text_len
        Emit(RiscVEncoder.Ld(Reg.T1, Reg.A1, 0));   // t1 = old_len
        Emit(RiscVEncoder.Ld(Reg.T2, Reg.A2, 0));   // t2 = new_len

        Emit(RiscVEncoder.Sd(Reg.Sp, Reg.T0, 24));  // [sp+24] = text_len
        Emit(RiscVEncoder.Sd(Reg.Sp, Reg.T1, 32));  // [sp+32] = old_len
        Emit(RiscVEncoder.Sd(Reg.Sp, Reg.T2, 40));  // [sp+40] = new_len

        // Result buffer at current heap ptr; write cursor at result+8
        Emit(RiscVEncoder.Mv(Reg.T4, Reg.S1));       // t4 = result base
        Emit(RiscVEncoder.Sd(Reg.Sp, Reg.T4, 48));   // [sp+48] = result base
        foreach (uint insn in RiscVEncoder.Li(Reg.T5, 0)) Emit(insn); // t5 = out_len
        foreach (uint insn in RiscVEncoder.Li(Reg.T6, 0)) Emit(insn); // t6 = source index i

        // ── Main loop ──
        int mainLoop = m_instructions.Count;

        // if i >= text_len → done
        Emit(RiscVEncoder.Ld(Reg.T0, Reg.Sp, 24));  // text_len
        int doneCheck = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // patched: bge t6, t0 → done

        // Check if old_len == 0 → skip match (prevent infinite loop)
        Emit(RiscVEncoder.Ld(Reg.T1, Reg.Sp, 32));  // old_len
        int skipMatch = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // patched: beqz t1 → no_match

        // Check if i + old_len > text_len → can't match
        Emit(RiscVEncoder.Add(Reg.T2, Reg.T6, Reg.T1));  // t2 = i + old_len
        int cantMatch = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // patched: bgt t2, t0 → no_match

        // Compare text[i..i+old_len] with old
        // t3 = compare index j (0..old_len-1)
        foreach (uint insn in RiscVEncoder.Li(Reg.T3, 0)) Emit(insn);
        Emit(RiscVEncoder.Ld(Reg.A0, Reg.Sp, 0));   // text_ptr
        Emit(RiscVEncoder.Ld(Reg.A1, Reg.Sp, 8));   // old_ptr

        int cmpLoop = m_instructions.Count;
        // if j >= old_len → full match!
        int matchFound = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // patched: bge t3, t1 → match

        // Load text[i+j+8] and old[j+8]
        Emit(RiscVEncoder.Add(Reg.T2, Reg.T6, Reg.T3));   // t2 = i + j
        Emit(RiscVEncoder.Add(Reg.T2, Reg.A0, Reg.T2));   // t2 = text + i + j
        Emit(RiscVEncoder.Lbu(Reg.T2, Reg.T2, 8));        // t2 = text_data[i+j]
        Emit(RiscVEncoder.Add(Reg.A2, Reg.A1, Reg.T3));   // a2 = old + j
        Emit(RiscVEncoder.Lbu(Reg.A2, Reg.A2, 8));        // a2 = old_data[j]

        int mismatch = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // patched: bne t2, a2 → no_match

        Emit(RiscVEncoder.Addi(Reg.T3, Reg.T3, 1));
        Emit(RiscVEncoder.J((cmpLoop - m_instructions.Count) * 4));

        // ── Match found: copy new_str bytes to output ──
        int matchLabel = m_instructions.Count;
        m_instructions[matchFound] = RiscVEncoder.Bge(Reg.T3, Reg.T1, (matchLabel - matchFound) * 4);

        Emit(RiscVEncoder.Ld(Reg.A2, Reg.Sp, 16));   // new_ptr
        Emit(RiscVEncoder.Ld(Reg.T2, Reg.Sp, 40));   // new_len
        foreach (uint insn in RiscVEncoder.Li(Reg.T3, 0)) Emit(insn);  // j = 0
        Emit(RiscVEncoder.Ld(Reg.T4, Reg.Sp, 48));   // result base

        int copyNewLoop = m_instructions.Count;
        int copyNewExit = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // patched: bge t3, t2 → done copying

        Emit(RiscVEncoder.Add(Reg.A3, Reg.A2, Reg.T3));
        Emit(RiscVEncoder.Lbu(Reg.A3, Reg.A3, 8));        // new_data[j]
        Emit(RiscVEncoder.Add(Reg.A4, Reg.T4, Reg.T5));
        Emit(RiscVEncoder.Sb(Reg.A4, Reg.A3, 8));         // out[out_len+j] (skip length prefix)
        Emit(RiscVEncoder.Addi(Reg.T5, Reg.T5, 1));
        Emit(RiscVEncoder.Addi(Reg.T3, Reg.T3, 1));
        Emit(RiscVEncoder.J((copyNewLoop - m_instructions.Count) * 4));
        int copyNewTarget = m_instructions.Count;
        m_instructions[copyNewExit] = RiscVEncoder.Bge(Reg.T3, Reg.T2, (copyNewTarget - copyNewExit) * 4);

        // Advance source index by old_len
        Emit(RiscVEncoder.Ld(Reg.T1, Reg.Sp, 32));   // old_len
        Emit(RiscVEncoder.Add(Reg.T6, Reg.T6, Reg.T1));
        Emit(RiscVEncoder.J((mainLoop - m_instructions.Count) * 4));

        // ── No match: copy one byte ──
        int noMatchLabel = m_instructions.Count;
        m_instructions[skipMatch] = RiscVEncoder.Beq(Reg.T1, Reg.Zero, (noMatchLabel - skipMatch) * 4);
        m_instructions[cantMatch] = RiscVEncoder.Blt(Reg.T0, Reg.T2, (noMatchLabel - cantMatch) * 4);
        m_instructions[mismatch] = RiscVEncoder.Bne(Reg.T2, Reg.A2, (noMatchLabel - mismatch) * 4);

        Emit(RiscVEncoder.Ld(Reg.A0, Reg.Sp, 0));    // text_ptr
        Emit(RiscVEncoder.Add(Reg.T0, Reg.A0, Reg.T6));
        Emit(RiscVEncoder.Lbu(Reg.T0, Reg.T0, 8));   // text_data[i]
        Emit(RiscVEncoder.Ld(Reg.T4, Reg.Sp, 48));   // result base
        Emit(RiscVEncoder.Add(Reg.T1, Reg.T4, Reg.T5));
        Emit(RiscVEncoder.Sb(Reg.T1, Reg.T0, 8));    // out[out_len]
        Emit(RiscVEncoder.Addi(Reg.T5, Reg.T5, 1));
        Emit(RiscVEncoder.Addi(Reg.T6, Reg.T6, 1));
        Emit(RiscVEncoder.J((mainLoop - m_instructions.Count) * 4));

        // ── Done ──
        int doneLabel = m_instructions.Count;
        m_instructions[doneCheck] = RiscVEncoder.Bge(Reg.T6, Reg.T0, (doneLabel - doneCheck) * 4);

        // Store length and finalize
        Emit(RiscVEncoder.Ld(Reg.T4, Reg.Sp, 48));   // result base
        Emit(RiscVEncoder.Sd(Reg.T4, Reg.T5, 0));    // store length

        // Bump heap: s1 = result + align8(8 + out_len)
        Emit(RiscVEncoder.Addi(Reg.A0, Reg.T5, 15));
        Emit(RiscVEncoder.Andi(Reg.A0, Reg.A0, -8));
        Emit(RiscVEncoder.Add(Reg.S1, Reg.T4, Reg.A0));

        Emit(RiscVEncoder.Mv(Reg.A0, Reg.T4));       // return result
        Emit(RiscVEncoder.Ld(Reg.Ra, Reg.Sp, 56));
        Emit(RiscVEncoder.Addi(Reg.Sp, Reg.Sp, 64));
        Emit(RiscVEncoder.Ret());
    }

    // ── Syscalls / IO ────────────────────────────────────────────

    void EmitSyscallWrite()
    {
        if (m_target == RiscVTarget.BareMetal)
        {
            EmitUartWriteBuffer();
            return;
        }
        foreach (uint insn in RiscVEncoder.Li(Reg.A7, 64)) Emit(insn);  // SYS_write
        foreach (uint insn in RiscVEncoder.Li(Reg.A0, 1)) Emit(insn);   // stdout
        Emit(RiscVEncoder.Ecall());
    }

    void EmitSyscallExit(uint codeReg)
    {
        if (m_target == RiscVTarget.BareMetal)
        {
            // Bare metal: infinite loop (halt)
            Emit(RiscVEncoder.J(0)); // j .  (spin forever)
            return;
        }
        Emit(RiscVEncoder.Mv(Reg.A0, codeReg));
        foreach (uint insn in RiscVEncoder.Li(Reg.A7, 93)) Emit(insn);  // SYS_exit
        Emit(RiscVEncoder.Ecall());
    }

    // ── UART input (bare metal) ──────────────────────────────────

    void EmitBareMetalReadSerialHelper()
    {
        // __bare_metal_read_serial: → a0=length-prefixed string on heap
        // Polls UART for bytes until EOT (0x04) or null (0x00).
        // Converts Unicode→CCE via lookup table in rodata.
        m_functionOffsets["__bare_metal_read_serial"] = m_instructions.Count;

        Emit(RiscVEncoder.Addi(Reg.Sp, Reg.Sp, -32));
        Emit(RiscVEncoder.Sd(Reg.Sp, Reg.Ra, 24));
        Emit(RiscVEncoder.Sd(Reg.Sp, Reg.S2, 16));
        Emit(RiscVEncoder.Sd(Reg.Sp, Reg.S3, 8));
        Emit(RiscVEncoder.Sd(Reg.Sp, Reg.S4, 0));

        // s2 = output buffer start (current heap ptr)
        Emit(RiscVEncoder.Mv(Reg.S2, Reg.S1));
        // s3 = byte count
        foreach (uint insn in RiscVEncoder.Li(Reg.S3, 0)) Emit(insn);
        // s4 = Unicode→CCE table address
        m_rodataFixups.Add(new RodataFixup(m_instructions.Count, Reg.S4, m_unicodeToCceTableOffset));
        foreach (uint insn in RiscVEncoder.Li(Reg.S4, 0)) Emit(insn); // patched

        // t0 = UART base
        foreach (uint insn in RiscVEncoder.Li(Reg.T0, UartBase)) Emit(insn);

        // Poll loop: wait for LSR bit 0 (data ready)
        int pollLoop = m_instructions.Count;
        Emit(RiscVEncoder.Lbu(Reg.T1, Reg.T0, 5));  // LSR at UART+5
        Emit(RiscVEncoder.Andi(Reg.T1, Reg.T1, 1));  // bit 0 = data ready
        int dataReady = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // patched: bnez t1 → read byte
        Emit(RiscVEncoder.J((pollLoop - m_instructions.Count) * 4)); // spin
        int readByte = m_instructions.Count;
        m_instructions[dataReady] = RiscVEncoder.Bne(Reg.T1, Reg.Zero, (readByte - dataReady) * 4);

        // Read byte from UART RBR (offset 0)
        Emit(RiscVEncoder.Lbu(Reg.T1, Reg.T0, 0));   // t1 = received byte

        // Check for EOT (0x04)
        foreach (uint insn in RiscVEncoder.Li(Reg.T2, 0x04)) Emit(insn);
        int gotEot = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // patched: beq t1, t2 → done

        // Check for null (0x00)
        int gotNull = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // patched: beqz t1 → done

        // Convert Unicode→CCE: table[byte]
        Emit(RiscVEncoder.Add(Reg.T2, Reg.S4, Reg.T1));
        Emit(RiscVEncoder.Lbu(Reg.T1, Reg.T2, 0));    // t1 = CCE byte

        // Store to output buffer: buf[8 + count]
        Emit(RiscVEncoder.Add(Reg.T2, Reg.S2, Reg.S3));
        Emit(RiscVEncoder.Sb(Reg.T2, Reg.T1, 8));      // buf[8+count] = CCE byte

        // count++
        Emit(RiscVEncoder.Addi(Reg.S3, Reg.S3, 1));
        Emit(RiscVEncoder.J((pollLoop - m_instructions.Count) * 4)); // loop

        // Done: store length, bump heap, return
        int doneLabel = m_instructions.Count;
        m_instructions[gotEot] = RiscVEncoder.Beq(Reg.T1, Reg.T2, (doneLabel - gotEot) * 4);
        m_instructions[gotNull] = RiscVEncoder.Beq(Reg.T1, Reg.Zero, (doneLabel - gotNull) * 4);

        Emit(RiscVEncoder.Sd(Reg.S2, Reg.S3, 0));      // [buf+0] = length
        // Bump heap: s1 = s2 + align8(8 + count)
        Emit(RiscVEncoder.Addi(Reg.T1, Reg.S3, 15));
        Emit(RiscVEncoder.Andi(Reg.T1, Reg.T1, -8));
        Emit(RiscVEncoder.Add(Reg.S1, Reg.S2, Reg.T1));
        Emit(RiscVEncoder.Mv(Reg.A0, Reg.S2));          // return buffer ptr

        Emit(RiscVEncoder.Ld(Reg.S4, Reg.Sp, 0));
        Emit(RiscVEncoder.Ld(Reg.S3, Reg.Sp, 8));
        Emit(RiscVEncoder.Ld(Reg.S2, Reg.Sp, 16));
        Emit(RiscVEncoder.Ld(Reg.Ra, Reg.Sp, 24));
        Emit(RiscVEncoder.Addi(Reg.Sp, Reg.Sp, 32));
        Emit(RiscVEncoder.Ret());
    }

    // ── UART output (bare metal) ─────────────────────────────────

    void EmitUartWriteBuffer()
    {
        // On entry: a1 = buffer pointer, a2 = length
        // Uses t0 = UART base, t1 = end pointer, t2 = current byte
        foreach (uint insn in RiscVEncoder.Li(Reg.T0, UartBase)) Emit(insn);
        Emit(RiscVEncoder.Add(Reg.T1, Reg.A1, Reg.A2)); // t1 = buf + len

        int loopStart = m_instructions.Count;
        // if (a1 >= t1) break
        int exitIndex = m_instructions.Count;
        Emit(RiscVEncoder.Nop()); // patched: bge a1, t1 → exit

        // load byte, store to UART
        Emit(RiscVEncoder.Lbu(Reg.T2, Reg.A1, 0));
        Emit(RiscVEncoder.Sb(Reg.T0, Reg.T2, 0)); // UART THR at offset 0
        Emit(RiscVEncoder.Addi(Reg.A1, Reg.A1, 1));
        Emit(RiscVEncoder.J((loopStart - m_instructions.Count) * 4));

        int exitTarget = m_instructions.Count;
        m_instructions[exitIndex] = RiscVEncoder.Bge(Reg.A1, Reg.T1,
            (exitTarget - exitIndex) * 4);
    }

    // ── _start ───────────────────────────────────────────────────

    void EmitStart(IRModule module)
    {
        m_functionOffsets["__start"] = m_instructions.Count;

        if (m_target == RiscVTarget.BareMetal)
        {
            // Stack at 16MB above code base (plenty for deep recursion)
            foreach (uint insn in RiscVEncoder.Li(Reg.Sp, 0x81000000L)) Emit(insn);
            // Two-space heap at 32MB above code base (2MB working + 2MB result)
            foreach (uint insn in RiscVEncoder.Li(Reg.S1, 0x82000000L)) Emit(insn);
            foreach (uint insn in RiscVEncoder.Li(ResultReg, 0x82000000L + 0x200000L)) Emit(insn);
            Emit(RiscVEncoder.Mv(ResultBaseReg, ResultReg)); // save result-space base
        }
        else
        {
            // Linux: two-space heap via brk syscall (214)
            foreach (uint insn in RiscVEncoder.Li(Reg.A0, 0)) Emit(insn);
            foreach (uint insn in RiscVEncoder.Li(Reg.A7, 214)) Emit(insn);
            Emit(RiscVEncoder.Ecall());
            Emit(RiscVEncoder.Mv(Reg.S1, Reg.A0)); // working space starts at brk base

            // Grow by 128MB: 64MB working + 64MB result
            foreach (uint insn in RiscVEncoder.Li(Reg.T0, 128L * 1024 * 1024)) Emit(insn);
            Emit(RiscVEncoder.Add(Reg.A0, Reg.S1, Reg.T0));
            foreach (uint insn in RiscVEncoder.Li(Reg.A7, 214)) Emit(insn);
            Emit(RiscVEncoder.Ecall());

            // Result space starts at brk_base + 64MB
            Emit(RiscVEncoder.Mv(ResultReg, Reg.S1));
            foreach (uint insn in RiscVEncoder.Li(Reg.T0, 64L * 1024 * 1024)) Emit(insn);
            Emit(RiscVEncoder.Add(ResultReg, ResultReg, Reg.T0));
            Emit(RiscVEncoder.Mv(ResultBaseReg, ResultReg)); // save result-space base
        }

        IRDefinition? mainDef = null;
        foreach (IRDefinition def in module.Definitions)
        {
            if (def.Name == "main") { mainDef = def; break; }
        }

        if (mainDef is null)
        {
            EmitSyscallExit(Reg.Zero);
            return;
        }

        EmitCallTo("main");

        CodexType returnType = ComputeReturnType(mainDef.Type, mainDef.Parameters.Length);
        switch (returnType)
        {
            case IntegerType: EmitPrintI64(Reg.A0); break;
            case BooleanType: EmitPrintBool(Reg.A0); break;
            case TextType:    EmitPrintText(Reg.A0); break;
        }

        EmitSyscallExit(Reg.Zero);
    }

    // ── Call patching ────────────────────────────────────────────

    void EmitCallTo(string targetName)
    {
        m_callPatches.Add((m_instructions.Count, targetName));
        Emit(RiscVEncoder.Nop());
    }

    void PatchCalls()
    {
        foreach ((int insnIndex, string target) in m_callPatches)
        {
            if (m_functionOffsets.TryGetValue(target, out int targetIndex))
                m_instructions[insnIndex] = RiscVEncoder.Call((targetIndex - insnIndex) * 4);
            else
                Console.Error.WriteLine($"RISCV WARNING: unresolved call to '{target}' at instruction {insnIndex}");
        }

        int textSizeBytes = m_instructions.Count * 4;
        ulong rodataVaddr = ElfWriter.ComputeRodataVaddr(textSizeBytes, m_target);

        Console.Error.WriteLine($"RISCV: rodata vaddr=0x{rodataVaddr:X}, {m_rodataFixups.Count} fixup(s), text={textSizeBytes}B");
        foreach (RodataFixup fixup in m_rodataFixups)
        {
            long addr = (long)(rodataVaddr + (ulong)fixup.RodataOffset);
            // Bare metal addresses like 0x80000xxx are 32-bit but look >int.MaxValue as long.
            // Cast to int (sign-extending) so Li uses the efficient lui+addi encoding.
            if (addr > int.MaxValue && addr <= uint.MaxValue)
                addr = (int)(uint)addr;
            uint[] insns = RiscVEncoder.Li(fixup.Register, addr);
            Console.Error.WriteLine($"  fixup: insn[{fixup.InstructionIndex}] reg=x{fixup.Register} rodata+{fixup.RodataOffset} → 0x{(rodataVaddr + (ulong)fixup.RodataOffset):X} ({insns.Length} insns)");
            for (int i = 0; i < 2 && i < insns.Length; i++)
                m_instructions[fixup.InstructionIndex + i] = insns[i];
        }

        // Patch function address fixups (function pointers)
        ulong textVaddr = m_target == RiscVTarget.BareMetal
            ? 0x80000000UL
            : 0x10000UL + (ulong)ElfWriter.ComputeTextFileOffset(m_target);
        foreach (FuncAddrFixup fixup in m_funcAddrFixups)
        {
            if (m_functionOffsets.TryGetValue(fixup.FunctionName, out int funcIndex))
            {
                long funcAddr = (long)(textVaddr + (ulong)(funcIndex * 4));
                if (funcAddr > int.MaxValue && funcAddr <= uint.MaxValue)
                    funcAddr = (int)(uint)funcAddr;
                uint[] insns = RiscVEncoder.Li(fixup.Register, funcAddr);
                for (int i = 0; i < 2 && i < insns.Length; i++)
                    m_instructions[fixup.InstructionIndex + i] = insns[i];
            }
        }
    }

    // ── Register allocation ──────────────────────────────────────

    uint AllocTemp()
    {
        uint reg = m_nextTemp;
        m_nextTemp++;
        if (m_nextTemp > Reg.T6) m_nextTemp = Reg.T3;
        return reg;
    }

    // Virtual register numbers >31 indicate stack-spilled locals.
    // Slot 0 is at sp+96, slot 1 at sp+104, etc. (above the base frame).
    const uint SpillBase = 32;

    uint AllocLocal()
    {
        if (m_nextLocal <= Reg.S9)
        {
            uint reg = m_nextLocal;
            m_nextLocal++;
            return reg;
        }
        // Spill to stack: return virtual register number
        uint slot = SpillBase + (uint)m_spillCount;
        m_spillCount++;
        return slot;
    }

    int SpillOffset(uint virtualReg) => 80 + ((int)(virtualReg - SpillBase)) * 8;

    // Store a value into a local (register or stack slot)
    void StoreLocal(uint local, uint valueReg)
    {
        if (local < SpillBase)
            Emit(RiscVEncoder.Mv(local, valueReg));
        else
            EmitSpillStore(valueReg, local);
    }

    uint m_loadLocalToggle;

    // Load a local into a scratch register for use.
    // Alternates T0/T1 (not in AllocTemp rotation) to allow two
    // simultaneous spill loads (e.g., both operands of a binary op).
    uint LoadLocal(uint local)
    {
        if (local < SpillBase)
            return local;
        uint scratch = (m_loadLocalToggle++ % 2 == 0) ? Reg.T0 : Reg.T1;
        EmitSpillLoad(scratch, local);
        return scratch;
    }

    // Emit sd valueReg, SpillOffset(slot)(sp) — handles offsets > 2047
    void EmitSpillStore(uint valueReg, uint slot)
    {
        int offset = SpillOffset(slot);
        if (offset >= -2048 && offset <= 2047)
        {
            Emit(RiscVEncoder.Sd(Reg.Sp, valueReg, offset));
        }
        else
        {
            // Use T2 as scratch (not in T0/T1 LoadLocal rotation or T3-T6 AllocTemp rotation)
            foreach (uint insn in RiscVEncoder.Li(Reg.T2, offset)) Emit(insn);
            Emit(RiscVEncoder.Add(Reg.T2, Reg.Sp, Reg.T2));
            Emit(RiscVEncoder.Sd(Reg.T2, valueReg, 0));
        }
    }

    // Emit ld rd, SpillOffset(slot)(sp) — handles offsets > 2047
    void EmitSpillLoad(uint rd, uint slot)
    {
        int offset = SpillOffset(slot);
        if (offset >= -2048 && offset <= 2047)
        {
            Emit(RiscVEncoder.Ld(rd, Reg.Sp, offset));
        }
        else
        {
            // For T0/T1 loads: use T2 as scratch for address computation
            foreach (uint insn in RiscVEncoder.Li(Reg.T2, offset)) Emit(insn);
            Emit(RiscVEncoder.Add(Reg.T2, Reg.Sp, Reg.T2));
            Emit(RiscVEncoder.Ld(rd, Reg.T2, 0));
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
