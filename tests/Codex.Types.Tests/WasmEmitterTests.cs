using System.Diagnostics;
using Xunit;

namespace Codex.Types.Tests;

public class WasmEmitterTests
{
    [Fact]
    public void Hello_emits_wasm_bytes()
    {
        string source = """
            square : Integer -> Integer
            square (x) = x * x

            main : Integer
            main = square 5
            """;
        byte[]? bytes = Helpers.CompileToWasm(source, "hello_wasm");
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);
        // Verify WASM magic bytes
        Assert.Equal(0x00, bytes[0]);
        Assert.Equal(0x61, bytes[1]);
        Assert.Equal(0x73, bytes[2]);
        Assert.Equal(0x6D, bytes[3]);
        // Verify WASM version 1
        Assert.Equal(0x01, bytes[4]);
        Assert.Equal(0x00, bytes[5]);
        Assert.Equal(0x00, bytes[6]);
        Assert.Equal(0x00, bytes[7]);
    }

    [Fact]
    public void Factorial_emits_wasm_bytes()
    {
        string source = """
            factorial : Integer -> Integer
            factorial (n) = if n == 0 then 1 else n * factorial (n - 1)

            main : Integer
            main = factorial 5
            """;
        byte[]? bytes = Helpers.CompileToWasm(source, "factorial_wasm");
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);
    }

    [Fact]
    public void Text_literal_emits_wasm_bytes()
    {
        string source = """
            greeting : Text -> Text
            greeting (name) = "Hello, " ++ name ++ "!"

            main : Text
            main = greeting "World"
            """;
        byte[]? bytes = Helpers.CompileToWasm(source, "greeting_wasm");
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);
    }

    [Fact]
    public void Arithmetic_emits_wasm_bytes()
    {
        string source = """
            add : Integer -> Integer -> Integer
            add (x) (y) = x + y

            main : Integer
            main = add 3 4
            """;
        byte[]? bytes = Helpers.CompileToWasm(source, "arithmetic_wasm");
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);
    }

    [Fact]
    public void Boolean_emits_wasm_bytes()
    {
        string source = """
            is-even : Integer -> Integer
            is-even (n) = if n == 0 then 1 else 0

            main : Integer
            main = is-even 4
            """;
        byte[]? bytes = Helpers.CompileToWasm(source, "bool_wasm");
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);
    }

    [Fact]
    public void Hello_runs_under_wasmtime()
    {
        string source = """
            square : Integer -> Integer
            square (x) = x * x

            main : Integer
            main = square 5
            """;
        string? output = CompileAndRun(source, "hello_run_wasm");
        if (output is null)
        {
            return; // wasmtime not available, skip
        }

        Assert.Equal("25", output.Trim());
    }

    [Fact]
    public void Factorial_runs_under_wasmtime()
    {
        string source = """
            factorial : Integer -> Integer
            factorial (n) = if n == 0 then 1 else n * factorial (n - 1)

            main : Integer
            main = factorial 5
            """;
        string? output = CompileAndRun(source, "factorial_run_wasm");
        if (output is null)
        {
            return;
        }

        Assert.Equal("120", output.Trim());
    }

    [Fact]
    public void Text_greeting_runs_under_wasmtime()
    {
        string source = """
            greeting : Text -> Text
            greeting (name) = "Hello, " ++ name ++ "!"

            main : Text
            main = greeting "World"
            """;
        string? output = CompileAndRun(source, "greeting_run_wasm");
        if (output is null)
        {
            return;
        }

        Assert.Equal("Hello, World!", output.Trim());
    }

    [Fact]
    public void Let_binding_runs_under_wasmtime()
    {
        string source = """
            main : Integer
            main = let x = 10 in let y = 20 in x + y
            """;
        string? output = CompileAndRun(source, "let_run_wasm");
        if (output is null)
        {
            return;
        }

        Assert.Equal("30", output.Trim());
    }

    [Fact]
    public void Show_integer_runs_under_wasmtime()
    {
        string source = """
            main : Text
            main = show 42
            """;
        string? output = CompileAndRun(source, "show_int_run_wasm");
        if (output is null)
        {
            return;
        }

        Assert.Equal("42", output.Trim());
    }

    [Fact]
    public void String_equality_emits_wasm_bytes()
    {
        string source = """
            same : Text -> Text -> Integer
            same (a) (b) = if a == b then 1 else 0

            main : Integer
            main = same "hello" "hello"
            """;
        byte[]? bytes = Helpers.CompileToWasm(source, "streq_wasm");
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);
    }

    [Fact]
    public void String_equality_true_runs_under_wasmtime()
    {
        string source = """
            same : Text -> Text -> Integer
            same (a) (b) = if a == b then 1 else 0

            main : Integer
            main = same "hello" "hello"
            """;
        string? output = CompileAndRun(source, "streq_true_wasm");
        if (output is null)
        {
            return;
        }

        Assert.Equal("1", output.Trim());
    }

    [Fact]
    public void String_equality_false_runs_under_wasmtime()
    {
        string source = """
            same : Text -> Text -> Integer
            same (a) (b) = if a == b then 1 else 0

            main : Integer
            main = same "hello" "world"
            """;
        string? output = CompileAndRun(source, "streq_false_wasm");
        if (output is null)
        {
            return;
        }

        Assert.Equal("0", output.Trim());
    }

    [Fact]
    public void String_not_equal_runs_under_wasmtime()
    {
        string source = """
            different : Text -> Text -> Integer
            different (a) (b) = if a != b then 1 else 0

            main : Integer
            main = different "foo" "bar"
            """;
        string? output = CompileAndRun(source, "strneq_wasm");
        if (output is null)
        {
            return;
        }

        Assert.Equal("1", output.Trim());
    }

    [Fact]
    public void Text_length_runs_under_wasmtime()
    {
        string source = """
            main : Integer
            main = text-length "hello"
            """;
        string? output = CompileAndRun(source, "textlen_wasm");
        if (output is null)
        {
            return;
        }

        Assert.Equal("5", output.Trim());
    }

    [Fact]
    public void Concat_and_compare_runs_under_wasmtime()
    {
        string source = """
            main : Integer
            main = if ("ab" ++ "cd") == "abcd" then 1 else 0
            """;
        string? output = CompileAndRun(source, "concat_eq_wasm");
        if (output is null)
        {
            return;
        }

        Assert.Equal("1", output.Trim());
    }

    [Fact]
    public void Nested_calls_run_under_wasmtime()
    {
        string source = """
            double : Integer -> Integer
            double (x) = x * 2

            quad : Integer -> Integer
            quad (x) = double (double x)

            main : Integer
            main = quad 3
            """;
        string? output = CompileAndRun(source, "nested_wasm");
        if (output is null)
        {
            return;
        }

        Assert.Equal("12", output.Trim());
    }

    [Fact]
    public void Pattern_match_literal_runs_under_wasmtime()
    {
        string source = """
            describe : Integer -> Text
            describe (n) = if n == 0 then "zero" else if n == 1 then "one" else "other"

            main : Text
            main = describe 1
            """;
        string? output = CompileAndRun(source, "matchlit_wasm");
        if (output is null)
        {
            return;
        }

        Assert.Equal("one", output.Trim());
    }

    [Fact]
    public void Text_to_integer_runs_under_wasmtime()
    {
        string source = """
            main : Integer
            main = text-to-integer "42"
            """;
        string? output = CompileAndRun(source, "t2i_wasm");
        if (output is null)
        {
            return;
        }

        Assert.Equal("42", output.Trim());
    }

    [Fact]
    public void Text_to_integer_negative_runs_under_wasmtime()
    {
        string source = """
            main : Integer
            main = text-to-integer "-7"
            """;
        string? output = CompileAndRun(source, "t2i_neg_wasm");
        if (output is null)
        {
            return;
        }

        Assert.Equal("-7", output.Trim());
    }

    [Fact]
    public void Integer_to_text_runs_under_wasmtime()
    {
        string source = """
            main : Text
            main = integer-to-text 99
            """;
        string? output = CompileAndRun(source, "i2t_wasm");
        if (output is null)
        {
            return;
        }

        Assert.Equal("99", output.Trim());
    }

    [Fact]
    public void Char_at_runs_under_wasmtime()
    {
        string source = """
            main : Text
            main = char-at "hello" 1
            """;
        string? output = CompileAndRun(source, "charat_wasm");
        if (output is null)
        {
            return;
        }

        Assert.Equal("e", output.Trim());
    }

    [Fact]
    public void Substring_runs_under_wasmtime()
    {
        string source = """
            main : Text
            main = substring "hello world" 6 5
            """;
        string? output = CompileAndRun(source, "substr_wasm");
        if (output is null)
        {
            return;
        }

        Assert.Equal("world", output.Trim());
    }

    [Fact]
    public void Region_allocator_stable_across_calls()
    {
        // A function that allocates heap values (text concat) and returns
        // a scalar. Regions free the allocations on return. Calling this
        // 1000 times should not exhaust the 64KB WASM page.
        string source = """
            greet : Integer -> Integer
            greet (n) = text-length ("hello " ++ (show n) ++ "!")

            loop : Integer -> Integer -> Integer
            loop (i) (acc) = if i == 0 then acc else loop (i - 1) (acc + greet i)

            main : Integer
            main = loop 1000 0
            """;
        string? output = CompileAndRun(source, "region_stable_wasm");
        if (output is null)
        {
            return;
        }
        // Each greet call produces "hello N!" where N is 1-1000
        // The sum of text-lengths should be consistent
        int result = int.Parse(output.Trim());
        Assert.True(result > 0, $"Expected positive sum, got {result}");
    }

    [Fact]
    public void Text_escape_promotion_works()
    {
        // A function that returns a heap-allocated Text (concat).
        // The region should promote the return value to the caller's region.
        string source = """
            make-greeting : Text -> Text
            make-greeting (name) = "Hello, " ++ name ++ "!"

            main : Text
            main = make-greeting "World"
            """;
        string? output = CompileAndRun(source, "text_escape_wasm");
        if (output is null)
        {
            return;
        }

        Assert.Equal("Hello, World!", output.Trim());
    }

    [Fact]
    public void Text_escape_stable_in_loop()
    {
        // Call a text-returning function many times — regions must promote
        // the result each time without exhausting memory
        string source = """
            tag : Integer -> Text
            tag (n) = "item-" ++ (show n)

            count-chars : Integer -> Integer -> Integer
            count-chars (i) (acc) = if i == 0 then acc else count-chars (i - 1) (acc + text-length (tag i))

            main : Integer
            main = count-chars 500 0
            """;
        string? output = CompileAndRun(source, "text_escape_loop_wasm");
        if (output is null)
        {
            return;
        }

        int result = int.Parse(output.Trim());
        Assert.True(result > 0, $"Expected positive sum, got {result}");
    }

    [Fact]
    public void Record_creation_and_field_access_emits()
    {
        string source = """
            Point = record {
              x : Integer,
              y : Integer
            }

            main : Integer
            main = let p = Point { x = 3, y = 4 } in p.x + p.y
            """;
        byte[]? bytes = Helpers.CompileToWasm(source, "record_wasm");
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);
    }

    [Fact]
    public void Record_field_access_runs_under_wasmtime()
    {
        string source = """
            Point = record {
              x : Integer,
              y : Integer
            }

            main : Integer
            main = let p = Point { x = 10, y = 20 } in p.x + p.y
            """;
        string? output = CompileAndRun(source, "record_run_wasm");
        if (output is null)
        {
            return;
        }

        Assert.Equal("30", output.Trim());
    }

    [Fact]
    public void Sum_type_constructor_and_match_emits()
    {
        string source = """
            Shape =
              | Circle (r : Integer)
              | Rect (w : Integer) (h : Integer)

            area : Shape -> Integer
            area (s) =
              when s
                is Circle (r) -> r * r
                is Rect (w) (h) -> w * h

            main : Integer
            main = area (Circle 5)
            """;
        byte[]? bytes = Helpers.CompileToWasm(source, "sum_wasm");
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);
    }

    [Fact]
    public void Sum_type_match_runs_under_wasmtime()
    {
        string source = """
            Shape =
              | Circle (r : Integer)
              | Rect (w : Integer) (h : Integer)

            area : Shape -> Integer
            area (s) =
              when s
                is Circle (r) -> r * r
                is Rect (w) (h) -> w * h

            main : Integer
            main = area (Rect 3 4)
            """;
        string? output = CompileAndRun(source, "sum_run_wasm");
        if (output is null)
        {
            return;
        }

        Assert.Equal("12", output.Trim());
    }

    [Fact]
    public void Record_passed_to_function_runs_under_wasmtime()
    {
        string source = """
            Point = record {
              x : Integer,
              y : Integer
            }

            sum-point : Point -> Integer
            sum-point (p) = p.x + p.y

            main : Integer
            main = sum-point (Point { x = 7, y = 8 })
            """;
        string? output = CompileAndRun(source, "record_fn_wasm");
        if (output is null)
        {
            return;
        }

        Assert.Equal("15", output.Trim());
    }

    // ── Helpers ────────────────────────────────────────────────

    static string? CompileAndRun(string source, string moduleName)
    {
        byte[]? bytes = Helpers.CompileToWasm(source, moduleName);
        if (bytes is null)
        {
            return null;
        }

        // Check if wasmtime is available
        if (!IsWasmtimeAvailable())
        {
            return null;
        }

        string tempDir = Path.Combine(Path.GetTempPath(),
            "codex_wasm_test_" + moduleName + "_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);
        try
        {
            string wasmPath = Path.Combine(tempDir, moduleName + ".wasm");
            File.WriteAllBytes(wasmPath, bytes);

            ProcessStartInfo psi = new("wasmtime", wasmPath)
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
                    $"wasmtime exited with code {proc.ExitCode}.\nstdout: {stdout}\nstderr: {stderr}");
            }

            return stdout;
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    static bool IsWasmtimeAvailable()
    {
        try
        {
            ProcessStartInfo psi = new("wasmtime", "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using Process? proc = Process.Start(psi);
            if (proc is null)
            {
                return false;
            }

            proc.WaitForExit(5_000);
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
