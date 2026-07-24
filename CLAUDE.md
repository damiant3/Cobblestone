# CLAUDE.md -- Codex Project Instructions

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

## Session Start

**On session start, run `/init`.** This is non-negotiable. The `/init`
skill reads all live docs, sets up `.p4config`, checks Perforce status,
and loads memory. Do not skip it. Do not substitute your own init
sequence. The skill is at `.claude/skills/init/SKILL.md`.

If the user's first message asks you to initialize, run `/init`. If
you are unsure whether init has been done, run `/init`.

### Docs Reference

These are the docs that `/init` reads. Listed here for reference only --
you do not need to read them manually if you ran `/init`.

**Mandatory (read directly -- all docs in root of `docs/`):**
- `docs/CuratorsCatalogue.md`
- `docs/ArchitectsSketchbook.md`
- `docs/DevelopersGuide.md`
- `docs/DevelopersRulebook.md`
- `docs/ExaminersAssay.md`
- `docs/KingsAndCourts.md`
- `docs/OperatorsManual.md`
- `docs/TheShimmeringPortal.md`
- `docs/TinkersToolbox.md`
- `docs/UsersHandbook.md`
- `docs/VisionAndVirtues.md`

**Read in full, directly (not via an agent):**
- `docs/PM/Active/` -- everything in it, recursively. The stories live in
  `docs/PM/Active/Stories/`: seventeen accounts of how agents on this
  project actually failed. They were in `Done/` and went unread, and the
  failures kept repeating. About 180 KB, spent deliberately.

**Via parallel agents:**
- `docs/PM/CurrentPlan.md` -- current plan
- `docs/Agents/PerforceProcess.md` -- shelve/revert/sync protocol
- `docs/Designs/Active/` -- ALL active designs (skip `docs/Designs/Done/`, the archive)
- `docs/PM/Stories/Vision/` -- founding prompts
- `docs/PM/Stories/Vision/` -- founding prompts (read directly, not via agent)

## Document Lifecycle

There is no platform-wide register of open work. `docs/PM/BACKLOG.md`
was deleted 2026-07-23. **Do not recreate it.** Application-domain
registers (`apps/<app>/<app>-backlog.md`,
`codex/<quire>/<quire>-backlog.md`) are unaffected.

`docs/PM/CurrentPlan.md` is the shape and the priority order.
`docs/Designs/Active/` means **live work only**. `docs/Designs/Done/` is
the archive -- shipped and superseded designs folded together, kept but
**not read at init**. `docs/Reference/` is surveys and position docs and
is **not read at init either**. Lifecycle docs use one top-level
`Active/`/`Done/` split with the domain beneath (e.g.
`docs/Designs/Active/Compiler/`, `docs/PM/Done/GitHubUpdates/`); Reference
has no such state. If you find a finished campaign sitting in `Active/`,
move it to `Done/`.

**Verify every "this doc is wrong" finding against the source before
acting on it.** A claim is cheap to check and cheap to get wrong in
either direction.

**Never carry a count forward. Re-measure it.** Test counts, module
counts, line counts, and plug counts in these docs have all been wrong.

## Current State

**The compiler is a hard fixed point of itself on bare metal.** Codex
compiles itself end-to-end on bare metal (codex-vm x86-64, no OS, no
libc), and the output of that self-compile compiled by itself is
byte-identical to itself. No C# anywhere in the chain.

A green battery does not mean there is no work. For whichever
application you are standing in, the work is in that app's own
`*-backlog.md`. Read it before you decide the project is finished.

The canonical artifact is `seed/Codex.cdx` -- a ~2.1 MB
self-sustaining CDX binary, bootable via codex-vm (or QEMU multiboot).
The CDX is the root of trust.

`tools/codex-vm.exe` is a ~6000-line C program (WHP hypervisor) that
emulates: PCI bus (3 devices), xHCI USB 3.x (mass storage + HID
keyboard + UVC camera), Intel HDA audio with host waveOut, Bochs VBE
display, NE2K NIC with NAT + port forwarding, IDE disk (read/write
with flush), HPET, IOAPIC (24 entries), LAPIC (per-core, SIPI for
SMP boot), ACPI/SMBIOS tables, UEFI firmware (ConIn/ConOut, GOP,
Block I/O, Simple File System, memory map, runtime services,
auto-extract PE from GPT images), VGA text, GOP framebuffer at GPA
0xBF000000 (in-RAM, no MMIO trap), host-side GPU triangle rasterizer
(I/O ports 0x400-0x40F: depth buffer, lighting, texture mapping),
PS/2 keyboard + mouse, CMOS RTC, PC speaker. Multi-core via `-smp N`
(1-16 cores, each an independent WHP VP + host thread). Screenshot
capture via `-screenshot`. Build with `tools/build-vm.ps1`. Full
CLI reference and device details in `docs/OperatorsManual.md`.

### Bootstrap History -- 2026-04-24: The cord is cut

All four bootstraps green for the first time, 41 days from project start:

| Bootstrap | Path | Result |
|---|---|---|
| BS1 | .NET → C# | Legacy -- locked |
| BS1.1 | .NET → Codex | Legacy -- locked |
| BS2 (pingpong) | bare-metal → CDX | CDX fixed point: stage 1 CDX = stage 2 CDX |
| BS3 | bare-metal → CDX | CDX fixed point (standalone, from pingpong output) |

BS1 and BS1.1 used the C# reference compiler to bootstrap
the selfhost. The reference compiler is **permanently retired** -- do not
edit, invoke, or rebuild it. The whole `old/` tree remains in the depot
as historical record only.

## The Rules

### 1. The build is the test

Semantic equivalence of text mode, byte-identical text (pingpong), and
byte-identical binary (hard fixed point), plus the BVT. The gate is ONE
command, and these are the only verification commands you run:

```powershell
build/build.ps1                     # Text round-trip + CDX fixed-point + BVT. THE gate.
build/compile.ps1 -Src X -Out Y -Log Z   # Compile one .codex file. -Log is MANDATORY:
                                         # omitting it hangs headless on a parameter prompt
```

Every change that touches codegen must pass the gate before it is done.
If the gate is red, shelve changes, notify Damian, and re-evaluate. To
check one thing, compile and run that one test -- never a sweep.

**The full battery (`build/test.ps1`) is not an agent command.** It is
Damian's tool; the script refuses to run without his approval, and that
refusal is deliberate. There is no category of change -- not codegen,
not forewords, not apps, not seeds -- that earns a battery run on your
own initiative. If you believe your change warrants one, say exactly
that in one sentence and stop; Damian runs it or hands you the command.
Asking is always right. Launching is always wrong.

**Zero failures before copy-up.** Do not copy up to main with any test
failures -- whether the CL carries a seed, source, or both. "Verified"
means: the standing gate is green AND you compiled and ran the specific
tests your change touches. It does NOT mean you ran the battery -- a
change risky enough to want a sweep is a message to Damian, not a
reason to launch one. "Pre-existing" is not an excuse for a red test
you noticed: report it, don't wave it through. Other agents inherit
main through merge-down; a failure you wave through becomes their
debugging detour.

Container formats (ELF, PE, GPT/FAT disk images) are produced by
**plug CDX binaries** in `codex/plugs/`, not by the compiler itself.
The compiler emits CDX or text. Plugs receive IR or CDX over TCP and
produce the final binary format.

### 2. Read before you write

Do not modify code you have not read. Do not guess at file contents. Do
not assume structure from names. The self-hosted compiler has subtle
invariants -- a wrong assumption will cost hours.

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
Do not "while I'm here." The compiler is ~36,300 lines of Codex across
55 files. A wrong change in one place surfaces as a silent corruption
three pipeline stages later.

### 5. CCE is the internal encoding

Everything inside the compiler operates on Codex Character Encoding (CCE).
Unicode conversion happens ONLY at I/O boundaries. Do not introduce Unicode
assumptions in internal code.

### 6. Do not use the Bash tool. PowerShell only.

**Do not use the Bash tool.** It is problematic in this environment.
Use the PowerShell tool for all shell work, and the dedicated tools
(Grep, Glob, Read, Edit, Write) for searching and editing files -- not
`grep`/`cat`/`sed`/`find` shelled out through bash. Need to run Python
or another interpreter? Invoke it from PowerShell.

Use PowerShell (.ps1) or Codex for all normal work. The single
exception is a live GDB debugging session under WSL (trace/probe
workflow documented in OperatorsManual) -- that, and only that, may use
Unix tooling. Do not introduce dependencies on anything outside the
Windows + codex-vm environment. If a capability is missing, build it in
PowerShell or Codex.

Agents keep slipping anyway, so name the reflexes. The harness itself
sometimes suggests Bash for a wait-loop or a one-off command; ignore the
suggestion. The habits that reach for it are muscle memory -- a heredoc
(`<<EOF`), `sleep`, `/tmp`, `rm -rf`. In PowerShell those are a
here-string (`@'...'@`), `Start-Sleep`, the session scratchpad, and
`Remove-Item`. And when the Bash tool's own guardrails block a command --
a `Remove-Item` with a regex-looking path, say -- that is not an obstacle
to route around, it is the signal that you are in the wrong tool. Switch.

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
pairing. Bare-metal has no GC -- every allocation is permanent until the
producing function returns.

### 9. Signing is automatic

Signing is hardcoded and always works. Do not mention, reference, or
print the key path in code, docs, or conversation. If the sign step
fails, fix the build scripts.

### 10. Report the result, not the journey

Damian reads four agents' reports every session. Write only what he does
not already know and would act on. Everything else is noise wearing the
costume of diligence.

**Do not report:** a mistake you made and fixed yourself with nothing
left behind; the steps of a standard process that went as documented;
what you read, considered, or ruled out; a gap he already knows about;
anything marked `Deferred`. A self-corrected detour with no residue is a
memory-file entry, not a status update. He does not need to watch you
discover how Perforce works.

**Do report, always:** what changed and where it landed; a failure that
is still failing; a result that contradicts what a doc or the plan says;
a decision only he can make. **A red gate, a wrong byte shipped, or a
test you skipped is reported every time, in full** -- brevity is never a
reason to soften or omit a real failure. Rule 1's "zero failures before
copy-up" and the standing honesty rule outrank this one.

The test: would he do something differently if he read it? If not, cut
it. One line beats a paragraph; the CL is the record.

This governs the running status too, not only the final report. A stream
of intermediate updates about a side quest, a minor annoyance, or a
"trap" that turns out to be your own unfamiliarity with a tool is the
same noise delivered live. Learning how a tool behaves is you catching up
to the tool, not a finding: it belongs in nobody's status. Do not narrate
it, and do not write a memory file about it either, unless the behavior
is genuinely non-obvious and will cost a future session real time. Most
tool surprises are neither -- they are one-in-many-sessions gotchas that
read as diligence and function as clutter. When in doubt, the durable
operational facts belong in `docs/Agents/PerforceProcess.md` or this
file, once, not restated across four agents' memories.

### 11. The em-dash is banned

Never type an em-dash. Not in docs, not in CL descriptions, not in prose
at column 2, not in a comment, not in a report, not in a reply. The same
goes for the en-dash outside a numeric range.

It is not house style and it never was. It is a model tic: agents arrive
mistrained to like it, and it has been spreading through the tree ever
since one of them started writing docs. Measured 2026-07-17:
`OperatorsManual.md` held 62, `ExaminersAssay.md` 42, and this file 34
(the register held 163 before it was deleted). Every one of them is work
for whoever cleans it up, and blu has had to run a campaign doing
exactly that.

It is not free technically either. An em-dash is a non-ASCII byte, and a
non-ASCII byte is what made source files land as `text` or `utf8` or
binary-by-detection depending on when they were added, which is the trap
CL 8778 exists to close. A Windows-1252 em-dash (byte `0x97`) is what
corrupted two archived docs outright.

Use a comma. Use a colon. Use parentheses. Use a full stop. If a sentence
genuinely needs a dash, `--` is ASCII and it is what the `.codex` prose
already uses. It is not the more expensive choice, which is the first
thing everyone assumes: on disk `--` is `2d 2d`, two bytes, against the
em-dash's three (`e2 80 94`), so the swap makes a file smaller.

Inside the compiler it is not a cost question at all. **The em-dash has
no CCE code point.** It is U+2014, in General Punctuation, and General
Punctuation is not a CCE block at any tier: the eleven Tier 1 blocks are
Latin Extended, Cyrillic, Greek, Arabic, Hebrew, Devanagari, Thai,
Hangul, CJK, Kana, and Mathematical Operators (U+2200..U+227F), and
U+2014 is in none of them. `from-unicode` answers negative one for it,
the same answer it gives a carriage return, and an unmapped code point is
dropped. So an em-dash in `.codex` prose does not cost bytes, it silently
disappears at the I/O boundary. The character earns nothing that ASCII
punctuation does not, and it cannot survive the encoding this project is
built on.

Do not sweep other people's em-dashes as a side quest. Blu owns the
removal campaign. Just stop producing them.

## Agent Identity

Working directory: `D:\Projects\NewRepository-XXX`. Use pwd to find the
actual XXX value. You are **XXX** -- the last 3 characters of your working
directory name.

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
On-disk files are the source of truth for compilation -- unshelved edits
contaminate gate runs.

### Build Coordination (AgentGrid)

You are one of several agents racing to main. **Do not run gates or
submit without holding the AgentGrid build token.** The protocol is in
`docs/Agents/CoordinationProtocol.md` -- read it before your first gate
run. Summary: shelve your CL, write a `build-request` JSON into your
coordination mailbox (path is in the `.agentgrid` file in your
workspace root), wait for the `[AgentGrid coordinator]` GO message in
your terminal, merge down from main first if the grant says so, then
run gates, submit, and write `build-complete` to release the token.

The token covers the gate dance and the submit, nothing else. The
moment your CL needs more code -- a red gate, a fix, a test -- shelve,
release the token, do the work WITHOUT it, and re-request when the CL
is ready again (protocol rule 8). Either you submit and free the
token, or you free the token. There is no third outcome.

If `.agentgrid` does not exist in your workspace root, AgentGrid is not
managing this workspace -- proceed without the token.

## What Not To Do

- Do not add features beyond what is asked
- Do not refactor unrelated code
- Do not add comments, docstrings, or type annotations to code unless a strong argument can be made that it prevents rediscovery
- Do not create abstractions for one-time operations
- Do not introduce Unicode handling inside the compiler
- Do not edit, invoke, or rebuild anything under `old/` (the retired reference compiler, sln, tests, and generated artifacts)
