# Shell Script Emitter

## Problem

The Codex build pipeline depends on ~15 PowerShell scripts that
orchestrate compilation, testing, signing, and verification. These
scripts are the last external dependency between "Codex compiles
itself" and "Codex controls its own build." They also lock the
build to Windows -- there is no path to running the gates on Linux
or macOS without rewriting them by hand.

The scripts contain real logic: VM lifecycle management, file
comparison, output parsing, retry loops, parallel job dispatch,
exit-code gating. Deleting them requires replacing that logic, not
just stubbing it out.

## Approach

Write the build orchestration logic in Codex. Emit shell scripts
through plugs -- the same architecture we use for Ada, Python, Rust,
and the other 47 transpiler targets. One Codex source, multiple
shell targets.

```
build.codex (Codex source)
    |
    +---> PowerShell plug ---> build.ps1
    +---> Bash plug ---------> build.sh
    +---> Zsh plug ----------> build.zsh
```

The generated scripts are checked in as build artifacts (like the
generated HTML for web apps). A developer clones the repo and runs
the generated script for their platform. The Codex source is the
single source of truth; the scripts are derived.

## Architecture

### 1. Shell DSL Foreword (`codex.foreword.shell`)

A small domain-specific library for shell operations. Not a
general-purpose language -- bounded to what build scripts need.

```codex
Chapter: Shell
  cites Foreword chapter Maybe

Section: Types

  ShellCmd =
    | Run (Text) (List Text)
    | RunCapture (Text) (List Text)
    | Copy (Text) (Text)
    | Delete (Text)
    | Mkdir (Text)
    | WriteFile (Text) (Text)
    | ReadFile (Text)
    | Env (Text)
    | SetEnv (Text) (Text)
    | Cd (Text)
    | Exit (Integer)
    | Echo (Text)
    | If (ShellExpr) (ShellBlock) (ShellBlock)
    | For (Text) (ShellExpr) (ShellBlock)
    | While (ShellExpr) (ShellBlock)
    | Pipe (List ShellCmd)
    | Parallel (List ShellBlock)
    | Try (ShellBlock) (ShellBlock)

  ShellExpr =
    | ExitCodeEq (Integer)
    | FileExists (Text)
    | DirExists (Text)
    | StringEq (Text) (Text)
    | Not (ShellExpr)
    | And (ShellExpr) (ShellExpr)
    | Or (ShellExpr) (ShellExpr)
    | Var (Text)
    | Lit (Text)
    | FileSizeBytes (Text)
    | Concat (ShellExpr) (ShellExpr)
    | CaptureOutput (ShellCmd)

  ShellBlock = List ShellCmd

Section: Script

  ShellScript = record {
    name : Text,
    description : Text,
    params : List ShellParam,
    body : ShellBlock
  }

  ShellParam = record {
    param-name : Text,
    param-type : ShellParamType,
    param-default : Maybe Text
  }

  ShellParamType = | ParamString | ParamInt | ParamSwitch | ParamPath
```

The DSL is intentionally shallow. No variables beyond params and
captures. No functions (use multiple scripts). No arithmetic beyond
exit-code comparison. This keeps the emitters simple and the
generated scripts readable.

### 2. Shell Emitter Plugs

Three plugs, one per target shell:

| Plug | Target | Directory |
|------|--------|-----------|
| `powershell` | PowerShell 7+ | `codex/plugs/powershell/` |
| `bash` | Bash 4+ | `codex/plugs/bash/` |
| `zsh` | Zsh 5+ | `codex/plugs/zsh/` |

Wait -- we already have shell-like emitters? No. The existing plugs
emit programming languages. Shell scripts have fundamentally
different semantics:

- **No types.** Everything is a string or an exit code.
- **Process-centric.** The unit of work is a process, not an
  expression. Exit codes are the error model.
- **Quoting hell.** Each shell has different quoting, escaping,
  and variable expansion rules. The emitter must handle these
  correctly or the generated script breaks on filenames with
  spaces.
- **Platform-specific commands.** `Copy-Item` vs `cp`,
  `Remove-Item` vs `rm`, `Start-Process` vs `&`.

Each emitter translates the ShellCmd/ShellExpr IR into idiomatic
shell code for its target. Examples:

**ShellCmd: Run "codex-vm" ["-mem", "3072", "seed/Codex.cdx"]**

PowerShell:
```powershell
& "codex-vm" -mem 3072 "seed/Codex.cdx"
```

Bash:
```bash
"codex-vm" -mem 3072 "seed/Codex.cdx"
```

**ShellCmd: If (FileExists "build-output/Sut.cdx") then ... else ...**

PowerShell:
```powershell
if (Test-Path "build-output/Sut.cdx") { ... } else { ... }
```

Bash:
```bash
if [ -f "build-output/Sut.cdx" ]; then ... else ... fi
```

**ShellCmd: Parallel [block1, block2, block3]**

PowerShell:
```powershell
$jobs = @()
$jobs += Start-Job -ScriptBlock { ... }
$jobs += Start-Job -ScriptBlock { ... }
$jobs += Start-Job -ScriptBlock { ... }
$jobs | Wait-Job | Receive-Job
```

Bash:
```bash
( ... ) &
( ... ) &
( ... ) &
wait
```

### 3. Build Logic in Codex

The current PS1 scripts become Codex source files in a new
`codex/build/` directory (or `apps/works/build/`):

| Current PS1 | Codex Source | What it does |
|-------------|-------------|--------------|
| `build/build.ps1` | `Build.codex` | Full gate run: clean, concat, compile, sign, canary, text round-trip, sem-equiv, CDX fixed point, BVT |
| `build/test.ps1` | `TestBattery.codex` | Batch compile + run samples, compare .expected sidecars |
| `build/compile.ps1` | `CompileOne.codex` | Compile one .codex file through codex-vm |
| `build/build-apps.ps1` | `BuildApps.codex` | Bundle + compile all web apps through HTML plug |
| `build/test-cross.ps1` | `TestCross.codex` | Cross-compile + Renode test for ARM64/RISC-V |
| `build/concat-codex-self.ps1` | `ConcatSelf.codex` | Gather and concatenate compiler source files |

Each Codex source file uses the Shell DSL to describe the build
steps. The emitter produces the target shell script. The generated
scripts are checked in under `build/` (replacing the current
hand-written ones).

### 4. Bootstrap Path

Chicken-and-egg: to run the Codex build, you need the generated
scripts. To generate the scripts, you need to run the Codex build.

Resolution: the generated scripts are **checked-in artifacts**,
like the seed CDX. They are regenerated when the build logic
changes, just as the seed is rebuilt when the compiler changes.
A developer cloning the repo for the first time runs the checked-in
script. A developer changing the build logic runs the emitter to
regenerate scripts, verifies the gates still pass, and checks in
the updated scripts alongside the Codex source change.

The thinnest possible bootstrap: one hand-written script per
platform that knows how to:

1. Build `codex-vm.exe` (or locate a pre-built binary)
2. Boot the seed CDX
3. Run the shell emitter plug on the build logic source
4. Execute the generated build script

This bootstrap shim is the only hand-written shell code in the
project. Everything else is generated.

## Phases

### Phase 1: Shell DSL + PowerShell Emitter

- Write `codex.foreword.shell` (Shell.codex) with the types above
- Write the PowerShell emitter plug (`codex/plugs/powershell/`)
- Port `compile.ps1` to Codex as proof of concept
- Generate `compile.ps1` from the Codex source
- Verify the generated script produces identical results

### Phase 2: Full Build Port

- Port remaining 14 PS1 scripts to Codex
- Generate all PS1 from Codex source
- Delete hand-written PS1 (keep bootstrap shim only)
- Verify full gate run with generated scripts

### Phase 3: Bash + Cross-Platform

- Write the Bash emitter plug
- Generate `build.sh`, `test.sh`, etc.
- Verify gates on Linux (WSL or native)
- Optional: Zsh emitter for macOS

### Phase 4: DevConsole Integration

- Wire the Shell DSL into the DevConsole UEFI menu
- "Self-Compile" and "Run Tests" execute build logic
  natively without any external shell
- The generated scripts become a convenience, not a
  requirement

## Design Principles

1. **Generated scripts must be readable.** A developer should
   be able to open the generated `build.ps1` and understand it
   without knowing Codex. The emitter must produce clean,
   idiomatic shell code, not obfuscated one-liners.

2. **The DSL is intentionally limited.** If you need loops over
   complex data structures, that logic belongs in Codex proper
   (compiled to CDX and run in codex-vm), not in the shell DSL.
   The shell layer is glue: launch processes, check results,
   move files.

3. **No runtime dependency.** The generated scripts must run
   on a stock OS install -- no npm, no pip, no special modules.
   PowerShell 7+, Bash 4+, and Zsh 5+ are the only requirements.

4. **One source of truth.** The Codex source IS the build logic.
   The generated scripts are derived artifacts. Editing a
   generated script by hand is an error (like editing a .o file).

5. **Quoting correctness over brevity.** Every file path and
   variable expansion must be correctly quoted for the target
   shell. A generated script that breaks on `C:\Program Files`
   or a filename with spaces is a bug in the emitter.

## Relationship to PS1 Wire-Out (Gap 1)

This design is a superset of the original PS1 wire-out plan.
The original plan was to wire DevConsole menu items to
VmCompile/VmPingpong/VmSweep. That remains Phase 4. Phases 1-3
deliver immediate value (cross-platform builds, single source of
truth for build logic) without requiring the full bare-metal
DevConsole path to be working.

## Open Questions

- **Plug or standalone emitter?** The shell emitters could be
  plugs (CDX binaries that consume IR over TCP, like all other
  plugs) or standalone Codex programs that import the Shell DSL
  and write files directly. Plugs are more consistent with the
  architecture; standalone is simpler for a first pass.

- **Makefile target?** Some CI systems prefer Make. A Makefile
  emitter could be added later as a fourth target.

- **Variable scoping.** The current DSL has no variables beyond
  params and captures. If the build logic needs intermediate
  values (e.g., parsed output from a compile step), should the
  DSL support let-bindings, or should that logic live in a
  Codex helper that runs in codex-vm and writes results to a
  file?
