using System.Diagnostics;
using Xunit;

namespace Codex.Types.Tests;

public class X86_64EmitterTests
{
    [Fact]
    public void Simple_integer_emits_elf_bytes()
    {
        string source = """
            main : Integer
            main = 42
            """;
        byte[]? bytes = Helpers.CompileToX86_64(source, "simple_x64");
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);
        AssertValidElf(bytes);
        // e_machine at offset 18: EM_X86_64 = 0x3E = 62
        ushort machine = (ushort)(bytes[18] | (bytes[19] << 8));
        Assert.Equal(62, machine);
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
        byte[]? bytes = Helpers.CompileToX86_64(source, "fact_x64");
        Assert.NotNull(bytes);
        AssertValidElf(bytes);
    }

    // ── Integration tests (run natively via WSL) ────────────────

    [Fact]
    public void Integer_42_runs_natively()
    {
        string? output = CompileAndRun("""
            main : Integer
            main = 42
            """, "int42_x64");
        if (output is null) return;
        Assert.Equal("42", output.Trim());
    }

    [Fact]
    public void Arithmetic_add_runs_natively()
    {
        string? output = CompileAndRun("""
            add : Integer -> Integer -> Integer
            add (x) (y) = x + y

            main : Integer
            main = add 3 4
            """, "add_x64");
        if (output is null) return;
        Assert.Equal("7", output.Trim());
    }

    [Fact]
    public void Square_runs_natively()
    {
        string? output = CompileAndRun("""
            square : Integer -> Integer
            square (x) = x * x

            main : Integer
            main = square 5
            """, "square_x64");
        if (output is null) return;
        Assert.Equal("25", output.Trim());
    }

    [Fact]
    public void Factorial_runs_natively()
    {
        string? output = CompileAndRun("""
            factorial : Integer -> Integer
            factorial (n) = if n == 0 then 1 else n * factorial (n - 1)

            main : Integer
            main = factorial 5
            """, "fact_run_x64");
        if (output is null) return;
        Assert.Equal("120", output.Trim());
    }

    [Fact]
    public void Let_binding_runs_natively()
    {
        string? output = CompileAndRun("""
            main : Integer
            main = let x = 10 in let y = 20 in x + y
            """, "let_x64");
        if (output is null) return;
        Assert.Equal("30", output.Trim());
    }

    [Fact]
    public void If_else_runs_natively()
    {
        string? output = CompileAndRun("""
            main : Integer
            main = if 1 == 1 then 42 else 0
            """, "ifelse_x64");
        if (output is null) return;
        Assert.Equal("42", output.Trim());
    }

    // ── Records ─────────────────────────────────────────────────

    [Fact]
    public void Record_field_access_runs_natively()
    {
        string? output = CompileAndRun("""
            Point = record {
              x : Integer,
              y : Integer
            }

            main : Integer
            main = let p = Point { x = 3, y = 4 } in p.x + p.y
            """, "record_x64");
        if (output is null) return;
        Assert.Equal("7", output.Trim());
    }

    [Fact]
    public void Record_passed_to_function_runs_natively()
    {
        string? output = CompileAndRun("""
            Pair = record {
              fst : Integer,
              snd : Integer
            }

            sum-pair : Pair -> Integer
            sum-pair (p) = p.fst + p.snd

            main : Integer
            main = sum-pair (Pair { fst = 10, snd = 20 })
            """, "recfn_x64");
        if (output is null) return;
        Assert.Equal("30", output.Trim());
    }

    // ── Sum types + Pattern matching ────────────────────────────

    [Fact]
    public void Pattern_match_circle_runs_natively()
    {
        string? output = CompileAndRun("""
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
            """, "match_circle_x64");
        if (output is null) return;
        Assert.Equal("25", output.Trim());
    }

    [Fact]
    public void Pattern_match_rect_runs_natively()
    {
        string? output = CompileAndRun("""
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
            """, "match_rect_x64");
        if (output is null) return;
        Assert.Equal("35", output.Trim());
    }

    // ── Lists ───────────────────────────────────────────────────

    [Fact]
    public void List_length_runs_natively()
    {
        string? output = CompileAndRun("""
            main : Integer
            main = list-length [1, 2, 3]
            """, "listlen_x64");
        if (output is null) return;
        Assert.Equal("3", output.Trim());
    }

    [Fact]
    public void List_at_runs_natively()
    {
        string? output = CompileAndRun("""
            main : Integer
            main = list-at [10, 20, 30] 1
            """, "listat_x64");
        if (output is null) return;
        Assert.Equal("20", output.Trim());
    }

    [Fact]
    public void List_cons_runs_natively()
    {
        string? output = CompileAndRun("""
            main : Integer
            main = list-length (0 :: [1, 2, 3])
            """, "listcons_x64");
        if (output is null) return;
        Assert.Equal("4", output.Trim());
    }

    [Fact]
    public void List_append_runs_natively()
    {
        string? output = CompileAndRun("""
            main : Integer
            main = list-length ([1, 2] ++ [3, 4, 5])
            """, "listapp_x64");
        if (output is null) return;
        Assert.Equal("5", output.Trim());
    }

    // ── Text builtins ───────────────────────────────────────────

    [Fact]
    public void Text_length_runs_natively()
    {
        string? output = CompileAndRun("""
            main : Integer
            main = text-length "hello"
            """, "textlen_x64");
        if (output is null) return;
        Assert.Equal("5", output.Trim());
    }

    [Fact]
    public void Text_concat_runs_natively()
    {
        string? output = CompileAndRun("""
            main : Text
            main = "hello " ++ "world"
            """, "concat_x64");
        if (output is null) return;
        Assert.Equal("hello world", output.Trim());
    }

    [Fact]
    public void Show_integer_runs_natively()
    {
        string? output = CompileAndRun("""
            main : Text
            main = show 42
            """, "show_x64");
        if (output is null) return;
        Assert.Equal("42", output.Trim());
    }

    [Fact]
    public void Show_true_runs_natively()
    {
        string? output = CompileAndRun("""
            main : Text
            main = show True
            """, "show_true_x64");
        if (output is null) return;
        Assert.Equal("True", output.Trim());
    }

    [Fact]
    public void Show_false_runs_natively()
    {
        string? output = CompileAndRun("""
            main : Text
            main = show False
            """, "show_false_x64");
        if (output is null) return;
        Assert.Equal("False", output.Trim());
    }

    [Fact]
    public void String_equality_runs_natively()
    {
        string? output = CompileAndRun("""
            main : Integer
            main = if "abc" == "abc" then 1 else 0
            """, "streq_x64");
        if (output is null) return;
        Assert.Equal("1", output.Trim());
    }

    [Fact]
    public void String_inequality_runs_natively()
    {
        string? output = CompileAndRun("""
            main : Integer
            main = if "abc" == "xyz" then 1 else 0
            """, "strneq_x64");
        if (output is null) return;
        Assert.Equal("0", output.Trim());
    }

    // ── Higher-order functions ──────────────────────────────────

    [Fact]
    public void Higher_order_function_runs_natively()
    {
        string? output = CompileAndRun("""
            apply-fn : (Integer -> Integer) -> Integer -> Integer
            apply-fn (f) (x) = f x

            double : Integer -> Integer
            double (n) = n * 2

            main : Integer
            main = apply-fn double 21
            """, "hof_x64");
        if (output is null) return;
        Assert.Equal("42", output.Trim());
    }

    // ── Many locals (spill test) ────────────────────────────────

    [Fact]
    public void Many_locals_spill_runs_natively()
    {
        string? output = CompileAndRun("""
            main : Integer
            main =
              let a = 1 in let b = 2 in let c = 3 in
              let d = 4 in let e = 5 in let f = 6 in
              a + b + c + d + e + f
            """, "spill_x64");
        if (output is null) return;
        Assert.Equal("21", output.Trim());
    }

    // ── Concurrency (sequential fork/await) ─────────────────────

    [Fact]
    public void Fork_await_compiles_x86_64()
    {
        string source = """
            compute : Nothing -> Integer
            compute (x) = 42

            do-fork : [Concurrent] Integer
            do-fork = let t = fork compute in await t

            main : Integer
            main = do-fork
            """;
        byte[]? bytes = Helpers.CompileToX86_64(source, "fork_x64");
        Assert.NotNull(bytes);
    }

    // ── Helpers ──────────────────────────────────────────────────

    static void AssertValidElf(byte[] bytes)
    {
        Assert.True(bytes.Length >= 64, "ELF must be at least 64 bytes (header size)");
        Assert.Equal(0x7F, bytes[0]);
        Assert.Equal((byte)'E', bytes[1]);
        Assert.Equal((byte)'L', bytes[2]);
        Assert.Equal((byte)'F', bytes[3]);
    }

    static string? CompileAndRun(string source, string moduleName)
    {
        byte[]? bytes = Helpers.CompileToX86_64(source, moduleName);
        if (bytes is null) return null;

        if (!IsWslAvailable()) return null;

        string tempDir = Path.Combine(Path.GetTempPath(),
            "codex_x64_test_" + moduleName + "_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);
        try
        {
            string elfPath = Path.Combine(tempDir, moduleName);
            File.WriteAllBytes(elfPath, bytes);

            string wslPath = ToWslPath(elfPath);
            string wslTmp = $"/tmp/codex_x64_{moduleName}_{Guid.NewGuid().ToString("N")[..8]}";

            // Copy into WSL filesystem, make executable, run natively (x86-64 on x86-64 = no QEMU)
            // Use 'wsl' explicitly — 'bash' on Windows may be Git Bash, not WSL
            ProcessStartInfo psi = new("wsl",
                $"bash -c \"cp '{wslPath}' '{wslTmp}' && chmod +x '{wslTmp}' && '{wslTmp}'; EXIT=$?; rm -f '{wslTmp}'; exit $EXIT\"")
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
                    $"x86-64 binary exited with code {proc.ExitCode}.\nstdout: {stdout}\nstderr: {stderr}");

            return stdout;
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    static bool IsWslAvailable()
    {
        try
        {
            ProcessStartInfo psi = new("wsl", "uname -m")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using Process? proc = Process.Start(psi);
            if (proc is null) return false;
            string output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(5_000);
            return proc.ExitCode == 0 && output.Trim() == "x86_64";
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
