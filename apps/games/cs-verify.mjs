// Grade the Chess wasm module.
//
// The arm that carries this file is a SECOND MOVE GENERATOR, written here
// from the rules of chess and reading nothing but the board, the side to
// move, the en passant square and the four castling rights that the module
// reports. Every position reached in the seeded games below is generated
// twice and the two lists must agree as sets. One illegal move offered
// makes every game after it meaningless, and nothing about watching the
// pieces move would show it.
//
// The second reading is deliberately shaped differently from the engine's.
// Chess.codex answers "is this square attacked" by scanning outward FROM
// the square; this file answers it by generating the attack set of every
// enemy piece and asking whether the square is in it. A defect that
// survives both has to be a defect in the rules as both files read them,
// not a slip in either walk.
//
// Beside the two generators there is a published oracle: perft from the
// opening position is 20, 400, 8902 and 197281, which are counts nobody
// here computed. codex/test/apps/chess-legality reads the same four off the
// bare-metal engine, so a disagreement here is a wasm parity finding.
//
// Usage: node apps/games/cs-verify.mjs [path/to/chess.wasm]

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const wasmPath = process.argv[2] ||
  join(here, '..', 'landing', 'web', 'games', 'chess.wasm');

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

// --- the encoding, read off Chess.codex ----------------------------------
// index = row * 8 + col, row 0 is rank 8, White advances toward row 0.
// 1..6 White pawn knight bishop rook queen king, 7..12 the Black six.
const R = i => i >> 3, C = i => i & 7, SQ = (r, c) => r * 8 + c;
const side = v => (v === 0 ? -1 : v > 6 ? 1 : 0);
const kind = v => (v > 6 ? v - 6 : v);
const NAME = ['a', 'b', 'c', 'd', 'e', 'f', 'g', 'h'];
const sqName = i => (i < 0 ? '--' : NAME[C(i)] + (8 - R(i)));

const KN = [[-2, -1], [-2, 1], [-1, -2], [-1, 2], [1, -2], [1, 2], [2, -1], [2, 1]];
const KG = [[-1, -1], [-1, 0], [-1, 1], [0, -1], [0, 1], [1, -1], [1, 0], [1, 1]];
const DIAG = [[-1, -1], [-1, 1], [1, -1], [1, 1]];
const ORTH = [[-1, 0], [1, 0], [0, -1], [0, 1]];

// --- the second reading --------------------------------------------------
function attacked(cells, target, by) {
  for (let s = 0; s < 64; s++) {
    const v = cells[s];
    if (v === 0 || side(v) !== by) continue;
    const k = kind(v), r = R(s), c = C(s), tr = R(target), tc = C(target);
    if (k === 1) {
      const dr = by === 0 ? -1 : 1;
      if (r + dr === tr && Math.abs(c - tc) === 1) return true;
      continue;
    }
    if (k === 2 || k === 6) {
      for (const [dr, dc] of (k === 2 ? KN : KG)) {
        if (r + dr === tr && c + dc === tc) return true;
      }
      continue;
    }
    const dirs = k === 3 ? DIAG : k === 4 ? ORTH : DIAG.concat(ORTH);
    for (const [dr, dc] of dirs) {
      let rr = r + dr, cc = c + dc;
      while (rr >= 0 && rr < 8 && cc >= 0 && cc < 8) {
        const t = SQ(rr, cc);
        if (t === target) return true;
        if (cells[t] !== 0) break;
        rr += dr; cc += dc;
      }
    }
  }
  return false;
}

function pseudo(st) {
  const { cells, turn, ep, rights } = st;
  const out = [];
  const last = turn === 0 ? 0 : 7;
  const promos = turn === 0 ? [5, 4, 3, 2] : [11, 10, 9, 8];
  const add = (from, to, captured, promote, special) =>
    out.push({ from, to, captured, promote, special });
  for (let s = 0; s < 64; s++) {
    const v = cells[s];
    if (v === 0 || side(v) !== turn) continue;
    const k = kind(v), r = R(s), c = C(s);
    if (k === 1) {
      const dir = turn === 0 ? -1 : 1;
      const home = turn === 0 ? 6 : 1;
      const r1 = r + dir;
      if (r1 >= 0 && r1 < 8 && cells[SQ(r1, c)] === 0) {
        const d = SQ(r1, c);
        if (r1 === last) for (const p of promos) add(s, d, 0, p, 0);
        else add(s, d, 0, 0, 0);
        const r2 = r + dir * 2;
        if (r === home && r2 >= 0 && r2 < 8 && cells[SQ(r2, c)] === 0) {
          add(s, SQ(r2, c), 0, 0, 1);
        }
      }
      for (const dc of [-1, 1]) {
        const rr = r + dir, cc = c + dc;
        if (rr < 0 || rr > 7 || cc < 0 || cc > 7) continue;
        const d = SQ(rr, cc);
        // The en passant square is tested before occupancy, as the engine
        // tests it: the square a pawn skipped over is empty by construction.
        if (d === ep) { add(s, d, turn === 0 ? 7 : 1, 0, 2); continue; }
        const t = cells[d];
        if (t === 0 || side(t) === turn) continue;
        if (rr === last) for (const p of promos) add(s, d, t, p, 0);
        else add(s, d, t, 0, 0);
      }
      continue;
    }
    if (k === 2 || k === 6) {
      for (const [dr, dc] of (k === 2 ? KN : KG)) {
        const rr = r + dr, cc = c + dc;
        if (rr < 0 || rr > 7 || cc < 0 || cc > 7) continue;
        const d = SQ(rr, cc), t = cells[d];
        if (t !== 0 && side(t) === turn) continue;
        add(s, d, t, 0, 0);
      }
      if (k === 6 && s === (turn === 0 ? 60 : 4)) castles(st, s, add);
      continue;
    }
    const dirs = k === 3 ? DIAG : k === 4 ? ORTH : DIAG.concat(ORTH);
    for (const [dr, dc] of dirs) {
      let rr = r + dr, cc = c + dc;
      while (rr >= 0 && rr < 8 && cc >= 0 && cc < 8) {
        const d = SQ(rr, cc), t = cells[d];
        if (t !== 0 && side(t) === turn) break;
        add(s, d, t, 0, 0);
        if (t !== 0) break;
        rr += dr; cc += dc;
      }
    }
  }
  return out;
}

// A castle needs the right, the rook still on its corner, every square
// between empty, and a king that is not in check, does not pass through an
// attacked square, and does not land on one.
function castles(st, king, add) {
  const { cells, turn, rights } = st;
  const by = 1 - turn;
  const rook = turn === 0 ? 4 : 10;
  const spec = turn === 0
    ? [{ right: rights.wk, corner: 63, through: [61, 62], empty: [61, 62], to: 62 },
       { right: rights.wq, corner: 56, through: [59, 58], empty: [59, 58, 57], to: 58 }]
    : [{ right: rights.bk, corner: 7, through: [5, 6], empty: [5, 6], to: 6 },
       { right: rights.bq, corner: 0, through: [3, 2], empty: [3, 2, 1], to: 2 }];
  for (const s of spec) {
    if (!s.right) continue;
    if (cells[s.corner] !== rook) continue;
    if (s.empty.some(q => cells[q] !== 0)) continue;
    if (attacked(cells, king, by)) continue;
    if (s.through.some(q => attacked(cells, q, by))) continue;
    add(king, s.to, 0, 0, 3);
  }
}

function applyMove(st, m) {
  const cells = st.cells.slice();
  const piece = cells[m.from];
  cells[m.from] = 0;
  cells[m.to] = m.promote !== 0 ? m.promote : piece;
  if (m.special === 2) cells[SQ(R(m.from), C(m.to))] = 0;
  if (m.special === 3) {
    const shift = (from, to) => { cells[to] = cells[from]; cells[from] = 0; };
    if (m.to === 62) shift(63, 61);
    else if (m.to === 58) shift(56, 59);
    else if (m.to === 6) shift(7, 5);
    else shift(0, 3);
  }
  const ep = m.special === 1 ? SQ((R(m.from) + R(m.to)) / 2, C(m.from)) : -1;
  const quiet = kind(piece) !== 1 && m.captured === 0;
  return {
    cells,
    turn: 1 - st.turn,
    ep,
    half: quiet ? st.half + 1 : 0,
    rights: {
      wk: st.rights.wk && m.from !== 60 && m.from !== 63 && m.to !== 63,
      wq: st.rights.wq && m.from !== 60 && m.from !== 56 && m.to !== 56,
      bk: st.rights.bk && m.from !== 4 && m.from !== 7 && m.to !== 7,
      bq: st.rights.bq && m.from !== 4 && m.from !== 0 && m.to !== 0,
    },
  };
}

const kingSq = (cells, s) => cells.indexOf(s === 0 ? 6 : 12);
const inCheck = (st, s) => {
  const k = kingSq(st.cells, s);
  return k >= 0 && attacked(st.cells, k, 1 - s);
};
const legal = st => pseudo(st).filter(m => !inCheck(applyMove(st, m), st.turn));

// --- reading the module's state ------------------------------------------
const readState = h => ({
  cells: [...Array(64)].map((_, i) => e.cs_cell(h, i)),
  turn: e.cs_turn(h),
  ep: e.cs_ep(h),
  half: e.cs_half(h),
  rights: {
    wk: e.cs_right(h, 0) === 1, wq: e.cs_right(h, 1) === 1,
    bk: e.cs_right(h, 2) === 1, bq: e.cs_right(h, 3) === 1,
  },
});

const moduleMoves = h => [...Array(e.cs_moves(h))].map((_, i) => ({
  from: e.cs_move_from(h, i), to: e.cs_move_to(h, i),
  captured: e.cs_move_cap(h, i), promote: e.cs_move_promote(h, i),
  special: e.cs_move_special(h, i),
}));

const key = m => `${m.from},${m.to},${m.captured},${m.promote},${m.special}`;
const asSet = ms => ms.map(key).sort();
const diff = (a, b) => {
  const A = new Set(a), B = new Set(b);
  return {
    onlyModule: a.filter(k => !B.has(k)),
    onlyHere: b.filter(k => !A.has(k)),
  };
};

console.log(`cs-verify ${wasmPath}`);

// --- the opening position ------------------------------------------------
const start = e.cs_new();
{
  const st = readState(start);
  ok('the handle is a live address', start > 0, `0x${start.toString(16)}`);
  ok('thirty-two pieces, sixteen a side',
     st.cells.filter(v => side(v) === 0).length === 16 &&
     st.cells.filter(v => side(v) === 1).length === 16);
  ok('White is to move, no en passant, all four rights, clock at zero',
     st.turn === 0 && st.ep === -1 && st.half === 0 &&
     st.rights.wk && st.rights.wq && st.rights.bk && st.rights.bq);
  ok('the opening offers twenty moves', e.cs_moves(start) === 20, e.cs_moves(start));
  ok('nothing is decided and nobody is in check',
     e.cs_done(start) === 0 && e.cs_result(start) === -1 && e.cs_check(start) === 0);
  ok('material is level', e.cs_material(start, 0) === 0 && e.cs_material(start, 1) === 0);
}

// --- the published oracle ------------------------------------------------
{
  const want = [1, 20, 400, 8902];
  for (let d = 1; d <= 3; d++) {
    const got = e.cs_perft(start, d);
    ok(`perft ${d} from the opening position`, got === want[d], `${got} (published ${want[d]})`);
  }
  const t0 = Date.now();
  const got = e.cs_perft(start, 4);
  ok('perft 4 from the opening position', got === 197281,
     `${got} (published 197281) in ${((Date.now() - t0) / 1000).toFixed(1)}s`);
}

// --- the two generators, over seeded games -------------------------------
// A seeded walk rather than a fixed list of positions, because the
// positions worth comparing are the ones neither file's author chose.
let rng = 0;
const nextRand = () => { rng = (rng * 1103515245 + 12345) & 0x7fffffff; return rng; };

function walkGame(seed, plies) {
  rng = seed;
  e.__heap_reset();
  let h = e.cs_new();
  const bad = [];
  let played = 0, sawCastle = 0, sawEp = 0, sawPromote = 0;
  for (let ply = 0; ply < plies; ply++) {
    const st = readState(h);
    const ms = moduleMoves(h);
    const mine = asSet(ms);
    const hers = asSet(legal(st));
    if (mine.length !== hers.length || mine.some((k, i) => k !== hers[i])) {
      bad.push({ ply, ...diff(mine, hers), fen: st.cells.join('') });
      break;
    }
    const n = e.cs_moves(h);
    if (n === 0 || e.cs_done(h) === 1) break;
    // A uniform walk agrees about a narrower game than chess: over 707
    // positions it castled never and took en passant never, so the three
    // rules that are always the ones left out were two thirds unreached
    // (L-CONSTRUCT). When a castle or an en passant capture is on offer the
    // walk takes it two times in three, which reaches both and still leaves
    // the line varied.
    const special = ms.map((m, i) => [i, m.special])
      .filter(([, s]) => s === 2 || s === 3).map(([i]) => i);
    const pick = (special.length && nextRand() % 3 !== 0)
      ? special[nextRand() % special.length]
      : nextRand() % n;
    if (ms[pick].special === 3) sawCastle++;
    if (ms[pick].special === 2) sawEp++;
    if (ms[pick].promote !== 0) sawPromote++;
    h = e.cs_apply(h, pick);
    played++;
  }
  return { bad, played, sawCastle, sawEp, sawPromote };
}

{
  let positions = 0, castled = 0, epTaken = 0, promoted = 0;
  const failures = [];
  for (const seed of [1, 7, 99, 4242, 31337, 8675309, 271828, 161803]) {
    const r = walkGame(seed, 90);
    positions += r.played;
    castled += r.sawCastle; epTaken += r.sawEp; promoted += r.sawPromote;
    for (const b of r.bad) failures.push({ seed, ...b });
  }
  ok('the two generators agree on every position of eight seeded games',
     failures.length === 0, `${positions} positions`);
  if (failures.length) {
    for (const f of failures.slice(0, 3)) {
      console.log(`        seed ${f.seed} ply ${f.ply}`);
      console.log(`        the module offers and this file does not: ${f.onlyModule.join(' ') || '(none)'}`);
      console.log(`        this file offers and the module does not:  ${f.onlyHere.join(' ') || '(none)'}`);
      console.log(`        cells ${f.fen}`);
    }
  }
  // The walk is only worth its runtime if it REACHED the three rules that
  // are always the ones left out. A run that met none of them agrees about
  // a narrower game than chess (L-CONSTRUCT).
  ok('the walk reached castling, en passant and promotion',
     castled > 0 && epTaken > 0 && promoted > 0,
     `${castled} castles, ${epTaken} en passant captures, ${promoted} promotions`);
}

// --- the control: the comparison can fail --------------------------------
// Two lists that always agree read exactly like two correct generators
// (L-FALSIF). So compare the module's LEGAL list against this file's
// PSEUDO-LEGAL list in a position where a pin or a check separates them.
// If that comparison passes, every arm above proved nothing.
{
  e.__heap_reset();
  let h = e.cs_new();
  // 1 f2f3 e7e5 2 g2g4 Qd8h4 is mate, and White is in check at the end, so
  // the pseudo-legal list is far larger than the legal one.
  for (const [from, to] of [[53, 45], [12, 28], [54, 38], [3, 39]]) {
    const i = e.cs_find(h, from, to);
    if (i < 0) { ok(`the fool's mate line is playable (${sqName(from)}${sqName(to)})`, false); break; }
    h = e.cs_apply(h, i);
  }
  const st = readState(h);
  const legalHere = asSet(legal(st));
  const pseudoHere = asSet(pseudo(st));
  const mine = asSet(moduleMoves(h));
  ok('the module agrees with the legal reading of the mated position',
     mine.length === legalHere.length && mine.every((k, i) => k === legalHere[i]),
     `${mine.length} moves`);
  ok('control: the same comparison REFUSES the pseudo-legal reading',
     pseudoHere.length !== legalHere.length,
     `pseudo-legal ${pseudoHere.length} against legal ${legalHere.length}`);
  ok('the fool\'s mate is mate: no legal move, in check, White has lost',
     e.cs_moves(h) === 0 && e.cs_check(h) === 1 && e.cs_done(h) === 1 && e.cs_result(h) === 1,
     `moves ${e.cs_moves(h)} check ${e.cs_check(h)} result ${e.cs_result(h)}`);
  ok('a mated side is offered no move to apply', e.cs_apply(h, 0) === h);
}

// --- the AI finds a mate in one ------------------------------------------
// A win rate would agree with an engine that had no search at all, which is
// the bar GAME-13 records. A mate in one either is found or is not.
{
  e.__heap_reset();
  let h = e.cs_new();
  for (const [from, to] of [[53, 45], [12, 28], [54, 38]]) h = e.cs_apply(h, e.cs_find(h, from, to));
  const beforeSearch = readState(h).cells.join(',');
  const i = e.cs_ai(h, 2);
  const from = e.cs_move_from(h, i), to = e.cs_move_to(h, i);
  ok('the search plays the mate in one', from === 3 && to === 39,
     `${sqName(from)}${sqName(to)} (want d8h4)`);
  const after = e.cs_apply(h, i);
  ok('and the position it chose is mate',
     e.cs_moves(after) === 0 && e.cs_check(after) === 1 && e.cs_result(after) === 1);
  ok('the search left the board it was asked about alone',
     readState(h).cells.join(',') === beforeSearch && e.cs_turn(h) === 1);
}

// --- the handle contract -------------------------------------------------
// GAME-11 warns that a handle is dead once played through, because
// list-set-at shares its list with the board it came from. Chess.codex
// copies the cells inside chess-apply, so a chess handle SURVIVES, and the
// page is entitled to hold the previous position. That is worth an arm
// rather than a sentence.
{
  e.__heap_reset();
  const a = e.cs_new();
  const before = readState(a).cells.join(',');
  const i = e.cs_find(a, 52, 36); // 1 e2e4
  const b = e.cs_apply(a, i);
  ok('a played move answers a different handle', b !== a && b > 0);
  ok('the old handle still reads the position it named',
     readState(a).cells.join(',') === before);
  ok('the new handle has the pawn on e4 and none on e2',
     e.cs_cell(b, 36) === 1 && e.cs_cell(b, 52) === 0);
  ok('a second opening board is a different address', e.cs_new() !== a);
  ok('the two boards disagree about e4',
     e.cs_cell(a, 36) === 0 && e.cs_cell(b, 36) === 1);
  ok('asking the search does not move a piece',
     (() => { const c = readState(a).cells.join(','); e.cs_ai(a, 1); return readState(a).cells.join(',') === c; })());
}

// --- refusals ------------------------------------------------------------
// A guard that ANSWERS instead of refusing ships a wrong number no caller
// can tell from a right one (L-BAILVALUE), so each of these asks what the
// refusal RETURNS.
{
  e.__heap_reset();
  const h = e.cs_new();
  ok('a cell below the board is refused', e.cs_cell(h, -1) === -1);
  ok('a cell past the board is refused', e.cs_cell(h, 64) === -1);
  ok('a castling right that does not exist is refused', e.cs_right(h, 4) === -1);
  ok('a move index past the list is refused', e.cs_apply(h, 999) === h);
  ok('a move index below the list is refused', e.cs_apply(h, -1) === h);
  ok('a query about a move past the list answers -1',
     e.cs_move_from(h, 99) === -1 && e.cs_move_to(h, 99) === -1 &&
     e.cs_move_promote(h, 99) === -1 && e.cs_move_special(h, 99) === -1);
  ok('a from-square with no move answers 0 and one with a move answers 1',
     e.cs_can(h, 28) === 0 && e.cs_can(h, 52) === 1);
  ok('a from-and-to pair that is not a move answers -1', e.cs_find(h, 52, 20) === -1);
  ok('the search depth is clamped rather than trusted',
     e.cs_ai(h, 99) >= 0 && e.cs_ai(h, -5) >= 0);
}

const pages = e.memory.buffer.byteLength / 65536;
console.log(`\n${pass} passed, ${fail} failed; the module grew to ${pages} pages (${(pages * 64 / 1024).toFixed(1)} MB)`);
if (fail) process.exitCode = 1;
