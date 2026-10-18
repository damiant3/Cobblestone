# UOAIX: the 1998 skill system and blacksmithing

Owner: fester. Ruled by Damian 2026-10-12: "ok well, we need all this in uoaix, write the doc.  we do the code later
when i have tokens." "All this" is the June 1998 OSI skill-gain rules and blacksmithing as reverse-engineered by OUO,
which root modeled the same day. Status: stages 1 to 4 built (UOAIX-242).

## Rulings where UOAIX departs from 1998

Damian, 2026-10-12: "i don't like the skills economy feature at all, it made the game suck.  the forge and anvil rule
keep.  change for failures below 50.  skill gain below 50 should always be fairly easy.  yes add max durability
reduction on repairs, every time min 1, but make it scale with the current max for the item so a 255 durabilty item
loses 5 and a 127 loses 3.  powders need demand."

| # | subject | rule |
|---|---|---|
| D1 | Population | No skills economy: no usage counters, no rebalancing. Every character gains at one shard constant, `BASE_DELAY`. |
| D2 | `BASE_DELAY` | 1000, the 1998 rate of a skill used as often as the average (Damian, 2026-10-12, "yeah that's fine", accepting root's readings of D3 to D5 and this default). Pace scales about as 1 / `BASE_DELAY`. |
| D3 | Below 50.0 | Every check below 50.0 base skill gains, success or failure: SkillCheck runs with alwaysGain 1 (root's reading of "change for failures below 50" and "always be fairly easy"). From 50.0 the 1998 rule: a success only. |
| D4 | Smith's station | The 1998 rule: an anvil AND a forge within 3 tiles (root's reading of "the forge and anvil rule keep"; it replaces the 2026-10-10 anvil-only ruling). |
| D5 | Repair | Every attempt, success or failure, lowers the item's max durability by max(1, (max + 25) / 50): 5 at 255, 3 at 127, 1 at 74 and below (root's formula through Damian's two points). A success mends hits to the new max (1998); the chance is the 1998 repair check, difficulty (max - hits) * 1250 / max - 250, range 50. It replaces UOAIX-238's ServUO rule. |
| D6 | Powders | Powders of restoration need demand: the max lost on every repair is the demand, and NPC gear wears too, so a smith who repairs for NPCs (guards, fighters) spends powders and buys them (stage 4). |
| D7 | Caps | No total skill cap and no decay (Damian, 2026-10-12: "no skill caps, no decay."). 1998 has no 700.0 cap and decays unused skills instead; UOAIX keeps neither. |
| D8 | Stat cap | The 225 stat total stays (Damian, 2026-10-12: "stat cap stays."), though 1998 has none. |
| D9 | Resist | A resisted spell loses its whole effect, UOAIX-201's model (Damian, 2026-10-12: "resist should be full. leave it full."); not 1998's half damage by circle. |

No colored ores (UOAIX-240) stands; the 1998 smith menu reads metal of any ingot type, and iron is the only one.

## Sources

- `github.com/draxinar/ouo` (MIT): a C99 translation of the 1998 `UoDemo.exe` server, each function annotated with its
  binary address. The demo binary is the production server's Windows port; its data was extracted from production on
  1998-06-02 (OUO README). The live shard `uo1998.com` runs OUO.
- `github.com/draxinar/rundir`: the server data from `UoDemo.dat` (Origin's copyright, no licence in the repository):
  `skills.txt`, the Wombat scripts (`scripts.wombat/*.m`, decompiled), `itemres.mul`, `weapons/`, `tiledata.mul`.
- Neither repository is in the depot. Read them with `gh api repos/draxinar/<repo>/contents/<path> -H "Accept:
  application/vnd.github.raw"`, binaries with `Invoke-WebRequest https://raw.githubusercontent.com/draxinar/rundir/main/<path>`.
  `itemres.mul` is 16384 slots of a uint32 node count, then 33-byte nodes (three int32 values, a type byte, 16 bytes of
  padding, a uint32 resource id), all little-endian (OUO `load.c` LoadItemRes).
- `apps/uoaix/SmithModel.codex` (main 43575): the blacksmithing modeler built from both. It is the oracle for every
  number below and the reference arm for every stage's proof. Pinned case (`codex_run`, 2026-10-12): skill 92.4,
  strength 100, `BASE_DELAY` 1000, executioner's axe (difficulty 918): chance 526, 668 tenths gained per 10,000 swings;
  platemail gorget from 90.0: chance 81%, 495 per 10,000.

Function addresses below are OUO's annotations of `UoDemo.exe`. Skill values are tenths, 0 to 1000.

## The rules

### The checked skill value (GetSkillValue, 0x004D5FBB)

Each skill in `skills.txt` weighs strength, dexterity and intelligence (they sum to 100) and carries `SkillStat`.

    weighted = str * strWeight + dex * dexWeight + int * intWeight
    stat     = weighted * (100 - SkillStat) / 1000
    value    = skill * (1000 - stat) / 1000 + stat

Blacksmithy: strength 100, SkillStat 90, so value = skill * (1000 - str) / 1000 + str. Gain and the advance curve read
the BASE skill; the success chance reads the value.

### A skill check with a difficulty (SkillCheck, 0x004D52ED; CalcChance, 0x004D50A8)

A script calls `testAndLearnSkill(user, skill, difficulty, range)`, which is `SkillCheck(mob, skill, difficulty, range,
alwaysGain 0, gainPercent 100, divisor 1)`. A difficulty below -1000 succeeds, above 2000 fails, and a range outside
10..100 fails, all without a check.

    chance  = (value - difficulty) * 100 / range + 500
    success = chance / divisor - random(1000)        random(1000) is uniform 0..999
    on success > 0 (or alwaysGain): SkillGain(mob, skill, 1000 - chance, gainPercent)

At divisor 1 a player's check registers one use of the skill (D1) and every check tries a stat gain; a check by a mob
that passes the code's IsCreatureBody test is also shared, at divisor 4, with every mob within 1 tile that faces and
sees it.

### The gain (SkillGain, 0x004D5118; CalcDelay, 0x004D55D2)

    factor  = min(1000 - chance, 1000); no gain when factor <= 0
    rate    = CalcDelay(skill, base) * gainPercent / 100
    gain    = factor * rate / 1000                    thousandths of a tenth
    tenths  = gain / 1000, plus 1 more with chance (gain mod 1000) / 1000
    the skill stops at 1000

    CalcDelay = BASE_DELAY * AdvRate(base) / 1000     (D1: 1998 uses the usage ratio, clamped 500..5000)
    AdvRate(x) = B * A^x + C through (0, lower), (500, middle), (1000, higher) of the skill's AdvRate line

For AdvRate pick the root of the quadratic that is not 1.0: the binary picks 1.0 and every gain is NaN (OUO
`skill.c` Double3_Init, "FIXED"). Blacksmithy's AdvRate is 8000 2000 100: u = A^500 = 19/60, B = 360000/41,
C = -32000/41 (SmithModel: 1999 at 50.0, 99 at 100.0). OUO's floor of 1 on a truncated zero gain (its 1998-11-10
patch note) is kept: it changes nothing for blacksmithy from 50.0.

The roll is a real random draw, which the 1998 rates assume uniform. A player's `ep-practice-in` rolls a hash of the
character and its attempt count; an NPC economy actor still rolls `(attempts * 137 + 37) mod 1000`.

### A skill with no difficulty (DirectUse, 0x004D5F09; TestSkillInternal, 0x004D567D)

Hiding, Detect Hidden, Spirit Speak and the like: result = value - random(1000); gainFactor 100 on success, 50 on
failure. An anti-macro counter per mob and skill rises by 1 a use to at most 100 and decays with time since the last
use (reset after 900 ticks); a use gains only when the counter is at most random(100). The gain is
SkillGain(mob, skill, (1000 - base) * gainFactor / 100, 100), then a stat try and observational learning
(HandleWatchingSkill, 0x004D5916: players within 1 tile facing the user learn at the same factor, without stats).

### Stat gain (TryStatGain, 0x004D5A5D; CalcStatThreshold, 0x004D5A2C)

    value     = CalcDelay(skill, base) * gainPercent / 10000      a check at divisor 1, before its roll
              = gainFactor / 2                                    a direct use (TestSkillInternal)
    threshold = (100 - stat) * weight * value / 1000              each of str, dex, int; weight from skills.txt
    raise the stat by 1 when threshold > random(20000)

All three thresholds are computed before any raise. A raise that would pass 100 sets the step to 0 for that stat and
every stat after it in the try. StatAdvRate is read into the skill table and never used: CalcStatThreshold's advRate
argument is the dead GetStatReq. A check tries once whether it passes or not; UOAIX keeps its 225 total (ServUO
StatCap), which 1998 does not have. Blacksmithy (strength 100) at 0.0 tries at value 80, at 50.0 at 19, at 100.0 at 0.

### Blacksmithing (rundir `scripts.wombat/blacksmith.m`)

- Workstation: D4.
- Items: 49 menu items (ring, chain and plate armor, helmets, shields, blades, axes, pole arms, bludgeoning). Each item's metal is its template's required metal (`itemres.mul` resource
  44, value1). Its raw value is metal + speed * average damage / 12 for a weapon, metal + 2 * max AC for armor and
  shields; average damage is dice * (faces + 1) / 2 + bonus. The raw values are rescaled so the lowest item is
  difficulty 0 and the highest 1000. The table and the result are in `SmithModel.codex` (buckler 0, platemail 1000).
- Menu: an item is listed while difficulty <= value + 250 and the pack holds its metal.
- The attempt: `testAndLearnSkill(user, 7, difficulty, 50)`, after 1 to 5 hammer sounds.
- Success spends the item's metal and makes the item: success >= 600 is exceptional (HP, AC and damage x1.2), success < 300
  is poor (x0.8).
- Failure loses metal * (-success) / 1000 + 1 of the metal and makes nothing ("You lost some metal."). UOAIX today
  spends nothing on a failure (`GameCraft.md`, "Admission and production").

Model results (SmithModel, strength 100, `BASE_DELAY` 1000, 2026-10-12), 50.0 to 100.0: fewest swings 4,137 swings
and 50,118 ingots; fewest ingots 4,430 swings and 34,758 ingots (short spear to 80.1, platemail gorget to 83.5,
executioner's axe to 100.0).

### Armor defence (Damian, 2026-10-13: "yes, build armor AC to 1998 rules")

- OUO `combat.c` `Combat_CalcArmorAbsorption` (0x004452D8): a landed melee blow picks one of 14 armor groups at
  random (`armorGroupTable`, 0x00614FC0: helm twice, neck, chest five times, legs three times, arms twice, hands; each
  group lists up to four wear layers) and takes the worn piece rated highest in that group
  (`CMobile_GetBestArmorInGroup`, 0x00444434).
- `Combat_ApplyDurabilityLoss` (0x00444C75, damage type 1): the piece absorbs `GetRandomRange(rating / 4,
  (rating + 1) / 2)`, never more than the blow.
- `weapon.c` `CItem_GetArmorRating` (0x004DF86D) and `CItem_CalcAC_Inner` (0x004DFAE8): a rating is
  floor((hits / max hits + 1.5) / 2 * AC), 1.25 times the class when whole. UOAIX keeps no hits for clothing, so
  clothing rates as whole.
- Classes: rundir `weapons/stats` "Armor Class", name-matched to the 1.25 TILEDATA.MUL arts (`Armor1998`), clothing
  included; a name rundir defines twice takes its first definition.
- The smith band (x1.2 exceptional and x0.8 poor, `sm98-band-permille`) multiplies the class of a smith-made piece.
- Built: `Armor1998`, GameCombat `armor-hook` (bound to `cg-armor`, every combatant: players, guards, fighters and
  spawns), `bvt/BvtArmor1998`. The shield keeps UOAIX's parry (`cb-parry`). The status gump's armor value is still
  `GameArmor`'s (UOX3 classes); a creature's own AC (`CMobile_GetBonusAC`) is not modelled.

## What it replaces

- `SkillCurve.codex`'s gain (`skc-gain-at`, `skc-bonus`, `skc-uses`), replaced on every player path and the NPC
  fighter's swings; NPC economy actors (`ep-practice-in` with no row) keep it, because the EconomyClock proofs pin
  their emergent rates. The 700.0 total cap (`skc-train`) goes and no decay replaces it (D7); 1998 enforces no total
  cap (OUO `skill.c` CMobile_PostSkillGain, 0x004D60E7) and lowers under-served skills instead.
- `ep-range-chance` (ServUO recipe ranges) for crafts that have a 1998 script.

## Stages

| stage | state | where | proof |
|---|---|---|---|
| 1 Skill table, SkillCheck and SkillGain on every practice path | built | `Skill1998`, `Skill1998Data` (`import-skills1998.ps1`), `SkillCurve` `skc-learn`; casting is spelskil.m's testAndLearnSkill(Magery, circle * 1000 / 7, 40); combat is OUO `combat.c`'s swing and parry; crafts and harvests take the per-script checks in `Craft1998` | `bvt/BvtSkill1998`, `bvt/BvtCraft1998`, `proofs/MageryReplay` |
| 2 Direct use, anti-macro gate, watching, shared check at divisor 4 | built | `ActiveSkillsState` `as-roll` (every `testSkill` script and Spirit Speak direct; Forensics, Inscription, Stealing, Taming testAndLearnSkill); `GameSession` `gs-skill-share` and `gs-skill-watch` over the characters within 1 tile that face the user (slot byte 151, `gs-faces`), from combat (`cb-practise-check`), active skills, crafts and harvests (the economy's `learn-hook`, `cg-learn`), the smith's repair and a cast. Bystanders and watchers are present player characters; seeing is presence and an unhidden user, with no line of sight. The economy's direct uses (harvests, most crafts) have no anti-macro gate | `bvt/BvtDirectUse1998`, `bvt/BvtSkillShare1998`, `proofs/ActiveSkillsReplay` |
| 3 Stat gain | built | `SkillCurve` `skc-stat-try`, `skc-stat-value` | `bvt/BvtStatGain` |
| 4 Blacksmithing: menu, items, difficulty, metal loss, quality, anvil and forge (D4) | built | `Smith1998`, `ep-craft-98` | `bvt/BvtSmith1998` |
| 4 Repair (D5) | built | `ItemDurability` `id-repair`, `id-max-loss`; "repair" (`cg-repair-one`) | `bvt/BvtDurability` |
| 4 Powder demand (D6) | built | NPC gear wear, smith repair and powder purchase (val) | `bvt/BvtPowderDemand` |

Resisting Spells gains by 1998; the resist itself is UOAIX-201's attack against defence, and a resisted effect is lost in full (Damian, 2026-10-12: "resist should be full. leave it full.").
## Open facts

- No 1998 script makes jewelry (catalog items 162 to 165) or the scribe's pen (118): rundir `tinker.m` and
  `blacksmith.m` list neither. Their smithy recipes keep the pre-1998 chance under Blacksmithy, as farming does.

- The tiledata weapon id does not resolve against rundir's `weapons/` files (the dagger's points at Long Sword), so
  SmithModel matches weapon and armor stats by name. Axe, double axe and large battle axe match no definition; their
  rows take UOX3's (Damian, 2026-10-13, "yes" to another source): metal from `data/dfndata/create/smithing.dfn` ITEM 29
  (axe, lines 557-567, METAL 14), ITEM 32 (double axe, 590-600, METAL 12) and ITEM 33 (large battle axe, 601-611,
  METAL 14, art 0x13FB); damage and speed from `data/dfndata/items/gear/weapons/weapons_t2a/axes_t2a.dfn` `[0x0f49_t2a]`
  (lines 67-77, 6-33, spd 37), `[0x0f4b_t2a]` (79-89, 5-35, spd 37) and `[0x13fa_t2a]` (7-17, 6-38, spd 30). Difficulty
  786, 803 and 737; catalog items 208 to 210 (`Smith1998`, `SmithModel`).
- Observed on uo1998.com (Damian, 2026-10-12, strength about 100): 20 platemail gorgets from 90.0 gained 0.3;
  8 executioner's axes from 92.4 gained nothing; 5 platemail gloves from 92.4 gained 3 tenths. The joint likelihood
  peaks at 3x to 4x the `BASE_DELAY` 1000 rate (713 per million at 3x, 26 at 1x): the shard runs fast, by usage or a
  `-fast` switch. That describes uo1998.com, not a target for UOAIX; UOAIX's pace is D2.
