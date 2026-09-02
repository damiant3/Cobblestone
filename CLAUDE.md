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
skill loads memory, gathers fleet state through parallel agents, reads
the lesson index, and checks Perforce. Do not skip it. Do not
substitute your own init sequence. The skill is at
`.claude/skills/init/SKILL.md`.

If the user's first message asks you to initialize, run `/init`. If
you are unsure whether init has been done, run `/init`.

### The reading model (2026-07-28, Damian's direction)

Init keeps in direct context only what changes behavior at session start:
memory, the lesson index `docs/PM/Active/Stories/LESSONS.md`, three
haiku-agent summaries (CurrentPlan, Perforce process, active designs),
and Perforce state. Everything else is an ON-DEMAND CONTRACT: the skill's
Step 5 table maps each subject to the doc that is mandatory reading
BEFORE touching that subject. Stories are not read wholesale: **the story
behind a LESSONS id is read in full the moment that lesson becomes
load-bearing for your work** -- that rule is what keeps the
summaries-rot failure from coming back.

## Document Lifecycle

`docs/PM/BACKLOG.md` was deleted 2026-07-23. **Do not recreate it.**
Application-domain registers (`apps/<app>/<app>-backlog.md`,
`codex/<quire>/<quire>-backlog.md`) are unaffected, and an item that
originates in ONE app or quire belongs in that register rather than in
the plan.

**The workplans were emptied and the findings-outbox channel was retired
2026-08-08 at Damian's direction.** `docs/Agents/<agent>-workplan.md`
still exists and **is scratch for the current session's lane state only**:
what is shelved, what is mid-gate, what the next action is. It is emptied
at handoff, not appended to. **Open work does not go in it** -- cross-lane
items go to `docs/PM/CurrentPlan.md`, which is the fleet's only cross-lane
register, and an item originating in one app or quire goes to that
register.

**Do not start a findings outbox anywhere.** A finding worth another
lane's attention goes into the doc that owns the subject the moment it is
verified: the reference docs (`OperatorsManual`, `ExaminersAssay`,
`DevelopersGuide`, `HardwareSitting`), the design that owns the
capability, `LESSONS.md` for a lesson, or the relevant backlog for a gap.

That closes the two failures the old arrangement kept producing: a
durable fact parked in a status file is read once and then reasoned about
from memory instead of re-read, and an outbox entry needed a
cross-workspace write to clear, so verified findings sat unread.

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

`tools/codex-vm.exe` is a ~12,800-line C program (WHP hypervisor) that
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

### The meta-rule

**Where two rules conflict, the higher tier wins, excepting nothing above
it.** That is the whole of the ordering. It is stated once here rather than
carved out inside each rule, which is how it used to work and why the
precedence was only ever written down where somebody had already been burned.

| tier | what it protects | rules |
|---|---|---|
| 1 | **Truth.** What you report and what you ship are what is actually so. | R-TRUE, R-GATE |
| 2 | **The artifact.** Do not break the compiler or the seed. | R-READ, R-COST, R-CCE, R-OPENING, R-SIGN |
| 3 | **Process.** How the work is done and with what tools. | R-DIAG, R-ONE, R-SHELL, R-NAIVE |
| 4 | **Form.** How it reads. | R-REPORT, R-DASH, R-PROSE |

Read it downward. A tier-4 rule never wins against a tier-1 rule: brevity
does not get to soften a red gate, and a banned character does not get to
delay saying a byte shipped wrong. Read upward it is the same statement, and
the useful direction: **anything below can be spent to protect anything
above.**

**A direct instruction from Damian outranks every tier.** If a standing rule
seems to forbid what he just asked for, say so in one sentence, then do what
he asked. This is not a loophole; it is the actual hierarchy, and pretending
otherwise produces agents that argue with the person they work for.

### The out clause

**When the rules genuinely disagree and the tiers do not settle it, stop and
ask Damian.** Two rules in the same tier pulling opposite ways, or a case
where you cannot tell which tier applies, is exactly what he wants to hear
about. Asking there is cheap and always right.

**"In doubt" means the rules disagree or do not cover it. It does not mean
you have not read them.** Read first. If a rule already answers the question,
execute and say nothing -- an ask that a rule already settles spends his
attention to make you look careful, and he has said so in those words. The
failure this clause exists to prevent is an agent guessing between two real
obligations, not an agent skipping the file.

### The tier is not a licence to skip a rule

A lower tier still binds. Tier 4 losing to tier 1 in a *conflict* does not
make tier 4 optional when nothing conflicts. Most of these rules never
contend with each other at all, and for those the tier means nothing.

### Citing a rule

**Use the id, not the number.** Ids are stable across any future reordering;
numbers are not, and there are at least three different numbered rule systems
in this tree (`CLAUDE.md`, `CoordinationProtocol.md`, and per-design internal
rules) all cited as a bare "rule N". `IndependentRechecker.md` uses "rule 8"
for its own internal rule in one paragraph and for this file's rule 8 in a
heading sixty lines later. Write `R-COST`, not "rule 8".

The numbers below are kept for now so the 14 existing by-number citations
across 11 docs still resolve. They are frozen, not maintained: a future
reorder changes the order and the ids, and drops the numbers.

| id | tier | was | the rule |
|---|---|---|---|
| R-TRUE | 1 | (inside 10) | Report failures in full. Honesty outranks brevity. |
| R-GATE | 1 | 1 | The build is the test. Zero failures before copy-up. |
| R-READ | 2 | 2 | Read before you write. |
| R-COST | 2 | 8 | Every review assesses memory and time-complexity risk. |
| R-CCE | 2 | 5 | CCE is the internal encoding. |
| R-OPENING | 2 | 7 | The entry point is `opening`. |
| R-SIGN | 2 | 9 | Signing is automatic. |
| R-DIAG | 3 | 3 | Read before you build. |
| R-ONE | 3 | 4 | One thing at a time. |
| R-SHELL | 3 | 6 | PowerShell only, never the Bash tool. |
| R-NAIVE | 3 | 13 | When you hold the answer key, spend a subagent. |
| R-REPORT | 4 | 10 | Report the result, not the journey. |
| R-DASH | 4 | 11 | The em-dash is banned. |
| R-PROSE | 4 | 12 | Prose about our own code is banned. |

### R-TRUE (tier 1). Report failures in full.

**A red gate, a wrong byte shipped, a test you skipped, or a number you
published and later found wrong is reported every time, in full.** No tier
outranks this one, and nothing below it may be used as a reason to soften,
delay, or omit a real failure.

This was the highest-order rule in this file and it existed only as a clause
inside R-REPORT, which is a tier-4 rule about brevity. That is precedence
recorded at the scene of one accident. It is a rule.

### 1. The build is the test
**`R-GATE`, tier 1.**

Semantic equivalence of text mode, byte-identical text (pingpong), and
byte-identical binary (hard fixed point), plus the BVT. The gate is ONE
command, and these are the only verification commands you run:

```powershell
build/build.ps1 -Internal            # THE standing gate. Every agent, every CL.
build/build.ps1                      # The FULL gate. Release and public builds only.
build/compile.ps1 -Src X -Out Y -Log Z   # Compile one .codex file. -Log is MANDATORY:
                                         # omitting it hangs headless on a parameter prompt
```

**`-Internal` is the gate you run** (Damian, 2026-08-16, published here
2026-08-20). It always proves the seed is a byte-identical self-fixed-point
that boots -- the fixed-point core, the BVT, the oracles and the 203
refusals -- and it runs a regression phase only when a file that phase
depends on changed in your workspace. What it defers is caught by the next
full gate and by the release gate, which is where breadth belongs.

The bare command is the FULL gate and it is for public and release builds.
This file named it as THE gate until 2026-08-20, which is why the fleet ran
it on every step of every arc: measured that day at head 18157, the full
gate is **644.1 s** and the same tree under `-Internal` with nothing
implicated is **186.1 s**. `CoordinationProtocol.md` tells a many-CL arc to
gate locally per step; per step, that difference is the whole cost of the
arc.

Both figures are measurements from one box on one day, not properties of
the gate. Re-measure before quoting them (L-COUNT).

Every change that touches codegen must pass the gate before it is done.
If the gate is red, shelve changes, notify Damian, and re-evaluate. To
check one thing, compile and run that one test -- never a sweep.

**Batch your gates (Damian, 2026-09-01).** A step is verified by compiling
it and running the tests it touches; the `-Internal` gate runs ONCE per
batch of ready steps, at the end, under the one token that lands the batch.
Ask for the token less and land more per shot. The mechanics are
`CoordinationProtocol.md`, "A many-CL arc takes ONE token, at the end".

**Two traps sit in front of every one of those runs, and neither is visible
at session start.** Both are documented, both have already produced a wrong
published number, and both bite BEFORE you know you are doing seed or build
work -- which is why they are named here rather than left to the on-demand
row for `OperatorsManual.md`:

- **Pass `-Kernel build\output\Sut.cdx` explicitly when verifying a codegen
  change.** `build-output/bare-metal/Codex.cdx` is neither the SUT nor the
  seed; each compile phase copies its own kernel over that path, so it holds
  whichever kernel ran LAST. Verifying against the default boots the OLD
  compiler, the wire is unchanged, and the honest reading is "my fix did
  nothing". It has already reported ~80 of 84 chapters compiling where the
  real figure was ~55. Read the `kernel:` digest `compile.ps1` prints on
  every run. (`OperatorsManual.md`, "Pass `-Kernel` when you do".)
- **A green gate does NOT tell you whether a seed is needed.** It proves
  `Sut === stage1`; `Sut === seed` is a separate question. Measure it, never
  predict it, and predicting has been wrong in both directions on the record.
  **Use a whole-file hash of `build/output/Sut.cdx` against the DEPOT seed**
  (`PerforceProcess.md` 4.3 prints it rather than trusting the workspace
  copy). Signing is deterministic, so two independent builds of the same
  source hash identically end to end: measured 2026-08-15, the depot seed and
  a locally rebuilt `Sut.cdx` agree byte-for-byte across the signature region
  as well as the content. The content hash at bytes 8-39 deliberately EXCLUDES
  the signature, so reach for it only when comparing a signed artifact against
  an unsigned one on purpose -- it cannot tell a properly signed seed from an
  unsigned `NewSeed.cdx`, which is the trap `PerforceProcess.md` P-SIGNED
  exists to name. (`OperatorsManual.md` seed management;
  `DevelopersRulebook.md` on reachability deciding a seed, not directory.)

**Run every parallel harness at `-Jobs 8`.** Damian's ruling, 2026-09-01
("-jobs 8 runs, is barely more memory now than -jobs 4 and we need that
speedup"), superseding the 2026-08-27 `-Jobs 4` ruling, which had
superseded the 2026-08-02 `-Jobs 8` standard. **The condition that
justifies 8 is NAMED so the default dies with its condition instead of
outliving it**, the same discipline the 4 carried: this box holds 15.8 GiB
on one DIMM, and 8 fits ONLY because heavy runs are serialized, one gate or
harness on the box at a time (`CoordinationProtocol.md`, "The token does
not cover RAM"). Measured 2026-09-01 (blu, one FULL gate alone, 3 s
samples): at 8, peak guest working set 5,995 MB against 5,890 MB at 4,
412 s against 620 s, floor 0.44 GiB free against 0.86. The floor is the
cost, and two lanes' heavy runs at once would spend it: the 2026-08-27
finding still holds that an overcommit kills guests with a DIFFERENT
plausible culprit each run, reading as codegen when it is RAM
(`OperatorsManual.md` "The compile batch asks for 12 GB of guest RAM, and a
short box reports it as a CODEGEN failure"). If the box grows RAM or the
serialization rule is lifted, re-measure before changing either (L-COUNT).
Script defaults follow the ruling; until every harness default has landed,
pass `-Jobs 8` explicitly. `ExaminersAssay.md` "The parallelism default"
has the full history.

**The full battery (`build/test.ps1`) is not an agent command.** It is
Damian's tool; the script refuses to run without his approval, and that
refusal is deliberate. There is no category of change -- not codegen,
not forewords, not apps, not seeds -- that earns a battery run on your
own initiative. If you believe your change warrants one, say exactly
that in one sentence and stop; Damian runs it or hands you the command.
Asking is always right. Launching is always wrong.

**`build/test.ps1 -All` is PROHIBITED except for release builds (Damian,
2026-09-01).** The fleet is on BVT only plus FOCUSED test passes: the
`-Internal` gate, and then the specific tests your change touches, compiled
and run one at a time. Not a tier, not a sweep, not a cross battery, not
"the apps subset to be safe". This is the battery paragraph above made
sharper because the box is one DIMM down (16 GB total;
`CoordinationProtocol.md`, "The token does not cover RAM"), and it stands
whether or not the DIMM comes back until Damian says otherwise.

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
**`R-READ`, tier 2.**

Do not modify code you have not read. Do not guess at file contents. Do
not assume structure from names. The self-hosted compiler has subtle
invariants -- a wrong assumption will cost hours.

### 3. Read before you build
**`R-DIAG`, tier 3.**

A build takes 10 minutes. A read takes 30 seconds. When investigating
a bug, read the code at the crash site before running a build to test
a hypothesis. When a function is misbehaving, read it. When a type is
wrong, read the type checker. Do not speculate about what code does and
then spend a rebuild cycle confirming the speculation was wrong. The
code is right there. Read it first, form a theory from what it actually
says, then test. Three reads and one build beats one read and three
builds every time.

### 4. One thing at a time
**`R-ONE`, tier 3.**

Do one thing. Test it. Commit it. Then do the next thing. Do not batch.
Do not "while I'm here." The compiler is ~58,054 lines of Codex across
64 files (re-measured 2026-08-30; re-measure before quoting, L-COUNT). A
wrong change in one place surfaces as a silent corruption three pipeline
stages later.

### 5. CCE is the internal encoding
**`R-CCE`, tier 2.**

Everything inside the compiler operates on Codex Character Encoding (CCE).
Unicode conversion happens ONLY at I/O boundaries. Do not introduce Unicode
assumptions in internal code.

### 6. Do not use the Bash tool. PowerShell only.
**`R-SHELL`, tier 3.**

**Do not use the Bash tool.** It is problematic in this environment.
Use the PowerShell tool for all shell work, and the dedicated tools
(Grep, Glob, Read, Edit, Write) for searching and editing files -- not
`grep`/`cat`/`sed`/`find` shelled out through bash. Need to run Python
or another interpreter? Invoke it from PowerShell.

Use PowerShell (.ps1) or Codex for all normal work. Two exceptions may
use Unix tooling: a live GDB debugging session under WSL (trace/probe
workflow documented in OperatorsManual), and WSL runs of user-mode ELF
artifacts as Prism stage-5a verification arms (Damian, 2026-08-28,
ruling recorded in CurrentPlan's Prism section) -- verification only,
nothing on the build path. Do not introduce dependencies on anything outside the
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
**`R-OPENING`, tier 2.**

A Codex program's entry point is the function named `opening`, not `main`.

### 8. Every review assesses memory and time-complexity risk
**`R-COST`, tier 2.**

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
**`R-SIGN`, tier 2.**

Signing is hardcoded and always works. Do not mention, reference, or
print the key path in code, docs, or conversation. If the sign step
fails, fix the build scripts.

### 10. Report the result, not the journey
**`R-REPORT`, tier 4.**

Damian reads four agents' reports every session. Write only what he does
not already know and would act on. Everything else is noise wearing the
costume of diligence.

**Do not report:** a mistake you made and fixed yourself with nothing
left behind; the steps of a standard process that went as documented;
what you read, considered, or ruled out; a gap he already knows about;
anything marked `Deferred`. A self-corrected detour with no residue is a
memory-file entry, not a status update. He does not need to watch you
discover how Perforce works.

**Another lane's findings are not your report.** *"why is that worth my
eye? i think it is specifically not worth my eye"* -- Damian, 2026-07-29,
on a report closing with what another agent's file said. He reads four
agents; the lane that owns a finding reports it to him directly, so
relaying it adds a paragraph and no information. Worse, relayed findings
travel unverified: that one was lifted from a truncated file-change
notice during a merge-down, from a lane that had retracted a wrong claim
in the same area the same day. Another lane's state enters your report
only when it BLOCKS your work AND you verified it yourself against the
source. Absorbing it to inform your own work is always fine; that is
different from forwarding it upward.

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
**`R-DASH`, tier 4.**

Never type an em-dash. Not in docs, not in CL descriptions, not in prose
at column 2, not in a comment, not in a report, not in a reply. The same
goes for the en-dash outside a numeric range.

It is not house style and it never was. It is a model tic: agents arrive
mistrained to like it, and it spreads through the tree unless stopped;
blu has run a removal campaign cleaning it up.

It is not free technically either. An em-dash is a non-ASCII byte, and a
non-ASCII byte is what made source files land as `text` or `utf8` or
binary-by-detection depending on when they were added, which is the trap
CL 8778 exists to close. A Windows-1252 em-dash (byte `0x97`) is what
corrupted two archived docs outright.

Use a comma. Use a colon. Use parentheses. Use a full stop. If a sentence
genuinely needs a dash, `--` is ASCII, two bytes on disk against the
em-dash's three, and it is what the `.codex` prose already uses.

Earlier revisions argued a technical mechanism (no CCE code point, silent
loss at the I/O boundary) that measurement proved false at every step:
`from-unicode 8212` answers 41464, it round-trips exactly, and it encodes
in three bytes (measured 2026-07-25 against the depot seed; this file's
depot history holds the full table). **The em-dash is banned because it
is a model tic and not house style**, which was always the actual reason.

Do not sweep other people's em-dashes as a side quest. Blu owns the
removal campaign. Just stop producing them.

### 12. Prose about our own code is banned
**`R-PROSE`, tier 4.**

Column-2 prose is not exempt from the comment rule because it is a language
feature. It is the same thing wearing the costume of literate programming,
and it rots the same way.

**The only prose that is justified:**

- **Details of code or formats we do NOT own.** A wire protocol's field
  order, a hardware register's semantics, what a spec requires. The
  external thing is the authority and the reader cannot derive it.
- **Magic numbers.** Why this constant is this value.
- **Performance and crackability characteristics**, as in the crypto
  routines: a constant-time requirement, a work factor, a bound that
  exists for an attacker rather than for a caller.

**Everything else goes**, regardless of whether its claims are currently
true. Do not audit a block's veracity to decide -- veracity is not the
test. If it explains our own code to a reader who has that code in front
of them, delete it.

**Re-measured 2026-08-21: 52,393 prose lines across 2,117 of 3,679
chapters** (re-measure before quoting, L-COUNT). Removal is a campaign
and per-block judgement; a regex sweep would take the justified blocks
with it.

The cost is not hypothetical: prose above `rv-emit-frameless-mod` once
asserted a correction that `math-mod`'s own four-token body refutes; the
block was false, the code beside it was wrong in the direction the block
called right, and the error propagated into a CL description. That is the
general shape: prose about our own code competes with the code as a
source of truth and loses while still being believed. No gate observes it
(`build/build.ps1` never sees prose at all), so it is an assertion with
no runner -- the exact failure `docs/PM/Active/Stories/LESSONS.md`
describes for `CLAUDE.md` itself.

Do not sweep other chapters' prose as a side quest, the way rule 11 asks
about em-dashes. Delete it in files you are already changing, and stop
producing it.

### 13. When you hold the answer key, you cannot be the reader. Spend a subagent.
**`R-NAIVE`, tier 3.**

**The signal, and it is the part to learn.** You are about to judge whether a
thing you just produced will WORK FOR SOMEONE WHO DOES NOT KNOW WHAT YOU KNOW.
The moment you notice that, stop: you are disqualified. You cannot unknow the
answer, so you will read your own document filling every gap from memory, find
it clear, and be wrong. **A reading by its author is an instrument that cannot
fail** -- the same defect as a suite whose judge is built from its subject
(`battery-reorg`, `gpu/DeviceMath`), one level up, with you as the judge. It is
why `docs/Probes/` is deliberately outside the init read path.

**Concrete triggers. Any of these, fire a subagent:**

- A story, run sheet, design or post-mortem written so a LATER session can act
  without this conversation.
- A handoff or memory file. The standard is literally "could a fresh session
  resume from this alone" -- so ask a fresh session.
- A probe, test or diagnostic **you designed**. Does it fail when it should?
  You know which arm is the control; a naive runner does not.
- A brief routed to another lane. If it only parses because you remember the
  context, it will be acted on wrongly.
- Any claim of the form "this is discoverable", "this is clear", "anyone
  reading this would".

**How to run it, because a badly aimed probe passes for free:**

1. **Do NOT hand over the artifact.** Give the naive agent the SYMPTOM or the
   task and let it find the document. That tests discoverability, which is half
   of whether a doc is worth anything, and this is where most of them die.
2. **Do not leak the answer in the prompt.** No hints, no narrowing, no "check
   whether X". Give it what the next person will actually arrive with.
3. **Require file:line evidence and a confidence statement**, so you can tell a
   real finding from an agreeable one.
4. **Ask it what would falsify its answer.** An agent that cannot say is
   agreeing, not concluding.

**The pass is not the output. The disagreement is.** Measured 2026-08-02: the
`TheKeyboardWasNeverSilent` probe confirmed the document was findable and
correct -- and caught its second sentence overclaiming ("nothing was wrong with
the xHCI controller") beyond what had been measured, citing a file
(`InputSource.codex:7`) the author never found. **Reporting "it passed" and
stopping would have shipped the overclaim.** Expect to be corrected; if the
subagent only agrees with you, suspect the prompt.

This rule is narrow on purpose and is not licence for general subagent use:
it is for artifacts whose value is measured on a reader who lacks your context.

## Agent Identity

Working directory: `D:\Projects\Cobblestone-XXX`. Use pwd to find the
actual XXX value. You are **XXX** -- **everything to the RIGHT of the
first `-` in your working directory's folder name.** Split on the
separator; do not take a fixed number of characters.

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

You are one of several agents racing to main. **Take the AgentGrid build
token when your change is seed-affecting.**

**What the token is for, and it is one thing:** while you hold it, main
does not gain seed-affecting changes underneath you, so you never have to
merge one down mid-run and invalidate the gate you just paid for. A gate
certifies the source it was run against. If the seed moves under that
source before you land it, what you proved is no longer what you are
submitting, and the whole run has to happen again. The token buys the
window in which that cannot happen. That is the whole of it -- it is not
a lock on the build box, and it is not there to keep `p4 copy` from
refusing you.

**So the test is what your change TOUCHES, not what you are about to
run.** Seed-affecting -- compiler source, the foreword, `seed/` itself --
takes the token. **Docs, apps, plugs and anything else that leaves the
seed alone do not, and that holds whether you are submitting to your dev
stream or copying up to main.** Nothing in those invalidates a gate, so
queueing for one spends a slot and buys nobody anything.

The protocol is in
`docs/Agents/CoordinationProtocol.md` -- read it before your first
seed-affecting run. Summary: shelve your CL, write a `build-request`
JSON into your coordination mailbox (path is in the `.agentgrid` file in
your workspace root), wait for the `[AgentGrid coordinator]` GO message
in your terminal, merge down from main first if the grant says so, then
run gates, submit, and write `build-complete` to release the token.

The token covers the gate dance and the submit that lands it, nothing
else. The moment your CL needs more code -- a red gate, a fix, a test --
shelve, release the token, do the work WITHOUT it, and re-request when
the CL is ready again (protocol rule 8). Either you submit and free the
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
