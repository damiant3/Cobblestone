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

const here = dirname(fileURLToPath(import.meta.url));
const wasmPath = process.argv[2] ||
  join(here, '..', 'landing', 'web', 'games', 'war.wasm');

const imports = {
  wasi_snapshot_preview1: {
    fd_write: () => { throw new Error('fd_write: the game module must not write'); },
    fd_read: () => { throw new Error('fd_read: the game module must not read'); },
  },
};

const inst = new WebAssembly.Instance(
  new WebAssembly.Module(readFileSync(wasmPath)), imports);
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
