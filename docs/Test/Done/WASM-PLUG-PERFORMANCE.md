# WASM Plug Performance Investigation

## Summary

The WASM plug (WasmPlug.codex + WasmEmitter.codex) hangs or times out
when processing IR larger than ~1.8MB. The compiler selfhost (30K lines,
1.3MB source) builds in 30 seconds, but the WASM plug cannot process
2.1MB of IR in 10 minutes.

## Confirmed Non-Issues

These were investigated and ruled out:

- **Text concatenation `&`** — the x86-64 `__str_concat` has a fast path
  that extends in-place when the string is at the heap top. The
  right-to-left concatenation pattern in `emit-wat-defs` should hit the
  fast path every time. Confirmed by reading X86_64TextHelpers.codex.

- **`list-at` complexity** — Lists are array-backed; `list-at` is O(1)
  (direct index scaling + load). Confirmed by reading
  X86_64Builtins.codex line 197-212.

- **`text-length` / `list-length`** — Both are O(1) (load from header).

- **Binary search** — `bsearch-arity-pos` with O(1) `list-at` is
  genuinely O(log n). Not a bottleneck.

## Observations

- The plug produces 0 bytes of output after 60+ seconds on 2.1MB IR.
- The plug successfully processes 1.8MB IR (126KB WASM) in ~5 minutes.
- The output is all-or-nothing (`print-line-uni output` at the end).
- The plug runs in codex-vm with 4GB RAM.
- The 2.1MB IR comes from adding foreword Signal modules (Synth,
  Oscillator, Envelope, Filter, AudioEffect, MusicTheory) which bring
  deep transitive dependency chains.

## Hypotheses Still Open

1. **Tokenizer on 2.1MB text** — The tokenizer walks every character.
   With CCE encoding this should be O(n) but n=2.1M. If there are
   quadratic patterns in token list building (`list-push` copies?),
   this could be slow.

2. **IR parser S-expression tree building** — `build-tree` constructs
   an S-expression tree from tokens. If tree construction has quadratic
   behavior for deeply nested expressions, this could hang.

3. **Heap exhaustion** — The plug allocates aggressively during
   parsing and emission. With 2.1MB of IR, the intermediate data
   structures (token list, S-expression tree, arity map, string table,
   WAT text) might exhaust the 4GB arena, triggering the out-of-memory
   handler which loops or halts.

4. **Specific IR pattern** — The foreword Signal modules may produce
   IR with characteristics (deep nesting, many string literals, complex
   type expressions) that trigger worst-case behavior in a specific
   emitter function.

## Next Steps

- Run the plug with `-debug` mode and set breakpoints on key functions
  (tokenize, build-tree, emit-wasm-chapter, emit-wat-defs) to measure
  phase timing.
- Check heap high-water mark after the plug runs on 1.8MB vs 2.1MB IR.
- Profile which phase consumes the time: parse or emit.
- Check if `list-push` on very large token lists triggers copy behavior.

## Discovery

Found 2026-06-08 by agent fester during Spark audio buildout.
The CL 3313 build (1.8MB IR, 126KB WASM) works. Adding Signal foreword
modules pushes IR to 2.1MB and the plug hangs.
