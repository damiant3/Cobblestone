// Grade the War wasm module.
//
// War has no strategy and no choices, so the only things to get wrong are
// the deal and the bookkeeping -- which is exactly why it needs a grader
// that looks at the CARDS rather than at the score. "Player 1 wins after 6
// rounds" is a plausible sentence whatever the deal was.
//
// The load-bearing arm is that the two players hold DIFFERENT cards. A deal
// that hands both players the same array makes every round a tie, so every
// round is a war, and the game ends in a handful of rounds with a winner
// and a round count that read perfectly normally.
//
// The second is conservation, and here it is the right instrument for once:
// War moves cards between two hands and creates none, so the two sizes must
// sum to 52 for as long as the rules say cards only change hands.
//
// Usage: node apps/games/wr-verify.mjs [path/to/war.wasm]

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import { gameInstance } from '../landing/web/games/arcade.js';

const here = dirname(fileURLToPath(import.meta.url));
const wasmPath = process.argv[2] ||
  join(here, '..', 'landing', 'web', 'games', 'war.wasm');


const inst = gameInstance(
  new WebAssembly.Module(readFileSync(wasmPath)));
const e = inst.exports;

let pass = 0, fail = 0;
const ok = (name, cond, detail) => {
  if (cond) { console.log(`  ok    ${name}${detail !== undefined ? ': ' + detail : ''}`); pass++; }
  else { console.log(`  FAIL  ${name}${detail !== undefined ? ': ' + detail : ''}`); fail++; }
};

const hand1 = h => [...Array(e.wr_p1n(h))].map((_, i) => e.wr_p1c(h, i));
const hand2 = h => [...Array(e.wr_p2n(h))].map((_, i) => e.wr_p2c(h, i));
const rank = c => c % 13;

console.log(`wr-verify ${wasmPath}`);

// -- THE DEAL: two players, two different halves --------------------------
{
  const bad = [];
  const deals = new Set();
  for (let seed = 1; seed <= 40; seed++) {
    const h = e.wr_new(seed);
    const a = hand1(h), b = hand2(h);
    if (e.wr_p1n(h) !== 26 || e.wr_p2n(h) !== 26) {
      bad.push(`seed ${seed}: hands of ${e.wr_p1n(h)} and ${e.wr_p2n(h)}`);
      continue;
    }
    // Record the deal BEFORE any check can skip the rest of the loop: a
    // seed-dependence arm that only runs when the other arms pass is not
    // measuring seed dependence, it is reporting that the loop bailed.
    const all = [...a, ...b];
    deals.add(all.join(','));
    if (a.join(',') === b.join(',')) {
      bad.push(`seed ${seed}: BOTH PLAYERS HOLD THE SAME 26 CARDS`);
      continue;
    }
    if (new Set(all).size !== 52) {
      bad.push(`seed ${seed}: ${new Set(all).size} distinct cards across the two hands`);
    }
    if (all.some(c => c < 0 || c > 51)) bad.push(`seed ${seed}: a card is off the deck`);
  }
  ok('the deal gives each player twenty-six DIFFERENT cards, the whole deck once',
     bad.length === 0, bad.slice(0, 3).join('; ') || '40 deals');
  ok('the deal depends on the seed', deals.size > 1, `${deals.size} distinct from 40 seeds`);
  ok('a slot off the hand reads -1', e.wr_p1c(e.wr_new(1), 52) === -1 && e.wr_p2c(e.wr_new(1), -1) === -1);
  ok('control: rank is the card modulo thirteen and covers all thirteen',
     new Set([...Array(52)].map((_, c) => e.wr_rank(c))).size === 13 &&
     [...Array(52)].every((_, c) => e.wr_rank(c) === rank(c)));
}

// -- The first round follows from the two top cards ----------------------
{
  const bad = [];
  let plainWins = 0, wars = 0;
  for (let seed = 1; seed <= 40; seed++) {
    const h = e.wr_new(seed);
    const c1 = e.wr_p1c(h, 0), c2 = e.wr_p2c(h, 0);
    const next = e.wr_round(h);
    if (rank(c1) === rank(c2)) { wars++; continue; }
    plainWins++;
    const winner = rank(c1) > rank(c2) ? 1 : 2;
    // The winner gains one card net, the loser loses one.
    const want1 = winner === 1 ? 27 : 25;
    const want2 = winner === 1 ? 25 : 27;
    if (e.wr_p1n(next) !== want1 || e.wr_p2n(next) !== want2) {
      bad.push(`seed ${seed}: ${rank(c1)} against ${rank(c2)} left ` +
               `${e.wr_p1n(next)}/${e.wr_p2n(next)}, expected ${want1}/${want2}`);
    }
  }
  ok('the higher card takes both, so the winner is up one and the loser down one',
     bad.length === 0, bad.slice(0, 3).join('; ') || `${plainWins} plain rounds`);
  ok('control: most first rounds are not ties, so the arm was exercised',
     plainWins > wars, `${plainWins} plain, ${wars} wars`);
}

// -- THE COPY ARM ---------------------------------------------------------
{
  const h = e.wr_new(3);
  const before = JSON.stringify([hand1(h), hand2(h), e.wr_p1n(h), e.wr_p2n(h)]);
  const next = e.wr_round(h);
  ok('a round answers a different state', next !== h);
  ok('THE COPY ARM: the state the round was played from is untouched',
     JSON.stringify([hand1(h), hand2(h), e.wr_p1n(h), e.wr_p2n(h)]) === before);
  ok('control: the round changed something',
     e.wr_p1n(next) !== e.wr_p1n(h) || e.wr_p2n(next) !== e.wr_p2n(h));
}

// -- CONSERVATION ---------------------------------------------------------
// War moves cards between hands; nothing in the chapter's stated rules
// destroys one. "The higher face-up card takes all cards" is its own
// sentence about a war, so a war must not consume the face-down cards.
{
  const bad = [];
  let rounds = 0, warsSeen = 0;
  let firstLoss = null;
  for (let seed = 1; seed <= 25; seed++) {
    let h = e.wr_new(seed);
    let prev = e.wr_p1n(h) + e.wr_p2n(h);
    if (prev !== 52) { bad.push(`seed ${seed}: the deal holds ${prev} cards`); continue; }
    for (let r = 0; r < 60 && e.wr_p1n(h) > 0 && e.wr_p2n(h) > 0; r++) {
      const wasWar = rank(e.wr_p1c(h, 0)) === rank(e.wr_p2c(h, 0));
      h = e.wr_round(h);
      rounds++;
      if (wasWar) warsSeen++;
      const total = e.wr_p1n(h) + e.wr_p2n(h);
      if (total !== prev && firstLoss === null) {
        firstLoss = `seed ${seed} round ${r}: ${prev} cards became ${total}` +
                    (wasWar ? ' (in a war)' : ' (in a plain round)');
      }
      if (total !== prev) bad.push(`seed ${seed} round ${r}: ${prev} -> ${total}`);
      prev = total;
      if (bad.length > 2) break;
    }
    if (bad.length > 2) break;
  }
  ok('the fifty-two cards are conserved: every round only moves them',
     bad.length === 0, firstLoss || `${rounds} rounds, ${warsSeen} of them wars`);
  ok('control: wars did happen, so the arm covers the path that can lose cards',
     warsSeen > 0, `${warsSeen} wars in ${rounds} rounds`);
}

// -- RULES: every class of round, from a position built card by card -----
// The rules are pagat's (https://www.pagat.com/war/war.html), as War.codex
// states them. A round has no choice in it, so the legal set of each class
// is one outcome, and each arm asserts both packets exactly: the cards not
// played keep their order on top, and the taker's winnings go underneath,
// player 1's table cards and then player 2's. Each war arm is built so that
// the engine's earlier rules (three cards face down, a tied war card won by
// player 1) give a different answer.
{
  const C = (r, s = 0) => s * 13 + r;          // r: 0 is the two, 12 the ace
  const [R2, R3, R4, R5, R7, R9, RT, RJ, RQ, RK, RA] = [0, 1, 2, 3, 5, 7, 8, 9, 10, 11, 12];
  const pos = (a, b) => {
    let h = e.wr_empty(1);
    for (const c of a) h = e.wr_push1(h, c);
    for (const c of b) h = e.wr_push2(h, c);
    return h;
  };
  const same = (x, y) => x.length === y.length && x.every((v, i) => v === y[i]);
  const round = (name, a, b, want1, want2) => {
    const h = pos(a, b);
    const built = same(hand1(h), a) && same(hand2(h), b);
    const n = e.wr_round(h);
    const got1 = hand1(n), got2 = hand2(n);
    ok(name, built && same(got1, want1) && same(got2, want2),
       built ? `player 1 [${got1}] player 2 [${got2}], want [${want1}] and [${want2}]` : 'the position did not build');
  };
  round('a plain round: the higher card takes both, underneath',
    [C(RK), C(R5)], [C(R9, 1), C(R7, 1)], [C(R5), C(RK), C(R9, 1)], [C(R7, 1)]);
  round('a plain round the other way: player 2 takes',
    [C(R3), C(R5)], [C(RJ, 1), C(R7, 1)], [C(R5)], [C(R7, 1), C(R3), C(RJ, 1)]);
  round('aces are high: an ace takes a king',
    [C(RA), C(R5)], [C(RK, 1)], [C(R5), C(RA), C(RK, 1)], []);
  round('the two is lowest: a two loses to a three',
    [C(R2)], [C(R3, 1), C(R4, 1)], [], [C(R4, 1), C(R2), C(R3, 1)]);
  // War: tie, then ONE down and one up. Face-up are the third cards (a four
  // against a queen, player 2 takes); three down would have read the fifth
  // cards (a king against a two, player 1).
  round('a war is one card down and one up, and the higher up card takes the six',
    [C(R7), C(RT), C(R4), C(R9), C(RK), C(R5)],
    [C(R7, 1), C(RJ, 1), C(RQ, 1), C(R3, 1), C(R2, 1), C(R5, 1)],
    [C(R9), C(RK), C(R5)],
    [C(R3, 1), C(R2, 1), C(R5, 1), C(R7), C(RT), C(R4), C(R7, 1), C(RJ, 1), C(RQ, 1)]);
  // The up cards tie twice (nines, then threes) and the third up cards, a
  // two against an ace, give player 2 all fourteen. Three down would have
  // read the tied threes as its only up cards and given them to player 1.
  round('equal face-up cards continue the war until an up card is higher',
    [C(R7), C(RT), C(R9), C(R4), C(R3), C(RQ), C(R2), C(R5)],
    [C(R7, 1), C(RJ, 1), C(R9, 1), C(R2, 1), C(R3, 1), C(RK, 1), C(RA, 1), C(R4, 1)],
    [C(R5)],
    [C(R4, 1), C(R7), C(RT), C(R9), C(R4), C(R3), C(RQ), C(R2),
     C(R7, 1), C(RJ, 1), C(R9, 1), C(R2, 1), C(R3, 1), C(RK, 1), C(RA, 1)]);
  round('two cards after the tie are enough for a war',
    [C(R7), C(R2), C(RK)], [C(R7, 1), C(R3, 1), C(R4, 1)],
    [C(R7), C(R2), C(RK), C(R7, 1), C(R3, 1), C(R4, 1)], []);
  round('a player left with one card after the tie cannot play the war and loses: the other takes every card',
    [C(R7), C(RA)], [C(R7, 1), C(R2, 1), C(R3, 1), C(R4, 1)],
    [], [C(R2, 1), C(R3, 1), C(R4, 1), C(RA), C(R7), C(R7, 1)]);
  round('the same for player 2, with no card left after the tie',
    [C(R7), C(R2), C(R3), C(R4)], [C(R7, 1)],
    [C(R2), C(R3), C(R4), C(R7), C(R7, 1)], []);
  round('running short in a CONTINUED war loses the same way',
    [C(R7), C(RT), C(R9), C(RK)], [C(R7, 1), C(RJ, 1), C(R9, 1), C(R2, 1), C(R3, 1)],
    [], [C(R2, 1), C(R3, 1), C(RK), C(R7), C(RT), C(R9), C(R7, 1), C(RJ, 1), C(R9, 1)]);
  round('both short in the same war: each takes back what it put down',
    [C(R7), C(RA)], [C(R7, 1), C(R2, 1)], [C(RA), C(R7)], [C(R2, 1), C(R7, 1)]);
  {
    const h = pos([C(R7)], []);
    const n = e.wr_round(h);
    ok('a finished game refuses a round: the position comes back unchanged',
       e.wr_p1n(n) === 1 && e.wr_p2n(n) === 0 && e.wr_p1c(n, 0) === C(R7));
  }
}

// -- The whole game -------------------------------------------------------
{
  const bad = [];
  const outcomes = new Set();
  let totalRounds = 0;
  for (let seed = 1; seed <= 40; seed++) {
    const r = e.wr_run(seed);
    const w = e.wr_winner(r), rounds = e.wr_rounds(r);
    if (w !== 1 && w !== 2) bad.push(`seed ${seed}: winner ${w}`);
    if (rounds < 0 || rounds > 1000) bad.push(`seed ${seed}: ${rounds} rounds`);
    outcomes.add(`${w}:${rounds}`);
    totalRounds += rounds;
  }
  ok('every game names a winner and stops inside the thousand-round cap',
     bad.length === 0, bad.slice(0, 3).join('; ') || '40 games');
  ok('control: the games are not all the same game',
     outcomes.size > 1, `${outcomes.size} distinct outcomes`);
  console.log(`  note  40 games, ${(totalRounds / 40).toFixed(1)} rounds on average`);
}

console.log(fail === 0
  ? `\nPASS: War deals two hands and moves the cards between them (${pass} arms).`
  : `\nFAIL: ${fail} of ${pass + fail} arms.`);
process.exitCode = fail === 0 ? 0 : 1;
