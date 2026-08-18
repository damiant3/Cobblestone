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

**`codex/plugs/zig/` came in with Steve Howell's PR 66 and IS ours to gate**
(red, 2026-08-17, superseding the 2026-08-16 reading that it was not this
lane's to change). Edit it like any other plug; credit PR 66 in the CL
description when the change touches a row that came from it.

## 1.38 -- RISC-V: `from-unicode` inside a SPAWNED process hangs under QEMU and is fine under Renode

`codex/test/fs-spawn-inherits` and `codex/test/scope-runtime-spawn` answer
empty on riscv64 and pass on arm64. The cause is not the filesystem, not
the capability inheritance either test is about, and not `resume` (that was
a separate gap, closed at main 16505).

Reduced to, and this is the whole of it:

| in a spawned child, under QEMU | result |
|---|---|
| `poke-byte 28000 0 9` | works |
| `7 + from-unicode 69` | **hangs** |

`from-unicode` in the PARENT works under QEMU in the same program, and the
identical source with the identical plug runs the child arm correctly under
Renode (`child fu69 46`). So it is not the code and not the plug: it is
this construct, in a child, on that machine.

It presents as a HANG rather than a fault because `rv-rt-trap-handler`
(`RiscVRuntime.codex`, "Trap Handler") saves, restores and `mret`s without
advancing `mepc`, so a faulting instruction re-executes forever. Anything
that traps in a spawned process on this lane looks like silence.

**This is why the cross battery does not see it.** `build/test-cross.ps1`
runs Renode and `build/test-cross-batch.ps1` with it, so every spawn test in
that battery (`spawn-reuse`, `nested-spawn`, `process-exit-status`,
`proc-state-running`, `network-scope-spawn`) is green. Only
`build/test-cross-disk.ps1` runs QEMU, so the only tests that can show this
are the ones with a `.disk` sidecar, and they show it as a filesystem
failure. A reader who runs the battery and sees spawn green will conclude
the opposite of what is true.

EXCLUDED by measurement, so nobody re-walks them: child heap exhaustion
(1024 `block-read-sector` calls in a child are fine), stack depth (20000
frames are fine), the block capability gate (predates it), a user function
call after an act-bind in a child (fine), plain text construction in a child
(fine), and `fat16-extract-name` over a RAM buffer in a child (fine).

Measure with a `.expected` LONGER than the output you expect.
`test-cross.ps1` and `test-cross-disk.ps1` both truncate actual output to
the expected line count, so a hang and a correct run look identical when the
output happens to reach that many lines. That cost a wrong reading here.

## 1.20 -- the pascal plug's HOISTING is closed; four other gaps remain beside it

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
rather than run. A mechanical scan of the emitted `plug-oracle-arith` for
identifiers that are neither declared, a parameter, nor RTL turns up
`list_push` and `list_snoc` and nothing else, so the change added no new
undeclared read.

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

  7. **The lambda hole. NOT STARTED, and it is bigger than fortran.**
     `fort-emit-expr` answers `IrLambda` with its BODY and discards the
     parameter binding, so `(\x -> x + 100) 5` emits `(x + 100_8)(5_8)`:
     `x` is unbound and the result is applied as though it were a
     function. `FunTy` is `integer(8)` for the same reason, which is why
     `lambda_map`'s `f_in` is an integer. Measured unchanged at 1
     occurrence across every stage of this campaign.

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

  8. **Builtins that are STATEMENTS, in expression position. NOT STARTED.**
     `print-line-uni` emits `print *, ...`, `process-exit` emits `stop`,
     `close-file` emits `close(...)`, and `fort_open_file` and
     `fort_write_file` are called for effect. All go through
     `fort-emit-expr`. Stage 5 fixed the effectful-DEF half of this; the
     builtin half is untouched.

  9. **Record fields.** `emit-fort-record-fields` hardcodes `integer(8)`,
     so a record field holding a list or a text is wrong.

  10. **The oracle. RULED (Damian, 2026-08-17): we cannot compile it here,
     and that is accepted.** The standard for this campaign is that a
     reader looking at the emitted Fortran thinks **"this is ready to test
     on a real Fortran compiler"**. That is the bar the seven stages above
     were held to, and by it they pass.

     **NEXT STEP, recorded and not scheduled: run the emitted output
     through a real Fortran compiler.** Whoever has one takes
     `codex/plugs/test-input/builtin-reach.codex` plus the four probes
     named above, and the first real compile is expected to find things
     that reading did not.

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

## 1.31 -- five text builtins have no arm in ~25 text plugs, and a foreword def is all that hides it

Found by root 2026-08-17 while measuring COMPILER-9 stage 3
(`docs/Designs/Done/Compiler/ForewordShadows.md`). `text-starts-with`,
`text-contains`, `text-replace`, `text-split` and `text-to-integer` are
compiler builtins AND foreword definitions; a chapter that cites
`StringUtils`/`TextSearch`/`Parse` reaches the def and is transpiled as user
code, so every text plug works on it today. A chapter that does NOT cite
reaches the builtin, and a plug with no arm emits a bare call to the Codex
name and reports OK. Measured on `typescript` with `test-input/builtin-reach`:
`text_starts_with(a, b)`, `text_contains`, `text_split`, `text_to_integer`
emitted with no definition in the prelude (only `text_replace` is there).
Static string census across the 55 plug directories, re-run before trusting
(L-COUNT): all five registered in 21 plugs; unregistered `text-starts-with`
31, `text-contains` 26, `text-replace` 29, `text-split` 33, `text-to-integer`
24, of which `elf img pe ptx spirv wgsl t3isa` are binary/GPU targets and the
rest are text targets (`angular babbage clojure cobol compose d electron
flutter groovy gtk html java pascal perl qt react scheme svelte swiftui
typescript vue wasm winforms wpf maui julia zig` between the five lists).

The five foreword defs are KEPT as the text-plug fallback (red's ruling
2026-08-17, `ForewordShadows.md`), so a citing chapter is not exposed.

**CLOSED 2026-08-17 (reek), main 16351, 16357, 16361, 16363, 16366, 16368,
16370. Twenty-four plugs given arms.** typescript angular react vue svelte
electron html qt winforms wpf maui java gtk compose flutter swiftui zig d
pascal scheme clojure groovy perl julia. Two plugs are deliberately NOT
closed here because neither is one arm short: `fortran`, which is 1.7's, and
`babbage`, which is 1.37's.

**`zig` is in that list.** It was flagged when it landed at main 16366,
because this file's header said zig was not this lane's to change. **Red
ruled 2026-08-17: zig plug edits are fine, it is in the depot and ours to
gate, and Steve Howell's PR 66 is credited in the CL description wherever a
change touches a row that came from it.** The header above is corrected to
match. What landed: the table entry and prelude body for `text-replace`,
plus the empty-separator fix in `cx_text_split`, executed against codex-vm
under zig 0.16.

**THE CENSUS ABOVE IS NOT A CENSUS OF ARMS, in both directions, and that is
the reusable part.** It keys on the quoted Codex name, `"text-starts-with"`,
which is the shape only a table-registration plug uses. `winforms` declares
`static bool text_starts_with(...)` in a C# prelude and answers ZERO to that
census while being perfectly armed; `zig` answers ONE for `text-replace`
because the string appears inside `@compileError("zig plug: no emitter for
text-replace")`, which is a REFUSAL and the opposite of an arm. What was
measured instead: run one subject through the plug and count the name the
plug chose in its OUTPUT. One occurrence is a bare call with nothing
defining it, two is a definition plus a call, and none at all means the plug
inlines the builtin and never spells it.

**`builtin-reach.codex` cannot do this job on most of these plugs.** It dies
at `br-abs` because their preludes are missing roughly twenty further
builtins before the Text section is ever reached. The subject used instead
was a 21-row probe over the five names alone, carrying the edge cases the
foreword defs actually specify: a prefix longer than the string, an empty
prefix, an empty needle, an empty separator, a trailing separator, and
to-integer against "12ab", "ab", "" and "-". Seven plugs were EXECUTED against
codex-vm on it and agreed 21 of 21: typescript and html under node 24, gtk
under python 3.11, zig under zig 0.16, and winforms, wpf and maui as
extracted C# under .NET 9. The rest have no toolchain on this box and are
census plus plug-smoke only, said plainly in each CL.

**Fifteen of the arms were present and WRONG rather than missing**, every one
of them `text-replace` against an EMPTY needle, where Codex answers the
string unchanged. JavaScript, Java, Python, Kotlin and Dart all insert the
replacement between every character (typescript angular react vue svelte
electron html qt java gtk compose flutter); .NET's `String.Replace` THROWS
ArgumentException (winforms wpf maui), which a control run of the pre-fix arm
under .NET 9 confirmed rather than assumed. Only swiftui's
`replacingOccurrences` was already right. Separately, `zig`'s `cx_text_split`
answered an empty separator with an EMPTY list where Codex answers `[s]`.
All fixed with the arms beside them.

## 1.8 -- a field store as an `act` statement or on a non-name target is not observable through `haskell`, `elixir` or `clojure`

**The `let` shape is CLOSED 2026-08-17 (root):** the wire binds a store's result
to a throwaway `(let "__seq" T (field-store (name c) f v) body)`, so each of the
three emitters now rebinds `c` there instead: elixir `(fn -> c = Map.put(c, :ca,
n); body end).()`, clojure `(let [c (assoc c :ca n)] body)`, haskell `((\c ->
body) (c { ca = n }))` (a `let c = c {..}` would be recursive). Measured on the
emitted text of `codex/test/plug-oracle-arith.codex` through the three built
plugs, `store-one`/`store-two`/`store-untouched` read 55 / 56 / 7 by
inspection; no runtime for the three is on the box, so the arm is the text.
Left: a store in `act` statement position (`IrDoExec`) and a store whose
target is not a bare name still construct a copy and discard it; the same
rebinding applies to a do-block (`c <- return (c {..})` in haskell shadows for
the rest of the block) and is the next step if a subject needs it.

Swept 2026-08-16 (reek), with the arm that makes it visible:
`codex/test/plug-oracle-arith.codex` carries a `mutable Cell` and three
rows (`store-one`, `store-two`, `store-untouched`, bare metal 55 / 56 / 7).
`python`, `javascript`, `kotlin`, `ocaml` and `scala` are fixed and thread
the `ARecordTypeDef` mutability flag through to the decoration.

**The divergence, as swept: `haskell`, `elixir` and `clojure` have no mutable
record at all.** Record update, `Map.put` and `assoc` all CONSTRUCT rather than
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

**What is still missing is the plugs the check cannot see.** It models two
registration shapes, a record table (`name = "x"`, python/zig/csharp) and an
if/else dispatch chain (`n == "x"`, javascript/wasm). `pascal` and `fortran`
use a third: they extract 6 and 4 names against python's 94, so any gap
reported against them would be the extraction's fault rather than theirs.
The check refuses on a thin extraction instead of listing phantoms, which
means those plugs are UNCHECKED rather than clean. Teaching it their shape is
the work; until then nothing compares their tables against a wire.

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
## 1.35 -- the six TS/JS-family plugs flatten an apply whose callee arity is unknown on the wire

Over-application closed 2026-08-17 (root, 16343) and under-application of a
KNOWN def closed the same day (root): `typescript`, `angular`, `react`,
`vue`, `svelte`, `electron` split an apply chain at the callee's arity
(`emit-*-apply-split`): first `ar` args flat, the rest applied one at a time;
fewer args than params wraps the flat call in one arrow per missing parameter
(`emit-*-partial-wrappers`, `_p0_`, `_p1_`...), the shape `javascript` already
had. Measured on `codex\plugs\test-input\lambda.codex` and `partial.codex`
through all six: `make_adder(10)(32)`, `((x) => (x + 100))(5)`, `const g =
(_p0_) => add3(1, 2, _p0_)`, `const h = (_p0_) => (_p1_) => add3(10, _p0_,
_p1_)`. Left, and it is the same test-input's last line: a callee whose arity
the arity map does not know (a local, a parameter, a let-bound partial) applied
to several args stays one flat call, `h(20, 12)`, which hands `_p1_`
`undefined`. The arity map is built from `IRDef` params only. Curried types
cannot settle it because a multi-param lambda `\x y -> ...` is emitted as one
n-ary arrow yet carries the same curried type as a partial; the consistent
close is curried lambdas plus curried unknown-callee applies plus an eta-wrap
where a def is used as a value, all six plugs together, and it should be
measured against what `javascript` does for the same three shapes before it is
copied.
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

## 1.30 -- the 17 plugs with their own listener each carry the truncation check separately, and none is measured

`build/plug-run.ps1`'s half is FIXED (val, main 16184). It grepped the
guest's own `TRUNCATED sent=` line in the REDIRECTED STDERR FILE while
booting codex-vm with no `-output`, so the guest console went to the VM's
output ring and was captured nowhere and the check could never match. A
guest dying mid-emission answered `OK` with a truncated artifact on disk.
It now boots with `-output` into a second temp file and greps that, and
the no-output diagnostic prints the guest console beside stderr. Generator
and shipped script moved together; `check-generated-scripts.ps1 -Only
plug-run` reports match, 0 drift.

**The census, and it contradicts what this row first said** (val,
2026-08-17). Of 55 `run.ps1` under `codex/plugs`, 38 share the harness and
17 have a private listener. The row claimed those 17 "each carry the same
three lines separately". They do not: **16 of the 17 carry no truncation
check of any kind.** Only `recheck` had one, and it is fixed (main 16212)
-- its host-side flag catches an ABRUPT disconnect only, so a send the plug
refuses CLEANLY closes the socket normally, the flag stays false, and the
guest's own line was the only evidence.

**Two incompatible guest report shapes exist, and one harness pattern cannot
match both.** This is the part that matters, because the pattern is copied
between harnesses:

- `TRUNCATED sent=N of M ...` -- `javascript`, `wpf`, `rust`, `recheck` and
  the 36-chapter sweep. This is what every harness greps for.
- `OK ... sent=N TRUNCATED ...` -- `pe` (`PePlug.codex:67`), `elf` (`:142`)
  and `img` (`:103`, `:114`), which append the token CONDITIONALLY inside
  the OK line: `(if sent.ns-complete then "" else " TRUNCATED")`. Here
  `TRUNCATED` comes AFTER `sent=`, so the pattern `TRUNCATED sent=` does not
  match it, **and the line begins with `OK`.** A harness reading these three
  sees success on a refused send however carefully it was written.

So the open work is not "add three lines to 16 harnesses". It is: normalise
the guest report to ONE shape first, then add the check only where the guest
can actually report truncation. By that test only five of the 16 need one --
`javascript` and `wpf` (shape 1) and `pe`, `elf`, `img` (shape 2). The other
eleven (`arm64`, `csharp`, `html`, `maui`, `ptx`, `riscv`, `spirv`, `t3isa`,
`wasm`, `wgsl`, `winforms`) stream to the codex-vm output ring or make no
checked TCP send, so there is no refusal for a harness to hear and a check
there would be decoration.

The method is cheap and is the part worth copying, because a check that
now passes is indistinguishable from one that never ran. Sabotage the
PATTERN, not the plug: grep for a line the guest demonstrably DOES print
(`OK defs=`) and see whether the harness fails. Against the depot wiring
that arm exits 0 -- it cannot see a line the guest printed, which is the
defect -- and against the fixed wiring it exits 7 and quotes the guest's own
line back. Keep the broken arm; the pass alone proves nothing (L-FALSIF,
L-ORACLE).

### The img truncation is real, and the guest is not the one lying

Measured val 2026-08-17, box verified quiet first (0 `codex-vm`, 20 logical
processors), `codex/plugs/img/test-img.ps1`, two arms per run.

| arm | runs | result |
|---|---|---|
| rebuild, then 10 runs | 20 | **3 fail.** Run 1 BOTH arms (fat32 13,232,800 and fat16 11,905,600 against 16,777,216); run 2 fat32 short by 16,416; runs 3-10 clean |
| no rebuild, same CDX, 10 runs | 20 | **0 fail** |

**The row's own wording was wrong and is corrected here: this is not
fat32-specific.** It has hit both arms, and the worse loss was fat16.

Severity DECREASES run over run and then stops, so it is a warm-up
gradient rather than a coin flip, and every failure on record arrived in
the first runs after a build. **The two arms do NOT separate the rebuild
from the warmth**, because the no-rebuild arm ran on a box ten runs warm
already; that confound is unresolved and the next experiment is
rebuild-run-rebuild-run, not more repetitions of either arm.

**What the arms DO settle is that the check added above cannot see this
class at all.** The guest printed `OK` on all three failures, so
`is-complete` was True while megabytes were missing. The reason is in the
harness, not the plug:

**`build/plug-run.ps1`'s receive loop cannot tell a complete transfer from
a truncated one.** At `:94-107` the stream gets a `ReadTimeout` and the
whole read loop sits in a `try` whose `catch` is commented *"Read timeout
or connection reset -- normal end"*. A timeout and an RST are therefore
indistinguishable from a clean FIN: the partial buffer is written to
`-Out`, the script prints `[plug-run] OK` and exits 0. **The only failure
this loop can report is zero bytes** (`:111`). Nothing compares the byte
count against anything, although the plug knows the length it meant to
send and could state it.

That is the same defect class as the `:120` grep red routed here, one
layer down and worse: the grep could not HEAR a refusal, and this cannot
NOTICE a truncation even when no refusal was ever made. It also means the
`TRUNCATED sent=` check reads the guest's belief only, and on this
evidence the guest may have been right each time.

Open, and the next step, in order:

1. **DONE as observation (val, 2026-08-17).** The loop now records which
   path ended it and writes a WARNING to stderr when that was the
   exception. **The exit code is deliberately unchanged**, because turning
   the catch into a failure would redden every plug that ends its stream
   with a reset, and that had to be measured before it shipped rather than
   after. Generator and shipped script moved together;
   `check-generated-scripts.ps1 -Only plug-run` reports match, 0 drift.

   **The flag was proved to distinguish the two endings, not just to
   exist.** Two arms against a local socket pair running the same loop
   shape: a peer that sends 64 bytes and closes cleanly, and a peer that
   sends the same 64 bytes and then stalls past the read timeout. **Both
   arms receive 64 bytes; one reports aborted False and the other True.**
   Identical byte counts, opposite verdicts, which is exactly the pair
   that was indistinguishable before and is why a byte count alone could
   never have been the test.
   **The survey that decides the follow-up is done: 40 of 40 built plugs
   that share this harness end the receive loop by CLEAN EOF, none by the
   exception path, and none exits non-zero** (measured 2026-08-17 over
   `builtin-reach.codex`). So nothing relies on the swallow, and turning
   it into a failure reddens nobody.

2. **Turn the warning into a failure.** The survey above clears it. That
   is the CL that closes the hole; the observation only makes it visible.

3. **DONE, and it REFUTED the host-side explanation** (val, 2026-08-17).
   With the refusal in place, four img runs on a verified quiet box
   reproduced two truncations: 15,785,000 and 16,675,400 bytes against
   16,777,216. **Both reported `recv-aborted=False`.** The receive loop
   ended by CLEAN EOF, not by timeout and not by reset.

   So the swallowed catch was NOT what hid this. The peer closed normally,
   the guest printed `OK` with `is-complete` True, and the bytes were
   simply not there. **Both endpoints believe the transfer completed while
   megabytes are missing**, which puts the loss in the transport, in the
   NE2K path or the TCP stack, and not in either harness.

   **The warm-up reading recorded above did NOT reproduce and should not be
   relied on.** The earlier arm had all three failures in runs 1 and 2 with
   decaying severity; this one failed on runs 2 and 4 with 1 and 3 clean.
   Intermittent, yes; clustered after a rebuild, not established.

4. **DONE, and IT IS LOSS IN TRANSIT** (val, 2026-08-17). img's OK line
   already carried `img=<img-bytes>` and `sent=<is-sent>` and the harness
   already captured the guest console for the grep, so **the one number
   that could refuse a silent truncation was being printed and thrown
   away.** `img/run.ps1` now asserts built == sent == received and exits 9
   on disagreement. The failing run says:

   ```
   guest built 16777216, guest sent 16777216, host received 16629200
   ```

   **The guest built the whole image, its send loop accounts for having
   handed all 16,777,216 bytes to the transport, and the host received
   148,016 fewer, with a clean close at both ends.** That eliminates the
   other two candidates outright: the send loop did not stop early, so
   `is-complete` was not lying about its own accounting, and the build was
   not short. The bytes are lost between the guest's send accounting and
   the host's socket.

   **So this is a NetIO or NE2K defect, not a plugs defect**, and it is not
   this lane's to fix. It belongs to whoever owns `codex/os/net`. What the
   plugs lane owed it was an instrument that can see it, and that is now in
   place: any img run that loses bytes exits 9 and names all three counts.

   Two things the next reader needs. `test-img.ps1` swallows
   `img/run.ps1`'s stdout, so the `guest built ...` line is visible only on
   a FAILING run, where it comes back on stderr inside the exit-9 message;
   a passing run prints nothing and that is not a missing instrument. And a
   `codex-vm` from a previous batch was still alive when a later one
   started, so **per-run contention is an untested candidate for the
   intermittency** and it never needed the warm-up story.

`is-complete` cannot be both the value under suspicion and the only thing
asserting success (L-ORACLE).

Also open, found in the same read and not chased: the loop accumulates
with `$allBytes.Add($readBuf[$bi])` one byte at a time, which is 16.7
million `.Add()` calls for a 16 MB image on the receive path of every
binary plug run (R-COST).

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
`Arm64Runtime.codex` now records the new occupant, so 1.33's deck region
starts above `#40022000`.

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

**DEPRIORITISED (red, 2026-08-17): latent, and the prerequisite is an allocator rather than a patch. No work in flight.**

Codex says "this value must survive a phase compact or a `__heap-restore`" by wrapping it in `deck-record`, and `codex/compiler/Core/DiagnosticBag.codex:31-35` is the worked example. `deck-record` is the IDENTITY function (`codex/compiler/Core/PhaseAllocator.codex:50`, body `x`); all of its meaning is in the emitter. **Both cross lanes emit nothing for it, silently**, so code following the documented pattern is unprotected there and no diagnostic says so.

It is not one missing intrinsic. `emit-deck-record-wrapper` (`X86_64Compound.codex:1779-1787`) is a BRACKET -- `__deck-enter`, evaluate the argument, `__deck-exit`, return the value -- and the whole family underneath it is stubbed on both lanes (measured 2026-08-17):

| builtin | arm64 `Arm64CodeGen2.codex` | riscv `RiscVCodeGen2.codex` |
|---|---|---|
| `__deck-enter` | `:1501` literal 0 | `:924` literal 0 |
| `__deck-exit` | `:1502` literal 0 | `:925` literal 0 |
| `__deck-pos` | `:1498` literal 0 | `:921` literal 0 |
| `__deck-set` | `:1499` literal 0 | `:922` literal 0 |
| `__deck-alloc` | `:1500` returns its SIZE argument | `:923` returns its SIZE argument |

All five are in the arity tables as well (`Arm64CodeGen2.codex:1257-1261`), so they resolve, emit, and report nothing.

**The x86-64 shape is the pointer for whoever takes this.** A deck is a second bump region whose position lives in a fixed cell: `emit-deck-bump` reads `deck-pos-addr`, adds the size, writes it back; `emit-deck-enter-builtin` / `emit-deck-exit-builtin` switch which region an allocation lands in, and `X86_64.codex:644` gives `deck-record` its sentinel arity so the call is recognised rather than emitted. Porting it needs a region, a position cell, and the enter/exit switch -- **and the region's placement is constrained**, because the top of the ARM64 stub's allocation is already what the VirtIO DMA constants must clear (`docs/Designs/Active/OS/OracleCloudArm64.md`, and the assertion at `build/build-arm64-img.ps1`). That makes it `docs/ArchitectsSketchbook.md`'s subject.

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

**Remaining: `cobol`, and ada's `text-split` alone.** `ada` and `wasm` were
on this list and are closed; the entries below record what each turned out to
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
- **`ada`: four of the five are FIXED (main 16466), and `text-split` is not
  a divergence but a STUB.** `AdaEmitter.codex` idx 41 emits
  `emit-ada-expr (list-at args 0)`, the first argument unchanged, so
  `list-length (text-split s sep)` came out as `Long_Long_Integer(S'Length)`,
  the length of the string. Giving ada a real split needs a list
  representation it does not have, which is 1.7's class of work and not a
  guard, and the measurement that shows it is `types.codex` through this
  plug: `list-length [1, 2, 3]` emits `Long_Long_Integer((1, 2, 3)'Length)`,
  an aggregate with an attribute applied directly, which is not legal Ada.
  ada has no list TYPE, so there is nothing for a split to return.
  The four that are closed were: starts-with and contains via `Index`,
  which answers 0 for an EMPTY pattern; replace via `Replace_Slice` over
  `Index`, which replaced ONE occurrence, raised when the needle was absent,
  and evaluated `Index` three times; and `Long_Long_Integer'Value`, which
  raises on a non-numeric tail.
- **`cobol` is a TEXT REPRESENTATION, not four arms, and here is the
  measurement that settles it.** Of the five it has ONE, `text-to-integer`
  (`FUNCTION NUMVAL`, which is implementation-defined on a non-numeric tail
  and was not touched); `text-starts-with`, `text-contains`, `text-replace`
  and `text-split` have no arm at all. But the blocker is upstream of all
  four: every text variable is emitted as `PIC X(256)` and
  `emit-cobol-builtin-text-length` is `COMPUTE n = FUNCTION LENGTH(v)`.
  **`FUNCTION LENGTH` of a `PIC X(256)` item is 256 whatever it holds**, so
  `text-length "abc"` answers 256, and every arm that indexes or compares by
  length is wrong before the missing four are reached. That is the same root
  as `fortran`'s `character(len=256)` in 1.7: fixed-width, space-padded text
  with no length. Order of work: a text representation carrying a length,
  then `text-length` and `substring` over it, then the four arms.
  There is no COBOL toolchain on this box either (`cobc` absent).

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

## 1.37 -- babbage has no text at all, so an unknown name becomes an Engine comment

Surfaced 2026-08-17 (reek) as one of the two plugs 1.31 could not close by
adding an arm, because neither is one arm short. **`fortran` is the other
and it is NOT recorded here: 1.7 already owns it**, and owns it better,
with fifteen undefined `fort_*` helpers measured against a wider subject
where this campaign's text probe reached four. Nothing about the text
builtins changes that ordering; read 1.7.

**`babbage` targets the Analytical Engine, which has no text type.** It does
not silently emit a call: it emits `. call to unknown function
text-starts-with` and `. text literal not supported on Analytical Engine` as
Engine comments. Text builtins on a numeric machine are not a missing arm,
and the honest question for this plug is whether an unknown name should
REFUSE the way zig's `@compileError` does rather than emit a comment and a
number. That is the same question 1.21 asks about the builtin catch-all.
## 1.38 -- `__deck-set` is emitted without its argument, and zig will not compile the result

Surfaced 2026-08-17 (Steve Howell, `showell/codex-zig-ladder`, tag
`u46-14of14`). Fixed in the PR that carries this entry.

`ZigEmitter.codex` mapped the builtin to a bare constant and never touched
`args`:

    ZigBuiltinEmitter { name = "__deck-set", emit = \args ctx d ty -> "0" },

Answering `0` is right. There is no deck in the zig target, so pointing the
deck cell somewhere is genuinely a no-op. Dropping the argument is the defect:
the argument is a binding at the call site, and zig refuses to compile a
binding whose only consumer disappeared.

    error: unused local constant
    b2: { const deck_base = cx_heap_save(); break :b2 b3: { _ = 0; ...

The remedy was already in the same prelude, for the same problem, with a
comment that states the rule outright:

    // address-of answers 0 here by X86_64Compound's own account of
    // targets without it; the argument is still evaluated so its binding
    // stays used.
    fn cx_address_of(v: anytype) i64 { _ = v; return 0; }

So this is a second instance of a pattern whose first instance is solved two
lines away. `__deck-set` is the only other entry in the builtins table that
ignores `args` and takes one; `__deck-enter`, `__deck-exit` and `__deck-pos`
also ignore `args` and are all nullary, so the table is now clean.

**Why it stayed latent.** Both callers in the compiler use the address for
something else as well, so the binding survives on its second consumer:

    build (size) =
     let p = __heap-save
     in let deck-init = __deck-set p
     in let guarded = deck-reservation-guard p size      <- p used again

    init-phase-allocator =
     let base = __heap-save
     in let deck-init = __deck-set base
     in base                                             <- and here

The ladder's `whole` rung transpiles both of those to zig and compiles clean.
It took a caller that only sets, which is what the ladder's harness prologue
became once it named `init-phase-allocator` to turn `deck-record-intrinsic` on.

**Worth carrying to the other 43.** A target with no analogue for a builtin
still has to consume that builtin's operands, or it silently changes which
bindings are live. A target whose language does not mind unused locals would
not have reported this at all -- it would have compiled, and the argument
expression would simply not have run. Same shape as 1.21's question about the
builtin catch-all: the cheap wrong answer produces a wrong program with no
diagnostic.
