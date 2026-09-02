// Grade the Go wasm module.
//
// The arm this file exists for is the suicide rule, and it is written as a
// CONSTRUCTED position rather than hoped for out of random play. A corner
// point has only two neighbours, so two enemy stones surround it; playing
// there has no liberties and captures nothing, and the move must be refused
// with the board untouched. Before the fix that arm read "point 0 now: 1,
// stones 4 -> 5, to move 1 -> 1": the move was refused, the turn did not
// pass, and the illegal stone stayed on the board anyway, because
// `go-place-stone` wrote the stone with `list-set-at` before it checked the
// liberties (games-backlog GAME-20).
//
// The other arm that no total can replace is capture accounting: the number
// of enemy stones that disappear from the board must equal the rise in the
// capturing player's counter, every move.
//
// Usage: node apps/games/go-verify.mjs [path/to/go.wasm]

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const wasmPath = process.argv[2] ||
  join(here, '..', 'landing', 'web', 'games', 'go.wasm');

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

const grid = h => [...Array(81)].map((_, i) => e.go_cell(h, i));
const stonesOf = (h, c) => grid(h).filter(v => v === c).length;

console.log(`go-verify ${wasmPath}`);

// -- The empty board ------------------------------------------------------
const s0 = e.go_new();
ok('eighty-one empty points', grid(s0).every(v => v === 0));
ok('black to move, nothing captured, no passes',
   e.go_cur(s0) === 1 && e.go_captures(s0, 1) === 0 && e.go_captures(s0, 2) === 0 &&
   e.go_passes(s0) === 0 && e.go_done(s0) === 0);
ok('no ko point yet', e.go_ko(s0) === -1);

// -- THE SUICIDE ARM ------------------------------------------------------
{
  let h = e.go_new();
  for (const pt of [40, 1, 41, 9]) h = e.go_place(h, pt);
  const before = JSON.stringify(grid(h));
  const beforeStones = grid(h).filter(v => v).length;
  ok('setup: the corner is surrounded and black is to move',
     e.go_cell(h, 1) === 2 && e.go_cell(h, 9) === 2 && e.go_cur(h) === 1 && e.go_cell(h, 0) === 0);
  const after = e.go_place(h, 0);
  ok('THE SUICIDE ARM: a move with no liberties leaves no stone',
     e.go_cell(after, 0) === 0, `point 0 reads ${e.go_cell(after, 0)}`);
  ok('and the stone count does not move',
     grid(after).filter(v => v).length === beforeStones,
     `${beforeStones} -> ${grid(after).filter(v => v).length}`);
  ok('and the board it was played from is untouched', JSON.stringify(grid(h)) === before);
  ok('and the turn does not pass on a refused move', e.go_cur(after) === 1);
}

// -- GAME-13 and the copy arm --------------------------------------------
{
  let h = e.go_new();
  for (const pt of [40, 30, 50, 20]) h = e.go_place(h, pt);
  const before = JSON.stringify(grid(h));
  for (let i = 0; i < 5; i++) e.go_ai(h, i + 1);
  ok('GAME-13: asking for a move does not mutate the board asked about',
     JSON.stringify(grid(h)) === before);
  const next = e.go_place(h, 60);
  ok('placing answers a different board', next !== h);
  ok('THE COPY ARM: the board placed from is untouched',
     JSON.stringify(grid(h)) === before);
  ok('an occupied point is refused', e.go_cell(e.go_place(h, 40), 40) === e.go_cell(h, 40) &&
     grid(e.go_place(h, 40)).filter(v => v).length === grid(h).filter(v => v).length);
  ok('a point off the board is refused', e.go_place(h, 81) === h && e.go_cell(h, 81) === -1);
}

// -- Passing --------------------------------------------------------------
{
  const p1 = e.go_pass(e.go_new());
  ok('a pass counts and hands over', e.go_passes(p1) === 1 && e.go_cur(p1) === 2);
  const p2 = e.go_pass(p1);
  ok('two passes end the game', e.go_done(p2) === 1, e.go_passes(p2));
  ok('a finished game refuses a move', e.go_place(p2, 40) === p2 && e.go_pass(p2) === p2);
}

// -- Capture accounting over real games ----------------------------------
{
  const bad = [];
  let totalMoves = 0, totalCaptures = 0, finished = 0;
  for (let seed = 1; seed <= 12; seed++) {
    let h = e.go_new(), guard = 0;
    while (e.go_done(h) === 0 && guard < 300) {
      guard++;
      const mv = e.go_ai(h, seed * 1000 + guard);
      const beforeGrid = grid(h);
      const beforeCur = e.go_cur(h);
      const opp = beforeCur === 1 ? 2 : 1;
      const beforeOpp = stonesOf(h, opp);
      const beforeCaps = e.go_captures(h, beforeCur);
      const next = mv < 0 ? e.go_pass(h) : e.go_place(h, mv);
      if (mv >= 0 && next !== h) {
        const gone = beforeOpp - stonesOf(next, opp);
        const gained = e.go_captures(next, beforeCur) - beforeCaps;
        if (gone !== gained) {
          bad.push(`seed ${seed} move ${guard}: ${gone} stones removed, counter rose ${gained}`);
        }
        totalCaptures += gained;
        // A legal move adds exactly one stone of the mover, minus captures.
        const mineBefore = beforeGrid.filter(v => v === beforeCur).length;
        const mineAfter = stonesOf(next, beforeCur);
        if (mineAfter !== mineBefore + 1) {
          bad.push(`seed ${seed} move ${guard}: mover went from ${mineBefore} to ${mineAfter} stones`);
        }
        // Every point holds 0, 1 or 2 and nothing else.
        if (!grid(next).every(v => v === 0 || v === 1 || v === 2)) {
          bad.push(`seed ${seed}: a point holds something that is not a stone`);
        }
      }
      h = next;
      totalMoves++;
      if (bad.length > 3) break;
    }
    if (e.go_done(h) === 1) finished++;
  }
  ok('stones removed always equal the rise in the capture counter',
     bad.length === 0, bad.length ? bad.slice(0, 3).join('; ') : `${totalMoves} moves`);
  ok('control: captures actually happened in the sample', totalCaptures > 0,
     `${totalCaptures} stones captured`);
  ok('control: the games ran long enough', totalMoves > 200, totalMoves);
  ok('the games end', finished === 12, `${finished} of 12`);
}

// -- Liberties ------------------------------------------------------------
{
  const h = e.go_place(e.go_new(), 40);
  ok('a lone stone in the middle has four liberties', e.go_liberties(h, 40) === 4,
     e.go_liberties(h, 40));
  const corner = e.go_place(e.go_new(), 0);
  ok('a lone stone in the corner has two', e.go_liberties(corner, 0) === 2,
     e.go_liberties(corner, 0));
  ok('an empty point has no liberties to report', e.go_liberties(h, 1) === -1);
}

// -- Controls -------------------------------------------------------------
ok('control: two new boards are different handles', e.go_new() !== e.go_new());
ok('control: different points give different boards',
   JSON.stringify(grid(e.go_place(e.go_new(), 0))) !==
   JSON.stringify(grid(e.go_place(e.go_new(), 1))));
ok('control: the board reader can see a stone',
   e.go_cell(e.go_place(e.go_new(), 5), 5) === 1 && e.go_cell(e.go_new(), 5) === 0);

console.log(fail === 0
  ? `\nPASS: Go refuses a suicide and counts what it captures (${pass} arms).`
  : `\nFAIL: ${fail} of ${pass + fail} arms.`);
process.exitCode = fail === 0 ? 0 : 1;
