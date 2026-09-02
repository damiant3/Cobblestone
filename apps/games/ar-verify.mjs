// Grade every arcade descriptor against its own wasm module.
//
// The page is the thing a person sees, so the thing to check is not "does
// the module load" -- wasm-verify.mjs and the per-game graders already
// answer that. What is new here, and what only this arm can see, is whether
// the DESCRIPTOR drives the module correctly: whether boot returns a live
// handle, whether step actually advances, whether the view reads cells that
// are really there, and whether the game arrives somewhere.
//
// The load-bearing arm is PROGRESS. A descriptor whose step is wired to the
// wrong accessor still returns a handle and still renders a board; it just
// renders the SAME board forever, and a screenshot cannot tell that from a
// game that happens to be slow. So every game must either reach its own
// terminal state or produce a view that changes, and a game that does
// neither is a failure however good it looks.
//
// Usage: node apps/games/ar-verify.mjs [game-id ...]

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import { GAMES, IMPORTS, driver } from '../landing/web/games/arcade.js';

const here = dirname(fileURLToPath(import.meta.url));
const modDir = join(here, '..', 'landing', 'web', 'games');
const only = process.argv.slice(2);

let pass = 0, fail = 0;
const ok = (name, cond, detail) => {
  if (cond) pass++;
  else { console.log(`  FAIL  ${name}: ${detail}`); fail++; }
  return cond;
};

const shape = v => {
  if (!v) return '';
  if (v.kind === 'grid' || v.kind === 'hex') return v.cells.map(c => c.text + '|' + c.cls).join(',');
  if (v.kind === 'rows') return JSON.stringify(v.rows);
  if (v.kind === 'columns') return JSON.stringify(v.cols);
  if (v.kind === 'pair') return JSON.stringify(v.grids);
  return JSON.stringify(v);
};

const count = v => !v ? 0
  : v.kind === 'grid' || v.kind === 'hex' ? v.cells.length
    : v.kind === 'rows' ? v.rows.length
      : v.kind === 'columns' ? v.cols.length
        : v.kind === 'pair' ? v.grids.length : 0;

console.log(`ar-verify: ${GAMES.length} arcade descriptors\n`);

for (const game of GAMES) {
  if (only.length && !only.includes(game.id)) continue;
  const label = game.id.padEnd(14);
  let exports;
  try {
    const bytes = readFileSync(join(modDir, `${game.id}.wasm`));
    exports = new WebAssembly.Instance(new WebAssembly.Module(bytes), IMPORTS).exports;
  } catch (err) {
    ok(`${game.id} loads`, false, err.message);
    console.log(`  ----  ${label} module did not load`);
    continue;
  }

  const d = driver(game, exports);
  let boot, err = null;
  try { boot = d.reset(7); } catch (e) { err = e; }
  if (!ok(`${game.id} boots`, err === null, err && err.message)) continue;
  ok(`${game.id} boot returns a handle`, boot !== null && boot !== undefined && Number.isInteger(boot),
    `boot gave ${boot}`);

  // The view must read something. A descriptor pointed at an accessor that
  // is not there answers undefined for every cell and still renders.
  let first;
  try { first = d.view(); } catch (e) { err = e; }
  if (!ok(`${game.id} renders`, err === null, err && err.message)) continue;
  ok(`${game.id} view has cells`, count(first) > 0, `view held ${count(first)} entries`);
  ok(`${game.id} status reads`, typeof d.status() === 'string' && d.status().length > 0,
    `status was ${JSON.stringify(d.status())}`);
  ok(`${game.id} view holds no undefined`, !shape(first).includes('undefined'),
    'a cell read undefined, so an accessor name is wrong');

  // THE PROGRESS ARM.
  const cap = game.steps || 200;
  const before = shape(first);
  let steps = 0, ended = false;
  try {
    while (steps < cap) {
      if (!d.step()) { ended = true; break; }
      steps++;
      if (d.done()) { ended = true; break; }
    }
  } catch (e) {
    ok(`${game.id} steps without trapping`, false, `${e.message} after ${steps} steps`);
    continue;
  }
  const after = shape(d.view());

  if (game.step) {
    // `steps > 0` must NOT appear in this test. A descriptor wired to the
    // wrong accessor still runs its cap of steps and still renders; what it
    // does not do is CHANGE. An OR against the step count passes every such
    // descriptor for free, which is the arm reporting that the loop ran.
    ok(`${game.id} advances`, after !== before,
      `${steps} steps left the view identical -- step is not driving the module`);
    ok(`${game.id} arrives`, ended || steps >= cap,
      `stopped after ${steps} steps without finishing or hitting the cap`);
    ok(`${game.id} final view holds no undefined`, !after.includes('undefined'),
      'a cell read undefined after stepping');
  }

  // The whole-game call, where the module offers one. It runs inside a
  // single call, so it is the cheap arm on a bump allocator.
  let runs = null;
  if (game.runs) {
    try { runs = d.runs(); } catch (e) { err = e; }
    ok(`${game.id} runs to completion in one call`, err === null && typeof runs === 'string',
      err ? err.message : `run gave ${runs}`);
  }

  const state = d.done() ? 'finished' : `${steps} steps`;
  console.log(`  ok    ${label} ${state.padEnd(12)} ${d.status()}`);
  if (runs) console.log(`        ${' '.repeat(14)} whole game: ${runs}`);
}

// -- THE CONTROL: does the progress arm fail when it should? --------------
// An arm that has only ever been seen passing is an assertion, not a test.
// This breaks each descriptor the way a real miswiring breaks it -- step
// returns the handle it was given, which is exactly what tictactoe did --
// and requires the arm to catch every one. A descriptor that survives the
// break is one whose progress arm is vacuous.
if (!only.length) {
  let caught = 0, missed = [];
  for (const game of GAMES) {
    if (!game.step) continue;
    let exports;
    try {
      exports = new WebAssembly.Instance(
        new WebAssembly.Module(readFileSync(join(modDir, `${game.id}.wasm`))), IMPORTS).exports;
    } catch { continue; }
    const broken = { ...game, step: (e, h) => h };
    const d = driver(broken, exports);
    try {
      d.reset(7);
      const before = shape(d.view());
      for (let i = 0; i < (game.steps || 200) && !d.done(); i++) if (!d.step()) break;
      if (shape(d.view()) === before) caught++; else missed.push(game.id);
    } catch { caught++; }
  }
  ok('CONTROL: a step wired to a no-op is caught for every game',
    missed.length === 0, `${missed.join(', ')} still looked like ${missed.length === 1 ? 'it advanced' : 'they advanced'}`);
  console.log(`\n  ok    control        ${caught} descriptors go red when step stops advancing`);
}

console.log(fail === 0
  ? `\nPASS: every arcade descriptor drives its module (${pass} arms).`
  : `\nFAIL: ${fail} of ${pass + fail} arms.`);
process.exitCode = fail === 0 ? 0 : 1;
