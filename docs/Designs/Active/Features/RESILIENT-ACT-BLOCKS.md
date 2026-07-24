# Resilient Act Blocks — Strategy, Retry, and Escalation

**Date**: 2026-05-09
**Author**: Cam (Opus 4.6, agent)
**Status**: Partially shipped -- the inline trying / falling-back-to / on-failure form (IrTry) is live end-to-end (lexer, lowering, lift, scoper, x86 emit). Pending: the strategy / resolve / resolve-with / Criticality machinery.

**Scoping of the three regions** (decided 2026-07-16):
`body`, `falling back to` and `on failure` are each their own binding
region over the scope the `trying` was written in. A binding made in one
is not visible in the others and does not survive the `end`. The reason
is not tidiness: a fallback runs precisely when the body did NOT finish,
so a body binding is by construction possibly-unwritten when a fallback
reads it -- and the body re-runs from the top on every attempt, so its
slot holds one failed attempt rather than one value. There is no reading
of a shared binding that is well-defined. Pinned by
`codex/test/scope-try-region`.
**Depends on**: Effect system (done), `IrHandle` / `IrWithTimeout` (done),
capability refinement (design)

---

## The Problem

Act blocks today are fire-and-hope. Every effectful operation either
succeeds or the program halts. There is no language-level mechanism for:

- Retrying a failed operation
- Falling back to an alternative implementation
- Escalating based on the criticality of the payload
- Composing these policies declaratively

This surfaces everywhere: a log write that discovers a corrupt file has
no way to pick a new file. A network call that times out has no way to
retry. A notification that cannot reach a prompt has no way to escalate
to a louder channel. The caller must encode all recovery logic inline,
or accept that failures are fatal.

The PowerShell test harness works around this with retry loops and
timeout budgets in procedural code. That logic belongs in the language.

---

## The Two Axes

### Axis 1 — Operation

The *what*: log, notify, persist, fetch, connect. Each operation has a
set of possible implementations — strategies — ranked by preference.

### Axis 2 — Criticality

The *how much it matters*: a debug trace that fails to log is ignorable.
An audit event that fails to log is a halt-the-world problem. The
criticality of the payload determines how hard the system tries and how
far it escalates.

Resolution plans need both axes. The same operation (log) with different
criticality (debug vs. audit) should produce different behavior when the
primary strategy fails.

---

## The Language Surface

### Criticality Levels

A built-in variant, available without import:

```
  Criticality =
   | Ignorable
   | Routine
   | Important
   | Critical
```

`Ignorable`: best-effort, silent failure is acceptable.
`Routine`: should succeed, warn on failure, do not escalate.
`Important`: must succeed, retry aggressively, escalate on exhaustion.
`Critical`: must succeed or halt the program. All strategies tried.

### Strategy Declarations

A strategy is a named, typed function that performs an operation. The
`strategy` keyword groups alternative implementations under a common
signature:

```
  strategy notify-user : Text -> [Console] Nothing where
    prompt   (msg) = act print-line msg end
    alert    (msg) = act print-line ("ALERT: " & msg) end
    escalate (msg) = act print-line ("ESCALATE: " & msg) end
```

Each arm is a distinct implementation. They share a type signature.
Order matters: the first arm is the preferred strategy, the last is
the most desperate.

### Resolution Plans

A resolution plan binds a strategy to retry/escalation behavior based
on criticality. The `resolve` keyword introduces a plan:

```
  resolve notify-user
    on Ignorable -> try prompt once
    on Routine   -> try prompt 3 times then try alert once
    on Important -> try prompt 3 times then try alert 3 times
                    then try escalate once
    on Critical  -> try prompt 5 times then try alert 5 times
                    then try escalate 3 times then halt
```

`try <arm> <n> times` retries the named strategy arm up to N times.
`then` chains to the next fallback. `halt` aborts the program if all
strategies are exhausted. Without `halt`, exhaustion returns a
`Resolution` result the caller can inspect.

### Using a Resilient Operation

At the call site, the caller provides the criticality:

```
  log-event : Text -> Criticality -> [Console, FileSystem] Nothing
  log-event (msg) (crit) = act
   resolve-with crit (notify-user msg)
  end
```

`resolve-with` is the built-in that triggers the resolution plan. The
caller says *what* to do and *how important it is*. The plan decides
*how* to do it.

### Inline Resilience in Act Blocks

For cases where a full strategy declaration is overkill, act blocks
gain inline resilience syntax:

```
  write-log : Text -> [FileSystem] Nothing
  write-log (msg) = act
   trying 3 times
    append-file "app.log" msg
   falling back to
    append-file "app.log.backup" msg
   on failure
    print-line ("LOG LOST: " & msg)
  end
```

`trying <n> times` retries the block up to N times.
`falling back to` introduces an alternative block.
`on failure` is the final handler if all attempts are exhausted.

This desugars to a strategy + plan internally — the inline form is
syntactic sugar, not a separate mechanism.

### Criticality Inference from Context

When criticality is not provided explicitly, the compiler can infer
a default from the effect context:

```
  act
   resolve-with Routine (log-entry msg)
  end
```

But when a function's type signature declares a criticality parameter,
the caller must provide it. No implicit escalation.

---

## Interaction with Existing Language Features

### Effect Handlers

Strategies compose with effect handlers. A strategy arm that uses
`[Network]` can be handled by a `with Network` handler that provides
a mock or a retry-aware implementation:

```
  with Network (retry-network 3) do
    resolve-with Important (fetch-data url)
```

The handler provides the network implementation. The resolution plan
provides the retry/fallback logic. These are orthogonal.

### Capability Refinement

Resolution plans respect capability refinement (direction, scope,
time-boxing). A strategy arm that needs `[FileSystem.Write "/logs/"]`
will fail cleanly if the capability is not granted — the plan catches
the failure and moves to the next arm.

### with-timeout

`IrWithTimeout` already exists in the IR. Resolution plans integrate
with timeout by treating a timed-out operation as a failed attempt:

```
  strategy fetch-data : Url -> [Network] Response where
    fast   (url) = with-timeout 2 [Network] (http-get url)
    slow   (url) = with-timeout 30 [Network] (http-get url)
    cached (url) = read-cache url
```

The plan can order strategies by aggressiveness: try the fast path
first, fall back to a longer timeout, finally try the local cache.

### Structured Concurrency

`fork`/`await` from the concurrency design interact with resilience.
A forked task that fails can be retried by its resolution plan
independently of the parent. The parent's `await` sees either the
resolved result or a `Resolution` failure:

```
  act
   task <- fork (resolve-with Important (fetch-data url))
   result <- await task
   when result
    is Resolved (data) -> process data
    is Exhausted (reason) -> handle-failure reason
  end
```

---

## The Resolution Type

All resilient operations return a `Resolution` result:

```
  Resolution a =
   | Resolved (value : a)
   | Exhausted (attempts : List StrategyAttempt)

  StrategyAttempt = record {
   arm-name : Text,
   attempt-number : Integer,
   outcome : AttemptOutcome
  }

  AttemptOutcome =
   | Succeeded
   | Failed (reason : Text)
   | TimedOut
   | CapabilityDenied
```

When a plan ends with `halt`, exhaustion aborts. Otherwise, the caller
receives `Exhausted` and the full attempt history — which strategies
were tried, how many times, and why each failed.

---

## Desugaring to IR

### New IR Nodes

```
  IRExpr +=
   | IrTry (IRExpr) (Integer) (IRExpr) (CodexType) (SourceSpan)
   | IrResolve (Text) (Criticality) (List IRExpr) (CodexType) (SourceSpan)

  IRStrategy = record {
   name : Text,
   arms : List IRStrategyArm,
   plan : List IRPlanEntry,
   signature : CodexType,
   span : SourceSpan
  }

  IRStrategyArm = record {
   arm-name : Text,
   body : IRExpr,
   span : SourceSpan
  }

  IRPlanEntry = record {
   criticality : Criticality,
   steps : List IRPlanStep
  }

  IRPlanStep =
   | PsTry (Text) (Integer)
   | PsHalt
```

### Lowering

`trying N times <body> falling back to <alt>` desugars to:

```
  IrTry body N (IrTry alt 1 failure-expr type span) type span
```

`resolve-with crit (strategy args)` desugars to:

```
  IrResolve strategy-name crit [args...] type span
```

The x86-64 backend emits `IrTry` as a loop with an attempt counter
and a success flag. On failure, control flows to the fallback expression.
`IrResolve` emits a strategy dispatch table indexed by criticality.

---

## Codex Prose Form

In CPL, the prose reads naturally:

```
 We say: To notify the user of a message with a given criticality,
 first, try the prompt strategy up to three times.
 Then, if the prompt strategy is exhausted, try the alert strategy.
 Finally, if the criticality is Critical and all strategies are
 exhausted, halt the program.
 This is written:

  resolve notify-user
    on Ignorable -> try prompt once
    on Routine   -> try prompt 3 times then try alert once
    on Critical  -> try prompt 5 times then try alert 5 times
                    then halt
```

The prose describes the resolution semantics in CPL-compliant English.
The notation following `This is written:` is the executable form.

---

## Syntax Summary

### New Keywords

```
strategy  resolve  trying  times  once
falling   back     to      on     failure
halt      then
```

Of these, `then` and `on` are already reserved. The remaining keywords
are new: `strategy`, `resolve`, `trying`, `times`, `once`, `falling`,
`back`, `to`, `failure`, `halt`.

### Grammar Productions

```
strategy-decl   ::= 'strategy' name ':' type 'where' strategy-arm+
strategy-arm    ::= name param* '=' expr

resolve-decl    ::= 'resolve' name resolve-branch+
resolve-branch  ::= 'on' criticality '->' plan-step ('then' plan-step)*
plan-step       ::= 'try' name integer 'times'
                  | 'try' name 'once'
                  | 'halt'

try-expr        ::= 'trying' integer 'times' act-body
                    ('falling' 'back' 'to' act-body)*
                    ('on' 'failure' act-body)?

resolve-expr    ::= 'resolve-with' expr '(' expr ')'
```

---

## Comparison to Other Systems

| System | Mechanism | Codex Difference |
|--------|-----------|-----------------|
| Erlang supervisors | Process restart strategies | Codex operates at the expression level, not process level |
| Common Lisp restarts | Condition/restart separation | Codex adds criticality as a first-class axis |
| Rust Result + ? | Monadic error propagation | Codex adds retry and escalation, not just propagation |
| Polly (.NET) | Policy-based retry/circuit-breaker | Codex makes policies part of the type system |
| Haskell exceptions | IO-layer catch | Codex integrates with effect handlers, not a separate mechanism |

The closest relative is the Erlang supervisor tree: declarative restart
policies separate from the code that might fail. Codex adds criticality
as a first-class concept and integrates with the effect/capability system
rather than a process model.

---

## Open Questions

1. **Delay between retries.** Should `try prompt 3 times` retry
   immediately, or should there be a backoff policy? Options: always
   immediate (simplest), explicit `with delay <ms>` clause, or
   criticality-driven defaults (Ignorable = no delay, Critical =
   exponential backoff).

2. **Strategy composition.** Can strategies cite other strategies? If
   `notify-user` escalates to `call-phone`, and `call-phone` has its
   own resolution plan, do the plans compose or is nesting explicit?

3. **Compile-time exhaustiveness.** Should the compiler require that
   every criticality level has a plan entry? Or allow partial plans
   where unlisted criticality levels get a default (e.g., Ignorable
   defaults to "try once, ignore failure")?

4. **Runtime cost.** On bare metal with no GC, attempt tracking
   (the `List StrategyAttempt` in `Exhausted`) allocates. Should
   attempt history be opt-in (e.g., only when the caller pattern-matches
   on `Exhausted`)?

5. **Interaction with linear types.** If a strategy arm consumes a
   linear resource and fails, is the resource lost? Should strategy
   arms be forbidden from consuming linear resources, or should the
   plan include a cleanup clause?

6. **Logging the resolution.** Should the resolution machinery
   automatically produce observable events (which strategy was tried,
   how many times, final outcome)? This connects to the broader
   observability story (the `Observe` quire).

7. **User-defined criticality.** Is the four-level `Criticality`
   variant sufficient, or should users be able to define domain-specific
   criticality types (e.g., `SecurityCriticality` with different levels
   than `LogCriticality`)?
