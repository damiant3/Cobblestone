Start with tools/codex-agent/codex-agent.exe orient and clean stale intermediates per
CLAUDE.md and sync master origin.

We are working on docs/CurrentPlan.md Phase 4: Runtime Helpers — the hands and voice
of the Second Bootstrap. Phases 1 (encoder), 2 (ELF/CDX), and 3 (core codegen, all 9
milestones) are complete. The codegen is in Codex.Codex/Emit/X86_64.codex (~1,400 lines).
Phase 3 covers: int, let, if, calls, records, match, lists, TCO, closures.

Read docs/Active/Compiler/SECOND-BOOTSTRAP.md Phase 4 section for the 22 runtime helpers needed.
Read the .handoff file for key learnings. Read docs/SYNTAX-QUICKREF.md Pitfalls section.

The C# reference is src/Codex.Emit.X86_64/X86_64CodeGen.cs. Runtime helpers are in
EmitRuntimeHelpers() starting at line 2437, spanning to ~line 4100. Each helper is a
hand-assembled function registered in m_functionOffsets and emitted as raw bytes into
the text section. In Codex these become functions returning List Integer, appended via
st-append-text.

The 22 helpers by priority (self-compile dependencies first):

Critical path (needed for self-compilation):
  __str_concat    — rdi=ptr1, rsi=ptr2 → rax=concatenated string (line 2467)
  __str_eq        — rdi=ptr1, rsi=ptr2 → rax=1/0 (line 2578)
  __itoa          — rdi=integer → rax=ptr to length-prefixed string (line 2622)
  __list_snoc     — rdi=list, rsi=elem → rax=new list (line 3052)
  __list_cons     — rdi=elem, rsi=list → rax=new list (line 4030)
  __list_append   — rdi=list1, rsi=list2 → rax=concatenated list (line 4072)
  __ipow          — rdi=base, rsi=exp → rax=result (search for __ipow)
  __text_to_int   — rdi=text_ptr → rax=integer value

String operations:
  __str_replace        — rdi=haystack, rsi=needle, rdx=replacement → rax
  __text_contains      — rdi=haystack, rsi=needle → rax=1/0
  __text_starts_with   — rdi=text, rsi=prefix → rax=1/0
  __text_compare       — rdi=a, rsi=b → rax=ordering
  __text_concat_list   — rdi=list of strings → rax=concatenated
  __text_split         — rdi=text, rsi=separator → rax=list of strings

List operations:
  __list_insert_at     — rdi=list, rsi=index, rdx=elem → rax=new list
  __list_contains      — rdi=list, rsi=elem → rax=1/0

I/O:
  __read_file          — rdi=path_ptr → rax=file content string
  __read_line          — → rax=line from serial
  __bare_metal_read_serial — low-level serial byte read

CCE encoding (needed for text I/O boundary):
  __cce_to_unicode     — rdi=cce_byte → rax=unicode codepoint (table lookup)
  __unicode_to_cce     — rdi=unicode → rax=cce_byte (table lookup)

Memory layout (already established in Phase 3):
  R10 = heap bump pointer (initialized to 4MB in __start)
  Strings: [length:8][data:N] (length-prefixed, CCE-encoded bytes)
  Lists: [capacity:8][length:8][elem0:8][elem1:8]...

Start with __str_concat, __str_eq, and __itoa — these are the most exercised helpers
in the self-hosted compiler. Write QEMU boot tests for each. The inline itoa in __start
can serve as a reference for the __itoa helper (but the helper allocates a string on
the heap rather than printing directly).

Create a new file Codex.Codex/Emit/X86_64Helpers.codex for the helpers. Each helper
is a function that takes a CodegenState and returns a CodegenState with the helper's
bytes appended to st.text and its offset recorded in st.func-offsets. Wire them into
x86-64-emit-module before emit-all-defs (helpers must be emitted before user code so
call patches resolve correctly).

Push to a feature branch for review when a batch of helpers is working. Run pingpong
before pushing.

Note: module namespaces are implemented — no cg- prefixes needed. The ModuleScoper
handles cross-file collisions automatically.
