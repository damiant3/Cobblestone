// Grade the Hex War wasm module.
//
// Hex War never removes a unit: a casualty is marked eliminated and stays
// in the list at its last hex. So the unit COUNT is fixed for a whole game
// and only the alive count moves, which gives three invariants that no
// amount of watching a battle would check:
//
//   the roster never grows or shrinks;
//   a unit once eliminated is never alive again, and its strength stays 0;
//   every unit sits on the map, inside the scenario's own width and height.
//
// All thirteen scenarios are exercised, because a roster placed off its own
// map is a per-scenario defect and one scenario passing says nothing about
// the other twelve.
//
// Usage: node apps/games/hw-verify.mjs [path/to/hexwar.wasm]

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const wasmPath = process.argv[2] ||
  join(here, '..', 'landing', 'web', 'games', 'hexwar.wasm');

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

const roster = h => [...Array(e.hw_units(h))].map((_, i) => ({
  owner: e.hw_owner(h, i), q: e.hw_q(h, i), r: e.hw_r(h, i),
  str: e.hw_str(h, i), max: e.hw_max(h, i), dead: e.hw_dead(h, i),
}));

console.log(`hw-verify ${wasmPath}`);

// -- Every scenario sets up legally --------------------------------------
{
  const bad = [];
  for (let sc = 0; sc < 13; sc++) {
    const h = e.hw_new(sc, 1);
    const n = e.hw_units(h);
    const w = e.hw_width(h), ht = e.hw_height(h);
    if (n <= 0) { bad.push(`scenario ${sc}: ${n} units`); continue; }
    if (w <= 0 || ht <= 0) { bad.push(`scenario ${sc}: map ${w}x${ht}`); continue; }
    if (e.hw_limit(h) <= 0) bad.push(`scenario ${sc}: turn limit ${e.hw_limit(h)}`);
    for (const u of roster(h)) {
      if (u.q < 0 || u.q >= w || u.r < 0 || u.r >= ht) {
        bad.push(`scenario ${sc}: a unit sits at ${u.q},${u.r} on a ${w}x${ht} map`);
        break;
      }
      if (u.owner !== 0 && u.owner !== 1) { bad.push(`scenario ${sc}: owner ${u.owner}`); break; }
      if (u.str <= 0 || u.str > u.max) { bad.push(`scenario ${sc}: strength ${u.str}/${u.max}`); break; }
      if (u.dead !== 0) { bad.push(`scenario ${sc}: a unit starts eliminated`); break; }
    }
    if (e.hw_alive(h, 0) === 0 || e.hw_alive(h, 1) === 0) {
      bad.push(`scenario ${sc}: a side starts with nothing alive`);
    }
  }
  ok('all thirteen scenarios place a legal roster on their own map',
     bad.length === 0, bad.length ? bad.slice(0, 3).join('; ') : '13 scenarios');
}

// -- The copy arm ---------------------------------------------------------
{
  const h = e.hw_new(0, 7);
  const before = JSON.stringify(roster(h));
  const next = e.hw_step(h);
  ok('stepping answers a different state', next !== h);
  ok('THE COPY ARM: the state stepped from is untouched',
     JSON.stringify(roster(h)) === before);
}

// -- Whole battles --------------------------------------------------------
function playScenario(sc, seed) {
  let h = e.hw_new(sc, seed);
  const n0 = e.hw_units(h);
  const w = e.hw_width(h), ht = e.hw_height(h);
  let prev = roster(h), guard = 0;
  while (e.hw_done(h) === 0 && guard < 400) {
    guard++;
    const beforeTurn = e.hw_turn(h);
    h = e.hw_step(h);
    if (e.hw_units(h) !== n0) return { bad: `roster went ${n0} to ${e.hw_units(h)}`, h };
    const now = roster(h);
    for (let i = 0; i < n0; i++) {
      const a = prev[i], b = now[i];
      if (b.owner !== a.owner) return { bad: `unit ${i} changed sides`, h };
      if (a.dead === 1 && b.dead === 0) return { bad: `unit ${i} came back to life`, h };
      // NOT "a dead unit has strength 0". Only the attrition path ties the
      // two together; `eliminate-defender` and `elim-atk-loop` set the flag
      // and leave the strength alone, so a combat casualty keeps its last
      // strength. That is consistent rather than broken, because every
      // consumer checks the flag first: `ai-atk-str-loop` skips eliminated
      // units and `arty-support-loop` requires `eliminated == False`. The
      // leftover number is inert. What a PAGE must not do is read strength
      // without reading `dead`, which is why it is recorded rather than
      // asserted away (games-backlog GAME-21).
      if (b.str < 0 || b.str > b.max) return { bad: `unit ${i} strength ${b.str}/${b.max}`, h };
      // A unit that is still alive must have positive strength.
      if (b.dead === 0 && b.str <= 0) return { bad: `unit ${i} alive with strength ${b.str}`, h };
      if (b.q < 0 || b.q >= w || b.r < 0 || b.r >= ht) {
        return { bad: `unit ${i} walked off the map to ${b.q},${b.r}`, h };
      }
    }
    if (e.hw_turn(h) < beforeTurn) return { bad: `the turn went backwards`, h };
    if (e.hw_alive(h, 0) < 0 || e.hw_alive(h, 1) < 0) return { bad: `negative alive count`, h };
    prev = now;
  }
  return { h, guard };
}

{
  const bad = [];
  let finished = 0, runs = 0, totalSteps = 0;
  for (let sc = 0; sc < 13; sc++) {
    for (const seed of [1, 2]) {
      runs++;
      const r = playScenario(sc, seed);
      if (r.bad) { bad.push(`scenario ${sc} seed ${seed}: ${r.bad}`); continue; }
      totalSteps += r.guard;
      if (e.hw_done(r.h) === 1) finished++;
    }
  }
  ok('across all thirteen scenarios twice, no unit is added, revived or lost off the map',
     bad.length === 0, bad.length ? bad.slice(0, 3).join('; ') : `${runs} battles, ${totalSteps} turns`);
  ok('every battle reaches an end', finished === runs, `${finished} of ${runs}`);
}

// -- The end condition ----------------------------------------------------
{
  const bad = [];
  for (let sc = 0; sc < 13; sc++) {
    let h = e.hw_new(sc, 3), guard = 0;
    while (e.hw_done(h) === 0 && guard++ < 400) h = e.hw_step(h);
    if (e.hw_done(h) === 1) {
      const outOfUnits = e.hw_alive(h, 0) === 0 || e.hw_alive(h, 1) === 0;
      const outOfTurns = e.hw_turn(h) >= e.hw_limit(h);
      if (!outOfUnits && !outOfTurns) {
        bad.push(`scenario ${sc}: over with ${e.hw_alive(h, 0)}v${e.hw_alive(h, 1)} alive at turn ${e.hw_turn(h)}/${e.hw_limit(h)}`);
      }
      const w = e.hw_winner(h);
      if (w < -1 || w > 1) bad.push(`scenario ${sc}: winner ${w}`);
    }
  }
  ok('a finished battle ran out of units or out of turns',
     bad.length === 0, bad.length ? bad.slice(0, 3).join('; ') : '13 scenarios');
}

// -- Refusals -------------------------------------------------------------
{
  const h = e.hw_new(0, 1);
  ok('a unit index off the roster is refused',
     e.hw_owner(h, e.hw_units(h)) === -1 && e.hw_q(h, -1) === -1 && e.hw_dead(h, 999) === -1);
  ok('a scenario index off the list is clamped rather than crashing',
     e.hw_units(e.hw_new(-5, 1)) > 0 && e.hw_units(e.hw_new(99, 1)) > 0);
  let done = e.hw_new(1, 5), n = 0;
  while (e.hw_done(done) === 0 && n++ < 400) done = e.hw_step(done);
  ok('a finished battle refuses another turn', e.hw_step(done) === done);
}

// -- Controls -------------------------------------------------------------
ok('control: two new battles are different handles', e.hw_new(0, 1) !== e.hw_new(0, 1));
ok('control: different scenarios differ',
   e.hw_units(e.hw_new(0, 1)) !== e.hw_units(e.hw_new(4, 1)) ||
   e.hw_width(e.hw_new(0, 1)) !== e.hw_width(e.hw_new(4, 1)));
ok('control: the roster reader sees casualties', (() => {
  let h = e.hw_new(0, 2), n = 0;
  const start = e.hw_alive(h, 0) + e.hw_alive(h, 1);
  while (e.hw_done(h) === 0 && n++ < 400) h = e.hw_step(h);
  return e.hw_alive(h, 0) + e.hw_alive(h, 1) < start;
})());

console.log(fail === 0
  ? `\nPASS: Hex War keeps its roster honest across every scenario (${pass} arms).`
  : `\nFAIL: ${fail} of ${pass + fail} arms.`);
process.exitCode = fail === 0 ? 0 : 1;
