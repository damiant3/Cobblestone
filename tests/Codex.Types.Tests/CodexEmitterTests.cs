using Xunit;

namespace Codex.Types.Tests;

public sealed class CodexEmitterTests
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
            {
                return candidate;
            }

            dir = Path.GetDirectoryName(dir)!;
        }
        throw new DirectoryNotFoundException("Cannot find samples/ directory");
    }

    [Fact]
    public void Hello_roundtrips_to_codex()
    {
        string source = ReadSample("hello.codex");
        string? codex = Helpers.CompileToCodex(source, "hello");
        Assert.NotNull(codex);
        Assert.Contains("square", codex);
        Assert.Contains("Integer -> Integer", codex);
        Assert.Contains("x * x", codex);
    }

    [Fact]
    public void Factorial_roundtrips_to_codex()
    {
        string source = ReadSample("factorial.codex");
        string? codex = Helpers.CompileToCodex(source, "factorial");
        Assert.NotNull(codex);
        Assert.Contains("factorial", codex);
        Assert.Contains("if n == 0", codex);
        Assert.Contains("then 1", codex);
    }

    [Fact]
    public void Shapes_roundtrips_to_codex()
    {
        string source = ReadSample("shapes.codex");
        string? codex = Helpers.CompileToCodex(source, "shapes");
        Assert.NotNull(codex);
        Assert.Contains("Shape =", codex);
        Assert.Contains("| Circle", codex);
        Assert.Contains("| Rectangle", codex);
        Assert.Contains("when", codex);
        Assert.Contains("is Circle", codex);
    }

    [Fact]
    public void Person_roundtrips_to_codex()
    {
        string source = ReadSample("person.codex");
        string? codex = Helpers.CompileToCodex(source, "person");
        Assert.NotNull(codex);
        Assert.Contains("record {", codex);
        Assert.Contains("name : Text", codex);
        Assert.Contains("age : Integer", codex);
        Assert.Contains("++", codex);
    }

    [Fact]
    public void SafeDivide_roundtrips_to_codex()
    {
        string source = ReadSample("safe-divide.codex");
        string? codex = Helpers.CompileToCodex(source, "safe_divide");
        Assert.NotNull(codex);
        Assert.Contains("Result =", codex);
        Assert.Contains("| Success", codex);
        Assert.Contains("| Failure", codex);
        Assert.Contains("when", codex);
    }

    [Fact]
    public void EffectfulHello_roundtrips_to_codex()
    {
        string source = ReadSample("effectful-hello.codex");
        string? codex = Helpers.CompileToCodex(source, "effectful_hello");
        Assert.NotNull(codex);
        Assert.Contains("act", codex);
        Assert.Contains("end", codex);
        Assert.Contains("print-line", codex);
        Assert.Contains("name <-", codex);
    }

    [Fact]
    public void TcoStress_roundtrips_to_codex()
    {
        string source = ReadSample("tco-stress.codex");
        string? codex = Helpers.CompileToCodex(source, "tco_stress");
        Assert.NotNull(codex);
        Assert.Contains("sum-to", codex);
        Assert.Contains("Integer -> Integer -> Integer", codex);
        Assert.Contains("if n == 0", codex);
    }

    [Fact]
    public void StringOps_roundtrips_to_codex()
    {
        string source = ReadSample("string-ops.codex");
        string? codex = Helpers.CompileToCodex(source, "string_ops");
        Assert.NotNull(codex);
        Assert.Contains("count-letters", codex);
        Assert.Contains("Char -> Boolean", codex);
        Assert.Contains("True", codex);
        Assert.Contains("False", codex);
    }

    [Fact]
    public void Output_is_valid_codex_syntax()
    {
        string source = "double : Integer -> Integer\ndouble (x) = x + x\n\nmain : Integer\nmain = double 21\n";
        string? codex = Helpers.CompileToCodex(source, "test");
        Assert.NotNull(codex);
        Assert.Contains("double : Integer -> Integer", codex);
        Assert.Contains("main : Integer", codex);
        Assert.Contains("x + x", codex);
        Assert.Contains("double 21", codex);
    }

    [Fact]
    public void Let_binding_emits_let_in()
    {
        string source = "f : Integer -> Integer\nf (x) = let y = x + 1 in y * 2\n\nmain : Integer\nmain = f 5\n";
        string? codex = Helpers.CompileToCodex(source, "test");
        Assert.NotNull(codex);
        Assert.Contains("let y =", codex);
        Assert.Contains("in ", codex);
    }

    [Fact]
    public void Lambda_emits_backslash_arrow()
    {
        string source = "apply : (Integer -> Integer) -> Integer -> Integer\napply (f) (x) = f x\n\nmain : Integer\nmain = apply (\\x -> x + 1) 41\n";
        string? codex = Helpers.CompileToCodex(source, "test");
        Assert.NotNull(codex);
        Assert.Contains("\\", codex);
        Assert.Contains("->", codex);
    }

    [Fact]
    public void List_literal_emits_brackets()
    {
        string source = "main : List Integer\nmain = [1, 2, 3]\n";
        string? codex = Helpers.CompileToCodex(source, "test");
        Assert.NotNull(codex);
        Assert.Contains("[1, 2, 3]", codex);
    }

    [Fact]
    public void File_extension_is_codex()
    {
        Codex.Emit.Codex.CodexEmitter emitter = new();
        Assert.Equal(".codex", emitter.FileExtension);
        Assert.Equal("Codex", emitter.TargetName);
    }
}
