# Edge Mesh Game Servers -- Auto-Spawning Distributed Game Infrastructure

## Status

**Ruling 2026-08-05 (Damian): RESURFACED, chained to blu's network track.** Phase 2 begins when B2-B4 (link bring-up, TCP/IP, the repository protocol) give the mesh a real socket surface; until then this design waits on that lane rather than on an owner of its own.

**The socket surface B4 owed this design exists in the bed, 2026-08-17 (root,
CurrentPlan B4 step 5).** Phase 2 need not wait on B4 any longer, only on its
own owner. What it can start against today, all in `codex/os/net` and
`codex/os/trust` and all driven by host harnesses under codex-vm: a listening
TCP transport (`net-io-listen`, `net-io-accept`, `net-io-send`,
`net-io-recv-loop` in `NetIO.codex`; `TcpTransport.codex`), tagged frames
(`MessageFraming.codex`), the trust-side message codec
(`TrustTransport.codex`, `AgentProtocol.codex`), and two serving programs to
copy the shape of, `tools/cdx-serve.codex` (9300) and `tools/cdx-registry.codex`
(9301), reachable through `codex-vm -portfwd`. The wire itself is written down
in `DevelopersRulebook.md` "The repository wire". The same conversation over
the Intel model is `build/cdx-serve-test.ps1 -Card e1000`; on the part it is
B3's flight. GroupMembership, EdgeRouter and TrustNode remain this design's
own wiring, as the phasing below says.
**Phase 1 is shipped -- as a self-contained simulation.**
`codex/foreword/engine/EdgeMesh.codex` implements the queue, Elo
pairing, aggregate-latency region selection, fleet lifecycle, and reward
computation, with test coverage.

**It is not wired to any of the infrastructure this design names.** The
chapter cites exactly four things: `Engine/HelmBridge`,
`Engine/GameplayTags`, `Game/Netcode`, and `Foreword/Maybe`. It cites
**nothing from `codex/os/net` and nothing from `codex/os/trust`**. There
is no SWIM discovery, no real traffic routing, no authenticated session,
and no on-chain reward posting -- the regions, the servers, and the mesh
are all models inside the chapter. What Phase 1 proves is that the
*orchestration logic* is right, which is worth proving on its own. It is
not a running edge mesh.

**Next: Phase 2 -- wire it to the real infrastructure.** GroupMembership
(SWIM), EdgeRouter, and TrustNode all exist and are the three
connections that turn the simulation into a system.

The design below describes the intended end state (GroupMembership,
MeshRoles, EdgeRouter, RaftConsensus, GossipProtocol, TrustNode,
TrustLattice, PolicyEngine, ChainCore, MintAuthority, HelmBridge,
Scene3D, Renderer3D, Physics, Netcode). Read the "Existing
Infrastructure Used" table as *available and intended*, not as
*currently called*.

---

## Motivation

In 1996, a company called RTime Inc. (Resonant Reality) tried to build
real-time game server infrastructure with Microsoft DirectPlay. They
wanted "LAN and head-to-head services" that auto-connected players.
The technology didn't exist yet -- broadband was rare, cloud computing
was a decade away, and edge computing was two decades away. RTime
faded out.

The concept they were reaching for -- auto-spawning dedicated game
servers near players on demand -- became real in the 2010s with
Multiplay, Edgegap, Amazon GameLift, i3D.net, and Google Agones. But
all of those are services built on other people's infrastructure:
Linux, Kubernetes, AWS, Vulkan, etc.

Codex can do it from scratch. We own the entire stack from the bare
metal hypervisor to the game engine to the blockchain. A Codex game
server is a single CDX binary, bootable via codex-vm, auto-discovered
through SWIM gossip, trust-authenticated through the lattice, and
economically integrated through ManaCoin. No containers, no
orchestrators, no cloud providers.

---

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│  Player Client                                                  │
│  (Codex CDX, bare metal or WASM)                               │
├─────────────────────────────────────────────────────────────────┤
│                          ↕ Trust Handshake                      │
├─────────────────────────────────────────────────────────────────┤
│  Edge Mesh                                                      │
│  ┌──────────┬──────────┬──────────┬──────────┬───────────┐      │
│  │ Match    │ Edge     │ Game     │ Helm     │ ManaCoin  │      │
│  │ Queue    │ Router   │ Server   │ Bridge   │ Reward    │      │
│  │          │          │ (engine) │ (voice)  │ (chain)   │      │
│  │ Elo pair │ DDoS/    │ Scene3D  │ Rank     │ Tx post   │      │
│  │ latency  │ rate     │ Physics  │ derive   │ Elo delta │      │
│  │ region   │ limit    │ Netcode  │ Events   │ Coin mint │      │
│  └────┬─────┴────┬─────┴────┬─────┴────┬─────┴─────┬─────┘      │
│       │          │          │          │           │             │
├───────┼──────────┼──────────┼──────────┼───────────┼─────────────┤
│  Existing Infrastructure                                        │
│  ┌──────────┬──────────┬──────────┬──────────┬───────────┐      │
│  │ Group    │ Mesh     │ Raft     │ Trust    │ ChainCore │      │
│  │ Member   │ Roles    │ Consen-  │ Lattice  │ ManaCoin  │      │
│  │ SWIM     │ Edge/    │ sus      │ Ed25519  │ Blockchain│      │
│  │ Gossip   │ Service/ │          │ Policy   │ PlaneServ │      │
│  │          │ Data     │          │          │           │      │
│  └──────────┴──────────┴──────────┴──────────┴───────────┘      │
│  6 regions: us-east, us-west, eu-west, eu-central,              │
│             asia-east, asia-south                                │
└─────────────────────────────────────────────────────────────────┘
```

---

## The Flow

### 1. Player Queues

Player opens CodexMagic, hits "Play Ranked." The client pings all 6
edge regions and records latencies: `[20, 80, 120, 140, 200, 250]` ms.
The client sends a `QueuedPlayer` to the orchestrator with their Elo
rating, per-region latencies, and game mode.

### 2. Matchmaker Pairs

The orchestrator's `mq-try-pair` scans the queue for a compatible
opponent. Compatibility is Elo-based: rating difference must be within
a window that *widens linearly with wait time*. A player who has waited
60 seconds matches against a wider range than one who just queued. This
prevents starvation without sacrificing match quality for players who
find matches quickly.

### 3. Region Selection (The RTime Insight)

`em-best-shared-region` picks the region with the lowest *aggregate*
latency across both players -- not just closest to one. If Alice has
20ms to us-east and Bob has 25ms to us-east, the aggregate is 45ms.
If Alice has 80ms to us-west and Bob has 30ms to us-west, the
aggregate is 110ms. us-east wins.

This is the key insight RTime was reaching for: optimize for the group,
not the individual. A match where both players have 25ms is better than
a match where one has 5ms and the other has 150ms.

### 4. Server Spawns at Edge

`fleet-spawn` creates a `GameServer` instance at the selected region.
The mesh infrastructure handles the heavy lifting:

- **MeshRoles** orchestrator assigns the new node the `RWorker` role
- **SWIM gossip** (GroupMembership) announces the server to the mesh
- **EdgeRouter** starts routing player traffic to the new server
- **TrustLattice** authenticates the server's identity (Ed25519)

The server boots the game engine: Scene3D, Renderer3D (for
server-authoritative state), Physics, and the rollback Netcode module.

### 5. Helm Wires Up

The `HelmBridge` creates an `EngineSession` and maps players to Helm
voice slots. Player roles (leader, member) derive Helm ranks (admiral,
crew). The River gets a match chat room. Voice hierarchy wires
automatically -- no manual channel setup.

### 6. Game Runs

Rollback netcode handles game ticks. The trust lattice authenticates
every packet (replay protection via sequence numbers). Game events
(kills, objectives, team wipes) flow through the HelmBridge to Helm's
event stream -- critical events (team wipe, victory) trigger emergency
voice broadcasts.

### 7. Match Ends, Rewards Post

`compute-reward` calculates ManaCoin payouts using the Elo expected-
score formula:

```
  expected = 500 + (winner_rating - loser_rating) * 400 / 10000
  delta = K * (1000 - expected) / 1000
  winner_coins = 50 + delta    (upsets pay more)
  loser_coins = 10             (participation reward)
```

K-factor is 32 for ranked, 0 for casual. Reward transactions are
posted to `ChainCore` -- the permissioned ManaCoin blockchain. The
`MintAuthority` handles the minting, with anti-abuse flags for
suspicious patterns (win-trading, rating manipulation).

### 8. Server Drains

The server transitions to `SsDraining`, stops accepting new
connections, waits for the current match to complete, then transitions
to `SsShutdown`. The orchestrator reclaims the node. SWIM gossip
removes it from the membership list. The EdgeRouter stops routing
to it.

---

## Existing Infrastructure Used

| Component | Location | What it provides |
|-----------|----------|-----------------|
| GroupMembership | `codex/os/net/` | SWIM gossip, heartbeats, failure detection, leader election |
| MeshRoles | `codex/os/net/` | Edge/Service/Data/Orchestrator roles, VM lifecycle, auto-scaling |
| EdgeRouter | `codex/os/net/` | DDoS mitigation, rate limiting, session affinity, health routing |
| RaftConsensus | `codex/os/net/` | Distributed config replication, leader election, split-brain safety |
| GossipProtocol | `codex/os/net/` | SWIM-style infection dissemination for membership changes |
| DistributedConfig | `codex/os/net/` | Versioned KV store with TTL, watchers, CAS operations |
| NetworkStack | `codex/os/net/` | TCP/IP, Ethernet, ARP, routing |
| TrustLattice | `codex/os/trust/` | Fixed-point trust scoring with decay and thresholds |
| TrustNode | `codex/os/trust/` | Ed25519 identity, peer sessions, replay protection |
| ChainCore | `apps/codexmagic/` | Hash-linked block chain, Merkle trees, transaction processing |
| MintAuthority | `apps/codexmagic/` | On-demand minting, burn tracking, supply management |
| Matchmaking | `apps/codexmagic/` | Elo rating, seasonal ranks, match request pairing |
| PlaneServer | `apps/codexmagic/` | Multiverse hub, cross-game exchange, dungeon progression |
| HelmBridge | `codex/foreword/engine/` | Game session → Helm voice hierarchy, event stream |
| Netcode | `codex/foreword/game/` | Rollback netcode, input history, desync detection |
| Scene3D | `codex/foreword/engine/` | 3D scene graph, camera, lights, transforms |
| Renderer3D | `codex/foreword/engine/` | Software rasterization pipeline with depth buffer |
| GameLoop | `codex/foreword/engine/` | Fixed-timestep accumulator pattern |
| GameplayTags | `codex/foreword/engine/` | Hierarchical tag queries for ability gating |
| AbilitySystem | `codex/foreword/engine/` | Tag-gated abilities, attribute modifiers, effects |

---

## Edge Regions

| Region | Lat/Lon | Initial Capacity |
|--------|---------|-----------------|
| us-east | 39.0N, 77.0W | 100 servers |
| us-west | 37.0N, 122.0W | 80 servers |
| eu-west | 51.0N, 0.0 | 80 servers |
| eu-central | 50.0N, 8.0E | 60 servers |
| asia-east | 35.0N, 139.0E | 60 servers |
| asia-south | 19.0N, 72.0E | 40 servers |

Capacity is elastic. The orchestrator scales by spawning additional
CDX VMs when active server count exceeds 80% of region capacity and
draining idle servers when below 30%.

---

## What Makes This Different

1. **No cloud provider.** The game server is a CDX binary. It boots
   on bare metal via codex-vm. No AWS, no GCP, no Azure.

2. **No container runtime.** No Docker, no Kubernetes. The mesh
   orchestrator spawns VMs directly. SWIM gossip handles discovery.

3. **No external auth.** The trust lattice with Ed25519 keys handles
   authentication. No OAuth, no JWT libraries, no third-party identity
   providers.

4. **No external database.** Raft consensus replicates state across
   data nodes. The ManaCoin blockchain is the audit trail.

5. **No external matchmaker.** The Elo pairing runs on the orchestrator
   node. Region selection uses measured latencies, not geo-IP guesses.

6. **No external voice.** Helm provides voice hierarchy with rank-based
   routing, wired automatically from game roles.

7. **Self-sustaining.** The same Codex compiler that compiles the game
   engine compiles the mesh infrastructure. The same CDX binary format
   runs the game server and the orchestrator. One language, one binary
   format, one trust model, one blockchain.

---

## Comparison to Industry

| Feature | Edgegap | GameLift | Agones | Codex EdgeMesh |
|---------|---------|----------|--------|----------------|
| Server runtime | Docker | Custom AMI | Docker/K8s | CDX binary |
| Discovery | API | API | K8s svc | SWIM gossip |
| Auth | External | IAM | External | Trust lattice |
| Matchmaking | External | FlexMatch | External | Built-in Elo |
| Voice | External | External | External | Helm (built-in) |
| Economy | External | External | External | ManaCoin chain |
| Region selection | Geo-IP | Latency | Manual | Measured aggregate |
| Scaling | Edge API | Auto-scale | HPA | Mesh orchestrator |
| Dependencies | Linux+Docker | AWS | K8s | None (bare metal) |

---

## Phasing

### Phase 1: Core Orchestration -- DONE (simulation only)

- EdgeMesh chapter with queue, pairing, region selection, fleet management
- ManaCoin reward computation (Elo-based)
- Integration with HelmBridge for voice/chat
- Test coverage for all EdgeMesh operations

### Phase 2: Live Mesh Integration -- NEXT (the real work)

This is what turns Phase 1 from a model into infrastructure:

- Wire EdgeMesh to GroupMembership for real SWIM discovery
- Wire to EdgeRouter for real traffic routing
- Wire to TrustNode for authenticated game sessions
- Wire to ChainCore for on-chain reward posting

The first three are the ones that matter: without them there is no mesh,
only a queue.

### Phase 3: Multi-Game Support

- PlaneServer integration for cross-game matchmaking
- UniversalMatchmaking queue federation across game modes
- Cross-plane ManaCoin rewards (play Chess, earn coins for CodexMagic)
- Tournament mode with bracket management

### Phase 4: Global Scale

- Region auto-discovery (nodes self-report location)
- Inter-region failover (if us-east goes down, reroute to us-west)
- Latency-predictive server pre-warming (spawn before queue fills)
- Economic anti-abuse (ChainCore transaction validator + MintAuthority)
