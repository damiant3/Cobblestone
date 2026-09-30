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
| COMPILER-109 | **Every compiler line fits in 100 columns** | The code-layout formatter (`docs/Designs/Active/Build/CodeLayout.md`) reflows whitespace only; what it cannot break is its R7 report, a hand rewrite each. Lines over 100 after formatting (2026-09-30): Semantics 109, Ast 275, Core 150 (132 of them `CdxCodes`), Syntax 231, IR 647, Types 610, Emit 670, `opening.codex` and `EntryPoint.codex` 159. `Types/TypeCheckerInference.codex` is unformatted: its prose holds U+22A2, which has no CCE code point. List them with `codex/build/CodeLayoutFormat.codex` given `LAYOUTREPORT` as its first input line. A rewrite changes the binary, so it lands on the seed path. |
