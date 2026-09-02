// Grade the Mancala wasm module.
//
// Mancala is the one game in this arcade with a real game-tree search:
// `minimax` recurses through `mm-max` and `mm-min` with alpha-beta pruning,
// and it explores by APPLYING moves. That is the shape that corrupted
// Connect Four, so the first arm here is GAME-13's, and it is not a
// formality: before the fix, asking for a move on the OPENING position
// returned a board with every pit empty and all forty-eight seeds sitting
// in the two stores, because the search had played whole games into the
// caller's board. The seed count stayed 48 throughout, which is exactly why
// a conservation check could not see it.
//
// Beside it, the conservation law: forty-eight seeds, always, and a sowing
// never creates or destroys one.
//
// Usage: node apps/games/mc-verify.mjs [path/to/mancala.wasm]

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const wasmPath = process.argv[2] ||
  join(here, '..', 'landing', 'web', 'games', 'mancala.wasm');

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

const pits = h => [...Array(14)].map((_, i) => e.mc_pit(h, i));
const seeds = h => pits(h).reduce((a, b) => a + b, 0);
const OPENING = [4, 4, 4, 4, 4, 4, 0, 4, 4, 4, 4, 4, 4, 0];

console.log(`mc-verify ${wasmPath}`);

// -- The opening ----------------------------------------------------------
const s0 = e.mc_new();
ok('the opening is four seeds in each of the twelve pits',
   JSON.stringify(pits(s0)) === JSON.stringify(OPENING), JSON.stringify(pits(s0)));
ok('forty-eight seeds on the board', seeds(s0) === 48, seeds(s0));
ok('both stores are empty', e.mc_south(s0) === 0 && e.mc_north(s0) === 0);
ok('south to move, not over', e.mc_turn(s0) === 0 && e.mc_done(s0) === 0);
ok('south may play its own six pits and not the north ones',
   [0, 1, 2, 3, 4, 5].every(p => e.mc_legal(s0, p) === 1) &&
   [6, 7, 8, 9, 10, 11, 12, 13].every(p => e.mc_legal(s0, p) === 0));

// -- THE GAME-13 ARM ------------------------------------------------------
{
  const before = JSON.stringify(pits(s0));
  for (let d = 1; d <= 5; d++) e.mc_ai(s0, d);
  ok('GAME-13: the alpha-beta search leaves the board it searches alone',
     JSON.stringify(pits(s0)) === before, JSON.stringify(pits(s0)));
  // And on a mid-game position too, not only the opening.
  let mid = s0;
  for (const p of [2, 7, 0, 8]) if (e.mc_legal(mid, p) === 1) mid = e.mc_move(mid, p);
  const midBefore = JSON.stringify(pits(mid));
  for (let d = 1; d <= 5; d++) e.mc_ai(mid, d);
  ok('and leaves a mid-game position alone as well',
     JSON.stringify(pits(mid)) === midBefore, JSON.stringify(pits(mid)));
}

// -- The copy arm ---------------------------------------------------------
{
  const before = JSON.stringify(pits(s0));
  const next = e.mc_move(s0, 2);
  ok('moving answers a different board', next !== s0);
  ok('THE COPY ARM: the board moved from is untouched', JSON.stringify(pits(s0)) === before);
  ok('the chosen pit is emptied', e.mc_pit(next, 2) === 0, e.mc_pit(next, 2));
  ok('the seeds are still all there', seeds(next) === 48, seeds(next));
}

// -- Whole games ----------------------------------------------------------
function playGame(pick) {
  let h = e.mc_new(), plies = 0;
  const problems = [];
  while (e.mc_done(h) === 0 && plies < 300) {
    const legal = [...Array(14)].map((_, i) => i).filter(i => e.mc_legal(h, i) === 1);
    if (legal.length === 0) break;
    const turn = e.mc_turn(h);
    // A player may only ever sow from their own side.
    for (const p of legal) {
      const mine = turn === 0 ? (p >= 0 && p <= 5) : (p >= 7 && p <= 12);
      if (!mine) problems.push(`ply ${plies}: pit ${p} is legal for player ${turn}`);
      if (e.mc_pit(h, p) === 0) problems.push(`ply ${plies}: empty pit ${p} is legal`);
    }
    const chosen = pick(h, legal);
    const before = pits(h);
    h = e.mc_move(h, chosen);
    const after = pits(h);
    if (after.reduce((a, b) => a + b, 0) !== 48) {
      problems.push(`ply ${plies}: ${after.reduce((a, b) => a + b, 0)} seeds after sowing pit ${chosen}`);
    }
    if (before[chosen] !== 0 && after[chosen] !== 0 && after[chosen] >= before[chosen]) {
      // Sowing all the way round can put one back, but never more than it had.
      problems.push(`ply ${plies}: pit ${chosen} went ${before[chosen]} to ${after[chosen]}`);
    }
    // Stores never shrink.
    if (e.mc_south(h) < before[6] || e.mc_north(h) < before[13]) {
      problems.push(`ply ${plies}: a store lost seeds`);
    }
    if (after.some(v => v < 0)) problems.push(`ply ${plies}: a pit went negative`);
    plies++;
    if (problems.length > 3) break;
  }
  return { h, plies, problems };
}

{
  const all = [];
  let finished = 0, totalPlies = 0;
  for (let run = 0; run < 8; run++) {
    let s = run * 5011 + 17;
    const pick = run % 2 === 0
      ? (h => e.mc_ai(h, 3))
      : ((h, legal) => { s = (s * 1103515245 + 12345) & 0x7fffffff; return legal[s % legal.length]; });
    const r = playGame(pick);
    all.push(...r.problems.map(p => `run ${run} ${p}`));
    totalPlies += r.plies;
    if (e.mc_done(r.h) === 1) finished++;
  }
  ok('forty-eight seeds survive every sowing, and only own pits are legal',
     all.length === 0, all.length ? all.slice(0, 3).join('; ') : `${totalPlies} plies`);
  ok('the games end', finished === 8, `${finished} of 8`);
  ok('control: enough plies to mean something', totalPlies > 100, totalPlies);
}

// -- A finished game has swept both sides --------------------------------
{
  const bad = [];
  for (let run = 0; run < 8; run++) {
    let h = e.mc_new(), n = 0;
    while (e.mc_done(h) === 0 && n++ < 300) {
      const d = e.mc_ai(h, 3);
      if (d < 0) break;
      h = e.mc_move(h, d);
    }
    if (e.mc_done(h) === 1) {
      const p = pits(h);
      const southSide = p.slice(0, 6).reduce((a, b) => a + b, 0);
      const northSide = p.slice(7, 13).reduce((a, b) => a + b, 0);
      if (southSide !== 0 && northSide !== 0) {
        bad.push(`run ${run}: over with ${southSide} and ${northSide} still on the sides`);
      }
      if (e.mc_south(h) + e.mc_north(h) !== 48) {
        bad.push(`run ${run}: stores hold ${e.mc_south(h)} + ${e.mc_north(h)}`);
      }
    }
  }
  ok('a finished game has swept every seed into the two stores',
     bad.length === 0, bad.length ? bad.slice(0, 3).join('; ') : '8 games');
}

// -- Refusals -------------------------------------------------------------
ok('a pit off the board is refused',
   e.mc_move(s0, 14) === s0 && e.mc_move(s0, -1) === s0 && e.mc_pit(s0, 14) === -1);
ok('a store is not a legal move', e.mc_legal(s0, 6) === 0 && e.mc_legal(s0, 13) === 0);
ok('an empty pit is refused', (() => {
  const h = e.mc_move(s0, 2);
  return e.mc_legal(h, 2) === 0 || e.mc_turn(h) !== 0;
})());

// -- Controls -------------------------------------------------------------
ok('control: two new boards are different handles', e.mc_new() !== e.mc_new());
ok('control: different pits give different boards',
   JSON.stringify(pits(e.mc_move(e.mc_new(), 0))) !==
   JSON.stringify(pits(e.mc_move(e.mc_new(), 1))));
ok('control: a deeper search may choose differently, so depth is read',
   typeof e.mc_ai(s0, 1) === 'number' && e.mc_ai(s0, 1) >= 0 && e.mc_ai(s0, 5) >= 0,
   `depth 1 picks ${e.mc_ai(s0, 1)}, depth 5 picks ${e.mc_ai(s0, 5)}`);

console.log(fail === 0
  ? `\nPASS: Mancala searches without disturbing the board (${pass} arms).`
  : `\nFAIL: ${fail} of ${pass + fail} arms.`);
process.exitCode = fail === 0 ? 0 : 1;
