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

4. **Sign**: If `D:\Projects\signing.key` exists, compile and run an
   inline Ed25519 signing program to embed a signature in the CDX
   header (bytes 40–135).

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

`tools/codex-vm.exe` — a 400-line C program using Windows Hypervisor
Platform (WHP). Features: shadow register file (WHP GPR corruption
workaround), NE2000 NIC with NAT, VGA display, PS/2 keyboard/mouse,
UEFI emulation, GOP framebuffer.

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
- Network: NE2K ISA NIC
- `kernel-irqchip=off` required for bare-metal operation (QEMU only)

## Self-Host Compilation Protocol

`build/test-compile.ps1` boots the compiler kernel in a VM and
communicates over serial:

1. Wait for `READY` on control channel (ch1).
2. Send mode header (`CDX`, `ELF`, `TEXT`, `IR`, etc.) on data channel.
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
| `ELF` | ELF x86-64 bare-metal |
| `EFI` | PE32+ UEFI application |
| `UEFI` | PE32+ UEFI app (ConOut) |
| `IMG` | GPT disk image |
| `TEXT` | Codex source text |
| `IR` | IR text dump |
| `MEASURE` | Phase metrics |

Append profile: `ELF QEMU-11.0.0`
Append flags: `TEXT prose`

## Seed Management

The canonical seed is `seed/Codex.cdx` — the self-sustaining CDX
binary, bootable via codex-vm or QEMU multiboot. `seed/Codex.img` is
a UEFI-bootable GPT disk image.

### Seed Rebuild Procedure

**Pre-conditions:**
- All source changes are submitted
- The change justifies a rebuild (codegen change, new builtin, foreword
  change that affects compilation)

**Steps:**
1. Run the full build: `build/build.ps1`. All phases must PASS.
2. Install new seed: `Copy-Item build-output\bare-metal\Codex.cdx seed\Codex.cdx -Force`
3. Build bootable image: `build/build-boot-img.ps1`
4. Self-verify: `build/test-self-verify.ps1`. Must print
   "THE SEED VERIFIES ITSELF".
5. Capture digests: `Get-FileHash -Algorithm SHA256 seed\Codex.cdx`
6. Submit to Perforce.

**Rules:**
- Never skip the full build. Never skip self-verify.
- One seed per CL. CDX is primary; ELF is derived.
- IMG is the distribution artifact.
- Signing is automatic if `D:\Projects\signing.key` exists.
- Never `git add -A`. Never force-push.

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

## Debugging with GDB and QEMU

For memory corruption hunting, GDB under WSL with QEMU is the primary
tool. Rule 5 permits Unix tools for this purpose. The PowerShell script
`build/gdb-watchpoint.ps1` wraps this workflow.

### Workflow: Trace First, Probe Second

1. **Trace** — run Codex.cdx under QEMU **TCG** (no KVM) with
   `-d in_asm` to capture every translated block. Use this to find
   which addresses are actually executed.
2. **Probe** — run Codex.cdx under QEMU **KVM** with gdbstub, set a
   hardware breakpoint at the target address, inspect registers when hit.

Never set a gdb `hbreak` at an address you have not first confirmed
is in the trace.

### GDB Script Skeleton

```
set architecture i386:x86-64
target remote :1234
set pagination off
set confirm off

hbreak *0xADDRESS
continue
printf "HIT rip=%#lx rdi=%#lx rsi=%#lx\n", $rip, $rdi, $rsi

kill
quit
```

Must set architecture BEFORE `target remote`. Use the Register
Convention table in `docs/ArchitectsSketchbook.md` to interpret
register values — Codex does not use the System V ABI.

### Known GDB/QEMU Quirks

1. **HW breakpoint requires exact instruction boundary.** An `hbreak`
   mid-instruction silently never fires.
2. **Only 4 HW breakpoints (DR0-DR3).** A 5th fails silently. Use
   software `break` (INT3) for overflow.
3. **One continue per session.** After a HW bp hits, a second
   `continue` fails. Set all breakpoints before the first `continue`.
4. **TCG is slow.** `-d in_asm` forces TCG (~20-60s vs ~2s under KVM).

### QEMU Debug Flags

| Flag | Purpose |
|------|---------|
| `-kernel Codex.cdx` | Multiboot boot of CDX |
| `-serial stdio` | Kernel's `CDX\n<src>\x04` input, binary output |
| `-device isa-debug-exit,iobase=0xf4,iosize=0x04` | `out 0xf4, 0` exits QEMU cleanly |
| `-gdb tcp::1234 -S` | GDB stub on port 1234, start halted |
| `-enable-kvm` | 10x+ faster — use for all iterative debug runs |
| `-d in_asm -D file.log` | Record every translated block — TCG only, no KVM |
| `-display none -no-reboot -m 1024` | Headless, 1 GB |
