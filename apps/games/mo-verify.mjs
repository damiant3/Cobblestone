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

console.log(fail === 0
  ? `\nPASS: Monopoly's ownership ledger agrees with itself (${pass} arms).`
  : `\nFAIL: ${fail} of ${pass + fail} arms.`);
process.exitCode = fail === 0 ? 0 : 1;
