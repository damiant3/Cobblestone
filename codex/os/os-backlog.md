# OS quire -- open capabilities

Quire-domain backlog for `codex/os/**` (kernel, net, observe). The shape and
priority order for the platform live in `docs/PM/CurrentPlan.md`; anything
that is this quire's own behaviour lives here.

The rules are the standing ones: an entry says what is still missing and
nothing else, a closed entry is DELETED rather than annotated, and a gap that
is still real is never quietly dropped.

## Open

| Source | JSON emission gap |
|---|---|
| `observe/NotificationLog.codex:194-200` | `nl-entry-to-json` quotes severity/source/title/body raw; `nl-export-json` joins these records. Quote, backslash and control characters can invalidate exported JSON. Source-inspected 2026-10-01 at main 33668; runtime reproduction remains for the owning fix. |

Where an image already lives at an address, `verify-cdx-full-at` and
`evaluate-load-at` (`codex/os/verify`) verify it without the eight bytes per
byte a list costs; OTA Gate B (`ota-lwm2m-loopback`) does, and
`codex/test/apps/verify-cdx-at` holds both paths to the same verdicts. Every
other caller holds its image as a `List Integer` at the source (`BinaryStore`
through `CmdInstall` into `ShellCore exec-install` and `ProgramRegistry`,
`ShellDispatch dispatch-verify`, the loader arms), and that is not a gap: the
list is the source's own form, so the address path would only add a copy, and
a `BinaryStore` of buffers is a refactor nothing asks for (root, 2026-09-24,
L-LESS). Both paths stay.

`HttpClient` (`codex/os/net/HttpClient.codex`) puts the status CODE token in
`HttpClientResponse.status-text`, not the reason phrase, and keeps no headers.
Nothing reads `status-text` at head: `apps/browser/PageFetcher` reads its own
header block from the raw bytes (2026-09-29).

**`bytes-to-text` traps on a carriage return.** `codex/os/net/WebServer.codex` `bytes-to-text-pieces` (line 34) maps each byte through `char-to-text (code-to-char (from-unicode b))`; `from-unicode 13` is -1 and the encoder refuses -1 (`!EXC=06`), so any caller handing it raw HTTP request bytes halts on the first CR. Found 2026-09-29 (blu): SparkServer died on every request through an unused call (removed, main 30264). The CORE-8 census searched the `code-to-char (from-unicode ...)` spelling and did not list this site. The repair is `cce-foreign-byte-text`, as the closed CORE-8 sites use.
