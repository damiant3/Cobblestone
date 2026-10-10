# UOAIX admin port

`AdminServer.codex` exposes the citable `admin-serve auth state port` listener.
The serialized shard owner constructs `AdminWorld` from its live `TfWorld` and
`TmMind`, constructs `AdminAuth`, and keeps all three below the network loop's
heap mark. Port 2594 is the development default. The fixture entry under
`proofs/` is a synthetic town, not the deployed shard image.

## Provisioning and authentication

`admin-auth-new keys epoch` requires four independent 32-byte random keys,
encoded as 64 hex characters, in health, mind, keeper, human order. The caller
supplies a fresh random 32-byte boot epoch in hex on every process start.
Never reuse an epoch with the same keys, including after restoring a snapshot.
The four service keys are supplied through trusted boot configuration. The
owner can separately issue personal GM keys over the authenticated panel;
[GmContract.md](GmContract.md#personal-panel-credentials) defines that boundary.
Invalid lengths or hex refuse construction. Entropy quality
and durable key provisioning belong to the image startup caller; the module
does not pretend that a clock or a deterministic mixer supplies entropy.

`GET /handshake` returns `UOAIX1`, LF, and the epoch. No world or account data
is returned. Application commands require authenticated `POST /admin`.
The [panel handshake](AdminPanel.md#browser-protocol) also supports an
authenticated `POST /handshake` probe and nonce-range reservation.
HTTP framing is supplied by the existing bounded WebServer mux. Authentication
is per request, including on reused connections; a successful request never
turns its connection into an unauthenticated shortcut.

The POST body contains four LF-separated ASCII fields, without a trailing LF:
role (1 through 132), sequence (1 through 999999999), ciphertext hex, tag hex.
Roles 1 through 4 retain their service meanings. Roles 5 through 132 are
initially disabled, bounded personal GM credential slots; only the panel route
provisions them, and the panel dispatcher never sends GM requests to the
service-command dispatcher. A disabled slot returns an empty 401.
Plaintext is UTF-8 JSON, at most 2048 bytes; the tag is 16 bytes. The sequence
must lie within the issued ceiling and strictly exceed the last accepted
sequence for that role during the boot. Each sender reserves a fresh range
before its first encrypted request, on reconnect, and after using a range.
Unreserved sequences are refused. One serialized sender owns each role's sequence. Reconnects skip old ranges;
after losing a reply the sender must not retry a mutation under a new sequence
without reconciling state. Replays return an empty 401 response.

Direction keys are HMAC-SHA256 of the role key over UTF-8
`UOAIX1/request/<role>/<epoch>` and `UOAIX1/response/<role>/<epoch>`.
AES-256-GCM uses a 12-byte nonce: four zero bytes followed by the sequence as
an unsigned 64-bit little-endian value. Request AAD is UTF-8
`UOAIX1/request/<role>/<sequence>`. Response AAD replaces `request` with
`response`. Direction keys prevent nonce reuse between a request and its reply.
The authenticated sequence is consumed before JSON parsing or dispatch.
Tampered, wrong-key, malformed-envelope and replayed requests return empty 401.

A successful response has HTTP status 200 and an ASCII body of ciphertext hex,
LF, tag hex, with no trailing LF. Decrypt using the response key and the
request's sequence. Errors after authentication are encrypted JSON error
objects. Clients must verify the response tag before using any report.
The GET handshake is not authenticated; an altered epoch causes authentication to
fail and does not disclose the role key or world state.

## Commands and authority

[HostRelay.md](HostRelay.md) defines the host-only speech worker and read-only
MCP context tools. `context-read` accepts `topic`, `id` and `after` for service
roles 2 through 4. The guest binds its trusted read view; unavailable culture,
opinion or game-character adapters return an explicit unavailable result.
Health credentials and personal GM routes cannot query those views.

The decrypted JSON object selects an operation with the `command` field.
Admission refuses floating-point and exponent notation; `true`, `false` and
`null` literals are admitted.
The smallest health request is `{"command":"health"}`. A mind polls with
`{"command":"mind-next"}`. A human reads `{"command":"report-peek"}` and
acknowledges the received id with `{"command":"report-ack","id":1}`.
Through the panel route, `report-ack` additionally requires the live human
`session` returned by `panel-open` and records the acknowledgement in its log.

| JSON command | Roles | Result |
|---|---|---|
| `health` | Service roles 1 through 4 | Up/down, game hour, live NPC population, economy counters/prices, spawns, fallbacks, refused proposals, errors, pending reports/job state |
| `mind-next` | Mind, keeper, human | Server-issued job id and quoted context, or `{"job":null}` |
| `mind-reply` | Mind, keeper, human | Validated proposal result, retained for the shard to deliver |
| `report-add` | Mind, keeper, human | Queued report id, or explicit full/invalid refusal |
| `report-peek` | Keeper, human | Oldest report, or `{"report":null}`; reading does not remove a report |
| `report-ack` | Human only | Removes exactly the oldest matching report id after human consumption |

The core health/report/mind commands do not kick, ban, jail, mute, remove player
items or change accounts. The panel's owner-only [GM commands](GmContract.md)
have their own grant and stand-in contract. Unknown commands are refused.
Health credentials expose only shard
counters, never player text, personas, account names or conduct evidence.

`report-add` accepts `subject_kind` as `account`, `npc` or `world`. Missing
kind means `account` for existing producers and stored reports.

| Subject kind | Identity fields |
|---|---|
| `account` | Nonempty `account` text, at most 64 CCE bytes; `subject_id` absent or zero |
| `npc` | `subject_id` from 1 through 2147483647, in the layer-1 NPC namespace; `account` absent or empty |
| `world` | `subject_id` from 1 through 2147483647 for a world-object serial, absent or zero for the whole world; `account` absent or empty |

IDs describe the report, not verified existence or authority. New queue bodies
always carry `subject_kind` and `subject_id`; only account reports carry an
`account` field. Conflicting identities, unknown kinds and malformed id types
are refused before queue mutation. Existing account reports remain readable;
UAC1 preserves their body bytes without rewriting or migrating the envelope.
The ring remains 32 entries with at most 2047 CCE bytes per complete body.
The existing `conduct_pending` health field counts all queued subject kinds.

Every report requires `happened` (1..256),
`location` (1..64), `wall_time` (1..64), `game_hour` (nonnegative integer), and
`evidence` (1..512, the relevant log lines). Report data is supplied evidence,
not a verified misconduct verdict. The queue holds 32 reports, refuses overflow,
and never overwrites an unread report. `report-ack` carries integer `id`.
The canonical serialized report must fit 2047 CCE bytes after JSON escaping.

## Shard integration

`admin-job-submit state npc event request playerLine` is called only by the
trusted simulation. The job records the live NPC, event kind and increasing
TownMind request id. The off-VM host cannot choose those fields through a
reply. One job can be outstanding; a second submission returns -2. Invalid
input returns -1. A successful submission returns a positive job id.

`mind-reply` carries `id`, `actor`, `kind`, `target`, `item`, `quantity`,
`destination`, `speech`, `provider` and `tokens`. Integer fields must be JSON
integers; speech defaults to empty. The transport calls `tm-respond`, including
its event, inventory, location, token, rate, replay and audit checks. The model
host owns tokenizer accuracy. No provider call occurs inside the guest.

[TownMind's action policy](TownMind.md#action-policy) defines kind 0 as speech,
1 as a gift and 2 as a visit; gift items 1, 2 and 3 are bread, goods and coins.
Provider status 1 means a completed response; every other value requests the
unavailable fallback. `tokens` is the host's measured generation token count.

The shard calls `admin-job-fallback` on its own deadline if no model replies.
The call uses TownMind's logged unavailable-model result; audit-full returns
-2 and preserves the job for retry after audit draining. A completed job stays
at state 2 until the shard consumes `admin-job-result`, delivers speech to the
recorded NPC, and calls `admin-job-ack state id`. Job state 0 is empty and 1 is
awaiting a host. The listener alone does not advance the simulation, run a
deadline timer, or deliver in-game speech.

Health population and model counters read live Townsfolk/TownMind state.
The shard owner updates `gold-in`, `gold-out`, `price-bread`, `price-goods`,
`spawns`, `errors` and `online` from its world event accounting. Initial zero
values are initial counters, not evidence that a live economy has been sampled.
Polling `health` supplies the keeper's daily report input; report scheduling
belongs to the keeper.

Queue records and job text are copied into preallocated integer storage;
request-owned pointers never escape into retained state. Report storage is
32 slots of 2048 machine words; job storage is 4096 words. Each slot starts
with its CCE byte length. The fixed report/job payload budget is 557056 bytes,
plus list headers and the wrapper. Health walks at most 128 NPCs. Enqueue,
peek and request parsing are linear in their bounded text size; acknowledgements
are constant time. AES-GCM work is linear in payload length. The shared mux
reclaims transient request allocations. Construct keys, world and mind state
before entering that loop. The runtime has no garbage collector.

Persistent report/job checkpointing, key storage, automatic fallback deadlines,
and joining the game and admin listeners in the deployable image remain the
stage D integration caller's work. Never reset sequences under a retained
epoch after recovery. No public deployment acceptance is claimed by the
standalone port fixture.

## Acceptance

Run from the workspace with PowerShell:

```powershell
pwsh -NoProfile -File apps/uoaix/test-admin.ps1 -Kernel seed/Codex.cdx
```

The standalone harness compiles and runs the core proof, then the TCP listener
fixture. The host uses .NET AES-GCM and HMAC independently of the guest codec.
Each run writes a new evidence directory and a `result.json` receipt; temporary
fixture keys are removed and the owned guest is stopped on success or failure.
`-Poison` grades allocation initialization. `-SabotageAuthentication` changes
only a scratch protocol copy to expose `/reports`; successful compilation must
be followed by `FAIL unauthenticated /reports empty refusal`.

The protocol proof covers encrypted health and report readback, wrong epoch,
tampered tag, replay, reservation replay, unreserved and over-ceiling
sequences, skipping a lost request's range, role authority, report capacity and
ordered acknowledgement, mind-event enforcement, and a fragmented authenticated
request followed by a plaintext request on the same connection. Core controls
cover a valid gift, unavailable-model fallback, audit backpressure, and retained
text after request heap restoration; 1000 storage/restore cycles retain no
additional bytes.
