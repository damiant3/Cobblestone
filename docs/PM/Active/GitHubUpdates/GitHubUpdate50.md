# GitHub Update 50

**Scope: main CLs after the Update 49 release push commit, opened 2026-08-21.**
Update 49 covered main 17858 to the release head plus the release's own map,
img, diag, README and report CLs. Accumulate this cycle's themes here as they
land; every number in the final report gets re-measured at the release head,
not carried forward (L-COUNT).

## Open from Update 49

- **Sitting 12 (red composes, Damian sits once):** root's `banked=n` paint at
  the step (19021), the six-part `rings-link` split (19029) and `pchk1`
  listening after the K1 write (18948), reek's `died`/`recovered` sink words
  (18932, 18966) and the K1 control (18874) all ride it. Questions it asks:
  which of swflag or the CTRL|SLU write kills the medium; whether the K1 write
  took; whether the sitting-10 reset hang recurs.
- **The sitting-10 reset hang is open and unexplained**: identical code hung
  at sitting 10 and ran at sitting 11; state-dependent or intermittent.
- **The SWFLAG acquire is a full-register RMW that writes MNG bit 7 and the
  ext-config fields back 2,000 times with no delay** (blu, registered in
  `I219IsNotAnE1000.md`); fix after sitting 12 names the line.
- **B4 step 6** (the repository wire on the part) is open now that B3 flew.
- **NIC-5 and A8's metal arm** still ride a flight.
- **HAL hardware crypto dispatch steps 2 and 3** are blocked on a board
  crypto manual the tree does not hold.
- **CostModel `fixed` rung** stays unshipped until the registry's unknown
  rows are measured.
- **WORKS-25, per-controller USB attachment in codex-vm**: deferred (red,
  2026-08-21) with the size measured in the catalog's prerequisite row.
- **CDX4022's message text is false** (says induction checking is
  unimplemented; it is): seed-affecting one-liner, val's lane.
- **PR 76 closes with the Update 49 push commit named.**
- **Ruling 16 (ProductBuilder stage 6 host)** is customer-gated and the only
  ruling left.

## Landed this cycle

### Interim mirror push, 2026-08-22 (github `ff9eaf4c`, gitlab the same), seed `A01C1547` (unchanged since Update 49)

Not a release: a mirror update carrying main 19069 to 19106, all of it
build scripts, tests and docs, the seed untouched. No release proof was
re-run for it and none was owed: the four proofs certify a seed, and this
seed carried them on 2026-08-21. What it carries is **the battery
choreography** (Damian's direction 2026-08-22, red coordinating, fester on
item 2): **the full battery went from about 10.5 minutes to 123 s** on a
quiet box (phase 1 ~8 min to 58 s, phase 2 151 s to 53 s), measured three
times that day, the first two on a box something else was loading.

- **The batch compiler's parser was quadratic** (red, main 19081).
  `$raw = if (...) { ReadAllBytes } else { ... }` ran the `byte[]` through
  the pipeline and landed an `Object[]` of boxed bytes, so every `GetString`
  and `Array.Copy` re-converted the whole capture: 96 tests 132 s to 0.8 s,
  and the release batches of 193 had spent 417 to 456 s parsing against 20
  to 62 s compiling. One direct assignment in the generator.
- **Batches are dealt by size** (red, 19086): the last run's `.src-bytes`,
  heaviest first onto the lightest batch; round-robin had put 11.0 to 17.4 MB
  per batch, size-dealt 14.2 MB each.
- **`codex-vm -run-list`** (fester, 19092): a supervisor that spawns a FRESH
  `codex-vm` child per line and reports `exit`, `output`, `dropped` and `ms`
  per line, so a batch is byte-identical to N single runs by construction.
  Measured: the `pwsh` child per test was 501 of the 575 ms a test cost, the
  exe's own start 12.6 ms; reusing a process would have bought 2 per cent for
  376 host globals to reset. `build/check-run-list.ps1`, five arms.
- **Both harnesses run their phase 2 through it** (red, 19095 and 19098),
  one supervisor per `-Jobs` slot, proven byte-identical to `test-run.ps1`
  on every sidecar kind. On the way: **a `-RedirectStandardError` file
  anywhere on D: costs ~7.5 ms per stderr line** (2.6 s against 12.3 s for
  the same eight supervisors), now a standing bed fact in the Assay; both
  harnesses capture on the system temp and move the file afterwards.
- **Two root tests renamed** (red, 19100): `engine-culling-cost` and
  `engine-texture-cost` had shared stems with the `forewords/` smoke tests
  and the battery keyed verdicts by stem.
- **`test-run.ps1` releases its writable disk copies** (red, 19102): 9,340
  leaked temp images, 15.7 GB, since 2026-06-15.
- **Open, for the `tools/codex-vm.c` claim holder:** SMP teardown. One SMP
  test per battery run went red and each a different one: `smp-affinity`
  hung at exit for 60 s with its complete output on the wire, `smp-halt`'s
  child faulted on the host (`0xC0000005`) at teardown after its complete
  output. Each green standalone three of three; the harness passes the case
  as `test-run.ps1` always did and the register records it.

## The 2026-08-24 interim push: Steve Howell's six PRs absorbed

Interim mirror push, not a release: github `111c0fea` (master), gitlab the
same commit (main), from main 19154. Seed `6CF4A8E0` (2,876,035 bytes),
installed per 4.3b off the 19140 gate's one-pass fixed point and proven by
the full public gate before the copy-up (608.4 s green: 1436 chapters
compiled, 57 generators 0 drift, deck-headroom floor 1.28, app sweep 265
clean 0 regressions, vm-differential both hosts agree). No release proofs
owed or run; img and map unchanged.

What it carries, all reviewed against source before landing (red):

- **PR 77** (19125): the zig plug's allocator becomes bare metal's model --
  one 4 GiB lazily-faulted region, one bump frontier, the deck a true second
  cursor with a crossing guard in both directions, exact list capacities,
  copying text results, trapping substring, heap-relative `address-of`.
  Rides with the emit deck flat term 24 to 28 MB (`X86_64Chapter.codex`),
  measured both arms. The design and every measurement are his.
- **PR 81** (19131): a self tail call in the zig plug emits as a loop;
  887 of 3,633 compiler definitions loop; `zigemit` on the 13.2 MB fibx IR
  ran at stock 512 MB stack where 3.5 GiB had not sufficed.
- **PR 83** (19133): over-application applies the rest through the returned
  closure instead of calling flat; his tier-14 detector comes back online
  and isolates COMPILER-18 alone.
- **PR 82** (19140): the parser's two top-level scans return their item
  instead of mutually tail-calling, the shape every TCO already flattens;
  his measurement, 32 MB minimum transpiled stack to 4.
- **PR 79** (19116): COMPILER-18 recorded -- the native partial-application
  closure carries no remaining-arity word, so under-application corrupts
  silently. The representation ruling is in CurrentPlan Pending.
- **PR 80** (19117): plugs 1.57 recorded -- riscv ships a correct over-apply
  helper nothing calls, java never consults its arity table. Ruled binding
  (call 21); wiring is close-out work.

Tweaks made on top during absorb, so the credit stays honest: one duplicated
prose block trimmed from PR 82's parser change (R-PROSE; the scan-side
rationale and the state-re-read constraint stay), and nothing else altered.

## The Update 50 release

**Release head: main 19777. Seed `C45E5825` (2,922,230 bytes,
SHA-256 `C45E582526BAB7BBA313059F9AAFD57FBFA142F358EAAD437B9412C6EE56F9BB`),
unchanged from the freeze; every proof below ran against it.**

### The compiler compiles itself in WebAssembly, and in a browser tab

The headline of the cycle, all fester:

- **The wasm plug self-compiles the compiler byte-identically to x86-64**
  (plugs 1.81, main 19763): its own 2,945,373-byte source, 2,460,088
  characters of emitted text, SHA-256 `B3491BE7..` from the wasm module and
  from codex-vm running `Sut.cdx` alike, zero diagnostics, five seconds on
  either target. The mechanism that unlocked it is saturating closure
  application (`$clo_applyN`), caught by a helper census reading 21.2M
  one-argument chain calls in one phase span.
- **`return_call` holds the self-compile at a 1 MB stack under wasmtime**
  (plugs 1.82, main 19767): 2,874 `return_call` sites in the compiler's own
  module; mutual tail recursion runs constant-stack. The stack claim is
  wasmtime's, deliberately: V8's frames are fatter and the same depth needs
  1-2 MB there (fester's node repro, register 1.83).
- **The page is real and witnessed** (plugs 1.83): the compiler as a wasm
  module in a static webpage, source beside it, phases streaming, and on
  completion the page hashes its output in the tab against a bare-metal
  anchor computed at page build. Damian's browser, 2026-08-25: 2,460,178
  characters in 19.0 s, hash `6F0A4122..` computed in the tab, equal to the
  anchor to all 64 characters. Conditions attached: main-thread fallback
  (a browser worker's 1 MB stack dies on the emit spine's non-tail
  recursion; the retry is itself the measurement), `decks=125`, the page's
  own anchor. De-recursing the emit spine is Update 51 scope.

### What the release proofs found: the lambda lift had broken the DDC

The one red this release surfaced, and the fix it shipped. 19558's
lambda-lift fix (plugs 1.70) put lifted `__lam_N` defs on every plug's IR
wire with unresolved type variables in their signatures (the lift runs
after resolve, so a lifted def carries the expected types its lambda was
handed). Roslyn refused the emitted C# arm: 484 errors at the freeze,
which made the DDC witness INCONCLUSIVE with every other proof banked
green. The fix (main 19775, 19777): tvar-typed lam params emit as
`dynamic`, lam names convert through bare adapter lambdas, and the four
CS1977 sites (a `map-list` over a field read off a dynamic value is a
dynamically dispatched call, which C# refuses to hand a bare lambda) route
through `_Buf.dmap` with the receiver cast to the non-generic IEnumerable,
so the binding is static and the result list keeps the input's runtime
element type. Update 47's parking-lot entry predicted the class ("a
csharp-plug change is a DDC change"); this cycle proved the converse, a
compiler change that reshapes the IR wire is a DDC change too, and only
the DDC sees it.

### The release proofs

- **Full gate** (`build/build.ps1`, every phase): green, 757.4 s, text leg
  and sem-equiv included.
- **Battery** (`-Tier all -Jobs 8`): total 1,658, pass 1,608, fail 0,
  skip 50.
- **App sweep** (`-Check -Jobs 8`): exit 0, no regressions against
  baseline.
- **Poison build** (0xCD fill): one timeout (`smp-affinity`) re-run green
  in isolation, zero 0xCDCD faults.
- **DDC witness**: WITNESS HOLDS. Run in the `-main` workspace against the
  depot at head 19777: the Roslyn-built C# arm and the codex-vm seed arm
  each produced 2,922,230 bytes from the same input, with 95 differing
  bytes, every one inside the signature region at offsets 40..135 and
  none outside it.
- **Artifacts**: `seed/Codex.map` refreshed from the gate's `Sut.map` and
  validated against the seed's embedded MAP1 (5,351 of 5,351 names, zero
  address or size differences); `seed/Codex.img` rebuilt against the
  release seed (SHA-256
  `6E1A9A5954874233D03B56660C338C5779342F2622E7A33089B83ED486052C7F`);
  `build/boot/diag.img` rebuilt against the release seed and rehearsed in
  full (SHA-256
  `6F3124AC0E5A71E2E270DF9BD23FED9BA5E87311CCBBEB9FBA4EB81FA3C3E08A`).
  The rehearsal's first full run went red on one arm, `b3-banklost`: its
  expected note name was a literal re-pinned the day before, and the bank
  size drifts run to run, so the note the death lands on flaps across the
  11-sector boundary. The arm now derives the expected note from the
  serial trail (first refused note must be a b3 reset-* step, and the
  medium's last note must be its predecessor), which keeps every
  falsifiable claim and stops asserting which reset step dies, which was
  never the arm's subject.

### The CCE/Unicode defect family (blu)

COMPILER-20 (saturated call returning a function), COMPILER-21 (tier-0
Cyrillic low-byte branch, three print paths fixed), COMPILER-22 (encode
direction, three further readers), COMPILER-23 split into A/B/C with the
char-to-text truncation ruled (split primitive; char-encode landed 19579;
the bounded 0..255 domain waits on COMPILER-28). COMPILER-28: bounds on
non-Integer bases were silently decorative, zero in-tree reliance, census
about 875 source declarations.

### Steve Howell's eight PRs, absorbed same-day (84-91)

The zig stack note; the zig self-tail shadow fix (plugs 1.58); the
overapply corpus fixture (1.59, measured red at his seed and green at
head); the python TCO guard gap (1.72); the ir- prefix rename; the
Linux/QEMU runner gap (1.73, RULED supported); COMPILER-26
(AST-incomplete tparams); COMPILER-27 (advisory deck reservation, 421 of
512 MB proximity).

### The rest of the cycle

- **QEMU is a supported plug host everywhere** (Damian's ruling): plug-run
  honors vm-config selection (reek 19743), and plug-smoke gained a QEMU
  arm with a byte-identical cross-host requirement.
- **The windows campaign** (val): ShellRefinement 6.3
  focus/maximize/minimize, 6.4 first units, 6.5 every-pane-a-window (nine
  panes plus Browser), 6.6 (19720), taskbar pill icons (19742).
  `Window.codex` deleted (WORKS-49, L-UNCALLED).
- **Two red-main incidents, both same-day caught, both fixed forward**:
  builtin-name-shadow (19551 to 19568, new guard arm plus
  nullary-by-registry-type) and the sem-equiv prose mismatch (19652 to
  19725; sem-equiv is now R-PROSE's first runner). Gate scoping hardened:
  compiler-wide test-compile descoping ruled; sem-equiv trigger widening
  deferred to next cycle.
- **README split**: `README.md` is the business page (Damian's draft),
  `TechnicalDetails.md` carries every measured claim; the checker and the
  release skill are repointed, eleven live docs redirected.
- **Vec builtins** (reek): four of five repaired with guards; vec-empty's
  two halves remain. `arm64-build-elf` deleted (L-UNCALLED).
- **COMPILER-24** (recursive `==` crash) and **COMPILER-25**
  (emit-zero-region prefix order) recorded, unowned.
- **CostModel census**: 132 to 107 unknowns (print, VMX/MSR/UEFI,
  process/channel families).
- **The email channel**: cobblestone.project.agent@gmail.com, Steve-only
  reply protocol, instruction census delivered (105 mnemonics).