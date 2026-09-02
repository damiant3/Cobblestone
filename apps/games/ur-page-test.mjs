// Drive the SHIPPED Royal Ur page's own script against the real module.
//
// ur-verify.mjs grades the engine; this grades the page. It runs the inline
// script out of royalur.html in a stub DOM with timers we control, so what
// is under test is the code a visitor actually gets, not a copy of it.
//
// The arm that matters most here is the heap one. This page must NOT reset
// the module heap between calls, because the page holds a HANDLE into that
// heap; a page that reset per call the way the Tic-Tac-Toe page does would
// hand the module a freed address. That is invisible in a screenshot and
// fatal in play, so it is asserted directly.
//
// Usage: node apps/games/ur-page-test.mjs

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import vm from 'node:vm';

const here = dirname(fileURLToPath(import.meta.url));
const pagePath = join(here, '..', 'landing', 'web', 'games', 'royalur.html');
const wasmPath = join(here, '..', 'landing', 'web', 'games', 'royalur.wasm');

const html = readFileSync(pagePath, 'utf8');
const scripts = [...html.matchAll(/<script>([\s\S]*?)<\/script>/g)].map(m => m[1]);
if (scripts.length !== 1) throw new Error(`expected one inline script, found ${scripts.length}`);

// --- the stub DOM ----------------------------------------------------
const mkEl = id => ({
  id, innerHTML: '', textContent: '', dataset: {}, value: '5', disabled: false,
  _classes: new Set(),
  classList: {
    toggle(c, on) { on ? this._o._classes.add(c) : this._o._classes.delete(c); },
  },
  addEventListener() {},
});
const els = {};
for (const id of ['speed', 'status', 'roll', 'rollbtn', 'p1', 'p2',
                  's1', 's2', 'n1', 'n2', 'm-1', 'm-2', 'm-w']) {
  els[id] = mkEl(id);
  els[id].classList._o = els[id];
}
const document = { getElementById: id => els[id] || null };

// --- timers we control ------------------------------------------------
let queue = [];
const setTimeout_ = fn => { queue.push(fn); return queue.length; };
const clearTimeout_ = () => { queue = []; };
function drain(limit = 4000) {
  let n = 0;
  while (queue.length && n++ < limit) { const fn = queue.shift(); fn(); }
  if (n >= limit) throw new Error('timer queue did not settle -- the page is looping');
}

// --- the module, and a count of every heap reset the page asks for -----
let heapResets = 0;
const fetch_ = async () => ({ arrayBuffer: async () => readFileSync(wasmPath) });
const WebAssembly_ = {
  ...WebAssembly,
  instantiateStreaming: () => { throw new Error('no streaming in this harness'); },
  async instantiate(bytes, imports) {
    const res = await WebAssembly.instantiate(bytes, imports);
    const raw = res.instance.exports;
    // Wrap so the page's own __heap_reset calls are counted rather than
    // inferred. Every other export passes through untouched.
    const wrapped = Object.create(null);
    for (const k of Object.keys(raw)) {
      wrapped[k] = k === '__heap_reset'
        ? (...a) => { heapResets++; return raw[k](...a); }
        : raw[k];
    }
    return { instance: { exports: wrapped }, module: res.module };
  },
};

const ctx = vm.createContext({
  document, console,
  setTimeout: setTimeout_, clearTimeout: clearTimeout_,
  fetch: fetch_, WebAssembly: WebAssembly_, Date,
});
vm.runInContext(scripts[0], ctx);

els.status.textContent = 'Loading the engine…';
const realWait = ms => new Promise(r => globalThis.setTimeout(r, ms));
for (let i = 0; i < 200; i++) {
  if (els.p1.innerHTML || els.status.textContent.includes('did not load')) break;
  await realWait(25);
}

// --- reading what the visitor sees -------------------------------------
const steps = side => [...els['p' + side].innerHTML
  .matchAll(/<span class="step">([^<]*)<\/span>/g)].map(m => m[1]);
const openCount = side => [...els['p' + side].innerHTML
  .matchAll(/class="pc[^"]*\bopen\b[^"]*"/g)].length;
const status = () => els.status.textContent || '';
const run = expr => vm.runInContext(expr, ctx);

const fails = [];
const check = (label, ok, detail = '') => {
  console.log(`  ${ok ? 'ok  ' : 'FAIL'}  ${label}${detail ? ': ' + detail : ''}`);
  if (!ok) fails.push(label);
};

console.log(`ur-page-test ${pagePath}`);

check('the engine loaded', !status().includes('did not load'), status());
check('both sides render seven pieces',
      steps(1).length === 7 && steps(2).length === 7,
      `${steps(1).length} and ${steps(2).length}`);
check('a new game shows every piece waiting at 0',
      steps(1).every(s => s === '0') && steps(2).every(s => s === '0'),
      JSON.stringify(steps(1)));
check('the score line starts at none home',
      els.s1.textContent === '0 of 7 home', els.s1.textContent);

// -- Before a roll there is nothing to click ------------------------------
check('no piece is clickable before the dice are rolled', openCount(1) === 0, openCount(1));
check('the roll button is offered', els.rollbtn.disabled === false);
check('the status asks for a roll', status().includes('Roll the dice'), status());

// -- Rolling --------------------------------------------------------------
const resetsBeforeRoll = heapResets;
run('doRoll()');
const rolled = parseInt(els.roll.textContent, 10);
check('rolling shows a number between 0 and 4',
      Number.isFinite(rolled) && rolled >= 0 && rolled <= 4, els.roll.textContent);

if (rolled > 0 && openCount(1) > 0) {
  check('a legal piece becomes clickable', openCount(1) > 0, openCount(1));
  const before = JSON.stringify(steps(1));
  run('play(0)');
  check('clicking a piece moves it on the board',
        JSON.stringify(steps(1)) !== before, `${before} -> ${JSON.stringify(steps(1))}`);
} else {
  // A zero roll, or a roll nothing can use, must pass rather than stall.
  drain();
  check('a roll nothing can use passes the turn instead of stalling',
        status().length > 0, status());
}

// -- THE HEAP ARM ---------------------------------------------------------
// The page holds a handle into the module heap, so a reset between calls
// would free the board under it. Only newGame may reset.
check('the page did not reset the module heap to roll or move',
      heapResets === resetsBeforeRoll,
      `${heapResets - resetsBeforeRoll} resets during play`);

const resetsBeforeNew = heapResets;
run('newGame()');
check('starting a new game DOES reset the heap',
      heapResets === resetsBeforeNew + 1, `${heapResets - resetsBeforeNew}`);
check('a new game is back to every piece waiting',
      steps(1).every(s => s === '0'), JSON.stringify(steps(1)));

// -- Watch mode plays a whole game on its own -----------------------------
run('setMode(0)');
drain();
check('watch mode reaches a finished game', status().includes('wins'), status());
const winnerSide = status().includes('Player 1') ? 1 : 2;
check('the winner has borne off all seven pieces',
      els['s' + winnerSide].textContent === '7 of 7 home',
      `${els.s1.textContent} / ${els.s2.textContent}`);
check('watch mode offers nothing to click',
      openCount(1) === 0 && openCount(2) === 0);

// -- Control --------------------------------------------------------------
// L-FALSIF: prove the reader above can tell two different boards apart, or
// every board assertion in this file passed by being blind.
const finished = JSON.stringify(steps(1));
run('setMode(1)');
check('control: the reader notices a board that changed',
      JSON.stringify(steps(1)) !== finished,
      `${finished} -> ${JSON.stringify(steps(1))}`);

console.log(fails.length === 0
  ? `\nPASS: the shipped page drives the module correctly.`
  : `\nFAIL: ${fails.length} arm(s): ${fails.join('; ')}`);
process.exitCode = fails.length === 0 ? 0 : 1;
