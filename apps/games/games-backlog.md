# Games -- open capabilities

App-domain backlog. The shape and priority order for the platform live in
`docs/PM/CurrentPlan.md`; the platform-wide register was deleted 2026-07-23
and is not coming back. Anything that is this application's own behaviour
lives here.

The rules are the same ones: an entry says what is still missing and
nothing else, a closed entry is DELETED rather than annotated, and a
gap that is still real is never quietly dropped.

## The arcade

`apps/landing/web/games/index.html` runs the games in the browser, linked
from the landing page's `td4` card. Damian's standing instruction
is that every game works before the CobblestoneWeb deploy. Magic is skipped
by his direction (2026-09-01).

**All 35 rows of `apps/games/build-wasm.ps1` build and pass both graders**,
their own `<prefix>-verify.mjs` and `ar-verify.mjs` (790 arms), measured
2026-09-30 on seed B3256BF8B4CC8327 with every module rebuilt.

**Rebuild the wasm plug before anything else.** Every
`apps/landing/web/games/*.wasm` and the plug at
`codex/plugs/wasm/build-output/wasm-plug.cdx` are build output, compiled BY
the seed, so a merge-down that moves the seed makes every module on disk
stale even when no game source changed (L-SAMEVER). The order is
`codex/plugs/wasm/build.ps1`, then `apps/games/build-wasm.ps1 -Game <id>` per
game, which grades what it just built; about twenty minutes for all 35. A
game whose module fails to build is a parity finding for the wasm plug lane.

**OPEN: `apps/landing/build.ps1`'s staleness guard cannot fail after a
sync.** It refuses `fishtank.wasm` only when the module's `LastWriteTime` is
older than `FishTankWasm.codex`'s, and `p4 sync -f` rewrites both, so a
tracked module weeks behind its source passes (it shipped 33,678 bytes against
head's 36,563 until main 30043). The globe WGSL (`build.ps1:380`) and
`starmap.wasm` (`:430`) carry the same guard; `spark.wasm` (`:409`) and
`safari-page.wasm` (`:188`) are copied with none. A guard that can fail compares content: rebuild to
scratch and compare bytes, or run the module's grader
(`apps/fishtank/ft-verify.mjs`) by hand before a publish.

## Rules campaign: open

## CodexMagic integration

**The named skill ladder needs validation on shuffled play.** The MAIN33929
measurement used gemstone-first unshuffled starter order, not live New Game
setup. Its 400 runs/profile gave Master and Expert 41 wins each, but those
numbers do not describe shuffled play. Root's 2026-10-01 assignment requires
every adjacent level to beat the lower level by more than two paired standard
errors, without relabeling. AIGameplay's Skill Levels section owns the locked
holdout, approved shuffle correction and reporting contract. Tuning remains open.

**Ranked skill-level enforcement is open.** `/game/new` has no live
ranked/casual selector. Requiring Master in ranked play needs that mode contract
and wiring; the current skill-level ranking unit does not invent one
(root ruling 2026-10-01).

**Crafting cannot share a unit with the card-game Engine.** A citer of
`codexmagic/Crafting.codex` that also adds the card-game Engine collides on
`advance-turn` with RPGEngine; no live application unit does this.

## Where game defects hide

**A conservation law is the wrong instrument**, in the campaign's own
examples: Backgammon conserved all thirty checkers while white could
never bear off, Bridge summed to thirteen tricks while crediting them to
the wrong side, and Mancala kept all forty-eight seeds while the search ate
the board. Two shapes recur and both are cheap to test.
**A refusal that changes state**: Connect Four, Go and Mancala all mutated
through a path that then reported the move rejected, so the test is to call
the refusing operation and compare the board. **A rule expressed once that
is right for the common case and wrong for the second**: one ace softened
but not two, one bound that contains its own home, one cell called safe
because its neighbour count is zero. What catches these is an independent
oracle or a decidable property, never a total.

## The rules the graders cite

Each row is a rule a `*-verify.mjs` or a test names by id.

| # | Rule | Where it holds |
|---|---|---|
| GAME-56 | **Pinochle's auction has no "pass with help"** | pagat's pinmain.html names the call only as a pass that "can be used to convey extra information to your partner" and gives the information no meaning, so the table offers a bid, a jump and a plain pass, and no seat signals (root, 2026-09-29). |
| GAME-55 | **Risk is played in primitive actions, each answered by the engine** | The whole game is one Integer list (`Risk.codex`, "The State Layout"); placing, trading, attacking, moving in, fortifying and ending are each allowed or refused by `rk-legal`. The engine's players are a policy, `rk-ai`, choosing one such action, and `risk-step` is those choices applied one at a time; `rk-verify.mjs` grades the actions against an oracle written from the rules text, in lockstep, with the legal set at each position. A Risk arm counts only once a sabotage has turned it red (L-CONSTRUCT). |
| GAME-54 | **Monopoly is played in primitive actions, each answered by the engine** | The whole game is one Integer list (`Monopoly.codex`, "The State Layout"); every action a player may take, throw to lift a mortgage, is allowed or refused by `mono-legal`, and whose choice it is by `mono-actor`. The engine's players are a policy, `mono-ai`, choosing one such action, and `mono-step` is those choices applied one at a time; `mo-verify.mjs` grades the actions against an oracle written from the rules text, in lockstep, with the whole legal set at each position. |
| GAME-42 | **An oracle is independent only where it differs** | A grader's rule model must not reuse the engine's reading of a rule (`sp-verify` once shared the engine's ace-high `c % 13`), and an arm must not reach its subject through the function under test. |
| GAME-40 | **A page is graded by running it, and an opponent by how often it moves** | `apps/games/page-verify.mjs` runs a page's module script against a stub browser and requires it to finish and build its gallery; `node --check` only parses. A human-play arm requires the opponent to move at least a quarter as often as the human, because "more than none" passed a backgammon side that moved four times in 81. |
| GAME-30 | **A Risk game that hits the turn cap has no winner** | `risk-make-result` reports the winner slot, which is -1 when the 400-turn cap ends the game with several players alive; naming the first survivor there would be a bail that answers (L-BAILVALUE). |
| GAME-29 | **Risk deals from its seed** | `risk-init` shuffles the 42 territories with `mix-bits` of the seed and deals them round the table, one army each, so the opening varies with the seed and the counts differ by at most one; the card deck is shuffled the same way. |
| GAME-27 | **A wild card is substituted, not counted** | `pv-evaluate-with-wilds` ranks the best hand a substitution makes from fifteen real candidates, each scored by `evaluate-hand`, and the seven-card variants choose their five WITH the wilds; the grader's wild model shares no code with the engine. |
| GAME-25 | **A Pinochle deck holds two of every card** | Every meld counts the double form (both kings and queens of a suit are a double marriage), not a presence test. |
| GAME-21 | **A dead Hex War unit keeps its strength; read the flag** | Combat sets `eliminated` and leaves `strength`, and every engine consumer tests the flag first; a page or report must read `dead` before drawing a strength. Not a defect. |
| GAME-20 | **A refused Go move leaves the board unchanged** | `go-place-stone` counts liberties on a copy before writing, so a suicide is refused with no stone left behind. |
| GAME-17 | **Bridge removes the card that was played** | A trick returns the four cards played and the hands lose exactly those; counts alone (thirteen tricks, thirteen cards) cannot see a wrong card leaving. |
| GAME-13 | **A move search leaves the board it searches unchanged** | A search that probes by APPLYING a move must copy first (`c4-copy-as`, and `chess-apply` copies by construction), because `list-set-at` writes in place. Every searching game's grader carries the arm: read the board, ask for a move, compare. Connect Four also blocks a loss in one and takes a win in one, graded as decidable tactics. |
| GAME-11 | **A game crosses the wasm boundary as a HANDLE, and a handle is dead once played through** | Only TicTacToe's state fits the one signed i32 the export wrapper passes, so every other game hands the page the address of its state record. A derived board shares its predecessor's lists (`list-set-at` writes in place), so the page replaces its handle after every move and never reads an old one. |
| GAME-9 | **The Magic simulator's two fixes, as ruled** | `apply-screw-fix` fires at zero gems in hand and `apply-flood-fix` at three or more (`screw-fix-fires`, `flood-fix-fires`); the first card in hand leaves on a discard or a pitch (`drop-first-card`); each fires at most once per turn per player. `tools/sim-test.ps1` counts both. |
| GAME-3 | **Spider has a solver and a player** | `solve-spider` is a depth-first search in the greedy scorer's move order (budget 100000 nodes, depth 1000); it undoes moves after seeing what they turn up, so it only answers whether a deal can be won, and `classic-games-oracle` calls it. `run-spider-game` plays forward on `sp-suggest-move`'s move, which reads face-up cards only (`sp-verify.mjs` twins positions with swapped face-down cards), dealing when no unseen position is left, for at most 1500 turns. |
| GAME-8 | **Monopoly seats trade for colour groups, and offer seat 0 its part** | A seat holding all but one unmortgaged deed of a colour asks the holder, before throwing: a swap completing both groups, else cash at twice the printed price with 150 kept back (`mono-trade-want`, `mono-trade-give`, `mono-trade`). When the holder is seat 0, the page's person, the seat OFFERS instead (phase 2) and the turn waits for accept or decline; a deed is asked for once a turn. `mo-verify.mjs` checks every trade against Rule 12 and plays the offers in lockstep. |
