using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Xunit;

namespace Codex.Types.Tests;

public class ILEmitterIntegrationTests
{
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
                return candidate;
            dir = Path.GetDirectoryName(dir)!;
        }
        throw new DirectoryNotFoundException("Cannot find samples/ directory");
    }

    // ── Basic emission: produces non-null, non-empty bytes ─────

    [Fact]
    public void Hello_emits_il_bytes()
    {
        string source = ReadSample("hello.codex");
        byte[]? bytes = Helpers.CompileToIL(source, "hello");
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);
    }

    [Fact]
    public void Factorial_emits_il_bytes()
    {
        string source = ReadSample("factorial.codex");
        byte[]? bytes = Helpers.CompileToIL(source, "factorial");
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);
    }

    [Fact]
    public void Arithmetic_emits_il_bytes()
    {
        string source = ReadSample("arithmetic.codex");
        byte[]? bytes = Helpers.CompileToIL(source, "arithmetic");
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);
    }

    // ── PE structure validation ────────────────────────────────

    [Fact]
    public void Hello_produces_valid_pe()
    {
        string source = ReadSample("hello.codex");
        byte[]? bytes = Helpers.CompileToIL(source, "hello");
        Assert.NotNull(bytes);

        using MemoryStream ms = new(bytes);
        using PEReader pe = new(ms);
        Assert.True(pe.HasMetadata);

        MetadataReader reader = pe.GetMetadataReader();
        Assert.True(reader.GetTableRowCount(TableIndex.MethodDef) > 0);
    }

    [Fact]
    public void Factorial_produces_valid_pe()
    {
        string source = ReadSample("factorial.codex");
        byte[]? bytes = Helpers.CompileToIL(source, "factorial");
        Assert.NotNull(bytes);

        using MemoryStream ms = new(bytes);
        using PEReader pe = new(ms);
        Assert.True(pe.HasMetadata);

        MetadataReader reader = pe.GetMetadataReader();
        Assert.True(reader.GetTableRowCount(TableIndex.MethodDef) > 0);
    }

    // ── Method definitions in metadata ─────────────────────────

    [Fact]
    public void Hello_contains_expected_methods()
    {
        string source = ReadSample("hello.codex");
        byte[]? bytes = Helpers.CompileToIL(source, "hello");
        Assert.NotNull(bytes);

        List<string> methodNames = GetMethodNames(bytes);
        Assert.Contains("square", methodNames);
        Assert.Contains("double", methodNames);
        Assert.Contains("Main", methodNames);
    }

    [Fact]
    public void Factorial_contains_expected_methods()
    {
        string source = ReadSample("factorial.codex");
        byte[]? bytes = Helpers.CompileToIL(source, "factorial");
        Assert.NotNull(bytes);

        List<string> methodNames = GetMethodNames(bytes);
        Assert.Contains("factorial", methodNames);
        Assert.Contains("abs", methodNames);
        Assert.Contains("max", methodNames);
        Assert.Contains("Main", methodNames);
    }

    [Fact]
    public void Arithmetic_contains_expected_methods()
    {
        string source = ReadSample("arithmetic.codex");
        byte[]? bytes = Helpers.CompileToIL(source, "arithmetic");
        Assert.NotNull(bytes);

        List<string> methodNames = GetMethodNames(bytes);
        Assert.Contains("max", methodNames);
        Assert.Contains("abs", methodNames);
        Assert.Contains("clamp", methodNames);
        Assert.Contains("Main", methodNames);
    }

    // ── Inline source tests ────────────────────────────────────

    [Fact]
    public void Simple_text_main_emits_il()
    {
        string source = """
            main : Text
            main = "Hello, IL!"
            """;
        byte[]? bytes = Helpers.CompileToIL(source, "textmain");
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);

        List<string> methodNames = GetMethodNames(bytes);
        Assert.Contains("Main", methodNames);
    }

    [Fact]
    public void Boolean_main_emits_il()
    {
        string source = """
            main : Boolean
            main = 1 == 1
            """;
        byte[]? bytes = Helpers.CompileToIL(source, "boolmain");
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);
    }

    [Fact]
    public void Let_binding_emits_il()
    {
        string source = """
            main : Integer
            main = let x = 10 in x + 5
            """;
        byte[]? bytes = Helpers.CompileToIL(source, "letmain");
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);
    }

    [Fact]
    public void If_then_else_emits_il()
    {
        string source = """
            main : Integer
            main = if 1 > 0 then 42 else 0
            """;
        byte[]? bytes = Helpers.CompileToIL(source, "ifmain");
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);
    }

    [Fact]
    public void Multi_param_function_emits_il()
    {
        string source = """
            add : Integer -> Integer -> Integer
            add (x) (y) = x + y

            main : Integer
            main = add 3 4
            """;
        byte[]? bytes = Helpers.CompileToIL(source, "addmain");
        Assert.NotNull(bytes);

        List<string> methodNames = GetMethodNames(bytes);
        Assert.Contains("add", methodNames);
        Assert.Contains("Main", methodNames);
    }

    // ── Runtime execution tests ────────────────────────────────

    [Fact]
    public void Hello_exe_runs_and_prints_25()
    {
        string source = ReadSample("hello.codex");
        string? output = CompileAndRun(source, "hello_run");
        Assert.NotNull(output);
        Assert.Equal("25", output.Trim());
    }

    [Fact]
    public void Arithmetic_exe_runs_and_prints_37()
    {
        string source = ReadSample("arithmetic.codex");
        string? output = CompileAndRun(source, "arithmetic_run");
        Assert.NotNull(output);
        Assert.Equal("37", output.Trim());
    }

    [Fact]
    public void Factorial_exe_runs_and_prints_3628800()
    {
        string source = ReadSample("factorial.codex");
        string? output = CompileAndRun(source, "factorial_run");
        Assert.NotNull(output);
        Assert.Equal("3628800", output.Trim());
    }

    [Fact]
    public void Text_main_exe_runs_and_prints_hello()
    {
        string source = """
            main : Text
            main = "Hello, IL!"
            """;
        string? output = CompileAndRun(source, "text_run");
        Assert.NotNull(output);
        Assert.Equal("Hello, IL!", output.Trim());
    }

    [Fact]
    public void Boolean_main_exe_runs_and_prints_true()
    {
        string source = """
            main : Boolean
            main = 1 == 1
            """;
        string? output = CompileAndRun(source, "bool_run");
        Assert.NotNull(output);
        Assert.Equal("True", output.Trim());
    }

    [Fact]
    public void Let_binding_exe_runs_correctly()
    {
        string source = """
            main : Integer
            main = let x = 10 in x + 5
            """;
        string? output = CompileAndRun(source, "let_run");
        Assert.NotNull(output);
        Assert.Equal("15", output.Trim());
    }

    [Fact]
    public void If_then_else_exe_runs_correctly()
    {
        string source = """
            main : Integer
            main = if 1 > 0 then 42 else 0
            """;
        string? output = CompileAndRun(source, "if_run");
        Assert.NotNull(output);
        Assert.Equal("42", output.Trim());
    }

    [Fact]
    public void Multi_param_function_exe_runs_correctly()
    {
        string source = """
            add : Integer -> Integer -> Integer
            add (x) (y) = x + y

            main : Integer
            main = add 3 4
            """;
        string? output = CompileAndRun(source, "add_run");
        Assert.NotNull(output);
        Assert.Equal("7", output.Trim());
    }

    [Fact]
    public void Negation_exe_runs_correctly()
    {
        string source = """
            abs : Integer -> Integer
            abs (x) = if x < 0 then -x else x

            main : Integer
            main = abs (-5)
            """;
        string? output = CompileAndRun(source, "neg_run");
        Assert.NotNull(output);
        Assert.Equal("5", output.Trim());
    }

    [Fact]
    public void Nested_apply_exe_runs_correctly()
    {
        string source = """
            max : Integer -> Integer -> Integer
            max (x) (y) = if x > y then x else y

            abs : Integer -> Integer
            abs (x) = if x < 0 then -x else x

            clamp : Integer -> Integer -> Integer -> Integer
            clamp (lo) (hi) (x) =
              let clamped = if x < lo then lo else if x > hi then hi else x
              in clamped

            main : Integer
            main = clamp 0 100 (abs (max (-42) 37))
            """;
        string? output = CompileAndRun(source, "nested_run");
        Assert.NotNull(output);
        Assert.Equal("37", output.Trim());
    }

    [Fact]
    public void Three_arg_function_exe_runs_correctly()
    {
        string source = """
            clamp : Integer -> Integer -> Integer -> Integer
            clamp (lo) (hi) (x) =
              if x < lo then lo else if x > hi then hi else x

            main : Integer
            main = clamp 0 100 42
            """;
        string? output = CompileAndRun(source, "three_arg_run");
        Assert.NotNull(output);
        Assert.Equal("42", output.Trim());
    }

    [Fact]
    public void Compose_two_calls_exe_runs_correctly()
    {
        string source = """
            double : Integer -> Integer
            double (x) = x + x

            main : Integer
            main = double (double 3)
            """;
        string? output = CompileAndRun(source, "compose_run");
        Assert.NotNull(output);
        Assert.Equal("12", output.Trim());
    }

    [Fact]
    public void Three_arg_with_call_arg_exe_runs_correctly()
    {
        string source = """
            clamp : Integer -> Integer -> Integer -> Integer
            clamp (lo) (hi) (x) =
              if x < lo then lo else if x > hi then hi else x

            double : Integer -> Integer
            double (x) = x + x

            main : Integer
            main = clamp 0 100 (double 25)
            """;
        string? output = CompileAndRun(source, "call_arg_run");
        Assert.NotNull(output);
        Assert.Equal("50", output.Trim());
    }

    [Fact]
    public void Show_integer_exe_runs_correctly()
    {
        string source = """
            main : Text
            main = show 42
            """;
        string? output = CompileAndRun(source, "show_int_run");
        Assert.NotNull(output);
        Assert.Equal("42", output.Trim());
    }

    [Fact]
    public void Show_boolean_exe_runs_correctly()
    {
        string source = """
            main : Text
            main = show (1 == 1)
            """;
        string? output = CompileAndRun(source, "show_bool_run");
        Assert.NotNull(output);
        Assert.Equal("True", output.Trim());
    }

    [Fact]
    public void Show_text_exe_runs_correctly()
    {
        string source = """
            main : Text
            main = show "hello"
            """;
        string? output = CompileAndRun(source, "show_text_run");
        Assert.NotNull(output);
        Assert.Equal("hello", output.Trim());
    }

    [Fact]
    public void PrintLine_exe_runs_correctly()
    {
        string source = """
            main : [Console] Nothing
            main = print-line "Hello from IL!"
            """;
        string? output = CompileAndRun(source, "print_run");
        Assert.NotNull(output);
        Assert.Equal("Hello from IL!", output.Trim());
    }

    [Fact]
    public void Do_block_exe_runs_correctly()
    {
        string source = """
            main : [Console] Nothing
            main = do
              print-line "first"
              print-line "second"
            """;
        string? output = CompileAndRun(source, "do_run");
        Assert.NotNull(output);
        Assert.Contains("first", output);
        Assert.Contains("second", output);
    }

    [Fact]
    public void Generic_sum_type_with_show_exe_runs_correctly()
    {
        string source = """
            Result (a) =
              | Success (a)
              | Failure (Text)

            safe-divide (x) (y) = if y == 0 then Failure "division by zero" else Success (x / y)

            describe (result) = when result is Success (n) -> "got " ++ show n is Failure (msg) -> "error: " ++ msg

            main : Text
            main = describe (safe-divide 42 7)
            """;
        string? output = CompileAndRun(source, "generic_sum_run");
        Assert.NotNull(output);
        Assert.Equal("got 6", output.Trim());
    }

    [Fact]
    public void Effectful_do_with_read_line_emits_il()
    {
        string source = """
            main : [Console] Nothing
            main = do
              print-line "What is your name?"
              name <- read-line
              print-line ("Hello, " ++ name ++ "!")
            """;
        byte[]? bytes = Helpers.CompileToIL(source, "effectful_do");
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);
    }

    // ── Helpers ────────────────────────────────────────────────

    static List<string> GetMethodNames(byte[] peBytes)
    {
        using MemoryStream ms = new(peBytes);
        using PEReader pe = new(ms);
        MetadataReader reader = pe.GetMetadataReader();
        List<string> names = new();
        foreach (MethodDefinitionHandle handle in reader.MethodDefinitions)
        {
            MethodDefinition method = reader.GetMethodDefinition(handle);
            names.Add(reader.GetString(method.Name));
        }
        return names;
    }

    static string? CompileAndRun(string source, string chapterName)
    {
        byte[]? bytes = Helpers.CompileToIL(source, chapterName);
        if (bytes is null) return null;

        string tempDir = Path.Combine(Path.GetTempPath(), "codex_il_test_" + chapterName + "_" + Guid.NewGuid().ToString("N")[..8]);
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

    [Fact]
    public void Text_to_integer_on_valid_input()
    {
        string source = """
            main : Integer
            main = text-to-integer "42"
            """;
        string? output = CompileAndRun(source, "tti_valid");
        Assert.NotNull(output);
        Assert.Equal("42", output.Trim());
    }

    [Fact]
    public void Text_to_integer_on_non_numeric_returns_zero()
    {
        string source = """
            main : Integer
            main = text-to-integer "abc"
            """;
        string? output = CompileAndRun(source, "tti_abc");
        Assert.NotNull(output);
        Assert.Equal("0", output.Trim());
    }

    [Fact]
    public void Text_to_integer_on_empty_string_returns_zero()
    {
        string source = """
            main : Integer
            main = text-to-integer ""
            """;
        string? output = CompileAndRun(source, "tti_empty");
        Assert.NotNull(output);
        Assert.Equal("0", output.Trim());
    }

    [Fact]
    public void Text_to_integer_on_negative_number()
    {
        string source = """
            main : Integer
            main = text-to-integer "-7"
            """;
        string? output = CompileAndRun(source, "tti_neg");
        Assert.NotNull(output);
        Assert.Equal("-7", output.Trim());
    }

    [Fact]
    public void Text_to_integer_on_float_string_returns_zero()
    {
        string source = """
            main : Integer
            main = text-to-integer "3.14"
            """;
        string? output = CompileAndRun(source, "tti_float");
        Assert.NotNull(output);
        Assert.Equal("0", output.Trim());
    }

    [Fact]
    public void Text_to_integer_on_overflow_returns_zero()
    {
        string source = """
            main : Integer
            main = text-to-integer "99999999999999999999"
            """;
        string? output = CompileAndRun(source, "tti_overflow");
        Assert.NotNull(output);
        Assert.Equal("0", output.Trim());
    }

    // ── run-state effect handler (IL) ──────────────────────────

    [Fact]
    public void Run_state_simple_get_returns_initial()
    {
        string source = """
            main : Integer
            main = run-state 42 get-state
            """;
        string? output = CompileAndRun(source, "rs_simple_get");
        Assert.NotNull(output);
        Assert.Equal("42", output.Trim());
    }

    [Fact]
    public void Run_state_set_then_get()
    {
        string source = """
            main : Integer
            main = run-state 0 do
              set-state 10
              get-state
            """;
        string? output = CompileAndRun(source, "rs_set_get");
        Assert.NotNull(output);
        Assert.Equal("10", output.Trim());
    }

    [Fact]
    public void Run_state_increment()
    {
        string source = """
            main : Integer
            main = run-state 0 do
              x <- get-state
              set-state (x + 1)
              get-state
            """;
        string? output = CompileAndRun(source, "rs_increment");
        Assert.NotNull(output);
        Assert.Equal("1", output.Trim());
    }

    [Fact]
    public void Run_state_arithmetic_chain()
    {
        string source = """
            main : Integer
            main = run-state 0 do
              x <- get-state
              set-state (x + 10)
              y <- get-state
              set-state (y * 2)
              get-state
            """;
        string? output = CompileAndRun(source, "rs_arith_chain");
        Assert.NotNull(output);
        Assert.Equal("20", output.Trim());
    }

    [Fact]
    public void Run_state_with_text()
    {
        string source = """
            main : Text
            main = run-state "hello" do
              s <- get-state
              set-state (s ++ " world")
              get-state
            """;
        string? output = CompileAndRun(source, "rs_text");
        Assert.NotNull(output);
        Assert.Equal("hello world", output.Trim());
    }

    [Fact]
    public void Run_state_emits_valid_il()
    {
        string source = """
            main : Integer
            main = run-state 0 do
              x <- get-state
              set-state (x + 1)
              get-state
            """;
        byte[]? bytes = Helpers.CompileToIL(source, "rs_emit");
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);
    }

    // ── user-defined effect handlers (IL) ─────────────────────────

    [Fact]
    public void Handle_ask_returns_42()
    {
        string source = """
            effect Ask where
              ask : Integer

            comp : [Ask] Integer
            comp = ask

            main : Integer
            main = with Ask comp
              ask (resume) = resume 42
            """;
        string? output = CompileAndRun(source, "handle_ask");
        Assert.NotNull(output);
        Assert.Equal("42", output.Trim());
    }

    [Fact]
    public void Handle_ask_direct_computation()
    {
        string source = """
            effect Ask where
              ask : Integer

            main : Integer
            main = with Ask ask
              ask (resume) = resume 99
            """;
        string? output = CompileAndRun(source, "handle_ask_direct");
        Assert.NotNull(output);
        Assert.Equal("99", output.Trim());
    }

    [Fact]
    public void Handle_log_with_parameter()
    {
        string source = """
            effect Logger where
              log : Text -> Integer

            program : [Logger] Integer
            program = log "hello"

            main : Integer
            main = with Logger program
              log (msg) (resume) = resume 0
            """;
        string? output = CompileAndRun(source, "handle_log");
        Assert.NotNull(output);
        Assert.Equal("0", output.Trim());
    }

    [Fact]
    public void Handle_ask_resume_with_arithmetic()
    {
        string source = """
            effect Ask where
              ask : Integer

            comp : [Ask] Integer
            comp = ask + 8

            main : Integer
            main = with Ask comp
              ask (resume) = resume 34
            """;
        string? output = CompileAndRun(source, "handle_ask_arith");
        Assert.NotNull(output);
        Assert.Equal("42", output.Trim());
    }

    [Fact]
    public void Handle_emits_valid_il()
    {
        string source = """
            effect Ask where
              ask : Integer

            comp : [Ask] Integer
            comp = ask

            main : Integer
            main = with Ask comp
              ask (resume) = resume 42
            """;
        byte[]? bytes = Helpers.CompileToIL(source, "handle_emit");
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);
    }
}
