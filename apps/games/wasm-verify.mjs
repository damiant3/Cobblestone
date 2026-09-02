// Grade a game's wasm module against the answer key the bare-metal battery
// already publishes, so "it assembled" is never mistaken for "it computes".
//
// The oracle is codex/test/ttt-perfect.expected, produced by the same Codex
// source compiled for bare metal. It is an EXHAUSTIVE walk: the AI plays one
// side, every legal line is played against it, and the counts are recorded.
// Reproducing those counts through wasm means the module answers what the
// engine answers on every reachable position, not on the handful a spot check
// would have picked (L-CONSTRUCT: a corpus that builds its inputs one way
// never exercises the other; here the corpus is every way).
//
// Usage: node apps/games/wasm-verify.mjs [path/to/tictactoe.wasm]

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const wasmPath = process.argv[2] ||
  join(here, '..', 'landing', 'web', 'games', 'tictactoe.wasm');

// The emitter imports WASI for print paths this game never reaches. Refusing
// loudly rather than returning 0 keeps a silent write out of a passing run.
const imports = {
  wasi_snapshot_preview1: {
    fd_write: () => { throw new Error('fd_write: the game module must not write'); },
    fd_read:  () => { throw new Error('fd_read: the game module must not read'); },
  },
};

const mod = await WebAssembly.instantiate(readFileSync(wasmPath), imports);
const x = mod.instance.exports;

// No GC: the module bump-allocates and never frees, so the heap is reset
// between calls. Everything the game remembers is in the state word.
const call = (f, ...a) => { x.__heap_reset(); return f(...a); };

const EXPECT = {
  selfPlayWinner: 0,
  selfPlayMoves: 9,
  asX: { paths: 92,  wins: 85,  draws: 7,   losses: 0 },
  asO: { paths: 569, wins: 386, draws: 183, losses: 0 },
};

function walk(w, ai, st) {
  if (call(x.ttt_done, w) === 1) {
    const winner = call(x.ttt_winner, w);
    st.paths++;
    if (winner === ai) st.wins++;
    else if (winner === 0) st.draws++;
    else st.losses++;
    return st;
  }
  if (call(x.ttt_cur, w) === ai) return walk(call(x.ttt_ai, w), ai, st);
  for (let c = 0; c < 9; c++) {
    if (call(x.ttt_cell, w, c) !== 0) continue;
    walk(call(x.ttt_play, w, c), ai, st);
  }
  return st;
}

function selfPlay() {
  let w = call(x.ttt_new), moves = 0;
  while (call(x.ttt_done, w) !== 1 && moves <= 9) { w = call(x.ttt_ai, w); moves++; }
  return { winner: call(x.ttt_winner, w), moves };
}

const fails = [];
const say = (label, got, want) => {
  const ok = JSON.stringify(got) === JSON.stringify(want);
  console.log(`  ${ok ? 'ok  ' : 'FAIL'}  ${label}: ${JSON.stringify(got)}` +
              (ok ? '' : `  expected ${JSON.stringify(want)}`));
  if (!ok) fails.push(label);
};

console.log(`wasm-verify ${wasmPath}`);

const sp = selfPlay();
say('perfect play against itself', sp,
    { winner: EXPECT.selfPlayWinner, moves: EXPECT.selfPlayMoves });

say('AI as X vs all lines', walk(call(x.ttt_new), 1, { paths: 0, wins: 0, draws: 0, losses: 0 }), EXPECT.asX);
say('AI as O vs all lines', walk(call(x.ttt_new), 2, { paths: 0, wins: 0, draws: 0, losses: 0 }), EXPECT.asO);

// Two arms the exhaustive walk cannot express, because it only ever offers
// legal moves and only ever asks about live games.
const start = call(x.ttt_new);
const afterCentre = call(x.ttt_play, start, 4);
say('a move onto an occupied cell is refused', call(x.ttt_play, afterCentre, 4), afterCentre);
say('a move off the board is refused', call(x.ttt_play, start, 9), start);
say('a move below the board is refused', call(x.ttt_play, start, -1), start);
say('the opening board is empty and X is to move',
    { cells: [...Array(9)].map((_, i) => call(x.ttt_cell, start, i)),
      cur: call(x.ttt_cur, start), done: call(x.ttt_done, start) },
    { cells: [0,0,0,0,0,0,0,0,0], cur: 1, done: 0 });

// The control, and it is the arm that makes `losses: 0` mean anything.
//
// A walk that can never RECORD a loss reports zero losses whether or not the
// AI is perfect, and it reads exactly like a proof (L-FALSIF). So replace the
// module's AI with the dumbest legal chooser -- first empty cell -- and run
// the same walk. That player is beatable, so a walk that can see a loss must
// see some here. If this comes back zero, the two arms above proved nothing.
function walkDumb(w, ai, st) {
  if (call(x.ttt_done, w) === 1) {
    const winner = call(x.ttt_winner, w);
    st.paths++;
    if (winner === ai) st.wins++;
    else if (winner === 0) st.draws++;
    else st.losses++;
    return st;
  }
  if (call(x.ttt_cur, w) === ai) {
    for (let c = 0; c < 9; c++) {
      if (call(x.ttt_cell, w, c) === 0) return walkDumb(call(x.ttt_play, w, c), ai, st);
    }
    return st;
  }
  for (let c = 0; c < 9; c++) {
    if (call(x.ttt_cell, w, c) !== 0) continue;
    walkDumb(call(x.ttt_play, w, c), ai, st);
  }
  return st;
}
const dumb = walkDumb(call(x.ttt_new), 1, { paths: 0, wins: 0, draws: 0, losses: 0 });
const controlSees = dumb.losses > 0;
console.log(`  ${controlSees ? 'ok  ' : 'FAIL'}  control: a first-empty-cell player DOES lose ` +
            `(${dumb.losses} of ${dumb.paths}), so the walk can record a loss`);
if (!controlSees) fails.push('control');

if (fails.length) {
  console.log(`\nFAILED: ${fails.length} of 8 -- ${fails.join(', ')}`);
  process.exitCode = 1;
}
console.log('\nPASS: the wasm module matches codex/test/ttt-perfect.expected on every line.');
