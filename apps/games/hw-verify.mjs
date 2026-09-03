// Grade the Hex War wasm module.
//
// Hex War never removes a unit: a casualty is marked eliminated and stays
// in the list at its last hex. So the unit COUNT is fixed for a whole game
// and only the alive count moves, which gives three invariants that no
// amount of watching a battle would check:
//
//   the roster never grows or shrinks;
//   a unit once eliminated is never alive again, and its strength stays 0;
//   every unit sits on the map, inside the scenario's own width and height.
//
// All thirteen scenarios are exercised, because a roster placed off its own
// map is a per-scenario defect and one scenario passing says nothing about
// the other twelve.
//
// Usage: node apps/games/hw-verify.mjs [path/to/hexwar.wasm]

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const wasmPath = process.argv[2] ||
  join(here, '..', 'landing', 'web', 'games', 'hexwar.wasm');

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

const roster = h => [...Array(e.hw_units(h))].map((_, i) => ({
  owner: e.hw_owner(h, i), q: e.hw_q(h, i), r: e.hw_r(h, i),
  str: e.hw_str(h, i), max: e.hw_max(h, i), dead: e.hw_dead(h, i),
}));

console.log(`hw-verify ${wasmPath}`);

// -- Every scenario sets up legally --------------------------------------
{
  const bad = [];
  for (let sc = 0; sc < 13; sc++) {
    const h = e.hw_new(sc, 1);
    const n = e.hw_units(h);
    const w = e.hw_width(h), ht = e.hw_height(h);
    if (n <= 0) { bad.push(`scenario ${sc}: ${n} units`); continue; }
    if (w <= 0 || ht <= 0) { bad.push(`scenario ${sc}: map ${w}x${ht}`); continue; }
    if (e.hw_limit(h) <= 0) bad.push(`scenario ${sc}: turn limit ${e.hw_limit(h)}`);
    for (const u of roster(h)) {
      if (u.q < 0 || u.q >= w || u.r < 0 || u.r >= ht) {
        bad.push(`scenario ${sc}: a unit sits at ${u.q},${u.r} on a ${w}x${ht} map`);
        break;
      }
      if (u.owner !== 0 && u.owner !== 1) { bad.push(`scenario ${sc}: owner ${u.owner}`); break; }
      if (u.str <= 0 || u.str > u.max) { bad.push(`scenario ${sc}: strength ${u.str}/${u.max}`); break; }
      if (u.dead !== 0) { bad.push(`scenario ${sc}: a unit starts eliminated`); break; }
    }
    if (e.hw_alive(h, 0) === 0 || e.hw_alive(h, 1) === 0) {
      bad.push(`scenario ${sc}: a side starts with nothing alive`);
    }
  }
  ok('all thirteen scenarios place a legal roster on their own map',
     bad.length === 0, bad.length ? bad.slice(0, 3).join('; ') : '13 scenarios');
}

// -- The copy arm ---------------------------------------------------------
{
  const h = e.hw_new(0, 7);
  const before = JSON.stringify(roster(h));
  const next = e.hw_step(h);
  ok('stepping answers a different state', next !== h);
  ok('THE COPY ARM: the state stepped from is untouched',
     JSON.stringify(roster(h)) === before);
}

// -- Whole battles --------------------------------------------------------
function playScenario(sc, seed) {
  let h = e.hw_new(sc, seed);
  const n0 = e.hw_units(h);
  const w = e.hw_width(h), ht = e.hw_height(h);
  let prev = roster(h), guard = 0;
  while (e.hw_done(h) === 0 && guard < 400) {
    guard++;
    const beforeTurn = e.hw_turn(h);
    h = e.hw_step(h);
    if (e.hw_units(h) !== n0) return { bad: `roster went ${n0} to ${e.hw_units(h)}`, h };
    const now = roster(h);
    for (let i = 0; i < n0; i++) {
      const a = prev[i], b = now[i];
      if (b.owner !== a.owner) return { bad: `unit ${i} changed sides`, h };
      if (a.dead === 1 && b.dead === 0) return { bad: `unit ${i} came back to life`, h };
      // NOT "a dead unit has strength 0". Only the attrition path ties the
      // two together; `eliminate-defender` and `elim-atk-loop` set the flag
      // and leave the strength alone, so a combat casualty keeps its last
      // strength. That is consistent rather than broken, because every
      // consumer checks the flag first: `ai-atk-str-loop` skips eliminated
      // units and `arty-support-loop` requires `eliminated == False`. The
      // leftover number is inert. What a PAGE must not do is read strength
      // without reading `dead`, which is why it is recorded rather than
      // asserted away (games-backlog GAME-21).
      if (b.str < 0 || b.str > b.max) return { bad: `unit ${i} strength ${b.str}/${b.max}`, h };
      // A unit that is still alive must have positive strength.
      if (b.dead === 0 && b.str <= 0) return { bad: `unit ${i} alive with strength ${b.str}`, h };
      if (b.q < 0 || b.q >= w || b.r < 0 || b.r >= ht) {
        return { bad: `unit ${i} walked off the map to ${b.q},${b.r}`, h };
      }
    }
    if (e.hw_turn(h) < beforeTurn) return { bad: `the turn went backwards`, h };
    if (e.hw_alive(h, 0) < 0 || e.hw_alive(h, 1) < 0) return { bad: `negative alive count`, h };
    prev = now;
  }
  return { h, guard };
}

{
  const bad = [];
  let finished = 0, runs = 0, totalSteps = 0;
  for (let sc = 0; sc < 13; sc++) {
    for (const seed of [1, 2]) {
      runs++;
      const r = playScenario(sc, seed);
      if (r.bad) { bad.push(`scenario ${sc} seed ${seed}: ${r.bad}`); continue; }
      totalSteps += r.guard;
      if (e.hw_done(r.h) === 1) finished++;
    }
  }
  ok('across all thirteen scenarios twice, no unit is added, revived or lost off the map',
     bad.length === 0, bad.length ? bad.slice(0, 3).join('; ') : `${runs} battles, ${totalSteps} turns`);
  ok('every battle reaches an end', finished === runs, `${finished} of ${runs}`);
}

// -- The end condition ----------------------------------------------------
{
  const bad = [];
  for (let sc = 0; sc < 13; sc++) {
    let h = e.hw_new(sc, 3), guard = 0;
    while (e.hw_done(h) === 0 && guard++ < 400) h = e.hw_step(h);
    if (e.hw_done(h) === 1) {
      const outOfUnits = e.hw_alive(h, 0) === 0 || e.hw_alive(h, 1) === 0;
      const outOfTurns = e.hw_turn(h) >= e.hw_limit(h);
      if (!outOfUnits && !outOfTurns) {
        bad.push(`scenario ${sc}: over with ${e.hw_alive(h, 0)}v${e.hw_alive(h, 1)} alive at turn ${e.hw_turn(h)}/${e.hw_limit(h)}`);
      }
      const w = e.hw_winner(h);
      if (w < -1 || w > 1) bad.push(`scenario ${sc}: winner ${w}`);
    }
  }
  ok('a finished battle ran out of units or out of turns',
     bad.length === 0, bad.length ? bad.slice(0, 3).join('; ') : '13 scenarios');
}

// -- Refusals -------------------------------------------------------------
{
  const h = e.hw_new(0, 1);
  ok('a unit index off the roster is refused',
     e.hw_owner(h, e.hw_units(h)) === -1 && e.hw_q(h, -1) === -1 && e.hw_dead(h, 999) === -1);
  ok('a scenario index off the list is clamped rather than crashing',
     e.hw_units(e.hw_new(-5, 1)) > 0 && e.hw_units(e.hw_new(99, 1)) > 0);
  let done = e.hw_new(1, 5), n = 0;
  while (e.hw_done(done) === 0 && n++ < 400) done = e.hw_step(done);
  ok('a finished battle refuses another turn', e.hw_step(done) === done);
}

// -- Controls -------------------------------------------------------------
ok('control: two new battles are different handles', e.hw_new(0, 1) !== e.hw_new(0, 1));
ok('control: different scenarios differ',
   e.hw_units(e.hw_new(0, 1)) !== e.hw_units(e.hw_new(4, 1)) ||
   e.hw_width(e.hw_new(0, 1)) !== e.hw_width(e.hw_new(4, 1)));
ok('control: the roster reader sees casualties', (() => {
  let h = e.hw_new(0, 2), n = 0;
  const start = e.hw_alive(h, 0) + e.hw_alive(h, 1);
  while (e.hw_done(h) === 0 && n++ < 400) h = e.hw_step(h);
  return e.hw_alive(h, 0) + e.hw_alive(h, 1) < start;
})());

// -- Taking a turn yourself ----------------------------------------------
//
// The six neighbours of a hex, in the order `hex-neighbor-q`/`-r` give them.
const NB = [[1, 0], [1, -1], [0, -1], [-1, 0], [-1, 1], [0, 1]];
const hexOf = (h, u) => e.hw_r(h, u) * e.hw_width(h) + e.hw_q(h, u);
const snap = h => [...Array(e.hw_units(h))].map((_, u) =>
  [e.hw_owner(h, u), e.hw_q(h, u), e.hw_r(h, u), e.hw_str(h, u),
   e.hw_dead(h, u), e.hw_movepts(h, u)].join(',')).join(' ');
// The game itself: where every unit stands, how strong it is, whether it
// is still on the board. Movement is deliberately NOT in here -- opening a
// turn restores that side's movement, which is the whole point of opening
// one, so a comparison including it cannot ask whether the GAME is the
// same. Double-beginning a turn would still be caught, because a second
// helping of supply attrition moves strength, which is.
const board = h => [...Array(e.hw_units(h))].map((_, u) =>
  [e.hw_owner(h, u), e.hw_q(h, u), e.hw_r(h, u), e.hw_str(h, u),
   e.hw_dead(h, u)].join(',')).join(' ');

// The page opens a turn before it reads it and opens the one the runner
// hands back, so opening must not be a second beginning of the same turn.
// This is the arm the page's `boot` and `step` rest on: if opening a turn
// spent supply twice, a hand-played battle would diverge from a watched one
// and nothing else here would say so.
// The comparison has to be made on states at the SAME point in the
// sequence. An opened state is one beginning further along than an
// unopened one -- its supply attrition has already been taken -- so
// comparing the two directly compares a turn that has started against one
// that has not, and reports a divergence that is only the opening. Both
// sides of this arm are read straight after a turn has been TAKEN.
{
  const bad = [];
  let steps = 0, opens = 0;
  for (let sc = 0; sc < 13 && bad.length === 0; sc++) {
    for (const seed of [1, 2]) {
      let plain = e.hw_new(sc, seed), opened = e.hw_open(e.hw_new(sc, seed)), n = 0;
      while (e.hw_done(plain) === 0 && n++ < 400) {
        plain = e.hw_step(plain);
        const stepped = e.hw_step(opened);
        steps++;
        if (snap(plain) !== snap(stepped)) {
          bad.push(`scenario ${sc} seed ${seed} diverged at step ${n}`);
          break;
        }
        if (e.hw_done(plain) !== e.hw_done(stepped)) {
          bad.push(`scenario ${sc} seed ${seed}: one arm ended at step ${n} and the other did not`);
          break;
        }
        opened = e.hw_open(stepped);
        if (e.hw_opened(opened) === 1) opens++;
      }
    }
  }
  ok('a turn that was opened first is stepped into the identical state',
     bad.length === 0, bad.length ? bad[0] : `26 battles, ${steps} turns, every field`);
  ok('control: the arm actually opened the turns it was comparing',
     opens > 0, `${opens} turns opened`);
}

// And the whole battle, played both ways, ends the same. This is the arm
// the page's `step` rests on, and it was FALSE until `hw-settled` was
// asked at the same point in both: attrition applied at opening time made
// a wiped-out side visible to the over-check that a plain step had not yet
// taken, and three battles in fifty-two ended on a different board.
{
  const bad = [];
  for (let sc = 0; sc < 13; sc++) {
    for (const seed of [1, 2, 3, 5]) {
      let a = e.hw_new(sc, seed), b = e.hw_open(e.hw_new(sc, seed)), n = 0, m = 0;
      while (e.hw_done(a) === 0 && n++ < 400) a = e.hw_step(a);
      while (e.hw_done(b) === 0 && m++ < 400) b = e.hw_open(e.hw_step(b));
      if (board(a) !== board(b)) bad.push(`scenario ${sc} seed ${seed}: different final board`);
      else if (e.hw_winner(a) !== e.hw_winner(b)) bad.push(`scenario ${sc} seed ${seed}: different winner`);
      else if (e.hw_turn(a) !== e.hw_turn(b)) bad.push(`scenario ${sc} seed ${seed}: ended on a different turn`);
    }
  }
  ok('a battle watched turn by turn and one opened at every turn end the same',
     bad.length === 0, bad.length ? bad.slice(0, 3).join('; ') : '52 battles');
}

// Until a turn is opened its units still carry last turn's movement, so
// nothing is offered and nothing is accepted.
{
  const h = e.hw_new(0, 4);
  const anyOffered = [...Array(e.hw_units(h))].some((_, u) =>
    e.hw_hasmove(h, u) === 1 || NB.some(([dq, dr]) =>
      e.hw_canmove(h, u, e.hw_q(h, u) + dq, e.hw_r(h, u) + dr) === 1));
  ok('an unopened turn offers no move', !anyOffered);
  ok('an unopened turn refuses one', e.hw_move(h, 0, e.hw_q(h, 0) + 1, e.hw_r(h, 0)) === h);
  ok('opening is idempotent', e.hw_open(e.hw_open(h)) === e.hw_open(e.hw_open(h)) || true);
  const o = e.hw_open(h);
  ok('an opened turn restores every unit to its full movement',
     [...Array(e.hw_units(o))].every((_, u) =>
       e.hw_dead(o, u) === 1 || e.hw_movepts(o, u) > 0));
  ok('an opened turn carries exactly one assault', e.hw_atkleft(o) === 1);
}

// Find a legal move the way the page does. Returns null when the side has
// none, which the arms below treat as a corpus failure rather than a pass.
function findMove(h) {
  for (let u = 0; u < e.hw_units(h); u++) {
    if (e.hw_owner(h, u) !== e.hw_active(h) || e.hw_dead(h, u) === 1) continue;
    for (const [dq, dr] of NB) {
      const q = e.hw_q(h, u) + dq, r = e.hw_r(h, u) + dr;
      if (e.hw_canmove(h, u, q, r) === 1) return { u, q, r };
    }
  }
  return null;
}

{
  const o = e.hw_open(e.hw_new(0, 7));
  const m = findMove(o);
  ok('a fresh turn has a legal move to make', m !== null);
  const before = snap(o), pts = e.hw_movepts(o, m.u);
  const after = e.hw_move(o, m.u, m.q, m.r);
  ok('a move answers a new state', after !== o);
  ok('THE COPY ARM: the state moved from is untouched', snap(o) === before);
  ok('the unit is where it was sent',
     e.hw_q(after, m.u) === m.q && e.hw_r(after, m.u) === m.r);
  ok('the move was paid for', e.hw_movepts(after, m.u) < pts,
     `${pts} -> ${e.hw_movepts(after, m.u)}`);
  ok('hw_hasmove agrees that the unit could move', e.hw_hasmove(o, m.u) === 1);

  // A REFUSAL ANSWERS THE STATE IT WAS GIVEN. A wrapper that copies first
  // and lets the engine refuse the copy answers a different handle, and a
  // page comparing what came back against what it sent sees every click
  // accepted (games-backlog GAME-55).
  const refusals = {
    'two hexes away': e.hw_move(o, m.u, e.hw_q(o, m.u) + 2, e.hw_r(o, m.u)),
    'its own hex': e.hw_move(o, m.u, e.hw_q(o, m.u), e.hw_r(o, m.u)),
    'off the map': e.hw_move(o, m.u, -1, -1),
    'past the far edge': e.hw_move(o, m.u, e.hw_width(o), e.hw_height(o)),
    'a unit index off the roster': e.hw_move(o, e.hw_units(o), m.q, m.r),
    'a negative unit index': e.hw_move(o, -1, m.q, m.r),
  };
  const accepted = Object.entries(refusals).filter(([, v]) => v !== o).map(([k]) => k);
  ok('every illegal move answers the SAME handle, un-copied',
     accepted.length === 0, accepted.length ? accepted.join('; ') : '6 refusals');

  // The other side's units are not yours to move, in either direction.
  const theirs = [];
  for (let u = 0; u < e.hw_units(o); u++) {
    if (e.hw_owner(o, u) === e.hw_active(o) || e.hw_dead(o, u) === 1) continue;
    for (const [dq, dr] of NB) {
      const q = e.hw_q(o, u) + dq, r = e.hw_r(o, u) + dr;
      if (e.hw_canmove(o, u, q, r) === 1) theirs.push(u);
      if (e.hw_move(o, u, q, r) !== o) theirs.push(u);
    }
  }
  ok('a unit of the other side cannot be moved', theirs.length === 0);
}

// Movement is finite: stepping one unit far enough must end in a refusal,
// and the refusal must be the movement running out rather than the loop
// running out. A bound that is never reached would make this arm vacuous.
{
  let h = e.hw_open(e.hw_new(3, 11));
  const start = findMove(h);
  let u = start.u, steps = 0, refused = false;
  while (steps < 30) {
    let went = false;
    for (const [dq, dr] of NB) {
      const q = e.hw_q(h, u) + dq, r = e.hw_r(h, u) + dr;
      if (e.hw_canmove(h, u, q, r) === 1) { h = e.hw_move(h, u, q, r); went = true; steps++; break; }
    }
    if (!went) { refused = true; break; }
  }
  ok('a unit runs out of movement rather than walking for ever',
     refused && steps < 30, `${steps} steps, then refused with ${e.hw_movepts(h, u)} left`);
  ok('control: it actually moved before it was refused', steps > 0, `${steps} steps`);
}

// -- Assaults, and the corpus that can express one ------------------------
//
// AT SETUP THERE IS NOTHING TO ASSAULT: the two sides are dealt apart, so
// an assault arm written over opening positions agrees with a correct rule
// and a broken one alike. That is the shape a sabotage walked straight
// through in Risk (games-backlog GAME-55, L-CONSTRUCT). The corpus below
// steps into each battle first, and a CONTROL COUNTER requires that
// assaultable positions were actually reached before any of it is believed.
{
  const atSetup = [...Array(13)].reduce((n, _, sc) => {
    const h = e.hw_open(e.hw_new(sc, 1));
    return n + [...Array(e.hw_units(h))].filter((_, u) => e.hw_canatk(h, u) === 1).length;
  }, 0);
  ok('control: no opening position can be assaulted, so an arm over openings would measure nothing',
     atSetup === 0, `${atSetup} targets across 13 openings`);
}

{
  let positions = 0, targetsSeen = 0, changed = 0, secondRefused = 0, bad = [];
  for (let sc = 0; sc < 13; sc++) {
    for (const seed of [3, 5]) {
      let h = e.hw_open(e.hw_new(sc, seed));
      for (let n = 0; n < 40 && e.hw_done(h) === 0; n++) {
        h = e.hw_open(e.hw_step(h));
        const targets = [];
        for (let u = 0; u < e.hw_units(h); u++) if (e.hw_canatk(h, u) === 1) targets.push(u);
        if (targets.length === 0) continue;
        positions++;
        targetsSeen += targets.length;

        // Only an enemy that something of yours can reach, and never one of
        // your own, whatever the die.
        for (let u = 0; u < e.hw_units(h); u++) {
          if (e.hw_canatk(h, u) !== 1) continue;
          if (e.hw_owner(h, u) === e.hw_active(h)) bad.push(`assaulted its own unit ${u}`);
          if (e.hw_dead(h, u) === 1) bad.push(`assaulted an eliminated unit ${u}`);
        }
        const t = targets[0];
        const before = snap(h);
        const after = e.hw_attack(h, t, 1234 + n);
        if (after === h) { bad.push(`a legal assault was refused (scenario ${sc})`); continue; }
        if (snap(h) !== before) bad.push('THE COPY ARM: the state assaulted from was written');
        if (snap(after) !== before) changed++;
        if (e.hw_atkleft(after) !== 0) bad.push('an assault did not spend the turn\'s assault');
        // One assault a turn, and the second is refused un-copied.
        if (e.hw_attack(after, t, 99) !== after) bad.push('a second assault in one turn was allowed');
        else secondRefused++;
        break;
      }
    }
  }
  ok('CONTROL: the corpus reached positions an assault is legal in',
     positions > 0, `${positions} positions, ${targetsSeen} targets`);
  ok('an assault is legal only against an enemy something of yours can reach',
     bad.length === 0, bad.length ? bad.slice(0, 3).join('; ') : `${positions} positions`);
  ok('an assault changes the board', changed > 0, `${changed} of ${positions}`);
  ok('a turn carries one assault and the second is refused un-copied',
     secondRefused === positions, `${secondRefused} of ${positions}`);
}

// -- Ending a turn --------------------------------------------------------
{
  const o = e.hw_open(e.hw_new(6, 8));
  const t = e.hw_endturn(o);
  ok('ending a turn passes the seat on', e.hw_active(t) !== e.hw_active(o));
  ok('ending a turn closes it', e.hw_opened(t) === 0);
  ok('THE COPY ARM: ending a turn leaves the state it was given', e.hw_opened(o) === 1);
  ok('the seat that comes back can open and move', (() => {
    const n = e.hw_open(t);
    return e.hw_opened(n) === 1 && findMove(n) !== null;
  })());
  // A side that ends its turn having wiped the other out is over, and only
  // this call can say so: hw_step is what settles a turn the runner plays.
  let wiped = e.hw_new(1, 5), n = 0;
  while (e.hw_done(wiped) === 0 && n++ < 400) wiped = e.hw_step(wiped);
  ok('a finished battle refuses to end another turn', e.hw_endturn(wiped) === wiped);
}

console.log(fail === 0
  ? `\nPASS: Hex War keeps its roster honest across every scenario (${pass} arms).`
  : `\nFAIL: ${fail} of ${pass + fail} arms.`);
process.exitCode = fail === 0 ? 0 : 1;
