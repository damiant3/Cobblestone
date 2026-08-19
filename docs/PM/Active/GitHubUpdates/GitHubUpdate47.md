# GitHub Update 47

**Scope: main CLs after the Release 46 push commit, opened 2026-08-17.** Update 46
covered 16149 to 16558 plus the release's own map, img, README and report CLs.
Accumulate this cycle's themes here as they land; every number in
the final report gets re-measured at the release head, not carried forward.

## Open from Update 46

- **The battery harness can lose bytes from a batch stream.** Carried from
  Update 44; not seen since.
- **Nothing exercises the guard page under a genuine allocation walk.**
- ~~Neither virtio driver derives its DMA regions from the stub's allocation~~ CLOSED this cycle (root, main 16697: the stub publishes the DMA floor and both drivers read it).
- **plugs 1.34, the ARM64 MMIO boundary** (effect row on the device window, or
  EL0): ruling with Damian.
- ~~plugs 1.33, no DECK on arm64/riscv~~ CLOSED this cycle (blu, main 16760).
- ~~B4 step 2b~~ CLOSED this cycle (main 16636); B4 step 6 still waits on the B3 metal sitting.
- **Steve Howell's PR 67 is CLOSED (main 16627 landed `contrib/README.md` and the
  `__deck-set` emitter fix); the ladder lives at github.com/showell/codex-zig-ladder.
  **At the Update 47 push, comment on PR 67 with the public commit** (closed
  before its work was public, per PublicPush.md).

## Landed this cycle

Draft, written from the 163 main CL descriptions in cls47.txt (16570 to
17129) and nothing else. Every count and digest below is what a CL said
about itself at the time; per the stub's rule, all of them get re-measured
at the release head. Where a CL number is given without a stream prefix it
is the MAIN CL (the copy-up); dev CLs are only cited where the main CL
cited them. Merge-downs and claim-only CurrentPlan copy-ups are folded into
the bullet that carries the work.

### The compiler: issue 70, the warning register, and the seeds

- **Issue 70 (Steve Howell): nine `jcc` patches used a POST-append
  `code-len`** (blu, main 17048, seed `12B07296` -> `55E53A81`).
  `patch-jcc-at` wants the jcc's START offset (rel32 = target - (jcc-pos +
  6), patch at jcc-pos + 2) and all nine sites handed it a value 6 too
  large, so the displacement stayed 0 (the loop fell through, which is what
  the issue reported) AND the 4-byte patch landed at pos+8 INSIDE the next
  instruction. Verified three ways, not by reading: `st-append-code` mutates
  through `__record-set` so `st7` and `st8` alias; argument evaluation order
  MEASURED left-to-right with a two-way probe (1111 vs 1101, it answered
  1111), which promotes the eight sibling sites from fragile to wrong today;
  the arithmetic at `X86_64State.codex:959`. Sites re-measured rather than
  copied from the issue: 3216 plus 3164, 3166, 3175, 3177, 3186, 3198, 3326,
  3424; the scanner validated both directions (nine before, zero after, and
  it does not flag 3218 where `.code-len` is legitimately the target). Why
  nothing caught it: the beds set `skip-ata-bring-up` so the bad bytes are
  emitted and never executed, and the fixed-point gate cannot see this class
  by construction, because a deterministic miscompilation reproduces
  byte-identically. Seed measured, not predicted: pass 1 gave Sut `015F27F6`
  with stage1 === stage2 (built by the OLD seed), unsigned NewSeed
  intermediate, then the SIGNED Sut; passes 3 and 4 green at `55E53A81`, so
  seed === Sut === stage1, whole-file hashes throughout (P-SIGNED). The
  copy-up states its deviation: main moved four times under the token, all
  non-seed files; three merges were re-gated and the fourth was not, on the
  grounds that every merge touched zero files under `codex/compiler`,
  `codex/foreword` and `seed/`.

- **Ruling 14: warnings do not gate the build, they are AUDITED AT THE
  RELEASE GATE, and there was nothing to audit** (blu, main 17087, then
  17102; red 17058/17060 recorded the ruling; COMPILER-16).
  `Invoke-BuildCdx` wrote the self-compile diagnostics to a temp file and
  deleted them on success, so every green build destroyed the only copy.
  That is what issue 70 cost: `CDX2064` fired CORRECTLY on
  `X86_64Boot.codex:3216` on every build for as long as the defect existed
  and reached nobody (blu published the opposite on the issue first and
  corrected it publicly after measuring the retained pre-fix source). The
  log now persists at `build/output/<artifact>.diag.log`; both the generator
  (`codex/build/BuildScript.codex`) and the shipped `build/build.ps1` carry
  it, and the CL records that the shipped script is the maintained side (39
  of 40 generators had drifted; a bulk regenerate would have destroyed
  `build.ps1` and `test.ps1`), so `check-generated-scripts.ps1`'s missing
  `-Write` flag is load-bearing. `ExaminersAssay` "The self-compile warning
  register" opened, counts MEASURED from the retained log at seed
  `CAE56FBC`: 13 CDX6020, 7 CDX3005, 980 CDX4010 info, 6 CDX2053 info, 1
  CDX4030 info, 0 errors; CDX2064 reads 0 and was 1 before the fix, which
  is the log shown to work. The 13 CDX6020 are recorded UNJUDGED (one reads
  `st` while mutating `st1` and is worth a look). COMPILER-16 also records
  that issue 70's premise was half wrong: the real CDX2064 blind spot is the
  eight SIBLING-ARGUMENT sites, not 3216. Then 17102: 987 of 1009 lines were
  infos, so the audit surface was still unreadable; the log is split
  LOSSLESSLY into `<artifact>.diag.log` (warnings, errors, non-diagnostic
  context) and `<artifact>.info.log` (info, hint, deprecated), measured at
  `CAE56FBC` as 20 warnings in a 22-line file, 987 infos, zero misfiled; an
  EMPTY diag.log is now a meaningful green. Two traps recorded in
  `ExaminersAssay`: `info CDX4030: PIPELINE ...` starts at column 0, and a
  `\s` class written in a Codex string literal emits a bare `s`, so the
  shipped and generated scripts disagree while both look right; `(^| )`
  needs no escaping in either language.

- **plugs 1.42 closed IN THE COMPILER: a unit constructor is no longer
  emitted as an application** (fester, main 17071, seed `CAE56FBC`; ruling
  13a, red 16938). Sized first by reek: the hole was at least seven plugs,
  not two lanes (16956), and the source plugs are the named test target for
  the compiler fix (16962); fester 16913 rewrote 1.42 and rulings 13 to show
  the three instructions instead of describing them. Later CLs on main
  (17087, 17090, 17102, 17112) all report the seed unchanged at
  `CAE56FBC665E4C51`.

- **COMPILER-13 gains the guard it could not have on x86-64: a Lambdas
  section in `plug-oracle-arith`, and its first run found four plug defects**
  (blu, main 16977, seed unchanged `12B07296419847B2`; 16689 unblocked it on
  val's release of the oracle harness to blu, recorded with 16704). The
  multi-parameter lambda defect closed at 16326/16374 lived only in the IR
  type annotations crossing the plug wire, so x86-64 answered correctly under
  both compilers; the arm has to be behavioural and cross the wire. Seven
  rows, x86-64 truth MEASURED 105, 3, 14, 7, -7, 15, 300; `lam-mixed`'s
  second parameter is a Boolean ON PURPOSE. First census: javascript and
  typescript 40 of 40; python (N-ary lambda applied curried), wasm (inline
  body referencing undeclared locals; the plug must lift and does not),
  csharp (CS0149, six sites), zig (`@TypeOf(k)` on a `comptime_int` capture,
  so the env struct has no runtime size). reek closed all four arms the same
  day (16981, harness 6 of 6), after `plug-oracle-test` learned to REFUSE a
  stale plug binary (reek 16877; 1.40 had recorded an arm passing 33 of 33
  because it did when it was built).

- **The zig plug: upstream, disputed, and then ordinary fleet code again**
  (blu 16996; reek 16984; red 16987; reek 17112, Damian 2026-08-18). blu
  recorded the comptime_int capture defect as `plugs-backlog` 1.43 for Steve
  Howell, verbatim failing arm, zig 0.16.0's errors, and the x86-64 truth
  (`lam-capture 5` = 15, `lam-capture-two 1 2` = 300), with reek's fix at
  16981 held pending a ruling on whether the fleet may edit that tree.
  Damian's ruling: Steve had the zig plug for his big early updates, those
  are absorbed, and `codex/plugs/zig/` is held to the same standard as its
  neighbours; credit Steve in a CL that changes what he wrote and flag it in
  the next GitHubUpdate. **So, for Steve: 16981 changed `ZigEmitter` to
  capture with a runtime-sized env; zig passes all 40 values of the oracle
  subject, Lambdas included, on a plug rebuilt for seed `CAE56FBC`.** 1.43
  is DELETED as it said it would be if the fix is kept. `PlugDeepRecursion.md`
  carried the stale rule twice and a stale measurement claiming zig's arm red
  for a missing `list-snoc` emitter; `ZigEmitter.codex:787` maps it to
  `cx_ll_push` and the red reading came from a plug binary older than its
  source.

- **An unresolved `block-` call is a refusal now, not a warning; ARM64 gates
  only the three body calls** (fester, main 16947), after 16904 emitted the
  block helpers only when the driver is in the program, which removed the
  `vb-*` unresolved-call flood from both lanes.

- **COMPILER-9 final: ARM64 dispatch derives "user def wins" from
  registration past `runtime-funcs`** (root, main 16638; `a64-helper-by-name`
  is the helper-vs-arm precedence table, `a64-emit-user-fn-call` gains the
  >8-arg stack path; arm64 bed 30/30 FAIL set unchanged after merging blu's
  1.33 deck emitters; compiler-backlog row closed). Beside it 16637:
  `ArchitectsSketchbook` explains the June F# JIT sum of 4 against today's
  11, and `Arm64CodeGen3`'s inline-here decision reads registration.

- **plugs 1.44 opened by measurement only: single-width float on riscv64 and
  arm64 is wrong BEFORE the guard** (reek, main 17125, docs only, MAIN
  PINNED). riscv64 fails two lines, not one: line 3 answers 4278190078 =
  0xFEFFFFFE, which is 0x7F7FFFFF doubled, an f32 pattern reinterpreted as an
  f64 denormal; the exponent constants alone will not fix it. arm64 line 3
  PASSES and nothing explains why; recorded with a warning not to validate an
  arm64 fix on that line.

### RISC-V and ARM64 codegen: the frameless gate, the deck, and a spawn defect

- **plugs 1.3: the honest frameless gate is ON on riscv64, and it took four
  CLs to get there** (fester, main 16610, 16818, 16836, 16847; not
  seed-affecting, plug source only). 16610: `rv-save-args-skip-last` held the
  last tail argument in a register the copy loop then reused (`rv-load-local`
  rotates temps on every spilled load), so the last parameter received an
  earlier argument's value; MEASURED ON THE BATTERY because the four `tco-*`
  tests are not the instrument: riscv64 cross battery 95 failures to 61,
  thirty-four fixed and one has `tco` in its name (aesgcm256, x509-chain,
  tls13-record, usb-test among them; earlier the same day that list had been
  put down to unsupported crypto and graphics). `tco-shuffle-spill` was the
  arm and already existed; it fails IDENTICALLY on arm64 and still does.
  16818: `int-mod` in a framed two-argument tail call materialised its
  divisor into the dividend's own register (`addi s2,zero,1000` then `rem
  t4,s2,s2`, 0 instead of 456), same leak in `rv-emit-two-arg-binop`; both
  now clear result-dest before emitting operands; Renode battery 62 to 59
  FAIL. 16836: a framed TCO loop saved no `ra` when its only call was inside
  a binary operator. 16847: gate ON, riscv battery 58 to 52, no new failures.
  16919 routes the open risks of the step 0 groundwork and the frameless gate
  to red in the docs that own them.

- **plugs 1.33 CLOSED: the deck exists on arm64 and riscv** (blu, main
  16604, 16607, 16624, 16687, 16760; every one "gate green, seed untouched").
  arm64 step 1: three 8-byte cells in `Arm64Runtime`, real `__deck-pos /
  __deck-set / __deck-alloc`, and the name-path route without which value
  builtins never reach dispatch, pinned by `deck-cell-contract`. Step 2: the
  BRACKET, `__deck-enter / __deck-exit` on x28 with the nesting counter,
  `deck-bracket-contract` (control fails on exactly the 2 of 5 lines that
  distinguish a real bracket). Step 2b: the `deck-record` intercept, ported
  from `X86_64Compound:150` and gated on the chapter that defines
  `deck-record` beside `init-phase-allocator`, not on the name;
  `deck-record-contract`. Step 3, riscv: that lane did not LACK the deck
  builtins, it had STUBS that answered zero, so `phase-compact` there was
  `__heap-restore 0` and nothing said a word; three cells at `#80095000`,
  real bodies, name-path routes for the three NULLARY builtins, the intercept;
  control 6 of 13 lines failing, now 13 of 13; full riscv cross battery
  before and after 62 to 59 with ZERO new failures. 16607 also corrected the
  row's own stub table, which contradicted its header. (Open item from Update
  46, closed.)

- **plugs 1.38 closed: a spawned RISC-V child inherited a zero memo base
  (`s0`)** (fester, main 16707), so every memoized definition faulted on a
  load from address 0; under QEMU that read as a hang (the trap handler
  never advances `mepc`), under Renode the cache simply missed forever.
  `process-spawn` stores the caller's `s0` into the child's saved-s0 cell.
  Guard `codex/test/spawn-memo-table`, ablated. `ExaminersAssay` records
  that the cross runners truncate captured UART to the `.expected` line
  count, so a run that prints everything and then hangs compares equal.

- **plugs 1.14, 1.8, 1.35, 1.40 by root** (main 16639, 16640, 16641). 1.8
  CLOSED: haskell/elixir/clojure rebind a field store in act-statement
  position (x86-64 answers 42, the three read 42 by text). 1.14 by family:
  JVM plugs run the entry on a 512 MB thread, native plugs on a 512 MB thread
  or the platform's shell, gtk takes the python counter; class 2 and 3
  recorded per plug in `PlugDeepRecursion.md`. 1.35 CLOSED: the six TS/JS
  plugs carry javascript's call model, typescript runs the entry on a 512 MB
  worker, typescript is an oracle arm under node 24 (33/33 then). 1.40
  renumbered from 1.39, which is reek's cobol row.

### The plugs close-out: cobol, pascal, fortran, and the harnesses

- **cobol is the last plug and it is at parity; the campaign is spent**
  (reek, main 16600, 16601, 16655, 16674, 16840, 16925, 17039, 17042;
  `plugs-backlog` 1.36 and 1.39). The cobol plug bound no names and a tail
  call overwrote a parameter another argument still read (16600, measured
  through the plug; `cobc` is absent so the output is read rather than run);
  a staged row with four measured divergences named (16601); texts carry
  their own length (16655); `text-replace` added and `text-split` refuses
  instead of answering its first argument (16674); the list builtins refuse
  instead of answering zero (16840, which also wired five more plugs into the
  builtin check under 1.32); cobol and ada get a real list representation
  (16925); list concat and cons were the row's last two refusals (17042).

- **pascal: records, a list runtime, a working field store, and variants**
  (reek, main 16581, 16582, 16827; `plugs-backlog` 1.20). Code-complete,
  hand-traced, never compiled: no `fpc` on this box, and "install fpc, run
  plug-oracle-arith" is the recorded next step. The row now names the two
  silent wrong answers it never named.

- **fortran 1.7 stages 7 to 14: it emits and traces clean** (val 16596,
  16615, 16619, 16735, 16777, 16785; reek 16896, 16907, 16950). Stage 8:
  statement position emits Fortran statements, not `merge()`; `merge` is an
  intrinsic FUNCTION that evaluates BOTH arms, so an if whose arms are prints
  had emitted `merge(print *, ..., print *, ...)`, and the "obvious" prelude
  fix would have traded a syntax error for both arms printing. Stage 9: a
  function body's control flow assigns to the result, so every recursive
  function written as an if no longer recurses unconditionally (`merge(`
  across the 13 test-input outputs 39 to 17). Stages 10 and 11: let bindings
  declared and assigned, field stores mutate, record construction through a
  generated `mk_` constructor because the Gauge band lives only in the type
  and a Fortran structure constructor cannot run code, component keywords
  because the IR carries values in SOURCE order. All 33 oracle rows
  hand-trace to `plug-oracle-arith.expected`; HAND-TRACING IS NOT COMPILING
  and the register names the three things a first real compile should be
  pointed at. Every emitted function is now RECURSIVE with a result clause
  (16735, depth class 3 unablated); a lambda fails loudly and has a runner
  (16777), then a lambda that is CALLED is lifted while one used as a VALUE
  refuses (reek 16907); record fields and constructor params take their
  declared type (16785); variants construct and destructure (reek 16896);
  the five text builtins, the half 1.31 excluded (reek 16950).

- **plugs 1.30 CLOSED: every harness can hear its guest, and the residue
  bottoms out in the serial capture** (val, main 16578, 16621, 16658,
  16684). pe, elf and javascript booted codex-vm with no `-output` at all and
  wpf never read it, so the TRUNCATED `sent=` line their guests had printed
  since 16375 reached no one; all four now WaitForExit and grep the console
  (exit 7), pe and elf assert built == sent == received (exit 9), sabotage
  arm on each. `plug-run` fails a guest that died mid-run (16658) and hears
  the codex-vm serial drop (16684). Where the eleven output-ring harnesses
  are actually exposed is `output_buf_write` (`tools/codex-vm.c:8969`), which
  dropped a byte with no counter when the capture buffer cannot grow;
  reachability UNMEASURED and recorded unowned, then root closed it at 16668
  (below, codex-vm).

- **plugs 1.41: bulk receive at every big-artifact site, 116.77 s to 0.02 s
  per 16 MB** (val 16723 the shared accumulate, nine sites named; reek 16870
  three sites, plus elf/img/pe treating an ABSENT guest witness as a
  failure; reek 17090 the last two, `run-plug.ps1` and `boot-arm64.ps1`,
  which are GENERATED so the fix went into the generators and the shipped
  scripts were brought to match by hand). The fix buys a CORE, not a second:
  the receive is overlapped with a guest slower than the loop, so at `-Jobs
  8` it is contention that moves. Four sites remain and none is worth
  changing. Also in 17090, 1.14's runtime census: 37 candidates, five
  binaries on PATH (python, node, wasmtime, dotnet, zig), which is the six
  oracle arms already wired and none of the 42 parked plugs; verified with
  `plug-oracle-test` 6 of 6 at 40 of 40 on plugs rebuilt for `CAE56FBC`.

- **plugs rows closed one line each.** reek 16662: the builtin-table check
  learns the index-dispatch shape, pascal wired (1.32). reek 16806: 1.21
  CLOSED, five index-dispatch plugs refuse an unarmed builtin instead of
  emitting a list append. reek 16802: 1.37 CLOSED, babbage refuses instead of
  emitting a comment and computing zero. reek 16829: two closed entries
  deleted. reek 17031: 1.27 finding 9 FIXED, the row's last unrouted item.
  val 16679: 1.26 rung 13 re-measured, parked on a bracket; reek 17025: three
  ways to build the missing payload generator, all dead ends. blu 16704:
  `ExaminersAssay` records that the cross battery's run retry cannot tell a
  SLOW test from a silent one (same ceiling), with the four arm64 tests that
  fire the class.

### ModernDesk: every pane is a step, no pane owns a loop

- **The design, the rulings, and stage 0 with the numbers** (val 16809,
  16867, 16873; red 16860). Frame counter in `GopScene`: host rasterizer 16
  ms/frame at 1024x768 and 1600x900 (a 60 Hz pace against wall clock, not a
  counter cap), software 86 and 135 ms/frame, metal unmeasured; frozen-clock
  control holds, scene byte-identical, counter 598 to 1196. Damian's two
  rulings recorded: cooperative panes with saved state, and the widget-panel
  flex default. Then the correction (16873): `Widget.codex:44` has set
  `wn-flex = 1` since main 16020, `browser-backlog` BROWSER-2 said so and was
  cited without being read, so "defaults to 1" means no change and stage 1
  closes with no code.

- **Stages 2 to 4, the chrome** (val 16832, 16815, 16857). The sidebar is
  no longer clipped by the top bar or by the 3D pane at 1600+ (WORKS-31
  closed); the 3D camera takes its viewport aspect (sphere 1.440 to 1.042 at
  16:9; the cause was `camera3d-new`'s 1.333 literal, not a missing
  projection term); the taskbar carries the system-menu entry and the clock,
  repainted by `comp-walk` so it follows the theme, new test
  `desk-taskbar-hit`, WORKS-34 filed because the 28-logical band cannot hold
  a themed button.

- **Stages 5 to 8: fourteen panes converted, one at a time, each calibrated
  against the HOST clock** (val 16916, 16931, 16953, 16965, 16973, 16975,
  17022, 17028, 17035, 17009). `desk-monitor` is the first STEP, called via
  `desk-focus-cell` (ds cell 12); taskbar-clock lag 18 s to 0, and a
  pre-existing 3,594 B/s idle leak in `desk-loop` found and fixed on the way,
  frontier now flat; `ArchitectsSketchbook` corrected (prologue stack tracking
  is gated on `trace-alloc`). Calendar (`desk-step-of` is now the only
  function naming panes); Appearance (the protocol carries the whole event,
  `sc` AND `clicked`, first pointer-driven pane); Calculator (`gcalc-key`
  applies as well as reports, so a second evaluation would double every
  digit, 1 2 3 gives 123); Clock (`last` moved into the pane's own state
  block, and the F12 arm it never carried is fixed); the launcher (the
  protocol grows a third answer: 0 close, 1 stay, anything else a scancode to
  dispatch after closing); Diffusion (shared `desk-quiet-step`, a pane of
  that shape costs one line); Issues (its DbServer rebuilds per KEY instead
  of per POLL, cheaper than the loop it replaced); Console (ls types one char
  per key and lists the real ESP). WORKS-37 (17009): the taskbar clock
  stopped flickering under a step pane; every chrome render goes through
  `dk-chrome-paint`, which invalidates the clock gate. The plan correction
  in 17035: the remaining cost is WHERE THE LOOP LIVES, not cheap versus dear.

- **Stage 9: the last five panes were blocked on ONE decision, and it was
  made** (val 17045, 17055, 17081, 17094, 17114, 17129; Damian's option 3).
  Every one of Files, Browser, 3D View, Aquarium and Edit carries a TYPED
  value across loop iterations and none of the nine converted panes did;
  Files would mount the ESP per keystroke and its directory stack IS the
  user's position, the 3D target is 4.6 MB. The typed app-state channel
  landed as its own CL: `DeskApps` threaded through `desk-loop` beside root,
  a filled value lives above the base mark and below the frame mark so steps
  keep it and close reclaims it deliberately, both close paths drop the
  record and rebuild after the restore, verified against all nine converted
  panes on seed `55E53A81`. Then Files (first with typed state; the protocol
  grew a negative answer for a step that grew durable state), Browser
  (opening is two calls so the desk's record outlives the pane's first
  keystroke), the 3D panes (one step serves both; ten open/close cycles
  reclaim the 4.6 MB target bit-identically), and the Editor (the 9 MB
  buffer stays in its ds cell). **All fourteen panes are steps and no pane
  owns a loop; Esc closes to a desk 0 pixels from plain.** Not
  seed-affecting throughout.

### The diagnostic stick and the flight

- **`DiagnosticStick.md`, from proposal to a flown image in one cycle** (red
  16726 design and the HardwareSitting QUICKREF ruling that a metal question
  is a stage with grouped sittings composed by red; 16728 output-channel
  section and rig ruling; 16712 approvals; root 16822 step 1; root 16851
  step 3; val 16889; red 17098; root 17105, 17117). Step 1: the diagnostic
  ladder `build/boot/diag/Diag.codex` + `DiagStage/DiagPci/DiagScene`,
  `diag.img`, `build-diag.ps1`, `diag-arm.ps1` (7 arms: 5 codex-vm, 2 OVMF
  incl. read-only medium), Diag quire in quire-map, `build-img -Extra`,
  `test-ovmf -ReadOnlyDisk`. Step 3: SMBIOS, EDID and CPU passive rows, the
  stub's handoff block v2 (`SetMode` path byte-identical), GopHandoff v2
  readers, `check-diag-verdicts.ps1`, codex-vm SMBIOS/EDID/hypervisor-bit
  publication with `-no-smbios/-no-edid/-edid-bad` and a stray-NUL fix in its
  SMBIOS strings; diag-arm 10 arms green. DiagScene banks a frame time
  (WORKS-36, root approved): passive third row, plain vs shadowed because the
  host rasterizer is a codex-vm device and does not exist on metal, 60-frame
  cap, NOT the desk frame rate. **Flight 2026-08-18** (red 17098,
  `HardwareSitting`): item 2 FIXED at 17105 (`DiagPci` judges the first
  MEMORY BAR, I/O BARs skipped; forced arm `diag-pci-map-judge`; the rung-2
  RTL8168 BELOW3G row RETRACTED); item 4 FIXED at 17117 (EDID size per spec,
  `ded-size`, and a `cpu max-apic-ids` row, with the 1.3/both-non-zero
  correction); eight arms green on the rebuilt image.

- **A8: a refused heap allocation paints DARK RED, so the sitting can read
  the answer off the glass on a board with no serial port** (fester, main
  16757). Previously the refusal reported only to the two UARTs and painted
  the anonymous in-stub DARK BLUE, so a photograph could not tell "AMI said
  no" from any other death in the stub. Bed-verified both ways on the flight
  bytes: `-MemMB 2048` paints the desk, `-MemMB 256` gives 602010 on every
  one of 262,144 samples. `cdx-to-pe.ps1` is generated, so the generator
  carries it; the flown `-EntryStart`-without-Ebs path stays byte-identical
  by hash. Also for the sittings: reek 16751, WORKS-9, the queued sinkladder
  image was stale against Fat16's release-blocker fix, rebuilt and
  re-rehearsed with a flight card; red 16731 retires the eject/reinsert
  warning as a live hazard.

- **codex-vm: a PCI-to-PCI bridge model, and dropped serial bytes counted**
  (root, main 16746 and 16668; claims 16742, 16645). `-pci-bridge` so
  `pci-scan-all`'s descent branch runs, bus-aware config, `pci-bridge-scan`
  calibrated by the flag (count 5 bus1=1 vs 3/0). 16668: codex-vm counts and
  reports dropped serial bytes (val's 1.30 residue); a failed demand-commit
  is named as the HOST's (`ERROR_COMMITMENT_LIMIT`) and no longer kills the
  crash reporter, which was the intermittent brotli-interop HOST CRASH; Code
  at RIP no longer empty on a 3 GB guest; both gates green, Sut == seed
  `12B07296`, brotli-interop 14/14 + control. Rebuilt `tools/codex-vm.exe`
  both times.

### The identity ceremony

- **Identity reconciliation, stages 1 to 3** (red, main 16754, 16763, 16772,
  16798, 16881; rulings 11 and 12; every code CL "gate green, Sut == seed").
  Stage 1: the wizard's HKDF key fixed, `IDENTITY.DAT` v2 with v1 rewrap and
  an independent KAT arm; then the v1 reader removed at Damian's direction
  (16772). Stage 2: pinned seed, key-zero on lock/restart/power-off,
  constant-time compare, plus a `HeapScrub` chapter and arm (`heap-scrub-to`;
  a Text built by appending leaves every prefix on the heap), and the
  ceremony order upstream/timezone/identity. Stage 3: auto-unlock, bed-only,
  with the `ExaminersAssay` account. 16798 also moved val to the Modern Desk,
  the plugs lane to reek, and root to DiagnosticStick step 1.

### HAL: nine boards on linear handles, then real capabilities

- **`HardwareAbstractionLayer` claimed and worked in three phases** (root;
  16928 claim, OracleCloudArm64 deferred as a dead project per Damian at
  16922). Phase 1, main 16944: a linear GPIO `Pin` handle in `Board.codex`
  (`gpio-open/write/close`, `[Device.Mmio]`), lifecycle test and two refusal
  tests (2063 leak, 2061 use-after-close); gate green, 181 refusals ok, Sut
  === seed. Phase 2, one board per CL: FE310 16959, STM32F4 16968, ESP32-C6
  16991, STM32L4 16993, nRF52840 16999, nRF9160 17001, RP2040 17005, Pi4
  17012, QemuVirt 17014, each threading the shipped linear UART and GPIO
  handles with its smoke test up by two and `boards-test` green; design and
  plan record at 17016/17018. Phase 3, main 17063, **seed `7590CCA1`**:
  `[Gpio]/[Uart]/[Spi]` become real capabilities (`Capability.codex` cs-id
  18/19/20, mirroring Flash), foreword gpio/uart/spi handle ops promoted to
  their caps, 3 launder tests (CDX2031) + `hal-spi-linear`,
  `check-effect-vocab` regenerated 0 drift; self-verifies, 184 refusals ok.
  Then 17084: the nine board wrappers promoted to `[Gpio]/[Uart]`, so
  board-level UART/GPIO access is gated; boards-test 9 green, 126 sub-tests.
  Design record 17067.

### Networking: TCP byte loss, the sixth handshake, ARM64 DMA

- **TCP byte loss FIXED, and it was in codex-vm, not the guest** (blu, main
  16941; 16792 the read that pointed there). A 16 MB guest send
  intermittently arrived short with a clean close at both ends (found by
  val, instrument at 16515). `nat_handle_tx`'s FIN branch called
  `nat_tx_flush` ONCE, a single non-blocking `send()`, then `shutdown`; a
  socket whose send buffer is full at that instant takes part and refuses
  the rest, and those bytes sat in codex-vm's own txbuf; `nat_poll_rx` never
  flushed a state-3 connection again and VM exit skipped state 3 too. Fix:
  defer the shutdown and the free until txlen reaches zero, flush state 3 in
  the poll, drain at exit on select. The census stays: NAT TX BYTES at exit
  counts every discard site; the failing run read `sent=16256800
  drop-freed=520416`, summing to 16777216. L-PARTIAL: the first fix removed
  exactly the mechanism read out of the code and the arm still lost 580,616
  bytes; splitting one counter into reap and exit named VM exit's drain.
  L-STALL: a uniformly slow reader does NOT provoke this, a one-shot 15 s
  stall near the end does, every time. Two corrections to the plan row:
  `flush-transport-outbox` discarding the driver's answer is real and still
  open but NOT the cause; the planned guest-side refusal COUNT would have
  read zero by construction (`net-send-raw` never reads the NE2000 transmit
  status). Verified: stall arm FAT32 and FAT16 both deliver 16777216 against
  523,216 / 575,016 / 580,616 lost on three prior builds; seed unchanged
  `12B07296419847B2`.

- **The sixth-handshake defect: `net-listen` destroyed a half-open
  handshake** (blu, main 16788; the per-boot connection ceiling root routed
  at 16719/16710). `tcp-fresh-listener` was called whatever the state, so a
  re-listen over SYN_RECEIVED forgot the peer and the client's ACK died at
  `Tcp.codex:262`. Half the routed hypothesis refuted by measurement: given
  a preserved SYN_RECEIVED the same ACK reaches ESTABLISHED, so
  `tcp-step-syn-received` is sound and whatever failed on the board is in the
  arm64 frame path, still open. The half-open is preserved BOUNDED, and the
  first bound written was false (the SYN-ACK is never armed for
  retransmission, and `arm64-net-io-accept` never calls `net-tick`); budget
  is counted in re-listens instead. Fourteen pre-existing arms unchanged; Sut
  byte-identical to the depot seed.

- **ARM64 VirtIO DMA regions derive from a stub-published floor at
  `#40004000`** (root, main 16697; OracleCloudArm64 residue 2). Stub publishes
  align-up(stack-top, 2 MB), `VirtioNet`/`VirtioBlk` peek it, the
  `build-arm64-img` DMA check is now a wiring check; arm64 serves 3/3 HTTP
  200. (Open item from Update 46, closed.) Residue 1 re-verified closed; the
  ~5-conn ceiling recorded and then routed to blu (16719), which is the
  sixth-handshake bullet above.

- **B4 steps 2 to 5 and 2b** (root, main 16636): `-Card ne2k|e1000` on
  `cdx-serve-test` and `registry-locate-test`, the registry-probe path, the
  repository wire in `DevelopersRulebook`, and `cdx-registry`/`cdx-announce`
  now call `net-driver-bring-up` with `recv-give-up` in ticks. (Open item B4
  step 2b from Update 46, closed; step 6 still waits on the B3 metal
  sitting.)

- **ProtocolStack, OTA and Track D** (reek). 16780: reassembly was already
  closed and the header lied; the binding gap was LwM2M alone and
  `tools/lwm2m-client.codex` closes it. 16793: OTA socket wiring, the
  download runs over UDP against aiocoap, and the first real exchange caught
  `fw-next-request` putting a multi-segment path in one Uri-Path option.
  Track D item 20 (16693, 16701, 16769): `gguf-fits` was itself the additive
  class and admitted a read at offset 9.2e18; `MessageFraming.frame-fits`
  made subtractive; the named files read by hand, every addend a fixed-width
  wire field, none needs changing.

### ComplianceEvidence

- **The evidence plug, FactStore ingestion, and per-board posture** (root,
  main 16886, 16900, 16910; not seed-affecting, gate green each).
  `codex/plugs/evidence/` (`EvidencePackage`, `EvidencePlug`, build/run/test
  scripts, five arms, byte-stable package, optional Ed25519 signature, hash
  provenance rows); `FactIngest.codex` records the package as a kind-50 fact
  in a disk image's fact-store partition (`run.ps1 -FactImage`, idempotent
  by content), `test-evidence.ps1` fact-ingest + no-store arms (7 green),
  `DevelopersGuide` capability-grant pitfall; then the board-posture table
  (`ev-board-posture`) keyed by `-Board`, four SoC attributes with the
  sections each anchors and the vendor reference, `board.*` facts + HTML
  appendix, claims unchanged across boards (board arm, 8 green).
  KingsAndCourts count re-measured (61).

### CrossLaneFilesystem

- **The servicer refusal arms are measured on all three lanes; the read arm
  was missing everywhere** (fester, main 16883; red 16854 moved fester's
  plan row to CrossLaneFilesystem riscv).

### Docs, process, rulings, and small closes

- **CurrentPlan pruned 670 to 311 lines** at Damian's direction (red 16665;
  `DevelopersGuide` + `ExaminersAssay` `print-line`/`print-line-raw`
  corrected to match `Builtins.codex` since main 14809). The rulings queue
  this cycle: 9 (ModernDesk shape, red 16860), 11 and 12 (identity, 16754),
  13 ruled (a) (16938), 14 (warnings audited at the release gate,
  17058/17060). Approvals of 2026-08-18 (16712): owners in the lanes table
  and the pool, sittings coordinated by red and grouped, the diagnostic-stick
  item, two closed codex-vm sections deleted per the file's rule. 16844: the
  pool gains the QEMU bulk-output path.

- **CoordinationProtocol: a landed report does not end the lane** (red
  16774, docs only).

- **ProportionalDecks C1 closed by measurement and the design moved to
  `Done/`** (root 16738, 16740; `ExaminersAssay` pointer).

- **Release 46 itself, PR 67, and the Update 47 stub** (red 16570 report,
  README digests, doc counts, seed map + img at `12B07296`; 16575
  `ExaminersAssay` ai-foreword coverage note; 16627 PR 67 landing,
  `contrib/README.md` + `ZigEmitter __deck-set`; 16629 the Update 47 note on
  PR 67). ProductBuilder plan and spec landed under `apps/productbuilder`
  (red 16893, 17121; gitignored, not part of the public tree).

- **Claim-only and row-only copy-ups**, folded here rather than listed:
  root 16643, 16645, 16671, 16742, 16922 (also the merge-down to tip), 17018,
  17067; reek 16782; red 16844, 16854; blu 16689.

## Seeds this cycle, in order

| main CL | agent | seed | what moved it |
|---|---|---|---|
| 17048 | blu | `55E53A81` | issue 70 (Steve Howell), nine `jcc` patch sites in `X86_64Boot.codex` handed `patch-jcc-at` a post-append `code-len`; from `12B07296`; self-verifies per the CL |
| 17063 | root | `7590CCA1` | HAL: `[Gpio]/[Uart]/[Spi]` become real capabilities (`Capability.codex` cs-id 18/19/20) and the foreword handle ops move under them; self-verifies per the CL |
| 17071 | fester | `CAE56FBC665E4C51` | plugs 1.42 closed in the compiler, a unit constructor is no longer emitted as an application (ruling 13a); self-verified, orphans OK per fester's report |
| 17139 | root | `318B2BF6` | HAL read side: linear tuple components are tracked owners, tuple-type parser accepts linear, `gpio-read`/`uart-recv`/`SpiTxn`; one-pass fixed point, 189 refusals |
| 17174 | root | `5B2DE4E6` | HAL `[I2c]/[Adc]/[Power]` capability rows (bits 1/2/13) + linear I2cBus/AdcUnit; `capability-doors` re-recorded 72/72; 193 refusals |
| 17213 | blu | `FAD4F1E24F43961A` | the QEMU bulk-output path: 16550 FIFO detect + `rep outsb` bursts and the 2 s trailer drain; plug-oracle-arith 4.84 s to 2.93 s under WHPX, output byte-identical |
| 17236 | blu | `90646EEB22CEB9AB` | the release battery's finding: the issue-70 fix made the four ATA status waits really loop, and none had an exit for an absent drive (codex-vm answers 0x00, hardware floats 0xFF) or a failed command (0x51, ERR with DRQ clear), so every `Device.Block` program with no disk hung 60 s; guards added; **release seed** |

The release 46 seed was `12B07296419847B2` and held through 16941 and 16977; five seeds moved it today, every one self-verifying per its CL (`Sut == seed`), and the release head 17236 carries `90646EEB22CEB9AB`, content hash prefix `6E56196CC1EABA08`. Six seeds moved on 2026-08-18 alone; the seventh, blu's CDX2064 sibling-argument extension (CL 17122), waits for MAIN OPEN after this push so the proofs ran once.

## The release proof, at head 17236

| proof | result |
|---|---|
| Battery, `-Tier all -Jobs 8` | 1,563 tests, 1,517 pass, 0 fail, 46 skip |
| App sweep (the gate's `-Check`) | 270 units, 265 clean, 5 known-dirty, 0 regressions |
| Poison build (0xCD fill), `-Tier all -Jobs 8` | 1,563 tests, 1,517 pass, 0 fail, 46 skip |
| DDC witness (Roslyn arm) | both arms 2,844,269 bytes, 96 differing bytes all inside the signature region 40..135, 0 outside |

Seed `90646EEB22CEB9AB`, one-pass hard fixed point (`SUT === stage1`), and
`build/output/Sut.cdx` from the release gate is byte-identical to the depot
seed. `Codex.map` refreshed from the gate's `Sut.map` and validated against
the seed's embedded MAP1: 5,304 rows, zero address or size differences.
`Codex.img` rebuilt on the seed (`3C47B2A7..7D594BC4`).

**The first battery of this release, at seed `FAD4F1E2` (head 17213), was
RED: 5 of 1,563.** Every `Device.Block` program run with no disk attached
hung 60 s in silence (`scope-allow`, `effect-widen-scope-narrow`,
`fs-handler-install`, `sector-read-list-growth`, `block-select-drives`).
Bisected across the day's seeds to 17048, the issue-70 fix: nine `jcc`
targets corrected, four of them ATA status waits that had fallen through at
zero iterations for as long as they existed; made to loop as written, none
had an exit for an absent drive (codex-vm answers 0x00 on the status ports,
hardware floats 0xFF) or a failed command (0x51, ERR with DRQ clear). Guards
added at 17236, all five pass in 0.2-3.7 s, and the proofs in the table are
the re-run on that seed. **The DDC's first run was INCONCLUSIVE**: the C#
arm did not build (174 CS0246), because the csharp plug's Lambdas fix of the
same day (16981) wrapped every lambda in a delegate cast and, where the
lambda's parameter type is a free type variable, spelled it `Func<T72, ..>`
at 106 `map_list` sites; the cast is now emitted only for a concrete type
(`has-typevar` guard in `emit-lambda`), the arm builds with 0 errors, and
the witness holds. The standing gate also went red once on the
`vm-differential` arm (a QEMU timeout with no binary, exit 3; rulings
queue 7) while five orphaned arm64 QEMU guests from an earlier cross-battery
self-outage were being killed; green on the immediate full re-run.
