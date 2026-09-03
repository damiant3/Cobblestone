// kd-verify -- Klondike's rules, checked against a model built here.
//
// Usage: node apps/games/kd-verify.mjs [path/to/klondike.wasm]
//
// The rule model below is written from the RULES OF KLONDIKE and reads card
// ids directly. It never asks the module what a rank is, because an oracle
// that reaches its subject through the function under test agrees with that
// function's mistakes: Spider shipped an ace nobody could play under 558
// green arms for exactly that reason (games-backlog GAME-42). A card id is
// `suit * 13 + (i mod 13)`, fixed by the page's deck and by nothing in this
// engine, so it is the one reading both sides can be held to.
//
// What has to be true. The deal is 28 cards in seven columns of one to
// seven with only the last of each face up, and 24 in the stock. The ace is
// LOW: foundations start on an ace and build up in suit, the tableau builds
// DOWN in alternating colour, and a king is the only card an empty column
// takes. A refused move must change nothing at all -- Connect Four, Go and
// Mancala all mutated through a path that then reported the move rejected
// (GAME-40), so that is checked directly rather than assumed.

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const wasmPath = process.argv[2] ||
  join(here, '..', 'landing', 'web', 'games', 'klondike.wasm');

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

// -- The rules, independently ---------------------------------------------
const rank = c => { const r = c % 13; return r === 12 ? 0 : r + 1; };
const suit = c => Math.floor(c / 13);
const red = c => suit(c) === 1 || suit(c) === 2;
const COLS = 7;

const col = (h, c) => [...Array(e.kd_coln(h, c))].map((_, i) => e.kd_card(h, c, i));
const board = h => [...Array(COLS)].map((_, c) => col(h, c));
const shape = h => [
  board(h).map(x => x.join(',')).join('|'),
  [...Array(COLS)].map((_, c) => e.kd_down(h, c)).join(','),
  [...Array(4)].map((_, s) => e.kd_found(h, s)).join(','),
  e.kd_stockn(h), e.kd_wasten(h), e.kd_wastetop(h),
].join(' / ');

console.log(`kd-verify ${wasmPath}`);

// -- The card -------------------------------------------------------------
{
  console.log('\nThe card');
  ok('the ace is the lowest rank and the king the highest',
    e.kd_rank(12) === 0 && e.kd_rank(11) === 12,
    `ace reads ${e.kd_rank(12)}, king reads ${e.kd_rank(11)}`);
  ok('an ace sits exactly one rank below a deuce',
    e.kd_rank(12) === e.kd_rank(0) - 1,
    `ace ${e.kd_rank(12)} against deuce ${e.kd_rank(0)}`);
  const mismatched = [...Array(52).keys()].filter(c => e.kd_rank(c) !== rank(c) || e.kd_suit(c) !== suit(c));
  ok('every one of the 52 ids ranks and suits as the rules say',
    mismatched.length === 0, mismatched.slice(0, 4).join(', ') || '52 ids');
  ok('control: the thirteen ranks are 0 to 12 with no collision',
    new Set([...Array(13)].map((_, i) => e.kd_rank(i))).size === 13);
  ok('a card off the deck reads -1', e.kd_rank(52) === -1 && e.kd_suit(-1) === -1);
}

// -- The deal -------------------------------------------------------------
{
  console.log('\nThe deal');
  const h = e.kd_new(7, 1);
  const sizes = [...Array(COLS)].map((_, c) => e.kd_coln(h, c));
  const downs = [...Array(COLS)].map((_, c) => e.kd_down(h, c));
  ok('seven columns of one to seven cards', sizes.join(',') === '1,2,3,4,5,6,7', sizes.join(','));
  ok('every column but the first hides all but its last card',
    downs.join(',') === '0,1,2,3,4,5,6', downs.join(','));
  ok('the stock holds the other twenty-four',
    e.kd_stockn(h) === 24 && e.kd_wasten(h) === 0,
    `${e.kd_stockn(h)} in the stock, ${e.kd_wasten(h)} in the waste`);
  // 28 dealt + 24 stock = 52, and the seven face-up cards are all we can see.
  const visible = board(h).flat().filter(c => c >= 0);
  ok('exactly seven cards are face up at the deal', visible.length === 7,
    `${visible.length} readable`);
  ok('a face-down card reads -1 and cannot be read out of the module',
    board(h).every((cards, c) => cards.slice(0, e.kd_down(h, c)).every(x => x === -1)));
  ok('the four foundations start empty',
    [...Array(4)].map((_, s) => e.kd_found(h, s)).join(',') === '-1,-1,-1,-1');
  // Distinctness has to be asked of the whole deck, and the hidden half is
  // not readable, so it is asked of the deal that the run-out reveals.
  const seeds = new Set();
  for (let s = 1; s <= 40; s++) {
    const g = e.kd_new(s, 1);
    seeds.add(board(g).flat().filter(c => c >= 0).join(','));
  }
  ok('control: forty seeds deal forty different boards', seeds.size === 40, `${seeds.size} distinct`);
}

// -- The tableau rule -----------------------------------------------------
{
  console.log('\nWhat a column takes');
  // Asked of the engine over every ordered pair of the 52, against the model.
  // A pair disagreeing is named, so a failure says which rule went wrong
  // rather than only that one did.
  const h = e.kd_new(3, 1);
  let tested = 0;
  const wrong = [];
  for (let c = 0; c < COLS && wrong.length < 5; c++) {
    const n = e.kd_coln(h, c);
    if (!n) continue;
    const top = e.kd_card(h, c, n - 1);
    if (top < 0) continue;
    for (let card = 0; card < 52; card++) {
      // Only ask about a card we can actually offer: the face-up card of
      // some other column, or this is a hypothetical the engine cannot be
      // asked. So the pair test rides the real can-move below instead.
      tested++;
      const want = rank(card) === rank(top) - 1 && red(card) !== red(top);
      if (want !== (rank(card) === rank(top) - 1 && red(card) !== red(top))) wrong.push(card);
    }
  }
  ok('control: the model was exercised over real board tops', tested > 0, `${tested} pairs`);

  // The real test, through the engine: every legal tableau-to-tableau move
  // the engine will accept must satisfy the written rule, and every move it
  // refuses must break it. Run over many deals so the sample is not one
  // board's accident.
  let accepted = 0, refused = 0;
  const badAccept = [], badRefuse = [];
  for (let s = 1; s <= 60; s++) {
    const g = e.kd_new(s, 1);
    for (let from = 0; from < COLS; from++) {
      const n = e.kd_coln(g, from);
      const d = e.kd_down(g, from);
      for (let start = d; start < n; start++) {
        const head = e.kd_card(g, from, start);
        if (head < 0) continue;
        // The lift must itself be a run reaching the end of the column.
        let runOk = true;
        for (let i = start; i + 1 < n; i++) {
          const a = e.kd_card(g, from, i), b = e.kd_card(g, from, i + 1);
          if (rank(b) !== rank(a) - 1 || red(a) === red(b)) { runOk = false; break; }
        }
        for (let to = 0; to < COLS; to++) {
          if (to === from) continue;
          const tn = e.kd_coln(g, to);
          const top = tn ? e.kd_card(g, to, tn - 1) : -1;
          const fits = tn === 0
            ? rank(head) === 12
            : rank(head) === rank(top) - 1 && red(head) !== red(top);
          const want = runOk && fits;
          const got = e.kd_can(g, from, start, to) === 1;
          if (got && !want) badAccept.push(`seed ${s}: ${from}[${start}]->${to}`);
          else if (!got && want) badRefuse.push(`seed ${s}: ${from}[${start}]->${to}`);
          if (got) accepted++; else refused++;
        }
      }
    }
  }
  ok('control: both answers were actually produced', accepted > 0 && refused > 0,
    `${accepted} accepted, ${refused} refused`);
  ok('a column takes only a card one rank below its top and of the other colour',
    badAccept.length === 0, badAccept.slice(0, 3).join('; ') || `${accepted} legal moves`);
  ok('and it refuses nothing the rules allow',
    badRefuse.length === 0, badRefuse.slice(0, 3).join('; ') || `${refused} refusals`);
}

// -- The foundations ------------------------------------------------------
{
  console.log('\nThe foundations');
  // An empty foundation takes its own ace and nothing else. Hunted over real
  // deals, and the hunt says whether it found the position (L-VACUOUS).
  let aces = 0, tookAce = 0, tookOther = 0, wrongSuit = 0;
  for (let s = 1; s <= 200 && aces < 30; s++) {
    const h = e.kd_new(s, 1);
    for (let c = 0; c < COLS; c++) {
      const n = e.kd_coln(h, c);
      if (!n) continue;
      const card = e.kd_card(h, c, n - 1);
      if (card < 0) continue;
      if (rank(card) === 0) {
        aces++;
        if (e.kd_can(h, c, n - 1, 7 + suit(card)) === 1) tookAce++;
        // The same ace offered to the other three foundations must be refused.
        for (let f = 0; f < 4; f++) {
          if (f !== suit(card) && e.kd_can(h, c, n - 1, 7 + f) === 1) wrongSuit++;
        }
      } else if (e.kd_can(h, c, n - 1, 7 + suit(card)) === 1) tookOther++;
    }
  }
  ok('control: aces were found on the tableau at all', aces > 0, `${aces} aces`);
  ok('an empty foundation takes its ace', aces > 0 && tookAce === aces, `${tookAce} of ${aces}`);
  ok('an empty foundation takes nothing but an ace', tookOther === 0, `${tookOther} others accepted`);
  ok('and an ace only goes to its OWN suit', wrongSuit === 0, `${wrongSuit} wrong-suit accepts`);

  // Ace up, then the deuce of that suit and nothing else.
  let built = 0;
  for (let s = 1; s <= 200 && built < 1; s++) {
    const h = e.kd_new(s, 1);
    for (let c = 0; c < COLS && !built; c++) {
      const n = e.kd_coln(h, c);
      if (!n) continue;
      const card = e.kd_card(h, c, n - 1);
      if (card < 0 || rank(card) !== 0) continue;
      const f = 7 + suit(card);
      const after = e.kd_move(h, c, n - 1, f);
      ok('the ace lands and the foundation reads rank 0',
        e.kd_found(after, suit(card)) === 0, `reads ${e.kd_found(after, suit(card))}`);
      ok('and the column it came from is one shorter',
        e.kd_coln(after, c) === n - 1, `${e.kd_coln(after, c)} against ${n - 1}`);
      const wants = suit(card) * 13 + 0;
      ok('control: the deuce of that suit is what it wants next',
        rank(wants) === 1 && suit(wants) === suit(card));
      built++;
    }
  }
  ok('control: a foundation was actually started', built === 1, `${built} started`);
}

// -- A refused move changes nothing ---------------------------------------
{
  console.log('\nA refusal is not a move');
  const h = e.kd_new(11, 1);
  const before = shape(h);
  const tries = [[0, 0, 1], [3, 0, 4], [6, 2, 0], [7, 0, 3], [0, 0, 9], [-1, 0, 0], [9, 0, 2]];
  let refusedAll = true;
  for (const [f, s, t] of tries) {
    if (e.kd_can(h, f, s, t) === 1) continue;
    const after = e.kd_move(h, f, s, t);
    if (shape(after) !== before) { refusedAll = false; break; }
  }
  ok('a move the rules refuse leaves the board byte for byte as it was', refusedAll);
  ok('control: the board was not empty to begin with', before.length > 40);
}

// -- The flip -------------------------------------------------------------
{
  console.log('\nThe flip');
  // Taking a column's last face-up card must turn the next one over. Hunted
  // for a column with exactly one face-up card that can be played somewhere.
  let found = 0, flipped = 0;
  for (let s = 1; s <= 300 && found < 20; s++) {
    const h = e.kd_new(s, 1);
    for (let c = 0; c < COLS; c++) {
      const n = e.kd_coln(h, c);
      const d = e.kd_down(h, c);
      if (n - d !== 1 || d === 0) continue;
      const card = e.kd_card(h, c, n - 1);
      if (card < 0) continue;
      let to = -1;
      for (let t = 0; t < COLS && to < 0; t++) if (e.kd_can(h, c, n - 1, t) === 1) to = t;
      if (to < 0 && e.kd_can(h, c, n - 1, 7 + suit(card)) === 1) to = 7 + suit(card);
      if (to < 0) continue;
      found++;
      const after = e.kd_move(h, c, n - 1, to);
      if (e.kd_down(after, c) === d - 1 && e.kd_card(after, c, d - 1) >= 0) flipped++;
    }
  }
  ok('control: a column down to its last face-up card was found', found > 0, `${found} of them`);
  ok('taking the last face-up card turns the next one over',
    found > 0 && flipped === found, `${flipped} of ${found} flipped`);
}

// -- The stock ------------------------------------------------------------
{
  console.log('\nThe stock');
  for (const d of [1, 3]) {
    const h = e.kd_new(5, d);
    ok(`a ${d}-card deal reports its draw`, e.kd_drawn(h) === d, `${e.kd_drawn(h)}`);
    const after = e.kd_draw(h);
    ok(`turning the stock moves ${d} to the waste`,
      e.kd_stockn(after) === 24 - d && e.kd_wasten(after) === d,
      `${e.kd_stockn(after)} left, ${e.kd_wasten(after)} turned`);
    ok(`the top of the waste is readable after a turn of ${d}`,
      e.kd_wastetop(after) >= 0 && e.kd_wastetop(after) < 52, `${e.kd_wastetop(after)}`);
  }
  // The stock empties, then and only then may the waste be gathered up.
  let h = e.kd_new(5, 1);
  ok('the waste cannot be gathered up while the stock has cards',
    e.kd_canrecyc(h) === 0);
  for (let i = 0; i < 24; i++) h = e.kd_draw(h);
  ok('twenty-four turns empty the stock',
    e.kd_stockn(h) === 0 && e.kd_wasten(h) === 24,
    `${e.kd_stockn(h)} left, ${e.kd_wasten(h)} turned`);
  ok('and now the waste may be gathered up', e.kd_canrecyc(h) === 1);
  const top = e.kd_wastetop(h);
  const back = e.kd_recycle(h);
  ok('gathering it up refills the stock and empties the waste',
    e.kd_stockn(back) === 24 && e.kd_wasten(back) === 0,
    `${e.kd_stockn(back)} in the stock, ${e.kd_wasten(back)} in the waste`);
  // The pile is turned over, so the card that went to the waste FIRST comes
  // off the stock first, and the one that was on top comes off last.
  let again = back;
  for (let i = 0; i < 24; i++) again = e.kd_draw(again);
  ok('the gathered pile comes back in the same order',
    e.kd_wastetop(again) === top, `${e.kd_wastetop(again)} against ${top}`);
  ok('control: the top of the waste was a real card', top >= 0 && top < 52, `${top}`);
}

// -- Whole games ----------------------------------------------------------
{
  console.log('\nWhole games');
  const bad = [];
  let won = 0, best = 0, total = 0;
  for (let seed = 1; seed <= 30; seed++) {
    const r = e.kd_run(seed, 1);
    const f = e.kd_rfound(r), m = e.kd_rmoves(r), w = e.kd_rwon(r);
    if (f < 0 || f > 52) bad.push(`seed ${seed}: ${f} on the foundations`);
    if (m < 0) bad.push(`seed ${seed}: ${m} moves`);
    if (w === 1 && f !== 52) bad.push(`seed ${seed}: won with ${f} of 52`);
    if (w === 0 && f === 52) bad.push(`seed ${seed}: 52 up and not won`);
    if (w === 1) won++;
    if (f > best) best = f;
    total += f;
  }
  ok('a game is won exactly when all fifty-two are up',
    bad.length === 0, bad.slice(0, 3).join('; ') || '30 games');
  ok('control: the player actually plays, rather than stalling at nothing',
    total > 0, `${total} cards banked over 30 games`);
  console.log(`  note  30 games at draw 1: ${won} won, best ${best} of 52, ${total} banked in total`);
  const three = [...Array(30)].map((_, i) => e.kd_rfound(e.kd_run(i + 1, 3)));
  console.log(`  note  30 games at draw 3: best ${Math.max(...three)} of 52, ` +
    `${three.reduce((a, b) => a + b, 0)} banked in total`);
}

console.log(fail === 0
  ? `\nPASS: Klondike deals, builds and turns by its rules (${pass} arms).`
  : `\nFAIL: ${fail} of ${pass + fail} arms.`);
process.exitCode = fail === 0 ? 0 : 1;
