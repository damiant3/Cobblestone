# reek -- what is blocked in the plugs close-out lane

Written 2026-08-21 at Damian's direction, so the lane can be put down and
picked up later without re-deriving why most of its register is not
drawable. This is NOT `reek-workplan.md`, which stays empty by design.
`codex/plugs/plugs-backlog.md` remains the register; this file says only
which of its rows cannot be worked on this box, and why.

Re-check before trusting any row here: every reason below is a measurement
with a date, and two of them turn on what is installed, which can change.

## Blocked on the no-new-toolchains rule

Damian's standing rule is that no new build environment is installed now.
These are not open work until that lifts.

| row | needs | measured |
|---|---|---|
| 1.14 deep recursion class per plug | a runtime per language to ablate | 2026-08-21 |
| 1.20 pascal record type | `fpc` / `ppcx64` / `lazbuild` | absent |
| 1.39 cobol subjects | `cobc` | absent |
| 1.46 wire text plugs to the oracle | any runtime for an unwired plug | 2026-08-21 |

**1.46 is the one that looks drawable and is not.** Six plugs are wired
(python, javascript, typescript, zig, wasm, csharp) and every one had its
runtime on the box already. Swept 52 executable names covering every
remaining emitter, including the alternate spellings a one-name-per-language
check misses (`clj`, `luajit`, `ldc2`, `runghc`, `guile`, `racket`,
`scalac`, `swiftc`, `ocamlfind`). Exactly one is present, `nvcc`, and it
compiles ptx device code rather than running the console subject the oracle
grades. Wiring has never been the constraint; the runtime has. Run
`Get-Command` for the language FIRST; if one has appeared, the wiring is one
entry in the `$Plugs` table in `build/plug-oracle-test.ps1`, which is blu's
claim.

**The consequence worth knowing before relying on the oracle:** fortran,
wgsl and babbage cannot be covered by it on this machine at all. For those
plugs, reading the emitted source against the x86-64 oracle by evaluation is
not a shortcut, it is the only instrument there is. Two register rows were
wrong about their own defect this session and reading is what caught both,
but reading is also what put one of them in wrong in the first place.

## Not mine, or already called

| row | why |
|---|---|
| 1.1 | DEFERRED by Damian 2026-08-05 |
| 1.3 | fester's |
| 1.33 | blu's |
| 1.53a, 1.54 | closed-out zig notes; the real closure is a custom allocator over `VirtualAlloc` and `mmap`, which is a bigger change than either row |
| babbage | SHELVED by Damian 2026-08-21 as vanity work; items in `codex/plugs/babbage/babbage-backlog.md` |

## Drawable, and what I would take first

Three rows are mine and unblocked:

- **1.41** -- a per-byte receive accumulate costs 116.77 s per 16 MB, and the
  shape is still in several harnesses. A measured symptom with a real cost.
  This is the one to take first.
- **1.29** -- three stale ARM64 load-address constants, whose only reader is
  `arm64-build-elf`, a second ELF builder nothing invokes beside the
  PowerShell one the cross bed uses. Turns on a DELETION call that a
  constants row should not make alone; ask before spending time.
- **1.48** -- the arm64 mov-elimination peephole is unsound in the general
  case. The standing gap is `br`, an indirect branch this lane emits none of
  today, so there is no complainant.

## Instrument left behind

`codex/plugs` was censused 2026-08-21 for one defect shape (a `let` bound in
expression position). All 56 plugs rebuilt, 49 emitted. The method is worth
reusing for any single-shape question: one subject, one run per plug, then
READ the flagged and the cleared alike. Seven of nine flags were false
positives from the classifier regex, and one more was a false positive from
my own misreading of folded wat that `wat2wasm` and `wasmtime` refuted.
Verify through a runtime wherever one exists.
