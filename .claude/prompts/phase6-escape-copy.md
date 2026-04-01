Start with tools/codex-agent/codex-agent.exe orient and clean stale intermediates per
CLAUDE.md and sync master origin.

We are working on docs/CurrentPlan.md Phase 6: Escape Copy & Regions. Phases 1-5
are complete. The codegen is in Codex.Codex/Emit/X86_64.codex (~1,700 lines) and
runtime helpers are in Codex.Codex/Emit/X86_64Helpers.codex (~1,300 lines). Current
state: 566KB ELF, 113MB Stage 1 HWM on 128MB budget. Escape copy will reclaim heap
within function scopes and bring HWM down significantly.

Read docs/Compiler/SECOND-BOOTSTRAP.md Phase 6 section, the .handoff file, and
docs/SYNTAX-QUICKREF.md Pitfalls section.

This is the most intricate port in the backend. It is TYPE-DRIVEN CODE GENERATION:
the compiler generates unique escape-copy helpers for each record, list, and sum
type it encounters. This is qualitatively different from Phases 4-5 which ported
fixed assembly routines.

The C# reference is src/Codex.Emit.X86_64/X86_64CodeGen.cs. Key sections:

  EmitRegion           (lines 1305-1386)  — region entry/body/exit/escape-copy
  EmitEscapeCopy       (lines 1388-1401)  — type dispatch wrapper
  EmitEscapeCopyHelpers(lines 4177-4198)  — queue-draining type-specific generator
  EmitFwdTableZero     (lines 4213-4235)  — zero 512KB forwarding hash table
  EmitFwdTableLookup   (lines 4240-4286)  — linear-probe lookup, early return on hit
  EmitFwdTableInsert   (lines 4290-4328)  — linear-probe insert
  EmitEscapeHelperPrologue/Epilogue       — shared frame: null guard, fwd lookup, push/pop
  EmitRecordEscapeHelper(lines 4390-4405) — per-record: allocate + copy fields + recurse
  EmitListEscapeHelper  (lines 4407-4477) — per-list: allocate + copy elements + recurse
  EmitSumTypeEscapeHelper(lines 4479-4517)— per-sum: tag dispatch + per-ctor copy
  EmitEscapeFieldCopy                     — single field: shallow or deep (recursive call)

Architecture overview:

  Two-space heap:
    R10 (HeapReg) = working-space bump pointer. Intermediates go here.
    R15 (ResultReg) = result-space bump pointer. Escaped values go here.
    Working space starts at 4MB, result space at 125MB (bare metal).

  Region lifecycle:
    1. Entry: save HeapReg as mark (local variable)
    2. Body: evaluate expression (allocates in working space via R10)
    3. Fwd table: allocate + zero 512KB table in working space
    4. Escape: deep-copy body result from working space to result space
       - Skip if result is scalar or already in result space (ptr < result_base)
       - Forwarding table deduplicates shared substructure
    5. Exit: swap R15 back to R10, restore mark → reclaim working space

  Forwarding hash table:
    32,768 entries * 16 bytes = 512KB. Entry: [old_ptr:8 | new_ptr:8].
    Empty = old_ptr == 0. Hash: (ptr >> 3) & 0x7FFF. Linear probing.
    Table base stored in 8-byte rodata global (m_fwdTableGlobalOffset).

  Type-specific escape helpers:
    Each has signature: RDI = old pointer → RAX = new pointer (in result space).
    Frame: push RBX R12 R13 R14. RBX = old ptr, R12 = new ptr.
    Prologue: null guard → fwd table lookup (return cached if hit) → allocate.
    Body: copy fields/elements, recursively calling escape helpers for nested heap types.
    Epilogue: fwd table insert → return R12.

    Types: __escape_text (pre-emitted), __escape_record_<Name>,
           __escape_list_<ElemKey>, __escape_sum_<Name>.

    Type queue: GetOrQueueEscapeHelper discovers types lazily. Nested field types
    enqueue their own helpers. EmitEscapeCopyHelpers drains the queue after all
    user functions are emitted.

BLOCKER — Rodata fixup support:

  Escape copy is the FIRST feature requiring rodata fixups in the Codex codegen.
  The forwarding table base and result-space base are stored as 8-byte globals
  in the rodata section, loaded via mov-ri64 with a patch applied at link time.

  What needs to happen:
  1. Add a RodataFixup record type: { patch-offset : Integer, rodata-offset : Integer }
  2. Add rodata-fixups field to CodegenState (List RodataFixup)
  3. Add st-add-rodata-fixup helper that records a fixup
  4. Add emit-load-rodata-global : CodegenState -> Integer -> Integer -> EmitResult
     that emits mov-ri64 dst 0 and records a fixup at the immediate offset
  5. In x86-64-emit-module, apply rodata fixups alongside call-patches:
     for each fixup, patch 8 bytes at patch-offset with (rodata-vaddr + rodata-offset)
  6. Allocate rodata globals (fwd-table-base, result-space-base) at module init

  The rodata vaddr for bare metal is: load-addr + text-section-size (page-aligned).
  The ELF writer already handles this layout. The fixup just needs the final address.

Implementation plan:

  1. Add rodata fixup infrastructure to CodegenState + x86-64-emit-module
  2. Port the forwarding table (zero, lookup, insert) as inline emission helpers
  3. Port EmitRegion — the region entry/body/exit/escape-copy orchestrator
  4. Port the escape helper prologue/epilogue (shared frame + fwd table ops)
  5. Port EmitEscapeFieldCopy (shallow vs deep per-field dispatch)
  6. Port EmitRecordEscapeHelper, EmitListEscapeHelper, EmitSumTypeEscapeHelper
  7. Port the type queue system (GetOrQueueEscapeHelper, drain loop)
  8. Wire IRRegion into emit-expr dispatch
  9. Wire escape helper emission into x86-64-emit-module (after user funcs)
  10. Initialize result-space base in __start

  The forwarding table ops and escape helpers go in X86_64Helpers.codex (they don't
  call emit-expr, so no circular dependency). The region emission goes in X86_64.codex
  (it calls emit-expr for the body).

  The type queue is the trickiest part. In C# it's a mutable Queue that grows as
  types are discovered. In Codex it must be threaded through CodegenState. Add a
  field escape-queue (List of type descriptors) and escape-names (List of name-to-key
  mappings). The drain loop runs after emit-all-defs, emitting helpers and potentially
  discovering more types, until the queue is empty.

Register assignments:
  R10 = HeapReg (working space)
  R15 = ResultReg (result space) — currently unused in Codex codegen, available
  RBX = old pointer (in escape helpers, callee-saved)
  R12 = new pointer (in escape helpers, callee-saved)
  R13, R14 = scratch in escape helpers (callee-saved)
  RDI = escape helper input (old ptr)
  RAX = escape helper output (new ptr)

Run pingpong before pushing. Push to a feature branch for review.

Note: module namespaces handle cross-file names automatically.
Note: Codex has no || operator. Use if/then True/else chains.
Note: max ~18 let bindings per function. Split into sub-functions + records.
