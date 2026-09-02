// Grade the Spider Solitaire wasm module.
//
// The chapter's first line says "Spider Solitaire, 2-suit variant" and its
// next clause says "suit = card / 13". Over 104 cards those two statements
// disagree: card / 13 takes eight distinct values, which is eight-suit
// Spider, the hardest variant there is. The suit arm below decides which of
// them the code actually implements, by counting.
//
// That matters because same-suit adjacency is the whole game. A run only
// builds, and only completes, within one suit, so the number of suits sets
// how hard the deal is; and GAME-3 records the solver completing 0 of 8
// suits and reads that as a limit of greedy search.
//
// The rest is structural and decidable: the deal is a permutation of the
// 104 cards, a sequence is a same-suit descending run, a legal move needs
// only rank, an illegal one must change nothing, and a completed suit must
// really be a king-to-ace run of one suit leaving the column shorter by 13.
//
// Usage: node apps/games/sp-verify.mjs [path/to/spider.wasm]

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const wasmPath = process.argv[2] ||
  join(here, '..', 'landing', 'web', 'games', 'spider.wasm');

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

const COLS = 10;
const col = (h, c) => [...Array(e.sp_coln(h, c))].map((_, i) => e.sp_card(h, c, i));
const board = h => [...Array(COLS)].map((_, c) => col(h, c));
const allCards = h => board(h).flat();

// The rules, independently: rank is the card modulo 13, and a sequence is a
// descending run of one suit.
const rank = c => c % 13;

console.log(`sp-verify ${wasmPath}`);

// -- THE SUIT COUNT -------------------------------------------------------
// Two suits or eight is not a matter of opinion: count the distinct values
// the engine's own sp-suit takes over the 104 cards it deals.
{
  const suits = new Set([...Array(104)].map((_, c) => e.sp_suit(c)));
  const perSuit = {};
  for (let c = 0; c < 104; c++) {
    const s = e.sp_suit(c);
    perSuit[s] = (perSuit[s] || 0) + 1;
  }
  const counts = Object.values(perSuit);
  console.log(`  note  sp-suit takes ${suits.size} distinct values over the 104 cards, ` +
              `${counts[0]} cards each`);
  ok('the deck is the two-suit deck the chapter says it is',
     suits.size === 2, `${suits.size} suits, ${counts.length} groups of ${counts[0]}`);
  ok('control: every card has thirteen ranks available and the ranks are 0..12',
     new Set([...Array(104)].map((_, c) => e.sp_rank(c))).size === 13);
  ok('control: each suit holds a whole number of complete thirteen-card runs',
     counts.every(n => n % 13 === 0), counts.join(','));
  ok('a card off the deck reads -1', e.sp_suit(104) === -1 && e.sp_rank(-1) === -1);
}

// -- The deal -------------------------------------------------------------
{
  const bad = [];
  const deals = new Set();
  for (let seed = 1; seed <= 40; seed++) {
    const h = e.sp_new(seed);
    const sizes = [...Array(COLS)].map((_, c) => e.sp_coln(h, c));
    const cards = allCards(h);
    const stock = e.sp_stockn(h);
    if (JSON.stringify(sizes) !== JSON.stringify([6, 6, 6, 6, 5, 5, 5, 5, 5, 5])) {
      bad.push(`seed ${seed}: columns ${sizes.join(',')}`);
    }
    if (stock !== 50) bad.push(`seed ${seed}: stock of ${stock}`);
    if (cards.length + stock !== 104) bad.push(`seed ${seed}: ${cards.length + stock} cards in play`);
    if (e.sp_suits(h) !== 0 || e.sp_moves(h) !== 0) bad.push(`seed ${seed}: not a fresh deal`);
    if (cards.some(c => c < 0 || c > 103)) bad.push(`seed ${seed}: a card is off the deck`);
    deals.add(cards.join(','));
  }
  ok('every deal is 6,6,6,6,5,5,5,5,5,5 on the table with fifty in the stock',
     bad.length === 0, bad.slice(0, 3).join('; ') || '40 deals');
  ok('the deal depends on the seed',
     deals.size > 1, `${deals.size} distinct deals from 40 seeds`);
  ok('a column or card off the board reads -1',
     e.sp_coln(e.sp_new(1), 10) === -1 && e.sp_card(e.sp_new(1), 0, 99) === -1);
}

// -- The deal is the whole deck, once each -------------------------------
// The stock is not readable card by card, so this counts what IS readable
// and checks the table holds no duplicate.
{
  const bad = [];
  for (let seed = 1; seed <= 40; seed++) {
    const cards = allCards(e.sp_new(seed));
    if (new Set(cards).size !== cards.length) bad.push(`seed ${seed}: a card is dealt twice`);
  }
  ok('no card is dealt to the table twice',
     bad.length === 0, bad.slice(0, 3).join('; ') || '40 deals, 54 cards each');
}

// -- Sequences ------------------------------------------------------------
// sp-seq-len from index i must be the length of the same-suit descending
// run starting there, which is decidable from the cards themselves.
{
  const bad = [];
  let checked = 0, nonTrivial = 0;
  for (let seed = 1; seed <= 40; seed++) {
    const h = e.sp_new(seed);
    for (let c = 0; c < COLS; c++) {
      const cards = col(h, c);
      for (let i = 0; i < cards.length; i++) {
        let want = 1;
        while (i + want < cards.length &&
               rank(cards[i + want]) === rank(cards[i + want - 1]) - 1 &&
               e.sp_suit(cards[i + want]) === e.sp_suit(cards[i + want - 1])) want++;
        const got = e.sp_seqlen(h, c, i);
        checked++;
        if (want > 1) nonTrivial++;
        if (got !== want) {
          bad.push(`seed ${seed} col ${c} idx ${i}: engine ${got}, rules ${want}`);
        }
      }
      if (bad.length > 3) break;
    }
  }
  ok('every sequence length is the same-suit descending run that is really there',
     bad.length === 0, bad.slice(0, 3).join('; ') || `${checked} positions`);
  ok('control: some runs are longer than one card, so the arm is not vacuous',
     nonTrivial > 0, `${nonTrivial} runs of two or more`);
}

// -- Legality and the refusal --------------------------------------------
{
  const bad = [];
  let legal = 0, illegal = 0;
  for (let seed = 1; seed <= 25; seed++) {
    const h = e.sp_new(seed);
    const b = board(h);
    for (let from = 0; from < COLS; from++) {
      for (let to = 0; to < COLS; to++) {
        if (from === to) continue;
        const fc = b[from], tc = b[to];
        for (let i = 0; i < fc.length; i++) {
          // The engine's rule: an empty target takes anything, otherwise the
          // moved card must be one rank below the target's top card.
          const want = tc.length === 0 ? 1 : (rank(fc[i]) === rank(tc[tc.length - 1]) - 1 ? 1 : 0);
          const got = e.sp_can(h, from, i, to);
          if (got !== want) {
            bad.push(`seed ${seed}: ${from}[${i}]->${to} engine ${got}, rule ${want}`);
            break;
          }
          if (want) legal++; else illegal++;
        }
        if (bad.length) break;
      }
      if (bad.length) break;
    }
  }
  ok('legality is rank alone, an empty column taking anything',
     bad.length === 0, bad.slice(0, 3).join('; ') || `${legal} legal, ${illegal} refused`);
  ok('control: both answers occurred, so the arm can tell them apart',
     legal > 0 && illegal > 0);
  ok('a move off the board is refused',
     e.sp_can(e.sp_new(1), 10, 0, 1) === 0 && e.sp_can(e.sp_new(1), 0, 0, 10) === 0 &&
     e.sp_can(e.sp_new(1), 0, 0, 0) === 0 && e.sp_can(e.sp_new(1), 0, 99, 1) === 0);
}

// -- THE REFUSAL AND COPY ARMS -------------------------------------------
{
  const h = e.sp_new(4);
  const before = JSON.stringify(board(h));
  // Find an illegal move and a legal one.
  let illegalTriple = null, legalTriple = null;
  const b = board(h);
  for (let from = 0; from < COLS && !(illegalTriple && legalTriple); from++) {
    for (let to = 0; to < COLS; to++) {
      if (from === to) continue;
      for (let i = 0; i < b[from].length; i++) {
        const can = e.sp_can(h, from, i, to);
        if (can === 0 && !illegalTriple) illegalTriple = [from, i, to];
        if (can === 1 && !legalTriple) legalTriple = [from, i, to];
      }
    }
  }
  ok('control: the deal offers both a legal and an illegal move to test with',
     illegalTriple !== null && legalTriple !== null);
  if (illegalTriple) {
    ok('an illegal move is refused and answers the same state',
       e.sp_move(h, ...illegalTriple) === h);
  }
  ok('THE REFUSAL ARM: the board is untouched by the refusal',
     JSON.stringify(board(h)) === before);
  if (legalTriple) {
    const next = e.sp_move(h, ...legalTriple);
    ok('a legal move answers a different state', next !== h);
    ok('THE COPY ARM: the board the move was made from is untouched',
       JSON.stringify(board(h)) === before);
    ok('the move counter advanced by one', e.sp_moves(next) === e.sp_moves(h) + 1);
    const movedCount = b[legalTriple[0]].length - legalTriple[1];
    ok('the cards left one column and arrived at the other',
       e.sp_coln(next, legalTriple[0]) === e.sp_coln(h, legalTriple[0]) - movedCount &&
       (e.sp_coln(next, legalTriple[2]) === e.sp_coln(h, legalTriple[2]) + movedCount ||
        e.sp_suits(next) > e.sp_suits(h)),
       `${movedCount} cards`);
  }
}

// -- The stock deal -------------------------------------------------------
{
  const h = e.sp_new(6);
  const before = JSON.stringify(board(h));
  const beforeStock = e.sp_stockn(h);
  const next = e.sp_deal(h);
  ok('dealing the stock answers a different state', next !== h);
  ok('THE COPY ARM: the state dealt from is untouched',
     JSON.stringify(board(h)) === before && e.sp_stockn(h) === beforeStock);
  ok('ten cards left the stock and ten reached the table',
     e.sp_stockn(next) === beforeStock - 10 &&
     allCards(next).length === allCards(h).length + 10,
     `stock ${beforeStock} to ${e.sp_stockn(next)}`);
  ok('every column grew by exactly one',
     [...Array(COLS)].every((_, c) => e.sp_coln(next, c) === e.sp_coln(h, c) + 1));
}

// -- Whole games ----------------------------------------------------------
{
  const bad = [];
  const results = [];
  let won = 0;
  for (let seed = 1; seed <= 30; seed++) {
    const r = e.sp_run(seed);
    const suits = e.sp_rsuits(r), moves = e.sp_rmoves(r), w = e.sp_rwon(r);
    results.push(suits);
    if (suits < 0 || suits > 8) bad.push(`seed ${seed}: ${suits} suits`);
    if (moves < 0) bad.push(`seed ${seed}: ${moves} moves`);
    if (w === 1 && suits !== 8) bad.push(`seed ${seed}: won with ${suits} of 8 suits`);
    if (w === 0 && suits === 8) bad.push(`seed ${seed}: eight suits and not won`);
    if (w === 1) won++;
  }
  ok('a game is won exactly when all eight suits are complete',
     bad.length === 0, bad.slice(0, 3).join('; ') || '30 games');
  const best = Math.max(...results);
  const total = results.reduce((a, b) => a + b, 0);
  console.log(`  note  30 games: ${won} won, best ${best} of 8 suits, ${total} suits completed in total`);
}

console.log(fail === 0
  ? `\nPASS: Spider deals, sequences and moves by its rules (${pass} arms).`
  : `\nFAIL: ${fail} of ${pass + fail} arms.`);
process.exitCode = fail === 0 ? 0 : 1;
