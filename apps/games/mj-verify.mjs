// Grade the Mahjong Solitaire wasm module.
//
// The layout is a hundred and forty-four tiles, thirty-six types of four.
// Two things follow that no amount of watching would check:
//
//   remaining + removed is always 144, and removed is always twice matched,
//   so a pair that took one tile or three shows immediately;
//
//   a removed pair must have been the SAME TYPE and both tiles must have
//   been free at the moment they were taken. This file re-derives both from
//   the board before each step rather than trusting the pair the engine
//   offers, which is what makes it an oracle rather than an echo.
//
// Usage: node apps/games/mj-verify.mjs [path/to/mahjong.wasm]

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const wasmPath = process.argv[2] ||
  join(here, '..', 'landing', 'web', 'games', 'mahjong.wasm');

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

const tiles = h => [...Array(144)].map((_, i) => e.mj_tile(h, i));
const present = h => tiles(h).filter(v => v > 0).length;

console.log(`mj-verify ${wasmPath}`);

// -- The layout -----------------------------------------------------------
const s0 = e.mj_new(17);
ok('a hundred and forty-four tiles are laid out',
   present(s0) === 144 && e.mj_remaining(s0) === 144, present(s0));
ok('nothing removed or matched yet',
   e.mj_removed(s0) === 0 && e.mj_matched(s0) === 0);
{
  const counts = new Map();
  for (const v of tiles(s0)) {
    const t = e.mj_type(v);
    counts.set(t, (counts.get(t) ?? 0) + 1);
  }
  ok('thirty-six types, four tiles of each',
     counts.size === 36 && [...counts.values()].every(n => n === 4),
     `${counts.size} types`);
  ok('every tile value is distinct',
     new Set(tiles(s0)).size === 144, new Set(tiles(s0)).size);
}

// -- The copy arm ---------------------------------------------------------
{
  const before = JSON.stringify(tiles(s0));
  const next = e.mj_step(s0);
  ok('stepping answers a different state', next !== s0);
  ok('THE COPY ARM: the layout stepped from is untouched',
     JSON.stringify(tiles(s0)) === before, present(s0));
  ok('a step removes exactly two tiles',
     present(next) === present(s0) - 2, `${present(s0)} -> ${present(next)}`);
}

// -- Whole deals ----------------------------------------------------------
function playDeal(seed) {
  let h = e.mj_new(seed);
  let steps = 0;
  while (e.mj_done(h) === 0 && steps < 100) {
    steps++;
    const before = tiles(h);
    const code = e.mj_pair(h);
    if (code < 0) break;
    const i = Math.floor(code / 144), j = code % 144;
    // Re-derive the pair's legality from the board, before the engine acts.
    if (before[i] <= 0 || before[j] <= 0) {
      return { bad: `step ${steps}: pair ${i},${j} includes a tile already gone`, h };
    }
    if (i === j) return { bad: `step ${steps}: pair is one tile with itself`, h };
    if (e.mj_type(before[i]) !== e.mj_type(before[j])) {
      return { bad: `step ${steps}: types ${e.mj_type(before[i])} and ${e.mj_type(before[j])} do not match`, h };
    }
    if (e.mj_free(h, i) !== 1 || e.mj_free(h, j) !== 1) {
      return { bad: `step ${steps}: a tile in the pair was not free`, h };
    }
    h = e.mj_step(h);
    const after = tiles(h);
    if (after[i] > 0 || after[j] > 0) {
      return { bad: `step ${steps}: the matched tiles are still there`, h };
    }
    // Nothing else moved.
    for (let k = 0; k < 144; k++) {
      if (k !== i && k !== j && after[k] !== before[k]) {
        return { bad: `step ${steps}: tile ${k} changed as well`, h };
      }
    }
    if (e.mj_remaining(h) + e.mj_removed(h) !== 144) {
      return { bad: `step ${steps}: ${e.mj_remaining(h)} + ${e.mj_removed(h)} is not 144`, h };
    }
    if (e.mj_removed(h) !== 2 * e.mj_matched(h)) {
      return { bad: `step ${steps}: removed ${e.mj_removed(h)} against ${e.mj_matched(h)} matches`, h };
    }
    if (e.mj_remaining(h) !== present(h)) {
      return { bad: `step ${steps}: counter ${e.mj_remaining(h)} against ${present(h)} on the board`, h };
    }
  }
  return { h, steps };
}

{
  let broke = null, cleared = 0, stuck = 0, totalSteps = 0;
  for (let seed = 1; seed <= 15 && !broke; seed++) {
    const r = playDeal(seed);
    if (r.bad) { broke = `seed ${seed}: ${r.bad}`; break; }
    totalSteps += r.steps;
    if (e.mj_remaining(r.h) === 0) cleared++; else stuck++;
  }
  ok('every pair taken is a matching free pair, and the counts hold',
     broke === null, broke ?? `15 deals, ${totalSteps} pairs`);
  ok('every deal ends, cleared or stuck', cleared + stuck === 15,
     `${cleared} cleared, ${stuck} stuck`);
  ok('control: enough pairs taken to mean something', totalSteps > 100, totalSteps);
}

// -- Stuck really is stuck ------------------------------------------------
{
  const bad = [];
  for (let seed = 1; seed <= 15; seed++) {
    let h = e.mj_new(seed), n = 0;
    while (e.mj_done(h) === 0 && n++ < 100) h = e.mj_step(h);
    if (e.mj_remaining(h) > 0) {
      if (e.mj_stuck(h) !== 1) bad.push(`seed ${seed}: ended with tiles but not stuck`);
      if (e.mj_pair(h) >= 0) bad.push(`seed ${seed}: stuck but a pair is available`);
      if (e.mj_step(h) !== h) bad.push(`seed ${seed}: a stuck board still stepped`);
    }
  }
  ok('a deal that ends with tiles left has no legal pair',
     bad.length === 0, bad.length ? bad.slice(0, 3).join('; ') : '15 deals');
}

// -- Refusals -------------------------------------------------------------
ok('a tile off the layout is refused', e.mj_tile(s0, 144) === -1 && e.mj_tile(s0, -1) === -1);
ok('an empty slot has no type', e.mj_type(0) === -1 && e.mj_type(-3) === -1);
ok('a slot off the layout is not free', e.mj_free(s0, 200) === 0 && e.mj_free(s0, -1) === 0);

// -- Controls -------------------------------------------------------------
ok('control: two new deals are different handles', e.mj_new(1) !== e.mj_new(1));
ok('control: different seeds lay out differently',
   JSON.stringify(tiles(e.mj_new(1))) !== JSON.stringify(tiles(e.mj_new(2))));
ok('control: the same seed lays out the same',
   JSON.stringify(tiles(e.mj_new(5))) === JSON.stringify(tiles(e.mj_new(5))));

console.log(fail === 0
  ? `\nPASS: Mahjong takes only matching free pairs (${pass} arms).`
  : `\nFAIL: ${fail} of ${pass + fail} arms.`);
process.exitCode = fail === 0 ? 0 : 1;
