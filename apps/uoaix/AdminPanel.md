# The authenticated admin panel

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

`panel-action` accepts a bounded command record with `action` and its arguments.
The allowlist is `lord-british-enter`, `lord-british-leave`, `teleport`,
`invisible`, `summon-item`, `summon-creature`, `move` and `inspect`. The panel
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
address and connected-at text. `panel-disconnect` clears that slot. Connection
data is copied, so temporary request strings can be reclaimed. The panel does
not infer game connections from admin TCP sessions. Support evidence and account
text are inserted into the browser as text, never parsed as HTML.
The game owner submits player help through `panel-help`, attaching the real
account, location, wall time, game hour and evidence. Help shares the bounded
human-review queue with conduct reports; no game-client administrator claim is
accepted through that API.

The log stores 128 records in fixed 1024-word slots. Each record's canonical
JSON is at most 1020 CCE bytes. The outcome cell records 0 queued, 1 applied,
-1 refused or -2 cancelled. The last slot is reserved for session revocation;
ordinary actions refuse at 127 records. Connection storage is 64 slots of 512
words. Construction reserves all storage; subsequent requests retain no
request-owned pointers. Listing walks bounded slots, gathers text pieces and
joins once. Work is linear in the bounded text read or returned; no compiler
heap or time behavior changes.

The live log is retained in memory. `PanelAuditCodec` encodes the audit in
UPA1, and `ShardCheckpoint` includes UPA1 in its composite WorldDisk payload.
The stage D owner must commit that payload before publishing durable effects
and handle audit draining or admission backpressure.
No accepted command is silently dropped to make room. A full log explicitly
refuses further ordinary actions.

## Source and verification

`web/admin-login.html` and `web/admin-panel.html` are the page sources.
`build-admin-page.ps1` encodes those sources into `AdminPage.codex`;
`-Check` verifies exact parity. The standalone proof is:

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
