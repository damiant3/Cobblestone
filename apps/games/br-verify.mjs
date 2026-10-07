// Grade the Bridge wasm module against pagat's contract bridge
// (https://www.pagat.com/auctionwhist/bridge.html), one duplicate board.
//
// Every rule is written out here from pagat and not read from the engine:
// the board's dealer and vulnerability, which calls are legal, when the
// auction ends and who declares, the opening lead and the dummy, following
// suit, and the duplicate score. The trick winner is decided on this side
// by the identity of the winning card (GAME-17).
//
// Usage: node apps/games/br-verify.mjs [path/to/bridge.wasm]

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import { gameInstance } from '../landing/web/games/arcade.js';

const here = dirname(fileURLToPath(import.meta.url));
const wasmPath = process.argv[2] ||
  join(here, '..', 'landing', 'web', 'games', 'bridge.wasm');


const inst = gameInstance(
  new WebAssembly.Module(readFileSync(wasmPath)));
const e = inst.exports;

let pass = 0, fail = 0;
const ok = (name, cond, detail) => {
  if (cond) { console.log(`  ok    ${name}${detail !== undefined ? ': ' + detail : ''}`); pass++; }
  else { console.log(`  FAIL  ${name}${detail !== undefined ? ': ' + detail : ''}`); fail++; }
};

// Seats: north 0, south 1, east 2, west 3. Clockwise is N, E, S, W.
const SEATS = [0, 1, 2, 3];
const NEXT = [2, 3, 1, 0];
const team = s => s <= 1 ? 0 : 1;
const partner = s => [1, 0, 3, 2][s];
// Card suits 0..3 are spades, hearts, diamonds, clubs; denominations 0..4
// are clubs, diamonds, hearts, spades, no trumps.
const suitOfStrain = d => d === 4 ? 4 : 3 - d;
const hand = (h, s) => [...Array(e.br_count(h, s))].map((_, i) => e.br_card(h, s, i));
const PASS = 0, DBL = 1, RDBL = 2;
const bid = (level, d) => 5 * level + d;

console.log(`br-verify ${wasmPath}`);

// -- The deck's arithmetic ------------------------------------------------
const hcps = [...Array(52)].map((_, c) => e.br_card_hcp(c));
ok('the four honours in each suit are worth 4, 3, 2 and 1, forty in the deck',
   [4, 3, 2, 1].every(v => hcps.filter(x => x === v).length === 4) && hcps.reduce((a, b) => a + b, 0) === 40);
ok('there are four suits of thirteen',
   [0, 1, 2, 3].every(s => [...Array(52)].filter((_, c) => e.br_suit(c) === s).length === 13));

// -- THE BOARD --------------------------------------------------------------
// pagat's sixteen-board vulnerability table; the dealer goes N, E, S, W.
const VUL = ['none', 'NS', 'EW', 'both', 'NS', 'EW', 'both', 'none',
             'EW', 'both', 'none', 'NS', 'both', 'none', 'NS', 'EW'];
const VULCODE = { none: 0, NS: 1, EW: 2, both: 3 };
const vulFor = (v, t) => v === 3 || (t === 0 ? v === 1 : v === 2);
{
  const bad = [];
  for (let seed = 1; seed <= 32; seed++) {
    const g = e.br_new(seed);
    const board = 1 + (seed - 1) % 16;
    const dealer = [0, 2, 1, 3][(board - 1) % 4];
    if (e.br_board(g) !== board) bad.push(`seed ${seed}: board ${e.br_board(g)}`);
    if (e.br_dealer(g) !== dealer || e.br_cur(g) !== dealer) bad.push(`board ${board}: dealer ${e.br_dealer(g)}, to call ${e.br_cur(g)}`);
    if (e.br_vul(g) !== VULCODE[VUL[board - 1]]) bad.push(`board ${board}: vulnerability ${e.br_vul(g)}, pagat ${VUL[board - 1]}`);
    const all = SEATS.flatMap(s => hand(g, s));
    if (SEATS.some(s => e.br_count(g, s) !== 13) || new Set(all).size !== 52) bad.push(`seed ${seed}: not a whole deck in four thirteens`);
    if (e.br_phase(g) !== 0 || e.br_ncalls(g) !== 0) bad.push(`seed ${seed}: opens in phase ${e.br_phase(g)} with ${e.br_ncalls(g)} calls`);
  }
  ok('every board deals the deck, has pagat\'s vulnerability, and its dealer calls first',
     bad.length === 0, bad.length ? bad.slice(0, 3).join('; ') : '32 boards');
}

// -- THE SCORE TABLE --------------------------------------------------------
// Written from pagat's duplicate table, then held to values any duplicate
// scorer knows.
function dup(level, d, dbl, vul, tricks) {
  const need = level + 6;
  const mult = [1, 2, 4][dbl];
  const trickVal = d <= 1 ? 20 : 30;
  const cp = (d === 4 ? 40 + 30 * (level - 1) : trickVal * level) * mult;
  if (tricks >= need) {
    const over = tricks - need;
    let s = cp;
    s += dbl === 0 ? over * trickVal : over * (vul ? 200 : 100) * (dbl === 2 ? 2 : 1);
    s += cp >= 100 ? (vul ? 500 : 300) : 50;
    if (level === 6) s += vul ? 750 : 500;
    if (level === 7) s += vul ? 1500 : 1000;
    s += [0, 50, 100][dbl];
    return s;
  }
  const u = need - tricks;
  if (dbl === 0) return -u * (vul ? 100 : 50);
  let p = 0;
  for (let k = 1; k <= u; k++) p += vul ? (k === 1 ? 200 : 300) : (k === 1 ? 100 : k <= 3 ? 200 : 300);
  return -p * (dbl === 2 ? 2 : 1);
}
{
  const known = [
    ['1C made', 1, 0, 0, 0, 7, 70], ['2C made', 2, 0, 0, 0, 8, 90], ['1NT made', 1, 4, 0, 0, 7, 90],
    ['3NT made', 3, 4, 0, 0, 9, 400], ['3NT+1 vulnerable', 3, 4, 0, 1, 10, 630],
    ['4S made', 4, 3, 0, 0, 10, 420], ['4S made vulnerable', 4, 3, 0, 1, 10, 620],
    ['4H+1', 4, 2, 0, 0, 11, 450], ['5C made', 5, 0, 0, 0, 11, 400],
    ['6H made', 6, 2, 0, 0, 12, 980], ['7NT made vulnerable', 7, 4, 0, 1, 13, 2220],
    ['1C doubled made', 1, 0, 1, 0, 7, 140], ['1C redoubled made', 1, 0, 2, 0, 7, 230],
    ['2S doubled +1 vulnerable', 2, 3, 1, 1, 9, 870], ['2S doubled made', 2, 3, 1, 0, 8, 470],
    ['4S down 1', 4, 3, 0, 0, 9, -50], ['4S down 2 vulnerable', 4, 3, 0, 1, 8, -200],
    ['3NT doubled down 1', 3, 4, 1, 0, 8, -100], ['down 2', 3, 4, 1, 0, 7, -300],
    ['down 3', 3, 4, 1, 0, 6, -500], ['down 4', 3, 4, 1, 0, 5, -800],
    ['3NT doubled down 1 vulnerable', 3, 4, 1, 1, 8, -200], ['down 2 vulnerable', 3, 4, 1, 1, 7, -500],
    ['down 3 vulnerable', 3, 4, 1, 1, 6, -800],
    ['3NT redoubled down 1', 3, 4, 2, 0, 8, -200], ['redoubled down 3', 3, 4, 2, 0, 6, -1000],
    ['redoubled down 2 vulnerable', 3, 4, 2, 1, 7, -1000],
  ];
  const oracle = known.filter(([, l, d, x, v, t, want]) => dup(l, d, x, v === 1, t) !== want).map(k => k[0]);
  ok('control: this side\'s table gives the values any duplicate scorer knows', oracle.length === 0,
     oracle.length ? oracle.join('; ') : `${known.length} results`);
  const engine = known.filter(([, l, d, x, v, t, want]) => e.br_dupscore(l, d, x, v, t) !== want)
    .map(([n, l, d, x, v, t, want]) => `${n}: ${e.br_dupscore(l, d, x, v, t)}, wanted ${want}`);
  ok('the engine scores every one of them', engine.length === 0,
     engine.length ? engine.slice(0, 3).join('; ') : `${known.length} results`);
  const sweep = [];
  let n = 0;
  for (let l = 1; l <= 7; l++) for (let d = 0; d <= 4; d++) for (let x = 0; x <= 2; x++)
    for (const v of [0, 1]) for (let t = 0; t <= 13; t++) {
      n++;
      if (e.br_dupscore(l, d, x, v, t) !== dup(l, d, x, v === 1, t)) sweep.push(`${l}${'CDHSN'[d]}${['', 'x', 'xx'][x]} v${v} ${t}: ${e.br_dupscore(l, d, x, v, t)}, table ${dup(l, d, x, v === 1, t)}`);
    }
  ok('and every contract, doubling, vulnerability and trick count agrees with the table',
     sweep.length === 0, sweep.length ? sweep.slice(0, 3).join('; ') : `${n} results`);
}

// -- A WHOLE BOARD, EVERY TRANSITION WATCHED --------------------------------
const tally = { calls: 0, doubled: 0, passedOut: 0, notBidder: 0, bySide: [0, 0],
  southDecl: 0, northDecl: 0, revokeAsked: 0, revokeRefused: 0, boards: 0, made: 0, down: 0 };
function legalCall(model, seat, code) {
  if (code === PASS) return true;
  if (code === DBL) return model.last > 0 && model.dbl === 0 && team(model.bidder) !== team(seat);
  if (code === RDBL) return model.last > 0 && model.dbl === 1 && team(model.bidder) === team(seat);
  return code >= 5 && code <= 39 && code > model.last;
}
function board(g0, bad, label, act) {
  let g = g0, guard = 0;
  const model = { last: 0, bidder: -1, dbl: 0, passes: 0, history: [] };
  for (let i = 0; i < e.br_ncalls(g0); i++) {
    const c = e.br_callat(g0, i), seat = Math.floor(c / 100), code = c % 100;
    model.history.push({ seat, code });
    if (code === PASS) model.passes++;
    else { model.passes = 0; if (code >= 5) { model.last = code; model.bidder = seat; model.dbl = 0; } else model.dbl = code; }
  }
  let trick = [], ns = 0, ew = 0, dummySeenEarly = false;
  while (e.br_done(g) === 0 && guard++ < 300) {
    const before = g;
    if (e.br_phase(g) === 0) {
      const seat = e.br_cur(g);
      if (act && e.br_who(g) === 1) g = act(g); else g = e.br_step(g);
      const n = e.br_ncalls(g);
      if (n !== model.history.length + 1) { bad.push(`${label}: ${n} calls after ${model.history.length}`); break; }
      const c = e.br_callat(g, n - 1), cs = Math.floor(c / 100), code = c % 100;
      tally.calls++;
      if (cs !== seat) bad.push(`${label}: seat ${cs} called on seat ${seat}'s turn`);
      if (!legalCall(model, seat, code)) bad.push(`${label}: seat ${seat} made the illegal call ${code} over ${model.last}`);
      model.history.push({ seat, code });
      if (code === PASS) model.passes++;
      else { model.passes = 0; if (code >= 5) { model.last = code; model.bidder = seat; model.dbl = 0; } else model.dbl = code; }
      if (code === DBL) tally.doubled++;
      const over = (model.last === 0 && model.passes === 4) || (model.last > 0 && model.passes === 3);
      if (!over) {
        if (e.br_phase(g) !== 0) bad.push(`${label}: the auction ended after ${model.passes} passes`);
        else if (e.br_cur(g) !== NEXT[seat]) bad.push(`${label}: after seat ${seat} the call went to ${e.br_cur(g)}`);
      } else if (model.last === 0) {
        tally.passedOut++;
        if (e.br_phase(g) !== 2 || e.br_score(g) !== 0 || e.br_decl(g) !== -1) bad.push(`${label}: four passes did not pass the board out`);
      } else {
        const d = model.last % 5;
        const decl = model.history.find(x => x.code >= 5 && x.code % 5 === d && team(x.seat) === team(model.bidder)).seat;
        if (decl !== model.bidder) tally.notBidder++;
        if (e.br_phase(g) !== 1) bad.push(`${label}: three passes after a bid did not end the auction`);
        if (e.br_decl(g) !== decl) bad.push(`${label}: declarer ${e.br_decl(g)}, first to name the denomination ${decl}`);
        if (e.br_level(g) !== Math.floor(model.last / 5) || e.br_strain(g) !== d || e.br_dbl(g) !== model.dbl) bad.push(`${label}: contract ${e.br_level(g)}/${e.br_strain(g)}/${e.br_dbl(g)}`);
        if (e.br_trump(g) !== suitOfStrain(d)) bad.push(`${label}: trump ${e.br_trump(g)} for denomination ${d}`);
        if (e.br_cur(g) !== NEXT[decl] || e.br_leader(g) !== NEXT[decl]) bad.push(`${label}: seat ${e.br_cur(g)} leads, the declarer's left is ${NEXT[decl]}`);
        tally.bySide[team(decl)]++;
        if (decl === 1) tally.southDecl++;
        if (decl === 0) tally.northDecl++;
        if (partner(decl) !== 1 && e.br_shown(g, partner(decl), 0) !== -1) dummySeenEarly = true;
      }
    } else {
      const seat = e.br_cur(g), decl = e.br_decl(g), dummy = partner(decl);
      const who = seat === dummy ? decl : seat;
      if (e.br_who(g) !== who) bad.push(`${label}: seat ${e.br_who(g)} decides for seat ${seat}, the rules say ${who}`);
      for (const s of SEATS) {
        const shownAny = e.br_shown(g, s, 0) !== -1;
        const leadOut = e.br_tricks(g) > 0 || trick.length > 0;
        const should = s === 1 || (s === dummy && leadOut);
        if (shownAny !== should && e.br_count(g, s) > 0) bad.push(`${label}: seat ${s} shown ${shownAny} to South, the rules say ${should}`);
      }
      const mine = hand(g, seat);
      if (trick.length) {
        const led = e.br_suit(trick[0].c);
        const wrong = mine.findIndex(c => e.br_suit(c) !== led);
        if (mine.some(c => e.br_suit(c) === led) && wrong >= 0) {
          tally.revokeAsked++;
          if (e.br_legal(g, wrong) === 0 && e.br_play(g, wrong) === g) tally.revokeRefused++;
        }
      }
      g = act && who === 1 ? act(g) : e.br_step(g);
      const gone = mine.filter(c => !hand(g, seat).includes(c));
      if (gone.length !== 1) { bad.push(`${label}: seat ${seat} lost ${gone.length} cards`); break; }
      trick.push({ seat, c: gone[0] });
      if (trick.length === 4) {
        const t = e.br_trump(g), led = e.br_suit(trick[0].c);
        let w = trick[0];
        for (const x of trick) {
          const xs = e.br_suit(x.c), ws = e.br_suit(w.c);
          if ((xs === t && ws !== t) || (xs === ws && e.br_rank(x.c) > e.br_rank(w.c))) w = x;
        }
        if (team(w.seat) === 0) ns++; else ew++;
        if (e.br_done(g) === 0 && e.br_cur(g) !== w.seat) bad.push(`${label}: seat ${e.br_cur(g)} leads, seat ${w.seat} won the trick`);
        trick = [];
      }
    }
    if (g === before) { bad.push(`${label}: nothing moved`); break; }
  }
  if (dummySeenEarly) bad.push(`${label}: the dummy was shown before the opening lead`);
  if (e.br_done(g) !== 1) { bad.push(`${label}: never finished`); return g; }
  tally.boards++;
  if (e.br_decl(g) >= 0) {
    if (e.br_nstricks(g) !== ns || e.br_ewtricks(g) !== ew) bad.push(`${label}: engine ${e.br_nstricks(g)}/${e.br_ewtricks(g)}, by identity ${ns}/${ew}`);
    const decl = e.br_decl(g), tm = team(decl);
    const got = tm === 0 ? ns : ew;
    const s = dup(Math.floor(model.last / 5), model.last % 5, model.dbl, vulFor(e.br_vul(g), tm), got);
    if (s >= 0) tally.made++; else tally.down++;
    const want = tm === 0 ? s : -s;
    if (e.br_score(g) !== want) bad.push(`${label}: north-south scored ${e.br_score(g)}, the table says ${want}`);
  }
  return g;
}

{
  const bad = [];
  for (let seed = 1; seed <= 120; seed++) board(e.br_new(seed), bad, `board seed ${seed}`, null);
  ok('CONTROL: the auctions passed boards out and gave the contract to each side',
     tally.passedOut > 0 && tally.bySide[0] > 0 && tally.bySide[1] > 0,
     `${tally.passedOut} passed out, ${tally.doubled} doubles, NS ${tally.bySide[0]} EW ${tally.bySide[1]}`);
  ok('CONTROL: a declarer was not the last bidder, so the first-to-name rule decided it', tally.notBidder > 0,
     `${tally.notBidder} boards`);
  ok('CONTROL: South declared, and North declared with South as dummy', tally.southDecl > 0 && tally.northDecl > 0,
     `South ${tally.southDecl}, North ${tally.northDecl}`);
  ok('CONTROL: contracts made and went down', tally.made > 0 && tally.down > 0, `${tally.made} made, ${tally.down} down`);
  ok('CONTROL: a revoke was offered and refused every time', tally.revokeAsked > 0 && tally.revokeRefused === tally.revokeAsked,
     `${tally.revokeRefused} of ${tally.revokeAsked}`);
  ok('every call is legal and in turn, the auction ends and names the declarer by pagat, the dummy and the play follow, and the board scores by the table',
     bad.length === 0, bad.length ? bad.slice(0, 3).join('; ') : `${tally.boards} boards, ${tally.calls} calls`);
}

// -- YOUR SEAT --------------------------------------------------------------
{
  const bad = [];
  let tried = 0, doubled = 0, redoubled = 0, played = 0;
  for (let seed = 1; seed <= 60; seed++) {
    let g = e.br_new(seed), guard = 0;
    while (e.br_phase(g) === 0 && e.br_who(g) !== 1 && guard++ < 20) g = e.br_step(g);
    if (e.br_phase(g) !== 0) continue;
    tried++;
    const last = e.br_lastbid(g);
    if (last > 0 && e.br_call(g, last) !== g) bad.push(`seed ${seed}: a bid equal to the last was taken`);
    if (e.br_call(g, 40) !== g || e.br_call(g, 3) !== g || e.br_call(g, 4) !== g) bad.push(`seed ${seed}: a call off the list was taken`);
    const canDbl = last > 0 && e.br_dbl(g) === 0 && team(e.br_bidder(g)) !== 0;
    if ((e.br_cancall(g, DBL) === 1) !== canDbl) bad.push(`seed ${seed}: double offered ${e.br_cancall(g, DBL)}, the rules say ${canDbl}`);
    if (e.br_cancall(g, RDBL) === 1) bad.push(`seed ${seed}: a redouble with nothing doubled`);
    if (canDbl) {
      const x = e.br_call(g, DBL);
      doubled++;
      if (e.br_dbl(x) !== 1 || e.br_callat(x, e.br_ncalls(x) - 1) !== 101) bad.push(`seed ${seed}: the double went in as ${e.br_callat(x, e.br_ncalls(x) - 1)}`);
    }
    // Bid a seven-level contract and play the board from the South seat,
    // taking the first legal card whenever South decides.
    const b = e.br_call(g, bid(7, 4));
    if (e.br_lastbid(b) !== 39 || e.br_bidder(b) !== 1) { bad.push(`seed ${seed}: 7NT went in as ${e.br_lastbid(b)}`); continue; }
    const act = h => {
      if (e.br_phase(h) === 0) {
        if (e.br_cancall(h, RDBL) === 1) { redoubled++; return e.br_call(h, RDBL); }
        return e.br_call(h, PASS);
      }
      const i = [...Array(13).keys()].find(k => e.br_legal(h, k) === 1);
      played++;
      return e.br_play(h, i);
    };
    board(b, bad, `your seat ${seed}`, act);
  }
  ok('CONTROL: you called and doubled, you redoubled when doubled, and you played cards',
     tried > 0 && doubled > 0 && redoubled > 0 && played > 0,
     `${tried} auctions, ${doubled} doubles, ${redoubled} redoubles, ${played} cards`);
  ok('your calls take only what the rules allow, and a whole board plays from your seat',
     bad.length === 0, bad.length ? bad.slice(0, 3).join('; ') : `${tried} boards`);
}

// -- A HANDLE A CALLER IS HOLDING DOES NOT MOVE (GAME-13) -------------------
{
  let g = e.br_new(3), guard = 0;
  while (e.br_phase(g) !== 1 && guard++ < 60) g = e.br_step(g);
  const before = SEATS.map(s => hand(g, s)).join('|');
  const idx = [...Array(13).keys()].find(i => e.br_legal(g, i) === 1);
  const after = e.br_play(g, idx);
  ok('GAME-13: playing a card leaves the handle you held alone',
     e.br_phase(g) === 1 && SEATS.map(s => hand(g, s)).join('|') === before && after !== g);
  const done = (() => { let h = e.br_new(3), k = 0; while (e.br_done(h) === 0 && k++ < 300) h = e.br_step(h); return h; })();
  ok('a finished board refuses every move with the same handle',
     e.br_step(done) === done && e.br_play(done, 0) === done && e.br_call(done, PASS) === done);
}

// -- The session runner is one board of the same table ----------------------
{
  const bad = [];
  for (let seed = 1; seed <= 30; seed++) {
    let g = e.br_new(seed), guard = 0;
    while (e.br_done(g) === 0 && guard++ < 300) g = e.br_step(g);
    const r = e.br_run(seed);
    const decl = e.br_decl(g);
    const wantTeam = decl < 0 ? -1 : team(decl);
    const wantScore = decl < 0 ? 0 : (wantTeam === 0 ? e.br_score(g) : -e.br_score(g));
    if (e.br_rteam(r) !== wantTeam || e.br_rlevel(r) !== e.br_level(g) || e.br_rscore(r) !== wantScore
        || (decl >= 0 && e.br_rtricks(r) !== (wantTeam === 0 ? e.br_nstricks(g) : e.br_ewtricks(g)))) {
      bad.push(`seed ${seed}: runner ${e.br_rteam(r)}/${e.br_rlevel(r)}/${e.br_rtricks(r)}/${e.br_rscore(r)}`);
    }
  }
  ok('the session runner scores one board the way the table does', bad.length === 0,
     bad.length ? bad.slice(0, 3).join('; ') : '30 boards');
}

// -- Refusals and controls ------------------------------------------------
{
  const h = e.br_new(11);
  ok('a seat or card off the table is refused',
     e.br_card(h, 4, 0) === -1 && e.br_card(h, -1, 0) === -1 && e.br_count(h, 9) === 0
     && e.br_card(h, 0, 13) === -1 && e.br_suit(-1) === -1 && e.br_card_hcp(-1) === -1);
  ok('control: the same seed deals the same board, different seeds different ones',
     hand(e.br_new(8), 0).join() === hand(e.br_new(8), 0).join()
     && hand(e.br_new(1), 0).join() !== hand(e.br_new(2), 0).join());
}

console.log(fail === 0
  ? `\nPASS: Bridge bids, plays and scores a duplicate board by pagat (${pass} arms).`
  : `\nFAIL: ${fail} of ${pass + fail} arms.`);
process.exitCode = fail === 0 ? 0 : 1;
