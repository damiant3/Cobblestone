# Hat Tip: Mutable Linear State

Threaded-state records that update one field at a time used to allocate
a fresh record per update, creating a chain of dead objects on the heap.
The `record-set` builtin compiles to a single `mov [rec+off], val` on
bare metal and returns the same pointer -- no allocation, no copy.

## What landed

| Record | File | CL | Functions converted |
|---|---|---|---|
| CodegenState | Emit/X86_64*.codex | pre-existing | all state-threading sites |
| UnificationState | Types/Unifier.codex | CL 11 | advance-id, add-subst, add-unify-error |
| LexState | Syntax/Lexer.codex | CL 13 | skip-spaces, scan-ident-rest, scan-digits, scan-string-body |
| ParseState | Syntax/ParserCore.codex | CL 17 | enter-paren, exit-paren, expect (error branch), report-reserved-keyword, skip-newlines |
| TypeEnv | Types/TypeEnv.codex | CL 18 | env-bind |

## What stayed copy-on-update

`advance-char` (Lexer) and `advance` (Parser). Their callers use
patterns like `make-token … s` next to `advance st` in the same
expression -- in-place mutation would change the captured position
read. Evaluation-order-dependent safety is not worth it.

## Heap HWM trajectory (bare-metal pingpong, stage 1 / stage 2)

| Point | Stage 1 | Stage 2 |
|---|---|---|
| Pre-HatTip | ~197 MB | ~148 MB |
| +Lexer | 191 MB | 142 MB |
| +ParseState | 190 MB | 141 MB |
| +TypeEnv | 150 MB | 101 MB |

TypeEnv was the sleeper: `env-bind` fires per builtin, per def header,
per lambda param, per let binding, and per pattern bind during
typecheck -- thousands of calls, each previously allocating a fresh
TypeEnv wrapper around a rebuilt bindings list. Eliminating the
wrapper compounded to −40 MB per stage.
