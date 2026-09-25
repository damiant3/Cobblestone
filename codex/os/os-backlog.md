# OS quire -- open capabilities

Quire-domain backlog for `codex/os/**` (kernel, net, observe). The shape and
priority order for the platform live in `docs/PM/CurrentPlan.md`; anything
that is this quire's own behaviour lives here.

The rules are the standing ones: an entry says what is still missing and
nothing else, a closed entry is DELETED rather than annotated, and a gap that
is still real is never quietly dropped.

## Open

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
