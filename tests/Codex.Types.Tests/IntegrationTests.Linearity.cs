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
            d.Code == CdxCodes.LinearUnused || d.Code == CdxCodes.LinearUsedTwice);
    }

    [Fact]
    public void Linear_let_forward_original_not_double_used()
    {
        string source =
            "bad : linear FileHandle -> [FileSystem] Nothing\n" +
            "bad (h) = let x = h in act\n" +
            "  close-file x\n" +
            "  close-file h\n" +
            "end\n";
        DiagnosticBag diag = Helpers.CheckWithLinearity(source);
        Assert.Contains(diag.ToImmutable(), d => d.Code == CdxCodes.LinearUsedTwice);
    }

    [Fact]
    public void Linear_let_forward_unused_produces_error()
    {
        string source =
            "waste : linear FileHandle -> Integer\n" +
            "waste (h) = let x = h in 42\n";
        DiagnosticBag diag = Helpers.CheckWithLinearity(source);
        Assert.Contains(diag.ToImmutable(), d => d.Code == CdxCodes.LinearUnused);
    }

    [Fact]
    public void Linear_variable_in_if_branches_must_be_consistent()
    {
        string source =
            "cond-use : linear FileHandle -> Boolean -> [FileSystem] Nothing\n" +
            "cond-use (h) (b) = if b then close-file h else close-file h\n";
        DiagnosticBag diag = Helpers.CheckWithLinearity(source);
        Assert.DoesNotContain(diag.ToImmutable(), d => d.Code == CdxCodes.LinearInconsistentBranches);
    }

    [Fact]
    public void Linear_variable_inconsistent_branches_produces_error()
    {
        string source =
            "bad-branch : linear FileHandle -> Boolean -> [FileSystem] Nothing\n" +
            "bad-branch (h) (b) = if b then close-file h else 0\n";
        DiagnosticBag diag = Helpers.CheckWithLinearity(source);
        Assert.Contains(diag.ToImmutable(), d => d.Code == CdxCodes.LinearInconsistentBranches);
    }

    [Fact]
    public void Linear_variable_used_once_in_match_is_ok()
    {
        string source =
            "use-in-match : linear FileHandle -> Boolean -> [FileSystem] Nothing\n" +
            "use-in-match (h) (b) =\n" +
            "  when b\n" +
            "    is True -> close-file h\n" +
            "    is False -> close-file h\n";
        DiagnosticBag diag = Helpers.CheckWithLinearity(source);
        Assert.DoesNotContain(diag.ToImmutable(), d =>
            d.Code == CdxCodes.LinearUnused || d.Code == CdxCodes.LinearUsedTwice);
    }

    // --- Closure capture (CDX2043) — promoted to error, with safe patterns ---

    [Fact]
    public void Linear_captured_by_naked_closure_errors()
    {
        // Naked closure (not in let, not directly applied) — CDX2043 error.
        string source =
            "capture : linear FileHandle -> (Integer -> [FileSystem] Nothing)\n" +
            "capture (h) = \\x -> close-file h\n";
        DiagnosticBag diag = Helpers.CheckWithLinearity(source);
        Diagnostic cdx2043 = Assert.Single(diag.ToImmutable(), d => d.Code == CdxCodes.LinearCapturedByClosure);
        Assert.Equal(DiagnosticSeverity.Error, cdx2043.Severity);
    }

    [Fact]
    public void Linear_not_captured_no_error()
    {
        string source =
            "direct : linear FileHandle -> [FileSystem] Nothing\n" +
            "direct (h) = close-file h\n";
        DiagnosticBag diag = Helpers.CheckWithLinearity(source);
        Assert.DoesNotContain(diag.ToImmutable(), d => d.Code == CdxCodes.LinearCapturedByClosure);
    }

    [Fact]
    public void Linear_closure_let_used_once_ok()
    {
        // let f = \x -> close-file h in f 42 — safe pattern.
        // Let-binding recognizes the capture, consumes h, makes f linear.
        // f used once — no CDX2040/2041/2043.
        string source =
            "safe : linear FileHandle -> [FileSystem] Nothing\n" +
            "safe (h) = let f = \\x -> close-file h\n" +
            "  in f 42\n";
        DiagnosticBag diag = Helpers.CheckWithLinearity(source);
        Assert.DoesNotContain(diag.ToImmutable(), d =>
            d.Code == CdxCodes.LinearUnused || d.Code == CdxCodes.LinearUsedTwice || d.Code == CdxCodes.LinearCapturedByClosure);
    }

    [Fact]
    public void Linear_closure_let_unused_errors()
    {
        // let f = \x -> close-file h in 42 — f never called, h leaked via f.
        // CDX2040 fires for f (the linear closure binding).
        // No CDX2043 — the let pattern is recognized, just unused.
        string source =
            "waste : linear FileHandle -> Integer\n" +
            "waste (h) = let f = \\x -> close-file h\n" +
            "  in 42\n";
        DiagnosticBag diag = Helpers.CheckWithLinearity(source);
        Assert.Contains(diag.ToImmutable(), d => d.Code == CdxCodes.LinearUnused);
        Assert.DoesNotContain(diag.ToImmutable(), d => d.Code == CdxCodes.LinearCapturedByClosure);
    }

    [Fact]
    public void Linear_closure_let_used_twice_errors()
    {
        // let f = \x -> close-file h in (f 1; f 2) — f used twice, double-close.
        // CDX2041 fires for f. No CDX2043 — let pattern recognized.
        string source =
            "bad : linear FileHandle -> [FileSystem] Nothing\n" +
            "bad (h) = let f = \\x -> close-file h\n" +
            "  in let a = f 1\n" +
            "  in f 2\n";
        DiagnosticBag diag = Helpers.CheckWithLinearity(source);
        Assert.Contains(diag.ToImmutable(), d => d.Code == CdxCodes.LinearUsedTwice);
        Assert.DoesNotContain(diag.ToImmutable(), d => d.Code == CdxCodes.LinearCapturedByClosure);
    }

    [Fact]
    public void Linear_closure_direct_apply_ok()
    {
        // (\x -> close-file h) 42 — immediate application, closure never escapes.
        // No CDX2040/2041/2043.
        string source =
            "immediate : linear FileHandle -> [FileSystem] Nothing\n" +
            "immediate (h) = (\\x -> close-file h) 42\n";
        DiagnosticBag diag = Helpers.CheckWithLinearity(source);
        Assert.DoesNotContain(diag.ToImmutable(), d =>
            d.Code == CdxCodes.LinearUnused || d.Code == CdxCodes.LinearUsedTwice || d.Code == CdxCodes.LinearCapturedByClosure);
    }

    // --- Step 4: Higher-order linear callbacks ---

    [Fact]
    public void Linear_callback_parameter_used_once_ok()
    {
        // Function declares a linear callback parameter — must call it exactly once.
        string source =
            "apply-once : linear (Integer -> Integer) -> Integer\n" +
            "apply-once (f) = f 42\n";
        DiagnosticBag diag = Helpers.CheckWithLinearity(source);
        Assert.DoesNotContain(diag.ToImmutable(), d =>
            d.Code == CdxCodes.LinearUnused || d.Code == CdxCodes.LinearUsedTwice);
    }

    [Fact]
    public void Linear_callback_parameter_unused_errors()
    {
        // Linear callback parameter never called — CDX2040.
        string source =
            "ignore-callback : linear (Integer -> Integer) -> Integer\n" +
            "ignore-callback (f) = 42\n";
        DiagnosticBag diag = Helpers.CheckWithLinearity(source);
        Assert.Contains(diag.ToImmutable(), d => d.Code == CdxCodes.LinearUnused);
    }

    [Fact]
    public void Linear_callback_parameter_used_twice_errors()
    {
        // Linear callback parameter called twice — CDX2041.
        string source =
            "double-call : linear (Integer -> Integer) -> Integer\n" +
            "double-call (f) = let a = f 1 in f 2\n";
        DiagnosticBag diag = Helpers.CheckWithLinearity(source);
        Assert.Contains(diag.ToImmutable(), d => d.Code == CdxCodes.LinearUsedTwice);
    }

    [Fact]
    public void Linear_closure_passed_to_linear_param_ok()
    {
        // The key Step 4 test: passing a lambda that captures a linear variable
        // to a function whose parameter is declared linear.
        // apply-once guarantees exactly-once use, so h is consumed safely.
        string source =
            "apply-once : linear (Integer -> [FileSystem] Nothing) -> [FileSystem] Nothing\n" +
            "apply-once (f) = f 42\n" +
            "use-it : linear FileHandle -> [FileSystem] Nothing\n" +
            "use-it (h) = apply-once (\\x -> close-file h)\n";
        DiagnosticBag diag = Helpers.CheckWithLinearity(source);
        Assert.DoesNotContain(diag.ToImmutable(), d =>
            d.Code == CdxCodes.LinearUnused || d.Code == CdxCodes.LinearUsedTwice || d.Code == CdxCodes.LinearCapturedByClosure);
    }

    [Fact]
    public void Linear_closure_passed_to_non_linear_param_errors()
    {
        // Passing a linear-capturing lambda to a non-linear parameter — CDX2043.
        // The function doesn't guarantee single-use.
        string source =
            "maybe-call : (Integer -> [FileSystem] Nothing) -> [FileSystem] Nothing\n" +
            "maybe-call (f) = f 42\n" +
            "unsafe : linear FileHandle -> [FileSystem] Nothing\n" +
            "unsafe (h) = maybe-call (\\x -> close-file h)\n";
        DiagnosticBag diag = Helpers.CheckWithLinearity(source);
        Assert.Contains(diag.ToImmutable(), d => d.Code == CdxCodes.LinearCapturedByClosure);
    }

    [Fact]
    public void Linear_closure_to_curried_linear_param_ok()
    {
        // Curried function: second parameter is linear callback.
        // with-resource "path" (\h -> close-file h) should be safe.
        string source =
            "with-resource : Text -> linear (Integer -> [FileSystem] Nothing) -> [FileSystem] Nothing\n" +
            "with-resource (name) (f) = f 42\n" +
            "use-resource : linear FileHandle -> [FileSystem] Nothing\n" +
            "use-resource (h) = with-resource \"path\" (\\x -> close-file h)\n";
        DiagnosticBag diag = Helpers.CheckWithLinearity(source);
        Assert.DoesNotContain(diag.ToImmutable(), d =>
            d.Code == CdxCodes.LinearUnused || d.Code == CdxCodes.LinearUsedTwice || d.Code == CdxCodes.LinearCapturedByClosure);
    }
}
