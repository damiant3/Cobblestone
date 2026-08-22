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

**babbage is SHELVED** (Damian, 2026-08-21): vanity work. Its open items
moved to `codex/plugs/babbage/babbage-backlog.md`. Do not add babbage items
here.
