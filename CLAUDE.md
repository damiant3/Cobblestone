# CLAUDE.md — Codex Project Instructions

## What This Is

Codex is a new programming language, compilers (reference and selfhost), tools, operating system, repository protocol, trust lattice, encoding, and more.  We take the best of type theory, language design, aesthetics, security research, and actual practice.  We leave everything else behind.  If we didn't build it, we don't trust it.  Codex is a new computational substrate intended to be impervious to all currently known attack vectors by-design.

The project was started 3/14/2026.

## Current State (MM4 — Proven 2026-04-24)

**MM4 is proven.** Codex now compiles itself end-to-end on bare metal, and the output
of that self-compile compiled by itself is byte-identical to itself. The fixed
point holds. No C# anywhere in the chain.

Design doc: `docs/Active/Compiler/SECOND-BOOTSTRAP.md`
Current plan: `docs/CurrentPlan.md`
Backlog: `docs/BACKLOG.md`

### Milestone — 2026-04-24: The cord is cut

All four bootstraps green for the first time, 41 days from project start (2026-03-14):

| Bootstrap | Path | Result | Output |
|---|---|---|---|
| BS1 | .NET → C# | stage 1 = stage 3 | 1,147,278 chars |
| BS1.1 | .NET → Codex | stage 1 = stage 2 | 686,503 chars |
| BS2 (pingpong) | bare-metal → Codex | sem-equiv + stage 1 = stage 2 | 686,535 bytes |
| BS3 | bare-metal → ELF | stage 1 = stage 2 | 1,224,304 bytes |

Submitted as Perforce CL 340 / GitHub `62e9c4e`.

## The Rules

### 1. The acceptance test is the sample battery plus byte-identity

Correctness is **not** "stages agree." Stage agreement is a self-compilation
gate — it proves that stage N and stage N+1 produce the same bytes. It does
not prove the bytes are right. Pre-CL-128, pingpong passed while the
compiled programs were silently wrong (missing `IRLambda` / `IRRunState`
emit cases, register-clobbering predicate builtins, unpropagated type
args — see `docs/Active/Compiler/REF-LESSONS-FOR-SELFHOST.md`). Agreement
with a broken compiler is ceremonial.

The acceptance test has two parts. Both are required; neither is
sufficient alone:

1. **Sample battery.** `tools/sweep.sh` diffs every sample's runtime
   output against a hand-verified `.expected` snapshot, every compile
   failure against a `.failing` CDX code sidecar, and enumerates
   skips against `.skip` reasons. 54 verified / 8 diag / 10 skip / 0 fail
   of 72 samples as of CL 128. This is the correctness anchor.
2. **Byte-identity between stages.** `pingpong-self.sh` (bootstrap 2):
   stage 1 === stage 2 byte-identical under the self-built SUT. This is
   the self-compilation gate — it proves the compiler is a fixed point of
   its own output. Sem-equiv between source and stage 1 follows from
   byte-identity when both compilations run on the same SUT
   (SUT(source) == SUT(stage 1)). The legacy `pingpong.sh` (REF builds
   the ELF, REF runs sem-equiv) is preserved for comparison.

There are **three separate bootstraps**. Do not confuse them. See
`docs/CodexBootstrap.png` and `docs/Test/BOOTSTRAP-REPORT.md`.

- **Bootstrap 1** — .NET self-host, emits C#. Fixed point: stage 1 === stage 3.
- **Bootstrap 1.1** — .NET self-host, emits Codex text. Fixed point: stage 1 === stage 2.
- **Bootstrap 2 (pingpong)** — bare-metal ELF under QEMU, emits Codex text.
  Byte-identity gate: sem-equiv PASS + stage 1 === stage 2.
- **Bootstrap 3** — bare-metal ELF emits ELF. Fixed point: stage 1 ELF === stage 2 ELF. Proven 2026-04-24 (CL 340).

Bootstraps 1 and 1.1 run under `dotnet` and are now **legacy** (REF is
locked, see `docs/CurrentPlan.md`). Pingpong is bootstrap 2, and **only**
bootstrap 2. A green "BOOTSTRAP 1" or "BOOTSTRAP 1.1" line from `codex
bootstrap` says nothing about pingpong. If you report pingpong green, it
is because `wsl bash tools/pingpong-self.sh` ran green: stage1 === stage2
byte-identical under the self-built SUT **and** the sample battery is
green on the same compiler.

Every change that touches codegen must pass both gates before it is
considered done. If either is red, back it out.

### 2. Read before you write

Do not modify code you have not read. Do not guess at file contents. Do not assume
structure from names. The self-hosted compiler has subtle invariants — a wrong
assumption will cost hours.

### 3. One thing at a time

This is in the principles doc and it is the most violated rule. Do one thing. Test it.
Commit it. Then do the next thing. Do not batch. Do not "while I'm here." The compiler
is 12,000 lines of Codex and 7,000 lines of C# codegen. A wrong change in one place
surfaces as a silent corruption three pipeline stages later.

### 4. CCE is the internal encoding

Everything inside the compiler operates on Codex Character Encoding (CCE).
Unicode conversion happens ONLY at I/O boundaries. Do not introduce Unicode
assumptions in internal code.

### 5. Never use python.

If you need to write a script, you can use bash (.sh), powershell (.ps1), Codex, or C#.  We don't need another dependency.

### 6. Parity is narrow

The reference (`src/`) is a baseline, not a mirror. The self-host (`Codex.Codex/`) is a strict superset — free to do more, do better, diverge on shape. The parity requirement is narrow: anything that affects the **compilation output** (lexer, parser, desugarer, type checker, lowering, codegen semantics) must mirror precisely; everything else (diagnostics wording, CLI output, debug dumps, profiler output, span precision, error formatting) is free to diverge. The acceptance test for the narrow invariant is Rule 1 (sample battery + byte-identity). Full treatment in principle 11 of `docs/10-PRINCIPLES.md`. Live port guide: `docs/Active/Compiler/REF-LESSONS-FOR-SELFHOST.md`. The older pattern-matched parity matrix in `docs/Active/Compiler/SELF-HOST-PARITY-AUDIT.md` is tagged STALE as of CL 128 and kept only for historical context.

### 7. The entry-point identifier is `opening`

A Codex program's entry point is the function named `opening`, not `main`. This is an aesthetic choice — Codex leans on the book metaphor (chapters, cites, foreword, opening). The compiler's entry-point lookup is hardcoded to `opening` across every emitter backend (`src/Codex.Emit.*`) and in the self-host (`Codex.Codex/Emit/X86_64Chapter.codex` emit-start). Scope-adefs exempts `opening` from slug-mangling; two chapters both defining `opening` is a hard diagnostic, not a silent rename. `main` is free for user code — Forewords and libraries may use it without colliding with the compiler's entry.

### 8. Every review assesses memory and time-complexity risk

This runs on finite hardware. Every review (self-review before shelving, or review of someone else's CL) must include an explicit risk assessment for **heap blow-up** and **time complexity**. "Easiest implementation" is not a defense when it blows the heap or runs quadratic.

**Inspection is the first test. Testing is the fallback.** A 5-minute pingpong run is expensive relative to a 30-second code read. Default to reasoning from the code:

1. **Inspect first.** Read the changed lines. Ask: does this add a loop? An accumulator? A buf↔List round-trip? A call to `buf-read-bytes` in a hot path? A new recursion without a fuel cap? If the answer is "no — this is a bounded match-arm addition / a leaf function / a one-shot computation", inspection alone is sufficient.
2. **Test when genuinely unsure.** If the change's cost depends on data you don't have (call count at runtime, input-size scaling, existing accumulator behavior), or if the surrounding code is complex enough that inspection would take longer than a test run, run pingpong before/after and diff `heap hwm` + elapsed time.
3. **Never skip the assessment.** Every CL review must state, in one or two lines, the memory and time-complexity verdict — "inspection: no loops, bounded match-arm, O(1) per call, no heap impact" or "measured: +4 MB stage 1 heap, +0.2s, acceptable for X reason". Silent approvals are not allowed.

**No guessing about allocation behavior.** If a claim is about an implementation's cost, it must cite a file:line. `src/Codex.Emit.X86_64/X86_64CodeGen.cs` is authoritative for `__list_append`, `__list_snoc`, `__buf_read_bytes`, etc. (e.g. `__list_append` is amortized O(1) per element via geometric realloc — verified at line 5468, not "I think it copies".) Claims without a reference are speculation and will be treated as such.

**Red flags that demand a second look.** `buf-read-bytes` in hot paths (8× byte-to-List-slot blowup). Repeated buf→List→buf round-trips. Building full-pipeline accumulators with List when a buf would do. Retaining AST/IR across phases when `heap-save`/`heap-restore` would reset it. Nested loops with unclear pairing. Pattern-matching an allocating idiom from surrounding code without asking whether it's cheap. Bare-metal pingpong has no GC — every allocation is permanent until the producing function returns.

**Localization when something grows.** `Codex.Codex/opening.codex` `compile-measure` instruments 14 phase boundaries with `heap-save`. Use it to localize growth instead of guessing which phase is the culprit. Pingpong already reports `heap hwm` per stage — diff before/after non-trivial changes when inspection is inconclusive.

### Key Tools

| Tool | What |
|------|------|
| `tools/pingpong-self.sh` | Self-compilation acceptance test, selfhost-driven (WSL); routes through Codex.Cli-Codex with no REF in the chain |
| `tools/pingpong.sh` | Legacy pingpong (REF-driven), kept for comparison |
| `tools/codex-agent/codex-agent.exe` | Agent toolkit (orient, build, test) |
| `tools/Codex.Cli-Codex/` | Selfhost-side CLI (Codex source compiled to .NET DLL by the selfhost itself). Ports `dump-source` and `build --target x86-64-bare` from REF Cli; remaining commands print "not yet implemented" pending port. |
| `tools/CodexHost/` | Launcher that hosts the selfhost dll on a 32 MB-stack thread (selfhost's curried lambdas overflow .NET's 1 MB default on large source). |
| `tools/Codex.Bootstrap-Codex/` | Selfhost-side bootstrap driver (replaces Codex.Bootstrap in the no-REF chain). |
| `tools/Codex.Cli/` | Legacy REF CLI (.NET, C#); no longer in the bootstrap path. |
| `tools/Codex.Bootstrap/` | Legacy REF bootstrap driver; no longer in the bootstrap path. |

### Build and Test

```bash
dotnet build Codex.sln              # Builds everything
dotnet test Codex.sln               # Runs all tests
bash tools/sweep.sh                 # Sample battery (selfhost via QEMU, ~2-5s per sample)
wsl bash tools/pingpong-self.sh     # Bootstrap 2 (pingpong, selfhost-driven): bare-metal ELF emits Codex text
wsl bash tools/bootstrap3.sh        # Bootstrap 3: bare-metal ELF emits ELF (not pingpong — no ELF ingester)
```

## Agent Identity

Working directory: `D:\Projects\NewRepository-XXX`. Use pwd to find the actual XXX value.
You are **XXX** — the last 3 characters of your working directory name. The current roster is **Cam**, **Nib**.
Agent file: `docs/Agents/<your-name>.txt`

## What Not To Do

- Do not add features beyond what is asked
- Do not refactor unrelated code
- Do not add comments, docstrings, or type annotations to code unless a strong argument can be made that it prevents rediscovery
- Do not create abstractions for one-time operations
- Do not introduce Unicode handling inside the compiler

## Kudos

To Anthropic and the Claude team — Codex's bootstrap was built with Claude
Opus 4.6/4.7 (1M context) running as a small team of parallel agents
under Claude Code. The 1M-token window made it tractable to review thousand-line
codegen diffs against IR invariants in a single pass. The Agent SDK's
parallel-agent model let multiple agents work distinct CLs simultaneously
without cross-contaminating their reasoning. The harness's permission model
and sandboxing made it safe to give the agents direct access to git, p4, WSL,
QEMU, and gdb without supervising every command. Persistent memory across
sessions meant context compounded instead of evaporating between runs.

Forty-one days from project start to a self-sustaining bare-metal compiler is
not a thing one human plus one shell does. It's a thing one human plus a team
of disciplined agents does. Codex stands on the shoulders of the C# self-host,
which stands on the shoulders of Claude. Thank you.

