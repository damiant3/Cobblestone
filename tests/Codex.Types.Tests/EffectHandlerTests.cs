using Codex.Core;
using Xunit;

namespace Codex.Types.Tests;

public class EffectHandlerTests
{
    [Fact]
    public void Get_state_type_checks()
    {
        string source = """
            counter : [State, Console] Integer
            counter = act
              x <- get-state
              x
            end
            """;
        DiagnosticBag diag = Helpers.TypeCheckWithDiagnostics(source);
        Assert.False(diag.HasErrors, string.Join("; ", diag.ToImmutable()));
    }

    [Fact]
    public void Set_state_type_checks()
    {
        string source = """
            bump : [State] Nothing
            bump = act
              x <- get-state
              set-state (x + 1)
            end
            """;
        DiagnosticBag diag = Helpers.TypeCheckWithDiagnostics(source);
        Assert.False(diag.HasErrors, string.Join("; ", diag.ToImmutable()));
    }

    [Fact]
    public void Run_state_type_checks()
    {
        string source = """
            main : Integer
            main = run-state 0 act
              x <- get-state
              set-state (x + 1)
              get-state
            end
            """;
        DiagnosticBag diag = Helpers.TypeCheckWithDiagnostics(source);
        Assert.False(diag.HasErrors, string.Join("; ", diag.ToImmutable()));
    }

    [Fact]
    public void Run_state_emits_cs()
    {
        string source = """
            main : Integer
            main = run-state 0 act
              x <- get-state
              set-state (x + 1)
              get-state
            end
            """;
        string? cs = Helpers.CompileToCS(source);
        Assert.NotNull(cs);
        Assert.Contains("__state", cs);
    }

    [Fact]
    public void Run_state_simple_get_emits_cs()
    {
        string source = """
            main : Integer
            main = run-state 42 get-state
            """;
        string? cs = Helpers.CompileToCS(source);
        Assert.NotNull(cs);
        Assert.Contains("__state", cs);
    }

    [Fact]
    public void Run_state_increment_emits_cs()
    {
        string source = """
            main : Integer
            main = run-state 0 act
              set-state 10
              get-state
            end
            """;
        string? cs = Helpers.CompileToCS(source);
        Assert.NotNull(cs);
        Assert.Contains("__state", cs);
    }

    [Fact]
    public void Run_state_pure_context_eliminates_state_effect()
    {
        string source = """
            main : Integer
            main = run-state 100 get-state
            """;
        DiagnosticBag diag = Helpers.TypeCheckWithDiagnostics(source);
        Assert.False(diag.HasErrors, string.Join("; ", diag.ToImmutable()));
    }

    [Fact]
    public void Run_state_with_arithmetic_type_checks()
    {
        string source = """
            main : Integer
            main = run-state 0 act
              x <- get-state
              set-state (x + 10)
              y <- get-state
              set-state (y * 2)
              get-state
            end
            """;
        DiagnosticBag diag = Helpers.TypeCheckWithDiagnostics(source);
        Assert.False(diag.HasErrors, string.Join("; ", diag.ToImmutable()));
    }

    [Fact]
    public void Run_state_with_text_type_checks()
    {
        string source = """
            main : Text
            main = run-state "" act
              set-state "hello"
              x <- get-state
              set-state (x ++ " world")
              get-state
            end
            """;
        DiagnosticBag diag = Helpers.TypeCheckWithDiagnostics(source);
        Assert.False(diag.HasErrors, string.Join("; ", diag.ToImmutable()));
    }

    [Fact]
    public void Run_state_in_function_type_checks()
    {
        string source = """
            add-ten : Integer -> Integer
            add-ten (x) = x + 10

            main : Integer
            main = add-ten (run-state 5 get-state)
            """;
        DiagnosticBag diag = Helpers.TypeCheckWithDiagnostics(source);
        Assert.False(diag.HasErrors, string.Join("; ", diag.ToImmutable()));
    }

    [Fact]
    public void Run_state_cs_emits_get_and_set()
    {
        string source = """
            main : Integer
            main = run-state 0 act
              set-state 42
              get-state
            end
            """;
        string? cs = Helpers.CompileToCS(source);
        Assert.NotNull(cs);
        Assert.Contains("__state =", cs);
        Assert.Contains("__state", cs);
    }

    [Fact]
    public void State_effect_not_allowed_in_pure_function()
    {
        string source = """
            pure-fn : Integer -> Integer
            pure-fn (x) = act
              set-state x
              get-state
            end
            """;
        DiagnosticBag diag = Helpers.TypeCheckWithDiagnostics(source);
        Assert.True(diag.HasErrors);
        Assert.Contains(diag.ToImmutable(), d => d.Code == CdxCodes.EffectNotDeclared);
    }

    // ── User-defined effect handlers ──────────────────────────

    [Fact]
    public void User_effect_declaration_type_checks()
    {
        string source = """
            effect Logger where
              log : Text -> Nothing

            silent : [Logger] Nothing
            silent = log "hello"
            """;
        DiagnosticBag diag = Helpers.TypeCheckWithDiagnostics(source);
        Assert.False(diag.HasErrors, string.Join("; ", diag.ToImmutable()));
    }

    [Fact]
    public void Handle_expression_type_checks()
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
        DiagnosticBag diag = Helpers.TypeCheckWithDiagnostics(source);
        Assert.False(diag.HasErrors, string.Join("; ", diag.ToImmutable()));
    }

    [Fact]
    public void Handle_expression_emits_csharp()
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
        string? cs = Helpers.CompileToCS(source);
        Assert.NotNull(cs);
        Assert.Contains("_handle_log_", cs);
    }

    [Fact]
    public void Handle_eliminates_effect_from_type()
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
        DiagnosticBag diag = Helpers.TypeCheckWithDiagnostics(source);
        Assert.False(diag.HasErrors, string.Join("; ", diag.ToImmutable()));
    }
}
