# GitHub Update 54

**Scope: main CLs after the Update 53 release push commit.** Update 53
covers the cycle from the Update 52 push through its release head (seed
`B066CEB5FE8FC9E8`, the Prism fleet day and the Update 53 proofs). This
update covers main 20766 through the release head, 2026-08-28 to
2026-09-01: 171 changelists on main since the Update 53 push, re-measured
at the head (`p4 changes //Codex/main/...@20766,#head`).

## Open from Update 53, and where each went

- **COMPILER-32's remaining piece** (blu): the `lazy-smoke` redo landed
  with a running arm (main 21071, the lower-lazy inner type from the LAZY
  node); COMPILER-40 then bisected an arm64 regression to COMPILER-32's
  step 2 and fixed it (21193).
- **Prism stage 2 page wiring** (fester): landed at 20796 and the boards
  at 20806/20808 (reek, from fester's shelf); fester is parked since
  2026-09-01 to free RAM on the one-DIMM box.
- **Prism stages 2c-2f**: 2c (the in-tab signer) stays on red's row behind
  the memory campaign; the boards' in-tab half landed with stage 2; the
  rest as before.
- **Steve Howell's second PR and PR 98's Finding 67**: the whole open
  queue (PRs 99 through 114) is absorbed with credit this cycle, below.
- **The L-NOGATE candidate runner** (`-Internal` RUNS the `codex/test`
  chapters that cite a changed chapter): still unowned; it gained a
  fourth motivating instance this cycle (COMPILER-40, four arm64 tests red
  for five days).

## Landed this cycle

- **The compiler's memory, halved** (red, main 21187, seed `FECCDD90`).
  Per-definition reclamation in CHECK, SCOPE, PARSE and LEX: each
  definition's survivors are copied out and both cursors rewind to a mark
  taken before it, with a content-keyed text layer keeping names stable
  across batches. Self-compile host peak working set 1,147 MB to 537 MB,
  measured with the new compiler as kernel compiling its own unit; the
  account is `ArchitectsSketchbook.md` "Per-definition reclamation". The
  same CL closed a release blocker nobody's gate could see: the FULL
  gate's `sem-equiv` phase was red on clean main with 39 body mismatches,
  38 from COMPILER-38's binder rename reaching TEXT mode (TEXT is emitted
  from lowered IR) and one from prose sitting between a signature and its
  body, which the comparer reads as an empty body. Both diagnosed
  host-side at zero box cost (blu's instrumentation corroborated the two
  shapes independently) and fixed together.
- **Real literals and the Real printer** (blu). COMPILER-37: a Real
  literal wider than an i64 is refused (CDX2073) instead of silently
  reading as a different number (main 21199, the cycle's 205th refusal
  test). COMPILER-41: `__real_to_text` split its digit buffer at 16, so an
  integer part of 17 or more digits overwrote its own most significant
  end; split moved to 24 with `real-show-wide` as the runner (21215).
  COMPILER-40: an immediately-applied lambda literal lowered to an
  over-curried type and arm64 returned the closure instead of the value;
  bisected to COMPILER-32 step 2 and fixed (21193). COMPILER-34: `==` on a
  self-recursive variant is structural on every backend and plug (20888).
  COMPILER-24 and COMPILER-15 closed; COMPILER-16's 13 CDX6020 sites judged
  benign for one shared reason (21184); COMPILER-9's engine group closed
  and the seven class-B failures triaged to six causes (21147). arm64
  `print-line-uni` emitted the low byte of a code point instead of UTF-8
  (21158).
- **Steve Howell's queue, absorbed with credit** (red). Nine of the ten
  open PRs are on main and closed with a comment naming the CL; PR 100
  (the zig plug's real-to-int and real-from-int) is NOT landed, because its
  expected output encodes x86's NaN and overflow answers and the cross
  battery grades `codex/test/ops` on arm64 and riscv64 too, where both
  saturate; the reason is on `plugs-backlog.md` 2.07 and on the PR. The
  first push of this report said all ten were on main; corrected in the
  follow-up commit. On main: COMPILER-35, a text literal opened at end of
  line was silently empty (PR 114, main 20906); COMPILER-38, lowering
  renames colliding binders so two live bindings never share a name (issue
  113, main 20995); PRs 111 and 112 on the wasm plug (guard walkers,
  per-depth scrutinee, `ipow`, `show INT64_MIN`); PR 108's Cordic accuracy
  test and PR 101's COMPILER-30 witness (20944), which this release's
  ir-fidelity run measured as the empty-list element type now CARRIED;
  PRs 99, 103, 105 and the DeviceMath arc tangent from 107 (20849). The
  riscv real-to-int and `show` on a Real were silently wrong above 2^31
  on all three backends (20879).
- **A Codex program is now a native Linux app and a Windows .exe** (reek).
  The hosted x86-64 lift reads, writes binaries and reaches its own cells
  (20822, 20824); the Windows console PE container is proven (20810); both
  containers are ported to Codex as ElfStdio and PeStdio modes and the
  Prism page offers both as pills (20812, 20814). Plugs 2.17, the hosted
  entry reserving no stack frame, was localised through three retractions
  and fixed with a new seed (21038); its lesson is L-VACUOUS.
- **The wasm plug reaches parity with the hosted x86-64 lift** (reek). 60
  of 60 on the hosted corpus (20884), the `codex/test/ops` slice from 26
  pass 14 fail to 39 pass 1 fail (21088, 21112), SIMD's vector and mask
  families (21134), per-binding local slots, `~` ULP semantics, the
  harnesses reaching every eligible subject with the denominator printed
  beside the score (20899, 20903; L-DENOM), and the campaign's closing
  batch of eleven CLs pulled into this release at 21221. The
  shadow-stack repair COMPILER-38 made dead is deleted (21009).
- **The games reach the landing site** (val). All 33 games play in the
  browser, linked from the landing page, compiled to wasm by the fleet's
  own plug (stage 1 TicTacToe at 20909, the arcade complete at 21145).
  Porting them found and fixed a run of engine defects the watch-only
  demos had hidden: Blackjack softened one ace, Backgammon's bear-off gate
  scanned the wrong points, Othello reported the player to move as the
  winner, War dealt both players the same hand, Spider dealt eight suits
  while calling itself two, Poker had no wheel, Go left a refused suicide
  stone on the board, and more, each with its proof.
- **Prism, stage 2** (reek, fester). The library is on board and the
  toolbox over it (20796); RISC-V and arm64 boards reach the page and the
  in-tab RISC-V kernel boots on QEMU byte-identical to `factorial.expected`
  (20806, 20808); the ELF target lights with a kernel/usermode switch and
  the binary targets are named for what you get (20800, 20802); the
  binary tab's CDX9002 fixed (20818); RESOLVE mode emits its frame in
  unicode, not the CCE wire encoding, seed `7B6A4950` (20783).
- **The build box lost a DIMM, and the gates got cheaper instead of
  slower** (blu, root). 16 GB is the working shape for September. The
  test-compile drivers no longer hold the batch input twice (21043), the
  generated-script check reads through .NET (21091), `concat-codex-self`
  and `compile.ps1` stream their output (21098, 21119: 292 to 178 and 248
  to 152 MB peak); with the drivers small, `-Jobs 8` is the default again,
  measured 412 s against 620 s for one FULL gate alone (21023, 21035).
  Batch your gates (20913): a step is compile plus focused tests, one
  `-Internal` per batch under one token; `build/test.ps1 -All` is
  prohibited outside a release (20892); a compiler-touching gate runs
  ALONE (20927). `compare-codex-semantic.ps1 -Show`, declared since it was
  written, now names the first differing token (21180).
- **The registers, scavenged** (root). CurrentPlan from 2,105 lines to
  about 650 with every open item, ruling and claim kept (20843); the
  tombstones and war stories out of `CLAUDE.md` and the init skill (21109);
  the 2026-08-31 fleet disposition (20842); fester parked to free RAM
  (20985). Lessons added: L-VACUOUS (an arm that agrees with a hypothesis
  and its negation measures nothing), and L-CONSTRUCT's runner column
  corrected to backend-partial (20880). The Fable 5.1 fresh-eyes survey
  with Damian's rulings (`docs/FiveOneSurvey.md`, 21148, 21151). The
  Cobblestone rename campaign is closed (20792).

## What the release proofs found

- **The seed on main was not the fixed point of main's source.** The full
  gate at head 21221 built `FCBABF07` in one pass; main carried `18995A1A`
  (21215, blu's COMPILER-41 land). Same size, different content hash, and
  blu's stream compiler source byte-identical to main. The chain stopped
  at `PerforceProcess.md` 4.3's check, its first catch on a release run,
  and the release landed the gated fixed point as the seed (21223). The
  cause, settled the same evening (blu, via root): blu's gate had printed
  the two-pass P-STAGE2 refusal (the old seed compiled the new source to a
  Sut that was not yet its own fixed point) and the PRE-CONVERGENCE Sut
  was installed against it, the wrong log having been grepped. One
  source, one fixed point; 4.3a names this trap. The release seed was
  verified by computed whole-file SHA-256, not the header field: Sut,
  depot seed and workspace seed `FCBABF07`, and the unsigned stage1
  `8A343FA6` equal to the seed with its signature region zeroed.
- **ir-fidelity moved one row, in the good direction.**
  `empty-list-element-type` was banked DROPPED on 2026-08-27 and reported
  CARRIED at this head: COMPILER-30's witness (Steve Howell's PR 101, main
  20944) lets the checker's element type reach the empty literal.
  Re-baselined at 21224 with the design's sentence corrected;
  `unexpected` was 0 both times and the instrument's three ablations
  passed.
- **The DDC's Roslyn arm refused the emitted C#: `print_uni` did not
  exist.** Update 50's parking-lot entry, one more time: a compiler change
  that reaches the IR wire is a DDC change and only the DDC sees it.
  Fester's RESOLVE-mode frame (main 20783, 2026-08-28) made the compiler
  call the `print-uni` builtin, and the C# plug's builtin table had no
  emitter for it, so every other proof stayed green while the witness
  could not build. Fixed as the ordinary upkeep the manual describes: a
  `print-uni` entry beside `print-text` in `CSharpEmitterExpressions.codex`
  (21228), plug rebuilt, arm rebuilt, witness re-run.
- **A plugs batch is a whole-chain change.** reek's wasm parity batch
  (21221, plugs, page and docs) was pulled into the release after the
  chain had started. The gate's plug phases, the battery, the poison
  battery and the DDC all consume plug binaries, so the chain was stopped
  ten minutes in and every proof ran once from the frozen head.

**The proofs, all at the release head against `FCBABF07479516DE`:**

| proof | result |
|---|---|
| Full gate (every phase) | green, 451.1 s; test-compile 1,492 chapters at 8 jobs in 119.8 s |
| Sut === seed | whole-file hash equal after the seed land at 21223 |
| ir-fidelity `-Grade` | green, 10 s, unexpected 0, moved 0 (after 21224) |
| Battery `-Tier all` | 1,697 total, 1,647 pass, **0 fail**, 50 skip, newly red 0; 343 s at 8 slots; oracles scalar 2013/2013, vector 130/130, cce 1485/1516 with 31 documented gaps |
| App sweep | 265 clean, 5 known-dirty, 0 regressions, 60 s |
| Poison (`-Poison`, `-Tier all`) | poison seed `0CCC06F2`; 1,697 total, 1,647 pass, **0 fail**, 50 skip, newly red 0; kernel restored to `FCBABF07` |
| DDC witness | **HOLDS**: both arms 3,116,369 bytes, 96 differing all inside the signature region, 0 outside (after the `print-uni` emitter) |
| Map | refreshed from the gate's own `Sut.map`, 184,670 bytes |
| Img | `seed/Codex.img` rebuilt against the release seed, 16,777,216 bytes, SHA-256 `D035BD2CEF30271E054A613458104D1CE0ECDFE165E6DAF0D2315167D4FB8679` |
| Diag | rebuilt against the release seed, FULL rehearsal (46 arms, both beds, 652 s) appended `086C5A6FBEF63B780470D0546EB0AC5631269F62D50A88CE298ABA6AED28BF5E` to `diag.rehearsed` |
| `check-shipping-images` | OK: the baked `DIAG.CFG` is the checked-in default |
| `check-doc-counts` | 63 claims, 0 drifted after the seed, img and diag digest refresh |

The proofs ran as one detached chain on red-main, each phase starting
itself when the previous ended, with a verdict line per phase to a log
outside `build-output/`; the chain was relaunched from the phase after each
finding rather than from the top.
