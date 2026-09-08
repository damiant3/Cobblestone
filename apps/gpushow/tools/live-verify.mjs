// Drive ONE demo page's live-compile block for real, in headless Chrome.
//
// validate-all.mjs grades the shaders a page creates. It cannot see whether the
// live block works, because a page whose block is broken still compiles the
// same two modules and still reports ok. This is the arm that watches the block
// actually compile, compare, accept an edit, and revert.
//
// The subject defaults to cube.html on purpose: CubeKernel cites TWO library
// chapters (DeviceEffect and DeviceMath), so it exercises the cites parsing.
// Plasma cites one, and a one-chapter subject would pass whether or not the
// block reads the cites at all.
//
// Usage: node live-verify.mjs [page.html] [--chrome <path>] [--keep]
// Exit 0 = every arm passed, 1 = an arm failed, 2 = could not run at all.

import { spawn } from 'node:child_process';
import { createServer } from 'node:net';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const argOf = (n) => { const i = process.argv.indexOf(n); return i > 0 ? process.argv[i + 1] : null; };
const page = (process.argv[2] && !process.argv[2].startsWith('--')) ? process.argv[2] : 'cube.html';
const chromePath = argOf('--chrome') || 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe';
const keep = process.argv.includes('--keep');

const freePort = () => new Promise((resolve) => {
  const s = createServer();
  s.listen(0, '127.0.0.1', () => { const p = s.address().port; s.close(() => resolve(p)); });
});

const arms = [];
const ok = (name, pass, detail) => { arms.push({ name, pass, detail }); console.log(`  ${pass ? 'ok  ' : 'FAIL'}  ${name}${detail ? ': ' + detail : ''}`); };

class Cdp {
  constructor(ws) { this.ws = ws; this.id = 0; this.waiting = new Map(); }
  static async open(port) {
    let url = null;
    for (let i = 0; i < 60 && !url; i++) {
      try {
        const r = await fetch(`http://127.0.0.1:${port}/json/list`);
        const t = (await r.json()).find(x => x.type === 'page' && x.webSocketDebuggerUrl);
        if (t) { url = t.webSocketDebuggerUrl; break; }
      } catch {}
      await new Promise(r => setTimeout(r, 250));
    }
    if (!url) throw new Error('no CDP target');
    const ws = new WebSocket(url);
    await new Promise((res, rej) => { ws.addEventListener('open', res); ws.addEventListener('error', rej); });
    const cdp = new Cdp(ws);
    ws.addEventListener('message', (ev) => {
      const m = JSON.parse(ev.data);
      if (m.id && cdp.waiting.has(m.id)) { cdp.waiting.get(m.id)(m); cdp.waiting.delete(m.id); }
    });
    return cdp;
  }
  send(method, params = {}) {
    const id = ++this.id;
    return new Promise((res) => { this.waiting.set(id, res); this.ws.send(JSON.stringify({ id, method, params })); });
  }
  async eval(expression) {
    const r = await this.send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true });
    if (r.result?.exceptionDetails) throw new Error(r.result.exceptionDetails.text || 'evaluate threw');
    return r.result?.result?.value;
  }
}

let chrome = null, server = null, profile = null;
try {
  const httpPort = await freePort();
  server = spawn(process.execPath, [join(here, 'serve.mjs'), String(httpPort)], { stdio: 'ignore' });
  await new Promise(r => setTimeout(r, 400));

  const cdpPort = await freePort();
  profile = mkdtempSync(join(tmpdir(), 'gpushow-live-'));
  chrome = spawn(chromePath, [
    '--headless=new', '--disable-gpu-sandbox', '--no-first-run', '--no-default-browser-check',
    '--enable-unsafe-webgpu', '--enable-features=Vulkan',
    `--remote-debugging-port=${cdpPort}`, `--user-data-dir=${profile}`, 'about:blank',
  ], { stdio: 'ignore' });

  const cdp = await Cdp.open(cdpPort);
  await cdp.send('Page.enable');
  await cdp.send('Runtime.enable');
  await cdp.send('Page.navigate', { url: `http://127.0.0.1:${httpPort}/web/${page}` });
  await new Promise(r => setTimeout(r, 1200));

  console.log(`live-verify: ${page}`);

  const present = await cdp.eval(`!!(document.getElementById('live') && window.__liveGo)`);
  ok('the page carries the live block', present === true);
  if (!present) throw new Error('no live block on the page');

  // ARM 1: compile the UNEDITED source. The claim is not merely that it
  // compiles: it must reproduce the .wgsl that shipped, which is what makes
  // this a comparison rather than a smoke test.
  await cdp.eval(`window.__live = null; window.__liveGo()`);
  const r1 = await cdp.eval(`(async () => { for (let i = 0; i < 240 && !window.__live; i++) await new Promise(r => setTimeout(r, 500)); return window.__live; })()`);
  ok('compiles the kernel in the tab', !!(r1 && r1.ok), r1 && r1.ok ? `decks=${r1.decks}` : (r1 && r1.error) || 'no result');
  ok('the lowered WGSL carries the entry point', !!(r1 && r1.ok && r1.wgsl && r1.wgsl.includes(r1.entry)), r1 && r1.entry);
  ok('unedited source reproduces the shipped .wgsl', !!(r1 && r1.same), r1 && r1.same ? 'identical' : 'DIFFERS from the shipped file');

  // ARM 2: an EDIT must reach the compiler and change the output. The edit is a
  // NUMBER, which is what the block's own text invites and what keeps the unit
  // valid Codex: a syntactically broken edit only proves the compiler refuses
  // it. Asserting the WGSL actually MOVED is what separates "the textarea is
  // wired to the compiler" from "the flag says edited".
  const original = await cdp.eval(`document.getElementById('lv-src').value`);
  const edit = await cdp.eval(`
    (() => {
      const t = document.getElementById('lv-src');
      if (t.value.indexOf('cu-half-w : Integer = 512') < 0) return 'anchor not found';
      t.value = t.value.replace('cu-half-w : Integer = 512', 'cu-half-w : Integer = 500');
      window.__live = null; window.__liveGo();
      return 'ok';
    })()
  `);
  ok('the edit anchor is present in the fetched source', edit === 'ok', edit);
  const r2 = await cdp.eval(`(async () => { for (let i = 0; i < 240 && !window.__live; i++) await new Promise(r => setTimeout(r, 500)); return window.__live; })()`);
  ok('an edited source is reported as edited', !!(r2 && r2.edited), r2 ? `edited=${r2.edited}${r2.refused ? ', refused: ' + (r2.diag || []).join(' ') : ''}` : 'no result');
  ok('the edit reaches the compiler and moves the WGSL',
     !!(r2 && r2.ok && r1 && r1.wgsl && r2.wgsl && r2.wgsl !== r1.wgsl),
     r2 && r2.ok ? (r2.wgsl === (r1 && r1.wgsl) ? 'output UNCHANGED by the edit' : 'output changed') : 'no output to compare');

  // ARM 3: revert restores the fetched source exactly.
  await cdp.eval(`document.getElementById('lv-rv').click()`);
  const after = await cdp.eval(`document.getElementById('lv-src').value`);
  ok('revert restores the original source', after === original,
     after === original ? `${after.length} chars` : `${after.length} chars vs ${original.length}`);

  const failed = arms.filter(a => !a.pass);
  console.log(`\n${failed.length ? 'FAIL' : 'PASS'}: ${arms.length - failed.length} of ${arms.length} arms on ${page}`);
  process.exitCode = failed.length ? 1 : 0;
} catch (e) {
  console.log(`could not run: ${e.message}`);
  process.exitCode = 2;
} finally {
  try { chrome?.kill(); } catch {}
  try { server?.kill(); } catch {}
  if (profile && !keep) { try { rmSync(profile, { recursive: true, force: true }); } catch {} }
}
