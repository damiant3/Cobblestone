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
    h = e.mo_step(h, seed * 7919 + turns);
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
  // the STATE, and it is rare because nothing assembles a colour group
  // deliberately (games-backlog GAME-8: no trading). `mono-loop` resolves an
  // undecided game by capping the turns and awarding it to the richest, so a
  // page stepping the state does the same with `mo_cap` and `mo_richest`.
  // What must hold is that an unfinished game is genuinely undecided: every
  // seat still solvent, and a richest player nameable.
  const undecided = [];
  for (let seed = 1; seed <= 12; seed++) {
    const players = 2 + (seed % 3);
    let h = e.mo_new(seed, players), n = 0;
    while (e.mo_done(h) === 0 && n++ < 800) h = e.mo_step(h, seed * 7919 + n);
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
    while (e.mo_done(h) === 0 && n++ < 800) h = e.mo_step(h, seed * 31 + n);
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
  // does: with no trading, most games do not (GAME-8).
  for (let seed = 1; seed <= 40; seed++) {
    let h = e.mo_new(seed, 2), n = 0;
    while (e.mo_done(h) === 0 && n++ < 800) h = e.mo_step(h, seed * 101 + n);
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
  while (e.mo_done(h) === 0 && n++ < 200 && owners(h).every(o => o === -1)) h = e.mo_step(h, n);
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
    h = e.mo_step(h, 1000 + i);
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
// The watch-only runner is a POLICY over the same turn: roll, and take what
// you were offered if you can afford it. If that stops being true the page
// and the self-playing game have drifted apart, which is the failure the
// whole split risks and no other arm here would notice.
{
  const bad = [];
  for (let seed = 1; seed <= 60; seed++) {
    const base = e.mo_new(seed, 4);
    const byStep = e.mo_step(base, seed);
    const rolled = e.mo_roll(base, seed);
    const byHand = e.mo_phase(rolled) === 1 ? e.mo_take(rolled) : rolled;
    const shape = h => [e.mo_cur(h), e.mo_turn(h), ...cashes(h), ...owners(h)].join(',');
    if (shape(byStep) !== shape(byHand)) {
      bad.push(`seed ${seed}: step and roll-then-take disagree`);
    }
  }
  ok('the self-playing turn is the same turn a person takes',
     bad.length === 0, bad.length ? bad.slice(0, 2).join('; ') : '60 turns');
}

console.log(fail === 0
  ? `\nPASS: Monopoly's ownership ledger agrees with itself (${pass} arms).`
  : `\nFAIL: ${fail} of ${pass + fail} arms.`);
process.exitCode = fail === 0 ? 0 : 1;
