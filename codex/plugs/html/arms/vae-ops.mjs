// Stage 3 of docs/Designs/Active/Apps/InBrowserDiffusion.md: BrowserVae's
// bv-norm (GroupNorm 32, eps 1e-6, SiLU) and bv-conv (3 x 3, pad 1) composed in
// a compiled page, codex/plugs/html/arms/VaeOpsArm.codex, in headless Edge,
// against the same arithmetic in f64 here. f32 on the device, so the bar is a
// relative 1e-3 of the output's largest magnitude. A control computes the
// reference without the SiLU and must miss.
// Usage: node codex/plugs/html/arms/vae-ops.mjs   Exit 0 = every arm passed.
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

const val = (n, s) => Array.from({ length: n }, (_, i) => Math.fround(((i * s) % 128) / 64 - 1));
const C = 64, H = 4, W = 4, HW = H * W, G = 32;
const reference = (silu) => {
  const x = val(C * HW, 41), gam = val(C, 37), bet = val(C, 11), wt = val(C * C * 9, 53), bias = val(C, 29);
  const len = C * HW / G, nrm = new Array(C * HW);
  for (let g = 0; g < G; g++) {
    let m = 0; for (let i = 0; i < len; i++) m += x[g * len + i]; m /= len;
    let v = 0; for (let i = 0; i < len; i++) v += (x[g * len + i] - m) ** 2; v /= len;
    const r = 1 / Math.sqrt(v + 1e-6);
    for (let i = 0; i < len; i++) { const k = g * len + i, ch = Math.floor(k / HW); let y = (x[k] - m) * r * gam[ch] + bet[ch]; if (silu) y = y / (1 + Math.exp(-y)); nrm[k] = y; }
  }
  const out = [];
  for (let co = 0; co < C; co++) for (let oy = 0; oy < H; oy++) for (let ox = 0; ox < W; ox++) {
    let a = bias[co];
    for (let ci = 0; ci < C; ci++) for (let ky = 0; ky < 3; ky++) for (let kx = 0; kx < 3; kx++) {
      const iy = oy + ky - 1, ix = ox + kx - 1;
      if (iy >= 0 && iy < H && ix >= 0 && ix < W) a += wt[((co * C + ci) * 3 + ky) * 3 + kx] * nrm[(ci * H + iy) * W + ix];
    }
    out.push(a);
  }
  return out;
};

const attnReference = (scaleOn) => {
  const x = val(C * HW, 41), gam = val(C, 37), bet = val(C, 11);
  const len = C * HW / G, hn = new Array(C * HW);
  for (let g = 0; g < G; g++) {
    let m = 0; for (let i = 0; i < len; i++) m += x[g * len + i]; m /= len;
    let v = 0; for (let i = 0; i < len; i++) v += (x[g * len + i] - m) ** 2; v /= len;
    const r = 1 / Math.sqrt(v + 1e-6);
    for (let i = 0; i < len; i++) { const k = g * len + i, ch = Math.floor(k / HW); hn[k] = (x[k] - m) * r * gam[ch] + bet[ch]; }
  }
  const lin = (wv, bv, inp) => { const w = val(C * C, wv), b = val(C, bv), o = new Array(C * HW); for (let co = 0; co < C; co++) for (let p = 0; p < HW; p++) { let a = b[co]; for (let ci = 0; ci < C; ci++) a += w[co * C + ci] * inp[ci * HW + p]; o[co * HW + p] = a; } return o; };
  const q = lin(13, 17, hn), k = lin(19, 23, hn), v = lin(31, 43, hn), sc = scaleOn ? 1 / Math.sqrt(512) : 1;
  const o = new Array(C * HW);
  for (let i = 0; i < HW; i++) {
    const s = []; for (let j = 0; j < HW; j++) { let a = 0; for (let c = 0; c < C; c++) a += q[c * HW + i] * k[c * HW + j]; s.push(a * sc); }
    const mx = Math.max(...s), e = s.map(z => Math.exp(z - mx)), sum = e.reduce((a, b) => a + b, 0);
    for (let c = 0; c < C; c++) { let a = 0; for (let j = 0; j < HW; j++) a += e[j] / sum * v[c * HW + j]; o[c * HW + i] = a; }
  }
  const pj = lin(47, 59, o);
  return pj.map((z, i) => z + x[i]);
};

let edge = null, server = null, work = null;
try {
  work = mkdtempSync(join(tmpdir(), 'vae-ops-arm-'));
  pwsh('build/bundle-app.ps1', ['-Src', join(repo, 'codex', 'plugs', 'html', 'arms', 'VaeOpsArm.codex'), '-Out', join(work, 'vo.codex')]);
  pwsh('codex/plugs/html/run.ps1', ['-Src', join(work, 'vo.codex'), '-Out', join(work, 'vo.html')]);
  pwsh('codex/plugs/wgsl/run.ps1', ['-Src', join(repo, 'codex', 'foreword', 'gpu', 'BrowserKernels.codex'), '-Out', join(work, 'bk.wgsl')]);
  const wgsl = readFileSync(join(work, 'bk.wgsl'), 'utf8');
  const html = readFileSync(join(work, 'vo.html'), 'utf8').replace('<script>', `<script>window.__DATA=${JSON.stringify({ bk: wgsl })};</script><script>`);
  const port = await freePort();
  server = http.createServer((q, r) => { r.writeHead(200, { 'Content-Type': 'text/html' }); r.end(html); });
  await new Promise(res => server.listen(port, '127.0.0.1', res));
  const dbg = await freePort();
  edge = spawn(edgePath, ['--headless=new', `--remote-debugging-port=${dbg}`, `--user-data-dir=${join(work, 'profile')}`, '--no-first-run', '--enable-unsafe-webgpu', 'about:blank'], { stdio: 'ignore' });
  let url = null;
  for (let i = 0; i < 60 && !url; i++) {
    try { const t = (await (await fetch(`http://127.0.0.1:${dbg}/json/list`)).json()).find(x => x.type === 'page' && x.webSocketDebuggerUrl); if (t) url = t.webSocketDebuggerUrl; } catch {}
    if (!url) await sleep(250);
  }
  if (!url) throw new Error('no CDP target');
  const ws = new WebSocket(url);
  await new Promise((res, rej) => { ws.addEventListener('open', res); ws.addEventListener('error', rej); });
  let id = 0; const waiting = new Map(); const pageErrors = [];
  const send = (method, params = {}) => new Promise((res) => { const i = ++id; waiting.set(i, res); ws.send(JSON.stringify({ id: i, method, params })); });
  ws.addEventListener('message', (ev) => {
    const m = JSON.parse(ev.data);
    if (m.id && waiting.has(m.id)) { waiting.get(m.id)(m); waiting.delete(m.id); }
    else if (m.method === 'Runtime.exceptionThrown') pageErrors.push(String(m.params.exceptionDetails.exception?.description || m.params.exceptionDetails.text).split('\n').slice(0, 4).join(' / '));
  });
  await send('Page.enable'); await send('Runtime.enable');
  const evalIn = async (expression) => { const r = await send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true }); return r.result?.result?.value; };
  await send('Page.navigate', { url: `http://127.0.0.1:${port}/` });
  for (let i = 0; i < 240; i++) { await sleep(250); try { if (await evalIn('typeof _st !== "undefined" && Number(_st.done) === 1')) break; } catch {} }
  const st = await evalIn('JSON.stringify(Object.fromEntries(Object.entries(typeof _st !== "undefined" ? _st : {}).map(([k, v]) => [k, typeof v === "bigint" ? Number(v) : v])))').then(JSON.parse);
  const asF = v => new Float32Array(new Int32Array([v]).buffer)[0];
  let y = []; try { y = JSON.parse(st.y).map(asF); } catch {}
  const errOf = (ref) => { const big = Math.max(...ref.map(Math.abs)); return y.length === ref.length ? Math.max(...ref.map((v, i) => Math.abs(v - y[i]))) / big : Infinity; };
  const e = errOf(reference(true)), ec = errOf(reference(false));
  ok('bv-norm then bv-conv run with no WebGPU error', st.ok === 1 && st.err === '', `ok ${st.ok}, error '${st.err}'${pageErrors.length ? '; page errors: ' + pageErrors.join(' | ') : ''}`);
  ok('the result matches GroupNorm + SiLU + conv in f64 within 1e-3 of its largest magnitude', e < 1e-3, `relative error ${e.toExponential(2)}`);
  ok('control, the reference without SiLU, misses', ec > 1e-2, `relative error ${ec.toExponential(2)}`);
  let at = []; try { at = JSON.parse(st.attn).map(asF); } catch {}
  const errAt = (ref) => { const big = Math.max(...ref.map(Math.abs)); return at.length === ref.length ? Math.max(...ref.map((v, i) => Math.abs(v - at[i]))) / big : Infinity; };
  const ea = errAt(attnReference(true)), eac = errAt(attnReference(false));
  ok('bv-attention matches the decoder attention in f64 within 1e-3 of its largest magnitude', ea < 1e-3, `relative error ${ea.toExponential(2)}`);
  ok('control, the reference without the 1/sqrt(512) scale, misses', eac > 1e-3, `relative error ${eac.toExponential(2)}`);
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
