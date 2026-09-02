// Grade the Sudoku wasm module.
//
// A solved Sudoku is checkable without any agreement about how it was
// solved: every row, every column and every box must be a permutation of
// one to nine. That is 27 constraints and it admits no argument, which
// makes it the right arm for a solver -- an "iterations" count says the
// solver worked hard, not that it was right.
//
// The second arm is that the solution must AGREE WITH THE PUZZLE: every
// given cell keeps its value. A solver that clears a given and fills the
// grid some other way produces a perfectly valid Sudoku that is not the
// answer to the question, and the 27 constraints cannot see it.
//
// Usage: node apps/games/sd-verify.mjs [path/to/sudoku.wasm]

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const wasmPath = process.argv[2] ||
  join(here, '..', 'landing', 'web', 'games', 'sudoku.wasm');

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

const grid = h => [...Array(81)].map((_, i) => e.sd_cell(h, i));
const cell = (g, r, c) => g[r * 9 + c];
const isPerm = xs => xs.length === 9 && new Set(xs).size === 9 && xs.every(v => v >= 1 && v <= 9);

// The 27 constraints, independently.
function violations(g) {
  const bad = [];
  for (let r = 0; r < 9; r++) {
    const row = [...Array(9)].map((_, c) => cell(g, r, c));
    if (!isPerm(row)) bad.push(`row ${r}: ${row.join('')}`);
  }
  for (let c = 0; c < 9; c++) {
    const colv = [...Array(9)].map((_, r) => cell(g, r, c));
    if (!isPerm(colv)) bad.push(`col ${c}: ${colv.join('')}`);
  }
  for (let b = 0; b < 9; b++) {
    const br = Math.floor(b / 3) * 3, bc = (b % 3) * 3;
    const box = [];
    for (let i = 0; i < 9; i++) box.push(cell(g, br + Math.floor(i / 3), bc + (i % 3)));
    if (!isPerm(box)) bad.push(`box ${b}: ${box.join('')}`);
  }
  return bad;
}

// The engine's own validity question, independently: may `val` go at
// (row,col) without repeating in that row, column or box?
function canPlace(g, row, col, val) {
  for (let c = 0; c < 9; c++) if (cell(g, row, c) === val) return false;
  for (let r = 0; r < 9; r++) if (cell(g, r, col) === val) return false;
  const br = row - (row % 3), bc = col - (col % 3);
  for (let i = 0; i < 9; i++) {
    if (cell(g, br + Math.floor(i / 3), bc + (i % 3)) === val) return false;
  }
  return true;
}

console.log(`sd-verify ${wasmPath}`);

// -- The seeded grid ------------------------------------------------------
{
  const bad = [];
  const seeded = new Set();
  for (let seed = 1; seed <= 20; seed++) {
    const h = e.sd_new(seed);
    const g = grid(h);
    if (g.some(v => v < 0 || v > 9)) bad.push(`seed ${seed}: a cell is off the range`);
    // The three diagonal boxes are filled, the rest empty: 27 givens.
    const given = g.filter(v => v !== 0).length;
    if (given !== 27) bad.push(`seed ${seed}: ${given} cells filled, expected 27`);
    if (e.sd_givens(h) !== given) bad.push(`seed ${seed}: sd_givens says ${e.sd_givens(h)}`);
    for (const b of [0, 4, 8]) {
      const br = Math.floor(b / 3) * 3, bc = (b % 3) * 3;
      const box = [];
      for (let i = 0; i < 9; i++) box.push(cell(g, br + Math.floor(i / 3), bc + (i % 3)));
      if (!isPerm(box)) bad.push(`seed ${seed}: diagonal box ${b} is ${box.join('')}`);
    }
    seeded.add(g.join(''));
  }
  ok('a new grid fills the three diagonal boxes with one to nine and nothing else',
     bad.length === 0, bad.slice(0, 3).join('; ') || '20 grids');
  ok('the grid depends on the seed', seeded.size > 1, `${seeded.size} distinct from 20 seeds`);
  ok('a cell off the grid reads -1', e.sd_cell(e.sd_new(1), 81) === -1 && e.sd_cell(e.sd_new(1), -1) === -1);
}

// -- Validity agrees with the constraints --------------------------------
{
  const bad = [];
  let checked = 0, yes = 0, no = 0;
  for (let seed = 1; seed <= 10; seed++) {
    const h = e.sd_new(seed);
    const g = grid(h);
    for (let r = 0; r < 9; r++) {
      for (let c = 0; c < 9; c++) {
        for (let v = 1; v <= 9; v++) {
          const want = canPlace(g, r, c, v) ? 1 : 0;
          const got = e.sd_valid(h, r, c, v);
          checked++;
          if (want) yes++; else no++;
          if (got !== want) { bad.push(`seed ${seed} (${r},${c})=${v}: engine ${got}, rules ${want}`); break; }
        }
        if (bad.length) break;
      }
      if (bad.length) break;
    }
  }
  ok('validity agrees with the row, column and box constraints everywhere',
     bad.length === 0, bad.slice(0, 3).join('; ') || `${checked} placements`);
  ok('control: both answers occurred', yes > 0 && no > 0, `${yes} allowed, ${no} refused`);
  ok('an argument off the board reads -1',
     e.sd_valid(e.sd_new(1), 9, 0, 1) === -1 && e.sd_valid(e.sd_new(1), 0, 0, 10) === -1 &&
     e.sd_valid(e.sd_new(1), 0, 0, 0) === -1);
}

// -- THE SOLVER: 27 constraints, and the copy ----------------------------
{
  const bad = [];
  let solved = 0;
  for (let seed = 1; seed <= 20; seed++) {
    const h = e.sd_new(seed);
    const before = grid(h).join('');
    const s = e.sd_solve(h);
    if (grid(h).join('') !== before) { bad.push(`seed ${seed}: THE COPY ARM, the puzzle was modified`); break; }
    const g = grid(s);
    if (e.sd_empty(s) >= 0) { bad.push(`seed ${seed}: the solution has an empty cell`); continue; }
    const v = violations(g);
    if (v.length) { bad.push(`seed ${seed}: ${v.slice(0, 2).join('; ')}`); continue; }
    // The solution must agree with the seeded cells.
    const p = grid(h);
    for (let i = 0; i < 81; i++) {
      if (p[i] !== 0 && p[i] !== g[i]) { bad.push(`seed ${seed}: cell ${i} was ${p[i]}, solved to ${g[i]}`); break; }
    }
    if (e.sd_iters(s) <= 0) bad.push(`seed ${seed}: solved in ${e.sd_iters(s)} iterations`);
    solved++;
  }
  ok('every solved grid satisfies all twenty-seven constraints and keeps its givens',
     bad.length === 0, bad.slice(0, 3).join('; ') || `${solved} grids`);
  ok('control: the solver actually solved them', solved === 20, `${solved} of 20`);
}

// -- Removing cells makes a puzzle that still solves ----------------------
{
  const bad = [];
  let puzzles = 0;
  for (let seed = 1; seed <= 15; seed++) {
    const full = e.sd_solve(e.sd_new(seed));
    const solution = grid(full).join('');
    const puzzle = e.sd_remove(full, seed * 3 + 1, 40);
    if (grid(full).join('') !== solution) { bad.push(`seed ${seed}: THE COPY ARM, removal modified the solution`); break; }
    const p = grid(puzzle);
    const givens = p.filter(v => v !== 0).length;
    if (e.sd_givens(puzzle) !== givens) bad.push(`seed ${seed}: sd_givens disagrees with the grid`);
    if (givens >= 81) bad.push(`seed ${seed}: nothing was removed`);
    if (givens < 41) bad.push(`seed ${seed}: ${givens} givens, more than 40 removed`);
    // Every remaining given must match the solution it came from.
    const sol = grid(full);
    for (let i = 0; i < 81; i++) {
      if (p[i] !== 0 && p[i] !== sol[i]) { bad.push(`seed ${seed}: given ${i} does not match its solution`); break; }
    }
    // And the puzzle must solve back to a valid grid.
    const again = e.sd_solve(puzzle);
    if (e.sd_empty(again) >= 0) bad.push(`seed ${seed}: the puzzle did not solve`);
    else {
      const v = violations(grid(again));
      if (v.length) bad.push(`seed ${seed}: re-solved grid breaks ${v[0]}`);
      const g2 = grid(again);
      for (let i = 0; i < 81; i++) {
        if (p[i] !== 0 && p[i] !== g2[i]) { bad.push(`seed ${seed}: re-solve changed given ${i}`); break; }
      }
    }
    puzzles++;
  }
  ok('a puzzle keeps its solution\'s digits and solves back to a valid grid',
     bad.length === 0, bad.slice(0, 3).join('; ') || `${puzzles} puzzles`);
  ok('control: removal removed something and left a solvable puzzle', puzzles === 15, `${puzzles} of 15`);
}

// -- The pinned run -------------------------------------------------------
{
  const r = e.sd_run();
  ok('the pinned run solves', e.sd_rsolved(r) === 1);
  ok('the pinned run reports the givens and iterations classic-games-run pins',
     e.sd_rgivens(r) === 48 && e.sd_riters(r) === 921,
     `givens=${e.sd_rgivens(r)} iterations=${e.sd_riters(r)}`);
}

console.log(fail === 0
  ? `\nPASS: Sudoku solves to a grid that satisfies every constraint (${pass} arms).`
  : `\nFAIL: ${fail} of ${pass + fail} arms.`);
process.exitCode = fail === 0 ? 0 : 1;
