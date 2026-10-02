# Code Layout

Stage 0: the design. Owner blu. Damian, 2026-09-30: "the expression
formatting terseness sometimes creating walls of text".

## The governing test

**The level of indent shows the level of the decision tree you are at**
(Damian, 2026-09-30: "like a switchboard or well drawn circuit"). Every rule
below is an instance of it, and a case no rule names is decided by it: a
reader who sees two lines at the same column must be able to say they are
alternatives at the same decision, and a line one step deeper must be a
consequence of the line above it.

## Scope

- The width is **128 columns**, counted from column 1 (Damian, 2026-10-01).
  Keep code already formatted at 100 as it is. Hand rewrites address eligible
  lines over 200 first; signature continuation belongs to COMPILER-114.
- In: every `.codex` chapter written by hand.
- Out: long string constants (a string is not broken to meet the width);
  literal tables, a list whose every element is a literal (a 41,588-column
  byte table in `codex/test/brotli-interop` would become 11,900 lines);
  generated chapters (the generator is fixed instead, and its output then
  obeys these rules); column-2 prose, which the formatter never touches.

## What the parser allows, which bounds every rule

Each of these is read from the compiler, not assumed.

| Fact | Where |
|---|---|
| Outside `(` and `[`, a newline ends an application. A deeper-indented next line starting with a literal, `(` or `[` is error CDX1070. | `ParserExpressions.codex:138` (newlines skipped only when `paren-depth > 0`), `:160-175` |
| Inside `(` and `[`, newlines are skipped, so an application or a list may span lines there. | `:138` |
| Layout around `then` and `else` is ignored: the parser skips newlines before and after each keyword. | `:389-403` |
| A record literal may put each field on its own line: newlines are skipped after `{` and around each comma. A field's VALUE is an ordinary expression, so an application in it stays on one line. | `:332-360` |
| A record literal's `{` stays on the line of its type name: `parse-atom-type-ident` checks for `{` without skipping newlines. | `:294` |
| A bound expression must START on its binding line (`let x =` then a newline is CDX1023). | `docs/DevelopersGuide.md`, "Pitfalls" |
| A line may not start with `.` (CDX1071) or with `& ...` (a new expression, not a continuation). | same section; "No multi-line `&` chains" |
| Inside an `act` block a newline separates statements. | `docs/DevelopersGuide.md`, the act syntax |

`docs/DevelopersGuide.md` also says, in its syntax section, that "multi-line
function applications work everywhere". That sentence is wrong for
applications outside parentheses (the first row above), and it is corrected
in the same change as this design.

## The rules

The indent step is **2 spaces**. A definition's name sits at column 3 (two
spaces), its body one step under the name.

**R1. A chain stays flat.** `else if` lines of one decision sit at the column
of the first `if`, however many there are (`docs/PM/Done/Plans/BOOTSTRAP-FIXEDPOINT-PLAN.md:46-55`).
Ramping each `else if` one step deeper is a violation: it draws a tree that
is not there.

**R2. A branch that spans lines puts its result one step under its
condition.** The line ends with `then` (or with `else`), and the result
starts on the next line one step deeper. A branch that fits stays on its line even when its neighbours in the chain break: R2 breaks a branch for its own width, not for symmetry.

**R3. A nested `if` that is not a link of the chain starts one step deeper**
than the branch that holds it. It is a new decision inside a result, and the
indent says so.

**R4. A `let` / `in` chain is one binding per line**, each `in let` line starting
at the column where the line holding the first `let` starts (so a chain opened by
`else let` puts its `in let` lines under that `else`). When the chain ends in an `if`, that `if` follows
the last `in`, and its `else` lines sit at the `let`'s column, so the chain
and the decision it feeds read as one column.

**R5. A record or list literal that does not fit puts one field or element
per line**, one step under the line holding the opening brace or bracket,
with the closing brace or bracket on its own line at that line's column.

**R6. An application is not broken.** Breaking one inside its parentheses is
legal, but every break point it offers cuts an argument list in two, which
draws a structure that is not a decision and fails the governing test. A line
that R1 to R5 leave over the width goes to R7.

**R7. What the rules leave over the width is reported, not rewritten.** An
application (R6), an `&` chain, or a single token longer than the room left
is a line the formatter will not break. It is
listed with its file and line for a hand rewrite, which normally binds a
sub-expression with `let`.

## Exhibits

Each "before" is quoted from the tree (2026-09-30). Each "after" is what the
rules produce. The formatter produces the whitespace-only ones; the hand
rewrite R7 asks for is shown separately where one is needed.

### `TypeChecker.codex:514-518`, `subst-arms` (R4, R5, R7)

Before (the last line is 190 columns):

```
  subst-arms (sub) (arms) (i) (len) (acc) =
   if i == len then acc
   else let arm = list-at arms i
   in let inner = remove-shadowed sub (pat-vars (arm.pattern))
   in subst-arms sub arms (i + 1) len (list-push acc (AMatchArm { pattern = arm.pattern, body = subst-expr inner (arm.body), guard = arm.guard, span = arm.span, alt-group = arm.alt-group }))
```

After, whitespace only (R5 breaks the record inside the call's parentheses):

```
  subst-arms (sub) (arms) (i) (len) (acc) =
    if i == len then acc
    else let arm = list-at arms i
    in let inner = remove-shadowed sub (pat-vars (arm.pattern))
    in subst-arms sub arms (i + 1) len (list-push acc (AMatchArm {
      pattern = arm.pattern,
      body = subst-expr inner (arm.body),
      guard = arm.guard,
      span = arm.span,
      alt-group = arm.alt-group
    }))
```

### `ParserExpressions.codex:377-381`, `finish-list-element` (R1, R2, R4, R6, R7)

Before: the `else if` and `else` ramp three columns deeper than the `if` they
belong to, and the last line is 261 columns.

```
  finish-list-element (acc) (sp) (e) (st) =
   let st2 = skip-newlines st
   in if is-comma (current-kind st2) then parse-list-elements (__linked-list-push acc e) sp (skip-newlines (advance st2))
      else if is-right-bracket (current-kind st2) then parse-list-elements (__linked-list-push acc e) sp st2
      else ExprOk (deck-record (ListExpr (deck-record (__linked-list-to-list (__linked-list-push acc e))) sp)) (__record-set st2 "bag" (bag-add (st2.bag) (make-error cdx-list-no-close "Unclosed list literal: expected ',' or ']'" (token-to-span (current st2)))))
```

After, whitespace only:

```
  finish-list-element (acc) (sp) (e) (st) =
    let st2 = skip-newlines st
    in if is-comma (current-kind st2) then
      parse-list-elements (__linked-list-push acc e) sp (skip-newlines (advance st2))
    else if is-right-bracket (current-kind st2) then
      parse-list-elements (__linked-list-push acc e) sp st2
    else
      ExprOk (deck-record (ListExpr (deck-record (__linked-list-to-list (__linked-list-push acc e))) sp)) (__record-set st2 "bag" (bag-add (st2.bag) (make-error cdx-list-no-close "Unclosed list literal: expected ',' or ']'" (token-to-span (current st2)))))
```

The `ExprOk` line is an application over the width, which R6 leaves alone. It
is on the R7 report, and the hand rewrite binds its arguments:

```
    else
      let items = __linked-list-to-list (__linked-list-push acc e)
      in let lst = deck-record (ListExpr (deck-record items) sp)
      in let msg = "Unclosed list literal: expected ',' or ']'"
      in let err = make-error cdx-list-no-close msg (token-to-span (current st2))
      in ExprOk lst (__record-set st2 "bag" (bag-add (st2.bag) err))
```

### `UNet.codex:418`, `unet-ops-with` (R5)

Before: one line of 668 columns, a record of 27 fields.

```
  unet-ops-with (patch) (selfv) (probsat) (pr) = UNetOps { conv = un-conv, linear = un-linear, ... }
```

After, whitespace only:

```
  unet-ops-with (patch) (selfv) (probsat) (pr) = UNetOps {
    conv = un-conv,
    linear = un-linear,
    linear-b = un-linear-b,
    group-norm = un-group-norm,
    ...
    probs-at = probsat,
    probs = pr
  }
```

## The formatter

A Codex program that rewrites whitespace and newlines and nothing else.

**Why a whitespace reflow and not the emitter.** `codex/compiler/Emit/CodexEmitter.codex`
prints Codex from the IR, and `docs/Designs/Done/Compiler/SourceRefresh.md` tried
making it the canonical formatter. The IR has already lost what a source file
carries beyond its meaning: column-2 prose, the names a desugaring replaces,
and the author's choice between equivalent spellings. Printing from it
rewrites tokens, which makes every formatted chapter a review of content, not
of layout. The emitter stays useful as the reference for R1: it already emits
flat chains (`codex-emit-if-chain`, `CodexEmitter.codex:444`).

**How it decides.** It lexes the chapter, keeps every token and its text, and
takes the structure (which `if` a `then` belongs to, where a record literal
opens) from the parse tree, whose tokens carry line and column. It then
re-emits the same token sequence with new line breaks and indentation, and
lists every line R7 names.

## Current width report

`LAYOUTREPORT` uses 128 columns. It omits column-2 prose. Its `string` and
`table` rows identify exemptions and are excluded from work counts. A short
literal-list argument does not exempt the surrounding call: the literal
span itself must exceed the available width. Computed list entries are not
literals. Quoted characters do not open strings; escaped characters and
numeric separators are recognized using Codex's syntax.

Measured 2026-10-01, main 34159 plus COMPILER-109 DEV34136 and DEV34150,
69 chapters from `build/compiler-order.txt`. The auditor was compiled with
depot seed 66AE634E870CA342:

| Quire | Current source over 128 | Current source over 200 | R7 output over 128 | R7 output over 200 |
|---|---:|---:|---:|---:|
| Ast | 127 | 17 | 127 | 17 |
| Core | 66 | 8 | 66 | 8 |
| Emit | 259 | 40 | 259 | 40 |
| IR | 263 | 36 | 263 | 36 |
| Semantics | 18 | 0 | 18 | 0 |
| Syntax | 60 | 6 | 60 | 6 |
| Top | 70 | 17 | 70 | 17 |
| Types | 276 | 53 | 270 | 52 |
| Total | 1,139 | 177 | 1,133 | 176 |

All columns exclude string/table exemptions and prose. The current-source
counts include 126 signatures over 128, 11 of them over 200; the remaining
166 lines over 200 are non-signature code, including three cite declarations.
Syntax has no eligible expression over 200; its six remaining long lines
are signatures. R7 counts follow an in-memory formatter pass, not a
repository rewrite, and do not classify signatures.

Current census evidence is in `D:/Projects/Cobblestone-reek/build-output/compiler109-batch2-rebased/`:
`census/inventory.json` records source, formatter and auditor hashes;
`census/rows.json` carries original file/line/text for current-source rows;
`census/summary.json` and per-chapter output retain both views. `audit.codex`
wraps the formatter with raw and formatted report sections. `census.ps1`
records the invocation and exclusions; it is a lane-local runner, not a
general command. It requires the compiled auditor, an unused output
directory and an owned status path. No generated chapters were found by
its first-24-line header check. Column-2 prose is replaced with blank lines
in guest input, preserving source line numbers and admitting
`TypeCheckerInference` despite its non-CCE prose. Remaining source is checked
as ASCII; reported raw widths and all source hashes are checked unchanged.

Formatter boundary controls in `build-output/layout128/` accept 128 and report
129, 200 and 201. Short-list calls,
computed entries and quote characters remain code; long primitive tables
and strings retain exemption labels. A computed-list rewrite is token- and
whole-CDX-identical, and a changed-token control is detected. A narrow
fixture remains unchanged. These checks do not prove arbitrary formatter
output; each future rewrite still needs the proof below.

## The proof

1. **Token identity.** For each formatted chapter, the lexer's token sequence
   with Newline, Indent and Dedent removed is identical before and after,
   kind and text. This is the whole claim of a whitespace-only formatter, and
   a checker that compares the two sequences is the instrument; it must be run
   against a deliberately altered token (a control) before it is believed.
2. **Compile identity.** The formatted chapter compiles to the same bytes as
   before. A CDX carries no source positions: measured 2026-09-30 (seed
   B3256BF8B4CC8327), `codex/test/unit-smoke` with 3 lines inserted and
   `codex/test/hamt-test` with 10 lines inserted and 43 lines re-indented each
   compile byte-identical to the original, while one literal changed (42 to 43)
   moves 33 bytes. So the comparison is whole-file equality, with nothing masked;
   the first formatted quire widens the evidence past these two programs.
3. **On the compiler, a fixed point.** A formatted compiler quire, compiled by
   the depot seed, reaches stage 2 == stage 3, and its output is the depot
   compiler's output apart from positions.

## Rollout

The rollout measurements below were taken at the former 100-column limit.
They are not the current compiler census.

One quire per landing, the compiler first. A compiler quire's landing takes the
token (it is compiler source), but it needs no seed. The proof is the
concatenated compiler compiled by the depot seed with `-Repl`: the
formatted source must compile to the same bytes as the original, and both
must equal the depot seed outside the signature window (offsets 40..135).
Controls: a literal changed on a formatted line in a reached definition of
each chapter moves the CDX. The symbol map does not say which definitions
are reached, because inlined definitions are missing from it too. The R7
report of each quire is cleared by hand rewrites on the seed path, or
counted in that quire's backlog.

## Stages

- **0**, this design, with a naive reader (R-NAIVE) before any code.
- **1**, the token-identity checker with its control. **Built:**
  `codex/build/TokenIdentity.codex`. Its input is the two chapters joined by a
  line `LAYOUTSPLITLAYOUTSPLIT`, carriage returns removed, encoded as CCE bytes
  (`ConvertTo-CceBytes` in `build/vm-config.ps1`; CCE has no U+000D) and ended
  by a zero byte, given to `tools/codex-vm.exe -kernel <cdx> -mem 1024 -input
  <file> -headless -output <out>`. `read-serial-cce` copies the ring's bytes
  unconverted, therefore the host must send CCE. The lexer runs on the
  compiler's own deck, set up as `compile-lex` does (`init-phase-allocator`,
  `build demand-lex-floor`, `phase-start`); without it `tokenize` faults. It
  prints `tokens N and M, identical` or the first difference by index, line,
  column and text. Measured 2026-09-30 on `codex/test/hamt-test`: the chapter
  against itself, against a copy with 43 lines re-indented, and against a copy
  with 10 lines inserted, each `identical` at 539 tokens; against a copy with
  one literal changed, `DIFFER at token 56: 16:37 '42' against 16:37 '43'`.
- **2**, the formatter over one small quire outside the compiler, proven by 1.
  `codex/build/CodeLayoutFormat.codex` reads one chapter by the stage 1
  recipe (no split line) and prints it formatted. It rewrites raw lines, not
  a parse tree. **R1 to R5 are built.** The first pass works line by line
  and keeps 2 stacks of columns: one for each open depth-0 `if`, and one for
  each open `let` (a `for`'s `in` is skipped). An `else` line takes the column
  of its `if`, and an `in` line takes the column of its `let`. The line after
  a line ending in `then` or `else` sits 2 deeper than that `if`. Every other
  line moves by the same amount as the nearest earlier line at its indent or
  shallower. An over-width line splits after its depth-0 `then` and before
  the matching `else` (R2), and a lone over-width `else X` splits after the
  `else`. R5 then runs on every line. On `codex/tracker` (2026-09-30), lines
  over 100 went 27 to 9; the 9 left are R7 lines. Compile identity needs
  an entry chapter that CALLS every changed definition: the compiler drops
  unreferenced definitions, so a 1-literal change in an uncalled definition
  leaves the CDX byte-identical (measured on `te-find-instance`). `-Text`
  writes nothing for a chapter without `opening`. Proof (seed
  B3256BF8B4CC8327): 1 entry chapter citing all 4 tracker chapters compiles
  to the same bytes before and after. 6 one-literal controls, each inside a
  changed definition, all move the CDX. The R2 split inside a binding
  (`let next = if ... then`) is 1 of the 6. More evidence, not landed:
  `codex/test` gpu-layer-kernels, brotli-interop and interval-exhaustive,
  formatted, are token-identical (8129, 25780 and 1750 tokens) and compile to
  the same bytes, and a literal bumped on a changed line moves each one;
  `ParserExpressions.codex` is token-identical at 8766 tokens (79 to 51 lines
  over 100). **The R7 report is built**: given `LAYOUTREPORT` as its first
  input line, the formatter prints only the lines over 128 of its formatted
  output, as `line: kind: width`, where kind is `string` (a literal longer
  than the room), `table`, `& chain` or `application`. A synthetic chapter
  with 1 line of each kind classifies all 4 correctly. The tracker's 9 R7
  lines are rewritten by hand with `let` bindings and 1 helper,
  `iss-query-where`: the entry chapter's printed output is unchanged, and a
  literal changed inside each rewritten chapter moves it. `codex/tracker`
  has 0 lines over 100, and the formatter leaves it unchanged. **Stage 2 is
  done.**
- **3**, the compiler, quire by quire. **A `when` arm's column is
  semantic**: `parse-match-branches` (`ParserExpressions.codex`) takes an
  `is` as the next arm only on the previous arm's line or at exactly the first
  arm's column. So the formatter moves every arm of one `when` by that
  `when` line's own shift: an `is` line belongs to the innermost open `when`
  whose first arm (a mid-line `is` counts) had the same original column, and
  closes the `when`s it passes. R2 does not split a line with a depth-0 `is`
  after its start, because the split would move a same-line arm off its line.
  The formatter also appends a lone `then` line to the line above. A
  column-2 prose line inside a body keeps every open `if`, `let` and
  `when`; only a line at column 3 (a definition) resets them. The
  arm rule was found on Semantics (`collect-ctor-names`' arms ramped right
  once `in` lines had moved), and both column facts on IR and Types, where
  token-identical text failed to compile (CDX1078) until they held. **Semantics is formatted** (2026-09-30): token
  identity at 5704 and 5191 tokens; the compiler compiles to the depot seed's
  bytes outside the signature window (3,813,351 bytes, 0 differing); controls
  in `collect-top-level-names` and `add-duplicate-cite-error` each move the
  CDX, while one in `assign-chapters` moved nothing because nothing reaches
  that definition. **Ast, Core and Syntax are formatted** (2026-09-30, 24
  chapters, one landing): every chapter token-identical, the compiler again
  0 bytes from the depot seed outside the signature window, and one string
  control per quire (`AstNodes`, `Severity`, `Lexer`) moves the CDX. Lines
  over 100: Ast 319 to 275, Core 155 to 150, Syntax 343 to 231. **IR and
  Types are formatted** the same way (controls in `IRCheck` and `Builtins`):
  IR 876 to 647, Types 931 to 610. `TypeCheckerInference.codex` is not
  formatted: its prose holds U+22A2, which has no CCE code point, so it cannot
  be handed to the guest. **Emit and the top-level chapters are formatted**
  (controls in `CodexEmitter` and `opening.codex`): Emit 793 to 670, top level
  221 to 159. **Stage 3 is done** except `TypeCheckerInference.codex`. R7:
  COMPILER-109.
- **Binary identity does not cover a check that parses SOURCE text**: the
  IR and Types pass broke `check-builtin-alloc`'s one-line row parse, and
  the release gate found it (fixed at main 32394). So every landing also runs
  `build.ps1`'s static source checks (`check-constants`,
  `check-effect-vocab`, `check-sidecars`, `check-cdx-registry`,
  `check-facts-guid`, `check-backlog-ids`, `check-annotation-targets`,
  `check-shell-raw`, `check-pipe-verdicts`, `check-tools`,
  `check-cite-names`, `check-builtin-alloc`, `check-plug-types`) on the
  formatted tree (L-NOGATE). All 13 pass at main 32394, with every earlier pass
  landed.
- **4**, every other quire (`codex/foreword`, `codex/os`, `codex/plugs`,
  `apps`), by the stage 2 recipe: token identity per chapter, and an
  identical CDX from an entry that reaches the changed definitions (a
  test or an app build), with a moving control. **The 23 Foreword and Math
  chapters the compiler cites are formatted** (2026-09-30, 16 changed) by the
  stage 3 proof, because `concat-codex-self.ps1` preloads them into the
  compiler's unit: the compiler is 0 bytes from the depot seed outside the
  signature window, and a control in `map-list-loop` (`ListUtils`) moves it.
  Lines over 100 in those chapters: 172 to 122. **The rest of the foreword
  is formatted except `engine`, `shell` and `punctual`** (2026-09-30, 278
  chapters). The instrument is a CDX per test. `cite-gate.ps1 -ListOnly`
  selects every `codex/test` chapter that reaches a changed chapter. Each is
  compiled before and after (2,003 selections over 10 quires), and every
  one is identical. The 39 that fail both times are `codex/test/errors`
  subjects, which fail by design. The control is a literal bumped on a
  changed line, graded through a test that cites that chapter directly,
  not through `foreword-all-compile`, which cites every chapter and calls
  none. Each landed quire has one control that moved. `engine` (6 tries),
  `shell` (3) and `punctual` (1) found none, so they are not formatted.
  `gpu/BrowserKernels.codex` is excluded while red edits it. Lines over 100:
  compress 147 to 104, math 31 to 12, sim 37 to 10, signal 30 to 19, gpu 108
  to 90, game 147 to 40, ai 451 to 311, ui 412 to 223, encode 513 to 284,
  core 447 to 248. **`codex/plugs` is formatted** (reek, 2026-09-30, 147 of 309 chapters): the
  instrument is each plug's own build (`codex/plugs/<p>/build.ps1`, the
  depot seed), and a chapter lands only if every changed line appears in its
  plug's bundle. All 57 plug builds are byte-identical before and after, and
  each has a literal control in its own chapters that moved. Held back, 110
  chapters whose changed lines do not all appear in their own plug's bundle:
  chiefly the `Stdio` variants, `html/arms`, the `test`, `test-input` and
  `corpus` subjects, and babbage. Lines over 100 in the landed ones: 6,391
  to 4,981. **`codex/os` (9 quires) and `codex/boards` are formatted**
  the same way (145 chapters, 945 selections, all identical, a moving control
  in each quire; the 13 source checks green). The instrument cannot see 3
  arm64 subjects (`arm64-net-calibrate`, `arm64-timer`, `arm64-web-server`),
  which build through the cross plug and fail as x86 CDX both before and
  after. `codex/product` (no test reaches it) and `codex/workflow` (no
  control) are not formatted. Lines over 100: os 1,194 to 601, boards 75
  to 63. **An app is proven through its `opening` chapters as well as the
  tests**: every entry chapter in the app is compiled before and after, and
  a changed chapter outside the cite closure of everything compiled is not
  formatted. `apps/data`, `apps/erp` and `apps/explorer` are formatted
  (60 chapters; 103, 15 and 15 compiles identical; a moving control in
  each), and so are `apps/accp`, `apps/browser`, `apps/circuits`,
  `apps/cvmm` and `apps/safari` (107 chapters; 4, 80, 1, 26 and 19
  compiles identical; a moving control in each, cvmm's on the sixth try).
  `apps/mathbook`, `market`, `globe`, `vision`, `diagram`, `fishtank`,
  `helm`, `edgemesh` and `c64` are formatted the same way (60 chapters; a
  moving control in each, diagram's and helm's through the app's own
  `opening`). Not formatted, because no compiled entry reaches them: 7
  chapters in erp, 7 in accp, 18 in circuits, 8 in cvmm, 4 in mathbook, 4 in
  market, 5 in globe, `ExplorerData`, `ExplorerDb`,
  `browser/pages/dashboard`, `SafariWasm`, `HelmGameBridge`, and all 14
  changed `gpushow` kernels (gpushow has no entry). Lines over 100: data 548
  to 298, erp 120 to 36, explorer 262 to 221, accp 103 to 53, browser 43 to
  40, circuits 158 to 125, cvmm 372 to 240, safari 285 to 168, mathbook 69
  to 21, market 34 to 17, globe 36 to 26, vision 50 to 26, diagram 76 to
  26, fishtank 151 to 127, helm 57 to 38, edgemesh 25 to 13, c64 37 to 28.
  33 more apps are formatted the same way (74 chapters, 1,255 to 922 lines
  over 100); a single-chapter app's control is graded through its own
  compile. Not formatted: chat, compliance, fontai, fontexplorer, gpu,
  guios, iot, perf, radio and realtime (no changed line carries a literal
  whose change moves the CDX), assetforge and site (no entry reaches the
  changed chapters), and `FireworksShow`, `MlpKernels`, `NetToolPersist`,
  `WebGraphics` and 3 workflow chapters (no entry reaches them). Not yet
  taken: works, diffusion, games, codexmagic-mobile, modbuilder, spark,
  prism and landing (other lanes' work on 2026-09-30).
