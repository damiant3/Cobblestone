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

    // ── Compile-and-run helpers ──────────────────────────────────

    static string? CompileAndRunX86_64(string source, string moduleName)
    {
        byte[]? bytes = Helpers.CompileToX86_64(source, moduleName);
        if (bytes is null) return null;
        return RunElf(bytes, moduleName, null);
    }

    static string? CompileAndRunArm64(string source, string moduleName)
    {
        byte[]? bytes = Helpers.CompileToArm64(source, moduleName);
        if (bytes is null) return null;
        return RunElf(bytes, moduleName, "qemu-aarch64");
    }

    static string? CompileAndRunRiscV(string source, string moduleName)
    {
        byte[]? bytes = Helpers.CompileToRiscV(source, moduleName);
        if (bytes is null) return null;
        return RunElf(bytes, moduleName, "qemu-riscv64");
    }

    /// <summary>
    /// Write ELF to /tmp, chmod +x, run (directly or via qemu-user), return stdout.
    /// </summary>
    static string? RunElf(byte[] elfBytes, string moduleName, string? qemuBinary)
    {
        string tempDir = Path.Combine(Path.GetTempPath(),
            $"codex_linux_{moduleName}_{Guid.NewGuid().ToString("N")[..8]}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string elfPath = Path.Combine(tempDir, moduleName);
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
}
