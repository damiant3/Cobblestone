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
| STARMAP-2 | **There are two star maps and only one of them is built** | `web/starmap.html` is a self-contained WebGPU renderer, 36 KB of hand-written JS with inline WGSL, fetching its own `data/starmap.dat` (3,790,812 bytes, 118K HYG v4.2 stars). It contains no `WebAssembly` and no reference to any Codex output, nothing builds it, and it is not what the landing page links to. The Codex module beside it renders the 80 objects that live in `StarCatalog.codex`. Neither half is wrong; what is missing is a decision. Either the module grows a `starmap.dat` loader and the 118K catalog becomes the product (measured as feasible: 118,000 x 48 B is a 5.7 MB table beside the 3.79 MB file, inside the 16 MB the module already declares, and parsing straight into the table allocates nothing per object), or the JS page and its 3.79 MB of data are deleted as superseded. Until it is decided the repository ships a catalog 1,475 times smaller than the one on disk and says nothing about it. Measured 2026-09-02. |
| STARMAP-3 | **`catalog-all` is not the whole catalog, and `catalog-total` says it is** | `catalog-all = catalog-stars` (`StarCatalog.codex`), the 50 named stars alone, while `catalog-total` next to it answers 80 by summing the three counts. A reader who wants every object has to know to append `catalog-messier` and `catalog-galaxies` by hand, which is what `StarMapWasm.sm-catalog` now does. The name promises what the definition does not deliver and no caller can see the difference until a Messier object is missing from a result. |
| STARMAP-4 | **Three chapters are in no build and no test** | `StarDb.codex` (the octree and its range and nearest-neighbour queries), `Constellations.codex` (13 stick figures) and `tests/TestStarMap.codex` (7 sections) are reached by nothing. The wasm entry does not cite them, no battery compiles them, and `build/quire-map.ps1:50` is a quire registry rather than a build. They have never been compiled by anything, which is what `StarMapWasm.codex` itself was until it was rewritten, so assume defects of the same class rather than that they work. The constellation figures are the one of the three the page would visibly use. |
| STARMAP-5 | **`co-dist` is populated but nothing reads it** | `star-dist` answers a real distance again (it used to square coordinates that overflow a 64-bit integer for the far galaxies, so the catalog could not be built at all), but the shipped page computes distance from x, y and z itself because it holds them as doubles. So the field is carried through the star table's cost and its correctness is graded by nothing. Either the page reads it or the star record drops it. |

## What the module publishes

`StarMapWasm.codex` owns the memory map and `web/starmap-codex.html` and
`sm-verify.mjs` both read it from there. It is a contract between three
files: change an address or a stride in the chapter and both readers move
with it, or the page draws a plausible wrong sky rather than failing.
