// Grade the Rock Paper Scissors wasm module.
//
// The rules are three lines, so they are checked exhaustively: all nine
// move pairs against an independent table, and the whole of `rps-beats`.
//
// The part worth a grader is the AI. It claims to counter the opponent's
// most frequent move, and that claim is decidable: give it a lopsided count
// triple, sweep seeds, and the choice it settles on must BEAT the move the
// opponent plays most. A counter function pointed the wrong way round loses
// to the dominant move instead, and nothing in a series score says so --
// two AIs making the same mistake against each other still produce a
// plausible spread of wins, losses and ties.
//
// Moves are 0 rock, 1 paper, 2 scissors.
//
// Usage: node apps/games/rp-verify.mjs [path/to/rps.wasm]

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const wasmPath = process.argv[2] ||
  join(here, '..', 'landing', 'web', 'games', 'rps.wasm');

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

const NAME = ['rock', 'paper', 'scissors'];
// The rules, independently: x defeats (x + 2) % 3, and the move that
// defeats m is (m + 1) % 3.
const defeats = x => (x + 2) % 3;
const counterTo = m => (m + 1) % 3;
const outcome = (a, b) => a === b ? 0 : defeats(a) === b ? 1 : -1;

console.log(`rp-verify ${wasmPath}`);

// -- The rules, all nine pairs -------------------------------------------
{
  const bad = [];
  for (let m = 0; m < 3; m++) {
    if (e.rp_beats(m) !== defeats(m)) {
      bad.push(`${NAME[m]} beats ${NAME[e.rp_beats(m)]}, rules say ${NAME[defeats(m)]}`);
    }
  }
  ok('each move defeats the one the rules say it does',
     bad.length === 0, bad.length ? bad.join('; ') : 'rock>scissors, paper>rock, scissors>paper');

  const bad2 = [];
  for (let a = 0; a < 3; a++) {
    for (let b = 0; b < 3; b++) {
      if (e.rp_outcome(a, b) !== outcome(a, b)) {
        bad2.push(`${NAME[a]} against ${NAME[b]}: ${e.rp_outcome(a, b)}, rules say ${outcome(a, b)}`);
      }
    }
  }
  ok('all nine move pairs score by the rules', bad2.length === 0,
     bad2.length ? bad2.join('; ') : '9 pairs');
  ok('control: the rules are not a constant, they have a winner and a loser and a tie',
     new Set([outcome(0, 0), outcome(0, 2), outcome(0, 1)]).size === 3);
  ok('control: no move defeats itself and every move is defeated by exactly one',
     [0, 1, 2].every(m => defeats(m) !== m) &&
     [0, 1, 2].every(m => [0, 1, 2].filter(x => defeats(x) === m).length === 1));
}

// -- THE AI COUNTERS, OR IT DOES NOT --------------------------------------
// A lopsided count triple, swept over seeds. The fuzz branch fires with
// probability 2/(total+3), so a large total makes the settled choice the
// overwhelming majority and the modal answer is the AI's real intent.
{
  const bad = [];
  const rows = [];
  for (let dominant = 0; dominant < 3; dominant++) {
    const counts = [1, 1, 1];
    counts[dominant] = 200;
    const tally = [0, 0, 0];
    for (let seed = 1; seed <= 300; seed++) {
      const c = e.rp_choice(counts[0], counts[1], counts[2], seed);
      if (c < 0 || c > 2) { bad.push(`seed ${seed}: chose ${c}`); break; }
      tally[c]++;
    }
    const modal = tally.indexOf(Math.max(...tally));
    const want = counterTo(dominant);
    rows.push(`against mostly ${NAME[dominant]}: chose ${NAME[modal]} ` +
              `${tally[modal]} of 300 (rules say ${NAME[want]})`);
    if (modal !== want) {
      bad.push(`against mostly ${NAME[dominant]} it plays ${NAME[modal]}, which ` +
               `${outcome(modal, dominant) === 1 ? 'beats' : outcome(modal, dominant) === 0 ? 'ties' : 'LOSES TO'} it`);
    }
  }
  ok('the AI answers the opponent\'s most frequent move with the move that beats it',
     bad.length === 0, bad.length ? bad.join('; ') : '3 lopsided profiles');
  for (const r of rows) console.log(`          ${r}`);
  ok('control: with no history the AI still answers in range',
     [...Array(60)].map((_, i) => e.rp_choice(0, 0, 0, i + 1)).every(c => c >= 0 && c <= 2));
  ok('control: with no history it is not a constant, so the sweep can see variety',
     new Set([...Array(60)].map((_, i) => e.rp_choice(0, 0, 0, i + 1))).size > 1);
}

// -- The state and its copy ----------------------------------------------
{
  const h = e.rp_new(11);
  ok('a new series is empty',
     [0, 1, 2].every(m => e.rp_c1(h, m) === 0 && e.rp_c2(h, m) === 0) &&
     e.rp_w1(h) === 0 && e.rp_w2(h) === 0 && e.rp_ties(h) === 0);
  ok('a move off the table reads -1', e.rp_c1(h, 3) === -1 && e.rp_c2(h, -1) === -1);

  const before = JSON.stringify([0, 1, 2].map(m => [e.rp_c1(h, m), e.rp_c2(h, m)]));
  const next = e.rp_round(h);
  ok('a round answers a different state', next !== h);
  ok('THE COPY ARM: the state the round was played from is untouched',
     JSON.stringify([0, 1, 2].map(m => [e.rp_c1(h, m), e.rp_c2(h, m)])) === before &&
     e.rp_w1(h) === 0 && e.rp_w2(h) === 0 && e.rp_ties(h) === 0);
  ok('control: the round did something',
     [0, 1, 2].reduce((n, m) => n + e.rp_c1(next, m), 0) === 1);
}

// -- The ledger adds up ---------------------------------------------------
{
  const bad = [];
  for (let seed = 1; seed <= 40; seed++) {
    let h = e.rp_new(seed);
    for (let i = 0; i < 25; i++) h = e.rp_round(h);
    const c1 = [0, 1, 2].map(m => e.rp_c1(h, m));
    const c2 = [0, 1, 2].map(m => e.rp_c2(h, m));
    const played1 = c1.reduce((a, b) => a + b, 0);
    const played2 = c2.reduce((a, b) => a + b, 0);
    const decided = e.rp_w1(h) + e.rp_w2(h) + e.rp_ties(h);
    if (played1 !== 25) bad.push(`seed ${seed}: player one played ${played1} of 25`);
    if (played2 !== 25) bad.push(`seed ${seed}: player two played ${played2} of 25`);
    if (decided !== 25) bad.push(`seed ${seed}: ${decided} results from 25 rounds`);
  }
  ok('after twenty-five rounds each side has played twenty-five moves and every round was scored',
     bad.length === 0, bad.length ? bad.slice(0, 3).join('; ') : '40 series');
}

// -- The series runner agrees with stepping it by hand --------------------
{
  const bad = [];
  for (let seed = 1; seed <= 30; seed++) {
    const r = e.rp_run(seed, 20);
    let h = e.rp_new(seed);
    for (let i = 0; i < 20; i++) h = e.rp_round(h);
    if (e.rp_rp1(r) !== e.rp_w1(h) || e.rp_rp2(r) !== e.rp_w2(h) || e.rp_rties(r) !== e.rp_ties(h)) {
      bad.push(`seed ${seed}: run ${e.rp_rp1(r)}/${e.rp_rp2(r)}/${e.rp_rties(r)} against ` +
               `stepped ${e.rp_w1(h)}/${e.rp_w2(h)}/${e.rp_ties(h)}`);
    }
    if (e.rp_rp1(r) + e.rp_rp2(r) + e.rp_rties(r) !== 20) {
      bad.push(`seed ${seed}: the series does not add to 20`);
    }
  }
  ok('running a series and stepping it round by round give the same score',
     bad.length === 0, bad.length ? bad.slice(0, 3).join('; ') : '30 series');
  ok('control: a zero-round series is empty',
     e.rp_rp1(e.rp_run(1, 0)) === 0 && e.rp_rties(e.rp_run(1, 0)) === 0);
}

console.log(fail === 0
  ? `\nPASS: RPS scores by the rules and its AI counters (${pass} arms).`
  : `\nFAIL: ${fail} of ${pass + fail} arms.`);
process.exitCode = fail === 0 ? 0 : 1;
