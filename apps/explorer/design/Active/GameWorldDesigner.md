# Explorer → Game-World Design Tool

Status: Active (design) · Created 2026-05-30 · Owner: val

## 1. Thesis: game systems are emit targets

The Explorer designers (Setting / Character / Item) today are *prompt-gen
knobs for Stable Diffusion*. That is useful, but it is one output. The real
opportunity is to make Explorer a **game-world design tool**: you author
characters, items, settings, story elements, the relationships between
characters, and the events planned for a campaign — and the tool **renders**
that world into whatever ruleset you target.

This is the Codex founding thesis applied to worldbuilding. Codex "abstracts
[all human languages] into a single perfect language … transpiled to any old
human-designed language." We do the same for game content:

> An entity is the canonical source of truth. A rule system — D&D 5e,
> Pathfinder, GURPS, Gamma World, Magic: the Gathering — is an **emit target**,
> a Plug, exactly like `RustEmitter` / `WasmEmitter` / `HtmlEmitter`.

**Excalibur** is always a longsword named Excalibur, looks a certain way, and
carries a fixed backstory. Those are invariant. But its *stats* render
differently per system: a `+3 longsword, attunement by a lawful-good creature`
in D&D 5e; a point-costed fine weapon in GURPS; a Legendary Equipment in MTG
that may only equip a King. **Author once, emit to many systems.**

## 2. Architecture (it mirrors the compiler)

```
   Canonical Entity            Semantic Abstraction            Rule-System Emitters
   ("what it IS, always")      (the "IR" — system-neutral)     (Plugs — per ruleset)
   ───────────────────         ──────────────────────          ─────────────────────
   identity, taxonomy,   ──▶   weapon · martial melee   ──▶    D&D5e   → stat block
   appearance, lore,           blade · legendary tier ·        Pathfinder → stat block
   provenance, links           slashing+radiant ·              GURPS   → point build
                               wielder=worthiness ·            GammaWorld → ...
                               leadership aura                 MTG/CodexMagic → card
                                                               SD-visual → prompt (today)
```

- **Layer 1 — Canonical Entity.** The literate, human-readable truth.
  System-independent. The current DB rows are a thin shadow of this; it grows
  into a rich record. *The SD prompt is derived from this layer — not authored
  separately.*
- **Layer 2 — Semantic Abstraction (the IR).** The system-neutral *meaning* an
  emitter needs. This is the hard, valuable core, exactly like designing
  Codex's IR so it can lower faithfully to both Rust and WASM. It must encode
  *meaning* (worthiness, power tier, role, magical affinity) — never one
  system's mechanics.
- **Layer 3 — Rule-System Emitters (Plugs).** Each lowers the abstraction its
  own way. Same shape as the existing language-emitter plugs; resolved the same
  way. Deterministic given the abstraction.

The discipline that makes this work is the same discipline that makes the
compiler work: **keep the IR honest.** If an emitter wants something the
abstraction can't express, the fix is to enrich the abstraction in a
system-neutral way — not to special-case the emitter.

## 3. The canonical entity

Fields common to all entity kinds (item / character / setting / faction /
location / event):

- **Identity** — name, kind, a stable id.
- **Taxonomy** — what it is (longsword; human paladin; alpine valley).
- **Appearance** — the descriptive basis the SD-visual emitter consumes.
- **Lore** — backstory, provenance, significance (literate prose).
- **Links** — typed edges to other entities (see §5).

Entity kinds extend this with kind-specific canonical fields (a weapon has
reach/heft/edge; a character has lineage/vocation/disposition).

## 4. The semantic abstraction (the IR) — the crux

This is where the design lives or dies, and **MTG is the forcing function.**
Because Magic is a CCG, not an RPG, an abstraction that can faithfully emit
Excalibur to *both* a d20 stat block and a Magic card cannot be smuggling RPG
mechanics inside. It must be about meaning. A first cut for the *weapon* kind:

- `weapon-class` — blade / haft / ranged / focus / worn …
- `power-tier` — mundane / fine / exceptional / legendary / artifact
- `damage-concept` — slashing / piercing / bludgeoning (+ elemental/holy affinity)
- `wielder-constraint` — none / proficiency / alignment / worthiness / royalty …
- `granted-effects` — leadership aura, light, fear, regeneration … (tagged, tiered)
- `narrative-weight` — how central it is (flavors rarity/legendary status)

Each emitter maps these to its own dials (CR / mana cost / point value /
attunement / equip cost / colour identity). The abstraction is **declarative
data plus tagged effects**; emitters supply the *logic* that turns tags+tiers
into system numbers.

Open: how thick is the IR? Too thin → emitters guess; too thick → coupled to
one worldview. Heuristic: **let the hardest target (MTG) drive the minimum
expressiveness; let the richest target (GURPS) drive the maximum granularity.**

## 5. From catalog to world: relationships & events

Entities + emitters give you a *richer catalog*. What makes it a *world* is the
graph and the timeline — this is the **Narrator / Historian** role in Codex's
environment vocabulary.

- **Relationship graph** — typed edges between entities: `wields`, `forged-by`,
  `allied-with`, `rival-of`, `rules`, `located-in`, `member-of`. Excalibur
  `forged-by` the Lady of the Lake; Arthur `wields` Excalibur; Arthur `rules`
  Camelot.
- **Campaign timeline** — planned events with preconditions and participants:
  "the sword is drawn from the stone (Arthur, T0)", "Camelot falls (T+N)".
  Events reference entities; emitters can render an event into a system's
  encounter / quest / scene format.

The graph + timeline are themselves emit targets: a campaign can render to a
GM's prep doc, a VTT import, or a Narrator-generated session brief.

## 6. The emitter plug interface

An emitter is a Codex chapter that implements, per entity kind:

```
emit-<system>-<kind> : CanonicalEntity, Abstraction -> SystemRendering
```

`SystemRendering` is per-system structured output (a stat block record, a card
record, a prompt). Emitters compose with the existing plug architecture —
they are resolved exactly like `RustEmitter` et al. **CodexMagic is the proof
target for MTG**: the MTG emitter produces a CodexMagic card record, so an
authored entity flows straight into the existing ~14.5K-line card kit.

The **Verifier / trust lattice** has a natural role: an emitter pass can check
*legality / balance* — is this a legal MTG card? is this CR-appropriate for the
party level? — and surface verdicts rather than silently shipping a broken stat
block.

## 7. The SD-visual emitter, reframed

Today's `build-*-prompt` functions become **the SD-visual emitter** — one
target among many, consuming the canonical Appearance + Lore. No special
status; it sits beside the D&D and MTG emitters in the same dispatch.

## 8. Authoring & generation

Today the flow is generative (pick knobs → prompt). For the world model:

- **Draft via LLM, curate by hand.** Describe Excalibur in prose → infer a draft
  abstraction → the author curates. Keep the *emitters deterministic* so a
  given (entity, abstraction) always renders the same system output. That makes
  renderings reproducible and trustworthy.
- **Verify.** Per-system legality/balance checks (Verifier) gate questionable
  renderings.

## 9. Storage

Extends `ExplorerStore` (the multi-table paged `.db`):

- Rich **entity tables** (one per kind) carrying canonical fields + a serialized
  abstraction blob.
- **relationship** table (typed edges: from-id, kind, to-id).
- **event** table (timeline: id, when, participants, preconditions).

Served by `ExplorerServer` over the same `/api/d/<table>` mechanism; the
designer pages fetch and render. Emitters run server-side (Codex) or as a new
`/api/emit/<system>/<entity>` endpoint returning the system rendering.

## 10. Vertical slice: Excalibur, end to end

Prove the whole thesis on one object:

1. Author **Excalibur** as a canonical weapon entity + a v1 weapon abstraction.
2. Write **two emitters** — D&D 5e stat block + a CodexMagic/MTG card — beside
   the existing SD-visual emitter.
3. In the Item designer, render **all three** for the same entity, side by side:
   the art prompt, the 5e magic-item block, and the Magic card.

If that clicks, generalize the abstraction, add systems (Pathfinder, GURPS,
Gamma World) and entity kinds (character, setting), then layer in the
relationship graph and campaign timeline.

## 11. Open decisions

1. **IR thickness** — minimum expressiveness driven by MTG; granularity by GURPS.
2. **Faithful translation vs. balance/legality** — how much Verifier involvement.
3. **Authoring** — how much hand-authored vs. LLM-assisted draft-then-curate.
4. **Scope order** — prove entities+emitters (the Excalibur slice) before the
   relationship/event world model, or build them together.

## 12. Relationship to existing code

- `apps/explorer/ExplorerStore.codex` — storage substrate (extend with entity /
  relationship / event tables).
- `apps/explorer/ExplorerServer.codex` — serves tables; add emit endpoints.
- `apps/explorer/{Setting,Char,Item}DesignerApp.codex` — become the **Writer**
  over the world model; gain emit-target panels.
- `apps/games/codexmagic/` — the **MTG emit target** (cards), already built.
- The Codex plug architecture (`codex/plugs/*`) — the model the rule-system
  emitters follow.

See also: the Explorer pip-tree UI and DB-backing work that this builds on
(submitted across CL 2706 / 2747–2754).
