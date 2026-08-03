# Campaign Studio -- the platform

Status: Active (north-star design) · Created 2026-05-30 · Owner: val

This is the umbrella vision the Explorer designers grow into: a DM campaign
creator and live game table. `GameWorldDesigner.md` is the **Author / entities**
chapter of this doc; everything here frames where that fits.

## 1. What it is

A tool where a DM (and players) **author** a game world -- characters, items,
settings, maps, cities, encounters, story elements, decks -- from a palette of
options rather than tables and dice; **compile** that system-neutral content
into a specific ruleset (D&D, Pathfinder, GURPS, a themed card game); and
**play** a live session across devices -- phones/tablets for character sheets,
dice, moves, marching orders, equipped/readied/deck state, with an optional
shared screen (a TV) for the map and scene/monster art. Co-located or remote.

Multi-user from the start: accounts/profiles, each person's own content, the
ability to create and share.

## 2. The spine: Author → Compile → Play

```
   AUTHOR                     COMPILE                      PLAY
   (Writer / Explorer)        (the emit step)              (Executor)
   ──────────────────         ──────────────────           ──────────────
   entities, maps, cities,    ruleset = emit target:       live session:
   tacticals, decks, story,   module/encounter lowers      authoritative shared
   relationships, events,     to playable mechanics for    state; per-device
   modules, encounters        the chosen system            views; shared screen
        │                           │                            │
        ▼                           ▼                            ▼
   system-neutral content  ──▶  compiled module        ──▶   executing the module
```

**The engine is a compilation step.** The DM authors system-neutral content;
the chosen rule system is a Plug (exactly like Codex's language backends) that
lowers it into playable mechanics. Running the session is *executing* the
compiled module. This is the Codex thesis applied end to end -- Excalibur
(`GameWorldDesigner.md`) already proves the one-entity case; a module/encounter
is just a larger source artifact lowering through the same model.

## 3. Pillars mapped to Codex primitives (the leverage)

Most of this is assembly, not invention -- each capability rides an existing
Codex subsystem:

| Capability | Rides on |
|---|---|
| Accounts / profiles / ownership / sharing | Identity + trust lattice (`OS/Active/Identity.md`, `TrustAndRuntime.md`); content-addressed facts/proposals/verdicts |
| Multi-device clients + shared screen (TV) | Net stack + html-plug clients (the Explorer pages over framed TCP are the prototype); browsers on phones/tablets/TV hit a Codex session server |
| Rulesets (D&D / PF / GURPS / card game) | Emit-plug architecture (`codex/plugs/*`) -- same model as language backends |
| Cards / themed game party | CodexMagic card kit (`apps/games/codexmagic/`) is the MTG/card emit target |
| Art / tacticals / scene + monster pics | The SD visual emitter (already the prompt layer) |
| Randomized palette (no tables/dice) | Pre-compile the system's random tables into a generator the DM samples and curates into choices |
| Persistence / "my library" | `ExplorerStore` paged DB (extend with entity/relationship/event tables, keyed to identity) |

## 4. Author pillar

The creation tools, all data-driven and palette-first:

- **Entities** -- items / characters / settings / factions / locations. *(Item +
  Excalibur slice done; characters next.)*
- **Spatial** -- hex maps, random terrain generation, map making, city design,
  tactical grids. Its own domain (algorithms + rendering); own design doc.
- **Decks** -- decklists/cards via the MTG/CodexMagic emit target.
- **Story** -- relationships graph + campaign timeline/events (Narrator/Historian).
- **Composition** -- module designer and encounter designer assemble entities +
  maps + events into playable units. The encounter designer is the palette in
  action: the DM picks from curated, pre-generated options.

## 5. Compile pillar

The chosen ruleset is an emit target. Inputs scale up from a single entity to a
full module:

- entity → stat block / card (done for weapons).
- encounter → statted monsters + map + objectives for the system.
- module → a sequence of encounters/scenes + the entities/maps they need.

The **random tables of a system are themselves content**: "compiling" them
yields a generator. Instead of the DM rolling on a wandering-monster table, the
table is compiled into a sampler that pre-rolls a *palette* of options the DM
curates. Generation (procedural and/or LLM-assisted) feeds the palette; the DM
picks. Deterministic emit keeps results reproducible.

## 6. Play pillar (the capstone)

The live session runtime:

- **Authoritative shared state** on a Codex session server. DM-authoritative is
  the natural model.
- **Devices send intents** (roll, move, ready spell, equip, play card) and
  render their slice: character sheet, dice roller, map + marching orders, hand
  / deck / equipped / readied.
- **Shared screen** is another (read-mostly) client showing the map and
  scene/monster art.
- **Sync** over the net stack; presence + intent ordering.

The whole client tier is Codex server + html-plug pages served to browsers --
the stack the Explorer demo already exercises, scaled to real-time.

## 7. Identity, ownership, sharing

Lean on Codex's identity + trust lattice rather than a bolted-on auth system.
A campaign, module, entity, or map is content; ownership and sharing are
trust/permission facts over content-addressed data. "My library vs. yours vs.
the table's shared set" falls out of the repository protocol. Decision (see §9):
start with a thin profile store and grow into the full lattice, or use the
lattice from the outset.

## 8. Environment-role mapping

The platform is a concrete instance of Codex's environment vocabulary:

- **Writer / Explorer** -- the authoring tools.
- **Verifier** -- ruleset legality + balance checks; ownership/sharing permissions.
- **Executor** -- the live session runtime (executes the compiled module).
- **Narrator** -- scene/encounter surfacing, the DM palette, generated prose.
- **Historian** -- campaign timeline + session log (what happened, when).

## 9. The honest hard parts / open decisions

1. **Real-time multi-client sync** -- the one substantial new engineering surface
   (presence, intent ordering, authority). Everything else is authoring + emit.
2. **Spatial / procedural domain** -- hex/terrain/city generation is deep enough
   to be its own track and design doc.
3. **Identity depth** -- thin profile store first vs. full trust lattice now.
4. **Generation source** -- procedural tables compiled to generators,
   LLM-assisted, or both, feeding the palette.
5. **Module/abstraction granularity** -- how rich the system-neutral encounter/
   module IR must be to emit faithfully across very different systems (the same
   IR-thickness dilemma as the entity abstraction; let the hardest target drive
   the minimum).

## 10. Phased roadmap (each phase shippable)

1. **Generalize the emitter model** -- second entity kind (character), real
   CodexMagic *card record* out of `emit-mtg` (not just HTML), maybe a second
   system. Hardens the abstraction. *(In progress.)*
2. **Persistence + accounts** -- DB-back entities keyed to identity; "my library."
3. **Spatial layer** -- hex maps + terrain generation + a tactical grid, rendered
   to the html client.
4. **Module + encounter designers** -- compose entities + maps + events; the
   palette; the compile step over a whole module.
5. **Session runtime** -- the live multi-device table. Capstone.

Each builds on the last and is usable on its own (a personal library, then
maps, then composed encounters) well before the full live table.

## 11. Relationship to existing code

- `apps/explorer/design/Active/GameWorldDesigner.md` -- the Author/entities
  chapter (entity model, abstraction, emitter plugs, the Excalibur slice).
- `apps/explorer/{WorldModel,Emitters,ExcaliburSlice}.codex` -- the working slice.
- `apps/explorer/ExplorerStore.codex` / `ExplorerServer.codex` -- persistence +
  the session/content server substrate.
- `apps/games/codexmagic/` -- the card emit target.
- `codex/os/net/`, `codex/plugs/*`, `OS/Active/Identity.md` -- net, emit, identity.
