# Examiner's Assay

Test infrastructure, coverage, and known results for the Codex
compiler, OS, and library stack. For build process and gate
mechanics, see `OperatorsManual.md`. This document records what
is tested, what passes, and what remains.

## Test Battery Layout

```
codex/test/              Root-level tests (default battery)
codex/test/apps/         Application and integration tests (-Apps or -All)
codex/test/errors/       Expected-failure tests (bad programs that must reject)
codex/test/forewords/    Per-chapter foreword compile tests (-FW or -All)
codex/test/fuzz/         Fuzz and stress tests (-Fuzz or -All)
```

The default battery runs `codex/test/*.codex` + `codex/test/errors/*.codex`.
Use `-Apps` for the full application suite, `-All` for everything.

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
| `foo.stdin` | Pumped to VM serial after boot (runtime input) |
| `foo.disk` | Attached as IDE disk image via codex-vm `-disk` flag |

A test with no sidecar compiles but is unverified (PASS_UNVERIFIED).

## Current State (2026-07-08, fester, seed 9DF129A5...)

Default battery baseline is now **336 total / 321 pass / 0 fail /
15 skip** on the seed carrying the UEFI boot arc, blu's capability
enforcement, and the spawn-pool carve. New this cycle: the boot-arc
regression battery (`act-tco-loop`, `gop-text-field`, `ide-pio-read`,
`ahci-encode`, `gop-fat16`, `gop-wake`, `disk-write`, `fat-write` --
each a known-answer fixture built independently from the FAT/GPT/AHCI
specs) and blu's `nested-spawn` probe. Earlier this cycle: New this day: `cap-block-denied` and
`cap-identity-denied` — runtime capability-denial probes (block and
identity-key syscalls consult the per-process cap word; a stripped
grant gets -1 with the device/key untouched) — `errors/cap-launder-pure-key`
(a pure signature cannot mention the key intrinsics; CDX2031+2033),
and `errors/opening-value-effect-undeclared` (a value-binding opening
with an effectful body must declare it; CDX2031).

Capability stage 7 fixed a stage-4 regression: nine `-Apps` identity
tests (identity-basic/inherit/cap-gate/no-cap/set-proc-test/
get-proc-test/setproc-no-admin/keygen/mutual-auth) had gone
pass->fail because the gated kernel builtins carried empty effect
rows and could not earn their manifest grant. They now declare their
real effects (Concurrent/Identity/Capability) and all pass on the
current seed. -Apps identity/disk/caps sweep (17 tests) verified
green. RESOLVED (was KNOWN): a two-level process spawn (a spawned
process that spawns a grandchild) used to hang -- a spawn-allocator
heap-budget bug (child heap+stack carved from the spawner's own
allocation frontier, overlapping a grandchild's stack), not capability
typing. Fixed by the spawn-pool carve (main CL 7354): all three spawn
helpers cut child regions from a global pool cursor. Probe:
`codex/test/nested-spawn.codex`. The eight `-Apps`
disk/caps tests (block-identify, block-io-basic, disk-facts-*,
process-caps-test) were run individually against the new seed and
pass; `boot-stage-test` is a pre-existing CDX2033 compile failure
(verified identical on the prior seed — the never-swept `-Apps`
surface, which also carries 33 pre-existing CDX2051 compile
failures from QuartermastersMap dig 1).

Earlier baseline (2026-07-07, blu 7198): **319/304/0/15** on the
demand-paged, survey-free seed: the compiler demand-pages its heap
([6 MB, 2 GB) commits on touch), phase decks are fixed generous
floors, and every compiled test binary demand-boots. Validated the
same day: all 52 plugs build, ARM64 + RISC-V board tests pass,
+86 KB source-growth pingpong stays byte-identical, and
`ttt-perfect` (first battery coverage of the games quire) joined the
battery.

## Previous State (2026-07-04, CL 6993)

99 individual tests consolidated into 11 smoke bundles (unit-smoke,
rt-smoke, try-smoke, prose-smoke, linear-smoke, linear-errors,
mutable-smoke, typeclass-smoke, handler-smoke, record-smoke,
lang-smoke, bs3-smoke, punctual-smoke). Each smoke test exercises
multiple features in a single VM boot, cutting battery time ~60%.

BVT mode (`build/build.ps1` default): runs a 10-test subset for
fast iteration (~18s). Full battery: `build/test.ps1 -Jobs 4`.

The default battery grew from ~192 to ~294 over the vision-check
campaign: each closed by-construction hole shipped its adversarial
probe as a `codex/test/errors/*.failing` negative test (linear
laundering, effect laundering, capability laundering, bounded-signature
out-of-range), and the IoT protocol build-out added ~25
`*-encode.codex` known-answer tests.

Cross-architecture testing: ARM64 135/135 (100%). RISC-V is at ~132 on
the committed Renode board after a plug-repair campaign; parity work is
ongoing (the earlier "135/135" was measured against an uncommitted
large-memory emulator override, not the committed board). See the
Cross-Architecture Battery section below.

### Default Battery (`build/test.ps1`)

| Category | Count |
|----------|-------|
| PASS_EXPECTED | ~294 |
| PASS_FAILING | 0 |
| SKIPPED | 15 |
| FAIL | 0 |
| **Total** | **~294 pass / 0 fail / 15 skip** |

### Full Battery (`build/test.ps1 -Apps`)

| Category | Count |
|----------|-------|
| PASS_EXPECTED | ~405 |
| PASS_FAILING | 0 |
| SKIPPED | ~25 |
| FAIL | 0 |
| **Total** | **~430** |

The batch REPL compiler times out on ~118 large-dependency-chain
tests in `-Apps` mode. These tests compile and pass when given
individual VMs. The timeout is a test harness scalability issue,
not a code defect.

### Foreword Battery (`build/test.ps1 -FW`)

Per-chapter compile tests for all foreword modules. Not included
in the default or `-Apps` battery. Some foreword tests have large
dependency chains (~127s compile) and are marked `.slow`.

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
build/test-cross-batch.ps1 -Arch arm64 -Jobs 4 -RenoTimeout 10 # full battery (Renode)
build/test-cross-batch.ps1 -Arch arm64 -Jobs 4 -UseQemu        # full battery (QEMU)
```

### ARM64 (2026-06-27, CL 6173)

| Category | Count |
|----------|-------|
| PASS_EXPECTED | 135 |
| PASS_UNVERIFIED | 2 |
| SKIPPED | 17 |
| FAIL | 0 |
| **Total** | **154** |

135/135 verified tests pass (100% parity with x86-64 battery).
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

### Legitimate Skips (cannot run headlessly)

| Test | Reason |
|------|--------|
| vmx-init-test, vmx-launch-test, vmx-serial-test | VMX requires CPL 0 + VT-x; no nested VMX under WHPX |
| vga-terminal-demo | Requires display + keyboard |
| vga-shell-test | Blocks waiting for keyboard input |
| diagnostic-boot, firstboot-lite | Blocks waiting for keyboard/RDRAND |
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
| ~~codex-shell, mini-shell, boot-init~~ | RESOLVED 2026-07-07 (was misdiagnosed as a "REPL-batch miscompile"): the batch driver sent `CDX repl` per test, embedding the REPL loop in every test binary. Output-only tests were unaffected (the loop exits on stdin EOF) but stdin-consuming tests hung with zero output. The compiler was deterministic and innocent — batch output was byte-identical to an individual `-Repl` compile. Fixed by sending plain `CDX` in `test-compile-batch.ps1` (the session loops because the seed is repl-built; no per-request flag needed). |
| db-test | Compiles clean but heap-scan overflows 2GB RAM at runtime |
| db-full-test | CDX1000 parse errors in ~9304-line concat (token mismatch in Server.codex) |
| historian-test | Historian.codex has a parse error (CDX1000 on `is` keyword) |
| foreword-all-compile | CDX3001 duplicate type `Event` when all forewords compiled together |

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

`block-identify` needs no disk (returns 0 sectors when no disk is
attached). `block-io-basic` uses a 512-byte image with value 42 in
the first qword.

## Expected-Failure Tests

40 tests in `codex/test/errors/` verify that the compiler rejects
invalid programs with the correct diagnostic codes. Each has a
`.failing` sidecar listing the expected CDX error codes. Examples:
`apply-non-function` (CDX2001), `duplicate-def` (CDX3002),
`infinite-type` (CDX2010), `linear-twice` (CDX2061).

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

## History

| Date | CL | Change |
|------|-----|--------|
| 2026-06-02 | 2986 | 405 PASS_EXPECTED with `-Apps`. Fixed 3 stale expected files. |
| 2026-06-02 | 2985 | Un-skipped 9 disk I/O tests via `.disk` sidecar images for codex-vm. |
| 2026-06-02 | 2984 | Promoted 41 redundant-slow tests to regular. 201/0/10 default battery. |
| 2026-06-02 | 2983 | Filled 19 stub tests (annotations, repo, trust, json, linear, mutable, variants). |
| 2026-06-01 | 2961 | Added lambda-body-def regression test. |
| 2026-05-21 | 1927 | Poison build: 105/105 pass (no uninitialized field dependencies). |
| 2026-05-20 | 1885 | Short-circuit `&`/`|` eliminates guard-bug class. |
