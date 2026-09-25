// Grade the Monopoly wasm module.
//
// Money is not conserved in Monopoly, because the bank both pays and
// receives, so the arm that carries this file is the OWNERSHIP LEDGER
// instead. Every property records an owner, and every player carries a list
// of the properties they own. Those two are written separately, in
// `mono-buy-property`, and they can disagree: a property owned by nobody
// while a player lists it, or owned by two players at once. Neither is
// visible from watching a game, and both make the rent wrong for the rest
// of it.
//
// Beside it: a position is always a square on the board, cash is never
// negative because `mono-pay` clamps to what a player has, and a bankrupt
// player keeps no properties.
//
// Usage: node apps/games/mo-verify.mjs [path/to/monopoly.wasm]

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const wasmPath = process.argv[2] ||
  join(here, '..', 'landing', 'web', 'games', 'monopoly.wasm');

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

const owners = h => [...Array(e.mo_props(h))].map((_, i) => e.mo_owner(h, i));
const settle = (h, r) => (e.mo_twant(h) >= 0 ? e.mo_resume(e.mo_decline(h), r) : h);
const cashes = h => [...Array(e.mo_players(h))].map((_, p) => e.mo_cash(h, p));
const ownedBy = (h, p) => [...Array(e.mo_owned(h, p))].map((_, k) => e.mo_ownedat(h, p, k));

console.log(`mo-verify ${wasmPath}`);

// -- The setup ------------------------------------------------------------
const s0 = e.mo_new(23, 4);
ok('four players seated', e.mo_players(s0) === 4, e.mo_players(s0));
ok('every property starts unowned', owners(s0).every(o => o === -1),
   owners(s0).filter(o => o !== -1).length + ' owned');
ok('there are properties to buy', e.mo_props(s0) > 0, e.mo_props(s0));
ok('everyone starts with the same cash and none owned',
   new Set(cashes(s0)).size === 1 && [0, 1, 2, 3].every(p => e.mo_owned(s0, p) === 0),
   cashes(s0).join(','));
ok('everyone starts on GO, out of jail',
   [0, 1, 2, 3].every(p => e.mo_pos(s0, p) === 0 && e.mo_jail(s0, p) === 0));
ok('not over', e.mo_done(s0) === 0);

// -- The copy arm ---------------------------------------------------------
{
  const before = JSON.stringify([cashes(s0), owners(s0)]);
  const next = e.mo_step(s0, 5);
  ok('stepping answers a different state', next !== s0);
  ok('THE COPY ARM: the state stepped from is untouched',
     JSON.stringify([cashes(s0), owners(s0)]) === before);
}

// -- The ownership ledger, over whole games ------------------------------
function playGame(seed, players) {
  let h = e.mo_new(seed, players);
  const nProps = e.mo_props(h);
  let turns = 0;
  while (e.mo_done(h) === 0 && turns < 800) {
    turns++;
    h = settle(e.mo_step(h, seed * 7919 + turns), seed * 7919 + turns);
    const np = e.mo_players(h);
    const o = owners(h);
    // Every owner is a real seat or nobody.
    for (let i = 0; i < nProps; i++) {
      if (o[i] !== -1 && (o[i] < 0 || o[i] >= np)) {
        return { bad: `turn ${turns}: property ${i} owned by ${o[i]}`, h };
      }
    }
    // The two halves of the ledger must agree, both ways.
    const claimed = new Map();
    for (let p = 0; p < np; p++) {
      for (const idx of ownedBy(h, p)) {
        if (idx < 0 || idx >= nProps) {
          return { bad: `turn ${turns}: player ${p} lists property ${idx}`, h };
        }
        if (claimed.has(idx)) {
          return { bad: `turn ${turns}: property ${idx} listed by players ${claimed.get(idx)} and ${p}`, h };
        }
        claimed.set(idx, p);
        if (o[idx] !== p) {
          return { bad: `turn ${turns}: player ${p} lists property ${idx}, which records owner ${o[idx]}`, h };
        }
      }
    }
    for (let i = 0; i < nProps; i++) {
      if (o[i] !== -1 && claimed.get(i) !== o[i]) {
        return { bad: `turn ${turns}: property ${i} records owner ${o[i]}, who does not list it`, h };
      }
    }
    // Positions and cash stay sane.
    for (let p = 0; p < np; p++) {
      const pos = e.mo_pos(h, p);
      if (pos < 0 || pos > 39) return { bad: `turn ${turns}: player ${p} at square ${pos}`, h };
      if (e.mo_cash(h, p) < 0) return { bad: `turn ${turns}: player ${p} has ${e.mo_cash(h, p)}`, h };
    }
    if (e.mo_cur(h) < 0 || e.mo_cur(h) >= np) {
      return { bad: `turn ${turns}: current player ${e.mo_cur(h)}`, h };
    }
  }
  return { h, turns };
}

{
  let broke = null, finished = 0, totalTurns = 0, everBought = 0;
  for (let seed = 1; seed <= 12 && !broke; seed++) {
    const players = 2 + (seed % 3);
    const r = playGame(seed, players);
    if (r.bad) { broke = `seed ${seed} (${players}p): ${r.bad}`; break; }
    totalTurns += r.turns;
    if (e.mo_done(r.h) === 1) finished++;
    everBought += owners(r.h).filter(o => o !== -1).length;
  }
  ok('the two halves of the ownership ledger agree on every turn of 12 games',
     broke === null, broke ?? `12 games, ${totalTurns} turns`);
  ok('control: properties were actually bought', everBought > 0,
     `${everBought} owned across the finished games`);
  ok('control: the games ran long enough', totalTurns > 200, totalTurns);
  // NOT "every game ends". Bankruptcy is the only ending this engine has in
  // the STATE; `mono-loop` resolves an undecided game by capping the turns
  // and awarding it to the richest, so a page stepping the state does the
  // same with `mo_cap` and `mo_richest`. What must hold is that an unfinished
  // game is genuinely undecided: every seat still solvent, and a richest
  // player nameable.
  const undecided = [];
  for (let seed = 1; seed <= 12; seed++) {
    const players = 2 + (seed % 3);
    let h = e.mo_new(seed, players), n = 0;
    while (e.mo_done(h) === 0 && n++ < 800) h = settle(e.mo_step(h, seed * 7919 + n), seed * 7919 + n);
    if (e.mo_done(h) !== 1) {
      const rich = e.mo_richest(h);
      if (rich < 0 || rich >= e.mo_players(h)) undecided.push(`seed ${seed}: richest ${rich}`);
      for (let p = 0; p < e.mo_players(h); p++) {
        if (e.mo_cash(h, p) < 0) undecided.push(`seed ${seed}: player ${p} in debt but game runs on`);
      }
    }
  }
  ok('a game with no bankruptcy is still decidable on the richest seat',
     undecided.length === 0,
     undecided.length ? undecided.slice(0, 3).join('; ') : `${finished} of 12 ended by bankruptcy, the rest on cash`);
  ok('the turn cap the page should use is the one the engine uses',
     e.mo_cap() === 601, e.mo_cap());
}

// -- A finished game names a solvent winner ------------------------------
{
  const bad = [];
  for (let seed = 1; seed <= 12; seed++) {
    let h = e.mo_new(seed, 3), n = 0;
    while (e.mo_done(h) === 0 && n++ < 800) h = settle(e.mo_step(h, seed * 31 + n), seed * 31 + n);
    if (e.mo_done(h) === 1) {
      const w = e.mo_winner(h);
      if (w < 0 || w >= e.mo_players(h)) bad.push(`seed ${seed}: winner ${w}`);
      else if (e.mo_cash(h, w) < 0) bad.push(`seed ${seed}: winner holds ${e.mo_cash(h, w)}`);
    }
  }
  ok('a finished game names a seat that is not in debt',
     bad.length === 0, bad.length ? bad.slice(0, 3).join('; ') : '12 games');
}

// -- Refusals -------------------------------------------------------------
ok('a seat off the table is refused',
   e.mo_cash(s0, 9) === -1 && e.mo_pos(s0, -1) === -1 && e.mo_owned(s0, 9) === -1);
ok('a property off the board is refused',
   e.mo_owner(s0, 999) === -2 && e.mo_cost(s0, -1) === -1);
ok('an owned-list index off the end is refused', e.mo_ownedat(s0, 0, 0) === -1);
ok('a finished game refuses another turn', (() => {
  // Find a seed that actually ends in bankruptcy rather than assuming one
  // does.
  for (let seed = 1; seed <= 40; seed++) {
    let h = e.mo_new(seed, 2), n = 0;
    while (e.mo_done(h) === 0 && n++ < 800) h = settle(e.mo_step(h, seed * 101 + n), seed * 101 + n);
    if (e.mo_done(h) === 1) return e.mo_step(h, 1) === h;
  }
  return false;   // no bankruptcy in forty games would itself be a finding
})());

// -- Controls -------------------------------------------------------------
ok('control: two new games are different handles', e.mo_new(1, 2) !== e.mo_new(1, 2));
ok('control: the board has the colours a Monopoly board has',
   new Set([...Array(e.mo_props(s0))].map((_, i) => e.mo_color(s0, i))).size >= 6,
   new Set([...Array(e.mo_props(s0))].map((_, i) => e.mo_color(s0, i))).size + ' colours');
ok('control: the ledger reader can see an owner change', (() => {
  let h = e.mo_new(3, 2), n = 0;
  while (e.mo_done(h) === 0 && n++ < 200 && owners(h).every(o => o === -1)) h = settle(e.mo_step(h, n), n);
  return owners(h).some(o => o !== -1);
})());

// -- EVERY SEAT GETS A TURN -----------------------------------------------
// Nothing above could see a player being SKIPPED. The ledger balances, the
// cash balances, the turn counter climbs and the game still finishes, all
// of which are true of a game two of whose four players never move: this
// wrapper called `mono-advance-turn` on top of a `mono-do-turn` that had
// already advanced, so the page dealt every other seat out of its own game.
// The board cycles, so the arm is that it cycles.
{
  const seen = new Map();
  const order = [];
  let h = e.mo_new(23, 4);
  for (let i = 0; i < 40 && e.mo_done(h) !== 1; i++) {
    const who = e.mo_cur(h);
    order.push(who);
    seen.set(who, (seen.get(who) || 0) + 1);
    h = settle(e.mo_step(h, 1000 + i), 1000 + i);
  }
  const counts = [0, 1, 2, 3].map(p => seen.get(p) || 0);
  ok('every seat comes round, and in order', counts.every(c => c >= 9),
     `turns taken: ${counts.join(', ')}`);
  ok('the seat advances by exactly one each step',
     order.every((w, k) => k === 0 || w === (order[k - 1] + 1) % 4),
     order.slice(0, 12).join(''));
  // The turn NUMBER has to move with the seat, or a page's turn cap counts
  // something other than turns.
  const t0 = e.mo_turn(e.mo_new(23, 4));
  const t1 = e.mo_turn(e.mo_step(e.mo_new(23, 4), 5));
  ok('one step is one turn', t1 - t0 === 1, `${t0} then ${t1}`);
}

// -- THE TURN SPLITS WHERE THE DECISION IS --------------------------------
// A turn is a roll and, when the square is worth deciding about, a choice.
// These arms are the difference between a game you watch and one you play.
{
  const bad = [];
  let offers = 0, bought = 0, passed = 0, noOffer = 0;
  for (let seed = 1; seed <= 120; seed++) {
    let h = e.mo_new(seed, 4);
    if (e.mo_canroll(h) !== 1) { bad.push(`seed ${seed}: cannot roll at the open`); continue; }
    if (e.mo_candecide(h, e.mo_cur(h)) === 1) bad.push(`seed ${seed}: a decision before a roll`);
    // Rolling must not decide anything for you.
    const who = e.mo_cur(h);
    const r = e.mo_roll(h, seed);
    if (e.mo_phase(r) === 1) {
      offers++;
      const pi = e.mo_offered(r);
      if (pi < 0) bad.push(`seed ${seed}: phase 1 with nothing pending`);
      if (e.mo_owner(r, pi) !== -1) bad.push(`seed ${seed}: offered a property somebody owns`);
      if (e.mo_cur(r) !== who) bad.push(`seed ${seed}: the turn passed while a decision was open`);
      const cost = e.mo_offercost(r);
      if (cost !== e.mo_cost(r, pi)) bad.push(`seed ${seed}: the offer quotes ${cost}, the board says ${e.mo_cost(r, pi)}`);
      const cashBefore = e.mo_cash(r, who);

      // Taking it: the property changes hands and it costs exactly the price.
      const t = e.mo_take(r);
      bought++;
      if (e.mo_owner(t, pi) !== who) bad.push(`seed ${seed}: bought and the deed did not move`);
      if (e.mo_cash(t, who) !== cashBefore - cost) {
        bad.push(`seed ${seed}: paid ${cashBefore - e.mo_cash(t, who)} for a ${cost} property`);
      }
      if (e.mo_phase(t) !== 0) bad.push(`seed ${seed}: buying left the turn open`);
      if (e.mo_cur(t) === who) bad.push(`seed ${seed}: buying did not end the turn`);

      // Leaving it: nothing moves but the turn.
      const l = e.mo_leave(r);
      passed++;
      if (e.mo_owner(l, pi) !== -1) bad.push(`seed ${seed}: passed and the deed moved anyway`);
      if (e.mo_cash(l, who) !== cashBefore) bad.push(`seed ${seed}: passing cost money`);
      if (e.mo_cur(l) === who) bad.push(`seed ${seed}: passing did not end the turn`);

      // The handle the caller still holds is the position it was.
      if (e.mo_phase(r) !== 1 || e.mo_owner(r, pi) !== -1) {
        bad.push(`seed ${seed}: deciding wrote through the caller's handle`);
      }
    } else {
      noOffer++;
      if (e.mo_cur(r) === who) bad.push(`seed ${seed}: nothing to decide and the turn did not end`);
      if (e.mo_candecide(r, who) === 1) bad.push(`seed ${seed}: a decision offered with no offer`);
    }
  }
  ok('rolling stops at the decision, and both answers end the turn',
     bad.length === 0, bad.length ? bad.slice(0, 3).join('; ') : '120 openings');
  // L-VACUOUS: every claim above about an offer is silent unless an offer
  // was actually made, and the no-offer claims unless one was not.
  ok('control: both branches were reached', offers > 0 && noOffer > 0,
     `${offers} offers (${bought} taken, ${passed} passed), ${noOffer} turns with nothing to decide`);
}

// -- ONE MODEL, NOT TWO ---------------------------------------------------
// The watch-only runner is a POLICY over the same turn: trade, roll, and take
// what you were offered if you can afford it. Each game is walked forward to
// a position where the seat to move trades, alternating with the position
// before it, because at the opening nobody owns a deed and a trade cannot
// fire.
{
  const bad = [];
  let tradedTurns = 0;
  for (let seed = 1; seed <= 60; seed++) {
    let base = e.mo_new(seed, 4), prev = base;
    for (let n = 0; n < 400 && e.mo_done(base) === 0; n++) {
      if (owners(e.mo_trade(base)).join(',') !== owners(base).join(',')) break;
      prev = base;
      base = settle(e.mo_step(base, seed * 13 + n), seed * 13 + n);
    }
    if (seed % 2 === 0) base = prev;
    if (e.mo_done(base) === 1) continue;
    const traded = e.mo_trade(base);
    if (owners(traded).join(',') !== owners(base).join(',')) tradedTurns++;
    const byStep = settle(e.mo_step(base, seed), seed);
    const rolled = e.mo_roll(traded, seed);
    const byHand = e.mo_phase(rolled) === 1 ? e.mo_take(rolled) : rolled;
    const shape = h => [e.mo_cur(h), e.mo_turn(h), ...cashes(h), ...owners(h)].join(',');
    if (shape(byStep) !== shape(byHand)) {
      bad.push(`seed ${seed}: step and trade-roll-take disagree`);
    }
  }
  ok('the self-playing turn is the same turn a person takes',
     bad.length === 0, bad.length ? bad.slice(0, 2).join('; ') : `60 positions, ${tradedTurns} with a trade`);
  ok('control: a trade fired in at least one of those positions', tradedTurns > 0, tradedTurns);
}

// -- TRADING (GAME-8) -----------------------------------------------------
// A trade moves deeds and cash between two seats and nothing else: the bank
// takes no part, so the table's cash is conserved across `mo_trade`. A cash
// buy costs exactly twice the printed price, every trade completes a colour
// group for the seat that asked, and seat 0, the page's person, is never
// made to sell.
{
  const bad = [];
  let swaps = 0, buys = 0, seatZeroAsked = 0;
  const groupWhole = (h, p, c) => {
    let held = 0, size = 0;
    for (let i = 0; i < e.mo_props(h); i++) {
      if (e.mo_color(h, i) !== c) continue;
      size++;
      if (e.mo_owner(h, i) === p) held++;
    }
    return held === size;
  };
  for (let seed = 1; seed <= 40; seed++) {
    const players = 2 + (seed % 3);
    let h = e.mo_new(seed, players), n = 0;
    while (e.mo_done(h) === 0 && n++ < 601) {
      const who = e.mo_cur(h);
      const t = e.mo_trade(h);
      const o0 = owners(h), o1 = owners(t);
      const moved = o0.map((o, i) => [i, o, o1[i]]).filter(([, a, b]) => a !== b);
      if (moved.length > 0) {
        const c0 = cashes(h), c1 = cashes(t);
        const sum = a => a.reduce((x, y) => x + y, 0);
        if (sum(c0) !== sum(c1)) bad.push(`seed ${seed} turn ${n}: a trade changed the table's cash`);
        for (const [i, a] of moved) {
          if (a === 0 && who !== 0) bad.push(`seed ${seed} turn ${n}: seat 0 was made to sell deed ${i}`);
        }
        const bought = moved.filter(([, , b]) => b === who);
        if (bought.length !== 1) bad.push(`seed ${seed} turn ${n}: the asking seat gained ${bought.length} deeds`);
        else if (!groupWhole(t, who, e.mo_color(t, bought[0][0]))) {
          bad.push(`seed ${seed} turn ${n}: a trade that completed no group`);
        }
        if (moved.length === 2) swaps++;
        else if (moved.length === 1) {
          buys++;
          const paid = c0[who] - c1[who];
          if (paid !== 2 * e.mo_cost(h, moved[0][0])) {
            bad.push(`seed ${seed} turn ${n}: paid ${paid} for a ${e.mo_cost(h, moved[0][0])} deed`);
          }
        } else bad.push(`seed ${seed} turn ${n}: ${moved.length} deeds moved in one trade`);
      }
      // Never ask the protected seat: count the positions where only seat 0
      // held the deed someone wanted, so the refusal is seen to be reached.
      if (who !== 0 && moved.length === 0) {
        for (let c = 0; c < 8; c++) {
          let mine = 0, size = 0, other = -1;
          for (let i = 0; i < e.mo_props(h); i++) {
            if (e.mo_color(h, i) !== c) continue;
            size++;
            if (e.mo_owner(h, i) === who) mine++; else other = e.mo_owner(h, i);
          }
          if (mine === size - 1 && other === 0) seatZeroAsked++;
        }
      }
      h = settle(e.mo_step(h, seed * 7919 + n), seed * 7919 + n);
    }
  }
  ok('a trade conserves cash, costs twice the price, completes a group, and never takes seat 0\'s deed',
     bad.length === 0, bad.length ? bad.slice(0, 3).join('; ') : `${swaps} swaps, ${buys} buys over 40 games`);
  ok('control: both kinds of trade happened', swaps > 0 && buys > 0, `${swaps} swaps, ${buys} buys`);
  ok('control: seat 0 held a deed somebody wanted, and was not asked', seatZeroAsked > 0, seatZeroAsked);
}

// -- A TRADE OFFERED TO SEAT 0 (GAME-8) -----------------------------------
// A seat that wants the one deed of a colour seat 0 holds OFFERS it: the
// turn stops with the offer until it is answered. The offer names seat 0's
// deed, and the asking seat holds the rest of that colour. An accepted offer
// moves the deed to the asking seat and moves a deed back for a swap or twice
// the printed price for a buy, and no other deed or cash moves; a declined
// one changes nothing. Both answers, and both kinds, must happen.
{
  const bad = [];
  let accepted = 0, declined = 0, swapOffers = 0, buyOffers = 0;
  for (let seed = 1; seed <= 60; seed++) {
    const players = 2 + (seed % 3);
    let h = e.mo_new(seed, players), n = 0;
    while (e.mo_done(h) === 0 && n++ < 601) {
      h = e.mo_step(h, seed * 104729 + n);
      if (e.mo_twant(h) < 0) continue;
      const who = e.mo_cur(h), want = e.mo_twant(h), give = e.mo_tgive(h), price = e.mo_tprice(h);
      if (who === 0) bad.push(`seed ${seed} turn ${n}: seat 0 offered to itself`);
      if (e.mo_owner(h, want) !== 0) bad.push(`seed ${seed} turn ${n}: the wanted deed is not seat 0's`);
      let held = 0, size = 0;
      for (let i = 0; i < e.mo_props(h); i++) {
        if (e.mo_color(h, i) !== e.mo_color(h, want)) continue;
        size++;
        if (e.mo_owner(h, i) === who) held++;
      }
      if (held !== size - 1) bad.push(`seed ${seed} turn ${n}: the asking seat holds ${held} of ${size}`);
      if (give >= 0) {
        swapOffers++;
        if (price !== 0 || e.mo_owner(h, give) !== who) bad.push(`seed ${seed} turn ${n}: a swap priced ${price}, giving a deed owned by ${e.mo_owner(h, give)}`);
      } else {
        buyOffers++;
        if (price !== 2 * e.mo_cost(h, want)) bad.push(`seed ${seed} turn ${n}: priced ${price} for a ${e.mo_cost(h, want)} deed`);
      }
      const o0 = owners(h), c0 = cashes(h);
      const yes = ((seed + n) % 2) === 0;
      const t = yes ? e.mo_accept(h) : e.mo_decline(h);
      const o1 = owners(t), c1 = cashes(t);
      if (e.mo_twant(t) >= 0) bad.push(`seed ${seed} turn ${n}: the offer outlived its answer`);
      const moved = o0.map((o, i) => [i, o, o1[i]]).filter(([, a, b]) => a !== b);
      const sum = a => a.reduce((x, y) => x + y, 0);
      if (sum(c0) !== sum(c1)) bad.push(`seed ${seed} turn ${n}: an answer changed the table's cash`);
      if (!yes) {
        declined++;
        if (moved.length !== 0 || c0.some((c, p) => c !== c1[p])) bad.push(`seed ${seed} turn ${n}: a decline moved something`);
      } else {
        accepted++;
        const expect = give >= 0 ? [[want, 0, who], [give, who, 0]] : [[want, 0, who]];
        const same = moved.length === expect.length
          && expect.every(([i, a, b]) => moved.some(m => m[0] === i && m[1] === a && m[2] === b));
        if (!same) bad.push(`seed ${seed} turn ${n}: accepting moved ${JSON.stringify(moved)}`);
        if (c0[who] - c1[who] !== price || c1[0] - c0[0] !== price) {
          bad.push(`seed ${seed} turn ${n}: paid ${c0[who] - c1[who]}, received ${c1[0] - c0[0]}, price ${price}`);
        }
      }
      h = e.mo_resume(t, seed * 104729 + n);
    }
  }
  ok('an offer to seat 0 names its deed, prices a swap at 0 and a buy at twice, and its answer moves exactly that',
     bad.length === 0, bad.length ? bad.slice(0, 3).join('; ') : `${accepted} accepted, ${declined} declined`);
  ok('control: offers were accepted and declined, and both kinds were offered',
     accepted > 0 && declined > 0 && swapOffers > 0 && buyOffers > 0,
     `${accepted} accepted, ${declined} declined, ${swapOffers} swaps, ${buyOffers} buys offered`);
}

console.log(fail === 0
  ? `\nPASS: Monopoly's ownership ledger agrees with itself (${pass} arms).`
  : `\nFAIL: ${fail} of ${pass + fail} arms.`);
process.exitCode = fail === 0 ? 0 : 1;
