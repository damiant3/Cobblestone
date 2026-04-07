Start with tools/codex-agent/codex-agent.exe orient and clean stale intermediates per
CLAUDE.md and sync master origin.

We are working on docs/CurrentPlan.md Phase 5: Builtins — wiring Codex operations to
the runtime helpers and inline instruction sequences. Phases 1-4 are complete. The
codegen is in Codex.Codex/Emit/X86_64.codex (~1,400 lines) and the runtime helpers
are in Codex.Codex/Emit/X86_64Helpers.codex (~1,300 lines). Phase 4 ported 16 of 22
runtime helpers; 5 I/O helpers are deferred to Phase 7 (the I/O boundary).

Read docs/Active/Compiler/SECOND-BOOTSTRAP.md Phase 5 section, the .handoff file, and
docs/SYNTAX-QUICKREF.md Pitfalls section.

CCE boundary principle: CCE is the internal encoding. Unicode is the outside world.
Conversion happens only at I/O boundaries (serial in/out, file read/write). Everything
inside the compiler — all builtins, all helpers, all string operations — operates on
CCE natively. No builtin in this phase needs CCE↔Unicode tables. The tables belong
in Phase 7 (boot sequence / I/O boundary), alongside the 5 deferred helpers.

The C# reference for builtins is src/Codex.Emit.X86_64/X86_64CodeGen.cs, in the
EmitBuiltin method starting around line 1420. Each builtin maps a Codex operation
name (e.g. "text-length", "list-at") to either:
  (a) An inline instruction sequence (e.g. text-length = load [ptr+0])
  (b) A call to a runtime helper (e.g. text-replace = call __str_replace)

Phase 5 builtins (~30, all pure CCE, no I/O boundary):

Inline (1-3 instructions each):
  text-length       — load [ptr+0]
  list-length       — load [list+0]
  list-at           — load [list+8+index*8]
  negate            — neg reg
  char-at           — movzx-byte from [ptr+8+index], wrap as 1-byte CCE string
  char-code-at      — movzx-byte (returns CCE byte value as integer)
  char-code         — load first byte of single-char CCE text
  code-to-char      — allocate 1-byte CCE string from integer
  char-to-text      — same as code-to-char
  is-letter         — CCE range check
  is-digit          — CCE range check (3..12)
  is-whitespace     — CCE value check
  get-args          — return empty list (bare metal has no args)
  current-dir       — return empty string (bare metal)
  substring         — allocate + copy byte range (small inline loop)

Helper-calling (move args to RDI/RSI/RDX, call, result in RAX):
  integer-to-text   — calls __itoa
  show              — calls __itoa
  text-to-integer   — calls __text_to_int
  text-replace      — calls __str_replace
  text-contains     — calls __text_contains
  text-starts-with  — calls __text_starts_with
  text-compare      — calls __text_compare
  text-concat-list  — calls __text_concat_list
  text-split        — calls __text_split
  list-cons         — calls __list_cons
  list-append       — calls __list_append
  list-snoc         — calls __list_snoc
  list-insert-at    — calls __list_insert_at
  list-contains     — calls __list_contains

Deferred to Phase 7 (I/O boundary — need CCE tables + rodata fixups + syscalls):
  print-line        — CCE→Unicode + serial output
  read-file         — calls __read_file (deferred helper)
  read-line         — calls __read_line (deferred helper)
  write-file        — CCE→Unicode + file syscalls
  file-exists       — file syscall
  fork / await      — bare metal no-op

The builtin emission goes in a new file: Codex.Codex/Emit/X86_64Builtins.codex.
Each builtin takes a CodegenState + arguments (register allocations for the args)
and returns an EmitResult with the result register.

The builtins need to be wired into emit-expr in X86_64.codex. Currently emit-expr
handles IrIntLit, IrName, IrLet, IrBinary, IrIf, IrApply, IrRecord, IrFieldAccess,
IrMatch, IrList, and IrLambda. It needs a new case for IrBuiltin (or the existing
IrApply case needs to check if the function name is a known builtin).

In the C# reference, builtins are dispatched inside EmitExpr when the function
name matches a known builtin string. The same pattern works in Codex: check the
function name against a list of known builtins, and if matched, emit the inline
sequence or helper call instead of a normal function call.

Suggested implementation order:
  1. Thin inline builtins first (text-length, list-length, list-at, negate) —
     proves the dispatch wiring works.
  2. Helper-calling builtins (text-replace, list-cons, etc.) — the largest batch,
     mostly mechanical: save caller-saved regs, move args, call, restore.
  3. Remaining inline builtins (char-at, substring, is-digit, etc.).

After Phase 5, Phase 6 (escape copy) shrinks the 109MB HWM. Then Phase 7 adds
the I/O boundary (CCE tables, print-line, read-file, serial, boot sequence).

Push to a feature branch for review when a batch of builtins is working.
Run pingpong before pushing.

Note: module namespaces are implemented — no cg- prefixes needed. The ModuleScoper
handles cross-file collisions automatically.
