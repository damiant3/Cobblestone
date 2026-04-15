using System.Diagnostics;
using Xunit;

namespace Codex.Types.Tests;

public class ILEmitterBuiltinTests
{
    // ── text-contains ───────────────────────────────────────────

    [Fact]
    public void Text_contains_true_case()
    {
        string source = """
            main : Text
            main = show (text-contains "hello world" "world")
            """;
        string? output = CompileAndRun(source, "contains_true");
        Assert.NotNull(output);
        Assert.Equal("True", output.Trim());
    }

    [Fact]
    public void Text_contains_false_case()
    {
        string source = """
            main : Text
            main = show (text-contains "hello world" "xyz")
            """;
        string? output = CompileAndRun(source, "contains_false");
        Assert.NotNull(output);
        Assert.Equal("False", output.Trim());
    }

    [Fact]
    public void Text_contains_empty_substring()
    {
        string source = """
            main : Text
            main = show (text-contains "hello" "")
            """;
        string? output = CompileAndRun(source, "contains_empty");
        Assert.NotNull(output);
        Assert.Equal("True", output.Trim());
    }

    // ── text-starts-with ────────────────────────────────────────

    [Fact]
    public void Text_starts_with_true_case()
    {
        string source = """
            main : Text
            main = show (text-starts-with "hello world" "hello")
            """;
        string? output = CompileAndRun(source, "starts_true");
        Assert.NotNull(output);
        Assert.Equal("True", output.Trim());
    }

    [Fact]
    public void Text_starts_with_false_case()
    {
        string source = """
            main : Text
            main = show (text-starts-with "hello world" "world")
            """;
        string? output = CompileAndRun(source, "starts_false");
        Assert.NotNull(output);
        Assert.Equal("False", output.Trim());
    }

    // ── text-contains used in control flow ──────────────────────

    [Fact]
    public void Text_contains_in_if_expression()
    {
        string source = """
            has-at : Text -> Text
            has-at (s) = if text-contains s "@" then "email" else "not email"

            main : Text
            main = has-at "user@example.com"
            """;
        string? output = CompileAndRun(source, "contains_if");
        Assert.NotNull(output);
        Assert.Equal("email", output.Trim());
    }

    // ── get-env ─────────────────────────────────────────────────

    [Fact]
    public void Get_env_returns_value()
    {
        // PATH is set on both Windows and Linux
        string source = """
            main : Text
            main = get-env "PATH"
            """;
        string? output = CompileAndRun(source, "getenv_path");
        Assert.NotNull(output);
        Assert.False(string.IsNullOrWhiteSpace(output));
    }

    // ── current-dir ─────────────────────────────────────────────

    [Fact]
    public void Current_dir_returns_nonempty()
    {
        string source = """
            main : Text
            main = current-dir
            """;
        string? output = CompileAndRun(source, "curdir");
        Assert.NotNull(output);
        Assert.False(string.IsNullOrWhiteSpace(output));
    }

    [Fact]
    public void Current_dir_used_in_concatenation()
    {
        string source = """
            main : Text
            main = "cwd=" ++ current-dir
            """;
        string? output = CompileAndRun(source, "curdir_concat");
        Assert.NotNull(output);
        Assert.StartsWith("cwd=", output.Trim());
        Assert.True(output.Trim().Length > 4); // has actual path content after prefix
    }

    // ── list-files ──────────────────────────────────────────────

    [Fact]
    public void List_files_emits_il()
    {
        string source = """
            main : Integer
            main = list-length (list-files "." "*")
            """;
        byte[]? bytes = Helpers.CompileToIL(source, "listfiles_emit");
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);
    }

    [Fact]
    public void List_files_returns_list()
    {
        // The temp dir always has at least the .dll and .runtimeconfig.json we write
        string source = """
            main : Text
            main = if list-length (list-files "." "*") > 0
              then "found files"
              else "empty"
            """;
        string? output = CompileAndRun(source, "listfiles_run");
        Assert.NotNull(output);
        Assert.Equal("found files", output.Trim());
    }

    // ── is-letter / is-digit / is-whitespace (CCE ranges) ──────
    // CCE encodes letters at 13-64, digits at 3-12, whitespace at 0-2.
    // The IL emitter must do range checks on the CCE value, not
    // delegate to .NET's Char.Is* (which interpret the value as a
    // Unicode codepoint and return false for CCE letter positions).

    [Fact]
    public void Is_letter_true_for_cce_letter()
    {
        string source = """
            main : Text
            main = show (is-letter (char-at "a" 0))
            """;
        string? output = CompileAndRun(source, "is_letter_true");
        Assert.NotNull(output);
        Assert.Equal("True", output.Trim());
    }

    [Fact]
    public void Is_letter_false_for_digit()
    {
        string source = """
            main : Text
            main = show (is-letter (char-at "1" 0))
            """;
        string? output = CompileAndRun(source, "is_letter_false_digit");
        Assert.NotNull(output);
        Assert.Equal("False", output.Trim());
    }

    [Fact]
    public void Is_letter_false_for_space()
    {
        string source = """
            main : Text
            main = show (is-letter (char-at " " 0))
            """;
        string? output = CompileAndRun(source, "is_letter_false_space");
        Assert.NotNull(output);
        Assert.Equal("False", output.Trim());
    }

    [Fact]
    public void Is_digit_true_for_cce_digit()
    {
        string source = """
            main : Text
            main = show (is-digit (char-at "7" 0))
            """;
        string? output = CompileAndRun(source, "is_digit_true");
        Assert.NotNull(output);
        Assert.Equal("True", output.Trim());
    }

    [Fact]
    public void Is_digit_false_for_letter()
    {
        string source = """
            main : Text
            main = show (is-digit (char-at "a" 0))
            """;
        string? output = CompileAndRun(source, "is_digit_false_letter");
        Assert.NotNull(output);
        Assert.Equal("False", output.Trim());
    }

    [Fact]
    public void Is_whitespace_true_for_space()
    {
        string source = """
            main : Text
            main = show (is-whitespace (char-at " " 0))
            """;
        string? output = CompileAndRun(source, "is_ws_true");
        Assert.NotNull(output);
        Assert.Equal("True", output.Trim());
    }

    [Fact]
    public void Is_whitespace_false_for_letter()
    {
        string source = """
            main : Text
            main = show (is-whitespace (char-at "a" 0))
            """;
        string? output = CompileAndRun(source, "is_ws_false_letter");
        Assert.NotNull(output);
        Assert.Equal("False", output.Trim());
    }

    // ── Helpers ─────────────────────────────────────────────────

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
