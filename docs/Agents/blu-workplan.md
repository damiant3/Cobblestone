# blu -- workplan

*Status, not journal. Per-CL history is in Perforce. Durable process truths are in
`docs/Agents/PerforceProcess.md` and `CLAUDE.md`, not here. This file is the current
picture and the next moves only. Keep it under ~80 lines.*

## Status

Filetype conversions and reek's LIR both landed and are merged down; workspace is
clean at head. Clean build + regular battery green 2026-07-17: `build/build.ps1`
one-pass fixed point, BVT + plug gates green; battery 471 pass / 4 fail / 20 skip.
The 4 reds are pre-existing and non-codegen, none introduced by the merge:
`let-shadow-scope` (`.expected` missing its trailing newline; value is correct),
`smp-arm64-boot` / `smp-riscv-boot` (QEMU cross-arch tests with no `.skip`, wrongly
run by the x86 codex-vm battery -- their `.expected` is only reachable on QEMU), and
`uefi-read-key-nofirmware` (codex-vm returns 0 not -1 with no firmware, BACKLOG 7.17).

Prior targets 7.20 and 7.2 are CLOSED (7.20 was my own misfile, the inverse of a real
bug -- console-test fixed and un-skipped; all 44 stub-reason tests read and cleared).

## My target -- 1.13, then the 2.14 residue

- 1.13 residual: one citable table the effect-vocab readers derive from, so they
  cannot diverge by construction rather than by the `check-effect-vocab.ps1` guard
  (the guard is a truce, not the fix -- as 1.12 says of its own). The effect-vocab
  half is in my lane: foreword `effect <Name> where` declarations and the
  `capability-vocabulary` array. 1.12's sibling cap-name->bit table
  (`boot-cap-mask-for`) is in Emit -- coordinate with reek before touching that half.
- 2.14 residue: fold the emitter column into the one `BuiltinSpec` table.
  NameResolver and TypeEnv already derive from it; only `x86-builtin-emitters`
  remains separate -- and it is in Emit, so coordinate with reek.

## My lane (own it; others stay out)

codex/compiler Types, codex/compiler Syntax (parser), codex/foreword/core CCE and
the encoding chapters, TLS 1.3. Not reek's Emit/IR/LIR, not fester's apps/boot.

## Open in my lane (BACKLOG, after the target)

- 2.5 / 2.6 proofs reaching the stdlib; the positive proof-term grammar
  (ProofTotalityProbe Option B).
- 5.3 DTLS secure channel (Deferred): app-traffic keys + record protection +
  fragmentation. The anonymous handshake is NOT a secure channel.

## For other agents

- Filetype conversions landed: record a `.expected` via `test-run.ps1`'s OutFile,
  which strips the leading 0x01 and CRLF and types the file correctly. A stray 0x01
  or an em-dash in a sidecar flips its p4 filetype and fails the battery.

## The one parser invariant to keep

`parse-selector-expr` must CONSUME the dot before recursing, or it returns ExprOk
without progress and the parser loops until the stack blows (2.26). Progress, not
recursion depth, is the termination witness.
