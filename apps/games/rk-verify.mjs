// Grade the Risk wasm module.
//
// Risk has a conservation law -- armies leave the board only through combat
// and arrive only through reinforcement -- and this campaign has learned
// that a conservation law is the thing a real defect hides behind. So the
// arms here are mostly structural and decidable instead.
//
// The map is a fixed graph, so symmetry, irreflexivity, connectivity and a
// minimum degree of one are all checkable without agreeing on what the map
// should be. The reinforcement rule is arithmetic the chapter states in its
// own opening paragraph. And the setup is the arm that matters: a game
// dealt from a seed must depend on the seed.
//
// Usage: node apps/games/rk-verify.mjs [path/to/risk.wasm]

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const wasmPath = process.argv[2] ||
  join(here, '..', 'landing', 'web', 'games', 'risk.wasm');

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

const N = 12;
const owners = h => [...Array(N)].map((_, i) => e.rk_owner(h, i));
const armies = h => [...Array(N)].map((_, i) => e.rk_armies(h, i));
const continentOf = t => Math.floor(t / 3);

console.log(`rk-verify ${wasmPath}`);

// -- The map --------------------------------------------------------------
{
  const adj = (a, b) => e.rk_adj(a, b) === 1;
  const bad = [];
  let edges = 0;
  for (let a = 0; a < N; a++) {
    if (adj(a, a)) bad.push(`${a} is adjacent to itself`);
    for (let b = 0; b < N; b++) {
      if (adj(a, b) !== adj(b, a)) bad.push(`${a}-${b} adjacency is not symmetric`);
      if (a < b && adj(a, b)) edges++;
    }
  }
  ok('the map is symmetric and no territory borders itself',
     bad.length === 0, bad.length ? bad.slice(0, 3).join('; ') : `${edges} edges`);

  const degree = [...Array(N)].map((_, a) =>
    [...Array(N)].map((_, b) => b).filter(b => adj(a, b)).length);
  ok('every territory has at least one neighbour, so none is unreachable',
     degree.every(d => d > 0), `degrees ${degree.join(',')}`);

  // Connectivity: a map in two pieces makes the game unwinnable from one of
  // them, and nothing in a play-through would say so.
  const seen = new Set([0]);
  const queue = [0];
  while (queue.length) {
    const a = queue.pop();
    for (let b = 0; b < N; b++) if (adj(a, b) && !seen.has(b)) { seen.add(b); queue.push(b); }
  }
  ok('the map is connected, so every territory can be conquered',
     seen.size === N, `${seen.size} of ${N} reachable from territory 0`);
  ok('control: a territory off the map borders nothing',
     e.rk_adj(12, 0) === 0 && e.rk_adj(-1, 0) === 0 && e.rk_adj(0, 12) === 0);
}

// -- The deal -------------------------------------------------------------
{
  const bad = [];
  for (const np of [2, 3, 4]) {
    for (let seed = 1; seed <= 20; seed++) {
      const h = e.rk_new(seed, np);
      const o = owners(h), a = armies(h);
      if (e.rk_np(h) !== np) bad.push(`np ${np} seed ${seed}: reports ${e.rk_np(h)}`);
      if (o.some(v => v < 0 || v >= np)) bad.push(`np ${np} seed ${seed}: owner off the table`);
      if (a.some(v => v !== 3)) bad.push(`np ${np} seed ${seed}: armies ${a.join(',')}`);
      if (e.rk_done(h) !== 0) bad.push(`np ${np} seed ${seed}: over before it started`);
      // Every player must hold something, or they are dead on turn zero.
      for (let p = 0; p < np; p++) {
        if (!o.includes(p)) bad.push(`np ${np} seed ${seed}: player ${p} holds nothing`);
        if (e.rk_total(h, p) !== o.filter(v => v === p).length * 3) {
          bad.push(`np ${np} seed ${seed}: player ${p} army total disagrees with the board`);
        }
      }
    }
  }
  ok('every deal gives twelve territories, three armies each, and nobody is dealt out',
     bad.length === 0, bad.length ? bad.slice(0, 3).join('; ') : '60 deals');
  ok('a territory off the board reads -1',
     e.rk_owner(e.rk_new(1, 2), 12) === -1 && e.rk_armies(e.rk_new(1, 2), -1) === -1);
  ok('control: the deal is an even split, so no player starts ahead',
     [2, 3, 4].every(np => {
       const o = owners(e.rk_new(5, np));
       const counts = [...Array(np)].map((_, p) => o.filter(v => v === p).length);
       return Math.max(...counts) - Math.min(...counts) <= 1;
     }));
}

// -- THE SEED ARM ---------------------------------------------------------
// A dealt game must depend on what it was dealt from. This is the arm that
// caught GAME-29: risk-assign-loop threads an Rng through every iteration
// and assigns `owner = rng-mod i np`, which is the territory INDEX, so the
// board was identical for every seed and the Rng was never read.
{
  const boards = new Map();
  for (let seed = 1; seed <= 40; seed++) {
    const key = owners(e.rk_new(seed, 2)).join(',');
    boards.set(key, (boards.get(key) || 0) + 1);
  }
  ok('forty seeds do not all deal the same board',
     boards.size > 1, `${boards.size} distinct openings from 40 seeds`);
  ok('control: the same seed deals the same board twice',
     owners(e.rk_new(7, 2)).join(',') === owners(e.rk_new(7, 2)).join(','));

  const boards3 = new Set();
  for (let seed = 1; seed <= 40; seed++) boards3.add(owners(e.rk_new(seed, 3)).join(','));
  ok('the same holds at three players',
     boards3.size > 1, `${boards3.size} distinct openings`);
}

// -- Reinforcements are the rule the chapter states -----------------------
{
  const bad = [];
  for (const np of [2, 3, 4]) {
    for (let seed = 1; seed <= 15; seed++) {
      const h = e.rk_new(seed, np);
      const o = owners(h);
      for (let p = 0; p < np; p++) {
        const held = o.filter(v => v === p).length;
        let conts = 0;
        for (let c = 0; c < 4; c++) {
          const cells = [0, 1, 2].map(k => c * 3 + k);
          if (cells.every(t => o[t] === p)) conts++;
        }
        const want = Math.max(3, Math.floor(held / 3)) + 2 * conts;
        const got = e.rk_reinf(h, p);
        if (got !== want) bad.push(`np ${np} seed ${seed} p${p}: ${got}, rule says ${want} (held ${held}, continents ${conts})`);
        for (let c = 0; c < 4; c++) {
          const cells = [0, 1, 2].map(k => c * 3 + k);
          const wantC = cells.every(t => o[t] === p) ? 1 : 0;
          if (e.rk_cont(h, p, c) !== wantC) bad.push(`np ${np} seed ${seed} p${p} continent ${c}`);
        }
      }
    }
  }
  ok('reinforcements are territories over three, floor, at least three, plus two a continent',
     bad.length === 0, bad.length ? bad.slice(0, 3).join('; ') : '45 deals');
  ok('a player off the table reads -1',
     e.rk_reinf(e.rk_new(1, 2), 2) === -1 && e.rk_reinf(e.rk_new(1, 2), -1) === -1);
}

// -- THE COPY ARM ---------------------------------------------------------
// risk-set-terr and risk-recount-players both write with list-set-at.
{
  const h = e.rk_new(3, 2);
  const before = JSON.stringify([owners(h), armies(h), e.rk_cur(h), e.rk_turnno(h)]);
  const next = e.rk_turn(h, 99);
  ok('a turn answers a different state', next !== h);
  ok('THE COPY ARM: the state the turn was taken from is untouched',
     JSON.stringify([owners(h), armies(h), e.rk_cur(h), e.rk_turnno(h)]) === before);
  ok('control: the turn did something, so the arm is not vacuous',
     JSON.stringify([owners(next), armies(next)]) !== JSON.stringify([owners(h), armies(h)]) ||
     e.rk_cur(next) !== e.rk_cur(h));
}

// -- Turns keep the board legal -------------------------------------------
{
  const bad = [];
  let turns = 0, conquests = 0;
  for (let seed = 1; seed <= 20; seed++) {
    let h = e.rk_new(seed, 3);
    for (let t = 0; t < 40 && e.rk_done(h) === 0; t++) {
      const beforeO = owners(h), beforeA = armies(h);
      const np = e.rk_np(h);
      h = e.rk_turn(h, seed * 1000 + t);
      turns++;
      const o = owners(h), a = armies(h);
      if (o.some(v => v < 0 || v >= np)) { bad.push(`seed ${seed} turn ${t}: owner off the table`); break; }
      if (a.some(v => v < 0)) { bad.push(`seed ${seed} turn ${t}: negative armies ${a.join(',')}`); break; }
      // A territory an owner holds must hold at least one army: a zero-army
      // territory makes its owner dead while still owning ground.
      for (let i = 0; i < N; i++) {
        if (a[i] < 1) { bad.push(`seed ${seed} turn ${t}: territory ${i} owned by ${o[i]} with ${a[i]} armies`); break; }
      }
      // Three attacks a turn is the cap, so at most three can change hands.
      const changed = o.filter((v, i) => v !== beforeO[i]).length;
      if (changed > 3) bad.push(`seed ${seed} turn ${t}: ${changed} territories changed hands`);
      conquests += changed;
      // Every player's recorded total agrees with the board.
      for (let p = 0; p < np; p++) {
        const onBoard = a.filter((_, i) => o[i] === p).reduce((x, y) => x + y, 0);
        if (e.rk_total(h, p) !== onBoard) {
          bad.push(`seed ${seed} turn ${t}: player ${p} total ${e.rk_total(h, p)} against ${onBoard}`);
        }
        if ((e.rk_alive(h, p) === 1) !== (onBoard > 0)) {
          bad.push(`seed ${seed} turn ${t}: player ${p} alive flag disagrees with its armies`);
        }
      }
      if (bad.length) break;
    }
  }
  ok('every turn leaves a legal board and the army ledger agrees with it',
     bad.length === 0, bad.length ? bad.slice(0, 3).join('; ') : `${turns} turns`);
  ok('control: territories did change hands, so the arm saw combat',
     conquests > 0, `${conquests} changes of ownership`);
}

// -- The whole game -------------------------------------------------------
{
  const bad = [];
  const results = new Set();
  let decided = 0;
  for (let seed = 1; seed <= 30; seed++) {
    const r = e.rk_run(seed, 3);
    const w = e.rk_rwin(r), turns = e.rk_rturns(r);
    const finalO = [...Array(N)].map((_, i) => e.rk_rowner(r, i));
    results.add(`${w}:${turns}`);
    if (turns < 0) bad.push(`seed ${seed}: ${turns} turns`);
    if (w >= 0) {
      decided++;
      if (!finalO.every(v => v === w)) {
        bad.push(`seed ${seed}: player ${w} won holding ${finalO.filter(v => v === w).length} of 12`);
      }
    }
    if (finalO.some(v => v < -1 || v >= 3)) bad.push(`seed ${seed}: owner off the table in the result`);
  }
  ok('a declared winner owns all twelve territories',
     bad.length === 0, bad.length ? bad.slice(0, 3).join('; ') : '30 games');
  ok('control: the games are not all the same game',
     results.size > 1, `${results.size} distinct outcomes, ${decided} decided`);
  ok('a result territory off the board reads -1',
     e.rk_rowner(e.rk_run(1, 2), 12) === -1 && e.rk_rowner(e.rk_run(1, 2), -1) === -1);
}

// -- THE TURN CAP MUST NOT DECLARE A WINNER -------------------------------
// risk-make-result used to fall back to risk-find-alive when nothing had
// been won, so a game that ran out of turns reported the first surviving
// player as the winner (GAME-30). Thirty games never reach the cap; this
// arm sweeps twelve hundred, which is what it takes to see it at all.
{
  const bad = [];
  let games = 0, capped = 0, decided = 0, longest = 0;
  for (const np of [2, 3, 4]) {
    for (let seed = 1; seed <= 400; seed++) {
      const r = e.rk_run(seed, np);
      const w = e.rk_rwin(r), turns = e.rk_rturns(r);
      const own = [...Array(N)].map((_, i) => e.rk_rowner(r, i));
      games++;
      if (turns > longest) longest = turns;
      if (w < 0) capped++;
      else {
        decided++;
        if (!own.every(v => v === w)) {
          bad.push(`np${np} seed${seed}: ${turns} turns, winner ${w} owns ${own.filter(v => v === w).length} of 12`);
        }
      }
    }
  }
  ok('over twelve hundred games, a declared winner always owns all twelve',
     bad.length === 0, bad.length ? `${bad.length} bad, e.g. ${bad.slice(0, 2).join('; ')}` : `${games} games`);
  ok('control: some games DO run out of turns, so the arm is not vacuous',
     capped > 0, `${capped} undecided at the cap, ${decided} decided, longest ${longest} turns`);
}

// -- THE TURN IN STEPS ----------------------------------------------------
// Everything above grades whole turns taken by the engine. From 2026-09-02
// a turn is two phases a person can take one piece at a time, so these arms
// grade the pieces: that placing puts down exactly one army where you said,
// that an attack is refused unless it is legal, and that the turn ends when
// the attacks run out.
{
  const bad = [];
  let placed = 0, attacks = 0, refusals = 0, stops = 0;
  for (let seed = 1; seed <= 40; seed++) {
    let h = e.rk_new(seed, 4);
    const me = e.rk_cur(h);
    if (e.rk_phase(h) !== 0) bad.push(`seed ${seed}: opens in phase ${e.rk_phase(h)}`);
    // The reinforcement is territories/3 or three, whichever is larger,
    // plus continents. It is never nothing, so an opened turn always has a
    // move in it.
    if (e.rk_toplace(h) < 3) bad.push(`seed ${seed}: opened with ${e.rk_toplace(h)} to place`);
    if (e.rk_toplace(h) !== e.rk_reinf(h, me)) {
      bad.push(`seed ${seed}: opened with ${e.rk_toplace(h)}, reinforcements say ${e.rk_reinf(h, me)}`);
    }

    // You may not place on somebody else's territory, and a refusal must
    // leave the board alone.
    const theirs = [...Array(N).keys()].find(t => e.rk_owner(h, t) !== me);
    if (theirs !== undefined) {
      refusals++;
      if (e.rk_canplace(h, theirs) === 1) bad.push(`seed ${seed}: offered a place on ${theirs}`);
      if (e.rk_place(h, theirs) !== h) bad.push(`seed ${seed}: placed on somebody else's territory`);
    }
    if (e.rk_canplace(h, -1) === 1 || e.rk_canplace(h, 12) === 1) {
      bad.push(`seed ${seed}: a territory off the board was offered`);
    }
    // Attacking is refused while there are armies still to place.
    if (e.rk_canstop(h) === 1) bad.push(`seed ${seed}: could stop before placing`);

    // Put the whole reinforcement on one territory of yours, one at a time.
    const mine = [...Array(N).keys()].find(t => e.rk_owner(h, t) === me);
    const want = e.rk_toplace(h);
    const startArmies = e.rk_armies(h, mine);
    let g = h;
    for (let k = 0; k < want; k++) {
      const before = e.rk_toplace(g);
      g = e.rk_place(g, mine);
      placed++;
      if (e.rk_toplace(g) !== before - 1) {
        bad.push(`seed ${seed}: to-place went ${before} to ${e.rk_toplace(g)}`);
        break;
      }
    }
    if (e.rk_armies(g, mine) !== startArmies + want) {
      bad.push(`seed ${seed}: ${want} armies placed and the territory gained ${e.rk_armies(g, mine) - startArmies}`);
    }
    if (e.rk_phase(g) !== 1) bad.push(`seed ${seed}: still placing after the last army`);
    if (e.rk_atkleft(g) !== 3) bad.push(`seed ${seed}: ${e.rk_atkleft(g)} attacks after placing`);
    if (e.rk_canplace(g, mine) === 1) bad.push(`seed ${seed}: still offered a place in the attack phase`);
    // The handle the caller held is the position it was.
    if (e.rk_toplace(h) !== want) bad.push(`seed ${seed}: placing wrote through the caller's handle`);

    // An attack must be adjacent, from a territory with something to
    // attack WITH, and onto somebody else's.
    for (let from = 0; from < N; from++) {
      for (let to = 0; to < N; to++) {
        const legal = e.rk_canattack(g, from, to) === 1;
        const ok2 = e.rk_owner(g, from) === me && e.rk_owner(g, to) !== me
          && e.rk_armies(g, from) >= 2 && e.rk_adj(from, to) === 1;
        if (legal !== ok2) {
          bad.push(`seed ${seed}: ${from}->${to} offered ${legal}, rules say ${ok2}`);
          from = to = N;
        }
      }
    }

    // Take a legal attack if one exists, and check the bookkeeping.
    let found = null;
    for (let from = 0; from < N && !found; from++) {
      for (let to = 0; to < N; to++) {
        if (e.rk_canattack(g, from, to) === 1) { found = [from, to]; break; }
      }
    }
    if (found) {
      const totalBefore = armies(g).reduce((a, b) => a + b, 0);
      const left = e.rk_atkleft(g);
      const after = e.rk_attack(g, found[0], found[1], seed * 31 + 7);
      attacks++;
      const totalAfter = armies(after).reduce((a, b) => a + b, 0);
      // Combat destroys armies and never creates them.
      if (totalAfter > totalBefore) {
        bad.push(`seed ${seed}: the board gained armies in a battle, ${totalBefore} to ${totalAfter}`);
      }
      // Either this was the last attack, in which case the turn moved on,
      // or one fewer remains.
      if (e.rk_cur(after) === me && e.rk_done(after) !== 1) {
        if (e.rk_atkleft(after) !== left - 1) {
          bad.push(`seed ${seed}: attacks went ${left} to ${e.rk_atkleft(after)}`);
        }
      }
      if (e.rk_armies(g, found[0]) < 2) bad.push(`seed ${seed}: attacked from a single army`);
    }

    // Stopping ends the turn without spending the attacks.
    if (e.rk_canstop(g) === 1) {
      const s = e.rk_stop(g);
      stops++;
      if (e.rk_done(s) !== 1) {
        if (e.rk_cur(s) === me) bad.push(`seed ${seed}: stopping did not end the turn`);
        // The next player's turn is OPEN, or the page has nothing to offer.
        if (e.rk_phase(s) !== 0 || e.rk_toplace(s) < 3) {
          bad.push(`seed ${seed}: the next turn opened at phase ${e.rk_phase(s)} with ${e.rk_toplace(s)} to place`);
        }
      }
    }
  }
  ok('a turn places one army at a time and then offers three attacks',
     bad.length === 0, bad.length ? bad.slice(0, 3).join('; ') : '40 turns');
  // L-VACUOUS: each claim above is silent unless its branch was reached.
  ok('control: every branch was reached',
     placed > 0 && attacks > 0 && refusals > 0 && stops > 0,
     `${placed} armies placed, ${attacks} attacks, ${refusals} refusals, ${stops} stops`);
}

// -- THE RULE THAT TURN ONE CANNOT REACH ----------------------------------
// An army has to stay behind, so a territory holding one cannot attack out
// of. Nothing in the block above can see that rule: every territory is
// dealt three armies and the opening reinforcement only adds more, so
// across forty openings there is not ONE single-army territory to refuse.
// Sabotaging `a.armies < 2` to `< 1` changed no arm's colour, which is how
// this was found. The corpus has to be positions the game has been PLAYED
// into, not positions it starts in.
{
  const bad = [];
  let singles = 0, refusedFromSingle = 0, states = 0;
  for (let seed = 1; seed <= 60; seed++) {
    let h = e.rk_new(seed, 4);
    // Step into the middle of the game, where territories have been worn
    // down, then examine whoever is on the go.
    for (let n = 0; n < 12 && e.rk_done(h) !== 1; n++) h = e.rk_turn(h, seed * 977 + n);
    if (e.rk_done(h) === 1) continue;
    // Put the reinforcement somewhere out of the way so the attack phase
    // is reached without inflating the territory under test.
    const me = e.rk_cur(h);
    const dump = [...Array(N).keys()].find(t => e.rk_owner(h, t) === me);
    if (dump === undefined) continue;
    let g = h;
    let guard = 0;
    while (e.rk_phase(g) === 0 && guard++ < 40) g = e.rk_place(g, dump);
    if (e.rk_phase(g) !== 1) continue;
    states++;
    for (let from = 0; from < N; from++) {
      if (e.rk_owner(g, from) !== me) continue;
      if (e.rk_armies(g, from) !== 1) continue;
      singles++;
      for (let to = 0; to < N; to++) {
        if (e.rk_adj(from, to) !== 1) continue;
        if (e.rk_owner(g, to) === me) continue;
        // Adjacent, enemy-held, and only one army: the rules refuse it.
        if (e.rk_canattack(g, from, to) === 1) {
          bad.push(`seed ${seed}: ${from} holds one army and was offered an attack on ${to}`);
        } else if (e.rk_attack(g, from, to, seed) !== g) {
          bad.push(`seed ${seed}: ${from} attacked ${to} with its last army`);
        } else refusedFromSingle++;
      }
    }
  }
  ok('a territory holding one army cannot attack out of', bad.length === 0,
     bad.length ? bad.slice(0, 3).join('; ') : `${refusedFromSingle} refusals over ${states} positions`);
  // Without this the arm above is the same silence that let the sabotage
  // through: no single-army territory means nothing was refused.
  ok('control: single-army territories were actually reached', singles > 0,
     `${singles} found across ${states} mid-game positions`);
}

// -- A SELF-PLAYED TURN LEAVES A PLAYABLE POSITION ------------------------
// `risk-do-turn` advances the player without opening the next turn, because
// the watch-only runner opens each one as it takes it. A page holds the
// state BETWEEN turns, so if the wrapper did not open it the visitor would
// be handed a board with nothing to place and no attacks left.
{
  const bad = [];
  for (let seed = 1; seed <= 30; seed++) {
    let h = e.rk_new(seed, 4);
    for (let n = 0; n < 8 && e.rk_done(h) !== 1; n++) {
      h = e.rk_turn(h, seed * 101 + n);
      if (e.rk_done(h) === 1) break;
      if (e.rk_phase(h) !== 0 || e.rk_toplace(h) < 3) {
        bad.push(`seed ${seed} turn ${n}: phase ${e.rk_phase(h)}, ${e.rk_toplace(h)} to place`);
        break;
      }
      if (!(e.rk_alive(h, e.rk_cur(h)) === 1)) {
        bad.push(`seed ${seed} turn ${n}: the turn opened on a dead player`);
        break;
      }
    }
  }
  ok('a stepped turn hands back a board the next player can act on',
     bad.length === 0, bad.length ? bad.slice(0, 3).join('; ') : '30 games');
}

console.log(fail === 0
  ? `\nPASS: Risk deals, reinforces and conquers by its rules (${pass} arms).`
  : `\nFAIL: ${fail} of ${pass + fail} arms.`);
process.exitCode = fail === 0 ? 0 : 1;
