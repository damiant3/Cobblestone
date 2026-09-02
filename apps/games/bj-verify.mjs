// Grade the Blackjack wasm module.
//
// Blackjack is the first game in this set with a genuine ORACLE available
// that is not the engine restated: hand value is arithmetic, so this file
// computes it independently from the raw cards and requires the engine to
// agree. Ace softening is where hand scoring goes wrong, and an
// independent sum catches a wrong one where no invariant would -- a hand
// scored 12 instead of 22 is still a plausible number.
//
// The second arm worth its place is the deck. A shuffle that drops or
// duplicates a card is the classic Fisher-Yates defect, and it is invisible
// from watching a hand: the cards look like cards. Every card dealt out of
// one deck must be distinct.
//
// Usage: node apps/games/bj-verify.mjs [path/to/blackjack.wasm]

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const wasmPath = process.argv[2] ||
  join(here, '..', 'landing', 'web', 'games', 'blackjack.wasm');

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

const phand = h => [...Array(e.bj_pcount(h))].map((_, i) => e.bj_pcard(h, i));
const dhand = h => [...Array(e.bj_dcount(h))].map((_, i) => e.bj_dcard(h, i));

console.log(`bj-verify ${wasmPath}`);

// -- The deck's own arithmetic -------------------------------------------
// Ask the engine what each card is worth, then check the shape of a deck:
// four of each rank, values 2..11, and exactly four cards worth 11.
const values = [...Array(52)].map((_, c) => e.bj_card_value(c));
const ranks = [...Array(52)].map((_, c) => e.bj_card_rank(c));
ok('every card has a value between 2 and 11',
   values.every(v => v >= 2 && v <= 11), `min ${Math.min(...values)} max ${Math.max(...values)}`);
ok('there are thirteen ranks, four cards each',
   new Set(ranks).size === 13 &&
   [...new Set(ranks)].every(r => ranks.filter(x => x === r).length === 4),
   `${new Set(ranks).size} ranks`);
const ACE = values.findIndex(v => v === 11);
ok('four cards are worth eleven, which is the ace',
   values.filter(v => v === 11).length === 4, `ace rank ${ranks[ACE]}`);
ok('sixteen cards are worth ten, the tens and the three face ranks',
   values.filter(v => v === 10).length === 16, values.filter(v => v === 10).length);
ok('a card off the end is refused',
   e.bj_card_value(-1) === -1 && e.bj_card_rank(-1) === -1);

// The independent oracle. Aces count 11 until that busts, then 1.
const isAce = card => e.bj_card_value(card) === 11;
function best(cards) {
  let total = cards.reduce((n, c) => n + e.bj_card_value(c), 0);
  let aces = cards.filter(isAce).length;
  while (total > 21 && aces > 0) { total -= 10; aces--; }
  return total;
}

// -- The opening hand -----------------------------------------------------
const s0 = e.bj_new(7);
ok('a new hand deals two cards to each side',
   e.bj_pcount(s0) === 2 && e.bj_dcount(s0) === 2,
   `${e.bj_pcount(s0)} and ${e.bj_dcount(s0)}`);
ok('four cards have come off the deck', e.bj_deckpos(s0) === 4, e.bj_deckpos(s0));
ok('the opening four cards are distinct',
   new Set([...phand(s0), ...dhand(s0)]).size === 4,
   JSON.stringify([phand(s0), dhand(s0)]));

// -- THE COPY ARM ---------------------------------------------------------
const beforeHand = JSON.stringify(phand(s0));
const beforeCount = e.bj_pcount(s0);
const hit = e.bj_hit(s0);
ok('hitting answers a different state', hit !== s0, `${s0} -> ${hit}`);
ok('the new hand has one more card', e.bj_pcount(hit) === beforeCount + 1, e.bj_pcount(hit));
ok('THE COPY ARM: the hand hit from is untouched',
   e.bj_pcount(s0) === beforeCount && JSON.stringify(phand(s0)) === beforeHand,
   `${e.bj_pcount(s0)} cards, ${JSON.stringify(phand(s0))}`);
ok('the card drawn is the next one off the deck',
   e.bj_deckpos(hit) === e.bj_deckpos(s0) + 1);

// -- The value oracle, over many hands ------------------------------------
{
  const wrong = [];
  let softSeen = 0, bustSeen = 0, checked = 0;
  for (let seed = 1; seed <= 300; seed++) {
    let h = e.bj_new(seed);
    for (let step = 0; step < 6; step++) {
      const p = phand(h), d = dhand(h);
      checked++;
      if (e.bj_pvalue(h) !== best(p)) {
        wrong.push(`seed ${seed} step ${step}: engine ${e.bj_pvalue(h)} against ${best(p)} for ${p}`);
      }
      if (e.bj_dvalue(h) !== best(d)) {
        wrong.push(`seed ${seed} step ${step}: dealer ${e.bj_dvalue(h)} against ${best(d)} for ${d}`);
      }
      if (e.bj_psoft(h) === 1) softSeen++;
      if (e.bj_bust(h) === 1) { bustSeen++; break; }
      h = e.bj_hit(h);
    }
  }
  ok('the engine agrees with an independent hand-value sum',
     wrong.length === 0,
     `${checked} hands checked` + (wrong.length ? `, first: ${wrong[0]}` : ''));
  // Controls: the oracle must have met the cases it exists for.
  ok('control: soft hands occurred, so ace handling was exercised',
     softSeen > 0, `${softSeen} soft hands`);
  ok('control: busts occurred, so the downgrade path was exercised',
     bustSeen > 0, `${bustSeen} busts`);
}

// -- The deck is a permutation, as far as one hand can see ----------------
{
  const dup = [];
  for (let seed = 1; seed <= 300; seed++) {
    let h = e.bj_new(seed);
    for (let step = 0; step < 8 && e.bj_bust(h) === 0; step++) h = e.bj_hit(h);
    h = e.bj_stand(h);
    const all = [...phand(h), ...dhand(h)];
    if (new Set(all).size !== all.length) dup.push(`seed ${seed}: ${all}`);
  }
  ok('no card is dealt twice out of one deck in 300 hands',
     dup.length === 0, dup.length ? dup[0] : '300 hands');
}

// -- The dealer's rule ----------------------------------------------------
{
  const bad = [];
  for (let seed = 1; seed <= 200; seed++) {
    const stood = e.bj_stand(e.bj_new(seed));
    const dv = e.bj_dvalue(stood);
    // The dealer draws to 16 and stands on all 17. Below 17 with cards left
    // means the dealer stopped early.
    if (dv < 17) bad.push(`seed ${seed}: dealer stood on ${dv}`);
  }
  ok('the dealer never stands below seventeen', bad.length === 0,
     bad.length ? bad.slice(0, 3).join('; ') : '200 hands');
}

// -- The result follows from the two hands --------------------------------
{
  const bad = [];
  for (let seed = 1; seed <= 200; seed++) {
    const h = e.bj_auto(e.bj_new(seed));
    const p = best(phand(h)), d = best(dhand(h));
    const r = e.bj_result(h);
    const pbj = e.bj_pcount(h) === 2 && p === 21;
    const dbj = e.bj_dcount(h) === 2 && d === 21;
    let want;
    if (p > 21) want = -1;
    else if (pbj && dbj) want = 0;
    else if (pbj) want = 1;
    else if (dbj) want = -1;
    else if (d > 21) want = 1;
    else if (p > d) want = 1;
    else if (p < d) want = -1;
    else want = 0;
    if (r !== want) bad.push(`seed ${seed}: said ${r}, player ${p} dealer ${d}, wanted ${want}`);
  }
  ok('the reported result follows from the two hands', bad.length === 0,
     bad.length ? bad.slice(0, 3).join('; ') : '200 hands');
}

// -- Refusals -------------------------------------------------------------
{
  let h = e.bj_new(3);
  while (e.bj_bust(h) === 0) h = e.bj_hit(h);
  ok('a busted hand refuses another card', e.bj_hit(h) === h);
  ok('a busted hand loses', e.bj_result(h) === -1, e.bj_result(h));
}

// -- Controls -------------------------------------------------------------
ok('control: two new hands are different handles', e.bj_new(5) !== e.bj_new(5));
ok('control: different seeds deal different hands',
   JSON.stringify(phand(e.bj_new(1))) !== JSON.stringify(phand(e.bj_new(2))));
ok('control: the same seed deals the same hand',
   JSON.stringify(phand(e.bj_new(9))) === JSON.stringify(phand(e.bj_new(9))));
// Prove the oracle can disagree, or every agreement arm passed by being blind.
ok('control: the oracle scores a known soft hand correctly',
   best([ACE, values.findIndex(v => v === 6)]) === 17,
   `ace plus six reads ${best([ACE, values.findIndex(v => v === 6)])}`);

console.log(fail === 0
  ? `\nPASS: Blackjack counts, shuffles and pays by the rules (${pass} arms).`
  : `\nFAIL: ${fail} of ${pass + fail} arms.`);
process.exitCode = fail === 0 ? 0 : 1;
