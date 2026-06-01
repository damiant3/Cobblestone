# CLAUDE.md — Codex Project Instructions

## What This Is

Codex is a new programming language, self-sustaining compiler, tools, operating system, repository protocol, trust lattice, encoding, and more. We take the best of type theory, language design, aesthetics, security research, and actual practice. We leave everything else behind. If we didn't build it, we don't trust it. Codex is a new computational substrate intended to be impervious to all currently known attack vectors by-design.

The project was started 3/14/2026.

### The Founding Vision (docs/PM/Stories/Vision/NewRepository.txt)

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

On session start, read ALL live docs using parallel agents. This costs
~20K tokens (~2% of context) and eliminates an entire class of mistakes
where an agent doesn't know about a prior decision, a known condition,
or a design that's relevant to the current task. Skip every
`docs/Designs/*/Done/`, the `docs/Designs/History/` archive, and
`docs/PM/Done/` — those are historical and recoverable from
`p4 filelog` if needed.

### Mandatory Reading (read directly, not via agent)

- `docs/VisionAndVirtues.md` — founding vision, non-negotiables, engineering virtues
- `docs/DevelopersGuide.md` — language syntax, types, CPL, seed rebuild procedure
- `docs/DevelopersRulebook.md` — foreword quire catalog, library rules
- `docs/OperatorsManual.md` — build process, test harness, VM setup, debugging
- `docs/ArchitectsSketchbook.md` — memory layout, registers, allocators, phase maps

### Also Read (via parallel agents at session start)

- `docs/PM/CurrentPlan.md` — current plan
- `docs/PM/BACKLOG.md` — outstanding work items
- `docs/Agents/PerforceProcess.md` — shelve/revert/sync protocol
- `docs/Designs/*/Active/` — ALL active designs, one section per concern: `Compiler/`, `Language/`, `Memory/`, `OS/`, `Hardware/`, `Backends/`, `Build/`, `Test/`, `Tools/`, `Features/`, `Projects/`, and `Apps/<project>/` (e.g. `Apps/CodexMagic/`, `Apps/Explorer/`). Each section has its own `Active/` + `Done/`; historical piles live under `docs/Designs/History/`
- `docs/PM/Stories/Vision/` — founding prompts
- `docs/Test/` — known conditions, crash investigations
- `docs/Reference/` — UEFI specs, AMI Aptio, paper index
- `docs/ReadingNotes/` — observations from external projects (NVlabs/Sana, etc.)

## Current State

**The compiler is a hard fixed point of itself on bare metal.** Codex
compiles itself end-to-end on bare metal (codex-vm x86-64, no OS, no
libc), and the output of that self-compile compiled by itself is
byte-identical to itself. No C# anywhere in the chain.

The canonical artifact is `seed/Codex.cdx` — a ~2.1 MB
self-sustaining CDX binary, bootable via codex-vm (or QEMU multiboot).
The CDX is the root of trust.

`tools/codex-vm.exe` is a ~4500-line C program (WHP hypervisor) that
emulates: PCI bus, xHCI USB (mass storage + HID keyboard + UVC camera),
Intel HDA audio with host waveOut, Bochs VBE display, NE2K NIC with
NAT, IDE disk, HPET, IOAPIC, ACPI/SMBIOS tables, UEFI firmware
(LocateProtocol, Block I/O, memory map, auto-extract PE from GPT
images), VGA text, GOP framebuffer, PS/2, CMOS RTC, PC speaker.
Build with `tools/build-vm.ps1`.

### Bootstrap History — 2026-04-24: The cord is cut

All four bootstraps green for the first time, 41 days from project start:

| Bootstrap | Path | Result |
|---|---|---|
| BS1 | .NET → C# | Legacy — locked |
| BS1.1 | .NET → Codex | Legacy — locked |
| BS2 (pingpong) | bare-metal → CDX | CDX fixed point: stage 1 CDX = stage 2 CDX |
| BS3 | bare-metal → CDX | CDX fixed point (standalone, from pingpong output) |

BS1 and BS1.1 used the C# reference compiler to bootstrap
the selfhost. The reference compiler is **permanently retired** — do not
edit, invoke, or rebuild it. The whole `old/` tree remains in the depot
as historical record only.

## The Rules

### 1. The build is the test

Semantic equivalence of text mode, byte-identical text (pingpong), and
byte-identical binary (hard fixed point), plus the smoke battery. Every
change that touches codegen must pass all gates before it is done. If
any gate is red, shelve changes, notify Damian, and re-evaluate.

```powershell
build/test.ps1                      # Sample battery (~2-5s per sample)
build/test.ps1 -Jobs 4              # Parallel test
build/build.ps1                     # Text round-trip + CDX fixed-point (all gates)
build/compile.ps1 -Src X -Out Y     # Compile a single .codex file (was test-compile.ps1)
```

Container formats (ELF, PE, GPT/FAT disk images) are produced by
**plug CDX binaries** in `codex/plugs/`, not by the compiler itself.
The compiler emits CDX or text. Plugs receive IR or CDX over TCP and
produce the final binary format.

### 2. Read before you write

Do not modify code you have not read. Do not guess at file contents. Do
not assume structure from names. The self-hosted compiler has subtle
invariants — a wrong assumption will cost hours.

### 3. Read before you build

A build takes 10 minutes. A read takes 30 seconds. When investigating
a bug, read the code at the crash site before running a build to test
a hypothesis. When a function is misbehaving, read it. When a type is
wrong, read the type checker. Do not speculate about what code does and
then spend a rebuild cycle confirming the speculation was wrong. The
code is right there. Read it first, form a theory from what it actually
says, then test. Three reads and one build beats one read and three
builds every time.

### 4. One thing at a time

Do one thing. Test it. Commit it. Then do the next thing. Do not batch.
Do not "while I'm here." The compiler is ~29,000 lines of Codex across
53 files. A wrong change in one place surfaces as a silent corruption
three pipeline stages later.

### 5. CCE is the internal encoding

Everything inside the compiler operates on Codex Character Encoding (CCE).
Unicode conversion happens ONLY at I/O boundaries. Do not introduce Unicode
assumptions in internal code.

### 6. WSL and Unix tools only for GDB

Use PowerShell (.ps1) or Codex for all normal work. WSL, bash, and Unix
tools are permitted only for GDB debugging sessions (trace/probe workflow
documented in OperatorsManual). Do not introduce dependencies on anything
outside the Windows + codex-vm environment. If a capability is missing,
build it in PowerShell or Codex.

### 7. The entry-point identifier is `opening`

A Codex program's entry point is the function named `opening`, not `main`.

### 8. Every review assesses memory and time-complexity risk

This runs on finite hardware with no GC. Every review must include an
explicit risk assessment for **heap blow-up** and **time complexity**.

**Inspection is the first test. Testing is the fallback.** Default to
reasoning from the code:

1. **Inspect first.** Read the changed lines. Does this add a loop? An
   accumulator? A new recursion without a fuel cap? If not, inspection
   alone is sufficient.
2. **Test when genuinely unsure.** Run pingpong before/after and diff
   `heap hwm` + elapsed time.
3. **Never skip the assessment.** Every CL review must state the memory
   and time-complexity verdict.

**Red flags.** `buf-read-bytes` in hot paths (8x blowup). Repeated
buf-to-List-to-buf round-trips. Retaining AST/IR across phases when
`heap-save`/`heap-restore` would reset it. Nested loops with unclear
pairing. Bare-metal has no GC — every allocation is permanent until the
producing function returns.

### 9. Signing is automatic

Signing is hardcoded and always works. Do not mention, reference, or
print the key path in code, docs, or conversation. If the sign step
fails, fix the build scripts.

## Agent Identity

Working directory: `D:\Projects\NewRepository-XXX`. Use pwd to find the
actual XXX value. You are **XXX** — the last 3 characters of your working
directory name.
Agent file: `docs/Agents/<your-name>.txt`

### Perforce `.p4config`

On session start, check that a `.p4config` file exists in your working
directory root. If it does not, create one:

```
P4PORT=localhost:1666
P4USER=damian
P4CLIENT=BigWhite_Codex_XXX
```

where `XXX` is your agent name (lowercase). This file is already in
`.p4ignore`. Without it, `p4` commands fall through to the machine
default client and target another agent's workspace.

### Perforce Process

Read `docs/Agents/PerforceProcess.md` before running ANY Perforce
operation beyond `p4 edit` and `p4 submit`. Do not guess at commands.
Do not flail. The doc has exact commands for every workflow: gates,
copy-up, merge-down, seed rebuild. Read it, copy the command, run it.

The critical rule: **shelve, revert, sync -f, unshelve, then
visually inspect the CL and opened files before running any build.**
On-disk files are the source of truth for compilation — unshelved edits
contaminate gate runs.

## What Not To Do

- Do not add features beyond what is asked
- Do not refactor unrelated code
- Do not add comments, docstrings, or type annotations to code unless a strong argument can be made that it prevents rediscovery
- Do not create abstractions for one-time operations
- Do not introduce Unicode handling inside the compiler
- Do not edit, invoke, or rebuild anything under `old/` (the retired reference compiler, sln, tests, and generated artifacts)
