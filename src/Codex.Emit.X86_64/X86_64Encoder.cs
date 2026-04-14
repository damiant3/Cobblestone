namespace Codex.Emit.X86_64;

// x86-64 instruction encoder. Variable-length encoding (1-15 bytes).
// All methods append to a byte list. Register numbering follows AMD64:
//   RAX=0, RCX=1, RDX=2, RBX=3, RSP=4, RBP=5, RSI=6, RDI=7,
//   R8=8, R9=9, R10=10, R11=11, R12=12, R13=13, R14=14, R15=15
static class Reg
{
    public const byte RAX = 0;
    public const byte RCX = 1;
    public const byte RDX = 2;
    public const byte RBX = 3;
    public const byte RSP = 4;
    public const byte RBP = 5;
    public const byte RSI = 6;
    public const byte RDI = 7;
    public const byte R8  = 8;
    public const byte R9  = 9;
    public const byte R10 = 10;
    public const byte R11 = 11;
    public const byte R12 = 12;
    public const byte R13 = 13;
    public const byte R14 = 14;
    public const byte R15 = 15;

    // Calling convention (System V AMD64):
    //   Args:    RDI, RSI, RDX, RCX, R8, R9
    //   Return:  RAX
    //   Callee-saved: RBX, RBP, R12-R15
    //   Caller-saved: RAX, RCX, RDX, RSI, RDI, R8-R11
    public static readonly byte[] ArgRegs = [RDI, RSI, RDX, RCX, R8, R9];
    public static readonly byte[] CalleeSaved = [RBX, R12, R13, R14, R15];
    // RBP used as frame pointer, not in callee-saved rotation
}

static class X86_64Encoder
{
    // ═════════════════════════════════════════════════════════════
    // REX prefix helpers
    // ═════════════════════════════════════════════════════════════

    // REX.W = 64-bit operand size
    static byte Rex(bool w, bool r, bool x, bool b) =>
        (byte)(0x40 | (w ? 8 : 0) | (r ? 4 : 0) | (x ? 2 : 0) | (b ? 1 : 0));

    static byte RexW(byte reg, byte rm) =>
        Rex(true, reg >= 8, false, rm >= 8);

    static byte ModRM(byte mod, byte reg, byte rm) =>
        (byte)((mod << 6) | ((reg & 7) << 3) | (rm & 7));

    // SIB byte for RSP/R12-based addressing (RSP as rm requires SIB)
    static byte Sib(byte scale, byte index, byte baseReg) =>
        (byte)((scale << 6) | ((index & 7) << 3) | (baseReg & 7));

    // ═════════════════════════════════════════════════════════════
    // MOV instructions
    // ═════════════════════════════════════════════════════════════

    // mov rd, rs (64-bit register to register)
    public static void MovRR(List<byte> buf, byte rd, byte rs)
    {
        buf.Add(RexW(rs, rd));
        buf.Add(0x89); // MOV r/m64, r64
        buf.Add(ModRM(0b11, rs, rd));
    }

    // mov rd, imm64 (movabs — 10-byte encoding)
    public static void MovRI64(List<byte> buf, byte rd, long imm)
    {
        buf.Add(Rex(true, false, false, rd >= 8));
        buf.Add((byte)(0xB8 + (rd & 7)));
        WriteI64(buf, imm);
    }

    // mov rd, imm32 (sign-extended to 64-bit)
    public static void MovRI32(List<byte> buf, byte rd, int imm)
    {
        buf.Add(RexW(0, rd));
        buf.Add(0xC7);
        buf.Add(ModRM(0b11, 0, rd));
        WriteI32(buf, imm);
    }

    // mov rd, [rs + offset] (64-bit load)
    public static void MovLoad(List<byte> buf, byte rd, byte rs, int offset)
    {
        buf.Add(RexW(rd, rs));
        buf.Add(0x8B); // MOV r64, r/m64
        EmitMemOperand(buf, rd, rs, offset);
    }

    // mov [rd + offset], rs (64-bit store)
    public static void MovStore(List<byte> buf, byte rd, byte rs, int offset)
    {
        buf.Add(RexW(rs, rd));
        buf.Add(0x89); // MOV r/m64, r64
        EmitMemOperand(buf, rs, rd, offset);
    }

    // mov rd, [rip + disp32] (RIP-relative load, 7 bytes)
    public static void MovLoadRipRel(List<byte> buf, byte rd, int disp32)
    {
        buf.Add(Rex(true, rd >= 8, false, false));
        buf.Add(0x8B); // MOV r64, r/m64
        buf.Add(ModRM(0b00, rd, 5)); // rm=101 → RIP-relative
        WriteI32(buf, disp32);
    }

    // mov [rip + disp32], rs (RIP-relative store, 7 bytes)
    public static void MovStoreRipRel(List<byte> buf, byte rs, int disp32)
    {
        buf.Add(Rex(true, rs >= 8, false, false));
        buf.Add(0x89); // MOV r/m64, r64
        buf.Add(ModRM(0b00, rs, 5)); // rm=101 → RIP-relative
        WriteI32(buf, disp32);
    }

    // movzx rd, byte [rs + offset] (zero-extend byte load)
    public static void MovzxByte(List<byte> buf, byte rd, byte rs, int offset)
    {
        buf.Add(RexW(rd, rs));
        buf.Add(0x0F);
        buf.Add(0xB6);
        EmitMemOperand(buf, rd, rs, offset);
    }

    // mov byte [rd + offset], rs_low (byte store)
    public static void MovStoreByte(List<byte> buf, byte rd, byte rs, int offset)
    {
        // REX needed for SPL/BPL/SIL/DIL or extended registers
        byte rex = Rex(false, rs >= 8, false, rd >= 8);
        if (rex != 0x40 || rs >= 4) // need REX for uniform byte access
            buf.Add(rex);
        buf.Add(0x88); // MOV r/m8, r8
        EmitMemOperand(buf, rs, rd, offset);
    }

    // ═════════════════════════════════════════════════════════════
    // Arithmetic
    // ═════════════════════════════════════════════════════════════

    // add rd, rs
    public static void AddRR(List<byte> buf, byte rd, byte rs)
    {
        buf.Add(RexW(rs, rd));
        buf.Add(0x01);
        buf.Add(ModRM(0b11, rs, rd));
    }

    // add rd, imm32
    public static void AddRI(List<byte> buf, byte rd, int imm)
    {
        if (imm >= -128 && imm <= 127)
        {
            buf.Add(RexW(0, rd));
            buf.Add(0x83);
            buf.Add(ModRM(0b11, 0, rd));
            buf.Add((byte)(sbyte)imm);
        }
        else
        {
            buf.Add(RexW(0, rd));
            buf.Add(0x81);
            buf.Add(ModRM(0b11, 0, rd));
            WriteI32(buf, imm);
        }
    }

    // sub rd, rs
    public static void SubRR(List<byte> buf, byte rd, byte rs)
    {
        buf.Add(RexW(rs, rd));
        buf.Add(0x29);
        buf.Add(ModRM(0b11, rs, rd));
    }

    // sub rd, imm32
    public static void SubRI(List<byte> buf, byte rd, int imm)
    {
        if (imm >= -128 && imm <= 127)
        {
            buf.Add(RexW(0, rd));
            buf.Add(0x83);
            buf.Add(ModRM(0b11, 5, rd));
            buf.Add((byte)(sbyte)imm);
        }
        else
        {
            buf.Add(RexW(0, rd));
            buf.Add(0x81);
            buf.Add(ModRM(0b11, 5, rd));
            WriteI32(buf, imm);
        }
    }

    // imul rd, rs (signed multiply, result in rd)
    public static void ImulRR(List<byte> buf, byte rd, byte rs)
    {
        buf.Add(RexW(rd, rs));
        buf.Add(0x0F);
        buf.Add(0xAF);
        buf.Add(ModRM(0b11, rd, rs));
    }

    // neg rd (two's complement negate)
    public static void NegR(List<byte> buf, byte rd)
    {
        buf.Add(RexW(0, rd));
        buf.Add(0xF7);
        buf.Add(ModRM(0b11, 3, rd));
    }

    // cqo (sign-extend RAX into RDX:RAX for idiv)
    public static void Cqo(List<byte> buf)
    {
        buf.Add(Rex(true, false, false, false));
        buf.Add(0x99);
    }

    // idiv rs (signed divide RDX:RAX by rs, quotient in RAX, remainder in RDX)
    public static void IdivR(List<byte> buf, byte rs)
    {
        buf.Add(RexW(0, rs));
        buf.Add(0xF7);
        buf.Add(ModRM(0b11, 7, rs));
    }

    // and rd, rs
    public static void AndRR(List<byte> buf, byte rd, byte rs)
    {
        buf.Add(RexW(rs, rd));
        buf.Add(0x21);
        buf.Add(ModRM(0b11, rs, rd));
    }

    // and rd, imm32
    public static void AndRI(List<byte> buf, byte rd, int imm)
    {
        if (imm >= -128 && imm <= 127)
        {
            buf.Add(RexW(0, rd));
            buf.Add(0x83);
            buf.Add(ModRM(0b11, 4, rd));
            buf.Add((byte)(sbyte)imm);
        }
        else
        {
            buf.Add(RexW(0, rd));
            buf.Add(0x81);
            buf.Add(ModRM(0b11, 4, rd));
            WriteI32(buf, imm);
        }
    }

    // shl rd, imm8
    public static void ShlRI(List<byte> buf, byte rd, byte imm)
    {
        buf.Add(RexW(0, rd));
        buf.Add(0xC1);
        buf.Add(ModRM(0b11, 4, rd));
        buf.Add(imm);
    }

    // shr rd, imm8 (logical shift right)
    public static void ShrRI(List<byte> buf, byte rd, byte imm)
    {
        buf.Add(RexW(0, rd));
        buf.Add(0xC1);
        buf.Add(ModRM(0b11, 5, rd));
        buf.Add(imm);
    }

    // sar rd, imm8 (arithmetic shift right)
    public static void SarRI(List<byte> buf, byte rd, byte imm)
    {
        buf.Add(RexW(0, rd));
        buf.Add(0xC1);
        buf.Add(ModRM(0b11, 7, rd));
        buf.Add(imm);
    }

    // ═════════════════════════════════════════════════════════════
    // Compare and test
    // ═════════════════════════════════════════════════════════════

    // cmp rd, rs
    public static void CmpRR(List<byte> buf, byte rd, byte rs)
    {
        buf.Add(RexW(rs, rd));
        buf.Add(0x39);
        buf.Add(ModRM(0b11, rs, rd));
    }

    // cmp rd, imm32
    public static void CmpRI(List<byte> buf, byte rd, int imm)
    {
        if (imm >= -128 && imm <= 127)
        {
            buf.Add(RexW(0, rd));
            buf.Add(0x83);
            buf.Add(ModRM(0b11, 7, rd));
            buf.Add((byte)(sbyte)imm);
        }
        else
        {
            buf.Add(RexW(0, rd));
            buf.Add(0x81);
            buf.Add(ModRM(0b11, 7, rd));
            WriteI32(buf, imm);
        }
    }

    // test rd, rs
    public static void TestRR(List<byte> buf, byte rd, byte rs)
    {
        buf.Add(RexW(rs, rd));
        buf.Add(0x85);
        buf.Add(ModRM(0b11, rs, rd));
    }

    // ═════════════════════════════════════════════════════════════
    // Setcc (set byte on condition)
    // ═════════════════════════════════════════════════════════════

    // setcc rd (set low byte of rd based on condition code)
    public static void Setcc(List<byte> buf, byte cc, byte rd)
    {
        if (rd >= 8 || rd >= 4) // need REX for uniform byte access
            buf.Add(Rex(false, false, false, rd >= 8));
        buf.Add(0x0F);
        buf.Add((byte)(0x90 + cc));
        buf.Add(ModRM(0b11, 0, rd));
    }

    // Condition codes for Jcc and Setcc
    public const byte CC_B  = 0x2;  // below (CF=1), unsigned
    public const byte CC_AE = 0x3;  // above or equal (CF=0), unsigned
    public const byte CC_E  = 0x4;  // equal (ZF=1)
    public const byte CC_NE = 0x5;  // not equal (ZF=0)
    public const byte CC_L  = 0xC;  // less (SF≠OF)
    public const byte CC_GE = 0xD;  // greater or equal (SF=OF)
    public const byte CC_LE = 0xE;  // less or equal (ZF=1 or SF≠OF), signed
    public const byte CC_G  = 0xF;  // greater (ZF=0 and SF=OF), signed
    public const byte CC_BE = 0x6;  // below or equal (CF=1 or ZF=1), unsigned
    public const byte CC_A  = 0x7;  // above (CF=0 and ZF=0), unsigned

    // ═════════════════════════════════════════════════════════════
    // movzx for zero-extending setcc result
    // ═════════════════════════════════════════════════════════════

    // movzx rd, rd_low8 (zero-extend byte to 64-bit)
    public static void MovzxByteSelf(List<byte> buf, byte rd)
    {
        buf.Add(RexW(rd, rd));
        buf.Add(0x0F);
        buf.Add(0xB6);
        buf.Add(ModRM(0b11, rd, rd));
    }

    // ═════════════════════════════════════════════════════════════
    // Branches and jumps
    // ═════════════════════════════════════════════════════════════

    // jcc rel32 (conditional jump, 6-byte encoding)
    public static void Jcc(List<byte> buf, byte cc, int rel32)
    {
        buf.Add(0x0F);
        buf.Add((byte)(0x80 + cc));
        WriteI32(buf, rel32);
    }

    // jmp rel32 (5-byte encoding)
    public static void Jmp(List<byte> buf, int rel32)
    {
        buf.Add(0xE9);
        WriteI32(buf, rel32);
    }

    // call rel32
    public static void Call(List<byte> buf, int rel32)
    {
        buf.Add(0xE8);
        WriteI32(buf, rel32);
    }

    // ret
    public static void Ret(List<byte> buf)
    {
        buf.Add(0xC3);
    }

    // nop (1-byte)
    public static void Nop(List<byte> buf)
    {
        buf.Add(0x90);
    }

    // ═════════════════════════════════════════════════════════════
    // Push / Pop
    // ═════════════════════════════════════════════════════════════

    // push r64
    public static void PushR(List<byte> buf, byte rd)
    {
        if (rd >= 8)
            buf.Add(Rex(false, false, false, true));
        buf.Add((byte)(0x50 + (rd & 7)));
    }

    // pop r64
    public static void PopR(List<byte> buf, byte rd)
    {
        if (rd >= 8)
            buf.Add(Rex(false, false, false, true));
        buf.Add((byte)(0x58 + (rd & 7)));
    }

    // ═════════════════════════════════════════════════════════════
    // LEA
    // ═════════════════════════════════════════════════════════════

    // lea rd, [rs + offset]
    public static void Lea(List<byte> buf, byte rd, byte rs, int offset)
    {
        buf.Add(RexW(rd, rs));
        buf.Add(0x8D);
        EmitMemOperand(buf, rd, rs, offset);
    }

    // ═════════════════════════════════════════════════════════════
    // Syscall
    // ═════════════════════════════════════════════════════════════

    // syscall (2 bytes)
    public static void Syscall(List<byte> buf)
    {
        buf.Add(0x0F);
        buf.Add(0x05);
    }

    // ═════════════════════════════════════════════════════════════
    // Load immediate (helper that picks best encoding)
    // ═════════════════════════════════════════════════════════════

    // Load a 64-bit immediate into rd. Uses shortest encoding available.
    public static void Li(List<byte> buf, byte rd, long value)
    {
        if (value == 0)
        {
            // xor rd, rd (shortest way to zero a register)
            XorRR(buf, rd, rd);
        }
        else if (value >= int.MinValue && value <= int.MaxValue)
        {
            MovRI32(buf, rd, (int)value);
        }
        else
        {
            MovRI64(buf, rd, value);
        }
    }

    // xor rd, rs (32-bit — used for zeroing, implicitly zero-extends to 64-bit)
    public static void XorRR(List<byte> buf, byte rd, byte rs)
    {
        // No REX.W — 32-bit XOR implicitly zero-extends
        byte rex = Rex(false, rs >= 8, false, rd >= 8);
        if (rex != 0x40) buf.Add(rex);
        buf.Add(0x31);
        buf.Add(ModRM(0b11, rs, rd));
    }

    // or rd, rs (64-bit)
    public static void OrRR(List<byte> buf, byte rd, byte rs)
    {
        buf.Add(RexW(rs, rd));
        buf.Add(0x09);
        buf.Add(ModRM(0b11, rs, rd));
    }

    // xor rd, rs (64-bit)
    public static void Xor64RR(List<byte> buf, byte rd, byte rs)
    {
        buf.Add(RexW(rs, rd));
        buf.Add(0x31);
        buf.Add(ModRM(0b11, rs, rd));
    }

    // not rd (64-bit bitwise complement)
    public static void NotR(List<byte> buf, byte rd)
    {
        buf.Add(RexW(0, rd));
        buf.Add(0xF7);
        buf.Add(ModRM(0b11, 2, rd));
    }

    // shl rd, cl (64-bit variable shift left)
    public static void ShlCL(List<byte> buf, byte rd)
    {
        buf.Add(RexW(0, rd));
        buf.Add(0xD3);
        buf.Add(ModRM(0b11, 4, rd));
    }

    // sar rd, cl (64-bit variable arithmetic shift right)
    public static void SarCL(List<byte> buf, byte rd)
    {
        buf.Add(RexW(0, rd));
        buf.Add(0xD3);
        buf.Add(ModRM(0b11, 7, rd));
    }

    // ═════════════════════════════════════════════════════════════
    // Internal helpers
    // ═════════════════════════════════════════════════════════════

    static void EmitMemOperand(List<byte> buf, byte reg, byte rm, int offset)
    {
        byte rmLow = (byte)(rm & 7);

        // RSP/R12 as base requires SIB byte
        bool needsSib = rmLow == 4;
        // RBP/R13 as base with zero offset requires disp8(0)
        bool rbpBase = rmLow == 5;

        if (offset == 0 && !rbpBase)
        {
            buf.Add(ModRM(0b00, reg, rm));
            if (needsSib)
                buf.Add(Sib(0, 4, rmLow)); // SIB: no index, base=RSP/R12
        }
        else if (offset >= -128 && offset <= 127)
        {
            buf.Add(ModRM(0b01, reg, rm));
            if (needsSib)
                buf.Add(Sib(0, 4, rmLow));
            buf.Add((byte)(sbyte)offset);
        }
        else
        {
            buf.Add(ModRM(0b10, reg, rm));
            if (needsSib)
                buf.Add(Sib(0, 4, rmLow));
            WriteI32(buf, offset);
        }
    }

    static void WriteI32(List<byte> buf, int value)
    {
        buf.Add((byte)(value & 0xFF));
        buf.Add((byte)((value >> 8) & 0xFF));
        buf.Add((byte)((value >> 16) & 0xFF));
        buf.Add((byte)((value >> 24) & 0xFF));
    }

    // OUT DX, AL — write byte in AL to I/O port in DX
    public static void OutDxAl(List<byte> buf)
    {
        buf.Add(0xEE);
    }

    // IN AL, DX — read byte from I/O port in DX into AL
    public static void InAlDx(List<byte> buf)
    {
        buf.Add(0xEC);
    }

    // HLT — halt the processor
    public static void Hlt(List<byte> buf)
    {
        buf.Add(0xF4);
    }

    /// <summary>PAUSE — spin-wait hint (F3 90). Saves power on real HW, no-op on QEMU.</summary>
    public static void Pause(List<byte> buf)
    {
        buf.Add(0xF3);
        buf.Add(0x90);
    }

    // CLI — disable interrupts
    public static void Cli(List<byte> buf)
    {
        buf.Add(0xFA);
    }

    // STI — enable interrupts
    public static void Sti(List<byte> buf)
    {
        buf.Add(0xFB);
    }

    // IRETQ — return from 64-bit interrupt
    public static void Iretq(List<byte> buf)
    {
        buf.Add(0x48); // REX.W prefix
        buf.Add(0xCF);
    }

    // LIDT [addr] — load IDT register from memory at addr in RAX
    // We emit: lea rdi, [rax]; lidt [rdi]
    // Actually, lidt m16&64 = 0F 01 /3 with mod/rm
    // lidt [rdi] = 0F 01 1F (ModRM: mod=00, reg=011 (/3), rm=111 (RDI))
    public static void LidtRdi(List<byte> buf)
    {
        buf.Add(0x0F);
        buf.Add(0x01);
        buf.Add(0x1F); // ModRM: [RDI]
    }

    // SWAPGS — swap GS base (needed for interrupt handling in long mode)
    public static void Swapgs(List<byte> buf)
    {
        buf.Add(0x0F);
        buf.Add(0x01);
        buf.Add(0xF8);
    }

    static void WriteI64(List<byte> buf, long value)
    {
        for (int i = 0; i < 8; i++)
            buf.Add((byte)((value >> (i * 8)) & 0xFF));
    }

    // ── SSE2 Instructions ───────────────────────────────────────

    public static void MovqToXmm(List<byte> buf, byte xmm, byte gpr)
    {
        buf.Add(0x66);
        buf.Add(RexW(xmm, gpr));
        buf.Add(0x0F); buf.Add(0x6E);
        buf.Add(ModRM(3, xmm, gpr));
    }

    public static void MovqFromXmm(List<byte> buf, byte gpr, byte xmm)
    {
        buf.Add(0x66);
        buf.Add(RexW(xmm, gpr));
        buf.Add(0x0F); buf.Add(0x7E);
        buf.Add(ModRM(3, xmm, gpr));
    }

    public static void Addsd(List<byte> buf, byte dst, byte src)
    {
        buf.Add(0xF2); buf.Add(0x0F); buf.Add(0x58);
        buf.Add(ModRM(3, dst, src));
    }

    public static void Subsd(List<byte> buf, byte dst, byte src)
    {
        buf.Add(0xF2); buf.Add(0x0F); buf.Add(0x5C);
        buf.Add(ModRM(3, dst, src));
    }

    public static void Mulsd(List<byte> buf, byte dst, byte src)
    {
        buf.Add(0xF2); buf.Add(0x0F); buf.Add(0x59);
        buf.Add(ModRM(3, dst, src));
    }

    public static void Divsd(List<byte> buf, byte dst, byte src)
    {
        buf.Add(0xF2); buf.Add(0x0F); buf.Add(0x5E);
        buf.Add(ModRM(3, dst, src));
    }

    public static void Ucomisd(List<byte> buf, byte a, byte b)
    {
        buf.Add(0x66); buf.Add(0x0F); buf.Add(0x2E);
        buf.Add(ModRM(3, a, b));
    }
}
