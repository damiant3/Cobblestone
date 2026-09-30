# Browser -- open capabilities

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
| BROWSER-4 | **A named host loads over the wire only under `build/browser-remote-test.ps1`, which no gate runs** | `resolve-and-load-remote` fetches `codex://<host>/<path>` as an HTTP GET to 10.0.2.2:80 (no DNS: the host name goes only into the `Host` header) and compiles an `application/codex` body; headers are read from the header block, never from the body. The default `resolve-and-load` stays offline, so the browser and `GopDesk` carry no Network effect (WORKS-10), which `codex/test/apps/browser-offline-load` pins. The harness is the only caller: it serves four paths from a PowerShell responder on 127.0.0.1:80 and refuses when that port is held, so it is in no battery and no gate (L-NOGATE); run it after changing `PageFetcher`, `PageCompiler` or `PageRuntime`. Header names are matched exactly as written (`Content-Type: `), where HTTP makes them case-insensitive. |
