// Grade the Liar's Dice wasm module.
//
// The dice are the invariant. Every player starts with five, a lost
// challenge costs exactly one, and a player with none is out. So across a
// whole game the total dice count falls by exactly one per elimination step
// and never rises, the alive flags and the alive counter agree, and nobody
// is alive with zero dice or out with some left. A game that quietly gave a
// die back, or dropped two at once, still plays and still ends.
//
// The face count is the other checkable thing: `ld_cface` is asked to count
// a face across the table, and this file counts the same face itself out of
// the pools. A miscount there decides challenges wrongly and is invisible.
//
// Usage: node apps/games/ld-verify.mjs [path/to/liarsdice.wasm]

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const wasmPath = process.argv[2] ||
  join(here, '..', 'landing', 'web', 'games', 'liarsdice.wasm');

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

const np = h => e.ld_players(h);
const dice = h => [...Array(np(h))].map((_, p) => e.ld_dice(h, p));
const alive = h => [...Array(np(h))].map((_, p) => e.ld_alive(h, p));
// Count a face across every ALIVE player's live dice, independently.
const countFace = (h, face) => {
  let n = 0;
  for (let p = 0; p < np(h); p++) {
    if (e.ld_alive(h, p) !== 1) continue;
    for (let k = 0; k < e.ld_dice(h, p); k++) if (e.ld_die(h, p, k) === face) n++;
  }
  return n;
};

console.log(`ld-verify ${wasmPath}`);

// -- The opening ----------------------------------------------------------
const s0 = e.ld_new(31, 4);
ok('four players seated', np(s0) === 4, np(s0));
ok('everyone starts with five dice', dice(s0).every(d => d === 5), dice(s0).join(','));
ok('everyone is alive', alive(s0).every(a => a === 1) && e.ld_alivenum(s0) === 4);
ok('twenty dice on the table', e.ld_total(s0) === 20, e.ld_total(s0));
ok('the game is not over', e.ld_done(s0) === 0 && e.ld_winner(s0) === -1);
{
  const faces = [];
  for (let p = 0; p < 4; p++) for (let k = 0; k < 5; k++) faces.push(e.ld_die(s0, p, k));
  ok('every die shows one to six', faces.every(f => f >= 1 && f <= 6),
     `min ${Math.min(...faces)} max ${Math.max(...faces)}`);
}

// -- The copy arm ---------------------------------------------------------
{
  const before = JSON.stringify([dice(s0), alive(s0)]);
  const next = e.ld_step(s0);
  ok('stepping answers a different state', next !== s0);
  ok('THE COPY ARM: the state stepped from is untouched',
     JSON.stringify([dice(s0), alive(s0)]) === before);
}

// -- Whole games ----------------------------------------------------------
function playGame(seed, players) {
  let h = e.ld_new(seed, players);
  let prevTotal = e.ld_total(h), turns = 0;
  while (e.ld_done(h) === 0 && turns < 3000) {
    turns++;
    h = e.ld_step(h);
    const n = np(h);
    // Alive flags and the counter must agree.
    const liveFlags = alive(h).filter(a => a === 1).length;
    if (liveFlags !== e.ld_alivenum(h)) {
      return { bad: `turn ${turns}: ${liveFlags} flags against counter ${e.ld_alivenum(h)}`, h };
    }
    for (let p = 0; p < n; p++) {
      const d = e.ld_dice(h, p), a = e.ld_alive(h, p);
      if (d < 0 || d > 5) return { bad: `turn ${turns}: player ${p} has ${d} dice`, h };
      if (a === 1 && d === 0) return { bad: `turn ${turns}: player ${p} alive with no dice`, h };
      if (a === 0 && d > 0) return { bad: `turn ${turns}: player ${p} out with ${d} dice`, h };
      for (let k = 0; k < d; k++) {
        const f = e.ld_die(h, p, k);
        if (f < 1 || f > 6) return { bad: `turn ${turns}: player ${p} die ${k} shows ${f}`, h };
      }
    }
    // Dice never come back.
    const total = e.ld_total(h);
    if (total > prevTotal) return { bad: `turn ${turns}: dice rose ${prevTotal} to ${total}`, h };
    if (prevTotal - total > 1) {
      return { bad: `turn ${turns}: ${prevTotal - total} dice lost at once`, h };
    }
    prevTotal = total;
    // The engine's face count must agree with counting the pools.
    for (let f = 1; f <= 6; f++) {
      if (e.ld_cface(h, f) !== countFace(h, f)) {
        return { bad: `turn ${turns}: engine counts ${e.ld_cface(h, f)} of face ${f}, pools show ${countFace(h, f)}`, h };
      }
    }
    // A bid, when there is one, must be a legal quantity and face. ZERO is
    // the no-bid sentinel here, set by `ld-init` and left between rounds,
    // not a bid of nothing: qty 0 and face 0 are its correct reading.
    if (e.ld_bid(h) > 0) {
      const q = e.ld_qty(h), f = e.ld_face(h);
      if (f < 1 || f > 6) return { bad: `turn ${turns}: bid face ${f}`, h };
      if (q < 1 || q > 20) return { bad: `turn ${turns}: bid quantity ${q}`, h };
    }
  }
  return { h, turns };
}

{
  let broke = null, finished = 0, winners = new Set();
  for (let seed = 1; seed <= 20 && !broke; seed++) {
    const players = 2 + (seed % 3);
    const r = playGame(seed, players);
    if (r.bad) { broke = `seed ${seed} (${players}p): ${r.bad}`; break; }
    if (e.ld_done(r.h) === 1) {
      finished++;
      const w = e.ld_winner(r.h);
      winners.add(w);
      if (e.ld_alive(r.h, w) !== 1) broke = `seed ${seed}: winner ${w} is not alive`;
      if (e.ld_alivenum(r.h) !== 1) broke = `seed ${seed}: ${e.ld_alivenum(r.h)} left standing`;
    }
  }
  ok('dice only ever leave the table, one at a time, in 20 games',
     broke === null, broke ?? '20 games');
  ok('every game leaves exactly one player standing', finished === 20, `${finished} of 20`);
  ok('more than one seat wins across the set', winners.size > 1,
     `winners: ${[...winners].sort().join(',')}`);
}

// -- Refusals -------------------------------------------------------------
ok('a seat off the table is refused',
   e.ld_dice(s0, 9) === -1 && e.ld_alive(s0, -1) === -1 && e.ld_die(s0, 9, 0) === -1);
ok('a die off the cup is refused', e.ld_die(s0, 0, 5) === -1 && e.ld_die(s0, 0, -1) === -1);
ok('a face off the die counts nothing', e.ld_cface(s0, 0) === 0 && e.ld_cface(s0, 7) === 0);
ok('a finished game refuses another turn', (() => {
  let h = e.ld_new(2, 2), n = 0;
  while (e.ld_done(h) === 0 && n++ < 3000) h = e.ld_step(h);
  return e.ld_done(h) === 1 && e.ld_step(h) === h;
})());

// -- Controls -------------------------------------------------------------
ok('control: two new games are different handles', e.ld_new(1, 2) !== e.ld_new(1, 2));
ok('control: different seeds roll differently',
   JSON.stringify([...Array(5)].map((_, k) => e.ld_die(e.ld_new(1, 2), 0, k))) !==
   JSON.stringify([...Array(5)].map((_, k) => e.ld_die(e.ld_new(2, 2), 0, k))));
// L-FALSIF: the independent face counter must be able to disagree.
ok('control: the face counter counts something',
   [1, 2, 3, 4, 5, 6].reduce((a, f) => a + countFace(s0, f), 0) === 20,
   [1, 2, 3, 4, 5, 6].map(f => countFace(s0, f)).join('+'));

console.log(fail === 0
  ? `\nPASS: Liar's Dice loses one die at a time and counts them right (${pass} arms).`
  : `\nFAIL: ${fail} of ${pass + fail} arms.`);
process.exitCode = fail === 0 ? 0 : 1;
