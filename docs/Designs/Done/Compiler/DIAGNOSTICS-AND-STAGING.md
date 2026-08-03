# Compiler Infrastructure Wishlist

Adjacent pieces of internal compiler infrastructure that a professional
compiler would have and Codex currently has partially or not at all.
Each is its own body of work.

## A. Inspection & introspection

- **DWARF `.debug_line`**: PC→source-line mapping. Step-level debugging
  and `break file.codex:42` need it. IRExpr now carries a source span
  (the prerequisite); emit `.debug_line` from those spans.
- **DWARF parameter / local-variable DIEs**, type info for structured
  values.
- **Reverse mapping (emitted instruction → IR node)**: IR node → source
  span is in place; the instruction → IR direction is not. Needed for
  "why did this code get emitted" queries and source-level profiling.
- **Find-all-references / go-to-definition**: symbol table with
  backlinks. Cheap to design in now, expensive to add later.

## F. Provenance -- transformation origin tag

Every IR node records *what transformation produced it* in addition to
the source span it came from (span landed separately). Concrete example:
when desugaring turns `x <- e; body` into `e >>= (\x -> body)`, the
generated lambda's provenance is "desugared from act-bind at line N".
Crashes in lowered code blame the user's original construct, not the
intermediate IR.

## G. Self-verification / roundtrip tests

- **Typecheck → emit → re-typecheck emitted IR**: the emitter shouldn't
  produce IR its own typechecker rejects. Pingpong covers this
  end-to-end; a unit-sized variant would be narrower signal.
- **Codex text → binary → Codex text** (once disassembly exists).

## J. Hash-consing / canonical forms

Structurally-identical IR subtrees share memory. Benefits: equality
becomes pointer-equality; caching by argument graph. Cost: a cons-table
per allocation. Only worth it if profiling shows overhead.

## K. Plugin points / phase composition

Phases as first-class values -- orderable, replaceable, instrumentable.
Enables experimental phases, external consumers (LSP, doc generators),
and research work without forking. Big refactor; not urgent.
