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
their own `<prefix>-verify.mjs` and `ar-verify.mjs`, measured 2026-09-25 on
seed 9093EDA39488F68D with the wasm plug rebuilt first; every module was
rewritten by that run.

**Rebuild the wasm plug before anything else.** Every
`apps/landing/web/games/*.wasm` and the plug at
`codex/plugs/wasm/build-output/wasm-plug.cdx` are build output, compiled BY
the seed, so a merge-down that moves the seed makes every module on disk
stale even when no game source changed (L-SAMEVER). The order is
`codex/plugs/wasm/build.ps1`, then `apps/games/build-wasm.ps1 -Game <id>` per
game, which grades what it just built; about twenty minutes for all 35. A
game whose module fails to build is a parity finding for the wasm plug lane.

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
| GAME-55 | **Risk's turn is stepped** | `phase`, `to-place` and `attacks-left` live in the record: a turn is placement, one army per click, then attacks from a source to a target, and the self-playing turn is a policy over those steps. A Risk arm counts only once a sabotage has turned it red (L-CONSTRUCT). |
| GAME-54 | **Monopoly's turn stops at the buy** | Landing OFFERS a property, with `phase`, `pending` and `last-roll` in the record so a page can interrupt the turn there; the watch-only runner is a policy over the same turn. |
| GAME-42 | **An oracle is independent only where it differs** | A grader's rule model must not reuse the engine's reading of a rule (`sp-verify` once shared the engine's ace-high `c % 13`), and an arm must not reach its subject through the function under test. |
| GAME-40 | **A page is graded by running it, and an opponent by how often it moves** | `apps/games/page-verify.mjs` runs a page's module script against a stub browser and requires it to finish and build its gallery; `node --check` only parses. A human-play arm requires the opponent to move at least a quarter as often as the human, because "more than none" passed a backgammon side that moved four times in 81. |
| GAME-30 | **A Risk game that hits the turn cap has no winner** | `risk-make-result` reports `s.winner`, which is -1 when the 400-turn cap ends the game with several players alive; naming the first survivor there would be a bail that answers (L-BAILVALUE). |
| GAME-29 | **Risk deals from its seed** | `risk-assign-loop` keeps the even split `i mod np` and Fisher-Yates shuffles it with the threaded `rng-next`, so the opening board varies with the seed and no player is dealt nothing. |
| GAME-27 | **A wild card is substituted, not counted** | `pv-evaluate-with-wilds` ranks the best hand a substitution makes from fifteen real candidates, each scored by `evaluate-hand`, and the seven-card variants choose their five WITH the wilds; the grader's wild model shares no code with the engine. |
| GAME-25 | **A Pinochle deck holds two of every card** | Every meld counts the double form (both kings and queens of a suit are a double marriage), not a presence test. |
| GAME-21 | **A dead Hex War unit keeps its strength; read the flag** | Combat sets `eliminated` and leaves `strength`, and every engine consumer tests the flag first; a page or report must read `dead` before drawing a strength. Not a defect. |
| GAME-20 | **A refused Go move leaves the board unchanged** | `go-place-stone` counts liberties on a copy before writing, so a suicide is refused with no stone left behind. |
| GAME-17 | **Bridge removes the card that was played** | A trick returns the four cards played and the hands lose exactly those; counts alone (thirteen tricks, thirteen cards) cannot see a wrong card leaving. |
| GAME-13 | **A move search leaves the board it searches unchanged** | A search that probes by APPLYING a move must copy first (`c4-copy-as`, and `chess-apply` copies by construction), because `list-set-at` writes in place. Every searching game's grader carries the arm: read the board, ask for a move, compare. Connect Four also blocks a loss in one and takes a win in one, graded as decidable tactics. |
| GAME-11 | **A game crosses the wasm boundary as a HANDLE, and a handle is dead once played through** | Only TicTacToe's state fits the one signed i32 the export wrapper passes, so every other game hands the page the address of its state record. A derived board shares its predecessor's lists (`list-set-at` writes in place), so the page replaces its handle after every move and never reads an old one. |

| engine | search | used? |
|---|---|---|
| TicTacToe | **full-depth minimax**, `ttt-mm-score` and `ttt-mm-fold` in mutual recursion, bounded by the nine-ply board | yes, and its perfection is graded exhaustively |
| Mancala | **real alpha-beta**, `minimax` recursing through `mm-max` and `mm-min` at depth minus one | yes |
| Connect Four | a two-ply engine, `c4-iterative-ai` with `c4-min-response` and `c4-max-response`, in `Minimax.codex` | **NO. Defined and called by nothing** (L-UNCALLED). The game plays `c4-ai-move`, a win-in-one and block-in-one check ahead of a centre preference |
| Checkers | none. `ck-pick-best` scores each move once and never recurses; `checkers-ai-move` takes a `depth` argument that two callers thread through and its body never reads, which is what made the original claim look true from the signature | n/a |

So a chess AI has two working models in the tree to copy from, and one dead one worth reviving or deleting. The legality filter remains the genuinely new ground, and it is stage 3. |
| GAME-9 | **The Magic simulator's two fixes, as ruled** | `apply-screw-fix` fires at zero gems in hand and `apply-flood-fix` at three or more (`screw-fix-fires`, `flood-fix-fires`); the first card in hand leaves on a discard or a pitch (`drop-first-card`); each fires at most once per turn per player. `tools/sim-test.ps1` counts both. |

1. **Which card does the discard or the pitch take?** A gem, the cheapest card in hand, or the first. `ScrewFixDiscard` discards one and gains a ray; `FloodFixPitch2Draw1` pitches two and draws one; `FloodFixPitch1Draw1` pitches one and draws one.
2. **What triggers a fix?** `sim-game-loop` already computes `gems-in-hand == 0` for screw and `gems-in-hand >= 3` for flood on the active player, so the cheapest answer is those two tests. Say if the trigger is something else.
3. **May a fix fire more than once in a turn?** The loop calls both once per turn per player as written.

Answer those three and the two functions are a short write with no further choices in them. |
| GAME-3 | **Spider is solved by search** | `run-spider-game` is a depth-first search in the greedy scorer's move order, budget 100000 nodes and depth 1000; `sp-suggest-move`, the page's one-step hint, is the greedy scorer. |
| GAME-8 | **Monopoly seats trade for colour groups, and offer seat 0 its part** | A self-playing seat holding all but one deed of a colour asks the holder: a swap completing both groups, else cash at twice the printed price with 150 kept back (`mono-trade`). When the holder is seat 0, the page's person, the seat OFFERS instead (`mono-trade-offer`, phase 2, the wanted deed in `pending`) and the turn waits for accept or decline, then resumes with no second offer. `mo-verify.mjs` "TRADING" and "A TRADE OFFERED TO SEAT 0" grade both. |
