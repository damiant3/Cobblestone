# Spark -- open capabilities

App-domain backlog. There is no platform-wide register:
`docs/PM/BACKLOG.md` must not be recreated.
`docs/PM/CurrentPlan.md` carries the shape and the priority order for
the platform. Anything that is this application's own behaviour lives
here.

The rules are the same ones: an entry says what is still missing and
nothing else, a closed entry is DELETED rather than annotated, and a
gap that is still real is never quietly dropped.

| # | Capability | State of the gap |
|---|---|---|
| SPARK-6 | **The browser demo runs and looks bad** | Damian, 2026-09-02, on seeing it: spark and fishtank both "need a lot of work" and stay off the public site until they are brought up. The module is not at fault, and this row does not guess at what is: spark_render returns 334 faces and draws 50,276 of 307,200 pixels. What IS known and probably contributes: the scene is a still, rendered once with no animation and no interaction; it is 640x480 upscaled to the canvas width; and until val 23436 it was entirely greyscale (the shading dropped the light's colour; fixed, and the demo still ships white lights). Whoever takes this looks at the page before changing anything. |
| SPARK-7 | **Test coverage: 93 of 108 library chapters are reached by no computing test** | `pwsh build/coverage-census.ps1 -Tree apps/spark -List` names them (2026-10-26); 22 of them are reached by no GUI-driven program either. Open: a computing test for each, or a deletion. |
| SPARK-8 | **The Qwen shader does not build** | `pwsh codex/plugs/wgsl/run.ps1 -Src apps/spark/QwenKernels.codex -Out <file>` exits 6 at blu 49527 on seed 68C40416D08F8396 (2026-10-26): the emitter refuses 4 `CODEX_REFUSED_approximate_equality_exact`, the `~0` in `qw-amax-pairs` (`QwenKernels.codex:736`), and writes nothing, so `build-studio-page.mjs` stops at `qwen.wgsl`. Open: a comparison the WGSL plug lowers, with the argmax tie-break kept. |
