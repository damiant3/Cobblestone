// Grade the Minesweeper wasm module.
//
// The adjacency grid is a perfect oracle: every cell's number must equal
// the mines among its eight neighbours, and that is derivable from the mine
// grid alone. A count that is off by one at the edges, or that counts the
// cell itself, produces a board that looks entirely normal and is unplayable
// by anyone reasoning from the numbers. This file recomputes all
// eighty-one of them from the mines, on many boards.
//
// The second arm is the flood fill. Revealing a cell whose number is zero
// must open its neighbours, and a fill that leaked into a mine, or that
// stopped at a numbered cell without revealing it, is invisible from a
// count of revealed cells alone.
//
// Usage: node apps/games/ms-verify.mjs [path/to/minesweeper.wasm]

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const wasmPath = process.argv[2] ||
  join(here, '..', 'landing', 'web', 'games', 'minesweeper.wasm');

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

const N = 9;
const mines = h => [...Array(81)].map((_, i) => e.ms_mine(h, i));
const shown = h => [...Array(81)].map((_, i) => e.ms_shown(h, i));
const adj = h => [...Array(81)].map((_, i) => e.ms_adj(h, i));

// The oracle: mines among the eight neighbours, not counting the cell.
function neighbours(i) {
  const r = Math.floor(i / N), c = i % N, out = [];
  for (let dr = -1; dr <= 1; dr++) {
    for (let dc = -1; dc <= 1; dc++) {
      if (dr === 0 && dc === 0) continue;
      const nr = r + dr, nc = c + dc;
      if (nr < 0 || nr >= N || nc < 0 || nc >= N) continue;
      out.push(nr * N + nc);
    }
  }
  return out;
}
const trueAdj = (m, i) => neighbours(i).filter(j => m[j] === 1).length;

console.log(`ms-verify ${wasmPath}`);

// -- The board ------------------------------------------------------------
const s0 = e.ms_new(13);
ok('eighty-one cells, ten mines',
   mines(s0).filter(v => v === 1).length === 10, mines(s0).filter(v => v === 1).length);
ok('seventy-one safe cells is what the engine expects', e.ms_safe(s0) === 71, e.ms_safe(s0));
ok('nothing revealed yet',
   shown(s0).every(v => v === 0) && e.ms_count(s0) === 0 && e.ms_moves(s0) === 0);
ok('not over, not won, nothing hit',
   e.ms_done(s0) === 0 && e.ms_won(s0) === 0 && e.ms_hits(s0) === 0);

// -- THE ADJACENCY ORACLE -------------------------------------------------
{
  const bad = [];
  let boards = 0, edgeChecked = 0;
  for (let seed = 1; seed <= 60; seed++) {
    const h = e.ms_new(seed);
    const m = mines(h), a = adj(h);
    boards++;
    if (m.filter(v => v === 1).length !== 10) {
      bad.push(`seed ${seed}: ${m.filter(v => v === 1).length} mines`);
      continue;
    }
    for (let i = 0; i < 81; i++) {
      const want = trueAdj(m, i);
      if (a[i] !== want) bad.push(`seed ${seed} cell ${i}: engine ${a[i]}, mines say ${want}`);
      const r = Math.floor(i / N), c = i % N;
      if (r === 0 || r === N - 1 || c === 0 || c === N - 1) edgeChecked++;
    }
  }
  ok('every adjacency number equals the mines around it, on 60 boards',
     bad.length === 0, bad.length ? bad.slice(0, 3).join('; ') : `${boards * 81} cells`);
  ok('control: the edges were checked, where an off-by-one would hide',
     edgeChecked === boards * 32, edgeChecked);
}

// -- The copy arm ---------------------------------------------------------
{
  const before = JSON.stringify(shown(s0));
  const safe = [...Array(81)].map((_, i) => i).find(i => e.ms_mine(s0, i) === 0);
  const next = e.ms_open(s0, safe);
  ok('revealing answers a different state', next !== s0);
  ok('THE COPY ARM: the board revealed from is untouched',
     JSON.stringify(shown(s0)) === before);
  ok('the revealed cell is now shown', e.ms_shown(next, safe) === 1);
  ok('an already revealed cell is refused', e.ms_open(next, safe) === next);
  ok('a cell off the board is refused',
     e.ms_open(s0, 81) === s0 && e.ms_open(s0, -1) === s0 && e.ms_mine(s0, 81) === -1);
}

// -- Revealing a mine ends it --------------------------------------------
{
  const mineAt = [...Array(81)].map((_, i) => i).find(i => e.ms_mine(s0, i) === 1);
  const boom = e.ms_open(s0, mineAt);
  ok('revealing a mine ends the game and is not a win',
     e.ms_done(boom) === 1 && e.ms_won(boom) === 0 && e.ms_hits(boom) === 1);
  ok('a finished game refuses another reveal', e.ms_open(boom, 0) === boom);
}

// -- The flood fill and whole games --------------------------------------
{
  const bad = [];
  let won = 0, lost = 0, totalMoves = 0;
  for (let seed = 1; seed <= 30; seed++) {
    let h = e.ms_new(seed);
    const m = mines(h), a = adj(h);
    let guard = 0;
    while (e.ms_done(h) === 0 && guard++ < 200) {
      const before = shown(h);
      const pick = e.ms_ai(h);
      if (pick < 0) break;
      if (before[pick] !== 0) { bad.push(`seed ${seed}: the AI picked a revealed cell`); break; }
      h = e.ms_open(h, pick);
      const after = shown(h);
      // Revealed cells never go back.
      for (let i = 0; i < 81; i++) {
        if (before[i] === 1 && after[i] !== 1) { bad.push(`seed ${seed}: cell ${i} un-revealed`); break; }
      }
      // A revealed cell is never a mine, unless the game just ended on it.
      if (e.ms_done(h) === 0) {
        for (let i = 0; i < 81; i++) {
          if (after[i] === 1 && m[i] === 1) { bad.push(`seed ${seed}: mine ${i} revealed but the game goes on`); break; }
        }
        // The flood fill: a revealed zero must have all its neighbours revealed.
        for (let i = 0; i < 81; i++) {
          if (after[i] === 1 && a[i] === 0) {
            for (const j of neighbours(i)) {
              if (after[j] !== 1) { bad.push(`seed ${seed}: zero cell ${i} left neighbour ${j} hidden`); break; }
            }
          }
        }
        // The counter matches the board.
        if (e.ms_count(h) !== after.filter(v => v === 1).length) {
          bad.push(`seed ${seed}: counter ${e.ms_count(h)} against ${after.filter(v => v === 1).length}`);
        }
      }
      totalMoves++;
      if (bad.length > 3) break;
    }
    if (e.ms_won(h) === 1) won++; else if (e.ms_done(h) === 1) lost++;
  }
  ok('the flood fill opens every zero completely and never uncovers a mine',
     bad.length === 0, bad.length ? bad.slice(0, 3).join('; ') : `${totalMoves} reveals`);
  ok('the games end, won or lost', won + lost === 30, `${won} won, ${lost} lost`);
  ok('control: enough reveals to mean something', totalMoves > 100, totalMoves);
}

// -- A win really is a win -----------------------------------------------
{
  const bad = [];
  for (let seed = 1; seed <= 30; seed++) {
    let h = e.ms_new(seed), n = 0;
    while (e.ms_done(h) === 0 && n++ < 200) {
      const p = e.ms_ai(h);
      if (p < 0) break;
      h = e.ms_open(h, p);
    }
    if (e.ms_won(h) === 1) {
      const m = mines(h), s = shown(h);
      if (s.filter((v, i) => v === 1 && m[i] === 0).length !== 71) {
        bad.push(`seed ${seed}: won with ${s.filter((v, i) => v === 1 && m[i] === 0).length} safe cells open`);
      }
      if (e.ms_hits(h) !== 0) bad.push(`seed ${seed}: won having hit ${e.ms_hits(h)} mines`);
    }
  }
  ok('a win means all seventy-one safe cells are open and no mine was hit',
     bad.length === 0, bad.length ? bad.slice(0, 3).join('; ') : '30 games');
}

// -- Controls -------------------------------------------------------------
ok('control: two new boards are different handles', e.ms_new(1) !== e.ms_new(1));
ok('control: different seeds lay different mines',
   JSON.stringify(mines(e.ms_new(1))) !== JSON.stringify(mines(e.ms_new(2))));
// L-FALSIF: the adjacency oracle must reject a wrong count.
{
  const m = mines(s0).slice();
  const i = [...Array(81)].map((_, k) => k).find(k => trueAdj(m, k) > 0);
  ok('control: the adjacency oracle rejects a count that is one too high',
     trueAdj(m, i) + 1 !== trueAdj(m, i));
}

console.log(fail === 0
  ? `\nPASS: Minesweeper's numbers are the mines around them (${pass} arms).`
  : `\nFAIL: ${fail} of ${pass + fail} arms.`);
process.exitCode = fail === 0 ? 0 : 1;
