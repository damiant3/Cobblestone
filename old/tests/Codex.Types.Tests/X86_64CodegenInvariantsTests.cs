using Codex.Core;
using Codex.Emit.X86_64;
using Codex.IR;
using Xunit;

namespace Codex.Types.Tests;

public class X86_64CodegenInvariantsTests
{
    static X86_64Emitter EmitClean()
    {
        IRChapter? ir = Helpers.CompileToIR("opening : Integer\nopening = 42", "clean");
        Assert.NotNull(ir);
        X86_64Emitter emitter = new();
        emitter.EmitAssembly(ir, "clean");
        return emitter;
    }

    [Fact]
    public void Verify_passes_on_well_formed_program()
    {
        X86_64Emitter emitter = EmitClean();
        X86_64CodegenInvariants.Verify(emitter);
    }

    [Fact]
    public void Verify_throws_on_unresolved_call()
    {
        // Hand-built IR where opening calls a function that isn't defined at the
        // top level. Codegen emits a call patch; PatchCalls can't resolve it;
        // Verify turns the dangling patch into a hard failure.
        CodexType fnType = new FunctionType(IntegerType.s_instance, IntegerType.s_instance);
        IRExpr body = new IRApply(
            new IRName("ghost_target", fnType),
            new IRIntegerLit(0L),
            IntegerType.s_instance);
        IRDefinition opening = new("opening", [], IntegerType.s_instance, body);
        IRChapter ir = new(
            QualifiedName.Simple("T"),
            [opening],
            Map<string, CodexType>.s_empty);

        X86_64Emitter emitter = new();
        emitter.EmitAssembly(ir, "bad");

        InvariantViolationException ex = Assert.Throws<InvariantViolationException>(
            () => X86_64CodegenInvariants.Verify(emitter));
        Assert.Equal("codegen", ex.Phase);
        Assert.Contains("ghost_target", ex.Detail);
        Assert.Contains("direct call target", ex.Detail);
    }

    [Fact]
    public void Verify_throws_on_unresolved_funcAddrFixup()
    {
        // Force a fixup-only failure via the test hook. Building an IR that
        // naturally produces an unresolved FuncAddrFixup is impractical (the
        // closure emitter keeps target names consistent with IRChapter.
        // Definitions), so the hook injects a synthetic unresolved entry to
        // exercise the verifier's second branch symmetrically with the call
        // branch above.
        X86_64Emitter emitter = EmitClean();
        emitter.InjectUnresolvedFuncAddrFixupForTesting("phantom_fn");

        InvariantViolationException ex = Assert.Throws<InvariantViolationException>(
            () => X86_64CodegenInvariants.Verify(emitter));
        Assert.Equal("codegen", ex.Phase);
        Assert.Contains("phantom_fn", ex.Detail);
        Assert.Contains("function-address fixup", ex.Detail);
    }

    [Fact]
    public void Verify_reports_both_classes_in_one_exception()
    {
        // Build an IR with an unresolved call AND inject an unresolved fixup.
        // The combined verifier should report both in a single exception so
        // bad runs surface everything in one pass.
        CodexType fnType = new FunctionType(IntegerType.s_instance, IntegerType.s_instance);
        IRDefinition opening = new(
            "opening", [], IntegerType.s_instance,
            new IRApply(new IRName("ghost_call", fnType), new IRIntegerLit(0L), IntegerType.s_instance));
        IRChapter ir = new(QualifiedName.Simple("T"), [opening], Map<string, CodexType>.s_empty);

        X86_64Emitter emitter = new();
        emitter.EmitAssembly(ir, "both");
        emitter.InjectUnresolvedFuncAddrFixupForTesting("phantom_fixup");

        InvariantViolationException ex = Assert.Throws<InvariantViolationException>(
            () => X86_64CodegenInvariants.Verify(emitter));
        Assert.Contains("ghost_call", ex.Detail);
        Assert.Contains("phantom_fixup", ex.Detail);
        Assert.Contains("direct call target", ex.Detail);
        Assert.Contains("function-address fixup", ex.Detail);
    }

    [Fact]
    public void Verify_deduplicates_and_sorts_detail_names()
    {
        // Diagnostic stability: across runs, the ordering of names in the
        // detail string should not depend on insertion order.
        X86_64Emitter emitter = EmitClean();
        emitter.InjectUnresolvedFuncAddrFixupForTesting("zebra");
        emitter.InjectUnresolvedFuncAddrFixupForTesting("apple");
        emitter.InjectUnresolvedFuncAddrFixupForTesting("zebra");  // dup

        InvariantViolationException ex = Assert.Throws<InvariantViolationException>(
            () => X86_64CodegenInvariants.Verify(emitter));
        int appleIdx = ex.Detail.IndexOf("apple", StringComparison.Ordinal);
        int zebraIdx = ex.Detail.IndexOf("zebra", StringComparison.Ordinal);
        Assert.True(appleIdx >= 0 && zebraIdx >= 0);
        Assert.True(appleIdx < zebraIdx, "detail should be sorted");
        Assert.Equal(1, CountOccurrences(ex.Detail, "zebra"));
    }

    static int CountOccurrences(string haystack, string needle)
    {
        int count = 0;
        int idx = 0;
        while ((idx = haystack.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += needle.Length;
        }
        return count;
    }
}
