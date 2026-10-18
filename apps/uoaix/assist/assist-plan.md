# UOAIX Assist

Our replacement for UOAssist 1.1.6 (Tugsoft, 1998): a relay between the 1.25.32 client and the
server that reads, shows and injects packets. Owner: blu. Users: Damian on uo1998.com and on the
UOAIX shard.

## Damian's rulings

- 2026-10-14: "yes its ok to use on 1998 as long as we mimic the behavior of uoassist and the
  client and don't blow their servers up."
- 2026-10-14: "well, it will do things uoassist doesn't, that will be the point, but it wont do
  things to exploit the server"
- 2026-10-14, on the first feature: "i like the interface where i am allowed to pick the
  variables during iteration zero, then the macro takes over." His mining round is two shovels,
  two targets, "the delay between the two uses is critical 1250 each", then a fixed number of
  rounds. He picks the targets himself; nothing refuses a target by distance ("let me manage it").

## What "mimic the client" binds

uo1998.com is a third-party server, so every rule here protects their server and Damian's account.

1. **L-FUTILITY: NEVER REPEAT ANYTHING THAT DID NOT GET A PROPER ANSWER THE FIRST TIME. NO LINE
   IS SAID TWICE, NO NPC IS ASKED TWICE, NO CAST, LIFT, USE OR DROP IS SENT AGAIN AFTER SILENCE OR
   A REFUSAL, AND NO LOOP MOVES TO THE NEXT TARGET TO ASK THE SAME THING. NO ANSWER IS AN ANSWER:
   THE CODE THAT ASKED STOPS AND SAYS WHY.** The only repeat is one the server's own answer asks
   for: "You must wait", a fizzle, a failed skill check, a vendor's "Let us go back to my shop"
   (asked once more, at its home). `GameLink.Say` refuses the same line, whatever sent it, when it
   got no proper answer within the last 120 s and the player has not moved 3 tiles; an answered line
   (a sale's "Brian sell" per 5 shirts) goes again. A vendor, banker or station that gives no proper
   answer stops the circuit and says why. (Damian, 2026-10-17, the third time: "if it didn't get a proper response the first
   time, DO NOT EVER REPEAT IT OVER AND OVER"; "it should NEVER EVER EVER say the same thing
   twice"; "ENSURE EVERYONE OBEYS THIS RULE".)
2. The relay sends only packets the 1.25.32 client itself can send, with the same fields and
   lengths. No packet id, field value or length the client cannot produce.
3. Pace is human pace. At most one injected action (use, target, lift, equip, attack) in flight.
   The next injected use goes out at the LATER of: the server's answer to the last one, and the
   step's configured delay after it (1250 ms use to use for the mining round). A slow server
   stretches the round; nothing shortens it.
4. A target response is sent only while the server has a target cursor open (0x6C from the
   server), and it answers that cursor's id. A cursor opened by an injected use belongs to the
   macro: the relay answers it and does not forward that 0x6C to the client, so the player's
   client never answers the same cursor a second time.
5. On a desync (a packet the relay cannot frame, a Huffman DESYNC, an unexpected reply) the relay
   stops starting new injections and says so in the window. It never retries in a loop. Once any
   byte has been injected toward the server, the client's keystream and the server's no longer
   match, so from then on the relay must keep decrypting and re-encrypting every client byte for
   the life of the connection; "stop injecting" never means "pass the raw bytes through".
6. Nothing exploits the server. The assist is meant to do more than UOAssist on the CLIENT side
   (what it shows, records and automates), but what reaches the server is what a player at this
   client could send by hand: no speed changes, no packet the server would not get from a person, and no
   reading of state the server did not send to this client. Movement is injected by the tailoring circuit
   (stage 6) and the control port's `walk` and `flee`, one step at a time (Damian, 2026-10-17: "no i need you to
   automate all this"), by `pace auto` unless set: a mount the stamina estimate holds rested runs, a tired one walks
   after a short rest and runs again once rested, and each refusal for fatigue rests longer (Damian, 2026-10-17: "when
   the horse is in a known rested state, run it if it helps ... if walking is still a problem, rest a bit longer");
   `pace walk|run` fixes it; a flee always runs; any step of the player's own ends the walk.
7. The login password never reaches a log. The relay and `packet-proxy.ps1` mask it (0x80 bytes
   31-60, 0x91 bytes 35-64).
8. Logs are bounded: one file per session, rotated at 64 MB, the last 4 kept (R-COST).

## Where it runs

On the Windows host, beside the client: PowerShell with inline C#, the way
`apps/uoaix/packet-proxy.ps1` is built (R-SHELL), with a WinForms window for stage 5. It talks
to the client and the server over TCP only; it does not inject into the client process.

## The code

- `assist.ps1` starts the relay: `pwsh apps/uoaix/assist/assist.ps1 -LoginServer
  login.uo1998.com,5000` (or `127.0.0.1,<shard port>`), `-ListenPort` 2630 by default (2600 is the live shard), logs in
  `build-output/uoaix/assist/`. A shard copy on a forwarded port names its guest port (2593, the
  live shard's proxy) in 0x8C: run it with `-GameServer 127.0.0.1,<its port>` as well.
- `Assist.cs` is the relay and its loopback control port (relay port + 1; one command a line, one
  answer line each), the headless way to drive what the window drives; the commands are the cases of
  `AssistRelay.Command`, each with its arguments in the comment above it.
  `GameLink.cs` is one game connection: both directions framed, client bytes re-encrypted, server
  packets re-encoded one a unit, and rules 1 to 5 enforced where packets are put in (rule 1 at `Say`).
  `AssistState.cs` is what the server has told the client. `AssistFeatures.cs` is stage 4: the loop
  menu with each delay's source, round-zero recording, rule-2 pacing, stop and wait lines, arm and
  disarm, drinking, autopickup, counts, advice; control commands `loops`, `loop <name> [rounds N]
  [until-skill V] [until-line text]`, `stop`, `macro`, `arm`, `pickup on|off`, `reagents [add|remove
  hex]`, `counts`, `advice`, `notes`, `display [name on|off]`. What a server corrects (a delay, an
  advice threshold, the pickup reach) is kept per login server in
  `%LOCALAPPDATA%\uoaix-assist\settings.txt` (`-Settings` names another file). A wait line doubles
  the loop's delay and the doubled value is kept, so the first session on a new server can take
  several refusals (Arms Lore on the shard: 1000 to 16000 ms against its 15000 ms lore wait).
- `AssistWindow.cs` is stage 5: the window finds the client by the process that owns the client's
  TCP connection to the relay, is owned by the client's window, docks to its right edge at its
  height (WinEvent location hook), hides with it and undocks when it closes; panels Status, Loops,
  Arm and disarm, Drinking, Reagent pickup, Resources, Skills, Advice and events, Display; F2 loop,
  F3 arm/disarm, F4 pickup by default, each rebound on its panel, registered only while the
  client is the foreground window. `-NoWindow` runs headless; the control command `window`
  reports the dock and `window snapshot <png>` draws the window off screen. `UoWire.cs` is the one host-side copy of the 1.25.32 wire (keystream,
  Huffman code, framing), shared with `packet-proxy.ps1`; the Codex copy in `LoginWire.codex`
  serves the shard. Both scripts read the Huffman codebook from `GameHuffman.codex` and the
  packet lengths from `go-lengths` in `GameOpcode.codex` (CLIENT.EXE's own table: 0 marks a
  variable packet, 3-byte header with the length in bytes 1-2; the table ends at 0xB3, and an
  opcode past it is a desync).
- The login and the game connection both XOR with the keystream seeded by the connection's first
  4 bytes; the game connection's seed is the key from 0x8C. Login replies are plain; game replies
  are one Huffman unit per packet, each ending on a byte boundary.
- `D:\Games\UO1998com-Assist` is the uo1998 assist client, its `LOGIN.CFG` at `127.0.0.1,2650`; its
  `launch-assist.ps1` (desktop "UO1998 Client (Assist)") starts the relay on 2650 against `login.uo1998.com,5000`, so it
  runs beside the shard's relay on 2630. Its CLIENT.EXE is a third identity (class "UoClientAp3" at offset 715308,
  "UO Playe3" at 689724, the FindWindow jump at 0x4A6F2 as CLIENT2.EXE): two clients with the same second-instance
  bytes refuse each other with "Another instance of UO is already running." A relay on a port other than 2630 keeps `settings-<port>.txt`, and every log is
  named `assist-<port>-<time>.log`.

The client's login server is set in `LOGIN.CFG` beside `CLIENT.EXE`. Never edit
`D:\Games\UO1998com-Client1`: it is the pristine source every UOAIX client bake reads. Never
edit `D:\Games\UO1998com-Client2` either: it is Damian's second uo1998 client. Copy
`uo1998-client2` to `D:\Games\UO1998com-Assist` and set that copy's `LOGIN.CFG` to
`LoginServer=127.0.0.1,<relay port>`; the relay takes the real login server as a parameter
(`login.uo1998.com,5000` for uo1998, the shard's port for UOAIX). The copy's CLIENT.EXE is the
patched second-instance build (the same bytes as `D:\Games\UOAIX-Client2\CLIENT2.EXE`), so it
runs beside a first client.

## Stages

Each stage is proved on the UOAIX shard first, on a world copy root boots for it, never against
the live shard while Damian plays. A run that drives a client asks root for a GO, which root
grants while Damian is idle. A client run is one AutoHotkey v2 script that waits on small
partial-screen checks and re-checks that the client window is active before every click. A
stage reaches uo1998 only when Damian runs it.

1. **Relay with redirect.** The client logs in through the relay. The relay rewrites the
   server's 0x8C (game server IP, port, 4-byte key) to 127.0.0.1 and its own port, remembers the
   real address by key, and connects the game connection (identified by its first 4 bytes, which
   are that key) upstream to that address. Done when a scripted login and 10 minutes of walking
   and item use on the shard pass through with the client in sync and both directions decoded;
   then Damian logs in to uo1998 through it once and plays normally. That run also confirms the
   uo1998 server speaks the same wire (decoded 0x8C, no Huffman DESYNC).
2. **Framing and state.** Split both directions into whole packets with `go-lengths`. Track the
   player serial, backpack and container contents (0x25, 0x3C), worn items (0x2E), skills
   (0x3A), status (0x11), journal text (0x1C) and the open target cursor (0x6C). Done when a
   session's state matches what the client shows.
3. **Injection.** To the server: decrypt every client byte and re-encrypt it, with injected
   packets placed only between whole client packets (rule 4). To the client: Huffman-encode
   injected packets with the same codebook, only between whole server packets. Done when an
   injected double-click (0x06) and its target (0x6C) mine one ore on the UOAIX shard and the
   client stays in sync.
4. **Features, in Damian's order:**
   - **Mining macro.** F2 records round zero: the serials of the two items double-clicked and the
     tile each target cursor was answered with. Then it replays the rounds: double-click by
     serial, wait for the server's target cursor, answer with the recorded tile, pace by rule 2,
     a round count set in the window. Stops on F2 or any desync. The journal lines for an empty
     spot or a broken tool on uo1998 are read from the relay log of Damian's first uo1998 mining
     run and added as stop lines then; the shard's own lines are in the shard source.
   - **Skills and stats monitor, with advice.** Every skill's value and its gains this session,
     from 0x3A, and strength, dexterity and intelligence from 0x11. Damian, 2026-10-14: "then we
     should put in a skills / stats monitor that triggers for helpful advise like 'you can now
     wear chain tunics' etc." When a value crosses a threshold, the panel says what it opens up
     ("you can now wear chain tunics", "you can now mine colored ore"). The thresholds live in
     one table in the source, each cited (RunUO's pre-AOS item strength requirements, the
     1998-era skill gates), and the server is the check: a refusal line such as "you are not
     strong enough to equip that" after an advice line means the threshold is wrong for that
     server, and the table entry is corrected rather than the advice repeated.
   - **Arm and disarm.** A hotkey that lifts and equips (0x07, 0x13) a remembered weapon set, one
     move at a time at the client's pace.
   - **Drinking without hotkeys.** Damian, 2026-10-14: "now drinking potions is a 3 step deal of
     drop your weapon, drink, rearm. we should detect that sitation when someone doubleclicks a
     heal or cure potion, and just do that instead of mandating a bunch of setup hotkeys. it
     actually makes hotkey management a pain." When the player double-clicks a potion (0x06 on
     a serial whose item type is a potion, from the stage 2 state) while no hand is free (the
     worn-item layers say so), the relay holds that double-click and does the three steps in
     order, each waiting for the server's answer to the last: lift the weapon and drop it into
     the backpack (0x07, 0x08), send the held double-click, then lift the weapon back and equip
     it (0x07, 0x13). With a hand free, the double-click passes straight through. Every step is
     one the player would make by hand; the panel shows the sequence as it runs, and a refusal at
     any step stops it with the weapon wherever the server left it, named on the panel.
   - **Reagent autopickup.** Damian, 2026-10-14: "another feature they added to later versions of
     uoassis that we need here is autopickup reagents on the ground. it is a pita to pick them,
     and they actually clutter the server saves, and so it would be helpful even to the server
     admin if that was working again." When a reagent on the ground (from the world items the
     server sends) is within pick-up reach, the assist lifts it (0x07) and drops it into the
     backpack (0x08), one item at a time, each waiting for the server's answer to the last, as a
     player would by hand. Reach is RunUO's lift range to start; a server refusal ("that is too
     far away") shrinks it and the item is left alone. A toggle on its panel, and a list of which
     item types count as reagents, editable there.
   - **Resource counts.** Live counts of what the pack holds (ore, ingots, logs, reagents,
     bandages, arrows), from the container state of stage 2.
   - **Display options.** The settings UOAssist hides in its Display tab, shown on the panel.
   - **What the client does not show:** chosen from what the server already sends (names, hits,
     item properties), Damian picks.
   - **Canned macros.** Damian, 2026-10-14: "the arm/disarm, resource counts, skills, display
     options, now can be un-hidden, and we can have the macros be, intelligable. there is some
     issues with their ui where continuous means looping and adding a default delay that doesn't
     work for any particular skill, just a default. it would be nice if we could just have canned
     macros for certain things that everyone does". So:
     - a macro reads as steps in words ("use the shovel, target the rock you pick, wait 1250 ms"),
       never UOAssist's "Use Skill/Cast Spell, Delay 12500" list;
     - repetition is stated, never a "Continuous" box: N times, until a stop condition (an empty
       spot, a full pack, a skill value reached, a journal line), or until F2;
     - every wait belongs to its step and is named for what it waits on (the server's answer, the
       skill's own delay, a fixed delay the player set), with no one default delay for all;
     - a MENU of named loops for what every player does ("Loop Hiding", "Loop Arms Lore", "Loop
       Mining" and the rest; Damian, 2026-10-14: "makes more sense to have 'Loop Hiding' and know
       the proper delay instead. and to have lots of them in a menu, instead. and use the
       techique of using the first iter as the configuration of the loop. so if i wanna do arms
       lore, i only pick the item, then the loop takes over."). Picking a loop starts its first
       iteration, which the player does by hand and which configures the rest: Loop Arms Lore
       records only the item targeted; Loop Hiding needs nothing; Loop Mining records two tools
       and two tiles. Then the loop runs at its skill's own delay;
     - each loop carries its skill's proper delay, from a cited source (RunUO's
       `Scripts/Skills/*.cs` handlers return the per-skill delay of the 1998 rules) in one table
       in the source. The server's "you must wait to perform another action" line (read from the
       journal) is the check: if it ever appears, the loop lengthens that skill's delay, shows
       the new value on its panel and keeps it, so a delay that is wrong for uo1998 corrects
       itself and is never too short twice.
5. **Window, docked to the client.** Damian, 2026-10-14: "i want them merged, linked, docked
   together if possible, then we can use all that real estate there for a better integrated
   display", and "so we can have the hot keys all shown on the actual 'thing' that does it, not
   buried in the key keys tab". His screen is 1920x1080 with the client at about 1300x1010 in
   the top-left corner; the strip to its right is empty. The assist window:
   - docks to the client window's right edge at the client's full height, follows it when it
     moves or resizes (a WinEvent location hook on the client's window), minimizes and restores
     with it, and is owned by it so it stays directly above the client in z-order, not above
     every other window;
   - shows every feature as its own always-visible panel (macros, skills, arm/disarm, resource
     counts, display options, the extra information), not tabs, and each panel shows its own
     hotkey on its own control. There is no separate
     key list tab: a key is rebound on the panel that uses it.
   Hotkeys are active only while the client has focus.

6. **The tailoring circuit** (design, wire facts, proven and open: `tailoring-circuit.md`) (Damian, 2026-10-17: "i want a simple safe loop of just tailoring in all safe spots in
   town"; "yeah lets build it out"; he owns the account risk: uo1998 publishes no macro policy and no support, "as long
   as they don't tell me to stop"). The loop starts at any tailor with 200 gold from the bank; buys shuttles of thread up
   to about 100, buying a vendor out because "each time if they have sold out of thread, the next respawn gets more. all
   the way to 999", restocks about every 15 minutes; weaves bolts and stacks them; sews shirts from all the cloth; sells
   to a tailor at 27 gp, moving on to another tailor when one will not, Magincia first ("next time, try magincia
   first"); banks the purse in the city it sold in ("frequent drop offs of gold is good idea"), every bank stack under
   60,000. It travels from any bank by the bank box's rune for the city, read in place (runes never leave the bank,
   "they are there because we don't want to lose them on accidental death or... murder"; "all the destinations we
   need are right there in any bank"): no rune library and no trip back to Britain. Travel is Recall ("it sometimes fizzles but it works. be
   persistent"), 30 of each recall reagent carried and refilled from the bank ("wherever you go, there is a bank").
   Recall, speech ("vendor buy", "vendor sell", "bank"), buying, selling and walking are what a player sends by hand,
   so the injectable set grows by steps (0x02), speech (0x03), the buy reply (0x3B) and the sell reply (0x9F). A vendor
   is 3 tiles from the loom or the next vendor even inside one shop, so the circuit walks (rule 6's exception). The
   relay numbers every step, the client's and the assist's, in one count, gives the client its own numbers back, and
   draws the player anew (0x20) after assist steps. Walking routes around other characters: a step through one is
   blocked without full stamina and costs a stamina penalty with it (Damian, 2026-10-17). A vendor is always addressed by name ("Morena buy"), never
   "vendor buy": "otherwise other npc can intercept". A vendor trades only inside its small shop range; engaged away from
   it, it teleports home or walks home slowly ("I am sorry, I do not have my wares here with me. Let us go back to my
   shop."), so only that answer earns one more ask, made once the vendor stands still at home (rule 1).
   Everything the circuit does is the packet itself, and every window or cursor it causes stays off the client: the
   shop windows, a bag opened to read it, a spell's cursor ("lets just inject the right messages instead of user
   emulation of clicks unless absolutely necessary"). A vendor hears its name from 3 tiles and not from 6 (Michelle and
   Tracy, Britain, 2026-10-17), so the circuit walks to within 3 tiles of the vendor, in sight. A vendor can stand behind a
   door ("tracy was behind a door, you have to learn to open those"): an unanswered vendor with a closed door near is
   asked again after the door is double-clicked open. A vendor's stock is bought out
   in one purchase ("maximizing the count in a single transaction is best"): Jannelle sold
   10 thread at 3 gp, then her last 9 at 4 gp in a second purchase (2026-10-17). A sale takes at most 5 items ("you can
   only sell 5 items at a time"), so the circuit sews up to a multiple of 5 before it sells ("if you have like 14 shirts
   to sell, its better to make 1 more for the even 5"), and carries a remainder the cloth cannot round up to the next sale.
   A banker hears "bank" through the bank's walls from a distance, so the circuit walks to the outside of the wall and
   says it there ("its usual to just walk up to the bank wall and say bank there, rather than go inside").
   Two sewing kits are carried, the spare bought from the tailor when one wears out ("i usually keep 2, in case for some
   reason that particular vendor doesn't have one").
   The circuit stops
   and tells Damian when staff speak to or near the character, it is attacked, dies or disconnects, or the fail-safe
   trips; it never answers anyone as him. Players are ignored ("players are annoying sometimes"); staff is a game master
   or counselor, not a grandmaster: a name begun "GM " ("the custom was to name the char GM <whatever>"), a title of game
   master, GM, counselor (or councilor), seer or admin, or the GM robe (0x204F in the 1.25 tile data) worn. Built in
   steps, each tried with him watching:
   1. speech, "<name> buy" for thread (the buy window answered by the assist, buying out up to a count and the gold
      on hand), "<name> sell" at a floor price, "bank" with gold deposits under 60,000 a stack and reagent refills;
   2. Recall to a rune, retried only on a fizzle, reagents checked first;
   3. travel from the bank box (`travel <city>`, `BankRecall`);
   4. the circuit itself: per vendor its restock time and thread stock from its buy lists, the weave, the sew and the
      sale, the next stop by restock and demand, the bank at a purse size.

## Retires

`D:\Projects\uoaix-macros\Mining.ahk` (the screen-position version of the mining round, F2) once
stage 4's mining macro works on uo1998.

## Rulings of 2026-10-15 (Damian, testing on the shard), built on main

- Window: "add expand/contract controls to each section", "for resources, lets make that formatted into 2 column groups
  wide, and with some small icon graphics from the game assets", "for skills show columns displayed and base, and
  change, since its not all gain here. and sorting with a click on the column header, right click desc", "all of the
  panels of the uoaix assist need to fit the screen with no full window scroll bars in any resolution" ("if it doesn't
  fit, just let it underflow the screen, but make it fit a properly sized screen"). Docking has no owner link: a
  cross-process owner shares the client's hidden cursor (the Windows cursor vanished over the assist).
- Client size: "i can't get the client exe to be the right size, the graphics renders tearing of fonts and stuff if
  its off by a pixel. can you make it so when assist runs it 'right sizes' and locates the window so it can be all seen
  on the window properly". At dock the client area is set to the largest whole multiple of 640x480 that leaves the
  assist its minimum width beside it (1280x960 on a 1920x1080 screen), at the work area's top left.
- Chat: "a chat tab, that reads incoming journal entries, and filters out everything but human player speech".
- Item pickup and resources: "for reagent pickup make it item pickup, and add a button to add, that reads in the item
  data and lets you pick from a list sorted by... likely pick upable ness"; "add the same for the resources, a picker and
  a set list".
- Session: "add session status, time since login, where you at overview, track and show the exact tile info, any zone
  you are in, guarded/not guarded, home, not home, show criminal state". Zone and guarded come from the server's guard
  lines and, on uo1998, a local regions-1998.txt (Origin's data, kept out of the depot); "home" is shown as inside a
  house's footprint, because nothing on the wire says whose house it is.
- Fail-safe (AssistGuard): "if errors appear to happen, stop doing the proxy behaviors. run clean unless everything
  seems nominal. we want this to detect subtle errors in configuration and not spam the server with bad requests",
  with a log tab, the reason shown, an override, and "turning off the auto disablement for specific error patterns".
- Loop pacing: "different skills have different timeouts, as do different servers. we need a timeout default of 12500
  seconds for lore, hiding, detect. for mining 1 its 2500ms for 2 its 1250 per tool use (after first and second
  target). for others, it depends. ... if a loop is running and you get that error, back off to repeat rate and find one
  that works. start with a 5s assumption and program in a bisect." Root's reading: 12500 ms for Hiding, Detect Hidden,
  Arms Lore and Animal Lore; every other skill loop starts at 5000 ms and probes shorter; a "you must wait" line bisects
  between the longest refused and the shortest working delay, kept per login server, and the fail-safe leaves a
  running loop's wait lines to the loop.
- Resources: "group resources into subgroups: harvest, create, reagents, and combat", "and make the icons a touch
  bigger" (28 px).
- Display and status: "the display option, can it add things to uo window titlebar? having display configs affect
  status in uoaixassist is unnecessary with the collapser. format them in the status better too, two columns name:
  values grouped by vitals, attrs, and other", "skill gains display option is useless with collapser". The Display tab
  picks what follows "  |  " in the client's title (WM_SETTEXT); the Status panel shows every value.
- Loops menu: "no need to prefix each option with Loop. that is implied".
- Loop border and first wait: "when a loop is running, put a green border around the whole window. draw that border 2
  pixel wide, and it should be painted background gray to start so it doesn't relayout when running. if it stops for
  failsafe, make it red. when the failsafe is bypassed, turn it back gray to start again. then also, when looping, be
  sure to wait the skill-appropriate default timeout before starting the actions." The first action waits the loop's
  delay in use for the server.
- Delays: "the delay for mining is 2500 ... in uoassist exactly. never fails at that", "for shovel or pickaxe"; the
  delay counts from the target. "it would be good to put the actual delays in there that can be shown and overridden,
  with a button to turn on or off auto-detect by bisect feature": the Loops panel's delay box and auto-detect checkbox
  (off: a wait line repeats at the same delay, 3 in a row stop the loop).
- Skill notices: "do we have the cool green skill update notifications in uoassist? also they should not be green if it
  is actually going down. uoassist shows drops green. i'd like to see them red.", "its generated chat in the client on
  skill bumps". A client-only journal line per skill change, green up, red down.
- uo1998 status: "the name and title doesn't come through against uo1998", "we need to invoke status to get that
  info", "we should do that automatically on login". uo1998 sends 0x11 and the 0x3A list only on the client's 0x34;
  at 0x55 the assist sends 0x34 type 4 then type 5, each after the last one's answer, and reads 0xA1-0xA3 and the
  paperdoll line (0x88).
- Map: "next is a third main tab which is map, and read the map file and put a player tracker there like uo automap
  which is defunct now": the client's MAP0/STATICS0 in its RADARCOL colours, centred on the player, the trail walked
  (last 2000 tiles), people seen in the last minute (red on the criminal side), zoom 1 to 8 by wheel or +/-.
- Atlas: "map should update to show shop keeps, bankers, other npcs. add filters by type and subtype. show regions on
  the map, and drill down for specific npc item lists when the vendor buy list comes up", "add a shopkeeps and merchants
  as a main tab". `AssistAtlas.cs`: mobiles typed by the title in their name; kept NPCs saved to
  `atlas-<server>.txt`; names learned by one single click (0x09) per new unnamed human within 14 tiles, at most one per
  1.5 s (the "Name people by a click" box); the buy list (0x74) stored on its vendor; Map filters (type boxes,
  Subtypes...), regions layer, click a mark for its details; the Shops tab lists vendors with an item search.
- Trades, houses, guildstones: "record all transactions, buy and sell with each npc vendor and also keep track of player
  vendors and houses, showing house decay level and any other details. if you see guildstones, record them and show them
  on the map." Every item bought (client 0x3B) or sold (0x9F), priced from the server's 0x74/0x9E offer, goes to
  `trades-<server>.csv` and the vendor's details; "the vendor" titles are player vendors; a multi (0x1A graphic 0x4000) is a
  house, its sign (0x0BCF-0x0BD2) clicked once for its label, and any system line on decay or condition heard beside it
  kept as its details (the 1998 server's decay text is in its scripts, so the wording is learned, not known); a
  gravestone-art item (0x0ED4) whose label names a guild is a guildstone; houses and guildstones are kept in
  `places-<server>.txt` and drawn on the map.
- Stamina: "add a stamina warning for both you and mount": the player's below 25% and 10%; the mount's stamina is
  server-side only, so the server's "Your horse is very fatigued." (at 10%) is repeated in red.
- Harvest crops: "wheat and cotton need to harvest, as weell cabbage which requires no digging, just pluck it". The
  plants are a fixed 1.25 list (carrots 0C76, cotton 0C4F-0C54, wheat 0C55-0C5B); a pluck's product is the new item on
  the plant's tile, else the plant itself; a head of cabbage (0C7B, 0C7C) is lifted, never double-clicked (that eats it).
- Mill, spin and weave: "we need a miller feature", "and a tailor for the cott". One action (F5) works the pack's
  material at the stations within 3 tiles, until the material runs out or F5 again: wheat sheaves into every flour
  mill in reach at once ("there are three at a time here"; 4 sheaves a hopper, a double-click grinds, the sack drops
  beside the mill and is picked up), cotton or wool on a spinning wheel (1 spool a
  bale, about 5 s on uo1998), thread or yarn on a loom (1 spool a use; the bolt builds across uses). A wheel and a loom
  in reach take turns: "make it spin and weave at the same time if both are there". Every spool in the pack is used:
  "if you start with some, be sure to use those too".
- Mount recovery: "i need to know when the mount is recovered". The assist estimates the mount's stamina from the
  server's regen and drain rules, anchored on the server's two horse fatigue lines, and says in green when a 75-stamina
  horse would be full (about 3 min 11 s at rest from 10%).
- Session summary: "add a session summary report to the session details, showing aggregates of things like kills by
  creature, total skills gained lost, attrs, reagents picked up, loops run, and their effectiveness at skill gain". A
  kill is the death (0xAF) of a mobile the player attacked within 2 minutes; a loop's gain is its skill (Mining for the
  mining loops) from start to end, per hour and per 100 rounds.
- People: "keep a report of all players seen, any activities they were performing, where they were, noteriety and
  criminal details". Every human-bodied mobile (the 1.25 wire does not tell players from human NPCs), written as
  Markdown to `%LOCALAPPDATA%\uoaix-assist\reports\people-<server>-<start>.md` every 30 s. uo1998 sends the raw signed
  notoriety (-127 to 127); negative is shown as the criminal side.
- Full pack: "full pack should dump the biggest load of ore out of your pack on to the ground at your feet, like the
  server will do, but instead of the server, we do with the ore you have, not the overflow." On uo1998's full-pack
  lines a loop lifts the pack's largest ore stack, drops it on the player's tile, and goes on; with no ore it stops.
- "the 8x8 runes don't show in side caves, where they are useful": a corner whose land is more than 12 from the player's
  height takes the player's height, and the runes are redrawn when the player's height moves more than 10.
- Load: "add a warning, for 50%, 75%, 95% to the client like with skill gains": client journal lines at those marks of
  the 1998 carry limit (strength * 4 + 30, OUO mobile.c), yellow, orange, red; autopickup pauses short of the limit.
- Z-order: "the uoaix assist window is not at the same z level as the client window": kept directly above the client.
- Border: "make the colored border 3 pixels, not 2. and add yellow to indicate \"configuring\" first step".
- Pages: "if we are in an under-flow condition with the screen space where a scroller should appear, we need a way to
  indicate that that doesn't scroll. like maybe the whole main panel needs a mode/tab for putting the info in one place
  and the actions in the other." Root's reading: an Info tab (Status, Session, Resources, Skills, Chat and events) and
  an Actions tab (Loops, Arm and disarm, Item pickup, Automatic and display); an amber bar at the bottom while a page
  runs past the window's edge.
- "make the subgroups collapsable in resources", "make the group names and the items start on the same row to perserve
  vertical screen space".
- Bank corners: "show the 8x8 corners for resource harvesting in the client somehow, like we did we runes for zones in
  uoaix server": client-only recall runes on each 8x8 bank's first tile; the server never sees them.
