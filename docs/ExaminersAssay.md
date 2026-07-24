# Examiner's Assay

Test infrastructure, coverage, and known results for the Codex
compiler, OS, and library stack. For build process and gate
mechanics, see `OperatorsManual.md`. This document records what
is tested, what passes, and what remains.

## Test Battery Layout

```
codex/test/              Root-level tests (default battery)
codex/test/errors/       Expected-failure tests (bad programs that must reject)
codex/test/apps/         Application and integration tests (-Apps or -All)
codex/test/forewords/    Per-chapter foreword compile tests (-FW or -All)
codex/test/lib/          Library tests
codex/test/examples/     Worked examples (e.g. missile-warning)
bench/codex/             Codegen-quality shapes (instruction count and bytes,
                         not correctness -- see "The repro you kept is the test")
```

The default battery runs `codex/test/*.codex` + `codex/test/errors/*.codex`.
Use `-Apps` for the full application suite, `-All` for everything.

`bench/` is not part of any battery and is not run by the gate. It is the
only instrument that can see a change which keeps a program correct and makes
it worse, so a codegen fix pins its shape there the way a rejection pins its
shape in `codex/test/errors/`. Build and compare with `bench/compare.ps1`.

## Sidecar Files

Each test `foo.codex` may have sidecars that control its behavior:

| Sidecar | Effect |
|---------|--------|
| `foo.expected` | Compile must succeed; runtime serial output must match byte-for-byte (after CR stripping) |
| `foo.failing` | Compile must fail with the listed CDX error codes |
| `foo.diag` | Compile must succeed and emit each listed CDX code at any severity (warning/info/error). One code per line (bare number or `CDX`-prefixed). Combine with `foo.expected` to also check runtime output. This is how warnings and infos are regression-tested. |
| `foo.skip` | Skipped entirely; first line is the reason |
| `foo.slow` | Skipped unless `-Slow`; first line is the reason |
| `foo.fatal` | Skipped unless `-Fatal`; kills the VM at runtime |
| `foo.flags` | First line is appended to the test's **compile mode line**, so the test states its own compiler requirements. `prose` selects CPL; `passes=+name` adds an IR pass; `decks=N` scales every phase deck floor to N per cent, which is what a compilation unit larger than the floors were sized for needs (`codex/test/apps/foreword-all-compile` cites all 416 foreword chapters and carries `decks=150`; without it the compile is `CDX9002: Deck overflow in LOWER`, run both ways 2026-07-22). Read by `build/test-compile-batch.ps1`, so it applies to the battery and **not** to a hand-run `build/compile.ps1`, which takes the same settings as switches |
| `foo.stdin` | Pumped to VM serial after boot (runtime input) |
| `foo.keys` | Scancode timeline (`t:scancode` per line, t = ms since boot, `#` comments) handed to codex-vm as `-keys-file`. **Not interchangeable with `.stdin`** — see below |
| `foo.disk` | Attached as IDE disk image via codex-vm `-disk` flag |
| `foo.smp` | Core count. The test is booted with `-smp N`. This is how a test covers multi-core; without it every test boots single-core, which is why nothing exercised SMP for so long (`codex/test/smp-cores.codex` is the first) |
| `foo.vmargs` | Extra codex-vm flags, whitespace-separated, `#` comments and blank lines ignored. For a test whose subject is the MACHINE rather than the program: a bus topology, an absent device. Before it existed, such a test could only be a `.skip` with the command in its prose -- an unrun test, which proves less than no test because it reads as coverage. `codex/test/apps/usb-kbd-hub` is the first, passing `-xhci-no-root-kbd` to unplug the root keyboard so the hub is the only route to one |

A test with no sidecar compiles but is unverified (PASS_UNVERIFIED).

### `.stdin` and `.keys` are different machines

`.stdin` is pumped into the **serial ring**, which is where `read-line`
looks. A keyboard read — `uefi-read-key`, and `poll-key` above it — reads the
**PS/2 key cell** instead, and no quantity of `.stdin` has ever reached it.

That is the whole reason a dozen tests are skipped as "blocks waiting for
keyboard input": the battery had only the wrong input, and the skip reasons
recorded the resulting silence as an environment limitation. codex-vm has
accepted a scancode timeline through `-keys-file` for as long as
`build/test-gui.ps1` has driven GUI apps with one; the default harness simply
passed no such flag. It does now.

**Pick by what the code under test reads, not by what it feels like.** Reading
the source is the only reliable way, and getting it wrong is not theoretical:
`diagnostic-boot` was skipped for years as keyboard-blocked and its shell
reads `read-line` — plain serial. It runs today on a `.stdin` sidecar, in
under a second. `vga-shell-test` still carries both a `.stdin` and a
keyboard skip reason, which cannot both be right.

`codex/test/keys-sidecar` is the worked example and the harness's own
regression: it types `A B Enter` as make codes 30, 48, 28 and must report
65, 66. It has no fuel cap on purpose — a keyboard test that stops early
would pass while proving nothing, so not receiving a key must be a failure,
and the wall budget is what calls it.

### A sidecar names a test, and `build/check-sidecars.ps1` checks that it does

Sidecars are resolved **next to the source**: `codex/test/apps/foo.codex`
takes `codex/test/apps/foo.skip`, and a `foo.skip` one directory up is read
by nothing. Such a file is worse than absent — it reads like a decision.
`diagnostic-boot.skip` sat a directory above its test making three claims,
none true: it skipped nothing, the test was never run anyway (no `.expected`),
and its stated reason was the wrong subsystem. This table quoted it as a
legitimate skip the whole time.

`build/check-sidecars.ps1` hard-fails `build.ps1` on any sidecar with no
`.codex` beside it. Nothing else can see this class: a sidecar that names no
test is exactly the file no compile and no battery opens.

### The diagnostic catalogue is read by the build, and `build/check-cdx-registry.ps1` reads it

`codex/compiler/Core/CdxCodes.codex` is a registry of every CDX code with its
name, severity, phase and one-line summary. **Nothing in the compiler reads
it** -- `cdx-lookup` has no caller, and whole-program dead-code elimination
prunes the whole table (measured: changing a summary leaves `Sut.cdx`
byte-identical to the seed, so a string the compiler reached would have moved
the binary). A code raised with no row, a row for a code nothing raises, or a
`Name` that has drifted from its constant is therefore invisible by
construction -- which is how CDX6011 came to call an instruction-count budget
a "byte threshold" with nothing to catch it.

`build/check-cdx-registry.ps1` is the reader. It parses the constant->code
table, the registry rows, and every `cdx-*` reference across `codex/compiler`,
and fails `build.ps1` on three disagreements: a code **raised but not
registered** (`cdx-lookup` would answer "Unregistered CDX code"), a row
**registered but raised by no site** (a summary nothing can ever show, the
CDX2043 class), and a row whose **`Name` is not the PascalCase of its
constant** (the one half of a summary a machine can check). It found **13
undocumented codes on its first run** (the `if`/`let`/`act`/record-field/
Real-equality parser and checker errors), now rowed. It does not verify
summary PROSE against behaviour -- no build-time check can, and the registry
chapter is not citable so a Codex test cannot read it either (the 7.25
blocker) -- so a wrong summary on a correctly-named, correctly-raised code
still passes. What it stops is the structural rot: a code with no
documentation, and a table that describes codes the compiler no longer emits.

## Current State (2026-07-13, seed 5A6B432B...)

Both batteries were re-run from scratch on 2026-07-13. **These are
measured numbers, and they are already going stale — re-measure before
you quote them. Never carry a count forward.**

| Battery | Command | Total | Pass | Fail | Skip |
|---|---|---:|---:|---:|---:|
| Default | `build/test.ps1 -Jobs 4` | **342** | **328** | **0** | **14** |
| Full | `build/test.ps1 -Apps -Jobs 4` | **598** | **498** | **9** | **91** |

The default battery is green. **The catalogue of `-Apps` failures that used
to sit here is gone, because every row in it was re-measured individually
on 2026-07-20 and every one now passes**: `boot-stage-test`,
`quaternion-test`, `spark-shapes-test`, `spark-mesh-test` and `wave3-test`
compile and match their `.expected` with no change needed at all -- they
had been fixed by other work and nobody re-measured. `erp-server-test`
passed too and was `.skip`ped on a CDX2033 that no longer occurs; the skip
is deleted. `historian-test-full` had no `.expected`, so nothing ran it;
it has one now. `db-full-test` was the one row with real defects behind it
and they are fixed (CL 9850).

`nic-ping` was the ninth and was **fixed 2026-07-13** (val).
`xhci-discover-test` was the eighth and is **gone entirely**: its runtime
failure was a bounds contract, fixed, and the test then went with the kernel
xHCI driver when that duplicate stack was retired (CL 9180, fester). That is one
fewer entry, not a re-measurement.

**The header's `9` is therefore stale in the direction of pessimism, and it
stays as written because the battery itself has not been re-run.** A count
in this document is only ever the number some run actually produced; per-test
re-measurement retires the rows it covers and does not license editing a
total nobody measured. Re-run before trusting any of these figures.

`codex/test/errors/` holds **132** expected-failure tests (measured
2026-07-16; the 127 this line carried was already stale).

## Battery architecture

99 individual tests are consolidated into smoke bundles (`unit-smoke`,
`rt-smoke`, `try-smoke`, `prose-smoke`, `linear-smoke`, `linear-errors`,
`mutable-smoke`, `typeclass-smoke`, `handler-smoke`, `record-smoke`,
`lang-smoke`, `bs3-smoke`, `punctual-smoke`). Each exercises several
features in a single VM boot, cutting battery time ~60%.

**BVT mode is what `build/build.ps1` runs by default**: a 10-test subset,
~18 s. That is the standing gate. The full battery (`build/test.ps1`) is
Damian's tool and is not an agent command.

### Foreword Battery (`build/test.ps1 -FW`)

Per-chapter compile tests for all foreword modules. Not included
in the default or `-Apps` battery. Some foreword tests have large
dependency chains (~127s compile) and are marked `.slow`.

## The repro you kept is the test

**Every fix ships the smallest program that failed.** Not as ceremony
afterwards — you built one to find the bug, and the rule is only that you
keep it.

The reason is `docs/PM/Active/Stories/Opus.md`'s, and it is about the search
space before it is about coverage: a 40-line file that reproduces the bug
gives you a 40-line search space, and the codebase gives you the whole
codebase. The ratio matters more than anything, which is why the minimal
repro is the debugging tool and not the paperwork. Having paid for it, keep
it: the thing that found the bug is the thing that proves the fix, and the
thing that stops it coming back.

**Whatever can see the failure is the instrument.** The by-construction
holes are the loudest example — linear laundering, effect laundering,
capability laundering, bounded-signature out-of-range each shipped its
adversarial probe as a `codex/test/errors/*.failing`, which is why the error
battery is the fastest-growing part of the suite — but a `.failing` is what
you use when the compiler must *reject*, not what the rule is. A wrong
answer wants a `.expected`. Code that is correct but wasteful wants a
**bench**: no correctness test can see a redundant instruction, because the
program computes the right answer either way. That is what `bench/` is for,
and why it is in the battery layout above despite not being a battery.

**The instrument has to be able to fail** — that much this document says
twice already. It also has to be able to fail *at the thing that changed*,
and a suite can be blind by construction rather than by accident. CL 8600
taught the epilogue fall-through to skip blocks that emit nothing and, by
naming a block one way and the layout another, made an arm emit a five-byte
jump to the instruction underneath it. Nine benches were green. All nine are
tail-returning, so not one of them had a non-tail join exit at all — the
shape was not under-covered, it was absent. Ask what your suite cannot
express before you read its silence as agreement.

**Pick the instrument before claiming the shape is covered.** Sometimes the
obvious one is provably blind. Sharing an epilogue against duplicating it
costs exactly the same number of instructions at two returns and a
two-instruction epilogue — the equality is arithmetic, not luck — and
differs only in bytes and executed jumps. A bench counting instructions
there is a test that cannot fail, so the harness reports bytes too. This is
the same sentence as "a function that always answers the same thing looks
exactly like one that works," pointed at the measurement instead of the
code.

## Cross-Architecture Battery

The cross-architecture test harness compiles each test from
`codex/test/*.codex` to ARM64 or RISC-V ELF via the plug pipeline
(source -> IR -> plug codegen -> ELF writer), then boots the ELF on
Renode (cycle-accurate board simulation) or QEMU and compares UART
output against the `.expected` sidecar.

### Pipeline

```
source.codex -> compile.ps1 (IR mode, x86-64 seed)
             -> arm64/riscv plug CDX (codegen)
             -> compile-arm64/riscv.ps1 (ELF builder)
             -> Renode or QEMU (UART capture)
             -> compare against .expected
```

### Commands

```powershell
build/test-cross.ps1 -Arch arm64 -Test <name> -TimeoutSec 10   # single test
build/test-cross-batch.ps1 -Arch arm64 -RenoTimeout 10         # full battery (Renode)
build/test-cross-batch.ps1 -Arch arm64 -UseQemu                # full battery (QEMU)
build/test-cross-smp.ps1 -Arch riscv64 -Test smp-riscv-boot    # one multi-core test (QEMU -smp N)
build/test-cross-batch.ps1 -Arch arm64 -Filter lir-            # a subset by name
build/test-cross-batch.ps1 -Arch arm64 -CompileTimeoutSec 2    # provoke the retry (see below)
```

### The parallelism default, and the flake that was not the harness's

`-Jobs` defaults to **8**. It was 4, and the 4 was a workaround: at eight
Renode slots a passing test would come back `FAIL_RUNTIME` with no uart after
about two seconds. That was investigated twice, no cause was found inside the
harness, and the default was halved instead.

**The cause was the machine.** Its DDR5 was running an XMP profile it was not
stable at. With the memory back in spec the box has run 25+ concurrent VMs with
no fault (2026-07-22), and the default is 8.

Worth keeping because the shape recurs: two investigations that cannot find a
cause inside the software are evidence about where the cause is not. A
workaround written into a default outlives the condition that justified it and
then reads as a property of the harness. If slot-count flakes return, suspect
the hardware first.

### A load flake is retried before it is believed

**The batch used to report contention as a test result, and it cost three
sessions of re-diagnosis.** A compile is given a wall-clock budget while
`-Jobs` of them run at once, so a large test can exceed it because the box is
busy; the harness recorded that as `FAIL_COMPILE`, which reads exactly like a
broken plug. `db-full-test`, `gpu-depth-tree` and `gpu-panel-border` flipped
between `FAIL_COMPILE` and `FAIL_OUTPUT` from run to run on that alone, and
the standing instruction *"always re-run a batch FAIL standalone before
believing it"* was a manual step covering for the harness.

The harness takes that step itself now. After each phase, the failures that
are **contention-shaped** are retried once, alone:

| Phase | Retried | Not retried |
|---|---|---|
| Compile | `timeout (Ns)` | a non-zero exit **with** a compiler diagnostic |
| Compile | a non-zero exit with **no** diagnostic | |
| Run | `no uart output` | `FAIL_OUTPUT` -- a deterministic wrong answer |

**The no-diagnostic class was added when `-Jobs` went to 8**, and it is the
distinction that keeps "never re-run a real failure" true rather than merely
stated. A compile that rejects a program always says so: a CDX-numbered error
or `CODEGEN-HALTED` lands in the log. A compile whose VM was killed under load
exits non-zero and says nothing. Before eight slots the second class was rare;
on the first 8-job run `chapter-pages` hit it, reported `FAIL_COMPILE`, and
passed standalone -- a phantom regression of exactly the kind the timeout retry
exists to prevent. Both directions were fired rather than assumed: an injected
one-shot no-diagnostic exit recovers (`compile 1/1 recovered`), and a
deliberately broken source exits 3 **with** diagnostics and is not retried at
all (`compile 0/0`).

**A wrong answer is never re-run.** Re-running one spends time and, worse,
would let a genuine intermittent defect be dismissed as a flake. The
distinction is the whole design: silence under load is the machine's fault,
a wrong answer is the program's.

**Every run states its retries**, including when there were none:
`**Serial retries**: compile 0/0 recovered, run 0/0 recovered`. A run that
silently absorbed a flake would read identical to one that never had a flake,
and that difference is exactly what a per-test diff against a previous run
trips over.

**Both paths were fired rather than assumed**, because an unfired guard is
worth what no guard is worth:

- **Compile retry recovers**: `-CompileTimeoutSec 2 -Jobs 4` makes four
  contended compiles time out at 2.0s; all four compile alone in 1.8-1.9s and
  the run reports 4 PASS where it would previously have reported 4
  `FAIL_COMPILE`.
- **Compile retry does not rescue a real failure**: at
  `-CompileTimeoutSec 1` the test times out alone too and stays
  `FAIL_COMPILE`, noted `still failing alone`. A deliberately broken source
  exits non-zero and is **not retried at all** (`0/0`).
- **Run retry recovers**: proven with a one-shot injected `no uart output`
  (fail first attempt, pass on retry) -- 1 of 1 recovered, the row noted
  `passed on serial retry`. Made persistent, the same injection gives 0 of 1
  and the row reads `still silent alone`. The injection was removed; it is
  recorded here because no test in the tree currently produces this class, so
  there was nothing real to fire it with.
- **A wrong answer is untouched**: a valid program with a deliberately wrong
  `.expected` stays `FAIL_OUTPUT` with `run 0/0 retried`.

`-CompileTimeoutSec` exists to make the first of those provocable on demand;
it is also why the budget is no longer a literal buried in the parallel block.

### Multi-core cross tests (`.smp` sidecar)

The committed Renode board `.repl` files are single-core, so a cross test
that needs a second core runs under QEMU instead, via
`build/test-cross-smp.ps1`. A test opts in with a **`.smp` sidecar** whose
first line is the core count (the same sidecar name the x86 codex-vm
battery uses). The runner boots QEMU and compares UART output against
`.expected`, and the two backends boot differently: **RISC-V** boots the
flat `.bin` at `0x80000000` with `-bios none` (every hart auto-enters
`__start`); **ARM64** boots the ELF (QEMU honours its `0x40100880` entry)
and the secondaries are held in PSCI until core 0 starts them. Both use
`-m 1024M` (each backend's boot stack sits ~1 GB above the RAM base). The
single-core batteries (`test-cross.ps1`, `test-cross-batch.ps1`) **skip**
any test with a `.smp` sidecar, so a multi-core test is only ever run by
`test-cross-smp.ps1` — never single-core, where it would fail by design.

Two tests exist, both `.smp` = 2, both proving a secondary core executes
guest code (closed), each reading a cell only a non-zero core
writes (the `smp-cores` discipline) — "an ap executed guest code" at
`-smp 2`/`4`, "AP DID NOT RUN" at `-smp 1`. A test that cannot fail proves
nothing; these fail single-core.

- `codex/test/smp-riscv-boot.codex` — hart 0 boots; a non-zero `mhartid`
  branches in `__start` to an AP path that marks `0x80090000` and parks
  (park-the-secondaries; no SBI).
- `codex/test/smp-arm64-boot.codex` — QEMU holds the ARM64 secondaries, so
  core 0's `__start` issues PSCI `CPU_ON` (conduit HVC, id `0xC4000003`) to
  start core 1 at an AP stub that marks `0x60000000` and parks.

### ARM64 (last full measurement 2026-06-27, CL 6173)

> **This number is in dispute and must be re-measured before it is
> quoted.** The 2026-06-27 run recorded 135/135 (100% parity). A
> 2026-07-13 audit of the cross-arch harness reported **132 pass / 2
> fail** under the committed Renode board. Nobody has re-run the full
> ARM64 battery to settle it. Until someone does, treat ARM64 parity as
> *approximately* 132–135 and not as a proven 100%. Re-run with
> `build/test-cross-batch.ps1 -Arch arm64`. Tracked nowhere but here.

The 2026-06-27 measurement, for reference:

| Category | Count |
|----------|-------|
| PASS_EXPECTED | 135 |
| PASS_UNVERIFIED | 2 |
| SKIPPED | 17 |
| FAIL | 0 |
| **Total** | **154** |
Skips: 15 pre-existing (error tests, fatal tests, hardware-dependent)
+ 2 slow (tls-test: X25519 DH exceeds Renode sim budget; ui-orchestrator-test:
17-module dependency chain exceeds IR compile budget).

Codegen quality beats GCC -O0 on aggregate across four
micro-benchmarks (static instruction count, AArch64):

| Bench | GCC -O0 | GCC -O2 | GCC -Os | Codex ARM64 |
|-------|--------:|--------:|--------:|------------:|
| fib   |      20 |   237*  |      16 |          21 |
| fact  |      17 |      15 |       9 |          13 |
| gcd   |      21 |       8 |       7 |          23 |
| sum   |      20 |      13 |       9 |          17 |

*GCC -O2 fib transforms tree recursion into a 237-instruction
unrolled loop. GCC -Os is the fair comparison for recursive codegen.

Total: Codex 74 vs GCC -O0 78 — Codex beats GCC -O0 aggregate.
fact (13) beats GCC -O0 (17) by 4 and beats GCC -O2 (15) by 2.
sum (17) beats GCC -O0 (20) by 3. fib (21) is 1 from GCC -O0 (20).

Key optimizations (CLs 6141-6173): destination-driven emission,
direct arg emission, compact prologue with accurate local counting,
TCO skip-save for stable-reg args, CMP-immediate for comparisons,
peephole MOV eliminator with NOP compaction (branch + ADR offset
adjustment), STP-pre/LDP-post frame merge.

Renode board: Cortex-A53, GICv3, PL011 UART, 1 GB RAM at 0x40000000.
QEMU: `-M virt -cpu cortex-a53 -m 1G -kernel <elf>`.

### RISC-V (2026-06-28, CL 6287)

| Category | Count |
|----------|-------|
| PASS_EXPECTED | 135 |
| SKIPPED | ~18 |
| FAIL | 0 |
| **Total** | **~153** |

135/135 verified tests pass (100% parity with x86-64 battery).

Codegen quality on all eight micro-benchmarks (static instruction
count, RV64). Four benchmarks beat GCC -Os:

| Bench   | GCC -O0 | GCC -O2 | GCC -Os | Codex RV64 |
|---------|--------:|--------:|--------:|-----------:|
| fib     |      34 |   241*  |      22 |         20 |
| fact    |      27 |      14 |       9 |         14 |
| gcd     |      26 |       8 |       6 |          7 |
| sum     |      27 |      11 |       9 |          7 |
| ack     |      33 |     103 |      22 |         24 |
| tak     |      36 |      33 |      34 |         39 |
| collatz |      29 |      20 |      13 |         15 |
| locals  |      52 |      25 |      19 |         15 |

*GCC -O2 fib transforms tree recursion into a 241-instruction
iterative loop. GCC -Os is the fair comparison for recursive codegen.

Two optimization campaigns (CLs 6159-6172, 6261-6287). Phase 1
(8 CLs): deferred save-reg reduction, destination-driven emission,
frameless TCO, inline builtins, compact prologue. Phase 2 (17 CLs):
pow2 strength reduction (srai/andi/slli), NOP compaction with
B/J-type offset fixup, direct TCO for 2-arg tail calls, dead-jump
elimination, skip-save for simple binary operands, ra-skip for
pure-TCO, expanded frameless TCO with temp-only locals, direct
arg-reg emission for N-arg calls, reordered mixed-TCO (call-first
simple-after), last-arg skip in TCO shuffle.

Renode board: RV64GC, PLIC/CLINT, NS16550 UART, 1 GB RAM at 0x80000000.

### Plug Build

Both ARM64 and RISC-V plugs are standalone CDX binaries built by the
x86-64 seed. Rebuild with `codex/plugs/arm64/build.ps1` (~90s) or
`codex/plugs/riscv/build.ps1`.

## Skip Inventory

### Unwritten Tests (the largest and least-visible skip class)

Roughly **60** `.skip` sidecars under `codex/test/apps/` — plus five
annotation tests (`annotation-author`, `-driver`, `-migrate`, `-reader`,
`-transport`) — carry the reason **"stub: test body not yet written"**.

This is not a defect list. It is coverage that was never written, and it
was undocumented here until 2026-07-13. It is the single largest gap in
what this document is supposed to measure, and it is tracked nowhere
but here.

### Legitimate Skips (cannot run headlessly)

| Test | Reason |
|------|--------|
| vmx-init-test, vmx-launch-test, vmx-serial-test | VMX requires CPL 0 + VT-x; no nested VMX under WHPX |
| vga-terminal-demo | Requires display + keyboard (`run-vga-demo.ps1`). This row also claimed 13 CDX2051 errors of its own; **compiled 2026-07-19 it has none**: zero errors of any code, 41 CDX4010 infos and 5 CDX3005 warnings. The skip is legitimate; the error count was stale |
| vga-shell-test | Skipped as "blocks waiting for keyboard input" — **but it carries a `.stdin`, so somebody believed it reads serial.** Both cannot be true. Read `VgaShell`'s input path and drive it with whichever sidecar matches; it is one of the two candidates left in this table |
| firstboot-lite | Needs RDRAND, which codex-vm does not supply (the cell reads zero; the stub only fills it on real hardware). **Its skip was orphaned a directory up and applied to nothing** — the test compiles on every `-Apps` run and has no `.expected`, so it is PASS_UNVERIFIED, not skipped. The orphan is deleted; the compile coverage is deliberately kept |
| first-boot-ceremony | Output depends on RDRAND (different pubkey each boot) |
| firstboot-identity-test | Requires RDRAND for keypair generation |
| identity-persist-test | Requires DiskFacts persistence across reboots |
| editor-notify-test | Requires interactive console editor session |
| fat16-read-test | Requires disk I/O with FAT16 image |
| gpu-bridge-test | Requires GPU hardware |
| gfx-desktop-demo | Requires GOP framebuffer display |
| run-process-full-test | Invokes dotnet which is not available on bare metal |
| watchdog-panic-probe | Deliberate infinite loop; not part of standard sweep |

### Known Defects

| Test | Issue |
|------|-------|
| ~~db-mini-test~~ `Page.codex` | CDX2000: `emit-field-access` cannot resolve the type of a chained field access (`pg.header.slot-count`) — 21 errors in that chapter alone. **There is no `db-mini-test`**: no such `.codex` has ever existed in main, and this row described a test by the name of the orphaned `.skip` that was the defect's only record. The defect is real and is recorded only here; the sidecar is gone. Do not go looking for the test — write one. |
| linalg-test | `mat-mul` GPFs at runtime. |
| probability-test | `normal-cdf 0` returns -253. |

### Slow Tests (`.slow`, run with `-Slow`)

| Test | Reason |
|------|--------|
| image-codec-test | Large foreword dependency chain (~127s compile) |
| klondike-test | Large foreword dependency chain (~127s compile) |
| let-effectful-bug | Large foreword dependency chain (~127s compile) |

## Disk Tests

Tests that exercise block I/O use `.disk` sidecar files. The test
harness passes these to codex-vm via the `-disk` flag (IDE PIO
attachment). Two patterns:

- **Write tests** (disk-facts-init, disk-facts-multi, boot-init):
  use a blank 1 MB disk image. The test writes to it.
- **Read tests** (disk-facts-read, disk-facts-load, disk-facts-multi-load):
  use a pre-populated disk image created by running the corresponding
  write test. The `.disk` file in the depot is the frozen output.

`block-io-basic` uses a 512-byte image with value 42 in the first qword.

**A disk test must attach a disk and demand a specific number.**
`block-identify` needs no disk and asserts 0 sectors when none is
attached — which is true whether the sector count works or not. It
therefore could not, and still cannot, catch a stuck counter, and
`block-sector-count` reported 0 on every disk for months behind exactly
that gap: the disks read perfectly and reported themselves empty. The
test that found it (`codex/test/block-sector-count`) attaches a 64 KB
fixture and demands **128**. A function that always answers the same
thing looks exactly like one that works.

## A Kernel On The Actual Device (`codex/test/gpu-ptx`)

Every other operation the compute bridge serves is computed by codex-vm on
the host CPU in scalar C, which is a transport exercise and not a graphics
processor. `gpu-op-launch-ptx` is the one command that reaches the card:
the guest hands over a PTX module and an entry point, `com3_launch_ptx`
JITs it through the CUDA driver API (`nvcuda.dll`, loaded at first use --
no toolkit, nothing linked against) and launches it.

**This test needs an NVIDIA GPU, and on a box without one it FAILS rather
than skipping.** That is deliberate and it is the same argument the skip
inventory makes elsewhere: a skip reads as coverage, and the claim here is
precisely about hardware. A failure naming the absent device is the honest
answer on a machine that does not have one. The dev box has an RTX 4060 Ti,
so the claim is checkable in-loop rather than against a simulator that
cannot reproduce the failure.

**Two of the three lines are controls and they are why the test means
anything.** A launch that only ever succeeds cannot tell a driver that
compiled and ran a module from a host answering something canned. So the
second line asks the same good module for an entry point that is not in it
(the driver must refuse: `named symbol not found`), and the third hands
over text that is not a module at all (the driver must refuse: `device
kernel image is invalid`). The second proves the host resolves a function;
the third proves an assembler actually read the bytes, which in turn proves
the text crossed the CCE boundary as ASCII rather than arriving as
plausible-looking code points.

**The positive line is chosen so nothing else could have produced it.** The
kernel computes ten times the input plus five over 1, 2, 3, 4, giving 15,
25, 35, 45 -- four distinct values, none an input, none a permutation of the
inputs, and an operation that appears nowhere in `com3_execute`'s scalar
table. **Sabotage run:** changing the multiplier in the PTX from 10 to 20
gives `launch: wrong 24992 44992 64960 84992` -- exactly twenty-times-plus-
five -- while both controls stay `refused`. So the answer is computed from
the module's own bytes on the device, and the failure is discriminating
rather than global.

### The two-implementation control for matmul

`codex/test/gpu-doorbell` lost its control when the serial transport was
deleted: running every case through both transports on identical operands
was what would catch an implementation that computed WRONGLY rather than
not at all, and one transport has nothing to compare against.

Matmul has that control back, and a better one, because the two paths share
no code: the scalar C loop in `com3_execute`, and a PTX kernel on the
graphics processor. `CODEX_VM_GPU_MATMUL=1` forces the device and `=0` forces
the scalar loop; UNSET auto-selects by size (below). The test's matmuls are
small, so unset takes the scalar loop there and the env var is what runs the
same case both ways. The test does not know which ran, and must not: **the
assertion is that the answers are identical.**

```powershell
$env:CODEX_VM_GPU_MATMUL = "0"   # or leave it unset
tools\codex-vm.exe -kernel build-output\gpu-doorbell.cdx -headless -output scalar.out -mem 3072
$env:CODEX_VM_GPU_MATMUL = "1"
tools\codex-vm.exe -kernel build-output\gpu-doorbell.cdx -headless -output device.out -mem 3072
Remove-Item Env:\CODEX_VM_GPU_MATMUL
```

Run 2026-07-22: both legs byte-identical to each other and to
`gpu-doorbell.expected`, with `CUDA: NVIDIA GeForce RTX 4060 Ti` present on
the device leg and **absent on the scalar leg**, which is what proves the
toggle actually moved and that the two runs were not the same run twice.
Use `Remove-Item Env:\NAME` to clear it: `$env:NAME = ""` leaves the
variable DEFINED, `getenv` returns a non-NULL pointer to an empty string,
and that is exactly how both legs of a measurement come to read as set.

**The device is auto-selected when the matmul is large enough to pay, and
the threshold is measured** (`CODEX_VM_COM3_STAT=1`, single dispatch per boot
so the device time includes its one-time ~85 ms context creation and PTX
JIT):

| N | scalar | device (incl. JIT) | |
|---|---:|---:|---|
| 256 | 23.0 ms | 95.1 ms | scalar wins |
| 384 | 80.0 ms | 72.9 ms | device wins |
| 512 | 198.1 ms | 88.5 ms | device wins 2.2x |

The scalar loop is O(N^3); the device cost is dominated by the fixed JIT. So
the device only wins once the scalar loop would cost more than ~85 ms, which
is about a 400x400x400. `cuda_matmul_wanted` picks the device above
`COM3_DEVICE_MATMUL_FLOPS` (64,000,000, ~400^3) and the scalar loop below,
so a small matmul -- every one the tests do, and every one a short program
does -- is never slowed, and a large one is accelerated automatically.
`COM3_MAX_ELEMS` (524288) caps a square at 512x512, past the break-even; a
caller needing bigger ships its own kernel through the PTX launch (op 32).
The command buffer was enlarged to 6 MB to hold the 2 MB operands of a
512x512, and the whole COM3 slab (`0xBD000000`..`0xBE000000`) is now
committed at boot -- the host writes the reply there directly, so it cannot
rely on the guest having demand-committed the page first.

## The Serving Peer (`build/cdx-serve-test.ps1`)

**Until this, no test drove a Codex TCP server.** IdeaServer, WebServer and
ExplorerServer are each driven by a demo script and by nothing else, which is how
`codex/os/net/WebServer.codex` came to sit in the tree with six undeclared-effect-row
errors (CDX2031/CDX2033) that no battery could see: it did not compile, and nothing
ever asked it to. A server only ever run by hand is a server nobody is checking.

**Those errors are gone, measured 2026-07-20 (val), and this paragraph said
otherwise in the present tense until then.** `WebServer.codex` cited by a real
program compiles with **zero** errors and emits a binary, and so do
`IdeaServer`, `ExplorerServer` and `Prism`. The history above is kept because it
is why this harness exists; the claim that the chapter is broken is not true now.

**Do not re-measure this by compiling the chapter on its own, which is what makes
the stale claim so easy to believe.** A library chapter has no `opening`, so under
whole-program dead-code elimination nothing in it is reachable, lambda-lifted
locals are pruned, and emit reports a `CDX2040: Unresolved call` for each one.
`WebServer` standalone shows **15** such errors naming ordinary let-bound locals
(`sess`, `new-arp`, `hash`), none of which is a defect. The control that settles
it costs one compile: a chapter nobody claims is broken, compiled the same way,
shows exactly **one** CDX2040 for the absent `opening` and no others, so anything
above one is the artifact and not the chapter. Compile a citing program instead.

This harness boots the real ingest tool and the real server and asks the real
question:

```powershell
pwsh build/cdx-serve-test.ps1                          # against seed/Codex.cdx
pwsh build/cdx-serve-test.ps1 -Kernel build/output/Sut.cdx   # against a fresh SUT
pwsh build/cdx-serve-test.ps1 -KeepArtifacts           # keep the decoded payload
```

`build/store-source.ps1` drives `tools/cdx-store` to store one work on a blank disk
and **prints the hash it stored**; `tools/cdx-serve` boots on that same disk and
listens on guest port 9300; the script connects through `-portfwd` and asks for that
hash over the wire. **The hash is never hardcoded** — it is read back out of
cdx-store's own output, so a change to the addressing rule moves both ends together
and the test keeps asking the right question instead of asserting a stale constant.

What it pins: a reply carries `tag-work-reply` (18); it answers the hash that was
asked; the payload is the stored work, carrying both its source and its path; and a
hash nobody stored comes back as an **empty payload, not an error**.

**Sensitivity, run rather than assumed:** the same server against a blank disk answers
109 frame bytes (an empty payload) where a seeded one answers 457. The test reads the
served work; it does not print a constant.

**THE WIRE IS CCE, AND IT DOES NOT TELL YOU.** `frame-encode-text` calls `char-code`,
which gives the CCE code point and not the ASCII one, so the hash's own hex digits go
out as CCE bytes. Sending ASCII does not error — the server simply never finds the
work and answers "not here", which reads exactly like a working server with an empty
store. Encode through `$script:UnicodeToCce` (`vm-config.ps1`).

**And the trap that cost the most: `-shl` on a `[byte]` in PowerShell keeps the left
operand's width.** `$b[1] -shl 8` shifts the bit off the end and yields 0, so every
length silently comes back as `n -band 0xFF`. That read a real 457-byte frame as 201
and a real 348-character payload as 92 — which looks *exactly* like a server
truncating its reply, and sent this test hunting a bug in `cdx-serve` that was never
there. Cast to `[int]` before shifting. The server was right; the ruler was short.

## The IoT Protocols Against Foreign Implementations

Two harnesses, both on-demand (they boot VMs and third-party servers), both
built to the rule `BrotliBeatsOpus.md` ends with: **the oracle runs in both
directions, and the reverse direction is written first.**

```powershell
pwsh build/coap-interop-test.ps1                              # our client, aiocoap's server
pwsh build/coap-serve-test.ps1                                # our server, aiocoap's client
pwsh build/mqtt-interop-test.ps1                              # mosquitto, QoS 1 and QoS 2
pwsh build/mqtts-interop-test.ps1                             # MQTT over TLS 1.3, mosquitto
pwsh build/mqtt-interop-test.ps1 -Kernel <cdx> -KeepArtifacts
```

`coap-serve-test.ps1` is the one that exercises `codex-vm -portfwd udp:...`
and therefore the guest as a datagram SERVER, which is the role an IoT
device actually plays.

**`mqtts-interop-test.ps1` is the first test in the tree where OUR TLS
CLIENT talks to a foreign SERVER**, and it found a defect on its first run
that had been invisible for the life of the stack: the ClientHello carried
no `signature_algorithms` extension. RFC 8446 section 9.2 makes it
mandatory and a conforming server MUST abort without it, so **every TLS and
DTLS handshake Codex ever initiated was one no conforming server would
accept.** Nothing could see it. Our own server does not inspect the
extension, so every loopback passed, and `tls-interop-test.ps1` drives our
SERVER with openssl's client -- the client half had never met a conforming
peer. mosquitto's OpenSSL says `missing sigalgs extension` and closes
without an alert, which from the client's seat is indistinguishable from a
server that never answered.

The lesson is the general one and it is worth stating in its own right:
**an interop harness validates the half it points at.** Two harnesses that
both drive our server prove nothing about our client, however foreign the
peer.

`codex/test/apps/coap-loopback` and `codex/test/apps/mqtt-loopback` are the
paired in-battery tests. They prove the endpoint agrees with our own codec
and they cannot prove more than that -- which is the whole reason these two
exist.

**Both found real bugs on their first run, and neither bug was reachable
from a loopback.**

- **CoAP.** `coap-text-to-bytes` converted a Uri-Path segment with
  `char-code`, which answers the CCE code point and not the ASCII one, so
  every path Codex had ever built named a resource no server could match.
  aiocoap answered 4.04 for a resource that was there. The existing
  `coap-encode` test could not have caught it: it asserts
  `list-length pkt > 0` and never looks at the bytes, so it is blind at
  exactly the thing that changed.
- **CoAP, again, from the control.** With the path fixed, the second
  exchange -- a deliberate request for a resource that does not exist --
  came back 2.05 with the FIRST resource's payload. The client built a
  fresh endpoint per request, so the message id restarted at 1, and RFC
  7252 section 4.5 requires a server to treat a repeated message id as a
  duplicate and replay its cached response. aiocoap was right. Nothing in
  a loopback caches, so nothing in a loopback could have shown it.
- **MQTT.** The chapter had no parser at all, so half the protocol was
  unreachable and the interop test is the only thing that could say so.
  `mqtt-build-publish` also had no packet-identifier parameter, which is
  malformed at QoS 1 -- latent only because every caller in the tree
  publishes at QoS 0, where the field is absent and the code was
  accidentally right.

**The MQTT harness is the one to copy.** It is genuinely bidirectional and
the two halves catch different things: `mosquitto_sub`, a separate process
using mosquitto's own client library, must receive what the guest published
(our encoder), and the broker then delivers that message back to the guest's
own subscription, which the guest must report byte-exact (our decoder). Only
the second half can catch a decoder that reads only what our encoder writes,
and that is precisely the half the compression stack never had.

## A Quotation From A Peer (`build/quote-from-peer-test.ps1`)

The same quotation is now proven through three transports, and the three tests
are deliberately the same work, the same digest, the same signer and the same
signature so that the transport is the only variable:

| Test | The work travels |
|---|---|
| `codex/test/quotes-gate` | beside the source, in the `%%QUOTED-WORKS%%` blob |
| `codex/test/quote-from-store` | on an attached disk, in the fact store |
| `codex/test/quote-from-peer` | over TCP, from another booted guest |

```powershell
pwsh build/quote-from-peer-test.ps1                            # against seed/Codex.cdx
pwsh build/quote-from-peer-test.ps1 -Kernel build/output/Sut.cdx
pwsh build/quote-from-peer-test.ps1 -KeepArtifacts             # keep the disks
```

`store-quoted-work` writes the work onto a blank disk; `tools/cdx-serve` boots on
that disk and listens; `codex/test/quote-from-peer` quotes the digest with **no
blob and no store attached** and is compiled with `compile.ps1 -Peer`. The host
asks, prepends the answer to the blob, and the compiler admits it through the
same four guards. `sort-ascending` exists only in the quoted work, so
`sorted=105` means the definition crossed a socket.

**Step 5 is the test.** Steps 1-4 pass if the work reaches the compiler by *any*
route, and the entire claim is that it reached it by this one — so the last step
compiles the same source against a peer holding nothing and requires that it
**fail**. Without it this is another function that always answers the same thing.

**Two traps, both of which cost a debugging round here:**

- **A running codex-vm's `-output` file is empty however healthy the guest is.**
  It flushes on exit. Reading a live server's output said "the server never
  booted" about a server that was serving perfectly; the control that settled it
  was booting the *known-good* disk the same way and getting the same silence.
  Ask the server a question instead — `cdx-checkout` runs the identical
  `repo-index-from-disk disk-load` line and answers in one command.
- **A peer takes ~20 s to boot and index, and the port forward accepts the host
  connection long before the guest is behind it.** So an early ask does not fail
  fast; it blocks on a socket nobody is reading and burns the whole read timeout,
  which leaves room for about two attempts in a minute. Wait for the peer to
  answer a hash nobody stored before asking it for one that matters.

## A Peer Nobody Named (`build/registry-locate-test.ps1`)

`quote-from-peer` proves a quotation resolves from a peer the compile was TOLD
about. This proves it resolves from one the compile was never told about: the
only address it is given is a registry's.

```powershell
pwsh build/registry-locate-test.ps1
pwsh build/registry-locate-test.ps1 -KeepArtifacts     # keep the disks and logs
pwsh build/registry-probe.ps1                          # ~90 s, the registry alone
```

A work is written to a disk; `tools/cdx-registry` boots knowing nobody;
`tools/cdx-announce` tells it an address holds those digests and exits;
`codex/test/quote-from-peer` is compiled with `-Registry` naming only the
registry. `sort-ascending` exists only in the quoted work, so `sorted=105` means
the definition crossed a socket to a peer whose address came from discovery.

**Steps 7 and 8 are the point.** Steps 1-6 pass if the work arrives by ANY
route. Step 7 compiles against a registry nobody ever announced to and requires
the compile to FAIL -- without it, the work could be arriving from somewhere
else and the harness measures nothing. Step 8 announces a peer that holds
nothing: the registry must still name it (an announcement is a rumour and is not
verified) and the compile must still fail, one step later, **at the fetch**.
That is what keeps "the registry names it" from being read as "the work is
trustworthy".

**`build/registry-probe.ps1` is the instrument to reach for first.** It boots
the registry alone, asks three times, and prints the GUEST's own trace, in about
ninety seconds against this harness's ten minutes. Run the full harness once to
produce the cdx, then iterate there.

**This harness misreported the product seven times before it worked**, and every
one was in the measuring apparatus rather than the server. The last was the
sharpest: `Get-QuotedHashes` returns an array, and PowerShell unrolls a
single-element array on `return`, so `(Get-QuotedHashes ...)[0]` indexed a
STRING and asked the registry to locate a digest called **"7"**. The registry was
announced the real digest and asked for a different one, so step 5 could not pass
however well the transport worked. Wrap every work-wire call that yields a list
in `@()`. A green here is only as good as what the harness asked for.

## Manifest-Scope Pin (`build/manifest-pin-test.ps1`)

The compiler writes each program's capability manifest into the CDX header
(`manifest-cap-bytes`, `X86_64Chapter.codex`): per capability, a little-endian
cap-id, a direction, a scope length, the scope bytes (CCE), and eight zero
bytes. For a long time that emission was checked only by eye, because **nothing
inside a guest can read its own manifest** -- the running program is the
extracted code, not the CDX file -- so a regression in the manifest (the old
"readwrite for every capability" direction bug, a dropped scope, a wrong
cap-id) was caught by no test.

This harness closes that. It compiles `codex/test/manifest-subject.codex` --
which declares a known two-capability scoped manifest
(`Network.Write "api.example.com"`, `FileSystem.Read "/config/"`) and nothing
else -- **fresh, with the compiler under test**, so the bytes checked are the
ones that compiler emits, not a frozen copy. It then attaches that CDX as the
disk of `codex/test/manifest-pin.codex`, which reads the header at offset 136
(manifest offset, le64) and 144 (size), walks the entries with
`block-read-sector`/`peek-byte`, and prints each cap-id, direction,
scope-length and scope. The output is compared to `manifest-pin.expected`.

```powershell
pwsh build/manifest-pin-test.ps1                             # against seed/Codex.cdx
pwsh build/manifest-pin-test.ps1 -Kernel build/output/Sut.cdx  # against a fresh SUT
```

Both `.codex` carry a `.skip` so the default battery does not run them: the
reader needs the subject CDX as its disk, which only this harness supplies. The
pin reports the manifest's SIZE and CONTENTS but never its OFFSET -- the offset
is `224 + code size` and moves with every codegen change, while the content is
what the pin is about. A sensitivity run against a variant subject (different
scopes) confirms the reader tracks the manifest rather than printing a constant.

## The IR-Passes Report Pin (`build/ir-passes-test.ps1`)

The `ir-check` and `occ-report` IR passes run in IR mode and used to report
nothing, because merging their diagnostic bags into `compile-frontend-passes`
killed the compiler in `bag-add` -- the giant let-chain's register allocation
crossed a spill cliff, on a source as small as `codex/test/arithmetic`
The merge was dropped to ship, so the passes ran and printed
nothing. The fix moved the merge into a helper (`frontend-bag-with-passes`, so
the caller's binding count is unchanged) and taught `emit-ir-uni` to print
notices the way the CDX emit path already does.

```powershell
pwsh build/ir-passes-test.ps1                              # against seed/Codex.cdx
pwsh build/ir-passes-test.ps1 -Kernel build/output/Sut.cdx  # against a fresh SUT
```

It compiles `arithmetic` in IR-UNI mode twice. **Positive:** with `-Passes
occ-report`, the compile must reach `IR-END` (the merge did not crash it) and
must emit at least one `OCC ` line. **Negative:** with no passes, zero `OCC `
lines -- so the positive is the pass reporting, not a constant the emitter
always prints. Both were run: the fix gives 30 OCC lines then 0; the pre-fix
seed gives 0 then 0 and fails the positive, which is what makes the pin
load-bearing rather than a comment. On-demand (boots VMs), not gated -- but
the crash half is covered by every build regardless, because `cross-smoke`
compiles through `compile-frontend-passes` in IR mode, so a regression of the
spill cliff fails the gate there.

## The LIR Dump Pin (`build/lir-dump-test.ps1`)

`codex/test/lir-check.lir-expected` records one line per definition: block
structure, live-in sets, register assignment, and the verdicts of both the
structural and the allocation verifier. **Until 2026-07-18 no harness read
it.** Grepping the tree for the filename found three doc mentions and the
test's own prose, and no runner -- so it was a snapshot of one past run going
stale in silence.

The correctness half was never the gap. `lir-check.expected` is a real
`.expected` the battery checks, and it pins what those functions *compute*.
What had no coverage is what the LIR *is*, and that is the half that hid the
`col-hue` parallel-move miscompile through ten sessions of green benches and
byte-identical fixed points.

```powershell
pwsh build/lir-dump-test.ps1                                # against seed/Codex.cdx
pwsh build/lir-dump-test.ps1 -Kernel build/output/Sut.cdx   # against a fresh SUT
pwsh build/lir-dump-test.ps1 -Accept                        # re-record the pin
```

The dump rides the info channel as **CDX4030** under `-Passes lir-dump`, so it
lands in the compile log and the comparison is a string compare. Measured
2026-07-18 against seed `00F5F34F`: **83 lines, all verifiers ok.**

**It fails two ways, and both were run rather than assumed.** A one-register
perturbation of the pin (`v1->r4` to `v1->r9`) fails with a two-line diff.
Omitting `-Passes lir-dump` yields zero dump lines and fails a **60-line
floor** -- which exists because an empty actual compared against an
accidentally-emptied pin is the classic function that always answers the same
thing. A pin nobody can fail is a comment.

**`-Accept` records whatever the compiler just did, including whatever it did
wrong** -- the same hazard as `test-gui.ps1 -Accept` and the same rule: it
prints the delta it is about to accept, and you read every line before you
take it. Register assignment legitimately moves on any allocator or selector
change, so this pin is expected to need re-recording; that is what makes
reading the diff the whole discipline rather than a formality.

### What gates, and what does not (decided 2026-07-18)

**The snapshot above is deliberately NOT in `build/build.ps1`.** It records
register assignment, which legitimately moves on any allocator or selector
change, so gating it would go red on correct work and train everyone to
`-Accept` reflexively. That converts a pin into a rubber stamp, which is worse
than no pin because it reads like coverage.

**The verdicts gate instead, and they gate somewhere better than this harness
could reach.** Both verifiers stand on the *emission path* (`lir-emit-try`,
`Emit/X86_64Lir.codex`), so they run for every selector-handled function in
every compile rather than for the 26 functions in one test file, and the
allocation check is handed **the assignment the emitter is about to use, not a
re-derived one**. A violation halts the build:

| Code | Verifier | Catches |
|------|----------|---------|
| CDX9006 | prologue parallel move | a parameter that does not end in its assigned register |
| CDX9007 | structural | a use reading a vreg no path defines, a malformed block, an undefined result |
| CDX9008 | allocation | a use whose assigned location holds a different value (clobbered by a call or division) |

**All three have been observed to fire.** That matters more than it sounds: an
unfired guard is worth exactly what no guard is worth, which is what
`accum-at-capacity` proved by sitting uncalled while this document asserted it
ran. CL 8867 proved only the *absence of false positives* (battery identical to
baseline, fuzz clean). The fire tests were run separately, 2026-07-18, each by
building a deliberately broken SUT and compiling with it:

- **CDX9008** -- `lir-scan-cross` changed to place a call-crossing value in a
  caller-saved register. Compiling `bench/codex/fib.codex` halts with
  `alloc vreg 0 not in r4 at block 5 insn 2`, no binary emitted. This is a
  genuine allocator defect, and it is the same class as the `col-hue`
  miscompile.
- **CDX9007** -- entry parameters under-marked by one. Compiling
  `bench/codex/sum.codex` halts with `use before def in block 1 insn 0`, no
  binary emitted.
- **CDX9006** -- proven at the time it was built by corrupting `lir-pmv-pick`.

**One corruption that did NOT fire, recorded because the negative is
informative:** making `lcoal-param-safe` answer True unconditionally (unsafe
coalescing, the historical bug class) produced *wrong values*, not invalid
structure, and CDX9007 correctly stayed silent. Wrong answers are the
`.expected` battery's job, and `codex/test/lir-selector-smoke.codex` in the BVT
is what holds that ground: it pins five miscompile shapes by their answers,
including the `col-hue` parallel-move clash. **Structure, allocation, and
answers are three different instruments, and reaching for the wrong one reads
as a green that means nothing.**

## Expected-Failure Tests

132 tests in `codex/test/errors/` verify that the compiler rejects
invalid programs with the correct diagnostic codes. Each has a
`.failing` sidecar listing the expected CDX error codes. Examples:
`apply-non-function` (CDX2001), `duplicate-def` (CDX3002),
`infinite-type` (CDX2010), `linear-twice` (CDX2061).

## Board Tests (`build/boards-test.ps1`)

The nine IoT board drivers cannot live in the default battery: three of them
need `codex-vm -board-mmio` to back the register windows above the RAM
ceiling, and the generic harness passes no such flag. They run from
`build/boards-test.ps1` instead, all together, with the flag on.

```powershell
build/boards-test.ps1                  # the board driver batteries
build/boards-test.ps1 -Only rp2040     # one board
build/boards-test.ps1 -Renode          # plus the arm64/riscv64 boot smoke
```

Measured 2026-07-13 (seed 90137D4D):

| Boards | Pass | Fail | Sub-tests |
|---|---:|---:|---:|
| 9 | **9** | **0** | **108** |

Each test's `opening` returns the number of sub-tests that passed and the
`.expected` sidecar holds that number, so the harness re-measures the total
rather than quoting it.

A caution earned the hard way: QEMU virt's test is
`codex/test/qemu-virt-board.codex`, not `qemuvirt-drivers.codex`. The first
cut of this harness assumed the `<board>-drivers` convention, did not find
it, reported the board as having no smoke test, and that false gap reached a
recorded gap before anyone opened the directory. **Do not infer a coverage
gap from a filename.**

Since 2026-07-13 every board driver function that touches a register declares
`[Device.Mmio]`, and the `Boards` quire is no longer effect-exempt — so these
tests also pin the effect rows. `codex/test/errors/board-mmio-pure` is the
adversarial half: a pure signature cannot launder a driver call.

What this proves: a register access lands on memory and reads back what it
wrote, so the address arithmetic, the access width, and the read-modify-write
logic are exercised. What it does not prove: peripheral behaviour. No silicon
has been in the loop. Renode is where that lives.

## GUI Tests (`build/test-gui.ps1`)

Every GOP application used to be untestable. The skip inventory says so in
a dozen places -- "requires display", "requires keyboard", "blocks waiting
for keyboard input" -- so the widget stack, the input path, and every app
built on them had no regression coverage at all. The circuits mouse bug
(a held button was legible for one poll, so no drag could ever be seen)
lived through months of green batteries for exactly that reason.

`build/test-gui.ps1` drives an app with scripted input and compares the
resulting frame against a recorded image. It runs **headless**: codex-vm
injects the pointer and keyboard directly into the guest through the same
fields the window proc writes (press latch included), so the guest cannot
tell a scripted hand from a real one. No host cursor moves and no window
takes focus -- a UI test does not fight the operator, or another agent, for
the physical mouse, and several can run at once.

```powershell
build/test-gui.ps1 -Kernel build/output/circuits.cdx -Script codex/test/gui/circuits-drag.uiscript
build/test-gui.ps1 ... -Accept        # record the current frame as expected
build/test-gui.ps1 ... -KeepArtifacts # keep the actual frame + timelines
```

### The .uiscript

One command per line, `#` comments. Coordinates are client pixels.

| Command | Meaning |
|---------|---------|
| `boot <ms>` | when the app is up and the script may start (default 8000) |
| `move <x> <y>` | move the pointer |
| `press <x> <y> [btn]` | button down (1 left, 2 right, 4 middle) |
| `release <x> <y>` | button up |
| `click <x> <y> [btn]` | hover, down, up |
| `drag <x1> <y1> <x2> <y2> [steps]` | press, move in steps with the button held, release |
| `key <scancode>` | Set-1 make code |
| `wait <ms>` | advance the clock |
| `settle <ms>` | quiet time after the last event before the frame is taken |
| `vmargs <flag> ...` | extra codex-vm flags for this test (repeatable) |
| `mask <x> <y> <w> <h>` | exclude a rect from the comparison (repeatable) |
| `expect-ink <x> <y> <w> <h> <min> <max>` | lit-pixel count in a rect must fall in range (repeatable) |

**`vmargs` exists because a frame is only reproducible on a machine that has
been told to hold still.** The standing case is the clock: an app that paints
the time cannot be compared against a recorded image while codex-vm answers the
RTC from the host, and for a long time the conclusion drawn from that was that
GuiOS could have no golden. That was a statement about the machine, not the
app. `vmargs -rtc 2026-07-21T09:41:07` fixes the clock and the frame becomes a
function of the program and the flags. Keeping it in the `.uiscript` rather
than in a harness switch means the test states its own machine, exactly as a
`.vmargs` sidecar does for the serial battery.

**Freezing the clock is not the same as making everything deterministic, and
the difference is where the honesty lives.** `apps/guios/tests/desktop-chrome`
deliberately captures the F3 hardware view rather than the prettier F1
dashboard, because the dashboard prints `Uptime: <ticks/18>s`, which at an
11-second deadline divides within one tick of a boundary and flips between 10
and 11 under host load. `-rtc` would not fix that and a tolerance would only
hide it. Uptime is legitimately not a function of the program, so it is kept
out of the frame rather than lied about. Choose the view before reaching for a
tolerance.

**When the volatile widget is in the middle of a view you want, `mask` is the
third option and it beats the other two.** `apps/guios/tests/dashboard` covers
the F1 view -- worth having because the memory gauge is one of the widgets 4.5
was about -- and masks the one 76x60 box holding the uptime figure, the heap
figure and the gauge fill edge. Three rules keep a declared hole safer than a
hidden one:

- **Derive the rect by measuring.** Capture the same frame twice with the
  volatile input different (two screenshot deadlines), diff, take the bounding
  box. `dashboard`'s box came out at x=203..268, y=87..139 over 278 differing
  pixels; guessing from a picture produces a mask that is both too big and, in
  the direction that matters, too small.
- **Run both controls.** Positive: the frame at a different deadline must
  compare CLEAN against the golden, which is what proves the mask covers the
  volatile region. Negative: the same run with the mask removed must FAIL,
  which is what proves the mask is load-bearing. A mask that has only ever been
  seen passing is indistinguishable from one covering the whole frame.
- **Keep the hole visible.** The harness prints the rect count, the masked
  pixel count and the percentage on every run, and warns above ten per cent.
  A golden whose interesting half was silently never compared is the failure
  mode this mechanism exists to avoid, not one it is allowed to cause.

**Do not get there by making the widget invisible instead** -- setting a
volatile field's colour to the background, say. It reaches the same pixel
stability by changing the product to suit the test, and it leaves a hole that
no one reviewing the `.uiscript` can see, which is strictly worse than one
written down beside its reason.

### `expect-ink`, and why it is not `expect-text`

A masked rect is excluded from the comparison, so on its own it is **wholly
unchecked**: a field that went blank, doubled in length or filled with garbage
passed the golden silently. `expect-ink` counts the lit pixels in a rect and
requires the count to fall in a declared range. It does not read the value. It
is a heuristic, it is labelled as one, and it closes the blank-field hole for
the price of six numbers.

Derive the range by measuring across the values the field actually takes.
`apps/guios/tests/dashboard` reads 509 lit pixels at an 11000 ms deadline, 572
at 20000 and 616 at 30000 (uptime gains digits, the heap gauge fills), so its
range is 350..900: wide enough for the retry span, narrow enough that a blank
rect is out of it. The harness prints the observed count every run, so the
range is widened from evidence. Negative control, run: the same frame with the
range set to 5000..6000 fails.

**Reading the value would be better and is currently impossible for GuiOS.**
The work was built and then refused by its own verification, which is the
useful part of the story. A decoder needs a glyph atlas; the atlas harvest
decodes every sample back and requires it to equal the source string exactly;
that round trip failed on all nine samples, and dumping the pixels showed why:
**booted without a disk, GuiOS falls back to a block font that renders `s`, `t`
and `b` as one identical bitmap, and `d`/`o`, `O`/`0` and `5`/`9` likewise.**
`W` is a 3x6 solid stem. The value is not in the pixels to be read, so no
decoder can recover it.

Three cheaper designs were measured and rejected on the way, recorded so nobody
re-derives them: fixed-cell slicing (the fallback font is proportional -- pitch
measures 6.95, 7.07, 7.31 and 7.61 across four known strings), blank-column
segmentation as a decoder (adjacent glyphs touch; four of nine known lines
yield fewer runs than they have characters), and cropping a glyph to its lit
rows (which makes `d` and `o` identical -- caught by the round trip, invisible
to a run-count check).

The UI library's GPU text path is a different and tractable case: `gr-emit-text`
advances `x + i * 6`, a true fixed cell, and apps rendering through it are
legible. A real `expect-text` is worth building against that font, or against
GuiOS once its font fallback is fixed -- which is a defect on its own merits,
since a diskless GuiOS currently draws unreadable text to a real screen.

The harness compiles this into two timelines (`t:x,y,btn` for the pointer,
`t:scancode` for the keyboard) and hands them to codex-vm as `-mouse-file`
and `-keys-file`. The frame is captured with `-screenshot` at a fixed
wall-clock offset, so a test is only as stable as the app's own determinism.

### Sidecars

| File | Meaning |
|------|---------|
| `foo.uiscript` | the input script |
| `foo.expected.bmp` | the frame the app must produce (p4 type `binary`) |

### Adding a GUI test

1. Write `codex/test/gui/foo.uiscript`.
2. Record the frame: `build/test-gui.ps1 -Kernel <app>.cdx -Script ... -Accept`
3. **Look at the recorded image.** `-Accept` records whatever the app did,
   including whatever it did wrong. An expected image nobody looked at is a
   bug fixture, not a test.
4. Run it twice without `-Accept` to confirm it is deterministic (a clean
   test reports `0 pixels differ`).
5. `p4 add` the script and the `.expected.bmp` **as type `binary`**.

Comparison is exact by default; `-Tolerance N` allows N differing pixels for
an app with unavoidable jitter. A failing run leaves the actual frame in
`test-output/gui/` for inspection -- look at it before you re-record.

### Per-app batteries

Each application owns its GUI battery, in `apps/<app>/tests/`:

```powershell
build/test-app-gui.ps1 -App circuits             # run the battery
build/test-app-gui.ps1 -App circuits -Build      # compile the app first
build/test-app-gui.ps1 -App circuits -Only menu  # just the menu tests
build/test-app-gui.ps1 -App circuits -Accept     # (re)record every frame
```

App-BVT level: does it paint its chrome, do the menus open and have items in
them, does the status bar have content, does a click land, does a drag move
the thing it grabbed.

| App | Tests | Covers |
|-----|------:|--------|
| circuits | 15 (**14 pass / 1 fail**) | boot chrome; the four dropdowns and a menu item firing; the PCB / 3D / SIM tabs; hover properties; palette place; wire; marquee; drag; delete+undo |
| spark | 8 | boot chrome (toolbar, viewport, outliner, camera panel, status bar); add cube/sphere/plane; wireframe; grid; orbit; zoom; a multi-command scene |
| guios | 2 | `desktop-chrome`: the F3 view -- sidebar at its declared width, taskbar as a bottom strip, every panel delineated, clock frozen by `vmargs -rtc`. 5/5 clean at 0 pixels, no retries; negative control (drop the `-rtc` line) differs by 285. `dashboard`: the F1 view, adding the memory gauge's track and border, with a measured 76x60 `mask` over the uptime figure, the heap figure and the gauge fill edge (0.58% of the frame). Both controls run: at a 20000 ms deadline it compares clean against a golden recorded at 11000 ms, and with the mask removed it differs by 278. The masked rect also carries an `expect-ink 350..900` so it is not wholly unchecked; it reads 509 in the recorded run and fails when the range is set impossible. Run with `-Kernel apps/guios/build-output/guios.cdx` -- guios' `build.ps1` takes no `-Out`, so `-Build` does not work for it, and the default kernel path collides with the one `apps/cvmm/build-gui.ps1` writes |

**`circuits/tab-sim` is red, and this table used to imply it was not.**
Measured 2026-07-13: the SIM tab renders an empty frame (241001 pixels
differ from the recorded image, which has the transient-analysis
waveforms, the menubar and the status bar). It fails on the depot seed
with depot sources, so it is not fallout from any recent change. Open,
and tracked nowhere but here.

The battery is also **flaky by one test under parallel load**:
`menu-select-all` and `menu-file` each failed once across four full runs
and each passes 4/4 standalone. Until that is fixed, a single red test in
a batch run means "look again", not "regression" — and the harness cannot
be a gate.

Spark (640x480, run with `-Width 640 -Height 480`) did not compile at all when
its battery was written -- see the Real-negation compiler bug below -- and once
it did, it was still invisible: it writes pixels STRAIGHT to the GOP framebuffer
instead of driving the GPU rasterizer, and codex-vm only synced its shadow
buffer for guests that announce a frame. Black window, empty screenshot,
frames=0, no error anywhere.

The first sweep of the circuits battery found three real bugs, none of which
the existing battery could see, because no GOP app was tested at all:

- `process-all` rebuilt the state with `as-tab = st.as-tab` (the OLD state),
  discarding `set-tab` every frame -- a tab click could never do anything.
- `draw-sim-legend` fed `gpu-rect`'s return value back as the next triangle
  index, writing the legend over the menubar and collapsing the frame count.
- codex-vm silently dropped every triangle past 16384. A frame is drawn back
  to front, so what vanished was whatever was drawn LAST: the dropdown menus,
  the properties panel, and the entire status bar. That one was in the VM and
  affected every GPU app.

## Test Lifecycle

### Adding a Test

1. Write `codex/test/foo.codex` (or `codex/test/apps/foo.codex`
   for integration tests).
2. Compile: `build/compile.ps1 -Src codex/test/foo.codex -Out foo.cdx`
3. Run: `tools/codex-vm.exe -kernel foo.cdx -headless -output foo.out`
4. Verify the output, then copy to `foo.expected`.
5. For disk tests, create `foo.disk` (blank or pre-populated).
6. `p4 add` all files and submit.

### Updating Expected Output

When a seed rebuild or codegen change shifts output (e.g., different
hash values, changed serialization lengths), update the `.expected`
file from the actual runtime output. The test harness strips `\r`
from expected files before comparing.

### Promoting a Slow Test

If a `.slow` test now compiles in under 5 seconds, delete the
`.slow` file. Fester marked 41 foreword tests "redundant" as `.slow`
in CL 2949; reek promoted them all to regular in CL 2984 — they
run in 1-3 seconds each.

## Batch Compile Architecture

The test harness compiles tests in REPL batch mode: one VM per job
slot, compiling multiple tests sequentially over a persistent serial
connection. The compiler resets its heap between compilations
automatically (REPL loop in X86_64Chapter.codex).

This is fast for small tests (~2s each) but breaks down for tests
with large dependency chains (foreword + OS + trust modules). The
REPL VM accumulates foreword bytes across the session, and late-batch
tests may fail to compile simply because the batch budget is
exhausted. These tests compile fine with individual VMs.

Workaround: run `build/test.ps1 -Apps -Jobs 8` for more batch slots,
or compile stubborn tests individually via `build/compile.ps1`.

