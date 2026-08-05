# Spark -- open capabilities

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
| SPARK-2 | **`spark-boolean-test` agrees with its own `.expected`** | The test is RED and someone has to decide which side is wrong. `.expected` wants `self-union verts: 0`, `self-diff verts: 0` and `csg-info union: union`; the test produces `24`, `36` and `csg-info: union` -- so the self-boolean cases now return geometry where the file expects them to collapse to nothing, and the `csg-info` line has lost its operand. **It is not codegen: seed-compiled and SUT-compiled output are byte-identical**, checked 2026-08-04 by fester while validating a boot-code change (79 of 80 sampled tests matched; this was the one). So either MeshBoolean's self-union/self-difference behaviour changed and the file is stale, or it regressed and the file is right. From outside the app there is no way to tell, which is why this is here rather than fixed. Whoever owns CSG: run it, decide, and update the code or the file. |
| SPARK-1 | **Chapters in this app stop sharing definition names** | Measured 2026-07-21 by `build/sweep-app-classes.ps1 -Jobs 6` (2.6 min, 266 entry units; parse `test-output/clssweep/*.log` for `warning CDX3006`). **Private copies of a platform name** (cite it instead of defining it): `real-abs` (ColorPicker vs Gpu--DeviceMath, 1u). CDX3006 is a warning, not an error: each chapter sees its own definition, so this compiles and runs. What it costs is that a mention in a chapter defining NEITHER resolves by the order the build globs files, and moving a body between chapters silently changes what it means. **Compare the bodies before merging a pair -- same name does not mean same function**, and a pair whose arities differ cannot be merged at all.  |
