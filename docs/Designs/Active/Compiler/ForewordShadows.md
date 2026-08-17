# Foreword definitions that shadow native helpers (COMPILER-9, the six)

**Owner:** root, from red's routing 2026-08-16. **Status:** plan, written as
the handoff at 59 per cent context; nothing below is landed unless a stage
paragraph says DONE.

## What is measured (2026-08-16, root, from source)

reek's census (`compiler-backlog` COMPILER-9, main 16076) named six foreword
definitions that shadow a native helper the ARM64 runtime registers under the
same name. Read against `codex/compiler/Types/Builtins.codex` the shape is
sharper than "ARM64 helper shadowed":

| foreword def | Builtins.codex row | x86-64 helper | ARM64 helper | RISC-V helper |
|---|---|---|---|---|
| `StringUtils.codex:7 text-starts-with` | yes, `__text_starts_with` | yes | yes | yes |
| `StringUtils.codex:32 text-contains` | yes, `__text_contains` | yes | yes | yes |
| `StringUtils.codex:59 text-replace` | yes, `__str_replace` | yes | yes | yes |
| `TextSearch.codex:44 text-split` | yes, `__text_split` | yes | yes | yes |
| `Parse.codex:42 text-to-integer` | yes, `__text_to_int` | yes | yes | yes |
| `ListUtils.codex:15 list-tail` | **no row** | only the `__list_tail` intrinsic behind the Cons/Nil desugar | registered | not registered |

So for five of the six the compiler ALREADY has the builtin and the x86-64
emitter, and the foreword's Codex definition wins over it on EVERY lane
(a chapter-level definition shadows a builtin: `emit-apply` asks
`lookup-x86-arity` before `is-builtin`). The native helpers are dead weight on
x86-64 too, exactly the `peek-16` shape (L-UNCALLED), and every program in the
tree, the compiler included, runs the Codex versions today. `list-tail` is
different: there is no builtin, so it needs a row and an x86-64 emitter (the
`__list_tail` intrinsic is a view-sharing tail for the desugar and may not be
what `ListUtils.list-tail` promises: read both before reusing it).

**The consequence red's brief does not state:** deleting the five foreword
definitions changes the IMPLEMENTATION every caller runs, the compiler's own
lexer/parser/checker among them (StringUtils is cited by 57 chapters,
ListUtils by 238, TextSearch by 33, Parse by 17), so the change is
seed-affecting and the risk is a silent semantic difference between a Codex
definition and a helper nobody has run for months: an empty needle to
`text-contains`, an empty or multi-character separator to `text-split`, a
leading `-`/`+`, whitespace or a non-digit to `text-to-integer`, overlapping
matches to `text-replace`. The Codex definitions are the de facto spec; the
helpers must be measured against them BEFORE any def is dropped.

## The method: a differential probe per name, then delete, then re-probe

The battery preloads only the foreword chapters a test CITES, so a test that
does not cite `StringUtils` reaches the builtin and one that does reaches the
Codex definition. That gives the arm for free:

1. For each of the five, `codex/test/shadow-<name>-codex.codex` (cites the
   foreword chapter) and `codex/test/shadow-<name>-native.codex` (does not)
   run the SAME table of inputs -- ordinary cases and every edge listed above
   -- and print the results. Record both `.expected` through `test-run.ps1`
   against the seed. **The two `.expected` files must be identical**; where
   they differ, the difference is the finding and the def is NOT dropped until
   the helper is fixed (or the def's behaviour is declared the spec and the
   helper made to match). Also run both arms on the arm64 and riscv beds: the
   native arm exercises three helper implementations, and they must agree.
2. Only when an arm's pair agrees on all three lanes: delete that foreword def
   (one CL per name is fine, they are independent), gate under the token, seed.
   After the deletion both arms are native and must still print the recorded
   output; the `-codex` arm keeps its cite so the pair stays a differential if
   the def is ever restored.
3. `list-tail`: decide first whether `ListUtils.list-tail` (copying) and the
   `__list_tail` intrinsic (a view) are the same contract; if not, the "native
   helper" the census counted is not a drop-in and the row is closed by
   deleting the ARM64 registration instead of the def. Measure, do not guess.
4. Close COMPILER-9's six-def clause in the backlog row (reek owns the row's
   shadow-check half, already landed 16081); the six app-local redefinitions
   in the census stay the apps' business.

## Measured 2026-08-16 (root): the differential, before anything is dropped

Two arms of one body, `codex/test/text-helper-spec.codex` (cites StringUtils,
TextSearch, Parse: the Codex definitions, which is the de facto spec and the
recorded `.expected`) and the same body without the cites (the native
helpers), run against seed `31A5A0FD` on x86-64 and on the arm64 and riscv
beds:

| case | Codex def (spec) | x86-64 helper | ARM64 helper | RISC-V helper |
|---|---|---|---|---|
| `text-starts-with`, 7 cases incl. empty prefix/text | `TTTFFTF` | same | arm silent, see below | arm silent |
| `text-contains`, 8 cases incl. empty needle/text | `TTTFTFTF` | same | | |
| `text-replace "hello" "" "x"` (empty needle) | **loops to OUT OF MEMORY** | `hello` | | |
| `text-replace`, the other 6 cases | `heLLo,,bb,abc,,abababab` | same | | |
| `text-split "a::b::c" "::"` | 3 `[a][b][c]` | **5** `[a][][b][][c]` | | |
| `text-split`, the other 6 cases | as recorded | same | | |
| `text-to-integer "+7"`, `"abc"`, `"12abc"`, `" 42"` | `0 0 12 0` | **`737 1511 13511 -58`** (accepts anything) | | |
| `text-to-integer` digits, `-42`, `007`, i64-max, `""`, `"-"` | `42 -42 7 ... 0 0` | same | | |

The native arm printed NOTHING on the arm64 bed and on the riscv bed (compile
OK, silent through the retry). Split per name on the arm64 bed (root, same
day): `text-starts-with` `TTTFF`, `text-contains` `TTTFT`, `text-replace`
`heLLo||bb|` all agree with the spec; `text-to-integer "abc"` answers **1511**
like x86-64 (the same accept-anything helper); and the ARM64 `text-split`
helper **FAULTS** on the table (`!A64FAULT ESR=96000050 ELR=40101300
FAR=80000000`, a data abort at an address off the map, on the `"a,b,,c"`,
`""`, `"a::b::c"` triple; which of the three is not yet isolated). RISC-V is
unsplit. red, 2026-08-16: reek has an incoming `0x2F` arm (the ARM64 web
server answers 404 because `/` becomes `n` in bytes-to-Text,
`unicode-bytes-to-text`); add it to the spec arm when it arrives and treat
that helper as part of this campaign. So at least one of the five helpers
per lane faults or lies on this table; the Codex arm passes on arm64 and is silent on riscv
(the riscv-only cluster, not this campaign's). Consequences for the stages
above: `text-starts-with` and `text-contains` may be dropped once the ARM64 and
RISC-V native arms are green; `text-replace` needs the EMPTY-NEEDLE case
settled first (the Codex def is the defective side, it never terminates; the
helper's "unchanged" answer is the sane spec, so fix the def or delete it and
pin the helper); `text-split` and `text-to-integer` must have their x86-64
helpers fixed to the Codex behaviour (whole-string separator; refuse
non-digits, or at least prefix-parse the way the def does) and re-measured
before the def goes. The native arm is the test that goes red today and must
be green on all three lanes before each deletion; keep both arms after.

## Not touched

`Console.codex:8 print-line-uni` (an effect operation, the intended dispatch);
the app-local redefinitions; the shadow check itself (reek, main 16081).

## Risks and costs (R-COST)

Each helper is already in the seed; dropping a Codex def removes code and
allocation, it adds none. Time: a native helper is at worst the Codex loop it
replaces. The only real cost is the semantic risk above, and the differential
is what bounds it.

## Then: plugs 1.17, the ARM64 SVC servicer path

Stage 4 of `docs/Designs/Done/Compiler/Arm64ProcessKernel.md`, unblocked now
that fester's block path is real (CrossLaneFilesystem step 3, main 16001;
step 4 in progress). Before touching `ir-emit-roots` or the servicer, agree
the file split with fester in the fleet channel; fester's claim today covers
`Arm64Runtime.codex` block helpers and `codex/compiler/opening.codex
ir-emit-roots`. Plan doc first, in this directory, with the ARM64 address map
from the 1.10/1.18 designs (pool `#50000000`, table `#40005000`, pid from sp).
