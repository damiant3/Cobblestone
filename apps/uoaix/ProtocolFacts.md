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

The captured real game login has a clear seed matching the pending relay key
and 65 encrypted bytes; the first encrypted byte 43 decodes to 91 under rolling
XOR (47 under Blowfish), and XOR decodes the whole packet into the relay key,
account and password-minus-13 with zero padding.

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

Speech 03/1C is specified in GameServer.md and is separate from entry.
The real client sends A7 after rendering its character in the world.
RunUO's `RequestScrollWindow` reads the fixed fields without a response;
UOX3's `CPITips` corroborates the four-byte layout. This optional request
has known framing and is ignored safely. Unknown-length opcodes still
close only their connection after the bounded diagnostic.

A real capture establishes a 100-byte creation followed by two `7300` pings. Waiting for 104 consumed
those pings as clothing hues. The [older creation handler](https://github.com/draxinar/ouo/blob/main/packet_handler.c)
and [version-specific lengths](https://github.com/draxinar/ouo/blob/main/version.c)
corroborate the distinction: the two trailing hue words belong to 1.26+.
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

- A9 keeps count 5, five physical slots and the zero32 trailer. With one shard
  click, the unchanged profile selects a character and reconnects after
  creation; count, trailer and A8-flag variants also selected, so none of them
  is a requirement.
- The A9 city index is 0: index 1 crashes 1.25.32 (APPCRASH c0000005,
  `CLIENT.EXE+0x61186`).
- The references differ and none targets this client alone. The archived
  [UOX writer](https://github.com/UOX3DevTeam/UOX3/blob/d416aadc56a22c6e0b7bf30b1872e2fc3c9784e4/source/packets.cpp)
  (2003) uses the occupied count, five physical slots and no trailer.
  [Sphere 0.52 login](https://github.com/Sphereserver/Source-Archive/blob/main/0.52/GraySvr/CClientLog.cpp)
  has an explicit pre-1.26 server-index branch; its
  [character-list writer](https://github.com/Sphereserver/Source-Archive/blob/main/0.52/GraySvr/CClientMsg.cpp)
  and [wire definitions](https://github.com/Sphereserver/Source-Archive/blob/main/0.52/Common/grayproto.h)
  use count 5, five entries, one-based city IDs and no trailer.
  [POL login](https://github.com/polserver/polserver/blob/master/pol-core/pol/login.cpp)
  corroborates the 30/30 and 31/31 widths but is modern: its feature prelude,
  one-based server index, expansion flags and B9 prelude belong to later
  clients, and no B9 is sent to this one.
- 81/86 are Sphere's switching and deletion packets, not part of the login path.

## Proof and acceptance boundary

Every outgoing profile message needs a complete-byte fixture, including
reserved fields, signed z and padding. Incoming fixtures cover complete
messages, fragmentation, coalescing, truncation and rejected field values.
The captured real 91 frame must decode byte-for-byte and authenticate in a
native replay. Socket replay must decode replies independently and keep
accepting after each malformed connection. A replay cannot establish a
client screen: root records real-client acceptance separately for shard
list, relay, character list, creation/selection and world entry.

The real client is accepted for login, shard list, selection, relay, creation,
world entry in Britain, A7 handling, walking, and relogin after creation.
