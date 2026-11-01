# ACCP information flow

Status: ruled (section 8); rungs 0, 0b and 1 built, and the second implementation of rung 1 built (section 6); rung 1 admits a run rung 0 refuses when both implementations answer `no path` (section 5). Owner val. Source row:
`apps/accp/accp-backlog.md`, "No information-flow property"
(`docs/Reference/Nectry.md` section 5, item 1).

## 1. The gap

ACCP admits a run when every label of the row on `opening` is ambient or
leased, with each scope inside the leased scope
(`AgentCompilerControlProtocol.md` section 7). That bounds WHICH channels a
program touches. It does not bound WHICH DATA travels between them. A
program granted `[FileSystem.Read "/patient/", Network.Write "api.example"]`
may send every byte it read to that host, and no instrument in ACCP can say
otherwise. Nectry's NectryCore states this property (information flow, its
property 1), after Chlipala's UrFlow (OSDI'10).

Exposure today is nil, and the reason matters for the order of work. The
deployed conduit serves the ambient set only (pure and request-buffered
`Console`), where every input comes from the requester and every output goes
back to the requester, and a granted `Network.Read` traps in the hosted runner
(`AgentCompilerControlProtocol.md` section 5.2). The property becomes
load-bearing the day a lease grants a read channel and a write channel to the
same run. This design is the precondition for that day.

## 2. The property

For one admitted run:

- A **source** is a row label with a Read direction, with its scope. The
  scoped operations the type checker knows today (`scoped-op-effect`,
  `codex/compiler/Types/TypeChecker.codex`) give `FileSystem.Read`,
  `Network.Read` and `Console.Read` (stdin, which the conduit binds to the
  request's `input` field).
- A **sink** is a row label with a Write direction, with its scope:
  `FileSystem.Write`, `Network.Write` and `Console.Write` (stdout and stderr,
  bound to the response). A label with no direction (`FileSystem`, `Network`)
  is both, because it covers both directions.
- A **flow rule** is a pair (source pattern, sink pattern) that the grant
  FORBIDS. A pattern is an effect name and a scope.
- **Matching.** A row label matches a pattern when the names OVERLAP and the
  scopes OVERLAP. Names overlap when either covers the other by
  `effect-covered-by`'s rule (a name covers itself and its dotted children,
  so `FileSystem` overlaps `FileSystem.Read`). Scopes overlap when either
  subsumes the other by `scope-subsumes` (`TypeChecker.codex`), the
  scope-against-scope relation, for the label's kind: a path for `FileSystem`
  (`path-admits`, so `/patient/` covers `/patient/2026/` and not
  `/patientx`), an authority for `Network` (`authority-subsumes`, where a
  portless scope covers every port), a channel for `Console` (equality). Not
  `scope-admits`, which tests a resource against a grant and treats a portless
  authority as narrower than a ported one. Overlap, not containment, because a row holding
  the wider capability can do the forbidden narrower thing.
- A rule whose two ends both name `Console` is refused when the lease is
  issued: both channels belong to the requester, so the rule forbids nothing
  the requester does not already see.

**The run is admitted only if no value computed from a forbidden source can
reach a forbidden sink**, where "reach" covers:

- explicit flow: the value, or anything built from it (a record field, a list
  element, a concatenation, a closure capture, a function result), is an
  argument of the sink's builtin;
- implicit flow: the sink's builtin is called under a branch (`if`, `when`, a
  guard) whose condition is computed from the source, so whether it runs, or
  with which arguments, depends on the source.

Out of scope, stated so that nobody reads the property as covering them:
timing (how long the run takes), termination (whether it finishes before
`limits.ms`), resource use (heap, the length of `stdout`, trap versus no
trap), and the behaviour of the external system on the far side of a sink.
A flow through these channels is not refused. Section 7 says what that costs.

Flow rules forbid, they do not permit. A grant with no flow rule admits as
today. This keeps principle 4 (absence restricts) intact: a flow rule only
ever removes runs that the row check would admit.

## 3. Raw memory is refused

The row does not bound raw memory: heap `peek-*` and `poke-*` are pure by
ruling (`docs/Designs/Done/Compiler/CapabilityProbe.md`, the 7.3 table), so
a program can store a source byte at an address and load it beside a sink
without a single typed edge between the two. No analysis over typed IR
sees that. **When any flow rule applies to a run, a program whose reachable
IR names a raw-memory builtin (the `peek-*` and `poke-*` families and every
other builtin that reads or writes an address it is given) is refused with
`needs: ["no raw memory under a flow rule"]`.** The list is fixed by the
conduit and censused against the compiler's builtin table at each seed
(L-CENSUS: census by the table, not by spelling).

**Built (rung 0b):** `afl-raw-memory` and `afl-raw-check` in
`apps/accp/AccpFlow.codex`, called from `ap-leased` once the rung 0 pair check
admits. The list holds the peek, poke, buffer, vector, atomic, allocator
(`__heap-restore`, `__deck-*`: a rewound allocator hands a live value's bytes
to the next allocation), device-memory and address-register families, and
`__cover-arm`. A builtin that only yields an address (`alloc-bytes`,
`address-of`, `__heap-save`) is not on it: without a reader the address
carries nothing. The scan is exact because the IR spells a reference to a
builtin only as `(name "<builtin>" ...)` and escapes every quote inside a
string; every def in the unit is scanned, a superset of the reachable ones.
The census is `apps/accp/raw-memory-census.ps1`, run by `apps/accp/build.ps1`,
so a bundle whose compiler holds an unclassified builtin is not built. The
corpus is `codex/test/apps/accp-flow-rung0b`; the wire arm is in
`apps/accp/test.ps1`.

`get-state` and `set-state` are not raw memory, but they are a typed store and
load through one global cell, so rung 1 treats a `set-state` of a tagged value
as tagging every `get-state` in the unit.

In-place writes are typed and stay allowed, but they are their own hazard.
`list-push`, `list-set-at` and `__record-set` store into the caller's value
and hand the same pointer back (L-ALIAS), so a tagged value written into a
list reaches every other name holding that list, which a per-value summary
does not see. Section 5 item 3 handles them.

## 4. Rung 0: flow rules on the row

**Built** (`apps/accp/AccpFlow.codex`): the rule text and refusal are in
`apps/accp/README.md`; the corpus is `codex/test/apps/accp-flow-rung0`, and
the wire arm is in `apps/accp/test.ps1`. Rules are checked once the lease
itself admits (`ap-leased`), so an unverified lease never reaches the pair
check. The `ProseGrant` sentence of section 8 item 3 is not lowered yet; a
grantor writes the field.

**The conduit reimplements the matching.** L2 does not cite compiler chapters
(`AgentCompilerControlProtocol.md` section 3: the compiler unit is assembled by
glob), so section 2's covering and scope rules are written again in
`apps/accp`, as `ai-needs` already writes its own label test. The corpus pins
the copy to the compiler's: each matching arm is a case `effect-covered-by`
and `scope-admits` also decide, with the same answer recorded.

The cheapest sound check needs no analysis: **refuse a run whose row holds
both ends of a forbidden pair.** A program cannot leak through a channel it
does not hold, so a row without the pair is safe by the row check already
proved, and a row with the pair is refused before any IR is read.

- Input: the admitted row and scopes (`AccpAdmission`, `apps/accp/AccpIr.codex`,
  built by `ai-type`) and the lease's flow rules. The check runs in
  `ap-leased` once `ag-admit` admits the lease.
- Cost: rows hold at most 32 labels (`ai-string-list`), so the check is
  |row|^2 times |rules| comparisons, constant per request.
- Answer on refusal: `status: refused`, `stage: flow`, and
  `flow: [{"source": ..., "sink": ...}]` naming each pair found. The journal
  record (`ap-decision-fields`, `AccpProtocol.codex`) copies a fixed field
  list, so `flow` is added to it in the same change or it is lost there.
- What it gives up: every program that holds both channels, leaking or not.
  A grantor who forbids `/patient/` to `api.example` and also grants both to
  one agent gets no run from that agent that touches both. That is the
  correct answer for a first grant and the reason rung 1 exists.

Rung 0 is decidable, total and cannot be wrong in the leaking direction. It
is the rung to build first.

## 5. Rung 1: a flow analysis over the IR

Rung 1 admits a run that holds both ends of a forbidden pair when the IR
shows no path between them. It reads the IR chapter the conduit already
parses, so the compiler does not change (ACCP principle 1).

The analysis, conservative everywhere it must choose:

1. **Seeds.** Every call of a source builtin whose label and scope match a
   rule's source pattern is a seed carrying that rule's source tag.
2. **Def summaries.** Each def gets a summary: which tags its result depends
   on, for each parameter whether the parameter reaches the result, and which
   tags reach each sink call it contains (directly or through its callees).
   Tags form a finite lattice (subsets of the rules' source tags), so the
   interprocedural fixed point over the call graph terminates, mutual and
   self recursion included.
3. **Values are joined, not tracked by field.** A record, list, tuple, `Maybe`
   or closure carrying a tagged value is tagged as a whole. A field read or
   `list-at` of a tagged structure is tagged. This is field-insensitive on
   purpose: precision here is where analyses go unsound. An in-place write
   (`list-push`, `list-set-at`, `__record-set`) of a tagged value tags every
   value of the target's type in the unit, because any of them can be an
   alias of the target (section 3).
4. **Control.** Every expression under a branch whose condition is tagged
   carries the condition's tags (the program-counter label), and a sink call
   under it counts as reached by them. A function value called under a tagged
   branch is treated the same.
5. **Higher order.** A call through a function value whose target the
   analysis cannot name joins the tags of every def whose address is taken
   anywhere in the unit.
6. **Verdict.** The run is admitted if no sink call matching a rule's sink
   pattern is reached by that rule's source tag; otherwise refused with the
   rung 0 answer plus the def and call site of each reaching sink.

Cost: the IR's size times the number of tags, times the number of fixed-point
passes; the passes are bounded by the tag count times the def count, since
each pass can only add tags. The analysis allocates one summary per def, so
the conduit runs it inside the request's own heap budget and refuses with
`timeout` past it (L-PEROBJECT: summaries times defs is the multiplication to
check).

**Built (rung 1, `apps/accp/AccpFlowIr.codex`, `afi-analyse`).** What it
decides, and where it is coarser than the items above:

- **Summaries are one mask per def**, not per parameter: the union of every
  call's arguments, the def's result, and the pc of every call (context and
  parameter insensitive). A helper called once with a tagged value is tagged
  for every caller. This is sound and refuses more than items 2 and 4 ask.
- **Labels come from the IR type** of each name: `(label "Network.Read" "")`
  inside a function type's row. A builtin whose type prints no row (a bare
  effectful value such as `net-status`) is named by `afi-nullary`; scopes are
  not read, so every label overlaps every scope (conservative).
- **A function value** is a def referenced outside call position, a partial
  application, a lambda (lambda-lifted in the IR, so a partial application
  of `__lam_N`), or a builtin passed as an argument. Every unknown call
  joins all of them: arguments into their parameters, their results into the
  call's result, and the call's pc and the function value's own mask into
  their pc. A sink builtin passed as a value is reached by every unknown
  call's arguments.
- **Unmodelled, answering `unanalysed`:** `handle`, `try`, `with-timeout`,
  `fork`, `await` and any other form the walker does not name; any effect
  operation; the builtins in `afi-unmodelled` (tasks, processes, kernel
  channels, `open-file` and the other row-less ones); raw memory (section 3);
  an IR over 262144 characters; more than 30 rules; no fixed point within
  4 x defs x rules + 4 x rules + 8 passes. `set-state` and `get-state` are
  modelled as one global cell.
- **Admission (ruling 2).** Both implementations run when rung 0 refuses on a
  forbidden pair (`ap-flow-ruled`, `AccpProtocol.codex`). The run is admitted
  only when both answer `no path`; any other pair keeps the rung 0 refusal.
  Either way the answer and the fact carry `analysis: {verdict, reach:
  [{source, sink, defs}]}` (the first) and `analysis_second` (the second's
  verdict text). A rule-text refusal or a row/scope mismatch is refused
  without the analyses.
- **Census.** `apps/accp/raw-memory-census.ps1` refuses a bundle whose
  builtin table holds a row-less builtin that neither `afi-nullary` nor
  `afi-unmodelled` names.

## 6. Proof of the checker

A flow checker that admits everything looks exactly like one that works
(L-FALSIF), and its users will never see the difference. Each rung lands with
a corpus of plain programs, written by hand, in `codex/test/apps/accp-flow-*`,
and the corpus is graded in both directions:

- **must refuse**, one program per channel kind: direct argument, record field,
  list element, concatenation, closure capture, function result through a
  callee, recursion accumulator, an in-place write into a list another name
  holds, implicit flow through `if`, implicit flow through `when`, a function
  value chosen by a tagged branch, and raw memory;
- **must admit** (rung 1 only), the same shapes with the sink fed from an
  untagged value, so that a checker refusing everything fails too;
- each refusal arm is run against a sabotaged checker (the one rule that
  catches that channel removed, match count printed and non-zero) and must
  turn into an admission, so that every arm is shown to reach its rule.

**Built for rung 1:** `codex/test/apps/accp-flow-rung1` reads
`accp-flow-rung1.stdin`, one `label|IR` line per program, written by
`apps/accp/flow-corpus.ps1` from the programs it holds with the bundle's own
compiler (rerun it when the IR format changes). 32 programs (2026-11-01): 16
must refuse, one per channel above plus a sink in a tagged `if`'s branches, a
function value returning a source, a sink builtin passed as a value and the
global state cell; 15 must admit, the same shapes untagged; raw memory
answers `unanalysed`. Each of 13 sabotaged rules moved at least one arm and
only arms whose flow runs through that rule (the binary-operator rule moved
6, every other rule 1 or 2).

**The second implementation (ruling 2)** is `apps/accp/AccpFlowCheck.codex`
(`afc-check`, blu), written from this section and section 5 without reading
`AccpFlowIr.codex`. It follows items 2 and 4 as written: per def, a result
mask and a per-rule sink mask over the rules' tags, a caller program-counter
bit and one bit per parameter, substituted at each known call; an unknown
call joins every address-taken def. In-place writes and `set-state` go to a
store keyed by type (or the global cell) that holds tags only; the part of a
write that depends on a def's parameters or caller is that def's store
effect, re-applied, substituted, at each call. `codex/test/apps/accp-flow-rung1-second`
runs both implementations over the 32 corpus programs plus 6 precision probes
and prints agreement per program (2026-11-01): 37 agree; one disagrees, probe
A (a helper called with the source and again with an untagged value that
reaches the sink), where the second answers `no path`, the truth, and the
first answers `path` (its one mask per def; sound, coarser). Each of 11
sabotaged rules of the second moved only refusal arms on its channel (binary
operators 8, builtin arguments 11, every other rule 1 or 2). The second
answers `unanalysed` past 20 rules, 40 parameters, 256 store keys or 1024
store effects, and its raw-memory list is its own prefix list, not
`afl-raw-memory`.

## 7. What this does not do

- **Declassification** is section 10 (rung 2, designed, not built): a pinned,
  pure def the grant names. It does not prove that the def destroys
  information (10.5).
- **Covert channels.** Section 2's exclusions stand: a program refused nothing
  by rung 1 can still signal a source bit by taking longer, by trapping, or by
  the length of its output to a permitted sink. Against a grantor whose
  concern is a curious agent rather than a malicious program, that is the
  usual and stated limit of static information flow. Against a malicious
  program it is a real leak of a few bits per run, bounded by the run count a
  lease allows.
- **Flows across runs.** Each run is a fresh instance (ACCP principle 5), so
  no state carries between runs inside the runner. A flow through an external
  system (write to a file the next run may read) is the grantor's to forbid,
  as a rule on that file's scope.
- **Bare metal.** Bare metal is not an ACCP runner (ACCP section 5.3), and
  section 3's refusal does not make it one.

## 8. Rulings

Damian, 2026-11-01: "1 yes, 2 yes, 3 both for accp". Each recommendation
below is ruled as written: rung 0 is the first build; rung 1 admits nothing
rung 0 refuses until a second implementation agrees; a flow rule is written
as a `ProseGrant` sentence lowered to the lease field the conduit checks.

1. **Is rung 0 the first build?** Recommended: yes, before any lease grants a
   read channel and a write channel together. It is small, sound and total.
2. **Rung 1's instrument count.** Accept the analysis as the single instrument
   for flows, with the section 6 corpus, or require a second implementation
   before rung 1 admits anything rung 0 would refuse. Recommended: require
   it, because rung 1's whole value is admitting runs the cheap check refuses,
   and an unsound admission is silent.
3. **Where a flow rule is written.** As a sentence of a `ProseGrant`
   (`codex/os/trust/PolicyProse.codex`), in Codex Prose Language form ("data
   read from `/patient/` does not reach `Network.Write`"), which also answers
   part of Nectry item 4, or as a structured field of the lease only.
   Recommended: both, the sentence lowered to the field, the field being what
   the conduit checks.

## 9. Rungs

| rung | delivers | acceptance |
|---|---|---|
| 0 | flow rules on leases; admission refuses a row holding both ends; `stage: flow` refusal on the wire | corpus: every forbidden pair refused, every row missing one end admitted; a sabotaged rule moves exactly its arms |
| 0b | raw-memory refusal under any flow rule | corpus arm per raw-memory builtin family; census of the list against the builtin table at the seed |
| 1 | the IR analysis of section 5 | built, advisory: section 6 corpus in both directions, sabotage per channel rule; the second implementation built and agreeing on the corpus (section 6); admission when both answer `no path`, a disagreement refused naming both (`apps/accp/test.ps1` arms) |
| 2 | declassifiers (section 10) | designed; rulings D1 to D4 pending. Acceptance: section 10.6's corpus in both directions, run by both implementations, agreeing on every program, and a sabotage per check of section 10.3 |

## 10. Rung 2: declassifiers

A grantor who forbids `/patient/` to `api.example` may still want "the number
of patient records may go to the host". A **declassifier** is a def in the
program unit that the grant names, through which data from a forbidden
source is allowed to reach that rule's sink. Nothing else about a flow
changes: a tagged value that reaches the sink by any path other than the
declassifier's result is still a path. Both implementations (section 6)
implement this section, and every rule below is stated for both.

### 10.1 How a grant names one

A flow rule gains an optional clause after its sink end:

```
SOURCE -> SINK through NAME sha256:HEX
SOURCE -> SINK through NAME sha256:HEX, NAME sha256:HEX
```

`through` is followed by one or more entries separated by `, `. `NAME` is
one label (letters, digits, `-`, `_`; the def names the IR prints);
`HEX` is exactly 64 lowercase hex digits. A clause that does not read this
way, or names one `NAME` twice, makes the rule malformed, which refuses the
lease at admission as a malformed rule does today (`afl-rule-refusal`).
A rule with a `through` clause is a rule with exceptions, not a weaker rule:
rung 0 still refuses a row holding both ends, so a declassifier admits
nothing until rung 1 admits (section 9, row 1). The `ProseGrant` sentence
form (ruling 3) carries the pin too: "data read from SOURCE does not reach
SINK except through NAME (sha256 HEX)". It lowers to the same field and
waits on the rung 0 sentence form, which is not lowered yet (section 4).

**Tags are per rule.** Rule `k` has its own source tag even when two rules
share a source pattern, and removing rule `k`'s tag never touches another
rule's.

### 10.2 Which code it is, and the pin

1. The declassifier is the def in the unit's `(defs ...)` whose name is
   `NAME`, matched on the bare name across every chapter of the unit. None,
   or more than one, answers `unanalysed: declassifier NAME not found`.
2. Its **closure** is that def and every def reachable from its body through
   any `name` reference, transitively, each taken once.
3. The **pin** is the SHA-256 of the UTF-8 bytes of the closure's def forms
   exactly as the IR prints them, each from `(def` to its matching `)`
   (string-aware: a parenthesis inside a string literal does not count),
   joined in the order the defs appear in `(defs ...)`, separated by one
   `\n`, with no trailing separator. The form includes the trailing integers
   the IR prints after the body. A pin that differs answers `unanalysed:
   declassifier NAME does not match its grant`. Pinning the closure means a
   helper rewritten under an unchanged declassifier changes the pin.
4. The `check` op reports `pins: [{"name": NAME, "sha256": HEX}]` for every
   def of the unit, so a grantor re-pins after reading the code.

### 10.3 What the closure may contain

A declassifier is a pure function from its arguments to its result. Every def
in its closure passes all of these checks, and the first failure, in this
order, answers `unanalysed: declassifier NAME is not pure (REASON)`, `REASON`
being the bracketed word:

1. `[label]`: no `name` whose IR type holds a `(label ...)` form, and no
   row-less effectful builtin (`read-line`, `read-line-cce`, `get-args`,
   `current-dir`, `net-status`, `key-zero`, `key-status`,
   `block-sector-count`); a row variable with no labels is not a label;
2. `[store]`: no raw-memory builtin (section 3), no in-place builtin
   (`list-push`, `list-snoc`, `list-set-at`, `list-insert-at`,
   `__record-set`, `__linked-list-push`), no `field-store`, no `set-state`,
   no `get-state`;
3. `[function]`: no `lambda`, and no def name referenced outside call
   position or applied to fewer arguments than its `(params ...)` count. The
   IR spells a call as nested one-argument `apply` forms; a call's argument
   count is the length of its `apply` spine down to the head `name`;
4. `[unknown call]`: every spine head is a `name` that is a def of the unit
   or a builtin with no label; never a parameter, a `let` or `do-bind`
   name, a pattern variable, or another expression;
5. `[form]`: none of section 5's unmodelled forms or builtins;
6. `[result]`, for the named def only: its IR result type is `int-default`,
   `boolean` or `char` (D2), and no parameter has a function type.

Outside its closure, the declassifier may be referenced only as the head of a
spine whose length equals its parameter count. Anything else answers
`unanalysed: declassifier NAME is used as a function value`. Checks run in
the order of the clause's entries, 10.2 before 10.3 before this one, and the
first `unanalysed` answer is the verdict even when a separate `path` also
exists.

### 10.4 What the analyses do at a call

At a call of a declassifier that rule `k` names, made from outside its
closure with a full spine, the call's result mask is computed in three
steps:

1. the declassifier's result as a function of its **arguments only**
   (each implementation's ordinary summary, substituted with the argument
   masks, with no caller program counter);
2. rule `k`'s tag removed from that;
3. the call site's program counter joined back in, with every rule's tag it
   holds, rule `k`'s included.

A call made under a branch on a tagged value therefore stays tagged: the
choice to call is not declassified. Tags of rules that do not name this
declassifier pass through step 2 unchanged. A tagged value passed to the
declassifier and also used elsewhere is still tagged at the other use. A
call from inside the closure (recursion) is an ordinary call with no tag
removed.

The first implementation keeps one mask per def (section 5), so a helper
the declassifier shares with other code carries every caller's tags, and
step 1's "arguments only" is coarser there than in the second. The two
answers can therefore differ in the direction section 6's probe A did. D4
governs that difference.

### 10.5 What it does not bound

A declassifier that returns its input, or `x / 1`, admits the whole value: the
pin says which code the grantor read, not that the code destroys information.
D2's scalar result bounds each call to 64 bits; a call budget per run (D2)
bounds the total. The covert channels of section 7 are unchanged.

### 10.6 Corpus plan

Source `net-status` (`Network.Read`, an integer), sink `print-line-uni`
(`Console.Write`). Rule A is `Network.Read -> Console.Write through count
sha256:P`, where `P` is the pin the generator computes from the IR it just
compiled; a "wrong pin" is `P` with its first hex digit changed. `count (n) =
n / 1000` unless a row says otherwise. Rows that need a second rule add rule
B, `Console.Read -> Console.Write`, with stdin as its source (`read-line`).

| program | rule | expected |
|---|---|---|
| print `show (count value)` | A | no path |
| print `show (count value)` | A without `through` | path |
| print `show value & show (count value)` | A | path |
| `if value == 0 then set-state (count 1) else set-state 0`, then print `show get-state` | A | path (pc joins after removal) |
| print `show (count value)`, pin wrong | A | unanalysed: does not match |
| print `show (count value)`, no def `count` | A | unanalysed: not found |
| `count` prints | A | unanalysed: not pure (label) |
| `count` calls `half`, which reads `net-status` | A | unanalysed: not pure (label) |
| `count` calls pure `half`, pinned with `half` | A | no path |
| as above, `half` edited and `P` kept | A | unanalysed: does not match |
| `count` uses a lambda | A | unanalysed: not pure (function) |
| `count` writes `set-state` | A | unanalysed: not pure (store) |
| `count` returns `text` | A | unanalysed: not pure (result) |
| `count` recursive (`count (n) = if n < 10 then n else count (n / 10)`) | A | no path |
| `count` passed to `apply-to (g) (x) = g x` | A | unanalysed: used as a function value |
| `count` applied partially (`let f = clamp 10 in f value`, `clamp` pinned) | A | unanalysed: used as a function value |
| a second def with `count`'s body, unpinned, printed through | A | path |
| rule A and rule B; stdin's line length through `count` to stdout | A and B | path (B names no declassifier) |
| `identity (n) = n`, pinned, print `show (identity value)` | `Network.Read -> Console.Write through identity sha256:P` | no path (10.5: a pin is not a proof of loss) |
| `count` and `opening` share pure helper `scale`, `opening` prints `show (scale value)` too | A | path, both implementations |
| `count` and `opening` share `scale`; `opening` prints `show (scale 3)` and `show (count value)` | A | no path in the second; the first may answer path (10.4, D4) |

Each implementation runs the corpus. A sabotage of each check in 10.3, and of
each step of 10.4, must move the row that check or step exists for. A
sabotage of step 2 must turn every `no path` row to `path`.

### 10.7 Rulings needed

- **D1, whose code may declassify.** (a) Any def the grantor pins, from the
  agent's own source. (b) Only defs from an audited conduit library (a
  `library` artifact such as `math`), whose pins the conduit publishes.
  Recommended: (a) with 10.3's purity check, because the pin already binds
  the audited closure, and (b) is a subset the grantor gets by pinning a
  library def.
- **D2, how much one run may carry.** (a) Scalar results only, plus an
  optional `at most N calls` after the entry, counting calls from outside the
  closure and refusing the run past `N`. (b) Scalar results only. (c) No
  bound. Recommended: (a). Without a call budget, a loop calling a scalar
  declassifier once per byte declassifies the whole input.
- **D3, what the pin hashes.** (a) The closure's IR form text (10.2), which is
  the code the analyses read and the runner executes (L-ARTIFACT); it changes
  whenever the compiler's IR printing changes, so grants are re-pinned per
  bundle with the `check` op's `pins`. (b) The source text, stable across
  compilers but one step removed from what runs. Recommended: (a).
- **D4, a difference between the two implementations.** Recommended: the
  same policy as rung 1's admission (section 9, row 1): a run is admitted only
  when both answer `no path`, so the coarser answer wins and a difference
  never admits. The last corpus row is expected to differ and is graded per
  implementation.
