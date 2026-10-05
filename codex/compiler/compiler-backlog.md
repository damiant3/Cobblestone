# Compiler -- open capabilities

Quire-domain backlog, same rules as the app registers: an entry says
what is still missing and nothing else, a closed entry is DELETED, and
a gap that is still real is never quietly dropped. There is no
platform-wide register; `docs/PM/CurrentPlan.md` carries the shape.

**Next id: COMPILER-123.** A closed row is deleted, so the highest row in the
table is not the last id used (COMPILER-117 to 119 closed without a row here);
take this number for a new row and raise it in the same edit.

A deletion is proven seed-neutral only by the BINARY, never assumed: build the
candidate from the depot seed and compare it to that seed byte for byte outside
the signature window (offsets 40..135). COMPILER-64 came out identical and
shipped no seed; COMPILER-23's four unreferenced functions came out 228 bytes
smaller (main 24438), so code nothing references can still be present in the
shipped seed, and a removal that assumes otherwise lands a seed it never proved.

**Structural equality is minted in ONE place and every backend receives it.**
`eq-attach-helpers` (`opening.codex`) appends `eq-helper-defs` to the shared
IR, on both the x86-64 path and the IR wire the plugs consume, which is what
COMPILER-44 fixed after sending them to the emitter alone left every plug
comparing heap pointers. So a gap in derived equality is fixed once, in
`Emit/X86_64.codex` where the helpers are built, and NOT four times; an
estimate of four backend implementations was made and measured wrong on
2026-09-08. What decides whether a comparison reaches a helper at all is
`lower-eq-dispatch` (`IR/Lowering.codex`): it resolves the operand, takes the
type's name and its site actuals, and calls `__eq_<name>@<actuals>`; anything
it cannot name falls through to `IrBinary OpEq`, which for a heap value
compares the two POINTERS. That fallback is what made COMPILER-74 and
COMPILER-75 silent wrong answers rather than diagnostics, and it is the first
thing to check when a comparison answers False for two equal values.

| # | Capability | State of the gap |
|---|---|---|
| COMPILER-122 | **Phase measurement labels a closing heap position as a high-water mark** | `Core/PhaseAllocator.codex` `phase-measure` takes `bivy-hwm` from `__heap-save` at phase close. `Emit/X86_64Builtins.codex` reads the current R10 cursor for that builtin, and `__heap-restore` can lower the cursor. `opening.codex` `format-phase-decks` prints that snapshot as `bivy-hwm` and its difference from the origin as `bivy-used`. These values do not establish a phase's transient peak after reclamation. Clarify the output contract and labels, or measure the peak separately. Acceptance needs an allocate/reclaim fixture whose peak exceeds its closing position. Found during COMPILER-109 TypeChecker scope proof, 2026-10-04; no telemetry implementation change belongs in that formatting batch. |
| COMPILER-116 | **Constructor return annotations (GADT support): deferred, low priority** | Damian, 2026-10-01: "go ahead and log the gadt annotation work. its not hi pri though, unless we have a legit usecase." Implementing GADT support waits for a concrete program that needs it; a lane that finds one cites it here and asks root to raise the priority. Until then the parser deliberately refuses explicit constructor return annotations with CDX1080 `CtorReturnAnnotationUnsupported`, at the colon, before type checking. Ordinary constructor fields remain supported. Tracked crash repros are `codex/test/ctor-return-match.codex` and `ctor-return-no-match.codex`; removing only the annotations gives `ctor-return-control.codex`, which prints 0 then 7. On 2026-10-01, seed CC3FC5222D726096 (main 33794) trapped on both repros at `__list_set_at+0x18`, RIP 0x00101EFA. `build/compile.ps1` reported the same trap at requested 3072 MB and its automatic 8192 MB retry, heap 523.9 MB; these were capped VM runs, not proof of 8 GiB effective memory. The separate uncapped 8192 MB invocation triple-faulted during boot before parsing. Parser probes `ctor-return-parser.codex` and `ctor-return-recovery.codex` pin empty return metadata, EOF, following-constructor recovery and named refusal spans; `ctor-return-unused.codex` pins refusal independent of use. See the reproduction commands below. No GADT implementation belongs in COMPILER-109. |
| COMPILER-109 | **Every eligible compiler line fits in 128 columns** | Owner red (from reek, root 2026-10-03). Damian's 2026-10-01 ruling keeps existing 100-column formatting and takes lines over 200 first. Done 2026-10-03: every signature over 128 wraps after a comma or an arrow, and every compiler line over 200 is rewritten except these 14 (census 2026-10-03, column-2 prose and lines whose longest string literal exceeds 128 excluded). Exempt literal byte tables: `X86_64Chapter.codex` `avx-refusal-text`, `X86_64IO.codex:10`, `:12`. Literal-bound: `CdxCodes.codex` registry rows 335, 336, 337, 361, 363, 368, 394, 395, 398, 423, 451. A row is one `mk-cdx` application and cannot span lines (MultilineApplication), so the only break binds the summary with `let`, which at the registry's 4-space indent leaves 116 columns for the literal; 7 of the 11 summaries run 119 to 125 (`check-cdx-registry` reads only `mk-cdx <const> "Name"` from a line). A `when` arm whose alternatives share a one-token body splits by alternatives with no binary change (measured 2026-10-03, five arms). Lines over 128: Unifier, LoweringTypes, ResolveTypes, AstNodes, X86_64State, X86_64Builtins, X86_64Lir, ChapterScoper, CodexEmitter, X86_64Compound, IRTextEmitter, X86_64Helpers, X86_64TextHelpers, X86_64Chapter, TypeCheckerInference, Parser and Lir are clear except literal-bound lines, `X86_64State.codex:292`, `Lir.codex:498` (`lower-match-chain`, 14 arguments), `Builtins.codex:4`, `Occurrence.codex:6` and `Passes.codex:2` (cite lines), `CdxWriter.codex:31` (the `cdx-header-bytes` head, 13 parameters), `Parser.codex:13` (a cite line: splitting this Parser name list over two cite lines has not been graded against the cite checkers), and in `X86_64Chapter` `cdx-build-header` (11 parameters: its head and its one call) and the `x86-64-emit-cdx-with-exit-mode` head (10 parameters), because no definition head in the tree continues on a following line (the `lower-defs-keep` record is the fix) (`init-rodata`, a constant whose `&` chain cannot wrap and whose body no constant in the compiler starts on a following line). A parenthesized application continues across newlines (`parse-app-loop` skips them at paren depth above 0); that form is the continued-argument shape the narrow-language ruling refuses, so rewrites use `let` or a `deck-record (` extent holding a `let` chain (`ResolveTypes` IrTry). Held in `Builtins.codex`: the 34 `bs-type` lines over 128 in the builtin table. A `let` inside a table entry moves the startup heap frontier: with 32 entries rewritten (2026-10-03) every deck moved 224 bytes, 46 allocation goldens in the cite-gate went red and desk-root-guard stopped compiling, while the same chapter without them is green. Rewrite the table only together with refreshing those goldens. Done 2026-10-03 as well: DiagnosticBag, CdxWriter, X86_64InsnCount, IRFidelity, LambdaLifting, NameResolver, Lexer, ParserCore, CodexType, ConstShare, Simplify, ParserExpressions, SyntaxNodes (but its `UnitFamilyBody` copy arm, which opens a multi-line record), Utf8Cce's decode sums, CodexTypeTree's two folds and X86_64Boot's four code chains. Not yet taken: Utf8Cce's two block tables (constants `check-constants` reads from source), CodexTypeTree's `RecordTy` copy arm (a multi-line record), X86_64ProcessHelpers' three spawn-slot chains (no split of the three operands fits 128). TypeChecker is clear except literal-bound lines (R7, 2026-10-04). Its invariant `CheckScope` is allocated before the batch reclaim mark and reused across batches: one 40-byte record per chapter check, with no new asymptotic heap/time growth. The split Build Settings cite compiles standalone and alone leaves compiler bytes unchanged. COMPILER-122 records the phase-end measurement limitation. Remaining COMPILER-109 work waits behind red's UOAIX stage N assignment (root, 2026-10-04). Resume with the remaining chapters (census 2026-10-03: Desugarer 92, MethodSpecialization 65, Lowering 65, opening 51, X86_64's cite line (11) and literal-bound line (`deck-record (IrError` with a 113-column text) and a tail of one to six lines per chapter: list it by censusing `codex/compiler` for lines over 128, leaving out column-2 prose and any line whose longest string literal is longer than 128 minus its indent minus 8, then drop the lines this row holds). Comparison tests for a batch are programs that reach the changed code; each COMPILER-109 batch CL description names the set it used. Method, per batch, each step a check that can fail: bind sub-expressions with `let` in left-associated order and split no string literal; inside a `deck-record` keep every binding inside that extent (Sketchbook, Deck-Bound Mode); then stage 2 == 3, `-Measure` seed against candidate, program bytes, `-IrUni` text and `-DebugMode -EscapeCheck` diagnostics compared on test programs that reach the change, BVT, cite-gate, the 13 static source checks, signed self-verified seed. Run `codex/build/CodeLayoutFormat.codex` with `LAYOUTREPORT` as the first input line for R7 (`docs/Designs/Active/Build/CodeLayout.md`). |

## Constructor annotation reproduction

From the repository root, export the old seed to reproduce the trap. Use
`seed/Codex.cdx` as `-Kernel` to grade the installed refusal instead. The
negative sidecars require CDX1080 and its colon spans; a trap is never a pass.

```powershell
New-Item -ItemType Directory -Force build-output/compiler116-repro
p4 print -q -o build-output/compiler116-repro/cc3.cdx //Codex/main/seed/Codex.cdx@33794
pwsh build/compile.ps1 -Src codex/test/ctor-return-match.codex -Out build-output/compiler116-repro/match.cdx -Log build-output/compiler116-repro/match.log -Kernel build-output/compiler116-repro/cc3.cdx
pwsh build/compile.ps1 -Src codex/test/ctor-return-no-match.codex -Out build-output/compiler116-repro/no-match.cdx -Log build-output/compiler116-repro/no-match.log -Kernel build-output/compiler116-repro/cc3.cdx
pwsh build/compile.ps1 -Src codex/test/ctor-return-control.codex -Out build-output/compiler116-repro/control.cdx -Log build-output/compiler116-repro/control.log -Kernel build-output/compiler116-repro/cc3.cdx
pwsh build/test-run.ps1 -Kernel build-output/compiler116-repro/control.cdx -OutFile build-output/compiler116-repro/control.serial
```

The first two compiles fail on CC3; the control succeeds and its output must
equal `ctor-return-control.expected`. The original matched/no-match stderr
and corrected `reproduce.ps1` remain in
`D:/Projects/Cobblestone-reek/build-output/compiler109-ctor-fields/`.
That runner's success means the old trap was reproduced, not that annotated
constructors work. The tracked sources above are the durable repro inputs.

For the current parser and refusal fixtures, the sidecar-aware runner is:

```powershell
New-Item -ItemType Directory -Force build-output/compiler116-repro
Get-ChildItem codex/test/ctor-return-*.codex | ForEach-Object FullName | Set-Content build-output/compiler116-repro/subjects.txt
pwsh build/bvt.ps1 -CodexCdx seed/Codex.cdx -Jobs 1 -SubjectsFile build-output/compiler116-repro/subjects.txt
```
