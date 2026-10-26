# GitHub Update 71

Covers main 45585 through 48729. The UOAIX server is audited end to end: client
packets, the admin HTTP port, logins and player text are bounded, per-pulse
walks over every player, site and slot give way to indexed lookups, and the
coroners arrive to bury the dead. UOAIX Assist grows cave mining,
lumberjacking, feather hunting and a full tailoring economy circuit, a walk
planner that searches by A*, and its own test suites with a virtual clock and
coverage. The network stack and codex-vm's port forwarding are hardened, the
SASS plug emits SM 8.9 code with tensor-core WMMA, the Perl and Zig plugs widen,
CostModel 5.7 measures builtins that were unknown, and six pull requests from
an outside contributor land. The release seed is `967435BD` (signed and
self-verified); full digests are recorded in `TechnicalDetails.md` at release.
Numbers in parentheses are main changelists.

## Compiler

- **CostModel 5.7 registry rows**: builtin allocation rows leave `unknown` for measured costs: `vec-eq`, `vec-ne`, `vec-approx-eq` and `vec-approx-exact` fixed at 16 bytes; `__list-head`, `__list-len`, `__heap-save`, `__deck-pos`, `text-to-double-bits`, `__self-type-defs`, `__record-set`, `key-status`, `run-state`, `get-state`, `set-state`, `block-sector-count`, `net-status`, `net-get-hwaddr`, `block-select` and `await` allocate nothing; the effect beds find `par`, `race` and the `s-*` builtins broken, registered as COMPILER-133 and COMPILER-134 (val 47417, 47451, 47457, 47581, 47593, 47604, 47629; red 48714).
- **Heap-window census closed**: `net-recv-heap` windows build their frames before the measurement, and CostModel 5.6 and the 3.5 `to-unicode` and literal-list costs are corrected to what was measured (val 47381, 47391, 47395, 47494).
- **COMPILER-132**: bare-metal `read-line` reads the stdin ring before polling the serial ports (fester 47275).
- **Independent rechecker**: derives constructor instantiations, polymorphic call-site instantiations and instantiations inside polymorphic bodies; kill-rate 27 of 27 (val 47266, 47316, 47359).

## Kernel and OS

- **TCP and IP hardening**: unpredictable initial sequence numbers; FIN/ACK guards, in-order data and a pinned peer, so a connected session takes segments only from its peer; IP options, fragments and a bad TCP offset refused; a FIN carrying data delivers it; a segment for another host refused; the SYN_RECEIVED ack is checked and FIN_WAIT half-close data delivered (val 46578, 46584, 46597, 46617, 46629; blu 46716, 46724).
- **codex-vm port forwarding**: forwarded data starts at the NAT ISN + 1 (a 1-bot soak went from 8 disconnects to 0); `-portfwd` binds 127.0.0.1 and `-portfwd-any` opts in; un-ACKed resends on a forwarded connection back off, doubling to a 1.6 s cap; NAT timers and serial capture run on wall time (val 46545, 46699, 46828; reek 46132; fester 46375).
- **GSP host queue (GSP stage 5)**: the GSP-RM queue transport `NvGspQueue`, graded against tinygrad (reek 47658).

## Plugs

- **SASS plug, SM 8.9 (GSP stage 2)**: an SM 8.9 encoder graded by turingas (integer, move, memory, exit, PRMT, LOP3, FMNMX, MUFU, F2F, FADD, FMUL), with target-neutral lowering that leaves the SM 5.2 listing byte-identical; 64-bit multiply, signed division by a power of two, loops as guarded backward branches, y and z indices, 16- and 32-bit device memory, shared memory and `sync-threads`, cp.async, WARPSYNC, and WMMA as HMMA.16816.F32 from global and shared memory; `GemmKernels.codex`'s `kernel-gemm-wmma` lists 800 of 800 words against turingas (reek 47101, 47116, 47124, 47157, 47182, 47190, 47211, 47228, 47230, 47240, 47247, 47590, 47624, 47626, 47639).
- **SASS plug registers**: registers freed per statement, one predicate per `if` with a busy mask and else-if chains, `if` registers reused, unread bindings keep none, loads allocate the destination first, named register refusals, tail-position loops copy no caller registers, and a register mask allocator; 11 of 15 GEMM kernels list; whole-plug cost is linear (reek 47552, 47559, 47562, 47568, 47574, 47585, 47597, 47601, 47644, 47646, 47648; fester 45986).
- **Perl plug**: 59 of 59 on `plug-oracle-arith` and part of `plug-oracle-test`; bit, heap and sized-vector builtins, let-bound function locals, integer pow, Text equality, wrapping fields, unit types, `print-line` and `print-uni`, and a copied local map, at 32 of 71 ops (red 46514, 46582, 46607, 46645, 46662, 46669, 46677, 46687, 46691, 46705).
- **Zig plug**: `fail` raises only where called and passes through closure values, a shadowing parameter is applied rather than the definition, a defined `fail` is not the builtin, `is-whitespace` and wrapping fields lower, forms for Zig 0.17, block device, process scope and FileSystem host calls, and a third shake mask that lifts the part ceiling to 189 (GitHub issue 165) (red 46464, 46476, 46526, 46694, 46696; val 48709, 48710, 48721).
- **HTML plug**: dialogs and widget labels set their text by `textContent` (numeric and 19 named entities decoded), so user text never runs as markup, and `event-source-then` (val 45865, 45875, 48711).

## UOAIX

### Security audits

- **Client packets**: a view gate refuses objects past 18 tiles for labels, status, attack, paperdolls, map charts and pet aims; house ban and eject move only a player inside the holder's house; board posts need the board within 2 tiles; a single click answers only for an object whose outermost holder is within 18 tiles (val 45771; red 45649).
- **Admin port and logins**: an oversized Content-Length no longer halts the shard; GM key commands past 128 accounts index the right credential; logins are throttled per account (1 s doubling to 32 s) with a PBKDF2 budget spent only by refused passwords; board post subjects and lines end at their first byte outside 32..126 (val 45804, 45856).
- **Forged and dangling state**: battleship buttons past square 100 and forged skill and attribute cursor contexts are refused; a lift from a bag inside a trade box clears both accepts; ship actors, skill buffers and cast walkers no longer dangle after compaction (red 45619, 45639, 45649).

### Scale and tick time

- **Indexed lookups**: a spawn's slot, a player's row and the players near a tile come from open-addressed tables and the tile index; the event ring is read from the listener's last heard row; planting and house views step only sites within reach; codec checks run in one pass; GM names grow by doubling (red 45639, 45652, 45655; fester 45752, 45762, 45768, 45776, 45780).
- **Round time (UOAIX-267)**: town route tables evict the route used longest ago and hold 256 routes with bounded steps; flora syncs once per world tick; a link close or relay defers its save to the next timer; reachability probes are budgeted and clipped ones fail-cached; a guard that has not closed on a moving foe in 10 steps drops the chase; WHEEL TOTAL, WORLD PULSE and ROUND SLOW lines name where the time goes (red 45734, 45788, 45810, 45817, 45839, 45906, 45934, 46767, 46825, 46982, 47754).
- **Spawns and NPC combat (UOAIX-268)**: NPCs swing in the wheel's combat round at tick pace and fight with no player online; dozing spawns wake only near players, re-arm on a strike or death, and a ghost keeps none awake (fester 45792, 46096, 46112; red 47635).
- **Memory**: a checkpoint widen at least doubles its room; a client view holds rows only for the objects it shows (640 to 681 KB per 256-slot growth, from 1.63 MB); walk-map growth is budgeted at 256 MiB across all maps; a TCP segment past a link's receive buffer is dropped instead of failing the link; a pending transmit queue appends instead of refusing, and a game response past its link's 64 KiB transmit buffer goes on into a boot-made spill that refills the buffer as it drains (reek 45828, 45851, 46089; fester 46781, 46784; val 46843; red 47203).

### World and play

- **Coroners (UOAIX-275)**: an incident table, coroners who are offered and accept a burial, a grave and a cemetery, a house deed for the dead and owed deeds funded by the crown; incidents walk the whole map and unaccepted ones close at a game day (red 47037, 47060, 47074, 47148, 47445, 47616, 47676, 47730).
- **The Lord's secretary (UOAIX-274)**: the keep's front door takes appointments through the secretary (red 46987, 47002).
- **Player fixes**: purchase change is paid back into the pack, Coin Available shows gold, silver and copper, every food eats, the mana bar recovers, the craft menu keeps its page, Alt+P no longer dismounts, a bulletin post is listed once, guards step around a blocking player, bookcase books answer page requests, felled stumps are drawn without the axe, and a drawn map shows its chart only within reach (red 46985, 46992, 46998, 47000, 47009, 47011, 47014, 47021; root 47030; val 45881).
- **Placement and routes**: placement skips a floor an impassable piece blocks and treats a dock plank over water as clear; Jhelom's herb farmer and city workers take teleporters; a gatherer's failed rock-face search moves on; Britain's reagent gatherers live in the town's houses (red 45726; fester 45797, 46467, 46469, 46595, 46733, 46739).
- **Crafts (UOAIX-53)**: the 1998 tinker's parts chain (catalog items 211 to 219) with the tinker and provisioner buying parts back, and inscription of all 64 spells (fester 46422, 46542, 46563).
- **Card games**: a card game ends with a final gump to both seats (val 46676).
- **Merchant fleet (UOAIX-237)**: fleet tallies and buy and checkout census on ECONOMY ROWS, and ships borrow on a leg with no open crown loan (reek 46775, 46796, 46836, 46873).

### Shard tools

- **Soak bots**: a soak fails on a disconnect no GM made, drops a bot whose socket failed, spaces first logins, counts late relays and GM disconnects apart, a widen family, a decor-build family and a coroner family (val 46702, 46708, 46719, 46747, 46761; reek 45971; root 45622; fester 46356; red 47106, 47174).
- **Setup and play**: a Windows setup script with a sparse ServUO and UOX3 fetch, a walkthrough page, `play-uoaix -Port/-NoClient`, `packet-proxy` on loopback by default (`-Any` opts in) masking passwords in every client packet, and `bvt.ps1` compiling the BVT unit with `-Decks 150 -MemNoCap` (reek 46634, 46654, 46666, 46730; val 46504, 46557; fester 46367).

## UOAIX Assist

- **Security audit**: the control port refuses HTTP, passwords are masked, streams, items and ground lists are bounded, CR/LF is refused, and the snapshot path is confined (val 46485, 46506, 46508, 46516).
- **Cave mining**: one or two shovels with automatic targets by the 1998 mining rules (4-tile reach, 8x8 chunks), cave-wide respawn learned from the server's regrow cycle, a sentinel, a mining toolbox and shovel trips, ingot stashes on the floor, a pile, a bag or a house vendor, and caves in walking reach walked to (red 45600, 45604, 45606; blu 45625, 47067, 47280, 47439, 47940, 47996, 48583, 48612).
- **Lumberjacking and harvest**: a lumberjacking circuit with an oval tree order, chunk grid, sentinel, Resume and New chop, and log collection nearest-first by bunny hop; Gather lifts, tools and orders; a feather circuit that hunts, carves and stores and never attacks a bird labelled (tame) (blu 47992, 48006, 48069, 48072, 48092, 48182, 48259, 48263, 48278).
- **Tailoring circuits**: tailors asked in walking order and best offer first against a market floor, offers kept across restarts, homespun day (weaving and sewing from a house floor), thread vendors bought out with restocks learned by bisecting their strike window, and bank stops that bank gold over a limit (blu 46244, 46266, 46278, 46943, 47736, 47759, 47809, 47924, 48550, 48663).
- **More circuits and loops**: Advertise (blank books written by packets), Spellbooks, Mark runes, Buy ingots, Watch me sell, a Magery loop, automatic cure, reagent loadouts that move only the difference, and last targets (F9) for every loop (blu 45668, 45671, 45678, 45707, 46296, 46435, 46931; reek 46565, 46575).
- **Houses**: a house registry with a key routine per house, a Houses tab, a door kept locked with the key on the vendor, door sweeps by Detecting Hidden, and a house placement pretest by the 1998 server's multi rule in the game client and on the map (blu 47097, 47128, 47703, 47709; reek 47526, 47537, 47540, 47542).
- **Walking**: steps by ServUO's Movement.Check, teleporters from public data, Find with the shortest walk drawn, cave walks that match the 1998 server, doors opened ahead, and a planner that searches by A* and stops on request (a measured route from 120.8 s to about 34 s) (blu 45685, 45687, 45693, 47065, 48074, 48206).
- **Map and vendors**: building signs from the 1998 dynamic data, a harvest chunks overlay, a decor catalogue, houses on the map, and a Vendors tab with other players' vendors and magic items identified by wand (blu 46290, 47069, 48018, 48154, 48168, 48292).
- **Timings**: every pause is a timing, changes are logged, and busy waits auto-tune by refusal rate in 50 ms steps within 50% of their base (blu 46238, 46250, 47881, 47891, 47986).
- **Structure and tests**: a virtual clock, byte pipe, test runner and coverage instrument; the send layer at 100% lines and branches and AssistState at 100%; `test-assist` at 836 of 836; the window split into partial files under C# guidelines, `#nullable enable` and the `Uoaix.Assist` namespace; `watch-relay.ps1`, `run-report.ps1` and `thread-report.ps1` (blu 46361, 47901, 47906, 47911, 47914, 48324, 48520, 48524, 48534, 48620).

## Apps

- **Magic**: the Ice Age era ladder, from Whiteout and Stampede to Gaze of Pain and Melee; Auras whose effects last only while attached and control Auras that revert, Detonate, Aspect of the Wolf, the AI casting Dark Ritual and spell deck value by effect (fester 47185, 47208, 47223, 47239, 47285, 47299, 47313, 47362, 47392, 48700).
- **Classic games**: Checkers scores toward its own king row and counts the forty-move rule from the last capture or man move, chess perft graded on four published positions, Risk's two-player neutral army to the 1959 rules, Yahtzee new-game dice and Spider move decode (val 46473, 47161, 47168, 47195, 48707, 48708).
- **Overflow refused (VFP row 23)**: GameServer and the codexmagic servers refuse overlong numeric query values, and PageCompiler, WaLoad, WorkflowEngine and the magic OracleParse refuse or read as none a number that does not fit (val 47508, 47518, 47550, 47555, 47565, 47571).
- **Website**: a landing card for UOAIX Assist with a screenshot, and the compile page served from our own kernel with a flat heap over 30 fetches (red 45718; fester 46068, 46071).

## Tools and build

- **Shell DSL readability**: comment lines in `cdx-to-pe`, `test`, `vm-config`, `build`, `build-img`, `quire-map`, `bvt`, `plug-build-lib`, `check-vm-differential`, `check-errors`, `boot-arm64`, `test-disk-compile`, `compile`, `test-compile-batch` and `test-run` leave ScRaw for ScComment, and the UEFI stub's 410 literal byte writes become typed calls (reek 45977, 45983, 45990, 46000, 46004, 46008, 46014, 46018, 46026, 46031, 46040, 46044, 46051, 46075, 46079, 46083).
- **Gates and utilities**: the cite-gate runs `check-builtin-alloc` on a compiler change; VmCompile reads UTF-8 serial input with a strict SIZE frame and refuses CODEGEN and exception failures with diagnostics; five Perforce and fleet utilities move to `tools/agents`; SASS oracles clone maxas per workspace and GSP oracles refuse a checkout with no HEAD; `concat-codex-self` runs under pwsh on Linux (GitHub issue 164) (val 47402, 47476, 47485, 48728; fester 45996; reek 47662).

## Release proofs

- **Full gate** (`build/build.ps1`, every phase) at main 48729: the compiler is a hard fixed point in one pass and byte-identical to the shipped seed `967435BD`. Red, and named rather than blocking: the browser check of ModBuilder, whose page renders its markup as text since HTML labels are set as text (val 45875); ModBuilder and six more pages holding HTML-string labels go to val.
- **IR fidelity**: 9 cases, 0 unexpected.
- **Battery** (`-Tier all`): 2304 tests, 2233 pass and 64 skip; the 7 red are GPU-model subjects past the 60 s wall budget with 16 guests on one GPU, and each passes alone on the release compiler. `sdxl-euler` takes 156 to 166 s alone and is marked `.slow` (red 48747).
- **App sweep**: 562 units against the baseline, 559 clean and 3 known.
- **Poison build** (0xCD fill, `-Tier all`): no uninitialized-field read; 2230 pass and 64 skip, and the 10 red are the same GPU-model wall-budget subjects, all 10 passing alone against the poison compiler.
- **Images**: `seed/Codex.img` rebuilt from the release seed (SHA-256 `993C4875`); `seed/Codex.map` taken from the gate, all 5,843 rows matching the seed's embedded MAP1; `build/boot/diag.img` (SHA-256 `72B6C771A5019C897BA3A02CB2B8A0B0608F1D75B8A088F947EC13216CDE2ADB`) rehearsed on all 60 arms on Codex VM and QEMU/OVMF, and the shipping check passes.
- **Box** (`docs/Agents/box-release-2026-10-26-u71.csv`): free-memory floor 16.26 GiB at 06:48, as the poison battery started; peak 19 guests at 06:14, in the battery, at 546 MB working set per guest.
- **DDC** (Roslyn arm): both arms 3,829,461 bytes, the seed's length, and 0 bytes differ outside the signature region.
## Outside contributions

Six pull requests from Steve Howell on GitHub are ingested: PR 159, Zig plug
`fail` raises only where called (red 46464); PR 160, GameServer Yahtzee
new-game dice (val 48707); PR 161, GameServer Spider move decode (val 48708);
PR 166, Zig plug forms for Zig 0.17 (val 48709); PR 168, Zig plug block device,
process scope and FileSystem host calls (val 48710); PR 169, HTML plug
`event-source-then` (val 48711).
