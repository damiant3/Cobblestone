# Self-Host Parity Audit

## What parity means

See **principle 11 "Parity Is Narrow"** in `docs/10-PRINCIPLES.md`. Briefly:
the reference compiler (`src/`) is a **baseline**, not a mirror. The self-host
(`Codex.Codex/`) is expected to be a strict superset — doing more, doing
better, diverging on shape.

The parity requirement is narrow and sharp. Only things that affect the
**compilation output** must mirror precisely — lexing, parsing, desugaring,
type checking, lowering, codegen semantics. Two compilers operating on the
same source must reach the same program; `pingpong.sh` is the acceptance
test.

## Parity matrix

Legend: 🟡 partial / different · ❌ missing · ⏭️ deliberately diverged

### Data structures

| Item | Reference | Self-host | Status | Notes |
|------|-----------|-----------|--------|-------|

| `Maybe` / `Option` | ✓ stdlib | ❌ | ❌ | Exists in `foreword/Maybe.codex` but self-host compiler code does not use it. Callers work around via sentinel pairs. |
| `LinkedList` | ✓ | 🟡 | 🟡 | Type exists; `record-set` builtin + O(1) text-chunks mutation landed (`827ce6e`). Need audit of call sites. |
| `Queue` / `Stack` | ✓ stdlib | ❌ in self-host | ❌ | `foreword/Queue.codex` present but compiler does not consume it |
| `StringBuilder` | ✓ stdlib | ❌ in self-host | ❌ | Per-char `acc ++ char-to-text c` quadratic pattern (P10/P11) was fixed via chunked accumulation rather than introducing a StringBuilder abstraction |
| `Pair` | ✓ stdlib | 🟡 ad-hoc | 🟡 | `foreword/Pair.codex` exists; self-host uses per-purpose 2-field records |
| `TextSearch` (trie) | ✓ stdlib | ❌ in self-host | ❌ | `foreword/TextSearch.codex` present but unused |


### Diagnostics & error reporting

| Item | Reference | Self-host | Status | Notes |
|------|-----------|-----------|--------|-------|

| `let`-bind on effectful value rejected (CDX2033) | ✓ | ✓ | 🟡 | Both compilers emit the error. Ref uses `binding.Value.Span`; self-host uses `synthetic-span` because `add-unify-error` takes no span, so the diagnostic points at (0,0). Diagnostic-only divergence under "Parity is Narrow" — doesn't affect compilation output. Follow-up: thread binding span through `add-unify-error` in self-host. Repro: `samples/let-effectful-bug.codex`. |


### Debugging / crash behavior

| Item | Reference | Self-host | Status | Notes |
|------|-----------|-----------|--------|-------|

### Parser features

| Item | Reference | Self-host | Status | Notes |
|------|-----------|-----------|--------|-------|

| `(record-expr).field` chaining | 🟡 | 🟡 | 🟡 | Neither `ParseRecordExpression` (ref `Parser.Expressions.cs:214-258`) nor `parse-record-expr` (self-host `ParserExpressions.codex`) runs the `.field` loop on its return path, so `Foo { x = 1 }.x` does not chain — the trailing `.x` is left for the application loop's else-branch fallback to attach, which only fires after the record has been swallowed by an apply. Workaround in source: bind first (`let r = Foo { x = 1 } in r.x`). **Symmetric gap, not a parity divergence**: tracked here so future-Hex who fixes one side knows to fix the other in the same commit, otherwise pingpong byte-identity flips. The natural fix is calling `parse-field-access` (or ref's `while Current.Kind == Dot` loop) at both record-expr return sites — same shape as the `(atom).field` row above. |

### Type system

| Item | Reference | Self-host | Status | Notes |
|------|-----------|-----------|--------|-------|

| Union types with type variables | 🟡 | 🟡 | 🟡 | Needs dedicated pass |
| Polymorphism coverage audit | ? | ? | ❔ | Not yet done |


### Codegen / emission features

| Item | Reference | Self-host | Status | Notes |
|------|-----------|-----------|--------|-------|

| IL emitter | ✓ | ❌ | ⏭️ | Deliberate — .NET dependency being retired (BACKLOG) |
| x86-64 Linux user mode | ✓ | ❌ | ⏭️ | Ref-only target (emits syscalls, runs ring 3 under Linux). Self-host targets bare-metal x86-64 end-to-end (ring 0, port I/O, owns interrupts) — strictly harder, and what MM4 actually needs. Not a parity gap under "Parity is Narrow" — a deliberately-diverged target backend, same category as the retired IL emitter. |

## Top open gaps (priority order)

2. **Polymorphism coverage audit** — type system row marked ❔. No
   systematic test sweep exists; build one before claiming parity.

