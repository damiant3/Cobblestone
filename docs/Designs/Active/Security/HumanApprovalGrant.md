# Human-approval grants: an action a human must sign, provably unbypassable

Owner: reek. Ruled (section 6); the build waits on ACCP L3
(`apps/accp/accp-backlog.md`, Nectry section 5 item 2, root 2026-10-31).

## 1. The gap

An ACCP grant today says WHICH effects and scopes a program may use
(`AgentCompilerControlProtocol.md` section 7). It cannot say "this one may
happen only when a named human approves it". Nectry calls that property
workflow integrity: which actions require human authorisation, and whether
the requirement can be bypassed (`docs/Reference/Nectry.md` section 3,
property 4). Nothing in `codex/os/trust/` names it: `Lease` has
`audit-required` (a record afterwards), and no field that blocks before.

## 2. The property

For a capability C granted with approval by approver set A:

> Every execution of an operation covered by C is covered by an approval
> that a key in A signed, whose condition held on that call at the moment
> of the call, and no path from the program to the external system C names
> avoids the check.

An approval's condition is a formula over the call, the time and the call's
data (section 4). Approving one call is the narrowest case: its condition is
"this instance", a digest over the label, the scope, the argument bytes, the
run's request fact and the call's sequence number, so it cannot be replayed
into another run, moved to another call or reused with other arguments. A
standing approval ("Email.Send for the next 10 minutes", "urgent replies
to Gary out of hours") covers every call its condition admits until
it expires or runs out of uses.

What the conduit can prove is that a holder of an approver key signed.
Whether that holder is a human is a key-custody fact (section 5), and the
design states it as an assumption rather than claiming it.

## 3. The bypasses and what closes each

| bypass | closed by | proof form |
|---|---|---|
| the program reaches the system without the effect | the type checker: an operation outside the row is CDX2031 | existing (`effect-covered-by`) |
| the program reaches the host function directly | the runner's link surface: the guarded import IS the gate, there is no other import for it | existing rule (ACCP section 2, item 3), plus one arm per gated label |
| the same lease set grants an unguarded capability covering the same scope (`Network "smtp.example"` beside `Email.Send` with approval) | admission refuses a lease set in which any capability without approval covers a guarded scope, by the type checker's covering rule and the scope-prefix rule | a decision over a finite lease set, with no run: `approval-cover-conflict` |
| the program sets the data a condition reads (marks its own mail urgent to fall under a standing approval) | admission refuses a relaxing condition that reads a field the program computes (section 4, "Who controls a field") | a decision over the condition and the label's field schema |
| the agent produces the approval itself | the approver key lives in the desk, out of the agent's reach (section 5) | assumption, stated per deployment |

Raw memory is the standing caveat: a runner that does not confine raw memory
is not an ACCP runner (ACCP section 2), so bare metal stays excluded here too.

## 4. Mechanism

**Conditions.** One closed language, `ApprovalCondition`, evaluated by the
gate over an `ApprovalContext` (label, scope, the call's named fields, the
current time):

- call: `CallIs (label) (scope)`, by the type checker's covering rule;
- time: `TimeBetween (start) (stop)` on the absolute clock, start inclusive
  and stop exclusive, and
  `WeekWindow (days) (start-minute) (stop-minute)` on PolicyProse's week
  minutes (day times 1440 plus minute of day);
- data: `FieldIs (name) (value)`, `FieldHas (name) (text)` and
  `FlagSet (name)`, over the fields the gated label's schema declares (for
  `Email.Send`: `to`, `subject`, `urgent`, and `answering`, the sender of
  the received message a reply answers); text comparisons ignore the case
  of ASCII letters;
- `CondAll`, `CondAny`, `CondNot`, `CondAlways` (the shorter names belong
  to `PolicyEngine`).

Evaluation has three values: a week window evaluated with no local clock is
undecidable, `CondNot` keeps it undecidable, `CondAll` fails on any false
child and is otherwise undecidable if any child is, `CondAny` holds on any
true child and is otherwise undecidable if any child is. A check never reads
an undecidable condition as true or false.

A week window's clock times are local to the zone the grant names with
`in <zone>` ("business hours" is a local fact; a condition with a clock
time and no zone refuses); the gate converts the absolute time
into that zone by its real rules, and a grant whose window edge falls on a
local time that the zone skips or repeats is refused at compile time rather
than read one way silently. The zone rules come from the time-zone campaign
(reek's row NEXT, CurrentPlan).

It has no arithmetic, no binding and no recursion beyond the formula's own
tree, so evaluation is linear in the formula's size, and a question about a
rule ("does any condition here ever admit `Email.Send` to an outside
address?") is a decision over a finite formula, not a run. That is the
sublanguage Nectry section 5 item 3 asks for, applied to one place.

**Who controls a field.** The arguments of a gated call are bytes the
program computed, and the program is the agent's. Every condition in this
design RELAXES the gate from its default of approving each call: a grant's
`when` waives approval wherever it is false, and a standing approval covers
every call it admits. A condition may therefore read only the call, the
time, and fields whose provenance lies outside the program: a value carried
unchanged from an attested input, such as a message signed by a key in the
NameBook. An email sender is not an attested input for an expensive
operation (Damian, 2026-11-01: "given the various ways to spoof an email,
email might never be a trustworthy source for certain expensive
operations"): a condition over an email's sender or headers never waives
approval of a call the grant marks expensive. Until provenance can be shown, which is the information-flow work
(`docs/Designs/Active/Compiler/AccpInformationFlow.md`), admission treats
every data field as program-controlled and refuses a condition that reads
one. Damian's example, "urgent and from Gary after business hours", is
admitted only once `answering` and `urgent` can be proved to come from the
received message; its time half is admitted today.

**Expensive calls.** A grant marks calls expensive with the clause
`expensive when <condition>` and, against splitting, `expensive past <n>
<field> per day` (the conduit sums the field over the day's calls in the
grant's zone from the journal, and every call that takes the running total
past `n` is expensive). Example: "Jake is permitted to use Payment.Send with
approval from Damian, expensive when amount above 100, expensive past 300
amount per day". The data predicates gain `FieldAbove (name) (integer)` and
`FieldBelow (name) (integer)`. A field's provenance is one of three classes:
`program` (computed by the program), `email` (the sender or a header of a
received message, which can be spoofed) and `signed` (carried unchanged from
a message signed by a key in the `NameBook`). On a call that is not
expensive, a relaxing condition may read `email` and `signed` fields; on an
expensive call it may read only the call, the time and `signed` fields, so
no email sender or header ever waives it. The `expensive` condition itself
may read `program` fields, because the daily total bounds what steering can
win: a program that keeps every call under the per-call line still crosses
the day's total, and every call past it is expensive.

**Grant.** `ProseGrant` gains `approvers : List Text` (empty means none),
`approval-when : ApprovalCondition` (default `CondAlways`) and
`approval-zone : Text` (empty when the clause names none). `PolicyProse` gains
the clause `with approval from <name>[, <name>] [when <condition>] [in <zone>]`.
`when` names the calls that NEED approval; every other call is waived. The
condition forms are `marked <flag>`, `answering <value>`, `to <value>`,
`between HH:MM and HH:MM`, `outside HH:MM to HH:MM`, `on weekdays`,
`on weekends`, joined by `and`, `or`, with `and` binding tighter than `or`;
`outside A to B on weekdays` holds on
a weekday before A or after B and all day on a weekend. For example: "Jake is permitted
to use Email.Send with approval from Damian when outside 09:00 to
17:00 on weekdays in Europe/London" asks for approval out of hours and lets
office-hours mail go. A standing rule in the other direction ("urgent replies
to Gary may go out of hours without asking") is a `StandingApproval` from the
desk, not grant text. `lower-policy` lowers a grant to a PolicyEngine
`Policy` rule (`PolicyProse.codex`, `pol-lower-grants`), not to a lease; a
grant with approvers lowers to the outcome `AllowWithApproval (keys)
(condition) (zone)`: the approvers' Ed25519 public keys from the `NameBook`,
the condition as its `Approval` encoding (`PolicyEngine` cites no `Approval`
chapter, so its citers across the OS do not link one), and the zone. `eval-policy` allows it
like `Allow` and names it "allowed with approval". A policy whose approver
resolves to the grant subject's own key refuses to lower, and a clause that
does not read completely (an unknown word, a dangling `and`, a window whose
start is not before its stop, an hour past 24:00, no name, a zone that is
empty or two words) refuses the grant and becomes a commentary line.
A lease drawn under that rule (`lease-draw`) copies the three into new
`Lease.approval : List AgentIdentity`, `Lease.approval-when` and
`Lease.approval-zone` fields, beside `audit-required`
(`LeaseManager.codex`); a condition that does not decode, or an empty
approver set, refuses the draw, and a handoff keeps the sender's approval.
Built: graded by `codex/test/apps/policy-prose-approval`. `clarifier-reflect` prints them as
`approval: Damian when ...`, and says which fields the condition reads.

**Names to keys.** An `AgentIdentity` is a public key only
(`AgentProtocol.codex`), so a name in grant text resolves through a
`NameBook` (`codex/os/trust/NameBook.codex`, val, shared with policy
subjects): name to Ed25519 public key, signed by the grantor, carried
beside the policy and cited by hash. A name the book does not hold refuses
the grant at compile time; the agent cannot add a name, because it cannot
sign as the grantor.

**Records** (new chapter `codex/os/trust/Approval.codex`). Text fields are
encoded as UTF-8 at this boundary (R-CCE); every field is length-prefixed
(4 bytes, big-endian) in the order listed, integers are 8 bytes big-endian,
and a condition is encoded as its tree in prefix order, one tag byte a node.

- `ApprovalRequest`: label, scope, argument SHA-256, the named fields, the
  SHA-256 of the lease the call runs under, run request fact, sequence
  number, expiry. Its digest is SHA-256 of that encoding.
- `Approval` (one call): request digest, approver public key, verdict
  (1 approve, 0 deny), and an Ed25519 signature over the ASCII tag
  `codex-approval-v1`, then the request digest, then the verdict byte.
- `StandingApproval`: the lease's hash, an `ApprovalCondition`, not-before,
  not-after, maximum uses, approver public key, and an Ed25519 signature
  over the tag `codex-standing-v1` and that encoding.
- The tags keep an approval signature from verifying as any other signed
  object, and either kind from verifying as the other.
- `ApprovalVerdict = Approved (Text) | Unapproved (Text)`; `Approved` names
  the approval that covered the call.
- `approval-check : ApprovalRequest, Approval, List AgentIdentity, Integer -> ApprovalVerdict`
  and `standing-check : ApprovalRequest, StandingApproval, Integer, List AgentIdentity, ApprovalClock -> ApprovalVerdict`,
  where the Integer of `standing-check` is the uses already spent, the last
  Integer of `approval-check` is the current time, and `ApprovalClock` is
  `{ now, week-minute }`. `week-minute` is the call's LOCAL time in the zone
  the grant names (`TimeZone`'s `utc-to-local`), day times 1440 plus minute
  of day with Monday 0, never the UTC week minute; it is -1 when the caller
  has no conversion, and then every week window is undecidable. A refusal
  names the first failed condition: key not in the set, signature invalid,
  digest differs (one-call) or lease differs (standing), condition
  undecidable, condition false, not yet valid, expired, uses spent, denied.
  Built: `codex/os/trust/Approval.codex`, graded by
  `codex/test/apps/approval-check`.

**Gate** (rulings D1, D4). For a gated label the runner links a gate in place
of the host function. On each call it builds the `ApprovalRequest` and
evaluates the lease's `approval-when`; if false the call proceeds (that is
the grant's own waiver). Otherwise it looks for a standing approval that
`standing-check` admits, then for a one-call approval, and performs the call
under the first that covers it. With neither, the gate sends the request to
the approval channel (never the agent's channel) and suspends the run: the
program's process stays blocked inside the host import, its compute clock
paused, and `codex_run` answers `awaiting_approval` with a resume token
(128 random bits, bound to the run's request fact), the request digest and
the request's fields. A later call with that token waits for the run to
finish or to suspend again; the token carries no approval, because the
approval arrives from the desk and never through the agent. A denial, or no
answer by the approval deadline, traps the run with stage `approval` and the
reason. A session holds at most a fixed number of suspended runs, and each
keeps its memory until it ends. A run that makes no gated call needs no
approval and never suspends.

**Evidence.** The run record lists every gated call: request digest, the
approval that covered it (one-call or standing, by fact hash) and the
verdict; a standing approval's uses are counted from the journal.
`verify-fact.ps1` re-checks each signature and each condition against the
recorded fields, so an auditor can confirm offline that every gated call was
covered.

## 5. Key custody: the desk (ruling D2)

The conduit verifies a signature; the approver key's location decides
whether the signer is a human. On a box where the agent holds a shell, any
key file under the same account is readable by the agent, so the key lives
in the desk on bare metal, on our own stack, out of the agent's reach. The
conduit sends each `ApprovalRequest` to the desk over `TrustTransport`
(`codex/os/trust/TrustTransport.codex`), the desk shows the label, scope,
fields and run summary, and the human answers with a one-call approval, a
standing approval drawn from the request ("approve calls like this for the
next hour"), or a denial, which the desk signs and returns. The desk never
signs without a human input event.

## 6. Rulings

Damian, 2026-11-01: "D1 a, D2 a, D3 should allow a full gamut of time and
context options: per call being one, but also, time boxed, data dependant
special flags (e.g. If marked urgent and from gary after normal business
hours), D4 resume token". D1 is the in-run gate per call; D2 is the desk on
bare metal as the approver device; D3 makes an approval a condition over the
call, the time and the call's own data, one call being one case of it; D4
is `awaiting_approval` with a resume token. Sections 2 and 4 are written to
these rulings.

## 7. Prerequisites and rungs

**Blocked today on ACCP L3.** The conduit refuses every `lease` field and
its WASM runner links nothing beyond Console (`apps/accp/README.md`), so no
capability exists to gate. ACCP rung 6 (the lease check for non-ambient
rows) and a first granted effect come first. Relaxing conditions over data
also wait on the information-flow design's provenance (section 4).

| rung | deliverable | graded by |
|---|---|---|
| 1 | `Approval.codex`: records, encoding, digests, `ApprovalCondition` and its evaluator, `approval-check`, `standing-check` | an arm per refusal reason; the degenerate inputs (zero key, small-order key, empty arguments, empty condition: L-DEGENERATE), which `ed25519-verify` already refuses and which the arm keeps refused through the composition; a replay arm (one-call approval, next sequence number); cross-object arms (a CDX signature presented as an approval, a one-call approval presented as standing); condition arms at and one past each time edge (L-SHAPE) and for each data predicate and combinator; a uses-spent arm |
| 2 | `ProseGrant.approvers` and `approval-when`, the `with approval from ... when ...` clause, the `NameBook` lookup, lowering to the rule and the lease, the clarifier line | `PolicyProse` arms: parse each condition form, lower, reflect; a grant without the clause has an empty set; a name missing from the book refuses; Damian's example parses to the condition section 4 gives |
| 3 | admission: `approval-cover-conflict`, the refusal of a condition over a program-controlled field, and the expensive-call class rule. Built: `lower-policy` refuses a grant whose approval condition reads any call field, or has a week window and names no zone (`pol-approvals-admissible`), and refuses a policy in which the same subject holds an unguarded grant covering a guarded one by label or family (`pol-cover-conflict`); arms in `policy-prose-approval`. Open: a cover across families (`Network` beside `Email.Send`), which needs the resource each gated label reaches from rung 4's link surface, and the expensive-call class rule | arms: guarded `Email.Send` with an unguarded `Network` covering its host refused, with a disjoint scope admitted; a `when` or standing approval reading `urgent` refused, one reading only the time admitted; a clock time with no zone refused; on an expensive call a standing approval reading an `email` field refused and one reading a `signed` field admitted; four payments of 80 under `expensive past 300 amount per day` leave the first three not expensive and make the fourth expensive |
| 4 | the runner gate, suspension and the resume token, the run record's gated-call list (after ACCP rung 6). Blocked on the runner: programs run under the `wasmtime` command line (`apps/accp/serve.ps1` bridge `execute`) and `ab-imports` (`AccpWasm.codex`) admits only WASI `fd_read` and `fd_write`, so no gated label has a host import to replace with a gate; the first step is a runner that links one host function per gated label, whose two routes section 8 costs | through `mcp-test.ps1`: covered by standing, covered by one-call, suspended then approved, denied, deadline passed, mutated arguments, a resume token from another run refused; `verify-fact.ps1` refuses a record whose approval signature or condition fails |
| 5 | the desk approver pane over `TrustTransport` (D2) | a run approved from the desk, the same run refused when the desk's key is absent, and no signature without an input event |
| 6 | field provenance for conditions (after the information-flow design) | "urgent replies to Gary" admitted as a standing approval only when `urgent` and `answering` are proved to come from the received message |

Costs (R-COST): per gated call, one SHA-256 over the arguments, one condition
evaluation linear in its size, and one Ed25519 verify for the approval that
covers it (one verify allocated 110,880 bytes on 2026-11-01, measured for
main 52306; `codex/test/cost/ed25519-heap` holds it under 1,000,000; its
time is not measured). The cover-conflict check is quadratic in the lease
set's capability count, which is small and bounded by the frame. A suspended
run holds its memory until it ends, which is why a session caps the count.

## 8. Rung 4's runner: two routes, costed

Ruled (Damian, 2026-11-01): "For now, lets go with A.  Record B as our prefered solution, if tokens were free." Route A is built, starting with the stage 0 speed probe; route B is recorded as the preferred route if tokens were free.

**What the gate needs from a runner:** one host function per gated label that
the program cannot reach any other way, a run that can stop inside that host
function and resume in a later request, and a compute clock that stops while
the run waits. The `wasmtime` command line gives none of the three:
`serve.ps1`'s `execute` bridge spawns `wasmtime run` once per run under a Job
Object (`HostIo.cs`, `Child.Run`), the run links WASI and `--preload` modules
only, and its epoch timeout runs on the wall. Both routes below replace the
`execute` stage only; the compile and lens stages stay on the command line.

**Measured, 2026-11-01:** the box holds the `wasmtime` 45.0.0 command line
alone (`C:\Program Files\Wasmtime\bin\wasmtime.exe`), with no C API library.
Across the 26 executes of the last `apps/accp` test run, 25 took 23 to 29 ms,
mostly spawn and JIT, and the 26th was the `loop` fixture timing out at its
1000 ms limit. No execute in the readable `apps/accp` test logs (196 samples)
took more than 100 ms with spawn included, so no recorded program is
compute-bound. Every lens-emitted module carries a runtime prelude of about
100 scalar instructions, including `return_call`, `i64.trunc_sat_f64_s` and
the f32 forms, plus SIMD (`v128.load`, `v128.store`, `i32x4.bitmask`,
`i64x2.bitmask`, and the vector arithmetic `WasmEmitter.codex` emits for
`Vector` types, with v128 parameters and locals). The program imports `fd_read` and
`fd_write`, plus `random_get` and `env.on_key` when the program needs them
(`WasmEmitter.codex:2535-2538`). The conduit restores its heap to a
per-request base (`Accp.codex:12-16`, `AccpMcp.codex:12-16`).

**Route A: our own runner, in Codex, inside the conduit.**

1. A decoder for the sections the lens emits (`AccpWasm.codex` already reads
   the header, the type section and the import section) and a validator (types,
   stack heights, branch targets). The validator stays because the runner is
   the confinement boundary ACCP section 2 names, even for a module our own
   pipeline built.
2. An interpreter for about 110 instructions over preallocated byte buffers
   (operand stack, call stack, linear memory). Every load and store is
   bounds-checked, every branch and call is charged fuel, and no instruction
   allocates (L-PEROBJECT).
3. WASI `fd_read` and `fd_write` over the run's input and its capped output.
   Each gated label is a Codex function that calls `Approval.codex` directly,
   with no bridge round trip.
4. Suspension is free in kind: the run is data, and suspending is not
   stepping. The cost is placement. A suspended run outlives the request, so
   its slot must be allocated below the per-request heap base (L-BELOWMARK).
   N slots at the 64 MiB execute memory cap cost N times 64 MiB of the
   conduit's 4 GiB Job, for example 256 MiB for 4.
5. The clock is fuel, which is deterministic, and it stops for free when
   stepping stops.

Size: an estimate, not a measurement, of 2,000 to 3,000 lines of Codex,
against 4,635 in `WasmEmitter.codex`. Trust: the `execute` stage leaves
third-party code. Risk: speed. A Codex interpreter's slowdown against the JIT
is unmeasured on this box. At a slowdown S, the largest program that fits
the 10 s budget is one that computes for 10 s / S under the JIT. Float semantics (NaN bits, saturating truncation,
f32 rounding) are graded against `wasmtime` as the referee (L-REFEREE).

**Route B: embed `wasmtime` through its C API.**

1. A new pinned binary: the C API library (`wasmtime.dll`, from the
   wasmtime release's c-api archive), which is not on the box. The library is
   a runtime, not a compiler toolchain. Whether the 2026-10-31 ruling ("no new
   toolchains") covers a runtime library is Damian's to say.
2. A P/Invoke surface in `HostIo.cs`, through the same `Add-Type` mechanism as
   the Job Object calls: an engine with epoch interruption, a store with a
   memory limiter, a linker with WASI and one defined function per gated label
   (a C# delegate as the callback), then compile, instantiate and call
   `_start`.
3. A thread per run. A gated call's callback hands the request to the
   `serve.ps1` loop, which forwards the request to the conduit. The bridge
   today is conduit-driven request and reply (`AccpProtocol.codex:317`), so
   `execute` becomes a loop: `execute`, then `gated`, then `gate-answer`, until
   the run ends. The callback blocks on a wait handle to suspend, and the
   epoch ticker skips the store while the callback blocks, which pauses the
   clock.
4. Isolation moves. The run leaves its own child process and Job Object for
   the `serve.ps1` process, so a fault or a leak in `wasmtime` takes the
   adapter down, and the memory cap becomes `wasmtime`'s limiter alone.
   Keeping a child process needs a host executable in C#, and whether `pwsh`
   7's `Add-Type` can emit one is unverified.

Speed: unchanged. Trust: confinement and the gate's link surface sit in
third-party code plus our C# shim, and the run record gains the library's
digest beside `engine_sha256`. Upkeep: the C API is pinned per `wasmtime`
major version.

**Recommendation: route A**, with stage 0 deciding the speed risk before any
gate code exists. Route A adds no binary and runs the gate in the same
language as `Approval.codex`, and suspension and the paused clock come with
its design. Route B costs a new binary, a reversed bridge and a weaker
isolation boundary in return for the JIT's speed.

**Stage 0, the speed probe.** The recorded programs cannot grade an
interpreter: every recorded program fits the budget at any slowdown under 100
(L-VACUOUS). The probe runs three compute-bound programs through the real
compile and lens path: an integer loop, an f64 loop and an allocating list
build, each sized to compute for about 1 s under `wasmtime`. Each program's
JIT time is the `run` phase minus an empty program's `run` phase (spawn and
JIT setup), and S is the interpreter's time over that JIT time, with the
interpreter built as the conduit is (the PE console plug) and timed on the
same box in the same hour (L-WINDOW). An S of 10 or more, which puts the
largest program that fits the budget under 1 s of JIT compute, sends the
choice back to Damian with the three numbers.

**Stage 0 result (2026-11-01): S is 208 to 311, so the bound fails.** The
interpreter (`apps/accp/AccpRun.codex`, shelved, see the blu row) runs every
lens-emitted scalar instruction and traps on SIMD. Each program printed
the same bytes under both runners. The times below are the best of 3 runs,
each less the empty program (15.6 ms JIT, 13.7 ms interpreter):

| program | JIT ms | interpreter ms | S |
|---|---|---|---|
| integer loop, 10^8 steps | 536 | 135,322 | 252 |
| f64 loop, 10^8 steps | 321 | 66,736 | 208 |
| list build, 100 rounds of 100,000 pushes | 63 | 19,505 | 311 |

At S of 250, the largest program that fits the 10 s budget computes for
40 ms under the JIT. The interpreter spends about 270 ns per counted step:
the x86-64 backend dispatches an integer `when` as a compare chain, and
compiles a tail call to another function as a call, not a jump. Only the
self tail call in `wr-run` runs flat. Every operation outside the eight
inlined ones therefore returns through `wr-step` with its state in memory.
An S under 10 needs the interpreter 25 times faster. How much of that factor
dispatch tuning recovers under those two codegen properties is unmeasured.
The engines that remove interpretation are a compiler from wasm to x86-64
inside the conduit, or a runner over the IR rather than over wasm, and both
are larger than section 8's 2,000 to 3,000 line estimate.
