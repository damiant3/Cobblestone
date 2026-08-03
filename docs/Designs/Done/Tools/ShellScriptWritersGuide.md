# Shell Script Writer's Guide

How to generate shell scripts from Codex using the Shell DSL.

## Quick Start

Write a Codex program that constructs a `ShellScript` value and
calls `emit-powershell` to produce PowerShell source text:

```codex
Chapter: MyScript
  cites Foreword chapter Console
  cites Shell chapter ShellTypes
  cites Shell chapter ShellBuild
  cites Shell chapter PowerShellEmit

  my-script : ShellScript = sh-script "my-tool" "Does something useful" [sh-param "Name" SptString] [ScSetStrictMode, ScSetErrorStop, ScEcho (SeVar "Name")]

  opening : [Console] Nothing = act
    print-line-uni (emit-powershell my-script)
  end
```

Compile it, run it, capture stdout:

```powershell
build/compile.ps1 -Src my-script.codex -Out my-script.cdx -Log my-script.log -Survey "lower-mul:500000"
build/test-run.ps1 -Kernel my-script.cdx -OutFile my-tool.ps1
```

The generated `my-tool.ps1` is a complete, runnable PowerShell script.

## The Survey Hint

**You must pass `-Survey "lower-mul:500000"` when compiling Shell DSL
programs.** The default LOWER phase deck budget is sized by definition
count. Shell DSL programs have few definitions but generate large IR
trees (deeply nested sum type constructors). Without the hint, the
compiler overflows the LOWER deck.

This is a known condition (see `docs/Test/Active/KNOWN-CONDITIONS.md`). The
self-host compiler (30K lines, 2000+ defs) compiles fine with the
default; Shell DSL programs need the override because their IR weight
per definition is much higher than normal code.

## Types

### ShellExpr -- Values and Conditions

| Constructor | Emits (PowerShell) |
|---|---|
| `SeLit "hello"` | `"hello"` |
| `SeInt 42` | `42` |
| `SeVar "x"` | `$x` |
| `SeEnvVar "PATH"` | `$env:PATH` |
| `SeFileExists e` | `(Test-Path -PathType Leaf <e>)` |
| `SeDirExists e` | `(Test-Path -PathType Container <e>)` |
| `SeStringEq a b` | `(<a> -eq <b>)` |
| `SeNot e` | `(-not <e>)` |
| `SeAnd a b` | `(<a> -and <b>)` |
| `SeOr a b` | `(<a> -or <b>)` |
| `SeProperty obj "Name"` | `<obj>.Name` |
| `SeMethodCall obj "Trim" []` | `<obj>.Trim()` |
| `SeStaticCall "System.IO.File" "Exists" [e]` | `[System.IO.File]::Exists(<e>)` |
| `SeMatch e "pattern"` | `(<e> -match "pattern")` |
| `SeStartsWith s prefix` | `<s>.StartsWith(<prefix>)` |
| `SeHashLiteral` | `@{}` |
| `SeArrayLiteral [a, b]` | `@(<a>, <b>)` |
| `SeRaw "text"` | `text` (verbatim escape hatch) |

### ShellCmd -- Statements

| Constructor | Emits (PowerShell) |
|---|---|
| `ScAssign "x" e` | `$x = <e>` |
| `ScEcho e` | `Write-Host <e>` |
| `ScWriteError e` | `[Console]::Error.WriteLine(<e>)` |
| `ScCopy src dst` | `Copy-Item -Force <src> <dst>` |
| `ScDelete path` | `Remove-Item -Force ... <path>` |
| `ScMkdir path` | `New-Item -ItemType Directory -Force <path>` |
| `ScIf cond then else` | `if (<cond>) { <then> } else { <else> }` |
| `ScWhile cond body` | `while (<cond>) { <body> }` |
| `ScForEach "x" items body` | `foreach ($x in <items>) { <body> }` |
| `ScForLoop "i" init cond step body` | `for ($i = <init>; <cond>; <step>) { ... }` |
| `ScLabeledWhile "lbl" cond body` | `:lbl while (<cond>) { <body> }` |
| `ScContinueLabel "lbl"` | `continue lbl` |
| `ScTry body catch finally` | `try { ... } catch { ... } finally { ... }` |
| `ScExit e` | `exit <e>` |
| `ScSetStrictMode` | `Set-StrictMode -Version Latest` |
| `ScSetErrorStop` | `$ErrorActionPreference = 'Stop'` |
| `ScDotNetCall cls method args` | `[<cls>]::<method>(<args>)` |
| `ScDotNetCallAssign var cls method args` | `$<var> = [<cls>]::<method>(<args>)` |
| `ScSetContent path value` | `Set-Content -Path <path> -Value <value> ...` |
| `ScAddContent path value` | `Add-Content -Path <path> -Value <value> ...` |
| `ScStartProcess spec` | `$var = Start-Process -FilePath ...` |
| `ScWaitProcess "p" 60000` | `$p.WaitForExit(60000)` |
| `ScHashFile "SHA256" "h" path` | `$h = (Get-FileHash -Algorithm SHA256 ...).Hash` |
| `ScSequence cmds` | emits commands inline (no wrapper) |
| `ScComment "text"` | `# text` |
| `ScRaw "text"` | `text` (verbatim escape hatch) |

## Convenience Builders (ShellBuild)

`sh-if`, `sh-assign`, `sh-echo`, `sh-exit`, `sh-foreach`, `sh-raw`,
`sh-try`, `sh-param`, `sh-switch`, `sh-script`, etc. These wrap the
constructors with shorter names and sensible defaults.

## Structuring Scripts

**Keep each definition under 15 ShellCmd constructors.** The compiler
sizes the LOWER deck by definition count. A single definition with
50 nested constructors produces more IR than the deck budget allows
(even with the survey hint). Split large scripts into named sections:

```codex
  s01-preamble : List ShellCmd = [ScSetStrictMode, ScSetErrorStop, ...]
  s02-validate : List ShellCmd = [ScIf (...) [...] [], ...]
  s03-compile  : List ShellCmd = [ScRaw "...", ...]

  script-body : List ShellCmd = [ScSequence s01-preamble, ScBlank, ScSequence s02-validate, ScBlank, ScSequence s03-compile]
```

Use `ScSequence` to compose sections -- it emits the inner commands
inline without any wrapper syntax. Do NOT use `&` (list append) to
concatenate sections -- it creates nested `list-append` IR trees that
blow up the LOWER phase.

## The ScRaw Escape Hatch

For complex .NET interop, regex, or PowerShell-specific syntax that
doesn't warrant a dedicated constructor, use `ScRaw`:

```codex
  ScRaw "$cdxBytes = [System.IO.File]::ReadAllBytes($Stage0Copy)"
```

This emits the text verbatim. Use typed constructors where possible
(they produce cleaner, more portable output) and ScRaw for the rest.

## Adding a Bash Emitter (Future)

The types are shell-agnostic. A bash emitter would pattern-match the
same `ShellCmd`/`ShellExpr` types and produce bash syntax. The design
doc is at `docs/Designs/Tools/Active/ShellScriptEmitter.md`.

## Reference

- `codex/foreword/shell/ShellTypes.codex` -- type definitions
- `codex/foreword/shell/ShellBuild.codex` -- convenience constructors
- `codex/foreword/shell/PowerShellEmit.codex` -- PowerShell emitter
- `codex/build/CompileScript.codex` -- proof-of-concept (generates compile.ps1)
- `docs/Designs/Tools/Active/ShellScriptEmitter.md` -- architecture design
- `docs/Test/Active/KNOWN-CONDITIONS.md` -- LOWER deck survey condition
