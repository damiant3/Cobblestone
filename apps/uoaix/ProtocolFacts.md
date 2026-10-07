# 1.25.32 login-to-world facts

This is the implementation input for the flow requested in UOAIX.md
section 2. Reference handlers are ported under their licenses, with Sphere
Source-X first and attribution retained. Integer fields below are
big-endian unless stated otherwise. Offsets include the opcode byte.
Source agreement is distinct from acceptance by the unmodified client.

## Evidence and version selection

Measured 2026-10-04 against the client hash recorded in WireCapture.md.
RunUO and ServUO retain older packet writers, but also contain later
expansion fields. Current UOX3 targets newer clients with encryption
removed; its layouts corroborate fields, not 1.25.32 cipher selection.
POL's old-Blowfish label describes a login variant but its game dispatch
still selects Blowfish. That game-cipher assumption failed our real client.

| Source | Facts consulted |
|---|---|
| [RunUO packet writers](https://github.com/runuo/runuo/blob/master/Server/Network/Packets.cs), [handlers](https://github.com/runuo/runuo/blob/master/Server/Network/PacketHandlers.cs) | A8/8C, old character list, creation/selection fields, compression after game authentication, entry/update/equipment/light/complete packets |
| [ServUO packet writers](https://github.com/ServUO/ServUO/blob/pub57/Server/Network/Packets.cs) | Independent current maintenance of 37-byte 1B, signed z, FFFFFFFF field and map dimensions |
| [Sphere cipher table](https://github.com/Sphereserver/Source-X/blob/master/src/sphereCrypt.ini), [cipher selection](https://github.com/Sphereserver/Source-X/blob/master/src/common/crypto/CCrypto.cpp) | 1.25.32 keys 389DE58C/026950C6 and rotation-cipher game profile for pre-1.26 clients |
| [POL cipher table](https://github.com/polserver/polserver/blob/master/pol-core/pol/crypt/cryptkey.cpp), [login recurrence](https://github.com/polserver/polserver/blob/master/pol-core/pol/crypt/logincrypt.cpp), [dispatch](https://github.com/polserver/polserver/blob/master/pol-core/pol/crypt/crypt.cpp) | Old rolling-XOR recurrence; conflicting game Blowfish dispatch kept as a rejected hypothesis |
| [UOX3 outgoing packets](https://github.com/UOX3DevTeam/UOX3/blob/master/source/CPacketSend.cpp), [incoming packets](https://github.com/UOX3DevTeam/UOX3/blob/master/source/CPacketReceive.cpp) | Relay field order and entry/update packet offsets |
| [OUO version table](https://github.com/draxinar/ouo/blob/main/version.c), [connection handling](https://github.com/draxinar/ouo/blob/main/usersock.c) | Corroborates inherited game XOR for 1.25.32; game seed reinitializes the rolling state |

Downloaded source bodies remain in ignored local research scratch, not in
the depot. SHA-256 identities for the inspected packet bodies:
RunUO `52B9BE1C88C29DC11A02D91066E9F0C11CA64168E9764FCC989598C51FE534D4`;
ServUO `2D43377DB8346A421FA236925ECB7B0599A3F63DFD6D4FAF093F8F1066DCFA55`;
Sphere table `BEBA9FBBA706FCD7DD21502B003859D3430083086E34C87DF38D2670A226A72D`;
UOX3 outgoing `8E4AC304AA868C8B50B960833C7276668D0F98F5E36B4CF8BCD5B5BAC6A7812C`.

The real game capture in `game-trace35144-v2/server.log` has a clear
12345678 seed matching the pending relay key and 65 encrypted bytes. The
first encrypted byte 43 becomes 91 under rolling XOR, but 47 under the
discarded Blowfish hypothesis. XOR decodes the entire packet into the relay
key, test account and password-minus-13 with zero padding. Raw capture
SHA-256 is `551777E4BC9E6166FC9D5A34CBF1EAADDD7E90DBE8AA155F56D8124F38F81F24`;
decoded packet SHA-256 is `26B0EBFF8DCB249C71DA19E5FEDCC83EA3AD7F2A9B155DB5DBCDE0130A313C7C`.

## Streams and order

Both client-to-server connections begin with a clear four-byte seed.
Login uses the client seed; game uses the issued relay token. Each seed
starts a fresh rolling XOR state. For each byte, XOR with the low state's
low byte, then rotate both old 32-bit states right through each other's
lowest bit. XOR the resulting low state with 026950C6 and high with
389DE58C. Initial low is the low 32 bits of
`((~seed XOR 1357) << 16) OR ((seed XOR AAAA) AND FFFF)`;
initial high is `((~seed AND FFFF0000) XOR ABCD0000) OR ((seed >> 16) XOR 4321)`.
All operations use 32-bit unsigned state. No Blowfish or Twofish belongs
to this target profile.

Server login replies are plain bytes. Game replies use the UO Huffman
codebook, terminal symbol 256 and zero padding to the byte boundary;
there is no additional server-to-client XOR layer in this profile.
GameHuffmanProof and its foreign codebook oracle own compression checks.

The selected sequence is seed, 80, A8, optional A4/ping, A0, 8C, new
connection/seed, 91, A9, then 00 creation or 5D selection. World entry sends
1B, 20, each 2E equipment item, 4F and 55. The first 1B precedes world
updates; 55 completes entry. Later server implementations send additional
expansion and repeated update packets; those do not establish requirements
for this pre-T2A client. The minimal sequence and legacy A9 ending remain
subject to actual character-screen/world rendering acceptance.

## Packet profile

`Z16` means signed two's-complement 16-bit z; `Z8` its low byte. Fixed
strings are zero-padded. Character creation and selection never authorize
administrator powers. Unsupported extensions close only that connection
with the diagnostic below.

| Packet | Bytes | Fields after opcode |
|---|---:|---|
| C 80 login | 62 | account[30], password-minus-13[30], trailing client byte |
| S 82 denied | 2 | reason8; invalid test credentials use3, then connection closes |
| S A8 shards | 6+40N | length16, flag5D, count16; each: index16, name[32], fullness8, timezone8, reversed IPv4 octets |
| C A4 hardware | 149 | 148 opaque bytes; ignored without closing or relaying |
| C A0 choose shard | 3 | index16; only index0 exists in this fixture |
| S 8C relay | 11 | network-order IPv4, port16, token32; close login connection after complete send |
| C 91 game login | 65 | token32, account[30], password-minus-13[30]; token must match both seed and outstanding relay |
| S A9 characters | 372 | length16, slots8=5; five name[30]/zero[30] pairs; cities8=1; city-index8=0, city[31], building[31], zero32 trailer |
| C 00 create | 100 | markers at1/5; zero9; name10[30], password40[30], sex70, STR/DEX/INT71..73, three skill/value pairs74..79, skin80, hair82/hue84, beard86/hue88, shard90, city91, slot32 at92, client-IP32 at96 |
| C 5D select | 73 | marker32, name[30], legacy/reserved fields through64, slot32 at65, client-IP32 at69 |
| S 1B enter | 37 | serial32, zero32, body16, x16, y16, Z16, direction8, zero8, FFFFFFFF32, zero32, width16=6144, height16=4096, six zero bytes |
| S 20 player | 19 | serial32, body16, zero8, hue16, flags8, x16, y16, zero16, direction8, Z8 |
| S 2E equip | 15 | item-serial32, graphic16, zero8, layer8, parent-serial32, hue16 |
| S 4F light | 2 | level8; fixture uses0 |
| S 55 ready | 1 | no payload |
| C A7 tip/notice | 4 | last-tip16, selector8 (0 tips, 1 notices); after world entry, consume and log all selectors without a reply or disconnect |
| C/S 73 ping | 2 | one sequence byte echoed unchanged |
| C 34 status | 10 | marker32, type8=4, own serial32 |
| S 11 status | 66 | length16, serial32, name[30], HP16/max16, rename8=0, format8=1, sex8, STR16/DEX16/INT16, stamina16/max16, mana16/max16, gold32, armor16, weight16 |
| C 02 walk | 3 | direction8, sequence8; legacy target profile, no fastwalk key |
| S 22 walk accepted | 3 | sequence8, notoriety8=1 |
| S 21 walk denied | 8 | sequence8, x16, y16, direction8, Z8 |
| C 01 logout | 5 | legacy marker32; connection closes, world character remains |

The A9 profile retains count5 and its zero32 trailer. The earlier reconnect
failure reports were retracted: root's second shard click landed on Create
Character after Character Select appeared. Unchanged35318 with a
client-created character showed Character Select on relogin after three
seconds when driven with one shard selection. No server-side reconnect fix
was required. The synthetic populated A9 capture remains valid byte evidence
(`live-character-list35318.bin`, `.huffman`, `.reference.json`); its exact
RunUO compression match was independent of our decoder. It was not a capture
of the failed GUI interaction.
Speech 03/1C is specified in GameServer.md and is separate from entry.
The real 35195 client rendered its character in Britain, then sent A7.
RunUO's `RequestScrollWindow` reads the fixed fields without a response;
UOX3's `CPITips` corroborates the four-byte layout. This optional request
has known framing and is ignored safely. Unknown-length opcodes still
close only their connection after the bounded diagnostic.

The real `server35168/server.log` connection 1 capture establishes a
100-byte creation followed by two `7300` pings. Waiting for 104 consumed
those pings as clothing hues. The [older creation handler](https://github.com/draxinar/ouo/blob/main/packet_handler.c)
and [version-specific lengths](https://github.com/draxinar/ouo/blob/main/version.c)
corroborate the distinction: the two trailing hue words belong to 1.26+.
The captured creation alone has SHA-256
`423BA38675AE43797E5830316EE3F640DD3ED51667D9BA51F8CBA24212A8ABF6`.
Stored character metadata retains its 104-byte normalized representation:
the 100 wire bytes followed by two server-owned clothing hues, initially zero.

## Admission and diagnostics

TCP chunks are not packet boundaries. Seed bytes, packet bodies and
successive packets can split or coalesce arbitrarily. Preserve XOR state
between chunks and packets. An empty receive window while TCP remains
open is not EOF. The server must survive unknown opcodes, impossible
lengths, invalid credentials, stale keys and invalid creation fields.

Each refused connection logs its connection number, selected stream/cipher,
decoded opcode (or unavailable before decode), reason and at most 256 raw
bytes from a bounded recent-byte buffer. One malformed form closes that
connection after the log; the outer accept loop continues. No unbounded
pre-authentication trace is retained. Expected logout/relay closes remain
named packet events. A mid-packet EOF logs the incomplete form.

## Legacy A9 and login comparison

The same-guest baseline selected the character, invalidating the proposed
count, trailer and A8-flag fixes. Root subsequently identified the GUI
second-click error and confirmed the unchanged production profile after
client creation and a three-second relogin. The count patch was discarded.
City-id1 still caused a distinct client crash and remains excluded.
The archived [UOX writer](https://github.com/UOX3DevTeam/UOX3/blob/d416aadc56a22c6e0b7bf30b1872e2fc3c9784e4/source/packets.cpp)
uses occupied count, always emits five physical slots, and omits a trailer
(`CPISecondLogin::Handle`, `CPCharAndStartLoc`). This is a 2003 retained
legacy writer, not evidence that the entire server targets 1.25.32.
[Sphere 0.52 login](https://github.com/Sphereserver/Source-Archive/blob/main/0.52/GraySvr/CClientLog.cpp)
has an explicit pre-1.26 server-index branch; its
[character-list writer](https://github.com/Sphereserver/Source-Archive/blob/main/0.52/GraySvr/CClientMsg.cpp)
and [wire definitions](https://github.com/Sphereserver/Source-Archive/blob/main/0.52/Common/grayproto.h)
use count 5, five entries, one-based city IDs and no trailer.
[POL login](https://github.com/polserver/polserver/blob/master/pol-core/pol/login.cpp)
corroborates the old 30/30 and 31/31 widths but is modern: its unconditional
feature prelude, one-based server index and expansion flags are not evidence
of a required pre-1.26 sequence. No pre-1.26-specific POL source was found
within this comparison; that version-coverage gap is explicit.

| Field/order | Production baseline | Reference difference or agreement | Controlled run |
|---|---|---|---|
| A9 opcode and length prefix | A9, big-endian16 | All agree | Unchanged |
| A9 byte3 | 5 | UOX occupied count; Sphere/POL five slots | Variant1 selects and enters, but baseline also selects; no causal conclusion |
| Physical entries | Five name30/password30 pairs | All agree | Unchanged |
| Names/passwords/padding | Stored name, zero password and padding | Same intended fields; Sphere writes empty-string terminators | Unchanged; no arbitrary padding experiment |
| City count and strings | One Britain/Sweet Dreams Inn entry | Server configuration, not a fixed protocol count | Variant6 advanced synthetically only; no real-client grade |
| City record width | index8, city31, building31 | All old layouts agree | Unchanged |
| City index | 0 | Sphere emits i+1; UOX/POL i | Variant2 id1: client APPCRASH c0000005, CLIENT.EXE+0x61186; keep0 |
| A9 trailer | zero32, length372 | UOX/Sphere omit it; later POL/RunUO append flags | Variant3 selects; baseline also selects; no causal conclusion |
| A8 flag | 5D | Sphere/POL FF | Variant4 selects; baseline also selects; no causal conclusion |
| A8 server count/index | One server, index0 | Sphere pre-1.26 agrees; modern POL index1 is outside established target branch | Unchanged |
| A8 name/fullness/timezone/IP | UOAIX,0,0,loopback; fixed field widths | Configured metadata; Sphere name30 plus zero16 equals our padded32 | Unchanged |
| 8C endpoint | loopback,2593, big-endian port | Same wire order; endpoint configured | Unchanged |
| 8C token | 12345678 in controlled baseline | Sphere/UOX use loopback token7F000001; POL comments name olderFFFFFFFF | Variant5 advanced synthetically only; no real-client grade; POL value untested |
| Login/game cipher boundary | fresh seed; measured rolling XOR; game Huffman | Target capture and Sphere pre-1.26 branch agree; modern POL game Blowfish was rejected by actual bytes | Already measured, unchanged |
| 91 to A9 order | A9 directly | Sphere/UOX listed login paths agree; modern POL B9 prelude belongs to later profile | No unsupported B9 injected into this client |
| 81/86 character packets | Not sent on game login | Sphere uses them for switching/deletion, not Setup_ListReq | Not a login-path difference |

The diagnostic guest calls the production handler and changes one selected
field or structural section per valid login. It uses a pre-created Uoaix
Tester and fixed baseline relay token, restores all baseline fields for
each run, and prints complete outgoing A8/8C/A9 bytes with mode and
connection. Retries can advance the input list: a result is usable only
when root's run matches the emitted mode. Unknown input/EOF refuses.
The source, input and log are under reek `build-output/uoaix/semantics/`.
The city-count experiment also changes dependent length/records; it is a
structural test, not a one-byte test. Client screens are root observations,
not inferred from the guest's pre-send log.

Reference file SHA256 identities in local research scratch: UOX packets
`848A9A54E3FCCF34F20E83AC97138F033666B0F4421B41BE835DD8F11380F366`;
Sphere character writer
`C93FE9021FC9E117B562FA392562FD0AEA3F726D9743FD600993A18AEA29ECC6`;
Sphere login `88EAF135AA37DE6D7B7610B1E07791ED9E79BD641D12B557BE4A7C24E5B3F824`;
Sphere wire `9C8E22E1536372A05E5F0891B6B1CC9ABCEE2280CC8C7D24B3477C957BD5504F`;
POL login `9039A7BED044EE5AC749A33A5869C83162C0C5B7D8126A2E2264EB4C9B2CB56F`.

The earlier same-shard synthetic readback retained Uoaix Tester in slot0
(`build-output/uoaix/live-character-list35318.bin`, `.huffman`, `.json`).
It sent no create/select, advanced relay state, and was not real connection3.
Unchanged pinned RunUO compression of a separately constructed packet
matched the captured120 bytes (`.reference.json`), compressed SHA256
`43F751F104F55406ED6F687E32C12F9C62A7FC5241EF4D4B4B990DF2C71BB447`.
That excludes a compression discrepancy for this packet, not a target-client
interpretation difference.
Control receipt: reek inbox e4cdab278c874ef5fdd9526442c20d52.json.
Diagnostic variant0/connection15 sends baseline A8/A9 and relay12345678;
the earlier production relogin used an incremented relay token. That is
another condition to control. The baseline A9 is byte-identical to the
35318 synthetic readback; this does not prove the complete failed real
connection's byte stream or UI event sequence.

## Proof and acceptance boundary

Every outgoing profile message needs a complete-byte fixture, including
reserved fields, signed z and padding. Incoming fixtures cover complete
messages, fragmentation, coalescing, truncation and rejected field values.
The captured real 91 frame must decode byte-for-byte and authenticate in a
native replay. Socket replay must decode replies independently and keep
accepting after each malformed connection. A replay cannot establish a
client screen: root records real-client acceptance separately for shard
list, relay, character list, creation/selection and world entry.

Current real evidence establishes login, shard display, selection, relay,
the empty-account transition into creation, successful creation and a
rendered character in Britain. On 35206 optional A7 handling and five real
walks passed with no refusal. Prior reconnect failures on 35206, 35220 and
35318 were client-driver artifacts.
Root confirmed production reconnect after client creation; prior failure reports were retracted. Compiler heap/time behavior is unchanged
by this fact register; implementations carry their own cost verdicts.
