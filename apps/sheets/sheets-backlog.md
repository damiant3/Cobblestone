# Sheets -- open capabilities

App-domain backlog. The shape and priority order for the platform live in
`docs/PM/CurrentPlan.md`; the platform-wide register was deleted 2026-07-23
and is not coming back. Anything that is this application's own behaviour
lives here.

The rules are the same ones: an entry says what is still missing and
nothing else, a closed entry is DELETED rather than annotated, and a
gap that is still real is never quietly dropped.

The design is `docs/Designs/Done/Apps/Sheets.md`, moved there once the app was
built; what it still carried that lives nowhere else is SHEET-14 below. Built
as of 2026-09-08:
the cell store (`apps/sheets/CellStore.codex`), csv in and out
(`apps/sheets/CellCsv.codex`), the formula parser
(`apps/sheets/CellFormula.codex`), the dependency graph
(`apps/sheets/CellGraph.codex`), the evaluator with its function set
(`apps/sheets/CellEval.codex`) and the desk pane
(`apps/works/GopSheet.codex`), with arms `codex/test/apps/sheet-cell-store`,
`codex/test/apps/sheet-csv`, `codex/test/apps/sheet-formula`,
`codex/test/apps/sheet-graph`, `codex/test/apps/sheet-eval`,
`codex/test/apps/sheet-chain` and the sheet itself
(`apps/sheets/CellView.codex`, arm `codex/test/apps/sheet-view`).

**The parser's contract, because SHEET-3 is its consumer.**
`formula-parse` takes the whole formula text including the leading `=` and
answers a `FxParse` whose `px-ok` is the discriminator; a refusal carries a
message and the byte offset it stopped at, and its `px-expr` means nothing.
`formula-refs` walks a parsed `Expr` and answers the edge set as a
`List CellRef`, each carrying its row, its column and the spelling the person
wrote. A range contributes its two CORNERS, not the cells between them:
enumerating those needs the sheet's bounds, which the parser does not have.
Comparison is non-associative, and a formula must be consumed whole.

The rows below are what is left. The app is
listed on the landing page as of 2026-08-26 under that section's "partially
built" wording.

| # | Capability | State of the gap |
|---|---|---|
| SHEET-1 | **The sheet, built. Its contract** | `apps/sheets/CellView.codex`, arm `codex/test/apps/sheet-view`. A `Sheet` owns a store, the `Program` of formulas over it, and a `View` (top, left, rows, cols, column width). `sheet-type` classifies what a person typed by exactly the rule CSV import uses, reusing `csv-numeric` rather than writing a second one: text opening with `=` is a formula, digits with an optional minus whose value fits the store's i32 are a number (a wider one is text, and `sheet-set` refuses it), nothing typed CLEARS the cell, everything else is text. `sheet-render` answers the window as text, numbers right-aligned and text left. **A number too wide for its column is NOT truncated: the cell fills with `#`.** A cut number reads as a different number and nothing in the display says so, which is the typed refusal one layer down; text IS cut, with a `>` in the last position. `sheet-bar` answers what an editor needs above the grid: the cell name, what was TYPED (the formula source for a formula cell, never its answer), and what it shows. |
| SHEET-9 | **The desk pane: the contract its citers rely on** | `apps/works/GopSheet.codex`, focus id 18, scancode 31, arm `codex/test/apps/desk-sheet-pane`. Its launcher row is under ACCESSORIES, beside the Calculator, and the group is a measurement rather than a taste: as a seventh PRODUCTIVITY entry it made the expanded group 11 rows and `desk-menu-groups` measured the laid menu's bottom PAST the taskbar band at 1600 wide (menu 58..410, band 418..454 of 450, against 74..398 and 406..442 before). That arm exists because rows once landed off the glass where no count, no hit test and no id lookup could see them. **The launcher row is the pane's ONLY road, and the scancode is not one.** The desktop stopped launching apps from a keystroke on 2026-08-26 (`GopDesk`, "THE DESKTOP NO LONGER LAUNCHES AN APP FROM A KEYSTROKE"): `desk-loop`'s last arm swallows every scancode except F12 while no pane is focused, and the dispatch table survives only because a start-menu row hands its answer back with `clicked` false. A launcher row is the ONLY road, so it is a prerequisite rather than a convenience. **The pane is EDITABLE and PHOTOGRAPHED, so the READ ONLY this row was titled for is gone** (SHEET-10, and again 2026-09-08 at 1600x900 showing the evaluator's answers, Widget 3 250 750 and Total 1550, with the pill in the taskbar). Its click coordinates come from `codex/test/apps/desk-sheet-point`, which is reconciled and prints DEVICE centres when it is given an ESP carrying `CMUNSS.TTF`. |
| SHEET-10 | **The Sheets pane is editable; one background shade is unverified at head** | Built at head in `GopDesk.codex`: `desk-sheet-step` and `desk-sheet-body` paint the pane from `da-sheet`, `desk-sheet-focus` and `desk-sheet-reenter` join the pill restore, `desk-sheet-hit` and `desk-sheet-take` pick a cell, and `desk-sheet-key` types and commits through `sheet-type`; `codex/test/apps/desk-sheet-pane` and `desk-sheet-state` are the arms. LATENT: when step 3 was held, twelve rows at the bottom of the client area (y 812 to 823 at 1600x900) painted the theme background `0,14,28` where the depot control painted the window's `pal-bg`; whether that still holds needs a desk photograph against a depot-built control. |
| SHEET-7 | **The chain arm, built** | `codex/test/apps/sheet-chain`: 1,000 dependent cells, A1000 reads 1004, editing A1 to 100 makes A1000 read 1099, and editing A1 recomputes 999 cells from A2 to A1000. The heap reading carries its own control: two `__heap-save` reads with nothing between them differ by 0, printed, so an instrument that stopped working fails visibly rather than making every number beside it meaningless. Measured 2026-09-08 on seed F6A07B16, AFTER SHEET-8: building and parsing the chain 12,956,664 bytes, one recalculation 1,340,368, and the same arm at 4,000 cells for the linearity. SHEET-8's entry carries both sizes. The numbers are printed rather than compared against a recorded bound, because a bound recorded today is a number nobody re-measures (L-COUNT); the value is the shape. |
| SHEET-8 | **No quadratic line in Sheets. Measured at two sizes** | Closed. Every accumulator in `CellGraph` and `CellEval` is built with `list-push`, which extends in place, rather than `acc & [x]`, which allocates a fresh list of length 1, 2, 3 and so on. **A `Program` is a WORKBOOK, not a value: `graph-put` extends it in place and answers the same workbook, and an earlier binding names the same workbook.** That contract is stated in the chapter rather than avoided, because avoiding it was the quadratic; `pg-slot` was already a shared block, so a `Program` was never a value a caller could hold two versions of, and the cell list was the half pretending otherwise. Measured 2026-09-08 on seed F6A07B16 by `codex/test/apps/sheet-chain` at 1,000 and 4,000 cells: build 12,956 and 14,999 bytes a cell (the rise is the formula text getting longer, `=A3999+1` against `=A9+1`), recalculation 1,340,368 and 5,363,280 bytes, which is 4.00x for four times the cells. Recalculation at 1,000 cells fell from 17,467,024 bytes to 1,340,368 over the two passes, 13x. Two sizes, because one number cannot say whether a cost is linear. |
| SHEET-14 | **Where a sheet runs, and what the first version deliberately left out** | Lifted here when the design moved to `docs/Designs/Done/Apps/Sheets.md`, because an open item belongs in the register that owns the app. **OPEN: the desk version and a web version may want genuinely different evaluation strategies.** Every other app in this catalogue compiles to HTML through the Codex compiler; Sheets has no web build and no `build-wasm.ps1` of its own, and recalculation is the first place the two targets would plausibly diverge rather than share. Nothing is decided and nothing is built. **Deliberately out of the first version, recorded so a later reader knows these were chosen against rather than forgotten:** charts, multiple sheets, cell formatting beyond alignment, frozen panes, `.xlsx` import and export, and macros. `.csv` in and out was the one exchange format judged worth having early, and it is built (`apps/sheets/CellCsv.codex`, arm `codex/test/apps/sheet-csv`). |
