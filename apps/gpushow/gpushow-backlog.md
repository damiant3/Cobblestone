# GpuShow -- open capabilities

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
| GPUSHOW-3 | **The live-compile block is built into all 39 pages and waits on Damian to publish** | **RULED (Damian, 2026-09-29): publish with the 2026-09-29 release.** `tools/build-pages.mjs` assembles the pages from one skeleton and `tools/page-data.json` (`--check` compares bytes, 39 of 39), and `tools/live-block.html` puts the live-compile block into every page from one place: it compiles the page's kernel with `codex-compiler.wasm`, lowers it with `wgsl-stdio.wasm`, compares against the shipped `.wgsl`, and is editable with Revert. `tools/live-verify.mjs` grades it on `cube.html`. OPEN: publishing it is Damian's decision (no GitHub update since 2026-09-07 names it). View with `node apps/gpushow/tools/serve.mjs 8099`, then `http://127.0.0.1:8099/web/cube.html`; WebGPU needs a secure context, so not `file://`. |
