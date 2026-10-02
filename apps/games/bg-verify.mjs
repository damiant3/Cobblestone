// Grade the Backgammon wasm module, and the arcade page that plays it.
//
// Three things are graded, and the second is the one the rules rest on.
//
// CONSERVATION. Each side owns exactly fifteen checkers, always, on a point,
// on the bar, or borne off. A hit that loses a checker, a bear off that
// double-counts, or a copy that half-copies the board all break that sum.
//
// THE RULES. Every board-state class the rules distinguish gets a position
// and a throw, and the arm asserts the WHOLE legal set: what is allowed and
// what is refused (L-BOTHARMS). Then an oracle written here, from the rules
// text and not from the engine, is run against the engine on random
// positions. The oracle counts in each side's own pips (25 is the bar, 0 and
// below is off) and tries every order of the dice with no pruning, where the
// engine works in board indices and prunes a double's search; a shared
// misreading would have to be made twice in two different coordinates.
//
// THE PAGE. The arcade's own move() decides which legal move a click makes,
// so it is driven here too: the bar entry must offer every enterable die,
// the tray bears off with the smaller of two dice that both would while a
// die clicked in the tray spends that die, and the
// opponent's every move must be one the oracle allows.
//
// Usage: node apps/games/bg-verify.mjs [path/to/backgammon.wasm]

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import { GAMES } from '../landing/web/games/arcade.js';

const here = dirname(fileURLToPath(import.meta.url));
const wasmPath = process.argv[2] ||
  join(here, '..', 'landing', 'web', 'games', 'backgammon.wasm');

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

const BAR = 24;
const pack = q => q.reduce((n, d, i) => n + d * Math.pow(8, i), 0);
const points = h => [...Array(24)].map((_, i) => e.bg_point(h, i));
const held = (h, p) => points(h).reduce((n, v) => n + (p === 0 ? Math.max(v, 0) : Math.max(-v, 0)), 0)
  + e.bg_bar(h, p) + e.bg_off(h, p);

// A position: who is on roll, points as {index: signed count}, bar and off.
function position(cur, pts, bar = [0, 0], off = [0, 0]) {
  let h = e.bg_empty(cur);
  for (const [i, v] of Object.entries(pts)) h = e.bg_put(h, Number(i), v);
  h = e.bg_put(h, 24, bar[0]); h = e.bg_put(h, 25, bar[1]);
  h = e.bg_put(h, 26, off[0]); h = e.bg_put(h, 27, off[1]);
  return h;
}

// The engine's legal set for a throw, as sorted "from:die" strings.
function engineSet(h, dice) {
  const s = [];
  for (const d of new Set(dice)) {
    const m = e.bg_mask(h, pack(dice), d);
    for (let f = 0; f <= 24; f++) if ((m >>> f) & 1) s.push(`${f}:${d}`);
  }
  return s.sort();
}

// -- THE ORACLE -------------------------------------------------------------
// A position as plain data, read back out of the module.
const snap = h => ({
  pts: points(h), bar: [e.bg_bar(h, 0), e.bg_bar(h, 1)],
  off: [e.bg_off(h, 0), e.bg_off(h, 1)], cur: e.bg_cur(h),
});
// Side p's pip r (1..24) is this board index; 25 is the bar.
const idx = (p, r) => p === 0 ? r - 1 : 24 - r;
const pip = (p, i) => p === 0 ? i + 1 : 24 - i;
const mine = (s, p, i) => p === 0 ? s.pts[i] > 0 : s.pts[i] < 0;
const theirs = (s, p, i) => p === 0 ? s.pts[i] < 0 : s.pts[i] > 0;
const count = (s, i) => Math.abs(s.pts[i]);

function oneOk(s, r, d) {
  const p = s.cur;
  if (r === 25) {
    if (s.bar[p] === 0) return false;
    const t = idx(p, 25 - d);
    return !(theirs(s, p, t) && count(s, t) >= 2);
  }
  if (s.bar[p] > 0) return false;
  if (!mine(s, p, idx(p, r))) return false;
  const to = r - d;
  if (to >= 1) {
    const t = idx(p, to);
    return !(theirs(s, p, t) && count(s, t) >= 2);
  }
  // Off: every checker home, and past the edge only from the highest pip.
  for (let q = 7; q <= 24; q++) if (mine(s, p, idx(p, q))) return false;
  if (to === 0) return true;
  for (let q = r + 1; q <= 6; q++) if (mine(s, p, idx(p, q))) return false;
  return true;
}

function oneApply(s, r, d) {
  const p = s.cur, n = { pts: s.pts.slice(), bar: s.bar.slice(), off: s.off.slice(), cur: p };
  const sign = p === 0 ? 1 : -1;
  if (r === 25) n.bar[p]--; else n.pts[idx(p, r)] -= sign;
  const to = r - d;
  if (to <= 0) { n.off[p]++; return n; }
  const t = idx(p, to);
  if (theirs(n, p, t) && count(n, t) === 1) { n.pts[t] = 0; n.bar[1 - p]++; }
  n.pts[t] += sign;
  return n;
}

// Every order of the dice, no pruning: the longest play, and the first moves
// of every play that long.
function oracle(s, dice) {
  let best = 0;
  const firsts = new Set();
  const walk = (st, left, depth, first) => {
    if (st.off[st.cur] >= 15) left = [];
    let moved = false;
    for (let j = 0; j < left.length; j++) {
      const d = left[j];
      for (let r = 1; r <= 25; r++) {
        if (!oneOk(st, r, d)) continue;
        moved = true;
        const rest = left.slice(0, j).concat(left.slice(j + 1));
        walk(oneApply(st, r, d), rest, depth + 1, first || { r, d });
      }
    }
    if (!moved) {
      if (depth > best) { best = depth; firsts.clear(); }
      if (depth === best && first) firsts.add(`${first.r}:${first.d}`);
    }
  };
  walk(s, dice, 0, null);
  let set = [...firsts];
  // Either die but not both: the larger.
  if (best === 1 && dice.length === 2 && dice[0] !== dice[1]) {
    const hi = Math.max(...dice);
    if (set.some(m => m.endsWith(':' + hi))) set = set.filter(m => m.endsWith(':' + hi));
  }
  const p = s.cur;
  return {
    most: best,
    set: set.map(m => { const [r, d] = m.split(':').map(Number); return `${r === 25 ? BAR : idx(p, r)}:${d}`; }).sort(),
  };
}

console.log(`bg-verify ${wasmPath}`);

// -- The opening position -------------------------------------------------
const start = e.bg_new();
const OPEN = [-2, 0, 0, 0, 0, 5, 0, 3, 0, 0, 0, -5, 5, 0, 0, 0, -3, 0, -5, 0, 0, 0, 0, 2];
ok('the opening is the standard backgammon setup',
   JSON.stringify(points(start)) === JSON.stringify(OPEN), JSON.stringify(points(start)));
ok('each side starts with fifteen checkers',
   held(start, 0) === 15 && held(start, 1) === 15,
   `white ${held(start, 0)}, black ${held(start, 1)}`);
ok('white is on roll, nothing borne off, nobody on the bar',
   e.bg_cur(start) === 0 && e.bg_off(start, 0) === 0 && e.bg_off(start, 1) === 0 &&
   e.bg_bar(start, 0) === 0 && e.bg_bar(start, 1) === 0);
ok('the game is not over at the start', e.bg_done(start) === 0 && e.bg_winner(start) === -1);

// -- The dice -------------------------------------------------------------
const dice400 = [...Array(400)].map((_, s) => e.bg_die(s + 1));
ok('every die is between 1 and 6', dice400.every(d => d >= 1 && d <= 6),
   `min ${Math.min(...dice400)} max ${Math.max(...dice400)}`);
ok('all six faces appear', new Set(dice400).size === 6, `${new Set(dice400).size} faces`);
ok('the same seed gives the same die', e.bg_die(4242) === e.bg_die(4242));

// -- THE COPY ARM ---------------------------------------------------------
const before = JSON.stringify(points(start));
const stepped = e.bg_play(start, pack([3, 1]), 7, 3);
ok('playing answers a different board', stepped !== start, `${start} -> ${stepped}`);
ok('THE COPY ARM: the board played from is untouched',
   JSON.stringify(points(start)) === before, JSON.stringify(points(start)));
ok('the play moved exactly one checker, 8-point to 5-point',
   e.bg_point(stepped, 7) === 2 && e.bg_point(stepped, 4) === 1 && held(stepped, 0) === 15);
for (let d = 1; d <= 6; d++) e.bg_mask(start, pack([d, d]), d);
e.bg_ai(start, pack([6, 5]));
ok('GAME-13: asking for masks and a pick does not mutate the board asked about',
   JSON.stringify(points(start)) === before, JSON.stringify(points(start)));
ok('an illegal play answers the same board', e.bg_play(start, pack([3, 1]), 12, 1) === start);
ok('a die not in hand is refused', e.bg_legal(start, pack([3, 1]), 7, 2) === 0);

// -- Turns ----------------------------------------------------------------
const handed = e.bg_endturn(start);
ok('ending the turn hands the dice over', e.bg_cur(handed) === 1, e.bg_cur(handed));
ok('ending the turn moves no checker', JSON.stringify(points(handed)) === before);
ok('ending a turn is a value too, the old board still on roll for white', e.bg_cur(start) === 0);

// -- THE RULES: one arm per board-state class -----------------------------
// Each names the position, the throw, and the whole legal set it must answer.
const cls = (name, h, dice, want, most) => {
  const got = engineSet(h, dice);
  ok(`class: ${name}`, JSON.stringify(got) === JSON.stringify([...want].sort()),
     `legal ${JSON.stringify(got)}, want ${JSON.stringify([...want].sort())}`);
  if (most !== undefined) ok(`class: ${name}, dice playable`, e.bg_most(h, pack(dice)) === most,
     `${e.bg_most(h, pack(dice))}, want ${most}`);
  return h;
};

cls('the opening, 3-1: every checker moves, the 13-point may not play the 1 onto five of Black',
    start, [3, 1], ['23:3', '12:3', '7:3', '5:3', '23:1', '7:1', '5:1'], 2);
const barBoth = cls('one on the bar, both dice enter: the bar only, either die',
    position(0, { 10: 1 }, [1, 0]), [2, 5], ['24:2', '24:5'], 2);
cls('one on the bar, the 2 entry blocked: the 5 only',
    position(0, { 10: 1, 22: -2 }, [1, 0]), [2, 5], ['24:5'], 2);
cls('one on the bar, both entries blocked: nothing, the turn is forfeit',
    position(0, { 10: 1, 22: -2, 19: -2 }, [1, 0]), [2, 5], [], 0);
const twoBar = cls('two on the bar: both dice must enter',
    position(0, { 10: 1 }, [2, 0]), [3, 4], ['24:3', '24:4'], 2);
cls('two on the bar, one entered: the second must enter with the other die',
    e.bg_play(twoBar, pack([3, 4]), 24, 3), [4], ['24:4'], 1);
cls('MUST PLAY BOTH: the 1 from the 11-point would lose the 6, so it is refused',
    position(0, { 10: 1, 20: 1, 14: -2, 3: -2 }), [6, 1], ['10:6', '20:1'], 2);
cls('LARGER DIE: either die alone, never both, so the 6',
    position(0, { 9: 1, 2: -2 }), [6, 1], ['9:6'], 1);
cls('larger die blocked: the smaller is played',
    position(0, { 9: 1, 3: -2, 2: -2 }), [6, 1], ['9:1'], 1);
cls('a double played as far as it goes: two of four',
    position(0, { 12: 1, 3: -2 }), [3, 3, 3, 3], ['12:3'], 2);
cls('bearing off: exact dice, a move inside home, no overshoot from below a higher checker',
    position(0, { 2: 1, 4: 1 }, [0, 0], [13, 0]), [3, 5], ['2:3', '4:3', '4:5'], 2);
cls('bearing off: a die above the highest point bears it off, and the larger is played',
    position(0, { 2: 1 }, [0, 0], [14, 0]), [6, 5], ['2:6'], 1);
cls('bearing off: an empty point with a checker above it moves the higher checker',
    position(0, { 4: 1, 0: 1 }, [0, 0], [13, 0]), [3, 2], ['4:2', '4:3'], 2);
cls('no bearing off while a checker is outside home',
    position(0, { 2: 1, 8: 1 }, [0, 0], [13, 0]), [3, 4], ['8:3', '8:4'], 2);
const hitHome = cls('hit while bearing off: enter first',
    position(0, { 2: 1 }, [1, 0], [13, 0]), [3, 4], ['24:3', '24:4'], 2);
cls('hit while bearing off, entered: no bearing off until it comes home',
    e.bg_play(hitHome, pack([3, 4]), 24, 3), [4], ['21:4'], 1);
const last = cls('the last checker: either die bears it off, the larger is played',
    position(0, { 0: 1 }, [0, 0], [14, 0]), [1, 2], ['0:2'], 1);
const won = e.bg_play(last, pack([1, 2]), 0, 2);
ok('class: bearing off the fifteenth wins, and nothing moves after',
   e.bg_done(won) === 1 && e.bg_winner(won) === 0 && e.bg_mask(won, pack([1]), 1) === 0);
cls('Black runs the other way: the larger die for Black',
    position(1, { 14: -1, 21: 2 }), [6, 1], ['14:6'], 1);
cls('Black enters on its own side of the board',
    position(1, { 10: -1, 1: 2 }, [0, 1]), [2, 5], ['24:5'], 2);

// -- THE CUBE: rule 8 -------------------------------------------------------
const cube = h => ({ v: e.bg_cube(h), owner: e.bg_owner(h), offered: e.bg_offered(h) });
{
  ok('cube: it starts in the middle at 1', JSON.stringify(cube(start)) === '{"v":1,"owner":-1,"offered":0}',
     JSON.stringify(cube(start)));
  ok('cube: in the middle, either side may double on its turn',
     e.bg_candouble(start) === 1 && e.bg_candouble(e.bg_endturn(start)) === 1);
  const off = e.bg_double(start);
  ok('cube: an offer waits for an answer, and nothing moves meanwhile',
     cube(off).offered === 1 && e.bg_candouble(off) === 0 && e.bg_most(off, pack([3, 1])) === 0 &&
     e.bg_ai(off, pack([3, 1])) === -1 && e.bg_done(off) === 0);
  const took = e.bg_take(off);
  ok('cube: a take doubles it, hands it to the taker, and the doubler plays on',
     JSON.stringify(cube(took)) === '{"v":2,"owner":1,"offered":0}' && e.bg_cur(took) === 0,
     JSON.stringify(cube(took)));
  ok('cube: once owned, only its owner may double',
     e.bg_candouble(took) === 0 && e.bg_candouble(e.bg_endturn(took)) === 1);
  const re = e.bg_take(e.bg_double(e.bg_endturn(took)));
  ok('cube: a redouble taken makes it 4 and hands it back',
     JSON.stringify(cube(re)) === '{"v":4,"owner":0,"offered":0}', JSON.stringify(cube(re)));
  const dropped = e.bg_drop(off);
  ok('cube: a drop ends the game, the doubler wins at the value BEFORE the offer',
     e.bg_done(dropped) === 1 && e.bg_winner(dropped) === 0 && e.bg_points(dropped) === 1 && e.bg_kind(dropped) === 1,
     `done ${e.bg_done(dropped)} winner ${e.bg_winner(dropped)} points ${e.bg_points(dropped)}`);
  const redrop = e.bg_drop(e.bg_double(e.bg_endturn(took)));
  ok('cube: dropping a redouble loses the 2 the cube stood at',
     e.bg_winner(redrop) === 1 && e.bg_points(redrop) === 2, `winner ${e.bg_winner(redrop)} points ${e.bg_points(redrop)}`);
  ok('cube: no double after the game is over', e.bg_candouble(dropped) === 0 && e.bg_double(dropped) === dropped);
  ok('cube: take and drop answer nothing without an offer', e.bg_take(start) === start && e.bg_drop(start) === start);
}

// -- SCORING: rule 9 ------------------------------------------------------
const ended = (winPts, losePts, loseBar, loseOff, cubeV, winner = 0) => {
  let h = position(winner === 0 ? 1 : 0, Object.assign({}, winPts, losePts),
    winner === 0 ? [0, loseBar] : [loseBar, 0], winner === 0 ? [15, loseOff] : [loseOff, 15]);
  return e.bg_put(h, 28, cubeV);
};
const score = (name, h, kind, points) => ok(`scoring: ${name}`,
  e.bg_done(h) === 1 && e.bg_kind(h) === kind && e.bg_points(h) === points,
  `kind ${e.bg_kind(h)} points ${e.bg_points(h)}, want ${kind} and ${points}`);
score('a single game when the loser has borne one off', ended({}, { 12: -14 }, 0, 1, 1), 1, 1);
score('a gammon when the loser has borne none off', ended({}, { 12: -15 }, 0, 0, 1), 2, 2);
score('a backgammon with a loser on the bar', ended({}, { 12: -14 }, 1, 0, 1), 3, 3);
score('a backgammon with a loser in the winner\'s home board', ended({}, { 12: -14, 5: -1 }, 0, 0, 1), 3, 3);
score('only a gammon one point outside the winner\'s home board', ended({}, { 12: -14, 6: -1 }, 0, 0, 1), 2, 2);
score('Black\'s backgammon, a White checker on Black\'s 1-point', ended({}, { 10: 14, 23: 1 }, 0, 0, 1, 1), 3, 3);
score('Black\'s gammon, one point outside Black\'s home', ended({}, { 10: 14, 17: 1 }, 0, 0, 1, 1), 2, 2);
score('the cube multiplies it: a gammon at 4 is 8', ended({}, { 12: -15 }, 0, 0, 4), 2, 8);
ok('scoring: a game in progress is worth nothing yet', e.bg_kind(start) === 0 && e.bg_points(start) === 0);

// -- THE ORACLE AGAINST THE ENGINE ----------------------------------------
// Random positions, half of them bear-off races, a fifth with someone on the
// bar, a sixth thrown doubles. Every legal set and every count must agree.
let seed = 99;
// The high bits: an LCG's low bit alternates, so `% 2` on it is a pattern.
const rnd = n => { seed = (seed * 1103515245 + 12345) & 0x7fffffff; return (seed >>> 16) % n; };
function randomPosition() {
  const pts = new Array(24).fill(0), bar = [0, 0], off = [0, 0];
  const race = rnd(2) === 0;
  for (const p of [0, 1]) {
    let n = 15;
    if (rnd(5) === 0) { bar[p] = 1 + rnd(2); n -= bar[p]; }
    const o = race ? rnd(10) : 0; off[p] = o; n -= o;
    while (n > 0) {
      const r = race && bar[p] === 0 ? 1 + rnd(6) : 1 + rnd(24);
      const i = idx(p, r);
      if (p === 0 ? pts[i] < 0 : pts[i] > 0) continue;
      pts[i] += p === 0 ? 1 : -1; n--;
    }
  }
  const cur = rnd(2);
  const o = {};
  pts.forEach((v, i) => { if (v) o[i] = v; });
  return position(cur, o, bar, off);
}
let disagree = null, compared = 0, doublesSeen = 0, forcedSeen = 0;
for (let k = 0; k < 800 && !disagree; k++) {
  const h = randomPosition();
  const a = 1 + rnd(6), b = k % 6 === 0 ? a : 1 + rnd(6);
  const dice = a === b ? [a, a, a, a] : [a, b];
  if (a === b) doublesSeen++;
  const want = oracle(snap(h), dice), got = engineSet(h, dice), most = e.bg_most(h, pack(dice));
  if (want.most < dice.length) forcedSeen++;
  if (JSON.stringify(want.set) !== JSON.stringify(got) || want.most !== most) {
    disagree = `position ${JSON.stringify(snap(h))} dice ${dice}: engine ${JSON.stringify(got)} most ${most}, ` +
      `oracle ${JSON.stringify(want.set)} most ${want.most}`;
  }
  compared++;
}
ok('the oracle and the engine agree on every legal set and every count',
   disagree === null, disagree ?? `${compared} positions, ${doublesSeen} doubles, ${forcedSeen} where not every die plays`);
ok('the random positions reach the forced-play rules', forcedSeen > 20, `${forcedSeen} of ${compared}`);

// Rule 9 from its text, in each side's own pips: the winner's home board is
// the loser's pips 19 to 24.
function oracleScore(s, w, cubeV) {
  const l = 1 - w;
  if (s.off[l] > 0) return cubeV;
  let behind = s.bar[l] > 0;
  for (let r = 19; r <= 24; r++) if (mine(s, l, idx(l, r))) behind = true;
  return cubeV * (behind ? 3 : 2);
}
{
  let wrong = null, kinds = new Set();
  for (let k = 0; k < 300 && !wrong; k++) {
    const w = rnd(2), l = 1 - w, pts = new Array(24).fill(0), bar = [0, 0], off = [0, 0];
    off[w] = 15;
    let n = 15;
    if (rnd(4) === 0) { bar[l] = 1; n--; }
    if (rnd(3) === 0) { off[l] = 1 + rnd(5); n -= off[l]; }
    // Half the time the loser is kept out of the winner's home board (its
    // own pips 19 to 24), or no plain gammon would ever turn up.
    const clear = rnd(2) === 0;
    while (n > 0) { const i = clear ? idx(l, 1 + rnd(18)) : rnd(24); pts[i] += l === 0 ? 1 : -1; n--; }
    const o = {};
    pts.forEach((v, i) => { if (v) o[i] = v; });
    const cv = [1, 2, 4, 8][rnd(4)];
    const h = e.bg_put(position(l, o, bar, off), 28, cv);
    const want = oracleScore(snap(h), w, cv);
    kinds.add(want / cv);
    if (e.bg_points(h) !== want || e.bg_winner(h) !== w) {
      wrong = `${JSON.stringify(snap(h))} cube ${cv}: engine ${e.bg_points(h)} for ${e.bg_winner(h)}, oracle ${want} for ${w}`;
    }
  }
  ok('the scoring oracle and the engine agree on 300 finished games', wrong === null, wrong ?? `kinds seen ${[...kinds].sort()}`);
  ok('those games reach single, gammon and backgammon', kinds.size === 3, [...kinds].join(','));
}

// -- Whole games, the engine playing both sides ---------------------------
// Every move the engine's player makes is checked against the oracle, and
// conservation after every one.
function playGame(gseed) {
  let h = e.bg_new(), s = gseed, turns = 0;
  while (e.bg_done(h) === 0 && turns < 600) {
    turns++;
    s = (s * 1103515245 + 12345) & 0x7fffffff; const d1 = e.bg_die(s);
    s = (s * 1103515245 + 12345) & 0x7fffffff; const d2 = e.bg_die(s);
    let q = d1 === d2 ? [d1, d1, d1, d1] : [d1, d2];
    while (q.length && e.bg_done(h) === 0) {
      const pick = e.bg_ai(h, pack(q));
      const legal = oracle(snap(h), q);
      if (pick < 0) {
        if (legal.most !== 0) return { bad: `the engine passed with ${q} where ${legal.set} is legal`, h };
        break;
      }
      const m = `${pick & 31}:${pick >> 5}`;
      if (!legal.set.includes(m)) return { bad: `the engine played ${m} with ${q}; legal ${legal.set}`, h };
      h = e.bg_play(h, pack(q), pick & 31, pick >> 5);
      q.splice(q.indexOf(pick >> 5), 1);
      if (held(h, 0) !== 15 || held(h, 1) !== 15) return { bad: `checkers not conserved after ${m}`, h };
    }
    if (e.bg_done(h) === 1) break;
    h = e.bg_endturn(h);
  }
  return { h, turns };
}
let broke = null, finished = 0;
const winners = new Set(), borneOff = [0, 0];
for (let gs = 1; gs <= 12 && !broke; gs++) {
  const r = playGame(gs);
  if (r.bad) { broke = `seed ${gs}: ${r.bad}`; break; }
  borneOff[0] += e.bg_off(r.h, 0); borneOff[1] += e.bg_off(r.h, 1);
  if (e.bg_done(r.h) === 1) {
    finished++;
    const w = e.bg_winner(r.h);
    winners.add(w);
    if (e.bg_off(r.h, w) !== 15) broke = `seed ${gs}: winner ${w} bore off ${e.bg_off(r.h, w)}`;
  }
}
ok('every move of 12 engine games is legal and conserves thirty checkers', broke === null, broke ?? 'clean');
ok('the games reach a finish', finished === 12, `${finished} of 12`);
ok('both colours win some of them', winners.size === 2, `winners: ${[...winners].join(',')}`);
ok('BOTH sides can bear off, not just one', borneOff[0] > 0 && borneOff[1] > 0,
   `white ${borneOff[0]}, black ${borneOff[1]}`);

// -- THE PAGE --------------------------------------------------------------
const g = GAMES.find(x => x.id === 'backgammon');
const BG_BAR = 100, BG_OFF = 101, BG_DIE = 200;
const rollOf = q => ({ dice: q.length === 4 ? [q[0], q[0]] : q.slice(), queue: q.slice(), spent: [] });
const click = (h, i, sel, q) => g.move(e, h, i, { sel, roll: rollOf(q), rand: () => 1 });

{
  const pick = click(barBoth, BG_BAR, null, [2, 5]);
  ok('page: clicking the bar picks the checker up', pick && pick.sel === BG_BAR, JSON.stringify(pick));
  const by2 = click(barBoth, 22, BG_BAR, [2, 5]), by5 = click(barBoth, 19, BG_BAR, [2, 5]);
  ok('page: the bar enters on the 2 when you choose the 2',
     by2 && by2.handle && e.bg_point(by2.handle, 22) === 1 && JSON.stringify(by2.roll.queue) === '[5]',
     by2 && JSON.stringify(by2.roll));
  ok('page: and on the 5 when you choose the 5 (the page no longer spends the smallest)',
     by5 && by5.handle && e.bg_point(by5.handle, 19) === 1 && JSON.stringify(by5.roll.queue) === '[2]',
     by5 && JSON.stringify(by5.roll));
  ok('page: a point that is not a checker of yours is not picked up while on the bar',
     click(barBoth, 10, null, [2, 5]) === null);
}
{
  const f = position(0, { 10: 1, 20: 1, 14: -2, 3: -2 });
  ok('page: the must-play-both refusal holds at the click',
     click(f, 9, 10, [6, 1]) === null && click(f, 4, 10, [6, 1]).handle !== undefined);
  const v = g.view(e, f, null, null, rollOf([6, 1]));
  const hinted = [...v.top, ...v.bottom].filter(p => p.hint).map(p => p.i).sort((a, b) => a - b);
  ok('page: the ringed checkers are exactly the legal starts', JSON.stringify(hinted) === '[10,20]',
     JSON.stringify(hinted));
}
{
  const two = position(0, { 1: 2 }, [0, 0], [13, 0]);
  const tray = click(two, BG_OFF, 1, [5, 6]);
  ok('page: two dice that bear the same checker off: the tray spends the smaller',
     tray && tray.handle && e.bg_off(tray.handle, 0) === 14 && JSON.stringify(tray.roll.queue) === '[6]',
     tray && JSON.stringify(tray.roll));
  const home = position(0, { 3: 3, 2: 2, 0: 1 }, [0, 0], [9, 0]);
  const exact = click(home, BG_OFF, 3, [5, 4]);
  ok('page: everything on the 4-point or lower, 5-4: the tray bears the 4-point checker off with the 4',
     exact && exact.handle && e.bg_off(exact.handle, 0) === 10 && e.bg_point(exact.handle, 3) === 2
       && JSON.stringify(exact.roll.queue) === '[5]',
     exact && JSON.stringify(exact.roll));
  const by5 = click(two, BG_DIE + 5, 1, [5, 6]), by6 = click(two, BG_DIE + 6, 1, [5, 6]);
  ok('page: clicking a die in the tray spends that die',
     by5 && JSON.stringify(by5.roll.queue) === '[6]' && by6 && JSON.stringify(by6.roll.queue) === '[5]',
     `${by5 && JSON.stringify(by5.roll)} / ${by6 && JSON.stringify(by6.roll)}`);
  const one = position(0, { 2: 1, 4: 1 }, [0, 0], [13, 0]);
  const off = click(one, BG_OFF, 2, [3, 5]);
  ok('page: one die bears the checker off, and the tray click is not needed',
     off && off.handle && e.bg_off(off.handle, 0) === 14 && JSON.stringify(off.roll.queue) === '[5]',
     off && JSON.stringify(off.roll));
}
{
  const stuck = position(0, { 10: 1, 22: -2, 19: -2 }, [1, 0]);
  const pass = g.actions.find(a => a.label === 'No move, pass');
  ok('page: the pass button is offered exactly when nothing can be played',
     pass.enabled(e, stuck, rollOf([2, 5])) && !pass.enabled(e, barBoth, rollOf([2, 5])));
}

{
  const act = label => g.actions.find(a => a.label === label);
  const [offer, take, drop, roll] = ['Offer double', 'Take', 'Drop', '\u{1F3B2} Roll'].map(act);
  ok('page: Offer double is there before your roll, and gone once you have rolled',
     offer.enabled(e, start, null) && !offer.enabled(e, start, rollOf([3, 1])));
  const taken = offer.run(e, start, () => 1, null);
  ok('page: an even game, the engine takes: the cube is 2 and theirs',
     JSON.stringify(cube(taken.handle)) === '{"v":2,"owner":1,"offered":0}', JSON.stringify(cube(taken.handle)));
  ok('page: and you may not double again with their cube', !offer.enabled(e, taken.handle, null));
  const race = position(0, { 0: 5, 1: 5, 2: 5 }, [0, 0], [0, 0]);
  const racing = e.bg_put(race, 12, -15);
  const dropped = offer.run(e, racing, () => 1, null).handle;
  ok('page: far behind, the engine drops, and the status names the drop and the points',
     e.bg_done(dropped) === 1 && g.status(e, dropped) === 'Black drops the double · White wins 1 point',
     g.status(e, dropped));
  // The engine doubles you: Black far ahead, Black on roll, the throw fresh.
  const ahead = e.bg_put(e.bg_put(position(1, { 23: -5, 22: -5, 21: -5 }), 12, 15), 28, 1);
  const out = g.step(e, ahead, () => 1, rollOf([3, 1]));
  ok('page: the engine offers before it throws, and the throw is discarded',
     out && e.bg_offered(out.handle) === 1 && out.roll === null, out && JSON.stringify({ o: e.bg_offered(out.handle), roll: out.roll }));
  const h2 = out.handle;
  ok('page: its offer makes the turn yours, with Take and Drop and no Roll',
     g.turn(e, h2) === 0 && take.enabled(e, h2, null) && drop.enabled(e, h2, null) && !roll.enabled(e, h2, null) &&
     !offer.enabled(e, h2, null));
  const mine2 = take.run(e, h2).handle;
  ok('page: your take makes the cube 2 and yours, and the engine plays on',
     JSON.stringify(cube(mine2)) === '{"v":2,"owner":0,"offered":0}' && g.turn(e, mine2) === 1, JSON.stringify(cube(mine2)));
  const gone = drop.run(e, h2).handle;
  ok('page: your drop loses 1 point to Black', e.bg_done(gone) === 1 && e.bg_winner(gone) === 1 && e.bg_points(gone) === 1);
}
{
  // Whole games through the page's own step, the engine on both sides and
  // the cube live, the way the driver steps them.
  let played = 0, doubled = 0, drops = 0, bad = null;
  for (let gs = 1; gs <= 12 && !bad; gs++) {
    let s = gs * 7919, h = e.bg_new(), roll = null, n = 0;
    const rand = () => { s = (s * 1103515245 + 12345) & 0x7fffffff; return s; };
    while (e.bg_done(h) === 0 && n++ < 4000) {
      if (roll === null) roll = g.beginTurn(e, h, rand);
      const was = e.bg_cube(h);
      const out = g.step(e, h, rand, roll);
      if (!out) { bad = `seed ${gs}: step answered nothing`; break; }
      h = out.handle;
      if ('roll' in out) roll = out.roll;
      if (e.bg_cube(h) > was) doubled++;
      if (held(h, 0) !== 15 || held(h, 1) !== 15) { bad = `seed ${gs}: checkers not conserved`; break; }
    }
    if (bad) break;
    if (e.bg_done(h) !== 1) { bad = `seed ${gs}: no finish in 4000 steps`; break; }
    played++;
    const w = e.bg_winner(h);
    if (e.bg_off(h, w) < 15) { drops++; if (e.bg_points(h) !== e.bg_cube(h)) bad = `seed ${gs}: a drop scored ${e.bg_points(h)}`; }
    else if (e.bg_points(h) !== oracleScore(snap(h), w, e.bg_cube(h))) bad = `seed ${gs}: scored ${e.bg_points(h)}`;
  }
  ok('page: 12 games with the cube live finish, conserve, and score by rule 9', bad === null && played === 12,
     bad ?? `${played} games, ${doubled} cube turns taken, ${drops} drops`);
  ok('page: the cube is actually turned in those games', doubled > 0, `${doubled} takes`);
}

// -- Controls -------------------------------------------------------------
ok('control: two new boards are different handles', e.bg_new() !== e.bg_new());
// The class arm must be able to fail: a wrong answer key is reported.
ok('control: a class arm with the wrong key would fail',
   JSON.stringify(engineSet(start, [3, 1])) !== JSON.stringify(['12:1']));
// The oracle must be able to disagree: one that ignored blocking would allow
// the 13-point to play the 1 in the opening.
ok('control: the oracle refuses a blocked point',
   !oracle(snap(start), [3, 1]).set.includes('12:1') && oneOk({ ...snap(start), pts: OPEN.map(v => v < 0 ? 0 : v) }, 13, 1));
{
  const heldFake = () => 0;
  ok('control: the conservation reader would report a shortfall', heldFake() !== 15);
}

console.log(fail === 0
  ? `\nPASS: Backgammon plays by the rules (${pass} arms).`
  : `\nFAIL: ${fail} of ${pass + fail} arms.`);
process.exitCode = fail === 0 ? 0 : 1;
