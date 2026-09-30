// Grade the Yahtzee wasm module.
//
// Yahtzee's scoring table is a pure function of five dice, and there are
// only 6^5 = 7,776 of those, so this grader does not sample: it scores
// EVERY dice combination in EVERY one of the thirteen categories against
// an independent implementation of the rules. 101,088 comparisons, a
// second or so, and no corner left for a defect to sit in.
//
// The second arm needs no dice at all. Each category has a set of values
// it is even capable of scoring -- a Yahtzee is 0 or 50, a full house 0 or
// 25, the fives are 0, 5, 10, 15, 20 or 25 -- so a finished scorecard can
// be checked against those sets without knowing what was rolled. That
// catches a score written into the wrong row, which the exhaustive arm
// cannot see because both rows score correctly in isolation.
//
// Usage: node apps/games/yh-verify.mjs [path/to/yahtzee.wasm]

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const wasmPath = process.argv[2] ||
  join(here, '..', 'landing', 'web', 'games', 'yahtzee.wasm');

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

const CATNAME = ['ones','twos','threes','fours','fives','sixes','three of a kind',
                 'four of a kind','full house','small straight','large straight',
                 'yahtzee','chance'];

// The rules, independently.
function score(dice, cat) {
  const count = f => dice.filter(d => d === f).length;
  const sum = dice.reduce((a, b) => a + b, 0);
  const maxCount = Math.max(...[1, 2, 3, 4, 5, 6].map(count));
  const has = f => count(f) > 0;
  if (cat <= 5) return count(cat + 1) * (cat + 1);
  if (cat === 6) return maxCount >= 3 ? sum : 0;
  if (cat === 7) return maxCount >= 4 ? sum : 0;
  if (cat === 8) {
    const three = [1, 2, 3, 4, 5, 6].some(f => count(f) === 3);
    const two = [1, 2, 3, 4, 5, 6].some(f => count(f) === 2);
    return three && two ? 25 : 0;
  }
  if (cat === 9) {
    const run4 = (a) => [a, a + 1, a + 2, a + 3].every(has);
    return (run4(1) || run4(2) || run4(3)) ? 30 : 0;
  }
  if (cat === 10) {
    const run5 = (a) => [a, a + 1, a + 2, a + 3, a + 4].every(has);
    return (run5(1) || run5(2)) ? 40 : 0;
  }
  if (cat === 11) return maxCount === 5 ? 50 : 0;
  return sum;
}

// What each category is even capable of scoring.
function legalValues(cat) {
  if (cat <= 5) {
    const f = cat + 1;
    return new Set([0, f, 2 * f, 3 * f, 4 * f, 5 * f]);
  }
  if (cat === 6 || cat === 7) {
    // Sum of five dice, at least three alike, or zero.
    const s = new Set([0]);
    for (let a = 1; a <= 6; a++) for (let b = 1; b <= 6; b++) for (let c = 1; c <= 6; c++)
      for (let d = 1; d <= 6; d++) for (let f = 1; f <= 6; f++) {
        const v = score([a, b, c, d, f], cat);
        if (v) s.add(v);
      }
    return s;
  }
  if (cat === 8) return new Set([0, 25]);
  if (cat === 9) return new Set([0, 30]);
  if (cat === 10) return new Set([0, 40]);
  if (cat === 11) return new Set([0, 50]);
  const s = new Set();
  for (let v = 5; v <= 30; v++) s.add(v);
  return s;
}

console.log(`yh-verify ${wasmPath}`);

// -- EVERY DICE COMBINATION, EVERY CATEGORY -------------------------------
{
  const bad = [];
  let checked = 0;
  const nonZero = new Array(13).fill(0);
  for (let a = 1; a <= 6 && bad.length < 4; a++)
    for (let b = 1; b <= 6 && bad.length < 4; b++)
      for (let c = 1; c <= 6 && bad.length < 4; c++)
        for (let d = 1; d <= 6 && bad.length < 4; d++)
          for (let f = 1; f <= 6 && bad.length < 4; f++) {
            const dice = [a, b, c, d, f];
            for (let cat = 0; cat < 13; cat++) {
              const want = score(dice, cat);
              const got = e.yh_score(a, b, c, d, f, cat);
              checked++;
              if (want !== 0) nonZero[cat]++;
              if (got !== want) {
                bad.push(`${dice.join('')} ${CATNAME[cat]}: engine ${got}, rules ${want}`);
                break;
              }
            }
          }
  ok('every category scores every one of the 7,776 dice combinations by the rules',
     bad.length === 0, bad.slice(0, 3).join('; ') || `${checked} comparisons`);
  ok('control: every category scored something somewhere, so none is dead',
     nonZero.every(n => n > 0),
     CATNAME.map((n, i) => `${n} ${nonZero[i]}`).join(', '));
  ok('a category off the card reads -1',
     e.yh_score(1, 1, 1, 1, 1, 13) === -1 && e.yh_score(1, 1, 1, 1, 1, -1) === -1);
}

// -- The named hands ------------------------------------------------------
{
  const cases = [
    ['five ones is a yahtzee and not a full house', [1, 1, 1, 1, 1], 11, 50],
    ['five ones scores nothing as a full house',    [1, 1, 1, 1, 1], 8, 0],
    ['three and two is a full house',               [3, 3, 3, 5, 5], 8, 25],
    ['four and one is not a full house',            [3, 3, 3, 3, 5], 8, 0],
    ['1-2-3-4 with a spare is a small straight',    [1, 2, 3, 4, 4], 9, 30],
    ['1-2-3-4 with a spare is not a large one',     [1, 2, 3, 4, 4], 10, 0],
    ['2-3-4-5-6 is a large straight',               [2, 3, 4, 5, 6], 10, 40],
    ['a large straight is also a small one',        [2, 3, 4, 5, 6], 9, 30],
    ['1-2-4-5-6 is neither',                        [1, 2, 4, 5, 6], 9, 0],
    ['four of a kind counts every die',             [4, 4, 4, 4, 2], 7, 18],
    ['three of a kind counts every die',            [4, 4, 4, 1, 2], 6, 15],
    ['the fives row counts only fives',             [5, 5, 1, 2, 5], 4, 15],
  ];
  const oracleBad = [], engineBad = [];
  for (const [name, dice, cat, want] of cases) {
    if (score(dice, cat) !== want) oracleBad.push(`ORACLE on ${name}: ${score(dice, cat)}`);
    const got = e.yh_score(...dice, cat);
    if (got !== want) engineBad.push(`${name}: engine ${got}, rules ${want}`);
  }
  ok('control: the oracle gets the named hands right first',
     oracleBad.length === 0, oracleBad.join('; ') || `${cases.length} hands`);
  ok('every named hand scores as the rules say',
     engineBad.length === 0, engineBad.join('; ') || `${cases.length} hands`);
}

// -- The Joker, the bonuses and the legal boxes (Rules 3 and 4) ----------
// The oracle below is the rules text, not the engine: `filled` maps a box to
// the value it holds, and a Yahtzee with the Yahtzee box filled is a Joker.
function legalSet(dice, filled) {
  const five = dice.every(d => d === dice[0]);
  const open = [...Array(13).keys()].filter(c => !(c in filled));
  if (!(five && 11 in filled)) return open;
  const upper = dice[0] - 1;
  if (!(upper in filled)) return [upper];
  const lower = open.filter(c => c >= 6);
  return lower.length ? lower : open.filter(c => c < 6);
}
function takeScore(dice, filled, cat) {
  const joker = dice.every(d => d === dice[0]) && 11 in filled;
  if (joker && cat === 8) return 25;
  if (joker && cat === 9) return 30;
  if (joker && cat === 10) return 40;
  return score(dice, cat);
}
const bonusOf = (dice, filled) => (dice.every(d => d === dice[0]) && filled[11] === 50 ? 100 : 0);
function stateOf(filled, dice, bonus = 0) {
  let h = e.yh_new(1);
  for (const [c, v] of Object.entries(filled)) h = e.yh_put(h, Number(c), v);
  if (bonus) h = e.yh_put(h, 13, bonus);
  return e.yh_dice(h, ...dice);
}
const engineLegal = h => [...Array(13).keys()].filter(c => e.yh_legal(h, c) === 1);
const same = (a, b) => a.length === b.length && a.every((v, i) => v === b[i]);
{
  // One arm per state class the rules distinguish.
  const upperFull = { 0: 3, 1: 6, 2: 9, 3: 12, 4: 15, 5: 18 };
  const lowerFull = { 6: 20, 7: 0, 8: 25, 9: 30, 10: 40, 12: 22 };
  const classes = [
    ['not five alike: every open box', { 0: 1, 8: 25 }, [2, 2, 3, 3, 3]],
    ['five alike, Yahtzee box open: every open box, no Joker', { 0: 1 }, [4, 4, 4, 4, 4]],
    ['Joker, its upper box open: that box only', { 11: 50 }, [4, 4, 4, 4, 4]],
    ['Joker, upper box filled, lower open: the open lower boxes', { 11: 50, 3: 12 }, [4, 4, 4, 4, 4]],
    ['Joker, Yahtzee box 0: the same boxes, no bonus', { 11: 0, 3: 12, 8: 25 }, [4, 4, 4, 4, 4]],
    ['Joker, upper and lower filled: the open upper boxes', { 11: 50, 3: 12, ...lowerFull }, [4, 4, 4, 4, 4]],
    ['Joker, one upper box left: that box', { 11: 50, ...upperFull, ...lowerFull, 0: undefined }, [6, 6, 6, 6, 6]],
  ];
  const bad = [];
  for (const [name, f0, dice] of classes) {
    const filled = Object.fromEntries(Object.entries(f0).filter(([, v]) => v !== undefined));
    const h = stateOf(filled, dice);
    const want = legalSet(dice, filled), got = engineLegal(h);
    if (!same(want, got)) { bad.push(`${name}: engine [${got}], rules [${want}]`); continue; }
    for (const c of want) {
      const t = e.yh_take(h, c);
      if (e.yh_card(t, c) !== takeScore(dice, filled, c)) bad.push(`${name}: box ${CATNAME[c]} took ${e.yh_card(t, c)}, rules ${takeScore(dice, filled, c)}`);
      if (e.yh_bonus(t) !== bonusOf(dice, filled)) bad.push(`${name}: bonus ${e.yh_bonus(t)}, rules ${bonusOf(dice, filled)}`);
    }
    for (let c = 0; c < 13; c++) if (!want.includes(c) && e.yh_take(h, c) !== h) bad.push(`${name}: box ${CATNAME[c]} was taken, which the rules refuse`);
  }
  ok('every class: the legal boxes, what each scores, the bonus, and every other box refused',
     bad.length === 0, bad.slice(0, 3).join('; ') || `${classes.length} classes`);
}
{
  // Random cards against the rules, every five-alike roll and random rolls.
  let seed = 7;
  const rnd = n => { seed = (seed * 1103515245 + 12345) % 2147483648; return Math.floor(seed / 65536) % n; };
  const bad = [];
  let sets = 0, jokers = 0, bonuses = 0;
  for (let k = 0; k < 400 && bad.length < 3; k++) {
    const filled = {};
    for (let c = 0; c < 13; c++) if (rnd(3) === 0) filled[c] = c === 11 ? (rnd(2) ? 50 : 0) : score([1, 2, 3, 4, 5].map(() => 1 + rnd(6)), c);
    const rolls = [...Array(6).keys()].map(f => [f + 1, f + 1, f + 1, f + 1, f + 1]).concat([...Array(6)].map(() => [0, 0, 0, 0, 0].map(() => 1 + rnd(6))));
    for (const dice of rolls) {
      const h = stateOf(filled, dice);
      const want = legalSet(dice, filled), got = engineLegal(h);
      sets++;
      if (dice.every(d => d === dice[0]) && 11 in filled) jokers++;
      if (!same(want, got)) { bad.push(`card ${JSON.stringify(filled)} dice ${dice}: engine [${got}], rules [${want}]`); break; }
      const c = want[rnd(want.length)];
      if (c === undefined) continue;
      const t = e.yh_take(h, c);
      const card = { ...filled, [c]: takeScore(dice, filled, c) };
      const up = [0, 1, 2, 3, 4, 5].reduce((a, i) => a + (card[i] || 0), 0);
      const b = bonusOf(dice, filled);
      if (b) bonuses++;
      const total = Object.values(card).reduce((a, v) => a + v, 0) + (up >= 63 ? 35 : 0) + b;
      if (e.yh_total(t) !== total) { bad.push(`card ${JSON.stringify(filled)} dice ${dice} box ${c}: total ${e.yh_total(t)}, rules ${total}`); break; }
    }
  }
  ok('400 random cards: the legal set, the take and the total (upper and Yahtzee bonuses) by the rules',
     bad.length === 0, bad.slice(0, 2).join('; ') || `${sets} legal sets, ${jokers} Jokers, ${bonuses} bonuses`);
  ok('control: the random cards reached Jokers and bonuses', jokers > 50 && bonuses > 10, `${jokers} Jokers, ${bonuses} bonuses`);
}
{
  const at = v => stateOf({ 0: 3, 1: 6, 2: 9, 3: 12, 4: 15, 5: v }, [1, 2, 3, 4, 6]);
  ok('the upper bonus: 63 in the upper boxes adds 35, 62 adds nothing',
     e.yh_total(at(18)) === 63 + 35 && e.yh_total(at(17)) === 62, `${e.yh_total(at(18))} and ${e.yh_total(at(17))}`);
}
// -- A whole game: the card, the bookkeeping and the legal values ---------
{
  const bad = [];
  const scores = [];
  for (let seed = 1; seed <= 30; seed++) {
    let h = e.yh_new(seed);
    if ([...Array(13)].some((_, c) => e.yh_card(h, c) !== 0 || e.yh_done(h, c) !== 0)) {
      bad.push(`seed ${seed}: a fresh card is not empty`);
      continue;
    }
    const before = [...Array(13)].map((_, c) => e.yh_card(h, c)).join(',');
    const first = e.yh_turn(h);
    if ([...Array(13)].map((_, c) => e.yh_card(h, c)).join(',') !== before) {
      bad.push(`seed ${seed}: THE COPY ARM, the card a turn was taken from changed`);
      break;
    }
    if (first === h) bad.push(`seed ${seed}: a turn answered the same state`);
    for (let t = 0; t < 13; t++) h = e.yh_turn(h);
    const done = [...Array(13)].map((_, c) => e.yh_done(h, c));
    const card = [...Array(13)].map((_, c) => e.yh_card(h, c));
    if (done.some(v => v !== 1)) bad.push(`seed ${seed}: ${done.filter(v => v === 1).length} of 13 categories used`);
    for (let c = 0; c < 13; c++) {
      if (!legalValues(c).has(card[c])) {
        bad.push(`seed ${seed}: ${CATNAME[c]} scored ${card[c]}, which it cannot score`);
        break;
      }
    }
    const upper = card.slice(0, 6).reduce((a, b) => a + b, 0);
    const total = card.reduce((a, b) => a + b, 0) + (upper >= 63 ? 35 : 0) + e.yh_bonus(h);
    if (e.yh_total(h) !== total) bad.push(`seed ${seed}: total ${e.yh_total(h)} against ${total}`);
    scores.push(total);
  }
  ok('thirteen turns fill thirteen categories, each with a value that category can score',
     bad.length === 0, bad.slice(0, 3).join('; ') || '30 games');
  ok('control: the games scored something and differed from each other',
     new Set(scores).size > 1 && Math.min(...scores) > 0,
     `${new Set(scores).size} distinct totals, ${Math.min(...scores)} to ${Math.max(...scores)}`);
}

// -- The runner agrees with stepping it -----------------------------------
{
  const bad = [];
  for (let seed = 1; seed <= 20; seed++) {
    const r = e.yh_run(seed);
    let h = e.yh_new(seed);
    for (let t = 0; t < 13; t++) h = e.yh_turn(h);
    if (e.yh_rscore(r) !== e.yh_total(h)) {
      bad.push(`seed ${seed}: run ${e.yh_rscore(r)}, stepped ${e.yh_total(h)}`);
    }
    if (e.yh_rturns(r) !== 13) bad.push(`seed ${seed}: ${e.yh_rturns(r)} turns`);
  }
  ok('running a game and stepping it turn by turn give the same score',
     bad.length === 0, bad.slice(0, 3).join('; ') || '20 games');
}

console.log(fail === 0
  ? `\nPASS: Yahtzee scores every hand in every category by the rules (${pass} arms).`
  : `\nFAIL: ${fail} of ${pass + fail} arms.`);
process.exitCode = fail === 0 ? 0 : 1;
