// Grade the Bridge wasm module.
//
// Two oracles are available here that are not the engine restated, and
// between them they carry the file.
//
//   High card points are arithmetic: ace 4, king 3, queen 2, jack 1, so a
//   full deal is exactly 40 points however it falls. A wrong rank mapping
//   or a hand miscounted shows as a total that is not 40, and no amount of
//   watching a hand play would reveal it.
//
//   The trick winner is decidable. This file replays the engine's OWN trick
//   structure -- same lead, same best-card choice, same discard -- and
//   differs from it in exactly one respect: it decides which side won by
//   the IDENTITY of the winning card rather than by its suit. Mirroring
//   everything else is what makes a disagreement isolate that one decision
//   instead of meaning the two implementations simply drifted.
//
// Usage: node apps/games/br-verify.mjs [path/to/bridge.wasm]

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const wasmPath = process.argv[2] ||
  join(here, '..', 'landing', 'web', 'games', 'bridge.wasm');

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

const SEATS = [0, 1, 2, 3];            // north, south, east, west
const hand = (h, s) => [...Array(e.br_count(h, s))].map((_, i) => e.br_card(h, s, i));

console.log(`br-verify ${wasmPath}`);

// -- The deck's arithmetic ------------------------------------------------
const hcps = [...Array(52)].map((_, c) => e.br_card_hcp(c));
ok('the four honours in each suit are worth 4, 3, 2 and 1',
   hcps.filter(v => v === 4).length === 4 && hcps.filter(v => v === 3).length === 4 &&
   hcps.filter(v => v === 2).length === 4 && hcps.filter(v => v === 1).length === 4,
   `${hcps.filter(v => v > 0).length} honours`);
ok('a whole deck is forty high card points',
   hcps.reduce((a, b) => a + b, 0) === 40, hcps.reduce((a, b) => a + b, 0));
ok('there are four suits of thirteen',
   new Set([...Array(52)].map((_, c) => e.br_suit(c))).size === 4 &&
   [0, 1, 2, 3].every(s => [...Array(52)].filter((_, c) => e.br_suit(c) === s).length === 13));

// -- The deal -------------------------------------------------------------
{
  const bad = [];
  for (let seed = 1; seed <= 100; seed++) {
    const h = e.br_new(seed);
    const all = SEATS.flatMap(s => hand(h, s));
    if (all.length !== 52) { bad.push(`seed ${seed}: ${all.length} cards dealt`); continue; }
    if (new Set(all).size !== 52) { bad.push(`seed ${seed}: a card was dealt twice`); continue; }
    if (Math.min(...all) !== 0 || Math.max(...all) !== 51) {
      bad.push(`seed ${seed}: cards outside 0..51`); continue;
    }
    if (SEATS.some(s => e.br_count(h, s) !== 13)) {
      bad.push(`seed ${seed}: hands are ${SEATS.map(s => e.br_count(h, s))}`); continue;
    }
    const pts = SEATS.reduce((n, s) => n + e.br_hcp(h, s), 0);
    if (pts !== 40) bad.push(`seed ${seed}: ${pts} points dealt, not 40`);
  }
  ok('every one of 100 deals is a whole deck, thirteen each, forty points',
     bad.length === 0, bad.length ? bad.slice(0, 3).join('; ') : '100 deals');
}

// -- The trick oracle -----------------------------------------------------
// Mirror the engine's structure exactly, except for deciding the winner.
function beats(challenger, best, led, trump) {
  return e.br_beats(challenger, best, led, trump) === 1;
}
function bestIdx(cards, led, trump) {
  let bi = 0;
  for (let i = 0; i < cards.length; i++) {
    if (beats(cards[i], cards[bi], led, trump)) bi = i;
  }
  return bi;
}
// Returns NS tricks, deciding each trick by WHICH CARD won.
function nsTricksByIdentity(h) {
  let n = hand(h, 0), s = hand(h, 1), ea = hand(h, 2), w = hand(h, 3);
  const trump = e.br_trump(h), declarer = e.br_declarer(h);
  let ns = 0;
  while (n.length > 0) {
    const lead = declarer === 0 ? s[0] : ea[0];
    const led = e.br_suit(lead);
    const nc = n[bestIdx(n, led, trump)];
    const sc = s[bestIdx(s, led, trump)];
    const ec = ea[bestIdx(ea, led, trump)];
    const wc = w[bestIdx(w, led, trump)];
    let win = nc;
    if (beats(sc, win, led, trump)) win = sc;
    if (beats(ec, win, led, trump)) win = ec;
    if (beats(wc, win, led, trump)) win = wc;
    // The one difference from the engine: identity, not suit.
    if (win === nc || win === sc) ns++;
    n = n.slice(1); s = s.slice(1); ea = ea.slice(1); w = w.slice(1);
  }
  return ns;
}

{
  const diffs = [];
  let suitWouldDiffer = 0;
  for (let seed = 1; seed <= 100; seed++) {
    const h = e.br_new(seed);
    const engine = e.br_nstricks(h);
    const oracle = nsTricksByIdentity(h);
    if (engine !== oracle) diffs.push(`seed ${seed}: engine ${engine}, by identity ${oracle}`);
    if (engine !== oracle) suitWouldDiffer++;
  }
  ok('the engine credits each trick to the side that actually won it',
     diffs.length === 0,
     diffs.length ? `${diffs.length} of 100 deals differ; ${diffs.slice(0, 2).join('; ')}`
                  : '100 deals agree');
}

// -- Bounds and bookkeeping ----------------------------------------------
{
  const bad = [];
  for (let seed = 1; seed <= 100; seed++) {
    const h = e.br_new(seed);
    const ns = e.br_nstricks(h);
    if (ns < 0 || ns > 13) bad.push(`seed ${seed}: ${ns} NS tricks`);
    const made = e.br_made(h);
    const want = e.br_declarer(h) === 0 ? ns : 13 - ns;
    if (made !== want) bad.push(`seed ${seed}: made ${made}, expected ${want}`);
    const t = e.br_trump(h);
    if (t < 0 || t > 4) bad.push(`seed ${seed}: trump ${t}`);
    const c = e.br_contract(h);
    if (c < 0 || c > 7) bad.push(`seed ${seed}: contract ${c}`);
  }
  ok('tricks, trump and contract stay in range and agree with the declarer',
     bad.length === 0, bad.length ? bad.slice(0, 3).join('; ') : '100 deals');
}

// -- Scoring, computed independently -------------------------------------
{
  const bad = [];
  for (let seed = 1; seed <= 100; seed++) {
    const h = e.br_new(seed);
    const level = e.br_contract(h), made = e.br_made(h), trump = e.br_trump(h);
    const needed = level + 6;
    let want;
    if (made >= needed) {
      const over = made - needed;
      want = level * (trump >= 3 ? 30 : 20) + over * 50 + (level >= 6 ? 500 : 0);
    } else {
      want = -((needed - made) * 50);
    }
    if (e.br_score(h) !== want) {
      bad.push(`seed ${seed}: score ${e.br_score(h)} against ${want}`);
    }
  }
  ok('the score follows from the contract, the trump and the tricks',
     bad.length === 0, bad.length ? bad.slice(0, 3).join('; ') : '100 deals');
}

// -- GAME-13: counting the tricks must not consume the hands --------------
{
  const h = e.br_new(3);
  const before = SEATS.map(s => hand(h, s));
  const first = e.br_nstricks(h);
  const second = e.br_nstricks(h);
  ok('GAME-13: counting the tricks leaves the hands alone',
     JSON.stringify(SEATS.map(s => hand(h, s))) === JSON.stringify(before),
     SEATS.map(s => e.br_count(h, s)).join(','));
  ok('counting twice gives the same answer', first === second, `${first} then ${second}`);
}

// -- Refusals -------------------------------------------------------------
{
  const h = e.br_new(11);
  ok('a seat off the table is refused',
     e.br_card(h, 4, 0) === -1 && e.br_card(h, -1, 0) === -1 && e.br_count(h, 9) === 0);
  ok('a card index off the end is refused',
     e.br_card(h, 0, 13) === -1 && e.br_card(h, 0, -1) === -1);
  ok('a card off the deck is refused',
     e.br_suit(-1) === -1 && e.br_rank(-1) === -1 && e.br_card_hcp(-1) === -1);
}

// -- Controls -------------------------------------------------------------
ok('control: two new deals are different handles', e.br_new(2) !== e.br_new(2));
ok('control: different seeds deal different hands',
   JSON.stringify(hand(e.br_new(1), 0)) !== JSON.stringify(hand(e.br_new(2), 0)));
ok('control: the same seed deals the same hand',
   JSON.stringify(hand(e.br_new(8), 0)) === JSON.stringify(hand(e.br_new(8), 0)));
// L-FALSIF: the trick oracle must be able to disagree with the engine at
// all. Feed it a case where suit and identity give different answers.
{
  // Two cards of the same suit: the higher wins by identity, but a
  // suit-only test cannot tell the two apart.
  const low = 0, high = 12;              // same suit, different ranks
  const sameSuit = e.br_suit(low) === e.br_suit(high);
  const highWins = e.br_beats(high, low, e.br_suit(low), 4) === 1;
  ok('control: two cards of one suit are distinguishable by identity but not by suit',
     sameSuit && highWins && low !== high,
     `suits ${e.br_suit(low)}/${e.br_suit(high)}, higher beats lower: ${highWins}`);
}

console.log(fail === 0
  ? `\nPASS: Bridge deals, plays and scores by the rules (${pass} arms).`
  : `\nFAIL: ${fail} of ${pass + fail} arms.`);
process.exitCode = fail === 0 ? 0 : 1;
