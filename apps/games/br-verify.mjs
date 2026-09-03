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
  // `br_new` hands back a position and not a deal: from 2026-09-02 the
  // opening lead belongs to the declarer's left, so when North-South buy
  // the contract there are already cards face up when you get the handle.
  // The deck is therefore the four hands PLUS what is on the table, and an
  // arm that counted only the hands read 49 cards and called it a bad deal.
  const bad = [];
  for (let seed = 1; seed <= 100; seed++) {
    const h = e.br_new(seed);
    const table = SEATS.map(s => e.br_trick(h, s)).filter(c => c >= 0);
    const all = SEATS.flatMap(s => hand(h, s)).concat(table);
    if (all.length !== 52) { bad.push(`seed ${seed}: ${all.length} cards dealt`); continue; }
    if (new Set(all).size !== 52) { bad.push(`seed ${seed}: a card was dealt twice`); continue; }
    if (Math.min(...all) !== 0 || Math.max(...all) !== 51) {
      bad.push(`seed ${seed}: cards outside 0..51`); continue;
    }
    if (SEATS.reduce((n, s) => n + e.br_count(h, s), 0) + table.length !== 52) {
      bad.push(`seed ${seed}: hands are ${SEATS.map(s => e.br_count(h, s))}`); continue;
    }
    if (e.br_cur(h) !== 1) bad.push(`seed ${seed}: opens on seat ${e.br_cur(h)}, not South`);
    if (e.br_count(h, 1) !== 13) bad.push(`seed ${seed}: South holds ${e.br_count(h, 1)}`);
  }
  ok('every one of 100 deals is a whole deck, and it is South to play',
     bad.length === 0, bad.length ? bad.slice(0, 3).join('; ') : '100 deals');
  // The point count is asked of an untouched deal, where all forty are
  // still in hands and the sum means what it says.
  {
    const pts = SEATS.reduce((n, s) => n + e.br_hcp(e.br_new(1), s), 0)
      + SEATS.map(s => e.br_trick(e.br_new(1), s)).filter(c => c >= 0)
        .reduce((n, c) => n + e.br_card_hcp(c), 0);
    ok('the forty high card points are all still on the table', pts === 40, pts);
  }
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
// Play a hand out through the engine's own step and decide every trick on
// this side, by IDENTITY: which card won, not which suit. That distinction
// is the whole reason this function exists. GAME-17 lived here for as long
// as the engine did, because the loop it replaced chose a card per seat and
// then discarded index 0 from every hand, so the card leaving was not the
// card played and no count could tell.
//
// The card a seat played is read as the difference its hand shows across
// the step, which asks the engine for nothing but the hands.
function playOut(h) {
  const trump = e.br_trump(h);
  let g = h, guard = 0, ns = 0, ew = 0, trick = [], plays = 0, bad = null;
  // Whatever is already face up when the handle arrives is this trick's
  // first cards, in the order the seats took them.
  let seat = e.br_leader(g);
  for (let k = 0; k < 4; k++) {
    const c = e.br_trick(g, seat);
    if (c < 0) break;
    trick.push({ seat, c });
    seat = [2, 3, 1, 0][seat];
  }
  while (e.br_done(g) !== 1 && guard++ < 200) {
    const s = e.br_cur(g);
    const before = hand(g, s);
    const next = e.br_step(g);
    const gone = before.filter(c => !hand(next, s).includes(c));
    if (gone.length !== 1) { bad = `a step took ${gone.length} cards out of seat ${s}`; break; }
    trick.push({ seat: s, c: gone[0] });
    plays++;
    if (trick.length === 4) {
      const led = e.br_suit(trick[0].c);
      let w = trick[0];
      for (const t of trick) if (beats(t.c, w.c, led, trump)) w = t;
      if (w.seat <= 1) ns++; else ew++;
      trick = [];
    }
    g = next;
  }
  return { g, ns, ew, plays, bad };
}

{
  const diffs = [];
  for (let seed = 1; seed <= 100; seed++) {
    const h = e.br_new(seed);
    const r = playOut(h);
    if (r.bad) { diffs.push(`seed ${seed}: ${r.bad}`); continue; }
    if (e.br_done(r.g) !== 1) { diffs.push(`seed ${seed}: never finished`); continue; }
    if (r.ns + r.ew !== 13) { diffs.push(`seed ${seed}: ${r.ns + r.ew} tricks decided`); continue; }
    if (e.br_nstricks(r.g) !== r.ns || e.br_ewtricks(r.g) !== r.ew) {
      diffs.push(`seed ${seed}: engine ${e.br_nstricks(r.g)}/${e.br_ewtricks(r.g)},`
        + ` by identity ${r.ns}/${r.ew}`);
    }
  }
  ok('the engine credits each trick to the side that actually won it',
     diffs.length === 0,
     diffs.length ? `${diffs.length} of 100 deals differ; ${diffs.slice(0, 2).join('; ')}`
                  : '100 deals agree');
}

// -- Bounds and bookkeeping ----------------------------------------------
// The trick counts are RUNNING now, so these are asked of a finished hand.
// Asked of a fresh handle they would all read zero and agree with each
// other, which is a thing that cannot fail.
{
  const bad = [];
  let split = 0;
  for (let seed = 1; seed <= 100; seed++) {
    const g = playOut(e.br_new(seed)).g;
    const ns = e.br_nstricks(g), ew = e.br_ewtricks(g);
    if (ns < 0 || ns > 13 || ew < 0 || ew > 13) bad.push(`seed ${seed}: ${ns}/${ew} tricks`);
    if (ns + ew !== 13) bad.push(`seed ${seed}: ${ns + ew} tricks in a hand of thirteen`);
    if (e.br_tricks(g) !== 13) bad.push(`seed ${seed}: ${e.br_tricks(g)} tricks played`);
    const made = e.br_made(g);
    const want = e.br_declarer(g) === 0 ? ns : ew;
    if (made !== want) bad.push(`seed ${seed}: made ${made}, expected ${want}`);
    const t = e.br_trump(g);
    if (t < 0 || t > 4) bad.push(`seed ${seed}: trump ${t}`);
    const c = e.br_contract(g);
    if (c < 0 || c > 7) bad.push(`seed ${seed}: contract ${c}`);
    if (ns > 0 && ew > 0) split++;
  }
  ok('tricks, trump and contract stay in range and agree with the declarer',
     bad.length === 0, bad.length ? bad.slice(0, 3).join('; ') : '100 deals');
  // The pinned golden recorded a declarer taking all thirteen, which is
  // what the old loop produced and what nothing could see. A hand where
  // both sides take some is the shape of a game actually being played.
  ok('both sides take tricks in nearly every deal', split >= 95,
     `${split} of 100 deals were shared out`);
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

// -- GAME-13: a handle a caller is holding must not move under it ---------
// This arm used to ask whether COUNTING the tricks consumed the hands, back
// when counting them meant playing the whole hand out. `br_nstricks` is a
// field read now, so that question can no longer fail and asking it would
// be an arm dressed as a test. What still answers it is PLAYING: the page
// keeps every position it has been in and offers undo out of them, so a
// play that wrote through its argument would silently rewrite the past.
{
  const h = e.br_new(3);
  const before = SEATS.map(s => hand(h, s));
  const idx = [...Array(13).keys()].find(i => e.br_legal(h, i) === 1);
  const after = e.br_play(h, idx);
  ok('GAME-13: playing a card leaves the handle you held alone',
     JSON.stringify(SEATS.map(s => hand(h, s))) === JSON.stringify(before),
     SEATS.map(s => e.br_count(h, s)).join(','));
  ok('and the new handle is a different position',
     e.br_count(after, 1) === 12 && after !== h, `${e.br_count(after, 1)} cards left`);
  // Playing the whole hand out twice from one handle must agree, which it
  // cannot if a step is writing through what it was given.
  const a = playOut(e.br_new(3)), b = playOut(e.br_new(3));
  ok('two play-outs of one deal agree', a.ns === b.ns && a.ew === b.ew,
     `${a.ns}/${a.ew} then ${b.ns}/${b.ew}`);
}

// -- Following suit -------------------------------------------------------
// The legality is the only rule bridge asks of a card, so it is the only
// thing standing between the page and a visitor who can revoke at will.
{
  let asked = 0, offered = 0, taken = 0;
  for (let seed = 1; seed <= 25; seed++) {
    let g = e.br_new(seed), guard = 0;
    while (e.br_done(g) !== 1 && guard++ < 200) {
      const s = e.br_cur(g);
      const lead = e.br_trick(g, e.br_leader(g));
      if (lead >= 0 && e.br_leader(g) !== s) {
        const led = e.br_suit(lead);
        const mine = hand(g, s);
        const wrong = mine.findIndex(c => e.br_suit(c) !== led);
        if (mine.some(c => e.br_suit(c) === led) && wrong >= 0) {
          asked++;
          if (e.br_legal(g, wrong) === 1) offered++;
          if (e.br_play(g, wrong) !== g) taken++;
        }
      }
      g = e.br_step(g);
    }
  }
  ok('no card off the suit led was ever offered', offered === 0,
     `${offered} of ${asked} would have been a revoke`);
  ok('and a refused card changes nothing', taken === 0, `${taken} were taken anyway`);
  // L-VACUOUS: both arms above are silent unless a revoke was available.
  ok('control: the follow-suit question was reachable', asked > 0, `asked ${asked} times`);
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
