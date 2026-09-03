// Validate EVERY WGSL shader gpushow ships, against Chrome's real WGSL
// compiler, in one headless Chrome and one browser launch.
//
// Why this exists and why it is not validate.mjs in a loop. Each demo page
// creates TWO shader modules and only one of them is a file: the compute
// kernel is fetched from /kernels/<Name>.wgsl, and the render shader is a
// template literal inside the page. Validating kernels/*.wgsl therefore
// measures roughly half of what a visitor's browser compiles, and nothing
// says so. This drives the pages and records every module the page actually
// creates, so the population is what ships rather than what happens to be a
// file (L-ARTIFACT, L-DENOM).
//
// Kernels no page fetches are still validated, standalone, so a file cannot
// hide by being unreferenced.
//
// Usage:
//   node validate-all.mjs [--only <substr>] [--chrome <path>] [--keep]
// Exit 0 = every shader compiled clean, 1 = at least one WGSL error,
//      2 = could not run at all (no Chrome, no adapter, no CDP).

import { spawn } from 'node:child_process';
import { readFileSync, readdirSync, mkdtempSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname, basename } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createServer } from 'node:net';

const here = dirname(fileURLToPath(import.meta.url));
const appRoot = join(here, '..');
const webDir = join(appRoot, 'web');
const kernelDir = join(appRoot, 'kernels');

const argv = process.argv.slice(2);
const argOf = (name) => { const i = argv.indexOf(name); return i >= 0 ? argv[i + 1] : null; };
const only = argOf('--only');
const keep = argv.includes('--keep');
const chromePath = argOf('--chrome')
  || 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe';

const sleep = (ms) => new Promise(r => setTimeout(r, ms));

// A fixed port makes a fleet peer's browser answer your query, and makes two
// runs on one box collide (L-SHARED). Ask the OS for a free one instead.
function freePort() {
  return new Promise((resolve, reject) => {
    const s = createServer();
    s.on('error', reject);
    s.listen(0, '127.0.0.1', () => { const p = s.address().port; s.close(() => resolve(p)); });
  });
}

// -- the hook, installed before any page script runs --------------------------
// Wraps createShaderModule so every module the page builds is recorded with
// its compilation info, whatever the source was.
const HOOK = `
(() => {
  const st = { modules: [], adapter: null, pageError: null, gpu: (typeof navigator !== 'undefined' && !!navigator.gpu) };
  globalThis.__wgsl = st;
  addEventListener('error', (e) => { if (!st.pageError) st.pageError = String(e.message || e); });
  addEventListener('unhandledrejection', (e) => { if (!st.pageError) st.pageError = String((e.reason && e.reason.message) || e.reason); });
  if (typeof GPUAdapter !== 'undefined' && navigator.gpu) {
    const ra = navigator.gpu.requestAdapter.bind(navigator.gpu);
    navigator.gpu.requestAdapter = async function (...a) {
      const ad = await ra(...a);
      if (ad) { try { st.adapter = ad.info ? (ad.info.vendor + ' ' + ad.info.architecture) : 'unknown'; } catch { st.adapter = 'unknown'; } }
      return ad;
    };
  }
  if (typeof GPUDevice !== 'undefined') {
    const orig = GPUDevice.prototype.createShaderModule;
    GPUDevice.prototype.createShaderModule = function (desc) {
      const mod = orig.call(this, desc);
      const code = (desc && desc.code) || '';
      const rec = { label: (desc && desc.label) || '', bytes: code.length, settled: false, messages: [] };
      st.modules.push(rec);
      try {
        mod.getCompilationInfo().then((info) => {
          rec.messages = info.messages.map(m => ({ type: m.type, message: m.message, line: m.lineNum, col: m.linePos }));
          rec.settled = true;
        }).catch((e) => { rec.messages = [{ type: 'error', message: String(e), line: 0, col: 0 }]; rec.settled = true; });
      } catch (e) { rec.messages = [{ type: 'error', message: String(e), line: 0, col: 0 }]; rec.settled = true; }
      return mod;
    };
  }
})();
`;

const SNAPSHOT = `JSON.stringify({
  n: __wgsl.modules.length,
  pending: __wgsl.modules.filter(m => !m.settled).length,
  adapter: __wgsl.adapter,
  gpu: __wgsl.gpu,
  pageError: __wgsl.pageError,
  modules: __wgsl.modules.map(m => ({ label: m.label, bytes: m.bytes, messages: m.messages }))
})`;

// -- a minimal CDP client over the page target --------------------------------
class Cdp {
  constructor(ws) { this.ws = ws; this.id = 0; this.waiting = new Map(); this.events = []; }
  static async attach(port) {
    let url = null;
    for (let i = 0; i < 80; i++) {
      try {
        const r = await fetch(`http://127.0.0.1:${port}/json/list`);
        const t = (await r.json()).find(x => x.type === 'page' && x.webSocketDebuggerUrl);
        if (t) { url = t.webSocketDebuggerUrl; break; }
      } catch { /* not up yet */ }
      await sleep(250);
    }
    if (!url) throw new Error('CDP endpoint never came up');
    const ws = new WebSocket(url);
    await new Promise((res, rej) => { ws.addEventListener('open', res); ws.addEventListener('error', rej); });
    const c = new Cdp(ws);
    ws.addEventListener('message', (ev) => {
      const m = JSON.parse(ev.data);
      if (m.id && c.waiting.has(m.id)) { const { res, rej } = c.waiting.get(m.id); c.waiting.delete(m.id);
        if (m.error) rej(new Error(JSON.stringify(m.error))); else res(m.result); }
      else if (m.method) c.events.push(m.method);
    });
    return c;
  }
  send(method, params = {}) {
    const id = ++this.id;
    return new Promise((res, rej) => {
      this.waiting.set(id, { res, rej });
      this.ws.send(JSON.stringify({ id, method, params }));
      setTimeout(() => { if (this.waiting.has(id)) { this.waiting.delete(id); rej(new Error(method + ' timed out')); } }, 30000);
    });
  }
  async evalJson(expression) {
    const r = await this.send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true });
    if (r.exceptionDetails) throw new Error(r.exceptionDetails.text || 'eval threw');
    return JSON.parse(r.result.value);
  }
}

// Drive one URL and return every shader module it created.
async function drivePage(cdp, url) {
  cdp.events.length = 0;
  await cdp.send('Page.navigate', { url });
  for (let i = 0; i < 60 && !cdp.events.includes('Page.loadEventFired'); i++) await sleep(100);
  // The page's GPU init is async and we cannot know when it is finished, so
  // wait for the module count to stop growing with nothing left pending.
  let last = -1, stable = 0, snap = null;
  for (let i = 0; i < 80; i++) {
    await sleep(150);
    try { snap = await cdp.evalJson(SNAPSHOT); } catch { continue; }
    if (snap.n === last && snap.pending === 0) { stable++; if (stable >= 3) break; }
    else { stable = 0; last = snap.n; }
  }
  return snap;
}

// ---------------------------------------------------------------------------
const pages = readdirSync(webDir).filter(f => f.endsWith('.html'))
  .filter(f => !only || f.includes(only)).sort();
const kernels = readdirSync(kernelDir).filter(f => f.endsWith('.wgsl')).sort();

// Which kernels does some page fetch? Anything else is validated standalone.
const fetched = new Set();
for (const f of readdirSync(webDir).filter(x => x.endsWith('.html'))) {
  const html = readFileSync(join(webDir, f), 'utf8');
  for (const m of html.matchAll(/\/kernels\/([A-Za-z0-9_.-]+\.wgsl)/g)) fetched.add(m[1]);
}
const orphans = kernels.filter(k => !fetched.has(k)).filter(k => !only || k.includes(only));

console.log(`gpushow WGSL validation`);
console.log(`  pages:            ${pages.length}${only ? ` (filtered by "${only}")` : ''} of ${readdirSync(webDir).filter(f => f.endsWith('.html')).length}`);
console.log(`  kernel files:     ${kernels.length}, fetched by a page: ${fetched.size}, orphaned: ${orphans.length}`);

const httpPort = await freePort();
const cdpPort = await freePort();
const userDir = mkdtempSync(join(tmpdir(), 'gpushow-validate-'));

const server = spawn(process.execPath, [join(here, 'serve.mjs'), String(httpPort)], { stdio: 'ignore' });
const chrome = spawn(chromePath, [
  '--headless=new',
  `--remote-debugging-port=${cdpPort}`,
  '--remote-allow-origins=*',
  '--enable-unsafe-webgpu',
  '--enable-unsafe-swiftshader',
  '--enable-features=Vulkan,WebGPU',
  '--no-first-run', '--no-default-browser-check',
  `--user-data-dir=${userDir}`,
  `http://127.0.0.1:${httpPort}/web/index.html`,
], { stdio: 'ignore' });

let exitCode = 2;
let cdp = null;
try {
  await sleep(400);
  cdp = await Cdp.attach(cdpPort);
  await cdp.send('Page.enable');
  await cdp.send('Runtime.enable');
  await cdp.send('Page.addScriptToEvaluateOnNewDocument', { source: HOOK });

  let modulesSeen = 0, errorCount = 0, badPages = 0, adapter = null, noGpu = 0;
  const rows = [];

  for (const p of pages) {
    const snap = await drivePage(cdp, `http://127.0.0.1:${httpPort}/web/${p}`);
    if (!snap) { rows.push([p, 'NO SNAPSHOT', 0, 1]); badPages++; continue; }
    if (snap.adapter && !adapter) adapter = snap.adapter;
    if (!snap.gpu) noGpu++;
    const errs = snap.modules.flatMap(m => m.messages.filter(x => x.type === 'error').map(x => ({ ...x, label: m.label })));
    modulesSeen += snap.n;
    errorCount += errs.length;
    if (errs.length) badPages++;
    rows.push([p, errs.length ? 'INVALID' : (snap.n ? 'ok' : 'no shaders'), snap.n, errs.length]);
    for (const e of errs) console.log(`  [error] ${p}${e.label ? ' (' + e.label + ')' : ''} ${e.line}:${e.col} ${e.message}`);
    if (snap.pageError && !errs.length) console.log(`  [page]  ${p}: ${snap.pageError}`);
  }

  // Orphaned kernels: compile them directly, in the same browser.
  for (const k of orphans) {
    const code = readFileSync(join(kernelDir, k), 'utf8');
    const expr = `(async () => {
      const a = await navigator.gpu.requestAdapter(); if (!a) return JSON.stringify({ e: 'no adapter' });
      const d = await a.requestDevice();
      const info = await d.createShaderModule({ code: ${JSON.stringify(code)} }).getCompilationInfo();
      return JSON.stringify({ messages: info.messages.map(m => ({ type: m.type, message: m.message, line: m.lineNum, col: m.linePos })) });
    })()`;
    const r = JSON.parse(await cdp.evalJson(`(${expr}).then(x => JSON.stringify(x))`));
    const errs = (r.messages || []).filter(m => m.type === 'error');
    modulesSeen++; errorCount += errs.length; if (errs.length) badPages++;
    rows.push([k + ' (orphan)', errs.length ? 'INVALID' : 'ok', 1, errs.length]);
    for (const e of errs) console.log(`  [error] ${k} ${e.line}:${e.col} ${e.message}`);
  }

  console.log('');
  for (const [name, verdict, n, errs] of rows) {
    console.log(`  ${verdict === 'ok' ? ' ' : '!'} ${name.padEnd(28)} ${String(n).padStart(2)} module(s)  ${verdict}${errs ? ' (' + errs + ' error(s))' : ''}`);
  }
  console.log('');
  console.log(`adapter: ${adapter ?? '(none reported)'}`);
  console.log(`modules compiled: ${modulesSeen}, WGSL errors: ${errorCount}, pages/kernels affected: ${badPages}`);

  if (noGpu === pages.length && pages.length > 0) {
    console.log('WGSL VALIDATION COULD NOT RUN: navigator.gpu absent on every page');
    exitCode = 2;
  } else if (modulesSeen === 0) {
    console.log('WGSL VALIDATION COULD NOT RUN: no shader module was created anywhere');
    exitCode = 2;
  } else if (errorCount > 0) {
    console.log('WGSL INVALID');
    exitCode = 1;
  } else {
    console.log(`WGSL VALID: ${modulesSeen} modules, 0 errors`);
    exitCode = 0;
  }
} catch (e) {
  console.error('validate-all failed:', e.message || e);
  exitCode = 2;
} finally {
  try { chrome.kill(); } catch {}
  try { server.kill(); } catch {}
  if (!keep) { try { rmSync(userDir, { recursive: true, force: true }); } catch {} }
}
process.exit(exitCode);
