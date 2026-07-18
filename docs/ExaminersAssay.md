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
| `foo.stdin` | Pumped to VM serial after boot (runtime input) |
| `foo.keys` | Scancode timeline (`t:scancode` per line, t = ms since boot, `#` comments) handed to codex-vm as `-keys-file`. **Not interchangeable with `.stdin`** — see below |
| `foo.disk` | Attached as IDE disk image via codex-vm `-disk` flag |
| `foo.smp` | Core count. The test is booted with `-smp N`. This is how a test covers multi-core; without it every test boots single-core, which is why nothing exercised SMP for so long (`codex/test/smp-cores.codex` is the first) |

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

## Current State (2026-07-13, seed 5A6B432B...)

Both batteries were re-run from scratch on 2026-07-13. **These are
measured numbers, and they are already going stale — re-measure before
you quote them. Never carry a count forward.**

| Battery | Command | Total | Pass | Fail | Skip |
|---|---|---:|---:|---:|---:|
| Default | `build/test.ps1 -Jobs 4` | **342** | **328** | **0** | **14** |
| Full | `build/test.ps1 -Apps -Jobs 4` | **598** | **498** | **9** | **91** |

The default battery is green. The `-Apps` failures are catalogued
per-test defects — each a real defect, not harness noise — tracked in
`docs/PM/BACKLOG.md` §7.5:

| Test | Failure |
|---|---|
| `boot-stage-test` | compile — effect-row rot (CDX2031/2033) |
| `erp-server-test` | compile — CDX3001 |
| `quaternion-test` | compile — CDX2001 |
| `spark-shapes-test` | compile — CDX2085 |
| `historian-test-full` | expected error but compiled |
| `spark-mesh-test` | output mismatch |
| `wave3-test` | output mismatch |
| `xhci-discover-test` | runtime — dies in the slot/type walk (BACKLOG 4.10) |

`nic-ping` was the ninth and was **fixed 2026-07-13** (val), so the
measured `9` above is now `8` — but **the battery has not been re-run to
confirm it, so the table keeps the number it actually measured.** Re-run
before trusting either figure.

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

The reason is `docs/PM/Done/Stories/Opus.md`'s, and it is about the search
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
build/test-cross-batch.ps1 -Arch arm64 -Jobs 4 -RenoTimeout 10 # full battery (Renode)
build/test-cross-batch.ps1 -Arch arm64 -Jobs 4 -UseQemu        # full battery (QEMU)
build/test-cross-smp.ps1 -Arch riscv64 -Test smp-riscv-boot    # one multi-core test (QEMU -smp N)
```

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
guest code (BACKLOG 4.2, closed), each reading a cell only a non-zero core
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
> `build/test-cross-batch.ps1 -Arch arm64 -Jobs 4`. Tracked in
> `docs/PM/BACKLOG.md` §3.6.

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
what this document is supposed to measure, and it is tracked in
`docs/PM/BACKLOG.md` §7.1–7.2.

### Legitimate Skips (cannot run headlessly)

| Test | Reason |
|------|--------|
| vmx-init-test, vmx-launch-test, vmx-serial-test | VMX requires CPL 0 + VT-x; no nested VMX under WHPX |
| vga-terminal-demo | Requires display + keyboard. Also 13 CDX2051 errors of its own (BACKLOG 4.14) |
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
| db-full-test | Compiles and runs; still skipped. Remaining blockers: a when-on-inline-generic-field **miscompile**, plus bulk-import defects (`BulkLoader.bulk-insert` clobbers `bi-ok = True` over an inner failure) and `.expected` adjudication. BACKLOG 7.3. |
| ~~db-mini-test~~ `Page.codex` | CDX2000: `emit-field-access` cannot resolve the type of a chained field access (`pg.header.slot-count`) — 21 errors in that chapter alone. **There is no `db-mini-test`**: no such `.codex` has ever existed in main, and this row described a test by the name of the orphaned `.skip` that was the defect's only record. The defect is real and now lives in `docs/PM/BACKLOG.md` §7; the sidecar is gone. Do not go looking for the test — write one. |
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

## The Serving Peer (`build/cdx-serve-test.ps1`)

**Until this, no test drove a Codex TCP server.** IdeaServer, WebServer and
ExplorerServer are each driven by a demo script and by nothing else, which is how
`codex/os/net/WebServer.codex` came to sit in the tree with six undeclared-effect-row
errors (CDX2031/CDX2033) that no battery could see: it does not compile, and nothing
ever asked it to. A server only ever run by hand is a server nobody is checking.

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
backlog entry before anyone opened the directory. **Do not infer a coverage
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

**`circuits/tab-sim` is red, and this table used to imply it was not.**
Measured 2026-07-13: the SIM tab renders an empty frame (241001 pixels
differ from the recorded image, which has the transient-analysis
waveforms, the menubar and the status bar). It fails on the depot seed
with depot sources, so it is not fallout from any recent change. Tracked
as `docs/PM/BACKLOG.md` §7.9.

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

