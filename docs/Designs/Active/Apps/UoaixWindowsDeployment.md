# UOAIX on Windows: walkthrough page and deployment

A player with a Windows PC and their own 1.25 Ultima Online client runs one setup script and has a UOAIX shard on
their own machine, with the Assist beside their client, following a page on cobblestoneproject.com. Owner: reek (taken
from blu, 2026-10-19).

## Rulings

- Damian, 2026-10-19: "prep a page for the uoaix and assist cards that walk a user player through setting up the server
  and using uoaix and build a deployment of that people can just run in windows".
- The package is our code only: codex-vm.exe, the UOAIX server and its scripts, the Assist's sources. No EA file and no
  patched CLIENT.EXE ship; every client-derived file is built on the player's machine from their own client folder.
- "it will just be in git with a link from the .com": the deployment is a folder in the depot that reaches the public
  repository with the next release; the page links to it.

## What exists (survey, 2026-10-19)

- The run sequence is `apps/uoaix/CompositeGame.md`: compile `CompositeGameServer.codex` with `build/compile.ps1`
  (`-Kernel seed\Codex.cdx`; read the exit code, stdout prints True either way), `import-decoration.ps1`,
  `transform-flora.ps1`, `install-map-cache.ps1`, then `start-composite-game.ps1` or `supervise-composite-game.ps1`.
- From the client folder: `MAP0.MUL`, `STAIDX0.MUL`, `STATICS0.MUL`, `TILEDATA.MUL`, `MULTI.IDX`, `MULTI.MUL`
  (`install-map-cache.ps1` checks exact sizes). The Assist reads the client's art, sound, gump, anim, radar and texture
  files at runtime from the folder of the running client.
- Host: `tools/codex-vm.exe` is in the depot (no build needed), Windows Hypervisor Platform on, PowerShell 7 (inline C#
  with `System.Buffers.Binary`, Windows Forms for the Assist). Nothing else: no zig, node or CUDA.
- No installer exists. `UoaixPage.codex` "Run your own shard, step by step" is a developer recipe; the Assist card has no
  setup steps.
- **The one outside input:** `import-decoration.ps1` reads ServUO (`Doors.cs`, `SecretDoors.cs`, `Item.cs`, `Addons`)
  and UOX3 (`felucca_doors.jsdata`, `felucca_signs.jsdata`) checkouts from `-ReferenceRoot`, default
  `D:/Projects/uo-reference`.

## The deployment: `apps/uoaix/deploy/`

**`setup-uoaix.ps1`**, run once, every step refusing with the reason and the fix, nothing written into the repo:

1. Prerequisites: PowerShell 7; Windows Hypervisor Platform enabled (`Get-WindowsOptionalFeature HypervisorPlatform`,
   with the command to enable it when off); free RAM and disk measured against the start script's own floors.
2. The client folder: asked for (or `-ClientRoot`); each file above present with the size `install-map-cache.ps1`
   expects, the flora-stripped statics refused as that script refuses them.
3. The references: ServUO and UOX3 fetched at pinned commits (`fetch --depth 1`, sparse to the folders
   `import-decoration.ps1` reads, `core.longpaths`) into `%LOCALAPPDATA%\uoaix\reference` (fetched on the player's
   machine, never shipped). A git exit other than 0 or a missing folder stops the setup.
4. The server: `build/compile.ps1` with the depot seed into `%LOCALAPPDATA%\uoaix\server.cdx`, the exit code read.
5. The world: `import-decoration.ps1 -ReferenceRoot` the fetched references, `transform-flora.ps1`, then
   `install-map-cache.ps1` into `%LOCALAPPDATA%\uoaix\world.disk`.
6. Writes `play-uoaix.ps1` beside the world: the server started in the right mode with the ports, and the Assist started
   against it (`assist.ps1 -LoginServer 127.0.0.1,2593 -ListenPort 2630`), each headless with its log path printed.
7. Prints the one change to the player's client: `LOGIN.CFG` to `LoginServer=127.0.0.1,2630` (through the Assist) or
   `127.0.0.1,2593` (straight to the server); the script offers to write it after showing the old line, never silently.

The hard-coded `D:\` defaults the run path meets are parameters already; the setup passes every one explicitly.

## The page

The UOAIX and Assist cards on `LandingPage.codex` link to a walkthrough section of `UoaixPage.codex` (built by
`apps/landing/build.ps1 -Page`, the HTML plug): what you need (Windows, a 1.25 client, Hypervisor Platform), the clone,
`setup-uoaix.ps1`, `play-uoaix.ps1`, the `LOGIN.CFG` line, first login and British, then the Assist's tabs. The page
links the deploy folder on the public repository. Publishing is the site's manual git push (CobblestoneWeb).

## Open

- The client half of `play-uoaix.ps1` (the Assist and `CLIENT.EXE` against the setup's world): NOT RUN, waits for
  Damian. The setup and `play-uoaix.ps1 -Port 2693 -NoClient` ran end to end into a scratch `-UoaixDir` (2026-10-19).
- `play-uoaix.ps1` starts the server `-Testing -Open` (development accounts: any name and password make an account at
  first login, `GameServer.md`) until Damian rules on the home mode.
- The game port is `-Port` (default 2593); the Assist's 2630 is fixed. On Damian's box the live shard holds 2593 and
  the uo1998 relay 2650.
- The hypervisor check asks `WinHvPlatform.dll` (`WHvGetCapability`, code 0) because `Win32_OptionalFeature` reported
  HypervisorPlatform "disabled" (InstallState 2) on a PC where codex-vm runs (Windows 11 build 26300).

- Server mode for a home player: `-Testing` keeps the economy closed until British opens it (`CompositeGame.md`);
  public mode asks for British's password at start. The setup picks one with Damian.
- Whether the walkthrough says anything about uo1998 or other servers: the Assist works against any 1.25 server, and the
  page describes it only against the player's own UOAIX shard unless Damian says otherwise.
- Licences: ServUO and UOX3 are fetched, not shipped; their licences are named on the page beside the fetch.
