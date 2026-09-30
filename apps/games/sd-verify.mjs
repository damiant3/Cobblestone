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

// -- THE RULES (Sudoku.codex, "The Rules") --------------------------------
//
// The oracle below is written from the rules, not from the engine: a move
// (cell, digit) is legal when the cell is on the board and not a given and
// the digit is 1..9 and no other cell of its row, column or box holds it;
// clearing (digit 0) is legal on any cell that is not a given. Positions
// are built through sd_blank / sd_put / sd_fix, which apply no rule.
const peers = (a, b) => {
  const ra = Math.floor(a / 9), ca = a % 9, rb = Math.floor(b / 9), cb = b % 9;
  return ra === rb || ca === cb
    || (Math.floor(ra / 3) === Math.floor(rb / 3) && Math.floor(ca / 3) === Math.floor(cb / 3));
};
const fixedOf = h => [...Array(81)].map((_, i) => e.sd_fixed(h, i));
function legal(g, fx, i, v) {
  if (i < 0 || i >= 81 || fx[i] === 1) return false;
  if (v === 0) return true;
  if (v < 1 || v > 9) return false;
  for (let j = 0; j < 81; j++) if (j !== i && g[j] === v && peers(i, j)) return false;
  return true;
}
const wonRules = g => g.every(v => v >= 1 && v <= 9) && violations(g).length === 0;
// An independent solution counter: plain first-empty backtracking, capped.
function countSolutions(g0, limit) {
  const g = g0.slice();
  let n = 0;
  const rec = () => {
    const i = g.indexOf(0);
    if (i < 0) { n++; return; }
    for (let v = 1; v <= 9 && n < limit; v++) {
      if (legal(g, [], i, v)) { g[i] = v; rec(); g[i] = 0; }
    }
  };
  rec();
  return n;
}
const build = (puts, fixes) => {
  let h = e.sd_blank();
  for (const [i, v] of puts) h = e.sd_put(h, i, v);
  for (const [i, v] of fixes) h = e.sd_fix(h, i, v);
  return h;
};
const placeOk = (h, i, v) => {
  const n = e.sd_place(h, i, v);
  return n !== h && e.sd_cell(n, i) === v;
};

// Rules 2 and 3, one class at a time: a 5 at r0c0, a given 7 at r4c4.
{
  const h = build([[0, 5], [1, 6]], [[40, 7]]);
  const g = grid(h), fx = fixedOf(h);
  const classes = [
    ['row conflict (r0c8 = 5)', 8, 5],
    ['column conflict (r8c0 = 5)', 72, 5],
    ['box conflict (r2c2 = 5)', 20, 5],
    ['no conflict (r8c8 = 5)', 80, 5],
    ['a given repeated in its column (r0c4 = 7)', 4, 7],
    ['a given repeated in its box (r3c3 = 7)', 30, 7],
    ['overwrite your own digit with itself (r0c0 = 5)', 0, 5],
    ['overwrite your own digit with a free one (r0c0 = 9)', 0, 9],
    ['overwrite your own digit into a conflict (r0c1 = 5)', 1, 5],
    ['clear your own digit (r0c0 = 0)', 0, 0],
    ['clear a blank (r8c8 = 0)', 80, 0],
    ['change a given (r4c4 = 1)', 40, 1],
    ['clear a given (r4c4 = 0)', 40, 0],
    ['digit off the range (r8c8 = 10)', 80, 10],
    ['digit off the range (r8c8 = -1)', 80, -1],
    ['cell off the board (81)', 81, 1],
    ['cell off the board (-1)', -1, 1],
  ];
  for (const [name, i, v] of classes) {
    const want = legal(g, fx, i, v);
    const got = placeOk(h, i, v);
    const fits = v >= 1 ? e.sd_fits(h, i, v) === 1 : got;
    ok(`rules: ${name} is ${want ? 'allowed' : 'refused'}`, got === want && fits === want,
       `place ${got}, fits ${fits}`);
  }
  ok('rules: a refused move answers the same handle', e.sd_place(h, 40, 1) === h && e.sd_place(h, 8, 5) === h);
  ok('rules: the given reads fixed and the others do not',
     e.sd_fixed(h, 40) === 1 && e.sd_fixed(h, 0) === 0 && e.sd_fixed(h, 80) === 0 && e.sd_fixed(h, 81) === -1);
}

// The whole legal set, in random positions reached through play from dealt
// puzzles at each of the arcade's three settings.
{
  const bad = [];
  let positions = 0, yes = 0, no = 0, sampled = 0, stuck = 0;
  let rnd = 12345;
  const next = () => (rnd = (rnd * 1103515245 + 12345) % 2147483648);
  for (const holes of [36, 50, 64]) {
    for (let seed = 1; seed <= 4 && !bad.length; seed++) {
      let h = e.sd_remove(e.sd_solve(e.sd_new(seed)), seed, holes);
      for (let ply = 0; ply < 25 && !bad.length; ply++) {
        const g = grid(h), fx = fixedOf(h);
        positions++;
        const moves = [];
        for (let i = 0; i < 81; i++) {
          for (let v = 1; v <= 9; v++) {
            const want = legal(g, fx, i, v);
            const got = e.sd_fits(h, i, v) === 1;
            if (want) yes++; else no++;
            if (got !== want) { bad.push(`holes ${holes} seed ${seed} ply ${ply} (${i},${v}): engine ${got}, rules ${want}`); break; }
            if (want && g[i] === 0) moves.push([i, v]);
          }
          if (bad.length) break;
        }
        // sd_place allocates, so it is sampled: 12 pairs per position, then the move.
        for (let k = 0; k < 12 && !bad.length; k++) {
          const i = next() % 83 - 1, v = next() % 11 - 1;
          sampled++;
          if (placeOk(h, i, v) !== legal(g, fx, i, v)) {
            bad.push(`holes ${holes} seed ${seed} ply ${ply}: place (${i},${v}) disagrees with the rules`);
          }
        }
        if (!moves.length) { if (!wonRules(g)) stuck++; break; }
        const [i, v] = moves[next() % moves.length];
        h = e.sd_place(h, i, v);
        if (e.sd_cell(h, i) !== v) bad.push(`a legal move (${i},${v}) was not made`);
      }
    }
  }
  ok('rules: the engine\'s legal set equals the rules\' in every position reached',
     bad.length === 0, bad.slice(0, 3).join('; ') || `${positions} positions, ${sampled} sampled places`);
  ok('control: both answers occurred, and play reached a dead end',
     yes > 0 && no > 0 && stuck > 0, `${yes} allowed, ${no} refused, ${stuck} dead ends`);
}

// Rule 5: the win is every cell filled AND all 27 constraints.
{
  const sol = grid(e.sd_solve(e.sd_new(3)));
  const full = build(sol.map((v, i) => [i, v]), []);
  const oneBlank = e.sd_put(full, 40, 0);
  // Swap two digits within row 0: the row stays a permutation, two columns break.
  const swapped = e.sd_put(e.sd_put(full, 0, sol[1]), 1, sol[0]);
  // Every cell 1..9, but row 0 repeated into row 1: rows hold, columns and boxes break.
  const rowCopy = build(sol.map((v, i) => [i, i >= 9 && i < 18 ? sol[i - 9] : v]), []);
  const cases = [
    ['a solved grid', full, true],
    ['a solved grid with one blank', oneBlank, false],
    ['a full grid with two digits swapped in a row', swapped, false],
    ['a full grid whose row 1 repeats row 0', rowCopy, false],
    ['the blank board', e.sd_blank(), false],
    // (r + c) mod 9: every row and column is one to nine, every box repeats.
    ['a Latin square whose boxes repeat', build(sol.map((_, i) => [i, (Math.floor(i / 9) + i % 9) % 9 + 1]), []), false],
  ];
  for (const [name, h, want] of cases) {
    const rules = wonRules(grid(h));
    ok(`rules: ${name} is ${want ? '' : 'not '}won`, (e.sd_won(h) === 1) === want && rules === want,
       `engine ${e.sd_won(h)}, rules ${rules}`);
  }
}

// Rules 3 and 4: a dealt puzzle's givens are fixed, and it has exactly one solution.
{
  const bad = [];
  let puzzles = 0, minGivens = 81;
  for (const holes of [36, 50, 64]) {
    for (let seed = 1; seed <= 10; seed++) {
      const p = e.sd_remove(e.sd_solve(e.sd_new(seed)), seed, holes);
      const g = grid(p), fx = fixedOf(p);
      for (let i = 0; i < 81; i++) {
        if ((g[i] !== 0) !== (fx[i] === 1)) { bad.push(`holes ${holes} seed ${seed}: cell ${i} value ${g[i]} fixed ${fx[i]}`); break; }
      }
      const n = countSolutions(g, 2);
      if (n !== 1) bad.push(`holes ${holes} seed ${seed}: ${n} solutions by the rules`);
      if (e.sd_count(p, 2) !== n) bad.push(`holes ${holes} seed ${seed}: engine counts ${e.sd_count(p, 2)}, rules ${n}`);
      const s = e.sd_solve(p);
      const sf = fixedOf(s);
      if (sf.join() !== fx.join()) bad.push(`holes ${holes} seed ${seed}: solving moved the givens`);
      if (e.sd_won(s) !== 1) bad.push(`holes ${holes} seed ${seed}: the solution is not won`);
      minGivens = Math.min(minGivens, g.filter(v => v).length);
      puzzles++;
    }
  }
  ok('rules: every dealt puzzle has exactly one solution and its givens are fixed',
     bad.length === 0, bad.slice(0, 3).join('; ') || `${puzzles} puzzles, fewest givens ${minGivens}`);

  // Control: a deadly rectangle (a,b / b,a over two rows, two columns and
  // exactly two boxes) blanked in a solved grid has exactly two solutions.
  let found = null;
  for (let seed = 1; seed <= 30 && !found; seed++) {
    const g = grid(e.sd_solve(e.sd_new(seed)));
    for (let r1 = 0; r1 < 9 && !found; r1++) for (let r2 = r1 + 1; r2 < 9 && !found; r2++) {
      const sameBand = Math.floor(r1 / 3) === Math.floor(r2 / 3);
      for (let c1 = 0; c1 < 9 && !found; c1++) for (let c2 = c1 + 1; c2 < 9 && !found; c2++) {
        const sameStack = Math.floor(c1 / 3) === Math.floor(c2 / 3);
        if (sameBand === sameStack) continue;
        const a = g[r1 * 9 + c1], b = g[r1 * 9 + c2];
        if (g[r2 * 9 + c1] === b && g[r2 * 9 + c2] === a) found = { g, cells: [r1 * 9 + c1, r1 * 9 + c2, r2 * 9 + c1, r2 * 9 + c2] };
      }
    }
  }
  if (found) {
    const holed = found.g.map((v, i) => (found.cells.includes(i) ? 0 : v));
    const h = build([], holed.map((v, i) => [i, v]).filter(([, v]) => v));
    ok('control: a deadly rectangle counts two solutions, by the rules and by the engine',
       countSolutions(holed, 3) === 2 && e.sd_count(h, 3) === 2,
       `rules ${countSolutions(holed, 3)}, engine ${e.sd_count(h, 3)}`);
  } else {
    ok('control: a deadly rectangle was found to test', false, 'none in 30 grids');
  }

  // A dead end: row 0 holds 1..8 and r1c8 holds 9, so r0c8 has no digit.
  const dead = build([[0, 1], [1, 2], [2, 3], [3, 4], [4, 5], [5, 6], [6, 7], [7, 8], [17, 9]], []);
  ok('rules: a blank with no digit left counts no solution and the solver does not win it',
     e.sd_count(dead, 2) === 0 && countSolutions(grid(dead), 2) === 0 && e.sd_won(e.sd_solve(dead)) === 0,
     `engine ${e.sd_count(dead, 2)}`);
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
