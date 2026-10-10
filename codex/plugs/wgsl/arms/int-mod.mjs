// Run IntModArm's WGSL on WebGPU in headless Edge: int-mod must answer the Euclidean
// remainder x86-64 gives (in [0, |b|)), where WGSL's own % keeps the dividend's sign.
// The control zeroes the correction and must miss exactly the pairs whose truncated
// remainder is negative.
// Usage: node codex/plugs/wgsl/arms/int-mod.mjs   Exit 0 = every arm passed.
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
  ws.addEventListener('message', (ev) => { const m = JSON.parse(ev.data); if (m.id && waiting.has(m.id)) { waiting.get(m.id)(m); waiting.delete(m.id); } });
  const send = (method, params = {}) => new Promise((res) => { const i = ++id; waiting.set(i, res); ws.send(JSON.stringify({ id: i, method, params })); });
  return async (expression) => {
    const r = await send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true });
    if (r.result?.exceptionDetails) throw new Error(JSON.stringify(r.result.exceptionDetails).slice(0, 400));
    return r.result?.result?.value;
  };
}

// Binding 0 is ab (the pairs) and binding 1 is yb, the emitter's discovery order for im-mod.
const runInPage = `async (code, pairs) => {
  const a = await navigator.gpu.requestAdapter(); const d = await a.requestDevice();
  const errs = [];
  const m = d.createShaderModule({ code });
  for (const x of (await m.getCompilationInfo()).messages) if (x.type === 'error') errs.push(x.message);
  if (errs.length) { d.destroy(); return { errs, out: [] }; }
  d.pushErrorScope('validation');
  const n = pairs.length / 2, S = GPUBufferUsage.STORAGE;
  const x = d.createBuffer({ size: n * 8, usage: S | GPUBufferUsage.COPY_DST }); d.queue.writeBuffer(x, 0, new Int32Array(pairs));
  const y = d.createBuffer({ size: n * 4, usage: S | GPUBufferUsage.COPY_SRC });
  const r = d.createBuffer({ size: n * 4, usage: GPUBufferUsage.MAP_READ | GPUBufferUsage.COPY_DST });
  const p = d.createComputePipeline({ layout: 'auto', compute: { module: m, entryPoint: 'im_mod_main' } });
  const bg = d.createBindGroup({ layout: p.getBindGroupLayout(0), entries: [{ binding: 0, resource: { buffer: x } }, { binding: 1, resource: { buffer: y } }] });
  const e = d.createCommandEncoder(); const c = e.beginComputePass(); c.setPipeline(p); c.setBindGroup(0, bg); c.dispatchWorkgroups(Math.ceil(n / 64)); c.end();
  e.copyBufferToBuffer(y, 0, r, 0, n * 4); d.queue.submit([e.finish()]);
  const v = await d.popErrorScope(); if (v) errs.push(v.message);
  await r.mapAsync(GPUMapMode.READ); const out = Array.from(new Int32Array(r.getMappedRange())); r.unmap(); d.destroy();
  return { errs, out };
}`;

let edge = null, server = null, work = null;
try {
  work = mkdtempSync(join(tmpdir(), 'wgsl-intmod-'));
  const wgslFile = join(work, 'intmod.wgsl');
  execFileSync('pwsh', ['-NoProfile', '-File', join(repo, 'codex', 'plugs', 'wgsl', 'run.ps1'), '-Src', join(repo, 'codex', 'plugs', 'wgsl', 'arms', 'IntModArm.codex'), '-Out', wgslFile], { stdio: 'inherit' });
  const code = readFileSync(wgslFile, 'utf8');
  ok('int-mod lowers to an expression, not a call', !/int_mod\(/.test(code) && /select\(0, abs\(/.test(code));

  const divisors = [-7, -3, -1, 1, 3, 7, 1000003, -1000003];
  const pairs = [], want = [];
  let negatives = 0;
  for (let a = -13; a <= 13; a++) for (const b of divisors) {
    pairs.push(a, b);
    const t = a % b;
    if (t < 0) negatives++;
    want.push(t < 0 ? t + Math.abs(b) : t);
  }

  const port = await freePort();
  server = http.createServer((q, r) => { r.writeHead(200, { 'Content-Type': 'text/html' }); r.end('<!doctype html><title>intmod</title>'); });
  await new Promise(res => server.listen(port, '127.0.0.1', res));
  const dbg = await freePort();
  edge = spawn(edgePath, ['--headless=new', `--remote-debugging-port=${dbg}`, `--user-data-dir=${join(work, 'profile')}`, '--no-first-run', '--enable-unsafe-webgpu', `http://127.0.0.1:${port}/`], { stdio: 'ignore' });
  const evaluate = await cdpOpen(dbg);
  for (let i = 0; i < 40 && !(await evaluate('!!navigator.gpu')); i++) await sleep(250);
  const run = (src) => evaluate(`(${runInPage})(${JSON.stringify(src)}, ${JSON.stringify(pairs)})`);

  const real = await run(code);
  ok('the module compiles on WebGPU', real.errs.length === 0, real.errs.join('; ').slice(0, 200));
  const bad = real.out.filter((v, i) => v !== want[i]).length;
  ok('int-mod answers the Euclidean remainder for every pair', real.errs.length === 0 && bad === 0, `${bad} of ${want.length} differ`);

  const truncated = await run(code.split('select(0, abs(').join('select(0, 0 * abs('));
  const missed = truncated.out.filter((v, i) => v !== want[i]).length;
  ok('control: without the correction exactly the negative truncated remainders differ', truncated.errs.length === 0 && missed === negatives, `${missed} differ, ${negatives} predicted`);
} catch (e) {
  ok('the arm ran', false, String(e && e.message || e));
} finally {
  if (edge) { try { edge.kill(); } catch {} }
  if (server) server.close();
  await sleep(1000);
  if (work) {
    try { execFileSync('pwsh', ['-NoProfile', '-Command', `Get-CimInstance Win32_Process -Filter "Name='msedge.exe'" | Where-Object { $_.CommandLine -like '*${work.replace(/'/g, "''")}*' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }`], { stdio: 'ignore' }); } catch {}
    try { rmSync(work, { recursive: true, force: true }); } catch {}
  }
}
const failed = arms.filter(a => !a).length;
console.log(failed === 0 ? `\nPASS: ${arms.length} arms` : `\nFAIL: ${failed} of ${arms.length} arms`);
process.exit(failed === 0 ? 0 : 1);
