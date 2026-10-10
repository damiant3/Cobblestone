# GM grants, limits and administration

The human holding the owner admin credential dubs, changes and revokes GMs.
GM authority belongs to an existing human account. NPCs, models and game-client
flags cannot confer that authority. Every operation re-reads the current grant;
revocation or narrowing therefore affects the next action without reconnecting.

`GmMemory.codex` is the explicitly named **in-memory stand-in** until the
[Codex DB backend](Database.md) exists. The panel labels
the mode; successful queries and mutation receipts carry `stand_in: true`.
Authentication/session failures remain protocol errors. The stand-in is a rules
reference and development fixture, not durable shard state or live game powers.
The database/game boundary implements this contract; the wire contract and
transaction rules below remain independent of storage effects.

The composite server admits every game account into its stand-in before it
answers each admin request (`cgs-admit`): account index i is GM account id
i + 1 with the account's name, and every account except British is human, so
Lord British can dub any player account but not British. The stand-in holds
128 accounts; accounts past 128 are not admitted.

In game (`cgs-gm-allow`, `cp-route`), a dubbed GM's `[go x y` and `[goto` run
as `go-to-player` through `gm-memory-act` with the GM as actor and target and
detail `in game`: a grant holding bit 2 travels, and any other grant is answered
`GM refused:` with the audit row. Every attempt is an action row. A player who
is not a GM is not audited, and outside testing mode the speech stays speech.
A GM's `[inspect` runs as `inspect` (bit 8) the same way and opens British's
inspect cursor; the cursor's answer inspects its target only while the grant
still holds bit 8 (checked, not audited again). A target answer from anyone
else inspects nothing. `[kick` is `kick` (bit 32) the same way: its cursor's
answer closes every logged-in connection playing the targeted character
(`cgs-kick`), never British's. Every dubbed GM reads the in-game help pages
with `[pages` (not audited); `[pageack id` is `support` (bit 1), audited.
`[mute` is `mute` (bit 64): its cursor's answer toggles the targeted
character in a fixed 64-slot mute list (`CompositeOwner.muted`, in memory
like the stand-in, so a restart clears it); a muted character's speech
reaches no other player. British cannot be muted. `[unstuck` is `unstuck`
(bit 4): its cursor's answer moves the targeted player's character to the
nearest standing tile within 3 of the new-character entry 1420,1698 in
Britain, commits, and closes that player's connection so the client logs
back in there; British's characters are refused. `[jail N` is `jail`
(bit 128), N game hours from 1 to 8760: its cursor's answer opens a law case
of kind 4 (a GM order: no deed, witness or guard) jailed at once in Britain's
cell until its due hour (`lw-confine`), moves the character onto the cell,
commits and closes that player's connection. The cell's bars, the release at
the due hour and the break-out rule are the law's (`Law.md`). Answering the
cursor on a prisoner releases every jailed case of that character
(`lw-free`). British's characters are refused. `[ban` is `ban` (bit 256): its
cursor's answer bans the account owning the targeted character (`ga-owner`),
commits and closes that player's connection; a banned account's login is
refused with 0x82 reason 2. `[unban name` is `ban` too and lifts the ban by
account name. The flag is account record byte 31 (`ga-ban-at`). British's
account and the GM's own are refused. British uses `[kick`, `[mute`,
`[unstuck`, `[jail`, `[ban` and `[unban` himself with no grant and no audit row.
`[zones` and `[spawns` (any dubbed GM, and British, not audited) toggle the
GM overlays (`GmOverlay.codex`, `docs/Designs/Active/Apps/UoaixDecorator.md`
item 7) for that connection only; every pulse re-reads the grant and drops the
overlay once the GM is no longer dubbed. The overlay is held in memory and a
restart clears it. `[landshow` toggles the pending land edits the same way. The
decorator's land tools (`[land`, `[raise`, `[lower`, `[flatten`, `[brush`,
`[landclear`) check `?decorate` (not audited); each stroke writes an `ADMIN land`
line to the server log.
A GM holding any of the three play aspects runs `[play` as British does (action `play`,
audited), and a Director runs `[story` (action `director`); the panel's Plays tab stays the owner's.

## Database contract

| Table | Columns and invariants |
|---|---|
| `accounts` | Existing account id/name/human classification; add `is_gm` and monotonically increasing `gm_revision`. Account creation and human classification are trusted server operations. |
| `gm_grants` | One row per account: power mask, coin per-action/per-game-day limits, item-quantity per-action/per-game-day limits, title and plot per-game-day limits. Every limit is explicit; zero disables that favor. |
| `gm_usage` | Account, server game day, coin/item/title/plot amounts consumed, last server-issued request id. Grant changes, revocation and re-dubbing do not reset usage or request history. A later game day resets daily amounts; an earlier day is refused. |
| `admin_actions` | Id, transaction id, actor account, authenticated principal, action, target account, subject id, amount, request id, observed grant revision, server game day, accepted/refused decision, reason, relevant before/after values and detail. Preserve all rows on disk; page queries rather than truncating the log. |
| `coin_ledger` | Same transaction id, payer purse, payee purse and positive amount. A coin favor debits the royal treasury and credits the recipient by exactly the same amount. |

Owner actions outside the game use actor account 0 and the trusted admin-key
principal name; zero is reserved for that external owner principal in audit
rows and for the royal treasury in the stand-in ledger. Positive gameplay
actor ids come from the authenticated game session, never a request's actor
or GM flag. Real database foreign keys map the treasury to its actual purse.

One operation is one transaction: validate account/grant revision and limits,
reserve usage, move owned resources, append the ledger when relevant, append
the admin-action row, and commit. Acknowledgement waits for the WAL flush.
Refusal commits only the refused audit attempt and request-admission metadata;
it moves no resource and consumes no favor quota. If audit storage cannot accept
the operation, refuse without effects. Do not expose a partially written grant.
The storage implementation owns locking and rollback; the memory reference is
serialized and performs no fallible operation after its audit admission.

## Powers

| Bit | Action name | Scope |
|---|---|---|
| 1 | `support` | Work the support queue |
| 2 | `go-to-player` | Travel to a player |
| 4 | `unstuck` | Free a stuck character |
| 8 | `inspect` | Inspect items and logs |
| 16 | `restore-items` | Return items lost to a bug, with server-validated evidence |
| 32 | `kick` | Disconnect an account |
| 64 | `mute` | Apply a server-validated mute |
| 128 | `jail` | Apply a server-validated jail action |
| 256 | `ban` | Apply a server-validated ban |
| 512 | `favor-coins` | Transfer treasury coin within both coin limits |
| 1024 | `favor-items` | Transfer owned treasury items within both quantity limits |
| 2048 | `favor-title` | Grant a defined title, one per operation, within the daily limit |
| 4096 | `favor-plot` | Transfer a crown-owned plot, one per operation, within the daily limit |
| 8192 | `decorate` | Place, move, remove and mark decoration (`UoaixDecorator.md`) |
| 16384 | `scene-design` | Plays: marks, scenes and set pieces (`UoaixPlays.md`) |
| 32768 | `character-design` | Plays: the cast |
| 65536 | `dialog` | Plays: states, lines and cues |
| 131072 | `director` | Stories: switch a story's decoration sets on and off |

The mask is 0 through 262143. Per-action limits cannot exceed their daily limit.
Limits are 0 through one billion; amount must be positive. Favor recipients
must be admitted human accounts. Loyalty remains a human judgement; the policy
does not invent a loyalty score. The backend validates target existence,
actual item/plot ownership, game geometry and account-action details in addition
to this power gate. The non-favor operations are authorization-only in the
stand-in and do not teleport, unstick, restore, inspect private game data or
modify a real account.

## Panel wire contract

The owner commands below use the [encrypted admin protocol](AdminPort.md), human role 4,
and a live owner `session` from `panel-open`. No keeper or mind credential can
read grants or change them. Client-supplied principal/actor fields are ignored.

- `gm-list`: returns mode, treasury, game day, action count and admitted accounts
  with current policy and usage.
- `gm-dub` / `gm-set`: fields `account`, `powers`, `coin_each`, `coin_day`,
  `item_each`, `item_day`, `title_day`, `plot_day`. Dub requires a non-GM human;
  set requires an existing GM. Both advance the grant revision.
- `gm-revoke`: field `account`; clears GM status, powers and favor limits and
  advances revision. Usage and replay history survive.
- `gm-log`: field `before`, zero for the newest page or a positive exclusive
  id cursor. Returns at most sixteen action rows. Older pages expose every
  retained row, including refusal attempts and owner changes.

The game adapter calls the reference operation:

```text
gm-memory-act store authenticatedAccount requestId action targetAccount subject amount serverGameDay detail
```

Request ids increase per account and are 1 through 999999999. Replays are
refused. `subject` is an item-type, title or plot identifier for those favors;
coin favors use zero. Detail is at most 128 CCE bytes. This function is not an
admin-port endpoint: the game owner must resolve the actor from its authenticated
session. The production implementation retains these semantics inside its
database transaction, with its own correctly typed storage-effect adapter.

## Personal panel credentials

Lord British issues, rotates or revokes each GM's personal credential from the
same panel. The browser generates a random 32-byte key using Web Crypto and
sends `gm-key-set` with `account`, `key` and the live owner `session` inside
the encrypted owner channel. `gm-key-revoke` needs `account` and `session`.
Both require a currently dubbed human GM and log the actor, target and
credential operation before changing access. Neither key nor derived key
enters an action row. The owner receives a `role` and assembles
`UOAIXGM1:<role>:<64-hex-key>`; copy that credential privately to the GM.
The browser displays the credential in a password field and stores no key
in URLs or browser storage. Leaving the panel clears the displayed key.

Roles 5 through 132 bind to the fixed admitted account slots in the stand-in.
Each role has its own direction keys, monotonically increasing replay counter
and expiring panel session. Rotation closes the prior session and copies new
derived key bytes into preallocated storage; request scratch cannot escape.
Rotation never resets the replay counter, even when a caller supplies the same
key. A GM grant revocation disables the key and closes the GM session.
Re-dubbing requires fresh key issuance. Restart clears all personal keys;
production key custody and provisioning belong to the database/image adapter.

A GM opens the same page using the personal credential. `panel-open` returns
the account name and `gm:true`; `panel-data` returns only that account's policy
and usage, aggregate health and the support queue. Every GM may read support
evidence; only a GM with the support bit may acknowledge a report, with an
action row identifying the GM. The GM receives neither connection addresses
nor other accounts' grants, balances or action history. `gm-own-log` pages
the GM's actions and owner changes targeting that GM, newest first, with an
exclusive `before` cursor. Zero selects the newest page.

`gm-action` accepts `action`, `target`, `subject`, `amount` and `detail`.
The server obtains the actor from the authenticated credential slot, assigns
the next request id and uses the server game day. Request actor/power fields
cannot elevate authority. Every action re-reads the current grant and uses
the same favor limits and audit admission as `gm-memory-act`. Non-favor
actions remain authorization-only stand-ins, explicitly labeled in the UI.
Owner controls, credential controls and mind commands return a role refusal
to a GM even when submitted directly, bypassing the displayed controls.

The adapter must restore account slots and credential bindings together or
re-provision all personal keys. Persist only durable account identity in the
production credential table; an array slot is not a cross-restart identity.

## Stand-in budget and lifetime

The stand-in admits 128 accounts, 32 item types, 32 titles and 64 plots.
Account records contain nineteen machine-word fields. Names use 65-word slots
(length plus at most 64 CCE bytes), and inventory has 32 quantity cells per
account. Title ownership is a bitmap; plot cells contain the owning account id,
or zero for crown ownership. Treasury and recipient balances are capped at one
billion. Fixtures supply initial treasury/vault resources; gameplay never mints
coin or copies an item without debiting the source.

`admin_actions` reserves 256 slots of 1024 words. Each canonical row is at most
1023 CCE bytes. Ordinary operations stop after 128 rows, reserving the remaining
128 for one successful revocation of every possible GM. Invalid or repeated
revocations cannot consume that reserve. The coin ledger holds 128 four-word
rows, keyed to the action transaction id. A full log refuses further ordinary
actions; no row is overwritten. These are stand-in admission bounds, not the
database's long-term retention policy.

Construct the store below the network scratch mark and retain one serialized
owner. Names and action rows are copied into fixed storage; request pointers
do not escape. Lookup scans at most 128 accounts; resource/quota updates are
constant work, and text generation is linear in bounded returned text. No
compiler heap/time behavior changes. `panel-new-with-gms` accepts a configured
stand-in; `panel-new` creates an empty one with zero treasury for compatibility.
Repeated request scratch restores preserve the stand-in's allocation.
Personal access adds 128 fixed session triples and 128 pairs of 32-byte keys
represented as machine-word lists, plus fixed replay/enabled cells in
`AdminAuth`. GM log paging scans at most 256 bounded rows and emits at most
sixteen. No allocation grows with the number of logins or key rotations.

## Verification

Run `pwsh -NoProfile -File apps/uoaix/test-admin-panel.ps1 -Kernel seed/Codex.cdx`.
The harness compiles and runs `proofs/GmProof.codex` alongside the existing
panel core and browser fixture. `-Poison` grades every proof entry point with
poisoned allocation. Native controls grade conservation, quota and replay
refusal, immediate narrowing/revocation, preserved usage on re-dub, title/plot
ownership, audit exhaustion and the revocation reserve. Browser controls grade
the named mode, human-only account selection, exact grant limits, narrowing,
revocation and the displayed actor/action rows. Real client authority binding,
database durability and real non-favor effects remain backend acceptance.
