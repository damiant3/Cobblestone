using System.Text;
using Codex.Core;
using Codex.IR;
using Codex.Types;

namespace Codex.Emit.X86_64;

sealed class X86_64CodeGen(X86_64Target target = X86_64Target.LinuxUser)
{
    readonly X86_64Target m_target = target;
    readonly List<byte> m_text = [];
    readonly List<byte> m_rodata = [];
    readonly Dictionary<string, int> m_functionOffsets = [];
    readonly List<(int PatchOffset, string Target)> m_callPatches = [];
    readonly List<RodataFixup> m_rodataFixups = [];
    readonly List<FuncAddrFixup> m_funcAddrFixups = [];
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
    const int SpillBase = 32; // virtual register numbers for spilled locals

    int m_nextTemp;
    int m_nextLocal;
    int m_spillCount;
    int m_loadLocalToggle;
    Dictionary<string, int> m_locals = [];
    string m_currentFunction = "";

    readonly record struct RodataFixup(int PatchOffset, int RodataOffset);
    readonly record struct FuncAddrFixup(int PatchOffset, byte Rd, string FuncName);
    int m_cceToUnicodeTableOffset = -1; // rodata offset for 128-byte CCE→Unicode lookup
    int m_unicodeToCceTableOffset = -1; // rodata offset for 256-byte Unicode→CCE lookup (input boundary)

    public void EmitModule(IRModule module)
    {
        // Bare metal: emit multiboot header + 32→64 trampoline at byte 0
        if (m_target == X86_64Target.BareMetal)
            EmitMultibootHeader();

        m_typeDefs = module.TypeDefinitions;
        m_escapeHelperNames["text"] = "__escape_text";

        // Emit CCE→Unicode lookup table (128 bytes) into .rodata.
        // Used by print helpers to convert CCE bytes back to Unicode for output.
        m_cceToUnicodeTableOffset = m_rodata.Count;
        for (int i = 0; i < 128; i++)
            m_rodata.Add((byte)CceTable.ToUnicode[i]);
        while (m_rodata.Count % 8 != 0) m_rodata.Add(0);

        // Emit Unicode→CCE lookup table (256 bytes) into .rodata.
        // Used by serial input to convert incoming Unicode bytes to CCE encoding.
        m_unicodeToCceTableOffset = m_rodata.Count;
        for (int i = 0; i < 256; i++)
            m_rodata.Add((byte)(CceTable.FromUnicode.TryGetValue(i, out int cce) ? cce : CceTable.ReplacementCce));
        while (m_rodata.Count % 8 != 0) m_rodata.Add(0);

        EmitRuntimeHelpers();

        foreach (IRDefinition def in module.Definitions)
            EmitFunction(def);

        EmitEscapeCopyHelpers();

        // Bare metal: emit ISR stubs, syscall handler, process 1 entry
        if (m_target == X86_64Target.BareMetal)
        {
            EmitIsrStubsAndIdt();
            EmitSyscallHandler();
            EmitProcess1Entry();
        }
        EmitStart(module);
        PatchCalls();
        PatchRodataRefs();
    }

    public byte[] BuildElf()
    {
        byte[] text = m_text.ToArray();
        byte[] rodata = m_rodata.ToArray();

        if (m_target == X86_64Target.BareMetal)
        {
            // 32-bit ELF with PVH note for QEMU direct boot.
            // QEMU requires ELFCLASS32 and jumps to the PVH entry in
            // 32-bit protected mode. Our trampoline sets up long mode.
            return ElfWriter32.WriteExecutable(text, rodata, 12);
        }

        if (m_functionOffsets.TryGetValue("__start", out int startOffset))
            return ElfWriterX86_64.WriteExecutable(text, rodata, (ulong)startOffset);

        return ElfWriterX86_64.WriteExecutable(text, rodata, 0);
    }

    public byte[] BuildFlatBinary()
    {
        // Bare metal flat binary: text + rodata concatenated.
        // Multiboot header is at the start of m_text (emitted by EmitStart).
        // Rodata is appended after text, aligned to 8 bytes.
        List<byte> binary = new(m_text);
        while (binary.Count % 8 != 0)
            binary.Add(0);
        int rodataOffset = binary.Count;
        binary.AddRange(m_rodata);

        // Patch rodata references to use flat binary offsets
        // (in ELF mode, rodata is at a separate vaddr; in flat mode, it's inline)
        foreach (RodataFixup fixup in m_rodataFixups)
        {
            long addr = rodataOffset + fixup.RodataOffset;
            // MovRI64 is 10 bytes: REX.W + B8+rd + imm64. The imm64 starts at PatchOffset.
            binary[fixup.PatchOffset] = (byte)(addr & 0xFF);
            binary[fixup.PatchOffset + 1] = (byte)((addr >> 8) & 0xFF);
            binary[fixup.PatchOffset + 2] = (byte)((addr >> 16) & 0xFF);
            binary[fixup.PatchOffset + 3] = (byte)((addr >> 24) & 0xFF);
            binary[fixup.PatchOffset + 4] = (byte)((addr >> 32) & 0xFF);
            binary[fixup.PatchOffset + 5] = (byte)((addr >> 40) & 0xFF);
            binary[fixup.PatchOffset + 6] = (byte)((addr >> 48) & 0xFF);
            binary[fixup.PatchOffset + 7] = (byte)((addr >> 56) & 0xFF);
        }

        return binary.ToArray();
    }

    // ── Function emission ────────────────────────────────────────

    // ── Tail Call Optimization ─────────────────────────────────

    bool m_inTCOFunction;
    bool m_inTailPosition;
    int m_tcoLoopTop;
    int[] m_tcoParamLocals = [];

    static bool ShouldTCO(IRDefinition def)
    {
        return def.Parameters.Length > 0 && HasTailCall(def.Body, def.Name);
    }

    static bool HasTailCall(IRExpr expr, string funcName)
    {
        return expr switch
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
    }

    static bool IsSelfCall(IRExpr expr, string funcName)
    {
        IRExpr current = expr;
        while (current is IRApply app)
            current = app.Function;
        return current is IRName name && name.Name == funcName;
    }

    int[] m_tcoTempLocals = []; // pre-allocated locals for tail call arg temps
    int m_tcoSavedNextLocal;
    int m_tcoSavedNextTemp;

    void EmitTailCall(IRApply app)
    {
        // Collect all arguments
        List<IRExpr> args = [];
        IRExpr current = app;
        while (current is IRApply a)
        {
            args.Insert(0, a.Argument);
            current = a.Function;
        }

        // Reset allocators so iterations don't grow the stack
        m_nextLocal = m_tcoSavedNextLocal;
        m_nextTemp = m_tcoSavedNextTemp;

        // Evaluate all args into PRE-ALLOCATED temps (avoid growing stack)
        for (int i = 0; i < args.Count && i < m_tcoTempLocals.Length; i++)
        {
            bool savedTail = m_inTailPosition;
            m_inTailPosition = false; // arg evaluation is NOT tail position
            byte r = EmitExpr(args[i]);
            m_inTailPosition = savedTail;
            StoreLocal(m_tcoTempLocals[i], r);
        }

        // Reassign params from temps
        for (int i = 0; i < args.Count && i < m_tcoParamLocals.Length; i++)
        {
            byte val = LoadLocal(m_tcoTempLocals[i]);
            StoreLocal(m_tcoParamLocals[i], val);
        }

        // Jump to loop top
        X86_64Encoder.Jmp(m_text, m_tcoLoopTop - (m_text.Count + 5));
    }

    void EmitFunction(IRDefinition def)
    {
        m_functionOffsets[def.Name] = m_text.Count;
        m_currentFunction = def.Name;
        m_nextTemp = 0;
        m_nextLocal = 0;
        m_spillCount = 0;
        m_loadLocalToggle = 0;
        m_locals = [];
        m_inTCOFunction = ShouldTCO(def);

        // Prologue
        X86_64Encoder.PushR(m_text, Reg.RBP);
        X86_64Encoder.MovRR(m_text, Reg.RBP, Reg.RSP);

        foreach (byte reg in LocalRegs)
            X86_64Encoder.PushR(m_text, reg);

        int frameSizePatchOffset = m_text.Count;
        EmitSubRspImm32(0);

        // Bind parameters
        m_tcoParamLocals = new int[def.Parameters.Length];
        for (int i = 0; i < def.Parameters.Length; i++)
        {
            int local = AllocLocal();
            if (i < Reg.ArgRegs.Length)
            {
                StoreLocal(local, Reg.ArgRegs[i]);
            }
            else
            {
                int stackOffset = 16 + (i - Reg.ArgRegs.Length) * 8;
                byte tmp = AllocTemp();
                X86_64Encoder.MovLoad(m_text, tmp, Reg.RBP, stackOffset);
                StoreLocal(local, tmp);
            }
            m_locals[def.Parameters[i].Name] = local;
            m_tcoParamLocals[i] = local;
        }

        // TCO: pre-allocate temp locals for tail call args and record loop top
        if (m_inTCOFunction)
        {
            m_tcoTempLocals = new int[def.Parameters.Length];
            for (int i = 0; i < def.Parameters.Length; i++)
                m_tcoTempLocals[i] = AllocLocal();
        }
        m_tcoLoopTop = m_text.Count;
        m_tcoSavedNextLocal = m_nextLocal;   // save for reset on each iteration
        m_tcoSavedNextTemp = m_nextTemp;
        m_inTailPosition = m_inTCOFunction;

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
            // CCE-encode: each character becomes one CCE byte (Tier 0).
            // This matches the C# emitter's CCE-native text model — strings
            // contain CCE bytes, Unicode only at I/O boundaries.
            string cceEncoded = CceTable.Encode(value);
            byte[] cceBytes = new byte[cceEncoded.Length];
            for (int i = 0; i < cceEncoded.Length; i++)
                cceBytes[i] = (byte)cceEncoded[i];
            // Length-prefixed: 8-byte i64 length + CCE data, 8-byte aligned
            m_rodata.AddRange(BitConverter.GetBytes((long)cceBytes.Length));
            m_rodata.AddRange(cceBytes);
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
        if (m_locals.TryGetValue(name.Name, out int local))
            return LoadLocal(local);

        // Try zero-arg builtins
        byte builtinResult = TryEmitBuiltin(name.Name, []);
        if (builtinResult != byte.MaxValue)
            return builtinResult;

        // Function used as a value — wrap as 0-capture closure
        if (name.Type is FunctionType)
            return EmitPartialApplication(name.Name, []);

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
                // Allocate [tag:8] on heap
                int nullaryPtr = AllocLocal();
                byte nullaryTmp = AllocTemp();
                X86_64Encoder.MovRR(m_text, nullaryTmp, HeapReg);
                StoreLocal(nullaryPtr, nullaryTmp);
                X86_64Encoder.AddRI(m_text, HeapReg, 8);
                byte tagReg = AllocTemp();
                X86_64Encoder.Li(m_text, tagReg, tag);
                X86_64Encoder.MovStore(m_text, LoadLocal(nullaryPtr), tagReg, 0);
                return LoadLocal(nullaryPtr);
            }
        }

        // Top-level constant or zero-arg function — call it
        EmitCallTo(name.Name);
        byte rd = AllocTemp();
        X86_64Encoder.MovRR(m_text, rd, Reg.RAX);
        return rd;
    }

    // ── Binary operations ────────────────────────────────────────

    byte EmitBinary(IRBinary bin)
    {
        byte left = EmitExpr(bin.Left);
        int savedLeft = AllocLocal();
        StoreLocal(savedLeft, left);

        byte right = EmitExpr(bin.Right);
        int savedRight = AllocLocal();
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
                X86_64Encoder.MovRR(m_text, Reg.RDI, lReg);
                X86_64Encoder.MovRR(m_text, Reg.RSI, rReg);
                EmitCallTo("__ipow");
                X86_64Encoder.MovRR(m_text, rd, Reg.RAX);
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
            case IRBinaryOp.And:
                X86_64Encoder.MovRR(m_text, rd, lReg);
                X86_64Encoder.AndRR(m_text, rd, rReg);
                break;
            case IRBinaryOp.Or:
                // Boolean OR: (lReg | rReg) != 0
                X86_64Encoder.MovRR(m_text, rd, lReg);
                X86_64Encoder.AddRR(m_text, rd, rReg);
                X86_64Encoder.CmpRI(m_text, rd, 0);
                X86_64Encoder.Setcc(m_text, X86_64Encoder.CC_NE, rd);
                X86_64Encoder.MovzxByteSelf(m_text, rd);
                break;
            case IRBinaryOp.AppendText:
                X86_64Encoder.MovRR(m_text, Reg.RDI, lReg);
                X86_64Encoder.MovRR(m_text, Reg.RSI, rReg);
                EmitCallTo("__str_concat");
                byte concatResult = AllocTemp();
                X86_64Encoder.MovRR(m_text, concatResult, Reg.RAX);
                return concatResult;
            case IRBinaryOp.AppendList:
                X86_64Encoder.MovRR(m_text, Reg.RDI, lReg);
                X86_64Encoder.MovRR(m_text, Reg.RSI, rReg);
                EmitCallTo("__list_append");
                byte appendResult = AllocTemp();
                X86_64Encoder.MovRR(m_text, appendResult, Reg.RAX);
                return appendResult;
            case IRBinaryOp.ConsList:
                X86_64Encoder.MovRR(m_text, Reg.RDI, lReg);
                X86_64Encoder.MovRR(m_text, Reg.RSI, rReg);
                EmitCallTo("__list_cons");
                byte consResult = AllocTemp();
                X86_64Encoder.MovRR(m_text, consResult, Reg.RAX);
                return consResult;
            default:
                Console.Error.WriteLine($"X86_64 WARNING: unhandled binary op {bin.Op}");
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
        bool savedTail = m_inTailPosition;
        m_inTailPosition = false; // condition is NOT in tail position
        byte cond = EmitExpr(ifExpr.Condition);
        X86_64Encoder.TestRR(m_text, cond, cond);

        int jeFalseOffset = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_E, 0);

        // Then branch IS in tail position (if outer was)
        m_inTailPosition = savedTail;
        byte thenResult = EmitExpr(ifExpr.Then);
        int resultLocal = AllocLocal();
        StoreLocal(resultLocal, thenResult);

        // jmp end
        int jmpEndOffset = m_text.Count;
        X86_64Encoder.Jmp(m_text, 0); // patched

        // Else branch IS in tail position (if outer was)
        int elseStart = m_text.Count;
        PatchJcc(jeFalseOffset, elseStart);

        m_inTailPosition = savedTail;
        byte elseResult = EmitExpr(ifExpr.Else);
        StoreLocal(resultLocal, elseResult);

        int endOffset = m_text.Count;
        PatchJmp(jmpEndOffset, endOffset);

        return LoadLocal(resultLocal);
    }

    byte EmitLet(IRLet letExpr)
    {
        bool savedTail = m_inTailPosition;
        m_inTailPosition = false; // value is NOT in tail position
        byte value = EmitExpr(letExpr.Value);
        int local = AllocLocal();
        StoreLocal(local, value);
        m_locals[letExpr.Name] = local;
        m_inTailPosition = savedTail; // body IS in tail position
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
                    int savedReg = AllocLocal();
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
        // TCO: if in tail position of a TCO function, emit jump instead of call
        if (m_inTCOFunction && m_inTailPosition && IsSelfCall(apply, m_currentFunction))
        {
            EmitTailCall(apply);
            // Return a dummy register — the jmp means we never reach here,
            // but the caller expects a register value
            byte dummy = AllocTemp();
            X86_64Encoder.Li(m_text, dummy, 0);
            return dummy;
        }

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

        // Sub-expressions of a call (args, constructor fields, builtins) are NOT in tail position
        bool savedTailPos = m_inTailPosition;
        m_inTailPosition = false;

        // Try builtins first
        if (funcName is not null)
        {
            byte builtinResult = TryEmitBuiltin(funcName, args);
            if (builtinResult != byte.MaxValue)
                return builtinResult;

            // Sum type constructor: allocate [tag][field0][field1]... on heap
            SumType? sumType = apply.Type as SumType;
            if (sumType is null && apply.Type is ConstructedType ctApply)
                sumType = m_typeDefs[ctApply.Constructor.Value] as SumType;
            if (sumType is not null)
            {
                byte ctorResult = EmitConstructor(funcName, args, sumType);
                if (ctorResult != byte.MaxValue)
                    return ctorResult;
            }

            // Partial application: result is a function → create closure
            if (apply.Type is FunctionType && !m_locals.ContainsKey(funcName))
                return EmitPartialApplication(funcName, args);
        }

        // Evaluate and save args
        List<int> argLocals = [];
        foreach (IRExpr arg in args)
        {
            byte r = EmitExpr(arg);
            int saved = AllocLocal();
            StoreLocal(saved, r);
            argLocals.Add(saved);
        }

        // Push stack args first (7th+), then set up register args (1st-6th).
        // Stack args pushed in reverse order so callee sees them at [rbp+16], [rbp+24]...
        for (int i = argLocals.Count - 1; i >= Reg.ArgRegs.Length; i--)
        {
            byte loaded = LoadLocal(argLocals[i]);
            X86_64Encoder.PushR(m_text, loaded);
        }

        // Register args: two-phase push/pop to avoid R8/R9 spill/arg conflict
        int regArgCount = Math.Min(argLocals.Count, Reg.ArgRegs.Length);
        for (int i = 0; i < regArgCount; i++)
        {
            byte loaded = LoadLocal(argLocals[i]);
            X86_64Encoder.PushR(m_text, loaded);
        }
        for (int i = regArgCount - 1; i >= 0; i--)
            X86_64Encoder.PopR(m_text, Reg.ArgRegs[i]);

        if (funcName is not null)
        {
            if (m_locals.ContainsKey(funcName))
            {
                // Indirect call via closure: R11=closure, load code_ptr, call
                byte closureReg = LoadLocal(m_locals[funcName]);
                X86_64Encoder.MovRR(m_text, Reg.R11, closureReg);
                X86_64Encoder.MovLoad(m_text, Reg.RAX, Reg.R11, 0);
                // call rax (indirect call — 2 bytes: FF D0)
                m_text.Add(0xFF); m_text.Add(0xD0);
            }
            else
            {
                EmitCallTo(funcName);
            }
        }

        // Clean up stack args after call
        int stackArgCount = argLocals.Count - Reg.ArgRegs.Length;
        if (stackArgCount > 0)
            X86_64Encoder.AddRI(m_text, Reg.RSP, stackArgCount * 8);

        m_inTailPosition = savedTailPos;
        byte result = AllocTemp();
        X86_64Encoder.MovRR(m_text, result, Reg.RAX);
        return result;
    }

    byte EmitConstructor(string ctorName, List<IRExpr> args, SumType sumType)
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
        if (tag < 0) return byte.MaxValue;

        List<int> argLocals = [];
        foreach (IRExpr arg in args)
        {
            byte r = EmitExpr(arg);
            int saved = AllocLocal();
            StoreLocal(saved, r);
            argLocals.Add(saved);
        }

        int totalSize = (1 + args.Count) * 8;
        int ctorPtrLocal = AllocLocal();
        byte ctorTmp = AllocTemp();
        X86_64Encoder.MovRR(m_text, ctorTmp, HeapReg);
        StoreLocal(ctorPtrLocal, ctorTmp);
        X86_64Encoder.AddRI(m_text, HeapReg, totalSize);

        byte tagReg = AllocTemp();
        X86_64Encoder.Li(m_text, tagReg, tag);
        X86_64Encoder.MovStore(m_text, LoadLocal(ctorPtrLocal), tagReg, 0);

        for (int i = 0; i < argLocals.Count; i++)
        {
            byte val = LoadLocal(argLocals[i]);
            byte ptr = LoadLocal(ctorPtrLocal);
            X86_64Encoder.MovStore(m_text, ptr, val, 8 + i * 8);
        }

        return LoadLocal(ctorPtrLocal);
    }

    byte EmitPartialApplication(string funcName, List<IRExpr> capturedArgs)
    {
        // Evaluate and save captured args
        List<int> capLocals = [];
        foreach (IRExpr arg in capturedArgs)
        {
            byte r = EmitExpr(arg);
            int saved = AllocLocal();
            StoreLocal(saved, r);
            capLocals.Add(saved);
        }

        int numCaptures = capLocals.Count;
        string trampolineName = $"__tramp_{funcName}_{numCaptures}_{m_text.Count}";

        // Jump over trampoline code
        int jumpOverOffset = m_text.Count;
        X86_64Encoder.Jmp(m_text, 0); // patched

        m_functionOffsets[trampolineName] = m_text.Count;

        // Trampoline: R11 = closure pointer
        // Shift visible args right by numCaptures (backward to avoid clobbering)
        for (int i = Reg.ArgRegs.Length - 1; i >= 0; i--)
        {
            if (i + numCaptures < Reg.ArgRegs.Length)
                X86_64Encoder.MovRR(m_text, Reg.ArgRegs[i + numCaptures], Reg.ArgRegs[i]);
        }

        // Load captured args from closure into first N arg registers
        for (int i = 0; i < numCaptures && i < Reg.ArgRegs.Length; i++)
            X86_64Encoder.MovLoad(m_text, Reg.ArgRegs[i], Reg.R11, 8 + i * 8);

        // Tail-jump to the real function (load address, jmp via rax)
        EmitLoadFunctionAddress(Reg.RAX, funcName);
        m_text.Add(0xFF); m_text.Add(0xE0); // jmp rax (tail-call, no stack frame)

        // Patch jump-over
        int afterTrampoline = m_text.Count;
        PatchJmp(jumpOverOffset, afterTrampoline);

        // Allocate closure on heap: [trampoline_addr][cap_0][cap_1]...
        int closureSize = (1 + numCaptures) * 8;
        int ptrLocal = AllocLocal();
        byte tmp = AllocTemp();
        X86_64Encoder.MovRR(m_text, tmp, HeapReg);
        StoreLocal(ptrLocal, tmp);
        X86_64Encoder.AddRI(m_text, HeapReg, closureSize);

        // Store trampoline address
        EmitLoadFunctionAddress(Reg.RAX, trampolineName);
        X86_64Encoder.MovStore(m_text, LoadLocal(ptrLocal), Reg.RAX, 0);

        // Store captured args
        for (int i = 0; i < capLocals.Count; i++)
        {
            byte val = LoadLocal(capLocals[i]);
            X86_64Encoder.MovStore(m_text, LoadLocal(ptrLocal), val, 8 + i * 8);
        }

        return LoadLocal(ptrLocal);
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
        Dictionary<string, int> fieldMap = [];
        foreach ((string name, IRExpr value) in rec.Fields)
        {
            byte r = EmitExpr(value);
            int saved = AllocLocal();
            StoreLocal(saved, r);
            fieldMap[name] = saved;
        }

        RecordType? rt = rec.Type as RecordType;
        if (rt is null && rec.Type is ConstructedType ctRec)
            rt = m_typeDefs[ctRec.Constructor.Value] as RecordType;

        int fieldCount = rt?.Fields.Length ?? rec.Fields.Length;
        int totalSize = fieldCount * 8;
        int ptrLocal = AllocLocal();
        byte tmpPtr = AllocTemp();
        X86_64Encoder.MovRR(m_text, tmpPtr, HeapReg);
        StoreLocal(ptrLocal, tmpPtr);
        X86_64Encoder.AddRI(m_text, HeapReg, totalSize);

        if (rt is not null)
        {
            for (int i = 0; i < rt.Fields.Length; i++)
            {
                string fieldName = rt.Fields[i].FieldName.Value;
                if (fieldMap.TryGetValue(fieldName, out int saved))
                {
                    byte val = LoadLocal(saved);
                    byte ptr = LoadLocal(ptrLocal);
                    X86_64Encoder.MovStore(m_text, ptr, val, i * 8);
                }
                // Field not found in IR — this should not happen
                // (all record constructions include all fields)
            }
        }

        return LoadLocal(ptrLocal);
    }

    byte EmitFieldAccess(IRFieldAccess fa)
    {
        byte baseReg = EmitExpr(fa.Record);
        int fieldIndex = 0;

        RecordType? rt = fa.Record.Type as RecordType;
        if (rt is null && fa.Record.Type is ConstructedType ctFa)
            rt = m_typeDefs[ctFa.Constructor.Value] as RecordType;

        if (rt is not null)
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
        bool savedTail = m_inTailPosition;
        m_inTailPosition = false; // scrutinee is NOT tail position
        byte scrutReg = EmitExpr(match.Scrutinee);
        m_inTailPosition = savedTail; // branch bodies ARE tail position
        int savedScrut = AllocLocal();
        StoreLocal(savedScrut, scrutReg);

        int resultLocal = AllocLocal();

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
                    int local = AllocLocal();
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
                    SumType? matchSumType = ctorPat.Type as SumType;
                    if (matchSumType is null && ctorPat.Type is ConstructedType ctMatch)
                        matchSumType = m_typeDefs[ctMatch.Constructor.Value] as SumType;
                    if (matchSumType is SumType sumType)
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
                            int fieldLocal = AllocLocal();
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
        List<int> elemLocals = [];
        foreach (IRExpr elem in list.Elements)
        {
            byte r = EmitExpr(elem);
            int saved = AllocLocal();
            StoreLocal(saved, r);
            elemLocals.Add(saved);
        }

        int totalSize = (1 + list.Elements.Length) * 8;
        int listPtrLocal = AllocLocal();
        byte listTmp = AllocTemp();
        X86_64Encoder.MovRR(m_text, listTmp, HeapReg);
        StoreLocal(listPtrLocal, listTmp);
        X86_64Encoder.AddRI(m_text, HeapReg, totalSize);

        // Store length
        byte lenReg = AllocTemp();
        X86_64Encoder.Li(m_text, lenReg, list.Elements.Length);
        X86_64Encoder.MovStore(m_text, LoadLocal(listPtrLocal), lenReg, 0);

        // Store elements
        for (int i = 0; i < elemLocals.Count; i++)
        {
            byte val = LoadLocal(elemLocals[i]);
            byte ptr = LoadLocal(listPtrLocal);
            X86_64Encoder.MovStore(m_text, ptr, val, 8 + i * 8);
        }

        return LoadLocal(listPtrLocal);
    }

    // ── Regions ──────────────────────────────────────────────────

    byte EmitRegion(IRRegion region)
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
        int savedHeap = AllocLocal();
        byte hpTmp = AllocTemp();
        X86_64Encoder.MovRR(m_text, hpTmp, HeapReg);
        StoreLocal(savedHeap, hpTmp);

        byte bodyResult = EmitExpr(region.Body);

        // Scalar return — restore HeapReg, value survives in register
        X86_64Encoder.MovRR(m_text, HeapReg, LoadLocal(savedHeap));
        return bodyResult;
    }

    byte EmitEscapeCopy(int srcLocal, CodexType type)
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
            case "show" when args.Count == 1:
                return EmitShow(args[0]);
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
                    if (m_target == X86_64Target.BareMetal)
                    {
                        // Bare metal: read source from serial (COM1) until EOT/null
                        EmitExpr(args[0]); // evaluate path (ignored)
                        EmitCallTo("__bare_metal_read_serial");
                        byte rd = AllocTemp();
                        X86_64Encoder.MovRR(m_text, rd, Reg.RAX);
                        return rd;
                    }
                    byte path = EmitExpr(args[0]);
                    X86_64Encoder.MovRR(m_text, Reg.RDI, path);
                    EmitCallTo("__read_file");
                    byte rd2 = AllocTemp();
                    X86_64Encoder.MovRR(m_text, rd2, Reg.RAX);
                    return rd2;
                }
                return byte.MaxValue;
            case "read-line" when args.Count == 0:
            {
                if (m_target == X86_64Target.BareMetal)
                {
                    // Bare metal: return embedded path string
                    int pathOffset = AddRodataString("test.codex");
                    byte rd = AllocTemp();
                    EmitLoadRodataAddress(rd, pathOffset);
                    return rd;
                }
                EmitCallTo("__read_line");
                byte rd2 = AllocTemp();
                X86_64Encoder.MovRR(m_text, rd2, Reg.RAX);
                return rd2;
            }
            case "write-file" when args.Count == 2:
            {
                byte pathReg = EmitExpr(args[0]);
                int savedPath = AllocLocal();
                StoreLocal(savedPath, pathReg);
                byte contentReg = EmitExpr(args[1]);
                if (m_target == X86_64Target.BareMetal)
                {
                    EmitSerialStringFromPtr(contentReg);
                }
                else
                {
                    X86_64Encoder.MovLoad(m_text, Reg.RDX, contentReg, 0);
                    X86_64Encoder.Lea(m_text, Reg.RSI, contentReg, 8);
                    X86_64Encoder.Li(m_text, Reg.RAX, 1);
                    X86_64Encoder.Li(m_text, Reg.RDI, 1);
                    X86_64Encoder.Syscall(m_text);
                }
                byte wrRd = AllocTemp();
                X86_64Encoder.Li(m_text, wrRd, 0);
                return wrRd;
            }
            case "file-exists" when args.Count == 1:
            {
                EmitExpr(args[0]); // evaluate for side effects
                byte feRd = AllocTemp();
                X86_64Encoder.Li(m_text, feRd, 1); // simplified: always true
                return feRd;
            }
            case "get-args" when args.Count == 0:
            {
                // Return empty list (heap-allocated [length=0])
                byte gaRd = AllocTemp();
                X86_64Encoder.MovRR(m_text, gaRd, HeapReg);
                X86_64Encoder.Li(m_text, Reg.R11, 0);
                X86_64Encoder.MovStore(m_text, HeapReg, Reg.R11, 0);
                X86_64Encoder.AddRI(m_text, HeapReg, 8);
                return gaRd;
            }
            case "current-dir" when args.Count == 0:
            {
                // Return "."
                int dotOff = AddRodataString(".");
                byte cdRd = AllocTemp();
                EmitLoadRodataAddress(cdRd, dotOff);
                return cdRd;
            }
            case "char-at":
                if (args.Count >= 2)
                {
                    byte str = EmitExpr(args[0]);
                    int savedStr = AllocLocal();
                    StoreLocal(savedStr, str);
                    byte idx = EmitExpr(args[1]);
                    byte strLoaded = LoadLocal(savedStr);
                    // char-at returns byte value as integer: movzx byte at [str+8+idx]
                    X86_64Encoder.AddRR(m_text, idx, strLoaded);
                    X86_64Encoder.MovzxByte(m_text, idx, idx, 8);
                    return idx;
                }
                return byte.MaxValue;
            case "substring":
                if (args.Count >= 3)
                {
                    byte str = EmitExpr(args[0]);
                    int savedStr = AllocLocal();
                    StoreLocal(savedStr, str);
                    byte start = EmitExpr(args[1]);
                    int savedStart = AllocLocal();
                    StoreLocal(savedStart, start);
                    byte len = EmitExpr(args[2]);
                    int savedLen = AllocLocal();
                    StoreLocal(savedLen, len);
                    // Allocate result: [len][bytes]
                    int subPtr = AllocLocal();
                    byte subTmp = AllocTemp();
                    X86_64Encoder.MovRR(m_text, subTmp, HeapReg);
                    StoreLocal(subPtr, subTmp);
                    byte lenLoaded = LoadLocal(savedLen);
                    X86_64Encoder.MovRR(m_text, Reg.R11, lenLoaded);
                    X86_64Encoder.AddRI(m_text, Reg.R11, 15);
                    X86_64Encoder.AndRI(m_text, Reg.R11, -8);
                    X86_64Encoder.AddRR(m_text, HeapReg, Reg.R11);
                    lenLoaded = LoadLocal(savedLen);
                    X86_64Encoder.MovStore(m_text, LoadLocal(subPtr), lenLoaded, 0);
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
                    X86_64Encoder.MovRR(m_text, Reg.RDX, LoadLocal(subPtr));
                    X86_64Encoder.AddRR(m_text, Reg.RDX, Reg.R11);
                    X86_64Encoder.MovStoreByte(m_text, Reg.RDX, Reg.RSI, 8);
                    X86_64Encoder.AddRI(m_text, Reg.R11, 1);
                    X86_64Encoder.Jmp(m_text, subLoop - (m_text.Count + 5));
                    PatchJcc(subExit, m_text.Count);
                    return LoadLocal(subPtr);
                }
                return byte.MaxValue;
            case "list-cons":
                if (args.Count >= 2)
                {
                    byte head = EmitExpr(args[0]);
                    int savedHead = AllocLocal();
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
                    int savedL1 = AllocLocal();
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
                    int savedList = AllocLocal();
                    StoreLocal(savedList, list);
                    byte idx = EmitExpr(args[1]);
                    byte listLoaded = LoadLocal(savedList);
                    // elem = list[8 + idx*8] — use temp to avoid clobbering idx
                    byte addr = AllocTemp();
                    X86_64Encoder.MovRR(m_text, addr, idx);
                    X86_64Encoder.ShlRI(m_text, addr, 3);
                    X86_64Encoder.AddRR(m_text, addr, listLoaded);
                    byte rd = AllocTemp();
                    X86_64Encoder.MovLoad(m_text, rd, addr, 8);
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
            case "char-code-at" when args.Count >= 2:
            {
                byte textReg = EmitExpr(args[0]);
                int savedText = AllocLocal();
                StoreLocal(savedText, textReg);
                byte idxReg = EmitExpr(args[1]);
                int savedIdx = AllocLocal();
                StoreLocal(savedIdx, idxReg);
                byte text = LoadLocal(savedText);
                byte idx = LoadLocal(savedIdx);
                byte rd = AllocTemp();
                X86_64Encoder.MovRR(m_text, rd, text);
                X86_64Encoder.AddRR(m_text, rd, idx);
                X86_64Encoder.MovzxByte(m_text, rd, rd, 8);
                return rd;
            }
            case "char-code" when args.Count >= 1:
            {
                // char-code: identity — Char is already an integer
                return EmitExpr(args[0]);
            }
            case "code-to-char" when args.Count >= 1:
            {
                // code-to-char: identity — Char is already an integer
                return EmitExpr(args[0]);
            }
            case "char-to-text" when args.Count >= 1:
            {
                // Allocate 1-char string on heap: [len=1][byte]
                byte codeReg = EmitExpr(args[0]);
                int savedCode = AllocLocal();
                StoreLocal(savedCode, codeReg);
                int c2tPtr = AllocLocal();
                byte c2tTmp = AllocTemp();
                X86_64Encoder.MovRR(m_text, c2tTmp, HeapReg);
                StoreLocal(c2tPtr, c2tTmp);
                X86_64Encoder.AddRI(m_text, HeapReg, 16);
                X86_64Encoder.Li(m_text, Reg.R11, 1);
                X86_64Encoder.MovStore(m_text, LoadLocal(c2tPtr), Reg.R11, 0);
                byte code = LoadLocal(savedCode);
                X86_64Encoder.MovStoreByte(m_text, LoadLocal(c2tPtr), code, 8);
                return LoadLocal(c2tPtr);
            }
            case "is-letter" when args.Count >= 1:
            {
                // CCE: letters are 13-64 (lowercase 13-38, uppercase 39-64)
                // Single range check: (rd - 13) <= (64 - 13)
                byte rd = EmitExpr(args[0]);
                X86_64Encoder.SubRI(m_text, rd, 13); // CCE letter start
                X86_64Encoder.CmpRI(m_text, rd, 64 - 13); // CCE letter range
                X86_64Encoder.Setcc(m_text, X86_64Encoder.CC_BE, rd);
                X86_64Encoder.MovzxByteSelf(m_text, rd);
                return rd;
            }
            case "is-digit" when args.Count >= 1:
            {
                // CCE: digits are 3-12
                // (rd - 3) <= (12 - 3)
                byte rd = EmitExpr(args[0]);
                X86_64Encoder.SubRI(m_text, rd, 3); // CCE digit start
                X86_64Encoder.CmpRI(m_text, rd, 12 - 3); // CCE digit range
                X86_64Encoder.Setcc(m_text, X86_64Encoder.CC_BE, rd);
                X86_64Encoder.MovzxByteSelf(m_text, rd);
                return rd;
            }
            case "is-whitespace" when args.Count >= 1:
            {
                // CCE: whitespace is 0-2 (NUL, LF, Space)
                // Single comparison: rd <= 2
                byte rd = EmitExpr(args[0]);
                X86_64Encoder.CmpRI(m_text, rd, 2);
                X86_64Encoder.Setcc(m_text, X86_64Encoder.CC_BE, rd);
                X86_64Encoder.MovzxByteSelf(m_text, rd);
                return rd;
            }
            case "negate" when args.Count >= 1:
            {
                byte val = EmitExpr(args[0]);
                byte rd = AllocTemp();
                X86_64Encoder.MovRR(m_text, rd, val);
                X86_64Encoder.NegR(m_text, rd);
                return rd;
            }
            case "text-replace" when args.Count >= 3:
            {
                byte textReg = EmitExpr(args[0]);
                int savedText = AllocLocal();
                StoreLocal(savedText, textReg);
                byte oldReg = EmitExpr(args[1]);
                int savedOld = AllocLocal();
                StoreLocal(savedOld, oldReg);
                byte newReg = EmitExpr(args[2]);
                X86_64Encoder.MovRR(m_text, Reg.RDX, newReg);
                X86_64Encoder.MovRR(m_text, Reg.RSI, LoadLocal(savedOld));
                X86_64Encoder.MovRR(m_text, Reg.RDI, LoadLocal(savedText));
                EmitCallTo("__str_replace");
                byte rd = AllocTemp();
                X86_64Encoder.MovRR(m_text, rd, Reg.RAX);
                return rd;
            }
            case "text-contains" when args.Count >= 2:
            {
                byte textReg = EmitExpr(args[0]);
                int savedText = AllocLocal();
                StoreLocal(savedText, textReg);
                byte needleReg = EmitExpr(args[1]);
                X86_64Encoder.MovRR(m_text, Reg.RSI, needleReg);
                X86_64Encoder.MovRR(m_text, Reg.RDI, LoadLocal(savedText));
                EmitCallTo("__text_contains");
                byte rd = AllocTemp();
                X86_64Encoder.MovRR(m_text, rd, Reg.RAX);
                return rd;
            }
            case "text-starts-with" when args.Count >= 2:
            {
                byte textReg = EmitExpr(args[0]);
                int savedText = AllocLocal();
                StoreLocal(savedText, textReg);
                byte prefixReg = EmitExpr(args[1]);
                X86_64Encoder.MovRR(m_text, Reg.RSI, prefixReg);
                X86_64Encoder.MovRR(m_text, Reg.RDI, LoadLocal(savedText));
                EmitCallTo("__text_starts_with");
                byte rd = AllocTemp();
                X86_64Encoder.MovRR(m_text, rd, Reg.RAX);
                return rd;
            }
            case "text-compare" when args.Count >= 2:
            {
                byte aReg = EmitExpr(args[0]);
                int savedA = AllocLocal();
                StoreLocal(savedA, aReg);
                byte bReg = EmitExpr(args[1]);
                X86_64Encoder.MovRR(m_text, Reg.RSI, bReg);
                X86_64Encoder.MovRR(m_text, Reg.RDI, LoadLocal(savedA));
                EmitCallTo("__text_compare");
                byte rd = AllocTemp();
                X86_64Encoder.MovRR(m_text, rd, Reg.RAX);
                return rd;
            }
            case "list-snoc" when args.Count >= 2:
            {
                byte listReg = EmitExpr(args[0]);
                int savedList = AllocLocal();
                StoreLocal(savedList, listReg);
                byte elemReg = EmitExpr(args[1]);
                X86_64Encoder.MovRR(m_text, Reg.RSI, elemReg);
                X86_64Encoder.MovRR(m_text, Reg.RDI, LoadLocal(savedList));
                EmitCallTo("__list_snoc");
                byte rd = AllocTemp();
                X86_64Encoder.MovRR(m_text, rd, Reg.RAX);
                return rd;
            }
            case "list-insert-at" when args.Count >= 3:
            {
                byte listReg = EmitExpr(args[0]);
                int savedList = AllocLocal();
                StoreLocal(savedList, listReg);
                byte idxReg = EmitExpr(args[1]);
                int savedIdx = AllocLocal();
                StoreLocal(savedIdx, idxReg);
                byte elemReg = EmitExpr(args[2]);
                X86_64Encoder.MovRR(m_text, Reg.RDX, elemReg);
                X86_64Encoder.MovRR(m_text, Reg.RSI, LoadLocal(savedIdx));
                X86_64Encoder.MovRR(m_text, Reg.RDI, LoadLocal(savedList));
                EmitCallTo("__list_insert_at");
                byte rd = AllocTemp();
                X86_64Encoder.MovRR(m_text, rd, Reg.RAX);
                return rd;
            }
            case "list-contains" when args.Count >= 2:
            {
                byte listReg = EmitExpr(args[0]);
                int savedList = AllocLocal();
                StoreLocal(savedList, listReg);
                byte elemReg = EmitExpr(args[1]);
                X86_64Encoder.MovRR(m_text, Reg.RSI, elemReg);
                X86_64Encoder.MovRR(m_text, Reg.RDI, LoadLocal(savedList));
                EmitCallTo("__list_contains");
                byte rd = AllocTemp();
                X86_64Encoder.MovRR(m_text, rd, Reg.RAX);
                return rd;
            }
            case "text-concat-list" when args.Count >= 1:
            {
                byte listReg = EmitExpr(args[0]);
                X86_64Encoder.MovRR(m_text, Reg.RDI, listReg);
                EmitCallTo("__text_concat_list");
                byte rd = AllocTemp();
                X86_64Encoder.MovRR(m_text, rd, Reg.RAX);
                return rd;
            }
            case "text-split" when args.Count >= 2:
            {
                byte textReg = EmitExpr(args[0]);
                int savedText = AllocLocal();
                StoreLocal(savedText, textReg);
                byte delimReg = EmitExpr(args[1]);
                X86_64Encoder.MovRR(m_text, Reg.RSI, delimReg);
                X86_64Encoder.MovRR(m_text, Reg.RDI, LoadLocal(savedText));
                EmitCallTo("__text_split");
                byte rd = AllocTemp();
                X86_64Encoder.MovRR(m_text, rd, Reg.RAX);
                return rd;
            }
            case "fork" when args.Count == 1:
                return EmitFork(args[0]);
            case "await" when args.Count == 1:
                return EmitAwait(args[0]);

            default:
                return byte.MaxValue;
        }
    }

    byte EmitFork(IRExpr thunkExpr)
    {
        // ── Per-thread arena fork ──────────────────────────────────
        // Child gets its own 1MB heap arena via mmap.
        // Parent and child never share HeapReg (R10).

        // 1. Evaluate the thunk (closure pointer)
        byte thunk = EmitExpr(thunkExpr);
        int savedThunk = AllocLocal();
        StoreLocal(savedThunk, thunk);

        // 2. Allocate task slot on PARENT heap: [4B futex_word=0] [4B pad] [8B result]
        //    (futex operates on 32-bit words, so first 4 bytes are the futex)
        byte taskPtr = AllocTemp();
        X86_64Encoder.MovRR(m_text, taskPtr, HeapReg);
        X86_64Encoder.Li(m_text, Reg.R11, 0);
        X86_64Encoder.MovStore(m_text, HeapReg, Reg.R11, 0);  // futex_word + pad = 0
        X86_64Encoder.MovStore(m_text, HeapReg, Reg.R11, 8);  // result = 0
        X86_64Encoder.AddRI(m_text, HeapReg, 16);
        int savedTask = AllocLocal();
        StoreLocal(savedTask, taskPtr);

        // 3. mmap 1MB arena for child heap
        X86_64Encoder.Li(m_text, Reg.RAX, 9);     // SYS_mmap
        X86_64Encoder.Li(m_text, Reg.RDI, 0);     // addr = kernel chooses
        X86_64Encoder.Li(m_text, Reg.RSI, 1048576); // 1MB
        X86_64Encoder.Li(m_text, Reg.RDX, 3);     // PROT_READ | PROT_WRITE
        X86_64Encoder.Li(m_text, Reg.R10, 0x22);  // MAP_PRIVATE | MAP_ANONYMOUS
        X86_64Encoder.Li(m_text, Reg.R8, -1);     // fd = -1
        X86_64Encoder.Li(m_text, Reg.R9, 0);      // offset = 0
        X86_64Encoder.Syscall(m_text);
        // RAX = child arena base
        int savedArena = AllocLocal();
        StoreLocal(savedArena, Reg.RAX);
        // Restore HeapReg (mmap clobbered nothing, but R10 was used for flags)
        X86_64Encoder.MovRR(m_text, HeapReg, LoadLocal(savedTask));
        X86_64Encoder.SubRI(m_text, HeapReg, 16); // undo the +16 above
        X86_64Encoder.AddRI(m_text, HeapReg, 16); // re-advance past task slot
        // Actually, HeapReg was already correct before mmap — let's just reload
        // the pre-task value and re-advance. Simpler: save HeapReg before mmap.
        // Let me restructure: save parent HeapReg before mmap.

        // OK let me simplify — the mmap doesn't modify HeapReg (R10 was saved
        // to a local before being clobbered by the R10=flags arg). Actually,
        // mmap uses R10 for the flags argument, which clobbers our HeapReg.
        // So we need to save/restore it.

        // Restructure: save HeapReg before mmap, restore after.
        // I'll redo from step 2.

        // ... this inline approach is getting messy. Let me use a cleaner structure.
        return EmitForkClean(thunkExpr, savedThunk, savedTask);
    }

    byte EmitForkClean(IRExpr thunkExpr, int savedThunk, int savedTask)
    {
        // Save parent HeapReg
        int savedHeap = AllocLocal();
        StoreLocal(savedHeap, HeapReg);

        // mmap 1MB for child arena
        X86_64Encoder.Li(m_text, Reg.RAX, 9);     // SYS_mmap
        X86_64Encoder.Li(m_text, Reg.RDI, 0);
        X86_64Encoder.Li(m_text, Reg.RSI, 1048576);
        X86_64Encoder.Li(m_text, Reg.RDX, 3);
        X86_64Encoder.Li(m_text, Reg.R10, 0x22);  // clobbers HeapReg!
        X86_64Encoder.Li(m_text, Reg.R8, -1);
        X86_64Encoder.Li(m_text, Reg.R9, 0);
        X86_64Encoder.Syscall(m_text);
        int savedArena = AllocLocal();
        StoreLocal(savedArena, Reg.RAX);

        // Restore parent HeapReg
        X86_64Encoder.MovRR(m_text, HeapReg, LoadLocal(savedHeap));

        // mmap 64KB for child stack
        X86_64Encoder.Li(m_text, Reg.RAX, 9);     // SYS_mmap
        X86_64Encoder.Li(m_text, Reg.RDI, 0);
        X86_64Encoder.Li(m_text, Reg.RSI, 65536);
        X86_64Encoder.Li(m_text, Reg.RDX, 3);
        // Save HeapReg again before clobbering R10
        StoreLocal(savedHeap, HeapReg);
        X86_64Encoder.Li(m_text, Reg.R10, 0x22);
        X86_64Encoder.Li(m_text, Reg.R8, -1);
        X86_64Encoder.Li(m_text, Reg.R9, 0);
        X86_64Encoder.Syscall(m_text);
        // RAX = child stack base
        // child stack top = base + 65536 - 48 (room for 6 words of data)
        X86_64Encoder.AddRI(m_text, Reg.RAX, 65536 - 48);
        // Store: [+0]=thunk, [+8]=task, [+16]=arena on child stack
        byte thunkLoaded = LoadLocal(savedThunk);
        X86_64Encoder.MovStore(m_text, Reg.RAX, thunkLoaded, 0);
        byte taskLoaded = LoadLocal(savedTask);
        X86_64Encoder.MovStore(m_text, Reg.RAX, taskLoaded, 8);
        byte arenaLoaded = LoadLocal(savedArena);
        X86_64Encoder.MovStore(m_text, Reg.RAX, arenaLoaded, 16);
        // RSI = child stack top (for clone)
        X86_64Encoder.MovRR(m_text, Reg.RSI, Reg.RAX);

        // clone(CLONE_VM|CLONE_FS|CLONE_FILES|CLONE_SIGHAND|CLONE_THREAD = 0x10F00)
        X86_64Encoder.Li(m_text, Reg.RDI, 0x10F00); // flags
        // RSI = child stack
        X86_64Encoder.Li(m_text, Reg.RDX, 0);     // ptid
        X86_64Encoder.Li(m_text, Reg.R10, 0);     // ctid (clobbers HeapReg again!)
        X86_64Encoder.Li(m_text, Reg.R8, 0);      // tls
        X86_64Encoder.Li(m_text, Reg.RAX, 56);    // SYS_clone
        X86_64Encoder.Syscall(m_text);
        // RAX = 0 in child, child_tid in parent

        // Restore parent HeapReg (was clobbered by R10=ctid)
        X86_64Encoder.MovRR(m_text, HeapReg, LoadLocal(savedHeap));

        X86_64Encoder.CmpRI(m_text, Reg.RAX, 0);
        int childJump = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_E, 0); // jz child_code

        // ── Parent: jump past child code, return task pointer ──
        int parentSkip = m_text.Count;
        X86_64Encoder.Jmp(m_text, 0); // placeholder, patched after child code

        // ── Child code ──
        int childCodeAddr = m_text.Count;
        PatchJcc(childJump, childCodeAddr);

        // Child: load thunk, task, arena from stack
        // RSP points at our data area (clone set it)
        X86_64Encoder.MovLoad(m_text, Reg.RBX, Reg.RSP, 0);   // thunk
        X86_64Encoder.MovLoad(m_text, Reg.R12, Reg.RSP, 8);    // task
        X86_64Encoder.MovLoad(m_text, HeapReg, Reg.RSP, 16);   // arena → R10

        // Call thunk(null)
        X86_64Encoder.Li(m_text, Reg.RDI, 0);
        X86_64Encoder.MovLoad(m_text, Reg.R11, Reg.RBX, 0); // code_ptr = [thunk+0]
        m_text.Add(0x41); m_text.Add(0xFF); m_text.Add(0xD3); // call r11

        // Store result in task[8], set futex word to 1
        X86_64Encoder.MovStore(m_text, Reg.R12, Reg.RAX, 8);   // task[8] = result
        X86_64Encoder.Li(m_text, Reg.R11, 1);
        // Store 32-bit 1 to task[0] (futex word is 32-bit)
        X86_64Encoder.MovStore(m_text, Reg.R12, Reg.R11, 0);   // task[0] = 1

        // futex_wake(task, FUTEX_WAKE=1, val=1)
        X86_64Encoder.Li(m_text, Reg.RAX, 202);    // SYS_futex
        X86_64Encoder.MovRR(m_text, Reg.RDI, Reg.R12); // addr = task
        X86_64Encoder.Li(m_text, Reg.RSI, 1);      // FUTEX_WAKE
        X86_64Encoder.Li(m_text, Reg.RDX, 1);      // wake 1 waiter
        X86_64Encoder.Li(m_text, Reg.R10, 0);
        X86_64Encoder.Syscall(m_text);

        // Exit thread
        X86_64Encoder.Li(m_text, Reg.RAX, 60);     // SYS_exit
        X86_64Encoder.Li(m_text, Reg.RDI, 0);
        X86_64Encoder.Syscall(m_text);

        // ── Patch parent skip jump ──
        int afterChild = m_text.Count;
        int rel = afterChild - (parentSkip + 5); // 5 = size of jmp near
        m_text[parentSkip + 1] = (byte)(rel & 0xFF);
        m_text[parentSkip + 2] = (byte)((rel >> 8) & 0xFF);
        m_text[parentSkip + 3] = (byte)((rel >> 16) & 0xFF);
        m_text[parentSkip + 4] = (byte)((rel >> 24) & 0xFF);

        return LoadLocal(savedTask);
    }

    byte EmitAwait(IRExpr taskExpr)
    {
        // Wait for child thread to complete via futex
        byte taskPtr = EmitExpr(taskExpr);
        int savedTask = AllocLocal();
        StoreLocal(savedTask, taskPtr);

        // Save HeapReg across potential futex syscall
        int savedHeap = AllocLocal();
        StoreLocal(savedHeap, HeapReg);

        // Loop: check futex word
        int loopTop = m_text.Count;
        byte taskLoaded = LoadLocal(savedTask);
        // Load 64-bit from task[0] — low 32 bits are the futex word
        byte futexVal = AllocTemp();
        X86_64Encoder.MovLoad(m_text, futexVal, taskLoaded, 0);
        X86_64Encoder.CmpRI(m_text, futexVal, 1);
        int doneJump = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_GE, 0); // jge done (>=1 means done)

        // futex_wait(task, FUTEX_WAIT=0, expected=0, timeout=NULL)
        taskLoaded = LoadLocal(savedTask);
        X86_64Encoder.Li(m_text, Reg.RAX, 202);
        X86_64Encoder.MovRR(m_text, Reg.RDI, taskLoaded);
        X86_64Encoder.Li(m_text, Reg.RSI, 0);      // FUTEX_WAIT
        X86_64Encoder.Li(m_text, Reg.RDX, 0);      // expected = 0
        X86_64Encoder.Li(m_text, Reg.R10, 0);      // timeout = NULL (clobbers HeapReg!)
        X86_64Encoder.Syscall(m_text);

        // Restore HeapReg and loop
        X86_64Encoder.MovRR(m_text, HeapReg, LoadLocal(savedHeap));
        X86_64Encoder.Jmp(m_text, loopTop - (m_text.Count + 5));

        // Done: load result
        int doneAddr = m_text.Count;
        PatchJcc(doneJump, doneAddr);
        X86_64Encoder.MovRR(m_text, HeapReg, LoadLocal(savedHeap)); // restore HeapReg
        taskLoaded = LoadLocal(savedTask);
        byte result = AllocTemp();
        X86_64Encoder.MovLoad(m_text, result, taskLoaded, 8);
        return result;
    }

    // PatchJcc already defined elsewhere in this class

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

    byte EmitShow(IRExpr arg)
    {
        byte valReg = EmitExpr(arg);
        switch (arg.Type)
        {
            case IntegerType:
                X86_64Encoder.MovRR(m_text, Reg.RDI, valReg);
                EmitCallTo("__itoa");
                byte iRd = AllocTemp();
                X86_64Encoder.MovRR(m_text, iRd, Reg.RAX);
                return iRd;
            case BooleanType:
            {
                int trueOff = AddRodataString("True");
                int falseOff = AddRodataString("False");
                int savedVal = AllocLocal();
                StoreLocal(savedVal, valReg);
                byte val = LoadLocal(savedVal);
                byte rd = AllocTemp();
                X86_64Encoder.TestRR(m_text, val, val);
                int jeFalse = m_text.Count;
                X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_E, 0);
                EmitLoadRodataAddress(rd, trueOff);
                int jmpEnd = m_text.Count;
                X86_64Encoder.Jmp(m_text, 0);
                int falseLbl = m_text.Count;
                PatchJcc(jeFalse, falseLbl);
                EmitLoadRodataAddress(rd, falseOff);
                int endLbl = m_text.Count;
                PatchJmp(jmpEnd, endLbl);
                return rd;
            }
            default:
                return valReg;
        }
    }

    void EmitPrintText(byte ptrReg)
    {
        // Strings are CCE-encoded. Output requires CCE→Unicode conversion per byte.
        // The CceToUnicode table (128 bytes) lives in .rodata at m_cceToUnicodeTableOffset.
        int savedPtr = AllocLocal();
        StoreLocal(savedPtr, ptrReg);

        // Load CCE→Unicode table address into a local
        int savedTable = AllocLocal();
        byte tableReg = AllocTemp();
        m_rodataFixups.Add(new RodataFixup(m_text.Count + 2, m_cceToUnicodeTableOffset));
        X86_64Encoder.MovRI64(m_text, tableReg, 0); // patched to rodata+table
        StoreLocal(savedTable, tableReg);

        byte ptr = LoadLocal(savedPtr);
        byte len = AllocTemp();
        X86_64Encoder.MovLoad(m_text, len, ptr, 0);
        int savedLen = AllocLocal();
        StoreLocal(savedLen, len);
        int savedIdx = AllocLocal();
        X86_64Encoder.Li(m_text, Reg.R11, 0);
        StoreLocal(savedIdx, Reg.R11);

        int loopTop = m_text.Count;
        byte idx = LoadLocal(savedIdx);
        byte lenCheck = LoadLocal(savedLen);
        X86_64Encoder.CmpRR(m_text, idx, lenCheck);
        int doneJump = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_GE, 0);

        // Load CCE byte: ptr + 8 + idx
        byte ptrL = LoadLocal(savedPtr);
        idx = LoadLocal(savedIdx);
        X86_64Encoder.Lea(m_text, Reg.RSI, ptrL, 8);
        X86_64Encoder.AddRR(m_text, Reg.RSI, idx);
        X86_64Encoder.MovzxByte(m_text, Reg.RAX, Reg.RSI, 0); // RAX = CCE byte

        // Convert CCE→Unicode: table[CCE byte]
        byte tbl = LoadLocal(savedTable);
        X86_64Encoder.AddRR(m_text, Reg.RAX, tbl);
        X86_64Encoder.MovzxByte(m_text, Reg.RAX, Reg.RAX, 0); // RAX = Unicode byte

        if (m_target == X86_64Target.BareMetal)
        {
            X86_64Encoder.Li(m_text, Reg.RDX, 0x3F8);
            X86_64Encoder.OutDxAl(m_text);
        }
        else
        {
            // Write single byte via sys_write: buf on stack
            X86_64Encoder.SubRI(m_text, Reg.RSP, 8);
            X86_64Encoder.MovStore(m_text, Reg.RSP, Reg.RAX, 0);
            X86_64Encoder.MovRR(m_text, Reg.RSI, Reg.RSP); // buf
            X86_64Encoder.Li(m_text, Reg.RDX, 1); // count = 1
            X86_64Encoder.Li(m_text, Reg.RAX, 1); // sys_write
            X86_64Encoder.Li(m_text, Reg.RDI, 1); // stdout
            X86_64Encoder.Syscall(m_text);
            X86_64Encoder.AddRI(m_text, Reg.RSP, 8);
        }

        // idx++
        idx = LoadLocal(savedIdx);
        X86_64Encoder.AddRI(m_text, idx, 1);
        StoreLocal(savedIdx, idx);
        X86_64Encoder.Jmp(m_text, loopTop - (m_text.Count + 5));

        PatchJcc(doneJump, m_text.Count);

        // Newline
        if (m_target == X86_64Target.BareMetal)
        {
            X86_64Encoder.Li(m_text, Reg.RAX, '\n');
            X86_64Encoder.Li(m_text, Reg.RDX, 0x3F8);
            X86_64Encoder.OutDxAl(m_text);
        }
        else
        {
            EmitPrintNewline();
        }
    }

    void EmitPrintBool(byte valueReg)
    {
        int savedVal = AllocLocal();
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
        // Per-byte CCE→Unicode conversion + output (same logic as EmitPrintText without newline)
        int savedPtr = AllocLocal();
        StoreLocal(savedPtr, ptrReg);

        int savedTable = AllocLocal();
        byte tableReg = AllocTemp();
        m_rodataFixups.Add(new RodataFixup(m_text.Count + 2, m_cceToUnicodeTableOffset));
        X86_64Encoder.MovRI64(m_text, tableReg, 0);
        StoreLocal(savedTable, tableReg);

        byte ptr = LoadLocal(savedPtr);
        byte len = AllocTemp();
        X86_64Encoder.MovLoad(m_text, len, ptr, 0);
        int savedLen = AllocLocal();
        StoreLocal(savedLen, len);
        int savedIdx = AllocLocal();
        X86_64Encoder.Li(m_text, Reg.R11, 0);
        StoreLocal(savedIdx, Reg.R11);

        int loopTop = m_text.Count;
        byte idx = LoadLocal(savedIdx);
        byte lenCheck = LoadLocal(savedLen);
        X86_64Encoder.CmpRR(m_text, idx, lenCheck);
        int doneJump = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_GE, 0);

        byte ptrL = LoadLocal(savedPtr);
        idx = LoadLocal(savedIdx);
        X86_64Encoder.Lea(m_text, Reg.RSI, ptrL, 8);
        X86_64Encoder.AddRR(m_text, Reg.RSI, idx);
        X86_64Encoder.MovzxByte(m_text, Reg.RAX, Reg.RSI, 0);

        byte tbl = LoadLocal(savedTable);
        X86_64Encoder.AddRR(m_text, Reg.RAX, tbl);
        X86_64Encoder.MovzxByte(m_text, Reg.RAX, Reg.RAX, 0);

        if (m_target == X86_64Target.BareMetal)
        {
            X86_64Encoder.Li(m_text, Reg.RDX, 0x3F8);
            X86_64Encoder.OutDxAl(m_text);
        }
        else
        {
            X86_64Encoder.SubRI(m_text, Reg.RSP, 8);
            X86_64Encoder.MovStore(m_text, Reg.RSP, Reg.RAX, 0);
            X86_64Encoder.MovRR(m_text, Reg.RSI, Reg.RSP);
            X86_64Encoder.Li(m_text, Reg.RDX, 1);
            X86_64Encoder.Li(m_text, Reg.RAX, 1);
            X86_64Encoder.Li(m_text, Reg.RDI, 1);
            X86_64Encoder.Syscall(m_text);
            X86_64Encoder.AddRI(m_text, Reg.RSP, 8);
        }

        idx = LoadLocal(savedIdx);
        X86_64Encoder.AddRI(m_text, idx, 1);
        StoreLocal(savedIdx, idx);
        X86_64Encoder.Jmp(m_text, loopTop - (m_text.Count + 5));

        PatchJcc(doneJump, m_text.Count);
    }

    void EmitPrintNewline()
    {
        if (m_target == X86_64Target.BareMetal)
        {
            X86_64Encoder.Li(m_text, Reg.RAX, '\n');
            X86_64Encoder.Li(m_text, Reg.RDX, 0x3F8);
            X86_64Encoder.OutDxAl(m_text);
        }
        else
        {
            // Newline is an I/O boundary — emit literal Unicode 0x0A, not CCE byte.
            X86_64Encoder.SubRI(m_text, Reg.RSP, 8);
            X86_64Encoder.Li(m_text, Reg.RAX, 0x0A);
            X86_64Encoder.MovStore(m_text, Reg.RSP, Reg.RAX, 0);
            X86_64Encoder.MovRR(m_text, Reg.RSI, Reg.RSP); // buf
            X86_64Encoder.Li(m_text, Reg.RDX, 1);          // count = 1
            X86_64Encoder.Li(m_text, Reg.RAX, 1);          // sys_write
            X86_64Encoder.Li(m_text, Reg.RDI, 1);          // stdout
            X86_64Encoder.Syscall(m_text);
            X86_64Encoder.AddRI(m_text, Reg.RSP, 8);
        }
    }

    void EmitSerialStringFromPtr(byte ptrReg)
    {
        // Print string at [ptrReg+0]=len, [ptrReg+8..]=data to COM1.
        // Strings are CCE-encoded; serial output is a Unicode boundary, so convert per byte.
        int savedPtr = AllocLocal();
        StoreLocal(savedPtr, ptrReg);

        int savedTable = AllocLocal();
        byte tableReg = AllocTemp();
        m_rodataFixups.Add(new RodataFixup(m_text.Count + 2, m_cceToUnicodeTableOffset));
        X86_64Encoder.MovRI64(m_text, tableReg, 0); // patched to rodata+table
        StoreLocal(savedTable, tableReg);

        byte ptr = LoadLocal(savedPtr);
        byte len = AllocTemp();
        X86_64Encoder.MovLoad(m_text, len, ptr, 0);
        int savedLen = AllocLocal();
        StoreLocal(savedLen, len);
        int savedIdx = AllocLocal();
        X86_64Encoder.Li(m_text, Reg.R11, 0);
        StoreLocal(savedIdx, Reg.R11);

        int loopTop = m_text.Count;
        byte idx = LoadLocal(savedIdx);
        byte lenCheck = LoadLocal(savedLen);
        X86_64Encoder.CmpRR(m_text, idx, lenCheck);
        int doneJump = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_GE, 0);

        // Load CCE byte: ptr + 8 + idx
        byte ptrL = LoadLocal(savedPtr);
        idx = LoadLocal(savedIdx);
        X86_64Encoder.Lea(m_text, Reg.RSI, ptrL, 8);
        X86_64Encoder.AddRR(m_text, Reg.RSI, idx);
        X86_64Encoder.MovzxByte(m_text, Reg.RAX, Reg.RSI, 0); // RAX = CCE byte

        // Convert CCE→Unicode via table lookup
        byte tbl = LoadLocal(savedTable);
        X86_64Encoder.AddRR(m_text, Reg.RAX, tbl);
        X86_64Encoder.MovzxByte(m_text, Reg.RAX, Reg.RAX, 0); // RAX = Unicode byte

        X86_64Encoder.Li(m_text, Reg.RDX, 0x3F8);
        X86_64Encoder.OutDxAl(m_text);

        idx = LoadLocal(savedIdx);
        X86_64Encoder.AddRI(m_text, idx, 1);
        StoreLocal(savedIdx, idx);
        X86_64Encoder.Jmp(m_text, loopTop - (m_text.Count + 5));

        PatchJcc(doneJump, m_text.Count);
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
        if (m_target == X86_64Target.BareMetal)
            EmitBareMetalReadSerialHelper();
        EmitListConsHelper();
        EmitListAppendHelper();
        EmitStrReplaceHelper();
        EmitTextContainsHelper();
        EmitTextStartsWithHelper();
        EmitEscapeTextHelper();
        EmitTextCompareHelper();
        EmitListSnocHelper();
        EmitListInsertAtHelper();
        EmitListContainsHelper();
        EmitTextConcatListHelper();
        EmitTextSplitHelper();
        EmitIpowHelper();
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

        // Handle zero (CCE digit '0' = 3)
        X86_64Encoder.TestRR(m_text, Reg.RBX, Reg.RBX);
        int notZero = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_NE, 0);
        X86_64Encoder.Li(m_text, Reg.RSI, 3); // CCE '0'
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
        X86_64Encoder.AddRI(m_text, Reg.RDX, 3); // CCE digit offset: '0' = 3
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
        X86_64Encoder.Li(m_text, Reg.RSI, 73); // CCE '-'
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

    void EmitStrReplaceHelper()
    {
        // __str_replace: rdi=text, rsi=old, rdx=new → rax=result
        // Scan text for occurrences of old, replace with new.
        m_functionOffsets["__str_replace"] = m_text.Count;

        X86_64Encoder.PushR(m_text, Reg.RBX);
        X86_64Encoder.PushR(m_text, Reg.R12);
        X86_64Encoder.PushR(m_text, Reg.R13);
        X86_64Encoder.PushR(m_text, Reg.R14);
        X86_64Encoder.PushR(m_text, Reg.R15);

        // Save inputs: rbx=text, r12=old, r13=new
        X86_64Encoder.MovRR(m_text, Reg.RBX, Reg.RDI);
        X86_64Encoder.MovRR(m_text, Reg.R12, Reg.RSI);
        X86_64Encoder.MovRR(m_text, Reg.R13, Reg.RDX);

        // r14 = result base (heap), r15 = out_len, rcx = source index i
        X86_64Encoder.MovRR(m_text, Reg.R14, HeapReg);
        X86_64Encoder.Li(m_text, Reg.R15, 0);
        X86_64Encoder.Li(m_text, Reg.RCX, 0);

        // Load lengths
        X86_64Encoder.MovLoad(m_text, Reg.RAX, Reg.RBX, 0);   // text_len in rax (reloaded as needed)

        // Main loop
        int mainLoop = m_text.Count;
        X86_64Encoder.MovLoad(m_text, Reg.RAX, Reg.RBX, 0);
        X86_64Encoder.CmpRR(m_text, Reg.RCX, Reg.RAX);
        int doneCheck = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_GE, 0);

        // Check if old_len == 0 → copy byte (prevent infinite loop)
        X86_64Encoder.MovLoad(m_text, Reg.RDX, Reg.R12, 0);
        X86_64Encoder.TestRR(m_text, Reg.RDX, Reg.RDX);
        int noMatchOldEmpty = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_E, 0);

        // Check if i + old_len > text_len
        X86_64Encoder.MovRR(m_text, Reg.RSI, Reg.RCX);
        X86_64Encoder.AddRR(m_text, Reg.RSI, Reg.RDX);
        X86_64Encoder.CmpRR(m_text, Reg.RSI, Reg.RAX);
        int cantMatch = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_G, 0);

        // Compare text[i..i+old_len] with old
        X86_64Encoder.Li(m_text, Reg.RSI, 0); // j = 0
        int cmpLoop = m_text.Count;
        X86_64Encoder.CmpRR(m_text, Reg.RSI, Reg.RDX);
        int matchFound = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_GE, 0);

        // text[i+j+8] vs old[j+8]
        X86_64Encoder.MovRR(m_text, Reg.RAX, Reg.RBX);
        X86_64Encoder.AddRR(m_text, Reg.RAX, Reg.RCX);
        X86_64Encoder.AddRR(m_text, Reg.RAX, Reg.RSI);
        X86_64Encoder.MovzxByte(m_text, Reg.RAX, Reg.RAX, 8);
        X86_64Encoder.MovRR(m_text, Reg.RDI, Reg.R12);
        X86_64Encoder.AddRR(m_text, Reg.RDI, Reg.RSI);
        X86_64Encoder.MovzxByte(m_text, Reg.RDI, Reg.RDI, 8);
        X86_64Encoder.CmpRR(m_text, Reg.RAX, Reg.RDI);
        int mismatch = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_NE, 0);
        X86_64Encoder.AddRI(m_text, Reg.RSI, 1);
        X86_64Encoder.Jmp(m_text, cmpLoop - (m_text.Count + 5));

        // Match found: copy new_str bytes to output
        PatchJcc(matchFound, m_text.Count);
        X86_64Encoder.MovLoad(m_text, Reg.RDX, Reg.R13, 0); // new_len
        X86_64Encoder.Li(m_text, Reg.RSI, 0);
        int copyNewLoop = m_text.Count;
        X86_64Encoder.CmpRR(m_text, Reg.RSI, Reg.RDX);
        int copyNewDone = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_GE, 0);
        X86_64Encoder.MovRR(m_text, Reg.RAX, Reg.R13);
        X86_64Encoder.AddRR(m_text, Reg.RAX, Reg.RSI);
        X86_64Encoder.MovzxByte(m_text, Reg.RAX, Reg.RAX, 8);
        X86_64Encoder.MovRR(m_text, Reg.RDI, Reg.R14);
        X86_64Encoder.AddRR(m_text, Reg.RDI, Reg.R15);
        X86_64Encoder.MovStoreByte(m_text, Reg.RDI, Reg.RAX, 8);
        X86_64Encoder.AddRI(m_text, Reg.R15, 1);
        X86_64Encoder.AddRI(m_text, Reg.RSI, 1);
        X86_64Encoder.Jmp(m_text, copyNewLoop - (m_text.Count + 5));
        PatchJcc(copyNewDone, m_text.Count);

        // Advance i by old_len
        X86_64Encoder.MovLoad(m_text, Reg.RDX, Reg.R12, 0);
        X86_64Encoder.AddRR(m_text, Reg.RCX, Reg.RDX);
        X86_64Encoder.Jmp(m_text, mainLoop - (m_text.Count + 5));

        // No match: copy one byte
        PatchJcc(mismatch, m_text.Count);
        PatchJcc(noMatchOldEmpty, m_text.Count);
        PatchJcc(cantMatch, m_text.Count);
        X86_64Encoder.MovRR(m_text, Reg.RAX, Reg.RBX);
        X86_64Encoder.AddRR(m_text, Reg.RAX, Reg.RCX);
        X86_64Encoder.MovzxByte(m_text, Reg.RAX, Reg.RAX, 8);
        X86_64Encoder.MovRR(m_text, Reg.RDI, Reg.R14);
        X86_64Encoder.AddRR(m_text, Reg.RDI, Reg.R15);
        X86_64Encoder.MovStoreByte(m_text, Reg.RDI, Reg.RAX, 8);
        X86_64Encoder.AddRI(m_text, Reg.R15, 1);
        X86_64Encoder.AddRI(m_text, Reg.RCX, 1);
        X86_64Encoder.Jmp(m_text, mainLoop - (m_text.Count + 5));

        // Done: store length, bump heap
        PatchJcc(doneCheck, m_text.Count);
        X86_64Encoder.MovStore(m_text, Reg.R14, Reg.R15, 0);
        X86_64Encoder.MovRR(m_text, Reg.RAX, Reg.R15);
        X86_64Encoder.AddRI(m_text, Reg.RAX, 15);
        X86_64Encoder.AndRI(m_text, Reg.RAX, -8);
        X86_64Encoder.Lea(m_text, HeapReg, Reg.R14, 0);
        X86_64Encoder.AddRR(m_text, HeapReg, Reg.RAX);
        X86_64Encoder.MovRR(m_text, Reg.RAX, Reg.R14);

        X86_64Encoder.PopR(m_text, Reg.R15);
        X86_64Encoder.PopR(m_text, Reg.R14);
        X86_64Encoder.PopR(m_text, Reg.R13);
        X86_64Encoder.PopR(m_text, Reg.R12);
        X86_64Encoder.PopR(m_text, Reg.RBX);
        X86_64Encoder.Ret(m_text);
    }

    void EmitTextContainsHelper()
    {
        // __text_contains: rdi=text, rsi=needle → rax=1/0
        m_functionOffsets["__text_contains"] = m_text.Count;

        X86_64Encoder.MovLoad(m_text, Reg.RCX, Reg.RDI, 0); // text_len
        X86_64Encoder.MovLoad(m_text, Reg.RDX, Reg.RSI, 0); // needle_len
        X86_64Encoder.Li(m_text, Reg.R11, 0); // i = 0

        int searchLoop = m_text.Count;
        X86_64Encoder.MovRR(m_text, Reg.RAX, Reg.RCX);
        X86_64Encoder.SubRR(m_text, Reg.RAX, Reg.RDX);
        X86_64Encoder.AddRI(m_text, Reg.RAX, 1); // max start = text_len - needle_len + 1
        X86_64Encoder.CmpRR(m_text, Reg.R11, Reg.RAX);
        int notFound = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_GE, 0);

        // Compare text[i..i+needle_len] with needle
        X86_64Encoder.PushR(m_text, Reg.R11); // save i
        X86_64Encoder.Li(m_text, Reg.RAX, 0); // j = 0
        int cmpLoop = m_text.Count;
        X86_64Encoder.CmpRR(m_text, Reg.RAX, Reg.RDX);
        int found = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_GE, 0);

        X86_64Encoder.MovRR(m_text, Reg.R8, Reg.RDI);
        X86_64Encoder.AddRR(m_text, Reg.R8, Reg.R11);
        X86_64Encoder.AddRR(m_text, Reg.R8, Reg.RAX);
        X86_64Encoder.MovzxByte(m_text, Reg.R8, Reg.R8, 8);
        X86_64Encoder.MovRR(m_text, Reg.R9, Reg.RSI);
        X86_64Encoder.AddRR(m_text, Reg.R9, Reg.RAX);
        X86_64Encoder.MovzxByte(m_text, Reg.R9, Reg.R9, 8);
        X86_64Encoder.CmpRR(m_text, Reg.R8, Reg.R9);
        int byteMismatch = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_NE, 0);
        X86_64Encoder.AddRI(m_text, Reg.RAX, 1);
        X86_64Encoder.Jmp(m_text, cmpLoop - (m_text.Count + 5));

        PatchJcc(found, m_text.Count);
        X86_64Encoder.PopR(m_text, Reg.R11);
        X86_64Encoder.Li(m_text, Reg.RAX, 1);
        X86_64Encoder.Ret(m_text);

        PatchJcc(byteMismatch, m_text.Count);
        X86_64Encoder.PopR(m_text, Reg.R11);
        X86_64Encoder.AddRI(m_text, Reg.R11, 1);
        X86_64Encoder.Jmp(m_text, searchLoop - (m_text.Count + 5));

        PatchJcc(notFound, m_text.Count);
        X86_64Encoder.Li(m_text, Reg.RAX, 0);
        X86_64Encoder.Ret(m_text);
    }

    void EmitTextStartsWithHelper()
    {
        // __text_starts_with: rdi=text, rsi=prefix → rax=1/0
        m_functionOffsets["__text_starts_with"] = m_text.Count;

        X86_64Encoder.MovLoad(m_text, Reg.RCX, Reg.RDI, 0); // text_len
        X86_64Encoder.MovLoad(m_text, Reg.RDX, Reg.RSI, 0); // prefix_len

        // If prefix_len > text_len → false
        X86_64Encoder.CmpRR(m_text, Reg.RDX, Reg.RCX);
        int tooLong = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_G, 0);

        X86_64Encoder.Li(m_text, Reg.R11, 0);
        int loop = m_text.Count;
        X86_64Encoder.CmpRR(m_text, Reg.R11, Reg.RDX);
        int matched = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_GE, 0);

        X86_64Encoder.MovRR(m_text, Reg.RAX, Reg.RDI);
        X86_64Encoder.AddRR(m_text, Reg.RAX, Reg.R11);
        X86_64Encoder.MovzxByte(m_text, Reg.RAX, Reg.RAX, 8);
        X86_64Encoder.MovRR(m_text, Reg.R8, Reg.RSI);
        X86_64Encoder.AddRR(m_text, Reg.R8, Reg.R11);
        X86_64Encoder.MovzxByte(m_text, Reg.R8, Reg.R8, 8);
        X86_64Encoder.CmpRR(m_text, Reg.RAX, Reg.R8);
        int mismatch = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_NE, 0);
        X86_64Encoder.AddRI(m_text, Reg.R11, 1);
        X86_64Encoder.Jmp(m_text, loop - (m_text.Count + 5));

        PatchJcc(matched, m_text.Count);
        X86_64Encoder.Li(m_text, Reg.RAX, 1);
        X86_64Encoder.Ret(m_text);

        PatchJcc(tooLong, m_text.Count);
        PatchJcc(mismatch, m_text.Count);
        X86_64Encoder.Li(m_text, Reg.RAX, 0);
        X86_64Encoder.Ret(m_text);
    }

    void EmitEscapeTextHelper()
    {
        // __escape_text: rdi=old text ptr → rax=new text ptr
        m_functionOffsets["__escape_text"] = m_text.Count;

        // Null guard
        X86_64Encoder.TestRR(m_text, Reg.RDI, Reg.RDI);
        int notNullText = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_NE, 0);
        X86_64Encoder.Li(m_text, Reg.RAX, 0);
        X86_64Encoder.Ret(m_text);
        PatchJcc(notNullText, m_text.Count);

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

    void EmitTextCompareHelper()
    {
        // __text_compare: rdi=str1, rsi=str2 → rax=-1/0/1 (lexicographic on CCE bytes)
        m_functionOffsets["__text_compare"] = m_text.Count;

        X86_64Encoder.PushR(m_text, Reg.RBX);
        X86_64Encoder.PushR(m_text, Reg.R12);

        // RBX = min(len1, len2), R12 = len1, RCX = len2
        X86_64Encoder.MovLoad(m_text, Reg.R12, Reg.RDI, 0); // len1
        X86_64Encoder.MovLoad(m_text, Reg.RCX, Reg.RSI, 0); // len2
        X86_64Encoder.MovRR(m_text, Reg.RBX, Reg.R12);
        X86_64Encoder.CmpRR(m_text, Reg.RBX, Reg.RCX);
        int len1Smaller = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_LE, 0);
        X86_64Encoder.MovRR(m_text, Reg.RBX, Reg.RCX); // min = len2
        PatchJcc(len1Smaller, m_text.Count);

        // Compare bytes: i=0..min-1
        X86_64Encoder.Li(m_text, Reg.R11, 0); // i
        int cmpLoop = m_text.Count;
        X86_64Encoder.CmpRR(m_text, Reg.R11, Reg.RBX);
        int cmpDone = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_GE, 0);

        // Load byte from each string
        X86_64Encoder.MovRR(m_text, Reg.RAX, Reg.RDI);
        X86_64Encoder.AddRR(m_text, Reg.RAX, Reg.R11);
        X86_64Encoder.MovzxByte(m_text, Reg.RAX, Reg.RAX, 8); // str1[8+i]
        X86_64Encoder.MovRR(m_text, Reg.RDX, Reg.RSI);
        X86_64Encoder.AddRR(m_text, Reg.RDX, Reg.R11);
        X86_64Encoder.MovzxByte(m_text, Reg.RDX, Reg.RDX, 8); // str2[8+i]
        X86_64Encoder.CmpRR(m_text, Reg.RAX, Reg.RDX);
        int bytesEqual = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_E, 0);

        // Bytes differ: return -1 if a < b, +1 if a > b
        int aGreater = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_G, 0);
        X86_64Encoder.Li(m_text, Reg.RAX, -1);
        int retEarly1 = m_text.Count;
        X86_64Encoder.Jmp(m_text, 0);
        PatchJcc(aGreater, m_text.Count);
        X86_64Encoder.Li(m_text, Reg.RAX, 1);
        int retEarly2 = m_text.Count;
        X86_64Encoder.Jmp(m_text, 0);

        PatchJcc(bytesEqual, m_text.Count);
        X86_64Encoder.AddRI(m_text, Reg.R11, 1);
        X86_64Encoder.Jmp(m_text, cmpLoop - (m_text.Count + 5));

        // All min bytes equal — compare lengths
        PatchJcc(cmpDone, m_text.Count);
        X86_64Encoder.CmpRR(m_text, Reg.R12, Reg.RCX);
        int lenEqual = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_E, 0);
        int lenGreater = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_G, 0);
        X86_64Encoder.Li(m_text, Reg.RAX, -1);
        int retLen1 = m_text.Count;
        X86_64Encoder.Jmp(m_text, 0);
        PatchJcc(lenGreater, m_text.Count);
        X86_64Encoder.Li(m_text, Reg.RAX, 1);
        int retLen2 = m_text.Count;
        X86_64Encoder.Jmp(m_text, 0);
        PatchJcc(lenEqual, m_text.Count);
        X86_64Encoder.Li(m_text, Reg.RAX, 0);

        int retEnd = m_text.Count;
        PatchJmp(retEarly1, retEnd);
        PatchJmp(retEarly2, retEnd);
        PatchJmp(retLen1, retEnd);
        PatchJmp(retLen2, retEnd);

        X86_64Encoder.PopR(m_text, Reg.R12);
        X86_64Encoder.PopR(m_text, Reg.RBX);
        X86_64Encoder.Ret(m_text);
    }

    void EmitListSnocHelper()
    {
        // __list_snoc: rdi=list_ptr, rsi=element → rax=new list with element appended
        m_functionOffsets["__list_snoc"] = m_text.Count;

        X86_64Encoder.PushR(m_text, Reg.RBX);
        X86_64Encoder.PushR(m_text, Reg.R12);

        X86_64Encoder.MovRR(m_text, Reg.RBX, Reg.RDI);  // RBX = old list
        X86_64Encoder.MovRR(m_text, Reg.R12, Reg.RSI);   // R12 = new element
        X86_64Encoder.MovLoad(m_text, Reg.RCX, Reg.RBX, 0); // RCX = old length

        // Allocate (oldLen + 1 + 1) * 8 = (len+2)*8 bytes
        X86_64Encoder.MovRR(m_text, Reg.RAX, HeapReg);   // result ptr
        X86_64Encoder.MovRR(m_text, Reg.RDX, Reg.RCX);
        X86_64Encoder.AddRI(m_text, Reg.RDX, 2);
        X86_64Encoder.ShlRI(m_text, Reg.RDX, 3);
        X86_64Encoder.AddRR(m_text, HeapReg, Reg.RDX);

        // Store new length
        X86_64Encoder.MovRR(m_text, Reg.RDX, Reg.RCX);
        X86_64Encoder.AddRI(m_text, Reg.RDX, 1);
        X86_64Encoder.MovStore(m_text, Reg.RAX, Reg.RDX, 0);

        // Copy old elements: for i in 0..oldLen-1: new[8+i*8] = old[8+i*8]
        X86_64Encoder.Li(m_text, Reg.R11, 0);
        int copyLoop = m_text.Count;
        X86_64Encoder.CmpRR(m_text, Reg.R11, Reg.RCX);
        int copyDone = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_GE, 0);
        // Load old[8 + i*8]
        X86_64Encoder.MovRR(m_text, Reg.RDX, Reg.R11);
        X86_64Encoder.ShlRI(m_text, Reg.RDX, 3);
        X86_64Encoder.MovRR(m_text, Reg.RSI, Reg.RBX);
        X86_64Encoder.AddRR(m_text, Reg.RSI, Reg.RDX);
        X86_64Encoder.MovLoad(m_text, Reg.RSI, Reg.RSI, 8);
        // Store to new[8 + i*8]
        X86_64Encoder.MovRR(m_text, Reg.RDI, Reg.RAX);
        X86_64Encoder.AddRR(m_text, Reg.RDI, Reg.RDX);
        X86_64Encoder.MovStore(m_text, Reg.RDI, Reg.RSI, 8);
        X86_64Encoder.AddRI(m_text, Reg.R11, 1);
        X86_64Encoder.Jmp(m_text, copyLoop - (m_text.Count + 5));
        PatchJcc(copyDone, m_text.Count);

        // Store new element at new[8 + oldLen*8]
        X86_64Encoder.MovRR(m_text, Reg.RDX, Reg.RCX);
        X86_64Encoder.ShlRI(m_text, Reg.RDX, 3);
        X86_64Encoder.MovRR(m_text, Reg.RDI, Reg.RAX);
        X86_64Encoder.AddRR(m_text, Reg.RDI, Reg.RDX);
        X86_64Encoder.MovStore(m_text, Reg.RDI, Reg.R12, 8);

        X86_64Encoder.PopR(m_text, Reg.R12);
        X86_64Encoder.PopR(m_text, Reg.RBX);
        X86_64Encoder.Ret(m_text);
    }

    void EmitListInsertAtHelper()
    {
        // __list_insert_at: rdi=list, rsi=index, rdx=element → rax=new list
        m_functionOffsets["__list_insert_at"] = m_text.Count;

        X86_64Encoder.PushR(m_text, Reg.RBX);
        X86_64Encoder.PushR(m_text, Reg.R12);
        X86_64Encoder.PushR(m_text, Reg.R13);
        X86_64Encoder.PushR(m_text, Reg.R14);

        X86_64Encoder.MovRR(m_text, Reg.RBX, Reg.RDI);  // old list
        X86_64Encoder.MovRR(m_text, Reg.R12, Reg.RSI);   // index
        X86_64Encoder.MovRR(m_text, Reg.R13, Reg.RDX);   // element
        X86_64Encoder.MovLoad(m_text, Reg.R14, Reg.RBX, 0); // old length

        // Allocate (oldLen + 2) * 8
        X86_64Encoder.MovRR(m_text, Reg.RAX, HeapReg);
        X86_64Encoder.MovRR(m_text, Reg.RDX, Reg.R14);
        X86_64Encoder.AddRI(m_text, Reg.RDX, 2);
        X86_64Encoder.ShlRI(m_text, Reg.RDX, 3);
        X86_64Encoder.AddRR(m_text, HeapReg, Reg.RDX);

        // Store new length = oldLen + 1
        X86_64Encoder.MovRR(m_text, Reg.RDX, Reg.R14);
        X86_64Encoder.AddRI(m_text, Reg.RDX, 1);
        X86_64Encoder.MovStore(m_text, Reg.RAX, Reg.RDX, 0);

        // Copy elements before index: 0..index-1
        X86_64Encoder.Li(m_text, Reg.R11, 0);
        int preCopy = m_text.Count;
        X86_64Encoder.CmpRR(m_text, Reg.R11, Reg.R12);
        int preDone = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_GE, 0);
        X86_64Encoder.MovRR(m_text, Reg.RDX, Reg.R11);
        X86_64Encoder.ShlRI(m_text, Reg.RDX, 3);
        X86_64Encoder.MovRR(m_text, Reg.RSI, Reg.RBX);
        X86_64Encoder.AddRR(m_text, Reg.RSI, Reg.RDX);
        X86_64Encoder.MovLoad(m_text, Reg.RCX, Reg.RSI, 8);
        X86_64Encoder.MovRR(m_text, Reg.RDI, Reg.RAX);
        X86_64Encoder.AddRR(m_text, Reg.RDI, Reg.RDX);
        X86_64Encoder.MovStore(m_text, Reg.RDI, Reg.RCX, 8);
        X86_64Encoder.AddRI(m_text, Reg.R11, 1);
        X86_64Encoder.Jmp(m_text, preCopy - (m_text.Count + 5));
        PatchJcc(preDone, m_text.Count);

        // Store inserted element at new[8 + index*8]
        X86_64Encoder.MovRR(m_text, Reg.RDX, Reg.R12);
        X86_64Encoder.ShlRI(m_text, Reg.RDX, 3);
        X86_64Encoder.MovRR(m_text, Reg.RDI, Reg.RAX);
        X86_64Encoder.AddRR(m_text, Reg.RDI, Reg.RDX);
        X86_64Encoder.MovStore(m_text, Reg.RDI, Reg.R13, 8);

        // Copy elements after index: index..oldLen-1 → positions index+1..
        X86_64Encoder.MovRR(m_text, Reg.R11, Reg.R12); // i = index
        int postCopy = m_text.Count;
        X86_64Encoder.CmpRR(m_text, Reg.R11, Reg.R14);
        int postDone = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_GE, 0);
        // old[8 + i*8]
        X86_64Encoder.MovRR(m_text, Reg.RDX, Reg.R11);
        X86_64Encoder.ShlRI(m_text, Reg.RDX, 3);
        X86_64Encoder.MovRR(m_text, Reg.RSI, Reg.RBX);
        X86_64Encoder.AddRR(m_text, Reg.RSI, Reg.RDX);
        X86_64Encoder.MovLoad(m_text, Reg.RCX, Reg.RSI, 8);
        // new[8 + (i+1)*8]
        X86_64Encoder.AddRI(m_text, Reg.RDX, 8);
        X86_64Encoder.MovRR(m_text, Reg.RDI, Reg.RAX);
        X86_64Encoder.AddRR(m_text, Reg.RDI, Reg.RDX);
        X86_64Encoder.MovStore(m_text, Reg.RDI, Reg.RCX, 8);
        X86_64Encoder.AddRI(m_text, Reg.R11, 1);
        X86_64Encoder.Jmp(m_text, postCopy - (m_text.Count + 5));
        PatchJcc(postDone, m_text.Count);

        X86_64Encoder.PopR(m_text, Reg.R14);
        X86_64Encoder.PopR(m_text, Reg.R13);
        X86_64Encoder.PopR(m_text, Reg.R12);
        X86_64Encoder.PopR(m_text, Reg.RBX);
        X86_64Encoder.Ret(m_text);
    }

    void EmitListContainsHelper()
    {
        // __list_contains: rdi=list, rsi=element → rax=1 if found, 0 if not
        // Uses __str_eq for text elements (pointer comparison + content equality)
        m_functionOffsets["__list_contains"] = m_text.Count;

        X86_64Encoder.MovLoad(m_text, Reg.RCX, Reg.RDI, 0); // length
        X86_64Encoder.Li(m_text, Reg.R11, 0); // index

        int loop = m_text.Count;
        X86_64Encoder.CmpRR(m_text, Reg.R11, Reg.RCX);
        int notFound = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_GE, 0);

        // Load element at [list + 8 + i*8]
        X86_64Encoder.MovRR(m_text, Reg.RAX, Reg.R11);
        X86_64Encoder.ShlRI(m_text, Reg.RAX, 3);
        X86_64Encoder.AddRR(m_text, Reg.RAX, Reg.RDI);
        X86_64Encoder.MovLoad(m_text, Reg.RAX, Reg.RAX, 8);

        // Compare with target element
        X86_64Encoder.CmpRR(m_text, Reg.RAX, Reg.RSI);
        int found = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_E, 0);

        X86_64Encoder.AddRI(m_text, Reg.R11, 1);
        X86_64Encoder.Jmp(m_text, loop - (m_text.Count + 5));

        PatchJcc(found, m_text.Count);
        X86_64Encoder.Li(m_text, Reg.RAX, 1);
        X86_64Encoder.Ret(m_text);

        PatchJcc(notFound, m_text.Count);
        X86_64Encoder.Li(m_text, Reg.RAX, 0);
        X86_64Encoder.Ret(m_text);
    }

    void EmitTextConcatListHelper()
    {
        // __text_concat_list: rdi=list of text ptrs → rax=concatenated text
        m_functionOffsets["__text_concat_list"] = m_text.Count;

        X86_64Encoder.PushR(m_text, Reg.RBX);
        X86_64Encoder.PushR(m_text, Reg.R12);
        X86_64Encoder.PushR(m_text, Reg.R13);
        X86_64Encoder.PushR(m_text, Reg.R14);

        X86_64Encoder.MovRR(m_text, Reg.RBX, Reg.RDI);      // list ptr
        X86_64Encoder.MovLoad(m_text, Reg.R12, Reg.RBX, 0);  // list length

        // Pass 1: compute total byte length
        X86_64Encoder.Li(m_text, Reg.R13, 0); // total bytes
        X86_64Encoder.Li(m_text, Reg.R11, 0); // index
        int lenLoop = m_text.Count;
        X86_64Encoder.CmpRR(m_text, Reg.R11, Reg.R12);
        int lenDone = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_GE, 0);
        X86_64Encoder.MovRR(m_text, Reg.RAX, Reg.R11);
        X86_64Encoder.ShlRI(m_text, Reg.RAX, 3);
        X86_64Encoder.AddRR(m_text, Reg.RAX, Reg.RBX);
        X86_64Encoder.MovLoad(m_text, Reg.RAX, Reg.RAX, 8);   // str ptr
        X86_64Encoder.MovLoad(m_text, Reg.RAX, Reg.RAX, 0);   // str length
        X86_64Encoder.AddRR(m_text, Reg.R13, Reg.RAX);
        X86_64Encoder.AddRI(m_text, Reg.R11, 1);
        X86_64Encoder.Jmp(m_text, lenLoop - (m_text.Count + 5));
        PatchJcc(lenDone, m_text.Count);

        // Allocate result: 8 + align8(total)
        X86_64Encoder.MovRR(m_text, Reg.R14, HeapReg);         // result ptr
        X86_64Encoder.MovStore(m_text, Reg.R14, Reg.R13, 0);   // store total length
        X86_64Encoder.MovRR(m_text, Reg.RAX, Reg.R13);
        X86_64Encoder.AddRI(m_text, Reg.RAX, 15);
        X86_64Encoder.AndRI(m_text, Reg.RAX, -8);
        X86_64Encoder.AddRR(m_text, HeapReg, Reg.RAX);

        // Pass 2: copy bytes from each string
        X86_64Encoder.Li(m_text, Reg.R11, 0);  // list index
        X86_64Encoder.Li(m_text, Reg.R13, 0);  // dest offset
        int copyLoop = m_text.Count;
        X86_64Encoder.CmpRR(m_text, Reg.R11, Reg.R12);
        int copyDone = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_GE, 0);

        // Load string ptr from list
        X86_64Encoder.MovRR(m_text, Reg.RAX, Reg.R11);
        X86_64Encoder.ShlRI(m_text, Reg.RAX, 3);
        X86_64Encoder.AddRR(m_text, Reg.RAX, Reg.RBX);
        X86_64Encoder.MovLoad(m_text, Reg.RDI, Reg.RAX, 8);   // str ptr
        X86_64Encoder.MovLoad(m_text, Reg.RCX, Reg.RDI, 0);   // str len

        // Copy bytes: for j=0..strLen-1: dst[8+destOff+j] = src[8+j]
        X86_64Encoder.Li(m_text, Reg.RSI, 0);  // j
        int byteLoop = m_text.Count;
        X86_64Encoder.CmpRR(m_text, Reg.RSI, Reg.RCX);
        int byteDone = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_GE, 0);
        // Load src byte
        X86_64Encoder.MovRR(m_text, Reg.RAX, Reg.RDI);
        X86_64Encoder.AddRR(m_text, Reg.RAX, Reg.RSI);
        X86_64Encoder.MovzxByte(m_text, Reg.RAX, Reg.RAX, 8);
        // Store at dst[8 + destOff + j]
        X86_64Encoder.MovRR(m_text, Reg.RDX, Reg.R14);
        X86_64Encoder.AddRR(m_text, Reg.RDX, Reg.R13);
        X86_64Encoder.AddRR(m_text, Reg.RDX, Reg.RSI);
        X86_64Encoder.MovStoreByte(m_text, Reg.RDX, Reg.RAX, 8);
        X86_64Encoder.AddRI(m_text, Reg.RSI, 1);
        X86_64Encoder.Jmp(m_text, byteLoop - (m_text.Count + 5));
        PatchJcc(byteDone, m_text.Count);

        X86_64Encoder.AddRR(m_text, Reg.R13, Reg.RCX); // destOff += strLen
        X86_64Encoder.AddRI(m_text, Reg.R11, 1);
        X86_64Encoder.Jmp(m_text, copyLoop - (m_text.Count + 5));
        PatchJcc(copyDone, m_text.Count);

        X86_64Encoder.MovRR(m_text, Reg.RAX, Reg.R14);

        X86_64Encoder.PopR(m_text, Reg.R14);
        X86_64Encoder.PopR(m_text, Reg.R13);
        X86_64Encoder.PopR(m_text, Reg.R12);
        X86_64Encoder.PopR(m_text, Reg.RBX);
        X86_64Encoder.Ret(m_text);
    }

    void EmitTextSplitHelper()
    {
        // __text_split: rdi=text, rsi=delimiter → rax=list of text ptrs
        // Simple single-char delimiter split (delimiter is a 1-byte string)
        m_functionOffsets["__text_split"] = m_text.Count;

        X86_64Encoder.PushR(m_text, Reg.RBX);
        X86_64Encoder.PushR(m_text, Reg.R12);
        X86_64Encoder.PushR(m_text, Reg.R13);
        X86_64Encoder.PushR(m_text, Reg.R14);
        X86_64Encoder.PushR(m_text, Reg.R15);

        X86_64Encoder.MovRR(m_text, Reg.RBX, Reg.RDI);        // text ptr
        X86_64Encoder.MovLoad(m_text, Reg.R12, Reg.RBX, 0);   // text length
        // Load delimiter byte (first byte of delimiter string)
        X86_64Encoder.MovzxByte(m_text, Reg.R13, Reg.RSI, 8);  // delim byte

        // R14 = result list start on heap (we'll build it incrementally)
        X86_64Encoder.MovRR(m_text, Reg.R14, HeapReg);
        // Reserve space for max possible segments (textLen+1 elements + length slot)
        X86_64Encoder.MovRR(m_text, Reg.RAX, Reg.R12);
        X86_64Encoder.AddRI(m_text, Reg.RAX, 2);
        X86_64Encoder.ShlRI(m_text, Reg.RAX, 3);
        X86_64Encoder.AddRR(m_text, HeapReg, Reg.RAX);

        X86_64Encoder.Li(m_text, Reg.R15, 0);  // segment count
        X86_64Encoder.Li(m_text, Reg.R11, 0);  // scan position i
        X86_64Encoder.MovRR(m_text, Reg.RCX, Reg.R11); // segment start

        int scanLoop = m_text.Count;
        X86_64Encoder.CmpRR(m_text, Reg.R11, Reg.R12);
        int scanDone = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_GE, 0);

        // Load byte at text[8+i]
        X86_64Encoder.MovRR(m_text, Reg.RAX, Reg.RBX);
        X86_64Encoder.AddRR(m_text, Reg.RAX, Reg.R11);
        X86_64Encoder.MovzxByte(m_text, Reg.RAX, Reg.RAX, 8);
        X86_64Encoder.CmpRR(m_text, Reg.RAX, Reg.R13);
        int notDelim = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_NE, 0);

        // Found delimiter at i — emit segment [start..i)
        // Segment length = i - start
        X86_64Encoder.MovRR(m_text, Reg.RAX, Reg.R11);
        X86_64Encoder.SubRR(m_text, Reg.RAX, Reg.RCX); // segLen = i - start
        // Allocate segment string on heap
        X86_64Encoder.MovRR(m_text, Reg.RDI, HeapReg);  // segment ptr
        X86_64Encoder.MovStore(m_text, Reg.RDI, Reg.RAX, 0); // store length
        X86_64Encoder.PushR(m_text, Reg.RAX); // save segLen
        X86_64Encoder.AddRI(m_text, Reg.RAX, 15);
        X86_64Encoder.AndRI(m_text, Reg.RAX, -8);
        X86_64Encoder.AddRR(m_text, HeapReg, Reg.RAX);
        X86_64Encoder.PopR(m_text, Reg.RAX); // restore segLen

        // Copy bytes: text[8+start..8+i) → seg[8..]
        X86_64Encoder.PushR(m_text, Reg.R11); // save scan pos
        X86_64Encoder.Li(m_text, Reg.RSI, 0); // j
        int segCopy = m_text.Count;
        X86_64Encoder.CmpRR(m_text, Reg.RSI, Reg.RAX);
        int segCopyDone = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_GE, 0);
        X86_64Encoder.MovRR(m_text, Reg.RDX, Reg.RBX);
        X86_64Encoder.AddRR(m_text, Reg.RDX, Reg.RCX);
        X86_64Encoder.AddRR(m_text, Reg.RDX, Reg.RSI);
        X86_64Encoder.MovzxByte(m_text, Reg.RDX, Reg.RDX, 8);
        X86_64Encoder.MovRR(m_text, Reg.R11, Reg.RDI);
        X86_64Encoder.AddRR(m_text, Reg.R11, Reg.RSI);
        X86_64Encoder.MovStoreByte(m_text, Reg.R11, Reg.RDX, 8);
        X86_64Encoder.AddRI(m_text, Reg.RSI, 1);
        X86_64Encoder.Jmp(m_text, segCopy - (m_text.Count + 5));
        PatchJcc(segCopyDone, m_text.Count);
        X86_64Encoder.PopR(m_text, Reg.R11); // restore scan pos

        // Store segment ptr in result list
        X86_64Encoder.MovRR(m_text, Reg.RAX, Reg.R15);
        X86_64Encoder.ShlRI(m_text, Reg.RAX, 3);
        X86_64Encoder.AddRR(m_text, Reg.RAX, Reg.R14);
        X86_64Encoder.MovStore(m_text, Reg.RAX, Reg.RDI, 8);
        X86_64Encoder.AddRI(m_text, Reg.R15, 1);

        // Advance past delimiter
        X86_64Encoder.AddRI(m_text, Reg.R11, 1);
        X86_64Encoder.MovRR(m_text, Reg.RCX, Reg.R11); // new segment start
        X86_64Encoder.Jmp(m_text, scanLoop - (m_text.Count + 5));

        PatchJcc(notDelim, m_text.Count);
        X86_64Encoder.AddRI(m_text, Reg.R11, 1);
        X86_64Encoder.Jmp(m_text, scanLoop - (m_text.Count + 5));

        // End of string — emit final segment [start..len)
        PatchJcc(scanDone, m_text.Count);
        X86_64Encoder.MovRR(m_text, Reg.RAX, Reg.R12);
        X86_64Encoder.SubRR(m_text, Reg.RAX, Reg.RCX); // segLen
        X86_64Encoder.MovRR(m_text, Reg.RDI, HeapReg);
        X86_64Encoder.MovStore(m_text, Reg.RDI, Reg.RAX, 0);
        X86_64Encoder.PushR(m_text, Reg.RAX);
        X86_64Encoder.AddRI(m_text, Reg.RAX, 15);
        X86_64Encoder.AndRI(m_text, Reg.RAX, -8);
        X86_64Encoder.AddRR(m_text, HeapReg, Reg.RAX);
        X86_64Encoder.PopR(m_text, Reg.RAX);

        X86_64Encoder.Li(m_text, Reg.RSI, 0);
        int lastCopy = m_text.Count;
        X86_64Encoder.CmpRR(m_text, Reg.RSI, Reg.RAX);
        int lastCopyDone = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_GE, 0);
        X86_64Encoder.MovRR(m_text, Reg.RDX, Reg.RBX);
        X86_64Encoder.AddRR(m_text, Reg.RDX, Reg.RCX);
        X86_64Encoder.AddRR(m_text, Reg.RDX, Reg.RSI);
        X86_64Encoder.MovzxByte(m_text, Reg.RDX, Reg.RDX, 8);
        X86_64Encoder.MovRR(m_text, Reg.R11, Reg.RDI);
        X86_64Encoder.AddRR(m_text, Reg.R11, Reg.RSI);
        X86_64Encoder.MovStoreByte(m_text, Reg.R11, Reg.RDX, 8);
        X86_64Encoder.AddRI(m_text, Reg.RSI, 1);
        X86_64Encoder.Jmp(m_text, lastCopy - (m_text.Count + 5));
        PatchJcc(lastCopyDone, m_text.Count);

        // Store last segment in result list
        X86_64Encoder.MovRR(m_text, Reg.RAX, Reg.R15);
        X86_64Encoder.ShlRI(m_text, Reg.RAX, 3);
        X86_64Encoder.AddRR(m_text, Reg.RAX, Reg.R14);
        X86_64Encoder.MovStore(m_text, Reg.RAX, Reg.RDI, 8);
        X86_64Encoder.AddRI(m_text, Reg.R15, 1);

        // Store segment count in result list
        X86_64Encoder.MovStore(m_text, Reg.R14, Reg.R15, 0);
        X86_64Encoder.MovRR(m_text, Reg.RAX, Reg.R14);

        X86_64Encoder.PopR(m_text, Reg.R15);
        X86_64Encoder.PopR(m_text, Reg.R14);
        X86_64Encoder.PopR(m_text, Reg.R13);
        X86_64Encoder.PopR(m_text, Reg.R12);
        X86_64Encoder.PopR(m_text, Reg.RBX);
        X86_64Encoder.Ret(m_text);
    }

    void EmitIpowHelper()
    {
        // __ipow: rdi=base, rsi=exponent → rax=base^exponent
        // Exponentiation by squaring. Negative exponents → 0 (integer math).
        m_functionOffsets["__ipow"] = m_text.Count;

        // result = 1
        X86_64Encoder.Li(m_text, Reg.RAX, 1);

        // if exponent <= 0, skip (return 1 for exp==0, 0 for exp<0 is wrong but
        // we handle exp<0 separately)
        X86_64Encoder.CmpRI(m_text, Reg.RSI, 0);
        int jmpNeg = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_L, 0);  // exp < 0 → return 0
        int jmpZero = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_E, 0);   // exp == 0 → return 1

        // Loop: while exponent > 0
        int loopTop = m_text.Count;

        // if (exponent & 1) result *= base
        X86_64Encoder.MovRR(m_text, Reg.RCX, Reg.RSI);
        X86_64Encoder.AndRI(m_text, Reg.RCX, 1);
        int skipMul = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_E, 0);
        X86_64Encoder.ImulRR(m_text, Reg.RAX, Reg.RDI);     // result *= base
        PatchJcc(skipMul, m_text.Count);

        // base *= base
        X86_64Encoder.ImulRR(m_text, Reg.RDI, Reg.RDI);
        // exponent >>= 1
        X86_64Encoder.ShrRI(m_text, Reg.RSI, 1);

        // if exponent > 0, loop
        X86_64Encoder.CmpRI(m_text, Reg.RSI, 0);
        int jmpLoop = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_G, 0);
        PatchJcc(jmpLoop, loopTop);

        // Return result (already in RAX)
        int done = m_text.Count;
        PatchJcc(jmpZero, done);
        X86_64Encoder.Ret(m_text);

        // Negative exponent → return 0
        int negPath = m_text.Count;
        PatchJcc(jmpNeg, negPath);
        X86_64Encoder.Li(m_text, Reg.RAX, 0);
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
        X86_64Encoder.CmpRI(m_text, Reg.RDX, 73); // CCE '-'
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
        X86_64Encoder.SubRI(m_text, Reg.RDX, 3); // CCE '0' = 3
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
        X86_64Encoder.AddRI(m_text, Reg.R13, 23);             // skip path + null + round up
        X86_64Encoder.AndRI(m_text, Reg.R13, -8);              // align down to 8 bytes
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

        // Store length, bump heap past BOTH filename scratch AND text content
        // R13 = text header (already past filename copy + padding)
        // HeapReg = R13 + 8 (length prefix) + align8(content_len)
        X86_64Encoder.MovStore(m_text, Reg.R13, Reg.RBX, 0);
        X86_64Encoder.MovRR(m_text, Reg.RAX, Reg.RBX);
        X86_64Encoder.AddRI(m_text, Reg.RAX, 15);
        X86_64Encoder.AndRI(m_text, Reg.RAX, -8);
        X86_64Encoder.AddRI(m_text, Reg.RAX, 8);
        X86_64Encoder.Lea(m_text, HeapReg, Reg.R13, 0);       // HeapReg = R13 (past filename)
        X86_64Encoder.AddRR(m_text, HeapReg, Reg.RAX);         // + text allocation
        X86_64Encoder.MovRR(m_text, Reg.RAX, Reg.R13);         // return result ptr

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
        X86_64Encoder.PushR(m_text, Reg.R12);

        // Load Unicode→CCE table address into R12 (input boundary conversion)
        m_rodataFixups.Add(new RodataFixup(m_text.Count + 2, m_unicodeToCceTableOffset));
        X86_64Encoder.MovRI64(m_text, Reg.R12, 0); // patched to rodata+table

        // Read one byte at a time into heap buffer
        X86_64Encoder.MovRR(m_text, Reg.RBX, HeapReg);
        X86_64Encoder.Li(m_text, Reg.RCX, 0);  // length counter

        int readByte = m_text.Count;
        X86_64Encoder.PushR(m_text, Reg.RCX);   // save counter

        if (m_target == X86_64Target.BareMetal)
        {
            // Bare metal: poll COM1 (0x3FD bit 0) until data ready, read from 0x3F8
            int pollLoop = m_text.Count;
            X86_64Encoder.Li(m_text, Reg.RDX, 0x3FD); // line status register
            X86_64Encoder.InAlDx(m_text);               // AL = status
            X86_64Encoder.Li(m_text, Reg.R11, 1);
            X86_64Encoder.AndRR(m_text, Reg.RAX, Reg.R11); // test bit 0 (data ready)
            X86_64Encoder.TestRR(m_text, Reg.RAX, Reg.RAX);
            int dataReady = m_text.Count;
            X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_NE, 0); // got data
            X86_64Encoder.Hlt(m_text); // wait for interrupt (saves power)
            X86_64Encoder.Jmp(m_text, pollLoop - (m_text.Count + 5));
            PatchJcc(dataReady, m_text.Count);

            // Read the byte
            X86_64Encoder.Li(m_text, Reg.RDX, 0x3F8);
            X86_64Encoder.InAlDx(m_text); // AL = received byte

            // Store it: heap[8 + counter]
            X86_64Encoder.PopR(m_text, Reg.RCX);
            X86_64Encoder.PushR(m_text, Reg.RCX);
            X86_64Encoder.MovRR(m_text, Reg.RSI, Reg.RBX);
            X86_64Encoder.AddRR(m_text, Reg.RSI, Reg.RCX);
            X86_64Encoder.AddRI(m_text, Reg.RSI, 8);
            // mov byte [rsi], al
            m_text.Add(0x88); m_text.Add(0x06); // mov [rsi], al
            X86_64Encoder.Li(m_text, Reg.RAX, 1); // return 1 (bytes read)
        }
        else
        {
            // Linux: read(0, heap+8+rcx, 1)
            X86_64Encoder.Li(m_text, Reg.RDI, 0);   // stdin
            X86_64Encoder.Lea(m_text, Reg.RSI, Reg.RBX, 8);
            X86_64Encoder.PopR(m_text, Reg.RCX);
            X86_64Encoder.PushR(m_text, Reg.RCX);
            X86_64Encoder.AddRR(m_text, Reg.RSI, Reg.RCX);
            X86_64Encoder.Li(m_text, Reg.RDX, 1);
            X86_64Encoder.Li(m_text, Reg.RAX, 0);   // SYS_read
            X86_64Encoder.Syscall(m_text);
        }

        X86_64Encoder.PopR(m_text, Reg.RCX);

        // Check EOF
        X86_64Encoder.TestRR(m_text, Reg.RAX, Reg.RAX);
        int eof = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_LE, 0);

        // Check newline (compare against Unicode '\n' before CCE conversion)
        X86_64Encoder.MovRR(m_text, Reg.RDX, Reg.RBX);
        X86_64Encoder.AddRR(m_text, Reg.RDX, Reg.RCX);
        X86_64Encoder.MovzxByte(m_text, Reg.RDX, Reg.RDX, 8);
        X86_64Encoder.CmpRI(m_text, Reg.RDX, '\n');
        int gotNl = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_E, 0);

        // Convert stored byte Unicode→CCE in-place: buf[8 + count]
        X86_64Encoder.MovRR(m_text, Reg.RSI, Reg.RBX);
        X86_64Encoder.AddRR(m_text, Reg.RSI, Reg.RCX);
        X86_64Encoder.AddRI(m_text, Reg.RSI, 8);           // RSI = &buf[8+count]
        X86_64Encoder.MovzxByte(m_text, Reg.RAX, Reg.RSI, 0); // RAX = Unicode byte
        X86_64Encoder.AddRR(m_text, Reg.RAX, Reg.R12);
        X86_64Encoder.MovzxByte(m_text, Reg.RAX, Reg.RAX, 0); // RAX = CCE byte
        m_text.Add(0x88); m_text.Add(0x06); // mov [rsi], al — store CCE byte back

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

        X86_64Encoder.PopR(m_text, Reg.R12);
        X86_64Encoder.PopR(m_text, Reg.RBX);
        X86_64Encoder.Ret(m_text);
    }

    void EmitBareMetalReadSerialHelper()
    {
        // __bare_metal_read_serial: → rax=string ptr (reads COM1 until \x04 EOT)
        // Returns a length-prefixed string on heap: [len:8][data...]
        m_functionOffsets["__bare_metal_read_serial"] = m_text.Count;

        X86_64Encoder.PushR(m_text, Reg.RBX);
        X86_64Encoder.PushR(m_text, Reg.RCX);
        X86_64Encoder.PushR(m_text, Reg.R12);

        // Load Unicode→CCE table address into R12 (input boundary conversion)
        m_rodataFixups.Add(new RodataFixup(m_text.Count + 2, m_unicodeToCceTableOffset));
        X86_64Encoder.MovRI64(m_text, Reg.R12, 0); // patched to rodata+table

        X86_64Encoder.MovRR(m_text, Reg.RBX, HeapReg); // RBX = buffer start
        X86_64Encoder.Li(m_text, Reg.RCX, 0);           // RCX = byte count

        // Read loop: poll COM1, read byte, check for EOT
        int readLoop = m_text.Count;

        // Poll: check LSR bit 0 (data ready) — busy-wait for QEMU serial speed
        int pollLoop = m_text.Count;
        X86_64Encoder.Li(m_text, Reg.RDX, 0x3FD);       // line status register
        X86_64Encoder.InAlDx(m_text);                     // AL = status
        X86_64Encoder.Li(m_text, Reg.R11, 1);
        X86_64Encoder.AndRR(m_text, Reg.RAX, Reg.R11);
        X86_64Encoder.TestRR(m_text, Reg.RAX, Reg.RAX);
        int dataReady = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_NE, 0);
        X86_64Encoder.Pause(m_text);                      // hint: spin-wait (saves power on real HW)
        X86_64Encoder.Jmp(m_text, pollLoop - (m_text.Count + 5));
        PatchJcc(dataReady, m_text.Count);

        // Read byte from COM1
        X86_64Encoder.Li(m_text, Reg.RDX, 0x3F8);
        X86_64Encoder.InAlDx(m_text);                     // AL = byte

        // Check for EOT (\x04) — end of source
        X86_64Encoder.CmpRI(m_text, Reg.RAX, 0x04);
        int gotEot = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_E, 0);

        // Also check for \0 as alternate terminator
        X86_64Encoder.TestRR(m_text, Reg.RAX, Reg.RAX);
        int gotNull = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_E, 0);

        // Convert Unicode→CCE: table[unicode_byte]
        X86_64Encoder.AddRR(m_text, Reg.RAX, Reg.R12);
        X86_64Encoder.MovzxByte(m_text, Reg.RAX, Reg.RAX, 0); // RAX = CCE byte

        // Store CCE byte: buffer[8 + count]
        X86_64Encoder.MovRR(m_text, Reg.RSI, Reg.RBX);
        X86_64Encoder.AddRR(m_text, Reg.RSI, Reg.RCX);
        X86_64Encoder.AddRI(m_text, Reg.RSI, 8);
        m_text.Add(0x88); m_text.Add(0x06); // mov [rsi], al

        X86_64Encoder.AddRI(m_text, Reg.RCX, 1);
        X86_64Encoder.Jmp(m_text, readLoop - (m_text.Count + 5));

        // Done: store length, bump heap, return
        PatchJcc(gotEot, m_text.Count);
        PatchJcc(gotNull, m_text.Count);

        X86_64Encoder.MovStore(m_text, Reg.RBX, Reg.RCX, 0); // [buf+0] = length
        X86_64Encoder.MovRR(m_text, Reg.RAX, Reg.RCX);
        X86_64Encoder.AddRI(m_text, Reg.RAX, 15);
        X86_64Encoder.AndRI(m_text, Reg.RAX, -8);
        X86_64Encoder.AddRI(m_text, Reg.RAX, 8);             // total = 8 + align8(len)
        X86_64Encoder.AddRR(m_text, HeapReg, Reg.RAX);        // bump heap
        X86_64Encoder.MovRR(m_text, Reg.RAX, Reg.RBX);        // return buffer ptr

        X86_64Encoder.PopR(m_text, Reg.R12);
        X86_64Encoder.PopR(m_text, Reg.RCX);
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
        // Drain queue — helpers may enqueue new types for nested fields
        while (m_escapeHelperQueue.Count > 0)
        {
            (string _, string name, CodexType type) = m_escapeHelperQueue.Dequeue();
            switch (type)
            {
                case TextType:
                    break; // __escape_text already emitted
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

    // All escape helpers: RDI = old ptr in, RAX = new ptr out
    // Working regs: RBX=old, R12=new, R13=extra1, R14=extra2
    // Frame: push rbx, r12, r13, r14 (32 bytes + alignment)

    void EmitEscapeHelperPrologue(string name)
    {
        m_functionOffsets[name] = m_text.Count;
        // Null guard: if rdi == 0, return 0 immediately (empty list/null field)
        X86_64Encoder.TestRR(m_text, Reg.RDI, Reg.RDI);
        int notNull = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_NE, 0);
        X86_64Encoder.Li(m_text, Reg.RAX, 0);
        X86_64Encoder.Ret(m_text);
        PatchJcc(notNull, m_text.Count);

        X86_64Encoder.PushR(m_text, Reg.RBX);
        X86_64Encoder.PushR(m_text, Reg.R12);
        X86_64Encoder.PushR(m_text, Reg.R13);
        X86_64Encoder.PushR(m_text, Reg.R14);
        X86_64Encoder.MovRR(m_text, Reg.RBX, Reg.RDI); // rbx = old ptr
    }

    void EmitEscapeHelperEpilogue()
    {
        X86_64Encoder.MovRR(m_text, Reg.RAX, Reg.R12); // return new ptr
        X86_64Encoder.PopR(m_text, Reg.R14);
        X86_64Encoder.PopR(m_text, Reg.R13);
        X86_64Encoder.PopR(m_text, Reg.R12);
        X86_64Encoder.PopR(m_text, Reg.RBX);
        X86_64Encoder.Ret(m_text);
    }

    void EmitEscapeFieldCopy(int srcOffset, int dstOffset, CodexType fieldType)
    {
        CodexType resolved = ResolveType(fieldType);
        if (IRRegion.TypeNeedsHeapEscape(resolved))
        {
            string helper = GetOrQueueEscapeHelper(resolved);
            X86_64Encoder.MovLoad(m_text, Reg.RDI, Reg.RBX, srcOffset);
            EmitCallTo(helper);
            X86_64Encoder.MovStore(m_text, Reg.R12, Reg.RAX, dstOffset);
        }
        else
        {
            X86_64Encoder.MovLoad(m_text, Reg.RAX, Reg.RBX, srcOffset);
            X86_64Encoder.MovStore(m_text, Reg.R12, Reg.RAX, dstOffset);
        }
    }

    void EmitRecordEscapeHelper(string name, RecordType rt)
    {
        EmitEscapeHelperPrologue(name);

        int totalSize = rt.Fields.Length * 8;
        X86_64Encoder.MovRR(m_text, Reg.R12, HeapReg);
        X86_64Encoder.AddRI(m_text, HeapReg, totalSize);

        for (int i = 0; i < rt.Fields.Length; i++)
            EmitEscapeFieldCopy(i * 8, i * 8, rt.Fields[i].Type);

        EmitEscapeHelperEpilogue();
    }

    void EmitListEscapeHelper(string name, ListType lt)
    {
        EmitEscapeHelperPrologue(name);

        // r13 = length
        X86_64Encoder.MovLoad(m_text, Reg.R13, Reg.RBX, 0);
        // totalSize = (1 + len) * 8
        X86_64Encoder.MovRR(m_text, Reg.RAX, Reg.R13);
        X86_64Encoder.AddRI(m_text, Reg.RAX, 1);
        X86_64Encoder.ShlRI(m_text, Reg.RAX, 3);
        X86_64Encoder.MovRR(m_text, Reg.R12, HeapReg);
        X86_64Encoder.AddRR(m_text, HeapReg, Reg.RAX);
        // Store length
        X86_64Encoder.MovStore(m_text, Reg.R12, Reg.R13, 0);

        CodexType elemType = ResolveType(lt.Element);
        bool deepCopy = IRRegion.TypeNeedsHeapEscape(elemType);
        string? elemHelper = deepCopy ? GetOrQueueEscapeHelper(elemType) : null;

        // r14 = index = 0
        X86_64Encoder.Li(m_text, Reg.R14, 0);
        int loopStart = m_text.Count;
        X86_64Encoder.CmpRR(m_text, Reg.R14, Reg.R13);
        int exitIdx = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_GE, 0);

        // Load element
        X86_64Encoder.MovRR(m_text, Reg.RAX, Reg.R14);
        X86_64Encoder.ShlRI(m_text, Reg.RAX, 3);
        X86_64Encoder.AddRR(m_text, Reg.RAX, Reg.RBX);
        X86_64Encoder.MovLoad(m_text, Reg.RDI, Reg.RAX, 8);

        if (deepCopy)
        {
            EmitCallTo(elemHelper!);
            // rax = copied element
        }
        else
        {
            X86_64Encoder.MovRR(m_text, Reg.RAX, Reg.RDI);
        }

        // Store to new list
        X86_64Encoder.MovRR(m_text, Reg.RDI, Reg.R14);
        X86_64Encoder.ShlRI(m_text, Reg.RDI, 3);
        X86_64Encoder.AddRR(m_text, Reg.RDI, Reg.R12);
        X86_64Encoder.MovStore(m_text, Reg.RDI, Reg.RAX, 8);

        X86_64Encoder.AddRI(m_text, Reg.R14, 1);
        X86_64Encoder.Jmp(m_text, loopStart - (m_text.Count + 5));
        PatchJcc(exitIdx, m_text.Count);

        EmitEscapeHelperEpilogue();
    }

    void EmitSumTypeEscapeHelper(string name, SumType st)
    {
        EmitEscapeHelperPrologue(name);

        // r13 = tag
        X86_64Encoder.MovLoad(m_text, Reg.R13, Reg.RBX, 0);

        List<int> jumpToEndIdxs = [];

        for (int ctorIdx = 0; ctorIdx < st.Constructors.Length; ctorIdx++)
        {
            SumConstructorType ctor = st.Constructors[ctorIdx];
            int totalSize = (1 + ctor.Fields.Length) * 8;

            if (ctorIdx < st.Constructors.Length - 1)
            {
                X86_64Encoder.CmpRI(m_text, Reg.R13, ctorIdx);
                int branchIdx = m_text.Count;
                X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_NE, 0);

                EmitSumCtorEscapeHelper(ctor, totalSize);

                jumpToEndIdxs.Add(m_text.Count);
                X86_64Encoder.Jmp(m_text, 0);

                PatchJcc(branchIdx, m_text.Count);
            }
            else
            {
                EmitSumCtorEscapeHelper(ctor, totalSize);
            }
        }

        int endIdx = m_text.Count;
        foreach (int jIdx in jumpToEndIdxs)
            PatchJmp(jIdx, endIdx);

        EmitEscapeHelperEpilogue();
    }

    void EmitSumCtorEscapeHelper(SumConstructorType ctor, int totalSize)
    {
        X86_64Encoder.MovRR(m_text, Reg.R12, HeapReg);
        X86_64Encoder.AddRI(m_text, HeapReg, totalSize);
        // Copy tag
        X86_64Encoder.MovStore(m_text, Reg.R12, Reg.R13, 0);
        // Copy fields
        for (int i = 0; i < ctor.Fields.Length; i++)
            EmitEscapeFieldCopy((1 + i) * 8, (1 + i) * 8, ctor.Fields[i]);
    }

    // ── _start entry point ───────────────────────────────────────

    // ── Bare Metal (Codex.OS Rung 0) ────────────────────────────

    void EmitMultibootHeader()
    {
        // Multiboot1 header: must be 4-byte aligned in first 8KB.
        // Magic: 0x1BADB002, Flags: 0 (no special requirements), Checksum: -(magic + flags)
        const uint MULTIBOOT_MAGIC = 0x1BADB002;
        const uint MULTIBOOT_FLAGS = 0;
        const uint MULTIBOOT_CHECKSUM = unchecked(0u - MULTIBOOT_MAGIC - MULTIBOOT_FLAGS);

        // Emit header at byte 0
        EmitU32(MULTIBOOT_MAGIC);
        EmitU32(MULTIBOOT_FLAGS);
        EmitU32(MULTIBOOT_CHECKSUM);

        // 32-bit entry point: GRUB/QEMU jumps here in protected mode, no paging.
        // We need to set up long mode (64-bit) manually.
        // For now, emit a placeholder — the 32→64 trampoline.
        // This is 32-bit x86 code (NOT 64-bit), hand-assembled.
        Emit32BitTrampoline();
    }

    void Emit32BitTrampoline()
    {
        // This code runs in 32-bit protected mode (no paging).
        // It sets up identity-mapped page tables, enables long mode, and
        // far-jumps to 64-bit code.
        //
        // Page table layout (at fixed address 0x1000):
        //   PML4[0] → PDPT at 0x2000
        //   PDPT[0] → PD at 0x3000
        //   PD[0]   → 2MB huge page identity-mapping 0x0..0x1FFFFF
        //   PD[1]   → 2MB huge page identity-mapping 0x200000..0x3FFFFF
        //
        // Registers on entry (from multiboot):
        //   EAX = 0x2BADB002 (multiboot magic)
        //   EBX = pointer to multiboot info struct
        //   CS  = 32-bit code segment
        //   Paging disabled, A20 enabled

        // All 32-bit code is raw bytes (the encoder only does 64-bit)

        // ── Disable interrupts immediately ──
        m_text.Add(0xFA); // cli

        // ── Clear page table area (0x1000..0x3FFF) ──
        // mov edi, 0x1000
        m_text.AddRange([0xBF, 0x00, 0x10, 0x00, 0x00]);
        // mov ecx, 0xC00 (3 pages * 1024 dwords = 3072)
        m_text.AddRange([0xB9, 0x00, 0x0C, 0x00, 0x00]);
        // xor eax, eax
        m_text.AddRange([0x31, 0xC0]);
        // rep stosd
        m_text.AddRange([0xF3, 0xAB]);

        // ── Set up PML4[0] → PDPT ──
        // mov dword [0x1000], 0x2003 (present + writable + addr 0x2000)
        m_text.AddRange([0xC7, 0x05, 0x00, 0x10, 0x00, 0x00, 0x03, 0x20, 0x00, 0x00]);

        // ── Set up PDPT[0] → PD ──
        // mov dword [0x2000], 0x3003
        m_text.AddRange([0xC7, 0x05, 0x00, 0x20, 0x00, 0x00, 0x03, 0x30, 0x00, 0x00]);

        // ── Set up PD entries: 128 x 2MB huge pages (0-256MB identity mapped) ──
        // Use a 32-bit loop: edi=PD base, ecx=count, eax=phys|flags
        // mov edi, 0x3000
        m_text.AddRange([0xBF, 0x00, 0x30, 0x00, 0x00]);
        // mov ecx, 128
        m_text.AddRange([0xB9, 0x80, 0x00, 0x00, 0x00]);
        // mov eax, 0x83 (present + writable + huge, phys=0)
        m_text.AddRange([0xB8, 0x83, 0x00, 0x00, 0x00]);
        // loop: mov [edi], eax; mov [edi+4], 0; add edi, 8; add eax, 0x200000; dec ecx; jnz loop
        int pdLoopTop = m_text.Count;
        m_text.AddRange([0x89, 0x07]);              // mov [edi], eax
        m_text.AddRange([0xC7, 0x47, 0x04, 0x00, 0x00, 0x00, 0x00]); // mov dword [edi+4], 0
        m_text.AddRange([0x83, 0xC7, 0x08]);        // add edi, 8
        m_text.AddRange([0x05, 0x00, 0x00, 0x20, 0x00]); // add eax, 0x200000
        m_text.AddRange([0x49]);                     // dec ecx (32-bit)
        // jnz loop (2-byte short jump)
        int jnzOffset = -(m_text.Count - pdLoopTop + 2);
        m_text.AddRange([0x75, (byte)(jnzOffset & 0xFF)]);

        // ── Load PML4 into CR3 ──
        // mov eax, 0x1000
        m_text.AddRange([0xB8, 0x00, 0x10, 0x00, 0x00]);
        // mov cr3, eax
        m_text.AddRange([0x0F, 0x22, 0xD8]);

        // ── Enable PAE in CR4 ──
        // mov eax, cr4
        m_text.AddRange([0x0F, 0x20, 0xE0]);
        // or eax, 0x20 (bit 5 = PAE)
        m_text.AddRange([0x83, 0xC8, 0x20]);
        // mov cr4, eax
        m_text.AddRange([0x0F, 0x22, 0xE0]);

        // ── Enable long mode in EFER MSR ──
        // mov ecx, 0xC0000080 (IA32_EFER)
        m_text.AddRange([0xB9, 0x80, 0x00, 0x00, 0xC0]);
        // rdmsr
        m_text.AddRange([0x0F, 0x32]);
        // or eax, 0x100 (bit 8 = LME)
        m_text.AddRange([0x0D, 0x00, 0x01, 0x00, 0x00]);
        // wrmsr
        m_text.AddRange([0x0F, 0x30]);

        // ── Enable paging in CR0 ──
        // mov eax, cr0
        m_text.AddRange([0x0F, 0x20, 0xC0]);
        // or eax, 0x80000000 (bit 31 = PG)
        m_text.AddRange([0x0D, 0x00, 0x00, 0x00, 0x80]);
        // mov cr0, eax
        m_text.AddRange([0x0F, 0x22, 0xC0]);

        // ── Load 64-bit GDT and far jump to long mode ──
        // We need a GDT with a 64-bit code segment. Embed it inline.
        int gdtOffset = m_text.Count;

        // lgdt [gdt_ptr] — but we need the GDT first.
        // Emit GDT at current position, then jump over it.
        // jmp short over_gdt (2 bytes)
        int jmpOverGdt = m_text.Count;
        m_text.AddRange([0xEB, 0x00]); // patched

        // GDT: null descriptor + 64-bit code segment
        int gdtStart = m_text.Count;
        // Null descriptor (8 bytes)
        m_text.AddRange([0, 0, 0, 0, 0, 0, 0, 0]);
        // 64-bit code segment: base=0, limit=0, L=1, P=1, type=code(exec+read)
        // Byte layout: limit_lo(2), base_lo(2), base_mid(1), access(1), granularity(1), base_hi(1)
        m_text.AddRange([0xFF, 0xFF,  // limit low
                         0x00, 0x00,  // base low
                         0x00,        // base mid
                         0x9A,        // access: present + code + exec + read
                         0xAF,        // granularity: 4KB + long mode (L=1) + limit high
                         0x00]);      // base high
        // 64-bit data segment
        m_text.AddRange([0xFF, 0xFF, 0x00, 0x00, 0x00, 0x92, 0xCF, 0x00]);

        // GDT pointer (6 bytes: 2-byte limit + 4-byte base)
        int gdtPtrOffset = m_text.Count;
        int gdtSize = m_text.Count - gdtStart - 1;
        m_text.Add((byte)(gdtSize & 0xFF));
        m_text.Add((byte)((gdtSize >> 8) & 0xFF));
        // Base address (32-bit, will be the flat binary load address + offset)
        // For multiboot, code is loaded at the address specified or at 1MB (0x100000)
        // We assume load at 0x100000
        int gdtAddr = 0x100000 + gdtStart;
        m_text.Add((byte)(gdtAddr & 0xFF));
        m_text.Add((byte)((gdtAddr >> 8) & 0xFF));
        m_text.Add((byte)((gdtAddr >> 16) & 0xFF));
        m_text.Add((byte)((gdtAddr >> 24) & 0xFF));

        // Patch jump over GDT
        m_text[jmpOverGdt + 1] = (byte)(m_text.Count - (jmpOverGdt + 2));

        // lgdt [gdt_ptr]
        // This is tricky in raw bytes. lgdt m16&32:
        // 0F 01 15 [addr32] — lgdt [disp32]
        int gdtPtrAddr = 0x100000 + gdtPtrOffset;
        m_text.AddRange([0x0F, 0x01, 0x15]);
        m_text.Add((byte)(gdtPtrAddr & 0xFF));
        m_text.Add((byte)((gdtPtrAddr >> 8) & 0xFF));
        m_text.Add((byte)((gdtPtrAddr >> 16) & 0xFF));
        m_text.Add((byte)((gdtPtrAddr >> 24) & 0xFF));

        // Far jump to 64-bit code segment (selector 0x08)
        // EA [offset32] [selector16] — but this is 32-bit far jump
        // jmp 0x08:<64bit_entry>
        // Record where 64-bit code starts (will be patched when __start is emitted)
        m_bareMetalLongModeJumpPatch = m_text.Count;
        m_text.Add(0xEA);
        m_text.AddRange([0x00, 0x00, 0x00, 0x00]); // 32-bit offset (patched later)
        m_text.AddRange([0x08, 0x00]); // selector = GDT entry 1 (64-bit code)
    }

    int m_bareMetalLongModeJumpPatch = -1;

    void EmitU32(uint value)
    {
        m_text.Add((byte)(value & 0xFF));
        m_text.Add((byte)((value >> 8) & 0xFF));
        m_text.Add((byte)((value >> 16) & 0xFF));
        m_text.Add((byte)((value >> 24) & 0xFF));
    }

    void EmitSerialChar(byte reg)
    {
        // Write byte in `reg` to COM1 (port 0x3F8) via OUT
        X86_64Encoder.MovRR(m_text, Reg.RDX, 0); // clear RDX
        X86_64Encoder.Li(m_text, Reg.RDX, 0x3F8);
        if (reg != Reg.RAX)
            X86_64Encoder.MovRR(m_text, Reg.RAX, reg);
        X86_64Encoder.OutDxAl(m_text);
    }

    // ── Memory Map (Bare Metal) ──────────────────────────────────
    //
    // 0x0000-0x0FFF  Reserved (real mode IVT, BIOS data)
    // 0x1000-0x1FFF  Boot PML4
    // 0x2000-0x2FFF  Boot PDPT
    // 0x3000-0x3FFF  Boot PD (2x 2MB huge pages)
    // 0x4000-0x4FFF  TSS (Task State Segment)
    // 0x5000-0x5FFF  Process table (max 16 processes x 256 bytes each)
    // 0x6000-0x6FFF  IDT (256 entries x 16 bytes)
    // 0x7000-0x700F  Tick counter + key buffer
    // 0x8000-0x8FFF  Process 0 PML4
    // 0x9000-0x9FFF  Process 0 PDPT
    // 0xA000-0xAFFF  Process 0 PD
    // 0xB000-0xBFFF  Process 1 PML4
    // 0xC000-0xCFFF  Process 1 PDPT
    // 0xD000-0xDFFF  Process 1 PD
    // 0x10000-0x7FFFF  Kernel stack (grows down from 0x80000)
    // 0x100000+       Kernel code (.text + .rodata)
    // 0x200000+       Kernel heap
    // 0x400000+       Process 0 user-space (code + heap)
    // 0x600000+       Process 1 user-space (code + heap)

    // ── Process Management (Ring 2) ──────────────────────────────

    const long TssBase = 0x4000;
    const long ProcTableBase = 0x5000;
    const int MaxProcesses = 16;
    const int ProcEntrySize = 256;
    // Process entry layout (256 bytes):
    //   [0]   state: 0=free, 1=running, 2=ready, 3=blocked
    //   [8]   rsp: saved stack pointer
    //   [16]  cr3: page table root
    //   [24]  rip: saved instruction pointer
    //   [32]  rflags: saved flags
    //   [40]  heap_base: process heap start
    //   [48]  heap_ptr: current heap position

    const long CurrentProcAddr = 0x7010;  // index of currently running process

    void EmitProcessSetup()
    {
        // Initialize process table to all zeros (free)
        X86_64Encoder.Li(m_text, Reg.RDI, ProcTableBase);
        X86_64Encoder.Li(m_text, Reg.RCX, MaxProcesses * ProcEntrySize / 8);
        X86_64Encoder.Li(m_text, Reg.RAX, 0);
        // rep stosq: fill [RDI] with RAX, RCX times
        m_text.Add(0x48); // REX.W
        m_text.Add(0xF3); // REP
        m_text.Add(0xAB); // STOS (stosq with REX.W)

        // Set current process = 0
        X86_64Encoder.Li(m_text, Reg.RDI, CurrentProcAddr);
        X86_64Encoder.Li(m_text, Reg.RAX, 0);
        X86_64Encoder.MovStore(m_text, Reg.RDI, Reg.RAX, 0);

        // Build process 0 page tables at 0x8000 (maps kernel + 0x400000 user heap)
        EmitBuildProcessPageTables(0, 0x8000, 0x400000);
        EmitCreateProcess(0, 0x8000 /* process 0 CR3 */, 0x400000 /* heap base */);

        // Switch to process 0's page tables
        X86_64Encoder.Li(m_text, Reg.RAX, 0x8000);
        m_text.Add(0x0F); m_text.Add(0x22); m_text.Add(0xD8); // mov cr3, rax

        // Update kernel heap pointer to process 0's private heap
        X86_64Encoder.Li(m_text, HeapReg, 0x400000);
    }

    void EmitCreateProcess(int procIndex, long cr3, long heapBase)
    {
        long entryAddr = ProcTableBase + procIndex * ProcEntrySize;
        X86_64Encoder.Li(m_text, Reg.RDI, entryAddr);

        // state = 1 (running) for process 0, 2 (ready) for others
        X86_64Encoder.Li(m_text, Reg.RAX, procIndex == 0 ? 1 : 2);
        X86_64Encoder.MovStore(m_text, Reg.RDI, Reg.RAX, 0);

        // cr3
        X86_64Encoder.Li(m_text, Reg.RAX, cr3);
        X86_64Encoder.MovStore(m_text, Reg.RDI, Reg.RAX, 16);

        // heap_base and heap_ptr
        X86_64Encoder.Li(m_text, Reg.RAX, heapBase);
        X86_64Encoder.MovStore(m_text, Reg.RDI, Reg.RAX, 40);
        X86_64Encoder.MovStore(m_text, Reg.RDI, Reg.RAX, 48);
    }

    void EmitContextSwitch()
    {
        // Called from timer interrupt handler.
        // Saves current process state, picks next ready process, restores it.
        //
        // Current process RSP is already on the interrupt stack frame.
        // We need to:
        // 1. Save current process's RSP to its proc table entry
        // 2. Find next ready process (round-robin)
        // 3. Load next process's CR3 and RSP
        // 4. Return via iretq (which pops RIP, CS, RFLAGS, RSP, SS)

        // Load current process index
        X86_64Encoder.Li(m_text, Reg.RDI, CurrentProcAddr);
        X86_64Encoder.MovLoad(m_text, Reg.RAX, Reg.RDI, 0); // RAX = current proc index

        // Save RSP to proc table[current].rsp
        // RSP at this point includes the interrupt frame
        X86_64Encoder.Li(m_text, Reg.RCX, ProcEntrySize);
        X86_64Encoder.ImulRR(m_text, Reg.RAX, Reg.RCX); // RAX = index * ProcEntrySize
        X86_64Encoder.Li(m_text, Reg.RCX, ProcTableBase);
        X86_64Encoder.AddRR(m_text, Reg.RAX, Reg.RCX);   // RAX = &proc_table[current]
        X86_64Encoder.MovStore(m_text, Reg.RAX, Reg.RSP, 8); // save RSP

        // Mark current as ready (state = 2)
        X86_64Encoder.Li(m_text, Reg.RCX, 2);
        X86_64Encoder.MovStore(m_text, Reg.RAX, Reg.RCX, 0);

        // Find next ready process (round-robin)
        // Simple: try (current+1) % MaxProcesses, then (current+2), etc.
        X86_64Encoder.Li(m_text, Reg.RDI, CurrentProcAddr);
        X86_64Encoder.MovLoad(m_text, Reg.RSI, Reg.RDI, 0); // RSI = current index
        X86_64Encoder.AddRI(m_text, Reg.RSI, 1);

        int searchLoop = m_text.Count;
        // if RSI >= MaxProcesses, wrap to 0
        X86_64Encoder.CmpRI(m_text, Reg.RSI, MaxProcesses);
        int wrapJump = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_L, 0); // skip wrap if < MaxProcesses
        X86_64Encoder.Li(m_text, Reg.RSI, 0);
        PatchJcc(wrapJump, m_text.Count);

        // Check if proc_table[RSI].state == 1 or 2 (running/ready)
        X86_64Encoder.MovRR(m_text, Reg.RAX, Reg.RSI);
        X86_64Encoder.Li(m_text, Reg.RCX, ProcEntrySize);
        X86_64Encoder.ImulRR(m_text, Reg.RAX, Reg.RCX);
        X86_64Encoder.Li(m_text, Reg.RCX, ProcTableBase);
        X86_64Encoder.AddRR(m_text, Reg.RAX, Reg.RCX); // RAX = &proc_table[RSI]
        X86_64Encoder.MovLoad(m_text, Reg.RCX, Reg.RAX, 0); // state
        X86_64Encoder.CmpRI(m_text, Reg.RCX, 0); // free?
        int foundReady = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_NE, 0); // not free = usable

        // Free slot, try next
        X86_64Encoder.AddRI(m_text, Reg.RSI, 1);
        X86_64Encoder.Jmp(m_text, searchLoop - (m_text.Count + 5));

        PatchJcc(foundReady, m_text.Count);

        // RSI = next process index, RAX = &proc_table[next]
        // Update current process index
        X86_64Encoder.Li(m_text, Reg.RDI, CurrentProcAddr);
        X86_64Encoder.MovStore(m_text, Reg.RDI, Reg.RSI, 0);

        // Mark new process as running (state = 1)
        X86_64Encoder.Li(m_text, Reg.RCX, 1);
        X86_64Encoder.MovStore(m_text, Reg.RAX, Reg.RCX, 0);

        // Load new process's CR3
        X86_64Encoder.MovLoad(m_text, Reg.RCX, Reg.RAX, 16); // cr3
        // mov cr3, rcx
        m_text.Add(0x0F); m_text.Add(0x22); m_text.Add(0xD9); // mov cr3, rcx

        // Load new process's RSP
        X86_64Encoder.MovLoad(m_text, Reg.RSP, Reg.RAX, 8);

        // Load new process's HeapReg
        X86_64Encoder.MovLoad(m_text, HeapReg, Reg.RAX, 48);
    }

    int m_process1EntryOffset = -1; // patched after __proc1_entry is emitted

    void EmitBuildProcessPageTables(int procIndex, long pml4Addr, long userHeapPhys)
    {
        // Build a per-process page table set:
        //   PML4 at pml4Addr
        //   PDPT at pml4Addr + 0x1000
        //   PD   at pml4Addr + 0x2000
        //
        // Maps:
        //   PD[0] → 0x000000 (2MB, kernel low memory: IDT, proc table, etc.)
        //   PD[1] → 0x200000 (2MB, kernel heap — shared read-only in future)
        //   PD[N] → userHeapPhys (2MB, this process's private heap)
        //
        // All entries use 2MB huge pages (PS bit = 0x80).

        long pdptAddr = pml4Addr + 0x1000;
        long pdAddr = pml4Addr + 0x2000;

        // Zero all three pages (3 * 4096 / 8 = 1536 qwords)
        X86_64Encoder.Li(m_text, Reg.RDI, pml4Addr);
        X86_64Encoder.Li(m_text, Reg.RCX, 1536);
        X86_64Encoder.Li(m_text, Reg.RAX, 0);
        m_text.Add(0x48); m_text.Add(0xF3); m_text.Add(0xAB); // rep stosq

        // PML4[0] → PDPT (present + writable = 0x03)
        X86_64Encoder.Li(m_text, Reg.RDI, pml4Addr);
        X86_64Encoder.Li(m_text, Reg.RAX, pdptAddr | 0x03);
        X86_64Encoder.MovStore(m_text, Reg.RDI, Reg.RAX, 0);

        // PDPT[0] → PD (present + writable = 0x03)
        X86_64Encoder.Li(m_text, Reg.RDI, pdptAddr);
        X86_64Encoder.Li(m_text, Reg.RAX, pdAddr | 0x03);
        X86_64Encoder.MovStore(m_text, Reg.RDI, Reg.RAX, 0);

        // Map 128 x 2MB pages (0-256MB) — full identity map per process
        for (int i = 0; i < 128; i++)
        {
            long physAddr = i * 0x200000L;
            X86_64Encoder.Li(m_text, Reg.RAX, physAddr | 0x83);
            X86_64Encoder.MovStore(m_text, Reg.RDI, Reg.RAX, i * 8);
        }
    }

    void EmitProcess1Setup()
    {
        // Process 1 runs __proc1_entry (emitted after all functions).
        // Same page tables as kernel (CR3 = 0x1000).
        // Own stack at 0x70000 (grows down, below kernel stack at 0x80000).
        // Own heap at 0x300000.

        const long proc1Stack = 0x70000;
        const long proc1Heap = 0x600000;

        // Build process 1 page tables at 0xB000 (maps kernel + 0x600000 user heap)
        EmitBuildProcessPageTables(1, 0xB000, proc1Heap);
        EmitCreateProcess(1, 0xB000 /* process 1 CR3 */, proc1Heap);

        // Build a fake interrupt frame on process 1's stack so the first
        // context switch can "iretq" into it.
        // Stack layout (growing down from proc1Stack):
        //   [RSP+32] SS     = 0x10 (kernel data segment)
        //   [RSP+24] RSP    = proc1Stack (will be the running RSP)
        //   [RSP+16] RFLAGS = 0x202 (IF=1, reserved bit 1 = 1)
        //   [RSP+8]  CS     = 0x08 (kernel code segment)
        //   [RSP+0]  RIP    = __proc1_entry (patched later)
        // Then our saved registers: RDI, RSI, RDX, RCX, RAX (5 words)
        // Total: 10 words = 80 bytes

        long frameBase = proc1Stack - 80;

        // Write SS
        X86_64Encoder.Li(m_text, Reg.RDI, frameBase + 72);
        X86_64Encoder.Li(m_text, Reg.RAX, 0x10);
        X86_64Encoder.MovStore(m_text, Reg.RDI, Reg.RAX, 0);

        // Write RSP
        X86_64Encoder.Li(m_text, Reg.RDI, frameBase + 64);
        X86_64Encoder.Li(m_text, Reg.RAX, proc1Stack);
        X86_64Encoder.MovStore(m_text, Reg.RDI, Reg.RAX, 0);

        // Write RFLAGS (IF=1)
        X86_64Encoder.Li(m_text, Reg.RDI, frameBase + 56);
        X86_64Encoder.Li(m_text, Reg.RAX, 0x202);
        X86_64Encoder.MovStore(m_text, Reg.RDI, Reg.RAX, 0);

        // Write CS
        X86_64Encoder.Li(m_text, Reg.RDI, frameBase + 48);
        X86_64Encoder.Li(m_text, Reg.RAX, 0x08);
        X86_64Encoder.MovStore(m_text, Reg.RDI, Reg.RAX, 0);

        // Write RIP = __proc1_entry (already emitted, address known)
        long proc1Vaddr = 0x100000 + m_process1EntryOffset;
        X86_64Encoder.Li(m_text, Reg.RDI, frameBase + 40);
        X86_64Encoder.Li(m_text, Reg.RAX, proc1Vaddr);
        X86_64Encoder.MovStore(m_text, Reg.RDI, Reg.RAX, 0);

        // Write saved registers (all zero)
        X86_64Encoder.Li(m_text, Reg.RAX, 0);
        for (int i = 0; i < 5; i++)
        {
            X86_64Encoder.Li(m_text, Reg.RDI, frameBase + i * 8);
            X86_64Encoder.MovStore(m_text, Reg.RDI, Reg.RAX, 0);
        }

        // Set process 1's saved RSP to point to the fake frame
        long proc1Entry = ProcTableBase + 1 * ProcEntrySize;
        X86_64Encoder.Li(m_text, Reg.RDI, proc1Entry + 8); // rsp field
        X86_64Encoder.Li(m_text, Reg.RAX, frameBase);
        X86_64Encoder.MovStore(m_text, Reg.RDI, Reg.RAX, 0);
    }

    void EmitProcess1Entry()
    {
        // A simple process that loops printing "B" to serial
        m_process1EntryOffset = m_text.Count;

        // Set up this process's heap pointer
        X86_64Encoder.Li(m_text, HeapReg, 0x600000);

        // Infinite loop: print "B" via syscall (capability-checked), then hlt
        int loopTop = m_text.Count;
        X86_64Encoder.Li(m_text, Reg.RAX, SYS_WRITE_SERIAL); // syscall number
        X86_64Encoder.Li(m_text, Reg.RDI, 'B');               // byte to write
        X86_64Encoder.Syscall(m_text);                         // kernel checks CAP_CONSOLE

        // Halt and wait for timer interrupt (which will switch away from us)
        X86_64Encoder.Hlt(m_text);
        X86_64Encoder.Jmp(m_text, loopTop - (m_text.Count + 5));
    }

    // ── Capability System (Ring 3) ──────────────────────────────

    // Capability bits (matches the Codex effect system)
    const int CAP_CONSOLE    = 0;  // [Console] — serial I/O
    const int CAP_FILESYSTEM = 1;  // [FileSystem] — disk I/O (future)
    const int CAP_NETWORK    = 2;  // [Network] — network I/O (future)
    const int CAP_CONCURRENT = 3;  // [Concurrent] — fork/spawn

    // Syscall numbers
    const int SYS_WRITE_SERIAL = 1;  // write byte to serial (requires CAP_CONSOLE)
    const int SYS_READ_KEY     = 2;  // read last keycode (requires CAP_CONSOLE)
    const int SYS_GET_TICKS    = 3;  // get tick count (no capability needed)
    const int SYS_EXIT         = 60; // exit process (no capability needed)

    // Per-process capability bitfield stored in process table at offset 56
    const int ProcCapOffset = 56;

    void EmitSyscallSetup()
    {
        // Set up STAR MSR (0xC0000081): kernel CS/SS in bits 47:32, user CS/SS in bits 63:48
        // Kernel: CS=0x08, SS=0x10. User: CS=0x08, SS=0x10 (both Ring 0 for now)
        // STAR[47:32] = SYSRET CS/SS base, STAR[31:0] = reserved
        // For kernel-only (no Ring 3 yet): both point to kernel segments
        EmitWriteMsr(0xC0000081, (0x08L << 32) | (0x10L << 48)); // STAR

        // Set up LSTAR MSR (0xC0000082): syscall entry point
        // Will be patched after the handler is emitted
        m_syscallHandlerPatch = m_text.Count;
        EmitWriteMsr(0xC0000082, 0xDEAD); // placeholder, patched later

        // Set up SFMASK MSR (0xC0000084): mask IF on syscall entry
        EmitWriteMsr(0xC0000084, 0x200); // mask bit 9 (IF) — disable interrupts in handler

        // Enable SCE (System Call Extensions) in EFER
        // Already enabled by the trampoline? Let's set it explicitly.
        X86_64Encoder.Li(m_text, Reg.RCX, 0xC0000080); // IA32_EFER
        // rdmsr
        m_text.Add(0x0F); m_text.Add(0x32);
        // or eax, 1 (SCE bit)
        X86_64Encoder.Li(m_text, Reg.R11, 1);
        // Need to OR into EAX... use different approach
        // or rax, 1
        m_text.Add(0x48); m_text.Add(0x83); m_text.Add(0xC8); m_text.Add(0x01); // or rax, 1
        // wrmsr
        m_text.Add(0x0F); m_text.Add(0x30);
    }

    int m_syscallHandlerPatch = -1;

    void EmitWriteMsr(long msrAddr, long value)
    {
        // wrmsr: ECX = MSR address, EDX:EAX = value (high:low 32 bits)
        X86_64Encoder.Li(m_text, Reg.RCX, msrAddr);
        X86_64Encoder.Li(m_text, Reg.RAX, value & 0xFFFFFFFF);        // low 32
        X86_64Encoder.Li(m_text, Reg.RDX, (value >> 32) & 0xFFFFFFFF); // high 32
        m_text.Add(0x0F); m_text.Add(0x30); // wrmsr
    }

    void EmitSyscallHandler()
    {
        // Syscall entry: RCX=return RIP, R11=saved RFLAGS, RAX=syscall#
        // We're in Ring 0, interrupts masked (SFMASK cleared IF).
        int handlerOffset = m_text.Count;
        long handlerVaddr = 0x100000 + handlerOffset;

        // Patch LSTAR to point here
        if (m_syscallHandlerPatch >= 0)
        {
            // The EmitWriteMsr for LSTAR wrote: Li(RCX, msr), Li(RAX, low32), Li(RDX, high32), wrmsr
            // The Li(RAX, value) is the second Li. We need to patch its immediate.
            // Actually, let's just re-emit the LSTAR write with the correct value.
            // Easier: store handlerVaddr and patch after.
            // The Li for RAX starts at m_syscallHandlerPatch + <offset of second Li>
            // This is fragile. Let me use a simpler approach: write LSTAR at setup time
            // using a register that holds the address.
        }

        // Save caller's stack pointer and switch to kernel stack
        // For simplicity (both in Ring 0): just use the current stack
        X86_64Encoder.PushR(m_text, Reg.RCX); // save return RIP
        X86_64Encoder.PushR(m_text, Reg.R11); // save RFLAGS

        // Dispatch on syscall number (RAX)
        X86_64Encoder.CmpRI(m_text, Reg.RAX, SYS_WRITE_SERIAL);
        int notWrite = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_NE, 0);

        // ── SYS_WRITE_SERIAL: write byte in RDI to COM1 ──
        // Check CAP_CONSOLE
        EmitCheckCapability(CAP_CONSOLE);
        int capDenied = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_E, 0); // ZF=1 means denied

        // Granted: write byte
        X86_64Encoder.MovRR(m_text, Reg.RAX, Reg.RDI); // byte to write
        X86_64Encoder.Li(m_text, Reg.RDX, 0x3F8);
        X86_64Encoder.OutDxAl(m_text);
        X86_64Encoder.Li(m_text, Reg.RAX, 0); // success
        int writeOk = m_text.Count;
        X86_64Encoder.Jmp(m_text, 0); // jump to return

        // Denied: return -1
        PatchJcc(capDenied, m_text.Count);
        X86_64Encoder.Li(m_text, Reg.RAX, -1);
        int writeDenied = m_text.Count;
        X86_64Encoder.Jmp(m_text, 0); // jump to return

        PatchJcc(notWrite, m_text.Count);

        // ── SYS_READ_KEY ──
        X86_64Encoder.CmpRI(m_text, Reg.RAX, SYS_READ_KEY);
        int notReadKey = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_NE, 0);

        EmitCheckCapability(CAP_CONSOLE);
        int keyDenied = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_E, 0);

        X86_64Encoder.Li(m_text, Reg.RDI, KeyBufferAddr);
        X86_64Encoder.MovLoad(m_text, Reg.RAX, Reg.RDI, 0);
        int keyOk = m_text.Count;
        X86_64Encoder.Jmp(m_text, 0);

        PatchJcc(keyDenied, m_text.Count);
        X86_64Encoder.Li(m_text, Reg.RAX, -1);
        int keyDeniedJmp = m_text.Count;
        X86_64Encoder.Jmp(m_text, 0);

        PatchJcc(notReadKey, m_text.Count);

        // ── SYS_GET_TICKS (no capability needed) ──
        X86_64Encoder.CmpRI(m_text, Reg.RAX, SYS_GET_TICKS);
        int notTicks = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_NE, 0);

        X86_64Encoder.Li(m_text, Reg.RDI, TickCountAddr);
        X86_64Encoder.MovLoad(m_text, Reg.RAX, Reg.RDI, 0);
        int ticksOk = m_text.Count;
        X86_64Encoder.Jmp(m_text, 0);

        PatchJcc(notTicks, m_text.Count);

        // ── Unknown syscall: return -1 ──
        X86_64Encoder.Li(m_text, Reg.RAX, -1);

        // ── Common return path ──
        int returnPath = m_text.Count;
        PatchJmp(writeOk, returnPath);
        PatchJmp(writeDenied, returnPath);
        PatchJmp(keyOk, returnPath);
        PatchJmp(keyDeniedJmp, returnPath);
        PatchJmp(ticksOk, returnPath);

        X86_64Encoder.PopR(m_text, Reg.R11); // restore RFLAGS
        X86_64Encoder.PopR(m_text, Reg.RCX); // restore return RIP

        // Restore RFLAGS (re-enable interrupts)
        // push r11; popfq
        X86_64Encoder.PushR(m_text, Reg.R11);
        m_text.Add(0x9D); // popfq

        // Return to caller via jmp rcx (RCX = saved RIP from syscall)
        // jmp rcx = FF E1
        m_text.Add(0xFF); m_text.Add(0xE1);

        // Store handler address for LSTAR patching
        m_syscallHandlerAddr = handlerVaddr;
    }

    long m_syscallHandlerAddr;

    void EmitCheckCapability(int capBit)
    {
        // Load current process's capability bitfield and test the bit.
        // Sets ZF=1 if capability is NOT granted (for Jcc CC_E = denied).
        X86_64Encoder.Li(m_text, Reg.R11, CurrentProcAddr);
        X86_64Encoder.MovLoad(m_text, Reg.R11, Reg.R11, 0); // current proc index

        X86_64Encoder.Li(m_text, Reg.RAX, ProcEntrySize);
        X86_64Encoder.ImulRR(m_text, Reg.R11, Reg.RAX);
        X86_64Encoder.Li(m_text, Reg.RAX, ProcTableBase + ProcCapOffset);
        X86_64Encoder.AddRR(m_text, Reg.R11, Reg.RAX); // R11 = &proc[current].capabilities

        X86_64Encoder.MovLoad(m_text, Reg.R11, Reg.R11, 0); // R11 = capability bitfield
        X86_64Encoder.Li(m_text, Reg.RAX, 1L << capBit);
        X86_64Encoder.AndRR(m_text, Reg.R11, Reg.RAX); // test bit
        // ZF=1 if bit was 0 (not granted), ZF=0 if granted
        X86_64Encoder.TestRR(m_text, Reg.R11, Reg.R11);
    }

    void EmitGrantCapability(int procIndex, int capBit)
    {
        // Set a capability bit in a process's capability field
        long capAddr = ProcTableBase + procIndex * ProcEntrySize + ProcCapOffset;
        X86_64Encoder.Li(m_text, Reg.RDI, capAddr);
        X86_64Encoder.MovLoad(m_text, Reg.RAX, Reg.RDI, 0);
        X86_64Encoder.Li(m_text, Reg.R11, 1L << capBit);
        // or rax, r11
        m_text.Add(0x4C); m_text.Add(0x09); m_text.Add(0xD8); // or rax, r11
        X86_64Encoder.MovStore(m_text, Reg.RDI, Reg.RAX, 0);
    }

    // ── Interrupt Handling (Ring 1) ──────────────────────────────

    const long IdtBase = 0x6000;
    // --- Kernel data page (0x7000-0x7FFF) ---
    // 0x7000  TickCountAddr    8 bytes  Timer interrupt counter
    // 0x7008  KeyBufferAddr    8 bytes  Last keyboard scancode
    // 0x7010  CurrentProcAddr  8 bytes  (defined at line ~3281 with process table)
    // 0x7018  ArenaBaseAddr    8 bytes  Heap arena base for REPL reset
    const long TickCountAddr = 0x7000;
    const long KeyBufferAddr = 0x7008;
    const long ArenaBaseAddr = 0x7018;

    void EmitInterruptSetup()
    {
        // 1. Remap PIC: IRQ 0-7 → vectors 32-39, IRQ 8-15 → vectors 40-47
        EmitPicInit();

        // 2. ISR stubs already emitted by EmitModule; IDT entries built by EmitIdtEntries.

        // 3. Load IDT register via stack-built IDTR descriptor
        // IDTR: [limit:16][base:64] = 10 bytes, packed on stack
        X86_64Encoder.SubRI(m_text, Reg.RSP, 16);
        long idtLimit = 256 * 16 - 1; // 4095
        long low8 = (idtLimit & 0xFFFF) | ((IdtBase & 0xFFFFFFFFFFFF) << 16);
        X86_64Encoder.Li(m_text, Reg.RAX, low8);
        X86_64Encoder.MovStore(m_text, Reg.RSP, Reg.RAX, 0);
        long high8 = (IdtBase >> 48) & 0xFFFF;
        X86_64Encoder.Li(m_text, Reg.RAX, high8);
        X86_64Encoder.MovStore(m_text, Reg.RSP, Reg.RAX, 8);
        X86_64Encoder.MovRR(m_text, Reg.RDI, Reg.RSP);
        X86_64Encoder.LidtRdi(m_text);
        X86_64Encoder.AddRI(m_text, Reg.RSP, 16);

        // 4. Initialize tick counter, key buffer, and arena base to 0
        X86_64Encoder.Li(m_text, Reg.RDI, TickCountAddr);
        X86_64Encoder.Li(m_text, Reg.RAX, 0);
        X86_64Encoder.MovStore(m_text, Reg.RDI, Reg.RAX, 0);
        X86_64Encoder.Li(m_text, Reg.RDI, KeyBufferAddr);
        X86_64Encoder.MovStore(m_text, Reg.RDI, Reg.RAX, 0);
        X86_64Encoder.Li(m_text, Reg.RDI, ArenaBaseAddr);
        X86_64Encoder.MovStore(m_text, Reg.RDI, Reg.RAX, 0);

        // 5. Enable interrupts
        X86_64Encoder.Sti(m_text);
    }

    void EmitPicInit()
    {
        // ICW1: begin init (0x11)
        EmitOutByte(0x20, 0x11); // PIC1 command
        EmitOutByte(0xA0, 0x11); // PIC2 command
        // ICW2: vector offset
        EmitOutByte(0x21, 32);   // PIC1: IRQ 0-7 → vectors 32-39
        EmitOutByte(0xA1, 40);   // PIC2: IRQ 8-15 → vectors 40-47
        // ICW3: cascading
        EmitOutByte(0x21, 4);    // PIC1: IRQ2 has slave
        EmitOutByte(0xA1, 2);    // PIC2: cascade identity 2
        // ICW4: 8086 mode
        EmitOutByte(0x21, 1);
        EmitOutByte(0xA1, 1);
        // Mask all except timer (IRQ0) and keyboard (IRQ1)
        EmitOutByte(0x21, 0xFC); // PIC1: unmask IRQ0 + IRQ1 (bits 0,1 = 0)
        EmitOutByte(0xA1, 0xFF); // PIC2: mask all
    }

    void EmitOutByte(int port, int value)
    {
        X86_64Encoder.Li(m_text, Reg.RDX, port);
        X86_64Encoder.Li(m_text, Reg.RAX, value);
        X86_64Encoder.OutDxAl(m_text);
    }

    void EmitIsrStubsAndIdt()
    {
        // For each of the 256 interrupt vectors, emit a tiny ISR stub
        // and write the IDT entry pointing to it.
        //
        // ISR stub pattern:
        //   push rax (save)
        //   mov al, <vector>
        //   jmp common_handler
        //
        // IDT entry (16 bytes, 64-bit interrupt gate):
        //   [0:2]  offset_low
        //   [2:4]  selector (0x08 = kernel code segment from GDT)
        //   [4]    IST (0)
        //   [5]    type_attr (0x8E = present + DPL0 + 64-bit interrupt gate)
        //   [6:8]  offset_mid
        //   [8:12] offset_high
        //   [12:16] reserved (0)

        // First emit the common handler
        int commonHandlerOffset = m_text.Count;
        EmitCommonInterruptHandler();

        // Now emit 256 stubs and build IDT
        for (int vec = 0; vec < 256; vec++)
        {
            int stubOffset = m_text.Count;

            // push rax
            X86_64Encoder.PushR(m_text, Reg.RAX);
            // mov al, <vec>
            m_text.Add(0xB0); m_text.Add((byte)vec);
            // jmp common_handler
            int rel = commonHandlerOffset - (m_text.Count + 5);
            X86_64Encoder.Jmp(m_text, rel);

            // Compute the stub's virtual address
            // In bare metal, m_text[0] maps to 0x100000
            long stubVaddr = 0x100000 + stubOffset;

            // Write IDT entry at IdtBase + vec*16
            // We can't write to arbitrary memory at compile time —
            // we need to emit code that writes the IDT at runtime.
            // Let me emit the IDT writes in EmitInterruptSetup instead.

            // Store stub address for later IDT construction
            if (vec < m_isrStubAddrs.Length)
                m_isrStubAddrs[vec] = stubVaddr;
        }
    }

    readonly long[] m_isrStubAddrs = new long[256];

    void EmitIdtEntries()
    {
        // Emit a runtime loop that writes all 256 IDT entries.
        // Replaces the previous unrolled version which emitted ~12.8KB of code.
        // Each ISR stub is 8 bytes (push rax + mov al,vec + jmp rel32),
        // contiguous starting at m_isrStubAddrs[0].
        //
        // Registers used:
        //   RCX = loop counter (256 → 0)
        //   RDI = IDT entry pointer (IdtBase, += 16 each iteration)
        //   RSI = stub virtual address (firstStubAddr, += 8 each iteration)
        //   RAX, RDX = scratch

        const int StubSize = 8; // push rax(1) + mov al,vec(2) + jmp rel32(5)

        long firstStubAddr = m_isrStubAddrs[0];

        X86_64Encoder.Li(m_text, Reg.RCX, 256);
        X86_64Encoder.Li(m_text, Reg.RDI, IdtBase);
        X86_64Encoder.Li(m_text, Reg.RSI, firstStubAddr);

        int loopTop = m_text.Count;

        // ── dword 0: offset_low(16) | selector(16) ──
        X86_64Encoder.MovRR(m_text, Reg.RAX, Reg.RSI);
        X86_64Encoder.AndRI(m_text, Reg.RAX, 0xFFFF);       // addr & 0xFFFF
        X86_64Encoder.AddRI(m_text, Reg.RAX, 0x08 << 16);   // | selector=0x08
        X86_64Encoder.MovStore(m_text, Reg.RDI, Reg.RAX, 0);

        // ── dword 1: IST(0) | type_attr(0x8E) | offset_mid(16) ──
        X86_64Encoder.MovRR(m_text, Reg.RAX, Reg.RSI);
        X86_64Encoder.ShrRI(m_text, Reg.RAX, 16);            // addr >> 16
        X86_64Encoder.ShlRI(m_text, Reg.RAX, 16);            // (addr >> 16) << 16
        X86_64Encoder.AddRI(m_text, Reg.RAX, 0x8E << 8);     // | type_attr
        X86_64Encoder.MovStore(m_text, Reg.RDI, Reg.RAX, 4);

        // ── dwords 2-3: offset_high(32) | reserved(32) = 0 (addr < 4GB) ──
        X86_64Encoder.Li(m_text, Reg.RAX, 0);
        X86_64Encoder.MovStore(m_text, Reg.RDI, Reg.RAX, 8);

        // ── Advance pointers, decrement counter ──
        X86_64Encoder.AddRI(m_text, Reg.RDI, 16);            // next IDT entry
        X86_64Encoder.AddRI(m_text, Reg.RSI, StubSize);      // next stub
        X86_64Encoder.SubRI(m_text, Reg.RCX, 1);

        int jccOffset = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_NE, 0);   // loop if RCX != 0
        PatchJcc(jccOffset, loopTop);
    }

    void EmitCommonInterruptHandler()
    {
        // On entry: RAX was pushed, AL = vector number
        // Save all registers
        X86_64Encoder.PushR(m_text, Reg.RCX);
        X86_64Encoder.PushR(m_text, Reg.RDX);
        X86_64Encoder.PushR(m_text, Reg.RSI);
        X86_64Encoder.PushR(m_text, Reg.RDI);

        // Zero-extend AL to RAX (vector number)
        // movzx eax, al = 0F B6 C0
        m_text.Add(0x0F); m_text.Add(0xB6); m_text.Add(0xC0);

        // Check: vector 32 = timer, vector 33 = keyboard
        X86_64Encoder.CmpRI(m_text, Reg.RAX, 32);
        int notTimer = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_NE, 0);

        // ── Timer handler: increment tick counter + context switch ──
        X86_64Encoder.Li(m_text, Reg.RDI, TickCountAddr);
        X86_64Encoder.MovLoad(m_text, Reg.RSI, Reg.RDI, 0);
        X86_64Encoder.AddRI(m_text, Reg.RSI, 1);
        X86_64Encoder.MovStore(m_text, Reg.RDI, Reg.RSI, 0);

        // Send EOI early (before potential stack switch)
        EmitOutByte(0x20, 0x20);

        // Context switch if more than one active process
        // Check proc_table[1].state != 0 (any second process exists)
        X86_64Encoder.Li(m_text, Reg.RDI, ProcTableBase + ProcEntrySize);
        X86_64Encoder.MovLoad(m_text, Reg.RDI, Reg.RDI, 0);
        X86_64Encoder.CmpRI(m_text, Reg.RDI, 0);
        int skipCtxSwitch = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_E, 0); // skip if no second process

        // Do context switch — this changes RSP to next process's saved frame
        EmitContextSwitch();

        PatchJcc(skipCtxSwitch, m_text.Count);

        // Restore registers and iretq — works for BOTH paths:
        // - No switch: RSP still points to current process's saved regs
        // - Switch: RSP now points to next process's saved regs
        X86_64Encoder.PopR(m_text, Reg.RDI);
        X86_64Encoder.PopR(m_text, Reg.RSI);
        X86_64Encoder.PopR(m_text, Reg.RDX);
        X86_64Encoder.PopR(m_text, Reg.RCX);
        X86_64Encoder.PopR(m_text, Reg.RAX);
        X86_64Encoder.Iretq(m_text);

        // Timer is fully handled above — skip the normal EOI path
        int doneTimer = m_text.Count; // not used, but needed for the jmp below
        // (fall through to notTimer which will jmp to EOI for other vectors)

        // ── Not timer ──
        PatchJcc(notTimer, m_text.Count);
        X86_64Encoder.CmpRI(m_text, Reg.RAX, 33);
        int notKeyboard = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_NE, 0);

        // ── Keyboard handler: read scancode from port 0x60 ──
        X86_64Encoder.Li(m_text, Reg.RDX, 0x60);
        X86_64Encoder.InAlDx(m_text);
        // Store scancode
        X86_64Encoder.Li(m_text, Reg.RDI, KeyBufferAddr);
        // movzx rax, al
        m_text.Add(0x48); m_text.Add(0x0F); m_text.Add(0xB6); m_text.Add(0xC0);
        X86_64Encoder.MovStore(m_text, Reg.RDI, Reg.RAX, 0);

        // ── Not keyboard (fall through to EOI) ──
        PatchJcc(notKeyboard, m_text.Count);

        // ── Send EOI to PIC (for non-timer interrupts) ──
        EmitOutByte(0x20, 0x20); // EOI to PIC1

        // Restore registers
        X86_64Encoder.PopR(m_text, Reg.RDI);
        X86_64Encoder.PopR(m_text, Reg.RSI);
        X86_64Encoder.PopR(m_text, Reg.RDX);
        X86_64Encoder.PopR(m_text, Reg.RCX);
        X86_64Encoder.PopR(m_text, Reg.RAX);
        X86_64Encoder.Iretq(m_text);
    }

    void EmitSerialString(string s)
    {
        // Emit each byte of the string to COM1
        foreach (char c in s)
        {
            X86_64Encoder.Li(m_text, Reg.RAX, c);
            X86_64Encoder.Li(m_text, Reg.RDX, 0x3F8);
            X86_64Encoder.OutDxAl(m_text);
        }
    }

    void EmitStart(IRModule module)
    {
        m_functionOffsets["__start"] = m_text.Count;
        m_currentFunction = "__start";
        m_nextTemp = 0;
        m_nextLocal = 0;
        m_spillCount = 0;
        m_loadLocalToggle = 0;
        m_locals = [];

        // Prologue (needed for AllocLocal/StoreLocal in print helpers)
        X86_64Encoder.PushR(m_text, Reg.RBP);
        X86_64Encoder.MovRR(m_text, Reg.RBP, Reg.RSP);
        foreach (byte reg in LocalRegs)
            X86_64Encoder.PushR(m_text, reg);
        int frameSizePatchOffset = m_text.Count;
        EmitSubRspImm32(0); // patched later

        if (m_target == X86_64Target.BareMetal)
        {
            // Bare metal: patch the 32→64 far jump to land here
            if (m_bareMetalLongModeJumpPatch >= 0)
            {
                int entryAddr = 0x100000 + m_text.Count - 1; // -1 because we're mid-function
                // Actually, the jump target is the instruction AFTER the prologue.
                // The far jump lands at __start which starts with push rbp etc.
                entryAddr = 0x100000 + m_functionOffsets["__start"];
                m_text[m_bareMetalLongModeJumpPatch + 1] = (byte)(entryAddr & 0xFF);
                m_text[m_bareMetalLongModeJumpPatch + 2] = (byte)((entryAddr >> 8) & 0xFF);
                m_text[m_bareMetalLongModeJumpPatch + 3] = (byte)((entryAddr >> 16) & 0xFF);
                m_text[m_bareMetalLongModeJumpPatch + 4] = (byte)((entryAddr >> 24) & 0xFF);
            }

            // Disable interrupts until IDT is ready
            X86_64Encoder.Cli(m_text);

            // Set up stack at 0x80000 (512KB, grows down)
            X86_64Encoder.Li(m_text, Reg.RSP, 0x80000);
            X86_64Encoder.MovRR(m_text, Reg.RBP, Reg.RSP);

            // Set up heap at 0x200000 (2MB — above identity-mapped pages)
            X86_64Encoder.Li(m_text, HeapReg, 0x200000);

            // Initialize process table (process 1 disabled for compiler test)
            EmitProcessSetup();
            // EmitProcess1Setup(); // disabled — let compiler run alone

            // Grant capabilities
            EmitGrantCapability(0, CAP_CONSOLE);    // kernel can write serial
            EmitGrantCapability(0, CAP_CONCURRENT);
            EmitGrantCapability(1, CAP_CONSOLE);    // process 1 can write serial

            // Set up syscall MSRs (handler already emitted, address known)
            EmitWriteMsr(0xC0000081, (0x08L << 32)); // STAR: kernel CS=0x08
            EmitWriteMsr(0xC0000082, m_syscallHandlerAddr); // LSTAR: handler entry
            EmitWriteMsr(0xC0000084, 0x200); // SFMASK: mask IF
            // Enable SCE in EFER
            X86_64Encoder.Li(m_text, Reg.RCX, 0xC0000080);
            m_text.Add(0x0F); m_text.Add(0x32); // rdmsr
            m_text.Add(0x48); m_text.Add(0x83); m_text.Add(0xC8); m_text.Add(0x01); // or rax, 1
            m_text.Add(0x0F); m_text.Add(0x30); // wrmsr

            // Initialize COM1 UART: 115200 baud, 8N1, FIFO enabled
            EmitOutByte(0x3F9, 0x00); // IER: disable all interrupts
            EmitOutByte(0x3FB, 0x80); // LCR: enable DLAB (set baud rate divisor)
            EmitOutByte(0x3F8, 0x01); // DLL: divisor low byte (115200 baud = 1)
            EmitOutByte(0x3F9, 0x00); // DLM: divisor high byte
            EmitOutByte(0x3FB, 0x03); // LCR: 8 bits, no parity, 1 stop bit (DLAB off)
            EmitOutByte(0x3FA, 0xC7); // FCR: enable FIFO, clear, 14-byte trigger
            EmitOutByte(0x3FC, 0x0B); // MCR: DTR + RTS + OUT2 (enable IRQ line)

            // Build IDT, load IDTR, init PIC, enable interrupts
            EmitIdtEntries();
            EmitInterruptSetup();
        }
        else
        {
            // Linux user mode: set up heap via brk(0) then brk(brk_result + 1MB)
            X86_64Encoder.Li(m_text, Reg.RAX, 12); // sys_brk
            X86_64Encoder.Li(m_text, Reg.RDI, 0);
            X86_64Encoder.Syscall(m_text);
            X86_64Encoder.MovRR(m_text, HeapReg, Reg.RAX); // heap start

            // Grow by 64MB (bump allocator needs headroom for large programs / deep TCO)
            byte growReg = Reg.R11;
            X86_64Encoder.Li(m_text, growReg, 64 * 1024 * 1024);
            X86_64Encoder.MovRR(m_text, Reg.RDI, Reg.RAX);
            X86_64Encoder.AddRR(m_text, Reg.RDI, growReg);
            X86_64Encoder.Li(m_text, Reg.RAX, 12); // sys_brk
            X86_64Encoder.Syscall(m_text);
        }

        IRDefinition? mainDef = null;
        foreach (IRDefinition def in module.Definitions)
        {
            if (def.Name == "main") { mainDef = def; break; }
        }

        if (mainDef is null)
        {
            X86_64Encoder.Li(m_text, Reg.RDI, 0);
            X86_64Encoder.Li(m_text, Reg.RAX, 60);
            X86_64Encoder.Syscall(m_text);
            return;
        }

        CodexType returnType = ComputeReturnType(mainDef.Type, mainDef.Parameters.Length);

        if (m_target == X86_64Target.BareMetal)
        {
            // Arena-based REPL loop: save heap as arena base, call main,
            // print result, restore heap, repeat. Each compilation gets a
            // fresh arena. The heap below the arena base is persistent
            // (currently empty — reserved for future REPL state).

            // Save current heap pointer as the arena base
            X86_64Encoder.Li(m_text, Reg.RDI, ArenaBaseAddr);
            X86_64Encoder.MovStore(m_text, Reg.RDI, HeapReg, 0);

            int replLoop = m_text.Count;

            // Restore heap to arena base (discard previous compilation's garbage)
            X86_64Encoder.Li(m_text, Reg.RDI, ArenaBaseAddr);
            X86_64Encoder.MovLoad(m_text, HeapReg, Reg.RDI, 0);

            EmitCallMainAndPrint(returnType);

            // Loop back — arena reset happens at top of loop
            X86_64Encoder.Jmp(m_text, replLoop - (m_text.Count + 5));
        }
        else
        {
            EmitCallMainAndPrint(returnType);

            X86_64Encoder.Li(m_text, Reg.RDI, 0);
            X86_64Encoder.Li(m_text, Reg.RAX, 60); // sys_exit
            X86_64Encoder.Syscall(m_text);
        }

        // Patch frame size for spill slots
        int frameSize = m_spillCount * 8;
        frameSize = (frameSize + 15) & ~15;
        m_text[frameSizePatchOffset + 3] = (byte)(frameSize & 0xFF);
        m_text[frameSizePatchOffset + 4] = (byte)((frameSize >> 8) & 0xFF);
        m_text[frameSizePatchOffset + 5] = (byte)((frameSize >> 16) & 0xFF);
        m_text[frameSizePatchOffset + 6] = (byte)((frameSize >> 24) & 0xFF);
    }

    void EmitCallMainAndPrint(CodexType returnType)
    {
        EmitCallTo("main");
        switch (returnType)
        {
            case IntegerType:
                X86_64Encoder.MovRR(m_text, Reg.RDI, Reg.RAX);
                EmitCallTo("__itoa");
                EmitPrintText(Reg.RAX);
                break;
            case BooleanType:
                EmitPrintBool(Reg.RAX);
                break;
            case TextType:
                EmitPrintText(Reg.RAX);
                break;
        }
    }

    static CodexType ComputeReturnType(CodexType type, int paramCount)
    {
        CodexType current = type;
        for (int i = 0; i < paramCount; i++)
        {
            if (current is FunctionType ft) current = ft.Return;
            else break;
        }
        // Unwrap EffectfulType to get the actual return type
        if (current is EffectfulType eft) current = eft.Return;
        return current;
    }

    // ── Register allocation ──────────────────────────────────────

    byte AllocTemp()
    {
        byte reg = TempRegs[m_nextTemp % TempRegs.Length];
        m_nextTemp++;
        return reg;
    }

    int AllocLocal()
    {
        if (m_nextLocal < LocalRegs.Length)
        {
            int reg = LocalRegs[m_nextLocal];
            m_nextLocal++;
            return reg;
        }
        // Spill to stack
        int slot = SpillBase + m_spillCount;
        m_spillCount++;
        return slot;
    }

    void StoreLocal(int local, byte valueReg)
    {
        if (local < SpillBase)
        {
            if (local != valueReg)
                X86_64Encoder.MovRR(m_text, (byte)local, valueReg);
        }
        else
        {
            int offset = -((local - SpillBase) + 1) * 8 - LocalRegs.Length * 8;
            X86_64Encoder.MovStore(m_text, Reg.RBP, valueReg, offset);
        }
    }

    byte LoadLocal(int local)
    {
        if (local < SpillBase)
            return (byte)local;
        byte scratch = (m_loadLocalToggle++ % 2 == 0) ? Reg.R8 : Reg.R9;
        int offset = -((local - SpillBase) + 1) * 8 - LocalRegs.Length * 8;
        X86_64Encoder.MovLoad(m_text, scratch, Reg.RBP, offset);
        return scratch;
    }

    // ── Rodata helpers ───────────────────────────────────────────

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

    void EmitLoadRodataAddress(byte rd, int rodataOffset)
    {
        m_rodataFixups.Add(new RodataFixup(m_text.Count + 2, rodataOffset));
        X86_64Encoder.MovRI64(m_text, rd, 0); // placeholder
    }

    void EmitLoadFunctionAddress(byte rd, string funcName)
    {
        // movabs rd, <text_vaddr + func_offset> — patched in PatchFuncAddrRefs
        m_funcAddrFixups.Add(new FuncAddrFixup(m_text.Count + 2, rd, funcName));
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
                int rel32 = targetOffset - (patchOffset + 5);
                m_text[patchOffset + 1] = (byte)(rel32 & 0xFF);
                m_text[patchOffset + 2] = (byte)((rel32 >> 8) & 0xFF);
                m_text[patchOffset + 3] = (byte)((rel32 >> 16) & 0xFF);
                m_text[patchOffset + 4] = (byte)((rel32 >> 24) & 0xFF);
            }
            else
            {
                Console.Error.WriteLine($"X86_64 WARNING: unresolved call to '{target}' at text offset {patchOffset}");
            }
        }
    }

    void PatchRodataRefs()
    {
        int textSize = m_text.Count;
        ulong rodataVaddr;

        if (m_target == X86_64Target.BareMetal)
        {
            // Bare metal: text at 0x100000, rodata follows text (aligned to 8)
            int rodataOffset = (textSize + 7) & ~7;
            rodataVaddr = 0x100000 + (ulong)rodataOffset;
        }
        else
        {
            rodataVaddr = ElfWriterX86_64.ComputeRodataVaddr(textSize);
        }

        foreach (RodataFixup fixup in m_rodataFixups)
        {
            ulong addr = rodataVaddr + (ulong)fixup.RodataOffset;
            byte[] bytes = BitConverter.GetBytes((long)addr);
            for (int i = 0; i < 8; i++)
                m_text[fixup.PatchOffset + i] = bytes[i];
        }

        // Patch function address references (for closures/trampolines)
        ulong textVaddr;
        if (m_target == X86_64Target.BareMetal)
        {
            // Bare metal: text loaded at 0x100000 (1MB), no file offset adjustment
            textVaddr = 0x100000;
        }
        else
        {
            int textFileOffset = ElfWriterX86_64.ComputeTextFileOffset();
            textVaddr = 0x400000UL + (ulong)textFileOffset;
        }

        foreach (FuncAddrFixup fixup in m_funcAddrFixups)
        {
            if (m_functionOffsets.TryGetValue(fixup.FuncName, out int funcOffset))
            {
                ulong addr = textVaddr + (ulong)funcOffset;
                byte[] bytes = BitConverter.GetBytes((long)addr);
                for (int i = 0; i < 8; i++)
                    m_text[fixup.PatchOffset + i] = bytes[i];
            }
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
