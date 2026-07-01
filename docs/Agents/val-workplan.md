# val workplan -- proof system + codegen soundness

**Stream**: //Codex/val (copy-up client BigWhite_Codex_val_main, root
D:\Projects\NewRepository-val-main)
**Date**: 2026-06-30
**Last CLs (main)**: 6455 (Stage 4a induction parse + seed), 6447 (Stage 3
normalizer + seed), 6442 (proofs doc), 6440 (Stage 2 tests), 6438 (Stage 6), 6434 (tco-nested-if in BVT),
6432 (regression test), 6430 (codegen jmp-end fix), 6425 (claim/proof/qed
parser workaround)

(The prior x86/RISC-V codegen-optimization campaign that lived here is
done and recorded in memories [x86 codegen campaign] / [RISC-V codegen
status]. This workplan now tracks the proof-system build.)

## Headline this session

1. **Codegen soundness bug found + fixed (CL 6430).** `emit-if-to-local`
   (and fused/test-mask/generic variants, X86_64.codex) unsoundly elided
   an if's terminating jmp when `last-was-jmp` saw a trailing jmp. Wrong
   when the then-branch is a nested-if whose else ends in a tail-call jmp
   (its then-path reaches the construct's end via the nested jmp-end): in
   a tail-self-recursive if-chain the WHOLE arm was silently skipped.
   This was the true root cause of the claim/proof/qed vacuity bug
   (earlier "dangling-else" label was wrong -- the parse was correct).
   Fix: `tail-is-join` gates the elision. Regression: `tco-nested-if`
   (in BVT) + `proof-qed-vacuous` (un-skipped, CDX2001). Silent
   miscompile the fixed point misses -- see memory
   [codegen-jmp-end / parser-nested-if].

2. **Proof system, the "do all the things" pass.** 6-stage plan in
   `docs/Designs/Language/Active/Induction.md`.

## Proof system stage status

| Stage | What | Status |
|-------|------|--------|
| 1 | sound propositional eq (unify-at + un-degenerate cong) | DONE (6404) |
| 1b | claim/proof/qed threading (was the codegen bug) | DONE (6425/6430) |
| 2 | value-level === (syntactic) | DONE via REUSE (6439/6440) |
| 3 | **normalizer (delta/iota/beta + Fuel) + defeq** | DONE (6447) |
| 6 | assume emits CDX4021 axiom warning per use | DONE (6437/6438) |
| 4a | for-all + induction PARSE, unverified (CDX4022, erased) | DONE (6455) |
| 4b/5 | real induction node + subgoal checking; **add-zero (Nat) green** | DONE (6460) |
| 5a | N-ary constructor congruence (unifier peel); **append-nil green** | DONE (6462) |
| 5b | **FLAGSHIP reverse-reverse** -- nested for-all, app-cong, applicable lemmas, capture-avoiding normalizer | DONE (6467) |
| 5c | parametric-type induction (`Lst a`); `sumty-of` resolves applied ConstructedTy to its SumTy | DONE (6473) |

**Stage 2 was free**: probing showed `TypeCon`/`TypeApply`/`TypeVar`
already are the term language; `reverse (reverse xs) === ...`,
`Cons h t === ...` check soundly and mismatches reject CDX2001. No
CodexType leaf needed. Tests: `value-eq.codex`,
`errors/term-mismatch.codex`.

## DONE: Stage 3 normalizer (CL 6447)

Fuel-bounded delta/iota/beta defeq normalizer, in `TypeChecker.codex`
(Section: Proof Normalizer). Verified: `flip On -> Off` (delta+iota),
`id-bit On -> On` (delta), `flip (flip On) -> On`; proofs erase CDX4020;
the false `flip On === On` rejects CDX2001 (soundness tripwire). One-pass
hard fixed point, BVT green (normalize-eq + errors/normalize-false added).

**Key correction to the design:** integration is at `register-all-defs`
(~line 597, after `resolve-type-expr`, BEFORE `parameterize-type`), NOT
`resolve-declared-type`. At resolve-declared-type the operand names are
already fresh TypeVars (parameterize ran) so delta can't recover the
function name. At register-all-defs the operands are still `ConstructedTy`
with names intact; `mod.defs` is the DefMap (passed as the existing
`defs` param, no UnificationState change). `is-term-ctype` guards to
ConstructedTy operands; a side is replaced only if it reduces to a closed
name/app tree (`is-aterm-normal`), else it falls back to the original
(unproven, never a spurious pass). Bool/Int literal folding + foreword
(cross-chapter) DefMap deferred to Stage 4/5.

## DONE: Stage 4a -- induction parses, unverified (CL 6455)

Parser-only, minimal surface. `for all (x:T), P` parses (parse-forall-type,
-> synthetic Proof NamedType); `induction on x ... qed` parses
(parse-induction-expr, reuses parse-match-expr to consume branches ->
assume-equiv) and is disclosed UNPROVEN via CDX4022, erased CDX4020. No
new AST/CST nodes -- deliberately deferred to Stage 5 so the node lands
with its checker. Test induction-parse.codex (add-zero shape) in BVT.
GOTCHA: token-text = substring(t.source, offset, length); .source is the
whole file, so synthetic tokens use offset 0 and peeks use token-text.

## DONE: Stage 4b/5 -- real induction node + sound checking, add-zero (CL 6460)

Real `AForallType`/`AInductionExpr` nodes threaded through every exhaustive
CST/AST walker. Checker in `TypeChecker.codex` (Section: Induction Checking):
`check-def` intercepts `for all`-claimed `induction` proofs; enumerates T's
ctors from `tdm` (SumTy -> SumCtor{name,fields}); per ctor builds the
ctor-app term with RIGID field vars (resolved via resolve-type-expr, never
parameterize-type -- the crux); subgoal = PropEqTy over the normalized
`subst n:=ctorApp` sides; IH per recursive field bound via env-bind-local;
`infer-expr arm.body` + `unify` against the subgoal. `tdm` threaded into
check-def/check-all-defs. **Essential normalizer fix** (`normalize-app`):
delta-unfold only if the result is-aterm-normal, else keep the application
(rebuild-app) -- without it `add k Zero` (k opaque) unfolds to a stuck
match and the subgoal `Succ (add k Zero)` falls back unreduced, so cong ih
fails. `add-zero : for all (n:Nat), add n Zero === n` PROVEN (erased
CDX4020); `errors/induction-unsound.codex` (Succ by bare Refl) rejects
CDX2001. Both in BVT. One-pass hard fixed point.

## DONE: Stage 5a -- N-ary constructor congruence (CL 6462)

`append-nil : for all (xs:MyList), append xs MyNil === xs` PROVEN over a
binary ctor. MyCons case = `cong ih`, cong solving ?f := MyCons h (partial
2-arg ctor app). Enabler: Unifier `unify-ctor-apply-peel` peels a saturated
`ConstructedTy name [.., last]` as curried `(ConstructedTy name init) last`
vs `TypeApply fb ab` (generalizes the 1-arg arms ~339/388; 1-arg path
preserved so add-zero is unchanged). Tests induction-list (CDX4020 proven)
+ errors/induction-list-unsound (MyCons by Refl rejects CDX2001), both BVT.
One-pass fixed point; full battery 203/0.

## DONE: Stage 5b -- FLAGSHIP reverse-reverse (CL 6467)

`reverse (reverse xs) === xs` PROVEN over a self-contained MyList
(codex/test/reverse-reverse.codex), via the lemma chain append-nil ->
append-assoc -> reverse-append -> reverse-reverse. Four pieces:
- Nested `for all` (checker walks AForallType chain, inducts on named
  scrutinee, other binders opaque; NameResolver adds all binders to the
  proof-body scope).
- `app-cong : forall f g x. (f===g)->(f x===g x)` builtin (function-position
  congruence; TypeEnv + NameResolver + X86_64Builtins).
- Applicable lemmas: elab-claim-apps replaces each maximal claim-application
  spine with a fresh local bound to the claim's prop instantiated at the arg
  terms + normalized (instantiate-claim / aexpr-to-cterm).
- CAPTURE-AVOIDING normalizer (soundness): normalize-app alpha-renames a def
  body's binders that collide with the actuals' free vars (to ~fuel names)
  before substituting. Without it, `append`'s `is MyCons (h)(t)` binder
  captured the `t` in a substituted `reverse(reverse t)`, silently -> MyNil.
All four proofs erase CDX4020; errors/reverse-reverse-unsound (MyCons by
cong ih alone) rejects CDX2001. One-pass fixed point; full battery 206/0.

The proof system now checks the founding flagship. The proof surface
(claim/proof/qed, for-all, induction, Refl/sym/trans/cong/app-cong,
applicable lemmas) is a working little proof assistant over user datatypes.

## DONE: Stage 5c -- parametric-type induction (CL 6473)

`app xs Nl === xs` over `Lst a` PROVEN (codex/test/induction-param.codex).
`Lst a` resolves to an applied `ConstructedTy Lst [a]`, not a bare SumTy,
so check-induction-core fell through to CDX4022. Fix: `sumty-of` resolves a
ConstructedTy head to its variant definition via lookup-type-def before the
SumTy match. Element type is irrelevant to the proof (opaque). ~8 lines.

## Follow-ons -- measured scope (probed against the CL 6473 seed)

- **Literals/arithmetic in props** -- MEDIUM. `claim c : 1 + 1 === 2` fails
  at PARSE: CDX1000 "got '1'". parse-type (which parses both `===` operands)
  rejects integer literals and operators in type position. Fix: extend the
  prop-operand grammar to accept int/text literals and `+ - * /`, resolve
  them to term nodes, and extend the normalizer to fold literal arithmetic
  (normalize-aterm folds AIfExpr on Bool today; ABinaryExpr on two literals
  is NOT folded -- add it). Two files (parser + normalizer), no new AST if
  ALitExpr/ABinaryExpr are reused as term carriers.

- **Proofs over the builtin `List`** -- LARGE, several stacked blockers:
  1. `[]` (empty-list literal) in a prop fails at PARSE (CDX1000 "got ']'").
     List literals aren't valid in type position.
  2. `is Cons (h) (t)` / `is Nil` patterns don't typecheck against `List`
     (the alias) / `ConsList` (raw) -- CDX2001 (DevelopersGuide pitfall).
  3. Cross-chapter DefMap: list-append/reverse live in the foreword, but the
     normalizer's DefMap is only `mod.defs`, so delta can't unfold them.
  Only worth it to let proofs cite foreword list functions directly; the
  self-contained MyList path already covers real proofs.

- **Nested-binder shadowing in the capture-avoiding rename** -- NON-ISSUE
  in practice (I overstated it earlier). Source-level shadowing (an inner
  match binder with the same name as an outer one) is PRESERVED by
  same-suffix renaming: inner still shadows outer, matching source
  semantics. The only capture risk is a substituted actual's free var vs a
  def binder -- the reverse-reverse case -- which is handled (actuals are
  never renamed, def binders are). A gensym counter (thread one Integer
  through normalize-aterm/app/match, replacing the fuel suffix) would close
  a contrived same-fuel sibling-reduction hole, but no real gap exists.

- **Multi-hypothesis / mutual induction, well-founded (non-structural)
  recursion** -- NOT STARTED, large, only if a real proof needs it.

## Gotchas / process

- Codegen changes are NOT one-pass: SUT != stage1 first build, but
  stage1 == stage2; install build/output/NewSeed.cdx -> seed/Codex.cdx
  and rebuild for a clean one-pass. Type-checker/data-only changes ARE
  one-pass.
- BVT (in build.ps1) is the gate. Do NOT run the full battery for normal
  changes (user: "a full pass is only warranted for major changes").
- Copy-up: verify the seed is a fixed point on the main workspace
  (D:\...-val-main\build\build.ps1) BEFORE submitting the copy-up.
- Lane is clear: no other live agent touches Types/ or the proof layer.

## Test commands

```powershell
build/build.ps1                         # text + CDX fixed point + BVT (gate)
build/bvt.ps1                           # BVT alone (~7s after a build)
build/compile.ps1 -Src X -Out Y -Log Z  # one file (-Log mandatory)
build/test-run.ps1 -Kernel Y.cdx -OutFile out   # boot a compiled program
```
