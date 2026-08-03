# __record-set field 19+ silent fail -- RESOLVED, NO LONGER REPRODUCIBLE

**Original report:** 2026-06-15 (CL 4395)
**Investigated:** 2026-06-18 (CL 4727 seed)
**Status:** Cannot reproduce. Condition retired.

## Original Problem

Adding `result-dest` as the 19th field of the RISC-V plug's `RvState`
record caused `__record-set st "result-dest" value` to silently drop
the write. The field read back the old value with no error or diagnostic.

Confirmed on `RvState` (19 fields) and suspected on `CodegenState`
(39 fields). Workaround was to split into sub-records with <15 fields.

## Investigation (2026-06-18)

Tested `__record-set` on the CL 4727 seed across four scenarios:

1. **40-field uniform Integer record** -- `__record-set` on fields 1,
   20, 39, 40 all correct. Chained sets preserve untouched fields.
2. **25-field uniform Integer record** -- all fields settable.
3. **23-field mixed-width record** (8-byte pointers, 4-byte u32,
   2-byte u16, 1-byte bool) -- `__record-set` on all width categories
   correct, including chained sets across widths.
4. **20-field record** -- fields 19 and 20 settable.

No failure reproduced in any scenario.

The original target `RvState` no longer exists -- it was split into
sub-records as part of the workaround. `CodegenState` still has 39
fields and an analogous layout works correctly.

## Likely Resolution

The bug was likely fixed incidentally by one of the intervening CLs
between CL 4395 and CL 4727 (CCE layout improvements, offset
calculation fixes, record field width-sort changes, or emitter
separation). The exact fix CL is unknown.

## Remaining Hazard

`__record-set` **mutates in place** -- the returned record shares
backing memory with the input. This is confirmed by the alias test:

```
let old = r.f20               -- captures 20
in let r2 = __record-set r "f20" 999
in r.f20                      -- reads 999, not 20
```

This aliasing is the remaining `__record-set` danger, not field count.
CDX6020 diagnostic flags the width-sorted evaluation hazard in record
constructors. The `revised` expression (CL 4217) provides safe
multi-field update syntax.

## Sub-record Workaround

The sub-record pattern in the codebase (e.g., `TcoState` inside
`CodegenState`) is harmless and provides good organizational structure
but is no longer required for correctness.
