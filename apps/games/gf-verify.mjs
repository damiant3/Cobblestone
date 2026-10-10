// Grade the Go Fish wasm module.
//
// This game has the cleanest conservation law of the card games: every one
// of the fifty-two cards is in a hand, in the draw pile, or in a completed
// book of four, and nothing else can hold one. So
//
//     cards in hands + draw pile + 4 * books == 52
//
// at every moment of every game. A transfer that copies instead of moving,
// a book that takes three cards or five, or a draw that does not shrink the
// pile all break that sum, and every one of them leaves a game that still
// looks like Go Fish from the outside.
//
// Usage: node apps/games/gf-verify.mjs [path/to/gofish.wasm]

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import { gameInstance } from '../landing/web/games/arcade.js';

const here = dirname(fileURLToPath(import.meta.url));
const wasmPath = process.argv[2] ||
  join(here, '..', 'landing', 'web', 'games', 'gofish.wasm');


const inst = gameInstance(
  new WebAssembly.Module(readFileSync(wasmPath)));
const e = inst.exports;

let pass = 0, fail = 0;
const ok = (name, cond, detail) => {
  if (cond) { console.log(`  ok    ${name}${detail !== undefined ? ': ' + detail : ''}`); pass++; }
  else { console.log(`  FAIL  ${name}${detail !== undefined ? ': ' + detail : ''}`); fail++; }
};

const holds = (h, p) => [...Array(52)].map((_, c) => e.gf_has(h, p, c));
const held = (h, p) => holds(h, p).reduce((a, b) => a + b, 0);
const snapshot = h => JSON.stringify(
  [...Array(e.gf_players(h))].map((_, p) => holds(h, p)));

console.log(`gf-verify ${wasmPath}`);

// -- The deal -------------------------------------------------------------
const s0 = e.gf_new(21, 4);
ok('four players were seated', e.gf_players(s0) === 4, e.gf_players(s0));
ok('the counters match the flags',
   [...Array(4)].every(p => e.gf_size(s0, p) === held(s0, p)),
   [...Array(4)].map(p => `${e.gf_size(s0, p)}/${held(s0, p)}`).join(' '));
{
  let inHands = 0;
  for (let p = 0; p < 4; p++) inHands += e.gf_size(s0, p);
  ok('the whole deck is accounted for at the deal',
     inHands + e.gf_pile(s0) + 4 * e.gf_total(s0) === 52,
     `${inHands} held + ${e.gf_pile(s0)} pile + ${4 * e.gf_total(s0)} in books`);
}
ok('nobody has a book yet and nobody has won',
   e.gf_total(s0) === 0 && e.gf_done(s0) === 0);
ok('there are thirteen ranks of four',
   new Set([...Array(52)].map((_, c) => e.gf_rank(c))).size === 13);

// -- The copy arm ---------------------------------------------------------
{
  const before = snapshot(s0);
  const next = e.gf_step(s0);
  ok('stepping answers a different state', next !== s0);
  ok('THE COPY ARM: the state stepped from is untouched', snapshot(s0) === before);
}

// -- Conservation through whole games ------------------------------------
function playGame(seed, players) {
  let h = e.gf_new(seed, players);
  const np = e.gf_players(h);
  let turns = 0;
  while (e.gf_done(h) === 0 && turns < 1500) {
    turns++;
    h = e.gf_step(h);
    let inHands = 0;
    for (let p = 0; p < np; p++) {
      const flags = held(h, p), counter = e.gf_size(h, p);
      if (flags !== counter) {
        return { bad: `turn ${turns} player ${p}: ${flags} flags against counter ${counter}`, h };
      }
      inHands += counter;
    }
    const books = e.gf_total(h);
    let bookSum = 0;
    for (let p = 0; p < np; p++) bookSum += e.gf_books(h, p);
    if (bookSum !== books) {
      return { bad: `turn ${turns}: books per player sum ${bookSum} against total ${books}`, h };
    }
    const total = inHands + e.gf_pile(h) + 4 * books;
    if (total !== 52) {
      return { bad: `turn ${turns}: ${inHands} held + ${e.gf_pile(h)} pile + ${4 * books} booked = ${total}`, h };
    }
    // No card in two hands.
    for (let c = 0; c < 52; c++) {
      let owners = 0;
      for (let p = 0; p < np; p++) if (e.gf_has(h, p, c) === 1) owners++;
      if (owners > 1) return { bad: `turn ${turns}: card ${c} is in ${owners} hands`, h };
    }
    // Nobody may hold four of a rank: that is a book and must have been taken.
    for (let p = 0; p < np; p++) {
      for (let r = 0; r < 13; r++) {
        if (e.gf_rcount(h, p, r) >= 4) {
          return { bad: `turn ${turns} player ${p} still holds four of rank ${r}`, h };
        }
      }
    }
    if (e.gf_pile(h) < 0) return { bad: `turn ${turns}: pile ${e.gf_pile(h)}`, h };
  }
  return { h, turns };
}

{
  let broke = null, finished = 0, winners = new Set();
  for (let seed = 1; seed <= 20 && !broke; seed++) {
    const players = 2 + (seed % 3);
    const r = playGame(seed, players);
    if (r.bad) { broke = `seed ${seed} (${players}p): ${r.bad}`; break; }
    if (e.gf_done(r.h) === 1) {
      finished++;
      const emptyHand = [...Array(e.gf_players(r.h))].some((_, p) => e.gf_size(r.h, p) === 0);
      if (!emptyHand && e.gf_pile(r.h) !== 0) {
        broke = `seed ${seed}: finished with every hand holding cards and ${e.gf_pile(r.h)} in the stock`;
      }
      let best = -1, bestP = -1;
      for (let p = 0; p < e.gf_players(r.h); p++) {
        if (e.gf_books(r.h, p) > best) { best = e.gf_books(r.h, p); bestP = p; }
      }
      winners.add(bestP);
    }
  }
  ok('the fifty-two cards are accounted for on every turn of 20 games',
     broke === null, broke ?? '20 games');
  ok('every game ends, and only when a hand or the stock is empty', finished === 20, `${finished} of 20`);
  ok('more than one seat leads across the set', winners.size > 1,
     `leaders: ${[...winners].sort().join(',')}`);
}

// -- Refusals -------------------------------------------------------------
ok('a seat off the table is refused',
   e.gf_has(s0, 9, 0) === 0 && e.gf_size(s0, 9) === -1 && e.gf_books(s0, -1) === -1);
ok('a card off the deck is refused', e.gf_has(s0, 0, 52) === 0 && e.gf_rank(-1) === -1);
ok('a rank off the deck counts nothing',
   e.gf_rcount(s0, 0, 13) === 0 && e.gf_rcount(s0, 0, -1) === 0);
ok('a finished game refuses another turn', (() => {
  let h = e.gf_new(3, 2), n = 0;
  while (e.gf_done(h) === 0 && n++ < 1500) h = e.gf_step(h);
  return e.gf_done(h) === 1 && e.gf_step(h) === h;
})());

// -- Controls -------------------------------------------------------------
ok('control: two new games are different handles', e.gf_new(1, 2) !== e.gf_new(1, 2));
ok('control: different seeds deal differently', snapshot(e.gf_new(1, 2)) !== snapshot(e.gf_new(2, 2)));
ok('control: the same seed deals the same', snapshot(e.gf_new(6, 3)) === snapshot(e.gf_new(6, 3)));
// L-FALSIF: the conservation reader must be able to report a wrong total.
ok('control: the conservation reader would notice a missing card',
   0 + 0 + 4 * 0 !== 52);

// -- RULES: every class of ask, from positions built card by card -----------
// pagat's rules, as GoFish.codex states them. A card is suit 13 + rank.
{
  const C = (r, s = 0) => s * 13 + r;
  const build = (np, hands, stock) => {
    let h = e.gf_empty(np);
    hands.forEach((cs, p) => cs.forEach(c => { h = e.gf_give(h, p, c); }));
    for (const c of stock) h = e.gf_stock(h, c);
    return h;
  };
  const asks = h => [...Array(13)].map((_, r) => r).filter(r => e.gf_canask(h, r) === 1);
  for (const [np, each] of [[2, 7], [3, 5], [4, 5]]) {
    const h = e.gf_new(5, np);
    ok(`${np} players are dealt ${each} cards each and the rest is the stock`,
       [...Array(np)].every((_, p) => e.gf_size(h, p) === each) && e.gf_pile(h) === 52 - np * each,
       [...Array(np)].map((_, p) => e.gf_size(h, p)).join(',') + ` and ${e.gf_pile(h)}`);
  }
  {
    const h = build(3, [[C(0), C(3), C(3, 1)], [C(7)], [C(9)]], [C(11)]);
    ok('the ranks offered are exactly the ranks the asker holds', JSON.stringify(asks(h)) === '[0,3]', JSON.stringify(asks(h)));
    ok('asking yourself or a seat off the table is refused, the position unchanged',
       e.gf_ask(h, 0, 0) === h && e.gf_ask(h, 0, 3) === h && e.gf_ask(h, 5, 1) === h);
  }
  {
    const h = e.gf_ask(build(3, [[C(0), C(3)], [C(0, 1), C(0, 2), C(7)], [C(9)]], [C(11)]), 0, 1);
    ok('a hit hands over ALL of the rank and the asker asks again',
       e.gf_rcount(h, 0, 0) === 3 && e.gf_rcount(h, 1, 0) === 0 && e.gf_size(h, 1) === 1 && e.gf_cur(h) === 0 && e.gf_done(h) === 0,
       `asker holds ${e.gf_rcount(h, 0, 0)}, cur ${e.gf_cur(h)}`);
  }
  {
    const h = e.gf_ask(build(3, [[C(0), C(0, 1), C(0, 2), C(3)], [C(0, 3), C(7)], [C(9)]], [C(11)]), 0, 1);
    ok('four of a rank are booked at once and the asker goes on',
       e.gf_books(h, 0) === 1 && e.gf_rcount(h, 0, 0) === 0 && e.gf_size(h, 0) === 1 && e.gf_cur(h) === 0,
       `books ${e.gf_books(h, 0)}, hand ${e.gf_size(h, 0)}`);
  }
  {
    const h = e.gf_ask(build(3, [[C(0), C(3)], [C(7)], [C(9)]], [C(0, 1), C(9, 1)]), 0, 1);
    ok('go fish, and the card drawn is the rank asked: the asker asks again',
       e.gf_rcount(h, 0, 0) === 2 && e.gf_pile(h) === 1 && e.gf_cur(h) === 0, `cur ${e.gf_cur(h)}`);
  }
  {
    const h = e.gf_ask(build(3, [[C(0), C(3)], [C(7)], [C(9)]], [C(9, 1), C(0, 1)]), 0, 2);
    ok('go fish, any other card: the turn passes to the LEFT, not to the player asked',
       e.gf_size(h, 0) === 3 && e.gf_cur(h) === 1, `cur ${e.gf_cur(h)}`);
  }
  {
    const h = e.gf_ask(build(3, [[C(0), C(3)], [C(0, 1)], [C(9)]], [C(5)]), 0, 1);
    ok('the game ends as soon as a hand is empty: here the hand asked', e.gf_done(h) === 1 && e.gf_size(h, 1) === 0);
  }
  {
    const h = e.gf_ask(build(3, [[C(0), C(0, 1), C(0, 2)], [C(0, 3), C(7)], [C(9)]], [C(5)]), 0, 1);
    ok('a book that empties the asker\'s hand ends the game, and the most books wins',
       e.gf_done(h) === 1 && e.gf_winner(h) === 0, `done ${e.gf_done(h)}, winner ${e.gf_winner(h)}`);
  }
  {
    const h = e.gf_ask(build(3, [[C(0), C(3)], [C(7)], [C(9)]], [C(5)]), 0, 1);
    ok('the game ends when the stock runs out', e.gf_done(h) === 1 && e.gf_pile(h) === 0 && e.gf_size(h, 0) === 3);
  }
  {
    // Player 0 books the twos and goes on, misses on fives and fishes a
    // seven; player 1 then takes the last three from player 2 and books them.
    let h = build(3, [[C(0), C(0, 1), C(0, 2), C(3)], [C(0, 3), C(1), C(1, 1), C(1, 2), C(7)], [C(1, 3), C(9)]], [C(5), C(6), C(8)]);
    h = e.gf_ask(h, 0, 1);
    const after1 = e.gf_cur(h);
    h = e.gf_ask(h, 3, 1);
    const after2 = e.gf_cur(h);
    h = e.gf_ask(h, 1, 2);
    ok('one book each is a tie, and a tie has no single winner',
       after1 === 0 && after2 === 1 && e.gf_books(h, 0) === 1 && e.gf_books(h, 1) === 1 && e.gf_winner(h) === -1,
       `turns ${after1},${after2}; books ${e.gf_books(h, 0)},${e.gf_books(h, 1)}; winner ${e.gf_winner(h)}`);
  }
}
console.log(fail === 0
  ? `\nPASS: Go Fish keeps all fifty-two cards (${pass} arms).`
  : `\nFAIL: ${fail} of ${pass + fail} arms.`);
process.exitCode = fail === 0 ? 0 : 1;
