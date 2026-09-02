// Grade the Dots and Boxes wasm module.
//
// The arm this file leads with is GAME-13's, because the move search here
// does something none of the other engines do: `dots-find-completing-move`
// probes a candidate by writing the edge into the CALLER's own list, asking
// whether that completes a box, and writing it back. Borrow-and-restore is
// only safe while the restore always runs, and whether it does on the
// success path is a question about evaluation order, not about intent. So
// it is measured, twice: once around the search, and once by requiring the
// board to be unchanged across a whole run of searches.
//
// The scoring invariant is the other half: the two scores together must
// always equal the number of boxes actually closed on the board, and a
// player who closes a box keeps the turn while one who does not gives it up.
//
// Usage: node apps/games/dt-verify.mjs [path/to/dotsandboxes.wasm]

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const wasmPath = process.argv[2] ||
  join(here, '..', 'landing', 'web', 'games', 'dotsandboxes.wasm');

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

const edges = h => [...Array(24)].map((_, i) => e.dt_edge(h, i));
const boxes = h => [...Array(9)].map((_, i) => e.dt_box(h, i));
const closed = h => boxes(h).filter(v => v !== 0).length;

console.log(`dt-verify ${wasmPath}`);

// -- The opening board ----------------------------------------------------
const s0 = e.dt_new(5);
ok('twenty-four edges, all free', edges(s0).every(v => v === 0), edges(s0).filter(v => v).length);
ok('nine boxes, none claimed', boxes(s0).every(v => v === 0));
ok('player 1 to move, nothing scored',
   e.dt_cur(s0) === 1 && e.dt_score(s0, 1) === 0 && e.dt_score(s0, 2) === 0);
ok('the game is not over', e.dt_done(s0) === 0);
ok('every edge is playable', [...Array(24)].every((_, i) => e.dt_can(s0, i) === 1));

// -- GAME-13: the borrow-and-restore search ------------------------------
{
  const before = JSON.stringify(edges(s0));
  const beforeBoxes = JSON.stringify(boxes(s0));
  for (let i = 0; i < 10; i++) e.dt_ai(s0);
  ok('GAME-13: asking for a move leaves the edges exactly as they were',
     JSON.stringify(edges(s0)) === before, JSON.stringify(edges(s0)));
  ok('and leaves the boxes alone too', JSON.stringify(boxes(s0)) === beforeBoxes);
}

// -- The copy arm ---------------------------------------------------------
{
  const before = JSON.stringify(edges(s0));
  const next = e.dt_place(s0, 0);
  ok('placing answers a different board', next !== s0, `${s0} -> ${next}`);
  ok('THE COPY ARM: the board placed from is untouched',
     JSON.stringify(edges(s0)) === before);
  ok('the placed edge is set on the new board', e.dt_edge(next, 0) === 1);
  ok('an occupied edge is refused', e.dt_can(next, 0) === 0 && e.dt_place(next, 0) === next);
  ok('an edge off the board is refused',
     e.dt_place(s0, 24) === s0 && e.dt_place(s0, -1) === s0 && e.dt_edge(s0, 24) === -1);
}

// -- Whole games ----------------------------------------------------------
function playGame(seed, pick) {
  let h = e.dt_new(seed), plies = 0;
  const problems = [];
  while (e.dt_done(h) === 0 && plies < 40) {
    const free = [...Array(24)].map((_, i) => i).filter(i => e.dt_can(h, i) === 1);
    if (free.length === 0) break;
    const idx = pick(h, free);
    const before = h, beforeClosed = closed(h), beforeCur = e.dt_cur(h);
    // The search must not disturb the board it is asked about, on EVERY
    // position rather than only the opening one.
    const snapEdges = JSON.stringify(edges(h));
    e.dt_ai(h);
    if (JSON.stringify(edges(h)) !== snapEdges) {
      problems.push(`ply ${plies}: the search mutated the board`);
      break;
    }
    h = e.dt_place(h, idx);
    plies++;
    // Edges only ever fill in.
    const be = edges(before), ae = edges(h);
    for (let i = 0; i < 24; i++) {
      if (be[i] !== 0 && ae[i] !== be[i]) { problems.push(`ply ${plies}: edge ${i} changed`); }
    }
    if (ae.filter(v => v).length !== be.filter(v => v).length + 1) {
      problems.push(`ply ${plies}: edge count moved by more than one`);
    }
    // Scores equal boxes closed, and the turn follows the scoring rule.
    const gained = closed(h) - beforeClosed;
    if (e.dt_score(h, 1) + e.dt_score(h, 2) !== closed(h)) {
      problems.push(`ply ${plies}: scores ${e.dt_score(h, 1)}+${e.dt_score(h, 2)} against ${closed(h)} boxes`);
    }
    const keptTurn = e.dt_cur(h) === beforeCur;
    if (keptTurn !== (gained > 0)) {
      problems.push(`ply ${plies}: closed ${gained} boxes but ${keptTurn ? 'kept' : 'gave up'} the turn`);
    }
    if (problems.length > 3) break;
  }
  return { h, plies, problems };
}

{
  const all = [];
  let finished = 0, totalPlies = 0;
  for (let seed = 1; seed <= 15; seed++) {
    let s = seed * 6151 + 7;
    const pick = seed % 2 === 0
      ? (h => e.dt_ai(h))
      : ((h, free) => { s = (s * 1103515245 + 12345) & 0x7fffffff; return free[s % free.length]; });
    const r = playGame(seed, pick);
    all.push(...r.problems.map(p => `seed ${seed} ${p}`));
    totalPlies += r.plies;
    if (e.dt_done(r.h) === 1) finished++;
  }
  ok('edges only fill, scores match the closed boxes, and the turn follows the rule',
     all.length === 0, all.length ? all.slice(0, 3).join('; ') : `${totalPlies} plies`);
  ok('every game fills the board and ends', finished === 15, `${finished} of 15`);
  ok('control: the games ran the full board', totalPlies === 15 * 24, totalPlies);
}

// -- The final tally ------------------------------------------------------
{
  const bad = [];
  for (let seed = 1; seed <= 15; seed++) {
    let h = e.dt_new(seed);
    while (e.dt_done(h) === 0) h = e.dt_place(h, e.dt_ai(h));
    if (e.dt_score(h, 1) + e.dt_score(h, 2) !== 9) {
      bad.push(`seed ${seed}: ${e.dt_score(h, 1)}+${e.dt_score(h, 2)}`);
    }
    if (closed(h) !== 9) bad.push(`seed ${seed}: ${closed(h)} boxes closed`);
    if (edges(h).some(v => v === 0)) bad.push(`seed ${seed}: an edge is still free`);
  }
  ok('a finished game has all nine boxes claimed and all edges drawn',
     bad.length === 0, bad.length ? bad.slice(0, 3).join('; ') : '15 games');
}

// -- Controls -------------------------------------------------------------
ok('control: two new boards are different handles', e.dt_new(1) !== e.dt_new(1));
ok('control: different edges give different boards',
   JSON.stringify(edges(e.dt_place(e.dt_new(1), 0))) !==
   JSON.stringify(edges(e.dt_place(e.dt_new(1), 5))));
ok('control: the edge reader can see a change',
   e.dt_edge(e.dt_place(e.dt_new(1), 3), 3) === 1 && e.dt_edge(e.dt_new(1), 3) === 0);

console.log(fail === 0
  ? `\nPASS: Dots and Boxes scores what it closes (${pass} arms).`
  : `\nFAIL: ${fail} of ${pass + fail} arms.`);
process.exitCode = fail === 0 ? 0 : 1;
