# GitHub Update 51

**Scope: main CLs after the Update 50 release push commit.** Update 50
covers the cycle from main 19106 through its release head. Accumulate this
cycle's themes here as they land; every number in the final report gets
re-measured at the release head, not carried forward (L-COUNT).

## Open from Update 50

- **De-recurse or trampoline the compiler's emit spine** (`codex-emit-expr`)
  so the crazy-boss page runs in a browser WORKER's 1 MB stack instead of
  the main-thread fallback (plugs 1.14's durable browser close, register
  1.83). Seed-affecting, token, fester's lane per red.
- **The Cobblestone rename campaign**: reek's landing/Prism work took the
  web surfaces this cycle (brand pass 19793, "runs in Cobblestone OS"
  19860); prism/REPL surfaces and the home page ride this cycle's landing
  work; remainder per `Cobblestone.md`.
- **COMPILER-23 and COMPILER-28 are CLOSED this cycle** (see below); this
  list carried them as open at rotation and was corrected twice.
- **sem-equiv trigger widening** was held for Update 50 and is released;
  proposer reek, unowned after that.
- **SMP teardown** (one SMP test red per battery run, each a different
  one) stays with the `tools/codex-vm.c` claim holder.
- **B4 step 6, NIC-5, A8's metal arm, WORKS-9** still ride a sitting or
  flight; **HAL crypto dispatch 2 and 3** blocked on a board manual;
  **CostModel `fixed` rung** unshipped; **CDX4022's message text** is
  val's seed-affecting one-liner.

## Landed this cycle

*(themes from main 19784 through 19925 so far; numbers re-measured at the
release head, not carried from here)*

### The compiler register was drained (blu)

Damian's 2026-08-26 ruling put blu on the open compiler bugs, and every
row closed or resolved in one day, four of them seed-affecting with the
token and a green gate each:

- **COMPILER-28** turned out fixed BEFORE the Update 50 push (main 19731,
  new diagnostic CDX2054: a range on a non-Integer base is refused instead
  of silently ignored); the row and this file both carried the stale
  "open" reading and were corrected (19790).
- **COMPILER-23 closed whole** (19797, 19818): defect C (`char-encode`)
  was already built; A and B fell to Damian's repair-2 ruling on blu's
  re-measurement -- `text-to-unicode-bytes` and `unicode-bytes-to-text`
  stop being builtins and become Codex in Foreword chapter CCE, and the
  x86, arm64 and riscv hand-rolled copies are deleted. The re-measurement
  also caught two unrecorded forward-half defects (a tier-1 boundary
  error; a mis-framed 3-byte tier-2 read), both dead with the helpers.
  Seed shrank 8,278 bytes.
- **COMPILER-25** (19828): `emit-zero-region` emitted REX.W before F3, so
  real silicon ran `rep stosd` with a qword count and zeroed HALF of every
  region. One line. Invisible on codex-vm, which zero-fills guest RAM --
  no bed arm can see this class.
- **COMPILER-24** (19845): `==` on a recursive variant refuses with new
  CDX2097 instead of expanding until the compiler dies. A refusal, not
  support; what full support would take is recorded, and the
  refuse-vs-support ruling is Damian's queue.
- **COMPILER-26** (19889): a record or variant's implicit type parameters
  are carried on the AST rather than derived by the IR text emitter at
  serialisation time, so every AST consumer sees the complete type.
- **COMPILER-27** (19901): no fix needed -- the row was stale in both
  halves (the zig arm already refuses at the ceiling; the bare-metal half
  passes `deck-floor-test`). Real residue: hosted is now STRICTER than
  bare metal, a hot-path cost trade left for Damian.
- **COMPILER-9** (in flight at this writing): derive the arm64
  helper-vs-arm precedence table with the bed as judge.

### The landing page, Prism, and the wasm transport (reek)

Twenty-odd CLs building the public marketing surface: the four-scene
story parallax with painted cobblestone backdrops (19809/19835/19867),
the boiled-down hook (19825/19833), WHY COBBLESTONE with honest arcade
and ecosystem counts (19854), stats re-measured and the pitch fixed
(19816), the compile page naming its out-of-memory and reporting phases
as a completion report (19850/19860), `serve.ps1` fronting Prism and the
REPL on one origin (19842), a build script packaging the compile page
(19837), and `check-links.ps1` as the runner for the new static-deploy
rule (19882/19885). **Prism is a static page compiling in the tab**
(19882) and gained tabs, pills, three working lenses, verified examples
and syntax highlighting (19921). **The plugs gained a stdin/stdout
transport so they run as wasm modules** (19895), with csharp on the
stdio transport and `print-uni`'s wasm arm at 19909. Steve Howell's live
REPL is wired from the landing with a source link to his repo
(19821/19856/19869).

### The windows campaign: move, then resize (val)

ShellRefinement 6.4 landed in stages, each tested live by Damian:
**windows drag by the titlebar** as an XOR outline, may hang off the
glass with `dk-drag-keep` always reachable (19802, 19865); the desk
state block doubled to 256 bytes with an HPET started at boot and a
desk-loop rate counter behind it (19879); **resize handles and sizer
arrows** -- four cursor shapes, edge and corner hot zones, the resize
drag (19905); and the polish batch -- resize hover and drag fixes, the
mouse click latch, a minimised window no longer under the pointer
(19925). Code complete with Damian's usability tests passing. The
DeskScheduler design is PARKED on his ruling (choppiness gone, drag good
enough); WORKS-41 measured en route (19865).

### The email channel became a working coordination loop (Steve Howell)

The channel (Update 50's last line) carried real engineering both ways
this cycle, same-day:

- **PR 87 settled by measurement**: Steve asked three type-checker
  questions; blu answered with seven typed arms and file:line evidence
  (the occurs check CDX2010 is what makes every plug's arity-blind TCO
  gate safe; the wire's grammar can express the shape but the compiler
  cannot produce it). Steve withdrew the row, re-framed his own python
  finding (the plug copies the reference faithfully), and re-scoped to
  the documentation gap. **Two wire invariants are now declared in
  `DevelopersRulebook.md`** and the IRTextParser arity-check question is
  recorded as a lead in the plugs register.
- **His finding 52 was real and wider than his lead**: a Boolean literal
  pattern reached three of our plugs as the spelling `True`/`False`
  emitted verbatim -- csharp, javascript, and our absorbed zig copy --
  all three reproduced by RUNNING the emitted programs and fixed at
  19917, pinned by `when-bool-cross`/`when-bool-pattern`. The sweep also
  caught csharp's unreachable TCO catch-all (CS8510) and javascript's
  CharTy literals missing the BigInt suffix.
- **His falsifier probes on the PR 87 answer** confirmed arm B executes
  correctly (the TCO-rewritten closure survives, run not just compiled)
  and raised a let-bound-alias soundness question that DOES NOT REPRODUCE
  here: refused CDX2001 at head and at four seeds back to 2026-08-20 with
  a positive control at each. Awaiting his exact pin; the evidence points
  at his ladder harness (his deck-record variant is CDX3002-impossible as
  a user program, and COMPILER-14 records his harness once skipping
  RESOLVE).

### Process

The lanes were re-ruled 2026-08-26 (19784) and the register corrected as
each stale row was caught (19787/19790/19891). The fleet survived an
AgentGrid restart mid-cycle with one mid-gate CL recovered intact.

## The Update 51 release

**Seed `C3181693` (2,917,073 bytes, SHA-256
`C3181693B2C3733BFA5BC70C19F4E39B4222DD4FB9DC20A5C470DD5AF2B9E483`),
unchanged since main 19889; every proof below ran against it.** The proofs
ran at main 19939-19949; the push carries main at push time (reek's
landing-page CLs continued landing non-seed work during the run, per
Damian's ruling).

### The release proofs

- **Full gate** (`build/build.ps1`, every phase): green, 769.4 s, text leg
  and sem-equiv included, constants.hash unchanged.
- **Battery** (`-Tier all -Jobs 8`): first run 1,663 total, 1,612 pass,
  **1 fail** -- `desk-cursor-arrow` pinned the pre-crisp-pointer arrow's
  ink count (56 at both probe positions) where the shipped pointer paints
  198 and 136 (the difference is `cursor-span` clipping at the buffer
  edge). The erase invariant, the arm's stated subject, held at 0 on every
  run. Re-baselined at 19947/19949 with the prose de-hardcoded; re-run
  green: **1,663 total, 1,613 pass, 0 fail, 50 skip.** Oracles: scalar
  2013/2013, vector 130/130, CCE 1485/1516 with all 31 in documented gaps.
- **App sweep** (`-Check -Jobs 8`): exit 0, 265 clean, 0 regressions.
- **Poison build** (0xCD fill): full battery against the poison seed,
  1,613 pass, 0 fail, zero 0xCDCD faults.
- **DDC witness**: **WITNESS HOLDS.** Both arms produced 2,917,073 bytes
  from the same input; 96 differing bytes, every one inside the signature
  region at offsets 40..135, none outside.
- **Artifacts**: `seed/Codex.map` refreshed from the gate's `Sut.map` and
  validated against the seed's embedded MAP1 (5,358 of 5,358 names, zero
  address or size differences); `seed/Codex.img` rebuilt against the
  release seed (SHA-256 `12B7333C..CA4A09CC`); `build/boot/diag.img`
  rebuilt and rehearsed in FULL, every arm green (SHA-256
  `682FA342..D22C978F`, appended to `diag.rehearsed`);
  `check-shipping-images` OK (the baked cfg is the address-free default).
- **Doc counts**: 63 claims checked, 0 drifted.

## Rulings queue (Damian)

- COMPILER-24: refuse-vs-support for `==` on recursive variants (support
  needs a per-sum synthesized eq helper pre-pass).
- COMPILER-27 residue: hosted deck enforcement is stricter than bare
  metal; matching them is a hot-path cost trade.
