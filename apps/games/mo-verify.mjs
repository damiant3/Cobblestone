// Grade the Monopoly wasm module against the rules.
//
// The oracle below is a second implementation of the rules, written from
// "The Rules" in apps/games/classic/Monopoly.codex (Hasbro's US leaflet,
// 2008 amounts, and the choices the engine states where the rules leave
// one), not from the engine's code. It runs in lockstep with the engine: the
// same action goes to both, and after every action the whole observable
// state must agree -- squares, cash, Jail, cards, bankruptcy, every deed's
// owner, buildings and mortgage, the Bank's houses and hotels, the phase,
// whose choice it is, the offer, the auction, the debt queue and both
// decks. Before every action the LEGAL SET must agree too: every player,
// every action kind, every deed and a spread of bid amounts, allowed and
// refused (L-BOTHARMS), and an action the rules refuse must come back as
// the same handle.
//
// What the oracle takes from the engine, because none of it is a rule: the
// dice of a throw, the order the decks were shuffled into, the fresh throw a
// utility card calls for, and the engine players' choices, including the
// terms of a trade they propose (the trade itself is checked against Rule
// 12). The dice are graded separately, for the doubles the rules need.
//
// What a PASS does not mean: that the engine's players play WELL, or that
// the page draws the state; the oracle and the engine share one reading of
// the leaflet, so a rule both read the same wrong way passes (L-ORACLE).
//
// Usage: node apps/games/mo-verify.mjs [path/to/monopoly.wasm]

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import { gameInstance } from '../landing/web/games/arcade.js';

const here = dirname(fileURLToPath(import.meta.url));
const wasmPath = process.argv[2] ||
  join(here, '..', 'landing', 'web', 'games', 'monopoly.wasm');

const e = gameInstance(
  new WebAssembly.Module(readFileSync(wasmPath))).exports;

let pass = 0, fail = 0;
const ok = (name, cond, detail) => {
  if (cond) { console.log(`  ok    ${name}${detail !== undefined ? ': ' + detail : ''}`); pass++; }
  else { console.log(`  FAIL  ${name}${detail !== undefined ? ': ' + detail : ''}`); fail++; }
};
console.log(`mo-verify ${wasmPath}`);

// A seeded generator for the grader's own choices, high bits only.
function makeRand(seed) {
  let x = BigInt(seed) * 0x9E3779B97F4A7C15n + 0x632BE59BD9B4E019n;
  return () => {
    x = (x * 6364136223846793005n + 1442695040888963407n) & 0xFFFFFFFFFFFFFFFFn;
    return Number(x >> 33n) / 2147483648;
  };
}

// -- The board, from the deeds -------------------------------------------
// square, price, rent unimproved and with 1-4 houses and a hotel, house
// price, colour.
const BOARD = [
  [1, 60, [2, 10, 30, 90, 160, 250], 50, 'brown'],
  [3, 60, [4, 20, 60, 180, 320, 450], 50, 'brown'],
  [5, 200, null, 0, 'rail'],
  [6, 100, [6, 30, 90, 270, 400, 550], 50, 'light blue'],
  [8, 100, [6, 30, 90, 270, 400, 550], 50, 'light blue'],
  [9, 120, [8, 40, 100, 300, 450, 600], 50, 'light blue'],
  [11, 140, [10, 50, 150, 450, 625, 750], 100, 'pink'],
  [12, 150, null, 0, 'utility'],
  [13, 140, [10, 50, 150, 450, 625, 750], 100, 'pink'],
  [14, 160, [12, 60, 180, 500, 700, 900], 100, 'pink'],
  [15, 200, null, 0, 'rail'],
  [16, 180, [14, 70, 200, 550, 750, 950], 100, 'orange'],
  [18, 180, [14, 70, 200, 550, 750, 950], 100, 'orange'],
  [19, 200, [16, 80, 220, 600, 800, 1000], 100, 'orange'],
  [21, 220, [18, 90, 250, 700, 875, 1050], 150, 'red'],
  [23, 220, [18, 90, 250, 700, 875, 1050], 150, 'red'],
  [24, 240, [20, 100, 300, 750, 925, 1100], 150, 'red'],
  [25, 200, null, 0, 'rail'],
  [26, 260, [22, 110, 330, 800, 975, 1150], 150, 'yellow'],
  [27, 260, [22, 110, 330, 800, 975, 1150], 150, 'yellow'],
  [28, 150, null, 0, 'utility'],
  [29, 280, [24, 120, 360, 850, 1025, 1200], 150, 'yellow'],
  [31, 300, [26, 130, 390, 900, 1100, 1275], 200, 'green'],
  [32, 300, [26, 130, 390, 900, 1100, 1275], 200, 'green'],
  [34, 320, [28, 150, 450, 1000, 1200, 1400], 200, 'green'],
  [35, 200, null, 0, 'rail'],
  [37, 350, [35, 175, 500, 1100, 1300, 1500], 200, 'dark blue'],
  [39, 400, [50, 200, 600, 1400, 1700, 2000], 200, 'dark blue'],
];
const N = BOARD.length;
const deedAt = sq => BOARD.findIndex(d => d[0] === sq);
const colour = i => BOARD[i][4];
const street = i => BOARD[i][2] !== null;
const groupOf = i => BOARD.map((d, j) => j).filter(j => colour(j) === colour(i));
const mortgageValue = i => BOARD[i][1] / 2;
// Integer arithmetic: 50 * 1.1 is 55.000000000000007 in floating point.
const liftCost = i => Math.ceil(mortgageValue(i) * 11 / 10);
const interest = i => Math.ceil(mortgageValue(i) / 10);
const CHANCE = [7, 22, 36], CHEST = [2, 17, 33];
const GOOJF = [8, 4];
const KIND = { roll: 0, buy: 1, decline: 2, bid: 3, drop: 4, build: 5, sell: 6, mortgage: 7, lift: 8,
  fine: 9, card: 10, bankrupt: 11, accept: 12, refuse: 13, trade: 14 };

// -- The oracle ----------------------------------------------------------
const seen = new Map();
const saw = k => seen.set(k, (seen.get(k) || 0) + 1);

function newOracle(np, decks, human) {
  return {
    np, human, cur: 0, turn: 0, over: false, winner: -1, phase: 0, pending: -1,
    doubles: 0, again: false, move: 0, houses: 32, hotels: 12, card: -1,
    auction: null, offer: null, debts: [], aq: [], decks,
    players: [...Array(np)].map(() => ({ pos: 0, cash: 1500, jail: false, jt: 0, cards: [0, 0], out: false })),
    deeds: BOARD.map(() => ({ owner: -1, h: 0, m: false })),
  };
}

const live = (o, p) => !o.over && p >= 0 && p < o.np && !o.players[p].out;
const liveCount = o => o.players.filter(p => !p.out).length;
const holdsGroup = (o, p, i) => groupOf(i).every(j => o.deeds[j].owner === p);
const actorOf = o => {
  if (o.over) return -1;
  if (o.phase <= 1) return o.cur;
  if (o.phase === 2) return o.deeds[o.offer.want].owner;
  if (o.phase === 3) return o.auction.act;
  if (o.phase === 4) return o.debts[0][0];
  return -1;
};
const raisable = (o, p) => o.deeds.reduce((a, d, i) =>
  d.owner === p ? a + d.h * BOARD[i][3] / 2 + (d.m ? 0 : mortgageValue(i)) : a, 0);

function canBuild(o, p, i) {
  if (!(i >= 0 && i < N) || !street(i)) return false;
  const d = o.deeds[i];
  if (d.owner !== p || !holdsGroup(o, p, i)) return false;
  if (groupOf(i).some(j => o.deeds[j].m)) return false;
  if (d.h >= 5) return false;
  if (d.h > Math.min(...groupOf(i).map(j => o.deeds[j].h))) return false;
  if (d.h === 4 ? o.hotels < 1 : o.houses < 1) return false;
  return o.players[p].cash >= BOARD[i][3];
}
function canSell(o, p, i) {
  if (!(i >= 0 && i < N)) return false;
  const d = o.deeds[i];
  return d.owner === p && d.h >= 1 && d.h === Math.max(...groupOf(i).map(j => o.deeds[j].h));
}
function canMortgage(o, p, i) {
  if (!(i >= 0 && i < N)) return false;
  const d = o.deeds[i];
  return d.owner === p && !d.m && groupOf(i).every(j => o.deeds[j].h === 0);
}
function canLift(o, p, i) {
  if (!(i >= 0 && i < N)) return false;
  const d = o.deeds[i];
  return d.owner === p && d.m && o.players[p].cash >= liftCost(i);
}

function legal(o, p, kind, arg) {
  if (!live(o, p)) return false;
  const ph = o.phase, mine = p === o.cur, pl = o.players[p];
  const assets = ph === 0 || ph === 1 || ph === 4;
  switch (kind) {
    case KIND.roll: return ph === 0 && mine;
    case KIND.buy: return ph === 1 && mine && pl.cash >= BOARD[o.pending][1];
    case KIND.decline: return ph === 1 && mine;
    case KIND.bid: return ph === 3 && p === o.auction.act && arg > o.auction.high && arg <= pl.cash;
    case KIND.drop: return ph === 3 && p === o.auction.act;
    case KIND.build: return assets && canBuild(o, p, arg);
    case KIND.sell: return assets && canSell(o, p, arg);
    case KIND.mortgage: return assets && canMortgage(o, p, arg);
    case KIND.lift: return assets && canLift(o, p, arg);
    case KIND.fine: return ph === 0 && mine && pl.jail && pl.jt < 2 && pl.cash >= 50;
    case KIND.card: return ph === 0 && mine && pl.jail && pl.cards[0] + pl.cards[1] > 0;
    case KIND.bankrupt: return ph === 4 && p === o.debts[0][0] && pl.cash + raisable(o, p) < o.debts[0][2];
    case KIND.accept: case KIND.refuse: return ph === 2 && p === actorOf(o);
    default: return false;
  }
}

const addCash = (o, p, n) => { if (p >= 0) o.players[p].cash += n; };
const owe = (o, d, c, a) => { if (a > 0) o.debts.push([d, c, a]); };

function toJail(o, p) {
  const pl = o.players[p];
  pl.pos = 10; pl.jail = true; pl.jt = 0;
  o.again = false; o.doubles = 0;
}
function release(o, p) { o.players[p].jail = false; o.players[p].jt = 0; }

function rent(o, i, mode, eng) {
  const d = o.deeds[i], held = groupOf(i).filter(j => o.deeds[j].owner === d.owner).length;
  if (colour(i) === 'rail') {
    saw(`rent: ${held} railroad${held > 1 ? 's' : ''}${mode === 1 ? ', card double' : ''}`);
    return [25, 50, 100, 200][held - 1] * (mode === 1 ? 2 : 1);
  }
  if (colour(i) === 'utility') {
    if (mode === 2) { saw('rent: card utility, ten times a fresh throw'); return 10 * eng.cthrow(); }
    saw(`rent: utility x${held === 2 ? 10 : 4}`);
    return (held === 2 ? 10 : 4) * o.roll;
  }
  if (d.h > 0) { saw(d.h === 5 ? 'rent: hotel' : `rent: ${d.h} house${d.h > 1 ? 's' : ''}`); return BOARD[i][2][d.h]; }
  if (holdsGroup(o, d.owner, i)) {
    saw(groupOf(i).some(j => o.deeds[j].m) ? 'rent: doubled beside a mortgaged lot' : 'rent: doubled, whole group');
    return 2 * BOARD[i][2][0];
  }
  saw('rent: lot');
  return BOARD[i][2][0];
}

function land(o, p, sq, mode, eng) {
  if (sq === 30) { saw('jail: the square'); return toJail(o, p); }
  if (sq === 4) { saw('income tax'); return owe(o, p, -1, 200); }
  if (sq === 38) { saw('luxury tax'); return owe(o, p, -1, 100); }
  if (CHANCE.includes(sq)) return draw(o, p, 0, eng);
  if (CHEST.includes(sq)) return draw(o, p, 1, eng);
  const i = deedAt(sq);
  if (i < 0) return;
  const d = o.deeds[i];
  if (d.owner < 0) { o.pending = i; o.phase = 1; return; }
  if (d.owner === p) return;
  if (d.m) { saw('rent: none on a mortgaged deed'); return; }
  owe(o, p, d.owner, rent(o, i, mode, eng));
}

function moveBy(o, p, n, eng) {
  const pl = o.players[p], dest = (pl.pos + n) % 40;
  if (pl.pos + n >= 40) { addCash(o, p, 200); saw('GO: passed or landed on'); }
  pl.pos = dest;
  land(o, p, dest, 0, eng);
}
function advanceTo(o, p, dest, mode, eng) {
  if (dest < o.players[p].pos) { addCash(o, p, 200); saw('GO: passed by a card'); }
  o.players[p].pos = dest;
  land(o, p, dest, mode, eng);
}
const nearest = (from, list) => list.find(s => s > from) ?? list[0];

function draw(o, p, deck, eng) {
  const card = o.decks[deck].shift();
  o.card = deck * 16 + card;
  saw(`${deck === 0 ? 'chance' : 'chest'} card ${card}`);
  if (card === GOOJF[deck]) { o.players[p].cards[deck] = 1; return; }
  o.decks[deck].push(card);
  const pl = o.players[p];
  const repairs = (house, hotel) => o.deeds.reduce((a, d) =>
    d.owner === p ? a + (d.h === 5 ? hotel : d.h * house) : a, 0);
  const others = () => [...Array(o.np - 1)].map((_, k) => (p + 1 + k) % o.np).filter(q => !o.players[q].out);
  if (deck === 0) {
    switch (card) {
      case 0: return advanceTo(o, p, 39, 0, eng);
      case 1: return advanceTo(o, p, 0, 0, eng);
      case 2: return advanceTo(o, p, 24, 0, eng);
      case 3: return advanceTo(o, p, 11, 0, eng);
      case 4: case 5: return advanceTo(o, p, nearest(pl.pos, [5, 15, 25, 35]), 1, eng);
      case 6: return advanceTo(o, p, nearest(pl.pos, [12, 28]), 2, eng);
      case 7: return addCash(o, p, 50);
      case 9: pl.pos -= 3; return land(o, p, pl.pos, 0, eng);
      case 10: saw('jail: a card'); return toJail(o, p);
      case 11: return owe(o, p, -1, repairs(25, 100));
      case 12: return owe(o, p, -1, 15);
      case 13: return advanceTo(o, p, 5, 0, eng);
      case 14: for (const q of others()) owe(o, p, q, 50); return;
      case 15: return addCash(o, p, 150);
    }
  } else {
    switch (card) {
      case 0: return advanceTo(o, p, 0, 0, eng);
      case 1: return addCash(o, p, 200);
      case 2: return owe(o, p, -1, 50);
      case 3: return addCash(o, p, 50);
      case 5: saw('jail: a card'); return toJail(o, p);
      case 6: return addCash(o, p, 100);
      case 7: return addCash(o, p, 20);
      case 8: for (const q of others()) owe(o, q, p, 10); return;
      case 9: return addCash(o, p, 100);
      case 10: return owe(o, p, -1, 100);
      case 11: return owe(o, p, -1, 50);
      case 12: return addCash(o, p, 25);
      case 13: return owe(o, p, -1, repairs(40, 115));
      case 14: return addCash(o, p, 10);
      case 15: return addCash(o, p, 100);
    }
  }
}

function nextLive(o, p) {
  for (let k = 1; k <= o.np; k++) { const q = (p + k) % o.np; if (!o.players[q].out) return q; }
  return p;
}
function endTurn(o) {
  o.cur = nextLive(o, o.cur); o.turn++; o.doubles = 0; o.again = false;
  o.pending = -1; o.move = 0; o.phase = 0;
}
function carryOn(o, eng) {
  if (o.over) return;
  if (o.debts.length) return settle(o, eng);
  if (o.aq.length) { saw('auction: a bankrupt\'s deed'); return startAuction(o, o.aq.shift()); }
  if (o.players[o.cur].out) return endTurn(o);
  if (o.move > 0) {
    const m = o.move; o.move = 0; o.phase = 0;
    moveBy(o, o.cur, m, eng);
    return after(o, eng);
  }
  if (o.again) { o.again = false; o.phase = 0; saw('doubles: throw again'); return; }
  endTurn(o);
}
const after = (o, eng) => { if (o.phase !== 1) carryOn(o, eng); };
function settle(o, eng) {
  const [d, c, a] = o.debts[0];
  if (o.players[d].out || (c >= 0 && o.players[c].out)) { o.debts.shift(); return carryOn(o, eng); }
  if (o.players[d].cash >= a) {
    addCash(o, d, -a); addCash(o, c, a); o.debts.shift();
    return carryOn(o, eng);
  }
  saw('debt: short of cash');
  o.phase = 4;
}

function startAuction(o, i) {
  o.pending = -1;
  const active = [...Array(o.np)].map((_, p) => !o.players[p].out);
  o.auction = { prop: i, high: 0, lead: -1, active, act: -1 };
  o.auction.act = active[o.cur] ? o.cur : nextBidder(o, o.cur);
  o.phase = 3;
}
function nextBidder(o, p) {
  for (let k = 1; k <= o.np; k++) { const q = (p + k) % o.np; if (o.auction.active[q]) return q; }
  return -1;
}
function endAuction(o, eng) {
  const a = o.auction;
  if (a.lead >= 0) { addCash(o, a.lead, -a.high); o.deeds[a.prop].owner = a.lead; saw('auction: sold'); }
  else saw('auction: nobody bid');
  o.auction = null; o.phase = 0;
  carryOn(o, eng);
}

function strip(o, p, i) {
  const d = o.deeds[i];
  if (d.h === 5) o.hotels++; else o.houses += d.h;
  addCash(o, p, d.h * BOARD[i][3] / 2);
  d.h = 0;
}
function bankrupt(o, p, eng) {
  const c = o.debts[0][1];
  o.debts = o.debts.filter(([d, cr]) => d !== p && cr !== p);
  const pl = o.players[p];
  if (c >= 0) {
    saw('bankrupt: to a player');
    for (let i = 0; i < N; i++) {
      if (o.deeds[i].owner !== p) continue;
      strip(o, p, i);
      o.deeds[i].owner = c;
      if (o.deeds[i].m) { owe(o, c, -1, interest(i)); saw('bankrupt: interest on a mortgaged deed'); }
    }
    addCash(o, c, pl.cash);
    o.players[c].cards[0] += pl.cards[0]; o.players[c].cards[1] += pl.cards[1];
  } else {
    saw('bankrupt: to the Bank');
    for (let i = 0; i < N; i++) {
      if (o.deeds[i].owner !== p) continue;
      strip(o, p, i);
      o.deeds[i].owner = -1; o.deeds[i].m = false;
      o.aq.push(i);
    }
    if (pl.cards[0]) o.decks[0].push(GOOJF[0]);
    if (pl.cards[1]) o.decks[1].push(GOOJF[1]);
  }
  pl.cards = [0, 0]; pl.out = true; pl.cash = 0;
  if (p === o.cur) { o.move = 0; o.again = false; }
  if (liveCount(o) <= 1) {
    o.over = true; o.winner = o.players.findIndex(q => !q.out);
    o.debts = []; o.aq = []; o.move = 0; o.phase = 5;
    saw('game: won by the last player standing');
  }
}

function swap(o, p, q, want, give, price) {
  o.deeds[want].owner = p;
  if (give >= 0) o.deeds[give].owner = q;
  else { addCash(o, p, -price); addCash(o, q, price); }
}

// Apply one action the engine accepted. `eng` reads what the engine alone
// decides: dice, a card's fresh throw, a trade's terms.
function apply(o, p, kind, arg, eng) {
  const pl = o.players[p];
  switch (kind) {
    case KIND.roll: {
      const [d1, d2] = eng.dice();
      o.roll = d1 + d2;
      if (pl.jail) {
        o.again = false;
        if (d1 === d2) { saw('jail: out on doubles'); release(o, p); moveBy(o, p, d1 + d2, eng); return after(o, eng); }
        pl.jt++;
        if (pl.jt >= 3) { saw('jail: third failure pays and moves'); release(o, p); owe(o, p, -1, 50); o.move = d1 + d2; return carryOn(o, eng); }
        return carryOn(o, eng);
      }
      if (d1 === d2) {
        const k = o.doubles + 1;
        if (k >= 3) { saw('jail: third doubles'); toJail(o, p); return carryOn(o, eng); }
        o.doubles = k; o.again = true;
        moveBy(o, p, d1 + d2, eng); return after(o, eng);
      }
      o.again = false;
      moveBy(o, p, d1 + d2, eng); return after(o, eng);
    }
    case KIND.buy:
      addCash(o, p, -BOARD[o.pending][1]); o.deeds[o.pending].owner = p; saw('bought');
      o.pending = -1; o.phase = 0; return carryOn(o, eng);
    case KIND.decline: saw('declined, so auctioned'); return startAuction(o, o.pending);
    case KIND.bid: {
      const a = o.auction; a.high = arg; a.lead = p;
      const next = nextBidder(o, p);
      if (next === p) return endAuction(o, eng);
      a.act = next; return;
    }
    case KIND.drop: {
      const a = o.auction; a.active[p] = false;
      const n = a.active.filter(Boolean).length, next = nextBidder(o, p);
      if (n === 0 || (n === 1 && next === a.lead)) return endAuction(o, eng);
      a.act = next; return;
    }
    case KIND.build: {
      const d = o.deeds[arg];
      if (d.h === 4) { o.hotels--; o.houses += 4; saw('built: a hotel'); } else { o.houses--; saw('built: a house'); }
      addCash(o, p, -BOARD[arg][3]); d.h++;
      break;
    }
    case KIND.sell: {
      const d = o.deeds[arg], half = BOARD[arg][3] / 2;
      if (d.h === 5) {
        const got = Math.min(4, o.houses);
        saw(got < 4 ? 'sold: a hotel, the Bank short of houses' : 'sold: a hotel');
        o.hotels++; o.houses -= got; addCash(o, p, half * (5 - got)); d.h = got;
      } else { o.houses++; addCash(o, p, half); d.h--; saw('sold: a house'); }
      break;
    }
    case KIND.mortgage: o.deeds[arg].m = true; addCash(o, p, mortgageValue(arg)); saw('mortgaged'); break;
    case KIND.lift: o.deeds[arg].m = false; addCash(o, p, -liftCost(arg)); saw('lifted'); break;
    case KIND.fine: addCash(o, p, -50); release(o, p); saw('jail: paid the fine'); return;
    case KIND.card: {
      const d = pl.cards[0] > 0 ? 0 : 1;
      pl.cards[d] = 0; o.decks[d].push(GOOJF[d]); release(o, p); saw('jail: used a card'); return;
    }
    case KIND.bankrupt: bankrupt(o, p, eng); return carryOn(o, eng);
    case KIND.accept: {
      const f = o.offer; saw('offer: accepted');
      swap(o, o.cur, o.deeds[f.want].owner, f.want, f.give, f.price);
      o.offer = null; o.phase = 0; return;
    }
    case KIND.refuse: saw('offer: refused'); o.offer = null; o.phase = 0; return;
    case KIND.trade: {
      const [give, price] = eng.quote();
      const q = o.deeds[arg].owner;
      o.lastTrade = { p, q, want: arg, give, price };
      if (q === o.human) { o.offer = { want: arg, give, price }; o.phase = 2; saw('offer: made to the person'); return; }
      saw(give >= 0 ? 'trade: a swap' : 'trade: a sale');
      swap(o, p, q, arg, give, price);
      return;
    }
  }
  if (o.phase === 4) carryOn(o, eng);
}

// -- Reading the engine --------------------------------------------------
function snapshot(h, np) {
  const s = {
    phase: e.mo_phase(h), cur: e.mo_cur(h), turn: e.mo_turn(h), done: e.mo_done(h),
    actor: e.mo_actor(h), houses: e.mo_bhouses(h), hotels: e.mo_bhotels(h),
    offered: e.mo_offered(h), card: e.mo_card(h),
    players: [...Array(np)].map((_, p) => [e.mo_pos(h, p), e.mo_cash(h, p), e.mo_jail(h, p),
      e.mo_jturns(h, p), e.mo_cards(h, p, 0), e.mo_cards(h, p, 1), e.mo_out(h, p)].join(',')),
    deeds: [...Array(N)].map((_, i) => [e.mo_owner(h, i), e.mo_houses(h, i), e.mo_mort(h, i)].join(',')),
    debts: [...Array(e.mo_debts(h))].map((_, k) => [0, 1, 2].map(f => e.mo_debt(h, k, f)).join(',')),
    decks: [0, 1].map(d => [...Array(e.mo_decklen(h, d))].map((_, k) => e.mo_deck(h, d, k)).join(',')),
  };
  if (s.done) s.winner = e.mo_winner(h);
  if (s.phase === 3) s.auction = [e.mo_adeed(h), e.mo_ahigh(h), e.mo_alead(h)].join(',');
  if (s.phase === 2) s.offer = [e.mo_twant(h), e.mo_tgive(h), e.mo_tprice(h)].join(',');
  return s;
}
function oracleView(o) {
  const s = {
    phase: o.phase, cur: o.cur, turn: o.turn, done: o.over ? 1 : 0,
    actor: actorOf(o), houses: o.houses, hotels: o.hotels,
    offered: o.phase === 1 ? o.pending : -1, card: o.card,
    players: o.players.map(p => [p.pos, p.cash, p.jail ? 1 : 0, p.jt, p.cards[0], p.cards[1], p.out ? 1 : 0].join(',')),
    deeds: o.deeds.map(d => [d.owner, d.h, d.m ? 1 : 0].join(',')),
    debts: o.debts.map(d => d.join(',')),
    decks: o.decks.map(d => d.join(',')),
  };
  if (o.over) s.winner = o.winner;
  if (o.phase === 3) s.auction = [o.auction.prop, o.auction.high, o.auction.lead].join(',');
  if (o.phase === 2) s.offer = [o.offer.want, o.offer.give, o.offer.price].join(',');
  return s;
}
function differ(a, b) {
  for (const k of new Set([...Object.keys(a), ...Object.keys(b)])) {
    const x = JSON.stringify(a[k]), y = JSON.stringify(b[k]);
    if (x !== y) {
      if (Array.isArray(a[k]) && Array.isArray(b[k])) {
        const i = a[k].findIndex((v, j) => v !== b[k][j]);
        return `${k}[${i}]: engine ${a[k][i]}, rules ${b[k][i]}`;
      }
      return `${k}: engine ${x}, rules ${y}`;
    }
  }
  return null;
}
const decode = c => ({ kind: c % 16, p: Math.floor(c / 16) % 4, arg: Math.floor(c / 64) });

// Every (player, kind, arg) the legal-set arm asks, for one position.
function probes(o, rand) {
  const out = [];
  for (let p = -1; p <= o.np; p++) {
    for (const kind of [0, 1, 2, 4, 9, 10, 11, 12, 13]) out.push([p, kind, 0]);
    for (const kind of [5, 6, 7, 8]) for (let i = -1; i <= N; i++) out.push([p, kind, i]);
    const high = o.auction ? o.auction.high : 0;
    const cash = p >= 0 && p < o.np ? o.players[p].cash : 0;
    for (const a of [0, high, high + 1, cash, cash + 1, 1 + Math.floor(rand() * 400)]) out.push([p, 3, a]);
  }
  return out;
}

// -- The lockstep games --------------------------------------------------
// The driver mixes the engine's own choices with random legal actions, so
// a person's choices the engine never makes (declining, bidding past
// value, mortgaging for no reason, declaring bankruptcy) are exercised too.
// A throw is sometimes aimed at doubles by searching seeds, so three
// doubles and Jail's doubles are reached.
const failures = [];
const note = msg => { if (failures.length < 12) failures.push(msg); };
let actions = 0, legalChecks = 0, refusalsChecked = 0, stepChecks = 0, engineRefusedLegal = 0;
let tradeRuleBreaks = 0, gamesWon = 0, gamesCapped = 0;

function findDoubles(h, p, rand) {
  for (let t = 0; t < 60; t++) {
    const seed = 1 + Math.floor(rand() * 2 ** 30);
    const r = e.mo_act(h, p, 0, seed);
    if (r !== h && e.mo_die(r, 0) === e.mo_die(r, 1)) return seed;
  }
  return 1 + Math.floor(rand() * 2 ** 30);
}

function stepByHand(h, seed) {
  let g = h, n = 0;
  const t0 = e.mo_turn(h), theirs = e.mo_actor(h) === 0;
  while (e.mo_done(g) === 0 && n < 400) {
    if (n > 0 && e.mo_turn(g) !== t0) break;
    if (n > 0 && !theirs && e.mo_actor(g) === 0) break;
    const c = e.mo_ai(g);
    if (c < 0) break;
    const { kind, p, arg } = decode(c);
    const g2 = e.mo_act(g, p, kind, kind === 0 ? seed + n * 7919 : arg);
    if (g2 === g) break;
    g = g2; n++;
  }
  return g;
}
const allSlots = h => [...Array(472)].map((_, i) => e.mo_slot(h, i)).join(',');

// A builder drives toward the housing shortage: it builds whenever it can,
// and once the Bank is short of houses it sells a hotel.
function builder(o, menu, rand) {
  const hotelSale = menu.filter(([, k, a]) => k === KIND.sell && o.deeds[a].h === 5);
  if (o.houses < 4 && hotelSale.length) return hotelSale[0];
  const builds = menu.filter(([, k]) => k === KIND.build);
  if (builds.length && rand() < 0.9) return builds[Math.floor(rand() * builds.length)];
  return null;
}

function playGame(seed, np, engineShare, legalEvery, maxActions, prefer) {
  e.__heap_reset();
  const rand = makeRand(seed);
  let h = e.mo_new(seed, np);
  const decks = [0, 1].map(d => [...Array(e.mo_decklen(h, d))].map((_, k) => e.mo_deck(h, d, k)));
  const o = newOracle(np, decks.map(d => d.slice()), 0);
  const d0 = differ(snapshot(h, np), oracleView(o));
  if (d0) { note(`seed ${seed}: the opening differs: ${d0}`); return; }
  for (let n = 0; n < maxActions && !o.over; n++) {
    // The legal set, both ways.
    if (n % legalEvery === 0) {
      for (const [p, kind, arg] of probes(o, rand)) {
        legalChecks++;
        const want = legal(o, p, kind, arg), got = e.mo_legal(h, p, kind, arg) === 1;
        if (want !== got) { note(`seed ${seed} action ${n}: seat ${p} kind ${kind} arg ${arg}: engine ${got ? 'allows' : 'refuses'}, rules ${want ? 'allow' : 'refuse'} (phase ${o.phase})`); return; }
        if (!want && rand() < 0.02) {
          refusalsChecked++;
          if (e.mo_act(h, p, kind, arg) !== h) { note(`seed ${seed} action ${n}: a refused action changed the state (seat ${p} kind ${kind} arg ${arg})`); return; }
        }
      }
    }
    // A step is the engine's choices applied one at a time.
    if (rand() < 0.03) {
      stepChecks++;
      const s = 1 + Math.floor(rand() * 2 ** 30);
      if (allSlots(e.mo_step(h, s)) !== allSlots(stepByHand(h, s))) { note(`seed ${seed} action ${n}: a step is not its choices one at a time`); return; }
    }
    // Choose.
    let p, kind, arg;
    // The shortage, set up: the Bank's houses (slot 12 of the layout) cut
    // below four in both, where a hotel could be sold.
    if (prefer && o.houses >= 4 && rand() < 0.3
        && o.deeds.some((d, i) => d.h === 5 && legal(o, d.owner, KIND.sell, i))) {
      const cut = Math.floor(rand() * 4);
      h = e.mo_poke(h, 12, cut);
      o.houses = cut;
    }
    const ai = e.mo_ai(h);
    const pick = prefer ? prefer(o, probes(o, rand).filter(([q, k, a]) => legal(o, q, k, a)), rand) : null;
    if (pick) [p, kind, arg] = pick;
    else if (ai >= 0 && rand() < engineShare) ({ kind, p, arg } = decode(ai));
    else {
      const menu = probes(o, rand).filter(([q, k, a]) => legal(o, q, k, a));
      if (menu.length === 0) { if (ai < 0) { note(`seed ${seed}: nobody can act and the game is not over`); return; } ({ kind, p, arg } = decode(ai)); }
      else [p, kind, arg] = menu[Math.floor(rand() * menu.length)];
    }
    if (kind === 0) arg = rand() < 0.3 ? findDoubles(h, p, rand) : 1 + Math.floor(rand() * 2 ** 30);
    const quote = kind === KIND.trade ? [e.mo_qgive(h, p, arg), e.mo_qprice(h, p, arg)] : null;
    if (quote) {
      const q = o.deeds[arg].owner, give = quote[0];
      const built = i => groupOf(i).some(j => o.deeds[j].h > 0);
      if (q < 0 || q === p || o.players[q].out || built(arg) || (give >= 0 && (o.deeds[give].owner !== p || built(give)))
        || (give < 0 && o.players[p].cash < quote[1])) tradeRuleBreaks++;
    }
    const h2 = e.mo_act(h, p, kind, arg);
    actions++;
    if (h2 === h) { engineRefusedLegal++; note(`seed ${seed} action ${n}: the engine refused seat ${p} kind ${kind} arg ${arg}, which it called legal`); return; }
    const eng = {
      dice: () => [e.mo_die(h2, 0), e.mo_die(h2, 1)],
      cthrow: () => e.mo_cthrow(h2),
      quote: () => quote,
    };
    apply(o, p, kind, arg, eng);
    const d = differ(snapshot(h2, np), oracleView(o));
    if (d) { note(`seed ${seed} action ${n} (seat ${p} kind ${kind} arg ${arg}): ${d}`); return; }
    h = h2;
  }
  if (o.over) gamesWon++; else gamesCapped++;
}

const t0 = Date.now();
for (let g = 1; g <= 24; g++) playGame(g, 2 + (g % 3), 0.85, 1, 3000);
for (let g = 1; g <= 16; g++) playGame(1000 + g, 4, 0.5, 3, 3000);
for (let g = 1; g <= 16; g++) playGame(2000 + g, 3, 0.2, 5, 2000);
for (let g = 1; g <= 16; g++) playGame(3000 + g, 4, 0.85, 2, 3000, builder);
ok('the engine and the rules agree after every action, and on every legal set',
  failures.length === 0,
  failures.length ? failures.slice(0, 4).join('; ') : `${actions} actions, ${legalChecks} legal-set questions, ${refusalsChecked} refusals, ${((Date.now() - t0) / 1000).toFixed(0)} s`);
ok('a step is the engine\'s choices one at a time', failures.length === 0 && stepChecks > 20, `${stepChecks} positions`);
ok('every trade the engine proposes is one Rule 12 allows', tradeRuleBreaks === 0, `${tradeRuleBreaks} breaks`);
ok('control: games ended by bankruptcy', gamesWon > 10, `${gamesWon} won, ${gamesCapped} stopped at the action cap`);

// L-VACUOUS: every class the rules distinguish was reached.
const CLASSES = [
  'doubles: throw again', 'jail: third doubles', 'jail: the square', 'jail: a card',
  'jail: out on doubles', 'jail: paid the fine', 'jail: used a card', 'jail: third failure pays and moves',
  'GO: passed or landed on', 'GO: passed by a card', 'income tax', 'luxury tax',
  'rent: lot', 'rent: doubled, whole group', 'rent: doubled beside a mortgaged lot', 'rent: 1 house', 'rent: 2 houses',
  'rent: 3 houses', 'rent: 4 houses', 'rent: hotel', 'rent: 1 railroad', 'rent: 2 railroads',
  'rent: 3 railroads', 'rent: 4 railroads', 'rent: 1 railroad, card double', 'rent: utility x4', 'rent: utility x10',
  'rent: card utility, ten times a fresh throw', 'rent: none on a mortgaged deed',
  'bought', 'declined, so auctioned', 'auction: sold', 'auction: nobody bid', 'auction: a bankrupt\'s deed',
  'built: a house', 'built: a hotel', 'sold: a house', 'sold: a hotel', 'sold: a hotel, the Bank short of houses',
  'mortgaged', 'lifted', 'debt: short of cash', 'bankrupt: to a player', 'bankrupt: to the Bank',
  'bankrupt: interest on a mortgaged deed', 'trade: a swap', 'trade: a sale', 'offer: made to the person',
  'offer: accepted', 'offer: refused', 'game: won by the last player standing',
  ...[...Array(16)].map((_, k) => `chance card ${k}`), ...[...Array(16)].map((_, k) => `chest card ${k}`),
];
const missed = CLASSES.filter(c => !seen.has(c));
ok('control: every class of the rules was reached', missed.length === 0,
  missed.length ? `never reached: ${missed.join('; ')}` : `${CLASSES.length} classes`);

// -- The dice ------------------------------------------------------------
// The rules need doubles about one throw in six; dice drawn from a
// generator's low bits never match.
{
  // A reset reclaims the handle itself, so the game is made again after one.
  let h = 0;
  const faces = [0, 0, 0, 0, 0, 0, 0];
  let doubles = 0;
  const T = 6000;
  for (let t = 0; t < T; t++) {
    if (t % 500 === 0) { e.__heap_reset(); h = e.mo_new(5, 2); }
    const r = e.mo_act(h, 0, 0, 1 + t * 7919);
    const a = e.mo_die(r, 0), b = e.mo_die(r, 1);
    faces[a]++; faces[b]++;
    if (a === b) doubles++;
  }
  const share = doubles / T;
  ok('the dice throw doubles about one time in six', share > 0.14 && share < 0.195, share.toFixed(3));
  ok('every face comes up about one time in six', faces.slice(1).every(f => Math.abs(f / (2 * T) - 1 / 6) < 0.02),
    faces.slice(1).join(','));
}

// -- The board -----------------------------------------------------------
{
  const bad = [];
  for (let i = 0; i < N; i++) {
    if (e.mo_space(i) !== BOARD[i][0]) bad.push(`deed ${i} on square ${e.mo_space(i)}`);
    if (e.mo_cost(0, i) !== BOARD[i][1]) bad.push(`deed ${i} priced ${e.mo_cost(0, i)}`);
    if (e.mo_hcost(i) !== BOARD[i][3]) bad.push(`deed ${i} houses at ${e.mo_hcost(i)}`);
    if (e.mo_propat(BOARD[i][0]) !== i) bad.push(`square ${BOARD[i][0]} names deed ${e.mo_propat(BOARD[i][0])}`);
  }
  for (let sq = 0; sq < 40; sq++) if (deedAt(sq) < 0 && e.mo_propat(sq) !== -1) bad.push(`square ${sq} is not a deed`);
  const groups = new Map();
  for (let i = 0; i < N; i++) {
    const g = e.mo_group(0, i);
    if (!groups.has(g)) groups.set(g, colour(i));
    else if (groups.get(g) !== colour(i)) bad.push(`deed ${i} grouped with ${groups.get(g)}`);
  }
  if (groups.size !== 10) bad.push(`${groups.size} groups`);
  ok('the 28 deeds sit on their squares at their prices, in their groups', bad.length === 0,
    bad.length ? bad.slice(0, 3).join('; ') : '28 deeds, 10 groups');
}

// -- The page ------------------------------------------------------------
// Seat 0 plays only through the arcade descriptor: its buttons and clicks.
// At every position where the choice is seat 0's, each button is enabled
// exactly when the engine allows its action, a mode button's highlighted
// squares are exactly the deeds the engine allows that action on, and a
// click does what the engine's action does and nothing where it refuses.
// The page must always leave seat 0 something to do.
{
  const { GAMES } = await import('../landing/web/games/arcade.js');
  const g = GAMES.find(x => x.id === 'monopoly');
  const byLabel = l => g.actions.findIndex(a => a.label === l);
  const PLAIN = { 'Buy it': [1, 0], 'Leave it (auction)': [2, 0], 'Drop out': [4, 0], 'Pay $50 fine': [9, 0],
    'Use Get Out of Jail card': [10, 0], 'Declare bankruptcy': [11, 0], 'Accept trade': [12, 0], 'Decline trade': [13, 0] };
  const MODES = { Build: 5, 'Sell a building': 6, Mortgage: 7, 'Lift a mortgage': 8 };
  const bad = [];
  let positions = 0, finished = 0, clicks = 0, modeClicks = 0, bids = 0;
  const used = new Set();
  const movable = (h, sel) => g.view(e, h, false, sel).cells
    .filter(c => c.cls.split(' ').includes('movable')).map(c => c.i).sort((a, b) => a - b).join(',');
  for (let seed = 1; seed <= 20 && bad.length === 0; seed++) {
    e.__heap_reset();
    const rand = makeRand(500 + seed);
    const r = () => 1 + Math.floor(rand() * 2 ** 30);
    let h = g.boot(e, seed);
    for (let n = 0; n < 4000 && !g.done(e, h) && bad.length === 0; n++) {
      if (g.turn(e, h) !== 0) { h = g.step(e, h, r); continue; }
      positions++;
      const options = [];
      for (const [label, [kind, arg]] of Object.entries(PLAIN)) {
        const a = g.actions[byLabel(label)];
        const on = a.enabled(e, h, null, null), want = e.mo_legal(h, 0, kind, arg) === 1;
        if (on !== want) bad.push(`seed ${seed}: '${label}' ${on ? 'enabled' : 'disabled'}, engine ${want ? 'allows' : 'refuses'}`);
        if (on) options.push(() => { used.add(label); return a.run(e, h, r).handle; });
      }
      const roll = g.actions[byLabel('Roll')];
      if (roll.enabled(e, h, null, null) !== (e.mo_legal(h, 0, 0, 1) === 1)) bad.push(`seed ${seed}: Roll disagrees with the engine`);
      if (roll.enabled(e, h, null, null)) options.push(() => { used.add('Roll'); return roll.run(e, h, r).handle; });
      for (const inc of [10, 50, 100]) {
        const a = g.actions[byLabel(`Bid +$${inc}`)];
        const want = e.mo_legal(h, 0, 3, e.mo_ahigh(h) + inc) === 1;
        if (a.enabled(e, h, null, null) !== want) bad.push(`seed ${seed}: 'Bid +$${inc}' disagrees with the engine`);
        if (want) options.push(() => { used.add('Bid'); bids++; return a.run(e, h, r).handle; });
      }
      for (const [label, kind] of Object.entries(MODES)) {
        const a = g.actions[byLabel(label)];
        const legalDeeds = seq28().filter(pi => e.mo_legal(h, 0, kind, pi) === 1);
        if (a.enabled(e, h, null, null) !== (legalDeeds.length > 0)) bad.push(`seed ${seed}: '${label}' disagrees with the engine`);
        if (legalDeeds.length === 0) continue;
        const out = a.run(e, h, r);
        if (out.handle !== h || out.sel !== kind) bad.push(`seed ${seed}: '${label}' did not arm its mode`);
        const shown = movable(h, kind), want = legalDeeds.map(pi => e.mo_space(pi)).sort((x, y) => x - y).join(',');
        if (shown !== want) bad.push(`seed ${seed}: '${label}' highlights ${shown}, the engine allows ${want}`);
        for (let sq = 0; sq < 40; sq++) {
          const pi = e.mo_propat(sq), res = g.move(e, h, sq, { sel: kind });
          const allowed = pi >= 0 && e.mo_legal(h, 0, kind, pi) === 1;
          if (!allowed && res) bad.push(`seed ${seed}: '${label}' clicked square ${sq} and acted`);
          if (allowed && (!res || allSlots(res.handle) !== allSlots(e.mo_act(h, 0, kind, pi)))) bad.push(`seed ${seed}: '${label}' on square ${sq} is not the engine's action`);
        }
        for (const pi of legalDeeds) options.push(() => { used.add(label); modeClicks++; return g.move(e, h, e.mo_space(pi), { sel: kind }).handle; });
      }
      // Without a mode, a click buys the deed you are offered, and while you
      // owe more than your cash it sells a building from a deed or mortgages
      // it -- never both legal on one deed, which this checks.
      const offer = e.mo_offerspace(h), canBuy = e.mo_legal(h, 0, 1, 0) === 1;
      const debt = e.mo_phase(h) === 4;
      const plain = sq => {
        const pi = e.mo_propat(sq);
        if (canBuy && sq === offer) return [1, 0];
        if (!debt || pi < 0) return null;
        const sell = e.mo_legal(h, 0, 6, pi) === 1, mort = e.mo_legal(h, 0, 7, pi) === 1;
        if (sell && mort) bad.push(`seed ${seed}: deed ${pi} could both sell a building and mortgage`);
        return sell ? [6, pi] : mort ? [7, pi] : null;
      };
      const wantShown = [...Array(40).keys()].filter(sq => plain(sq)).join(',');
      if (movable(h, null) !== wantShown) bad.push(`seed ${seed}: the board highlights ${movable(h, null)} with no mode, the engine allows ${wantShown}`);
      for (let sq = 0; sq < 40; sq++) {
        const res = g.move(e, h, sq, { sel: null }), want = plain(sq);
        if ((res !== null) !== (want !== null)) bad.push(`seed ${seed}: a plain click on square ${sq} ${res ? 'acted' : 'did nothing'}`);
        else if (want && allSlots(res.handle) !== allSlots(e.mo_act(h, 0, want[0], want[1]))) bad.push(`seed ${seed}: a plain click on square ${sq} is not the engine's action`);
        if (want) options.push(() => { clicks++; if (want[0] !== 1) used.add('raise by a click'); return g.move(e, h, sq, { sel: null }).handle; });
      }
      if (options.length === 0) { bad.push(`seed ${seed}: seat 0 has the choice and the page offers nothing (phase ${e.mo_phase(h)})`); break; }
      h = options[Math.floor(rand() * options.length)]();
    }
    if (g.done(e, h)) {
      finished++;
      if (!g.status(e, h).includes('bankrupts the rest')) bad.push(`seed ${seed}: the finished status is '${g.status(e, h)}'`);
    }
  }
  ok('page: every button and click is the engine\'s legal set, and seat 0 always has a move',
    bad.length === 0, bad.length ? bad.slice(0, 3).join('; ') : `${positions} positions, ${finished} of 20 games finished`);
  const every = ['Roll', 'Buy it', 'Leave it (auction)', 'Bid', 'Drop out', 'Build', 'Sell a building', 'Mortgage', 'Accept trade', 'Decline trade', 'raise by a click'];
  ok('control: the page drove every kind of choice', every.every(l => used.has(l)) && clicks > 0 && modeClicks > 0,
    `missing: ${every.filter(l => !used.has(l)).join(', ') || 'none'}; ${clicks} buying clicks, ${modeClicks} mode clicks, ${bids} bids`);
}
function seq28() { return [...Array(28).keys()]; }

// -- Refusals off the board ----------------------------------------------
{
  const h = e.mo_new(3, 2);
  ok('a seat or deed off the table is refused',
    e.mo_cash(h, 9) === -1 && e.mo_pos(h, -1) === -1 && e.mo_owner(h, 99) === -2 && e.mo_houses(h, -1) === -1
    && e.mo_cards(h, 0, 2) === -1 && e.mo_deck(h, 0, 16) === -1 && e.mo_debt(h, 0, 0) === -9);
  ok('the page\'s turn cap is the engine\'s', e.mo_cap() === 600, e.mo_cap());
}

console.log(fail === 0
  ? `\nPASS: Monopoly agrees with its rules (${pass} arms).`
  : `\nFAIL: ${fail} of ${pass + fail} arms.`);
process.exitCode = fail === 0 ? 0 : 1;
