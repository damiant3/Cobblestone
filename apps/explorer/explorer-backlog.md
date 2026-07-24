# Explorer -- open capabilities

App-domain backlog. `docs/PM/BACKLOG.md` is the register for the
platform -- compiler, forewords, OS, plugs, backends. Anything that is
this application's own behaviour lives here instead, so the platform
register stays readable.

The rules are the same ones: an entry says what is still missing and
nothing else, a closed entry is DELETED rather than annotated, and a
gap that is still real is never quietly dropped.

Design: `apps/explorer/design/Active/`. The working server is
`apps/explorer/run-designers-demo.ps1`; the page builder is
`build/build-apps.ps1`.

| # | Capability | State of the gap |
|---|---|---|
| EXP-1 | **GameWorldDesigner relationship and event tables** | The relationship table and the event table are not built. |
| EXP-2 | **`/api/generate` and `/api/config` have an in-repo implementation** | All three designer pages call them. `ExplorerServer.codex` does not route them, and `apps/explorer/server.ps1` defines `Invoke-SdGenerate` / `Feed-SdConfig` and then never dispatches to them. So the prompt-building half works and the generation half answers nothing from a fresh sync. |
| EXP-3 | **`apps/explorer/server.ps1` works from a fresh sync** | It serves pages from `D:\Projects\CodexMagic\explorer\pages` -- a path outside the depot, on one machine. Fold the SD generation code into `run-designers-demo.ps1` and retire the out-of-repo path. |
| EXP-4 | **`build/build-explorer-pages.ps1` is live or gone** | Stale: it runs `build-output\{carddesigner,characterdesigner,settingdesigner,voicestudio}.cdx`, which nothing in the tree produces, and writes outside the depot. The live driver is `build/build-apps.ps1`. Delete it or rewrite it over `codex\plugs\html\run.ps1`. |
| EXP-5 | **A route reaches `CardDesignerApp`** | It compiles, and no server routes it: `run-designers-demo.ps1`'s page map has no `card` key. `VoiceStudio`, `WorkflowExporter`, `StoryGraph`, `WorldForge` and `NameForge` are in the same state -- chapters that compile and are reachable from no page. |
