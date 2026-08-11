# Explorer -- open capabilities

App-domain backlog. There is no platform-wide register any more:
`docs/PM/BACKLOG.md` was deleted 2026-07-23 and must not be recreated.
`docs/PM/CurrentPlan.md` carries the shape and the priority order for
the platform. Anything that is this application's own behaviour lives
here.

The rules are the same ones: an entry says what is still missing and
nothing else, a closed entry is DELETED rather than annotated, and a
gap that is still real is never quietly dropped.

Design: `apps/explorer/design/Active/`. The working server is
`apps/explorer/run-designers-demo.ps1`; the page builder is
`build/build-apps.ps1`.

| # | Capability | State of the gap |
|---|---|---|
| EXP-6 | **`CardEmitter` compiles: it needs a card shape that no chapter has** | `CardEmitDemo` and `ExcaliburSlice` are RED, 15 CDX3002 errors each, measured 2026-08-08 by `build/sweep-app-classes.ps1 -Jobs 8`. Both fail inside `apps/explorer/CardEmitter.codex`, so it is one defect surfacing twice. **It is not a missing cite, and changing the cite makes it worse -- that was tried.** There are TWO `Mana` chapters and TWO `Card` chapters: `apps/games/codexmagic` (quire `CodexMagic`) and `apps/games/magic` (quire `Magic`), and they are different games. CardEmitter uses `cmc`, `format-mana-cost` and `mana-cost-*`, which exist ONLY in `Magic`, and sets a `mana-cost` field, which exists only on `Magic`'s `CardTemplate`. It also uses `defense`, `is-token` and the `LegendaryMythic` rarity, which exist ONLY in `CodexMagic`'s. So it wants a `CardTemplate` carrying `mana-cost` AND `defense`/`is-token`, and neither chapter has that shape today. Repointing it at `Magic` yields `Unknown name: LegendaryMythic` and `Record 'CardTemplate' has no field 'defense'`/`'is-token'`; repointing only `Mana` yields duplicate `Blue`/`Red`/`Green`/`Colorless`/`ColorSet` constructors, because `CodexMagic chapter Card` pulls `CodexMagic`'s `Mana` in transitively. **The reading is that CardEmitter was written against a CodexMagic `Card` that still used mana-cost, before that game was reworked to the prismatic `light-cost` vocabulary, and was left behind.** Closing it is a decision about which game this emitter targets: port it to `light-cost`, or move it to `Magic` and give up `defense`/`is-token`/`LegendaryMythic`. Not a compile fix. |
| EXP-1 | **GameWorldDesigner relationship and event tables** | The relationship table and the event table are not built. |
| EXP-2 | **`/api/generate` and `/api/config` have an in-repo implementation** | All three designer pages call them. `ExplorerServer.codex` does not route them, and `apps/explorer/server.ps1` defines `Invoke-SdGenerate` / `Feed-SdConfig` and then never dispatches to them. So the prompt-building half works and the generation half answers nothing from a fresh sync. |
| EXP-3 | **`apps/explorer/server.ps1` works from a fresh sync** | It serves pages from `D:\Projects\CodexMagic\explorer\pages` -- a path outside the depot, on one machine. Fold the SD generation code into `run-designers-demo.ps1` and retire the out-of-repo path. |
| EXP-4 | **`build/build-explorer-pages.ps1` is live or gone** | Stale: it runs `build-output\{carddesigner,characterdesigner,settingdesigner,voicestudio}.cdx`, which nothing in the tree produces, and writes outside the depot. The live driver is `build/build-apps.ps1`. Delete it or rewrite it over `codex\plugs\html\run.ps1`. |
| EXP-5 | **A route reaches `CardDesignerApp`** | It compiles, and no server routes it: `run-designers-demo.ps1`'s page map has no `card` key. `VoiceStudio`, `WorkflowExporter`, `StoryGraph`, `WorldForge` and `NameForge` are in the same state -- chapters that compile and are reachable from no page. |
