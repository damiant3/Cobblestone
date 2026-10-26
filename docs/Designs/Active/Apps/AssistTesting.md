# Assist testing

Damian, 2026-10-22: "USE ABSOLUTELY NASA LEVEL SOFTWARE ENGINEERING PRINCIPLES. I WANT FULL TEST COVERAGE. I WANT EVER
BOUNDARY TESTED." and "IF YOU USE A PROTECTED ACCESS LEVEL YOU BETTER BE PREPARED FOR A RIGOROUS DISCUSSION OF
ENCAPSULATION".

This campaign gives every line and every branch of the Assist (`apps/uoaix/assist`) a test, and every limit a test at
each side of it. It runs beside `AssistStructure.md`: a
component that campaign moves lands with its suite here.

## Principles

1. **Every requirement traced.** Each rule in `assist-plan.md`, `tailoring-circuit.md` and the rulings of
   `uoaix-init.md` carries an id (`R-ASSIST-<n>`), and each id names the tests that hold it. A rule with no test is an
   open item in this doc, never a silent gap.
2. **Every boundary.** Every numeric limit (a spacing in ms, a purse, a carry limit, a retry count, a journal size, a
   stock of 0, 1 and 999) is tested one below, at, and one above. A limit without three cases is uncovered.
3. **Every fault.** Every action is tested against each answer the server can give: accepted, refused (each refusal
   text the server sends), silence until the timeout, the link closed, the stop pressed, the player taking over.
4. **Deterministic.** No test reads the wall clock or sleeps: time is a `VirtualClock` the test advances. A test
   passes or fails the same way on every run.
5. **Measured coverage.** Line and branch coverage are measured by our own instrument (below), per file. A file a
   stage has finished is at 100% line and 100% branch, or lists each exempt line with the reason Damian accepted.
6. **One implementation per capability.** Walking, doors, recall, the bank, the purse, a vendor purchase and the
   spacing of actions exist once, and their suite is written once. A second copy found is merged before its tests are
   written.
7. **Encapsulation.** Every class is `sealed` or `static` (`assist-csharp.md`); behaviour is shared by composition and
   interfaces, never inheritance. No member is `protected`. A test reaches a component only through its public
   contract, never through reflection or a widened access level.
8. **Every live failure becomes a case.** A defect seen live is reproduced as a failing test before it is fixed.

## Instruments (stage T1)

| instrument | what it is |
|---|---|
| `IClock` | `UtcNow` and `Sleep(ms)`; `SystemClock` live, `VirtualClock` in tests (a sleep advances the virtual time and wakes every waiter due) |
| `FakeShard` | a scripted server on in-memory streams that speaks the 1.25 wire (keystream from the client, Huffman to it) through a real `GameLink`; a script answers each client packet or stays silent |
| `test-assist.ps1` | compiles the Assist and the suites as one assembly, runs every case, prints `PASS`/`FAIL` per case and the counts, exits 1 on any failure |
| `cover-assist.ps1` | a Roslyn rewriter that puts a probe on every statement and every branch arm of a copy of the sources, runs the suites on that copy, and prints each file's line and branch coverage and every unhit line |

`GameLink` takes `Stream` in place of `NetworkStream` for both sides; the relay passes its network streams unchanged.

## Stages

1. **T1, instruments (built).** The four above. `coverage/test-cover.ps1` is the instrument's own control: a fixture
   whose untaken arms are listed by hand in `coverage/fixture/expected.txt`, compared both ways.
2. **T2, the send layer.** Built: `GameLink`, `UoWire`, `WireTables`, `Clock` and `BytePipe` at 100% line and branch,
   and the spacing gate (`GameLink.ActionGapMs`, 1100 ms, measured over the relay logs: a double-click or a lift goes
   no sooner after the last one either side sent; a hold handler on the client's thread sends at once). `AssistFeatures`
   runs on `AssistFeatures.Clock` (every read of the time, sleep, event wait and `Wait`), its threads actors under a
   virtual clock; `RecallTests` drive the real features against the fake shard (a scratch data folder, never the
   player's). `Recall` reads the server's own lines only and keeps waiting for its cursor once the player's words came.
   Every other answer reader (Teleport, Mark, the loop's "again",
   mining, smelting, inscription, work stations, crafts, the door, the key, the cure, the cast's re-arm, eating, the
   vendor's total) reads `Heard.Answers` (`AssistState.AnswersBetween`: every line but a label and the player's own
   words, so a line barked over an item or said by an NPC still answers), as do `Heard.Last`, `Find` and `Has`
   (`JournalTests`); `GameLink.IsAnswer` already took only the system's and the player's own speech. `AssistState`
   at 100% line and branch (`tests/AssistStateTests.cs`). The
   features' parts run on their clock: `AssistState` (the link's), `AssistAtlas`, `SmithBook`, `AssistTimings` and
   `TripRecorder` (the features'); the wall clock is read only by the relay host (`Assist.cs`), the window, and
   `SystemClock`.
3. **T3, Keep thread rebuilt on the shared components.** Travel (walk with doors, recall, the bank), the purse sized
   from the asked vendor's expected stock so that one purchase buys the whole stock, and the vendor purchase, each
   merged to one implementation, then Keep thread composed from them. Until then Keep thread runs only with Damian
   watching, after a bounce he calls. The cases, each found by name:
   - Built (`KeepRules.cs`, `tests/KeepRulesTests.cs`): the purse (Damian, 2026-10-22: "this npc has to know how much
     gold to carry around to pickup the full amount of all the threads in single vendor in a single purchase") sized
     for every vendor of the visited city bought whole (`PurseFor`) and kept through every bank stop (`PackKeeps`); a
     dry visit no longer moves a vendor's buy-out; the order by `DueByPhase` (`tailoring-circuit.md`, "How a 1998 vendor
     restocks"; a stock seen under 10 minutes after a buy-out is the same buy-out; dry visits bound nothing, other
     players buy vendors out too).
   - The trip: one rune chosen once, a failed cast classified and the trip ended (built in `ToShop`; `Travel` still
     falls back from a carried rune to the bank's); `TripReady` once a hop; no bank bag sweep
     (`BankTree` fresh) when nothing is needed; a stop, a takeover or a disabled assist ends every layer and is never a
     vendor failure (`SkipVendor` on a stop marks a vendor BROKEN after three).
   - Doors, built (`UseDoor`, `DoorShut` and `DoorOn` in `AssistFeatures.Walking.cs`; `tests/DoorWalkTests.cs` over
     `tests/StepShard.cs`, the fake shard that answers steps and door uses): a shut door on the next tile is opened
     before the step onto it, and the same method serves a refused step. TILEDATA flags a shut leaf and an open one
     alike, so the state is read from the art: the 1.25 client's 302 door-flag arts lie in sets of 16 from 0x00E8,
     0x0314, 0x0324, 0x0334, 0x0344, 0x0354, 0x0675, 0x0685, 0x0695, 0x06A5, 0x06B5, 0x06C5, 0x06D5, 0x06E5, 0x0824,
     0x0839, 0x084C, 0x0866, 0x1FED, and the pair at 0x190E (TILEDATA scan, 2026-10-23), shut at an even step past the
     base. An art read wrong costs one refused step, the refusal's own use opening it. An open leaf the walk did not open,
     refused on its tile, is never used, and no refused tile holding a door is remembered as a wall. Uncovered: the
     locked house's door (needs a test multi with door parts), two doors beside one refused tile, and the 4096-wall
     clear.
   - One implementation each, merged: the pack's runes (`PackRunes`: every pack picker, `PackRuneFor`,
     `CarriedShopRune`, `RecallNearer` and `RecallNearBank`, sees the pack through it, each label read once and kept, and
     `ShopRune` is gone into `CarriedShopRune`); the walk to a town's bank (`ToCityBank`, its five copies removed); the
     purse fill (`PurseTo`, eight copies, each caller naming its own target); the recall reagent count (`RecallRegsUnder`
     and `RecallRegsLow`; the three thresholds stay apart, as each answers its own question: `TripReagents` 4 to travel,
     `RecallLow` 10 to warn and make the bank due, `ReagentRefill` 30 to fill). Still two: the bank box's rune choice in
     `BankRecall` (its own ranks over the box), and the bank walk that records the bank's answer (`BankRecall`,
     `BuyBank`). Suites: `tests/PackRuneTests.cs`, `tests/PurseTests.cs`.
   - The whole lap (Keep thread through `CircuitKeep`, the only public way to `StashThread` and `FetchStashes`): the
     fake shard's pieces built are `tests/StepShard.cs` (steps and doors) and `tests/BankShard.cs` (the box, its bags,
     lifts and drops; `tests/PurseFillTests.cs`, run over `TestMap`, whose tile data makes a bag a container and gold
     stack: with none, no bag in the box opens and a savings case passes with the savings rule broken), and
     `tests/VendorShard.cs` (a vendor in view wearing its stock box: the 0x3C stock, the 0x74 list in the reverse of the
     stock's order, the 0x24 buy window, the client's 0x3B answered by the total, the goods and the close;
     `tests/VendorBuyTests.cs`: the anonymous "vendor buy", and a vendor named in view and in hearing, its label sent
     before it is drawn so the atlas has its name; one 10 tiles off walked to within hearing first, the shards' `Answer`
     chained on one link; one asked away from its shop answering "Let us go back to my shop." waited for at home and
     asked once more, over a test region file unloaded after). `tests/KeepLapTests.cs` drives Keep thread through
     `CircuitKeep`: no vendor ends it at once; a town with one CLOTHIER shop (tailor shops are the 1998 file's CLOTHIER
     and WEAVER regions) holding the player and Morena buys her stock on the first lap; the vendor due soonest is waited
     for until it is due (`KeepDue`, L-FUTILITY); with the tailor runes bag set (settings key `runes.shop`), a tailor in a shop it holds no rune for is
     never asked; a tailor in another town is reached by the rune labelled for her shop, cast once; a stone past the
     carry limit after the buy puts 10 thread down beside the player (no client map, so no shop spot), and at the limit
     nothing goes down; over `TestMap` and `StepShard`, the stash walked to the shop's most hidden spot, a spot whose
     drop comes back refused and the next taken, and room after the stash lifting it back at the lap's end.
4. **T4 on.** Each stage of `AssistStructure.md` (hands and inventory, the task contract, travel and vendors, the
   circuits, the window) lands its components with their suites at 100%.

## Open

- No rule carries an `R-ASSIST-<n>` id yet (principle 1).
- The window (`AssistWindow.cs` and its `AssistWindow.<Tab>.cs` parts, WinForms): the logic behind each panel moves into tested classes; whether the control
  code that remains is exempt from the coverage gate is Damian's ruling.
