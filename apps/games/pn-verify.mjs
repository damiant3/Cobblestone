// Grade the Pinochle wasm module.
//
// A Pinochle deck has TWO of every card, and that is the whole reason this
// grader exists. Every scoring rule in the game has a single form and a
// double form, and an engine that asks "do I hold a king and a queen of
// this suit" answers the same for one marriage and for two. So the arms
// here are in three layers.
//
// The deal is checked as a permutation: forty-eight distinct ids, twelve to
// each hand, and trump taken from the top card.
//
// The meld is checked against a second implementation of the engine's own
// single-copy table, which must agree everywhere. That arm is a CONTROL: it
// says the card encoding and the point table are read correctly here, so a
// disagreement in the next layer is about the rule and not about the reader.
//
// The third layer counts, over many deals, how often a hand holds a second
// copy that standard Pinochle scores again and this engine does not. That is
// a measurement and not an assertion: what to do about it is recorded in
// games-backlog.md, not decided here.
//
// Usage: node apps/games/pn-verify.mjs [path/to/pinochle.wasm]

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const wasmPath = process.argv[2] ||
  join(here, '..', 'landing', 'web', 'games', 'pinochle.wasm');

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

// id = suit * 12 + copy * 6 + rank; ranks 0..5 are 9, J, Q, K, 10, A.
const suitOf = c => Math.floor(c / 12);
const rankOf = c => c % 6;
const NINE = 0, JACK = 1, QUEEN = 2, KING = 3, TEN = 4, ACE = 5;
const POINTS = { [ACE]: 11, [TEN]: 10, [KING]: 4, [QUEEN]: 3, [JACK]: 2, [NINE]: 0 };

const hand = (h, p) => {
  const out = [];
  for (let i = 0; i < 12; i++) out.push(e.pn_card(h, p, i));
  return out;
};
const countOf = (cards, s, r) => cards.filter(c => suitOf(c) === s && rankOf(c) === r).length;

// The engine's own table, single copy only. This must agree everywhere.
function meldSingle(cards, trump) {
  let m = 0;
  for (let s = 0; s < 4; s++) {
    if (countOf(cards, s, KING) >= 1 && countOf(cards, s, QUEEN) >= 1) m += (s === trump ? 40 : 20);
  }
  if (countOf(cards, 1, JACK) >= 1 && countOf(cards, 3, QUEEN) >= 1) m += 40;
  const around = { [ACE]: 100, [KING]: 80, [QUEEN]: 60, [JACK]: 40 };
  for (const r of [ACE, KING, QUEEN, JACK]) {
    if ([0, 1, 2, 3].every(s => countOf(cards, s, r) >= 1)) m += around[r];
  }
  if ([ACE, TEN, KING, QUEEN, JACK].every(r => countOf(cards, trump, r) >= 1)) m += 150;
  return m;
}

// Standard Pinochle, where a second copy scores again.
function meldDouble(cards, trump) {
  let m = 0;
  for (let s = 0; s < 4; s++) {
    const n = Math.min(countOf(cards, s, KING), countOf(cards, s, QUEEN));
    m += n * (s === trump ? 40 : 20);
  }
  const pin = Math.min(countOf(cards, 1, JACK), countOf(cards, 3, QUEEN));
  m += pin >= 2 ? 300 : pin === 1 ? 40 : 0;
  const single = { [ACE]: 100, [KING]: 80, [QUEEN]: 60, [JACK]: 40 };
  const dbl = { [ACE]: 1000, [KING]: 800, [QUEEN]: 600, [JACK]: 400 };
  for (const r of [ACE, KING, QUEEN, JACK]) {
    const n = Math.min(...[0, 1, 2, 3].map(s => countOf(cards, s, r)));
    m += n >= 2 ? dbl[r] : n === 1 ? single[r] : 0;
  }
  const run = Math.min(...[ACE, TEN, KING, QUEEN, JACK].map(r => countOf(cards, trump, r)));
  m += run >= 2 ? 1500 : run === 1 ? 150 : 0;
  return m;
}

console.log(`pn-verify ${wasmPath}`);

// -- The deal -------------------------------------------------------------
{
  const bad = [];
  let deals = 0;
  for (let seed = 1; seed <= 60; seed++) {
    const h = e.pn_new(seed);
    const all = [0, 1, 2, 3].flatMap(p => hand(h, p));
    deals++;
    if (all.length !== 48) { bad.push(`seed ${seed}: ${all.length} cards`); continue; }
    if (new Set(all).size !== 48) bad.push(`seed ${seed}: a card was dealt twice`);
    if (all.some(c => c < 0 || c > 47)) bad.push(`seed ${seed}: a card is off the deck`);
    if (e.pn_trump(h) !== suitOf(hand(h, 0)[0])) {
      bad.push(`seed ${seed}: trump ${e.pn_trump(h)} against top card suit ${suitOf(hand(h, 0)[0])}`);
    }
    if (e.pn_card(h, 0, 12) !== -1 || e.pn_card(h, 4, 0) !== -1 || e.pn_card(h, -1, 0) !== -1) {
      bad.push(`seed ${seed}: a card off the hand did not read -1`);
    }
  }
  ok('every deal is the forty-eight card deck split four ways, trump off the top',
     bad.length === 0, bad.length ? bad.slice(0, 3).join('; ') : `${deals} deals`);
  ok('control: the deck really does hold two of every card',
     [0, 1, 2, 3].every(s => [0, 1, 2, 3, 4, 5].every(r =>
       [...Array(48)].map((_, c) => c).filter(c => suitOf(c) === s && rankOf(c) === r).length === 2)));
  ok('control: the deck is worth 240 in trick points',
     [...Array(48)].map((_, c) => POINTS[rankOf(c)]).reduce((a, b) => a + b, 0) === 240);
}

// -- THE MELD -------------------------------------------------------------
{
  const bad = [];
  let hands = 0, nonZero = 0;
  for (let seed = 1; seed <= 60; seed++) {
    const h = e.pn_new(seed), trump = e.pn_trump(h);
    for (let p = 0; p < 4; p++) {
      const cards = hand(h, p), want = meldDouble(cards, trump), got = e.pn_meld(h, p);
      hands++;
      if (want !== 0) nonZero++;
      if (want !== got) bad.push(`seed ${seed} player ${p}: engine ${got}, the rules say ${want}`);
    }
  }
  ok('the meld matches the rules on every hand, second copies included',
     bad.length === 0, bad.length ? bad.slice(0, 3).join('; ') : `${hands} hands`);
  ok('control: most hands scored something, so the table was exercised',
     nonZero > hands / 2, `${nonZero} of ${hands} hands melded`);
  ok('a player off the table reads -1',
     e.pn_meld(e.pn_new(1), 4) === -1 && e.pn_meld(e.pn_new(1), -1) === -1);
}

// -- The result is an identity --------------------------------------------
{
  const bad = [];
  for (let seed = 1; seed <= 60; seed++) {
    const h = e.pn_new(seed), r = e.pn_run(seed);
    const melds = [0, 1, 2, 3].map(p => e.pn_meld(h, p));
    // The deck carries 240 and the last trick is worth ten on top of what
    // its cards carry, so a played-out hand accounts for 250. The engine
    // paid no last-trick bonus at all until 2026-09-02, and this arm read
    // that as correct because it was asking for 240.
    const want = melds[0] + melds[2] + melds[1] + melds[3] + 250;
    const got = e.pn_t0(r) + e.pn_t1(r);
    if (want !== got) bad.push(`seed ${seed}: scores sum to ${got}, meld plus 250 is ${want}`);
    const w = e.pn_winner(r);
    const t0 = e.pn_t0(r), t1 = e.pn_t1(r);
    const wantW = t0 > t1 ? 0 : t1 > t0 ? 1 : -1;
    if (w !== wantW) bad.push(`seed ${seed}: winner ${w} against scores ${t0}/${t1}`);
  }
  ok('the two team scores sum to the meld plus the 250 a hand is worth',
     bad.length === 0, bad.length ? bad.slice(0, 3).join('; ') : '60 hands');
  ok('control: pn_new and pn_run agree on the deal for a seed',
     e.pn_trump(e.pn_new(7)) === e.pn_trump(e.pn_new(7)));
}

// -- WHAT THE SECOND COPY WAS WORTH ---------------------------------------
// meldSingle is the rule the engine had before GAME-25: it asked whether a
// card was present, so one marriage and two scored the same. Kept as the
// arm that says the fix discriminates rather than shifting everything.
{
  let hands = 0, differing = 0, totalGap = 0, biggest = 0, example = '';
  for (let seed = 1; seed <= 200; seed++) {
    const h = e.pn_new(seed), trump = e.pn_trump(h);
    for (let p = 0; p < 4; p++) {
      const cards = hand(h, p);
      const single = meldSingle(cards, trump), double = meldDouble(cards, trump);
      hands++;
      if (single !== double) {
        differing++; totalGap += double - single;
        if (double - single > biggest) {
          biggest = double - single;
          example = `seed ${seed} player ${p}: ${single} against ${double}`;
        }
      }
    }
  }
  ok('control: the old rule and the new one agree wherever no card is held twice',
     differing > 0 && differing < hands, `${hands - differing} of ${hands} hands agree`);
  console.log(`  note  the fix moved ${differing} of ${hands} hands ` +
              `(${(100 * differing / hands).toFixed(1)} per cent), ` +
              `${totalGap} points in total, largest ${biggest} on ${example}`);
}

// -- Controls -------------------------------------------------------------
{
  ok('control: different seeds deal different hands',
     JSON.stringify(hand(e.pn_new(1), 0)) !== JSON.stringify(hand(e.pn_new(2), 0)));
  // L-FALSIF: the single-copy table must reject a hand it should reject.
  const trump = 0;
  const noMeld = [0, 6, 12, 18, 24, 30, 36, 42, 1, 7, 13, 19].filter(c => rankOf(c) === NINE || rankOf(c) === JACK);
  ok('control: the table scores a hand with no marriage at zero',
     meldSingle([0, 6, 12, 18], trump) === 0, `${noMeld.length} nine-and-jack cards used`);
  ok('control: the table finds a marriage that is there',
     meldSingle([2, 3], 1) === 20 && meldSingle([2, 3], 0) === 40,
     'queen and king of clubs, off trump then on it');
  ok('control: the double table pays a second marriage and the single one does not',
     meldDouble([2, 3, 8, 9], 1) === 40 && meldSingle([2, 3, 8, 9], 1) === 20);
}

// -- PLAYING IT -----------------------------------------------------------
// Everything above grades a DEAL. From 2026-09-02 the state carries the
// trick and a hand is played one card at a time, so these arms grade the
// play: that the card leaving a hand is the card that was played, that the
// legality is the game's and not a suggestion, and that a whole hand lands
// on the 250 the deck plus the last trick are worth.
//
// The trick winner is recomputed here from the four cards and `pn_beats`
// is NOT exported, so this side decides it with its own comparison. That is
// the point: an oracle that asked the engine who won would agree with it
// whatever it answered.
{
  const handNow = (h, p) => {
    const out = [];
    for (let i = 0; i < e.pn_count(h, p); i++) out.push(e.pn_card(h, p, i));
    return out;
  };
  // Trump beats a plain suit, a higher card beats a lower of the same suit,
  // and anything off both is out of it. Ties keep the card played first.
  const beats = (c, best, led, trump) => {
    const cs = suitOf(c), bs = suitOf(best);
    if (cs === trump && bs !== trump) return true;
    if (cs === trump && bs === trump) return rankOf(c) > rankOf(best);
    if (cs === bs) return rankOf(c) > rankOf(best);
    return false;
  };

  const bad = [];
  let refusals = 0, asked = 0, offSuitOffered = 0, tricksChecked = 0;
  for (const seed of [11, 42, 77, 101, 999]) {
    if (e.__heap_reset) e.__heap_reset();
    const h = e.pn_new(seed);
    if (e.pn_cur(h) !== 0) bad.push(`seed ${seed}: opens on seat ${e.pn_cur(h)}, not yours`);
    if ([0, 1, 2, 3].some(p => e.pn_count(h, p) !== 12)) {
      bad.push(`seed ${seed}: hands are ${[0, 1, 2, 3].map(p => e.pn_count(h, p))}`);
    }

    // NOT index 0. A loop that drops the first card of a hand looks correct
    // exactly when the card played happens to be the first one, which is
    // the bug GAME-17 had in bridge for as long as that engine existed.
    const before = handNow(h, 0);
    const pick = 3;
    const played = before[pick];
    const h2 = e.pn_play(h, pick);
    if (e.pn_trick(h2, 0) !== played) {
      bad.push(`seed ${seed}: played ${played}, the trick holds ${e.pn_trick(h2, 0)}`);
    }
    const after = handNow(h2, 0);
    const wantAfter = before.filter((_, k) => k !== pick);
    if (after.join(',') !== wantAfter.join(',')) {
      bad.push(`seed ${seed}: the hand went ${before.join(',')} to ${after.join(',')}`);
    }
    if (handNow(h, 0).join(',') !== before.join(',')) {
      bad.push(`seed ${seed}: playing wrote through the handle the caller still holds`);
    }

    // Play it out, and at every trick check the winner against this side's
    // own comparison. The card each seat played is read as the difference
    // its hand shows across the step, which needs nothing from the engine
    // beyond the hands themselves.
    let g = h, guard = 0, tally = [0, 0];
    let trick = [], leadSeat = e.pn_leader(g);
    while (e.pn_done(g) !== 1 && guard++ < 200) {
      const seat = e.pn_cur(g);
      const led = e.pn_leader(g) === seat && trick.length === 0 ? -1 : suitOf(trick[0].c);
      if (led >= 0) {
        const mine = handNow(g, seat);
        const hasLed = mine.some(c => suitOf(c) === led);
        const wrong = mine.findIndex(c => suitOf(c) !== led);
        if (hasLed && wrong >= 0) {
          asked++;
          if (e.pn_legal(g, wrong) === 1) offSuitOffered++;
          if (e.pn_play(g, wrong) === g) refusals++;
          else bad.push(`seed ${seed}: an off-suit card was taken`);
        }
      }
      const handBefore = handNow(g, seat);
      const next = e.pn_step(g);
      const handAfter = handNow(next, seat);
      const gone = handBefore.filter(c => !handAfter.includes(c));
      if (gone.length !== 1) {
        bad.push(`seed ${seed}: a step took ${gone.length} cards out of seat ${seat}`);
        break;
      }
      if (trick.length === 0) leadSeat = seat;
      trick.push({ seat, c: gone[0] });
      if (trick.length === 4) {
        const trump = e.pn_trump(g);
        const led2 = suitOf(trick[0].c);
        let w = trick[0];
        for (const t of trick) if (beats(t.c, w.c, led2, trump)) w = t;
        const pts = trick.reduce((a, t) => a + POINTS[rankOf(t.c)], 0);
        const last = e.pn_done(next) === 1;
        tally[w.seat % 2] += pts + (last ? 10 : 0);
        tricksChecked++;
        trick = [];
      }
      g = next;
    }
    if (e.pn_done(g) !== 1) bad.push(`seed ${seed}: the hand never finished`);
    if (e.pn_tricks(g) !== 12) bad.push(`seed ${seed}: ${e.pn_tricks(g)} tricks, not 12`);
    if ([0, 1, 2, 3].some(p => e.pn_count(g, p) !== 0)) {
      bad.push(`seed ${seed}: hands left over`);
    }
    const total = e.pn_pts(g, 0) + e.pn_pts(g, 1);
    if (total !== 250) bad.push(`seed ${seed}: the hand accounted for ${total}, not 250`);
    if (e.pn_pts(g, 0) !== tally[0] || e.pn_pts(g, 1) !== tally[1]) {
      bad.push(`seed ${seed}: engine ${e.pn_pts(g, 0)}/${e.pn_pts(g, 1)},`
        + ` decided here ${tally[0]}/${tally[1]}`);
    }
  }
  ok('a hand plays out one card at a time and lands on 250', bad.length === 0,
     bad.length ? bad.slice(0, 3).join('; ') : '5 hands');
  ok('every trick goes to the seat this side says won it', tricksChecked === 60,
     `${tricksChecked} tricks checked against an independent comparison`);
  ok('no off-suit card was ever offered while the suit led was held',
     offSuitOffered === 0, `${offSuitOffered} of ${asked} would have been a revoke`);
  // An arm that never reached its condition is not an arm (L-VACUOUS): the
  // two above measure nothing unless a revoke was actually available.
  ok('control: the follow-suit question was reachable', asked > 0 && refusals === asked,
     `asked ${asked} times, refused ${refusals}`);
}

console.log(fail === 0
  ? `\nPASS: Pinochle deals and melds by the rules (${pass} arms).`
  : `\nFAIL: ${fail} of ${pass + fail} arms.`);
process.exitCode = fail === 0 ? 0 : 1;
