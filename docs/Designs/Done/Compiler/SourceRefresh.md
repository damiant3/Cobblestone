# Source Refresh

## Goal

Use the CodexEmitter as a canonical code formatter. Compile each .codex
source file through the compiler with `--target codex`, replace the source
with the output, review for semantic correctness. The emitter defines the
style — the source conforms to it.

## Why

The self-compiled output (stage1) and the hand-written source (stage0)
have hundreds of formatting differences that make code review impossible.
Fixing them one at a time by hand is not sustainable. The emitter should
be the single source of truth for formatting.

## Approach

### Phase 1: Formatting Test File

Create a small `.codex` file (`samples/format-test.codex`, ~100-200 lines)
that exercises every formatting construct:

- Sum types (variants with 0, 1, N fields)
- Record types (1 field, N fields)
- Record construction (inline single-field, multi-line multi-field)
- If/then/else (simple inline, multi-line, else-if chains)
- Flat dispatch chains (>3 branches)
- Short chains with column-aligned else (2-3 branches after let/in)
- When/match with patterns (var, ctor, wildcard, nested)
- Let/in bindings (single, chained)
- Function application (simple, with field-access args, with nested calls)
- Field access as argument (parens required)
- Binary operators (mixed precedence, no unnecessary parens)
- String concatenation (++)
- List operations (cons ::, append ++, literals)
- Lambdas
- Do blocks
- Nested expressions (records inside if, let inside match arms)

Iterate on the emitter until `codex build format-test.codex --target codex`
produces output identical to the input (modulo name mangling).

### Phase 2: Source Refresh

Once the emitter passes the formatting test:

1. For each `.codex` source file:
   ```
   codex build <file> --target codex --output-dir refresh/
   ```
2. Diff the refreshed output against the original
3. Review: formatting changes should be the only diffs
4. If semantic differences appear, they indicate either:
   - A compiler bug (fix the compiler)
   - An emitter bug (fix the emitter)
5. Replace the original with the refreshed output
6. Rebuild and test to confirm nothing broke

### Phase 3: Fixed Point

After the source refresh:
- Stage0 (source) matches the emitter's style
- Stage1 (compiled output) matches the emitter's style
- The diff between stage0 and stage1 shows only:
  - Name mangling (module prefixes on colliding names)
  - Module markers removed (no `module:` / `end module` in output)
  - Module ordering (alphabetical file sort vs source declaration order)
- Everything else should be identical

## Current Emitter Status

Working:
- De-hoisted typedefs via IRModuleSection
- 1-space indent
- Multi-line records for >1 field
- Column-aligned else under its if
- Flat dispatch chains (>3 branches)
- Precedence-aware binary parens
- Field-access args wrapped in parens

Known gaps (to fix during Phase 1):
- Some `let/in` + `if` combos don't align perfectly
- Long string concatenation lines (template expressions)
- Record field ordering in emitter vs source may differ
- When/match arm formatting edge cases

## Not In Scope

- Changing the Codex language syntax
- Modifying the self-hosted CodexEmitter.codex (that's a follow-up for
  fixed-point convergence after the C# emitter is canonical)
