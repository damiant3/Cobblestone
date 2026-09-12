# GitHub Update 60

**Release of 2026-09-12, the second of the day.** Update 60 lands the five
seed-affecting shelves the fleet had proven but not promoted before Update
59: native nested patterns, declared exports as native roots, concrete
generic equality helpers, a hosted compiler that types comparisons Boolean,
and a guarded deck exit. Every landing below was re-proven by red on the
head seed of its moment; the compiler identity and distribution digests are
in [TechnicalDetails.md](../../../../TechnicalDetails.md#distribution-artifacts).

## Landed changes

- COMPILER-82, from Steve Howell's PR 141: the native x86-64 backend tests and
  binds nested constructor patterns (main 25683, val's shelf 25595 re-proven by
  red). A constructor field now loads into a local and recurses through the
  pattern emitter with one shared failure-patch list, so every nested tag or
  literal mismatch falls through to the next arm before any guard runs; literal
  fields compare at the literal's own type. The PR 141 shared 31-row fixture,
  `native-nested-pattern` and `native-nested-tags` pass on the candidate and are
  refused or misprint at the previous seed; four shallow pattern fixtures compile
  byte-identical. Seed `E29C3BAA42415916`, fixed point, BVT 143/0, signed and
  self-verified. One extra local and load per nested field; no runtime heap.

- COMPILER-39, Steve Howell's issue 110: a native CDX build keeps every definition
  the chapter declares in `wasm-exports` (main 25690, reek's shelf 25592 re-proven
  by red). CDX pruning now takes the declared roots the IR paths already used;
  call-site inlining still happens. `codex/test/cdx-export-check.ps1` grades the
  declared/undeclared x default/text-plug matrix: the previous seed drops both
  declared functions under the default pipeline, the candidate keeps them, prunes
  undeclared and dead code as before, and emits byte-identical IR. Seed
  `38C0F18885368DAA`, fixed point, BVT 143/0, signed and self-verified. Still open:
  no plug but wasm reads the declaration, and no gate runs the check.

- plugs 2.54, from Steve Howell's PR 135 intake: generic equality helpers carry
  the instantiated sum type on their parameters, scrutinees and patterns (main
  25701, blu's shelf 25597 re-proven by red), so `eq-generic-fields` builds
  through the Zig plug: the previous seed's IR fails on undeclared `a_`, the new
  seed's prints the exact 10 lines, and 24 of 24 IR type sites are concrete
  against 0 of 24. Six native equality fixtures stay exact. Seed
  `0669897096EB6CA3`, fixed point, BVT 143/0, signed and self-verified. One
  deck-recorded sum type per instantiated helper, no graph clone. Still open:
  `typeclass-smoke` (T578), with MethodSpecialization.

- Issue 126, Steve Howell: a hosted compiler types the six comparisons Boolean.
  Two landings from reek's shelves, re-proven by red. The Zig plug emits a
  compile-time refusal as an invoked `noreturn` function, so dead unsupported
  code is valid Zig while a reachable refusal still fails compilation (main
  25713; dead, live, conditional, sequence and nested refusal controls plus seven
  exact cumulative outputs). BootPaint's two RTC entry points return the unarmed
  sentinel on the hosted target (main 25719, seed `6B2C7409AA197D37`, fixed point,
  BVT 143/0, signed and self-verified; native clock reads unchanged). Root's
  hosted-comparisons driver then builds through the streaming Zig entry, and lt, le,
  gt, ge, eq and ne are each one `boolean` definition identical to the native
  compiler's, whole IR payload byte-identical. Registered, not fixed: the network
  Zig entry retains a whole program's emission and ran out of memory on that IR
  (plugs 2.58).

- COMPILER-48, Steve Howell's issue 115: a deck exit refuses a nonpositive nesting
  counter before decrementing it (main 25728, reek's shelf 25605 re-proven by red).
  The previous seed let an exit at depth 0 write -1 and keep running; the new code
  traps with the counter unwritten, while balanced and nested enter/exit pairs
  restore the heap and publish the deck position as before
  (`codex/test/deck-exit-check.ps1`, hardware write watch on the counter). Seed
  `CF9EDD812EA7E78B`, fixed point stage 3 == stage 4, BVT 143/0, signed and
  self-verified. 12 bytes per emitted deck exit; the seed grows 0.56 percent. Issue
  115's wider subset and initialization obligations stay open.

## Outside contributions

Steve Howell: issues 110, 115 and 126 close with this update, and PR 135's
and PR 141's remaining halves (the native nested-pattern backend, the Zig
plug's structural refusals) are the work behind two of the landings. Each
landing names its origin in the Perforce changelist; the receipts on the
issues name the changelist and this commit.

## Release verification

Measurements below describe the release artifacts verified on 2026-09-12.

| Proof | Current result |
|---|---|
| Full release gate | Passed in 1,493.9 seconds, one-pass fixed point; 3,082 compile and runtime checks (1,556 run) with zero failures; 293 app units, 290 clean and 3 declared exceptions. |
| IR fidelity | Reader self-test passed; all 3 verdict ablations matched; 9 real cases, zero unexpected verdicts (27 compiles, 18 s). |
| Full normal battery | 1,777 passed, zero failed, 49 declared exclusions (1,826 subjects, 3 new this cycle); every oracle check matched its contract. Compile phase 201 s at three admitted slots, run phase 123 s. A first launch beside the DDC emit was admitted one slot and hit the batch timeout; the run reported here ran with the box to itself. |
| App-class sweep | 290 clean units and 3 declared baseline exceptions, zero regressions. Compilation coverage only. |
| Poison build and full poison battery | 1,777 passed, zero failed, 49 declared exclusions against the 0xCD-fill seed; normal-kernel restoration verified (CF9EDD81). No retries, guest deaths or dropped-byte reports in either battery. |
| Diverse double-compiling | Passed. Both arms produced 3,394,819 bytes, with zero differences outside signature offsets 40..135. |
| Signed seed and symbol map | The gate's one-pass compiler is byte-identical to the depot seed (3,394,819 bytes); all 5,730 sidecar symbols match the embedded MAP1 names, addresses and sizes. |
| Distribution images and shipping checks | Boot image rebuilt; embedded seed and source match the release inputs. Diagnostic image built and rehearsed by red at the release seed: a first 50-arm pass under battery load disagreed on four network arms, all four passed on rerun with the box idle, and a second full pass answered 50 of 50; the shipping check accepts its default configuration. Its payload, bundle and EFI are byte-identical to Update 59's image. |
| Box profile | Sampled free-memory floor 0.81 GiB at 15:10:17 during the gate's test-run phase (4 VM-host processes, 3,787 MiB working set together); 27 of 741 samples under 2 GiB, all in that phase. Peak VM-host process count 16 at 15:18:49 (1,374 MiB together). The profile is `docs/Agents/box-release-2026-09-12b.csv`. |

Both batteries use `-Tier all -Jobs 4`. Declared exclusions are deliberate
selections and are reported apart from unexpected failures.

Diagnostic image SHA-256:
`FE000229F03717A8ABB5BDA6DB9867FE73E2B20CF947B16066DD3568BE2ADD39`.
The image contains no identity and uses the checked-in default configuration.

## Known limits

`typeclass-smoke` still refuses under the Zig plug (T578, an open method use
the finite MethodSpecialization design has not settled); the Zig plug's
network entry retains a whole program's emission and runs out of memory on
the compiler's own IR (`plugs-backlog.md` 2.58). Declared wasm exports are
read by the wasm plug only, and no gate runs the export check. The app sweep
establishes compilation, not runtime behavior.
