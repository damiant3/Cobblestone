# CodexMagic: Prismatic System Design

Replaces the MTG clone mechanics with an original system based on
subtractive color theory (the paint/pigment color wheel everyone
learns in school) grounded in real spectral optics, with a Diablo-
style equipment and gem socketing system.

## Design Thesis

Casting is a two-axis system: **Rays** (total resource budget, grows
automatically) and **Gemstones** (color capacity, drawn from deck).
You can run out of rays before gems or gems before rays. This replaces
the single-axis land system where mana screw is binary.

## Core Concepts

### Light Rays

A player's casting budget. One new ray per turn, cumulative (turn 1 =
1 ray, turn 5 = 5 rays). All rays refresh at upkeep. A ray spent
without passing through any stone produces White (unfiltered light).

### Gemstones

Cards drawn from the deck and played as permanents. One gemstone play
per turn. Each gemstone taps when assigned to a ray and untaps at
upkeep. A single gemstone can only serve one ray per turn, but one
ray can chain through multiple gemstones to mix colors.

### Casting

To cast a spell, assign rays to gemstone chains that produce the
required colors. One ray through one or more stones = 1 mana of the
resulting color. The ray is spent, all stones in the chain tap.

## Color Wheel

Based on subtractive color theory (RYB), mapped to spectral wavelengths.

### Primary Colors -- each has its own gemstone

| Gemstone  | Color  | Symbol | Wavelength |
|-----------|--------|--------|------------|
| Ruby      | Red    | R      | 620-750 nm |
| Topaz     | Yellow | Y      | 565-590 nm |
| Sapphire  | Blue   | B      | 450-500 nm |

### Secondary Colors -- each is a mix of two primaries

| Mix       | Color  | Symbol | Gemstone   | Wavelength |
|-----------|--------|--------|------------|------------|
| Red + Yellow | Orange | O   | Carnelian  | 590-620 nm |
| Yellow + Blue | Green | G  | Emerald    | 520-565 nm |
| Red + Blue | Purple | P    | **None**   | Non-spectral |

Orange and Green are real spectral wavelengths, so they have their
own gemstones. Players can produce them directly (1 stone, 1 ray) OR
by mixing two primaries (2 stones, 1 ray).

**Purple is the only color with no gemstone.** It is non-spectral --
it exists only when the two ends of the visible spectrum (Red + Blue)
are combined. Purple always costs a minimum of 2 gemstones (Ruby +
Sapphire), making it the most stone-hungry color in the game. This is
the central deckbuilding tension for Purple decks.

### White -- Unfiltered Light

A ray spent without any gemstone = 1 White mana. White is pure light,
the absence of filtering. It costs 0 stones.

### Diamond -- Multiplier

Diamond is not a color. It doubles the output of whatever chain it
joins. It stacks as a 2x multiplier per Diamond.

- Diamond + ray (no other stone) = 2 White
- Diamond + Ruby + ray = 2 Red
- Diamond + Ruby + Sapphire + ray = 2 Purple
- Diamond + Diamond + Ruby + ray = 4 Red

### Obsidian -- Fluorescence Toggle

Obsidian produces UV/black light. Assigning a ray to an Obsidian
activates Fluorescence for the rest of the turn. This is a toggle:

- Once active, all spells cast that turn with a "With Fluorescence"
  clause get their boosted effect.
- Cards without a Fluorescence clause are unaffected.
- One Obsidian activation covers all eligible spells that turn.
- Costs 1 ray and taps the Obsidian.
- Resets at upkeep.

Fluorescence is not a color -- it is a modifier. It does not combine
with other stones in a chain; it is its own separate ray assignment.

## Cost Structure

### Two Axes

Every card cost has two dimensions:

- **Ray cost** = total rays spent (determines tempo/turn pressure)
- **Stone cost** = total stones tapped (determines board/deck pressure)

| Cost      | Rays | Stones | Notes                           |
|-----------|------|--------|---------------------------------|
| {1}       | 1    | 0      | 1 White (unfiltered)            |
| 1R        | 1    | 1      | 1 ray through Ruby              |
| 1P        | 1    | 2      | 1 ray through Ruby + Sapphire   |
| 1P + {3}  | 4    | 2      | Purple + 3 White                |
| 2R + 1G   | 3    | 3      | 2 Rubies + 1 Emerald (or Topaz+Sapphire) |
| 1O + 1P + 1G + Fluorescence | 3+1 | 7+1 | 3 color rays + 1 Obsidian ray |

### With Fluorescence

Cards may have an optional Fluorescence clause:

> **Obsidian Shade** -- 1R -- Creature 2/2
> *With Fluorescence: +2/+0 and Deathtouch*

If Fluorescence is active this turn, the creature enters as a 4/2
with Deathtouch. If not, it's a vanilla 2/2. The Fluorescence cost
is paid once per turn (the Obsidian activation) and applies to all
eligible casts that turn.

### Mixing Shortcuts

Orange and Green can be produced two ways:

| Color  | Direct (1 stone) | Mixed (2 stones)    |
|--------|-------------------|---------------------|
| Orange | Carnelian         | Ruby + Topaz        |
| Green  | Emerald           | Topaz + Sapphire    |

Having the direct stone is more efficient. Mixing is a fallback when
you have the primaries but not the secondary stone. This rewards
diverse deckbuilding.

## Turn Flow

1. **Upkeep** -- All gemstones untap. All rays refresh. Gain 1 new ray.
   Fluorescence toggle resets to off.
2. **Draw** -- Draw a card.
3. **Main Phase 1** -- Play a gemstone (optional, 1 per turn). Assign
   rays to gemstone chains. Optionally activate Obsidian for
   Fluorescence. Cast spells.
4. **Combat**
5. **Main Phase 2** -- Continue casting with remaining rays/stones.
6. **End Step / Cleanup**

## Gemstone Types (Base Set)

### Primary Stones (Basic -- unlimited in deck)

| ID | Name      | Produces |
|----|-----------|----------|
| 0  | Ruby      | Red      |
| 1  | Topaz     | Yellow   |
| 2  | Sapphire  | Blue     |

### Secondary Stones (Basic -- unlimited in deck)

| ID | Name      | Produces | Equivalent Mix  |
|----|-----------|----------|-----------------|
| 3  | Carnelian | Orange   | Ruby + Topaz    |
| 4  | Emerald   | Green    | Topaz + Sapphire|

### Special Stones (Basic -- unlimited in deck)

| ID | Name     | Effect                 |
|----|----------|------------------------|
| 5  | Diamond  | Doubles chain output   |
| 6  | Obsidian | Activates Fluorescence |

**No stone for Purple.** Must mix Ruby + Sapphire.

## Migration from MTG Clone

### What Changes

| Old (MTG)          | New (Prismatic)                        |
|--------------------|----------------------------------------|
| 5 colors (WUBRG)   | 3 primaries (RYB) + 3 secondaries (OGP) |
| White/Black colors | White = unfiltered ray / Obsidian = Fluorescence modifier |
| Lands in deck      | Gemstones in deck                      |
| 1 land drop/turn   | 1 gemstone play/turn                   |
| Tap land for mana  | Assign ray through stone chain         |
| Mana pool (1 axis) | Rays + Stones (2 axes)                 |
| No land = screwed  | No stones = screwed (but rays still grow) |
| Kicker costs       | Fluorescence toggle                    |

### Cards That Need Retheme

All 60 cards in the pool need new names, new costs, and Fluorescence
clauses where appropriate. The W/U/B/R/G cost structure becomes
R/Y/B/O/G/P with White as generic. Keywords stay (Haste, Flying,
etc.) -- those are combat mechanics, not color mechanics.

### Code Files Affected

| File              | Change                                      |
|-------------------|---------------------------------------------|
| `Mana.codex`      | Replace ManaColor/ManaCost/ManaPool with GemColor/LightCost/PrismaticState |
| `CardPool.codex`  | New card pool with RYB/OGP costs + Fluorescence |
| `Card.codex`      | Add Fluorescence clause to CardTemplate      |
| `GameRules.codex` | Screw/flood fixes adapt to gemstone counts   |
| `GameState.codex` | PlayerState gets ray count, fluorescence flag|
| `Turn.codex`      | Upkeep resets rays/stones/fluorescence       |
| `Engine.codex`    | Casting logic uses ray assignment + chains   |
| `server.ps1`      | PS1 card pool mirrors new structure          |
| `card-render.js`  | Gemstone symbols instead of WUBRG pips       |
| `collection.html` | Deck builder color filters                   |
| `magic.css`       | Gemstone symbol styles                       |

## Gemstone Varieties

Each color has many real-world gemstone varieties. The color determines
what mana a stone produces. The variety determines its properties:
rarity in packs, counter resistance, and special abilities.

### Color Families

Every gemstone belongs to a color family. All stones in a family
produce the same color of mana. The "basic" stone for each color is
Common rarity, unlimited in deck, no special ability.

| Color  | Basic Stone  | Hardness | Example Varieties (non-basic)           |
|--------|-------------|------|-----------------------------------------|
| Red    | Red Quartz  | 7    | Ruby (9), Garnet (7.5), Red Spinel (8), Fire Opal (5.5) |
| Yellow | Citrine     | 7    | Topaz (8), Yellow Sapphire (9), Heliodor (7.5) |
| Blue   | Blue Quartz | 7    | Sapphire (9), Blue Topaz (8), Lapis Lazuli (5.5), Aquamarine (7.5) |
| Orange | Carnelian   | 7    | Fire Opal (5.5), Sunstone (6.5), Amber (2.5), Padparadscha (9) |
| Green  | Aventurine  | 7    | Emerald (7.5), Peridot (6.5), Jade (6.5), Tsavorite (7.5) |

Diamond and Obsidian are their own families (no varieties in base set).

### Hardness Hardness -- Counter Resistance

When a spell is targeted by a counterspell, the hardness of the
gemstones used to cast it acts as a saving throw. Higher Hardness =
harder to counter.

Mechanic TBD -- possible approaches:
- Flat bonus to a counter-resistance roll
- Threshold: if total Hardness across casting stones exceeds N, counter
  fails
- Percentage reduction in counter effectiveness

This means upgrading your gem base (swapping Quartz for Sapphires)
makes your spells more resilient even if the mana color is identical.
Deckbuilding question: run cheap stones for consistency, or rare
stones for protection?

### Named Gem Abilities

Specific gem varieties can have unique triggered abilities when used
in a casting chain. These are like enchanted lands -- same mana, extra
effect.

Example concepts (not finalized):
- **Fire Opal** (Orange, Hardness 5.5) -- when used to summon a creature,
  that creature gets a +1/+1 counter
- **Star Sapphire** (Blue, Hardness 9) -- when tapped, scry 1
- **Amber** (Orange, Hardness 2.5) -- very soft but preserves: when a
  creature cast through Amber dies, return it to hand
- **Moonstone** (White/Diamond family, Hardness 6) -- doubles only at
  night phases in campaign mode

### Pack Rarity

Gem variety rarity in packs mirrors real-world scarcity:
- Quartz varieties (Common) -- baseline, easy to pull
- Named semi-precious (Uncommon) -- Garnet, Peridot, Aquamarine
- Precious gems (Rare) -- Ruby, Sapphire, Emerald, Topaz
- Exceptional specimens (Mythic) -- Star Sapphire, Padparadscha,
  Fire Opal, Alexandrite

This creates a gem economy: players trade for better varieties of
the colors they play. A Ruby and a Red Quartz both make Red, but
the Ruby is a strict upgrade.

## Card Types

### Gemstone (replaces Land)

Drawn from deck, played as permanents (1 per turn). Dual purpose:

1. **Mana production** -- stays on the field, taps to channel rays
2. **Socketing** -- permanently inserted into an equipment socket,
   removed from the mana field forever

See Color Wheel and Gemstone Varieties sections above.

### Creature

Summoned by spending rays through gemstones. Has combat stats
(power/toughness/defense), keywords, and equipment slots.

#### Equipment Slots

Every creature has 9 slots (Diablo-style):

| Slot   | Count | Accepts                             |
|--------|-------|-------------------------------------|
| Head   | 1     | Helmets, crowns, hoods, circlets    |
| Body   | 1     | Armor, robes, cloaks, vests         |
| Hands  | 1     | Gloves, gauntlets, bracers          |
| Feet   | 1     | Boots, greaves, sandals             |
| Ring   | 2     | Rings                               |
| Amulet | 1     | Amulets, necklaces, pendants        |
| Hand   | 2     | Weapons, shields, spellbooks, staffs|

**Hand slot rules:**
- One-handed items use 1 hand slot: swords, wands, daggers, shields
- Two-handed items use 2 hand slots: staves, greatswords, bows
- Combinations: dual-wield, weapon+shield, wand+spellbook, etc.

#### Equip Flow

- **Equipping is free** -- done during upkeep as creatures pick up
  and reassign gear
- **On creature death** -- all equipment drops to the battlefield,
  available for another creature to pick up next upkeep
- **Socketed gems stay in the item** -- they do not return to the
  mana field when equipment drops. The gem is permanently bound to
  the item, not the creature.

### Equipment (replaces Artifact)

Permanents cast by spending rays. Each equipment has:
- **Slot type** -- which creature slot it occupies
- **Stat bonuses** -- +power, +toughness, +defense, keywords
- **Socket** -- 1 gem socket per item (may be empty)

Equipment exists on the battlefield independently of creatures.
During upkeep, creatures pick up equipment. When a creature dies,
its equipment drops back to the field.

#### Gem Sockets

Each equipment item has 1 socket (base set). A gemstone from the
player's mana field can be permanently socketed into the item.

**Socketing rules:**
- Costs no rays -- done during upkeep alongside equip
- Permanent -- the gem leaves the mana field forever
- The gem stays in the item even when the item drops (creature death)
- If the equipment is destroyed, the socketed gem is destroyed too
- Socket bonus scales with the gem: color-based effect + Hardness scaling

**Socket bonuses by color (base values, scaled by Hardness):**

| Gem Color | Socket Effect                        |
|-----------|--------------------------------------|
| Red       | +power                               |
| Yellow    | +speed (Haste-like effects)          |
| Blue      | +resilience (counter/spell resistance)|
| Orange    | +power and +speed (blended)          |
| Green     | +toughness                           |
| Diamond   | Doubles the socket bonus             |
| Obsidian  | Socket grants Fluorescence to wearer |

Hardness hardness scales the magnitude: Quartz (7) gives +1, Topaz (8)
gives +2, Sapphire (9) gives +3. Exact scaling TBD.

#### Equipment Subtypes

**Weapons (Hand slot):**
- Swords, daggers, axes (1H) -- +power
- Greatswords, halberds (2H) -- more +power
- Wands, orbs (1H) -- +spell effects
- Staves (2H) -- +spell effects, bigger
- Bows, crossbows (2H) -- ranged keyword?

**Shields (Hand slot, 1H):**
- +defense, +toughness

**Spellbooks (Hand slot, 1H):**
- Spell-related bonuses, draw effects?

**Armor (Body slot):**
- +toughness, +defense

**Helmets (Head slot):**
- +toughness, keywords (Hexproof, Vigilance)

**Boots (Feet slot):**
- +speed keywords (Haste, First Strike)

**Gloves (Hands slot):**
- +power, combat keywords (Deathtouch, Lifelink)

**Rings (Ring slot):**
- Small bonuses, gem socket is the main value

**Amulets (Amulet slot):**
- Triggered abilities, protection effects

### Spells

Spells replace the Instant/Sorcery distinction with a speed-based
system. There is **no spell stack**. Spells either resolve instantly
or go through a single disruption window.

#### Spell Speeds

| Speed       | Disruptable? | When castable         | Power level |
|-------------|-------------|------------------------|-------------|
| Cantrip     | No          | Any time, including combat | Low      |
| Incantation | Yes         | Main phases only       | High        |
| Summoning   | Yes         | Main phases only       | Creatures   |
| Disruption  | No          | During disruption window only | Reactive |

**Cantrips** resolve instantly -- too quick for the opponent to
interfere. They are the safe option: combat tricks, small heals,
minor draw, buffs. Limited power as a tradeoff for safety.

**Incantations** are powerful but slow. When cast, the opponent gets
a disruption window. Major damage, board wipes, powerful effects.

**Summoning** is the act of creating a creature portal. Slow, gives
the opponent a disruption window. Same contest mechanic as
incantations.

**Disruption** spells are reactive cantrip-speed spells cast during
an opponent's disruption window. They attempt to fizzle the
opponent's slow spell through a contested Focus vs Disruption check.

#### Disruption Window

When a player casts a Summoning or Incantation:

1. Caster assigns rays through gems, declares spell
2. Opponent gets a **disruption window** -- may cast ONE disruption
   spell (assigning their own rays and gems)
3. **Contested check** -- Focus vs Disruption (see below)
4. Spell resolves or fizzles. **Done.** No further responses.
   One layer deep, no chaining.

#### Focus vs Disruption Contest

Both sides contribute gem hardness to a contested roll:

**Focus** (caster's side):
- Spell's base focus value
- Plus total Hardness hardness of gems used to cast
- Diamonds double the Hardness contribution in the chain

**Disruption** (opponent's side):
- Disruption spell's base power value
- Plus total Hardness hardness of gems used to cast it
- Diamonds double the Hardness contribution in the chain

**Formula:**

    disruption_chance = 90% + (D - F) * 4%, clamped [5%, 100%]

**Design targets:**
- Equal spend + equal Hardness = **90%** disruption chance
- Weakest disruption vs strongest cast = **5% floor** (1 in 20)
- Overcasting disruption by ~3 points = **100%** (guaranteed)

**Example scenarios:**

| Scenario | F | D | D-F | Chance |
|----------|---|---|-----|--------|
| Summon(5) via Sapphire(9) vs Thunder Clap(8) via Sapphire(9) | 14 | 17 | +3 | 100% |
| Summon(5) via Sapphire(9) vs Mosquito Plague(3) via Citrine(7) | 14 | 10 | -4 | 74% |
| Summon(5) via Sapphire(9)+Diamond(×2) vs Mosquito Plague(3) via Quartz(7) | 23 | 10 | -13 | 38% |
| Big summon(8) via Ruby(9)+Sapphire(9) vs Mosquito Plague(3) via Quartz(7) | 26 | 10 | -16 | 26% |
| Biggest cast possible vs weakest disruption | ~30 | ~8 | -22 | 5% floor |
| Equal everything | 14 | 14 | 0 | 90% |

**Strategic implications:**
- Disruption is strong by default -- casting is inherently risky
- Casters invest in better gems (higher Hardness) for protection
- Disruptors invest in better gems for reliability
- Guaranteeing disruption is expensive but possible (overcast by 3+)
- Even a desperate cheap disruption has a 1-in-20 shot
- Cantrips bypass all of this -- safe but weak

#### Disruption Spell Tiers

| Tier  | Example         | Base Power | Flavor                    |
|-------|-----------------|------------|---------------------------|
| Minor | Mosquito Plague | 3          | Annoying, cheap, low odds |
| Mid   | Blinding Flash  | 5          | Moderate cost, decent odds|
| Major | Thunder Clap    | 8          | Expensive, near-certain   |

### Enchantment

Persistent magical effects that consume resources continuously.

**Casting:** Incantation speed (disruptable). Assign rays through
gems as normal.

**Upkeep commitment:** The ray AND the gems used to cast the
enchantment remain locked (committed) for as long as the enchantment
persists. They do not refresh at upkeep. This permanently reduces
both the caster's ray budget and color capacity.

**Dropping an enchantment:** At upkeep, the caster may choose to
release any enchantment. The ray and gems are freed and become
available immediately.

**Dispelling:** When an opponent dispels an enchantment:
- The enchantment is removed
- The gems return to the caster's field (untap next upkeep)
- The ray is lost for that turn (already spent)

This means dispelling is not a pure win -- the caster gets their
gems back, potentially enabling a stronger follow-up cast.

**Strategic implications:**
- Early enchantments are very expensive -- turn 3 with 3 rays,
  locking 1 means only 2 for everything else
- Late game they're cheap -- turn 10, 1 locked ray barely matters
- Stacking enchantments drains fast -- 3 enchantments on turn 6
  leaves only 3 free rays
- Every upkeep is a decision: keep feeding or reclaim resources
- Dispel gives the opponent their gems back -- sometimes you don't
  WANT to dispel if it frees up their Sapphires

## Card Type Summary

| Old (MTG)    | New (Prismatic)  | Notes                          |
|-------------|------------------|--------------------------------|
| Land        | Gemstone         | Dual-use: mana or socket       |
| Artifact    | Equipment        | Slot-based, socketed, drops on death |
| Creature    | Creature         | 9 equipment slots              |
| Instant     | Instant          | Unchanged                      |
| Sorcery     | Sorcery          | Unchanged                      |
| Enchantment | Enchantment      | Unchanged                      |

## Resource Tension Summary

The game creates interlocking resource decisions:

1. **Rays vs Stones** -- two-axis casting. Run out of either and
   you're stuck.
2. **Mana vs Socket** -- every gem socketed is one less for casting.
   Early socket = weaker spells, stronger creature. Late socket =
   you had the mana when it mattered.
3. **Equipment persistence** -- gear outlives creatures. Invest in
   equipment and your board recovers faster from removal.
4. **Purple tax** -- always 2 stones per Purple mana, always
   stone-hungry, but powerful effects justify the cost.
5. **Fluorescence timing** -- spend 1 ray + 1 Obsidian early in
   the turn to boost all eligible spells, or save the ray for
   casting?
6. **Gem quality** -- Quartz is free but counterable. Ruby is rare
   but resilient. Fire Opal has an ability but low Hardness.

## Reserved Design Space

- **Prism** -- splits one ray into two weaker rays
- **Magnifier** -- triples instead of doubles
- **Lens** -- converts one primary to another
- **Fracture** -- destroys opponent's gemstones
- **Eclipse** -- blanket Fluorescence denial
- **Spectrum** -- a stone that counts as all three primaries
- **Multi-socket equipment** -- Mythic items with 2-3 sockets
- **Set bonuses** -- wearing matching equipment types (full plate
  set, mage set) grants bonus effects
- **Creature size limits** -- larger creatures get more/fewer slots?
- **Weapon enchantments** -- Instants/Sorceries that target equipment
- **Gem cutting** -- upgrade gem quality (Quartz → polished → faceted)
