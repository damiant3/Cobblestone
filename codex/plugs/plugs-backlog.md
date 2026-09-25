# Plugs -- open capabilities

Quire-domain backlog, same rules as the app registers: an entry says what is
still missing and nothing else, a closed entry is DELETED, and a gap that is
still real is never quietly dropped. `docs/PM/CurrentPlan.md` carries the
shape. **The depot is the record of what was done; this file is only what is
left.**

## Standing hazards

**UNDER QEMU A RISCV `!EXC` PC AND TVAL READ 0x80 BELOW THE `.map`/ELF ADDRESS.**
Add 0x80 before a `.map` lookup or a `.bin` read; unshifted, the PC names the
previous function and a cause-3 trap lands on a word that is not an `ebreak`
(measured 2026-09-24 on two subjects, the `ebreak` at reported PC + 0x80).

**A plug that does not handle a construct usually EMITS SOMETHING ANYWAY and
reports OK.** A missing builtin arm passes the name through as an ordinary
call; a wrong field spelling emits a division; a wrong `list-push` emits a
mutating append. For most of these plugs nothing downstream ever runs, so
silence is silence, not agreement (L-GAP).

**A LITERAL PATTERN IS A SECOND CODE PATH AND IT IS THE ONE THAT ROTS.**
Found by Steve Howell, 2026-08-26, who fixed it in his own zig plug and
reported the class. A Boolean `IrLitPat` carries the SPELLING `True` or
`False` rather than a number (bare metal decodes it in `pat-lit-to-integer`,
`codex/compiler/Syntax/Token.codex:149`). **Nearly every plug in this tree
already maps that spelling correctly where a Boolean appears as an
EXPRESSION, and did not where it appears as a PATTERN** -- the two paths are
separate in every plug and the pattern path gets written by copying the
integer case. Measured by running the emitted programs: csharp CS0103,
javascript `ReferenceError: True is not defined`, zig undeclared identifier,
all three fixed 2026-08-26. Python, Haskell, Ada and Pascal spell their
Booleans the way the wire does and are safe by coincidence, not by handling
it. **Two further defects surfaced only once the first fix let the programs
run further, which is the part to generalise: a literal-pattern bug hides
the next one behind it.** csharp appended a catch-all after arms naming both
`true` and `false`, which C# rejects as CS8510 unreachable; javascript gave a
Char literal pattern no BigInt suffix while the scrutinee carried one, so
`15n === 15` was false and every char arm fell through to the catch-all --
unrelated to Booleans and failing before any of this. **Grade a plug with
`codex/test/when-bool-cross` and `when-bool-pattern`**, which carry integer
and char controls precisely so a fix that breaks the neighbouring literal
kinds shows up. **UNSWEPT, and this is a lead rather than a finding:** the
remaining plugs were read, not run, and every one that emits `IrLitPat` text
verbatim into a target spelling Booleans lowercase is a candidate. **Queued
for the wasm plug (fester's, not touched here):** these two tests should gate
it early, per Steve's suggestion.

**RECORDED LEAD, NOT BUILT: the plug wire performs no arity check.**
`codex/plugs/common/IRTextParser.codex:713` builds `IrApply` structurally,
so hand-authored IR can express shapes the compiler cannot produce -- a
non-full-arity self-application in tail position being the measured example
(`docs/DevelopersRulebook.md`, "What the wire carries"). Every plug's TCO
gate is safe against COMPILER-produced IR by the type checker's occurs
check, and unprotected against anything else. Whether that matters is a
question about the plug wire's TRUST MODEL rather than about any plug, so it
is recorded here and deliberately not acted on. Raised by Steve Howell's
PR 87, answered 2026-08-26.

**A name census cannot answer a semantics question, in either direction.**
Keying on the quoted Codex name misses a plug that declares the arm in a
prelude and counts a plug whose REFUSAL text contains the name. A registered
name is not a correct arm either. Run a subject through the plug and read the
OUTPUT.

**A STALE PLUG BINARY IS A CONFIDENT WRONG ANSWER IN EITHER DIRECTION.**
Nothing here runs from the `.codex` you are reading; every harness runs the
`.cdx` beside it. Rebuild before believing any measurement through a plug, and
treat a merge-down as invalidating every plug binary it touches -- the seed
moves under the workspace and nothing rebuilds a plug when it does.
`build/plug-oracle-test.ps1` refuses a binary older than its source or than
`seed/Codex.cdx`; nothing else does.

**`codex/plugs/zig/` is ordinary fleet code** (Damian, 2026-08-18). Credit
Steve Howell in a CL that changes what he wrote and flag it in the next
GitHubUpdate; that is courtesy, not a gate.


**`Get-Command wat2wasm` CAN RESOLVE TO THE npm wabt SHIM, WHICH IS wabt
COMPILED TO WEBASSEMBLY AND INTERPRETED BY node.** pwsh prefers its `.ps1` over
a native exe on a later PATH entry. On a 2.1 MB `.wat` that shim spent 26+ CPU
minutes without finishing, measured twice, where wabt 1.0.41's native
`wat2wasm.exe` assembles the same file in 0.08 s. Native wabt is installed at
`%LOCALAPPDATA%\Programs\wabt` and prepended to the USER Path, but **a shell
started before that change still resolves the shim**, which is how a 22-minute
page build once read as build cost. Check what `wat2wasm` resolves to before
believing any assembly timing. The slowness is not size alone: the same shim
assembles the 2.3 MB `riscv-stdio.wat` in 0.9 s (2026-09-24).

**EXTEND A `WasmCtx` BY CONSTRUCTING A RECORD, NEVER BY `__record-set`.**
`__record-set` overwrites the field and returns the SAME record, so extending a
context with it hands the callee's state to every caller up the stack. The
shadowing fix was written twice and produced byte-identical WAT both times
before this was understood; a fresh record fixed it in one build. `ctx-with`
and `ctx-deeper` exist for this.

**TAKE ONE NEGATIVE FROM ANY SWEEP HERE AND READ IT BY EYE BEFORE BELIEVING
IT.** Three plugs break a sweep's assumptions rather than failing: `wpf` emits
a five-file PROJECT into a directory and refuses a file path; `t3isa` rewrites
the extension, so a sweep watching the `-Out` path sees nothing while the plug
wrote its output to `.t3s`; and `recheck` prints `AGREE n DISAGREE 0
UNSUPPORTED 0`, a column header a refusal-matching regex reads as a refusal.

**`babbage` and `t3isa` refuse HONESTLY and those refusals are correct.**
babbage is shelved, and t3isa exits 6 carrying `; !UNSUPPORTED:` markers that
each name a constraint of a 27-trit machine rather than miscompiling it
silently. Neither is a defect to chase.

**EVERY REFUSAL THIS PLUG EMITS READS `wasm plug:`**, inside `(unreachable (; ... ;))`
(one spelling since 2026-09-25). Census the whole file rather than by line: an
emitted line runs to thousands of characters.

**A GROWING BUFFER IS SOUND IN A READ LOOP AND NOT IN `$list_push`; DO NOT
REPAIR ONE BY ANALOGY WITH THE OTHER.** Both lean on consecutive `$bump_alloc`
calls being contiguous. In a read loop nothing else allocates and the buffer is
not yet anyone's value, so `$read_serial_cce`, `$read_file_uni` and
`$read_file_raw` double in place safely (the cap is 1 MiB, not the 4 MB this
register once claimed, and nothing is dropped). In `$list_push` the block being
extended is a list a CALLER still holds, which is the defect COMPILER-42
records.
## Open

33 rows (audited against head 2026-09-25): 18 open, 7
latent, 4 deferred, 4 standing notes. Rows keep their original numbers.

**Takeable now, no ruling or toolchain needed:** 1.57 (measure which plugs
neither apply nor refuse),
1.73 (measurement), 2.16, 2.17, 2.22, 2.38,
2.53, 2.73.

**WE DO NOT DO TOOLCHAINS** (Damian, 2026-09-07, in those words). This is not
a rule waiting to lift and it is not a question to put to him again: a target
whose runtime is not already on this box is verified by READING what it emits,
permanently. 1.59's four no-closure targets refuse by name under it.
Do not open an item whose acceptance step is "install X, then run the
subjects".

**Latent, taken on their trigger:** 1.48 (red), 1.54, 1.72, 1.106, 2.01, 2.07, 2.70.
**Deferred, not to be reopened without the ruler:** 1.1, 2.08, 2.09, 2.57 (Damian).
**Standing notes, not work:** 2.11 (ruled not built), 2.14, 2.39, 2.66.

**Another's, or blocked:** 1.3 (Damian's battery call), 1.33 (blu), 1.96's
upstream half (COMPILER-30), 2.02 (Steve Howell's), 2.13 (a representation
decision), 2.21 (red; class 6 blocked on representation), 2.28 (unverified
reports), 2.42 (blocked on encoding).

**1.1 -- lift the plug type reconstruction into shared code. DEFERRED**
(Damian, 2026-08-05): a de-risking rehearsal, not a prerequisite. The group-3
sites are `clamp-field-val` (csharp), `a64-field-type-for-store`,
`rv-find-field-type-st`, `a64-collect-field-types`, `rv-collect-field-types`,
`rc-check-ctor-ref-sum`, and the python and javascript clamp paths.

**1.3 (residue) -- 74 of the 77 riscv reds are unattributed.** The general
RISC-V temp-collision defect is fixed and three of the 77 are measured
pre-existing. The rest waits on Damian's call for the full two-arm cross
battery, which R-GATE puts out of a lane's own reach. Sizing for whoever takes
it: 34.2 s per subject SERIAL, so 421 eligible is about four hours per arm the
way it was run; `test-cross-batch` at `-Jobs 8` has never been measured on this
box. **A staleness guard belongs in `test-cross.ps1` itself**, which takes
whatever plug it finds where `hosted-wasm-test.ps1` refuses one older than its
source; that script is GENERATED, so it goes through `codex/build/`.

**1.33 -- there is no DECK on riscv** (blu), so nothing can be made to outlive a
`__heap-restore` there. Three of the five arm64 arms are done; the riscv side
returns its SIZE argument or a literal 0. Latent: `__deck-alloc` returning a
size where the caller wants a pointer.

**1.48 -- RULED LATENT 2026-08-25 (red): the guard suffices until the lane emits
`br`.** `a64-peephole-mov-elim` folds `mov Rd, Rm` into the preceding
instruction whenever that instruction's `Rd` matches, which is sound only while
the preceding instruction runs on every path reaching the mov. The guard is in;
the general case is not, because an indirect branch carries no target in its
encoding and this lane emits none. **Read this row before adding a `br` to this
lane**, which is the moment the general case starts mattering. Not work until
then.

**1.54 -- the touch-everything branch is NARROWED, not closed.** `cx_buf_want`
grows through `std.heap.page_allocator`, so a realloc releases what it replaces
and the arena's retention is gone. What is left is inherent transient COPY cost:
each growth allocates, copies, then frees, so both buffers are live at the
moment of the copy (200 MB touched at stride 4096 peaks at 294 MB and costs
148 ms against the arena's 85 ms). The trade is right, since reserve-and-touch-
little goes from 2,952 MB to 6 MB, but the honest claim is "much less in the
common case, bounded about 2x in the worst case", not "strictly less".
**What would close it: reserve address space and commit on demand, so growth
never copies.** That is a custom allocator over `VirtualAlloc` and `mmap`, a
larger change than this row.

**1.57 -- RULED (root, 2026-09-25, `docs/DevelopersRulebook.md:284-286`): every plug
that knows a callee's arity applies surplus arguments one at a time, or refuses;
none is exempt.** Which plugs still do neither is unmeasured.
The java and riscv halves are fixed and the riscv site is
`RiscVCodeGen2.codex:595`, the `is otherwise` arm, which dispatches on the
APPLICATION'S RESULT TYPE rather than the callee's.

**Two pre-existing riscv reds at head, and NOTHING RUNS EITHER (L-NOGATE):**
`codex/test/ops/saturated-call-returning-function` produces no output at all,
dying before its first line, and it is the canonical nine-arm test for this very
feature; its first statement is a two-level let-bound closure chain, which is
NOT 1.57's site, so it is a separate defect and wants its own row.
`codex/test/closure-under-apply` fails from `split-one-at-a-time` onward.

**1.59 -- four targets have no closures and REFUSE over- and partial application
by name.** ada, cobol, pascal and babbage cannot express a definition that
returns a function, so `overapply-shape.ps1`'s `OverapplyNoClosures` list fails
them by NAME rather than through a shape heuristic; the other 41 subject plugs
are correct on both halves. The wire is lambda-lifted, so the repair would be a
partial application emitted as a tagged record plus one `apply` dispatcher
(`LiftingForClosurelessTargets.md`). It is NOT built: its acceptance is running
the emitted program, and code verified only by reading is an instrument that
cannot fail (L-FALSIF; root 2026-09-25). **Reopens when a toolchain for any of
the four is on this box**, which is Damian's call.

Three limits of the instrument, each measured and each worth knowing before
quoting its score:

- **The sweep grades the emitted SHAPE, not that the emission compiles.** No
  toolchain here runs most of these targets. The PHP eta-wrap is the clearest
  exposure: it inlines the supplied arguments rather than capturing them, which
  is correct for this subject's literals and would need a `use` clause for a
  variable.
- **Dropped-detection was attempted and WITHDRAWN.** go's correct chain carries
  its second surplus argument 110 characters after `choose`; ada's contaminating
  `C(2)(3)`, which belongs to the next statement, sits at 130. Every window wide
  enough to accept go accepts ada. So **an emitter that silently drops surplus
  arguments now PASSES the shape check**, and only the no-closure list catches
  the four that do.
- **`recheck` does not build**, so it has no row and no verdict.

**1.72 -- the python plug's TCO matches a self-call by NAME and not by arity, so
its argument loop and its parameter loop can disagree. LATENT: whether any
well-typed program reaches it is UNESTABLISHED, and that is the weakest part of
this row.** `is-self-call-root` (`PythonEmitter.codex:706`) compares the chain's
ROOT name to the definition's name and nothing compares argument count to
parameter count; the jump evaluates one temporary per ARGUMENT and assigns one
parameter per PARAMETER, so the loops agree only at exact arity. Fewer
arguments: `NameError` on the first turn, and a STALE python local on later
turns, so the loop continues with the wrong argument and no diagnostic. The zig
plug is the control: `zig-tail-self-call` requires
`list-length (chain.args) == (tl.tail-arity)`. Filed because "the type system
happens to prevent it" and "the emitter checks" are different statements and
only the second survives a change to either. **What would settle it, in order:**
the type-checker question (does the shape exist at all), then emit and READ the
output. The fix is not one clause: `is-self-call` has no arity access (signature
change, three call sites).

**1.73 (residue) -- 8 of 56 runners are PROVEN cross-host; the rest is an
argument.** The QEMU per-run port below is PARKED (root, 2026-09-25): no
consumer asks for it. The selection is honored everywhere (`Start-PlugVm` and
`Invoke-PlugVmFileSerial` in `vm-config`), and `plug-smoke` asserts the two
hosts agree BYTE FOR BYTE. What is not measured, kept apart from what is:

- Proven on both hosts: python, javascript, csharp, typescript, wasm, ptx, wgsl
  and plug-run's own arm. The other 48 take the same two helpers, which is an
  argument and not a measurement. **elf, img and pe additionally need a binary
  wire fixture rather than a source file**, and recheck and wpf were not run.
- **Two runs of one plug now coexist on codex-vm** (2026-09-24): `plug-run.ps1` listens on a
  per-run `Get-VmPort` port and `Start-PlugVm` passes `-natmap <plug port>:<that port>`, so the
  guest still dials its compiled-in port. Proven on python: two concurrent runs of one IR both exit 0
  with byte-identical output on host ports 50556 and 56470, where the depot scripts refused the
  second with exit 7. Still fixed under QEMU (`CODEX_VM_HOST=qemu`), and **no `guestfwd` form
  can remap it** (measured 2026-09-25, QEMU 11.1.0, python plug): every plug dials `host-ip`
  `127.0.0.1`, which is outside slirp's `10.0.2.0/24`, so slirp NATs the connection straight to host
  `127.0.0.1:<plug port>` and consults no forward rule. QEMU refuses `guestfwd=tcp:127.0.0.1:...`
  ("Conflicting/invalid host:port"); with `guestfwd=tcp:10.0.2.100:9131-tcp:127.0.0.1:<P>` the
  chardev dialled `<P>` at QEMU start (0.1 s) and the guest's own connection arrived on the literal
  9131 (0.6 s). **What would work, both unbuilt:** (a) `host-ip` moved to an in-network address
  such as `10.0.2.100` in every plug (a literal in 46 `.codex` sources, 2026-09-25) plus a matching
  route in codex-vm's NAT, then a `guestfwd` per run; unmeasured there is whether QEMU holds
  host-first bytes written before the guest dials, and a chardev carries ONE stream, so tag 3
  (serve-again) cannot ride it. (b) A host broker on the fixed port that names each connection's
  QEMU by its source port (`Get-NetTCPConnection` owning process) and relays to that run's
  listener: no guest change, one shared process (L-SHARED). A second QEMU run still refuses
  naming the port. The 18 runners that call `Start-PlugVm` without their own `plug-run` keep
  their fixed ports.
- **The codex-vm serial-drop check (`output buffer growth failed`, exit 10) has
  no QEMU counterpart**, so on that host a short console is not detected. Say
  so rather than read its silence as agreement (L-FALSIF).
- `produced nothing` and `differs` should not read alike in the failure text:
  the first is a statement about the host, the second about the subject, and
  only the second is ever a plug finding.

**babbage is SHELVED** (Damian, 2026-08-21): vanity work. Its open items are in
`codex/plugs/babbage/babbage-backlog.md`. Do not add babbage items here.

**1.96 (residue) -- `ada-type` and `fort-type` still guess for `TypeVar` and
`FunTy`.** Both arms now refuse an `ErrorTy` with `cx_UNSUPPORTED_ErrorTy`
rather than guessing a 64-bit integer, and three subjects carry that refusal.
The same shape of guess with a different atom was NOT swept and no complainant
has appeared for it. **The upstream half is COMPILER-30** and is open;
`build/ir-fidelity`'s `lambda-param-type` case is its standing runner, and the
three marked programs should stop carrying the refusal when it lands.

## 1.106 -- arm64 staging runs into the platform registers past six stack arguments

The 2026-08-27 clobber is fixed (blu, in COMPILER-9's class-B set). What is
left is a latent limit of the same family, left unfixed on purpose: the
scratch base is `a64-x10 + slot`, so past six stack-passed arguments, which
is more than fourteen parameters, staging runs into x16, x17 and x18, the
intra-procedure-call and platform registers. Nothing in the tree reaches it
today. Reproducer with its controls: `docs/Test/Active/Arm64StackArgClobber.codex`.


## 2.01 -- a single ptx/hello flap under contention, one occurrence

`qemu produced nothing` once (red, 2026-08-28) in a standing gate at
~20543-era head, on a box running several lanes' VMs; the identical standalone
leg immediately after answered 1,630 chars, exit 0. Load-suspected and
recorded rather than quieted, per the phase's own rule. **A second occurrence
makes it a finding about the file-serial QEMU path under contention**; until
then there is nothing to chase.


## 2.02 -- the zig plug REFUSES a redundant match arm that the compiler now emits combined, so it refuses legal Codex

**Routed by root 2026-08-28 from Steve Howell's PR 96, which was closed as
already-fixed COMPILER-side (fester 20398).** The compiler half is
verified (blu); **the zig half is NOT, and that is the open work here.**

red gave C# the drop at 20352. zig has no such drop, so where the compiler
now emits a combined arm, the zig plug refuses a program that is legal
Codex and that every other lane accepts. Evidence is PR 96; read it before
measuring, and measure the zig arm rather than inferring it from the C#
one -- the two plugs took different routes to the same requirement.

**STEVE'S TO FINISH, not a lane's to lift (Damian, 2026-08-29):** *"leave the
zig plug work to Steve, its his bug to finish here, not a cross cutting plug
lift like we do sometimes."* It came in on his PR 96 and it stays with him. Do
not claim this row; it is not unowned work waiting for a free lane.

**MOSTLY DONE (Steve Howell, PR 103, absorbed by red 2026-08-31).**
`zig-arm-shadowed` drops a prong an earlier trivially-guarded arm already
names, so the nullary-constructor and literal shapes now emit. Verified end to
end rather than by reading the emitted text: the pre-fix plug refuses
`codex/test/ops/match-shadowed-arm` with exactly two `duplicate switch value`
errors, and the rebuilt plug emits zig that compiles under 0.16.0 and prints
the same five lines as the x86 arm.

**One shape is still open, and it is the reason this row is not closed.**
`zig-pat-switch-value` answers only for `list-length subs == 0`, but
`emit-zig-match-arm` also emits a BARE prong for a payload-carrying
constructor whose binders are all unused (`ZigEmitter.codex:2155` for one sub,
`:2158` for several). Two such arms on the same constructor still collide and
still refuse at zig. The failure direction is safe -- it under-drops, so
nothing is miscompiled -- and the fix is to widen `zig-pat-switch-value` to
mirror those two conditions.

Two notes for whoever takes that. The PR's prose claimed a guarded earlier arm
does not shadow; that is unreachable and was dropped at absorption, because
`emit-zig-match-arms` has exactly two call sites (`:2078`, `:3275`) and both
sit in the `else` of `zig-branches-guarded`. And `zig-arm-shadowed` scans
backward per arm, making emission O(n squared) in arm count with one Text
allocated per comparison; bounded and accepted, but if the widening makes it
hotter, render each arm's switch value once into a list rather than
re-rendering the earlier arm's on every comparison.

## 2.07 -- OPEN (latent): the ELF plug's wire carries no architecture, so `ElfWriter` cannot be told to label a non-x86 header

`ElfWriter` threads a `machine` parameter through all three header builders and
defines `elf-machine-riscv` (243) and `elf-machine-aarch64` (183); every caller
passes x86-64. The payload's MODE byte selects a container (0 bare metal, 1
user-mode, 2 hosted console) and no architecture, so a machine selector has to
reach `plug-emit-bytes` (a payload byte beside the mode, or a second mode range),
which moves a protocol four callers spell. Take it when the first non-x86 caller
exists. The user-mode file is a correct ELF64 container around bare-metal code
(console and heap are device registers), so it loads on Linux and stops at its
first print; the hosted arms are PrismDevEnvironment stage 5a.

## 2.53 -- SizedVec coverage: what Fortran and HTML emit for a sized vector

Both type emitters now carry a `SizedVecTy` arm (checked 2026-09-25), so the
CDX2070 refusal no longer fires: `fort-type` answers the element type, the same
convention its `ListTy` arm uses, and the html emitter answers `Array`.

C# emits all five `vec-*` builtins (2026-09-25): `codex/test/ops/vec-sized` matches its
`.expected` through .NET 9 and the csharp oracle passes 59 of 59.

No other plug maps a sized vector to a plausible wrong type (blu, source census
2026-09-25): zig's `emit-zig-type` falls back to `@compileError`; ptx's
`ptx-type-for` gives every heap handle, this one included, the 64-bit register
its Text handle takes; `cobol-pic` has no caller; 22 typed-target plugs (go,
java, kotlin, swift, haskell and kin) name no `CodexType` constructor at all, and
typescript and unity name one only to format a literal or a console call, so
they erase types and have no fallback arm to be wrong in. The five
sized-vector builtins (`vec-empty`, `vec-singleton`, `vec-cons`, `vec-head`,
`vec-length`) are dispatched by name in csharp, python, javascript, typescript,
zig, arm64, riscv and recheck only (same census, no prefix handler anywhere; the
three script plugs and zig since blu 2026-09-25, `codex/test/ops/vec-sized`
matching its `.expected` under Python 3.11, node and zig 0.16, a refusal or an
exception on each depot plug before; zig gives an empty vector whose element no
use fixes the element type `void`, so a later push fails to compile): every other text plug and wasm has no
arm, so a program holding a sized vector reaches them as a call the target never
defines, the shape `check-plug-builtins.ps1` exists for and cannot see, because
its subject holds no vector.

**What Fortran and HTML should emit is a per-language decision rather than a
mechanical arm.** `fort-type`'s own prose has Fortran emit a reference to a type
nothing declares, so that the target compiler names the unmappable type; copying
a neighbouring arm would ship a plausible wrong type instead (L-BAILVALUE).

## 2.08 -- the board ELF carries no per-board link or flash address

Deferred: no board, Damian 2026-09-24.

`RiscVStdio` takes a mode line and `ELF` answers a RISC-V ELF64 from the riscv
plug's own writer, entry resolved through `rv-find-func-offset` and REFUSED by
name when there is none.

What it honestly is: ELF64, `EM_RISCV` 243, loaded at `0x80000000`, which is
the RAM base `qemu-system-riscv64 -machine virt` uses. **The per-board link and
flash addresses for the nine named HAL boards are NOT in**, so the pill says
"RISC-V kernel" rather than naming ESP32-C6 or FE310. Putting a board's name on
a file whose load address was not derived from that board's memory map is the
mislabelling this register keeps closing.

`codex/plugs/arm64/Arm64Elf.codex` is reached from the page: the Binary tab's
ARM64 kernel pill builds an AArch64 ELF64 at `0x40000000` through
`arm64-stdio.wasm`, graded by `page-workspace-arm.js` arm 11c. No kernel from
it has been booted.


## 2.09 -- the nine HAL boards cannot run what the board chain emits, and a design doc says they can

Deferred: no board, Damian 2026-09-24.

A kernel built through the browser chain boots on `qemu-system-riscv64` and
prints output byte-identical to `codex/test/factorial.expected`. It needs
`-m 1G`, because `__start` sets the stack to `0xBFFF0000` inherited from the
x86 memory map, so QEMU virt's default 128 MiB leaves the stack outside RAM:

    qemu-system-riscv64 -machine virt -m 1G -nographic -bios none -kernel kernel.elf

**The board that works is the SYNTHETIC RV64 platform the cross battery
already uses, and none of the nine HAL chapters in `codex/boards` can run
this.** FE310-G002 is RV32IMAC and ESP32-C6 is RV32IMC while we emit RV64, and
`LD` and `SD` do not exist on RV32; `QemuVirtBoard` is AArch64; the remaining
six are Cortex-M or Cortex-A with no codegen at all.

**`PrismDevEnvironment.md` stage 2e says ESP32-C6 and FE310 "have a real chain
through the riscv plug's own ELF writer", and that is wrong on ISA WIDTH**,
which no link-address work fixes. FE310's SRAM base is `0x80000000`, the same
number as QEMU virt, which is the likely source of the claim. Reaching those
chips needs an RV32 mode: ELF32, 32-bit pointers, no doubleword ops.


## 2.39 -- the hosted targets' known limits

Both hosted targets run and are graded (60 of 60 across Linux ELF64 and
Windows console PE32+, against the same `.expected` sidecars bare metal uses).
These limits stand, and are named here rather than left to be rediscovered:

- **One write per byte on both targets.** Staging was written and withdrawn:
  it makes correctness depend on every caller bracketing its print, and the
  raw and itoa paths do not, so an unbracketed stage is a wild store rather
  than a wrong character.
- **No stdin on either target.**
- **The Windows arena is 1 GB against bare metal's 3.**
- **The Windows binary has run on one machine.** The PE disables ASLR and
  depends on fixed addresses, so a host that forces relocation is untested.
- **Linux is asserted beyond WSL2, and Damian ruled 2026-08-29 that the
  assertion is enough.** The artifact has no host surface to depend on:
  ET_EXEC, no INTERP, no dynamic section, three PT_LOADs, and only `write` and
  `exit_group`.


## 2.11 -- binary wasm emission: RULED not built (Damian, 2026-09-02: "wat2wasm is fine")

Neither a binary wasm emitter nor a WAT assembler in Codex is built. The
transpiler stack is the rope thrown back to the old world and the cord is cut
in the forward direction, so neither serves either direction; `wat2wasm` stays
on the PATH as the assembler. This closed the row, and it supersedes the
2026-08-30 "approved, scheduled later".

**Open above the ruling, and it is Damian's:** the SHAPE of wasm binary
emission if he reopens it (`docs/PM/CurrentPlan.md`, reek's row). The sizing
that would inform that choice is in the CLs on this row: the plug's own
emission is 3.6 s of a 149.4 s module phase and `wat2wasm` itself is 0.3 s, so
there is no build-time case, only self-sufficiency.


## 2.13 -- the zig plug discards a Real's width and overflow mode

`ZigEmitter.codex:348` and `:379` map `RealTy (w) (m)` to `f64`, discarding
both the width and the overflow mode, so in this plug an f32 Real is an f64, a
trapping Real does not trap and a saturating one does not saturate. Filling the
mode rows would replace an honest refusal with a plausible wrong number.
**This wants a representation decision, not an emitter row.**

**The WIDTH half is settled and the MODE half is not.** An approx Real is the
exact f64 widening of its f32, which is a real convention rather than an
erasure: `cx_bits_to_real_approx` states it, `cx_to_real_approx` rounds through
f32 so the narrowing is not lost, and `real-approx-to-int` follows from it
(2.36). Nothing analogous exists for the modes, and
`to-real-trapping` / `to-real-saturating` are still refused by name, which is
why `codex/test/ops/real-mode-show` cannot build.

**`show` on a Real WORKS now (reek, 2026-09-07),** so this row no longer blocks
text output. `cx_real_to_text` follows the `__real_to_text` oracle's format: at
most fifteen fractional digits, trailing zeros stripped to one, always a dot
and always a digit after it, with the integer part taken through
`cx_real_to_int` so the two agree on NaN, the infinities and anything past 2^63
by construction rather than by a guard written twice. CCE codes read off the
plug's own `cce_table`: 3 is '0', 65 is '.', 73 is '-'.

**An `opening` that RETURNS a Real prints too**, from the same helper; that was
a second refusal for the same missing capability and both are deleted rather
than left uncalled. Graded on an entry returning `2.5`, which answers `2.5`.

**The corpus cannot grade the interesting half of that, so it was probed
directly.** Every `show` of a Real in the tree is a half or an integer, all
exact in binary, so a green run does not separate a correct formatter from one
merely right on exact values (L-CONSTRUCT). Driving the emitted function on
values the corpus lacks: `0.1` answers `0.1` rather than a 0.0999 tail,
`1.0/3.0` answers `0.333333333333333`, `2.9000001` and `3.141592654` come back
exactly, `0.0000001` keeps its leading zeros. Negative zero prints `-0.0`,
following the oracle's sign-bit test; no `.expected` in the tree spells it, so
that one is a decision rather than a measurement.


## 2.14 -- what the hosted corpus score does NOT mean

The four defects this row was opened for are fixed and parity holds: wasm 51 =
hosted linux 51, one ahead of hosted windows 50, re-measured 2026-09-01 on seed
D6ED6F35, both arms on the same kernel with the plug rebuilt first (L-SAMEVER).

**THE DEFAULT 60 IS A STRATIFIED SAMPLE, NOT THE CORPUS** (2.16). A score read
against a run from before 2026-09-25 compares different sixties: the earlier
default was the first 60 by name, 55 of them `apps/*`. **Quote a named slice or
quote the eligible population; never read the default as a corpus score**
(L-DENOM).

**`hosted-kind` is hard-coded to 1 and no consumer can currently tell.**
`WasmEmitter.codex:1119` answers 1, which in the compiler's own convention means
hosted LINUX (`X86_64State.codex:120`, 0 bare, 1 Linux, 2 Windows). Every
consumer tests `/= 0` only, so the wrong value is inert today and the first
consumer that distinguishes 1 from 2 inherits it as data. Whether wasm gets its
own value is a compiler call, not a plug one.

**The corpus cannot distinguish exact from approximate equality, so `~`'s green
proves less than it looks.** `approx-eq` only ever compares equal values and
values a whole integer apart, so exact equality passes all six of its checks.
The evidence for the ULP form is the x86-64 emitter it was read off, not the
subject. A subject with operands one to five ULPs apart would divide them and
does not exist in this tree.


## 2.17 -- every wasm verdict published before 2026-09-01 was graded against a broken control

`emit-hosted-start` set `rbp = rsp` and reserved NOTHING, so every local the
entry code spilled sat BELOW the stack pointer, where the next push or call
overwrote it. The prologue now patches a placeholder with
`align-16 (peak-spill * 8 + 64)` once the entry body is emitted, sized the way
`emit-function-standard` has always sized a frame, with `reset-func-scratch`
first so `peak-spill` counts the ENTRY's spills.

**One cause, both symptoms:** the truncation was the print loop's exit test
re-reading a corrupted length (`Zebra 7` printing `Z 7` on Windows and looping
forever on Linux), and the 0xC0000005 was the same corruption landing on a
pointer. Only the entry sum printer was affected because it is the one place
with enough live values to SPILL; in user code the same printer's locals stay in
registers. The x86-64 control over `ops/*` is 40 pass 0 fail, so
`ops/real-mode-fields` closes and 2.16's "red on both arms" row is retired.

**THE STANDING CONSEQUENCE, and it is why this row is kept rather than
deleted: every wasm verdict this campaign published before 2026-09-01 was
graded against an arm that mis-rendered any constructor name.** The control is
correct now and the wasm gaps can be trusted as wasm gaps, but a number quoted
from an earlier run of this campaign cannot.

**One item is still open under this row:**

- **`__self-type-defs` is a compiler-wide question, not a wasm one, and is NOT
  taken.** wasm emits an empty list; arm64 and RISC-V emit integer `0`, which
  hands back a null where a `List TypeBinding` is expected, so wasm's answer is
  strictly better than theirs. Only x86-64 builds the real table. Porting it
  means about 250 lines from `X86_64Compound.codex` plus tracking `CodexType`'s
  28 constructors forever, and every consumer is inside that same file. A
  decision for Damian if it is ever wanted.


## 2.16 -- OPEN, narrowed (reek, root 2026-09-24): the release gate runs 60 wasm subjects, not the corpus

`build/build.ps1` phase `wasm-run` (after `wasm-bundles`, triggered by `codex/plugs/wasm` or
`codex/compiler`) runs `hosted-wasm-test.ps1` with this run's compiler. It grades the harness default:
60 of 1099 eligible (2026-09-25), STRATIFIED by first path segment in `hosted-elf-test.ps1`, each stratum's picks its
names of lowest SHA-256 rank, so adding a subject moves at most one pick. A red listed in
`codex/plugs/wasm/wasm-run-baseline.txt` (subject, row, reason; every entry cites a row here) does not
fail the run; a listed subject that passes does, and so does a cited row that is gone. At seed EBCA68AA
(2026-09-25): 59 pass, 0 known red, 1 `.arch-only` skip. Still a
sample: a wasm regression outside those 60 reaches no gate. `wasm-e2e.ps1`, `page-example-test.ps1` and
`check-emitted-runtime.ps1` run in no gate either.

**A Text accumulator appends in place on wasm**, as on x86-64: a self-tail-recursive Text parameter
that every tail call passes back as `acc & r` (`wip-accumulators`, the port of `inplace-accumulators`)
extends at the heap top through `$text_append_inplace`, behind a `$text_own` entry guard, and a literal
below `$heap_start` is never extended. `cost/accumulator-corpus` `p-append-text` retains 68 / 132 / 260
bytes at n / 2n / 4n (x86-64 72 / 136 / 264); the subject still skips wasm by `.arch-only` because its
other rows print x86-64 allocator counts. `codex/test/text-accum-owner` grades the guard.
**Grade this plug by RUNNING, not by assembling.** An unbound or arity-less name emits
`(unreachable (; wasm plug: no form for <name> ;))`, so a missing arm assembles and traps at runtime; grep the
emitted `.wat` for `wasm plug:` to name every missing builtin in a module rather than the first one
that stopped the assembler. A source grep for builtin names cannot answer this: `fail`, `now`, `max`,
`compare`, `abs` and `force` are ordinary English and `.codex` carries prose by design.

## 2.21 -- OPEN (red, from COMPILER-36): the wire states the integer overflow contract, and the remaining plugs wrap where it says trap

THE CONTRACT (root, COMPILER-36). `add-int`, `sub-int` and `mul-int` on a plain Integer TRAP on signed overflow. The wrapping band is spelled `add-int-wrapping` / `sub-int-wrapping` / `mul-int-wrapping` and its node type reads `(int i64-min 9223372036854775807 ov-wrap)`. `codex/plugs/common/IRTextParser.codex` collapses both spellings to the plain op and `ir-parse-expr-binary` (`IRTextParser.codex:729`) parses the node TYPE, so every plug already holds the mode per node and keys on `int-ty-wraps ty` (`codex/compiler/Types/CodexType.codex:142`, a chapter every bundle carries). The gap is at the EMIT sites only; the parser needs nothing. The contract also reaches a plug's own PROGRAM, where `plug-selftest` is the only runner that sees it: a byte assembler is a shift, not a multiply.

DONE. x86-64 bare metal traps. wasm: `emit-wat-binary` keys on `int-ty-wraps` and calls `$cx_add_trap` / `$cx_sub_trap` / `$cx_mul_trap`, three preamble helpers ending in `unreachable`. Text family 1, zig / csharp / rust: zig emits `+` `-` `*` against `+%` `-%` `*%`, csharp `checked(...)` against the unchecked default, rust `checked_*(...).expect("integer overflow")` against `wrapping_*`. Text family 2, groovy / go: groovy's operators promote to BigInteger rather than wrapping, so the band narrows with an explicit `(long)` and a plain Integer takes `Math.addExact` / `subtractExact` / `multiplyExact` on coerced longs; go has no checked primitive and gets three hand-written helpers that panic, the mul check by quotient after answering a zero operand and the two `-1` cases.

UNIT 2.21a -- 64-BIT INTEGER IN THE JVM TEXT PLUGS (java, kotlin, scala), the prerequisite for their trapping arm. Each represents a Codex Integer as a 32-BIT int: java casts `(int)` at every integer op and emits `IrIntLit` with no `L` suffix (`JavaEmitter.codex:82,114-119`), kotlin (`:236`) and scala (`:230`) emit a bare literal, which is a 32-bit `Int` in both languages. Two consequences, and the second is why this blocks rather than merely degrades: a Codex Integer past 2^31 is already wrong in all three today, and `Math.addExact` on those operands binds the INT overload and traps at 2^31, on values the contract declares legal, so a trapping arm built on the current representation turns working programs into crashes and is strictly worse than the wrap it replaces. The unit is the representation: literal suffix, the casts at every integer site, and whatever the type mapping and stdio paths assume about width. None of the three has a toolchain on this box, so it is accepted by emission only. **NO JDK, PERMANENTLY (root relaying Damian's 2026-09-07 standing rule, verified at this file's "WE DO NOT DO TOOLCHAINS"): the question is not to be put again.** What that closes is the BLOCKER, not the defect. The row was held for a JDK because no JVM here can tell a coherent-but-wrong-width representation from an incoherent partial widening; under the standing rule that distinction is settled by READING the emission, which is the same standard this file's toolchain rule sets, and a partial widening is visible in the emitted source. So the unit stays takeable and its acceptance is a reading: literal suffix, the casts at every integer site, and what the type mapping and stdio paths assume about width, in all three emitters at once, with compose following Kotlin. **NOTHING IS SHELVED and no CL exists** (re-verified 2026-09-07 on both red clients); a circulated "written and shelved" wording was wrong and there is no shelf number to cite.

OPEN, AS SEVEN CLASSES (root's ruling 2026-09-07: one CL per class, class = the target's overflow mechanism, and REFUSE with a named diagnostic wherever the class has no way to honour the contract). 48 plugs carry an emitter; 6 are done. Each of the remaining 42 carries its OWN `is IrAddInt` arm, so there is no shared site and no delegation: measured 2026-09-07, only 1 of 6 front-end wrappers checked emits through the javascript emitter, and the wrappers do not share a target (compose is Kotlin, maui/winforms/wpf are C#, qt/gtk are C++, flutter is Dart). **The REFUSAL IDIOM already exists and is `wgsl-refuse` (`WgslEmitter.codex:30`): `"CODEX_REFUSED_" & what`, an undefined identifier carrying its own reason, so the target toolchain fails with the name in its error and no value reaches a caller (L-BAILVALUE, L-ACCEPTED).** **A PLUG IS CLASSED BY THE LANGUAGE IT EMITS, NEVER BY THE NAME OF ITS TOOLKIT, and the second reading is what caught it: qt emits QML with a JavaScript block and gtk emits Python (PyGObject), so neither is C++ and both were in the wrong class; compose emits Kotlin, flutter Dart, maui/winforms/wpf C#, swiftui Swift, and react/vue/svelte/angular/electron/html JavaScript.** The wrapper plugs emit the language of their HOST RUNTIME, not the language the toolkit is implemented in. Membership below is MEASURED on each plug's emitted literal and operator; the overflow MECHANISM in each heading is a claim about the language, not something this box can run, and no toolchain here grades any of these.

- **Class 1, checked primitive, 64-bit. DONE** (csharp, clojure, maui, winforms, wpf, julia). csharp, maui, winforms and wpf all emit C# with `L` literals and take one arm: `checked(...)` for a plain Integer, the unchecked default for the band. clojure needed no helper at all, its `+` on a long already throws ArithmeticException and `unchecked-add` already wraps. julia has both natively too: Int64 wraps and `Base.Checked.checked_add` and its siblings throw OverflowError, which is why it belongs here rather than with the bignum targets its language family suggests. GRADED: csharp answers 57 of 57 against x86-64 truth on `plug-oracle-test`; the other five have no toolchain on this box and are accepted by EMISSION only.
- **Class 2, wrapping-only 64-bit, no checked primitive. DONE** (d, fortran, objc, flutter). The go shape: a helper per op in the plug's own preamble, so the emission carries one grammar rather than four closure syntaxes. Two members are NOT plain wraps and the difference is in the code: **objc** is C, where signed overflow is UNDEFINED rather than wrapping, so its band does the arithmetic in `NSUInteger` and converts back, and its trap computes the same way before reading the sign, because the check must not be the thing that overflows; **fortran** has no unsigned type at all, so its band is the bare operator and rests on gfortran wrapping in practice rather than on the standard, which leaves `integer(8)` overflow undefined. Fortran also has no exceptions: `error stop` is the only way one of its procedures can refuse to return. d and flutter (Dart on the VM) genuinely wrap, so their bands are the bare operator. EMISSION ONLY: no toolchain for any of the four is on this box (g++, gcc, clang, dmd, ldc2, gfortran, dart all absent, measured 2026-09-07) and none is oracle-wired, so nothing here is graded by running. Dart on the WEB is a double, where neither arm holds; that build would take the class 5 treatment. **FOR WHOEVER INSTALLS A TOOLCHAIN, START WITH FORTRAN, AND THE QUESTION IS REFUSE-OR-WRAP:** its band is the one arm in this class resting on a compiler's habit rather than on a language guarantee, so `gfortran -ftrapv` or a future release turning overflow into a trap would make the WRAPPING band abort instead of wrapping, which is the contract inverted rather than merely unimplemented. The two outcomes to distinguish on the first real run are a band row answering the wrapped value (the arm is sound and the standard's silence is harmless here) against a band row aborting (the plug cannot express the band at all and belongs in class 5 with a named refusal, not in this one). Nothing on this box can tell those apart today.
- **Class 3, trapping by default. DONE** (zig, swift, swiftui, nim, ada). **The PLAIN arm needed nothing and the BAND was the entire job**, which is the reverse of every other class: these targets already trap, so what was wrong is that the wrapping band trapped too, inverting the contract rather than leaving it unimplemented. The row previously called this class "nothing but a sentence" and that reading had it backwards. swift and swiftui take the masking operators `&+` `&-` `&*`; nim has no such operator, so its band goes through `uint`, whose arithmetic wraps by definition, and casts back; ada needs a MODULAR type and `Unsigned_64` is the one that matches the width, but converting a negative `Long_Long_Integer` to it would itself raise, so the band is two `Ada.Unchecked_Conversion` instantiations with the arithmetic done in the modular type. **The PLAIN arm in two of them rests on a compiler setting rather than on the language, the same shape as fortran's band in class 2:** nim's overflow checks are on by default but `-d:release` turns them off unless `--overflowChecks:on` is passed, and swift's trap is off under `-Ounchecked`. Neither is expressible in the emission; both are the build's to guarantee. EMISSION ONLY: no swift, nim or ada toolchain on this box.
- **Class 4, bignum, no fixed width.** python, ruby, elixir, scheme, javascript DONE (main 22426). **gtk joins this class and is done with them**: it emits Python (PyGObject), not C++. GRADED where a runtime exists: javascript and python each 57 of 57 values against x86-64 truth. The band rows overflow, so a bignum plug without the truncation cannot match, and they cannot pass by accident (L-VACUOUS). The trap arm's throw is not exercised: the oracle cannot grade a program that traps.
- **Class 5, double-valued, cannot represent i64. DONE** (typescript, angular, react, vue, svelte, electron, html, qt). Every one emits JavaScript or, for qt, QML with a JavaScript block, so a Codex Integer is a Number and a double holds an exact integer only to 2^53. **BOTH ARMS REFUSE and the node type is not consulted**: the band cannot wrap at 64 bits and the plain arm cannot tell an overflow from a rounding, and in range the two agree anyway, so there is nothing for `int-ty-wraps` to decide. The guard is on the VALUES (`Number.isSafeInteger` of both operands and the result), so a program whose integers fit a double still computes and only the unrepresentable case refuses; refusing every integer add outright would forfeit the 55 oracle rows that are correct today to answer the 2 that are not. The marker is `CODEX_REFUSED_integer_exceeds_double_precision`, which `plug-oracle-test.ps1` reads as its REFUSED verdict. **typescript left this class with 2.43** (a Codex Integer is a BigInt there) and answers the oracle subject in full, PASS 57/57; the other seven carry the refusal arm. The seven are not oracle-wired and could not be: they emit framework sources (React components, Vue and Svelte single-file components, an Electron main, an HTML page, QML) rather than a program node can run, so for them the build is the only instrument. lua, perl and php remain OUT of this class: the row places them here by a language claim nobody has measured, and each needs its integer width established first.
- **Class 6, integer narrower than 64 bits. OPEN:** java, kotlin, scala (32-bit, unit 2.21a), compose (emits Kotlin), ocaml (native `int` is 63-bit), wgsl (i32, already refuses a literal wider than 32 bits), and haskell, which emits bare literals with no `:: Int`, so GHC defaults them to `Integer` and its width is not what the class name assumes. Blocked on the representation: a checked primitive applied to a narrow int traps at the wrong boundary, which is worse than the wrap.
- **Class 7, not general-purpose expression emitters. RULED OUT OF 2.21, not deferred** (babbage, cobol, pascal, ptx, t3isa). The first question was whether the contract applies at all, and measured on what each emits, it does not. **babbage** and **cobol** return a structured result (a store-and-operate record, `PIC S9(18)` fixed decimal) rather than an expression, and neither target has a 64-bit two's-complement integer for the band to wrap in: COBOL's `PIC S9(18)` is 18 DECIMAL digits, not 2^63. **ptx** (`add.s64`) and **t3isa** (`TADD`) are machine targets whose add IS the hardware's, so the band is already whatever the ISA does and a trap would have to be synthesised in emitted instructions. **pascal** is the one worth revisiting if anyone installs a toolchain: it is a real expression language, so the class is a statement about the other four rather than about it. Nothing here is blocked; the contract simply does not reach these emitters, and saying so is the answer rather than a deferral.

GRADING. `codex/test/ops/int-mul-wrapping` and `int-add-wrapping` are the wrapping arms and must PASS on every lane; a trapping arm is x86-only until a lane traps. `codex/test/plug-oracle-arith` carries `wrap-mul` and `wrap-add` rows, so a plug answering a band op with a bignum or a double fails the oracle. A text plug is graded only where its toolchain is on the box: zig and csharp run here (57/57 each), and rust, groovy, go, java, kotlin and scala have no toolchain on this box and are accepted by EMISSION only. A trapping probe is not a fixture, because bare metal cannot grade a program that traps: it lives in the CL description with its control, which is the same emitted program with only the probe function's operator swapped.

## 2.28 -- findings routed from Steve Howell's safari-codex intake, UNVERIFIED

Routed as pointers by val on 2026-09-02 during the safari intake (`apps/safari`,
provenance commit `571d7d08`), at root's direction, and deliberately NOT acted
on. None was reproduced here, so each is his claim rather than our measurement,
and the falsifying test runs before any of it is believed or quoted (L-ROUTE).
His documents are the source and are not in our depot: read them in his
repository at that commit.

- **`FINDINGS.md` item 3, the zig plug: single-letter function names `a` to `d`
  are unusable.** A Codex function named `d` emits `fn d_`, which collides with
  the `Tup4` comptime parameter `d_`, and the error appears at a distance from
  its cause. The fix is to move the tuple constructors' comptime parameters out
  of the namespace `zig-sanitize` renames reserved user names into. **Never sent
  upstream: this is in no PR and no issue.**
- **`WASM_FINDINGS.md` 7, OPEN: the vector ops have finding 1's shape**, finding
  1 being that `Real` was not implemented, which he has since fixed on his side.
- **`WASM_FINDINGS.md` 9, OPEN: neither `env` import can be reached by any
  program.** His own local commit `e6f09556` is titled for this while the
  document still marks it OPEN, so establish which is current before working it.
- **PR 112, UNVERIFIED: unprefixed runtime helper names, SIMD type mismatches and
  corpus parity gaps in the wasm plug.** Reproduce each report before taking a
  repair.

His `WASM_FINDINGS.md` 4 (`show` on a `Real` prints its bit pattern) is not
routed: it is fixed on OUR side, `$f64_to_text` in `WasmEmitter.codex` at head
2026-09-07. His items 7 and 8 belong to `angry-gopher` and are not ours.


## 2.38 -- OPEN: the undefined-callee check covers python, javascript, typescript, zig and csharp only

`build/check-plug-callees.ps1` reads the source `plug-oracle-test.ps1 -KeepArtifacts`
leaves in `build-output/plug-oracle` and fails on a name that is called, bound
nowhere in the file, and not a global of the language's own runtime (python's
`ast` and `builtins`; node's `typeof globalThis`); zig by `zig ast-check`, which resolves every function including those nothing calls (`zig run` analyses lazily and does not), and csharp by a Roslyn build of the file (CS0103). Five rows; zig 109 callees and csharp 92 PASS at 2026-09-25's head, python 55, javascript 49 and typescript 69 at 2026-09-24's. Every
other text plug joins by wiring its runtime into `plug-oracle-test.ps1` and
adding one row to the check's table; a language without a runtime on the box
needs an analyzer written for it.

## 2.42 -- OPEN (reek, measured 2026-09-07): the compiler lifts every lambda before IR text, so no compiled subject can reach any plug's lambda arms; hand-authored IR is the only route

The transport supports it at both ends and the plugs implement it, but nothing produces
one. `IRTextEmitter` emits `(lambda (params ...) ...)` and `IRTextParser.codex:714` parses
that atom, so the wire is capable. In fortran, `fort-collect-lams`, `fort-emit-lam-def`,
`fort-dispatch-lams`, `fort-lam-key-of` and `fort-lam-name` all exist to serve it.

Measured through `-Passes 'text-plug'`, which is what EVERY plug `run.ps1` compiles with:
zero `(lambda` in `codex/test/roc-closure-captures-list.codex` (whose source carries
`\ignored -> xs`), zero in `factorial.codex`, zero in the page's lens subject, and **zero
in the compiler's own 1,299,513-byte IR-UNI**, whose 856,125 bytes of emitted Fortran
contain no `cx_lam_` at all. Its 205 `cx_apply` arms come from `fort-dispatch-defs`,
partial application of NAMED definitions, not from lambdas.

**THE CAUSE IS MEASURED (reek, 2026-09-07): the compiler LIFTS every lambda to a named
definition before it emits IR text, and the pipeline is not the variable.** Compiled with
the DEFAULT pipeline and with `-Passes 'text-plug'`, `roc-closure-captures-list.codex`
gives BYTE-IDENTICAL IR, 2,132 bytes each, zero `(lambda` in both. Its source
`\ignored -> xs` arrives as:

    (def "__lam_0" "" (params (param "xs" ...) (param "ignored" ...)) ... (name "xs" ...))

with the site rewritten to `(apply (name "__lam_0" ...) (name "xs" ...))`. The capture
became an explicit leading parameter. So a plug is handed a partial application of a NAMED
DEFINITION, which is what `fort-dispatch-defs` serves and why the compiler's own Fortran
carries 205 such arms and no `cx_lam_`.

**The route that does reach it is hand-authored IR text.** A plug reads IR from stdin and
`IRTextParser.codex:714` accepts the `lambda` atom, so a `(lambda ...)` written by hand
reaches the arms without any compiler in the path. That is almost certainly what the
2026-08-18 probe did (`FortranEmitter.codex:524`, five lambdas, which is why the key is a
hash of the emitted body rather than a span). A grading harness for these arms should feed
IR text directly rather than compile a subject; no choice of subject or pass can do it.

**RULED (root, 2026-09-07): GRADE, never delete.** An arm is deleted only if a REACHED arm
proves the code wrong, never because it was unreached.

**DONE for fortran, and the arms are correct.** The route works and costs no guest at all,
since the module runs under `wasmtime` on the host. Take a compiled IR, replace the lifted
`(apply (name "__lam_0" ...) (name "xs" ...))` with

    (lambda (params (param "ignored" int-default)) (name "xs" (list int-default)) (fn int-default (list int-default)))

which is the shape `ir-parse-expr-lambda` reads (`(lambda <params> <body> <type>)`), and
feed it to `fortran-stdio.wasm`. It answers 15,265 bytes containing

    function cx_lam_120762235(ignored, xs) result(cx_lam_120762235_r)
        cx_lam_120762235_r = xs

with the capture as a parameter, 2 `cx_apply` arms and one `cx_fun` value. The key is a
real hash rather than 0, which is what grades the `fort-collect-lams` change: it emits
the body, hashes it, restores the heap, and only then allocates the record, so a restore
that freed something live would give a garbage key or a trap.

**The residue is byte parity for this path, and it is blocked on ENCODING rather than on
the plug.** `build/plug-run.ps1:93` writes a hardcoded mode byte 1 (IR-CCE) and that file
is generated from the shell DSL, so hand-authored IR-UNI cannot be handed to the bare-metal
plug for a whole-program comparison without a CCE encoder for hand-written text. The wasm
arm is graded by inspection above; the whole-program arm is not graded on any lambda.

Until then this is L-UNCALLED at transport scale: every lambda arm in all 45 plugs is code
no test can execute, and a change to one of them lands ungraded no matter how carefully it
is graded on everything else. That happened here, deliberately and on the record, for `fort-collect-lams`.

## 2.70 -- OPEN (latent, ungraded): riscv `__heap-advance` leaves the heap pointer unaligned

`rv-emit-heap-advance` (`RiscVCodeGen3.codex`) and `rv-rt-heap-advance`
(`RiscVRuntime.codex`) add without rounding, so an inline allocation after an
odd advance is misaligned; arm64 rounds to 8, and x86-64 does NOT round
(`emit-heap-advance-builtin` is a bare `add r10`, `heap-bump-reg`), so the
reference backend leaves the same misalignment. Rounding here therefore makes an
observable heap position (`__heap-save` after an odd advance) differ from x86's.
**Ruled (root, 2026-09-24): the contract is 8-byte-aligned advances on every
backend.** x86-64 and riscv adopt it together in one seed CL when this row's
trigger fires, and the heap-count pins move once, in that CL. QEMU riscv completes misaligned
accesses, so `codex/test/heap-advance-odd` passes on the unfixed plug and cannot
grade a fix; a core without misaligned-access support traps. The repair is
`addi 7` and `andi -8` at both sites, graded on a bed that traps misaligned
access.

## 2.57 -- the arm64 Darwin Mach-O host wrap is the contributor's, outside the tree

Deferred: no Mac, Damian 2026-09-24.

GitHub PR 144 (apoorvapendse, 2026-09-12) added `IR-CCE darwin` to the arm64
plug: `svc #0x80` `mmap`/`write`/`exit`, a slab at 8 GiB, and `WIRE-END` after
the wire. The tree carries that Codex half. Turning the wire into a signed
Mach-O (`wrap_macho.py`, `compile-macho.py`, `ld -lSystem`, `codesign`) needs a
macOS toolchain and stays in the contributor's repository: no dependency
outside Windows + codex-vm enters the build. Nothing in the tree runs a
Darwin binary, so every `darwin` byte is compile-checked only; the
contributor's M5 run of eight-queens is the only execution on record.

The virt half of the same change (`stp`/`ldp x27, xzr`) is executed: Renode
`test-cross -Arch arm64` passes `factorial`, `arithmetic`, `typeclass-poly`,
`int-add-wrapping` and `hamt-test` on the plug from main 25665, seed
49070BAEB1E31085 (2026-09-12); `hamt-test` carries 7 of the changed pairs.

## 2.66 -- BOUNDARY (COMPILER-83): a dictionary VALUE whose method is generic in its own type variable has no zig type

A class method generic in its own type variable (`runtime-value : a, b -> b`) gives the dictionary record a field still generic in `b`, and a zig struct field cannot hold a generic function outside a comptime-only struct. A projection of a generated dictionary, direct or through a `let`, is rewritten by the desugarer into a use of the instance's own method and reaches zig through MethodSpecialization slots (`method-template-contract-runtime`, `typeclass-method-local-two-types` and `-independent` are exact through both frozen readers). A dictionary that flows as a value (passed, returned, captured) keeps the by-name refusal in `emit-zig-record-fields`, shown by `method-template-contract-escape` and `typeclass-method-local-shadow`; lifting it needs a boxed generic field, which is not proposed (`docs/Designs/Done/Compiler/MethodLocalPolymorphism.md`, stage 3).

## 2.73 -- OPEN (blu measured, reek registered, 2026-09-24): arm64 cross reds at head, identical on the depot plug

blu's arm64 battery (`D:\Projects\Cobblestone-blu\test-output-cross\arm64_cross_results.md`, 16:30) and a
depot-plug A/B per subject (`%TEMP%\claude\ab-depot-<subject>.log`) give the same colour on both plugs, so
none is a regression of the lane that ran them. reek reproduced
`edge-mesh-mint` alone on QEMU after main 27702; the rest are blu's logs, unreproduced.
Grouped by shape, not by count:

- **Heap-count pins:** arm64 packs bounded-integer record fields widest-first as x86-64 does (blu,
  2026-09-25), so `desk-reuse` prints x86-64's 8224 and carries no arm64 pin. Two pins remain
  (`.expected-arm64`, ExaminersAssay), each one output over 3 runs of one plug: `wademo-bulk` retains
  200064 against x86-64's 200304 (arm64 now 240 LOWER, unmeasured), and `web-mux-heap` reads 66224 per
  connection at n=1 against 66128. `web-mux-heap`'s arm64 gap is 111 bytes per connection at n=16 (66,193 against 66,082), 112 in `recycled` and 128 in `drop-delta`, read off its two `.expected` files. **Packing took exactly 24 bytes at n=16 (66,217 to 66,193), the bound fester's x86-64 A/B set (2026-09-25):** stripping the bounds from `Tcp.codex`'s seven bounded fields moves x86-64 per-conn +16 (to 66,098) and stripping every bounded field in `codex/os/net` (14 chapters) moves it +24 (to 66,106, `recycled` 552), while `drop-delta` stays 144 against arm64's 272. The other ~111 bytes and the whole `drop-delta` gap are a different mechanism, still unmeasured, and it is not allocator alignment: arm64's `__heap-advance` and `alloc-bytes` round to 8 exactly as x86-64 does (`Arm64Runtime.codex`, `a64-rt-heap-advance`, `a64-rt-alloc-bytes`). **Named (fester, 2026-09-25, a per-step `__heap-save` probe over `fresh-listen-transport`, x86-64 against arm64 on QEMU, fresh plug AD7D3981):** `net-session-new` 288 against 464 (+176), `arp-cache-add` 80 against 32 (-48), `set-arp` 104 on both, `transport-new-with-cap` 65,592 against 65,600 (+8); net +136, the pinned `recycled` gap, and the x86-64 sum 66,064 is the pinned `discarded`. **Split (fester, 2026-09-25, seed 31356832, fresh plug 79C69C2C, x86-64 against arm64):** `net-session-new`'s +176 is the `NetSession` record +64 (a 13-word all-Integer record is 104 on both, so the +64 is its two `[]` fields), `arp-cache-empty` +32 (its one `[]`), `TcpConnection` +16 (56 against 72: the three bounded fields, packed since) and the `net-driver-mac` reference +64 (64 against 128, not split). A 4-element list literal is 48 on both. So an empty list literal is 16 bytes on x86-64 and 48 on arm64 (inferred from both `[]` arms agreeing, not measured alone): arm64 allocates a capacity-4 `[]`, and `arp-cache-add`'s -48 is the first push landing in that capacity. Next: read the arm64 empty-list emitter against x86-64's and decide whether capacity-4 `[]` is a defect or a pin; `net-driver-mac`'s +64 is unsplit. Run on a freshly built plug (`codex/plugs/arm64/build.ps1`): `test-cross` takes whatever `build-output/arm64-plug.cdx` holds. `desk-reuse` is not measured yet. Ruled out by a reverted trial: list literal capacity and frontier in-place push
  (x86-64 `emit-list-bivy`, `__list_snoc`) matched per operation and moved no pin. Latent:
  `a64-rt-list-push` keeps a capacity-0 list at capacity 0 (`cap-ok` is `b.ge`).
- **Slow, not hung:** `crypto-vectors`, `ecdsa-p384` and `ecdsa-sha384` carry `.cross-budget` sidecars.
  Every hang in this row was one of three shapes: a runtime branch emitted before its label was recorded
  (`cbz x19, #0` in `__text_to_double`); a fixture whose pointer cell is zero, because `a64-rt-remap-addr`
  maps an address below 0x1000000 to 0x40000000 + address, so a null read on arm64 returns the image's
  first word where x86-64 reads 0 (`desk-parse`); and a self-recursive loop reading a top-level constant,
  whose `bl` the TCO gate did not see (`codex/test/tco-global-bound`).
- **Plug faults at compile:** `method-template-producer-guards` needs more than 498 spill slots in one
  function and `A64Local.reg`'s bound traps it by design: the frame cliff in `Arm64CodeGen.codex`,
  "Codegen State", whose repair is a two-instruction (`LSL #12`) prologue and epilogue. That prologue is
  blu's prologue-guard claim; take the cliff after it lands.
- **Refusals, not defects:** `cpu-park` ("emits no such function"),
  `vector-f32-mask` (mask family not built), `hosted-echo`/`http`/`https` (hosted-Linux only).

## 2.80 -- OPEN (fester, 2026-09-25): arm64 data-aborts at the bit-31 value in `codex/test/ops/cap-grant-emit`

The fixture (pure computation: the x86-64 boot grant emitters driven into a 256-byte workspace, then a one-qword
x86-64 interpreter over the bytes) compiles on arm64 (plug 79C69C2C, seed 31356832) and faults on its first row
with `!A64FAULT V=4 EL=1 ESR=0000000096000050 ELR=0000000040150960 FAR=0000000080000000`: a data abort at
address 2^31, which is the mask value that row passes, so an Integer is being used as an address. ELR is
`builtins` +0x481C in the ELF map. x86-64 prints all seven rows. The fixture carries `.arch-only` x86-64 until
this is fixed; acceptance is that sidecar deleted and the fixture green on arm64 against its `.expected`.

## 2.82 -- OPEN (val, 2026-09-25): no runner grades the wasm plug's 256-bit vector refusal

The wasm plug turns every `vec8-`, `vec4d-`, `mask8-` and `mask4d-` name, and any vector arithmetic or
comparison wider than 16 bytes (`wat-vec-is-wide`), into a token wat2wasm rejects
(`codex-refused-<name>-256-bit-vector-on-wasm`), so the module fails to assemble. `hosted-wasm-test.ps1`
has no refusal sidecar, so nothing observes that token appearing (L-NOGATE); ARM64 and RISC-V are graded by
`codex/test/vec256-cross-refused.cross-refusal`. Acceptance: a wasm arm that fails when the refusal is
removed.
