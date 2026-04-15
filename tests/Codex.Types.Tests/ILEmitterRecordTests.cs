using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Xunit;

namespace Codex.Types.Tests;

public class ILEmitterRecordTests
{
    [Fact]
    public void Record_emits_il_bytes()
    {
        string source = """
            Person = record {
              name : Text,
              age : Integer
            }

            get-name : Person -> Text
            get-name (p) = p.name

            main : Text
            main = get-name (Person { name = "Alice", age = 30 })
            """;
        byte[]? bytes = Helpers.CompileToIL(source, "record_emit");
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);
    }

    [Fact]
    public void Record_pe_has_type_definition()
    {
        string source = """
            Person = record {
              name : Text,
              age : Integer
            }

            get-name : Person -> Text
            get-name (p) = p.name

            main : Text
            main = get-name (Person { name = "Alice", age = 30 })
            """;
        byte[]? bytes = Helpers.CompileToIL(source, "record_pe");
        Assert.NotNull(bytes);

        using MemoryStream ms = new(bytes);
        using PEReader pe = new(ms);
        MetadataReader reader = pe.GetMetadataReader();

        List<string> typeNames = new();
        foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
        {
            TypeDefinition td = reader.GetTypeDefinition(handle);
            typeNames.Add(reader.GetString(td.Name));
        }
        Assert.Contains("Person", typeNames);
    }

    [Fact]
    public void Record_field_access_runs_correctly()
    {
        string source = """
            Person = record {
              name : Text,
              age : Integer
            }

            get-name : Person -> Text
            get-name (p) = p.name

            main : Text
            main = get-name (Person { name = "Alice", age = 30 })
            """;
        string? output = CompileAndRun(source, "record_field_run");
        Assert.NotNull(output);
        Assert.Equal("Alice", output.Trim());
    }

    [Fact]
    public void Record_integer_field_access_runs_correctly()
    {
        string source = """
            Point = record {
              x : Integer,
              y : Integer
            }

            sum-coords : Point -> Integer
            sum-coords (p) = p.x + p.y

            main : Integer
            main = sum-coords (Point { x = 10, y = 20 })
            """;
        string? output = CompileAndRun(source, "record_int_field_run");
        Assert.NotNull(output);
        Assert.Equal("30", output.Trim());
    }

    [Fact]
    public void Sum_type_emits_il_bytes()
    {
        string source = """
            Color =
              | Red
              | Green
              | Blue

            describe : Color -> Text
            describe (c) = when c
              is Red -> "red"
              is Green -> "green"
              is Blue -> "blue"

            main : Text
            main = describe Red
            """;
        byte[]? bytes = Helpers.CompileToIL(source, "sum_emit");
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);
    }

    [Fact]
    public void Sum_type_pe_has_type_definitions()
    {
        string source = """
            Color =
              | Red
              | Green
              | Blue

            describe : Color -> Text
            describe (c) = when c
              is Red -> "red"
              is Green -> "green"
              is Blue -> "blue"

            main : Text
            main = describe Red
            """;
        byte[]? bytes = Helpers.CompileToIL(source, "sum_pe");
        Assert.NotNull(bytes);

        using MemoryStream ms = new(bytes);
        using PEReader pe = new(ms);
        MetadataReader reader = pe.GetMetadataReader();

        List<string> typeNames = new();
        foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
        {
            TypeDefinition td = reader.GetTypeDefinition(handle);
            typeNames.Add(reader.GetString(td.Name));
        }
        Assert.Contains("Color", typeNames);
        Assert.Contains("Red", typeNames);
        Assert.Contains("Green", typeNames);
        Assert.Contains("Blue", typeNames);
    }

    [Fact]
    public void Sum_type_match_runs_correctly()
    {
        string source = """
            Color =
              | Red
              | Green
              | Blue

            describe : Color -> Text
            describe (c) = when c
              is Red -> "red"
              is Green -> "green"
              is Blue -> "blue"

            main : Text
            main = describe Green
            """;
        string? output = CompileAndRun(source, "sum_match_run");
        Assert.NotNull(output);
        Assert.Equal("green", output.Trim());
    }

    [Fact]
    public void Sum_type_with_fields_match_runs_correctly()
    {
        string source = """
            Shape =
              | Circle (Integer)
              | Rect (Integer) (Integer)

            area : Shape -> Integer
            area (s) = when s
              is Circle (r) -> r * r
              is Rect (w) (h) -> w * h

            main : Integer
            main = area (Rect 3 4)
            """;
        string? output = CompileAndRun(source, "sum_fields_run");
        Assert.NotNull(output);
        Assert.Equal("12", output.Trim());
    }

    [Fact]
    public void Sum_type_circle_branch_runs_correctly()
    {
        string source = """
            Shape =
              | Circle (Integer)
              | Rect (Integer) (Integer)

            area : Shape -> Integer
            area (s) = when s
              is Circle (r) -> r * r
              is Rect (w) (h) -> w * h

            main : Integer
            main = area (Circle 5)
            """;
        string? output = CompileAndRun(source, "sum_circle_run");
        Assert.NotNull(output);
        Assert.Equal("25", output.Trim());
    }

    [Fact]
    public void Recursive_sum_type_eval_runs_correctly()
    {
        string source = """
            Expr =
              | Lit (Integer)
              | Add (Expr) (Expr)

            eval : Expr -> Integer
            eval (e) = when e
              is Lit (n) -> n
              is Add (a) (b) -> eval a + eval b

            main : Integer
            main = eval (Add (Lit 3) (Lit 4))
            """;
        string? output = CompileAndRun(source, "recursive_sum_run");
        Assert.NotNull(output);
        Assert.Equal("7", output.Trim());
    }

    [Fact]
    public void Multi_branch_ctor_dispatch_runs_correctly()
    {
        string source = """
            Op =
              | Lit (Integer)
              | Add (Op) (Op)
              | Mul (Op) (Op)

            eval : Op -> Integer
            eval (e) = when e
              is Lit (n) -> n
              is Add (a) (b) -> eval a + eval b
              is Mul (a) (b) -> eval a * eval b

            main : Integer
            main = eval (Mul (Add (Lit 2) (Lit 3)) (Lit 4))
            """;
        string? output = CompileAndRun(source, "multi_ctor_dispatch_run");
        Assert.NotNull(output);
        Assert.Equal("20", output.Trim());
    }

    [Fact]
    public void Wildcard_pattern_runs_correctly()
    {
        string source = """
            Color =
              | Red
              | Green
              | Blue

            is-red : Color -> Text
            is-red (c) = when c
              is Red -> "yes"
              is otherwise -> "no"

            main : Text
            main = is-red Blue
            """;
        string? output = CompileAndRun(source, "wildcard_run");
        Assert.NotNull(output);
        Assert.Equal("no", output.Trim());
    }

    [Fact]
    public void Person_sample_emits_il()
    {
        string source = File.ReadAllText(FindSamplePath("person.codex"));
        byte[]? bytes = Helpers.CompileToIL(source, "person_il");
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);
    }

    [Fact]
    public void Person_sample_runs_correctly()
    {
        string source = File.ReadAllText(FindSamplePath("person.codex"));
        string? output = CompileAndRun(source, "person_run");
        Assert.NotNull(output);
        Assert.Equal("Hello, Alice!", output.Trim());
    }

    // ── Helpers ────────────────────────────────────────────────

    static string FindSamplePath(string name)
    {
        string dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            string candidate = Path.Combine(dir, "samples", name);
            if (File.Exists(candidate))
                return candidate;
            dir = Path.GetDirectoryName(dir)!;
        }
        throw new FileNotFoundException($"Cannot find samples/{name}");
    }

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
