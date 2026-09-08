# GitHub Update 53

**Scope: main CLs after the Update 52 release push commit.** Update 52
covers the cycle from the Update 51 push (`012a9d2e`, main ~19928)
through its release head (main 20354, seed `61C81B04D0C3CC2E`).
Accumulate this cycle's themes here as they land; every number in the
final report gets re-measured at the release head, not carried forward
(L-COUNT).

## Open from Update 52

- **Steve Howell's second PR is expected against the Update 52 pin**: the
  empty-list carrier, the instance-method span site,
  `subst-type-vars-from-arg`'s learning arms, and `lower-nonempty-list`,
  all measured on his branch and RESERVED for him in the COMPILER-30 row.
  Absorb on arrival; red answered "send all four" on PR 93.
- **COMPILER-27 flag-guard** (ruled: guard in behind a compile-time flag,
  single-operation toggle, measured) shelved in blu's stream (20310);
  lands at MAIN OPEN.
- **COMPILER-32** (lazy-cell lowering records the no-expectation sentinel
  as a real type): fester's, with the `-Atom` noexpect census over the
  full corpus to restart -- lazy-smoke is the one confirmed carrier and
  the other 595 are UNKNOWN, not clean.
- **The battery batch-attribution defect** found at this release's step 1:
  a batch can hand every test in it another test's output, whole. UNOWNED;
  the CurrentPlan row and the `SomethingSeenDuringRelease` Update 52 open
  entry carry the evidence pointers and the one-line re-measure.
- **Heavy-pane stranding ruled: fix the allocator** -- buried heap marks
  become reclaimable; val's campaign after the task frame, the 6.4
  frontier table is the acceptance arm.
- **plugs 1.57's riscv half is a ruling ask** (reek's close-out); queue it
  with its row measurement.
- **The landing integrations lane** (reek, Damian-directed): web surfaces,
  the in-browser compile page emitting binaries and text-plug output.
- **SMP teardown** stays with the `tools/codex-vm.c` claim holder.
- **B4 step 6, NIC-5, A8's metal arm, WORKS-9** ride a sitting or flight;
  **HAL crypto dispatch 2 and 3** blocked on a board manual; **CostModel
  `fixed` rung** unshipped; **CDX4022's message text** is val's
  seed-affecting one-liner.
- **sem-equiv trigger widening** holds until after the release cycle
  (Damian via red, 2026-08-25); proposer reek, unowned after that.

## Landed this cycle

*(accumulates)*

- **Steve Howell's PR 98, absorbed and closed with credit** (reek, dev
  20738, main 20740; register row `codex/plugs/plugs-backlog.md` 2.04):
  the zig plug emitted its whole 37 KB runtime prelude into every program
  and now emits only the parts the program reaches. `zig-prelude` becomes
  `zig-prelude-parts`, 96 named `ShakePart` rows; reachability is generic
  and lands as a new foreword chapter, `codex/foreword/core/Shake.codex`.
  This is the larger half row 2.01 named and left unclaimed.

  Measured here rather than carried: emitted bytes 227,425 to 122,453 over
  five subjects, **46.2 per cent off**, about 20 KB per program, programs
  keeping 33 to 48 of the 96 parts. Behaviour inert against a rebuilt
  depot-revision control over 22 subjects graded on `codex/test/*.expected`
  (16 pass, 6 refused, same 16 and same 6 on both arms; the six are
  pre-existing plug gaps that name themselves). Surface check green, zig
  oracle 55/55.

  It carries a real defect of its own, **Finding 67**: the prelude-surface
  check's parameter regex read past `fn NAME` and dropped the name, so it
  covered 22 of the prelude's 96 declarations and none of its 74 functions.
  Verified here independently at 74 fn + 22 const/var. `CxList` and
  `CxFn1..CxFn4` are CamelCase like any Codex type name, so an ordinary
  program picking one emitted a duplicate struct member and would not
  compile. The check now requires each emitted prelude to be a
  SUB-SELECTION of one known whole in table order, which also catches
  reordering, duplication, truncation and invention.

  **Not seed-affecting**, measured against the compiler unit rather than
  assumed: only `ZigEmitter` cites `Shake`, and the unit carries neither it
  nor `TextSearch` (`Foreword--Sort` present as the calibration). No build
  token was taken. PR 98's third file, the three `is NoExpectTy` arms in
  `Lowering.codex`, had already landed as fester 20398 (the PR 96 finding),
  so that hunk was dropped rather than reapplied.

- **TechnicalDetails seed digests re-measured** (reek, main 20742) for the
  seed red landed at 20736. All four seed claims named the previous seed,
  so `check-doc-counts` was red at head and every lane inherited it through
  merge-down. It is a gate preflight, which is what makes a stale digest
  everyone's problem rather than the release's.

- **The Prism fleet day (2026-08-28, Damian's direction, all lanes).**
  Prism became the local dev environment in one day, each piece landed
  gate-green and most of it live on cobblestoneproject.com the same
  afternoon:
  - **The compiler as a preset** (red 20592): the whole concatenated
    source embedded beside the modules, taking the project to itself;
    big-file guards (8 MB storage caps, plain highlighter past 512 KB,
    5,000-line paint cap, deck ladder entering at 125 past 1 MB).
    Subsumes the self-compile page's purpose; retirement waits on the
    TEXT identity anchor.
  - **Compliance evidence from the Binary tab** (red 20600): built from
    the CDX header itself; root cause was the wasm runtime's silent
    256-unit read-line cap (raised to 4096; x86 measured unbounded).
  - **The CDX target** (red 20621) and the **binary save path**.
  - **Stage 4, the Claude panel** (val, main 20580/20604): SSE wire, key
    handling, the dock, six tools over the real project behind one
    injectable transport.
  - **Native plugs as wasm modules and the native-build configs** (reek,
    20681/20697/20702/20712/20723): riscv and arm64 wires byte-identical
    to bare metal; config UI, the local bridge, and a working C# preset
    -- Damian built and ran a C# app from the page at the browser.
    **Bench** (reek 20726): twelve of thirteen `bench/codex` programs as
    page examples, timed in-tab and over the bridge.
  - **Fat16 long filenames** (fester, main 20684), with a status-unread
    defect in the record writes caught pre-landing by the write-status
    convention find below.
  - **Stage 2, cite resolution, the whole mechanism** (red + fester,
    converged after parallel probes): **Device.Block grounds in linear
    memory** (red 20689 -- `disk_reserve` host contract, reads and
    writes against the host-provided image, the scope pair grounded as
    one unscoped process; the write-status convention is x86's 0=success,
    and the first build inverting it produced every byte correct with the
    verdict FAILED); **the library image** (fester 20720); and the
    **`RESOLVE` compiler mode** (red 20736): source on stdin, cites
    resolved from the mounted volume, the resolved unit answered between
    `RESOLVE-BEGIN`/`RESOLVE-END` with one `CITE-MISSING` line per absent
    chapter (fester's measured refinement -- the first cascade rationale
    died by their own control measurement). Proven end to end: DISK mode
    in-tab compiles `SOURCE.SRC` off a linear-memory volume and writes
    `OUT.CDX` back byte-identical to the stdin compile, dug out by an
    independent test-side FAT16 referee; fester's image resolves cites
    off the volume (CDX3007 without it, the control).
  - **The design register** (`PrismDevEnvironment.md`) carries the
    convergence, both probes' criteria and results, the boards survey
    (two real chains, seven honest absences), and stages 2c-2f.
- **COMPILER-32 step 3 landed and partially reopened, correctly**
  (blu 20754 promote; revert of the one culpable edit at 20760/20762
  after this release's battery caught it -- the account is in the
  release section below). `lazy-smoke` carries `noexpect` at fn[2]
  again; the remaining piece waits on a RUNNING arm, not a census.
- **VM admission** (fester): `Get-VmAdmittedSlots` at the fan-outs,
  budget measured (1,100 MB per guest against the 3,072 ask), wired into
  deck-headroom and the battery phases. The register carries red's
  four-kill census separating the launch-shape mechanism (session
  teardown of background shells) from real contention.

## The Update 53 release (2026-08-28 evening)

Called by Damian at head 20755; one real blocker found and fixed at
step 1; every proof green at the final head against seed
`B066CEB5FE8FC9E8` (3,064,878 bytes).

**The blocker, and it is the battery doing its job:** `lazy-smoke` newly
red -- `call`/`memo1`/`memo2` answering heap addresses for expected
values, `variant:` empty: unforced thunk pointers escaping as values.
Reproduced solo against the then-head seed; attributed by control (green
on the pre-promote seed, red on it); narrowed by blu to one of 20754's
three `lower-lazy` edits (lam-ty) by single-edit ablation; reverted,
verified across all fourteen arc chapters, landed 20762. **L-NOGATE's
third recorded instance**: the promoting gates COMPILED the chapter and
the arc's acceptance instrument was a wire census -- both blind to a
runtime defect by construction; only the battery RUNS it, and only a
release runs the battery.

**The proofs, all at the release head against `B066CEB5FE8FC9E8`:**

| proof | result |
|---|---|
| Full gate (every phase) | green, 1,066.4 s |
| Sut === seed | whole-file hash equal, `B066CEB5` |
| ir-fidelity `-Grade` | green, 17 s, unexpected 0 |
| Battery `-Tier all` | 1,687 total, **0 fail**, 50 skip; delta vs prior run: exactly one change, `lazy-smoke` FAIL -> PASS |
| App sweep | 265 clean, 5 known-dirty, 0 regressions |
| Poison (`-Poison`, `-Tier all`) | 0 newly red; kernel restored |
| DDC witness | **HOLDS**: both arms 3,064,878 bytes, 96 differing all inside the signature region, 0 outside |
| Map | refreshed from the gate's own `Sut.map`; **5,460 of 5,460 rows** match the seed's embedded MAP1 by name, address and size |
| Img | `seed/Codex.img` rebuilt, SHA-256 `6009B76E59DD042B9CE9889100D5040F5ABE71E6923FE53C3F824A1C0B0E0F04` |
| Diag | rebuilt against the release seed, FULL rehearsal (46 arms, both beds) appended `6152B28629672C4B571677A6FD7CCDA97DFD3A5DCF2478DAA6A5365F8511E2A1` to `diag.rehearsed` |
| `check-shipping-images` | OK |
| `check-doc-counts` | 63 claims, 0 drifted after the img and diag digest refresh |

The proofs ran as three detached chains (each phase starting itself when
the previous ended), which is also what let them survive the day's
session-teardown kills: the one launch shape that never died was a
process with no session parent.
