// Grade the Royal Game of Ur wasm module.
//
// Ur cannot be graded the way TicTacToe is. TicTacToe has an exhaustive
// answer key because perfect play is decidable over a 3x3 board; Ur has
// dice, so there is no line to enumerate. What is checkable instead is a
// set of invariants the rules impose on EVERY reachable position, driven
// over deterministic seeds so a failure reproduces exactly.
//
// The arm that earns its place is the copy arm. Before RoyalUrWasm copied
// the board, playing through a handle left the caller's own board holding
// the move with its scalar fields stale: piece 0 read 4 while moves-made
// read 0. That measured mixture is this file's control value, so the arm
// has a known failing reading rather than a hope.
//
// Usage: node apps/games/ur-verify.mjs [path/to/royalur.wasm]

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const wasmPath = process.argv[2] ||
  join(here, '..', 'landing', 'web', 'games', 'royalur.wasm');

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

const pieces = (h, p) => [...Array(7)].map((_, i) => e.ur_piece(h, p, i));

console.log(`ur-verify ${wasmPath}`);

// -- The opening position -------------------------------------------------
const start = e.ur_new();
ok('a new game gives all fourteen pieces at step 0',
   pieces(start, 1).every(s => s === 0) && pieces(start, 2).every(s => s === 0),
   JSON.stringify([pieces(start, 1), pieces(start, 2)]));
ok('player 1 is to move, nothing scored, no moves made',
   e.ur_cur(start) === 1 && e.ur_scored(start, 1) === 0 &&
   e.ur_scored(start, 2) === 0 && e.ur_moves(start) === 0 && e.ur_done(start) === 0);

// -- The dice -------------------------------------------------------------
// Four binary dice, so 0..4 with a mean near 2. A generator stuck on one
// value passes a range check and fails the spread check beside it.
const rolls = [...Array(400)].map((_, s) => e.ur_roll(s + 1));
ok('every roll is between 0 and 4', rolls.every(r => r >= 0 && r <= 4),
   `min ${Math.min(...rolls)} max ${Math.max(...rolls)}`);
const distinct = new Set(rolls).size;
ok('the dice produce more than one outcome', distinct >= 4, `${distinct} distinct values`);
ok('the same seed gives the same roll', e.ur_roll(12345) === e.ur_roll(12345));

// -- The copy arm: a handle is a value ------------------------------------
const before = e.ur_new();
const movedTo4 = e.ur_play(before, 0, 4);
ok('playing answers a different board', movedTo4 !== before, `${before} -> ${movedTo4}`);
ok('the played board advanced piece 0 by the roll', e.ur_piece(movedTo4, 1, 0) === 4,
   e.ur_piece(movedTo4, 1, 0));
ok('THE COPY ARM: the original board is untouched (read 4 before the copy landed)',
   e.ur_piece(before, 1, 0) === 0, e.ur_piece(before, 1, 0));
ok('the original board still records zero moves', e.ur_moves(before) === 0, e.ur_moves(before));

// -- A rosette grants another turn ----------------------------------------
// Step 4 is a rosette, so a piece moved 0 -> 4 leaves the same player to move.
ok('landing on the rosette at step 4 keeps the same player to move',
   e.ur_cur(movedTo4) === 1, e.ur_cur(movedTo4));
// Step 3 is not a rosette, so the turn passes.
const movedTo3 = e.ur_play(e.ur_new(), 0, 3);
ok('landing off a rosette passes the turn', e.ur_cur(movedTo3) === 2, e.ur_cur(movedTo3));

// -- Refusals -------------------------------------------------------------
const base = e.ur_new();
ok('a piece index off the end is refused', e.ur_play(base, 7, 2) === base);
ok('a negative piece index is refused', e.ur_play(base, -1, 2) === base);
ok('a roll of zero is refused as a move', e.ur_play(base, 0, 0) === base);
ok('a roll above four is refused', e.ur_play(base, 0, 5) === base);

// -- Passing --------------------------------------------------------------
const passed = e.ur_pass(base);
ok('passing hands the turn over and counts a move',
   e.ur_cur(passed) === 2 && e.ur_moves(passed) === 1);
ok('passing leaves the pieces alone', pieces(passed, 1).every(s => s === 0));

// -- ur_can must agree with ur_play's own refusal -------------------------
// Two independent statements of the same rule: ur-wasm-can mirrors the
// legality filter by hand, while ur_play refuses by answering the SAME
// handle back. Walking real positions, a disagreement means one of them is
// wrong, and this arm is the only thing in the file that can say which.
{
  let h = e.ur_new(), s = 7, guard = 0, checked = 0, disagreed = [];
  while (e.ur_done(h) === 0 && guard < 2000) {
    guard++;
    for (let i = 0; i < 7; i++) {
      for (let r = 1; r <= 4; r++) {
        const can = e.ur_can(h, i, r) === 1;
        const moved = e.ur_play(h, i, r) !== h;
        checked++;
        if (can !== moved) disagreed.push(`piece ${i} roll ${r}: can=${can} moved=${moved}`);
      }
    }
    const roll = e.ur_roll(s); s = (s * 1103515245 + 12345) & 0x7fffffff;
    if (roll === 0) { h = e.ur_pass(h); continue; }
    const idx = e.ur_ai(h, roll);
    h = idx < 0 ? e.ur_pass(h) : e.ur_play(h, idx, roll);
  }
  ok('ur_can agrees with ur_play on every piece and roll of a whole game',
     disagreed.length === 0, `${checked} judgements, ${disagreed.length} disagreements` +
     (disagreed.length ? ': ' + disagreed.slice(0, 3).join('; ') : ''));
  ok('control: that agreement check actually judged something', checked > 500, checked);
}

// -- A full game ----------------------------------------------------------
// Drive the module's own AI for both sides over a fixed seed stream and
// assert the invariants the rules impose on every position along the way.
function playGame(seed) {
  let h = e.ur_new(), s = seed, guard = 0;
  while (e.ur_done(h) === 0 && guard < 4000) {
    guard++;
    const roll = e.ur_roll(s); s = (s * 1103515245 + 12345) & 0x7fffffff;
    if (roll === 0) { h = e.ur_pass(h); continue; }
    const idx = e.ur_ai(h, roll);
    h = idx < 0 ? e.ur_pass(h) : e.ur_play(h, idx, roll);
    for (const p of [1, 2]) {
      for (const st of pieces(h, p)) {
        if (st < 0 || st > 15) return { bad: `step ${st} out of range`, h };
      }
      const sc = e.ur_scored(h, p);
      if (sc < 0 || sc > 7) return { bad: `scored ${sc} out of range`, h };
    }
  }
  return { h, guard };
}

let finished = 0, winners = new Set(), badness = null;
for (let seed = 1; seed <= 40 && !badness; seed++) {
  const r = playGame(seed);
  if (r.bad) { badness = `seed ${seed}: ${r.bad}`; break; }
  if (e.ur_done(r.h) === 1) {
    finished++;
    const w = e.ur_winner(r.h);
    winners.add(w);
    if (e.ur_scored(r.h, w) !== 7) badness = `seed ${seed}: winner ${w} scored ${e.ur_scored(r.h, w)}`;
  }
}
ok('no position in 40 games broke a rule invariant', badness === null, badness ?? 'clean');
ok('all 40 games reached a finish', finished === 40, `${finished} of 40`);
ok('both players win some of them', winners.size === 2, `winners seen: ${[...winners].join(',')}`);

// -- Controls -------------------------------------------------------------
// L-FALSIF: an instrument that cannot fail is not evidence.
ok('control: two new boards are different handles', e.ur_new() !== e.ur_new());
ok('control: a bigger roll moves the piece further',
   e.ur_piece(e.ur_play(e.ur_new(), 0, 1), 1, 0) !== e.ur_piece(e.ur_play(e.ur_new(), 0, 3), 1, 0));
// If the AI ignored the board it would answer one index forever, and from
// the opening EVERY piece is equivalent so 0 is the correct answer there.
// Comparing two hand-built positions therefore proves nothing: the honest
// question is whether the AI is a constant function over positions it
// actually reaches, so collect its answers across real play.
const seen = new Set();
{
  let h = e.ur_new(), s = 99, guard = 0;
  while (e.ur_done(h) === 0 && guard < 4000) {
    guard++;
    const roll = e.ur_roll(s); s = (s * 1103515245 + 12345) & 0x7fffffff;
    if (roll === 0) { h = e.ur_pass(h); continue; }
    const idx = e.ur_ai(h, roll);
    if (idx >= 0) seen.add(idx);
    h = idx < 0 ? e.ur_pass(h) : e.ur_play(h, idx, roll);
  }
}
ok('control: the AI is not a constant function over real positions',
   seen.size > 1, `chose ${[...seen].sort((a, b) => a - b).join(',')}`);

console.log(fail === 0
  ? `\nPASS: the Ur module holds every rule invariant (${pass} arms).`
  : `\nFAIL: ${fail} of ${pass + fail} arms.`);
process.exitCode = fail === 0 ? 0 : 1;
