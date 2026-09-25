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
| CVMM-1 | **Phase 2: the managers serve real state** | ProcessManager serves the kernel process table (`pm-read-kernel-table`, `pm-live`; `codex/test/apps/cvmm-process-table`). DriveManager serves the IDE drives and their GPT partitions (`dm-read-block-devices`; `codex/test/apps/cvmm-drive-table`). NetworkManager serves the NIC the stack is bound to and its live MAC (`nm-live`; `codex/test/apps/cvmm-nic-table`). Every other manager serves a `mock-*` fixture in `CvmmServer`. Staged one manager per landing, each replacing one fixture with a reader over the real source and armed by a test that changes the source and sees the view follow: 4 PortMonitor from live transports. 5 ServiceManager from spawned services. 6 UsbManager from xHCI enumeration. 7 DisplayManager from the GOP mode. 8 FileExplorer from the FAT16 volume. Unowned. |
