# StarMap -- open capabilities

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

## What the module publishes

`StarMapWasm.codex` owns the memory map AND the star, deep-sky and constellation layouts, and
`web/starmap-codex.html` and `sm-verify.mjs` both read them from there. It is a
contract between three files and a fourth thing nobody can edit, the shipped
`starmap.dat`: change an address, a stride or a field offset in the chapter and
both readers must move with it, or the page draws a plausible wrong sky rather
than failing.

The module carries no catalogue. It is handed one, and it refuses a file whose
magic, version, size, star count, deep-sky section or constellation section does
not agree, each with its own numbered
error, because a loader that accepts a bad file draws a sky out of noise.
