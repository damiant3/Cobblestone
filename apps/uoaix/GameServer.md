# Game server

## Account creation is a runtime mode (Damian, 2026-10-05)

"make account creation a runtime mode so dev builds can have autocreation and
public servers can have a more managed process to prevent spammers and cheaters."

The mode is a launch input, like testing mode. Development: an unknown name and
password create the account on first login. Public: no login creates an account;
accounts come from British's `[account` command ("Managed accounts" below).

Prebuilt owner account (Damian, 2026-10-05): "we need a prebuilt account named
British and it has the Lord noteriety title. default password for that account
is Astronaut". Every world starts with account British, password Astronaut, whose
character carries the Lord title. The default password ships in this source, so
a public release build starts only after the operator sets a new one (Damian,
2026-10-05: "when running a public release build, you should prompt for a new
password before it is turned on."). The launch script prompts for British's new
password; the server refuses public mode while British still has the default.
The 1.25 client itself refuses the character name "Lord British", so the title
is server-applied, not part of the typed name.
The British account holds a character named British: at boot, when none of
its five slots holds one, the server creates it in the first empty slot
(`cg-british-character`, the client's creation path without entering the
world) and logs "BRITISH character created in slot N" or "BRITISH character
present"; characters in the other slots are left alone. A full account with no
British logs "BRITISH character not created". `proofs/BritishCharacterProof`.

British is the admin character (Damian, 2026-10-05): "this is the admin character,
and should have all the invuln and extra powers". British cannot be damaged or
killed, and holds every staff power: the admin panel's actions (teleport,
invisibility, summon item, summon creature, move, inspect) as in-game commands,
and the testing commands ([go, [where) in every mode, not only testing.

## Accounts

`GameAccounts.codex` holds 64 accounts of 880 bytes: name, 16-byte salt,
PBKDF2-HMAC-SHA256 hash at 20000 iterations, and the account's five
character slots. `GameShard.characters` is the bound account's working copy;
`ga-sync` writes it back before every encode. A fresh table holds British
(index 0) and is bound to it. Names are 1..30 letters and digits, compared
without case; passwords are 1..30 printable characters.

0x80 admits an existing name with its password, or in development mode an
unknown name while a record is free, and creates nothing; a refusal answers
0x82 reason 3. 0x91 repeats the check, creates the development account,
binds its slots and touches the world revision so the route commits. Creation
takes the first free slot, whatever slot the client names. UGC1 version 2
carries the table; the mode and the boot's RDRAND words are zeroed in it.

**Testing stock (Damian, 2026-10-08).** A new testing character's backpack stays light. Its bank box holds one bag per profession, each fully stocked with every tool of that craft and enough of each material to make one of each item it can make (the trowel included); the bank box has no item-count or weight limit. An item crafted with a tool is created inside the bag that holds that tool, so a tester takes a bag, tries every item, and puts the bag back ("lets group them by profession, and include the materials we currently put in the bank box. lets keep the initial backpack light ... make bank boxes virutally unlimited in item count and weight. make bags grouped by proffsion fully stocked with all tools and amounts of each material components of their craft sufficient to build one of each item ... items should be created inside the bag with the tool used to create them.").

Launch: `start-composite-game.ps1 -Testing` (dev accounts, testing stock),
`-Dev` (dev accounts), or neither (public). Public prompts for British's new
password, empty keeping the current one, and sends it once in the launch
record, which the guest wipes from its serial ring and the launcher deletes;
the guest logs `MODE composite PUBLIC` and refuses to start while British has
the default. `-Hosted` refuses `-Testing` and `-Dev`.

British's powers apply only after a game login as British (`ga-is-british`:
British bound and the transient login flag set). The composite marks
British's combat actor immune at entry, so combat and harmful spells refuse
it; the paperdoll and a single click on a character show "Lord <name>"
for British in session. A single click on any other player's character, online
or logged out, answers its name from that character's account record
(`gsi-account-character`). A logged-out character stays in the world 300 s and
then leaves every view until its player enters (WorldTimers kind 7);
`[go x y` (teleport) and `[where` work in every mode; `[goto <place>`
teleports to a named place, `[goto` alone lists the places, one line per
town (UOAIX-23), and `[goto brit` lists every place whose name starts with
the words typed, without case. Names are the town prefix (brit, trin, minoc, vesper, yew,
moonglow, magincia, jhelom, skara, cove, buc, nujelm, ocllo, serp, wind) plus bank,
moon, healer, smith, inn or spawn; a second site of a kind is numbered from 2
(britbank2). The 1.25 map's dungeons are listed by name at their entrances (covetous,
deceit, despise, destard, hythloth, shame, wrong, ice, fire, orccave; ServUO's Felucca
go list, Ice and Fire by their Britannia entrances); the Lost Lands are not on it. `GotoPlaceData` is generated by
`import-goto-places.ps1` from UOX3 location.dfn banks, `GameMoongates`, town
spawn regions for healers, smiths and innkeepers, and the nearest world-spawn
region within 200 tiles; Britain's rows are hand-placed. Wind has no bank or
smith, Magincia no healer, smith or inn, and the islands Magincia and Nujel'm
no spawn region in that range. `[add art-hex [count]`
summons an item stack into the backpack and commits; `[add <item name> [count]`
(UOAIX-69) does the same by TILEDATA name, case-insensitive, spaces allowed
(`[add longsword`, `[add bread loaf 5`). A token of at most four hex digits that
holds a digit or is four long is art (`[add bed` is a name). Several exact
matches are one item's art variants, so the lowest art is added and the reply
names the others; with no exact match, one partial match is added, several are
listed (eight at most, with their art), and none refuses. Names match the
singular form only. The scan reads all 16384 entries under a heap mark and
retains nothing (`proofs/ItemTableProof`). `[skill <name|id|all> <0.0-100.0> [target]` (testing mode) sets a skill of the speaker or, with `target`, of a character of the same account picked by cursor: the session's ClassicSkills level, the economy actor's mapped skill and live Wrestling, Tactics and Anatomy (never the character record's creation skills, which every save validates); a name is any unique prefix without spaces (`[skill swo 90`). `[attr <str|dex|int|all> <1-255> [target]` (testing mode) sets strength, dexterity or intelligence the same way, in character record bytes 147..149 (0 reads the creation stat at 79..81), which every stat reader takes through `gp-stat`; the status bar is resent, a lowered strength lowers held hits and a lowered dexterity lowers stamina spent. `[help` lists the British commands. Every bracket command has a help mode (Damian, 2026-10-08: "all commands need a help mode, by default if it requires params, then the command alone is a help call. if a command is parameterless otherwise, add a help parameter"): a command that requires parameters answers its usage when typed alone, and a parameterless command answers its usage to `[<command> help`. `[invisible` toggles
invisibility: the client draws British hidden (0x20 flag 0x80) and monsters do
not hunt British; one character plays at a time, so no other client is shown
British, and the state ends at restart. `[summon body-hex` creates a creature
beside British and commits. `[inspect` opens a target cursor and prints the
target's serial, graphic, hue, place, amount and health. `[move` opens a
target cursor for a ground item or a creature (not a character, not a
contained item), then a location cursor, and commits the move. `[remove`
(`CompositeRemoval`) opens a target cursor and deletes the targeted item, with
everything inside it, or NPC, then logs `REMOVE` with its serial, graphic and
place and commits. A deleted vendor lot leaves the ledger as consumed goods, and
a monster or spawn slot frees at its next visit. A removed resident dies in the
town (`tf-death`): its lots leave the ledger, its owner row and clothes go, and
the town, the live network and their codecs pass over a dead resident, so the
world saves and restores with it. A removed vendor or banker is concealed for
good, not deleted: its serial joins the saved removed list (7 at most), it
trades no more (`gv-checkout` refuses its actor), banks no more and answers no
speech, and every boot conceals it again (`crm-reapply`); its rows, lots and
containers stay, so a vendor's row index (its shop role) holds. Re-staffing a
shop is the case that needs a true delete. Refused: player characters, British,
the other townspeople (workers, the economy NPCs), their goods other than a lot,
moongates, shrines, and a player's backpack or bank box. `[kill` opens a target cursor and deals a creature its full hits from
British through the spell damage path (corpse, loot, respawn), logs `KILL` and
commits; players, British, townspeople and items are refused.

## Managed accounts

In every mode, British creates an account with `[account <name> <password>`
(`CompositeAccountCommands`, `ga-create`) and lists them with `[accounts`.
The password reaches only the salted hash: no reply, log line or journal
byte carries it, and every outcome answers a sysmessage (a refused route
logs the raw packet). This is the public shard's only account path.

## Help pages

In game, 0x9B (Request Help, 258 bytes, no text) queues a page in the
composite's ring of 32 (`CompositeHelp`, `CompositeGame.pages`): the
character's serial, position and time. One page per character is pending; a
repeat answers the same id. The player gets a sysmessage with the page id,
the server log a `HELP page=` line, and a full ring refuses with a
sysmessage. British lists pages with `[pages` and closes one with
`[pageack <id>`. The ring is durable: it is the composite checkpoint's UHP1 section
(UCC1 version 18, `cc-write-pages`), so pending pages survive a restart.

## Entry and ownership

`GameServer.codex` boots the citable `GameNet.game-server` entry, using
`WorldRecords`, `MulReaders` and the read-only MUL bridge. The captured real
game login establishes rolling XOR and password-minus-13 for 1.25.32
([ProtocolFacts.md](ProtocolFacts.md)); [WireCapture.md](WireCapture.md) owns
the capture provenance. Packet framing follows the 1.25.32 length table and
skips unknown packets; 0x83 deletes a character.

`GameDispatch.md` owns opcode framing, authenticated routing and pulse
integration. The `game-server` entry keeps the core handlers and
`game-server-with` binds lane handlers explicitly; a per-opcode handler lives
in its lane's chapter, returns unhandled without mutation, and publishes only
after the enclosing commit. [GameLinks.md](GameLinks.md) owns multi-session
integration and [GameClientView.md](GameClientView.md) presentation.

Driving the real client: select the shard once and read the resulting screen
before any further click (a second click lands on Create Character). A scratch
executable with an inbound listener raises a Windows firewall prompt. Old
`run.json` files do not authorize operating a live process.


## Run

From the repository root, with host ports 2593 and 2595 free:

```powershell
pwsh -File apps/uoaix/serve.ps1 -ClientRoot C:/Users/Damian/uo1998-client -Kernel seed/Codex.cdx
```

The launcher compiles with the explicit compiler, admits RAM, starts the
read-only adapter and a detached codex-vm guest, and records owned PIDs and
logs in `build-output/uoaix/server/run.json`. The launcher stays attached
to its children and stops only those children on exit. `LISTEN game-server`
in `server.log` means the map preload completed. The guest listens on 2593;
host forwarding is `-portfwd 2593:2593`, with outbound `-natmap 2595:2595`.
The service does not launch or operate a client. Root schedules the real
client run with Damian before any action that takes keyboard focus.

The separate client install and synthetic account are specified in
`UOAIX.md`. The entry runs development accounts, so `uoaixtest` / `uoaixtest`
creates its account at first login. State is volatile:
characters survive TCP reconnects, not a server restart. `WorldDisk` is a
separate persistence integration, not implicitly used by this entry.

The normal entry preloads the 64x64 window beginning at (1408,1680), which
contains the Britain spawn (1420,1698). Movement refuses outside the loaded
window, including the final border needed for four-corner height queries.
This is a bounded Britain area, not a claim that the whole map is served.
The shard-list flag is 5D. Both inbound streams use the rolling XOR
profile in [ProtocolFacts.md](ProtocolFacts.md); game server replies use
the fixed UO Huffman codebook. The game seed reinitializes XOR state.

## Session contract

`gs-new` allocates one shard. `gs-relay-key` issues one pending single-use
relay token, and `gs-session key` creates one connection state. `gs-handle`
takes complete decrypted packets; malformed input returns `Err`, with no
claim of a reply. `gn-chunk` owns TCP fragmentation, login XOR, game cipher,
compression and send completion. The raw accept path preserves handshake
payload without applying the repository message-framing protocol.
Unexpected forms close only their connection after a `REFUSE` line names
the connection, stream/cipher mode, decoded opcode, reason and at most
256 recent raw bytes. The receive ring is fixed in size. Partial packets
at EOF receive the same diagnostic; the outer accept loop continues.
An empty raw receive can mean an exhausted polling window rather than
peer EOF. The connection owner preserves an open transport across that
window and reclaims receive scratch before waiting again. A peer FIN,
closed connection or stale transport still ends the session. Login stays
available while the player reads the shard list; A4 hardware information
does not close the connection or send the A0 shard selection on its behalf.

Admitted game packets are 91 game login, 00 creation, 5D selection, legacy
three-byte 02 movement, 34 own-status query (type 4), 73 ping, A4 hardware
information and 01 client logout. Version-dependent layouts remain client
acceptance hypotheses. Unknown opcodes close with an explicit diagnostic.
After world entry, the four-byte A7 tip/notice request is consumed and
logged with its id and selector; no content is served and the connection
continues. This follows the ignored optional request in the written facts.
The server also admits variable-length 03 ASCII speech: normal, emote,
whisper and yell, with 1..128 printable ASCII characters, a final zero,
hue 0..1001 and font 0..9. Framing reads the three-byte prefix, checks
the declared length 10..137, then waits for exactly that packet before
admitting another. Short, over-budget or inconsistent lengths refuse.

Speech returns a 1C message to the active client using the server's mobile
serial, body and stored character name. Client-supplied system/command
speech types are refused. `gp-speech-text` converts validated speech to CCE
for future simulation consumers. Range-based recipients, NPC conversation,
duel phrase handling and chat persistence remain integration work; the
current server still admits one active mobile. The wire layouts follow
[ModernUO's packet definitions](https://modernuo.com/packets.html), retaining
the existing 1.25.32 client-acceptance limitation.

A9 includes the trailing zero dword in the reference writers.

Creation admits five slots. Names, stats, skill choices, hues, appearance,
starting city and slot are checked on the 100-byte legacy request. The
stored record appends four server-owned clothing-hue bytes, initially zero,
preserving the 104-byte checkpoint representation. The slot is a 32-bit
field at offset 92, rewritten to the first free slot before storage;
following pings are separate messages. Validation
precedes object creation. A failed
equipment allocation rolls back newly created objects. Clothing, optional
hair and beard use item serials with the mobile as container. Selection
returns the existing mobile and equipment. Status reflects the stored
creation stats. A facing change turns in place; a repeated facing attempts
a step. A rejected sequence or step returns authoritative position and
resets the expected sequence. Sequence 255 advances to 1.

Movement uses four-corner land interpolation, wet/impassable flags, static
surfaces, bridge half-height, sixteen units of headroom and a two-unit step
limit. Diagonals require both orthogonal sides to be traversable. Dynamic
world-item and mobile collision and placed multis are not integrated yet.
The map loader checks every range/decoder result and refuses collider
budget overflow; missing or corrupt data never becomes a zero-height tile.
Original client bytes are read in place, held only in guest scratch memory,
and reclaimed after decoding. No MUL bytes are added to the repository.

## Budgets and lifetime

One shard owns 64 world slots, five 160-byte working character slots and a
56384-byte account table. One
connection owns a 40-byte `GameSession`, a 104-byte `GameInput`, two 256-byte
buffers for packet assembly and recent raw bytes, and an 88-byte XOR/seed
decoder. Record sizes follow the native packed layout rounded
to eight bytes. The Huffman lookup is 2056 bytes, initialized once. Temporary codebook lists
are startup cost, not per-connection allocation.

`WalkMap` has a 48-byte wrapper and 224 bytes per loaded cell: 24 land bytes
and eight 24-byte colliders plus their count. Windows are bounded at
256x256 cells. Preload scratch includes 1036288 tiledata bytes, a 196-byte
map block, 12 index bytes and 65536 static bytes, reclaimed on success.
Initialization is linear in loaded data. Coordinate lookup is constant
time; a movement checks at most eight candidate surfaces against eight
colliders per queried tile. Packet work is linear in packet length. No
growing world index or per-byte cipher record is allocated.

The network arena is allocated below outer restoration marks.
The retained NetCompact set is a 16-byte header and three 131072-byte
arenas. Each connection also uses a 4096-byte transport receive buffer and
the driver's bounded frame buffer. Session,
world and map state stay below the per-chunk mark; the latest transport is
copied into its arena before transient chunk memory is reclaimed. Send
failure retains the latest transport for close/release.

## Replay proofs

Compile each native proof through `build/compile.ps1` with explicit `-Src`,
`-Out`, `-Log` and `-Kernel`, and run through `build/test-run.ps1`.
ProtocolProof, LoginWireProof, GameSessionProof and GameHuffmanProof compare their entire
output with the corresponding `.expected` file, removing CR only.
GameSessionProof covers bounded speech and spoof/control refusals. The
socket replay sends a speech packet in individual byte writes, then a
maximum-size speech packet and ping in one write, and verifies identity,
payload, packet boundaries and malformed-length closure. Host writes may
coalesce in TCP; client rendering remains unproved. Speech uses bounded
request scratch and linear validation/packet construction; no persistent
per-message state is added.
`test-game-server.ps1 -IdleLoginSeconds 60` adds a quiet interval after
A4 and before A0 on the first login. The replay must still receive the
relay and complete its subsequent game-session checks. This exercises
human-paced shard selection without operating the real client.
WalkLoadProof uses the independent original-file checker below. These are
focused app checks, not additions to the battery.

- `proofs/GameSessionProof.codex`: login/key admission, creation refusals,
  world objects and packet fields, turning, movement, sequencing, walls,
  diagonals, water, bridges, headroom and collider capacity.
- `proofs/GameHuffmanProof.codex`: every single byte, empty input and a
  4096-byte pattern against unchanged RunUO compression, plus input bounds.
- `proofs/ProtocolProof.codex`: complete-byte packet vectors from the
  written facts, captured real game-login decoding/authentication, and raw
  diagnostic ring bounds. This includes signed entry z, reserved bytes
  and movement-denial coordinate ordering.
- `proofs/WalkLoadProof.codex`: the real adapter and original files, with
  twenty printed height samples. Use its `.vmargs` with the runner. Compare
  samples independently with the original map, not a generated map fixture:

```powershell
pwsh -File apps/uoaix/proofs/test-map-load.ps1 -Actual <WalkLoadProof runtime output> -ClientRoot <original install>
```

This proves terrain decoding/interpolation at the named coordinates. The
socket replay checks movement replies and sequencing; neither grade alone
proves the unmodified client's walking/height acceptance in `UOAIX.md`.

The foreign sources stay outside the depot. Fetch these files into a
reference directory; the scripts refuse hashes different from the measured
versions:

- [RunUO Compression.cs](https://raw.githubusercontent.com/runuo/runuo/master/Server/Network/Compression.cs)

`proofs/huffman-oracle.ps1 -ReferenceSource <Compression.cs> -OutFile <file>`
regenerates the Huffman golden using unchanged RunUO code and a platform
shim. Socket replay creates inbound XOR bytes independently in PowerShell
from the written recurrence and uses the captured real frame as an
additional native oracle. The standalone Blowfish library/proof is not
part of this client's active stream.

For the socket replay, keep the adapter on loopback 2595, compile
`proofs/GameServerReplay.codex`, and boot the result with
`-portfwd 2594:2593 -natmap 2595:2595`. This entry loads a 32x32 window and
leaves the stage-0 capture listener on 2593 untouched. Alternatively the
launcher accepts `-Replay` and owns both the adapter and this replay guest;
ports 2594 and 2595 must be free. Run:

```powershell
pwsh -File apps/uoaix/proofs/test-game-server.ps1 -Port 2594 -CompressionSource <Compression.cs> -Evidence <scratch-directory> -ServerLog <server.log>
```

The replay requires a fresh empty shard. It grades encrypted TCP login,
relay, compressed replies, creation, equipment, status, twenty movement
requests, logout, and character selection on a new connection. Its captured
packets are synthetic evidence and never count as a real client screen.

Wire layout references are [Jerrith's guide](http://uo.torfo.org/packetguide/)
and [RunUO packets](https://github.com/runuo/runuo/blob/master/Server/Network/Packets.cs).
Land triangulation is described by
[RunUO Map.GetAverageZ](https://github.com/runuo/runuo/blob/master/Server/Map.cs).
