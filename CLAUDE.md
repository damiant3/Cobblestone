# CLAUDE.md — Codex Project Instructions

## What This Is

Codex is a new programming language, self-sustaining compiler, tools, operating system, repository protocol, trust lattice, encoding, and more. We take the best of type theory, language design, aesthetics, security research, and actual practice. We leave everything else behind. If we didn't build it, we don't trust it. Codex is a new computational substrate intended to be impervious to all currently known attack vectors by-design.

The project was started 3/14/2026.

### The Founding Vision (docs/Stories/Vision/NewRepository.txt)

The original prompt that started the project:

> the new repository. condense all the good ideas humans have had in
> github, sourceforge, etc into a new language. start from first
> principles, find the best implementation, the best abstraction. port it
> to a language that can be transpiled to any old human designed language,
> it abstracts them all into a single perfect language. it is the basis
> for all future code. it exists for human reading and machine. it should
> read like a book. fulfill liskov's hopes for cobol. then we delete
> github and sourceforge entirely fully replaced with a single, ideal
> solution. write the book.

From there the design grew into: a literate-programming language where
prose is load-bearing, a type system with dependent types / linear types /
effect types, a content-addressed repository protocol replacing Git
(facts, proposals, verdicts, trust lattice), a unified environment
(Reader, Writer, Verifier, Explorer, Executor, Narrator, Historian), and
transpilation targets from Rust to WASM to LLVM IR. The full founding
document is in the file above.

## Docs Index

On session start, run `Get-ChildItem -Path docs -Recurse -Name` to see the
live docs tree. Key entry points:

- `docs/PM/CurrentPlan.md` — current plan (Closing the Toolbox, 9 gaps)
- `docs/PM/BACKLOG.md` — outstanding work items
- `docs/DevelopersGuide.md` — language syntax, types, CPL, seed rebuild procedure
- `docs/DevelopersRulebook.md` — foreword quire catalog, library rules
- `docs/UsersHandbook.md` — VS Code setup, getting started
- `docs/VisionAndVirtues.md` — founding vision, non-negotiables, engineering virtues
- `docs/Agents/PerforceProcess.md` — shelve/revert/sync protocol
- `docs/Designs/Active/` — all in-progress designs (compiler, hardware, language, OS, features, tools)
- `docs/Designs/Done/` — completed technical work (shipped designs, reviews, bugs, bootstrap, test)
- `docs/PM/Done/` — archived PM (handoffs, plans, updates, projects)
- `docs/Suspended/` — paused work (abandoned backends, phone project)
- `docs/Stories/Vision/NewRepository.txt` — THE founding prompt

## Current State

**The compiler is a hard fixed point of itself on bare metal.** Codex
compiles itself end-to-end on bare metal (QEMU x86-64, no OS, no libc),
and the output of that self-compile compiled by itself is byte-identical
to itself. No C# anywhere in the chain.

The canonical artifact is `seed/Codex.cdx` — a ~1.8 MB
self-sustaining CDX binary, bootable via QEMU multiboot. The CDX is the
root of trust; the ELF is a derived artifact.

Current plan: `docs/PM/CurrentPlan.md`
Backlog: `docs/PM/BACKLOG.md`

### Bootstrap History — 2026-04-24: The cord is cut

All four bootstraps green for the first time, 41 days from project start:

| Bootstrap | Path | Result |
|---|---|---|
| BS1 | .NET → C# | Legacy — locked |
| BS1.1 | .NET → Codex | Legacy — locked |
| BS2 (pingpong) | bare-metal → CDX | CDX fixed point: stage 1 CDX = stage 2 CDX |
| BS3 | bare-metal → CDX | CDX fixed point (standalone, from pingpong output) |

BS1 and BS1.1 used the C# reference compiler under `old/src/` to bootstrap
the selfhost. The reference compiler is **permanently retired** — do not
edit, invoke, or rebuild it. The whole `old/` tree (sln, src, tests,
generated-output) remains in the depot as historical record only. All
work goes through the selfhost (`codex/`).

## The Rules

### 1. The acceptance test is the sample battery plus byte-identity

The acceptance test has two parts. Both are required:

1. **Sample battery.** `codex.build/test.ps1` diffs every sample's runtime
   output against a hand-verified `.expected` snapshot, every compile
   failure against a `.failing` CDX code sidecar, and enumerates
   skips against `.skip` reasons. This is the correctness anchor.
2. **Pingpong.** `codex.build/build.ps1` runs three fixed-point checks:
   - **Phase 4 (text round-trip):** SUT compiles source in TEXT mode twice;
     stage1.codex === stage2.codex proves the emitter is a fixed point.
   - **Phase 5 (CDX fixed-point):** SUT compiles source in CDX mode → stage1.cdx;
     stage1 compiles source → stage2.cdx; byte-identical proves the binary is
     a fixed point. (This subsumes the old BS3/bootstrap3.ps1 check.)

Every change that touches codegen must pass both gates before it
is considered done. If either is red, shelve changes, notify Damian,
and re-evaluate.

### 2. Read before you write

Do not modify code you have not read. Do not guess at file contents. Do not assume
structure from names. The self-hosted compiler has subtle invariants — a wrong
assumption will cost hours.

### 3. One thing at a time

Do one thing. Test it. Commit it. Then do the next thing. Do not batch. Do not
"while I'm here." The compiler is ~21,000 lines of Codex across 52 files. A wrong
change in one place surfaces as a silent corruption three pipeline stages later.

### 4. CCE is the internal encoding

Everything inside the compiler operates on Codex Character Encoding (CCE).
Unicode conversion happens ONLY at I/O boundaries. Do not introduce Unicode
assumptions in internal code.

### 5. Never use python, WSL, or Unix tools.

If you need to write a script, use PowerShell (.ps1) or Codex. Do not use WSL,
bash, mtools, dd, or any Unix/Linux tool. Do not introduce dependencies on
anything outside the Windows + QEMU environment. If a capability is missing,
build it in PowerShell or Codex.

### 6. The entry-point identifier is `opening`

A Codex program's entry point is the function named `opening`, not `main`. This is an aesthetic choice — Codex leans on the book metaphor (chapters, cites, foreword, opening). The compiler's entry-point lookup is hardcoded to `opening` in the selfhost (`codex/Emit/X86_64Chapter.codex` emit-start). `main` is free for user code.

### 7. Every review assesses memory and time-complexity risk

This runs on finite hardware with no GC. Every review must include an explicit risk assessment for **heap blow-up** and **time complexity**.

**Inspection is the first test. Testing is the fallback.** Default to reasoning from the code:

1. **Inspect first.** Read the changed lines. Ask: does this add a loop? An accumulator? A new recursion without a fuel cap? If not, inspection alone is sufficient.
2. **Test when genuinely unsure.** Run pingpong before/after and diff `heap hwm` + elapsed time.
3. **Never skip the assessment.** Every CL review must state the memory and time-complexity verdict.

**Red flags.** `buf-read-bytes` in hot paths (8× blowup). Repeated buf→List→buf round-trips. Retaining AST/IR across phases when `heap-save`/`heap-restore` would reset it. Nested loops with unclear pairing. Bare-metal has no GC — every allocation is permanent until the producing function returns.

### Key Tools

| Tool | What |
|------|------|
| `codex.build/build.ps1` | Text round-trip + CDX fixed-point (phases 3-5) via QEMU WHPX |
| `codex.build/test.ps1` | Sample battery (parallel, WHPX, `-Jobs N`) |
| `codex.build/sample-compile-selfhost.ps1` | One-shot: seed CDX + sample source → compiled CDX (or ELF with `-Elf`, UEFI with `-Uefi`) |
| `codex.build/run-for-test.ps1` | One-shot: boot CDX/ELF in QEMU, capture serial output |
| `codex.build/run-with-disk.ps1` | Boot CDX/ELF in QEMU with IDE disk attached |
| `codex.build/build-boot-img.ps1` | Build UEFI-bootable GPT disk image (seed/Codex.img) |
| `codex.build/qemu-config.ps1` | WHPX config, chardev setup, `kernel-irqchip=off`, port allocation |

### Build and Test

```powershell
codex.build/test.ps1                      # Sample battery (selfhost via QEMU WHPX, ~2-5s per sample)
codex.build/test.ps1 -Jobs 4              # Parallel test
codex.build/build.ps1                     # Text round-trip + CDX fixed-point (all gates)
```

## Agent Identity

Working directory: `D:\Projects\NewRepository-XXX`. Use pwd to find the actual XXX value.
You are **XXX** — the last 3 characters of your working directory name. The current roster is **Cam**, **Nib**.
Agent file: `docs/Agents/<your-name>.txt`

### Perforce `.p4config`

On session start, check that a `.p4config` file exists in your working directory root. If it does not, create one:

```
P4PORT=localhost:1666
P4USER=damian
P4CLIENT=BigWhite_Codex_XXX
```

where `XXX` is your agent name (lowercase). This file is already in `.p4ignore`. Without it, `p4` commands fall through to the machine default client and target another agent's workspace.

### Perforce Process

Read `docs/Agents/PerforceProcess.md` before running gates or splitting CLs. The critical rule: **shelve + revert + sync -f before running pingpong/sweep/BS3.** On-disk files are the source of truth for compilation — unshelved edits contaminate gate runs.

## What Not To Do

- Do not add features beyond what is asked
- Do not refactor unrelated code
- Do not add comments, docstrings, or type annotations to code unless a strong argument can be made that it prevents rediscovery
- Do not create abstractions for one-time operations
- Do not introduce Unicode handling inside the compiler
- Do not edit, invoke, or rebuild anything under `old/` (the retired reference compiler, sln, tests, and generated artifacts)
