# GitHub Update 48

**Scope: main CLs after the Release 47 push commit, opened 2026-08-18.** Update 47
covered 16559 to 17236 plus the release's own map, img, README and report CLs.
Accumulate this cycle's themes here as they land; every number in
the final report gets re-measured at the release head, not carried forward.

## Open from Update 47

- **The battery harness can lose bytes from a batch stream.** Carried from
  Update 44, and **seen again 2026-08-20 during this release's poison run**:
  seven subjects reported red, and the artifacts were truncated rather than
  wrong -- `ideas-test` held 204 bytes of a 2,199-byte output and
  `repo-tombstone` held 0. The compiler was not at fault, measured: the
  poison and normal seeds produce `ideas-test` byte-identical. It cost a
  wrong routing to blu before the arithmetic was done, which is the lesson
  L-PARTIAL and L-ASSUME both name -- establish which event you are
  explaining first. A truncated artifact and a miscompile are the same
  colour on the verdict line, and nothing in the harness distinguishes them.
- ~~**Nothing exercises the guard page under a genuine allocation walk.**~~
  CLOSED (root, 17848): a WALK arm in `guard-page-test.ps1` with a
  runaway-by-construction probe, `HEAP=` checked inside the page; first
  flight caught the walk at guard+185 KB, FIRE and CONTROL both green.
- **plugs 1.34, the ARM64 MMIO boundary** (effect row on the device window, or
  EL0): rulings queue 10.
- **B4 step 6** waits on the B3 metal sitting.
- **blu's CDX2064 sibling-argument extension (CL 17122)** was held out of
  Update 47 so the proofs ran once; it lands at MAIN OPEN and moves the seed.
- **rulings queue 15** (HAL Power: linear Board for `sleep-deep`) blocks root's
  last HAL step.
- **The `vm-differential` gate arm has no retry** and reds the whole gate on a
  QEMU timeout (rulings queue 7); it did so once during the Update 47 proofs.
- **My preview gate reported `Sut === stage1` on a pre-convergence stage** (blu
  measured compile(A) != A that night); the gate's fixed-point claim wants a
  look (red, 2026-08-18).
- **Steve Howell's issue 70 and PR 67**: comment with the public commit at the
  Update 47 push (PublicPush.md).

## Landed this cycle

### Interim mirror push, 2026-08-19 (github a061c173, gitlab the same), seed `800A7683`

Not a release: a routine mirror update so Steve Howell could rebase the
zig ladder on everything of his that had been absorbed. It carried main
17243 to 17256: blu's CDX2064 sibling-argument extension and its test (the
seed moved `90646EEB` to `800A7683`), and the three items below. The README
seed digest was re-measured for the seed in the commit (`check-doc-counts`
61/61); `Codex.img` was NOT rebuilt and still embeds `90646EEB`, which the
README's img row states correctly as that file's own hash. Rebuild it at the
Update 48 release as the release skill already says.

- **PR 69 (Steve Howell), main 17251:** `Resolve-PlugForewords` asks presence
  before the registry, so a chapter both hand-listed and cite-resolvable is
  bundled once. Generator and regenerated script together; all 55 bundles
  byte-identical; control bundle with Sort hand-listed drops from 8800 lines
  (Sort twice) to 8742.
- **PR 71 (Steve Howell), main 17254:** the zig prelude allocates through one
  `ArenaAllocator` instead of `page_allocator` per object; his ladder
  measurement 3.0 GB OOM-killed to 238 MB, output byte-identical. Re-applied
  by hand; the `cx_heap` unification he designed is left for his follow-up.
- **Issue 72 (Steve Howell), main 17254:** the zig plug dropped match guards.
  A guarded match is now an if-chain in a labeled block; guardless matches
  byte-identical; the oracle subject carries nine guard rows; zig PASS 49/49.
  The same rows show python, wasm, csharp and typescript drop guards too, and
  typescript mis-emits its first variant type (`never` with no separator):
  `plugs-backlog` 1.46.
- **PR 69's aside, not taken:** `build/test-compile-batch.ps1` passes
  `Start-Process -WindowStyle Hidden` unconditionally, which PowerShell on
  Linux refuses, so `check-generated-scripts.ps1` cannot run there. He offered
  it as its own PR and was told yes.

### Second interim mirror push, 2026-08-19 (github 8f997bd8, gitlab the same), seed `E45B56F1`

Again not a release: main 17257 to 17409 so Steve Howell could rebase on
PR 73 and PR 74. The seed in the commit is blu's `E45B56F1` (main 17299,
CDX2064) and the README digest was re-measured for it (2,849,516 bytes,
content prefix `5504267863C9A2CD`, 1,573 test files). `Codex.img` still
embeds `90646EEB`; rebuild at the release. The commit also carried the
fleet's landings since the first push: plugs 1.46 closed on the six wired
plugs and 1.7 fortran stage 5 (reek), the WORKS-9 sink instrument and
nicring RDH bank in `diag.img` 3209902931 (reek, blu), ModernDesk stages
10-12 (val), GopWizard vitals carrying the USB keyboard state (red),
CostModel's none rung (blu). ProductBuilder stages 0-4 landed on main but
`apps/productbuilder/` and `codex/product/` are gitignored and did not ship.

- **PR 73 (Steve Howell), main 17401:** the zig plug's plain switch with
  literal arms is pinned through `zig-pin-lit-arms` (the chain already was),
  and `__linked-list-empty` consumes its Integer size hint through
  `cx_ll_with_capacity`. Oracle zig 49/49; a plain literal-armed probe emits
  `@as(i64, switch ...)` and runs under zig 0.16.0.
- **PR 74 (Steve Howell), main 17401:** the aside above, taken:
  `-WindowStyle Hidden` only when `$IsWindows`, generator and emitted script
  together; `check-generated-scripts -Only test-compile-batch` match, 0 drift.

## The Update 48 release, 2026-08-20 (RELEASE HEAD, MEASUREMENTS AND SEED TO FILL AT PUSH)

Themes since the second mirror, in the order they matter to a reader:

- **Identity is reconciled end to end (red).** The four-stage campaign closed
  2026-08-20: the wizard's wrap is HKDF's real 32 bytes with a .NET known
  answer the round trip cannot dodge (stage 1); the seed lives in the pinned
  kernel region and every ceremony secret is scrubbed above one mark, since a
  Text built by appending leaves every prefix on the heap (stage 2); the
  bench auto-unlock is bed-only on the hypervisor bit, so metal always asks
  (stage 3); and `IDENTITY.DAT` is version 3 with a 64-byte Ed25519
  self-vouch over the public key, signed at keygen and verified at parse, so
  the record is self-certifying and the file is the persisted trust root
  (stage 4, main 17792). P on the unlocked screen rewraps under a new
  passphrase with fresh salt and iv; v2 and v1 are refused; the vouch binds
  only the public key, so a passphrase change leaves it byte-identical.
- **The desk finished its multitasking conversion (val, fester).** All 15
  ModernDesk stages are done: every pane is a step, the four heavy panes stay
  alive while another app runs, the taskbar carries the live-app row. The
  Review pane reads the medium's fact store, casts signed verdicts through
  the kernel key slot, pages, and wraps long descriptions bounded by
  measured slack (WORKS-44; WORKS-15 closed the editor).
- **The browser register burned down, and its last open defect turned out to
  be a memory-safety find (fester).** BROWSER-1, -2, -6, -7 and BROWSER-5's
  arithmetic half closed; the page is built for the viewport the layout
  actually gives. Then the paint half of BROWSER-5 was run to ground with
  `-hwwatch`: the pane was writing 78 rows -- 499,200 bytes -- past the
  framebuffer end on every paint, silent above the watch rows and absent
  with no Browser open. Fixed at main 17827 with the bounds rule recorded in
  the desk contract (5.1). What remains is the network-gated BROWSER-4.
- **The HAL's Power phase closed on ruling 15 = (a): the Board is linear
  throughout (root).** The foreword re-signs the five opens plus
  sleep-deep/board-open, all nine board chapters thread it, and sleep-deep
  proves no handle is open by construction. ADC threading landed the same
  cycle (main 17777): ADC1 on the STM32s, SAADC on both nRF boards, five
  boards carrying `AdcUnit`. (CL numbers for the linear Board land with it.)
- **The plugs close-out (reek, fleet).** Text builtins 1.31/1.36/1.37 closed
  with the inline-arm sweep across 16 text plugs; 1.46 match guards on the
  six wired plugs; the three .NET UI plugs were compiled for the first time
  and their defects named (1.51).
- **codex-vm disk writes are 10x faster (blu, main 17796).** `ide_flush`
  reopened the image per sector: 62.5 of 68 seconds. Now 6 s; the remainder
  is the guest's one-exit-per-word ATA loop, recorded unowned.
- **bounded and punctual accept `&`-joined conditions (blu, seed 17751; the
  regression it carried was caught by this release's battery and fixed at
  17822).** `infer-and` records its boolean arm; the punctual checker now
  reads WHAT the expr-type table recorded rather than whether it recorded.
  The battery caught `is-power-of-two` refused as concatenation under the
  first cut; the negative control (`punctual-text-append`) still refuses all
  three of its shapes.
- **Steve Howell's zig plug chain (PRs 69, 71, 73, 74, 75 and issue 72).**
  PR 75 landed in this release (main 17806): the full CCE tier tables in the
  prelude, char-to-text as one CCE frame, and the heap base and deck made
  real after our own oracles caught the hosted arm booting its bump pointer
  at 0. His census: 566 depot programs through the hosted arm. His aside
  about the csharp plug sharing the zero-base defect is verified and
  recorded (plugs-backlog 1.52).

### Release measurements (measured at head 17833, seed `930FF7F1`)

- Battery `-Tier all -Jobs 8`: 1587 total, 1541 pass, 0 fail, 46 skip;
  oracles scalar 2013/2013, vector 130/130, CCE 1485/1516 with 31 in
  documented gaps and 0 unexplained. The first run at the pre-fix head went
  red 8: four board-driver sidecars missing the runner's trailing LF (fixed
  17815) and four punctual/foreword compile failures from the 17751 seed's
  infer-and cut refusing `is-power-of-two`'s boolean `&` (bisected to the
  seed, fixed by blu at 17822; the negative control still refuses).
- App sweep `-Check -Jobs 8`: exit 0, clean against the baseline.
- Poison build (0xCD fill, `-Tier all -Jobs 8`): 1587 total, 1541 pass, 0
  fail, 46 skip. The first run reported 7 red and every one was a TRUNCATED
  artifact rather than a wrong one; the re-run cleared all 7 with no change
  to the tree. Working kernel restored and verified after both runs.
- DDC: both arms 2,872,563 bytes against the seed's 2,872,563, 96 differing
  bytes all inside the signature region at 40..135 and **0 outside**.
  WITNESS HOLDS. Prerequisites were rebuilt on the audited seed first (the
  gate wipes `build-output`, and a stale arm certifies the wrong compiler).
- seed/Codex.cdx: 2,872,563 bytes, SHA-256
  `930FF7F174E1E126DB95218B5FAA9FC325A030B6CB861691694FCD956568614D`, MD5
  `8AC2CB7FD7B5FAC78713F34ABFA5E445`, content prefix `CAEED9D6F81BB571`
- seed/Codex.img: 16,777,216 bytes, SHA-256
  `9221657C3525FD54B48EDA9DFF7792BD9CA7A14B1B6CD24CEB2AFBA91E3DA5E9`
- build/boot/diag.img: 16,777,216 bytes, SHA-256
  `130C07F0C7ED32C5AE7C3A91FCFDA9166DD126321B9575BD05D6A2C3B8E88D46`,
  id `60a40350265b0e2c`, rehearsed across all 20 arms in both beds
  (codex-vm and OVMF) and recorded in `build/boot/diag.rehearsed`, so
  `flash-usb.ps1 -Rehearsed` accepts it.
- seed/Codex.map: refreshed from the release build's `Sut.map`.
- Public commits: github <sha>, gitlab <sha>; PR 75 closed with the commit

