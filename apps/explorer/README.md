# Codex Explorer

A data-driven, browser-based world-building and game-asset design suite. Users design fantasy items, characters, settings, and worlds; the compiler -- not hand-written JavaScript -- produces all client-side logic. The bare-metal server compiles to a CDX binary.

## Modules

- **ExplorerTheme** -- Shared dark-gold theme; derives all CSS from a Theme record at runtime
- **ExplorerData** -- Single source of truth for all content: 31 item types, materials, rarities, races, classes, biomes, enchantments
- **ExplorerStore** -- Custom binary paged store (4 KB pages, CCE-native, little-endian) for the bare-metal server
- **ExplorerDb** -- Relational schema (7 tables) declared with the Codex Data quire
- **ExplorerServer** -- Bare-metal CDX HTTP server with JSON API, per-user creations (save/delete/remix/export-md), Accounts auth
- **AuthClient** -- Reusable login/register/logout widget compiled to JavaScript
- **ItemDesignerApp** -- 8-dimension item prompt builder
- **CharDesignerApp** -- 5-dimension character prompt builder
- **SettingDesignerApp** -- 5-dimension setting/world prompt builder
- **CardDesignerApp + CardEmitter** -- Card dimension explorer; lowers WorldModel entities into CardTemplate objects
- **WorldModel + Emitters** -- System-neutral game-entity abstraction; emitters lower to Stable Diffusion prompt, D&D 5e stat block, and MTG card
- **ExcaliburSlice** -- Vertical demo slice: Excalibur and Merlin rendered through all three emitters
- **NameForge** -- Seeded deterministic name generator for people, settlements, kingdoms, regions, and natural features
- **StoryGraph** -- Procedural branching story generator: archetype x motif x seed = deterministic saga graph
- **WorldForge** -- Browser SPA: picks seed + archetype + motif, runs NameForge and StoryGraph client-side
- **CreationsApp** -- Account-owned world builder + community gallery
- **WorkflowExporter** -- Builds ComfyUI multi-node pipeline JSON for layered image generation
- **VoiceStudio** -- Voice synthesis UI with character voice profiles and emotion controls

## Completeness

70% -- The core pipeline is functional end-to-end: server compiles and serves, designer apps are DB-backed, NameForge/StoryGraph/WorldForge run in the browser, AuthClient and the creations API work. The ExcaliburSlice demo is complete. Gaps: VoiceStudio has no TTS backend, WorkflowExporter download/launch path not integrated, CardDesignerApp interactive flow partially wired, no test harness.

## Codex Conformance

Full -- All modules are written in Codex. Client-side logic is emitted through the HTML plug CDX. The bare-metal server is a CDX binary. No hand-written JavaScript; backend and browser implementations derive from the same Codex source.
