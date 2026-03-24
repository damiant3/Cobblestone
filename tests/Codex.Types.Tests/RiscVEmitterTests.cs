using System.Diagnostics;
using Xunit;

namespace Codex.Types.Tests;

public class RiscVEmitterTests
{
    [Fact]
    public void Simple_integer_emits_elf_bytes()
    {
        string source = """
            main : Integer
            main = 42
            """;
        byte[]? bytes = Helpers.CompileToRiscV(source, "simple_rv");
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);
        // Verify ELF magic
        Assert.Equal(0x7F, bytes[0]);
        Assert.Equal((byte)'E', bytes[1]);
        Assert.Equal((byte)'L', bytes[2]);
        Assert.Equal((byte)'F', bytes[3]);
        // ELF64
        Assert.Equal(2, bytes[4]);
        // Little-endian
        Assert.Equal(1, bytes[5]);
    }

    [Fact]
    public void Square_emits_elf_bytes()
    {
        string source = """
            square : Integer -> Integer
            square (x) = x * x

            main : Integer
            main = square 5
            """;
        byte[]? bytes = Helpers.CompileToRiscV(source, "square_rv");
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);
        AssertValidElf(bytes);
    }

    [Fact]
    public void Factorial_emits_elf_bytes()
    {
        string source = """
            factorial : Integer -> Integer
            factorial (n) = if n == 0 then 1 else n * factorial (n - 1)

            main : Integer
            main = factorial 5
            """;
        byte[]? bytes = Helpers.CompileToRiscV(source, "factorial_rv");
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);
        AssertValidElf(bytes);
    }

    [Fact]
    public void Arithmetic_emits_elf_bytes()
    {
        string source = """
            add : Integer -> Integer -> Integer
            add (x) (y) = x + y

            main : Integer
            main = add 3 4
            """;
        byte[]? bytes = Helpers.CompileToRiscV(source, "arith_rv");
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);
        AssertValidElf(bytes);
    }

    [Fact]
    public void Let_binding_emits_elf_bytes()
    {
        string source = """
            main : Integer
            main = let x = 10 in let y = 20 in x + y
            """;
        byte[]? bytes = Helpers.CompileToRiscV(source, "let_rv");
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);
        AssertValidElf(bytes);
    }

    [Fact]
    public void If_else_emits_elf_bytes()
    {
        string source = """
            main : Integer
            main = if 1 == 1 then 42 else 0
            """;
        byte[]? bytes = Helpers.CompileToRiscV(source, "ifelse_rv");
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);
        AssertValidElf(bytes);
    }

    [Fact]
    public void Elf_has_riscv_machine_type()
    {
        string source = """
            main : Integer
            main = 1
            """;
        byte[]? bytes = Helpers.CompileToRiscV(source, "machine_rv");
        Assert.NotNull(bytes);
        // e_machine at offset 18 (2 bytes, little-endian)
        ushort machine = (ushort)(bytes[18] | (bytes[19] << 8));
        Assert.Equal(243, machine); // EM_RISCV
    }

    [Fact]
    public void Fork_await_compiles_riscv()
    {
        string source = """
            compute : Nothing -> Integer
            compute (x) = 42

            do-fork : [Concurrent] Integer
            do-fork = let t = fork compute in await t

            main : Integer
            main = do-fork
            """;
        byte[]? bytes = Helpers.CompileToRiscV(source, "fork_rv");
        Assert.NotNull(bytes);
        AssertValidElf(bytes);
    }

    [Fact]
    public void Elf_has_valid_entry_point()
    {
        string source = """
            main : Integer
            main = 1
            """;
        byte[]? bytes = Helpers.CompileToRiscV(source, "entry_rv");
        Assert.NotNull(bytes);
        // e_entry at offset 24 (8 bytes, little-endian)
        ulong entry = BitConverter.ToUInt64(bytes, 24);
        Assert.True(entry >= 0x10000, "Entry point should be at or above base address 0x10000");
    }

    static void AssertValidElf(byte[] bytes)
    {
        Assert.True(bytes.Length >= 64, "ELF must be at least 64 bytes (header size)");
        Assert.Equal(0x7F, bytes[0]);
        Assert.Equal((byte)'E', bytes[1]);
        Assert.Equal((byte)'L', bytes[2]);
        Assert.Equal((byte)'F', bytes[3]);
    }

    // ── Integration tests (require qemu-riscv64 via WSL) ────────

    [Fact]
    public void Integer_42_runs_under_qemu()
    {
        string source = """
            main : Integer
            main = 42
            """;
        string? output = CompileAndRun(source, "int42_run_rv");
        if (output is null) return;
        Assert.Equal("42", output.Trim());
    }

    [Fact]
    public void Arithmetic_add_runs_under_qemu()
    {
        string source = """
            add : Integer -> Integer -> Integer
            add (x) (y) = x + y

            main : Integer
            main = add 3 4
            """;
        string? output = CompileAndRun(source, "add_run_rv");
        if (output is null) return;
        Assert.Equal("7", output.Trim());
    }

    [Fact]
    public void Square_runs_under_qemu()
    {
        string source = """
            square : Integer -> Integer
            square (x) = x * x

            main : Integer
            main = square 5
            """;
        string? output = CompileAndRun(source, "square_run_rv");
        if (output is null) return;
        Assert.Equal("25", output.Trim());
    }

    [Fact]
    public void Let_binding_runs_under_qemu()
    {
        string source = """
            main : Integer
            main = let x = 10 in let y = 20 in x + y
            """;
        string? output = CompileAndRun(source, "let_run_rv");
        if (output is null) return;
        Assert.Equal("30", output.Trim());
    }

    [Fact]
    public void Factorial_runs_under_qemu()
    {
        string source = """
            factorial : Integer -> Integer
            factorial (n) = if n == 0 then 1 else n * factorial (n - 1)

            main : Integer
            main = factorial 5
            """;
        string? output = CompileAndRun(source, "fact_run_rv");
        if (output is null) return;
        Assert.Equal("120", output.Trim());
    }

    // ── Helpers ──────────────────────────────────────────────────

    static string? CompileAndRun(string source, string moduleName)
    {
        byte[]? bytes = Helpers.CompileToRiscV(source, moduleName);
        if (bytes is null) return null;

        if (!IsQemuAvailable()) return null;

        string tempDir = Path.Combine(Path.GetTempPath(),
            "codex_rv_test_" + moduleName + "_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);
        try
        {
            string elfPath = Path.Combine(tempDir, moduleName);
            File.WriteAllBytes(elfPath, bytes);

            // Copy into WSL filesystem (NTFS doesn't support chmod),
            // make executable, then run under qemu-riscv64
            string wslPath = ToWslPath(elfPath);
            string wslTmp = $"/tmp/codex_rv_{moduleName}_{Guid.NewGuid().ToString("N")[..8]}";

            ProcessStartInfo psi = new("bash",
                $"-c \"cp '{wslPath}' '{wslTmp}' && chmod +x '{wslTmp}' && qemu-riscv64 '{wslTmp}'; EXIT=$?; rm -f '{wslTmp}'; exit $EXIT\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = tempDir
            };

            using Process? proc = Process.Start(psi);
            if (proc is null) return null;

            string stdout = proc.StandardOutput.ReadToEnd();
            string stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit(10_000);

            if (proc.ExitCode != 0)
                throw new InvalidOperationException(
                    $"qemu-riscv64 exited with code {proc.ExitCode}.\nstdout: {stdout}\nstderr: {stderr}");

            return stdout;
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    static bool IsQemuAvailable()
    {
        try
        {
            ProcessStartInfo psi = new("bash", "-c \"qemu-riscv64 --version\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using Process? proc = Process.Start(psi);
            if (proc is null) return false;
            proc.WaitForExit(5_000);
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    static string ToWslPath(string windowsPath)
    {
        string full = Path.GetFullPath(windowsPath);
        if (full.Length >= 2 && full[1] == ':')
        {
            char drive = char.ToLowerInvariant(full[0]);
            string rest = full[2..].Replace('\\', '/');
            return $"/mnt/{drive}{rest}";
        }
        return full.Replace('\\', '/');
    }

    // ═════════════════════════════════════════════════════════════
    // Records
    // ═════════════════════════════════════════════════════════════

    [Fact]
    public void Record_creation_and_field_access_emits_elf()
    {
        string source = """
            Point = record {
              x : Integer,
              y : Integer
            }

            main : Integer
            main = let p = Point { x = 3, y = 4 } in p.x + p.y
            """;
        byte[]? bytes = Helpers.CompileToRiscV(source, "record_rv");
        Assert.NotNull(bytes);
        AssertValidElf(bytes);
    }

    [Fact]
    public void Record_field_access_runs_under_qemu()
    {
        string source = """
            Point = record {
              x : Integer,
              y : Integer
            }

            main : Integer
            main = let p = Point { x = 3, y = 4 } in p.x + p.y
            """;
        string? output = CompileAndRun(source, "record_run_rv");
        if (output is null) return;
        Assert.Equal("7", output.Trim());
    }

    [Fact]
    public void Record_passed_to_function_runs_under_qemu()
    {
        string source = """
            Pair = record {
              fst : Integer,
              snd : Integer
            }

            sum-pair : Pair -> Integer
            sum-pair (p) = p.fst + p.snd

            main : Integer
            main = sum-pair (Pair { fst = 10, snd = 20 })
            """;
        string? output = CompileAndRun(source, "recfn_run_rv");
        if (output is null) return;
        Assert.Equal("30", output.Trim());
    }

    // ═════════════════════════════════════════════════════════════
    // Sum types + Pattern matching
    // ═════════════════════════════════════════════════════════════

    [Fact]
    public void Sum_type_construction_emits_elf()
    {
        string source = """
            Shape =
              | Circle (r : Integer)
              | Rect (w : Integer) (h : Integer)

            area : Shape -> Integer
            area (s) =
              when s
                if Circle (r) -> r * r
                if Rect (w) (h) -> w * h

            main : Integer
            main = area (Circle 5)
            """;
        byte[]? bytes = Helpers.CompileToRiscV(source, "sum_rv");
        Assert.NotNull(bytes);
        AssertValidElf(bytes);
    }

    [Fact]
    public void Pattern_match_circle_runs_under_qemu()
    {
        string source = """
            Shape =
              | Circle (r : Integer)
              | Rect (w : Integer) (h : Integer)

            area : Shape -> Integer
            area (s) =
              when s
                if Circle (r) -> r * r
                if Rect (w) (h) -> w * h

            main : Integer
            main = area (Circle 5)
            """;
        string? output = CompileAndRun(source, "match_circle_rv");
        if (output is null) return;
        Assert.Equal("25", output.Trim());
    }

    [Fact]
    public void Pattern_match_rect_runs_under_qemu()
    {
        string source = """
            Shape =
              | Circle (r : Integer)
              | Rect (w : Integer) (h : Integer)

            area : Shape -> Integer
            area (s) =
              when s
                if Circle (r) -> r * r
                if Rect (w) (h) -> w * h

            main : Integer
            main = area (Rect 5 7)
            """;
        string? output = CompileAndRun(source, "match_rect_rv");
        if (output is null) return;
        Assert.Equal("35", output.Trim());
    }

    [Fact]
    public void Record_and_match_combined_runs_under_qemu()
    {
        string source = """
            Point = record {
              x : Integer,
              y : Integer
            }

            Shape =
              | Circle (r : Integer)
              | Rect (w : Integer) (h : Integer)

            area : Shape -> Integer
            area (s) =
              when s
                if Circle (r) -> r * r
                if Rect (w) (h) -> w * h

            main : Integer
            main = let p = Point { x = 3, y = 4 } in area (Rect p.x p.y)
            """;
        string? output = CompileAndRun(source, "rec_match_rv");
        if (output is null) return;
        Assert.Equal("12", output.Trim());
    }

    // ═════════════════════════════════════════════════════════════
    // Bare Metal tests
    // ═════════════════════════════════════════════════════════════
    // Register spill (>10 locals)
    // ═════════════════════════════════════════════════════════════

    [Fact]
    public void Many_locals_spill_runs_under_qemu()
    {
        string source = """
            main : Integer
            main =
              let a = 1 in let b = 2 in let c = 3 in
              let d = 4 in let e = 5 in let f = 6 in
              a + b + c + d + e + f
            """;
        string? output = CompileAndRun(source, "spill_rv");
        if (output is null) return;
        Assert.Equal("21", output.Trim());
    }

    // ═════════════════════════════════════════════════════════════
    // Lists + higher-order functions
    // ═════════════════════════════════════════════════════════════

    [Fact]
    public void List_literal_length_runs_under_qemu()
    {
        string source = """
            main : Integer
            main = list-length [1, 2, 3]
            """;
        string? output = CompileAndRun(source, "listlen_rv");
        if (output is null) return;
        Assert.Equal("3", output.Trim());
    }

    [Fact]
    public void List_at_runs_under_qemu()
    {
        string source = """
            main : Integer
            main = list-at [10, 20, 30] 1
            """;
        string? output = CompileAndRun(source, "listat_rv");
        if (output is null) return;
        Assert.Equal("20", output.Trim());
    }

    [Fact]
    public void List_cons_runs_under_qemu()
    {
        string source = """
            main : Integer
            main = list-length (0 :: [1, 2, 3])
            """;
        string? output = CompileAndRun(source, "listcons_rv");
        if (output is null) return;
        Assert.Equal("4", output.Trim());
    }

    [Fact]
    public void List_append_runs_under_qemu()
    {
        string source = """
            main : Integer
            main = list-length ([1, 2] ++ [3, 4, 5])
            """;
        string? output = CompileAndRun(source, "listapp_rv");
        if (output is null) return;
        Assert.Equal("5", output.Trim());
    }

    [Fact]
    public void Higher_order_function_runs_under_qemu()
    {
        string source = """
            apply-fn : (Integer -> Integer) -> Integer -> Integer
            apply-fn (f) (x) = f x

            double : Integer -> Integer
            double (n) = n * 2

            main : Integer
            main = apply-fn double 21
            """;
        string? output = CompileAndRun(source, "hof_rv");
        if (output is null) return;
        Assert.Equal("42", output.Trim());
    }

    // ═════════════════════════════════════════════════════════════
    // Text builtins
    // ═════════════════════════════════════════════════════════════

    [Fact]
    public void Text_length_runs_under_qemu()
    {
        string source = """
            main : Integer
            main = text-length "hello"
            """;
        string? output = CompileAndRun(source, "textlen_rv");
        if (output is null) return;
        Assert.Equal("5", output.Trim());
    }

    [Fact]
    public void Text_to_integer_runs_under_qemu()
    {
        string source = """
            main : Integer
            main = text-to-integer "42"
            """;
        string? output = CompileAndRun(source, "txt2int_rv");
        if (output is null) return;
        Assert.Equal("42", output.Trim());
    }

    [Fact]
    public void Text_to_integer_negative_runs_under_qemu()
    {
        string source = """
            main : Integer
            main = text-to-integer "-7"
            """;
        string? output = CompileAndRun(source, "txt2int_neg_rv");
        if (output is null) return;
        Assert.Equal("-7", output.Trim());
    }

    [Fact]
    public void Text_concat_emits_elf()
    {
        string source = """
            main : Text
            main = "hello " ++ "world"
            """;
        byte[]? bytes = Helpers.CompileToRiscV(source, "concat_rv");
        Assert.NotNull(bytes);
        AssertValidElf(bytes);
    }

    [Fact]
    public void Text_concat_runs_under_qemu()
    {
        string source = """
            main : Text
            main = "hello " ++ "world"
            """;
        string? output = CompileAndRun(source, "concat_run_rv");
        if (output is null) return;
        Assert.Equal("hello world", output.Trim());
    }

    [Fact]
    public void Show_integer_runs_under_qemu()
    {
        string source = """
            main : Text
            main = show 42
            """;
        string? output = CompileAndRun(source, "show_int_rv");
        if (output is null) return;
        Assert.Equal("42", output.Trim());
    }

    [Fact]
    public void String_equality_runs_under_qemu()
    {
        string source = """
            main : Integer
            main = if "abc" == "abc" then 1 else 0
            """;
        string? output = CompileAndRun(source, "streq_rv");
        if (output is null) return;
        Assert.Equal("1", output.Trim());
    }

    [Fact]
    public void String_inequality_runs_under_qemu()
    {
        string source = """
            main : Integer
            main = if "abc" == "xyz" then 1 else 0
            """;
        string? output = CompileAndRun(source, "strneq_rv");
        if (output is null) return;
        Assert.Equal("0", output.Trim());
    }

    // ═════════════════════════════════════════════════════════════
    // Bare Metal tests
    // ═════════════════════════════════════════════════════════════

    [Fact]
    public void BareMetal_integer_emits_flat_binary()
    {
        string source = """
            main : Integer
            main = 42
            """;
        byte[]? bytes = Helpers.CompileToRiscVBareMetal(source, "bm_int");
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);

        // Flat binary: first 4 bytes should be a valid RISC-V instruction, not ELF magic
        Assert.NotEqual(0x7F, bytes[0]); // not ELF
        // First instruction should be a J-type (jal x0, offset) trampoline to _start
        uint firstInsn = (uint)(bytes[0] | (bytes[1] << 8) | (bytes[2] << 16) | (bytes[3] << 24));
        uint opcode = firstInsn & 0x7F;
        Assert.Equal(0x6Fu, opcode); // JAL opcode — jump to _start
    }

    [Fact]
    public void BareMetal_factorial_emits_flat_binary()
    {
        string source = """
            factorial : Integer -> Integer
            factorial (n) = if n == 0 then 1 else n * factorial (n - 1)

            main : Integer
            main = factorial 5
            """;
        byte[]? bytes = Helpers.CompileToRiscVBareMetal(source, "bm_fact");
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);
        Assert.NotEqual(0x7F, bytes[0]); // flat binary, not ELF
    }

    [Fact]
    public void BareMetal_string_emits_flat_binary()
    {
        string source = """
            main : Text
            main = "Hello from bare metal"
            """;
        byte[]? bytes = Helpers.CompileToRiscVBareMetal(source, "bm_text");
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);
        Assert.NotEqual(0x7F, bytes[0]); // flat binary, not ELF
    }

    [Fact]
    public void BareMetal_no_ecall_in_binary()
    {
        // Bare metal should use UART, not Linux syscalls.
        // ecall encodes as 0x00000073.
        string source = """
            main : Integer
            main = 42
            """;
        byte[]? bytes = Helpers.CompileToRiscVBareMetal(source, "bm_no_ecall");
        Assert.NotNull(bytes);

        bool foundEcall = false;
        for (int i = 0; i + 3 < bytes.Length; i += 4)
        {
            uint insn = (uint)(bytes[i] | (bytes[i + 1] << 8) |
                (bytes[i + 2] << 16) | (bytes[i + 3] << 24));
            if (insn == 0x00000073) { foundEcall = true; break; }
        }
        Assert.False(foundEcall, "Bare metal binary should not contain ecall instructions");
    }

    [Fact]
    public void BareMetal_integer_42_runs_under_qemu_system()
    {
        string source = """
            main : Integer
            main = 42
            """;
        string? output = CompileAndRunBareMetal(source, "bm_int42_run");
        if (output is null) return; // skip if qemu-system-riscv64 not available
        Assert.Contains("42", output);
    }

    [Fact]
    public void BareMetal_factorial_runs_under_qemu_system()
    {
        string source = """
            factorial : Integer -> Integer
            factorial (n) = if n == 0 then 1 else n * factorial (n - 1)

            main : Integer
            main = factorial 5
            """;
        string? output = CompileAndRunBareMetal(source, "bm_fact_run");
        if (output is null) return;
        Assert.Contains("120", output);
    }

    static string? CompileAndRunBareMetal(string source, string moduleName)
    {
        byte[]? bytes = Helpers.CompileToRiscVBareMetal(source, moduleName);
        if (bytes is null) return null;
        if (!IsQemuSystemAvailable()) return null;

        string tempDir = Path.Combine(Path.GetTempPath(),
            "codex_bm_test_" + moduleName + "_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);
        try
        {
            string binPath = Path.Combine(tempDir, moduleName + ".bin");
            File.WriteAllBytes(binPath, bytes);

            string qemuPath;
            if (OperatingSystem.IsWindows())
            {
                // Run via WSL on Windows
                qemuPath = ToWslPath(binPath);
            }
            else
            {
                // Native Linux — use path directly, chmod +x for good measure
                File.SetUnixFileMode(binPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                qemuPath = binPath;
            }

            ProcessStartInfo psi = new("bash",
                $"-c \"timeout 5 qemu-system-riscv64 -machine virt -bios none -nographic -serial mon:stdio -kernel '{qemuPath}' 2>/dev/null || true\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = tempDir
            };

            using Process? proc = Process.Start(psi);
            if (proc is null) return null;

            string stdout = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(10_000);
            return stdout;
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    static bool IsQemuSystemAvailable()
    {
        try
        {
            ProcessStartInfo psi = new("bash", "-c \"qemu-system-riscv64 --version\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using Process? proc = Process.Start(psi);
            if (proc is null) return false;
            proc.WaitForExit(5_000);
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
