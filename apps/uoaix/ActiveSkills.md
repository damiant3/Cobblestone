# Classic active skills

## Resting state

UNVERIFIED: val shelf35627 contains three draft chapters; compilation,
native replay, peer review and client grading have NOT RUN. Root ordered
wrap-up on 2026-10-05; no active-skill effect has landed or been enabled.

The requested live effects are Hiding, Detect Hidden, bandage Healing,
Anatomy, Arms Lore, Item Identification, Animal Lore, Tracking, Begging,
Snooping, Peacemaking and Evaluating Intelligence. Damian ruled that the
shard serves the installed 1.25 client: keep the existing 46-skill profile.
Meditation and Stealth are absent from its SKILLS.IDX/MUL and are dropped,
without replacement server commands. Mana uses classic passive regeneration.

Every admitted use must check requirements and roll skill gain through the
K skill core. Invalid targets, cancelled/replayed cursors and missing tools
must not practice or spend resources. Animal Lore on a player refuses with
a tongue-in-cheek line about people being capable of more than animal
behavior. Anatomy and Eval Int return classic qualitative tiers, never raw
numbers; RNG error decreases with the user's skill.

## Resume

Workspace/client: `D:/Projects/Cobblestone-val`, `BigWhite_Codex_val`.
Refresh inbox and depot state, then merge main by `PerforceProcess.md` before
restoring shelf35627. No code is open at this checkpoint. The reverted add
files were moved to `build-output/uoaix/active-skills/paused35627`; the shelf
is authoritative. Do not copy those backup files over a newer unshelve.

```powershell
p4 unshelve -s 35627 -c 35627
p4 sync
p4 resolve -am
```

Read the three chapters and relevant callers before continuing. Complete
the handler/effect/codec integration and create meaningful packet replay
controls before compiling; there is no ActiveSkills proof or opening yet.
Use the normal isolated focused gate, with explicit `seed/Codex.cdx`,
mandatory compile log and serial guest RAM check. Do not run the release
gate or full battery. UOAIX's no-poison/no-reader ruling applies. Blu reviews
the completed unit; hand fester handler and codec in the landing turn. Root
grades only fester's complete composite.

The last exercised seed was
`4228CD5103DC45232EB40C1FC9DE655BE2A805F7CAA5019E3EC76B28FF9216A8`.
No seed was changed. This identifies prior focused receipts, not a proof of
shelf35627. No build, VM or listener is left running by val. The val workplan
is empty.

## Draft contents and remaining work

| Chapter | Present on shelf | Still required |
|---|---|---|
| ClassicSkills | K `sk-buffer`/`sk-set` storage, Source-X bell/S success curve, separate deterministic success/gain rolls, attempt counter | Compile and validate; review draft gain policy and total cap with blu |
| ActiveSkillsState | Five player records, 46 skill cells each, starting/production baselines, target namespace4153, hidden-state queries, typed external-service contract | Handler, effect dispatch, timers, bank/target/LOS admission and provider binding |
| ActiveSkillsLore | Qualitative Anatomy/Eval Int with skill-scaled fuzz, Animal Lore player refusal, item/arms inspection draft | Native controls, authoritative metadata binding, LOS and disclosure review |

Draft policy values are a 50-per-thousand gain chance, 7000 total cap and
1000 per-skill cap. They are not a newly approved historical tuning claim.
The existing K `Skills` chapter covers 14 profession parents and 32
instruments with admitted deterministic practice; it has no classic active
skill vector or RNG seam. `ClassicSkills` is a proposed companion extension,
not a replacement for those instrument APIs. No caller uses it yet.

Hiding/reveal and Detect Hidden effects, bandage consumption/timers,
Tracking menus/arrows, Begging transfers, Snooping and Peacemaking are NOT
implemented. There is no ActiveSkills persistence codec, skill-list routing,
visibility hook or end-to-end test. The draft services record is only an
interface proposal; callbacks are not live implementations.

Keep resources authoritative: bandages must consume the selected physical
GvLot and matching quantity/quality/wear/basis in the economy atomically.
Begging transfers existing NPC coin through EuCurrency; no new rewards or
coins may be spawned. Peacemaking needs an actual musical instrument; no
instrument recipe/catalog change has been made. Snooping must not grant item
movement or bank access. Delayed actions and cursors must recheck ownership,
range, life and connection and obey the encompassing commit boundary.

## Current integration dependencies

- `GameSkillsItems.gsi-skill` contains the current unavailable-effect reply.
  GSI owns C12/subcommand24, C34 skills and its4753 target namespace. The draft
  active layer proposes a distinct4153 namespace. Preserve bank filtering and
  other packet-family target namespaces.
- Reek confirmed no GameClientView edit claim at main35629 and authorized
  visibility callbacks. Hiding needs actual viewer admission and the self80
  flag, plus server-side target refusal and reveal on admitted actions/damage.
  The current culler suppresses removal packets for still-existing mobiles;
  concealment must explicitly distinguish hiding from leaving view range.
  Reek continues to own GameNet/GameOpcode framing.
- Blu's Magery shelf35510 owns `MgActor` mana and poison (-1 means none).
  `mgs-ensure magic shard serial` returns `Result MgActor Text`; `mgs-track`
  retains timed work, and `mgp-mana` emits A2. Use that shared pool, not a
  second mana/poison model. Meditation was dropped after this API discussion.
  Cure and resurrection callbacks must bind the current private action
  candidate and never mutate a separately published state.
- Blu reports no active-gain seam in Magery: `mgs-skill` currently reads
  creation slots through `cb-skill`; `mgr-cast-skill` checks success only.
  Bind the completed shared ClassicSkills provider there with blu.
- Blu requested an economy review of shelf35510 `MageryResources`:
  `mgr-ready/debit/consume` binds exact backpack reagent lots. Blu reports a
  focused proof for stack consumption, proportional basis and census; val
  has NOT reviewed that seam. `Magery.md` owns candidate status and restart.
- Red is building `GameLiveActions*`, `TownNetworkLive*`, controlled TownLive
  movement and narrow Gv checkout reuse for live stage N. Root assigned val
  review/contract ownership. No review shelf was supplied before wrap. The
  existing EconomyActions currency refusal remains -17. Use real offers,
  exact serial lots and encompassing rollback; `TownLive.md` and
  `GameVendor.md` own the current contracts.

## Reference locations already inspected

Source-X commit `dd28a0ad53258adb55b548b1bd4df989065a1fe4`, Apache-2.0,
under `D:/Projects/uo-reference/Source-X`:

- `src/common/CExpression.cpp`: Calc_GetBellCurve/Calc_GetSCurve.
- `src/game/chars/CCharSkill.cpp`: Skill_CheckSuccess, Skill_Experience,
  Hiding, DetectHidden, Healing, Tracking, Snooping, Peacemaking and Begging.
  Source-X Begging has no successful transfer effect; use the reference
  fallback where that implementation is absent.
- `src/game/clients/CClientTarg.cpp`: OnSkill_AnimalLore, ItemID, EvalInt,
  ArmsLore and Anatomy.

ServUO `Scripts/Skills/Anatomy.cs` and `EvalInt.cs` supply the inspected
skill-scaled error shapes. `Begging.cs`'s ordinary-human branch consumes
existing gold; omit its modern reward-spawning branch. `Snooping.cs`,
`Peacemaking.cs` and `Tracking.cs` were also inspected. Preserve existing
reference licenses; port for the old ASCII client, not modern cliloc/gumps.

## Completed predecessor

The missing-door fix is MAIN35626 (dev35621), reviewed by blu. Host facing,
locked type, precedence and adapter checks passed; the native DecorationReplay
passed again with blu's indexed view and the trace-gated VM. The local install
artifact is `build-output/uoaix/doors/britannia.dwd` with its JSON manifest.
It adds 1200 door placements; five unmapped supplemental rows are reported.
Fester received the artifact and WDS1 identity/install contract. No derived
client data was put in the depot. Composite client grading remains root-owned.

Preserve paused civic-input shelf35382; development proofs previously passed,
but its final isolation/landing is NOT RUN and live work superseded it.
Its add-file backup is `build-output/uoaix/governmentinput/paused35382`.
Old pending34134 and unrelated MAIN edit34210 (`apps/landing/README.md`) are
not part of this unit. Do not delete or submit them during cleanup.
