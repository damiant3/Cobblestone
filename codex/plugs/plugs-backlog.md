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
`codex/plugs/common/IRTextParser.codex:705` builds `IrApply` structurally,
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
believing any assembly timing.

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

**CENSUS THIS PLUG'S REFUSALS WITH ALL THREE SPELLINGS AND OVER THE WHOLE
FILE:** `no wasm form for|has no form on this target|wasm plug:`. There is no
single string that finds them all, and line-scoped matching misses them because
an emitted line runs to thousands of characters. Measured: in one graded run
`apps/cam-capture` carried 4 of `no wasm form for` and zero of `wasm plug:`
while `apps/console-test` carried the reverse, so a census with either pattern
alone reports the other subject as clean. The first pass over the default 60's
eight reds found two subjects and missed six, and the six were the interesting
ones.

**A GROWING BUFFER IS SOUND IN A READ LOOP AND NOT IN `$list_push`; DO NOT
REPAIR ONE BY ANALOGY WITH THE OTHER.** Both lean on consecutive `$bump_alloc`
calls being contiguous. In a read loop nothing else allocates and the buffer is
not yet anyone's value, so `$read_serial_cce`, `$read_file_uni` and
`$read_file_raw` double in place safely (the cap is 1 MiB, not the 4 MB this
register once claimed, and nothing is dropped). In `$list_push` the block being
extended is a list a CALLER still holds, which is the defect COMPILER-42
records.
## Open

Nothing here is both open and takeable on this box without a ruling or a
toolchain, so a lane arriving for the next entry in register order should read
this list and go elsewhere. Rows keep their original numbers.

**WE DO NOT DO TOOLCHAINS** (Damian, 2026-09-07, in those words). This is not
a rule waiting to lift and it is not a question to put to him again: a target
whose runtime is not already on this box is verified by READING what it emits,
permanently. Rows 1.14, 1.20, 1.39 and 1.46 are closed under it rather than
blocked by it, and 1.59's four no-closure targets are in the same position.
Do not open an item whose acceptance step is "install X, then run the
subjects".

**2.33 -- kotlin and scala emit `IrPowInt` as an infix operator that neither
language has. CLOSED 2026-09-07 (red).** Neither language has a Long power
operator, so both plugs now emit `_cx_ipow`, a square-and-multiply helper in
the prelude beside `_cx_text_to_integer`, matching `__ipow` in
`codex/compiler/Emit/X86_64TextHelpers.codex` including its wrapping product
and its 0 for a negative exponent. `kt-bin-op-symbol`'s `"pow"` and
`sc-bin-op-symbol`'s `"^"` are now unreachable, as csharp's already was.
Graded by reading the emission, like every row under the toolchain rule.

**2.34 -- CLOSED (reek, 2026-09-07): ten plugs repaired, qt reclassified to 2.43,
cobol to 2.44. An integer power routed through a double is wrong above 2^53. Found
2026-09-07 (red) while fixing 2.33.** `__ipow` (`codex/compiler/Emit/X86_64TextHelpers.codex`) is
square-and-multiply on 64-bit integers, so the reference answer is exact;
a `Math.pow`/`math.pow`/`**`-on-Number path carries a 53-bit significand and
returns 16677181699666568 for `3^34`, one short of 16677181699666569. Read
2026-09-07: 11 plugs route it through a double `pow` (compose, java, go,
swiftui, qt, wpf, winforms, maui, csharp, clojure, flutter) and 8 more apply
`**` to a JavaScript Number, which is the same double (javascript, typescript,
react, vue, svelte, angular, electron, html). Those two lists are by SPELLING
and the classification is the work, not the finding (L-CENSUS): `**` is exact
in python, ruby and groovy, so the operator does not decide it, the runtime's
numeric tower does. The repair where it is wrong is the one 2.33 took: a
`_cx_ipow` helper in the prelude, matching `__ipow` including its wrapping
product and its 0 for a negative exponent. Not gradeable by running, under
the toolchain rule; graded by reading the emission.

**CENSUS BY BEHAVIOUR, reek 2026-09-07, taken over every `is IrPowInt` arm in
the tree (47 plugs, 62 sites) rather than by spelling.** The 11 double-`pow`
plugs are confirmed and are the unit: compose, java, go, swiftui, qt, wpf,
winforms, maui, csharp, clojure, flutter. Three corrections to the lists above:

- **gtk is NOT a case, though it emits `(lhs ** rhs)`.** It targets PYTHON
  (PyGObject) and `GtkEmitter.codex:118` says so in its own words, "its
  integers are arbitrary precision". The `**` spelling put it beside the JS
  eight; the numeric tower takes it out again.
- **flutter is a JUDGEMENT, not a confirmed member.** It emits
  `pow((l as int), (r as int)).toInt()`, and whether Dart's `pow` is exact for
  two `int` arguments is a question about Dart's numeric tower, the same
  question that acquits python and gtk. Settle it before repairing it.
- **cobol is a CANDIDATE the 19 do not name.** `CobolEmitter.codex:628` emits
  `COMPUTE <result> = <l> ** <r>`, COBOL leaves exponentiation free to be
  evaluated in floating point, and the target field is `PIC S9(18)`, which runs
  past a double's exact range at 10^16. Untestable under the toolchain rule; it
  wants a reading of the standard rather than a run.

Correct already and needing no repair, verified at the arm: ada (`** Natural`),
fortran (integer `**`), rust (`.pow`), haskell (`^`), python, ruby, groovy,
zig (`cx_ipow`), wasm (`call $cx_ipow`), kotlin and scala (2.33).

**2.34 IS CLOSED (reek, 2026-09-07): ten repaired, one reclassified out.**
Tranche 1 java, wpf, winforms, maui (reek 23051, main 23054); tranche 2 csharp,
compose, go, swiftui, clojure (reek 23093, main 23095); flutter (reek 23098,
main 23102). qt is not a member and gtk never was. The residue left this row as
two new rows: 2.43 for the JS-number targets and 2.44 for cobol.

flutter was the census's open judgement and is settled by making the question
moot: `_cx_ipow` is exact whichever way Dart's `pow` behaves, so the repair rests
on no unverifiable claim. Dart VM ints are fixed 64-bit and wrap, matching
`__ipow`. Zero bare `pow(` calls remain in its emission.

Each graded by reading its module's
emission of `codex/test/ops/int-pow.codex`: no double `pow` of any spelling,
`_cx_ipow` present, exit 0. The helper is NOT one shape: csharp had no runtime
helper block at all and needed `cs-ipow-runtime` emitted after
`emit-class-header`; swift needs `&*` because `*` traps; clojure uses
`unchecked-multiply`; go keeps `math` imported because `Float64frombits` and the
trap builtins still use it.

**qt IS NOT ONE OF THE ELEVEN, and this is the SECOND plug the spelling split
misfiled.** It emits QML JAVASCRIPT (`function __narrow(n) { ... }`,
`outputModel.append`), so its numbers are IEEE doubles and a JS
square-and-multiply is no more exact above 2^53 than `Math.pow`: the
MULTIPLICATION is the lossy step, not the call. qt belongs with the eight
`**`-on-a-Number plugs and wants the answer they want, which is BigInt rather
than this helper. gtk was the first misfiling, in the other direction.

**The residue left this row as 2.43 and 2.44.**

**Waiting on a ruling:** 1.57 (whether over-application of a named definition
is required of every plug that keeps an arity map), 1.97 (the effect-op table's
environment pointer).

**Ruled latent or deferred, not to be reopened without the ruler:** 1.1
(Damian, deferred), 1.48 (red, latent), 1.54, 1.72.

**Another lane's:** 1.33 (blu), 1.96's upstream half (COMPILER-30, open in
`codex/compiler/compiler-backlog.md`).

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

**1.101 -- nine plug run drivers compile their subject with no `-Kernel`, so
the subject is built by whatever is lying in `build-output/bare-metal/`.**
`compile.ps1` falls back to that path, which holds whichever compiler ran last,
and a run driver that takes the fallback grades its plug against an unknown
compiler. Measured 2026-09-08 (fester) over every `compile.ps1` invocation
under `codex/plugs`: 71 sites, 54 pass an explicit `-Kernel`, and nine do not
and have none in scope. They are `csharp/run.ps1:58`, `html/run.ps1:24`,
`javascript/run.ps1:46`, `maui/run.ps1:41`, `ptx/run.ps1:20`,
`t3isa/run.ps1:25`, `wasm/build-designer.ps1:54`, `winforms/run.ps1:21` and
`wpf/run.ps1:42`. Every one of those files DOES name `-Kernel` elsewhere, for
`Invoke-PlugVmFileSerial`, which is the plug VM and not the compiler, so a
grep for the flag says the file is fine when the compile is not; that is the
tell to distrust here. The plug BUILD is already correct:
`common/plug-build-lib.ps1:163` passes the depot seed, which is what
`PlugDeepRecursion.md`'s retraction asked for. Not fixed here, because a run
driver may want the SUT rather than the seed and which one each wants is a
per-driver decision, not a sweep.

**1.14 -- deep recursion is not free on a stack language.** The wasm half is
CLOSED (`return_call`; the module self-compiles at a 0.5 MB worker stack and
dies at 0.25 MB). What remains is every other runtime's class, established by
ABLATION rather than by the language's reputation: python looked like a C-stack
limit and is a counter, one line to raise. Non-tail depth is a real frame
obligation on every conventional target, wasm included.

**1.20 (residue) -- the pascal record type.** No Free Pascal toolchain on this
box (`fpc`, `ppcx64`, `lazbuild` absent), so anything here is reviewed by
reading. Two traps for the next reader: `WriteLn` and `Halt` are PROCEDURES, so
`Result := WriteLn(...)` does not compile, and the entry wrapper must emit
`opening;` or it prints an Unassigned Variant after the real output.

**1.33 -- there is no DECK on riscv** (blu), so nothing can be made to outlive a
`__heap-restore` there. Three of the five arm64 arms are done; the riscv side
returns its SIZE argument or a literal 0. Latent: `__deck-alloc` returning a
size where the caller wants a pointer.

**1.39 -- cobol is verified by reading, and that is the final state.** All five
stages landed; `cobc` is absent and is not going to be installed, so every
claim in the CLs is read against the language rather than run. There is no
next step.

**1.41 (residue) -- two dead harnesses on the codex-vm path.** The per-byte
accumulate is fixed at every live site, and codex-vm now refuses an
unrecognised argument (L-ACCEPTED). What is left is dead code nobody owns:
**CLOSED by deletion.** `codex/plugs/elf/extract-x86-output.ps1` was dead in
BOTH halves (it passed `-data-port`, which codex-vm never parsed, and sent an
`ELF` mode header the compiler does not have, so it got echoed source) and went
at main 21478; `tools/test-codex-vm.ps1`, which passed the same two flags and
invoked a path from before the restructure, went 2026-09-07. Reviving this plug
means writing a producer, which `codex/plugs/elf/run.ps1` already says.

**1.46 (residue) -- the text plugs are not wired to the oracle and will not be.**
Six are wired (python, javascript,
typescript, zig, wasm, csharp) and every one of those had its runtime already on
the box. Measured 2026-08-21 across 52 executable names covering every remaining
emitter: the only one present is `nvcc`, which compiles ptx device code rather
than running a console subject. The wiring itself is one entry in the `$Plugs`
table in `build/plug-oracle-test.ps1`, which is blu's claim.

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

**1.57 -- THE RULING ASK: is over-application of a named definition required of
every plug that keeps an arity map**, in which case each wants its arity check,
or are some plugs exempt, and should `docs/DevelopersRulebook.md:258` say which?
The java and riscv halves are fixed and the riscv site is
`RiscVCodeGen2.codex:593`, the `is otherwise` arm, which dispatches on the
APPLICATION'S RESULT TYPE rather than the callee's.

**Two pre-existing riscv reds at head, and NOTHING RUNS EITHER (L-NOGATE):**
`codex/test/ops/saturated-call-returning-function` produces no output at all,
dying before its first line, and it is the canonical nine-arm test for this very
feature; its first statement is a two-level let-bound closure chain, which is
NOT 1.57's site, so it is a separate defect and wants its own row.
`codex/test/closure-under-apply` fails from `split-one-at-a-time` onward.

**1.59 (residue) -- four targets cannot express the over-application case at
all.** 41 of 45 subject plugs are correct on both halves. ada, cobol, pascal and
babbage have no closures (ada emits the literal `null` for a lambda, cobol emits
nothing), so none can express a definition that RETURNS a function; **the repair
is lifting, a design rather than a branch**, and they fail by NAME with that
reason rather than through a shape heuristic.

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
this row.** `is-self-call-root` (`PythonEmitter.codex:665`) compares the chain's
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
argument.** The selection is honored everywhere (`Start-PlugVm` and
`Invoke-PlugVmFileSerial` in `vm-config`), and `plug-smoke` asserts the two
hosts agree BYTE FOR BYTE. What is not measured, kept apart from what is:

- Proven on both hosts: python, javascript, csharp, typescript, wasm, ptx, wgsl
  and plug-run's own arm. The other 48 take the same two helpers, which is an
  argument and not a measurement. **elf, img and pe additionally need a binary
  wire fixture rather than a source file**, and recheck and wpf were not run.
- **The port collision is UNTOUCHED and it is now the binding limit on running
  one plug twice at once.** Corrected 2026-09-07: `build/plug-run.ps1`'s `$Port`
  default of `9100` is not the shape, because **38 plugs pass their own fixed
  port** (python 9131), so ports are unique per plug and shared across
  concurrent runs and across workspaces. A second run of one plug refuses at
  the listener naming the port and L-SHARED, which is a loud failure rather
  than a silent one, and it fires before 2.26's scratch could cross. Only the
  MASKING was fixed, so the real port error surfaces instead of a StrictMode
  complaint; deriving the port per run is what would let two coexist.
  A red in this phase is worth one re-run before it is believed, and the re-run
  is 40 s. The discriminating step for the next flap: read the error and see
  whether it names the port.
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

**1.97 -- OPEN, and it is a design: a handler clause that captures a local other
than `resume` cannot be compiled by the native plugs.** A clause closing over an
enclosing local produces a lifted def with extra capture parameters, and there
is nowhere to put them: a handler is installed in the effect-op table and called
with the operation's arguments only, so the plug cannot carry a closure to it.
Both plugs refuse it now. **Closing it properly means giving the effect-op table
an environment pointer.** No test is pinned, because the bed has no program of
this shape, which is why it was never noticed.

**A SECOND finding on that row, with a live reproducer and wider than it.** A
valid program whose clause captures an enclosing local
(`offset-by (n) = let r = with Reader ask / ask (resume) = resume (n + 1) in r`)
runs correctly on bare metal, answering 42. On the same IR, **arm64 REFUSES with
`[UNSUPPORTED] n: the arm64 plug emits no such function` and exits 6, while
riscv emits 49,473 bytes and exits 0.** The capture reaches the handler as a
free name, and the asymmetry is not the clause path at all: arm64 has an
unresolved-name refusal and riscv does not.

**1.98 (residue) -- `-Measure` swallows the CDX9002 it should report.** At the
derived scale the arm64 and riscv bundles refuse with `CDX9002: Deck overflow in
CHECK`, and the overflow aborts CHECK before any DECK record is written, so the
measure log is empty. `compile.ps1 -Measure` ends at `PHASE-h-post-emit` with
`EMIT-BYTES:0` and not one `error CDX` line, while the same bundle compiled
normally prints CDX9002 at once. `deck-headroom.ps1 -MinMargin` now FAILS on a
unit with no deck records rather than skipping it, so the gate is no longer
blind; what is unfixed is `-Measure` reporting neither the records nor the
diagnostic.

## 1.90 -- CLOSED (reek, 2026-09-07): arm64 sum equality never reached the fields at all; the repair lands with an arm that is red in both directions

**Found 2026-08-27 (blu) while re-establishing the arm64 baseline for
COMPILER-9, and it is a WRONG ANSWER rather than a refusal, which is why
nothing surfaced it for as long as it has existed.** `codex/test/recursive-eq`
compiles clean on arm64 and prints `ne` where `eq` is expected, on the first
of its eleven rows.

**Measured**, arm64 cross bed, `build/test-cross-batch.ps1 -Arch arm64`:
`recursive-eq  line 1: exp=[eq] act=[ne]`. That test is x86-64-correct on all
eleven rows against seed `555791DA1F39A810` (COMPILER-24, main 20018).

**The structural cause is read off the emitter, not inferred from the
symptom.** `a64-emit-sum-eq` (`codex/plugs/arm64/Arm64CodeGen.codex:1164`)
compares the tag with `arm64-cmp`, then loads each field with `arm64-ldr` at
`+8` and `+16` and compares it with `arm64-cmp` as well. There is no dispatch
on the FIELD's type anywhere in it: no `__str_eq` call for a Text field, no
call for a nested sum, no recursion. x86-64's inlining path calls
`emit-eq-op` per field (`emit-sum-fields-eq`) and therefore does dispatch.
So a field holding a POINTER is compared as a pointer, and two structurally
equal values at different addresses answer unequal.

**Three consequences. (1) and (2) are measured; (3) is read off the same lines
and is NOT measured, so do not quote it as a result.**

1. A field at a recursive sum compares by pointer -- the measured case.
2. **A `Text` field of ANY sum, recursive or not, should compare by pointer
   too**, so `Held "hi" == Held "hi"` is predicted `ne` on arm64 and is `eq`
   on x86-64. This is the one worth testing first: it needs no recursion and
   it is a divergence on an ordinary shape.
3. `a64-max-fields-for-type` caps the unroll, and the emitter has arms for
   0, 1 and 2-or-more fields where the last compares exactly fields at `+8`
   and `+16`, so **a constructor with four or more fields appears to compare
   only its first three**.

**Not fixed here, and the x86-64 repair does not carry over**: COMPILER-24
synthesises a per-sum helper as an ordinary `IRDef` inside the x86-64
emitter, so arm64 and riscv never see it. Answering (1) on arm64 means the
same synthesis on that plug or, better, lifting it to a shared IR pass where
all three targets get it at once. Answering (2) is smaller and independent:
dispatch the field compare on the field's type the way `emit-sum-fields-eq`
does. **riscv is UNMEASURED for all three.**

**(2) IS NOW MEASURED, on emitted arm64 code and with no Renode boot** (reek,
2026-09-07). Subject: `Held = | Empty | Hold (Text)`, with `Hold "hi" == Hold
"hi"` and `Hold "hi" == Hold "no"` as its only comparisons, through
`codex/plugs/arm64/compile-arm64.ps1`. Disassembling `opening` (0x40103B38, 220
bytes) for BL instructions gives exactly two targets, both `print-line`. **It
never calls `__str_eq`**, so the Text field is compared as a raw word. The
emitter census agrees and is the structural half: `__str_eq` appears at exactly
two sites in the whole of `Arm64CodeGen.codex` (`:1330`, `:1342`), both in the
TOP-LEVEL `IrEq`/`IrNotEq` text arms, and none inside `a64-emit-sum-eq`, whose
body loads `+8` and `+16` with `arm64-ldr` and compares with `arm64-cmp`.

**`__str_eq` IS in the emitted map and that is not evidence of a call**
(L-UNCALLED): the runtime links wholesale, 101 symbols including
`__process_resume`, which this subject cannot reach. Only the BL scan answers
it.

**What the raw compare actually does is sharper than "wrong", and it decides
how to write the arm:** the answer depends on whether the two identical string
literals are POOLED to one address, not on their contents. `Hold "hi" == Hold
"no"` answers `ne` by luck whatever the pooling. So an arm built only on
unequal values passes on a broken emitter, and the arm must compare two
EQUAL-valued Texts built at different addresses.

**THE MECHANISM ABOVE IS WRONG AND THIS PARAGRAPH CORRECTS IT** (reek, later
the same day; 23289 published the wrong one and this replaces it). Widening
the scan from BL to LDR and CMP says something stronger than "the field is
compared as a raw word": **no field is compared at all.** In `opening`, for
both subjects, there is exactly ONE `CMP` per `==` and ZERO `LDR` from any
non-SP base:

| subject | insns | CMP(reg,reg) | LDR from a non-SP base | `__str_eq` |
|---|---|---|---|---|
| `Hold (Text)`, 2 comparisons | 55 | 2 | none | not called |
| `Quad (Integer) x4`, 5 comparisons | 248 | 5 | none | not called |

`a64-emit-sum-eq` begins by loading the tag at `+0` and would load `+8` and
`+16` after it. None of those loads is in the emission, so **that function
never runs for these subjects**: the dispatch at `Arm64CodeGen.codex:1323` is
`text -> sum -> plain cmp`, and it is taking the third arm, comparing the two
operand REGISTERS. For freshly constructed values those are two distinct heap
pointers, so every one of these comparisons answers `ne`, including
`Quad 1 2 3 4 == Quad 1 2 3 4`.

**So `a64-is-sum-type (ir-expr-type left)` is answering False, and WHY is the
open question.** It is not the missing per-field dispatch this row was written
around; that defect is real in `a64-emit-sum-eq`'s body but is not what these
subjects hit. It also makes (3) as stated wrong in the same direction: a
four-field constructor does not "compare only its first three", it compares
none of them.

**(3) IS THEREFORE ANSWERED, and not as predicted.** What remains is the
type question above.

**`-IrUni` IS NOT BROKEN and the note that used to stand here was wrong.** It
dumps the IR into the LOG and never writes `-Out`, so it emits no `SIZE:` line
and `compile.ps1` exits 4 by design. Three scripts say so already
(`check-plug-builtins.ps1:72`, `check-text-plug-passes.ps1:62`,
`ir-fidelity.ps1:105`); reading the flag rather than the exit code is the whole
lesson (L-READ). The IR is in the `-Log` file.

## THE DIAGNOSIS, COMPLETE (reek, 2026-09-07)

**Three defects in series, each of which alone produces a wrong answer, and
none of which is the missing per-field dispatch this row was built around.**

**(A) `a64-emit-if` and `a64-emit-if-targeted` fuse a comparison into a branch,
and their guard chains have arms for Text and for the Reals but NONE for sums**
(`Arm64CodeGen2.codex:331` and `:661`). So `if A == B then` on a sum never
reaches `a64-emit-binary`, never reaches `a64-emit-sum-eq`, and compares the two
operand REGISTERS -- distinct heap pointers for freshly built values. Measured:
`opening` held one `CMP` per `==` and ZERO loads from a non-SP base. **They are
the SAME whitelist written twice**, one for a value and one for a targeted
register, and an `if` used as an argument takes the second; adding the guard to
only the first left the emission BYTE-IDENTICAL and read exactly like the guard
being wrong.

**(B) `a64-max-fields-for-type`'s `SumTy` arm never consults `defs`**
(`Arm64CodeGen.codex:1156`): `is SumTy (n) (ta) (cs) -> a64-sum-max-fields cs 0
0`, using the type's OWN carried ctors, where the `ConstructedTy` and `TypeCon`
arms beside it both look the name up in `defs`. **`IRTextEmitter.codex:259`
emits `(sum "Name" (args ...))` with no ctors**, and `ir-parse-type-sum` builds
`SumTy name args []` when the third element is absent, so a sum arriving over IR
TEXT always has an empty ctor list and this arm always answers 0. Measured with
(A) guarded: tag loads at `+0` appear, and nothing at `+8` or `+16`, so
`a64-emit-sum-eq` runs and takes its `max-fields == 0` arm, comparing the tag
and no field at all. **This is why the defect is native-backends-only: x86-64
never round-trips through IR text**, which is also why COMPILER-24's per-sum
helper could not carry over. riscv is unmeasured and shares the parser.

**(C) the unroll caps at two fields**, the row's consequence (3), and it is
real: the emitter's arms are 0, 1 and else, where else compares exactly `+8` and
`+16`.

**EACH REPAIR ALONE MAKES THE ANSWER WORSE, WHICH IS WHY NOTHING IS LANDED.**
Fixing (A) alone turns a false-UNEQUAL into a false-EQUAL: every constructor
compares by tag only, so `Hold "hi" == Hold "no"` answers eq. Fixing (A)+(B)
leaves (C), so a four-field constructor compares its first two and answers eq
for `Quad 1 2 3 9 == Quad 1 2 3 4`, where head answers ne by luck. A false-equal
is worse than a false-unequal, and the trade is invisible in any arm that only
uses unequal values.

## LANDED AS ONE CHANGE, and the arm64 cross bed moved

**(A)** both `if` whitelists gained a sum arm. **(B)** the ctors now come from
`defs` via `a64-sum-ctors-for-type`, because a sum over IR text carries none of
its own. **(C)** `a64-emit-sum-eq` was rewritten: it compares the tag, then
every field of the one field-carrying constructor, each dispatched on its own
type, with `__str_eq` for a Text. It has ONE forward branch and one patch,
against the four hand-unrolled arms it replaces.

**MEASURED ON THE ARM64 CROSS BED (Renode), two runs:**

| subject | before | after |
|---|---|---|
| `recursive-eq` | FAIL, the defect this row opened on | **PASS_EXPECTED** |
| `sum-field-eq` (new) | red in both directions by construction | **PASS_EXPECTED** |

**THREE CLAIMS IN THIS ROW WERE FALSE and are corrected here.**

- *"COMPILER-24 synthesises a per-sum helper inside the x86-64 emitter, so
  arm64 and riscv never see it."* No: `Ast/Desugarer.codex:805` (`gen-eq-def`)
  builds `__eq_<T>` in the DESUGARER, so it rides the IR to every backend.
  `recursive-eq`'s arm64 emission calls `__eq_Nest` seven times and
  `__eq_Chain` four. **The helper always reached arm64; defect (A) was stopping
  the CALL**, which is the whole reason row 1 was wrong.
- *"`recursive-eq` carries a `.no-cross` sidecar, so the cross bed skips it."*
  There is no such sidecar at head and the bed runs it, so it has been red in
  the arm64 baseline all along.
- Consequence (3) as stated, "compares only its first three". Before the
  repair it compared NONE of them.

**The refusals, and the one that was wrong.** `a64-emit-sum-eq` refuses a sum
with more than one field-carrying constructor, and a field that resolves to a
sum; `run.ps1` turns `[UNSUPPORTED]` into a hard refusal, so neither silently
compares pointers. The first draft refused on `a64-is-sum-type` alone, which
answers True for `ConstructedTy` -- **and a TYPE PARAMETER arrives as exactly
that**, so it refused COMPILER-24's own subject, `eq-generic-fields`, which has
nothing to do with nested sums. Resolving the name against `defs` separates
them: a parameter resolves to no ctors. The first Renode run is what caught it.

**Two failures on that bed are PRE-EXISTING and are not touched here.**
`real-approx-equality` is 1.91, arm64 doing `~` and `~0` as an exact `fcmp`.
`eq-generic-fields` fails line 3 (`one-param eq : no`) because arm64 has NONE
of COMPILER-24's generic-field machinery: `subst-field-type`,
`resolve-to-sum-with-defs` and `emit-eq-op` are in `Emit/X86_64.codex` and in
the wasm and zig plugs, and in no file under `codex/plugs/arm64/`. Registered
as COMPILER-63 for red, together with the shared-IR-pass answer that would
retire both of this row's refusals.

**riscv is still unmeasured and shares the IR-text parser**, so (B) is almost
certainly true there too.

**How to grade any of it without a Renode boot:** compile through
`codex/plugs/arm64/compile-arm64.ps1`, then scan `opening` in the ELF for LDR
(`(w >> 22) == 0x3E5`, byte offset `((w >> 10) & 0xFFF) * 8`), CMP
(`(w & 0x7F20001F) == 0x6B00001F`) and BL (`(w >> 26) == 0b100101`). Field loads
are the LDRs whose base is not x31. Tag is `+0`, fields `+8` and `+16`.

**And the arm this needs must use EQUAL values at different addresses.** An arm
built on unequal values passes on a pointer compare, a tag compare and a correct
compare alike.

**One trap this cost, worth not repeating:** the native plugs take IR from
`compile.ps1 -IrCce`, which is what `compile-arm64.ps1` passes. Feeding them IR
produced with `-Passes 'text-plug'` faults the plug at `walk-chapter` with
`!EXC=06` and an identical register dump for every input, arm64 and riscv
alike, which reads exactly like a plug broken at head. It is not; it is the
wrong wire. Use `compile-arm64.ps1`.

`codex/test/recursive-eq` carries a `.no-cross` sidecar naming this row, so
the cross bed skips it and the arm64 baseline is unmoved; pin the arm with
the fix, not before it.

**1.93 -- FIXED, THE PARSE-DECK INFLATION WAS `list-insert-at` NEVER GROWING
ITS CAPACITY, AND "2.4x" WAS A GROWTH RATE READ AS A CONSTANT** (fester,
2026-08-27; `codex/plugs/wasm/WasmEmitter.codex`).

`$list_insert_at` fills in place when `n < cap` and copies when it does not,
exactly as `$list_push` does. Its grow path allocated capacity `n + 1`. So a
list built by repeated insertion arrived at every call with `n == cap`, the
in-place path could never be taken, and each insertion copied the whole list
into a buffer with no room in it either. n insertions cost O(n^2) bytes on an
allocator that never frees. x86-64 doubles (`emit-list-insert-at-grow`:
`shl rax, 1`, floor 4) and grows in place by advancing the allocation
pointer, so the same source is linear there. The prose above the emitter said
this defence was already present and warned in terms about the O(n^2) it
would cost without it; the code below it had disabled the defence.

**PARSE deck, same five real units, both targets, re-measured today:**

| unit | KB | x86-64 | wasm before | wasm after | after / x86 |
|---|---|---|---|---|---|
| maui | 110 | 813,296 | 1,378,024 | 954,312 | 1.17 |
| elf | 233 | 1,271,352 | 5,622,606 | -- | -- |
| rust | 353 | 1,909,344 | 9,574,415 | 2,196,511 | 1.15 |
| arm64 | 804 | 4,238,552 | 26,256,380 | 4,661,148 | 1.10 |
| the compiler | 2,878 | 14,185,568 | 265,286,010 | 15,429,802 | **1.09** |

**The ratio was never 2.4. It rose 1.69, 4.42, 5.01, 6.19, 18.70 with unit
size, exponent about 1.6, and 2.4 is simply where somebody measured.** x86
over the identical five units is linear at about 5,000 deck bytes per KB of
source, which is the control that makes the curve a property of the target
rather than of the ladder (1.79 built padded ladders because real units of
different sizes are confounded; the confound is answered here by the second
arm rather than by the inputs). 249.9 MB leaves the compiler's self-compile.

**Output is unchanged.** Cleaned the way the page cleans it, before and after
are byte-identical at 2,460,178 chars, `6F0A41222301E7199ACF0BC7`, which is
1.83's anchor. The raw stream differs by exactly 2 bytes and both are inside
the filtered `WD:` lines, where `deck-usage=` lost a digit. Suite 26 of 26.

**How it was found, because three cheaper answers were wrong first.** The
counter recipe from 1.80 (counters after the local declarations, dump and
reset at `$phase_compact`) gives per-phase numbers once each dump is matched
to its phase by the `deck_ptr` it prints. It said allocation COUNT is flat at
1,890 to 2,335 per KB across a 26x size range and small-object BYTES flat at
79k to 91k per KB, both linear, while deck growth per KB rose 4x. **Linear
allocation under superlinear deck growth is what killed the volume theory,
and with it 1.80's standing residue that x86 must be eliding allocations wasm
performs.** It elides nothing. Three named suspects then died by measurement,
each of which reads plausibly and would have shipped as the cause: the deck
branch of `$list_push` moves `deck_ptr` without any `bump_alloc` a counter
can see, and contributes 0 bytes; `$list_push`'s copy path contributes
521,096 of 305,526,058, under one per cent; `$list_cons` copies whole lists
and is never called in the span at all, 0 bytes with the counter verified
present inside it. What named the real one was attribution rather than
suspicion: route each candidate helper's `bump_alloc` through a wrapper
taking the same size argument, which needs no call site's size expression
reproduced, and read the census. `$list_insert_at`, 250,118,256 bytes of
305,526,058 in the span, 82 per cent -- the same 82 per cent an independent
histogram had already attributed to allocations over 4 KB.

**Arm `insert-at-grow-rt`, graded both ways**, and the count in it is
measured rather than reasoned. Inserting AT the length is an append and
shifts nothing, so the arm measures the growth policy alone. **At 30,000
elements it passed under BOTH plugs and measured nothing**: the quadratic
form asks for about 3.6 GB and the host simply granted it. At 50,000 it asks
for about 10 GB, past what a 32-bit address space holds, and the head
revision rebuilt fails `memory fault at wasm address 0xffff0000 in linear
memory of size 0xffff0000` -- fault address equal to memory size, one byte
past the frontier (L-MECHANISM). The doubling form still asks under a
megabyte and agrees with x86-64. The first version of that arm is the lesson:
a threshold set where two behaviours differ IN PRINCIPLE, rather than where
they differ ON THIS BED, is a green arm that cannot fail.

No compiler change, no seed, no token.
## 1.91 -- CLOSED, both native backends (reek, 2026-09-07 arm64, 2026-09-08 riscv): `~` and `~0` are the ordinal mapping now

**Found 2026-08-27 (blu), working COMPILER-9's class-B set.
`codex/test/ops/real-approx-equality` fails its three f32 lines on arm64 and
passes its two f64 lines.** The natural reading of that split is a width bug.
It is not, and acting on the width alone would fix two of the three failing
lines and leave the third, while leaving f64 wrong in a way this test cannot
see (L-GAP).

**What the operators MEAN, read off the x86-64 emitters** (`emit-approx-eq`
and `emit-approx-eq-exact`, `X86_64.codex:1724` and `:1749`): each operand is
mapped to a MONOTONIC ORDINAL by `float-to-ordinal-sized` (width-aware, eight
instructions), the two ordinals are subtracted, the absolute value taken, and
compared -- `~` is True within **4 ULPs**, `~0` within **0**. The ordinal
mapping is what makes `-0.0` and `+0.0` the same value, and the ULP tolerance
is what makes two values one ULP either side of zero compare equal.

**What arm64 does** (`Arm64CodeGen.codex:1383-1384`): both `IrApproxEq` and
`IrApproxEqExact` go to `a64-emit-real-comparison ... 1`, which is an
`fcmp-d` with the equality condition. That is exact IEEE equality at f64
width, with no dispatch on the operand's width -- while the ORDERING
operators thirty lines above do dispatch, on `a64-real-cmp-kind == 2`.

**So there are two defects stacked, and the measurement separates them.**
The f32 lines fail because an f32 bit pattern zero-extended in a 64-bit
register is read as an f64: `-0.0f` is `0x80000000`, which as an f64 is a
tiny denormal, not zero, so it compares unequal to `+0.0`. **The f64 lines
pass only because IEEE says `-0.0 == +0.0`, which happens to agree with the
ordinal answer for that one input.** An f64 `~` across a one-ULP straddle
would fail too, and no line in the test spells it.

**The fix is a port, and the port is NOT direct: two encoders are missing.**
`codex/foreword/core/Arm64Encoder.codex` has no `eor` at all, and `asr` only
in register form, so the x86 sequence (`sar 63` / `shr 1` / `xor` / `sub`)
cannot be transcribed. The formulation that needs only what exists is
`ord = b < 0 ? INT64_MIN - b : b`, built from `a64-emit-li`, `arm64-sub`,
`arm64-cmp-imm` and `arm64-csel`, which is the same mapping. For the f32 arm,
shifting the pattern left 32 and NOT shifting the ordinal back down is
cheaper than adding an immediate `asr`: one f32 ULP is then 2^32, so the
tolerance is `4 * 2^32` in a register rather than 4 as an immediate.
**`a64-alloc-temp` rotates a pool of FOUR registers** (the prose at
`Arm64CodeGen2.codex:110`), so a two-operand sequence of this length must
park each ordinal in a local the way `a64-emit-sum-eq` does, rather than hold
it in a temp.

## LANDED FOR arm64 (reek, 2026-09-07)

`a64-emit-approx-eq` maps both operands to the ordinal and compares the
difference against 4 ULPs (`~`) or 0 (`~0`), replacing the `fcmp-d` both arms
shared. Built exactly as this row prescribed, and the prescription held: no
`eor` and no immediate `asr` are needed, because `ord = b < 0 ? INT64_MIN - b :
b` uses only `sub`, `cmp` and `csel`, with `INT64_MIN` from `1 << 63` rather
than a 64-bit immediate; an f32 is shifted up 32 and LEFT there, so one ULP is
2^32 and the tolerance is `4 * 2^32` in a register.

**MEASURED on the arm64 Renode bed, `-Filter approx`: 5 of 5 PASS_EXPECTED, 0
FAIL** -- `real-approx-equality` (this row's subject, all five lines, the three
f32 ones included), `approx-eq`, `real-approx`, `real-approx-modes`,
`real-approx-negate`. Before: `real-approx-equality` FAIL_OUTPUT on line 1.

**The algorithm was checked against x86-64 BEFORE the boot**, by simulating the
emitted sequence against `float-to-ordinal-sized`: they agree on all five
subject cases and on a 5-ULP control that must answer False. That left only the
register and flag handling for the bed, which is what it then caught.

**THE BED CAUGHT A REGRESSION AND IT IS THE LESSON OF THIS ROW.** The first
build turned `approx-eq` red on `1.0 ~ 1.0`. The left operand's ordinal
allocates several temps while the RIGHT operand is still live in its own
register, and the pool rotates four, so one allocation was handed `r-reg` back
and the second ordinal was computed from a clobbered value. `real-approx-equality`
stayed GREEN through that, because its operands come from named definitions
reloaded fresh -- so the row's own subject could not see the defect, and only a
second subject did. The old `fcmp` code was safe by accident: it moved both
operands into FP registers on its first two instructions. Both operands are now
parked in locals before any temp is allocated, and the reason is written at the
site. This row's warning about the four-register pool was right and still not
enough: it named the ordinals, and the OPERANDS were the ones that got clobbered.

**riscv HAS THE IDENTICAL DEFECT, and it is no longer unmeasured; it is read
off the source.** `RiscVCodeGen.codex:1297-1298` sends both `IrApproxEq` and
`IrApproxEqExact` to `rv-emit-real-comparison ... rv-feq-d`, an exact f64
compare with no width dispatch, and no ordinal mapping exists anywhere in the
plug. **Its fused-branch form is NOT worse, and the claim that it was is
withdrawn** (reek, 2026-09-08). `RiscVCodeGen2.codex:147-148` do admit both
operators into the fusion whitelist and `:160-161` do emit `rv-bne left right`,
a raw integer compare of the two bit patterns, but `rv-emit-if` never reaches
them for an approx-eq: `:421-423` send every text-, real- and real-approx-typed
left operand to `rv-emit-if-val` BEFORE `:440` tests `rv-is-cmp-op`, and
`rv-is-real-type` unwraps `UnitTy`, so a real operand cannot slip past. riscv
has ONE `if` emitter, not arm64's pair, so there is no second whitelist to miss.
Those two arms are dead for their stated subject. The claim came from reading
the whitelist without its caller.

## LANDED FOR riscv (reek, 2026-09-08)

`rv-emit-approx-eq` and `rv-emit-real-ordinal` replace the shared `rv-feq-d`,
with the same tolerances. riscv takes the DIRECT x86-64 mapping rather than
arm64's `csel` form, because `RiscVEncoder.codex` has all four of `slli`,
`srli`, `srai` and `xor`: `m = (v << 1) >> 1`, `k = v >> 63` arithmetic,
`ord = (m ^ k) - k`. That is the same function of `v` as arm64's
`v < 0 ? INT64_MIN - v : v` over the whole 64-bit range, `INT64_MIN` itself
included, where both give 0. The f32 arm shifts up 32 and leaves it there, so
the tolerance is `4 * 2^32`. Both operands are parked in locals before any temp
is allocated; `rv-alloc-temp` rotates four exactly as `a64-alloc-temp` does.

**MEASURED on the riscv64 Renode bed, `-Filter approx`: 5 of 5 PASS_EXPECTED,
0 FAIL** -- `real-approx-equality`, `approx-eq`, `real-approx`,
`real-approx-modes`, `real-approx-negate`.

**The green is an ABLATION, not a quoted baseline.** The dispatch was put back
to `rv-emit-real-comparison ... rv-feq-d`, the plug rebuilt, and the same five
run: `real-approx-equality` FAIL_OUTPUT at line 1, `f32 -0 ~0 +0` wanting True
and answering False, which is 1.100's recorded symptom. Restoring the fix and
rebuilding gave a plug byte-size identical to the first (706491) and 5 of 5
again. So the change is what turned the test, and it is not a vacuous pass.

**ONE LINE IN THE WHOLE BATTERY CAN SEE THIS DEFECT, on riscv as on arm64.**
The ablation reds exactly one test at exactly one line, the f32 one. Every f64
line passes under `feq-d` because IEEE agrees with the ordinal answer on the
inputs the tests use; the f64 one-ULP straddle that separates them is still
written down nowhere and still untested on any backend. That gap is the row's
residue, and it belongs to whoever next writes real-comparison arms.

## 1.106 -- arm64 staging runs into the platform registers past six stack arguments

The 2026-08-27 clobber is fixed (blu, in COMPILER-9's class-B set). What is
left is a latent limit of the same family, left unfixed on purpose: the
scratch base is `a64-x10 + slot`, so past six stack-passed arguments, which
is more than fourteen parameters, staging runs into x16, x17 and x18, the
intra-procedure-call and platform registers. Nothing in the tree reaches it
today. Reproducer with its controls: `docs/Test/Active/Arm64StackArgClobber.codex`.


## 1.100 -- CLOSED (reek, 2026-09-08): the four riscv `real-*` reds were FOUR SEPARATE DEFECTS and none of them was about Reals

`real-approx-equality` was 1.91's `rv-feq-d`. The other three are below, each
measured on the riscv64 Renode bed and each with an arm64 control run. **The
row's own framing was the trap: they sat undiagnosed for a week as a "real-*"
family, and no two of them share a cause.**

**`real-compare-negative` (was FAIL_STARVED at 1 of 15 lines): `show 0` read an
UNINITIALISED STACK SLOT.** `rv-rt-itoa` keeps the index its digit-reversal
starts at in `8(sp)` and stores it at `pos-label`, which the ZERO path branches
straight past on its way to `done-label`. The reversal then indexed the digit
buffer by whatever the caller had left in that slot. Line 1 of the test prints
`11` and line 2 is the first `show 0`, by which point the slot held `-2.9`'s bit
pattern from `say-gt`'s frame, so it took an unmapped address. A stale value
that happened to be a small non-negative integer would have printed silently
wrong output instead. Fixed by storing zero on the zero path.

**`real-mode-fields` (was FAIL_RUNTIME, no uart) IS NOT A riscv DEFECT: arm64
fails it identically.** Its `opening` returns a `Sample`, and
`RiscVCodeGen3.codex:1373-1387` prints an `opening` result only for Integer,
Real and Text, falling through silently for anything else; arm64 does the same.
`real-mode-opening` passes because it returns a `Real`. Marked `.no-cross` with
that reason, which is the instrument the harness already has
(`test-cross-batch.ps1:89`). Showing a constructed value on the native backends
is COMPILER-63's shared-IR-pass shape, not this row's.

**`real-cert` (was FAIL_OUTPUT at line 5): riscv MEMOISES a def that returns a
LIST, and `list-set-at` writes in place.** `rv-rt-list-set-at` is a shift, an
add, a store and a return, and arm64's `a64-rt-list-set-at` is the same four
instructions, so in-place update is the native convention and NOT the defect.
The defect is `rv-is-memoizable`, which is riscv's alone: it caches the returned
register of any zero-parameter pure def, so a memoised list is ONE object shared
by every caller. `sha256-h0` is such a def and the compression loop sets its
eight words, so `sha256` answered correctly ONCE per program and differently
wrong on every call after. `real-cert` is the only test in the tree that hashes
twice, which is why one line and one line only was red. Fixed by
`rv-memo-safe-type`: only Integer, Real and Boolean results may be memoised.

**HOW THE LAST ONE WAS FOUND, because the first three theories were all wrong
and each was refuted by a measurement rather than by reading.** Heap exhaustion:
refuted, zero illegal accesses and the heap reached 0x802B8CC8 of a 1 GiB DRAM.
A 4096-bit limit in `cb-mod-exp`: refuted, a synthetic probe answered `2^3 = 8`
correctly at 64, 128, 192, 256, 320, 384 and 512-byte moduli. A SHA-256 padding
edge at `len mod 64 == 55`: refuted, riscv disagreed with arm64 at EVERY length.
What located it was reordering the probe so the same input was hashed twice:
only the FIRST call was right, and two identical calls gave two DIFFERENT wrong
answers, which is state carried between calls and cannot be a length bug.
Turning memoisation off made every call correct.

**THE MEMO DEFECT WAS FAR LARGER THAN THIS ROW, and the full battery is how the
size became visible.** `build/test-cross-batch.ps1 -Arch riscv64 -Jobs 3`, the
same 536 tests run twice, once on the plug rebuilt from the reverted source and
once on the fixed one:

| | PASS_EXPECTED | FAIL |
|---|---|---|
| before | 428 | 79 |
| after | 444 | 63 |

**Re-measured at MERGED HEAD** (reek 23467 brought red 23339's host-socket
helper into the same file), all sixteen run one at a time: sixteen of sixteen
PASS_EXPECTED, plus `approx-eq` and `real-approx-equality` for 1.91. **Two
attempts at a third full battery were KILLED for memory, one at `-Jobs 8` and
one at `-Jobs 3`**, so the head confirmation is the named subset rather than the
whole 536; the before-and-after pair above is the comparison the claim rests on
and both halves of the pair completed.

**Sixteen tests turned green and NOT ONE turned red.** Beyond the three named
above, the sixteen are `crypto-vectors`, `dtls-handshake`, `dtls-message`,
`dtls-openssl-fragments`, `dtls-record`, `edvector`, `idm-key-tests`,
`idm-salt-iv-distinct`, `int-rem`, `ota-gate-block`, `rsa-pss`, `synth-test`,
`tls-cert` and `tls13-schedule`. Every one of the crypto and TLS entries hashes
more than once, which is the memo defect exactly; `int-rem` is the `show 0`
defect. **The whole crypto and TLS surface was wrong on riscv after the first
digest of any program**, and the register carried the damage as three unrelated
`real-*` rows. The plug that produced the after-run hashes identical to the plug
built from the submitted source.

**A riscv fault is SILENT, and that is why this row was undiagnosed.**
`__trap_handler` loops on the faulting instruction with no output, so every
riscv fault reaches the harness as FAIL_STARVED or FAIL_RUNTIME with nothing to
read. The way to a diagnosis is Renode directly: `cpu CreateExecutionTracing`
for the PC trace, `sysbus.cpu GetRegister N` at the trap, and `objdump -D -b
binary -m riscv:rv64 --adjust-vma=0x80000080` over the `.bin` (the flat image
starts at the ENTRY, 0x80000080, not at 0x80000000; getting that wrong shifts
every address by 0x80 and makes correct code read as misaligned). Registered as
2.48.


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

## 2.03 -- the `irbytes` runner is CLOSED; it is on the push checklist and in no automated gate

`codex/plugs/wasm/page-wire-test.ps1` (main 20798) is the runner, the third
beside `page-lens-test.ps1` for the `ir` rows and `page-bytes-test.ps1` for the
`bytes` rows. It compares each native backend's wasm wire against that same
backend's bare-metal plug byte for byte, an oracle sharing no code with the
subject, and `-Calibrate` corrupts one wire byte and requires every row to go
red at it, so a green says the comparison fired. The three mechanics that each
read exactly like a broken plug are in its header and are not repeated here.

It does NOT assert a refusal on input that is not IR, because these two modules
do not refuse; that gap is 2.06, and a runner asserting behaviour its subject
lacks would be red from its first run and switched off.

**The residue is cost, not coverage.** `PublicPush.md` 5b names it, so a push
runs it; no automated gate runs it, which 5b states of all three graders. It is
not free: `build-page-modules.ps1 -Only riscv,arm64` and each
`codex/plugs/<plug>/build.ps1` must have produced the module and the
`<plug>-plug.cdx` oracle, and the oracle arm boots a guest per row.


## 2.06 -- the native backends answer a plausible wire on input that is not IR

Handed `this is not an IR chapter`, `riscv-stdio.wasm` answers 46,886 bytes and
`arm64-stdio.wasm` 15,737, both exit 0, where the real subject gives 50,184 and
18,667. The output TRACKS the input, so the modules are reading it; what is
absent is a refusal.

This is L-BAILVALUE on a front door: a producer that answers rather than
refusing leaves a caller unable to tell that anything went wrong, and the
page's board target would hand somebody a downloadable binary built from
whatever was in the box. The text lenses do refuse, which
`page-lens-test.ps1 -Calibrate` asserts across all 45 of them; these two are the
exception.

The fix belongs with whoever owns `PlugIrBytes`: refuse an input with no
`IR-BEGIN`, the way `compile-plain` refuses an unknown mode (L-ACCEPTED). The
wire runner deliberately does NOT assert the refusal today, because a runner
that asserted behaviour these modules do not have would be red from its first
run and would be switched off.


## 2.07 -- PARTLY CLOSED (reek, 2026-09-08): `ElfWriter` takes a machine parameter now, and the mislabelling it guarded against turns out to be LATENT

The ELF lens ships with its kernel/usermode switch and both modes are graded
on entry ARITHMETIC rather than on a magic number. Two things stand.

**`ElfWriter` knows only `elf-machine-386` (3) and `elf-machine-x86-64` (62)
and writes them literally at three header builders** (`ElfWriter.codex:73`,
`:91`, `:130`, verified at head 2026-09-07). A riscv or arm64 wire fed through
it comes back in an ELF claiming to be x86-64. It needs `EM_RISCV` (243) and
`EM_AARCH64` (183) and a machine parameter threaded through
`elf32-header-bytes-shdrs` and `elf64-header-bytes`. Small, and not done rather
than shipping a mislabelled header.

**LANDED (reek, 2026-09-08): the parameter and the two constants.**
`elf-machine-riscv` (243) and `elf-machine-aarch64` (183) are defined, and
`machine` is threaded through all THREE header builders rather than the two
this row named, because the third writes the same literal and would have been
the next one found. Every existing call site passes what it hardcoded before,
so emission is unchanged: an ELF built from the depot seed through
`cdx-to-elf.ps1` answers `e_machine` 62 and a well-formed ELF64 header, and
the plug builds at 183,210 bytes.

**AND THE DEFECT IS LATENT, WHICH THIS ROW DID NOT SAY.** Nothing feeds a
non-x86 wire through `ElfWriter` today. riscv answers ELF from its OWN writer
(2.08's subject), and the ELF plug's payload carries a MODE byte that selects a
CONTAINER, 0 bare metal, 1 user-mode, 2 hosted console, and carries no
architecture at all. So no arm in the tree can produce the mislabelled header
this row describes, and the parameter is the enabling half rather than a fix
with an observable effect. Said plainly rather than left for a reader to infer
from a green run.

**What is actually left, and it is a WIRE change rather than a writer change:**
the plug cannot label a header it has no way to be told about. A machine
selector has to reach `plug-emit-bytes`, which means either a byte in the
payload header beside the mode byte or a second mode range, and either one moves
a protocol that four callers spell. That is the decision, and it should be taken
when the first non-x86 caller exists rather than before it (WORKS-61's
precedent: priced and refused until a consumer exists).

**What the usermode file is NOT.** It is a correct ELF64 container whose CODE
is still what the backends emit for bare metal: console and heap are device
registers, not `write(2)` and `mmap(2)`. It loads on Linux and stops at its
first print. The hosted arms are PrismDevEnvironment stage 5a, compiler work
and seed-affecting; the pill's own title says so.


## 2.08 -- the board ELF carries no per-board link or flash address

`RiscVStdio` takes a mode line and `ELF` answers a RISC-V ELF64 from the riscv
plug's own writer, entry resolved through `rv-find-func-offset` and REFUSED by
name when there is none.

What it honestly is: ELF64, `EM_RISCV` 243, loaded at `0x80000000`, which is
the RAM base `qemu-system-riscv64 -machine virt` uses. **The per-board link and
flash addresses for the nine named HAL boards are NOT in**, so the pill says
"RISC-V kernel" rather than naming ESP32-C6 or FE310. Putting a board's name on
a file whose load address was not derived from that board's memory map is the
mislabelling this register keeps closing.

Corrected at head 2026-09-07: `codex/plugs/arm64/Arm64Elf.codex` now EXISTS
(4,927 bytes), so this row's earlier claim that no `Arm64Elf` chapter is
anywhere in the tree is out of date. Whether the arm64 module reaches it, and
whether that module ships, is 2.03's and the page manifest's question.


## 2.09 -- the nine HAL boards cannot run what the board chain emits, and a design doc says they can

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


## 2.10 -- CLOSED (reek, 2026-09-07): the `decks` field in `examples.json` was consumed by nothing and is deleted

The two page bugs this row opened on are fixed (the CDX compile rides the
ladder; the payload-marker search is whole-stream), and the deck-consumption
question it left open is closed: on both the wasm module and x86-64 the IR is
byte-identical from the lowest scale that compiles up through the clamp and the
compiler's own derivation, with both arms refusing at 4 and compiling at 5
(reek 22672, which carries the measurement).

**The method is worth reusing and the reason is cost.** `opening.codex`'s
32-vs-33 record is a unit compiling CLEAN at a short scale with different bytes,
so a refusal census certifies nothing and only OUTPUT EQUALITY does;
`build/deck-headroom.ps1`'s header calls that instrument too expensive to
repeat at one VM boot per arm per unit. Run against the page module under
wasmtime it needs no guest at all, which is what makes a corpus-wide answer
affordable.

**THE FIELD IS DELETED, 69 lines, and honouring it was never the alternative
it looked like.** `codex/plugs/wasm/page/examples.json` carried a `decks` value
per example that nothing read. Honouring it would have meant writing a consumer
AND making the number right for all 69 examples, which is work with no demand;
deleting it costs nothing and removes a trap, because it said 12 for 68 examples
and **200 for `widget-box`, which compiles at 5** and is above the page ladder's
own top of 125, so a consumer added later would have inherited a wrong number.

**Every reader enumerated before the delete, which is the whole of the grade
here** (L-UNCALLED: a field nothing calls is a field nothing has tested, and a
census by name is a statement about the name until you check what each hit
does):

- `build-page.ps1:145` copies the file and `:356` embeds it verbatim as
  `window.__EXAMPLES`. Both field-agnostic.
- `page-example-test.ps1` reads `source`, `prelude`, `name`, `cat`. Its ladder
  comes from the PAGE's `const DECKS`, parsed out of `prism.html`, and it
  refuses rather than guessing if that list is absent.
- `page-lens-test.ps1` reads `name` (to find `accumulator-corpus`) and `source`.
- `prism.html:2755` loads the file; `const DECKS = [12, 48, 125]` at `:709` is
  the ladder and is untouched.
- `build-page-modules.ps1:67` DOES read a `decks`, and it is a different one:
  `$m` iterates the `$PageModules` table in that script, not this file.

After the delete: the JSON parses, 69 examples, every one still carrying
`source`, `name` and `cat`, 38 with a `prelude`, and both named subjects
(`accumulator-corpus` for the lens test, `widget-box`) present.


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

`ZigEmitter.codex:342` and `:373` map `RealTy (w) (m)` to `f64`, discarding
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


## 2.14 -- what the hosted corpus score does NOT mean, and three subjects nobody owns

*Cite this by subject, not by number: this file carries two entries numbered
2.10 and two numbered 2.11.*

The four defects this row was opened for are fixed and parity holds: wasm 51 =
hosted linux 51, one ahead of hosted windows 50, re-measured 2026-09-01 on seed
D6ED6F35, both arms on the same kernel with the plug rebuilt first (L-SAMEVER).

**THE DEFAULT 60 IS A SAMPLE OF ONE DIRECTORY, NOT OF THE CORPUS.** 2.16 made
the harness reach every eligible subject, which pulled `codex/test/apps/**`
into range; the alphabet is dominated by that directory, so the default cap now
selects 56 `apps/*` of its 60, and `apps/*` is full of subjects asserting
bare-metal machine facts no hosted target can run. A score read against an
earlier run is comparing different sixties and nothing announces the change.
**Quote a named slice or quote the eligible population; never read the default
as a corpus score** (L-DENOM).

**Three subjects fail on every arm and belong to no lane:**

- `apps/classic-games-run`: wasm, linux and windows all print
  `Backgammon: Black wins in 104 plies` where the oracle says 174. Three
  independent targets agreeing with each other and disagreeing with bare metal
  puts the question on the BARE-METAL side, not on any plug.
- `apps/bp-symbolic-write`: 16 of 166 on wasm, exit 139 on linux, 0xC0000005 on
  windows. Three manners of failing.
- `apps/cam-capture`, `console-test`, `cpu-builtins`, `cpu-inspect`: machine-fact
  subjects that all three arms refuse.

**`hosted-kind` is hard-coded to 1 and no consumer can currently tell.**
`WasmEmitter.codex:1009` answers 1, which in the compiler's own convention means
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

**Two items are still open under this row:**

- **The refusal marker has TWO spellings and unifying them is queued.** Seven
  emit sites produce an `unreachable` with a comment, five as
  `(unreachable (; wasm plug: <why> ;))` and two as
  `(unreachable) (; no wasm form for <name> ;)`, the second including
  `wat-try-builtin`'s generic fallthrough, where most refusals come from.
  Unifying is a comment-text change with no behavioural effect, but it moves
  every subject's emitted bytes, so it wants a grading run.
- **`__self-type-defs` is a compiler-wide question, not a wasm one, and is NOT
  taken.** wasm emits an empty list; arm64 and RISC-V emit integer `0`, which
  hands back a null where a `List TypeBinding` is expected, so wasm's answer is
  strictly better than theirs. Only x86-64 builds the real table. Porting it
  means about 250 lines from `X86_64Compound.codex` plus tracking `CodexType`'s
  28 constructors forever, and every consumer is inside that same file. A
  decision for Damian if it is ever wanted.


## 2.16 -- no gate phase runs any wasm harness, and one subject is red on both arms

The selection repair is landed (the glob recurses, `-Max 0` means the whole
eligible corpus, every score line names what it was drawn FROM). Two things
outlive it.

**Nothing in `build/build.ps1` runs a wasm harness.** Re-measured 2026-09-07:
the only occurrence of `wasm` in that file is a comment, and no phase invokes
`hosted-wasm-test.ps1`, `wasm-e2e.ps1`, `page-example-test.ps1` or
`check-emitted-runtime.ps1`. So a green gate says nothing whatever about this
plug while the page modules built from it ship on the landing site. That is
L-NOGATE at its widest: not a test going red unnoticed, but a whole backend
outside every runner, where the first thing that can notice a regression is a
person opening the page.

**`ops/real-mode-fields` is red on BOTH arms and is not evidence about wasm.**
The hosted x86-64 arm exits `-1073741819` (0xC0000005), an access violation in
the hosted lift that nothing had graded. Unowned.

**Grade this plug by RUNNING, not by assembling.** An unbound or arity-less
name emits `(unreachable) (; no wasm form for <name> ;)`, so a missing arm
assembles and traps at runtime; grep the emitted `.wat` for `no wasm form for`
to name every missing builtin in a module rather than the first one that
stopped the assembler. A source grep for builtin names cannot answer this:
`fail`, `now`, `max`, `compare`, `abs` and `force` are ordinary English and
`.codex` carries prose by design.


## 2.18 -- the `wasm-exports` census is ruled and not built

**RULED 2026-09-01 (root), TAKEN as Steve Howell's PR 112 proposed:** a
chapter declares its own exports and that declaration wins; the 484-name
allowlist applies only where a chapter declares nothing. It carries a census:
once every shipped page module declares, **the allowlist is deleted**, because
a list drawn from unrelated applications is a leak in one direction and a
coincidence in the other. reek's.

Not built. Verified at head 2026-09-07: `wasm-export-list` is still the
allowlist at `WasmEmitter.codex:3904`.

**Steve's remaining reports, HIS measurements on HIS corpus and not
re-measured here** (L-COUNT: re-measure before quoting any of these): ~45
runtime helpers carry no prefix, so each is a name a program may not use; 6
SIMD type mismatches; 26 of 526 corpus programs differ from their `.expected`.
His `Text` literal PATTERN item is CLOSED at head: `wat-lit-pat-test` tests a
Text pattern with `$text_eq` against the string table.


## 2.15 -- the typescript plug SAYS it has no `read-line`; refusing the general case needs a scope it does not have

**FIXED for `read-line` (reek, 2026-09-07).** The plug emitted
`const s: any = read_line;`, an identifier defined nowhere in the file, so the
artifact was plausible and died at run time on a ReferenceError naming neither
the plug nor the gap. `ts-io-builtins-text` now defines `read_line` to throw
`typescript plug: no emitter for read-line`, which gives the arity table a
0-ary entry and so makes the emission the CALL `read_line()`, putting the
refusal at the point of use.

**The marker is deliberately not `CODEX_REFUSED_`.** That is
`plug-oracle-test.ps1`'s class-5 integer-precision verdict, and its rule runs
BOTH ways: a plug on the refusal list that prints values instead of refusing is
a FAIL, and one not on the list that refuses is also a FAIL. Borrowing it here
would report typescript as having refused a question it was never asked.

**THE GENERAL CASE IS NOT FIXED AND IS BIGGER THAN IT LOOKS, which is the part
worth knowing before anyone reads this row as a template.** Refusing every
unimplemented builtin at the name site is what zig does, and zig can only do it
because `zig-name-known` consults a CONTEXT carrying bound names, renames,
constructors and the arity map. `emit-ts-name` takes `(Text, List ArityEntry)`
and the arity table holds ONLY prelude functions and top-level defs, so
`lookup-arity` answering `-1` means "a local OR an unknown name" and cannot
tell them apart. **Refusing on `-1` would break every local variable
reference.** Doing it properly is a bound-names context threaded through
`emit-ts-expr`, a signature change across the file, and it is the same work
2.38 names for the other ~45 plugs.


## 2.19 -- `Read-StreamBytes` discards how far a partial read got

The img plug's spurious CCE conversion and the harness contract defect behind
it are both fixed (reek 21526 and 21550); `test-disk-compile` passes end to
end. What is left is small and separate: `Read-StreamBytes`
(`build/vm-config.ps1:637`) returns `$null` on a partial read and discards the
count, so a harness cannot say how many bytes arrived. Not the cause of
anything currently failing.


## 2.21 -- OPEN (red, from COMPILER-36): the wire states the integer overflow contract, and the remaining plugs wrap where it says trap

THE CONTRACT (root, COMPILER-36). `add-int`, `sub-int` and `mul-int` on a plain Integer TRAP on signed overflow. The wrapping band is spelled `add-int-wrapping` / `sub-int-wrapping` / `mul-int-wrapping` and its node type reads `(int i64-min 9223372036854775807 ov-wrap)`. `codex/plugs/common/IRTextParser.codex` collapses both spellings to the plain op and `ir-parse-expr-binary` (`IRTextParser.codex:727`) parses the node TYPE, so every plug already holds the mode per node and keys on `int-ty-wraps ty` (`codex/compiler/Types/CodexType.codex:141`, a chapter every bundle carries). The gap is at the EMIT sites only; the parser needs nothing. The contract also reaches a plug's own PROGRAM, where `plug-selftest` is the only runner that sees it: a byte assembler is a shift, not a multiply.

DONE. x86-64 bare metal traps. wasm: `emit-wat-binary` keys on `int-ty-wraps` and calls `$cx_add_trap` / `$cx_sub_trap` / `$cx_mul_trap`, three preamble helpers ending in `unreachable`. Text family 1, zig / csharp / rust: zig emits `+` `-` `*` against `+%` `-%` `*%`, csharp `checked(...)` against the unchecked default, rust `checked_*(...).expect("integer overflow")` against `wrapping_*`. Text family 2, groovy / go: groovy's operators promote to BigInteger rather than wrapping, so the band narrows with an explicit `(long)` and a plain Integer takes `Math.addExact` / `subtractExact` / `multiplyExact` on coerced longs; go has no checked primitive and gets three hand-written helpers that panic, the mul check by quotient after answering a zero operand and the two `-1` cases.

UNIT 2.21a -- 64-BIT INTEGER IN THE JVM TEXT PLUGS (java, kotlin, scala), the prerequisite for their trapping arm. Each represents a Codex Integer as a 32-BIT int: java casts `(int)` at every integer op and emits `IrIntLit` with no `L` suffix (`JavaEmitter.codex:82,114-119`), kotlin (`:236`) and scala (`:230`) emit a bare literal, which is a 32-bit `Int` in both languages. Two consequences, and the second is why this blocks rather than merely degrades: a Codex Integer past 2^31 is already wrong in all three today, and `Math.addExact` on those operands binds the INT overload and traps at 2^31, on values the contract declares legal, so a trapping arm built on the current representation turns working programs into crashes and is strictly worse than the wrap it replaces. The unit is the representation: literal suffix, the casts at every integer site, and whatever the type mapping and stdio paths assume about width. None of the three has a toolchain on this box, so it is accepted by emission only. **NO JDK, PERMANENTLY (root relaying Damian's 2026-09-07 standing rule, verified at this file's "WE DO NOT DO TOOLCHAINS"): the question is not to be put again.** What that closes is the BLOCKER, not the defect. The row was held for a JDK because no JVM here can tell a coherent-but-wrong-width representation from an incoherent partial widening; under the standing rule that distinction is settled by READING the emission, which is the same standard 1.14, 1.20, 1.39, 1.46 and 1.59 are closed under, and a partial widening is visible in the emitted source. So the unit stays takeable and its acceptance is a reading: literal suffix, the casts at every integer site, and what the type mapping and stdio paths assume about width, in all three emitters at once, with compose following Kotlin. **NOTHING IS SHELVED and no CL exists** (re-verified 2026-09-07 on both red clients); a circulated "written and shelved" wording was wrong and there is no shelf number to cite.

OPEN, AS SEVEN CLASSES (root's ruling 2026-09-07: one CL per class, class = the target's overflow mechanism, and REFUSE with a named diagnostic wherever the class has no way to honour the contract). 48 plugs carry an emitter; 6 are done. Each of the remaining 42 carries its OWN `is IrAddInt` arm, so there is no shared site and no delegation: measured 2026-09-07, only 1 of 6 front-end wrappers checked emits through the javascript emitter, and the wrappers do not share a target (compose is Kotlin, maui/winforms/wpf are C#, qt/gtk are C++, flutter is Dart). **The REFUSAL IDIOM already exists and is `wgsl-refuse` (`WgslEmitter.codex:30`): `"CODEX_REFUSED_" & what`, an undefined identifier carrying its own reason, so the target toolchain fails with the name in its error and no value reaches a caller (L-BAILVALUE, L-ACCEPTED).** **A PLUG IS CLASSED BY THE LANGUAGE IT EMITS, NEVER BY THE NAME OF ITS TOOLKIT, and the second reading is what caught it: qt emits QML with a JavaScript block and gtk emits Python (PyGObject), so neither is C++ and both were in the wrong class; compose emits Kotlin, flutter Dart, maui/winforms/wpf C#, swiftui Swift, and react/vue/svelte/angular/electron/html JavaScript.** The wrapper plugs emit the language of their HOST RUNTIME, not the language the toolkit is implemented in. Membership below is MEASURED on each plug's emitted literal and operator; the overflow MECHANISM in each heading is a claim about the language, not something this box can run, and no toolchain here grades any of these.

- **Class 1, checked primitive, 64-bit. DONE** (csharp, clojure, maui, winforms, wpf, julia). csharp, maui, winforms and wpf all emit C# with `L` literals and take one arm: `checked(...)` for a plain Integer, the unchecked default for the band. clojure needed no helper at all, its `+` on a long already throws ArithmeticException and `unchecked-add` already wraps. julia has both natively too: Int64 wraps and `Base.Checked.checked_add` and its siblings throw OverflowError, which is why it belongs here rather than with the bignum targets its language family suggests. GRADED: csharp answers 57 of 57 against x86-64 truth on `plug-oracle-test`; the other five have no toolchain on this box and are accepted by EMISSION only.
- **Class 2, wrapping-only 64-bit, no checked primitive. DONE** (d, fortran, objc, flutter). The go shape: a helper per op in the plug's own preamble, so the emission carries one grammar rather than four closure syntaxes. Two members are NOT plain wraps and the difference is in the code: **objc** is C, where signed overflow is UNDEFINED rather than wrapping, so its band does the arithmetic in `NSUInteger` and converts back, and its trap computes the same way before reading the sign, because the check must not be the thing that overflows; **fortran** has no unsigned type at all, so its band is the bare operator and rests on gfortran wrapping in practice rather than on the standard, which leaves `integer(8)` overflow undefined. Fortran also has no exceptions: `error stop` is the only way one of its procedures can refuse to return. d and flutter (Dart on the VM) genuinely wrap, so their bands are the bare operator. EMISSION ONLY: no toolchain for any of the four is on this box (g++, gcc, clang, dmd, ldc2, gfortran, dart all absent, measured 2026-09-07) and none is oracle-wired, so nothing here is graded by running. Dart on the WEB is a double, where neither arm holds; that build would take the class 5 treatment. **FOR WHOEVER INSTALLS A TOOLCHAIN, START WITH FORTRAN, AND THE QUESTION IS REFUSE-OR-WRAP:** its band is the one arm in this class resting on a compiler's habit rather than on a language guarantee, so `gfortran -ftrapv` or a future release turning overflow into a trap would make the WRAPPING band abort instead of wrapping, which is the contract inverted rather than merely unimplemented. The two outcomes to distinguish on the first real run are a band row answering the wrapped value (the arm is sound and the standard's silence is harmless here) against a band row aborting (the plug cannot express the band at all and belongs in class 5 with a named refusal, not in this one). Nothing on this box can tell those apart today.
- **Class 3, trapping by default. DONE** (zig, swift, swiftui, nim, ada). **The PLAIN arm needed nothing and the BAND was the entire job**, which is the reverse of every other class: these targets already trap, so what was wrong is that the wrapping band trapped too, inverting the contract rather than leaving it unimplemented. The row previously called this class "nothing but a sentence" and that reading had it backwards. swift and swiftui take the masking operators `&+` `&-` `&*`; nim has no such operator, so its band goes through `uint`, whose arithmetic wraps by definition, and casts back; ada needs a MODULAR type and `Unsigned_64` is the one that matches the width, but converting a negative `Long_Long_Integer` to it would itself raise, so the band is two `Ada.Unchecked_Conversion` instantiations with the arithmetic done in the modular type. **The PLAIN arm in two of them rests on a compiler setting rather than on the language, the same shape as fortran's band in class 2:** nim's overflow checks are on by default but `-d:release` turns them off unless `--overflowChecks:on` is passed, and swift's trap is off under `-Ounchecked`. Neither is expressible in the emission; both are the build's to guarantee. EMISSION ONLY: no swift, nim or ada toolchain on this box.
- **Class 4, bignum, no fixed width.** python, ruby, elixir, scheme, javascript DONE (main 22426). **gtk joins this class and is done with them**: it emits Python (PyGObject), not C++. GRADED where a runtime exists: javascript and python each 57 of 57 values against x86-64 truth. The band rows overflow, so a bignum plug without the truncation cannot match, and they cannot pass by accident (L-VACUOUS). The trap arm's throw is not exercised: the oracle cannot grade a program that traps.
- **Class 5, double-valued, cannot represent i64. DONE** (typescript, angular, react, vue, svelte, electron, html, qt). Every one emits JavaScript or, for qt, QML with a JavaScript block, so a Codex Integer is a Number and a double holds an exact integer only to 2^53. **BOTH ARMS REFUSE and the node type is not consulted**: the band cannot wrap at 64 bits and the plain arm cannot tell an overflow from a rounding, and in range the two agree anyway, so there is nothing for `int-ty-wraps` to decide. The guard is on the VALUES (`Number.isSafeInteger` of both operands and the result), so a program whose integers fit a double still computes and only the unrepresentable case refuses; refusing every integer add outright would forfeit the 55 oracle rows that are correct today to answer the 2 that are not. The marker is `CODEX_REFUSED_integer_exceeds_double_precision`, which `plug-oracle-test.ps1` reads as its REFUSED verdict. **typescript is the graded exemplar and the other seven carry the identical arm**: it answers REFUSED on the oracle subject against x86-64 truth, with python PASS 57/57 as the negative control and a sabotage forcing python onto the refusal list as the positive one. The seven are not oracle-wired and could not be: they emit framework sources (React components, Vue and Svelte single-file components, an Electron main, an HTML page, QML) rather than a program node can run, so for them the build is the only instrument. lua, perl and php remain OUT of this class: the row places them here by a language claim nobody has measured, and each needs its integer width established first.
- **Class 6, integer narrower than 64 bits. OPEN:** java, kotlin, scala (32-bit, unit 2.21a), compose (emits Kotlin), ocaml (native `int` is 63-bit), wgsl (i32, already refuses a literal wider than 32 bits), and haskell, which emits bare literals with no `:: Int`, so GHC defaults them to `Integer` and its width is not what the class name assumes. Blocked on the representation: a checked primitive applied to a narrow int traps at the wrong boundary, which is worse than the wrap.
- **Class 7, not general-purpose expression emitters. RULED OUT OF 2.21, not deferred** (babbage, cobol, pascal, ptx, t3isa). The first question was whether the contract applies at all, and measured on what each emits, it does not. **babbage** and **cobol** return a structured result (a store-and-operate record, `PIC S9(18)` fixed decimal) rather than an expression, and neither target has a 64-bit two's-complement integer for the band to wrap in: COBOL's `PIC S9(18)` is 18 DECIMAL digits, not 2^63. **ptx** (`add.s64`) and **t3isa** (`TADD`) are machine targets whose add IS the hardware's, so the band is already whatever the ISA does and a trap would have to be synthesised in emitted instructions. **pascal** is the one worth revisiting if anyone installs a toolchain: it is a real expression language, so the class is a statement about the other four rather than about it. Nothing here is blocked; the contract simply does not reach these emitters, and saying so is the answer rather than a deferral.

GRADING. `codex/test/ops/int-mul-wrapping` and `int-add-wrapping` are the wrapping arms and must PASS on every lane; a trapping arm is x86-only until a lane traps. `codex/test/plug-oracle-arith` carries `wrap-mul` and `wrap-add` rows, so a plug answering a band op with a bignum or a double fails the oracle. A text plug is graded only where its toolchain is on the box: zig and csharp run here (57/57 each), and rust, groovy, go, java, kotlin and scala have no toolchain on this box and are accepted by EMISSION only. A trapping probe is not a fixture, because bare metal cannot grade a program that traps: it lives in the CL description with its control, which is the same emitted program with only the probe function's operator swapped.

## 2.22 -- OPEN (fester, 2026-09-02): a bundle's staleness check reads only the plug's own chapters

`build/deck-headroom.ps1 -Plugs` now decides staleness on a CONTENT digest of the
plug's sources (main CL for the mtime fix), and the digest covers the plug
directory's own `*.codex` only, which is the set the mtime check covered. But
`Add-PlugChapter` also bundles compiler declaration chapters, Lir,
`codex/plugs/common/PlugTypes.codex` and `IRTextParser.codex`. A change to one of
those leaves every bundle reading FRESH while every bundle is in fact out of
date, so the plug deck check measures the previous revision and says nothing.
Widening the digest to the assembler's actual input list is the fix; it changes
which plugs read stale, so it is its own change and its own rebuild, not a
rider on the mtime one. Named rather than built on root's ruling, 2026-09-02.

## 2.23 -- OPEN (reek, 2026-09-02, measured not investigated): two wasm-e2e subjects disagree with x86-64 on a non-ASCII character

wasm-e2e.ps1 over all 31 fixtures after the 21960 plug rebuild: 29 passed,
2 failed. cce-text-rt and 
aw-bytes-rt both fail the same way, and only on
the accented rows: where x86-64 prints the letter, the wasm arm prints the two
bytes of its UTF-8 form, so the lines differ by exactly one character
(cce-text-rt 151 against 149, 
aw-bytes-rt 68 against 67). Every other row
of both subjects agrees, including the numeric-code-unit and length rows, so
this is the accented LITERAL path rather than Text generally.

Those two subjects are the ones 1.61 and 1.60 added when the plug's Text was
moved off end-to-end UTF-8 onto CCE, so this is either a regression of that
work or the harness comparing a UTF-8 capture against a CCE one, and which of
the two it is has NOT been established. Noticed while grading an unrelated
change; not caused by it, and the check is mechanical: neither subject
contains a # at all, so wat-lit-pat-const never sees a hex spelling in
either. Unowned.

## 2.26 -- the shared scratch path is CLOSED; two runs of one plug still cannot coexist, for a different reason

**All 56 `run.ps1` now key their scratch to the run** (reek, 2026-09-07). Each
derives `$RunTag` from `-Out`, falling back to `$PID` when `-Out` is empty, and
writes `last-run-$RunTag.ir` and `run-$RunTag.log`. The fallback is deliberate:
whether every script makes `-Out` mandatory is a census I got wrong twice, and
a tag that cannot be empty does not depend on the answer.

The trap it removes: a fixed `build-output/last-run.ir` and
`build-output/run.log` meant two invocations of one plug raced, and the loser
read the winner's IR. It did not fail. Both runs exited 0 with well-formed,
plausible output for the WRONG subject, so a harness running plug runners in
parallel reported a content difference that read as an emitter defect. Measured
on wgsl at 2 concurrent: five gpushow kernels graded DIFFERS, the tell being
ReflectKernel coming back at exactly D20Kernel's line count (L-SUSPECT).

**THE CROSSING WAS ONLY EVER REACHABLE FOR THE FILE-SERIAL PLUGS, and that is
worth knowing before anyone reads this row as a fleet-wide near miss.** The 38
plugs that go through `build/plug-run.ps1` each pass their own fixed TCP port,
so a second concurrent run of the same plug REFUSES at the listener (`FAIL: TCP
9131 is already in use ... the port is fixed per plug and shared across
workspaces (L-SHARED)`) before it can reach the scratch. The 11 that preload
serial with `-input` have no port to collide, which is why wgsl, one of them,
is where the crossing was actually observed.

**Proven 2026-09-07, and only as far as it goes.** Sequential and concurrent
runs of `python/run.ps1` on two subjects: all four runs produced their own
scratch pair, and the concurrent run that got the port answered byte-identically
to its sequential baseline (4,908 bytes, same hash). **The other concurrent run
refused on the port, so this measurement does NOT show two same-plug runs
coexisting** -- it shows the scratch no longer crosses and the output is
unaffected. Making them coexist is the port, which is 1.73's residue.

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

His `WASM_FINDINGS.md` 4 (`show` on a `Real` prints its bit pattern) is not
routed: it is fixed on OUR side, `$f64_to_text` in `WasmEmitter.codex` at head
2026-09-07. His items 7 and 8 belong to `angry-gopher` and are not ours.


## 2.29 -- CLOSED (reek, 2026-09-07): all 40 lenses stream and answer the whole compiler; fortran was the last

Damian, on the public site: the JavaScript lens errors on the compiler source. Reproduced
in wasmtime on the 17,010,694-char IR-UNI of `build/output/Codex.codex` at seed 81F9E817:
the trap is `unreachable` in `grow_by` under `bump_alloc`, reached from `text_append` inside
the emitter's defs loop (`emit_ts_defs`, `emit_py_defs`, `js_sanitize` under
`js_emit_defs_loop`), which is memory exhaustion, not a codegen refusal. The cause is one
shape in every lens: `emit-X-chapter` builds the WHOLE program as one Text, and the defs
loop concatenates right-recursively (`emit-X-def d & emit-X-defs rest`), so each
definition's text is copied into every longer suffix under an allocator that never
reclaims. The C# lens does not have it because `plug-emit-ir-spans` streams one definition
at a time between `__heap-save` and `__heap-restore`.

All 40 are streamed and answer the whole compiler. ptx and wgsl are kernel lenses and the
compiler is not their subject.

cobol is the newest and its shape is worth reusing. `CobolStdio` emits a head, then
per-definition storage, then per-definition working storage, then the PROCEDURE DIVISION
and per-definition code, with `__heap-save`/`__heap-restore` around each definition.
COBOL fixes the order, every WORKING-STORAGE item before the PROCEDURE DIVISION that uses
it, and those storage lines are a side effect of emitting the paragraphs, so each
definition is emitted TWICE. What made it tractable is narrower than it looks: the only
thing crossing a definition boundary is `var-counter`, which names every temporary and
never resets, and it is an Integer, so it survives the restore where the emitted text does
not. That is python's `py-uses-mask` first pass rather than a new shape.
`working-storage` is append-only and read once, and `env` and `current-func` reset per
definition, which is why nothing else has to survive. Both passes start from counter 0 or
the two halves name different variables. The whole compiler emits 2,068,394 B of COBOL
from a 1,299,513 B IR-UNI. `emit-cobol-chapter` is untouched, so the network plug and the
oracle are unchanged.

fortran was the last and it is done. `FortranStdio` streams the module in
`emit-fortran-chapter`'s own order (module head, lambda defs, the dispatch's lambda arms
then def arms, the per-definition functions, module tail), each item under a mark taken
ABOVE `lams`; `fort-emit-apply-dispatch` is split into `fort-dispatch-head`/`-tail` so
both paths share one copy of the fixed text. `fort-collect-lams` now saves the heap,
computes the Integer key, restores, and only THEN allocates the `FortLam`, so a lambda's
hashing text is transient while its record persists. The whole compiler emits 856,125 B
of Fortran from a 1,299,513 B IR-UNI. `emit-fortran-chapter` is unchanged for the network
path.

**READ THIS BEFORE TRUSTING THE PARITY GREEN: the lambda half went in UNGRADED, and 2.42
says why.** Parity was proven on `roc-closure-captures-list.codex` against an oracle
rebuilt from head (14,927 B identical plus the one stdio terminator), but that subject,
`factorial`, the lens subject and the COMPILER ITSELF all carry zero `(lambda` in their
IR, so `fort-collect-lams`'s `IrLambda` branch, `fort-emit-lam-def` and
`fort-dispatch-lams` were never executed. The green covers the type defs, ctors, dispatch
DEF arms, per-definition functions and the head/tail split. It does not cover the lambda
path at all.

Two fortran costs remain and are quadratic in the LAMBDA COUNT rather than the text, so
they are invisible until something produces lambdas: `acc & [FortLam {...}]` in
`fort-collect-lams` copies the accumulator on every append, and `fort-lam-seen-before`
rescans the prefix per lambda in both `fort-emit-lam-defs` and the dispatch. Neither is
worth paying for while 2.42 holds.

**THE PARITY TRAP, and it costs a codegen hunt.** The stdio module and the bare-metal plug
DO NOT SHARE A CONTRACT: `plug-emit-ir-stream` wraps its output in `print-line-uni`, which
appends one newline, while `CobolPlug` writes `emit-cobol-chapter`'s text raw. A harness
comparing the two reds on LENGTH by exactly one `0x0A` with the whole prefix identical,
which reads as a codegen defect and is a terminator (L-SHORT, L-BOTHARMS). Grade the
module against the STDIO contract, or allow the terminator explicitly.

The census that found this (root, 2026-09-03) ran every IR lens over the same input for
exit code and bytes out: four answered (zig, html, wasm, csharp) and forty trapped in
`grow_by` within 1 to 7 s. None trap now.

The recipe is the two landed CLs: stream in the Stdio chapter, restore the heap per
definition, lift the header, and where the header depends on the body (python's helper
gate) take it from a first pass. Each lens needs its own read (R-READ): the header
assembly differs per emitter. Grade with `page-lens-test.ps1 -Only <lens>` for parity and
with the compiler IR for the memory. The census script is one loop over `page-lenses.ps1`
feeding `wasmtime` the IR; `page-lens-test` grades a 29-name subject and cannot see this.

## 2.30 -- a plug build resolves its cites against a RELATIVE path

The stale-kernel half is fixed in all 37 `run.ps1` (reek, 2026-09-07): each
takes `-Kernel`, resolves it absolutely, creates `build-output` so
`compile.ps1` can write the log its own error names, and prints the `kernel:`
digest. The second trap in the same place is NOT fixed. A plug `build.ps1`
run from anywhere but the repo root fails with `error 3010: Unresolvable cite:
Foreword chapter 'ListUtils' ... (expected .\codex\foreword\core\ListUtils.codex)`,
naming a chapter that is present, correctly named and in the depot. It reads
as a broken tree rather than a wrong working directory, and it took all six
class-4 builds down at once. **A driver must pin the working directory;
`Start-Process` without `-WorkingDirectory` inherits the launcher's.**


## 2.31 -- OPEN (unowned, measured by red 2026-09-07): the go plug asserts a type on literal operands, which is not valid Go

`emit-go-expr` emits a literal bare and every consumer appends `.(int)` / `.(string)`, so
the emitted program carries `0.(int)`, `9223372036854775807.(int)` and
`"band-sub want ...".(string)`. A Go type assertion requires an INTERFACE operand; on an
untyped constant it does not compile. Measured on the family-2 probe emission at kernel
81F9E8171DCF6268: the arithmetic bodies are well formed (`func plain_add(a interface{}, b
interface{}) interface{} { return _cx_add_trap(a.(int), b.(int)) }`) and the `opening`
body is not. Pre-existing and independent of 2.21, which changed only which FORM the
operands are fed into. No go toolchain on this box, which is why no run has ever said so;
the check costs one `go vet` wherever a toolchain exists.

## 2.32 -- OPEN (reek, 2026-09-07): `build-page.ps1` ships a page with every language lens DARK instead of refusing

A target plug whose binary is absent is treated as an optional lens that "stays
dark", so `build-page.ps1` exits 0 and emits a complete-looking `prism.html`
carrying none of the target modules. Measured 2026-09-07 on a workspace
purified by `build/p4-purify.ps1`, which deletes every untracked file and so
removes all 53 target plug binaries: the rebuilt page was 53 lines shorter than
the live one, nothing added, and each missing line is one embedded module
(`javascript-stdio.wasm`, `csharp-stdio.wasm`, `python-stdio.wasm`, cobol,
fortran, zig, rust, go, java and the rest). Every lens on the page would be
dead. Nothing in the build says so.

This is L-BAILVALUE: the guard produces a VALUE rather than refusing, so no
caller can tell it fired. The failure is worse than a dark lens because the
artifact is the thing we publish: the page is plausible, complete, smaller, and
its own build calls it green. It was caught only by a `git diff` against the
live site before the publish, which is a human reading a diff (L-BODY), not a
runner. Not published; the live compile page is untouched.

The fix is a refusal at the point the modules are gathered, naming the missing
plugs, with a switch for the deliberate case of building the page without
them. The count is decidable from `page-lenses.ps1`, which already knows the
lens set, so the check is "how many of the lenses I know about have a binary",
not a hardcoded 53.

The wider fact, which is not this plug's to fix but bites anything that
assembles the site: a purified workspace cannot build the landing bundle at
all until the plug binaries and `build/output/Codex.codex` are regenerated.
`apps/landing/build.ps1` fails at section 1 with `MISSING html-plug.cdx` and
at section 4 with `REFUSE: no concatenated compiler source`, both of which are
honest refusals. This one is not.
## 2.36 -- `show` on a Real does not compile in the zig plug

**The two conversion arms are DONE (reek, 2026-09-07), and the row understated
the work: zig lacked `to-real-approx` as well as `real-approx-to-int`**, so the
subject needed two arms rather than the one this row named.

- **`to-real-approx` is `cvtsd2ss` on bare metal, so it NARROWS**, and the
  precision loss is the whole content of the operation. A Real is f64 in this
  plug and an approx Real is the exact f64 widening of an f32, which is the
  convention `cx_bits_to_real_approx` already states, so the faithful form is
  the round trip through f32. Returning the f64 unchanged would answer a number
  bare metal never produces.
- **`real-approx-to-int` needs no function of its own.** On metal it is
  `cvtss2sd` then `cvttsd2si`; here the operand is ALREADY that widening and
  f32 to f64 is exact, so `cx_real_to_int` is the right callee, guards
  included.

Measured: the emitted zig built with zig 0.16 and RUN answers all eight
gradeable lines of `codex/test/ops/real-to-int-wide` identically to the
battery's own `.expected`, `approx-wide 3000000000` among them. The control
reproduced first: the depot plug carried eight `@compileError` refusals.

**THE SUBJECT NOW BUILDS AND RUNS, all nine lines matching the battery's own
`.expected`** (reek, 2026-09-07), `show-real 3000000000.5` included. It took
three separate things, and only the first was the conversion this row named:
the two arms above, then the typer arms that let an existing refusal fire at
all, then `cx_real_to_text` itself (2.13).

**The refusal was already written and could not fire, which is the part worth
keeping.** `zig-show-arg` has tested `zig-is-real-type` since it was written;
what failed is upstream of it. `zig-expr-type` had NO arm for `IrNumLit`, so a
real literal fell to `is otherwise -> VoidTy` and every type-directed decision
below asked its question of the wrong answer. `IrBoolLit`, `IrCharLit` and
`IrNegate` had the same hole, the first two being literals whose node kind IS
their type and the third carrying its type in the node. All four now answer,
and a guard that exists but is unreachable is L-UNCALLED with the caller
present.

**Blast radius measured rather than argued, because this moves a type-directed
decision:** three subjects emitted before and after, `plug-oracle-arith` and a
`poke-32` subject BYTE-IDENTICAL, and `real-to-int-wide` moved by 31 bytes, all
of it the one `show` call becoming the refusal. `plug-oracle-test -Only zig`
PASS 57 of 57; `check-zig-prelude-surface` OK.

**`check-zig-prelude-surface.ps1` was RED AT HEAD and is FIXED (reek,
2026-09-07); the defect it named was real.** `cx_w` and `cx_x` are function
LOCALS inside three prelude parts (`cx_poke_32` and `cx_poke_16` declare
`const cx_w: u64 = @bitCast(v)`, `cx_memset` declares `const cx_x: u8`), which
is why neither appears as a top-level name anywhere in the emitter. They were
absent from `zig-prelude-decls`, so nothing renamed a user definition away from
them.

**Zig forbids a local shadowing a container declaration, so this refused whole
programs.** Measured end to end on a subject whose definition is named `cx-w`
and which reaches `poke-32`: without the reservation the user's definition
emits as `fn cx_w` and zig refuses with `local constant shadows declaration of
'cx_w'` **pointing at the PRELUDE's line**, the user's own line appearing only
as a note. That is 2.34's shape exactly, and it means any program defining
`cx-w` or `cx-x` and touching those three builtins could not be built, with an
error naming code the author did not write. With the reservation the definition
becomes `cx_w_`, zig builds clean and the program answers `cx-w-collide 42`.

Both names are now in `zig-prelude-decls` (190 covering a surface of 188). The
check is calibrated rather than merely green: removing one name puts it back to
`MISSING (1): cx_w` and exit 1. `plug-oracle-test -Only zig` PASS, 57 of 57.
**No subject was added, deliberately: the check IS the runner for this class**,
deriving the surface from every part rather than from whichever ones a subject
reaches, so the next prelude local added without a declaration fails it.


## 2.38 -- ~45 language plugs emit code no runner ever executes

The three undefined typescript builtins are fixed (red, 2026-09-07). The
argument they carry is the gap, not those three: nothing in the tree ran a
text plug's OUTPUT until `plug-oracle-test.ps1` did, and typescript was wired
into it only on 2026-08-17, so the same shape is possible in any of the ~45
language plugs whose output no runner executes. It surfaced three times in one
session, one builtin per run, each reading as "the plug does not refuse" while
the program was dying before it reached the row under test. **An undefined
callee is mechanically decidable from the emitted source**, which is the check
worth writing.


## 2.40 -- OPEN (unowned, found by reek and measured by red, 2026-09-07): five plugs answer 0 for an arity MISS, where a zero-parameter definition also answers 0

`lookup-arity` returns a miss as 0 in go, html, java, qt and wpf, and as -1 in the
other 39 emitters. In those five, `ar == 0` can no longer distinguish "a
definition taking no parameters" from "not a definition at all".

**Not live today, and the reason is the same in all five**: each tests `ar < 1`
or `ar > 0`, which treats 0 and -1 alike. The six plugs that DO branch on
`ar == 0` to emit `name()` -- angular, electron, react, svelte, typescript, vue
-- all return -1, so an unknown name correctly falls through to a bare
reference. The hazard is a future reader copying one of the five arity maps into
a plug of the second shape, where every unknown name would silently become a
call.

Two corrections to how this was first stated, both worth keeping because each
was a plausible reading that measurement refused. It did NOT change recently:
qt has answered 0 in every revision of its file on main, #1 through #18. And qt
is not the odd one out; it is one of five. **The census that found "eleven"
plugs was wrong for a reason that recurs**: `if pos >= len then 0` is a SUBSTRING
of `if pos >= len then 0 - 1`, so a substring match counted six `-1` plugs as
`0`. A miss has TWO branches (index past the end, and name mismatch at the
index) and both have to be read; matching one spelling of one branch is
L-CENSUS one level down.

## 2.41 -- OPEN (reek, measured 2026-09-07): a plug `run.ps1` hardcodes its 3072 MB guest, so a granted memory cap cannot be honoured

Measured over `codex/plugs/**/run.ps1`: 53 runners pass `-MemMB 3072` to
`build/plug-run.ps1`. Exactly two, csharp and wpf, expose it as `[int]$MemMB =
3072` that a caller can override. The other 51 write the number inline.

The cost is coordination rather than memory. When the commander grants a run at a
stated cap, a lane driving any of those 51 cannot comply: the only routes are to
edit a shipped build script so it fits the grant, or to drop the arm that needs
the guest. Both are worse than the extra gigabyte, the first because a grant does
not get to reshape a script and the second because it buys an unverified claim.
reek hit this on fortran and red on kotlin and scala, both 2026-09-07, and both
had to stop and ask.

The fix is the shape csharp and wpf already carry: a `[int]$MemMB = 3072`
parameter threaded through to `build/plug-run.ps1`, so a cap is passed rather
than negotiated. No default changes. The callee already supports it:
`build/plug-run.ps1` declares `[int]$MemMB = 2048`, so the 51 runners are
overriding a working parameter with a literal, and nothing new has to be built.

## 2.42 -- OPEN (reek, measured 2026-09-07): the compiler lifts every lambda before IR text, so no compiled subject can reach any plug's lambda arms; hand-authored IR is the only route

The transport supports it at both ends and the plugs implement it, but nothing produces
one. `IRTextEmitter` emits `(lambda (params ...) ...)` and `IRTextParser.codex:710` parses
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
`IRTextParser.codex:710` accepts the `lambda` atom, so a `(lambda ...)` written by hand
reaches the arms without any compiler in the path. That is almost certainly what the
2026-08-18 probe did (`FortranEmitter.codex:523`, five lambdas, which is why the key is a
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
real hash rather than 0, which is what grades 2.29's `fort-collect-lams` change: it emits
the body, hashes it, restores the heap, and only then allocates the record, so a restore
that freed something live would give a garbage key or a trap.

**The residue is byte parity for this path, and it is blocked on ENCODING rather than on
the plug.** `build/plug-run.ps1:93` writes a hardcoded mode byte 1 (IR-CCE) and that file
is generated from the shell DSL, so hand-authored IR-UNI cannot be handed to the bare-metal
plug for a whole-program comparison without a CCE encoder for hand-written text. The wasm
arm is graded by inspection above; the whole-program arm is not graded on any lambda.

Until then this is L-UNCALLED at transport scale: every lambda arm in all 45 plugs is code
no test can execute, and a change to one of them lands ungraded no matter how carefully it
is graded on everything else. That happened here, deliberately and on the record, in 2.29.

## 2.43 -- CLOSED (reek, 2026-09-07): all eight JS-number targets emit a Codex Integer as a BigInt

**DONE.** typescript (reek 23144, main 23146) and then react, vue, svelte, angular,
electron, html and qt (reek 23177, main 23179). Every one is now exact to 64 bits
rather than to 2^53, on javascript's in-tree representation. Graded by reading each
emission over `int-pow.codex` and the lens subject: all eight emit `ipow(3n, 34n)`,
and across the lens subject each shows zero `Math.trunc`, zero `>>>`, zero
`isSafeInteger`, zero `Math.pow`, 74 `asIntN` guards (html 76) and 76 BigInt literals
(typescript 78).

**Three defects the port would otherwise have shipped, none of which a build catches
and all of which came out of reading the emitted text:**

- **`Math.trunc` throws on a BigInt**, and all eight used it for `IrDivInt`. BigInt
  division already truncates toward zero, so the call is removed rather than replaced.
- **qt emitted `Math.pow`**, which throws on BigInt operands. Now `**`, exact on
  BigInt. qt needed the pow repair 2.34 could not give it AND the representation
  change, which is exactly why it was never a member of that row.
- **html's `bit_shru` used `>>>`, which JavaScript does not support on BigInt at
  all** and which throws. It is now `BigInt.asIntN(64, BigInt.asUintN(64, a) >> n)`,
  which is what an unsigned 64-bit shift means; `bit_shl` gained an `asIntN` wrap so a
  shift past the width truncates to the declared width instead of growing unbounded.
  html's whole bitwise family also used `Math.trunc` on every operand.

**The old guard had to go or nothing would have run.** `Number.isSafeInteger` is false
for every BigInt, so the pre-existing check raising
`CODEX_REFUSED_integer_exceeds_double_precision` would have refused every trapping
integer op in all eight. It is javascript's guard throughout now:
`BigInt.asIntN(64, v) !== v` raising `CODEX_TRAP_integer_overflow`.

**One shape, but not one substitution**, and the per-plug divergences are worth
knowing before touching this family again: vue and angular emit TYPESCRIPT and carry
`: number` annotations; svelte, electron, vue and angular spell `char_code` with
`typeof c === 'string'` where react uses `c.charCodeAt ?`; qt has no `char_code` or
`char_at` at all; html carries an integer builtin family the others do not.

Split out of 2.34, which repaired integer power by helper in ten plugs and could not
repair these. **The `pow` call was never their defect.** A Codex `Integer` emitted as a
JS `Number` is an IEEE double with a 53-bit significand, so the MULTIPLICATION is the
lossy step and a square-and-multiply helper written over Numbers is exactly as wrong as
`Math.pow` was.

**MEASURED 2026-09-07, and it corrects this row's own first draft and 2.34 before it:
javascript is NOT a member. It already emits BigInt and is exact.** Fed
`int-pow.codex`, the nine modules answer:

| javascript | 32 BigInt literals, 1 `BigInt()` boundary call, emits `ipow(3n, 34n)` and `return (a ** b)` | EXACT, `**` on BigInt is exact by definition |
|---|---|---|
| typescript, react, vue, svelte, angular, electron, html | ZERO BigInt anywhere, emit `ipow(3, 34)` and the same `return (a ** b)` | wrong above 2^53 |
| qt | ZERO BigInt, and `return Math.pow(a, b)` | wrong, and by two mechanisms |

The contrast is the whole finding: javascript and typescript emit the SAME `a ** b`
and differ only in whether the operands are BigInt. So this was never a question about
the operator, and it is not a question about `pow`.

**That also settles the design, which this row previously framed as an open choice
between three shapes.** It is not open: javascript already made the choice in-tree,
and the work is to bring the other eight to it rather than to invent anything. The
boundary cost is measured rather than feared: over 18,065 bytes of emitted JS from the
lens subject there are about 24 sites where an integer meets a JS API that wants a
Number (`.length` 5, indexing 4, `charCodeAt`/`charAt` 4, `split`/`join` 3,
`String`/`Number` 8) against roughly 390 numeric touchpoints, and javascript already
spells the conversion at each: `BigInt(xs.length)`, `BigInt(t.charCodeAt(i) - 48)`,
`let v = 0n`, `v * 10n`.

**The one hazard to carry across is `===`.** `1n === 1` is false, so a mixed
comparison silently answers wrong where `<` and `>` coerce and behave. The lens
subject emits 207 comparison sites, and they are the part of this port to read rather
than sweep.

qt reached this row by BEHAVIOUR after the spelling put it with the double-`pow`
group: it emits QML JavaScript (`function __narrow(n) { ... }`, `outputModel.append`).
gtk went the other way and is NOT here, because it emits Python, whose integers are
arbitrary precision. Two misfilings, both caught by reading the target rather than the
operator, which is the whole of L-CENSUS.

This is an Integer REPRESENTATION change, not an operator repair: it touches every
integer operation in the emitted program rather than `pow`. The reference is
`JavaScriptEmitter.codex`, and the eight are ported to it.

**How this row was wrong, because the correction is the lesson.** 2.34 listed these
nine by the SPELLING of their pow arm, all of them emitting `**`. This row inherited
that list and asserted the defect of all nine without emitting one. One run of the
nine modules over `int-pow.codex` separated them in about a minute. That is the third
plug this census has misfiled by spelling: gtk (acquitted, emits Python), qt
(convicted, emits QML JavaScript rather than sharing the `Math.pow` group's shape),
and now javascript (acquitted, already BigInt). Each was settled by reading the
emission rather than the emitter.

## 2.44 -- CLOSED (reek, 2026-09-07): cobol's `COMPUTE ... ** ...` is exact for the integer exponents this emitter produces; the negative exponent is guarded and the floating-point premise is withdrawn

`CobolEmitter.codex:628` emits `COMPUTE <result> = <l> ** <r>`.

**THE FLOATING-POINT PREMISE IS WITHDRAWN, read 2026-09-07 against documented
behaviour.** 2.34 carried this row as "COBOL leaves exponentiation free to be
evaluated in floating point, so `3^34` is a plausible wrong answer". It is not.
COBOL enters the float path on a FRACTIONAL exponent or a floating-point
operand, and this emitter can produce neither: `cobol-pic` maps every Integer
to `PIC S9(18)`, so both operands are integers with no decimal places. IBM
Enterprise COBOL evaluates a nonzero integer power as a succession of
FIXED-POINT multiplications, carrying 30 digits of intermediate result under
the default `ARITH(COMPAT)` and 31 under `ARITH(EXTEND)`; `3^34` is
16677181699666569, 17 digits, inside both that and the receiving field.
GnuCOBOL holds its decimal intermediates in GMP rather than a double. Running
either is out under the toolchain rule, so this is a reading and not a run,
which is what the row asked for; the two runtimes read agree.

**The residue, and the whole of the row now: a NEGATIVE exponent.** `__ipow`
(`codex/compiler/Emit/X86_64TextHelpers.codex:395`) is square-and-multiply and
answers 0 for a negative exponent. COBOL divides into 1 instead, which IBM
documents as the extra step. So `5^-2` truncates to 0 in an `S9(18)` field and
agrees by accident, `1^-1` is 1 and does not, and `0^-5` is a divide by zero
with no `ON SIZE ERROR` to catch it. `codex/test/ops/int-pow.codex` carries
`neg-exponent` as `ipow 5 (0 - 2)`, one of the cases that agrees, so no arm in
the corpus can see this (L-CONSTRUCT).

**THE REPAIR IS NOT A PERFORM LOOP, and that follows from the withdrawal.**
This row's first draft called for an integer-power paragraph with its own
working-storage counter, because if `**` itself were floating point the whole
operator had to be replaced. It is not, so only the one case it gets wrong is
replaced: `IrPowInt` emits `IF <r> < 0 / MOVE 0 TO <result> / ELSE / COMPUTE
<result> = <l> ** <r> / END-IF`, in the shape `IrAnd` and `IrOr` already use.
No new paragraph, no counter, no prelude.

**Graded by reading the emission of `codex/test/ops/int-pow.codex`** (the
method 2.34 used), plug rebuilt and run under depot seed `seed/Codex.cdx`: one
`**` site, guarded, and `WS-IPOW-A` / `WS-IPOW-B` both declared `PIC S9(18)`
in the emitted `WORKING-STORAGE`. That last is the premise confirmed from the
plug's own output rather than from `cobol-pic`'s source: with both operands
integral, the float path is unreachable. The guard's own justification is at
the arm, because a reader who does not know COBOL divides into 1 will read it
as redundant and no arm in the corpus goes red if it is removed
(L-CONSTRUCT).

## 2.47 -- CLOSED (reek, 2026-09-07): cobol emitted `TEMP-QUOT` for every `int-rem` and declared it nowhere; the quotient goes to the already-declared `WS-TEMP`

`CobolEmitter.codex` emits `DIVIDE <l> BY <r> GIVING TEMP-QUOT REMAINDER
<result>` for `IrRemInt`, and `TEMP-QUOT` appears in no `WORKING-STORAGE` the
emitter produces: grepped over the whole plug 2026-09-07, its only occurrences
in the tree are that line and its two `build-output` copies. Every other
scratch field the arm table uses (`WS-RESULT`, `WS-TEMP`, `WS-STRPTR`,
`WS-LIDX`) is in `cobol-fixed-ws` and in the block `emit-cobol-chapter` writes
inline; this one is not in either.

**CONFIRMED FROM THE EMISSION before it was touched**, which is what this row
asked for rather than a re-read of the source: `codex/test/ops/int-rem.codex`
through the plug under depot seed `076181B2` gave three uses of `TEMP-QUOT` at
lines 297, 307 and 313 and zero declarations of it anywhere in the 294 lines
of `WORKING-STORAGE` above.

**The repair is NOT the declaration this row first called for.** The same
emission shows `WS-TEMP` DECLARED and referenced by nothing at all: one hit in
`int-rem`, one in `int-pow`, the declaration itself in each. So the quotient
goes there, which is one name changed in one arm and fixes both emission paths
at once. Adding a new name would have meant editing `cobol-fixed-ws` AND the
copy `emit-cobol-chapter` duplicates inline, and only one of those two is on
the streaming path, so the repair that looked smaller was the one with a
missable half.

Graded by re-emitting and diffing: three lines changed, nothing else, zero
`TEMP-QUOT` remaining, every quotient into the declared `WS-TEMP`.
`build/check-plug-builtins.ps1` green (11 builtins on the wire, 23 known gaps).

`WS-RESULT` is still declared and still referenced by nothing, in every
program this emitter writes. Left alone deliberately: a dead declaration
compiles, and this row was about one that does not.

## 2.45 -- CLOSED (reek, 2026-09-07; measured by val): codex-vm answered 0 to every device port read from an APPLICATION PROCESSOR; an AP now gets the same device models the boot processor gets

`tools/codex-vm.c:3944-3973`: an AP's I/O-port exit serves COM1 and returns
0 for every other port, by design of that handler, because `handle_io` binds
its register file to VP 0 (84 register accesses name VP 0 by constant) and
is not safe to enter from another thread. Measured on seed `076181B2` at
`-smp 4`: proc 0 reads the NE2000's `CR`, `ISR` and `BNRY` as 2, 0, 70; a
`process-spawn` child claimed by an AP reads 0, 0, 0 from the same ports in
the same run, and a 600,000-round poll of `net-driver-recv-frame` on an AP
sees no frame while the boot processor's sees the host's SYNs.

**L-ARENA: the bed was LESS capable than the target in exactly the respect
under test.** A board's application processors do port I/O, so every service
that had to reach a device was pinned to core 0 on the bed (`gopweb-start`,
`process-spawn-on-core ... 0`) with the desk yielding once per loop to let it
run, and the PreemptiveScheduler stage 2 acceptance is proven on that
accommodation.

**THE REPAIR IS NOT THE REQUEST QUEUE THIS ROW PROPOSED.** The queue would
have parked the AP until the boot processor's run loop serviced it, and that
loop only turns when the BSP itself exits, so an AP's read could wait on a
guest that is idle. The two things that actually made `handle_io` unsafe are
both cheap to remove. It took its VP by CONSTANT, writing VP 0's registers
whichever processor had faulted: it takes a `vp` parameter now, and the 19
sites inside it were the whole of that binding, because it calls none of the
VP-0-bound helpers (`handle_device_mmio`, `uefi_handle_trap`, the `dbg_*`
family). And the device models are shared mutable state: `handle_io` is now a
wrapper that serialises on `io_lock` and calls `handle_io_locked`, which is
where the many returns are. The lock is taken only when `smp_cores > 1`, so a
uniprocessor run takes the path it always did (L-FALLBACK).

The AP exit arm no longer serves COM1 and answers 0 to everything else; it
calls `handle_io(&ctx, cpu_id)`, which serves COM1 at least as well, having
the REP OUTSB burst the stub lacked.

**Graded by `codex/test/smp-ap-port-io`, new with this row, falsifiable both
ways on the same subject binary:** a child pinned to core 2 reads the NE2000's
CR, BNRY and ISR, the parent reads the same three, and the arm compares the
two processors rather than asserting 2, 0, 70, which would go red on a driver
change rather than a bed change. On the pre-fix codex-vm (`#120`) it prints
`AP DISAGREES WITH BSP`; on the fixed one, `ap agrees with bsp`. Both runs
print `bsp read something True`, so neither is the vacuous pass a run with no
NIC on the bus would give (L-VACUOUS).

**The uniprocessor path is unchanged, measured rather than argued.** The same
diag image under the pre-fix codex-vm (`#120`) and the fixed one produces a
BYTE-IDENTICAL BOT census, 11,138 lines each, and banks the same 86 rows; the
whole difference in the bank is one timing row, `frame plain=980us` against
`985us`. That is what the `smp_cores > 1` guard on the lock and the `vp = 0`
at the two boot-processor call sites are worth.

**The accommodation is NOT removed here, and that is val's call.**
`gopweb-start` still pins to core 0 and the stage 2 acceptance still rests on
it. What changed is that it is now an accommodation rather than a necessity.

## 2.51 -- OPEN (val, 2026-09-08; reek's area): an application processor polling the NE2000 convoys codex-vm's `io_lock` and the boot processor's disk load crawls behind it

**Renumbered from 2.46 by reek, 2026-09-08, because 2.46 was already taken by a
landed row below and a register cannot answer a dispatch by number twice.**

`tools/codex-vm.c`: `handle_io` serialises every port exit on `io_lock`, a
plain `CRITICAL_SECTION`, and an AP running `web-mux-loop` re-takes it for
every NE2000 register read of every empty poll, with a `WHvRunVirtualProcessor`
round trip as the only gap. The boot processor's IDE string reads (about 306
exits a sector) queue behind that. Measured on seed `076181B2`, DeskVm
`87B724B7` at `-smp 4` with `gopweb-start` spawning unpinned: the BSP made
29,920 exits in 25 s (6.5 million with the service pinned to core 0), its
IDE census stopped at 553 of 636 string exits, and the 25 s frame is black;
at 60 s the load is complete, the frame painted and the desk at 15,550
iterations a second, so it is a convoy and not a deadlock. The web service on
the AP answers throughout. Nothing sleeps under the lock (the port `0xE0`
host sleep is inside `handle_io_locked`, but no guest code emits it).

A fair lock, a spin-then-yield on the AP's side after an empty NE2000 read,
or the BSP taking precedence would each end the convoy; the choice is the
plug's. The design that hit it is `PreemptiveScheduler.md` stage 2, which
keeps the pin lifted and carries the delay as the bed's cost.

**MEASURED, AND THE ROW SPLITS IN TWO: the AP polling is real, the boot
processor is NOT starved, and no fix is written** (reek, 2026-09-08).

**Nothing in `codex-vm.c` could settle the row, so the instrument came first.**
`exits` is ONE GLOBAL COUNTER, so a run where an AP takes six million polls and
the BSP takes thirty thousand reads exactly like a healthy run where the BSP
took them all. `IO BY VP: vp=N port-exits=N io-lock-wait-ms=N` is new, printed
at exit whenever SMP is on, and the wait is timed around the acquire alone: a
large wait against a small exit count IS the convoy, and a small wait is not,
whatever the totals say.

Both runs `-smp 4 -mem 3072 -hid-combo -disk seed/Codex.img -headless`, frame
and census at 25 s:

| kernel | vp | port-exits | io-lock-wait-ms |
|---|---|---|---|
| head `23AA66C5` | 0 (BSP) | 3,342,336 | 206.4 |
| head `23AA66C5` | 1 (AP) | 6,175,784 | 776.9 |
| the row's own `076181B2` | 0 (BSP) | 3,383,064 | 199.3 |
| the row's own `076181B2` | 2 (AP) | 6,227,450 | 721.4 |

**What is CONFIRMED**: the service runs on an application processor and polls
hard, 6.2 million port exits in 25 seconds, and the AP does contend, waiting
about 750 ms for the lock.

**What is REFUTED**: the boot processor is not starved by the contention. The
BSP took 3.3 million port exits of its own and waited **206 ms in 25 seconds,
under one per cent of the run**. Its IDE census is COMPLETE at 636 of 636 where
this row records it stopping at 553, and the 25 s frame is PAINTED where this
row records it black. The row's own kernel gives the same answer as head, so a
moved seed is not the difference.

**No fix is written, because a fix could not be shown to move anything**
(L-MECHANISM). A fair lock or BSP precedence would each redistribute a 206 ms
wait, which is not the reported symptom.

**What is left, and it is a different item**: 6.2 million VM exits in 25
seconds is a real cost whoever owns it, and it is the GUEST's poll loop rather
than this lock. `web-mux-loop` returning after 50 million consecutive empty
polls (main 23330) is a bound, not a yield. That belongs to the design that
lifted the pin, not to codex-vm.

**Whoever revisits the original symptom: quote `IO BY VP`, not `exits`.** The
global counter does not count what an AP does, and comparing 29,920 against 6.5
million across a change that moved work onto an AP is comparing two different
populations.

## 2.46 -- LANDED (reek, 2026-09-07): codex-vm refused SYNCHRONIZE CACHE and had no write-back cache to flush, so the bed could not rehearse the commit the last sitting flies

Two defects, one cause: the modelled BOT target was write-through and its
command allow-list did not carry 0x35.

**The refusal.** `tools/codex-vm.c` failed every command outside
`{0x00, 0x03, 0x12, 0x25, 0x28, 0x2A}` with CHECK CONDITION. The guest issues
0x35: `GopUsbMsc.codex:33` sets `scsi-op-sync-cache = 53` and `msc-sync-cache`
stamps it, `usb-sync-cache` answers -1 on refusal, and
`DiagStage.codex:162,206` fails the bank on `fl < 0`. So under blu's flush the
bed banks nothing and reports it as a bank failure, which is a bed artefact
and not a stage defect. 0x35 is now accepted, moves no data, and answers GOOD.

**The freedom the bed could not express (L-FREEDOM).** Accepting the command
is not enough on a write-through target: a BOT `WRITE_10` called `ide_flush`
in its own data phase, so the bytes were durable whether or not the guest ever
asked for a commit, and a run WITH the flush and a run WITHOUT it are the same
colour. Sittings 13 and 14 lost the bank on the board. `-usb-writeback` makes
writes durable only at SYNCHRONIZE CACHE and drops whatever is uncommitted
when the machine stops, which is what a real write-back cache does at
power-off. Off by default, so no existing arm moves. The dirty region is one
coalesced span, which is exact rather than approximate: the bytes between two
writes are unmodified, so `ide.data` already equals the file there.

**The instrument, because an arm here can pass vacuously (L-VACUOUS).** With
the flag on, exit prints `USB WRITEBACK: commits=N committed-bytes=N
lost-bytes=N`. A flush-absent arm reporting `lost-bytes=0` never reached the
condition it exists to test and its colour means nothing.

**The arms, named and MEASURED by hand 2026-09-07, not yet written into
`diag-arm.ps1`.** `Read-Bank` there already reads `DIAG.TXT` out of the image
FILE after codex-vm is killed, so power-off readback needs no new machinery,
and the kill IS the power-off. Run as `codex-vm -kernel IMG -disk IMG -uefi
-headless -output OUT -census CEN [-usb-writeback]`, then `read-stick.ps1
-ImageFile IMG -Name DIAG.TXT`:

- `bank-writeback` -- `-usb-writeback`, kernel `076181B2` built after blu's
  flush (23069). **186 cached writes, 16 commits, DIAG.TXT complete at 86
  rows.** The flush works and the commit lands.
- `bank-writeback-noflush` -- `-usb-writeback`, the shipping `diag.img` whose
  kernel `9E5C7780` predates 23069 by six minutes. **186 cached writes, 0
  commits, 4,618,240 bytes pending, DIAG.TXT ABSENT from the image, and the
  guest's own summary still says `bank=ok medium=usb`.** The target serves the
  updated directory entry back out of the same cache, so the size readback
  agrees with a write that never reached the medium, which is what
  `DiagStage.codex` says in its own words above the flush. **What this
  establishes is that the MECHANISM is real and is undetectable from inside
  the guest. It does NOT establish that it is what killed sittings 13 and 14**
  (blu, 2026-09-07), whose media died mid-run rather than at power-off; a
  mechanism that explains a symptom is not its cause until the fix moves the
  symptom (L-MECHANISM).
- `bank-writethrough` -- the CONTROL, the pre-flush kernel with NO
  `-usb-writeback`. **Bank whole at 82 rows, zero writeback lines, census
  otherwise byte-for-byte the same 11,135 BOT lines.** It is what says the
  loss is the model and not the kernel, and it says the flag changes no
  existing arm's census.

**The residual weakness, stated so the arm is written properly:** the two
writeback arms differ in KERNEL as well as in flush, because 23069 has landed
and the shipping image predates it. The control pins the conclusion, but the
clean form is one kernel with `disk-sync-cache` ABLATED, and that is how it
should go into `diag-arm.ps1`.

R-COST: five file-scope counters and two O(1) functions; no allocation, no
per-object cost. The one added `fprintf` per BOT write is inside the flag,
which is off by default.

Landed: the two units in `tools/codex-vm.c`, the rebuilt `codex-vm.exe`, and
the flag row in `OperatorsManual.md`. `-usb-writeback` parses and an unknown
flag is still refused (L-ACCEPTED, checked both ways).


## 2.48 -- a riscv fault is SILENT, so every one of them reaches the bed as FAIL_STARVED or FAIL_RUNTIME

`__trap_handler` re-executes the faulting instruction forever and prints
nothing, so the harness sees an incomplete run or no uart at all and the cause
is not in the output. Three of 1.100's four reds sat undiagnosed for a week
behind this, and two of them were one-line fixes once the PC was in hand.

What it should do is print the cause and the address and stop: arm64's bed
answers a fault the same silent way, so this is not riscv's alone, but riscv is
where it has cost a week.

Until then, the recipe that got the diagnosis, in full:

```
cpu CreateExecutionTracing "tracer" @<path> PC   # then RunFor, then Dispose
sysbus.cpu PC ; sysbus.cpu GetRegister <n>       # at the trap
sysbus ReadDoubleWord 0x<addr>                   # the instruction actually there
riscv64-linux-gnu-objdump -D -b binary -m riscv:rv64 --adjust-vma=0x80000080 <bin>
```

**The flat `.bin` begins at the ENTRY, 0x80000080, not at 0x80000000.** An
`--adjust-vma=0x80000000` shifts every address by 0x80, and correct code then
reads as misaligned and invents defects that are not there; that cost an hour
and a wrong reading of two comparison emitters on this row.


## 2.49 -- CLOSED (reek, 2026-09-08): the riscv plug FAULTED on five integer tests, and it was the plug crashing, not a refusal

Measured 2026-09-08 (reek), and PRE-EXISTING: reverting the 1.100 changes and
rebuilding reproduces it exactly, so it is not that work. `int-add-wrapping`,
`int-min-literal`, `int-wrapping-spelling`, `interval-exhaustive` and
`plug-oracle-arith` all end the same way, and `compile-riscv.ps1` says so
plainly:

```
FAIL: the plug FAULTED; ...last-compile.riscv.bin holds a register dump, not a wire.
  !EXC=06 RIP=000000000010f73e RBX=7fffffffffffffff R12=7fffffffffffffff
  R13=000000000000000a R14=ffffffffffffffff RDI=7fffffffffffffff RSI=7fffffffffffffff
FAIL: RISC-V codegen plug exited 7
```

**DIAGNOSED 2026-09-08 (reek): `rv-li-fits-32` OVERFLOWS on the value it is
asked about.** `RiscVEncoder.codex:383` reads

```
rv-li-fits-32 (value) = if bit-shru (value + #80000000) 32 == 0 then True else False
```

and `value + #80000000` leaves the signed 64-bit range for every value above
`#7FFFFFFF7FFFFFFF`, so the addition traps inside the plug before any encoding
happens. **The boundary was PREDICTED from the arithmetic and then measured, and
the measurement landed on the predicted byte**: `show #7FFFFFFF7FFFFFFF`
compiles, `show #7FFFFFFF80000000` faults. `show 1` compiles and
`show #8000000000000000` compiles, which is why INT64_MIN was never the subject
even though four of the five tests are named for the minimum.

The repair is a range test that cannot overflow, `value < -2147483648` or
`value > 2147483647`, rather than an addition that walks off the top.

**An earlier reading on this row said the dump was a divide-by-ten walk, from
`R13` holding 10. That reading is WRONG and is withdrawn.** `7fffffffffffffff`
in four registers at once was the whole of the evidence and the rest was
decoration. `arm64` compiles all five because `Arm64Encoder` builds immediates
another way.

This is NOT the `[UNSUPPORTED]` refusal the plug gives for `char-encode`,
`raw-bytes-to-text` and `vec-empty` (24 tests in the same battery, all of them
honest and none of them this). A refusal names the builtin and exits cleanly; a
fault hands back a register dump where a wire should be, which is what 2.06
warns about from the other direction.

**LANDED.** `rv-li-fits-32` tests the range against its two ends. Four of the
five now compile and PASS on the bed: `int-min-literal`, `int-add-wrapping`,
`int-wrapping-spelling`, `interval-exhaustive`. **The fifth, `plug-oracle-arith`,
now REFUSES instead of faulting**, naming `char-encode` as the builtin the riscv
plug does not emit, which is the honest answer and a separate gap.

The arm is `codex/test/rv-big-literal`, and the arm found 2.50 in the same
function family.


## 1.107 -- CLOSED (reek, 2026-09-08): riscv sum equality never reached the fields either, and the arm the arm64 half left behind proved it

1.90 predicted riscv shares the defect ("riscv is still unmeasured and shares
the IR-text parser") and left the prediction unmeasured. **Measured 2026-09-08
(reek): `codex/test/sum-field-eq` FAILS on the riscv64 Renode bed and PASSES on
arm64**, at merged head, with the 1.100 fixes in. The arm was built for 1.90
with nine rows that fail in BOTH directions, so a pointer compare, a tag compare
and a correct compare each answer differently, which is why the arm carries the
finding rather than merely reporting a red.

`rv-emit-sum-eq` (`RiscVCodeGen.codex:1096`) is the site, and 1.90's account of
the arm64 defect is the map: the ctors come from `defs`, NOT from the type,
because `IRTextEmitter` emits `(sum "Name" (args ...))` with no ctors, so every
sum arriving over IR TEXT carries an empty ctor list. Reading the empty list is
how arm64 answered "no fields" for every sum and compared the tag alone.

The arm64 repair is `a64-sum-ctors-for-type` and the five functions beside it,
and the refusals it makes (a second field-carrying constructor, a sum-typed
field) are deliberate and belong in a shared IR pass, which is COMPILER-63.
**Do not widen the refusal to `ConstructedTy` blindly**: a type PARAMETER
arrives as `ConstructedTy` and an over-broad refusal killed `eq-generic-fields`
on arm64 until the ctors were resolved against `defs` instead.

## LANDED (reek, 2026-09-08)

**riscv's defect was a THIRD shape, not arm64's.** arm64 compared two heap
POINTERS; riscv loaded the tag and then TWO WORDS at `+8` and `+16`
unconditionally, for every sum of every shape. So a nullary constructor had two
words of whatever followed the object compared as integers, a `Text` field had
its pointer compared rather than its bytes, and a constructor with three fields
had its third ignored. Reading the row's prediction as "the same defect" would
have been wrong about the mechanism while right about the conclusion.

`rv-sum-ctors-for-type` and the five functions beside it are 1.90's repair
ported: the ctors come from `defs`, one constructor may carry fields, no field
may itself be a sum, and both restrictions REFUSE rather than guess. A `Text`
field goes through `__str_eq`; everything else is `sub` / `sltu` / `xori`, since
riscv has no flags to fold into a `csinc`.

**MEASURED on the riscv64 Renode bed: `codex/test/sum-field-eq` is GREEN, all
nine rows**, and the arm was RED on riscv before the change, measured in this
same session. `recursive-eq` stays green.

**`eq-generic-fields` refuses at compile with `[UNSUPPORTED] __eq_Pair@Text`,
and the refusal is NOT this change.** Ablated: the plug rebuilt from reverted
source refuses identically. Earlier today the same test compiled and answered
`concrete-control : no`, so something between those runs moved it, and the seed
moved twice in the interval. A refusal is the better failure of the two, and the
missing `__eq_<T>` machinery is COMPILER-63 on both native backends.


## 2.50 -- CLOSED (reek, 2026-09-08): `lui` plus `addi` cannot reach the top 2048 values of the 32-bit range, and the rounding hid the fact

Found by 2.49's own arm, which is the point of building one: the arm was written
to straddle 2.49's boundary by a byte, and two of its ten rows came back wrong
for a DIFFERENT reason.

`rv-li-hi20` computes `(value + #800) >> 12`. For any value above `#7FFFF7FF`
the `+ #800` carries into bit 31 and the result is `#80000`, one past the
largest signed 20-bit `lui` operand, and the wrap turns it into `-#80000`.
Because `lui` SIGN-EXTENDS on RV64 the pair then builds `#FFFFFFFF80000000` and
adds a negative `addi`, so **`2147483647` was loaded as `-2147483649`**. The
same band is reached through `rv-li-64`'s low half, which is why
`#7FFFFFFF7FFFFFFF` came back exactly 2^32 short.

**Narrowing `rv-li-fits-32` to `#7FFFF7FF` is the obvious repair and it does not
terminate.** `rv-li-64` composes the low half by calling `rv-li` on a signed
32-bit value; if that value is outside the narrowed range, `rv-li` sends it back
to `rv-li-64`, which asks the same question again. The low half must be answered
where it is, never handed back. So the band is climbed instead: `lui #7FFFF`
reaches `#7FFFF000`, one `addi 2047` reaches `#7FFFF7FF`, and the remainder is
at most 2048, one more than an `addi` immediate holds, so `rv-li-top-band` adds
the remainder in at most two steps.

**MEASURED on the riscv64 Renode bed**: `codex/test/rv-big-literal`, ten rows,
all ten green, covering the boundary from both sides, both 32-bit ends and both
64-bit ends. `codex/test/riscv-encoder` gains six `rv-li-fits-32` rows and a
length row for INT64_MAX, and runs green on the riscv bed and on x86-64 under
codex-vm, byte-identical to the expected file in both.

**The length row is a real cross-check rather than a copied answer.** `li max
len=7` was derived by hand from `rv-li-64` before any run, and the run agreed.


## 2.52 -- LATENT (reek, 2026-09-08): `check-plug-ports` skips its host half in SILENCE when a plug has no `run.ps1`

A plug's TCP port is one fact in two places, and `build/check-plug-ports.ps1`
exists to hold them together. Its host half is guarded: `$run = (Join-Path $dir
'run.ps1')` at line 73 of the shipped script, and everything after it sits
inside `if (Test-Path $run)`. A plug whose `<Name>Plug.codex` dials a port and
which ships NO `run.ps1` therefore passes a check whose own name promises both
halves, with nothing printed and nothing counted. That is L-ACCEPTED: a check
that checked half looks exactly like one that checked both.

**MEASURED AT HEAD 2026-09-08 AND THE HOLE IS EMPTY: 45 plug directories under
`codex/plugs` carry a `*Plug.codex` matching `net-session-new`, and 0 of the 45
lack a `run.ps1`.** No plug in the tree takes the silent path today, therefore
no verdict this check has published is wrong. Said plainly rather than left to
be inferred from a green run, because a row that reads as a live hole gets
chased, and this one is a guard against a future plug.

The asymmetry is the part worth keeping in view: the GUEST half already refuses
an absence, printing `NO GUEST SOURCE dialing a port` and incrementing `bad`,
so one half of this check treats a missing file as a finding and the other
treats it as nothing to do. However it is settled, the two halves should agree.

Two repairs, and choosing between them is the work: count a missing `run.ps1`
as `bad`, the way the guest half already counts a missing source, which is
honest and fires the moment a plug arrives that is driven some other way; or
print a SKIPPED line with a tally, so the check reports what it did not check.
Neither is taken here.

Found by the pipeline-model migration (`PipelineModel.md`): declaring what a
stage consumes and what its verdict means is what made the silent branch
visible.
