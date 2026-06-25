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
| `foo.skip` | Skipped entirely; first line is the reason |
| `foo.slow` | Skipped unless `-Slow`; first line is the reason |
| `foo.fatal` | Skipped unless `-Fatal`; kills the VM at runtime |
| `foo.stdin` | Pumped to VM serial after boot (runtime input) |
| `foo.disk` | Attached as IDE disk image via codex-vm `-disk` flag |

A test with no sidecar compiles but is unverified (PASS_UNVERIFIED).

## Current State (2026-06-13, CL 4082)

99 individual tests consolidated into 11 smoke bundles (unit-smoke,
rt-smoke, try-smoke, prose-smoke, linear-smoke, linear-errors,
mutable-smoke, typeclass-smoke, handler-smoke, record-smoke,
lang-smoke, bs3-smoke, punctual-smoke). Each smoke test exercises
multiple features in a single VM boot, cutting battery time ~60%.

BVT mode (`build/build.ps1` default): runs a 10-test subset for
fast iteration (~18s). Full battery: `build/test.ps1 -Jobs 4`.

### Default Battery (`build/test.ps1`)

| Category | Count |
|----------|-------|
| PASS_EXPECTED | ~182 |
| PASS_FAILING | 0 |
| SKIPPED | 10 |
| FAIL | 0 |
| **Total** | **~192** |

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
