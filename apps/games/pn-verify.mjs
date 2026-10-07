// Grade the Pinochle wasm module against pagat's single deck partnership
// pinochle (https://www.pagat.com/marriage/pinmain.html).
//
// Every rule below is written out here from pagat, not read from the
// engine: the meld table row by row, the auction's order, the four cards
// each way, following and heading the trick, counters and the last trick,
// making or going set, and the game at 1500. A deck holds TWO of every
// card, so every meld has a single and a double form, and the built hands
// below name both.
//
// Usage: node apps/games/pn-verify.mjs [path/to/pinochle.wasm]

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import { gameInstance } from '../landing/web/games/arcade.js';

const here = dirname(fileURLToPath(import.meta.url));
const wasmPath = process.argv[2] ||
  join(here, '..', 'landing', 'web', 'games', 'pinochle.wasm');


const inst = gameInstance(
  new WebAssembly.Module(readFileSync(wasmPath)));
const e = inst.exports;

let pass = 0, fail = 0;
const ok = (name, cond, detail) => {
  if (cond) { console.log(`  ok    ${name}${detail !== undefined ? ': ' + detail : ''}`); pass++; }
  else { console.log(`  FAIL  ${name}${detail !== undefined ? ': ' + detail : ''}`); fail++; }
};

// id = suit * 12 + copy * 6 + rank; ranks 0..5 are 9, J, Q, K, 10, A;
// suits 0..3 are clubs, diamonds, hearts, spades.
const suitOf = c => Math.floor(c / 12);
const rankOf = c => c % 6;
const NINE = 0, JACK = 1, QUEEN = 2, KING = 3, TEN = 4, ACE = 5;
const CLUBS = 0, DIAMONDS = 1, HEARTS = 2, SPADES = 3;
const id = (s, r, copy = 0) => s * 12 + copy * 6 + r;
// Counters: aces, tens and kings, ten each.
const POINTS = c => [ACE, TEN, KING].includes(rankOf(c)) ? 10 : 0;
const PHASE = { AUCTION: 0, TRUMP: 1, PARTNER: 2, BACK: 3, PLAY: 4, SCORED: 5, OVER: 6 };

const countOf = (cards, s, r) => cards.filter(c => suitOf(c) === s && rankOf(c) === r).length;
const handOf = (g, p) => [...Array(e.pn_count(g, p)).keys()].map(i => e.pn_card(g, p, i));

// pagat's meld table, row by row.
function meldPagat(cards, trump) {
  let m = 0;
  for (let s = 0; s < 4; s++) {
    if (s === trump) continue;
    m += 20 * Math.min(countOf(cards, s, KING), countOf(cards, s, QUEEN)); // common marriage
  }
  if (trump >= 0) {
    const n = r => countOf(cards, trump, r);
    const runs = Math.min(n(ACE), n(TEN), n(KING), n(QUEEN), n(JACK));
    if (runs >= 2) m += 1500;                                 // double run
    else if (runs === 1) {
      const extra = (n(KING) === 2 ? 'K' : '') + (n(QUEEN) === 2 ? 'Q' : '');
      m += { '': 150, K: 190, Q: 190, KQ: 230 }[extra];       // run, with extra king/queen/marriage
    } else m += 40 * Math.min(n(KING), n(QUEEN));             // royal marriage
    m += 10 * n(NINE);                                        // the nine of trump
  }
  const pin = Math.min(countOf(cards, DIAMONDS, JACK), countOf(cards, SPADES, QUEEN));
  m += pin >= 2 ? 300 : pin === 1 ? 40 : 0;
  const single = { [ACE]: 100, [KING]: 80, [QUEEN]: 60, [JACK]: 40 };
  for (const r of [ACE, KING, QUEEN, JACK]) {
    const k = Math.min(...[0, 1, 2, 3].map(s => countOf(cards, s, r)));
    m += k >= 2 ? single[r] * 10 : k === 1 ? single[r] : 0;
  }
  return m;
}

console.log(`pn-verify ${wasmPath}`);

// -- THE MELD TABLE ---------------------------------------------------------
{
  const run = s => [ACE, TEN, KING, QUEEN, JACK].map(r => id(s, r));
  const built = [
    ['a bare run', run(HEARTS), HEARTS, 150],
    ['a run with an extra king', [...run(HEARTS), id(HEARTS, KING, 1)], HEARTS, 190],
    ['a run with an extra queen', [...run(HEARTS), id(HEARTS, QUEEN, 1)], HEARTS, 190],
    ['a run with an extra marriage', [...run(HEARTS), id(HEARTS, KING, 1), id(HEARTS, QUEEN, 1)], HEARTS, 230],
    ['a double run', [...run(HEARTS), ...[ACE, TEN, KING, QUEEN, JACK].map(r => id(HEARTS, r, 1))], HEARTS, 1500],
    ['the same run out of trump is one common marriage', run(HEARTS), CLUBS, 20],
    ['a royal marriage', [id(SPADES, KING), id(SPADES, QUEEN)], SPADES, 40],
    ['two royal marriages', [id(SPADES, KING), id(SPADES, QUEEN), id(SPADES, KING, 1), id(SPADES, QUEEN, 1)], SPADES, 80],
    ['two common marriages', [id(CLUBS, KING), id(CLUBS, QUEEN), id(CLUBS, KING, 1), id(CLUBS, QUEEN, 1)], HEARTS, 40],
    ['one nine of trump', [id(DIAMONDS, NINE)], DIAMONDS, 10],
    ['both nines of trump', [id(DIAMONDS, NINE), id(DIAMONDS, NINE, 1)], DIAMONDS, 20],
    ['a nine off trump', [id(DIAMONDS, NINE)], CLUBS, 0],
    ['a pinochle', [id(DIAMONDS, JACK), id(SPADES, QUEEN)], CLUBS, 40],
    ['a double pinochle', [id(DIAMONDS, JACK), id(SPADES, QUEEN), id(DIAMONDS, JACK, 1), id(SPADES, QUEEN, 1)], CLUBS, 300],
    ['aces around', [0, 1, 2, 3].map(s => id(s, ACE)), CLUBS, 100],
    ['aces around twice', [0, 1, 2, 3].flatMap(s => [id(s, ACE), id(s, ACE, 1)]), CLUBS, 1000],
    ['kings around, two of them a royal marriage with the queen', [...[0, 1, 2, 3].map(s => id(s, KING)), id(CLUBS, QUEEN)], CLUBS, 120],
    ['jacks around with a pinochle in them', [...[0, 1, 2, 3].map(s => id(s, JACK)), id(SPADES, QUEEN)], HEARTS, 80],
  ];
  const oracle = [], engine = [];
  for (const [name, cards, trump, want] of built) {
    if (meldPagat(cards, trump) !== want) oracle.push(`${name}: ${meldPagat(cards, trump)}`);
    let l = e.pn_cards0(0);
    for (const c of cards) l = e.pn_cardsadd(l, c);
    const got = e.pn_meldof(l, trump);
    if (got !== want) engine.push(`${name}: ${got}, pagat ${want}`);
  }
  ok('control: this side reads pagat\'s table right on every built hand', oracle.length === 0,
     oracle.length ? oracle.join('; ') : `${built.length} hands`);
  ok('every built hand melds what pagat\'s table says', engine.length === 0,
     engine.length ? engine.join('; ') : `${built.length} hands`);

  const bad = [];
  let hands = 0, nonZero = 0;
  for (let seed = 1; seed <= 40; seed++) {
    const g = e.pn_new(seed);
    for (let p = 0; p < 4; p++) {
      for (let t = 0; t < 4; t++) {
        const want = meldPagat(handOf(g, p), t), got = e.pn_meldin(g, p, t);
        hands++;
        if (want) nonZero++;
        if (want !== got) bad.push(`seed ${seed} player ${p} trump ${t}: engine ${got}, pagat ${want}`);
      }
    }
  }
  ok('every dealt hand melds by the table under every trump', bad.length === 0,
     bad.length ? bad.slice(0, 3).join('; ') : `${hands} hand-and-trump pairs, ${nonZero} melding`);
}

// -- THE DEAL ---------------------------------------------------------------
{
  const bad = [];
  for (let seed = 1; seed <= 40; seed++) {
    const g = e.pn_new(seed);
    const all = [0, 1, 2, 3].flatMap(p => handOf(g, p));
    if ([0, 1, 2, 3].some(p => e.pn_count(g, p) !== 12)) bad.push(`seed ${seed}: hands of ${[0, 1, 2, 3].map(p => e.pn_count(g, p))}`);
    if (new Set(all).size !== 48 || all.some(c => c < 0 || c > 47)) bad.push(`seed ${seed}: not the 48 cards`);
    if (e.pn_trump(g) !== -1) bad.push(`seed ${seed}: trump ${e.pn_trump(g)} before the auction`);
    if (e.pn_phase(g) !== PHASE.AUCTION) bad.push(`seed ${seed}: opens in phase ${e.pn_phase(g)}`);
    if (e.pn_cur(g) !== (e.pn_dealer(g) + 1) % 4) bad.push(`seed ${seed}: seat ${e.pn_cur(g)} opens, dealer ${e.pn_dealer(g)}`);
    if (e.pn_card(g, 0, 12) !== -1 || e.pn_card(g, 4, 0) !== -1) bad.push(`seed ${seed}: a card off the hand did not read -1`);
  }
  ok('every deal is the 48 cards twelve each, no trump yet, the auction opened at the dealer\'s left',
     bad.length === 0, bad.length ? bad.slice(0, 3).join('; ') : '40 deals');
  ok('control: the deck holds two of every card, and 24 counters make 240',
     [0, 1, 2, 3].every(s => [0, 1, 2, 3, 4, 5].every(r =>
       [...Array(48).keys()].filter(c => suitOf(c) === s && rankOf(c) === r).length === 2))
     && [...Array(48).keys()].reduce((a, c) => a + POINTS(c), 0) === 240);
  ok('you open the first auction', e.pn_cur(e.pn_new(1)) === 0 && e.pn_dealer(e.pn_new(1)) === 3);
}

// -- A WHOLE HAND, EVERY TRANSITION WATCHED ---------------------------------
//
// `hand` steps one hand from its deal to its score, every seat played by
// the module, and grades each transition as it goes. The trick winner, the
// counters and the score are all decided on this side.
const beats = (c, best, trump) => {
  const cs = suitOf(c), bs = suitOf(best);
  if (cs === trump && bs !== trump) return true;
  if (cs === bs) return rankOf(c) > rankOf(best);
  return false;
};
const tally = {
  bids: 0, passes: 0, stuck: 0, hands: 0, made: 0, set: 0, defZero: 0,
  revokeAsked: 0, revokeRefused: 0, tricks: 0, ties: 0,
};
function hand(g0, bad, label) {
  let g = g0, guard = 0;
  let calledBid = false;
  let meld = null, played = [], trick = [], pts = [0, 0];
  while (e.pn_phase(g) < PHASE.SCORED && guard++ < 400) {
    const ph = e.pn_phase(g), seat = e.pn_cur(g);
    const before = g;
    if (ph === PHASE.AUCTION) {
      const bid = e.pn_bid(g), min = e.pn_minbid(g);
      if (min !== (bid === 0 ? 250 : bid + 10)) bad.push(`${label}: minimum ${min} over ${bid}`);
      if (e.pn_out(g, seat) === 1) bad.push(`${label}: seat ${seat}, out, asked to call`);
      g = e.pn_step(g);
      if (e.pn_phase(g) === PHASE.AUCTION) {
        if (e.pn_bid(g) !== bid) {
          tally.bids++; calledBid = true;
          if (e.pn_bidder(g) !== seat || e.pn_bid(g) < min || e.pn_bid(g) % 10 !== 0) bad.push(`${label}: seat ${seat} bid ${e.pn_bid(g)}`);
        } else {
          tally.passes++;
          if (e.pn_out(g, seat) !== 1) bad.push(`${label}: seat ${seat} passed and is not out`);
        }
        let want = (seat + 1) % 4;
        while (e.pn_out(g, want) === 1) want = (want + 1) % 4;
        if (e.pn_cur(g) !== want) bad.push(`${label}: after seat ${seat} the call went to ${e.pn_cur(g)}, not ${want}`);
      } else {
        const outs = [0, 1, 2, 3].filter(s => e.pn_out(g, s) === 1);
        const decl = [0, 1, 2, 3].find(s => e.pn_out(g, s) === 0);
        if (outs.length !== 3) bad.push(`${label}: the auction ended with ${outs.length} out`);
        if (e.pn_bidder(g) !== decl || e.pn_cur(g) !== decl) bad.push(`${label}: declarer ${e.pn_bidder(g)}, the seat still in ${decl}`);
        if (!calledBid) {
          tally.stuck++;
          if (decl !== e.pn_dealer(g) || e.pn_bid(g) !== 250) bad.push(`${label}: nobody bid, and seat ${decl} took it at ${e.pn_bid(g)}`);
        }
      }
    } else if (ph === PHASE.TRUMP) {
      g = e.pn_step(g);
      const decl = e.pn_bidder(g);
      if (e.pn_trump(g) < 0 || e.pn_trump(g) > 3) bad.push(`${label}: trump ${e.pn_trump(g)}`);
      if (e.pn_cur(g) !== (decl + 2) % 4) bad.push(`${label}: seat ${e.pn_cur(g)} passes first, not the partner`);
    } else if (ph === PHASE.PARTNER || ph === PHASE.BACK) {
      const giver = seat, taker = (seat + 2) % 4;
      const gb = handOf(g, giver), tb = handOf(g, taker);
      g = e.pn_step(g);
      const ga = handOf(g, giver), ta = handOf(g, taker);
      const left = gb.filter(c => !ga.includes(c)), arrived = ta.filter(c => !tb.includes(c));
      if (left.length !== 4 || ga.length !== gb.length - 4 || ta.length !== tb.length + 4
          || left.slice().sort().join() !== arrived.slice().sort().join()) {
        bad.push(`${label}: seat ${giver} passed ${left.length}, seat ${taker} took ${arrived.length}`);
      }
      if (ph === PHASE.PARTNER && giver !== (e.pn_bidder(g) + 2) % 4) bad.push(`${label}: seat ${giver} passed first`);
      if (ph === PHASE.BACK && giver !== e.pn_bidder(g)) bad.push(`${label}: seat ${giver} passed back`);
      if (e.pn_phase(g) === PHASE.PLAY) {
        if ([0, 1, 2, 3].some(p => e.pn_count(g, p) !== 12)) bad.push(`${label}: after the pass, hands of ${[0, 1, 2, 3].map(p => e.pn_count(g, p))}`);
        if (e.pn_cur(g) !== e.pn_bidder(g)) bad.push(`${label}: seat ${e.pn_cur(g)} leads, the declarer is ${e.pn_bidder(g)}`);
        const t = e.pn_trump(g);
        meld = [0, 1, 2, 3].map(p => meldPagat(handOf(g, p), t));
        for (const team of [0, 1]) {
          if (e.pn_hmeld(g, team) !== meld[team] + meld[team + 2]) bad.push(`${label}: team ${team} melded ${e.pn_hmeld(g, team)}, pagat ${meld[team] + meld[team + 2]}`);
        }
      }
    } else if (ph === PHASE.PLAY) {
      const mine = handOf(g, seat);
      if (trick.length) {
        const led = suitOf(trick[0].c);
        const wrong = mine.findIndex(c => suitOf(c) !== led);
        if (mine.some(c => suitOf(c) === led) && wrong >= 0) {
          tally.revokeAsked++;
          if (e.pn_legal(g, wrong) === 0 && e.pn_play(g, wrong) === g) tally.revokeRefused++;
        }
      }
      g = e.pn_step(g);
      const after = handOf(g, seat);
      const gone = mine.filter(c => !after.includes(c));
      if (gone.length !== 1) { bad.push(`${label}: seat ${seat} lost ${gone.length} cards`); break; }
      trick.push({ seat, c: gone[0] });
      if (trick.length === 4) {
        const t = e.pn_trump(g);
        let w = trick[0];
        for (const x of trick) if (beats(x.c, w.c, t)) w = x;
        if (trick.filter(x => x.c !== w.c && suitOf(x.c) === suitOf(w.c) && rankOf(x.c) === rankOf(w.c)).length) tally.ties++;
        played.push(...trick.map(x => x.c));
        const last = played.length === 48;
        pts[w.seat % 2] += trick.reduce((a, x) => a + POINTS(x.c), 0) + (last ? 10 : 0);
        tally.tricks++;
        if (!last && e.pn_cur(g) !== w.seat) bad.push(`${label}: seat ${e.pn_cur(g)} leads, seat ${w.seat} won the trick`);
        trick = [];
      }
    }
    if (g === before) { bad.push(`${label}: phase ${ph} seat ${seat} would not move`); break; }
  }
  return { g, meld, pts, played };
}

// Score a finished hand on this side and compare.
function gradeScore(g0, r, bad, label) {
  const { g, meld, pts } = r;
  if (!meld) { bad.push(`${label}: never reached the play`); return; }
  if (pts[0] + pts[1] !== 250) bad.push(`${label}: the tricks came to ${pts[0] + pts[1]}`);
  for (const team of [0, 1]) {
    if (e.pn_pts(g, team) !== pts[team]) bad.push(`${label}: team ${team} took ${e.pn_pts(g, team)}, decided here ${pts[team]}`);
  }
  const d = e.pn_bidder(g) % 2, bid = e.pn_bid(g);
  const m = [meld[0] + meld[2], meld[1] + meld[3]];
  const make = m[d] + pts[d] >= bid;
  const delta = [0, 0];
  delta[d] = make ? m[d] + pts[d] : -bid;
  delta[1 - d] = pts[1 - d] > 0 ? m[1 - d] + pts[1 - d] : 0;
  tally.hands++;
  if (make) tally.made++; else tally.set++;
  if (pts[1 - d] === 0) tally.defZero++;
  if (e.pn_made(g) !== (make ? 1 : 0)) bad.push(`${label}: made ${e.pn_made(g)}, ${m[d]} + ${pts[d]} against ${bid}`);
  for (const team of [0, 1]) {
    const want = e.pn_score(g0, team) + delta[team];
    if (e.pn_score(g, team) !== want) bad.push(`${label}: team ${team} scored ${e.pn_score(g, team)}, wanted ${want}`);
  }
}

{
  const bad = [];
  for (let seed = 1; seed <= 40; seed++) {
    const g0 = e.pn_new(seed);
    const r = hand(g0, bad, `seed ${seed}`);
    gradeScore(g0, r, bad, `seed ${seed}`);
  }
  ok('CONTROL: the auctions held bids, passes, and a dealer stuck at 250',
     tally.bids > 0 && tally.passes > 0 && tally.stuck > 0, `${tally.bids} bids, ${tally.passes} passes, ${tally.stuck} stuck`);
  ok('CONTROL: hands were made and hands went set', tally.made > 0 && tally.set > 0,
     `${tally.made} made, ${tally.set} set of ${tally.hands}`);
  ok('CONTROL: tricks were decided between two identical cards', tally.ties > 0, `${tally.ties} tricks`);
  ok('CONTROL: a revoke was offered and refused every time', tally.revokeAsked > 0 && tally.revokeRefused === tally.revokeAsked,
     `${tally.revokeRefused} of ${tally.revokeAsked}`);
  ok('a hand is the auction, trump, four each way, the declarer\'s lead, the play and the score, all by pagat',
     bad.length === 0, bad.length ? bad.slice(0, 3).join('; ') : `${tally.hands} hands, ${tally.tricks} tricks`);
  console.log(`  note  the defenders took no counter and not the last trick in ${tally.defZero} of ${tally.hands} hands`);
}

// -- YOUR SEAT ----------------------------------------------------------------
// The calls a person makes through the page's exports, and what each refuses.
{
  const bad = [];
  let named = 0, gave = 0;
  for (let seed = 1; seed <= 60; seed++) {
    let g = e.pn_new(seed);
    const min = e.pn_minbid(g);
    if (e.pn_callbid(g, min - 10) !== g || e.pn_callbid(g, min + 5) !== g) bad.push(`seed ${seed}: an illegal bid was taken`);
    if (e.pn_canbid(g, min) !== 1 || e.pn_canbid(g, min + 40) !== 1) bad.push(`seed ${seed}: a legal bid was refused`);
    if (e.pn_play(g, 0) !== g || e.pn_name(g, 0) !== g || e.pn_give(g) !== g || e.pn_next(g) !== g) bad.push(`seed ${seed}: a move out of its phase was taken`);
    // You bid, and the others play on until it is your call again.
    g = e.pn_callbid(g, min + 40);
    if (e.pn_bid(g) !== min + 40 || e.pn_bidder(g) !== 0) bad.push(`seed ${seed}: a jump to ${min + 40} made ${e.pn_bid(g)}`);
    let guard = 0;
    while (e.pn_phase(g) === PHASE.AUCTION && guard++ < 50) {
      g = e.pn_cur(g) === 0 ? e.pn_pass(g) : e.pn_step(g);
    }
    if (e.pn_phase(g) === PHASE.TRUMP && e.pn_cur(g) === 0) {
      if (e.pn_name(g, 4) !== g || e.pn_name(g, -1) !== g) bad.push(`seed ${seed}: a fifth suit was named`);
      const s = seed % 4;
      g = e.pn_name(g, s);
      named++;
      if (e.pn_trump(g) !== s) bad.push(`seed ${seed}: named ${s}, trump is ${e.pn_trump(g)}`);
    }
    guard = 0;
    while (e.pn_phase(g) < PHASE.PLAY && guard++ < 20) {
      if ((e.pn_phase(g) === PHASE.PARTNER || e.pn_phase(g) === PHASE.BACK) && e.pn_cur(g) === 0) {
        const before = handOf(g, 0);
        let h = g;
        for (const i of [1, 3, 5]) h = e.pn_mark(h, i);
        if (e.pn_cangive(h) !== 0 || e.pn_give(h) !== h) bad.push(`seed ${seed}: three cards could be passed`);
        if (e.pn_mark(h, 3) !== h) bad.push(`seed ${seed}: a card was marked twice`);
        const four = e.pn_mark(h, 7);
        if (e.pn_marked(g, 1) !== 0) bad.push(`seed ${seed}: marking wrote through the handle the caller held`);
        if (e.pn_mark(four, 8) !== four) bad.push(`seed ${seed}: a fifth card was marked`);
        const cleared = e.pn_clear(four);
        if ([1, 3, 5, 7].some(i => e.pn_marked(cleared, i) !== 0)) bad.push(`seed ${seed}: the marks did not clear`);
        const next = e.pn_give(four);
        const sent = [1, 3, 5, 7].map(i => before[i]).sort().join();
        const left = before.filter(c => !handOf(next, 0).includes(c)).sort().join();
        if (sent !== left) bad.push(`seed ${seed}: marked ${sent}, sent ${left}`);
        gave++;
        g = next;
      } else g = e.pn_step(g);
    }
  }
  ok('CONTROL: you named trump and passed cards', named > 0 && gave > 0, `named ${named}, passed ${gave}`);
  ok('your bids, trump and pass take only what the rules allow, and refusals hand back the same handle',
     bad.length === 0, bad.length ? bad.slice(0, 3).join('; ') : '60 deals');
}

// -- THE GAME ---------------------------------------------------------------
{
  const bad = [];
  let games = 0, handsPlayed = 0, both = 0, dealerTurns = 0;
  for (let seed = 1; seed <= 6; seed++) {
    if (e.__heap_reset) e.__heap_reset();
    let g = e.pn_new(seed * 101), guard = 0;
    while (e.pn_done(g) === 0 && guard++ < 40) {
      const g0 = g, dealer = e.pn_dealer(g);
      const r = hand(g, bad, `game ${seed} hand ${e.pn_hands(g)}`);
      gradeScore(g0, r, bad, `game ${seed} hand ${e.pn_hands(g)}`);
      g = r.g;
      handsPlayed++;
      const s0 = e.pn_score(g, 0), s1 = e.pn_score(g, 1);
      const over = s0 >= 1500 || s1 >= 1500;
      if ((e.pn_done(g) === 1) !== over) bad.push(`game ${seed}: ${s0} to ${s1}, over ${e.pn_done(g)}`);
      if (over) {
        const d = e.pn_bidder(g) % 2;
        const want = s0 >= 1500 && s1 >= 1500 ? d : s0 >= 1500 ? 0 : 1;
        if (s0 >= 1500 && s1 >= 1500) both++;
        if (e.pn_gwinner(g) !== want) bad.push(`game ${seed}: won by ${e.pn_gwinner(g)} at ${s0} to ${s1}`);
        if (e.pn_step(g) !== g || e.pn_next(g) !== g) bad.push(`game ${seed}: moved after the game was over`);
      } else {
        const n = e.pn_step(g);
        if (e.pn_dealer(n) === (dealer + 1) % 4 && e.pn_hands(n) === e.pn_hands(g) + 1) dealerTurns++;
        else bad.push(`game ${seed}: the next deal is dealer ${e.pn_dealer(n)} after ${dealer}`);
        if (e.pn_score(n, 0) !== s0 || e.pn_score(n, 1) !== s1) bad.push(`game ${seed}: the scores moved at the deal`);
        g = n;
      }
    }
    if (e.pn_done(g) === 1) games++;
  }
  ok('CONTROL: whole games reached 1500', games > 0, `${games} games, ${handsPlayed} hands`);
  ok('scores carry from hand to hand, the deal passes left, and the game ends at 1500',
     bad.length === 0, bad.length ? bad.slice(0, 3).join('; ') : `${dealerTurns} new deals`);
  console.log(`  note  both teams passed 1500 on one hand in ${both} of ${games} games`);
}

// -- The session runner is one hand of the same game ------------------------
{
  const bad = [];
  for (let seed = 1; seed <= 20; seed++) {
    let g = e.pn_new(seed), guard = 0;
    while (e.pn_phase(g) < PHASE.SCORED && guard++ < 400) g = e.pn_step(g);
    const r = e.pn_run(seed);
    if (e.pn_t0(r) !== e.pn_score(g, 0) || e.pn_t1(r) !== e.pn_score(g, 1)) {
      bad.push(`seed ${seed}: the runner says ${e.pn_t0(r)}/${e.pn_t1(r)}, the hand ${e.pn_score(g, 0)}/${e.pn_score(g, 1)}`);
    }
    const w = e.pn_winner(r), t0 = e.pn_t0(r), t1 = e.pn_t1(r);
    if (w !== (t0 > t1 ? 0 : t1 > t0 ? 1 : -1)) bad.push(`seed ${seed}: winner ${w} at ${t0}/${t1}`);
  }
  ok('the session runner scores one hand the way the table does', bad.length === 0,
     bad.length ? bad.slice(0, 3).join('; ') : '20 hands');
}

console.log(fail === 0
  ? `\nPASS: Pinochle plays by pagat's partnership rules (${pass} arms).`
  : `\nFAIL: ${fail} of ${pass + fail} arms.`);
process.exitCode = fail === 0 ? 0 : 1;
