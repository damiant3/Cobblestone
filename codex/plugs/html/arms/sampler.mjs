// Stage 3 of docs/Designs/Active/Apps/InBrowserDiffusion.md: Euler from
// SamplerGraph run in headless Edge over browser-sampler-ops
// (apps/diffusion/BrowserSampler.codex), driven by the compiled page
// codex/plugs/html/arms/SamplerArm.codex. The denoiser answers 0.5 x, so every
// value is exact in f32 and the answer must be 0.65625 x word for word. A
// control swaps bk_lincomb3's a and b in the module's cx-kernel line.
// Usage: node codex/plugs/html/arms/sampler.mjs   Exit 0 = every arm passed.
import { spawn, execFileSync } from 'node:child_process';
import { createServer } from 'node:net';
import http from 'node:http';
import { mkdtempSync, rmSync, readFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const repo = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..', '..', '..');
const edgePath = 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
const sleep = (ms) => new Promise(r => setTimeout(r, ms));
const freePort = () => new Promise((res) => { const s = createServer(); s.listen(0, '127.0.0.1', () => { const p = s.address().port; s.close(() => res(p)); }); });
const arms = [];
const ok = (name, pass, detail) => { arms.push(pass); console.log(`  ${pass ? 'ok  ' : 'FAIL'}  ${name}${detail ? ': ' + detail : ''}`); };
const pwsh = (script, args) => execFileSync('pwsh', ['-NoProfile', '-File', join(repo, script), ...args], { stdio: 'inherit' });

async function cdpOpen(port) {
  let url = null;
  for (let i = 0; i < 60 && !url; i++) {
    try { const t = (await (await fetch(`http://127.0.0.1:${port}/json/list`)).json()).find(x => x.type === 'page' && x.webSocketDebuggerUrl); if (t) url = t.webSocketDebuggerUrl; } catch {}
    if (!url) await sleep(250);
  }
  if (!url) throw new Error('no CDP target');
  const ws = new WebSocket(url);
  await new Promise((res, rej) => { ws.addEventListener('open', res); ws.addEventListener('error', rej); });
  let id = 0; const waiting = new Map();
  const send = (method, params = {}) => new Promise((res) => { const i = ++id; waiting.set(i, res); ws.send(JSON.stringify({ id: i, method, params })); });
  ws.addEventListener('message', (ev) => { const m = JSON.parse(ev.data); if (m.id && waiting.has(m.id)) { waiting.get(m.id)(m); waiting.delete(m.id); } });
  await send('Page.enable');
  const evalIn = async (expression) => {
    const r = await send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true });
    if (r.result?.exceptionDetails) throw new Error(JSON.stringify(r.result.exceptionDetails).slice(0, 400));
    return r.result?.result?.value;
  };
  return { send, evalIn };
}

let edge = null, server = null, work = null;
try {
  work = mkdtempSync(join(tmpdir(), 'sampler-arm-'));
  pwsh('build/bundle-app.ps1', ['-Src', join(repo, 'codex', 'plugs', 'html', 'arms', 'SamplerArm.codex'), '-Out', join(work, 'sa.codex')]);
  pwsh('codex/plugs/html/run.ps1', ['-Src', join(work, 'sa.codex'), '-Out', join(work, 'sa.html')]);
  pwsh('codex/plugs/wgsl/run.ps1', ['-Src', join(repo, 'codex', 'foreword', 'gpu', 'BrowserKernels.codex'), '-Out', join(work, 'bk.wgsl')]);
  const html = readFileSync(join(work, 'sa.html'), 'utf8'), wgsl = readFileSync(join(work, 'bk.wgsl'), 'utf8');

  const N = 256, x0 = Array.from({ length: N }, (_, i) => -2 + i / 64), want = x0.map(v => 0.65625 * v);
  const swapped = wgsl.replace(/(\/\/ cx-kernel bk_lincomb3_main [^\n]*)/, (line) => line.replace(' a=u0', ' a=TMP').replace(' b=u1', ' b=u0').replace(' a=TMP', ' a=u1'));
  const page = (code) => html.replace('<script>', `<script>window.__DATA=${JSON.stringify({ bk: code })};</script><script>`);
  const port = await freePort();
  server = http.createServer((q, r) => { r.writeHead(200, { 'Content-Type': 'text/html' }); r.end(q.url.startsWith('/control') ? page(swapped) : page(wgsl)); });
  await new Promise(res => server.listen(port, '127.0.0.1', res));
  const dbg = await freePort();
  edge = spawn(edgePath, ['--headless=new', `--remote-debugging-port=${dbg}`, `--user-data-dir=${join(work, 'profile')}`, '--no-first-run', '--enable-unsafe-webgpu', 'about:blank'], { stdio: 'ignore' });
  const { send, evalIn } = await cdpOpen(dbg);

  const runPage = async (path) => {
    await send('Page.navigate', { url: `http://127.0.0.1:${port}${path}` });
    for (let i = 0; i < 240; i++) { await sleep(250); try { if (await evalIn('typeof _st !== "undefined" && Number(_st.done) === 1')) break; } catch {} }
    return evalIn('JSON.stringify(Object.fromEntries(Object.entries(typeof _st !== "undefined" ? _st : {}).map(([k, v]) => [k, typeof v === "bigint" ? Number(v) : v])))').then(JSON.parse);
  };
  const asF = v => new Float32Array(new Int32Array([v]).buffer)[0];
  const xBad = (text, ref) => { let a; try { a = JSON.parse(text); } catch { return N; } if (!Array.isArray(a)) return N; return ref.reduce((bad, v, i) => bad + (asF(a[i]) === v ? 0 : 1), 0); };

  const st = await runPage('/');
  let open = {}; try { open = JSON.parse(st.open || '{}'); } catch {}
  ok('the page opens a WebGPU device', open.ok === true, st.open);
  ok('the denoiser answers 0.5 x exactly', xBad(st.y, x0.map(v => 0.5 * v)) === 0, `${xBad(st.y, x0.map(v => 0.5 * v))} of ${N} differ; first words ${String(st.y).slice(0, 80)}`);
  ok('Euler reports no refused step and WebGPU no error', st.bad === 0 && st.err === '' && st.err2 === '', `bad ${st.bad}; error '${st.err}', after the read '${st.err2}'`);
  ok('Euler over browser-sampler-ops answers 0.65625 x exactly', xBad(st.x, want) === 0, `${xBad(st.x, want)} of ${N} differ`);
  ok('the answer is not the input (the steps ran)', xBad(st.x, x0) > 0, `${xBad(st.x, x0)} of ${N} differ from x`);

  const ct = await runPage('/control');
  ok('control, bk_lincomb3 a and b swapped in the cx-kernel line, is caught', swapped !== wgsl && xBad(ct.x, want) > 0, `${xBad(ct.x, want)} of ${N} differ`);
} catch (e) {
  ok('the arm ran', false, String(e && e.message || e));
} finally {
  if (edge) { try { edge.kill(); } catch {} }
  if (server) server.close();
  if (work) {
    try { execFileSync('pwsh', ['-NoProfile', '-Command', `Get-CimInstance Win32_Process -Filter "Name='msedge.exe'" | Where-Object { $_.CommandLine -like '*${work.replace(/'/g, "''")}*' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }`]); } catch {}
    await sleep(500); try { rmSync(work, { recursive: true, force: true }); } catch {}
  }
}
const failed = arms.filter(p => !p).length;
console.log(failed === 0 ? `PASS: ${arms.length} arms` : `FAIL: ${failed} of ${arms.length} arms`);
process.exit(failed === 0 ? 0 : 1);
