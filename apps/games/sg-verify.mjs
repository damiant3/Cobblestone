// Grade the Set wasm module.
//
// Set is the game in this arcade whose rules are most completely decidable,
// so this grader proves rather than samples. The card space is 81 cards and
// four ternary attributes, the validity rule is a function of three cards,
// and the deck is small enough to check every claim exhaustively:
//
//   the decoder is a bijection onto the 81 attribute quadruples;
//   validity agrees with the rule on ALL 85,320 unordered triples;
//   for every pair of distinct cards exactly one third card completes a
//     set, which is the structural theorem the game rests on;
//   the tableau count agrees with an independent count over its 220 triples.
//
// The third of those is the one worth having. A validity rule that is wrong
// in some corner still produces plausible tableau counts, and a count is
// what a play-through shows you; "exactly one" is a property no wrong rule
// can satisfy.
//
// Usage: node apps/games/sg-verify.mjs [path/to/setgame.wasm]

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const wasmPath = process.argv[2] ||
  join(here, '..', 'landing', 'web', 'games', 'setgame.wasm');

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

// The rules, independently: id is base three, least significant first,
// number, shading, colour, shape.
const attrs = id => [id % 3, Math.floor(id / 3) % 3, Math.floor(id / 9) % 3, Math.floor(id / 27) % 3];
const attrOk = (a, b, c) => (a === b && b === c) || (a !== b && b !== c && a !== c);
const validSet = (x, y, z) => {
  const [ax, ay, az] = [attrs(x), attrs(y), attrs(z)];
  return [0, 1, 2, 3].every(k => attrOk(ax[k], ay[k], az[k]));
};

console.log(`sg-verify ${wasmPath}`);

const table = h => [...Array(e.sg_tabn(h))].map((_, i) => e.sg_tab(h, i));
const deckOf = h => [...Array(e.sg_deckn(h))].map((_, i) => e.sg_deck(h, i));
// The whole legal set of a position, by the oracle: every valid triple of
// distinct table positions, and a deal exactly when no set is out and the
// deck holds a card.
const oracleTriples = tab => {
  const out = [];
  for (let i = 0; i < tab.length; i++)
    for (let j = i + 1; j < tab.length; j++)
      for (let k = j + 1; k < tab.length; k++)
        if (validSet(tab[i], tab[j], tab[k])) out.push(`${i},${j},${k}`);
  return out;
};
const engineTriples = h => {
  const n = e.sg_tabn(h), out = [];
  for (let i = 0; i < n; i++)
    for (let j = i + 1; j < n; j++)
      for (let k = j + 1; k < n; k++)
        if (e.sg_cantake(h, i, j, k) === 1) out.push(`${i},${j},${k}`);
  return out;
};

// -- The decoder ----------------------------------------------------------
{
  const bad = [];
  const seen = new Set();
  for (let id = 0; id < 81; id++) {
    const want = attrs(id);
    const got = [e.sg_number(id), e.sg_shading(id), e.sg_color(id), e.sg_shape(id)];
    if (JSON.stringify(got) !== JSON.stringify(want)) {
      bad.push(`card ${id}: ${got.join(',')} against ${want.join(',')}`);
    }
    if (got.some(v => v < 0 || v > 2)) bad.push(`card ${id}: attribute out of range`);
    seen.add(got.join(','));
  }
  ok('all eighty-one cards decode to the attributes the rules give them',
     bad.length === 0, bad.length ? bad.slice(0, 3).join('; ') : '81 cards');
  ok('the decoder is a bijection: eighty-one distinct attribute quadruples',
     seen.size === 81, `${seen.size} distinct`);
  ok('a card off the deck reads -1',
     e.sg_number(81) === -1 && e.sg_shape(-1) === -1 && e.sg_valid(81, 0, 1) === -1);
}

// -- Validity, on every unordered triple of the whole deck ---------------
{
  let checked = 0, valid = 0;
  const bad = [];
  for (let a = 0; a < 81 && bad.length < 4; a++) {
    for (let b = a + 1; b < 81 && bad.length < 4; b++) {
      for (let c = b + 1; c < 81; c++) {
        const want = validSet(a, b, c) ? 1 : 0;
        const got = e.sg_valid(a, b, c);
        checked++;
        if (want) valid++;
        if (got !== want) { bad.push(`${a},${b},${c}: engine ${got}, rules ${want}`); break; }
      }
    }
  }
  ok('validity agrees with the rules on every unordered triple in the deck',
     bad.length === 0, bad.length ? bad.slice(0, 3).join('; ') : `${checked} triples`);
  // 81 * 80 / 6 = 1080 sets exist in a Set deck. A count that is right is a
  // second, independent statement about the same function.
  ok('control: the deck holds exactly 1,080 sets, which is what the card space requires',
     valid === 1080, `${valid} sets among ${checked} triples`);
}

// -- Built triples, for the corners a sweep states but does not name ------
{
  const cases = [
    ['all same but number', [0, 1, 2], true],
    ['all different in every attribute', [0, 1 + 3 + 9 + 27, 2 + 6 + 18 + 54], true],
    ['two numbers alike and one not', [0, 1, 1 + 3], false],
    ['alike in three attributes, two-and-one in the fourth', [0, 1, 3], false],
  ];
  const bad = [];
  for (const [name, ids, want] of cases) {
    if (new Set(ids).size !== 3) bad.push(`ORACLE on ${name}: the triple repeats a card`);
    if (validSet(...ids) !== want) bad.push(`ORACLE on ${name}`);
  }
  ok('control: the oracle gets the built triples right, and each is three distinct cards',
     bad.length === 0, bad.length ? bad.join('; ') : `${cases.length} triples`);
  const wrong = [];
  for (const [name, ids, want] of cases) {
    const got = e.sg_valid(...ids) === 1;
    if (got !== want) wrong.push(`${name}: engine ${got}, rules ${want}`);
  }
  ok('every built triple is judged by the rules',
     wrong.length === 0, wrong.length ? wrong.join('; ') : `${cases.length} triples`);
}

// -- THE STRUCTURAL THEOREM ----------------------------------------------
// Any two distinct cards lie in exactly one set. This is what makes Set the
// game it is, and no rule that is wrong anywhere can satisfy it everywhere.
{
  const bad = [];
  let pairs = 0;
  for (let a = 0; a < 81 && bad.length < 3; a++) {
    for (let b = a + 1; b < 81 && bad.length < 3; b++) {
      let completions = 0;
      for (let c = 0; c < 81; c++) {
        if (c === a || c === b) continue;
        if (e.sg_valid(a, b, c) === 1) completions++;
      }
      pairs++;
      if (completions !== 1) bad.push(`cards ${a} and ${b} have ${completions} completions`);
    }
  }
  ok('every pair of cards is completed to a set by exactly one third card',
     bad.length === 0, bad.length ? bad.slice(0, 3).join('; ') : `${pairs} pairs`);
}

// -- The deal -------------------------------------------------------------
{
  const bad = [];
  const tableaux = new Set();
  for (let seed = 1; seed <= 60; seed++) {
    const h = e.sg_new(seed);
    const tab = [...Array(e.sg_tabn(h))].map((_, i) => e.sg_tab(h, i));
    const deck = [...Array(e.sg_deckn(h))].map((_, i) => e.sg_deck(h, i));
    if (tab.length !== 12) bad.push(`seed ${seed}: tableau of ${tab.length}`);
    if (deck.length !== 69) bad.push(`seed ${seed}: ${deck.length} left in the deck`);
    const all = [...tab, ...deck];
    if (new Set(all).size !== 81) bad.push(`seed ${seed}: the deal is not the whole deck`);
    if (all.some(c => c < 0 || c > 80)) bad.push(`seed ${seed}: a card is off the deck`);
    if (tab.some(c => deck.includes(c))) bad.push(`seed ${seed}: a card is in both`);
    if (e.sg_found(h) !== 0) bad.push(`seed ${seed}: found ${e.sg_found(h)} before play`);
    tableaux.add(tab.join(','));
  }
  ok('every deal splits the eighty-one card deck into twelve and sixty-nine',
     bad.length === 0, bad.length ? bad.slice(0, 3).join('; ') : '60 deals');
  ok('the deal depends on the seed',
     tableaux.size > 1, `${tableaux.size} distinct tableaux from 60 seeds`);
  ok('control: the same seed deals the same tableau',
     [...Array(12)].map((_, i) => e.sg_tab(e.sg_new(9), i)).join(',') ===
     [...Array(12)].map((_, i) => e.sg_tab(e.sg_new(9), i)).join(','));
  ok('a position off the tableau reads -1',
     e.sg_tab(e.sg_new(1), 12) === -1 && e.sg_deck(e.sg_new(1), 69) === -1);
}

// -- The count ------------------------------------------------------------
{
  const bad = [];
  const counts = [];
  for (let seed = 1; seed <= 200; seed++) {
    const h = e.sg_new(seed);
    const tab = [...Array(12)].map((_, i) => e.sg_tab(h, i));
    let want = 0;
    for (let i = 0; i < 12; i++)
      for (let j = i + 1; j < 12; j++)
        for (let k = j + 1; k < 12; k++)
          if (validSet(tab[i], tab[j], tab[k])) want++;
    const got = e.sg_sets(h);
    const viaRun = e.sg_run(seed);
    counts.push(want);
    if (got !== want) bad.push(`seed ${seed}: engine ${got}, rules ${want}`);
    if (viaRun !== got) bad.push(`seed ${seed}: run says ${viaRun}, the tableau says ${got}`);
  }
  ok('the number of sets in the tableau is the number the rules find, on 200 deals',
     bad.length === 0, bad.length ? bad.slice(0, 3).join('; ') : '200 deals');
  const mean = counts.reduce((a, b) => a + b, 0) / counts.length;
  ok('control: the tableaux are not all setless, so the counter was exercised',
     counts.some(c => c > 0), `mean ${mean.toFixed(2)} sets, range ${Math.min(...counts)} to ${Math.max(...counts)}`);
}

// -- RULES: the legal set of every position a game passes through ---------
// Set Enterprises' rules, as SetGame.codex states them. At every position of
// 40 played-through games the engine's takeable triples must be exactly the
// oracle's, the deal offered exactly when no set is out and the deck is not
// empty, and each move must grow or shrink the table as the rules say.
{
  const badSet = [], badDeal = [], badMove = [];
  let positions = 0, maxTable = 0, grew = 0, shrank = 0;
  for (let seed = 1; seed <= 40; seed++) {
    let h = e.sg_new(seed);
    for (let step = 0; step < 60; step++) {
      const tab = table(h), deck = deckOf(h);
      positions++;
      maxTable = Math.max(maxTable, tab.length);
      const want = oracleTriples(tab), got = engineTriples(h);
      if (want.join(';') !== got.join(';')) badSet.push(`seed ${seed} step ${step}: engine ${got.length}, oracle ${want.length} on ${tab.length} cards`);
      const dealWant = want.length === 0 && deck.length > 0 ? 1 : 0;
      if (e.sg_candeal(h) !== dealWant) badDeal.push(`seed ${seed} step ${step}: deal ${e.sg_candeal(h)}, want ${dealWant}`);
      if (want.length === 0 && deck.length === 0) break;
      const next = e.sg_step(h);
      const nt = table(next), nd = deckOf(next);
      if (want.length === 0) {
        grew++;
        const add = Math.min(3, deck.length);
        if (nt.length !== tab.length + add || nd.length !== deck.length - add ||
            nt.slice(tab.length).join() !== deck.slice(0, add).join())
          badMove.push(`seed ${seed} step ${step}: a deal from ${tab.length} made ${nt.length}`);
      } else {
        const keep = tab.length - 3;
        const refill = keep >= 12 ? 0 : Math.min(12 - keep, deck.length);
        if (tab.length > 12) shrank++;
        if (nt.length !== keep + refill || nd.length !== deck.length - refill)
          badMove.push(`seed ${seed} step ${step}: a take from ${tab.length} (deck ${deck.length}) left ${nt.length} (deck ${nd.length}), want ${keep + refill}`);
      }
      h = next;
    }
  }
  ok('every position offers exactly the oracle\'s sets, no more and no fewer', badSet.length === 0,
     badSet.slice(0, 2).join('; ') || `${positions} positions`);
  ok('the deal is offered exactly when no set is out and the deck is not empty', badDeal.length === 0,
     badDeal.slice(0, 2).join('; ') || `${positions} positions`);
  ok('a deal adds three from the deck, a take from twelve refills and a take from more does not', badMove.length === 0,
     badMove.slice(0, 2).join('; ') || `${grew} deals, ${shrank} takes from a grown table`);
  ok('control: tables grew past twelve and shrank back, so both rules were exercised', grew > 0 && shrank > 0 && maxTable > 12,
     `${grew} deals, ${shrank} shrinks, largest table ${maxTable}`);
}

// -- RULES: built positions -------------------------------------------------
{
  // The sixteen cards whose every attribute is 0 or 1 hold no set: all-
  // different needs a 2 somewhere, and all-same is one card.
  const cap = [];
  for (let id = 0; id < 81; id++) if (attrs(id).every(v => v < 2)) cap.push(id);
  const third = (a, b) => { for (let c = 0; c < 81; c++) if (c !== a && c !== b && validSet(a, b, c)) return c; return -1; };
  const build = (tab, deck) => {
    let h = e.sg_empty(0);
    for (const c of tab) h = e.sg_pushtab(h, c);
    for (const c of deck) h = e.sg_pushdeck(h, c);
    return h;
  };
  ok('control: the sixteen cards hold no set', cap.length === 16 && oracleTriples(cap).length === 0);
  const y = third(cap[0], cap[1]);
  const extra = [0, 1, 2, 3, 4, 5, 6, 7, 8].map(i => third(cap[2], cap[3 + i])).filter(c => c !== y && !cap.includes(c));
  let h = build(cap.slice(0, 12), [cap[12], cap[13], cap[14], cap[15], y, extra[0], extra[1], extra[2]]);
  ok('twelve cards with no set offer the deal and no triple', e.sg_candeal(h) === 1 && engineTriples(h).length === 0);
  h = e.sg_deal(h);
  ok('fifteen with still no set offer the deal again', e.sg_tabn(h) === 15 && e.sg_candeal(h) === 1 && engineTriples(h).length === 0,
     `${e.sg_tabn(h)} cards`);
  h = e.sg_deal(h);
  const tab = table(h);
  ok('eighteen cards, and now a set is out, so the deal is refused', e.sg_tabn(h) === 18 && e.sg_candeal(h) === 0 && engineTriples(h).length > 0,
     `${e.sg_tabn(h)} cards, ${engineTriples(h).length} sets`);
  const [i, j, k] = [tab.indexOf(cap[0]), tab.indexOf(cap[1]), tab.indexOf(y)];
  const deckBefore = e.sg_deckn(h);
  const t = e.sg_take(h, i, j, k);
  ok('a set taken from eighteen is not replaced: fifteen stay and the deck is untouched',
     e.sg_tabn(t) === 15 && e.sg_deckn(t) === deckBefore && !table(t).includes(cap[0]) && !table(t).includes(y),
     `${e.sg_tabn(t)} cards, deck ${e.sg_deckn(t)} of ${deckBefore}`);
  ok('the same index twice, or one off the table, is not a set',
     e.sg_cantake(h, i, i, k) === 0 && e.sg_cantake(h, i, j, 18) === 0 && e.sg_cantake(h, -1, j, k) === 0);
  const short = e.sg_take(build([cap[0], cap[1], y, ...cap.slice(2, 11)], [cap[11], cap[12]]), 0, 1, 2);
  ok('a take from twelve with two cards left refills two: eleven out', e.sg_tabn(short) === 11 && e.sg_deckn(short) === 0,
     `${e.sg_tabn(short)} cards`);
  const end = build(cap.slice(0, 12), []);
  ok('no set out and an empty deck: no triple and no deal, which is the end',
     engineTriples(end).length === 0 && e.sg_candeal(end) === 0 && e.sg_find(end) < 0);
  const refused = build([cap[0], cap[1], y, ...cap.slice(2, 11)], [cap[11]]);
  ok('a deal while a set is out is refused and leaves the table as it was',
     e.sg_candeal(refused) === 0 && e.sg_deal(refused) === refused);
}
console.log(fail === 0
  ? `\nPASS: Set decodes, judges and counts by the rules (${pass} arms).`
  : `\nFAIL: ${fail} of ${pass + fail} arms.`);
process.exitCode = fail === 0 ? 0 : 1;
