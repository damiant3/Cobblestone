# Nettool -- open capabilities

App-domain backlog. The shape and priority order for the platform live in
`docs/PM/CurrentPlan.md`; there is no platform-wide register any more.
Anything that is this application's own behaviour lives here.

The rules are the standing ones: an entry says what is still missing and
nothing else, a closed entry is DELETED rather than annotated, and a gap that
is still real is never quietly dropped.

## Open

**`tests/TestGroupMembership.codex` is not run by anything.** It is a real
suite, twenty arms, and it found a live defect on 2026-08-19
(`gs-register-service` counting a `list-push` twice, fixed in
`codex/os/net/GroupMembership.codex`) -- but it found it because fester ran it
by hand. `build.ps1`'s app-sweep phase COMPILES every entry chapter under
`apps/` and executes none of them, so the fifteen arms that passed and the
sixteenth that took a bounds trap looked identical to every gate. The
invariant that broke now has a battery-run arm at
`codex/test/apps/group-service-count`, so the specific defect cannot come
back silently; the other nineteen arms are still unwatched. Either give this
suite a runner or move what it covers to where `test.ps1` sweeps.