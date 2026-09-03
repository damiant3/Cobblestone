# GpuShow -- open capabilities

App-domain backlog. There is no platform-wide register any more:
`docs/PM/BACKLOG.md` was deleted 2026-07-23 and must not be recreated.
`docs/PM/CurrentPlan.md` carries the shape and the priority order for
the platform. Anything that is this application's own behaviour lives
here.

The rules are the same ones: an entry says what is still missing and
nothing else, a closed entry is DELETED rather than annotated, and a
gap that is still real is never quietly dropped.

| # | Capability | State of the gap |
|---|---|---|
| GPUSHOW-1 | **Nothing triggers the WGSL sweep** | `tools/validate-all.mjs` grades every shader the app ships (83 modules across the 40 pages, both the fetched `kernels/*.wgsl` and each page's inline render shader) and is calibrated in both directions; `docs/ExaminersAssay.md` "The gpushow WGSL Sweep" has the mechanism and the numbers. **No runner invokes it.** It needs an installed browser, so it cannot go in `build/build.ps1` -- the same constraint `build/check-app-pages.ps1` lives under, and the same consequence: a WGSL regression is caught only when somebody remembers to run it. Discipline until that changes: run it when you touch a kernel, the WGSL plug, or a demo page. What would close this is a place for browser-dependent checks that something actually invokes, which is a fleet question rather than a gpushow one and is not worth inventing for one app. All 42 kernels and all 40 pages were clean at 2026-09-01. **A node-free way to drive the browser exists and is not in the depot** (fester, 2026-09-02): headless Chrome with `--remote-debugging-port`, a `System.Net.WebSockets.ClientWebSocket` from PowerShell, and the DevTools protocol as raw JSON reads back page state, console and log entries, and a screenshot, in about 110 lines with no dependency but Chrome. It found five real defects in the fireworks page in one session. Whether it earns a place beside these tools is Damian's call and he has not ruled. Two traps if anyone rebuilds it: `chrome.exe` is a GUI-subsystem binary on Windows, so `--dump-dom` writes to a stdout nothing is attached to and reads as a failed launch; and `--virtual-time-budget` does not advance WebGPU adapter creation, so a page screenshots blank at its first status line, which reads as "WebGPU unavailable" when it is not. |
