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

// The WHOLE legal set, from the rules text, in rows and columns: a pending
// chain allows only that piece's captures (rule 5); otherwise any capture
// makes every non-capture illegal (rule 4). Compared as a set, so a move the
// engine leaves out is as wrong as one it adds.
function oracleSet(g, turn, chain) {
  const caps = [], slides = [];
  const fwd = turn === 0 ? -1 : 1;
  for (let sq = 0; sq < 64; sq++) {
    if (owner(g[sq]) !== turn) continue;
    if (chain >= 0 && sq !== chain) continue;
    const r = Math.floor(sq / 8), c = sq % 8;
    const dirs = isKing(g[sq]) ? [-1, 1] : [fwd];
    for (const dr of dirs) for (const dc of [-1, 1]) {
      const r1 = r + dr, c1 = c + dc, r2 = r + 2 * dr, c2 = c + 2 * dc;
      if (r1 < 0 || r1 > 7 || c1 < 0 || c1 > 7) continue;
      if (g[r1 * 8 + c1] === 0) slides.push(`${sq}-${r1 * 8 + c1}`);
      else if (owner(g[r1 * 8 + c1]) === 1 - turn && r2 >= 0 && r2 <= 7 && c2 >= 0 && c2 <= 7 &&
        g[r2 * 8 + c2] === 0) caps.push(`${sq}-${r2 * 8 + c2}x${r1 * 8 + c1}`);
    }
  }
  return (caps.length || chain >= 0 ? caps : slides).sort();
}
const engineSet = h => moves(h).map(m => m.cap >= 0 ? `${m.from}-${m.to}x${m.cap}` : `${m.from}-${m.to}`).sort();

function playGame(pick) {
  let h = e.ck_new(), plies = 0, lastCapture = 0, lastProgress = 0;
  const problems = [];
  while (e.ck_done(h) === 0 && plies < 400) {
    const g = cells(h), turn = e.ck_turn(h), ms = moves(h);
    if (ms.length === 0) break;
    for (const m of ms) {
      const why = illegal(g, turn, m);
      if (why) problems.push(`ply ${plies}: ${m.from}->${m.to} ${why}`);
    }
    const rulesSay = oracleSet(g, turn, e.ck_chain(h)), got = engineSet(h);
    if (JSON.stringify(rulesSay) !== JSON.stringify(got)) {
      problems.push(`ply ${plies}: legal ${JSON.stringify(got)}, rules say ${JSON.stringify(rulesSay)}`);
    }
    const idx = pick(h, ms.length, plies);
    const before = cells(h);
    const chosen = ms[idx];
    if (chosen.cap >= 0) lastCapture = plies;
    const crowns = !isKing(g[chosen.from]) && (turn === 0 ? Math.floor(chosen.to / 8) === 0 : Math.floor(chosen.to / 8) === 7);
    if (chosen.cap >= 0 || crowns) lastProgress = plies + 1;
    h = e.ck_apply(h, idx);
    const after = cells(h);
    // Rules 5 and 6 from the board, not from the engine's own chain: after a
    // capture that did not crown, the turn stays exactly when the piece can
    // capture again from where it landed.
    if (e.ck_done(h) === 0) {
      const goesOn = chosen.cap >= 0 && !crowns && oracleSet(after, turn, chosen.to).length > 0;
      const wantTurn = goesOn ? turn : 1 - turn, wantChain = goesOn ? chosen.to : -1;
      if (e.ck_turn(h) !== wantTurn || e.ck_chain(h) !== wantChain) {
        problems.push(`ply ${plies}: after ${chosen.from}->${chosen.to} turn ${e.ck_turn(h)} chain ${e.ck_chain(h)}, rules say ${wantTurn} and ${wantChain}`);
      }
    }
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
  return { h, plies, problems, lastCapture, lastProgress };
}

{
  const all = [];
  let totalPlies = 0, draws = 0, wipeouts = 0, blockedWins = 0;
  const judged = [];
  const openEnded = [];
  for (let seed = 0; seed < 12; seed++) {
    // A seeded chooser rather than always the engine's pick, so the
    // generator is exercised on positions the engine would never reach.
    let s = seed * 7919 + 13;
    const pick = (h, n) => { s = (s * 1103515245 + 12345) & 0x7fffffff; return s % n; };
    const r = playGame(seed === 0 ? (h => e.ck_ai(h)) : pick);
    all.push(...r.problems.map(p => `seed ${seed} ${p}`));
    totalPlies += r.plies;
    if (e.ck_done(r.h) !== 1) { openEnded.push(`seed ${seed}: running after ${r.plies} plies`); continue; }
    // A finished game is a win for the side that moved last, because the side
    // to move has no piece or no legal move; or it is a draw at exactly eighty
    // plies without a capture or a crowning.
    const w = e.ck_winner(r.h), toMove = e.ck_turn(r.h), quiet = r.plies - r.lastProgress;
    if (w === 1 - toMove) {
      if (count(cells(r.h), toMove) > 0 && e.ck_moves(r.h) > 0) judged.push(`seed ${seed}: ${w} wins but ${toMove} can move`);
      else if (count(cells(r.h), toMove) > 0) blockedWins++; else wipeouts++;
    } else if (w === -1) {
      if (quiet !== 80) judged.push(`seed ${seed}: a draw after ${quiet} quiet plies, not 80`);
      else draws++;
    } else judged.push(`seed ${seed}: winner ${w} with ${toMove} to move`);
  }
  ok('every move offered in twelve games is legal, and the pieces account for',
     all.length === 0, all.length ? all.slice(0, 3).join('; ') : `${totalPlies} plies`);
  ok('every game ends, and every ending is a legal one',
     openEnded.length === 0 && judged.length === 0,
     [...openEnded, ...judged].slice(0, 3).join('; ') || `${wipeouts} wiped out, ${blockedWins} blocked, ${draws} drawn`);
  // CONTROL: the draw rule must be REACHED, or the arm above says nothing
  // about it. Before the rule, three of these twelve games were all-king
  // shuffles that never ended.
  ok('CONTROL: the corpus reaches the eighty-ply draw', draws > 0, `${draws} drawn`);  ok('control: the games were long enough to mean something', totalPlies > 100, totalPlies);
}

// -- A side with pieces and no legal move has lost ----------------------
// No game above reaches it, so the position is set up. North's one man on
// 49 (row 6) moves toward row 7, where 56 and 58 hold South men; it cannot
// jump them, because the landing squares are off the board. South's men on
// 40 and 42 also block those two men's jumps over 49, so South's only
// moves are slides. After any of them North still has a piece and nothing
// to do with it. The CONTROL frees 58, so the same slide leaves North a move.
{
  const setUp = (sqs) => sqs.reduce((h, [sq, v]) => e.ck_put(h, sq, v), e.ck_blank());
  const slide = (h) => {
    for (let i = 0; i < e.ck_moves(h); i++) if (e.ck_move_from(h, i) === 40) return i;
    return -1;
  };
  const blocked = setUp([[49, 3], [56, 1], [58, 1], [40, 1], [42, 1]]);
  const bi = slide(blocked);
  const after = bi >= 0 ? e.ck_apply(blocked, bi) : blocked;
  ok('a side with pieces and no legal move has lost',
     bi >= 0 && e.ck_done(after) === 1 && e.ck_winner(after) === 0 &&
     count(cells(after), 1) === 1 && e.ck_moves(after) === 0,
     `slide ${bi}, done ${e.ck_done(after)}, winner ${e.ck_winner(after)}, north pieces ${count(cells(after), 1)}`);
  const open = setUp([[49, 3], [56, 1], [40, 1], [42, 1]]);
  const oi = slide(open);
  const after2 = oi >= 0 ? e.ck_apply(open, oi) : open;
  ok('CONTROL: with 58 free the same slide leaves North a move, and play goes on',
     oi >= 0 && e.ck_done(after2) === 0 && e.ck_moves(after2) > 0,
     `done ${e.ck_done(after2)}, north moves ${e.ck_moves(after2)}`);
}
// -- THE RULES: one arm per board-state class -----------------------------
// South is player 0, men 1 and kings 2, moving toward row 0.
const setUp = (sqs, turn = 0) =>
  e.ck_put(sqs.reduce((h, [sq, v]) => e.ck_put(h, sq, v), e.ck_blank()), 64, turn);
const find = (h, from, to) => moves(h).findIndex(m => m.from === from && m.to === to);
const cls = (name, h, want) => {
  const got = engineSet(h);
  ok(`class: ${name}`, JSON.stringify(got) === JSON.stringify([...want].sort()),
     `legal ${JSON.stringify(got)}, want ${JSON.stringify([...want].sort())}`);
  return h;
};
cls('a capture is compulsory: the slides of 46 are refused', setUp([[42, 1], [33, 3], [46, 1]]), ['42-24x33']);
const two = cls('any capture may be chosen, the single as well as the start of a double',
    setUp([[40, 1], [33, 3], [45, 1], [36, 3], [20, 3]]), ['40-26x33', '45-27x36']);
const mid = e.ck_apply(two, find(two, 45, 27));
cls('a chain must go on: the same piece, and only its capture', mid, ['27-13x20']);
ok('class: while the chain goes on, the turn is not handed over',
   e.ck_turn(mid) === 0 && e.ck_chain(mid) === 27, `turn ${e.ck_turn(mid)} chain ${e.ck_chain(mid)}`);
const end = e.ck_apply(mid, find(mid, 27, 13));
ok('class: the chain ends when no capture is left, and the turn passes',
   e.ck_turn(end) === 1 && e.ck_chain(end) === -1 && e.ck_cell(end, 20) === 0 && e.ck_cell(end, 36) === 0,
   `turn ${e.ck_turn(end)} chain ${e.ck_chain(end)}`);
const crown = setUp([[17, 1], [10, 3], [12, 3], [60, 3]]);
const crowned = e.ck_apply(crown, find(crown, 17, 3));
ok('class: crowning ends the move, though the new king could capture again',
   e.ck_cell(crowned, 3) === 2 && e.ck_turn(crowned) === 1 && e.ck_chain(crowned) === -1,
   `cell ${e.ck_cell(crowned, 3)} turn ${e.ck_turn(crowned)} chain ${e.ck_chain(crowned)}`);
cls('a man does not capture backwards', setUp([[26, 1], [35, 3], [60, 3]]), ['26-17', '26-19']);
cls('a king captures backwards, and must', setUp([[26, 2], [35, 3], [60, 3]]), ['26-44x35']);
cls('a king moves one square, not flying', setUp([[27, 2], [60, 3]]), ['27-18', '27-20', '27-34', '27-36']);
cls('North captures toward row 7, and a chain goes on for North too',
    setUp([[21, 3], [28, 1], [44, 1], [0, 1]], 1), ['21-35x28']);
{
  const n = setUp([[21, 3], [28, 1], [44, 1], [0, 1]], 1);
  const n2 = e.ck_apply(n, find(n, 21, 35));
  cls('North\'s chain: the same piece, and only its capture', n2, ['35-53x44']);
}
const last = setUp([[42, 1], [33, 3]]);
const won = e.ck_apply(last, find(last, 42, 24));
ok('class: taking the last piece wins', e.ck_done(won) === 1 && e.ck_winner(won) === 0,
   `done ${e.ck_done(won)} winner ${e.ck_winner(won)}`);

// -- THE PAGE -------------------------------------------------------------
{
  const { GAMES } = await import('../landing/web/games/arcade.js');
  const g = GAMES.find(x => x.id === 'checkers');
  const click = (h, i, sel) => g.move(e, h, i, { sel, roll: null, rand: () => 1 });
  const p1 = click(two, 45, null);
  const p2 = click(two, 27, 45);
  ok('page: pick up 45, put it down on 27: the first capture of the chain', p1 && p1.sel === 45 && p2 && p2.handle,
     `${JSON.stringify(p1)} ${JSON.stringify(p2)}`);
  const h = p2.handle;
  ok('page: mid-chain the other piece cannot be picked up', click(h, 40, null) === null);
  const p3 = click(h, 27, null), p4 = p3 && click(h, 13, 27);
  ok('page: the chain piece is picked up again and finishes the chain',
     p3 && p3.sel === 27 && p4 && p4.handle && e.ck_turn(p4.handle) === 1, `${JSON.stringify(p3)} ${JSON.stringify(p4)}`);
  const slide = click(setUp([[42, 1], [33, 3], [46, 1]]), 46, null);
  ok('page: a piece with only slides is not picked up while a capture is owed', slide === null, JSON.stringify(slide));
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
