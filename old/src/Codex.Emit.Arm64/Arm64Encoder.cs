namespace Codex.Emit.Arm64;

static class Arm64Encoder
{
    // ═════════════════════════════════════════════════════════════
    // Data Processing — Register (shifted)
    // [sf | opc | 01011 | shift | 0 | Rm | imm6 | Rn | Rd]
    // ═════════════════════════════════════════════════════════════

    public static uint Add(uint rd, uint rn, uint rm) =>
        0x8B000000u | (rm << 16) | (rn << 5) | rd;

    public static uint Sub(uint rd, uint rn, uint rm) =>
        0xCB000000u | (rm << 16) | (rn << 5) | rd;

    public static uint And(uint rd, uint rn, uint rm) =>
        0x8A000000u | (rm << 16) | (rn << 5) | rd;

    public static uint Or(uint rd, uint rn, uint rm) =>
        0xAA000000u | (rm << 16) | (rn << 5) | rd;

    public static uint Xor(uint rd, uint rn, uint rm) =>
        0xCA000000u | (rm << 16) | (rn << 5) | rd;

    public static uint Lsl(uint rd, uint rn, uint rm) =>
        0x9AC02000u | (rm << 16) | (rn << 5) | rd;

    public static uint Lsr(uint rd, uint rn, uint rm) =>
        0x9AC02400u | (rm << 16) | (rn << 5) | rd;

    public static uint Asr(uint rd, uint rn, uint rm) =>
        0x9AC02800u | (rm << 16) | (rn << 5) | rd;

    // ── Multiply / Divide ────────────────────────────────────────

    public static uint Mul(uint rd, uint rn, uint rm) =>
        0x9B007C00u | (rm << 16) | (rn << 5) | rd;  // MADD rd, rn, rm, XZR

    public static uint Sdiv(uint rd, uint rn, uint rm) =>
        0x9AC00C00u | (rm << 16) | (rn << 5) | rd;

    // MSUB rd, rn, rm, ra => rd = ra - rn*rm (used to compute remainder)
    public static uint Msub(uint rd, uint rn, uint rm, uint ra) =>
        0x9B008000u | (rm << 16) | (ra << 10) | (rn << 5) | rd;

    // ── Compare (sets flags, discards result via XZR) ────────────

    public static uint Cmp(uint rn, uint rm) =>
        0xEB000000u | (rm << 16) | (rn << 5) | Arm64Reg.Xzr;  // SUBS XZR, Rn, Rm

    public static uint CmpImm(uint rn, int imm12) =>
        0xF100001Fu | ((uint)(imm12 & 0xFFF) << 10) | (rn << 5);  // SUBS XZR, Rn, #imm12

    // ── Conditional select (CSEL rd, rn, rm, cond) ───────────────

    public static uint Csel(uint rd, uint rn, uint rm, uint cond) =>
        0x9A800000u | (rm << 16) | (cond << 12) | (rn << 5) | rd;

    // Condition codes
    public const uint CondEq = 0;   // equal (Z=1)
    public const uint CondNe = 1;   // not equal (Z=0)
    public const uint CondLt = 11;  // signed less than (N!=V)
    public const uint CondGe = 10;  // signed greater or equal (N==V)
    public const uint CondLe = 13;  // signed less or equal (Z=1 || N!=V)
    public const uint CondGt = 12;  // signed greater than (Z=0 && N==V)

    // ═════════════════════════════════════════════════════════════
    // Data Processing — Immediate
    // ═════════════════════════════════════════════════════════════

    // ADD Xd, Xn, #imm12 (optionally shifted left 12)
    public static uint AddImm(uint rd, uint rn, int imm12) =>
        0x91000000u | ((uint)(imm12 & 0xFFF) << 10) | (rn << 5) | rd;

    // SUB Xd, Xn, #imm12
    public static uint SubImm(uint rd, uint rn, int imm12) =>
        0xD1000000u | ((uint)(imm12 & 0xFFF) << 10) | (rn << 5) | rd;

    // MOVZ Xd, #imm16, LSL #shift (shift = 0, 16, 32, 48)
    public static uint Movz(uint rd, int imm16, int shift = 0) =>
        0xD2800000u | ((uint)(shift / 16) << 21) | ((uint)(imm16 & 0xFFFF) << 5) | rd;

    // MOVK Xd, #imm16, LSL #shift (keep other bits)
    public static uint Movk(uint rd, int imm16, int shift = 0) =>
        0xF2800000u | ((uint)(shift / 16) << 21) | ((uint)(imm16 & 0xFFFF) << 5) | rd;

    // MOVN Xd, #imm16, LSL #shift (move wide NOT)
    public static uint Movn(uint rd, int imm16, int shift = 0) =>
        0x92800000u | ((uint)(shift / 16) << 21) | ((uint)(imm16 & 0xFFFF) << 5) | rd;

    // AND Xd, Xn, #bitmask_imm
    public static uint AndImm(uint rd, uint rn, int imm)
    {
        // For simple power-of-2 - 1 masks, encode the bitmask immediate.
        // ARM64 logical immediate encoding: N:immr:imms
        // For 0xFFF (12 bits): N=1, immr=0, imms=0b001011 (= 11)
        // For 0xFF (8 bits): N=1, immr=0, imms=0b000111 (= 7)
        // For -8 (= 0xFFFFFFFFFFFFFFF8): N=1, immr=61, imms=60 (61 ones rotated right by 61)
        uint n_immr_imms;
        if (imm == -8)
            n_immr_imms = (1u << 22) | (61u << 16) | (60u << 10); // N=1 immr=61 imms=60
        else if (imm == 0xFF)
            n_immr_imms = (1u << 22) | (0u << 16) | (7u << 10);
        else if (imm == 0xFFF)
            n_immr_imms = (1u << 22) | (0u << 16) | (11u << 10);
        else
            throw new ArgumentException($"Unsupported AND immediate: {imm}. Use register form.");

        return 0x92000000u | n_immr_imms | (rn << 5) | rd;
    }

    // ═════════════════════════════════════════════════════════════
    // Loads and Stores — unsigned offset
    // LDR Xt, [Xn, #imm] — imm is byte offset, must be 8-aligned for 64-bit
    // STR Xt, [Xn, #imm]
    // ═════════════════════════════════════════════════════════════

    // LDR Xt, [Xn, #pimm] (unsigned offset, scaled by 8)
    public static uint Ldr(uint rt, uint rn, int offset)
    {
        uint imm12 = (uint)((offset / 8) & 0xFFF);
        return 0xF9400000u | (imm12 << 10) | (rn << 5) | rt;
    }

    // STR Xt, [Xn, #pimm] (unsigned offset, scaled by 8)
    public static uint Str(uint rt, uint rn, int offset)
    {
        uint imm12 = (uint)((offset / 8) & 0xFFF);
        return 0xF9000000u | (imm12 << 10) | (rn << 5) | rt;
    }

    // LDRB Wt, [Xn, #pimm] (load byte, unsigned offset)
    public static uint Ldrb(uint rt, uint rn, int offset)
    {
        uint imm12 = (uint)(offset & 0xFFF);
        return 0x39400000u | (imm12 << 10) | (rn << 5) | rt;
    }

    // STRB Wt, [Xn, #pimm] (store byte, unsigned offset)
    public static uint Strb(uint rt, uint rn, int offset)
    {
        uint imm12 = (uint)(offset & 0xFFF);
        return 0x39000000u | (imm12 << 10) | (rn << 5) | rt;
    }

    // LDR Xt, [Xn, Xm] (register offset)
    public static uint LdrReg(uint rt, uint rn, uint rm) =>
        0xF8606800u | (rm << 16) | (rn << 5) | rt;

    // LDRB Wt, [Xn, Xm] (register offset, byte)
    public static uint LdrbReg(uint rt, uint rn, uint rm) =>
        0x38606800u | (rm << 16) | (rn << 5) | rt;

    // STRB Wt, [Xn, Xm] (register offset, byte)
    public static uint StrbReg(uint rt, uint rn, uint rm) =>
        0x38206800u | (rm << 16) | (rn << 5) | rt;

    // STP Xt1, Xt2, [Xn, #imm] (store pair, pre-index — for prologue)
    public static uint Stp(uint rt1, uint rt2, uint rn, int offset)
    {
        uint imm7 = (uint)((offset / 8) & 0x7F);
        return 0xA9000000u | (imm7 << 15) | (rt2 << 10) | (rn << 5) | rt1;
    }

    // LDP Xt1, Xt2, [Xn, #imm] (load pair — for epilogue)
    public static uint Ldp(uint rt1, uint rt2, uint rn, int offset)
    {
        uint imm7 = (uint)((offset / 8) & 0x7F);
        return 0xA9400000u | (imm7 << 15) | (rt2 << 10) | (rn << 5) | rt1;
    }

    // ═════════════════════════════════════════════════════════════
    // Branches
    // ═════════════════════════════════════════════════════════════

    // B offset (unconditional, PC-relative, 26-bit signed imm, scaled by 4)
    public static uint B(int offset)
    {
        uint imm26 = (uint)((offset / 4) & 0x3FFFFFF);
        return 0x14000000u | imm26;
    }

    // BL offset (branch with link = call)
    public static uint Bl(int offset)
    {
        uint imm26 = (uint)((offset / 4) & 0x3FFFFFF);
        return 0x94000000u | imm26;
    }

    // B.cond offset (conditional branch, 19-bit signed imm, scaled by 4)
    public static uint Bcond(uint cond, int offset)
    {
        uint imm19 = (uint)((offset / 4) & 0x7FFFF);
        return 0x54000000u | (imm19 << 5) | cond;
    }

    // Condition code helpers for Bcond
    public static uint Beq(int offset) => Bcond(0x0, offset * 4);  // EQ
    public static uint Bne(int offset) => Bcond(0x1, offset * 4);  // NE
    public static uint Bge(int offset) => Bcond(0xA, offset * 4);  // GE (signed)
    public static uint Blt(int offset) => Bcond(0xB, offset * 4);  // LT (signed)

    // CBZ Xt, offset (compare and branch if zero, 19-bit)
    public static uint Cbz(uint rt, int offset)
    {
        uint imm19 = (uint)((offset / 4) & 0x7FFFF);
        return 0xB4000000u | (imm19 << 5) | rt;
    }

    // CBNZ Xt, offset (compare and branch if not zero, 19-bit)
    public static uint Cbnz(uint rt, int offset)
    {
        uint imm19 = (uint)((offset / 4) & 0x7FFFF);
        return 0xB5000000u | (imm19 << 5) | rt;
    }

    // BR Xn (branch to register — indirect jump)
    public static uint Br(uint rn) =>
        0xD61F0000u | (rn << 5);

    // BLR Xn (branch with link to register — indirect call)
    public static uint Blr(uint rn) =>
        0xD63F0000u | (rn << 5);

    // RET {Xn} (default Xn=X30=LR)
    public static uint Ret(uint rn = Arm64Reg.Lr) =>
        0xD65F0000u | (rn << 5);

    // ═════════════════════════════════════════════════════════════
    // System
    // ═════════════════════════════════════════════════════════════

    // SVC #imm16 (supervisor call / syscall)
    public static uint Svc(int imm16 = 0) =>
        0xD4000001u | ((uint)(imm16 & 0xFFFF) << 5);

    // NOP
    public static uint Nop() => 0xD503201Fu;

    // ═════════════════════════════════════════════════════════════
    // Pseudo-instructions
    // ═════════════════════════════════════════════════════════════

    // ADD encodes reg 31 as SP; ORR encodes it as XZR
    public static uint Mov(uint rd, uint rm)
    {
        if (rm == Arm64Reg.Sp || rd == Arm64Reg.Sp) return AddImm(rd, rm, 0);
        return Or(rd, Arm64Reg.Xzr, rm);
    }

    // MOV Xd, #imm (alias for MOVZ when non-negative, MOVN when negative)
    public static uint MovImm(uint rd, long value)
    {
        if (value >= 0 && value <= 0xFFFF)
            return Movz(rd, (int)value);
        if (value < 0 && value >= -0x10000)
            return Movn(rd, (int)(~value));
        throw new ArgumentException($"MovImm value {value} doesn't fit in 16 bits. Use Li().");
    }

    public static uint[] Li(uint rd, long value)
    {
        if (value >= 0 && value <= 0xFFFF)
            return [Movz(rd, (int)value)];

        if (value < 0 && value >= -0x10000)
            return [Movn(rd, (int)(~value))];

        if (value >= 0 && value <= 0xFFFFFFFF)
        {
            int lo16 = (int)(value & 0xFFFF);
            int hi16 = (int)((value >> 16) & 0xFFFF);
            if (hi16 == 0)
                return [Movz(rd, lo16)];
            if (lo16 == 0)
                return [Movz(rd, hi16, 16)];
            return [Movz(rd, lo16), Movk(rd, hi16, 16)];
        }

        List<uint> insns = [];
        bool first = true;
        for (int shift = 0; shift < 64; shift += 16)
        {
            int chunk = (int)((value >> shift) & 0xFFFF);
            if (chunk != 0 || (shift == 0 && value == 0))
            {
                if (first)
                {
                    insns.Add(Movz(rd, chunk, shift));
                    first = true;   // next chunks use MOVK
                    first = false;
                }
                else
                {
                    insns.Add(Movk(rd, chunk, shift));
                }
            }
        }

        if (insns.Count == 0)
            insns.Add(Movz(rd, 0));

        return [.. insns];
    }

    // NEG Xd, Xm => SUB Xd, XZR, Xm
    public static uint Neg(uint rd, uint rm) => Sub(rd, Arm64Reg.Xzr, rm);
}

// AArch64 register constants. ARM64 has 31 GP registers (X0-X30),
// SP (stack pointer, encoded as 31 in some contexts), and
// XZR (zero register, also encoded as 31 in other contexts).
static class Arm64Reg
{
    // Arguments / return value (caller-saved)
    public const uint X0 = 0;
    public const uint X1 = 1;
    public const uint X2 = 2;
    public const uint X3 = 3;
    public const uint X4 = 4;
    public const uint X5 = 5;
    public const uint X6 = 6;
    public const uint X7 = 7;

    // Indirect result location (caller-saved)
    public const uint X8 = 8;

    // Temporaries (caller-saved)
    public const uint X9 = 9;
    public const uint X10 = 10;
    public const uint X11 = 11;
    public const uint X12 = 12;
    public const uint X13 = 13;
    public const uint X14 = 14;
    public const uint X15 = 15;

    // Intra-procedure scratch (caller-saved, not for general use)
    public const uint X16 = 16;
    public const uint X17 = 17;

    // Platform register (reserved)
    public const uint X18 = 18;

    // Callee-saved registers
    public const uint X19 = 19;
    public const uint X20 = 20;
    public const uint X21 = 21;
    public const uint X22 = 22;
    public const uint X23 = 23;
    public const uint X24 = 24;
    public const uint X25 = 25;
    public const uint X26 = 26;
    public const uint X27 = 27;
    public const uint X28 = 28;

    // Frame pointer (callee-saved)
    public const uint Fp = 29;

    // Link register (return address)
    public const uint Lr = 30;

    // Zero register / Stack pointer (context-dependent, encoded as 31)
    public const uint Xzr = 31;
    public const uint Sp = 31;  // Same encoding, different context
}
