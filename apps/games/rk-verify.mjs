// Grade the Risk wasm module against the rules.
//
// The oracle below is a second implementation of the rules, written from
// "The Rules" in apps/games/classic/Risk.codex (Hasbro's classic leaflet,
// and the choices the engine states where the rules leave one), not from
// the engine's code. The board is transcribed again here as neighbour lists
// by name. The oracle runs in lockstep with the engine: the same action goes
// to both, and after every action the whole observable state must agree --
// every territory's owner and armies, the phase, whose turn, the armies to
// place and to set up, every card's holder, the deck and the discard pile,
// the trade count and a conquest's move-in. Before actions the LEGAL SET
// must agree both ways (L-BOTHARMS), and an action the rules refuse comes
// back as the same handle.
//
// What the oracle takes from the engine, because none of it is a rule: the
// deal's order and the deck's order, both shuffled from the seed (the deal
// is checked to be round the table), the dice of each battle, the order a
// reshuffled deck comes out in (checked to be the discard pile), and the
// engine players' choices. The dice are graded separately.
//
// What a PASS does not mean: that the engine's players play well, or that
// the page draws the map; the oracle and the engine share one reading of
// the leaflet, so a rule both read the same wrong way passes (L-ORACLE).
//
// Usage: node apps/games/rk-verify.mjs [path/to/risk.wasm]

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import { gameInstance } from '../landing/web/games/arcade.js';

const here = dirname(fileURLToPath(import.meta.url));
const wasmPath = process.argv[2] ||
  join(here, '..', 'landing', 'web', 'games', 'risk.wasm');
const e = gameInstance(new WebAssembly.Module(readFileSync(wasmPath))).exports;

let pass = 0, fail = 0;
const ok = (name, cond, detail) => {
  if (cond) { console.log(`  ok    ${name}${detail !== undefined ? ': ' + detail : ''}`); pass++; }
  else { console.log(`  FAIL  ${name}${detail !== undefined ? ': ' + detail : ''}`); fail++; }
};
console.log(`rk-verify ${wasmPath}`);

function makeRand(seed) {
  let x = BigInt(seed) * 0x9E3779B97F4A7C15n + 0x632BE59BD9B4E019n;
  return () => {
    x = (x * 6364136223846793005n + 1442695040888963407n) & 0xFFFFFFFFFFFFFFFFn;
    return Number(x >> 33n) / 2147483648;
  };
}

// -- The board, from the leaflet's map -----------------------------------
const NAMES = ['Alaska', 'Northwest Territory', 'Greenland', 'Alberta', 'Ontario', 'Quebec',
  'Western United States', 'Eastern United States', 'Central America',
  'Venezuela', 'Peru', 'Brazil', 'Argentina',
  'Iceland', 'Scandinavia', 'Great Britain', 'Northern Europe', 'Ukraine', 'Western Europe', 'Southern Europe',
  'North Africa', 'Egypt', 'East Africa', 'Congo', 'South Africa', 'Madagascar',
  'Ural', 'Siberia', 'Yakutsk', 'Kamchatka', 'Irkutsk', 'Mongolia', 'Japan', 'Afghanistan', 'China',
  'Middle East', 'India', 'Siam',
  'Indonesia', 'New Guinea', 'Western Australia', 'Eastern Australia'];
const NEIGHBOURS = {
  'Alaska': ['Northwest Territory', 'Alberta', 'Kamchatka'],
  'Northwest Territory': ['Alaska', 'Alberta', 'Ontario', 'Greenland'],
  'Greenland': ['Northwest Territory', 'Ontario', 'Quebec', 'Iceland'],
  'Alberta': ['Alaska', 'Northwest Territory', 'Ontario', 'Western United States'],
  'Ontario': ['Northwest Territory', 'Alberta', 'Greenland', 'Quebec', 'Western United States', 'Eastern United States'],
  'Quebec': ['Ontario', 'Greenland', 'Eastern United States'],
  'Western United States': ['Alberta', 'Ontario', 'Eastern United States', 'Central America'],
  'Eastern United States': ['Western United States', 'Ontario', 'Quebec', 'Central America'],
  'Central America': ['Western United States', 'Eastern United States', 'Venezuela'],
  'Venezuela': ['Central America', 'Peru', 'Brazil'],
  'Peru': ['Venezuela', 'Brazil', 'Argentina'],
  'Brazil': ['Venezuela', 'Peru', 'Argentina', 'North Africa'],
  'Argentina': ['Peru', 'Brazil'],
  'Iceland': ['Greenland', 'Great Britain', 'Scandinavia'],
  'Scandinavia': ['Iceland', 'Great Britain', 'Northern Europe', 'Ukraine'],
  'Great Britain': ['Iceland', 'Scandinavia', 'Northern Europe', 'Western Europe'],
  'Northern Europe': ['Great Britain', 'Scandinavia', 'Ukraine', 'Southern Europe', 'Western Europe'],
  'Ukraine': ['Scandinavia', 'Northern Europe', 'Southern Europe', 'Ural', 'Afghanistan', 'Middle East'],
  'Western Europe': ['Great Britain', 'Northern Europe', 'Southern Europe', 'North Africa'],
  'Southern Europe': ['Western Europe', 'Northern Europe', 'Ukraine', 'Middle East', 'Egypt', 'North Africa'],
  'North Africa': ['Brazil', 'Western Europe', 'Southern Europe', 'Egypt', 'East Africa', 'Congo'],
  'Egypt': ['North Africa', 'Southern Europe', 'Middle East', 'East Africa'],
  'East Africa': ['Egypt', 'North Africa', 'Congo', 'South Africa', 'Madagascar', 'Middle East'],
  'Congo': ['North Africa', 'East Africa', 'South Africa'],
  'South Africa': ['Congo', 'East Africa', 'Madagascar'],
  'Madagascar': ['South Africa', 'East Africa'],
  'Ural': ['Ukraine', 'Siberia', 'China', 'Afghanistan'],
  'Siberia': ['Ural', 'Yakutsk', 'Irkutsk', 'Mongolia', 'China'],
  'Yakutsk': ['Siberia', 'Kamchatka', 'Irkutsk'],
  'Kamchatka': ['Yakutsk', 'Irkutsk', 'Mongolia', 'Japan', 'Alaska'],
  'Irkutsk': ['Siberia', 'Yakutsk', 'Kamchatka', 'Mongolia'],
  'Mongolia': ['Siberia', 'Irkutsk', 'Kamchatka', 'Japan', 'China'],
  'Japan': ['Kamchatka', 'Mongolia'],
  'Afghanistan': ['Ukraine', 'Ural', 'China', 'India', 'Middle East'],
  'China': ['Afghanistan', 'Ural', 'Siberia', 'Mongolia', 'Siam', 'India'],
  'Middle East': ['Ukraine', 'Afghanistan', 'India', 'Egypt', 'East Africa', 'Southern Europe'],
  'India': ['Middle East', 'Afghanistan', 'China', 'Siam'],
  'Siam': ['India', 'China', 'Indonesia'],
  'Indonesia': ['Siam', 'New Guinea', 'Western Australia'],
  'New Guinea': ['Indonesia', 'Eastern Australia', 'Western Australia'],
  'Western Australia': ['Indonesia', 'New Guinea', 'Eastern Australia'],
  'Eastern Australia': ['New Guinea', 'Western Australia'],
};
const CONTINENTS = [
  ['North America', 5, NAMES.slice(0, 9)], ['South America', 2, NAMES.slice(9, 13)],
  ['Europe', 5, NAMES.slice(13, 20)], ['Africa', 3, NAMES.slice(20, 26)],
  ['Asia', 7, NAMES.slice(26, 38)], ['Australia', 2, NAMES.slice(38, 42)],
];
const T = NAMES.length;
const idx = n => NAMES.indexOf(n);
const ADJ = NAMES.map(n => new Set(NEIGHBOURS[n].map(idx)));
const adjacent = (a, b) => a >= 0 && b >= 0 && a < T && b < T && ADJ[a].has(b);
const START = { 2: 40, 3: 35, 4: 30 };
const symbol = c => (c >= 42 ? 'wild' : ['infantry', 'cavalry', 'artillery'][c % 3]);
const tradeValue = k => (k < 5 ? [4, 6, 8, 10, 12][k] : 15 + 5 * (k - 5));
const pack = (a, b, c) => a + 64 * b + 4096 * c;
const unpack = x => [x % 64, Math.floor(x / 64) % 64, Math.floor(x / 4096)];
const KIND = { place: 0, trade: 1, attack: 2, stop: 3, movein: 4, fortify: 5, end: 6 };

// -- The oracle ----------------------------------------------------------
const seen = new Map();
const saw = k => seen.set(k, (seen.get(k) || 0) + 1);

const held = (o, p) => o.owner.filter(q => q === p).length;
const hand = (o, p) => o.holder.filter(q => q === p).length;
const alive = (o, p) => held(o, p) > 0;
function reinforcements(o, p) {
  let n = Math.max(3, Math.floor(held(o, p) / 3));
  for (const [name, bonus, lands] of CONTINENTS) {
    if (lands.every(l => o.owner[idx(l)] === p)) { n += bonus; saw(`continent bonus: ${name}`); }
  }
  return n;
}
function isSet(a, b, c) {
  const s = [a, b, c].map(symbol);
  if (s.includes('wild')) { saw('set: with a wild card'); return true; }
  if (s[0] === s[1] && s[1] === s[2]) { saw('set: three of a kind'); return true; }
  if (new Set(s).size === 3) { saw('set: one of each'); return true; }
  return false;
}
function connected(o, p, a, b) {
  const seenT = new Set([a]), q = [a];
  while (q.length) {
    const t = q.shift();
    for (const u of ADJ[t]) if (!seenT.has(u) && o.owner[u] === p) { seenT.add(u); q.push(u); }
  }
  return seenT.has(b);
}

function legal(o, p, kind, arg) {
  if (o.over || p < 0 || p >= o.np || p !== o.cur) return false;
  const ph = o.phase;
  switch (kind) {
    case KIND.place:
      if (!(arg >= 0 && arg < T) || o.owner[arg] !== p) return false;
      if (ph === 0) return o.setup[p] > 0;
      if (ph === 1 || ph === 5) return o.place > 0 && hand(o, p) < 5;
      return false;
    case KIND.trade: {
      if (!(ph === 1 || (ph === 5 && hand(o, p) >= 5))) return false;
      const [a, b, c] = unpack(arg);
      if (!(a < b && b < c && c < 44)) return false;
      if (o.holder[a] !== p || o.holder[b] !== p || o.holder[c] !== p) return false;
      return isSet(a, b, c);
    }
    case KIND.attack: {
      if (ph !== 2) return false;
      const [from, to, dice] = unpack(arg);
      if (!(from < T && to < T)) return false;
      return o.owner[from] === p && o.owner[to] !== p && adjacent(from, to)
        && dice >= 1 && dice <= 3 && dice <= o.armies[from] - 1;
    }
    case KIND.stop: return ph === 2;
    case KIND.movein: return ph === 3 && arg >= o.move.min && arg <= o.armies[o.move.from] - 1;
    case KIND.fortify: {
      if (ph !== 4) return false;
      const [from, to, n] = unpack(arg);
      if (!(from < T && to < T) || from === to) return false;
      if (o.owner[from] !== p || o.owner[to] !== p) return false;
      if (!(n >= 1 && n <= o.armies[from] - 1)) return false;
      return connected(o, p, from, to);
    }
    case KIND.end: return ph === 4;
    default: return false;
  }
}

function beginTurn(o) {
  o.conquered = false; o.bonus = false; o.elim = false;
  o.place = reinforcements(o, o.cur); o.phase = 1;
  if (hand(o, o.cur) >= 5) saw('cards: five or more, must trade');
}
function nextLive(o, p) {
  for (let k = 1; k <= o.np; k++) { const q = (p + k) % o.np; if (alive(o, q)) return q; }
  return p;
}
function draw(o, p, eng) {
  if (o.deck.length === 0) {
    const next = eng.deck();
    const before = o.discard.slice().sort((x, y) => x - y).join(','), after = next.slice().sort((x, y) => x - y).join(',');
    if (before !== after) o.reshuffleBad = `the reshuffled deck is ${after}, the discard pile was ${before}`;
    if (next.length) saw('cards: the discard pile reshuffled into the deck');
    o.deck = next.slice(); o.discard = [];
    for (const c of o.deck) o.holder[c] = -2;
  }
  if (o.deck.length === 0) return;
  const c = o.deck.shift();
  o.holder[c] = p;
  saw('cards: drawn for a conquest');
}

function apply(o, p, kind, arg, eng) {
  switch (kind) {
    case KIND.place:
      o.armies[arg]++;
      if (o.phase === 0) {
        o.setup[p]--; saw('setup: an army placed');
        for (let k = 1; k <= o.np; k++) {
          const q = (p + k) % o.np;
          if (o.setup[q] > 0) { o.cur = q; return; }
        }
        o.cur = 0; beginTurn(o); saw('setup: finished, the first turn begins');
        return;
      }
      o.place--;
      if (o.place === 0) o.phase = 2;
      return;
    case KIND.trade: {
      const [a, b, c] = unpack(arg);
      o.place += tradeValue(o.trades); o.trades++;
      saw(o.trades > 6 ? 'trade: past the sixth, five more each' : `trade: number ${o.trades}`);
      if (o.phase === 5) saw('trade: forced after an elimination');
      for (const x of [a, b, c]) { o.holder[x] = -1; o.discard.push(x); }
      if (!o.bonus) {
        const t = [a, b, c].find(x => x < 42 && o.owner[x] === p);
        if (t !== undefined) { o.armies[t] += 2; o.bonus = true; saw('trade: 2 armies on a held territory'); }
      } else if ([a, b, c].some(x => x < 42 && o.owner[x] === p)) saw('trade: no second territory bonus in a turn');
      return;
    }
    case KIND.attack: {
      const [from, to, n] = unpack(arg);
      const m = Math.min(2, o.armies[to]);
      const dice = eng.dice();
      const atk = dice.slice(0, n).sort((x, y) => y - x), def = dice.slice(3, 3 + m).sort((x, y) => y - x);
      let aLost = 0, dLost = 0;
      for (let k = 0; k < Math.min(n, m); k++) {
        if (atk[k] > def[k]) dLost++;
        else { aLost++; if (atk[k] === def[k]) saw('battle: a tie goes to the defender'); }
      }
      saw(`battle: ${n} dice against ${m}`);
      o.armies[from] -= aLost; o.armies[to] -= dLost;
      if (o.armies[to] > 0) return;
      const q = o.owner[to];
      o.owner[to] = p; o.conquered = true; saw('conquest');
      o.move = { from, to, min: n };
      if (held(o, p) === T) {
        o.armies[to] = n; o.armies[from] -= n;
        o.over = true; o.winner = p; o.phase = 6; o.place = 0; saw('game: one player holds the world');
        return;
      }
      if (held(o, q) === 0) {
        saw('elimination: a player knocked out');
        for (let c = 0; c < 44; c++) if (o.holder[c] === q) { o.holder[c] = p; saw('elimination: cards taken'); }
        o.elim = hand(o, p) >= 6;
      }
      o.phase = 3;
      return;
    }
    case KIND.stop: o.phase = 4; saw('attack: stopped'); return;
    case KIND.movein: {
      const { from, to, min } = o.move;
      saw(arg === min ? 'move in: the least' : arg === o.armies[from] - 1 ? 'move in: all but one' : 'move in: between');
      o.armies[from] -= arg; o.armies[to] = arg;
      o.move = { from: -1, to: -1, min: 0 };
      if (o.elim) { o.elim = false; o.place = 0; o.phase = 5; saw('elimination: six or more cards, trade at once'); }
      else o.phase = 2;
      return;
    }
    case KIND.fortify: {
      const [from, to, n] = unpack(arg);
      o.armies[from] -= n; o.armies[to] += n;
      saw(adjacent(from, to) ? 'fortify: next door' : 'fortify: through a chain of own territories');
      return endTurn(o, eng);
    }
    case KIND.end: saw('turn: ended without fortifying'); return endTurn(o, eng);
  }
}
function endTurn(o, eng) {
  if (o.conquered) draw(o, o.cur, eng);
  o.cur = nextLive(o, o.cur); o.turn++;
  beginTurn(o);
}

// -- Reading the engine --------------------------------------------------
function snapshot(h, np) {
  const s = {
    phase: e.rk_phase(h), cur: e.rk_cur(h), turn: e.rk_turnno(h), done: e.rk_done(h),
    place: e.rk_toplace(h), trades: e.rk_trades(h),
    setup: [...Array(np)].map((_, p) => e.rk_setupleft(h, p)).join(','),
    owner: [...Array(T)].map((_, t) => e.rk_owner(h, t)).join(','),
    armies: [...Array(T)].map((_, t) => e.rk_armies(h, t)).join(','),
    holder: [...Array(44)].map((_, c) => e.rk_holder(h, c)).join(','),
    deck: [...Array(e.rk_decklen(h))].map((_, k) => e.rk_deck(h, k)).join(','),
    discard: [...Array(e.rk_discardlen(h))].map((_, k) => e.rk_discard(h, k)).join(','),
  };
  if (s.done) s.winner = e.rk_winner(h);
  if (s.phase === 3) s.move = [e.rk_mfrom(h), e.rk_mto(h), e.rk_mmin(h)].join(',');
  return s;
}
function oracleView(o) {
  const s = {
    phase: o.phase, cur: o.cur, turn: o.turn, done: o.over ? 1 : 0,
    place: o.place, trades: o.trades,
    setup: o.setup.join(','), owner: o.owner.join(','), armies: o.armies.join(','),
    holder: o.holder.join(','), deck: o.deck.join(','), discard: o.discard.join(','),
  };
  if (o.over) s.winner = o.winner;
  if (o.phase === 3) s.move = [o.move.from, o.move.to, o.move.min].join(',');
  return s;
}
function differ(a, b) {
  for (const k of new Set([...Object.keys(a), ...Object.keys(b)])) {
    if (JSON.stringify(a[k]) !== JSON.stringify(b[k])) return `${k}: engine ${JSON.stringify(a[k])}, rules ${JSON.stringify(b[k])}`;
  }
  return null;
}
const decode = c => ({ kind: c % 8, p: Math.floor(c / 8) % 8, arg: Math.floor(c / 64) });

// Every (player, kind, arg) the legal-set arm asks, for one position. The
// current player's candidates are complete; the others' are a spread,
// since the rules refuse every action off turn.
function probes(o, rand) {
  const out = [], p = o.cur;
  for (let q = -1; q <= o.np; q++) {
    for (const kind of [3, 6]) out.push([q, kind, 0]);
    out.push([q, 0, Math.floor(rand() * T)]);
  }
  for (let t = -1; t <= T; t++) out.push([p, 0, t]);
  const mine = [...Array(44).keys()].filter(c => o.holder[c] === p);
  for (let i = 0; i < mine.length; i++) for (let j = i + 1; j < mine.length; j++) for (let k = j + 1; k < mine.length; k++) {
    out.push([p, 1, pack(mine[i], mine[j], mine[k])]);
  }
  if (mine.length >= 2) out.push([p, 1, pack(mine[1], mine[0], mine[mine.length - 1])]);
  out.push([p, 1, pack(0, 1, 2)], [p, 1, pack(41, 42, 43)]);
  for (let from = 0; from < T; from++) {
    if (o.owner[from] !== p) continue;
    for (const to of [...ADJ[from], Math.floor(rand() * T)]) for (let d = 0; d <= 4; d++) out.push([p, 2, pack(from, to, d)]);
  }
  if (o.phase === 3) for (let n = o.move.min - 1; n <= o.armies[o.move.from]; n++) out.push([p, 4, n]);
  out.push([p, 4, 0], [p, 4, 1]);
  if (o.phase === 4) {
    const own = [...Array(T).keys()].filter(t => o.owner[t] === p);
    for (const from of own) for (const to of own) {
      if (rand() < 0.4) continue;
      for (const n of [0, 1, o.armies[from] - 1, o.armies[from]]) out.push([p, 5, pack(from, to, n)]);
    }
    out.push([p, 5, pack(0, 41, 1)]);
  }
  return out;
}

// -- The lockstep games --------------------------------------------------
const failures = [];
const note = msg => { if (failures.length < 12) failures.push(msg); };
let actions = 0, legalChecks = 0, refusals = 0, stepChecks = 0, won = 0, capped = 0;

function stepByHand(h, seed) {
  let g = h, n = 0;
  const t0 = e.rk_turnno(h), theirs = e.rk_cur(h) === 0, setup = e.rk_phase(h) === 0;
  while (e.rk_done(g) === 0 && n < 2000) {
    if (n > 0 && !setup && e.rk_turnno(g) !== t0) break;
    if (n > 0 && setup && e.rk_phase(g) !== 0) break;
    if (n > 0 && setup && e.rk_cur(g) === 0) break;
    if (n > 0 && !theirs && e.rk_cur(g) === 0) break;
    const c = e.rk_ai(g);
    if (c < 0) break;
    const { kind, p, arg } = decode(c);
    const g2 = e.rk_act(g, p, kind, arg, seed + n * 7919);
    if (g2 === g) break;
    g = g2; n++;
  }
  return g;
}
const allSlots = h => [...Array(280)].map((_, i) => e.rk_slot(h, i)).join(',');

function playGame(seed, np, engineShare, legalEvery, maxActions) {
  e.__heap_reset();
  const rand = makeRand(seed);
  let h = e.rk_new(seed, np);
  const owner = [...Array(T)].map((_, t) => e.rk_owner(h, t));
  // Rule 2's deal: round the table, one army each.
  const counts = [...Array(np)].map((_, p) => owner.filter(q => q === p).length);
  const want = [...Array(np)].map((_, p) => Math.floor(T / np) + (p < T % np ? 1 : 0));
  if (counts.join() !== want.join()) { note(`seed ${seed}: dealt ${counts}, round the table is ${want}`); return; }
  const deck = [...Array(44)].map((_, k) => e.rk_deck(h, k));
  if ([...deck].sort((a, b) => a - b).join() !== [...Array(44).keys()].join()) { note(`seed ${seed}: the deck is not the 44 cards`); return; }
  const o = {
    np, cur: 0, turn: 0, over: false, winner: -1, phase: 0, place: 0, trades: 0,
    conquered: false, bonus: false, elim: false, move: { from: -1, to: -1, min: 0 },
    owner, armies: owner.map(() => 1), setup: want.map(c => START[np] - c),
    holder: [...Array(44)].map(() => -2), deck, discard: [],
  };
  const d0 = differ(snapshot(h, np), oracleView(o));
  if (d0) { note(`seed ${seed}: the opening differs: ${d0}`); return; }
  for (let n = 0; n < maxActions && !o.over; n++) {
    if (n % legalEvery === 0) {
      for (const [p, kind, arg] of probes(o, rand)) {
        legalChecks++;
        const want = legal(o, p, kind, arg), got = e.rk_legal(h, p, kind, arg) === 1;
        if (want !== got) { note(`seed ${seed} action ${n}: seat ${p} kind ${kind} arg ${arg} (${unpack(arg)}): engine ${got ? 'allows' : 'refuses'}, rules ${want ? 'allow' : 'refuse'} (phase ${o.phase})`); return; }
        if (!want && rand() < 0.01) {
          refusals++;
          if (e.rk_act(h, p, kind, arg, 7) !== h) { note(`seed ${seed} action ${n}: a refused action changed the state`); return; }
        }
      }
    }
    if (rand() < 0.03) {
      stepChecks++;
      const s = 1 + Math.floor(rand() * 2 ** 30);
      if (allSlots(e.rk_step(h, s)) !== allSlots(stepByHand(h, s))) { note(`seed ${seed} action ${n}: a step is not its choices one at a time`); return; }
    }
    let p, kind, arg;
    const ai = e.rk_ai(h);
    if (ai >= 0 && rand() < engineShare) ({ kind, p, arg } = decode(ai));
    else {
      const menu = probes(o, rand).filter(([q, k, a]) => legal(o, q, k, a));
      if (menu.length === 0) { note(`seed ${seed}: nobody can act and the game is not over (phase ${o.phase})`); return; }
      [p, kind, arg] = menu[Math.floor(rand() * menu.length)];
    }
    const seedA = 1 + Math.floor(rand() * 2 ** 30);
    const h2 = e.rk_act(h, p, kind, arg, seedA);
    actions++;
    if (h2 === h) { note(`seed ${seed} action ${n}: the engine refused seat ${p} kind ${kind} arg ${arg}, which it called legal`); return; }
    const deckAfter = [...Array(e.rk_decklen(h2))].map((_, k) => e.rk_deck(h2, k));
    const eng = {
      dice: () => [0, 1, 2, 3, 4].map(k => e.rk_die(h2, k)),
      // A reshuffle happens inside the draw that needs it, so the new deck
      // is the card that draw handed out followed by what is left.
      deck: () => [...Array(44).keys()].filter(c => e.rk_holder(h2, c) === o.cur && o.holder[c] !== o.cur).concat(deckAfter),
    };
    apply(o, p, kind, arg, eng);
    if (o.reshuffleBad) { note(`seed ${seed} action ${n}: ${o.reshuffleBad}`); return; }
    const d = differ(snapshot(h2, np), oracleView(o));
    if (d) { note(`seed ${seed} action ${n} (seat ${p} kind ${kind} arg ${unpack(arg)}): ${d}`); return; }
    h = h2;
  }
  if (o.over) won++; else capped++;
}

const t0 = Date.now();
for (let g = 1; g <= 24; g++) playGame(g, 2 + (g % 3), 0.9, 1, 4000);
for (let g = 1; g <= 16; g++) playGame(1000 + g, 4, 0.6, 2, 4000);
for (let g = 1; g <= 12; g++) playGame(2000 + g, 3, 0.3, 4, 3000);
ok('the engine and the rules agree after every action, and on every legal set',
  failures.length === 0,
  failures.length ? failures.slice(0, 4).join('; ') : `${actions} actions, ${legalChecks} legal-set questions, ${refusals} refusals, ${((Date.now() - t0) / 1000).toFixed(0)} s`);
ok('a step is the engine\'s choices one at a time', failures.length === 0 && stepChecks > 20, `${stepChecks} positions`);
ok('control: games were won outright', won > 10, `${won} won, ${capped} stopped at the action cap`);

const CLASSES = [
  'setup: an army placed', 'setup: finished, the first turn begins',
  ...CONTINENTS.map(([n]) => `continent bonus: ${n}`),
  'set: three of a kind', 'set: one of each', 'set: with a wild card',
  'trade: number 1', 'trade: number 2', 'trade: number 3', 'trade: number 4', 'trade: number 5', 'trade: number 6',
  'trade: past the sixth, five more each', 'trade: 2 armies on a held territory', 'trade: no second territory bonus in a turn',
  'cards: five or more, must trade', 'cards: drawn for a conquest', 'cards: the discard pile reshuffled into the deck',
  'battle: 1 dice against 1', 'battle: 2 dice against 1', 'battle: 3 dice against 1',
  'battle: 1 dice against 2', 'battle: 2 dice against 2', 'battle: 3 dice against 2', 'battle: a tie goes to the defender',
  'conquest', 'move in: the least', 'move in: all but one', 'move in: between',
  'elimination: a player knocked out', 'elimination: cards taken', 'elimination: six or more cards, trade at once',
  'trade: forced after an elimination', 'attack: stopped',
  'fortify: next door', 'fortify: through a chain of own territories', 'turn: ended without fortifying',
  'game: one player holds the world',
];
const missed = CLASSES.filter(c => !seen.has(c));
ok('control: every class of the rules was reached', missed.length === 0,
  missed.length ? `never reached: ${missed.join('; ')}` : `${CLASSES.length} classes`);

// -- The dice ------------------------------------------------------------
{
  const faces = [0, 0, 0, 0, 0, 0, 0];
  let pairs = 0, same = 0;
  // One position, set up: seat 0 attacking (slot 5, the phase), from a
  // territory of 10 armies into a neighbour of 5 (slots 40 + 2t + 1).
  e.__heap_reset();
  let base = e.rk_poke(e.rk_new(9, 2), 5, 2);
  let found = null;
  for (let a = 0; a < T && !found; a++) for (const b of ADJ[a]) {
    if (e.rk_owner(base, a) === 0 && e.rk_owner(base, b) === 1) { found = [a, b]; break; }
  }
  base = e.rk_poke(e.rk_poke(base, 40 + found[0] * 2 + 1, 10), 40 + found[1] * 2 + 1, 5);
  for (let t = 0; t < 400; t++) {
    const r = e.rk_act(base, 0, 2, pack(found[0], found[1], 3), 1 + t * 7919);
    const d = [0, 1, 2, 3, 4].map(k => e.rk_die(r, k));
    for (const x of d) faces[x]++;
    pairs++; if (d[0] === d[1]) same++;
  }
  const total = faces.slice(1).reduce((a, b) => a + b, 0);
  ok('every face comes up about one time in six', total > 1500 && faces.slice(1).every(f => Math.abs(f / total - 1 / 6) < 0.03),
    faces.slice(1).join(','));
  ok('two of the attacker\'s dice match about one time in six', pairs > 300 && same / pairs > 0.1 && same / pairs < 0.23,
    `${same} of ${pairs}`);
}

// -- The board -----------------------------------------------------------
{
  const bad = [];
  let edges = 0;
  for (let a = 0; a < T; a++) for (let b = 0; b < T; b++) {
    if (adjacent(a, b) !== adjacent(b, a)) bad.push(`${NAMES[a]}-${NAMES[b]} one way in the transcription`);
    if ((e.rk_adj(a, b) === 1) !== adjacent(a, b)) bad.push(`${NAMES[a]}-${NAMES[b]}: engine ${e.rk_adj(a, b)}`);
    if (a < b && adjacent(a, b)) edges++;
  }
  if (edges !== 83) bad.push(`${edges} borders`);
  for (let c = 0; c < 6; c++) {
    const [name, bonus, lands] = CONTINENTS[c];
    if (e.rk_contbonus(c) !== bonus) bad.push(`${name} bonus ${e.rk_contbonus(c)}`);
    for (const l of lands) if (e.rk_cont(idx(l)) !== c) bad.push(`${l} in continent ${e.rk_cont(idx(l))}`);
  }
  const symbols = [0, 0, 0, 0];
  for (let c = 0; c < 44; c++) symbols[e.rk_symbol(c)]++;
  if (symbols.join() !== '14,14,14,2') bad.push(`symbols ${symbols}`);
  ok('the map: 42 territories, 83 borders, six continents at 5, 2, 5, 3, 7, 2, and 14 of each symbol with 2 wilds',
    bad.length === 0, bad.length ? bad.slice(0, 3).join('; ') : '83 borders');
  ok('the engine answers no border off the map', e.rk_adj(-1, 0) === 0 && e.rk_adj(0, 42) === 0 && e.rk_adj(5, 5) === 0);
  ok('the turn cap is 400', e.rk_cap() === 400, e.rk_cap());
}

// -- The page ------------------------------------------------------------
{
  const { GAMES } = await import('../landing/web/games/arcade.js');
  const g = GAMES.find(x => x.id === 'risk');
  const bad = [];
  let positions = 0, finished = 0;
  const used = new Set();
  const marked = (h, sel) => g.view(e, h, false, sel).cells
    .filter(c => c.cls.split(' ').includes('movable')).map(c => c.i).sort((a, b) => a - b).join(',');
  for (let seed = 1; seed <= 16 && bad.length === 0; seed++) {
    e.__heap_reset();
    const rand = makeRand(700 + seed);
    const r = () => 1 + Math.floor(rand() * 2 ** 30);
    let h = g.boot(e, seed);
    for (let n = 0; n < 5000 && !g.done(e, h) && bad.length === 0; n++) {
      if (g.turn(e, h) !== 0) { h = g.step(e, h, r); continue; }
      positions++;
      const ph = e.rk_phase(h), options = [];
      // Placing: the ringed territories are exactly where an army may go,
      // and a click places one.
      if (ph === 0 || ph === 1 || ph === 5) {
        const want = [...Array(T).keys()].filter(t => e.rk_legal(h, 0, 0, t) === 1).join(',');
        if (marked(h, null) !== want) bad.push(`seed ${seed}: placing rings ${marked(h, null)}, the engine allows ${want}`);
        for (const t of want ? want.split(',').map(Number) : []) options.push(() => { used.add('place'); return g.move(e, h, t, { sel: null, rand: r }).handle; });
      }
      // Attacking: the ringed sources are exactly the territories with a
      // legal attack, and a held source rings exactly its legal targets.
      if (ph === 2) {
        const froms = [...Array(T).keys()].filter(a => [...ADJ[a]].some(b => e.rk_legal(h, 0, 2, pack(a, b, 1)) === 1));
        if (marked(h, null) !== froms.join(',')) bad.push(`seed ${seed}: attack sources ring ${marked(h, null)}, the engine allows ${froms}`);
        for (const a of froms) {
          const pick = g.move(e, h, a, { sel: null, rand: r });
          if (!pick || pick.sel !== a) bad.push(`seed ${seed}: clicking source ${a} did not hold it`);
          const targets = [...ADJ[a]].filter(b => e.rk_legal(h, 0, 2, pack(a, b, 1)) === 1).sort((x, y) => x - y).join(',');
          if (marked(h, a) !== targets) bad.push(`seed ${seed}: holding ${a} rings ${marked(h, a)}, the engine allows ${targets}`);
          for (const b of targets.split(',').map(Number)) options.push(() => { used.add('attack'); return g.move(e, h, b, { sel: a, rand: r }).handle; });
        }
      }
      if (ph === 4) {
        const froms = [...Array(T).keys()].filter(a => [...Array(T).keys()].some(b => e.rk_legal(h, 0, 5, pack(a, b, 1)) === 1));
        if (marked(h, null) !== froms.join(',')) bad.push(`seed ${seed}: fortify sources ring ${marked(h, null)}, the engine allows ${froms}`);
        for (const a of froms.slice(0, 3)) {
          const targets = [...Array(T).keys()].filter(b => e.rk_legal(h, 0, 5, pack(a, b, 1)) === 1).join(',');
          if (marked(h, a) !== targets) bad.push(`seed ${seed}: fortifying from ${a} rings ${marked(h, a)}, the engine allows ${targets}`);
          for (const b of targets.split(',').filter(Boolean).map(Number)) options.push(() => { used.add('fortify'); return g.move(e, h, b, { sel: a, rand: r }).handle; });
        }
      }
      // Each acting button is one engine action: enabled exactly when the
      // engine allows it, and doing exactly what the engine does.
      const most = ph === 3 ? e.rk_armies(h, e.rk_mfrom(h)) - 1 : 0, least = ph === 3 ? e.rk_mmin(h) : 0;
      const BUTTON = {
        'Trade cards': [1, e.rk_firstset(h, 0)], 'Stop attacking': [3, 0], 'End turn': [6, 0],
        'Move in: least': [4, least], 'Move in: half': [4, Math.max(least, Math.floor(most / 2))], 'Move in: all': [4, most],
      };
      for (const a of g.actions) {
        const on = a.enabled(e, h, null, null);
        const act = BUTTON[a.label];
        if (act) {
          const want = e.rk_legal(h, 0, act[0], act[1]) === 1 && (act[0] !== 4 || ph === 3);
          if (on !== want) bad.push(`seed ${seed}: '${a.label}' ${on ? 'enabled' : 'disabled'}, engine ${want ? 'allows' : 'refuses'}`);
          if (!on) continue;
          const out = a.run(e, h, r);
          if (allSlots(out.handle) !== allSlots(e.rk_act(h, 0, act[0], act[1], 0))) bad.push(`seed ${seed}: '${a.label}' is not the engine's action`);
          options.push(() => { used.add(a.label.startsWith('Move in') ? 'Move in' : a.label); return a.run(e, h, r).handle; });
        } else if (on) {
          const out = a.run(e, h, r);
          if (out.handle !== h) bad.push(`seed ${seed}: '${a.label}' changed the board`);
          used.add('a setting');
        }
      }
      // A move-in or a forced trade has no board click, so the buttons must
      // offer it.
      if (ph === 3 && !g.actions.some(a => a.label.startsWith('Move in') && a.enabled(e, h, null, null))) bad.push(`seed ${seed}: a move-in with no button`);
      if ((ph === 1 || ph === 5) && e.rk_hand(h, 0) >= 5 && !g.actions.some(a => a.label === 'Trade cards' && a.enabled(e, h, null, null))) bad.push(`seed ${seed}: a forced trade with no button`);
      if (options.length === 0) { bad.push(`seed ${seed}: seat 0 has the choice and the page offers nothing (phase ${ph})`); break; }
      h = options[Math.floor(rand() * options.length)]();
    }
    if (g.done(e, h)) finished++;
  }
  ok('page: the ringed territories and the buttons are the engine\'s legal set, and seat 0 always has a move',
    bad.length === 0, bad.length ? bad.slice(0, 3).join('; ') : `${positions} positions, ${finished} of 16 games over for seat 0`);
  const every = ['place', 'attack', 'fortify', 'Trade cards', 'Stop attacking', 'End turn', 'Move in', 'a setting'];
  ok('control: the page drove every kind of choice', every.every(l => used.has(l)),
    `missing: ${every.filter(l => !used.has(l)).join(', ') || 'none'}`);
}

console.log(fail === 0
  ? `\nPASS: Risk agrees with its rules (${pass} arms).`
  : `\nFAIL: ${fail} of ${pass + fail} arms.`);
process.exitCode = fail === 0 ? 0 : 1;
