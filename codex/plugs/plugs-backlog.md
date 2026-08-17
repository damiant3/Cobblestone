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

**`codex/plugs/zig/` is Steve Howell's and is not this lane's to change**
(Damian, 2026-08-16). Where zig appears below it is because a sweep
measured it, and for no other reason.

## 1.16 -- three plugs left, and they are the three that are not the shape

**THE SWEEP IS DONE (val, 2026-08-16): 36 chapters converted, all 36
build.** What is left is `csharp`, `recheck` and `rust`, and they are left
because the converter REFUSED them rather than because they were skipped:
`csharp` and `rust` do not have the canonical send line, `recheck` and
`rust` emit no `OK` line at all, and `rust` has two send sites. Each needs
reading rather than a pattern.

The conversion was scripted and every file was checked before and after.
The guards, because a scripted multi-file edit that does not assert the
patch applied scores the previous build: exactly one send line, exactly
one `OK` line, the send variable used exactly twice (its bind and its
close), the close on the very next line, no existing use of the name
`sent`, and after the write, exactly one checked send, one rethreaded
close and one `TRUNCATED` branch. Three files failed a guard and were
left alone; the 36 that passed all built, and `javascript`, the only
converted plug with a wired oracle arm, still passes 33 of 33.

**The sweep found the `go` plug does not build on main, and it is not the
sweep's doing.** `GoEmitter.codex` carries a `list-snoc` `GoBuiltinEmitter`
whose BODY LINE IS MISSING -- the entry is immediately followed by the
`list-push` entry -- so the bundle fails with `CDX1024: Expected field name
in record literal`. It landed at main 15882, "plugs 1.21 batch 1": the
`list-push` registration was inserted and consumed the line under it.
Nothing in any gate builds a plug, so it sat. Fixed here by restoring the
body, which is the same `append(...)` `list-push` emits. **The other
fifteen 1.21 plugs were checked by building them, and only `go` was
damaged.**

The os/net half is closed (blu, 2026-08-16): `tcp-step-close-wait`
answering `EvSend` with `ActError "peer closed"` was the cap, `NetIO` now
carries `NetSendResult` with `net-io-send-checked` / `-send-raw-checked` /
`-send-text-checked`, and the account is `ExaminersAssay.md` "A Send That
Cannot Finish Says So". The checked entry points are ADDITIVE, so nothing
changed under any plug.

The half this entry was raised for was the shape `ts2 <- net-io-send-text
(recv.transport) output` followed by `print-line-uni ("OK ...")`, which
reports success for a send that refused. With the CLOSE_WAIT fix in, none
of them was losing bytes, so it was about a plug never printing `OK` after
a refusal rather than about recovering lost output.

`build/plug-run.ps1` fails with exit 7 when a plug reports `TRUNCATED
sent=`, so the 38 plugs on the shared harness get the check for free as
they are converted. The 17 with their own listener each need the same
three lines; `wpf` writes its guest console to `build-output/plug-wpf.out`.

**"None of them is losing bytes today" was false, and the sweep is
therefore about lost output as well as a wrong `OK` (reek, 2026-08-16).**
The shape above is not the only one: `pe` did not call `net-io-send-text`
at all. It carried a PRIVATE send loop, `send-buf-loop`, streaming a heap
buffer through `net-send` directly, taking `r.session` whether or not the
send was refused and never reading a frame, so no ACK pruned the rexmit
queue. Every ARM64 PE over `net-rexmit-capacity * net-mss` = 11,200 bytes
was cut to exactly 11,200 with a clean close, and the ARM64 boot image has
been unbootable for it: AAVMF refused the PE `Unsupported` and dropped to
the UEFI shell. Converted to `net-io-send-raw-checked`, which also removed
a whole-PE heap copy the buffer stream needed; the same payload now
delivers 77,824 bytes with the section extents ending exactly there, and
QEMU boots into Codex on AArch64.

**So the census is by CONCEPT, not by the `net-io-send-text` shape.**
Grep every plug for `net-send` and for a private send or chunk loop, not
only for the documented call. A plug with its own loop is the case that
loses bytes silently.

### THE CENSUS, val 2026-08-16. The private-loop class is THREE plugs and `pe` was one

Every `.codex` under `codex/plugs`, counting `net-send`, `net-io-send-text`
and the checked entry points:

- **A private loop calling `net-send` directly: `pe` (reek, fixed), `elf`
  (`ElfPlug.codex:27`), `img` (`ImgPlug.codex:37`).** These three are the
  whole class. All three had the identical body -- `let r = net-send
  (ts.session) chunk`, take `r.session`, advance the offset regardless --
  and all three chunk at 1400 with a 10,000,000-iteration `spin-wait`
  between chunks as the only backpressure.
- **`arm64`, `riscv` and `recheck` match a `net-send` grep and are NOT in
  the class.** Their hits are the string `"net-send-raw"` as an EMITTED
  builtin name in codegen, not a call. Read the hit before counting it.
- **Converted: `pe`, `elf`, `img`, `python`, `wpf`.** The private-loop
  class is now empty. Everything else still uses `net-io-send-text`
  unchecked, which is the original 43-chapter half and all that is left of
  this entry.

**`elf` and `img` are converted (val, 2026-08-16).** `elf` sends a byte
list, so its private loop is deleted outright for
`net-io-send-raw-checked`. `img` sends from a BUFFER and keeps a chunked
loop, because reading a whole disk image into one list would pay the
`__buf-read-bytes` blowup over every sector at once; the loop calls
`net-io-send-raw-checked` per chunk and STOPS on a refusal instead of
walking past it. Both report `sent=` and `TRUNCATED`, the shape `pe` uses
and `build/plug-run.ps1` already fails on. **`img` is exercised end to end
and delivers a full 16,777,216-byte image on both filesystem paths**; the
`elf` conversion is read against `pe`'s and not run, because building its
wire input needs a compiler x86 extract.

**A trap that class cost a full session, and it is the reason to read the
next paragraph before writing any send loop.** `img` carried
`__heap-save` / `__heap-restore` around each chunk, restoring immediately
after the send returned. **A send RETAINS its chunk in the retransmit
queue until an ACK prunes it, so the restore handed the network stack a
pointer into rewound heap that the next chunk then overwrote.** The plug
died at `!EXC=0e` before delivering anything, and it died INSIDE
`codex/os/net` -- `__list_concat_many` under `tcp-with-checksum` on the
send path with the private loop, and `net-rexmit-prune` under
`net-receive-segment` on the ACK path once the checked channel started
draining. Two different faults, one cause, and neither of them an os/net
defect: os/net was walking memory the plug had freed under it (L-MYSIDE).
Deleting the two heap calls is the whole fix. Swept: `img` was the only
plug in the tree with a `__heap-restore` anywhere near a send.

## 1.20 -- the pascal plug still needs HOISTING, and four other gaps found beside it

The BODY position is fixed (val, 2026-08-16), statements and bindings both.
`emit-pas-def` routes a definition's body through `emit-pas-body`, one
Pascal statement per act statement; `IrLet` in body position emits
`name := val;` and recurses; an act `IrDoBind` assigns its own name rather
than `Result`, with `Result` set from the last bind if the block ends on
one; and `pas-var-block` declares every name the body's let-chain and
act-statement list bind, seeded with the parameter names so a rebound
PARAMETER is assigned rather than illegally redeclared.

Measured on a probe covering the four shapes a `var` block has to cover (a
let-chain, a let shadowing a parameter, a name bound more than once, and an
act that binds then reads). x86-64 answers 18, 16, 19; the emitted Pascal
now declares `var a, b: Variant;` for the chain, declares nothing for the
shadow and assigns the parameter, declares `a, c, a2` once each, and hand
traces to 18, 16, 19. Against `plug-oracle-arith.codex`, undeclared reads
go 10 to 7 and assignment targets 12 to 19, with every one of the 19
declared; the three closed are the act binding `c` in `store-one`,
`store-two` and `store-untouched`.

**A `list-push` in the plug's OWN source cost a build here and is worth the
next reader's minute.** `pas-var-block` took `list-length seed` after
pushing onto `seed`, and `list-push` extends in place when the backing
array has capacity, so the length it read was the GROWN one and the guard
could never fire: the var block came out empty while every assignment
emitted correctly. That is the pattern
`docs/Designs/Done/Language/SAFE-MUTATION.md` marks unsafe. Bind the length
to an Integer BEFORE the collection runs.

**Still open, and it is the hoisting gap.** `emit-pas-act-expr` is
last-statement-only and `IrLet` in EXPRESSION position still emits only its
body, because Pascal has nowhere to put a statement inside an expression.
Closing either needs the plug to hoist into the enclosing block, which it
has no machinery for; they are one piece of work, not two.

**Four more undeclared reads the same measurement turned up, none of them
the binding gap.** In `plug-oracle-arith.codex` pascal emits
`gauge:g` (a bounded-field read as a bare name), `list_push` and
`list_snoc` (**pascal has no emitter for either name**, so it emits a call
to a function it never defines -- pascal is one of the eight plugs 1.7
names with no `list-snoc` of any kind), and `store-one:ca`,
`store-two:cb`, `store-untouched:cb` (record field access and store;
`IrFieldStore` emits the literal `"0"` at `PascalEmitter.codex:162`).

`fortran` has the identical last-statement-only shape in
`fort-emit-act-expr` and is NOT known to be affected: its top-level path
emits every statement, so what its expression form costs is a nested act
used as an expression, and that is unmeasured rather than clean.

**There is no Free Pascal toolchain on this box** (measured 2026-08-16:
`fpc`, `ppcx64`, `lazbuild` all absent), so anything here is reviewed by
reading against the language. Two traps the body fix had to get right and
the next reader will meet again: `WriteLn` and `Halt` are PROCEDURES, so
`Result := WriteLn(...)` does not compile, and the entry wrapper must emit
`opening;` rather than `WriteLn(opening);` or it prints an Unassigned
Variant after the real output.

## 1.7 -- three list-emission gaps left after the sweep

Swept 2026-08-16 (reek) across all 57 directories under `codex/plugs`.
`list-snoc` is not unconditionally destructive on bare metal: `__list_snoc`
(`compiler/Emit/X86_64ListHelpers.codex`) stores in place only when the
backing array has spare capacity, and otherwise COPIES. **An empty list
literal has no spare capacity, so `list-push [] x` returns a new list and
leaves the original empty**, and the compiler depends on that
(`tco-ensure-temps`, `X86_64.codex:359`). **The standard is that an
UNCONDITIONAL in-place append is the defect**, and the fix is the
capacity-aware helper: in place under capacity, otherwise copy.
`csharp`'s `_Buf.lpush` is the model; `lua` and `python` now emit
`_cx_lpush` against it.

What remains open:

- **`fortran` emits `fort_list_snoc(a, v)` and never defines it.** Two
  mentions in the whole plug, the names list at `FortranEmitter.codex:339`
  and the call at `:385`, and no prelude emits a body. The generated
  Fortran cannot compile.
- **`java`, `typescript`, `d`, `julia`, `perl`, `scheme`, `groovy` and
  `clojure` are real language plugs with no `list-snoc` of any kind.**
  Unmeasured; a third failure mode beyond destructive and correct.
- `zig` still appends unconditionally (`cx_ll_push`) and is Steve's.

**Two sweep traps, because the next census here will hit both.** The
builtin is named `list-snoc`, not `list-push`, and a sweep for
`list-push` finds four plugs and misses the twelve that matter. And there
are two dispatch shapes: most plugs declare `BuiltinEmitter { name =
"list-snoc", ... }`, javascript uses an inline `else if n == "list-snoc"`,
and seven plugs (`ada`, `elixir`, `fortran`, `nim`, `objc` among them)
keep an ordered `*-builtin-names` list and dispatch on the INDEX, with the
name nowhere near the emission.

**Do not add an aliasing-observing row to the plug oracle.** Reading a
list after pushing to it is the UNSAFE pattern
`docs/Designs/Done/Language/SAFE-MUTATION.md` names explicitly; its answer
is unspecified by design, so such a row would grade every plug against
unspecified behaviour. An earlier revision of this entry inverted every
verdict on the strength of exactly that probe and was rescinded the same
day.

## 1.21 -- a plug's builtin CATCH-ALL emits a list append for any name it does not know

**The sixteen-plug `list-push` gap this entry was raised for is CLOSED**
(re-measured val, 2026-08-16, and the entry is kept only for the residue
below). A census of `"list-snoc"` and `"list-push"` string registrations
across all 57 directories now finds **zero** plugs carrying the first
without the second: 21 register both, three register `list-push` only
(`maui`, `wasm`, `zig`), and none is missing it. Re-run that census rather
than trusting this paragraph (L-COUNT).

Verified rather than counted, on the entry's own demonstration -- a
chapter whose only unusual content is `build (list-push acc n) (n - 1)`,
beside the same loop written with `list-snoc`. x86-64 answers 4 and 4;
through the built python plug both call sites emit `_cx_lpush(acc, n)` and
it answers 4 and 4. The entry's recorded failure was `_tco_0 =
list_push(acc)(n)` and `NameError: name 'list_push' is not defined`.

**What is left is the mechanism, and it is a real hazard.** In the five
plugs that dispatch a builtin by INDEX (`ada`, `elixir`, `fortran`, `nim`,
`objc`), `list-push` was closed by appending the name to the end of
`*-builtin-names` -- so it takes a new index, there is no arm for that
index, and it reaches the trailing `else`. It emits the right thing only
because **in all five, the catch-all is byte-identical to the `list-snoc`
arm**: `a & (b)` in ada, `a ++ [b]` in elixir, `a & @[b]` in nim,
`arrayByAddingObject:` in objc, `fort_list_snoc(a, b)` in fortran.

So the fix works and the plug is also, in all five, **emitting a list
append for every builtin name it does not recognise.** A name the plug has
never heard of does not refuse and does not fall through to a function
call: it silently becomes a two-argument list concatenation, and if it had
one argument the emitter reads `list-at args 1` off the end. That is the
standing hazard at the top of this register in its purest form. The repair
is an explicit arm per registered name and a catch-all that REFUSES, in
the shape the other plugs already use (`!UNSUPPORTED: call to an unknown
function`, which is what `t3isa` emits).

Not measured: whether any unregistered builtin actually reaches this path
today in a real chapter. Reading the emission is what found it.

## 1.8 -- a field store is not observable through `haskell`, `elixir` or `clojure`

Swept 2026-08-16 (reek), with the arm that makes it visible:
`codex/test/plug-oracle-arith.codex` carries a `mutable Cell` and three
rows (`store-one`, `store-two`, `store-untouched`, bare metal 55 / 56 / 7).
`python`, `javascript`, `kotlin`, `ocaml` and `scala` are fixed and thread
the `ARecordTypeDef` mutability flag through to the decoration.

**The divergence: `haskell`, `elixir` and `clojure` have no mutable record
at all.** Record update, `Map.put` and `assoc` all CONSTRUCT rather than
assign, and the result is discarded in statement position. A Codex program
that assigns `c.f = v` and then reads `c.f` reads the value from BEFORE
the store. **This is not a one-line fix and it is not a bug in the plug:**
closing it means rewriting the store into a rebinding and threading the
new record through the rest of the expression, an IR-to-source
transformation rather than an emitter flag. Nobody should attempt it as
part of this item without deciding that first.

**Census of the rest, READ FROM SOURCE and not executed** -- the same
silent no-op, by two different causes. Discards a functional copy: `ada`
(`'Update`), `groovy` (map `+`), `php` (mutates the CLONE). Mutates a copy
because the language passes records by value: `swift`, `nim`, `rust`,
`d`, `objc` (`mutableCopy`). Correct as written: `lua`, `perl`, `go`,
`java`, `ruby`, `typescript`, `julia`. Emits a LITERAL for a store and
drops it entirely: `fortran` (`"0"`), `scheme` (`"'()"`).

**Two lessons the fixed five paid for.** A plug that emits records
immutably and a plug that discards the store are the same bug wearing two
hats, and fixing either alone leaves it broken: making javascript's store
a real assignment turned a silent wrong answer into `TypeError: Cannot
assign to read only property`, because the constructor also wrapped the
record in `Object.freeze`. And `Gauge`, the NON-mutable record in the same
subject, is the discriminator that keeps a blanket `var` from passing
every row unnoticed.

**Adjacent and not fixed: the `ocaml` plug emits `type Gauge` and `type
Cell` with leading capitals**, which OCaml reserves for constructors and
modules, so the emitted type names are not legal OCaml regardless of
mutability. It belongs to whoever takes `ocaml` next.

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

## 1.26 -- PR 66 carried in; rung 13 does not reproduce on this box

**Steve Howell's `ZigEmitter.codex` from `showell/NewRepository`
`zig-plug-arith` (46 commits, head `ea885864`) is TAKEN WHOLESALE** (val,
2026-08-16), CRLF-normalised on the way in because his tree is LF and the
depot's is CRLF. His file is 224 definitions against the depot's 207: he
carries 27 the depot lacked and his own implementations of the 10 it had,
including all four 1.13 fixes and the third store site. The PR head is on
**his fork, not `damiant3/NewRepository`** -- that ref 404s.

**Carried: `ZigEmitter.codex` only.** `ZigPlug.codex` is content-identical
to the depot's (line endings only), and **his `run.ps1` LACKS `-Passes
'text-plug'`, so the depot's is ahead and stays** -- his emitter was
developed against the default pipeline and is graded here against the
text-plug IR the depot serves. `zig-ladder/` stays on his branch, as PR 65
set the precedent.

**MERGED ON TOP, the one thing he lacks: a `list-snoc` registration.** His
emitter registers `list-push` and not `list-snoc`, so the oracle subject's
`snoc-len` emitted `@compileError("zig plug: no emitter for list-snoc")`
and the program did not compile. One line, pointing at the same
`cx_ll_push` his `list-push` uses. **That is what took zig's arm from red
to green**, and the arm had been red on main since 1.7 added the row.

**Verified here.** `build/plug-oracle-test.ps1` is **5 passed, 0 failed --
python, javascript, zig, wasm and csharp each 33 of 33 -- which is the
first time every wired arm has been green.** The 1.13 checks pass through
his file: Euclidean `int-mod`, record-literal parens and the list-literal
element type are oracle rows, and the third store site, which no wired arm
covers, was run separately (`m.g = n` on a clamping `mutable` field
answers 100, -100, 42, matching x86-64).

**RUNG 13 DOES NOT REPRODUCE ON THIS BOX, and the reason is not his
emitter.** His claim is the whole compiler through the plug, 16,874 lines
of zig, diffing empty against bare metal. Reproduced as far as: his
`bundle_whole.ps1` builds the subject here (54,856 lines, 2,575,126
bytes); it compiles at his `-Decks 172` and runs on bare metal, 2,911
lines of output; IR emission is 13,488,840 bytes in 118 s. **The plug then
dies.** The guest raises `OUT OF MEMORY` part way through emitting, at
`SP=0xbdfffd08 HEAP=0xb9e00238`, and the emission stops between 534,800
and 547,400 bytes -- **five identical runs, five different lengths, every
one a multiple of `net-mss` 1400**. It is not the guest's `-mem`: 3 GB and
12 GB both stop in the same band. So his 16,874 lines is HIS measurement
on HIS harness (`zig-ladder/codex_vm.py`), and the process form is
Linux-only and is his measurement too.

**And `build/plug-run.ps1` reported `OK` on every one of those dead
guests.** It greps the VM's stderr for `TRUNCATED sent=`, but the guest
console is not on stderr -- capturing it needs `-output`, which the
harness does not pass -- so a guest that dies mid-emission is
indistinguishable from one that finished, and a truncated `.zig` is
written under an OK line. **38 plugs use that harness**; nothing noticed
because every wired subject is a few KB. The repair is the same shape
`ExaminersAssay.md` records for the self-check tier: pass `-output`, scan
it for `OUT OF MEMORY` and `!EXC=`, and fail. `plug-run.ps1` is GENERATED,
so it is a change to `codex/build/plugrunScript.codex` plus a regeneration,
submitted together -- not a hand edit.

Converting `ZigPlug.codex` to the checked send channel (1.16's sweep
skipped it as Steve's) is in this CL and does NOT close the gap: the
checked send never reports `TRUNCATED`, because the guest dies rather than
being refused.

**Draft PR reply, three lines, for Damian:**

> Carried your ZigEmitter wholesale onto main; it takes the zig oracle arm
> from red to green and every wired arm is now 33 of 33, the first time
> that has been true. The one thing added on top was a `list-snoc`
> registration beside your `list-push` -- without it the shared oracle
> subject does not compile.
> Rung 13 we could not reproduce on Windows: the plug guest raises OUT OF
> MEMORY part way through the whole-compiler emission, stopping between
> 534,800 and 547,400 bytes across five identical runs, and it is not the
> guest's memory size. Your 16,874-line result stands as your measurement
> on your harness; the process form is Linux-only and is recorded the same
> way.
> Your run.ps1 is the one file we did not take: main's passes
> `-Passes 'text-plug'`, which a source plug needs, and yours does not.
## 1.17 -- ARM64 has no SVC servicer path (Stage 4 of docs/Designs/Done/Compiler/Arm64ProcessKernel.md)

x86-64 routes the block and identity families through `syscall` numbers
10-18 (`X86_64Helpers.codex:3322-3324, 3848, 4415-4489`; dispatch
`X86_64Boot.codex:2773-3073`) so those helpers cannot be entered without
passing the servicer's capability check. ARM64 has the vector table and
all sixteen slots patched to `a64-rt-fault-handler`
(`Arm64Runtime.codex`, `a64-rt-patch-vectors`); nothing emits an `SVC`. The
stage is: carve the synchronous-EL1 slot out of the patch loop, an `SVC #n`
from each serviced helper, dispatch on `n`, `a64-dis-svc` confirming the
instruction at each site, and a direct-call bypass arm that must be
refused. **Blocked on `CrossLaneFilesystem.md` steps 2-5** (fester, block
builtins on ARM64): until a block or identity path exists there is nothing
for the servicer to serve. Filed 2026-08-16 by red's ruling on
Arm64ProcessKernel.md question 3.

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
its main thread 1 MB.

**Campaign plan: `docs/Designs/Active/Compiler/PlugDeepRecursion.md`** (val,
2026-08-16), which is the handoff if this changes hands. It carries the
inventory of all 54 entry points, the measurements, and the order.

**EVERY PLUG THIS BOX CAN EXECUTE PASSES, AND THE ARM IS WIRED.** The probe
is a `Deep recursion` section in `codex/test/plug-oracle-arith.codex`; the
truth set goes 28 values to 33. `csharp` and `zig` already carried a 512 MB
thread; `python` needed one line (`sys.setrecursionlimit`, NOT the thread);
`javascript` needed `worker_threads` with `resourceLimits.stackSizeMb`;
`wasm` needed nothing in the plug, because the module is correct and the
HOST's stack is the constraint (`wasmtime run -W
max-wasm-stack=268435456`, which the oracle arm now passes).

**What is left is the 42 plugs whose runtime is not on this box**, one
entry-point wrapper each, readable but not runnable here.

**`zig`'s oracle arm is RED on main and it is NOT this item.** The subject
gained `snoc-len` when 1.7 landed and the zig plug has no emitter for
`list-snoc`, so the emitted program does not compile: `@compileError("zig
plug: no emitter for list-snoc")` plus an unused parameter. Measured
against the DEPOT subject, which fails identically, so it predates the
recursion rows. It is the same gap wasm had (1.23) and it is Steve
Howell's to close. **Until it is, `build/plug-oracle-test.ps1` exits 1 even
though four of five arms pass 33 of 33**, and no gate runs it.

**Measured 2026-08-16, the first time anything asked a plug this question**
-- `codex/test/plug-oracle-arith.codex` contains no recursion at all. Two
shapes, self and mutual, at 1,000 and 100,000, plus a non-tail row:

| arm | self 100k | mutual 1k | mutual 100k | non-tail 100k |
|---|---|---|---|---|
| x86-64 | ok | ok | ok | 5000050000 |
| `csharp` | ok | ok | ok | 5000050000 |
| `javascript` | ok | ok | **RangeError** | -- |
| `wasm` | ok | ok | **call stack exhausted** | -- |
| `python` | ok | **RecursionError** | -- | -- |

Self recursion is green everywhere because the plugs already emit a loop
for it (python emits `while True:` with a reassignment). Every failure is
the mutual pair, which is the entry's claim, now measured rather than
argued. `csharp` passing every row is what makes the others mean something.

**The entry says "the big-stack entry point" and that is the wrong fix for
python**, which four ablations settle: a raised `sys.setrecursionlimit` on
the MAIN thread passes every row, a 512 MB thread with the default counter
still dies at 1,000, and `threading.stack_size(512MB)` is refused outright
on this box. CPython 3.11 stopped consuming C stack for Python-to-Python
calls, so the limit is a counter and the fix is one line. Establish each
plug's class by ablation, not by the language's reputation.

## 1.26 -- RISC-V `peek-32` sign-extends where x86-64 and ARM64 zero-extend

`rv-rt-peek-32` (`codex/plugs/riscv/RiscVRuntime.codex`) is `lw`, which sign-extends
bit 31 into a 64-bit Integer; the x86-64 helper is `mov eax, [rdi]` (zero-extends,
the `DevelopersGuide` pitfall says so) and ARM64 `ldr w0` zero-extends. Measured
2026-08-16 (root) while landing `codex/test/poke16-width`: `peek-32` of the bytes
`44 33 CD AB` answered 2882351940 on x86-64 and ARM64 and -1412615356 on RISC-V;
the test now avoids bit 31 so it can run on all three lanes. Fix is `lwu` in
`rv-rt-peek-32` (and a check of `read-mmio-32`, which is the same block); arm: a
`peek-32` of a word with bit 31 set, recorded on x86-64, green on riscv.
