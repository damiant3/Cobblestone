// Grade the Checkers wasm module.
//
// The arm that carries this file is an independent legality check on the
// move GENERATOR. Every move the engine offers is re-derived here from the
// board alone: a slide is one diagonal step onto an empty square, a jump is
// two diagonal steps onto an empty square with an enemy piece exactly
// between, and a man may only move toward its own promotion row while a
// king may go either way. A generator that offers one illegal move makes
// every game after it meaningless, and nothing about watching a game play
// would show it, because the pieces still move like pieces.
//
// Encoding, read from the engine: 0 empty, 1 player-0 man, 2 player-0 king,
// 3 player-1 man, 4 player-1 king. Player 0 advances toward row 0.
//
// Usage: node apps/games/ck-verify.mjs [path/to/checkers.wasm]

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const wasmPath = process.argv[2] ||
  join(here, '..', 'landing', 'web', 'games', 'checkers.wasm');

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

const cells = h => [...Array(64)].map((_, i) => e.ck_cell(h, i));
const owner = v => v === 1 || v === 2 ? 0 : v === 3 || v === 4 ? 1 : -1;
const isKing = v => v === 2 || v === 4;
const count = (g, p) => g.filter(v => owner(v) === p).length;
const moves = h => [...Array(e.ck_moves(h))].map((_, i) => ({
  from: e.ck_move_from(h, i), to: e.ck_move_to(h, i), cap: e.ck_move_cap(h, i),
}));

console.log(`ck-verify ${wasmPath}`);

// -- The opening board ----------------------------------------------------
const start = e.ck_new();
{
  const g = cells(start);
  ok('twelve pieces a side', count(g, 0) === 12 && count(g, 1) === 12,
     `${count(g, 0)} and ${count(g, 1)}`);
  ok('every piece is on a dark square',
     g.every((v, i) => v === 0 || ((Math.floor(i / 8) + (i % 8)) % 2 === 1)));
  ok('player 1 fills the top three rows and player 0 the bottom three',
     g.every((v, i) => {
       const row = Math.floor(i / 8);
       if (v === 0) return true;
       return row <= 2 ? v === 3 : row >= 5 ? v === 1 : false;
     }));
  ok('nothing is a king yet', g.every(v => !isKing(v)));
  ok('player 0 moves first, nothing decided',
     e.ck_turn(start) === 0 && e.ck_done(start) === 0 && e.ck_winner(start) === -1);
  ok('the opening position offers seven moves', e.ck_moves(start) === 7, e.ck_moves(start));
}

// -- GAME-13 and the copy arm --------------------------------------------
{
  const before = JSON.stringify(cells(start));
  for (let i = 0; i < 5; i++) e.ck_ai(start);
  ok('GAME-13: asking for a move does not mutate the board asked about',
     JSON.stringify(cells(start)) === before);
  for (let i = 0; i < 3; i++) e.ck_moves(start);
  ok('generating the moves does not mutate the board either',
     JSON.stringify(cells(start)) === before);
  const next = e.ck_apply(start, 0);
  ok('applying answers a different board', next !== start, `${start} -> ${next}`);
  ok('THE COPY ARM: the board applied from is untouched',
     JSON.stringify(cells(start)) === before);
  ok('the turn passed on the new board', e.ck_turn(next) === 1, e.ck_turn(next));
}

// -- The legality oracle, over real play ---------------------------------
// Re-derive every offered move from the board alone.
function illegal(g, turn, m) {
  const fr = Math.floor(m.from / 8), fc = m.from % 8;
  const tr = Math.floor(m.to / 8), tc = m.to % 8;
  const piece = g[m.from];
  if (owner(piece) !== turn) return `moves a piece belonging to ${owner(piece)}`;
  if (g[m.to] !== 0) return 'lands on an occupied square';
  const dr = tr - fr, dc = tc - fc;
  if (Math.abs(dr) !== Math.abs(dc)) return 'is not diagonal';
  const forward = turn === 0 ? -1 : 1;
  if (!isKing(piece) && Math.sign(dr) !== forward) return 'moves a man backwards';
  if (Math.abs(dr) === 1) {
    if (m.cap !== -1) return `is a slide claiming a capture at ${m.cap}`;
    return null;
  }
  if (Math.abs(dr) === 2) {
    const mid = ((fr + tr) / 2) * 8 + ((fc + tc) / 2);
    if (m.cap !== mid) return `jumps over ${mid} but claims ${m.cap}`;
    if (owner(g[mid]) !== 1 - turn) return `jumps over ${g[mid]}, which is not an enemy`;
    return null;
  }
  return `moves ${Math.abs(dr)} squares`;
}

function playGame(pick) {
  let h = e.ck_new(), plies = 0, lastCapture = 0;
  const problems = [];
  while (e.ck_done(h) === 0 && plies < 400) {
    const g = cells(h), turn = e.ck_turn(h), ms = moves(h);
    if (ms.length === 0) break;
    for (const m of ms) {
      const why = illegal(g, turn, m);
      if (why) problems.push(`ply ${plies}: ${m.from}->${m.to} ${why}`);
    }
    const idx = pick(h, ms.length, plies);
    const before = cells(h);
    const chosen = ms[idx];
    if (chosen.cap >= 0) lastCapture = plies;
    h = e.ck_apply(h, idx);
    const after = cells(h);
    // Piece bookkeeping: a jump removes exactly one enemy, a slide none.
    const lost = count(before, 1 - turn) - count(after, 1 - turn);
    const want = chosen.cap >= 0 ? 1 : 0;
    if (lost !== want) problems.push(`ply ${plies}: opponent lost ${lost}, expected ${want}`);
    if (count(before, turn) !== count(after, turn)) {
      problems.push(`ply ${plies}: the mover's own count changed`);
    }
    // Promotion happens exactly on the far row.
    const tr = Math.floor(chosen.to / 8);
    const moved = after[chosen.to];
    const wasKing = isKing(before[chosen.from]);
    const shouldKing = wasKing || (turn === 0 ? tr === 0 : tr === 7);
    if (isKing(moved) !== shouldKing) {
      problems.push(`ply ${plies}: piece at ${chosen.to} king=${isKing(moved)}, expected ${shouldKing}`);
    }
    plies++;
    if (problems.length > 4) break;
  }
  return { h, plies, problems, lastCapture };
}

{
  const all = [];
  let totalPlies = 0, ended = 0;
  const openEnded = [];
  for (let seed = 0; seed < 12; seed++) {
    // A seeded chooser rather than always the engine's pick, so the
    // generator is exercised on positions the engine would never reach.
    let s = seed * 7919 + 13;
    const pick = (h, n) => { s = (s * 1103515245 + 12345) & 0x7fffffff; return s % n; };
    const r = playGame(seed === 0 ? (h => e.ck_ai(h)) : pick);
    all.push(...r.problems.map(p => `seed ${seed} ${p}`));
    totalPlies += r.plies;
    if (e.ck_done(r.h) === 1 || e.ck_moves(r.h) === 0) ended++;
    else openEnded.push({ seed, sinceCapture: r.plies - r.lastCapture });
  }
  ok('every move offered in twelve games is legal, and the pieces account for',
     all.length === 0, all.length ? all.slice(0, 3).join('; ') : `${totalPlies} plies`);
  // NOT "every game ends". This engine has no draw rule -- real checkers
  // ends a barren endgame by the forty-move-without-capture rule and
  // `checkers-loop` only caps the move count and reports no winner
  // (games-backlog GAME-18). So the property that actually holds is that an
  // unfinished game is a no-capture shuffle rather than a stuck engine: a
  // game still running with a capture in recent memory WOULD be a defect,
  // because it would mean play had stopped making progress for some other
  // reason.
  const stuck = openEnded.filter(o => o.sinceCapture < 50);
  ok('any game still running is a no-capture shuffle, not a stalled one',
     stuck.length === 0,
     openEnded.length
       ? `${openEnded.length} unfinished, quiet for ${openEnded.map(o => o.sinceCapture).join('/')} plies`
       : 'all 12 finished');
  ok('control: the games were long enough to mean something', totalPlies > 100, totalPlies);
}

// -- Refusals -------------------------------------------------------------
{
  ok('a move index off the end is refused',
     e.ck_apply(start, 99) === start && e.ck_apply(start, -1) === start);
  ok('a square off the board is refused',
     e.ck_cell(start, 64) === -1 && e.ck_cell(start, -1) === -1);
  ok('a move index off the end has no from square',
     e.ck_move_from(start, 99) === -1 && e.ck_move_to(start, -1) === -1);
}

// -- Controls -------------------------------------------------------------
ok('control: two new boards are different handles', e.ck_new() !== e.ck_new());
ok('control: different moves give different boards',
   JSON.stringify(cells(e.ck_apply(e.ck_new(), 0))) !==
   JSON.stringify(cells(e.ck_apply(e.ck_new(), 1))));
// L-FALSIF: the legality reader must be able to reject something.
{
  const g = cells(start);
  const bogus = { from: 40, to: 41, cap: -1 };   // sideways, not diagonal
  ok('control: the legality reader rejects a non-diagonal move',
     illegal(g, 0, bogus) !== null, illegal(g, 0, bogus) ?? 'accepted it');
}

console.log(fail === 0
  ? `\nPASS: Checkers generates only legal moves and keeps its pieces (${pass} arms).`
  : `\nFAIL: ${fail} of ${pass + fail} arms.`);
process.exitCode = fail === 0 ? 0 : 1;
