# Prism game targets and composed modifications

Owner: red. Authorized by Damian, 2026-09-20. The requested first game is
Valheim on Windows x64, Unity 6000.0.75f1, Mono. The requested Apple target is
Apoorva Pendse's ARM64 Darwin backend from PR 144, not SwiftUI or iOS.

## Product contract

### Current delivery state

Main25913 contains saved Prism Unity/Darwin profiles, C# library emission
against installed Valheim references and ARM64 Darwin wire generation.
The C# DDC emitter and seed remain unchanged. Mac packaging and execution
remain unverified.

Main25922 promotes the runtime and website from red25921. Damian's current contract is
consumer-only linking and an expanded native chest grid. The previous custom
menu and proximity grouping are superseded.

Main25927 builds the Windows Mono loader directly from native C++.
The typed managed-emission change is red25930, promoted by main25931.

The game assertions pass for newly constructed chests remaining unlinked, common
workbench membership, native chopping-block range/effect lookup, movement
exclusion, corrected material warnings, crafting, access from every linked
chest, quick transfer, drag/drop and stale-projection refusal. The 2026-09-21
rendered test passes with the native opening animation advanced before glyph
inspection: `build-output/prism-ui/tests7/prism-world-tests/35e627aaa9fb426eb933befc1f998272/run.json`.
It verifies bottom-row quantity glyph bounds, hidden padding slots, transfer
refusal into padding, four-container limits and hover-effect target, reuse and
movement removal. The screenshot was inspected; its panel top is cropped.
Mouse/controller acceptance remains a separate manual check.

The bootstrap packages the managed payload inside one `winhttp.dll` and pins
the game assembly and Unity player hashes. `WindowsBootstrap.cpp` owns
initialization. `windows-proxy.ps1` derives the complete named/ordinal export
surface from the machine's System32 WinHTTP library and emits Win64 forwarding
thunks. The package verifies that export surface and records the system-library
identity. Unity's imports alone are insufficient: game networking libraries
also import WinHTTP functions. Forwarding reaches the original system library,
preserving integer/vector argument registers and caller stack arguments.

`MethodBridge.codex` supplies restricted Mono method copying and startup
redirection; unsupported exception regions are refused. The Unity payload uses
a private namespace to avoid Valheim's global `Console` type. Pure insert and
withdraw plans live in `apps/modbuilder/mods/valheim/`. The host validates quantity,
compatibility, unique debits and conservation before physical mutations.

Current limits: solo play; linked crafting covers ordinary new-item recipes,
item upgrades and upgrader stations. Single-ingredient-choice recipes use the
vanilla path. Marked ingredients in linked chests are refused with a request
to move the ingredients into player inventory. Discovery refreshes on opening
and once per second; inventory changes invalidate rows and previews.

### Typed emission and native boundary

`codex/plugs/csharp/CsSyntax.codex` owns the managed target tree: types,
expressions, statements, fields, methods, classes, delegates, attributes and
generic constraints. Method bodies contain `CstStmt` nodes. Literal text and
external symbol names occupy explicit expression/type fields.
`CsSyntaxEmitter.codex` renders those trees through the C# plug. Codex checks
the tree constructors and their payload types; the SDK C# compiler resolves
and checks external Unity/Valheim API names and signatures against the pinned
game references. That division does not claim a Codex semantic model of Unity.

Unity entry/export, method interception, linked storage, native inventory UI
and diagnostic probes construct those nodes. `UnityStdio.codex` connects the
typed interop to ordinary Codex IR emission. `page-lenses.ps1` bundles the
shared C# renderer into the Unity module. The build does not parse embedded
C# source. Member streaming restores temporary AST/render allocations after
each member; nested string construction remains bounded by one member's size.

`WindowsBootstrap.cpp` owns the native Windows/Mono ABI boundary, following
the direct native-source pattern of `tools/codex-vm.c`. `package.ps1` invokes
MSVC and records native source/compiler hashes. Native packaging requires no
Codex printer compilation or VM execution. The generated WinHTTP export table
and assembler thunks remain derived from the installed system library.

The isolated world-test launcher bundles `CsSyntax.codex`,
`CsSyntaxEmitter.codex` and `WorldProbe.codex` before compiling the diagnostic
emitter. `WorldProbe.codex` is not a standalone compilation unit without those
shared chapters. `BridgeProbe.codex` supplies a typed diagnostic class for a
test entry to emit.

Verification for the typed lift, 2026-09-20: the refreshed embedded Unity
module and native SDK package pass. The storage test receipt is
`build-output/prism-targets/ast-lift/page-game-test/run.json` (PASS, exit 0).
The pre-lift and typed managed libraries have identical method signatures,
IL, exception regions and metadata excluding MVID; the same comparison passes
with the world diagnostic class included. A changed group-limit constant fails
the comparison as required. Evidence lives under
`build-output/prism-targets/ast-lift/`: `il-compare.log`,
`world-il-compare.log`, `negative-compare.log` and the corresponding build
receipts. Game heap/time behavior is unchanged by that executable comparison.
The earlier rendered-panel receipt remains a failed historical run; the
current rendered check above passes.

### Build and test entry

Build from a workspace containing main25931. Inspect current open work before
changing source or rebuilding.

The embedded page is `apps/landing/web/compile/prism.html`; its template is
`codex/plugs/wasm/page/prism.html`. Targets -> Load storage demo loads the
sources in `apps/modbuilder/mods/valheim/linked-storage.json`. Loading a template
detaches an opened filesystem folder without deleting the folder's files.

Targets -> Unity configures the game directory, separate test-save root,
MSVC toolset, Windows SDK, per-consumer container limit and workbench-link selection.
Profiles can be named, saved and exported. The machine-local prepared profile
is `build-output/prism-targets/valheim-local.prism-target.json`.

Build extension and Test extension require the authenticated local bridge:

```powershell
pwsh apps/prism/build-bridge.ps1 -Root D:/Projects/Cobblestone-red/build-output/prism-targets
```

Paste the bridge's displayed token into Prism's Bridge settings. Inspect checks
game identity. Build extension emits Unity C# and packages the DLL. Test
extension validates the source/configuration/game receipt, then runs an isolated
startup or inventory probe. Browser emission and profile editing need no bridge.
No action installs into the original game directory.

Valheim's client does not apply `-savedir` through the server-argument path.
The test payload must explicitly call `Utils.SetSaveDataPath` and verify
`Utils.GetSaveDataPath(Local)` before world/profile writes. The general test
launcher supplies `-prism-isolated-test`, an absolute `-savedir` and
`prism-isolated-test.marker` containing `Prism isolated game test`. A missing
marker or mismatched effective directory fails initialization and quits.
A command-line save path by itself is not isolation evidence.

The real-world harness emits `WorldProbe.codex` as diagnostic C# beside the
actual library emitted by Prism. The diagnostic controller is absent from
ordinary packages. Run create, reload and feature-disabled checks serially:

```powershell
node apps/prism/test-targets.cjs --unity
pwsh apps/modbuilder/test-world.ps1 -UnitySource build-output/prism-targets/page-storage.cs -ProfilePath build-output/prism-targets/valheim-local.prism-target.json -Mode create
```

Read `build-output/prism-targets/latest-world-test.json` after completion.
Capture that receipt's exact `saves` path before subsequent runs replace the
latest pointer:

```powershell
$fixtureSaves = (Get-Content build-output/prism-targets/latest-world-test.json -Raw | ConvertFrom-Json).saves
pwsh apps/modbuilder/test-world.ps1 -UnitySource build-output/prism-targets/page-storage.cs -ProfilePath build-output/prism-targets/valheim-local.prism-target.json -Mode reload -Saves $fixtureSaves
pwsh apps/modbuilder/test-world.ps1 -UnitySource build-output/prism-targets/page-storage.cs -ProfilePath build-output/prism-targets/valheim-local.prism-target.json -Mode vanilla -Saves $fixtureSaves
```

`vanilla` skips storage initialization and verifies loaded items, the original
container-open method and transfers through unmodified inventory methods.
The diagnostic bootstrap remains present to select
the fixture and read assertions; that run is not a literal no-DLL launch.
The harness verifies world identity on reload, owns its PID, bounds run time
and requires its PASS marker plus exit 0. A killed process is not a pass.
`-Render` omits batch/headless switches, captures a unique image under test
saves and records its `imagePath`/`imageHash` in the current run receipt. The
controller checks the container panel during rendering.
Image existence alone is insufficient: inspect the image before claiming
visible UI acceptance. Hidden-window runs produced black captures on this
machine. Add `-ShowWindow` only when a visible game test is requested; that
switch requires `-Render`. Mouse/controller interaction remains a separate check.

`PRISM COST` rows carry Stopwatch ticks/frequency and managed-heap deltas for
a representative warehouse. Heap deltas include process noise and are not
exact per-thread allocation counts. This Mono build's per-thread allocation
counter returned zero despite known allocation and is not a valid witness.

The test receipt's `outDirectory/valheim.exe` is the copied executable.
Its `game` field, when present, names the original installation. Never launch
a copied executable against normal saves by omitting verified isolation.
Test roots retain logs and save fixtures. Directory junctions point to installed
game resources; cleanup must not recurse through those junctions.

The host boundaries are `target-toolchain.ps1`, `test-game.ps1`,
`test-world.ps1` and `codex/plugs/unity/package.ps1`. Packaging takes explicit
managed-library, output, MSVC and SDK paths; output must be empty. Rebuild the
emitter with `pwsh codex/plugs/unity/build.ps1 -Kernel <depot-seed>` after
Unity emitter/binding changes, and refresh the embedded Unity module before
using page-generated output as evidence.

For a targeted refresh, first open `apps/landing/web/compile/prism.html` for
edit in the current changelist, then run from the workspace root:

```powershell
$pagePath = [IO.Path]::GetFullPath('apps/landing/web/compile/prism.html')
$modulePath = [IO.Path]::GetFullPath('codex/plugs/unity/build-output/unity-stdio.wasm')
$page = [IO.File]::ReadAllText($pagePath)
$pattern = '("unity-stdio\.wasm"\s*:\s*")[A-Za-z0-9+/=]+(")'
if ([regex]::Matches($page, $pattern).Count -ne 1) { throw 'Expected one embedded Unity module' }
$bytes = [Convert]::ToBase64String([IO.File]::ReadAllBytes($modulePath))
$updated = [regex]::Replace($page, $pattern, [Text.RegularExpressions.MatchEvaluator]{
    param($match)
    $match.Groups[1].Value + $bytes + $match.Groups[2].Value
})
[IO.File]::WriteAllText($pagePath, $updated, [Text.UTF8Encoding]::new($false))
node apps/prism/test-targets.cjs --unity
if ($LASTEXITCODE) { throw 'Embedded target verification failed' }
```

### Proof boundary

No compiler seed changed. The depot seed printed at the initial checkpoint
was SHA-256 `BD66718CBE24F589E8AD8E9D7AAC47289081B00A8824ECD22DBDE77321279541`.
No full battery, fixed point, BVT or seed self-verify is claimed. Full-page /
compiler-module regeneration has not run; unrelated embedded assets are
preserved. The separate IR-UNI wrapper missing-SIZE failure remains registered
in `docs/Designs/Active/Build/Build.md`; Prism uses compiler wasm IR.

### Local play deployment, 2026-09-21

Damian's ordinary play copy is `D:/Games/Valheim-Prism`, with the desktop
shortcut `Valheim - Prism (Separate Saves)`. The shortcut requests fullscreen.
The deployed library contains linked storage, Meadows spawn policy and the noon arc preview without the world probe
controller. Game assets are junctions to the original Steam installation;
the root executable, Unity player and mod DLL are separate files.

`Saves` contains a physical copy of the base character/world data. The
`prism-local-saves.marker` file enables the typed `UnityLocalSaves.codex`
startup adapter: select the adjacent Saves directory, disable the current
Steam platform object's cloud provider, and verify the resulting local path.
Steam account preferences remain unchanged. Keep the marker and Saves folder
with the deployment. An invalid marker or missing save directory refuses
initialization and quits. A normal package without a marker retains the
ordinary save policy. Source: main25938.

`Support/verification.json` records an invalid-marker refusal (exit 1) and
the successful inventory probe (exit 0), including local-path and cloud-off
log markers. The base saves remained hash-identical. The deployed source hash
matches the rebuilt page output. `Support/base-branch.json` records the copied
files; `Support/Base-snapshot.zip` preserves the initial branch point.

`Support/qol-verification.json` records the rendered inventory and headless
Meadows checks and the installed ordinary package hash. The previous DLL and
receipts are in `Support/Before-QoL-25945`. The updated package was installed
with Valheim closed; the adjacent play saves were not replaced.

`ModIdentity.codex` adds `Prism Valheim | Build <number>` to the native main
menu using a separate clone of the game's version label. The original game
version remains visible and the new label does not receive pointer events.
Package builds assign a UTC `yyyyMMdd.HHmmss` number and record it as
`buildNumber` in the build receipt; it identifies the package build, not a
Perforce changelist. The component polls for the menu twice per second and
allocates one label per menu instance, with no steady-state per-frame scan.
The C# plug emits domain-named typed AST members for this component.
Installed build: `20260924.013121`. `Support/weather-verification.json`
records the current package hash, its tests and save preservation; the
previous DLL is in `Support/Before-arc-weather`. `Support/menu-verification.json`
links the headless and rendered checks; the `menu-render3` screenshot was
inspected and shows the label beside the unchanged native menu/version UI.
The previous DLL is in `Support/Before-menu-25948`. Play-save file hashes were
verified unchanged before relaunching the ordinary fullscreen copy.

A composed subset is accepted in play (Damian, 2026-09-24): the arc-only
build `20260925.022424` (`winhttp.dll` SHA-256 `39756DF8`, receipt features `arc`)
showed the arc and no linked chests. Its package, receipt and install steps are in
`Support/Staged-arc-only-20260925.022424`; the full build `20260924.013121` is
reinstalled, with the pre-install DLL and `Saves.zip` in `Support/Before-arc-only`.

The old scratch game copies, engine DLL copies, browser profiles and build
temps were removed without traversing asset junctions. Historical paths below
are relative to the former `build-output/prism-targets` tree:
`Support/Development-evidence.zip` preserves receipts/logs/source;
`Support/Test-saves.zip` preserves test-save trees and recovered test saves.
`Support/Legacy-test.zip` preserves the earlier `build-output/prism-valheim`
save/log files. Archives have per-file SHA-256 manifests and were verified
before cleanup. The Steam installation and deployment links were checked
after cleanup. Current target settings and a deployment pointer remain in
`build-output/prism-targets`, with current emitted examples under `current/`.

The newer `build-output/prism-ui` evidence paths in this document are archive
paths: their suffixes are preserved in `Support/QoL-validation.zip`, with a
verified per-file SHA-256 manifest. Its temporary game copies and engine DLLs
were removed without following asset junctions. Installed code and rollback
DLLs remain in the deployment; diagnostic game copies are not play shortcuts.

### Historical diagnostics and next action

The archived native-grid functional run is
`build-output/prism-targets/prism-world-tests/7d620bfd304a4465a0dfca1351962318/run.json`.
The overall result is FAIL, exit 1, at the rendered-panel assertion; the log
contains the passing functional assertions named above. The final GUI trace at
`prism-world-tests/4cff8104cb934c3bbb9ed65a5795a528/player.log` identifies
InventoryGui.Update as the close caller. The exact close condition is unresolved.

The pre-deployment play receipt is
`build-output/prism-targets/prism-world-tests/9db371ac90284dc087ffdeaeb43b83b3/run.json`;
the recorded PID is historical. The diagnostic play package original page source
hash is `259525511C0A5C7B84BE3906A1CCF2C8DD13B431056C8B41ED3DA16F20852B6F`.
That package includes the diagnostic play controller; an ordinary package
omits that controller. The controller records readiness separately from PASS
and leaves the game running for Damian. An image path is logged after the chest
screen stays open long enough for a capture.

To reproduce that diagnostic session, restore the test archive into an empty
recovery directory, then use the restored fixture. Do not run diagnostics on
the deployment's Saves directory or terminate an active user play session.

```powershell
$restore = 'D:/Projects/Cobblestone-red/build-output/prism-recovery'
if (Test-Path -LiteralPath $restore) { throw 'Choose an empty recovery destination' }
Expand-Archive -LiteralPath 'D:/Games/Valheim-Prism/Support/Test-saves.zip' -DestinationPath $restore
node apps/prism/test-targets.cjs
pwsh apps/modbuilder/test-world.ps1 -UnitySource build-output/prism-targets/page-storage.cs -ProfilePath build-output/prism-targets/valheim-local.prism-target.json -Mode reload -Saves "$restore/prism-world-tests/7d620bfd304a4465a0dfca1351962318/saves" -Play
```

`-Play` explicitly requests a visible interactive game, grounds the marked test
fixtures, links those fixture chests to the test bench, adds trial resources and
leaves the process under user control. Ordinary game construction remains
unlinked. The play controller does not turn readiness into an acceptance pass.
Do not terminate a live user play process for an automated test.

The current rendered check supersedes that harness gap without changing the
historical failed receipt. Re-run linked-resource
and save compatibility checks when the implementation changes. The original
installation remains unchanged. Recovered earlier test-only saves are archived under
`prism-world-tests/137649d1a55e49d5a06a4baec1505e6a/recovered-saves`; the move
manifest is `build-output/prism-targets/recovered-test-saves.json`.
Current open work is MB-4 in `apps/modbuilder/modbuilder-backlog.md`.

Prism assembles selected Codex feature sources into one versioned game
extension. A saved target profile identifies the game, engine version,
runtime, architecture, adapter, feature selections, compiler and toolchain.
The builder checks declared feature requirements and incompatible policies
before emitting code. Packaging DLLs together is not conflict resolution.

The release goal is one installed code DLL containing the bootstrap and
selected behavior. Configuration, logs, manifests and game assets are separate
data. No BepInEx, Doorstop or Harmony runtime dependency enters that release.
The game and its installed Unity/Mono libraries remain external contracts.
A managed-library build alone is not a working mod or a completed release.

Ordinary features use typed game operations. The privileged adapter owns
reflection, interception, native calls and engine object references. Host
filesystem, network, process execution and arbitrary foreign-code loading are
not ordinary feature capabilities. Enforcing that boundary requires the
compiler/plug and adapter; a source extension or signature cannot enforce it.
No claim of sandboxing hostile native code inside the game is made.

## Existing foundation and target separation

`codex/plugs/csharp/emit-compiler.ps1` supplies the C# DDC host path. The
Unity target reuses the C# emitter, builtin mappings and CCE runtime rather
than starting a new general language backend. The DDC executable target
retains its independent behavior and has no Unity references.

`codex/plugs/unity/` owns Unity library emission and versioned target
profiles. Shared C# emission stays under `codex/plugs/csharp/`. Unity
operations have a typed Codex surface; a game's bindings live under
`apps/modbuilder/bindings/<game>/` and are bundled into the plug. A game
adapter under `apps/modbuilder/mods/<game>/` owns game-specific semantics.
Library initialization calls `opening` through a generated engine entry;
Unity callbacks run on the required engine thread. The standalone C# big-stack
thread/console wrapper does not run inside Unity. CCE converts at engine I/O.

Profiles keep engine version, Mono versus IL2CPP, platform, architecture,
managed API profile, game assembly identity and adapter revision separate.
The first profile is `valheim-windows-mono-6000.0.75f1`. A different engine,
runtime or game requires its own proven adapter/profile. Unsupported profiles
are refused, never mapped silently to the nearest known version.

Local inspection on 2026-09-20 found Steam build 25390630, Unity
6000.0.75f1 (26349cd2a5c8), `MonoBleedingEdge` and
`valheim_Data/Managed/assembly_valheim.dll` at Damian's supplied installation.
Those values are observations, not a permanent compatibility range. Inspection
and build receipts remeasure assembly hashes. Machine paths live in local
profiles, not published presets. Proprietary game assemblies stay local.

## First feature: linked storage

Containers join a group only through an explicit link to one central crafting
station. Construction does not link containers. Chest-to-chest links and
proximity/transitive grouping are absent. Unlinked ordinary chests remain
independent. First consumer support uses Valheim's CraftingStation API.

The connection range comes from the installed chopping block prefab
`piece_workbench_ext1`, field `StationExtension.m_maxStationDistance`.
The current installation reports 5 metres. Prism exposes a per-consumer
container limit, default and maximum four; Prism does not override the native
range. Old profile radius fields are discarded by the browser migration and
ignored by the host. Discovery uses bounded local physics queries.

A stored link is active only while chest and consumer remain loaded, accessible
and in range. Opening, transferring and crafting revalidate current membership;
moving a supported static chest or consumer out of range excludes the chest from
those operations. Active UI/effect positions are watched between discovery
refreshes. The explicit link token persists outside range and can reactivate
when the endpoints return within range. Destroying the consumer does not link
containers to a replacement built at the same position.

Opening any member displays the same combined slots through the normal
InventoryGrid. The standalone IMGUI menu is removed. The displayed inventory
contains projection copies; original container inventories and item positions
remain authoritative. UI selection maps a displayed slot back to its physical
inventory/item. Normal drag/drop, split and right-click paths receive those
physical identities. Stale projection objects cannot manufacture items.
Quick moves stack across the group; explicit drag/drop retains slot placement.

The native container panel includes styled Take All, Place Stacks, Link and
Unlink buttons. Link closes the inventory and asks the player to use the desired
workbench. Escape cancels. Unlink detaches only the opened chest. No button
creates a chest-to-chest group. Membership changes preserve stored items.

Movement assertions cover membership queries and hover-effect removal;
post-move transfer/crafting lacks separate runtime assertions. Wagons and
containers with root overrides remain unsupported.

Connections reuse the chopping block's actual `m_connectionPrefab` and its
endpoint transform/scale behavior, rather than a custom line material.
The effect lasts three seconds after linking. World hover refreshes its
one-second lifetime using the stock prefab, with endpoint range checked each
frame. Container links do not install StationExtension components or raise station
upgrade level. Out-of-range or destroyed endpoints remove the active effect.

Crafting availability, per-material warning colors and actual debits use the
same eligible linked pool plus player inventory. The native requirement-row
method is intercepted alongside the existing recipe and consumption methods.
Ordinary new-item recipes, item upgrades and upgrader stations are supported;
single-ingredient-choice recipes retain the vanilla path. Marked linked
ingredients remain outside the supported crafting pool.

Construction requirements and warning rows include the union of linked chests
at every station whose native build range covers the placement point. Chest
membership still uses the chopping-block range and four-container cap.
Construction does not create links. Shared chests are deduplicated. Native
placement validates geometry; before placement, the wrapper reserves only the
material deficit beyond player inventory from chests. A rejected placement
restores those chests. The native caller then consumes the player remainder
exactly once. Free-build and player-only paths retain native behavior.

The focused `consumer-create7/latest-world-test.json` receipt verifies native
item upgrade replacement, quality-dependent chest debits, red-to-white upgrade
and construction warnings, overlapping station pools, out-of-range exclusion,
placement refusal refunds and successful debit conservation. Placement outcome
delegates are controlled in this fixture; native placement geometry is not
reimplemented or claimed to be tested by those delegates.

A bench stores `prism.storage.id` as `1:<GUID-N>`; linked chests store that
token under `prism.storage.station`. Raw network IDs are remapped during world
load and cannot serve as persistent identities. Items retain the original game
save format. Solo play remains the acceptance target; multiplayer requires
authoritative transaction and concurrency work before enablement.

## Meadows spawn policy

Natural Meadows necks retain native initial spawn timing, chance and population
limits. Once the tracked population originating in a zone has been killed,
that zone suppresses natural neck spawns for a saved random seven to fourteen
Valheim days. A private random generator leaves Unity's random state untouched.
Only prefab `Neck`, biome exactly `Meadows`, and entries in the natural spawn
lists qualify. Event spawns and every other creature delegate unchanged.
No SpawnData field is modified. Missing or unloaded living creatures do not
count as kills. Existing loaded necks are adopted into the appropriate zone.

`MeadowsSpawns.codex` supplies the pure delay policy. The typed Unity component
intercepts `SpawnSystem.Spawn` and subscribes to native character death events.
Zone and population GUIDs persist in ZDO metadata because native network IDs
change across world loads. A deadline is written only when the last tracked
neck dies and is not rerolled on reload. At the installed 1800-second day,
the delay is 12600 to 25200 game seconds. There is no forced spawn at expiry;
the native spawn conditions still decide when the next population appears.

`neck-complete-create/latest-world-test.json` and
`neck-complete-reload/latest-world-test.json` pass initial-spawn allowance,
native death callback, deadline bounds, save/reload persistence, no reroll,
unloaded-living-neck handling, other-zone allowance, event/Deer exclusion,
unchanged spawn fields and unchanged Unity random state. These controlled
checks do not claim an elapsed fourteen-day play session. Exact delay evidence
is `neck-policy-proof.json`, fact hash
`2b58a23ce5942632c778470bc4aca897e73df243941ac1e95431a5628ad7b164`.

## Circumhorizontal arc preview, 2026-09-22

The preview appears between 09:00 and 15:00 Valheim time, on day 1 and then
every 7 days exactly (days 1, 8, 15 and on), and only while the current
weather is clear: `Clear`, `Heath clear` or `Twilight_Clear` (Damian,
2026-09-23: clear or lightly clouded daytime only; the artistic arc is kept).
There is no random gate. It fades inside the half-hour edges of that window
and hides as soon as the weather or the day stops qualifying. Existing worlds
need no reset. The day period and the weather list are typed settings in the
Valheim chapter; a period below 1 or a missing list refuses at install.

A new world starts on `EnvMan.GetDay()` 1. Valheim has no dry, lightly
clouded environment apart from the three clear ones: `m_cloudOpacityDay` is
8.53 in almost every environment, and every environment with a thin rain-cloud
layer also carries precipitation (`LightRain`, `Snow`, `Twilight_Snow`), while
`Mistlands_clear` carries a full one (`m_rainCloudAlpha` 2). Measured
2026-09-23 by an environment dump in a fresh world,
`build-output/prism-refine/weather/env-dump/player.log`.
Codex Text reaches the emitted C# as CCE, so the component converts the names
with `_Cce.ToUnicode` once at install.

Verification: `build-output/prism-refine/weather/weather-2/run.json`, PASS,
exit 0, a Direct3D 11 batch session in a fresh world, 14 arms, each asserting
it reached its weather and day before reading the renderer: noon shows the arc
for Clear, Heath clear and Twilight_Clear on day 1 and for Clear on days 8 and
15, and hides it for Rain, Misty, LightRain, Mistlands_clear and ThunderStorm,
for Clear on days 2, 7 and 9, and at 07:12 on day 8. The ordinary package
passes the isolated storage probe
(`build-output/prism-targets/prism-target-tests/5c039b6a46694bea86defcfa09be97c0/run.json`);
its emitted source differs from the previously installed one only in the arc
class and its settings. `CircumhorizontalArc.codex`
in the Valheim demo owns a typed `ArcPreviewSettings` record and spectrum.
The Unity chapter of the same filename builds domain-named C# AST members;
`unity-circumhorizontal-arc-start` selects the component independently of storage.

To see it, use an open view of the sky near noon and look toward the
sun's direction, about 12 degrees above the horizon. Turning the camera does
not move the band to the middle of the screen.

Current settings are elevation 12 degrees, span 70 degrees, band height 16
degrees, distance 12000 game units and strength 800/1000. A flat tangent plane
replaces the strongly curved band. Distance is capped at 65 percent of the
camera far clip. The render queue precedes native clouds, letting their white
foreground patches obscure the spectrum. Opaque depth also obscures it.
This remains an artistic camera-relative layer, not physical refraction.

The shipped `Sprites/Default` shader multiplies a continuous vertex spectrum
by an embedded wispy cirrus PNG. `apps/modbuilder/mods/valheim/cirrus-mask.png` was
generated with the imagegen skill from Damian's cloud-shape reference; it has
no baked rainbow. `target-toolchain.ps1` embeds it as
`PrismGenerated.CirrusMask.png` and records its SHA-256 in the build receipt.
The managed payload and texture remain inside the single native DLL. The
texture loads once through Unity ImageConversion and becomes non-readable.
Native `_CloudOffset` supplies bounded subtle drift. Native materials, sunlight,
weather and play saves are unchanged.

Verification: `arc-cirrus/run.json` under the refinement evidence root records
PASS, exit 0, first-day visibility, pixel contribution, distant placement,
native-cloud render order, occlusion by an opaque blocker 1000 units away,
absence before 09:00 and after 15:00, noon return and resource reuse. The screenshot
`arc-cirrus/saves/arc-day.png` was inspected and shows the wispy continuous band; a copy is
`D:/Games/Valheim-Prism/Support/Arc-preview.png`. Browser emission, standalone
arc SDK compilation and the ordinary package are part of the focused proof.
`Support/refinement-verification.json` records the installed build, tested
base-source identity and unchanged play-save hashes.

Cost: one bounded PNG decode and temporary byte buffer at initialization, one
2425-vertex/4608-triangle mesh and one material. Per-frame authored arc work is
O(1), without texture readback, scene scan or reference allocation. C# member
ASTs stream with heap restoration; compiler/seed unchanged. GPU fill time is
unmeasured. Neck initialization indexes world ZDOs once; retained dictionaries
scale with tracked zones and necks. The one-second refresh scans loaded
characters/zones and tracked members. Construction scans stations and eligible
linked containers, with frame-local preview caching; transaction snapshots
scale with eligible inventory contents. These bounded local-play costs add no
compiler heap retention across members.

Current proof paths are relative to `build-output/prism-refine`, archived in
`D:/Games/Valheim-Prism/Support/Valheim-refinement-validation.zip` after deployment.
The noon arithmetic is in `noon-proof.json`, fact hash
`0a61b8183b22c804c43b38b4d6c879b286268c5325e93d4dbbb37d2e5c22e4bf`.
Physical refraction is not pursued: Damian kept the artistic arc on
2026-09-23. The archived earlier preview is `Support/Arc-preview-validation.zip`.

### Physical model and prior investigation

Evidence paths in this section originated in workspace `D:/Projects/Cobblestone-red`,
not the MAIN client. Their suffixes under `build-output/prism-atmosphere` are
preserved in `D:/Games/Valheim-Prism/Support/Atmosphere-validation.zip`, with
a verified per-file SHA-256 manifest. Temporary game copies were recycled
after removing only their asset junctions. `arc-math.codex` and `arc-math.json`
also remain directly in Support for reproduction through `codex_run` with
`library: math`.

The physical investigation below informs later refinement. Damian authorized
the artistic preview above because native sun geometry prevents a physical
one in the inspected Meadows presets. This is Windows Unity work, separate
from the ARM64 Darwin target.

The real effect uses suitably oriented ice crystals in high, thin clouds such
as cirrus or cirrostratus. Plate crystals lie approximately horizontal; light
enters a vertical side face and leaves the bottom face. Color belongs only
where those clouds exist, with red above violet. It does not require rain or
freezing air at ground level. Sources: [WMO cirrus](https://cloudatlas.wmo.int/en/physical-constitution-cirrus.html),
[WMO cirrostratus](https://cloudatlas.wmo.int/en/physical-constitution-cirrostratus.html)
and [Atmospheric Optics ray path](https://www.atoptics.org.uk/halo/chaform.htm).

### Geometry and the native sun

For ideal horizontal plates and side-to-bottom refraction, a useful central
ray model is `sin(e)^2 = 1 - n^2 + sin(h)^2`, where `h` is solar elevation,
`e` is the arc elevation and `n` is the wavelength-dependent ice refractive
index relative to air. A negative right side means no transmitted ray through
the bottom face. This is not a complete intensity or crystal-orientation model.

With illustrative constant `n = 1.31`, Codex computes a threshold about
57.8 degrees and arc elevation about 22.3 degrees for a sun at 68 degrees.
The 45-degree case intentionally returns MathError, no transmitted ray.
These are approximate binary64 calculations, rounded to 0.1 degree, not an
empirical dispersion fit. [WMO](https://cloudatlas.wmo.int/en/circumhorizontal-arc.html)
gives the usual threshold above 58 degrees and peak intensity near 68 degrees.
Execution evidence: `build-output/prism-atmosphere/arc-math.json`, fact hash
`17427fd722cb3086742a0b2bf0db6d8719145fefbca680323701861b81cae169`.

Inspection of `EnvMan.SetEnv` gives the daytime solar-direction vertical
component `sin(m_sunAngle) * sin(-90 + 360 * m_smoothDayFraction)`, with angular
arguments in degrees. Its 45-degree Meadows presets therefore peak at
45 degrees. Night lighting flips the directional light for moonlight; an arc
must also require daylight. Evidence: `build-output/prism-atmosphere/EnvMan.il`
and the prior world `environment.log` in the same directory.

An artistic mode should keep native lighting/shadows unchanged and relax the
arc's solar-elevation rule. It can retain the real sun's azimuth, horizontal
bands with red above violet, cloud masking and slow appearance/disappearance. Its altitude
and sun separation must be documented as artistic; lowering the threshold
alone does not make the refraction equation valid at a 45-degree sun.

### Reusing Valheim's clouds

The world controller exposes `m_clouds`, `m_rainClouds` and
`m_rainCloudsDownside` mesh renderers. It writes `_CloudOffset` from wind and
sets the cloud material's `_Opacity` and `_Rain` as weather/daylight changes.
This supplies existing geometry, motion and weather inputs without simulating
individual crystals or a new cloud system.

The isolated menu inspection found `Custom/Clouds`, meshes named `default`,
materials `cloud_plane_menu` and `cloud`, and textures `cloud`, `soft_cloud`,
`rain_cloud` with their normal maps. Exposed properties include `_MainTex`,
`_UVScale`, `_Speed`, `_Opacity`, and separate rain texture/scale/speed controls.
No dedicated cirrus asset or local spectral-band control was identified.
Receipts: `build-output/prism-atmosphere/menu-run2/run.json` and
`menu-render3/run.json`, with their `player.log` files.
This is menu-asset inspection, not proof that every world preset uses those
assets. Headless scalar material reads returned zero and are not accepted as
rendered opacity/speed measurements. The rendered menu run returns nonzero
material values and confirms the property bindings, but not world weather
behavior. Shader internals remain uninspected.

The current renderer uses a separate generated cirrus mask and native wind input.
Exact alignment with the original cloud shader's UV/density function is still
unimplemented. A whole-cloud tint cannot produce a localized arc.

The stock sprite shader supports the preview mask and depth test. A more exact
world-cloud integration may need a custom compiled shader asset.
`Shader.Find` only locates a shader already available to the player; assigning
new property names does not add shader logic ([Unity API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Shader.Find.html)).
Exact world UV/density mapping, natural-scene terrain/cloud occlusion across
weather presets, indoor visibility and wet-to-clear transitions need further
rendered acceptance. The controlled distant-blocker and render-order checks
above pass; they do not cover every natural scene.

## Prism toolchain workflow

The Targets surface exposes Unity game extensions and Apple Silicon/Darwin
beside existing language and binary outputs. Target configuration is a saved,
exportable local profile, distinct from source-language lens configuration.
Profiles support multiple games and versions without replacing one another.

Unity configuration names game directory, executable, managed references,
engine/runtime/platform/architecture, adapter, feature parameters, build output,
test save directory and launch arguments. Actions are Inspect, Build, Test,
Install, Launch and Restore as each action becomes implemented. An unavailable
action says why and cannot report success. Building never installs implicitly.
Game installation requires a completed artifact, a matching receipt and a
backup/restore manifest; an unknown existing loader or running game is refused.
Tests use a separate save directory. Native interaction tests and manual
play acceptance remain distinct in the receipt.

The static page builds browser-supported stages and exports a recipe for host
stages. The optional authenticated local bridge runs explicit typed target
actions. Paths and arguments are structured data, not interpolated shell
fragments. Every stage retains diagnostics and exit status. A previous output
cannot satisfy a failed current build. Game changes invalidate compatibility
receipts. Profiles do not grant arbitrary imported code permission to execute.

Apple Silicon uses the existing ARM64 `darwin` path. The Windows-side output
is the ARM64 wire (code, data and function entries), not a Mach-O executable.
`codex/plugs/arm64/run.ps1 -Darwin` already selects that path; the stdio/browser
transport needs the same explicit mode. Preserve the existing virt and ELF
modes. The native Mac wrapping, linking and signing toolchain is configured
separately and is optional for Windows wire generation. PR 144's contributor
owns the external Mach-O wrapper; preserve attribution and establish its
source/license before any import. A Windows compile pass is not a Mac run.
The target must show packaging and runtime verification as separate states.

## Cost and trust contracts

Build receipts identify selected source revisions, dependency closure,
compiler/plug identity, game assembly hashes, configuration, output hashes,
tests and limitations. Compatible feature ordering is deterministic. Signature
verification establishes artifact provenance, not behavior or absence of bugs.

Chest discovery is a bounded physics query around the selected consumer.
There is no transitive expansion through neighboring chests. Projection rebuilds
and snapshots are bounded by group and physical-slot limits; projection copies
are refreshed on changes and periodically, rather than every frame.
Native input maps back to physical storage and validates membership/slots before
mutation. Per-action rollback retains original item identities.

The normal InventoryGrid renderer now sees more cells than a single chest.
Its cell/item lookup and UI allocation cost must be assessed at representative
occupancy; the group/deposit timing probe alone is not a native UI frame-cost
proof. Source review must include projection clone retention, weak mappings for
stale UI objects, callback cleanup and the native cell count. Compiler/seed and
shared C# IR emission remain unchanged. Unity interop emission constructs one
member AST and renders that member before restoring temporary allocations.
Nested text construction can be quadratic in member text size; the complete
runtime tree is not retained across streaming steps.

## Delivery and acceptance

| Stage | Deliverable | Acceptance |
|---|---|---|
| GT1 | Design and saved Prism target profiles | Reader validation; profile round trip, invalid values and unavailable actions tested; existing lenses preserved. |
| GT2 | Unity plug and game inspection | Emit through shared C# base; compile a library against installed references; refuse mismatched engine/runtime; DDC output preserved. |
| GT3 | Owned bootstrap and interception | Start one packaged code DLL inside the matching game; execute a Codex callback; mismatch refusal and vanilla restoration proven. |
| GT4 | Linked-storage demo | Native combined slots from explicitly consumer-linked chests, chopping-block range/effect, movement exclusion, native requirement warnings and input, resource debit, save/reload and feature-disabled compatibility in a separate world. |
| GT5 | Apple Silicon target | Darwin wire produced through Prism; virt mode regression; Mac packaging/run remain explicitly unverified until run on a Mac. |

GT1/GT2 are implemented with focused checks. GT3's packaged bootstrap and
GT4's playable linked-storage implementation are delivered; their automated
proofs are recorded above. GT4 still needs full manual mouse/controller
acceptance. GT5's wire output is implemented; Mac packaging/run remain unverified.
The owning open-work entry is MB-4 in
`apps/modbuilder/modbuilder-backlog.md`; this table owns acceptance details.
