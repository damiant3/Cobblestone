# Host speech relay and read-only context

`host-relay.mjs` runs on the model host under Node 24, not in the guest image.
The host holds the provider key and connects into the encrypted admin port.
The guest contains neither the provider key nor an outbound provider client.
Provider, model and monthly budget remain R2 decisions. The shipped adapter
uses the [Responses API](https://developers.openai.com/api/reference/typescript/resources/responses/methods/create)
and [function tools](https://developers.openai.com/api/docs/guides/function-calling);
an alternative provider must implement that wire interface or supply another
host adapter. No model name or billed-provider acceptance is implied.

## Configuration and ownership

Supply credentials through the host process environment, never command-line
arguments, URLs, fixture files committed to the repository, or model context:

| Variable | Meaning |
|---|---|
| `UOAIX_ADMIN_KEY` | The 64-hex mind or keeper service key |
| `UOAIX_ADMIN_ROLE` | `2` for mind, or `3` for keeper; default `2` |
| `UOAIX_PROVIDER_KEY` | Provider bearer credential; speech modes only |
| `UOAIX_PROVIDER_URL` | Responses endpoint; default `https://api.openai.com/v1/responses` |
| `UOAIX_MODEL` | Explicit R2-approved model; no default |
| `UOAIX_MAX_PROVIDER_CALLS` | Required speech-mode ceiling, 1 through 100000 calls for this process lifetime |

Remote providers require HTTPS; loopback HTTP supports a local provider or
verification fixture. Redirects are refused. The call ceiling is not a
monthly monetary budget and resets at process restart; configure the provider
account's budget separately under R2. Retries and tool rounds consume the
ceiling. Logs contain job ids, outcomes and bounded failure codes, not keys,
player text, provider bodies or generated speech.

Run from the repository root:

```powershell
node apps/uoaix/host-relay.mjs mcp --admin-url http://127.0.0.1:2594
node apps/uoaix/host-relay.mjs once --admin-url http://127.0.0.1:2594
node apps/uoaix/host-relay.mjs relay --admin-url http://127.0.0.1:2594
node apps/uoaix/host-relay.mjs serve --admin-url http://127.0.0.1:2594
```

`mcp` exposes tools only and needs no provider configuration. `once` handles
one currently pending job. `relay` polls for jobs until interrupted. `serve`
runs the worker and stdio MCP endpoint in one process, stopping the worker
when the MCP input closes. In combined mode both paths share one serialized
admin client and replay counter. Exactly one process owns a service key's
sequence. Do not run separate workers/MCP servers against the same role key;
use combined mode, or give the tools-only keeper a different service role.

The admin client authenticates nonce-range reservation, validates every encrypted
reply and never automatically repeats a mutation after a transport failure.
Every client startup or logical reconnect reserves a new range and abandons its predecessor, including
nonces used for encrypted requests that were lost before acceptance. Range
renewal follows [the admin handshake](AdminPanel.md#browser-protocol).
Replacing an HTTP connection does not discard a still-owned range. An uncertain
reservation receipt requires another authenticated probe and fresh reservation.
Read-only context handlers reconnect and retry once after stale authentication
or a transport failure, including in tools-only MCP mode. Interruption cancels
MCP input processing and in-flight host requests as well as the worker.
The worker reconnects, re-reads the pending job and retains one computed reply
per boot epoch/job id. If that job is still pending, the worker can submit the
same reply without another provider call. A completed job is not regenerated.
The game owner remains responsible for job deadlines, unavailable fallback,
durable job/result state, delivering the speech and acknowledging delivery.

## Context and MCP boundary

The server implements the bounded stdio subset of MCP
[2025-11-25 lifecycle](https://modelcontextprotocol.io/specification/2025-11-25/basic/lifecycle),
[tool calls](https://modelcontextprotocol.io/specification/2025-11-25/server/tools)
and [newline framing](https://modelcontextprotocol.io/specification/2025-11-25/basic/transports).
The negotiated version is explicit; no claim of the newer stateless revision
is made. Initialize, send `notifications/initialized`, then list or call tools.
Stdout carries JSON-RPC only. Unsupported methods and extra tool arguments are
refused. Input buffering is capped at 65536 bytes. EOF closes the service.

All tool schemas are strict integer selectors with no paths, URLs, commands
or arbitrary admin payloads. The provider sees the same catalogue and calls
the same read-only handlers exposed through MCP. Every query becomes an
authenticated `context-read` command; the host has no direct guest memory or
database access.

| Tool | Selector | Current native view |
|---|---|---|
| `uoaix_character` | `id`: world serial | Requires game-character adapter |
| `uoaix_npc` | `id`: layer-1 NPC id | Identity, location, role, needs, mood and held stock |
| `uoaix_persona` | `id`: NPC | Configured description, fallback and token budget |
| `uoaix_memory` | `id`: NPC | Recent memory event ids; retained event details through the events tool |
| `uoaix_family` | `id`: NPC | Spouse, parents and child ids |
| `uoaix_opinions` | `id`: NPC | Explicit unavailable result plus observed mood until opinion adapter is bound |
| `uoaix_town` | `id`: town | Name, population, places, birth/death counters |
| `uoaix_prices_stock` | `id`: town | Existing layer-1 town market prices and stock, explicitly scoped |
| `uoaix_town_culture` | `id`: town | Requires culture adapter: dialect, jargon, values and customs |
| `uoaix_registry` | `after`: exclusive NPC id cursor | Layer-1 NPC registry, sixteen rows per page |
| `uoaix_recent_events` | `id`: town or zero, `after`: sequence | Up to sixteen currently unacknowledged town events |

IDs in different namespaces are not interchangeable. Native memory references
can outlive the in-memory event batch. Acknowledged events require the durable
history adapter; the native view does not fabricate their contents. Native
market prices follow `tf-buy` (bread 1, goods 3), not the separate economy
simulation's unbound inventories. Missing character, opinion and culture
records return `available:false` with a reason, never invented content.

## Guest read adapter

`AdminContext.codex` serves `context-read` with `topic`, `id` and `after`.
Only service roles 2 through 4 may query; the health key and personal GM
dispatchers cannot read these views. Unknown topics and invalid ranges are
refused. Replies contain topic, id, current game hour and `data`. Queries are
serialized reads, not a multi-query snapshot; later calls may see a later hour.

`AdminWorld.context-extra` is a trusted pure read callback:

```text
Text topic, Integer id, Integer after -> Text JSON
```

Bind a callback below the persistent heap mark when constructing or restoring
the image's read view. Empty text selects the native fallback. Nonempty text
replaces that topic's native `data`, must be valid JSON at most 16384 CCE bytes,
and must respect the named identity and cursor. The callback reads already
materialized, stable records; its pure signature does not permit block-device
effects. The database owner updates that view through the normal logged world
transaction path. There is no context-write network command or MCP tool.

This boundary accommodates game characters, durable history, economy stock,
opinions from the townsfolk network and town cultures without adding a second
state store. `AdminQueueCodec` does not serialize function pointers; restore
constructs the default native view, and image startup must rebind the read
adapter. The callback must not borrow request scratch or expose credentials.

## Speech admission and limits

The worker reads server-issued jobs. Player text and tool text are quoted
context, never authority. It sends at most three provider requests per job,
at most eight read-only calls per response, and a total output-token allowance
no greater than the persona's server-supplied budget. Provider usage metadata
and completed status are checked; invalid/missing usage, incomplete responses,
HTTP failure, budget exhaustion or oversized speech produce the explicit
unavailable-model fallback. Input context is bounded to 65536 UTF-8 bytes and
provider responses to 1 MiB. Speech is at most 256 Unicode code points and
512 UTF-8 bytes; the guest's CCE limit remains the final admission rule.

The relay fixes actor from the server job and always submits `kind:0`, with
zero target, quantity, item and destination. Model output supplies speech,
not a game action or identity. Every reply still passes `tm-respond`, which
owns the authoritative limits and audit. Canned-line persistence and live
game delivery remain the speech/game adapter's work; the relay adds no cache
that could become a competing world-state store.

Native query work is bounded by 128 NPCs or 1024 retained event rows and the
returned text. Registry/event pages emit at most sixteen rows. The optional
read adapter owns its cost and must keep output bounded. Native query scratch
is reclaimed by the network loop; no retained cache grows with requests.
The host holds one pending computed speech reply and bounded per-call text.
Compiler heap/time behavior is unchanged.

## Verification

```powershell
pwsh -NoProfile -File apps/uoaix/test-host-relay.ps1 -Kernel seed/Codex.cdx
```

`-Poison` grades native allocation. The harness compiles native context controls,
starts the real guest with ephemeral keys, then grades Node crypto, actual
stdio MCP, context queries, provider tool use, budget/error handling and a
speech-only round trip against a loopback provider fixture. No billed provider
is called. The harness removes fixture keys and stops its own processes.
Live provider quality, R2 billing choices, image composition, durable context
adapters and canned speech are separate acceptance boundaries.
