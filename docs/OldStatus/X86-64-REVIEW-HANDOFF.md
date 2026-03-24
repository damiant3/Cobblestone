File: docs\OldStatus\X86-64-REVIEW-HANDOFF.md
````````markdown
# x86-64 Backend Review & Merge — Handoff Summary

**Date**: 2026-03-23 (verified via `Get-Date`)
**Agent**: Copilot (VS 2022, Windows)
**Branch**: `windows/review-x86-64` → merged to `master`
**Remote**: https://github.com/damiant3/NewRepository
**Merge commit**: `b1f280c`

---

## What Was Done

### Task 1: Review of `cam/x86-64-backend` (commit `0379c70`)

Reviewed the callee-saved register clobbering fix. Single file changed:
`src/Codex.Emit.X86_64/X86_64CodeGen.cs` (+11/-10).

**Root cause confirmed**: Original prologue did `push rbp → mov rbp,rsp → sub rsp,frameSize → push callee-saved`.
Spill offsets assumed callee saves were immediately below rbp, but they were actually below the frame space.
When a function used >5 locals (triggering spills), spill slot 0 at `[rbp-48]` overwrote saved R14.

**Fix verified**: Prologue reordered to `push rbp → mov rbp,rsp → push callee-saved → sub rsp,spillFrame`.
Epilogue uses `lea rsp, [rbp - 40]` to skip spill space before popping callee-saved regs.

Stack layout after fix:

| Address | Content |
|---------|---------|
| `[rbp+0]` | saved rbp |
| `[rbp-8]` | saved rbx |
| `[rbp-16]` | saved r12 |
| `[rbp-24]` | saved r13 |
| `[rbp-32]` | saved r14 |
| `[rbp-40]` | saved r15 |
| `[rbp-48]` | spill slot 0 ← safely below saves |
| `[rbp-56]` | spill slot 1 |

Spill offset math (`StoreLocal`/`LoadLocal`) confirmed correct:
- Slot 0: `-(0+1)*8 - 5*8 = -48` ✅
- Slot 1: `-(1+1)*8 - 5*8 = -56` ✅

### Task 2: Build & Test Verification

- **Build**: zero warnings, zero errors on x86-64 project and all dependencies.
- **Tests**: 414/414 passed in main suite. 1 pre-existing failure in `Codex.AgentToolkit.Tests`
  (`Peek_non_numeric_start_does_not_crash` — `ArgumentOutOfRangeException` in `format_lines_loop`,
  completely unrelated to x86-64 changes).

### Task 3: Cleanup

Removed 2 `.snap` files left behind by the previous agent session:
- `src/Codex.Emit.X86_64/X86_64CodeGen.cs.snap`
- `src/Codex.Emit.X86_64/X86_64Encoder.cs.snap`

### Task 4: Merge to Master

Merged `cam/x86-64-backend` into `master` via `--no-ff` and pushed to `origin/master`.

---

## x86-64 Backend Status

### Working
- **Encoder** (`X86_64Encoder.cs`): Full instruction set — MOV, ADD, SUB, IMUL, IDIV, CMP, TEST,
  Jcc, CALL, RET, PUSH, POP, LEA, SYSCALL, SETCC, MOVZX, shifts, bitwise ops.
- **ELF writer** (`ElfWriterX86_64.cs`): Generates Linux x86-64 ELF binaries (~4KB).
- **Codegen** (`X86_64CodeGen.cs`): All IR nodes — literals, binary ops, if/else, let, do, apply,
  records, field access, pattern matching (wildcard, var, literal, ctor), lists, regions, escape copy.
- **Frame layout**: Fixed. Callee-saved pushes before `sub rsp`.
- **Runtime helpers**: `__itoa`, `__str_concat`, `__str_eq`, `__escape_text` (stubs wired).
- **CLI wiring**: `--target x86-64` flag in `Program.Build.cs`.

### Stubbed / TODO
- `__read_file`, `__text_to_int` — wired but return 0.
- Per-type escape copy helpers (record/list/sum) — architecture wired, drain-queue emits stubs.
- Closures / partial application — not yet implemented.
- Large frame sizes (>127 bytes spill space) — needs imm32 encoding in prologue patch.
- `__str_concat` / `__str_eq` — stubs, need byte-level implementation.

### Next Steps
1. **QEMU verification**: `qemu-x86_64 ./hello` on Linux sandbox (or native WSL).
2. Fix any encoding bugs found during verification.
3. Implement remaining builtins (`__str_concat`, `__itoa`, `__read_file`).
4. Test with self-hosted compiler samples.
5. Implement closures / partial application.
6. Handle large frame sizes (>127 bytes spill space) with imm32 encoding.

---

## Native WSL Verification (2026-03-23)

First correct x86-64 execution on real hardware — verified natively in WSL, no QEMU needed.

| Program | Expected | Result |
|---------|----------|--------|
| `factorial(5)` | 120 | 120 ✓ |
| `factorial(10)` | 3628800 (exit 0, mod 256 = 0) | exit 0 ✓ |
| `sum-to(5)` | 15 | 15 ✓ |
| `square(5)` | 25 | 25 ✓ |

### Bugs Found and Fixed

1. **Frame layout collision** — Callee-saved pushes after `sub rsp` overlapped spill slots.
   Spill slot writes clobbered saved registers when functions had >5 locals.
   *Fix*: Reordered prologue to push callee-saved regs *before* `sub rsp,spillFrame`.

2. **EFLAGS clobbering** — `xor` used to zero a register before `setcc` destroyed the
   comparison flags set by the preceding `cmp`/`test` instruction.
   *Fix*: Use `movzx` or reorder to avoid clearing flags before `setcc`.

3. **Register pool aliasing** — `LoadLocal` and `AllocTemp` both handed out RAX/RCX,
   causing the second operand of a binary op to clobber the first.
   *Fix*: Separated the register pools so temporaries don't alias local-load destinations.

---

## Pre-existing Issue Noted

`Codex.AgentToolkit.Tests.CodexAgentExeTests.Peek_non_numeric_start_does_not_crash` fails with
`ArgumentOutOfRangeException` in `format_lines_loop`. This is a bug in `codex-agent` peek,
not related to any x86-64 work.
