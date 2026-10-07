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
import { gameInstance } from '../landing/web/games/arcade.js';

const here = dirname(fileURLToPath(import.meta.url));
const wasmPath = process.argv[2] ||
  join(here, '..', 'landing', 'web', 'games', 'game2048.wasm');


const inst = gameInstance(
  new WebAssembly.Module(readFileSync(wasmPath)));
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

// -- THE RULES: the slide, merge, spawn and score of each move ---------------
// Every tile slides as far as it can toward the move's side; two equal tiles
// that meet merge into one of twice the value, each tile merging at most once
// a move and the pair nearest the side merging first; the score rises by the
// value of every merged tile; a move that changes nothing is refused; a
// changed board gains one 2 or 4 on an empty cell; the game ends when no
// direction changes anything. Directions: 0 left, 1 right, 2 up, 3 down.
function slideLine(line) {
  const t = line.filter(v => v), out = []; let gained = 0;
  for (let i = 0; i < t.length; i++) {
    if (i + 1 < t.length && t[i] === t[i + 1]) { out.push(t[i] * 2); gained += t[i] * 2; i++; }
    else out.push(t[i]);
  }
  while (out.length < 4) out.push(0);
  return { out, gained };
}
function slide(g, d) {
  const n = g.slice(); let gained = 0;
  for (let k = 0; k < 4; k++) {
    const idx = [0, 1, 2, 3].map(j => d === 0 ? k * 4 + j : d === 1 ? k * 4 + 3 - j : d === 2 ? j * 4 + k : (3 - j) * 4 + k);
    const r = slideLine(idx.map(i => g[i]));
    idx.forEach((i, j) => { n[i] = r.out[j]; });
    gained += r.gained;
  }
  return { n, gained, changed: n.some((v, i) => v !== g[i]) };
}
{
  let bad = null, moves = 0, merges = 0, refused = 0, fours = 0, s = 9;
  const rnd = k => { s = (s * 1103515245 + 12345) & 0x7fffffff; return (s >>> 16) % k; };
  for (let g = 0; g < 20 && !bad; g++) {
    let h = e.g2_new(g + 1);
    for (let m = 0; m < 3000 && !bad && e.g2_done(h) === 0; m++) {
      const before = grid(h), score = e.g2_score(h);
      for (let d = 0; d < 4; d++) {
        const ch = slide(before, d).changed;
        if ((e.g2_can(h, d) === 1) !== ch) { bad = `game ${g}: direction ${d} playable ${e.g2_can(h, d)}, rules ${ch}`; break; }
      }
      if (bad) break;
      const d = rnd(4), r = slide(before, d);
      const next = e.g2_move(h, d);
      if (!r.changed) {
        refused++;
        if (JSON.stringify(grid(next)) !== JSON.stringify(before) || e.g2_moves(next) !== e.g2_moves(h) || e.g2_score(next) !== score) {
          bad = `game ${g}: a move that changes nothing changed the game`;
        }
        continue;
      }
      const after = grid(next);
      const diff = after.map((v, i) => i).filter(i => after[i] !== r.n[i]);
      if (diff.length !== 1 || r.n[diff[0]] !== 0 || (after[diff[0]] !== 2 && after[diff[0]] !== 4)) {
        bad = `game ${g}: after ${d} the board is ${after}, the slide gives ${r.n} plus one new 2 or 4`; break;
      }
      if (after[diff[0]] === 4) fours++;
      if (e.g2_score(next) !== score + r.gained) { bad = `game ${g}: score ${e.g2_score(next)}, rules ${score + r.gained}`; break; }
      if (r.gained) merges++;
      const over = [0, 1, 2, 3].every(k => !slide(after, k).changed);
      if ((e.g2_done(next) === 1) !== over) { bad = `game ${g}: done ${e.g2_done(next)}, rules ${over}`; break; }
      h = next; moves++;
    }
  }
  ok('rules: every move of 20 games slides, merges, spawns and scores by the rules', bad === null, bad ?? `${moves} moves`);
  ok('rules: the games reached merges, refused moves and spawned 4s', merges > 0 && refused > 0 && fours > 0,
     `${merges} merging moves, ${refused} refusals, ${fours} fours`);
  const once = slide([2, 2, 2, 2, 4, 4, 8, 0, 2, 2, 4, 0, 0, 0, 0, 0], 0).n.slice(0, 12);
  ok('control: the oracle merges each tile once, nearest the side first',
     JSON.stringify(once) === JSON.stringify([4, 4, 0, 0, 8, 8, 0, 0, 4, 4, 0, 0]), JSON.stringify(once));
}

console.log(fail === 0
  ? `\nPASS: 2048 merges without losing or inventing a tile (${pass} arms).`
  : `\nFAIL: ${fail} of ${pass + fail} arms.`);
process.exitCode = fail === 0 ? 0 : 1;
