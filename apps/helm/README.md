# Helm

A scalable communications platform with two integrated surfaces: The River (a high-volume chat room that auto-clusters messages into topic Currents ranked by attention score) and The Bridge (a hierarchical voice command system where rank governs audio routing). Designed for large coordinated groups -- gaming raids, naval operations, live events.

## Modules

- **HelmTypes** -- Complete type system: Message, Current, TimeBand, Room, VoiceNode, JointChannel, EmergencyBroadcast, RoomSettings (three presets), Sentiment, AttentionScore
- **HelmCluster** (The River) -- Message ingestion with automatic mode switching; keyword similarity clustering; attention-scored current ranking; sentiment detection; current pin/mute
- **HelmVoice** (The Bridge) -- 4-rank hierarchy (Admiral/Captain/Lieutenant/Crew), can-hear routing rules, mute/unmute/speaking state, joint channels, emergency broadcast, request-to-speak
- **HelmMixer** -- MixRule computation: downward/squad-internal/peer/joint/listen-up/emergency override volume levels
- **HelmAttention** -- Multi-factor scoring (participants, clicks, recency, velocity); time band management
- **HelmStore** -- JSON persistence of Room (kind 45) and VoiceHierarchy (kind 46) via DiskFacts
- **HelmGameBridge** -- Game session integration: 8 preset rank templates (MMO Raid, Battle Royale, MOBA, RTS, FPS, Sports, Card Game, Custom), GameSession with roster, GameEvent stream
- **RiverPage** -- Widget tree for The River: sorted current cards with attention bar, sentiment indicator
- **BridgePage** -- Widget tree for The Bridge: admiral panel with captain sub-panels and crew indicators

## Completeness

78% -- All domain logic is fully implemented. Gaps: vh-request-speak stores the request but doesn't attach it to the hierarchy; no actual audio pipeline or network transport (deferred to plug/kernel); HelmStore saves room metadata but not full Current list; HelmGameBridge game session is typed but has no entry point.

## Codex Conformance

Full -- Written entirely in Codex. UI through Widget foreword. Persistence via DiskFacts. Audio mixing rules are data-only; actual audio output deferred to future plug integration.
