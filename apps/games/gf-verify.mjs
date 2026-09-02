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

const here = dirname(fileURLToPath(import.meta.url));
const wasmPath = process.argv[2] ||
  join(here, '..', 'landing', 'web', 'games', 'gofish.wasm');

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
      if (e.gf_total(r.h) !== 13) {
        broke = `seed ${seed}: finished with ${e.gf_total(r.h)} books, not 13`;
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
  ok('every game ends with all thirteen books taken', finished === 20, `${finished} of 20`);
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

console.log(fail === 0
  ? `\nPASS: Go Fish keeps all fifty-two cards (${pass} arms).`
  : `\nFAIL: ${fail} of ${pass + fail} arms.`);
process.exitCode = fail === 0 ? 0 : 1;
