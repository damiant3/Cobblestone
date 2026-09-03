# Mathbook -- open capabilities

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
| MB-1 | **`Notebook.codex` compiles** | It does not, and it is the chapter `codex.project.json` names as this app's ENTRY. Measured 2026-09-02 at seed F134E3E7: four sites, `Notebook.codex:318,326,336,341`, all `CDX3008: Undefined type name: Cell`. The record this app actually declares is `NotebookCell` (`Notebook.codex:112`), so this is an incomplete rename rather than a missing chapter, and the repair is four references. NOT fixed while finding it, because the wasm entry does not go through `Notebook` and R-ONE says a pre-existing defect goes to the register rather than into somebody else's change. **The reason nobody knew is the interesting half and it generalises past this app**: no script under `build/` mentions mathbook at all except `quire-map.ps1`, so no gate, no sweep and no battery has ever compiled a line of it. That is L-NOGATE with nothing even nominally watching, and the same question is worth asking of every app in `apps/` whose name appears in no build script. |
| MB-2 | **The browser notebook is more than one line at a time** | `MathbookWasm.codex` (stage 1, 2026-09-02) is a WASI program: one expression in on `fd_read`, one answer out on `fd_write`, `_start` runs once and exits, and the page instantiates per evaluation. That is the compiler page's own shape (`codex/plugs/wasm/page-workspace-arm.js`) and it is right for "type an expression, get an answer": each evaluation gets a fresh heap, which matters in a module with no collector. What it does NOT carry is the notebook: no symbol table across evaluations, so `x := 3` cannot be read back by the next line, and `Notebook.codex`'s `symtab-set`/`symtab-get` are unreachable from the browser. Carrying state across a WASI `_start` means either keeping the bindings at a fixed address the way the C64 keeps its machine (`apps/c64/C64Wasm.codex`), or handing the page the whole session as text and replaying it. Decide which before adding a second export, because the two do not mix. |
