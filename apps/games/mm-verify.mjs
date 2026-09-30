// Grade the Mastermind wasm module.
//
// Scoring is where Mastermind is got wrong, and it is pure arithmetic, so
// this file computes it independently and checks the engine over EVERY
// ordered pair of a large sample of codes. Duplicates are the trap: three
// reds against two reds is two whites, not three, and the naive
// "count matching colours" answer is wrong exactly when a colour repeats.
// A wrong scorer still plays, still narrows a pool and still solves most
// games, so nothing short of the arithmetic catches it.
//
// The second arm is that the pool only ever shrinks and always still
// contains the secret. A filter that dropped the secret would make the
// game unwinnable in a way that looks like bad luck.
//
// Usage: node apps/games/mm-verify.mjs [path/to/mastermind.wasm]

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const wasmPath = process.argv[2] ||
  join(here, '..', 'landing', 'web', 'games', 'mastermind.wasm');

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

// The independent scorer. Blacks are exact positions; whites are the
// remaining colour overlap, counted as the minimum of each colour's counts
// across the two codes, less the blacks.
const digits = code => [0, 1, 2, 3].map(p => Math.floor(code / [1, 6, 36, 216][p]) % 6);
function score(secret, guess) {
  const s = digits(secret), g = digits(guess);
  let blacks = 0;
  for (let i = 0; i < 4; i++) if (s[i] === g[i]) blacks++;
  let overlap = 0;
  for (let c = 0; c < 6; c++) {
    overlap += Math.min(s.filter(x => x === c).length, g.filter(x => x === c).length);
  }
  return blacks * 10 + (overlap - blacks);
}

const pool = h => [...Array(e.mm_pool(h))].map((_, i) => e.mm_poolat(h, i));

console.log(`mm-verify ${wasmPath}`);

// -- The digit encoding ---------------------------------------------------
{
  let bad = 0;
  for (let code = 0; code < 1296; code += 7) {
    const mine = digits(code);
    for (let p = 0; p < 4; p++) if (e.mm_digit(code, p) !== mine[p]) bad++;
  }
  ok('the engine reads a code the same way this file does', bad === 0, `${bad} mismatches`);
  ok('a code or position off the board is refused',
     e.mm_digit(1296, 0) === -1 && e.mm_digit(0, 4) === -1 && e.mm_digit(-1, 0) === -1);
}

// -- THE SCORING ORACLE ---------------------------------------------------
{
  const bad = [];
  let checked = 0, withDupes = 0;
  for (let a = 0; a < 1296; a += 11) {
    for (let b = 0; b < 1296; b += 13) {
      const want = score(a, b), got = e.mm_score(a, b);
      checked++;
      if (new Set(digits(a)).size < 4 || new Set(digits(b)).size < 4) withDupes++;
      if (want !== got) {
        bad.push(`secret ${digits(a)} guess ${digits(b)}: engine ${got}, arithmetic ${want}`);
      }
    }
  }
  ok('the engine scores every sampled pair the way the arithmetic does',
     bad.length === 0, bad.length ? `${bad.length} of ${checked} wrong; ${bad[0]}` : `${checked} pairs`);
  ok('control: the sample was mostly codes with repeated colours',
     withDupes > checked / 2, `${withDupes} of ${checked} had a duplicate`);
  // A few by hand, because a sample that agrees with a wrong oracle agrees.
  ok('control: four of a kind against four of another scores nothing',
     e.mm_score(0, 1 + 6 + 36 + 216) === 0, e.mm_score(0, 1 + 6 + 36 + 216));
  ok('control: a code against itself is four blacks', e.mm_score(500, 500) === 40);
  ok('control: three of a colour against one of it is one white, not three',
     // secret 0,0,0,1 against guess 1,2,2,2 -> the single 1 matches, misplaced
     e.mm_score(0 + 0 + 0 + 216, 1 + 12 + 72 + 432) === 1,
     e.mm_score(0 + 0 + 0 + 216, 1 + 12 + 72 + 432));
}

// -- The pool -------------------------------------------------------------
const s0 = e.mm_new(9);
ok('the pool starts as every code', e.mm_pool(s0) === 1296, e.mm_pool(s0));
ok('nothing guessed yet',
   e.mm_guesses(s0) === 0 && e.mm_guess(s0) === -1 && e.mm_solved(s0) === 0);
ok('the secret is a real code', e.mm_secret(s0) >= 0 && e.mm_secret(s0) < 1296, e.mm_secret(s0));

// -- The copy arm ---------------------------------------------------------
{
  const before = e.mm_pool(s0), beforeFirst = e.mm_poolat(s0, 0);
  const next = e.mm_step(s0);
  ok('stepping answers a different state', next !== s0);
  ok('THE COPY ARM: the pool stepped from is untouched',
     e.mm_pool(s0) === before && e.mm_poolat(s0, 0) === beforeFirst,
     `${before} -> ${e.mm_pool(s0)}`);
}

// -- Whole games ----------------------------------------------------------
{
  const bad = [];
  let solved = 0, totalGuesses = 0, worst = 0;
  for (let seed = 1; seed <= 60; seed++) {
    let h = e.mm_new(seed);
    const secret = e.mm_secret(h);
    let prevPool = e.mm_pool(h), steps = 0;
    while (e.mm_done(h) === 0 && steps < 12) {
      steps++;
      h = e.mm_step(h);
      const p = e.mm_pool(h);
      if (p > prevPool) bad.push(`seed ${seed}: pool grew ${prevPool} to ${p}`);
      if (!e.mm_solved(h)) {
        // The secret must survive every filter, or the game is unwinnable.
        if (!pool(h).includes(secret)) {
          bad.push(`seed ${seed} guess ${steps}: the secret left the pool`);
          break;
        }
      }
      // The reported score must match the arithmetic for the guess made.
      const g = e.mm_guess(h);
      const want = score(secret, g);
      const got = e.mm_blacks(h) * 10 + e.mm_whites(h);
      if (want !== got) {
        bad.push(`seed ${seed} guess ${steps}: reported ${got}, arithmetic ${want}`);
        break;
      }
      if (e.mm_solved(h) && g !== secret) {
        bad.push(`seed ${seed}: solved on a guess that is not the secret`);
      }
      prevPool = p;
    }
    totalGuesses += e.mm_guesses(h);
    if (e.mm_guesses(h) > worst) worst = e.mm_guesses(h);
    if (e.mm_solved(h) === 1) solved++;
  }
  ok('the pool never grows and never loses the secret, and every score checks out',
     bad.length === 0, bad.length ? bad.slice(0, 2).join('; ') : '60 games');
  ok('every game is solved', solved === 60, `${solved} of 60`);
  // Consistency-filtering Mastermind solves in at most five guesses in
  // theory and this engine takes the pool's first entry, so a bound of ten
  // is the engine's own limit rather than a number picked to pass.
  ok('no game needs more than the engine allows', worst <= 10, `worst ${worst} guesses`);
  ok('control: the games took real work', totalGuesses > 60, `${totalGuesses} guesses over 60 games`);
}

// -- THE RULES (Mastermind.codex, "The Rules") ----------------------------
//
// Rule 3 over the whole set: every ordered pair of the 1296 codes.
{
  const dg = [...Array(1296)].map((_, c) => digits(c));
  const counts = dg.map(d => [0, 1, 2, 3, 4, 5].map(k => d.filter(x => x === k).length));
  let bad = 0, first = '';
  for (let a = 0; a < 1296; a++) {
    for (let b = 0; b < 1296; b++) {
      let bl = 0, ov = 0;
      for (let i = 0; i < 4; i++) if (dg[a][i] === dg[b][i]) bl++;
      for (let k = 0; k < 6; k++) ov += Math.min(counts[a][k], counts[b][k]);
      const want = bl * 10 + ov - bl, got = e.mm_score(a, b);
      if (got !== want) { if (!bad) first = `secret ${dg[a]} guess ${dg[b]}: engine ${got}, rules ${want}`; bad++; }
    }
  }
  ok('rules: the key pegs of every ordered pair of codes', bad === 0, bad ? `${bad} wrong; ${first}` : '1679616 pairs');
}

// Rule 2 and rule 4, the codebreaker's legal set at each state class: every
// code is a legal guess until the code is broken or ten rows are spent, and
// none after. A refused guess answers the same handle.
{
  const wrongOf = s => (s + 1) % 1296;
  const after = (seed, n) => {
    let h = e.mm_new(seed);
    const s = e.mm_secret(h);
    // n guesses, none of them the secret and none repeating.
    for (let k = 0; k < n; k++) h = e.mm_guessat(h, (s + 1 + k * 97) % 1296);
    return h;
  };
  const classes = [
    ['a new game', after(5, 0), 1],
    ['after five wrong guesses', after(5, 5), 1],
    ['after nine wrong guesses (the tenth row)', after(5, 9), 1],
    ['after ten wrong guesses', after(5, 10), 0],
  ];
  for (const [name, h, want] of classes) {
    const s = e.mm_secret(h), g0 = e.mm_guesses(h);
    let bad = '', taken = 0, wonOn = 0;
    for (let c = 0; c < 1296 && !bad; c++) {
      const n = e.mm_guessat(h, c);
      const tookIt = n !== h;
      if (tookIt !== (want === 1)) { bad = `code ${c}: ${tookIt ? 'taken' : 'refused'}`; break; }
      if (!tookIt) continue;
      taken++;
      const sc = e.mm_blacks(n) * 10 + e.mm_whites(n);
      if (e.mm_guesses(n) !== g0 + 1 || e.mm_guess(n) !== c || sc !== score(s, c)) {
        bad = `code ${c}: guesses ${e.mm_guesses(n)}, last ${e.mm_guess(n)}, score ${sc} vs ${score(s, c)}`;
      }
      const won = e.mm_solved(n) === 1;
      if (won !== (c === s)) bad = `code ${c}: solved ${won}, secret ${s}`;
      if (won) wonOn++;
      const over = c !== s && g0 + 1 >= 10;
      if ((e.mm_done(n) === 1) !== (won || over)) bad = `code ${c}: done ${e.mm_done(n)}`;
    }
    ok(`rules: ${name}, ${want ? 'every code is a legal guess' : 'no guess is legal'}`,
       !bad && e.mm_canguess(h) === want && taken === (want ? 1296 : 0) && wonOn === want,
       bad || `${taken} taken, canguess ${e.mm_canguess(h)}`);
  }
  const lost = after(5, 10);
  ok('rules: ten wrong guesses lose, and the game is over',
     e.mm_solved(lost) === 0 && e.mm_done(lost) === 1 && e.mm_guesses(lost) === 10);
  let won = e.mm_new(6);
  won = e.mm_guessat(won, e.mm_secret(won));
  ok('rules: a broken code refuses every further guess',
     e.mm_solved(won) === 1 && e.mm_canguess(won) === 0
       && [0, 1, 700, 1295, e.mm_secret(won)].every(c => e.mm_guessat(won, c) === won));
  const fresh = e.mm_new(5);
  ok('rules: a code off the set is not a guess',
     [-1, 1296, 5000].every(c => e.mm_guessat(fresh, c) === fresh));
}

// The count the page shows ("codes still fit") is the set of codes consistent
// with every score so far, recomputed here from the history alone, through
// human guesses and the solver taking over part way.
{
  let bad = '', games = 0, plies = 0;
  let rnd = 777;
  const next = () => (rnd = (rnd * 1103515245 + 12345) % 2147483648);
  for (let seed = 1; seed <= 20 && !bad; seed++) {
    let h = e.mm_new(seed);
    const s = e.mm_secret(h), hist = [];
    for (let k = 0; e.mm_done(h) === 0 && k < 10 && !bad; k++) {
      h = k < 3 ? e.mm_guessat(h, next() % 1296) : e.mm_step(h);
      hist.push([e.mm_guess(h), score(s, e.mm_guess(h))]);
      plies++;
      if (e.mm_solved(h) === 1) break;
      const want = [];
      for (let c = 0; c < 1296; c++) if (hist.every(([g, sc]) => score(c, g) === sc)) want.push(c);
      const got = pool(h);
      if (got.length !== want.length || got.some((c, i) => c !== want[i])) {
        bad = `seed ${seed} ply ${k}: pool ${got.length}, rules ${want.length}`;
      }
    }
    games++;
  }
  ok('rules: the codes still fitting are exactly those every score allows', !bad, bad || `${games} games, ${plies} guesses`);
}

// Rule 1: every code, repeats included, can be the secret.
{
  const seen = new Set();
  for (let seed = 1; seed <= 20000 && seen.size < 1296; seed++) seen.add(e.mm_secret(e.mm_new(seed)));
  ok('rules: every one of the 1296 codes is a reachable secret', seen.size === 1296, `${seen.size} reached`);
}

// -- Refusals -------------------------------------------------------------
ok('a code off the set is refused in scoring',
   e.mm_score(-1, 0) === -1 && e.mm_score(0, 1296) === -1);
ok('a pool index off the end is refused', e.mm_poolat(s0, 1296) === -1 && e.mm_poolat(s0, -1) === -1);
ok('a solved game refuses another guess', (() => {
  let h = e.mm_new(4), n = 0;
  while (e.mm_done(h) === 0 && n++ < 12) h = e.mm_step(h);
  return e.mm_step(h) === h;
})());

// -- Controls -------------------------------------------------------------
ok('control: two new games are different handles', e.mm_new(1) !== e.mm_new(1));
ok('control: different seeds pick different secrets',
   e.mm_secret(e.mm_new(1)) !== e.mm_secret(e.mm_new(2)));
ok('control: the same seed picks the same secret',
   e.mm_secret(e.mm_new(8)) === e.mm_secret(e.mm_new(8)));

console.log(fail === 0
  ? `\nPASS: Mastermind scores its pegs correctly, duplicates and all (${pass} arms).`
  : `\nFAIL: ${fail} of ${pass + fail} arms.`);
process.exitCode = fail === 0 ? 0 : 1;
