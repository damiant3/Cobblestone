// Grade the Poker wasm module.
//
// Poker's evaluator is the rare game component with a total oracle: there
// are 2,598,960 five-card hands and the category of each is decidable, so
// this file classifies hands independently and compares. It does not
// enumerate all of them -- it takes every hand the shuffler deals plus a
// table of hands built by hand for the categories a random deal almost
// never produces, which is where an evaluator's holes actually are.
//
// Cards are 0-51, suit = card / 13, rank = card mod 13 with 0 being the
// deuce and 12 the ace. Categories: 0 high card, 1 pair, 2 two pair,
// 3 trips, 4 straight, 5 flush, 6 full house, 7 quads, 8 straight flush.
//
// Usage: node apps/games/pk-verify.mjs [path/to/poker.wasm]

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const wasmPath = process.argv[2] ||
  join(here, '..', 'landing', 'web', 'games', 'poker.wasm');

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

const seq5 = [0, 1, 2, 3, 4];
const rankOf = c => c % 13;
const suitOf = c => Math.floor(c / 13);
const card = (r, s) => s * 13 + r;   // r 0..12 (deuce..ace), s 0..3
const RANKNAME = ['2','3','4','5','6','7','8','9','T','J','Q','K','A'];
const SUITNAME = ['c','d','h','s'];
const show = cs => cs.map(c => RANKNAME[rankOf(c)] + SUITNAME[suitOf(c)]).join(' ');
const CATEGORY = ['high card','pair','two pair','trips','straight','flush',
                  'full house','quads','straight flush'];
const A = 12, K = 11, Q = 10, J = 9, T = 8;

// The oracle: the category of a five-card hand, by the rules.
function classify(cs) {
  const counts = new Map();
  for (const c of cs) counts.set(rankOf(c), (counts.get(rankOf(c)) || 0) + 1);
  const shape = [...counts.values()].sort((a, b) => b - a);
  const flush = new Set(cs.map(suitOf)).size === 1;
  const ranks = [...counts.keys()].sort((a, b) => a - b);
  // A straight is five distinct ranks in sequence. The ace plays low as
  // well as high, so A-2-3-4-5 is a straight and A-K-Q-J-T is a straight.
  let straight = false;
  if (ranks.length === 5) {
    if (ranks[4] - ranks[0] === 4) straight = true;
    else if (JSON.stringify(ranks) === JSON.stringify([0, 1, 2, 3, 12])) straight = true;
  }
  if (straight && flush) return 8;
  if (shape[0] === 4) return 7;
  if (shape[0] === 3 && shape[1] === 2) return 6;
  if (flush) return 5;
  if (straight) return 4;
  if (shape[0] === 3) return 3;
  if (shape[0] === 2 && shape[1] === 2) return 2;
  if (shape[0] === 2) return 1;
  return 0;
}

const engine = cs => e.pk_rank(e.pk_hand(...cs));

console.log(`pk-verify ${wasmPath}`);

// -- The deck -------------------------------------------------------------
{
  const bad = [];
  for (let seed = 1; seed <= 40; seed++) {
    const d = e.pk_deck(seed);
    const all = [...Array(52)].map((_, i) => e.pk_card(d, i));
    if (new Set(all).size !== 52) bad.push(`seed ${seed}: not a permutation`);
    if (all.some(c => c < 0 || c > 51)) bad.push(`seed ${seed}: a card is off the deck`);
    if (e.pk_card(d, 52) !== -1 || e.pk_card(d, -1) !== -1) bad.push(`seed ${seed}: off-deck read`);
  }
  ok('every shuffle is a permutation of the fifty-two cards',
     bad.length === 0, bad.length ? bad.slice(0, 3).join('; ') : '40 shuffles');
  ok('control: different seeds shuffle differently',
     e.pk_card(e.pk_deck(1), 0) !== e.pk_card(e.pk_deck(2), 0) ||
     e.pk_card(e.pk_deck(1), 1) !== e.pk_card(e.pk_deck(2), 1));
}

// -- Dealt hands ----------------------------------------------------------
{
  const bad = [];
  const seen = new Map();
  let hands = 0;
  for (let seed = 1; seed <= 400; seed++) {
    const d = e.pk_deck(seed);
    for (const start of [0, 5, 10, 15, 20]) {
      const cs = [0, 1, 2, 3, 4].map(i => e.pk_card(d, start + i));
      const want = classify(cs), got = engine(cs);
      hands++;
      seen.set(want, (seen.get(want) || 0) + 1);
      if (want !== got) {
        bad.push(`${show(cs)}: engine ${CATEGORY[got]}, rules ${CATEGORY[want]}`);
      }
    }
  }
  ok('every dealt hand is classified by the rules',
     bad.length === 0, bad.length ? bad.slice(0, 3).join('; ') : `${hands} hands`);
  console.log(`  note  dealt categories: ` +
    [...seen.entries()].sort((a, b) => a[0] - b[0])
      .map(([k, v]) => `${CATEGORY[k]} ${v}`).join(', '));
}

// -- THE BUILT HANDS ------------------------------------------------------
// A random deal produces a straight flush about once in 65,000 hands, so
// the categories an evaluator gets wrong are the ones no sweep will reach.
{

  const cases = [
    ['royal flush',        [card(A,0), card(K,0), card(Q,0), card(J,0), card(T,0)], 8],
    ['straight flush 9-5', [card(7,1), card(6,1), card(5,1), card(4,1), card(3,1)], 8],
    ['THE WHEEL, suited',  [card(A,2), card(0,2), card(1,2), card(2,2), card(3,2)], 8],
    ['THE WHEEL, offsuit', [card(A,0), card(0,1), card(1,2), card(2,3), card(3,0)], 4],
    ['broadway straight',  [card(A,0), card(K,1), card(Q,2), card(J,3), card(T,0)], 4],
    ['quads',              [card(5,0), card(5,1), card(5,2), card(5,3), card(A,0)], 7],
    ['full house',         [card(5,0), card(5,1), card(5,2), card(A,0), card(A,1)], 6],
    ['flush',              [card(A,0), card(J,0), card(8,0), card(4,0), card(2,0)], 5],
    ['trips',              [card(5,0), card(5,1), card(5,2), card(A,0), card(J,1)], 3],
    ['two pair',           [card(5,0), card(5,1), card(A,0), card(A,1), card(J,1)], 2],
    ['pair',               [card(5,0), card(5,1), card(A,0), card(J,1), card(3,2)], 1],
    ['ace high',           [card(A,0), card(J,1), card(8,2), card(4,3), card(2,0)], 0],
    ['ace-high not a straight', [card(A,0), card(K,1), card(Q,2), card(J,3), card(2,0)], 0],
    ['K-high wrap is not a straight', [card(Q,0), card(K,1), card(A,2), card(0,3), card(1,0)], 0],
  ];
  const bad = [];
  for (const [name, cs, want] of cases) {
    if (classify(cs) !== want) bad.push(`ORACLE WRONG on ${name}: says ${CATEGORY[classify(cs)]}`);
  }
  ok('control: the oracle gets every built hand right before the engine is asked',
     bad.length === 0, bad.length ? bad.join('; ') : `${cases.length} hands`);

  const wrong = [];
  for (const [name, cs, want] of cases) {
    const got = engine(cs);
    if (got !== want) wrong.push(`${name} (${show(cs)}): engine ${CATEGORY[got]}, rules ${CATEGORY[want]}`);
  }
  ok('every built hand is classified by the rules',
     wrong.length === 0, wrong.length ? wrong.join('; ') : `${cases.length} hands`);
}

// -- Comparison is a total order on categories ---------------------------
{
  const bad = [];
  const reps = [
    [card(A,0), card(J,1), card(8,2), card(4,3), card(2,0)],          // high card
    [card(5,0), card(5,1), card(A,0), card(J,1), card(3,2)],          // pair
    [card(5,0), card(5,1), card(A,0), card(A,1), card(J,1)],          // two pair
    [card(5,0), card(5,1), card(5,2), card(A,0), card(J,1)],          // trips
    [card(A,0), card(K,1), card(Q,2), card(J,3), card(T,0)],          // straight
    [card(A,0), card(J,0), card(8,0), card(4,0), card(2,0)],          // flush
    [card(5,0), card(5,1), card(5,2), card(A,0), card(A,1)],          // full house
    [card(5,0), card(5,1), card(5,2), card(5,3), card(A,0)],          // quads
    [card(A,0), card(K,0), card(Q,0), card(J,0), card(T,0)],          // straight flush
  ];
  for (let i = 0; i < reps.length; i++) {
    for (let j = 0; j < reps.length; j++) {
      const got = e.pk_cmp(e.pk_hand(...reps[i]), e.pk_hand(...reps[j]));
      const want = i > j ? 1 : j > i ? 2 : 0;
      if (got !== want) bad.push(`${CATEGORY[i]} against ${CATEGORY[j]}: ${got}, want ${want}`);
    }
  }
  ok('the categories compare in order, every pair of the nine',
     bad.length === 0, bad.length ? bad.slice(0, 3).join('; ') : '81 comparisons');
  ok('control: a hand ties itself',
     e.pk_cmp(e.pk_hand(...reps[0]), e.pk_hand(...reps[0])) === 0);
}

// -- A FIVE-HIGH STRAIGHT IS THE LOWEST STRAIGHT --------------------------
// Recognising the wheel is half the rule. The other half is that its high
// card is the five and not the ace, which no category test can see: both
// halves have to be right or the worst straight in the deck beats the best.
{
  const wheel    = [card(A,0), card(0,1), card(1,2), card(2,3), card(3,0)];
  const sixHigh  = [card(0,0), card(1,1), card(2,2), card(3,3), card(4,0)];
  const broadway = [card(A,0), card(K,1), card(Q,2), card(J,3), card(T,0)];
  const wheelF   = [card(A,2), card(0,2), card(1,2), card(2,2), card(3,2)];
  const sixHighF = [card(0,2), card(1,2), card(2,2), card(3,2), card(4,2)];
  const cmp = (a, b) => e.pk_cmp(e.pk_hand(...a), e.pk_hand(...b));
  ok('the wheel loses to a six-high straight', cmp(wheel, sixHigh) === 2);
  ok('the wheel loses to broadway', cmp(wheel, broadway) === 2);
  ok('the steel wheel loses to a six-high straight flush', cmp(wheelF, sixHighF) === 2);
  ok('control: broadway beats the six-high straight, so the order is not reversed',
     cmp(broadway, sixHigh) === 1);
  ok('control: the wheel ties itself', cmp(wheel, wheel) === 0);
}

// -- The session ----------------------------------------------------------
{
  const bad = [];
  for (let seed = 1; seed <= 20; seed++) {
    const r = e.pk_play(seed, 10);
    const p1 = e.pk_p1(r), p2 = e.pk_p2(r), n = e.pk_played(r), w = e.pk_winner(r);
    if (n !== 10) bad.push(`seed ${seed}: played ${n}`);
    if (p1 + p2 > n) bad.push(`seed ${seed}: ${p1}+${p2} wins in ${n} hands`);
    if (p1 < 0 || p2 < 0) bad.push(`seed ${seed}: negative wins`);
    const wantW = p1 > p2 ? 1 : p2 > p1 ? 2 : 0;
    if (w !== wantW) bad.push(`seed ${seed}: winner ${w} against ${p1}/${p2}`);
  }
  ok('a session plays the hands it is asked for and its winner follows its counts',
     bad.length === 0, bad.length ? bad.slice(0, 3).join('; ') : '20 sessions');
  ok('control: a session of zero hands is a tie',
     e.pk_played(e.pk_play(1, 0)) === 0 && e.pk_winner(e.pk_play(1, 0)) === 0);
}

// -- The draw -------------------------------------------------------------
{

  const bad = [];
  const draws = [
    ['made flush',  [card(A,0), card(J,0), card(8,0), card(4,0), card(2,0)], 0],
    ['quads',       [card(5,0), card(5,1), card(5,2), card(5,3), card(A,0)], 1],
    ['trips',       [card(5,0), card(5,1), card(5,2), card(A,0), card(J,1)], 2],
    ['two pair',    [card(5,0), card(5,1), card(A,0), card(A,1), card(J,1)], 1],
    ['one pair',    [card(5,0), card(5,1), card(A,0), card(J,1), card(3,2)], 2],
    ['nothing',     [card(A,0), card(J,1), card(8,2), card(4,3), card(2,0)], 3],
  ];
  for (const [name, cs, want] of draws) {
    const got = e.pk_draw(...cs);
    if (got !== want) bad.push(`${name}: draws ${got}, the strategy says ${want}`);
  }
  ok('the draw strategy keeps what it should keep',
     bad.length === 0, bad.length ? bad.join('; ') : `${draws.length} hands`);
}

// -- THE TABLE ------------------------------------------------------------
// Everything above grades the EVALUATOR, which is what the chapter had.
// From 2026-09-02 there is a table with chips on it, so these arms grade
// the game: that the money is conserved, that a card is never dealt twice,
// that the discard is a CHOICE and not a count, and that a hand ends.
{
  const seeds = [1, 7, 11, 42, 99, 123, 777, 2026];
  const BANK = 400;   // two stacks of 200
  const hand = (t, who) => seq5.map(i => e.pkt_card(t, who, i));
  const bad = [];
  let folds = 0, showdowns = 0, draws = 0, refusals = 0, tested = 0;

  for (const seed of seeds) {
    if (e.__heap_reset) e.__heap_reset();
    let t = e.pkt_new(seed);

    if (e.pkt_cur(t) !== 0) bad.push(`seed ${seed}: opens on player ${e.pkt_cur(t)}`);
    if (e.pkt_stage(t) !== 0) bad.push(`seed ${seed}: opens at stage ${e.pkt_stage(t)}`);
    if (e.pkt_pot(t) !== 10) bad.push(`seed ${seed}: antes made a pot of ${e.pkt_pot(t)}`);

    // Your five are yours; the opponent's are not, until the hand is over.
    const yours = hand(t, 0);
    if (yours.some(c => c < 0 || c > 51)) bad.push(`seed ${seed}: your hand is ${yours}`);
    if (new Set(yours).size !== 5) bad.push(`seed ${seed}: a card dealt twice in one hand`);
    if (seq5.some(i => e.pkt_card(t, 1, i) !== -1)) {
      bad.push(`seed ${seed}: the opponent's cards are readable before the showdown`);
    }

    // A move out of stage must be refused and must change nothing.
    tested++;
    if (e.pkt_candraw(t) === 1) bad.push(`seed ${seed}: drawing offered during the betting`);
    if (e.pkt_draw(t) !== t) bad.push(`seed ${seed}: a draw was taken during the betting`);
    else refusals++;
    if (e.pkt_mark(t, 0) !== t) bad.push(`seed ${seed}: a card was marked outside the draw`);
    if (e.pkt_mark(t, 9) !== t) bad.push(`seed ${seed}: a card off the hand was marked`);

    // Play the hand: you call whatever is in front of you, and at the draw
    // you throw the cards the engine's own mask picks, which is also what
    // the opponent does. The point is the invariants, not the strategy.
    let guard = 0;
    while (e.pkt_done(t) !== 1 && guard++ < 60) {
      const bank = e.pkt_chips(t, 0) + e.pkt_chips(t, 1) + e.pkt_pot(t);
      if (bank !== BANK) {
        bad.push(`seed ${seed}: ${bank} chips on the table, not ${BANK}`);
        break;
      }
      if (e.pkt_cur(t) !== 0) { t = e.pkt_step(t); continue; }
      if (e.pkt_candraw(t) === 1) {
        // Mark exactly as many as the engine says this hand should throw,
        // taking them from the top so the choice is the page's and not a
        // replay of the mask under test.
        const n = e.pkt_dcount(t, 0);
        for (let i = 0; i < n; i++) t = e.pkt_mark(t, i);
        if (seq5.filter(i => e.pkt_marked(t, i) === 1).length !== n) {
          bad.push(`seed ${seed}: marked ${n} cards and the table shows otherwise`);
        }
        const before = hand(t, 0);
        t = e.pkt_draw(t);
        draws++;
        const after = hand(t, 0);
        if (after.length !== 5) bad.push(`seed ${seed}: ${after.length} cards after the draw`);
        if (new Set(after).size !== 5) bad.push(`seed ${seed}: the draw dealt a duplicate`);
        // The cards NOT marked must still be there, in place.
        for (let i = n; i < 5; i++) {
          if (after[i] !== before[i]) {
            bad.push(`seed ${seed}: position ${i} was not marked and changed anyway`);
          }
        }
        continue;
      }
      if (e.pkt_cancall(t) === 1) { t = e.pkt_call(t); continue; }
      if (e.pkt_canraise(t) === 1) { t = e.pkt_raise(t); continue; }
      break;
    }

    if (e.pkt_done(t) !== 1) { bad.push(`seed ${seed}: the hand never ended`); continue; }
    if (e.pkt_folded(t) >= 0) folds++; else showdowns++;
    const bank = e.pkt_chips(t, 0) + e.pkt_chips(t, 1) + e.pkt_pot(t);
    if (bank !== BANK) bad.push(`seed ${seed}: ${bank} chips at the end, not ${BANK}`);
    if (e.pkt_pot(t) !== 0) bad.push(`seed ${seed}: ${e.pkt_pot(t)} left in the pot`);

    // Ten distinct cards at the showdown, which is the arm that says the
    // draw came out of the undealt part of the deck.
    if (e.pkt_folded(t) < 0) {
      const all = hand(t, 0).concat(hand(t, 1));
      if (all.some(c => c < 0)) bad.push(`seed ${seed}: a card was still hidden at the showdown`);
      else if (new Set(all).size !== 10) {
        bad.push(`seed ${seed}: ${new Set(all).size} distinct cards across the two hands`);
      }
      // The winner is the better hand under the module's own comparator.
      const r0 = e.pk_hand(...hand(t, 0)), r1 = e.pk_hand(...hand(t, 1));
      const c = e.pk_cmp(r0, r1);
      const want = c === 1 ? 0 : c === 2 ? 1 : -1;
      if (e.pkt_winner(t) !== want) {
        bad.push(`seed ${seed}: the table gave it to ${e.pkt_winner(t)}, the comparator says ${want}`);
      }
    }
  }

  ok('a hand plays out with the chips conserved', bad.length === 0,
     bad.length ? bad.slice(0, 3).join('; ') : `${seeds.length} hands`);
  ok('every hand reaches an end', folds + showdowns === seeds.length,
     `${folds} folded, ${showdowns} to a showdown`);
  // L-VACUOUS: the draw arms are silent unless a draw actually happened,
  // and the refusal arms unless a refusal was actually offered.
  ok('control: the draw was reached', draws > 0, `${draws} draws taken`);
  ok('control: an out-of-stage move was refused', refusals === tested,
     `${refusals} of ${tested} refused`);
}

// -- FOLDING --------------------------------------------------------------
// The loop above never folds, so without this the fold path and the award
// that goes with it are code nothing has run. Winning without showing a
// hand is the one outcome poker has that no other game here does.
{
  const bad = [];
  let folded = 0;
  for (const seed of [1, 7, 11, 42, 99]) {
    if (e.__heap_reset) e.__heap_reset();
    const t = e.pkt_new(seed);
    if (e.pkt_canfold(t) !== 1) { bad.push(`seed ${seed}: folding is not offered at the open`); continue; }
    const mine = e.pkt_chips(t, 0), theirs = e.pkt_chips(t, 1), pot = e.pkt_pot(t);
    const f = e.pkt_fold(t);
    folded++;
    if (e.pkt_done(f) !== 1) bad.push(`seed ${seed}: folding did not end the hand`);
    if (e.pkt_folded(f) !== 0) bad.push(`seed ${seed}: the table says ${e.pkt_folded(f)} folded`);
    if (e.pkt_winner(f) !== 1) bad.push(`seed ${seed}: the pot did not go to the other player`);
    if (e.pkt_chips(f, 0) !== mine) bad.push(`seed ${seed}: folding cost or paid you chips`);
    if (e.pkt_chips(f, 1) !== theirs + pot) {
      bad.push(`seed ${seed}: they hold ${e.pkt_chips(f, 1)}, expected ${theirs + pot}`);
    }
    if (e.pkt_chips(f, 0) + e.pkt_chips(f, 1) !== 400) {
      bad.push(`seed ${seed}: ${e.pkt_chips(f, 0) + e.pkt_chips(f, 1)} chips after the fold`);
    }
    // A folded hand is over, so nothing further may be taken from it.
    if (e.pkt_call(f) !== f || e.pkt_raise(f) !== f || e.pkt_fold(f) !== f) {
      bad.push(`seed ${seed}: the hand accepted a move after it was over`);
    }
    // The handle the caller still holds must be the position it was.
    if (e.pkt_done(t) === 1) bad.push(`seed ${seed}: folding wrote through the caller's handle`);
  }
  ok('folding ends the hand and the pot goes to the other player',
     bad.length === 0, bad.length ? bad.slice(0, 3).join('; ') : '5 hands');
  ok('control: the fold path was reached', folded === 5, `${folded} folds taken`);
}

// -- THE DISCARD IS A CHOICE, NOT A COUNT ---------------------------------
// `draw-count` says HOW MANY to throw and the mask says WHICH, so the two
// must agree on the number, and the mask must never throw a card that is
// carrying the hand. Built by hand rather than dealt, because a dealt
// corpus is mostly high-card hands and would never reach the branches.
{
  const bad = [];
  const cases = [
    // [name, five cards, what must survive the discard]
    // One pair keeps the pair AND its best kicker, so draw-count says two
    // and not three, and the mask has to say the same number. The pair
    // sits at the BACK here on purpose: a mask that throws the last N
    // looks correct on every hand whose pair is at the front.
    ['a pair of aces at the back', [card(2,0), card(5,1), card(9,2), card(A,0), card(A,1)], [A, A, J]],
    ['a pair of deuces at the front', [card(0,0), card(0,1), card(6,2), card(J,0), card(K,1)], [0, 0, K]],
    ['two pair split apart', [card(A,0), card(3,1), card(A,2), card(3,3), card(7,0)], [A, A, 3, 3]],
    ['trips at the back', [card(2,0), card(5,1), card(8,2), card(8,0), card(8,1)], [8, 8, 8]],
    ['quads at the front', [card(Q,0), card(Q,1), card(Q,2), card(Q,3), card(2,0)], [Q, Q, Q, Q]],
    ['a flush', [card(2,1), card(5,1), card(9,1), card(J,1), card(K,1)], null],
    ['a straight', [card(4,0), card(5,1), card(6,2), card(7,3), card(8,0)], null],
    ['the wheel', [card(A,0), card(0,1), card(1,2), card(2,3), card(3,0)], null],
    ['nothing at all', [card(2,0), card(4,1), card(7,2), card(J,3), card(K,0)], [J, K]],
  ];
  for (const [name, cs, survives] of cases) {
    const n = e.pk_draw(...cs);
    if (survives === null) {
      if (n !== 0) bad.push(`${name}: throws ${n} away`);
      continue;
    }
    if (n !== 5 - survives.length) {
      bad.push(`${name}: throws ${n}, should throw ${5 - survives.length}`);
    }
  }
  ok('draw-count keeps what the hand is built on', bad.length === 0,
     bad.length ? bad.join('; ') : `${cases.length} hands`);
}

// -- THE COUNT AND THE CHOICE MUST BE THE SAME NUMBER ---------------------
// `draw-count` says how many and the mask says which, and they are two
// functions that can drift apart in silence: the table would still deal a
// legal hand, the chips would still balance, and the strategy the chapter
// documents would simply not be the one being played. This arm is what
// caught the mask throwing three cards on a pair while the count said two.
{
  const disagree = [];
  const seenCounts = new Map();
  for (let seed = 1; seed <= 300; seed++) {
    if (e.__heap_reset) e.__heap_reset();
    const t = e.pkt_new(seed);
    const cards = seq5.map(i => e.pkt_card(t, 0, i));
    const byCount = e.pk_draw(...cards);
    const byMask = e.pkt_dcount(t, 0);
    seenCounts.set(byCount, (seenCounts.get(byCount) || 0) + 1);
    if (byCount !== byMask) {
      disagree.push(`${show(cards)}: count ${byCount}, mask ${byMask}`);
    }
  }
  ok('the discard mask throws exactly as many as draw-count says',
     disagree.length === 0,
     disagree.length ? `${disagree.length} of 300; ${disagree.slice(0, 2).join('; ')}`
                     : '300 hands');
  // L-VACUOUS again: agreement over 300 hands that all wanted the same
  // number would test one branch and read as testing them all.
  ok('control: the deal reached more than one discard size', seenCounts.size >= 3,
     [...seenCounts.entries()].sort((a, b) => a[0] - b[0])
       .map(([k, v]) => `${k}:${v}`).join(' '));
}

console.log(fail === 0
  ? `\nPASS: Poker classifies hands by the rules (${pass} arms).`
  : `\nFAIL: ${fail} of ${pass + fail} arms.`);
process.exitCode = fail === 0 ? 0 : 1;
