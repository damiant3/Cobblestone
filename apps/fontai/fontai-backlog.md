# FontAI -- open capabilities

App-domain backlog. There is no platform-wide register any more:
`docs/PM/BACKLOG.md` was deleted 2026-07-23 and must not be recreated.
`docs/PM/CurrentPlan.md` carries the shape and the priority order for
the platform. Anything that is this application's own behaviour lives
here.

The rules are the same ones: an entry says what is still missing and
nothing else, a closed entry is DELETED rather than annotated, and a
gap that is still real is never quietly dropped.

| # | Capability | State of the gap |
|---|---|---|
| FONTAI-1 | **`FontExtract` compiles and can actually read a font** | RED, 3 CDX3002 errors, measured 2026-08-08 by `build/sweep-app-classes.ps1 -Jobs 8`. Two separate problems, and the second is the blocking one. **(a) `opening` is a malformed `act` block.** `FontExtract.codex:12-22` chains `let ... in` and then uses `in` again as a statement separator (`in print-line (...)` at 15, `in if ...` at 16), so `font`, bound by `else let font = ttf-parse buf` at 17, is out of scope by lines 20 and 21 and the compiler says `Undefined name: font`. In an `act` block statements are separated by newlines, not by `in`; the fix is to write the sequence as an act body rather than as one let-chain. **(b) `read-byte` does not exist anywhere in the tree** (`FontExtract.codex:63`, inside `read-bytes-loop`, which is what `read-all-bytes` is built on). So even with the scoping repaired this program cannot obtain its input: there is no byte-at-a-time stdin primitive to call. Closing this means deciding how a console program reads binary input -- a foreword capability question -- and only then fixing the act block. The TrueType side is not implicated: `ttf-parse`, `ttf-glyph-for-char` and the `tf-head`/`th-units-per-em` accessors it uses all resolve. |
