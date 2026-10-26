---
description: >-
  Session start for Blu-assist, the UOAIX Assist project (the uo1998 relay in
  apps/uoaix/assist). Run instead of the Cobblestone /init when the working
  directory is apps/uoaix/assist: memory, the Assist's rules and register row,
  the live relay's state, the relay watch armed under a Monitor, Perforce.
shell: powershell
---

Invoked as `/assist-init`. A skill added mid-session is not listed until the next session starts.

This seat is **Blu-assist**: blu's Assist work in its own session, owner blu, user Damian. The Cobblestone `/init` is
not run here (Damian, 2026-10-25: "this project is not directly related, and deserves its own init"). The Cobblestone
`CLAUDE.md` still binds (R-SHELL, R-TRUE, R-REPORT, R-DASH, R-PROSE, R-HISTORY). Do every step, then report.

When a session here finds something a future init would want (a path, a trap, a step that should be added or
dropped), edit this file the same session (Damian, 2026-10-25: "update it as you discover things that would be
helpful there"). State what IS; no history.

## Step 1 -- Memory

Read this seat's
`C:\Users\Damian\.claude\projects\D--Projects-Cobblestone-blu-apps-uoaix-assist\memory\MEMORY.md` and every file it
lists, then blu's conduct memory in
`C:\Users\Damian\.claude\projects\D--Projects-Cobblestone-blu\memory\` (`working-with-damian.md`,
`check-your-own-instrument.md`). Both bind live work in game.

## Step 2 -- The rules and the open work

Read in full:

- `assist-plan.md`: rules 1 to 11 (rule 1, L-FUTILITY, above all) and the rulings.
- `tailoring-circuit.md`, its "Decisions" table at least; the rest before touching a circuit.
- `..\uoaix-init.md`, "How work lands" and the Assist bullets of "Pitfalls that cost sessions" (from the
  `ImmutableArray` one to the end).
- The **blu** row of `D:\Projects\Cobblestone-blu\docs\PM\CurrentPlan.md` (find the line starting `| **blu** |`):
  what is live, what is seen working, NEXT.

On demand, before touching the subject:

| subject | read |
|---|---|
| any `*.cs` | `assist-csharp.md`, `docs/Designs/Active/Apps/AssistStructure.md` |
| tests, coverage | `docs/Designs/Active/Apps/AssistTesting.md` |
| a client crash or hang | `client-crashes.md` (add the row the same day) |
| houses, placement | `AssistHouses.md`, `AssistHousePlacement.md` (same Designs folder) |
| tools, loadout | `AssistTools.md` |
| identity, staff detection | `AssistIdentity.md` |
| the circuit brain | `AssistCircuitBrain.md` (paused; Damian 2026-10-24) |
| a 1998 server rule | OUO, `D:\Projects\uo-reference\ouo` and `rundir`; ServUO as the cross-check |

## Step 3 -- The live relay

```powershell
Get-NetTCPConnection -LocalPort 2650 -State Listen -ErrorAction SilentlyContinue | Select-Object OwningProcess
Get-CimInstance Win32_Process -Filter "ProcessId=<pid>" | Select-Object CreationDate, CommandLine | Format-List
Get-ChildItem D:\Projects\Cobblestone-root\build-output\uoaix\assist -Filter 'assist-2650-*.log' |
    Sort-Object LastWriteTime -Descending | Select-Object -First 2 Name, Length, LastWriteTime
Get-Process -Name CLIENT -ErrorAction SilentlyContinue | Select-Object Id, Path, StartTime
```

Each in its own command (two object types in one command print the second as nothing). The listener's PID is the
relay; its command line names the workspace it runs from and its `-LogDir`. The newest log is the relay's own, and the
second row is the previous one (Step 4's replay control). A log's file name is local time; the times on its lines are
UTC (local is UTC-7). `LastWriteTime` lags on the open log, so read its last lines instead
(`Get-Content <log> -Tail 5`): Damian is logged in when recent lines carry a game connection (`c2 --`, `S>C`
traffic); a 0-byte log after a bounce means nobody has logged in yet, which is not a fault. The relay is never
started, stopped or bounced without Damian's word; the line is `uoaix-init.md`'s "The uo1998 relay and its client".
No `status.json` exists in this workspace (the blu row of CurrentPlan says otherwise); take the PID from the listener.

## Step 4 -- Arm the relay watch

Control it before arming (`check-your-own-instrument`):

```powershell
pwsh -NoProfile -File D:\Projects\Cobblestone-blu\apps\uoaix\assist\watch-relay.ps1 -RelayPid <pid> -Log <previous log> -Replay
pwsh -NoProfile -File D:\Projects\Cobblestone-blu\apps\uoaix\assist\watch-relay.ps1 -RelayPid 999999 -Log <newest log>
```

The first prints the previous run's circuit lines and exits 0; the second prints `RELAY PID 999999 GONE` and exits 1.
Then arm a Monitor, `timeout_ms` 1800000, re-armed at every expiry, with this exact command. A Monitor runs its
command under bash, which R-SHELL allows here only because the command is one `pwsh -File` process that exits when the
relay's PID is gone and leaves nothing behind (the blu row names this watch); write no shell logic around it: forward
slashes, single quotes, never an inline `-Command`.

```
pwsh -NoProfile -File 'D:/Projects/Cobblestone-blu/apps/uoaix/assist/watch-relay.ps1' -RelayPid <pid> -Log 'D:/Projects/Cobblestone-root/build-output/uoaix/assist/<newest log>'
```

After a bounce the log changes: re-arm on the new one. With no circuit running, an expiry that delivered nothing is
not re-armed again after the second: the watch is armed afresh when Damian next writes or starts a circuit (blu's
memory: idle time is audit time, never watch time). The watch passes no `ASSIST Bank:` or `ASSIST Walk:` lines,
so a purse that drops between two circuit lines is most often a deposit (`MakeRoom` banks gold over the price to make
room for thread); read the log between the two times (`ASSIST` lines) before calling anything lost. An event Damian would act on now (an alarm, staff, attack,
BROKEN, HALT, DESYNC, the relay gone, a circuit ended unasked) is told to him; routine circuit lines are not.

The other tools in this folder: `run-report.ps1 [-Since hh:mm:ss -Until hh:mm:ss]` (one circuit run from the newest
log, UTC times), `thread-report.ps1` (thread vendors' restock estimates), and `test-assist.ps1` / `cover-assist.ps1`
(the suites and coverage, each in a fresh `pwsh`; both green before a landing).

## Step 5 -- Perforce and AgentGrid

This folder holds a copy of the workspace root's `.p4config` (client `BigWhite_Codex_blu`), so p4 runs from here with
no `Set-Location` (the PowerShell tool resets the directory after every call and says so). For the main workspace, pass
`p4 -c BigWhite_Codex_blu_main -d D:\Projects\Cobblestone-blu-main`. `p4 opened` and `p4 changes -s shelved -c BigWhite_Codex_blu -m 5`: say what is open or shelved. Read
`docs/Agents/PerforceProcess.md` before anything beyond `p4 edit` and `p4 submit`. Depot files are read-only until
`p4 edit` (an Edit fails EPERM). `uoaix-init.md`'s "straight to main" is reached this way, and a landing is told to Damian (he manages blu directly).
A landing is: test-assist and cover-assist green, a numbered CL (4.7), shelve,
revert, merge down, unshelve, submit to `//Codex/blu`, then copy up BY PATH, one file per copy, with
`-c BigWhite_Codex_blu_main` (workspace `D:\Projects\Cobblestone-blu-main`): the blu stream holds Construction 35373,
which stays off main, so a bare stream copy would carry it (P-COPYALL). Docs go straight to main in that workspace.
A CL with a new file: the revert leaves the file on disk and the unshelve then skips it, so check `p4 opened -c <CL>`
against the shelf's count and `p4 unshelve -f` the adds it dropped. A new file written by a tool is LF: make it CRLF
before the CL. This folder's `.agentgrid` names
the AgentGrid seat `alpha`; read any message in `D:\Projects\Cobblestone-blu\apps\uoaix\.agentgrid\alpha\inbox`.

## Step 6 -- Report

Three lines at most: the relay (PID, start, log, whether Damian is logged in), the watch armed, and what the blu row
says is NEXT or what Perforce holds open. Then take Damian's ask.
