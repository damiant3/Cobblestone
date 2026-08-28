# GitHub Update 52

**Scope: main CLs after the Update 51 release push commit.** Update 51
covers the cycle from main 19784 through its release head. Accumulate this
cycle's themes here as they land; every number in the final report gets
re-measured at the release head, not carried forward (L-COUNT).

## Open from Update 51

- **Steve Howell's PR 92 is ABSORBED** (red 19961, main 19963): zig plug,
  six emitter defects fixed, two reported open (plugs register 1.84-1.90);
  twelve cross-backend test pairs, every `.expected` re-measured on bare
  metal against seed C3181693 before being trusted, 12 of 12 match. Tweaks
  on top: blu's 19917 `zig-bool-lit-text` replaced by Steve's typed
  `zig-lit-pat-text` (same fix, his dispatches on the pattern type);
  test-file count 1,676 -> 1,688. His corpus headline counts are treated
  as provisional pending his harness error-gate work. **The `a0425e10`
  close was PARTIAL and its message wrong** (found by Steve against the
  mirror): 19963 missed `ZigEmitter.codex` and `plugs-backlog.md` to a
  missed noclobber refusal, so the pushed tree lacked the emitter fixes
  it claimed. Repaired at main 19980 (2026-08-27) with the zig arm
  graded first; correction push `012a9d2e` (2026-08-27, both mirrors)
  carries the emitter, verified on the pushed tree (zig-lit-pat-text
  present, 3315 lines against a0425e10's 3002), and the account went to
  the PR as a comment. The Update 51 addendum carries the full
  correction. CLOSED.
- **COMPILER-29** (an effect op with an argument returns a pointer on
  arm64 and nothing on riscv; cause is an unlifted handler-clause lambda
  on the plug wire, COMPILER-12's declared contract) recorded at 19946,
  unowned.
- **COMPILER-9** in flight (blu): derive the arm64 helper-vs-arm
  precedence table with the bed as judge.
- **Steve's settling measurement** on the phantom-CDX2001 question (his
  ladder-built compiler vs bare metal) reports through the email channel;
  if his zig plug is miscompiling emitted checker code, the reproducer
  routes to the plugs register.
- **De-recurse or trampoline the compiler's emit spine** (plugs 1.14) so
  the wasm page runs in a browser worker: seed-affecting, fester's when
  taken up.
- **sem-equiv trigger widening** released, proposer reek, unowned.
- **SMP teardown** stays with the `tools/codex-vm.c` claim holder.
- **B4 step 6, NIC-5, A8's metal arm, WORKS-9** ride a sitting or flight;
  **HAL crypto dispatch 2 and 3** blocked on a board manual; **CostModel
  `fixed` rung** unshipped; **CDX4022's message text** is val's
  seed-affecting one-liner.
- **Rulings queue**: EMPTY as of 2026-08-27 -- COMPILER-24 ruled SUPPORT
  (blu 20018); COMPILER-27 ruled the same day (guard in behind a
  compile-time flag, single-operation toggle, measured; open work,
  unowned); the walkthrough's other rulings are in CurrentPlan's Pending
  section.

## Landed this cycle

*(accumulates)*

- **Interim mirror push `3942e362` (2026-08-27 afternoon, both mirrors,
  red).** Carries **Steve Howell's PR 93 absorbed with its seed**
  (main 20184, seed 20189, `4341370C`; lambda parameter types carried to
  the IR -- his design, his measurements, our gate and BVT 407/0 on top),
  and the day through main ~20243: COMPILER-29 fixed (blu 20223, arm64
  and riscv follow the lifted handler clause), the IR front doors
  reconciled (blu 20176, `-IrUni` lifts as `-IrCce`, jonquil keeps the
  unlifted view), the **IR-fidelity instrument** (fester 20161/20243 plus
  red's release wiring 20236: 7 cases at push time, unexpected 0, now
  release-gate Step 0c by Damian's ruling), the desk task frame docking
  to any edge with the flick landed (val 20024/20078 and the day's WORKS
  closures), plugs 1.89-1.97 (reek/fester), FRET and zig-customer
  reference notes, and the day's rulings recorded in the registers.
  **PR 93 closed with credit on this commit.** Steve's second PR
  (empty-list carrier, instance-method span, `subst-type-vars-from-arg`
  learning arms, `lower-nonempty-list`) measures against this pin;
  the four items are RESERVED for him in the COMPILER-30 row.

## The Update 52 release (2026-08-27 evening, the corrective run)

**The interim push above carried a seed with none of the release proofs
run.** No battery, no app sweep, no poison build, no DDC witness preceded
`3942e362`; the seed it published was proven only by the standing gate.
This release is the corrective run: every proof executed at the release
head, by red, solo.

**Landed on main after the interim push, through the release head:**

- **COMPILER-30 ErrorTy split** (fester 20316: `ErrorTy` and the new
  `NoExpectTy` are distinct atoms on the wire) with the `NoExpectTy` arm
  swept into all 56 plugs (20327). Its free-invariant claim measured and
  refuted by the **ir-fidelity `-Census`** (fester 20343): 23 of 596
  clean-compiling programs carry `error`, 251 occurrences at one Lowering
  site. **COMPILER-32 opened**: the lazy-cell lowering records the
  no-expectation sentinel as a real type.
- **COMPILER-31**: `show` and compare refuse a function value with
  CDX2052, and the deck-margin runner sees the plug bundles (blu 20340).
- **Seed refreshed** 30C5C194 -> **61C81B04D0C3CC2E** (2,933,240 bytes,
  blu 20346, one-pass fixed point, self-verifies).
- **The csharp plug skips match arms C# refuses** (red 20352): a Codex
  match arm whose pattern an earlier arm fully handles is legal dead code
  on every target, but expands on the IR wire into a duplicate switch arm
  Roslyn refuses as CS8510. 20316's or-pattern arms broke the DDC's C#
  arm at three sites; the emitter now drops shadowed arms, closing the
  class. Compiler source and seed untouched.
- **Prism compile page UI tab**: nine more lenses, 33 lenses over 37
  embedded modules (reek 20331). **Desk campaign 6.7 complete**: a flick
  docks a pill to the edge it points at, each edge carries its own strip
  (val 20210-20305); WORKS-56 band-depth fix (20283). Landing hero gains
  the try-it block (20276).
- **Mindmeld doc consolidation**: fleet memories lifted into the docs
  that own them (20310-20348).
- **Release artifacts** (20353): `seed/Codex.map` refreshed from the
  gate's own sidecar and validated 5,381 of 5,381 rows against the seed's
  embedded MAP1; `seed/Codex.img` rebuilt; `build/boot/diag.img` rebuilt
  against the release seed and fully rehearsed, every arm, both beds.

**The proofs, all at the release head against seed `61C81B04D0C3CC2E`:**

| proof | result |
|---|---|
| Full gate (`build/build.ps1`, every phase) | green, 681.4 s; Sut === depot seed whole-file |
| ir-fidelity `-Grade` | 7 cases, unexpected: 0 |
| Battery `-Tier all` | 1,681 total, 1,631 pass, **0 fail**, 50 skip; oracles green; 0 newly red |
| App sweep | 270 units: 265 clean, 5 known-dirty (baseline), 0 regressions |
| Poison (`-Poison`) | 1,630 pass, 1 red: `smp-preempt` wall-budget under 8-way contention, solo re-run against the same poison seed byte-exact green; no `0xCD` fault anywhere |
| Poison (`-PoisonCompact`) | 1,631 pass, 0 fail (`smp-preempt` passed in-battery) |
| DDC witness | **HOLDS**: both arms 2,933,240 bytes, 0 differing bytes outside the signature region |
| Diag rehearsal | every arm, both beds, green; image `36A7095F` on the rehearsed list |
| `check-shipping-images` | OK, default cfg, no address |
| `check-doc-counts` | 63 claims, 0 drifted after the img/diag hash refresh |
