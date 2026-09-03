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
//
// The ace is the LOWEST card in Spider. A card id's `mod 13` is the page's
// ace-high encoding, where 12 is the ace and 0 the deuce, so the ace folds
// to 0 and everything else shifts up: king 11 becomes 12, the highest. This
// oracle read `c % 13` straight until 2026-09-02 and so agreed with the
// engine that an ace may not be laid on a deuce -- both were wrong the same
// way, which is exactly what an oracle sharing its subject's mistake looks
// like from the outside: 558 green arms and a rule nobody could play.
const rank = c => { const r = c % 13; return r === 12 ? 0 : r + 1; };
// The arm's OWN reading of a card id, so the rule model below is not built
// out of engine calls. That this reading is the right one is established
// separately, by the suit-count arm: it asks for one, two, three and four
// suits and counts what comes back.
const suit = c => Math.floor(c / 13);

console.log(`sp-verify ${wasmPath}`);

// -- THE SUIT COUNT -------------------------------------------------------
// Count what the engine DEALT, not what sp-suit answers over ids 0 to 103.
// Card ids are no longer those numbers: the suit count is baked into them at
// deal time, so a two-suit deck uses ids 0 to 25 and a one-suit deck 0 to 12.
// Measuring the function over a range the deck does not use would report on
// the function and say nothing about the deal.
{
  const deal = n => {
    const h = e.sp_new(5, n);
    // The stock is not readable card by card, so the face-up 54 carry this
    // and the counter accounts for the rest.
    return { cards: allCards(h), stock: e.sp_stockn(h) };
  };
  const two = deal(2);
  const suits = new Set(two.cards.map(c => e.sp_suit(c)));
  console.log(`  note  a two-suit deal shows ${suits.size} suits over its ` +
              `${two.cards.length} face-up cards`);
  ok('the deck is the two-suit deck the chapter says it is',
     suits.size === 2, `${suits.size} suits among the cards dealt`);
  ok('control: every rank appears in a deal',
     new Set(two.cards.map(c => e.sp_rank(c))).size === 13);
  // The option is the whole point: ask for one, three or four and get it.
  for (const n of [1, 3, 4]) {
    const d = deal(n);
    const got = new Set(d.cards.map(c => e.sp_suit(c))).size;
    ok(`asking for ${n} suit${n > 1 ? 's' : ''} deals ${n}`, got === n, `${got} came out`);
    ok(`a ${n}-suit deck is still a hundred and four cards`,
       d.cards.length + d.stock === 104, `${d.cards.length} dealt + ${d.stock} stock`);
  }
  ok('a card off the deck reads -1', e.sp_suit(104) === -1 && e.sp_rank(-1) === -1);

  // The ace, on its own. Card id `suit * 13 + (i mod 13)`, so in suit 0 the
  // deuce is 0, the king 11 and the ace 12. Spider's ace is the LOWEST card
  // and these two arms are the ones the ace-high engine failed: it answered
  // rank 12 for the ace, which is one ABOVE the king, and asked whether
  // 12 === -1 when a player tried to lay an ace on a deuce.
  ok('the ace is the lowest rank and the king the highest',
     e.sp_rank(12) === 0 && e.sp_rank(11) === 12,
     `ace reads ${e.sp_rank(12)}, king reads ${e.sp_rank(11)}`);
  ok('an ace sits exactly one rank below a deuce',
     e.sp_rank(12) === e.sp_rank(0) - 1,
     `ace ${e.sp_rank(12)} against deuce ${e.sp_rank(0)}`);
  ok('control: the thirteen ranks are still 0 to 12 with no collision',
     new Set([...Array(13)].map((_, i) => e.sp_rank(i))).size === 13);
}

// -- The deal -------------------------------------------------------------
{
  const bad = [];
  const deals = new Set();
  for (let seed = 1; seed <= 40; seed++) {
    const h = e.sp_new(seed, 2);
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
     e.sp_coln(e.sp_new(1, 2), 10) === -1 && e.sp_card(e.sp_new(1, 2), 0, 99) === -1);
}

// -- The deal is the whole deck, once each -------------------------------
// The stock is not readable card by card, so this counts what IS readable
// and checks the table holds no duplicate.
{
  // A Spider deck REPEATS. Two suits is four copies of each of twenty-six
  // cards, one suit is eight copies of thirteen. This arm used to ask for no
  // duplicates at all, which the old encoding satisfied only because it
  // numbered all 104 cards distinctly -- an artefact of the representation,
  // not a rule of the game. What the rules bound is how MANY copies exist.
  const bad = [];
  for (const suits of [1, 2, 4]) {
    const copies = 104 / (13 * suits);
    for (let seed = 1; seed <= 40; seed++) {
      const cards = allCards(e.sp_new(seed, suits));
      const seen = {};
      for (const c of cards) {
        seen[c] = (seen[c] || 0) + 1;
        if (seen[c] > copies) {
          bad.push(`${suits}-suit seed ${seed}: card ${c} dealt ${seen[c]} times, deck holds ${copies}`);
          break;
        }
      }
    }
  }
  ok('no card is dealt more often than the deck contains it',
     bad.length === 0, bad.slice(0, 3).join('; ') || 'three suit counts, 40 deals each');
}

// -- Sequences ------------------------------------------------------------
// sp-seq-len from index i must be the length of the same-suit descending
// run starting there, which is decidable from the cards themselves.
{
  const bad = [];
  let checked = 0, nonTrivial = 0;
  for (let seed = 1; seed <= 40; seed++) {
    const h = e.sp_new(seed, 2);
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
    const h = e.sp_new(seed, 2);
    const b = board(h);
    for (let from = 0; from < COLS; from++) {
      for (let to = 0; to < COLS; to++) {
        if (from === to) continue;
        const fc = b[from], tc = b[to];
        for (let i = 0; i < fc.length; i++) {
          // TWO conditions, and the arm works both out from the cards it can
          // see rather than asking the engine, or it would be checking the
          // engine against itself.
          //
          // First, what is lifted has to BE a sequence: from i to the bottom
          // of the column, each card one rank below the last and the same
          // suit. Reached from a browser anything can be asked for, so this
          // is not something a caller can be trusted to respect.
          let isRun = true;
          for (let k = i + 1; k < fc.length; k++) {
            if (rank(fc[k]) !== rank(fc[k - 1]) - 1 || suit(fc[k]) !== suit(fc[k - 1])) {
              isRun = false;
              break;
            }
          }
          // Second, the destination: empty takes anything, otherwise the
          // lifted card sits one rank below the target's top card.
          const fits = tc.length === 0 || rank(fc[i]) === rank(tc[tc.length - 1]) - 1;
          const want = isRun && fits ? 1 : 0;
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
  ok('legality is a real sequence lifted onto a card one rank above it',
     bad.length === 0, bad.slice(0, 3).join('; ') || `${legal} legal, ${illegal} refused`);
  ok('control: both answers occurred, so the arm can tell them apart',
     legal > 0 && illegal > 0);
  ok('a move off the board is refused',
     e.sp_can(e.sp_new(1, 2), 10, 0, 1) === 0 && e.sp_can(e.sp_new(1, 2), 0, 0, 10) === 0 &&
     e.sp_can(e.sp_new(1, 2), 0, 0, 0) === 0 && e.sp_can(e.sp_new(1, 2), 0, 99, 1) === 0);
}

// -- THE REFUSAL AND COPY ARMS -------------------------------------------
{
  const h = e.sp_new(4, 2);
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
  const h = e.sp_new(6, 2);
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

// -- An ace onto a deuce, on a real board ---------------------------------
// The rank arms above test the rule; this one tests that a player can
// actually make the move, which is the thing that was broken. It hunts real
// deals for a column topped by an ace and another topped by a deuce, and
// asks the engine's own legality test. The hunt REPORTS WHETHER IT FOUND
// THE POSITION: an arm that never reached its condition and printed nothing
// would read exactly like an arm that passed (L-VACUOUS).
//
// THE ACE IS FOUND BY ITS CARD ID, NOT BY `sp_rank`. Written the obvious
// way -- hunt for a top card whose sp_rank is 0 -- the arm reached the ace
// only THROUGH the function under test, so against the ace-high engine it
// dutifully found 25 positions of a DEUCE on a THREE, accepted every one,
// and passed. It measured the one thing it could not fail on. A card id is
// `suit * 13 + (i mod 13)` and that encoding is the page's, fixed and
// independent of any rule this engine holds: `% 13 === 12` is the ace and
// `=== 0` the deuce, whichever way round the engine ranks them.
const isAce = c => c >= 0 && c % 13 === 12;
const isDeuce = c => c >= 0 && c % 13 === 0;
{
  let found = 0, legal = 0, firstMiss = '';
  for (let seed = 1; seed <= 400 && found < 25; seed++) {
    const h = e.sp_new(seed, 4);
    const tops = [...Array(COLS)].map((_, c) => {
      const n = e.sp_coln(h, c);
      return n ? e.sp_card(h, c, n - 1) : -1;
    });
    for (let a = 0; a < COLS; a++) {
      if (!isAce(tops[a])) continue;
      for (let d = 0; d < COLS; d++) {
        if (d === a || !isDeuce(tops[d])) continue;
        found++;
        const start = e.sp_coln(h, a) - 1;
        if (e.sp_can(h, a, start, d) === 1) legal++;
        else if (!firstMiss) firstMiss = `seed ${seed}: ace on col ${a} refused by deuce on col ${d}`;
      }
    }
  }
  console.log(`  note  found ${found} ace-on-deuce positions in the first 400 four-suit deals`);
  ok('the hunt reached its condition at all', found > 0,
     found ? `${found} positions` : 'NO ace-on-deuce position found, so the arm below measured nothing');
  ok('an ace may be laid on a deuce', found > 0 && legal === found,
     `${legal} of ${found} accepted${firstMiss ? '; ' + firstMiss : ''}`);
}

console.log(fail === 0
  ? `\nPASS: Spider deals, sequences and moves by its rules (${pass} arms).`
  : `\nFAIL: ${fail} of ${pass + fail} arms.`);
process.exitCode = fail === 0 ? 0 : 1;

