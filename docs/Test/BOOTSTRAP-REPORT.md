# Bootstrap Verification Report

**Date:** 2026-04-16
**Compiler version:** master @ `e84f674`
**Result:** Bootstrap 1 and 1.1 green. **Bootstrap 2 (pingpong) RED**
since `4420b92` (2026-04-15 22:05) — parametric type emission
regression in the Codex-text emitter. Bootstrap 3 (bare-metal binary)
not yet green by design.

See `docs/CodexBootstrap.png` for the diagram.

---

## Naming

Three separate fixed-point proofs, each on a different compiler image
or output modality:

| # | Name | Runtime | Emitter | What it proves |
|---|------|---------|---------|----------------|
| 1 | Bootstrap 1 | .NET | C# | .NET self-host is self-consistent when emitting C# |
| 1.1 | Bootstrap 1.1 | .NET | Codex text | .NET self-host is self-consistent when emitting Codex text |
| 2 | Bootstrap 2 (pingpong) | bare-metal ELF under QEMU | Codex text | self-host compiled to ELF, run on bare hardware, reproduces source semantically and its own output byte-identically |
| 3 | Bootstrap 3 | bare-metal ELF under QEMU | x86-64 machine code | self-compiled binary reproduces itself byte-identically |

**Bootstrap 1 and 1.1 are not pingpong.** Both run under `dotnet`. A green
"BOOTSTRAP 1" or "BOOTSTRAP 1.1" message from `codex bootstrap` is a
`dotnet`-hosted result only; it says nothing about bare metal.

**Bootstrap 2 (pingpong) runs on bare metal.** An ELF runs in QEMU, reads source over
serial, writes Codex text back out. That output, fed back in, must
produce itself byte-identically.

---

## What Each Bootstrap Requires

### Bootstrap 1 (.NET, C# output)

- Ref compiler → stage 0 (self-host as C#).
- `dotnet` compiles and runs stage 0 → stage 1 (self-host compiling
  itself, emitting C#).
- Stage 1 → stage 3 (same operation, run from stage 1's compiled C#).
- Fixed point: **stage 1 === stage 3**.

### Bootstrap 1.1 (.NET, Codex-text output)

- Stage 0's emitter emits Codex text → bootstrap 1.1 stage 1.
- Stage 1's emitter emits Codex text → bootstrap 1.1 stage 2.
- Fixed point: **bootstrap 1.1 stage 1 === bootstrap 1.1 stage 2**.

### Bootstrap 2 (pingpong) — bare-metal ELF, Codex-text output

- Ref compiler produces the bare-metal ELF (`Codex.Codex.elf`, target
  `x86-64-bare`). ELF runs under QEMU — no OS, no libc, no dotnet.
- QEMU sends the ELF `TEXT\n` + source over serial, captures Codex
  text output → bootstrap 2 (pingpong) stage 1.
- sem-equiv(source, bootstrap 2 (pingpong) stage 1) must PASS. Without this,
  stage 1 is garbage and the stage-1=stage-2 check is meaningless.
- QEMU repeats with bootstrap 2 (pingpong) stage 1 as input → bootstrap 2 (pingpong) stage 2.
- Fixed point: **bootstrap 2 (pingpong) stage 1 === bootstrap 2 (pingpong) stage 2**,
  AND sem-equiv PASS.

### Bootstrap 3 (bare-metal binary)

- ELF runs `BINARY` mode, emits x86-64 machine code for its own source.
- Output is itself a bare-metal ELF.
- Fixed point: self-compiled binary === reference-compiled binary.
- **Not green.** MM4 Phase 8.

---

## Current State (2026-04-16)

| # | Check | Size | Time | Verdict |
|---|-------|------|------|---------|
| 1 | stage 1 === stage 3 | 946,826 chars | ~11s | PASS |
| 1.1 | stage 1 === stage 2 | 549,881 chars | ~5s | PASS |
| 2 | bootstrap 2 (pingpong) stage 1 produced | 562,740 B | 39s | produced |
| 2 | sem-equiv(source, stage 1) | — | ~1s | **FAIL** |
| 2 | stage 1 === stage 2 | — | — | **SKIPPED** (gated on sem-equiv) |
| 3 | self-compiled binary byte-identical | — | — | not green by design |

Bootstrap 2 (pingpong) stage 1 bare-metal metrics:
- Stack HWM: 2,497,152 B
- Heap HWM: 1,011,859,392 B (≈965 MB of the ≈1 GB bare-metal heap)

### Bootstrap 2 (pingpong) regression — root cause

Sem-equiv(source, bootstrap 2 (pingpong) stage 1) reports:

- **Dropped (1)**: `foreword--maybe: Maybe (a) (line 1)` — source has
  `Maybe (a) =`, stage 1 has `Maybe =`. Type parameter dropped from
  the parametric type definition.
- **Extra (2)**: `?: Maybe :` (orphan, no chapter attribution);
  `emit--csharp-emitter: emit-pattern : IRPat -> Text` (pre-existing,
  not in this session).
- **Sig mismatches (5)**: `from-maybe`, `is-just`, `is-none`,
  `maybe-map`, `maybe-bind`. Source: `Maybe a -> ...`. Stage 1:
  `Maybe -> ...`. Type argument stripped from type *applications*
  in sigs too.
- Bodies: 1,536 of 1,536 match. Only type declarations and type
  references lose parametricity.

Location of the bug: `Codex.Codex/Emit/CodexEmitter.codex:11-17`
(`emit-type-def`). Both `ARecordTypeDef (name) (tparams) (fields) (s)`
and `AVariantTypeDef (name) (tparams) (ctors) (s)` destructure
`tparams` but never emit it. The type-application case in
`emit-type-expr` similarly loses its argument list when emitting
sigs (verified by diffing source sigs against `bootstrap2-stage1.codex`
at `from-maybe` et al).

Regression introduced at `4420b92` (Merge hex-cam/self-host-maybe,
2026-04-15 22:05). The Maybe merge added parametric Maybe records to
self-host but did not update the Codex-text emitter to carry type
parameters through definition or reference emission. Bootstrap 1 and
1.1 pass because they don't reparse their own Codex-text output —
Bootstrap 2 (pingpong) fails because pingpong does.

---

## Files in This Directory

| File | Size (B) | Contents |
|------|----------|----------|
| `source.codex` | 600,341 | Concatenated Codex source. Input to all bootstraps. |
| `bootstrap1-stage0.cs` | 1,141,723 | Ref compiler → C# |
| `bootstrap1-stage1.cs` | 946,826 | Stage 0 self-host → C# under `dotnet` |
| `bootstrap1-stage3.cs` | 946,826 | Stage 1 self-host → C# (== stage 1) |
| `bootstrap1.1-stage1.codex` | 549,881 | Stage 0 self-host → Codex text under `dotnet` |
| `bootstrap1.1-stage2.codex` | 549,881 | Stage 1 self-host → Codex text under `dotnet` (== stage 1) |
| `bootstrap2-stage1.codex` | 562,701 | Bare-metal ELF → Codex text (STACK/HEAP/RESULT stripped) |
| `bootstrap2-stage2.codex` | 36 | Placeholder: `could not produce, sem-equiv failed`. Stage 2 correctly gated and not run. |

The placeholder content of `bootstrap2-stage2.codex` is the proof that
bootstrap 2 (pingpong) is red at master.

---

## How to Reproduce

Prerequisites: .NET 8 SDK; WSL with QEMU + KVM (`/dev/kvm` rw for user's
primary group).

Full run (clean + build + bootstraps 1, 1.1, 2):

```bash
rm -rf build-output tools/Codex.Bootstrap/CodexLib.g.cs \
       tools/Codex.Bootstrap/bootstrap-output Codex.Codex/out .codex-build
find . -type d \( -name bin -o -name obj \) -not -path '*/.git/*' | xargs rm -rf
dotnet build Codex.sln
wsl bash tools/pingpong.sh
```

`pingpong.sh` Phase 3 = bootstraps 1 + 1.1. Phase 4 = bootstrap 2 (pingpong).
Bootstrap 3 is not exercised by `pingpong.sh`.

Expected output at master 2026-04-16:

```
✅ BOOTSTRAP 1 (.NET, C# output): Stage 1 = Stage 3 (946,826 chars identical)
✅ BOOTSTRAP 1.1 (.NET, Codex-text output): Stage 1 = Stage 2 (549,881 chars identical)
Stage 1: 562740 bytes (39s)
Verdict: FAIL
sem-equiv: FAIL (exit 1)
Skipping stage 2 — semantic equivalence failed.
```

The first two ✅ are bootstraps 1 and 1.1. The FAIL is bootstrap 2 (pingpong).
Do not report "pingpong green" based on the two ✅.

---

## Trust Chain

1. Ref compiler (`src/`) is hand-written, auditable C# — the trust root.
2. Ref → bootstrap 1 stage 0 (self-host as C#).
3. Stage 0 → stage 1 via `dotnet`. Stage 1 → stage 3. Stage 1 === stage
   3 proves .NET self-consistency for C# emission. **Bootstrap 1.**
4. Same path with Codex-text emitter. Bootstrap 1.1 stage 1 === stage
   2. Proves .NET self-consistency for Codex-text emission. **Bootstrap 1.1.**
5. Ref → bare-metal ELF (self-host compiled for `x86-64-bare`).
6. ELF under QEMU → bootstrap 2 (pingpong) stage 1. sem-equiv(source, stage 1)
   PASS. **Currently failing.**
7. Same ELF → bootstrap 2 (pingpong) stage 2 (from stage 1). stage 1 === stage 2.
   Gated on step 6; blocked.
8. Bare-metal binary self-compile byte-identical. **Bootstrap 3** — not
   green by design.

Step 1 is the only trust assumption. After that, the fixed-point
algebra closes the loop — but only when every step is green.
Bootstraps 1 and 1.1 being green does not imply bootstrap 2 (pingpong) is green.
