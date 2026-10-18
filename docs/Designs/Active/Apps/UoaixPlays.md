# UOAIX plays

The order (Damian, 2026-10-09): "i want to basically create roles necessary for creating plays, where bots play actor
roles and players can participate. we need scene design, character design, dialog, state machine, etc for a while
interactive, player interactive role play where the action is carried along by bots with player involvement somewhat
not gating minor progresses."

## End state

1. GM roles for authoring a play, beside the decorator (`UoaixDecorator.md`): scene design, character design and
   dialog, each a GM aspect in the GM grant (`GmContract.md`).
2. A scene: a place in the world, its set (decorator placements), its cast and its cues.
3. A character: a bot actor with a body, clothing (`CharacterStylist.md`), a name, a role in the play and its lines.
4. Dialog: lines a bot speaks and lines a player can answer with, tied to the play's state.
5. A state machine per play: states, transitions on cues (time, a bot's action, a player's speech or deed), and the
   bots' actions in each state.
6. Bots carry the action. A player can join, speak, act and change the outcome, but a minor step proceeds on the bots'
   own cue when no player acts; only a major branch waits on players, and a play says which steps are major.
7. A play is authored in the client and the admin page, saved with the world, and started, paused and reset by a GM.

## The model

A play is a **script**: plain ASCII text a GM writes, one statement per line. The script is the only authored form.
The admin page edits the script in a text box, the client authors it by bracket commands that append or replace
statements, and the server compiles the script into a play's tables when it is saved. The world saves the script's
bytes and the play's cursor, not the compiled tables, so a format change to the tables never needs a migration.

```text
play The Baker's Debt
mark door 1452 1618 0
mark boar 1475 1645 0
story bakery
scene market 1445 1610 1465 1650
cast nell "Nell" "the baker" townsfolk female at door
cast silas "Silas" "the gambler" townsfolk male at boar
set market 0x0E7C 0 1453 1620 0

state open minor
  silas walk door
  silas say "Nell! Forty gold, by sundown."
  nell say "I'll have it, I swear it on my oven."
  after 30 -> plea
  heard "help" "pay" -> offer

state plea major
  nell emote "wrings her hands"
  hint 90 nell say "Will no one help a poor baker?"
  given silas gold 40 -> paid
  heard "guard" -> guards

state offer minor
  nell say "You would help me? Pay him, and I'll bake you bread for a week."
  after 45 -> plea

state paid end
  silas say "Hmph. Paid in full."
  nell say "Bless you, stranger."

state guards end
  silas say "No need for guards! I'm going."
  silas walk boar
```

Statements:

| Statement | Meaning |
|---|---|
| `play NAME` | The play's title (first line). |
| `mark NAME X Y Z` | A named spot. `[play mark NAME` in the client writes one at the GM's feet. |
| `story NAME [stay]` | The story the play dresses (Stories, below); `stay` keeps it on after the play ends. One per script; without it the story is named after the play. |
| `scene NAME X1 Y1 X2 Y2` | A rectangle of the map: player cues count only inside a scene, and the scene is a set of the play's story with that area. A play has one or more scenes. |
| `cast ID "NAME" "TITLE" ROLE SEX at MARK` | A character: a bot mobile made when the play starts, dressed by the stylist's `ROLE` (`cst-*` roles by name), named and titled per the fully-attributed ruling, placed at `MARK`, blessed (cannot be harmed). |
| `set SCENE ART HUE X Y Z` | A set piece: an ordinary world item of the scene's set, standing while the story is on. The piece lies inside its scene. |
| `state ID minor\|major\|end` | A state. The first state is the opening. |
| `ACTOR say\|yell "TEXT"` | A line, spoken overhead to every player within 18 tiles that the actor can see. |
| `ACTOR emote "TEXT"` | An emote line (`*wrings her hands*`). |
| `ACTOR walk MARK` | Walk to a mark on the shared route planner; the action completes on arrival. |
| `ACTOR face MARK\|player` | Turn toward a mark or the nearest player in the scene. |
| `ACTOR give ITEM N` / `take ITEM N` | ITEM is a catalog item number or `gold`, `silver` or `copper`. The nearest living player within 3 tiles of the actor receives up to N units of ITEM from a lot in the actor's pack (`give`), or hands them over from a lot in the player's pack (`take`), by `ep-hand` between the cast member's economy actor and the player's, as theft moves a lot: a whole stack keeps its lot, a part opens a new one. Coin moves between the two actors' purses (`cg-play-coin`, as the mayor's donation pays); a short purse gives what it holds. No player in reach, or nothing to move, does nothing. |
| `wait N` | Pause the action list N game seconds. |
| `after N -> STATE` | Bot cue: N game seconds after the state's actions finish. |
| `heard "WORD" ... -> STATE` | Player cue: a player in the scene says any of the words (word-bounded, case-free, as NPC speech). |
| `near -> STATE` | Player cue: a living player enters the scene. |
| `given ACTOR gold|silver|copper|ITEM N -> STATE` | Player cue: a player says "give N gold" near the actor (the coin goes to the town purse), or drops at least N of catalog item ITEM on the actor: what the player dropped of its own lot, a whole stack or a part lifted off one, goes into the actor's pack and that much of the lot to the cast member's economy actor (`cg-play-keep`); an item with no lot is handed back. |
| `used ACTOR -> STATE` | Player cue: a player double-clicks the actor. |
| `hint N ACTOR say "TEXT"` | While a major state waits, the actor repeats the line every N game seconds. |

Rules the compiler enforces, refusing the script with the line number:

- **A minor state has an `after` cue** (the bots carry it on when no player acts). A **major state has no `after`**: it
  waits for a player cue, and it carries at least one `hint` so a player can learn what it waits for. An `end`
  state has no cues; reaching it finishes the play and turns its story off unless it stays, and the cast stands until
  `[play reset` removes it.
- A play has at least one scene, where players join it. Every cue names a state that exists; every actor and mark is declared; every state is reachable from the opening.
- Text is printable ASCII, at most 120 bytes a line (the 1.25 client's speech width); a script is at most 64 KiB, a
  deliberate content cap.

## Runtime

- **Cursor.** A running play holds: the state, the index of the next action, the game second it is due, the second
  the state's actions finished, the next hint second, and each cast member's mobile serial. Pause freezes the
  cursor; reset removes the cast and the set and puts the cursor before the opening.
- **Clock.** One wheel timer kind (`WorldTimers`) per play, keyed by the play's index, falls due at the cursor's next
  deadline; a play costs nothing between deadlines. Seconds are the town clock's game seconds, so the admin page's day
  length speeds plays as it speeds the town.
- **Cues.** Player cues are observed, never consumed: the play reads a C03 speech, an 0x06 double-click or a drop on a
  cast mobile on its way to the usual handlers, so a player talking to a cast member still gets the normal NPC
  answer as well. A cue counts only in the current state; the first matching cue wins; a state's own actions finish
  before its `after` clock starts, and a player cue may cut the action list short.
- **Actors.** A cast member is a server-made human NPC, a kind-1 world mobile with a `GsiItem` registration, dressed
  and named like every other NPC, walking with `rte-toward` on the town planner. Actors outside a covered map window
  hold position. Cast mobiles are not townsfolk. Each cast member of a running or paused play has an economy actor, bound at the
  growth point (`cg-cast-bind`, purse label 6000000 + play slot x 64 + cast index, found again by label after a restart),
  whose lots the vendor check accepts (`cg-wild-keeps`); it holds the goods `give`, `take` and a kept `given` move.
- **Persistence.** One new UCC1 section, `PLY1`: the story registry (stories, their sets' areas, on or off, and members), and per play its script bytes, its run state (stopped, running, paused,
  finished) and its cursor. A restore recompiles each script, re-binds the cast by serial and resumes the cursor.
- **Cost (R-COST).** A play's tables are built once per compile; nothing allocates per tick except the reply packets.
  A due play runs at most one action or one cue check per deadline; a speech cue is a scan of the current state's
  words (bounded by the script cap) only for speech inside a running play's scene.

## Authoring

- **Admin page:** a Plays tab lists every play with its run state and current state, shows the state graph as a
  table, and edits the script in a text box. Save compiles it and shows the compiler's refusal with its line, or
  stores it. Start, pause, resume and reset are buttons.
- **Client:** `[play` shows the selected play; `[play select 1-4`, `[play new` (clears its script), `[play line
  STATEMENT` (appends one), `[play mark NAME` (appends a mark at your feet), `[play sample`, `[play start`, `[play pause`,
  `[play resume`, `[play reset`, `[play show`; `[story`, `[story on NAME`, `[story off NAME`. A long script is easier in
  the admin page. `[play gump` opens the authoring gump: a button per command (select 1-4, sample, start, pause,
  resume, reset, show); a press runs that command as if spoken, under the same GM gate, and the gump comes back. Gumps
  draw only with art the 1998 GUMPIDX holds (backing 2600, buttons 2151/2152 and 247/248; 5054 and 4005 are absent).
- **Director's view** (Damian, 2026-10-09: "loading a play should auto [go there"; "i should also be told, as play
  director, when the actors will show up, since it is night, there where are they?"): `[play sample` compiles the
  sample, and it and a `[play start` that puts the cast on stage move the GM to the centre of the play's first scene,
  as `[go` does. Sample, start and `[play show` list every cast member: on stage, where it stands; before the start,
  the mark it enters at. The cast enters at its marks the moment the play starts, at any hour.
- **GM aspects:** three grant bits, `scene-design` (marks, scenes, set), `character-design` (cast) and `dialog`
  (states, lines, cues); running a play needs any of the three. The bits take the next free values after the
  decorator's.

## Stories: decoration sets a Director switches

The widening (Damian, 2026-10-09): "it would be cool if we could bundle up decorations bounded by map areas and grouped
into stories, so like we could restore an area to normal when a play has ended. and those could be managed like
independant units a "Director" level staff member could turn on and off in the live world."

- A **set** is a named bundle of decorator-placed world items inside one map area (a rectangle), saved with the world.
  A decorator adds an item to a set as it places it, or gathers every unbaked item in an area into one.
- A **story** is a named group of sets, and a play names the story it dresses; the play's `set` statements become a
  set in its story.
- A set is **on** (its items stand in the world) or **off** (they are removed, and the area shows what the shard's
  statics and land show, its normal state). Turning a story on or off turns every set in it. A play's end, or its
  reset, turns its story off unless the story is marked to stay.
- A set holds world items only, never baked statics or land edits: those reach the client through the patched client
  files at a downtime and cannot be switched live. A decorator who bakes an item takes it out of every set.
- A play can span **more than one area** (Damian, 2026-10-09: "there might be more than one area per play too"): its
  story holds one or more sets, each with its own area.
- **Conflicts are refused when an area is defined**, not at playtime (Damian, 2026-10-09: "we should lookout for
  conflicts at the time the bounded regions are defined, so we don't have two plays competing for the decoration set
  at playtime"). Defining or moving a set's area refuses, naming the other set and its story, when the area overlaps
  any set of another story; sets of one story may overlap. A story's sets are owned by that story alone, so two plays
  never switch the same items.
- The **Director** is a GM aspect, a grant bit after the play aspects: lists stories and sets with their areas and
  state, and turns them on and off, in the client (`[story`) and on the admin Plays tab. `[story gump` shows the first
  16 stories as buttons; a press switches that story the other way (as `[story on|off NAME` spoken) and the gump returns.

## Stages

| Stage | Built | Grade |
|---|---|---|
| 1 | Script compiler: statements, tables, the validation rules | BVT pairs: a valid play compiles; each rule refuses with its line |
| 2 | Runtime: cast made and dressed, say/emote/walk/face/wait, `after`, `heard`, `near`, `used`, `hint`; `[play` start, pause, reset, show (British only) | the sample play above runs in a booted server with no player and finishes by its minor steps alone until the major state, which waits and hints |
| 3 | Stories: the set and story registry (area, members, on or off), switching a story on and off by placing and removing its members, the overlap refusal when an area is defined, `story` and rectangle `scene` in the script, a play's start turning its story on and its end or reset turning it off unless it stays; `[story` list, on, off (British) | a story switched off restores its area; a second play whose scene overlaps another story's set is refused naming that set |
| 4 | `PLY1` save and restore of scripts, cursors, cast and the story registry | a restart mid-play resumes the state with its story on |
| 5 | `given ACTOR gold|silver|copper N`: a player in the scene within 4 tiles of the actor says "give N gold" (the mayor's donate form) and the coin moves to Britain's town purse (built). `given ACTOR ITEM N`: a player in the scene drops at least N of catalog item ITEM on the actor, which looks it over and hands it back, so the economy is untouched (built). Built: an economy actor per cast member (`cg-cast-actor`). Built: `give` and `take` of catalog items (`cg-play-trade`, the `PlrStore` trade hook; the cast mobile gets a plain pack, `cg-play-pack`). Built: a kept `given` for a whole stack or a part lifted off one (`cg-play-keep`, `cg-play-split`). Built: coin in `give`/`take` (`cg-play-coin`, coin encoded as 0 - metal). The player is told at once (`cg-tell`). Plan as built: `plr-act` sees only the `GameShard`, so `PlrStore` gains a trade hook the composite sets at install (slot, cast index, kind, item, N); the hook finds the nearest player within 3 tiles of the cast mobile, and `give` moves N units of ITEM from a lot in the cast mobile's pack to the player's pack, `take` the reverse, both by `ep-hand` between `cg-cast-actor` and the player's `gv-player` actor (the `as-steal-lot` transfer, a whole stack keeping its lot, a part opening a new one); a kept `given` does the same on the drop. The cast mobile needs a pack (as `tl-pack` gives residents); a short `give` gives what the actor holds and logs the shortfall | a player's 40 gold to Silas branches to `paid` |
| 6 | Admin Plays tab: the plays, a script editor that saves and compiles, start, pause, resume, reset, and the stories switched by name (built) | Damian authors a play in the page |
| 7 | GM aspects: scene design, character design and dialog run `[play`, Director runs `[story` (built). Built: the authoring gump, `[play gump` (`cp-play-gump`, `cpy-gump-answer`) | a dubbed GM authors and runs a play; a Director switches a story |

The record a decorator writes when it places an item into a set (blu) is `sty-add-member`: the set's registry row,
the item's art, hue, x, y and z, and the placing account; while the set is on the record also holds the item's live
serial. The registry refuses a member outside its set's area.

## Owner

fester designs and builds the play model; the authoring gumps reuse the decorator's menu and gump work (blu).
