# CVMM -- open capabilities

App-domain backlog. There is no platform-wide register any more:
`docs/PM/BACKLOG.md` was deleted 2026-07-23 and must not be recreated.
`docs/PM/CurrentPlan.md` carries the shape and the priority order for
the platform. Anything that is this application's own behaviour lives
here.

The rules are the same ones: an entry says what is still missing and
nothing else, a closed entry is DELETED rather than annotated, and a
gap that is still real is never quietly dropped.

Design: `apps/cvmm/design/Active/`.

| # | Capability | State of the gap |
|---|---|---|

## JSON string emission

Source census, 2026-10-01 at main 33668. These emitters insert Text without
JSON escaping. Preserve decoded values, keys, array shapes and scalar types;
quote stripping is not a repair. Runtime reproductions remain for this fix.

| Source | Unescaped fields or helper |
|---|---|
| `Serialize.codex:12` | `json-text`, used by pair keys and the `serialize-*` value/message helpers |
| `CvmmDashboard.codex:111` | `system-summary-to-json`: hostname |
| `CvmmRoutes.codex:25`, `:28`, `:73`, `:75`, `:258` | Error/message, explorer cwd and terminal screen |
| `CvmmShell.codex:248` | Shell theme and hostname |
| `DriveManager.codex:237`, `:246` | Disk IDs/models and partition/mount strings |
| `FileExplorer.codex:358`, `:438`, `:493` | Operation arguments, file details and entries |
| `FleetManager.codex:234` | Device identity, endpoint and group |
| `LogViewer.codex:257` | Log source, severity and message |
| `Monitor.codex:777`, `:791`, `:807`, `:815` | Panel/layout/catalog text and displayed values |
| `NetworkManager.codex:381` | NIC identity and addresses |
| `PortMonitor.codex:376` | Bind and service strings |
| `ProcessManager.codex:371` | Process names and users |
| `ServerManager.codex:234` | Server identity and endpoint |
| `ServiceManager.codex:365` | Service identity and labels |
| `Terminal.codex:366` | Input line |
| `UsbManager.codex:229` | USB identity and labels |

**Selection arms fail in `apps/cvmm/tests/TestFileExplorer.codex`.** `select-one`, `select-two` and `deselect-one` print `expected 1 got -1`, `expected 2 got -2` and `expected 1 got -1` (2026-10-03, seed 506B403C), on the depot source before and after the path fix in main 34573. The test has no `.expected` and is in no battery.
