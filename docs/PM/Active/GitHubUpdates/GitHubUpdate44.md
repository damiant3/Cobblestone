# GitHub Update 44

**Scope: main CLs 15254 onward, opened 2026-08-15.** Update 43 covered 15085
to 15253. Accumulate this cycle's themes here as they land; every number in
the final report gets re-measured at the release head, not carried forward.

## Open from Update 43

- **`B5-UDP`** (blu): `udp-io-mine` carries the same unguarded truncation crash
  that was just fixed on the TCP path, `EXC=06` on a short frame, and DHCP
  rides it.

- **The deck floor under `codex/build`** (fester): `BuildScript.codex` and
  `cdxtopeScript.codex` sit at exactly the 1.25 margin, 51 of 64, with nothing
  left. Three routes and only one retires it: raise the ceiling, shrink the two
  chapters, or make the decks proportional as `ProportionalDecks.md` already
  argues. Do not lower `-MinMargin`.

- **The zig plug's output is still unverified here** (`plugs-backlog` 1.13). No
  zig toolchain on this box and zig is not wired into `plug-oracle-test`, so
  Steve Howell's byte-identical lexer and parser results remain his measurement
  rather than ours. GitHub PR 64 stays OPEN; he says the work is in progress.

- **Nothing exercises the guard page under a genuine allocation walk.**
