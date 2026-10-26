# Assist structure: SOLID, DRY and class design

The UOAIX Assist (`apps/uoaix/assist`) is to be rebuilt as classes with one responsibility each, small interfaces,
dependencies on abstractions, and one contract every task honours. Owner: blu, solo (Damian, 2026-10-24: "reek needs
be told nothing, you can work solo"). A stage that moves circuit code keeps every `Decide` point of the circuit brain
(`AssistCircuitBrain.md`) and its legal guard as that design defines them.

## Rulings

- Damian, 2026-10-21: "you need to do a SOLID and DRY and basic OOD campaign on this, with proper class design including
  information hiding, interface segregation, dependency inversion, and of course single responsibility and our hero
  Liskov".
- "because are getting whackamole pushmepullme bugs here"; "this is cray what you built but its a damn tangled mess".
- "you should follow the project in old\ the compiler written in c#"; "import the whole c# code guidelines into this
  assist project and them apply them": `apps/uoaix/assist/assist-csharp.md`. Each stage brings the code it moves to
  those guidelines, `Map` and `Set` in place of `Dictionary` and `HashSet` included.
- Behaviour is kept: every stage is a move, not a change of what the player sees, except where a stage names one.

## The tangle

Counts are occurrences of the name in the source, measured 2026-10-21 after stage 1's move.

- `AssistFeatures` is one class of 14,571 lines (2026-10-24), a `partial` class in `AssistFeatures.cs` and 29
  `AssistFeatures.<Section>.cs` files (Damian 2026-10-23: "break up that 13k+ file into subfiles of managable length");
  the files split it by section, its state is still shared by every one.
- One settings store (`Cfg.`, 223 occurrences) is reached from every section.
- Across the Assist's 151 files (2026-10-24): 41 lines over 120 columns (inline lambda bodies and chained
  assignments), all to go. No `Dictionary` or `HashSet` is left; the walk planner's tables are arrays over a dense
  state index (`PlanGrid`). The rules a compiler can apply without changing a byte of IL are applied to
  every file: explicit types for `var`, `new()`, no `this.`, default accessibility omitted, `m_`, `s_` and PascalCase
  on private fields, Allman braces and one statement a line, braces on a multi-line body, expression-bodied properties
  and accessors, the file-scoped namespace `Uoaix.Assist`, every public static value a property over its `s_` field, every static readonly list an `ImmutableArray<T>`, `#nullable enable` (a `?` where a value can be null,
  a `!` where the code assumes it is not: 789 of them, each a place to check).
- Three stop flags (`stop` for loops, `circuitStop`, `workStop`) and seven hand-started threads, each task with its own
  status string and its own end handling.
- The window (`AssistWindow.cs` and a partial `AssistWindow.<Tab>.cs` per tab) reads 250 members of `AssistFeatures`
  and 63 of its statics (2026-10-21).
- Timings: one shared `busy` fed 25 waits in 19 functions, so a fix for one task moved others. The per-task busy now
  rides on a `[ThreadStatic]` task name (a hidden global), and the timings popup's action list is a second hand-kept
  copy of what each section shows.

## Target design

| class or interface | responsibility | replaces |
|---|---|---|
| `ISettings` / `ServerSettings` | key-value store, the server prefix applied once | `Cfg` with `Server +` at 169 call sites |
| `TimingCatalog` | the timing definitions (key, name, group, default, text) | the `Timings` table inside `AssistFeatures` |
| `ITimings` | read and set a timing, the shared fallback, the change log | `Ms`, `SetMs`, `LogTiming` |
| `TaskTimings` | one task's view of the timings: its own busy key, its list of keys | `[ThreadStatic] busyTask`, `ActionTimingSets` |
| `IHands` | one action at a time on the game link: use, lift, drop, target, busy waits, verified put-down | `actionGate`, `BusyLift`, `MoveStack`, `PutDownBeside`, `Answered` |
| `IInventory` | what the player carries and stands by: pack tree, counts, weight, carry limit, ground items | `PackCount`, `PackTree`, `LargestCarried`, `ItemOrGround`, `WeightNow`, `CarryLimit` |
| `ITravel` | walking, recall, runes, the bank stop | `WalkTo`, `Recall`, `ToShop`, `RecallNearBank`, `BankHere` |
| `IVendors` | asking, buying, selling, the vendor records | `VendorBuy`, `VendorSell`, `AskNear`, the attempts and circuit files |
| `ITask` | the one contract for every task (below) | the loops, circuits, weave, pickup, harvest, loadout, watch me sell |
| `TaskRunner` | starts a task on its thread, the one stop, the one status, the end note | three stop flags, seven thread starts |
| `TaskRegistry` | every task by name; the window and the timings popup list from it | `TimingsFor` switches, `ActionTimingSets`, `WorkTimings` and the other lists |
| one class per task | a loop kind, each circuit, weave, pickup, harvest, loadout | the sections of `AssistFeatures` |
| `UoData` files | tile, art, gump, animation and radar readers | the readers once inside `AssistWindow.cs` |
| window panels | one panel per task, bound to `ITask` and `TaskTimings` | the window's reach into 250 members |

### The task contract (Liskov)

Every `ITask` is substitutable for any other wherever the runner, the window or the brain holds one:

- `Name`, `Skill`, and `Timings` (its `TaskTimings`).
- `Run(TaskContext)` returns why it ended; it never blocks the window's thread.
- It stops when the context's stop is set, at its next step, and says "stopped"; no task has a stop of its own.
- It reaches the game only through `IHands`, `IInventory`, `ITravel` and `IVendors` from the context, never through
  the link or the settings directly (dependency inversion).
- It reports through the context's status and notes; it holds no static state.

A task that cannot meet one of these is not an `ITask` and is named as a gap in this design, not special-cased in the
runner.

### Primitives (Damian, 2026-10-21: "yes on the Outcome, and the rest")

Each replaces a pattern written out by hand in `AssistFeatures.cs` (occurrences, 2026-10-21).

| primitive | what it is | replaces |
|---|---|---|
| `Outcome` | a `sealed record`: done, or stopped with the reason (refused, timed out, stopped, closed) and the server's words | the "null, or why it stopped" string most steps return |
| `Wait.Until(done, ms, stop)` | polls `done` until it holds, the time runs out, the task is stopped or the link closes; returns an `Outcome` | 44 deadline loops, 78 deadlines, 152 bare `Thread.Sleep` calls |
| the task's stop | one `CancellationToken` in the `TaskContext`, the only stop any task or helper tests | `stop`, `circuitStop`, `workStop` (231 tests; the wrong flag tested in a shared helper is a recorded pitfall in `uoaix-init.md`) |
| `using (hands.Act())` | an `IDisposable` scope: takes the action gate and waits out the previous action's answer | `lock (actionGate) { Answered(k, AnswerMs); ... }` (25 copies, 91 gate locks) |
| `using (Journal heard = link.Listen())` | an `IDisposable` scope: what the server said from the scope's start | 40 `JournalSeq` marks read with `JournalSince` |

The token arrives with the task contract (stage 3); until then `Wait.Until` takes the stop flag the caller tests
today.

## Stages

A stage, in order: it compiles (the `Add-Type` line of `assist.ps1`, run in a fresh `pwsh`); it lands on main; the
relay is bounced (the running `assist.ps1` process stopped by its PID and started again with the same arguments, which
loads main's code; Damian logs back in through it); the tasks it moved run live. The next stage may begin once the
bounce is done; a moved task that has not yet run live is listed as unproven in the landing report until it has.

1. **Timings.** `TimingCatalog`, `ITimings` and `TaskTimings` out of `AssistFeatures` into `AssistTimings.cs`, and
   `Map`, `ValueMap` and `Set` into `Collections.cs`; every
   section's list of keys is its `TaskTimings`, and the popup's groups (task, skill, general) are computed from those,
   not a second copy. The `[ThreadStatic]` task name stays until stage 3 gives each task a context to carry its
   `TaskTimings`.
2. **Hands and inventory.** `IHands` and `IInventory` out of `AssistFeatures`; every lift and drop checks its own result
   (a refused drop is the case). `ITools` beside them: a toolbox and a toolbag per profession, the bank toolbag
   dissolved (`AssistTools.md`). With them the primitives above; every method a stage moves from here on uses them, and returns an `Outcome` where it returned a reason
   string. Built: `Hands` (`Hands.cs`, `tests/HandsTests.cs`) holds the gate's scope, the answer and busy waits, the
   lift judged by the server's refusal, the refusal record, and `Put`, the lift and drop checked against where the item
   lay (an item still there after the answer was put back); `AssistFeatures` reaches it through members of the old
   names (`Answered`, `Lifted`, `BusyLift`, `BusyWait`, `MoveStack`, `clickedAt`), so every `MoveStack` drop is checked.
   A drop or equip sent by hand after its lift is checked by `Landed` (disarm, arm, the drink's put-away and re-arm,
   the harvest's pickup) or by a check of its own (the spellbook fill's scroll count, the bag fill's count, the bolts'
   `BoltLeft`, the smelt, the feeding's answer, `LumberEquip`). Every entry to the action gate is `Act` or, for a background
   loop that skips its turn, `TryAct`. Every journal mark is a `Heard` (`Lines`, `Server`, `PlayerSpoke`, `Last`,
   `Find`), but two reads of the line before the newest (`TailorRun`'s quality, `People`'s last line). Every wait that only polls is `Wait.Until`, or
   `Wait.Till` for one to a due time; the 24 deadline loops left (2026-10-23) act inside their bodies, `AwaitCursor`
   among them, whose `ended` callbacks can dump ore and must not run past the deadline. The loadout's moves are judged
   by the bag's count (`LoadoutLanded`): a part lift's remainder keeps its serial where it lay, so the place check cannot
   judge them.
3. **The task contract.** `ITask`, `TaskContext`, `TaskRunner`, `TaskRegistry`; the loops first, then weave (`Work`),
   pickup (`PickupLoop`), harvest (`HarvestLoop`), loadout (`LoadoutRun`) and watch me sell (`WatchSell`); one stop and
   one status; the `[ThreadStatic]` task name replaced by the context's `TaskTimings`. Built (`TaskContract.cs`,
   `tests/TaskRunnerTests.cs`): `ITask`, `TaskContext` (its `TaskTimings` and one `CancellationToken` stop) and
   `TaskRunner` (one task at a time, its thread, its stop, its end); the loops run as a `LoopTask` on the loops' runner,
   whose stop is the loops' `stop` (while none runs, `idleStop`: set by a login, a death and a stop, cleared by a
   loop's start and a manual action); mill, spin and weave runs as a `WorkTask` on the weave's runner, whose stop is
   `workStop`; the reagent loadout runs as a `LoadoutTask` on its own runner (a second start refused). Still to move:
   the loop and weave bodies onto the context (they read `stop` and `workStop`), watch me sell (its stop is
   `circuitStop`, which `VendorSell` tests), pickup and harvest (background loops that run from the features' start to
   `Shutdown`), `TaskRegistry`, one status, and the `[ThreadStatic]` task name. The one stop needs every shared helper
   to test the running task's stop rather than `stop`, `circuitStop` or `workStop` (135, 203 and 6 occurrences,
   2026-10-24).
4. **Travel, bank and vendors.** `ITravel` and `IVendors`.
5. **Circuits.** Each circuit its own `ITask` class, the brain's decision points kept.
6. **Window.** The UO data readers and `FieldGrid` to their own files (6a: `UoTiles`, `UoArt`, `UoGump`, `UoAnim`,
   `UoRadar`, `MapView`, `FieldGrid`, `SortList` moved; 6b: `AssistNative`, `UoSounds`, `ItemPicker`, `UoFoliage`,
   `UoTex`, `TitleBars`, `UoMap` and `UoRegions` moved to their own files, and the window split into a partial file per tab, its constructor into one `Build<Tab>` method per tab,
   called in the old order); one panel per task bound through the registry.

## Open

- Each stage's proof is `test-assist.ps1` green, its files' coverage measured (`AssistTesting.md`), and a live run of
  the tasks it moved. A task a stage moved that has not run live since is listed as unproven in that stage's landing
  report until it has.
