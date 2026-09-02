// Grade the 2048 wasm module.
//
// 2048 has an unusually exact invariant, and it is the whole point of this
// file. Merging preserves the sum: two 2s become a 4. Sliding preserves it
// too. The only thing that ever adds to the board is the tile that appears
// after a move, which is a 2 or a 4. So across any accepted move the grid
// sum must rise by EXACTLY 2 or 4, and across a refused one it must not
// move at all. A merge that doubled the wrong tile, dropped a tile, or
// merged a tile twice in one slide all break that arithmetic, and none of
// them is visible from watching the board.
//
// Every tile must also be a power of two, which catches a merge that added
// instead of doubling.
//
// Usage: node apps/games/g2-verify.mjs [path/to/game2048.wasm]

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const wasmPath = process.argv[2] ||
  join(here, '..', 'landing', 'web', 'games', 'game2048.wasm');

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

const grid = h => [...Array(16)].map((_, i) => e.g2_cell(h, i));
const sum = h => grid(h).reduce((a, b) => a + b, 0);
const isPow2 = v => v === 0 || (v >= 2 && (v & (v - 1)) === 0);

console.log(`g2-verify ${wasmPath}`);

// -- The opening board ----------------------------------------------------
const s0 = e.g2_new(9);
ok('a new board has exactly two tiles',
   grid(s0).filter(v => v !== 0).length === 2, JSON.stringify(grid(s0)));
ok('the opening tiles are 2s or 4s',
   grid(s0).filter(v => v !== 0).every(v => v === 2 || v === 4), JSON.stringify(grid(s0)));
ok('fourteen cells are empty', e.g2_empty(s0) === 14, e.g2_empty(s0));
ok('no moves made, not over', e.g2_moves(s0) === 0 && e.g2_done(s0) === 0);
ok('the reported sum matches the board', e.g2_sum(s0) === sum(s0), `${e.g2_sum(s0)} / ${sum(s0)}`);

// -- The copy arm ---------------------------------------------------------
{
  const before = JSON.stringify(grid(s0));
  let moved = null;
  for (let d = 0; d < 4 && !moved; d++) if (e.g2_can(s0, d) === 1) moved = e.g2_move(s0, d);
  ok('some direction is playable from the opening', moved !== null);
  ok('moving answers a different state', moved !== s0);
  ok('THE COPY ARM: the board moved from is untouched',
     JSON.stringify(grid(s0)) === before, JSON.stringify(grid(s0)));
  // g2_can must not disturb the board either: it slides a copy to decide.
  for (let d = 0; d < 4; d++) e.g2_can(s0, d);
  ok('asking whether a direction is playable leaves the board alone',
     JSON.stringify(grid(s0)) === before);
}

// -- The sum arithmetic, over real games ---------------------------------
{
  const bad = [];
  let totalMoves = 0, best = 0, finished = 0;
  for (let seed = 1; seed <= 25; seed++) {
    let h = e.g2_new(seed), guard = 0;
    while (e.g2_done(h) === 0 && guard < 2000) {
      guard++;
      const dir = e.g2_ai(h);
      if (dir < 0) break;
      const beforeSum = sum(h), beforeGrid = JSON.stringify(grid(h));
      const beforeMoves = e.g2_moves(h);
      const can = e.g2_can(h, dir) === 1;
      const next = e.g2_move(h, dir);
      if (!can) {
        // A refused direction must change nothing at all.
        if (next !== h) bad.push(`seed ${seed}: a refused direction answered a new state`);
        break;
      }
      const gained = sum(next) - beforeSum;
      if (gained !== 2 && gained !== 4) {
        bad.push(`seed ${seed} move ${beforeMoves}: sum rose by ${gained}, not 2 or 4`);
      }
      if (e.g2_moves(next) !== beforeMoves + 1) {
        bad.push(`seed ${seed}: move counter went ${beforeMoves} to ${e.g2_moves(next)}`);
      }
      if (e.g2_sum(next) !== sum(next)) {
        bad.push(`seed ${seed}: reported sum ${e.g2_sum(next)} against ${sum(next)}`);
      }
      if (!grid(next).every(isPow2)) {
        bad.push(`seed ${seed}: a tile is not a power of two: ${JSON.stringify(grid(next))}`);
      }
      if (JSON.stringify(grid(next)) === beforeGrid) {
        bad.push(`seed ${seed}: an accepted move changed nothing`);
      }
      h = next;
      totalMoves++;
      if (bad.length > 3) break;
    }
    if (e.g2_done(h) === 1) finished++;
    if (e.g2_max(h) > best) best = e.g2_max(h);
  }
  ok('every accepted move raises the sum by exactly one new tile',
     bad.length === 0, bad.length ? bad.slice(0, 3).join('; ') : `${totalMoves} moves`);
  ok('the games end', finished === 25, `${finished} of 25`);
  ok('control: the games got somewhere', best >= 64, `best tile ${best}`);
  ok('control: enough moves to mean something', totalMoves > 500, totalMoves);
}

// -- A finished board really is stuck -------------------------------------
{
  const bad = [];
  for (let seed = 1; seed <= 10; seed++) {
    let h = e.g2_new(seed), guard = 0;
    while (e.g2_done(h) === 0 && guard++ < 2000) {
      const d = e.g2_ai(h);
      if (d < 0) break;
      h = e.g2_move(h, d);
    }
    if (e.g2_done(h) === 1) {
      if (e.g2_empty(h) !== 0) bad.push(`seed ${seed}: over with ${e.g2_empty(h)} empty cells`);
      for (let d = 0; d < 4; d++) {
        if (e.g2_can(h, d) === 1) bad.push(`seed ${seed}: over but direction ${d} is playable`);
      }
    }
  }
  ok('a finished board is full and has no playable direction',
     bad.length === 0, bad.length ? bad.slice(0, 3).join('; ') : '10 games');
}

// -- Refusals -------------------------------------------------------------
ok('a direction off the compass is refused',
   e.g2_move(s0, 4) === s0 && e.g2_move(s0, -1) === s0 &&
   e.g2_can(s0, 4) === 0 && e.g2_can(s0, -1) === 0);
ok('a cell off the grid is refused', e.g2_cell(s0, 16) === -1 && e.g2_cell(s0, -1) === -1);

// -- Controls -------------------------------------------------------------
ok('control: two new boards are different handles', e.g2_new(1) !== e.g2_new(1));
ok('control: different seeds start differently',
   JSON.stringify(grid(e.g2_new(1))) !== JSON.stringify(grid(e.g2_new(2))));
ok('control: the same seed starts the same',
   JSON.stringify(grid(e.g2_new(4))) === JSON.stringify(grid(e.g2_new(4))));
// L-FALSIF: the power-of-two reader must reject something.
ok('control: the tile reader rejects a non power of two', !isPow2(6) && isPow2(8));

console.log(fail === 0
  ? `\nPASS: 2048 merges without losing or inventing a tile (${pass} arms).`
  : `\nFAIL: ${fail} of ${pass + fail} arms.`);
process.exitCode = fail === 0 ? 0 : 1;
