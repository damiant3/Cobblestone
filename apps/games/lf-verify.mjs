// Grade the Conway's Life wasm module.
//
// Life is the one game here whose rules are fully decidable in a few lines,
// so this file computes the next generation itself and requires the engine
// to agree cell for cell, on a twenty by twenty torus, for many generations
// from many starts. That is a complete oracle rather than a sample of one.
//
// Beside it sit three exact statements about Conway's rules that hold
// whatever the implementation:
//
//   a block never changes;
//   a blinker returns to itself after two generations;
//   a glider returns to its own shape after four, displaced by one row and
//   one column, which is the arm that catches a wrap or an index that is
//   subtly off, because it is the only one that moves.
//
// Usage: node apps/games/lf-verify.mjs [path/to/life.wasm]

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const wasmPath = process.argv[2] ||
  join(here, '..', 'landing', 'web', 'games', 'life.wasm');

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

const N = 20;
const grid = h => [...Array(N)].map((_, r) => [...Array(N)].map((_, c) => e.lf_cell(h, r, c)));
const live = g => g.flat().filter(v => v === 1).length;
const show = g => g.map(r => r.join('')).join('/');

// The oracle: Conway's rules on a torus.
function nextGen(g) {
  const out = [];
  for (let r = 0; r < N; r++) {
    const row = [];
    for (let c = 0; c < N; c++) {
      let n = 0;
      for (let dr = -1; dr <= 1; dr++) {
        for (let dc = -1; dc <= 1; dc++) {
          if (dr === 0 && dc === 0) continue;
          n += g[(r + dr + N) % N][(c + dc + N) % N];
        }
      }
      row.push(g[r][c] === 1 ? (n === 2 || n === 3 ? 1 : 0) : (n === 3 ? 1 : 0));
    }
    out.push(row);
  }
  return out;
}

console.log(`lf-verify ${wasmPath}`);

// -- The blank grid -------------------------------------------------------
const blank = e.lf_blank();
ok('a blank grid is four hundred dead cells', live(grid(blank)) === 0 && e.lf_alive(blank) === 0);
ok('a blank grid stays blank', live(grid(e.lf_step(blank))) === 0);

// -- Neighbour counting on the torus -------------------------------------
{
  const b = e.lf_place(blank, 2, 0, 0);          // a block at the origin
  ok('a block places four cells', live(grid(b)) === 4, live(grid(b)));
  // The cell diagonally off the corner wraps to see the block's far corner.
  ok('neighbour counting wraps around the torus',
     e.lf_nbrs(b, 19, 19) === 1, e.lf_nbrs(b, 19, 19));
}

// -- The three exact patterns --------------------------------------------
{
  const b = e.lf_place(blank, 2, 5, 5);
  const b1 = e.lf_step(b);
  ok('a block is a still life', show(grid(b1)) === show(grid(b)), live(grid(b1)));
}
{
  const bl = e.lf_place(blank, 1, 8, 8);
  const g1 = e.lf_step(bl), g2 = e.lf_step(g1);
  ok('a blinker changes after one generation', show(grid(g1)) !== show(grid(bl)));
  ok('a blinker returns after two', show(grid(g2)) === show(grid(bl)),
     `${live(grid(bl))} then ${live(grid(g1))} then ${live(grid(g2))}`);
}
{
  const gl = e.lf_place(blank, 0, 4, 4);
  ok('a glider places five cells', live(grid(gl)) === 5, live(grid(gl)));
  let h = gl;
  for (let i = 0; i < 4; i++) h = e.lf_step(h);
  const before = grid(gl), after = grid(h);
  // After four generations the glider is the same shape one row and one
  // column on. Compare with that offset applied.
  let same = true;
  for (let r = 0; r < N && same; r++) {
    for (let c = 0; c < N; c++) {
      if (before[r][c] !== after[(r + 1) % N][(c + 1) % N]) { same = false; break; }
    }
  }
  ok('a glider travels one row and one column every four generations',
     same, `${live(before)} cells then ${live(after)}`);
}

// -- The full oracle, over many generations ------------------------------
{
  const bad = [];
  let gens = 0;
  for (let seed = 1; seed <= 12; seed++) {
    let h = e.lf_new(seed);
    let g = grid(h);
    for (let step = 0; step < 30; step++) {
      const want = nextGen(g);
      h = e.lf_step(h);
      const got = grid(h);
      gens++;
      if (show(got) !== show(want)) {
        // Name the first cell that differs rather than dumping the board.
        let where = '';
        for (let r = 0; r < N && !where; r++) {
          for (let c = 0; c < N; c++) {
            if (got[r][c] !== want[r][c]) { where = `${r},${c}: got ${got[r][c]} want ${want[r][c]}`; break; }
          }
        }
        bad.push(`seed ${seed} gen ${step + 1} at ${where}`);
        break;
      }
      if (e.lf_alive(h) !== live(got)) {
        bad.push(`seed ${seed} gen ${step + 1}: counter ${e.lf_alive(h)} against ${live(got)}`);
        break;
      }
      g = got;
    }
  }
  ok('the engine matches Conway generation for generation, cell for cell',
     bad.length === 0, bad.length ? bad.slice(0, 2).join('; ') : `${gens} generations`);
  ok('control: enough generations to mean something', gens >= 300, gens);
}

// -- The copy arm ---------------------------------------------------------
{
  const base = e.lf_place(blank, 0, 10, 10);
  const before = show(grid(base));
  const stepped = e.lf_step(base);
  ok('stepping answers a different state', stepped !== base);
  ok('THE COPY ARM: the generation stepped from is untouched',
     show(grid(base)) === before);
  const placed = e.lf_place(base, 2, 2, 2);
  ok('placing answers a different state and leaves the old one alone',
     placed !== base && show(grid(base)) === before);
}

// -- Refusals -------------------------------------------------------------
ok('a cell off the grid is refused',
   e.lf_cell(blank, 20, 0) === -1 && e.lf_cell(blank, 0, -1) === -1);
ok('a placement off the grid is refused',
   e.lf_place(blank, 0, 20, 0) === blank && e.lf_place(blank, 0, 0, -1) === blank);
ok('an unknown pattern places nothing',
   live(grid(e.lf_place(blank, 9, 5, 5))) === 0);

// -- Controls -------------------------------------------------------------
ok('control: two seeded grids are different handles', e.lf_new(1) !== e.lf_new(1));
ok('control: different seeds scatter differently',
   show(grid(e.lf_new(1))) !== show(grid(e.lf_new(2))));
// L-FALSIF: the oracle must disagree with a wrong board.
{
  const g = grid(e.lf_place(blank, 1, 3, 3));
  const wrong = nextGen(g).map(r => r.slice());
  wrong[0][0] = 1 - wrong[0][0];
  ok('control: the comparison notices a single flipped cell',
     show(wrong) !== show(nextGen(g)));
}

console.log(fail === 0
  ? `\nPASS: Life is Conway's, generation for generation (${pass} arms).`
  : `\nFAIL: ${fail} of ${pass + fail} arms.`);
process.exitCode = fail === 0 ? 0 : 1;
