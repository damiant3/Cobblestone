// Grade the Hex wasm module.
//
// Hex has a property no other game here has: it cannot be drawn. Once the
// board is full exactly one side owns a connecting path, and a path is
// decidable, so this file runs its own flood fill over the finished board
// and requires the engine's verdict to match. That is a real oracle rather
// than the engine restated: a win detector that stops one row short, or
// that treats the wrong pair of edges as the goal, produces a game that
// still ends and still names a winner.
//
// Player 1 connects row 0 to row 10; player 2 connects column 0 to column
// 10. A hex cell has six neighbours: the four square ones plus the two on
// the leading diagonal.
//
// Usage: node apps/games/hx-verify.mjs [path/to/hexgame.wasm]

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const wasmPath = process.argv[2] ||
  join(here, '..', 'landing', 'web', 'games', 'hexgame.wasm');

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

const cells = h => [...Array(121)].map((_, i) => e.hx_cell(h, i));

// An independent connectivity check. Player 1 goes top to bottom, player 2
// left to right.
function connects(g, player) {
  const seen = new Set();
  const stack = [];
  for (let k = 0; k < 11; k++) {
    const start = player === 1 ? k : k * 11;   // row 0, or column 0
    if (g[start] === player) { stack.push(start); seen.add(start); }
  }
  while (stack.length) {
    const i = stack.pop();
    const r = Math.floor(i / 11), c = i % 11;
    if (player === 1 ? r === 10 : c === 10) return true;
    for (const [dr, dc] of [[1, -1], [-1, 1], [0, 1], [0, -1], [1, 0], [-1, 0]]) {
      const nr = r + dr, nc = c + dc;
      if (nr < 0 || nr > 10 || nc < 0 || nc > 10) continue;
      const n = nr * 11 + nc;
      if (g[n] === player && !seen.has(n)) { seen.add(n); stack.push(n); }
    }
  }
  return false;
}

console.log(`hx-verify ${wasmPath}`);

// -- The empty board ------------------------------------------------------
const s0 = e.hx_new();
ok('a hundred and twenty-one empty cells', cells(s0).every(v => v === 0));
ok('player 1 to move, no moves made, not over',
   e.hx_cur(s0) === 1 && e.hx_moves(s0) === 0 && e.hx_done(s0) === 0 && e.hx_winner(s0) === 0);
ok('nobody connects an empty board',
   e.hx_connected(s0, 1) === 0 && e.hx_connected(s0, 2) === 0);
ok('the oracle agrees the empty board connects nothing',
   !connects(cells(s0), 1) && !connects(cells(s0), 2));

// -- GAME-13 and the copy arm --------------------------------------------
{
  let h = e.hx_new();
  for (const i of [60, 61, 49, 50]) h = e.hx_place(h, i);
  const before = JSON.stringify(cells(h));
  for (let k = 0; k < 3; k++) e.hx_ai(h);
  ok('GAME-13: the winning-move search leaves the board as it found it',
     JSON.stringify(cells(h)) === before);
  const next = e.hx_place(h, 70);
  ok('placing answers a different board', next !== h);
  ok('THE COPY ARM: the board placed from is untouched',
     JSON.stringify(cells(h)) === before);
  ok('an occupied cell is refused', e.hx_can(h, 60) === 0 && e.hx_place(h, 60) === h);
  ok('a cell off the board is refused',
     e.hx_place(h, 121) === h && e.hx_place(h, -1) === h && e.hx_cell(h, 121) === -1);
}

// -- A hand-built connection ---------------------------------------------
// Player 1 needs a path from row 0 to row 10. Column 5 straight down is one,
// and the two are placed alternately so player 2 fills a harmless column.
{
  let h = e.hx_new();
  let done = false;
  for (let r = 0; r < 11 && !done; r++) {
    h = e.hx_place(h, r * 11 + 5);          // player 1 down column 5
    if (e.hx_done(h) === 1) { done = true; break; }
    h = e.hx_place(h, r * 11 + 0);          // player 2 down column 0
  }
  ok('a straight column connects top to bottom for player 1',
     e.hx_done(h) === 1 && e.hx_winner(h) === 1,
     `done ${e.hx_done(h)} winner ${e.hx_winner(h)}`);
  ok('the oracle agrees that path connects', connects(cells(h), 1));
  ok('and that the other side does not', !connects(cells(h), 2));
  ok('a finished game refuses another stone', e.hx_place(h, 60) === h);
}

// -- Whole games against the oracle --------------------------------------
{
  const bad = [];
  let finished = 0, totalMoves = 0;
  const winners = new Set();
  for (let seed = 1; seed <= 14; seed++) {
    let h = e.hx_new(), guard = 0;
    let s = seed * 7717 + 3;
    while (e.hx_done(h) === 0 && guard < 200) {
      guard++;
      const free = [...Array(121)].map((_, i) => i).filter(i => e.hx_can(h, i) === 1);
      if (free.length === 0) break;
      // Alternate the engine with a seeded chooser so the win detector meets
      // shapes the engine's own preferences would never build.
      let idx;
      if (seed % 2 === 0) idx = e.hx_ai(h);
      else { s = (s * 1103515245 + 12345) & 0x7fffffff; idx = free[s % free.length]; }
      const mover = e.hx_cur(h);
      const before = cells(h);
      h = e.hx_place(h, idx);
      const after = cells(h);
      // Exactly one stone appears, and it belongs to the mover.
      const added = after.filter(v => v).length - before.filter(v => v).length;
      if (added !== 1) bad.push(`seed ${seed}: ${added} stones appeared`);
      if (after[idx] !== mover) bad.push(`seed ${seed}: cell ${idx} holds ${after[idx]}, mover was ${mover}`);
      // Nothing already placed ever changes.
      for (let i = 0; i < 121; i++) {
        if (before[i] !== 0 && after[i] !== before[i]) {
          bad.push(`seed ${seed}: cell ${i} changed from ${before[i]} to ${after[i]}`);
        }
      }
      // THE ORACLE: the engine may declare a win only when a path exists.
      const g = cells(h);
      const engineDone = e.hx_done(h) === 1;
      const trulyWon = connects(g, 1) || connects(g, 2);
      if (engineDone !== trulyWon) {
        bad.push(`seed ${seed} move ${guard}: engine says over=${engineDone}, paths say ${trulyWon}`);
      }
      if (engineDone) {
        const w = e.hx_winner(h);
        if (!connects(g, w)) bad.push(`seed ${seed}: winner ${w} has no path`);
        winners.add(w);
      }
      totalMoves++;
      if (bad.length > 3) break;
    }
    if (e.hx_done(h) === 1) finished++;
  }
  ok('the engine declares a win exactly when a path exists',
     bad.length === 0, bad.length ? bad.slice(0, 3).join('; ') : `${totalMoves} moves`);
  ok('the games finish', finished === 14, `${finished} of 14`);
  ok('both colours win some', winners.size === 2, `winners: ${[...winners].sort().join(',')}`);
  ok('control: enough moves to mean something', totalMoves > 300, totalMoves);
}

// -- Controls -------------------------------------------------------------
ok('control: two new boards are different handles', e.hx_new() !== e.hx_new());
ok('control: different cells give different boards',
   JSON.stringify(cells(e.hx_place(e.hx_new(), 0))) !==
   JSON.stringify(cells(e.hx_place(e.hx_new(), 1))));
// L-FALSIF: the path oracle must reject a board that does not connect.
{
  const g = new Array(121).fill(0);
  for (let r = 0; r < 10; r++) g[r * 11 + 5] = 1;   // one row short
  ok('control: the path oracle rejects a path that stops one row short',
     !connects(g, 1));
  g[10 * 11 + 5] = 1;
  ok('control: and accepts it once completed', connects(g, 1));
}

console.log(fail === 0
  ? `\nPASS: Hex declares a win exactly when one exists (${pass} arms).`
  : `\nFAIL: ${fail} of ${pass + fail} arms.`);
process.exitCode = fail === 0 ? 0 : 1;
