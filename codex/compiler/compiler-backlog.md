# Compiler -- open capabilities

Quire-domain backlog, same rules as the app registers: an entry says
what is still missing and nothing else, a closed entry is DELETED, and
a gap that is still real is never quietly dropped. There is no
platform-wide register; `docs/PM/CurrentPlan.md` carries the shape.

A deletion is proven seed-neutral only by the BINARY, never assumed: build the
candidate from the depot seed and compare it to that seed byte for byte outside
the signature window (offsets 40..135). COMPILER-64 came out identical and
shipped no seed; COMPILER-23's four unreferenced functions came out 228 bytes
smaller (main 24438), so code nothing references can still be present in the
shipped seed, and a removal that assumes otherwise lands a seed it never proved.

**Structural equality is minted in ONE place and every backend receives it.**
`eq-attach-helpers` (`opening.codex`) appends `eq-helper-defs` to the shared
IR, on both the x86-64 path and the IR wire the plugs consume, which is what
COMPILER-44 fixed after sending them to the emitter alone left every plug
comparing heap pointers. So a gap in derived equality is fixed once, in
`Emit/X86_64.codex` where the helpers are built, and NOT four times; an
estimate of four backend implementations was made and measured wrong on
2026-09-08. What decides whether a comparison reaches a helper at all is
`lower-eq-dispatch` (`IR/Lowering.codex`): it resolves the operand, takes the
type's name and its site actuals, and calls `__eq_<name>@<actuals>`; anything
it cannot name falls through to `IrBinary OpEq`, which for a heap value
compares the two POINTERS. That fallback is what made COMPILER-74 and
COMPILER-75 silent wrong answers rather than diagnostics, and it is the first
thing to check when a comparison answers False for two equal values.

| # | Capability | State of the gap |
|---|---|---|
| COMPILER-103 | **Generic `==` has no equality to call: it is refused, not dispatched.** | `==` and `/=` on a value whose type is, or holds, a type variable of the definition's own signature are refused at the checker (CDX2100, `check-eq-on-declared-vars` in `Types/TypeChecker.codex`; `codex/test/errors/poly-eq-text`, `poly-eq-list`), where they compiled and compared two words, which for Text, a list, a vector or a record are their addresses. The refusal also rejects the instantiations that were right, such as `same 7 7`. **Missing:** real generic equality, dictionary-passed so a generic `==` works at every type, Integer included (`docs/Designs/Active/Compiler/GenericEquality.md`); when it lands, CDX2100 stays for a signature without the constraint and its message names the constraint as the fix. After Update 63. **val** (root 2026-09-25). |
| COMPILER-17 | **The arm64 and riscv boot grants refuse a capability at bit 31 or above.** Both emit `[UNSUPPORTED] boot capability grant` and no grant (`a64-add-shadow-warning` in `codex/plugs/arm64/Arm64Runtime.codex`, `rv-add-shadow-warning` in `codex/plugs/riscv/RiscVRuntime.codex`), because `a64-emit-li` and `rv-emit-li` are unmeasured for a mask that wide. `Capability.codex` assigns bits 0 to 30 (`cap-rng` is 30), so the next capability lands on bit 31 and compiles for x86-64 only until these two are measured. The x86-64 side is graded: `codex/test/ops/cap-grant-emit` runs `emit-grant-cap-mask`, `emit-grant-capability` and `emit-revoke-capability` at bits 31 and 40 through a one-qword x86-64 interpreter, with the pre-fix `or-ri` form as its control, and `codex/test/ops/cap-word-64` grades the loader's 64-bit round trip. Owner: root (HAL), with the first bit-31 capability. |


