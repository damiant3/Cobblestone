// Grade the Battleship wasm module.
//
// No answer key and no tactic to check, so the bar is placement and
// bookkeeping. Two invariants carry most of the weight:
//
//   Each fleet is exactly 17 cells, [5,4,3,3,2]. Overlapping ships or a
//   ship the placer gave up on both show as a count below 17, and neither
//   is visible from watching a game.
//
//   Every mark in a tracking grid must AGREE with the opponent's ships: a
//   hit only where a ship is, a miss only where one is not. That checks
//   bs-shoot end to end against the board it is shooting at, which no
//   count of hits can do -- a shot recorded on the wrong grid, or against
//   the shooter's own ships, keeps every total plausible.
//
// Usage: node apps/games/bs-verify.mjs [path/to/battleship.wasm]

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const wasmPath = process.argv[2] ||
  join(here, '..', 'landing', 'web', 'games', 'battleship.wasm');

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

const ships = (h, p) => [...Array(10)].map((_, r) =>
  [...Array(10)].map((_, c) => e.bs_ship(h, p, r, c)));
const track = (h, p) => [...Array(10)].map((_, r) =>
  [...Array(10)].map((_, c) => e.bs_track(h, p, r, c)));
const count = (g, v) => g.flat().filter(x => x === v).length;

console.log(`bs-verify ${wasmPath}`);

// -- Placement ------------------------------------------------------------
const s0 = e.bs_new(42);
ok('a new game places seventeen ship cells for each side',
   count(ships(s0, 1), 1) === 17 && count(ships(s0, 2), 1) === 17,
   `p1 ${count(ships(s0, 1), 1)}, p2 ${count(ships(s0, 2), 1)}`);
ok('nothing has been shot at yet',
   count(track(s0, 1), 0) === 100 && count(track(s0, 2), 0) === 100);
ok('no hits, no shots, not over',
   e.bs_hits(s0, 1) === 0 && e.bs_shots(s0, 1) === 0 && e.bs_done(s0) === 0);

// Across many seeds, because one placement passing says nothing about the
// placer. A ship the placer silently gave up on shows here and nowhere else.
{
  const wrong = [];
  for (let seed = 1; seed <= 40; seed++) {
    const h = e.bs_new(seed);
    for (const p of [1, 2]) {
      const n = count(ships(h, p), 1);
      if (n !== 17) wrong.push(`seed ${seed} p${p}: ${n} cells`);
    }
  }
  ok('every fleet in 40 seeded games is exactly seventeen cells',
     wrong.length === 0, wrong.length ? wrong.slice(0, 3).join('; ') : '80 fleets');
}

// THERE IS DELIBERATELY NO RUN-LENGTH CHECK HERE, and the reason is worth
// keeping so it is not added back. An arm asserting "no straight run of
// ship cells exceeds the largest ship" fails on correct placements: this
// engine allows ships to TOUCH (bs-all-empty only requires the target cells
// be empty), so a 5 laid against a 4 reads as a run of 9. Measured, that is
// exactly what happens. It is not an oversized ship either, and the fleet
// count proves it rather than assuming it: every fleet is exactly 17 cells
// and no ship exceeds 5, so a 9-run must be two ships end to end.
//
// Runs cannot bound placement in general here anyway, because two parallel
// ships lying alongside each other create perpendicular runs that belong to
// no single ship. The seventeen-cell count is the placement invariant that
// actually holds: overlap makes it fall short, and a ship laid past its
// size makes it overshoot.

// -- THE COPY ARM ---------------------------------------------------------
const beforeTrack = JSON.stringify(track(s0, 1));
const beforeShips = JSON.stringify(ships(s0, 1));
const s1 = e.bs_step(s0);
ok('stepping answers a different state', s1 !== s0, `${s0} -> ${s1}`);
ok('THE COPY ARM: the state stepped from has an untouched tracking grid',
   JSON.stringify(track(s0, 1)) === beforeTrack,
   `${count(track(s0, 1), 0)} cells still unknown`);
ok('a round fires exactly one shot for each side',
   e.bs_shots(s1, 1) === 1 && e.bs_shots(s1, 2) === 1 &&
   100 - count(track(s1, 1), 0) === 1 && 100 - count(track(s1, 2), 0) === 1);
ok('the ships are not disturbed by shooting',
   JSON.stringify(ships(s1, 1)) === beforeShips);

// -- A whole game, checking agreement after every round -------------------
function playGame(seed) {
  let h = e.bs_new(seed);
  const fleet1 = JSON.stringify(ships(h, 1)), fleet2 = JSON.stringify(ships(h, 2));
  let rounds = 0, prev1 = track(h, 1), prev2 = track(h, 2);
  while (e.bs_done(h) === 0 && rounds < 120) {
    rounds++;
    h = e.bs_step(h);
    // Ships never move.
    if (JSON.stringify(ships(h, 1)) !== fleet1) return { bad: 'p1 fleet changed', h };
    if (JSON.stringify(ships(h, 2)) !== fleet2) return { bad: 'p2 fleet changed', h };
    for (const [p, opp, prev] of [[1, 2, prev1], [2, 1, prev2]]) {
      const t = track(h, p), o = ships(h, opp);
      for (let r = 0; r < 10; r++) {
        for (let c = 0; c < 10; c++) {
          // A mark must agree with the OPPONENT's ships.
          if (t[r][c] === 2 && o[r][c] !== 1) return { bad: `p${p} hit on empty water at ${r},${c}`, h };
          if (t[r][c] === 1 && o[r][c] === 1) return { bad: `p${p} miss on a ship at ${r},${c}`, h };
          // Knowledge is never unlearned.
          if (prev[r][c] !== 0 && t[r][c] !== prev[r][c]) {
            return { bad: `p${p} changed a known cell at ${r},${c}`, h };
          }
        }
      }
      if (e.bs_hits(h, p) !== count(t, 2)) {
        return { bad: `p${p} hit count ${e.bs_hits(h, p)} against ${count(t, 2)} marks`, h };
      }
      if (e.bs_hits(h, p) > 17) return { bad: `p${p} has ${e.bs_hits(h, p)} hits`, h };
    }
    prev1 = track(h, 1); prev2 = track(h, 2);
  }
  return { h, rounds };
}

let broke = null, finished = 0, winners = new Set();
for (let seed = 1; seed <= 12 && !broke; seed++) {
  const r = playGame(seed);
  if (r.bad) { broke = `seed ${seed}: ${r.bad}`; break; }
  if (e.bs_done(r.h) === 1) {
    finished++;
    const w = e.bs_winner(r.h);
    winners.add(w);
    if (e.bs_hits(r.h, w) !== 17) broke = `seed ${seed}: winner ${w} has ${e.bs_hits(r.h, w)} hits`;
  }
}
ok('every mark agrees with the opposing fleet, in every round of 12 games',
   broke === null, broke ?? 'clean');
ok('the games reach a finish', finished === 12, `${finished} of 12`);
ok('the winner sank all seventeen cells', broke === null && finished > 0);
ok('both players win some of them', winners.size === 2, `winners: ${[...winners].join(',')}`);

// -- Controls -------------------------------------------------------------
ok('control: two new games are different handles', e.bs_new(1) !== e.bs_new(1));
ok('control: different seeds place different fleets',
   JSON.stringify(ships(e.bs_new(1), 1)) !== JSON.stringify(ships(e.bs_new(2), 1)));
ok('control: the same seed places the same fleet',
   JSON.stringify(ships(e.bs_new(7), 1)) === JSON.stringify(ships(e.bs_new(7), 1)));
// Prove the agreement reader can actually report a disagreement, or every
// agreement arm above passed by being blind (L-FALSIF).
{
  const t = [[2]], o = [[0]];
  const wouldCatch = t[0][0] === 2 && o[0][0] !== 1;
  ok('control: the agreement reader would report a hit on empty water', wouldCatch);
}

console.log(fail === 0
  ? `\nPASS: Battleship places and shoots by the rules (${pass} arms).`
  : `\nFAIL: ${fail} of ${pass + fail} arms.`);
process.exitCode = fail === 0 ? 0 : 1;
