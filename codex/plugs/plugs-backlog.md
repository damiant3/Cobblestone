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

**A NAME CENSUS CANNOT ANSWER A SEMANTICS QUESTION, and it is wrong in BOTH
directions.** Kept from 1.31 when that entry closed, because it is the trap
this quire's measurements keep falling into. Keying on the quoted Codex name
misses a plug that declares the arm in a prelude (`winforms` reads as ZERO
while being perfectly armed) and counts a plug whose REFUSAL text contains the
name (`zig` read as ONE for `text-replace` because the string sat inside a
`@compileError`, which is the opposite of an arm). **And a registered name is
not a correct arm**: fortran had all five text builtins registered and two of
them answered wrongly. Run a subject through the plug and read the OUTPUT: one
occurrence is a bare call with nothing defining it, two is a definition plus a
call, none at all means the builtin was inlined and never spelled.

**THE SECOND HAZARD, and it is the one that wastes a whole session: A STALE
PLUG BINARY.** Nothing in this quire runs from the `.codex` you are reading.
Every harness runs the `.cdx` beside it, so a plug binary older than its
source is a confident wrong answer in either direction: a green scored on the
old emitter, or a red for a defect that was fixed. Measured 2026-08-18, three
times in one session. An `img` plug a day old FAULTED on its own known-good
subject and the harness reported OK on 2800 bytes. A `typescript` plug one
merge-down behind emitted a prelude truncated to its first function and failed
36 lines. A `zig` plug reported PASS 33 of 33 against a binary a day old, and
that green had to be discarded. **Rebuild the plug before you believe any
measurement through it, and a merge-down invalidates every plug binary it
touches.** `build/plug-oracle-test.ps1` refuses a stale binary now; nothing
else does.

**Zig ownership, SETTLED by Damian 2026-08-18:** `codex/plugs/zig/` is ORDINARY FLEET CODE and is edited like any other plug. Steve Howell had it for his early updates, those updates are absorbed, and the loan is over (Damian, 2026-08-18). Credit him in a CL that changes what he wrote and flag it in the next GitHubUpdate, which is courtesy rather than a gate. This supersedes both earlier readings: the 08-16 "not a fleet edit" rule AND the "ours to gate loosely, no rigour beyond a smoke" relaxation that replaced it. The plug is held to the same standard as its neighbours. The dispute paragraph that stood here from 16984 (two live docs disagreeing; fleet edits 16366, 16409, 16627, 16981 already landed under the "ours" reading) is gone with the dispute.

## 1.42 -- CLOSED: a unit constructor was emitted as a CALL on both plug lanes, and it resolved to nothing

**Fixed in the COMPILER, fester 2026-08-18, Damian's ruling 13 = (a).**
`lower-apply-normal` no longer builds an application for a unit constructor:
`is-unit-ctor` recognises one by its RETURN type rather than its spelling (a
function whose return is `UnitTy nm` and which is itself named `nm`), so
`Minute-to-Second`, which also returns a `UnitTy` and is not named for it,
stays a real call. Measured with the kernel named explicitly on every run:

| | depot seed `12B07296` | new compiler `E21873C4` |
|---|---|---|
| `unit-real-arith` unresolved calls | 28 | **0** |
| riscv lane, distinct unresolved names | 26 across 11 tests | **3 across 2** |
| riscv battery | 52 failures | 52, identical failure set |
| the ten unit tests on x86-64 | green by accident | green on purpose |

The three survivors were never unit constructors:
`to-real-approx-saturating`, `from-real-approx-saturating` and
`uefi-read-key`. They are separate plug gaps.

### Two wrong fixes came first, and neither could be seen by a test

Both are the same failure: a type quietly stopped being what it was, the
answer stayed right, and the battery reported green.

**v1, elide the application and retype the argument.** `set-ir-expr-type` has
NO ARM for a literal, because `IrIntLit` carries no type slot at all and
`ir-expr-type` answers `int-ty-default` for it. So `Celsius 100` became a bare
`Integer`, `try-unit-convert` stopped seeing a `UnitTy` on the argument, the
`Celsius-to-Kelvin` conversion never fired, and `codex/test/implicit-convert`
answered `cold` where `warm` is expected. **On x86-64, the lane that was
already correct.**

**v2, bind the value in a let so it carries the type.** `set-ir-expr-type`
DOES have an `IrLet` arm, and it is a trap: it rewrites the let's own type
field, while `ir-expr-type` of a let reads its BODY. The retype changed
nothing anyone reads. `Span = unit Metre` is the shape that exposes it: the
value of `Span (Metre 1.0)` went on reporting `Metre`, and `try-unit-convert`
one position out synthesised a call to `Metre-to-Span`, a conversion no
annotation declares and no program defines. `unit-real-arith` PASSED with
four of them in it.

**v3 retypes a let through its body**, which is the only place the type is
read.

**What found both was not the battery.** `unit-real-arith` reads
`PASS_EXPECTED` under the depot seed, under v2 and under v3. What
discriminates is compiling ONE file twice, once per kernel, and diffing the
unresolved-call NAMES: 28 under the seed, 4 different ones under v2, 0 under
v3. A test that passes for the wrong reason cannot tell you the reason
changed (L-FALSIF).

### The account of the defect, kept because the mechanism is the lesson

`Second = unit Integer` gives a constructor `Second`, and `Second 42` reached
both plugs as an ordinary application of a name no plug emits and no program
defines. The patcher leaves the branch unpatched and reports

    [WARN] unresolved call to 'Second': the riscv plug emits no such function,
    and the branch was left unpatched -- reaching it reads a stale a0

**The tests pass anyway, and that is the reason nobody noticed.** The whole
of it, `codex/test/unit-smoke` compiled for riscv64, at `0x8000B08C`. Three
instructions, and they are `let t1 = Second 42`:

    addi a0, zero, 42     a0 = 42, the argument
    addi zero, zero, 0    the call to `Second` -- a nop, never patched
    addi s2, a0, 0        t1 = a0

`rv-emit-call-to` emits `rv-nop` as the placeholder and records a patch;
`rv-patch-calls-loop` cannot find `Second` in `func-names`, so it warns and
moves on, and the nop stays. A unit constructor is the identity on its
representation, `a0` is both where the argument goes in and where the result
comes back, and a nop between them cannot spoil it. **The accident is exact.**
`unit-smoke`, `unit-family`, `unit-real-arith` and the rest are green on both
lanes today with an unresolved call in each.

**This entry said `to-real-approx-saturating` was the counter-example, a
missing function that is NOT the identity, and that was wrong (fester,
2026-08-18).** `emit-to-real-saturating-builtin` (`X86_64Builtins.codex:1565`)
emits its argument and returns that register, so it is the identity too and
its unresolved call is the same harmless accident. What actually breaks
`real-approx-modes` is width, and it is 1.44 below.

Measured 2026-08-18 (fester) over a full cross battery on each lane, counting
only compiles from that run: **26 distinct unresolved names on riscv64 across
11 tests, 27 on arm64**. Almost all of them are unit constructors (`Metre`,
`Celsius`, `Second`, `Kelvin`, `Hertz`, `Fahrenheit`, `Span`, `Tick`,
`Duration`, `Angle`, `Force`, `Energy`, `Power`, `Speed`, `DataSize`,
`Count`, `Flag`, `Name`, `Length`, `Dist`, `AstroDistance`, ...). The rest of
the tail is `to-real-approx-saturating` / `from-real-approx-saturating`
(`real-approx-modes` FAILS on both lanes, and the two lanes answer
DIFFERENTLY: `sat mul overflow 0` on riscv64, `1073741824` on arm64, against
an expected `2139095039`. Two targets giving two different wrong numbers for
the same source is the signature of a branch that was left reading whatever
the register held, which is what an unresolved call leaves behind) and
`uefi-read-key` (an x86 firmware entry point, in a test whose subject is that
there is no firmware).

**This was what blocked CrossLaneFilesystem step 0**, whose subject is
turning the unresolved-call `[WARN]` into a hard `[UNSUPPORTED]` refusal:
it could not go in while eleven passing tests carried one. With this closed,
the two tests still carrying an unresolved call are `real-approx-modes` and
`uefi-read-key-nofirmware`, and they are the whole of what step 0 has left to
answer for.

**IT IS NOT TWO LANES, IT IS AT LEAST SEVEN, and that bears on the choice
above (reek, 2026-08-18).** This entry reads as a riscv and arm64 finding.
The SOURCE plugs have it too, and there the accident that makes it harmless
on the native lanes does not apply, because there is no register to leave
untouched. `codex/test/unit-smoke` through the source plugs emits
`Second(...)` with **nothing defining it** in `python`, `typescript`,
`csharp`, `fortran` and `pascal` (4, 4, 4, 2 and 4 call sites; zero
definitions in every one). `cobol` and `ada` emit no call at all, so they are
clean by a different route.

**python is executable here and it is a hard failure, not a latent one:**

    t1 = Second(42)
    NameError: name 'Second' is not defined

So the per-plug option is not "repeated" across two lanes but across at least
seven, and the count is the whole source-plug set rather than a tail.

**RULED (a) by Damian, main 16938: the compiler erases unit-constructor
applications, and fester owns it as a seed-affecting change** (red,
2026-08-18). The source plugs stay untouched; a per-plug no-op arm in seven
emitters is exactly the work this ruling makes dead.

**The source-plug half is a TEST TARGET for that change, so here is what to
assert.** Subject `codex/test/unit-smoke.codex`, which declares
`Second`, `Meter` and `Minute` as `unit Integer`. Run it through each plug
and require that no unit-constructor name survives as a call:

| plug | `Second(` call sites before | definitions |
|---|---|---|
| `python` | 4 | 0 |
| `typescript` | 4 | 0 |
| `csharp` | 4 | 0 |
| `pascal` | 4 | 0 |
| `fortran` | 2 | 0 |
| `cobol` | 0 | 0 |
| `ada` | 0 | 0 |

`cobol` and `ada` emit no call at all and are already clean, so they are the
CONTROL: a change that erases the application must leave those two unmoved
while taking the other five to zero. `python` is the arm that can be RUN
here, and it is the one that fails outright rather than latently, so it is
the cheapest end-to-end check of the ruling.

## 1.44 -- CLOSED: both plugs computed a SATURATING f32 op in double, so the clamp never fired

**FIXED ON BOTH LANES (reek, 2026-08-18).** `codex/test/ops/real-approx-modes`
passes on riscv64 and arm64, confirmed on seed 318B2BF6 after a merge-down and
a rebuild of both plugs.

**The fix, and it is wider than the row proposed.** Eight dispatch arms per
lane, the four Saturating and the four Trapping, now branch on
`is-real-f32 ty`. `ty` was already the fifth parameter of
`rv-emit-binary-reg` and its arm64 twin, and the vector arms beside them
already branched on it, so nothing new had to be threaded. Each lane gained one
emitter: `rv-emit-real-saturating-arith-f32` and
`a64-emit-real-saturating-arith-f32`, single-width moves and ops with the
single-width clamp constants (`slli 33` / `srli 56` / against 255, mantissa
by `slli 41`). Both shift encoders mask the amount to six bits, so the larger
amounts needed no new instruction. Trapping needed no new emitter on either
lane: it has no exponent read, so it collapses to the existing approx path with
the `-s` ops.

**The row read this as a clamp that never fires. That was the second-order
half.** riscv line 3 answered 4278190078 = `0x7F7FFFFF` doubled exactly,
which is an f32 bit pattern run through an f64 add as a denormal; line 2's
`0` is denormal times denormal underflowing. The ARITHMETIC was wrong before
the guard was ever consulted, so the single-width exponent constants alone
would have fixed neither line.

**Measured against its own baseline on each lane, which is the only way
"pre-existing" was said rather than assumed.** Same filter, same box, plug
rebuilt from depot source for the baseline arm:

| lane | baseline | with the fix |
|---|---|---|
| riscv64 | 14 pass / 6 fail | **15 / 5** |
| arm64 | 17 pass / 3 fail | **18 / 2** |

The single delta on each lane is `real-approx-modes`; every other failing row
is byte-identical across the pair. `real-saturating`, `real-trapping` and
`unit-real-*` pass in both arms, which is what says the f64 paths this also
touched did not move.

**TWO WAYS `build/test-cross-batch.ps1` HANDS OUT A FALSE GREEN, both met
here.** It exits **0 when the filter matches nothing** -- `-Filter 'ops/real'`
answers `0 eligible` and exit 0, because the filter matches the BARE test
name and not the path. And it exits **0 with failures present**: the run that
reported `FAIL: 5` also exited 0. Read the banner; the exit code carries no
verdict either way.

**Still failing on these lanes and NOT this row's work** (verified present in
both baseline and fixed arms): `real-approx-equality` (both lanes, f32 signed
zero), `real-mode-fields` (both lanes, no uart), and on riscv only
`real-cert`, `real-compare-negative` (starved) and `real-mode-compare`
(`approx neg lt neg` answers 00 for 11). The last is a real comparison defect
and wants its own row.

`IrMulRealApprox` goes to `fmul-s` on both lanes and `IrMulRealSaturating`
goes to `fmul-d`, because the emitters dispatch on the OPERATION and never on
the width. `Real approximate saturating` is f32, so its operands are run
through f64 arithmetic and then through an f64 exponent read:
`rv-emit-real-saturating-arith` does `slli 1`, `srli 53`, `addi -2047`, which
is the double layout. A single needs `shl 33`, `shr 56`, against 255.

`codex/test/ops/real-approx-modes` was written for exactly this and says so in
its own prose: "Run the f32 values through the f64 constants and the exponent
read is nonsense, the guard never fires, and saturating quietly stops
saturating." It is FAILING on both cross lanes now, and the two answers differ
because each lane's wrong arithmetic is wrong differently:

| lane | `sat mul overflow` | expected |
|---|---|---|
| riscv64 | 0 | 2139095039 |
| arm64 | 1073741824 | 2139095039 |

2139095039 is `0x7F7FFFFF`, the largest finite single, which is what the clamp
is supposed to produce. x86-64 passes the test, so the f64 path in these
emitters is right and only the f32 path is wrong.

**RE-MEASURED 2026-08-18 (reek), and the table above is incomplete in one
direction and unexplained in another.** Run directly, `build/test-cross.ps1
-Arch <a> -Test ops/real-approx-modes`:

| lane | line 2 `sat mul overflow` | line 3 `sat add overflow` |
|---|---|---|
| expected | 2139095039 | 2139095039 |
| riscv64 | 0 | **4278190078** |
| arm64 | 1073741824 | **passes** |

**riscv64 fails TWO lines, not one, and the second decodes the mechanism.**
4278190078 is `0xFEFFFFFE`, which is `0x7F7FFFFF` doubled exactly. An f32
bit pattern reinterpreted as an f64 is a tiny denormal, and denormal plus
denormal doubles the pattern exactly; denormal times denormal underflows,
which is line 2's `0`. **So the arithmetic is wrong BEFORE the guard is ever
consulted.** This row reads as a clamp that never fires, and that is the
second-order half. Swapping in the single-width exponent constants alone will
not fix either line: the FP ops and the register moves have to change with
them (on riscv, `fmv-w-x`/`fmv-x-w` in place of `fmv-d-x`/`fmv-x-d`).

**arm64 line 3 PASSES and nothing here explains why.** The same width-blind
path that produces 1073741824 on line 2 should produce 4278190078 on line 3 as
riscv does, and it does not. **Do not read that pass as evidence the add path
is right, and do not validate an arm64 fix on line 3.** Whatever makes it green
is not understood, so it is as likely to be an accident as a correctness.

**Trapping has the same width blindness with no exponent read at all**, so it
is not covered by this test and no arm currently fails for it. On riscv the
Trapping arms call `rv-emit-real-arith` with the `-d` ops, so an f32
trapping op runs the same denormal arithmetic; the fix collapses to reusing the
existing `rv-emit-real-approx-arith`.

**The type is already in hand at the dispatch.** `ty` is the fifth parameter
of `rv-emit-binary-reg` (and the arm64 twin), and the vector arms beside these
already branch on it with `rv-is-vec-f32`; `is-real-f32` is the scalar
predicate and `RiscVCodeGen.codex:1426` already calls it. Both shift encoders
mask the amount to six bits, so 33, 41 and 56 encode without a new instruction.

The fix is a width-aware saturating and trapping emitter on each lane: pick
`fmul-s`/`fadd-s` and the single-width exponent constants when the type is
f32, the existing double ones otherwise. Both plugs need it and both have the
same shape, so it is one job done twice, not two investigations.

**Recorded 2026-08-18 (fester) while closing 1.42.** It was mistaken for a
consequence of the unresolved `to-real-approx-saturating` call in this
register and in `CrossLaneFilesystem.md`; both are corrected. That conversion
is the identity on x86 (`emit-to-real-saturating-builtin` returns its
argument's register), so its unresolved call changes no answer.
## 1.46 -- match guards are dropped by every source-emitting plug, and the oracle subject now asks

Found 2026-08-19 by Steve Howell (GitHub issue 72) on the zig plug and fixed
there the same day (a guarded match emits as a labeled block of if-statements;
guardless matches are byte-identical to before). `IRBranch` carries
`guard : IRExpr`, `IrBoolLit True` when the arm has none; the machine-code
plugs read it (arm64, riscv, t3isa) and NO text plug does: grep
`codex/plugs/*/*.codex` for `.guard` and the three native lanes are the only
hits. A guarded arm becomes a bare switch prong or if-branch and fires whenever
its pattern matches.

`codex/test/plug-oracle-arith.codex` carries a "Match guards" section now
(`classify`, `band`: two guarded arms on one constructor, a guard on a
catch-all, a guarded tuple payload; rows 41-49 of the truth). Measured through
`build/plug-oracle-test.ps1` on seed `800A7683`, plug binaries rebuilt first:

| arm | before the rows | with the rows |
|---|---|---|
| zig | PASS 40 | PASS 49 (fixed) |
| python | PASS 40 | FAIL, rows 41/43/45/49 answer 1/1/4/1 for 3/2/7/2: guards dropped, WRONG VALUE, no refusal |
| wasm | STALE | FAIL, the same four rows the same way |
| csharp | PASS 40 | FAIL, the emitted program does not build: CS8510 "pattern is unreachable" three times on the switch expression |
| typescript | PASS 40 | FAIL, the emitted program does not compile, and it is TWO defects: the guards are dropped (`else if (true)` for a guarded catch-all), AND `type Val = { _tag: "Num" } \| { _tag: "Pair" } \| { _tag: "Nil" }never;` -- the first VARIANT type this subject has ever carried, and the emitter writes `never` with no separator after the last alternative |

Python and wasm are the shape the issue names, a wrong answer with no refusal;
csharp and typescript happen to refuse only because two arms share a
constructor. The remaining ~40 text plugs are not wired to the oracle and are
presumed to share the drop; the zig fix is the worked shape (an if-chain with
bindings before the guard, and `unreachable` after a non-catch-all tail).

The typescript `never` is separate from guards and would have failed on any
variant type; it is recorded here because the subject found it and nothing else
has a row with a variant in it.

## 1.45 -- riscv answers False for `approx neg lt neg`, and it is the f32 COMPARISON path, not the arithmetic one

Found 2026-08-18 (reek) while baselining 1.44, and separated from it on
purpose: 1.44 was the saturating and trapping ARITHMETIC arms, this is the
approximate COMPARISON arm, and fixing the first did not touch it.

`codex/test/ops/real-mode-compare` line 4:

| lane | `approx neg lt neg want 11` |
|---|---|
| expected | 11 |
| riscv64 | **00** |
| arm64 | passes |

**RISCV ONLY.** arm64 answers 11 and x86 is the truth the row is written
against, so this is one lane's lowering rather than a shared one. Both digits
are wrong together, which says it is the comparison itself and not the fused
or value-position wrapper: line 53 prints `approx-fused` and `approx-val`
side by side, they take different paths to the same predicate
(`if a < b then 1 else 0` against `b2i (a < b)`), and both answer 0.

The operands are `to-real-approx neg-big` and `to-real-approx neg-small`,
that is -2.9 and -1.5 as f32, so the case is two NEGATIVE singles. The plain,
trapping and saturating rows of the same test pass on the same lane with the
same values, and those go through `rv-emit-real-comparison` with
`rv-flt-d`; the approximate row goes through
`rv-emit-real-approx-comparison` with `rv-flt-s`. That pair is the whole
of the difference and is where to start.

**Not diagnosed further and not attempted.** It is recorded because it was
measured in both the baseline and the fixed arm of 1.44's comparison, so it is
pre-existing rather than anything 1.44 did, and because a defect found inside
a closed entry's evidence is lost the moment that entry is read as done.

## 1.20 -- pascal is code-complete and has never been compiled: the next step is fpc

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

**THE HOISTING GAP IS CLOSED (reek, main 16460), and it was bigger than this
row said.** `emit-pas-expr-at` is replaced by `pas-h`, which answers a triple
of (statements, expression, next temporary) and is threaded through every
arm, so a binding, a conditional, a match or an act can appear anywhere an
expression can and its statements land in the enclosing block. That closed
four shapes at once, not the two the row named, and the third one is the
reason the work was worth more than it looked:

**`IfThen` is a FUNCTION.** `emit-pas-expr-at` lowered
every `IrIf` to `IfThen(c, t, e)`, and Free Pascal
evaluated BOTH arms before calling it. A recursive Codex definition was
therefore non-terminating through this plug:

    pr-count (n) = if n <= 0 then 0 else 1 + pr-count (n - 1)
    function pr_count(n: Variant): Variant;
    begin
      Result := IfThen((n <= 0), 0, (1 + pr_count((n - 1))));
    end;

`emit-pas-match` had the same shape, a chain of nested `IfThen`, so every
branch of every match ran too. Both wanted the same thing: an
`if/then/else` STATEMENT assigning to a temporary. `pr_count` now emits that
and recurses only in the else branch, and `IfThen(` appears ZERO times in
the output of every subject tried (`plug-oracle-arith`, the 1.36 text probe,
`hello`, `record`, and both probes here).

The two shapes the row originally named emitted code that does not COMPILE
rather than code that answers wrongly:

    ph-binary (n) = (let a = n + 1 in a * 2) + (let b = n + 3 in b * 10)
    Result := ((a * 2) + (b * 10));    { before: a and b never declared }
    a := (n + 1); b := (n + 3);        { after }
    Result := ((a * 2) + (b * 10));

All five probe shapes now declare and assign every name they read and
hand-trace to the x86-64 answers (80, 18, 100, 16, 15).

**Two design points, because they are not re-derivable from the result.**
The temporary counter is THREADED rather than global, because the `var`
block must declare every temporary and the count is only known after the
body is emitted, so `emit-pas-def` emits first and builds the declarations
from what came back. And `pas-collect-binds` now walks the WHOLE expression
rather than the top-level let-chain, because a binding hoisted out of an
operand is assigned in the same block as one that was already there.

There is still no Free Pascal on this box, so the above is hand-traced
rather than run.

**THE FOUR REMAINING GAPS ARE CLOSED (reek, 2026-08-17: 16535, 16539,
16543), and one of the four was named wrong.** The row said `gauge:g` was
"a bounded-field read as a bare name". It was not: `IrRecord` emits a call
to `MakeX` and **nothing in the plug defined a record constructor**, so
`MakeGauge` and `MakeCell` were both undefined and the clamping band on `g`
was never applied at all. What closed:

- **A record is now a variant array** (16535). That is what lets it live in
  the `Variant` locals this plug declares; a Pascal `record` cannot. A field
  is an index and the index is already on the wire, since the IR spells an
  access or a store as `name/slot`. Clamping is honoured in the constructor,
  the one place every construction passes through.
- **A construction's field values arrive in SOURCE order, not declaration
  order.** Measured through this plug: `Trio { cc = n + 2, aa = n, bb = n +
  1 }` emitted `MakeTrio((n + 2), n, (n + 1))`. A positional constructor
  therefore has to be permuted against the type defs, which the plug
  receives and had not been reading. Values are hoisted in source order and
  only the finished texts permuted, so evaluation order is unchanged.
- **A list runtime** (16539): five named helpers, no inline library call.
  pascal is one of the eight plugs 1.7 names with no `list-snoc` of any
  kind, and `IrAppendList`/`IrConsList` were emitting `ConcatArrays` and
  `ConsArray`, undefined too. **Two further defects the same measurement
  turned up and the row had not named: `list-at` emitted `a[i + 1]`**, the
  1-based convention Pascal STRINGS use applied to a 0-based `VarArrayOf`,
  so `list-at [10,20,30,40] 2` answered 40 where Codex answers 30, silently,
  on every list read this plug ever emitted; and `list-length` emitted
  `Length(a)`, which counts a string or a dynamic array and cannot count a
  variant array's elements.
- **`IrFieldStore`** (16543) emitted the literal `"0"`, so the store was
  ABSENT rather than wrong and left a plausible integer behind. It is now a
  statement in the hoisted triple, answering the field read back rather than
  the stored expression so no value is evaluated twice.

**The index-dispatch trap was handled before it fired.** `pas-builtin-text`
dispatches on an INDEX into `pas-builtin-names` and ended in a bare `else`
handling the last name implicitly, so appending `list-push` and `list-snoc`
would have made the fallback emit `text_to_integer(a, v)` -- worse than the
undefined call it replaced. The last real branch is explicit now and the
final `else` emits an identifier no Pascal unit defines.

**NEXT STEP: install fpc, run `plug-oracle-arith`.** Damian ruled 2026-08-17
that no new build environment goes on this box for now, so all of the above
is read against the language and hand-traced, and **nothing in this plug has
ever been compiled by a Pascal compiler**. Free Pascal 3.2.2 is available
from winget (`FreePascal.FreePascalCompiler`) when that changes. What the
inspection does establish: the scan of the emitted `plug-oracle-arith` for
call targets that are neither defined in the file nor Pascal RTL is now
EMPTY, where before these three CLs it held `MakeCell`, `MakeGauge`,
`list_push` and `list_snoc`; the wider scan for identifiers read but never
declared leaves only `varVariant` and `rfReplaceAll`, both RTL constants
from units already in the uses clause; and the complete diff of emitted
output across the three CLs is thirteen lines, every one of them a line that
was wrong, with every arithmetic and recursion function byte identical to
before.

**Still open in this plug, and NOT part of these four.** The record type
declarations `emit-pas-type-def` emits are now declarative only, since a
record is a variant array; they are kept because dropping them can leave an
empty `type` block ahead of the helpers' own.

**VARIANTS ARE CLOSED (reek, 2026-08-18), and construction was the smaller
half of it.** Measured through this plug on a probe of seven shapes, `area`
came back as this:

    if s.tag = 'Empty' then ... _t0 := ((3 * r) * r);

Four defects in one nine-line function. `Circle(n)` and `Empty` were calls to
constructors nothing defined, the same shape as the `MakeX` record gap.
`s.tag` selected a field on a `Variant`, which has no fields at all, so the
scrutinee test could not compile either. **`r`, `w` and `h` were never
declared and never assigned**: `pas-collect-branch-binds` walked the branch
BODY and no arm of the emitter ever looked at a pattern's sub-patterns, so
every field a `when` bound read back as an Unassigned Variant. And a variant
type declaration collided by name with its own constructor whenever a source
writes `Wrap = | Wrap (Integer)`, which is the ordinary single-constructor
shape.

A variant value is now a variant array like a record, tag in slot 0 and the
fields after it, so a `Variant` local can hold one. A constructor function is
emitted per ctor; the scrutinee is hoisted into its own temporary because
Pascal cannot index a function result; the test is
`VarToStr(_t0[0]) = 'Circle'`; sub-patterns are assigned from `_t0[i + 1]`
ahead of the branch body and are declared in the `var` block. The dead
variant type declarations are gone, which is what closes the name collision,
and the `type` keyword is now emitted only when the block has something in
it.

**A bounded field on a CONSTRUCTOR is not clamped, and that is not the record
rule.** Measured on x86-64 2026-08-18: `Gauge (Integer between 0 and 100
clamping)` constructed as `Gauge 150` and read back through `is Gauge (v) ->
v` answers **150**, where a bounded RECORD field answers 100. The plug
therefore stores a ctor field as given. Copying `pas-emit-ctor`'s
`codex_clamp` into the variant constructor would have made this plug disagree
with x86-64 while looking more careful.

Eleven readings against x86-64 (75, 42, 4, 1, 2, 4, 7, 12, 150, 42, 42) hand
trace correctly through the emitted Pascal. Against `plug-oracle-arith` the
complete diff across this change is the four `Tup2..Tup5` declarations
becoming four `MkTupN` constructor functions and nothing else, so the
arithmetic, record, list, text and TCO halves are byte identical.

**Nested constructor patterns were NOT exercised, because they do not
parse.** `is Box (Circle (r)) ->` is rejected by the compiler with
`CDX2040: Unresolved call to 'r'`, and there is not one of them anywhere in
the compiler's own source. The sub-pattern walk is written recursively, so it
would carry them, but only the one-level shape is measured.

Booleans are `IrLitPat`, not `IrCtorPat` (measured: `when b is True` emits
`b = True`), so no constructor-table lookup was needed to keep them out of
the tag test.
`text-length` emits `Length(a)` on a Variant, which is the same doubt the
list count had and is 1.36's ground, not this row's.

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

- **`fortran` is a CAMPAIGN, ruled in by Damian 2026-08-17 (via red), and
  stage 1 has landed.** The goal is a fortran plug that works end to end and
  handles every case the other source plugs do, good enough to DDC through.
  Staged, one CL per stage, and the stage list is below the account.

  The row said `fort_list_snoc` was emitted and never defined. True, but it
  was one of **fifteen** `fort_*` helpers the
  emitter emits and **zero** it defined: `fort_file_exists`,
  `fort_integer_to_text`, `fort_list_concat`, `fort_list_cons`,
  `fort_list_insert_at`, `fort_list_set_at`, `fort_list_snoc`,
  `fort_open_file`, `fort_read_file`, `fort_text_compare`,
  `fort_text_concat_list`, `fort_text_replace`, `fort_text_split`,
  `fort_text_to_integer`, `fort_write_file`. There was no prelude anywhere in
  the plug: `emit-fortran-chapter` emitted a module of the chapter's own defs
  and a program, and nothing else. Run against
  `codex/test/plug-oracle-arith.codex` the emitted `.f90` called
  `fort_integer_to_text` 33 times and `fort_list_snoc` 3 times and defined
  neither, so adding only the `list-snoc` body would have left the output
  exactly as uncompilable as it was. **Stage 1 below closed this half.**

  **Two more defects are pure emission syntax and independent of any helper.**
  A list literal emits a Fortran array constructor and is then indexed
  directly -- `lit_at = (/ 10_8, 20_8, 30_8, 40_8 /)(int(i) + 1)` -- which is
  not legal Fortran; and a function result is indexed in the same way,
  `push_at = fort_list_snoc((/ 10_8, 20_8 /), n)(int(2_8) + 1)`. Both need a
  temporary, and the plug has no hoisting machinery, which is the same gap
  1.20 names for `pascal` and notes `fortran` shares.

  **The root is the type mapping.** `fort-type` answers `integer(8)` for
  `ListTy` and `LinkedListTy` (`FortranEmitter.codex:120-121`), so a list
  parameter is DECLARED a scalar while list literals are PASSED as array
  constructors, and `list-length` emits `size()` over the scalar. Nothing
  here is fixable while that holds. Settling it also forces the function
  form: every def is emitted in the old-style result-type-prefix shape
  (`integer(8) function name(args)`, `:466` and `:560`), which cannot
  express an array result at all, so list-returning functions need the
  `function name(...) result(r)` form with a separate declaration.

  **There is no Fortran toolchain on this box** (re-measured 2026-08-17:
  `gfortran` and `flang` absent), so as with `pascal` in
  1.20 nothing here can be compiled and the work is reviewed by reading.

  **STAGE 1 IS DONE (val, 2026-08-17).** `fort-prelude` emits a
  `codex_fort_prelude` module ahead of the chapter defining all fifteen
  helpers; the chapter module and the program both `use` it. 8,343 bytes of
  Fortran in 22 literals, generated mechanically from a draft rather than
  hand transcribed, and refused if the draft held a quote or a backslash.
  Lists are rank-1 arrays with `allocatable` results, which is the contract
  the emitter was ALREADY assuming at the value level, and generic
  interfaces cover `integer(8)` and `character(len=256)` element kinds for
  concat/cons/snoc/set_at/insert_at.

  **`fort_list_cons` takes (element, list) and that was verified, not
  assumed**: ten other emitters agree `IrConsList` passes the element left.

  **The oracle, and its limit.** `test-plugs.ps1` already refuses a plug that
  emits `__narrow` with no definition; the same rule generalised to `fort_*`
  is the check. Over five inputs against the landed seed, every `fort_`
  called is defined, 0 undefined, all 15 present. **The control arm is kept
  and it fails**: strip the prelude module from the same output and the check
  reports 7 undefined on `builtin-reach` and 1 to 2 on the other four, so it
  can fail on every input rather than only the rich one. **It judges the
  defect stage 1 fixed and NOTHING about whether the Fortran compiles**, and
  `builtin-reach` only REACHES 7 of the 15; the other 8 are defined by
  inspection. Do not read this arm as end to end.

  **Stage order changed from what this row used to say, and the new order is
  the right one.** It said type mapping, then function form, then prelude,
  then hoisting. Damian ruled prelude first, and prelude-first is coherent
  because the prelude is what FIXES the rank-1-array contract that the type
  mapping then has to be made to match. Remaining stages, in order:

  2. **Function form. DONE (val, 2026-08-17), and it was SWAPPED with the
     type mapping deliberately.** This list first had the mapping at 2 and
     the form at 3. Taking the mapping first would splice an array type into
     the old prefix shape and emit
     `integer(8), dimension(:) function name(...)`, which is not legal, so
     the form has to come first; it is also a strict improvement on its own.
     The result type now moves out of the prefix into a declaration in the
     specification part, `integer(8) :: name`, which is legal today and can
     carry `allocatable` and a rank once `fort-type` changes.

     Three arms. **Control**, the stage-1 outputs kept on disk: 32, 1, 2, 4
     and 1 prefix-form function statements across the five inputs.
     **Fixed**: 0 on all five, with 32 functions actually examined, so the
     result-declaration half is not vacuous there (it IS vacuous on the
     control, where nothing matches the bare `function name(` pattern at
     all; say so rather than read two zeroes as agreement). **Sabotage**,
     one result declaration deleted: reports it and names `br_abs`.

  3. **Type mapping. DONE (val, 2026-08-17).** `fort-type` answered
     `integer(8)` for `ListTy` and `LinkedListTy`, so a list was DECLARED
     scalar while literals were passed as array constructors, and it
     discarded the element type so `List Text` and `List Integer` were
     indistinguishable. It now answers the ELEMENT type and **rank moves to
     the declaration site**, which is where it has to live: a list dummy is
     `<elem>, dimension(:), intent(in)` and a list result or local is
     `<elem>, allocatable :: n(:)`. Those are different syntaxes, so one
     type text cannot serve both and this needed declaration helpers rather
     than a one-line change.

     **The operand was chosen so the two candidate answers disagree.**
     `br-text-concat-list : List Text -> Text` went from
     `integer(8), intent(in) :: xs` to
     `character(len=256), dimension(:), intent(in) :: xs`, which separates
     "element type preserved" from "rank added to everything"; `br-list-at`'s
     scalar `i` stayed scalar in the same function, and `lambda_map`'s four
     parameters split correctly into two lists and two scalars. Across the
     five stock inputs, list-shaped declarations go 0 to 9.

     **`names : List Text -> List Text` emits
     `names = fort_list_snoc(xs, 'tail')` against
     `character(len=256), dimension(:)` operands, so the prelude's generic
     interface now RESOLVES to its `_t` variant.** That is the point of
     preserving the element type and it is the first thing in this plug that
     needed stages 1 and 3 together.

     **Neither the allocatable branch nor the TCO path is reachable from
     `codex/plugs/test-input/`**, so both were exercised with written
     probes: a list-returning function, a tail-recursive function with a
     list accumulator, and a text-list function. Reading the check's output
     rather than its exit code caught a defect IN THE CHECK, which required
     the declaration to end at the bare name and so scored every correct
     `:: grow(:)` as undeclared. Widening it did not blind it: the sabotage
     arm still fails and still names `br_abs`.

     Known limits, all pre-existing and none closed here. A nested
     `List (List Integer)` collapses to the inner element at rank 1.
     `emit-fort-record-fields` still hardcodes `integer(8)`, so a record
     field holding a list or a text is wrong. `FunTy` is still `integer(8)`,
     which is why `lambda_map`'s `f_in` is an integer.
  4. **Hoisting. DONE (val, 2026-08-17) and it did NOT need hoisting.**
     This entry said the plug has no machinery for it and that both cases
     need a temporary. They do not. The defect is narrower than "hoisting":
     four builtins subscripted their first argument directly, which is only
     legal when that argument is a NAME, and **passing an expression to a
     helper is legal exactly where subscripting it is not.** So `list-at`,
     `char-at`, `char-code-at` and `substring` became `fort_list_at`,
     `fort_char_code_at` and `fort_substr` in the prelude, `fort_list_at`
     generic over the two element kinds like its neighbours. The helper
     bodies keep the original expressions exactly, bounds behaviour
     included, so the FORM changed and the semantics did not.

     **A measured control, not an argued one.** The previous emitter
     revision was rebuilt from the depot and the same probe run through it,
     and it reproduced the two strings this row recorded, character for
     character:

     ```
     lit_at  = (/ 10_8, 20_8, 30_8, 40_8 /)(int(i) + 1)
     push_at = fort_list_snoc((/ 10_8, 20_8 /), n)(int(2_8) + 1)
     ```

     against the fixed arm's `fort_list_at((/ ... /), i)` and
     `fort_list_at(fort_list_snoc((/ ... /), n), 2_8)`. The text pair went
     the same way. Direct subscripts of an expression: **3 to 0.** The
     probe is a written chapter; nothing in `codex/plugs/test-input/`
     produces any of these forms, because every stock call passes a bare
     name.

     **One residual, and it is NOT this defect.** `lambda` still emits
     `(x + 100_8)(5_8)` for `(\x -> x + 100) 5`. Measured across every
     stage of this campaign it is unchanged at 1, because it is the LAMBDA
     hole rather than a subscript: `fort-emit-expr` answers `IrLambda` with
     its BODY, discarding the parameter binding, and the apply then wraps
     the result as if it were a function. `FunTy` is `integer(8)` for the
     same reason. That wants its own stage and has not been started.
  5. **Statements in expression position. THE EFFECTFUL-DEF HALF IS DONE
     (val, 2026-08-17); the builtin half is still open.**

     Done: `fort-is-effectful` now sees an effectful def with parameters.
     **The first attempt was wrong and is worth the next reader's minute.**
     I added a `FunTy` arm recursing into the RESULT type, rebuilt, and the
     output was unchanged, because a Codex function does not carry its
     effects on the result: `FunTy (p) (row) (r)` carries them in the
     EFFECT ROW, as `IRTextParser.codex:304` builds and
     `PlugManifest.codex:33` already reads. The test is
     `list-length (fnrow.labels) > 0`, taken from that working idiom rather
     than guessed a second time.

     Detection alone would have swapped one invalid form for another, so
     the same stage emits the call. `ArityEntry` gained `is-proc`, set from
     the def's own type, and an act statement whose callee resolves to a
     procedure emits `call name(args)`. The misplaced `implicit none` went
     with it, now that the code is reachable.

     Measured on the probe, all three symptoms at once:
     `shout = print *, trim(msg)` and a bare `shout('probe')` became a
     `subroutine shout` holding a plain `print *, trim(msg)` and a
     `call shout('probe')`. **Containment control: all seven other outputs
     are BYTE-IDENTICAL to stage 4**, which also says plainly that nothing
     in `codex/plugs/test-input/` exercises this stage at all.

     Still open. The BUILTINS are unchanged: `print-line-uni` emits
     `print *, ...`, `process-exit` emits `stop`, `close-file` emits
     `close(...)`, and `fort_open_file`/`fort_write_file` are called for
     effect, all through `fort-emit-expr`, so any use in a value position
     is still a syntax error. And an effectful def used in a VALUE position
     (`x <- shout msg`) now emits `x = shout(msg)`, calling a subroutine as
     a function; that shape was already wrong and this did not fix it.
  6. **Array constructor type specifications. DONE (val, 2026-08-17), and
     it was found by AUDITING THE EMITTED OUTPUT rather than by guessing
     the next stage.** Two illegal forms, both closed by one change.

     An empty list emitted `(/ /)`, a constructor with no values and no
     type, which Fortran cannot type and rejects; it comes from a literal
     such as `build 3 []`. A text list emitted `(/ 'a', 'b' /)`, whose
     elements have character length 1, and passed it to the
     `character(len=256), dimension(:)` dummy STAGE 3 ITSELF NOW DECLARES,
     which is a length mismatch against an explicit-length dummy. **The
     second defect was created by stage 3 and would not have been visible
     without reading the output**, which is the argument for auditing after
     a type change rather than only checking the thing you changed.

     `fort-emit-list` now emits the F2003 type specification, from the type
     it was already handed and discarding: `(/ integer(8) :: 1_8, 2_8 /)`,
     `(/ character(len=256) :: 'a', 'b' /)`, `(/ integer(8) :: /)`. The
     type-spec form pads each character value to the declared length, which
     is what the dummy wants. Control is the previous stage's outputs kept
     on disk: untyped empty constructors 1 to 0, untyped text literals 2 to
     0, typed constructors 0 to 10.

  7. **LIFTING IS DONE for a lambda that is CALLED (reek, 2026-08-18); a
     lambda used as a VALUE still refuses, and that is the honest half.**
     A called lambda becomes a module procedure taking its own parameters
     then every captured variable, and the call site passes the captures.
     Measured on `docs/Probes/plug-lambda-subject.codex`, built for this and
     verified against x86-64 (`105 21 17 42 7`):

         immediate_r      = cx_lam_615444021(n)
         captures_local_r = cx_lam_475348430(7_8, c)
         captures_param_r = cx_lam_475348244(1_8, n)

     with `cx_lam_475348430(x, c)` and `cx_lam_475348244(x, n)` declaring the
     capture as a second dummy. Those three hand-trace to 105, 17 and 42.
     **Refusal sites went 5 to 2** and `plug-oracle-arith` is byte identical,
     which it must be: it holds zero lambda nodes, measured with `-IrUni`.

     **Two shapes still refuse, and lifting cannot close either.** A
     let-bound lambda (`let f = \x -> x * 3 in f n + ...`) and one passed to
     a higher-order definition both put a procedure where `fort-type` maps
     `FunTy` to `integer(8)`, so `f = <procedure>` assigns into an integer
     and `apply_twice` receives `integer(8), intent(in) :: f` and calls it.
     Closing them wants `procedure(iface)` dummies with an abstract
     interface, and for a CAPTURING lambda a closure record of the kind
     `zig` carries as `CxFn1`..`CxFn4`. The marker is now
     `fort_lambda_value_unsupported`, so the same undefined-call scan still
     names them.

     **Two measurements were wrong before they were right, and both are the
     kind that ship.** The lambda was first keyed by its span, and **every
     `IrLambda` reaching this plug carries `sp.start.offset` of zero**: all
     five probe lambdas became `cx_lam_0` and the emitter wrote five
     procedures with one name. The key is a hash of the lambda's own emitted
     text now, so two structurally identical lambdas share one procedure,
     which is right rather than a collision. And the capture set was first
     computed against the ENCLOSING bound names, so `c` in
     `let c = n * 2 in (\x -> x + c) 7` read as bound and was not captured;
     captures are the free names against the lambda's OWN parameters, and
     the enclosing scope does not enter it.

     **A lifted procedure is emitted even for a lambda that only ever
     refuses**, so two of the five in the probe are unused. Fortran permits
     an unused module procedure and they become live when the value case
     closes.

     The account of the original defect follows, unchanged.

     **The lambda hole as it was: no lifting, but no longer
     SILENT, and with a runner (val, 2026-08-18).**
     `fort-emit-expr` answered `IrLambda` with its BODY and discarded the
     parameter binding, so `(\x -> x + 100) 5` emitted `(x + 100_8)(5_8)`:
     `x` unbound and the result applied as though it were a function.
     `FunTy` is `integer(8)` for the same reason, which is why
     `lambda_map`'s `f_in` is an integer.

     **What changed is only the failure mode, and that is the point.** The
     arm now emits `fort_lambda_unsupported(<body>)`, a name no Fortran
     unit defines, so the same call-targets-are-defined scan stage 1
     already runs answers 1 undefined on a subject with a lambda and 0 on
     one without. **The gap had prose and no runner; now it has a runner.**
     That is the L-CAPABILITY shape handled deliberately: a plug that
     cannot do a thing must not report the same as one that can.

     **Two arms, and the control is the half that matters.** A probe with
     three lambda shapes (immediate application, a let-bound lambda
     applied twice, a lambda capturing a local) emits the marker at all
     three sites. `plug-oracle-arith` is **byte-identical** before and
     after, SHA-256 `B0AA798F...`, 0 markers, so nothing in service moved.

     **NO CURRENT SUBJECT EXERCISES THIS, measured 2026-08-18, and the
     first two measurements of it were WRONG.** Both
     `plug-oracle-arith` and `builtin-reach` contain zero `(lambda`
     nodes; the probe contains three, which is what makes the zero
     readable rather than an instrument failing silently. **Inspecting
     plug IR has a trap that cost two invalid readings here: `-IrCce`
     writes CCE, which is not ASCII, so grepping it for `lambda` cannot
     match; and plain `-Out` without an IR switch writes a CDX binary.
     `-IrUni` is the readable one, and it writes the IR into the LOG and
     exits 4.** Validate the instrument on a subject known to contain the
     construct before reading any zero.

     **A real fix is lambda LIFTING and it is still a stage in its own
     right.** Fortran has no closures, so each lambda becomes a module
     procedure taking its own parameters plus every captured variable,
     and the call site passes the captures. That needs free-variable
     analysis over 25 `IRExpr` constructors and a stable name per lambda
     that both the collector and the emitter agree on; `zig` solves the
     same problem with `CxFn1`..`CxFn4` closure structs. Do not attempt it
     against a subject with no lambdas: land an arm first, which is what
     blu's Lambdas section for `plug-oracle-arith` is for.

     **`IrLambda` reaches every source plug, and that is a pipeline fact
     worth the whole lane knowing** (measured val, 2026-08-17).
     `text-plug-ir-pipeline` is `["fold-constants"]`, one pass
     (`compiler/IR/Passes.codex:71`), and `run-ir-pass` has **no
     lambda-lifting arm at all** even though `compiler/IR/LambdaLifting.codex`
     exists. So no plug receives lifted lambdas and each one owns the
     problem: `zig` solves it with `CxFn1`..`CxFn4` closure structs in its
     prelude, and `fortran` has no mechanism whatever.

     Fortran has no closures, so a real fix lifts each lambda to a module
     procedure and passes every captured variable as an extra argument,
     which the emitter has no machinery for and which touches the call
     site as well. That is a stage in its own right and is deliberately
     NOT half-done here: emitting the body and dropping the binding is
     precisely the shape L-CAPABILITY warns about, and it is what the plug
     does today.

  8. **Statement position emits STATEMENTS, not `merge()`. DONE (val,
     2026-08-17, `FortranEmitter.codex` at val 16590), and this row's own
     diagnosis was wrong about where the defect was.** It said the subject
     was builtins in expression position. The reachable defect is one level
     up: `fort-emit-statement` had no arm for `IrIf` or `IrMatch`, so a
     control-flow form in STATEMENT position fell through to
     `fort-emit-expr`, and both emit `merge(...)`
     (`fort-emit-match-merge` nests it). `merge` is an intrinsic FUNCTION,
     so its arms must be values. An `if` whose arms are prints emitted

     ```
     merge(print *, trim('positive'), print *, trim('negative'), (n > 0_8))
     ```

     which is what the builtin half of this row was seeing from below.

     **The fix that suggests itself is the wrong one, and the eagerness is
     why.** Making the four statement-shaped builtins into prelude functions
     (`fort_print_line` and friends) would have made that line COMPILE, and
     `merge` evaluates BOTH arms, so it would then have printed `positive`
     and `negative` on every call. That trades a loud syntax error for a
     silent wrong answer. `ada` has the identical hole
     (`AdaEmitter.codex:379-381`, a bare `Put_Line` in value position), so
     there was no idiom to borrow from a working plug.

     `fort-emit-statement` now emits real `if (...) then / else / end if`
     blocks and recurses, mirroring `fort-emit-tco-body` and
     `fort-emit-tco-match-branches` (`FortranEmitter.codex:528, 547`), which
     had been doing exactly this correctly in the same file all along. New
     `fort-emit-stmt-match` does the same for branches. `IrDoExec` routes
     through `fort-emit-statement` rather than duplicating its `otherwise`,
     which also gives a nested `act` inside a `do-exec` its statements back
     (`fort-emit-act-expr` keeps only the LAST one). The `otherwise` arm now
     uses `fort-emit-call-or-expr` instead of `fort-emit-expr`, so a def
     whose whole body is one call to a subroutine gets its `call`.

     **Three arms.** Control is the pre-change output kept on disk, which
     fails on the one true site. Fixed passes. The check is a substring test
     from the first `merge(`, and it had to be corrected: a line-wide match
     scored `print *, trim(... merge(42_8, 0_8, ...))` in `types.f90` as a
     defect when it is correct Fortran, the `print` being the outer
     statement and the `merge` a legitimate value inside it. That is the
     second time this campaign an over-broad check reported a fault in
     working output; read the check's OUTPUT, never only its exit code.

     **All 13 `codex/plugs/test-input/` outputs are byte-identical across
     the change**, measured by rebuilding the depot emitter as a control and
     diffing. The change is inert on every stock input and fires only on the
     path the probe reaches, which is the honest statement of its blast
     radius and also of the coverage gap.

     Two things this did NOT fix, both still open. **`merge` is still eager
     in genuine VALUE position**, so an `if` used as a value evaluates both
     arms; a division by zero or a bounds fault in the untaken arm will
     execute. Fortran has no conditional expression, so a real fix hoists
     to a temporary and an `if` block, which is the same machinery
     1.20 wants for `pascal` and which stage 7's hoist probe already
     covers. And a statement-shaped builtin in a TRUE value position -- as
     an argument, or bound by a `let` -- is still a syntax error; that is
     the residue of this row's original wording and it is much narrower
     than it looked, because control flow was supplying nearly all of it.

     `IrLet` in statement position still drops the binding
     (`fort-emit-expr` answers it with its body), which needs a declaration
     for the bound name and is stage 9's neighbour, not this one's.

     **This entry was written once, submitted at val 16590, and then
     DESTROYED by a bare `p4 resolve -at` in the merge-down that followed
     (val 16591), which is P-BULKAT in `PerforceProcess.md` happening with
     the trap index open in the same session.** The code survived because
     `FortranEmitter.codex` was not in that merge; only this file was, and
     accept-theirs put main's copy back, restoring the stale "NOT STARTED"
     wording. The tell the doc names is exact and was available: the file
     appears in your own recent submit. Partition with `p4 resolve -as` and
     use `-am` on what it skips, or name the files `-at` is for.

  9. **A function body's control flow assigns to the result. DONE (val,
     2026-08-17, main 16615).** `fort-emit-function` emitted the whole body
     as one expression, so a function whose body is an `if` became
     `ping = merge(0_8, pong((n - 1_8)), (n <= 0_8))`. `merge` evaluates
     BOTH arms, so **every recursive function written as an `if` recursed
     unconditionally and never terminated**: `ping`, `pong` and `sum-to` in
     the oracle, and `join` in `test-input/recurse.codex`, where `merge`
     was also a character-length mismatch between `'end'` and a 256-char
     result. New `fort-emit-assign` emits real `if/then/else` assigning to
     the result variable; it is stage 8 one level up, and the same shape
     `fort-emit-tco-def` always used, which is why `down` was already right
     and `ping` was not. `merge(` across the 13 test-input outputs falls
     **39 to 17**; the remaining 17 are value-position ifs nested inside
     larger expressions and are still open.

  10. **`let` bindings are declared and assigned; field stores mutate. DONE
     (val, 2026-08-17, val 16616).** `fort-emit-expr` answered `IrLet` with
     its BODY, dropping name and value, and answered `IrFieldStore` with
     `"0"`. The oracle's three mutable-record rows came out as
     `store_one = c%ca` with `c` undeclared and both stores gone: it does
     not compile, and if it did it would answer the value the field held
     BEFORE the store, which is exactly what that oracle section exists to
     catch. The IR is nested lets with the store bound to a
     compiler-generated `__seq` that is never read, so a let whose value is
     a field store emits the store as a statement and drops the invented
     binding. Bindings are collected up front and declared before the first
     executable statement, walking into `if` and `match` branches because
     procedure scope is the only scope Fortran offers here. **The collector
     is SEEDED with the parameters and the result name carrying empty
     text**, which is what stops a `let` shadowing a parameter from
     declaring a name Fortran already has.

     The three stock inputs that exercise records pass in BOTH arms: this
     defect was only ever reachable through the oracle.

  11. **Record construction goes through a generated constructor. DONE
     (val, 2026-08-17, val 16617).** The oracle's `Gauge` field is
     `Integer between -100 and 100 clamping` and the plug emitted
     `Gauge(n)%g`, storing what it was given, so `gauge 150` answered 150
     where the answer is 100. The band lives only in the type definition
     and never reaches expression emission, and a Fortran structure
     constructor cannot run code, so each record now emits a `mk_`
     function that applies the band on the way in. Building it where the
     type definitions are already in hand avoids threading a record table
     through every expression emitter, which is what `pascal` had to do.

     **The call uses component KEYWORDS, and that is not decoration.** The
     IR carries a construction's values in the order the SOURCE wrote them,
     not the declared order (measured through `pascal`, 2026-08-17), so a
     positional constructor mis-assigns every literal that reorders its
     fields, with no diagnostic. `mk_Cell(ca = 0_8, cb = 0_8)` cannot have
     that defect rather than having it fixed later.

  12. **Record field types. DONE (val, 2026-08-18).**
     `emit-fort-record-fields` and `fort-ctor-decls` both hardcoded
     `integer(8)`, so any field holding a text, a boolean, a list or
     another record was declared wrong. Both now take the field's own
     declared type.

     **`fort-type` could not serve, and that is the whole reason this
     needed new code.** It maps a `CodexType`, the RESOLVED type; a record
     field carries an `ATypeExpr`, the syntax the chapter wrote. The new
     `fort-atype` maps the latter, with `fort-atype-name` for the atoms
     and `fort-atype-is-list` deciding `allocatable :: f(:)` in the type
     block and `dimension(:), intent(in)` in the constructor. `ada`'s
     `emit-ada-atype-expr` is the same shape and was the model.

     **The row's own named case, checked rather than assumed.**
     `MqttConnectConfig` now declares
     `character(len=256) :: client_id`, `logical :: clean_start` and
     `type(Maybe) :: username`, where every one of those was `integer(8)`.

     **Three arms, and the control is a row the defect cannot move.** A
     probe covering Integer, Text, Boolean, `List Integer`, `List Text`
     and a nested record declares each correctly, constructor included.
     `plug-oracle-arith` is **byte-identical**, SHA-256 `B0AA798F...`,
     which is the right answer rather than a vacuous one: it DOES emit
     records (`Gauge`, `Cell`) and every field of them really is an
     integer, so the path is exercised and correctly stands still.
     Across all three subjects the scans answer 0 undefined calls and
     **0 undeclared types**, the second being the one that matters here,
     because widening a field type is exactly how a reference to a type
     nothing declares would get introduced.

  14. **DONE (reek, 2026-08-18), and the shared slot was the SMALLEST of four
     defects.** The row named the type: `field0..fieldN` numbered to the
     widest constructor, one slot shared by constructors giving it different
     types, so no single declared type is right. That much was correct, and
     the representation is now a component per constructor,
     `<Ctor>_f<i>`, each carrying its own type. Standard Fortran has no
     union to do better with: `equivalence` cannot overlay derived-type
     components and cannot carry an allocatable.

     **But the components were declared and read by NOTHING, so the wrong
     type could not be observed.** Measured through the plug on a probe
     whose `Shape` has `Empty`, `Circle (Integer)`, `Named (Text)` and
     `Rect (Integer) (Integer)`:

         if (s%tag == TAG_Circle) then
           area_r = ((3_8 * r) * r)

     `Circle(n)`, `Named(t)`, `Rect(a, b)` and `Empty` were calls to
     constructors nothing defined, and `r`, `t`, `w`, `h` were never
     declared and never assigned. The module carries `implicit none`, so
     that emitted Fortran did not compile at all: the shared slot was a
     wrong type on a component no code touched.

     What closed. A constructor function per ctor, keeping the ctor's own
     name because that is what the call site already emits. Pattern
     sub-patterns assigned from `s%<Ctor>_f<i>` ahead of each branch body in
     all three statement-shaped match emitters, and declared from
     `IrVarPat`'s own `CodexType`, so no type-def lookup was needed. A
     nullary ctor now emits `Empty()`: ctors go into the arity map, which
     never saw one because a ctor is not an `IRDef`, and Fortran reads a
     bare `Empty` as a procedure reference rather than a call.

     **A derived type now carries a `cx_` prefix, and that was forced.**
     `Wrap = | Wrap (Integer)` is the commonest variant shape there is, and
     Fortran forbids one name being both a derived type and a function in a
     scoping unit. Measured before the prefix: `type :: Wrap` and
     `function Wrap` in the same module. The ctor cannot move, since the
     call site emits a plain name and nothing there knows a ctor from a
     def, so the type moved. Codex type names are capitalised, so a
     lowercase prefix cannot collide.

     **A PARAMETERISED ctor field keeps `integer(8)`, and finding out why
     cost a build.** The first version applied the real type to every field
     and emitted `type(cx_a) :: MkTup2_f0` for the foreword tuples, whose
     fields are type PARAMETERS. Fortran has no generics, so `cx_a` is a
     derived type nothing declares: strictly worse than the wrong-but-real
     `integer(8)` it replaced. Parameterised fields therefore keep the old
     type. **That half of the row is NOT closed**, and a real answer is
     `class(*), allocatable` plus a `select type` at every read, which this
     emitter has no machinery for. `Tup2` through `Tup5` are the visible
     case and every subject has them.

     **A non-name scrutinee remains broken and it is PRE-EXISTING.**
     `when Wrap (n * 2) is Wrap (v) -> v` emits
     `v = Wrap((n * 2_8))%Wrap_f0`, and Fortran has no component reference
     on a function result. `fort-emit-match-cond` has always emitted
     `<scrut>%tag` the same way, so a call scrutinee was already illegal in
     the tag test before any of this; the bind just meets the same wall.
     The fix is an `associate` around the chain or a declared temporary,
     and it needs the scrutinee text threaded through four match emitters.

     Verification. A scan for capitalised call targets with no `function`
     definition answers **3 before** (`Circle`, `Named`, `Rect`) and **0
     after** on the probe, so it can fail. Against `plug-oracle-arith` the
     whole diff is the `cx_` prefix, `field<i>` becoming `<Ctor>_f<i>`, and
     four new `MkTupN` constructors; every arithmetic, record, list, text
     and TCO line is identical, 0 undeclared type references and 0
     undefined `fort_` on both sides. `arith` never CONSTRUCTS a tuple,
     which is why nothing noticed the missing constructors. Still no
     Fortran toolchain here, so all of it is read against the standard.

     **One scan of mine was blind and nearly published a regression.** The
     `fort_*`-defined check missed generic interfaces and reported six
     undefined names that are all declared as `interface fort_list_snoc`
     and so on. Count `interface` as a definition or the instrument reads
     the prelude as absent.

  16. **The Lambdas section landed (blu, main 16977) and took FOUR arms red.
     All four are closed and the harness is 6 of 6 at 40 of 40 (reek,
     2026-08-18).** Each was a different defect, which is what a shared
     subject is for.

     **`python`: a multi-parameter lambda was emitted N-ary and APPLIED
     curried.** `(lambda x, y: (x + y))(a)(b)` raises
     `TypeError: missing 1 required positional argument`. The wire delivers
     an N-ary `IrLambda` and nested one-argument applications, so the
     emission is curried now, one `lambda` per parameter. Currying also makes
     partial application work, where matching the N-ary form against the
     chain would need the arity to agree at every site.

     **`csharp`: CS0149, six times. C# cannot INVOKE a lambda expression** --
     it has no type until it is converted to a delegate, and the wire applies
     one at its definition site. One cast on the outside settles a whole
     curried chain, because each inner lambda is then in a context that
     already expects a `Func<>`; `cs-type` spells `FunTy` curried already.

     **`wasm` had no lambda support at all and the shape of the wrongness is
     worth seeing.** The apply arm emitted `call_indirect` over the arguments
     and then spliced the lambda BODY beside them as another operand, so
     `lam_two` read `local.get $x` for a local nothing declares. Every
     parameter is bound into a local of its own name now and the body
     follows, which needs no rewriting because this emitter already resolves
     an `IrName` to `local.get $name`. Binding rather than substituting is
     what stops an argument that is a CALL from being evaluated once per use,
     and a partial application refuses rather than emitting a body whose
     remaining parameters are unset.

     **`zig` failed on a fourth thing blu had not seen, and it is a Zig-only
     trap.** `let k = 100` binds a `comptime_int`, and the closure capturing
     it took `@TypeOf(k)` for its environment field: `cannot load
     comptime-only type 'comptime_int'`. A scalar `let` carries its type
     annotation now, forcing the runtime type at the binding where the IR
     already knows it. Only scalars are annotated: a list, closure or record
     binds to an expression whose Zig type is spelled by construction, and
     annotating those would put the emitter's idea of the type in front of
     the value's own.

  15. **The five text builtins, which 1.31 excluded as "fortran is 1.7's" and
     nobody then checked. DONE (reek, 2026-08-18).** All five were
     REGISTERED, which is why a name census read fortran as armed, and two
     of them answered wrongly.

     `text-starts-with` and `text-contains` emitted `index(s, p) == 1` and
     `index(s, n) > 0` on `character(len=256)` operands. Those are BLANK
     PADDED, so the needle is 256 characters wide and `index` searches for
     `"he"` followed by 254 blanks inside `"hello"` followed by 251 blanks:
     `text-starts-with "hello" "he"` answers FALSE where Codex answers True.
     Both operands are trimmed now, which is the convention the rest of this
     plug already uses, and the empty-prefix case comes out right for free
     because Fortran's `INDEX` answers 1 for a zero-length substring.

     `text-to-integer` was a list-directed `read(s, *, iostat=ios)`, which
     FAILS on `"12ab"` and returned 0 where Codex answers **12**. It is an
     explicit scan now: optional leading `-`, then digits, stopping at the
     first non-digit. `"ab"`, `""` and `"-"` all answer 0, which is what the
     foreword def specifies.

     `text-replace`, `text-split` and `text-compare` were already correct and
     already used `len_trim`; the empty-needle and empty-separator cases both
     match. Against `plug-oracle-arith` the entire diff is the three-line
     `to_integer` body coming out and the scan going in.

  13. **The oracle. RULED (Damian, 2026-08-17): we cannot compile it here,
     and that is accepted.** The standard for this campaign is that a
     reader looking at the emitted Fortran thinks **"this is ready to test
     on a real Fortran compiler"**. That is the bar the stages above were
     held to, and by it they pass.

     **`plug-oracle-arith` now emits, and all 33 rows hand-trace to
     `plug-oracle-arith.expected` (val, 2026-08-17, after stages 9 to
     11).** Rows 1-17 were already right before this session: Fortran's `/`
     truncates toward zero like Codex's, `mod` takes the sign of the
     dividend like `int-rem`, and `modulo(a, abs(b))` is Euclidean like
     `int-mod`. Rows 21-25 and 29 were right. Stages 9, 10 and 11 closed
     30-33, 26-28 and 18-20 respectively. **Hand-tracing is not
     compiling.**

     **THREE THINGS A FIRST REAL COMPILE SHOULD BE POINTED AT, in the order
     they are likely to bite.**

     - **No `recursive` prefix. DONE (val, 2026-08-18).** `sum-to` called
       itself directly and `ping`/`pong` are mutually recursive, and all
       three emitted as a plain `function` with the name-as-result form,
       which `-std=f95`, `-std=f2003` and `-std=f2008` should be expected
       to reject. Both emission paths now emit
       `recursive function f(..) result(f_r)`, the `_r` suffix following
       the `_t` convention the TCO temporaries already use.

       **The RESULT clause is not decoration and is why this was one
       change rather than two.** A recursive function cannot carry its
       result in its own name, because inside the body that name is the
       recursive CALL, so adding the prefix without the clause would have
       produced a worse program than it replaced. The assignment target
       had to be threaded through `fort-emit-tco-body`,
       `fort-emit-tco-match` and `fort-emit-tco-match-branches`, which
       took `func-name` and used it for two different jobs: testing for a
       self-call and naming the result. Those are now separate arguments.

       **Measured against the depot plug binary on `plug-oracle-arith`,
       and every diff row is classified.** 16 of 16 chapter functions gain
       the prefix and a `result` clause, 0 remain plain, 0 lines still
       assign to a bare function name, and all 102 differing rows are one
       of exactly three shapes: the header, the result declaration, the
       result assignment. Nothing unclassified, which is the evidence that
       nothing else moved. Both paths are covered by the subject: `down`
       takes the TCO path and emits the loop with `down_r`, while `ping`,
       `pong` and `sum_to` take the ordinary path.

       **Still not compiled.** There is no gfortran here, so this is read
       against the language and the emitted text, not run.
     - **`mk_Gauge(g = n)%g`** applies a component reference to a FUNCTION
       RESULT. F2008 permits it and gfortran accepts it; F2003 is
       doubtful. Hoisting to a temporary is the fix if a compiler objects.
     - **Rows 31 and 33 recurse 100,000 deep** (`ping 100000`,
       `sum-to 100000`), which is the row the oracle's own prose says no
       self-tail-call pass can flatten. A default 8 MB stack should hold
       frames this small, but this is the row expected to fail first on a
       runtime with a fixed stack, and failing it is a finding about the
       target, not about the plug.

     **NEXT STEP, recorded and not scheduled: run the emitted output
     through a real Fortran compiler.** Whoever has one takes
     `codex/test/plug-oracle-arith.codex` first, then
     `codex/plugs/test-input/builtin-reach.codex` and the four probes
     named above.

     **Nothing in this campaign has been compiled.** Every stage is
     measured on the emitted TEXT against the language, with a control and
     where possible a sabotage arm, and none of that is the same as a
     compiler accepting the file. Say so in any report.

  A patch that supplies one helper body still reads in the diff as a repair
  while leaving the capability absent (L-CAPABILITY), which is why the
  stages land one at a time and each says what it did NOT establish.

  **Two defects the stage-2 probes found, neither introduced by it, and
  neither reachable from `codex/plugs/test-input/` at all.** Both were
  invisible because the five stock inputs emit no subroutine and no TCO
  function; the probes that found them are two throwaway chapters, and
  **adopting them as test inputs would give every plug in the sweep
  permanent coverage of two paths nothing currently reaches.** That is a
  recommendation, not done here, because it changes every plug's arm.

  - **`fort-is-effectful` never unwraps `FunTy`.** It unwraps `ForAllTy`,
    `ForAllEff`, `LinearTy` and `TypeApply`, so a def whose type is
    `Text -> [Console] Nothing` is not seen as effectful and is emitted as a
    FUNCTION. Since `opening` is excluded from the module, that leaves
    `fort-emit-subroutine` reachable only for a zero-parameter effectful def
    that is not `opening`, which is very nearly dead (L-UNCALLED). The two
    symptoms in the probe output are `shout = print *, trim(msg)`, a `print`
    STATEMENT spliced into expression position, and `shout('probe')` used as
    a bare statement where Fortran needs `call` or an assignment. Both are
    stage 5's subject and this is its root.

    `fort-emit-subroutine` also emits `implicit none` AFTER the parameter
    declarations, where it is invalid, and it is redundant besides since
    every procedure sits in a module that already declares it. **That fix
    was written and then REVERTED rather than shipped**, because the probe
    proved the code cannot be reached to exercise it. It belongs with the
    `FunTy` fix, which is what makes it reachable.

  - **The TCO jump had no temporaries, so it computed the wrong answer.
    FIXED (val, 2026-08-17), and it is stage 4.**
    `fort-emit-tco-assigns` wrote the new argument values into the
    parameter variables one at a time, in order, while later arguments were
    still being read from those same variables. For
    `countdown (n - 1) (acc + n)` it emitted `n = (n - 1_8)` then
    `acc = (acc + n)`, and the second line read the ALREADY-UPDATED `n`.
    Hand-traced, `countdown 10 0` returned **45 where the answer is 55**.
    Same shape as the compiler's own `tco-ensure-temps`
    (`X86_64.codex:359`).

    Each argument now evaluates into a `<param>_t` temporary against the
    unchanged parameters and is copied back afterwards. Three-way:
    the old emission traces to **45**, the new one to **55**, and 55 is the
    answer. A single-parameter function keeps the direct assignment, since
    there is nothing after it to corrupt, and declares no temporary.
    Containment control: `builtin-reach`, `hello` and `types` are
    BYTE-IDENTICAL before and after, so the change reaches only the jump.

    The list case is the one that matters and it was probed:
    `build (n - 1) (list-snoc acc n)` used to snoc `n - 1`, and now emits
    `acc_t = fort_list_snoc(acc, n)` against an `integer(8), allocatable ::
    acc_t(:)` temporary.

    **R-COST, stated because it is a real cost and not a free fix.** One
    extra variable per parameter and one extra copy-back per iteration. For
    a list accumulator that is an extra ARRAY copy per iteration, so the
    constant factor roughly doubles. It is not a complexity change:
    `fort_list_snoc` already allocates and copies the whole array every
    iteration, so the accumulate was quadratic in copying before this and
    is quadratic after. **The available refinement, not taken:** the LAST
    parameter never needs a temporary, because no later argument reads it,
    so assigning it directly after the temporaries are set would remove
    exactly the expensive copy in the accumulator-last shape. Not taken
    because the ordering argument is subtle, the win is situational, and
    none of this can be compiled here to check.
  **The third failure mode is CLOSED and it was pass-through to an undefined
  name** (val, 2026-08-17). `java`, `typescript`, `d`, `julia`, `perl`,
  `scheme`, `groovy` and `clojure` were listed here as having no `list-snoc`
  of any kind and unmeasured. Measured by running all eight over
  `plug-oracle-arith.codex`: every one emitted BOTH spellings as an ordinary
  call, `list_snoc`/`list_push` (`list-snoc`/`list-push` in the two Lisps),
  and none of the eight defined either -- 16 of 16 undefined. That is not a
  missing emitter arm, it is a missing PRELUDE entry, because these plugs
  resolve a builtin by sanitized name against an emitted prelude rather than
  through a dispatch table, which is a third dispatch shape beyond the two
  this entry describes below. `java` and `typescript` already emitted
  `list_length` and `list_at` from that prelude, so only the entry was
  absent; `julia` and `groovy` had no prelude at all and gained one.

  Each now defines both against `_cx_lpush`'s semantics -- non-empty appends,
  empty answers a fresh single-element list. **Three of them copy
  unconditionally instead**, because the target has no in-place append to
  offer: `d`'s `~`, `scheme`'s `append` and `clojure`'s `conj` all return a
  new sequence. That is inside the band `SAFE-MUTATION.md` leaves
  unspecified, and copying is the safe end of it, so it is a deliberate
  difference rather than a divergence to chase.

  **No toolchain for any of the eight exists on this box** (measured
  2026-08-17: `java`, `javac`, `tsc`, `dmd`, `ldc2`, `gdc`, `julia`, `perl`,
  `guile`, `racket`, `chez`, `groovy`, `clojure` all absent; only `node`,
  `dotnet` and `python` are present, which is also why the oracle wires the
  arms it does), so none of the eight was compiled or run. What was checked mechanically is that every symbol the emitted program
  CALLS it also DEFINES, and the check was validated against the pre-fix
  outputs, where it reports all 16 missing. Hand-traced against the oracle's
  own expected values, all eight give `push-len` 3, `push-at` 30 and
  `snoc-len` 3.

  **One defect found beside this and NOT fixed:** `perl` emits
  `[10, 20, 30, 40]->[\&i]` for `list-at`, a reference to the subroutine `i`
  where the index `$i` belongs, so every `list-at` in emitted Perl is wrong.
  It is a separate gap from this entry and wants its own row.
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

## 1.3 -- CLOSED: RISC-V frameless TCO, the honest gate is wired and all four items are settled

The admission gate was `rv-is-frameless-tco` alone; the test that asks the
question that actually matters, `rv-body-is-frameless`, was called by
NOTHING (its only references were its own recursion, L-UNCALLED). The gate
in use read a local count that cannot express the real budget, because for
callee-saved registers in a function emitting no prologue the budget is
zero rather than six.

**The gate is ON, fester 2026-08-18.** `rv-emit-function`
now asks `rv-is-frameless-tco def & rv-body-is-frameless (def.body)
(def.name)`, and the riscv cross battery under QEMU went 58 failures to
52 with no new failure: `audio-diffusion-test`, `bezier-identity`,
`engine-animation`, `noise-test`, `rasterizer-test`, `stats-wrap-test`.

Wiring it was refused three times before, and correctly: a body the
honest test refuses falls to the framed path, and the framed path was
broken for the same shapes. That is what the three items below were.

### The three framed defects that had to be fixed before the gate could go on.

`rv-save-args-skip-last` held the LAST tail argument in a raw register and
kept it there across the whole of `rv-copy-to-params`, while
`rv-load-local` takes a temp from a rotation for every spilled load, so a
later load in that loop handed back the register the held value was
sitting in. Every argument is now saved to a local uniformly
(`rv-save-tail-args`). Measured on the riscv cross battery: **95 failures
to 61, 34 tests fixed, no regressions**, and the tests it fixed are mostly
NOT tail-call tests -- `aesgcm256`, `x509-parse`, `db-full-test`,
`truetype-render-test`, `qr-encode` and the rest were all failing on this
one defect.

**One thing is still open, and it widens the gate rather than blocking
it:**

1. **MEASURED AND DECLINED, fester 2026-08-18. It is not worth doing, and
   this row said it was.** `rv-cond-is-frameless-safe` is
   `rv-cond-is-zero-safe` and nothing more, so it admits only `x == 0` and
   `x /= 0` and an ordinary loop guard like `if k >= 8` falls to the framed
   path. Widening it to the six integer comparisons over trivial operands
   was built and measured against the same seed on both arms:

   | | riscv battery | `db-full-test` flat image |
   |---|---|---|
   | narrow (shipped) | 53 failures | 225,552 bytes |
   | widened | 53, IDENTICAL set | 225,520 bytes |

   **32 bytes on a 225 KB program, 0.014 per cent, and not one test
   moves.** The five tests the old estimate promised (`audio-diffusion-test`,
   `bezier-identity`, `engine-animation`, `noise-test`, `rasterizer-test`)
   already pass: they were fixed by the `ra` defect in item 3 and by turning
   the gate on, not by the condition. That estimate was taken before either
   landed, which is exactly why this row said to re-measure rather than
   carry it forward.

   So the trade is: more bodies admitted to a path that produced THREE
   defects this year, for 32 bytes and no measurable speed claim (red ruled
   no timing campaign, R-COST by inspection). Declined. The code is not
   shipped; reproduce it by making the predicate test the six ops over
   trivial operands with text, sum, real and real-approx excluded, since
   `IrEq` on Text lowers to a `__str_eq` call and needs `ra`.
2. **DONE, fester 2026-08-18.** `int-mod acc 1000` as the first argument
   of a two-argument framed tail call answered 0 rather than 456 because
   `rv-emit-int-mod` emitted its operands with the tail call's
   `result-dest` still live, so the divisor literal was materialised into
   the register the dividend was sitting in: `addi s2, zero, 1000` then
   `rem t4, s2, s2`. Only `int-mod` showed it because every other operation
   in that test takes an I-type immediate and never materialises its
   literal, while `rem` is R-type only. `rv-emit-two-arg-binop`, which
   serves `math-mod`, had the same leak. Both now clear `result-dest`
   before emitting operands, which is what `rv-emit-binary-reg`,
   `rv-emit-rem-pow2` and `rv-emit-mul-pow2` already did.
3. **DONE, fester 2026-08-18. The third blocker was a FRAMED defect, and
   it is live on main with the gate still off.** `rv-has-any-call`
   answered False for every `IrBinary`, but four binary ops lower to a
   subroutine call (`IrAppendText` to `__str_concat`, `IrConsList` to
   `__list_cons`, `IrAppendList` to `__list_append`, `IrPowInt` to
   `__int_pow`) and `IrEq`/`IrNotEq` call `__str_eq` when the operand is
   Text or a sum. So `rv-tco-needs-ra` said no, `rv-emit-function-framed`
   NOP-ed out the `sd ra` (line 1344), and a loop like
   `f (n) (acc) = when g n is Stop -> acc is Go -> f (n - 1) (acc & " ")`
   ran `jal ra, __str_concat` with `ra` unsaved: it returns into its own
   body, which is a hang with no trap. `codex/test/tco-framed-append` is
   the guard, verified to hang after line 01 with the fix ablated.

   Full riscv cross battery under QEMU, three arms, no regression in any:
   59 failures on main, 58 with this fix alone (`goose-encode`), 52 with
   the fix and the gate. Measure QEMU against QEMU: the 62-to-59 figures
   elsewhere in this entry are Renode runs and the two emulators do not
   agree test for test.

4. **DONE, fester 2026-08-18, and it was not the tail call at all.**
   `rv-rt-list-cons` had its two arguments the wrong way round. x86-64 is
   the contract of record and its `__list_cons` reads the list length out
   of `rsi`, the SECOND argument (`X86_64ListHelpers.codex`,
   `emit-list-cons-alloc`); ARM64's reads it out of `x1`; and every
   emitter, this lane's included, passes the ELEMENT first. The riscv
   helper read the list out of `a0` and stored `a1` as the element, so
   `n :: acc` handed it the integer as a list pointer and the first `ld`
   faulted on it: `cause 5`, `tval` equal to the integer being consed.
   The element is parked in `t6` across the allocation now, because `a0`
   is overwritten with the new list before the element is stored.

   **Nothing on this lane conses, which is why it survived.**
   `list-view-probe` is the only test in the tree using `::` and it is
   `PASS_REFUSED` here for `__list-len`, so it never runs: the riscv
   battery had never executed this helper. `codex/test/list-cons-tail` is
   the guard and it checks CONTENT rather than only length, because a
   length-only check passes against a cons that stores the wrong operand.
   Ablated: against the unfixed helper it faults after line 01.

**What the gate costs, R-COST verdict RULED by red 2026-08-18: no timing
campaign.** The framed fallback is a CONSTANT FACTOR per iteration with no
change in complexity, which is what R-COST asks about; the inspection
verdict stands as the assessment, and a number gets measured only if a
battery test starts timing out.

Every body the honest test refuses falls to the framed path, which is a
prologue, an epilogue and stack traffic on each iteration of a loop that
had none. Code size WAS measured and is nothing: `db-full-test` went
220,872 to 220,960 bytes, 88 bytes or 0.04 per cent, though the two loops
that actually moved roughly doubled (44 to 96 and 44 to 80). Run time was
not measured, and by the ruling above it does not need to be: the cross
battery is a correctness instrument, nothing in it times a loop, and a
constant factor on a loop that now answers correctly is the trade this
entry exists to make.

Re-measure against the battery, not against the four `tco-*` tests:
the 34 tests above are what the framed path actually costs, and none of
them has `tco` in its name.

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

**Re-measured 2026-08-18 (val) with the fixed harness, and the row is
PARKED on a bracket rather than a theory.** Every number below is against
the Release 46 seed `12B07296419847B2`, the same `zig-plug.cdx`, port 9145,
each payload run twice.

| IR payload | subject | result |
|---|---|---|
| 16,900 | `plug-oracle-arith` | passes |
| 865,899 | `brotli-interop` | passes, 819,396 out |
| 1,404,714 | `apps` RomData | passes, 411,294 out |
| 2,164,366 | `apps` Simulate | passes, 407,182 out |
| 16,079,640 | `concat-codex-self codex\compiler` | DIES, `OUT OF MEMORY` |

**Every surviving payload is BYTE-IDENTICAL across its two runs.** That is
the control the varying death lengths have to be read against: the plug is
deterministic whenever it lives, so whatever varies belongs to the death
and not to the emission.

**The ceiling is NOT the ~1.13 MB the recheck sweeps quote.** That figure
is `codex/plugs/recheck/sweep-all.ps1:132` and it is the RECHECK plug's;
zig survives 2,164,366 bytes, nearly twice it. Do not carry a ceiling
across plugs. **The zig threshold is UNMEASURED between 2,164,366 and
16,079,640** -- nothing in the tree compiles standalone into that gap, so
closing it needs a payload generator that does not exist yet, and that is
the actual next step for this row.

**A SYNTHETIC payload generator was built for that gap and it does NOT work,
which is worth recording so the next reader does not build it again** (reek,
2026-08-18). Mechanically generated chapters of `pgN (v) = if v > k then ...`
give a clean 346.8 IR bytes per definition and the instrument validates at
the bottom of the gap: a synthetic 2,108,957-byte payload LIVES, agreeing
with the real 2,164,366. It fails going up, and not in the plug.

- **A single chapter of ~34,000 definitions makes the COMPILER
  double-fault**, `!EXC=08` with `CR2` immediately below the stack base.
  Payloads have to be spread across chapters; 200 per chapter is safe.
- **Spread across 130 chapters, 2,318,510 bytes of synthetic source does not
  compile in 3,000 seconds**, while the REAL 2,881,715-byte whole-compiler
  subject compiles to 16,079,640 bytes of IR. So the synthetic is
  pathologically slow to compile rather than the size being infeasible, and
  a generator that cannot reach the gap is not a generator.

**Real subjects in the gap are harder to come by than the sizes suggest, and
the row's "nothing in the tree compiles standalone into that gap" is holding
up.** Two routes tried and both fail before the plug is reached:

- **A directory subset does not compile.** `concat-codex-self -CodexDir
  codex\os` (1,672,787 bytes concatenated) halts in 8 s with
  `CODEGEN-HALTED: errors in bag`, and `codex\compiler\Emit` (1,234,832) in
  1 s with `error 3010: Unresolvable cite: Codex chapter 'Build Settings'`.
  The tool preloads cited FOREWORD chapters, not cited chapters from other
  quires, so any directory whose chapters cite across the tree is not a
  subject. Both of these are the right SIZE for the gap and neither is a
  subject, which is the row's claim holding rather than failing.
- **A chapter PREFIX of the whole-compiler subject does not compile
  either.** The 86 chapters are not in dependency order: the first 43 stop at
  `error 3010: Unresolvable cite: Emit chapter 'Codex Emitter'`. A prefix
  would need the transitive closure of its cites, which is a dependency
  solver rather than a truncation.

So the honest state is that the bracket is still **2,164,366 LIVES,
16,079,640 DIES**, and what this pass adds is that two obvious ways to close
it are dead ends with the reasons recorded. A third that has not been tried:
keep the whole subject and take the closure of a chapter's cites rather than
a prefix.

**Two harness traps sit in front of this measurement and both read as plug
results.** `compile.ps1` defaults to `-TimeoutSec 600` and reports a timeout
the same way a crash arrives at the caller, as no IR file; and the compiler's
own death is an `!EXC=` line in the LOG, not on stdout. Read the log tail
before calling anything a plug failure.

**`-mem` is settled and the old wording was too weak.** Not "the same
band": 3,072 MB and 12,288 MB die at exactly the same byte, 128,800.

**The five-different-lengths claim does not survive contact with a
controlled re-run, and this is the part to be careful with.** Four runs of
the ONE 16 MB payload this session gave 131,600, 130,200, 128,800 and
128,800 bytes, which is 94, 93, 92 and 92 times `net-mss` 1400 -- stable
within a window and drifting DOWN across the session, not scattered. Two
back-to-back runs agreed exactly. **This box is shared by four agents and
the environment was not controlled**, so the honest statement is that the
death point is reproducible over a short window and moves slowly over
hours; whether that is host load, host memory pressure or something in the
guest is unmeasured. It is NOT established as guest nondeterminism, and
the multiple of 1400 is just the last whole flushed chunk.

**`build/plug-run.ps1` reported `OK` on every one of those dead guests,
and that is CLOSED (val, 2026-08-18).** 38 plugs share that harness and
nothing noticed, because every wired subject is a few KB.

**Half of the diagnosis this entry used to carry was already stale when it
was written, and the stale half is the one that names a cause.** It said
the harness does not pass `-output`. It does, and has since 2026-08-17,
along with a `WaitForExit` before the grep and a `recvAborted` arm. What
was actually missing is narrower and is why those three were not enough:
**a guest that dies is never REFUSED, so it prints no `TRUNCATED sent=`
line, and it closes the socket cleanly enough that `recvAborted` stays
false.** Its own death line is the only witness, and nothing read it.

The fix is a scan of the captured console for `OUT OF MEMORY` and `!EXC=`,
exiting 9, placed AHEAD of the truncation arm because a dead guest is the
cause and a missing `TRUNCATED` its absent symptom. In
`codex/build/plugrunScript.codex` with `build/plug-run.ps1` regenerated
from it, submitted together; `check-generated-scripts.ps1 -Only plug-run`
reports `match`, drift 0.

**The control was run and it is what makes this worth anything.** Same
16,079,640-byte payload, same plug binary, same port, one after the other:
the DEPOT script exits **0** and prints `OK` over a 130,200-byte artifact;
the fixed script exits **9** and names the guest's own `OUT OF MEMORY`.
A healthy run is unaffected (`plug-oracle-arith` through zig: exit 0, OK,
17,112 bytes), so the check discriminates rather than simply failing.
The two dead runs stopped at 130,200 and 131,600 bytes, exactly 93 and 94
times `net-mss` 1400 -- whole flushed chunks, which is what any death mid
send looks like and is not on its own a clue to the cause.

**Those are NOT comparable to the 534,800 to 547,400 band above and must
not be folded together.** That band is Steve's `bundle_whole.ps1` subject
(2,575,126 bytes of source); this control used
`concat-codex-self.ps1 -CodexDir codex\compiler` (2,881,715 bytes, 52,032
lines, IR 16,079,640 bytes against the Release 46 seed `12B07296419847B2`).
Two subjects, two payload sizes, so the only claim carried here is that the
guest dies and the old harness called it `OK`. What both share, and what
1.26 still does not explain, is that repeated runs of ONE payload die at
DIFFERENT lengths.

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
## 1.27 -- PR 66's five open findings are about OUR compiler, and nothing recorded them

Steve Howell's `deck-record-repro/README.md` on GitHub PR 66 is a findings
register: ten numbered plus one candidate, five closed by Update 43. The
five open ones and the candidate are about the COMPILER rather than about
zig, and they were in no register here until this row. The PR carries 151
paths and only `ZigEmitter.codex` was taken, so closing the PR without this
row would have closed the only copy. His probes stay on his branch under
`deck-record-repro/`; each finding below names the one that runs it.

**Verified here, 2026-08-17, against the released seed `270227BE`:**

- **Finding 6: `lift-lambdas` runs on the CDX path only, so lambdas that
  close over enclosing locals go out on the plug wire.**
  `codex/compiler/IR/LambdaLifting.codex` is a complete pass with exactly
  ONE caller, `opening.codex:833`, inside the `cdx-chapter` path with its
  own LIFT phase and deck budget. `emit-ir-cce` (`opening.codex:1694`) runs
  `compile-frontend-ir` and the named pipeline and never reaches it, and
  nothing in the plug interface says so. His cost: the zig emitter needed
  free-variable analysis with a shadowing rule that lifted IR would have
  made unnecessary, because the pass rewrites the use site as a partial
  application and that plug already handled partial application.
  **Routed to compiler-backlog COMPILER-12 (root, 2026-08-17)** when plugs 1.17
  closed: the fix is compiler-side (lift on the wire path), not a plug's.

- **Finding 9: an undefined TYPE name in an annotation compiles without a
  word.** Measured both directions on seed `270227BE`: a signature
  `f : PhantomSig -> Integer` with `PhantomSig` defined nowhere compiles at
  exit 0 with no diagnostic, while the control, an undefined VALUE name in
  the same shape, is refused `CDX3002: Undefined name`. Type names in
  annotations are simply not existence-checked the way value names are, in
  field position, bare field position and signature position alike
  (`probe-phantom-field`, `probe-phantom-bare`, `probe-phantom-sig`). The
  monolithic build hides it because it always carries every definition; a
  subset build compiles clean and the mistake surfaces elsewhere or never.
  This is the sharpest of the six and it is ours, not a plug gap.

  **FIXED, and re-measured rather than assumed (reek, 2026-08-18).** Against
  the depot seed `12B07296419847B2`, an undefined type name is now refused in
  every position this bullet named. Signature position:
  `error CDX3008: Undefined type name: PhantomSig`, exit 4. Field position:
  the same code on `Holder = record { h : PhantomField }`, exit 4. The
  control still behaves, so the comparison is live rather than two silences:
  an undefined VALUE name is `error CDX3002: Undefined name`, exit 4.

  It was measured open on seed `270227BE` and is closed on this one; nothing
  in this row records who closed it. **This was the only item in 1.27 with no
  route out**: 6, 10 and 11 are compiler-backlog COMPILER-12, COMPILER-11 and
  COMPILER-10, 7 is confirmed with no action owed, and 8 is refuted. The row
  is therefore spent as a work item, and what is left is a findings RECORD
  for GitHub PR 66 that this file says is the only copy. Deleting it would
  destroy that copy, so it is left standing and should be moved rather than
  dropped.

**Re-run here as OURS, 2026-08-17 (reek), against depot seed. Three of the
four hold; finding 8 is REFUTED as stated and the true mechanism is a
different one.**

- **Finding 7: CONFIRMED, both halves.** `IRExpr` is exactly 24 constructors
  (`IR/IRChapter.codex:19-43`), and there is no `ir-map-children` /
  `ir-fold-children` or any equivalent anywhere in `codex/compiler` or
  `codex/plugs/common`, while the control holds: `CodexTypeTree.codex:4` and
  `:24` do carry `codex-type-map-children` and `codex-type-fold-children`.
  His second half holds too, and it is the half worth having re-run: **no
  plug enumeration silently drops a constructor.** Censused per PLUG rather
  than per file, which is the unit that matters because arm64 and riscv split
  their walk across `CodeGen`, `CodeGen2` and `CodeGen3` and a per-file count
  reads a split walk as an incomplete one -- 51 of 52 plugs name all 24. The
  one that does not is `t3isa` at 18, missing `IrHandle`, `IrWithTimeout`,
  `IrFork`, `IrAwait`, `IrTry` and `IrError`, and it is not a silent
  fallthrough: `T3IsaEmitter.codex:671` is an explicit
  `is otherwise -> t3-refuse c "unsupported IR form"`.

  **A claim published here on 2026-08-17 and WITHDRAWN the same day, kept
  because red was assigned work on it.** It said a t3isa refusal reaches the
  artifact as a comment plus `TLIT R9, #0` and that "nothing observes it
  either way", inviting a fix to make the plug exit nonzero. **The refusal is
  observed, and it is already a hard failure.**
  `codex/plugs/t3isa/run.ps1` greps the emitted assembly for `!UNSUPPORTED:`,
  prints every one and **exits 6**; `gate.ps1:47` throws on any nonzero exit
  from it. Measured, not read: a valid program using a lambda (x86-64 compiles
  it and answers 42) run through `run.ps1` exits **6** and names
  `; !UNSUPPORTED: lambda` and `; !UNSUPPORTED: call to an unknown function:
  f`. The error was reading only the Codex side and concluding what the
  harness does -- the same shape as "grep for the CALL, never the cite".

  **What survives is small and is not a hole.** `T3Ctx.refusals` (`:167`) is
  accumulated and genuinely read by nothing, the only other mentions being
  record-copy boilerplate; the information travels in the emitted comment
  instead, so the field is redundant state rather than a lost signal. And
  `build/build.ps1` builds t3isa without running it, which its own comment
  states is deliberate: t3isa's gate needs an external emulator that lives on
  one machine.

- **Finding 8: REFUTED as stated.** The claim is that `ForAllTy` appears on
  the text-plug wire "where the machine-code plugs reading the same IR-CCE
  never see it". They do see it. Two arms over one polymorphic probe
  (`my-id : a -> a` and `my-first : List a, a -> a`), same source, same seed:
  under `-Passes 'text-plug'` the defs are `my-id, my-first, opening` with 1
  `forall` node, and under the DEFAULT pipeline they are `my-first, opening`
  with **1 `forall` node also**. Confirmed on the real wire encoding rather
  than the Unicode form: `compile.ps1 -IrCce` with the default pipeline emits
  `(forall 42 (fn (list (tvar 42)) ...))` for `my-first`, read back with
  `build/cce-grep.ps1` because `Select-String` over a CCE artifact returns
  zero matches rather than failing. `(forall ...)` is `ForAllTy`:
  `plugs/common/IRTextParser.codex:289` parses it into one.

  **What is true is about SURVIVAL, not about the type vocabulary.** The
  default pipeline inlines `my-id` away entirely, so one polymorphic
  definition disappears; text-plug keeps it. So text-plug sees MORE
  polymorphic definitions, and any polymorphic definition the inliner cannot
  remove arrives quantified on BOTH pipelines. That consequence is still
  undocumented, which was his point, but the mechanism in the row was wrong
  and the machine-code plugs are not exempt.

  **No live hazard measured.** `arm64`, `riscv`, `elf`, `pe` and `img` never
  name `ForAllTy` at all (`csharp` and `recheck` do, 6 mentions each), yet the
  arm64 plug compiles the probe at exit 0, 13,624 bytes, with no complaint
  about it. Recorded rather than chased.

- **Finding 10: CONFIRMED, and sharper than the row says.**
  `emit-record-set-builtin` is `Emit/X86_64Builtins.codex:863-888`, not
  `X86_64Helpers.codex`. It stores in place through
  `emit-narrow-store-proven` at the field offset and returns `loaded.reg`,
  the pointer it was handed, with no copy allocated; the `when rec-ty` has one
  `RecordTy` arm and does not distinguish `mutable` from plain, so the two
  behave identically. All as stated.

  **The part not in the row: the compiler already has half a check here.**
  Line 878 calls `noalias-note`, and `X86_64Compound.codex:1810` shows it
  raises `cdx-noalias-proven` as an INFO **only when non-aliasing IS proven**
  for a unique linear owner, and otherwise returns the state untouched. So the
  aliased case is exactly the unproven case, and the unproven case is silent.
  The positive arm exists and the negative arm does not, which means "proven
  safe" and "nobody asked" are the same output. So this is not only a
  documentation gap for plug authors; there is a diagnostic site already
  standing that could carry the answer. **Routed and decided: compiler-backlog
  COMPILER-11 (root, 2026-08-17): no note at the not-proven arm, measured on the
  corpus (CDX4011 fires zero times compiling the compiler; a negative note would
  fire 400-plus times per compile at documented-concession sites).**

- **Finding 11: NOT REPRODUCED, and his hypothesis is not needed to explain
  it, because 72 is unreachable by construction.** Read end to end,
  `Core/DiagnosticBag.codex` cannot produce it: every path in `bag-add-error`
  that increments `error-count` also pushes to `diagnostics` in the same
  record literal (`:50-59`), the cap at `max-errors` 20 leaves the count at 21
  and then returns the bag unchanged (`:48-49`), and the only construction of
  a `DiagnosticBag` outside the chapter is `copy-sx-bag`
  (`Syntax/SyntaxNodes.codex:559-564`), which copies count and list
  faithfully. So no path reaches 72, and none reaches a nonzero count with an
  empty list. His number came from somewhere other than this chapter; his
  probe is on his branch and was not run here, so this stays HIS measurement
  and is not ours.

  **A real divergence does exist, in the other direction.** `bag-add` pushes a
  non-error diagnostic to the list and leaves `error-count` alone (`:40-44`),
  so the list legitimately runs LONGER than the count, unbounded, while the
  count caps at 21. `bag-count` (`:88-90`) returns `list-length
  (bag.diagnostics)` -- the total diagnostic count, not the error count --
  sitting next to `bag-has-errors`, which reads `error-count`. A caller
  printing `bag-count` as an error total would count warnings and infos as
  errors. **Latent, not live: `bag-count` had no callers** (L-UNCALLED); it was
  recorded as compiler-backlog COMPILER-10 and the def was DELETED 2026-08-17
  (root), so the misread cannot be written.

## 1.32 -- the builtin-table check covers the WIRED plugs only

`build/check-plug-builtins.ps1` closed the general gap (main 16335): a plug
must have an arm for every builtin that reaches its wire, measured from the
IR the plug actually receives rather than from source. The csharp 16-bit
accessor pair that opened this row closed with it (main 16310).

**The third shape is modelled and `pascal` is wired (main, 2026-08-18).** The
check knew two registration shapes, a record table (`name = "x"`,
python/zig/csharp) and an if/else dispatch chain (`n == "x"`,
javascript/wasm). The third is an ordered names list dispatched by INDEX,
which `pascal`, `ada`, `babbage`, `cobol`, `elixir`, `fortran`, `nim`, `objc`
and `riscv` all write. Keying on quoted names alone would take every emitted
fragment in the file with them, so the extraction opens on a line matching
`builtin-names = [`, takes quoted names while it is open, and closes on the
first `]`.

`pascal` extracts 23 that way against 6 before: its 20-name list, plus
`True`, `False` and `Nothing`, which are declared builtins and which this
plug really does answer at `n == "True"`. That clears the thin-extraction
floor of 20 on its own, so the floor was not touched. Wired and green, 7
builtins on the wire, 0 gaps.

**The green was proved to be capable of failing before it was believed.**
Replacing `list-push` in `pas-builtin-names` with a name that is not a
builtin makes the check report `pascal list-push` and exit 1; reverted, it
returns to OK. A pass over 7 wire names against a 23-name table is otherwise
a result that would have happened whether or not the extraction worked.

**FIVE MORE PLUGS ARE READ AND WIRED (reek, 2026-08-18): `ada`, `elixir`,
`nim`, `objc`, `cobol`.** Eleven plugs are checked now against six. Every
extraction was read name by name against its table: `ada` takes 60 against a
46-name list, the other four 31 against 27, and in every case the surplus is
`True`, `False`, `Nothing` and `__narrow`, which those plugs really do answer
at `n == "x"`. Two extractions are imprecise and it does not matter, which is
the part worth knowing rather than assuming: `ada`'s `n ==` pattern also
catches `Integer`, `Text`, `Boolean` and six `real-*` TYPE names, and
`cobol`'s table pattern catches the emitted fragments `"0"`, `"1"` and
`"WS-"`. **None of those nine is a DECLARED builtin**, so none can ever reach
the wire and none can mask a gap. A surplus name is only dangerous when it is
a builtin.

**The green was proved capable of failing for each of the five.** Ablating
`list-length` out of one plug's names list at a time reports exactly
`<plug> list-length` and exits 1, five times, one per plug; restored, the
check returns to OK. That is what shows the new entries are actually in the
loop rather than merely spelled in it.

`cobol` was the only one of the five with a real gap, four of them, and it
was worse than the row's model of a gap: it emitted a paragraph that RUNS and
answers zero rather than a call COBOL cannot resolve. Closed at 16837 with
the account.

**THREE PLUGS THAT WRITE THIS SHAPE ARE STILL OUT, each for a different
reason.** `fortran` extracts 50 and belongs to 1.7, which measures it against
a wider subject. `riscv` is fester's lane (1.3 family). **`babbage` extracts
12 and FAILS the floor of 20, and the floor is right.** The Analytical Engine
has no text and no list, so `list-at`, `list-length`, `list-push` and
`list-snoc` are absent from that table on purpose and `babbage` answers them
with the `!UNSUPPORTED:` refusal 1.37 landed, not with an arm. Wiring it
would mean lowering the floor for everyone in order to accuse a plug that is
behaving correctly.

The design account is not repeated here. It is in the generator's own
doctrine comments (`codex/build/checkplugbuiltinsScript.codex`), where the
next person to change the check meets it, and in CL 16333.

## 1.34 -- on ARM64 the boundary between a program and the block device is the effect system, and the hole in it is `peek-32`/`poke-32`, not the VirtioBlk chapter

Found closing 1.17 (root, 2026-08-17); measured further 2026-08-17 (root)
before choosing a close, and the measurement changes the close. The block
BUILTINS reach the disk only through `svc #10/#11/#12` and the capability gate
in the synchronous vector slot (`Arm64ProcessKernel.md` Stage 4). The driver
those SVCs call is `codex/foreword/core/VirtioBlk.codex`, an ordinary Foreword
chapter whose entry points carry no effect row (`vb-read-auto`,
`vb-write-auto`, `vb-capacity-auto`, lines 220-257), so a chapter that writes
`cites Foreword chapter VirtioBlk` drives the device with nothing checked.
**But the driver is itself only `peek-32`/`poke-32` at the MMIO base
(`vb-read`/`vb-write`, lines 100-103), and those two builtins carry an EMPTY
effect row (`codex/compiler/Types/Builtins.codex:112,115`) because they are
the general memory accessors; `read-mmio-32` (`:119`, row `Device.Mmio`) is
the same instruction under a gated name.** So a `Device.Block` row on the
`vb-*` entry points, the first close this row offered, is walked around by
inlining the four peeks and pokes, and would be an instrument that says
"gated" over a hole (L-CAPABILITY-LOST shape). There is also no privilege
boundary to fall back on: ARM64 code runs at EL1 and the sysreg helpers
`write-ttbr0-el1`, `write-sctlr-el1`, `write-mair-el1` (`Arm64Runtime.codex:1259-1272`)
are called bare, with no effect row, from ordinary chapters (`os/kernel/Arm64Timer.codex:19-23`
calls `read-cntfrq-el0` and `write-cntp-ctl-el0` that way). x86-64 differs in KIND, not by luck: its
disk is on port I/O, and `port-in-*`/`port-out-*` carry `Device.Port`
(`Builtins.codex:94-104`), so a program that drives the ATA controller itself
must declare that capability in its manifest; the effect system IS the
boundary there and it holds. **Decision needed (routed to red 2026-08-17):**
(a) accept the effect system as the ARM64 boundary too and give the MMIO
window a gate of its own, which means the raw accessors refuse or require
`Device.Mmio` when the address is in the device window (a codegen or checker
range check; seed-affecting; the driver then gets `Device.Block` rows so
citing it declares what it does, and fester holds `VirtioBlk.codex`), or (b)
a real privilege boundary (programs at EL0 with the device window unmapped),
which is a process-model campaign, not a row. Until one is chosen the ARM64
capability gate should be read as gating the `block-*` builtins only. RISC-V
has the identical shape once its ecall twin lands (fester, riscv 1.3).
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

**The 42 plugs whose runtime is not on this box are DONE as far as emission
goes (root, 2026-08-17, red's reassignment from val; four CLs by family, each
read and not run):** class 1 wrappers in the JVM six, the native seven,
`typescript` (which then RAN under node 24, see 1.40) and `gtk` (the python
counter); class 2 and class 3 recorded per plug in
`docs/Designs/Active/Compiler/PlugDeepRecursion.md` step 3; the .NET UI
shells (`maui`, `wpf`, `winforms`) recorded as class 1 not applied (the
UI-bound output needs a dispatcher hop nobody here can measure); `fortran`
stays val's.

**`fortran` is DONE as far as emission goes (val, 2026-08-18).** Two
questions were tangled here and only one was about depth. The CONFORMANCE
half is fixed: every emitted function had no `RECURSIVE` prefix and
returned through its own name, so recursion was not standard-conforming at
all and depth was moot while any recursion was illegal (1.7 stage 9 carries
the account and the measurement). The DEPTH half reads as **class 3**, a
divergence recorded in `PlugDeepRecursion.md` step 3: Fortran has no
portable threading for the class-1 shape to bind to, and the depth
available is set by `ulimit -s`, the linker's stack reserve or
`OMP_STACKSIZE`, all outside the emitted source. **That class is a READING
and is NOT ablated** -- python read as class 1 from the outside and was a
counter, which is the standing reason not to trust one.

**No new runtime has appeared, measured 2026-08-18 (reek).** A PATH census of
37 candidate interpreters and compilers answers with exactly five binaries:
`python`, `node`, `wasmtime`, `dotnet`, `zig`. That is the six oracle
arms already wired (`node` carries both `javascript` and `typescript`)
and not one of the 42 parked plugs. Re-run the census before reading this row
as blocked, rather than carrying this sentence forward (L-COUNT).

What remains of this row is measurement when a runtime appears. Order and classes (the design's rule: class by the language's
mechanism, and say so as a reading): JVM family (`java`, `kotlin`, `scala`,
`groovy`, `clojure`, `compose`), class 1, `Thread(null, runnable,
"codex-main", 512L * 1024 * 1024)`; native family (`rust`, `d`, `swift`,
`swiftui`, `objc`, `qt`, `ada`, `pascal`), class 1 with the language's own
thread-with-stack-size; the JS-family node targets (`typescript`,
`electron`), class 1 by `worker_threads` as `javascript`; the .NET UI shells
(`maui`, `wpf`, `winforms`), class 1 by the `csharp` thread with the UI
caveat recorded; class 2, nothing to emit and a paragraph saying why (`go`,
`elixir`, `haskell`, `perl`, `php`, `scheme`); class 3, a recorded divergence
(`ruby`, `julia`, `lua`, `ocaml`, `nim`, `cobol`, `flutter`, `gtk`, and the
browser targets `angular`, `react`, `vue`, `svelte`, `html`, whose stack is
the browser's); `fortran` stays val's.

**THE ORACLE HARNESS IS GREEN: 6 passed, 0 failed, 0 skipped, exit 0**
(reek, 2026-08-18). This row said `zig`'s arm was RED for a missing
`list-snoc` emitter and that `plug-oracle-test.ps1` exits 1 because of it.
`ZigEmitter.codex:787` has had a `list-snoc` arm mapping to `cx_ll_push` for
some time, and re-measured against freshly built binaries every arm passes 33
of 33: python, javascript, typescript, zig, wasm, csharp. Nothing is owed to
Steve Howell here.

**Both red arms seen on the way to that green were STALE PLUG BINARIES, and
that is the durable lesson.** `typescript`'s binary was one merge-down behind
its source and emitted a prelude truncated to its first function, failing 36
lines with `int_mod is not defined` -- which reads exactly like a real defect
and directly contradicted 1.40's record of the arm passing 33 of 33. 1.40 was
not wrong; it was true when it was built. Then the staleness guard added at
this CL fired on `zig`, which had reported PASS 33 of 33 minutes earlier
against a binary a day old, so that green was scored on an old emitter and had
to be thrown away and re-measured. **A stale plug binary produces a confident
wrong answer in BOTH directions, and it cost three diagnoses in one session.**
`build/plug-oracle-test.ps1` now refuses a plug whose newest `.codex` is newer
than its `.cdx`, printing both timestamps and the rebuild command, rather than
scoring the old emitter.

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

## 1.41 -- a per-byte receive accumulate costs 116.77 s per 16 MB, and nine harnesses still have it

Carried out of 1.30. **Measured 2026-08-18 (val) and it is not noise, which
is what this row was filed expecting.** The shipped shape against a bulk
write, same 16,777,216 bytes arriving in the same 8,192-byte reads:

| arm | time |
|---|---|
| `$allBytes.Add($readBuf[$bi])` per byte | **116.77 s** |
| `$allBytes.Write($readBuf, 0, $n)` | **0.02 s** |

The cost is the 16,777,216 interpreter iterations, not `List.Add` and not
the transport, so it scales with the ARTIFACT and is invisible on every
text plug. That is why nothing noticed: the wired oracle subjects are a few
KB, where the same loop costs milliseconds.

**`build/plug-run.ps1` is FIXED (val, this CL)**, `MemoryStream` in place of
the list, `.Length` for `.Count`. The artifact is byte-identical before and
after (`brotli-interop` through zig, SHA-256 `B1908ADE...`, 819,396 bytes),
and the death arm still fires with a correct count. 38 plugs share it.

**Nine sites carried the shape when this was filed** (five are fixed below; grep
`for ($bi = 0; $bi -lt $n; $bi++) { ... .Add(`):

    build/boot-arm64.ps1:113        build/run-plug.ps1:108
    codex/plugs/elf/run.ps1:91      codex/plugs/img/run.ps1:107
    codex/plugs/javascript/run.ps1:89   codex/plugs/pe/run.ps1:98
    codex/plugs/wpf/run.ps1:105
    codex/plugs/recheck/kill-rate.ps1:443
    codex/plugs/recheck/sweep.ps1:184

**Only the ones that receive a BIG artifact are worth changing, and that is
a measurement per site, not a sweep.** `img`, `pe` and `elf` build multi-MB
binaries and are the candidates; `javascript` and `wpf` receive text in the
tens of KB and would gain nothing visible. The two under `recheck/` are that
lane's files. Do not regex this across nine files: `plug-run.ps1` is
GENERATED and the rest are hand-maintained, so they do not take the same
edit, and a blind sweep would put a generated-file edit where the next
regeneration discards it.

**THREE SITES FIXED (reek, 2026-08-18): `pe`, `img`, `elf`.** Six remain, and
the triage above held for WHICH sites. **What the row got wrong is the
payoff, and this is the part to read before doing the other six.**

The loop cost is real and val's figure reproduces exactly. Measured in
isolation on this box, 8,388,608 bytes in 65,536-byte chunks:

| arm | time |
|---|---|
| `$acc.Add($readBuf[$bi])` per byte | **58.33 s** |
| `$acc.Write($readBuf, 0, $n)` | **0.015 s** |

**And the harness wall clock did not move.** `img` on the sample chain, the
same 8,388,608-byte image byte-identical both ways
(`4ED5D817CC224B65928400E66F48902BE5EDA9A108C95EB63EA48FDD1C8960AF`):
**68.62 s before, 68.63 s after.** `pe` on `seed/Codex.cdx`, 2,677,760 bytes
identical (`E09D76F7...`): 67.35 s to 64.35 s, which is inside run-to-run
variance on a 65-second VM run and is not a result.

The receive is overlapped with a guest that is slower than the loop, so 58
seconds of host CPU sat inside the guest's own latency and never reached the
clock. **The fix buys a CORE, not a second.** That is still worth having and
is the honest reason to do the rest: the harnesses run at `-Jobs 8`, where
eight pegged cores are contention that does show up as wall clock. It is not
worth having as a wall-clock claim on a single run, and a CL that says the
run got faster would be wrong.

**TWO MORE SITES FIXED (reek, 2026-08-18): `build/run-plug.ps1` and
`build/boot-arm64.ps1`, the only two remaining that receive a big artifact.**
`run-plug.ps1` is what `plug-oracle-test.ps1` and `run-plug-chain.ps1`
deliver through and it can be pointed at the binary plugs; `boot-arm64.ps1`
receives a multi-MB PE from the PE plug. **BOTH ARE GENERATED**, so the edit
went into `codex/build/runplugScript.codex` and
`codex/build/bootarm64Script.codex` and the shipped scripts were brought to
match by hand rather than regenerated wholesale, which is the
drift-runs-the-other-way trap in `docs/Designs/Active/Build/Build.md`.
`check-generated-scripts.ps1` is 57 generators, **0 drifted, 0 broken**.

**Four sites remain and NONE of them is worth changing**, which closes this row
rather than parking it. `codex/plugs/javascript/run.ps1` and
`codex/plugs/wpf/run.ps1` receive text in the tens of KB, where the loop
costs milliseconds and the change buys nothing measurable;
`codex/plugs/recheck/kill-rate.ps1` and `codex/plugs/recheck/sweep.ps1` are
that lane's files. If a future subject makes one of the first two receive
megabytes, the edit is the three lines above.

So the rule for the remaining six is unchanged but its justification is not:
change the big-artifact sites because they waste a core, and do not expect
the elapsed time to fall.

**`elf` is fixed but NOT executed, and that is a separate gap.** Its input
comes from `codex/plugs/elf/extract-x86-output.ps1`, which exits 4 `FAIL: VM
did not start` on this box with `build-output/bare-metal/Codex.cdx` present
(2,829,317 bytes, 08-17). Two runs, same answer. Nothing in the elf lane can
be measured end to end until that is understood, and it was not chased here.

**A stale plug binary is what the `img` measurement ran into first**, and it
is worth knowing because nothing could see it: `img-plug.cdx` from 08-16
FAULTED on the very subject `test-disk-compile.ps1` uses, printing a register
dump, and the harness reported `[img-run] OK` on 2800 bytes of handshake.
That hole is closed at 16861; rebuilding the plug fixes the fault.

## 1.29 -- three ARM64 load-address constants are stale, and one of them has a reader

**The slot cap is CLOSED (blu, 2026-08-17).** `a64-assign-effect-op-addrs`
based the table at `#40100000` while the image loads at `#40100080`, so the
slots sat in the 128-byte ELF header hole, which holds exactly 16, and slot
16 was the program's first instruction. The table now sits at
`a64-effect-op-table-base` `#40020000` with `#E0000` of clearance, and
`a64-effect-op-slots` (1024) makes the emitter raise `[UNSUPPORTED]` rather
than wrap, which `run.ps1` already treats as a refusal (exit 6). Verified:
full arm64 cross battery at depot 418 pass / 31 fail and with the change
418 / 31 with an **identical FAIL set**; sabotage at ceiling 0 refuses with
the right operation count; the boundary at exactly 4 slots for 4 ops passes,
so the guard is `>` and not off by one. Both addresses are one `movz` with
`lsl 16`, so code size is unchanged and no layout shifted (L-DECODE).
`handler-table-base` was set to the new constant for tidiness but is
**written and read by nothing** (L-UNCALLED); only `effect-op-addrs` is
consumed, through `a64-find-effect-op-addr`. The fixed map in
`Arm64Runtime.codex` now records the new occupant. 1.33 needs no region at
all (see that row), and its three cells sit at `#40022000`..`#40022018`.

**What is left is the stale constants**, which are a separate defect and
were never the cap. The neighbouring prose is right and the constants are
the stale half:
`a64-load-base` (`Arm64CodeGen3.codex:1827`) and `a64-disasm-base-addr`
(`Arm64Disasm.codex:488`) say `#40000080`, and `arm64-build-elf`
(`Arm64Elf.codex`) uses `#400000`, none of which is where the image actually
loads (`#40100080`, and `compile-arm64.ps1:134` passes `0x40100000`).

**They are not equally dead, and that decides the order.**
`a64-disasm-base-addr` HAS a reader, `Arm64Disasm.codex:505` computes
`a64-disasm-base-addr + byte-off`, so every disassembly listing prints
addresses about 1 MB low. That is a live wrong number aimed at whoever is
debugging, which is exactly the audience least able to afford it.
`a64-load-base` has **no reader at all** (L-UNCALLED), so it is a wrong
constant nothing can act on: delete it rather than correcting it, since a
corrected constant with no reader is still a thing that can go stale again.
Verified 2026-08-17 (blu) by grep over `codex/plugs/arm64` excluding
`build-output`.

## 1.33 -- there is no DECK on arm64 or riscv, so nothing can be made to outlive a `__heap-restore` on either lane

**CLOSED (blu, 2026-08-18). ARM64 STEPS 1, 2 AND 2b, AND RISCV STEP 3.** Step 1: the three cells and `__deck-pos` / `__deck-set` / `__deck-alloc`, pinned by `codex/test/deck-cell-contract` (main 16604). Step 2: the bracket, `__deck-enter` / `__deck-exit` with the nesting counter, pinned by `codex/test/deck-bracket-contract` (main 16624). All five deck builtins work on arm64. Step 2b: the `deck-record` intercept, pinned by `codex/test/deck-record-contract`, so the documented pattern works on arm64 rather than only the raw builtins.

**`deck-record` IS STILL A NO-OP ON ARM64, and finishing the five builtins did not change that.** This is the trap in this row: the bracket exists but nothing connects it to the pattern Codex actually writes. `deck-record` is the IDENTITY function in source (`PhaseAllocator.codex:50`, body `x`), so all of its meaning lives in the emitter, and **the arm64 plug does not recognise the name at all** -- measured, zero occurrences of `deck-record` anywhere under `codex/plugs/arm64`, and the same for riscv. x86-64 intercepts it at `X86_64Compound.codex:150` (gated on `st.deck-record-intrinsic`) and gives it a sentinel arity at `X86_64.codex:644`. Without an equivalent, a `deck-record` call on arm64 compiles to an ordinary call to an identity function: the argument is evaluated on the bivy, nothing brackets it, and no diagnostic says so. **Step 2b is that intercept**, and it is the step that makes the documented pattern work rather than only the raw builtins. It is separate because it changes how a user-defined function call is emitted, which is a different risk from adding a builtin arm.

So the honest state of this lane: **arm64 now has the cells, the bracket AND the entry point, and so does riscv (step 3).**

**Step 3 corrected the premise this row was written on. riscv did not LACK the deck builtins, it had STUBS that answered zero**, which is the worse half of the two: `__deck-pos` answered 0, `__deck-set` ignored its argument, `__deck-alloc` was a plain passthrough, and the bracket was two more zeros. `phase-compact` on that lane was `__heap-restore 0` and nothing said a word. "Has none of the three" reads as absent and sent the estimate at the wrong problem; the dispatch arms were present and lying, so the work was replacing five bodies rather than adding five arms.

The cells are at `#80095000`, argued against this lane's fixed map (`RiscVRuntime.codex` "Block device helpers"): above the block rings and the `fs-elevated` cell ending `#80094108`, below the effect-op slots at `#80100000` and the heap from `#80100200`, and far above the measured image end `0x80015498`. `s1` is this lane's allocation pointer as `x28` is arm64's.

**The name-path routes are the half a port would miss.** `__deck-pos`, `__deck-enter` and `__deck-exit` are NULLARY, so they arrive as a bare `IrName` and never reach the builtin dispatch at all: without a route in `rv-emit-name` they fall through to `rv-emit-call-to` against a symbol the runtime never records. The builtin arm and the name path are two different doors and a value builtin needs both.

**The gate is the part to carry to riscv, not just the intercept.** `X86_64Chapter.codex` records why it exists: the intercept once fired on the name alone and a plug kernel emitted the bracket against a phase allocator it never initialized. The discriminator is the DEFINING chapter, so the intrinsic is the `deck-record` that sits beside `init-phase-allocator` and any other bundle gets the plain identity call it declared. Measured on arm64 2026-08-18, both directions from one source with one definition removed: with `init-phase-allocator` in the chapter the argument evaluates on the deck, without it the call is a plain identity and the argument evaluates on the bivy. `X86_64.codex:644` also keeps `deck-record` out of the leaf-call fast path; that half has no counterpart on either lane, neither has a leaf-call optimizer.

**Two corrections to this row, both measured 2026-08-17, and the first one is why it was sized wrong.**

**There is no region and no allocator to build.** A deck is a WATERMARK inside the same bump heap, not a second arena. `build` (`PhaseAllocator.codex:31-37`) does `__heap-save`, `__deck-set p`, `__heap-advance size`; `seal` is `__heap-advance 0`, a marker; and `phase-compact` is literally `__heap-restore (__deck-pos)` (`:131`). Everything below the mark survives, everything above is reclaimed. All the state that takes is three 8-byte cells, so the paragraph below about the region's placement being constrained by the VirtIO DMA clearance **does not apply** -- there is nothing to place but 24 bytes. `x28` is this lane's allocation pointer as `R10` is x86-64's. That premise, an allocator rather than a patch, is what this row was deprioritised on.

**The table below is wrong about three of the five, in the dangerous direction.** `__deck-set` and `__deck-alloc` take an argument, arrive as applications, and were genuinely stubbed as described. `__deck-pos`, `__deck-enter` and `__deck-exit` are **VALUE builtins** (`bs-type` is `int-ty-default` / `NothingTy`, not a `FunTy`, `Builtins.codex:198,201,202`), so they are never applied and can never reach the call-path dispatch their stubs live in. Their arms were **unreachable dead code**. What actually happened is that `a64-emit-name` (`Arm64CodeGen.codex:953`) checks locals, effect ops, `read-line`, then type, and falls through to `a64-emit-call-to`, so they emitted **unresolved symbol calls** -- and the plug prints its own warning saying that reads a stale x0. That is worse than a literal 0, because a stale register can be ACCIDENTALLY RIGHT: two of the four lines in `deck-cell-contract` printed the correct answer on arm64 before the fix, purely because x0 still held the value from the preceding `__heap-save`. A pass/fail probe would have said only "broken"; the four distinguishable states are what separated "stubbed to zero" from "unresolved, reading garbage" (L-STATES). Fixing the stubs alone would not have worked, and nothing in this row hinted that a name-path route was needed.

The test is a CONTRACT test rather than a survival test deliberately. Asking the survival question means calling `__heap-restore (__deck-pos)`, and on a lane where `__deck-pos` answers zero that sets the allocation pointer to zero and takes the rest of the program with it, so the lane without the cells would crash instead of reporting. Verified both directions: it passes on x86-64, and before the fix it failed READABLY on arm64. Regression: full arm64 cross battery 421 pass / 31 fail with the **FAIL set identical** to the 418/31 baseline, the +3 being this test plus `call-clobber` and `list-tail-empty` from other lanes' merge-downs.

Codex says "this value must survive a phase compact or a `__heap-restore`" by wrapping it in `deck-record`, and `codex/compiler/Core/DiagnosticBag.codex:31-35` is the worked example. `deck-record` is the IDENTITY function (`codex/compiler/Core/PhaseAllocator.codex:50`, body `x`); all of its meaning is in the emitter. **Both cross lanes emit nothing for it, silently**, so code following the documented pattern is unprotected there and no diagnostic says so.

It is not one missing intrinsic. `emit-deck-record-wrapper` (`X86_64Compound.codex:1779-1787`) is a BRACKET -- `__deck-enter`, evaluate the argument, `__deck-exit`, return the value -- and the family underneath it was stubbed on both lanes. **Re-measured 2026-08-17 AFTER arm64 step 2; every arm64 number below moved twice, once per step. Do not carry either column forward without re-measuring it.**

| builtin | arm64 `Arm64CodeGen2.codex` | riscv `RiscVCodeGen2.codex` |
|---|---|---|
| `__deck-pos` | `:1567` DONE, `a64-emit-deck-pos` | `:921` literal 0 |
| `__deck-set` | `:1568` DONE, `a64-emit-deck-set` | `:922` literal 0 |
| `__deck-alloc` | `:1569` DONE, `a64-emit-deck-alloc` | `:923` returns its SIZE argument |
| `__deck-enter` | `:1570` DONE, `a64-emit-deck-enter` | `:924` literal 0 |
| `__deck-exit` | `:1571` DONE, `a64-emit-deck-exit` | `:925` literal 0 |

All five are in the arity tables as well (`Arm64CodeGen2.codex:1257-1261`). **Three of the five are VALUE builtins and are NOT reached from the table above at all**: `__deck-pos`, `__deck-enter` and `__deck-exit` have no arguments, are never applied, and reach the emitter only through their name-path routes in `a64-emit-name` (`Arm64CodeGen.codex:957-959`). Whoever ports riscv needs both halves; the dispatch arms alone are unreachable and will read as working. That is the single most expensive thing this row has taught, and it cost step 1 the time twice.

**The x86-64 shape is the pointer for whoever takes this.** A deck is a second bump region whose position lives in a fixed cell: `emit-deck-bump` reads `deck-pos-addr`, adds the size, writes it back; `emit-deck-enter-builtin` / `emit-deck-exit-builtin` switch which region an allocation lands in, and `X86_64.codex:644` gives `deck-record` its sentinel arity so the call is recognised rather than emitted. **The rest of this paragraph used to say porting needs a region whose placement is constrained by the VirtIO DMA clearance (`docs/Designs/Active/OS/OracleCloudArm64.md`, the assertion at `build/build-arm64-img.ps1`), which made it `docs/ArchitectsSketchbook.md`'s subject. That is the premise the correction above falsifies and it is why this row was sized and deprioritised wrongly.** There is no region: the deck is a watermark in the bump heap that already exists, the position cell is 8 bytes, and step 1 shipped all three cells inside the fixed map with no placement question at all. Read the correction, not this paragraph, for what the remaining work costs.

**Why it is latent.** `__deck-alloc` returning a size where the caller wants a pointer would be severe, but `__deck-alloc`, `__deck-pos` and `__deck-set` have **no callers under `codex/foreword`, `codex/os` or `codex/test`**: only the compiler's own phase allocator reaches them, and the compiler does not self-host on either lane. The one live consequence is the one that surfaced it -- the ARM64 serve loop could not keep its state across the loop's heap discipline, so the fix there had to drop the `__heap-restore` instead of annotating around it.

## 1.36 -- the five text builtins are not only MISSING in places, they are INLINED AND WRONG in others

Found by reek 2026-08-17 while closing 1.31, and it is the reason that item's
census reads greener than the tree is. A plug that INLINES a builtin never
spells its name, so every census in 1.31 and every check that asks "is there
an arm" reads it as covered. Five measured instances, from source, in plugs
1.31 otherwise left alone:

| plug | name | emits | Codex answers |
|---|---|---|---|
| `scheme` | `text-to-integer` | `(string->number s)` | `#f` for "12ab", "ab", ""; Codex gives 12, 0, 0, and the caller then hands `#f` to `number->string` |
| `scheme` | `text-contains` | `(string-contains a b)` | SRFI-13, and the emitted header imports `(scheme base)` only, so the identifier is unbound |
| `pascal` | `text-to-integer` | `StrToInt(s)` | RAISES `EConvertError` on the same three inputs |
| `pascal` | `text-contains` | `(Pos(b, a) > 0)` | `False` for an EMPTY needle where Codex answers `True` |
| `clojure` | `text-to-integer` | `(Long/parseLong s)` | THROWS `NumberFormatException` on the same three |

**Red's framing, 2026-08-17: semantic parity of the five text builtins
against Codex, one arm per builtin per plug, across the 43 text plugs.
Census first, then fix as you go, one plug family per CL.** The census is
below and the campaign is in flight.

### The instrument, because a name census cannot do this job

`codex/plugs/test-input/builtin-reach.codex` cannot reach its own Text
section on most of these plugs; it dies at `br-abs`. The subject used
instead is a 29-row probe over the five names alone, and its rows are chosen
to separate a correct arm from a plausible library call:

| row | Codex answers | what it separates |
|---|---|---|
| `sw-longer`, `sw-empty` | F, T | a prefix longer than the string, an empty prefix |
| `ct-empty` | T | `Pos`-style search that answers 0 for an empty needle |
| `rp-empty-old` | `abc` | inserting between every character, or THROWING |
| `rp-dollar`, `rp-backslash` | `$&b`, `\1b` | a replacement read as a TEMPLATE |
| `rp-dot-needle` | `aXb` | a needle read as a PATTERN |
| `sp-empty-sep` | 1 | splitting into characters, or THROWING |
| `sp-trail` | 2 | a split that drops the trailing empty field |
| `sp-dot`, `sp-pipe` | 3, 2 | a separator read as a REGULAR EXPRESSION |
| `ti-trail`, `ti-junk`, `ti-empty`, `ti-dash` | 12, 0, 0, 0 | a parse that throws or answers null |
| `ti-interior` | 1 | a parse that SKIPS non-digits instead of STOPPING |

The last row is there because the probe started at 21 rows and passed `zig`,
whose `cx_text_to_integer` skipped rather than stopped and answered 12 for
"1a2". **The instrument only got that right on its second version, which is
the argument for keeping every row above rather than trimming it.**

Eight plugs have a runtime on this box and are EXECUTED against codex-vm:
typescript and html and javascript under node 24, python and gtk under
python 3.11, zig under zig 0.16, csharp under .NET 9 (and winforms, wpf and
maui as extracted C#), and wasm under wat2wasm plus wasmtime. Everything else
is read against the language and says so in its CL. **Check for the toolchain
before assuming there is none**: `wat2wasm` and `wasmtime` are installed here
and this campaign nearly wrote wasm off as unrunnable without looking.

### Measured 2026-08-17, and RE-MEASURE rather than trust this (L-COUNT)

**`text-starts-with` was published here as "at parity in every plug
measured" and that was WRONG.** `ada` lowered it to
`(Index(A, To_String(B)) = 1)`, and `Index` answers 0 for an EMPTY pattern,
so `text-starts-with s ""` answered False where Codex answers True (fixed,
main 16466). The claim generalised over a column of the table below without
asking whether any plug's implementation of that column had an empty case.
It is the same over-claim this row catches other censuses making, one level
up. Corrected reading: **starts-with is the least divergent of the five, and
`ada` is the one plug where it diverged.** The failures fall into six
classes:

| class | example | plugs seen |
|---|---|---|
| parse THROWS on a non-numeric tail | `toInt`, `Long.parseLong`, `long.Parse`, `int_of_string`, `StrToInt`, `read`, `parseInt`, `Int(...)!`, `to!long`, `'Value` | ada, clojure, csharp, d, elixir, groovy, haskell, javascript, julia, kotlin, nim, ocaml, pascal, python, rust, scala, swift |
| parse answers null or 0 for a value | `tonumber` (nil), `strconv.Atoi` with the error discarded (0 for "12ab") | lua, go |
| parse SKIPS non-digits instead of stopping | zig `cx_text_to_integer` | zig |
| empty needle inserts between every character, or throws | `String.replace`, `str.replace`, `replaceAll`, `gsub`, `Str.global_replace`, `Data.Text.replace` (throws), .NET `Replace` (throws) | clojure, csharp, elixir, go, groovy, haskell, javascript, julia, kotlin, lua, nim, ocaml, python, ruby, rust, scala, and 1.31's fifteen |
| separator or needle read as a REGEX or PATTERN | scala `String.split`, ocaml `Str`, lua `gsub`/`gmatch`, java `split` (avoided in 1.31) | scala, ocaml, lua |
| split drops the trailing empty field, or splits an empty separator into characters, or throws | `split`, `explode` (throws), `componentsSeparatedByString:` (throws), `splitOn` (throws) | elixir, go, haskell, javascript, kotlin, objc, ocaml, php, python, ruby, rust, scala |

At parity as found, and left alone: `objc` and `php` and `ruby` and `perl`
for to-integer; `objc` and `php` and `swift` for replace; `csharp` for split.

### The perl name bug, and the sweep that says it is NOT a family

Found while censusing this item and fixed at main 16409, because it made
every parity verdict for that plug meaningless: **the perl plug emitted
`\&name`, a Perl code reference, for every variable it bound.** Its own
`lookup-arity` answers **-1** for a name that is not a definition
(`PerlEmitter.codex:87` and `:89`), while `emit-pl-name` tested only
`ar == 0` and let everything else fall to the code-reference branch at
`:161`. A parameter is not a definition, so it took -1, so it was not 0.
The arity map means three things and the emitter asked about two.

**Swept for the same shape across every plug, 2026-08-17, and it is ONE
instance rather than a pattern.** The `-1` and `0` answers for an unknown
name really do differ between plugs (`-1` in ada, clojure, cobol, d, elixir,
fortran, groovy, gtk, haskell, nim, ocaml, pascal, perl, php, python, ruby,
rust, scala, zig; `0` in angular, electron, go, html, qt, react, wpf and the
rest of the TS family), but no other plug turns that answer into a different
KIND of emission: they all emit a bare sanitised name for `IrName` and use
the arity only to decide whether to split an apply chain, where 0 means "do
not split" and is the safe default. Recording the negative so nobody sweeps
it twice.

### Done, and what remains

Landed: main 16409 (python, javascript, csharp, zig, plus the perl name fix)
and 16416 (kotlin, scala, clojure, groovy). The pattern in every one is the
same and is the item's method: **a named prelude helper, never an inline
library call**, so the arm cannot evaluate an argument twice and the
semantics sit in one readable place.

Landed since: 16426 (go, rust, nim, d), 16436 (lua, ruby, php, elixir),
16443 (haskell, ocaml, julia), 16450 (objc, swift, pascal, scheme).
`text-contains` diverged in exactly two plugs anywhere, `pascal` and
`scheme`, and both are closed.

**NOTHING REMAINS. `cobol` is the last plug and it is at parity on all 21
probe rows (reek, 2026-08-18); ada's `text-split` was already closed and the
claim that it was a stub was stale.** The campaign is spent as a work item.
What is left in this row is its INSTRUMENT and its six failure classes, which
other rows cite, so it is kept as a closed record rather than deleted.

`cobol` needed a list of TEXT before `text-split` could exist at all: the
numeric list landed at 16925 with a `PIC S9(18) OCCURS` table, and an element
that is itself a length-carrying pair does not fit it. The text flavour is a
NESTED table, `-EL` and `-ED` per occurrence, so an element reads as
`<v>-ED(i)(1:<v>-EL(i))`, and `list-at` copies the pair out into a text group
of its own so everything downstream sees the ordinary `-L` and `-D`.

Hand-traced against x86-64 on all 21 rows, every one correct: `text-to-integer`
STOPS at the first non-digit rather than skipping (`"1a2"` is 1, not 12, which
is the row that caught `zig`), `"12ab"` is 12, and `""`, `"ab"` and `"-"` are
0; `text-starts-with` answers True for an empty prefix and False for a prefix
longer than the string; `text-contains` answers True for an empty needle;
`text-replace` leaves the string unchanged for an empty needle and treats
`$&`, `\1` and `.` as literals; `text-split` keeps the trailing empty field,
answers a one-element list for an empty separator, and reads `.` and `|` as
literals. The scans are explicit for that last reason: COBOL's `UNSTRING`
cannot express the empty-separator or trailing-field cases.

Open in `cobol` and recorded rather than guessed: appending to a list of TEXT
refuses, and `text-contains` inspects the full 256-character field rather than
the slice, so a needle made only of spaces would match the padding. Neither is
in the 21 rows.

`ada` and `wasm` were also on this list and are closed; the entries below
record what each turned out to
cost, because in both cases the first reading of the cost was wrong. Measured
2026-08-17, re-measure before trusting it (L-COUNT):

- **`wasm` is CLOSED (main 16482), and the entry above was right about the
  arm and wrong about the cost.** `text-to-integer` did emit `(local.get $s)`,
  the identity on a string pointer, and the other four had no arm at all. But
  the plug already had a text representation (`[i32 length][bytes]`), a list
  representation (`[i32 count][i32 capacity][i64 elements]`), `$bump_alloc`,
  `$substring` and `$list_push`, so the work was four functions over one new
  primitive (`$cx_text_match_at`, whether `sub` occurs at an offset) rather
  than a runtime to grow. **This entry read "not a guard-sized fix" from the
  arm alone without looking at what the plug already had**, which is the same
  mistake in miniature that this row exists to record. wasm is EXECUTED:
  `wat2wasm` and `wasmtime` are both on this box, and the emitted module
  agrees 29 of 29 with codex-vm.
- **`ada`: ALL FIVE ARE FIXED. `text-split` closed separately, and the last
  paragraph of this bullet was stale when it was written (reek,
  2026-08-18).** It said `text-split` was a stub returning its first
  argument, and that giving ada a real split needed a list representation it
  did not have. `Cx_Text_Split` has been fully implemented for some time,
  returning `Cx_Text_List`, and measured through the plug
  `list-length (text-split s ",")` and `list-at (text-split s ",") 1` both
  emit legal Ada and answer 3 and `b` for `"a,b,c"`, which are the x86-64
  answers. The stub is gone; nobody had re-measured it.

  **What WAS missing is the integer list, and that is now closed too.**
  `ada-type` answered `Long_Long_Integer` for `ListTy`, so an integer list
  was DECLARED a scalar while literals were emitted as bare aggregates, and
  the row's own example was right about the emission being illegal:

      return Long_Long_Integer((10, 20, 30, 40)'Length);
      return (10, 20) & (N)(Integer(2));

  `ListTy` now answers `Cx_Int_List` or `Cx_Text_List` by element kind, both
  declared `array (Natural range <>)`, and literals are qualified
  (`Cx_Int_List'(10, 20)`), which is what gives an aggregate a type.

  **The list operations go through PRELUDE FUNCTIONS rather than attributes,
  and that is required rather than tidy.** Ada's `indexed_component` needs a
  *name* as its prefix, and a parenthesised expression is not a name, so
  `(A & B)(I)` does not parse. The first version of this change added parens
  around the `list-at` operand and thereby BROKE the text case, which had
  been legal precisely because `Cx_Text_Split(...)` is a function call and a
  function call IS a name. `Cx_Int_At`, `Cx_Text_At`, `Cx_Int_Len`,
  `Cx_Text_Len`, `Cx_Int_Snoc` and `Cx_Text_Snoc` take any expression as an
  argument, so every shape works and `'Length` precedence stops mattering.
  `Cx_Int_At` and `Cx_Text_At` are bounds-guarded and answer 0 or `""`.

  `list-insert-at` and `list-set-at` were SILENT NO-OPS, each emitting its
  first argument unchanged, and now emit `cx_UNSUPPORTED_builtin` so 1.21's
  runner names them. They need slicing and are open.

  Against `plug-oracle-arith` the entire removed set is five lines, every one
  of them the illegal aggregate form; the prelude functions and the new call
  forms are additive and nothing else moved.
  The four that are closed were: starts-with and contains via `Index`,
  which answers 0 for an EMPTY pattern; replace via `Replace_Slice` over
  `Index`, which replaced ONE occurrence, raised when the needle was absent,
  and evaluated `Index` three times; and `Long_Long_Integer'Value`, which
  raises on a non-numeric tail.
- **`cobol` moved to its own staged row, 1.39. Read that one, not this
  paragraph.** What stood here was measured before 16497 and before the two
  defects 1.39 records: `text-length` has been `FUNCTION TRIM`-based since
  16497 and `text-starts-with` and `text-contains` have arms, so this
  bullet's account of what is missing is wrong in three places. What
  survives is that a space-padded `PIC X(256)` cannot represent a text
  ending in a space, which is the same root as `fortran`'s
  `character(len=256)` in 1.7, and that `text-replace` and `text-split`
  still have no arm. There is no COBOL toolchain on this box (`cobc`
  absent).

`fortran` and `babbage` are excluded for the reasons 1.7 and 1.37 give.

`text-to-integer` is the one to expect everywhere: Codex's `parse-decimal`
takes an optional leading minus then digits UNTIL THE FIRST NON-DIGIT and
answers 0 when there are none, and almost every target's library parse either
throws or answers a null on exactly those inputs. `winforms` carries the same
shape under the adjacent name `text_to_int` (`long.TryParse`, so 0 where
Codex gives 12 for "12ab"); that name is a sixth builtin and outside 1.31.

What is missing: run the 21-row probe 1.31 used against every plug's INLINE
arms as well, not only the ones that were bare. The probe is the cheap part;
it is `codex/plugs/test-input/builtin-reach.codex` narrowed to the five names
with the edge cases spelled out, and 1.31's account says which six plugs have
a runtime on this box to execute it against.

## 1.39 -- cobol, staged: all five stages landed; the toolchain is what is left

Opened 2026-08-17 (reek) on red's routing, which was "a staged row for a
length-carrying text representation, the 1.36 cobol residue". Measuring
through the plug before writing the row moved the starting point twice.

**1.36's cobol bullet is STALE and should be read from here instead.** It
says `emit-cobol-builtin-text-length` is `COMPUTE n = FUNCTION LENGTH(v)`
and that `text-starts-with` and `text-contains` have no arm. Both landed at
16497: length is `FUNCTION LENGTH(FUNCTION TRIM(v TRAILING))` with the
all-spaces case separated, and both predicates have arms built on it. What
survives from that bullet is the representation, and the reason is narrower
than "no length": a text that genuinely ENDS IN A SPACE is indistinguishable
from the padding.

**Stage 1 is DONE (16587): the plug bound no names at all.** Measured on two
probes, and it is not a text problem:

    01 WS-TWICE-N  PIC S9(18).             declared
        MOVE 10 TO WS-TWICE-ARG0           written at the call site
        ADD WS-N 1 GIVING WS-TWICE-SUM-1   read in the body

Three names for one parameter, two of them declared nowhere; a `let` was
written to `WS-TWICE-A-2` and read as `WS-A`. **No cobol program with a
parameter or a binding could compile**, so every arm downstream was
unreachable and no probe of the text builtins could have meant anything.
`CobolState` now carries an environment: parameters seed it from the def,
`let` and do-bind extend it, a `let` restores the depth it found so a
sibling branch cannot read a binding it never made, and `ArityEntry` carries
the callee's parameter names so a call site writes the slots the callee
declares. The TCO path already named slots that way and was the one path
that was right.

**Stage 2 is DONE (16593): a tail call overwrote a parameter another
argument still read.** `down (n) (acc) = if n <= 0 then acc else
down (n - 1) (acc + n)` emitted `MOVE ...-DIFF TO WS-DOWN-N` and then
`ADD WS-DOWN-ACC WS-DOWN-N`, so `down 4 0` summed 3 + 2 + 1 + 0 and answered
6 against Codex's 10; a two-parameter swap emitted `MOVE B TO A` then
`MOVE A TO B` and left both holding the old B. Arguments now land in
temporaries and commit together. **This was invisible until stage 1**: the
body read an undeclared name, so nothing could be compiled to be wrong.

**Stage 3 is DONE (16653): a text is a group item.**

    01 WS-FOO.
      05 WS-FOO-L      PIC S9(18).
      05 WS-FOO-D      PIC X(256).

A group `MOVE` between two of them copies a whole text in one statement, so
assignment, parameter passing, returns and record fields were unchanged in
shape; the live characters are `-D(1:-L)`. The type comes from
`ir-expr-type` on the node, so no notion of a value's type was added to
`CobolExprResult` and no symbol table was needed. Two COBOL rules shaped
the arms: a reference modification of length zero is illegal, so every read
of the slice is guarded by its length, and a concatenation uses `STRING`
`WITH POINTER` so an empty operand contributes nothing instead of raising.

**Stage 4 came with it and is DONE**, because the representation forced it.
`text-length` is now the `-L` field rather than
`FUNCTION LENGTH(FUNCTION TRIM(...))`, and `substring` clamps against `-L`
and sets the result's own. Measured through the plug on a subject whose
text is not constant-folded (`tail-space (s) = s & " "`, then length,
concat, equality, substring and starts-with over it): length answers 3 for
`"ab "` where the trimmed reading said 2, `"ab " == "ab"` is now False on a
length test before any character comparison, and `substring 0 3` keeps the
trailing space. **The trailing-space case is closed.**

Two arms had to move with the representation rather than after it.
`show` writes a numeric-edited item and trims it, which also closes the
eighteen-leading-zeros defect recorded below; and a text literal is
materialised into a group with its length known at emit time, which closes
the reference-modification-of-a-literal defect below.

**What the representation still does not close.** Ordering comparisons
(`<`, `>`) on text compare the padded `-D` fields, which is COBOL's
alphanumeric collation and is wrong for operands differing only in trailing
spaces; equality is exact. Text arriving from OUTSIDE the representation
carries no length, so `read-text` still trims (`cobol-len-trimmed`), and
the file arms are stubs anyway. `DISPLAY` of an empty text emits
`DISPLAY SPACE`, one space where Codex prints an empty line, because COBOL
has no zero-length display operand.

**Stage 5 is DONE: `text-replace` has an arm, and `text-split` stops lying.**
Neither had an arm, and the consequence was not "no arm" but a WRONG ANSWER:
an unknown name falls to `emit-cobol-call-def`, `lookup-arity` answers -1,
and the fallback emits the FIRST ARGUMENT. `text-replace s old new` answered
`s`, which is the same shape this row caught ada's `text-split` stub making,
one plug over.

The semantics were measured through the seed compiler rather than read off
`__str_replace`'s assembly: an empty needle answers the subject unchanged,
needle and replacement are both LITERAL (`"aXb"` replacing `"X"` with `"$&"`
gives `a$&b`, and `.` is not a pattern), and every NON-OVERLAPPING occurrence
is replaced left to right advancing by the needle length, so `"aaaa"` with
`"aa"` for `"b"` is `"bb"`. `INSPECT ... REPLACING` cannot serve because it
requires its two operands to be the same length, so the scan is written out.
Both appends are bounded against the 256-byte data item and the result length
is clamped, because a reference modification past the item's size is not
diagnosed by every COBOL.

`text-split` returns `List Text` and this plug has no list representation at
all (`IrAppendList` and `IrConsList` already emit "not supported", and
`emit-cobol-list-var` allocates a scalar per element and answers the last).
Giving it a real split is 1.7's class of work, not an arm, so it takes the
plug's existing not-supported stub: it refuses loudly at run time and answers
0 rather than answering its first argument.

**The remaining `else` in the dispatch is no longer implicit.** Appending two
names to `cobol-builtin-names` would have made the old bare `else` swallow
them into `text-contains`; `text-contains` is an explicit index now and the
fallback is `text-split` alone.

**Measured on the way and NOT part of any stage above**, so they are not
rediscovered as surprises:

- ~~`show n` answers eighteen digits with leading zeros~~ FIXED by stage 3.
- ~~`substring "abcdef" 1 3` reference-modifies a LITERAL~~ FIXED by stage 3:
  a literal is materialised into a group before any arm indexes it.
- A record is declared ONCE globally under its type name and `let p = ...`
  copies it into a `PIC S9(18)`, so two live values of one record type share
  storage and a record-typed binding is truncated. Measured on `record.codex`
  while checking the nested text field; it is a record problem, not a text
  one, and no stage above owns it.
- A match arm binds nothing: `IrCtorPat` ignores its sub-patterns and
  `IrVarPat` becomes a bare `WHEN OTHER`, so a pattern variable is unbound
  the way parameters were before stage 1.
- The prelude's `Tup2` through `Tup5` type defs are emitted into every
  program's working storage whether or not the subject mentions a tuple.

**THE LIST BUILTINS REFUSE NOW (16837), and what they did before is the
reason to read this paragraph.** `list-at`, `list-length`, `list-push` and
`list-snoc` had no arm, and the fall-through did not emit a call COBOL cannot
resolve, which is what this quire's standing hazard predicts. It emitted a
paragraph that runs:

    LIT_LEN-PARA.
        MOVE 10 TO WS-LIT_LEN-LIST-4
        MOVE 20 TO WS-LIT_LEN-LIST-5
        MOVE 30 TO WS-LIT_LEN-LIST-6
        MOVE 40 TO WS-LIT_LEN-LIST-7
        MOVE 0 TO WS-LIT_LEN-RET

Four literals moved nowhere and **0 returned where Codex answers 4**;
`lit-at [10,20,30,40] 2` gave 0 for 30. Five such sites in
`plug-oracle-arith` alone. That is 1.37's babbage failure in a second plug,
and no exit code could tell it from a working translation. The four names
route to `emit-cobol-builtin-bit-stub` now, the refusal this plug already
used for `bit-and` and `text-split`, and `run.ps1` greps for it and exits 6.

Appending to the names list also moved the trailing `else`, which WAS the
`text-split` arm. `idx == 23` is explicit now and the final `else` refuses by
name. That is 1.21's class and it was live here.

**THE COBOL LIST REPRESENTATION IS DONE (reek, 2026-08-18) and the refusal
is gone.** A list is the same length-carrying group as text with a TABLE in
place of the character field:

    01 WS-X.
      05 WS-X-L      PIC S9(18).
      05 WS-X-D      PIC S9(18) OCCURS 256 TIMES.

`OCCURS` is COBOL's only array and is fixed at compile time, so the capacity
is a constant and every write is bounded against it: a subscript past the
`OCCURS` is undefined behaviour in COBOL, not a trap. A literal fills the
table and sets the length; `list-length` reads `-L`; `list-at` adds one for
COBOL's one-based subscript and answers 0 outside the length, which is the
off-by-one the pascal plug shipped on every list read it ever emitted;
`list-push` and `list-snoc` both COPY, because an unconditional in-place
append is the defect this quire's 1.7 names and a fixed table has no spare
capacity to exploit anyway.

All five list rows of `plug-oracle-arith` hand-trace to the x86-64 answers
(4, 30, 3, 30, 3) and the subject now emits with ZERO refusals, where before
this it emitted five `DISPLAY ... not supported` sites and, before 16837,
five paragraphs that ran and answered 0.

**BOTH OF THOSE ARE CLOSED (reek, 2026-08-18) and this paragraph used to say
they were open.** `IrAppendList` and `IrConsList` are copy loops over the
`OCCURS` table now, each bounded against the capacity, with `WS-LIDX` as the
shared counter in the pattern `WS-STRPTR` already set for the text arms
(`emit-cobol-bin-stmt` has no state to allocate one from). The binary result
declaration had to learn the list group as well: it chose between text and
`PIC S9(18)` only, so a list result was being declared a scalar.

Traced against x86-64 on four rows, all correct: `[10,20] & [30,n]` has
length 4 and index 3 is `n`; `n :: [10,20]` has length 3 and index 0 is `n`.
**The operators are `&` and `::`, not `++`** -- `OpAppend` lowers to
`IrAppendList` for a non-text type and `OpCons` to `IrConsList`, and `++` is
an arithmetic error the type checker refuses, which cost a compile to find.

A list of TEXT does have a representation now, added for `text-split` in
1.36: a NESTED `OCCURS` table with `-EL` and `-ED` per element. What remains
open is narrower than the old sentence: **appending to a list of TEXT
refuses**, because these two arms copy a single numeric field per slot and a
text slot is a pair.

The next step for the whole row is unchanged: install `cobc`, then run the
subjects. Everything above is read against the language and hand traced.

**There is no COBOL toolchain on this box (`cobc` absent)** and Damian's
standing rule is that no new build environment is installed now, so every
claim above is read against the language and against emitted output rather
than run. The next step for the whole row is: install `cobc`, then run the
subjects.
