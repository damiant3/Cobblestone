# C64 -- open capabilities

App-domain backlog. `docs/PM/BACKLOG.md` is the register for the
platform -- compiler, forewords, OS, plugs, backends. Anything that is
this application's own behaviour lives here instead, so the platform
register stays readable.

The rules are the same ones: an entry says what is still missing and
nothing else, a closed entry is DELETED rather than annotated, and a
gap that is still real is never quietly dropped.

| # | Capability | State of the gap |
|---|---|---|
| C64-1 | **Chapters in this app stop sharing definition names** | Measured 2026-07-21 by `build/sweep-app-classes.ps1 -Jobs 6` (2.6 min, 266 entry units; parse `test-output/clssweep/*.log` for `warning CDX3006`). **Duplicated between this app's own chapters** (prefix one, or extract a shared chapter): `c64-ram` (Cpu6502 vs C64--Memory, 1u); `c64-ram` (Memory vs C64--Cpu6502, 1u); `char-rom` (Memory vs C64, 1u); `color-ram-base` (Memory vs C64, 1u); `gop-fb` (VicII vs C64, 1u); `gop-height` (VicII vs C64, 1u); `gop-width` (VicII vs C64, 1u). CDX3006 is a warning, not an error: each chapter sees its own definition, so this compiles and runs. What it costs is that a mention in a chapter defining NEITHER resolves by the order the build globs files, and moving a body between chapters silently changes what it means. **Compare the bodies before merging a pair -- same name does not mean same function**, and a pair whose arities differ cannot be merged at all.  |
