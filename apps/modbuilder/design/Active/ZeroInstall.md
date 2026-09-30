# ModBuilder with nothing to install

**Status: IN PROGRESS.** Ruled by Damian 2026-09-25: *"i don't want the
user to have to download anything, especially visual studio. yeah we can make
our own dll."* Remaining work belongs in `apps/modbuilder/modbuilder-backlog.md`.

## The capability

A customer with Windows, Steam's Valheim and the Edge that Windows ships
downloads ModBuilder, runs it, clicks through a setup page, picks powers and
presses Forge & Install. Nothing else is installed: no Visual Studio, no
Windows SDK, no .NET SDK, no PowerShell 7, no Node.

## What the customer path needs today, and what replaces each

| today | why it is needed | replaced by |
|---|---|---|
| Loader construction (`codex/plugs/unity/package.ps1`) | build the native WinHTTP proxy and Mono bootstrap | **MB-7 implemented**: Codex emits the DLL and resource; no native SDK |
| .NET SDK Roslyn `csc.dll` (`target-toolchain.ps1`) | compile the emitted C# against the game's managed DLLs | **MB-8**: a CIL plug writes the managed assembly directly |
| PowerShell 7 (`apps/prism/build-bridge.ps1`, `target-toolchain.ps1`, `test-game.ps1`) | serve the bridge, hash, copy, launch the sealed test | **MB-9**: one Codex hosted-windows `ModBuilder.exe` serves the page and does the host work |
| typed paths in the Details panel | the profile | **MB-10**: the setup pages find and create everything |

Windows Edge, the game and Steam are the only external parts; each is
already on the customer's machine by definition.

## MB-7: the loader DLL from the PE plug

The publisher and developer packaging paths use the Codex PE writer.
`codex/plugs/unity/package.ps1` compiles `PeBootstrapDriver.codex` with the
selected depot seed, reads System32's WinHTTP exports through `PeExports`,
and emits `winhttp.dll` with the supplied managed assembly as resource 101.
No MSVC, Windows SDK or resource compiler runs. The package receipt names the
source, seed, system-library, managed-input and output hashes.

`PeWriter.codex` exposes `build-forwarding-winhttp exports`.
`PeBootstrap.codex` extends that image through
`build-mono-winhttp exports payload-buffer payload-size`.
Both return `PeForwardDll`: buffer, size and refusal text. Export lists
require unique ordinals and ASCII-sorted names. The managed payload is bounded
to 64 MiB. Emission and retained heap grow linearly with payload bytes,
export-name bytes, export count and ordinal span.

The generated DLL has these contracts:

- Named and ordinal export trampolines jump through writable slots.
  Process attach initializes lazy-stub addresses using RIP-relative
  instructions. Lazy stubs preserve Win64 integer/vector arguments and stack
  arguments, load System32's `winhttp.dll` by full path, and atomically patch
  the resolved slot. A module-name forwarder would resolve back to the proxy.
  Each first resolution retains a system-library load reference.
- `IMAGE_FILE_DLL`, `DYNAMIC_BASE`, an empty relocation block and Win64
  unwind records permit rebasing. No Codex runtime or fixed Codex heap
  address is installed in the game process.
- The process-attach entry patches UnityPlayer's `GetProcAddress` imports.
  The lookup hook captures `mono_runtime_invoke` and returns a wrapper.
  After a successful `SceneLoader.Awake` or `FejdStartup.Awake`, an atomic
  guard admits one managed initialization.
- The wrapper loads resource 101 through Mono, locates
  `PrismUnityEntry.Initialize`, and calls the initializer through the
  original invoke pointer. Missing hooks, Mono functions, resources, images,
  assemblies or initializers take refusal paths. Managed exceptions receive
  a failure log. The bootstrap does not terminate the game.
- `prism-bootstrap.log` sits beside the DLL. Successful initialization
  writes `PASS managed initializer returned`. Both startup and storage
  acceptance require that marker; storage also requires process exit 0.

From the repository root, build a complete package into a new directory:

```powershell
pwsh codex/plugs/unity/package.ps1 -ManagedLibrary <PrismMod.dll> -OutDirectory <new-package-dir> -Kernel seed/Codex.cdx
```

The native helper retains its resource-update deployment protocol.
`pwsh apps/modbuilder/native/build-loader.ps1 -Kernel seed/Codex.cdx`
produces `build-output/native-deploy/winhttp-template.dll` with a placeholder
resource. `build-site.ps1` embeds that template and the Codex loader sources.
Deployment replaces resource 101 through
`BeginUpdateResourceW`/`UpdateResourceW`; customers need no toolchain.
MSVC/SDK fields are absent from the ModBuilder form, serialized profile and
target-toolchain adapter. Extra fields in an older imported profile are ignored.

The forwarding-only proof remains
`pwsh apps/modbuilder/test-pe-forward.ps1 -Kernel seed/Codex.cdx`.
The complete loader proof is:

```powershell
pwsh apps/modbuilder/test-pe-bootstrap.ps1 -GamePath <installed-Valheim> -ManagedLibrary <verified-PrismMod.dll> -OutDirectory <new-proof-dir> -Kernel seed/Codex.cdx -Template build-output/native-deploy/winhttp-template.dll
```

The supplied assembly must implement the existing isolated startup and storage
probes. The runner compares repeated DLL hashes and every named export/ordinal
against System32, checks rebased native forwarding, runs isolated game startup
and storage, and checks storage again after the helper's native resource-update
operation. Each game run owns a copied executable and separate test saves.
Logs and receipts remain in the requested proof directory.

The refusal controls remove resource 101, corrupt the managed image, remove a
late or early Mono function lookup, and remove the initializer lookup.
Each requires the corresponding refusal, no managed-initialization marker,
continued vanilla startup progress and a surviving process before bounded
harness shutdown. These controls do not cover every exception/failure branch.
The proof does not execute every WinHTTP export or establish Steam-overlay,
other-injector, rendered-gameplay or public-site deployment acceptance.
## MB-8: the managed assembly from a CIL plug

The CIL plug turns the editable model IR into a .NET assembly with ECMA-335
metadata and method bodies. Stable game interop lives in a bundled adapter
built by the publisher from the existing Unity source against the game's
managed references. Players receive that adapter in the page; the CIL plug
embeds it in each model DLL and supplies its managed startup entry. Model
records and callbacks use the adapter's public ABI. Player builds need no
Roslyn, SDK or checkout. The C# plug stays as the readable source rendering.
`codex/plugs/cil/README.md` owns the implemented protocol and developer proof.

Chosen over a runtime `System.Reflection.Emit` loader because the artifact
stays a real assembly: it is hashed into the receipt, can be decompiled and
compared, and a second build can be diffed against the first. A program that
exists only as runtime state inside the game cannot be audited (L-ERASED).

Acceptance: for each feature and the combined pack, the CIL-built assembly
passes the same probes the Roslyn-built one does (`test-game.ps1` startup and
storage, `test-world.ps1` create/reload/vanilla, `test-plants.ps1`), and
`peverify`-class checks find no invalid IL.

## MB-9: ModBuilder.exe

The native helper currently implements discovery, pairing, destination
selection, owned play-copy creation and deployment. Step 5 accepts either the
current browser build or a saved managed DLL. It validates the target, requires
the game to be closed, backs up the prior deployment and replaces the loader.
The native README owns its transaction and HTTP contracts. Launch verifies the
installed loader, uses the separate Saves folder and reports the DLL's module
ID, matching the in-game build label. Game acceptance remains open.

One hosted-windows program, built by the Codex compiler through the PE plug:

- serves the page (`web/modbuilder.html`) and the bridge's `/target` API on
  `127.0.0.1` with a per-run token, and opens Edge on it (the launcher's
  handoff, without `run.ps1`);
- performs inspect, build (MB-7 + MB-8), test (launch the sealed copy, own the
  process, read its log) and install (backup, copy, `restore.json`,
  `deployment.json`) with the guards `test-install.ps1` pins today;
- signs nothing it did not build and runs no command it was sent: the four
  operations are the whole API.

Acceptance: `test-install.ps1`'s seven arms and `test-modbuilder.mjs`'s
thirteen pass against `ModBuilder.exe` instead of the PowerShell bridge.

## MB-10: setup pages

First run, and any time from Details:

1. **Find the game.** Read Steam's `libraryfolders.vdf` (from the Steam path
   in `HKCU\Software\Valve\Steam`) and look for app 892970 in each library;
   show what was found; a folder picker only when nothing was.
2. **Check it.** Inspect: Unity version, assembly hashes, against the target
   catalogue; a mismatch says which game update it needs.
3. **Make the play copy.** Choose where (default `<library>\ModBuilder\Valheim`),
   then create the layout `D:/Games/Valheim-Prism` has today: junctions to the
   game's asset folders, copied `valheim.exe`, `UnityPlayer.dll` and
   `UnityCrashHandler64.exe`, a copy of the player's saves under `Saves`,
   `prism-local-saves.marker`, and a desktop shortcut.
4. **Test saves** go under the play copy's `Support`, never asked for.

Acceptance: on a machine with no profile, the setup reaches a green Inspect
and a play copy that starts vanilla, with no path typed.

## Order

MB-10's pages can be built against the PowerShell bridge first; MB-7 is the
smallest of the replacements and proves the PE plug can write a DLL a game
loads; MB-8 is the largest; MB-9 wraps them. The customer path is complete
only when all four land.
