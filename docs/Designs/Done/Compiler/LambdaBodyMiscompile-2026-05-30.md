# Bug 1 redux: a lambda-bodied def poisons the NEXT def's type-check (reek, 2026-05-30)

## RESOLVED -- CL 2937 (val, 2026-06-01)

**Root cause:** `check-all-defs` ran in deck-bound mode. Inference-time `list-push`
overflow on `substitutions`/`expr-types` grew INTO the deck via `__list_snoc` Path 2
(in-place extension at R10), overwriting ADef records from desugar -- including
`declared-type` fields. The lambda-bodied def's type inference required more
substitutions than a simple def, triggering the overflow that clobbered the NEXT
def's `declared-type` to empty.

**Fix:** CL 2937 wraps `check-all-defs` in `__deck-exit`/`__deck-enter`, so all
inference scratch goes to bivy (above the deck). Deck data (ADef records from
desugar) stays intact.

**Regression gate:** `codex/test/lambda-body-def.codex` -- a lambda-bodied def
followed by another def, both used at runtime.

## TL;DR for the next agent

The "empty-list cross-chapter" framing in `BareMetalMiscompiles-2026-05-30.md` (Bug 1)
was a **red herring**. Bisected from scratch (single chapter, no `cites`, no `[]`):

> **A def whose DIRECT body is a lambda (`\... -> ...`) corrupts shared type-checker state
> so that the NEXT def checked fails.** If the lambda-bodied def is the LAST def in the
> chapter, the program compiles clean. Capture of an enclosing parameter is NOT required.

It is a **LOUD compile error** (`CDX2001 Type mismatch: Fun vs Integer`, usually with a
synthetic span = no line:col), **not a silent miscompile** -- meaningfully less dangerous
than the original Bug 1 writeup implied.

## Evidence (each row = ONE compile, one-at-a-time; build/output/r*.codex, untracked)

Compile: `build/compile.ps1 -Src build/output/<f>.codex -Out build/output/<f>.cdx -Log build/output/<f>.log`
(default mode = CDX; exit 0 = pass; exit 4 = CODEGEN-HALTED, read the `.log`.)

| repro | shape | result |
|---|---|---|
| r8  | make-adder `(x) = \y -> x + y` **ALONE** (only def) | **PASS (exit 0)** |
| r12 | `helper = 5` first, then make-adder `(x) = \y -> x + y` (lambda LAST) | **PASS (exit 0)** |
| r2  | `make-adder (x) = let f = \y -> x + y in f` (+ nothing after) | **PASS (exit 0)** |
| r9  | `f = \y -> y` decl `Integer -> Integer` (0 params), then `opening = f 3` | **PASS (exit 0)** |
| r11 | make-adder `(x) = \y -> x + y` FIRST, then `helper = 5` | **FAIL** `CDX2001 Fun vs Integer` (synthetic span) |
| r10 | make-adder `(x) = \y -> y + 1` (NO capture) FIRST, then `opening = 0` | **FAIL** `CDX2001 Fun vs Integer` (synthetic span) |
| r1  | make-adder `(x) = \y -> x + y` decl fn, then `opening` | **FAIL** `CDX2001 Fun vs Integer` |
| r7  | make-adder `(x) = \y -> y` decl fn, then `opening = 0` | **FAIL** `7:13 Fun vs Integer` (REAL span, at opening's `0`) |
| r5  | make-adder `(x) = \y -> x + y` NO annotation, `opening = (make-adder 5) 3` | **FAIL** `6:14 Integer vs Fun` at USE site |
| r6  | `f = \y -> y` NO annotation (0 params), `opening = f 3` | **FAIL** `CDX2010 Infinite type` at USE site |

Reading the table:
- **r8 vs r11** is the clincher: identical make-adder def; the ONLY difference is whether a
  second def follows. Lambda-def last → PASS; lambda-def followed by ANY def → FAIL.
- **r12 vs r11**: order matters -- the lambda-def poisons LATER defs, not earlier ones.
- **r10**: capture is irrelevant. `\y -> y + 1` never mentions `x` and still fails.
- **r2**: wrapping the SAME lambda in `let f = ... in f` makes it pass -- the bug is in the
  direct-lambda-body path, not in `infer-lambda` per se.
- Two error shapes (likely two faces of one bug): synthetic-span "Fun vs Integer" attributed
  to the lambda-def's own body unify (r1/r10/r11), and a real-span error at the FOLLOWING def
  (r7: opening's `0`), plus use-site failures when undeclared (r5/r6).

## ⚠️ PARAM-LIST-CLOBBER HYPOTHESIS -- REFUTED 2026-05-30 (do not chase it)

Tested r13 = the let-wrapped lambda (`make-adder (x) = let f = \y -> x + y in f`) FOLLOWED
by a second def (`helper = 5`). It **PASSES (exit 0)**. But the direct-lambda version
followed by the same def (r11) FAILS. Both desugar the lambda's param list identically
(`map-list desugar-lambda-param params`, Desugarer.codex:59) -- so if a clobbered param list
were the cause, r13 would fail too. It doesn't. **The param list is NOT the bug.**

Confirmed facts about the machinery (so the next agent doesn't re-check):
- `make-name` (Core/Name.codex:12) does NOT deck-record (returns bare `Name { value = s }`).
- `desugar-lambda-param` (Desugarer.codex:172) returns that bare Name; the param LIST at
  Desugarer.codex:59 is not separately deck-recorded. This is real but NOT the trigger (r13).

## REAL DIFFERENCE: let path deep-resolves; direct path does not

The ONLY check-time difference between r11 (fail) and r13 (pass) is how check-def consumes
the lambda's inferred type:
- **Direct body** (`check-def`, TypeChecker.codex:279-280): `body-r = infer-expr ... (lambda)`
  yields a raw `FunTy(Vp, Vb)` with FRESH TypeVars, then immediately
  `unify(remaining, body-r.inferred-type)` -- the FunTy still contains unresolved fresh vars
  pointing into the shared substitution array.
- **Let body** (`infer-let-bindings`, TypeCheckerInference.codex:194-197): infers the lambda,
  then **`deep-resolve (vr.state) (vr.inferred-type)`** (line 195) BEFORE binding `f`, then
  the def body is `infer-name f` → `instantiate-type` of a resolved type.

So the let path fully resolves the lambda's fresh vars before they propagate; the direct path
leaves them live in shared state. This is consistent with the failure being shared-state
corruption that only manifests when ANOTHER def is checked afterward (r8/r12 pass: lambda-def
last → no later def to trip on the leaked vars).

### ⚠️ deep-resolve fix -- TRIED AND REFUTED 2026-05-30
Applied exactly this in check-def:279-281 --
```
in let body-ty = deep-resolve (body-r.state) (body-r.inferred-type)
in let u = unify (body-r.state) (env2.remaining-type) body-ty (aexpr-span (def.body))
in let effect-checked = check-effect-subset (u.state) (env2.remaining-type) (deep-resolve (u.state) body-ty) (aexpr-span (def.body))
```
Built a reg compiler from patched source (concat `build/concat-codex-self.ps1` → compile to
`build/output/reg.cdx` → install to `build-output/bare-metal/Codex.cdx`), recompiled r11:
**STILL FAILS, identical `CDX2001 Fun vs Integer`.** So deep-resolving the body type at
check-def is NOT the fix. Reverted. (reg compiler hash was E41BBA47; restored seed 6C562E97.)

This means the corruption is NOT "unresolved fresh vars in the committed body type" -- it is
upstream of check-def's final unify, OR it is in what check-def STORES vs what infer-lambda
leaves in shared state, OR (per the r2/r13 let-passes contrast) the let path differs in some
way OTHER than deep-resolve. **Three paper hypotheses now refuted (capture, param-list,
deep-resolve). STOP guessing -- instrument next.**

### ★★ INSTRUMENTED 2026-05-30 -- ROOT CAUSE NARROWED: the FOLLOWING def's declared-type is lost

Added a temporary DBG emit in `check-def` (forced via add-unify-error so it always prints).
Reg-built (concat → compile → install reg as kernel), compiled r8 and r11. Traces:

**r8** (make-adder ALONE):
```
4:3  DBG def=make-adder rem=Fun bodyraw=Fun bodyres=Fun nextid=4
```
**r11** (make-adder lambda, THEN `helper : Integer` / `helper = 5`):
```
     error CDX2001: Type mismatch: Fun vs Integer          ← the real failure
4:3  DBG def=make-adder rem=Fun bodyraw=T4  bodyres=Integer nextid=5
7:3  DBG def=helper     rem=T5  bodyraw=Integer bodyres=Integer nextid=6
```
(rem = `env2.remaining-type`; bodyraw/res = inferred body type raw/deep-resolved.)

TWO hard facts from the diff:
1. **`helper`, declared `Integer`, shows `rem=T5` -- a FRESH TYPEVAR, not `Integer`.**
   `remaining-type` comes from `resolve-declared-type` (TypeChecker.codex:345): if
   `list-length (def.declared-type) == 0` it builds a fresh-var fun type; else it instantiates
   the declared type. **rem=T5 means `helper.declared-type` was seen as EMPTY** → helper's
   `: Integer` annotation was DROPPED. That is the corruption. (When helper is undeclared-by-
   accident, its body `5` unifies T5:=Integer fine -- bodyres=Integer -- but the *prior* failure
   is already in the bag.)
2. **make-adder's own body inference CHANGED between r8 and r11**: `bodyraw=Fun` alone vs
   `bodyraw=T4`(→Integer) with helper present, and nextid shifted 4→5. So make-adder's AST/state
   is ALSO being read differently depending on what follows it.

⇒ This is an **AST/list aliasing clobber of the def list itself** (specifically each ADef's
`declared-type` list and/or the lambda body), NOT a type-inference logic bug and NOT a
substitutions-array bug. The `def.declared-type` of the def AFTER a lambda-bodied def reads as
empty. Consistent with everything: lambda-def LAST → no following def whose annotation can be
lost → PASS (r8/r12); let-wrapped (r2/r13) → the lambda is nested inside a let value, not the
def body, so it does not clobber the next def's ADef record → PASS.

### WHERE TO LOOK (the def list / ADef allocation, NOT the type checker)
The clobber is of `ADef.declared-type` (and possibly the lambda `body`) for the def following a
lambda-bodied def. Trace how the ADef list is built and whether a lambda body shares/overruns a
buffer into the next ADef:
- `desugar-def` (Desugarer.codex:284) builds each ADef with `declared-type = ann-types`
  (`desugar-annotations`, :296 → `deck-record [desugar-type-expr ...]`) and `body = desugar-expr`.
- `desugar-defs-ll` (:340) builds the ADef list via `__linked-list-push`, then
  `__linked-list-to-list` (desugar-document :362,:368). Suspect: the lambda's desugar
  (`ALambdaExpr (map-list desugar-lambda-param params) (desugar-expr-at body) synthetic-span`,
  :59) allocates a list/record that aliases or is overwritten when the NEXT def's
  `desugar-annotations` / ADef is allocated -- so the next ADef's `declared-type` ends up empty.
- ALSO check the CHECK-phase deck copy: check-chapter copies state with headroom
  (`copy-list-with-headroom`, :447/:450) -- but the def LIST (`mod.defs`) is iterated directly by
  check-all-defs; if that list or its ADef records live in a region overwritten during check,
  same effect. Given make-adder's body ALSO reads differently (fact 2), the clobber likely
  happens at/after desugar and is observed at check time.

NEXT PROBE (1 build): in check-all-defs (TypeChecker.codex:561) or at desugar-document, dump
`list-length (def.declared-type)` for EACH def. Predict: helper shows 0 in r11 (annotation lost)
but 1 in a passing arrangement. Then bisect which allocation overwrites it (deck-record the
suspect list / break the aliasing -- mirrors [[escape-approach]]).

(Earlier "instrument check-all-defs substitutions" plan is SUPERSEDED by the above -- the
substitutions array is not the carrier; the ADef.declared-type list is.)

Specific things the trace should answer:
- Does make-adder's STORED type (env binding) differ between r8 and r11 after make-adder's
  check? (If yes → the bug is that storing make-adder's result reads shared state that a later
  def will mutate -- but make-adder is checked BEFORE helper, so this would mean the binding
  holds a live TypeVar into the shared substitutions array that helper's check later binds.)
- What is `next-id` and substitutions length at the START of helper/opening's check in the
  failing case, and does helper's `remaining`/`expected` type contain a TypeVar that resolves
  to Fun?
- Is the culprit actually in `register-all-defs` (TypeChecker.codex:463) -- which pre-registers
  EVERY def's type with FRESH vars for undeclared defs BEFORE checking any -- combined with the
  lambda's check binding one of those shared fresh vars? Note register-all-defs runs once up
  front; a lambda def's body unify could bind a var that a LATER def's pre-registered type
  shares. THIS is the most likely real mechanism and was under-examined. Check whether
  make-adder (declared) vs helper/opening still go through fresh-var pre-registration and
  whether ids collide.

(Original param-list hypothesis text retained below for the record -- it is REFUTED.)

## LEADING HYPOTHESIS (REFUTED -- see above) -- clobbered lambda param-list (aliasing), NOT type logic

`wrap-fun-type` (`TypeCheckerInference.codex:245`):
```
wrap-fun-type (param-types) (result) = wrap-fun-type-loop param-types result (list-length param-types - 1)
wrap-fun-type-loop (pt) (result) (i) = if i < 0 then result else FunTy(pt[i], <recurse i-1>)
```
**If `param-types` is EMPTY, wrap-fun-type returns `result` (the body type) directly -- NOT a
FunTy.** So if `infer-lambda` sees an empty param list for `\y -> x + y`, it infers the body
as `Integer`, and check-def:280 does `unify(remaining = Fun(Int,Int), inferred = Integer)`
→ **"Fun vs Integer", synthetic span** -- exactly r10/r11/r1.

`infer-lambda` (`TypeCheckerInference.codex:217`) reads `params` straight from the
`ALambdaExpr params` AST node; `bind-lambda-params` iterates it. So the lambda's **param-name
list is being read as EMPTY at CHECK time -- but only when another def is checked after it.**
That is the signature of an **in-place / aliasing clobber of a shared list buffer**: the
deck/headroom-preallocated lists in check-chapter (`substitutions`/`expr-types`,
`TypeChecker.codex:446-451`, mutated in place via list-push/list-set-at) or the AST param
list buffer get overwritten by a later def's allocation. Lambda-def LAST → nothing allocates
after it → list survives → PASS (r8, r12). This also subsumes val's "empty-list `[]`"
observation: `[]` and the lambda param list are the same shared-buffer hazard class.

⇒ Bug 1 is most likely a **memory/aliasing bug, not a type-inference logic bug** -- same
family as [[phase-memory-escapes]] (in-place mutation of shared deck buffers).

**The clobber is almost certainly at DESUGAR time, not check time.** Reasoning: the error
is reported FOR make-adder, but make-adder ALONE checks fine (r8) and only fails when a def
follows it (r11). Each def reads its own lambda's param list during its OWN check, so a
*later* def's check cannot overwrite a buffer make-adder already consumed. The only timing
consistent with "make-adder's result depends on a def that comes AFTER it" is: **all defs
are parsed+desugared before any are checked** (see `desugar-document` building the full def
list, then `check-chapter`). So `map-list desugar-lambda-param params` (Desugarer.codex:59)
must be returning a buffer that gets REUSED/overwritten when the NEXT def is desugared --
overwriting make-adder's lambda `[y]` param list with the following def's param structure
(often empty). Lambda-def LAST → nothing desugared after it → list survives → PASS. This
fits EVERY row. **Start the investigation at `map-list` and the desugar list allocation,
not the type checker.**

## What is RULED OUT (re-confirmed one-compile-at-a-time -- do not re-test)
- NOT `[]` / empty list (removed entirely; still fails).
- NOT cross-chapter (single chapter, no `cites`; still fails).
- NOT capture of the enclosing param (r10: `\y -> y + 1`, no capture, fails).
- NOT parens in the type (`Integer -> Integer -> Integer` fails identically).
- NOT the lambda param failing to bind (`\y -> y` gives NO "unknown name: y").
- NOT a static-logic error in desugar/check (all paper traces say these should pass -- the
  fault is in runtime shared state, invisible by reading; confirmed by the position-dependence).

## What WORKS (clean idioms -- confirmed by compile)
- Lambda wrapped in a let: `make-adder (x) = let f = \y -> x + y in f` (r2).
- A lambda-bodied def as the LAST def in the chapter (r8, r12).
- 0-param `f = \y -> y` WITH an explicit annotation (r9) -- the annotation constrains the
  leaked var before it can corrupt downstream.
- Multi-param sugar `f (x)(y) = ...` (no lambda).
- Partial-application body `f (x) = g x` (function-typed body, not a lambda). Matches the
  dataquire `exec-sort-cmp` fix val mentioned.

## How to CONFIRM in ONE instrumented build (do this first -- stop reasoning on paper)
Add a temporary debug print in `infer-lambda` (`TypeCheckerInference.codex:217`) emitting
`list-length params` (and the param names) per lambda. Fast-loop a reg seed (see
[[phase-memory-escapes]] DURABLE TOOLING / "Fast loop"; emitter-LOGIC change → reg must be
rebuilt to observe). Compile r11 (lambda-def + following def) vs r8 (lambda-def alone).
- **Prediction:** r11 prints **0** params for make-adder's lambda (clobbered); r8 prints **1**.
- If confirmed → the fix is to make the lambda param-name list DURABLE (deck-record it at the
  right point / break the aliasing), NOT to touch the type checker. Trace where ALambdaExpr's
  `params` is allocated (`Desugarer.codex:59`, `map-list desugar-lambda-param params`) and what
  later phase/def reuses that buffer. Candidate fix mirrors [[escape-approach]]: back-copy the
  param list to a durable region before it can be clobbered.
- If r11 ALSO prints 1 param → hypothesis WRONG; it is type-state corruption. Fall back to
  instrumenting the shared `substitutions` buffer: in `check-all-defs` (`TypeChecker.codex:561`)
  print each def's name + `next-id` + substitutions length + `deep-resolve` of its inferred
  type, and compare make-adder→following-def across r8 (pass) vs r11 (fail).

## Then
1. Fix per whichever branch the instrumentation confirms.
2. Add regression gate `codex/test/lambda-body-def.codex` (`.expected` sample): a lambda-bodied
   def FOLLOWED by another def + a use site.
3. Seed rebuild per [[seed-rebuild]] (codegen change → rebuild until "hard fixed point in one
   pass"). Copy up to main via `BigWhite_Codex_reek_main`.

## Harness gotchas (IMPORTANT -- saves hours)
- **NEVER batch tool calls.** A compile that exits 4 (CODEGEN-HALTED -- a NORMAL failure)
  CANCELS every sibling tool call in the same turn. During bisection this silently scrambles
  which repro maps to which result (it made me briefly believe "capture is the trigger" off a
  corrupted batch). ONE tool call per turn, especially around compiles. See [[no-parallel-tools]].
- Bash is banned for normal work (PowerShell only; CLAUDE.md rule 6 -- Unix tools only for GDB).
- `build/compile.ps1` has **NO `-Mode` param**. Default output is CDX. Flags: `-IrUni`/`-IrCce`
  (IR text dump), `-Prose`, `-Repl`, `-Poison`, `-DebugMode`, `-Profile`, `-Trace`,
  `-EscapeCheck`, `-Break "<fn>"`, `-Survey "..."`. No TEXT switch.
- TEXT/IR emit does NOT run for a failing repro (type errors halt before emit) -- so you cannot
  dump the parsed AST via a normal compile of a failing file. You MUST instrument.
- This session: `seed/Codex.cdx` signed-file hash `6C562E97…`; main head CL 2826; my dev stream
  `//Codex/MutableRecords` head CL 2827 (main merged down today, current).

## Pointers
- Checker: `codex/compiler/Types/TypeChecker.codex` -- check-def:271 (body unify at :280, synthetic
  span for lambda body), check-all-defs:561, resolve-declared-type:345, bind-def-params:389,
  check-chapter pre-alloc :446-451.
- Inference: `codex/compiler/Types/TypeCheckerInference.codex` -- infer-name:39, instantiate-type:55,
  infer-lambda:217, bind-lambda-params:233, wrap-fun-type:245, infer-let:177 (note: let path
  deep-resolves the binding value at :195 -- check-def does NOT), infer-expr dispatch ~545.
- Unifier: `codex/compiler/Types/Unifier.codex` -- unify-mismatch:433 emits "X vs Y" as
  `type-desc a & " vs " & type-desc b`; UnificationState is `mutable`, substitutions shared.
- TypeEnv: `codex/compiler/Types/TypeEnv.codex` -- direct CodexType storage, binary search.
- Desugarer: `codex/compiler/Ast/Desugarer.codex` -- lambda:59 (`ALambdaExpr ... synthetic-span`),
  desugar-lambda-param:170, desugar-def:284.
- Parser def-body path: `Parser.codex` try-parse-def:411 → parse-def-body-seq
  (`ParserExpressions.codex:327`). Lambda parse: `ParserExpressions.codex:758`.
