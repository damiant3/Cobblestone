// Grade the Connect Four wasm module.
//
// Connect Four has no exhaustive answer key the way TicTacToe does. It is
// solved in the literature, but not by anything in this tree, and a table
// we cannot reproduce is not an oracle we own. So the bar here is the one
// GAME-11's sibling games will use:
//
//   1. rule invariants that hold at EVERY reachable position,
//   2. an agreement arm between two independent statements of legality,
//   3. TACTICAL properties that are decidable by inspection -- an engine
//      that fails to take a win in one, or to block a loss in one, is
//      wrong no matter what its search says,
//   4. controls that fire.
//
// (3) is what makes this more than "it plays". A heuristic that never
// looked at threats would pass every invariant in (1) and still be a bad
// engine, and no amount of self-play would say so.
//
// Usage: node apps/games/c4-verify.mjs [path/to/connect4.wasm]

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const wasmPath = process.argv[2] ||
  join(here, '..', 'landing', 'web', 'games', 'connect4.wasm');

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

const grid = h => [...Array(6)].map((_, r) =>
  [...Array(7)].map((_, c) => e.c4_cell(h, r, c)));
const flat = h => grid(h).flat();
const drops = (h, cols) => cols.reduce((b, c) => e.c4_drop(b, c), h);

console.log(`c4-verify ${wasmPath}`);

// -- The opening position -------------------------------------------------
const start = e.c4_new();
ok('a new board is 42 empty cells', flat(start).every(v => v === 0),
   flat(start).filter(v => v !== 0).length + ' occupied');
ok('player 1 moves first, nothing decided',
   e.c4_cur(start) === 1 && e.c4_done(start) === 0 && e.c4_winner(start) === 0);
ok('every column starts empty and playable',
   [...Array(7)].every((_, c) => e.c4_height(start, c) === 0 && e.c4_can(start, c) === 1));

// -- Gravity --------------------------------------------------------------
const one = e.c4_drop(start, 3);
ok('a piece lands on the bottom row, not where it was dropped',
   e.c4_cell(one, 5, 3) === 1 && e.c4_cell(one, 0, 3) === 0,
   `row5=${e.c4_cell(one, 5, 3)} row0=${e.c4_cell(one, 0, 3)}`);
ok('the column height follows the piece', e.c4_height(one, 3) === 1);
ok('the turn passes', e.c4_cur(one) === 2);

// -- THE COPY ARM ---------------------------------------------------------
// connect4-drop writes through list-set-at to BOTH lists. Without the copy
// the caller's own board takes the move.
ok('THE COPY ARM: the board dropped from is untouched',
   flat(start).every(v => v === 0) && e.c4_height(start, 3) === 0,
   `cells ${flat(start).filter(v => v !== 0).length}, height ${e.c4_height(start, 3)}`);

// A column filled to the top must refuse, and refuse by identity.
let tall = start;
for (let i = 0; i < 6; i++) tall = e.c4_drop(tall, 0);
ok('six pieces fill a column', e.c4_height(tall, 0) === 6, e.c4_height(tall, 0));
ok('a full column is refused', e.c4_can(tall, 0) === 0 && e.c4_drop(tall, 0) === tall);
ok('an off-board column is refused',
   e.c4_can(tall, 7) === 0 && e.c4_can(tall, -1) === 0 &&
   e.c4_drop(tall, 7) === tall && e.c4_drop(tall, -1) === tall);

// -- Winning --------------------------------------------------------------
// P1 takes columns 0,1,2,3 on the bottom row while P2 answers in column 6.
const won = drops(start, [0, 6, 1, 6, 2, 6, 3]);
ok('four in a row ends the game', e.c4_done(won) === 1, e.c4_done(won));
ok('the winner is the player who made the four', e.c4_winner(won) === 1, e.c4_winner(won));
ok('a finished game refuses every further move',
   [...Array(7)].every((_, c) => e.c4_can(won, c) === 0 && e.c4_drop(won, c) === won));

// A vertical four, so the win detector is not only reading rows.
const vert = drops(start, [0, 1, 0, 1, 0, 1, 0]);
ok('four in a column also wins', e.c4_done(vert) === 1 && e.c4_winner(vert) === 1);

// -- TACTICS: the arms that judge the engine, not the rules ---------------
// P1 holds 0,1,2 on the bottom row and it is P1 to move: column 3 wins now.
const canWin = drops(start, [0, 6, 1, 6, 2, 5]);
ok('setup: it is player 1 to move with three in a row',
   e.c4_cur(canWin) === 1 && e.c4_done(canWin) === 0);
const winPick = e.c4_ai(canWin);
ok('TACTIC: the engine takes a win in one', winPick === 3, `chose column ${winPick}`);
const after = e.c4_drop(canWin, winPick);
ok('and taking it actually wins', e.c4_done(after) === 1 && e.c4_winner(after) === 1);

// Same shape, but P2 to move: column 3 must be BLOCKED or P1 wins next.
const mustBlock = drops(start, [0, 6, 1, 6, 2]);
ok('setup: it is player 2 to move against three in a row',
   e.c4_cur(mustBlock) === 2 && e.c4_done(mustBlock) === 0);
const blockPick = e.c4_ai(mustBlock);
ok('TACTIC: the engine blocks a loss in one', blockPick === 3, `chose column ${blockPick}`);

// -- Invariants over whole games -----------------------------------------
function selfPlay(seed) {
  let h = e.c4_new(), s = seed, plies = 0;
  while (e.c4_done(h) === 0 && plies < 42) {
    // Alternate the engine with a seeded chooser so the games differ.
    let col;
    if (plies % 2 === 0) col = e.c4_ai(h);
    else {
      s = (s * 1103515245 + 12345) & 0x7fffffff;
      const legal = [...Array(7)].map((_, c) => c).filter(c => e.c4_can(h, c) === 1);
      if (legal.length === 0) break;
      col = legal[s % legal.length];
    }
    if (col < 0 || e.c4_can(h, col) !== 1) return { bad: `illegal choice ${col}`, h };
    h = e.c4_drop(h, col);
    plies++;
    // Gravity must hold everywhere: no piece may float above a hole.
    for (let c = 0; c < 7; c++) {
      const height = e.c4_height(h, c);
      for (let r = 0; r < 6; r++) {
        const filled = e.c4_cell(h, r, c) !== 0;
        const shouldBeFilled = r >= 6 - height;
        if (filled !== shouldBeFilled) return { bad: `col ${c} row ${r} floats`, h };
      }
    }
  }
  return { h, plies };
}

let bad = null, finishes = 0, engineLosses = 0;
for (let seed = 1; seed <= 40 && !bad; seed++) {
  const r = selfPlay(seed);
  if (r.bad) { bad = `seed ${seed}: ${r.bad}`; break; }
  if (e.c4_done(r.h) === 1) {
    finishes++;
    if (e.c4_winner(r.h) === 2) engineLosses++;
  }
}
ok('no position in 40 games floated a piece or played illegally', bad === null, bad ?? 'clean');
ok('the games reach a decision', finishes >= 38, `${finishes} of 40 decided`);
// The engine plays player 1 against a random legal chooser. Losing to random
// is a real defect; this is a property, not a tuned threshold.
ok('the engine never loses to a random player', engineLosses === 0,
   `${engineLosses} losses`);

// -- Agreement: c4_can against the engine's own refusal -------------------
{
  let h = e.c4_new(), checked = 0, plies = 0, disagreed = [];
  for (let ply = 0; ply < 42 && e.c4_done(h) === 0; ply++) {
    for (let c = -1; c <= 7; c++) {
      const can = e.c4_can(h, c) === 1;
      const moved = e.c4_drop(h, c) !== h;
      checked++;
      if (can !== moved) disagreed.push(`col ${c}: can=${can} moved=${moved}`);
    }
    const col = e.c4_ai(h);
    if (col < 0) break;
    h = e.c4_drop(h, col);
    plies++;
  }
  ok('c4_can agrees with c4_drop on every column of a whole game',
     disagreed.length === 0,
     `${checked} judgements, ${disagreed.length} disagreements` +
     (disagreed.length ? ': ' + disagreed.slice(0, 3).join('; ') : ''));
  // The bound is the game's, not a number picked to pass: four in a row
  // cannot appear before player 1's fourth move, so any decided game runs
  // at least 7 plies and the loop judges 9 columns at each of them.
  ok('control: the agreement loop ran a real game, not one position',
     plies >= 7 && checked === plies * 9, `${plies} plies, ${checked} judgements`);
}

// -- Controls -------------------------------------------------------------
ok('control: two new boards are different handles', e.c4_new() !== e.c4_new());
ok('control: dropping in different columns gives different boards',
   JSON.stringify(flat(e.c4_drop(e.c4_new(), 0))) !==
   JSON.stringify(flat(e.c4_drop(e.c4_new(), 6))));
// If the AI ignored the position it would answer one column forever.
{
  const seen = new Set();
  let h = e.c4_new();
  while (e.c4_done(h) === 0) {
    const c = e.c4_ai(h);
    if (c < 0) break;
    seen.add(c);
    h = e.c4_drop(h, c);
  }
  ok('control: the engine is not a constant function over real positions',
     seen.size > 1, `chose ${[...seen].sort((a, b) => a - b).join(',')}`);
}

console.log(fail === 0
  ? `\nPASS: the Connect Four module holds its rules and its tactics (${pass} arms).`
  : `\nFAIL: ${fail} of ${pass + fail} arms.`);
process.exitCode = fail === 0 ? 0 : 1;
