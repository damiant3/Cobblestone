# Owner treasury controls

The authenticated owner panel reads and changes the existing stage E economy.
It creates no money state, seed purse or second treasury. The GM development
stand-in remains separate and explicitly labeled.

The serialized image owner binds its retained `EpWorld` with:

```text
panel-bind-treasury panel economyWorld changesEnabled
panel-bind-currency panel currencyCoordinator verifiedLordActor changesEnabled
```

The first binding is for a gold-era economy; it refuses the extended metal
catalog so direct money operations cannot bypass a currency coordinator.
The second retains the existing `EuCurrency` and a trusted Lord British actor
binding, routing tax and grants through `eu-tax` and `eu-grant`. The actor is
an economy actor id from verified image identity, never a request field.
A currency binding requires synchronized coordinator state; enabling changes
also requires an eligible royal actor. A rejected binding clears access and
returns -1; success returns 0.
An unbound panel reports unavailable and refuses changes. `False` exposes
the real balances and turnover read-only. The Boolean comes from trusted
image configuration, never request data. State and binding must live below
request scratch marks. Rebind after full recovery to the recovered economy;
panel constructors and `PanelAuditCodec` restore unbound, with changes disabled.
No pointer or enable flag is serialized by the panel codec.

The composite server binds its live currency read-only
(`panel-bind-currency panel currency 1 False` in `cgs-panel`, Lord British's
economy actor 1): the owner panel shows the shard's real treasury, metals,
rates and chains, and refuses tax, grant and rate changes with
`treasury-read-only` until the durable path below exists.

The existing standalone listener proves in-memory operations only. A production
owner must keep changes disabled until its encompassing transaction path can
commit economy state and panel audit together before sending an applied receipt
or publishing game effects. The pure panel handler cannot perform block I/O.
Setting the Boolean does not supply durability or replace that integration.

## Commands

All commands require role 4 and a live owner panel session. GM, keeper, mind
and health credentials cannot read or mutate treasury controls. Client-supplied
actor/authorization flags convey no authority.

| Command | Input | Result |
|---|---|---|
| `treasury-view` | `session` | Availability, writable mode, backend, gold balance, available metals, copper/silver balances when bound, tax basis points, economy hour, royal-business purses and seven chain rows |
| `treasury-tax` | `session`, `basis_points` from 0 through 10000 | Sets gold tax or the coordinator's common rate across all three ledgers |
| `treasury-grant` | `session`, `purse`, positive integer `amount` up to 9999999999; optional `metal` (copper 1, silver 2, gold 3; default gold) | Transfers the selected existing coin to an admitted kind-5 royal-business purse |
| `treasury-rates` | `session`, `copper_per_gold`, `silver_per_gold` | Calls `eu-rates` through the bound Lord British actor; requires a writable currency binding |

The grant limit follows the admin protocol's ten-digit input bound; the money
component's larger balance limit is unchanged. No request can open a purse,
change its kind, mint coin or grant to a player/NPC/town through these commands.
Existing money admission enforces funds, recipient capacity and ledger space.

The view's `balance` field always means gold, including each business row.
Currency views also return `copper` and `silver` on both the treasury and
business rows. `metals` lists the selectable denomination ids.
Currency views also return `copper_per_gold` and `silver_per_gold`. Gold-era
views have no exchange-rate policy and refuse rate changes. The browser hides
rate controls for those views and disables them for read-only currency views.

Rates follow [EconomyCurrency.md](EconomyCurrency.md): positive integers,
copper at most 1000000, silver at most copper, and copper divisible by silver.
Changing rates changes valuation only. No coin, loan amount, issuance count
or historical rate changes. Before `eu-rates`, the panel logs old and new
rates, the trusted economy actor and expected next coordinator event id.
Full panel audit refuses before policy mutation; a coordinator refusal retains
the refused attempt. Requests cannot substitute an actor or authority flag.

For tax and grants, the panel records trusted actor/session/tick, operation, canonical arguments,
old tax, treasury balance before the attempt and the expected next economy
action id before calling the money operation. Success records applied status
and returns the actual economy action id. A money refusal records refused
status and moves no coin; its expected action id is not a committed ledger
reference. Full panel audit refuses before calling the economy. Full money
ledger refuses the economic change while retaining the panel refusal record.
Success returns `ok:1`, `log_id` and `economy_action`. A money refusal returns
`error:"treasury-refused"` and `log_id`; unbound/read-only, range, session and
audit-admission failures return the corresponding `error` without claiming an
economic action.
The tax/grant audit names `backend` and `metal`; tax uses metal zero for the common
policy. `economy_action` names the money action for the gold backend or the
coordinator event for the currency backend. Currency grants remain limited
to royal businesses even though the underlying coordinator also admits towns.

Tax is displayed as a percentage with two decimal places; the wire uses
integer basis points. Grants are untaxed treasury transfers under the existing
money rules. The treasury starts empty, and the panel has no issuance path.
The browser fixture uses `EconomyCurrencyFixtures` with harvested and smelted
metals, coordinated minting, grants, loans and payments. It creates no new
starting balance outside those existing fixture operations.

## Crown share

Each chain row reads `EpWorld.turnover` and `crown-turnover`. The displayed
share is direct crown-business purchases divided by gross turnover, rounded
down to a basis point. A chain with no turnover reports `null` and displays
"No turnover". This is the existing production counter's meaning: downstream
spending of a subsidy is not traced, and no complete subsidy-origin share is
claimed. The panel names that limitation beside the table.
The currency coordinator has no mixed-price production adapter yet; this
table reads the material world's existing production counters and does not
invent turnover from denomination transfers.

Queries scan at most 256 purses and seven chain counters. Per-chain arithmetic
is bounded by the production counters; mutations use the money module's
constant-work tax/grant paths. Binding adds only a retained reference and
enable flag to the panel, not copies of the economy or balances. Query/audit
text is bounded request scratch.

`TreasuryProof.codex` and `test-admin-panel.ps1` grade authority, conservation,
money/panel audit exhaustion, read-only and unbound states, chain arithmetic,
and real browser currency tax/grant/rate operations. Native cases retain
gold-era coverage and grade rate authority, log exhaustion, old/new policy
history and unchanged balances.
`-Poison` grades native initialization.
Live durable admission and production model/economy composition remain separate.
