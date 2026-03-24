using Codex.Core;
using Xunit;

namespace Codex.Types.Tests;

public partial class IntegrationTests
{
    // --- Enhanced linearity checks ---

    [Fact]
    public void Linear_let_forward_is_ok()
    {
        string source =
            "forward : linear FileHandle -> [FileSystem] Nothing\n" +
            "forward (h) = let x = h in close-file x\n";
        DiagnosticBag diag = Helpers.CheckWithLinearity(source);
        Assert.DoesNotContain(diag.ToImmutable(), d =>
            d.Code == "CDX2040" || d.Code == "CDX2041");
    }

    [Fact]
    public void Linear_let_forward_original_not_double_used()
    {
        string source =
            "bad : linear FileHandle -> [FileSystem] Nothing\n" +
            "bad (h) = let x = h in do\n" +
            "  close-file x\n" +
            "  close-file h\n";
        DiagnosticBag diag = Helpers.CheckWithLinearity(source);
        Assert.Contains(diag.ToImmutable(), d => d.Code == "CDX2041");
    }

    [Fact]
    public void Linear_let_forward_unused_produces_error()
    {
        string source =
            "waste : linear FileHandle -> Integer\n" +
            "waste (h) = let x = h in 42\n";
        DiagnosticBag diag = Helpers.CheckWithLinearity(source);
        Assert.Contains(diag.ToImmutable(), d => d.Code == "CDX2040");
    }

    [Fact]
    public void Linear_variable_in_if_branches_must_be_consistent()
    {
        string source =
            "cond-use : linear FileHandle -> Boolean -> [FileSystem] Nothing\n" +
            "cond-use (h) (b) = if b then close-file h else close-file h\n";
        DiagnosticBag diag = Helpers.CheckWithLinearity(source);
        Assert.DoesNotContain(diag.ToImmutable(), d => d.Code == "CDX2042");
    }

    [Fact]
    public void Linear_variable_inconsistent_branches_produces_error()
    {
        string source =
            "bad-branch : linear FileHandle -> Boolean -> [FileSystem] Nothing\n" +
            "bad-branch (h) (b) = if b then close-file h else 0\n";
        DiagnosticBag diag = Helpers.CheckWithLinearity(source);
        Assert.Contains(diag.ToImmutable(), d => d.Code == "CDX2042");
    }

    [Fact]
    public void Linear_variable_used_once_in_match_is_ok()
    {
        string source =
            "use-in-match : linear FileHandle -> Boolean -> [FileSystem] Nothing\n" +
            "use-in-match (h) (b) =\n" +
            "  when b\n" +
            "    if True -> close-file h\n" +
            "    if False -> close-file h\n";
        DiagnosticBag diag = Helpers.CheckWithLinearity(source);
        Assert.DoesNotContain(diag.ToImmutable(), d =>
            d.Code == "CDX2040" || d.Code == "CDX2041");
    }

    // --- Closure capture (CDX2043) ---

    [Fact]
    public void Linear_captured_by_closure_warns()
    {
        string source =
            "capture : linear FileHandle -> (Integer -> [FileSystem] Nothing)\n" +
            "capture (h) = \\x -> close-file h\n";
        DiagnosticBag diag = Helpers.CheckWithLinearity(source);
        Assert.Contains(diag.ToImmutable(), d => d.Code == "CDX2043");
    }

    [Fact]
    public void Linear_not_captured_no_warning()
    {
        string source =
            "direct : linear FileHandle -> [FileSystem] Nothing\n" +
            "direct (h) = close-file h\n";
        DiagnosticBag diag = Helpers.CheckWithLinearity(source);
        Assert.DoesNotContain(diag.ToImmutable(), d => d.Code == "CDX2043");
    }
}
