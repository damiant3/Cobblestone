# GitHub Update 59

**Release of 2026-09-12.** Update 59 is the outside-contribution release:
eleven pull requests from two contributors are ingested, five open issues
move, and the corrected decimal parsers that Update 58 promised ship in the
seed and in both hosted plugs. The compiler identity and distribution
digests are in
[TechnicalDetails.md](../../../../TechnicalDetails.md#distribution-artifacts).

## Landed changes

**Real literals, in every parser (issue 125, Steve Howell).** The
bare-metal carry/borrow correction (main 25513), the wasm decimal parser
(25518) and the Zig parser plus a shared oracle (25538) land together, so a
literal reads the same bits on the seed, under wasm and under Zig. The new
parsers agree with the seed and the host across the recorded 30-case
corpus; the old plug parsers, kept as controls, differ on 9 of them. Steve's
five boundary rows from PR 137 are in `codex/test/ops/real-literal-rounding`
(25528) and pass on the shipped seed. Update 58 shipped the pre-correction
seed and the old plug parsers; this release replaces both.

**Generic inlining and record fields (PR 139 and PR 140, Steve Howell).**
`inline-single-caller` keeps the return-type substitution the call site
carried, and `lower-record` hands each field the record's applied type
arguments (25585). The IR fidelity case `inlined-return-type` pins the first;
`codex/test/ops/record-closure-field-poly` pins the second, and the frozen
Zig plug that refused the old lambda binders runs the candidate's four rows.
COMPILER-81 stays open: an unconstrained empty-list element still reaches
typed IR and Zig refuses it, and no default type was added.

**The Zig plug (PR 135, PR 138, PR 141, PR 143, Steve Howell and
apoorvapendse).** Every non-generic payload-carrying variant is boxed, so
`address-of` is total the way it is on bare metal (25656, the runtime face of
issue 126's boxing gap). An unused `let` binds and silences its value instead
of discarding a name zig rejects, in tail position too. A nested constructor,
literal or text sub-pattern is a test rather than a wildcard: the copied work
lists in the contributor's traversal were replaced by a depth-first walk with
a balanced fragment join, measured at n=256 as 1,317,016 bytes against
5,316,256 upstream on the wide shape and 2,549,680 against 61,662,624 on the
deep one, and nested tail calls keep loop emission. The prelude's remainder
local is `cx_frac`, which is what PR 143 asked for. Six exact controls under
`codex/plugs/test-input` and `codex/test/ops` pin the arc.

**The wasm plug (PR 141, wasm half).** Nested constructor tests and bindings
(25578), with lazy guard evaluation after a nested pattern succeeds, which
fixed the expanded control's integer-divide-by-zero trap. All 31 rows of the
shared fixture `codex/plugs/test-input/match-nested-pattern` pass. Declared
wasm exports now survive both IR wires and drive wasm selection (25541,
issue 110); the all-target guarantee stays open as COMPILER-39.

**The parser (PR 142, Steve Howell; COMPILER-84).** A class or instance
declaration is scanned only where the keyword begins its line, so a section
title mentioning `instance Showable Text` no longer declares a phantom
instance (25660). `codex/test/ops/section-title-keywords` pins it.

**Prose and dependencies (issues 120 and 115, Steve Howell).** CDX1074 warns
when an accepted binary expression crosses removed prose (25533); the
warning preserves the AST, the output and legal column-3 code, and the
column/mode rules are now stated in the Developer's Guide (25635). Lexer
cites Syntax Nodes and Syntax Nodes cites ListUtils (25626), closing two of
the four undeclared dependencies; the remaining subset and deck obligations
are COMPILER-48.

**The arm64 plug (PR 144, apoorvapendse).** Mode `IR-CCE darwin` emits
`mmap`, `write` and `exit` through `svc #0x80` with a slab at 8 GiB, and the
prologue and epilogue pair `x27` with `xzr`, because `ldp x27, x27` is
constrained unpredictable and faults on Apple Silicon (25665). The virt wire
is unchanged except that pairing. The Mach-O host wrap stays in the
contributor's repository (`plugs-backlog.md` 2.57): no macOS toolchain enters
the build, and every `darwin` byte is compile-checked only.

**Apps and tooling.** `apps/globe/kernels/GlobeKernels.wgsl` is regenerated
from source and naga 29.0.4 accepts it (PR 136, Steve Howell, 25560). The
eight-queens example prints the first board under the seed (PR 145,
apoorvapendse, 25663). The focused Zig prelude checker keeps singleton
subject arrays and creates nested output parents (25641).

## Outside contributions

Steve Howell: PR 135, PR 136, PR 137, PR 138, PR 139, PR 140, PR 141, PR 142,
and the measurements behind issues 110, 115, 120, 125 and 126. apoorvapendse:
PR 143, PR 144 and PR 145. Each landing names its contributor in the Perforce
changelist; the receipts on the pull requests name the changelist and this
commit.

## Release verification

Measurements below describe the release artifacts verified on 2026-09-12.

| Proof | Current result |
|---|---|
| Full release gate | Passed in 1,083.6 seconds, one-pass fixed point; 3,076 compile and runtime checks (1,553 compiled, 1,523 run) with zero failures; 293 app units, 290 clean and 3 declared exceptions. |
| IR fidelity | Reader self-test passed; all 3 verdict ablations matched; 9 real cases, zero unexpected verdicts (27 compiles, 18.4 s). |
| Full normal battery | 1,774 passed, zero failed, 49 declared exclusions (1,823 subjects, 28 new this cycle); every oracle check matched its contract. Compile phase 597 s at two admitted slots, run phase 189 s. |
| App-class sweep | 290 clean units and 3 declared baseline exceptions, zero regressions. Compilation coverage only. |
| Poison build and full poison battery | 1,774 passed, zero failed, 49 declared exclusions against the 0xCD-fill seed; normal-kernel restoration verified (49070BAE). No retries, guest deaths or dropped-byte reports in either battery. |
| Diverse double-compiling | Passed. Both arms produced 3,374,965 bytes, with zero differences outside signature offsets 40..135. |
| Signed seed and symbol map | The gate's one-pass compiler is byte-identical to the depot seed (3,374,965 bytes); all 5,729 sidecar symbols match the embedded MAP1 names, addresses and sizes. |
| Distribution images and shipping checks | Boot image rebuilt; embedded seed and source match the release inputs. Diagnostic image built and rehearsed by red at the release seed: all 50 arms answered as they should across codex-vm and OVMF, and the shipping check accepts its default configuration. |
| Box profile | Sampled free-memory floor 1.01 GiB at 13:31:30 during the gate's test-run phase (4 VM-host processes, 3,347 MiB working set together, with a 2.3 GB browser idle beside them); 2 of 456 samples under 2 GiB. Peak VM-host process count 16 at 13:38:56 (1,851 MiB together, 116 MiB per process). The DDC emit ran beside the normal battery's two admitted slots. |

Both batteries use `-Tier all -Jobs 4`. Declared exclusions are deliberate
selections and are reported apart from unexpected failures.

Diagnostic image SHA-256:
`884FD218348190808E8F0699A34C7320E78113E7634EE2565F53603009D0BF84`. The image contains no identity and
uses the checked-in default configuration.

## Known limits

The Zig plug still refuses two typeclass programs the seed accepts,
`codex/test/eq-generic-fields` (an undeclared `a_` in a generated equality
signature) and `codex/test/typeclass-smoke` (a type variable not declared at
its site); both fail on the previous plug too and are register rows in
`plugs-backlog.md`. Issue 126's hosted-compiler comparison typing remains
open; the structural-refusal repair is in progress (`plugs-backlog.md`
2.55). PR 141's native fixture refusal is COMPILER-82. The app sweep
establishes compilation, not runtime behavior.
