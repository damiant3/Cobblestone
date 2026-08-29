// Stage-4a arm: load the SHIPPED page's script (the artifact, not a copy) in a
// stub DOM, then drive the Claude wire with a CANNED SSE stream. No key, no
// network, no spend -- the only thing under test is the reader and the turn it
// assembles.
//
// THE PATH IS DERIVED, NOT HARDCODED. A fleet tool with a fixed workspace path
// silently reports another agent's artifact as yours (L-SHARED); this arm reads
// the page beside itself.
const fs = require('fs');
const vm = require('vm');
const path = require('path');

const pagePath = path.join(__dirname, 'page', 'prism.html');
const html = fs.readFileSync(pagePath, 'utf8');
const a = html.lastIndexOf('<script>');
const b = html.lastIndexOf('</script>');
if (a < 0 || b < a) { console.log('FAIL: no script block in ' + pagePath); process.exit(1); }
const code = html.slice(a + 8, b);

function el() {
  const e = {
    style: {}, dataset: {}, children: [],
    classList: { add() {}, remove() {}, contains() { return false; } },
    addEventListener() {}, appendChild(c) { this.children.push(c); }, remove() {},
    setAttribute() {}, querySelector() { return null; }, closest() { return null; },
    click() {}, innerHTML: '', textContent: '', value: '', disabled: false,
    scrollTop: 0, scrollLeft: 0, files: [], title: ''
  };
  return e;
}
const byId = {};
const store = {};
const win = {};
// WebAssembly is NOT injected: a vm context has its own, and the page's
// runModule asks `inputTextOrBytes instanceof Uint8Array`, which is FALSE for a
// Uint8Array made in another realm. Injecting the host's WebAssembly and the
// host's TextEncoder put the wasm memory and the encoded input in the host
// realm, the instanceof went false, and the page stringified the byte array
// into "73,82,45,..." -- the compiler answered `no input mode on stdin` in
// three milliseconds, which reads exactly like a compiler that refused the
// program. `__hostEnc`/`__hostDec` below cross the boundary as plain arrays and
// are re-wrapped by the CONTEXT's Uint8Array, so every object the page touches
// belongs to one realm.
const sandbox = {
  console, __hostEnc: (s) => Array.from(new TextEncoder().encode(s)),
  __hostDec: (a) => new TextDecoder().decode(Uint8Array.from(a)),
  performance, URL,
  setTimeout, clearTimeout, Promise, JSON, Math, Date,
  Blob: class { constructor() {} },
  document: {
    getElementById(id) { return byId[id] || (byId[id] = el()); },
    querySelectorAll() { return []; },
    createElement() { return el(); },
    body: el()
  },
  location: { protocol: 'https:' },
  navigator: {},
  window: win,
  localStorage: {
    getItem(k) { return Object.prototype.hasOwnProperty.call(store, k) ? store[k] : null; },
    setItem(k, v) { store[k] = String(v); },
    removeItem(k) { delete store[k]; }
  },
  prompt() { return null; }, confirm() { return false; }, alert() {},
  atob(s) { return Buffer.from(s, 'base64').toString('binary'); },
  btoa(s) { return Buffer.from(s, 'binary').toString('base64'); },
  fetch: null
};
sandbox.globalThis = sandbox;
sandbox.self = sandbox;

// THE REAL COMPILER, for the section that refuses to shim it. The module ships
// as base64 inside the DEPLOYED page's embed block and exists nowhere else on
// disk, so it is lifted from there and handed to the page through the page's
// own `window.__EMBED` mechanism, the same door build-page.ps1 uses. That copy
// was built by whatever seed deployed it, which is why the section below plants
// an error a compiler of ANY vintage refuses (an unresolved name) rather than
// anything near a language edge (L-SAMEVER).
//
// There is no skip. A module that cannot be found FAILS the arm: a section that
// quietly does not run reports the same green as one that ran and passed.
const deployed = path.join(__dirname, '..', '..', '..', 'apps', 'landing', 'web', 'compile', 'prism.html');
let compilerB64 = null;
if (fs.existsSync(deployed)) {
  const dep = fs.readFileSync(deployed, 'utf8');
  const k = dep.indexOf('"codex-compiler.wasm": "');
  if (k >= 0) {
    const s = k + '"codex-compiler.wasm": "'.length;
    const e = dep.indexOf('"', s);
    if (e > s) compilerB64 = dep.slice(s, e);
  }
}
if (!compilerB64) { console.log('FAIL: no codex-compiler.wasm embedded in ' + deployed); process.exit(1); }
win.__EMBED = { 'codex-compiler.wasm': compilerB64 };

vm.createContext(sandbox);
vm.runInContext(
  'globalThis.TextEncoder = class { encode(s) { return Uint8Array.from(__hostEnc(String(s))); } };' +
  'globalThis.TextDecoder = class { constructor() {} decode(b) { return b ? __hostDec(Array.from(b)) : ""; } };',
  sandbox, { filename: 'arm:realm-bootstrap' });
try { vm.runInContext(code, sandbox, { filename: 'prism.html:script' }); }
catch (e) { console.log('FAIL: page script threw: ' + e.message); process.exit(1); }

const C = win.__claude;
if (!C) { console.log('FAIL: page did not publish window.__claude'); process.exit(1); }

let fails = 0;
function check(name, got, want) {
  const g = JSON.stringify(got), w = JSON.stringify(want);
  if (g === w) { console.log('  ok   ' + name); }
  else { console.log('  FAIL ' + name + '\n         got  ' + g + '\n         want ' + w); fails++; }
}

// ---- 1. the request body, against the pinned shape -----------------------
console.log('request body:');
const body = C.claudeBody([{ role: 'user', content: 'hi' }], {});
check('model', body.model, 'claude-opus-5');
check('stream', body.stream, true);
check('thinking', body.thinking, { type: 'adaptive', display: 'summarized' });
check('no budget_tokens (a 400 on opus 5)', body.thinking.budget_tokens === undefined, true);
check('max_tokens', body.max_tokens, 64000);
const hdr = C.claudeHeaders('sk-test');
check('anthropic-version', hdr['anthropic-version'], '2023-06-01');
check('browser access header', hdr['anthropic-dangerous-direct-browser-access'], 'true');

// ---- 2. the reader, over a stream split at hostile boundaries ------------
// The whole point: the deltas below are cut MID-EVENT and mid-JSON, which is
// what a network chunk does and what a fixture of whole events never does.
console.log('sse reader, split chunks:');
const evs = [];
const rd = C.makeSseReader((e) => evs.push(e));
const sink = C.makeTurnSink();
const rd2 = C.makeSseReader((e) => sink.onEvent(e));

const chunks = [
  'event: message_start\ndata: {"type":"message_start","message":{"id":"msg_1"}}\n\n',
  'event: content_block_start\ndata: {"type":"content_block_start","index":0,"content_bl',   // split mid-JSON
  'ock":{"type":"text","text":""}}\n\n',
  // A TEXT delta split mid-JSON. This one is load-bearing: with the buffering
  // removed, "Hel" is lost outright and the assembled text becomes "lo". A
  // boundary that falls between the data line and the blank line is NOT enough
  // -- both halves still parse on their own, so the text survives a sabotage
  // and the assertion proves nothing. Found by sabotaging this arm.
  'event: content_block_delta\ndata: {"type":"content_block_delta","index":0,"delta":{"type":"text_de',
  'lta","text":"Hel"}}\n\nevent: content_block_delta\ndata: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"lo"}}\n\n',
  'event: content_block_stop\ndata: {"type":"content_block_stop","index":0}\n\n',
  'event: message_delta\ndata: {"type":"message_delta","delta":{"stop_reason":"end_turn"},"usage":{"output_tokens":12}}\n\n',
  'event: message_stop\ndata: {"type":"message_stop"}'   // no trailing blank line
];
for (const c of chunks) { rd.push(c); rd2.push(c); }
rd.flush(); rd2.flush();

// Seven, counted off the chunk list: message_start, content_block_start, two
// text deltas, content_block_stop, message_delta, message_stop. The eight this
// first said was a miscount of mine, not a reader defect.
check('event count', evs.length, 7);
check('assembled text', sink.state.text, 'Hello');
check('stop reason', sink.state.stopReason, 'end_turn');
check('usage carried', sink.state.usage, { output_tokens: 12 });
check('last event survived a missing trailing blank line', evs[evs.length - 1].type, 'message_stop');

// ---- 3. a refusal is an HTTP 200, not an exception -----------------------
console.log('refusal:');
const rsink = C.makeTurnSink();
const rrd = C.makeSseReader((e) => rsink.onEvent(e));
rrd.push('data: {"type":"message_delta","delta":{"stop_reason":"refusal","stop_details":{"type":"refusal","category":"cyber"}}}\n\n');
check('stop reason', rsink.state.stopReason, 'refusal');
check('category carried', rsink.state.refusal.category, 'cyber');

// ---- 4. a tool_use block arrives as streamed partial JSON ----------------
console.log('tool_use:');
const tsink = C.makeTurnSink();
const trd = C.makeSseReader((e) => tsink.onEvent(e));
trd.push('data: {"type":"content_block_start","index":0,"content_block":{"type":"tool_use","id":"tu_1","name":"read_file"}}\n\n');
trd.push('data: {"type":"content_block_delta","index":0,"delta":{"type":"input_json_delta","partial_json":"{\\"pa"}}\n\n');
trd.push('data: {"type":"content_block_delta","index":0,"delta":{"type":"input_json_delta","partial_json":"th\\":\\"a.codex\\"}"}}\n\n');
trd.push('data: {"type":"content_block_stop","index":0}\n\n');
check('one tool call', tsink.state.tools.length, 1);
check('name', tsink.state.tools[0].name, 'read_file');
check('input parsed from split JSON', tsink.state.tools[0].input, { path: 'a.codex' });

// ---- 5. the panel is inert without a key --------------------------------
console.log('key handling:');

// ---- the stage-4 UI and agent loop --------------------------------------
// Everything below drives the SHIPPED panel through the transport seam. The
// service call is the one thing this arm cannot make, so it is the one thing
// replaced: a scripted transport pushes canned SSE text at the page's own
// reader, and the reader, the sink, the tool executor, the loop and the panel
// are all the real ones.

function sse(o) { return 'event: ' + o.type + '\ndata: ' + JSON.stringify(o) + '\n\n'; }
function textTurn(text, stop) {
  return [
    sse({ type: 'message_start', message: { id: 'm' } }),
    sse({ type: 'content_block_start', index: 0, content_block: { type: 'text', text: '' } }),
    sse({ type: 'content_block_delta', index: 0, delta: { type: 'text_delta', text: text } }),
    sse({ type: 'content_block_stop', index: 0 }),
    sse({ type: 'message_delta', delta: { stop_reason: stop || 'end_turn' }, usage: { output_tokens: 5 } }),
    sse({ type: 'message_stop' })
  ];
}
// A tool turn as the API actually sends one under adaptive thinking: a thinking
// block WITH its signature, then the tool_use block, then stop_reason tool_use.
function toolTurn(name, input, withThinking) {
  const out = [sse({ type: 'message_start', message: { id: 'm' } })];
  let idx = 0;
  if (withThinking) {
    out.push(sse({ type: 'content_block_start', index: idx, content_block: { type: 'thinking', thinking: '' } }));
    out.push(sse({ type: 'content_block_delta', index: idx, delta: { type: 'thinking_delta', thinking: 'let me look' } }));
    out.push(sse({ type: 'content_block_delta', index: idx, delta: { type: 'signature_delta', signature: 'SIGa' } }));
    out.push(sse({ type: 'content_block_stop', index: idx }));
    idx++;
  }
  out.push(sse({ type: 'content_block_start', index: idx, content_block: { type: 'tool_use', id: 'tu_' + name, name: name } }));
  out.push(sse({ type: 'content_block_delta', index: idx, delta: { type: 'input_json_delta', partial_json: JSON.stringify(input) } }));
  out.push(sse({ type: 'content_block_stop', index: idx }));
  out.push(sse({ type: 'message_delta', delta: { stop_reason: 'tool_use' }, usage: { output_tokens: 7 } }));
  out.push(sse({ type: 'message_stop' }));
  return out;
}
// Every scripted transport cuts each event in half, at a boundary inside the
// JSON, for the reason section 2 gives: a fixture of whole events cannot fail.
function scripted(turns) {
  let i = 0;
  const seen = [];
  const t = async function (key, body, onChunk) {
    seen.push(body);
    const chunks = turns[Math.min(i, turns.length - 1)];
    i++;
    for (const c of chunks) {
      const cut = Math.max(1, Math.floor(c.length * 0.6));
      onChunk(c.slice(0, cut));
      onChunk(c.slice(cut));
    }
  };
  t.seen = seen;
  t.turns = () => i;
  return t;
}

async function main() {
  // Awaited, not sampled: reading a variable a .catch() will set later races
  // the assertion and reports the empty string as the answer.
  check('refuses with no key',
        await C.claudeStream([{ role: 'user', content: 'hi' }], {}, null).then(() => 'RESOLVED', (e) => e.message),
        'no key');
  C.setClaudeKey('sk-test');

  // ---- 6. a chat turn, end to end through the seam ----------------------
  console.log('chat turn:');
  const chat = scripted([textTurn('two plus two is four')]);
  C.setTransport(chat);
  const st = await C.claudeStream([{ role: 'user', content: 'what is 2+2' }], { system: 'sys' });
  check('assembled text', st.text, 'two plus two is four');
  check('stop reason', st.stopReason, 'end_turn');
  check('system carried to the wire', chat.seen[0].system, 'sys');
  check('ordered content', st.content, [{ type: 'text', text: 'two plus two is four' }]);

  // ---- 7. the tools, against the real project ---------------------------
  console.log('tools:');
  const W = C.workspace;
  W.addFile('a.codex', 'Chapter: A\n', false);
  const ls = await C.runAgentTool('list_files', {});
  check('list_files names the file', ls.text.indexOf('a.codex') >= 0, true);
  const rd = await C.runAgentTool('read_file', { path: 'a.codex' });
  check('read_file', rd.text, 'Chapter: A\n');
  check('read_file on a missing path is an error, not empty text',
        (await C.runAgentTool('read_file', { path: 'nope.codex' })).isError, true);
  check('run_lens refuses an unknown lens rather than absorbing it',
        await C.runAgentTool('run_lens', { plug: 'not-a-lens' }).then(() => 'no throw', (e) => e.message),
        'no such lens: not-a-lens');

  // The write guard. The refusal must change nothing AND say so: a guard that
  // answers is a guard no caller can tell fired (L-BAILVALUE).
  C.agentEnv.allowWrites = false;
  const refused = await C.runAgentTool('write_file', { path: 'a.codex', text: 'CLOBBERED' });
  check('write refused without approval', refused.isError, true);
  check('the refusal names itself', refused.text.indexOf('REFUSED') === 0, true);
  check('and the file is untouched', W.fileByPath('a.codex').text, 'Chapter: A\n');
  C.agentEnv.allowWrites = true;
  await C.runAgentTool('write_file', { path: 'a.codex', text: 'Chapter: A2\n' });
  check('write lands once approved', W.fileByPath('a.codex').text, 'Chapter: A2\n');
  await C.runAgentTool('write_file', { path: 'new.codex', text: 'Chapter: N\n' });
  check('write creates a file that did not exist', !!W.fileByPath('new.codex'), true);

  // ---- 8. the tool loop -------------------------------------------------
  console.log('agent loop:');
  const loop = scripted([toolTurn('read_file', { path: 'a.codex' }, true), textTurn('it says Chapter: A2')]);
  C.setTransport(loop);
  const msgs = [{ role: 'user', content: 'what does a.codex say' }];
  const tools = [];
  const fin = await C.agentRun(msgs, { tools: C.tools }, null, (t, r) => tools.push([t.name, r.text]));
  check('two turns were taken', loop.turns(), 2);
  check('the tool ran', tools, [['read_file', 'Chapter: A2\n']]);
  check('final text', fin.text, 'it says Chapter: A2');
  check('history: user, assistant, tool_result, assistant-final is not appended by the loop', msgs.length, 3);
  check('the assistant turn replayed its thinking block WITH the signature',
        msgs[1].content[0], { type: 'thinking', thinking: 'let me look', signature: 'SIGa' });
  check('and its tool_use block', msgs[1].content[1].type, 'tool_use');
  check('the tool result came back as a user turn',
        msgs[2].content[0], { type: 'tool_result', tool_use_id: 'tu_read_file', content: 'Chapter: A2\n' });
  check('tools reached the wire', loop.seen[0].tools.length, C.tools.length);

  // ---- 9. the planted error, and the control ----------------------------
  // The 4b acceptance shape. The compiler is SHIMMED here -- what this proves
  // is the loop, the diagnostic path and the edit, not the compiler, and a
  // real compile still waits on a browser.
  console.log('planted error:');
  W.addFile('broken.codex', 'Chapter: B\n\n  bad-syntax\n', false);
  C.agentEnv.compile = async () => {
    const f = W.fileByPath('broken.codex');
    const bad = f.text.indexOf('bad-syntax') >= 0;
    return { ok: !bad, decks: 12, diagnostics: bad ? ['broken.codex:3:3: error CDX3002: unresolved name bad-syntax'] : [], ir: bad ? null : 'IR' };
  };
  const fixTurns = [
    toolTurn('compile', {}, false),
    toolTurn('read_file', { path: 'broken.codex' }, false),
    toolTurn('write_file', { path: 'broken.codex', text: 'Chapter: B\n\n  good : Integer\n  good = 1\n' }, false),
    toolTurn('compile', {}, false),
    textTurn('fixed: the name was undefined, and it compiles clean now')
  ];
  C.agentEnv.allowWrites = true;
  C.setTransport(scripted(fixTurns));
  const seenText = [];
  await C.agentRun([{ role: 'user', content: 'fix the build' }], { tools: C.tools }, null, (t, r) => seenText.push(r.text));
  check('the first compile reported the planted diagnostic', seenText[0].indexOf('CDX3002') >= 0, true);
  check('the file was edited', W.fileByPath('broken.codex').text.indexOf('bad-syntax') < 0, true);
  check('the recompile is clean', seenText[3], 'compiled clean at decks=12');

  // The control, and it is the point: the SAME script with writes not approved
  // must leave the file broken. If this passes with writes off, the arm is
  // measuring the script rather than the tools.
  W.fileByPath('broken.codex').text = 'Chapter: B\n\n  bad-syntax\n';
  C.agentEnv.allowWrites = false;
  C.setTransport(scripted(fixTurns));
  const ctl = [];
  await C.agentRun([{ role: 'user', content: 'fix the build' }], { tools: C.tools }, null, (t, r) => ctl.push(r.text));
  check('control: the write was refused', ctl[2].indexOf('REFUSED') === 0, true);
  check('control: the file is still broken', W.fileByPath('broken.codex').text.indexOf('bad-syntax') >= 0, true);

  // ---- 10. the step cap refuses, it does not answer ----------------------
  console.log('step cap:');
  C.agentEnv.allowWrites = true;
  C.setTransport(scripted([toolTurn('list_files', {}, false)]));
  const capped = await C.agentRun([{ role: 'user', content: 'spin' }], { tools: C.tools }, null, null)
    .then(() => 'RETURNED A TURN', (e) => e.message);
  check('a loop that never finishes throws', capped, 'the agent loop reached its 24-step cap without finishing');

  // ---- 11. the panel ----------------------------------------------------
  console.log('panel:');
  const P = C.panel;
  P.setMode('agent');
  check('mode', P.mode(), 'agent');
  C.setTransport(scripted([textTurn('hello from the panel')]));
  P.input.value = 'hi';
  await P.submit();
  const log = P.log();
  const kinds = log.children.map(c => c.className);
  check('a user bubble and an assistant bubble landed', kinds, ['cl-msg cl-user', 'cl-msg cl-asst']);
  check('the assistant bubble carries the streamed text', log.children[1].textContent, 'hello from the panel');
  check('the turn is in the history', P.history().length, 2);
  check('the composer was cleared', P.input.value, '');

  // A refusal is an HTTP 200 with no text: the panel must say so rather than
  // render an empty bubble.
  C.setTransport(scripted([[
    sse({ type: 'message_start', message: { id: 'm' } }),
    sse({ type: 'message_delta', delta: { stop_reason: 'refusal', stop_details: { type: 'refusal', category: 'cyber' } } }),
    sse({ type: 'message_stop' })
  ]]));
  P.input.value = 'something declined';
  await P.submit();
  const last = P.log().children[P.log().children.length - 1];
  check('a refusal is reported, not rendered as an empty bubble', last.className, 'cl-msg cl-note');
  check('and it names the category', last.textContent.indexOf('cyber') >= 0, true);

  // ---- 12. the planted error against the REAL compiler ------------------
  // Section 9 shims `agentEnv.compile`, so it proves the loop and not that the
  // compiler answers. Here the shim is REMOVED: the page's own compileForAgent
  // runs the embedded module through runW, which falls back to the synchronous
  // path because this sandbox has no Worker. What the loop sees is a real
  // diagnostic, remapped to the project file's own line by the page's region
  // table, and a real clean compile after the edit.
  console.log('planted error, REAL compiler:');
  C.agentEnv.compile = null;
  C.agentEnv.lens = null;
  C.agentEnv.allowWrites = true;
  const W2 = C.workspace;
  W2.project().files = [];
  const GOOD = [
    'Chapter: Greeting', '', 'Section: Main', '',
    '  double : Integer -> Integer',
    '  double (n) = n * 2', '',
    '  opening : [Console] Nothing = act',
    '   print-line-uni (integer-to-text (double 21))',
    '  end', ''
  ].join('\n');
  // One character of damage, on the CALL only: the definition stays, so the
  // refusal is an unresolved name and nothing else.
  const BAD = GOOD.replace('(double 21)', '(doubl 21)');
  W2.addFile('main.codex', BAD, false);

  const realTurns = [
    toolTurn('compile', {}, false),
    toolTurn('read_file', { path: 'main.codex' }, false),
    toolTurn('write_file', { path: 'main.codex', text: GOOD }, false),
    toolTurn('compile', {}, false),
    textTurn('the call named doubl; the definition is double')
  ];
  C.setTransport(scripted(realTurns));
  const real = [];
  const t0 = Date.now();
  await C.agentRun([{ role: 'user', content: 'fix the build' }], { tools: C.tools }, null, (t, r) => real.push(r.text));
  const secs = ((Date.now() - t0) / 1000).toFixed(1);
  // "refused" ALONE is a weak assertion and this arm proved it: while the
  // sandbox was handing the module a stringified byte array, the compiler
  // answered `no input mode on stdin` and this first check passed. The CDX code
  // and the file coordinates are what carry the section.
  check('the real compiler refused the planted error', real[0].indexOf('the compiler refused') === 0, true);
  check('and raised a CDX diagnostic', /error CDX\d+/.test(real[0]), true);
  // The remap is the half a shim cannot test: raw diagnostics arrive in the
  // ASSEMBLED unit's coordinates, and the tool must answer in the file's.
  check('the diagnostic is in the project file own coordinates', /main\.codex:\d+:\d+:/.test(real[0]), true);
  check('the edit made it compile clean', real[3].indexOf('compiled clean at decks=') === 0, true);
  console.log('  (' + secs + 's for four real compiles through the embedded module)');

  // The control. Same script, writes not approved: the file must stay broken
  // and the second compile must still refuse. A green here would mean the
  // section was reading something other than the project.
  W2.project().files = [];
  W2.addFile('main.codex', BAD, false);
  C.agentEnv.allowWrites = false;
  C.setTransport(scripted(realTurns));
  const realCtl = [];
  await C.agentRun([{ role: 'user', content: 'fix the build' }], { tools: C.tools }, null, (t, r) => realCtl.push(r.text));
  check('control: the write was refused', realCtl[2].indexOf('REFUSED') === 0, true);
  check('control: the real compiler still refuses', realCtl[3].indexOf('the compiler refused') === 0, true);

  console.log(fails === 0 ? 'ARM OK' : ('ARM FAILED: ' + fails));
  process.exit(fails === 0 ? 0 : 1);
}

main().catch((e) => { console.log('FAIL: arm threw: ' + (e && e.stack || e)); process.exit(1); });
