# ModBuilder native helper

Open the ModBuilder workspace in Edge or Chrome. Setup helper automatically
checks for a connection on page load and when returning to the page. If no
helper is connected, choose Build the helper to open the source tabs. Review
the Helper source, choose Compile helper, then its Save EXE as button.
Open the saved ModBuilder-Setup-v6.exe.
Allow setup in the native dialog. The helper reads Steam's library locations,
checks for Valheim's executable, managed assembly and Mono runtime, then opens
the workspace with a new pairing token. Windows can show a warning for the
unsigned preview. No elevated installation, Git, SDK or Visual Studio is used.

If a v3 helper opens the hosted page but leaves it waiting, refresh the page,
compile and run v6, then accept Restart. Uninstalling first is not required.
Allow the browser's local-network permission when prompted.

EXE exports use the browser's File System Access save picker,
including browser permission and file-security checks. A blob download from
a local page can lose the source path and receive a Restricted Sites tag;
the resulting Windows Security refusal is separate from SmartScreen.
Cancelling Save As writes no EXE. Save/write failures are reported without
falling back to the broken blob path. Use a current Edge or Chrome browser.

The helper discovers, connects and deploys the browser-built DLL. Discovery
does not change Valheim, mods or saves. Deployment verifies the chosen copy's
game assembly and Unity player hashes. If no game is found, install/locate Valheim
through Steam and reopen the helper, accepting Restart if a copy is running.
Manual original-game discovery is not provided; deployment has a play-copy picker.

## Build from the page

The Setup helper card has Helper and Uninstaller tabs with editable coloured
source. Both contain WindowsHost.codex, Deploy.codex, Cleanup.codex and
Helper.codex; the Uninstaller source selects the removal entry path.
The source ZIP action sits at the top left of the code view. Below the view,
each program has its own compile/save row and status. The embedded WASM
compiler and PE plug produce each EXE offline. Each Save EXE as button stays
disabled until that program compiles. Editing one program invalidates only
that program's EXE; switching tabs keeps both successful builds. Source changes
during Save As refuse stale writes.
Save source ZIP includes the original Codex files, winhost.js, browser.js,
this document, the loader sources and both edited compilation units as
Edited-Helper.codex and Edited-Uninstaller.codex. Keep the workspace
alongside the ZIP: the workspace carries the compiler, library and PE module.
The source ZIP alone does not carry compiler modules.

The page carries source and compiler modules, with no precompiled helper or
separate EXE download. Every helper/uninstaller is compiled from the editor
source, specialized by winhost.js and saved only after a separate user click.
The current workspace URL is embedded as data. browser.js owns that build flow.

Successful native discovery and pairing completes and collapses Setup helper.
Choose your prebuilts then creates one project containing the selected feature
files and a combined entry point. Shovel and hoe and Planting in rows are
independent: rows use the cultivator alone and the hoe when both are selected.
Compile and build checks the integrated source and produces the managed
binary in one action; Deploy and launch can use that result or read a saved DLL,
including one on the Desktop. Completed cards can be reopened.

## Deploy a mod

Choose Save the DLL and deploy manually to export PrismMod.dll for your own
compatible loader and packaging workflow. The DLL is not loaded by stock
Valheim merely by copying it into the game folder. Loader source is available
through the source ZIP. Build without a helper in Setup helper enables feature
selection, compilation and saving without discovery or a running helper.

Choose Use the website and helper for automatic packaging, deployment and launch.

Older helpers show Update helper in Deploy and launch. Compile and run the current helper
from Step 1. The versioned EXE name avoids overwriting the running old helper;
accept Restart in the new helper's normal dialog.

On helper startup, ModBuilder scans immediate child folders of
`<Steam-library>/ModBuilder`, verifies candidate copies and existing loader
receipts, and selects the valid copy with the most recently written successful
deployment receipt. Valid copies without a deployment are the fallback. Linked
target roots and changed/unowned loaders are excluded. If no valid copy exists,
the page defaults to Create a target. Explicit selections, including copies
outside the default folder, apply for the current helper session; restarting
the helper scans the default location again.

Choose existing target opens a native folder picker without a New Folder button and accepts full copies
or lightweight Prism-style targets. The Steam original, its descendants,
linked target roots and unowned existing loaders refuse.
An existing Prism deployment is recognized by its matching folder, base game
and loader hash. Create a target offers Full or Lightweight. Choose or create
a new folder opens a native picker with New Folder enabled. Select an empty
folder; the helper prepares that exact folder after confirmation. Nonempty
unowned folders refuse. Cancelling keeps the current destination. Create in
default folder remains available. Full defaults to
`<Steam-library>/ModBuilder/Valheim`; Windows supplies the copy-progress UI.
Lightweight defaults to `<Steam-library>/ModBuilder/Valheim-Light`, copies the root
game executables and Unity player, and creates native directory junctions for
valheim_Data, MonoBleedingEdge and D3D12 when present. Those large folders remain
shared with Steam, including Steam updates. The loader, Support and Saves stay
private. A recognized target of the same type is reused, preserving saves.
A nonempty unowned folder refuses. New copies have separate empty Saves;
worlds and characters are not migrated by deployment.

Deploy this DLL requires Valheim to be closed and asks for native confirmation
showing the exact destination and whether an existing deployment will be
replaced. The page also shows the installed build and backup notice before
redeploying. The helper embeds the received managed DLL as
resource 101 in its bundled loader template, backs up the prior loader and
receipt under `Support/Before-native-<random-id>`, and atomically replaces
`winhttp.dll`. The uploaded `PrismMod.dll` is also archived in that backup folder.
The page reports both the installed file and backup path. The Desktop DLL is
an optional saved copy and need not be moved manually.

The receipt is committed before loader replacement and accepts the new hash
or its recorded previous hash, so an interrupted replacement can be retried.
Unknown loader changes refuse. Existing copy settings are retained; stale
verification fields are removed. Deployment does not edit saves or launch
the game. The receipt stores the DLL's module ID as its build number, matching
the adapter's in-game label. The page shows it for builds, saved DLLs and the
installed mod. Full in-game acceptance remains separate work.

Deploy and launch starts the verified installed copy through Windows, with that copy as
the working directory, its Saves path and fullscreen enabled. It refuses a
running game, changed loader, missing deployment receipt, linked executable or
Saves folder, and missing local-save marker. The bundled adapter applies the
marked local-save policy. Launch success means Windows accepted the request;
compare the displayed build number with the in-game label after startup.
Steam must be running with an active user. If Steam is not ready, Launch opens
Steam and asks the user to sign in and retry, before starting Valheim.

The loader template is emitted by the Codex PE writer and Mono bootstrap in
`codex/plugs/pe/PeWriter.codex` and `PeBootstrap.codex`. From the repository
root, run `pwsh apps/modbuilder/native/build-loader.ps1 -Kernel seed/Codex.cdx`.
No MSVC or Windows SDK is required. `build-site.ps1` embeds the template and
Codex source, and pins helper game hashes to the bundled adapter.
The complete loader proof command and limits are in
`apps/modbuilder/design/Active/ZeroInstall.md`, section MB-7.

## Remove setup

In Setup helper, open the source view and select Uninstaller to review its
code. Choose Compile uninstaller, then Save EXE as in the uninstaller row,
and open Uninstall-ModBuilder-v6.exe.
Confirm removal. The uninstaller signals the shared native stop event used by
previous versions and waits for the verified running helper process to exit.
It removes recognized helper executables from the running process path,
recorded locations in helpers.json, and ModBuilder-named files in Desktop,
Downloads and its own folder. Running and recorded helpers can have renamed
executables; unrecorded renamed copies elsewhere require manual removal.
The current uninstaller remains and can be deleted after closing it.
Recognition requires the hosted PE layout and both compiled ownership/stop
markers; filenames alone are insufficient. Each deletion verifies the opened
file and deletes through that same handle. Links and unrelated files remain.
Locked or changed recorded files produce an incomplete result with a path;
settings remain available for retry. Earlier orphaned copies can be cleaned
even when the setup directory is already absent. A conflicting existing
ownership marker refuses removal.
Successful cleanup removes only owner.txt, inventory.json, destination.txt and
helpers.json under LocalAppData/Cobblestone/ModBuilder, plus the directory when
empty. Game copies, installed mods and saves remain.
There is no startup entry, service, registry installation or copied executable.

## Native contract

winhost.js specializes Prism's x64 hosted-windows PE output. Compile with
`CDX map hosted-windows passes=none` so mb-native-call remains a unique symbol.
The linker replaces that refusal stub with an eight-integer/pointer Win64
adapter, preserving Codex's R10 heap frontier. RDI holds the function pointer,
RSI the argument buffer; Win64 gets RCX/RDX/R8/R9 and stack arguments. Floating
point arguments, callbacks, SEH/unwind and arbitrary import layouts are outside
the adapter's contract. Modules load from System32 only.

The PE uses Prism's fixed image base 0x100000, .idata RVA 0x1000 and .text RVA
0x2000. Supported compiler IAT starts are 88, 188 and 196 relative to .idata;
the bootstrap VirtualAlloc call identifies the layout. The linker reconstructs
the runtime imports because the shipped PE plug and compiler can carry different
import layouts. Extra slots at 2048/2056 hold LoadLibraryExW/GetProcAddress.
Slots 2080/2088/2096 hold return URL pointer/length and uninstall flag.
Slots 2104/2112 hold the appended loader template pointer/length; 2120 selects
the listen port (8789 for players). Diagnostic ports have distinct stop events.
The adapter changes no compiler or seed and offers no general FFI facility.

The listener binds 127.0.0.1:8789. BCrypt supplies a fresh token; the return
page receives the token in a fragment and removes the fragment after capture.
For file pages, the helper resolves the user's default HTTPS browser through
AssocQueryStringW and gives that executable the complete quoted file URL.
The file-document route through ShellExecute strips the pairing fragment.
Discovery is read from authenticated health, not a legacy SDK target profile.
An older developer-bridge connection is labeled explicitly in the setup card.
Only authenticated health/inventory reveals a game path. Browser Origin must
match the return page (null for file pages). The parsed origin stays below the
request heap mark for the listener's lifetime.
Requests release temporary heap allocations after sending. Header and upload reads
have total deadlines; DLL uploads are bounded to 16 MiB and remain binary.
`GET /deployment` reports the destination and running-game state; authenticated
POSTs to `/deployment/select`, `/deployment/create` and `/deployment` choose,
prepare and deploy. `POST /deployment/create-light` creates a lightweight target.
`POST /deployment/create-folder` and `/deployment/create-folder-light` open
the new-target folder picker for full and lightweight copies respectively.
`POST /launch` starts the verified installed copy after checking Steam. Other operations return 409. Closing the page does not
stop the helper; use the setup uninstaller.

`native/test-deploy.ps1` exercises real native packaging, resource extraction,
backups, interruption, receipts, Unicode paths, owned copies and refusals in
disposable fixtures. Prepare the loader template with the publisher command above
and `unity-hud.dll` with the Unity adapter checks in `codex/plugs/cil/README.md`.
Generate the site, then run from the repository root:

```powershell
node apps/modbuilder/native/build-helper.mjs apps/modbuilder/native/Helper.codex build-output/native-deploy/ModBuilder.exe --console --port=18789 --loader=build-output/native-deploy/winhttp-template.dll
pwsh apps/modbuilder/native/test-deploy.ps1
node apps/modbuilder/test-deploy.mjs
```

`node apps/modbuilder/test-deploy.mjs` drives a copied file-mode workspace and
that native fixture helper, selecting a saved Desktop DLL and checking the
installed receipt and archived bytes. The native fixture launches a capture
executable named valheim.exe to check the real Windows arguments and working
directory. Neither test changes or launches the installed game.

The cleanup regression copies all EXEs into an isolated fixture before running
them. It tests a running v3 helper, recorded renamed paths, previous uninstallers,
locked-file retry and unrelated-file preservation. From a Perforce checkout,
prepare the historical binaries and current candidate on diagnostic port 18789:

```powershell
New-Item -ItemType Directory -Force build-output/native-cleanup | Out-Null
foreach($name in @('WindowsHost','Deploy','Helper')) {
    p4 print -q -o "build-output/native-cleanup/legacy-$name.codex" "//Codex/main/apps/modbuilder/native/$name.codex@29500"
    if($LASTEXITCODE -ne 0) { throw 'Historical source unavailable' }
}
$legacyUnit = (@('WindowsHost','Deploy','Helper') | ForEach-Object { Get-Content "build-output/native-cleanup/legacy-$_.codex" -Raw }) -join "`n"
Set-Content build-output/native-cleanup/Legacy.codex $legacyUnit -Encoding utf8
node apps/modbuilder/native/build-helper.mjs build-output/native-cleanup/Legacy.codex build-output/native-cleanup/legacy-helper.exe --console --port=18789
node apps/modbuilder/native/build-helper.mjs build-output/native-cleanup/Legacy.codex build-output/native-cleanup/legacy-uninstaller.exe --console --port=18789 --uninstall
node apps/modbuilder/native/build-helper.mjs apps/modbuilder/native/Helper.codex build-output/native-cleanup/ModBuilder.exe --console --port=18789
pwsh apps/modbuilder/native/test-cleanup.ps1 -LegacyExe build-output/native-cleanup/legacy-helper.exe -LegacyUninstaller build-output/native-cleanup/legacy-uninstaller.exe
```

The deployment fixture also exercises full/lightweight targets and the Steam
launch refusal with a test readiness override. This grades the launch gate;
it does not claim Steam account sign-in acceptance.

The hosted CORS regression uses an isolated native helper on port 18789:

```powershell
New-Item -ItemType Directory -Force build-output/native-cors | Out-Null
node apps/modbuilder/native/build-helper.mjs apps/modbuilder/native/Helper.codex build-output/native-cors/ModBuilder.exe --console --port=18789 --return=https://cobblestoneproject.com/modbuilder/
node apps/modbuilder/native/test-cors.mjs
```

The check tests repeated
preflight and authenticated requests after idle heap resets, rejects another
origin, then pairs the actual HTTPS page with the native fixture and exercises
automatic connection checks on return to the page. The isolated browser grants local-network/loopback permission;
CORS enforcement stays enabled. `--native-only` omits the browser leg, and
`--exe=<path>` selects a candidate for regression calibration.

For repository development, build-site.ps1 embeds source and compiler modules.
native/build-helper.mjs can compile a diagnostic executable. test-helper.ps1 exercises actual
native discovery, authentication, refusals and uninstall against an isolated
fixture, without launching the game. --probe prints read-only discovery;
--test is a fixture mode that skips dialogs and prints its temporary token.
--test-launch also exercises the actual browser return in that fixture mode.
Test mode uses a separate stop-event name. Both fixture runners refuse to start
when port 8789 is occupied. Those switches are diagnostic interfaces, not the
player setup flow.

`pwsh apps/modbuilder/native/test-launch.ps1` checks default-browser resolution
and the real native ShellExecute argument handoff using a small capture EXE.
The test preserves the fragment and encoded parameters without opening user
browser tabs or changing file/protocol associations. The browser suite then
checks token capture, authenticated discovery and completion of the setup card.
`node apps/modbuilder/native/test-return.mjs` exercises the complete native
return through the real default browser and a temporary copy of the workspace.
Prior bridge storage is restored and checked before reporting. The test asks
the browser to close the temporary tab; tab closure itself is not graded.
`--legacy-control` reproduces the old file-document launch losing its fragment.
The test refuses an occupied helper port and uses isolated settings.
