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
| STARMAP-6 | **The constellation figures are unreachable, and the file cannot be regenerated** | `import-hyg.ps1` patched the constellation OFFSET into byte 28 of the header, which is `con_count`'s slot, so every `starmap.dat` it has written carries an offset where the count belongs (the shipped file reads `con_count = 3787928`, which is exactly `dso_offset + 125 * 80`) and a zero where `con_offset` belongs. The 239 line segments are in the file and no reader following the documented layout can reach them. The importer is FIXED (shelved CL 22295, `$ms.Position = 32`), but `data/hyg_v42.csv` is NOT in the tree, so the shipped `.dat` cannot be rebuilt here and the defect stays in the data. Either fetch the HYG v4.2 CSV from codeberg.org/astronexus/hyg and regenerate, or teach the loader the shipped file's actual layout. Until then the page has no stick figures and its constellation toggle moves a flag nothing reads. Measured 2026-09-02. |
| STARMAP-7 | **The deep-sky objects are loaded and unused** | The module reads `dso_count` (125) and `dso_offset` from the header and publishes both, and nothing reads them after that. The 80-byte DSO records carry the Messier objects and the notable galaxies with their own names, kinds and descriptions, so the page could draw and identify them beside the stars for the cost of one more selection pass. The star path is the precedent and the record layout is in `StarMapWasm.codex`'s File Format section. |
| STARMAP-9 | **`CelestialTypes` and `StarCatalog` are dead weight in the wasm entry, and the page duplicates their logic in JS** | The wasm entry stopped citing them when the catalogue moved into `starmap.dat`, so the eighty hand-written objects, `bv-to-rgb`, `co-screen-size` and `format-dist-ly` are reachable from `StarMapScene` and `StarDb` only. The page reimplements the colour ramp and the distance formatting in JS because it holds the fields as doubles, and that duplication is now known to diverge: the Codex `format-dist-ly` was wrong by a factor of 1,000 until 2026-09-08 and the JS was not, which is the cost of two implementations of one rule. Both chapters ARE now built and run, by `codex/test/apps/starmap-suite.codex`, so the earlier reason not to delete them no longer applies; the open question is whether the page should call them instead of duplicating them. |

## What the module publishes

`StarMapWasm.codex` owns the memory map AND the star record layout, and
`web/starmap-codex.html` and `sm-verify.mjs` both read them from there. It is a
contract between three files and a fourth thing nobody can edit, the shipped
`starmap.dat`: change an address, a stride or a field offset in the chapter and
both readers must move with it, or the page draws a plausible wrong sky rather
than failing.

The module carries no catalogue. It is handed one, and it refuses a file whose
magic, version, size or star count does not agree, each with its own numbered
error, because a loader that accepts a bad file draws a sky out of noise.
