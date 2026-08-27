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
  graded first; the PR closes with the NEXT mirror push, the one that
  actually carries the emitter. The Update 51 addendum carries the full
  correction.
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
- **Rulings queue**: COMPILER-24 refuse-vs-support for recursive `==`;
  COMPILER-27's hosted-stricter-than-bare-metal deck inversion.

## Landed this cycle

*(accumulates)*
