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
import { gameInstance } from '../landing/web/games/arcade.js';

const here = dirname(fileURLToPath(import.meta.url));
const wasmPath = process.argv[2] ||
  join(here, '..', 'landing', 'web', 'games', 'crazyeights.wasm');


const inst = gameInstance(
  new WebAssembly.Module(readFileSync(wasmPath)));
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

// A game ends when somebody goes out. It cannot block: an empty stock is
// remade from the discards, so when nothing can be drawn every card but the
// top one is in a hand, and some hand holds the suit in force.
function judgeEnd(h) {
  const np = e.ce_players(h), w = e.ce_winner(h);
  const sizes = [...Array(np)].map((_, p) => e.ce_size(h, p));
  if (w >= 0 && sizes[w] === 0) return { how: 'out' };
  return { bad: `ended with hands ${sizes.join(',')} and nobody out, winner ${w}` };
}
{
  let broke = null, winners = new Set(), outs = 0;
  const unfinished = [];
  for (let seed = 1; seed <= 20 && !broke; seed++) {
    const players = 2 + (seed % 3);
    const r = playGame(seed, players);
    if (r.bad) { broke = `seed ${seed} (${players}p): ${r.bad}`; break; }
    if (e.ce_done(r.h) !== 1) { unfinished.push(`seed ${seed} (${players}p)`); continue; }
    winners.add(e.ce_winner(r.h));
    const j = judgeEnd(r.h);
    if (j.bad) broke = `seed ${seed} (${players}p): ${j.bad}`; else outs++;
  }
  ok('the flags and the counters agree on every turn of 20 games, and every game ends with a player out',
     broke === null, broke ?? `${outs} went out`);
  ok('every game ends inside the turn cap', unfinished.length === 0, unfinished.join('; ') || '20 of 20');
  ok('more than one seat wins across the set', winners.size > 1,
     `winners: ${[...winners].sort().join(',')}`);
}// -- Playability agrees with the rules -----------------------------------
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

// -- RULES: every class of turn, from positions built card by card ----------
// pagat's basic game, as CrazyEights.codex states it. A card is suit 13 +
// rank; rank 0 is the two, 6 the eight, 9-11 J Q K, 12 the ace.
{
  const C = (r, s) => s * 13 + r;
  const legal = h => { const out = []; for (let c = 0; c < 52; c++) if (e.ce_canplay(h, c) === 1) out.push(c); return out; };
  const build = (np, top, hands, stock, call) => {
    let h = e.ce_empty(np, top);
    hands.forEach((cs, p) => cs.forEach(c => { h = e.ce_give(h, p, c); }));
    for (const c of stock) h = e.ce_stock(h, c);
    if (call !== undefined) h = e.ce_call(h, call);
    return h;
  };
  for (const [np, each] of [[2, 7], [3, 5], [4, 5]]) {
    const h = e.ce_new(9, np);
    ok(`${np} players are dealt ${each} each, one card starts the pile, the rest is the stock`,
       [...Array(np)].every((_, p) => e.ce_size(h, p) === each) && e.ce_pile(h) === 52 - np * each - 1,
       [...Array(np)].map((_, p) => e.ce_size(h, p)).join(',') + ` stock ${e.ce_pile(h)}`);
  }
  {
    let seed = 1, h = e.ce_new(seed, 3);
    while (e.ce_drank(h) !== 6 && seed < 400) h = e.ce_new(++seed, 3);
    ok('a starting eight calls its own suit', e.ce_drank(h) === 6 && e.ce_declared(h) === e.ce_dsuit(h), `seed ${seed}`);
  }
  {
    const hand = [C(3, 3), C(7, 1), C(6, 0), C(11, 3), C(0, 2)];
    const h = build(2, C(3, 1), [hand, [C(4, 0)]], [C(5, 0)]);
    ok('on a five of hearts: the other five, a heart and an eight go; a club king and a diamond two do not',
       JSON.stringify(legal(h)) === JSON.stringify([C(6, 0), C(3, 3), C(7, 1)].sort((a, b) => a - b)),
       JSON.stringify(legal(h)));
  }
  {
    const h = build(2, C(6, 0), [[C(6, 1), C(10, 2), C(10, 0), C(1, 0)], [C(4, 3)]], [C(5, 3)], 2);
    ok('on an eight that named diamonds: another eight or a diamond, never the eight\'s own suit',
       JSON.stringify(legal(h)) === JSON.stringify([C(10, 2), C(6, 1)].sort((a, b) => a - b)), JSON.stringify(legal(h)));
  }
  {
    const h = build(2, C(3, 1), [[C(7, 1), C(2, 0)], [C(4, 0), C(9, 3)]], [C(5, 0), C(12, 2)]);
    ok('a draw is offered even with a card to play', e.ce_candraw(h) === 1 && legal(h).length === 1);
    const d = e.ce_draw(h);
    ok('a draw takes one card and ends the turn', e.ce_size(d, 0) === 3 && e.ce_has(d, 0, C(5, 0)) === 1 && e.ce_cur(d) === 1 && e.ce_pile(d) === 1);
  }
  {
    // Stock empty; the discards are every card in no hand and not on top.
    const hands = [[C(7, 1), C(2, 0)], [C(4, 0), C(9, 3)]];
    const h = build(2, C(3, 1), hands, []);
    const d = e.ce_draw(h);
    let drawn = -1;
    for (let c = 0; c < 52; c++) if (e.ce_has(d, 0, c) === 1 && !hands[0].includes(c)) drawn = c;
    ok('an empty stock is remade from the discards under the top card, and the draw comes from it',
       e.ce_candraw(h) === 1 && e.ce_size(d, 0) === 3 && e.ce_pile(d) === 52 - 4 - 1 - 1 && drawn >= 0 &&
       drawn !== C(3, 1) && !hands[1].includes(drawn), `drawn ${drawn}, stock ${e.ce_pile(d)}`);
  }
  {
    // Every card but the top one in the two hands: nothing to draw.
    const all = [...Array(52).keys()].filter(c => c !== C(3, 1));
    const h = build(2, C(3, 1), [all.slice(0, 26), all.slice(26)], []);
    ok('with every other card in a hand there is nothing to draw', e.ce_candraw(h) === 0 && e.ce_draw(h) === h);
    ok('and some hand still holds the suit in force, so the game goes on', legal(h).length > 0 && e.ce_done(h) === 0);
  }
  {
    const h = e.ce_play(build(2, C(3, 1), [[C(0, 1), C(9, 0)], [C(4, 0), C(8, 3)]], [C(5, 0)]), C(0, 1), -1);
    ok('a two is an ordinary card: the next player simply plays or draws',
       e.ce_cur(h) === 1 && e.ce_candraw(h) === 1 && e.ce_size(h, 1) === 2, `cur ${e.ce_cur(h)}`);
  }
  {
    const h = e.ce_play(build(2, C(3, 1), [[C(7, 1)], [C(6, 0), C(11, 2), C(12, 3), C(5, 1)]], [C(5, 0)]), C(7, 1), -1);
    ok('playing the last card goes out and wins', e.ce_done(h) === 1 && e.ce_winner(h) === 0);
    ok('the others\' penalty: an eight 50, a picture 10, an ace 1, a seven 7', e.ce_points(h, 1) === 68, e.ce_points(h, 1));
  }
}
console.log(fail === 0
  ? `\nPASS: Crazy Eights keeps its hands honest (${pass} arms).`
  : `\nFAIL: ${fail} of ${pass + fail} arms.`);
process.exitCode = fail === 0 ? 0 : 1;
