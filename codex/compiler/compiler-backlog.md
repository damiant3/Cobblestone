# Compiler -- open capabilities

Quire-domain backlog, same rules as the app registers: an entry says
what is still missing and nothing else, a closed entry is DELETED, and
a gap that is still real is never quietly dropped. There is no
platform-wide register; `docs/PM/CurrentPlan.md` carries the shape.

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
| COMPILER-118 | **Compile wrapper accepts MEASURE output** | `build/compile.ps1 -Measure` sends the supported MEASURE command, but its output parser accepts SIZE, source errors, traps or IR framing only. On 2026-10-01, main34028 seed 0FD86AE43ACF3693 produced complete PHASE/DECK/EMIT-BYTES output for `codex/test/arithmetic.codex` and exited normally; the wrapper returned 4 with `FAIL: the VM produced output but no SIZE: line`. Add an explicit measurement-output contract with fresh, complete output and guest termination checks, preserving crash/refusal detection. Reproduce with `pwsh build/compile.ps1 -Src codex/test/arithmetic.codex -Out build-output/measure.txt -Log build-output/measure.log -Kernel seed/Codex.cdx -Measure`. The retained-list regression is covered separately by `codex/test/check-empty-fidelity-lifetime.codex`. |
| COMPILER-116 | **Constructor return annotations (GADT support): deferred, low priority** | Damian, 2026-10-01: "go ahead and log the gadt annotation work. its not hi pri though, unless we have a legit usecase." Implementing GADT support waits for a concrete program that needs it; a lane that finds one cites it here and asks root to raise the priority. Until then the parser deliberately refuses explicit constructor return annotations with CDX1080 `CtorReturnAnnotationUnsupported`, at the colon, before type checking. Ordinary constructor fields remain supported. Tracked crash repros are `codex/test/ctor-return-match.codex` and `ctor-return-no-match.codex`; removing only the annotations gives `ctor-return-control.codex`, which prints 0 then 7. On 2026-10-01, seed CC3FC5222D726096 (main 33794) trapped on both repros at `__list_set_at+0x18`, RIP 0x00101EFA. `build/compile.ps1` reported the same trap at requested 3072 MB and its automatic 8192 MB retry, heap 523.9 MB; these were capped VM runs, not proof of 8 GiB effective memory. The separate uncapped 8192 MB invocation triple-faulted during boot before parsing. Parser probes `ctor-return-parser.codex` and `ctor-return-recovery.codex` pin empty return metadata, EOF, following-constructor recovery and named refusal spans; `ctor-return-unused.codex` pins refusal independent of use. See the reproduction commands below. No GADT implementation belongs in COMPILER-109. |
| COMPILER-115 | **Global rename duplicates have explicit precedence** | `Semantics/ChapterScoper.codex`, `build-global-rename-table`, reverse-pushes assignments, sorts only by original name and keeps the first duplicate. `Foreword/Sort.sort-by` is unstable in-place quicksort, so the former stable-merge-sort/last-inserted-winner assumption is false. On 2026-10-01, depot B1AB747C163BC10C sorted three equal-key records supplied as last/middle/first with middle first (`D:/Projects/Cobblestone-reek/build-output/compiler109-scoper-maps/tie-order.*`). This proves the sort-boundary issue; required full-table precedence and end-to-end wrong-answer impact are not yet established. Make the intended tie policy explicit, then grade actual rename tables and colliding-chapter programs. No behavior repair is part of COMPILER-109. |
| COMPILER-114 | **Function signatures accept newline continuation** | `Syntax/Parser.codex`: `comma-starts-type-param` examines the immediate token after a comma before `parse-type-continue` skips newlines; the arrow branch calls `parse-type` without skipping them. On 2026-10-01, depot 7DC6251D63672A7E compiled `add : Integer, Integer -> Integer` and the control returned 3. Breaking immediately after the comma failed CDX1000; breaking after the arrow failed CDX2001. Sources/logs: `D:/Projects/Cobblestone-reek/build-output/compiler109-collectors/signature-*`. This prevents wrapping long signatures under `docs/Designs/Active/Build/CodeLayout.md` without API/type changes. Grade comma and arrow continuation against flat signatures and malformed-input refusals. Parser work belongs here; COMPILER-109 leaves signatures unchanged. |
| COMPILER-109 | **Every eligible compiler line fits in 128 columns** | Damian's 2026-10-01 width ruling preserves existing 100-column formatting and prioritizes lines over 200. Census of 69 compiler chapters, main 34159 plus DEV34136 and DEV34150, excluding prose and string/table exemptions: 1,139 current-source lines over 128, 177 over 200; 126 are signatures, including 11 over 200. The 166 non-signature lines over 200 include three cite declarations. Syntax has no eligible expressions over 200; its six remaining long lines are signatures. After an in-memory formatter pass, R7 has 1,133 over 128 and 176 over 200: Ast 127/17, Core 66/8, Syntax 60/6, IR 263/36, Types 270/52, Emit 259/40, Semantics 18/0, top level 70/17. These R7 pairs include signatures. `docs/Designs/Active/Build/CodeLayout.md`, Current width report, owns the method and evidence in `build-output/compiler109-batch2-rebased/census/`. `Types/TypeCheckerInference.codex` is included by blanking column-2 prose only in guest input; its repository source remains unformatted. Run `codex/build/CodeLayoutFormat.codex` with `LAYOUTREPORT` as the first input line for R7. Hand expression rewrites land on the seed path; leave signatures to COMPILER-114. |

## Measurement wrapper reproduction

For COMPILER-118's historical observation, export main34028's seed and use
`build-output/measure-repro/seed.cdx` as the wrapper command's `-Kernel`.
The current seed command in the row checks whether the gap remains. The
wrapper returns 4 despite complete PHASE/DECK/EMIT-BYTES output and normal
guest termination; a guest trap is a different failure.

```powershell
New-Item -ItemType Directory -Force build-output/measure-repro | Out-Null
p4 print -q -o build-output/measure-repro/seed.cdx //Codex/main/seed/Codex.cdx@34028
Get-FileHash build-output/measure-repro/seed.cdx
```

Run the row's compile command through the detached, RAM-checked procedure
in `docs/Agents/CoordinationProtocol.md`, recording its PID and log in the
lane status. Successful measurement framing requires guest exit zero,
complete PHASE/DECK/EMIT-BYTES output and no trap or dropped serial bytes.

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
