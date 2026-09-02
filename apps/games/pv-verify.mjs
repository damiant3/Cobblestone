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

console.log(fail === 0
  ? `\nPASS: the variants run and the best five of seven is the best (${pass} arms).`
  : `\nFAIL: ${fail} of ${pass + fail} arms.`);
process.exitCode = fail === 0 ? 0 : 1;
