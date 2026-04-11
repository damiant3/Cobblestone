# Binary Codegen Audit — X86_64.codex vs X86_64CodeGen.cs

**Date:** 2026-04-09
**Author:** Agent Linux
**Status:** Blocking binary pingpong (MM4)

Line-by-line comparison of the self-hosted Codex x86-64 emitter against
the C# reference compiler. These are features present in the reference
that are missing from the Codex port.

---

## CRITICAL — Missing emit-expr cases

### 1. IrTextLit (string literals)

**Reference:** `EmitTextLit` — CCE-encodes the string, stores in rodata as
length-prefixed bytes, loads the rodata address into a