# GitHub Update 55

**Scope: main CLs after the Update 54 release push commit.** Update 54
covers the cycle from the Update 53 push through its release head (seed
`FCBABF07479516DE`, main 21229, 2026-09-01). This update covers main 21230
through the release head, one day, 2026-09-01 evening to 2026-09-02
evening; the changelist count is re-measured at the head in the proofs
section below (`p4 changes //Codex/main/...@21230,#head`).

## Open from Update 54, and where each went

- **The compiler memory campaign, stage 2 and DESUGAR** (red): stage 3
  landed, DESUGAR desugars straight into the frontend keep (22087, below).
- **Two collectors owed from the sem-equiv blocker** (red): landed at
  21381; `$tSemantic` fires on `IR/Lowering.codex` and
  `compare-codex-semantic.ps1` names an empty source body. Superseded the
  same day by Damian's ruling that sem-equiv runs whenever the core runs.
- **COMPILER-41** (blu): was fixed at 21214 and never closed; verified at
  head and closed 2026-09-02.
- **COMPILER-36** (red): ruled GO by Damian 2026-09-02; the multiply, add
  and sub units and the spelling unit all landed (below).
- **The L-NOGATE candidate runner**: landed (fester, 21999, below).

## Landed this cycle

- **Plain `Integer` arithmetic traps on overflow** (red; COMPILER-36,
  Steve Howell's issue 109). Multiply (21676, seed `E0042890`), add and
  sub (21798, seed `F185CB2E`): `jno; ud2` after the op on x86-64, keyed
  on the node's type; the exact i64 `wrapping` band reaches the op by the
  left operand and the wire spells `mul-int-wrapping`,
  `add-int-wrapping`, `sub-int-wrapping`; `Foreword chapter Wrap64`
  carries the wrap-by-design mixers, hash accumulators and literal
  accumulators, declared rather than silently wrapped. The spelling unit
  (21902, seed `FC01D8B7`, Damian's ruling 10:35): a bare `Integer
  wrapping` is the i64 band in wrapping mode and `-9223372036854775808`
  is a writable literal. The plugs follow as 2.21: the wasm plug traps
  through three preamble helpers (22103), and zig, csharp and rust emit
  the checked primitive for plain arithmetic and the wrapping form for
  the band (22121, zig and csharp 57 of 57 on the oracle). The
  census lesson, L-CENSUS: the first grep for ten-digit multipliers
  declared the compiler clean and two gates then died in accumulators
  spelled `h * 33 + c` and `acc * 10 + d`.
- **The compiler's memory, stage 3: DESUGAR into the keep** (red, 22087,
  seed `3127F4C7`). The desugarer writes straight into the frontend keep
  instead of a 50 MB scratch deck copied out afterwards; host peak 550 to
  506 MB on the self-compile. The first candidate died under
  `-PoisonCompact` because five parse-side pointers the desugarer shared
  (the ListExpr and PropEqType spans, the `OverflowMode` cell, the cite's
  two texts) survived the compact uncopied; each is copied now. The
  first land under the new R-GATE shape: scratch fixed point, one
  granted BVT run, sign as four single guests, the token for the landing
  only.
- **An emit deck overflow faults instead of corrupting** (fester;
  COMPILER-48 instance 5, Steve Howell's issue 115; 22074, 22100, seed
  `04BA03DB`). `emit-build` was `build` minus the deck reservation guard
  and minus the top-cell poke, so `__deck-enter` armed whatever the
  previous phase left: a measured ceiling of 449,847,088 against an emit
  top of 16,392,728, 27x above the extent, with the UD2 unreachable. Four
  arms: the fixed compiler is a one-pass hard fixed point; a 65,536-byte
  emit reservation with the fix faults EXC=06; the same undersizing
  without the fix exits 0 with output and an overrun of about 150 MB that
  nothing noticed.
- **A program's first spawn was pinned to the boot processor** (red;
  COMPILER-49, 21975, from val's PreemptiveScheduler stage 1 arm).
  `emit-process-setup` stored the spawn-affinity rest value twelve
  instructions before the page tables zeroed the page holding it; the
  store now follows the tables. val's `gopweb-spawn` arm goes from `NO AP
  RAN THE SERVICE` to `an ap ran the service`, and PreemptiveScheduler
  stage 1 lands the first system service, `GopWeb`, in its own process
  on SMP 4 (val, 22042).
- **The arcade is 34 of 34 playable** (val, 21911). hexwar lacked ATTACK
  legality, not move legality; pokervariants was one table and three
  riders rather than six variants; `GameServer.codex` had been red on
  main since 21272 and is repaired. SPARK-4 closed by deletion in the
  same copy-up.
- **Apps on the landing page, Damian's direct assignment** (val, reek).
  The C64 emulator runs in a browser tab, held to the KERNAL's own boot
  banner by frame 35 with a zeroed-ROM sabotage control (22025, card
  td7). mathbook (22084, td8) and the data console (22109, td9) are WASI
  programs on the compile page's shape, source in on `fd_read`, answer out
  on `fd_write`, UTF-8 at the boundary so no CCE codec in the page;
  graded 12 of 12 and 13 of 13 against facts about mathematics and about
  relational algebra rather than recorded output. gpushow became
  publishable when its 45 server-root references went relative (21952)
  and went live on cobblestoneproject.com the same afternoon (22014).
  fishtank had never run: three stacked faults, a JS syntax error from
  22 Codex hex spellings, a raw byte-10 terminator gluing tokens inside a
  CCE payload, and a missing `fd_read` import (21967; 52 fish, all 8
  sampled moved over 60 ticks). spark renders in the browser with
  `SparkDisplay` untouched, because every write goes through `gd-fb-addr`
  (21981; 334 faces, 50,276 of 307,200 pixels drawn). The globe is a real
  Codex port: its `[Device]` kernels compile through the wgsl plug and the
  Earth and the black hole render on the page (22140), once plugs 2.25
  was found (22136: the emitter's topological loop compared a list's
  length with itself across an in-place `list-push`, COMPILER-42's
  aliasing shape, and exited after one pass, dropping `bh_march`). The
  landing site gains one graphics-gallery card over gpushow, fishtank,
  globe and spark, and the globe page (22157).
- **Steve Howell's safari-codex, taken in with credit** (val; 22154,
  22160). His Safari driving screensaver ported to Codex, with his
  permission to Damian and a `PROVENANCE.md` naming the fork it was built
  against (base 58b08c38 plus PR 100 plus twenty wasm and zig commits).
  The staged expectation was a red compile; all twenty-seven port
  chapters compile clean at our head, and the green is non-vacuous by
  its control (an undefined name reddens at the predicted line). The
  fork's commits are backend fixes the x86-64 front end never sees.
  The same chapters then went through our wasm plug (22205: an 11.3 MB
  WAT, 730 functions, a 1.57 MB module that runs under node), his
  eighteen grader chapters run against his gold values (22215), and the
  ride's performance was fixed to a flat frame cost (22221). The page and
  card follow in the next cycle.
- **The gate runs the tests it compiles, and the runner honours the
  sidecars** (fester). `-Internal` RUNS the `codex/test` chapters whose
  source cites a changed chapter, not merely compiles them, with a
  sabotage arm that compiles clean and fails at RUN (21999; L-NOGATE's
  runner, three instances after it was proposed). `bvt.ps1` honours every
  machine sidecar `test-run.ps1` passes and drops `.skip`, `.slow` and
  `.fatal` by name, which cleared 80 device, SMP and GPU chapters that
  the widened subject set had made red; `test-compile-batch` takes
  `-Kernel` instead of grading with whatever `build-output` held (22037;
  L-SAMEVER on a gate).
- **COMPILER-42, list append, is the agents' problem** (blu; Damian's
  ruling 2026-09-02). Damian struck the question from his queue for good:
  the bar for any change on the append path is a measurement that no
  previously linear line went quadratic. The arc landed the same evening
  (22210, 22224, 22234, 22241, four seed moves): an ownership analysis
  that decides per push site whether the list is owned, reading uses in
  evaluation order so a read before the push does not refuse it, with the
  x86-64 `__list_snoc_copy` and wasm `$list_push_copy` helpers and the
  `list-push-copy` builtin beside it; the census reads 69 push sites, 51
  in place and 18 copying. The rewrite pass stays OFF by default: the
  end-to-end arm (22254) measured memory linear and correctness broken,
  a compiler built with the pass applied to itself crashes in
  `mcopy-labels`, wearing a deck-overflow costume on the large source;
  the fix is on the row.
- **The C# plug reads the compiler again** (root; 22229). The plug
  tokenized and tree-built the whole IR and kept both live for the run,
  so the compiler's 17.1 MB IR crossed the guest's 3 GB heap ceiling and
  the DDC could not run; definitions are located by text spans now and
  each is parsed and emitted inside its own heap region. A cast added to
  lifted-lambda references this cycle leaked type variables into the C#
  and Roslyn refused the compiler with 52 errors; the cast stays for
  concrete types only. Reek's runner refusal (22195) makes a truncated
  emission a FAIL rather than an OK.
- **More of the site from Codex** (fester, reek). The WGSL plug lowers
  the bit builtins and refuses what it cannot express instead of
  answering, against `docs/Reference/WgslSpec.md`, and the fireworks show
  runs on the web from Codex kernels (22248). The star map builds, runs
  and is graded in wasm with its landing card (22237, 22244).
- **The box and main are synchronized by two different things** (root,
  from Damian's words 2026-09-02). The AgentGrid token synchronizes MAIN
  only: requested to land an already-proven seed CL, held about 90
  seconds, no gate inside it. The commander synchronizes the BOX: every
  run that starts a guest is asked of root by one message and granted
  FIFO. `build/build.ps1 -Internal` is banned; a change is verified by
  the tests it touches, one at a time, and a seed CL adds the scratch
  fixed point, the BVT and a signed, self-verified seed. The measurement
  behind the rule: four gates died silently on 2026-09-02, each beside
  another lane's run, and blu's timed token unit measured 23.6 minutes
  held for 10.6 minutes of work. `CLAUDE.md` R-GATE carries it, and the
  same day's tune of `CLAUDE.md` to Anthropic's prompting guide for the
  Claude 5 models (21725) cut the file from 43,887 to 29,941 bytes.
- **Two fleet tools that reported another run's answer** (reek). Every
  plug runner wrote its IR to one fixed path, so two concurrent runs
  crossed outputs and read as a regression; wgsl's is per-run now and
  plugs 2.26 records the other 48 (22138, L-SHARED). `build-page.ps1`
  trusted a compiler-source concat it does not produce, and a five-hour
  stale one made the wasm-hosted compiler differ from bare metal by 159
  bytes of COMPILER-48's guard code, which read as a codegen divergence
  until the timestamps were compared; it refuses a stale concat now
  (22147, L-SAMEVER).
- **Perforce process: two seed CLs in a row** (fester, 22105). The
  second's proof dies silently when the first lands, because a fixed
  point and a BVT certify only the parent they were built from. A lane
  asks the commander for its place in the seed queue before asking for
  its proof runs.

## What the release proofs found

- **The seed on main was not the fixed point of main's source, for the
  second release in a row.** The full gate at head 22163 compiled the
  source with the depot seed `04BA03DB` (fester's COMPILER-48 land, 22100)
  and converged in one pass on `BBB9907C`: 277,490 bytes differ outside
  the signature and the file is 8 bytes shorter, with no compiler or
  foreword source moved after 22100. The release landed the gated fixed
  point as the seed (22173, copied up 22174), signed by the gate and
  self-verified, and the gate re-run at that head reads `SUT === stage1`
  in one pass with Sut byte-identical to the seed. Blu's two seeds of the
  evening (6AD77CCB, A2E240BA) measured the same way, and blu found the
  cause: the gate builds the seed with `-Repl`, which selects the compiler's
  REPL exit mode, and the lanes' scratch path never passed it, so every
  lane-landed seed was a different binary from the gate's. The release
  ships the gate's artifact; the lane path is being made to match. Update
  54's parking-lot entry asked for an install runner and none was built; it
  is asked for again (`SomethingSeenDuringRelease.md`).
- **Three chapters were red at head, and every one was COMPILER-36's
  trapping arithmetic reaching code no gate had run since it landed**
  (L-NOGATE, fourth instance). `st-product` in the SafeTensors foreword
  chapter multiplied a declared shape and wrapped on overflow, the very
  case its guard test exists for; it refuses the product now.
  `spark-noise-test`'s own hash wraps by design and now declares the band.
  `numeric-test` fed `newton` a diverging function and `rk4-solve` a
  t-end already in thousandths, and its recorded `newton=2686991593` was
  wrap garbage that had passed for as long as the test existed; the calls
  are corrected and the expectation re-measured (22173). The same class
  surfaced the same evening in the wasm plug's build of the arcade
  (reek, games-backlog), where the emitter lacked a case for the i64
  wrapping band; that fix follows the push.
- **The diagnostic stick's frozen-clock arm trapped instead of refusing.**
  The rehearsal's `b3-clockstuck` arm reached a frame dump where its
  ladder used to reach END: `hpet-ticks` assembled the 64-bit counter as
  `hi * 4294967296 + lo`, and a frozen HPET reads all-ones, so the sum
  exceeded i64 and COMPILER-36 trapped inside the kernel's clock reader.
  Assembled by shift-or now, the same shape red's sweep gave the other
  u64 readers; the arm answers `clock-stuck` again and the full ladder
  re-rehearsed. Fifth instance of the class in one release.
- **The DDC could not run until the C# plug was fixed.** The plug died
  OUT OF MEMORY at the guest's ceiling emitting the compiler's 17.1 MB
  IR, 3.28 MB of C# in, on the compiler's builtin table; the previous
  release emitted 4.2 MB from 16.98 MB in the same guest. Not today's
  plug change (the pre-22121 plug dies identically) and not the table's
  growth (one row): each definition's C# is built as one text by nested
  concatenation, so peak heap is the largest single definition. Damian's
  ruling: no ship without the DDC; reek streamed the emitter within a
  definition (plugs backlog), and the witness ran on the result.
- **A parity check that read as a codegen divergence was a stale concat**
  (L-SAMEVER, reek, 22147): `build-page.ps1` compared a wasm-hosted
  compiler built from a five-hour-old `build/output/Codex.codex` against
  bare metal at head, and the 159 bytes were COMPILER-48's guard. It
  refuses a concat older than the compiler source now.

**The proofs, all at the release head (main 22310, source of 22285) against
`81F9E8171DCF6268`, the full gate's own fixed point:**

| proof | result |
|---|---|
| Full gate (every phase) | green, 1,104.7 s; `SUT === stage1` in one pass; test-compile 1,459 chapters and test-run 1,435 chapters, 0 fail; check-errors 206 refusals; plug-smoke cross-host 8 of 8 byte-identical on codex-vm and QEMU |
| Sut === seed | whole-file hash equal after the seed land at 22310 (`81F9E817`, 3,179,934 bytes) |
| ir-fidelity `-Grade` | green, 10 s, 7 cases, unexpected 0 |
| Battery `-Tier all` | 1,712 total, 1,662 pass, **0 fail**, 50 skip, newly red 0; 443 s at 4 slots; oracles scalar 2013/2013, vector 130/130, cce 1485/1516 with 31 documented gaps |
| App sweep | 287 clean, 5 known-dirty, 0 regressions (the gate's full sweep at head) |
| Poison (`-Poison`, `-Tier all`) | poison seed `BF0493E1`; 1,712 total, 1,662 pass, **0 fail**, 50 skip, newly red 0; 300 s; kernel restored to `81F9E817` |
| DDC witness | **HOLDS**: both arms 3,179,934 bytes, 96 differing bytes all inside the signature region 40..135, 0 outside; run on the repaired C# plug (22229), 4,264,545 bytes of C#, Roslyn 0 errors |
| Map | refreshed from the gate's own `Sut.map`, 5,587 of 5,587 names match the embedded MAP1 by address and size |
| Img | `seed/Codex.img` rebuilt against the release seed, 16,777,216 bytes, SHA-256 `5D607C0E0B4B7B2A94C089F400954AC268BFE9365224E02DB5BF9F9B14C4C1D5` |
| Diag | rebuilt against the release seed, SHA-256 `FC4EE2EF3B3124EACC323870BD20993B2354D2F7DADFBEB0997B5B0DFDC4A823`; FULL rehearsal, every arm on both beds (46 arms, 675 s) appended it to `diag.rehearsed` |
| `check-shipping-images` | OK: the baked `DIAG.CFG` is the checked-in default |
| `check-doc-counts` | 61 claims, 0 drifted at the push head |

The proofs ran on root-main frozen at the release head while main stayed
open, and the release restarted from the top only for the changes that
could reach the fixed point: three seed moves. The seed that ships is the
gate's artifact, landed under the token after the gate at 22285 converged
on it in one pass.
