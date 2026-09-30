// Grade the Blackjack wasm module.
//
// Hand value is arithmetic, so this file computes it independently from the
// raw cards and requires the engine to agree: ace softening is where scoring
// goes wrong, and a hand scored 12 instead of 22 is still a plausible number.
// The deck must be a permutation, because a shuffle that drops or duplicates
// a card looks like cards.
//
// The Rules section builds positions from a stacked deck and asserts, for
// each class the rules in Blackjack.codex distinguish, the whole legal set
// (what is offered AND what is refused) and what the round pays.
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

const HIT = 0, STAND = 1, DOUBLE = 2, SPLIT = 3, INSURE = 4, DECLINE = 5;
const NAMES = ['hit', 'stand', 'double', 'split', 'insure', 'decline'];
const hand = (h, k) => [...Array(Math.max(0, e.bj_hn(h, k)))].map((_, i) => e.bj_hc(h, k, i));
const dhand = h => [...Array(e.bj_dcount(h))].map((_, i) => e.bj_dcard(h, i));
const legal = h => [0, 1, 2, 3, 4, 5].filter(a => e.bj_can(h, a) === 1);
const names = as => as.map(a => NAMES[a]).join(',') || 'none';

console.log(`bj-verify ${wasmPath}`);

// -- The deck's own arithmetic -------------------------------------------
const values = [...Array(52)].map((_, c) => e.bj_card_value(c));
const ranks = [...Array(52)].map((_, c) => e.bj_card_rank(c));
ok('every card has a value between 2 and 11',
   values.every(v => v >= 2 && v <= 11), `min ${Math.min(...values)} max ${Math.max(...values)}`);
ok('there are thirteen ranks, four cards each',
   new Set(ranks).size === 13 &&
   [...new Set(ranks)].every(r => ranks.filter(x => x === r).length === 4));
ok('four cards are worth eleven, which is the ace', values.filter(v => v === 11).length === 4);
ok('sixteen cards are worth ten, the tens and the three face ranks',
   values.filter(v => v === 10).length === 16);
ok('a card off the end is refused', e.bj_card_value(-1) === -1 && e.bj_card_rank(-1) === -1);

// The independent oracle. Aces count 11 until that busts, then 1.
const isAce = card => e.bj_card_value(card) === 11;
function best(cards) {
  let total = cards.reduce((n, c) => n + e.bj_card_value(c), 0);
  let aces = cards.filter(isAce).length;
  while (total > 21 && aces > 0) { total -= 10; aces--; }
  return total;
}

// -- The opening, and THE COPY ARM ----------------------------------------
{
  let s0 = e.bj_new(7);
  ok('a new round deals two cards to each side',
     e.bj_hn(s0, 0) === 2 && e.bj_dcount(s0) === 2, `${e.bj_hn(s0, 0)} and ${e.bj_dcount(s0)}`);
  ok('four cards have come off the deck', e.bj_deckpos(s0) === 4, e.bj_deckpos(s0));
  let seed = 7;
  while (e.bj_can(s0, HIT) !== 1 && seed < 60) s0 = e.bj_new(++seed);
  const before = JSON.stringify(hand(s0, 0));
  const hit = e.bj_act(s0, HIT);
  ok('hitting answers a different state', hit !== s0, `seed ${seed}`);
  ok('the hand has one more card', e.bj_hn(hit, 0) === 3, e.bj_hn(hit, 0));
  ok('THE COPY ARM: the table hit from is untouched',
     e.bj_hn(s0, 0) === 2 && JSON.stringify(hand(s0, 0)) === before);
  ok('the card drawn is the next one off the deck', e.bj_deckpos(hit) === e.bj_deckpos(s0) + 1);
}

// -- The value oracle, over many hands ------------------------------------
{
  const wrong = [];
  let softSeen = 0, bustSeen = 0, checked = 0;
  for (let seed = 1; seed <= 300; seed++) {
    let h = e.bj_new(seed);
    if (e.bj_phase(h) === 0) h = e.bj_act(h, DECLINE);
    for (let step = 0; step < 6; step++) {
      const p = hand(h, 0), d = dhand(h);
      checked++;
      if (e.bj_hv(h, 0) !== best(p)) wrong.push(`seed ${seed}: engine ${e.bj_hv(h, 0)} against ${best(p)} for ${p}`);
      if (e.bj_dvalue(h) !== best(d)) wrong.push(`seed ${seed}: dealer ${e.bj_dvalue(h)} against ${best(d)} for ${d}`);
      if (e.bj_hsoft(h, 0) === 1) softSeen++;
      if (e.bj_hv(h, 0) > 21) { bustSeen++; break; }
      if (e.bj_can(h, HIT) !== 1) break;
      h = e.bj_act(h, HIT);
    }
  }
  ok('the engine agrees with an independent hand-value sum', wrong.length === 0,
     `${checked} hands checked` + (wrong.length ? `, first: ${wrong[0]}` : ''));
  ok('control: soft hands occurred, so ace handling was exercised', softSeen > 0, `${softSeen}`);
  ok('control: busts occurred, so the downgrade path was exercised', bustSeen > 0, `${bustSeen}`);
}

// -- The deck is a permutation, and the dealer's rule ---------------------
{
  const dup = [], early = [], soft17 = [];
  let dealerPlayed = 0;
  for (let seed = 1; seed <= 300; seed++) {
    let h = e.bj_new(seed);
    if (e.bj_phase(h) === 0) h = e.bj_act(h, DECLINE);
    while (e.bj_phase(h) === 1) h = e.bj_act(h, STAND);
    const all = [...hand(h, 0), ...dhand(h)];
    if (new Set(all).size !== all.length) dup.push(`seed ${seed}: ${all}`);
    if (e.bj_dcount(h) > 2 || e.bj_dvalue(h) >= 17) dealerPlayed++;
    const d = dhand(h), dv = e.bj_dvalue(h);
    const natural = e.bj_hn(h, 0) === 2 && e.bj_hv(h, 0) === 21;
    const dnat = d.length === 2 && dv === 21;
    if (!natural && !dnat && dv < 17) early.push(`seed ${seed}: dealer stood on ${dv}`);
    // A soft 17 is a hit: the dealer never finishes on one.
    if (!natural && !dnat && dv === 17 && d.length >= 2) {
      let t = d.reduce((n, c) => n + e.bj_card_value(c), 0), a = d.filter(isAce).length;
      while (t > 21 && a > 0) { t -= 10; a--; }
      if (a > 0) soft17.push(`seed ${seed}: dealer stood on soft 17 ${d}`);
    }
  }
  ok('no card is dealt twice out of one deck in 300 rounds', dup.length === 0, dup[0] || '300 rounds');
  ok('the dealer never stands below seventeen', early.length === 0, early.slice(0, 2).join('; ') || `${dealerPlayed} dealer turns`);
  ok('the dealer hits a soft seventeen', soft17.length === 0, soft17.slice(0, 2).join('; ') || 'none stood on one');
}

// -- What a round pays, against an independent settlement ------------------
{
  const bad = [];
  const seen = new Set();
  for (let seed = 1; seed <= 400; seed++) {
    const h = e.bj_auto(e.bj_new(seed));
    const p = best(hand(h, 0)), d = best(dhand(h));
    const pnat = e.bj_hn(h, 0) === 2 && p === 21, dnat = e.bj_dcount(h) === 2 && d === 21;
    let want;
    if (p > 21) want = -2;
    else if (dnat) want = pnat ? 0 : -2;
    else if (pnat) want = 3;
    else if (d > 21 || p > d) want = 2;
    else if (p < d) want = -2;
    else want = 0;
    seen.add(want);
    if (e.bj_net(h) !== want) bad.push(`seed ${seed}: paid ${e.bj_net(h)}, player ${p} dealer ${d}, want ${want}`);
  }
  ok('an auto-played round pays what an independent settlement says', bad.length === 0,
     bad.slice(0, 2).join('; ') || '400 rounds');
  ok('control: wins, losses, pushes and a natural all occurred',
     [-2, 0, 2, 3].every(v => seen.has(v)), [...seen].sort().join(','));
}

// -- THE RUNNER AND THE TABLE ARE ONE GAME ---------------------------------
{
  const bad = [];
  for (let seed = 1; seed <= 300; seed++) {
    const net = e.bj_net(e.bj_auto(e.bj_new(seed)));
    const sign = net > 0 ? 1 : net < 0 ? -1 : 0;
    if (sign !== e.bj_ref(seed)) bad.push(`seed ${seed}: table ${net}, runner ${e.bj_ref(seed)}`);
  }
  ok('the table played by the runner\'s policy reaches the runner\'s result', bad.length === 0,
     bad.slice(0, 2).join('; ') || '300 seeds');
}

// -- RULES: every class of position, from a stacked deck --------------------
// A deck is stacked in dealing order: player, dealer up, player, dealer hole,
// then every later card. Ranks: 0 is the two, 8 the ten, 9 J, 10 Q, 11 K, 12 A.
{
  const C = (r, s = 0) => s * 13 + r;
  let used = {};
  const card = r => C(r, (used[r] = (used[r] ?? -1) + 1) % 4);
  const deal = rs => {
    used = {};
    let h = e.bj_stack(0);
    for (const r of rs) h = e.bj_stack_push(h, card(r));
    return e.bj_stack_deal(h);
  };
  const [T, J, Q, K, A] = [8, 9, 10, 11, 12];
  const v = n => n - 2;             // the rank index of a pip card
  const legalIs = (name, h, want) =>
    ok(name, JSON.stringify(legal(h)) === JSON.stringify(want), `offered ${names(legal(h))}, want ${names(want)}`);

  legalIs('an ordinary opening offers hit and stand only (12 does not double, a 5 and 7 do not split, no ace is up)',
    deal([v(5), v(9), v(7), v(8), v(3)]), [HIT, STAND]);
  legalIs('a total of 9 may double', deal([v(4), v(9), v(5), v(8), v(3)]), [HIT, STAND, DOUBLE]);
  legalIs('a total of 11 may double', deal([v(5), v(9), v(6), v(8), v(3)]), [HIT, STAND, DOUBLE]);
  legalIs('a total of 8 may not double', deal([v(3), v(9), v(5), v(8), v(3)]), [HIT, STAND]);
  legalIs('a soft 17 (ace and six) may not double', deal([A, v(9), v(6), v(8), v(3)]), [HIT, STAND]);
  legalIs('a pair of eights may split', deal([v(8), v(9), v(8), v(7), v(3)]), [HIT, STAND, SPLIT]);
  legalIs('a king and a queen are both worth ten but are not a pair', deal([K, v(9), Q, v(7), v(3)]), [HIT, STAND]);
  {
    const h = deal([v(5), v(9), v(7), v(8), v(3)]);
    ok('a refused action answers the table it was given', e.bj_act(h, DOUBLE) === h && e.bj_act(h, INSURE) === h);
  }
  {
    // 10 + 6 = 16, hit a 5 = 21: the hand stands by itself and the dealer
    // (9 up, 8 down = 17) plays; 21 against 17 pays the bet.
    const h = e.bj_act(deal([T, v(9), v(6), v(8), v(5)]), HIT);
    ok('a hand that reaches 21 stands by itself and the round settles',
       e.bj_phase(h) === 2 && e.bj_hst(h, 0) === 1 && legal(h).length === 0 && e.bj_net(h) === 2,
       `phase ${e.bj_phase(h)}, net ${e.bj_net(h)}, offered ${names(legal(h))}`);
  }
  {
    // 10 + 6 hits a king: bust, and the dealer does not draw to a bust hand.
    const h = e.bj_act(deal([T, v(9), v(6), v(2), K, v(9)]), HIT);
    ok('a bust hand loses its bet, is offered nothing, and the dealer does not draw',
       e.bj_phase(h) === 2 && e.bj_hst(h, 0) === 2 && e.bj_net(h) === -2 && e.bj_dcount(h) === 2,
       `net ${e.bj_net(h)}, dealer cards ${e.bj_dcount(h)}`);
  }
  {
    const h = e.bj_act(deal([v(5), v(9), v(6), v(7), T, v(2)]), DOUBLE);
    ok('a double down doubles the bet, takes exactly one card and stands',
       e.bj_hbet(h, 0) === 4 && e.bj_hn(h, 0) === 3 && e.bj_hst(h, 0) === 1 && e.bj_phase(h) === 2,
       `bet ${e.bj_hbet(h, 0)}, cards ${e.bj_hn(h, 0)}`);
    // 21 against the dealer's 9 + 7 + 2 = 18: the doubled bet is won.
    ok('the double is paid on the doubled bet', e.bj_net(h) === 4, e.bj_net(h));
  }
  {
    const h = deal([A, v(9), K, v(8)]);
    ok('a natural against no dealer natural settles at once and pays 3:2',
       e.bj_phase(h) === 2 && legal(h).length === 0 && e.bj_net(h) === 3 && e.bj_dcount(h) === 2,
       `phase ${e.bj_phase(h)}, net ${e.bj_net(h)}`);
  }
  {
    const h = deal([T, K, Q, A]);
    ok('a dealer natural under a ten up ends the round: a 20 loses and is offered nothing',
       e.bj_phase(h) === 2 && legal(h).length === 0 && e.bj_net(h) === -2, `net ${e.bj_net(h)}`);
  }
  {
    const h = deal([T, A, v(9), K]);
    legalIs('an ace up offers insurance and nothing else', h, [INSURE, DECLINE]);
    const ins = e.bj_act(h, INSURE), dec = e.bj_act(h, DECLINE);
    ok('insured against a dealer natural: the bet is lost and the insurance pays 2:1, net 0',
       e.bj_phase(ins) === 2 && e.bj_insured(ins) === 1 && e.bj_net(ins) === 0, `net ${e.bj_net(ins)}`);
    ok('declined against a dealer natural: the bet is lost', e.bj_phase(dec) === 2 && e.bj_net(dec) === -2, `net ${e.bj_net(dec)}`);
  }
  {
    const h = e.bj_act(deal([T, A, v(9), v(6), v(2)]), INSURE);
    legalIs('insured with no dealer natural: play goes on with the hand\'s own choices', h, [HIT, STAND]);
    const s = e.bj_act(h, STAND);
    // 19 against A + 6 = soft 17, which draws a 2: 19, a push; the insurance is lost.
    ok('the lost insurance is one unit', e.bj_phase(s) === 2 && e.bj_net(s) === -1 && e.bj_dvalue(s) === 19,
       `net ${e.bj_net(s)}, dealer ${e.bj_dvalue(s)}`);
  }
  {
    const h = e.bj_act(deal([A, A, K, K]), DECLINE);
    ok('two naturals push', e.bj_phase(h) === 2 && e.bj_net(h) === 0, `net ${e.bj_net(h)}`);
  }
  {
    // 10 + 8 stands against 9 up and 8 hole: 17, hard, the dealer stands.
    const h = e.bj_act(deal([T, v(9), v(8), v(8), v(5)]), STAND);
    ok('the dealer stands on a hard 17', e.bj_dcount(h) === 2 && e.bj_net(h) === 2, `dealer ${e.bj_dvalue(h)}, net ${e.bj_net(h)}`);
  }
  {
    // A 6 up and an ace in the hole: soft 17 draws. Up is not an ace, so no insurance.
    const h = e.bj_act(deal([T, v(6), v(8), A, v(3)]), STAND);
    ok('the dealer hits a soft 17', e.bj_dcount(h) === 3 && e.bj_dvalue(h) === 20 && e.bj_net(h) === -2,
       `dealer ${dhand(h)} = ${e.bj_dvalue(h)}, net ${e.bj_net(h)}`);
  }
  {
    // 8,8 split; hand 0 takes a 3 (11), hand 1 waits with one card.
    const h = e.bj_act(deal([v(8), v(9), v(8), v(7), v(3), v(10)]), SPLIT);
    ok('a split makes two hands, a bet on each, the first dealt its second card and the second waiting',
       e.bj_nh(h) === 2 && e.bj_cur(h) === 0 && e.bj_hn(h, 0) === 2 && e.bj_hn(h, 1) === 1 &&
       e.bj_hbet(h, 0) === 2 && e.bj_hbet(h, 1) === 2,
       `hands ${e.bj_nh(h)}, cards ${e.bj_hn(h, 0)} and ${e.bj_hn(h, 1)}`);
    legalIs('a split hand of 11 may double', h, [HIT, STAND, DOUBLE]);
    const s = e.bj_act(h, STAND);
    ok('standing moves play to the second hand, which then takes its second card',
       e.bj_cur(s) === 1 && e.bj_hn(s, 1) === 2 && e.bj_phase(s) === 1, `cur ${e.bj_cur(s)}, cards ${e.bj_hn(s, 1)}`);
  }
  {
    // A,A split: each takes one card (a king, a nine) and stands; 21 + 20
    // against the dealer's 9 + 8 = 17. The 21 is not a natural: 1:1.
    const h = e.bj_act(deal([A, v(9), A, v(8), K, v(9)]), SPLIT);
    ok('split aces take one card each and stand, and a 21 after a split pays 1:1',
       e.bj_phase(h) === 2 && e.bj_hn(h, 0) === 2 && e.bj_hn(h, 1) === 2 && e.bj_net(h) === 4,
       `phase ${e.bj_phase(h)}, net ${e.bj_net(h)}`);
  }
  {
    // The deck's four eights make four hands: each split hands hand 0 another
    // eight until the last, a three. One deck cannot reach the cap of four.
    let h = deal([v(8), v(9), v(8), v(7), v(8), v(8), v(3), v(2), v(4), v(5)]);
    let splits = 0;
    while (e.bj_can(h, SPLIT) === 1 && splits < 6) { h = e.bj_act(h, SPLIT); splits++; }
    ok('a pair dealt to a split hand splits again, to four hands', splits === 3 && e.bj_nh(h) === 4, `${splits} splits, ${e.bj_nh(h)} hands`);
    legalIs('the first hand, now an eight and a three, is offered no split', h, [HIT, STAND, DOUBLE]);
  }
  {
    // Only four cards in the deck: nothing can be drawn.
    const h = deal([v(5), v(9), v(6), v(8)]);
    legalIs('an empty deck refuses the hit and the double', h, [STAND]);
  }
}

console.log(fail === 0
  ? `\nPASS: Blackjack counts, shuffles and pays by the rules (${pass} arms).`
  : `\nFAIL: ${fail} of ${pass + fail} arms.`);
process.exitCode = fail === 0 ? 0 : 1;
