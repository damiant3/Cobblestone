using Xunit;
using Xunit.Abstractions;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Codex.Bootstrap.Tests;

public class Phase3M1BootTest
{
    private readonly ITestOutputHelper m_output;

    public Phase3M1BootTest(ITestOutputHelper output) => m_output = output;

    [Fact]
    public void MainEquals42_BootsInQemu_Prints42()
    {
        if (!HasWslQemu())
        {
            m_output.WriteLine("SKIP: WSL or qemu-system-x86_64 not available");
            return;
        }

        var mainDef = new IRDef(
            "\u001a\u000f\u0011\u0012",
            new List<IRParam>(),
            new IntegerTy(),
            new IrIntLit(42));
        var module = new IRModule(
            new Name("\u000e\u000d\u0013\u000e"),
            new List<IRDef> { mainDef });

        List<long> elfBytes = Codex_Codex_Codex.x86_64_emit_module(module);
        byte[] elf = elfBytes.Select(b => (byte)b).ToArray();

        m_output.WriteLine($"ELF size: {elf.Length} bytes");

        string tempDir = Path.Combine(Path.GetTempPath(), $"codex_m31_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string elfPath = Path.Combine(tempDir, "test.elf");
            File.WriteAllBytes(elfPath, elf);

            string wslPath = WindowsToWslPath(elfPath);
            m_output.WriteLine($"WSL path: {wslPath}");

            string cmd = $"timeout 10 /usr/bin/qemu-system-x86_64 -kernel {wslPath} -nographic -no-reboot 2>/dev/null";
            string? output = RunWsl(cmd);
            m_output.WriteLine($"QEMU output: [{output}]");

            Assert.NotNull(output);
            Assert.Contains("42", output);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void MainEquals42_ProducesValidElf()
    {
        var mainDef = new IRDef(
            "\u001a\u000f\u0011\u0012",
            new List<IRParam>(),
            new IntegerTy(),
            new IrIntLit(42));
        var module = new IRModule(
            new Name("\u000e\u000d\u0013\u000e"),
            new List<IRDef> { mainDef });

        List<long> elfBytes = Codex_Codex_Codex.x86_64_emit_module(module);
        byte[] elf = elfBytes.Select(b => (byte)b).ToArray();

        File.WriteAllBytes(Path.Combine(Path.GetTempPath(), "codex_m31_debug.elf"), elf);

        Assert.True(elf.Length >= 52, $"ELF too small: {elf.Length} bytes");
        Assert.Equal(0x7F, elf[0]);
        Assert.Equal((byte)'E', elf[1]);
        Assert.Equal((byte)'L', elf[2]);
        Assert.Equal((byte)'F', elf[3]);

        // Multiboot header is in the text section, which starts at a
        // computed offset within the ELF file. Verify PVH note is present.
        // The PVH note name is "Xen\0" at a fixed position in the ELF.
        bool foundXen = false;
        for (int i = 0; i < elf.Length - 3; i++)
        {
            if (elf[i] == (byte)'X' && elf[i + 1] == (byte)'e' && elf[i + 2] == (byte)'n' && elf[i + 3] == 0)
            {
                foundXen = true;
                break;
            }
        }
        Assert.True(foundXen, "PVH note with 'Xen' name not found in ELF");
    }

    static string WindowsToWslPath(string windowsPath)
    {
        string normalized = windowsPath.Replace('\\', '/');
        if (normalized.Length >= 2 && normalized[1] == ':')
        {
            char drive = char.ToLower(normalized[0]);
            return $"/mnt/{drive}{normalized[2..]}";
        }
        return normalized;
    }

    static string? RunWsl(string command)
    {
        try
        {
            using Process? proc = Process.Start(new ProcessStartInfo("wsl", $"bash -c \"{command}\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (proc is null) return null;
            string stdout = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(10_000);
            return stdout;
        }
        catch { return null; }
    }

    [Fact]
    public void M32_LetAndArithmetic_Prints30()
    {
        if (!HasWslQemu()) return;

        // main = let x = 10 in let y = 20 in x + y
        var body = new IrLet("x", new IntegerTy(), new IrIntLit(10),
            new IrLet("y", new IntegerTy(), new IrIntLit(20),
                new IrBinary(new IrAddInt(), new IrName("x", new IntegerTy()), new IrName("y", new IntegerTy()), new IntegerTy())));

        var module = new IRModule(
            new Name("\u000e\u000d\u0013\u000e"),
            new List<IRDef> { new IRDef("\u001a\u000f\u0011\u0012", new List<IRParam>(), new IntegerTy(), body) });

        List<long> elfBytes = Codex_Codex_Codex.x86_64_emit_module(module);
        byte[] elf = elfBytes.Select(b => (byte)b).ToArray();
        m_output.WriteLine($"ELF size: {elf.Length} bytes");

        string tempDir = Path.Combine(Path.GetTempPath(), $"codex_m32_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string elfPath = Path.Combine(tempDir, "test.elf");
            File.WriteAllBytes(elfPath, elf);
            string wslPath = WindowsToWslPath(elfPath);
            string? output = RunWsl($"timeout 10 /usr/bin/qemu-system-x86_64 -kernel {wslPath} -nographic -no-reboot 2>/dev/null");
            m_output.WriteLine($"QEMU output: [{output}]");
            Assert.NotNull(output);
            Assert.Contains("30", output);
        }
        finally { try { Directory.Delete(tempDir, true); } catch { } }
    }

    [Fact]
    public void M33_IfElseComparison_Prints1()
    {
        if (!HasWslQemu()) return;

        // main = if 5 > 3 then 99 else 0
        var body = new IrIf(
            new IrBinary(new IrGt(), new IrIntLit(5), new IrIntLit(3), new BooleanTy()),
            new IrIntLit(99),
            new IrIntLit(0),
            new IntegerTy());

        var module = new IRModule(
            new Name("\u000e\u000d\u0013\u000e"),
            new List<IRDef> { new IRDef("\u001a\u000f\u0011\u0012", new List<IRParam>(), new IntegerTy(), body) });

        List<long> elfBytes = Codex_Codex_Codex.x86_64_emit_module(module);
        byte[] elf = elfBytes.Select(b => (byte)b).ToArray();
        m_output.WriteLine($"ELF size: {elf.Length} bytes");

        string tempDir = Path.Combine(Path.GetTempPath(), $"codex_m33_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string elfPath = Path.Combine(tempDir, "test.elf");
            File.WriteAllBytes(elfPath, elf);
            string wslPath = WindowsToWslPath(elfPath);
            string? output = RunWsl($"timeout 10 /usr/bin/qemu-system-x86_64 -kernel {wslPath} -nographic -no-reboot 2>/dev/null");
            m_output.WriteLine($"QEMU output: [{output}]");
            Assert.NotNull(output);
            Assert.Contains("99", output);
        }
        finally { try { Directory.Delete(tempDir, true); } catch { } }
    }

    [Fact]
    public void M34_Factorial5_Prints120()
    {
        if (!HasWslQemu()) return;

        // factorial n = if n == 0 then 1 else n * factorial (n - 1)
        var factBody = new IrIf(
            new IrBinary(new IrEq(),
                new IrName("n", new IntegerTy()),
                new IrIntLit(0),
                new BooleanTy()),
            new IrIntLit(1),
            new IrBinary(new IrMulInt(),
                new IrName("n", new IntegerTy()),
                new IrApply(
                    new IrName("factorial", new FunTy(new IntegerTy(), new IntegerTy())),
                    new IrBinary(new IrSubInt(),
                        new IrName("n", new IntegerTy()),
                        new IrIntLit(1),
                        new IntegerTy()),
                    new IntegerTy()),
                new IntegerTy()),
            new IntegerTy());

        var factDef = new IRDef(
            "factorial",
            new List<IRParam> { new IRParam("n", new IntegerTy()) },
            new IntegerTy(),
            factBody);

        // main = factorial 5
        var mainBody = new IrApply(
            new IrName("factorial", new FunTy(new IntegerTy(), new IntegerTy())),
            new IrIntLit(5),
            new IntegerTy());

        var mainDef = new IRDef(
            "\u001a\u000f\u0011\u0012",
            new List<IRParam>(),
            new IntegerTy(),
            mainBody);

        var module = new IRModule(
            new Name("\u000e\u000d\u0013\u000e"),
            new List<IRDef> { factDef, mainDef });

        List<long> elfBytes = Codex_Codex_Codex.x86_64_emit_module(module);
        byte[] elf = elfBytes.Select(b => (byte)b).ToArray();
        m_output.WriteLine($"ELF size: {elf.Length} bytes");

        string tempDir = Path.Combine(Path.GetTempPath(), $"codex_m34_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string elfPath = Path.Combine(tempDir, "test.elf");
            File.WriteAllBytes(elfPath, elf);
            string wslPath = WindowsToWslPath(elfPath);
            string? output = RunWsl($"timeout 10 /usr/bin/qemu-system-x86_64 -kernel {wslPath} -nographic -no-reboot 2>/dev/null");
            m_output.WriteLine($"QEMU output: [{output}]");
            Assert.NotNull(output);
            Assert.Contains("120", output);
        }
        finally { try { Directory.Delete(tempDir, true); } catch { } }
    }

    [Fact]
    public void M35_RecordFieldAccess_Prints7()
    {
        if (!HasWslQemu()) return;

        // main = let p = Point { x = 3, y = 4 } in p.x + p.y
        var pointType = new RecordTy(
            new Name("Point"),
            new List<RecordField> {
                new RecordField(new Name("x"), new IntegerTy()),
                new RecordField(new Name("y"), new IntegerTy())
            });

        var body = new IrLet("p", pointType,
            new IrRecord("Point",
                new List<IRFieldVal> {
                    new IRFieldVal("x", new IrIntLit(3)),
                    new IRFieldVal("y", new IrIntLit(4))
                }, pointType),
            new IrBinary(new IrAddInt(),
                new IrFieldAccess(new IrName("p", pointType), "x", new IntegerTy()),
                new IrFieldAccess(new IrName("p", pointType), "y", new IntegerTy()),
                new IntegerTy()));

        var module = new IRModule(
            new Name("\u000e\u000d\u0013\u000e"),
            new List<IRDef> { new IRDef("\u001a\u000f\u0011\u0012", new List<IRParam>(), new IntegerTy(), body) });

        List<long> elfBytes = Codex_Codex_Codex.x86_64_emit_module(module);
        byte[] elf = elfBytes.Select(b => (byte)b).ToArray();
        m_output.WriteLine($"ELF size: {elf.Length} bytes");

        string tempDir = Path.Combine(Path.GetTempPath(), $"codex_m35_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string elfPath = Path.Combine(tempDir, "test.elf");
            File.WriteAllBytes(elfPath, elf);
            string wslPath = WindowsToWslPath(elfPath);
            string? output = RunWsl($"timeout 10 /usr/bin/qemu-system-x86_64 -kernel {wslPath} -nographic -no-reboot 2>/dev/null");
            m_output.WriteLine($"QEMU output: [{output}]");
            Assert.NotNull(output);
            Assert.Contains("7", output);
        }
        finally { try { Directory.Delete(tempDir, true); } catch { } }
    }

    [Fact]
    public void M36_MatchSumType_Prints42()
    {
        if (!HasWslQemu()) return;

        // Maybe = Some Integer | None
        // unwrap m = match m with | Some x -> x | None -> 0
        // main = unwrap (Some 42)
        var maybeTy = new SumTy(
            new Name("Maybe"),
            new List<SumCtor> {
                new SumCtor(new Name("Some"), new List<CodexType> { new IntegerTy() }),
                new SumCtor(new Name("None"), new List<CodexType>())
            });

        var unwrapBody = new IrMatch(
            new IrName("m", maybeTy),
            new List<IRBranch> {
                new IRBranch(
                    new IrCtorPat("Some", new List<IRPat> { new IrVarPat("x", new IntegerTy()) }, maybeTy),
                    new IrName("x", new IntegerTy())),
                new IRBranch(
                    new IrWildPat(),
                    new IrIntLit(0))
            }, new IntegerTy());

        var unwrapDef = new IRDef(
            "unwrap",
            new List<IRParam> { new IRParam("m", maybeTy) },
            new IntegerTy(),
            unwrapBody);

        var mainBody = new IrApply(
            new IrName("unwrap", new FunTy(maybeTy, new IntegerTy())),
            new IrApply(
                new IrName("Some", new FunTy(new IntegerTy(), maybeTy)),
                new IrIntLit(42),
                maybeTy),
            new IntegerTy());

        var mainDef = new IRDef(
            "\u001a\u000f\u0011\u0012",
            new List<IRParam>(),
            new IntegerTy(),
            mainBody);

        var module = new IRModule(
            new Name("\u000e\u000d\u0013\u000e"),
            new List<IRDef> { unwrapDef, mainDef });

        List<long> elfBytes = Codex_Codex_Codex.x86_64_emit_module(module);
        byte[] elf = elfBytes.Select(b => (byte)b).ToArray();
        m_output.WriteLine($"ELF size: {elf.Length} bytes");

        string tempDir = Path.Combine(Path.GetTempPath(), $"codex_m36_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string elfPath = Path.Combine(tempDir, "test.elf");
            File.WriteAllBytes(elfPath, elf);
            string wslPath = WindowsToWslPath(elfPath);
            string? output = RunWsl($"timeout 10 /usr/bin/qemu-system-x86_64 -kernel {wslPath} -nographic -no-reboot 2>/dev/null");
            m_output.WriteLine($"QEMU output: [{output}]");
            Assert.NotNull(output);
            Assert.Contains("42", output);
        }
        finally { try { Directory.Delete(tempDir, true); } catch { } }
    }

    [Fact]
    public void M37_ListLength_Prints3()
    {
        if (!HasWslQemu()) return;

        // main = let l = [10, 20, 30] in <read count from [l+0]>
        // List layout: [capacity | count | elem0 | ...]. The list pointer
        // points at the count word. IrFieldAccess on a non-RecordTy defaults
        // to field index 0, so loading [l+0] reads the count = 3.
        var listTy = new ListTy(new IntegerTy());

        var body = new IrLet("l", listTy,
            new IrList(new List<IRExpr> { new IrIntLit(10), new IrIntLit(20), new IrIntLit(30) }, new IntegerTy()),
            new IrFieldAccess(new IrName("l", listTy), "count", new IntegerTy()));

        var module = new IRModule(
            new Name("\u000e\u000d\u0013\u000e"),
            new List<IRDef> { new IRDef("\u001a\u000f\u0011\u0012", new List<IRParam>(), new IntegerTy(), body) });

        List<long> elfBytes = Codex_Codex_Codex.x86_64_emit_module(module);
        byte[] elf = elfBytes.Select(b => (byte)b).ToArray();
        m_output.WriteLine($"ELF size: {elf.Length} bytes");

        string tempDir = Path.Combine(Path.GetTempPath(), $"codex_m37_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string elfPath = Path.Combine(tempDir, "test.elf");
            File.WriteAllBytes(elfPath, elf);
            string wslPath = WindowsToWslPath(elfPath);
            string? output = RunWsl($"timeout 10 /usr/bin/qemu-system-x86_64 -kernel {wslPath} -nographic -no-reboot 2>/dev/null");
            m_output.WriteLine($"QEMU output: [{output}]");
            Assert.NotNull(output);
            Assert.Contains("3", output);
        }
        finally { try { Directory.Delete(tempDir, true); } catch { } }
    }

    static bool HasWslQemu()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return false;
        try
        {
            string? result = RunWsl("which qemu-system-x86_64 2>/dev/null && echo FOUND");
            return result?.Contains("FOUND") == true;
        }
        catch { return false; }
    }
}
