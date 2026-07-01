# Cam Session Prompt — 2026-03-23

## Context

Pull master. Run `codex-agent doctor`, `codex-agent status`, `codex-agent handoff show`.

Read `docs/CurrentPlan.md` for full status. Read `docs/Designs/TWRP-BUILD-HANDOFF.md` for the phone blocker.

### What happened since your last session

1. **Your escape copy work merged to master.** `cam/riscv-escape-copy` — Phase 2a (NeedsEscapeCopy annotation) and Phase 2b (per-type escape copy helpers for Text, Record, List, Sum). Linux reviewed, found a stack overflow on recursive types, you fixed it (`emit named per-type escape copy helpers`). Clean merge.

2. **ARM64 backend complete.** Agent Windows built Arm64Encoder, Arm64CodeGen (1,740 lines), ElfWriterArm64. `codex build --target arm64` produces ELF64 AArch64 binaries. Wired into CLI. Not yet verified under QEMU.

3. **Phone effects complete.** 7 new effects: Network, Display, Camera, Microphone, Location, Sensors, Identity. 7 prelude files, 13 new capability enforcement tests. 773 tests total, all green.

4. **V4 proof-carrying facts merged.** Proofs verified at view composition time.

5. **Phone hardware ready but blocked.** Samsung SM-G935T (T-Mobile S7 Edge) is backed up, SIM removed, OEM unlocked, Odin connected. **No pre-built TWRP exists for hero2qlte** (Qualcomm S7 Edge). Device tree source exists at `github.com/jcadduono/android_device_samsung_hero2qlte`. Full build instructions in `docs/Designs/TWRP-BUILD-HANDOFF.md`.

6. **Mojibake cleaned up** in Arm64CodeGen.cs — 29 corrupted UTF-8 comments fixed to clean ASCII.

7. **Branch cleanup done.** Only `master` and your `cam/riscv-escape-copy` (local) remain. All other branches pruned.

### Git log (recent)
```
85cb167 move to designs
1730830 docs: TWRP build handoff for Agent Linux — hero2qlte recovery.img needed
a4d9880 merge: cam/riscv-escape-copy — RISC-V escape copy for regions (Camp III-A Phase 2b)
d777da7 fix: emit named per-type escape copy helpers, fixing recursive type stack overflow
727825e merge: windows/phone-effects -- phone effects + ARM64 mojibake cleanup
b0e597c fix: clean up mojibake UTF-8 corruption in Arm64CodeGen.cs comments
de702fb merge: windows/phone-effects — 7 phone effect preludes + capability enforcement tests
```

---

## Priority Tasks

### Task 1: ARM64 QEMU Verification (HIGH — unblocks phone)

Verify the ARM64 backend output under `qemu-aarch64`. Steps:

```bash
# Build an ARM64 binary from Windows-side output or build fresh
dotnet run --project tools/Codex.Cli -- build samples/hello.codex --target arm64
dotnet run --project tools/Codex.Cli -- build samples/factorial.codex --target arm64

# Run under QEMU
qemu-aarch64 ./hello
qemu-aarch64 ./factorial
```

Expected: `Hello, world!` and `120`. If it fails, trace with `qemu-aarch64 -d in_asm,exec` and report what's wrong. The ARM64 codegen is modeled after the RISC-V codegen which you know well.

### Task 2: TWRP Build for hero2qlte (HIGH — unblocks phone flash)

If you have a Linux environment with ~15GB free disk:

Full instructions in `docs/Designs/TWRP-BUILD-HANDOFF.md`. Summary:
- `repo init` with TWRP minimal manifest
- Add jcadduono's device tree + kernel to local manifests
- `lunch omni_hero2qlte-eng && make recoveryimage`
- Output: `out/target/product/hero2qlte/recovery.img`
- Get it to Damian (GitHub release, scp, whatever works)

This is the bottleneck — no pre-built TWRP for the Qualcomm S7 Edge exists anywhere.

### Task 3: Camp III-A Phase 2c — Escape Analysis (MEDIUM)

Your Phase 2a/2b escape copy work is merged. The next step is actual escape analysis — statically determining which regions' values escape their scope, so we can re-enable region heap reclamation (currently disabled since the Camp II-C summit push). This is the path to a real linear allocator.

### Task 4: x86-64 Backend (LOWER)

Third native ISA after RISC-V and ARM64. Same pattern — Encoder, CodeGen, ElfWriter. Most common desktop target. Can wait until ARM64 is verified.

---

## Build & Test

```bash
codex-agent doctor
codex-agent build    # expect 0 real errors (CS5001 in Codex.Codex is pre-existing)
codex-agent test     # expect 773 pass, 1 pre-existing fail (Peek_non_numeric_start)
```

773 tests passing. Build clean. Master is stable.
