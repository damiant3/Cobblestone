# ModBuilder website

The public workspace is `https://cobblestoneproject.com/modbuilder/`.
It includes the feature catalogue, compiler, adapter and artwork, without a
navigation link from the landing page or Prism. The website mirror is
`damiant3/CobblestoneWeb`; `docs/Agents/PublicPush.md` owns that deployment.
`apps/landing/build.ps1` stages the tracked `web/workspace.html` as
`apps/landing/web/modbuilder/index.html`. A focused publication copies only
that file into the mirror's `modbuilder/index.html`; keep its root CNAME and
unrelated work. The subdomain is not required for this route.

For this focused update, use the existing tested workspace directly. Do not
run the whole-site rebuild or its broad staging command. Copy the source
workspace's `apps/modbuilder/web/workspace.html` into a clean website checkout
at `modbuilder/index.html`. In that website checkout, run
`git -c core.autocrlf=false add -- modbuilder/index.html`, and inspect
`git diff --cached --name-only`: only that path should be staged. Commit its
depot revision and build provenance, then push the website's master branch.

Verify the public response against the staged page bytes and run
`node apps/modbuilder/test-managed-build.mjs --url=https://cobblestoneproject.com/modbuilder/`.
This uses an isolated browser profile and tests the hosted compiler, edited DLL
identity, embedded adapter and Save As behavior. Helper connection and the
file picker are stand-ins; it does not deploy a mod or launch Valheim.
Hosted helper pairing is a separate real-native check:
`node apps/modbuilder/native/test-cors.mjs`, after the fixture preparation in
`native/README.md`. This exercises browser CORS rather than a connection stand-in.

`pwsh apps/modbuilder/build-site.ps1` regenerates `web/workspace.html`.
Open that file directly from disk. The downloaded
workspace also works alone, with the compiler, plugs, library and features
embedded. No website server is required. `serve-site.ps1` is optional.
The four step cards remain available when the downloaded workspace has no companion files.

The player view has Setup helper, Choose your prebuilts, Compile and build,
and Deploy and launch cards. Successful
steps collapse and the next card opens. `page/player-flow.js` owns the cards
and feature integration. The native helper supplies discovery, deployment and
launch. The Library
panel and top feature link are hidden. The title is Cobblestone ModBuilder.
The Valheim banner embeds local artwork and the official logo; `page/art/README.md`
owns their provenance. No artwork request is made by the standalone page.

The project output panel exports Valheim C# for a user's own build and loader
workflow. Compile and build instead produces the managed DLL through the
embedded IL emitter without a player SDK. The generic C# pill and unrelated
Prism binary-target help are hidden or removed in this profile only.
Compiler and adapter source uses a responsive file list beside coloured code.
Large previews are bounded; Copy full source and Save source ZIP keep the
complete files.

Setup helper checks the connection automatically on load and return to the
page; there is no manual Check connection button. Build the helper opens
Helper and Uninstaller source tabs with coloured, editable code. Source ZIP
export sits above the tabs; separate compile/save rows retain independent
artifacts and statuses for both programs.
Review Helper source, compile, Save EXE as, open ModBuilder-Setup-v6.exe and accept the
native setup dialog. The helper
finds Steam libraries and returns to this workspace with a fresh pairing token.
The native preview requires no checkout, Git, Visual Studio or SDK. Discovery
and pairing are implemented. Compile and build emits a real managed assembly
through the embedded CIL plug, including the bundled Unity adapter; Save DLL
as writes the completed artifact through the browser picker. Source edits
invalidate that artifact. The compiler and adapter sources are available in
the build card. Deploy and launch can deploy that build or a saved DLL through the updated
native helper. Choose an existing full/lightweight target or create either type
beside the Steam library. Lightweight targets share the large Steam game folders;
both types keep private saves and a private loader. New targets start with empty
saves. The page shows the destination, target type, installed loader and backup.
The Desktop is fine for the saved DLL; the helper handles installation.
The same card launches the verified copy with separate saves. Build, deploy and launch
show the DLL's module ID, matching the in-game build label. Game acceptance
remains MB-8/MB-9.
The native connection hides developer tool paths and refuses unsupported builds
before sending a package. The uninstaller follows the same compile/save flow.
The manual route can build and save a DLL without the helper, and exposes loader
source for users handling their own packaging. Stock Valheim does not load the
managed DLL by itself. The website route packages the required loader.
Launch checks Steam readiness before starting the game. The uninstaller removes
recognized previous helper executables, preserves game data, and reports locked
files for retry. Its own EXE remains until the user closes and deletes it.
`native/README.md` owns setup, offline source build, removal
and the PE adapter contract.

The workspace uses `codex/plugs/wasm/page/prism.html` with the ModBuilder
profile in `build-site.ps1`, `page/workspace.js` and `page/workspace.css`.
Edit those sources; the generated workspace is disposable.
`site/ModBuilderPage.codex` remains the separate
campaign proposal.

The build takes the compiler, C# and PE plugs and library from the shipped Prism page,
and the locally built Unity and CIL plugs plus bundled adapter. `-PrismPage`
and `-ForgePage` select the compiler asset inputs. Valheim presets read the
catalogue and Codex source under `mods/valheim/`. No compiler or plug is rebuilt.
`codex/plugs/cil/README.md` owns preparation and verification of the adapter
and managed compiler assets before site generation. The publisher builds the
adapter with an SDK; player project builds run in the browser without one.
The native helper also embeds a publisher-built loader template. Prepare it
with `native/build-loader.ps1 -Profile <developer-profile>` before site
generation. The loader source is included in the helper source ZIP. Players
compile the helper in the page and need no native compiler or SDK.
The page embeds helper source and the PE specialization, with no precompiled
helper PE/map. Compile uses the embedded WASM modules; a separate Save EXE as
click opens the browser's file picker with browser security checks intact.
Blob EXE downloads from a file origin are not used: Windows can label their
collapsed file:/// source as Restricted Sites and refuse launch.

The ModBuilder profile stores projects under `modbuilder-valheim` in browser
storage. Prism keeps `prism`. File mode saves each project file in localStorage,
scoped to the workspace file's path; HTTP mode uses OPFS. Export a ZIP before
moving the page, changing hosts or clearing browser data. A storage failure
is reported in the workspace status; folder binding and ZIP export remain
available.

Run `node apps/modbuilder/test-site.mjs --file` and then
`node apps/modbuilder/test-site.mjs` after generating the website. The
browser checks use an isolated Edge profile and a stand-in bridge. The checks
grade compiler output, persistence, project handoff and failure stops, not a
real game launch. File mode copies only the workspace into an otherwise empty
directory and starts no website server. Screenshots land under
`build-output/modbuilder-site/`.

`node apps/modbuilder/test-managed-build.mjs` checks the managed build card
from a copied standalone workspace with no website server. It uses a connection
stand-in, edits the HUD model, builds a DLL, checks Save As completion,
cancellation and security errors, and rejects stale or unsupported output.
The saved artifact is `build-output/modbuilder-site/PrismMod-browser.dll`.
The picker is a stand-in; game deployment is outside this browser check.
`node apps/modbuilder/test-deploy.mjs` instead uses the actual native helper
on diagnostic port 18789, with disposable game folders and a saved Desktop DLL.
`native/test-deploy.ps1` checks the native installation and refusal paths.
The native README owns fixture preparation and the deployment contract.

Unity output omits unused runtime groups. The health and food preset emits no
CCE, buffer or state helpers. Text-producing code retains required conversion
helpers. After the browser suite, `pwsh apps/modbuilder/test-unity-output.ps1`
compiles every exported preset against the installed game assemblies and checks
the HUD values and text/heap/state controls, without launching or installing a
game mod. `-Profile` selects the local target profile. The separate
`-UnicodeProbe` exposes the existing non-ASCII conversion defect in the current
output. That probe is separate from the passing runtime-elision checks.

The separate campaign proposal remains MB-1 in `modbuilder-backlog.md`.

## Valheim convenience options

Harvest and replant adds mature-crop/forage area harvesting with Shift + Use.
Ctrl + Shift + Use replants annual crops when the planting tool is equipped
and seeds, stamina and durability are available. New plants use the native
growth and terrain rules. Growth hover text adds remaining time to the game's
existing condition diagnosis. Extra flora uses the cultivator unless Shovel
and hoe is selected, in which case the hoe remains the planting tool.

Torch and campfire refuelling takes one matching fuel item at a time from
accessible chests within 10 metres of the torch or campfire. Only resin-burning standing torches and
wood-burning `fire_pit` campfires qualify. The scan runs near the local player
in solo play. Hearths, wall lights, other fuels and production stations are
excluded.

Flying build camera uses F8 in placement mode, WASD, Space/Ctrl and Shift for
speed. The character stays in place and remains vulnerable. Camera reach is
bounded; placement still uses the game's requirements. Leaving placement mode
or exiting the camera restores the player's original placement reach.

Equipment places a separate panel, drawn with the game's own window
background, in the centre of the inventory view. Armour
runs vertically as head, torso, legs and back; food, mead and ammunition have
separate columns. Food uses Alt+1/2/3 and mead uses Alt+4/5/6. Four ammo stacks
have Select buttons; ACTIVE identifies the equipped stack. Selecting ammo
uses native equipment state rather than inventory order. Existing saved slot
positions are retained, with the additional ammo slots appended. The dedicated food keys take
precedence over the centred HUD's repeat-food keys. Tools, weapons, shields,
hands, utility and trinket items remain in ordinary inventory. Armour upgrades
use native crafting and restore equipped replacements without an equip queue
or stamina charge. Extra items are stored by native inventory serialization;
empty the extra slots before disabling the feature.

`mods/valheim/features.json` owns the options, `mods/valheim/` the editable
policies and `bindings/valheim/` their typed Unity adapters. The source ZIP and
embedded IL route both use the same bundled runtime interface.

After rebuilding the Unity and CIL modules and bundled adapter, run the
browser checks and export the all-features source with
`node codex/plugs/cil/test.mjs --export-unity`. With the local certified profile:

```powershell
pwsh apps/modbuilder/test-plants.ps1 -UnitySource codex/plugs/cil/build-output/test/unity-all.cs -ProfilePath build-output/prism-targets/valheim-local.prism-target.json -Expansion
```

The expansion arm requires the all-features source. It runs in disposable
game/save folders and must reach every `EXPANSION PASS` assertion before
`PRISM WORLD TEST PASS`. It checks fuel conservation/refusals, slot types,
padding, swaps, save/load, grave recovery, upgrade re-equipping, camera bounds
and controls, and crop seed conservation. Add `-Render` to exercise
panel-to-inventory transfers and ammo selection, check unwrapped labels and
capture the real inventory UI offscreen as `loadout.png` beside the run receipt.
Controller direction mapping has focused assertions; physical controller
feel remains manual acceptance.
