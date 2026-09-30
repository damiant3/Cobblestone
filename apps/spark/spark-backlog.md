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
| SPARK-6 | **The browser demo runs and looks bad** | Damian, 2026-09-02, on seeing it: spark and fishtank both "need a lot of work" and stay off the public site until they are brought up. The module is not at fault, and this row does not guess at what is: spark_render returns 334 faces and draws 50,276 of 307,200 pixels. What IS known and probably contributes: the scene is a still, rendered once with no animation and no interaction; it is 640x480 upscaled to the canvas width; and until val 23436 it was entirely greyscale (the shading dropped the light's colour; fixed, and the demo still ships white lights). Whoever takes this looks at the page before changing anything. |
