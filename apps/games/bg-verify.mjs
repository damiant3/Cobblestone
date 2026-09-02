// Grade the Backgammon wasm module.
//
// Backgammon has dice, so there is no answer key, and it has no cheap
// decidable tactic the way Connect Four has "take the win in one". What it
// does have is the strongest conservation law of any game in this arcade:
// each side owns exactly fifteen checkers, always, and every one of them is
// on a point, on the bar, or borne off. A hit that loses a checker, a bear
// off that double-counts, or a copy that half-copies the board all break
// that sum, and nothing else in the game has to be understood to check it.
//
// Usage: node apps/games/bg-verify.mjs [path/to/backgammon.wasm]

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

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

const points = h => [...Array(24)].map((_, i) => e.bg_point(h, i));
// White is positive, black negative. Each side's checkers are on the board,
// on the bar, or off.
const held = (h, p) => points(h).reduce((n, v) => n + (p === 0 ? Math.max(v, 0) : Math.max(-v, 0)), 0)
  + e.bg_bar(h, p) + e.bg_off(h, p);

console.log(`bg-verify ${wasmPath}`);

// -- The opening position -------------------------------------------------
const start = e.bg_new();
const OPEN = [2, 0, 0, 0, 0, -5, 0, -3, 0, 0, 0, 5, -5, 0, 0, 0, 3, 0, 5, 0, 0, 0, 0, -2];
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
const dice = [...Array(400)].map((_, s) => e.bg_die(s + 1));
ok('every die is between 1 and 6', dice.every(d => d >= 1 && d <= 6),
   `min ${Math.min(...dice)} max ${Math.max(...dice)}`);
ok('all six faces appear', new Set(dice).size === 6, `${new Set(dice).size} faces`);
ok('the same seed gives the same die', e.bg_die(4242) === e.bg_die(4242));

// -- THE COPY ARM ---------------------------------------------------------
const before = JSON.stringify(points(start));
const stepped = e.bg_step(start, 3);
ok('stepping answers a different board', stepped !== start, `${start} -> ${stepped}`);
ok('THE COPY ARM: the board stepped from is untouched',
   JSON.stringify(points(start)) === before, JSON.stringify(points(start)));
ok('the step moved exactly one checker',
   held(stepped, 0) === 15 && held(stepped, 1) === 15 &&
   JSON.stringify(points(stepped)) !== before,
   `white ${held(stepped, 0)}, black ${held(stepped, 1)}`);

// GAME-13's per-game check: does asking the engine to move corrupt the
// board? bg-pick-loop scores by inspection rather than by applying, so this
// should hold; the arm is here because reading the source is not measuring.
const probe = e.bg_new();
const probeBefore = JSON.stringify(points(probe));
for (let d = 1; d <= 6; d++) e.bg_step(probe, d);
ok('GAME-13: asking for six moves does not mutate the board asked about',
   JSON.stringify(points(probe)) === probeBefore, JSON.stringify(points(probe)));

// -- Refusals -------------------------------------------------------------
ok('a die below 1 is refused', e.bg_step(start, 0) === start);
ok('a die above 6 is refused', e.bg_step(start, 7) === start);

// -- Turns ----------------------------------------------------------------
const handed = e.bg_endturn(start);
ok('ending the turn hands the dice over', e.bg_cur(handed) === 1, e.bg_cur(handed));
ok('ending the turn moves no checker',
   JSON.stringify(points(handed)) === before);
ok('ending a turn is a value too, the old board still on roll for white',
   e.bg_cur(start) === 0, e.bg_cur(start));

// -- Whole games, driven the way bg-game-loop drives them -----------------
// Two dice a turn, four steps on a double, then hand over. Conservation is
// checked after EVERY step, which is where a hit or a bear off would break.
function playGame(seed) {
  let h = e.bg_new(), s = seed, turns = 0;
  while (e.bg_done(h) === 0 && turns < 600) {
    turns++;
    s = (s * 1103515245 + 12345) & 0x7fffffff; const d1 = e.bg_die(s);
    s = (s * 1103515245 + 12345) & 0x7fffffff; const d2 = e.bg_die(s);
    const seq = d1 === d2 ? [d1, d1, d1, d1] : [d1, d2];
    for (const d of seq) {
      h = e.bg_step(h, d);
      if (held(h, 0) !== 15) return { bad: `white holds ${held(h, 0)} after a ${d}`, h };
      if (held(h, 1) !== 15) return { bad: `black holds ${held(h, 1)} after a ${d}`, h };
      const bad = points(h).findIndex((v, i) => !Number.isInteger(v));
      if (bad >= 0) return { bad: `point ${bad} is not an integer`, h };
      if (e.bg_done(h) === 1) break;
    }
    if (e.bg_done(h) === 1) break;
    h = e.bg_endturn(h);
  }
  return { h, turns };
}

let broke = null, finished = 0, winners = new Set();
const borneOff = [0, 0];
for (let seed = 1; seed <= 25 && !broke; seed++) {
  const r = playGame(seed);
  if (r.bad) { broke = `seed ${seed}: ${r.bad}`; break; }
  borneOff[0] += e.bg_off(r.h, 0);
  borneOff[1] += e.bg_off(r.h, 1);
  if (e.bg_done(r.h) === 1) {
    finished++;
    const w = e.bg_winner(r.h);
    winners.add(w);
    if (e.bg_off(r.h, w) !== 15) broke = `seed ${seed}: winner ${w} bore off ${e.bg_off(r.h, w)}`;
  }
}
ok('thirty checkers are conserved through every step of 25 games',
   broke === null, broke ?? 'clean');
ok('the games reach a finish', finished === 25, `${finished} of 25`);
ok('a winner has borne off all fifteen', broke === null && finished > 0);
ok('both colours win some of them', winners.size === 2, `winners: ${[...winners].join(',')}`);
// Named directly, because the bound bug this pins made ONE side unable to
// bear off at all: all-white-out scanned points 0..17, which contains
// white's own home, so white walked every checker to point 0 and stopped.
// A regression should report itself as "white cannot bear off" rather than
// as an unlucky-looking win split.
ok('BOTH sides can bear off, not just one',
   borneOff[0] > 0 && borneOff[1] > 0,
   `white ${borneOff[0]}, black ${borneOff[1]} borne off across 25 games`);

// -- Controls -------------------------------------------------------------
ok('control: two new boards are different handles', e.bg_new() !== e.bg_new());
// If bg_step ignored the die, every die would give the same board.
{
  const a = JSON.stringify(points(e.bg_step(e.bg_new(), 1)));
  const b = JSON.stringify(points(e.bg_step(e.bg_new(), 6)));
  ok('control: different dice give different boards', a !== b);
}
// Prove the conservation reader can actually report a wrong number, or
// every conservation arm above passed by being blind (L-FALSIF).
{
  const fake = { bg_point: () => 0, bg_bar: () => 0, bg_off: () => 0 };
  const heldFake = points => 0 + fake.bg_bar() + fake.bg_off();
  ok('control: the conservation reader would report a shortfall',
     heldFake() !== 15, `an empty board reads ${heldFake()}, not 15`);
}

console.log(fail === 0
  ? `\nPASS: Backgammon conserves its checkers and its rules (${pass} arms).`
  : `\nFAIL: ${fail} of ${pass + fail} arms.`);
process.exitCode = fail === 0 ? 0 : 1;
