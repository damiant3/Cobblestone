namespace Codex.Emit.RiscV;

// Encodes RISC-V RV64IMD instructions as 32-bit words.
// All methods are pure — they take operands and return a uint.
static class RiscVEncoder
{
    // ── R-type: [funct7 | rs2 | rs1 | funct3 | rd | opcode] ─────

    static uint RType(uint opcode, uint rd, uint funct3, uint rs1, uint rs2, uint funct7)
        => opcode | (rd << 7) | (funct3 << 12) | (rs1 << 15) | (rs2 << 20) | (funct7 << 25);

    // ── I-type: [imm12 | rs1 | funct3 | rd | opcode] ────────────

    static uint IType(uint opcode, uint rd, uint funct3, uint rs1, int imm12)
        => opcode | (rd << 7) | (funct3 << 12) | (rs1 << 15) | ((uint)(imm12 & 0xFFF) << 20);

    // ── S-type: [imm7 | rs2 | rs1 | funct3 | imm5 | opcode] ────

    static uint SType(uint opcode, uint funct3, uint rs1, uint rs2, int imm12)
    {
        uint imm = (uint)(imm12 & 0xFFF);
        uint lo5 = imm & 0x1F;
        uint hi7 = (imm >> 5) & 0x7F;
        return opcode | (lo5 << 7) | (funct3 << 12) | (rs1 << 15) | (rs2 << 20) | (hi7 << 25);
    }

    // ── B-type: [imm | rs2 | rs1 | funct3 | imm | opcode] ──────

    static uint BType(uint opcode, uint funct3, uint rs1, uint rs2, int imm13)
    {
        // imm13 is byte offset, must be even (bit 0 is always 0 and not stored)
        uint imm = (uint)(imm13 & 0x1FFE); // mask bits 12:1
        uint bit11 = (imm >> 11) & 1;
        uint bits4_1 = (imm >> 1) & 0xF;
        uint bits10_5 = (imm >> 5) & 0x3F;
        uint bit12 = (imm >> 12) & 1;
        return opcode | (bit11 << 7) | (bits4_1 << 8) | (funct3 << 12)
            | (rs1 << 15) | (rs2 << 20) | (bits10_5 << 25) | (bit12 << 31);
    }

    // ── U-type: [imm20 | rd | opcode] ───────────────────────────

    static uint UType(uint opcode, uint rd, int imm20)
        => opcode | (rd << 7) | ((uint)(imm20 & 0xFFFFF) << 12);

    // ── J-type: [imm | rd | opcode] ─────────────────────────────

    static uint JType(uint opcode, uint rd, int imm21)
    {
        // imm21 is byte offset, bit 0 not stored
        uint imm = (uint)(imm21 & 0x1FFFFF);
        uint bits19_12 = (imm >> 12) & 0xFF;
        uint bit11 = (imm >> 11) & 1;
        uint bits10_1 = (imm >> 1) & 0x3FF;
        uint bit20 = (imm >> 20) & 1;
        return opcode | (rd << 7) | (bits19_12 << 12) | (bit11 << 20)
            | (bits10_1 << 21) | (bit20 << 31);
    }

    // ═════════════════════════════════════════════════════════════
    // Integer arithmetic (RV64I + M extension)
    // ═════════════════════════════════════════════════════════════

    public static uint Add(uint rd, uint rs1, uint rs2) => RType(0x33, rd, 0x0, rs1, rs2, 0x00);
    public static uint Sub(uint rd, uint rs1, uint rs2) => RType(0x33, rd, 0x0, rs1, rs2, 0x20);
    public static uint Mul(uint rd, uint rs1, uint rs2) => RType(0x33, rd, 0x0, rs1, rs2, 0x01);
    public static uint Div(uint rd, uint rs1, uint rs2) => RType(0x33, rd, 0x4, rs1, rs2, 0x01);
    public static uint Rem(uint rd, uint rs1, uint rs2) => RType(0x33, rd, 0x6, rs1, rs2, 0x01);

    // 64-bit word variants (RV64 W-suffix)
    public static uint Addw(uint rd, uint rs1, uint rs2) => RType(0x3B, rd, 0x0, rs1, rs2, 0x00);
    public static uint Subw(uint rd, uint rs1, uint rs2) => RType(0x3B, rd, 0x0, rs1, rs2, 0x20);
    public static uint Mulw(uint rd, uint rs1, uint rs2) => RType(0x3B, rd, 0x0, rs1, rs2, 0x01);
    public static uint Divw(uint rd, uint rs1, uint rs2) => RType(0x3B, rd, 0x4, rs1, rs2, 0x01);
    public static uint Remw(uint rd, uint rs1, uint rs2) => RType(0x3B, rd, 0x6, rs1, rs2, 0x01);

    // Logical
    public static uint And(uint rd, uint rs1, uint rs2) => RType(0x33, rd, 0x7, rs1, rs2, 0x00);
    public static uint Or(uint rd, uint rs1, uint rs2) => RType(0x33, rd, 0x6, rs1, rs2, 0x00);
    public static uint Xor(uint rd, uint rs1, uint rs2) => RType(0x33, rd, 0x4, rs1, rs2, 0x00);

    // Shifts
    public static uint Sll(uint rd, uint rs1, uint rs2) => RType(0x33, rd, 0x1, rs1, rs2, 0x00);
    public static uint Srl(uint rd, uint rs1, uint rs2) => RType(0x33, rd, 0x5, rs1, rs2, 0x00);
    public static uint Sra(uint rd, uint rs1, uint rs2) => RType(0x33, rd, 0x5, rs1, rs2, 0x20);

    // Set-less-than
    public static uint Slt(uint rd, uint rs1, uint rs2) => RType(0x33, rd, 0x2, rs1, rs2, 0x00);
    public static uint Sltu(uint rd, uint rs1, uint rs2) => RType(0x33, rd, 0x3, rs1, rs2, 0x00);

    // ═════════════════════════════════════════════════════════════
    // Immediate arithmetic
    // ═════════════════════════════════════════════════════════════

    public static uint Addi(uint rd, uint rs1, int imm) => IType(0x13, rd, 0x0, rs1, imm);
    public static uint Andi(uint rd, uint rs1, int imm) => IType(0x13, rd, 0x7, rs1, imm);
    public static uint Ori(uint rd, uint rs1, int imm) => IType(0x13, rd, 0x6, rs1, imm);
    public static uint Xori(uint rd, uint rs1, int imm) => IType(0x13, rd, 0x4, rs1, imm);
    public static uint Slti(uint rd, uint rs1, int imm) => IType(0x13, rd, 0x2, rs1, imm);

    // Shift immediate (RV64I: 6-bit shift amount in imm[5:0])
    public static uint Slli(uint rd, uint rs1, int shamt) => IType(0x13, rd, 0x1, rs1, shamt & 0x3F);
    public static uint Srli(uint rd, uint rs1, int shamt) => IType(0x13, rd, 0x5, rs1, shamt & 0x3F);

    // 64-bit word immediate
    public static uint Addiw(uint rd, uint rs1, int imm) => IType(0x1B, rd, 0x0, rs1, imm);

    // ═════════════════════════════════════════════════════════════
    // Loads (I-type)
    // ═════════════════════════════════════════════════════════════

    public static uint Ld(uint rd, uint rs1, int offset) => IType(0x03, rd, 0x3, rs1, offset);
    public static uint Lw(uint rd, uint rs1, int offset) => IType(0x03, rd, 0x2, rs1, offset);
    public static uint Lh(uint rd, uint rs1, int offset) => IType(0x03, rd, 0x1, rs1, offset);
    public static uint Lb(uint rd, uint rs1, int offset) => IType(0x03, rd, 0x0, rs1, offset);
    public static uint Lbu(uint rd, uint rs1, int offset) => IType(0x03, rd, 0x4, rs1, offset);

    // ═════════════════════════════════════════════════════════════
    // Stores (S-type)
    // ═════════════════════════════════════════════════════════════

    public static uint Sd(uint rs1, uint rs2, int offset) => SType(0x23, 0x3, rs1, rs2, offset);
    public static uint Sw(uint rs1, uint rs2, int offset) => SType(0x23, 0x2, rs1, rs2, offset);
    public static uint Sh(uint rs1, uint rs2, int offset) => SType(0x23, 0x1, rs1, rs2, offset);
    public static uint Sb(uint rs1, uint rs2, int offset) => SType(0x23, 0x0, rs1, rs2, offset);

    // ═════════════════════════════════════════════════════════════
    // Branches (B-type) — offset is in bytes, must be even
    // ═════════════════════════════════════════════════════════════

    public static uint Beq(uint rs1, uint rs2, int offset) => BType(0x63, 0x0, rs1, rs2, offset);
    public static uint Bne(uint rs1, uint rs2, int offset) => BType(0x63, 0x1, rs1, rs2, offset);
    public static uint Blt(uint rs1, uint rs2, int offset) => BType(0x63, 0x4, rs1, rs2, offset);
    public static uint Bge(uint rs1, uint rs2, int offset) => BType(0x63, 0x5, rs1, rs2, offset);

    // ═════════════════════════════════════════════════════════════
    // Upper immediate
    // ═════════════════════════════════════════════════════════════

    public static uint Lui(uint rd, int imm20) => UType(0x37, rd, imm20);
    public static uint Auipc(uint rd, int imm20) => UType(0x17, rd, imm20);

    // ═════════════════════════════════════════════════════════════
    // Jumps
    // ═════════════════════════════════════════════════════════════

    public static uint Jal(uint rd, int offset) => JType(0x6F, rd, offset);
    public static uint Jalr(uint rd, uint rs1, int offset) => IType(0x67, rd, 0x0, rs1, offset);

    // ═════════════════════════════════════════════════════════════
    // System
    // ═════════════════════════════════════════════════════════════

    public static uint Ecall() => IType(0x73, 0, 0x0, 0, 0);
    public static uint Ebreak() => IType(0x73, 0, 0x0, 0, 1);

    // ═════════════════════════════════════════════════════════════
    // Pseudo-instructions (expand to real instructions)
    // ═════════════════════════════════════════════════════════════

    public static uint Nop() => Addi(0, 0, 0);
    public static uint Mv(uint rd, uint rs) => Addi(rd, rs, 0);
    public static uint Ret() => Jalr(0, 1, 0); // jalr x0, ra, 0
    public static uint J(int offset) => Jal(0, offset); // jal x0, offset
    public static uint Call(int offset) => Jal(1, offset); // jal ra, offset

    // Load a full 64-bit immediate into rd. Returns 2-8 instructions.
    // For small values (fits in 12-bit signed), returns a single addi.
    public static uint[] Li(uint rd, long value)
    {
        // Small immediate: fits in signed 12-bit
        if (value >= -2048 && value <= 2047)
        {
            return new[] { Addi(rd, 0, (int)value) };
        }

        // 32-bit range: lui + addi
        if (value >= int.MinValue && value <= int.MaxValue)
        {
            int val = (int)value;
            int hi20 = (val + 0x800) >> 12; // round up for sign extension
            int lo12 = val - (hi20 << 12);
            if (lo12 == 0)
                return new[] { Lui(rd, hi20) };
            return new[] { Lui(rd, hi20), Addiw(rd, rd, lo12) };
        }

        // Full 64-bit: build in stages
        // Split into 32-bit high and 32-bit low.
        // When lo32 is negative (bit 31 set), lui will sign-extend to set
        // upper 32 bits.  Bump hi32 by 1 so the add cancels those bits out.
        int lo32 = (int)(value & 0xFFFFFFFF);
        int hi32 = (int)(value >> 32);
        if (lo32 < 0) hi32++;

        List<uint> insns = new();

        // Load upper 32 bits into rd
        uint[] hiLoad = Li(rd, hi32);
        insns.AddRange(hiLoad);

        // Shift left 32
        insns.Add(IType(0x13, rd, 0x1, rd, 32)); // slli rd, rd, 32

        // Add lower 32 bits
        if (lo32 != 0)
        {
            // Use a temp register (t0 = x5) to load lo32, then add
            uint[] loLoad = Li(5, lo32);
            insns.AddRange(loLoad);
            insns.Add(Add(rd, rd, 5));
        }

        return insns.ToArray();
    }
}

// ABI register names as constants for readability.
static class Reg
{
    public const uint Zero = 0;
    public const uint Ra = 1;    // return address
    public const uint Sp = 2;    // stack pointer
    public const uint Gp = 3;    // global pointer
    public const uint Tp = 4;    // thread pointer
    public const uint T0 = 5;    // temporaries
    public const uint T1 = 6;
    public const uint T2 = 7;
    public const uint S0 = 8;    // frame pointer / callee-saved
    public const uint S1 = 9;
    public const uint A0 = 10;   // args / return value
    public const uint A1 = 11;
    public const uint A2 = 12;
    public const uint A3 = 13;
    public const uint A4 = 14;
    public const uint A5 = 15;
    public const uint A6 = 16;
    public const uint A7 = 17;   // syscall number
    public const uint S2 = 18;   // callee-saved
    public const uint S3 = 19;
    public const uint S4 = 20;
    public const uint S5 = 21;
    public const uint S6 = 22;
    public const uint S7 = 23;
    public const uint S8 = 24;
    public const uint S9 = 25;
    public const uint S10 = 26;
    public const uint S11 = 27;
    public const uint T3 = 28;   // temporaries
    public const uint T4 = 29;
    public const uint T5 = 30;
    public const uint T6 = 31;
}
