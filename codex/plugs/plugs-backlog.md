# Plugs -- open capabilities

Quire-domain backlog, same rules as the app registers: an entry says what is
still missing and nothing else, a closed entry is DELETED, and a gap that is
still real is never quietly dropped. `docs/PM/CurrentPlan.md` carries the
shape. **The depot is the record of what was done; this file is only what is
left.**

## Standing hazards

**A plug that does not handle a construct usually EMITS SOMETHING ANYWAY and
reports OK.** A missing builtin arm passes the name through as an ordinary
call; a wrong field spelling emits a division; a wrong `list-push` emits a
mutating append. For most of these plugs nothing downstream ever runs, so
silence is silence, not agreement (L-GAP).

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

## Open

**1.1 -- lift the plug type reconstruction into shared code. DEFERRED**
(Damian, 2026-08-05): a de-risking rehearsal, not a prerequisite. Group-3
sites are `clamp-field-val` (csharp), `a64-field-type-for-store`,
`rv-find-field-type-st`, `a64-collect-field-types`, `rv-collect-field-types`,
`rc-check-ctor-ref-sum`, and the python and javascript clamp paths.

**1.3 (residue) -- the general RISC-V temp-collision defect either side of a
frameless binop is open** (fester). `RiscVCodeGen.codex:1880-1884` records
that the frameless literal-operand fix is not the general fix.

**1.53a -- the reservation fix TRADES peak memory on a fully-touched
reservation, and my CL 18594 cost note was wrong to say otherwise.** That note
said "strictly less of both". Measured after red asked the right question:
a 200 MB reservation written across at stride 4096 peaks at **298 to 342 MB**
with the fix and **156 to 200 MB** without it, three runs each, both exiting 0
with identical output. The old code grew once to exactly N; the new one grows
incrementally and the arena never frees, so each geometric realloc leaves the
previous buffer behind. The factor is bounded at about 2x by the growth
schedule and it is not a failure.

It remains the right trade by a wide margin -- reserve-and-touch-little goes
from 2,810 MB to 10 MB, which is the case `act-tco-loop` and any
reserve-then-fill program is in -- but the claim to make is "much less in the
common case, bounded more in the worst case", not "strictly less".

The leak that sets that 2x is its own item, 1.54, not this one's to carry.

**1.54 (residue) -- `cx_heap` is off the arena and the touch-everything branch
is NARROWED, not closed.** `cx_buf_want` now grows the buffer through
`std.heap.page_allocator`, so a realloc releases what it replaces; everything
else stays on `cx_gpa`, where never-freeing is the point. Two runs each,
polling sampler, same three programs throughout:

| arm | before 18596 | 18596 (arena) | now |
|---|---|---|---|
| reserve 3.1e9, one write | 2,952 MB / 630 ms | 10 MB / 79 ms | **6 MB / 18-34 ms** |
| touch 200 MB at stride 4096 | 200 MB / 57 ms | 338 MB / 85 ms | **294 MB / 148 ms** |

**The residue is transient COPY cost, not retained garbage, and that is why
this did not reach 200 MB.** Each growth allocates the new buffer, copies, and
only then frees the old, so both are live at the moment of the copy. The
arena's extra ~44 MB was genuine retention and is gone; what is left is
inherent to a copying grow.

**IT ALSO COSTS TIME ON THAT ARM: 85 ms to 148 ms.** `page_allocator` takes a
fresh mapping per growth where the arena could sometimes extend in place. It
is the right trade because memory is what fails and 148 ms for 200 MB is not,
but it is a real cost and is not hidden.

**What would actually close it:** reserve address space and commit on demand,
so growth never copies. That is a custom allocator over `VirtualAlloc` and
`mmap` and is a larger change than either of these rows.
**1.56 -- the csharp plug emits XOR for integer exponentiation.** Steve
Howell's aside on PR 76, verified here 2026-08-21 against the source:
`CSharpEmitterExpressions.codex:984` maps `IrPowInt` to `"^"`, and nothing
intercepts `IrPowInt` before `emit-bin-op`, so line 1005's `otherwise` arm
emits `(l ^ r)`. In C# `^` on integers is XOR, not exponentiation. The
sibling plugs are the control and they are right: `wpf`, `winforms` and
`java` all go through `Math.Pow`, so csharp is the outlier rather than the
house style. `2 ** 10` answers 8 there and 1024 on bare metal.

**Its aside about python and javascript is NOT verified and is recorded as
his claim, not as a measurement**: that the python plug emits `+` on
unbounded ints with no 64-bit mask and so diverges silently past the word,
and that javascript is worse because f64 loses exactness past 2^53. Read
the emitters before acting on it.

**Neither has a runner, and that is the actual gap.** `plug-oracle-arith`
has no overflow row, measured 2026-08-21 by ablation: putting a plain `+`
back on the zig plug's `IrAddInt` still passes the oracle 49 of 49, so the
oracle cannot see wrapping in any plug. An overflow row would catch this
whole class at once and is a gate-weight change, so it is red's call rather
than a thing to add here (Steve offered to propose one).

**1.14 -- deep recursion is not free on a stack language.** What remains is
measurement when a runtime appears. Establish each plug's class by ABLATION,
not by the language's reputation: python looked like a C-stack limit and is a
counter, one line to raise.

**1.20 (residue) -- the pascal record type.** No Free Pascal toolchain on this
box (`fpc`, `ppcx64`, `lazbuild` absent), so anything here is reviewed by
reading. Two traps for the next reader: `WriteLn` and `Halt` are PROCEDURES,
so `Result := WriteLn(...)` does not compile, and the entry wrapper must emit
`opening;` or it prints an Unassigned Variant after the real output.

**1.29 -- three ARM64 load-address constants are stale, and one has a reader.**
The reader is `arm64-build-elf`, a second ELF builder nothing invokes, sitting
beside the PowerShell one the cross bed actually uses. By this row's own rule
that is a deletion of the BUILDER, which is a bigger call than a constants row
should make alone.

**1.33 -- there is no DECK on riscv** (blu), so nothing can be made to outlive
a `__heap-restore` there. Three of the five arm64 arms are done; the riscv
side returns its SIZE argument or a literal 0. Latent: `__deck-alloc`
returning a size where the caller wants a pointer.

**1.39 -- cobol is BLOCKED on its toolchain.** All five stages landed; `cobc`
is absent and Damian's standing rule is that no new build environment is
installed now, so every claim in the CLs is read against the language rather
than run. Next step, when that rule lifts: install `cobc`, then run the
subjects.

**1.41 -- a per-byte receive accumulate costs 116.77 s per 16 MB**, and the
shape is still in several harnesses. The end-to-end measurement was not chased
because the remaining ones need their own understanding first.


**1.46 (residue) -- the text plugs are not wired to the oracle, and cannot
be until the no-new-toolchains rule lifts.** Six are wired (python,
javascript, typescript, zig, wasm, csharp) and every one of those had its
runtime already on the box. Measured 2026-08-21 across 52 executable names
covering every remaining emitter -- ruby, perl, php, lua, java, go, rustc,
scala, kotlin, swift, ghc, ocaml, clojure and the rest, plus the alternate
spellings (`clj`, `luajit`, `ldc2`, `runghc`, `guile`, `racket`) -- and the
only one present is `nvcc`, which compiles ptx device code rather than
running a console subject. So the remaining plugs are not unwired for want
of the wiring: there is nothing on this box to run what they emit, and
Damian's standing rule is that no new build environment is installed now.

This row is BLOCKED for the same reason as 1.39, not merely open. Anyone
picking it up should check `Get-Command` for the language first; if a
runtime has appeared, the wiring itself is one entry in the `$Plugs` table
in `build/plug-oracle-test.ps1`, which is blu's claim.

**1.48 -- the mov-elimination peephole is unsound in the general case.**
`a64-peephole-mov-elim` folds `mov Rd, Rm` into the preceding instruction
whenever that instruction's `Rd` matches, which is sound only while the
preceding instruction runs on every path reaching the mov. The guard is in;
the general case is not. `br` is the standing gap: an indirect branch carries
no target in its encoding, and this lane emits none today.

**1.57 -- `riscv` and `java` do not handle over-application of a named
definition, and riscv's correct fix is in the tree with no caller.**
From the zig-plug ladder (`contrib/README.md`), 2026-08-24.
`docs/DevelopersRulebook.md:256-260` requires a plug that knows the
callee's arity to handle three cases -- flat at that arity,
under-applied with one arrow per missing parameter, over-applied by
applying the rest. The rule is unqualified: it binds "a plug", and names
the TS/JS family only as plugs that already carry the model. Three plugs
implement two of the three.

**riscv has the fix and does not call it.** The named-definition path
(`RiscVCodeGen2.codex:583-591`) tests `list-length args < known-arity`
and routes to `rv-emit-partial-application`; every other case,
`args > known-arity` included, falls into `rv-emit-direct-call` with the
whole argument list. Seventy lines below, `rv-emit-closure-over-apply`
(`:660-668`) is a correct take/drop over-apply, and
`grep -rn rv-emit-closure-over-apply codex/plugs/` returns exactly three
hits: its signature, its definition, and its own self-recursive tail.
Nothing reaches it.

**java never consults arity at all.** `JavaEmitter.codex:158-168` emits
`func & "(" & emit-jv-apply-args args ... & ")"` for both the `IrName`
root and the `otherwise` root. `lookup-arity` is defined at `:69-70` and
has no call site in the file.

**arm64 is a near miss, not a defect.** It has
`a64-emit-oversaturated-call` (`Arm64CodeGen2.codex:927-932`) reached
from `:980-981`, but the arity it consults is `a64-known-arity`
(`:901-915`), a hardcoded table of builtin names, so it does not fire
for user definitions. Its local-closure path (`:976-978`) does use a
real def-arity table.

The compliant plugs do it two ways, either of which is a template:
`csharp` (`CSharpEmitterExpressions.codex:830-841`), `python`
(`PythonEmitter.codex:646-655`), `javascript` (`:501-511`) and `rust`
(`RustEmitter.codex:547-560`) route every non-exact case to a curried
spine, so over-application is correct by construction; the TS family
(`TypeScriptEmitter.codex:205-214`) splits on `args > ar` with
take/drop, as does the compiler's own x86-64 back end
(`X86_64Compound.codex:154`, arity map built at `:38` from
`list-length (d.params)`).

**What is measured and what is not.** The same gap in the zig plug is
observed end to end: `((even-fn 4) 20) 22` against a one-ary definition
emits `even_fn(4, 20, 22)` and zig refuses it at compile time with
`expected 1 argument(s), found 3`. That one is the ladder's to fix and
is not this row. For riscv and java this entry offers the dispatch code
and the grep, NOT an observed miscompile, and the reporter is not going
to supply one -- **this wants verifying on the depot side, where the
toolchains are.** Per this file's own standing hazard about name
censuses, treat the runtime consequence as inferred from the emitted
shape until a subject has been run through both plugs and the output
read. Concretely, what would settle it: over-apply a NAMED top-level
definition that returns a function, emit Java, and check whether the call
site names a method the same file declares with fewer parameters. The
ladder host has no JDK and installing one is not its call, so the row is
deliberately filed as a source-level report rather than held back until
someone can run it. Note what would and would not catch it if someone
did: `test-plugs.ps1` asserts non-empty text with markers and never
COMPILES what a plug emitted, so it cannot detect this in `java` however
often it runs, and by its own prose it does not drive `riscv` or `arm64`
at all -- the native backends take `-IrInput` and emit the binary wire
protocol, so they "fail parameter binding and exit 1 in under a second
having done no work at all" and are deliberately absent from its plug
list.

**Why none of it was caught, which may be the cheaper half.**
`codex/plugs/test-input/partial.codex` exercises under-application
(`let g = add3 1 2`), saturation (`add3 1 2 3`) and over-application of
a LOCAL (`let h = add3 10 in (h 20) 12`), but its only definition is
`add3 : Integer, Integer, Integer -> Integer`, which does not return a
function. Nothing in the corpus over-applies a NAMED top-level
definition, so the branch all three plugs get wrong is unreachable from
it. `codex/plugs/test-plugs.ps1` then judges exit code,
non-empty output and text markers (`:93-97`, `:163-177`) without ever
compiling what it emitted. One added definition in `partial.codex` would
put all of these in front of a compiler.

**The ask is one ruling:** whether over-application of a named
definition is required of every plug that keeps an arity map -- in which
case riscv wants its dead function wired up and java wants an arity
check -- or whether some plugs are exempt, and `:258` should say which.

**1.61 -- no `run.ps1` consults the VM host selection in the config it
sources, so no plug can be run on Linux.**
From the zig-plug ladder (`contrib/README.md`; the repository is
https://github.com/showell/codex-zig-ladder), 2026-08-25, measured on a
Linux host against the tree at git `0c4327d5`, the Update 50 interim
push. Not a plug defect -- it is how every plug reaches a VM.
(1.58-1.60 are claimed by ladder branches in flight.)

**The contract.** `build/vm-config.ps1:14-16`: "codex-vm (the WHP
hypervisor) is the primary and is Windows-only; QEMU is the fallback and
the only host on Linux/WSL. Both paths are live: a missing codex-vm is
not an error, and the hard failure is reserved for having NEITHER host."
The file implements exactly that -- `:21` chooses (`$script:UseCodexVm`),
`:22-52` discovers a QEMU, `:55-56` is the error for having neither.

**No `run.ps1` reads any of it.** Across all 56,
`grep -lni 'qemu|UseCodexVm|Start-VmRun|FallbackVmBin' codex/plugs/*/run.ps1`
returns nothing. They divide three ways:

- **38 delegate to `build/plug-run.ps1`**, which hardcodes
  `$vmBin = Join-Path $Repo 'tools\codex-vm.exe'` (`:49`) and launches it
  (`:53`). No fallback; the string `qemu` does not occur in the file.
- **8 hardcode the same path themselves** -- `wasm:50`, `html:46`,
  `spirv:43`, `t3isa:43`, `winforms:40`, `ptx:39`, `wgsl:39`,
  `evidence:117`.
- **10 use `$script:CodexVmBin` from the sourced config** -- `riscv:54`,
  `csharp:81`, `javascript:47`, `maui:65` and six more. These read the
  config's PATH variable and skip its CHOICE variable, so they look like
  they consult it.

**What a Linux user gets.** With the zig plug built and a real IR
(`build/plug-run.ps1 -IrInput hello.ir -PlugCdx zig-plug.cdx -MemMB 3072
-Port 9145`):

    [plug-run] IR input: 1481 bytes
    [plug-run] Plug: codex/plugs/zig/build-output/zig-plug.cdx
    [plug-run] Listening on TCP 9145
    plug-run.ps1: The variable '$proc' cannot be retrieved because it
                  has not been set.
    exit 1, one second

**The message names the wrong thing, and this half is cheap.**
`$ErrorActionPreference = 'Stop'` (`:20`) makes `Start-Process` on a path
that does not exist a terminating error at `:53`, so control leaves the
`try` for the `finally` at `:167` without reaching `:59`. There `:168`'s
`if (($proc -and (-not $proc.HasExited)))` reads a variable that was
never assigned, and under `Set-StrictMode -Version Latest` (`:19`) that
throws -- and an exception raised in a `finally` replaces the original.
Reproduced in isolation with those four lines and nothing else. The real
cause is never printed, and `vm-config.ps1:55-56` cannot print it: its
condition is having NEITHER host, and on this box QEMU is present.

**`build/compile.ps1` is the shape of the fix.** It hardcodes the same
binary at `:209`, but `:218` is `if ($script:UseCodexVm)` -- the line the
plug scripts are missing -- and `:239` falls back to
`Invoke-VmCompileFallback` (`vm-config.ps1:821`). Measured on the same
box: `codex/plugs/test-input/hello.codex` to IR-CCE in four seconds,
inside a `qemu-system-x86_64 ... -m 3072` guest.

**Against the design record.** `docs/Designs/Active/Build/Build.md:699`
says of the non-delegating plugs "The remaining 17 are deliberately
untouched and are not a residue to close", which stands for what it is
about -- delegation, not hosting. This gap is orthogonal and cuts across
all three groups above. (That census also reads 55 plugs and 17; the
tree now has 56 and 18, the difference being `evidence`.) The mechanism
is already recorded two paragraphs earlier, at `:684-694`: both
generated scripts "carried defects that survived because a generator
with no live target is compiled but never compared against anything".
This is another one, and the live target it lacks is a Linux run.

**The transport is the part that is not a copy-paste, and there is a
working recipe.** Under codex-vm the guest dials the host's TCP listener;
the QEMU consumers in `vm-config.ps1` read the serial wire instead
(`:835` says so), so `Invoke-VmCompileFallback` is not a template. The
ladder runs this transport daily against the zig plug: it listens on
9145 and lets the guest dial out, booting with user-mode networking --
`-netdev user,id=net0 -device
ne2k_isa,netdev=net0,irq=9,iobase=0x300,mac=52:54:00:12:34:56`. The
sources are `plug_run.py` and `codex_vm.py:43-49` in the ladder
repository named above. Take them or ignore them.

**To reproduce**, on a Linux host with `qemu-system-x86_64` and pwsh:
`pwsh -NoProfile -File codex/plugs/zig/run.ps1 -Src
codex/plugs/test-input/hello.codex -Out /tmp/hello.zig`. Note that this
fails one step earlier still, in one second, because `run.ps1` calls
`compile.ps1` with no `-Kernel` and the checkout has no
`build-output/bare-metal/Codex.cdx`; pass `-Kernel seed/Codex.cdx` to
`compile.ps1` directly to get past it.

**The ask is one ruling: is Linux a supported host for RUNNING plugs, or
only for building them?** If it is, `plugrunScript.codex` wants the
choice `compile.ps1` already makes, and the other 18 want it too. If it
is not, the fix is smaller and different: say so where
`vm-config.ps1:14-16` currently says the opposite, and print the real
error instead of a strict-mode complaint.

**Hedges.** We have not tried a fix on Windows and cannot.
`build/plug-run.ps1` is generated, so any change belongs in
`codex/build/plugrunScript.codex` and not in the file quoted here --
which is why this entry carries no patch. And we cannot say whether
anyone runs plugs on Linux today: the ladder does not, because it wrote
its own transport rather than wait for this one.

**babbage is SHELVED** (Damian, 2026-08-21): vanity work. Its open items
moved to `codex/plugs/babbage/babbage-backlog.md`. Do not add babbage items
here.
