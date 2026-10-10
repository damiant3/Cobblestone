# The authenticated admin panel

**Layout (Damian, 2026-10-08):** the panel is organized into tabs and nested panels instead of one long series of sections, stays usable on a phone, and is designed first for a PC with a full-sized monitor ("use more tabbed and nested panels, rather than long series. still mobile friendly but also realizing that some things are just easier done at a PC with a full sized monitor").

The panel uses the [admin protocol](AdminPort.md). `GET /` serves only a login
handshake page: the form and client-side cryptography, without reports, accounts,
connections or administrator controls. The server supplies the panel markup and
data only after an authenticated owner or personal GM `panel-open` command.
Health, mind and keeper credentials cannot open the panel. The server limits
each GM to the current account grant; owner commands remain owner-only.

The [GM contract](GmContract.md) adds human-only grant, limit and revocation
controls and paged GM action rows. The GM section explicitly names its
in-memory stand-in; database and real game effects remain integration work.

Development uses the guest's admin port forwarded to `127.0.0.1`. The browser
requires a secure context. The loopback fixture is accepted; serving the login
code over untrusted plain HTTP on a rented host is not an accepted deployment.
The image/host integration must authenticate that bootstrap delivery before a
human enters a key. AES-GCM protects protocol messages, not a login script an
attacker replaces. No public-host or game-power acceptance is claimed here.

## Graphs and the page in Codex DOM

Damian, 2026-10-09: "i want graphs in the admin panel, and I want all that hmtl lifted into .codex dom and transpiled
not hardcoded html"; on the graphs: "i wanna see over time, treasury, prices, availability stuff like that."

The page is Codex DOM compiled by the HTML plug ([TheShimmeringPortal](../../docs/TheShimmeringPortal.md), Path A),
not hand-written HTML. Only the login page is served before authentication; `panel-open` returns the plug-compiled
panel code, which the login page runs after decrypting it (root, 2026-10-09: nothing of the panel before auth). The
login page's cryptography (HMAC-SHA256, AES-GCM, random bytes) is the HTML plug's runtime, not page JavaScript.

The panel page runs in an iframe the login page makes from the `panel-open` answer (`srcdoc`, `sandbox="allow-scripts"`
without `allow-same-origin`), so it has its own plug runtime and an opaque origin: it cannot read the login page or its
keys. It reaches the server only by `postMessage` to the login page, which seals each request, sends it, opens the
reply and posts the JSON back (one request at a time, in order). The plug primitives for this are `frame-mount`,
`frame-serve` (login side) and `host-request-then` (panel side). The page is built without the plug's font pack (the
pack alone is 1.9 MB of base64 TrueType); the panel keeps the system fonts.
`panel-history` (owner) with `item` answers the market history `CompositeHistory` keeps: one row a game hour in a ring
of 336 (14 game days), as `{"ok":true,"rows":N,"hours":[...],"copper":[...],"silver":[...],"gold":[...],"names":[...]}`,
oldest first, the treasury coin by metal; with `item` 1 up to the catalog it adds `"item"`, `"price"` (the lowest asking
price over the shops holding the item, -1 for none) and `"stock"` (what all shops hold). An unbound panel answers
`history-unbound`. The ring is made at panel bind (`cgs-panel`); a server without the panel keeps none.
## Browser protocol

The login form takes the owner role's 32-byte key as 64 hex characters or a
personal `UOAIXGM1:<role>:<key>` credential. Login proves possession without
transmitting the supplied key. The key is not placed in a URL or browser
storage. The browser derives request and response keys, clears the input, and submits
encrypted commands. Logout clears derived keys and removes all panel data.

A reconnect first probes the role's issued nonce ceiling with an authenticated handshake.
POST `/handshake` contains role, a fresh 32-byte client challenge in hex, and
an HMAC-SHA256 tag, separated by LF. The tag uses the derived request key over
UTF-8 `UOAIX1/sequence/<role>/<challenge>`. The response contains the issued
ceiling, LF, and HMAC under the response key over
`UOAIX1/sequence/<role>/<challenge>/<sequence>`. The browser verifies the tag
before reserving a new range. The probe changes no state and returns no world
data. A second POST contains role, challenge, the probed ceiling and request-key
HMAC over `UOAIX1/reserve/<role>/<challenge>/<ceiling>`, separated by LF.
The server requires the ceiling still to match, advances it by 1024, and
returns the old ceiling plus response-key HMAC over the same reserve label.
The browser verifies that receipt, then uses old ceiling + 1 through old
ceiling + 1024 exactly once each, renewing before exhausting the range.
Reservation replay is refused by the ceiling comparison. Reconnect abandons
the previous range, including encrypted requests that never reached the guest;
the last accepted request alone cannot establish nonce safety.
If a reservation receipt is lost or invalid, probe again and reserve a fresh
range. Never infer success or encrypt from an unverified receipt. Here
reconnect means client startup or logical reauthentication, not replacement
of an HTTP/TCP connection while the client still owns its counter and range.
If another full range would exceed 999999999, stop encrypted commands until
the server is provisioned with a fresh boot epoch. Never wrap counters or
reset them while retaining the same keys and epoch.
One sender owns each role's sequence; a second panel
login replaces the prior session for that credential. Different GMs and the
owner hold separate sessions. The owner can issue, rotate and revoke personal
keys; see [the credential contract](GmContract.md#personal-panel-credentials).

`panel-open` returns a positive session id, the configured human identity and
the panel markup. Every subsequent panel command supplies that session id
inside the encrypted JSON. Sessions expire after 180000 clock ticks without
an authenticated request naming the live panel session, thirty minutes at the
existing 100 Hz clock. A refused command with a valid session still counts as
activity.
For the owner, `panel-data` reads live health, support summaries, connections and recent log
entries. `report-read` reads a queued report by id. Human `report-ack` logs the
acknowledgement before removing the matching oldest report.
Support summaries and details label account, NPC and world subjects. Legacy
bodies without `subject_kind` remain account reports. NPC/world rows contain
no invented account; their subject id uses the namespace in
[the report contract](AdminPort.md#commands-and-authority).
`panel-log` with `before` reads the preceding sixteen log records, allowing
the browser to inspect every retained action. `panel-cover` (owner only) calls
`__cover-dump` and answers `{"ok":true,"slots":N}`: in a server compiled with
`-RawFlags cover` the counts land in the server log as a COVER block for
`build/coverage-report.ps1`, and in any other build N is 0 and nothing prints. A GM receives their own grant and
usage, health and support instead. `gm-own-log` exposes that GM's actions and
owner changes targeting the GM. GM requests cannot reach the owner or mind
dispatchers, even when sent without the browser controls.

## Actions and game authority

[TreasuryPanel.md](TreasuryPanel.md) defines owner tax and royal-business grants,
the treasury balance and direct crown-purchase share per chain. The panel binds
the existing economy; an unbound or read-only binding cannot change money.

The owner can change real seconds per game day without a restart, with the
old and new rate recorded in the action log. The default is two real hours.
[GameClock.md](GameClock.md) defines the shared clock and game-loop callback;
personal GM panels cannot read or invoke the owner setting controls.

The owner reads and changes the server's log levels without a restart
(UOAIX-27, CompositeGame.md "Log levels"). `panel-bind-log panel log` binds
the running server's `ServerLog` buffer; an unbound panel answers
`{"bound":false}` and refuses changes with `log-unbound`. `panel-levels`
returns the level for all subsystems and each subsystem's override, and
`panel-data` carries the same object as `log_levels`. `panel-level-set` takes
`subsystem` (`all` or a subsystem name) and `level` (trace, debug, info, warn,
error, off, or inherit for a subsystem); the action log records the
subsystem, old and new level before the change applies. GM panels cannot
read or invoke either command.

The Plays tab ([UoaixPlays.md](../../docs/Designs/Active/Apps/UoaixPlays.md)) uses `plays` (op `list`: the four plays,
the stories, and one play's script) and `play-edit` (op `script`, `start`, `pause`, `resume`, `reset`,
`story-on`, `story-off`), bound to the game by `panel-bind-plays`. Every `play-edit` is logged with its op,
play, story and the script's length and digest, never the script, which the world saves.

`panel-action` accepts a bounded command record with `action` and its arguments.
The allowlist is `lord-british-enter`, `lord-british-leave`, `teleport`,
`invisible`, `summon-item`, `summon-creature`, `move`, `inspect` and
`grant-plot` (target the holder, x and y the plot's center; the composite runs it as
 British's `[plot`, Construction.codex). The panel
records the authenticated identity, session, tick, action and arguments before
making the command available to the game owner. The browser labels acceptance
as **queued**, never applied. At most one game command awaits application.

The trusted game owner calls:

```text
panel-action-apply panel actionId now validateAndApply
```

The call checks the pending id and live issuing session, marks the command
claimed before invoking the callback, and records the callback outcome. Return
0 from a successful callback; a nonzero result is a refusal. The callback owns
game validation: a target must exist, values must fit the operation, and the
operation must be one the game server implements. The callback receives the
arguments recorded in the log, not a later network message. A second application
of the same id is refused. An expired or replaced session cancels pending work.
Game clients have no entry to this path and cannot confer authority by sending
administrator flags.

The composite server (`cgs-panel-take`, `cp-pulse`) applies a queued action
on the next pulse of British's own logged-in connection, as British's in-game
command, and the client shows the command's own answer. The `connection` field
is ignored: only British's session applies. `teleport` is `[go x y`,
`invisible` toggles `[invisible`, `summon-item` is `[add graphic amount`
(graphic 1 to 0x3FFF, amount 1 to 60000), `summon-creature` is
`[summon graphic` (body 1 to 0x3FF), `inspect` inspects `target`, and `move`
moves `target` to x, y, z. `lord-british-enter` sets every one of British's
skills to 100.0 and every stat and hits to 100 (`cp-lord`, applied from the
panel queue alone, never from a client packet); `lord-british-leave` changes
nothing, because British is invulnerable at all times (Damian, 2026-10-05)
and keeps his stats and skills. A target of 0 and an argument out of range
are logged refused.
Until British logs in, the action stays queued.

The game server implementing Lord British mode must bind its privileges to
`panel.sessions` and check `panel-live panel session now` before granting those
privileges. Logout, expiry and replacement revoke that predicate. Invulnerability,
stats, skills and in-world powers are game-server effects, not effects
implemented by the panel fixture.

## Integration and budgets

Construct `panel-new adminWorld trustedHumanName` before the network heap mark.
The identity comes from trusted key provisioning, not from a client-supplied
`actor` field. `panel-route auth panel request` composes with a shared listener;
`panel-serve auth panel port` is the standalone listener.

The game owner calls `panel-connect` with slot 1 through 64, account, character,
address and connected-at text. `panel-disconnect` clears that slot. The
composite server (`cgs-connections`) rewrites all 64 slots from its game links
before it answers each admin request: every logged-in connection gives its
account, character and peer address, with connected-at left empty, and every
other slot is cleared. Connection
data is copied, so temporary request strings can be reclaimed. The panel does
not infer game connections from admin TCP sessions. Support evidence and account
text are inserted into the browser as text, never parsed as HTML.
The game owner submits player help through `panel-help`, attaching the real
account, location, wall time, game hour and evidence. In the composite every
in-game help page (`ch-page`) is also filed this way (`cgs-help`): the
paging account's name, the page id and position, and the game hour, with the
wall time empty. Acknowledging the report on the panel does not close the
in-game page, and `[pageack` does not remove the report. Help shares the bounded
human-review queue with conduct reports; no game-client administrator claim is
accepted through that API.

The log is a rolling window of its 128 newest records in fixed 1024-word slots:
record n sits in slot (n - 1) mod 128 and the count only rises, so a full log
never refuses an action. Each record's canonical JSON is at most 1020 CCE bytes.
The outcome cell records 0 queued, 1 applied, -1 refused or -2 cancelled. Connection storage is 64 slots of 512
words. Construction reserves all storage; subsequent requests retain no
request-owned pointers. Listing walks bounded slots, gathers text pieces and
joins once. Work is linear in the bounded text read or returned; no compiler
heap or time behavior changes.

The live log is retained in memory. `PanelAuditCodec` encodes the audit in
UPA1, and `ShardCheckpoint` includes UPA1 in its composite WorldDisk payload.
The stage D owner must commit that payload before publishing durable effects
and handle audit draining or admission backpressure.
A record older than the newest 128 leaves the live window; the action it
recorded had already been applied or refused.

## Source and verification

`AdminLoginPage.codex` (served at `/`) and `AdminPanelPage.codex` (returned by `panel-open` with `codex` 1 as `page`)
are the pages, Codex DOM compiled by the HTML plug without its font pack. `build-admin-page.ps1` encodes them into
`AdminPage.codex`; `-Check` verifies exact parity. Through the composite's game links an admin-panel slot's transmit
queue is `stx-admin-bytes` (`ShardTx`), sized for the sealed panel page; a game slot's is `stx-max-bytes`
(`bvt/BvtAdminTx`). The standalone proof is:

```powershell
pwsh -NoProfile -File apps/uoaix/test-admin-panel.ps1 -Kernel seed/Codex.cdx
```

The harness runs core log/session assertions and a headless Edge session against
the real guest listener, including wrong-key refusal, authenticated reports,
acknowledgement logging, queued actions, logout/reconnect and desktop/narrow
screenshots, the callback seeing the recorded intent before applying (logout,
expiry and replacement leave the callback count unchanged), scoped GM commands,
forged actor/power refusal, key revocation, and an exhausted log that still
closes a GM session (the browser then clears local state and reports the
closure unconfirmed). `-Poison` compiles every proof entry point with poisoned
allocation. Every run writes a fresh evidence directory and stops its own guest
and browser. The host needs PowerShell 7, Node with the built-in WebSocket
client, and Edge at `C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe`.
