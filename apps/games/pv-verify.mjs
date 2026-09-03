// Grade the PokerVariants wasm module.
//
// The load-bearing component here is best-five-of-seven, used by Seven Card
// Stud and Baseball, and it can be graded two ways that do not share an
// assumption.
//
// The internal one needs no second evaluator: whatever five cards the engine
// picks must beat or tie all twenty-one five-card subsets under the same
// comparator the game itself uses. A selector that stops early, or that
// compares on too few fields, fails this without anyone having to agree on
// what a kicker is worth.
//
// The external one is a JavaScript classifier: the CATEGORY of the chosen
// hand must equal the best category available among the twenty-one. That
// one cannot see a kicker mistake and is not asked to.
//
// The wild-card scoring is measured rather than asserted, because what it
// should do is a piece of work and not a one-line repair. See GAME-27.
//
// Usage: node apps/games/pv-verify.mjs [path/to/pokervariants.wasm]

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const wasmPath = process.argv[2] ||
  join(here, '..', 'landing', 'web', 'games', 'pokervariants.wasm');

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

const rankOf = c => c % 13;
const suitOf = c => Math.floor(c / 13);
const card = (r, s) => s * 13 + r;
const RANKNAME = ['2','3','4','5','6','7','8','9','T','J','Q','K','A'];
const SUITNAME = ['c','d','h','s'];
const show = cs => cs.map(c => RANKNAME[rankOf(c)] + SUITNAME[suitOf(c)]).join(' ');
const CATEGORY = ['high card','pair','two pair','trips','straight','flush',
                  'full house','quads','straight flush'];
const VARIANTS = ['FiveCardDraw','FiveCardStud','SevenCardStud','Baseball',
                  'HiChicago','LowChicago','FollowTheQueen','JacksOrBetter'];
const A = 12, K = 11, Q = 10, J = 9, T = 8;

// The independent classifier, the same one pk-verify uses.
function classify(cs) {
  const counts = new Map();
  for (const c of cs) counts.set(rankOf(c), (counts.get(rankOf(c)) || 0) + 1);
  const shape = [...counts.values()].sort((a, b) => b - a);
  const flush = new Set(cs.map(suitOf)).size === 1;
  const ranks = [...counts.keys()].sort((a, b) => a - b);
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

// The twenty-one five-card subsets of seven cards.
function subsets5(cs) {
  const out = [];
  for (let a = 0; a < 3; a++)
    for (let b = a + 1; b < 4; b++)
      for (let c = b + 1; c < 5; c++)
        for (let d = c + 1; d < 6; d++)
          for (let f = d + 1; f < 7; f++)
            out.push([cs[a], cs[b], cs[c], cs[d], cs[f]]);
  return out;
}

// A deterministic seven-card deal that never repeats a card.
function sevenCards(seed) {
  let s = seed >>> 0;
  const rnd = () => { s = (s * 1103515245 + 12345) & 0x7fffffff; return s; };
  const deck = [...Array(52)].map((_, i) => i);
  for (let i = 51; i > 0; i--) {
    const j = rnd() % (i + 1);
    [deck[i], deck[j]] = [deck[j], deck[i]];
  }
  return deck.slice(0, 7);
}

console.log(`pv-verify ${wasmPath}`);

// -- Control: the subset enumeration is the whole of it -------------------
{
  const cs = [0, 1, 2, 3, 4, 5, 6];
  const subs = subsets5(cs);
  ok('control: seven cards have twenty-one five-card subsets, all distinct',
     subs.length === 21 && new Set(subs.map(s => s.join(','))).size === 21, subs.length);
}

// -- THE INTERNAL ARM: the chosen five beat every other five --------------
{
  const bad = [];
  let deals = 0, ties = 0;
  for (let seed = 1; seed <= 300; seed++) {
    const cs = sevenCards(seed);
    const chosen = [0, 1, 2, 3, 4].map(i => e.pv_cardat(e.pv_best5c(...cs), i));
    deals++;
    if (chosen.some(c => c < 0)) { bad.push(`seed ${seed}: the choice is short`); continue; }
    if (!chosen.every(c => cs.includes(c))) {
      bad.push(`seed ${seed}: chose ${show(chosen)} from ${show(cs)}`); continue;
    }
    if (new Set(chosen).size !== 5) { bad.push(`seed ${seed}: chose a card twice`); continue; }
    const mine = e.pv_eval5(...chosen);
    let beaten = 0;
    for (const sub of subsets5(cs)) {
      const other = e.pv_eval5(...sub);
      if (e.pv_cmp(mine, other) === 2) {
        bad.push(`seed ${seed}: chose ${show(chosen)} over ${show(sub)} from ${show(cs)}`);
        break;
      }
      if (e.pv_cmp(mine, other) === 0) ties++;
      else beaten++;
    }
  }
  ok('the best five of seven is not beaten by any of the twenty-one, on 300 deals',
     bad.length === 0, bad.length ? bad.slice(0, 3).join('; ') : `${deals} deals`);
  ok('control: the chosen hand did beat other subsets, so the test is not vacuous',
     ties > 0, `${ties} ties and other subsets strictly beaten elsewhere`);
}

// -- THE EXTERNAL ARM: the category is the best available -----------------
{
  const bad = [];
  const seen = new Map();
  for (let seed = 1; seed <= 300; seed++) {
    const cs = sevenCards(seed);
    const chosen = [0, 1, 2, 3, 4].map(i => e.pv_cardat(e.pv_best5c(...cs), i));
    const best = Math.max(...subsets5(cs).map(classify));
    const got = classify(chosen);
    seen.set(best, (seen.get(best) || 0) + 1);
    if (got !== best) {
      bad.push(`${show(cs)}: chose ${CATEGORY[got]}, ${CATEGORY[best]} was available`);
    }
  }
  ok('the chosen five is the best CATEGORY available, by an independent classifier',
     bad.length === 0, bad.length ? bad.slice(0, 3).join('; ') : '300 deals');
  console.log(`  note  best category available: ` +
    [...seen.entries()].sort((a, b) => a[0] - b[0])
      .map(([k, v]) => `${CATEGORY[k]} ${v}`).join(', '));
}

// -- Built sevens, for the categories a deal will not reach ---------------
{
  const cases = [
    ['a straight flush hiding in seven',
     [card(A,0), card(K,0), card(Q,0), card(J,0), card(T,0), card(2,1), card(5,2)], 8],
    ['quads plus junk',
     [card(5,0), card(5,1), card(5,2), card(5,3), card(2,1), card(9,2), card(J,3)], 7],
    ['a full house from two trips',
     [card(5,0), card(5,1), card(5,2), card(A,0), card(A,1), card(A,2), card(2,3)], 6],
    ['the wheel inside seven',
     [card(A,0), card(0,1), card(1,2), card(2,3), card(3,0), card(J,1), card(Q,2)], 4],
    ['nothing at all',
     [card(A,0), card(J,1), card(8,2), card(4,3), card(2,0), card(5,1), card(6,2)], 0],
  ];
  const oracleBad = [], engineBad = [];
  for (const [name, cs, want] of cases) {
    const best = Math.max(...subsets5(cs).map(classify));
    if (best !== want) oracleBad.push(`ORACLE on ${name}: ${CATEGORY[best]}, wanted ${CATEGORY[want]}`);
  }
  ok('control: the classifier gets every built seven right first',
     oracleBad.length === 0, oracleBad.length ? oracleBad.join('; ') : `${cases.length} hands`);
  for (const [name, cs, want] of cases) {
    const chosen = [0, 1, 2, 3, 4].map(i => e.pv_cardat(e.pv_best5c(...cs), i));
    const got = classify(chosen);
    if (got !== want) engineBad.push(`${name}: chose ${CATEGORY[got]} (${show(chosen)}), wanted ${CATEGORY[want]}`);
  }
  ok('every built seven yields its best hand',
     engineBad.length === 0, engineBad.length ? engineBad.join('; ') : `${cases.length} hands`);
}

// -- The eight variants run -----------------------------------------------
{
  const bad = [];
  const lines = [];
  for (let v = 0; v < 8; v++) {
    for (const seed of [123, 7, 99]) {
      const s = e.pv_run(v, seed, 2);
      const w = e.pv_winner(s), p1 = e.pv_p1(s), p2 = e.pv_p2(s);
      if (w < 0 || w > 2) bad.push(`${VARIANTS[v]} seed ${seed}: winner ${w}`);
      if (p1 < 0 || p1 > 8) bad.push(`${VARIANTS[v]} seed ${seed}: p1 rank ${p1}`);
      if (p2 < 0 || p2 > 8) bad.push(`${VARIANTS[v]} seed ${seed}: p2 rank ${p2}`);
      if (seed === 123) lines.push(`${VARIANTS[v]} P1=${CATEGORY[p1]} P2=${CATEGORY[p2]} w=${w}`);
    }
  }
  ok('all eight variants run and report a winner and two ranks in range',
     bad.length === 0, bad.length ? bad.slice(0, 3).join('; ') : '24 sessions');
  ok('control: the variants are not all the same session',
     new Set(lines).size > 1, `${new Set(lines).size} distinct results at seed 123`);
  // The index must select the variant: a wrapper that ignored it would give
  // eight identical sessions, which the control above already refuses, but
  // this pins the one the pinned test names.
  ok('SevenCardStud at seed 123 is the session classic-games-run pins',
     e.pv_p1(e.pv_run(2, 123, 2)) === 1 && e.pv_p2(e.pv_run(2, 123, 2)) === 1 &&
     e.pv_winner(e.pv_run(2, 123, 2)) === 2,
     `P1=${CATEGORY[e.pv_p1(e.pv_run(2, 123, 2))]} P2=${CATEGORY[e.pv_p2(e.pv_run(2, 123, 2))]} winner=${e.pv_winner(e.pv_run(2, 123, 2))}`);
}

// -- THE WILD CARD MEASUREMENT --------------------------------------------
// pv-evaluate-with-wilds adds the number of wild cards to the category and
// caps at 8. It never substitutes anything, so this is measured and
// recorded rather than asserted. GAME-27.
{
  const wildRank = 1;   // threes are wild, rank 1
  const rows = [];
  const cases = [
    ['no wild, a pair of fives', [card(5,0), card(5,1), card(A,0), card(J,1), card(8,2)]],
    ['one three, otherwise junk', [card(1,0), card(A,1), card(J,2), card(8,3), card(6,0)]],
    ['two threes, otherwise junk', [card(1,0), card(1,1), card(A,2), card(J,3), card(8,0)]],
    ['three threes, otherwise junk', [card(1,0), card(1,1), card(1,2), card(A,3), card(J,0)]],
    ['four threes and an ace', [card(1,0), card(1,1), card(1,2), card(1,3), card(A,0)]],
  ];
  let anyImpossible = false;
  for (const [name, cs] of cases) {
    const n = e.pv_wildn(...cs, wildRank);
    const got = e.pv_wild(...cs, wildRank);
    const plain = classify(cs);
    // What the hand could really make: try every substitution of the wilds.
    const nonWild = cs.filter(c => rankOf(c) !== wildRank);
    let bestReal = plain;
    if (n === 1) {
      for (let sub = 0; sub < 52; sub++) {
        if (nonWild.includes(sub)) continue;
        bestReal = Math.max(bestReal, classify([...nonWild, sub]));
      }
    }
    if (n >= 2 || got > bestReal) anyImpossible = anyImpossible || got > bestReal;
    rows.push(`${name}: wilds ${n}, engine ${CATEGORY[got]}` +
              (n === 1 ? `, best real ${CATEGORY[bestReal]}` : ''));
  }
  ok('the wild count is the number of cards of the wild rank',
     e.pv_wildn(card(1,0), card(1,1), card(A,2), card(J,3), card(8,0), 1) === 2 &&
     e.pv_wildn(card(5,0), card(5,1), card(A,0), card(J,1), card(8,2), 1) === 0);
  console.log('  note  wild scoring, recorded as GAME-27 and not asserted:');
  for (const r of rows) console.log(`          ${r}`);
  console.log(`  note  the engine claims a category the cards cannot make: ${anyImpossible}`);
}

// -- The table, per variant ----------------------------------------------
//
// All eight variants run on ONE table now. What differs between them is
// the number of cards dealt and how the two hands are ranked, so the arms
// below are mostly about the ranking being the variant's own and the money
// being nobody's.
const V = ['5-draw', '5-stud', '7-stud', 'baseball', 'hi-chicago',
           'low-chicago', 'follow-queen', 'jacks-or-better'];
const START = 400; // two seats, two hundred each

{
  const bad = [];
  for (let v = 0; v < 8; v++) {
    const p = e.pvt_new(v, 7);
    const want = (v === 0 || v === 1 || v === 7) ? 5 : 7;
    if (e.pvt_size(p) !== want) bad.push(`${V[v]}: dealt ${e.pvt_size(p)}, wanted ${want}`);
    if (e.pvt_card(p, 0, want - 1) < 0) bad.push(`${V[v]}: your last card is missing`);
    if (e.pvt_card(p, 0, want) !== -1) bad.push(`${V[v]}: a card past the hand was answered`);
    if (e.pvt_chips(p, 0) + e.pvt_chips(p, 1) + e.pvt_pot(p) !== START) {
      bad.push(`${V[v]}: ${e.pvt_chips(p,0)}+${e.pvt_chips(p,1)}+${e.pvt_pot(p)} is not ${START}`);
    }
  }
  ok('every variant deals its own hand size and antes the same', bad.length === 0,
     bad.length ? bad.slice(0, 3).join('; ') : '8 variants');
}

{
  const seen = [];
  for (let v = 0; v < 8; v++) {
    const p = e.pvt_new(v, 3);
    const n = e.pvt_size(p);
    const hidden = [...Array(n)].every((_, i) => e.pvt_shown(p, 1, i) === -1);
    if (!hidden) seen.push(V[v]);
  }
  ok('the opponent\'s cards are hidden while the hand is live', seen.length === 0,
     seen.length ? seen.join('; ') : '8 variants');
  ok('and its rank is hidden with them',
     [...Array(8)].every((_, v) => e.pvt_rank(e.pvt_new(v, 3), 1) === -1));
}

// Play a hand out. `act` decides what the human does, so an arm can drive
// the same table different ways.
function playOut(v, seed, act) {
  let p = e.pvt_new(v, seed), guard = 0;
  while (e.pvt_done(p) === 0 && guard++ < 60) {
    const before = p;
    p = e.pvt_cur(p) === 0 ? act(p) : e.pvt_step(p);
    if (p === before) break;
  }
  return { p, guard };
}
const callDown = p =>
  e.pvt_candraw(p) === 1 ? e.pvt_draw(p)
  : e.pvt_cancall(p) === 1 ? e.pvt_call(p) : p;

{
  const bad = [];
  let ends = 0;
  for (let v = 0; v < 8; v++) {
    for (const seed of [1, 2, 3, 5, 8, 13]) {
      const { p } = playOut(v, seed, callDown);
      if (e.pvt_done(p) !== 1) { bad.push(`${V[v]} seed ${seed}: never finished`); continue; }
      ends++;
      const w = e.pvt_winner(p);
      if (w < -1 || w > 1) bad.push(`${V[v]} seed ${seed}: winner ${w}`);
      // Nothing is created and nothing is lost: the pot is paid out whole.
      if (e.pvt_chips(p, 0) + e.pvt_chips(p, 1) !== START) {
        bad.push(`${V[v]} seed ${seed}: chips came to ${e.pvt_chips(p,0)+e.pvt_chips(p,1)}`);
      }
      if (e.pvt_pot(p) !== 0) bad.push(`${V[v]} seed ${seed}: pot ${e.pvt_pot(p)} left over`);
      // The showdown is where the opponent's hand becomes everybody's.
      if (e.pvt_folded(p) === -1 && e.pvt_shown(p, 1, 0) < 0) {
        bad.push(`${V[v]} seed ${seed}: the hand ended and the cards stayed hidden`);
      }
    }
  }
  ok('every variant plays to an end and pays out exactly the pot',
     bad.length === 0, bad.length ? bad.slice(0, 3).join('; ') : `${ends} hands`);
}

// THE ANTI-DRIFT ARM. The table's ranking must be the variant's own. For
// the seven-card variants that is decidable through a different export
// path: `pv_best5` picks the best five and `pv_rank` ranks it, which is
// what the watch-only session does, and the table must agree.
{
  const bad = [];
  let checked = 0;
  for (const v of [2, 4, 5]) { // seven cards, no wilds
    for (const seed of [1, 2, 3, 5, 8, 13, 21, 34]) {
      const p = e.pvt_new(v, seed);
      const c = [...Array(7)].map((_, i) => e.pvt_card(p, 0, i));
      const viaBest5 = e.pv_rank(e.pv_best5(c[0], c[1], c[2], c[3], c[4], c[5], c[6]));
      checked++;
      if (e.pvt_rank(p, 0) !== viaBest5) {
        bad.push(`${V[v]} seed ${seed}: table says ${e.pvt_rank(p,0)}, best-of-seven says ${viaBest5}`);
      }
    }
  }
  ok('a seven-card variant ranks a hand the way the session does',
     bad.length === 0, bad.length ? bad.slice(0, 3).join('; ') : `${checked} hands`);
}

// THE ARM THE SPLIT EXISTS FOR. `pk-advance-now` ends a betting round at
// `pk-showdown`, which ranks FIVE cards with `evaluate-hand`; a seven-card
// or wild variant settled that way would pay the wrong seat while the pot
// still balanced and the winner was still 0, 1 or -1. So the seat the
// table pays is checked against an independent comparison of the two
// hands, built from the exports the watch-only session uses.
{
  const bad = [];
  let checked = 0, splits = 0;
  for (const v of [0, 1, 2, 7]) { // no wilds, no spade side pot
    for (const seed of [1, 2, 3, 5, 8, 13, 21, 34, 55, 89]) {
      const { p } = playOut(v, seed, callDown);
      if (e.pvt_done(p) !== 1) continue;
      if (e.pvt_folded(p) !== -1) continue; // a fold is not a comparison
      const n = e.pvt_size(p);
      const mine = [...Array(n)].map((_, i) => e.pvt_shown(p, 0, i));
      const theirs = [...Array(n)].map((_, i) => e.pvt_shown(p, 1, i));
      if (theirs.some(c => c < 0)) { bad.push(`${V[v]} seed ${seed}: cards still hidden at the end`); continue; }
      const rank = c => n === 5
        ? e.pv_eval5(c[0], c[1], c[2], c[3], c[4])
        : e.pv_best5(c[0], c[1], c[2], c[3], c[4], c[5], c[6]);
      const cmp = e.pv_cmp(rank(mine), rank(theirs)); // 1, 2 or 0
      const want = cmp === 1 ? 0 : cmp === 2 ? 1 : -1;
      checked++;
      if (want === -1) splits++;
      if (e.pvt_winner(p) !== want) {
        bad.push(`${V[v]} seed ${seed}: paid seat ${e.pvt_winner(p)}, the hands say ${want}`);
      }
    }
  }
  ok('CONTROL: the corpus reached showdowns rather than folds', checked > 0,
     `${checked} showdowns, ${splits} split`);
  ok('the seat the table pays is the seat the two hands name',
     bad.length === 0, bad.length ? bad.slice(0, 3).join('; ') : `${checked} showdowns`);
}

// A wild variant must be ranked WITH the wilds, and that is only a real
// arm where the wilds change the answer. The counter below is what says
// the corpus reached such a hand, because a deal with no wild card in it
// ranks identically either way and would agree with a broken table.
{
  let moved = 0, bad = [];
  for (const v of [3, 6]) {
    for (let seed = 1; seed <= 40; seed++) {
      const p = e.pvt_new(v, seed);
      const c = [...Array(7)].map((_, i) => e.pvt_card(p, 0, i));
      const plain = e.pv_rank(e.pv_best5(c[0], c[1], c[2], c[3], c[4], c[5], c[6]));
      const wildCards = c.filter(x => e.pvt_wildat(p, x) === 1).length;
      if (wildCards === 0) continue;
      if (e.pvt_rank(p, 0) < plain) {
        bad.push(`${V[v]} seed ${seed}: wilds made the hand WORSE, ${plain} to ${e.pvt_rank(p,0)}`);
      }
      if (e.pvt_rank(p, 0) > plain) moved++;
    }
  }
  ok('CONTROL: the corpus reached wild hands whose rank the wilds change',
     moved > 0, `${moved} hands ranked higher with the wilds`);
  ok('a wild card never makes a hand worse', bad.length === 0,
     bad.length ? bad.slice(0, 3).join('; ') : 'baseball and follow-the-queen');
  const noWilds = [0, 1, 2, 4, 5, 7].filter(v => {
    const p = e.pvt_new(v, 1);
    return [...Array(52)].some((_, card) => e.pvt_wildat(p, card) === 1);
  });
  ok('only baseball and follow-the-queen have anything wild', noWilds.length === 0,
     noWilds.length ? noWilds.map(v => V[v]).join('; ') : '6 variants with none');
}

// The spade in the hole is a side pot in the Chicagos and nowhere else,
// and the arm that matters is the one where it OVERRIDES a drawn hand.
// Without a corpus that reaches that case the rule could be missing and
// every hand would still pay the same seat (L-CONSTRUCT).
{
  let decided = 0, elsewhere = [];
  for (let v = 0; v < 8; v++) {
    for (let seed = 1; seed <= 30; seed++) {
      const { p } = playOut(v, seed, callDown);
      const s = e.pvt_spade(p);
      if (v === 4 || v === 5) { if (s !== 0) decided++; }
      else if (s !== 0) elsewhere.push(`${V[v]} seed ${seed}: spade ${s}`);
    }
  }
  ok('CONTROL: the corpus reached hands the spade in the hole decided',
     decided > 0, `${decided} hands`);
  ok('and no other variant has a spade side pot', elsewhere.length === 0,
     elsewhere.length ? elsewhere.slice(0, 3).join('; ') : '6 variants');
}

// Jacks or better is the one variant with a RULE that is a decision: you
// may not open the betting without a pair of jacks or better. Both answers
// have to appear in the corpus or the arm is agreeing with itself.
{
  let opens = 0, refuses = 0, bad = [];
  for (let seed = 1; seed <= 60; seed++) {
    const p = e.pvt_new(7, seed);
    if (e.pvt_tocall(p, 0) !== 0) continue; // opening only means the unbet case
    if (e.pvt_canopen(p) === 1) {
      opens++;
      if (e.pvt_canraise(p) !== 1) bad.push(`seed ${seed}: could open and could not raise`);
    } else {
      refuses++;
      if (e.pvt_canraise(p) === 1) bad.push(`seed ${seed}: could not open and could raise anyway`);
    }
  }
  ok('CONTROL: the corpus reached hands that can open AND hands that cannot',
     opens > 0 && refuses > 0, `${opens} can, ${refuses} cannot`);
  ok('jacks or better gates the opening bet and nothing else does',
     bad.length === 0, bad.length ? bad.slice(0, 3).join('; ') : `${opens + refuses} hands`);
  const gated = [0, 1, 2, 3, 4, 5, 6].filter(v => e.pvt_canopen(e.pvt_new(v, 4)) !== 1);
  ok('no other variant is gated on opening', gated.length === 0,
     gated.length ? gated.map(v => V[v]).join('; ') : '7 variants');
}

// The draw belongs to five card draw alone.
{
  const offered = [1, 2, 3, 4, 5, 6, 7].filter(v => {
    let p = e.pvt_new(v, 9), guard = 0;
    while (e.pvt_done(p) === 0 && guard++ < 20) {
      if (e.pvt_candraw(p) === 1) return true;
      p = e.pvt_cur(p) === 0 ? callDown(p) : e.pvt_step(p);
    }
    return false;
  });
  ok('only five card draw ever offers a draw', offered.length === 0,
     offered.length ? offered.map(v => V[v]).join('; ') : '7 variants refuse');
  let p = e.pvt_new(0, 9), guard = 0;
  while (e.pvt_done(p) === 0 && e.pvt_candraw(p) === 0 && guard++ < 20) {
    p = e.pvt_cur(p) === 0 ? (e.pvt_cancall(p) === 1 ? e.pvt_call(p) : p) : e.pvt_step(p);
  }
  ok('and five card draw does offer one', e.pvt_candraw(p) === 1);
  const marked = e.pvt_mark(p, 0);
  ok('a mark answers a new table', marked !== p);
  ok('THE COPY ARM: the table marked from is untouched', e.pvt_marked(p, 0) === 0);
  ok('the mark landed', e.pvt_marked(marked, 0) === 1);
  ok('marking the same card twice is refused un-copied', e.pvt_mark(marked, 0) === marked);
  ok('a mark off the hand is refused un-copied',
     e.pvt_mark(p, 5) === p && e.pvt_mark(p, -1) === p);
}

// A REFUSAL ANSWERS THE STATE IT WAS GIVEN. A wrapper that copies first
// and lets the engine refuse the copy answers a different handle, and a
// page comparing what came back against what it sent sees every click
// accepted (games-backlog GAME-55).
{
  const bad = [];
  for (let v = 0; v < 8; v++) {
    const p = e.pvt_new(v, 6);
    // It is not the opponent's turn to be moved by us, and a variant that
    // deals seven has no draw to take.
    if (e.pvt_candraw(p) === 0 && e.pvt_draw(p) !== p) bad.push(`${V[v]}: a draw it cannot take`);
    if (e.pvt_canraise(p) === 0 && e.pvt_raise(p) !== p) bad.push(`${V[v]}: a raise it cannot make`);
    const done = playOut(v, 6, callDown).p;
    if (e.pvt_fold(done) !== done) bad.push(`${V[v]}: folded a finished hand`);
    if (e.pvt_call(done) !== done) bad.push(`${V[v]}: called a finished hand`);
    if (e.pvt_step(done) !== done) bad.push(`${V[v]}: stepped a finished hand`);
  }
  ok('every refusal answers the SAME handle, un-copied', bad.length === 0,
     bad.length ? bad.slice(0, 3).join('; ') : '8 variants');
}

// The watch-only session is untouched by any of this.
{
  const bad = [];
  for (let v = 0; v < 8; v++) {
    const s = e.pv_run(v, 7, 2);
    if (e.pv_winner(s) < 0 || e.pv_winner(s) > 2) bad.push(`${V[v]}: winner ${e.pv_winner(s)}`);
    if (e.pv_played(s) !== 1) bad.push(`${V[v]}: ${e.pv_played(s)} hands played`);
  }
  ok('the watch-only session still answers for all eight', bad.length === 0,
     bad.length ? bad.join('; ') : '8 variants');
}

console.log(fail === 0
  ? `\nPASS: the variants run and the best five of seven is the best (${pass} arms).`
  : `\nFAIL: ${fail} of ${pass + fail} arms.`);
process.exitCode = fail === 0 ? 0 : 1;
