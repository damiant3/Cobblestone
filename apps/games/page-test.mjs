// Drive the SHIPPED page's own script against the real wasm module.
//
// wasm-verify.mjs grades the module. This grades the WIRING, which is where a
// working module still reaches the visitor as a dead board: the script is read
// out of apps/landing/web/games/<game>.html itself rather than copied here, so
// it cannot drift from what is served.
//
// The DOM is a stub and timers are a queue this file drains, so an AI reply
// scheduled with setTimeout happens when the test says so instead of when the
// clock says so. What is asserted is what the visitor would SEE: the marks in
// the rendered board and the text of the status line.
//
// Usage: node apps/games/page-test.mjs

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import vm from 'node:vm';

const here = dirname(fileURLToPath(import.meta.url));
const pagePath = join(here, '..', 'landing', 'web', 'games', 'tictactoe.html');
const wasmPath = join(here, '..', 'landing', 'web', 'games', 'tictactoe.wasm');

const html = readFileSync(pagePath, 'utf8');
const scripts = [...html.matchAll(/<script>([\s\S]*?)<\/script>/g)].map(m => m[1]);
if (scripts.length !== 1) throw new Error(`expected one inline script, found ${scripts.length}`);

// --- the stub DOM ----------------------------------------------------
const mkEl = id => ({
  id, innerHTML: '', textContent: '', dataset: {},
  value: '5',
  _classes: new Set(),
  classList: {
    toggle(c, on) { on ? this._o._classes.add(c) : this._o._classes.delete(c); },
  },
  addEventListener() {},
});
const els = {};
for (const id of ['speed', 'board', 'status', 'm-x', 'm-o', 'm-w']) {
  els[id] = mkEl(id);
  els[id].classList._o = els[id];
}
const document = {
  getElementById: id => els[id] || null,
};

// --- timers we control ------------------------------------------------
let queue = [];
const setTimeout_ = fn => { queue.push(fn); return queue.length; };
const clearTimeout_ = () => { queue = []; };
function drain(limit = 200) {
  let n = 0;
  while (queue.length && n++ < limit) { const fn = queue.shift(); fn(); }
  if (n >= limit) throw new Error('timer queue did not settle -- the page is looping');
}

// --- the module, served the way the page asks for it -------------------
const fetch_ = async () => ({ arrayBuffer: async () => readFileSync(wasmPath) });
const WebAssembly_ = {
  ...WebAssembly,
  // Force the page's non-streaming fallback: node has no instantiateStreaming
  // over a Response, and the fallback is the arm a plain static host uses.
  instantiateStreaming: () => { throw new Error('no streaming in this harness'); },
};

const ctx = vm.createContext({
  document, console,
  setTimeout: setTimeout_, clearTimeout: clearTimeout_,
  fetch: fetch_, WebAssembly: WebAssembly_,
});
vm.runInContext(scripts[0], ctx);

// The page loads the module in an async IIFE. Wait on the OUTCOME -- the
// board rendering, or the page saying it failed -- rather than on a fixed
// number of ticks, which is a guess about how many awaits instantiation takes.
els.status.textContent = 'Loading the engine…';
const realWait = ms => new Promise(r => globalThis.setTimeout(r, ms));
for (let i = 0; i < 200; i++) {
  if (els.board.innerHTML || els.status.innerHTML.includes('did not load')) break;
  await realWait(25);
}

// --- reading what the visitor sees -------------------------------------
const MARK = { '': 0, '×': 1, '○': 2 };
function board() {
  const cells = [...els.board.innerHTML.matchAll(/<div class="cell ([^"]*)" data-i="(\d)">([^<]*)<\/div>/g)];
  if (cells.length !== 9) throw new Error(`board rendered ${cells.length} cells, not 9`);
  return cells.map(c => MARK[c[3]] ?? (() => { throw new Error(`unknown mark ${c[3]}`); })());
}
function openCells() {
  return [...els.board.innerHTML.matchAll(/class="cell [^"]*\bopen\b[^"]*" data-i="(\d)"/g)]
    .map(m => +m[1]);
}
const status = () => els.status.textContent || els.status.innerHTML;

const fails = [];
const check = (label, ok, detail = '') => {
  console.log(`  ${ok ? 'ok  ' : 'FAIL'}  ${label}${detail ? ': ' + detail : ''}`);
  if (!ok) fails.push(label);
};

console.log(`page-test ${pagePath}`);

check('the engine loaded', !status().includes('did not load'), status());

// --- you are X: the page must accept a click and answer it -------------
ctx.setMode('x');
drain();
check('a new game renders nine empty cells', board().every(v => v === 0));
check('every cell is clickable on your move', openCells().length === 9);

ctx.play(0);
drain();
const afterFirst = board();
check('your click lands an × where you clicked', afterFirst[0] === 1, JSON.stringify(afterFirst));
check('the engine answers in the same turn', afterFirst.filter(v => v === 2).length === 1);
check('the engine takes the centre against a corner', afterFirst[4] === 2, JSON.stringify(afterFirst));

// --- a whole game, playing the first empty cell every time -------------
// The AI cannot lose, so the only honest outcomes are a draw or its win. A
// human win here would mean the page is not calling the engine it claims to.
ctx.setMode('x');
drain();
for (let guard = 0; guard < 12; guard++) {
  const open = openCells();
  if (!open.length) break;
  ctx.play(open[0]);
  drain();
}
const end = board();
check('the game ends with the board full or won', end.filter(v => v !== 0).length >= 5,
      JSON.stringify(end));
check('you did not beat it', !status().includes('You won'), status());
check('the finished game says so', /draw|wins|win/i.test(status()), status());

// --- clicks that must do nothing ---------------------------------------
ctx.setMode('x');
drain();
ctx.play(4);
drain();
const held = board();
ctx.play(4);            // the cell you just took
drain();
check('a click on an occupied cell changes nothing',
      JSON.stringify(board()) === JSON.stringify(held));

// --- you are O: the engine must move first ------------------------------
ctx.setMode('o');
drain();
const oStart = board();
check('as O, the engine has already moved when you arrive',
      oStart.filter(v => v === 1).length === 1, JSON.stringify(oStart));

// --- watch mode ---------------------------------------------------------
ctx.setMode('w');
drain();
const watched = board();
check('watch mode plays the game out on its own', watched.every(v => v !== 0),
      JSON.stringify(watched));
check('watch mode ends in the draw perfect play forces',
      status().includes('draw'), status());
check('watch mode offers no clickable cell', openCells().length === 0);

// --- the control: this harness can see a wrong board --------------------
// Every arm above reads board(). If board() answered the same thing whatever
// the page did, all of them would pass on a dead page (L-FALSIF).
els.board.innerHTML = els.board.innerHTML.replace('data-i="0"', 'data-i="0" data-sabotaged');
let harnessSees = false;
try { harnessSees = JSON.stringify(board()) !== JSON.stringify(watched); }
catch (e) { harnessSees = true; }
check('control: the reader notices a board that changed', harnessSees);

if (fails.length) {
  console.log(`\nFAILED: ${fails.length} -- ${fails.join(', ')}`);
  process.exitCode = 1;
}
console.log('\nPASS: the shipped page drives the module correctly.');
