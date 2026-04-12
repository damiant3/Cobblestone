# Self-Host Parity Audit

## Why this exists

The self-hosted compiler (`Codex.Codex/`) is supposed to be modeled on the
reference compiler (`src/`). The reference is the source of truth for what
Codex *should* do. The self-host is supposed to match it, feature for feature,
so we can eventually cut the cord and not lose anything.

In practice, large chunks of the reference's foundational infrastructure
never made it into the self-host. We keep discovering these gaps one at a
time, painfully, often at the moment we most need the missing feature.

Recent examples of gaps we hit:
- **Parser diagnostics**: The reference parser produces structured error
  diagnostics. The self-host parser just crashes or produces garbage on
  malformed input. We discovered this mid-debugging after days of reading
  serial port error codes.
- **`Maybe` type**: Standard foreword library, never copied in. Tried to
  pull in the HAMT from the foreword, couldn't because its lookup returns
  `Maybe a`.
- **Set / Map / hash map**: No hash map primitive. Every symbol lookup,
  type env lookup, chapter scope lookup, and func-offset lookup in the
  self-host is a linear scan of a list.
- **Bitwise ops (xor, and, or, shl, shr)**: Missing from the self-host.
  Surfaced when we tried to implement a standard hash function (FNV-1a,
  Jenkins) — all of them use xor. Had to use modular arithmetic workarounds.
- **Floats**: `Number` in the self-host was `Integer` under the hood until
  recently; discovered by accident.
- **Bare-metal crash diagnostics**: No GPF handler. A bad pointer or null
  deref produces silent QEMU hang with no output. Every bug is a 4-hour
  scavenger hunt through debug prints injected into source files.

## What this is not

Not a call to backport everything from the reference. Some reference
features are dumb and we want to replace them. That's fine — document the
decision.

## What this is

An *audit* — a systematic pass through the reference compiler identifying
every feature, data structure, and runtime behavior, and documenting for
each one:

| Reference has | Self-host has | Decision |
|---------------|---------------|----------|
| Feature X     | ✓ matches      | No action |
| Feature Y     | ✗ missing      | Port it  |
| Feature Z     | ✗ missing      | Replace with different design — see doc |
| Feature W     | ✓ differs      | Either bring into parity or document divergence |

The audit becomes a living document. Every new feature added to either
compiler should be reflected in it.

## Scope of the audit

At minimum, cover:

### Data structures
- List (✓ both)
- Set, Map, HashMap / HAMT (✗ self-host missing)
- Maybe / Option (✗ self-host missing)
- LinkedList (partial)
- Stack / Queue (?)
- Buffer / flat mutable memory (✓ both)

### Primitives & runtime
- Integer (✓ both, but widths/semantics need verification)
- Number / Float (? — discovered self-host was secretly Integer)
- Text / String (✓ both, but CCE vs Unicode boundary handling)
- Character (✓ both)
- Boolean (✓ both)
- Bitwise ops: and, or, xor, shl, shr, not (✗ self-host missing)
- Arithmetic: abs, min, max, mod, div semantics on negatives (?)
- Comparison operators on text, integers, with CCE ordering (?)

### Diagnostics & error reporting
- Parser diagnostics (source location, severity, message) (✗ self-host missing)
- Type checker diagnostics (✓ both? — verify)
- Name resolution diagnostics (✓ both? — verify)
- Codegen diagnostics (✓ both? — verify)
- Structured diagnostic output format (?)

### Debugging / crash behavior
- Bare-metal GPF / page fault / stack overflow handler with serial dump (✗ missing)
- Source location tracking through IR lowering (?)
- Stack trace on .NET host (✓ exceptions work)

### Parser features
- Error recovery — skip to next top-level def instead of cascading failure (✗ self-host missing)
- Source span preservation through AST (?)
- Indentation sensitivity — formal spec and matching behavior (?)

### Type system
- Parameterized records (✗ — reference C# emitter chokes on them; self-host too?)
- Union types with type variables (✗ similar)
- Polymorphism / type inference coverage (?)
- Effect types (?)

### Runtime behaviors
- `list-snoc` in-place growth semantics (✓ both)
- `list-with-capacity` behavior on .NET — was broken, fixed in progress
- `heap-save` / `heap-restore` / `heap-advance` semantics on bare metal
- GC-like behavior or lack thereof (bump allocator documented)
- Text equality semantics (CCE vs Unicode)

### Codegen / emission features
- x86-64 bare metal (✓ both, reference frozen)
- x86-64 Linux user mode (? self-host)
- IL / C# emitter (✓ self-host has CSharpEmitter)
- Codex text emitter (for pingpong) (✓ self-host)
- ELF writer variants (✓ self-host)

## How to do the audit

Not "one big document dump" — one agent pass per section. Each section
produces a short report that becomes a row in the parity matrix.

Order of priority (highest first):

1. **Diagnostics & error reporting** — without these, every other bug is
   100x harder to find. Do this first.
2. **Data structures** — Map/Set unblock most of the performance work and
   eliminate entire classes of O(n²) bugs.
3. **Bitwise ops** — blocks proper hash functions and any serious crypto /
   low-level work.
4. **Crash diagnostics on bare metal** — one GPF handler pays for itself
   in hours saved per week.
5. **Parser error recovery** — so a missing paren reports a location
   instead of cascading garbage.

Lower priority:
6. Type system features, codegen variants, runtime behaviors.

## Outcome

After the audit:
- We have a document that tells any new contributor (or future Hex) what
  the self-host has and doesn't have, without needing them to trip over it.
- The critical missing pieces become individual tickets with known scope.
- Future "why doesn't X work" sessions start by checking the audit, not
  by discovering the gap through binary crashes.

## Not in scope

- Actually implementing the missing features (that's follow-up work per row)
- Making the self-host byte-identical to the reference (it's allowed to diverge)
- Rewriting the reference (it's frozen)

## Opening question

Who does the audit? It's big enough that one pass by one agent in one
session won't cut it. Suggest: one agent per section, 6-8 sessions total,
results merged into a single matrix doc.
