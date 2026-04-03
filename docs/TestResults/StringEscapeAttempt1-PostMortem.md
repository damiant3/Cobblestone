# String Escape Attempt 1 — Post-Mortem

**Date**: 2026-04-03
**Agent**: Cam
**Result**: Reverted. Root cause not in escape-one-char comparison logic.

## What We Tried

Changed `escape-one-char` and `escape-char` in `CodexEmitter.codex` from
`char-code-at` with string literals to `char-code` with char literals:

```
-- Before
escape-one-char (c) =
 if c == char-code-at "\\" 0 then "\\\\"
 else if c == char-code-at "\"" 0 then "\\\""
 else if c == char-code-at "\n" 0 then "\\n"
 else char-to-text (code-to-char c)

-- After (reverted)
escape-one-char (c) =
 if c == char-code '\\' then "\\\\"
 else if c == char-code '"' then "\\\""
 else if c == char-code '\n' then "\\n"
 else char-to-text (code-to-char c)
```

Also attempted fixing paren-dropping in `emit-binary` by splitting
`wrap-binary-child` into left (`<`) and right (`<=`) variants. Both reverted.

## What We Observed

### Bare-metal output (stage1) still had identical bugs after the fix

The `char-code` change compiled and the ELF ran, but stage1 still contained:
- `\I\n` instead of `\"\"` (double-quote escaping)
- Literal newlines instead of `\n` escape sequences
- `s` instead of `\n` in some positions

This proves the bug is **not** in the comparison constants of escape-one-char.

### Detailed symptom analysis

**Newline bug**: In a single escape-text call, the FIRST newline is escaped
correctly as `\n`, but the SECOND newline in the same string may fail —
producing either a literal newline (0x0A) or the letter `s` (Unicode 115,
CCE 19).

**Double-quote bug**: The backslash prefix IS emitted (meaning the comparison
matched), but the character after the backslash is wrong:
- First `"` in a string → `\I` (backslash + `I`, Unicode 73, CCE 43)
- Second `"` in same string → `\n` (backslash + `n`, Unicode 110, CCE 18)

**Key pattern**: The SAME escape-one-char function returns DIFFERENT wrong
results for identical input on successive calls within the same escape-text-loop.
This rules out static .rodata corruption or wrong RodataFixup addresses (those
would produce the same wrong result every time).

### Hex evidence

Stage0 (correct): `?? \"\")"` = bytes `3f3f 20 5c22 5c22 2922`
Stage1 (buggy):   `?? \I\n)"` = bytes `3f3f 20 5c49 5c6e 2922`

The backslash (0x5C) is present in both, confirming the if-branch matched.
But the second byte is wrong: 0x49 (`I`) and 0x6E (`n`) instead of 0x22 (`"`).

In CCE terms: the return string `"\\\""` should contain [CCE 86, CCE 72] but
the output acts as if it contains [CCE 86, CCE 43] and [CCE 86, CCE 18] on
successive calls.

## What We Ruled Out

1. **Comparison constant values**: `char-code-at "\n" 0` returns 1 correctly
   (the comparison matches — the backslash prefix proves it). Switching to
   `char-code '\n'` didn't help.

2. **Static .rodata corruption**: The wrong results DIFFER between successive
   calls to the same function, so the .rodata string isn't statically wrong.

3. **CCE-to-Unicode table**: The `"` character (CCE 72 → Unicode 34) converts
   correctly in other contexts (string literal delimiters show up as `"` in
   stage1). The bug is upstream of the conversion.

4. **`__str_concat` correctness**: Code review of the fast path (in-place
   extend) and slow path (full copy) found no corruption bugs. The fast path
   correctly checks `ptr + align8(len+8) == HeapReg` before extending.

## What We Think Is Happening

The varying wrong results on successive calls point to one of:

1. **Heap interaction with return strings**: escape-one-char returns .rodata
   pointers for escape cases and heap-allocated strings for pass-through.
   The `__str_concat` fast path extends the accumulator in place when the
   right operand is .rodata (no heap allocation happened). Something in this
   interaction may corrupt the accumulator or the source string `s`.

2. **TCO parameter corruption in escape-text-loop**: The TCO'd loop updates
   `s`, `i`, `len`, `acc` via temporaries. If a register or spill slot is
   clobbered by the escape-one-char call or __str_concat, subsequent
   iterations would read wrong values from `s`.

3. **x86-64 code generation bug**: A register allocation or spill/reload
   issue in the compiled escape-text-loop that causes `char-code-at s i`
   to read the wrong byte position after certain heap state changes.

## Recommended Next Steps

1. **Binary-level debugging**: Use GDB attached to QEMU to set a breakpoint
   in the compiled escape-one-char and trace the register values on the
   second newline call vs the first. Compare the `s` pointer, `i` value,
   and the byte read by `char-code-at`.

2. **Instrument escape-text-loop**: Add serial debug output before each
   escape-one-char call (print `i` and `c` values) to see if the input
   character code is wrong (string corruption) or correct (return value
   corruption).

3. **Check TCO spill slots**: Review the x86-64 codegen for TCO parameter
   updates in functions that call other functions with heap side effects.
   Look for caller-saved registers being used across call boundaries.

4. **Simplify the reproduction**: Find the shortest text literal that
   triggers the bug (a string with two newlines should suffice) and trace
   the bare-metal execution for just that case.

## Kept Changes

The sem-equiv normalizer improvement was kept (not reverted):
- `\{` → `{` and `\}` → `}` normalization (3 mismatches resolved, 63 → 60)
- These are not valid Codex escapes, so both forms produce the same character

## Other Findings

- **`end module main` in stage1**: Filed as separate bug `end-module-in-output`.
  The bare-metal emitter outputs `end module main` indented inside the main
  function body. The scanner correctly skips it during header scanning, so the
  issue is likely in how the main function's body range is determined.

- **Paren-dropping bug**: `a - (b + c)` emitted as `a - b + c` due to
  `needs-binary-wrap` using strict `<` instead of `<=` for the right operand.
  Fix was correct but reverted with the rest. Should be re-applied separately
  once the string escape issue is resolved.
