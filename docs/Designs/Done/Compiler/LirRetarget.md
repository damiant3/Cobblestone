# Retargeting the LIR to the ARM64 and RISC-V Plugs

**Status: DROPPED 2026-07-23 by Damian, and this file said "Active" for four
days afterwards.** *"the amount of time we've spent on these arm64 and riscv
codegen plugs has been rather huge, and never well scoped or surveyed."* What
landed stays; nothing further is planned, and step 5 (RISC-V) will not be built.
Moved to `Done/` 2026-07-27 (blu) as part of the stale-claim sweep. The live
document is `docs/Designs/Active/Compiler/LIR.md`, whose section 12 is the
closing note for the whole codegen-quality campaign.

The record below is kept because the ARM64 work it describes *shipped* and the
measurements in it are real. Read it as history, not as a plan.

**Status when it was current:** Active -- steps 0 through 3 done, **step 4 complete** (4a the
descriptor, 4b zero-instruction functions, 4c straight-line arithmetic, 4d the
frame and the entry moves, 4e comparisons and branches, 4f the call, 4g the
join via `LiMove`/`LiJump`, 4h the flag-setting `tst`, 4i the
constructor-pattern load). Every construct the lowering emits is now
whitelisted on ARM64. Step 5 (RISC-V) not started. **Filed:** 2026-07-18 (reek).
**Status:** the general allocator (`lir-alloc-linscan`) is in place;
subsumes the allocator half of 3.5. Supersedes nothing: `LIR.md` remains the specification of
the LIR itself, and this doc is the plan for making its register file really data.
**Prerequisite reading:** `docs/Designs/Active/Compiler/LIR.md` (the LIR and the
allocator), `MiddleEnd.md` step 5.

---

## 1. The claim this campaign has to make true

`LIR.md` section 3.1 says the register file is data, so "a new target is a table entry,
not a port". section 11 is honest that this is intent: *"The register file is
x86-64-shaped first. The claim that ARM64 is 'a table entry' is a design intent,
not a proven fact until the retarget is done."*

**As of 2026-07-19 (CLs 9234, 9246) it is true of the allocator and the two
verifiers, and not yet true of anything else.** Steps 1 and 2 below are done;
what the measurement underneath describes is the state before them, kept
because the shape of the gap is what the campaign is organised around.

The claim now rests on evidence rather than intent: the five constants are
gone, a `LirTarget` is threaded through the allocator and the allocation
verifier, and the descriptor has been run against two deliberately non-x86
shapes over the whole `lir-check` corpus with both verifiers clean. See step 2
for what that does and does not cover -- it does not cover the selector.

The state before step 1, measured 2026-07-18:

- **The LIR data types are genuinely neutral.** `LirInsn`, `LirBlock`,
  `LirFunc`, `Location` (`LocReg`/`LocSlot`) and `AllocRes` carry no x86.
  `lir-alloc-linscan : LirFunc -> AllocRes` operates on abstract indices only.
- **The physical mapping is already out of the way.** `lir-phys`,
  `lir-scratch-reg` and `lir-scratch2` live in the *selector*
  (`Emit/X86_64Lir.codex:76, 111, 113`), not in `IR/Lir.codex`.
- **The pool structure is five module-level constants, not parameters.**
  `lir-alloc-ncallee = 4`, `lir-alloc-nargregs = 5`, `lir-alloc-nregs = 9`,
  `lir-alloc-diva = 4`, `lir-alloc-divb = 7` (`IR/Lir.codex:1699-1725`),
  referenced by name at **35 sites** inside the allocator and its helpers.

So the table exists in prose and in the physical mapping, and does not exist in
the allocator. Step 1 is what closes that.

## 2. The finding that reshapes the campaign

**The plugs cannot reach the LIR today, and it is not only a cite problem.**

- The native plugs are **VM-hosted and serial-driven**, not TCP. `opening`
  (`codex/plugs/arm64/Arm64Plug.codex:90`) reads a mode line, `read-file`s the
  IR text, and parses it with `parse-ir-chapter`
  (`codex/plugs/common/IRTextParser.codex:717`) into **tree IR** --
  `IRChapter`/`IRDef`/`IRExpr` from `codex/plugs/common/PlugTypes.codex`. Codegen
  runs straight off that tree (`a64-emit-module`, `rv-emit-module`).
- **No plug in the tree cites `Codex`.** They cite Foreword, Encode, and the
  pseudo-quire `Plug`. `Lir.codex` is `Chapter: Lir` citing `Codex chapter Build
  Settings`, and `build/quire-map.ps1` deliberately has **no `Codex` entry** --
  the compiler is bundled by header-renaming, so `cites Codex chapter X` is
  satisfied by presence in the unit, never by the registry. A plug citing
  `Codex chapter Lir` fails at `Resolve-PlugCite` today.
- **The LIR lowering runs post-RESOLVE**, because constructor-pattern lowering
  needs `SumTy` (`IR/Lir.codex:17-26`). The plug's IR arrives as text.

That last point is the one that decides the route, and it is **the open question
step 3 exists to answer**: does the plug's `ParsedIR` (which does carry
`type-defs`) carry enough to run `lower-lir-expr`, and is `PlugTypes`' `IRExpr`
the same shape as the compiler's? Nobody has checked. Do not plan past it.

## 3. Steps

### Step 0 -- restore ARM64 measurability. DONE 2026-07-19.

**ARM64 produces no UART output on Renode at all** -- the whole lane. `hvc #0` is
emitted unconditionally in `__start` (`Arm64Runtime.codex`) for the PSCI
`CPU_ON` that closed 4.2; the committed Renode board has no PSCI provider, so it
traps to a `WFI + B .self` vector entry and hangs before touching the PL011.

The retired entry said to fold this into the LIR buildout. **Inverting that was right: it was step
0.** No ARM64 codegen change can be evaluated on a lane that emits nothing, so
every later step here is unmeasurable until it is fixed. The fix must keep 4.2:
gate the `CPU_ON` on a guest cell that reads zero unless a harness sets it.

Also decide here whether an ARM64 fault should be visible at all -- all 16 vector
entries are the same do-nothing stub, so the backend has no fault reporting.
Debugging a retarget without it is a bad trade.

### Step 1 -- make the register file data (x86 only, zero behaviour change) -- DONE, CL 9234

Thread a target descriptor -- count, the callee/arg splits, the division-fixed
pair -- through `lir-alloc-linscan` and its helpers, replacing the five constants
at their 35 sites. Pass x86-64's current values.

**This is a pure refactor and must prove it:** `gen2 === gen3` byte-identical,
the 9 benches instruction-identical *and* byte-identical, both verifiers clean,
`build/lir-dump-test.ps1` unchanged. If any of those move, the refactor changed
behaviour and is wrong.

This is the CL that converts "ARM64 is a table entry" from intent to fact, and
it is worth doing on its own even if the rest of the campaign stalls.

### Step 2 -- prove the descriptor against a second target, before any codegen -- DONE, CL 9246

Two probe descriptors live in `IR/LirTargets.codex` (a chapter that exists
because a descriptor with no physical mapping has nowhere else to go:
`lir-target-x86` must stay beside `lir-phys`, and `IR/Lir.codex` names no
machine). `-Passes lir-dump-a64` runs the allocator, the structural verifier
and the allocation verifier over the `lir-check` corpus under both, and emits
nothing. It is off by default and the default pipeline produces zero probe
lines.

**Result: every lowerable definition in the corpus is clean under both
descriptors, under both verifiers.** No count is written here on purpose. This
line said "28 of 28" from a hand count of a scrolled log, was corrected to 27
when the pin was first recorded, and was 33 per descriptor a few hours later
because blu's CL 9291 made `Resolve-CiteOrder` always walk Foreword ListUtils,
which widened every compilation unit. The number tracks the corpus, not the
claim. Read it off `codex/test/lir-check.a64-expected`.

Three things that came out of it, two of them worth more than the green:

- **The division-fixed pair does express "this target fixes none."** Both
  probes set `diva`/`divb` to -1 and neither corrupts the occupant map:
  `lir-alloc-clobber-one` guards each index and `lir-free-mask` masks nothing.
  The trap the prose warned about is genuinely closed, and `pmv-clash` and
  `coal-result` (which carry divisions) allocate and verify clean without one.
  This is the prediction above, confirmed.

- **A WIDE file tests LESS than a narrow one, and the first probe was almost
  useless because of it.** The ARM64-shaped descriptor has nineteen registers
  and **not one definition in the corpus spilled** -- so the crossing arm,
  Belady eviction and the entire spill path never executed, and a green over
  paths nothing runs is not a green. The corpus was built to reach x86-64's
  pressure points at nine registers. The fix is `lir-target-narrow-shape`
  (two callee-saved, one scratch, two argument registers, still no division
  pair), which forces `pmv-clash` to `s0`/`s1` and exercises the corners --
  and is the combination x86-64 can never present, since every spill the
  shipping target has ever taken had a division-fixed pair to mask. **Any
  future descriptor probe must include a narrow variant or it is decorative.**

- **A malformed descriptor was not diagnosed** (fixed, CL 9287). Built with
  `budget` 8 against `nregs` 19, the allocator is unaffected and correct and
  the verifier reports 44 spurious violations across 16 definitions, all of
  them accusing the allocator. That run is also this probe's sensitivity
  proof: the instrument can fail, and it was made to. `lir-target-viol`
  (`IR/Lir.codex`) now checks the descriptor's structural invariants at the
  entry to `lir-alloc-check` and answers one message naming the field, so the
  same corruption halts with "LirTarget.budget (8) is below nregs (9)" instead
  of forty-four accusations against working code.

**Both probes are pinned** by `build/lir-a64-test.ps1` against
`codex/test/lir-check.a64-expected` (54 lines, 27 per descriptor). It fails on
a perturbed register, on a dropped pass, and on a NARROW run that stops
spilling -- the last because the narrow descriptor exists to reach the
crossing arm and eviction, so a narrow run with no spill slot is testing the
wide case twice. Read a divergence carefully: **both targets moving together
is an ordinary allocator change; only one moving is the allocator having
become target-sensitive, which is the whole thing this pin is for.**

**What step 2 does NOT cover, stated so the green is not over-read.** The
probe runs the allocator and the verifiers. It never touches the selector, so
the two remaining predictions are still open and belong to step 4:

- **Two-operand assumptions.** The selector's aliasing machinery
  (`lir-alias-r`, `lir-sel-bin-aliased`, the read-then-write slot numbering) is
  x86's `mov dst,src; op dst,r` shape. ARM64 is three-operand and needs none of
  it. What step 2 *can* say is that it has not leaked into the allocator's
  numbering -- the numbering is what the intervals are built over, and three
  descriptors agree on every assignment's validity. Whether the selector can be
  parameterised is untested.
- **Flag-setting comparisons**, named in `LIR.md` section 11 as a suspect, are
  emitted by the selector and were not exercised.

### Step 3 -- decide the delivery route -- THE OPEN QUESTION IS ANSWERED (2026-07-19)

**Yes: the plug's parsed IR carries enough to lower to the LIR, for exactly the
constructs the LIR whitelist accepts. Route A is viable.** Measured by reading
the emitter, the parser, and every type the lowering touches.

The lowering's demand on the type system is far narrower than section 2 assumed.
`lower-def-to-lir` reads a `CodexType` in three places only -- `pat-lowerable`,
`lir-bind-pattern`, `lir-match-test` -- and each takes the type **off the
`IrCtorPat` node itself**, not from a `type-defs` table. So the question was
never whether `ParsedIR.type-defs` is rich enough. It is whether a constructor
pattern's type survives the wire.

It does:

- `IrCtorPat (Text) (List IRPat) (CodexType) (SourceSpan)` is **byte-identical**
  in both copies, and the emitter writes its type (`IRTextEmitter.codex:362-364`)
  exactly where the parser reads it (`IRTextParser.codex:488-499`).
- A full `SumTy` round-trips through `parse-type-sum` with **resolved
  `CodexType` fields**, which is what `lir-field-width` / `-signed` / `-offset`
  need.
- **Tags need no wire representation.** `lir-ctor-tag` returns the constructor's
  *index* in the list and the parser preserves declaration order, so both sides
  derive the tag the same way from the same order. `ParsedIR.type-defs` carrying
  no tag field is therefore not the gap it looks like.
- `Types/CodexType.codex` and `Types/CodexTypeHelpers.codex` (which own
  `hw-width-bytes` / `bounds-to-hw-width`) **cite nothing** -- leaf chapters, so
  Route A copies them into the bundle without dragging the Types quire.

**The four `PlugTypes` divergences do not block it**, checked one at a time
against what the lowering actually reads: `IrVecPat`'s missing `CodexType` is
moot because `pat-lowerable` declines vector patterns anyway;
`IRBranch.alt-group` and `IRDef.unique-params` are read by neither the lowering
nor the emit gate; `IRChapter`'s different shape matters only to `ch.defs`,
which both have.

**But the diff that answered the question also found a live miscompile**, and
that is what should change how the rest of this campaign is run.
`ir-expr-type` disagreed on `IrNegate` between the two copies -- the compiler
recurses into the operand, the plug answered `int-ty-default` -- so `-(-x)` on
a `Real` integer-negated an IEEE double on **both** native backends. `-(-2.5)`
answered 1.7 on ARM64 and RISC-V and 2.5 on x86-64. Fixed and pinned by
`codex/test/real-neg-neg.codex` (CL 9254), which has to be a cross-architecture
test: x86-64 is compiled by the other copy and cannot fail.

Note what did *not* find it. The x86 battery cannot; the fixed point cannot;
`real-negate` -- a test written specifically for negation on Reals -- passes on
both backends, because it negates calls and fields and never a negation. The
bug lived in the one shape no existing test had.

**The re-derivation itself is gone as of CL 10176.** `IrNegate`
carries a `(CodexType)` filled at lowering; the text wire sends it and the plug
parser reads it, and `ir-expr-type` -- one definition, bundled -- returns the
carried type instead of recursing. There is no longer a second derivation for a
copy to disagree with. `real-neg-neg` stays: it is what would see the field stop
being filled. This closes the instance and not the class, which is the point of
the paragraph below.

The lesson for step 4 is not the one line. `PlugTypes` used to be a
hand-maintained copy that nothing diffs, and Route A's entire premise is
bringing more compiler chapters into that bundle -- so on the old footing every
chapter Route A added was another copy that could drift in silence, invisible
until a program took the one path where the two answers differ.

**That precondition is satisfied, and the way it was satisfied is the shape Route
A should follow.** The plug no longer keeps a ledger of the compiler's types
beside the compiler's: `plug-build-lib.ps1` brings the declaration chapters
themselves into the bundle by path (`Core/Name`, `Core/SourceText`,
`Types/CodexType`, `Ast/AstNodes`, `IR/IRChapter`), and `PlugTypes.codex` is
down to emitter helpers. There is one declaration of these types in the tree and
every plug reads it.

**What makes it hold is that both now land in ONE compilation unit, so the
compiler enforces it and no script has to.** Re-declaring a bundled type in
`PlugTypes.codex` fails the plug build with `CDX3001` and emits no binary
(measured on the lua plug, with the clean build as its control). Route A
inherits that guard for free -- but only for chapters it brings in whole by
path. **Anything Route A copies or paraphrases instead of bundling steps back
outside the guard and reopens exactly this bug.**

### The two routes (Route A now preferred on evidence)

**Route A: the LIR travels into the plug bundle.** Add the LIR chapters to the
plug bundler's chapter list the way `PlugTypes`/`IRTextParser` already are
(`plug-build-lib.ps1:156-157`), and lower tree->LIR plug-side. Mirrors what
already works, needs no new quire, and sidesteps Library Rule 2 entirely by
copying chapters into the unit rather than declaring a sideways dependency from
`codex.plugs` into `codex`.

**Route B: the wire carries LIR.** The compiler lowers and allocates, and the
native plug does instruction selection only. One lowering, already post-RESOLVE,
and the plug gets much smaller.

Route A is the smaller step and matches the existing shape. Route B is cleaner
and is the only one that works if the plug's parsed IR turns out to be too thin
to lower from. **The transpiler plugs want neither** -- they emit high-level
source and have no use for registers -- so if Route B is taken the wire format
becomes mode-dependent, which is a cost to weigh rather than a blocker.

### Step 4 -- the ARM64 selector, behind the same ratchet the x86 one used

Whitelist by construct, fall back to the existing `a64-emit-*` tree codegen for
everything outside it, one construct per CL, each through the gate. The x86
selector's staging is the template and it worked; do not invent a new one.

**Step 4a is done: the descriptor and an empty whitelist.**
`codex/plugs/arm64/Arm64Lir.codex` carries `lir-target-a64`, the physical
mapping `a64-lir-phys`, the scratch pair, the slot arithmetic, and
`a64-lir-emit-try` wired into the head of `a64-emit-function`. No construct is
whitelisted, so every function still goes to the tree emitter. Measured on a
twelve-test corpus: **all twelve emitted ARM64 ELFs are byte-identical** to the
ones the pre-change plug produces, which is the property the ratchet is for.
The plug binary itself grows 476473 to 526376 bytes, because naming
`lower-def-to-lir` in a statically-dead branch is still reachability as far as
whole-program DCE is concerned. That is the price of having the lowering
compiled, type-checked and linked before the first construct lands, and it is
paid once.

**The register file is data, and ARM64 is where that claim first costs
something.** x86-64 gives one abstract index both the "return register" and the
"non-crosser preference" roles, and it gets away with it because `RAX` is not
an argument register. On ARM64 `x0` is the return register **and** argument
zero. Mapping it at both the preference index and the head of the argument
window would give one physical register two abstract indices, and the allocator
would believe it held two independent values. The descriptor puts `x9` at the
preference index instead: caller-saved, not an argument register, no other role
in this backend. `x0` keeps its single meaning, and parameter `i` still lands in
the register it arrived in, so the entry moves still elide.

The file is `x19..x27` callee-saved (nine), `x9` alone, then `x0..x7` in
argument order: `ncallee` 9, `nargregs` 10, `nregs` 18. `x28` is excluded
because it is the bump allocator, the ARM64 counterpart of `R10`. `diva` and
`divb` are both -1, so this is **the first shipping target to answer -1**, and
the guarding that `LirTargets.codex` verified with the probe descriptors is now
load-bearing rather than hypothetical.

**Step 4b is done: the first whitelisted construct is the function with no
instructions at all.** One block, zero instructions, result a parameter or an
immediate. Reading the lowering is what found it, and it is not a shape anyone
would think to name: `ident (x) = x` lowers to `b0: => v0`, `konst = 42` to
`b0: => #42`, `pick2 (a) (b) = b` to `b0: => v1`. Identities, constants and
argument selectors all land here. It exercises the dispatch, the per-function
bookkeeping and the return path while selecting no instruction, so a defect in
it cannot be a defect in instruction selection.

With no instructions every vreg is a parameter, so the result operand is a
parameter index and parameter `i` has arrived in `x_i`. The emitter reads the
index: **this construct does not consume the register assignment.** The
allocator and both verifiers still run over real functions, so the descriptor
is exercised, but no emitted byte depends on what they answered. The next
construct is the first that consumes it.

Lane, re-measured against the 4b plug: **302 PASS / 6 compile-only / 48 FAIL /
57 SKIPPED of 413, zero regressions** on a per-test diff against the step 4a
run. `codex/test/lir-nullary-cross` is the pin, and its sensitivity run is what
proves the LIR path emits these functions: with the parameter index ignored,
`pick2`, `pick-mid`, `pick-last` and `deep` fail on exactly the distinguishing
rows while `ident` and `konst` still pass.

**The regression 4b found is worth more than the construct.** `opening :
Integer = 0` is itself a zero-instruction function, and on this backend the
entry point is not an ordinary function: `a64-emit-function` appends
`integer-to-text` and `print-line-uni` after its body so the program prints what
`opening` returned. The selector claimed it, emitted `li x0, 0; ret`, and the
program printed nothing. `opening` is now declined by name. The general lesson:
**the tree path carries special cases attached to a function NAME, and a
whitelist written in terms of instruction shapes cannot see them. Grep
`a64-emit-function` for name-based special cases before whitelisting any
construct.**

**Step 4c is done: straight-line integer add/subtract/multiply in a leaf**, and
it is the first construct that consumes the register assignment. A single block
whose every instruction is a `LiBin` over `LoAdd`/`LoSub`/`LoMul`;
`codex/test/lir-binop-cross` is the pin.

**The first of step 2's two open predictions is answered, and the answer is
that ARM64 needs none of it.** x86-64's selector carries `lir-alias-r` and
`lir-sel-bin-aliased` because its ALU is two-operand: `op dst, src` destroys
`dst`, so the case where the destination register already holds the right
operand has to be detected and handled (`LoSub` there costs a `neg` and an
`add`). ARM64's `add`/`sub`/`mul` are three-operand and read both sources
before writing the destination, so that case is simply correct with no special
case at all. **The retarget removed machinery rather than porting it**, which
is the outcome the "register file is data" claim wanted and the one it is
easiest to accidentally not get.

**Step 4e is done and it answers the second prediction: flag-setting
comparisons work off x86.** `LiCmp`, `LiBranch` and `LiRet` are whitelisted, so
an `if` whose arms both return goes through the LIR:
`math-min (a) (b) = if a < b then a else b` lowers to
`b0: cmp v0 v1; br lt b1 b2 | b1: ret v0 | b2: ret v1`. `arm64-cmp` is
`subs xzr`, which discards its result exactly as x86's `cmp` does, so the
prediction resolves in the dull direction: the condition codes differ (EQ 0,
NE 1, GE 10, LT 11, GT 12, LE 13) and nothing structural does.
`codex/test/lir-branch-cross` is the pin.

This is the first multi-block function on the backend and so the first with a
block-offset table and branch fixups. Two things in it are load-bearing and
both were fired rather than assumed:

- **The table is keyed by block ID, not by layout position.** Ids are handed
  out eagerly, so a nested `if` numbers its arms above the join it jumps to and
  the two orders genuinely differ -- `nest` lays out b0, b1, b4, b5, b6, b2, b3.
  Keying by position breaks `nest` and NOTHING else in the pin, which is
  exactly the discrimination that makes it worth a test.
- **The condition is inverted when the true successor is the next block laid
  out**, which is the common case because the lowering emits the true arm
  first. Getting that backwards inverts every branch; the pin catches it on
  seventeen rows, and `mn-eq`/`mx-eq` deliberately do not move because both
  arms return the same value there.

**`LiTest` is NOT whitelisted, and the reason is that nothing could fire it.**
It is `LiCmp`'s bitwise-AND sibling, spelled `tst` on AArch64, which this
encoder cannot emit -- so it would be an `and` into a scratch plus a compare
against zero, which is easy to write. It appears only where the compiler fuses
a power-of-two remainder against zero, and that fusion happens after
`inline-leaf-calls` inlines the remainder. It does so in `lir-check` and does
not in a file written for this construct, where `math-mod n 2 == 0` stays a
call. Two attempts to provoke it failed.

**Both of those conclusions were wrong, and the third attempt found the real
blocker.** The compiler fuses this shape perfectly well in an ordinary file: a
three-function probe under `-Passes +lir-dump` gives `test v0 #1` for
`math-mod n 2 == 0` and `test v0 #3` for `math-mod n 4 == 0`. What cannot reach
it is the PLUG, and for a reason that has nothing to do with `tst`: the IR wire
the plug receives has not been through `run-ir-pipeline` at all, so `math-mod`
is never inlined and the shape arrives as a call plus a compare. The ARM64
disassembly is `MOVZ X1,#2; BL math-mod; CMP X9,#0`.

An `arm64-tst` encoding and the selector arm were written and then **dropped
rather than shipped**, because an emitter arm nothing can execute is worth what
no arm is worth. `codex/test/lir-test-cross` ships anyway: it pins the fusion's
two guards by answers, and those answers have to agree on both lanes however
differently each reaches them.

**Closed by CL 10262/10263; the blocker is gone.** The IR
emitters now run the pass pipeline through `compile-frontend-ir`, so the wire
the plug receives is inlined: `math-mod n 2 == 0` fuses to `test v0 #1` before
it leaves the compiler, `math-mod` does not appear in the ARM64 symbol map for
`lir-test-cross` at all, and `-Passes +lir-dump` on that file now shows
`b0: test v0 #1; br eq ...` for the three power-of-two shapes while the two
guards (`lt-mod3` divides by three, `lt-modone` compares against one) keep their
`rem` and so still decline. The `rem`-carrying guards decline to the tree
emitter regardless, because `LoRem` stays outside the whitelist -- so only the
fused shapes take the LIR path, which is exactly what the pin wants.

The method lesson is the one this campaign keeps re-learning from the other
direction: at 4e the trap was reading a shape off `-Passes lir-dump` and
assuming the plug would accept it. Here it was reading "cannot be fired" off two
old sessions and not re-measuring. **"Nothing can fire this" is a measurement
with a date on it, not a property of the code.**

## Step 4h -- the flag-setting `tst`. DONE, CL 10353/10355.

The dropped work was written again: `arm64-tst (rn) (rm) = arm64-rrr #EA000000
reg-xzr rn rm` (`ANDS xzr`, the flag-setting sibling of `cmp`'s `SUBS xzr`),
`LiTest` whitelisted, and `a64-lir-emit-test` materialising the mask into the
second scratch and emitting the register form. The immediate form is
deliberately not taken: `tst Xn, #imm` is the logical-bitmask encoding
(N:immr:imms), and while the fused masks 1, 3 and 7 are all valid bitmask
immediates, encoding them is fiddly and the register form is always correct at
the cost of one `li`.

`codex/test/lir-test-cross` was already in the tree from 4e and needed no
change: it pins answers, and 4h is what makes the ARM64 lane reach them through
the LIR rather than through the tree emitter. Zero-regression battery at a fixed
seed, sensitivity proven. No seed -- the compiler does not cite `Arm64Encoder`.

**This step shipped its code and updated neither this document nor the register,
both of which went on listing `LiTest` as open work for a session.** That is the
standing register rule read in its cheap direction: an entry claiming a gap that
is closed costs the next reader a re-derivation, exactly as an entry dropping a
gap that is open costs them the gap. Corrected with 4i.

## Step 4i -- the constructor-pattern load (`LiLoad`). DONE.

The last per-construct arm. A `when s is Circle (r) -> ...` lowers to
`LiLoad tag sv 0 8 False`, a `LiCmp` of the tag against the constructor's
declaration index, a `LiBranch`, and then one `LiLoad` per bound field at its
packed offset -- so every instruction in a variant match except the loads was
already whitelisted, and this notch is what makes the whole shape compile here
instead of falling back.

**Only the eight-byte width is admitted, and that is an encoder bound rather
than a selector one.** `lir-field-width` reads the shared width table, so a
pointer, an unbounded integer and a sum payload are eight bytes and a bounded
integer is narrower. `arm64-ldr` covers eight exactly; the narrow cases want
`ldrsw`, `ldrh`, `ldrsh` and `ldrsb`, none of which the encoder has. A narrow
load declines to the tree emitter, which reads it correctly today, and adding
the four encodings is a follow-on with its own sensitivity runs.

**The offset gate cannot fire, and it is there because the encoder masks.**
Fields pack widest-first from offset 8, so an eight-byte field is always at
`8 + k*8` and the tag at 0. But `arm64-ldr`'s unsigned form scales its immediate
by eight and masks it to twelve bits, so a non-multiple-of-eight offset would be
silently rounded down and one past 32760 silently truncated. Same reasoning as
the 4095-byte frame bound at 4d, and the same choice: refuse a shape the encoder
cannot refuse.

`codex/test/lir-load-cross` pins the eight-byte shapes by answers and pins the
DECLINE with them, which is the part worth carrying to the next backend.
`Bytes` carries two one-byte fields, so they are adjacent at offsets 8 and 9: a
gate that wrongly admitted them would answer `a + b * 256` for the first and the
same value again for the second, because `9/8` is 1. So the decline is
discriminated by an ANSWER rather than by a shape, which is the only way a
decline is testable at all -- a narrow field read by the tree emitter and a
narrow field read by a correct selector give the same number, and only a WRONG
selector separates them.

**The sensitivity run discriminates in both directions at once, and that is why
it is worth more than the green.** No-oping `a64-lir-emit-load` breaks all seven
eight-byte rows of the pin and leaves the three narrow rows untouched. The first
half proves the LIR path claimed the shapes rather than the tree emitter behind
it; the second proves the width gate really declines, rather than claiming a
load it would encode wrong. Both halves are necessary: `lir-load-cross` passes
on the PRE-change plug too, because the tree emitter reads these fields
correctly.

**A battery row went green, and the mechanism is supersession rather than
luck.** Measured against the same seed with the pre-change plug: 318 PASS / 6
compile-only / 49 FAIL / 58 SKIPPED, against 319 / 6 / 48 / 58 after, with
`db-row-update` the single per-test change and no regressions. It matches
`is ValInteger (n)` and `is ValText (t)` -- the `LiLoad` shape -- so before 4i
those functions fell back to the ARM64 tree emitter and one of them answered
wrong. `FAIL_OUTPUT` is the one class the batch harness never retries, precisely
because a wrong answer is deterministic rather than contention, so the baseline
failure was real and stable; it passes standalone now.

This is the payout the design doc predicted for frame elision and it should be
read as the campaign's argument rather than as a bonus: **do not fix an ARM64
tree-emitter miscompile in the tree emitter, because the whitelist supersedes
that code one construct at a time.** One of 3.19's FAIL rows closed here without
anyone debugging it.

**One combination was untested and is named rather than implied: a return that
has to undo a frame.** Every branching shape in the 4e pin is frameless, and
the eight-parameter shape written to combine the two is declined by the plug --
`wide-branch` is clean under the ARM64-shaped probe descriptor
(`[alloc-check: ok]`, `[check: ok]`) yet perturbing the branch inverter leaves
its answers correct, so the selector is not claiming it. **The plug lowers the
IR it receives over the wire with its own `lower-def-to-lir`, and that is where
it disagrees with the compiler's own lowering that `-Passes lir-dump` shows.**
**4f closes the combination from the other side**: a call forces a frame (the
link register), so `callcond` in `codex/test/lir-call-cross` is a returning
`if` whose every `LiRet` undoes one.

## Step 4f -- the call

Whitelisting `LiCall` needs three things that no earlier construct did:
the arguments placed where the ABI wants them, the link register preserved
across the `bl`, and the result taken out of `x0`.

**The argument placement went into the compiler, not the backend**, beside the
entry-move planner and for the same reason: it is a parallel move, an ordering
bug in one is a wrong answer rather than a crash, and a backend-local copy of
that logic is what kept `col-hue` miscompiled for ten sessions. `IR/Lir.codex`
Section: Argument Moves plans in abstract register indices -- argument `i` has
to reach `nargregs + i`, the same index parameter `i` arrives in -- and
simulates the plan against the assignment rather than re-deriving it. The
backend supplies the index-to-register mapping and nothing else, and it shares
the entry moves' mapping because two mappings that must agree are one mapping.

**One thing to know before replacing x86's `lir-place-args` with this**, read
off the x86 pin rather than reasoned: the descriptor's argument window for
x86-64 is `[nargregs, nregs)` = four registers, while `lir-place-args` places
into `arg-regs`, which is System V's six. So the planner refuses to plan a
five-argument call on x86 (`fold-list` dumps `beyond the argument registers`)
where the existing selector would have placed it. Either the descriptor's
window is wrong or the two disagree about what an argument register is, and
that has to be settled before the swap -- it is invisible today only because
x86 codegen does not consult this planner.

**The link register is saved exactly when the body contains a call.** A leaf
emits the frame 4d emitted, byte for byte; a caller reserves one more cell
above the saved callee-saved registers. Getting this wrong is not subtle -- a
function that returns after a call without saving `x30` returns into its own
callee's return site.

**A call whose target does not resolve is declined, and this is the one gate
that is specific to this backend.** An ARM64 call is a placeholder instruction
plus a patch record, and `a64-patch-calls` leaves the placeholder ALONE when
the name is not found: the call becomes a `nop`, does not happen, and the
caller reads whatever was in `x0`. There is no diagnostic channel to complain
through (`A64State` has no error bag and the wire carries the binary), so the
gate is on the name resolving with a matching arity before anything is emitted.
This is the unresolved-call class met head-on. It costs coverage: only a backward
reference is admitted, so a self-call and a call to anything defined later in
the module are declined until a module pre-scan exists.

**What fired and what did not, measured.** `codex/test/lir-call-cross` moves
all ten of its answers when the result register is perturbed, so the selector
claims every shape in it. Zeroing the argument planner's fuel so it emits no
moves changes seven of the ten. **Disabling the planner's blocked check changes
nothing at all**, and that is the honest finding of this step: the ordering
logic is unfired on ARM64.

The reason is narrower than "the argument window is empty at a call", and the
pin disproves that stronger claim -- `list-tail-loop` keeps `v5` in `r12`, an
argument register, and passes it as argument 1. What the crossing analysis
guarantees is only that values live ACROSS a call are in the callee-saved pool.
A value that dies AT the call may sit in the window, and every one that does in
this corpus lands on its OWN destination (`r12<-r12`, elided at emit). A cycle
needs one to land on a DIFFERENT argument's destination, and nothing in the
corpus or in `lir-call-cross` produces that. It is kept because it is checked by `lir-check.a64-expected` under
the probe descriptors, and because x86's `lir-place-args` -- which this exists
to replace -- reaches real cycles through its allocation hints.

One reading trap cost a build here and is worth carrying: in a chain of calls
the `mov` immediately after a `bl` is the previous call's RESULT move, not the
next call's first argument move. Read the other way, `callchain`'s listing
makes the ordering logic look fired when it is not.
Reading the shape off the x86 dump tells you what the compiler produces, not
what the plug will accept, and this is the first step where those two came
apart.

**What 4c does not do, stated because the gate is what makes the arm sound.**
It emits no prologue and no epilogue, so after the allocator runs the function
is declined unless three things hold: no spill slot is used, no callee-saved
register is used, and every parameter is still in the register it arrived in
(there is no entry parallel move, which on x86-64 is real code with its own
verifier, CDX9006). A leaf doing straight-line arithmetic satisfies all three
in practice -- with no call to cross, the allocator prefers the non-crosser
register and reuses it down the chain while the parameters keep their argument
window -- and that was read off `-Passes lir-dump` rather than assumed.

**Step 4d is done: the frame**, and it lifts the first two of those three. One
`sub sp` reserves a sixteen-byte-aligned region holding the spill slots at the
bottom (so slot `k` stays at `[SP + k*8]` and needs no base added) and the used
callee-saved registers above them; the epilogue restores and releases. There is
no frame pointer, no saved link register and no stack guard, because the
whitelist admits no call: nothing writes LR, and a function that makes no call
cannot grow the stack into the heap. The one gate that remains is the frame's
own size, bounded at 4095 bytes because `arm64-sub-imm` masks its twelve-bit
immediate rather than refusing an oversized one, and a silently truncated stack
pointer is a miscompile rather than a diagnostic. `codex/test/lir-frame-cross`
is the pin.

**The third decline is gone too, and the planner for it went into the compiler
rather than the backend.** `IR/Lir.codex` (Section: Entry Moves) plans the
moves in ABSTRACT register indices -- parameter `i` arrives in `nargregs + i`
and has to reach `assign[i]` -- so nothing in it is target-specific and a
backend supplies only the mapping from index to register. It then SIMULATES its
own plan against the assignment rather than re-deriving it, which is the check
the allocation verifier structurally cannot make: that verifier seeds the
parameters into their assigned locations, which is to say it assumes these
moves worked, and `col-hue` was miscompiled behind that assumption for ten
sessions while `[alloc-check: ok]` printed on every build.
`Emit/X86_64Lir.codex` still carries its own copy planned in physical
registers; a backend-side copy of compiler logic that nothing diffs is the bug
class 3.15 was about, so that one should be replaced by this rather than kept
beside it.

**What fires it is not an ARM64 program, and that distinction is worth keeping
straight.** No shape the ARM64 whitelist admits can displace a parameter: every
parameter is given interval start `-1`, expiry is `fin < start`, so none
expires against another, and each takes the argument register it arrived in.
Only a call, a division or a hint can move one, and this whitelist has no call
and no division while the allocator never hints a parameter. The planner is
fired instead by `codex/test/lir-check.a64-expected`, which runs it over the
real corpus under the ARM64-shaped and narrow probe descriptors, where calls DO
push parameters into the callee-saved pool: `list-zeros-loop` plans
`r0<-r11 r1<-r12 r2<-r13`, three real moves, simulated clean. What still waits
on `LiCall` is only the mapping from a plan to ARM64 instructions.

**The simulation caught a real defect on its first run**, which is the evidence
that it can fail at all. The planner assumed parameter `i` arrives in
`nargregs + i` unconditionally; that holds only while the target HAS an `i`th
argument register, and the narrow probe has two while `pmv-clash` has five
parameters, so three sources indexed past the end of the file. A sixth
parameter on x86-64 and a ninth on ARM64 arrive on the caller's stack and
moving one is a load, not a register move. Both selectors already decline such
a function before the allocator runs, each at its own argument-register count,
so the planner is never asked in a real compile -- and it now says so rather
than planning something that cannot be right.

**How much of this the register file forces is worth recording.** ARM64 gives
the allocator eighteen registers, so a leaf reaches the callee-saved pool only
at ten simultaneously live values and spills only at nineteen. No two- or
three-parameter expression comes close; the pin needs an eight-parameter tree
(eleven live) to reach the first and an eight-parameter right-leaning spine
(twenty live) to reach the second. On x86-64's nine registers the same two
shapes spill two and eleven slots respectively. A frame is therefore much rarer
on this target than on x86-64, which is a fact about the machine and not about
the selector.

Immediates are materialised into a scratch register and fed to the
register-register form, because ARM64 multiply has no immediate operand. That
is correct and it is not free: see the instruction-count note below.

**Do not fix 3.2 (ARM64 frameless miscompile) in the tree emitter.** It is a
narrow shape-specific miscompilation in `a64-emit-function-frameless`, and that
is precisely the code the LIR path supersedes. Let it be superseded.

## Step 4g -- the join (`LiMove` and `LiJump`). DONE, CL 10270.

The two constructs that admit the OTHER `if` shape: the one whose arms do NOT
return but compute into a shared vreg and jump to a join block where the joined
value is used. `let v = if c > 0 then 3 else 4 in v` lowers to
`b0: cmp v0 #0; br gt b1 b2 | b1: v_s = mov #3; jmp b3 | b2: v_s = mov #4; jmp b3 | b3: => v_s`,
and `(if c > 0 then 10 else 20) + 1` puts the `add` in the join block rather than
leaving it empty. This is the shape 4e excluded when it whitelisted the
returning `if`, and it is what `LiMove` (copy a `LirValue` into a vreg's
location) and `LiJump` (the unconditional sibling of `LiBranch`) are for.

**The gate relaxed with it.** A non-returning function no longer has to be a
single block: the only shape still handled specially is the zero-instruction
function, whose result is a parameter index read directly rather than an
allocated vreg. Everything else -- returning or not, one block or many -- goes
to the whitelist check, and the framed emitter reads its result out of the
allocation.

**This backend needs none of x86-64's `canon` fall-through machinery, and that
is a property of the two emit models rather than a shortcut.** x86-64 routes
every return through one shared epilogue block, so two arms both ending
`LiJump exit` land on the same synthetic block and the elision has to canonise
an empty exit to the epilogue or emit a five-byte jump to the instruction
underneath (the CL 8600 bug). Here each `LiRet` carries its own inline epilogue
and the join's arms fall out the bottom: the exit block is always the last one
lowered (`lower-def-to-lir` pushes the current block last), a jump to it
resolves through the offset table to wherever it physically sits, and eliding
only ever happens against the LITERAL next block, which is always a safe
fall-through. So `a64-lir-next-id` reads the literal next id and no
canonicalisation is needed for correctness.

`codex/test/lir-join-cross` pins five join shapes -- empty exit, continuation
in the join block, register arms, nested, and two joins in sequence -- by
answers recorded from x86-64. The sensitivity proof that the LIR path emits
these rather than the tree emitter behind it is a perturbation of
`a64-lir-emit-mov`: making it a no-op breaks exactly those rows and nothing else
in the ARM64 battery. Measured zero-regression against the same seed with the
pre-4g plug: 317 PASS / 6 compile-only / 48 FAIL, no per-test change.

### Step 5 -- RISC-V by the same table

`rv-alloc-temp`/`rv-alloc-local` (`RiscVCodeGen.codex:241-263`) are the same
modulo-4 bump-and-spill shape as ARM64's. If steps 1-4 landed honestly this is
a descriptor and a selector, and RISC-V is the proof that the table abstraction
holds for a third target rather than having been bent to fit the second.

## 4. Risks, named

- **`LIR.md` section 11's first limitation is stale and should be corrected.** It says
  "v1 does not touch loops because the IR has none". Back-edges exist now
  (`lir-has-backedge`, `IR/Lir.codex:222-231`), so the no-fixpoint liveness
  argument no longer rests on loop-freedom: it rests on a narrow
  header-intersection special case (`Lir.codex:1483-1490`) whose soundness
  argument is that a header's entry set is the parameters and cannot shrink.
  **A new target inherits that argument.** Re-read it before trusting liveness
  on ARM64; if a retarget ever makes a loop carry something other than the
  parameters, the argument is void.
- **The allocator is where silent miscompiles live.** The `col-hue` parallel-move
  bug survived ten sessions of green benches and byte-identical fixed points.
  For a retarget the corpus battery is the instrument, not the benches -- and
  the honest baseline is the *same source with the new path gated off*, never
  the seed (which is an old snapshot and compares two variables at once).
- **`gen2 === gen3` proves determinism, not correctness.** It will be green for
  a compiler that miscompiles the same way twice.
- **Register pressure is the hard case at ~10 registers** (`LIR.md` section 11). ARM64
  has far more, so the retarget may *look* better than x86 for reasons that say
  nothing about the allocator's quality. Compare against the ARM64 tree emitter,
  not against x86.

## 5. What "done" looks like

ARM64 and RISC-V both allocate through `lir-alloc-linscan` over their own
descriptors; the ad-hoc `a64-alloc-temp`/`a64-alloc-local` and
`rv-alloc-temp`/`rv-alloc-local` bump allocators are deleted, which needs the
tree emitter retired and is therefore a separate and much larger item than the
allocator 3.1 asked for; and the cross-arch battery runs and is honest again.
