# Codex FileShare

Content-addressed file sharing with Merkle-verified pieces, peer
swarms, Kademlia DHT discovery, and trust-weighted peer selection.

## Architecture

Files are split into 256 KB pieces, each SHA-256 hashed. A Merkle
tree over the piece hashes produces the root hash — the content
address. Peers exchange pieces over TCP using a BitTorrent-inspired
wire protocol. Discovery uses a Kademlia-style DHT with Ed25519-
signed announce records, verified on receipt.

```
  File → chunk (256 KB pieces) → Merkle tree → root hash
                                                   │
  DHT announces root hash + peer address ──────────┘
                                                   │
  Seekers query DHT → find peers → join swarm ─────┘
                                                   │
  Swarm exchanges pieces (rarest-first) ───────────┘
                                                   │
  Each piece verified against Merkle proof ────────┘
```

## Modules (7 + app, ~2400 lines)

| Module | Lines | Purpose |
|--------|-------|---------|
| ContentChunker | ~200 | 256KB piece splitting, Merkle tree (build/verify), content manifest |
| PeerSwarm | ~230 | Peer lifecycle (connect/active/choked/disconnected), trust scoring, piece bitmap, best-peer selection |
| PieceManager | ~220 | Piece status tracking (missing/requested/downloading/verified), rarest-first selection, progress bitmap |
| TransferProtocol | ~240 | Wire protocol: handshake, bitfield, have, request, piece, cancel, choke/unchoke/interest. Bit packing for bitmaps |
| PeerDiscovery | ~250 | Kademlia DHT: XOR distance, bucket routing, announce records (Ed25519 signed), lookup, TTL expiry |
| FileShareApp | ~250 | Transfer management (add/pause/resume/remove), piece reception with verification, UI rendering |
| opening | 15 | Entry point |

## Key Differences from BitTorrent

| BitTorrent | Codex FileShare |
|-----------|----------------|
| .torrent file with tracker URL | ContentManifest with root hash — no file needed |
| Centralized trackers | Kademlia DHT with Ed25519 signed announces |
| Anonymous peers | Identity-based (Ed25519 keys), trust-weighted selection |
| SHA-1 piece hashes | SHA-256 Merkle tree with full integrity proof |
| No content addressing | Root hash IS the content address |
| Tit-for-tat only | Trust score (0-100) affects peer selection |

## Wire Protocol

11 message types over TCP:
HANDSHAKE (content-hash + peer-id + signature),
BITFIELD, HAVE, REQUEST, PIECE, CANCEL,
CHOKE, UNCHOKE, INTERESTED, NOT_INTERESTED, KEEPALIVE.

All messages: 4-byte LE length + 1-byte type + payload.
Bitfields packed 1-bit-per-piece, big-endian bit order.
