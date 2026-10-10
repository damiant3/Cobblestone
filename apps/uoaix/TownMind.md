# Town mind: personas, context and action policy

`TownMind.codex` supplies persona configuration, quoted context, a single-action
validator, deterministic unavailable-model replies, and an audit buffer over
the [layer 1 simulation](Townsfolk.md). No model is called (ruling R2: canned
lines, `NpcSpeech.md`). Dialogue quality, autonomous morning planning and
nightly memory summaries are not built. `MindProof.codex` owns the adversarial fixture and
positive controls; `test-mind.ps1` compiles and runs that fixture.

## Server boundary

Cite `Uoaix chapter TownMind`. Create `tm-new 0` beside the existing `TfWorld`.
Both states are owned by the server's serialized simulation thread. Configure
each admitted NPC once through:

```text
tm-configure mind world npc description fallback callsPerHour tokenBudget giftLimit
```

Description and fallback are trusted persona data, capped at 512 and 128 CCE
characters. Calls per game hour are 1 through 8, token budget 1 through 512,
and gift quantity limit 0 through 10. Zero disables gifts. A successful
configuration returns 0; invalid or repeated configuration returns -1 without
mutation. Strings are borrowed and must outlive the mind. Persona identity
uses the layer 1 NPC serial, not a mobile world serial.

The server assigns event kind and increasing positive request ID per NPC.
Kinds are 1 player speech, 2 morning plan, 3 notable event, 4 nightly memory.
Request IDs cannot exceed one billion. A player line never supplies an NPC
identity, event kind, request ID, policy, provider status or token count.

`tm-context mind world npc event playerLine` creates JSON containing trusted
persona/identity, game time, needs, recent memory sequence indexes, response
token budget, and a separately JSON-quoted `player_text` field. Player input
is capped at 512 CCE characters. Invalid context returns a JSON `error` object;
the host must not send that object as a model request. Quoting defines a data
boundary; quoting alone is not proof that a future model resists instructions.

Until R2, call `tm-stub mind world npc event requestId playerLine`. Every
admitted stub call returns the configured in-character fallback with outcome
`model-unavailable` and fallback flag 1. No generated action is applied.
The request path also counts unavailable attempts toward the hourly limit.

The future trusted host adapter feeds a structured proposal to:

```text
tm-respond mind world npc event requestId playerLine proposal providerStatus tokensUsed
```

Provider status 1 means a completed response; every other status takes the
unavailable fallback. Token use is trusted adapter metadata, checked against
the configured limit. The future adapter must tokenize/limit requests and
enforce generation limits using the actual model tokenizer. The stub uses zero
model tokens; the fixture verifies admission of reported counts, not tokenizer
accuracy or a billed-call cap. Speech is capped separately at 256 characters.

`TmProposal` contains actor, kind, target, item, quantity, destination and
speech. `TmReply` contains speech, outcome, moved quantity and fallback flag.
Pass proposals only through `tm-respond`. `tm-apply`, `tm-store` and direct
record writes are internal mutation helpers, not model-callable operations.
There is no player-text parser that dispatches a function or writes a field.
The transport adapter owns speech delivery and must bind the reply to the
request's live NPC; the validator itself does not emit in-world speech.

## Action policy

| Kind | Admission |
|---|---|
| 0 speech | Matching living actor; bounded speech; no world mutation |
| 1 gift | Morning-plan event only, enabled trusted gift limit; different living target at the actor's town and location; positive quantity within limit; owned inventory and recipient capacity |
| 2 visit | Morning-plan event only, healthy actor; destination is the actor's home/workplace or the town market/tavern/temple |
| Every other value | Refused; no mint, birth, death, discipline or account operation |

Gift item 1 is bread, 2 goods, 3 coins. Transfers subtract and add the same
quantity, with recipient capacity one million. Player speech, notable events
and nightly memory cannot authorize a gift or visit. A visit changes only the
layer 1 destination identifier; the next hourly schedule can replace that
destination. Walking and route validation remain world integration work.

Admission checks audit capacity, context, nonce, input length and hourly
budget before provider status, token usage and proposal rules. A refused
proposal moves no inventory or location, but still consumes its request ID and
admitted call allowance. Repeated IDs are refused. The hourly allowance resets
from the simulation clock, not a host wall timer. No multi-action batch exists;
one response applies at most one validated action.

## Audit and lifetime

The audit holds 1024 records, each six integers: NPC, game hour, event kind,
request ID, outcome, moved quantity. For zero-based record `i`, its sequence is
`mind.sequence - mind.audit-count + i + 1`. Consume before `tm-ack-audit`.
At capacity, return outcome 9, empty speech and fallback flag 0 without applying
an action, consuming a request ID, or emitting an unlogged fallback. The server
must treat that result as backpressure, drain the audit and retry deliberately.

`tm-reason` names outcomes: 0 accepted, 1 model unavailable, 2 hourly limit,
3 text limit, 4 unsupported action, 5 actor/target, 6 inventory/quantity,
7 location, 8 token budget, 9 audit full, 10 replayed request, 11 invalid or
unconfigured context, 12 event policy. `fallbacks` counts every logged canned
reply; `refusals` counts logged nonzero outcomes except unavailable-model
replies; `accepted` counts applied responses including speech. Those totals
feed the keeper's future health report and survive audit acknowledgement.

Persist personas, counters, request IDs, call hours, pending audit records and
sequence with the layer 1 snapshot/input-log checkpoint. Replay ordered inputs,
including host outcomes and structured proposals, and deduplicate audit delivery
by sequence. No persistence or network implementation is supplied here.

The persistent state preallocates 128 persona records and the audit array.
Persona records have nine machine-word fields (72 bytes), proposals seven
(56 bytes), replies four (32 bytes), and the mind wrapper seven (56 bytes).
Borrowed persona
text and generated context text are additional. Admission, validation and audit
append are O(1); context preparation is bounded by the text caps and 16 memory
indexes. Reply and context allocations are temporary: the caller can release
their request arena after speech delivery. No temporary reply/context pointer
is stored in persistent mind state. Configure long-lived strings before taking
such an arena mark.

## Acceptance

```powershell
pwsh -NoProfile -File apps/uoaix/test-mind.ps1 -Kernel seed/Codex.cdx
```

The new evidence directory contains the compiled cite closure, hashes, compile
and run exits, transcript and result. The corpus grades literal preservation of
player text in JSON, logged unavailable replies, and zero per-person inventory
or location changes even when a simulated host proposes a valid owned gift in
a player-speech event. Positive controls transfer real bread and admit a local
visit. Separate controls refuse replay, excessive/negative quantities, empty
stock, actor forgery, unsupported actions, wrong locations, token excess and
call excess, and exercise audit backpressure.

`-Poison` checks initialization. `-Sabotage policy`, `inventory`, `replay` or
`audit` changes one scratch operation; each must compile and run but fail
runtime acceptance. The transcript is a deterministic stub transcript, not
evidence about Qwen3 or any other model. The standalone harness adds nothing
to the release battery.
