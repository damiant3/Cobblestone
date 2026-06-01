# Operator's Manual

How to build, test, install, and debug the Codex compiler. For the
runtime memory model, register conventions, and allocator internals,
see `docs/ArchitectsSketchbook.md`.

## Build Process

The build script (`build/build.ps1`) runs the full verification
pipeline. Each phase must pass before the next begins.

### Phases

1. **Clean**: Remove `build-output/` and temporary files.

2. **Source concat**: `build/concat-codex-self.ps1` scans `codex/compiler/`
   for `.codex` files, resolves foreword dependencies transitively, and
   emits a single concatenated source file. Foreword chapters are
   prefixed with `Foreword--`. Compiler chapters in subdirectories are
   prefixed with the directory name (e.g., `Emit--`).

3. **CDX build**: Boot the seed (`seed/Codex.cdx`) in the VM
   (codex-vm by default), feed it the concatenated source over serial,
   receive the compiled CDX binary. This produces the SUT (System Under
   Test).

4. **Sign**: Compile and run an inline Ed25519 signing program to
   embed a signature in the CDX header (bytes 40-135).

5. **Canary**: Compile `codex.test/hello.codex` with the SUT and verify
   runtime output matches `hello.expected`. This confirms the SUT can
   compile and run a simple program.

6. **Semantic equivalence**: The SUT emits the source in TEXT mode
   (stage1.codex). `build/compare-codex-semantic.ps1` parses both
   source and stage1, normalizes whitespace/parens/operator aliases,
   and compares every definition body. Mismatches indicate the emitter
   lost information.

7. **Text fixed point**: The SUT emits stage1.codex, then emits
   stage1.codex again to produce stage2.codex. SHA-256 of stage1 must
   equal SHA-256 of stage2. This proves the text emitter is idempotent.

8. **CDX fixed point**: The SUT compiles source → stage1.cdx. Then
   stage1.cdx compiles source → stage2.cdx. SHA-256 of stage1.cdx must
   equal SHA-256 of stage2.cdx. This proves the compiler is a fixed
   point of itself — the binary it produces is identical to itself.

9. **Test battery**: `build/test.ps1` runs all samples in `codex/test/`.
   Each sample has a sidecar (`.expected` for success, `.failing` for
   expected errors, `.skip` for skipped). Runs parallel VM instances.

### Quick Commands

```powershell
build/test.ps1                        # Sample battery (~2-5s per sample)
build/test.ps1 -Jobs 4                # Parallel test (4 batch slots)
build/test.ps1 -All                   # Include foreword + app tests
build/test.ps1 -Fatal                 # Include fatal (GPF/exception) tests
build/build.ps1                       # Full pipeline (all gates)
```

## Test Harness

### Two-Phase Architecture

**Phase 1 — Batch compile.** One VM per job slot, REPL loop reuse.
Each slot boots `seed/Codex.cdx` once, then compiles multiple test
sources sequentially over the persistent serial connection. The
compiler resets its heap between compilations automatically.

**Phase 2 — Run.** Each compiled CDX with a `.expected` sidecar is
booted in its own VM. Serial output is captured and compared
byte-for-byte against the expected output.

### Sidecars

| File | Meaning |
|------|---------|
| `foo.expected` | Compile must succeed; runtime output must match |
| `foo.failing` | Compile must fail with listed CDX error codes |
| `foo.skip` | Skipped entirely (first line = reason) |
| `foo.slow` | Skipped unless `-Slow` (first line = reason) |
| `foo.fatal` | Skipped unless `-Fatal` (kills VM at runtime) |
| `foo.stdin` | Pumped to VM serial after boot (runtime input) |
| `foo.disk` | Attached as IDE disk image |

### Test Results

Results are written to `test-output/_results/`, one file per test.
Each file contains a tab-separated line: `STATUS\tNAME\tDETAIL`.

| Status | Meaning |
|--------|---------|
| PASS_EXPECTED | Compiled, ran, output matched |
| PASS_FAILING | Compiled failed with expected error codes |
| PASS_UNVERIFIED | Compiled but no `.expected` sidecar |
| FAIL_COMPILE | Compile failed unexpectedly |
| FAIL_OUTPUT | Ran but output didn't match |
| FAIL_RUNTIME | VM crashed or timed out |
| FAIL_EXPECTED_BUT_COMPILED | Expected failure but compile succeeded |
| FAIL_WRONG_DIAGNOSTIC | Failed but wrong error codes |
| SKIPPED | Skipped (with reason) |

## VM Configuration

All scripts use `build/vm-config.ps1` for shared VM setup.

### codex-vm (default)

`tools/codex-vm.exe` — a ~4500-line C program using Windows Hypervisor
Platform (WHP). Features: shadow register file (WHP GPR corruption
workaround), NE2000 NIC with NAT, VGA text + GOP framebuffer + Bochs
VBE display, PS/2 keyboard/mouse, UEFI firmware emulation (LocateProtocol,
Block I/O, AllocatePages, GetMemoryMap, GetTime, auto-extract PE from GPT),
PCI config space (3 devices), xHCI USB 3.x controller (mass storage + HID
keyboard + UVC camera with isochronous transfers), Intel HDA audio with
host waveOut output, HPET, IOAPIC, ACPI tables, SMBIOS tables, CMOS RTC,
PC speaker with Beep().

```powershell
# codex-vm is used automatically when tools/codex-vm.exe exists
```

### QEMU (fallback)

Set `$env:USE_QEMU=1` to force QEMU. Required for GDB watchpoints
and TCG tracing.

```powershell
$env:USE_QEMU = 1
build/test.ps1 -Jobs 4
```

### Common VM Parameters

- Accelerator: WHPX (Windows Hypervisor Platform)
- Memory: 2048 MB (configurable via MemMB parameter)
- Serial: dual TCP sockets (data on ch0, control on ch1)
- Network: NE2K ISA NIC with user-mode NAT (10.0.2.x)
- Storage: IDE PIO from `-disk` image file
- USB: xHCI with mass storage, HID keyboard, UVC camera
- Audio: Intel HDA (48kHz 16-bit stereo) via waveOut
- Display: VGA text (80x25), GOP (up to 1024x768), Bochs VBE
- Timers: PIT (host-driven), HPET (QueryPerformanceCounter), CMOS RTC
- Interrupts: dual 8259 PIC, IOAPIC (24 redirection entries)
- Platform: PCI config space, ACPI (RSDP/RSDT/FADT/MADT/DSDT), SMBIOS
- `kernel-irqchip=off` required for bare-metal operation (QEMU only)

## Self-Host Compilation Protocol

`build/compile.ps1` boots the compiler kernel in a VM and
communicates over serial:

1. Wait for `READY` on control channel (ch1).
2. Send mode header (`CDX`, `TEXT`, `IR`, etc.) on data channel.
3. Send foreword library bytes (transitively resolved).
4. Send source bytes.
5. Send EOT (0x04).
6. Read diagnostic lines until `SIZE:<n>` (success) or
   `CODEGEN-HALTED` (failure).
7. On success, read `n` raw bytes of binary output.

### Compile Modes

| Mode | Output |
|------|--------|
| `CDX` | CDX binary |
| `CDX repl` | CDX binary, REPL loop enabled |
| `TEXT` | Codex source text |
| `IR` | IR text dump |
| `MEASURE` | Phase metrics |

Append flags: `TEXT prose`, `CDX repl`, `CDX poison`

Container formats (ELF, PE, IMG) are produced by post-compile
plugs in `codex/plugs/`. See `docs/Designs/Active/Compiler/EmitterExodus.md`.

## Seed Management

The canonical seed is `seed/Codex.cdx` — the signed, self-sustaining
CDX binary, bootable via codex-vm or QEMU multiboot.

### Seed Rebuild Procedure

**Pre-conditions:**
- All source changes are submitted
- The change justifies a rebuild (codegen change, new builtin, foreword
  change that affects compilation)

**Steps:**
1. Run the full build: `build/build.ps1`. All phases must PASS.
2. Install new seed: `Copy-Item build/output/Sut.cdx seed\Codex.cdx -Force`
   Use `build/output/Sut.cdx` — the signed SUT. Do NOT use
   `build-output/bare-metal/Codex.cdx` (unsigned boot kernel).
3. Self-verify: `build/test-self-verify.ps1`. Must print
   "THE SEED VERIFIES ITSELF".
4. Capture digest: `Get-FileHash -Algorithm SHA256 seed\Codex.cdx`
5. Submit to Perforce.

**Rules:**
- Never skip the full build. Never skip self-verify.
- One seed per CL. CDX is primary.
- The bootable image (`seed/Codex.img`) is a separate distribution
  artifact built by `build/build-boot-img.ps1`. It is NOT part of
  the seed rebuild.
- Signing is automatic.
- Never `git add -A`. Never force-push.

**Codegen changes need a one-pass fixed point.** Step 2's "install
`Sut.cdx`" is correct ONLY when the build prints
`(SUT === stage1 — hard fixed point in one pass)`. A change to code
generation (e.g. `emit-prologue`) built from a pre-change seed is
**two-pass**: the stage0 seed lacks the new codegen, so its `Sut` carries
the change only in its emit logic, not yet in its own function bodies —
`Sut != stage1`, and the real fixed point is `stage1` (`NewSeed.cdx`, which
is unsigned). Installing `Sut.cdx` there leaves a seed that is one pass
short: it compiles correctly but is not byte-identical to itself
(`seed != seed-compiles-seed`), so it must not be trusted or copied up. The
fix is simply to **rebuild again** from that once-built seed — the second
build converges one-pass with the change baked into the seed's own
prologues, and `Sut.cdx` is then the signed fixed point. Always rebuild
until the build reports one pass before submitting a codegen seed.

(The CDX fixed-point check compares content ignoring the signature bytes,
so a signed `Sut` and the unsigned `stage1` still register as one pass when
their code is identical. On copy-up, the gate is `Sut === seed` rebuilt on
the *target* workspace — see `docs/Agents/PerforceProcess.md`.)

## Status Server

`tools/status-server.ps1` serves a single-page dashboard at
`http://localhost:8080/` with live data from the workspace and Perforce.

```powershell
pwsh tools/status-server.ps1            # default port 8080
pwsh tools/status-server.ps1 -Port 9090 # custom port
```

Displays: seed CDX info, test battery results, module counts by quire,
recent changelists. Auto-refreshes every 30 seconds.

## Symbol Map

`seed/Codex.map` is a text file mapping code addresses to function
names, emitted during each seed build. Format:

```
# Codex Symbol Map
# Address         Size  Name
0x00100114 7 __alloc
0x0010011B 251 __str_concat
...
0x002F56FB 614 lookup-expr-type
```

Each line: hex address, decimal byte size, function name. Use it to
identify crash locations from `!EXC` diagnostic output:

```powershell
# Look up a crash RIP in the map
$rip = 0x2f588b
$lines = Get-Content seed\Codex.map | Where-Object { $_ -match '^0x' }
foreach ($line in $lines) {
    if ($line -match '^(0x[0-9a-fA-F]+)\s+(\d+)\s+(.+)$') {
        $addr = [Convert]::ToInt64($matches[1], 16)
        if ($addr -le $rip -and ($addr + [int]$matches[2]) -gt $rip) {
            "$($matches[3]) at $($matches[1]) (offset +$($rip - $addr))"
        }
    }
}
```

The map is rebuilt whenever the seed is rebuilt. It tracks the
compiler's own functions — not compiled test programs. Test CDX
binaries emit their own MAP block in the build log (visible between
`MAP:` and `MAP-END` lines).

## Native Debugging Toolkit

All debugging uses codex-vm and the PowerShell harness. No GDB, no
WSL, no external tools. The compiler embeds a binary MAP1 symbol map
in every CDX (2600+ functions, ~79KB), and the harness resolves
addresses automatically.

### Crash Reports

When a crash occurs during batch compilation, the harness prints a
resolved crash report:

```
CRASH in lookup-expr-type+0x42 (page fault, CR2=0x2eeef7000000)
  RIP   0x0027484a  lookup-expr-type+0x42
  callR 0x00274200  check-chapter+0x1a0
  R10   0x01a71cb0  (heap @ 21.4 MB)
  Stack trace (heuristic):
    S[0] 0x00274200  check-chapter+0x1a0
    S[3] 0x00261f10  compile-type-check+0x48
```

The `!EXC` line from the guest's exception handler includes RIP, all
callee-saved registers, CR2, callR (return address), and 16 stack
qwords. `Format-CrashReport` (vm-config.ps1) resolves every value in
the code range (0x100000-0x400000) to a function name.

### Manual Address Lookup

```powershell
build/resolve-rip.ps1 0x2748af                # single address
build/resolve-rip.ps1 0x100114 0x200000        # multiple
```

Or from any script that sources vm-config.ps1:

```powershell
Resolve-Rip -Rip 0x2748af                     # -> "function+0xNN"
Resolve-Name -Name "lookup-expr-type"          # -> 0x2F56FB (address)
```

### Breakpoints by Function Name

Patch INT3 at a function's entry point. The guest exception handler
fires `!EXC=03` (vector 3) and dumps register state.

```powershell
build/compile.ps1 -Src foo.codex -Out foo.cdx -Log foo.log `
    -Break "lookup-expr-type"
```

Output:
```
BREAK: patched INT3 at lookup-expr-type+0x0 (0x2F56FB, orig=0x4C)
  CRASH in lookup-expr-type+0x1 (breakpoint)
    RIP   0x002F56FC  lookup-expr-type+0x1
    R10   0x00c00000  (heap @ 6 MB)
    RDI   0x00000010
```

Exit code 5 = breakpoint hit (vs 4 = real crash).

### Interactive Debugger

Run codex-vm directly with `-debug` (and optionally `-map <file>.map`)
to get an interactive debug shell on breakpoints and single-steps.

```
tools/codex-vm.exe -kernel build-output/bare-metal/Codex.cdx ^
    -input input.tmp -output out.tmp -mem 2048 -headless ^
    -debug -map build/output/reg.map
```

When a breakpoint fires (`-Break` patch or runtime `b` command), the
debugger prints registers with symbol resolution and waits for commands:

```
--- Break at 0x12f1de <desugar-def+0> ---
  RIP=0x12f1de <desugar-def+0>
  RSP=00000000007ffa08 RBP=00000000007ffa30 ...
  R13=00000000006ff325
dbg> _
```

**Commands:**

| Command | Description |
|---------|-------------|
| `s` / `step` | Single-step one instruction (sets TF) |
| `c` / `continue` | Resume until next breakpoint |
| `r` / `regs` | Dump all registers with symbol lookup |
| `m <addr> [len]` | Hex+ASCII memory dump (default 64 bytes) |
| `x <addr>` | Read one qword at address |
| `bt` / `backtrace` | Walk RBP chain (resolved frames) |
| `stack` | Dump 16 stack slots with symbol lookup |
| `b <fn\|0xaddr> [if reg=val]` | Set breakpoint (optional condition) |
| `w <addr> [size]` | Set memory watchpoint |
| `sym <name>` | Look up symbol address |
| `q` / `quit` | Exit VM |

**Conditional breakpoints** let you skip high-frequency call sites and
only break when a register matches:

```
dbg> b desugar-def if rdi=0x705320
  conditional: rdi == 0x705320
  breakpoint 0 at 0x12f1de <desugar-def+0>
```

The debugger skips the breakpoint when `rdi != 0x705320` by restoring
the original byte, single-stepping past, and re-patching.

**Workflow: tracing a field corruption**

1. Break at the function that builds the record (`-Break "desugar-def"`)
2. `regs` to see the input parameter (rdi = Def pointer)
3. `x <rdi+0>` to read the Def's first field (ann in CCE order)
4. `step` through the function, watching how field values flow
5. After the record constructor, `x <r10_before+16>` to verify the
   declared-type field was stored correctly
6. Or use `w <addr>` to set a memory watchpoint on the field slot

**Build codex-vm with debugger:**

```powershell
tools/build-vm.ps1   # standard build includes debugger
```

The debugger compiles into the same binary — `-debug` activates it.
Without `-debug`, behavior is unchanged (breakpoints print and continue
as before).

### Debug Compile Mode

Emit phase markers during compilation. When a crash occurs, the
last `DBG:` line in the log identifies which phase was active.

```powershell
build/compile.ps1 -Src foo.codex -Out foo.cdx -Log foo.log `
    -DebugMode
```

Log output:
```
DBG:frontend src=1204413
DBG:emit defs=412
SIZE:2176384
```

### Workflow: Investigating a Crash

1. **Read the crash report.** The harness prints resolved function
   names. Start with the RIP function and the heuristic stack trace.

2. **Read the code.** `resolve-rip.ps1` gives you the function.
   Read that function in the compiler source. Form a theory.

3. **Set a breakpoint.** Use `-Break "suspect-function"` to confirm
   the function is reached and inspect register state at entry.

4. **Use debug mode.** `-DebugMode` shows phase progression. If the
   crash is in emit, the last `DBG:emit` line narrows the window.

5. **Run the poison build.** If you suspect uninitialized memory,
   build a poison seed (see Poison-Alloc section below) and run
   the tests. `CR2=0xCDCDCDCDCDCDCDCD` = uninitialized field.

### compile.ps1 Debug Flags

| Flag | Purpose |
|------|---------|
| `-Break "name"` | INT3 at function entry; use with codex-vm `-debug` for interactive shell |
| `-DebugMode` | Phase markers (`DBG:frontend`, `DBG:emit`) |
| `-Poison` | 0xCD fill in `__alloc` (catches uninitialized fields) |
| `-Repl` | REPL loop (for batch compilation) |
| `-Survey "f:n,..."` | Override per-phase survey multipliers at runtime (no seed rebuild). Fields: `lex-mul`, `parse-mul`, `desugar-mul`, `scope-mul`, `check-mul`, `lower-mul`, `resolve-mul`, `lift-mul`, `headroom`. Appends `survey=...` to the mode line; defaults are byte-identical to `BuildSettings`. Raising a multiplier reserves more deck (safe); lowering too far under-reserves and currently faults rather than bailing cleanly. |

### GDB (Legacy Fallback)

The interactive debugger (`-debug`) covers breakpoints, single-step,
register/memory inspection, conditional breaks, and backtraces. GDB
under WSL with QEMU TCG is a last resort for hardware watchpoints
(DR0-DR3) on specific memory addresses. Rule 6 permits Unix tools
for this purpose only. See `build/gdb-watchpoint.ps1`.

## Poison-Alloc Diagnostic Build

### Background: REPL Batch Stale Data

The test harness compiles multiple tests on a single VM instance via
the REPL loop to avoid per-test VM startup overhead. Between
compilations the REPL loop (X86_64Chapter.codex) resets R10 (bump
allocator), deck-pos, and heap-hwm back to the arena base. It does
NOT zero the freed memory. The `__alloc` helper was three
instructions — `mov rax, r10; add r10, rdi; ret` — returning
uninitialized memory.

This meant the second compilation allocated records on top of the
first compilation's stale data. Any field not explicitly written
after allocation would silently inherit the previous compilation's
value. On first boot the heap was zeroed by hardware, masking the
problem. On second REPL iteration, uninitialized fields contained
live pointers, type tags, or text references from the prior compile.

### Incident Timeline

| CL | Date | Event |
|----|------|-------|
| — | pre-1845 | Intermittent GPFs in REPL batch compilation. Plug compiler crashes under WHPX but not TCG. Crash at `text-compare` called from `bsearch-text-pos` during type lookup, with CR2 pointing into the seed's code section (partial-application trampolines). Six investigation sessions across three agents (see `docs/Test/PLUG-CRASH-INVESTIGATION.md`). |
| 1845 | 2026-05-19 | **Root cause found.** `lookup-expr-type` in Unifier.codex used non-short-circuit `&` to guard a `list-at` access after binary search: `if pos < len & (list-at entries pos).key == k`. When the key was not found (`pos == len`), the right operand executed anyway, reading one element past the list into stale heap. The OOB value — a seed return address shifted 3 bytes — propagated through the type environment and caused a GPF when later dereferenced. Fix: split into nested `if` so the access only executes when `pos < len`. |
| 1885 | 2026-05-20 | **Class fix.** `IrAnd`/`IrOr` now emit conditional jumps instead of bitwise AND/OR. The right operand is only evaluated when the left operand doesn't short-circuit. Eliminates the entire class of non-short-circuit guard bugs. |
| 1927 | 2026-05-21 | **Calloc + REPL hardening.** `__alloc` now zeroes its returned block via `rep stosb` (calloc semantics). REPL loop resets `stdin-eof-flag`, `stdin-eof-settled`, `try-fail-flag`, and `deck-bound-counter` between iterations. `codegen-carry-forward` now carries `vm-profile` (was silently dropped — latent uninitialized field). |

### Audit Results (CL 1927)

**Binary search call sites.** All 8 distinct `bsearch-*` functions
(~20 call sites) across Collections, TypeEnv, TypeChecker, Unifier,
ChapterScoper, LambdaLifting, X86_64Builtins, X86_64Compound were
audited. Every consumer follows the pattern
`if pos < len then if element.key == searchkey then HIT else DEFAULT else DEFAULT`.
No remaining OOB-after-miss vulnerabilities.

**Record construction.** `emit-store-record-fields-by-type`
(X86_64Compound.codex:612) iterates type-definition fields and
matches them against provided constructor fields via
`find-field-local-slot`. If a field name doesn't match (slot = -1),
the field's memory is not written. In a well-typed program every
field is provided, so this path is unreachable — but it would be
the mechanism if a name mismatch existed. The calloc ensures zeros
rather than stale data if this path ever fires.

**`codegen-carry-forward` fix.** This function creates a fresh
CodegenState preserving accumulated code/data but resetting locals.
It was not copying `vm-profile` — the field was uninitialized after
carry-forward. Fixed to carry both `vm-profile` and the new
`poison-alloc` flag.

### Poison Build: 105/105 Pass

On 2026-05-21, the compiler was built with `poison-alloc = True`,
producing a seed where `__alloc` fills every allocation with `0xCD`
instead of zeroing. The full test battery (105 tests, 4 batch REPL
slots) was run against this poison seed.

**Result: 105 pass, 0 fail.**

Every dereference of `0xCDCDCDCDCDCDCDCD` (non-canonical x86-64
address) would be an immediate page fault. Zero failures means every
heap-allocated record in the compiler is fully initialized before
any field is read. There are no latent uninitialized-field
dependencies hiding behind the calloc's zero fill.

### Conclusions

1. **CL 1845 was the real bug.** The non-short-circuit `&` caused
   an OOB read that copied a stale code-section address into the
   type environment. CL 1885 eliminated the entire class.
2. **The calloc is a safety net, not a patch.** The poison build
   proves the compiler initializes all its fields. The zero fill
   prevents future regressions from producing stale-data corruption
   — they'd produce zero-value bugs instead, which are detectable
   but not catastrophic.
3. **The REPL kernel state resets close the remaining exposure.**
   `stdin-eof`, `try-fail-flag`, and `deck-bound-counter` are now
   zeroed between iterations, preventing I/O state leakage.
4. **The poison build is a release gate.** Before any public build,
   run the test battery against a poison seed. If all tests pass,
   the compiler has no uninitialized-field dependencies.

### How to Run a Poison Build

```powershell
# 1. Concat compiler source
build/concat-codex-self.ps1 -CodexDir codex/compiler -OutFile build/output/Codex.codex

# 2. Compile a poison seed (0xCD fill instead of zero)
build/compile.ps1 -Src build/output/Codex.codex `
    -Out build/output/poison-seed.cdx `
    -Log build/output/poison-build.log -Repl -Poison

# 3. Run the full test battery against the poison seed
build/test.ps1 -CodexCdx build/output/poison-seed.cdx -Jobs 4

# Expected: 105 pass, 0 fail.
# Any failure means an uninitialized field was read during compilation.
# The crash CR2 will be 0xCDCDCDCDCDCDCDCD — look up RIP in the
# symbol map to find the function that dereferenced the bad pointer.
```

## Release-to-Public Gate

These steps run ONLY when publishing the seed to the public mirrors
(GitHub, GitLab) — never on routine seed rebuilds or copy-to-main. The
day-to-day gates (text + CDX fixed point, sample battery) already prove
correctness; the items here are public-facing polish and are not usually
needed internally.

1. **Poison build passes** (above) — the seed has no uninitialized-field
   dependencies.
2. **Refresh `seed/Codex.map`.** This is the one artifact that silently
   drifts: the seed is built `-Repl`, and `-Repl` mode does not emit the
   text `MAP:` block that `compile.ps1` captures into `<out>.map`, so
   neither the seed rebuild nor copy-to-main ever refreshes it.
   Regenerate by compiling the compiler source NON-repl with the
   published seed and copying the emitted map:

   ```powershell
   Copy-Item -Force seed/Codex.cdx build-output/bare-metal/Codex.cdx
   build/concat-codex-self.ps1 -CodexDir codex/compiler -OutFile build/output/Codex.codex
   build/compile.ps1 -Src build/output/Codex.codex -Out build/output/SutMap.cdx -Log build/output/map.log
   Copy-Item -Force build/output/SutMap.map seed/Codex.map
   ```

   The non-repl binary differs from the `-Repl` seed only in unnamed
   padding; every named function offset is byte-identical (cross-check
   against the seed's embedded MAP1 if unsure), so the text map is exact.
   The embedded MAP1 in each CDX is authoritative for crash reports
   regardless — the text map only feeds `-Break` and `resolve-rip.ps1`.
