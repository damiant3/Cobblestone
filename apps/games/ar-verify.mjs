// Grade every arcade descriptor against its own wasm module.
//
// The page is the thing a person sees, so the thing to check is not "does
// the module load" -- wasm-verify.mjs and the per-game graders already
// answer that. What is new here, and what only this arm can see, is whether
// the DESCRIPTOR drives the module correctly: whether boot returns a live
// handle, whether step actually advances, whether the view reads cells that
// are really there, and whether the game arrives somewhere.
//
// The load-bearing arm is PROGRESS. A descriptor whose step is wired to the
// wrong accessor still returns a handle and still renders a board; it just
// renders the SAME board forever, and a screenshot cannot tell that from a
// game that happens to be slow. So every game must either reach its own
// terminal state or produce a view that changes, and a game that does
// neither is a failure however good it looks.
//
// Usage: node apps/games/ar-verify.mjs [game-id ...]

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import { GAMES, IMPORTS, driver, renderHtml, spCardCode } from '../landing/web/games/arcade.js';
import { RULES } from '../landing/web/games/rules.js';

const here = dirname(fileURLToPath(import.meta.url));
const modDir = join(here, '..', 'landing', 'web', 'games');
const only = process.argv.slice(2);

let pass = 0, fail = 0;
const ok = (name, cond, detail) => {
  if (cond) pass++;
  else { console.log(`  FAIL  ${name}: ${detail}`); fail++; }
  return cond;
};

const shape = v => {
  if (!v) return '';
  if (v.kind === 'grid' || v.kind === 'hex') return v.cells.map(c => c.text + '|' + c.cls).join(',');
  if (v.kind === 'rows') return JSON.stringify(v.rows);
  if (v.kind === 'columns') return JSON.stringify(v.cols);
  // Klondike's DURABLE board only. `takes` and `picked` follow the selection
  // and `fresh` follows the last turn of the stock; none of the three is a
  // card moving, and left in the shape a click that only picked something up
  // would read as progress.
  // Sudoku's DURABLE board only: which digit is in hand follows the picker
  // and is not a square being filled.
  if (v.kind === 'sudoku') return v.cells.map(c => c.text).join(',');
  if (v.kind === 'klondike') {
    return JSON.stringify([
      v.cols.map(col => col.cards.map(c => c.card)),
      v.founds.map(f => f.card), v.stock, v.waste, v.wasteTop,
    ]);
  }
  if (v.kind === 'pair') return JSON.stringify(v.grids);
  // Counts only. The points now carry hint and picked flags that follow the
  // dice and the selection, and neither is a checker moving -- left in the
  // shape, a step that did nothing looked like progress.
  if (v.kind === 'backgammon') {
    return JSON.stringify([v.top.map(p => p.n), v.bottom.map(p => p.n), v.bar, v.off]);
  }
  if (v.kind === 'mancala') return JSON.stringify([v.north, v.south, v.stores]);
  // The DURABLE board only. A die is not progress: Ur's squares carry a
  // `playable` flag and its tray an `enter` index, both of which move when
  // the turn's roll changes and neither of which means a piece moved. Left
  // in, they let a step that does nothing look like a step that did.
  if (v.kind === 'ur') {
    const row = r => r.map(c => c.gap ? '.' : `${c.step}:${c.who}`).join(',');
    return [row(v.top), row(v.mid), row(v.bottom),
    v.trays.youWaiting, v.trays.youHome, v.trays.themWaiting, v.trays.themHome].join('|');
  }
  if (v.kind === 'war') return JSON.stringify([v.left, v.right, v.war]);
  return JSON.stringify(v);
};

const count = v => !v ? 0
  : v.kind === 'grid' || v.kind === 'hex' ? v.cells.length
    : v.kind === 'rows' ? v.rows.length
      : v.kind === 'columns' ? v.cols.length
        : v.kind === 'pair' ? v.grids.length
          : v.kind === 'backgammon' ? v.top.length + v.bottom.length
            : v.kind === 'mancala' ? v.north.length + v.south.length
              : v.kind === 'ur' ? v.top.length + v.mid.length + v.bottom.length
                : v.kind === 'war' ? 2
                : v.kind === 'klondike' ? v.cols.length + v.founds.length
                  : v.kind === 'sudoku' ? v.cells.length : 0;

console.log(`ar-verify: ${GAMES.length} arcade descriptors\n`);

for (const game of GAMES) {
  if (only.length && !only.includes(game.id)) continue;
  const label = game.id.padEnd(14);
  let exports;
  try {
    const bytes = readFileSync(join(modDir, `${game.id}.wasm`));
    exports = new WebAssembly.Instance(new WebAssembly.Module(bytes), IMPORTS).exports;
  } catch (err) {
    ok(`${game.id} loads`, false, err.message);
    console.log(`  ----  ${label} module did not load`);
    continue;
  }

  const d = driver(game, exports);
  let boot, err = null;
  try { boot = d.reset(7); } catch (e) { err = e; }
  if (!ok(`${game.id} boots`, err === null, err && err.message)) continue;
  ok(`${game.id} boot returns a handle`, boot !== null && boot !== undefined && Number.isInteger(boot),
    `boot gave ${boot}`);

  // The view must read something. A descriptor pointed at an accessor that
  // is not there answers undefined for every cell and still renders.
  let first;
  try { first = d.view(); } catch (e) { err = e; }
  if (!ok(`${game.id} renders`, err === null, err && err.message)) continue;
  ok(`${game.id} view has cells`, count(first) > 0, `view held ${count(first)} entries`);
  ok(`${game.id} status reads`, typeof d.status() === 'string' && d.status().length > 0,
    `status was ${JSON.stringify(d.status())}`);
  ok(`${game.id} view holds no undefined`, !shape(first).includes('undefined'),
    'a cell read undefined, so an accessor name is wrong');

  // The page draws with THIS function, so drawing it here is the real
  // check. A view kind the renderer does not handle answers the empty
  // string, which in the browser is a board that is simply not there.
  let html = '';
  try { html = renderHtml(first, true); } catch (e) { err = e; }
  ok(`${game.id} draws`, err === null && html.length > 0,
    err ? err.message : 'the renderer produced nothing for view kind ' + (first && first.kind));
  ok(`${game.id} draws no undefined`, !html.includes('undefined'),
    'the rendered board contains the text "undefined"');

  // Every game gets a rules panel. A game with no rules is a game a
  // visitor has to already know how to play.
  const r = RULES[game.id];
  ok(`${game.id} has rules`, Array.isArray(r) && r.length > 0, 'no entry in rules.js');

  // The carried piece. It runs on every hover, so it must not throw, and
  // the resting place it names has to be a square that exists.
  if (game.ghost) {
    let ghErr = null, offBoard = 0, offered = 0;
    try {
      const n = count(first);
      for (let i = 0; i < n; i++) {
        const g = game.ghost(exports, d.handle, i);
        if (!g) continue;
        offered++;
        if (!Number.isInteger(g.i) || g.i < 0 || g.i >= n) offBoard++;
      }
    } catch (e) { ghErr = e; }
    ok(`${game.id} ghost does not throw`, ghErr === null, ghErr && ghErr.message);
    ok(`${game.id} ghost lands on the board`, offBoard === 0,
      `${offBoard} of ${offered} ghosts named a square off the board`);
    ok(`${game.id} ghost offers the legal moves`, offered > 0,
      'no square on a fresh board offered a piece to place');
  }

  // THE PROGRESS ARM. This one drives the ENGINE playing itself, so it
  // watches: a game where you throw your own dice deliberately does not
  // roll for you on your turn, and stepping in play mode would find no
  // dice and correctly do nothing.
  d.mode = 'watch';
  const cap = game.steps || 200;
  const before = shape(first);
  let steps = 0, ended = false;
  try {
    while (steps < cap) {
      if (!d.step()) { ended = true; break; }
      steps++;
      if (d.done()) { ended = true; break; }
    }
  } catch (e) {
    ok(`${game.id} steps without trapping`, false, `${e.message} after ${steps} steps`);
    continue;
  }
  const after = shape(d.view());

  if (game.step) {
    // `steps > 0` must NOT appear in this test. A descriptor wired to the
    // wrong accessor still runs its cap of steps and still renders; what it
    // does not do is CHANGE. An OR against the step count passes every such
    // descriptor for free, which is the arm reporting that the loop ran.
    ok(`${game.id} advances`, after !== before,
      `${steps} steps left the view identical -- step is not driving the module`);
    ok(`${game.id} arrives`, ended || steps >= cap,
      `stopped after ${steps} steps without finishing or hitting the cap`);
    ok(`${game.id} final view holds no undefined`, !after.includes('undefined'),
      'a cell read undefined after stepping');
  }

  // The whole-game call, where the module offers one. It runs inside a
  // single call, so it is the cheap arm on a bump allocator.
  let runs = null;
  if (game.runs) {
    try { runs = d.runs(); } catch (e) { err = e; }
    ok(`${game.id} runs to completion in one call`, err === null && typeof runs === 'string',
      err ? err.message : `run gave ${runs}`);
  }

  const state = d.done() ? 'finished' : `${steps} steps`;
  console.log(`  ok    ${label} ${state.padEnd(12)} ${d.status()}`);
  if (runs) console.log(`        ${' '.repeat(14)} whole game: ${runs}`);
}

// -- SPIDER DEALS THE DECK IT WAS ASKED FOR ------------------------------
// One, two and four suits are three different games, and the difference is
// entirely in the deck. The suit count is baked into the card ids at deal
// time rather than applied on top of them, so this checks the deck that
// actually came out: a hundred and four cards, eight of every rank, and
// exactly as many distinct suits as were asked for. A deck that quietly
// stays two-suit whatever you pick still plays perfectly well, which is
// why it needs a count rather than an eye.
if (!only.length) {
  let exports = null;
  try {
    exports = new WebAssembly.Instance(
      new WebAssembly.Module(readFileSync(join(modDir, 'spider.wasm'))), IMPORTS).exports;
  } catch { /* reported above */ }
  if (exports) {
    for (const suits of [1, 2, 3, 4]) {
      const h = exports.sp_new(77, suits);
      const cards = [];
      for (let c = 0; c < 10; c++) {
        for (let i = 0; i < exports.sp_coln(h, c); i++) cards.push(exports.sp_card(h, c, i));
      }
      // The tableau holds 54; the rest is still in the stock, so the deck
      // is checked through what is dealt plus what the counters say.
      const dealt = cards.length, stock = exports.sp_stockn(h);
      ok(`spider ${suits}-suit: the whole deck is accounted for`, dealt + stock === 104,
        `${dealt} dealt and ${stock} in stock is ${dealt + stock}`);
      const seen = new Set(cards.filter(c => c >= 0).map(c => exports.sp_suit(c)));
      ok(`spider ${suits}-suit: no more suits than asked for`,
        [...seen].every(s => s >= 0 && s < suits),
        `saw suits ${[...seen].sort().join(',')} for a ${suits}-suit deal`);
      // One suit cannot show four, and four should not quietly deal one.
      // 54 cards is plenty to see every suit at least once.
      ok(`spider ${suits}-suit: every suit is actually in the deal`,
        seen.size === Math.min(suits, seen.size) && seen.size === suits,
        `${seen.size} distinct suits among ${dealt} dealt cards, wanted ${suits}`);
      const ranks = new Set(cards.filter(c => c >= 0).map(c => exports.sp_rank(c)));
      ok(`spider ${suits}-suit: ranks run ace to king`, ranks.size === 13,
        `${ranks.size} distinct ranks`);
    }
    // Two rules the engine did not enforce, both of which leave a perfectly
    // playable game that is not Spider.
    //
    // A deal puts a card on every column, so it may not happen while one is
    // empty. Emptying a column takes real play, so this is checked by
    // driving the engine's own solver until it opens one.
    {
      // Emptying a column takes real play and not every deal allows it, so
      // this searches seeds for a position that has one rather than
      // assuming the first seed will oblige.
      let found = null;
      for (let s = 1; s <= 60 && !found; s++) {
        let h = exports.sp_new(s, 1);
        for (let i = 0; i < 400; i++) {
          if (exports.sp_stockn(h) > 0 &&
            [...Array(10).keys()].some(c => exports.sp_coln(h, c) === 0)) { found = h; break; }
          const m = exports.sp_sugg(h);
          if (m < 0) {
            if (exports.sp_stockn(h) <= 0) break;
            const next = exports.sp_deal(h);
            if (exports.sp_stockn(next) === exports.sp_stockn(h)) break;
            h = next;
          } else {
            h = exports.sp_move(h, exports.sp_mfrom(m), exports.sp_mstart(m), exports.sp_mto(m));
          }
        }
      }
      ok('spider: control, a position with an empty column was reached', found !== null,
        'no seed in 60 opened a column, so the deal rule below was never exercised');
      if (found) {
        const before = [...Array(10).keys()].map(c => exports.sp_coln(found, c)).join(',');
        const after = exports.sp_deal(found);
        const cols = [...Array(10).keys()].map(c => exports.sp_coln(after, c)).join(',');
        ok('spider: no dealing while a column stands empty', cols === before,
          `columns went ${before} to ${cols} with an empty column on the board`);
      }
    }

    // -- SPLITTING A RUN ---------------------------------------------------
    // Naming a CARD moves that card and the ones on top of it and no more,
    // which is what splitting a run is and what the page could not express
    // while a selection was only a column number. Driven through the
    // DESCRIPTOR, not the module, because the split lives in the page.
    //
    // The control is that cards were LEFT BEHIND from the same run, which
    // is the whole difference between splitting and lifting the run whole:
    // before the page could name a card it always lifted from the run's
    // start, so `source left` would read `from` and not the card clicked.
    //
    // Clicking the COLUMN is deliberately NOT the control, though it looks
    // like the obvious one. Onto a NON-EMPTY destination `sp-can-move`
    // wants the lifted card exactly one rank below the destination's top
    // card, and a run holds each rank once, so exactly one start in it can
    // ever be legal for a given destination: the column click and the card
    // click are obliged to agree, and asserting they differ asserts
    // something the rules make impossible. Only an empty destination takes
    // any start, and no column is empty at the deal.
    {
      const g = GAMES.find(x => x.id === 'spider');
      const runStart = (h, c, n) => {
        let s = n - 1;
        while (s > 0 && exports.sp_seqlen(h, c, s - 1) === n - (s - 1)) s--;
        return s;
      };
      // A position where a run of two or more can be lifted from strictly
      // inside itself onto somewhere that will take it.
      let hit = null;
      for (let seed = 1; seed <= 400 && !hit; seed++) {
        const h = exports.sp_new(seed, 1);
        for (let c = 0; c < 10 && !hit; c++) {
          const n = exports.sp_coln(h, c);
          const from = n ? runStart(h, c, n) : 0;
          if (!n || n - from < 2) continue;
          for (let s = from + 1; s < n && !hit; s++) {
            for (let d = 0; d < 10 && !hit; d++) {
              if (d !== c && exports.sp_can(h, c, s, d) === 1) hit = { seed, c, s, d, n, from };
            }
          }
        }
      }
      ok('spider: control, a splittable run was found at all', hit !== null,
        hit ? `seed ${hit.seed} col ${hit.c} run from ${hit.from}, lifting at ${hit.s}`
          : 'no seed in 400 offered a run that could be lifted from inside itself, '
            + 'so the split arms below measured nothing');
      if (hit) {
        const play = code => {
          const dr = driver(g, exports);
          dr.reset(hit.seed);
          dr.clearSel();
          const first = dr.move(code);
          const landed = dr.move(hit.d);
          const v = dr.view();
          return { first, landed, cols: v.cols.map(col => (col[0].cls.includes('empty') ? 0 : col.length)) };
        };
        const base = exports.sp_coln(exports.sp_new(hit.seed, 1), hit.d);
        const exact = play(spCardCode(hit.c, hit.s));
        ok('spider: clicking a card holds a selection', exact.first === 'select', exact.first);
        ok('spider: a run splits where you clicked, and moves nothing else',
          exact.landed === 'moved'
          && exact.cols[hit.d] === base + (hit.n - hit.s)
          && exact.cols[hit.c] === hit.s,
          `${exact.landed}: destination ${base} to ${exact.cols[hit.d]} `
          + `(wanted ${base + (hit.n - hit.s)}), source left ${exact.cols[hit.c]} (wanted ${hit.s})`);
        ok('spider: control, the split left the rest of the run behind',
          hit.s > hit.from && exact.cols[hit.c] === hit.s,
          `lifted at ${hit.s} from a run starting at ${hit.from}, so `
          + `${hit.s - hit.from} card(s) of it stayed and ${hit.n - hit.s} went`);
      }
    }

    // -- KLONDIKE'S CLICK MODEL --------------------------------------------
    // Klondike names more places than a column index can hold, so a click is
    // a code: 90 the stock, 91 the waste, 92 to 95 the foundations, 0 to 6 a
    // column, and 10000 + column * 1000 + index a card inside one. Driven
    // through the DESCRIPTOR, because that mapping lives in the page and
    // nothing in the module can be wrong about it.
    {
      const g = GAMES.find(x => x.id === 'klondike');
      const ex = new WebAssembly.Instance(new WebAssembly.Module(
        readFileSync(join(modDir, 'klondike.wasm'))), IMPORTS).exports;

      // Clicking the stock turns a card. It is the one click in this game
      // that is always available and always means the same thing.
      {
        const d = driver(g, ex);
        d.reset(3);
        const before = d.view();
        const r = d.move(90);
        const after = d.view();
        ok('klondike: clicking the stock turns a card', r === 'moved', String(r));
        ok('klondike: and the card leaves the stock for the waste',
          after.stock === before.stock - 1 && after.waste === before.waste + 1,
          `stock ${before.stock} to ${after.stock}, waste ${before.waste} to ${after.waste}`);
        ok('klondike: the turned card is now readable', after.wasteTop >= 0 && after.wasteTop < 52,
          `${after.wasteTop}`);
      }

      // An ace off the waste goes to its foundation, through the page. This
      // is the move the whole game is built on, so it is hunted for rather
      // than assumed, and the hunt says whether it found one (L-VACUOUS).
      {
        let found = 0, landed = 0;
        for (let seed = 1; seed <= 120 && found < 8; seed++) {
          const d = driver(g, ex);
          d.reset(seed);
          for (let turn = 0; turn < 24 && found < 8; turn++) {
            if (d.move(90) !== 'moved') break;
            const v = d.view();
            const top = v.wasteTop;
            if (top < 0) continue;
            const r = top % 13 === 12 ? 0 : (top % 13) + 1;
            if (r !== 0) continue;
            const suit = Math.floor(top / 13);
            found++;
            d.clearSel();
            if (d.move(91) !== 'select') continue;
            if (d.move(92 + suit) !== 'moved') continue;
            const after = d.view();
            if (after.founds[suit].card === top) landed++;
          }
        }
        ok('klondike: control, an ace reached the waste at all', found > 0, `${found} aces`);
        ok('klondike: an ace clicked from the waste lands on its foundation',
          found > 0 && landed === found, `${landed} of ${found} landed`);
      }

      // A card click splits a run where you clicked, the same way Spider's
      // does. The position has to be PLAYED to, not dealt to: at the deal
      // every column holds exactly one face-up card, so a run of two or more
      // does not exist until cards have been moved onto each other. The hunt
      // therefore lets the game's own player advance the board and looks
      // after each step. Everything here is deterministic, so recording the
      // step count is enough to reach the same position again.
      {
        let hit = null;
        for (let seed = 1; seed <= 60 && !hit; seed++) {
          const d = driver(g, ex);
          d.reset(seed);
          for (let step = 0; step < 200 && !hit; step++) {
            const h = d.handle;
            for (let c = 0; c < 7 && !hit; c++) {
              const n = ex.kd_coln(h, c);
              const dn = ex.kd_down(h, c);
              for (let s = dn + 1; s < n && !hit; s++) {
                if (ex.kd_runlen(h, c, s) !== n - s) continue;
                for (let to = 0; to < 7 && !hit; to++) {
                  if (to !== c && ex.kd_can(h, c, s, to) === 1) {
                    hit = { seed, c, s, to, n, dn, step };
                  }
                }
              }
            }
            if (!hit && !d.step()) break;
          }
        }
        ok('klondike: control, a splittable run was found at all', hit !== null,
          hit ? `seed ${hit.seed} col ${hit.c} lifting at ${hit.s} of ${hit.n} `
            + `after ${hit.step} steps`
            : 'no seed in 60 played into one, so the split arm measured nothing');
        if (hit) {
          const d = driver(g, ex);
          d.reset(hit.seed);
          for (let step = 0; step < hit.step; step++) d.step();
          const base = ex.kd_coln(d.handle, hit.to);
          d.clearSel();
          const first = d.move(10000 + hit.c * 1000 + hit.s);
          const landed = d.move(hit.to);
          const h = d.handle;
          ok('klondike: clicking a card holds a selection', first === 'select', String(first));
          ok('klondike: a run splits where you clicked, and moves nothing else',
            landed === 'moved'
            && ex.kd_coln(h, hit.to) === base + (hit.n - hit.s)
            && ex.kd_coln(h, hit.c) === hit.s,
            `${landed}: destination ${base} to ${ex.kd_coln(h, hit.to)} `
            + `(wanted ${base + (hit.n - hit.s)}), source left ${ex.kd_coln(h, hit.c)} `
            + `(wanted ${hit.s})`);
        }
      }

      // The module must never hand the page a face-down card. This is the
      // one thing a solitaire cannot get wrong without the game being a lie,
      // and it is decidable rather than a matter of judgement.
      {
        const d = driver(g, ex);
        d.reset(9);
        let leaked = 0, hidden = 0;
        for (let turn = 0; turn < 30; turn++) {
          const h = d.handle;
          for (let c = 0; c < 7; c++) {
            const dn = ex.kd_down(h, c);
            hidden += dn;
            for (let i = 0; i < dn; i++) if (ex.kd_card(h, c, i) !== -1) leaked++;
          }
          if (d.move(90) !== 'moved') break;
        }
        ok('klondike: control, there were face-down cards to leak', hidden > 0, `${hidden} seen`);
        ok('klondike: no face-down card is ever readable from the module',
          leaked === 0, `${leaked} leaked`);
      }
    }

    // -- WHO LOSES A CHALLENGE ---------------------------------------------
    // The heart of Liar's Dice, and the easiest thing in it to get exactly
    // backwards: calling a bid that was TRUE costs the caller a die, and
    // calling one that was false costs the bidder. Both outcomes lose
    // somebody a die and reroll the table, so a swapped rule still produces
    // a game that runs to a winner and looks entirely normal.
    //
    // Checked against the real count read BEFORE the call, because settling
    // rerolls every die and the number that decided it is gone afterwards.
    // Both branches are counted so a run that only ever saw one of them
    // cannot pass as having tested the rule.
    {
      const g = GAMES.find(x => x.id === 'liarsdice');
      const ex = new WebAssembly.Instance(new WebAssembly.Module(
        readFileSync(join(modDir, 'liarsdice.wasm'))), IMPORTS).exports;
      // READ THE COUNTS BACK OUT OF THE NEW STATE. Reading them from the
      // handle the call was made ON compares the position with itself and
      // finds no loser ever, which is an arm that reports the rule broken
      // in every single case and is really reporting nothing at all.
      //
      // The engine's own player bids conservatively and almost never lies,
      // so the false-bid branch is REACHED DELIBERATELY: bidding the whole
      // table on one face is legal and is almost never true, and without
      // forcing it this arm would test one branch and claim to test two.
      let trueBids = 0, lies = 0, wrongLoser = 0;
      for (let seed = 1; seed <= 120 && trueBids + lies < 40; seed++) {
        let h = ex.ld_new(seed, 4);
        for (let t = 0; t < 80 && ex.ld_done(h) !== 1; t++) {
          if (ex.ld_cancall(h) !== 1) {
            // Force outrageous bids until enough lies have been called, then
            // let the engine's own player bid for the true-bid half. Keyed
            // on the count of lies SEEN rather than on the loop counter, so
            // the branch is reached whatever order the turns happen to take.
            const total = ex.ld_total(h);
            if (lies < 12 && ex.ld_canbid(h, total, 6) === 1) h = ex.ld_bidat(h, total, 6);
            else h = ex.ld_step(h);
            continue;
          }
          const p = ex.ld_turn(h);
          const bidder = ex.ld_lastbid(h);
          const qty = ex.ld_qty(h), actual = ex.ld_actual(h);
          const before = [...Array(4)].map((_, k) => ex.ld_dice(h, k));
          const next = ex.ld_call(h);
          const after = [...Array(4)].map((_, k) => ex.ld_dice(next, k));
          const lost = [...Array(4).keys()].filter(k => after[k] < before[k]);
          const shouldLose = actual >= qty ? p : bidder;
          if (actual >= qty) trueBids++; else lies++;
          if (lost.length !== 1 || lost[0] !== shouldLose) wrongLoser++;
          h = next;
        }
      }
      ok('liarsdice: control, both a true bid and a lie were called',
        trueBids > 0 && lies > 0, `${trueBids} true, ${lies} lies`);
      ok('liarsdice: the wrong one loses the die, and only one does',
        wrongLoser === 0, `${wrongLoser} of ${trueBids + lies} settled on the wrong player`);
    }

    // -- THE SUIT AN EIGHT NAMES IS THE PLAYER'S -----------------------------
    // `ce-play-card` calls `ce-best-suit-for-player`, which is right for the
    // engine's own player and wrong for a person: declaring is the whole
    // reason to hold an eight. The page plays through that function and sets
    // the declared suit afterwards, so what has to be proven is that the
    // override actually lands.
    //
    // THE CONTROL IS THAT THE NAMED SUIT DIFFERS FROM THE ENGINE'S OWN
    // CHOICE. Naming whatever it would have picked anyway passes whether the
    // override works or not, which is an arm that cannot fail; the suit
    // asked for here is deliberately one along from the automatic pick, and
    // the count of how often they differ is reported so a run where they
    // never did is visible rather than silently vacuous.
    {
      const g = GAMES.find(x => x.id === 'crazyeights');
      const ex = new WebAssembly.Instance(new WebAssembly.Module(
        readFileSync(join(modDir, 'crazyeights.wasm'))), IMPORTS).exports;
      let found = 0, honoured = 0, differed = 0;
      for (let seed = 1; seed <= 300 && found < 10; seed++) {
        const d = driver(g, ex);
        d.reset(seed);
        for (let t = 0; t < 60 && !d.done() && found < 10; t++) {
          const h = d.handle;
          if (ex.ce_cur(h) !== 0) { d.step(); continue; }
          const eight = [...Array(52).keys()].find(c =>
            ex.ce_has(h, 0, c) === 1 && ex.ce_rank(c) === 6 && ex.ce_canplay(h, c) === 1);
          if (eight === undefined) {
            const any = [...Array(52).keys()].find(c =>
              ex.ce_has(h, 0, c) === 1 && ex.ce_canplay(h, c) === 1);
            if (any !== undefined) d.move(500 + any);
            else if (ex.ce_canpen(h) === 1) d.act(1);
            else if (ex.ce_stuck(h) === 1) d.act(0);
            else break;
            continue;
          }
          found++;
          const auto = ex.ce_declared(ex.ce_play(h, eight, -1));
          const want = (auto + 1) % 4;
          if (want !== auto) differed++;
          d.clearSel();
          const held = d.move(500 + eight);
          const laid = d.move(560 + want);
          if (held === 'select' && laid === 'moved' && ex.ce_declared(d.handle) === want) {
            honoured++;
          }
          break;
        }
      }
      ok('crazyeights: control, the player was offered an eight to play',
        found > 0, `${found} positions`);
      ok('crazyeights: control, the suit named differs from the engine\'s own pick',
        differed === found, `${differed} of ${found} differ`);
      ok('crazyeights: an eight calls the suit YOU name',
        found > 0 && honoured === found, `${honoured} of ${found} honoured`);
    }

    // -- ONE RULES ENTRY PER GAME ------------------------------------------
    // A duplicate key in an object literal is NOT an error in JavaScript:
    // the last one silently wins. Lifting six games to playable gave each of
    // them a second rules entry beside the one already there, and for
    // yahtzee the stale entry came later and won, so the panel went on
    // describing a game you could only watch and never said you can click
    // dice to hold them. Two of those pairs reached main before anyone
    // looked.
    //
    // THE OBJECT CANNOT SHOW THIS. By the time `RULES` is imported the
    // duplicates are already collapsed and it looks perfectly well formed,
    // so the only place the defect is visible is the SOURCE TEXT.
    {
      const src = readFileSync(join(modDir, 'rules.js'), 'utf8');
      const keys = [...src.matchAll(/^ {2}([a-z0-9]+): \[/gm)].map(m => m[1]);
      const dupes = [...new Set(keys.filter((k, i) => keys.indexOf(k) !== i))];
      ok('control: rules.js was read and holds entries', keys.length > 20,
        `${keys.length} keys matched`);
      ok('rules.js names each game exactly once', dupes.length === 0,
        dupes.length
          ? `${dupes.join(', ')} appear twice -- the later entry silently wins`
          : `${keys.length} entries, no repeats`);
    }

    // -- THE CARD FACE -----------------------------------------------------
    // A card is drawn, and the number of pips IS the rank. Counted here
    // rather than shown as a table for somebody to read, because a table
    // becomes decoration the first time nobody reads it. Spider's opening
    // deal is the fixture: 54 cards face up, every rank among them.
    {
      const g = GAMES.find(x => x.id === 'spider');
      const ex = new WebAssembly.Instance(new WebAssembly.Module(
        readFileSync(join(modDir, 'spider.wasm'))), IMPORTS).exports;
      const d = driver(g, ex);
      d.reset(4);
      const html = renderHtml(d.view(), true);
      const faces = html.match(
        /<span class="card[^"]*"[^>]*>.*?<\/i>(?:<span class="pips[^"]*">.*?<\/span>|<span class="court">.*?<\/span>)<\/span>/g) || [];
      const want = { A: 1, 2: 2, 3: 3, 4: 4, 5: 5, 6: 6, 7: 7, 8: 8, 9: 9, 10: 10 };
      const ranks = new Set();
      const wrong = [];
      for (const f of faces) {
        const r = /<i>(10|[2-9AJQK])<em>/.exec(f)[1];
        ranks.add(r);
        const pips = (f.match(/class="pip[ "]/g) || []).length;
        if (want[r] !== undefined) {
          if (pips !== want[r]) wrong.push(`${r} drew ${pips} pips`);
        } else if (!/class="court"/.test(f)) wrong.push(`${r} is not drawn as a court`);
      }
      ok('cards: control, the deal put every rank on the table',
        faces.length === 54 && ranks.size === 13,
        `${faces.length} faces over ${ranks.size} ranks`);
      ok('cards: the pip count IS the rank, and a court is a court',
        wrong.length === 0, wrong.slice(0, 4).join('; ') || `${faces.length} faces`);
      ok('cards: an ace is one large mark', /class="pips a1"/.test(html));

      // The dealt row is a ONE-SHOT. Left set it would replay the deal on
      // every later repaint, which is the difference between a deal that
      // lands and a board that twitches whenever anything is redrawn.
      const acts = d.actions().filter(a => a.enabled);
      const dealAct = acts.find(a => /deal/i.test(a.label));
      if (dealAct && d.act(dealAct.index)) {
        const dealt = (renderHtml(d.view(), true).match(/dealt/g) || []).length;
        const again = (renderHtml(d.view(), true).match(/dealt/g) || []).length;
        ok('spider: a deal marks the ten cards it dealt', dealt === 10, `${dealt} marked`);
        ok('spider: and the mark is gone by the next repaint', again === 0, `${again} still marked`);
      }
    }

    // Only a real sequence may be lifted. `sp-can-move` asks about the
    // DESTINATION, so reached from a browser an arbitrary handful would go.
    {
      const h = exports.sp_new(9, 4);
      let tested = 0, allowed = 0;
      for (let c = 0; c < 10; c++) {
        const n = exports.sp_coln(h, c);
        const run = n > 0 ? exports.sp_seqlen(h, c, n - 1) : 0;
        // A start ABOVE the legal run is not a sequence, so nothing may
        // accept it from any column.
        for (let start = 0; start < n - run; start++) {
          for (let to = 0; to < 10; to++) {
            if (to === c) continue;
            tested++;
            if (exports.sp_can(h, c, start, to) === 1) allowed++;
          }
        }
      }
      ok('spider: a handful that is not a sequence cannot be lifted', allowed === 0,
        `${allowed} of ${tested} non-sequence pickups were allowed`);
      ok('spider: control, there were non-sequence pickups to refuse', tested > 0,
        'every column was a single clean run, so nothing was tested');
    }
  }
}

// -- THE DICE HAVE TO BE DICE --------------------------------------------
// `bg-wasm-die` is `1 + (seed mod 6)`: rng-new stores the seed without
// advancing it, so the number handed across IS the die. That makes the
// quality of the driver's generator a game-visible fact, and it reads the
// LOW bits, which is where a linear congruential generator is at its worst
// -- Rng.codex calls its low bit "the comb 0101". Fed by an LCG the dice
// threw doubles constantly and repeated a number four turns running.
//
// So: draw a lot, and check the shape. Every face must appear, no face may
// run away with it, and doubles must land near one in six.
if (!only.length) {
  let exports = null;
  try {
    exports = new WebAssembly.Instance(
      new WebAssembly.Module(readFileSync(join(modDir, 'backgammon.wasm'))), IMPORTS).exports;
  } catch { /* reported above */ }
  if (exports) {
    const g = GAMES.find(x => x.id === 'backgammon');
    const d = driver(g, exports);
    d.reset(12345);
    const faces = [0, 0, 0, 0, 0, 0, 0];
    let doubles = 0, runs = 0, prev = null, N = 3000;
    for (let i = 0; i < N; i++) {
      const t = g.beginTurn(exports, d.handle, d.randForTest);
      const [a, b] = t.dice;
      faces[a]++; faces[b]++;
      if (a === b) doubles++;
      if (prev !== null && a === prev) runs++;
      prev = a;
    }
    const bad = faces.slice(1).filter(n => n === 0).length;
    ok('backgammon: every face of the die turns up', bad === 0,
      `faces 1-6 came up ${faces.slice(1).join(',')}`);
    const lo = Math.min(...faces.slice(1)), hi = Math.max(...faces.slice(1));
    ok('backgammon: no face runs away with it', hi < lo * 2,
      `least ${lo}, most ${hi} over ${2 * N} throws`);
    ok('backgammon: doubles land near one in six',
      doubles > N * 0.10 && doubles < N * 0.23,
      `${doubles} doubles in ${N} throws, ${(100 * doubles / N).toFixed(1)} per cent`);
    ok('backgammon: one throw does not predict the next',
      runs < N * 0.28, `the first die repeated ${runs} times in ${N}, ` +
      `${(100 * runs / N).toFixed(1)} per cent against an expected 17`);
  }
}

// -- BACKGAMMON OPENS WHERE BACKGAMMON OPENS -----------------------------
// The opening position is not a matter of taste, it is a fact with a number
// attached: two on the 24-point, five on the 13, three on the 8, five on
// the 6, which is 167 pips for each side. A board whose signs are inverted
// relative to the direction of travel is still symmetric and still plays,
// so no conservation or progress arm can see it -- it just puts both sides
// on the wrong opening, and it counted 208.
if (!only.length) {
  let exports = null;
  try {
    exports = new WebAssembly.Instance(
      new WebAssembly.Module(readFileSync(join(modDir, 'backgammon.wasm'))), IMPORTS).exports;
  } catch { /* the module arm above already reported this */ }
  if (exports) {
    const h = exports.bg_new();
    const pts = [...Array(24).keys()].map(i => exports.bg_point(h, i));
    // White bears off below 0, so White's n-point is index n-1; Black bears
    // off above 23, so Black's n-point is index 24-n.
    let wPips = 0, bPips = 0, wMen = 0, bMen = 0;
    pts.forEach((v, i) => {
      if (v > 0) { wPips += v * (i + 1); wMen += v; }
      if (v < 0) { bPips += -v * (24 - i); bMen += -v; }
    });
    ok('backgammon: fifteen checkers a side', wMen === 15 && bMen === 15,
      `White ${wMen}, Black ${bMen}`);
    ok('backgammon: White opens on 167 pips', wPips === 167, `counted ${wPips}`);
    ok('backgammon: Black opens on 167 pips', bPips === 167, `counted ${bPips}`);
    const want = { 23: 2, 12: 5, 7: 3, 5: 5 };
    ok('backgammon: White sits on the 24, 13, 8 and 6 points',
      Object.entries(want).every(([i, n]) => pts[i] === n),
      'White holds ' + pts.map((v, i) => v > 0 ? `${i}:${v}` : '').filter(Boolean).join(' '));
    ok('backgammon: Black mirrors it',
      Object.entries(want).every(([i, n]) => pts[23 - i] === -n),
      'Black holds ' + pts.map((v, i) => v < 0 ? `${i}:${v}` : '').filter(Boolean).join(' '));
  }
}

// -- DOES THE RESULT NAME SOMEBODY? --------------------------------------
// Every engine writes its winner convention down in its own
// format-*-result, and they disagree: Othello numbers players from one and
// Checkers from zero, so a draw is 0 in one and -1 in the other. A label
// array indexed by the wrong convention does not throw -- it answers the
// neighbouring label, or nothing at all. That is how a won game of
// checkers came out as "a draw" and backgammon announced that "  bears off
// last", and neither is visible to any arm that only checks the board.
//
// So: play many games to the end and read the SENTENCE. A finished game
// must name a winner or say plainly that nobody won, and must never
// contain an empty name or the word undefined.
if (!only.length) {
  for (const game of GAMES) {
    let exports;
    try {
      exports = new WebAssembly.Instance(
        new WebAssembly.Module(readFileSync(join(modDir, `${game.id}.wasm`))), IMPORTS).exports;
    } catch { continue; }
    const bad = [];
    let ended = 0;
    for (let s = 1; s <= 25; s++) {
      const d = driver(game, exports);
      d.mode = 'watch';
      d.reset(s);
      for (let i = 0; i < (game.steps || 200) && !d.done(); i++) if (!d.step()) break;
      if (!d.done()) continue;
      ended++;
      const line = d.status();
      if (/undefined/.test(line)) bad.push(`seed ${s}: "${line}" holds undefined`);
      // A label array indexed off the end yields '', which shows up as a
      // leading space, a doubled space, or an empty run between separators.
      // The test has to be exactly that and no wider: matching every "· "
      // separator flags every well-formed line in the file.
      else if (/^\s/.test(line) || /\s{2,}/.test(line) || /·\s*·/.test(line) || /·\s*$/.test(line)) {
        bad.push(`seed ${s}: "${line}" has a gap where a name should be`);
      }
    }
    ok(`${game.id}: a finished game names a result`, bad.length === 0,
      bad.slice(0, 2).join('; '));
    if (game.runs) {
      const d = driver(game, exports);
      d.reset(3);
      const r = d.runs();
      ok(`${game.id}: the whole-game summary names a result`,
        typeof r === 'string' && !/undefined/.test(r) && !/\s{2,}/.test(r), `run said "${r}"`);
    }
  }
}

// -- CAN A PERSON ACTUALLY PLAY IT? --------------------------------------
// "You can play this game" is a claim about a whole sitting, not about one
// accepted click, and the failure it hides is specific: a page that takes
// your move and then sits there, because nothing tells the opponent to
// answer. So this plays a COMPLETE game through the same driver the page
// uses -- the human takes the first legal move on offer, the opponent
// replies -- and requires it to end.
//
// It also pins the rule Damian set: you are player one and you are on the
// go. A game that opens on the opponent's turn fails here, not in the page
// where somebody has to notice the board moved before they touched it.
if (!only.length) {
  for (const game of GAMES) {
    if (!(game.move || game.keys)) continue;
    let exports;
    try {
      exports = new WebAssembly.Instance(
        new WebAssembly.Module(readFileSync(join(modDir, `${game.id}.wasm`))), IMPORTS).exports;
    } catch { continue; }

    const d = driver(game, exports);
    d.reset(11);
    ok(`${game.id}: opens in play mode`, d.mode === 'play', `opened in ${d.mode}`);
    if (!game.solo) {
      ok(`${game.id}: YOU are player one and on the go`,
        d.turn() === game.human && d.yourTurn(),
        `turn is ${d.turn()}, you are ${game.human}, yourTurn ${d.yourTurn()}`);
    }

    // Counting accepted clicks is not counting moves. A click that only
    // holds a selection, or an action whose run answers nothing, still
    // returns true, and a tally of those reads as a game being played while
    // the board sits untouched. So every move claimed here has to be
    // witnessed by the VIEW changing.
    let mine = 0, theirs = 0, offTurn = 0, guard = 0, hollow = 0, spin = 0, myTurns = 0;
    // How far up the click codes this game's board goes. 130 covers a grid
    // and a handful of named targets, which is every game that was playable
    // when this loop was written. It is NOT a property of the harness: a
    // game whose codes run past it simply cannot be played here, and the
    // arm then reports "unfinished, no move refused" -- which reads like a
    // stalled game and is really a scan that never reached the board.
    // Mahjong's tiles run to 143; Yahtzee names five dice at 200 and
    // thirteen boxes at 210; Sudoku's digit picker sits at 101.
    const WIDTHS = {
      spider: 10, mahjong: 144, yahtzee: 223, sudoku: 110, gofish: 328,
      mastermind: 416, crazyeights: 564, liarsdice: 650,
    };
    const width = WIDTHS[game.id] === undefined ? 130 : WIDTHS[game.id];
    while (!d.done() && guard++ < 300) {
      const boardBefore = shape(d.view());
      d.beginTurn();
      let moved = false;
      // Two phases, because a two-click move needs them. Scanning from zero
      // for the destination would re-click the SELECTED square first and
      // put the piece straight back down, so phase two skips it -- without
      // that, checkers can hold a piece and never find anywhere to put it.
      // A checkers destination also sits at a LOWER index than its source,
      // South moving up the board, so neither phase may scan forward only.
      // A game picked THREE at a time cannot be driven by the two-phase scan
      // below at all: the scan clears the selection between clicks and would
      // have to walk every triple to find one that is a set. Set declares
      // `picks: 3` and is driven through its own finder instead.
      //
      // BE CLEAR WHAT THIS ARM THEN PROVES. It proves that three clicks
      // through `move` really make a take and advance the game. It does NOT
      // prove that a set can be found from the board, because the finder is
      // what handed over the indices -- that half is graded in the module's
      // own arms, where the finder is checked against the rule.
      if (game.picks === 3 && game.hint) {
        const trio = game.hint(exports, d.handle);
        if (trio) {
          d.clearSel();
          let last = null;
          for (const c of trio) last = d.move(c);
          if (last === 'moved') { moved = true; mine++; }
        }
      }
      for (let src = 0; src < width && !moved; src++) {
        if (!d.yourTurn()) { offTurn++; break; }
        d.clearSel();
        const r = d.move(src);
        if (!r) continue;
        if (r === 'moved') { moved = true; mine++; break; }
        if (d.sel === null || d.sel === undefined) continue;
        const held = d.sel;
        for (let dst = 0; dst < width; dst++) {
          if (dst === held) continue;
          if (d.move(dst) === 'moved') { moved = true; mine++; break; }
          // A click on another of your own pieces picks THAT one up
          // instead. Giving up there leaves whole destinations untried,
          // which reads as a stalled game when a legal move plainly
          // exists: put the original piece back and keep looking.
          if (d.sel !== held) { d.clearSel(); d.move(held); }
        }
      }
      let byAction = false;
      if (!moved) {
        d.clearSel();
        // No legal click. Either an action carries the turn (Ur passes on a
        // roll of nought, Spider deals) or the game is genuinely stuck.
        const acts = d.actions().filter(a => a.enabled);
        if (acts.length && d.act(acts[0].index)) { mine++; byAction = true; }
        else if (game.keys) { if (!Object.keys(game.keys).some(k => d.key(k))) break; else mine++; }
        else break;
      }
      // A turn is not always one click. Counting the human's TURNS as the
      // times the opponent was handed the board is what makes the arm below
      // ask about the turn passing rather than about how many clicks a turn
      // costs; for the games where a turn IS one click the two counts are
      // the same number and nothing about them changes.
      const answered = d.reply();
      theirs += answered;
      if (answered > 0) myTurns++;
      // Do NOT clear the turn here. A game where you throw your own dice
      // holds them in the turn state, and wiping it every iteration means
      // the roll is discarded before it can be used -- which reads as three
      // hundred moves against an opponent that never answers.
      // A pass is an action that legitimately leaves the board alone, so
      // only a turn that claimed a MOVE has to show for it.
      if (moved && shape(d.view()) === boardBefore) hollow++;
      // An action that changes no board and yields no turn has done
      // nothing. Once is a die being thrown; thirty times is a loop.
      if (!moved && byAction && shape(d.view()) === boardBefore && d.yourTurn()) spin++;
      else spin = 0;
      if (spin > 30) break;
    }

    ok(`${game.id}: a whole game plays out from clicks`, mine > 0,
      `the human never got a legal move in`);
    ok(`${game.id}: no action loops without advancing the turn`, spin <= 30,
      `an action fired ${spin} times running while the board and the turn both stood still`);
    ok(`${game.id}: every turn taken changes the board`, hollow === 0,
      `${hollow} of ${guard} turns left the board identical -- clicks are being accepted that do nothing`);
    ok(`${game.id}: the human never moved out of turn`, offTurn === 0,
      `${offTurn} moves attempted off turn`);
    if (!game.solo) {
      // "More than none" is far too weak a test. An opponent that answers
      // twice in a game the human plays eighty turns of is not playing, and
      // it reads as a win. In a game where the turn really alternates the
      // two sides take comparably many TURNS, so hold it to a quarter.
      //
      // Turns, not clicks. Held against clicks this said Hex War's turn was
      // not passing at 181 to 15, when a Hex War turn is legitimately a
      // dozen clicks: every unit steps one hex at a time while its movement
      // lasts. The two sides were alternating perfectly.
      ok(`${game.id}: the opponent answers`, theirs >= Math.max(1, myTurns / 4),
        `the human took ${myTurns} turns (${mine} clicks) and the opponent ${theirs}`
        + ` -- the turn is not passing`);
    }
    ok(`${game.id}: the game reaches an end`, d.done() || guard >= 300,
      `stopped after ${mine} of your moves with the game unfinished and no move refused`);
    console.log(`  ok    ${game.id.padEnd(14)} played  you ${String(mine).padStart(3)} · them ${String(theirs).padStart(3)} · ${d.done() ? 'finished' : 'open'} · ${d.status()}`);
  }
}

// -- THE CONTROL: does the progress arm fail when it should? --------------
// An arm that has only ever been seen passing is an assertion, not a test.
// This breaks each descriptor the way a real miswiring breaks it -- step
// returns the handle it was given, which is exactly what tictactoe did --
// and requires the arm to catch every one. A descriptor that survives the
// break is one whose progress arm is vacuous.
if (!only.length) {
  let caught = 0, missed = [];
  for (const game of GAMES) {
    if (!game.step) continue;
    let exports;
    try {
      exports = new WebAssembly.Instance(
        new WebAssembly.Module(readFileSync(join(modDir, `${game.id}.wasm`))), IMPORTS).exports;
    } catch { continue; }
    const broken = { ...game, step: (e, h) => h };
    const d = driver(broken, exports);
    try {
      d.reset(7);
      const before = shape(d.view());
      for (let i = 0; i < (game.steps || 200) && !d.done(); i++) if (!d.step()) break;
      if (shape(d.view()) === before) caught++; else missed.push(game.id);
    } catch { caught++; }
  }
  ok('CONTROL: a step wired to a no-op is caught for every game',
    missed.length === 0, `${missed.join(', ')} still looked like ${missed.length === 1 ? 'it advanced' : 'they advanced'}`);
  console.log(`\n  ok    control        ${caught} descriptors go red when step stops advancing`);
}

console.log(fail === 0
  ? `\nPASS: every arcade descriptor drives its module (${pass} arms).`
  : `\nFAIL: ${fail} of ${pass + fail} arms.`);
process.exitCode = fail === 0 ? 0 : 1;
