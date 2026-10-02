# Fester working index

WORKS-81 implementation is on main in CL 33787. No unfinished implementation
shelf or owned guest remains. The lane workplan is empty. Read the current
fester row in `docs/PM/CurrentPlan.md` before taking another unit.

## Owning documents

- `apps/works/works-desk-contract.md`, Web Server administration: control,
  DHCP, process ownership, shared log and diagnostic contracts.
- `apps/works/works-backlog.md`, WORKS-81: physical LAN acceptance remains
  for a coordinated sitting. Emulator and GUI proof do not close that item.
- `docs/PM/CurrentPlan.md`: DeskScheduler production worker integration is
  the next coding unit, awaiting dispatch. The bounded worker-lifetime
  prototype is not production integration.
- `docs/Hardware/HardwareSitting.md`: accepted GUI image and the physical
  questions. Do not relabel image 90B8CEFA as carrying the new Web service.

## Workspace and tools

DEV is `BigWhite_Codex_fester` at `D:/Projects/Cobblestone-fester`;
MAIN is `BigWhite_Codex_fester_main` at `D:/Projects/Cobblestone-fester-main`.
Verify `.agentgrid` against the current harness session before mailbox writes.
The conversation role `/root` does not confer fleet command.

Codex context usage is unavailable. The coordination protocol accepts
`context: null` with `backend: Codex`; do not use Claude transcript telemetry.
Cobblestone MCP servers remain off by Damian's direction; do not restart
servers as part of this work. The MCP leak investigation remains deferred.

The implementation CL names proof artifacts under `build-output/works81`.
Those results are tied to their saved inputs and seed, not proof of future
head. The full battery and a seed fixed point were not run for this app-only
change. Compiler heap/time behavior is unchanged by this index.
