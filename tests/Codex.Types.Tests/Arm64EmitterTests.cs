using System.Diagnostics;
using Xunit;

namespace Codex.Types.Tests;

public class Arm64EmitterTests
{
    // â”€â”€ ELF structure tests â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void Simple_integer_emits_elf_bytes()
    {
        string source = """
            main : Integer
            main = 42
            """;
        byte[]? bytes = Helpers.CompileToArm64(source, "simple_a64");
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);
        // ELF magic
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
    public void Elf_has_aarch64_machine_type()
    {
        string source = """
            main : Integer
            main = 1
            """;
        byte[]? bytes = Helpers.CompileToArm64(source, "mach_a64");
        Assert.NotNull(bytes);
        // e_machine at offset 18 (ELF64), EM_AARCH64 = 0xB7 = 183
        ushort machine = (ushort)(bytes[18] | (bytes[19] << 8));
        Assert.Equal(183, machine);
    }

    [Fact]
    public void Elf_has_valid_entry_point()
    {
        string source = """
            main : Integer
            main = 99
            """;
        byte[]? bytes = Helpers.CompileToArm64(source, "entry_a64");
        Assert.NotNull(bytes);
        // e_entry at offset 24 (ELF64), 8 bytes LE
        ulong entry = BitConverter.ToUInt64(bytes, 24);
        Assert.True(entry > 0, "Entry point should be non-zero");
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
        byte[]? bytes = Helpers.CompileToArm64(source, "square_a64");
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
        byte[]? bytes = Helpers.CompileToArm64(source, "fact_a64");
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);
        AssertValidElf(bytes);
    }

    // â”€â”€ QEMU execution tests (require qemu-aarch64) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void Integer_42_runs_under_qemu()
    {
        string source = """
            main : Integer
            main = 42
            """;
        string? output = CompileAndRun(source, "int42_run_a64");
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
        string? output = CompileAndRun(source, "add_run_a64");
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
        string? output = CompileAndRun(source, "square_run_a64");
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
        string? output = CompileAndRun(source, "let_run_a64");
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
        string? output = CompileAndRun(source, "fact_run_a64");
        if (output is null) return;
        Assert.Equal("120", output.Trim());
    }

    [Fact]
    public void If_else_runs_under_qemu()
    {
        string source = """
            main : Integer
            main = if 1 == 1 then 42 else 0
            """;
        string? output = CompileAndRun(source, "ifelse_run_a64");
        if (output is null) return;
        Assert.Equal("42", output.Trim());
    }

    [Fact]
    public void Negative_branch_runs_under_qemu()
    {
        string source = """
            main : Integer
            main = if 1 == 2 then 99 else 7
            """;
        string? output = CompileAndRun(source, "neg_run_a64");
        if (output is null) return;
        Assert.Equal("7", output.Trim());
    }

    [Fact]
    public void Subtraction_runs_under_qemu()
    {
        string source = """
            main : Integer
            main = 100 - 37
            """;
        string? output = CompileAndRun(source, "sub_run_a64");
        if (output is null) return;
        Assert.Equal("63", output.Trim());
    }

    [Fact]
    public void Multiply_runs_under_qemu()
    {
        string source = """
            main : Integer
            main = 6 * 7
            """;
        string? output = CompileAndRun(source, "mul_run_a64");
        if (output is null) return;
        Assert.Equal("42", output.Trim());
    }

    [Fact]
    public void Division_runs_under_qemu()
    {
        string source = """
            main : Integer
            main = 100 / 4
            """;
        string? output = CompileAndRun(source, "div_run_a64");
        if (output is null) return;
        Assert.Equal("25", output.Trim());
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
            main = let p = Point { x = 10, y = 20 } in p.x + p.y
            """;
        string? output = CompileAndRun(source, "rec_run_a64");
        if (output is null) return;
        Assert.Equal("30", output.Trim());
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
        string? output = CompileAndRun(source, "recfn_run_a64");
        if (output is null) return;
        Assert.Equal("30", output.Trim());
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
        string? output = CompileAndRun(source, "matchc_run_a64");
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
            main = area (Rect 6 7)
            """;
        string? output = CompileAndRun(source, "matchr_run_a64");
        if (output is null) return;
        Assert.Equal("42", output.Trim());
    }

    [Fact]
    public void Record_and_match_combined_runs_under_qemu()
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
            main = area (Circle 3) + area (Rect 4 5)
            """;
        string? output = CompileAndRun(source, "recmatch_run_a64");
        if (output is null) return;
        Assert.Equal("29", output.Trim());
    }

    [Fact]
    public void Many_locals_spill_runs_under_qemu()
    {
        string source = """
            main : Integer
            main =
                let a = 1 in
                let b = 2 in
                let c = 3 in
                let d = 4 in
                let e = 5 in
                let f = 6 in
                let g = 7 in
                let h = 8 in
                let i = 9 in
                let j = 10 in
                let k = 11 in
                let l = 12 in
                a + b + c + d + e + f + g + h + i + j + k + l
            """;
        string? output = CompileAndRun(source, "spill_run_a64");
        if (output is null) return;
        Assert.Equal("78", output.Trim());
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
        string? output = CompileAndRun(source, "hof_run_a64");
        if (output is null) return;
        Assert.Equal("42", output.Trim());
    }

    [Fact]
    public void Text_length_runs_under_qemu()
    {
        string source = """
            main : Integer
            main = text-length "hello"
            """;
        string? output = CompileAndRun(source, "txtlen_run_a64");
        if (output is null) return;
        Assert.Equal("5", output.Trim());
    }

    [Fact]
    public void Text_to_integer_runs_under_qemu()
    {
        string source = """
            main : Integer
            main = text-to-integer "123"
            """;
        string? output = CompileAndRun(source, "txtint_run_a64");
        if (output is null) return;
        Assert.Equal("123", output.Trim());
    }

    [Fact]
    public void Text_to_integer_negative_runs_under_qemu()
    {
        string source = """
            main : Integer
            main = text-to-integer "-42"
            """;
        string? output = CompileAndRun(source, "txtneg_run_a64");
        if (output is null) return;
        Assert.Equal("-42", output.Trim());
    }

    [Fact]
    public void Show_integer_runs_under_qemu()
    {
        string source = """
            main : Text
            main = show 42
            """;
        string? output = CompileAndRun(source, "show_run_a64");
        if (output is null) return;
        Assert.Equal("42", output.Trim());
    }

    [Fact]
    public void String_equality_runs_under_qemu()
    {
        string source = """
            main : Integer
            main = if "hello" == "hello" then 1 else 0
            """;
        string? output = CompileAndRun(source, "streq_run_a64");
        if (output is null) return;
        Assert.Equal("1", output.Trim());
    }

    [Fact]
    public void String_inequality_runs_under_qemu()
    {
        string source = """
            main : Integer
            main = if "hello" == "world" then 1 else 0
            """;
        string? output = CompileAndRun(source, "strne_run_a64");
        if (output is null) return;
        Assert.Equal("0", output.Trim());
    }

    [Fact]
    public void Text_concat_runs_under_qemu()
    {
        string source = """
            main : Text
            main = "hello " ++ "world"
            """;
        string? output = CompileAndRun(source, "concat_run_a64");
        if (output is null) return;
        Assert.Equal("hello world", output.Trim());
    }

    [Fact]
    public void List_literal_length_runs_under_qemu()
    {
        string source = """
            main : Integer
            main = list-length [1, 2, 3]
            """;
        string? output = CompileAndRun(source, "listlen_run_a64");
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
        string? output = CompileAndRun(source, "listat_run_a64");
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
        string? output = CompileAndRun(source, "listcons_run_a64");
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
        string? output = CompileAndRun(source, "listapp_run_a64");
        if (output is null) return;
        Assert.Equal("5", output.Trim());
    }

    // ── Concurrency (sequential fork/await) ─────────────────────

    [Fact]
    public void Fork_await_compiles_arm64()
    {
        string source = """
            compute : Nothing -> Integer
            compute (x) = 42

            do-fork : [Concurrent] Integer
            do-fork = let t = fork compute in await t

            main : Integer
            main = do-fork
            """;
        byte[]? bytes = Helpers.CompileToArm64(source, "fork_a64");
        Assert.NotNull(bytes);
        AssertValidElf(bytes);
    }

    //â”€â”€ Helper infrastructure â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    static void AssertValidElf(byte[] bytes)
    {
        Assert.True(bytes.Length >= 64, "ELF file too short");
        Assert.Equal(0x7F, bytes[0]);
        Assert.Equal((byte)'E', bytes[1]);
        Assert.Equal((byte)'L', bytes[2]);
        Assert.Equal((byte)'F', bytes[3]);
        Assert.Equal(2, bytes[4]); // ELF64
        Assert.Equal(1, bytes[5]); // Little-endian
        // EM_AARCH64 = 0xB7 = 183
        ushort machine = (ushort)(bytes[18] | (bytes[19] << 8));
        Assert.Equal(183, machine);
    }

    string? CompileAndRun(string source, string chapterName)
    {
        if (!IsQemuAvailable()) return null;

        byte[]? bytes = Helpers.CompileToArm64(source, chapterName);
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);

        string tempDir = Path.Combine(Path.GetTempPath(), $"codex_a64_{chapterName}_{Guid.NewGuid().ToString("N")[..8]}");
        Directory.CreateDirectory(tempDir);

        try
        {
            string elfPath = Path.Combine(tempDir, chapterName);
            File.WriteAllBytes(elfPath, bytes);

            string wslPath = ToWslPath(elfPath);
            string wslTmp = $"/tmp/codex_a64_{chapterName}_{Guid.NewGuid().ToString("N")[..8]}";

            // Copy into WSL filesystem, make executable, run under qemu-aarch64
            // Use 'wsl' explicitly â€” 'bash' on Windows may be Git Bash, not WSL
            ProcessStartInfo psi = new("wsl",
                $"bash -c \"cp '{wslPath}' '{wslTmp}' && chmod +x '{wslTmp}' && qemu-aarch64 '{wslTmp}'; EXIT=$?; rm -f '{wslTmp}'; exit $EXIT\"")
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
                    $"qemu-aarch64 exited with code {proc.ExitCode}.\nstdout: {stdout}\nstderr: {stderr}");

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
            ProcessStartInfo psi = new("wsl", "bash -c \"qemu-aarch64 --version\"")
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
}
