# Sheets: a spreadsheet where the dependency graph is the point

*A grid, a formula language, and a recalculation order that is derived rather
than guessed.*

**Status: DESIGN. Nothing built.** Opened 2026-08-26 (reek) at Damian's
direction, alongside the landing page's ecosystem refresh. The app is listed
on the landing page as of that date and does not exist in the tree; the
section's "partially built" wording is what covers the gap. **Read everything
below as intent, not as an inventory.**

---

## 1. Why this app and not another one

The app catalogue has 24 entries and no spreadsheet. That is the largest
obvious hole in it: Notes, Books, Publisher and Markets are all there, and the
one program most people would name first when asked what a computer is for is
missing.

It is also the app that exercises the language where the language is
interesting. A mail client is I/O and layout. A spreadsheet is a **dependency
graph with a fixed point**, evaluated to quiescence, and that is the same
shape as the compiler's own resolve phase. The parts that are hard in a
spreadsheet are hard for reasons Codex has opinions about:

- **A cycle is a type of error, not a hang.** `A1 = B1 + 1`, `B1 = A1 + 1` must
  be refused with both cells named, not spun on.
- **Recalculation order must be derived from the graph**, never from row-major
  order or from the order cells were typed. Deriving it is a topological sort;
  guessing it is the classic spreadsheet bug where a value is one keystroke
  stale.
- **There is no garbage collector.** A recalculation that reallocates the whole
  sheet on every keystroke is the R-COST failure this app will find first.

## 2. The shape

Three pieces, in the order they have to be built.

**The grid.** Cells addressed `A1` style, sparse. A sheet is mostly empty and
storing it densely is the mistake that makes a 1000-row sheet cost megabytes.
The representation question is open: an association list is wrong at size, and
the HAMT already in the tree (`HamtNode`, used by the compiler) is the obvious
candidate to reuse rather than to reinvent.

**The formula language.** A small expression grammar: literals, cell
references, ranges, arithmetic, comparison, and a function set. The parser is
not new work in this tree; the interesting decision is that a formula is
**typed**, so `=A1+B1` where `B1` holds text is a refusal with a cell name and
not a silent zero or a `#VALUE!` sigil inherited from 1985.

**The evaluator.** Parse every formula to an expression, extract the cell
references as the edge set, topologically sort, evaluate in that order. On a
cycle, report the cycle. On an edit, recompute only the transitive dependents
of the edited cell, which is the difference between a spreadsheet that stays
responsive and one that does not.

## 3. The function set, in the order it earns its place

Deliberately small and deliberately staged. A spreadsheet with 400 functions
and a wrong recalculation order is worse than one with twelve and a right one.

1. **Arithmetic and comparison.** `+ - * /`, parentheses, `= <> < > <= >=`.
2. **Aggregates over ranges.** `SUM`, `COUNT`, `MIN`, `MAX`, `AVERAGE`. These
   are what make a range reference worth having.
3. **Conditionals.** `IF`, and then `SUMIF` / `COUNTIF`, which are the two
   every real sheet reaches for immediately after `SUM`.
4. **Text.** `CONCAT`, `LEN`, `UPPER`, `LOWER`, `TRIM`.
5. **Lookup.** `VLOOKUP` or its saner replacement. Open question, see below.

## 4. What is deliberately out of scope for the first version

Charts, multiple sheets, cell formatting beyond alignment, frozen panes,
import and export of `.xlsx`, and macros. Each is a real feature and none of
them is the thing that makes the app worth building. `.csv` in and out is the
one exchange format worth having early, because it is the one that makes the
app usable with data that already exists.

## 5. Open questions, none of them settled

- **The cell store.** Reuse the compiler's HAMT, or write a grid-specific
  structure? The HAMT is proven and already in the tree, which argues for it;
  a spreadsheet's access pattern is column-and-range shaped rather than
  point-shaped, which argues against. Measure before choosing.
- **`VLOOKUP` is a bad function** and everyone knows it: positional, silent on
  a near-match by default, and brittle under column insertion. A named
  alternative is the better design and the worse decision for anyone arriving
  with an existing sheet in their head. Undecided.
- **Recalculation granularity.** Per-keystroke or on commit? Per-keystroke is
  what people expect and is the one that will find the allocation problem.
- **Where it runs.** The other apps in this catalogue compile to HTML through
  the Codex compiler. A spreadsheet is the first one where the desk version
  and the web version may want genuinely different evaluation strategies.

## 6. The first measurable milestone

Not "a grid renders". A grid that renders is not evidence of anything the hard
part touches. The first milestone that means something:

> A sheet with a chain of 1,000 dependent cells recalculates correctly after
> an edit to the first cell, in derived order, with the heap high-water mark
> recorded before and after.

That single arm exercises the graph, the sort, the incremental path and
R-COST at once, and it fails loudly for every one of them.

## Register

Items live in `apps/sheets/sheets-backlog.md` once the directory exists. This
doc moves to `docs/Designs/Done/Apps/` when the app ships.
