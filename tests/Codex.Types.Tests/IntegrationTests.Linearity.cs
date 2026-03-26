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
    public void Linear_captured_by_closure_is_error()
    {
        string source =
            "capture : linear FileHandle -> (Integer -> [FileSystem] Nothing)\n" +
            "capture (h) = \\x -> close-file h\n";
        DiagnosticBag diag = Helpers.CheckWithLinearity(source);
        Diagnostic cdx2043 = Assert.Single(diag.ToImmutable(), d => d.Code == "CDX2043");
        Assert.Equal(DiagnosticSeverity.Error, cdx2043.Severity);
    }

    [Fact]
    public void Linear_not_captured_no_error()
    {
        string source =
            "direct : linear FileHandle -> [FileSystem] Nothing\n" +
            "direct (h) = close-file h\n";
        DiagnosticBag diag = Helpers.CheckWithLinearity(source);
        Assert.DoesNotContain(diag.ToImmutable(), d => d.Code == "CDX2043");
    }

    [Fact]
    public void Linear_closure_let_used_once_ok()
    {
        // let f = \x -> close-file h in f 42 — closure used exactly once
        string source =
            "use-once : linear FileHandle -> [FileSystem] Nothing\n" +
            "use-once (h) = let f = \\x -> close-file h in f 42\n";
        DiagnosticBag diag = Helpers.CheckWithLinearity(source);
        // CDX2043 fires (closure captures linear), but f is used once so CDX2040/2041 don't.
        Assert.Contains(diag.ToImmutable(), d => d.Code == "CDX2043");
        Assert.DoesNotContain(diag.ToImmutable(), d => d.Code == "CDX2040");
        Assert.DoesNotContain(diag.ToImmutable(), d => d.Code == "CDX2041");
    }

    [Fact]
    public void Linear_closure_let_unused_produces_error()
    {
        // let f = \x -> close-file h in 42 — closure never called, h leaked
        // CDX2043 fires for the capture; CDX2040 fires for f (the linear closure) being unused.
        // h itself is counted as consumed by the capture (obligation transfers to f).
        string source =
            "waste : linear FileHandle -> Integer\n" +
            "waste (h) = let f = \\x -> close-file h in 42\n";
        DiagnosticBag diag = Helpers.CheckWithLinearity(source);
        Assert.Contains(diag.ToImmutable(), d => d.Code == "CDX2043");
        Assert.Contains(diag.ToImmutable(), d =>
            d.Code == "CDX2040" && d.Message.Contains("'f'"));
    }

    [Fact]
    public void Linear_closure_let_used_twice_produces_error()
    {
        // let f = \x -> close-file h in (f 1, f 2) — closure called twice
        string source =
            "double : linear FileHandle -> [FileSystem] Nothing\n" +
            "double (h) = let f = \\x -> close-file h in do\n" +
            "  f 1\n" +
            "  f 2\n";
        DiagnosticBag diag = Helpers.CheckWithLinearity(source);
        Assert.Contains(diag.ToImmutable(), d => d.Code == "CDX2043");
        Assert.Contains(diag.ToImmutable(), d => d.Code == "CDX2041");
    }

    [Fact]
    public void Linear_closure_direct_apply_ok()
    {
        // (\x -> close-file h) 42 — lambda applied immediately, no escape
        string source =
            "direct-apply : linear FileHandle -> [FileSystem] Nothing\n" +
            "direct-apply (h) = (\\x -> close-file h) 42\n";
        DiagnosticBag diag = Helpers.CheckWithLinearity(source);
        Assert.DoesNotContain(diag.ToImmutable(), d => d.Code == "CDX2043");
    }
}
