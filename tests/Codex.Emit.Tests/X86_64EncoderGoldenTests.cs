using Codex.Emit.X86_64;
using Xunit;

namespace Codex.Emit.Tests;

/// <summary>
/// Golden reference tests for X86_64Encoder. Each test encodes an instruction
/// using the C# encoder and asserts the exact byte sequence. These known-good
/// values are the acceptance criterion for the Codex port
/// (Codex.Codex/Emit/X86_64Encoder.codex).
/// </summary>
public class X86_64EncoderGoldenTests
{
    static byte[] Encode(Action<List<byte>> emit)
    {
        var buf = new List<byte>();
        emit(buf);
        return buf.ToArray();
    }

    // ═══════════════════════════════════════════════════════════
    // MOV instructions
    // ═══════════════════════════════════════════════════════════

    [Theory]
    [InlineData(Reg.RAX, Reg.RCX, new byte[] { 0x48, 0x89, 0xC8 })]     // mov rax, rcx
    [InlineData(Reg.R8,  Reg.R15, new byte[] { 0x4D, 0x89, 0xF8 })]     // mov r8, r15 (both extended)
    [InlineData(Reg.RAX, Reg.R8,  new byte[] { 0x4C, 0x89, 0xC0 })]     // mov rax, r8 (rs extended)
    [InlineData(Reg.R8,  Reg.RAX, new byte[] { 0x49, 0x89, 0xC0 })]     // mov r8, rax (rd extended)
    public void MovRR(byte rd, byte rs, byte[] expected) =>
        Assert.Equal(expected, Encode(b => X86_64Encoder.MovRR(b, rd, rs)));

    [Theory]
    [InlineData(Reg.RAX, 0L, new byte[] { 0x48, 0xB8, 0, 0, 0, 0, 0, 0, 0, 0 })]
    [InlineData(Reg.RAX, -1L, new byte[] { 0x48, 0xB8, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF })]
    [InlineData(Reg.R12, 0x0102030405060708L, new byte[] { 0x49, 0xBC, 0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01 })]
    public void MovRI64(byte rd, long imm, byte[] expected) =>
        Assert.Equal(expected, Encode(b => X86_64Encoder.MovRI64(b, rd, imm)));

    [Theory]
    [InlineData(Reg.RAX, 42, new byte[] { 0x48, 0xC7, 0xC0, 42, 0, 0, 0 })]
    [InlineData(Reg.R12, -128, new byte[] { 0x49, 0xC7, 0xC4, 0x80, 0xFF, 0xFF, 0xFF })]
    public void MovRI32(byte rd, int imm, byte[] expected) =>
        Assert.Equal(expected, Encode(b => X86_64Encoder.MovRI32(b, rd, imm)));

    [Theory]
    [InlineData(Reg.RAX, Reg.RBX, 0, new byte[] { 0x48, 0x8B, 0x03 })]           // [rbx]
    [InlineData(Reg.RAX, Reg.RBP, 0, new byte[] { 0x48, 0x8B, 0x45, 0x00 })]     // [rbp+0] forced disp8
    [InlineData(Reg.RAX, Reg.RSP, 0, new byte[] { 0x48, 0x8B, 0x04, 0x24 })]     // [rsp] needs SIB
    [InlineData(Reg.RAX, Reg.RBX, 8, new byte[] { 0x48, 0x8B, 0x43, 0x08 })]     // [rbx+8] disp8
    [InlineData(Reg.RAX, Reg.RSP, 8, new byte[] { 0x48, 0x8B, 0x44, 0x24, 0x08 })]  // [rsp+8] SIB+disp8
    [InlineData(Reg.RAX, Reg.RBX, 256, new byte[] { 0x48, 0x8B, 0x83, 0x00, 0x01, 0x00, 0x00 })]  // [rbx+256] disp32
    [InlineData(Reg.RAX, Reg.RSP, 256, new byte[] { 0x48, 0x8B, 0x84, 0x24, 0x00, 0x01, 0x00, 0x00 })]  // [rsp+256] SIB+disp32
    [InlineData(Reg.RAX, Reg.RBX, -8, new byte[] { 0x48, 0x8B, 0x43, 0xF8 })]    // [rbx-8] negative disp8
    [InlineData(Reg.R12, Reg.R13, 0, new byte[] { 0x4D, 0x8B, 0x65, 0x00 })]     // [r13+0] forced disp8 (r13=rbp analog)
    public void MovLoad(byte rd, byte rs, int offset, byte[] expected) =>
        Assert.Equal(expected, Encode(b => X86_64Encoder.MovLoad(b, rd, rs, offset)));

    [Theory]
    [InlineData(Reg.RBX, Reg.RAX, 0, new byte[] { 0x48, 0x89, 0x03 })]
    [InlineData(Reg.RBP, Reg.RAX, 16, new byte[] { 0x48, 0x89, 0x45, 0x10 })]
    public void MovStore(byte rd, byte rs, int offset, byte[] expected) =>
        Assert.Equal(expected, Encode(b => X86_64Encoder.MovStore(b, rd, rs, offset)));

    [Theory]
    [InlineData(Reg.RAX, 0, new byte[] { 0x48, 0x8B, 0x05, 0x00, 0x00, 0x00, 0x00 })]
    [InlineData(Reg.R8,  100, new byte[] { 0x4C, 0x8B, 0x05, 0x64, 0x00, 0x00, 0x00 })]
    public void MovLoadRipRel(byte rd, int disp, byte[] expected) =>
        Assert.Equal(expected, Encode(b => X86_64Encoder.MovLoadRipRel(b, rd, disp)));

    [Theory]
    [InlineData(Reg.RAX, 0, new byte[] { 0x48, 0x89, 0x05, 0x00, 0x00, 0x00, 0x00 })]
    public void MovStoreRipRel(byte rs, int disp, byte[] expected) =>
        Assert.Equal(expected, Encode(b => X86_64Encoder.MovStoreRipRel(b, rs, disp)));

    [Theory]
    [InlineData(Reg.RAX, Reg.RBX, 0, new byte[] { 0x48, 0x0F, 0xB6, 0x03 })]
    public void MovzxByte(byte rd, byte rs, int offset, byte[] expected) =>
        Assert.Equal(expected, Encode(b => X86_64Encoder.MovzxByte(b, rd, rs, offset)));

    [Theory]
    [InlineData(Reg.RBX, Reg.RAX, 0, new byte[] { 0x88, 0x03 })]                 // rs=0 < 4, no REX
    [InlineData(Reg.RBX, Reg.RSP, 0, new byte[] { 0x40, 0x88, 0x23 })]           // rs=4, needs REX
    [InlineData(Reg.R8,  Reg.RAX, 0, new byte[] { 0x41, 0x88, 0x00 })]           // rd extended
    [InlineData(Reg.RBX, Reg.R8,  0, new byte[] { 0x44, 0x88, 0x03 })]           // rs extended
    public void MovStoreByte(byte rd, byte rs, int offset, byte[] expected) =>
        Assert.Equal(expected, Encode(b => X86_64Encoder.MovStoreByte(b, rd, rs, offset)));

    // ═══════════════════════════════════════════════════════════
    // Arithmetic
    // ═══════════════════════════════════════════════════════════

    [Theory]
    [InlineData(Reg.RAX, Reg.RCX, new byte[] { 0x48, 0x01, 0xC8 })]
    public void AddRR(byte rd, byte rs, byte[] expected) =>
        Assert.Equal(expected, Encode(b => X86_64Encoder.AddRR(b, rd, rs)));

    [Theory]
    [InlineData(Reg.RAX, 1,    new byte[] { 0x48, 0x83, 0xC0, 0x01 })]          // imm8
    [InlineData(Reg.RAX, 127,  new byte[] { 0x48, 0x83, 0xC0, 0x7F })]          // imm8 boundary
    [InlineData(Reg.RAX, 128,  new byte[] { 0x48, 0x81, 0xC0, 0x80, 0, 0, 0 })] // imm32
    [InlineData(Reg.RAX, -128, new byte[] { 0x48, 0x83, 0xC0, 0x80 })]          // imm8 negative boundary
    [InlineData(Reg.RAX, -129, new byte[] { 0x48, 0x81, 0xC0, 0x7F, 0xFF, 0xFF, 0xFF })] // imm32 negative
    [InlineData(Reg.R12, 8,    new byte[] { 0x49, 0x83, 0xC4, 0x08 })]          // extended reg
    public void AddRI(byte rd, int imm, byte[] expected) =>
        Assert.Equal(expected, Encode(b => X86_64Encoder.AddRI(b, rd, imm)));

    [Theory]
    [InlineData(Reg.RAX, Reg.RCX, new byte[] { 0x48, 0x29, 0xC8 })]
    public void SubRR(byte rd, byte rs, byte[] expected) =>
        Assert.Equal(expected, Encode(b => X86_64Encoder.SubRR(b, rd, rs)));

    [Theory]
    [InlineData(Reg.RSP, 8, new byte[] { 0x48, 0x83, 0xEC, 0x08 })]            // sub rsp, 8 (imm8)
    [InlineData(Reg.RSP, 256, new byte[] { 0x48, 0x81, 0xEC, 0x00, 0x01, 0x00, 0x00 })] // imm32
    public void SubRI(byte rd, int imm, byte[] expected) =>
        Assert.Equal(expected, Encode(b => X86_64Encoder.SubRI(b, rd, imm)));

    [Theory]
    [InlineData(Reg.RAX, Reg.RCX, new byte[] { 0x48, 0x0F, 0xAF, 0xC1 })]
    public void ImulRR(byte rd, byte rs, byte[] expected) =>
        Assert.Equal(expected, Encode(b => X86_64Encoder.ImulRR(b, rd, rs)));

    [Fact]
    public void NegR() =>
        Assert.Equal(new byte[] { 0x48, 0xF7, 0xD8 }, Encode(b => X86_64Encoder.NegR(b, Reg.RAX)));

    [Fact]
    public void Cqo() =>
        Assert.Equal(new byte[] { 0x48, 0x99 }, Encode(b => X86_64Encoder.Cqo(b)));

    [Fact]
    public void IdivR() =>
        Assert.Equal(new byte[] { 0x48, 0xF7, 0xF9 }, Encode(b => X86_64Encoder.IdivR(b, Reg.RCX)));

    [Theory]
    [InlineData(Reg.RAX, Reg.RCX, new byte[] { 0x48, 0x21, 0xC8 })]
    public void AndRR(byte rd, byte rs, byte[] expected) =>
        Assert.Equal(expected, Encode(b => X86_64Encoder.AndRR(b, rd, rs)));

    [Theory]
    [InlineData(Reg.RAX, 7,    new byte[] { 0x48, 0x83, 0xE0, 0x07 })]          // imm8
    [InlineData(Reg.RAX, 0xFF, new byte[] { 0x48, 0x81, 0xE0, 0xFF, 0x00, 0x00, 0x00 })] // imm32
    public void AndRI(byte rd, int imm, byte[] expected) =>
        Assert.Equal(expected, Encode(b => X86_64Encoder.AndRI(b, rd, imm)));

    [Theory]
    [InlineData(Reg.RAX, 3, new byte[] { 0x48, 0xC1, 0xE0, 0x03 })]
    public void ShlRI(byte rd, byte imm, byte[] expected) =>
        Assert.Equal(expected, Encode(b => X86_64Encoder.ShlRI(b, rd, imm)));

    [Theory]
    [InlineData(Reg.RAX, 3, new byte[] { 0x48, 0xC1, 0xE8, 0x03 })]
    public void ShrRI(byte rd, byte imm, byte[] expected) =>
        Assert.Equal(expected, Encode(b => X86_64Encoder.ShrRI(b, rd, imm)));

    [Theory]
    [InlineData(Reg.RAX, 3, new byte[] { 0x48, 0xC1, 0xF8, 0x03 })]
    public void SarRI(byte rd, byte imm, byte[] expected) =>
        Assert.Equal(expected, Encode(b => X86_64Encoder.SarRI(b, rd, imm)));

    // ═══════════════════════════════════════════════════════════
    // Compare and Test
    // ═══════════════════════════════════════════════════════════

    [Theory]
    [InlineData(Reg.RAX, Reg.RCX, new byte[] { 0x48, 0x39, 0xC8 })]
    [InlineData(Reg.R8,  Reg.R15, new byte[] { 0x4D, 0x39, 0xF8 })]
    public void CmpRR(byte rd, byte rs, byte[] expected) =>
        Assert.Equal(expected, Encode(b => X86_64Encoder.CmpRR(b, rd, rs)));

    [Theory]
    [InlineData(Reg.RAX, 0,    new byte[] { 0x48, 0x83, 0xF8, 0x00 })]
    [InlineData(Reg.RAX, -1,   new byte[] { 0x48, 0x83, 0xF8, 0xFF })]
    [InlineData(Reg.RAX, 1000, new byte[] { 0x48, 0x81, 0xF8, 0xE8, 0x03, 0x00, 0x00 })]
    public void CmpRI(byte rd, int imm, byte[] expected) =>
        Assert.Equal(expected, Encode(b => X86_64Encoder.CmpRI(b, rd, imm)));

    [Theory]
    [InlineData(Reg.RAX, Reg.RAX, new byte[] { 0x48, 0x85, 0xC0 })]
    public void TestRR(byte rd, byte rs, byte[] expected) =>
        Assert.Equal(expected, Encode(b => X86_64Encoder.TestRR(b, rd, rs)));

    // ═══════════════════════════════════════════════════════════
    // Setcc
    // ═══════════════════════════════════════════════════════════

    [Theory]
    [InlineData(X86_64Encoder.CC_E,  Reg.RAX, new byte[] { 0x0F, 0x94, 0xC0 })]        // rd < 4, no REX
    [InlineData(X86_64Encoder.CC_E,  Reg.RSP, new byte[] { 0x40, 0x0F, 0x94, 0xC4 })]  // rd=4, needs REX for SPL
    [InlineData(X86_64Encoder.CC_NE, Reg.R8,  new byte[] { 0x41, 0x0F, 0x95, 0xC0 })]  // rd extended
    [InlineData(X86_64Encoder.CC_L,  Reg.RAX, new byte[] { 0x0F, 0x9C, 0xC0 })]        // different cc
    public void Setcc(byte cc, byte rd, byte[] expected) =>
        Assert.Equal(expected, Encode(b => X86_64Encoder.Setcc(b, cc, rd)));

    [Theory]
    [InlineData(Reg.RAX, new byte[] { 0x48, 0x0F, 0xB6, 0xC0 })]
    [InlineData(Reg.R8,  new byte[] { 0x4D, 0x0F, 0xB6, 0xC0 })]
    public void MovzxByteSelf(byte rd, byte[] expected) =>
        Assert.Equal(expected, Encode(b => X86_64Encoder.MovzxByteSelf(b, rd)));

    // ═══════════════════════════════════════════════════════════
    // Branches and Jumps
    // ═══════════════════════════════════════════════════════════

    [Theory]
    [InlineData(X86_64Encoder.CC_E, 0, new byte[] { 0x0F, 0x84, 0x00, 0x00, 0x00, 0x00 })]
    [InlineData(X86_64Encoder.CC_E, 100, new byte[] { 0x0F, 0x84, 0x64, 0x00, 0x00, 0x00 })]
    [InlineData(X86_64Encoder.CC_NE, -10, new byte[] { 0x0F, 0x85, 0xF6, 0xFF, 0xFF, 0xFF })]
    public void Jcc(byte cc, int rel, byte[] expected) =>
        Assert.Equal(expected, Encode(b => X86_64Encoder.Jcc(b, cc, rel)));

    [Theory]
    [InlineData(0,   new byte[] { 0xE9, 0x00, 0x00, 0x00, 0x00 })]
    [InlineData(-10, new byte[] { 0xE9, 0xF6, 0xFF, 0xFF, 0xFF })]
    public void Jmp(int rel, byte[] expected) =>
        Assert.Equal(expected, Encode(b => X86_64Encoder.Jmp(b, rel)));

    [Theory]
    [InlineData(0,   new byte[] { 0xE8, 0x00, 0x00, 0x00, 0x00 })]
    [InlineData(100, new byte[] { 0xE8, 0x64, 0x00, 0x00, 0x00 })]
    public void Call(int rel, byte[] expected) =>
        Assert.Equal(expected, Encode(b => X86_64Encoder.Call(b, rel)));

    [Fact]
    public void Ret() =>
        Assert.Equal(new byte[] { 0xC3 }, Encode(b => X86_64Encoder.Ret(b)));

    [Fact]
    public void Nop() =>
        Assert.Equal(new byte[] { 0x90 }, Encode(b => X86_64Encoder.Nop(b)));

    // ═══════════════════════════════════════════════════════════
    // Push / Pop
    // ═══════════════════════════════════════════════════════════

    [Theory]
    [InlineData(Reg.RAX, new byte[] { 0x50 })]
    [InlineData(Reg.RBP, new byte[] { 0x55 })]
    [InlineData(Reg.R8,  new byte[] { 0x41, 0x50 })]
    [InlineData(Reg.R15, new byte[] { 0x41, 0x57 })]
    public void PushR(byte rd, byte[] expected) =>
        Assert.Equal(expected, Encode(b => X86_64Encoder.PushR(b, rd)));

    [Theory]
    [InlineData(Reg.RAX, new byte[] { 0x58 })]
    [InlineData(Reg.RBP, new byte[] { 0x5D })]
    [InlineData(Reg.R8,  new byte[] { 0x41, 0x58 })]
    [InlineData(Reg.R15, new byte[] { 0x41, 0x5F })]
    public void PopR(byte rd, byte[] expected) =>
        Assert.Equal(expected, Encode(b => X86_64Encoder.PopR(b, rd)));

    // ═══════════════════════════════════════════════════════════
    // LEA
    // ═══════════════════════════════════════════════════════════

    [Theory]
    [InlineData(Reg.RAX, Reg.RBX, 8, new byte[] { 0x48, 0x8D, 0x43, 0x08 })]
    [InlineData(Reg.RAX, Reg.RSP, 16, new byte[] { 0x48, 0x8D, 0x44, 0x24, 0x10 })] // SIB
    public void Lea(byte rd, byte rs, int offset, byte[] expected) =>
        Assert.Equal(expected, Encode(b => X86_64Encoder.Lea(b, rd, rs, offset)));

    // ═══════════════════════════════════════════════════════════
    // Li (smart immediate loader)
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Li_Zero() =>
        Assert.Equal(
            Encode(b => X86_64Encoder.XorRR(b, Reg.RAX, Reg.RAX)),
            Encode(b => X86_64Encoder.Li(b, Reg.RAX, 0)));

    [Fact]
    public void Li_Small() =>
        Assert.Equal(
            Encode(b => X86_64Encoder.MovRI32(b, Reg.RAX, 42)),
            Encode(b => X86_64Encoder.Li(b, Reg.RAX, 42)));

    [Fact]
    public void Li_Large() =>
        Assert.Equal(
            Encode(b => X86_64Encoder.MovRI64(b, Reg.RAX, 0x1_0000_0000L)),
            Encode(b => X86_64Encoder.Li(b, Reg.RAX, 0x1_0000_0000L)));

    [Fact]
    public void Li_Negative() =>
        Assert.Equal(
            Encode(b => X86_64Encoder.MovRI32(b, Reg.RAX, -1)),
            Encode(b => X86_64Encoder.Li(b, Reg.RAX, -1)));

    // ═══════════════════════════════════════════════════════════
    // XorRR
    // ═══════════════════════════════════════════════════════════

    [Theory]
    [InlineData(Reg.RAX, Reg.RAX, new byte[] { 0x31, 0xC0 })]                    // no REX needed
    [InlineData(Reg.R8,  Reg.R8,  new byte[] { 0x45, 0x31, 0xC0 })]              // both extended
    public void XorRR(byte rd, byte rs, byte[] expected) =>
        Assert.Equal(expected, Encode(b => X86_64Encoder.XorRR(b, rd, rs)));

    // ═══════════════════════════════════════════════════════════
    // Syscall
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Syscall() =>
        Assert.Equal(new byte[] { 0x0F, 0x05 }, Encode(b => X86_64Encoder.Syscall(b)));

    // ═══════════════════════════════════════════════════════════
    // I/O and Privileged
    // ═══════════════════════════════════════════════════════════

    [Fact] public void OutDxAl() => Assert.Equal(new byte[] { 0xEE }, Encode(b => X86_64Encoder.OutDxAl(b)));
    [Fact] public void InAlDx()  => Assert.Equal(new byte[] { 0xEC }, Encode(b => X86_64Encoder.InAlDx(b)));
    [Fact] public void Hlt()     => Assert.Equal(new byte[] { 0xF4 }, Encode(b => X86_64Encoder.Hlt(b)));
    [Fact] public void Pause()   => Assert.Equal(new byte[] { 0xF3, 0x90 }, Encode(b => X86_64Encoder.Pause(b)));
    [Fact] public void Cli()     => Assert.Equal(new byte[] { 0xFA }, Encode(b => X86_64Encoder.Cli(b)));
    [Fact] public void Sti()     => Assert.Equal(new byte[] { 0xFB }, Encode(b => X86_64Encoder.Sti(b)));
    [Fact] public void Iretq()   => Assert.Equal(new byte[] { 0x48, 0xCF }, Encode(b => X86_64Encoder.Iretq(b)));
    [Fact] public void LidtRdi() => Assert.Equal(new byte[] { 0x0F, 0x01, 0x1F }, Encode(b => X86_64Encoder.LidtRdi(b)));
    [Fact] public void Swapgs()  => Assert.Equal(new byte[] { 0x0F, 0x01, 0xF8 }, Encode(b => X86_64Encoder.Swapgs(b)));
}
