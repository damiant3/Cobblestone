# Sheets -- open capabilities

App-domain backlog. The shape and priority order for the platform live in
`docs/PM/CurrentPlan.md`; the platform-wide register was deleted 2026-07-23
and is not coming back. Anything that is this application's own behaviour
lives here.

The rules are the same ones: an entry says what is still missing and
nothing else, a closed entry is DELETED rather than annotated, and a
gap that is still real is never quietly dropped.

The design is `docs/Designs/Active/Apps/Sheets.md`. **Nothing is built.** The
app is listed on the landing page as of 2026-08-26 under that section's
"partially built" wording, and there is no `apps/sheets/*.codex` yet.

| # | Capability | State of the gap |
|---|---|---|
| SHEET-1 | **There is no cell store** | Nothing in the tree represents a sheet. The decision that blocks everything else is dense versus sparse, and it is not close: a sheet is mostly empty, so dense storage makes a 1000-row grid cost megabytes for nothing. The compiler's `HamtNode` is already in the tree, proven, and the obvious candidate to reuse; the argument against it is that a spreadsheet's access pattern is range-shaped rather than point-shaped, and a range read that walks the trie per cell is how this becomes slow. Measure both against a 1,000-cell column sum before choosing, and record the number here. |
| SHEET-2 | **There is no formula parser** | Literals, `A1` references, `A1:B9` ranges, arithmetic, comparison, and a call form. Parsing is not new ground in this tree, so the work here is the grammar decision rather than the machinery. One thing to settle up front: a formula is typed, so `=A1+B1` with text in `B1` is a refusal naming the cell, not a silent zero and not an inherited `#VALUE!` sigil. |
| SHEET-3 | **There is no dependency graph, and this is the row that matters** | Recalculation order must be DERIVED by topological sort over the cell references extracted from each formula. Row-major order and typing order are both wrong and both look right on small sheets, which is why this is the defect that ships. A cycle (`A1 = B1 + 1`, `B1 = A1 + 1`) is an error naming both cells, never a spin. An edit recomputes only the transitive dependents of the edited cell; recomputing the whole sheet is correct and is the thing that makes it unusable at size. |
| SHEET-4 | **There is no function set** | Staged deliberately, because a spreadsheet with 400 functions and a wrong recalculation order is worse than one with twelve and a right one. In order: arithmetic and comparison; then `SUM`, `COUNT`, `MIN`, `MAX`, `AVERAGE` over ranges; then `IF`, `SUMIF`, `COUNTIF`; then `CONCAT`, `LEN`, `UPPER`, `LOWER`, `TRIM`. Lookup is deferred to SHEET-6. |
| SHEET-5 | **There is no csv in or out** | The one exchange format worth having early, because it is what makes the app usable with data that already exists. Charts, `.xlsx`, multiple sheets, macros and formatting beyond alignment are all out of scope for the first version and are not gaps in it. |
| SHEET-6 | **Lookup is undecided, not merely unbuilt** | `VLOOKUP` is positional, silent on a near-match by default, and brittle under column insertion. A named alternative is the better design and the worse decision for anyone who arrives with an existing sheet in their head. Damian's call, and it should not be made by whoever happens to reach this row. |
| SHEET-7 | **No arm exercises the hard part** | A grid that renders is not evidence about anything SHEET-3 touches. The first arm worth writing is a chain of 1,000 dependent cells recalculating correctly after an edit to the first cell, in derived order, with `heap hwm` recorded before and after. It exercises the graph, the sort, the incremental path and R-COST at once and fails loudly for each. Do not write the render test first and read its green as progress. |
