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
        m_currentFunction = def.Name;
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

        // Bind parameters (first 6 in registers, rest on stack per System V AMD64)
        for (int i = 0; i < def.Parameters.Length; i++)
        {
            int local = AllocLocal();
            if (i < Reg.ArgRegs.Length)
            {
                StoreLocal(local, Reg.ArgRegs[i]);
            }
            else
            {
                // Stack params at [rbp+16], [rbp+24], ... (after saved rbp + return addr)
                int stackOffset = 16 + (i - Reg.ArgRegs.Length) * 8;
                byte tmp = AllocTemp();
                X86_64Encoder.MovLoad(m_text, tmp, Reg.RBP, stackOffset);
                StoreLocal(local, tmp);
            }
            m_locals[def.Parameters[i].Name] = local;
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
        byte cond = EmitExpr(ifExpr.Condition);
        X86_64Encoder.TestRR(m_text, cond, cond);

        // je else_branch
        int jeFalseOffset = m_text.Count;
        X86_64Encoder.Jcc(m_text, X86_64Encoder.CC_E, 0); // patched

        // Then branch
        byte thenResult = EmitExpr(ifExpr.Then);
        int resultLocal = AllocLocal();
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
        int local = AllocLocal();
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
        byte scrutReg = EmitExpr(match.Scrutinee);
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
            case "write-file" when args.Count == 2:
            {
                byte pathReg = EmitExpr(args[0]);
                int savedPath = AllocLocal();
                StoreLocal(savedPath, pathReg);
                byte contentReg = EmitExpr(args[1]);
                // Simplified: write content to stdout
                X86_64Encoder.MovLoad(m_text, Reg.RDX, contentReg, 0); // len
                X86_64Encoder.Lea(m_text, Reg.RSI, contentReg, 8);     // data
                X86_64Encoder.Li(m_text, Reg.RAX, 1); // sys_write
                X86_64Encoder.Li(m_text, Reg.RDI, 1); // stdout
                X86_64Encoder.Syscall(m_text);
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
                    // Result = str[8 + idx] as a 1-char string on heap
                    X86_64Encoder.AddRR(m_text, idx, strLoaded);
                    X86_64Encoder.MovzxByte(m_text, idx, idx, 8);
                    // Allocate 1-char string on heap: [len=1][byte]
                    int charAtSaved = AllocLocal();
                    StoreLocal(charAtSaved, idx); // save the byte value
                    int charAtPtr = AllocLocal();
                    byte charAtTmp = AllocTemp();
                    X86_64Encoder.MovRR(m_text, charAtTmp, HeapReg);
                    StoreLocal(charAtPtr, charAtTmp);
                    X86_64Encoder.AddRI(m_text, HeapReg, 16);
                    X86_64Encoder.Li(m_text, Reg.R11, 1);
                    X86_64Encoder.MovStore(m_text, LoadLocal(charAtPtr), Reg.R11, 0);
                    X86_64Encoder.MovStoreByte(m_text, LoadLocal(charAtPtr), LoadLocal(charAtSaved), 8);
                    return LoadLocal(charAtPtr);
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
                byte textReg = EmitExpr(args[0]);
                byte rd = AllocTemp();
                X86_64Encoder.MovzxByte(m_text, rd, textReg, 8);
                return rd;
            }
            case "code-to-char" when args.Count >= 1:
            {
                byte codeReg = EmitExpr(args[0]);
                int savedCode = AllocLocal();
                StoreLocal(savedCode, codeReg);
                int c2cPtr = AllocLocal();
                byte c2cTmp = AllocTemp();
                X86_64Encoder.MovRR(m_text, c2cTmp, HeapReg);
                StoreLocal(c2cPtr, c2cTmp);
                X86_64Encoder.AddRI(m_text, HeapReg, 16);
                X86_64Encoder.Li(m_text, Reg.R11, 1);
                X86_64Encoder.MovStore(m_text, LoadLocal(c2cPtr), Reg.R11, 0);
                byte code = LoadLocal(savedCode);
                X86_64Encoder.MovStoreByte(m_text, LoadLocal(c2cPtr), code, 8);
                return LoadLocal(c2cPtr);
            }
            case "is-letter" when args.Count >= 1:
            {
                byte textReg = EmitExpr(args[0]);
                byte rd = AllocTemp();
                X86_64Encoder.MovzxByte(m_text, rd, textReg, 8); // first byte
                // Check lowercase: rd >= 'a' && rd <= 'z'
                byte lo = AllocTemp();
                X86_64Encoder.MovRR(m_text, lo, rd);
                X86_64Encoder.SubRI(m_text, lo, 'a');
                X86_64Encoder.CmpRI(m_text, lo, 'z' - 'a');
                X86_64Encoder.Setcc(m_text, X86_64Encoder.CC_LE, lo);
                // Check uppercase: rd >= 'A' && rd <= 'Z'
                byte hi = AllocTemp();
                X86_64Encoder.MovRR(m_text, hi, rd);
                X86_64Encoder.SubRI(m_text, hi, 'A');
                X86_64Encoder.CmpRI(m_text, hi, 'Z' - 'A');
                X86_64Encoder.Setcc(m_text, X86_64Encoder.CC_LE, hi);
                // Result = lower || upper (both are 0 or 1 in low byte)
                byte result = AllocTemp();
                X86_64Encoder.MovRR(m_text, result, lo);
                // or result, hi — need to add OrRR or use existing mechanism
                X86_64Encoder.AddRR(m_text, result, hi); // 0+0=0, 0+1=1, 1+0=1
                // Clamp to 0/1
                X86_64Encoder.CmpRI(m_text, result, 0);
                X86_64Encoder.Setcc(m_text, X86_64Encoder.CC_NE, result);
                X86_64Encoder.MovzxByteSelf(m_text, result);
                return result;
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
        // Linux x86-64 syscall write: rax=1, rdi=1(stdout), rsi=buf, rdx=len
        int savedPtr = AllocLocal();
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
        EmitStrReplaceHelper();
        EmitTextContainsHelper();
        EmitTextStartsWithHelper();
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

        // Set up heap via brk(0) then brk(brk_result + 1MB)
        X86_64Encoder.Li(m_text, Reg.RAX, 12); // sys_brk
        X86_64Encoder.Li(m_text, Reg.RDI, 0);
        X86_64Encoder.Syscall(m_text);
        X86_64Encoder.MovRR(m_text, HeapReg, Reg.RAX); // heap start

        // Grow by 1MB
        byte growReg = Reg.R11;
        X86_64Encoder.Li(m_text, growReg, 1024 * 1024);
        X86_64Encoder.MovRR(m_text, Reg.RDI, Reg.RAX);
        X86_64Encoder.AddRR(m_text, Reg.RDI, growReg);
        X86_64Encoder.Li(m_text, Reg.RAX, 12); // sys_brk
        X86_64Encoder.Syscall(m_text);

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

        EmitCallTo("main");

        CodexType returnType = ComputeReturnType(mainDef.Type, mainDef.Parameters.Length);
        switch (returnType)
        {
            case IntegerType:
                // Convert to text via __itoa, then print
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

        // Exit with code 0
        X86_64Encoder.Li(m_text, Reg.RDI, 0);
        X86_64Encoder.Li(m_text, Reg.RAX, 60); // sys_exit
        X86_64Encoder.Syscall(m_text);

        // Patch frame size for spill slots
        int frameSize = m_spillCount * 8;
        frameSize = (frameSize + 15) & ~15;
        m_text[frameSizePatchOffset + 3] = (byte)(frameSize & 0xFF);
        m_text[frameSizePatchOffset + 4] = (byte)((frameSize >> 8) & 0xFF);
        m_text[frameSizePatchOffset + 5] = (byte)((frameSize >> 16) & 0xFF);
        m_text[frameSizePatchOffset + 6] = (byte)((frameSize >> 24) & 0xFF);
    }

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
        ulong rodataVaddr = ElfWriterX86_64.ComputeRodataVaddr(textSize);

        foreach (RodataFixup fixup in m_rodataFixups)
        {
            ulong addr = rodataVaddr + (ulong)fixup.RodataOffset;
            byte[] bytes = BitConverter.GetBytes((long)addr);
            for (int i = 0; i < 8; i++)
                m_text[fixup.PatchOffset + i] = bytes[i];
        }

        // Patch function address references (for closures/trampolines)
        int textFileOffset = ElfWriterX86_64.ComputeTextFileOffset();
        ulong textVaddr = 0x400000UL + (ulong)textFileOffset;

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
