# Plugs -- open capabilities

Quire-domain backlog, same rules as the app registers: an entry says what
is still missing and nothing else, a closed entry is DELETED, and a gap
that is still real is never quietly dropped. There is no platform-wide
register; `docs/PM/CurrentPlan.md` carries the shape.

**The standing hazard in this quire: a plug that does not handle a
construct usually EMITS SOMETHING ANYWAY and reports OK.** A missing
builtin arm passes the name through as an ordinary call; a wrong field
spelling emits a division; a wrong `list-push` emits a mutating append.
The target's own toolchain or runtime is the first thing to notice, and
for most of these plugs nothing downstream ever runs. Assume silence is
silence, not agreement (L-GAP).

## 1.2 -- THIRTY-SIX plugs emit a DIVISION where a record field access belongs

The IR spells a field as `name/slot` (`ir-field-with-index`,
`compiler/Emit/IRTextEmitter.codex`), and a plug that does not strip the
slot emits the whole thing as an expression.

**This entry said "fourteen" and named them. Measured 2026-08-11 by
running every plug and grepping its output, it is thirty-six** (L-COUNT).
The fourteen were the ones anyone had read; the rest were never checked.

| | plugs |
|---|---|
| **leak the slot (36)** | ada, angular, clojure, cobol, compose, d, electron, elixir, flutter, fortran, go, groovy, gtk, haskell, java, julia, kotlin, lua, nim, objc, ocaml, pascal, perl, php, qt, react, ruby, rust, scala, scheme, svelte, swift, swiftui, typescript, vue, zig |
| **clean (6)** | python, html, csharp, javascript, wasm, recheck |
| **emits no field reference at all (1)** | babbage -- a different gap, not this one |

Slot 0 divides by zero and crashes, slot 1 divides by one and is
SILENTLY CORRECT, slot 2+ is silently wrong; the map-lookup plugs (go,
flutter, groovy, elixir) get a silently missing key instead and need
different handling than a substring before the slash. Copy `py-field` /
`js-field`.

**The deferral is lifted for DETECTING it.** This entry used to say the
change "cannot be verified today" because no runtime is wired into
`build/plug-oracle-test.ps1`. That is the standard for proving a fix
CORRECT and it still holds. It is not the standard for seeing the
defect, which is visible in the emitted text with no runtime at all:

```powershell
# 10 lines of Codex; the whole probe is a record built and one field read
pwsh build/compile.ps1 -Src <probe>.codex -Out probe.ir -Log probe.log -IrCce
pwsh build/plug-run.ps1 -IrInput probe.ir -Out out.txt `
    -PlugCdx codex/plugs/<name>/build-output/<name>-plug.cdx -Port <its port>
Select-String -Path out.txt -Pattern 'a/0'      # a hit IS the defect
```

The probe is `Pair = record { a : Integer }` with
`print-line-uni (integer-to-text (Pair { a = 7 }).a)`. Emitted today:
haskell `(a/0 Pair { a = 7 })`, ruby `Pair.new(7).a/0`, go
`map[...]{"a": 7}["a/0"]`, against python's correct `Pair(a=7).a`.
**The oracle is validated against the arm that is known-good:** python
was fixed in 13199 and the grep clears it, so the check discriminates
rather than merely firing. Each plug's port is the `-Port` literal in its
own `run.ps1`; passing the wrong one hangs the listener and exits 5,
which looks nothing like a pass.

Still wants an owner, and a fix still wants a reviewer or a runtime
per language. A blind sweep would be a compiling change, not a correct
one.

## 1.11 -- Nothing rebuilds the transpiler plug binaries, and it bit

`codex/plugs/*/build-output/*-plug.cdx` is NOT in the depot
(`build-output/` is in `.p4ignore`), so every workspace carries its own
copies and they are only as fresh as the last time somebody ran that
plug's `build.ps1` by hand.

**Measured 2026-08-11.** 44 plug binaries in this workspace dated
2026-08-06; the shared `codex/plugs/common/IRTextParser.codex` last
changed 2026-08-08 (changes 14182, 14206). Against IR containing a
record construction, **37 of 38 runnable plugs faulted** -- `!EXC=06`,
invalid opcode, inside `parse-type-record` -- and after
`codex/plugs/<name>/build.ps1` for each, **38 of 38 produced output**.
Only python survived stale. A rebuild is about 3 seconds per plug and
150 seconds for all 49.

**Read that result narrowly: 38 of 38 means they no longer CRASH, not
that they are correct.** Entry 1.2 above was measured on those same
rebuilt binaries and 36 of them emit the wrong thing while exiting 0.
That is the standing hazard at the top of this file, arriving exactly as
described.

Nothing in the gate closes this. `plug-binary` rebuilds only the six
binary backends (riscv, arm64, t3isa, elf, pe, img). `plug-smoke`
rebuilds a plug **only if its CDX is MISSING**, and then runs four of
them (typescript, python, rust, ptx) against a single input,
`codex/plugs/test-input/hello.codex`, which has no record in it -- so
the one path that faulted was the one path never exercised. **The gap is
the input as much as the staleness**: 38 of 38 stale plugs handle
`hello.codex` perfectly.

Wants: a staleness check or an unconditional rebuild in `plug-smoke`,
and a second smoke input carrying a record. Both are cheap; neither has
an owner.

## 1.7 -- Which plugs emit `list-push` as an unconditional in-place append?

Unswept. `list-push` is NOT unconditionally destructive on bare metal:
`__list_snoc` (`compiler/Emit/X86_64ListHelpers.codex`) stores in place
and returns the SAME pointer only when the backing array has spare
capacity, extends in place when the list is the topmost allocation, and
otherwise COPIES to a fresh allocation and returns a new pointer.
**An empty list literal has no spare capacity, so `list-push [] x`
returns a new list and leaves the original empty**, and the compiler
depends on that (`tco-ensure-temps`, `X86_64.codex:359`).

csharp emitted `_l.Add(v); return _l;` and silently reused spill slots
across tail calls in 17 functions of the 5,000 in the compiler. Fixed
there with a capacity-aware `_Buf.lpush` (in place when
`Count < Capacity`, else copy into twice the capacity, floor 4), which is
amortized O(1) with no measured slowdown.

**The failure is invisible in small programs: it needs a function with
two tail calls in one body before it shows.** The other 43 plugs are
unchecked. The semantics are published in `docs/DevelopersGuide.md`
under Lists.

## 1.8 -- Which plugs emit records into a mutable-capable target?

Still unswept, but no longer blocked: COMPILER-4 is closed and the wire
carries `(mutable)` as an optional trailing element on `rec-def` since
2026-08-08, so a plug CAN now read the answer off `ARecordTypeDef`.
Deriving it from the `field-store` nodes remains the stricter answer and
is what csharp does in `collect-mut-names`; the marker is the wider one.
**Consequence if a plug does neither: every store is a silent no-op and
the program still compiles.**

**Two measured instances, 2026-08-08, both from reading the emitters:**

- **kotlin is the silent no-op, and it is the bad direction.**
  `emit-kt-record-fields` emits `val` for EVERY field, and
  `IrFieldStore` is emitted as `rec.copy(field = value)` -- a functional
  copy whose result is discarded where a statement was wanted. It
  compiles and it throws the mutation away. Scala has the same shape
  (`case class` fields with no `var`).
- **swift is safe by accident.** `emit-sw-record-fields` emits `var` for
  every field regardless, so it is over-permissive and never refuses a
  store. It is wrong in the harmless direction.

Neither is fixed here. The sweep is the remaining work, and kotlin is
the one to do first.

## 1.3 -- RISC-V frameless TCO is a KNOWN-BAD PAIR, not a gap

The admission gate is `rv-is-frameless-tco`; the test that asks the
question that actually matters is `rv-body-is-frameless`, which NOTHING
CALLS (its only references are its own recursion, L-UNCALLED). The gate
in use reads a local count that cannot express the real budget, because
for callee-saved registers in a function emitting no prologue the budget
is zero rather than six.

**Do not simply wire the honest test in.** That was tried 2026-07-20 and
it makes the lane WORSE: a body it refuses falls to the framed path, and
the framed two-argument tail call is separately broken for the same
shapes (`v-shru`, a two-parameter loop over `bit-shru`, answers correctly
frameless and hangs framed). Both paths have to be right before the gate
can be turned on, so this is ONE item and not two.

Related and also open: `RiscVCodeGen.codex` 1880-1884 records that the
frameless literal-operand fix is NOT the general fix, and the general
temp-collision defect either side of a frameless binop is open. The full
account lives in `annotations/codex/plugs/riscv/RiscVCodeGen3.json`; a
live known-bad should not rest only in a sidecar, which is why it is
also here.

## 1.4 -- RED, pre-existing: the spirv text emitter drops a call

`codex/plugs/spirv/test-spirv.ps1` fails: `FAIL: missing
'OpFunctionCall %3'`, from a 9045-char `spirv-probe.spvasm`.

Established with a control rather than assumed: reverting every spirv
source to depot state, rebuilding the plug and re-running gives the
IDENTICAL failure and an identically sized artifact, and the plug binary
is 202,791 bytes either way. So the text path drops a call the probe
requires. `spirv-probe.codex` exercises a call deliberately, as one of
the four places the emitter used to produce something SPIR-V would
refuse.

`test-binary.ps1` (the word/validator path) PASSES with all four negative
controls firing, so **the defect is the disassembling text emitter, not
the binary encoder.** `test-emit.ps1` cannot run at all: it wants a plug
from `build-bin.ps1`, which does not exist in this directory.

**Nothing gates this.** `build/build.ps1` is green and does not run it,
which is why it can sit red.

## 1.1 -- Deferred: lift the plug type reconstruction into shared code

`a64-atype-to-codex-type` (`arm64/Arm64CodeGen2.codex:1926`) reconstructs
`IntegerTy lo hi mode` from `(a-bounded ...)` in the IR's `type-defs`
section, and it is the only by-name recovery of an elaborated field type
written anywhere. Step 3 of
`docs/Designs/Active/Compiler/IRTypeEmission.md` is to lift it into
shared plug code and point the group-3 sites at it with the emitter
UNCHANGED, so the rerouting is proven while the inline form still wins.

Group-3 sites: `clamp-field-val` (csharp), `a64-field-type-for-store`,
`rv-find-field-type-st`, `a64-collect-field-types`,
`rv-collect-field-types`, `rc-check-ctor-ref-sum`, and the python and
javascript clamp paths added in main 13199.

Deferred 2026-08-05 by Damian: it is a de-risking rehearsal, not a
prerequisite, and option A's own risks are measured closed.

## 1.9 -- The wasm plug has no clamp code at all

`afields-to-rfields` fills every `type-val` with `ErrorTy`. Pre-existing
gap, not a regression from any step.

## 1.10 -- ARM64 has no process/capability kernel (COMPILER-1 has no ARM64 counterpart)

The ARM64 target (`codex/plugs/arm64/`) boots a bare runtime with no
kernel: no process table, no capability cell, no scope cells, no
boot-time population, and no syscall/servicer path. The whole
process/cap/scope builtin layer is a hardcoded stub in
`Arm64CodeGen2.codex:1500-1508`: `process-get-pid`=0, `process-get-cap`=0,
`process-get-scope`/`-network-scope`="" (`a64-emit-empty-text`),
`process-set-scope`/`-network-scope`=-1, `process-restrict-cap`=-1. FS and
net effects are hard-refused before any check: `read-file` /
`read-file-raw` / `uefi-read-file` emit "arm64 has no filesystem"
(`a64-emit-unsupported-read`, `:1473-1480`), `net-send-raw`/`net-recv-raw`
return 0 (`:1507-1508`).

**Consequence.** The x86-64 runtime scope and capability enforcement
shipped and tested under COMPILER-1 (`codex/compiler/compiler-backlog.md`)
has NO ARM64 counterpart, so a scoped or capability-restricted program is
silently unenforced on ARM64. The arch-independent library predicates
(`fat16-scope-admits`, `net-scope-admits`) are never reached there, and
would read "" if they were.

**Parity is the whole chain, not a getter fix:** a process table with
cap/scope cells (mirror `X86_64Boot.codex:421,432,433`), boot population
(mirror `emit-set-boot-scope`/`emit-grant-cap-mask` into
`Arm64Runtime.codex` `__start`, ~`:1957`), real load/store codegen for the
seven stubs plus an `emit-check-capability` analog, and an SVC/servicer
path so effect ops reach the library gates. A QEMU/Renode cross bed DOES
exist now (`build/test-cross-batch.ps1 -Arch arm64 -UseQemu` boots each
test and asserts UART against `.expected`; `build/boot-arm64.ps1` for one
image); the runtime scope/cap tests are `.no-cross`-excluded today with
the reason "the cross lane boots a bare runtime with no kernel", and those
exclusions lift once the machinery lands. Major effort and its own
initiative, not a quick parity fix. Recorded 2026-08-11 (val); the bed
availability is what makes it newly actionable.

## 1.12 -- T3ISA: three follow-ons named and not taken (closed, not open)

The t3isa plug is FINISHED and its design is folded to
`docs/Designs/Done/Compiler/T3IsaPlug.md`. This entry exists so the three
follow-ons that design names are reachable from the register that owns the
quire, not because any of them is work waiting for an owner. **Damian's
ruling 2026-08-11: closed, revisit when the specification next moves.** Do
not pick one up as filler.

1. **v3: what a `List` COSTS** on a bump allocator with no reclamation and
   16 kilowords of heap. The question is the cost, not the feasibility.
2. **`show` outside a print.** `fmt::show_int` (syscall 14) is measured
   working since spec v1.3; the blocker is ours, the emitter having no Text
   value to carry the handle. Text append stays impossible (`fmt::concat` is
   not implemented on T3).
3. **The heap syscall trade.** `heap_alloc_words` (218) against our
   hand-rolled allocator: one syscall versus eleven instructions, bought
   with a dependence on a region that has moved once already.

**Before believing a green t3isa gate later, re-measure the target.** Four of
the seven load-bearing target facts moved between spec v1.0 and v1.3, and one
(`TSHR` rounding) would have been silent and wrong. The oracles are on this
machine only (`D:\Toolchain-Ternary`), so `codex/plugs/t3isa/gate.ps1` cannot
run anywhere else and nothing in `build/build.ps1` reaches it.

## 1.13 -- The zig plug's central claim cannot be checked on this machine

`build/plug-oracle-test.ps1` wires python, javascript and csharp. Zig is not
wired and there is no zig toolchain on this box, so the arm that would decide
Steve Howell's PR 64 does not exist here.

**What that leaves unverified is not a detail.** His claim is that the emitted
zig COMPILES AND RUNS byte-identical to bare metal on `Syntax/Lexer.codex`
(61 tokens) and on the whole of `Syntax/Parser.codex` (2,059 lines, 18,812
tokens, 202 defs). That is the strongest evidence any transpiler plug in this
quire has ever had, and it is his measurement on his machine, not ours. What
was verified here is that the plug builds, emits for a real subject, and gets
the CCE text model right by hand-decode.

The fix is a zig toolchain plus a wiring entry, which is a decision about what
this box carries, not a code change. Until then a zig regression is invisible
to every gate.

## 1.14 -- Codex assumes deep recursion is free; a stack language does not

Raised by PR 64 and worth stating once for the whole quire, because every plug
targeting a conventional runtime meets it. `Parser.codex` at 18,812 tokens
overflowed zig's 8 MB main-thread stack, and ReleaseFast does not rescue it --
the calls sit inside labelled block expressions and LLVM does not turn them
into loops.

**The obvious answer is wrong and CSharpPlug already records why.** Emitting a
loop for self-tail-calls does not close it: the case that reaches the limit is
MUTUAL recursion (the lexer's `scan-token` -> `skip-prose-line` -> `scan-token`
cycle), which no self-TCO pass can flatten. Both plugs now run the entry point
on a thread with a big stack, 512 MB, the same constant, so they agree.

It is a property of how codex source is written rather than of large input:
bare metal answers deep recursion with a multi-gigabyte arena and .NET gives
its main thread 1 MB. Anything targeting a language with a conventional stack
needs the big-stack entry point, and 40-odd plugs do not have one.
