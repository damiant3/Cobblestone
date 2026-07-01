# Bare-metal miscompiles found building Accounts (2026-05-30, val)

Two SILENT miscompiles in the bare-metal (CDX) self-host compiler, surfaced
while building `codex/os/net/Accounts.codex`. Both produce wrong behavior with
NO compile error. Both have workarounds in Accounts; root-cause fixes are open.
The html plug (JS) compiled the same patterns fine — these are specific to the
bare-metal codegen / type path. Repro trail: `p4 filelog` on Accounts.codex +
apps/explorer/AuthDemo.codex; tested via apps/explorer/_authtest.ps1.

## Bug 1 — FIXED (CL 2937, val, 2026-06-01). Root cause: deck-bound CHECK corrupted desugar ADef records.

The original "empty-list cross-chapter call" diagnosis below is **WRONG** and was
a coincidence of the Accounts code shape. Re-bisected from scratch with minimal
repros; the real trigger is unrelated to lists, `[]`, or chapter boundaries.

**Root cause (confirmed by reek, 2026-06-01):** `check-all-defs` ran in deck-bound
mode. `list-push` overflow on `substitutions`/`expr-types` during inference grew
into the deck via `__list_snoc` Path 2 (in-place extension at R10), overwriting
ADef.declared-type of the def following the lambda-bodied def. CL 2937 (val) fixed
this by wrapping `check-all-defs` in `__deck-exit`/`__deck-enter` so inference
scratch goes to bivy. Regression gate: `codex/test/lambda-body-def.codex`.

**Actual trigger: a lambda as the direct body of a named def whose declared
return type is itself a function.** Minimal, single chapter, no cites, no lists:
```
  make-adder : Integer -> (Integer -> Integer)
  make-adder (x) = \y -> x + y          -- FAILS
```
Compiling this emits 2-3 spurious `CDX2001 Type mismatch: Fun vs Integer` /
`Integer vs Fun`, located in callers / unrelated defs, never on the lambda line
— and the set/count shifts with surrounding code (state corruption), which is
exactly the "floating error" the original report saw. `make-adder n` is inferred
as `Integer` instead of `Integer -> Integer`, i.e. the def-body lambda is typed
as a non-`Fun`.

**This is a loud COMPILE-TIME error, not a silent miscompile.** It halts with
CODEGEN-ERRORS and emits no binary. (Different severity class from Bug 2.)

**What was ruled OUT (each reproduced or cleared in isolation):**
- `[]` is irrelevant — `[0]` (non-empty) fails identically.
- cross-chapter is irrelevant — fails in a single chapter with no `cites`.
- operator precedence is irrelevant — `\y -> (x + y)` with parens still fails.
- it is NOT a parser lambda-param absorption (parser keeps `LambdaExpr`; desugar
  keeps the 1-param def + `ALambdaExpr` body, verified by reading Desugarer).

**Confirmed-CLEAN idioms (use these; all compile + run):**
- multi-param def: `make-adder (x) (y) = x + y` : `Integer, Integer -> Integer`
  (then `make-adder n` is a normal partial application — also clean).
- partial-application body: `make-adder (x) = add2 x` (body returns a function
  WITHOUT being a lambda). Clean.

So the rough edge is narrowly: **`f (x) = \y -> ...` (an explicit single-arg
lambda as a def body returning a function)**. Matches the dataquire finding that
a curried `\a -> \b -> ...` comparator had to be rewritten as partial application
(see [[project-dataquire-server]] exec-sort-cmp).

**Root cause not yet pinned to a line.** `infer-lambda`
(TypeCheckerInference.codex:217) and `check-def` / `bind-def-params`
(TypeChecker.codex:271,389) all READ correct for this case — a 1-param def with a
2-arrow declared type peels one arrow and checks the lambda body against the
remaining `FunTy`. Yet empirically the body is inferred as the collapsed scalar
return. Next step: instrument the type printer (or add a temporary trace) to dump
make-adder's inferred body type vs its registered type; the discrepancy is the
bug. The fix is a core type-checker change → must land on a freshly merged-down,
constants-MATCHING tree with full seed gates (the tree was constants-MISMATCH on
2026-05-30; merge down latest seed first).

Original Accounts workaround (local `str-bytes-loop`) still stands and is
unrelated to the real cause; it happened to avoid the lambda form too.

--- ORIGINAL (WRONG) REPORT, kept for history ---

Calling a function defined in ANOTHER chapter with an empty-list literal `[]`
as an argument poisoned global type inference: the build failed with a bogus
`CDX2001 Type mismatch: List vs Fun` reported in an UNRELATED chapter
(NetworkStack `resolve-dst-mac` / TcpTransport `transport-close`), at a line
that floated as the concat order changed. Error count was 1 and the location
was never in the offending code.

Trigger (Accounts cited WebServer):
```
  str-bytes (t) = text-to-bytes t 0 (text-length t) []   -- text-to-bytes is in WebServer
```
- `[]` inside a record-literal field (auth-empty `accounts = []`) is fine.
- `[]` to a LOCAL function call is fine.
- `[]` to a CROSS-CHAPTER function call breaks it.

Workaround: a local copy of the recursive byte builder (str-bytes-loop), so the
`[]` is passed to a same-chapter function. Suspected: monomorphization/inference
of a cross-chapter call instantiated with a polymorphic empty list corrupts a
shared type var.

## Bug 2 — FIXED (reek, CL 2814). Root cause was NOT records — it was text-append `&`.

The "record field" framing was a red herring. The real cause: `__str_concat`
(the `&` runtime helper) had a fast in-place path that, when the left operand `a`'s
bytes end exactly at the heap-top bump pointer R10, appended the right operand into
a's buffer and **overwrote a's length field**, then returned a aliased to the result.
The heap-top check does NOT imply a is dead, so any `a & y` where a is reused
afterward corrupts a. It only fires when a is a heap string with SPARE CAPACITY
(sha256-to-hex output has it; string literals are exact-sized) — which is exactly
why plain-literal repros passed and digest-valued fields failed.

In Accounts: `hash-pw (salt) (pw) = digest (salt & ":" & pw)`. The `salt & ":"`
append corrupted `salt` in place to `salt:`, so the salt later stored in the
Account (and read back) was wrong. Nothing to do with field offsets.

MINIMAL REPRO (3 lines, deterministic):
```
  let a = digest "x"          -- 64-char sha256 hex, heap buffer w/ spare capacity
  in let b = a & ":secret"    -- corrupts a in place
  in a                        -- a is now "<64hex>:secret" (len 71, not 64)
```
FIX: force `__str_concat` to always take the safe fresh-alloc path (the slow path
already copies a then b into a new buffer, never mutating a). The fast in-place
path is fundamentally unsafe — a runtime helper cannot know if a is aliased.
Build timing unchanged (~195s); most compilation was already on the always-fresh
`__deck_str_concat`. Regression gate: codex/test/text-append-alias.codex.

The id-derived-salt workaround in Accounts can now be reverted (5-field Account
with a stored salt works), though it is harmless to leave.

--- ORIGINAL REPORT (kept for history) ---

## Bug 2 (original) — record field miscompiled to the previous field's value

A 5-field record read its last text field as the PREVIOUS field's value, with
no error:
```
  Account = record { acc-id : Integer, handle : Text, display : Text, pwhash : Text, salt : Text }
  ... Account { acc-id = id, handle = h, display = d, pwhash = ph, salt = slt }
  -- acc.salt returned ph (the pwhash value), not slt
```
Observed by dumping every field immediately after construction:
`acc-id,handle,display,pwhash` correct; `salt` == `pwhash`'s value.

Did NOT depend on:
- local-variable name shadowing the field (`salt = salt` vs `salt = slt`): same bug.
- function-call vs hoisted-local field value (`pwhash = hash-pw ...` vs `= ph`): same bug.
- being the last field: adding a trailing `pad : Integer` did NOT fix it (salt still wrong).

Smaller records were fine in the same build: `Session {token, sess-uid}` (2),
`AuthState {accounts, sessions, next-id, nonce}` (4) all read/threaded correctly.

Workaround: dropped the `salt` field entirely; salt is derived deterministically
from `acc-id` (`salt-for id = digest (show id & "$codex-salt")`), so Account is
4 fields {acc-id, handle, display, pwhash} and nothing reads a 5th field.

Still present on seed #157/#158 (re-tested 2026-05-30 after merging that seed:
restoring the salt field made login fail again; reverting to id-derived salt
fixed it). So the recent X86_64Compound/IRTextEmitter changes did NOT fix it.

UPDATE — a plain 5-field record does NOT reproduce it. This compiled and ran
CORRECTLY on seed #157/#158 (printed `a=1 b=B c=C d=D e=E`):
```
  RecFive = record { a : Integer, b : Text, c : Text, d : Text, e : Text }
  let r = RecFive { a = 1, b = "B", c = "C", d = "D", e = "E" } in print ... r.e ...
```
So the trigger is NARROWER than "5-field record." The failing case differs by
being built deep in context: inside a `when ... is None ->` branch, fields from
let-bound locals (one a `hash-pw` call result), list-pushed into AuthState, the
account later retrieved via `find-handle` (Maybe), and the field read in a
different function. Minimal trigger still unknown. Three repro attempts on seed #157/#158 all
compiled and ran CORRECTLY (did NOT reproduce):
1. plain 5-field record, literal values, top-level let.
2. record built in a fn, list-pushed, retrieved via `find : ... -> Maybe Rec`,
   field read in another fn.
3. + computed field values (`"x" & show k`) + the list stored inside a state
   record (`St { items : List Rec, ... }`) + retrieval via Maybe + cross-fn read.
So none of {list, Maybe, cross-fn, state-record nesting, computed values} alone
is the trigger. The real failure (Account in Accounts.codex) additionally has:
the `Sha256` + `WebServer` cites in the closure, field values that are SHA-256
hex (64-char) of related inputs (pwhash=hash-pw(slt,pw), salt=slt where both go
through `digest`), and the record consumed by both auth-register and auth-login.
Next suspects to add: the `Sha256` cite (large closure) and digest-valued
fields. Workaround (id-derived salt, 4-field Account) remains in place and
verified.

## Why this matters

Both are silent: the compiler accepts the program and emits a wrong binary.
For a compiler that is its own root of trust, silent miscompiles are the worst
class. These should get minimal repros and real fixes (type inference for
cross-chapter polymorphic calls; record field offset/codegen), and ideally a
gate sample that would have caught the record one.
