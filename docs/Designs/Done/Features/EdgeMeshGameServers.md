# Edge Mesh Game Servers -- Auto-Spawning Distributed Game Infrastructure

## Status

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

**Phase 2 is DONE.** Three chapters in `apps/edgemesh`, each with its arm
under `codex/test/edge-mesh-*`: `EdgeMeshLive` (discovery -- a region is
healthy only while the group holds a HEALTHY location for it, and placement
picks the lowest-load healthy node or REFUSES), `EdgeMeshRoute` (routing --
a returning player goes back to the node they were on, but only while that
node is still healthy), and `EdgeMeshAdmit` (authenticated sessions).

**Phase 3 is DONE (2026-09-08).** Five stages, five chapters in `apps/edgemesh`
and five arms under `codex/test/edge-mesh-*`: `EdgeMeshFederate` (one lane per
game), `EdgeMeshPlace` (a match reaches a node running the match's own game),
`EdgeMeshRewards` (per-plane wallets, settled once), `EdgeMeshBracket`
(tournament rounds and byes), `EdgeMeshPlanes` (the games quire's game identity
bridged to a lane key).

**Phase 4 is DONE (2026-09-08), and this design is COMPLETE.** Four stages,
four chapters and four arms: `EdgeMeshFailover` (cross regions, never cross
games, nearest serving region), `EdgeMeshDiscover` (a declared region is never
moved by a claim, and the lowest node name decides a new one),
`EdgeMeshPrewarm` (a plan counts what is already warm, so a second tick at the
same demand spawns nothing), `EdgeMeshMint` (a reward is validated before the
ledger is touched). Nine chapters and nine arms across phases 3 and 4, every
arm green together.

Two findings from the arc outlived it. L-ALIAS is now a `LESSONS.md` row: the
list and record builtins that allocate nothing write through the caller's
pointer, and the hazard bit three times here. And `ChainCore`'s hash trapped on
overflow for as long as the chapter had existed, because nothing had ever run
it; `codex/test/edge-mesh-mint` is the first thing in the tree to do so.

**Two ordering decisions the arms exist to hold, because both are invisible
in a passing run and reversing either looks like a refactor.**

Identity is checked BEFORE placement. An unauthenticated caller gets the
same refusal whether the region is busy, empty, or not a region at all, so
the refusal text is not a free map of the mesh; an unknown peer and a
known-but-unauthenticated one give the same answer, because telling them
apart tells an attacker which names exist.

Affinity beats load, and health beats affinity. Least-loaded is right for a
new session and loses a match for a returning one; affinity that outlives
the node's health sends every reconnect to a box that is already failing. A
route also answers WHY, sticky or fresh, because a working affinity table
and one that re-picks every time look identical in a load graph.

**A third ordering decision, phase 3 stage 1, and it is invisible the same way.**

The federation partitions the queue IN FRONT OF `mq-try-pair` rather than
beside it, by narrowing the queue that is handed over. `mq-find-pair` widens
the rating window with wait time, so a partition applied after it is a
partition a long enough wait defeats: the two arms that hold this are a pair
1000 apart inside one lane, which MUST pair once the wait has widened the
window from 200 to 1000, and two players in different games at that same wait,
which must not. Both pass and both are needed; either alone is satisfied by an
implementation that is wrong.

**Stage 2 adds the third level of the phase 2 ordering, for the same reason.**
Phase 2 held that affinity beats load and health beats affinity. Placement of a
federated match holds that GAME beats load and health beats game: a match goes
to a node running its own game even when a node running another game is
lighter, and when its own game has no healthy node the answer is a REFUSAL, not
the healthy box next door. The fallback is the tempting bug, it looks like
resilience, and a load graph cannot tell it from correct placement.

The partition is also why the chapter does not re-implement pairing. Reusing
`mq-try-pair` on a narrowed queue costs one copy of the lane's players per
call, proportional to the lane, and buys the guarantee that the window and its
widening are the foreword's own and cannot drift from it.

**Stage 3's two, same test: invisible in a passing run.** Settlement is once per
match id, so a replayed match credits nothing the second time; and a plane with
no wallet is REFUSED rather than opened, because auto-opening turns a mistyped
plane name into a coin sink nobody sees. Both look like a working ledger in any
single run.

Stage 3 also had to avoid `list-set-at` and `list-push` for the wallet list. The
first is a store through the same pointer (`CostModel.md` 5.1's zero-allocation
row) and the second can extend in place, so crediting through either writes into
whatever ledger the caller still holds -- the same hazard as the `GroupState`
constraint below, in the list builtins rather than in a record. The arm would
have shown it as a doubled balance on the repeat-settlement line.

**Two constraints anything continuing this design has to obey.**

**"Wire A to B" in the phases below means "a chapter ABOVE A and B", never an
edit to A.** A foreword module may not depend on `codex.os`, and the quire
order is foreword, codex, codex.os, apps (`DevelopersRulebook.md`, Library
Rules 1 and 2). `EdgeMesh` is `codex.foreword.engine` and `GroupMembership`
is `codex.os.net`, so the chapter that knows both sits right of both, which
is why phase 2 landed in an app quire.

**A `GroupState` cannot be held across a registration.** Its operations are
`__record-set` on the argument, which stores in place and returns the same
record, so an earlier name is not an earlier state: a group read after a
later registration reports the LATER health. Read only the newest state, and
know that a test which registers into one variable and reads another will
lie. Two fixtures built from identical constructor arguments share one
record for the same reason.

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

### Phase 2: Live Mesh Integration -- DONE for the three that matter

- GroupMembership for real SWIM discovery -- DONE
- EdgeRouter for real traffic routing -- DONE
- TrustNode for authenticated game sessions -- DONE
- ChainCore for on-chain reward posting -- NOT DONE, and it is the one
  bullet here that is not load-bearing: without the first three there is no
  mesh, only a queue, and rewards can post from a queue.

### Phase 3: Multi-Game Support

- Queue federation -- DONE (stage 1). `apps/edgemesh/EdgeMeshFederate.codex`,
  arm `codex/test/edge-mesh-federate`. One lane per game, and within a lane a
  pairing sees only its own mode.
- Placing a federated match -- DONE (stage 2).
  `apps/edgemesh/EdgeMeshPlace.codex`, arm `codex/test/edge-mesh-place`. A
  game's servers register under their own service name, so a match reaches a
  node running its own game and health and load stay
  `gs-pick-load-balanced`'s.
- Games-quire integration -- DONE (stage 5). `apps/edgemesh/EdgeMeshPlanes.codex`,
  arm `codex/test/edge-mesh-planes`. The games quire decides what a game IS and
  stage 1 keys a lane by a name the caller supplies; the bridge sits above both
  and cites `GameRegistry` alone, because game identity is all the bridge needs
  (L-SUBSET).

  **The lane key carries the game id, and an unknown id is refused rather than
  translated.** `find-game` answers a FABRICATED `RegisteredGame` for an id
  nobody registered, carrying `game-id = -1` and the display name "Unknown"
  (`GameRegistry.codex:115`), therefore a bridge keyed on the display name puts
  every unknown id into one lane named "Unknown" and pairs players from
  different games inside that lane -- the exact pairing stage 1 exists to
  refuse. The test is `game-id` and never the name, because a registered game
  is permitted to be called "Unknown". Two registered games are likewise
  permitted to share a display name, so the lane key is `name#id`.

  Both blockers this stage waited on were fixed by val in the games quire (main
  23408) and verified here before use: `UniversalMatchmaking`'s record is now
  `GameMatchQueue`, and `PlaneServer` is a library whose entry point moved to
  `PlaneServerEntry`. A chapter citing `EdgeMesh`, `UniversalMatchmaking` and
  `PlaneServer` together compiles and runs.
- Cross-plane ManaCoin rewards -- DONE (stage 3).
  `apps/edgemesh/EdgeMeshRewards.codex`, arm `codex/test/edge-mesh-rewards`. A
  ledger of per-plane wallets; `compute-reward` stays the foreword's and this
  decides only where the coins land and whether they may land yet. Settling is
  once per match id, and a plane with no wallet is refused rather than opened.
  ChainCore posting is still not done, and this is the queue-side half of it.
- Tournament mode with bracket management -- DONE (stage 4).
  `apps/edgemesh/EdgeMeshBracket.codex`, arm `codex/test/edge-mesh-bracket`.
  `ModeTournament` existed as a queue mode with nothing consuming it. The bye
  goes to the top SEED, and a round refuses a wrong number of results or a name
  that did not play its bout.
- **Cross-app cites are ALLOWED (root, 2026-09-08).** Library Rule 2 orders
  quires, not apps: apps sit at one level and the only ban is a cycle. An
  `apps/edgemesh` chapter may cite `apps/games/CodexMagic`; CodexMagic must
  never cite edgemesh back, and the citing chapter is the one above both.
  Declare the cite per chapter (L-SUBSET). This is what the PlaneServer and
  UniversalMatchmaking bullets below were waiting on.

### Phase 4: Global Scale

- Region auto-discovery -- DONE (phase 4 stage 2).
  `apps/edgemesh/EdgeMeshDiscover.codex`, arm `codex/test/edge-mesh-discover`.
  A node reports `region|lat|lon` under one service. **A DECLARED REGION IS
  NEVER MOVED BY A CLAIM**, because a node claiming to stand at `us-east` from
  Tokyo re-aims every distance measured against `us-east` and a placement
  follows the distance. Among claims for a new region the LOWEST NODE NAME
  decides, so registration order cannot move a region: a node restarting sends
  the node's own claim to the end of the group's list, and co-ordinates
  following list order would move with the restart.

  **`region-add` appends with `list-push`, which extends in place**, so
  discovering a region through `region-add` writes the region into whatever
  `RegionList` the caller still holds, `default-regions` included. Discovery
  builds a new list by concatenation. The arm caught the leak: a scenario making
  no claim at all reported seven regions. Third instance of the aliasing hazard
  in this arc, after the `GroupState` constraint below and stage 3's wallet list.
- Inter-region failover -- DONE (phase 4 stage 1).
  `apps/edgemesh/EdgeMeshFailover.codex`, arm `codex/test/edge-mesh-failover`.
  Phase 3 stage 2 refuses when a game has no healthy node in the region asked
  for, which is right for a wrong-game node and wrong for a region that has
  gone down. A failover crosses REGIONS and never crosses GAMES, and the region
  chosen is the nearest one still serving the game rather than the first one in
  the list.
- Server pre-warming -- DONE (phase 4 stage 3).
  `apps/edgemesh/EdgeMeshPrewarm.codex`, arm `codex/test/edge-mesh-prewarm`. A
  plan counts what is already WARM and subtracts it, and is capped by the
  region's capacity. **A plan reading the queue alone answers the whole demand
  on every tick and grows a fleet without bound, while every individual tick
  reads as correct**, so the arm ticks twice: six waiting and nothing warm
  spawns three, and the same queue with three warm spawns none. Unhealthy
  servers do not count as warm.
- Economic anti-abuse -- DONE (phase 4 stage 4).
  `apps/edgemesh/EdgeMeshMint.codex`, arm `codex/test/edge-mesh-mint`. A reward
  is validated by `validate-tx` BEFORE the ledger is touched, because a guard
  that credits and then rolls back answers the same words and leaves a
  different ledger. Every refusal arm asserts the BALANCE as well as the
  answer: an amount above the bound, a sender that is not the platform key, a
  bad signature and a plane with no wallet all leave the ledger at zero, and a
  match settled twice behind the guard stays at the first credit.

  **The stage was blocked first by an overflow TRAP inside `ChainCore`'s hash,
  and the trap was the correct behaviour of the seed rather than a compiler
  defect** (root, 2026-09-08, COMPILER-36 class; L-CENSUS names the same miss
  in `Hamt`'s djb2 and the classic-games LCG). A mixer's multiply-add wraps by
  design while `Integer` traps on overflow, so `compute-tx-hash 1 0 0 0 0 0`
  answered `EXC=06` on the first NONZERO input. `hash-combine` and
  `hash-finalize` now route the arithmetic through `hash-mul` and `hash-mix`,
  whose parameters and result carry the wrapping mode. Nothing had ever run
  `ChainCore`: no arm in `codex/test` cited the chapter, so the trap was as old
  as the chapter and only compiling was ever asked of it (L-UNHEARD). This arm
  is the first thing in the tree to run it.
