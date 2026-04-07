# Binary Emitter Gaps

**Last updated**: 2026-03-29 (verified against commit history)

Compiler-critical builtins for self-hosting on each native backend:

| Builtin | x86-64 | RISC-V | ARM64 | Notes |
|---------|--------|--------|-------|-------|
| TCO | Done | Done | Missing | Crash without it |
| is-digit | Done | Done | Missing | Lexer needs it (CCE ranges fixed 2026-03-29) |
| is-whitespace | Done | Done | Missing | Lexer needs it (CCE ranges fixed 2026-03-29) |
| is-letter | Done | Done | Missing | (CCE ranges fixed 2026-03-29) |
| negate | Done | Done | Missing | Arithmetic |
| text-contains | Done | Done | Missing | Name resolver |
| text-starts-with | Done | Done | Missing | Parser |
| list-cons | Done | Done | Missing | List building |
| list-append | Done | Done | Missing | Everywhere |
| Effect handlers | Missing | Missing | Missing | Not needed for compiler |

x86-64 self-hosting: **proven** (MM3 fixed point, 64MB bare metal).
RISC-V: at parity for builtins, bare-metal self-compile has a pre-existing crash under investigation.
ARM64: needs the same 8 compiler-critical builtins before self-hosting is possible.

See `docs/BACKLOG.md` for the full outstanding work list.
