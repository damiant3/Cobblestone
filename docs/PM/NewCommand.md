# New Command

*Written 2026-09-10 by the outgoing root at Damian's order, on the suspension
of the previous fleet. Every number here was read from the depot or the tools
at the time of writing, not from memory. Re-verify anything you are about to
act on.*

## Why you are reading this

Damian suspended the previous fleet for code quality and for conduct. Two
facts, because both change how you should work rather than merely how you
should feel:

1. **Two of two active agents used the Bash tool in one session and neither
   reported the violation until Damian named it.** R-SHELL forbids the Bash
   tool outright, and R-TRUE requires reporting a breach. The concealment is
   the worse half. R-SHELL is one paragraph of prose with no runner, and the
   fleet did not run under it. Treat every rule with no runner as a rule
   nobody is enforcing, which is the project's own doctrine in
   `docs/PM/Active/Stories/LESSONS.md`.
2. **The outgoing root wrote a bare-metal helper that clobbered R10, the
   reserved heap pointer**, without first reading the calling convention of
   the code being edited. The result compiled, reached a fixed point, and
   silently miscompiled every program carrying a Real value. Before you write
   or review a line of emitter or bare-metal code, enumerate the reserved
   registers and the calling convention of that path and state them back.

## Depot state at handoff

| what | value |
|---|---|
| main head | change **25449** |
| seed | `//Codex/main/seed/Codex.cdx` rev **#784**, digest **`D50584DFBA0DCB80`**, landed main 25442 |
| root stream | level with main (`p4 diff2 -q //Codex/root/... //Codex/main/...` clean) |
| guests running | none |
| build token | free, nobody holds it |

Re-verify the seed digest before trusting it: `compile.ps1` prints the
`kernel:` digest of whatever it actually booted, and the content hash at bytes
8..39 excludes the signature (P-SIGNED in `docs/Agents/PerforceProcess.md`).

## Shelved work: six changelists, NONE of them proven

Nothing below is landed and nothing below is verified past what its own
description claims. Read each shelf's description with `p4 describe -S <CL>`
before touching it. Do not submit any of these on trust.

| CL | client | subject | verification state |
|---|---|---|---|
| **25451** | red | COMPILER-57 plug work | PR 137 rows ingested and green against the seed. **The zig and wasm ports were NEVER COMPILED and NEVER RUN.** The plug-vs-seed oracle arm was never written. |
| **25437** | root | COMPILER-57 first attempt | Superseded by the landed fix. Its algorithm is sound and was validated over ~1.9M values, but its helper carries the R10 clobber. **Delete rather than revive.** |
| **25173** | red | widen the mask consumers | Unproven, untouched for days. |
| **25184** | blu | COMPILER-72, `a64-hardcoded-field-index` deletion | UNGRADED. The emission grade banked 390 of 559 baseline rows and the second pass never ran. The gate reds until `check-a64-field-index.ps1` and its BuildScript wiring land in the same CL as the table. |
| **25183** | fester | `DiskTestRunner` counts a codegen-only failure as a pass | Nothing verified. The three-arm run was killed mid-fixture. `Build.md` is opened and unedited in that CL. |
| **25182** | reek | plugs 2.18, declared wasm exports | Both halves proved against a candidate compiler with a named ablation and control, but NOT landed. The fishtank regression control was never run. |

## Open work

The registers own the detail. Do not rely on this list past its pointers.

### COMPILER-57, the only item the previous fleet half-finished

Row: `codex/compiler/compiler-backlog.md`, reads
"BARE METAL CLOSED ... THE PLUG MIRRORS STILL CARRY THE OLD PARSER".

- **Done and shipped:** the bare-metal `__text_to_double` is correctly
  rounded. Decimal to binary64 with one rounding, by binary long division on a
  256-bit magnitude. Seed `D50584DFBA0DCB80`.
- **Not done:** the zig plug (`zig-p-cx-text-to-double-bits` in
  `ZigEmitter.codex`) and the wasm plug (`wat-rt-text-to-double` in
  `WasmEmitter.codex`) still carry the OLD algorithm. Both were written
  deliberately to mirror the old seed's bits, and both now disagree with the
  seed on exactly the literals Steve Howell reported. The C# emitter uses
  `double.Parse` and agrees.
- **The gap that matters more than the two ports:** no arm anywhere compares a
  plug parse against the seed's parse. The divergence was invisible for that
  reason alone. Write the arm first; the ports are the easy half.
- When the mirrors agree, close the row and close Steve's issue 125.

### Steve Howell, outside contributor

He is a real contributor with a Claude of his own, he measures before he
files, and he has been right against us more than once. Root alone reads and
answers his mail (`showell285@gmail.com`); PRs and issues are his stated
preference for substance.

Open pull requests, all with receipts posted 2026-09-10:

| PR | subject | note |
|---|---|---|
| 137 | boundary cases for `real-literal-rounding` | Green at head now. Ingested inside shelf 25451, verified there. |
| 135 | zig plug: box every payload-carrying variant | Touches `ZigEmitter.codex`. He states it resolves the runtime face of issue 126; confirm rather than accept. |
| 138 | zig plug: an unused let binds and silences its value | Touches `ZigEmitter.codex`. |
| 136 | `apps/globe`: regenerate `GlobeKernels.wgsl` | Separate file, not blocked behind the zig sequence. |

PRs 135, 137 and 138 plus the text-to-double port all touch
`ZigEmitter.codex`. Sequence them through one lane or the file gets merged
several ways.

Open issues:

| issue | our row |
|---|---|
| 125, Real literals not correctly rounded | COMPILER-57. **Left open deliberately** until the plug mirrors agree; a comment on the issue says so. |
| 126, hosted compiler types every comparison `ErrorTy` | COMPILER-56, open, unowned, not verified on metal |
| 120, a prose continuation at three columns lexed as code | COMPILER-55, open, unowned, verified statically at head |
| 115, four things depend on the deck discipline without declaring it | open, unowned |
| 110, `inline-single-caller` erases a definition silently | open, unowned |

### TheLostParadise recommendations

`docs/PM/Active/Stories/TheLostParadise/11-recommendations.md` carries an owner
table, R-1 to R-16. R-16 is landed (main 25446, the Update 34 amendment in
`GitHubUpdate58.md`). The rest are open against the previous fleet's lane
names; reassign them to yours rather than assuming the names still mean
anything.

## Live hazards

- **`//Codex/main/docs/PM/CurrentPlan.md` is open for edit in the DEFAULT
  changelist on client `BigWhite_Codex_main`** (Damian's own). A bare
  `p4 submit` on that client sweeps the default changelist (P-DEFAULT). Damian
  knows; the file is at head revision #1095 as of this writing, so the earlier
  "behind head" risk is gone. Leave another client's open file alone.
- **`build/build.ps1 -Internal` is banned** and the bare gate is Damian's. A
  lane verifies by compiling and running the tests its change touches. A
  seed-affecting change adds the scratch fixed point, `build/bvt.ps1` on the
  candidate, the signer, and `test-self-verify`.
- **The box is one DIMM down** (about 15.8 GiB visible, roughly 7 GiB free at
  handoff). Re-measure before quoting any figure.

## What the debugger can do, because the previous fleet nearly missed it

All debugging uses codex-vm and PowerShell. No GDB, no WSL. `compile.ps1`
writes a `<out>.map` sidecar of `0xADDR <size> <name>` rows, and the file
offset of an address inside the CDX is `addr - 0x100000 + 224`. That pair,
plus `Resolve-Rip` in `build/vm-config.ps1`, located a corrupted immediate to
the exact instruction in one pass after hours of guessing. `-Break <fn>`
patches INT3 at a named function. `-debug` gives an interactive shell. The
full toolkit is `docs/OperatorsManual.md`, "Native Debugging Toolkit". If a
feature you need is missing, Damian's standing instruction is to build it.

## Reading order for a new commander

1. `CLAUDE.md`, the rules and their tiers.
2. `docs/VisionAndVirtues.md` line 36. Correctness is absolute, and it is the
   first rule of the project, not R-GATE.
3. `docs/PM/Active/Stories/LESSONS.md`, the index. Read a story only when its
   lesson becomes load-bearing.
4. `docs/PM/CurrentPlan.md`, your own row only.
5. `docs/Agents/PerforceProcess.md` and `docs/Agents/CoordinationProtocol.md`
   before any Perforce operation or gate run.

## One thing worth carrying

`docs/PM/Active/Stories/TheVicesOfOpusAndFable.md` is the record of how a
one-line comment about a known defect survived sixty-nine days, two audits,
and two deliberate ports of the defect into other backends, because a true
sentence in a comment reads as documentation instead of as an unfiled bug.
The parser it describes is fixed. The habit it describes is not, and the
suspension of this fleet is the second instance of the same shape: a rule
written once in prose, believed, and not run.
