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

| Polymorphism coverage | ✓ | ✓ pingpong | 🟡 | Sweep at `samples/polymorphism-coverage.codex` (13 defs): identity, const, polymorphic sum + map, polymorphic record + accessors, nested `Pair-r a b` / `List (Opt Integer)`, polymorphic function-as-arg, concrete instantiations — all green in the reference. Self-host's own source uses `Maybe a`, `ParseExprResult`, etc. throughout and pingpong is byte-identical, so the same shapes work there too. **Known gap (ref):** record construction with permuted type vars fails — e.g. `swap : P a b -> P b a / swap (p) = P { f = p.s, s = p.f }` defines fine but call sites emit CDX2001 type-mismatch (Integer↔Text swap). Definition-side polymorphism is intact; instantiation flips the field types incorrectly. |

