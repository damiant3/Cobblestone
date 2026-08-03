# Punctual Enforcement -- Stage-0 Probe Results + Fix Campaign

**Status:** FIX CAMPAIGN SHIPPED (blu CL 6970, 2026-07-04). All four
stage-0 laundering routes closed; the probes flipped to
`errors/punctual-launder-*` with `.failing` sidecars. Stage-0 findings
below are the historical record; section 6 is the as-built.

**Provenance:** BACKLOG "Fulfill the Vision Check" item 1, punctual
leg. Found by writing programs that try to break the bounded-execution
claim and seeing which the compiler accepts. blu, 2026-07-03.

---

## 1. The claim under test

DevelopersGuide: "A function marked `punctual` is proven to have
bounded execution at compile time." KingsAndCourts: the `punctual`
keyword "enforces the regulatory requirements as compile errors ...
bounded worst-case execution time (WCET)." Five structural
restrictions are advertised: CDX6001 (no non-punctual calls), CDX6002
(no heap), CDX6003 (no closures), CDX6004 (no bare I/O), CDX6005 (no
recursion, direct or mutual).

## 2. The mechanism as built

`check-linearity-def`'s sibling, the RT checks in TypeChecker.codex
(`check-rt-calls` / `check-rt-no-alloc` / `check-rt-no-lambda` /
`check-rt-effects` / `check-rt-cycles`), run per punctual def. Each is
a **syntax-directed AST walk over a fixed set of node shapes**:
`AApplyExpr`, `AIfExpr`, `ALetExpr`, `ABinaryExpr`, and `AMatchExpr`
arms. The mutual-recursion cycle check (`check-rt-cycles`,
`collect-rt-mentions`) is more thorough -- it walks nearly every node
shape and is correctly rejected (`errors/punctual-mutual-recursion`,
`errors/punctual-match-recursion`). The *other four* walks are not:
they share the same five-shape skeleton and fall through `otherwise`
everywhere else.

## 3. Stage-0 probe results (seed 47CABCEA)

Four probes. **All four compile clean and run**, each pinned as a
passing `.expected` test in `codex/test/punctual-launder-*`.

| Probe | Hole | CDX missed |
|---|---|---|
| punctual-launder-head | call through a COMPUTED head (`(if c then slow-sum else slow-dbl) x`); the branch names are bare references the call walk drops in its `otherwise` arm, and a non-name application head is never resolved to a callee | CDX6001 |
| punctual-launder-unary | a non-punctual recursive call and a heap list literal both under unary negation (`-(slow-sum x)`, `-(list-length [x,x])`); `AUnaryExpr` is absent from `check-rt-calls` and `check-rt-no-alloc` | CDX6001, CDX6002 |
| punctual-launder-act | three holes in one `act` block: a custom effect `Tick` (CDX6004 blocklists only Console/FileSystem/Network by name), a lambda (the no-lambda walk explicitly returns `st` for `AActExpr`), and a non-punctual recursive call (the call walk has no `AActExpr` arm) | CDX6003, CDX6004, CDX6001 |
| punctual-launder-builtin | `show` (allocates a fresh Text) and `list-length` (O(n) walk) are on the `is-rt-safe-builtin` allowlist; the "no heap" and "bounded instruction count" claims both leak through allowlisted builtins | CDX6002 (spirit), WCET accuracy |

Policed routes (correctly rejected, pinned by existing tests): direct
self-recursion, mutual recursion through a cycle, self-recursion
hidden in a match arm or let value, heap builtins by name, bare
Console/FileSystem/Network effects, lambdas in the checked node
shapes.

## 4. Reading the results

Two distinct deficits:

1. **Incomplete AST coverage.** `check-rt-calls`, `check-rt-no-alloc`,
   and `check-rt-no-lambda` each cover only {Apply, If, Let, Binary,
   Match}. Any construct outside that set -- unary, act blocks, try,
   handle, list/record literals (for the *call* and *lambda* walks),
   field access, timeout -- is a blind spot. The cycle checker
   (`collect-rt-mentions`) already demonstrates the exhaustive shape
   list these walks need; the fix is to bring the four safety walks up
   to that same coverage (and to resolve non-name application heads).

2. **Semantic blocklists/allowlists instead of type-directed
   judgment.** CDX6004 names three effects rather than asking whether
   the return type carries ANY effect row -- so a fourth effect
   launders. `is-rt-safe-builtin` names builtins believed cheap, but
   `show` and `list-length` are neither constant-time nor
   allocation-free, so the WCET report understates unboundedly.

Deficit 1 is the effect/linear-laundering shape once more: a real
discipline enforced at a fixed set of syntactic positions, shed by any
construct outside the set. Deficit 2 is a design choice (name-based
lists) that the effect-row machinery could replace (ask the type, not
the name).

## 5. What this is NOT

No compiler change shipped. The probes are passing `.expected` tests
pinning the permissive behavior; when enforcement closes a route, its
probe flips to `errors/` with a `.failing` sidecar (catalog green both
directions is the ship gate). A fix campaign would: (a) unify the four
safety walks onto the exhaustive node-shape coverage the cycle checker
already has, plus non-name head resolution; (b) replace the CDX6004
effect blocklist with an any-effect-row test; (c) tighten
`is-rt-safe-builtin` to genuinely constant-time, allocation-free
builtins (or annotate each builtin with a cost/effect and read that).
The claim surface (DevelopersGuide, ClaimsCalibration) now states the
true scope.

## 6. Fix campaign as-built (blu CL 6970, 2026-07-04)

All three items from section 5 shipped, plus one discovery in each
direction:

- **(a) Coverage.** `check-rt-calls`, `check-rt-no-alloc`, and
  `check-rt-no-lambda` now walk every expression shape the cycle
  collector walks -- unary, list/record literals, field access, act
  statements, try blocks, handle bodies, with-timeout, field assign,
  lazy -- PLUS match-arm GUARDS and HANDLER CLAUSE BODIES, two shapes
  even `collect-rt-mentions` was missing on the clause side (the
  cycle checker walked handle bodies but skipped clauses; fixed in
  the same CL -- a punctual function can discharge internal effects
  through a handler, so clause bodies are reachable punctual code).
  Non-name application heads are now rejected outright with CDX6001
  ("calls through a computed head"): a spine of AApplyExpr recurses,
  ANameExpr is checked against punctual names + the allowlist, and
  any other head shape is an error before the callee question --
  indirect dispatch is unbounded from the checker's seat.
- **(b) Any-effect-row.** `check-rt-effects` errors on EVERY effect
  name in the punctual signature (was: Console/FileSystem/Network by
  name). `is-rt-unsafe-effect` deleted. A punctual signature must be
  effect-free; a custom effect's handler latency is unbounded from
  the caller's seat.
- **(c) Allowlist.** Dropped `show`, `integer-to-text` (both allocate
  fresh Text), `text-starts-with`, `text-compare` (O(n) helper-call
  loops). **PROBE CORRECTION:** the stage-0 doc accused `list-length`
  of walking a linked list -- the x86 emitter refutes it. Runtime
  lists are length-prefixed contiguous vectors: `list-length` is a
  single header load and `list-at` is shift+add+load, both O(1) and
  allocation-free, so both STAY. `text-length` is likewise one header
  load. `text-at`/`array-at`/`array-length` have no x86 emitter (inert
  names, kept). Blast radius was zero: the 71 punctual foreword defs
  and Vga's two use only arithmetic/bit builtins and declare no
  effects.
  The WRITE side is the sharp edge of the vector model (Damian,
  2026-07-04): `__list_snoc` (list-push/list-snoc/vec-cons) has THREE
  paths -- (1) len < cap: in-place store, O(1), no allocation; (2) at
  capacity with the buffer end exactly at the bump pointer (R10, or
  deck-pos in deck mode): extend in place by advancing the pointer,
  capacity doubled, still O(1) and copy-free; (3) at capacity and
  NOT adjacent (something else allocated after the list): fresh
  doubled buffer plus a full O(n) element copy. Path 3 trips rarely
  (only when an interleaved allocation breaks adjacency), but it is
  a second, independent reason `list-push` sits on
  `is-rt-unsafe-name`: unbounded copy on top of allocation. Reads
  stay allowlisted; writes stay banned.

Probes flipped: errors/punctual-launder-head (.failing 6001), -unary
(6001+6002), -act (6001+6003+6004), -builtin (6001, with the
list-length operand kept as the legal half). rt-smoke gains an extra
6001 instance (`show` in bad-rt-io) under its already-listed codes.

Residual documented edge: a non-punctual function REFERENCE passed as
a value into an allowlisted builtin's argument is not itself an error
(the builtins do not call their arguments); the computed-head rule
catches any attempt to apply it inside punctual code.
