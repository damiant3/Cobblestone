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
| EXP-JSON-THEME | JSON request text | `ExplorerTheme.codex:433` inserts prompt and negative prompt raw in `gen-body`. Quote/backslash/control characters can change the JSON shape. Source census 2026-10-01, main 33668. |
| EXP-JSON-VOICE | JSON catalog text | `VoiceStudio.codex:233`, `:243`, `:253` quote profile, emotion and use-case fields without escaping. Current catalogs are fixed records; the helper contracts accept arbitrary Text. Preserve values rather than stripping quotes. |
| EXP-JSON-WORKFLOW | JSON workflow keys and values | `WorkflowExporter.codex:23`, `:26`, `:29`, `:32`, `:36` quote node IDs/classes, input keys, string values and references raw. Prompt/checkpoint values reach `input-str`; reference callers currently use fixed keys/IDs. |
| EXP-5 | **`CardDesignerApp` is a card designer** | Routed at `/card`, and the page is a stub: `opening` prints `card-designer-app loaded` and nothing else. Its dimensions (model, sampler, steps, CFG, LoRA) are SD knobs, so the page needs `/api/config` to list the WebUI's models, samplers and LoRAs, which answers only `current_model` today. |
| EXP-6 | **`VoiceStudio` and `WorkflowExporterMain` are pages** | Both `opening`s print a hand-written HTML and JS document as text, so the HTML plug renders the source, escaped, and no route serves them. Nothing answers VoiceStudio's `/api/tts/status` or `/api/tts/generate`. |
