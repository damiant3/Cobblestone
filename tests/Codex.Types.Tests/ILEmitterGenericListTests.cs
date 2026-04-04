using System.Diagnostics;
using Xunit;

namespace Codex.Types.Tests;

public class ILEmitterGenericListTests
{
    // ── List<Integer> ───────────────────────────────────────────

    [Fact]
    public void List_integer_literal_emits_il()
    {
        string source = """
            nums : List Integer
            nums = [10, 20, 30]

            main : Integer
            main = list-length nums
            """;
        byte[]? bytes = Helpers.CompileToIL(source, "list_int_emit");
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);
    }

    [Fact]
    public void List_integer_length_runs_correctly()
    {
        string source = """
            nums : List Integer
            nums = [10, 20, 30]

            main : Integer
            main = list-length nums
            """;
        string? output = CompileAndRun(source, "list_int_len");
        Assert.NotNull(output);
        Assert.Equal("3", output.Trim());
    }

    [Fact]
    public void List_integer_at_runs_correctly()
    {
        string source = """
            nums : List Integer
            nums = [10, 20, 30]

            main : Integer
            main = list-at nums 1
            """;
        string? output = CompileAndRun(source, "list_int_at");
        Assert.NotNull(output);
        Assert.Equal("20", output.Trim());
    }

    [Fact]
    public void List_integer_at_used_in_arithmetic()
    {
        string source = """
            nums : List Integer
            nums = [10, 20, 30]

            main : Integer
            main = list-at nums 0 + list-at nums 1 + list-at nums 2
            """;
        string? output = CompileAndRun(source, "list_int_arith");
        Assert.NotNull(output);
        Assert.Equal("60", output.Trim());
    }

    // ── List<Number> ────────────────────────────────────────────

    [Fact]
    public void List_number_at_runs_correctly()
    {
        string source = """
            vals : List Number
            vals = [3.14, 2.72, 1.41]

            main : Text
            main = show (list-at vals 0)
            """;
        string? output = CompileAndRun(source, "list_num_at");
        Assert.NotNull(output);
        Assert.Equal("3.14", output.Trim());
    }

    // ── List<Boolean> ───────────────────────────────────────────

    [Fact]
    public void List_boolean_length_runs_correctly()
    {
        string source = """
            flags : List Boolean
            flags = [True, False, True]

            main : Integer
            main = list-length flags
            """;
        string? output = CompileAndRun(source, "list_bool_len");
        Assert.NotNull(output);
        Assert.Equal("3", output.Trim());
    }

    [Fact]
    public void List_boolean_at_runs_correctly()
    {
        string source = """
            flags : List Boolean
            flags = [True, False, True]

            main : Text
            main = show (list-at flags 2)
            """;
        string? output = CompileAndRun(source, "list_bool_at");
        Assert.NotNull(output);
        Assert.Equal("True", output.Trim());
    }

    // ── List<Text> regression ───────────────────────────────────

    [Fact]
    public void List_text_still_works_through_cache()
    {
        string source = """
            words : List Text
            words = ["hello", "world"]

            main : Text
            main = list-at words 0 ++ " " ++ list-at words 1
            """;
        string? output = CompileAndRun(source, "list_text_regress");
        Assert.NotNull(output);
        Assert.Equal("hello world", output.Trim());
    }

    [Fact]
    public void Text_split_still_works_through_cache()
    {
        string source = """
            main : Text
            main = list-at (text-split "a,b,c" ",") 1
            """;
        string? output = CompileAndRun(source, "text_split_regress");
        Assert.NotNull(output);
        Assert.Equal("b", output.Trim());
    }

    // ── Empty list ──────────────────────────────────────────────

    [Fact]
    public void Empty_list_length_is_zero()
    {
        string source = """
            empty : List Integer
            empty = []

            main : Integer
            main = list-length empty
            """;
        string? output = CompileAndRun(source, "empty_list_len");
        Assert.NotNull(output);
        Assert.Equal("0", output.Trim());
    }

    // ── Single element ──────────────────────────────────────────

    [Fact]
    public void Single_element_list_works()
    {
        string source = """
            one : List Integer
            one = [42]

            main : Integer
            main = list-at one 0
            """;
        string? output = CompileAndRun(source, "single_elem");
        Assert.NotNull(output);
        Assert.Equal("42", output.Trim());
    }

    // ── Helpers ─────────────────────────────────────────────────

    static string? CompileAndRun(string source, string chapterName)
    {
        byte[]? bytes = Helpers.CompileToIL(source, chapterName);
        if (bytes is null) return null;

        string tempDir = Path.Combine(Path.GetTempPath(),
            "codex_il_test_" + chapterName + "_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);
        try
        {
            string exePath = Path.Combine(tempDir, chapterName + ".dll");
            File.WriteAllBytes(exePath, bytes);

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
            if (proc is null) return null;

            string stdout = proc.StandardOutput.ReadToEnd();
            string stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit(10_000);

            if (proc.ExitCode != 0)
                throw new InvalidOperationException(
                    $"dotnet exited with code {proc.ExitCode}.\nstdout: {stdout}\nstderr: {stderr}");

            return stdout;
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }
}
