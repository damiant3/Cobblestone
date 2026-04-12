using Codex.Core;
using Xunit;

namespace Codex.Types.Tests;

public partial class IntegrationTests
{
    [Fact]
    public void Map_with_pure_function_type_checks()
    {
        string source =
            "double : Integer -> Integer\n" +
            "double (x) = x + x\n\n" +
            "result = map (double) [1, 2, 3]\n";
        DiagnosticBag diag = Helpers.TypeCheckWithDiagnostics(source);
        Assert.False(diag.HasErrors, string.Join("; ", diag.ToImmutable()));
    }

    [Fact]
    public void Map_with_effectful_function_propagates_effects()
    {
        string source =
            "log-all : [Console] (List Nothing)\n" +
            "log-all = map (print-line) [\"a\", \"b\"]\n";
        DiagnosticBag diag = Helpers.TypeCheckWithDiagnostics(source);
        Assert.False(diag.HasErrors, string.Join("; ", diag.ToImmutable()));
    }

    [Fact]
    public void Effect_polymorphic_user_function_type_checks()
    {
        string source =
            "apply : (a -> [e] b) -> a -> [e] b\n" +
            "apply (f) (x) = f (x)\n";
        DiagnosticBag diag = Helpers.TypeCheckWithDiagnostics(source);
        Assert.False(diag.HasErrors, string.Join("; ", diag.ToImmutable()));
    }

    [Fact]
    public void Effect_row_variable_shared_across_type_signature()
    {
        string source =
            "apply-twice : (a -> a) -> a -> a\n" +
            "apply-twice (f) (x) = f (f (x))\n\n" +
            "use-apply : (a -> [e] b) -> a -> [e] b\n" +
            "use-apply (g) (y) = g (y)\n";
        DiagnosticBag diag = Helpers.TypeCheckWithDiagnostics(source);
        Assert.False(diag.HasErrors, string.Join("; ", diag.ToImmutable()));
    }

    [Fact]
    public void Concrete_effect_with_row_variable_type_checks()
    {
        string source =
            "log-and-apply : (a -> [e] b) -> a -> [Console, e] b\n" +
            "log-and-apply (f) (x) = do\n" +
            "  print-line \"applying\"\n" +
            "  f (x)\n";
        DiagnosticBag diag = Helpers.TypeCheckWithDiagnostics(source);
        Assert.False(diag.HasErrors, string.Join("; ", diag.ToImmutable()));
    }

    [Fact]
    public void Disallowed_effect_without_row_variable_produces_error()
    {
        string source =
            "pure-only : Integer -> Integer\n" +
            "pure-only (x) = do\n" +
            "  print-line \"oops\"\n" +
            "  x\n";
        DiagnosticBag diag = Helpers.TypeCheckWithDiagnostics(source);
        Assert.True(diag.HasErrors);
        Assert.Contains(diag.ToImmutable(), d => d.Code == CdxCodes.EffectNotDeclared);
    }
}
