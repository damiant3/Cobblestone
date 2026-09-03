// Actually RUN the arcade page's script, against a stub browser.
//
// `node --check` parses. It does not execute, so it cannot see a name used
// above its own `let`, a call to something undefined, or any other error
// that only exists at run time. That gap shipped a page whose script threw
// on the first line it ran: the gallery read `cur` from the temporal dead
// zone, the module aborted, and the visitor got a page with no tabs and
// empty panels. Every check in the tree was green.
//
// So this loads index.html, pulls out its module script, gives it just
// enough of a browser to get through startup, and requires it to reach the
// end without throwing AND to have actually built its gallery. A page that
// renders nothing is not a page that passed.
//
// Usage: node apps/games/page-verify.mjs

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import { GAMES, CHESS, IMPORTS, driver, renderHtml, esc } from '../landing/web/games/arcade.js';
import { RULES } from '../landing/web/games/rules.js';

const here = dirname(fileURLToPath(import.meta.url));
const web = join(here, '..', 'landing', 'web', 'games');

let pass = 0, fail = 0;
const ok = (name, cond, detail) => {
  if (cond) { console.log(`  ok    ${name}`); pass++; }
  else { console.log(`  FAIL  ${name}${detail ? ': ' + detail : ''}`); fail++; }
};

// --- the smallest browser that startup needs -----------------------------
const made = new Map();
function el(id) {
  const e = {
    id, innerHTML: '', textContent: '', className: '', hidden: false,
    style: { setProperty() { }, cssText: '' },
    dataset: {},
    offsetWidth: 400, offsetHeight: 300, clientWidth: 400, clientHeight: 300,
    classList: { add() { }, remove() { }, toggle() { }, contains: () => false },
    addEventListener() { }, removeEventListener() { },
    setPointerCapture() { }, releasePointerCapture() { },
    appendChild() { }, remove() { }, closest: () => null,
    getBoundingClientRect: () => ({ left: 0, top: 0, width: 40, height: 40 }),
    querySelectorAll: () => [], querySelector: () => null,
    // The page assigns handlers to these; holding them is what lets the
    // check below prove the gallery wired itself up.
    onclick: null, oninput: null, onmousemove: null, onmouseleave: null,
  };
  return e;
}
const $ = id => { if (!made.has(id)) made.set(id, el(id)); return made.get(id); };

const document = {
  getElementById: $,
  querySelectorAll: () => [],
  addEventListener() { },
  elementFromPoint: () => null,
  createElement: () => el('new'),
};
const window = { innerWidth: 1400, innerHeight: 900, addEventListener() { } };
const location = { protocol: 'http:' };
const setTimeout_ = () => 0;

// Serve the modules off disk, so startup's first game really instantiates.
const fetchStub = async name => {
  const file = join(web, name.replace(/^.*\//, ''));
  try {
    const bytes = readFileSync(file);
    return { ok: true, status: 200, arrayBuffer: async () => bytes };
  } catch (err) {
    return { ok: false, status: 404, arrayBuffer: async () => new Uint8Array() };
  }
};

// --- run it --------------------------------------------------------------
const html = readFileSync(join(web, 'index.html'), 'utf8');
const m = /<script type="module">([\s\S]*?)<\/script>/.exec(html);
ok('index.html carries a module script', !!m);
if (!m) process.exit(1);

const body = m[1]
  .replace(/^\s*import\s+\{[^}]*\}\s+from\s+'\.\/arcade\.js';\s*$/m, '')
  .replace(/^\s*import\s+\{[^}]*\}\s+from\s+'\.\/rules\.js';\s*$/m, '');

const run = new Function(
  'GAMES', 'CHESS', 'IMPORTS', 'driver', 'renderHtml', 'esc', 'RULES',
  'document', 'window', 'location', 'fetch', 'setTimeout', 'WebAssembly', 'console',
  `return (async () => {\n${body}\n})();`);

let threw = null;
try {
  await run(GAMES, CHESS, IMPORTS, driver, renderHtml, esc, RULES,
    document, window, location, fetchStub, setTimeout_, WebAssembly, console);
  // select() is async; give its awaits a turn to settle.
  await new Promise(r => process.nextTick(r));
  await new Promise(r => process.nextTick(r));
} catch (err) {
  threw = err;
}

ok('the page script runs to the end without throwing', threw === null,
  threw && (threw.name + ': ' + threw.message));

// --- and produced something ---------------------------------------------
const side = $('side').innerHTML;
ok('the gallery renders the category tabs', /class="cats"/.test(side),
  'no .cats block in the sidebar');
ok('every category with games gets a tab',
  ['Board', 'Card', 'Puzzle', 'Dice', 'Strategy', 'Other']
    .filter(c => GAMES.some(g => g.cat === c))
    .every(c => side.includes(`data-cat="${c}"`)),
  'a category with games has no tab');
ok('the gallery lists games', (side.match(/class="pick/g) || []).length > 0,
  'no game buttons in the sidebar');
ok('the gallery lists only the chosen category',
  (side.match(/class="pick/g) || []).length < GAMES.length,
  'every game is listed at once, so the tabs are not filtering');
ok('the sidebar holds no undefined', !side.includes('undefined'));

console.log(fail === 0
  ? `\nPASS: the arcade page starts up and builds itself (${pass} arms).`
  : `\nFAIL: ${fail} of ${pass + fail} arms.`);
process.exitCode = fail === 0 ? 0 : 1;
