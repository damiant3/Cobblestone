# Codex FileShare

A bare-metal peer-to-peer file sharing app modeled after BitTorrent but content-addressed (SHA-256 Merkle root as the file's identity), with Kademlia DHT discovery, Ed25519-signed announce records, and trust-weighted peer selection.

## Modules

- **ContentChunker** -- Splits files into 256 KB pieces, SHA-256 hashes each, builds a Merkle tree, produces a ContentManifest. Piece and root verification. JSON serialization.
- **PeerSwarm** -- Full peer lifecycle state machine (Connecting/Handshaking/Active/Choked/Disconnected). Per-peer piece bitmap, upload/download counters, trust score, choke/unchoke.
- **PieceManager** -- Piece-level state machine (Missing/Requested/Downloaded/Complete/Verified). Rarest-first selection. Progress tracking and retry counter.
- **TransferProtocol** -- 11-message wire protocol over TCP: HANDSHAKE (content-hash + peer-id + Ed25519 signature), BITFIELD, HAVE, REQUEST, PIECE, CANCEL, CHOKE, UNCHOKE, INTERESTED, NOT_INTERESTED, KEEPALIVE. Complete encode/decode.
- **PeerDiscovery** -- Kademlia-style DHT: XOR distance, 256-bucket routing table (k=20), Ed25519-signed announce records, TTL-based freshness
- **FileShareApp** -- Transfer management (add download/seed, pause/resume/remove), piece reception with hash verification, choke-round algorithm (top-4 + 1 optimistic unchoke), tick-based scheduling, manifest import/export
- **FileSharePersist** -- DiskFacts serialization (kind 25): transfers + embedded JSON manifests, full round-trip
- **opening** -- Disk load, transfer restore, persistent event loop

## Completeness

60% -- All data structures, algorithms, wire protocol, and persistence are fully implemented. Key gaps: no actual TCP network I/O (protocol exists but nothing calls the network stack); dht-find-closest returns nodes in bucket order, not XOR proximity order; trust-weighted DHT preference described but not implemented; ViewAddContent and ViewPeers/ViewDht views have no render functions; no NAT traversal.

## Codex Conformance

Full -- Written entirely in Codex. All crypto from Foreword. Network backend (TCP) would be emitted via the network plug.
