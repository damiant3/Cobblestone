# CDX Plugin System -- Loadable Modules

**Status**: Design sketch
**Date**: 2026-05-01
**Depends on**: CodexBinary.md (CDX format), CAPABILITY-REFINEMENT.md (cap enforcement)

---

## Motivation

The CDX binary format is currently standalone-only. Every program is
self-contained. But the compiler (and eventually the OS) needs
extensibility: forewords pre-compiled to native code, user-defined
optimization passes, lint rules, custom backends -- all loadable at
runtime with the same trust guarantees as standalone binaries.

Traditional dynamic linking (GOT, PLT, ld.so) is explicitly rejected.
It relies on symbol names, lacks capability gating, and has no integrity
verification. What we want is **trust-verified module loading** -- a CDX
binary that declares exports, imports capabilities from its host, and
runs within the host's capability budget.

---

## Design

### CDX Format Extension (v2)

New header flag: `CDX_FLAG_PLUGIN = 16`. When set, the binary includes
two additional sections:

1. **Export table** -- list of named entry points with type signatures.
   Each export: name (CCE string), offset into text section, type hash.
   The host looks up exports by name after verification passes.

2. **Import table** -- list of host-provided functions the plugin expects.
   Each import: name, expected type hash. The host provides a vtable
   pointer at load time. The plugin calls imports through this vtable.

### Loading Sequence (extends CodexBinary.md §Loading Sequence)

1. Magic check
2. Content hash verification (SHA-256)
3. Author signature verification (Ed25519, once implemented)
4. Trust threshold: `author_trust_score >= binary.trust_threshold`
5. Capability subset check: `plugin.capabilities ⊆ host.granted_capabilities`
6. Map text section at a host-chosen base address
7. Resolve imports: host provides vtable, plugin stores base pointer
8. Return export table to host

### Capability Delegation

The host grants the plugin a **subset** of its own capabilities. The
plugin cannot escalate. Time-boxing applies: `with-timeout` scopes in the
host propagate to plugin invocations. If the host's capability expires,
the plugin's syscalls are denied.

### Compiler Integration

Concrete use case: forewords as CDX plugins.

Today: `sample-compile-selfhost.ps1` concatenates foreword `.codex`
source with the sample, compiles everything together. Each sample
recompiles the same foreword.

Tomorrow: `codex build --target cdx foreword/Sha256.codex` produces
`Sha256.cdx`. The compiler loads Sha256.cdx at compile time, imports
its type signatures, and emits calls to the pre-compiled code. The
sample binary links against the plugin's text section (static embedding)
or loads it at runtime (dynamic plugin).

### Implementation Path

| Step | What | Effort |
|------|------|--------|
| 1 | `--target cdx` standalone emitter | Small -- wrap existing x86-64 output in CDX header using `cdx-encode` |
| 2 | Export table in CDX header | Small -- extend `cdx-encode` with export entries |
| 3 | CDX loader (bare-metal) | Medium -- map sections, verify, return exports |
| 4 | Import table + vtable resolution | Medium -- plugin-side trampoline stubs |
| 5 | Compiler foreword pre-compilation | Medium -- type signature extraction + link-time embedding |
| 6 | Runtime plugin loading (OS-level) | Large -- capability delegation, process isolation |

Step 1 is immediate. Steps 2-4 are the plugin MVP. Steps 5-6 are the
full vision.

---

## Relationship to Existing Design

**Not dynamic linking.** No symbol resolution by name at load time. No
shared libraries. No versioning hell. The plugin is a self-contained
binary that happens to expose named entry points. The host verifies
integrity and trust before any code runs.

**Aligned with trust lattice.** The plugin's author is a node in the
trust lattice. The host's trust threshold gates loading. A plugin from
an untrusted author is rejected before its code is mapped.

**Aligned with capability model.** The plugin declares needed
capabilities. The host checks that the plugin's needs are a subset of
the host's grants. A plugin that declares `[Network]` can't be loaded
by a host that only has `[Console]`.

---

## Open Questions

1. **Static embedding vs. runtime loading?** Step 5 (compiler
   forewords) could embed the plugin's text section into the final
   binary (like static linking) rather than loading at runtime. Simpler,
   no runtime loader needed, but loses hot-swappability.

2. **Plugin isolation?** Should plugins run in the same address space as
   the host (faster, shared heap) or in a separate memory region
   (safer, fault isolation)? The capability model provides logical
   isolation; physical isolation is an OS-level decision.

3. **Versioning?** Plugins are identified by content hash. A new version
   is a new hash. The host either pins a specific hash or accepts
   any version above a trust threshold. No semver, no compatibility
   matrix -- just trust.
