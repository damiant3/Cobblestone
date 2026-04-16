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

| `Maybe` / `Option` | ✓ stdlib | 🟡 | 🟡 | Self-host parser now uses `Maybe` records (`b84f8c2`, `2a10ed9`, `edb7aec`) — sentinel-pair sums gone from Syntax files. Remaining adoption: type checker, name resolver, emitters still use ad-hoc records where `Maybe` would read cleaner. |
| `LinkedList` | ✓ | 🟡 | 🟡 | Type exists; `record-set` builtin + O(1) text-chunks mutation landed (`827ce6e`). Need audit of call sites. |
| `Queue` / `Stack` | ✓ stdlib | ❌ in self-host | ❌ | `foreword/Queue.codex` present but compiler does not consume it |
| `StringBuilder` | ✓ stdlib | ❌ in self-host | ❌ | Per-char `acc ++ char-to-text c` quadratic pattern (P10/P11) was fixed via chunked accumulation rather than introducing a StringBuilder abstraction |
| `Pair` | ✓ stdlib | 🟡 ad-hoc | 🟡 | `foreword/Pair.codex` exists; self-host uses per-purpose 2-field records |
| `TextSearch` (trie) | ✓ stdlib | ❌ in self-host | ❌ | `foreword/TextSearch.codex` present but unused |


### Diagnostics & error reporting

| Item | Reference | Self-host | Status | Notes |
|------|-----------|-----------|--------|-------|

| Unifier diagnostics carry source spans | ✓ | ✓ | ✓ | Self-host `unify` and its family (`unify-resolved`, `unify-rhs`, `unify-structural`, `unify-fun`, `unify-constructed-args`, `unify-mismatch`) now take a `SourceSpan`; `add-unify-error` takes a span. Callers (binary, unary, if, application, list, match, let-effectful, def body) pass the appropriate AExpr span via `aexpr-span`. Covers CDX2001 (type mismatch), CDX2010 (infinite type), CDX2033 (let-binds-effectful), CDX3002 (unknown name). Mirrors reference `Unify(a, b, span)`. |


### Debugging / crash behavior

| Item | Reference | Self-host | Status | Notes |
|------|-----------|-----------|--------|-------|

### Parser features

| Item | Reference | Self-host | Status | Notes |
|------|-----------|-----------|--------|-------|

### Type system

| Item | Reference | Self-host | Status | Notes |
|------|-----------|-----------|--------|-------|

| Polymorphism coverage audit | ? | ? | ❔ | Not yet done |


### Codegen / emission features

| Item | Reference | Self-host | Status | Notes |
|------|-----------|-----------|--------|-------|

| IL emitter | ✓ | ❌ | ⏭️ | Deliberate — .NET dependency being retired (BACKLOG) |
| x86-64 Linux user mode | ✓ | ❌ | ⏭️ | Ref-only target (emits syscalls, runs ring 3 under Linux). Self-host targets bare-metal x86-64 end-to-end (ring 0, port I/O, owns interrupts) — strictly harder, and what MM4 actually needs. Not a parity gap under "Parity is Narrow" — a deliberately-diverged target backend, same category as the retired IL emitter. |

## Top open gaps (priority order)

2. **Polymorphism coverage audit** — type system row marked ❔. No
   systematic test sweep exists; build one before claiming parity.

