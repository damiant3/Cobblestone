// The image generator page in headless Edge, built here from
// apps/landing/ImageGenPage.codex through the html plug, so the code graded is
// the code shipped. It grades the plug's gpu-probe-then primitive through the
// page, in three states: the real GPU, a browser without WebGPU, and a GEMM
// whose shader is made to add one to every output, which the probe must report
// as wrong samples rather than as a speed.
// Usage: node apps/landing/test-imagegen.mjs   Exit 0 = every arm passed.
import { spawn, execFileSync } from 'node:child_process';
import { createServer } from 'node:net';
import http from 'node:http';
import { mkdtempSync, rmSync, readFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const repo = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..');
const edgePath = 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
const sleep = (ms) => new Promise(r => setTimeout(r, ms));
const freePort = () => new Promise((res) => { const s = createServer(); s.listen(0, '127.0.0.1', () => { const p = s.address().port; s.close(() => res(p)); }); });
const arms = [];
const ok = (name, pass, detail) => { arms.push(pass); console.log(`  ${pass ? 'ok  ' : 'FAIL'}  ${name}${detail ? ': ' + detail : ''}`); };

class Cdp {
  constructor(ws) { this.ws = ws; this.id = 0; this.waiting = new Map(); }
  static async open(port) {
    let url = null;
    for (let i = 0; i < 60 && !url; i++) {
      try { const t = (await (await fetch(`http://127.0.0.1:${port}/json/list`)).json()).find(x => x.type === 'page' && x.webSocketDebuggerUrl); if (t) url = t.webSocketDebuggerUrl; } catch {}
      if (!url) await sleep(250);
    }
    if (!url) throw new Error('no CDP target');
    const ws = new WebSocket(url);
    await new Promise((res, rej) => { ws.addEventListener('open', res); ws.addEventListener('error', rej); });
    const cdp = new Cdp(ws);
    ws.addEventListener('message', (ev) => { const m = JSON.parse(ev.data); if (m.id && cdp.waiting.has(m.id)) { cdp.waiting.get(m.id)(m); cdp.waiting.delete(m.id); } });
    return cdp;
  }
  send(method, params = {}) { const id = ++this.id; return new Promise((res) => { this.waiting.set(id, res); this.ws.send(JSON.stringify({ id, method, params })); }); }
  async eval(expression) {
    const r = await this.send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true });
    if (r.result?.exceptionDetails) throw new Error(JSON.stringify(r.result.exceptionDetails).slice(0, 400));
    return r.result?.result?.value;
  }
}

let edge = null, server = null, work = null;
try {
  work = mkdtempSync(join(tmpdir(), 'imagegen-'));
  const page = join(work, 'imagegen.html');
  execFileSync('pwsh', ['-NoProfile', '-File', join(repo, 'codex', 'plugs', 'html', 'run.ps1'), '-Src', join(repo, 'apps', 'landing', 'ImageGenPage.codex'), '-Out', page], { stdio: 'inherit' });
  const html = readFileSync(page);
  const port = await freePort();
  server = http.createServer((q, r) => { r.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' }); r.end(html); });
  await new Promise(res => server.listen(port, '127.0.0.1', res));
  const dbg = await freePort();
  edge = spawn(edgePath, ['--headless=new', `--remote-debugging-port=${dbg}`, `--user-data-dir=${join(work, 'profile')}`, '--no-first-run', '--enable-unsafe-webgpu', 'about:blank'], { stdio: 'ignore' });
  const cdp = await Cdp.open(dbg);
  await cdp.send('Page.enable');
  await cdp.send('Runtime.enable');

  // Load the page with an optional script run before it, click the check, and
  // wait for the verdict; answer the result box's text and the raw probe JSON.
  let hook = null;
  const run = async (before) => {
    if (hook) { await cdp.send('Page.removeScriptToEvaluateOnNewDocument', { identifier: hook }); hook = null; }
    if (before) hook = (await cdp.send('Page.addScriptToEvaluateOnNewDocument', { source: before })).result.identifier;
    await cdp.send('Page.navigate', { url: `http://127.0.0.1:${port}/` });
    for (let i = 0; i < 80 && !(await cdp.eval("!!document.getElementById('probe-btn')")); i++) await sleep(250);
    await cdp.eval("document.getElementById('probe-btn').click(),0");
    let text = '';
    for (let i = 0; i < 120; i++) { text = await cdp.eval("(document.getElementById('pres')||{}).textContent||''"); if (text && !text.startsWith('Checking')) break; await sleep(250); }
    const json = await cdp.eval('new Promise(r => gpu_probe_then(1000, t => r(t)))');
    return { text, o: JSON.parse(json) };
  };

  const real = await run(null);
  ok('the probe sees WebGPU and an adapter', real.o.webgpu === true && !real.o.err, JSON.stringify({ vendor: real.o.vendor, architecture: real.o.architecture, err: real.o.err }));
  ok('the GEMM is exact on every sampled output', real.o.checked === 64 && real.o.bad === 0, `${real.o.bad} of ${real.o.checked} wrong`);
  ok('the GEMM was timed', real.o.runs > 0 && real.o.gflops > 0, `${real.o.runs} runs, ${Math.round(real.o.gflops)} GFLOPS f32 at N=${real.o.n}`);
  ok('the page reports the card and the speed', /GFLOPS/.test(real.text) && (/native route/.test(real.text) || /no local route/.test(real.text)), real.text.slice(0, 160));

  const none = await run("Object.defineProperty(Navigator.prototype, 'gpu', { get: () => undefined });");
  ok('without WebGPU the probe says so', none.o.webgpu === false && none.o.err === 'no-webgpu', JSON.stringify(none.o));
  ok('without WebGPU the page says so', /does not offer WebGPU/.test(none.text), none.text.slice(0, 120));

  const broken = await run("{ const f = GPUDevice.prototype.createShaderModule; GPUDevice.prototype.createShaderModule = function (d) { return f.call(this, { ...d, code: d.code.replace('c[g.y*n+g.x]=s;', 'c[g.y*n+g.x]=s+1.0;') }); }; }");
  ok('control: a GEMM that adds one is caught on every sample', broken.o.bad === 64, `${broken.o.bad} of ${broken.o.checked} wrong`);
  ok('control: the page reports wrong results and no speed', /were wrong/.test(broken.text) && !/GFLOPS in f32/.test(broken.text), broken.text.slice(0, 160));
} catch (e) {
  ok('the arm ran', false, String(e && e.message || e));
} finally {
  if (edge) { try { edge.kill(); } catch {} }
  if (server) server.close();
  await sleep(1000);
  if (work) {
    // Headless Edge leaves helper processes behind; kill every one using this profile.
    try { execFileSync('pwsh', ['-NoProfile', '-Command', `Get-CimInstance Win32_Process -Filter "Name='msedge.exe'" | Where-Object { $_.CommandLine -like '*${work.replace(/'/g, "''")}*' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }`]); } catch {}
    try { rmSync(work, { recursive: true, force: true }); } catch {}
  }
}
const failed = arms.filter(a => !a).length;
console.log(failed === 0 ? `\nPASS: ${arms.length} arms` : `\nFAIL: ${failed} of ${arms.length} arms`);
process.exit(failed === 0 ? 0 : 1);
