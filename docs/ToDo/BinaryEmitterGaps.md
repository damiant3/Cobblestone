  Compiler-critical builtins (for self-hosting):

  ┌──────────────────┬─────────┬─────────┬─────────┬─────────────────────────┐
  │     Builtin      │ x86-64  │ RISC-V  │  ARM64  │          Notes          │
  ├──────────────────┼─────────┼─────────┼─────────┼─────────────────────────┤
  │ TCO              │ Done    │ Missing │ Missing │ Crash without it        │
  ├──────────────────┼─────────┼─────────┼─────────┼─────────────────────────┤
  │ is-digit         │ Done    │ Missing │ Missing │ Lexer needs it          │
  ├──────────────────┼─────────┼─────────┼─────────┼─────────────────────────┤
  │ is-whitespace    │ Done    │ Missing │ Missing │ Lexer needs it          │
  ├──────────────────┼─────────┼─────────┼─────────┼─────────────────────────┤
  │ negate           │ Done    │ Missing │ Missing │ Arithmetic              │
  ├──────────────────┼─────────┼─────────┼─────────┼─────────────────────────┤
  │ text-contains    │ Done    │ Missing │ Missing │ Name resolver           │
  ├──────────────────┼─────────┼─────────┼─────────┼─────────────────────────┤
  │ text-starts-with │ Done    │ Missing │ Missing │ Parser                  │
  ├──────────────────┼─────────┼─────────┼─────────┼─────────────────────────┤
  │ list-cons        │ Done    │ Missing │ Missing │ List building           │
  ├──────────────────┼─────────┼─────────┼─────────┼─────────────────────────┤
  │ list-append      │ Done    │ Missing │ Missing │ Everywhere              │
  ├──────────────────┼─────────┼─────────┼─────────┼─────────────────────────┤
  │ Effect handlers  │ Missing │ Missing │ Missing │ Not needed for compiler │
  └──────────────────┴─────────┴─────────┴─────────┴─────────────────────────┘

  x86-64 is now at 29/44 builtins — the compiler-critical ones are covered. ARM64 and RISC-V need the same treatment
  when we want to self-host on those ISAs.

  The bare metal compiler still hangs — might be QEMU TCG speed, or there are more missing builtins that the type
  checker or emitter needs. Agent Linux tracing with -d in_asm will pinpoint the exact instruction it spins on.