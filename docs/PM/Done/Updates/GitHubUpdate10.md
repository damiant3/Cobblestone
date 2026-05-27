# GitHub Update 10 -- 2026-05-27

## Build Infrastructure

- **Serial removal complete.** TCP sockets eliminated from the entire build pipeline. VM loads input via file into guest ring buffer; output captured from UART writes. 95+ plug scripts updated.
- **REPL batch compilation.** One VM per batch slot instead of per-test. Compile phase ~140s to ~30s.
- **VM port forwarding.** New `-portfwd host:guest` flag for host-to-guest TCP.
- **Compile retry.** compile.ps1 auto-retries with 4 GB on crash.

## Compiler

- **Configurable surveys.** Phase deck multipliers in BuildSettings.codex. Deck overflow is a warning.
- **Escape invariant (CDX9003).** Seal-time scan for deck-to-bivy pointers. PARSE/SCOPE compact disabled (127K violations identified).
- **env-bind dedup.** Fixes builtin shadowing and Tcp type errors.
- **Bootstrap seed rebuild.** Fixed REPL codegen regression from lazy eval.

## Language

- **Lazy evaluation.** `lazy` keyword with memoization. `force` builtin.
- **Type classes (phase 1-2).** `class`/`instance` keywords, dictionary-passing desugaring, specialized methods.

## Applications

- **Explorer app.** 17-module parameter explorer with web UI.

## Stats

- Seed: 2,267,679 bytes (SHA256 011DB01D...)
- 119/119 pass, 0 fail
- 48 transpiler plugs, 235 foreword modules