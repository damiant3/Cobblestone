using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Xunit;

namespace Codex.Types.Tests;

public class ILEmitterGenericsTcoTests
{
    // ── TCO: basic tail-recursive function ─────────────────────

    [Fact]
    public void Tco_sum_to_emits_il_bytes()
    {
        string source = """
            sum-to : Integer -> Integer -> Integer
            sum-to (n) (acc) =
              if n == 0
                then acc
                else sum-to (n - 1) (acc + n)

            main : Integer
            main = sum-to 100 0
            """;
        byte[]? bytes = Helpers.CompileToIL(source, "tco_sum_emit");
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);
    }

    [Fact]
    public void Tco_sum_to_runs_correctly()
    {
        string source = """
            sum-to : Integer -> Integer -> Integer
            sum-to (n) (acc) =
              if n == 0
                then acc
                else sum-to (n - 1) (acc + n)

            main : Integer
            main = sum-to 100 0
            """;
        string? output = CompileAndRun(source, "tco_sum_run");
        Assert.NotNull(output);
        Assert.Equal("5050", output.Trim());
    }

    [Fact]
    public void Tco_countdown_runs_correctly()
    {
        string source = """
            countdown : Integer -> Integer
            countdown (n) =
              if n == 0
                then 0
                else countdown (n - 1)

            main : Integer
            main = countdown 10000
            """;
        string? output = CompileAndRun(source, "tco_countdown_run");
        Assert.NotNull(output);
        Assert.Equal("0", output.Trim());
    }

    [Fact]
    public void Tco_stress_sample_emits_il()
    {
        string source = ReadSample("tco-stress.codex");
        byte[]? bytes = Helpers.CompileToIL(source, "tco_stress_emit");
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);
    }

    [Fact]
    public void Tco_stress_sample_runs_correctly()
    {
        string source = ReadSample("tco-stress.codex");
        string? output = CompileAndRun(source, "tco_stress_run");
        Assert.NotNull(output);
        // sum-to 1000000 0 = 500000500000
        Assert.Equal("500000500000", output.Trim());
    }

    [Fact]
    public void Tco_with_match_runs_correctly()
    {
        string source = """
            Shape =
              | Circle (Integer)
              | Rect (Integer) (Integer)

            count-circles : Shape -> Integer -> Integer
            count-circles (s) (acc) = when s
              is Circle (r) -> acc + 1
              is Rect (w) (h) -> acc

            main : Integer
            main = count-circles (Circle 5) 0
            """;
        string? output = CompileAndRun(source, "tco_match_run");
        Assert.NotNull(output);
        Assert.Equal("1", output.Trim());
    }

    // ── Generics: functions with type variables ────────────────

    [Fact]
    public void Generic_identity_emits_il_bytes()
    {
        string source = """
            id (x) = x

            main : Integer
            main = id 42
            """;
        byte[]? bytes = Helpers.CompileToIL(source, "generic_id_emit");
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);
    }

    [Fact]
    public void Generic_identity_monomorphized_runs_correctly()
    {
        // When id is used at a single concrete type, the type checker
        // monomorphizes it via unification — no ForAllType survives.
        // Verify the emitter handles this cleanly.
        string source = """
            id (x) = x

            main : Integer
            main = id 42
            """;
        string? output = CompileAndRun(source, "generic_id_run");
        Assert.NotNull(output);
        Assert.Equal("42", output.Trim());
    }

    [Fact]
    public void Generic_const_emits_il_bytes()
    {
        string source = """
            const (x) (y) = x

            main : Integer
            main = const 7 42
            """;
        byte[]? bytes = Helpers.CompileToIL(source, "generic_const_emit");
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);
    }

    [Fact]
    public void Generic_sum_type_emits_il()
    {
        string source = """
            Result (a) =
              | Ok (a)
              | Err (Text)

            get-or-default (r) (d) = when r
              is Ok (v) -> v
              is Err (msg) -> d

            main : Integer
            main = get-or-default (Ok 42) 0
            """;
        byte[]? bytes = Helpers.CompileToIL(source, "generic_sum_emit");
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);
    }

    // ── Non-TCO recursive functions still work ─────────────────

    [Fact]
    public void Non_tco_factorial_still_works()
    {
        string source = """
            factorial : Integer -> Integer
            factorial (n) = if n == 0 then 1 else n * factorial (n - 1)

            main : Integer
            main = factorial 10
            """;
        string? output = CompileAndRun(source, "non_tco_factorial");
        Assert.NotNull(output);
        Assert.Equal("3628800", output.Trim());
    }

    [Fact]
    public void Fibonacci_still_works()
    {
        string source = ReadSample("fibonacci.codex");
        string? output = CompileAndRun(source, "fib_still_works");
        Assert.NotNull(output);
        Assert.Equal("6765", output.Trim());
    }

    // ── Helpers ────────────────────────────────────────────────

    static string ReadSample(string name)
    {
        string path = Path.Combine(FindSamplesDir(), name);
        return File.ReadAllText(path);
    }

    static string FindSamplesDir()
    {
        string dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            string candidate = Path.Combine(dir, "samples");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = Path.GetDirectoryName(dir)!;
        }
        throw new DirectoryNotFoundException("Cannot find samples/ directory");
    }

    static string? CompileAndRun(string source, string chapterName)
    {
        byte[]? bytes = Helpers.CompileToIL(source, chapterName);
        if (bytes is null)
        {
            return null;
        }

        string tempDir = Path.Combine(Path.GetTempPath(),
            "codex_il_test_" + chapterName + "_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);
        try
        {
            string exePath = Path.Combine(tempDir, chapterName + ".dll");
            File.WriteAllBytes(exePath, bytes);
            Helpers.CopyIlRuntimeDeps(tempDir);

            string runtimeConfigPath = Path.Combine(tempDir, chapterName + ".runtimeconfig.json");
            File.WriteAllText(runtimeConfigPath, """
                {
                  "runtimeOptions": {
                    "tfm": "net8.0",
                    "framework": {
                      "name": "Microsoft.NETCore.App",
                      "version": "8.0.0"
                    }
                  }
                }
                """);

            ProcessStartInfo psi = new("dotnet", exePath)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = tempDir
            };

            using Process? proc = Process.Start(psi);
            if (proc is null)
            {
                return null;
            }

            string stdout = proc.StandardOutput.ReadToEnd();
            string stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit(10_000);

            if (proc.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"dotnet exited with code {proc.ExitCode}.\nstdout: {stdout}\nstderr: {stderr}");
            }

            return stdout;
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }
}
