// Grade the Othello wasm module.
//
// Othello has no conservation law worth anything: pieces are created and
// flipped every move, so a total tells you nothing. What it does have is a
// complete, cheap oracle. Legality, the set of flipped cells, whose turn it
// is after a move and when the game ends are all decidable from the board
// alone, so this file implements the rules a second time in JavaScript and
// plays whole games in lockstep with the module, comparing all sixty-four
// cells after every ply.
//
// The rules the second implementation is written from: a move must be on an
// empty cell and must flip at least one piece; a piece flips when it lies on
// a straight line between the placed piece and another of the mover's pieces
// with no gap; a player with no legal move passes; the game ends when
// neither player can move; the winner is whoever has more pieces.
//
// Usage: node apps/games/ot-verify.mjs [path/to/othello.wasm]

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const wasmPath = process.argv[2] ||
  join(here, '..', 'landing', 'web', 'games', 'othello.wasm');

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

const EMPTY = 0, BLACK = 1, WHITE = 2;
const cells = h => [...Array(64)].map((_, i) => e.ot_cell(h, i));

// -- The oracle, written from the rules ----------------------------------
const DIRS = [[-1,-1],[-1,0],[-1,1],[0,-1],[0,1],[1,-1],[1,0],[1,1]];
const other = p => (p === BLACK ? WHITE : BLACK);

function oracleStart() {
  const b = new Array(64).fill(EMPTY);
  b[27] = WHITE; b[28] = BLACK; b[35] = BLACK; b[36] = WHITE;
  return b;
}

// Every cell this move would flip, as indices. Empty means the move is illegal.
function oracleFlips(b, player, idx) {
  if (b[idx] !== EMPTY) return [];
  const r0 = Math.floor(idx / 8), c0 = idx % 8, out = [];
  for (const [dr, dc] of DIRS) {
    const run = [];
    let r = r0 + dr, c = c0 + dc;
    while (r >= 0 && r < 8 && c >= 0 && c < 8) {
      const v = b[r * 8 + c];
      if (v === EMPTY) { run.length = 0; break; }
      if (v === player) break;
      run.push(r * 8 + c);
      r += dr; c += dc;
    }
    // A run only counts when it was closed by one of the mover's own pieces.
    if (run.length && r >= 0 && r < 8 && c >= 0 && c < 8 && b[r * 8 + c] === player) {
      out.push(...run);
    }
  }
  return out;
}

const oracleLegal = (b, p, i) => oracleFlips(b, p, i).length > 0;
const oracleMoves = (b, p) => [...Array(64)].map((_, i) => i).filter(i => oracleLegal(b, p, i));

function oracleApply(b, player, idx) {
  const flips = oracleFlips(b, player, idx);
  const nb = b.slice();
  nb[idx] = player;
  for (const j of flips) nb[j] = player;
  return nb;
}

const count = (b, p) => b.filter(v => v === p).length;
const oracleWinner = b => {
  const bc = count(b, BLACK), wc = count(b, WHITE);
  return bc > wc ? BLACK : wc > bc ? WHITE : 0;
};

console.log(`ot-verify ${wasmPath}`);

// -- The opening position -------------------------------------------------
{
  const h = e.ot_new();
  const c = cells(h), o = oracleStart();
  ok('the opening is four pieces crosswise', JSON.stringify(c) === JSON.stringify(o));
  ok('black to move, two each, no moves made',
     e.ot_player(h) === BLACK && e.ot_black(h) === 2 && e.ot_white(h) === 2 &&
     e.ot_moves(h) === 0 && e.ot_done(h) === 0);
  const legal = [...Array(64)].map((_, i) => i).filter(i => e.ot_legal(h, i) === 1);
  ok('black has exactly the four opening moves',
     JSON.stringify(legal) === JSON.stringify([19, 26, 37, 44]), legal.join(','));
  ok('a cell off the board is not legal and reads -1',
     e.ot_legal(h, 64) === 0 && e.ot_legal(h, -1) === 0 && e.ot_cell(h, 64) === -1);
  ok('nobody has won a game that has not finished', e.ot_winner(h) === 0);
}

// -- THE REFUSAL AND COPY ARMS -------------------------------------------
// Connect Four, Go and Mancala all mutated the board through a path that
// then reported the move rejected, so the test is to call the refusing
// operation and compare the board.
{
  const h = e.ot_new();
  const before = JSON.stringify(cells(h));

  const occupied = 27, noFlip = 0;
  ok('control: the two refusals are refusals the oracle agrees with',
     !oracleLegal(oracleStart(), BLACK, occupied) && !oracleLegal(oracleStart(), BLACK, noFlip));
  ok('placing on an occupied cell is refused', e.ot_place(h, occupied) === h);
  ok('placing where nothing flips is refused', e.ot_place(h, noFlip) === h);
  ok('placing off the board is refused',
     e.ot_place(h, 64) === h && e.ot_place(h, -1) === h);
  ok('THE REFUSAL ARM: the board is untouched by every refusal',
     JSON.stringify(cells(h)) === before, cells(h).filter(v => v !== EMPTY).length + ' pieces');

  const next = e.ot_place(h, 19);
  ok('a legal move answers a different state', next !== h);
  ok('THE COPY ARM: the board played from is untouched',
     JSON.stringify(cells(h)) === before);
  ok('the move landed on the new board and flipped one',
     e.ot_cell(next, 19) === BLACK && e.ot_cell(next, 27) === BLACK &&
     e.ot_black(next) === 4 && e.ot_white(next) === 1);
  ok('a finished game refuses a move', true, 'checked in the game loop below');
}

// -- Flip counts against the oracle, on the opening -----------------------
{
  const h = e.ot_new(), o = oracleStart();
  const bad = [];
  for (let i = 0; i < 64; i++) {
    const want = oracleFlips(o, BLACK, i).length;
    if (e.ot_flips(h, i) !== want) bad.push(`cell ${i}: engine ${e.ot_flips(h, i)}, rules say ${want}`);
  }
  ok('every flip count on the opening board matches the rules',
     bad.length === 0, bad.length ? bad.slice(0, 3).join('; ') : '64 cells');
}

// -- WHOLE GAMES IN LOCKSTEP ---------------------------------------------
// The module's AI is deterministic, so one game is one sample. These games
// diverge by playing a seeded random legal move for the first plies, which
// is what reaches positions where a player must pass.
let games = 0, plies = 0, passes = 0, finished = 0;
let winnerAgrees = 0, playerAtEndWrong = 0;
const results = [];
{
  const bad = [];
  let seed = 12345;
  const rnd = n => { seed = (seed * 1103515245 + 12345) & 0x7fffffff; return seed % n; };

  for (let g = 0; g < 40 && bad.length === 0; g++) {
    let h = e.ot_new();
    let b = oracleStart();
    let player = BLACK;
    let ply = 0;

    while (e.ot_done(h) === 0 && ply < 80) {
      // Whose turn, and what is legal, must agree before anything is played.
      if (e.ot_player(h) !== player) {
        bad.push(`game ${g} ply ${ply}: engine says ${e.ot_player(h)} to move, rules say ${player}`);
        break;
      }
      const mine = oracleMoves(b, player);
      const theirs = [...Array(64)].map((_, i) => i).filter(i => e.ot_legal(h, i) === 1);
      if (JSON.stringify(mine) !== JSON.stringify(theirs)) {
        bad.push(`game ${g} ply ${ply}: legal moves ${theirs.join(',')} against ${mine.join(',')}`);
        break;
      }
      if (mine.length === 0) { bad.push(`game ${g} ply ${ply}: not over, but nothing is legal`); break; }

      const pick = g < 20 && ply < 8 ? mine[rnd(mine.length)] : e.ot_ai(h);
      if (!mine.includes(pick)) { bad.push(`game ${g} ply ${ply}: the AI picked illegal ${pick}`); break; }

      // The engine's own flip count for the move it is about to make.
      const wantFlips = oracleFlips(b, player, pick).length;
      if (e.ot_flips(h, pick) !== wantFlips) {
        bad.push(`game ${g} ply ${ply}: flips ${e.ot_flips(h, pick)} against ${wantFlips}`);
        break;
      }

      h = e.ot_place(h, pick);
      b = oracleApply(b, player, pick);
      ply++; plies++;

      if (JSON.stringify(cells(h)) !== JSON.stringify(b)) {
        bad.push(`game ${g} ply ${ply}: the boards diverged at move ${pick}`);
        break;
      }
      if (e.ot_black(h) !== count(b, BLACK) || e.ot_white(h) !== count(b, WHITE)) {
        bad.push(`game ${g} ply ${ply}: counts ${e.ot_black(h)}/${e.ot_white(h)} against ` +
                 `${count(b, BLACK)}/${count(b, WHITE)}`);
        break;
      }
      if (e.ot_moves(h) !== ply) {
        bad.push(`game ${g} ply ${ply}: move counter reads ${e.ot_moves(h)}`);
        break;
      }

      // Whose turn it is next: the opponent, unless the opponent must pass.
      const opp = other(player);
      if (oracleMoves(b, opp).length > 0) player = opp;
      else if (oracleMoves(b, player).length > 0) passes++;
      else {
        // Neither can move: the game is over and the engine must say so.
        if (e.ot_done(h) !== 1) bad.push(`game ${g} ply ${ply}: nobody can move and the engine plays on`);
        break;
      }
    }

    if (e.ot_done(h) === 1) {
      finished++;
      const want = oracleWinner(b);
      if (e.ot_winner(h) === want) winnerAgrees++;
      else bad.push(`game ${g}: winner ${e.ot_winner(h)} against ${want} ` +
                    `(${count(b, BLACK)}/${count(b, WHITE)})`);
      // The measurement the fix is for: the player to move at the end is not
      // the winner, and reporting it as one is right only by coincidence.
      if (e.ot_player(h) !== want) playerAtEndWrong++;
      results.push(`${count(b, BLACK)}/${count(b, WHITE)}`);
      if (e.ot_place(h, e.ot_ai(h)) !== h) bad.push(`game ${g}: a finished game accepted a move`);
      if (e.ot_ai(h) !== -1) bad.push(`game ${g}: a finished game still offers a move`);
    }
    games++;
  }

  ok('forty games run in lockstep with the rules, every cell after every ply',
     bad.length === 0, bad.length ? bad.slice(0, 3).join('; ') : `${plies} plies`);
  ok('control: enough plies for that to mean something', plies > 1500, plies);
  ok('control: at least one position forced a pass, so the pass rule was exercised',
     passes > 0, passes);
  ok('every game finished', finished === games, `${finished} of ${games}`);
  ok('the winner is the player with more pieces, in every finished game',
     winnerAgrees === finished, `${winnerAgrees} of ${finished}`);
}

// -- The winner is not the player to move --------------------------------
// Kept as a reported number, not an assertion: it is what makes the pinned
// single game in codex/test/apps/classic-games-run unable to see the defect
// this module's ot_winner exists to close.
console.log(`  note  the player to move at the end differs from the winner in ` +
            `${playerAtEndWrong} of ${finished} games`);

// -- Controls -------------------------------------------------------------
{
  ok('control: two new boards are different handles', e.ot_new() !== e.ot_new());
  // L-FALSIF: the oracle must reject a board it should reject.
  const o = oracleStart();
  ok('control: the oracle refuses a move that flips nothing',
     oracleFlips(o, BLACK, 0).length === 0 && oracleFlips(o, BLACK, 19).length === 1);
  ok('control: the oracle refuses to flip across a gap',
     oracleFlips([...Array(64)].map((_, i) => i === 0 ? BLACK : i === 2 ? WHITE : EMPTY),
                 BLACK, 3).length === 0);
  ok('control: the games were not all the same game',
     new Set(results).size > 1, `${new Set(results).size} distinct final scores`);
}

console.log(fail === 0
  ? `\nPASS: Othello plays by the rules (${pass} arms).`
  : `\nFAIL: ${fail} of ${pass + fail} arms.`);
process.exitCode = fail === 0 ? 0 : 1;
