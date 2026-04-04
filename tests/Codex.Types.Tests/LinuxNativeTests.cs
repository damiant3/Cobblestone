using System.Diagnostics;
using System.Runtime.InteropServices;
using Xunit;
using Xunit.Abstractions;

namespace Codex.Types.Tests;

/// <summary>
/// Tests that run compiled native ELF binaries on Linux directly.
/// x86-64 runs natively, ARM64 and RISC-V run via qemu-user.
/// These tests return early (with output) on non-Linux platforms.
/// </summary>
public class LinuxNativeTests
{
    private readonly ITestOutputHelper m_output;

    public LinuxNativeTests(ITestOutputHelper output)
    {
        m_output = output;
    }

    // ── Fork/Await Tests ─────────────────────────────────────────

    const string ForkAwaitSource = """
        compute : Nothing -> Integer
        compute (x) = 42

        do-fork : [Concurrent] Integer
        do-fork = let t = fork compute in await t

        main : Integer
        main = do-fork
        """;

    [Fact]
    public void Fork_await_runs_x86_64_on_linux()
    {
        if (!IsLinuxX64()) { m_output.WriteLine("SKIP: not Linux x86-64"); return; }

        string? output = CompileAndRunX86_64(ForkAwaitSource, "fork_x64_linux");
        Assert.NotNull(output);
        m_output.WriteLine($"x86-64 fork/await output: [{output.Trim()}]");
        Assert.Equal("42", output.Trim());
    }

    [Fact]
    public void Fork_await_runs_arm64_under_qemu()
    {
        if (!IsLinux()) { m_output.WriteLine("SKIP: not Linux"); return; }
        if (!HasQemu("qemu-aarch64")) { m_output.WriteLine("SKIP: no qemu-aarch64"); return; }

        string? output = CompileAndRunArm64(ForkAwaitSource, "fork_a64_linux");
        Assert.NotNull(output);
        m_output.WriteLine($"ARM64 fork/await output: [{output.Trim()}]");
        Assert.Equal("42", output.Trim());
    }

    [Fact]
    public void Fork_await_runs_riscv_under_qemu()
    {
        if (!IsLinux()) { m_output.WriteLine("SKIP: not Linux"); return; }
        if (!HasQemu("qemu-riscv64")) { m_output.WriteLine("SKIP: no qemu-riscv64"); return; }

        string? output = CompileAndRunRiscV(ForkAwaitSource, "fork_rv_linux");
        Assert.NotNull(output);
        m_output.WriteLine($"RISC-V fork/await output: [{output.Trim()}]");
        Assert.Equal("42", output.Trim());
    }

    // ── Baseline sanity: simple integer (no fork) ────────────────

    const string SimpleSource = """
        main : Integer
        main = 42
        """;

    [Fact]
    public void Simple_integer_runs_x86_64_on_linux()
    {
        if (!IsLinuxX64()) { m_output.WriteLine("SKIP: not Linux x86-64"); return; }

        string? output = CompileAndRunX86_64(SimpleSource, "simple_x64_linux");
        Assert.NotNull(output);
        m_output.WriteLine($"x86-64 simple output: [{output.Trim()}]");
        Assert.Equal("42", output.Trim());
    }

    [Fact]
    public void Simple_integer_runs_arm64_under_qemu()
    {
        if (!IsLinux()) { m_output.WriteLine("SKIP: not Linux"); return; }
        if (!HasQemu("qemu-aarch64")) { m_output.WriteLine("SKIP: no qemu-aarch64"); return; }

        string? output = CompileAndRunArm64(SimpleSource, "simple_a64_linux");
        Assert.NotNull(output);
        m_output.WriteLine($"ARM64 simple output: [{output.Trim()}]");
        Assert.Equal("42", output.Trim());
    }

    [Fact]
    public void Simple_integer_runs_riscv_under_qemu()
    {
        if (!IsLinux()) { m_output.WriteLine("SKIP: not Linux"); return; }
        if (!HasQemu("qemu-riscv64")) { m_output.WriteLine("SKIP: no qemu-riscv64"); return; }

        string? output = CompileAndRunRiscV(SimpleSource, "simple_rv_linux");
        Assert.NotNull(output);
        m_output.WriteLine($"RISC-V simple output: [{output.Trim()}]");
        Assert.Equal("42", output.Trim());
    }

    // ── Factorial + fork (exercises recursion in forked thunk) ───

    const string FactorialForkSource = """
        factorial : Integer -> Integer
        factorial (n) = if n <= 1 then 1 else n * factorial (n - 1)

        compute-fact : Nothing -> Integer
        compute-fact (x) = factorial 10

        do-fork : [Concurrent] Integer
        do-fork = let t = fork compute-fact in await t

        main : Integer
        main = do-fork
        """;

    [Fact]
    public void Factorial_fork_runs_x86_64_on_linux()
    {
        if (!IsLinuxX64()) { m_output.WriteLine("SKIP: not Linux x86-64"); return; }

        string? output = CompileAndRunX86_64(FactorialForkSource, "factfork_x64_linux");
        Assert.NotNull(output);
        m_output.WriteLine($"x86-64 factorial fork output: [{output.Trim()}]");
        Assert.Equal("3628800", output.Trim());
    }

    [Fact]
    public void Factorial_fork_runs_arm64_under_qemu()
    {
        if (!IsLinux()) { m_output.WriteLine("SKIP: not Linux"); return; }
        if (!HasQemu("qemu-aarch64")) { m_output.WriteLine("SKIP: no qemu-aarch64"); return; }

        string? output = CompileAndRunArm64(FactorialForkSource, "factfork_a64_linux");
        Assert.NotNull(output);
        m_output.WriteLine($"ARM64 factorial fork output: [{output.Trim()}]");
        Assert.Equal("3628800", output.Trim());
    }

    [Fact]
    public void Factorial_fork_runs_riscv_under_qemu()
    {
        if (!IsLinux()) { m_output.WriteLine("SKIP: not Linux"); return; }
        if (!HasQemu("qemu-riscv64")) { m_output.WriteLine("SKIP: no qemu-riscv64"); return; }

        string? output = CompileAndRunRiscV(FactorialForkSource, "factfork_rv_linux");
        Assert.NotNull(output);
        m_output.WriteLine($"RISC-V factorial fork output: [{output.Trim()}]");
        Assert.Equal("3628800", output.Trim());
    }

    // ── TCO (Tail Call Optimization) Tests ─────────────────────────

    // Small TCO: sum 1..10 = 55 (verifies TCO works at all)
    const string TCOSmallSource = """
        sum-to : Integer -> Integer -> Integer
        sum-to (n) (acc) =
          if n == 0
            then acc
            else sum-to (n - 1) (acc + n)

        main : Integer
        main = sum-to 10 0
        """;

    // Large TCO: sum 1..100000 = 5000050000 (64MB heap accommodates bump allocator)
    const string TCOSource = """
        sum-to : Integer -> Integer -> Integer
        sum-to (n) (acc) =
          if n == 0
            then acc
            else sum-to (n - 1) (acc + n)

        main : Integer
        main = sum-to 100000 0
        """;

    [Fact]
    public void TCO_sum_to_10_runs_x86_64()
    {
        if (!IsLinuxX64()) { m_output.WriteLine("SKIP: not Linux x86-64"); return; }
        string? output = CompileAndRunX86_64(TCOSmallSource, "tco_sm_x64");
        Assert.NotNull(output);
        m_output.WriteLine($"x86-64 TCO small: [{output.Trim()}]");
        Assert.Equal("55", output.Trim());
    }

    [Fact]
    public void TCO_sum_to_1000_runs_x86_64()
    {
        if (!IsLinuxX64()) { m_output.WriteLine("SKIP: not Linux x86-64"); return; }
        string source = """
            sum-to : Integer -> Integer -> Integer
            sum-to (n) (acc) =
              if n == 0
                then acc
                else sum-to (n - 1) (acc + n)

            main : Integer
            main = sum-to 1000 0
            """;
        string? output = CompileAndRunX86_64(source, "tco_1k_x64");
        Assert.NotNull(output);
        m_output.WriteLine($"x86-64 TCO 1k: [{output.Trim()}]");
        Assert.Equal("500500", output.Trim());
    }

    [Fact]
    public void TCO_sum_to_100k_runs_x86_64()
    {
        if (!IsLinuxX64()) { m_output.WriteLine("SKIP: not Linux x86-64"); return; }
        string? output = CompileAndRunX86_64(TCOSource, "tco_x64");
        Assert.NotNull(output);
        m_output.WriteLine($"x86-64 TCO output: [{output.Trim()}]");
        Assert.Equal("5000050000", output.Trim());
    }

    [Fact]
    public void TCO_sum_to_100k_runs_arm64()
    {
        if (!IsLinux()) { m_output.WriteLine("SKIP: not Linux"); return; }
        if (!HasQemu("qemu-aarch64")) { m_output.WriteLine("SKIP: no qemu-aarch64"); return; }
        string? output = CompileAndRunArm64(TCOSource, "tco_a64");
        Assert.NotNull(output);
        m_output.WriteLine($"ARM64 TCO output: [{output.Trim()}]");
        Assert.Equal("5000050000", output.Trim());
    }

    [Fact]
    public void TCO_sum_to_100k_runs_riscv()
    {
        if (!IsLinux()) { m_output.WriteLine("SKIP: not Linux"); return; }
        if (!HasQemu("qemu-riscv64")) { m_output.WriteLine("SKIP: no qemu-riscv64"); return; }
        string? output = CompileAndRunRiscV(TCOSource, "tco_rv");
        Assert.NotNull(output);
        m_output.WriteLine($"RISC-V TCO output: [{output.Trim()}]");
        Assert.Equal("5000050000", output.Trim());
    }

    // ── is-digit Tests ──────────────────────────────────────────

    // Positive case: '5' is a digit
    const string IsDigitPositiveSource = """
        main : Integer
        main = if is-digit (char-at "5" 0) then 1 else 0
        """;

    // Negative case: space is NOT a digit (this was the signed comparison bug)
    const string IsDigitNegativeSource = """
        main : Integer
        main = if is-digit (char-at " " 0) then 1 else 0
        """;

    [Fact]
    public void IsDigit_positive_runs_x86_64()
    {
        if (!IsLinuxX64()) { m_output.WriteLine("SKIP: not Linux x86-64"); return; }
        string? output = CompileAndRunX86_64(IsDigitPositiveSource, "isdigit_pos_x64");
        Assert.NotNull(output);
        Assert.Equal("1", output.Trim());
    }

    [Fact]
    public void IsDigit_space_rejected_x86_64()
    {
        if (!IsLinuxX64()) { m_output.WriteLine("SKIP: not Linux x86-64"); return; }
        string? output = CompileAndRunX86_64(IsDigitNegativeSource, "isdigit_neg_x64");
        Assert.NotNull(output);
        m_output.WriteLine($"x86-64 is-digit space: [{output.Trim()}]");
        Assert.Equal("0", output.Trim());
    }

    [Fact]
    public void IsDigit_positive_runs_arm64()
    {
        if (!IsLinux()) { m_output.WriteLine("SKIP: not Linux"); return; }
        if (!HasQemu("qemu-aarch64")) { m_output.WriteLine("SKIP: no qemu-aarch64"); return; }
        string? output = CompileAndRunArm64(IsDigitPositiveSource, "isdigit_pos_a64");
        Assert.NotNull(output);
        Assert.Equal("1", output.Trim());
    }

    [Fact]
    public void IsDigit_space_rejected_arm64()
    {
        if (!IsLinux()) { m_output.WriteLine("SKIP: not Linux"); return; }
        if (!HasQemu("qemu-aarch64")) { m_output.WriteLine("SKIP: no qemu-aarch64"); return; }
        string? output = CompileAndRunArm64(IsDigitNegativeSource, "isdigit_neg_a64");
        Assert.NotNull(output);
        Assert.Equal("0", output.Trim());
    }

    [Fact]
    public void IsDigit_positive_runs_riscv()
    {
        if (!IsLinux()) { m_output.WriteLine("SKIP: not Linux"); return; }
        if (!HasQemu("qemu-riscv64")) { m_output.WriteLine("SKIP: no qemu-riscv64"); return; }
        string? output = CompileAndRunRiscV(IsDigitPositiveSource, "isdigit_pos_rv");
        Assert.NotNull(output);
        Assert.Equal("1", output.Trim());
    }

    [Fact]
    public void IsDigit_space_rejected_riscv()
    {
        if (!IsLinux()) { m_output.WriteLine("SKIP: not Linux"); return; }
        if (!HasQemu("qemu-riscv64")) { m_output.WriteLine("SKIP: no qemu-riscv64"); return; }
        string? output = CompileAndRunRiscV(IsDigitNegativeSource, "isdigit_neg_rv");
        Assert.NotNull(output);
        Assert.Equal("0", output.Trim());
    }

    // ── is-whitespace Tests ─────────────────────────────────────

    const string IsWhitespacePositiveSource = """
        main : Integer
        main = if is-whitespace (char-at " " 0) then 1 else 0
        """;

    const string IsWhitespaceNegativeSource = """
        main : Integer
        main = if is-whitespace (char-at "a" 0) then 1 else 0
        """;

    [Fact]
    public void IsWhitespace_space_detected_x86_64()
    {
        if (!IsLinuxX64()) { m_output.WriteLine("SKIP: not Linux x86-64"); return; }
        string? output = CompileAndRunX86_64(IsWhitespacePositiveSource, "isws_pos_x64");
        Assert.NotNull(output);
        Assert.Equal("1", output.Trim());
    }

    [Fact]
    public void IsWhitespace_letter_rejected_x86_64()
    {
        if (!IsLinuxX64()) { m_output.WriteLine("SKIP: not Linux x86-64"); return; }
        string? output = CompileAndRunX86_64(IsWhitespaceNegativeSource, "isws_neg_x64");
        Assert.NotNull(output);
        Assert.Equal("0", output.Trim());
    }

    [Fact]
    public void IsWhitespace_space_detected_arm64()
    {
        if (!IsLinux()) { m_output.WriteLine("SKIP: not Linux"); return; }
        if (!HasQemu("qemu-aarch64")) { m_output.WriteLine("SKIP: no qemu-aarch64"); return; }
        string? output = CompileAndRunArm64(IsWhitespacePositiveSource, "isws_pos_a64");
        Assert.NotNull(output);
        Assert.Equal("1", output.Trim());
    }

    [Fact]
    public void IsWhitespace_letter_rejected_arm64()
    {
        if (!IsLinux()) { m_output.WriteLine("SKIP: not Linux"); return; }
        if (!HasQemu("qemu-aarch64")) { m_output.WriteLine("SKIP: no qemu-aarch64"); return; }
        string? output = CompileAndRunArm64(IsWhitespaceNegativeSource, "isws_neg_a64");
        Assert.NotNull(output);
        Assert.Equal("0", output.Trim());
    }

    [Fact]
    public void IsWhitespace_space_detected_riscv()
    {
        if (!IsLinux()) { m_output.WriteLine("SKIP: not Linux"); return; }
        if (!HasQemu("qemu-riscv64")) { m_output.WriteLine("SKIP: no qemu-riscv64"); return; }
        string? output = CompileAndRunRiscV(IsWhitespacePositiveSource, "isws_pos_rv");
        Assert.NotNull(output);
        Assert.Equal("1", output.Trim());
    }

    [Fact]
    public void IsWhitespace_letter_rejected_riscv()
    {
        if (!IsLinux()) { m_output.WriteLine("SKIP: not Linux"); return; }
        if (!HasQemu("qemu-riscv64")) { m_output.WriteLine("SKIP: no qemu-riscv64"); return; }
        string? output = CompileAndRunRiscV(IsWhitespaceNegativeSource, "isws_neg_rv");
        Assert.NotNull(output);
        Assert.Equal("0", output.Trim());
    }

    // ── negate Tests ────────────────────────────────────────────

    const string NegateSource = """
        main : Integer
        main = negate 42
        """;

    [Fact]
    public void Negate_runs_x86_64()
    {
        if (!IsLinuxX64()) { m_output.WriteLine("SKIP: not Linux x86-64"); return; }
        string? output = CompileAndRunX86_64(NegateSource, "negate_x64");
        Assert.NotNull(output);
        Assert.Equal("-42", output.Trim());
    }

    [Fact]
    public void Negate_runs_arm64()
    {
        if (!IsLinux()) { m_output.WriteLine("SKIP: not Linux"); return; }
        if (!HasQemu("qemu-aarch64")) { m_output.WriteLine("SKIP: no qemu-aarch64"); return; }
        string? output = CompileAndRunArm64(NegateSource, "negate_a64");
        Assert.NotNull(output);
        Assert.Equal("-42", output.Trim());
    }

    [Fact]
    public void Negate_runs_riscv()
    {
        if (!IsLinux()) { m_output.WriteLine("SKIP: not Linux"); return; }
        if (!HasQemu("qemu-riscv64")) { m_output.WriteLine("SKIP: no qemu-riscv64"); return; }
        string? output = CompileAndRunRiscV(NegateSource, "negate_rv");
        Assert.NotNull(output);
        Assert.Equal("-42", output.Trim());
    }

    // ── Compile-and-run helpers ──────────────────────────────────

    // ── Bare Metal x86-64 Boot Tests (qemu-system) ──────────────

    [Fact]
    public void BareMetal_integer_42_boots_under_qemu_system()
    {
        if (!IsLinux()) { m_output.WriteLine("SKIP: not Linux"); return; }
        if (!HasQemuSystem()) { m_output.WriteLine("SKIP: no qemu-system-x86_64"); return; }

        string? output = CompileAndBootBareMetal(SimpleSource, "bm_simple_x64");
        Assert.NotNull(output);
        m_output.WriteLine($"Bare metal boot output: [{output.Trim()}]");
        Assert.Equal("42", output.Trim());
    }

    [Fact]
    public void BareMetal_factorial_boots_under_qemu_system()
    {
        if (!IsLinux()) { m_output.WriteLine("SKIP: not Linux"); return; }
        if (!HasQemuSystem()) { m_output.WriteLine("SKIP: no qemu-system-x86_64"); return; }

        string source = """
            factorial : Integer -> Integer
            factorial (n) = if n <= 1 then 1 else n * factorial (n - 1)

            main : Integer
            main = factorial 10
            """;
        string? output = CompileAndBootBareMetal(source, "bm_fact_x64");
        Assert.NotNull(output);
        m_output.WriteLine($"Bare metal factorial output: [{output.Trim()}]");
        Assert.Equal("3628800", output.Trim());
    }

    // ── User-mode compile-and-run helpers ─────────────────────────

    static string? CompileAndRunX86_64(string source, string chapterName)
    {
        byte[]? bytes = Helpers.CompileToX86_64(source, chapterName);
        if (bytes is null) return null;
        return RunElf(bytes, chapterName, null);
    }

    static string? CompileAndRunArm64(string source, string chapterName)
    {
        byte[]? bytes = Helpers.CompileToArm64(source, chapterName);
        if (bytes is null) return null;
        return RunElf(bytes, chapterName, "qemu-aarch64");
    }

    static string? CompileAndRunRiscV(string source, string chapterName)
    {
        byte[]? bytes = Helpers.CompileToRiscV(source, chapterName);
        if (bytes is null) return null;
        return RunElf(bytes, chapterName, "qemu-riscv64");
    }

    // ── Bare metal boot helper ───────────────────────────────────

    static string? CompileAndBootBareMetal(string source, string chapterName)
    {
        byte[]? bytes = Helpers.CompileToX86_64BareMetal(source, chapterName);
        if (bytes is null) return null;

        string tempDir = Path.Combine(Path.GetTempPath(),
            $"codex_bm_{chapterName}_{Guid.NewGuid().ToString("N")[..8]}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string elfPath = Path.Combine(tempDir, chapterName + ".elf");
            File.WriteAllBytes(elfPath, bytes);

            // Boot under qemu-system-x86_64, capture serial output.
            // QEMU doesn't exit after kernel halt, so wrap with timeout.
            ProcessStartInfo psi = new("timeout",
                $"5 qemu-system-x86_64 -kernel {elfPath} -nographic -no-reboot")
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

            // Extract kernel output from after "Booting from ROM.."
            // The bare metal REPL loop prints output repeatedly, so take
            // only the first line — that's the first compilation's result.
            int marker = stdout.IndexOf("Booting from ROM..");
            if (marker >= 0)
            {
                string afterBoot = stdout[(marker + "Booting from ROM..".Length)..].Trim();
                int newline = afterBoot.IndexOf('\n');
                return newline >= 0 ? afterBoot[..newline].Trim() : afterBoot;
            }

            return stdout;
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    /// <summary>
    /// Write ELF to /tmp, chmod +x, run (directly or via qemu-user), return stdout.
    /// </summary>
    static string? RunElf(byte[] elfBytes, string chapterName, string? qemuBinary)
    {
        string tempDir = Path.Combine(Path.GetTempPath(),
            $"codex_linux_{chapterName}_{Guid.NewGuid().ToString("N")[..8]}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string elfPath = Path.Combine(tempDir, chapterName);
            File.WriteAllBytes(elfPath, elfBytes);

            // Use chmod via process to avoid CA1416 (platform compat analyzer)
            using (Process? chmod = Process.Start("chmod", $"+x {elfPath}"))
            {
                chmod?.WaitForExit(5_000);
            }

            string fileName;
            string arguments;
            if (qemuBinary is null)
            {
                // x86-64 on x86-64 Linux: run directly
                fileName = elfPath;
                arguments = "";
            }
            else
            {
                // Cross-arch: run via qemu-user
                fileName = qemuBinary;
                arguments = elfPath;
            }

            ProcessStartInfo psi = new(fileName, arguments)
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
            proc.WaitForExit(15_000);

            if (!proc.HasExited)
            {
                proc.Kill();
                throw new TimeoutException(
                    $"Process timed out after 15s.\nstdout: {stdout}\nstderr: {stderr}");
            }

            if (proc.ExitCode != 0)
                throw new InvalidOperationException(
                    $"{fileName} exited with code {proc.ExitCode}.\nstdout: {stdout}\nstderr: {stderr}");

            return stdout;
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    // ── Environment detection ────────────────────────────────────

    static bool IsLinux() => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

    static bool IsLinuxX64() =>
        IsLinux() && RuntimeInformation.OSArchitecture == Architecture.X64;

    static bool HasQemu(string binary)
    {
        try
        {
            ProcessStartInfo psi = new(binary, "--version")
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

    static bool HasQemuSystem() => HasQemu("qemu-system-x86_64");
}
