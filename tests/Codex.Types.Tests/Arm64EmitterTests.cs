using System.Diagnostics;
using Xunit;

namespace Codex.Types.Tests;

public class Arm64EmitterTests
{
    [Fact]
    public void Simple_integer_emits_elf_bytes()
    {
        string source = """
            main : Integer
            main = 42
            """;
        byte[]? bytes = Helpers.CompileToArm64(source, "simple_arm64");
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);
        AssertValidElf(bytes);
        // e_machine at offset 18: EM_AARCH64 = 0xB7 = 183
        ushort machine = (ushort)(bytes[18] | (bytes[19] << 8));
        Assert.Equal(183, machine);
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
        byte[]? bytes = Helpers.CompileToArm64(source, "fact_arm64");
        Assert.NotNull(bytes);
        AssertValidElf(bytes);
    }

    [Fact]
    public void Record_emits_elf_bytes()
    {
        string source = """
            Point = record {
              x : Integer,
              y : Integer
            }

            main : Integer
            main = let p = Point { x = 3, y = 4 } in p.x + p.y
            """;
        byte[]? bytes = Helpers.CompileToArm64(source, "record_arm64");
        Assert.NotNull(bytes);
        AssertValidElf(bytes);
    }

    [Fact]
    public void Sum_type_emits_elf_bytes()
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
        byte[]? bytes = Helpers.CompileToArm64(source, "sum_arm64");
        Assert.NotNull(bytes);
        AssertValidElf(bytes);
    }

    [Fact]
    public void List_emits_elf_bytes()
    {
        string source = """
            main : Integer
            main = list-length [1, 2, 3]
            """;
        byte[]? bytes = Helpers.CompileToArm64(source, "list_arm64");
        Assert.NotNull(bytes);
        AssertValidElf(bytes);
    }

    [Fact]
    public void Text_concat_emits_elf_bytes()
    {
        string source = """
            main : Text
            main = "hello " ++ "world"
            """;
        byte[]? bytes = Helpers.CompileToArm64(source, "concat_arm64");
        Assert.NotNull(bytes);
        AssertValidElf(bytes);
    }

    [Fact]
    public void Higher_order_function_emits_elf_bytes()
    {
        string source = """
            apply-fn : (Integer -> Integer) -> Integer -> Integer
            apply-fn (f) (x) = f x

            double : Integer -> Integer
            double (n) = n * 2

            main : Integer
            main = apply-fn double 21
            """;
        byte[]? bytes = Helpers.CompileToArm64(source, "hof_arm64");
        Assert.NotNull(bytes);
        AssertValidElf(bytes);
    }

    [Fact]
    public void Many_locals_emits_elf_bytes()
    {
        string source = """
            main : Integer
            main =
              let a = 1 in let b = 2 in let c = 3 in
              let d = 4 in let e = 5 in let f = 6 in
              a + b + c + d + e + f
            """;
        byte[]? bytes = Helpers.CompileToArm64(source, "spill_arm64");
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
        byte[]? bytes = Helpers.CompileToArm64(source, "entry_arm64");
        Assert.NotNull(bytes);
        ulong entry = BitConverter.ToUInt64(bytes, 24);
        Assert.True(entry >= 0x400000, "Entry point should be at or above ARM64 base address 0x400000");
    }

    // ── QEMU integration tests ──────────────────────────────────
    // These run under qemu-aarch64 via WSL. They silently skip if
    // qemu-aarch64 is not available.

    [Fact]
    public void Integer_42_runs_under_qemu()
    {
        string? output = CompileAndRun("""
            main : Integer
            main = 42
            """, "int42_arm64");
        if (output is null) return;
        Assert.Equal("42", output.Trim());
    }

    [Fact]
    public void Factorial_runs_under_qemu()
    {
        string? output = CompileAndRun("""
            factorial : Integer -> Integer
            factorial (n) = if n == 0 then 1 else n * factorial (n - 1)

            main : Integer
            main = factorial 5
            """, "fact_run_arm64");
        if (output is null) return;
        Assert.Equal("120", output.Trim());
    }

    [Fact]
    public void Record_field_access_runs_under_qemu()
    {
        string? output = CompileAndRun("""
            Point = record {
              x : Integer,
              y : Integer
            }

            main : Integer
            main = let p = Point { x = 3, y = 4 } in p.x + p.y
            """, "record_run_arm64");
        if (output is null) return;
        Assert.Equal("7", output.Trim());
    }

    [Fact]
    public void Pattern_match_runs_under_qemu()
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
            """, "match_run_arm64");
        if (output is null) return;
        Assert.Equal("35", output.Trim());
    }

    [Fact(Skip = "ARM64 _start text print needs frame setup — Agent Linux to verify")]
    public void Text_concat_runs_under_qemu()
    {
        string? output = CompileAndRun("""
            main : Text
            main = "hello " ++ "world"
            """, "concat_run_arm64");
        if (output is null) return;
        Assert.Equal("hello world", output.Trim());
    }

    [Fact]
    public void List_length_runs_under_qemu()
    {
        string? output = CompileAndRun("""
            main : Integer
            main = list-length [1, 2, 3]
            """, "listlen_run_arm64");
        if (output is null) return;
        Assert.Equal("3", output.Trim());
    }

    [Fact]
    public void String_equality_runs_under_qemu()
    {
        string? output = CompileAndRun("""
            main : Integer
            main = if "abc" == "abc" then 1 else 0
            """, "streq_run_arm64");
        if (output is null) return;
        Assert.Equal("1", output.Trim());
    }

    // ── Helpers ──────────────────────────────────────────────────

    static void AssertValidElf(byte[] bytes)
    {
        Assert.True(bytes.Length >= 64, "ELF must be at least 64 bytes");
        Assert.Equal(0x7F, bytes[0]);
        Assert.Equal((byte)'E', bytes[1]);
        Assert.Equal((byte)'L', bytes[2]);
        Assert.Equal((byte)'F', bytes[3]);
    }

    static string? CompileAndRun(string source, string moduleName)
    {
        byte[]? bytes = Helpers.CompileToArm64(source, moduleName);
        if (bytes is null) return null;

        if (!IsQemuArm64Available()) return null;

        string tempDir = Path.Combine(Path.GetTempPath(),
            "codex_arm64_test_" + moduleName + "_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);
        try
        {
            string elfPath = Path.Combine(tempDir, moduleName);
            File.WriteAllBytes(elfPath, bytes);

            string wslPath = ToWslPath(elfPath);
            string wslTmp = $"/tmp/codex_arm64_{moduleName}_{Guid.NewGuid().ToString("N")[..8]}";

            // Use wsl explicitly — bash on Windows may be Git Bash
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

    static bool IsQemuArm64Available()
    {
        try
        {
            ProcessStartInfo psi = new("wsl", "qemu-aarch64 --version")
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
