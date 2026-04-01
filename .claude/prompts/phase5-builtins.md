Start with tools/codex-agent/codex-agent.exe orient and clean stale intermediates per
CLAUDE.md and sync master origin.

We are working on docs/CurrentPlan.md Phase 5: Builtins — wiring Codex operations to
the runtime helpers and inline instruction sequences. Phases 1-4 are complete. The
codegen is in Codex.Codex/Emit/X86_64.codex (~1,400 lines) and the runtime helpers
are in Codex.Codex/Emit/X86_64Helpers.codex (~1,300 lines). Phase 4 ported 16 of 22
runtime helpers; 5 are deferred (I/O + CCE tables, need rodata fixup support).

Read docs/Compiler/SECOND-BOOTSTRAP.md Phase 5 section, the .handoff file, and
docs/SYNTAX-QUICKREF.md Pitfalls section.

The C# reference for builtins is src/Codex.Emit.X86_64/X86_64CodeGen.cs, in the
EmitBuiltin method starting around line 1420. Each builtin maps a Codex operation
name (e.g. "text-length", "list-at") to either:
  (a) An inline instruction sequence (e.g. text-length = load [ptr+0])
  (b) A call to a runtime helper (e.g. text-replace = call __str_replace)

The 36 builtins in the C# reference (by category):

I/O (need rodata for CCE tables — may need to defer some):
  print-line        — CCE→Unicode conversion + serial/write output
  read-file         — calls __read_file (deferred helper)
  read-line         — calls __read_line (deferred helper)
  write-file        — Linux syscall (open, write, close)
  file-exists       — Linux syscall (access)
  get-args          — returns empty list (bare metal has no args)
  current-dir       — returns empty string (bare metal)

String operations (mostly thin wrappers around Phase 4 helpers):
  text-length       — inline: load [ptr+0]
  integer-to-text   — calls __itoa
  show              — calls __itoa (same as integer-to-text)
  text-to-integer   — calls __text_to_int
  text-replace      — calls __str_replace
  text-contains     — calls __text_contains
  text-starts-with  — calls __text_starts_with
  text-compare      — calls __text_compare
  text-concat-list  — calls __text_concat_list
  text-split        — calls __text_split
  char-at           — inline: movzx-byte from [ptr+8+index]
  substring         — inline: allocate + copy range
  char-code-at      — inline: movzx-byte (returns CCE byte value)
  char-code         — inline: similar to char-at for single-char text
  code-to-char      — inline: allocate 1-byte string with given CCE code
  char-to-text      — inline: same as code-to-char
  is-letter         — inline: CCE range check
  is-digit          — inline: CCE range check (3..12)
  is-whitespace     — inline: CCE value check

List operations (mostly thin wrappers around Phase 4 helpers):
  list-cons         — calls __list_cons
  list-append       — calls __list_append
  list-at           — inline: load [list+8+index*8]
  list-length       — inline: load [list+0]
  list-snoc         — calls __list_snoc
  list-insert-at    — calls __list_insert_at
  list-contains     — calls __list_contains

Math:
  negate            — inline: neg rax
  ipow (if needed)  — calls __ipow (may be handled via binary-op)

Special:
  fork / await      — bare metal: no-op (single-threaded)

Critical path: The builtins are the bridge between user Codex code (which calls
operations like text-length, list-at) and the runtime helpers. Without builtins,
none of the Phase 4 helpers can be exercised by actual Codex programs.

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

The rodata fixup question: Several builtins (print-line, char-at for Unicode
output, read-file, write-file) need CCE↔Unicode conversion tables stored in
rodata. The Codex codegen currently has no rodata fixup mechanism. Two approaches:
  (a) Add rodata fixup support to CodegenState (a list of fixup records, similar
      to call-patches and func-addr-fixups). Fixups are applied during the patch
      phase in x86-64-emit-module.
  (b) Defer all builtins that need CCE tables to Phase 7 (boot), and do the
      non-CCE builtins now.

Approach (a) is preferred because print-line is needed for any useful output
beyond the current __start integer print. But approach (b) is acceptable if (a)
proves too complex — the remaining helpers still work through the inline itoa
path in __start.

Dependencies on escape copy (Phase 6): The builtins themselves don't depend on
escape copy, but any program that allocates strings/lists inside called functions
will leak heap without it. The current 128MB budget works for compilation but
heap pressure grows with each helper call. Phase 6 is what brings HWM down.

Suggested implementation order:
  1. Thin wrapper builtins first (text-length, list-length, list-at, negate) —
     these are 1-3 instructions each, no helper calls needed.
  2. Helper-calling builtins (text-replace, list-cons, etc.) — move args to
     RDI/RSI/RDX, call helper, result in RAX.
  3. Inline builtins (char-at, substring, char-code-at, is-letter, is-digit,
     is-whitespace) — small inline sequences.
  4. print-line — needs CCE→Unicode + serial output, requires rodata tables.
  5. I/O builtins — after rodata fixup support is in place.

Push to a feature branch for review when a batch of builtins is working.
Run pingpong before pushing.

Note: module namespaces are implemented — no cg- prefixes needed. The ModuleScoper
handles cross-file collisions automatically.

Note: The 5 deferred runtime helpers (__read_file, __read_line,
__bare_metal_read_serial, __cce_to_unicode, __unicode_to_cce) should be added to
X86_64Helpers.codex once rodata fixup support is available. They can land alongside
the I/O builtins.
