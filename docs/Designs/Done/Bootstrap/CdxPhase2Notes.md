# CDX Phase 2 Implementation Notes

**Date**: 2026-03-31
**Status**: Implementation
**Context**: Second Bootstrap Phase 2 — ELF Writer + CDX Writer in Codex

---

## Development-Mode Decisions

The CDX1 format (specified in `CodexBinary.md`) requires SHA-256 and Ed25519
for integrity and signing. These crypto primitives are post-MM4 dependencies
(`docs/Designs/Codex.OS/CryptoPrimitives.md`). During MM4 development, the CDX writer
zero-fills the crypto fields:

| Field | Offset | Size | Development value | Production value |
|-------|--------|------|-------------------|-----------------|
| `content_hash` | 0x08 | 32B | All zeros | SHA-256 of capabilities through EOF |
| `author_key` | 0x28 | 32B | All zeros | Ed25519 public key |
| `signature` | 0x48 | 64B | All zeros | Ed25519 signature over content_hash |
| `trust_threshold` | 0xD8 | 2B | 0 | Minimum trust score for loading |
| `fact_hash_count` | 0xDC | 4B | 0 | Content-addressed dependency count |

The structural layout (offsets, sizes, section ordering) is production-final.
Only the crypto field values change when primitives become available.

## CDX + ELF Coexistence During MM4

QEMU loads ELF, not CDX. During development:

1. **Codegen** produces text + rodata byte lists
2. **ELF writer** wraps them into a 32-bit ELF (PVH) for QEMU boot
3. **CDX writer** can produce a CDX binary from the same byte lists (parallel)
4. **Pingpong** uses the ELF path (QEMU requirement)

After MM4, when Codex.OS has its own loader:
- CDX becomes the primary output format
- ELF is used only as a QEMU boot shim during development
- The CDX loader verifies content_hash, signature, and capabilities before loading

## Flag Semantics During Development

| Flag | Value | When set |
|------|-------|----------|
| `BARE_METAL` | 1 | Self-hosted compiler targeting bare metal |
| `NEEDS_HEAP` | 2 | Any program using heap allocation (most programs) |
| `NEEDS_STACK_GUARD` | 4 | Not set until stack guard is implemented |
| `HAS_PROOFS` | 8 | Not set until proof system exists |

Typical development binary: flags = 3 (`BARE_METAL | NEEDS_HEAP`).

## What Changes When Crypto Arrives

1. **Writer gains `sign-cdx` function**: takes private key, computes SHA-256
   of payload (capabilities_offset through EOF), signs with Ed25519
2. **`content_hash` filled**: SHA-256 replaces zeros
3. **`author_key` filled**: public key from key pair
4. **`signature` filled**: Ed25519 signature
5. **`trust_threshold`**: set by author based on intended trust level
6. **Loader verifies**: steps 2-5 of the loading sequence become live

The writer API may gain an optional key parameter:
```codex
build-cdx-signed : CryptoKey -> Integer -> ... -> List Integer
```

No structural format changes. The CDX1 format version remains 1.
