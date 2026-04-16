using Codex.Core;
using Xunit;

namespace Codex.Types.Tests;

public class DoNotationTests
{
    [Fact]
    public void Do_block_with_user_effect_type_checks()
    {
        string source = """
            effect Ask where
              ask : Integer

            program : [Ask] Integer
            program = act
              x <- ask
              x + 1
            """;
        DiagnosticBag diag = Helpers.TypeCheckWithDiagnostics(source);
        Assert.False(diag.HasErrors, string.Join("; ", diag.ToImmutable()));
    }

    [Fact]
    public void Do_block_with_user_effect_emits_cs()
    {
        string source = """
            effect Ask where
              ask : Integer

            program : [Ask] Integer
            program = act
              x <- ask
              x + 1
            """;
        string? cs = Helpers.CompileToCS(source);
        Assert.NotNull(cs);
    }

    [Fact]
    public void Do_block_bind_and_return_type_checks()
    {
        string source = """
            effect Counter where
              inc : Integer

            count-twice : [Counter] Integer
            count-twice = act
              a <- inc
              b <- inc
              a + b
            """;
        DiagnosticBag diag = Helpers.TypeCheckWithDiagnostics(source);
        Assert.False(diag.HasErrors, string.Join("; ", diag.ToImmutable()));
    }

    [Fact]
    public void Do_block_with_handle_type_checks()
    {
        string source = """
            effect Ask where
              ask : Integer

            program : [Ask] Integer
            program = act
              x <- ask
              x + 1

            main : Integer
            main = with Ask program
              ask (resume) = resume 42
            """;
        DiagnosticBag diag = Helpers.TypeCheckWithDiagnostics(source);
        Assert.False(diag.HasErrors, string.Join("; ", diag.ToImmutable()));
    }

    [Fact]
    public void Do_block_with_handle_emits_cs()
    {
        string source = """
            effect Ask where
              ask : Integer

            program : [Ask] Integer
            program = act
              x <- ask
              x + 1

            main : Integer
            main = with Ask program
              ask (resume) = resume 42
            """;
        string? cs = Helpers.CompileToCS(source);
        Assert.NotNull(cs);
    }

    [Fact]
    public void Do_block_returns_last_expression_in_emitted_cs()
    {
        string source = """
            main : [Console] Integer
            main = act
              print-line "hello"
              42
            """;
        string? cs = Helpers.CompileToCS(source);
        Assert.NotNull(cs);
        Assert.Contains("return 42L", cs);
    }

    [Fact]
    public void Do_block_with_multiple_effects_type_checks()
    {
        string source = """
            effect Logger where
              log : Text -> Integer

            main : [Console, Logger] Integer
            main = act
              print-line "starting"
              x <- log "step1"
              x
            """;
        DiagnosticBag diag = Helpers.TypeCheckWithDiagnostics(source);
        Assert.False(diag.HasErrors, string.Join("; ", diag.ToImmutable()));
    }
}
