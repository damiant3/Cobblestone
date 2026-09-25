// Grade the Crazy Eights wasm module.
//
// A hand here is a bitmap of fifty-two flags per player, with the player's
// card COUNT kept beside it as a separate number rather than derived from
// the flags. Those two can disagree, and that is the arm this file leads
// with: a hand whose flags say five cards while its counter says four
// deals, plays and wins exactly like a correct one.
//
// The second arm is that no card is in two hands at once. `ce-hand-set`
// writes a flag and adjusts a counter, so a set on the wrong slot both
// duplicates a card and keeps every total plausible.
//
// Usage: node apps/games/ce-verify.mjs [path/to/crazyeights.wasm]

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const wasmPath = process.argv[2] ||
  join(here, '..', 'landing', 'web', 'games', 'crazyeights.wasm');

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

const EIGHT = 6;   // ce-can-play treats rank 6 as the wild card
const holds = (h, p) => [...Array(52)].map((_, c) => e.ce_has(h, p, c));
const held = (h, p) => holds(h, p).reduce((a, b) => a + b, 0);
const snapshot = h => {
  const np = e.ce_players(h);
  return JSON.stringify([...Array(np)].map((_, p) => holds(h, p)));
};

console.log(`ce-verify ${wasmPath}`);

// -- The deck's arithmetic ------------------------------------------------
{
  const suits = [...Array(52)].map((_, c) => e.ce_suit(c));
  const ranks = [...Array(52)].map((_, c) => e.ce_rank(c));
  ok('four suits of thirteen',
     new Set(suits).size === 4 && [0, 1, 2, 3].every(s => suits.filter(x => x === s).length === 13));
  ok('thirteen ranks of four',
     new Set(ranks).size === 13 && [...new Set(ranks)].every(r => ranks.filter(x => x === r).length === 4));
  ok('a card off the deck is refused', e.ce_rank(-1) === -1 && e.ce_suit(52) === -1);
}

// -- The deal -------------------------------------------------------------
const s0 = e.ce_new(11, 4);
ok('four players were seated', e.ce_players(s0) === 4, e.ce_players(s0));
ok('nobody has won yet', e.ce_done(s0) === 0 && e.ce_cur(s0) >= 0);
{
  const sizes = [...Array(4)].map((_, p) => e.ce_size(s0, p));
  ok('every player was dealt the same number of cards',
     new Set(sizes).size === 1 && sizes[0] > 0, sizes.join(','));
  ok('the counter matches the flags for every player',
     [...Array(4)].every(p => e.ce_size(s0, p) === held(s0, p)),
     [...Array(4)].map(p => `${e.ce_size(s0, p)}/${held(s0, p)}`).join(' '));
}

// -- THE COPY ARM ---------------------------------------------------------
{
  const before = snapshot(s0);
  const next = e.ce_step(s0);
  ok('stepping answers a different state', next !== s0, `${s0} -> ${next}`);
  ok('THE COPY ARM: the state stepped from is untouched', snapshot(s0) === before);
  ok('the turn advanced or the hand changed',
     e.ce_cur(next) !== e.ce_cur(s0) || snapshot(next) !== before);
}

// -- Whole games, checking the bitmap against the counters every turn -----
function playGame(seed, players) {
  let h = e.ce_new(seed, players);
  const np = e.ce_players(h);
  let turns = 0;
  while (e.ce_done(h) === 0 && turns < 800) {
    turns++;
    h = e.ce_step(h);
    for (let p = 0; p < np; p++) {
      const flags = held(h, p), counter = e.ce_size(h, p);
      if (flags !== counter) {
        return { bad: `turn ${turns} player ${p}: ${flags} flags against counter ${counter}`, h };
      }
      if (counter < 0) return { bad: `turn ${turns} player ${p}: counter ${counter}`, h };
    }
    // No card may be held by two players at once.
    for (let c = 0; c < 52; c++) {
      let owners = 0;
      for (let p = 0; p < np; p++) if (e.ce_has(h, p, c) === 1) owners++;
      if (owners > 1) return { bad: `turn ${turns}: card ${c} is in ${owners} hands`, h };
    }
    // Cards in hands plus the draw pile can never exceed the deck.
    let inHands = 0;
    for (let p = 0; p < np; p++) inHands += e.ce_size(h, p);
    if (inHands + e.ce_pile(h) > 52) {
      return { bad: `turn ${turns}: ${inHands} held plus ${e.ce_pile(h)} in the pile`, h };
    }
    if (e.ce_pile(h) < 0) return { bad: `turn ${turns}: pile ${e.ce_pile(h)}`, h };
    // The discard must be a real card, and the declared suit a real suit.
    const dr = e.ce_drank(h), ds = e.ce_declared(h);
    if (dr < 0 || dr > 12) return { bad: `turn ${turns}: discard rank ${dr}`, h };
    if (ds < 0 || ds > 3) return { bad: `turn ${turns}: declared suit ${ds}`, h };
  }
  return { h, turns };
}

// A game ends one of two ways. Somebody goes out, and holds nothing; or the
// position is DEAD, with the pile empty and no seat holding a playable card,
// and the smallest hand wins it (a tie for smallest is a draw, winner -1).
function anyPlayable(h) {
  let n = 0;
  for (let p = 0; p < e.ce_players(h); p++)
    for (let c = 0; c < 52; c++) n += e.ce_can(h, p, c);
  return n;
}
function judgeEnd(h) {
  const np = e.ce_players(h), w = e.ce_winner(h);
  const sizes = [...Array(np)].map((_, p) => e.ce_size(h, p));
  if (w >= 0 && sizes[w] === 0) return { how: 'out' };
  if (e.ce_pile(h) !== 0) return { bad: `ended with ${e.ce_pile(h)} in the pile and nobody out` };
  if (anyPlayable(h) > 0) return { bad: `ended with ${anyPlayable(h)} playable cards and nobody out` };
  const least = Math.min(...sizes);
  const holders = sizes.filter(n => n === least).length;
  const want = holders > 1 ? -1 : sizes.indexOf(least);
  if (w !== want) return { bad: `blocked with hands ${sizes.join(',')}: winner ${w}, want ${want}` };
  return { how: 'blocked' };
}
{
  let broke = null, winners = new Set(), outs = 0, blocked = 0;
  const unfinished = [];
  for (let seed = 1; seed <= 20 && !broke; seed++) {
    const players = 2 + (seed % 3);
    const r = playGame(seed, players);
    if (r.bad) { broke = `seed ${seed} (${players}p): ${r.bad}`; break; }
    if (e.ce_done(r.h) !== 1) { unfinished.push(`seed ${seed} (${players}p)`); continue; }
    winners.add(e.ce_winner(r.h));
    const j = judgeEnd(r.h);
    if (j.bad) broke = `seed ${seed} (${players}p): ${j.bad}`;
    else if (j.how === 'out') outs++; else blocked++;
  }
  ok('the flags and the counters agree on every turn of 20 games, and every ending is legal',
     broke === null, broke ?? `${outs} went out, ${blocked} blocked`);
  ok('every game ends, going out or blocked, inside the turn cap',
     unfinished.length === 0, unfinished.join('; ') || '20 of 20');
  ok('more than one seat wins across the set', winners.size > 1,
     `winners: ${[...winners].sort().join(',')}`);
  // CONTROL: the blocked ending must be REACHED, or the arm above says nothing
  // about it. Seed 12 at two players is the position measured dead from turn
  // 200 to turn 800 before the engine could end it.
  const d = playGame(12, 2);
  const jd = e.ce_done(d.h) === 1 ? judgeEnd(d.h) : { bad: 'did not end' };
  ok('CONTROL: seed 12 at two players ends BLOCKED, and the smallest hand wins',
     jd.how === 'blocked', jd.bad ?? `${d.turns} turns, hands ${e.ce_size(d.h, 0)},${e.ce_size(d.h, 1)}, winner ${e.ce_winner(d.h)}`);
}
// -- Playability agrees with the rules -----------------------------------
// ce-can-play says a card is playable when it is an eight, matches the
// discard rank, or matches the declared suit. Re-derive that here.
{
  const bad = [];
  for (let seed = 1; seed <= 30; seed++) {
    let h = e.ce_new(seed, 3);
    for (let t = 0; t < 12 && e.ce_done(h) === 0; t++) {
      const np = e.ce_players(h), dr = e.ce_drank(h), ds = e.ce_declared(h);
      for (let p = 0; p < np; p++) {
        for (let c = 0; c < 52; c++) {
          const engine = e.ce_can(h, p, c) === 1;
          const mine = e.ce_has(h, p, c) === 1 &&
                       (e.ce_rank(c) === EIGHT || e.ce_rank(c) === dr || e.ce_suit(c) === ds);
          if (engine !== mine) {
            bad.push(`seed ${seed} t${t} p${p} card ${c}: engine ${engine}, rules ${mine}`);
          }
        }
      }
      h = e.ce_step(h);
    }
  }
  ok('playability agrees with rank, suit and the wild card',
     bad.length === 0, bad.length ? bad.slice(0, 2).join('; ') : '30 games');
}

// -- Refusals -------------------------------------------------------------
ok('a seat off the table is refused',
   e.ce_has(s0, 9, 0) === 0 && e.ce_size(s0, 9) === -1 && e.ce_can(s0, -1, 0) === 0);
ok('a card off the deck is refused',
   e.ce_has(s0, 0, 52) === 0 && e.ce_can(s0, 0, -1) === 0);
ok('a finished game refuses another turn', (() => {
  let h = e.ce_new(4, 2), n = 0;
  while (e.ce_done(h) === 0 && n++ < 800) h = e.ce_step(h);
  return e.ce_done(h) === 1 && e.ce_step(h) === h;
})());

// -- Controls -------------------------------------------------------------
ok('control: two new games are different handles', e.ce_new(1, 2) !== e.ce_new(1, 2));
ok('control: different seeds deal differently', snapshot(e.ce_new(1, 2)) !== snapshot(e.ce_new(2, 2)));
ok('control: the same seed deals the same', snapshot(e.ce_new(6, 3)) === snapshot(e.ce_new(6, 3)));
// L-FALSIF: the flags reader must be able to disagree with a counter.
ok('control: the flags reader counts something',
   held(s0, 0) > 0 && held(s0, 0) < 52, held(s0, 0));

console.log(fail === 0
  ? `\nPASS: Crazy Eights keeps its hands honest (${pass} arms).`
  : `\nFAIL: ${fail} of ${pass + fail} arms.`);
process.exitCode = fail === 0 ? 0 : 1;
